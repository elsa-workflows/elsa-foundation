#!/bin/bash
# Installs the .NET SDK pinned in global.json for Claude Code cloud sessions, so agents (including the
# quality review and fix routines, ADR 0080 D7) can restore, build and test. Idempotent; never blocks the
# session: when the SDK cannot be downloaded it says why and exits 0.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

cd "${CLAUDE_PROJECT_DIR:-.}"
required=$(sed -n 's/.*"version": *"\([^"]*\)".*/\1/p' global.json | head -1)
install_dir="$HOME/.dotnet"

if [ -x "$install_dir/dotnet" ] && "$install_dir/dotnet" --list-sdks 2>/dev/null | grep -q "^$required "; then
  echo "dotnet SDK $required already installed."
else
  script="$(mktemp)"
  if curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$script" \
     && bash "$script" --jsonfile global.json --install-dir "$install_dir" --no-path >/dev/null; then
    echo "Installed dotnet SDK $required into $install_dir."
  else
    echo "WARNING: could not install dotnet SDK $required. Allow dot.net and builds.dotnet.microsoft.com" \
         "(and f.feedz.io for the Nuplane/BPMN feeds) in the cloud environment's network access settings." >&2
    rm -f "$script"
    exit 0
  fi
  rm -f "$script"
fi

if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo "export DOTNET_ROOT=\"$install_dir\""
    echo "export PATH=\"$install_dir:\$PATH\""
    echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
    echo "export DOTNET_NOLOGO=1"
  } >> "$CLAUDE_ENV_FILE"
fi
