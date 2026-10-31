package main

import (
	"flag"
	"os"
	"os/signal"
	"syscall"
	"time"
)

type daemonState struct {
	lastOnline *bool
	lastGuard  string
	lastNotice time.Time
}

func cmdDaemon(args []string) {
	fs := flag.NewFlagSet("daemon", flag.ExitOnError)
	once := fs.Bool("once", false, "只执行一轮检测")
	_ = fs.Parse(args)

	sigCh := make(chan os.Signal, 1)
	signal.Notify(sigCh, syscall.SIGINT, syscall.SIGTERM)

	cfg := waitForConfig(sigCh)
	if cfg == nil {
		return
	}
	LogInfo("程序启动 v%s（守护模式），配置文件：%s", version, ConfigFile())

	state := &daemonState{}
	for {
		runCycle(cfg, state)
		if *once {
			return
		}

		interval := time.Duration(cfg.IntervalSeconds) * time.Second
		if interval < minInterval*time.Second {
			interval = defaultInterval * time.Second
		}
		timer := time.NewTimer(interval)
		select {
		case <-sigCh:
			timer.Stop()
			LogInfo("收到退出信号，停止守护进程")
			return
		case <-timer.C:
		}

		// 配置文件被修改后无需重启服务即可生效。
		if updated, err := LoadConfig(); err == nil {
			cfg = updated
		}
	}
}

func waitForConfig(sigCh <-chan os.Signal) *Config {
	warned := false
	for {
		cfg, err := LoadConfig()
		if err == nil && cfg.Account != "" && cfg.Password() != "" {
			return cfg
		}
		if !warned {
			LogWarn("尚未完成配置，请运行 portalautologin config（配置文件：%s）", ConfigFile())
			warned = true
		}
		select {
		case <-sigCh:
			LogInfo("收到退出信号，停止守护进程")
			return nil
		case <-time.After(10 * time.Second):
		}
	}
}

func runCycle(cfg *Config, state *daemonState) {
	kind, iface := ActiveNetworkKind()
	if cfg.WiFiOnly && kind != NetworkWiFi {
		if state.lastGuard != "standby" {
			LogInfo("当前网络为%s，自动登录待机（仅 Wi-Fi 启用）", kind.String())
			state.lastGuard = "standby"
		}
		return
	}
	if state.lastGuard != "active" {
		state.lastGuard = "active"
		LogInfo("当前网络：%s%s", kind.String(), describeInterface(iface))
	}

	if IsOnline(cfg) {
		if state.lastOnline == nil || !*state.lastOnline {
			LogInfo("当前已在线")
		}
		setOnline(state, true)
		return
	}

	if state.lastOnline == nil || *state.lastOnline {
		LogWarn("检测到网络未认证，准备登录")
	}
	setOnline(state, false)

	if autoLogin(cfg, state) {
		setOnline(state, true)
	}
}

func autoLogin(cfg *Config, state *daemonState) bool {
	if cfg.PortalURL == "" {
		detection := Detect(cfg.CheckURL)
		if detection.Online {
			return true
		}
		if detection.PortalURL == "" {
			notifyThrottled(state, "未识别到认证服务器，请确认已连接需要认证的网络")
			return false
		}
		cfg.PortalURL = detection.PortalURL
		if err := SaveConfig(cfg); err == nil {
			LogInfo("自动识别认证服务器：%s（%s）", detection.PortalURL, detection.Evidence)
		}
	}

	result := Login(cfg)
	if result.Success {
		LogInfo("登录成功%s", messageSuffix(result))
		recordLogin()
		return true
	}
	LogWarn("登录失败%s", messageSuffix(result))

	// 与 Windows 版一致：失败后重新识别一次地址再重试。
	detection := Detect(cfg.CheckURL)
	if detection.Online {
		LogInfo("登录后检测已在线")
		recordLogin()
		return true
	}
	if detection.PortalURL != "" && detection.PortalURL != cfg.PortalURL {
		cfg.PortalURL = detection.PortalURL
		_ = SaveConfig(cfg)
		LogInfo("重新识别认证服务器：%s", detection.PortalURL)
		result = Login(cfg)
		if result.Success {
			LogInfo("登录成功%s", messageSuffix(result))
			recordLogin()
			return true
		}
		LogWarn("登录失败%s", messageSuffix(result))
	}

	if IsOnline(cfg) {
		LogInfo("当前已在线")
		return true
	}
	return false
}

func notifyThrottled(state *daemonState, format string, args ...any) {
	if time.Since(state.lastNotice) < time.Minute {
		return
	}
	state.lastNotice = time.Now()
	LogWarn(format, args...)
}

func setOnline(state *daemonState, online bool) {
	if state.lastOnline != nil && *state.lastOnline == online {
		return
	}
	value := online
	state.lastOnline = &value
	if !online {
		LogWarn("网络已离线，将按间隔自动重试")
	}
}

func describeInterface(iface string) string {
	if iface == "" {
		return ""
	}
	return "（" + iface + "）"
}
