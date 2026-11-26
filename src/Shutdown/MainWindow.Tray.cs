using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Shutdown.Core;
using Shutdown.Ui;

namespace Shutdown
{
    public partial class MainWindow
    {
        ITrayIcon _trayIcon;

        internal Func<Action, Func<ContextMenu>, ITrayIcon> CreateTrayIcon { get; set; } =
            (restore, createMenu) => new TrayIcon(restore, createMenu);

        internal bool IsInTray { get; private set; }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (WindowState != WindowState.Minimized || !IsVisible) return;

            _trayIcon ??= CreateTrayIcon(BringToFront, CreateTrayMenu);
            _trayIcon.Show(Title);
            Hide();
            IsInTray = true;
        }

        void HideTrayIcon()
        {
            if (!IsInTray) return;
            IsInTray = false;
            _trayIcon.Hide();
        }

        void RestoreWithoutFocus()
        {
            ShowNoActivate(this);
            if (IsVisible) HideTrayIcon();
        }

        void DisposeTrayIcon()
        {
            IsInTray = false;
            _trayIcon?.Dispose();
            _trayIcon = null;
        }

        ContextMenu CreateTrayMenu()
        {
            var menu = new ContextMenu();
            AutomationProperties.SetAutomationId(menu, "TrayMenu");
            menu.Items.Add(TrayMenuItem("TrayOpen", Loc.I["TrayOpen"], BringToFront));
            if (_pending != null)
            {
                var extend = TrayMenuItem("TrayExtend15", (string)Extend15Button.Content, () =>
                    RunTrayScheduleAction(() => OnExtend15(null, null)));
                AutomationProperties.SetName(extend, AutomationProperties.GetName(Extend15Button));
                menu.Items.Add(extend);
                menu.Items.Add(TrayMenuItem("TrayAbort", Loc.I["Abort"], () =>
                    RunTrayScheduleAction(() => OnAbort(null, null))));
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(TrayMenuItem("TrayExit", Loc.I["TrayExit"], Close));
            return menu;
        }

        void RunTrayScheduleAction(Action action)
        {
            if (_pending == null) return;
            action();
        }

        static MenuItem TrayMenuItem(string id, string header, Action click)
        {
            var item = new MenuItem { Header = header };
            AutomationProperties.SetAutomationId(item, id);
            item.Click += (s, e) => click();
            return item;
        }
    }
}
