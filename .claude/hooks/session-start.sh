#!/bin/bash
# Installs GSD Core (https://github.com/open-gsd/gsd-core) globally for Claude Code
# in cloud sessions, where ~/.claude does not persist between containers.
# GSD Core requires Node >=24, so Node 24 is installed first and made the
# session's default node.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

NODE_MAJOR=24
NODE_DIR="/opt/node${NODE_MAJOR}"
NODE_BIN="$NODE_DIR/bin/node"

if [ ! -x "$NODE_BIN" ]; then
  dist="https://nodejs.org/dist/latest-v${NODE_MAJOR}.x"
  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' EXIT
  tarball="$(curl -fsSL "$dist/SHASUMS256.txt" | awk '/linux-x64\.tar\.xz$/ {print $2}')"
  curl -fsSL "$dist/SHASUMS256.txt" -o "$tmp/SHASUMS256.txt"
  curl -fsSL "$dist/$tarball" -o "$tmp/$tarball"
  (cd "$tmp" && grep " $tarball\$" SHASUMS256.txt | sha256sum -c - >&2)
  mkdir -p "$NODE_DIR"
  tar -xJf "$tmp/$tarball" -C "$NODE_DIR" --strip-components=1
fi

export PATH="$NODE_DIR/bin:$PATH"
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  echo "export PATH=\"$NODE_DIR/bin:\$PATH\"" >> "$CLAUDE_ENV_FILE"
fi

# The installer bakes the node binary it runs under into GSD's hook commands,
# so reinstall unless an existing install already points at Node 24.
if [ -f "$HOME/.claude/gsd-core/VERSION" ] && grep -q "$NODE_BIN" "$HOME/.claude/settings.json" 2>/dev/null; then
  exit 0
fi

npx -y @opengsd/gsd-core@latest --claude --global < /dev/null >&2
