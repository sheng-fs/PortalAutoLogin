#!/usr/bin/env bash
set -euo pipefail

BIN_NAME="portalautologin-tray"
LINK_PATH="/usr/local/bin/$BIN_NAME"
ICON_PATH="/usr/share/icons/hicolor/256x256/apps/portalautologin.png"

if [ "$(id -u)" -ne 0 ]; then
  echo "请使用 root 权限运行：sudo $0 [运行用户]" >&2
  exit 1
fi

RUN_USER="${1:-${SUDO_USER:-root}}"
if ! id -u "$RUN_USER" >/dev/null 2>&1; then
  echo "用户不存在：$RUN_USER" >&2
  exit 1
fi
RUN_HOME="$(getent passwd "$RUN_USER" | cut -d: -f6)"

pkill -u "$RUN_USER" -f "$BIN_NAME tray" 2>/dev/null || true

rm -f "$RUN_HOME/.config/autostart/portalautologin-tray.desktop"
rm -f "$RUN_HOME/.local/share/applications/portalautologin-tray.desktop"
rm -f "$LINK_PATH"
rm -f "$ICON_PATH"
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -f -t /usr/share/icons/hicolor >/dev/null 2>&1 || true
fi

echo "已移除托盘版自启动、应用菜单项、命令链接与图标。"
echo "用户配置未删除：$RUN_HOME/.config/portalautologin"
