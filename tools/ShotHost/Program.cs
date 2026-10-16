using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// Starts Shutdown.exe (beside this exe) with an en-US UI culture, for English screenshots on a
// machine whose Windows display language is not English. It also writes the window content at 200 %
// every two seconds (to shot-render.png beside the exe, or "--render <file.png>"), independent of
// the screen size and scale.
static class Program
{
    const double Scale = 2.0;

    [STAThread]
    static void Main(string[] args)
    {
        var en = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = en;
        CultureInfo.DefaultThreadCurrentUICulture = en;
        System.Threading.Thread.CurrentThread.CurrentCulture = en;
        System.Threading.Thread.CurrentThread.CurrentUICulture = en;

        int i = Array.IndexOf(args, "--render");
        string target = i >= 0 && i + 1 < args.Length
            ? Path.GetFullPath(args[i + 1])
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shot-render.png");
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, e) => StartRendering((Window)s, target)));

        var app = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shutdown.exe"));
        app.EntryPoint.Invoke(null, app.EntryPoint.GetParameters().Length == 0 ? null : new object[] { new string[0] });
    }

    static void StartRendering(Window w, string target)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (s, e) => Render(w, target);
        timer.Start();
    }

    static void Render(Window w, string target)
    {
        // The template root covers the whole client area, margins and window background included.
        if (VisualTreeHelper.GetChildrenCount(w) == 0 || !(VisualTreeHelper.GetChild(w, 0) is FrameworkElement root) ||
            root.ActualWidth <= 0) return;
        var size = new Size(root.ActualWidth, root.ActualHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(w.Background, null, new Rect(size));
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
