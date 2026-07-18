/**
 * Node 22+ ships a global `localStorage` that is a non-functional stub unless
 * Node is started with `--localstorage-file`. Vitest's jsdom environment skips
 * copying jsdom's real Storage onto the test global for keys already present
 * on Node's global, so tests would otherwise see the broken stub. Bridge the
 * real jsdom Storage objects back (vitest exposes the JSDOM instance as
 * `globalThis.jsdom` in the jsdom environment). Reading Node's own
 * `globalThis.localStorage` getter would emit a `--localstorage-file` warning,
 * so overwrite without inspecting the current value.
 */
const dom = (globalThis as { jsdom?: { window: Window } }).jsdom;
if (dom?.window) {
  for (const key of ["localStorage", "sessionStorage"] as const) {
    Object.defineProperty(globalThis, key, {
      value: dom.window[key],
      writable: true,
      configurable: true
    });
  }
}
