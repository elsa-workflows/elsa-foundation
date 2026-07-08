import React, { createContext, useContext, useEffect, useState } from "react";
import type { StudioEndpointContext } from "../../sdk";
import type { StudioThemeDefinition, Theme, ThemeMode } from "../themes/presets";
import { builtInThemeDefinitions, toTheme } from "../themes/presets";
import { findSelectableTheme, getSelectableThemes, getThemeStore, normalizeThemeStore, type ThemeStoreResponse } from "../themes/themeStoreApi";

interface ThemeContextType {
  currentTheme: Theme;
  mode: ThemeMode;
  setTheme: (themeId: string) => void;
  setMode: (mode: ThemeMode) => void;
  previewTheme: (theme: StudioThemeDefinition) => void;
  availableThemes: Theme[];
  store: ThemeStoreResponse;
  refreshThemes: () => Promise<void>;
}

const ThemeContext = createContext<ThemeContextType | undefined>(undefined);

export function ThemeProvider({
  children,
  storeContext
}: {
  children: React.ReactNode;
  storeContext?: StudioEndpointContext;
}) {
  const [store, setStore] = useState<ThemeStoreResponse>(() => normalizeThemeStore());
  const [currentTheme, setCurrentTheme] = useState<Theme>(builtInThemeDefinitions[0]);
  const [mode, setMode] = useState<ThemeMode>("light");
  const [mounted, setMounted] = useState(false);
  const [persistThemeSelection, setPersistThemeSelection] = useState(true);

  // Initialize from localStorage on mount, falling back to the OS `prefers-color-scheme` when the
  // user has never chosen a mode. Once persisted, the stored preference always wins.
  useEffect(() => {
    const savedThemeId = getStoredPreference("elsa-studio-theme");
    const savedMode = getStoredPreference("elsa-studio-theme-mode") as ThemeMode | null;

    setMode(savedMode ?? getPreferredColorScheme());
    setCurrentTheme(findSelectableTheme(store, savedThemeId));
    setPersistThemeSelection(true);
    setMounted(true);
  }, [store]);

  useEffect(() => {
    let disposed = false;

    async function loadThemes() {
      const nextStore = storeContext ? await getThemeStore(storeContext) : normalizeThemeStore();
      if (!disposed) {
        setStore(nextStore);
        setCurrentTheme(current => findSelectableTheme(nextStore, current.id));
      }
    }

    void loadThemes();
    window.addEventListener("elsa-studio:theme-store-changed", loadThemes);
    return () => {
      disposed = true;
      window.removeEventListener("elsa-studio:theme-store-changed", loadThemes);
    };
  }, [storeContext]);

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

    // Persist to localStorage
    if (persistThemeSelection) {
      setStoredPreference("elsa-studio-theme", currentTheme.id);
    }
    setStoredPreference("elsa-studio-theme-mode", mode);
  }, [currentTheme, mode, mounted, persistThemeSelection]);

  const handleSetTheme = (themeId: string) => {
    setPersistThemeSelection(true);
    setCurrentTheme(findSelectableTheme(store, themeId));
  };

  const handlePreviewTheme = (theme: StudioThemeDefinition) => {
    setPersistThemeSelection(false);
    setCurrentTheme(toTheme(theme));
  };

  const refreshThemes = async () => {
    const nextStore = storeContext ? await getThemeStore(storeContext) : normalizeThemeStore();
    setStore(nextStore);
    setCurrentTheme(current => findSelectableTheme(nextStore, current.id));
  };

  return (
    <ThemeContext.Provider
      value={{
        currentTheme,
        mode,
        setTheme: handleSetTheme,
        setMode,
        previewTheme: handlePreviewTheme,
        availableThemes: getSelectableThemes(store),
        store,
        refreshThemes,
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

function getStoredPreference(key: string) {
  try {
    const storage = typeof window === "undefined" ? undefined : window.localStorage;
    return typeof storage?.getItem === "function" ? storage.getItem(key) : null;
  } catch {
    return null;
  }
}

function setStoredPreference(key: string, value: string) {
  try {
    const storage = typeof window === "undefined" ? undefined : window.localStorage;
    if (typeof storage?.setItem === "function") {
      storage.setItem(key, value);
    }
  } catch {
    // Local theme selection is a best-effort user preference; storage failures must not block Studio.
  }
}

export function useTheme(): ThemeContextType {
  const context = useContext(ThemeContext);
  if (!context) {
    throw new Error("useTheme must be used within ThemeProvider");
  }
  return context;
}
