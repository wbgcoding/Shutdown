using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using Shutdown.Core;
using Shutdown.Ui;
using Xunit;

namespace Shutdown.Tests
{
    [Collection("window")]
    [Trait("Layer", "Ui")]
    public class SpinnerTests : IDisposable
    {
        public void Dispose() => Sta.Run(() => Loc.I.Apply("en"));

        static NumberSpinner Make(int min = 0, int max = 99, bool wrap = false)
        {
            Loc.I.Apply("en");
            return new NumberSpinner { Minimum = min, Maximum = max, Wrap = wrap };
        }

        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, b));

        static bool Press(NumberSpinner s, Key k)
        {
            using (var src = new HwndSource(new HwndSourceParameters()))
            {
                var e = new KeyEventArgs(Keyboard.PrimaryDevice, src, 0, k) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                s.Box.RaiseEvent(e);
                return e.Handled;
            }
        }

        static bool TypeText(NumberSpinner s, string text)
        {
            var e = new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, s.Box, text))
            { RoutedEvent = TextCompositionManager.PreviewTextInputEvent };
            s.Box.RaiseEvent(e);
            return e.Handled;
        }

        static bool Paste(NumberSpinner s, string text)
        {
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, text);
            var e = new DataObjectPastingEventArgs(data, false, DataFormats.UnicodeText) { RoutedEvent = DataObject.PastingEvent };
            s.Box.RaiseEvent(e);
            return e.CommandCancelled;
        }

        static void Wheel(NumberSpinner s, int delta) =>
            s.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = UIElement.PreviewMouseWheelEvent });

        [Fact]
        public void A_new_spinner_shows_two_digits_from_zero_to_ninety_nine() => Sta.Run(() =>
        {
            var s = new NumberSpinner();
            Assert.Equal(0, s.Value);
            Assert.Equal(0, s.Minimum);
            Assert.Equal(99, s.Maximum);
            Assert.False(s.Wrap);
            Assert.Equal(1, s.WheelStep);
            Assert.Equal("00", s.Box.Text);
        });

        [Theory]
        [InlineData(1, 7, "7")]
        [InlineData(2, 7, "07")]
        [InlineData(3, 7, "007")]
        [InlineData(0, 7, "7")]
        [InlineData(-4, 7, "7")]
        [InlineData(2, 123, "123")]
        public void Digits_pads_the_field(int digits, int value, string text) => Sta.Run(() =>
        {
            var s = Make(max: 999);
            s.Digits = digits;
            s.Value = value;
            Assert.Equal(text, s.Box.Text);
        });

        [Theory]
        [InlineData(5, 5)]
        [InlineData(100, 99)]
        [InlineData(-1, 0)]
        [InlineData(int.MaxValue, 99)]
        [InlineData(int.MinValue, 0)]
        public void Value_is_clamped(int set, int expected) => Sta.Run(() =>
        {
            var s = Make();
            s.Value = set;
            Assert.Equal(expected, s.Value);
        });

        [Fact]
        public void The_event_fires_only_for_a_real_change() => Sta.Run(() =>
        {
            var s = Make();
            int n = 0;
            s.ValueChanged += (a, b) => n++;
            s.Value = 0;
            Assert.Equal(0, n);
            s.Value = 5;
            Assert.Equal(1, n);
            s.Value = 5;
            Assert.Equal(1, n);
            s.Value = 500;
            Assert.Equal(2, n);
            s.Value = 99;
            Assert.Equal(2, n);
            s.Value = -50;
            Assert.Equal(3, n);
        });

        [Fact]
        public void Typed_digits_count_at_once_and_are_clamped() => Sta.Run(() =>
        {
            var s = Make();
            int n = 0;
            s.ValueChanged += (a, b) => n++;
            s.Box.Text = "42";
            Assert.Equal(42, s.Value);
            Assert.Equal(1, n);
            s.Box.Text = "007";
            Assert.Equal(7, s.Value);
            s.Box.Text = "500";
            Assert.Equal(99, s.Value);
            // The field keeps what was typed until it is committed.
            Assert.Equal("500", s.Box.Text);
            Assert.Equal(3, n);
        });

        [Theory]
        [InlineData("abc")]
        [InlineData("")]
        [InlineData("-5")]
        [InlineData("1 2")]
        [InlineData("99999999999")]
        [InlineData("+3")]
        public void Unreadable_text_leaves_the_value_alone(string text) => Sta.Run(() =>
        {
            var s = Make();
            s.Value = 12;
            int n = 0;
            s.ValueChanged += (a, b) => n++;
            s.Box.Text = text;
            Assert.Equal(12, s.Value);
            Assert.Equal(0, n);
        });

        [Fact]
        public void Enter_commits_and_reformats_the_field() => Sta.Run(() =>
        {
            var s = Make();
            s.Box.Text = "7";
            Assert.True(Press(s, Key.Enter));
            Assert.Equal("07", s.Box.Text);
            s.Box.Text = "500";
            Press(s, Key.Enter);
            Assert.Equal("99", s.Box.Text);
            s.Box.Text = "abc";
            Press(s, Key.Enter);
            Assert.Equal("99", s.Box.Text);
            Assert.Equal(99, s.Value);
        });

        [Fact]
        public void The_typed_maximum_applies_only_when_text_is_committed() => Sta.Run(() =>
        {
            var s = Make(max: 59);
            s.TypedMaximum = 5999;
            s.Box.Text = "599";
            Assert.Equal(0, s.Value);
            s.Box.Text = "5999";
            Press(s, Key.Enter);
            Assert.Equal(5999, s.Value);
            s.Box.Text = "9999";
            Press(s, Key.Enter);
            Assert.Equal("5999", s.Box.Text);
            s.Value = 90;
            Assert.Equal(59, s.Value);
        });

        [Fact]
        public void Arrow_keys_step_from_the_typed_value() => Sta.Run(() =>
        {
            var s = Make();
            s.Box.Text = "5";
            Assert.True(Press(s, Key.Up));
            Assert.Equal(6, s.Value);
            Assert.Equal("06", s.Box.Text);
            Assert.True(Press(s, Key.Down));
            Assert.Equal(5, s.Value);
            Assert.True(Press(s, Key.PageUp));
            Assert.Equal(15, s.Value);
            Assert.True(Press(s, Key.PageDown));
            Assert.Equal(5, s.Value);
        });

        [Fact]
        public void Other_keys_are_left_alone() => Sta.Run(() =>
        {
            var s = Make();
            Assert.False(Press(s, Key.A));
            Assert.False(Press(s, Key.Left));
            Assert.Equal(0, s.Value);
        });

        [Fact]
        public void Keys_stop_at_the_ends_without_wrap_and_wrap_with_it() => Sta.Run(() =>
        {
            var s = Make();
            s.Value = 95;
            Press(s, Key.PageUp);
            Assert.Equal(99, s.Value);
            s.Value = 3;
            Press(s, Key.PageDown);
            Assert.Equal(0, s.Value);
            Press(s, Key.Down);
            Assert.Equal(0, s.Value);

            var c = Make(max: 59, wrap: true);
            c.Value = 55;
            Press(c, Key.PageUp);
            Assert.Equal(5, c.Value);
            Press(c, Key.PageDown);
            Assert.Equal(55, c.Value);
        });

        [Fact]
        public void Wrap_respects_a_minimum_above_zero() => Sta.Run(() =>
        {
            var s = Make(min: 5, max: 8, wrap: true);
            s.Value = 8;
            Click(s.UpButton);
            Assert.Equal(5, s.Value);
            Click(s.DownButton);
            Assert.Equal(8, s.Value);
        });

        [Fact]
        public void Carry_replaces_wrapping_when_stepping_from_a_limit() => Sta.Run(() =>
        {
            var s = Make(max: 59, wrap: true);
            s.TypedMaximum = 5999;
            s.CarryInsteadOfWrap = true;
            int carry = 0;
            s.Carry += (sender, direction) => carry = direction;

            s.Value = 55;
            Press(s, Key.PageUp);
            Assert.Equal(59, s.Value);
            Assert.Equal(0, carry);
            Wheel(s, 120);
            Assert.Equal(59, s.Value);
            Assert.Equal(1, carry);
            s.Value = 0;
            Wheel(s, -120);
            Assert.Equal(0, s.Value);
            Assert.Equal(-1, carry);
        });

        [Fact]
        public void Arrow_buttons_step_by_one_and_stop_at_the_ends() => Sta.Run(() =>
        {
            var s = Make(max: 2);
            Click(s.UpButton);
            Click(s.UpButton);
            Click(s.UpButton);
            Assert.Equal(2, s.Value);
            Click(s.DownButton);
            Click(s.DownButton);
            Click(s.DownButton);
            Assert.Equal(0, s.Value);
        });

        [Fact]
        public void The_wheel_steps_by_the_wheel_step_and_is_handled() => Sta.Run(() =>
        {
            var s = Make();
            s.WheelStep = 5;
            s.Value = 20;
            Wheel(s, 120);
            Assert.Equal(25, s.Value);
            Wheel(s, -240);
            Assert.Equal(15, s.Value);
            Wheel(s, 0);
            Assert.Equal(15, s.Value);
        });

        [Fact]
        public void Touchpad_deltas_add_up_to_one_step_per_notch() => Sta.Run(() =>
        {
            var s = Make();
            s.Value = 20;
            for (int i = 0; i < 4; i++) Wheel(s, 30);
            Assert.Equal(21, s.Value);
            Wheel(s, 60);
            Assert.Equal(21, s.Value);
            Wheel(s, -90);
            Wheel(s, -90);
            Assert.Equal(20, s.Value);
        });

        [Fact]
        public void The_wheel_wraps_on_clock_fields() => Sta.Run(() =>
        {
            var s = Make(max: 23, wrap: true);
            Wheel(s, -120);
            Assert.Equal(23, s.Value);
            Wheel(s, 120);
            Assert.Equal(0, s.Value);
        });

        [Theory]
        [InlineData("5", false)]
        [InlineData("0", false)]
        [InlineData("a", true)]
        [InlineData("-", true)]
        [InlineData(" ", true)]
        [InlineData(".", true)]
        [InlineData("", true)]
        [InlineData("٣", true)]
        [InlineData("1a", true)]
        public void Only_ascii_digits_can_be_typed(string text, bool blocked) => Sta.Run(() =>
        {
            Assert.Equal(blocked, TypeText(Make(), text));
        });

        [Theory]
        [InlineData("12", false)]
        [InlineData("007", false)]
        [InlineData("1x", true)]
        [InlineData("-1", true)]
        [InlineData("", true)]
        [InlineData("1 2", true)]
        [InlineData("٣", true)]
        [InlineData(" 15\r\n", false)]
        [InlineData("\t", true)]
        public void Only_ascii_digits_can_be_pasted(string text, bool cancelled) => Sta.Run(() =>
        {
            Assert.Equal(cancelled, Paste(Make(), text));
        });

        [Fact]
        public void Pasted_number_arrives_without_surrounding_whitespace() => Sta.Run(() =>
        {
            var s = Make();
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, "15\r\n");
            var e = new DataObjectPastingEventArgs(data, false, DataFormats.UnicodeText) { RoutedEvent = DataObject.PastingEvent };
            s.Box.RaiseEvent(e);
            Assert.False(e.CommandCancelled);
            Assert.Equal("15", e.DataObject.GetData(DataFormats.UnicodeText));
        });

        [Fact]
        public void Space_is_swallowed() => Sta.Run(() =>
        {
            Assert.True(Press(Make(), Key.Space));
        });

        [Fact]
        public void A_paste_without_text_is_cancelled() => Sta.Run(() =>
        {
            var s = Make();
            var data = new DataObject();
            data.SetData(DataFormats.Rtf, "x");
            var e = new DataObjectPastingEventArgs(data, false, DataFormats.Rtf) { RoutedEvent = DataObject.PastingEvent };
            s.Box.RaiseEvent(e);
            Assert.True(e.CommandCancelled);
        });

        [Fact]
        public void Automation_ids_come_from_the_key() => Sta.Run(() =>
        {
            var s = Make();
            s.AutomationKey = "Foo";
            Assert.Equal("FooSpinner", AutomationProperties.GetAutomationId(s));
            Assert.Equal("Foo", AutomationProperties.GetAutomationId(s.Box));
            Assert.Equal("FooUp", AutomationProperties.GetAutomationId(s.UpButton));
            Assert.Equal("FooDown", AutomationProperties.GetAutomationId(s.DownButton));
        });

        [Fact]
        public void The_caption_names_the_field_and_its_arrows() => Sta.Run(() =>
        {
            var s = Make();
            s.Caption = "Hours";
            Assert.Equal("Hours", s.CaptionText.Text);
            Assert.Equal("Hours", AutomationProperties.GetName(s.Box));
            Assert.Equal("Increase Hours", AutomationProperties.GetName(s.UpButton));
            Assert.Equal("Decrease Hours", AutomationProperties.GetName(s.DownButton));
            Loc.I.Apply("de");
            s.Caption = "Stunden";
            Assert.Equal(Loc.I.F("IncreaseFmt", "Stunden"), AutomationProperties.GetName(s.UpButton));
            Assert.NotEqual("Increase Stunden", AutomationProperties.GetName(s.UpButton));
        });

        [Fact]
        public void The_field_is_limited_to_three_characters() => Sta.Run(() =>
        {
            Assert.Equal(3, Make().Box.MaxLength);
        });
    }
}
