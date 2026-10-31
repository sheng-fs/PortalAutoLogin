//go:build !tray

package main

import (
	"fmt"
	"os"
)

func runTray() {
	fmt.Fprintln(os.Stderr, "当前二进制不包含图形界面，请安装 portalautologin-tray 版本。")
	os.Exit(1)
}
