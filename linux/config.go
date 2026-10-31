package main

import (
	"crypto/aes"
	"crypto/cipher"
	"crypto/rand"
	"crypto/sha256"
	"encoding/base64"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"sync"
)

const (
	defaultCheckURL    = "http://connect.rom.miui.com/generate_204"
	defaultLoginPath   = "/drcom/login"
	defaultEportalPort = 803
	defaultInterval    = 15
	minInterval        = 5
)

type Config struct {
	Account         string
	PasswordEnc     string
	PortalURL       string
	LoginPath       string
	EportalPort     int
	CheckURL        string
	IntervalSeconds int
	WiFiOnly        bool
}

type State struct {
	LastLogin  string
	TodayCount int
	CountDate  string
}

var configMu sync.Mutex

func configDir() string {
	if dir := os.Getenv("PORTAL_CONFIG_DIR"); dir != "" {
		return dir
	}
	if dir := os.Getenv("XDG_CONFIG_HOME"); dir != "" {
		return filepath.Join(dir, "portalautologin")
	}
	home, err := os.UserHomeDir()
	if err != nil || home == "" {
		home = "/root"
	}
	return filepath.Join(home, ".config", "portalautologin")
}

func ConfigFile() string { return filepath.Join(configDir(), "config.conf") }

func StateFile() string { return filepath.Join(configDir(), "state.conf") }

func defaultConfig() *Config {
	return &Config{
		LoginPath:       defaultLoginPath,
		EportalPort:     defaultEportalPort,
		CheckURL:        defaultCheckURL,
		IntervalSeconds: defaultInterval,
	}
}

func LoadConfig() (*Config, error) {
	configMu.Lock()
	defer configMu.Unlock()
	return loadConfigLocked()
}

func loadConfigLocked() (*Config, error) {
	data, err := os.ReadFile(ConfigFile())
	if err != nil {
		return nil, err
	}
	cfg := defaultConfig()
	for _, line := range strings.Split(string(data), "\n") {
		line = strings.TrimSpace(line)
		if line == "" || strings.HasPrefix(line, "#") {
			continue
		}
		key, value, ok := strings.Cut(line, "=")
		if !ok {
			continue
		}
		key = strings.ToLower(strings.TrimSpace(key))
		value = strings.TrimSpace(value)
		switch key {
		case "account":
			cfg.Account = value
		case "password":
			if value != "" {
				cfg.PasswordEnc = value
			}
		case "password_enc":
			cfg.PasswordEnc = value
		case "portal_url":
			cfg.PortalURL = value
		case "login_path":
			cfg.LoginPath = value
		case "eportal_port":
			cfg.EportalPort = atoiDefault(value, defaultEportalPort)
		case "check_url":
			cfg.CheckURL = value
		case "interval_seconds":
			cfg.IntervalSeconds = atoiDefault(value, defaultInterval)
		case "wifi_only":
			cfg.WiFiOnly = strings.EqualFold(value, "true") || value == "1"
		}
	}
	normalizeConfig(cfg)
	return cfg, nil
}

func normalizeConfig(cfg *Config) {
	cfg.PortalURL = strings.TrimSpace(strings.TrimSuffix(cfg.PortalURL, "/"))
	if cfg.LoginPath == "" {
		cfg.LoginPath = defaultLoginPath
	}
	if !strings.HasPrefix(cfg.LoginPath, "/") {
		cfg.LoginPath = "/" + cfg.LoginPath
	}
	if cfg.EportalPort <= 0 {
		cfg.EportalPort = defaultEportalPort
	}
	if cfg.CheckURL == "" {
		cfg.CheckURL = defaultCheckURL
	}
	if cfg.IntervalSeconds < minInterval {
		cfg.IntervalSeconds = defaultInterval
	}
}

func SaveConfig(cfg *Config) error {
	configMu.Lock()
	defer configMu.Unlock()
	return saveConfigLocked(cfg)
}

