import React, { useState, useRef, useEffect } from "react";
import { Moon, Sun, Palette, Check } from "lucide-react";
import { useTheme } from "./ThemeProvider";
import "./ThemeSwitcher.css";

export function ThemeSwitcher() {
  const { currentTheme, mode, setTheme, setMode, availableThemes } = useTheme();
  const [isThemeOpen, setIsThemeOpen] = useState(false);
  const themeDropdownRef = useRef<HTMLDivElement>(null);

  // Close dropdown when clicking outside
  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (themeDropdownRef.current && !themeDropdownRef.current.contains(event.target as Node)) {
        setIsThemeOpen(false);
      }
    }

    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

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

      {/* Theme Selector Dropdown */}
      <div className="theme-selector-wrapper" ref={themeDropdownRef}>
        <button
          className="theme-selector-button"
          onClick={() => setIsThemeOpen(!isThemeOpen)}
          aria-label="Select theme"
          aria-expanded={isThemeOpen}
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

        {isThemeOpen && (
          <div className="theme-dropdown">
            <div className="theme-dropdown-header">
              <span className="theme-dropdown-title">Choose a theme</span>
            </div>
            <div className="theme-list">
              {availableThemes.map((theme) => (
                <button
                  key={theme.id}
                  className={`theme-item ${currentTheme.id === theme.id ? "active" : ""}`}
                  aria-label={theme.name}
                  aria-pressed={currentTheme.id === theme.id}
                  onClick={() => {
                    setTheme(theme.id);
                    setIsThemeOpen(false);
                  }}
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
                </button>
              ))}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
