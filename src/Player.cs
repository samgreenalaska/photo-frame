// PhotoFrame.exe /play: the slideshow. Each photo drops onto a pile of prints,
// landing a little off square, with the older ones still showing beneath. Every
// monitor gets its own pile. Any key, click or mouse movement ends it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

static class Player
{
    const string MutexName = @"Local\PhotoFramePlay";

    public static readonly string[] Extensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic" };

    public static bool IsRunning() { return PhotoFrame.MutexExists(MutexName); }

    public static int Run()
    {
        bool created;
        using (var mutex = new Mutex(true, MutexName, out created))
        {
            if (!created) return 0;
            string folder = Settings.Folder;
            List<string> photos = FindPhotos(folder, Settings.Subfolders, int.MaxValue);
            if (photos.Count == 0)
            {
                PhotoFrame.Log("play: no photos in " + folder);
                return 1;
            }
            var rng = new Random();
            for (int i = photos.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                string t = photos[i]; photos[i] = photos[j]; photos[j] = t;
            }
            PhotoFrame.Log("play: " + photos.Count + " photos, " + Settings.SlideSeconds + " s each");

            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var options = new PileOptions();
            System.Windows.Forms.Screen[] screens = System.Windows.Forms.Screen.AllScreens;
            double scale = SystemDipScale();
            for (int i = 0; i < screens.Length; i++)
            {
                // Each monitor starts at a different point in the same order.
                var w = new StackWindow(photos, i * photos.Count / screens.Length, options, rng.Next());
                System.Drawing.Rectangle b = screens[i].Bounds;
                w.Left = b.Left / scale;
                w.Top = b.Top / scale;
                w.Width = b.Width / scale;
                w.Height = b.Height / scale;
                w.Closed += delegate { app.Shutdown(); };
                w.Show();
            }
            return app.Run();
        }
    }

    // Screen bounds come in pixels, WPF positions in 96-dpi units.
    static double SystemDipScale()
    {
        using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96.0;
    }

