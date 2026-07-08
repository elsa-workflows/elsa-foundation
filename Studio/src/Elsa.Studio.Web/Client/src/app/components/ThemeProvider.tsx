import React, { createContext, useContext, useEffect, useState } from "react";
import type { Theme } from "../themes/presets";
import { getTheme, isMaterialTheme, themes } from "../themes/presets";

type ThemeMode = "light" | "dark";

interface ThemeContextType {
  currentTheme: Theme;
  mode: ThemeMode;
  setTheme: (themeId: string) => void;
  setMode: (mode: ThemeMode) => void;
  availableThemes: Theme[];
}

const ThemeContext = createContext<ThemeContextType | undefined>(undefined);

export function ThemeProvider({ children }: { children: React.ReactNode }) {
  const [currentTheme, setCurrentTheme] = useState<Theme>(themes[0]);
  const [mode, setMode] = useState<ThemeMode>("light");
  const [mounted, setMounted] = useState(false);

  // Initialize from localStorage on mount, falling back to the OS `prefers-color-scheme` when the
  // user has never chosen a mode. Once persisted, the stored preference always wins.
  useEffect(() => {
    const savedThemeId = localStorage.getItem("elsa-studio-theme");
    const savedMode = localStorage.getItem("elsa-studio-theme-mode") as ThemeMode | null;

    if (savedThemeId) {
      const theme = getTheme(savedThemeId);
      if (theme) setCurrentTheme(theme);
    }

    setMode(savedMode ?? getPreferredColorScheme());
    setMounted(true);
  }, []);

  // Apply theme to DOM
  useEffect(() => {
    if (!mounted) return;

    const colors = mode === "light" ? currentTheme.light : currentTheme.dark;
    const root = document.documentElement;

    Object.entries(colors).forEach(([key, value]) => {
      if (key === "chartColors") {
        (value as string[]).forEach((color, index) => {
          root.style.setProperty(`--chart-${index + 1}`, color);
        });
      } else {
        // Convert camelCase to kebab-case
        const cssVarName = `--${key.replace(/([A-Z])/g, "-$1").toLowerCase()}`;
        root.style.setProperty(cssVarName, value as string);
      }
    });

    // Also set the data attribute for CSS selectors
    root.setAttribute("data-theme", currentTheme.id);
    root.setAttribute("data-theme-mode", mode);
    if (isMaterialTheme(currentTheme.id)) {
      root.setAttribute("data-theme-material", currentTheme.id);
    } else {
      root.removeAttribute("data-theme-material");
    }

    // Persist to localStorage
    localStorage.setItem("elsa-studio-theme", currentTheme.id);
    localStorage.setItem("elsa-studio-theme-mode", mode);
  }, [currentTheme, mode, mounted]);

  const handleSetTheme = (themeId: string) => {
    const theme = getTheme(themeId);
    if (theme) setCurrentTheme(theme);
  };

  return (
    <ThemeContext.Provider
      value={{
        currentTheme,
        mode,
        setTheme: handleSetTheme,
        setMode,
        availableThemes: themes,
      }}
    >
      {children}
    </ThemeContext.Provider>
  );
}

function getPreferredColorScheme(): ThemeMode {
  if (typeof window === "undefined" || typeof window.matchMedia !== "function") {
    return "light";
  }

  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

export function useTheme(): ThemeContextType {
  const context = useContext(ThemeContext);
  if (!context) {
    throw new Error("useTheme must be used within ThemeProvider");
  }
  return context;
}
