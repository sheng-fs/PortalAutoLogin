//go:build tray

package main

import (
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"sync"
	"time"

	"github.com/gotk3/gotk3/gdk"
	"github.com/gotk3/gotk3/glib"
	"github.com/gotk3/gotk3/gtk"
)

const trayCSS = `
window {
    background-color: #f3f3f3;
    color: #1b1b1b;
    font-family: "Noto Sans CJK SC", "Microsoft YaHei UI", "Segoe UI", sans-serif;
}
.card {
    background-color: #ffffff;
    border: 1px solid #e4e4e4;
    border-radius: 12px;
    padding: 18px;
}
.title {
    font-size: 24px;
    font-weight: 700;
}
.subtitle {
    color: #5f5f5f;
    font-size: 14px;
}
.heading {
    font-size: 28px;
    font-weight: 700;
}
.muted {
    color: #5f5f5f;
    font-size: 13px;
}
.faint {
    color: #8c8c8c;
    font-size: 12px;
}
.value {
    font-size: 15px;
    font-weight: 600;
}
.section-title {
    font-size: 14px;
    font-weight: 600;
}
button.primary {
    background-image: none;
    background-color: #0f6cbd;
    border: 1px solid #0f6cbd;
    border-radius: 8px;
    color: #ffffff;
    padding: 8px 18px;
    font-weight: 600;
}
button.primary:hover {
    background-color: #1a7acc;
    border-color: #1a7acc;
}
button.primary:active {
    background-color: #0b5a9e;
    border-color: #0b5a9e;
}
button.secondary {
    background-image: none;
    background-color: #ffffff;
    border: 1px solid #d1d1d1;
    border-radius: 8px;
    color: #1b1b1b;
    padding: 8px 16px;
}
button.secondary:hover {
    background-color: #f5f5f5;
}
entry {
    background-image: none;
    background-color: #ffffff;
    border: 1px solid #d1d1d1;
    border-radius: 6px;
    padding: 6px 8px;
}
`

type trayUI struct {
	win         *gtk.Window
	settingsWin *gtk.Window
	menu        *gtk.Menu
	items       []*gtk.MenuItem
	indicator   *trayIndicator

	dotLabel       *gtk.Label
	statusText     *gtk.Label
	statusDetail   *gtk.Label
	lastLoginLabel *gtk.Label
	ipValue        *gtk.Label
	accountValue   *gtk.Label
	countValue     *gtk.Label
	intervalValue  *gtk.Label
	autostartSw    *gtk.Switch

	accountEntry  *gtk.Entry
	passwordEntry *gtk.Entry
	portalEntry   *gtk.Entry
	checkEntry    *gtk.Entry
	loginPathEnt  *gtk.Entry
	eportalSpin   *gtk.SpinButton
	intervalSpin  *gtk.SpinButton
	wifiSwitch    *gtk.Switch

	updatingAutostart bool

	mu       sync.Mutex
	cfg      *Config
	busy     bool
	quitting bool
	quit     chan struct{}
}

func runTray() {
	gtk.Init(nil)
	installTrayCSS()

	ui := &trayUI{quit: make(chan struct{})}
	ui.cfg, _ = LoadConfig()
	ui.buildWindow()
	ui.buildSettingsWindow()
	ui.buildMenu()

	ui.indicator = newTrayIndicator("PortalAutoLogin")
	ui.indicator.SetMenu(ui.menu)
	ui.indicator.SetLabel("检测中")

	if ui.cfg == nil {
		ui.setState("尚未配置", "填写账号和密码后即可自动登录。")
		ui.indicator.SetLabel("未配置")
	} else {
		ui.setState("检测中…", "正在检查在线状态")
	}
	ui.updateInfo()

	ui.win.Connect("destroy", func() { ui.quitApp() })
	ui.win.Connect("delete-event", func() bool {
		ui.win.Hide()
		return true
	})

	go ui.autoLoop()

	hidden := false
	showSettings := false
	for _, arg := range os.Args {
		if arg == "--hidden" || arg == "-h" {
			hidden = true
		}
		if arg == "--settings" {
			showSettings = true
		}
	}
	if hidden && ui.cfg != nil && !showSettings {
		ui.win.Hide()
	} else {
		ui.win.ShowAll()
		ui.win.Present()
	}
	if showSettings {
		ui.openSettings()
	}

	gtk.Main()
}

