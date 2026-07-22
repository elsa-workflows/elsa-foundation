# Theme Configuration Guide

This document describes how to configure custom themes and theme defaults for Elsa Studio.

## Configuration Options

Themes are configured through the standard ASP.NET Core Configuration API under the `Themes`
section. Because the app uses the Configuration API, the same settings can be supplied from any
registered source — `appsettings.json`, environment variables, command-line arguments, user
secrets, etc. — with the usual precedence (later providers override earlier ones).

### appsettings.json

Add a `Themes` section to your `appsettings.json`:

```json
{
  "Themes": {
    "ThemesDirectory": null,
    "ThemesMode": "AddToBuiltIns",
    "DefaultThemeId": null
  }
}
```

### Environment Variables

The same keys are available as environment variables using the standard double-underscore (`__`)
separator. These override values from `appsettings.json`:

- `Themes__ThemesDirectory` - Path to custom themes
- `Themes__ThemesMode` - How to combine with built-ins
- `Themes__DefaultThemeId` - Default theme ID

## Configuration Properties

### ThemesDirectory

**Type:** `string` (nullable)  
**Default:** `null` (use built-in themes only)

Path to a directory or file containing custom themes. Supports:
- **Relative paths**: resolved from the application's content root
- **Absolute paths**: used as-is
- **Single JSON file**: e.g., `/themes/themes.json`
- **Directory**: e.g., `/themes/` loads all `.json` files

### ThemesMode

**Type:** `string`  
**Default:** `"AddToBuiltIns"`  
**Values:**
- `"BuiltInsOnly"` - Use built-in themes (17 built-in themes)
- `"DirectoryOnly"` - Use only themes from the directory
- `"AddToBuiltIns"` - Merge directory themes with built-ins (default)

### DefaultThemeId

**Type:** `string` (nullable)  
**Default:** `null` (first theme in list)

The theme ID to select on first load. Must match an available theme ID (either built-in or custom).

Common built-in theme IDs:
- `"material-design"` - Material Design (default appearance)
- `"black-glass"` - Dark HUD style
- `"stone"` - Slate surfaces
- `"paper"` - Layered paper
- `"blueprint"` - Architectural draft
- `"ceramic"` - Matte porcelain
- `"carbon"` - Technical carbon
- `"brass-instrument"` - Brass & enamel
- `"walnut-workshop"` - Walnut inlays
- `"harbor"` - Crisp blue
- `"borealis"` - Green-teal
- `"ember"` - Warm amber
- `"orchid"` - Violet
- `"hot-pink"` - High-energy pink
- `"coral"` - Red-coral
- `"graphite"` - Neutral

## Examples

### Use Only Custom Themes

```json
{
  "Themes": {
    "ThemesDirectory": "custom-themes",
    "ThemesMode": "DirectoryOnly",
    "DefaultThemeId": "my-custom-theme"
  }
}
```

With directory structure:
```
custom-themes/
  ├── my-custom-theme.json
  └── another-theme.json
```

### Add Custom Themes to Built-Ins

```json
{
  "Themes": {
    "ThemesDirectory": "custom-themes.json",
    "ThemesMode": "AddToBuiltIns",
    "DefaultThemeId": null
  }
}
```

### Use Environment Variables

```bash
export Themes__ThemesDirectory="/opt/themes"
export Themes__ThemesMode="AddToBuiltIns"
export Themes__DefaultThemeId="my-theme"
```

Or in Docker Compose:

```yaml
environment:
  Themes__ThemesDirectory: /app/themes
  Themes__ThemesMode: AddToBuiltIns
  Themes__DefaultThemeId: dark-mode
```

## Theme File Format

Custom themes must be JSON files containing a `themes` array with theme definitions:

