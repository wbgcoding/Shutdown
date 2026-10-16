using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;

namespace Shutdown.Core
{
    /// <summary>UI strings for the active language; bindings to the indexer update live on a switch.</summary>
    public sealed class Loc : INotifyPropertyChanged
    {
        // Languages must be initialised before I, the constructor reads it.
        public static readonly string[] Languages = { "en", "de" };
        public static readonly Loc I = new Loc();

        readonly Dictionary<string, Dictionary<string, string>> _tables = new Dictionary<string, Dictionary<string, string>>();
        string _lang = "en";

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler Changed;

        Loc()
        {
            foreach (var l in Languages) _tables[l] = LoadTable(l);
        }

        public static Dictionary<string, string> LoadTable(string lang)
        {
            var asm = Assembly.GetExecutingAssembly();
            var table = new Dictionary<string, string>();
            using (var s = asm.GetManifestResourceStream("Shutdown.Lang." + lang + ".resources"))
            {
                if (s == null) return table;
                using (var r = new ResourceReader(s))
                    foreach (DictionaryEntry e in r) table[(string)e.Key] = e.Value as string ?? "";
            }
            return table;
        }

        /// <summary>Active language code (en or de).</summary>
        public string Language => _lang;

        public CultureInfo Culture
        {
            get
            {
                var cur = CultureInfo.CurrentCulture;
                if (cur.TwoLetterISOLanguageName == _lang) return cur;
                return CultureInfo.GetCultureInfo(_lang == "de" ? "de-DE" : "en-US");
            }
        }

        /// <summary>Sets the language: system (the app's normal mode) or an explicit en/de for tests.</summary>
        public void Apply(string setting)
        {
            string lang = setting == "system" || !Languages.Contains(setting) ? Resolve(CultureInfo.CurrentUICulture) : setting;
            if (lang == _lang && _applied) return;
            _applied = true;
            _lang = lang;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            Changed?.Invoke(this, EventArgs.Empty);
        }
        bool _applied;

        public static string Resolve(CultureInfo ui) => ui.TwoLetterISOLanguageName == "de" ? "de" : "en";

        public string this[string key]
        {
            get
            {
                if (_tables[_lang].TryGetValue(key, out var v)) return v;
                if (_tables["en"].TryGetValue(key, out v)) return v;
                return key;
            }
        }

        public string F(string key, params object[] args) => string.Format(Culture, this[key], args);

        /// <summary>Short human span, e.g. "1 h 25 min".</summary>
        public string Span(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            var parts = new List<string>();
            if (t.Days > 0) parts.Add(F("SpanDays", t.Days));
            if (t.Hours > 0) parts.Add(F("SpanHours", t.Hours));
            if (t.Minutes > 0 && t.Days == 0) parts.Add(F("SpanMinutes", t.Minutes));
            if (parts.Count == 0) parts.Add(t.TotalSeconds >= 1 ? F("SpanSeconds", (int)t.TotalSeconds) : F("SpanSeconds", 0));
            return string.Join(" ", parts);
        }

        /// <summary>Countdown clock, e.g. "1:24:31" or "2 d 03:10:00".</summary>
        public string Clock(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            string hms = t.Days > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", t.Hours, t.Minutes, t.Seconds)
                : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", t.Hours, t.Minutes, t.Seconds);
            return t.Days > 0 ? F("ClockDays", t.Days, hms) : hms;
        }

        public string DateText(DateTime d) => d.ToString("ddd", Culture) + ", " + d.ToString("d", Culture);

        public string TimeText(DateTime d) => d.ToString("t", Culture);
    }
}
