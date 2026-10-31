package main

import (
	"bufio"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"runtime"
	"strconv"
	"strings"
	"time"

	"golang.org/x/term"
)

var version = "dev"

func main() {
	args := os.Args[1:]
	if len(args) == 0 {
		cmdStatus(nil)
		return
	}
	switch args[0] {
	case "status":
		cmdStatus(args[1:])
	case "login":
		cmdLogin(args[1:])
	case "config":
		cmdConfig(args[1:])
	case "daemon", "run":
		cmdDaemon(args[1:])
	case "tray", "--tray":
		runTray()
	case "test":
		cmdTest(args[1:])
	case "logs":
		cmdLogs(args[1:])
	case "version", "-v", "--version":
		fmt.Printf("PortalAutoLogin %s (%s/%s)\n", version, runtime.GOOS, runtime.GOARCH)
	case "help", "-h", "--help":
		usage()
	default:
		fmt.Fprintf(os.Stderr, "未知命令：%s\n\n", args[0])
		usage()
		os.Exit(2)
	}
}

func usage() {
	fmt.Print(`PortalAutoLogin - Portal 门户自动登录（Linux）

用法:
  portalautologin status [--json]     查看在线状态、认证服务器与统计
  portalautologin login [--verbose]   立即检测并登录一次
  portalautologin config [选项]       配置账号密码（无参数时进入交互式配置）
  portalautologin daemon              后台循环检测并自动重登（systemd 使用）
  portalautologin tray [--hidden]     启动图形托盘界面（托盘版二进制）
  portalautologin tray --settings     直接打开设置窗口（托盘版二进制）
  portalautologin test                自检并把结果写入 selftest.txt
  portalautologin logs [-f] [-n 50]   查看日志
  portalautologin version             显示版本

config 选项:
  --account 账号
  --password-stdin                    从标准输入读取密码
  --portal URL                        手动指定认证服务器（空字符串表示自动识别）
  --check-url URL                     在线检测地址
  --login-path PATH                   登录接口路径，默认 /drcom/login
  --eportal-port PORT                 ePortal 端口，默认 803
  --interval 秒                       检测间隔，默认 15
  --wifi-only true|false              是否仅在 Wi-Fi 下自动登录
  --show                              只显示当前配置
`)
}

func cmdStatus(args []string) {
	fs := flag.NewFlagSet("status", flag.ExitOnError)
	asJSON := fs.Bool("json", false, "以 JSON 输出")
	_ = fs.Parse(args)

	kind, iface := ActiveNetworkKind()
	cfg, err := LoadConfig()
	if err != nil {
		fmt.Fprintf(os.Stderr, "尚未配置：请先运行 portalautologin config\n配置文件：%s\n", ConfigFile())
		os.Exit(1)
	}
	online := IsOnline(cfg)
	st := LoadState()
	localIP := LocalIP(hostOf(cfg.PortalURL))

	if *asJSON {
		payload := map[string]any{
			"version":          version,
			"config":           ConfigFile(),
			"account":          cfg.Account,
			"portal_url":       cfg.PortalURL,
			"login_path":       cfg.LoginPath,
			"check_url":        cfg.CheckURL,
			"interval_seconds": cfg.IntervalSeconds,
			"wifi_only":        cfg.WiFiOnly,
			"online":           online,
			"network":          kind.String(),
			"interface":        iface,
			"local_ip":         localIP,
			"last_login":       st.LastLogin,
			"today_count":      st.TodayCount,
		}
		data, _ := json.MarshalIndent(payload, "", "  ")
		fmt.Println(string(data))
		return
	}

	fmt.Printf("PortalAutoLogin v%s\n", version)
	fmt.Printf("配置：%s\n", ConfigFile())
	fmt.Printf("账号：%s\n", cfg.Account)
	if cfg.PortalURL == "" {
		fmt.Println("认证服务器：待识别（掉线时自动识别）")
	} else {
		fmt.Printf("认证服务器：%s\n", cfg.PortalURL)
	}
	fmt.Printf("登录接口：%s\n", cfg.LoginPath)
	if online {
		fmt.Println("在线状态：已在线")
	} else {
		fmt.Println("在线状态：未登录")
	}
	fmt.Printf("网络：%s", kind.String())
	if iface != "" {
		fmt.Printf("（%s）", iface)
	}
	fmt.Println()
	fmt.Printf("本机 IP：%s\n", localIP)
	fmt.Printf("检测间隔：%d 秒\n", cfg.IntervalSeconds)
	if st.LastLogin != "" {
		fmt.Printf("上次自动登录：%s\n", st.LastLogin)
	}
	fmt.Printf("今日自动重登：%d 次\n", st.TodayCount)
}

