using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// Starts Shutdown.exe (beside this exe) with an en-US UI culture (or the one in culture.txt), for English screenshots on a
// machine whose Windows display language is not English. It also writes the window content at 200 %
// every two seconds (to shot-render.png beside the exe, or "--render <file.png>"), independent of
// the screen size and scale. An open right-click menu is written to shot-menu.png in the same folder.
static class Program
{
    const double Scale = 2.0;

    [STAThread]
    static void Main(string[] args)
    {
        // A culture.txt beside the exe (e.g. "de-DE") renders another language; the VM driver passes no arguments.
        string cultureFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "culture.txt");
        var culture = CultureInfo.GetCultureInfo(File.Exists(cultureFile) ? File.ReadAllText(cultureFile).Trim() : "en-US");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        System.Threading.Thread.CurrentThread.CurrentCulture = culture;
        System.Threading.Thread.CurrentThread.CurrentUICulture = culture;

        int i = Array.IndexOf(args, "--render");
        string target = i >= 0 && i + 1 < args.Length
            ? Path.GetFullPath(args[i + 1])
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shot-render.png");
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, e) => StartRendering((Window)s, target)));
        // An open right-click menu lives in its own popup, outside the window render: write it to shot-menu.png
        // while it stays open.
        string menuTarget = Path.Combine(Path.GetDirectoryName(target), "shot-menu.png");
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.ContextMenu), System.Windows.Controls.ContextMenu.OpenedEvent,
            new RoutedEventHandler((s, e) => StartMenuRendering((System.Windows.Controls.ContextMenu)s, menuTarget)));

        var app = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shutdown.exe"));
        app.EntryPoint.Invoke(null, app.EntryPoint.GetParameters().Length == 0 ? null : new object[] { new string[0] });
    }

    static void StartRendering(Window w, string target)
    {
        // The app's Display text mode snaps glyphs to the 96 dpi grid and renders them unsmoothed into a
        // bitmap; Ideal with grayscale smoothing matches how the text looks on a 200 % screen.
        TextOptions.SetTextFormattingMode(w, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(w, TextRenderingMode.Grayscale);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (s, e) => Render(w, target);
        timer.Start();
    }

    static void StartMenuRendering(System.Windows.Controls.ContextMenu menu, string target)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (s, e) =>
        {
            if (!menu.IsOpen) { timer.Stop(); return; }
            TextOptions.SetTextFormattingMode(menu, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(menu, TextRenderingMode.Grayscale);
            if (menu.ActualWidth > 0) Save(menu, Brushes.Transparent, target);
        };
        timer.Start();
    }

    static void Render(Window w, string target)
    {
        // The template root covers the whole client area, margins and window background included.
        if (VisualTreeHelper.GetChildrenCount(w) == 0 || !(VisualTreeHelper.GetChild(w, 0) is FrameworkElement root) ||
            root.ActualWidth <= 0) return;
        Save(root, w.Background, target);
    }

    static void Save(FrameworkElement root, Brush background, string target)
    {
        var size = new Size(root.ActualWidth, root.ActualHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(background, null, new Rect(size));
            dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null, new Rect(size));
        }
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(size.Width * Scale), (int)Math.Ceiling(size.Height * Scale),
            96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        string tmp = target + ".tmp";
        using (var fs = File.Create(tmp)) png.Save(fs);
        if (File.Exists(target)) File.Delete(target);
        File.Move(tmp, target);
    }
}
