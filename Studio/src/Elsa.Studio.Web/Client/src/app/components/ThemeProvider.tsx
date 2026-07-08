import React, { createContext, useContext, useEffect, useState } from "react";
import type { StudioEndpointContext } from "../../sdk";
import type { StudioThemeDefinition, Theme, ThemeMode } from "../themes/presets";
import { builtInThemeDefinitions, getSupportedThemeModes, isMaterialTheme, resolveThemeMode, toTheme } from "../themes/presets";
import { findSelectableTheme, getSelectableThemes, getThemeStore, normalizeThemeStore, type ThemeStoreResponse } from "../themes/themeStoreApi";

interface ThemeContextType {
  currentTheme: Theme;
  mode: ThemeMode;
  setTheme: (themeId: string) => void;
  setMode: (mode: ThemeMode) => void;
  supportedModes: ThemeMode[];
  canToggleMode: boolean;
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
  const [mode, setModeState] = useState<ThemeMode>("light");
  const [mounted, setMounted] = useState(false);
  const [persistThemeSelection, setPersistThemeSelection] = useState(true);
  const supportedModes = getSupportedThemeModes(currentTheme);
  const activeMode = resolveThemeMode(currentTheme, mode);
  const canToggleMode = supportedModes.length > 1;

  // Initialize from localStorage on mount, falling back to the OS `prefers-color-scheme` when the
  // user has never chosen a mode. Once persisted, the stored preference always wins.
  useEffect(() => {
    const savedThemeId = getStoredPreference("elsa-studio-theme");
    const savedMode = getStoredPreference("elsa-studio-theme-mode") as ThemeMode | null;
    const nextTheme = findSelectableTheme(store, savedThemeId);

    setModeState(resolveThemeMode(nextTheme, savedMode ?? getPreferredColorScheme()));
    setCurrentTheme(nextTheme);
    setPersistThemeSelection(true);
    setMounted(true);
  }, [store]);

  useEffect(() => {
    let disposed = false;

    async function loadThemes() {
      const nextStore = storeContext ? await getThemeStore(storeContext) : normalizeThemeStore();
      if (!disposed) {
        setStore(nextStore);
        setCurrentTheme(current => {
          const nextTheme = findSelectableTheme(nextStore, current.id);
          setModeState(currentMode => resolveThemeMode(nextTheme, currentMode));
          return nextTheme;
        });
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

    const colors = activeMode === "light" ? currentTheme.light : currentTheme.dark;
    const root = document.documentElement;

    clearMaterialVariables(root);
    applyMaterialVariables(root, currentTheme.material);
    Object.entries(colors).forEach(([key, value]) => {
      if (key === "chartColors") {
        (value as string[]).forEach((color, index) => {
          root.style.setProperty(`--chart-${index + 1}`, color);
        });
      } else if (key === "material") {
        applyMaterialVariables(root, value as StudioThemeDefinition["material"]);
      } else {
        // Convert camelCase to kebab-case
        const cssVarName = `--${key.replace(/([A-Z])/g, "-$1").toLowerCase()}`;
        root.style.setProperty(cssVarName, value as string);
      }
    });

    // Also set the data attribute for CSS selectors
    root.setAttribute("data-theme", currentTheme.id);
    root.setAttribute("data-theme-mode", activeMode);
    if (isMaterialTheme(currentTheme.id)) {
      root.setAttribute("data-theme-material", currentTheme.id);
    } else {
      root.removeAttribute("data-theme-material");
    }

    // Persist to localStorage
    if (persistThemeSelection) {
      setStoredPreference("elsa-studio-theme", currentTheme.id);
    }
    setStoredPreference("elsa-studio-theme-mode", activeMode);
  }, [currentTheme, activeMode, mounted, persistThemeSelection]);

  useEffect(() => {
    if (mode !== activeMode) {
      setModeState(activeMode);
    }
  }, [activeMode, mode]);

  const handleSetTheme = (themeId: string) => {
    setPersistThemeSelection(true);
    const nextTheme = findSelectableTheme(store, themeId);
    setCurrentTheme(nextTheme);
    setModeState(currentMode => resolveThemeMode(nextTheme, currentMode));
  };

  const handleSetMode = (nextMode: ThemeMode) => {
    setModeState(resolveThemeMode(currentTheme, nextMode));
  };

  const handlePreviewTheme = (theme: StudioThemeDefinition) => {
    const nextTheme = toTheme(theme);
    setPersistThemeSelection(false);
    setCurrentTheme(nextTheme);
    setModeState(currentMode => resolveThemeMode(nextTheme, currentMode));
  };

  const refreshThemes = async () => {
    const nextStore = storeContext ? await getThemeStore(storeContext) : normalizeThemeStore();
    setStore(nextStore);
    setCurrentTheme(current => {
      const nextTheme = findSelectableTheme(nextStore, current.id);
      setModeState(currentMode => resolveThemeMode(nextTheme, currentMode));
      return nextTheme;
    });
  };

  return (
    <ThemeContext.Provider
      value={{
        currentTheme,
        mode: activeMode,
        setTheme: handleSetTheme,
        setMode: handleSetMode,
        supportedModes,
        canToggleMode,
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

function applyMaterialVariables(root: HTMLElement, material: StudioThemeDefinition["material"] | undefined) {
  if (!material) return;

  for (const [name, value] of Object.entries(material.cssVariables ?? {})) {
    if (name.startsWith("--studio-material-")) {
      root.style.setProperty(name, value);
    }
  }

  const textures = Object.entries(material.textureAssets ?? {});
  for (const [name, value] of textures) {
    const variableName = name.startsWith("--studio-material-")
      ? name
      : `--studio-material-${name.replace(/([a-z0-9])([A-Z])/g, "$1-$2").replace(/[^a-z0-9-]+/gi, "-").replace(/^-+|-+$/g, "").toLowerCase()}-texture`;
    root.style.setProperty(variableName, `url("${value.replace(/"/g, '\\"')}")`);
  }

  if (textures.length > 0) {
    root.style.setProperty("--studio-material-texture-image", `url("${textures[0][1].replace(/"/g, '\\"')}")`);
  }
}

function clearMaterialVariables(root: HTMLElement) {
  for (const name of Array.from(root.style)) {
    if (name.startsWith("--studio-material-")) {
      root.style.removeProperty(name);
    }
  }
}

export function useTheme(): ThemeContextType {
  const context = useContext(ThemeContext);
  if (!context) {
    throw new Error("useTheme must be used within ThemeProvider");
  }
  return context;
}
