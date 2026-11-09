// Per-user install, no admin: the exe copies itself to
// %LOCALAPPDATA%\Programs\PhotoFrame, adds Start menu and desktop shortcuts,
// starts at sign-in, and registers with Settings > Apps so it uninstalls like
// any other app. Running a newer exe over an installed one updates it in place.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

static class Installer
{
    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\PhotoFrame");
    public static readonly string InstalledExe = Path.Combine(Dir, "PhotoFrame.exe");
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PhotoFrame";

    static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), PhotoFrame.Name + ".lnk"); } }
    static string StartMenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), PhotoFrame.Name + ".lnk"); } }

    public static bool IsRunningInstalled()
    {
        return string.Equals(Path.GetFullPath(PhotoFrame.Self), InstalledExe, StringComparison.OrdinalIgnoreCase);
    }

    public static int Install(bool ask)
    {
        bool update = File.Exists(InstalledExe);
        if (ask)
        {
            string old = update ? FileVersionInfo.GetVersionInfo(InstalledExe).FileVersion : null;
            string question = update
                ? string.Format("Update {0} from {1} to {2}?", PhotoFrame.Name, old, PhotoFrame.Version)
                : string.Format("Install {0} {1}?\n\nIt installs for you only, starts with Windows, and puts a shortcut on your desktop. Uninstall it from Settings > Apps.",
                    PhotoFrame.Name, PhotoFrame.Version);
            if (MessageBox.Show(question, PhotoFrame.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return 0;
        }

        StopOthers();
        Directory.CreateDirectory(Dir);
        if (!IsRunningInstalled()) File.Copy(PhotoFrame.Self, InstalledExe, true);

        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            k.SetValue("DisplayName", PhotoFrame.Name);
            k.SetValue("DisplayVersion", PhotoFrame.Version);
            k.SetValue("Publisher", "samgreenalaska");
            k.SetValue("DisplayIcon", InstalledExe);
            k.SetValue("InstallLocation", Dir);
            k.SetValue("UninstallString", "\"" + InstalledExe + "\" /uninstall");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
        }
        Shortcut(DesktopLink);
        Shortcut(StartMenuLink);
        // On by default; an update keeps whatever was chosen.
        if (!update || Settings.StartWithWindows) Settings.StartWithWindows = true;
        PhotoFrame.Log((update ? "updated to " : "installed ") + PhotoFrame.Version);

        Process.Start(InstalledExe, "/watch").Dispose();
        Process.Start(InstalledExe, "/settings").Dispose();
        return 0;
    }

    public static int Uninstall()
    {
        if (MessageBox.Show("Remove " + PhotoFrame.Name + "? Your photos are not touched.", PhotoFrame.Name,
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return 0;

        StopOthers();
        File.Delete(DesktopLink);
        File.Delete(StartMenuLink);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
        Settings.DeleteAll();

        // This exe is running from the folder, so the folder goes once it exits.
        var psi = new ProcessStartInfo("cmd.exe",
            "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + Dir + "\"");
        psi.CreateNoWindow = true;
        psi.UseShellExecute = false;
        psi.WorkingDirectory = Path.GetTempPath();
        Process.Start(psi).Dispose();

        MessageBox.Show(PhotoFrame.Name + " was removed.\n\nWindows' own screensaver was left off. Turn it back on in Screen Saver settings if you want it.",
            PhotoFrame.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 0;
    }

    // Any other copy, installed or not: the watcher, a slideshow, the settings.
    static void StopOthers()
    {
        int self = Process.GetCurrentProcess().Id;
        foreach (Process p in Process.GetProcessesByName("PhotoFrame"))
        {
            try
            {
                if (p.Id == self) continue;
                p.Kill();
                p.WaitForExit(3000);
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally { p.Dispose(); }
        }
    }

    // Through the shell's own scripting object, which every Windows has.
    static void Shortcut(string path)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(shellType);
        object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
        Type t = link.GetType();
        t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { InstalledExe });
        t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Dir });
        t.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { PhotoFrame.Name + " settings" });
        t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { InstalledExe + ",0" });
        t.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
    }
}
