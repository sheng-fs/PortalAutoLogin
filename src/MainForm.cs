using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PortalAutoLogin
{
    public class MainForm : Form
    {
        private enum UiState { Checking, Online, Offline, LoggingIn, Paused, Error }

        private readonly AppConfig _cfg;
        private readonly bool _startHidden;
        private bool _allowShow;
        private bool _exiting;
        private bool _busy;
        private bool _suppressToggle;
        private bool _balloonShown;

        private Dot _dot;
        private Label _title;
        private Label _detail;
        private Label _lastLogin;
        private Label _ipValue;
        private Label _accountValue;
        private Label _countValue;
        private Label _intervalValue;
        private RoundedButton _btnLogin;
        private RoundedButton _btnOpen;
        private RoundedButton _btnPause;
        private ToggleSwitch _toggleAuto;

        private NotifyIcon _tray;
        private ContextMenuStrip _menu;
        private ToolStripMenuItem _menuPause;
        private Icon _iconOk;
        private Icon _iconIdle;
        private Icon _iconBusy;
        private Icon _iconError;

        private System.Threading.Timer _timer;
        private object _tickLock = new object();

        public MainForm(AppConfig cfg, bool startHidden)
        {
            _cfg = cfg;
            _startHidden = startHidden;
            BuildUi();
            BuildTray();
            UpdatePauseUi();
            UpdateInfo();
            SetStatus(UiState.Checking, "正在检测网络", "正在检查当前网络认证状态…", Theme.Idle);
            _timer = new System.Threading.Timer(OnTick, null, 1200, Math.Max(5, _cfg.IntervalSeconds) * 1000);
            System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        }

        public void ShowFromTray()
        {
            _allowShow = true;
            ShowInTaskbar = true;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        private void BuildUi()
        {
            Text = "PortalAutoLogin 门户自动登录";
            ClientSize = new Size(540, 604);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Theme.Bg;
            Font = Theme.UI(9.5f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var pic = new PictureBox();
            pic.Location = new Point(24, 20);
            pic.Size = new Size(42, 42);
            pic.SizeMode = PictureBoxSizeMode.Zoom;
            try { pic.Image = Icon.ExtractAssociatedIcon(Application.ExecutablePath).ToBitmap(); } catch { }
            Controls.Add(pic);

            var lblName = new Label();
            lblName.Text = "PortalAutoLogin";
            lblName.Font = Theme.UI(14.5f, FontStyle.Bold);
            lblName.ForeColor = Theme.Text;
            lblName.Location = new Point(78, 17);
            lblName.Size = new Size(260, 28);
            lblName.BackColor = Color.Transparent;
            Controls.Add(lblName);

            var lblSub = new Label();
            lblSub.Text = "门户自动登录";
            lblSub.ForeColor = Theme.SubText;
            lblSub.Location = new Point(80, 45);
            lblSub.Size = new Size(260, 20);
            lblSub.BackColor = Color.Transparent;
            Controls.Add(lblSub);

            var statusCard = new CardPanel();
            statusCard.Location = new Point(24, 84);
            statusCard.Size = new Size(492, 150);
            Controls.Add(statusCard);

            _dot = new Dot();
            _dot.Location = new Point(24, 28);
            statusCard.Controls.Add(_dot);

            _title = new Label();
            _title.Text = "正在检测";
            _title.Font = Theme.UI(17f, FontStyle.Bold);
            _title.ForeColor = Theme.Text;
            _title.Location = new Point(44, 18);
            _title.Size = new Size(420, 32);
            _title.BackColor = Color.Transparent;
            statusCard.Controls.Add(_title);

            _detail = new Label();
            _detail.Text = "";
            _detail.ForeColor = Theme.SubText;
            _detail.Location = new Point(24, 60);
            _detail.Size = new Size(444, 40);
            _detail.BackColor = Color.Transparent;
            statusCard.Controls.Add(_detail);

            _lastLogin = new Label();
            _lastLogin.Text = "";
            _lastLogin.ForeColor = Theme.Faint;
            _lastLogin.Font = Theme.UI(8.8f);
            _lastLogin.Location = new Point(24, 110);
            _lastLogin.Size = new Size(444, 20);
            _lastLogin.BackColor = Color.Transparent;
            statusCard.Controls.Add(_lastLogin);

            var infoCard = new CardPanel();
            infoCard.Location = new Point(24, 250);
            infoCard.Size = new Size(492, 124);
            Controls.Add(infoCard);

            AddInfo(infoCard, "本机 IP", 24, 22, out _ipValue);
            AddInfo(infoCard, "账号", 256, 22, out _accountValue);
            AddInfo(infoCard, "今日自动重登", 24, 70, out _countValue);
            AddInfo(infoCard, "检测间隔", 256, 70, out _intervalValue);

            _btnLogin = new RoundedButton();
            _btnLogin.Text = "立即登录";
            _btnLogin.Location = new Point(24, 392);
            _btnLogin.Size = new Size(140, 40);
            _btnLogin.Click += delegate { LoginNow(); };
            Controls.Add(_btnLogin);

            _btnOpen = new RoundedButton();
            _btnOpen.Text = "打开认证页";
            _btnOpen.Location = new Point(176, 392);
            _btnOpen.Size = new Size(132, 40);
            _btnOpen.FillColor = Color.White;
            _btnOpen.HoverColor = Color.FromArgb(246, 246, 246);
            _btnOpen.DownColor = Color.FromArgb(238, 238, 238);
            _btnOpen.BorderColor = Theme.InputBorder;
            _btnOpen.TextColor = Theme.Text;
            _btnOpen.Click += delegate { OpenPortalPage(); };
            Controls.Add(_btnOpen);

            _btnPause = new RoundedButton();
            _btnPause.Text = "暂停自动登录";
            _btnPause.Location = new Point(320, 392);
            _btnPause.Size = new Size(150, 40);
            _btnPause.FillColor = Color.White;
            _btnPause.HoverColor = Color.FromArgb(246, 246, 246);
            _btnPause.DownColor = Color.FromArgb(238, 238, 238);
            _btnPause.BorderColor = Theme.InputBorder;
            _btnPause.TextColor = Theme.Text;
            _btnPause.Click += delegate { TogglePause(); };
            Controls.Add(_btnPause);

            var autoCard = new CardPanel();
            autoCard.Location = new Point(24, 448);
            autoCard.Size = new Size(492, 64);
            Controls.Add(autoCard);

            _toggleAuto = new ToggleSwitch();
            _toggleAuto.Location = new Point(24, 20);
            _toggleAuto.Checked = AutoStart.IsEnabled();
            _toggleAuto.CheckedChanged += OnAutoToggleChanged;
            autoCard.Controls.Add(_toggleAuto);

            var lblAuto = new Label();
            lblAuto.Text = "开机自动启动";
            lblAuto.Font = Theme.UI(9.5f, FontStyle.Bold);
            lblAuto.ForeColor = Theme.Text;
            lblAuto.Location = new Point(80, 11);
            lblAuto.Size = new Size(380, 20);
            lblAuto.BackColor = Color.Transparent;
            autoCard.Controls.Add(lblAuto);

            var lblAutoSub = new Label();
            lblAutoSub.Text = "登录 Windows 后在后台自动运行，掉线自动重新认证";
            lblAutoSub.ForeColor = Theme.SubText;
            lblAutoSub.Font = Theme.UI(8.6f);
            lblAutoSub.Location = new Point(80, 33);
            lblAutoSub.Size = new Size(390, 20);
            lblAutoSub.BackColor = Color.Transparent;
            autoCard.Controls.Add(lblAutoSub);

            var linkSettings = MakeLink("设置", new Point(24, 534));
            linkSettings.Click += delegate { OpenSettings(); };
            Controls.Add(linkSettings);

            var linkLog = MakeLink("查看日志", new Point(78, 534));
            linkLog.Click += delegate { OpenLog(); };
            Controls.Add(linkLog);

            var lblVersion = new Label();
            lblVersion.Text = "v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            lblVersion.ForeColor = Theme.Faint;
            lblVersion.Font = Theme.UI(8.5f);
            lblVersion.TextAlign = ContentAlignment.MiddleRight;
            lblVersion.Location = new Point(400, 532);
            lblVersion.Size = new Size(116, 20);
            lblVersion.BackColor = Color.Transparent;
            Controls.Add(lblVersion);

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
        }

        private Label MakeLink(string text, Point location)
        {
            var lbl = new Label();
            lbl.Text = text;
            lbl.ForeColor = Theme.Accent;
            lbl.Font = Theme.UI(9f);
            lbl.Location = location;
            lbl.Size = new Size(56, 22);
            lbl.Cursor = Cursors.Hand;
            lbl.BackColor = Color.Transparent;
            lbl.MouseEnter += delegate { lbl.ForeColor = Theme.AccentHover; };
            lbl.MouseLeave += delegate { lbl.ForeColor = Theme.Accent; };
            return lbl;
        }

        private static void AddInfo(Control parent, string caption, int x, int y, out Label valueLabel)
        {
            var cap = new Label();
            cap.Text = caption;
            cap.ForeColor = Theme.SubText;
            cap.Font = Theme.UI(8.6f);
            cap.Location = new Point(x, y);
            cap.Size = new Size(200, 18);
            cap.BackColor = Color.Transparent;
            parent.Controls.Add(cap);

            var val = new Label();
            val.Text = "-";
            val.ForeColor = Theme.Text;
            val.Font = Theme.UI(10f, FontStyle.Bold);
            val.Location = new Point(x, y + 20);
            val.Size = new Size(210, 22);
            val.BackColor = Color.Transparent;
            parent.Controls.Add(val);
            valueLabel = val;
        }

        private void BuildTray()
        {
            _iconOk = MakeBadgeIcon(Color.FromArgb(15, 123, 15));
            _iconIdle = MakeBadgeIcon(Color.FromArgb(120, 120, 120));
            _iconBusy = MakeBadgeIcon(Color.FromArgb(157, 93, 0));
            _iconError = MakeBadgeIcon(Color.FromArgb(196, 43, 28));

            _menu = new ContextMenuStrip();
            _menu.Items.Add("显示主界面", null, delegate { ShowFromTray(); });
            _menu.Items.Add("立即登录", null, delegate { LoginNow(); });
            _menuPause = new ToolStripMenuItem("暂停自动登录", null, delegate { TogglePause(); });
            _menu.Items.Add(_menuPause);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("打开认证页", null, delegate { OpenPortalPage(); });
            _menu.Items.Add("打开日志", null, delegate { OpenLog(); });
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("退出", null, delegate { ExitApp(); });

            _tray = new NotifyIcon();
            _tray.Icon = _iconIdle;
            _tray.Text = "PortalAutoLogin 门户自动登录";
            _tray.ContextMenuStrip = _menu;
            _tray.Visible = true;
            _tray.DoubleClick += delegate { ShowFromTray(); };
        }

        private Icon MakeBadgeIcon(Color dotColor)
        {
            try
            {
                using (var bmp = new Bitmap(16, 16))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.Clear(Color.Transparent);
                        try
                        {
                            using (var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                            {
                                if (appIcon != null) g.DrawIcon(appIcon, new Rectangle(0, 0, 16, 16));
                            }
                        }
                        catch
                        {
                        }
                        using (var bg = new SolidBrush(Color.White))
                        {
                            g.FillEllipse(bg, 8, 8, 8, 8);
                        }
                        using (var dotBrush = new SolidBrush(dotColor))
                        {
                            g.FillEllipse(dotBrush, 9, 9, 6, 6);
                        }
                    }
                    IntPtr h = bmp.GetHicon();
                    try
                    {
                        using (var tmp = Icon.FromHandle(h))
                        {
                            return (Icon)tmp.Clone();
                        }
                    }
                    finally
                    {
                        DestroyIcon(h);
                    }
                }
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        private void SetStatus(UiState state, string title, string detail, Color color)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<UiState, string, string, Color>(SetStatus), state, title, detail, color); } catch { }
                return;
            }
            _dot.DotColor = color;
            _dot.Invalidate();
            _title.Text = title;
            _detail.Text = detail;
            if (_tray != null)
            {
                if (state == UiState.Online) _tray.Icon = _iconOk;
                else if (state == UiState.Error) _tray.Icon = _iconError;
                else if (state == UiState.LoggingIn || state == UiState.Checking) _tray.Icon = _iconBusy;
                else _tray.Icon = _iconIdle;
                _tray.Text = "PortalAutoLogin - " + title;
            }
        }

        private void UpdateInfo()
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(UpdateInfo)); } catch { }
                return;
            }
            var client = new PortalClient(_cfg);
            _ipValue.Text = client.LocalIp();
            _accountValue.Text = string.IsNullOrEmpty(_cfg.Account) ? "未设置" : _cfg.Account;
            _intervalValue.Text = Math.Max(5, _cfg.IntervalSeconds) + " 秒";

            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (_cfg.CountDate != today)
            {
                _cfg.CountDate = today;
                _cfg.TodayCount = 0;
                ConfigStore.Save(_cfg);
            }
            _countValue.Text = _cfg.TodayCount + " 次";
            _lastLogin.Text = string.IsNullOrEmpty(_cfg.LastLogin) ? "尚未自动登录" : "上次自动登录：" + _cfg.LastLogin;
        }

        private void OnAutoToggleChanged(object sender, EventArgs e)
        {
            if (_suppressToggle) return;
            bool on = _toggleAuto.Checked;
            bool ok = on ? AutoStart.Enable(Application.ExecutablePath) : true;
            if (!on) AutoStart.Disable();
            if (on && !ok)
            {
                _suppressToggle = true;
                _toggleAuto.Checked = false;
                _suppressToggle = false;
                MessageBox.Show(this, "无法创建开机自启项，请检查系统权限。", "PortalAutoLogin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _cfg.AutoStart = _toggleAuto.Checked;
            ConfigStore.Save(_cfg);
            Log.Info("开机自启：" + (_cfg.AutoStart ? "开启" : "关闭"));
        }

        private void SyncToggle()
        {
            _suppressToggle = true;
            _toggleAuto.Checked = AutoStart.IsEnabled();
            _suppressToggle = false;
        }

        private void UpdatePauseUi()
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(UpdatePauseUi)); } catch { }
                return;
            }
            bool paused = _cfg.Paused;
            _btnPause.Text = paused ? "继续自动登录" : "暂停自动登录";
            _menuPause.Text = paused ? "继续自动登录" : "暂停自动登录";
            _menuPause.Checked = paused;
            if (paused)
            {
                SetStatus(UiState.Paused, "已暂停", "自动登录已暂停，网络掉线时不会自动认证", Theme.Idle);
            }
        }

        private void TogglePause()
        {
            _cfg.Paused = !_cfg.Paused;
            ConfigStore.Save(_cfg);
            UpdatePauseUi();
            if (!_cfg.Paused)
            {
                SetStatus(UiState.Checking, "正在检测网络", "正在检查当前网络认证状态…", Theme.Idle);
                TriggerCheck();
            }
        }

        private void TriggerCheck()
        {
            try { _timer.Change(200, Math.Max(5, _cfg.IntervalSeconds) * 1000); } catch { }
        }

        private void OnNetworkChanged(object sender, System.Net.NetworkInformation.NetworkAvailabilityEventArgs e)
        {
            if (e.IsAvailable) TriggerCheck();
        }

        private void OnTick(object state)
        {
            lock (_tickLock)
            {
                if (_exiting) return;
                if (_cfg.Paused)
                {
                    SetStatus(UiState.Paused, "已暂停", "自动登录已暂停，网络掉线时不会自动认证", Theme.Idle);
                    return;
                }
                if (_busy) return;
                _busy = true;
            }

            try
            {
                var client = new PortalClient(_cfg);
                bool online = client.IsOnline();
                if (online)
                {
                    SetStatus(UiState.Online, "已连接", "网络认证正常，无需登录", Theme.Ok);
                    UpdateInfo();
                    return;
                }

                SetStatus(UiState.Offline, "未连接", "检测到尚未认证，正在尝试自动登录…", Theme.Warn);
                PortalResult result = client.Login();
                bool ok = result.Success || client.IsOnline();
                if (ok)
                {
                    MarkLogin(true);
                    SetStatus(UiState.Online, "已连接", result.AlreadyOnline ? "当前已在线上" : "自动登录成功，网络已恢复", Theme.Ok);
                }
                else
                {
                    SetStatus(UiState.Error, "登录失败", Shorten(result.Message, 60), Theme.Bad);
                }
                UpdateInfo();
            }
            catch (Exception ex)
            {
                Log.Error("自动检测异常：" + ex.Message);
                SetStatus(UiState.Error, "检测异常", Shorten(ex.Message, 60), Theme.Bad);
            }
            finally
            {
                lock (_tickLock) { _busy = false; }
            }
        }

        private void LoginNow()
        {
            if (_busy)
            {
                SetStatus(UiState.Checking, "正在处理", "上一次检测尚未结束，请稍候…", Theme.Idle);
                return;
            }
            lock (_tickLock)
            {
                if (_busy) return;
                _busy = true;
            }
            _btnLogin.Enabled = false;
            SetStatus(UiState.LoggingIn, "正在登录", "正在向认证服务器提交账号信息…", Theme.Accent);

            Task.Run(delegate
            {
                PortalResult result = null;
                bool ok = false;
                string error = null;
                try
                {
                    var client = new PortalClient(_cfg);
                    if (client.IsOnline())
                    {
                        ok = true;
                        result = new PortalResult();
                        result.AlreadyOnline = true;
                        result.Message = "当前网络已在线，无需登录";
                    }
                    else
                    {
                        result = client.Login();
                        ok = result.Success || client.IsOnline();
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                Ui(delegate
                {
                    _busy = false;
                    _btnLogin.Enabled = true;
                    if (ok)
                    {
                        MarkLogin(false);
                        SetStatus(UiState.Online, "已连接",
                            result != null && result.AlreadyOnline ? "当前网络已在线，无需登录" : "登录成功，网络已恢复", Theme.Ok);
                    }
                    else
                    {
                        string msg = error ?? (result != null ? result.Message : "未知错误");
                        SetStatus(UiState.Error, "登录失败", Shorten(msg, 60), Theme.Bad);
                        string raw = result != null && !string.IsNullOrEmpty(result.Raw) ? Shorten(result.Raw.Trim(), 900) : "";
                        MessageBox.Show(this,
                            "登录未成功。\n\n原因：" + Shorten(msg, 200) + (raw.Length > 0 ? "\n\n服务器响应：\n" + raw : ""),
                            "PortalAutoLogin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    UpdateInfo();
                });
            });
        }

        private void MarkLogin(bool auto)
        {
            _cfg.LastLogin = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (_cfg.CountDate != today)
            {
                _cfg.CountDate = today;
                _cfg.TodayCount = 0;
            }
            if (auto) _cfg.TodayCount++;
            ConfigStore.Save(_cfg);
            Log.Info(auto ? "自动登录成功。" : "手动登录成功。");
        }

        private void OpenPortalPage()
        {
            string url = ConfigStore.EffectiveOpenUrl(_cfg);
            try
            {
                var uri = new Uri(url);
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch
            {
                MessageBox.Show(this, "链接无效：" + url + "\n请在设置中修改。", "PortalAutoLogin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenSettings()
        {
            using (var form = new SettingsForm(_cfg))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    SyncToggle();
                    UpdatePauseUi();
                    UpdateInfo();
                    TriggerCheck();
                }
            }
        }

        private void OpenLog()
        {
            try
            {
                Directory.CreateDirectory(Log.Dir);
                if (!File.Exists(Log.FilePath)) File.WriteAllText(Log.FilePath, "");
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + Log.FilePath + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开日志：" + ex.Message, "PortalAutoLogin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Ui(Action action)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(action);
            }
            catch
            {
            }
        }

        private static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length <= max) return text;
            return text.Substring(0, max) + "…";
        }

        protected override void SetVisibleCore(bool value)
        {
            if (_startHidden && !_allowShow)
            {
                base.SetVisibleCore(false);
                return;
            }
            base.SetVisibleCore(value);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                ShowInTaskbar = false;
                ShowBalloonOnce();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                ShowBalloonOnce();
                return;
            }
            base.OnFormClosing(e);
        }

        private void ShowBalloonOnce()
        {
            if (_balloonShown || _tray == null) return;
            _balloonShown = true;
            try
            {
                _tray.BalloonTipTitle = "PortalAutoLogin 仍在后台运行";
                _tray.BalloonTipText = "程序已最小化到托盘，会继续自动保持门户登录。";
                _tray.ShowBalloonTip(2500);
            }
            catch
            {
            }
        }

        private void ExitApp()
        {
            _exiting = true;
            try
            {
                if (_timer != null) _timer.Dispose();
            }
            catch
            {
            }
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
            var icons = new[] { _iconOk, _iconIdle, _iconBusy, _iconError };
            foreach (var ic in icons)
            {
                try { if (ic != null) ic.Dispose(); } catch { }
            }
            Log.Info("程序退出。");
            Close();
            Application.Exit();
        }
    }
}