func installTrayCSS() {
	provider, err := gtk.CssProviderNew()
	if err != nil {
		return
	}
	if err := provider.LoadFromData(trayCSS); err != nil {
		return
	}
	screen, err := gdk.ScreenGetDefault()
	if err != nil {
		return
	}
	gtk.AddProviderForScreen(screen, provider, gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)
}

type styleable interface {
	GetStyleContext() (*gtk.StyleContext, error)
}

func addClass(widget any, class string) {
	st, ok := widget.(styleable)
	if !ok {
		return
	}
	ctx, err := st.GetStyleContext()
	if err != nil {
		return
	}
	ctx.AddClass(class)
}

func newClassLabel(text, class string) *gtk.Label {
	label, err := gtk.LabelNew(text)
	if err != nil {
		fatalTray(err)
	}
	label.SetXAlign(0)
	addClass(label, class)
	return label
}

func card() *gtk.Box {
	box, err := gtk.BoxNew(gtk.ORIENTATION_VERTICAL, 8)
	if err != nil {
		fatalTray(err)
	}
	addClass(box, "card")
	return box
}

func logoFilePath() string {
	if exe, err := os.Executable(); err == nil {
		candidate := filepath.Join(filepath.Dir(exe), "portalautologin.png")
		if _, err := os.Stat(candidate); err == nil {
			return candidate
		}
	}
	if _, err := os.Stat("portalautologin.png"); err == nil {
		return "portalautologin.png"
	}
	return ""
}

func logoImage(size int) *gtk.Image {
	if path := logoFilePath(); path != "" {
		if pixbuf, err := gdk.PixbufNewFromFile(path); err == nil {
			if scaled, err := pixbuf.ScaleSimple(size, size, gdk.INTERP_BILINEAR); err == nil {
				if image, err := gtk.ImageNewFromPixbuf(scaled); err == nil {
					return image
				}
			}
		}
	}
	image, _ := gtk.ImageNewFromIconName("portalautologin", gtk.ICON_SIZE_DIALOG)
	image.SetPixelSize(size)
	return image
}