func cmdLogin(args []string) {
	fs := flag.NewFlagSet("login", flag.ExitOnError)
	verbose := fs.Bool("verbose", false, "输出请求与服务器原始响应")
	_ = fs.Parse(args)

	cfg, err := LoadConfig()
	if err != nil {
		fmt.Fprintf(os.Stderr, "尚未配置：请先运行 portalautologin config\n")
		os.Exit(1)
	}
	if cfg.Account == "" || cfg.Password() == "" {
		fmt.Fprintln(os.Stderr, "账号或密码为空：请先运行 portalautologin config")
		os.Exit(1)
	}

	if IsOnline(cfg) {
		fmt.Println("当前已在线，无需登录。")
		return
	}

	if cfg.PortalURL == "" {
		detection := Detect(cfg.CheckURL)
		if detection.Online {
			fmt.Println("当前已在线，无需登录。")
			return
		}
		if detection.PortalURL == "" {
			fmt.Fprintln(os.Stderr, "未识别到认证服务器，请连接需要认证的网络后重试。")
			os.Exit(2)
		}
		cfg.PortalURL = detection.PortalURL
		_ = SaveConfig(cfg)
		fmt.Printf("自动识别认证服务器：%s（%s）\n", detection.PortalURL, detection.Evidence)
	}

	result := Login(cfg)
	if *verbose {
		fmt.Printf("请求：%s\n", result.MaskedURL)
		fmt.Printf("响应：%s\n", strings.TrimSpace(result.Raw))
	}
	if result.Success {
		fmt.Printf("登录成功%s\n", messageSuffix(result))
		recordLogin()
		return
	}

	detection := Detect(cfg.CheckURL)
	if detection.Online {
		fmt.Println("登录后检测已在线。")
		recordLogin()
		return
	}
	if detection.PortalURL != "" && detection.PortalURL != cfg.PortalURL {
		cfg.PortalURL = detection.PortalURL
		_ = SaveConfig(cfg)
		fmt.Printf("重新识别认证服务器：%s\n", detection.PortalURL)
		result = Login(cfg)
		if *verbose {
			fmt.Printf("请求：%s\n", result.MaskedURL)
			fmt.Printf("响应：%s\n", strings.TrimSpace(result.Raw))
		}
		if result.Success {
			fmt.Printf("登录成功%s\n", messageSuffix(result))
			recordLogin()
			return
		}
	}

	fmt.Fprintf(os.Stderr, "登录失败%s\n", messageSuffix(result))
	os.Exit(2)
}

func messageSuffix(result *PortalResult) string {
	parts := make([]string, 0, 2)
	if result.Message != "" {
		parts = append(parts, result.Message)
	}
	if result.Result != "" {
		parts = append(parts, "结果码 "+result.Result)
	}
	if len(parts) == 0 {
		return ""
	}
	return "（" + strings.Join(parts, "，") + "）"
}

func recordLogin() {
	now := time.Now()
	st := LoadState()
	st.RecordLogin(now.Format("2006-01-02 15:04:05"), now.Format("2006-01-02"))
}

func cmdConfig(args []string) {
	fs := flag.NewFlagSet("config", flag.ExitOnError)
	account := fs.String("account", "", "账号")
	portal := fs.String("portal", "", "认证服务器地址")
	checkURL := fs.String("check-url", "", "在线检测地址")
	loginPath := fs.String("login-path", "", "登录接口路径")
	eportalPort := fs.Int("eportal-port", 0, "ePortal 端口")
	interval := fs.Int("interval", 0, "检测间隔（秒）")
	wifiOnly := fs.String("wifi-only", "", "仅 Wi-Fi 下启用 true|false")
	passwordStdin := fs.Bool("password-stdin", false, "从标准输入读取密码")
	show := fs.Bool("show", false, "只显示当前配置")
	resetPortal := fs.Bool("reset-portal", false, "清空认证服务器，恢复自动识别")
	_ = fs.Parse(args)

	cfg, err := LoadConfig()
	if err != nil {
		cfg = defaultConfig()
	}

	if *show {
		printConfig(cfg)
		return
	}

	interactive := fs.NArg() == 0 && len(args) == 0
	if interactive {
		runInteractiveConfig(cfg)
		return
	}

	if *account != "" {
		cfg.Account = *account
	}
	if *resetPortal {
		cfg.PortalURL = ""
	} else if *portal != "" {
		cfg.PortalURL = *portal
	}
	if *checkURL != "" {
		cfg.CheckURL = *checkURL
	}
	if *loginPath != "" {
		cfg.LoginPath = *loginPath
	}
	if *eportalPort > 0 {
		cfg.EportalPort = *eportalPort
	}
	if *interval > 0 {
		cfg.IntervalSeconds = *interval
	}
	if *wifiOnly != "" {
		cfg.WiFiOnly = parseBool(*wifiOnly)
	}
	if *passwordStdin {
		password, err := readPasswordFromStdin()
		if err != nil {
			fmt.Fprintf(os.Stderr, "读取密码失败：%v\n", err)
			os.Exit(1)
		}
		cfg.SetPassword(password)
	}

	if cfg.Account == "" {
		fmt.Fprintln(os.Stderr, "账号不能为空：请加上 --account，或直接运行 portalautologin config 进入交互式配置")
		os.Exit(1)
	}
	if cfg.Password() == "" {
		fmt.Fprintln(os.Stderr, "密码为空：请加上 --password-stdin，或直接运行 portalautologin config 进入交互式配置")
		os.Exit(1)
	}
	if err := SaveConfig(cfg); err != nil {
		fmt.Fprintf(os.Stderr, "保存配置失败：%v\n", err)
		os.Exit(1)
	}
	fmt.Printf("配置已保存：%s\n", ConfigFile())
	printConfig(cfg)
	fmt.Println("\n守护进程会在下一轮检测时自动加载新配置。")
}

