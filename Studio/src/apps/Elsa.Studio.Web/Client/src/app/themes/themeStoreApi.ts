import { isStudioThemeLayout, studioThemeLayouts, type StudioEndpointContext } from "../../sdk";
import {
  allThemeModes,
  builtInThemeDefinitions,
  cloneThemeDefinition,
  getSupportedThemeModes,
  getThemeModeDefinition,
  isThemeMode,
  themeModeKeys,
  themeTokenNames,
  toTheme,
  type StudioThemeDefinition,
  type Theme,
  type ThemeMode,
  type ThemeModeDefinition,
  type ThemeShape,
  type ThemeTypography
} from "./presets";

export interface ThemeStoreResponse {
  themes: StudioThemeDefinition[];
  defaultThemeId: string;
  assets: ThemeStoreAsset[];
  /** Built-in theme ids an admin has hidden from the theme picker dropdown. */
  disabledBuiltInThemeIds: string[];
}

export interface ThemeStoreCapabilities {
  storeEnabled: boolean;
  pickerEnabled: boolean;
  managementEnabled: boolean;
}

interface ThemeFeatureCapabilityResponse {
  enabled?: boolean;
}

export interface ThemeStoreAsset {
  id: string;
  fileName: string;
  contentType: string;
  size: number;
  url: string;
}

export interface ThemeValidationIssue {
  path: string;
  message: string;
  severity: "error" | "warning";
}

export interface ThemeValidationResult {
  valid: boolean;
  issues: ThemeValidationIssue[];
}

export interface ThemePack {
  version: number;
  themes: StudioThemeDefinition[];
  assets?: ThemeStoreAsset[];
}

export const themeStoreKeys = {
  all: ["theme-store"] as const,
  registry: ["theme-store", "registry"] as const
};

export const defaultThemeStoreCapabilities: ThemeStoreCapabilities = {
  storeEnabled: false,
  pickerEnabled: false,
  managementEnabled: false
};

export function normalizeThemeStore(value?: Partial<ThemeStoreResponse> | null): ThemeStoreResponse {
  const customThemes = (value?.themes ?? [])
    .filter(theme => theme?.source === "custom")
    .map(normalizeThemeDefinition)
    .filter(Boolean) as StudioThemeDefinition[];
  const builtInIds = new Set(builtInThemeDefinitions.map(theme => theme.id));
  // Admin-disabled built-ins stay in the store (the Theme Builder still lists them for
  // re-enabling) but are marked disabled so getSelectableThemes drops them from the picker.
  const disabledBuiltInThemeIds = (value?.disabledBuiltInThemeIds ?? [])
    .filter(id => typeof id === "string")
    .map(id => id.toLowerCase())
    .filter(id => builtInIds.has(id));
  const disabledBuiltIns = new Set(disabledBuiltInThemeIds);
  const themes = [
    ...builtInThemeDefinitions.map(theme => ({
      ...cloneThemeDefinition(theme),
      enabled: !disabledBuiltIns.has(theme.id.toLowerCase())
    })),
    ...customThemes.filter(theme => !builtInIds.has(theme.id))
  ];
  const defaultThemeId = themes.some(theme => theme.id === value?.defaultThemeId)
    ? value!.defaultThemeId!
    : builtInThemeDefinitions[0].id;

  return {
    themes,
    defaultThemeId,
    assets: value?.assets ?? [],
    disabledBuiltInThemeIds
  };
}

export function getSelectableThemes(store: ThemeStoreResponse): Theme[] {
  return store.themes
    .filter(theme => theme.enabled && theme.published)
    .map(toTheme);
}

export function findSelectableTheme(store: ThemeStoreResponse, themeId: string | null | undefined): Theme {
  const selectableThemes = getSelectableThemes(store);
  return selectableThemes.find(theme => theme.id === themeId)
    ?? selectableThemes.find(theme => theme.id === store.defaultThemeId)
    ?? toTheme(builtInThemeDefinitions[0]);
}

