import React, { createContext, useContext, useEffect, useState } from "react";
import { studioThemeLayoutAttribute, type StudioEndpointContext } from "../../sdk";
import type { StudioThemeDefinition, Theme, ThemeMaterialMode, ThemeMode } from "../themes/presets";
import {
  builtInThemeDefinitions,
  getSupportedThemeModes,
  getThemeColorScheme,
  getThemeModeDefinition,
  isMaterialTheme,
  isThemeMode,
  resolveThemeMode,
  toTheme
} from "../themes/presets";
import {
  getThemeNavMode,
  isNavModePreference,
  navModeAttribute,
  navModeStorageKey,
  resolveNavMode,
  type NavMode,
  type NavModePreference
} from "../themes/navMode";
import { findSelectableTheme, getSelectableThemes, getThemeStore, normalizeThemeStore, type ThemeStoreResponse } from "../themes/themeStoreApi";

interface ThemeContextType {
  currentTheme: Theme;
  /** The mode the current theme is rendered in. */
  mode: ThemeMode;
  /** The mode the user asked for; differs from `mode` when the current theme lacks it. */
  preferredMode: ThemeMode;
  setTheme: (themeId: string) => void;
  setMode: (mode: ThemeMode) => void;
  supportedModes: ThemeMode[];
  /** Where the main navigation is rendered: the user's override, else the theme's own default. */
  navMode: NavMode;
  /** What the user picked; `theme` means "follow the current theme". */
  navModePreference: NavModePreference;
  /** The mode the current theme asks for, i.e. what `theme` resolves to. */
  themeNavMode: NavMode;
  setNavModePreference: (preference: NavModePreference) => void;
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
  // The preferred mode survives theme switches: picking High contrast, visiting a light/dark-only
  // theme, and coming back restores High contrast rather than whatever that theme fell back to.
  const [preferredMode, setPreferredMode] = useState<ThemeMode>("light");
  const [navModePreference, setNavModePreferenceState] = useState<NavModePreference>("theme");
  const [mounted, setMounted] = useState(false);
  const [persistThemeSelection, setPersistThemeSelection] = useState(true);
  const supportedModes = getSupportedThemeModes(currentTheme);
  const activeMode = resolveThemeMode(currentTheme, preferredMode);
  const navMode = resolveNavMode(currentTheme.layout, navModePreference);

  // Initialize from localStorage on mount, falling back to the OS contrast and colour-scheme
  // preferences while the user has never chosen a mode. Once chosen, the stored mode always wins.
  useEffect(() => {
    const savedThemeId = getStoredPreference("elsa-studio-theme");
    const savedMode = getStoredPreference("elsa-studio-theme-mode");
    const savedNavMode = getStoredPreference(navModeStorageKey);
    const nextTheme = findSelectableTheme(store, savedThemeId);

    setPreferredMode(isThemeMode(savedMode) ? savedMode : getSystemPreferredMode());
    setNavModePreferenceState(isNavModePreference(savedNavMode) ? savedNavMode : "theme");
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

    const colors = getThemeModeDefinition(currentTheme, activeMode) ?? currentTheme.dark;
    const root = document.documentElement;

    clearMaterialVariables(root);
    applyMaterialVariables(root, currentTheme.material);
    applyThemeStyleVariables(root, currentTheme);
    Object.entries(colors).forEach(([key, value]) => {
      if (key === "chartColors") {
        (value as string[]).forEach((color, index) => {
          root.style.setProperty(`--chart-${index + 1}`, color);
        });
      } else if (key === "material") {
        applyMaterialVariables(root, value as ThemeMaterialMode);
      } else {
        // Convert camelCase to kebab-case
        const cssVarName = `--${key.replace(/([A-Z])/g, "-$1").toLowerCase()}`;
        root.style.setProperty(cssVarName, value as string);
      }
    });

    // `data-theme-mode` stays the light/dark colour scheme — the contract module CSS keys on — and
    // `data-theme-appearance` carries the exact mode (light, dark, dim, high-contrast).
    root.setAttribute("data-theme", currentTheme.id);
    root.setAttribute("data-theme-mode", getThemeColorScheme(activeMode));
    root.setAttribute("data-theme-appearance", activeMode);
    root.setAttribute(studioThemeLayoutAttribute, currentTheme.layout ?? "classic");
    root.setAttribute(navModeAttribute, navMode);
    if (isMaterialTheme(currentTheme.id)) {
      root.setAttribute("data-theme-material", currentTheme.id);
    } else {
      root.removeAttribute("data-theme-material");
    }

    if (persistThemeSelection) {
      setStoredPreference("elsa-studio-theme", currentTheme.id);
    }
  }, [currentTheme, activeMode, navMode, mounted, persistThemeSelection]);