func (u *trayUI) buildWindow() {
	win, err := gtk.WindowNew(gtk.WINDOW_TOPLEVEL)
	if err != nil {
		fatalTray(err)
	}
	u.win = win
	win.SetTitle("PortalAutoLogin 门户自动登录")
	win.SetDefaultSize(660, 690)
	win.SetResizable(true)
	win.SetPosition(gtk.WIN_POS_CENTER)
	if logo := logoFilePath(); logo != "" {
		if err := win.SetIconFromFile(logo); err != nil {
			win.SetIconName("portalautologin")
		}
	} else {
		win.SetIconName("portalautologin")
	}

	outer, err := gtk.BoxNew(gtk.ORIENTATION_VERTICAL, 18)
	if err != nil {
		fatalTray(err)
	}
	outer.SetBorderWidth(24)
	win.Add(outer)

	// 顶部品牌区：图标 + 名称 + 副标题
	header, err := gtk.BoxNew(gtk.ORIENTATION_HORIZONTAL, 14)
	if err != nil {
		fatalTray(err)
	}
	logo := logoImage(56)
	header.PackStart(logo, false, false, 0)

	titleBox, err := gtk.BoxNew(gtk.ORIENTATION_VERTICAL, 2)
	if err != nil {
		fatalTray(err)
	}
	titleBox.SetVAlign(gtk.ALIGN_CENTER)
	titleBox.PackStart(newClassLabel("PortalAutoLogin", "title"), false, false, 0)
	titleBox.PackStart(newClassLabel("门户自动登录", "subtitle"), false, false, 0)
	header.PackStart(titleBox, false, false, 0)
	outer.PackStart(header, false, false, 0)

	// 状态卡片
	statusCard := card()
	statusRow, err := gtk.BoxNew(gtk.ORIENTATION_HORIZONTAL, 10)
	if err != nil {
		fatalTray(err)
	}
	u.dotLabel, _ = gtk.LabelNew("")
	u.dotLabel.SetMarkup("<span size='xx-large' color='#8c8c8c'>●</span>")
	u.statusText, _ = gtk.LabelNew("")
	statusRow.PackStart(u.dotLabel, false, false, 0)
	statusRow.PackStart(u.statusText, false, false, 0)
	statusCard.PackStart(statusRow, false, false, 0)

	u.statusDetail = newClassLabel("", "muted")
	u.statusDetail.SetLineWrap(true)
	statusCard.PackStart(u.statusDetail, false, false, 0)

	u.lastLoginLabel = newClassLabel("上次自动登录：-", "faint")
	u.lastLoginLabel.SetMarginTop(10)
	statusCard.PackStart(u.lastLoginLabel, false, false, 0)
	outer.PackStart(statusCard, false, false, 0)

	// 信息卡片（2x2）
	infoCard := card()
	grid, err := gtk.GridNew()
	if err != nil {
		fatalTray(err)
	}
	grid.SetRowSpacing(14)
	grid.SetColumnSpacing(48)
	u.ipValue = addInfoCell(grid, 0, 0, "本机 IP")
	u.accountValue = addInfoCell(grid, 1, 0, "账号")
	u.countValue = addInfoCell(grid, 0, 1, "今日自动重登")
	u.intervalValue = addInfoCell(grid, 1, 1, "检测间隔")
	infoCard.PackStart(grid, false, false, 0)
	outer.PackStart(infoCard, false, false, 0)

	// 操作按钮
	buttons, err := gtk.BoxNew(gtk.ORIENTATION_HORIZONTAL, 10)
	if err != nil {
		fatalTray(err)
	}
	loginBtn, _ := gtk.ButtonNewWithLabel("立即登录")
	addClass(loginBtn, "primary")
	loginBtn.SetSizeRequest(180, -1)
	loginBtn.Connect("clicked", func() { u.loginNow() })
	buttons.PackStart(loginBtn, false, false, 0)

	settingsBtn, _ := gtk.ButtonNewWithLabel("设置")
	addClass(settingsBtn, "secondary")
	settingsBtn.SetSizeRequest(110, -1)
	settingsBtn.Connect("clicked", func() { u.openSettings() })
	buttons.PackStart(settingsBtn, false, false, 0)

	logsBtn, _ := gtk.ButtonNewWithLabel("查看日志")
	addClass(logsBtn, "secondary")
	logsBtn.Connect("clicked", func() { u.openLogs() })
	buttons.PackStart(logsBtn, false, false, 0)
	outer.PackStart(buttons, false, false, 0)

	// 开机自动启动
	autoCard := card()
	autoRow, err := gtk.BoxNew(gtk.ORIENTATION_HORIZONTAL, 12)
	if err != nil {
		fatalTray(err)
	}
	autoText, err := gtk.BoxNew(gtk.ORIENTATION_VERTICAL, 4)
	if err != nil {
		fatalTray(err)
	}
	autoText.PackStart(newClassLabel("开机自动启动", "section-title"), false, false, 0)
	autoText.PackStart(newClassLabel("登录桌面后自动在后台运行，掉线自动重新认证", "muted"), false, false, 0)
	autoRow.PackStart(autoText, true, true, 0)

	u.autostartSw, _ = gtk.SwitchNew()
	u.autostartSw.SetVAlign(gtk.ALIGN_CENTER)
	u.autostartSw.SetActive(autostartEnabled())
	u.autostartSw.Connect("notify::active", func() {
		if u.updatingAutostart {
			return
		}
		if err := setAutostart(u.autostartSw.GetActive()); err != nil {
			u.errorDialog("设置开机启动失败：" + err.Error())
			u.updatingAutostart = true
			u.autostartSw.SetActive(!u.autostartSw.GetActive())
			u.updatingAutostart = false
		}
	})
	autoRow.PackEnd(u.autostartSw, false, false, 0)
	autoCard.PackStart(autoRow, false, false, 0)
	outer.PackStart(autoCard, false, false, 0)

	footer, err := gtk.BoxNew(gtk.ORIENTATION_HORIZONTAL, 0)
	if err != nil {
		fatalTray(err)
	}
	footer.PackEnd(newClassLabel("v"+version, "faint"), false, false, 0)
	outer.PackStart(footer, false, false, 0)
}

