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
  type NavMode,
  type NavModePreference
} from "../themes/navMode";
import {
  readAppearancePreferences,
  hasStoredAppearancePreference,
  resetAppearancePreferences,
  resolveModePreference,
  watchSystemThemeMode,
  writeAppearancePreferences,
  type ThemeModePreference
} from "../themes/appearance";
import { findSelectableTheme, getSelectableThemes, getThemeStore, normalizeThemeStore, type ThemeStoreResponse } from "../themes/themeStoreApi";
import { applyWorkspaceStyle, getWorkspaceStyle, isWorkspaceStyle, type WorkspaceStyle } from "../themes/workspaceStyles";
import { minimalStudioPresets, type MinimalStudioPresetId } from "../themes/minimalPresets";
import {
  resolveNavigationColors,
  resolveSidebarColor,
  sidebarColorAttribute,
  type SidebarColorPreference
} from "../themes/sidebarColor";

interface ThemeContextType {
  currentTheme: Theme;
  currentColorScheme: Theme;
  workspaceStyle: WorkspaceStyle | null;
  setWorkspaceStyle: (style: WorkspaceStyle | null) => void;
  setColorScheme: (themeId: string | null) => void;
  /** The mode the current theme is rendered in. */
  mode: ThemeMode;
  /** The mode the user asked for; differs from `mode` when the current theme lacks it. */
  preferredMode: ThemeMode;
  /** The stored mode choice, including `system` (follow the OS). */
  modePreference: ThemeModePreference;
  setTheme: (themeId: string) => void;
  /** Explicitly applies a complete reference combination; one-axis setters keep their semantics. */
  applyPreset: (themeId: MinimalStudioPresetId) => void;
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
  /** The navigation surface's tone: `theme` follows the workspace, `light`/`dark` pin it. */
  sidebarColorPreference: SidebarColorPreference;
  setSidebarColorPreference: (preference: SidebarColorPreference) => void;
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
  const [baseTheme, setCurrentTheme] = useState<Theme>(builtInThemeDefinitions[0]);
  const [preview, setPreview] = useState<Theme>();
  const [workspaceStyle, setWorkspaceStyleState] = useState<WorkspaceStyle | null>("linen");
  const [colorSchemeId, setColorSchemeId] = useState<string | null>(null);
  const currentTheme = preview ?? baseTheme;
  const inheritedStyle = isWorkspaceStyle(currentTheme.workspaceStyle) ? currentTheme.workspaceStyle : currentTheme.id === "linen" ? "linen" : null;
  const activeStyle = preview ? inheritedStyle : workspaceStyle ?? inheritedStyle;
  const currentColorScheme = preview ?? (colorSchemeId === null ? baseTheme : findSelectableTheme(store, colorSchemeId));
  // Theme defaults retain editable layout/type/shape; only an explicit workspace override replaces them.
  const styledTheme = React.useMemo(() => applyWorkspaceStyle(currentTheme, preview ? null : workspaceStyle), [currentTheme, preview, workspaceStyle]);
  // The preferred mode survives theme switches: picking High contrast, visiting a light/dark-only
  // theme, and coming back restores High contrast rather than whatever that theme fell back to.
  const [modePreference, setModePreferenceState] = useState<ThemeModePreference>("system");
  const [systemModeVersion, setSystemModeVersion] = useState(0);
  // eslint-disable-next-line react-hooks/exhaustive-deps -- re-resolve `system` when the OS preference changes
  const preferredMode = React.useMemo(() => resolveModePreference(modePreference), [modePreference, systemModeVersion]);
  const [navModePreference, setNavModePreferenceState] = useState<NavModePreference>("theme");
  const [sidebarColorPreference, setSidebarColorPreferenceState] = useState<SidebarColorPreference>("dark");
  const [mounted, setMounted] = useState(false);
  const supportedModes = getSupportedThemeModes(currentColorScheme);
  const activeMode = resolveThemeMode(currentColorScheme, preferredMode);
  const themeNavMode = !preview && workspaceStyle ? getWorkspaceStyle(workspaceStyle).navigation : getThemeNavMode(styledTheme.layout);
  const navMode = navModePreference === "theme" ? themeNavMode : navModePreference;

