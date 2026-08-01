#!/usr/bin/env pwsh
# Thin shim. The generators are now one .NET tool (Elsa.Maps.Generator); this path is kept because
# AGENTS.md, docs/maps/README.md and ~27 spec tasks.md files document this exact invocation.
$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
dotnet run --project (Join-Path $scriptDir "Elsa.Maps.Generator") -- domain
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
