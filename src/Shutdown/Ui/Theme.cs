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
            IsDark = Override == "dark" || (Override != "light" && SystemPrefersDark());

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
                Set(res, "Accent", "#A996FF");
                Set(res, "AccentHover", "#B9A9FF");
                Set(res, "AccentText", "#1A1433");
                Set(res, "AccentSoft", "#352F52");
                Set(res, "AccentFg", "#CFC4FF");
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
                Set(res, "Accent", "#6A45E8");
                Set(res, "AccentHover", "#5A36D6");
                Set(res, "AccentText", "#FFFFFF");
                Set(res, "AccentSoft", "#EFEBFF");
                Set(res, "AccentFg", "#4B2BB8");
                Set(res, "Danger", "#B3261E");
                Set(res, "DangerSoft", "#FDE7E9");
                Set(res, "Focus", "#1B1B1B");
                Set(res, "Disabled", "#A0A0A0");
            }
        }

        static void Set(ResourceDictionary res, string key, string hex) =>
            Set(res, key, (Color)ColorConverter.ConvertFromString(hex));

        static void Set(ResourceDictionary res, string key, Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            res[key] = b;
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        const int DwmwaUseImmersiveDarkMode = 20;
        const int DwmwaCaptionColor = 35;

        /// <summary>Dark title bar and a caption matching the window background; skipped where Windows lacks it.</summary>
        public static void ApplyTitleBar(Window w)
        {
            try
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                if (hwnd == IntPtr.Zero) return;
                int dark = IsDark && !SystemParameters.HighContrast ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
                if (SystemParameters.HighContrast) return;
                if (!(w.TryFindResource("Bg") is SolidColorBrush bg)) return;
                var c = bg.Color;
                int colorRef = c.R | (c.G << 8) | (c.B << 16);
                DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref colorRef, sizeof(int));
            }
            catch (Exception) { }
        }

        public static void Watch(Action onChange)
        {
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color ||
                    e.Category == UserPreferenceCategory.Accessibility)
                    Application.Current?.Dispatcher.BeginInvoke(onChange);
            };
        }
    }
}