func runInteractiveConfig(cfg *Config) {
	reader := bufio.NewReader(os.Stdin)
	fmt.Println("PortalAutoLogin 配置（直接回车保持当前值）")

	cfg.Account = promptLine(reader, "Portal 账号", cfg.Account)

	fmt.Print("Portal 密码（留空保持不变）：")
	if term.IsTerminal(int(os.Stdin.Fd())) {
		raw, err := term.ReadPassword(int(os.Stdin.Fd()))
		fmt.Println()
		if err == nil && len(raw) > 0 {
			cfg.SetPassword(string(raw))
		}
	} else {
		line, _ := reader.ReadString('\n')
		if password := strings.TrimRight(line, "\r\n"); password != "" {
			cfg.SetPassword(password)
		}
	}

	portalDefault := cfg.PortalURL
	if portalDefault == "" {
		portalDefault = "自动识别"
	}
	portal := promptLine(reader, "认证服务器（自动识别可留空）", portalDefault)
	if portal == "自动识别" {
		cfg.PortalURL = ""
	} else {
		cfg.PortalURL = portal
	}

	cfg.CheckURL = promptLine(reader, "在线检测地址", cfg.CheckURL)
	cfg.IntervalSeconds = atoiDefault(promptLine(reader, "检测间隔（秒）", strconv.Itoa(cfg.IntervalSeconds)), cfg.IntervalSeconds)
	wifiDefault := "n"
	if cfg.WiFiOnly {
		wifiDefault = "y"
	}
	cfg.WiFiOnly = strings.EqualFold(promptLine(reader, "仅 Wi-Fi 下自动登录 (y/N)", wifiDefault), "y")

	if cfg.Account == "" {
		fmt.Fprintln(os.Stderr, "账号不能为空，配置未保存。")
		os.Exit(1)
	}
	if cfg.Password() == "" {
		fmt.Fprintln(os.Stderr, "密码不能为空，配置未保存。")
		os.Exit(1)
	}
	if err := SaveConfig(cfg); err != nil {
		fmt.Fprintf(os.Stderr, "保存配置失败：%v\n", err)
		os.Exit(1)
	}
	fmt.Printf("\n配置已保存：%s\n", ConfigFile())
	printConfig(cfg)
	fmt.Println("守护进程会在下一轮检测时自动加载新配置。")
}

func promptLine(reader *bufio.Reader, label, current string) string {
	if current != "" {
		fmt.Printf("%s [%s]：", label, current)
	} else {
		fmt.Printf("%s：", label)
	}
	line, _ := reader.ReadString('\n')
	line = strings.TrimRight(line, "\r\n")
	if line == "" {
		return current
	}
	return strings.TrimSpace(line)
}

func readPasswordFromStdin() (string, error) {
	data, err := io.ReadAll(os.Stdin)
	if err != nil {
		return "", err
	}
	return strings.TrimRight(string(data), "\r\n"), nil
}

func parseBool(text string) bool {
	switch strings.ToLower(strings.TrimSpace(text)) {
	case "1", "true", "yes", "y", "on":
		return true
	default:
		return false
	}
}