func saveConfigLocked(cfg *Config) error {
	normalizeConfig(cfg)
	var b strings.Builder
	b.WriteString("# PortalAutoLogin Linux 配置（由 portalautologin config 生成）\n")
	b.WriteString("# 认证服务器留空表示自动识别；修改后守护进程会自动重新加载。\n")
	fmt.Fprintf(&b, "account=%s\n", cfg.Account)
	if cfg.PasswordEnc != "" {
		fmt.Fprintf(&b, "password_enc=%s\n", cfg.PasswordEnc)
	}
	fmt.Fprintf(&b, "portal_url=%s\n", cfg.PortalURL)
	fmt.Fprintf(&b, "login_path=%s\n", cfg.LoginPath)
	fmt.Fprintf(&b, "eportal_port=%d\n", cfg.EportalPort)
	fmt.Fprintf(&b, "check_url=%s\n", cfg.CheckURL)
	fmt.Fprintf(&b, "interval_seconds=%d\n", cfg.IntervalSeconds)
	fmt.Fprintf(&b, "wifi_only=%t\n", cfg.WiFiOnly)

	if err := os.MkdirAll(configDir(), 0o700); err != nil {
		return err
	}
	tmp := ConfigFile() + ".tmp"
	if err := os.WriteFile(tmp, []byte(b.String()), 0o600); err != nil {
		return err
	}
	return os.Rename(tmp, ConfigFile())
}

func (c *Config) Password() string {
	return decryptSecret(c.PasswordEnc)
}

func (c *Config) SetPassword(plain string) {
	c.PasswordEnc = encryptSecret(plain)
}

func LoadState() *State {
	st := &State{}
	data, err := os.ReadFile(StateFile())
	if err != nil {
		return st
	}
	for _, line := range strings.Split(string(data), "\n") {
		key, value, ok := strings.Cut(line, "=")
		if !ok {
			continue
		}
		switch strings.TrimSpace(key) {
		case "last_login":
			st.LastLogin = strings.TrimSpace(value)
		case "today_count":
			st.TodayCount = atoiDefault(strings.TrimSpace(value), 0)
		case "count_date":
			st.CountDate = strings.TrimSpace(value)
		}
	}
	return st
}

func SaveState(st *State) {
	_ = os.MkdirAll(configDir(), 0o700)
	body := fmt.Sprintf("last_login=%s\ntoday_count=%d\ncount_date=%s\n", st.LastLogin, st.TodayCount, st.CountDate)
	tmp := StateFile() + ".tmp"
	if err := os.WriteFile(tmp, []byte(body), 0o600); err == nil {
		_ = os.Rename(tmp, StateFile())
	}
}

func (s *State) RecordLogin(now string, today string) {
	if s.CountDate != today {
		s.CountDate = today
		s.TodayCount = 0
	}
	s.TodayCount++
	s.LastLogin = now
	SaveState(s)
}

func atoiDefault(text string, fallback int) int {
	n, err := strconv.Atoi(text)
	if err != nil {
		return fallback
	}
	return n
}

// 与 Windows 版 DPAPI 等价的本机保护：密钥由 machine-id + 当前 UID 派生，
// 配置文件本身仍为 0600，避免密码以明文落盘。
func secretKey() []byte {
	machine := readTrim("/etc/machine-id")
	if machine == "" {
		machine = readTrim("/var/lib/dbus/machine-id")
	}
	if machine == "" {
		machine, _ = os.Hostname()
	}
	sum := sha256.Sum256([]byte("PortalAutoLogin.V1.2026|" + machine + "|" + strconv.Itoa(os.Getuid())))
	return sum[:]
}

func encryptSecret(plain string) string {
	if plain == "" {
		return ""
	}
	key := secretKey()
	block, err := aes.NewCipher(key)
	if err != nil {
		return ""
	}
	gcm, err := cipher.NewGCM(block)
	if err != nil {
		return ""
	}
	nonce := make([]byte, gcm.NonceSize())
	if _, err := rand.Read(nonce); err != nil {
		return ""
	}
	sealed := gcm.Seal(nonce, nonce, []byte(plain), nil)
	return "enc:v1:" + base64.StdEncoding.EncodeToString(sealed)
}

func decryptSecret(stored string) string {
	if stored == "" {
		return ""
	}
	if !strings.HasPrefix(stored, "enc:v1:") {
		// 兼容手工写入的明文 password。
		return stored
	}
	raw, err := base64.StdEncoding.DecodeString(strings.TrimPrefix(stored, "enc:v1:"))
	if err != nil {
		return ""
	}
	block, err := aes.NewCipher(secretKey())
	if err != nil {
		return ""
	}
	gcm, err := cipher.NewGCM(block)
	if err != nil || len(raw) < gcm.NonceSize() {
		return ""
	}
	nonce, data := raw[:gcm.NonceSize()], raw[gcm.NonceSize():]
	plain, err := gcm.Open(nil, nonce, data, nil)
	if err != nil {
		return ""
	}
	return string(plain)
}

func readTrim(path string) string {
	data, err := os.ReadFile(path)
	if err != nil {
		return ""
	}
	return strings.TrimSpace(string(data))
}

var errNoConfig = errors.New("尚未配置账号密码")