export function validateThemeDefinition(theme: StudioThemeDefinition): ThemeValidationResult {
  const issues: ThemeValidationIssue[] = [];
  if (!theme.id || !/^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$/.test(theme.id)) {
    issues.push(error("id", "Theme ID must be kebab-case and between 3 and 64 characters."));
  }

  if (!theme.name?.trim()) {
    issues.push(error("name", "Theme name is required."));
  }

  if (theme.source !== "built-in" && theme.source !== "custom") {
    issues.push(error("source", "Theme source must be built-in or custom."));
  }

  if (theme.supportedModes !== undefined) {
    if (!Array.isArray(theme.supportedModes) || theme.supportedModes.length === 0) {
      issues.push(error("supportedModes", "Theme supported modes must include at least one mode."));
    } else {
      for (const mode of theme.supportedModes) {
        if (!isThemeMode(mode)) {
          issues.push(error("supportedModes", "Theme supported modes must be light, dark, dim or high-contrast."));
        } else if (theme.modes && !getThemeModeDefinition(theme, mode)) {
          issues.push(error("supportedModes", `Theme supports ${mode} mode but does not define modes.${themeModeKeys[mode]}.`));
        }
      }
    }
  }

  // Light and dark are required; dim and high contrast are validated only when the theme defines them.
  for (const mode of allThemeModes) {
    const key = themeModeKeys[mode];
    const definition = theme.modes?.[key];
    if (definition || mode === "light" || mode === "dark") {
      validateMode(definition, `modes.${key}`, issues);
    }
  }

  validateTypography(theme.typography, issues);
  validateShape(theme.shape, issues);
  if (theme.layout !== undefined && theme.layout !== null && !isStudioThemeLayout(theme.layout)) {
    issues.push(error("layout", `Layout must be one of ${studioThemeLayouts.join(", ")}.`));
  }

  return { valid: issues.every(issue => issue.severity !== "error"), issues };
}

export async function fetchThemeStore(context: StudioEndpointContext): Promise<ThemeStoreResponse> {
  const response = await context.http.getJson<Partial<ThemeStoreResponse>>("/_elsa/theme-store");
  return normalizeThemeStore(response);
}

/** The theme store, or undefined when it could not be fetched — distinct from a store holding only the built-ins. */
export async function getThemeStore(context: StudioEndpointContext): Promise<ThemeStoreResponse | undefined> {
  try {
    return await fetchThemeStore(context);
  } catch {
    return undefined;
  }
}

export async function getThemeStoreCapabilities(context: StudioEndpointContext): Promise<ThemeStoreCapabilities> {
  const [storeEnabled, pickerEnabled, managementEnabled] = await Promise.all([
    getThemeFeatureCapability(context, "core"),
    getThemeFeatureCapability(context, "picker"),
    getThemeFeatureCapability(context, "management")
  ]);

  return { storeEnabled, pickerEnabled, managementEnabled };
}

async function getThemeFeatureCapability(context: StudioEndpointContext, feature: "core" | "picker" | "management") {
  try {
    const response = await context.http.getJson<ThemeFeatureCapabilityResponse>(`/_elsa/theme-store/capabilities/${feature}`);
    return response.enabled === true;
  } catch {
    return false;
  }
}

export async function saveTheme(context: StudioEndpointContext, theme: StudioThemeDefinition) {
  return normalizeThemeStore(await context.http.putJson<ThemeStoreResponse>(`/_elsa/theme-store/themes/${encodeURIComponent(theme.id)}`, theme));
}

/**
 * Shows or hides a BUILT-IN theme in the theme picker. Built-in definitions remain read-only
 * seeds; this only records the admin's visibility choice. Custom themes keep using saveTheme
 * with their own enabled flag.
 */