  const handleSetTheme = (themeId: string) => {
    setPersistThemeSelection(true);
    setCurrentTheme(findSelectableTheme(store, themeId));
  };

  const handleSetMode = (nextMode: ThemeMode) => {
    setPreferredMode(nextMode);
    setStoredPreference("elsa-studio-theme-mode", nextMode);
  };

  const handleSetNavModePreference = (preference: NavModePreference) => {
    setNavModePreferenceState(preference);
    setStoredPreference(navModeStorageKey, preference);
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
        mode: activeMode,
        preferredMode,
        setTheme: handleSetTheme,
        setMode: handleSetMode,
        supportedModes,
        navMode,
        navModePreference,
        themeNavMode: getThemeNavMode(currentTheme.layout),
        setNavModePreference: handleSetNavModePreference,
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

function getSystemPreferredMode(): ThemeMode {
  if (typeof window === "undefined" || typeof window.matchMedia !== "function") {
    return "light";
  }

  if (window.matchMedia("(prefers-contrast: more)").matches) return "high-contrast";
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

export function applyMaterialVariables(root: HTMLElement, material: ThemeMaterialMode | undefined) {
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
    root.style.setProperty(variableName, cssUrl(value));
  }

  // The material CSS recipes (tokens.css) read a single `--studio-material-texture` (and
  // `--studio-material-texture-size`) for every surface layer. Drive them from the theme's
  // primary texture asset (by convention the `surface` key, else the first asset) so a
  // preset- or store-supplied texture actually renders instead of only populating the
  // unread per-name `--studio-material-<name>-texture` aliases above.
  const primaryTexture = material.textureAssets?.surface ?? textures[0]?.[1];
  if (primaryTexture) {
    root.style.setProperty("--studio-material-texture", cssUrl(primaryTexture));
  }

  if (typeof material.textureSize === "number" && Number.isFinite(material.textureSize)) {
    const size = `${material.textureSize}px`;
    root.style.setProperty("--studio-material-texture-size", `${size} ${size}`);
  }
}

/**
 * Typography and shape primitives a theme may override. Each is cleared before the next theme is
 * applied, so a theme without them falls back to the stylesheet defaults (styles.css / tokens.css).
 */
const themeStyleVariables = {
  "--font-sans": (theme: Theme) => theme.typography?.sans,
  "--font-mono": (theme: Theme) => theme.typography?.mono,
  "--font-display": (theme: Theme) => theme.typography?.display,
  "--radius-sm": (theme: Theme) => theme.shape?.radiusSm,
  "--radius": (theme: Theme) => theme.shape?.radius,
  "--radius-md": (theme: Theme) => theme.shape?.radiusMd,
  "--radius-lg": (theme: Theme) => theme.shape?.radiusLg,
  "--radius-xl": (theme: Theme) => theme.shape?.radiusXl
} satisfies Record<string, (theme: Theme) => string | undefined>;

export function applyThemeStyleVariables(root: HTMLElement, theme: Theme) {
  for (const [name, read] of Object.entries(themeStyleVariables)) {
    const value = read(theme);
    if (value) {
      root.style.setProperty(name, value);
    } else {
      root.style.removeProperty(name);
    }
  }
}

/** Escape a texture asset URL for safe embedding in a CSS `url("…")` token. */
function cssUrl(value: string): string {
  return `url("${value.replace(/["\\]/g, "\\$&")}")`;
}

function clearMaterialVariables(root: HTMLElement) {
  for (const name of Array.from(root.style)) {
    if (name.startsWith("--studio-material-")) {
      root.style.removeProperty(name);
    }
  }
}

/** The resolved navigation mode; `left` outside a ThemeProvider, where nothing publishes a preference. */
export function useNavMode(): NavMode {
  return useContext(ThemeContext)?.navMode ?? "left";
}

export function useTheme(): ThemeContextType {
  const context = useContext(ThemeContext);
  if (!context) {
    throw new Error("useTheme must be used within ThemeProvider");
  }
  return context;
}
