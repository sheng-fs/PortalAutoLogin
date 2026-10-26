﻿using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace PortalAutoLogin
{
    public class SettingsForm : Form
    {
        private readonly AppConfig _cfg;
        private readonly bool _firstRun;

        private TextBox _txtAccount;
        private TextBox _txtPassword;
        private TextBox _txtPortal;
        private TextBox _txtOpenUrl;
        private TextBox _txtLoginPath;
        private TextBox _txtCheckUrl;
        private NumericUpDown _numPort;
        private NumericUpDown _numInterval;
        private ToggleSwitch _toggleAuto;
        private CheckBox _chkShow;

        public SettingsForm(AppConfig cfg)
        {
            _firstRun = cfg == null;
            _cfg = cfg ?? new AppConfig();
            BuildUi();
        }

        private void BuildUi()
        {
            Text = _firstRun ? "PortalAutoLogin 首次设置" : "PortalAutoLogin 设置";
            ClientSize = new Size(500, 680);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Bg;
            Font = Theme.UI(9.5f);

            var title = new Label();
            title.Text = _firstRun ? "欢迎使用 PortalAutoLogin" : "设置";
            title.Font = Theme.UI(15f, FontStyle.Bold);
            title.ForeColor = Theme.Text;
            title.Location = new Point(24, 16);
            title.Size = new Size(440, 30);
            title.BackColor = Color.Transparent;
            Controls.Add(title);

            var sub = new Label();
            sub.Text = _firstRun ? "填写 Portal 账号和密码，保存后即可自动登录。" : "修改账号、认证地址与自动登录行为。";
            sub.ForeColor = Theme.SubText;
            sub.Location = new Point(24, 46);
            sub.Size = new Size(452, 20);
            sub.BackColor = Color.Transparent;
            Controls.Add(sub);

            int y = 72;
            _txtAccount = AddText("账号（学号 / 工号 / 房间号）", y, _cfg.Account);
            y += 58;
            _txtPassword = AddText("密码", y, ConfigStore.UnprotectPassword(_cfg.PasswordProtected), true);

            _chkShow = new CheckBox();
            _chkShow.Text = "显示密码";
            _chkShow.ForeColor = Theme.SubText;
            _chkShow.Font = Theme.UI(8.8f);
            _chkShow.Location = new Point(24, y + 48);
            _chkShow.Size = new Size(100, 20);
            _chkShow.BackColor = Color.Transparent;
            _chkShow.CheckedChanged += delegate { _txtPassword.UseSystemPasswordChar = !_chkShow.Checked; };
            Controls.Add(_chkShow);

            y += 76;
            _txtPortal = AddText("认证服务器地址（Portal）", y, _cfg.PortalUrl);
            AddHint("例如 http://portal.example.com；各单位填自己的地址。", y + 48);

            y += 76;
            _txtOpenUrl = AddText("打开链接（可选）", y, _cfg.OpenUrl);
            AddHint("留空则打开 Portal 地址；也可填任意链接。", y + 48);

            y += 82;
            var advanced = new Label();
            advanced.Text = "高级设置（一般无需修改）";
            advanced.ForeColor = Theme.Faint;
            advanced.Font = Theme.UI(8.8f);
            advanced.Location = new Point(24, y);
            advanced.Size = new Size(300, 20);
            advanced.BackColor = Color.Transparent;
            Controls.Add(advanced);

            y += 24;
            var lblPath = MakeCaption("登录接口", 24, y);
            Controls.Add(lblPath);
            _txtLoginPath = new TextBox();
            _txtLoginPath.Text = _cfg.LoginPath;
            _txtLoginPath.Location = new Point(24, y + 20);
            _txtLoginPath.Size = new Size(272, 26);
            _txtLoginPath.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(_txtLoginPath);

            var lblPort = MakeCaption("ePortal 端口", 316, y);
            Controls.Add(lblPort);
            _numPort = new NumericUpDown();
            _numPort.Minimum = 1;
            _numPort.Maximum = 65535;
            _numPort.Value = Math.Max(1, Math.Min(65535, _cfg.EportalPort));
            _numPort.Location = new Point(316, y + 20);
            _numPort.Size = new Size(160, 26);
            _numPort.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(_numPort);

            y += 58;
            var lblInterval = MakeCaption("检测间隔（秒）", 24, y);
            Controls.Add(lblInterval);
            _numInterval = new NumericUpDown();
            _numInterval.Minimum = 5;
            _numInterval.Maximum = 600;
            _numInterval.Value = Math.Max(5, Math.Min(600, _cfg.IntervalSeconds));
            _numInterval.Location = new Point(24, y + 20);
            _numInterval.Size = new Size(160, 26);
            _numInterval.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(_numInterval);

            var lblCheck = MakeCaption("在线检测地址", 200, y);
            Controls.Add(lblCheck);
            _txtCheckUrl = new TextBox();
            _txtCheckUrl.Text = _cfg.CheckUrl;
            _txtCheckUrl.Location = new Point(200, y + 20);
            _txtCheckUrl.Size = new Size(276, 26);
            _txtCheckUrl.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(_txtCheckUrl);

            y += 62;
            var autoCard = new CardPanel();
            autoCard.Location = new Point(24, y);
            autoCard.Size = new Size(452, 58);
            Controls.Add(autoCard);

            _toggleAuto = new ToggleSwitch();
            _toggleAuto.Location = new Point(18, 17);
            _toggleAuto.Checked = _firstRun ? true : AutoStart.IsEnabled();
            autoCard.Controls.Add(_toggleAuto);

            var lblAuto = new Label();
            lblAuto.Text = "开机自动启动";
            lblAuto.Font = Theme.UI(9.2f, FontStyle.Bold);
            lblAuto.ForeColor = Theme.Text;
            lblAuto.Location = new Point(72, 8);
            lblAuto.Size = new Size(300, 20);
            lblAuto.BackColor = Color.Transparent;
            autoCard.Controls.Add(lblAuto);

            var lblAutoSub = new Label();
            lblAutoSub.Text = "登录 Windows 后自动在后台运行（首次设置时创建计划任务）";
            lblAutoSub.ForeColor = Theme.SubText;
            lblAutoSub.Font = Theme.UI(8.4f);
            lblAutoSub.Location = new Point(72, 30);
            lblAutoSub.Size = new Size(370, 20);
            lblAutoSub.BackColor = Color.Transparent;
            autoCard.Controls.Add(lblAutoSub);

            var note = new Label();
            note.Text = "密码使用 Windows DPAPI 加密，仅当前 Windows 用户可解密。";
            note.ForeColor = Theme.Faint;
            note.Font = Theme.UI(8.2f);
            note.Location = new Point(24, y + 68);
            note.Size = new Size(452, 20);
            note.BackColor = Color.Transparent;
            Controls.Add(note);

            var btnSave = new RoundedButton();
            btnSave.Text = "保存";
            btnSave.Location = new Point(276, 612);
            btnSave.Size = new Size(96, 36);
            btnSave.Click += delegate { Save(); };
            Controls.Add(btnSave);

            var btnCancel = new RoundedButton();
            btnCancel.Text = "取消";
            btnCancel.Location = new Point(380, 590);
            btnCancel.Size = new Size(96, 36);
            btnCancel.FillColor = Color.White;
            btnCancel.HoverColor = Color.FromArgb(246, 246, 246);
            btnCancel.DownColor = Color.FromArgb(238, 238, 238);
            btnCancel.BorderColor = Theme.InputBorder;
            btnCancel.TextColor = Theme.Text;
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnCancel);

            var lblVersion = new Label();
            lblVersion.Text = "PortalAutoLogin v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            lblVersion.ForeColor = Theme.Faint;
            lblVersion.Font = Theme.UI(8.2f);
            lblVersion.Location = new Point(24, 626);
            lblVersion.Size = new Size(220, 20);
            lblVersion.BackColor = Color.Transparent;
            Controls.Add(lblVersion);

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            AcceptButton = btnSave;
            CancelButton = btnCancel;
        }

        private TextBox AddText(string caption, int y, string value, bool password = false)
        {
            Controls.Add(MakeCaption(caption, 24, y));
            var box = new TextBox();
            box.Text = value ?? "";
            box.Location = new Point(24, y + 20);
            box.Size = new Size(452, 26);
            box.BorderStyle = BorderStyle.FixedSingle;
            if (password) box.UseSystemPasswordChar = true;
            Controls.Add(box);
            return box;
        }

        private static Label MakeCaption(string text, int x, int y)
        {
            var lbl = new Label();
            lbl.Text = text;
            lbl.ForeColor = Theme.SubText;
            lbl.Font = Theme.UI(8.8f);
            lbl.Location = new Point(x, y);
            lbl.AutoSize = true;
            lbl.BackColor = Color.Transparent;
            return lbl;
        }

        private void AddHint(string text, int y)
        {
            var lbl = new Label();
            lbl.Text = text;
            lbl.ForeColor = Theme.Faint;
            lbl.Font = Theme.UI(8.2f);
            lbl.Location = new Point(24, y);
            lbl.AutoSize = false;
            lbl.AutoEllipsis = true;
            lbl.Size = new Size(452, 20);
            lbl.BackColor = Color.Transparent;
            Controls.Add(lbl);
        }

        private void Save()
        {
            string account = _txtAccount.Text.Trim();
            string password = _txtPassword.Text;
            string portal = PortalClient.NormalizePortal(_txtPortal.Text);
            string openUrl = _txtOpenUrl.Text.Trim();
            string loginPath = _txtLoginPath.Text.Trim();
            string checkUrl = _txtCheckUrl.Text.Trim();

            if (account.Length == 0)
            {
                Warn("请填写账号。");
                _txtAccount.Focus();
                return;
            }
            if (password.Length == 0 && string.IsNullOrEmpty(ConfigStore.UnprotectPassword(_cfg.PasswordProtected)))
            {
                Warn("请填写密码。");
                _txtPassword.Focus();
                return;
            }
            Uri uri;
            if (portal.Length == 0 || !Uri.TryCreate(portal, UriKind.Absolute, out uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                Warn("认证服务器地址无效，请填写形如 http://portal.example.com 的地址。");
                _txtPortal.Focus();
                return;
            }
            if (openUrl.Length > 0)
            {
                string normalizedOpen = PortalClient.NormalizePortal(openUrl);
                Uri openUri;
                if (!Uri.TryCreate(normalizedOpen, UriKind.Absolute, out openUri))
                {
                    Warn("打开链接无效。");
                    _txtOpenUrl.Focus();
                    return;
                }
                openUrl = normalizedOpen;
            }
            if (loginPath.Length == 0)
            {
                Warn("登录接口不能为空。");
                _txtLoginPath.Focus();
                return;
            }
            if (!loginPath.StartsWith("/")) loginPath = "/" + loginPath;
            if (checkUrl.Length > 0)
            {
                Uri checkUri;
                if (!Uri.TryCreate(checkUrl, UriKind.Absolute, out checkUri))
                {
                    Warn("在线检测地址无效。");
                    _txtCheckUrl.Focus();
                    return;
                }
            }

            bool wasAuto = AutoStart.IsEnabled();
            _cfg.Account = account;
            if (!string.IsNullOrEmpty(password)) _cfg.PasswordProtected = ConfigStore.ProtectPassword(password);
            _cfg.PortalUrl = portal;
            _cfg.OpenUrl = openUrl;
            _cfg.LoginPath = loginPath;
            _cfg.CheckUrl = checkUrl;
            _cfg.EportalPort = (int)_numPort.Value;
            _cfg.IntervalSeconds = (int)_numInterval.Value;
            bool wantAuto = _toggleAuto.Checked;
            _cfg.AutoStart = wantAuto;

            if (!ConfigStore.Save(_cfg))
            {
                Warn("配置保存失败，请检查磁盘权限。");
                return;
            }

            if (wantAuto) AutoStart.Enable(Application.ExecutablePath);
            else if (wasAuto) AutoStart.Disable();

            Log.Info("设置已保存：账号=" + account + " Portal=" + portal + " 间隔=" + _cfg.IntervalSeconds + "s");
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Warn(string message)
        {
            MessageBox.Show(this, message, "PortalAutoLogin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