func printConfig(cfg *Config) {
	fmt.Printf("账号：%s\n", cfg.Account)
	if cfg.Password() == "" {
		fmt.Println("密码：未设置")
	} else {
		fmt.Println("密码：已保存（本机加密）")
	}
	if cfg.PortalURL == "" {
		fmt.Println("认证服务器：自动识别")
	} else {
		fmt.Printf("认证服务器：%s\n", cfg.PortalURL)
	}
	fmt.Printf("登录接口：%s\n", cfg.LoginPath)
	fmt.Printf("ePortal 端口：%d\n", cfg.EportalPort)
	fmt.Printf("在线检测地址：%s\n", cfg.CheckURL)
	fmt.Printf("检测间隔：%d 秒\n", cfg.IntervalSeconds)
	fmt.Printf("仅 Wi-Fi：%t\n", cfg.WiFiOnly)
	fmt.Printf("配置文件：%s\n", ConfigFile())
}

func cmdTest(args []string) {
	var sb strings.Builder
	sb.WriteString("版本: v" + version + "\n")
	_ = os.MkdirAll(configDir(), 0o700)

	cfg, err := LoadConfig()
	if err != nil {
		sb.WriteString("结果: 未配置账号密码（请先运行 portalautologin config）\n")
		writeSelfTest(sb.String())
		fmt.Print(sb.String())
		fmt.Printf("自检结果：%s\n", filepath.Join(configDir(), "selftest.txt"))
		os.Exit(1)
	}

	sb.WriteString("时间: " + time.Now().Format("2006-01-02 15:04:05") + "\n")
	sb.WriteString("账号: " + cfg.Account + "\n")
	portal := cfg.PortalURL
	if portal == "" {
		portal = "待识别"
	}
	sb.WriteString("Portal（自动识别）: " + portal + "\n")
	sb.WriteString("登录接口: " + cfg.LoginPath + "\n")

	if cfg.PortalURL == "" {
		detection := Detect(cfg.CheckURL)
		if detection.PortalURL != "" {
			cfg.PortalURL = detection.PortalURL
			_ = SaveConfig(cfg)
			sb.WriteString("自动识别: " + detection.PortalURL + "（" + detection.Evidence + "）\n")
		} else if detection.Online {
			sb.WriteString("自动识别: 当前已在线（" + detection.Evidence + "），暂不需要 Portal 地址\n")
		} else {
			sb.WriteString("自动识别: 未识别到认证服务器\n")
		}
	}

	sb.WriteString("本机 IP: " + LocalIP(hostOf(cfg.PortalURL)) + "\n")
	online := IsOnline(cfg)
	sb.WriteString("登录前在线: " + strconv.FormatBool(online) + "\n")

	var result *PortalResult
	if !online && cfg.PortalURL != "" {
		result = Login(cfg)
		sb.WriteString("登录结果: " + boolText(result.Success, "成功", "失败") + "\n")
		sb.WriteString("服务器结果码: " + result.Result + "\n")
		sb.WriteString("服务器消息: " + result.Message + "\n")
		sb.WriteString("登录后在线: " + strconv.FormatBool(IsOnline(cfg)) + "\n")
	}

	conclusion := online || (result != nil && (result.Success || IsOnline(cfg)))
	sb.WriteString("结论: " + boolText(conclusion, "在线可用", "未登录") + "\n")
	writeSelfTest(sb.String())
	fmt.Print(sb.String())
	fmt.Printf("自检结果：%s\n", filepath.Join(configDir(), "selftest.txt"))

	if conclusion {
		return
	}
	os.Exit(2)
}

func boolText(value bool, yes, no string) string {
	if value {
		return yes
	}
	return no
}

func writeSelfTest(content string) {
	path := filepath.Join(configDir(), "selftest.txt")
	_ = os.WriteFile(path, []byte(content), 0o600)
}

func cmdLogs(args []string) {
	fs := flag.NewFlagSet("logs", flag.ExitOnError)
	follow := fs.Bool("f", false, "持续输出新日志")
	lines := fs.Int("n", 50, "显示最近多少行")
	_ = fs.Parse(args)

	path := logFilePath()
	for _, line := range tailFile(path, *lines) {
		fmt.Println(line)
	}
	if !*follow {
		return
	}

	offset := int64(0)
	if info, err := os.Stat(path); err == nil {
		offset = info.Size()
	}
	for {
		time.Sleep(500 * time.Millisecond)
		info, err := os.Stat(path)
		if err != nil || info.Size() <= offset {
			continue
		}
		data, err := readFrom(path, offset, info.Size()-offset)
		if err != nil {
			continue
		}
		offset = info.Size()
		fmt.Print(string(data))
	}
}

func readFrom(path string, offset, size int64) ([]byte, error) {
	f, err := os.Open(path)
	if err != nil {
		return nil, err
	}
	defer f.Close()
	if _, err := f.Seek(offset, 0); err != nil {
		return nil, err
	}
	buf := make([]byte, size)
	n, err := f.Read(buf)
	if err != nil && n == 0 {
		return nil, err
	}
	return buf[:n], nil
}
