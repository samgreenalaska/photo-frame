// Settings live in HKCU\Software\PhotoFrame and are read fresh on every use, so
// a change in the settings window reaches the running watcher within a second.

using System;
using Microsoft.Win32;

static class Settings
{
    const string Key = @"Software\PhotoFrame";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "PhotoFrame";

    public static string Folder
    {
        get
        {
            string v = GetString("Folder");
            return v ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        }
        set { SetString("Folder", value); }
    }

    // Stored in seconds; the settings window shows minutes.
    public static int IdleSeconds { get { return Get("IdleSeconds", 300, 60, 24 * 3600); } set { Set("IdleSeconds", value); } }
    public static int SlideSeconds { get { return Get("SlideSeconds", 12, 3, 3600); } set { Set("SlideSeconds", value); } }

    // Brightness while the slideshow plays, and the rest of the time.
    public static int DimPercent { get { return Get("DimPercent", 30, 0, 100); } set { Set("DimPercent", value); } }
    public static int ActivePercent { get { return Get("ActivePercent", 80, 0, 100); } set { Set("ActivePercent", value); } }
    // Off: brightness is only touched while the slideshow plays, and put back after.
    public static bool HoldBrightness { get { return Get("HoldBrightness", 1, 0, 1) == 1; } set { Set("HoldBrightness", value ? 1 : 0); } }

    // Advanced.
    public static bool Subfolders { get { return Get("Subfolders", 1, 0, 1) == 1; } set { Set("Subfolders", value ? 1 : 0); } }
    public static int PileSize { get { return Get("PileSize", 10, 3, 30); } set { Set("PileSize", value); } }
    public static int SizeMin { get { return Get("SizeMin", 60, 20, 120); } set { Set("SizeMin", value); } }
    public static int SizeMax { get { return Math.Max(SizeMin, Get("SizeMax", 100, 20, 120)); } set { Set("SizeMax", value); } }
    public static int TiltMin { get { return Get("TiltMin", 2, 0, 30); } set { Set("TiltMin", value); } }
    public static int TiltMax { get { return Math.Max(TiltMin, Get("TiltMax", 7, 0, 30)); } set { Set("TiltMax", value); } }
    public static int Overflow { get { return Get("Overflow", 8, 0, 40); } set { Set("Overflow", value); } }
    public static bool KeepWindowsScreensaverOff { get { return Get("KeepWindowsScreensaverOff", 1, 0, 1) == 1; } set { Set("KeepWindowsScreensaverOff", value ? 1 : 0); } }

    // Everything but the folder, which is the one thing nobody wants reset.
    public static void ResetAdvanced()
    {
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key, true))
        {
            if (k == null) return;
            foreach (string n in new[] { "Subfolders", "PileSize", "SizeMin", "SizeMax", "TiltMin", "TiltMax", "Overflow", "KeepWindowsScreensaverOff" })
                k.DeleteValue(n, false);
        }
    }

    public static bool StartWithWindows
    {
        get
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(RunValue) != null;
        }
        set
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (value) k.SetValue(RunValue, "\"" + Installer.InstalledExe + "\" /watch");
                else k.DeleteValue(RunValue, false);
            }
        }
    }

    public static void DeleteAll()
    {
        Registry.CurrentUser.DeleteSubKeyTree(Key, false);
        StartWithWindows = false;
    }

    static int Get(string name, int fallback, int min, int max)
    {
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
        {
            object v = k == null ? null : k.GetValue(name);
            return v is int ? Math.Max(min, Math.Min(max, (int)v)) : fallback;
        }
    }

    static void Set(string name, int value)
    {
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key))
            k.SetValue(name, value, RegistryValueKind.DWord);
    }

    static string GetString(string name)
    {
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
        {
            string v = k == null ? null : k.GetValue(name) as string;
            return string.IsNullOrEmpty(v) ? null : v;
        }
    }

    static void SetString(string name, string value)
    {
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key))
            k.SetValue(name, value);
    }
}