    // Stops at limit, so the settings window can count a huge folder cheaply.
    public static List<string> FindPhotos(string folder, bool subfolders, int limit)
    {
        var found = new List<string>();
        var dirs = new Stack<string>();
        dirs.Push(folder);
        while (dirs.Count > 0 && found.Count < limit)
        {
            string dir = dirs.Pop();
            try
            {
                foreach (string f in Directory.EnumerateFiles(dir))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (Array.IndexOf(Extensions, ext) >= 0) found.Add(f);
                    if (found.Count >= limit) break;
                }
                if (subfolders)
                    foreach (string d in Directory.EnumerateDirectories(dir))
                        if ((new DirectoryInfo(d).Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                            dirs.Push(d);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return found;
    }

    // Decoded at no more than the screen's height, and turned upright by the
    // EXIF orientation that phone cameras set rather than rotating the pixels.
    public static BitmapSource Load(string path, int maxHeight)
    {
        int orientation = 1, width, height;
        using (var fs = File.OpenRead(path))
        {
            BitmapDecoder dec = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            BitmapFrame frame = dec.Frames[0];
            width = frame.PixelWidth;
            height = frame.PixelHeight;
            try
            {
                var meta = frame.Metadata as BitmapMetadata;
                object o = meta == null ? null : meta.GetQuery("/app1/ifd/{ushort=274}");
                if (o is ushort) orientation = (ushort)o;
            }
            catch (NotSupportedException) { }
            catch (ArgumentException) { }
        }
        bool sideways = orientation >= 5 && orientation <= 8;

        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.UriSource = new Uri(path);
        // Upright height is the stored width for a sideways photo.
        if (sideways) { if (width > maxHeight) img.DecodePixelWidth = maxHeight; }
        else if (height > maxHeight) img.DecodePixelHeight = maxHeight;
        img.EndInit();

        BitmapSource result = img;
        if (orientation > 1 && orientation <= 8)
        {
            var t = new TransformGroup();
            if (orientation == 2 || orientation == 4 || orientation == 5 || orientation == 7) t.Children.Add(new ScaleTransform(-1, 1));
            // EXIF: 2 mirror, 3 180, 4 mirror+180, 5 mirror+270, 6 90, 7 mirror+90, 8 270 (clockwise).
            int angle = orientation == 3 || orientation == 4 ? 180 : orientation == 6 || orientation == 7 ? 90
                : orientation == 5 || orientation == 8 ? 270 : 0;
            if (angle != 0) t.Children.Add(new RotateTransform(angle));
            result = new TransformedBitmap(img, t);
        }
        result.Freeze();
        return result;
    }
}

// Read once when the show starts.
sealed class PileOptions
{
    public readonly int Seconds = Settings.SlideSeconds;
    public readonly int PileSize = Settings.PileSize;
    public readonly double SizeMin = Settings.SizeMin / 100.0, SizeMax = Settings.SizeMax / 100.0;
    public readonly double TiltMin = Settings.TiltMin, TiltMax = Settings.TiltMax;
    public readonly double Overflow = Settings.Overflow / 100.0;
}

sealed class StackWindow : Window
{
    const double DropMs = 900;

    // Coverage grid: the drop that last covered each cell's centre. Each print
    // is placed so the pile together covers the whole screen.
    const int Cols = 32, Rows = 18, Candidates = 24;
    readonly int[,] coveredAt = new int[Cols, Rows];
    int drops;

    readonly List<string> photos;
    readonly PileOptions o;
    readonly Canvas pile = new Canvas();
    readonly DispatcherTimer timer = new DispatcherTimer();
    readonly Random rng;
    int next;
    bool loading;
    Point? mouseStart;

    public StackWindow(List<string> photos, int start, PileOptions options, int seed)
    {
        this.photos = photos;
        next = start % photos.Count;
        o = options;
        rng = new Random(seed);
        for (int c = 0; c < Cols; c++)
            for (int r = 0; r < Rows; r++) coveredAt[c, r] = -o.PileSize;

        Title = PhotoFrame.Name + " slideshow";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.None;
        Background = new RadialGradientBrush(Color.FromRgb(0x2a, 0x26, 0x22), Color.FromRgb(0x0c, 0x0b, 0x0a));
        pile.ClipToBounds = true;
        Content = pile;

        timer.Interval = TimeSpan.FromSeconds(o.Seconds);
        timer.Tick += delegate { LoadNext(); };
        Loaded += delegate { Activate(); LoadNext(); timer.Start(); };
        KeyDown += delegate { Close(); };
        MouseDown += delegate { Close(); };
        MouseMove += OnMouseMove;
    }

    // WPF raises a MouseMove when the window appears under a still mouse, so
    // measure from the first position seen, not from zero.
    void OnMouseMove(object sender, MouseEventArgs e)
    {
        Point p = e.GetPosition(this);
        if (mouseStart == null) { mouseStart = p; return; }
        if (Math.Abs(p.X - mouseStart.Value.X) + Math.Abs(p.Y - mouseStart.Value.Y) > 12) Close();
    }

    // Decodes off the UI thread so the animation in flight does not stall.
    // A photo that will not decode is skipped for the next one.
    void LoadNext()
    {
        if (loading) return;
        loading = true;
        string path = photos[next];
        next = (next + 1) % photos.Count;
        PresentationSource src = PresentationSource.FromVisual(this);
        double dpi = src == null ? 1 : src.CompositionTarget.TransformToDevice.M22;
        int height = (int)Math.Ceiling(ActualHeight * dpi);
        ThreadPool.QueueUserWorkItem(delegate
        {
            BitmapSource img = null;
            try { img = Player.Load(path, height); }
            catch (Exception e) { PhotoFrame.Log("play: skipped " + path + ": " + e.Message); }
            Dispatcher.BeginInvoke(new Action(delegate
            {
                loading = false;
                if (img != null) DropCard(img);
                else if (photos.Count > 1) LoadNext();
            }));
        });
    }

    double Between(double lo, double hi) { return lo + rng.NextDouble() * (hi - lo); }

    struct Placement
    {
        public double Left, Top, CardW, CardH, Angle;
    }

    // A random size, spot and tilt. A print can land anywhere from one edge to
    // the other and a little past it; the reach is per print, since a landscape
    // print fills the height long before the width.
    Placement RandomPlacement(double w, double h, double border, double aspect)
    {
        double size = Between(o.SizeMin, o.SizeMax);
        double maxW = w * size - 2 * border, maxH = h * size - 2 * border;
        double imgW = maxW, imgH = maxW / aspect;
        if (imgH > maxH) { imgH = maxH; imgW = maxH * aspect; }
        var p = new Placement();
        p.CardW = imgW + 2 * border;
        p.CardH = imgH + 2 * border;
        double reachX = Math.Max(0, (w - p.CardW) / 2) + o.Overflow * w;
        double reachY = Math.Max(0, (h - p.CardH) / 2) + o.Overflow * h;
        p.Left = (w - p.CardW) / 2 + Between(-1, 1) * reachX;
        p.Top = (h - p.CardH) / 2 + Between(-1, 1) * reachY;
        p.Angle = Between(o.TiltMin, o.TiltMax) * (rng.Next(2) == 0 ? -1 : 1);
        return p;
    }

    // Whether a placed, tilted print covers a point on the screen.
    static bool Covers(Placement p, double x, double y)
    {
        double dx = x - (p.Left + p.CardW / 2), dy = y - (p.Top + p.CardH / 2);
        double a = -p.Angle * Math.PI / 180;
        double lx = dx * Math.Cos(a) - dy * Math.Sin(a), ly = dx * Math.Sin(a) + dy * Math.Cos(a);
        return Math.Abs(lx) <= p.CardW / 2 && Math.Abs(ly) <= p.CardH / 2;
    }

    // What covering a cell is worth, by how many drops ago it was last covered.
    // A cell whose cover leaves the pile with this drop must be covered now;
    // one covered by the last two prints should not be, or they vanish at once.
    double Worth(int age)
    {
        if (age >= o.PileSize) return 400;
        if (age <= 2) return -6;
        return (age - 2) * (age - 2);
    }

    // Best of a few random placements by the cells it would cover, so prints
    // fall naturally but the pile never shows the background for long.
    Placement Place(double w, double h, double border, double aspect)
    {
        Placement best = new Placement();
        double bestScore = double.MinValue;
        for (int i = 0; i < Candidates; i++)
        {
            Placement p = RandomPlacement(w, h, border, aspect);
            double score = 0;
            for (int c = 0; c < Cols; c++)
                for (int r = 0; r < Rows; r++)
                    if (Covers(p, (c + 0.5) * w / Cols, (r + 0.5) * h / Rows))
                        score += Worth(drops - coveredAt[c, r]);
            if (score > bestScore) { bestScore = score; best = p; }
        }
        int bare = 0;
        for (int c = 0; c < Cols; c++)
            for (int r = 0; r < Rows; r++)
            {
                if (Covers(best, (c + 0.5) * w / Cols, (r + 0.5) * h / Rows)) coveredAt[c, r] = drops;
                else if (drops - coveredAt[c, r] >= o.PileSize) bare++;
            }
        drops++;
        if (drops % o.PileSize == 0)
            PhotoFrame.Log(string.Format("play: drop {0}, {1}% of the screen bare", drops, bare * 100 / (Cols * Rows)));
        return best;
    }

    void DropCard(BitmapSource img)
    {
        double w = pile.ActualWidth, h = pile.ActualHeight;
        double border = Math.Round(Math.Min(w, h) * 0.016);
        Placement place = Place(w, h, border, img.Width / img.Height);
        double imgW = place.CardW - 2 * border, imgH = place.CardH - 2 * border;

        var image = new Image();
        image.Source = img;
        image.Stretch = Stretch.Fill;
        image.Width = imgW;
        image.Height = imgH;
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        var shadow = new DropShadowEffect();
        shadow.Color = Colors.Black;
        shadow.Direction = 270;
        shadow.Opacity = 0.55;

        var card = new Border();
        card.Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xf2, 0xea));
        card.Padding = new Thickness(border);
        card.Child = image;
        card.Effect = shadow;
        card.RenderTransformOrigin = new Point(0.5, 0.5);
        card.Opacity = 0;
        Canvas.SetLeft(card, place.Left);
        Canvas.SetTop(card, place.Top);

        var scale = new ScaleTransform();
        var rotate = new RotateTransform();
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(rotate);
        card.RenderTransform = group;
        pile.Children.Add(card);

        var drop = new Duration(TimeSpan.FromMilliseconds(DropMs));
        // Falls from above the pile: bigger, tilted further, and lands with a
        // small settle past its final size.
        var land = new BackEase();
        land.EasingMode = EasingMode.EaseOut;
        land.Amplitude = 0.25;
        var fall = new CubicEase();
        fall.EasingMode = EasingMode.EaseOut;

        var s = new DoubleAnimation(1.35, 1, drop);
        s.EasingFunction = land;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, s);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, s);

