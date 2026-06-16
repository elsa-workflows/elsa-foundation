# Quickstart: Validate Elsa Studio UI System

## Prerequisites

- `pnpm install`
- .NET SDK compatible with the repository

## Build and test

```bash
pnpm build
pnpm test
dotnet build
dotnet test
```

## Run the Studio shell

```bash
dotnet run --project src/Elsa.Studio.Web/Elsa.Studio.Web.csproj
```

Open the Studio shell and validate:

- `/` renders with the shared shell, compact command/header rhythm, and no
  oversized dashboard typography.
- `/features` uses the split inspector configuration pattern.
- `/diagnostics/modules` remains usable until the dedicated module management
  page lands.
- The bottom panel remains resizable and keyboard accessible when present.

## Visual validation checklist

- Page titles are compact and not hero-scale.
- Resource-heavy content uses rows, grids, lists, or inspectors instead of
  defaulting to large cards.
- Feature and module statuses use shared semantic chips.
- Form controls align to shared label, validation, disabled, and dirty states.
- Light and dark themes keep status, focus, and text contrast readable.
- Module-specific CSS does not define unmanaged typography, color, status, or
  card systems.

## Module author validation

Create or update a representative module page using shared primitives only. The
page passes when it looks native without copying private host CSS selectors and
without redefining typography or status colors locally.
