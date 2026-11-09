// PhotoFrame.exe /settings. Every change is saved as it is made; the watcher
// reads the settings fresh each second, so there is no Apply button.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

sealed class SettingsWindow : Window
{
    const string MutexName = @"Local\PhotoFrameSettings";
    static readonly string WindowTitle = PhotoFrame.Name + " Settings";

    public static int Run()
    {
        bool created;
        using (var mutex = new Mutex(true, MutexName, out created))
        {
            if (!created) { PhotoFrame.Activate(WindowTitle); return 0; }
            return new Application().Run(new SettingsWindow());
        }
    }

    readonly TabItem advancedTab = new TabItem();
    TextBlock photoCount, monitorNote;

    SettingsWindow()
    {
        Title = WindowTitle;
        Width = 540;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        Background = SystemColors.ControlBrush;
        UseLayoutRounding = true;
        try { Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
            System.Drawing.Icon.ExtractAssociatedIcon(PhotoFrame.Self).Handle, Int32Rect.Empty,
            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions()); }
        catch (Exception) { }

        var tabs = new TabControl();
        tabs.Margin = new Thickness(12, 12, 12, 0);
        var general = new TabItem();
        general.Header = "General";
        general.Content = General();
        advancedTab.Header = "Advanced";
        advancedTab.Content = Advanced();
        tabs.Items.Add(general);
        tabs.Items.Add(advancedTab);

        var close = new Button();
        close.Content = "Close";
        close.MinWidth = 88;
        close.Padding = new Thickness(8, 3, 8, 3);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(12);
        close.IsCancel = true;
        close.Click += delegate { Close(); };

        var root = new DockPanel();
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(tabs);
        Content = root;