  // Initialize from localStorage on mount and again whenever the store changes, falling back to the
  // OS contrast and colour-scheme preferences while the user has never chosen a mode. Once chosen,
  // the stored mode always wins. The stored theme id is only written back once the store has loaded.
  useEffect(() => {
    const saved = readAppearancePreferences();
    const nextTheme = findSelectableTheme(store, saved.themeId);

    setModePreferenceState(saved.mode);
    setNavModePreferenceState(saved.navigation);
    setSidebarColorPreferenceState(saved.sidebarColor);
    setCurrentTheme(nextTheme);
    setWorkspaceStyleState(saved.workspaceStyle);
    // A definitive store response can remove a palette. Clear that override so the base
    // theme supplies colors again; an unresolved/offline store must retain the saved id.
    const nextColorSchemeId = storeLoaded && saved.colorSchemeId !== null &&
      !getSelectableThemes(store).some(theme => theme.id === saved.colorSchemeId) ? null : saved.colorSchemeId;
    setColorSchemeId(nextColorSchemeId);
    if (storeLoaded && saved.themeId !== null) {
      writeAppearancePreferences({ themeId: nextTheme.id });
    }
    if (storeLoaded && saved.colorSchemeId !== null && hasStoredAppearancePreference("colorSchemeId")) {
      writeAppearancePreferences({ colorSchemeId: nextColorSchemeId });
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

    const colors = getThemeModeDefinition(currentColorScheme, activeMode) ?? currentColorScheme.dark;
    const root = document.documentElement;

    clearMaterialVariables(root);
    if (activeStyle === null) {
      applyMaterialVariables(root, currentTheme.material);
      applyMaterialVariables(root, getThemeModeDefinition(currentTheme, activeMode)?.material);
    }
    applyThemeStyleVariables(root, styledTheme);
    Object.entries(colors).forEach(([key, value]) => {
      if (key === "chartColors") {
        (value as string[]).forEach((color, index) => {
          root.style.setProperty(`--chart-${index + 1}`, color);
        });
      } else if (key === "material") {
        // Material is presentation owned by the base preset/style, never by a color override.
        return;
      } else {
        // Convert camelCase to kebab-case
        const cssVarName = `--${key.replace(/([A-Z])/g, "-$1").toLowerCase()}`;
        root.style.setProperty(cssVarName, value as string);
      }
    });

    // Navigation is a separate surface: the user may pin it light or dark, else it follows the workspace.
    const navigationColors = resolveNavigationColors(currentColorScheme, activeMode, activeStyle, sidebarColorPreference);
    for (const [role, value] of Object.entries({
      bg: navigationColors.bg, text: navigationColors.text,
      active: navigationColors.active, "active-text": navigationColors.activeText,
      muted: navigationColors.text
    })) root.style.setProperty(`--studio-nav-${role}`, value);
    const sidebarColor = resolveSidebarColor(sidebarColorPreference, activeMode);
    if (sidebarColor) root.setAttribute(sidebarColorAttribute, sidebarColor);
    else root.removeAttribute(sidebarColorAttribute);

    // `data-theme-mode` stays the light/dark colour scheme — the contract module CSS keys on — and
    // `data-theme-appearance` carries the exact mode (light, dark, dim, high-contrast).
    root.setAttribute("data-theme", currentTheme.id);
    root.setAttribute("data-color-scheme", currentColorScheme.id);
    if (activeStyle) root.setAttribute("data-workspace-style", activeStyle);
    else root.removeAttribute("data-workspace-style");
    root.setAttribute("data-theme-mode", getThemeColorScheme(activeMode));
    root.setAttribute("data-theme-appearance", activeMode);
    root.setAttribute(studioThemeLayoutAttribute, styledTheme.layout ?? "classic");
    root.setAttribute(navModeAttribute, navMode);
    if (activeStyle === null && isMaterialTheme(currentTheme.id)) {
      root.setAttribute("data-theme-material", currentTheme.id);
    } else {
      root.removeAttribute("data-theme-material");
    }

  }, [currentTheme, currentColorScheme, styledTheme, activeStyle, activeMode, navMode, sidebarColorPreference, mounted]);

  const handleSetTheme = (themeId: string) => {
    const nextTheme = findSelectableTheme(store, themeId);
    writeAppearancePreferences({ themeId: nextTheme.id, workspaceStyle: null, colorSchemeId: null });
    setPreview(undefined);
    setWorkspaceStyleState(null);
    setColorSchemeId(null);
    setCurrentTheme(nextTheme);
  };

  const handleApplyPreset = (themeId: MinimalStudioPresetId) => {
    const preset = minimalStudioPresets.find(candidate => candidate.id === themeId);
    const nextTheme = getSelectableThemes(store).find(candidate => candidate.id === themeId);
    if (!preset || !nextTheme) return;
    writeAppearancePreferences({ themeId, workspaceStyle: "minimal", colorSchemeId: null, mode: preset.mode });
    setPreview(undefined);
    setWorkspaceStyleState("minimal");
    setColorSchemeId(null);
    setCurrentTheme(nextTheme);
    setModePreferenceState(preset.mode);
  };

  const handleSetWorkspaceStyle = (style: WorkspaceStyle | null) => {
    setPreview(undefined);
    setWorkspaceStyleState(style);
    writeAppearancePreferences({ workspaceStyle: style });
  };

  const handleSetColorScheme = (themeId: string | null) => {
    const id = themeId === null ? null : findSelectableTheme(store, themeId).id;
    setPreview(undefined);
    setColorSchemeId(id);
    writeAppearancePreferences({ colorSchemeId: id });
  };

  const handleSetModePreference = (preference: ThemeModePreference) => {
    setModePreferenceState(preference);
    writeAppearancePreferences({ mode: preference });
  };

  const handleSetNavModePreference = (preference: NavModePreference) => {
    setNavModePreferenceState(preference);
    writeAppearancePreferences({ navigation: preference });
  };

  const handleSetSidebarColorPreference = (preference: SidebarColorPreference) => {
    setSidebarColorPreferenceState(preference);
    writeAppearancePreferences({ sidebarColor: preference });
  };

  const handleResetAppearance = () => {
    const defaults = resetAppearancePreferences();
    setModePreferenceState(defaults.mode);
    setNavModePreferenceState(defaults.navigation);
    setSidebarColorPreferenceState(defaults.sidebarColor);
    setPreview(undefined);
    setWorkspaceStyleState(defaults.workspaceStyle);
    setColorSchemeId(defaults.colorSchemeId);
    // Not persisted: the configured default theme applies until the user picks one again.
    setCurrentTheme(findSelectableTheme(store, defaults.themeId));
  };

  const handlePreviewTheme = (theme: StudioThemeDefinition) => {
    setPreview(toTheme(theme));
  };

  const refreshThemes = async () => {
    const nextStore = await loadThemeStore(storeContext);
    setLoadedStore(current => nextStore ?? current);
  };

  return (
    <ThemeContext.Provider
      value={{
        currentTheme,
        currentColorScheme,
        workspaceStyle: activeStyle,
        setWorkspaceStyle: handleSetWorkspaceStyle,
        setColorScheme: handleSetColorScheme,
        mode: activeMode,
        preferredMode,
        setTheme: handleSetTheme,
        applyPreset: handleApplyPreset,
        modePreference,
        setMode: handleSetModePreference,
        setModePreference: handleSetModePreference,
        resetAppearance: handleResetAppearance,
        supportedModes,
        navMode,
        navModePreference,
        themeNavMode,
        setNavModePreference: handleSetNavModePreference,
        sidebarColorPreference,
        setSidebarColorPreference: handleSetSidebarColorPreference,
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

/** Optional shell integration; standalone hosts retain their existing arrangement. */
export function useWorkspaceStyle(): WorkspaceStyle | null {
  return useContext(ThemeContext)?.workspaceStyle ?? null;
}

export function useTheme(): ThemeContextType {
  const context = useContext(ThemeContext);
  if (!context) {
    throw new Error("useTheme must be used within ThemeProvider");
  }
  return context;
}
