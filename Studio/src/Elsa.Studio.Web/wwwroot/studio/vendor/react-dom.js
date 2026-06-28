import { g as a } from "./chunks/_commonjsHelpers.js";
import { r as p } from "./chunks/index3.js";
function u(o, c) {
  for (var n = 0; n < c.length; n++) {
    const t = c[n];
    if (typeof t != "string" && !Array.isArray(t)) {
      for (const r in t)
        if (r !== "default" && !(r in o)) {
          const s = Object.getOwnPropertyDescriptor(t, r);
          s && Object.defineProperty(o, r, s.get ? s : {
            enumerable: !0,
            get: () => t[r]
          });
        }
    }
  }
  return Object.freeze(Object.defineProperty(o, Symbol.toStringTag, { value: "Module" }));
}
var e = p();
const l = /* @__PURE__ */ a(e), f = /* @__PURE__ */ u({
  __proto__: null,
  default: l
}, [e]), m = e.createPortal, b = e.flushSync, g = e.preconnect, y = e.prefetchDNS, S = e.preinit, h = e.preinitModule, D = e.preload, _ = e.preloadModule, j = e.requestFormReset, v = e.unstable_batchedUpdates, F = e.useFormStatus, M = e.version;
export {
  m as createPortal,
  f as default,
  b as flushSync,
  g as preconnect,
  y as prefetchDNS,
  S as preinit,
  h as preinitModule,
  D as preload,
  _ as preloadModule,
  j as requestFormReset,
  v as unstable_batchedUpdates,
  F as useFormStatus,
  M as version
};
