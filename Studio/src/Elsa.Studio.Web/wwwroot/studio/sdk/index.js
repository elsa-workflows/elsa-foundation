import { r as c } from "../vendor/chunks/index.js";
const v = {
  status: "anonymous",
  roles: [],
  permissions: []
}, j = {
  status: "unknown",
  roles: [],
  permissions: []
};
function _(t) {
  return new F(t);
}
class F {
  constructor(e) {
    this.options = e;
    for (const s of e.adapters ?? []) {
      if (this.adapters.has(s.id))
        throw new p(`Duplicate auth provider adapter '${s.id}'.`);
      this.adapters.set(s.id, s);
    }
  }
  options;
  adapters = /* @__PURE__ */ new Map();
  activeAdapter = null;
  session = j;
  getSession() {
    return this.session;
  }
  getCapabilities() {
    return this.options.capabilities();
  }
  async initialize() {
    const e = await this.resolveAdapter();
    return this.session = this.options.isCallback?.() ? await e.handleCallback() : await e.initialize(), this.session;
  }
  async login(e) {
    await (e?.providerId ? await this.resolveAdapter(e.providerId) : await this.resolveAdapter()).login(e);
  }
  async handleCallback(e) {
    const s = e ? await this.resolveAdapter(e) : await this.resolveAdapter();
    return this.session = await s.handleCallback(), this.session;
  }
  async logout() {
    await (await this.resolveAdapter()).logout(), this.session = v;
  }
  async getAccessToken() {
    return (await this.resolveAdapter()).getAccessToken();
  }
  async refresh() {
    const e = await this.resolveAdapter();
    return this.session = await e.refresh(), this.session;
  }
  async resolveAdapter(e) {
    if (e)
      return this.requireAdapter(e);
    if (this.activeAdapter)
      return this.activeAdapter;
    const s = await this.options.bootstrap(), n = s.providers.find((r) => r.enabled && r.isDefault) ?? s.providers.find((r) => r.enabled);
    if (!n)
      throw new p("No enabled authentication provider was returned by /_elsa/identity/bootstrap.");
    return this.activeAdapter = this.resolveProviderAdapter(n), this.activeAdapter;
  }
  requireAdapter(e) {
    const s = this.adapters.get(e);
    if (!s)
      throw new p(`No auth provider adapter is registered for '${e}'.`);
    return this.activeAdapter = s, s;
  }
  resolveProviderAdapter(e) {
    const s = this.adapters.get(e.id);
    if (s)
      return this.activeAdapter = s, s;
    if (!this.options.adapterFactory)
      throw new p(`No auth provider adapter is registered for '${e.id}'.`);
    const n = this.options.adapterFactory(e);
    return this.adapters.set(e.id, n), this.activeAdapter = n, n;
  }
}
class p extends Error {
  constructor(e) {
    super(e), this.name = "AuthConfigurationError";
  }
}
function $(t) {
  const e = t.fetch ?? fetch, s = t.sessionEndpoint ?? "/_elsa/identity/session", n = t.logoutEndpoint ?? `/_elsa/identity/logout/${encodeURIComponent(t.id)}`;
  return {
    id: t.id,
    kind: t.kind,
    initialize: () => w(e, s, t),
    login: (r) => {
      const a = t.challenge;
      if (!a || a.type === "none")
        throw new l(`Provider '${t.id}' does not expose a redirect challenge.`);
      const i = "method" in a ? a.method.toUpperCase() : "GET";
      if (i !== "GET")
        throw new l(`Provider '${t.id}' exposes an unsupported ${i} challenge.`);
      const o = new URL(z(a), J(t)), u = r?.returnUrl ?? t.location?.href ?? window.location.href;
      return o.searchParams.set("returnUrl", u), (t.location ?? window.location).assign(o.toString()), Promise.resolve();
    },
    handleCallback: () => w(e, s, t),
    logout: async () => {
      const r = await e(A(n, t), { method: "POST", credentials: "include" });
      if (!r.ok)
        throw new l(`Sign-out failed with ${r.status}.`);
    },
    getAccessToken: async () => {
      if (!t.tokenEndpoint)
        return null;
      const r = await e(A(t.tokenEndpoint, t), { credentials: "include", cache: "no-store" });
      if (r.status === 401)
        return null;
      if (!r.ok)
        throw new l(`Access-token request failed with ${r.status}.`);
      const a = await r.json();
      return typeof a.accessToken == "string" ? a.accessToken : null;
    },
    refresh: async () => {
      const r = await t.getRefreshToken?.(), a = t.refreshEndpoint;
      if (!a || !r)
        return w(e, s, t);
      const i = await e(A(a, t), {
        method: "POST",
        credentials: "include",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json"
        },
        body: JSON.stringify({ refreshToken: r })
      });
      if (i.status === 401)
        return v;
      if (!i.ok)
        throw new l(`Session refresh failed with ${i.status}.`);
      const o = await i.json();
      return o.status ? P(o) : w(e, s, t);
    }
  };
}
async function w(t, e, s) {
  const n = await t(A(e, s), { credentials: "include", cache: "no-store" });
  if (n.status === 401)
    return v;
  if (!n.ok)
    throw new l(`Session request failed with ${n.status}.`);
  return H(n);
}
async function H(t) {
  const e = await t.json();
  return P(e);
}
function P(t) {
  return {
    ...t,
    roles: t.roles ?? [],
    permissions: t.permissions ?? []
  };
}
function z(t) {
  return "loginPath" in t ? t.loginPath : t.url;
}
function A(t, e) {
  return new URL(t, J(e)).toString();
}
function J(t) {
  return t?.baseUrl ?? t?.location?.origin ?? window.location.origin;
}
class l extends Error {
  constructor(e) {
    super(e), this.name = "AuthAdapterError";
  }
}
function at(t = {}) {
  const e = t.baseUrl ?? window.location.origin, s = t.fetch ?? fetch;
  return _({
    bootstrap: () => m(s, e, "/_elsa/identity/bootstrap"),
    capabilities: () => m(s, e, "/_elsa/identity/capabilities"),
    isCallback: t.isCallback,
    adapterFactory: (n) => $({
      id: n.id,
      kind: n.kind,
      baseUrl: e,
      challenge: n.challenge,
      fetch: s
    })
  });
}
async function m(t, e, s) {
  const n = await t(new URL(s, e).toString(), {
    credentials: "include",
    cache: "no-store",
    headers: { Accept: "application/json" }
  });
  if (!n.ok)
    throw new Error(`Auth discovery request failed with ${n.status}.`);
  return await n.json();
}
var k = { exports: {} }, f = {};
var C;
function N() {
  if (C) return f;
  C = 1;
  var t = /* @__PURE__ */ Symbol.for("react.transitional.element"), e = /* @__PURE__ */ Symbol.for("react.fragment");
  function s(n, r, a) {
    var i = null;
    if (a !== void 0 && (i = "" + a), r.key !== void 0 && (i = "" + r.key), "key" in r) {
      a = {};
      for (var o in r)
        o !== "key" && (a[o] = r[o]);
    } else a = r;
    return r = a.ref, {
      $$typeof: t,
      type: n,
      key: i,
      ref: r !== void 0 ? r : null,
      props: a
    };
  }
  return f.Fragment = e, f.jsx = s, f.jsxs = s, f;
}
var E;
function B() {
  return E || (E = 1, k.exports = N()), k.exports;
}
var h = B();
const q = c.createContext(null);
function x() {
  const t = c.useContext(q);
  if (!t)
    throw new Error("Auth SDK hooks must be used within <AuthProvider>.");
  return t;
}
function it({ manager: t, children: e }) {
  const [s, n] = c.useState(() => t.getSession() ?? j), [r, a] = c.useState(null);
  c.useEffect(() => {
    let d = !1;
    async function O() {
      const S = await t.initialize();
      if (!d && (n(S), S.status === "authenticated")) {
        const M = await t.getCapabilities();
        d || a(M);
      }
    }
    return O(), () => {
      d = !0;
    };
  }, [t]);
  const i = c.useCallback((d) => t.login(d), [t]), o = c.useCallback(async () => {
    await t.logout(), n(t.getSession()), a(null);
  }, [t]), u = c.useCallback(async () => {
    const d = await t.refresh();
    return n(d), d.status === "authenticated" ? a(await t.getCapabilities()) : a(null), d;
  }, [t]), y = c.useMemo(() => ({
    session: s,
    capabilities: r,
    login: i,
    logout: o,
    refresh: u
  }), [r, i, o, u, s]);
  return /* @__PURE__ */ h.jsx(q.Provider, { value: y, children: e });
}
function D() {
  return x().session;
}
function L() {
  const { permissions: t } = D();
  return c.useMemo(() => {
    const e = new Set(t);
    return {
      has: (s) => e.has(s),
      hasAny: (s) => s.some((n) => e.has(n)),
      hasAll: (s) => s.every((n) => e.has(n))
    };
  }, [t]);
}
function ot() {
  return x().capabilities;
}
function ct({ requires: t, requireAll: e = !0, fallback: s = null, children: n }) {
  const r = L(), a = typeof t == "string" ? [t] : t ?? [];
  return a.length === 0 || (e ? r.hasAll(a) : r.hasAny(a)) ? /* @__PURE__ */ h.jsx(h.Fragment, { children: n }) : /* @__PURE__ */ h.jsx(h.Fragment, { children: s });
}
function ut({ children: t, fallback: e = null, loginOptions: s }) {
  const { session: n, login: r } = x();
  return c.useEffect(() => {
    n.status === "anonymous" && r(s);
  }, [r, s, n.status]), n.status !== "authenticated" ? /* @__PURE__ */ h.jsx(h.Fragment, { children: e }) : /* @__PURE__ */ h.jsx(h.Fragment, { children: t });
}
function dt(t) {
  return $({
    ...t,
    kind: "external-oidc"
  });
}
function ht(t, e, s = {}) {
  return {
    getJson(n, r) {
      return b(t, n, e, s, I(r));
    },
    postJson(n, r, a) {
      return b(t, n, e, s, {
        ...a,
        method: "POST",
        headers: Q(a?.headers),
        body: JSON.stringify(r)
      });
    }
  };
}
async function b(t, e, s, n, r) {
  const a = n.fetch ?? fetch, i = new URL(e, t).toString(), o = await a(i, await U(s, r)), u = o.status === 401 && n.refreshOnUnauthorized !== !1 ? await G(a, i, s, r) : o;
  if (!u.ok)
    throw new g(u.status, await Y(u));
  const y = await u.text();
  try {
    return JSON.parse(y);
  } catch {
    throw new g(u.status, `Expected JSON from ${i}.`);
  }
}
async function G(t, e, s, n) {
  return (await s.refresh()).status !== "authenticated" ? new Response("Authentication required.", { status: 401 }) : t(e, await U(s, n));
}
async function U(t, e) {
  const s = new Headers(e?.headers), n = await t.getAccessToken();
  return n && s.set("Authorization", `Bearer ${n}`), {
    ...e,
    credentials: e?.credentials ?? "include",
    headers: s
  };
}
async function Y(t) {
  return (await t.text()).trim() || `Request failed with ${t.status}.`;
}
function I(t) {
  const e = new Headers(t?.headers);
  return e.has("Accept") || e.set("Accept", "application/json"), {
    ...t,
    cache: t?.cache ?? "no-store",
    headers: e
  };
}
function Q(t) {
  const e = new Headers(t);
  return e.has("Content-Type") || e.set("Content-Type", "application/json"), e.has("Accept") || e.set("Accept", "application/json"), e;
}
function V(t, e = {}) {
  return async () => await t.getAccessToken() ?? e.anonymousToken ?? "";
}
function lt(t, e) {
  return {
    ...t,
    accessTokenFactory: V(e)
  };
}
function ft() {
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
function pt(t, e = {}) {
  return {
    baseUrl: t,
    headers: e.headers,
    http: W(t, e.headers)
  };
}
function W(t, e) {
  return {
    async getJson(s, n) {
      return R(t, s, T(e, et(n)));
    },
    async postJson(s, n, r) {
      return R(t, s, T(e, {
        ...r,
        method: "POST",
        headers: st(r?.headers),
        body: JSON.stringify(n)
      }));
    }
  };
}
function T(t, e = {}) {
  return t ? {
    ...e,
    headers: X(t, e.headers)
  } : e;
}
function X(t, e) {
  const s = new Headers(t);
  return new Headers(e).forEach((n, r) => s.set(r, n)), s;
}
async function R(t, e, s) {
  const n = tt(t, e), r = await fetch(n, s);
  if (!r.ok)
    throw new g(r.status, await Z(r));
  const a = await r.text();
  try {
    return JSON.parse(a);
  } catch {
    throw new g(
      r.status,
      `Expected JSON from ${n}, but received ${nt(r, a)}. Check Studio:BackendBaseUrl and make sure the backend maps this API route.`
    );
  }
}
async function Z(t) {
  if ((t.headers.get("content-type") ?? "").includes("application/json"))
    try {
      const n = await t.json();
      return K(n) ?? `Request failed with ${t.status}.`;
    } catch {
      return `Request failed with ${t.status}.`;
    }
  return (await t.text()).trim() || `Request failed with ${t.status}.`;
}
function K(t) {
  if (typeof t.detail == "string" && t.detail.length > 0) return t.detail;
  if (typeof t.title == "string" && t.title.length > 0) return t.title;
  if (Array.isArray(t.errors) && t.errors.length > 0) return t.errors.map(String).join(" ");
  if (t.errors && typeof t.errors == "object") {
    const e = Object.values(t.errors).flatMap((s) => Array.isArray(s) ? s : [s]).map(String);
    if (e.length > 0) return e.join(" ");
  }
  return null;
}
function tt(t, e) {
  return new URL(e, t).toString();
}
function et(t) {
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
function nt(t, e) {
  const s = t.headers.get("content-type") ?? "an unknown content type", n = e.trim(), r = n.length > 0 ? `: ${n.slice(0, 80)}` : "";
  return `${s}${r}`;
}
class g extends Error {
  constructor(e, s) {
    super(s), this.status = e, this.name = "StudioHttpError";
  }
  status;
}
export {
  l as AuthAdapterError,
  p as AuthConfigurationError,
  ct as AuthGuard,
  it as AuthProvider,
  ut as RequireAuth,
  g as StudioHttpError,
  _ as createAuthProviderManager,
  ht as createAuthenticatedHttpClient,
  at as createBackendAuthProviderManager,
  ft as createContributionRegistry,
  pt as createEndpointContext,
  W as createHttpClient,
  dt as createOidcAuthAdapter,
  $ as createRedirectAuthAdapter,
  V as createSignalRAccessTokenFactory,
  ot as useAuthCapabilities,
  x as useAuthContext,
  D as useAuthSession,
  L as usePermissions,
  lt as withAuthenticatedSignalROptions,
  T as withDefaultHeaders
};