func addInfoCell(grid *gtk.Grid, col, row int, title string) *gtk.Label {
	box, err := gtk.BoxNew(gtk.ORIENTATION_VERTICAL, 2)
	if err != nil {
		fatalTray(err)
	}
	box.PackStart(newClassLabel(title, "muted"), false, false, 0)
	value := newClassLabel("-", "value")
	box.PackStart(value, false, false, 0)
	grid.Attach(box, col, row, 1, 1)
	return value
}

func (u *trayUI) buildSettingsWindow() {
	win, err := gtk.WindowNew(gtk.WINDOW_TOPLEVEL)
	if err != nil {
		fatalTray(err)
	}
	u.settingsWin = win
	win.SetTitle("PortalAutoLogin 设置")
	win.SetDefaultSize(640, 700)
	win.SetPosition(gtk.WIN_POS_CENTER)
	if logo := logoFilePath(); logo != "" {
		_ = win.SetIconFromFile(logo)
	} else {
		win.SetIconName("portalautologin")
	}
	win.Connect("delete-event", func() bool {
		win.Hide()
		return true
	})

	outer, err := gtk.BoxNew(gtk.ORIENTATION_VERTICAL, 16)
	if err != nil {
		fatalTray(err)
	}
	outer.SetBorderWidth(24)
	win.Add(outer)

	outer.PackStart(newClassLabel("欢迎使用 PortalAutoLogin", "heading"), false, false, 0)
	outer.PackStart(newClassLabel("填写 Portal 账号和密码，保存后即可自动登录。", "subtitle"), false, false, 0)

	formCard := card()
	grid, err := gtk.GridNew()
	if err != nil {
		fatalTray(err)
	}
	grid.SetRowSpacing(10)
	grid.SetColumnSpacing(12)

	u.accountEntry = u.addFormRow(grid, 0, "账号（学号 / 工号 / 房间号）")
	u.passwordEntry = u.addFormRow(grid, 1, "密码")
	u.passwordEntry.SetVisibility(false)
	u.passwordEntry.SetPlaceholderText("留空保持不变")

	u.portalEntry = u.addFormRow(grid, 2, "认证服务器地址")
	u.portalEntry.SetPlaceholderText("留空自动识别，例如 http://192.168.1.1")
	portalHint := newClassLabel("留空时会在掉线后自动识别 Portal 地址。", "faint")
	grid.Attach(portalHint, 1, 3, 1, 1)

	u.checkEntry = u.addFormRow(grid, 4, "在线检测地址")
	u.checkEntry.SetPlaceholderText(defaultCheckURL)

	u.loginPathEnt = u.addFormRow(grid, 5, "登录接口")
	u.loginPathEnt.SetPlaceholderText(defaultLoginPath)

	portBox, err := gtk.BoxNew(gtk.ORIENTATION_HORIZONTAL, 10)
	if err != nil {
		fatalTray(err)
	}
	u.eportalSpin, _ = gtk.SpinButtonNewWithRange(1, 65535, 1)
	u.eportalSpin.SetValue(defaultEportalPort)
	portBox.PackStart(u.eportalSpin, false, false, 0)
	grid.Attach(newClassLabel("ePortal 端口", "muted"), 0, 6, 1, 1)
	grid.Attach(portBox, 1, 6, 1, 1)

	u.intervalSpin, _ = gtk.SpinButtonNewWithRange(minInterval, 3600, 1)
	u.intervalSpin.SetValue(defaultInterval)
	grid.Attach(newClassLabel("检测间隔（秒）", "muted"), 0, 7, 1, 1)
	grid.Attach(u.intervalSpin, 1, 7, 1, 1)

	u.wifiSwitch, _ = gtk.SwitchNew()
	wifiRow, err := gtk.BoxNew(gtk.ORIENTATION_HORIZONTAL, 10)
	if err != nil {
		fatalTray(err)
	}
	wifiRow.PackStart(newClassLabel("仅在 Wi-Fi 网络下自动登录", "muted"), false, false, 0)
	wifiRow.PackEnd(u.wifiSwitch, false, false, 0)
	grid.Attach(newClassLabel("启用条件", "muted"), 0, 8, 1, 1)
	grid.Attach(wifiRow, 1, 8, 1, 1)

	formCard.PackStart(grid, false, false, 0)
	outer.PackStart(formCard, false, false, 0)

	outer.PackStart(newClassLabel("密码使用 AES-256-GCM 加密保存，仅当前系统用户可解密；日志中的密码会脱敏。", "faint"), false, false, 0)

	buttons, err := gtk.BoxNew(gtk.ORIENTATION_HORIZONTAL, 10)
	if err != nil {
		fatalTray(err)
	}
	buttons.SetHAlign(gtk.ALIGN_END)
	saveBtn, _ := gtk.ButtonNewWithLabel("保存")
	addClass(saveBtn, "primary")
	saveBtn.SetSizeRequest(120, -1)
	saveBtn.Connect("clicked", func() { u.saveSettings() })
	buttons.PackStart(saveBtn, false, false, 0)

	cancelBtn, _ := gtk.ButtonNewWithLabel("取消")
	addClass(cancelBtn, "secondary")
	cancelBtn.SetSizeRequest(100, -1)
	cancelBtn.Connect("clicked", func() { u.settingsWin.Hide() })
	buttons.PackStart(cancelBtn, false, false, 0)
	outer.PackStart(buttons, false, false, 0)

	footer := newClassLabel("配置文件："+ConfigFile(), "faint")
	footer.SetSelectable(true)
	outer.PackStart(footer, false, false, 0)
}

