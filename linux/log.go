package main

import (
	"fmt"
	"os"
	"path/filepath"
	"sync"
	"time"
)

const maxLogBytes = 1024 * 1024

var logMu sync.Mutex

func logFilePath() string {
	return filepath.Join(configDir(), "logs", "portalautologin.log")
}

func LogInfo(format string, args ...any)  { writeLog("INFO", format, args...) }
func LogWarn(format string, args ...any)  { writeLog("WARN", format, args...) }
func LogError(format string, args ...any) { writeLog("ERROR", format, args...) }

func writeLog(level string, format string, args ...any) {
	line := fmt.Sprintf("%s [%s] %s\n", time.Now().Format("2006-01-02 15:04:05"), level, fmt.Sprintf(format, args...))

	logMu.Lock()
	defer logMu.Unlock()

	path := logFilePath()
	if err := os.MkdirAll(filepath.Dir(path), 0o700); err == nil {
		if info, err := os.Stat(path); err == nil && info.Size() > maxLogBytes {
			_ = os.Rename(path, path+".old")
		}
		if f, err := os.OpenFile(path, os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0o600); err == nil {
			_, _ = f.WriteString(line)
			_ = f.Close()
		}
	}

	// 守护进程由 systemd 托管，同时写一份到 stderr 方便 journalctl 查看。
	_, _ = os.Stderr.WriteString(line)
}

func tailFile(path string, lines int) []string {
	data, err := os.ReadFile(path)
	if err != nil {
		return nil
	}
	all := splitLines(string(data))
	if lines > 0 && len(all) > lines {
		all = all[len(all)-lines:]
	}
	return all
}

func splitLines(text string) []string {
	var out []string
	start := 0
	for i := 0; i < len(text); i++ {
		if text[i] == '\n' {
			out = append(out, text[start:i])
			start = i + 1
		}
	}
	if start < len(text) {
		out = append(out, text[start:])
	}
	return out
}
