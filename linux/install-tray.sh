#!/usr/bin/env bash
set -euo pipefail

BIN_NAME="portalautologin-tray"
INSTALL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BIN_PATH="$INSTALL_DIR/$BIN_NAME"
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
RUN_GROUP="$(id -gn "$RUN_USER")"
if [ -z "$RUN_HOME" ] || [ ! -d "$RUN_HOME" ]; then
  echo "无法获取用户 $RUN_USER 的主目录" >&2
  exit 1
fi

missing=()
if ! ldconfig -p 2>/dev/null | grep -q 'libgtk-3.so.0'; then
  missing+=("GTK3")
fi
if ! ldconfig -p 2>/dev/null | grep -qE 'libayatana-appindicator3\.so|libappindicator3\.so'; then
  missing+=("AppIndicator")
fi
if [ "${#missing[@]}" -gt 0 ]; then
  echo "缺少运行库：${missing[*]}" >&2
  echo "请先安装（按发行版任选）：" >&2
  echo "  Debian/Ubuntu: sudo apt install libgtk-3-0 libayatana-appindicator3-1" >&2
  echo "  Fedora:        sudo dnf install gtk3 libayatana-appindicator-gtk3" >&2
  echo "  Arch:          sudo pacman -S gtk3 libayatana-appindicator" >&2
  exit 1
fi

chmod 0755 "$BIN_PATH"
if command -v chcon >/dev/null 2>&1; then
  chcon -t bin_t "$BIN_PATH" 2>/dev/null || true
fi
mkdir -p /usr/local/bin
ln -sfn "$BIN_PATH" "$LINK_PATH"

mkdir -p "$(dirname "$ICON_PATH")"
install -m 0644 "$INSTALL_DIR/portalautologin.png" "$ICON_PATH"
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -f -t /usr/share/icons/hicolor >/dev/null 2>&1 || true
fi

mkdir -p "$RUN_HOME/.config/autostart" "$RUN_HOME/.local/share/applications"

cat > "$RUN_HOME/.config/autostart/portalautologin-tray.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=PortalAutoLogin
Name[zh_CN]=PortalAutoLogin 门户自动登录
Comment=Portal 门户自动登录（托盘）
Exec=$BIN_PATH tray --hidden
Icon=portalautologin
Terminal=false
Categories=Network;Utility;
X-GNOME-Autostart-enabled=true
StartupNotify=false
EOF

cat > "$RUN_HOME/.local/share/applications/portalautologin-tray.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=PortalAutoLogin
Name[zh_CN]=PortalAutoLogin 门户自动登录
Comment=Portal 门户自动登录（托盘）
Exec=$BIN_PATH tray
Icon=portalautologin
Terminal=false
Categories=Network;Utility;
StartupNotify=false
EOF

chown "$RUN_USER:$RUN_GROUP" "$RUN_HOME/.config/autostart/portalautologin-tray.desktop" "$RUN_HOME/.local/share/applications/portalautologin-tray.desktop"

echo "PortalAutoLogin 托盘版已安装：$BIN_PATH"
echo "命令：$LINK_PATH"
echo "登录后自动启动：$RUN_HOME/.config/autostart/portalautologin-tray.desktop"
echo

if systemctl is-active --quiet portalautologin.service 2>/dev/null; then
  echo "注意：检测到 portalautologin.service 正在运行。"
  echo "托盘版和 systemd 服务同时运行会重复检测，建议只保留一种："
  echo "  sudo systemctl disable --now portalautologin.service"
  echo
fi

if [ ! -f "$RUN_HOME/.config/portalautologin/config.conf" ]; then
  echo "首次使用需要填写账号密码："
  if [ "$RUN_USER" = "$(id -un)" ]; then
    echo "  $BIN_NAME config"
  else
    echo "  sudo -u $RUN_USER -H $BIN_NAME config"
  fi
  echo "也可以启动托盘界面后在窗口里填写。"
else
  echo "已检测到配置：$RUN_HOME/.config/portalautologin/config.conf"
fi

echo
echo "启动托盘：$BIN_NAME tray"
echo "卸载：sudo $INSTALL_DIR/uninstall-tray.sh $RUN_USER"
