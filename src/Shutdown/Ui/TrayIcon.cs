using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Forms = System.Windows.Forms;

namespace Shutdown.Ui
{
    internal interface ITrayIcon : IDisposable
    {
        void Show(string text);
        void Hide();
    }

    internal sealed class TrayIcon : ITrayIcon
    {
        const int MaxTooltipLength = 63;
        readonly Action _restore;
        readonly Func<ContextMenu> _createMenu;
        readonly Icon _image;
        readonly Forms.NotifyIcon _icon;
        Window _menuHost;
        ContextMenu _menu;

        public TrayIcon(Action restore, Func<ContextMenu> createMenu)
        {
            _restore = restore;
            _createMenu = createMenu;
            using (var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("Shutdown.TrayIcon.ico"))
                _image = new Icon(stream, 16, 16);
            // NotifyIcon registers for TaskbarCreated and re-adds a visible icon after Explorer restarts.
            _icon = new Forms.NotifyIcon { Icon = _image };
            _icon.MouseClick += OnMouseClick;
        }

        public void Show(string text)
        {
            _icon.Text = text.Length <= MaxTooltipLength ? text : text.Substring(0, MaxTooltipLength);
            _icon.Visible = true;
        }

        public void Hide()
        {
            _icon.Visible = false;
            CloseMenu();
        }

        void OnMouseClick(object sender, Forms.MouseEventArgs e)
        {
            if (e.Button == Forms.MouseButtons.Left) _restore();
            else if (e.Button == Forms.MouseButtons.Right) OpenMenu();
        }

        void OpenMenu()
        {
            if (_menu != null) return;

            // A foreground window lets WPF capture the mouse and dismiss the popup on outside clicks.
            _menuHost ??= new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                Opacity = 0,
                Width = 1,
                Height = 1,
                ShowInTaskbar = false,
                ShowActivated = false
            };
            _menuHost.Show();
            _menuHost.Activate();

            _menu = _createMenu();
            _menu.PlacementTarget = _menuHost;
            _menu.Placement = PlacementMode.MousePoint;
            _menu.StaysOpen = false;
            _menu.Closed += OnMenuClosed;
            _menu.IsOpen = true;
        }

        void OnMenuClosed(object sender, RoutedEventArgs e) => CloseMenu();

        void CloseMenu()
        {
            if (_menu == null) return;
            _menu.Closed -= OnMenuClosed;
            _menu.IsOpen = false;
            _menu = null;
            _menuHost.Hide();
        }

        public void Dispose()
        {
            _icon.Dispose();
            _image.Dispose();
            CloseMenu();
            _menuHost?.Close();
        }
    }
}
