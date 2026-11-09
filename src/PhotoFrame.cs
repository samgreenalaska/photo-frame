// Photo Frame: turns an idle Windows PC into a photo frame. Photos drop onto a
// pile of prints, and the monitors dim over DDC/CI while the show runs.
//
// One exe does everything:
//   PhotoFrame.exe              Not installed: offers to install (or update).
//                               Installed: opens the settings.
//   PhotoFrame.exe /watch       Background watcher with a tray icon. Starts the
//                               slideshow after the idle time and manages
//                               brightness. Started at sign-in.
//   PhotoFrame.exe /play        The slideshow itself, one pile per monitor.
//   PhotoFrame.exe /settings    The settings window.
//   PhotoFrame.exe /uninstall   Removes it (Settings > Apps runs this).

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

[assembly: AssemblyTitle("Photo Frame")]
[assembly: AssemblyProduct("Photo Frame")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

static partial class PhotoFrame
{
    public const string Name = "Photo Frame";
    public static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
    public static readonly string Self = Assembly.GetExecutingAssembly().Location;
    static readonly string LogFile = Path.Combine(Path.GetDirectoryName(Self), "photoframe.log");

    [STAThread]
    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        try
        {
            switch (mode)
            {
                case "/watch": return Watcher.Run();
                case "/play": return Player.Run();
                case "/settings": return SettingsWindow.Run();
                case "/install": return Installer.Install(false);
                case "/uninstall": return Installer.Uninstall();
                case "": return Installer.IsRunningInstalled() ? OpenInstalled() : Installer.Install(true);
            }
        }
        catch (Exception e)
        {
            Log(mode + " failed: " + e);
            System.Windows.Forms.MessageBox.Show(e.Message, Name, System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Error);
            return 1;
        }
        return 2;
    }

    // From the desktop shortcut: make sure the watcher runs, then show settings.
    static int OpenInstalled()
    {
        if (!Watcher.IsRunning()) Start("/watch");
        return SettingsWindow.Run();
    }

    public static void Start(string arg)
    {
        try { Process.Start(Self, arg).Dispose(); }
        catch (System.ComponentModel.Win32Exception e) { Log("could not start " + arg + ": " + e.Message); }
    }

    public static void Log(string msg)
    {
        try
        {
            // Kept short: past 1 MB the older half goes.
            var info = new FileInfo(LogFile);
            if (info.Exists && info.Length > 1 << 20)
            {
                string all = File.ReadAllText(LogFile);
                File.WriteAllText(LogFile, all.Substring(all.Length / 2));
            }
            File.AppendAllText(LogFile, DateTime.Now.ToString("s") + " " + msg + Environment.NewLine);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void OpenLog()
    {
        if (!File.Exists(LogFile)) Log("log opened");
        Process.Start("notepad.exe", "\"" + LogFile + "\"").Dispose();
    }

    public static bool MutexExists(string name)
    {
        Mutex m;
        if (!Mutex.TryOpenExisting(name, out m)) return false;
        m.Dispose();
        return true;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hwnd, int cmd);

    // For a second copy of a window: bring the first forward instead.
    public static void Activate(string title)
    {
        IntPtr h = FindWindow(null, title);
        if (h == IntPtr.Zero) return;
        ShowWindow(h, 9); // SW_RESTORE
        SetForegroundWindow(h);
    }
}