export async function setBuiltInThemeEnabled(context: StudioEndpointContext, themeId: string, enabled: boolean) {
  return normalizeThemeStore(await context.http.putJson<ThemeStoreResponse>(`/_elsa/theme-store/themes/${encodeURIComponent(themeId)}/visibility`, { enabled }));
}

export async function duplicateTheme(context: StudioEndpointContext, themeId: string, name?: string) {
  return context.http.postJson<StudioThemeDefinition>(`/_elsa/theme-store/themes/${encodeURIComponent(themeId)}/duplicate`, { name });
}

export async function deleteTheme(context: StudioEndpointContext, themeId: string) {
  return normalizeThemeStore(await context.http.deleteJson<ThemeStoreResponse>(`/_elsa/theme-store/themes/${encodeURIComponent(themeId)}`));
}

export async function setDefaultTheme(context: StudioEndpointContext, themeId: string) {
  return normalizeThemeStore(await context.http.putJson<ThemeStoreResponse>("/_elsa/theme-store/default", { themeId }));
}

export async function uploadThemeAsset(context: StudioEndpointContext, file: File) {
  const body = new FormData();
  body.append("asset", file);
  return context.http.postForm<ThemeStoreAsset>("/_elsa/theme-store/assets", body);
}

export async function deleteThemeAsset(context: StudioEndpointContext, assetId: string) {
  return normalizeThemeStore(await context.http.deleteJson<ThemeStoreResponse>(`/_elsa/theme-store/assets/${encodeURIComponent(assetId)}`));
}

export async function importThemePack(context: StudioEndpointContext, pack: ThemePack) {
  return normalizeThemeStore(await context.http.postJson<ThemeStoreResponse>("/_elsa/theme-store/import", pack));
}

export async function exportThemePack(context: StudioEndpointContext, themeId?: string) {
  const suffix = themeId ? `?themeId=${encodeURIComponent(themeId)}` : "";
  return context.http.getJson<ThemePack>(`/_elsa/theme-store/export${suffix}`);
}

export function createCustomThemeFrom(theme: StudioThemeDefinition, id: string, name: string): StudioThemeDefinition {
  return {
    ...cloneThemeDefinition(theme),
    id,
    name,
    source: "custom",
    version: Math.max(1, theme.version || 1),
    enabled: false,
    published: false
  };
}

/** Dim and High contrast are optional: a new one is seeded from the dark palette, which is closest. */
export function withOptionalMode(theme: StudioThemeDefinition, mode: Extract<ThemeMode, "dim" | "high-contrast">): StudioThemeDefinition {
  const supported = getSupportedThemeModes(theme);
  return {
    ...theme,
    supportedModes: allThemeModes.filter(candidate => candidate === mode || supported.includes(candidate)),
    modes: { ...theme.modes, [themeModeKeys[mode]]: structuredClone(theme.modes.dark) }
  };
}

export function withoutOptionalMode(theme: StudioThemeDefinition, mode: Extract<ThemeMode, "dim" | "high-contrast">): StudioThemeDefinition {
  const modes = { ...theme.modes };
  delete modes[themeModeKeys[mode] as "dim" | "highContrast"];
  return { ...theme, modes, supportedModes: getSupportedThemeModes(theme).filter(candidate => candidate !== mode) };
}

/** Sets one typography or shape field; a blank value removes it, and an emptied section is dropped. */
export function withStyleField(theme: StudioThemeDefinition, section: "typography" | "shape", field: string, value: string): StudioThemeDefinition {
  const next: Record<string, string> = { ...(theme[section] as Record<string, string> | undefined) };
  if (value.trim()) next[field] = value;
  else delete next[field];
  return { ...theme, [section]: Object.keys(next).length > 0 ? next : undefined };
}

/**
 * Font stacks are a comma-separated list of family names; quoting is allowed, anything that could
 * break out of the declaration (`;`, braces, `url(`, escapes) is not.
 */
