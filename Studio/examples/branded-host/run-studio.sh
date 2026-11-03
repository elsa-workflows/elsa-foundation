#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../../.." && pwd)"
output="${LINEN_HOST_OUTPUT:-$(mktemp -d "${TMPDIR:-/tmp}/linen-studio.XXXXXX")}"
# Publish the standard host; the application receives its own static assets/configuration.
dotnet publish "$root/Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj" -f net10.0 -o "$output"
mkdir -p "$output/wwwroot/branding"
cp "$root/Studio/examples/branded-host/logo.svg" "$output/wwwroot/branding/logo.svg"
cd "$output"
export ASPNETCORE_ENVIRONMENT=Development
export Studio__Branding__ProductName='Northstar Studio'
export Studio__Branding__Subtitle='Automation workspace'
export Studio__Branding__HelpUrl=''
export Studio__Branding__PrivacyUrl=''
export Studio__Branding__LogoUrl='/branding/logo.svg'
export Studio__Branding__FaviconUrl='/branding/logo.svg'
export Studio__Appearance__WorkspaceStyle=linen
export Studio__Appearance__PaletteId=linen
export Studio__Appearance__Mode=system
export Studio__Appearance__Navigation=left
export Studio__BackendBaseUrl="${LINEN_BACKEND_URL:-https://localhost:7243}"
exec dotnet Elsa.Studio.Web.dll --urls "${LINEN_STUDIO_URL:-http://127.0.0.1:7086}"