        double extra = Between(6, 12) * (place.Angle < 0 ? -1 : 1);
        var r = new DoubleAnimation(place.Angle + extra, place.Angle, drop);
        r.EasingFunction = fall;
        rotate.BeginAnimation(RotateTransform.AngleProperty, r);

        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(DropMs * 0.35)));

        // The shadow tightens as the print comes down onto the pile.
        var depth = new DoubleAnimation(40, 5, drop);
        depth.EasingFunction = fall;
        shadow.BeginAnimation(DropShadowEffect.ShadowDepthProperty, depth);
        var blur = new DoubleAnimation(70, 22, drop);
        blur.EasingFunction = fall;
        shadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, blur);

        // Cards already fading are still children, so count only live ones.
        int live = 0;
        foreach (UIElement e in pile.Children) if (e.IsHitTestVisible) live++;
        if (live > o.PileSize)
        {
            foreach (UIElement e in pile.Children)
            {
                if (!e.IsHitTestVisible) continue;
                UIElement oldest = e;
                oldest.IsHitTestVisible = false;
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(800));
                fade.BeginTime = TimeSpan.FromMilliseconds(DropMs);
                fade.Completed += delegate { pile.Children.Remove(oldest); };
                oldest.BeginAnimation(OpacityProperty, fade);
                break;
            }
        }
    }
}
