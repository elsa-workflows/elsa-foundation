import { r as u } from "../vendor/chunks/index.js";
const b = {
  status: "anonymous",
  roles: [],
  permissions: []
}, L = {
  status: "unknown",
  roles: [],
  permissions: []
};
function N(t) {
  return new B(t);
}
class B {
  constructor(e) {
    this.options = e;
    for (const r of e.adapters ?? []) {
      if (this.adapters.has(r.id))
        throw new y(`Duplicate auth provider adapter '${r.id}'.`);
      this.adapters.set(r.id, r);
    }
  }
  options;
  adapters = /* @__PURE__ */ new Map();
  activeAdapter = null;
  pendingLoginProviderId = null;
  session = L;
  getSession() {
    return this.session;
  }
  getCapabilities() {
    return this.options.capabilities();
  }
  async initialize() {
    if (this.options.isCallback?.()) {
      const r = this.getCallbackProviderId(), n = r ? await this.getProviderAdapter(r) : await this.resolveActiveAdapter();
      return await this.applySession(await n.handleCallback(), n), this.pendingLoginProviderId = null, this.session;
    }
    const e = await this.resolveActiveAdapter();
    return await this.applySession(await e.initialize(), e), this.pendingLoginProviderId = null, this.session;
  }
  async login(e) {
    const r = e?.providerId ? await this.getProviderAdapter(e.providerId) : await this.resolveActiveAdapter();
    this.pendingLoginProviderId = r.id;
    try {
      const n = await r.login({ ...e, providerId: r.id });
      n ? (await this.applySession(n, r), this.pendingLoginProviderId = null) : this.session.status !== "authenticated" && (this.activeAdapter = r);
    } catch (n) {
      throw this.pendingLoginProviderId = null, n;
    }
  }
  async handleCallback(e) {
    const r = e ? await this.getProviderAdapter(e) : await this.resolveActiveAdapter();
    return await this.applySession(await r.handleCallback(), r), this.pendingLoginProviderId = null, this.session;
  }
  async logout() {
    await (await this.resolveActiveAdapter()).logout(), this.session = b;
  }
  async getAccessToken() {
    return (await this.resolveActiveAdapter()).getAccessToken();
  }
  async refresh() {
    const e = await this.resolveActiveAdapter();
    return await this.applySession(await e.refresh(), e), this.session;
  }
  async resolveActiveAdapter() {
    if (this.activeAdapter)
      return this.activeAdapter;
    const e = await this.options.bootstrap(), r = e.providers.find((s) => s.enabled && s.isDefault) ?? e.providers.find((s) => s.enabled);
    if (!r)
      throw new y("No enabled authentication provider was returned by /_elsa/identity/bootstrap.");
    const n = this.resolveProviderAdapter(r);
    return this.activeAdapter = n, n;
  }
  async getProviderAdapter(e) {
    const r = this.adapters.get(e);
    if (r)
      return r;
    const s = (await this.options.bootstrap()).providers.find((i) => i.enabled && i.id === e);
    if (!s)
      throw new y(`No auth provider adapter is registered for '${e}'.`);
    return this.resolveProviderAdapter(s);
  }
  resolveProviderAdapter(e) {
    const r = this.adapters.get(e.id);
    if (r)
      return r;
    if (!this.options.adapterFactory)
      throw new y(`No auth provider adapter is registered for '${e.id}'.`);
    const n = this.options.adapterFactory(e);
    return this.adapters.set(e.id, n), n;
  }
  getCallbackProviderId() {
    const e = this.options.getCallbackProviderId?.();
    return e || (this.pendingLoginProviderId ? this.pendingLoginProviderId : typeof window > "u" ? null : new URLSearchParams(window.location.search).get("authProviderId"));
  }
  async applySession(e, r) {
    this.session = e, this.activeAdapter = e.provider?.id ? await this.getProviderAdapter(e.provider.id) : r;
  }
}
class y extends Error {
  constructor(e) {
    super(e), this.name = "AuthConfigurationError";
  }
}
function F(t) {
  const e = t.fetch ?? fetch, r = t.sessionEndpoint ?? "/_elsa/identity/session", n = t.logoutEndpoint ?? `/_elsa/identity/logout/${encodeURIComponent(t.id)}`;
  return {
    id: t.id,
    kind: t.kind,
    initialize: () => v(e, r, t),
    login: (s) => {
      const i = t.challenge;
      if (!i || i.type === "none")
        throw new g(`Provider '${t.id}' does not expose a redirect challenge.`);
      const o = "method" in i ? i.method.toUpperCase() : "GET";
      if (o !== "GET")
        throw new g(`Provider '${t.id}' exposes an unsupported ${o} challenge.`);
      const c = new URL(Y(i), m(t)), a = s?.returnUrl ?? t.location?.href ?? window.location.href;
      return c.searchParams.set("returnUrl", Q(a, s?.providerId ?? t.id, t)), (t.location ?? window.location).assign(c.toString()), Promise.resolve();
    },
    handleCallback: () => v(e, r, t),
    logout: async () => {
      const s = await e(k(n, t), { method: "POST", credentials: "include" });
      if (!s.ok)
        throw new g(`Sign-out failed with ${s.status}.`);
    },
    getAccessToken: async () => {
      if (!t.tokenEndpoint)
        return null;
      const s = await e(k(t.tokenEndpoint, t), { credentials: "include", cache: "no-store" });
      if (s.status === 401)
        return null;
      if (!s.ok)
        throw new g(`Access-token request failed with ${s.status}.`);
      const i = await s.json();
      return typeof i.accessToken == "string" ? i.accessToken : null;
    },
    refresh: async () => {
      const s = await t.getRefreshToken?.(), i = t.refreshEndpoint;
      if (!i || !s)
        return v(e, r, t);
      const o = await e(k(i, t), {
        method: "POST",
        credentials: "include",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json"
        },
        body: JSON.stringify({ refreshToken: s })
      });
      if (o.status === 401)
        return b;
      if (!o.ok)
        throw new g(`Session refresh failed with ${o.status}.`);
      const c = await o.json();
      return c.status ? H(c) : v(e, r, t);
    }
  };
}
async function v(t, e, r) {
  const n = await t(k(e, r), { credentials: "include", cache: "no-store" });
  if (n.status === 401)
    return b;
  if (!n.ok)
    throw new g(`Session request failed with ${n.status}.`);
  return D(n);
}
async function D(t) {
  const e = await t.json();
  return H(e);
}
function H(t) {
  const e = G(t.status) ? t.status : "anonymous";
  return {
    ...t,
    status: e,
    roles: E(t.roles),
    permissions: E(t.permissions)
  };
}
function E(t) {
  return Array.isArray(t) ? t.filter((e) => typeof e == "string") : [];
}
function G(t) {
  return t === "unknown" || t === "anonymous" || t === "authenticated";
}
function Y(t) {
  return "loginPath" in t ? t.loginPath : t.url;
}
function k(t, e) {
  return new URL(t, m(e)).toString();
}
function m(t) {
  return t?.baseUrl ?? t?.location?.origin ?? window.location.origin;
}
function Q(t, e, r) {
  const n = new URL(t, V(r));
  return n.searchParams.set("authProviderId", e), W(t) ? `${n.pathname}${n.search}${n.hash}` : n.toString();
}
function V(t) {
  return t?.location?.href ?? (typeof window < "u" ? window.location.href : void 0) ?? t?.location?.origin ?? m(t);
}
function W(t) {
  try {
    return new URL(t), !1;
  } catch {
    return !0;
  }
}
class g extends Error {
  constructor(e) {
    super(e), this.name = "AuthAdapterError";
  }
}
function wt(t = {}) {
  const e = t.baseUrl ?? window.location.origin, r = t.fetch ?? fetch;
  return N({
    bootstrap: () => $(r, e, "/_elsa/identity/bootstrap"),
    capabilities: () => $(r, e, "/_elsa/identity/capabilities"),
    isCallback: t.isCallback,
    getCallbackProviderId: t.getCallbackProviderId,
    adapterFactory: (n) => F({
      id: n.id,
      kind: n.kind,
      baseUrl: e,
      challenge: n.challenge,
      fetch: r
    })
  });
}
async function $(t, e, r) {
  const n = await t(new URL(r, e).toString(), {
    credentials: "include",
    cache: "no-store",
    headers: { Accept: "application/json" }
  });
  if (!n.ok)
    throw new Error(`Auth discovery request failed with ${n.status}.`);
  return await n.json();
}
var P = { exports: {} }, A = {};
var j;
function X() {
  if (j) return A;
  j = 1;
  var t = /* @__PURE__ */ Symbol.for("react.transitional.element"), e = /* @__PURE__ */ Symbol.for("react.fragment");
  function r(n, s, i) {
    var o = null;
    if (i !== void 0 && (o = "" + i), s.key !== void 0 && (o = "" + s.key), "key" in s) {
      i = {};
      for (var c in s)
        c !== "key" && (i[c] = s[c]);
    } else i = s;
    return s = i.ref, {
      $$typeof: t,
      type: n,
      key: o,
      ref: s !== void 0 ? s : null,
      props: i
    };
  }
  return A.Fragment = e, A.jsx = r, A.jsxs = r, A;
}
var U;
function Z() {
  return U || (U = 1, P.exports = X()), P.exports;
}
var f = Z();
const O = u.createContext(null);
function x() {
  const t = u.useContext(O);
  if (!t)
    throw new Error("Auth SDK hooks must be used within <AuthProvider>.");
  return t;
}
function gt({ manager: t, children: e }) {
  const [r, n] = u.useState(() => t.getSession() ?? L), [s, i] = u.useState(null), o = u.useRef(!1), c = u.useRef(0), a = u.useCallback((d) => o.current && c.current === d, []);
  u.useLayoutEffect(() => {
    o.current = !0;
    const d = ++c.current;
    async function h() {
      try {
        const l = await t.initialize();
        if (!a(d))
          return;
        if (n(l), l.status !== "authenticated") {
          i(null);
          return;
        }
        try {
          const p = await t.getCapabilities();
          a(d) && i(p);
        } catch (p) {
          a(d) && (console.error("Auth capabilities request failed.", p), i(null));
        }
      } catch (l) {
        a(d) && (console.error("Auth initialization failed.", l), n(b), i(null));
      }
    }
    return h(), () => {
      o.current = !1, c.current += 1;
    };
  }, [t, a]);
  const w = u.useCallback(async (d) => {
    const h = ++c.current;
    if (await t.login(d), !a(h))
      return;
    const l = t.getSession();
    if (n(l), l.status === "authenticated")
      try {
        const p = await t.getCapabilities();
        a(h) && i(p);
      } catch (p) {
        a(h) && (console.error("Auth capabilities request failed.", p), i(null));
      }
    else
      i(null);
  }, [t, a]), R = u.useCallback(async () => {
    const d = ++c.current;
    await t.logout(), a(d) && (n(t.getSession()), i(null));
  }, [t, a]), T = u.useCallback(async () => {
    const d = ++c.current, h = await t.refresh();
    if (!a(d))
      return h;
    if (n(h), h.status === "authenticated")
      try {
        const l = await t.getCapabilities();
        a(d) && i(l);
      } catch (l) {
        a(d) && (console.error("Auth capabilities request failed.", l), i(null));
      }
    else
      i(null);
    return h;
  }, [t, a]), M = u.useMemo(() => ({
    session: r,
    capabilities: s,
    login: w,
    logout: R,
    refresh: T
  }), [s, w, R, T, r]);
  return /* @__PURE__ */ f.jsx(O.Provider, { value: M, children: e });
}
function K() {
  return x().session;
}
function tt() {
  const { permissions: t } = K();
  return u.useMemo(() => {
    const e = new Set(t);
    return {
      has: (r) => e.has(r),
      hasAny: (r) => r.some((n) => e.has(n)),
      hasAll: (r) => r.every((n) => e.has(n))
    };
  }, [t]);
}
function At() {
  return x().capabilities;
}
function yt({ requires: t, requireAll: e = !0, fallback: r = null, children: n }) {
  const s = tt(), i = typeof t == "string" ? [t] : t ?? [];
  return i.length === 0 || (e ? s.hasAll(i) : s.hasAny(i)) ? /* @__PURE__ */ f.jsx(f.Fragment, { children: n }) : /* @__PURE__ */ f.jsx(f.Fragment, { children: r });
}
function vt({ children: t, fallback: e = null, loginOptions: r }) {
  const { session: n, login: s } = x(), i = u.useRef(null);
  return u.useEffect(() => {
    if (n.status === "anonymous") {
      const o = et(r), c = i.current;
      if (c?.key === o && c.login === s)
        return;
      const a = { key: o, login: s };
      i.current = a, s(r).catch((w) => {
        i.current === a && (i.current = null), console.error("Auth login failed.", w);
      });
    } else
      i.current = null;
  }, [s, r, n.status]), n.status !== "authenticated" ? /* @__PURE__ */ f.jsx(f.Fragment, { children: e }) : /* @__PURE__ */ f.jsx(f.Fragment, { children: t });
}
function et(t) {
  return `${t?.providerId ?? ""}
${t?.returnUrl ?? ""}`;
}
function kt(t) {
  return F({
    ...t,
    kind: "external-oidc"
  });
}
function St(t, e, r = {}) {
  return {
    getJson(n, s) {
      return J(t, n, e, r, nt(s));
    },
    postJson(n, s, i) {
      return J(t, n, e, r, {
        ...i,
        method: "POST",
        headers: st(i?.headers),
        body: JSON.stringify(s)
      });
    }
  };
}
async function J(t, e, r, n, s) {
  const i = n.fetch ?? fetch, o = new URL(e, t).toString(), c = await i(o, await z(r, I(n, s))), a = c.status === 401 && n.refreshOnUnauthorized !== !1 ? await rt(i, o, r, I(n, s)) : c;
  if (!a.ok)
    throw new S(a.status, await _(a));
  const w = await a.text();
  try {
    return JSON.parse(w);
  } catch {
    throw new S(a.status, `Expected JSON from ${o}.`);
  }
}
async function rt(t, e, r, n) {
  return (await r.refresh()).status !== "authenticated" ? new Response("Authentication required.", { status: 401 }) : t(e, await z(r, n));
}
async function z(t, e) {
  const r = new Headers(e?.headers), n = await t.getAccessToken();
  return n && r.set("Authorization", `Bearer ${n}`), {
    ...e,
    credentials: e?.credentials ?? "include",
    headers: r
  };
}
function nt(t) {
  const e = new Headers(t?.headers);
  return e.has("Accept") || e.set("Accept", "application/json"), {
    ...t,
    cache: t?.cache ?? "no-store",
    headers: e
  };
}
function st(t) {
  const e = new Headers(t);
  return e.has("Content-Type") || e.set("Content-Type", "application/json"), e.has("Accept") || e.set("Accept", "application/json"), e;
}
function I(t, e) {
  return C(t.defaultHeaders ?? t.headers, e);
}
function it(t, e = {}) {
  return async () => await t.getAccessToken() ?? await e.fallbackAccessTokenFactory?.() ?? e.anonymousToken ?? "";
}
function bt(t, e) {
  const r = at(t.accessTokenFactory) ? t.accessTokenFactory.bind(t) : void 0;
  return {
    ...t,
    accessTokenFactory: it(e, { fallbackAccessTokenFactory: r })
  };
}
function at(t) {
  return typeof t == "function";
}
function Pt() {
  const t = [];
  return {
    add(e) {
      t.push(e);
    },
    list() {
      return [...t];
    }
  };
}
function Ct(t, e = {}) {
  return {
    baseUrl: t,
    headers: e.headers,
    http: ot(t, e.headers)
  };
}
function ot(t, e) {
  return {
    async getJson(r, n) {
      return q(t, r, C(e, lt(n)));
    },
    async postJson(r, n, s) {
      return q(t, r, C(e, {
        ...s,
        method: "POST",
        headers: ht(s?.headers),
        body: JSON.stringify(n)
      }));
    }
  };
}
function C(t, e = {}) {
  return t ? {
    ...e,
    headers: ct(t, e.headers)
  } : e;
}
function ct(t, e) {
  const r = new Headers(t);
  return new Headers(e).forEach((n, s) => r.set(s, n)), r;
}
async function q(t, e, r) {
  const n = dt(t, e), s = await fetch(n, r);
  if (!s.ok)
    throw new S(s.status, await _(s));
  const i = await s.text();
  try {
    return JSON.parse(i);
  } catch {
    throw new S(
      s.status,
      `Expected JSON from ${n}, but received ${ft(s, i)}. Check Studio:BackendBaseUrl and make sure the backend maps this API route.`
    );
  }
}
async function _(t) {
  const e = t.headers.get("content-type") ?? "";
  if (e.includes("application/json") || e.includes("+json"))
    try {
      const n = await t.json();
      return ut(n) ?? `Request failed with ${t.status}.`;
    } catch {
      return `Request failed with ${t.status}.`;
    }
  return (await t.text()).trim() || `Request failed with ${t.status}.`;
}
function ut(t) {
  if (typeof t.detail == "string" && t.detail.length > 0) return t.detail;
  if (typeof t.title == "string" && t.title.length > 0) return t.title;
  if (Array.isArray(t.errors) && t.errors.length > 0) return t.errors.map(String).join(" ");
  if (t.errors && typeof t.errors == "object") {
    const e = Object.values(t.errors).flatMap((r) => Array.isArray(r) ? r : [r]).map(String);
    if (e.length > 0) return e.join(" ");
  }
  return null;
}
function dt(t, e) {
  return new URL(e, t).toString();
}
function lt(t) {
  const e = new Headers(t?.headers);
  return e.has("Accept") || e.set("Accept", "application/json"), {
    ...t,
    cache: t?.cache ?? "no-store",
    headers: e
  };
}
function ht(t) {
  const e = new Headers(t);
  return e.has("Content-Type") || e.set("Content-Type", "application/json"), e.has("Accept") || e.set("Accept", "application/json"), e;
}
function ft(t, e) {
  const r = t.headers.get("content-type") ?? "an unknown content type", n = e.trim(), s = n.length > 0 ? `: ${n.slice(0, 80)}` : "";
  return `${r}${s}`;
}
class S extends Error {
  constructor(e, r) {
    super(r), this.status = e, this.name = "StudioHttpError";
  }
  status;
}
export {
  g as AuthAdapterError,
  y as AuthConfigurationError,
  yt as AuthGuard,
  gt as AuthProvider,
  vt as RequireAuth,
  S as StudioHttpError,
  N as createAuthProviderManager,
  St as createAuthenticatedHttpClient,
  wt as createBackendAuthProviderManager,
  Pt as createContributionRegistry,
  Ct as createEndpointContext,
  ot as createHttpClient,
  kt as createOidcAuthAdapter,
  F as createRedirectAuthAdapter,
  it as createSignalRAccessTokenFactory,
  _ as readStudioHttpErrorMessage,
  At as useAuthCapabilities,
  x as useAuthContext,
  K as useAuthSession,
  tt as usePermissions,
  bt as withAuthenticatedSignalROptions,
  C as withDefaultHeaders
};
