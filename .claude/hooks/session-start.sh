#!/bin/bash
# Installs GSD Core (https://github.com/open-gsd/gsd-core) globally for Claude Code
# in cloud sessions, where ~/.claude does not persist between containers.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

if [ -f "$HOME/.claude/gsd-core/VERSION" ]; then
  exit 0
fi

npx -y @opengsd/gsd-core@latest --claude --global < /dev/null
