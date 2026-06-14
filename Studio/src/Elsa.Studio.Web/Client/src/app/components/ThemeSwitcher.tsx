import React, { useState, useRef, useEffect } from "react";
import { Moon, Sun, Palette, ChevronDown } from "lucide-react";
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
          title="Theme selector"
        >
          <Palette size={18} />
          <span className="theme-name">{currentTheme.name}</span>
          <ChevronDown size={16} className={`chevron ${isThemeOpen ? "open" : ""}`} />
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
                  onClick={() => {
                    setTheme(theme.id);
                    setIsThemeOpen(false);
                  }}
                  title={theme.description}
                >
                  <span className="theme-item-preview">
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
                    <span className="theme-item-checkmark">✓</span>
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
