import { r as c } from "../vendor/chunks/index.js";
const x = {
  status: "anonymous",
  roles: [],
  permissions: []
}, P = {
  status: "unknown",
  roles: [],
  permissions: []
};
function F(t) {
  return new H(t);
}
class H {
  constructor(e) {
    this.options = e;
    for (const s of e.adapters ?? []) {
      if (this.adapters.has(s.id))
        throw new w(`Duplicate auth provider adapter '${s.id}'.`);
      this.adapters.set(s.id, s);
    }
  }
  options;
  adapters = /* @__PURE__ */ new Map();
  activeAdapter = null;
  session = P;
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
    await (await this.resolveAdapter()).logout(), this.session = x;
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
      throw new w("No enabled authentication provider was returned by /_elsa/identity/bootstrap.");
    return this.activeAdapter = this.resolveProviderAdapter(n), this.activeAdapter;
  }
  requireAdapter(e) {
    const s = this.adapters.get(e);
    if (!s)
      throw new w(`No auth provider adapter is registered for '${e}'.`);
    return this.activeAdapter = s, s;
  }
  resolveProviderAdapter(e) {
    const s = this.adapters.get(e.id);
    if (s)
      return this.activeAdapter = s, s;
    if (!this.options.adapterFactory)
      throw new w(`No auth provider adapter is registered for '${e.id}'.`);
    const n = this.options.adapterFactory(e);
    return this.adapters.set(e.id, n), this.activeAdapter = n, n;
  }
}
class w extends Error {
  constructor(e) {
    super(e), this.name = "AuthConfigurationError";
  }
}
function $(t) {
  const e = t.fetch ?? fetch, s = t.sessionEndpoint ?? "/_elsa/identity/session", n = t.logoutEndpoint ?? `/_elsa/identity/logout/${encodeURIComponent(t.id)}`;
  return {
    id: t.id,
    kind: t.kind,
    initialize: () => A(e, s, t),
    login: (r) => {
      const i = t.challenge;
      if (!i || i.type === "none")
        throw new l(`Provider '${t.id}' does not expose a redirect challenge.`);
      const a = "method" in i ? i.method.toUpperCase() : "GET";
      if (a !== "GET")
        throw new l(`Provider '${t.id}' exposes an unsupported ${a} challenge.`);
      const o = new URL(N(i), q(t)), u = r?.returnUrl ?? t.location?.href ?? window.location.href;
      return o.searchParams.set("returnUrl", u), (t.location ?? window.location).assign(o.toString()), Promise.resolve();
    },
    handleCallback: () => A(e, s, t),
    logout: async () => {
      const r = await e(g(n, t), { method: "POST", credentials: "include" });
      if (!r.ok)
        throw new l(`Sign-out failed with ${r.status}.`);
    },
    getAccessToken: async () => {
      if (!t.tokenEndpoint)
        return null;
      const r = await e(g(t.tokenEndpoint, t), { credentials: "include", cache: "no-store" });
      if (r.status === 401)
        return null;
      if (!r.ok)
        throw new l(`Access-token request failed with ${r.status}.`);
      const i = await r.json();
      return typeof i.accessToken == "string" ? i.accessToken : null;
    },
    refresh: async () => {
      const r = await t.getRefreshToken?.(), i = t.refreshEndpoint;
      if (!i || !r)
        return A(e, s, t);
      const a = await e(g(i, t), {
        method: "POST",
        credentials: "include",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json"
        },
        body: JSON.stringify({ refreshToken: r })
      });
      if (a.status === 401)
        return x;
      if (!a.ok)
        throw new l(`Session refresh failed with ${a.status}.`);
      const o = await a.json();
      return o.status ? J(o) : A(e, s, t);
    }
  };
}
async function A(t, e, s) {
  const n = await t(g(e, s), { credentials: "include", cache: "no-store" });
  if (n.status === 401)
    return x;
  if (!n.ok)
    throw new l(`Session request failed with ${n.status}.`);
  return z(n);
}
async function z(t) {
  const e = await t.json();
  return J(e);
}
function J(t) {
  return {
    ...t,
    roles: t.roles ?? [],
    permissions: t.permissions ?? []
  };
}
function N(t) {
  return "loginPath" in t ? t.loginPath : t.url;
}
function g(t, e) {
  return new URL(t, q(e)).toString();
}
function q(t) {
  return t?.baseUrl ?? t?.location?.origin ?? window.location.origin;
}
class l extends Error {
  constructor(e) {
    super(e), this.name = "AuthAdapterError";
  }
}
function at(t = {}) {
  const e = t.baseUrl ?? window.location.origin, s = t.fetch ?? fetch;
  return F({
    bootstrap: () => C(s, e, "/_elsa/identity/bootstrap"),
    capabilities: () => C(s, e, "/_elsa/identity/capabilities"),
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
async function C(t, e, s) {
  const n = await t(new URL(s, e).toString(), {
    credentials: "include",
    cache: "no-store",
    headers: { Accept: "application/json" }
  });
  if (!n.ok)
    throw new Error(`Auth discovery request failed with ${n.status}.`);
  return await n.json();
}
var v = { exports: {} }, f = {};
var b;
function B() {
  if (b) return f;
  b = 1;
  var t = /* @__PURE__ */ Symbol.for("react.transitional.element"), e = /* @__PURE__ */ Symbol.for("react.fragment");
  function s(n, r, i) {
    var a = null;
    if (i !== void 0 && (a = "" + i), r.key !== void 0 && (a = "" + r.key), "key" in r) {
      i = {};
      for (var o in r)
        o !== "key" && (i[o] = r[o]);
    } else i = r;
    return r = i.ref, {
      $$typeof: t,
      type: n,
      key: a,
      ref: r !== void 0 ? r : null,
      props: i
    };
  }
  return f.Fragment = e, f.jsx = s, f.jsxs = s, f;
}
var E;
function D() {
  return E || (E = 1, v.exports = B()), v.exports;
}
var h = D();
const U = c.createContext(null);
function m() {
  const t = c.useContext(U);
  if (!t)
    throw new Error("Auth SDK hooks must be used within <AuthProvider>.");
  return t;
}
function ot({ manager: t, children: e }) {
  const [s, n] = c.useState(() => t.getSession() ?? P), [r, i] = c.useState(null);
  c.useEffect(() => {
    let d = !1;
    async function M() {
      const S = await t.initialize();
      if (!d && (n(S), S.status === "authenticated")) {
        const _ = await t.getCapabilities();
        d || i(_);
      }
    }
    return M(), () => {
      d = !0;
    };
  }, [t]);
  const a = c.useCallback((d) => t.login(d), [t]), o = c.useCallback(async () => {
    await t.logout(), n(t.getSession()), i(null);
  }, [t]), u = c.useCallback(async () => {
    const d = await t.refresh();
    return n(d), d.status === "authenticated" ? i(await t.getCapabilities()) : i(null), d;
  }, [t]), k = c.useMemo(() => ({
    session: s,
    capabilities: r,
    login: a,
    logout: o,
    refresh: u
  }), [r, a, o, u, s]);
  return /* @__PURE__ */ h.jsx(U.Provider, { value: k, children: e });
}
function L() {
  return m().session;
}
function G() {
  const { permissions: t } = L();
  return c.useMemo(() => {
    const e = new Set(t);
    return {
      has: (s) => e.has(s),
      hasAny: (s) => s.some((n) => e.has(n)),
      hasAll: (s) => s.every((n) => e.has(n))
    };
  }, [t]);
}
function ct() {
  return m().capabilities;
}
function ut({ requires: t, requireAll: e = !0, fallback: s = null, children: n }) {
  const r = G(), i = typeof t == "string" ? [t] : t ?? [];
  return i.length === 0 || (e ? r.hasAll(i) : r.hasAny(i)) ? /* @__PURE__ */ h.jsx(h.Fragment, { children: n }) : /* @__PURE__ */ h.jsx(h.Fragment, { children: s });
}
function dt({ children: t, fallback: e = null, loginOptions: s }) {
  const { session: n, login: r } = m();
  return c.useEffect(() => {
    n.status === "anonymous" && r(s);
  }, [r, s, n.status]), n.status !== "authenticated" ? /* @__PURE__ */ h.jsx(h.Fragment, { children: e }) : /* @__PURE__ */ h.jsx(h.Fragment, { children: t });
}
function ht(t) {
  return $({
    ...t,
    kind: "external-oidc"
  });
}
function lt(t, e, s = {}) {
  return {
    getJson(n, r) {
      return R(t, n, e, s, Q(r));
    },
    postJson(n, r, i) {
      return R(t, n, e, s, {
        ...i,
        method: "POST",
        headers: V(i?.headers),
        body: JSON.stringify(r)
      });
    }
  };
}
async function R(t, e, s, n, r) {
  const i = n.fetch ?? fetch, a = new URL(e, t).toString(), o = await i(a, await O(s, r)), u = o.status === 401 && n.refreshOnUnauthorized !== !1 ? await Y(i, a, s, r) : o;
  if (!u.ok)
    throw new y(u.status, await I(u));
  const k = await u.text();
  try {
    return JSON.parse(k);
  } catch {
    throw new y(u.status, `Expected JSON from ${a}.`);
  }
}
async function Y(t, e, s, n) {
  return (await s.refresh()).status !== "authenticated" ? new Response("Authentication required.", { status: 401 }) : t(e, await O(s, n));
}
async function O(t, e) {
  const s = new Headers(e?.headers), n = await t.getAccessToken();
  return n && s.set("Authorization", `Bearer ${n}`), {
    ...e,
    credentials: e?.credentials ?? "include",
    headers: s
  };
}
async function I(t) {
  return (await t.text()).trim() || `Request failed with ${t.status}.`;
}
function Q(t) {
  const e = new Headers(t?.headers);
  return e.has("Accept") || e.set("Accept", "application/json"), {
    ...t,
    cache: t?.cache ?? "no-store",
    headers: e
  };
}
function V(t) {
  const e = new Headers(t);
  return e.has("Content-Type") || e.set("Content-Type", "application/json"), e.has("Accept") || e.set("Accept", "application/json"), e;
}
function W(t, e = {}) {
  return async () => await t.getAccessToken() ?? e.anonymousToken ?? "";
}
function ft(t, e) {
  return {
    ...t,
    accessTokenFactory: W(e)
  };
}
function p() {
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
function pt() {
  const t = /* @__PURE__ */ new Set();
  return {
    contextProviders: p(),
    promptActions: p(),
    tools: p(),
    proposalRenderers: p(),
    surfaces: p(),
    dispatchPrompt(e) {
      for (const s of t)
        s(e);
    },
    onPrompt(e) {
      return t.add(e), () => t.delete(e);
    }
  };
}
function wt(t, e = {}) {
  return {
    baseUrl: t,
    headers: e.headers,
    http: X(t, e.headers)
  };
}
function X(t, e) {
  return {
    async getJson(s, n) {
      return j(t, s, T(e, st(n)));
    },
    async postJson(s, n, r) {
      return j(t, s, T(e, {
        ...r,
        method: "POST",
        headers: nt(r?.headers),
        body: JSON.stringify(n)
      }));
    }
  };
}
function T(t, e = {}) {
  return t ? {
    ...e,
    headers: Z(t, e.headers)
  } : e;
}
function Z(t, e) {
  const s = new Headers(t);
  return new Headers(e).forEach((n, r) => s.set(r, n)), s;
}
async function j(t, e, s) {
  const n = et(t, e), r = await fetch(n, s);
  if (!r.ok)
    throw new y(r.status, await K(r));
  const i = await r.text();
  try {
    return JSON.parse(i);
  } catch {
    throw new y(
      r.status,
      `Expected JSON from ${n}, but received ${rt(r, i)}. Check Studio:BackendBaseUrl and make sure the backend maps this API route.`
    );
  }
}
async function K(t) {
  if ((t.headers.get("content-type") ?? "").includes("application/json"))
    try {
      const n = await t.json();
      return tt(n) ?? `Request failed with ${t.status}.`;
    } catch {
      return `Request failed with ${t.status}.`;
    }
  return (await t.text()).trim() || `Request failed with ${t.status}.`;
}
function tt(t) {
  if (typeof t.detail == "string" && t.detail.length > 0) return t.detail;
  if (typeof t.title == "string" && t.title.length > 0) return t.title;
  if (Array.isArray(t.errors) && t.errors.length > 0) return t.errors.map(String).join(" ");
  if (t.errors && typeof t.errors == "object") {
    const e = Object.values(t.errors).flatMap((s) => Array.isArray(s) ? s : [s]).map(String);
    if (e.length > 0) return e.join(" ");
  }
  return null;
}
function et(t, e) {
  return new URL(e, t).toString();
}
function st(t) {
  const e = new Headers(t?.headers);
  return e.has("Accept") || e.set("Accept", "application/json"), {
    ...t,
    cache: t?.cache ?? "no-store",
    headers: e
  };
}
function nt(t) {
  const e = new Headers(t);
  return e.has("Content-Type") || e.set("Content-Type", "application/json"), e.has("Accept") || e.set("Accept", "application/json"), e;
}
function rt(t, e) {
  const s = t.headers.get("content-type") ?? "an unknown content type", n = e.trim(), r = n.length > 0 ? `: ${n.slice(0, 80)}` : "";
  return `${s}${r}`;
}
class y extends Error {
  constructor(e, s) {
    super(s), this.status = e, this.name = "StudioHttpError";
  }
  status;
}
export {
  l as AuthAdapterError,
  w as AuthConfigurationError,
  ut as AuthGuard,
  ot as AuthProvider,
  dt as RequireAuth,
  y as StudioHttpError,
  pt as createAiContributionApi,
  F as createAuthProviderManager,
  lt as createAuthenticatedHttpClient,
  at as createBackendAuthProviderManager,
  p as createContributionRegistry,
  wt as createEndpointContext,
  X as createHttpClient,
  ht as createOidcAuthAdapter,
  $ as createRedirectAuthAdapter,
  W as createSignalRAccessTokenFactory,
  ct as useAuthCapabilities,
  m as useAuthContext,
  L as useAuthSession,
  G as usePermissions,
  ft as withAuthenticatedSignalROptions,
  T as withDefaultHeaders
};
