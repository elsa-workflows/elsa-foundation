import type { StudioEndpointContext } from "../../sdk";
import {
  builtInThemeDefinitions,
  cloneThemeDefinition,
  themeTokenNames,
  toTheme,
  type StudioThemeDefinition,
  type Theme,
  type ThemeMode,
  type ThemeModeDefinition
} from "./presets";

export interface ThemeStoreResponse {
  themes: StudioThemeDefinition[];
  defaultThemeId: string;
  assets: ThemeStoreAsset[];
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
  const themes = [
    ...builtInThemeDefinitions.map(theme => cloneThemeDefinition(theme)),
    ...customThemes.filter(theme => !builtInIds.has(theme.id))
  ];
  const defaultThemeId = themes.some(theme => theme.id === value?.defaultThemeId)
    ? value!.defaultThemeId!
    : builtInThemeDefinitions[0].id;

  return {
    themes,
    defaultThemeId,
    assets: value?.assets ?? []
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

  validateMode(theme.modes?.light, "modes.light", issues);
  validateMode(theme.modes?.dark, "modes.dark", issues);

  return { valid: issues.every(issue => issue.severity !== "error"), issues };
}

export async function fetchThemeStore(context: StudioEndpointContext): Promise<ThemeStoreResponse> {
  const response = await context.http.getJson<Partial<ThemeStoreResponse>>("/_elsa/theme-store");
  return normalizeThemeStore(response);
}

export async function getThemeStore(context: StudioEndpointContext): Promise<ThemeStoreResponse> {
  try {
    return await fetchThemeStore(context);
  } catch {
    return normalizeThemeStore();
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
    modes: {
      light: theme.modes.light,
      dark: theme.modes.dark
    },
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
    if (!/^[a-z0-9 .,%#()/+-]+$/i.test(value)) {
      issues.push(error(`${path}.material.cssVariables.${name}`, "Material variable values must be simple tokens."));
    }
  }
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