func (u *trayUI) addFormRow(grid *gtk.Grid, row int, label string) *gtk.Entry {
	grid.Attach(newClassLabel(label, "muted"), 0, row, 1, 1)
	entry, _ := gtk.EntryNew()
	entry.SetHExpand(true)
	grid.Attach(entry, 1, row, 1, 1)
	return entry
}

func (u *trayUI) buildMenu() {
	menu, err := gtk.MenuNew()
	if err != nil {
		fatalTray(err)
	}

	add := func(label string, handler func()) *gtk.MenuItem {
		item, err := gtk.MenuItemNewWithLabel(label)
		if err != nil {
			fatalTray(err)
		}
		if handler != nil {
			item.Connect("activate", handler)
		}
		menu.Append(item)
		u.items = append(u.items, item)
		return item
	}

	statusItem := add("PortalAutoLogin", nil)
	statusItem.SetSensitive(false)
	add("显示主界面", func() {
		u.win.ShowAll()
		u.win.Present()
	})
	add("立即登录", func() { u.loginNow() })
	add("设置…", func() { u.openSettings() })

	separator, err := gtk.SeparatorMenuItemNew()
	if err != nil {
		fatalTray(err)
	}
	menu.Append(separator)

	add("退出", func() { u.quitApp() })

	menu.ShowAll()
	u.menu = menu
}

func (u *trayUI) openSettings() {
	cfg := u.peek()
	if cfg != nil {
		u.accountEntry.SetText(cfg.Account)
		u.portalEntry.SetText(cfg.PortalURL)
		u.checkEntry.SetText(cfg.CheckURL)
		u.loginPathEnt.SetText(cfg.LoginPath)
		u.eportalSpin.SetValue(float64(cfg.EportalPort))
		u.intervalSpin.SetValue(float64(cfg.IntervalSeconds))
		u.wifiSwitch.SetActive(cfg.WiFiOnly)
	}
	u.passwordEntry.SetText("")
	u.settingsWin.ShowAll()
	u.settingsWin.Present()
}

func (u *trayUI) peek() *Config {
	u.mu.Lock()
	defer u.mu.Unlock()
	if u.cfg == nil {
		return nil
	}
	copyCfg := *u.cfg
	return &copyCfg
}

func (u *trayUI) beginManual() *Config {
	u.mu.Lock()
	if u.busy {
		u.mu.Unlock()
		u.setState("检测中…", "已有检测正在进行，请稍候。")
		return nil
	}
	cfg := u.cfg
	if cfg == nil || cfg.Account == "" || cfg.Password() == "" {
		u.mu.Unlock()
		u.errorDialog("请先在“设置”里填写并保存账号和密码。")
		return nil
	}
	u.busy = true
	copyCfg := *cfg
	u.mu.Unlock()
	return &copyCfg
}

