using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Shutdown.Ui
{
    /// <summary>Light/dark palette following Windows; high contrast maps to system colours.</summary>
    public static class Theme
    {
        public static string Override { get; set; } = "system";
        public static bool IsDark { get; private set; }

        public static bool SystemPrefersDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch { return false; }
        }

        public static void Apply(ResourceDictionary res)
        {
            bool Is(string v) => string.Equals(Override, v, StringComparison.OrdinalIgnoreCase);
            IsDark = Is("dark") || (!Is("light") && SystemPrefersDark());

            if (SystemParameters.HighContrast)
            {
                Set(res, "Bg", SystemColors.WindowColor);
                Set(res, "Card", SystemColors.WindowColor);
                Set(res, "CardBorder", SystemColors.WindowTextColor);
                Set(res, "Text", SystemColors.WindowTextColor);
                Set(res, "TextSub", SystemColors.WindowTextColor);
                Set(res, "Control", SystemColors.ControlColor);
                Set(res, "ControlHover", SystemColors.HighlightColor);
                // Text on a highlight fill must use the highlight text colour, or it may vanish.
                Set(res, "ControlHoverText", SystemColors.HighlightTextColor);
                Set(res, "ControlPressed", SystemColors.HighlightColor);
                // A highlight fill would make a disabled button look selected.
                Set(res, "DisabledFill", SystemColors.ControlColor);
                Set(res, "ControlBorder", SystemColors.WindowTextColor);
                Set(res, "Accent", SystemColors.HighlightColor);
                Set(res, "AccentHover", SystemColors.HighlightColor);
                Set(res, "AccentText", SystemColors.HighlightTextColor);
                Set(res, "Warn", SystemColors.WindowTextColor);
                Set(res, "Urgent", SystemColors.WindowTextColor);
                Set(res, "Danger", SystemColors.WindowTextColor);
                Set(res, "DangerSoft", SystemColors.WindowColor);
                Set(res, "Focus", SystemColors.HighlightColor);
                Set(res, "Disabled", SystemColors.GrayTextColor);
                // The red close hover would ignore the contrast theme.
                Set(res, "CaptionCloseHover", SystemColors.HighlightColor);
                Set(res, "CaptionClosePressed", SystemColors.HighlightColor);
                Set(res, "CaptionCloseText", SystemColors.HighlightTextColor);
                return;
            }

            // Windows' own close button red, in both modes.
            Set(res, "CaptionCloseHover", "#C42B1C");
            Set(res, "CaptionClosePressed", "#C83C31");
            Set(res, "CaptionCloseText", "#FFFFFF");
            var a = AccentPalette();
            if (IsDark)
            {
                Set(res, "Bg", "#202020");
                Set(res, "Card", "#2B2B2B");
                Set(res, "CardBorder", "#3A3A3A");
                Set(res, "Text", "#FFFFFF");
                Set(res, "TextSub", "#C8C8C8");
                Set(res, "Control", "#363636");
                Set(res, "ControlHover", "#404040");
                Set(res, "ControlHoverText", "#FFFFFF");
                Set(res, "ControlPressed", "#2F2F2F");
                Set(res, "DisabledFill", "#2F2F2F");
                Set(res, "ControlBorder", "#828282");
                // A deep accent, as in light mode; it reads better than WinUI's pale fill with black text.
                Set(res, "Accent", a[Dark1]);
                Set(res, "AccentHover", a[Accent]);
                Set(res, "AccentText", TextOn(a[Dark1], a[Accent]));
                Set(res, "Warn", "#FCE100");
                Set(res, "Urgent", "#FF6B6B");
                Set(res, "Danger", "#FF99A4");
                Set(res, "DangerSoft", "#442726");
                Set(res, "Focus", "#FFFFFF");
                Set(res, "Disabled", "#7A7A7A");
            }
            else
            {
                Set(res, "Bg", "#F3F3F3");
                Set(res, "Card", "#FFFFFF");
                Set(res, "CardBorder", "#E3E3E3");
                Set(res, "Text", "#1B1B1B");
                Set(res, "TextSub", "#5C5C5C");
                Set(res, "Control", "#FAFAFA");
                Set(res, "ControlHover", "#F0F0F0");
                Set(res, "ControlHoverText", "#1B1B1B");
                Set(res, "ControlPressed", "#E6E6E6");
                Set(res, "DisabledFill", "#E6E6E6");
                Set(res, "ControlBorder", "#8A8A8A");
                Set(res, "Accent", a[Dark1]);
                Set(res, "AccentHover", a[Dark2]);
                Set(res, "AccentText", TextOn(a[Dark1], a[Dark2]));
                Set(res, "Warn", "#9D5D00");
                Set(res, "Urgent", "#C42B1C");
                Set(res, "Danger", "#B3261E");
                Set(res, "DangerSoft", "#FDE7E9");
                Set(res, "Focus", "#1B1B1B");
                Set(res, "Disabled", "#A0A0A0");
            }
        }

        static void Set(ResourceDictionary res, string key, string hex) => Set(res, key, Rgb(hex));

        /// <summary>White or black, whichever stays more readable on both fills (a yellow accent needs black text).</summary>
        internal static Color TextOn(Color fill, Color hover)
        {
            double Worst(Color text) => Math.Min(Contrast(text, fill), Contrast(text, hover));
            return Worst(Colors.White) >= Worst(Colors.Black) ? Colors.White : Colors.Black;
        }

        /// <summary>WCAG contrast ratio of two opaque colours, 1 to 21.</summary>
        internal static double Contrast(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        static double Luminance(Color c)
        {
            double Channel(byte v)
            {
                double s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        static Color Rgb(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        // Index into the Windows accent palette: three lighter shades, the accent, three darker shades.
        const int Accent = 3, Dark1 = 4, Dark2 = 5;

        // Windows default blue, used when the palette cannot be read.
        static readonly string[] DefaultPalette = { "#99EBFF", "#4CC2FF", "#0091F8", "#0078D4", "#005FB8", "#004275", "#002642" };

        /// <summary>The user's accent colour in the seven shades Windows itself derives from it.</summary>
        public static Color[] AccentPalette()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
                    if (k?.GetValue("AccentPalette") is byte[] b && b.Length >= 28)
                    {
                        var p = new Color[7];
                        for (int i = 0; i < 7; i++) p[i] = Color.FromRgb(b[i * 4], b[i * 4 + 1], b[i * 4 + 2]);
                        return p;
                    }
            }
            catch (Exception) { }
            var d = new Color[7];
            for (int i = 0; i < 7; i++) d[i] = Rgb(DefaultPalette[i]);
            return d;
        }

        static void Set(ResourceDictionary res, string key, Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            res[key] = b;
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        const int DwmwaUseImmersiveDarkMode = 20;
        // Windows 10 before 20H1 knows the same attribute under this id.
        const int DwmwaUseImmersiveDarkModeOld = 19;
        const int DwmwaCaptionColor = 35;
        const int DwmwaWindowCornerPreference = 33;
        const int DwmwcpRound = 2;

        /// <summary>Dark title bar and a caption matching the window background; skipped where Windows lacks it.</summary>
        public static void ApplyTitleBar(Window w)
        {
            try
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                if (hwnd == IntPtr.Zero) return;
                int dark = IsDark && !SystemParameters.HighContrast ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeOld, ref dark, sizeof(int));
                // The app draws its own title bar; keep the rounded Windows 11 corners (ignored on Windows 10).
                int round = DwmwcpRound;
                DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref round, sizeof(int));
                if (SystemParameters.HighContrast) return;
                if (!(w.TryFindResource("Bg") is SolidColorBrush bg)) return;
                var c = bg.Color;
                int colorRef = c.R | (c.G << 8) | (c.B << 16);
                DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref colorRef, sizeof(int));
            }
            catch (Exception) { }
        }

        /// <summary>Calls onChange when the Windows theme, accent or contrast changes; returns the unsubscribe call.</summary>
        public static Action Watch(Action onChange)
        {
            UserPreferenceChangedEventHandler handler = (s, e) =>
            {
                if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color ||
                    e.Category == UserPreferenceCategory.VisualStyle ||
                    e.Category == UserPreferenceCategory.Accessibility)
                    // Background: after WPF has refreshed SystemParameters.HighContrast and SystemColors.
                    Application.Current?.Dispatcher.BeginInvoke(onChange, DispatcherPriority.Background);
            };
            SystemEvents.UserPreferenceChanged += handler;
            return () => SystemEvents.UserPreferenceChanged -= handler;
        }
    }
}
