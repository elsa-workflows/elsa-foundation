import { useSyncExternalStore } from "react";

/**
 * The structural layout a Studio theme asks for. Colour, type and shape stay in the token layer;
 * layout is the one theme property that changes *arrangement*, so modules may need to react to it:
 *
 * - `classic` — docked side panels (the default, and every theme without a layout).
 * - `floating` — panels float as rounded islands over a full-bleed workspace (Drift).
 * - `workbench` — IDE-like: dense panels, squared wiring, and top menubar navigation by default (Schematic).
 * - `editorial` — calm sheet-on-desk; authoring adds steps inline rather than from a toolbox (Atelier).
 */
export type StudioThemeLayout = "classic" | "floating" | "workbench" | "editorial";

export const studioThemeLayouts: readonly StudioThemeLayout[] = ["classic", "floating", "workbench", "editorial"] as const;

/** The `<html>` attribute the host publishes the active theme's layout on. */
export const studioThemeLayoutAttribute = "data-theme-layout";

export function isStudioThemeLayout(value: unknown): value is StudioThemeLayout {
  return typeof value === "string" && (studioThemeLayouts as readonly string[]).includes(value);
}

/** Reads the active theme's layout from the host-owned `<html>` attribute; `classic` when unset. */
export function getStudioThemeLayout(): StudioThemeLayout {
  if (typeof document === "undefined") return "classic";
  const value = document.documentElement.getAttribute(studioThemeLayoutAttribute);
  return isStudioThemeLayout(value) ? value : "classic";
}

// One observer serves every subscriber: a canvas renders a hook per edge, and each must not add its own.
const listeners = new Set<() => void>();
let observer: MutationObserver | null = null;

function subscribe(listener: () => void) {
  listeners.add(listener);
  if (!observer && typeof MutationObserver !== "undefined") {
    observer = new MutationObserver(() => listeners.forEach(notify => notify()));
    observer.observe(document.documentElement, { attributes: true, attributeFilter: [studioThemeLayoutAttribute] });
  }

  return () => {
    listeners.delete(listener);
    if (listeners.size === 0) {
      observer?.disconnect();
      observer = null;
    }
  };
}

/**
 * The active theme's layout, re-rendering when the user switches theme. Prefer CSS on
 * `html[data-theme-layout="…"]` for purely visual arrangement; use this hook only where the layout
 * changes behaviour or markup (an affordance that exists in one layout and not another).
 */
export function useStudioThemeLayout(): StudioThemeLayout {
  return useSyncExternalStore(subscribe, getStudioThemeLayout, () => "classic");
}
