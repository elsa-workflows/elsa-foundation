import React from "react";
import * as DropdownMenu from "@radix-ui/react-dropdown-menu";
import { Check, Contrast, Moon, Palette, Sun, SunDim, type LucideIcon } from "lucide-react";
import { useTheme } from "./ThemeProvider";
import { allThemeModes, getSupportedThemeModes, getThemeModeDefinition, isThemeMode, themeModeLabels, type ThemeMode } from "../themes/presets";
import "./ThemeSwitcher.css";

const modeIcons: Record<ThemeMode, LucideIcon> = {
  light: Sun,
  dark: Moon,
  dim: SunDim,
  "high-contrast": Contrast
};

export function ThemeSwitcher() {
  const { currentTheme, mode, setTheme, setMode, availableThemes, supportedModes, canToggleMode } = useTheme();
  // The quick toggle flips between the two base modes; Dim and High contrast live in the menu.
  const toggleTarget: ThemeMode = mode === "light" ? "dark" : "light";
  const toggleAvailable = canToggleMode && supportedModes.includes(toggleTarget);
  const modeToggleTitle = toggleAvailable
    ? `${themeModeLabels[toggleTarget]} mode`
    : `${currentTheme.name} supports ${themeModeLabels[mode].toLowerCase()} mode only`;
  const ToggleIcon = toggleTarget === "dark" ? Moon : Sun;

  return (
    <div className="theme-switcher-container">
      <button
        type="button"
        className="theme-toggle-button"
        onClick={() => toggleAvailable && setMode(toggleTarget)}
        disabled={!toggleAvailable}
        aria-label={toggleAvailable ? `Switch to ${themeModeLabels[toggleTarget].toLowerCase()} mode` : modeToggleTitle}
        title={modeToggleTitle}
      >
        <ToggleIcon size={18} />
      </button>

      {/* Theme Selector Dropdown (Radix: keyboard nav, Esc, focus trap/restore, ARIA menu) */}
      <DropdownMenu.Root>
        <DropdownMenu.Trigger asChild>
          <button
            type="button"
            className="theme-selector-button"
            aria-label="Select theme"
            title={`Theme: ${currentTheme.name} · ${themeModeLabels[mode]}`}
          >
            <Palette size={18} />
            <span className="theme-selector-swatch" aria-hidden="true">
              {supportedModes.map(themeMode => (
                <span
                  key={themeMode}
                  className="theme-selector-swatch-dot"
                  style={{ backgroundColor: getThemeModeDefinition(currentTheme, themeMode)?.primary }}
                />
              ))}
            </span>
          </button>
        </DropdownMenu.Trigger>

        <DropdownMenu.Portal>
          <DropdownMenu.Content className="theme-dropdown" align="end" sideOffset={8}>
            <DropdownMenu.Label className="theme-dropdown-header">
              <span className="theme-dropdown-title">Appearance</span>
            </DropdownMenu.Label>
            <DropdownMenu.RadioGroup
              className="theme-mode-group"
              value={mode}
              onValueChange={value => isThemeMode(value) && setMode(value)}
            >
              {allThemeModes.map(themeMode => {
                const Icon = modeIcons[themeMode];
                const available = supportedModes.includes(themeMode);
                return (
                  <DropdownMenu.RadioItem
                    key={themeMode}
                    value={themeMode}
                    className="theme-mode-item"
                    disabled={!available}
                    title={available ? themeModeLabels[themeMode] : `${currentTheme.name} has no ${themeModeLabels[themeMode].toLowerCase()} mode`}
                    // Keep the menu open so modes can be compared in place.
                    onSelect={event => event.preventDefault()}
                  >
                    <Icon size={16} aria-hidden="true" />
                    <span>{themeModeLabels[themeMode]}</span>
                  </DropdownMenu.RadioItem>
                );
              })}
            </DropdownMenu.RadioGroup>

            <DropdownMenu.Label className="theme-dropdown-header">
              <span className="theme-dropdown-title">Theme</span>
            </DropdownMenu.Label>
            <div className="theme-list">
              {availableThemes.map(theme => (
                <DropdownMenu.Item
                  key={theme.id}
                  className={`theme-item ${currentTheme.id === theme.id ? "active" : ""}`}
                  aria-label={theme.name}
                  onSelect={() => setTheme(theme.id)}
                  title={theme.description}
                >
                  <span className="theme-item-preview" aria-hidden="true">
                    {getSupportedThemeModes(theme).map(themeMode => (
                      <span
                        key={themeMode}
                        className={`theme-color-dot ${themeMode}`}
                        style={{ backgroundColor: getThemeModeDefinition(theme, themeMode)?.primary }}
                      />
                    ))}
                  </span>
                  <span className="theme-item-name">{theme.name}</span>
                  {currentTheme.id === theme.id && (
                    <Check size={16} className="theme-item-checkmark" aria-hidden="true" />
                  )}
                </DropdownMenu.Item>
              ))}
            </div>
          </DropdownMenu.Content>
        </DropdownMenu.Portal>
      </DropdownMenu.Root>
    </div>
  );
}