func (u *trayUI) beginIdle() *Config {
	u.mu.Lock()
	defer u.mu.Unlock()
	if u.busy || u.cfg == nil || u.cfg.Account == "" || u.cfg.Password() == "" {
		return nil
	}
	u.busy = true
	copyCfg := *u.cfg
	return &copyCfg
}

func (u *trayUI) endBusy() {
	u.mu.Lock()
	u.busy = false
	u.mu.Unlock()
}

func (u *trayUI) loginNow() {
	cfg := u.beginManual()
	if cfg == nil {
		return
	}
	u.setState("检测中…", "正在检查在线状态")
	go func() {
		defer u.endBusy()
		online, msg := u.checkAndLogin(cfg)
		glib.IdleAdd(func() bool {
			u.applyResult(online, msg)
			return false
		})
	}()
}

func (u *trayUI) checkAndLogin(cfg *Config) (bool, string) {
	if IsOnline(cfg) {
		return true, ""
	}

	if cfg.PortalURL == "" {
		detection := Detect(cfg.CheckURL)
		if detection.Online {
			return true, ""
		}
		if detection.PortalURL == "" {
			return false, "未识别到认证服务器，请确认已连接需要认证的网络"
		}
		cfg.PortalURL = detection.PortalURL
		u.mu.Lock()
		if u.cfg != nil {
			u.cfg.PortalURL = detection.PortalURL
		}
		u.mu.Unlock()
		_ = SaveConfig(cfg)
		glib.IdleAdd(func() bool {
			u.portalEntry.SetText(detection.PortalURL)
			return false
		})
	}

	result := Login(cfg)
	if result.Success || IsOnline(cfg) {
		recordLogin()
		return true, ""
	}
	return false, messageSuffix(result)
}

func (u *trayUI) applyResult(online bool, msg string) {
	if online {
		u.setState("已连接", "网络认证正常，无需登录")
		if u.indicator != nil {
			u.indicator.SetLabel("在线")
		}
	} else {
		detail := "检测到网络未认证，将按间隔自动重试"
		if msg != "" {
			detail = msg
		}
		u.setState("未登录", detail)
		if u.indicator != nil {
			u.indicator.SetLabel("离线")
		}
	}
	u.updateInfo()
}

func (u *trayUI) setState(text, detail string) {
	color := "#8c8c8c"
	switch text {
	case "已连接":
		color = "#0f7b0f"
	case "未登录":
		color = "#c42b1c"
	case "检测中…":
		color = "#0f6cbd"
	case "自动登录待机":
		color = "#9d5d00"
	}
	u.dotLabel.SetMarkup(fmt.Sprintf("<span size='xx-large' color='%s'>●</span>", color))
	u.statusText.SetMarkup(fmt.Sprintf("<span size='xx-large' weight='bold'>%s</span>", text))
	u.statusDetail.SetText(detail)
}

func (u *trayUI) updateInfo() {
	cfg := u.peek()
	state := LoadState()
	if cfg == nil {
		u.ipValue.SetText("-")
		u.accountValue.SetText("-")
		u.countValue.SetText("0 次")
		u.intervalValue.SetText(fmt.Sprintf("%d 秒", defaultInterval))
	} else {
		u.ipValue.SetText(LocalIP(hostOf(cfg.PortalURL)))
		u.accountValue.SetText(cfg.Account)
		u.countValue.SetText(fmt.Sprintf("%d 次", state.TodayCount))
		u.intervalValue.SetText(fmt.Sprintf("%d 秒", cfg.IntervalSeconds))
	}
	last := state.LastLogin
	if last == "" {
		last = "-"
	}
	u.lastLoginLabel.SetText("上次自动登录：" + last)
}

