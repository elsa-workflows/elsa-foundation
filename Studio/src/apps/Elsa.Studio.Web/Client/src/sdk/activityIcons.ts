import type { ComponentType, SVGProps } from "react";

/** Props a registered extension activity icon should accept. Use `currentColor` so tone tokens apply. */
export type StudioActivityIconComponent = ComponentType<SVGProps<SVGSVGElement> & { size?: number }>;

const registeredActivityIcons = new Map<string, StudioActivityIconComponent>();

function normalizeExtensionIconName(name: string): string | null {
  const trimmed = name.trim();
  if (!trimmed) return null;
  const bare = trimmed.startsWith("ext:") ? trimmed.slice(4) : trimmed;
  if (!/^[A-Za-z][A-Za-z0-9_-]*$/.test(bare)) return null;
  return bare.toLowerCase();
}

/**
 * Registers React icon components for catalog values of the form `ext:<name>`.
 * Call from an extension module's `register()` so the designer palette and canvas
 * can resolve `[Activity(Icon = "ext:name")]`. Passing `null` unregisters that name.
 */
export function registerActivityIcons(icons: Record<string, StudioActivityIconComponent | null | undefined>): void {
  for (const [name, component] of Object.entries(icons)) {
    const key = normalizeExtensionIconName(name);
    if (!key) continue;
    if (component) registeredActivityIcons.set(key, component);
    else registeredActivityIcons.delete(key);
  }
}

/** Looks up a component registered by `registerActivityIcons`. Accepts `name` or `ext:name`. */
export function getRegisteredActivityIcon(name: string): StudioActivityIconComponent | undefined {
  const key = normalizeExtensionIconName(name);
  return key ? registeredActivityIcons.get(key) : undefined;
}
