#!/bin/bash
# Prepares Claude Code cloud sessions (including the UX fix routine, docs/agents/ux-fix-routine.md) to build and test
# Studio: installs a .NET 10 SDK and activates the pnpm version pinned in package.json. Idempotent; never blocks the
# session: when a download fails it says why and exits 0.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

cd "${CLAUDE_PROJECT_DIR:-.}"
install_dir="$HOME/.dotnet"

if [ -x "$install_dir/dotnet" ] && "$install_dir/dotnet" --list-sdks 2>/dev/null | grep -q "^10\."; then
  echo "A .NET 10 SDK is already installed."
else
  script="$(mktemp)"
  # Bounded, so a stalled download reaches the warning below instead of holding the session at startup.
  if curl -fsSL --connect-timeout 20 --max-time 120 https://dot.net/v1/dotnet-install.sh -o "$script" \
     && timeout 900 bash "$script" --channel 10.0 --install-dir "$install_dir" --no-path >/dev/null; then
    echo "Installed the .NET 10 SDK into $install_dir."
  else
    echo "WARNING: could not install the .NET 10 SDK. Allow dot.net and builds.dotnet.microsoft.com" \
         "in the cloud environment's network access settings." >&2
  fi
  rm -f "$script"
fi

pnpm_version=$(sed -n 's/.*"packageManager": *"pnpm@\([^"]*\)".*/\1/p' package.json)
if command -v corepack >/dev/null && corepack enable pnpm 2>/dev/null && timeout 300 corepack prepare "pnpm@$pnpm_version" --activate >/dev/null 2>&1; then
  echo "Activated pnpm $pnpm_version."
else
  echo "WARNING: could not activate pnpm $pnpm_version through corepack; run 'npx pnpm@$pnpm_version' instead." >&2
fi

# Write .NET environment exports to stdout for the session env file or shell profile.
# Takes no arguments; reads install_dir and preserves a literal $PATH for later expansion.
exports() {
  echo "export DOTNET_ROOT=\"$install_dir\""
  echo "export PATH=\"$install_dir:\$PATH\""
  echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
  echo "export DOTNET_NOLOGO=1"
}

if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  exports >> "$CLAUDE_ENV_FILE"
else
  # No env file from the harness: later shells start from the profile, so persist the exports there once.
  marker="# elsa-foundation-studio: dotnet SDK"
  if ! grep -qxF "$marker" "$HOME/.bashrc" 2>/dev/null; then
    { echo "$marker"; exports; } >> "$HOME/.bashrc"
  fi
fi
