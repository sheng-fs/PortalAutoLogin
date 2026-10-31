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

  tar -C "$stage" -czf "$DIST/PortalAutoLogin-linux-x64-tray.tar.gz" .
  rm -rf "$stage"
else
  echo "跳过托盘版：缺少 gtk+-3.0 / ayatana-appindicator3 开发库"
fi

echo
ls -lh "$DIST"/PortalAutoLogin-linux-*.tar.gz
