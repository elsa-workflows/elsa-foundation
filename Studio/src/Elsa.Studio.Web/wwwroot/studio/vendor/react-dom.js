import { r as a } from "./chunks/index.js";
function u(t, c) {
  for (var n = 0; n < c.length; n++) {
    const r = c[n];
    if (typeof r != "string" && !Array.isArray(r)) {
      for (const o in r)
        if (o !== "default" && !(o in t)) {
          const s = Object.getOwnPropertyDescriptor(r, o);
          s && Object.defineProperty(t, o, s.get ? s : {
            enumerable: !0,
            get: () => r[o]
          });
        }
    }
  }
  return Object.freeze(Object.defineProperty(t, Symbol.toStringTag, { value: "Module" }));
}
function l(t) {
  return t && t.__esModule && Object.prototype.hasOwnProperty.call(t, "default") ? t.default : t;
}
var e = a();
const p = /* @__PURE__ */ l(e), d = /* @__PURE__ */ u({
  __proto__: null,
  default: p
}, [e]), f = e.createPortal, m = e.flushSync, b = e.preconnect, y = e.prefetchDNS, g = e.preinit, _ = e.preinitModule, h = e.preload, S = e.preloadModule, D = e.requestFormReset, O = e.unstable_batchedUpdates, j = e.useFormStatus, M = e.version;
export {
  f as createPortal,
  d as default,
  m as flushSync,
  b as preconnect,
  y as prefetchDNS,
  g as preinit,
  _ as preinitModule,
  h as preload,
  S as preloadModule,
  D as requestFormReset,
  O as unstable_batchedUpdates,
  j as useFormStatus,
  M as version
};
