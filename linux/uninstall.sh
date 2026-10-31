#!/usr/bin/env bash
set -euo pipefail

BIN_NAME="portalautologin"
UNIT_PATH="/etc/systemd/system/portalautologin.service"
LINK_PATH="/usr/local/bin/$BIN_NAME"

if [ "$(id -u)" -ne 0 ]; then
  echo "请使用 root 权限运行：sudo $0" >&2
  exit 1
fi

systemctl disable --now portalautologin.service 2>/dev/null || true
rm -f "$UNIT_PATH"
systemctl daemon-reload
rm -f "$LINK_PATH"

echo "已停止并移除 portalautologin 服务与 $LINK_PATH。"
echo "安装目录与用户配置未删除："
echo "  /opt/portalautologin"
echo "  ~/.config/portalautologin"
