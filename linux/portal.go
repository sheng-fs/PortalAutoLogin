package main

import (
	"encoding/base64"
	"encoding/json"
	"fmt"
	"math/rand"
	"net"
	"net/url"
	"strconv"
	"strings"
	"time"
)

type PortalResult struct {
	Success       bool
	AlreadyOnline bool
	Result        string
	Message       string
	Raw           string
	MaskedURL     string
}

type pageInfo struct {
	ProgramIndex  string
	PageIndex     string
	AccountPrefix int
}

// IsOnline 用 generate_204 类探针判断当前是否已认证。
func IsOnline(cfg *Config) bool {
	urls := make([]string, 0, 3)
	if strings.TrimSpace(cfg.CheckURL) != "" {
		urls = append(urls, strings.TrimSpace(cfg.CheckURL))
	}
	urls = append(urls, "http://connect.rom.miui.com/generate_204", "http://www.gstatic.com/generate_204")

	client := newHTTPClient(4 * time.Second)
	for _, target := range urls {
		req, err := newRequest("GET", target)
		if err != nil {
			continue
		}
		resp, err := client.Do(req)
		if err != nil {
			continue
		}
		status := resp.StatusCode
		readResponseBody(resp)
		if status == 204 {
			return true
		}
	}

	req, err := newRequest("HEAD", "http://www.baidu.com/")
	if err == nil {
		resp, err := client.Do(req)
		if err == nil {
			status := resp.StatusCode
			readResponseBody(resp)
			if status == 200 {
				return true
			}
		}
	}
	return false
}

// LocalIP 通过 UDP 连接取得出口地址，失败时回退到默认路由网卡的 IPv4 地址。
func LocalIP(host string) string {
	if host == "" {
		host = "www.baidu.com"
	}
	if conn, err := net.Dial("udp4", net.JoinHostPort(host, "80")); err == nil {
		if addr, ok := conn.LocalAddr().(*net.UDPAddr); ok {
			ip := addr.IP.String()
			if ip != "" && ip != "0.0.0.0" && !strings.HasPrefix(ip, "127.") {
				_ = conn.Close()
				return ip
			}
		}
		_ = conn.Close()
	}

	kind, preferred := ActiveNetworkKind()
	ifaces, err := net.Interfaces()
	if err != nil {
		return "0.0.0.0"
	}
	if kind != NetworkNone && preferred != "" {
		for _, iface := range ifaces {
			if iface.Name == preferred {
				if ip := firstIPv4(iface); ip != "" {
					return ip
				}
			}
		}
	}
	for _, iface := range ifaces {
		if iface.Flags&net.FlagUp == 0 || iface.Flags&net.FlagLoopback != 0 {
			continue
		}
		if ip := firstIPv4(iface); ip != "" {
			return ip
		}
	}
	return "0.0.0.0"
}

func firstIPv4(iface net.Interface) string {
	addrs, err := iface.Addrs()
	if err != nil {
		return ""
	}
	for _, addr := range addrs {
		ipnet, ok := addr.(*net.IPNet)
		if !ok {
			continue
		}
		ip := ipnet.IP.To4()
		if ip == nil || ip.IsLoopback() || ip.IsLinkLocalUnicast() {
			continue
		}
		return ip.String()
	}
	return ""
}

func hostOf(rawURL string) string {
	u, err := url.Parse(rawURL)
	if err != nil {
		return ""
	}
	return u.Hostname()
}

// portalRedirect 从 302 Location 中取出 Portal 跳转地址。
func portalRedirect(cfg *Config) string {
	targets := make([]string, 0, 2)
	if strings.TrimSpace(cfg.CheckURL) != "" {
		targets = append(targets, strings.TrimSpace(cfg.CheckURL))
	}
	targets = append(targets, "http://connect.rom.miui.com/generate_204")

	client := newHTTPClient(4 * time.Second)
	for _, target := range targets {
		req, err := newRequest("GET", target)
		if err != nil {
			continue
		}
		resp, err := client.Do(req)
		if err != nil {
			continue
		}
		location := resp.Header.Get("Location")
		readResponseBody(resp)
		if strings.HasPrefix(strings.ToLower(location), "http") {
			return location
		}
	}
	return ""
}

func loadPageInfo(cfg *Config, userIP string) *pageInfo {
	u, err := url.Parse(cfg.PortalURL)
	if err != nil || u.Hostname() == "" {
		return nil
	}
	port := cfg.EportalPort
	if port <= 0 {
		port = defaultEportalPort
	}
	u.Host = net.JoinHostPort(u.Hostname(), strconv.Itoa(port))
	u.Path = "/eportal/portal/page/loadConfig"
	u.RawQuery = "callback=dr1&program_index=&wlan_vlan_id=1&wlan_user_ip=" +
		url.QueryEscape(base64.StdEncoding.EncodeToString([]byte(userIP))) +
		"&wlan_user_ipv6=Ojo%3D&wlan_user_ssid=&wlan_user_areaid=&wlan_ac_ip=&wlan_ap_mac=000000000000" +
		"&gw_id=000000000000&page_index=&jsVersion=4.X&v=1234&lang=zh"

	client := newHTTPClient(5 * time.Second)
	req, err := newRequest("GET", u.String())
	if err != nil {
		return nil
	}
	resp, err := client.Do(req)
	if err != nil {
		return nil
	}
	body := decodeText(readResponseBody(resp))
	var root struct {
		Data map[string]any `json:"data"`
	}
	if err := json.Unmarshal([]byte(jsonpPayload(body)), &root); err != nil || root.Data == nil {
		return nil
	}
	return &pageInfo{
		ProgramIndex:  toText(root.Data["program_index"]),
		PageIndex:     toText(root.Data["page_index"]),
		AccountPrefix: toInt(root.Data["account_prefix"]),
	}
}

