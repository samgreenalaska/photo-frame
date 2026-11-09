// PhotoFrame.exe /watch: the background part, with a tray icon. Starts the
// slideshow once the PC has been idle long enough, dims the monitors while it
// plays, and (if asked) holds them at a set brightness the rest of the time.
//
// Why this starts the slideshow and Windows' screensaver does not: a screensaver
// that Windows itself launches can lock the PC when it ends, even with "On
// resume, display logon screen" off (seen on Windows 11 build 26300). The same
// program started by a user process ends without a lock. So Windows' own
// screensaver is kept off and this does the idle timing.
//
// It does not start over a full-screen app or presentation, while any app holds
// the display on (video), or while the desktop is locked.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

static class Watcher
{
    const string MutexName = @"Local\PhotoFrameWatch";

    [StructLayout(LayoutKind.Sequential)]
    struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    static extern bool GetLastInputInfo(ref LastInputInfo info);
    [DllImport("user32.dll")]
    static extern bool SystemParametersInfo(uint action, uint param, ref int value, uint winIni);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SystemParametersInfo(uint action, uint param, IntPtr value, uint winIni);
    [DllImport("user32.dll")]
    static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")]
    static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("shell32.dll")]
    static extern int SHQueryUserNotificationState(out int state);
    [DllImport("powrprof.dll")]
    static extern uint CallNtPowerInformation(int level, IntPtr input, uint inputSize, out uint output, uint outputSize);

    const int QUNS_NOT_PRESENT = 1;
    const int QUNS_ACCEPTS_NOTIFICATIONS = 5;
    const int QUNS_QUIET_TIME = 6;
    const int SystemExecutionState = 16;
    const uint ES_DISPLAY_REQUIRED = 0x2;
    const uint DESKTOP_SWITCHDESKTOP = 0x100;
    const uint SPI_GETSCREENSAVEACTIVE = 0x10;
    const uint SPI_SETSCREENSAVEACTIVE = 0x11;
    const uint SPIF_UPDATEINIFILE_SENDCHANGE = 3;

    // Shared between the tray (UI thread) and the loop.
    static readonly object gate = new object();
    static DateTime pausedUntil = DateTime.MinValue;
    static bool startNow;
    static volatile bool exiting;

    public static bool IsRunning() { return PhotoFrame.MutexExists(MutexName); }

    public static int Run()
    {
        bool created;
        using (var mutex = new Mutex(true, MutexName, out created))
        {
            if (!created) return 0;
            PhotoFrame.Log("watch started, version " + PhotoFrame.Version);
            Application.EnableVisualStyles();

            var loop = new Thread(Loop);
            loop.IsBackground = true;
            loop.Start();

            using (NotifyIcon tray = MakeTray())
            {
                Application.Run();
                tray.Visible = false;
            }
            exiting = true;
            loop.Join(3000);
            return 0;
        }
    }

    static NotifyIcon MakeTray()
    {
        var tray = new NotifyIcon();
        tray.Icon = Icon.ExtractAssociatedIcon(PhotoFrame.Self);
        tray.Text = PhotoFrame.Name;

        var menu = new ContextMenuStrip();
        var start = new ToolStripMenuItem("Start slideshow");
        var pause = new ToolStripMenuItem("Pause for 1 hour");
        var settings = new ToolStripMenuItem("Settings...");
        var exit = new ToolStripMenuItem("Exit");
        settings.Font = new Font(settings.Font, FontStyle.Bold);
        menu.Items.AddRange(new ToolStripItem[] { start, pause, new ToolStripSeparator(), settings, exit });

        start.Click += delegate { lock (gate) startNow = true; };
        pause.Click += delegate
        {
            lock (gate) pausedUntil = Paused() ? DateTime.MinValue : DateTime.Now.AddHours(1);
        };
        settings.Click += delegate { PhotoFrame.Start("/settings"); };
        tray.DoubleClick += delegate { PhotoFrame.Start("/settings"); };
        exit.Click += delegate { Application.ExitThread(); };

        // Labels reflect the pause as the menu opens.
        menu.Opening += delegate
        {
            bool p = Paused();
            pause.Text = p ? "Resume (paused until " + pausedUntil.ToShortTimeString() + ")" : "Pause for 1 hour";
            tray.Text = p ? PhotoFrame.Name + " (paused)" : PhotoFrame.Name;
        };
        tray.ContextMenuStrip = menu;
        tray.Visible = true;
        return tray;
    }

    static bool Paused()
    {
        lock (gate) return DateTime.Now < pausedUntil;
    }

    static void Loop()
    {
        const int holdEverySeconds = 5;
        List<uint> saved = null;
        bool dimmed = false;
        int sinceHold = holdEverySeconds;
        uint startedAt = 0;
        while (!exiting)
        {
            try
            {
                if (Settings.KeepWindowsScreensaverOff) KeepWindowsScreensaverOff();
                bool running = Player.IsRunning();
                uint lastInput = LastInputTick();
                bool now;
                lock (gate) { now = startNow; startNow = false; }

                // One start per idle stretch: if it fails or is closed without
                // input, wait for the next input before trying again.
                if (!running && (now || (lastInput != startedAt && !Paused()
                    && IdleMilliseconds(lastInput) >= Settings.IdleSeconds * 1000L && MayStart())))
                {
                    startedAt = lastInput;
                    PhotoFrame.Start("/play");
                }

                if (running)
                {
                    if (!dimmed) saved = Brightness.Dim(Settings.DimPercent);
                    dimmed = true;
                    // Due at once, so the screen brightens as soon as it ends.
                    sinceHold = holdEverySeconds;
                }
                else if (Settings.HoldBrightness)
                {
                    dimmed = false;
                    if (++sinceHold >= holdEverySeconds)
                    {
                        sinceHold = 0;
                        Brightness.Hold(Settings.ActivePercent);
                    }
                }
                else if (dimmed)
                {
                    dimmed = false;
                    Brightness.Restore(saved);
                }
            }
            catch (Exception e)
            {
                PhotoFrame.Log("watch: " + e.Message);
            }
            Thread.Sleep(1000);
        }
    }

    // A screensaver Windows starts can lock on exit, so Windows' own stays off.
    // Checked on every pass, so the Screen Saver dialog cannot bring it back.
    static void KeepWindowsScreensaverOff()
    {
        int active = 0;
        if (!SystemParametersInfo(SPI_GETSCREENSAVEACTIVE, 0, ref active, 0) || active == 0) return;
        bool ok = SystemParametersInfo(SPI_SETSCREENSAVEACTIVE, 0, IntPtr.Zero, SPIF_UPDATEINIFILE_SENDCHANGE);
        PhotoFrame.Log(ok ? "turned Windows' screensaver off" : "could not turn Windows' screensaver off, error " + Marshal.GetLastWin32Error());
    }

    static uint LastInputTick()
    {
        var info = new LastInputInfo();
        info.Size = (uint)Marshal.SizeOf(info);
        return GetLastInputInfo(ref info) ? info.Time : 0;
    }

    // Tick counts wrap after 49 days; unsigned subtraction still gives the gap.
    static long IdleMilliseconds(uint lastInput)
    {
        return unchecked((uint)Environment.TickCount - lastInput);
    }

    // Not over a full-screen app, a presentation, video holding the display on,
    // or a locked desktop (which would swallow the slideshow's window).
    static bool MayStart()
    {
        int state;
        if (SHQueryUserNotificationState(out state) != 0) return false;
        // NOT_PRESENT also means locked, which the desktop check below covers.
        if (state != QUNS_NOT_PRESENT && state != QUNS_ACCEPTS_NOTIFICATIONS && state != QUNS_QUIET_TIME)
            return false;
        uint es;
        if (CallNtPowerInformation(SystemExecutionState, IntPtr.Zero, 0, out es, 4) == 0
            && (es & ES_DISPLAY_REQUIRED) != 0) return false;
        IntPtr desk = OpenInputDesktop(0, false, DESKTOP_SWITCHDESKTOP);
        if (desk == IntPtr.Zero) return false;
        CloseDesktop(desk);
        return true;
    }
}
