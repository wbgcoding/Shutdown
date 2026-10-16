using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
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
                Set(res, "ControlPressed", SystemColors.HighlightColor);
                Set(res, "ControlBorder", SystemColors.WindowTextColor);
                Set(res, "Accent", SystemColors.HighlightColor);
                Set(res, "AccentHover", SystemColors.HighlightColor);
                Set(res, "AccentText", SystemColors.HighlightTextColor);
                Set(res, "AccentSoft", SystemColors.WindowColor);
                Set(res, "AccentFg", SystemColors.WindowTextColor);
                Set(res, "Danger", SystemColors.WindowTextColor);
                Set(res, "DangerSoft", SystemColors.WindowColor);
                Set(res, "Focus", SystemColors.HighlightColor);
                Set(res, "Disabled", SystemColors.GrayTextColor);
                return;
            }

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
                Set(res, "ControlPressed", "#2F2F2F");
                Set(res, "ControlBorder", "#4A4A4A");
                // Same roles WinUI uses for accent fills in dark mode.
                Set(res, "Accent", a[Light2]);
                Set(res, "AccentHover", a[Light3]);
                Set(res, "AccentText", Colors.Black);
                Set(res, "AccentSoft", Blend(a[Light2], Rgb("#2B2B2B"), 0.18));
                Set(res, "AccentFg", a[Light3]);
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
                Set(res, "ControlPressed", "#E6E6E6");
                Set(res, "ControlBorder", "#D3D3D3");
                Set(res, "Accent", a[Dark1]);
                Set(res, "AccentHover", a[Dark2]);
                Set(res, "AccentText", Colors.White);
                Set(res, "AccentSoft", Blend(a[Accent], Colors.White, 0.12));
                Set(res, "AccentFg", a[Dark2]);
                Set(res, "Danger", "#B3261E");
                Set(res, "DangerSoft", "#FDE7E9");
                Set(res, "Focus", "#1B1B1B");
                Set(res, "Disabled", "#A0A0A0");
            }
        }

        static void Set(ResourceDictionary res, string key, string hex) => Set(res, key, Rgb(hex));

        static Color Rgb(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        static Color Blend(Color fg, Color bg, double amount) => Color.FromRgb(
            (byte)(fg.R * amount + bg.R * (1 - amount)),
            (byte)(fg.G * amount + bg.G * (1 - amount)),
            (byte)(fg.B * amount + bg.B * (1 - amount)));

        // Index into the Windows accent palette: three lighter shades, the accent, three darker shades.
        const int Light3 = 0, Light2 = 1, Light1 = 2, Accent = 3, Dark1 = 4, Dark2 = 5, Dark3 = 6;

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
                    Application.Current?.Dispatcher.BeginInvoke(onChange);
            };
            SystemEvents.UserPreferenceChanged += handler;
            return () => SystemEvents.UserPreferenceChanged -= handler;
        }
    }
}