```json
{
  "themes": [
    {
      "id": "my-theme",
      "name": "My Custom Theme",
      "description": "A custom theme for the studio",
      "source": "custom",
      "version": 1,
      "enabled": true,
      "published": true,
      "modes": {
        "light": {
          "primary": "#0ea5e9",
          "primaryForeground": "#ffffff",
          "secondary": "#e2e8f0",
          "secondaryForeground": "#0f172a",
          "accent": "#0369a1",
          "accentForeground": "#ffffff",
          "success": "#2e7d32",
          "successForeground": "#ffffff",
          "warning": "#ed6c02",
          "warningForeground": "#ffffff",
          "danger": "#d32f2f",
          "dangerForeground": "#ffffff",
          "background": "#ffffff",
          "foreground": "#0f172a",
          "card": "#f8fafc",
          "cardForeground": "#0f172a",
          "muted": "#f1f5f9",
          "mutedForeground": "#475569",
          "border": "#cbd5e1",
          "input": "#ffffff",
          "sidebar": "#f8fafc",
          "sidebarForeground": "#0f172a",
          "sidebarActive": "#e0f2fe",
          "sidebarActiveForeground": "#0c4a6e",
          "ring": "#0284c7",
          "chartColors": ["#0ea5e9", "#6366f1", "#2e7d32", "#ed6c02", "#d32f2f"]
        },
        "dark": {
          "primary": "#0ea5e9",
          "primaryForeground": "#082f49",
          "secondary": "#334155",
          "secondaryForeground": "#f8fafc",
          "accent": "#38bdf8",
          "accentForeground": "#082f49",
          "success": "#22c55e",
          "successForeground": "#052e16",
          "warning": "#f59e0b",
          "warningForeground": "#451a03",
          "danger": "#f87171",
          "dangerForeground": "#450a0a",
          "background": "#0f172a",
          "foreground": "#f8fafc",
          "card": "#182234",
          "cardForeground": "#f8fafc",
          "muted": "#1e293b",
          "mutedForeground": "#cbd5e1",
          "border": "#475569",
          "input": "#1e293b",
          "sidebar": "#0f172a",
          "sidebarForeground": "#f8fafc",
          "sidebarActive": "#1e3a5f",
          "sidebarActiveForeground": "#e0f2fe",
          "ring": "#38bdf8",
          "chartColors": ["#38bdf8", "#818cf8", "#22c55e", "#f59e0b", "#f87171"]
        }
      }
    }
  ]
}
```

For detailed color format specifications (hex, oklch, rgb, hsl), see the Theme Builder UI in the application, or refer to the theme validation in `ElsaThemeStoreApi.cs`.

## How It Works

1. **Startup**: The `ThemeConfigurationService` is registered and initialized
2. **Configuration Load**: Binds the `Themes` section via the Configuration API (appsettings.json, ENV vars, etc.)
3. **Custom Themes Load**: Loads custom themes from the configured directory (if set)
4. **API Response**: When `/api/_elsa/theme-store` is called:
   - Custom themes from directory are loaded
   - Merged with persisted store (from `studio-theme-store.json`)
   - Configured default theme ID is included in response
5. **Frontend**: Frontend merges API response with built-in themes from `presets.ts`

## Backward Compatibility

- **No configuration**: Built-in themes only (existing behavior)
- **Persisted store**: Studio-saved custom themes still work via `studio-theme-store.json`
- **Built-in themes**: Always available, cannot be deleted (only hidden)

## Troubleshooting

### Themes Not Loading

1. Check logs for warnings about the configured path
2. Verify file exists and is readable: `ls -la /path/to/themes`
3. Validate JSON format: `jq . /path/to/themes.json`

### Default Theme Not Applied

1. Verify theme ID matches an available theme exactly (case-sensitive)
2. Check both environment variables and `appsettings.json`
3. Environment variables take precedence over the config file (standard Configuration API ordering)

### Persisted Themes Still There

Themes saved in the UI (studio-theme-store.json) are still available alongside directory themes. To replace them, use `ThemesMode: "DirectoryOnly"`.
