import { r as u } from "../vendor/chunks/index.js";
const P = {
  status: "anonymous",
  roles: [],
  permissions: []
}, F = {
  status: "unknown",
  roles: [],
  permissions: []
};
function B(t) {
  return new D(t);
}
class D {
  constructor(e) {
    this.options = e;
    for (const r of e.adapters ?? []) {
      if (this.adapters.has(r.id))
        throw new v(`Duplicate auth provider adapter '${r.id}'.`);
      this.adapters.set(r.id, r);
    }
  }
  options;
  adapters = /* @__PURE__ */ new Map();
  activeAdapter = null;
  pendingLoginProviderId = null;
  session = F;
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
    await (await this.resolveActiveAdapter()).logout(), this.session = P;
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
      throw new v("No enabled authentication provider was returned by /_elsa/identity/bootstrap.");
    const n = this.resolveProviderAdapter(r);
    return this.activeAdapter = n, n;
  }
  async getProviderAdapter(e) {
    const r = this.adapters.get(e);
    if (r)
      return r;
    const s = (await this.options.bootstrap()).providers.find((i) => i.enabled && i.id === e);
    if (!s)
      throw new v(`No auth provider adapter is registered for '${e}'.`);
    return this.resolveProviderAdapter(s);
  }
  resolveProviderAdapter(e) {
    const r = this.adapters.get(e.id);
    if (r)
      return r;
    if (!this.options.adapterFactory)
      throw new v(`No auth provider adapter is registered for '${e.id}'.`);
    const n = this.options.adapterFactory(e);
    return this.adapters.set(e.id, n), n;
  }
  getCallbackProviderId() {
    const e = this.options.getCallbackProviderId?.();
    return e || (this.pendingLoginProviderId ? this.pendingLoginProviderId : typeof window > "u" ? null : new URLSearchParams(window.location.search).get("authProviderId"));
  }
  async applySession(e, r) {
    const n = e.provider?.id ? await this.getProviderAdapter(e.provider.id) : r;
    this.session = e, this.activeAdapter = n;
  }
}
class v extends Error {
  constructor(e) {
    super(e), this.name = "AuthConfigurationError";
  }
}
function H(t) {
  const e = t.fetch ?? fetch, r = t.sessionEndpoint ?? "/_elsa/identity/session", n = t.logoutEndpoint ?? `/_elsa/identity/logout/${encodeURIComponent(t.id)}`;
  return {
    id: t.id,
    kind: t.kind,
    initialize: () => k(e, r, t),
    login: (s) => {
      const i = t.challenge;
      if (!i || i.type === "none")
        throw new A(`Provider '${t.id}' does not expose a redirect challenge.`);
      const o = "method" in i ? i.method.toUpperCase() : "GET";
      if (o !== "GET")
        throw new A(`Provider '${t.id}' exposes an unsupported ${o} challenge.`);
      const c = new URL(Q(i), x(t)), a = s?.returnUrl ?? t.location?.href ?? window.location.href;
      return c.searchParams.set("returnUrl", V(a, s?.providerId ?? t.id, t)), (t.location ?? window.location).assign(c.toString()), Promise.resolve();
    },
    handleCallback: () => k(e, r, t),
    logout: async () => {
      const s = await e(S(n, t), { method: "POST", credentials: "include" });
      if (!s.ok)
        throw new A(`Sign-out failed with ${s.status}.`);
    },
    getAccessToken: async () => {
      if (!t.tokenEndpoint)
        return null;
      const s = await e(S(t.tokenEndpoint, t), { credentials: "include", cache: "no-store" });
      if (s.status === 401)
        return null;
      if (!s.ok)
        throw new A(`Access-token request failed with ${s.status}.`);
      const i = await s.json();
      return typeof i.accessToken == "string" ? i.accessToken : null;
    },
    refresh: async () => {
      const s = await t.getRefreshToken?.(), i = t.refreshEndpoint;
      if (!i || !s)
        return k(e, r, t);
      const o = await e(S(i, t), {
        method: "POST",
        credentials: "include",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json"
        },
        body: JSON.stringify({ refreshToken: s })
      });
      if (o.status === 401)
        return P;
      if (!o.ok)
        throw new A(`Session refresh failed with ${o.status}.`);
      const c = await o.json();
      return c.status ? O(c) : k(e, r, t);
    }
  };
}
async function k(t, e, r) {
  const n = await t(S(e, r), { credentials: "include", cache: "no-store" });
  if (n.status === 401)
    return P;
  if (!n.ok)
    throw new A(`Session request failed with ${n.status}.`);
  return G(n);
}
async function G(t) {
  const e = await t.json();
  return O(e);
}
function O(t) {
  const e = Y(t.status) ? t.status : "anonymous";
  return {
    ...t,
    status: e,
    roles: $(t.roles),
    permissions: $(t.permissions)
  };
}
function $(t) {
  return Array.isArray(t) ? t.filter((e) => typeof e == "string") : [];
}
function Y(t) {
  return t === "unknown" || t === "anonymous" || t === "authenticated";
}
function Q(t) {
  return "loginPath" in t ? t.loginPath : t.url;
}
function S(t, e) {
  return new URL(t, x(e)).toString();
}
function x(t) {
  return t?.baseUrl ?? t?.location?.origin ?? window.location.origin;
}
function V(t, e, r) {
  const n = new URL(t, W(r));
  return n.searchParams.set("authProviderId", e), X(t) ? `${n.pathname}${n.search}${n.hash}` : n.toString();
}
function W(t) {
  return t?.location?.href ?? (typeof window < "u" ? window.location.href : void 0) ?? t?.location?.origin ?? x(t);
}
function X(t) {
  try {
    return new URL(t), !1;
  } catch {
    return !0;
  }
}
class A extends Error {
  constructor(e) {
    super(e), this.name = "AuthAdapterError";
  }
}
function At(t = {}) {
  const e = t.baseUrl ?? window.location.origin, r = t.fetch ?? fetch;
  return B({
    bootstrap: () => j(r, e, "/_elsa/identity/bootstrap"),
    capabilities: () => j(r, e, "/_elsa/identity/capabilities"),
    isCallback: t.isCallback,
    getCallbackProviderId: t.getCallbackProviderId,
    adapterFactory: (n) => H({
      id: n.id,
      kind: n.kind,
      baseUrl: e,
      challenge: n.challenge,
      fetch: r
    })
  });
}
async function j(t, e, r) {
  const n = await t(new URL(r, e).toString(), {
    credentials: "include",
    cache: "no-store",
    headers: { Accept: "application/json" }
  });
  if (!n.ok)
    throw new Error(`Auth discovery request failed with ${n.status}.`);
  return await n.json();
}
var m = { exports: {} }, g = {};
var U;
function Z() {
  if (U) return g;
  U = 1;
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
  return g.Fragment = e, g.jsx = r, g.jsxs = r, g;
}
var J;
function K() {
  return J || (J = 1, m.exports = Z()), m.exports;
}
var f = K();
const z = u.createContext(null);
function R() {
  const t = u.useContext(z);
  if (!t)
    throw new Error("Auth SDK hooks must be used within <AuthProvider>.");
  return t;
}
function gt({ manager: t, children: e }) {
  const [r, n] = u.useState(() => t.getSession() ?? F), [s, i] = u.useState(null), o = u.useRef(!1), c = u.useRef(0), a = u.useCallback((d) => o.current && c.current === d, []);
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
        a(d) && (console.error("Auth initialization failed.", l), n(P), i(null));
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
  }, [t, a]), T = u.useCallback(async () => {
    const d = ++c.current;
    await t.logout(), a(d) && (n(t.getSession()), i(null));
  }, [t, a]), E = u.useCallback(async () => {
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
  }, [t, a]), N = u.useMemo(() => ({
    session: r,
    capabilities: s,
    login: w,
    logout: T,
    refresh: E
  }), [s, w, T, E, r]);
  return /* @__PURE__ */ f.jsx(z.Provider, { value: N, children: e });
}
function tt() {
  return R().session;
}
function et() {
  const { permissions: t } = tt();
  return u.useMemo(() => {
    const e = new Set(t);
    return {
      has: (r) => e.has(r),
      hasAny: (r) => r.some((n) => e.has(n)),
      hasAll: (r) => r.every((n) => e.has(n))
    };
  }, [t]);
}
function yt() {
  return R().capabilities;
}
function vt({ requires: t, requireAll: e = !0, fallback: r = null, children: n }) {
  const s = et(), i = typeof t == "string" ? [t] : t ?? [];
  return i.length === 0 || (e ? s.hasAll(i) : s.hasAny(i)) ? /* @__PURE__ */ f.jsx(f.Fragment, { children: n }) : /* @__PURE__ */ f.jsx(f.Fragment, { children: r });
}
function kt({ children: t, fallback: e = null, loginOptions: r }) {
  const { session: n, login: s } = R(), i = u.useRef(null);
  return u.useEffect(() => {
    if (n.status === "anonymous") {
      const o = rt(r), c = i.current;
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
function rt(t) {
  return `${t?.providerId ?? ""}
${t?.returnUrl ?? ""}`;
}
function St(t) {
  return H({
    ...t,
    kind: "external-oidc"
  });
}
function bt(t, e, r = {}) {
  return {
    getJson(n, s) {
      return I(t, n, e, r, st(s));
    },
    postJson(n, s, i) {
      return I(t, n, e, r, {
        ...i,
        method: "POST",
        headers: it(i?.headers),
        body: JSON.stringify(s)
      });
    }
  };
}
async function I(t, e, r, n, s) {
  const i = n.fetch ?? fetch, o = new URL(e, t).toString(), c = await i(o, await _(r, q(n, s))), a = c.status === 401 && n.refreshOnUnauthorized !== !1 ? await nt(i, o, r, q(n, s)) : c;
  if (!a.ok)
    throw new b(a.status, await M(a));
  const w = await a.text();
  try {
    return JSON.parse(w);
  } catch {
    throw new b(a.status, `Expected JSON from ${o}.`);
  }
}
async function nt(t, e, r, n) {
  return (await r.refresh()).status !== "authenticated" ? new Response("Authentication required.", { status: 401 }) : t(e, await _(r, n));
}
async function _(t, e) {
  const r = new Headers(e?.headers), n = await t.getAccessToken();
  return n && r.set("Authorization", `Bearer ${n}`), {
    ...e,
    credentials: e?.credentials ?? "include",
    headers: r
  };
}
function st(t) {
  const e = new Headers(t?.headers);
  return e.has("Accept") || e.set("Accept", "application/json"), {
    ...t,
    cache: t?.cache ?? "no-store",
    headers: e
  };
}
function it(t) {
  const e = new Headers(t);
  return e.has("Content-Type") || e.set("Content-Type", "application/json"), e.has("Accept") || e.set("Accept", "application/json"), e;
}
function q(t, e) {
  return C(t.defaultHeaders ?? t.headers, e);
}
function at(t, e = {}) {
  return async () => await t.getAccessToken() ?? await e.fallbackAccessTokenFactory?.() ?? e.anonymousToken ?? "";
}
function Pt(t, e) {
  const r = ot(t.accessTokenFactory) ? t.accessTokenFactory.bind(t) : void 0;
  return {
    ...t,
    accessTokenFactory: at(e, { fallbackAccessTokenFactory: r })
  };
}
function ot(t) {
  return typeof t == "function";
}
function y() {
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
function mt() {
  const t = /* @__PURE__ */ new Set();
  return {
    contextProviders: y(),
    promptActions: y(),
    tools: y(),
    proposalRenderers: y(),
    surfaces: y(),
    dispatchPrompt(e) {
      for (const r of t)
        r(e);
    },
    onPrompt(e) {
      return t.add(e), () => t.delete(e);
    }
  };
}
function Ct(t, e = {}) {
  return {
    baseUrl: t,
    headers: e.headers,
    http: ct(t, e.headers)
  };
}
function ct(t, e) {
  return {
    async getJson(r, n) {
      return L(t, r, C(e, ht(n)));
    },
    async postJson(r, n, s) {
      return L(t, r, C(e, {
        ...s,
        method: "POST",
        headers: ft(s?.headers),
        body: JSON.stringify(n)
      }));
    }
  };
}
function C(t, e = {}) {
  return t ? {
    ...e,
    headers: ut(t, e.headers)
  } : e;
}
function ut(t, e) {
  const r = new Headers(t);
  return new Headers(e).forEach((n, s) => r.set(s, n)), r;
}
async function L(t, e, r) {
  const n = lt(t, e), s = await fetch(n, r);
  if (!s.ok)
    throw new b(s.status, await M(s));
  const i = await s.text();
  try {
    return JSON.parse(i);
  } catch {
    throw new b(
      s.status,
      `Expected JSON from ${n}, but received ${pt(s, i)}. Check Studio:BackendBaseUrl and make sure the backend maps this API route.`
    );
  }
}
async function M(t) {
  const e = t.headers.get("content-type") ?? "";
  if (e.includes("application/json") || e.includes("+json"))
    try {
      const n = await t.json();
      return dt(n) ?? `Request failed with ${t.status}.`;
    } catch {
      return `Request failed with ${t.status}.`;
    }
  return (await t.text()).trim() || `Request failed with ${t.status}.`;
}
function dt(t) {
  if (typeof t.detail == "string" && t.detail.length > 0) return t.detail;
  if (typeof t.title == "string" && t.title.length > 0) return t.title;
  if (Array.isArray(t.errors) && t.errors.length > 0) return t.errors.map(String).join(" ");
  if (t.errors && typeof t.errors == "object") {
    const e = Object.values(t.errors).flatMap((r) => Array.isArray(r) ? r : [r]).map(String);
    if (e.length > 0) return e.join(" ");
  }
  return null;
}
function lt(t, e) {
  return new URL(e, t).toString();
}
function ht(t) {
  const e = new Headers(t?.headers);
  return e.has("Accept") || e.set("Accept", "application/json"), {
    ...t,
    cache: t?.cache ?? "no-store",
    headers: e
  };
}
function ft(t) {
  const e = new Headers(t);
  return e.has("Content-Type") || e.set("Content-Type", "application/json"), e.has("Accept") || e.set("Accept", "application/json"), e;
}
function pt(t, e) {
  const r = t.headers.get("content-type") ?? "an unknown content type", n = e.trim(), s = n.length > 0 ? `: ${n.slice(0, 80)}` : "";
  return `${r}${s}`;
}
class b extends Error {
  constructor(e, r) {
    super(r), this.status = e, this.name = "StudioHttpError";
  }
  status;
}
export {
  A as AuthAdapterError,
  v as AuthConfigurationError,
  vt as AuthGuard,
  gt as AuthProvider,
  kt as RequireAuth,
  b as StudioHttpError,
  mt as createAiContributionApi,
  B as createAuthProviderManager,
  bt as createAuthenticatedHttpClient,
  At as createBackendAuthProviderManager,
  y as createContributionRegistry,
  Ct as createEndpointContext,
  ct as createHttpClient,
  St as createOidcAuthAdapter,
  H as createRedirectAuthAdapter,
  at as createSignalRAccessTokenFactory,
  M as readStudioHttpErrorMessage,
  yt as useAuthCapabilities,
  R as useAuthContext,
  tt as useAuthSession,
  et as usePermissions,
  Pt as withAuthenticatedSignalROptions,
  C as withDefaultHeaders
};