export function isAllowedFontStack(value: unknown): value is string {
  return typeof value === "string" && value.trim().length > 0 && value.length <= 512 && /^[a-z0-9\s,'"._-]+$/i.test(value);
}

/** Radii are a plain non-negative length in px or rem. */
export function isAllowedRadius(value: unknown): value is string {
  return typeof value === "string" && /^(0|\d{1,3}(\.\d+)?(px|rem))$/.test(value.trim());
}

function validateTypography(typography: ThemeTypography | undefined, issues: ThemeValidationIssue[]) {
  for (const [name, value] of Object.entries(typography ?? {})) {
    if (value !== undefined && !isAllowedFontStack(value)) {
      issues.push(error(`typography.${name}`, "Font stacks may only contain family names separated by commas."));
    }
  }
}

function validateShape(shape: ThemeShape | undefined, issues: ThemeValidationIssue[]) {
  for (const [name, value] of Object.entries(shape ?? {})) {
    if (value !== undefined && !isAllowedRadius(value)) {
      issues.push(error(`shape.${name}`, "Radii must be a length in px or rem, such as 6px."));
    }
  }
}

function normalizeThemeDefinition(theme: Partial<StudioThemeDefinition> | null | undefined): StudioThemeDefinition | null {
  if (!theme?.id || !theme.modes?.light || !theme.modes?.dark) {
    return null;
  }

  return {
    id: theme.id,
    name: theme.name ?? theme.id,
    description: theme.description ?? "",
    source: theme.source === "built-in" ? "built-in" : "custom",
    version: theme.version ?? 1,
    enabled: theme.enabled ?? true,
    published: theme.published ?? true,
    supportedModes: getSupportedThemeModes(theme),
    modes: {
      light: theme.modes.light,
      dark: theme.modes.dark,
      ...(theme.modes.dim ? { dim: theme.modes.dim } : {}),
      ...(theme.modes.highContrast ? { highContrast: theme.modes.highContrast } : {})
    },
    // The host serializes absent optional sections as null; keep them undefined client-side.
    typography: theme.typography ?? undefined,
    shape: theme.shape ?? undefined,
    layout: isStudioThemeLayout(theme.layout) ? theme.layout : undefined,
    material: theme.material
  };
}

function validateMode(mode: ThemeModeDefinition | undefined, path: string, issues: ThemeValidationIssue[]) {
  if (!mode) {
    issues.push(error(path, "Theme mode is required."));
    return;
  }

  for (const token of themeTokenNames) {
    const value = mode[token];
    if (!isAllowedTokenValue(value)) {
      issues.push(error(`${path}.${token}`, `${token} must be a color token, not arbitrary CSS.`));
    }
  }

  if (!Array.isArray(mode.chartColors) || mode.chartColors.length !== 5 || mode.chartColors.some(color => !isAllowedTokenValue(color))) {
    issues.push(error(`${path}.chartColors`, "Chart colors must contain five valid color tokens."));
  }

  validateContrastPair(mode.background, mode.foreground, `${path}.foreground`, issues);
  validateContrastPair(mode.card, mode.cardForeground, `${path}.cardForeground`, issues);
  validateContrastPair(mode.primary, mode.primaryForeground, `${path}.primaryForeground`, issues);
  validateMaterial(mode.material, path, issues);
}

function validateMaterial(material: ThemeModeDefinition["material"], path: string, issues: ThemeValidationIssue[]) {
  if (!material) {
    return;
  }

  if (material.textureSize !== undefined && (!Number.isFinite(material.textureSize) || material.textureSize < 32 || material.textureSize > 1024)) {
    issues.push(error(`${path}.material.textureSize`, "Texture size must be between 32 and 1024 pixels."));
  }

  for (const [name, value] of Object.entries(material.cssVariables ?? {})) {
    if (!/^--studio-material-[a-z0-9-]+$/.test(name)) {
      issues.push(error(`${path}.material.cssVariables.${name}`, "Material variables must use the --studio-material-* allowlist."));
    }
    if (!isAllowedMaterialValue(value)) {
      issues.push(error(`${path}.material.cssVariables.${name}`, "Material variable value contains disallowed CSS."));
    }
  }
}

/**
 * Deliberate grammar for the `--studio-material-*` escape hatch. Unlike the core color
 * tokens (which are color-only), material variables legitimately carry finishes/depths,
 * `<length>` pairs (`390px 390px`), color/gradient function stacks, and same-origin
 * texture `url(...)`s. We admit exactly those shapes rather than an accidental character
 * set, and reject anything that could break out of the declaration or fetch off-origin.
 *
 * Structural guards (checked before the character allowlist):
 *   - declaration/selector breakouts: `;`, `{`, `}`, `@`, `<`, `>`, and CSS `\` escapes.
 *   - unbalanced parentheses.
 *   - `url(...)` targets: only same-origin absolute (`/path`) or bundler-relative
 *     (`./`, `../`) paths — never `http:`/`https:`, protocol-relative `//`, `data:`, or
 *     any other scheme (blocks remote fetches / data-URI injection).
 *
 * The residual character allowlist then permits identifiers, numbers with units/signs,
 * function syntax, hex colors, quotes (for quoted url()s), and the separators gradients
 * and length pairs need.
 */
function isAllowedMaterialValue(value: string): value is string {
  if (typeof value !== "string") return false;
  const trimmed = value.trim();
  if (trimmed.length === 0 || trimmed.length > 1024) return false;

  // No declaration/rule breakouts or CSS escape sequences.
  if (/[;{}@<>\\]/.test(trimmed)) return false;

  // Balanced parentheses (cheap depth check; forbids stray `(`/`)`).
  let depth = 0;
  for (const char of trimmed) {
    if (char === "(") depth++;
    else if (char === ")" && --depth < 0) return false;
  }
  if (depth !== 0) return false;

  // Every url() must resolve same-origin: absolute (`/…`) or bundler-relative (`./`, `../`).
  const urlRe = /url\(\s*(['"]?)([^)'"]*)\1\s*\)/gi;
  let match: RegExpExecArray | null;
  while ((match = urlRe.exec(trimmed)) !== null) {
    const target = match[2].trim();
    if (!/^(?:\/(?!\/)|\.\.?\/)/.test(target)) return false;
  }

  // Residual character allowlist: identifiers, numbers/units, function + color syntax,
  // quotes for quoted url()s, and gradient/length separators.
  return /^[a-z0-9\s.,%#()/*+_'"=:-]+$/i.test(trimmed);
}

function validateContrastPair(background: string, foreground: string, path: string, issues: ThemeValidationIssue[]) {
  const backgroundLightness = getOklchLightness(background);
  const foregroundLightness = getOklchLightness(foreground);
  if (backgroundLightness !== null && foregroundLightness !== null && Math.abs(backgroundLightness - foregroundLightness) < 0.35) {
    issues.push({ path, message: "Foreground/background contrast may be too low.", severity: "warning" });
  }
}

function isAllowedTokenValue(value: unknown): value is string {
  return typeof value === "string"
    && (/^#[0-9a-f]{3,8}$/i.test(value)
      || /^oklch\(\s*(0?\.\d+|1(?:\.0+)?|0)\s+\d*\.?\d+\s+\d*\.?\d+\s*\)$/i.test(value)
      || /^rgb(a)?\([\d\s.,%/]+\)$/i.test(value)
      || /^hsl(a)?\([\d\s.,%/]+\)$/i.test(value)
      || /^var\(--[a-z0-9-]+\)$/i.test(value));
}

function getOklchLightness(value: string) {
  const match = value.match(/^oklch\(\s*(0?\.\d+|1(?:\.0+)?|0)\s/i);
  return match ? Number(match[1]) : null;
}

function error(path: string, message: string): ThemeValidationIssue {
  return { path, message, severity: "error" };
}