        // Sized to General, the taller tab, then fixed so switching tabs
        // does not make the window jump.
        Loaded += delegate
        {
            SizeToContent = SizeToContent.Manual;
            Height = ActualHeight;
            CountPhotos();
            CheckMonitors();
        };
    }

    UIElement General()
    {
        var p = Page();

        p.Children.Add(Header("Photos"));
        var folder = new TextBox();
        folder.Text = Settings.Folder;
        folder.IsReadOnly = true;
        folder.Padding = new Thickness(2);
        var browse = SmallButton("Browse...");
        browse.Margin = new Thickness(8, 0, 0, 0);
        browse.Click += delegate
        {
            string picked = FolderPicker.Pick(this, Settings.Folder);
            if (picked == null) return;
            Settings.Folder = picked;
            folder.Text = picked;
            CountPhotos();
        };
        var line = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        line.Children.Add(browse);
        line.Children.Add(folder);
        p.Children.Add(line);
        photoCount = Note("Counting photos...");
        p.Children.Add(photoCount);

        p.Children.Add(Header("Slideshow"));
        p.Children.Add(Row("Start after", NumberBox(Settings.IdleSeconds / 60, 1, 1440, delegate(int v) { Settings.IdleSeconds = v * 60; }), "minutes idle"));
        p.Children.Add(Row("Show each photo for", NumberBox(Settings.SlideSeconds, 3, 3600, delegate(int v) { Settings.SlideSeconds = v; }), "seconds"));
        var preview = SmallButton("Preview now");
        preview.HorizontalAlignment = HorizontalAlignment.Left;
        preview.Margin = new Thickness(0, 6, 0, 0);
        preview.Click += delegate { PhotoFrame.Start("/play"); };
        p.Children.Add(preview);
        p.Children.Add(Note("Move the mouse or press a key to end it."));

        p.Children.Add(Header("Brightness"));
        p.Children.Add(SliderRow("While the slideshow plays", Settings.DimPercent, delegate(int v) { Settings.DimPercent = v; }));
        var hold = Check("Set brightness the rest of the time too", Settings.HoldBrightness);
        FrameworkElement idle = SliderRow("The rest of the time", Settings.ActivePercent, delegate(int v) { Settings.ActivePercent = v; });
        idle.IsEnabled = hold.IsChecked == true;
        hold.Margin = new Thickness(0, 8, 0, 2);
        hold.Click += delegate
        {
            Settings.HoldBrightness = hold.IsChecked == true;
            idle.IsEnabled = Settings.HoldBrightness;
        };
        p.Children.Add(hold);
        p.Children.Add(idle);
        p.Children.Add(Note("Off: brightness goes back to where it was when the slideshow ends, and the monitor's own buttons work as usual."));
        monitorNote = Note("Checking monitors...");
        p.Children.Add(monitorNote);

        p.Children.Add(Header("Startup"));
        var startup = Check("Start with Windows", Settings.StartWithWindows);
        startup.Click += delegate { Settings.StartWithWindows = startup.IsChecked == true; };
        p.Children.Add(startup);
        return p;
    }

    UIElement Advanced()
    {
        var p = Page();

        p.Children.Add(Header("Photos"));
        var sub = Check("Include subfolders", Settings.Subfolders);
        sub.Click += delegate { Settings.Subfolders = sub.IsChecked == true; CountPhotos(); };
        p.Children.Add(sub);
        p.Children.Add(Note("JPEG, PNG, BMP, GIF and TIFF. WebP and HEIC need their codecs from the Microsoft Store."));

        p.Children.Add(Header("The pile"));
        p.Children.Add(Row("Prints on the pile", NumberBox(Settings.PileSize, 3, 30, delegate(int v) { Settings.PileSize = v; }), ""));
        p.Children.Add(Range("Print size", Settings.SizeMin, Settings.SizeMax, 20, 120, "% of the screen",
            delegate(int v) { Settings.SizeMin = v; }, delegate(int v) { Settings.SizeMax = v; }));
        p.Children.Add(Range("Tilt", Settings.TiltMin, Settings.TiltMax, 0, 30, "degrees",
            delegate(int v) { Settings.TiltMin = v; }, delegate(int v) { Settings.TiltMax = v; }));
        p.Children.Add(Row("Past the screen edge by up to", NumberBox(Settings.Overflow, 0, 40, delegate(int v) { Settings.Overflow = v; }), "%"));
        p.Children.Add(Note("Each print is placed so the pile covers the whole screen. Changes apply from the next slideshow."));

        p.Children.Add(Header("Windows"));
        var ss = Check("Keep Windows' own screensaver off", Settings.KeepWindowsScreensaverOff);
        ss.Click += delegate { Settings.KeepWindowsScreensaverOff = ss.IsChecked == true; };
        p.Children.Add(ss);
        p.Children.Add(Note("A screensaver that Windows starts can lock the PC when it ends, and would compete with this one."));

        var buttons = new WrapPanel();
        buttons.Margin = new Thickness(0, 14, 0, 0);
        var log = SmallButton("Open log");
        log.Click += delegate { PhotoFrame.OpenLog(); };
        var reset = SmallButton("Reset advanced settings");
        reset.Margin = new Thickness(8, 0, 0, 0);
        reset.Click += delegate { Settings.ResetAdvanced(); advancedTab.Content = Advanced(); CountPhotos(); };
        buttons.Children.Add(log);
        buttons.Children.Add(reset);
        p.Children.Add(buttons);
        p.Children.Add(Note(PhotoFrame.Name + " " + PhotoFrame.Version));
        return p;
    }

    void CountPhotos()
    {
        if (photoCount == null) return;
        photoCount.Text = "Counting photos...";
        string folder = Settings.Folder;
        bool sub = Settings.Subfolders;
        ThreadPool.QueueUserWorkItem(delegate
        {
            const int limit = 100000;
            int n = Player.FindPhotos(folder, sub, limit).Count;
            string text = n == 0 ? "No photos found here." : n >= limit ? "Over 100,000 photos." : n.ToString("N0") + (n == 1 ? " photo." : " photos.");
            Dispatcher.BeginInvoke(new Action(delegate { photoCount.Text = text; }));
        });
    }

    void CheckMonitors()
    {
        ThreadPool.QueueUserWorkItem(delegate
        {
            int n = Brightness.Controllable();
            int total = System.Windows.Forms.Screen.AllScreens.Length;
            string text = n == 0
                ? "No monitor here accepts brightness changes over DDC/CI, so brightness will not change."
                : string.Format("{0} of {1} monitor{2} accept{3} brightness changes over DDC/CI.", n, total, total == 1 ? "" : "s", n == 1 ? "s" : "");
            Dispatcher.BeginInvoke(new Action(delegate { monitorNote.Text = text; }));
        });
    }

    // --- building blocks ---

    static StackPanel Page()
    {
        var p = new StackPanel();
        p.Margin = new Thickness(16, 4, 16, 16);
        return p;
    }

    static TextBlock Header(string text)
    {
        var t = new TextBlock();
        t.Text = text;
        t.FontSize = 15;
        t.FontWeight = FontWeights.SemiBold;
        t.Margin = new Thickness(0, 14, 0, 6);
        return t;
    }

    static TextBlock Note(string text)
    {
        var t = new TextBlock();
        t.Text = text;
        t.TextWrapping = TextWrapping.Wrap;
        t.Foreground = SystemColors.GrayTextBrush;
        t.FontSize = 12;
        t.Margin = new Thickness(0, 4, 0, 0);
        return t;
    }

    static Button SmallButton(string text)
    {
        var b = new Button();
        b.Content = text;
        b.Padding = new Thickness(10, 2, 10, 2);
        return b;
    }

    static CheckBox Check(string text, bool value)
    {
        var c = new CheckBox();
        c.Content = text;
        c.IsChecked = value;
        c.Margin = new Thickness(0, 2, 0, 2);
        return c;
    }

    static FrameworkElement Row(string label, UIElement control, string suffix)
    {
        var g = new Grid();
        g.Margin = new Thickness(0, 3, 0, 3);
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = new TextBlock();
        l.Text = label;
        l.VerticalAlignment = VerticalAlignment.Center;
        var s = new TextBlock();
        s.Text = suffix;
        s.VerticalAlignment = VerticalAlignment.Center;
        s.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(control, 1);
        Grid.SetColumn(s, 2);
        g.Children.Add(l);
        g.Children.Add(control);
        g.Children.Add(s);
        return g;
    }

    static FrameworkElement Range(string label, int lo, int hi, int min, int max, string suffix, Action<int> saveLo, Action<int> saveHi)
    {
        var p = new StackPanel();
        p.Orientation = Orientation.Horizontal;
        p.Children.Add(NumberBox(lo, min, max, saveLo));
        var to = new TextBlock();
        to.Text = "to";
        to.VerticalAlignment = VerticalAlignment.Center;
        to.Margin = new Thickness(8, 0, 8, 0);
        p.Children.Add(to);
        p.Children.Add(NumberBox(hi, min, max, saveHi));
        return Row(label, p, suffix);
    }

    // Saves each valid value as it is typed; on leaving, shows what was saved.
    static TextBox NumberBox(int value, int min, int max, Action<int> save)
    {
        var t = new TextBox();
        t.Text = value.ToString();
        t.Width = 60;
        t.Padding = new Thickness(2);
        t.HorizontalContentAlignment = HorizontalAlignment.Right;
        int saved = value;
        t.PreviewTextInput += delegate(object s, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text) if (!char.IsDigit(c)) e.Handled = true;
        };
        t.TextChanged += delegate
        {
            int v;
            if (int.TryParse(t.Text, out v) && v >= min && v <= max && v != saved) { saved = v; save(v); }
        };
        t.LostFocus += delegate
        {
            int v;
            if (int.TryParse(t.Text, out v) && v != saved)
            {
                saved = Math.Max(min, Math.Min(max, v));
                save(saved);
            }
            t.Text = saved.ToString();
        };
        return t;
    }

    // Saved when the drag ends, so the registry is not written per pixel.
    static FrameworkElement SliderRow(string label, int value, Action<int> save)
    {
        var s = new Slider();
        s.Minimum = 0;
        s.Maximum = 100;
        s.Value = value;
        s.Width = 200;
        s.IsSnapToTickEnabled = true;
        s.TickFrequency = 5;
        s.SmallChange = 5;
        s.LargeChange = 10;
        s.VerticalAlignment = VerticalAlignment.Center;
        var g = Row(label, s, value + "%");
        var shown = (TextBlock)((Grid)g).Children[2];
        s.ValueChanged += delegate { shown.Text = (int)s.Value + "%"; };
        s.LostMouseCapture += delegate { save((int)s.Value); };
        s.KeyUp += delegate { save((int)s.Value); };
        s.MouseLeftButtonUp += delegate { save((int)s.Value); };
        return g;
    }
}

// Windows' own folder picker (the Explorer-style one, not the old tree view).
static class FolderPicker
{
    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    class FileOpenDialog { }

    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(uint count, IntPtr types);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem item);
        void SetFolder(IShellItem item);
        void GetFolder(out IShellItem item);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr bc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr bc, [In] ref Guid riid, out IShellItem item);

    const uint FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40;
    const uint SIGDN_FILESYSPATH = 0x80058000;

    public static string Pick(Window owner, string start)
    {
        var dialog = (IFileDialog)new FileOpenDialog();
        dialog.SetOptions(FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM);
        dialog.SetTitle("Choose the folder of photos");
        try
        {
            Guid iid = typeof(IShellItem).GUID;
            IShellItem folder;
            SHCreateItemFromParsingName(start, IntPtr.Zero, ref iid, out folder);
            dialog.SetFolder(folder);
        }
        catch (Exception) { }
        IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(owner).Handle;
        if (dialog.Show(hwnd) != 0) return null;
        IShellItem result;
        dialog.GetResult(out result);
        string path;
        result.GetDisplayName(SIGDN_FILESYSPATH, out path);
        return path;
    }
}