// Login 按 Dr.COM ePortal 的 portal_login 流程提交账号密码。
func Login(cfg *Config) *PortalResult {
	result := &PortalResult{}
	userIP := LocalIP(hostOf(cfg.PortalURL))
	mac := "000000000000"
	acName := ""

	if redirect := portalRedirect(cfg); redirect != "" {
		if u, err := url.Parse(redirect); err == nil {
			query := u.Query()
			if v := query.Get("wlanuserip"); v != "" {
				userIP = v
			}
			if v := query.Get("mac"); v != "" {
				mac = v
			}
			if v := query.Get("wlanacname"); v != "" {
				acName = v
			}
		}
	}

	page := loadPageInfo(cfg, userIP)
	prefix := ""
	if page != nil && page.AccountPrefix == 1 {
		prefix = ",0,"
	}
	password := cfg.Password()

	params := [][2]string{
		{"callback", "dr" + strconv.Itoa(randomBetween(1000, 9999))},
		{"DDDDD", cfg.Account},
		{"upass", password},
		{"0MKKey", "123456"},
		{"R1", "0"},
		{"R2", ""},
		{"R3", "0"},
		{"R6", "0"},
		{"para", "00"},
		{"v6ip", ""},
		{"R7", "0"},
		{"user_account", prefix + cfg.Account},
		{"user_password", password},
		{"wlan_user_ip", userIP},
		{"wlan_user_ipv6", ""},
		{"authex_enable", ""},
		{"wlan_user_mac", mac},
		{"wlan_ac_name", acName},
		{"jsVersion", "4.X"},
		{"terminal_type", "1"},
		{"lang", "zh"},
		{"user_agent", userAgent},
		{"enable_r3", "0"},
		{"mac_type", "0"},
		{"rcn", ""},
		{"operate", "portal_login"},
		{"business_type", ""},
		{"v", strconv.Itoa(randomBetween(500, 9999))},
	}
	if page != nil {
		params = append(params, [2]string{"program_index", page.ProgramIndex})
		params = append(params, [2]string{"page_index", page.PageIndex})
	}

	var query, masked strings.Builder
	for i, kv := range params {
		if i > 0 {
			query.WriteByte('&')
			masked.WriteByte('&')
		}
		key := escapeQuery(kv[0])
		query.WriteString(key + "=" + escapeQuery(kv[1]))
		value := escapeQuery(kv[1])
		if kv[0] == "upass" || kv[0] == "user_password" {
			value = "***"
		}
		masked.WriteString(key + "=" + value)
	}

	loginURL := strings.TrimRight(cfg.PortalURL, "/") + cfg.LoginPath + "?" + query.String()
	result.MaskedURL = strings.TrimRight(cfg.PortalURL, "/") + cfg.LoginPath + "?" + masked.String()

	LogInfo("本机IP=%s MAC=%s AC=%s", userIP, mac, acName)
	LogInfo("登录请求：%s", result.MaskedURL)

	client := newHTTPClient(8 * time.Second)
	req, err := newRequest("GET", loginURL)
	if err != nil {
		result.Message = err.Error()
		LogWarn("登录请求异常：%v", err)
		return result
	}
	resp, err := client.Do(req)
	if err != nil {
		result.Message = err.Error()
		LogWarn("登录请求异常：%v", err)
		return result
	}
	body := decodeText(readResponseBody(resp))
	result.Raw = body
	LogInfo("登录响应：%s", strings.TrimSpace(body))

	var data map[string]any
	_ = json.Unmarshal([]byte(jsonpPayload(body)), &data)
	if data != nil {
		result.Result = toText(data["result"])
		result.Message = toText(data["msg"])
		if msga := toText(data["msga"]); msga != "" {
			if result.Message == "" {
				result.Message = msga
			} else {
				result.Message += " / " + msga
			}
		}
		if result.Result == "1" || strings.EqualFold(result.Result, "ok") {
			result.Success = true
		} else if strings.Contains(strings.ToLower(result.Message), "online") {
			result.Success = true
			result.AlreadyOnline = true
		}
	}
	return result
}

func escapeQuery(value string) string {
	return strings.ReplaceAll(url.QueryEscape(value), "+", "%20")
}

func randomBetween(min, max int) int {
	if max <= min {
		return min
	}
	return min + rand.Intn(max-min+1)
}

func jsonpPayload(body string) string {
	start := strings.Index(body, "(")
	end := strings.LastIndex(body, ")")
	if start >= 0 && end > start {
		return strings.TrimSpace(body[start+1 : end])
	}
	return strings.TrimSpace(body)
}

func toText(value any) string {
	switch v := value.(type) {
	case nil:
		return ""
	case string:
		return v
	case json.Number:
		return v.String()
	case float64:
		return strconv.FormatFloat(v, 'f', -1, 64)
	case bool:
		return strconv.FormatBool(v)
	default:
		return fmt.Sprint(v)
	}
}

func toInt(value any) int {
	n, err := strconv.Atoi(toText(value))
	if err != nil {
		return 0
	}
	return n
}