func (u *trayUI) saveSettings() {
	cur := u.peek()
	var cfg *Config
	if cur != nil {
		copyCfg := *cur
		cfg = &copyCfg
	} else {
		cfg = defaultConfig()
	}

	account, _ := u.accountEntry.GetText()
	password, _ := u.passwordEntry.GetText()
	portal, _ := u.portalEntry.GetText()
	check, _ := u.checkEntry.GetText()
	loginPath, _ := u.loginPathEnt.GetText()

	cfg.Account = strings.TrimSpace(account)
	if password != "" {
		cfg.SetPassword(password)
	}
	cfg.PortalURL = strings.TrimSpace(portal)
	cfg.CheckURL = strings.TrimSpace(check)
	cfg.LoginPath = strings.TrimSpace(loginPath)
	cfg.EportalPort = u.eportalSpin.GetValueAsInt()
	cfg.IntervalSeconds = u.intervalSpin.GetValueAsInt()
	cfg.WiFiOnly = u.wifiSwitch.GetActive()

	if cfg.Account == "" || cfg.Password() == "" {
		u.errorDialog("账号和密码不能为空。")
		return
	}
	if err := SaveConfig(cfg); err != nil {
		u.errorDialog("保存配置失败：" + err.Error())
		return
	}

	u.mu.Lock()
	u.cfg = cfg
	u.mu.Unlock()

	u.passwordEntry.SetText("")
	u.settingsWin.Hide()
	u.setState("检测中…", "配置已保存，正在检查在线状态")
	u.updateInfo()
	u.loginNow()
}

func (u *trayUI) openLogs() {
	dir := filepath.Dir(logFilePath())
	if err := exec.Command("xdg-open", dir).Start(); err != nil {
		u.errorDialog("无法打开日志目录：" + err.Error())
	}
}

func (u *trayUI) errorDialog(message string) {
	dialog := gtk.MessageDialogNew(u.win, gtk.DIALOG_MODAL, gtk.MESSAGE_ERROR, gtk.BUTTONS_OK, "%s", message)
	dialog.Run()
	dialog.Destroy()
}

func (u *trayUI) autoLoop() {
	first := true
	for {
		interval := defaultInterval
		if cfg := u.peek(); cfg != nil && cfg.IntervalSeconds >= minInterval {
			interval = cfg.IntervalSeconds
		}

		delay := time.Duration(interval) * time.Second
		if first {
			delay = 3 * time.Second
			first = false
		}

		select {
		case <-u.quit:
			return
		case <-time.After(delay):
		}

		cfg := u.beginIdle()
		if cfg == nil {
			continue
		}

		kind, _ := ActiveNetworkKind()
		if cfg.WiFiOnly && kind != NetworkWiFi {
			u.endBusy()
			glib.IdleAdd(func() bool {
				u.setState("自动登录待机", "当前网络不是 Wi-Fi（已启用仅 Wi-Fi 模式）。")
				return false
			})
			continue
		}

		online, msg := u.checkAndLogin(cfg)
		u.endBusy()
		glib.IdleAdd(func() bool {
			u.applyResult(online, msg)
			return false
		})
	}
}

func (u *trayUI) quitApp() {
	u.mu.Lock()
	if u.quitting {
		u.mu.Unlock()
		return
	}
	u.quitting = true
	close(u.quit)
	u.mu.Unlock()

	if u.indicator != nil {
		u.indicator.SetActive(false)
	}
	gtk.MainQuit()
}

func fatalTray(err error) {
	fmt.Fprintln(os.Stderr, "图形界面初始化失败："+err.Error())
	os.Exit(1)
}

func autostartFilePath() string {
	home, err := os.UserHomeDir()
	if err != nil || home == "" {
		home = "."
	}
	return filepath.Join(home, ".config", "autostart", "portalautologin-tray.desktop")
}

func autostartEnabled() bool {
	_, err := os.Stat(autostartFilePath())
	return err == nil
}

func setAutostart(enabled bool) error {
	path := autostartFilePath()
	if !enabled {
		if err := os.Remove(path); err != nil && !os.IsNotExist(err) {
			return err
		}
		return nil
	}
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return err
	}
	exe, err := os.Executable()
	if err != nil {
		exe = "portalautologin-tray"
	}
	content := fmt.Sprintf(`[Desktop Entry]
Type=Application
Name=PortalAutoLogin
Name[zh_CN]=PortalAutoLogin 门户自动登录
Comment=Portal 门户自动登录（托盘）
Exec="%s" tray --hidden
Icon=portalautologin
Terminal=false
Categories=Network;Utility;
X-GNOME-Autostart-enabled=true
StartupNotify=false
`, exe)
	return os.WriteFile(path, []byte(content), 0o644)
}
