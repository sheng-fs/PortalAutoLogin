package main

import (
	"net"
	"net/url"
	"regexp"
	"strconv"
	"strings"
	"time"
)

type Detection struct {
	Online    bool
	PortalURL string
	Evidence  string
}

type probe struct {
	URL    string
	Marker string
}

var defaultProbes = []probe{
	{URL: "http://connect.rom.miui.com/generate_204"},
	{URL: "http://www.gstatic.com/generate_204"},
	{URL: "http://www.apple.com/library/test/success.html", Marker: "Success"},
	{URL: "http://www.msftconnecttest.com/connecttest.txt", Marker: "Microsoft Connect Test"},
	{URL: "http://detectportal.firefox.com/success.txt", Marker: "success"},
}

var (
	metaRefreshRegex = regexp.MustCompile(`(?is)<meta[^>]+http-equiv\s*=\s*["']?refresh["']?[^>]+content\s*=\s*["']?[^"'>]*url\s*=\s*(?P<url>[^"'>\s]+)`)
	scriptRedirect   = regexp.MustCompile(`(?is)(?:window\.|document\.)?location(?:\.href)?\s*(?:=|\.replace\(|\.assign\()\s*["'](?P<url>https?://[^"']+)["']`)
	baseHrefRegex    = regexp.MustCompile(`(?is)<base[^>]+href\s*=\s*["'](?P<url>https?://[^"']+)["']`)
	formActionRegex  = regexp.MustCompile(`(?is)<form[^>]+action\s*=\s*["'](?P<url>https?://[^"']+)["']`)
)

// Detect 依次探测在线状态与 Portal 地址，逻辑与 Windows 版 PortalDetector 一致。
func Detect(checkURL string) Detection {
	probes := make([]probe, 0, len(defaultProbes)+1)
	if strings.TrimSpace(checkURL) != "" {
		probes = append(probes, probe{URL: strings.TrimSpace(checkURL)})
	}
	probes = append(probes, defaultProbes...)

	for _, p := range probes {
		result, ok := detectFromProbe(p)
		if !ok {
			continue
		}
		if result.Online || result.PortalURL != "" {
			return result
		}
	}
	return Detection{}
}

func detectFromProbe(p probe) (Detection, bool) {
	client := newHTTPClient(4 * time.Second)
	req, err := newRequest("GET", p.URL)
	if err != nil {
		return Detection{}, false
	}
	resp, err := client.Do(req)
	if err != nil {
		return Detection{}, false
	}
	body := decodeText(readResponseBody(resp))
	return interpret(p, resp.StatusCode, resp.Header.Get("Location"), body), true
}

func interpret(p probe, status int, location, body string) Detection {
	if status == 204 {
		return Detection{Online: true, Evidence: "HTTP 204"}
	}
	if status == 200 {
		if p.Marker != "" && strings.Contains(body, p.Marker) {
			return Detection{Online: true, Evidence: "HTTP 200 " + p.Marker}
		}
		if strings.TrimSpace(body) == "" {
			return Detection{Online: true, Evidence: "HTTP 200"}
		}
	}

	if status >= 300 && status < 400 && location != "" {
		if portal := baseFrom(location, p.URL); portal != "" {
			return Detection{PortalURL: portal, Evidence: "HTTP " + strconv.Itoa(status) + " → " + location}
		}
	}

	if looksLikeHTML(body) {
		if redirect := extractRedirect(body); redirect != "" {
			if portal := baseFrom(redirect, p.URL); portal != "" {
				return Detection{PortalURL: portal, Evidence: "页面跳转 → " + redirect}
			}
		}
		if dnsPortal := portalFromDNS(p.URL); dnsPortal != "" {
			return Detection{PortalURL: dnsPortal, Evidence: "DNS 指向内网 " + dnsPortal}
		}
	}
	return Detection{}
}

func portalFromDNS(probeURL string) string {
	u, err := url.Parse(probeURL)
	if err != nil {
		return ""
	}
	addrs, err := net.LookupHost(u.Hostname())
	if err != nil {
		return ""
	}
	for _, addr := range addrs {
		if isPrivateIPv4(addr) {
			return "http://" + addr
		}
	}
	return ""
}

func isPrivateIPv4(text string) bool {
	ip := net.ParseIP(text)
	if ip == nil {
		return false
	}
	v4 := ip.To4()
	if v4 == nil {
		return false
	}
	return v4[0] == 10 ||
		(v4[0] == 172 && v4[1] >= 16 && v4[1] <= 31) ||
		(v4[0] == 192 && v4[1] == 168) ||
		(v4[0] == 169 && v4[1] == 254)
}

func looksLikeHTML(body string) bool {
	lower := strings.ToLower(body)
	return strings.Contains(lower, "<html") ||
		strings.Contains(lower, "<!doctype") ||
		strings.Contains(lower, "<script") ||
		strings.Contains(lower, "<meta") ||
		strings.Contains(lower, "<form")
}

func extractRedirect(body string) string {
	for _, re := range []*regexp.Regexp{metaRefreshRegex, scriptRedirect, baseHrefRegex, formActionRegex} {
		match := re.FindStringSubmatch(body)
		if match != nil {
			for i, name := range re.SubexpNames() {
				if name == "url" && i < len(match) {
					return strings.TrimSpace(match[i])
				}
			}
		}
	}
	return ""
}

// baseFrom 从跳转地址中提取 Portal 根地址；与探测目标同主机时忽略。
func baseFrom(target, baseURL string) string {
	target = strings.TrimSpace(target)
	if target == "" {
		return ""
	}
	if strings.HasPrefix(target, "//") {
		target = "http:" + target
	}
	u, err := url.Parse(target)
	if err != nil || (u.Scheme != "http" && u.Scheme != "https") || u.Hostname() == "" {
		return ""
	}
	base, err := url.Parse(baseURL)
	if err == nil && strings.EqualFold(u.Hostname(), base.Hostname()) {
		return ""
	}
	root := u.Scheme + "://" + u.Hostname()
	if port := u.Port(); port != "" {
		root += ":" + port
	}
	return root
}
