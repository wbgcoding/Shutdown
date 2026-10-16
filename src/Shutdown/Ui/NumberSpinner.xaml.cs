using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Shutdown.Core;

namespace Shutdown.Ui
{
    /// <summary>Large number field with up/down arrows, mouse wheel and arrow keys.</summary>
    public partial class NumberSpinner : UserControl
    {
        int _value;
        public int Minimum { get; set; }
        public int Maximum { get; set; } = 99;
        /// <summary>Stepping past an end continues at the other end (clock fields); typed values are clamped.</summary>
        public bool Wrap { get; set; }
        public int Digits { get; set; } = 2;
        public int WheelStep { get; set; } = 1;

        public event EventHandler ValueChanged;

        public NumberSpinner()
        {
            InitializeComponent();
            Render();
        }

        public int Value
        {
            get => _value;
            set
            {
                int v = Clamp(value);
                bool changed = v != _value;
                _value = v;
                Render();
                if (changed) ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Sets AutomationIds of the field (key) and arrows (keyUp, keyDown).</summary>
        public string AutomationKey
        {
            set
            {
                // The container would otherwise inherit x:Name as a duplicate id.
                AutomationProperties.SetAutomationId(this, value + "Spinner");
                AutomationProperties.SetAutomationId(Box, value);
                AutomationProperties.SetAutomationId(UpButton, value + "Up");
                AutomationProperties.SetAutomationId(DownButton, value + "Down");
            }
        }

        /// <summary>Visible caption under the field; also the accessible name.</summary>
        public string Caption
        {
            set
            {
                CaptionText.Text = value;
                AutomationProperties.SetName(Box, value);
                AutomationProperties.SetName(UpButton, Loc.I.F("IncreaseFmt", value));
                AutomationProperties.SetName(DownButton, Loc.I.F("DecreaseFmt", value));
            }
        }

        int Clamp(int v) => Math.Max(Minimum, Math.Min(Maximum, v));

        void Render()
        {
            string text = _value.ToString(new string('0', Math.Max(1, Digits)), CultureInfo.InvariantCulture);
            if (Box.Text != text) Box.Text = text;
        }

        void Step(int delta)
        {
            int v = _value + delta;
            if (Wrap)
            {
                int span = Maximum - Minimum + 1;
                v = ((v - Minimum) % span + span) % span + Minimum;
            }
            Value = v;
        }

        void Commit()
        {
            if (int.TryParse(Box.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int v)) Value = v;
            else Render();
        }

        void OnUp(object sender, RoutedEventArgs e) => Step(1);
        void OnDown(object sender, RoutedEventArgs e) => Step(-1);

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            Step(e.Delta > 0 ? WheelStep : -WheelStep);
            e.Handled = true;
        }

        // ASCII only: char.IsDigit also accepts other scripts' digits, which the parser rejects.
        static bool IsDigits(string s) => s.Length > 0 && s.All(c => c >= '0' && c <= '9');

        void OnPreviewText(object sender, TextCompositionEventArgs e) => e.Handled = !IsDigits(e.Text);

        void OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            var text = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string;
            if (text == null || !IsDigits(text)) e.CancelCommand();
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Up: Commit(); Step(1); e.Handled = true; break;
                case Key.Down: Commit(); Step(-1); e.Handled = true; break;
                case Key.PageUp: Commit(); Step(10); e.Handled = true; break;
                case Key.PageDown: Commit(); Step(-10); e.Handled = true; break;
                case Key.Enter: Commit(); Box.SelectAll(); e.Handled = true; break;
            }
        }

        // Typed digits count at once so the summary follows; clamping waits for commit.
        void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!int.TryParse(Box.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int v)) return;
            if (v < Minimum || v > Maximum || v == _value) return;
            _value = v;
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e) => Commit();

        void OnGotFocus(object sender, KeyboardFocusChangedEventArgs e) =>
            Dispatcher.BeginInvoke(new Action(Box.SelectAll));
    }
}
