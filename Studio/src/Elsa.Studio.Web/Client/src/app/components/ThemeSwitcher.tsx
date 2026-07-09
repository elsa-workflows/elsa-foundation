import React from "react";
import * as DropdownMenu from "@radix-ui/react-dropdown-menu";
import { Moon, Sun, Palette, Check } from "lucide-react";
import { useTheme } from "./ThemeProvider";
import { getSupportedThemeModes } from "../themes/presets";
import "./ThemeSwitcher.css";

export function ThemeSwitcher() {
  const { currentTheme, mode, setTheme, setMode, availableThemes, supportedModes, canToggleMode } = useTheme();
  const modeToggleTitle = canToggleMode
    ? `${mode === "light" ? "Dark" : "Light"} mode`
    : `${currentTheme.name} supports ${mode} mode only`;

  const toggleMode = () => {
    if (!canToggleMode) return;
    setMode(mode === "light" ? "dark" : "light");
  };

  return (
    <div className="theme-switcher-container">
      {/* Light/Dark Mode Toggle */}
      <button
        className="theme-toggle-button"
        onClick={toggleMode}
        disabled={!canToggleMode}
        aria-label={canToggleMode ? `Switch to ${mode === "light" ? "dark" : "light"} mode` : modeToggleTitle}
        title={modeToggleTitle}
      >
        {!canToggleMode || mode === "dark" ? <Sun size={18} /> : <Moon size={18} />}
      </button>

      {/* Theme Selector Dropdown (Radix: keyboard nav, Esc, focus trap/restore, ARIA menu) */}
      <DropdownMenu.Root>
        <DropdownMenu.Trigger asChild>
          <button
            className="theme-selector-button"
            aria-label="Select theme"
            title={`Theme: ${currentTheme.name}`}
          >
            <Palette size={18} />
            <span className="theme-selector-swatch" aria-hidden="true">
              {supportedModes.map((themeMode) => (
                <span
                  key={themeMode}
                  className="theme-selector-swatch-dot"
                  style={{ backgroundColor: currentTheme[themeMode].primary }}
                />
              ))}
            </span>
          </button>
        </DropdownMenu.Trigger>

        <DropdownMenu.Portal>
          <DropdownMenu.Content
            className="theme-dropdown"
            align="end"
            sideOffset={8}
          >
            <div className="theme-dropdown-header">
              <span className="theme-dropdown-title">Choose a theme</span>
            </div>
            <div className="theme-list">
              {availableThemes.map((theme) => (
                <DropdownMenu.Item
                  key={theme.id}
                  className={`theme-item ${currentTheme.id === theme.id ? "active" : ""}`}
                  aria-label={theme.name}
                  onSelect={() => setTheme(theme.id)}
                  title={theme.description}
                >
                  <span className="theme-item-preview" aria-hidden="true">
                    {getSupportedThemeModes(theme).map((themeMode) => (
                      <span
                        key={themeMode}
                        className={`theme-color-dot ${themeMode}`}
                        style={{ backgroundColor: theme[themeMode].primary }}
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
