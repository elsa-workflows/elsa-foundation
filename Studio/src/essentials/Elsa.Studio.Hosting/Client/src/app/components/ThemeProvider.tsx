import React, { createContext, useContext, useEffect, useState } from "react";
import { studioThemeLayoutAttribute, type StudioEndpointContext } from "../../sdk";
import type { StudioThemeDefinition, Theme, ThemeMaterialMode, ThemeMode } from "../themes/presets";
import {
  builtInThemeDefinitions,
  getSupportedThemeModes,
  getThemeColorScheme,
  getThemeModeDefinition,
  isMaterialTheme,
  resolveThemeMode,
  toTheme
} from "../themes/presets";
import {
  getThemeNavMode,
  navModeAttribute,
  resolveNavMode,
  type NavMode,
  type NavModePreference
} from "../themes/navMode";
import {
  readAppearancePreferences,
  resetAppearancePreferences,
  resolveModePreference,
  watchSystemThemeMode,
  writeAppearancePreferences,
  type ThemeModePreference
} from "../themes/appearance";
import { findSelectableTheme, getSelectableThemes, getThemeStore, normalizeThemeStore, type ThemeStoreResponse } from "../themes/themeStoreApi";

interface ThemeContextType {
  currentTheme: Theme;
  /** The mode the current theme is rendered in. */
  mode: ThemeMode;
  /** The mode the user asked for; differs from `mode` when the current theme lacks it. */
  preferredMode: ThemeMode;
  /** The stored mode choice, including `system` (follow the OS). */
  modePreference: ThemeModePreference;
  setTheme: (themeId: string) => void;
  /** Accepts a concrete mode or `system`. Same implementation as `setModePreference`. */
  setMode: (mode: ThemeModePreference) => void;
  setModePreference: (preference: ThemeModePreference) => void;
  /** Clears every stored appearance choice so the configured defaults apply again. */
  resetAppearance: () => void;
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
  const [builtInStore] = useState(normalizeThemeStore);
  // Until the store loads, a stored custom theme id cannot resolve yet, so the fallback theme shown
  // meanwhile must not overwrite it in localStorage. A store that failed to load never counts as loaded.
  const [loadedStore, setLoadedStore] = useState<ThemeStoreResponse>();
  const store = loadedStore ?? builtInStore;
  const storeLoaded = loadedStore !== undefined;
  const [currentTheme, setCurrentTheme] = useState<Theme>(builtInThemeDefinitions[0]);
  // The preferred mode survives theme switches: picking High contrast, visiting a light/dark-only
  // theme, and coming back restores High contrast rather than whatever that theme fell back to.
  const [modePreference, setModePreferenceState] = useState<ThemeModePreference>("system");
  const [systemModeVersion, setSystemModeVersion] = useState(0);
  // eslint-disable-next-line react-hooks/exhaustive-deps -- re-resolve `system` when the OS preference changes
  const preferredMode = React.useMemo(() => resolveModePreference(modePreference), [modePreference, systemModeVersion]);
  const [navModePreference, setNavModePreferenceState] = useState<NavModePreference>("theme");
  const [mounted, setMounted] = useState(false);
  const supportedModes = getSupportedThemeModes(currentTheme);
  const activeMode = resolveThemeMode(currentTheme, preferredMode);
  const navMode = resolveNavMode(currentTheme.layout, navModePreference);

  // Initialize from localStorage on mount and again whenever the store changes, falling back to the
  // OS contrast and colour-scheme preferences while the user has never chosen a mode. Once chosen,
  // the stored mode always wins. The stored theme id is only written back once the store has loaded.
  useEffect(() => {
    const saved = readAppearancePreferences();
    const nextTheme = findSelectableTheme(store, saved.themeId);

    setModePreferenceState(saved.mode);
    setNavModePreferenceState(saved.navigation);
    setCurrentTheme(nextTheme);
    if (storeLoaded && saved.themeId !== null) {
      writeAppearancePreferences({ themeId: nextTheme.id });
    }
    setMounted(true);
  }, [store, storeLoaded]);

  useEffect(() => {
    if (modePreference !== "system") return undefined;
    return watchSystemThemeMode(() => setSystemModeVersion(version => version + 1));
  }, [modePreference]);

  useEffect(() => {
    let disposed = false;

    async function loadThemes() {
      const nextStore = await loadThemeStore(storeContext);
      if (!disposed) {
        setLoadedStore(current => nextStore ?? current);
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

  }, [currentTheme, activeMode, navMode, mounted]);

  const handleSetTheme = (themeId: string) => {
    const nextTheme = findSelectableTheme(store, themeId);
    writeAppearancePreferences({ themeId: nextTheme.id });
    setCurrentTheme(nextTheme);
  };

  const handleSetModePreference = (preference: ThemeModePreference) => {
    setModePreferenceState(preference);
    writeAppearancePreferences({ mode: preference });
  };

  const handleSetNavModePreference = (preference: NavModePreference) => {
    setNavModePreferenceState(preference);
    writeAppearancePreferences({ navigation: preference });
  };

  const handleResetAppearance = () => {
    const defaults = resetAppearancePreferences();
    setModePreferenceState(defaults.mode);
    setNavModePreferenceState(defaults.navigation);
    // Not persisted: the configured default theme applies until the user picks one again.
    setCurrentTheme(findSelectableTheme(store, defaults.themeId));
  };

  const handlePreviewTheme = (theme: StudioThemeDefinition) => {
    setCurrentTheme(toTheme(theme));
  };

  const refreshThemes = async () => {
    const nextStore = await loadThemeStore(storeContext);
    setLoadedStore(current => nextStore ?? current);
  };

  return (
    <ThemeContext.Provider
      value={{
        currentTheme,
        mode: activeMode,
        preferredMode,
        setTheme: handleSetTheme,
        modePreference,
        setMode: handleSetModePreference,
        setModePreference: handleSetModePreference,
        resetAppearance: handleResetAppearance,
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

/** Fetches the theme store, or returns the built-ins without a store context. Undefined means the fetch failed. */
function loadThemeStore(storeContext: StudioEndpointContext | undefined) {
  return storeContext ? getThemeStore(storeContext) : Promise.resolve(normalizeThemeStore());
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

  // Material CSS recipes read a single `--studio-material-texture` (and
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
