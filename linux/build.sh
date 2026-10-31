#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIST="$ROOT/dist"
VERSION="$(tr -d ' \r\n' < "$ROOT/VERSION")"

if ! command -v go >/dev/null 2>&1; then
  echo "需要 Go 1.22 或更高版本来构建 Linux 版。" >&2
  exit 1
fi

mkdir -p "$DIST"

UPX_BIN="${UPX_BIN:-}"
if [ "${NO_UPX:-0}" = "1" ]; then
  UPX_BIN=""
elif [ -z "$UPX_BIN" ]; then
  UPX_BIN="$(command -v upx 2>/dev/null || true)"
fi

pack_binary() {
  local bin="$1"
  if [ -n "$UPX_BIN" ] && [ -x "$UPX_BIN" ]; then
    # UPX 自身会读取名为 UPX 的环境变量，这里清掉，避免与路径混淆。
    if ! env -u UPX "$UPX_BIN" --best --lzma "$bin" >/tmp/upx-pack.log 2>&1; then
      echo "UPX 压缩失败，保留未压缩二进制：$bin" >&2
      tail -5 /tmp/upx-pack.log >&2
    fi
  fi
}

if [ -n "$UPX_BIN" ]; then
  echo "使用 UPX 压缩：$UPX_BIN"
else
  echo "未找到 upx，生成未压缩包（安装 upx 后体积约减小 25%）"
fi

for pair in "x64:amd64" "arm64:arm64"; do
  name="${pair%%:*}"
  arch="${pair##*:}"
  stage="$DIST/.stage-linux-$name"
  rm -rf "$stage"
  mkdir -p "$stage"

  echo "构建 linux/$arch ..."
  (
    cd "$ROOT/linux"
    CGO_ENABLED=0 GOOS=linux GOARCH="$arch" go build \
      -trimpath -ldflags "-s -w -X main.version=$VERSION" \
      -o "$stage/portalautologin" .
  )

  cp "$ROOT/linux/install.sh" "$ROOT/linux/uninstall.sh" "$ROOT/linux/portalautologin.service" "$stage/"
  cp "$ROOT/LICENSE" "$ROOT/README.md" "$stage/"
  chmod 0755 "$stage/portalautologin" "$stage/install.sh" "$stage/uninstall.sh"
  pack_binary "$stage/portalautologin"

  tar -C "$stage" -czf "$DIST/PortalAutoLogin-linux-$name.tar.gz" .
  rm -rf "$stage"
done

if pkg-config --exists gtk+-3.0 ayatana-appindicator3-0.1 2>/dev/null; then
  stage="$DIST/.stage-linux-tray"
  rm -rf "$stage"
  mkdir -p "$stage"

  echo "构建 linux/amd64 托盘版 ..."
  (
    cd "$ROOT/linux"
    CGO_ENABLED=1 GOOS=linux GOARCH=amd64 go build \
      -tags tray \
      -trimpath -ldflags "-s -w -X main.version=$VERSION" \
      -o "$stage/portalautologin-tray" .
  )

  cp "$ROOT/linux/install-tray.sh" "$ROOT/linux/uninstall-tray.sh" "$ROOT/linux/portalautologin.png" "$stage/"
  cp "$ROOT/LICENSE" "$ROOT/README.md" "$stage/"
  chmod 0755 "$stage/portalautologin-tray" "$stage/install-tray.sh" "$stage/uninstall-tray.sh"
  pack_binary "$stage/portalautologin-tray"

  tar -C "$stage" -czf "$DIST/PortalAutoLogin-linux-x64-tray.tar.gz" .
  rm -rf "$stage"
else
  echo "跳过托盘版：缺少 gtk+-3.0 / ayatana-appindicator3 开发库"
fi

echo
ls -lh "$DIST"/PortalAutoLogin-linux-*.tar.gz
