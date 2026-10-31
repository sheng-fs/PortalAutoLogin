package main

import (
	"net"
	"os"
	"strconv"
	"strings"
)

type NetworkKind int

const (
	NetworkNone NetworkKind = iota
	NetworkWiFi
	NetworkOther
)

func (k NetworkKind) String() string {
	switch k {
	case NetworkWiFi:
		return "Wi-Fi"
	case NetworkOther:
		return "有线/其它网络"
	default:
		return "无网络"
	}
}

// ActiveNetworkKind 读取 /proc/net/route 的默认路由，再结合 sysfs 判断是否 Wi-Fi。
func ActiveNetworkKind() (NetworkKind, string) {
	iface := defaultRouteInterface()
	if iface != "" {
		if isWireless(iface) {
			return NetworkWiFi, iface
		}
		return NetworkOther, iface
	}
	entries, err := os.ReadDir("/sys/class/net")
	if err != nil {
		return NetworkNone, ""
	}
	for _, entry := range entries {
		name := entry.Name()
		if name == "lo" || !isUp(name) {
			continue
		}
		if isWireless(name) && hasIPv4(name) {
			return NetworkWiFi, name
		}
	}
	for _, entry := range entries {
		name := entry.Name()
		if name != "lo" && isUp(name) {
			return NetworkOther, name
		}
	}
	return NetworkNone, ""
}

func defaultRouteInterface() string {
	data, err := os.ReadFile("/proc/net/route")
	if err != nil {
		return ""
	}
	for _, line := range splitLines(string(data))[1:] {
		fields := strings.Fields(line)
		if len(fields) < 4 || fields[1] != "00000000" {
			continue
		}
		flags, _ := strconv.ParseUint(fields[3], 16, 32)
		if flags&0x2 == 0 { // RTF_GATEWAY
			continue
		}
		return fields[0]
	}
	return ""
}

func isWireless(iface string) bool {
	if _, err := os.Stat("/sys/class/net/" + iface + "/wireless"); err == nil {
		return true
	}
	if _, err := os.Stat("/sys/class/net/" + iface + "/phy80211"); err == nil {
		return true
	}
	return strings.HasPrefix(iface, "wl")
}

func isUp(iface string) bool {
	data, err := os.ReadFile("/sys/class/net/" + iface + "/operstate")
	if err != nil {
		return false
	}
	return strings.TrimSpace(string(data)) == "up"
}

func hasIPv4(iface string) bool {
	link, err := net.InterfaceByName(iface)
	if err != nil {
		return false
	}
	addrs, err := link.Addrs()
	if err != nil {
		return false
	}
	for _, addr := range addrs {
		if ipnet, ok := addr.(*net.IPNet); ok && ipnet.IP.To4() != nil && !ipnet.IP.IsLoopback() {
			return true
		}
	}
	return false
}
