import React from "react";
import * as DropdownMenu from "@radix-ui/react-dropdown-menu";
import { Moon, Sun, Palette, Check } from "lucide-react";
import { useTheme } from "./ThemeProvider";
import "./ThemeSwitcher.css";

export function ThemeSwitcher() {
  const { currentTheme, mode, setTheme, setMode, availableThemes } = useTheme();

  const toggleMode = () => {
    setMode(mode === "light" ? "dark" : "light");
  };

  return (
    <div className="theme-switcher-container">
      {/* Light/Dark Mode Toggle */}
      <button
        className="theme-toggle-button"
        onClick={toggleMode}
        aria-label={`Switch to ${mode === "light" ? "dark" : "light"} mode`}
        title={`${mode === "light" ? "Dark" : "Light"} mode`}
      >
        {mode === "light" ? <Moon size={18} /> : <Sun size={18} />}
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
              <span
                className="theme-selector-swatch-dot"
                style={{ backgroundColor: currentTheme.light.primary }}
              />
              <span
                className="theme-selector-swatch-dot"
                style={{ backgroundColor: currentTheme.dark.primary }}
              />
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
                    <span
                      className="theme-color-dot light"
                      style={{ backgroundColor: theme.light.primary }}
                    />
                    <span
                      className="theme-color-dot dark"
                      style={{ backgroundColor: theme.dark.primary }}
                    />
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
