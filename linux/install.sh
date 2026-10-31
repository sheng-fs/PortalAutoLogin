#!/usr/bin/env bash
set -euo pipefail

BIN_NAME="portalautologin"
INSTALL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BIN_PATH="$INSTALL_DIR/$BIN_NAME"
UNIT_PATH="/etc/systemd/system/portalautologin.service"
LINK_PATH="/usr/local/bin/$BIN_NAME"

if [ "$(id -u)" -ne 0 ]; then
  echo "请使用 root 权限运行：sudo $0 [运行用户]" >&2
  exit 1
fi
if ! command -v systemctl >/dev/null 2>&1; then
  echo "未找到 systemd（systemctl），Linux 版需要 systemd 托管后台服务。" >&2
  exit 1
fi

RUN_USER="${1:-${SUDO_USER:-root}}"
if ! id -u "$RUN_USER" >/dev/null 2>&1; then
  echo "用户不存在：$RUN_USER" >&2
  exit 1
fi
RUN_HOME="$(getent passwd "$RUN_USER" | cut -d: -f6)"
if [ -z "$RUN_HOME" ] || [ ! -d "$RUN_HOME" ]; then
  echo "无法获取用户 $RUN_USER 的主目录" >&2
  exit 1
fi

chmod 0755 "$BIN_PATH"
# Fedora / RHEL 等 SELinux 发行版需要可执行文件带 bin_t 标签，否则 systemd 可能拒绝启动。
if command -v chcon >/dev/null 2>&1; then
  chcon -t bin_t "$BIN_PATH" 2>/dev/null || true
fi
mkdir -p /usr/local/bin
ln -sfn "$BIN_PATH" "$LINK_PATH"

sed -e "s|__USER__|$RUN_USER|g" \
    -e "s|__HOME__|$RUN_HOME|g" \
    -e "s|__BIN__|$BIN_PATH|g" \
    "$INSTALL_DIR/portalautologin.service" > "$UNIT_PATH"
chmod 0644 "$UNIT_PATH"

systemctl daemon-reload
systemctl enable --now portalautologin.service

echo "PortalAutoLogin 已安装：$BIN_PATH"
echo "命令：$LINK_PATH"
echo "服务用户：$RUN_USER"
echo

CONFIG_FILE="$RUN_HOME/.config/portalautologin/config.conf"
if [ -f "$CONFIG_FILE" ]; then
  echo "已检测到配置：$CONFIG_FILE"
  echo "查看状态：$BIN_NAME status"
else
  cat <<EOF
首次使用还需要填写账号密码：
  $BIN_NAME config
或（切换到运行服务的用户）：
  sudo -u $RUN_USER -H $BIN_NAME config

保存后守护进程会自动开始检测，无需重启服务。
EOF
fi

echo
echo "常用命令："
echo "  $BIN_NAME status          查看在线状态"
echo "  $BIN_NAME login           立即登录一次"
echo "  $BIN_NAME logs -f         查看日志"
echo "  sudo systemctl status portalautologin"
