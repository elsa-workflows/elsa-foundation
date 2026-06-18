import { r as c } from "../vendor/chunks/index.js";
const k = {
  status: "anonymous",
  roles: [],
  permissions: []
}, U = {
  status: "unknown",
  roles: [],
  permissions: []
};
function z(t) {
  return new _(t);
}
class _ {
  constructor(e) {
    this.options = e;
    for (const r of e.adapters ?? []) {
      if (this.adapters.has(r.id))
        throw new A(`Duplicate auth provider adapter '${r.id}'.`);
      this.adapters.set(r.id, r);
    }
  }
  options;
  adapters = /* @__PURE__ */ new Map();
  activeAdapter = null;
  pendingLoginProviderId = null;
  session = U;
  getSession() {
    return this.session;
  }
  getCapabilities() {
    return this.options.capabilities();
  }
  async initialize() {
    if (this.options.isCallback?.()) {
      const r = this.getCallbackProviderId(), n = r ? await this.getProviderAdapter(r) : await this.resolveActiveAdapter();
      return this.session = await n.handleCallback(), this.activeAdapter = n, this.pendingLoginProviderId = null, this.session;
    }
    const e = await this.resolveActiveAdapter();
    return this.session = await e.initialize(), this.pendingLoginProviderId = null, this.session;
  }
  async login(e) {
    const r = e?.providerId ? await this.getProviderAdapter(e.providerId) : await this.resolveActiveAdapter();
    this.pendingLoginProviderId = r.id;
    try {
      const n = await r.login({ ...e, providerId: r.id });
      n ? (this.session = n, this.activeAdapter = r, this.pendingLoginProviderId = null) : this.session.status !== "authenticated" && (this.activeAdapter = r);
    } catch (n) {
      throw this.pendingLoginProviderId = null, n;
    }
  }
  async handleCallback(e) {
    const r = e ? await this.getProviderAdapter(e) : await this.resolveActiveAdapter();
    return this.session = await r.handleCallback(), this.activeAdapter = r, this.pendingLoginProviderId = null, this.session;
  }
  async logout() {
    await (await this.resolveActiveAdapter()).logout(), this.session = k;
  }
  async getAccessToken() {
    return (await this.resolveActiveAdapter()).getAccessToken();
  }
  async refresh() {
    const e = await this.resolveActiveAdapter();
    return this.session = await e.refresh(), this.session;
  }
  async resolveActiveAdapter() {
    if (this.activeAdapter)
      return this.activeAdapter;
    const e = await this.options.bootstrap(), r = e.providers.find((s) => s.enabled && s.isDefault) ?? e.providers.find((s) => s.enabled);
    if (!r)
      throw new A("No enabled authentication provider was returned by /_elsa/identity/bootstrap.");
    const n = this.resolveProviderAdapter(r);
    return this.activeAdapter = n, n;
  }
  async getProviderAdapter(e) {
    const r = this.adapters.get(e);
    if (r)
      return r;
    const s = (await this.options.bootstrap()).providers.find((i) => i.enabled && i.id === e);
    if (!s)
      throw new A(`No auth provider adapter is registered for '${e}'.`);
    return this.resolveProviderAdapter(s);
  }
  resolveProviderAdapter(e) {
    const r = this.adapters.get(e.id);
    if (r)
      return r;
    if (!this.options.adapterFactory)
      throw new A(`No auth provider adapter is registered for '${e.id}'.`);
    const n = this.options.adapterFactory(e);
    return this.adapters.set(e.id, n), n;
  }
  getCallbackProviderId() {
    const e = this.options.getCallbackProviderId?.();
    return e || (this.pendingLoginProviderId ? this.pendingLoginProviderId : typeof window > "u" ? null : new URLSearchParams(window.location.search).get("authProviderId"));
  }
}
class A extends Error {
  constructor(e) {
    super(e), this.name = "AuthConfigurationError";
  }
}
function q(t) {
  const e = t.fetch ?? fetch, r = t.sessionEndpoint ?? "/_elsa/identity/session", n = t.logoutEndpoint ?? `/_elsa/identity/logout/${encodeURIComponent(t.id)}`;
  return {
    id: t.id,
    kind: t.kind,
    initialize: () => g(e, r, t),
    login: (s) => {
      const i = t.challenge;
      if (!i || i.type === "none")
        throw new f(`Provider '${t.id}' does not expose a redirect challenge.`);
      const a = "method" in i ? i.method.toUpperCase() : "GET";
      if (a !== "GET")
        throw new f(`Provider '${t.id}' exposes an unsupported ${a} challenge.`);
      const o = new URL(D(i), m(t)), d = s?.returnUrl ?? t.location?.href ?? window.location.href;
      return o.searchParams.set("returnUrl", B(d, s?.providerId ?? t.id, t)), (t.location ?? window.location).assign(o.toString()), Promise.resolve();
    },
    handleCallback: () => g(e, r, t),
    logout: async () => {
      const s = await e(v(n, t), { method: "POST", credentials: "include" });
      if (!s.ok)
        throw new f(`Sign-out failed with ${s.status}.`);
    },
    getAccessToken: async () => {
      if (!t.tokenEndpoint)
        return null;
      const s = await e(v(t.tokenEndpoint, t), { credentials: "include", cache: "no-store" });
      if (s.status === 401)
        return null;
      if (!s.ok)
        throw new f(`Access-token request failed with ${s.status}.`);
      const i = await s.json();
      return typeof i.accessToken == "string" ? i.accessToken : null;
    },
    refresh: async () => {
      const s = await t.getRefreshToken?.(), i = t.refreshEndpoint;
      if (!i || !s)
        return g(e, r, t);
      const a = await e(v(i, t), {
        method: "POST",
        credentials: "include",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json"
        },
        body: JSON.stringify({ refreshToken: s })
      });
      if (a.status === 401)
        return k;
      if (!a.ok)
        throw new f(`Session refresh failed with ${a.status}.`);
      const o = await a.json();
      return o.status ? L(o) : g(e, r, t);
    }
  };
}
async function g(t, e, r) {
  const n = await t(v(e, r), { credentials: "include", cache: "no-store" });
  if (n.status === 401)
    return k;
  if (!n.ok)
    throw new f(`Session request failed with ${n.status}.`);
  return M(n);
}
async function M(t) {
  const e = await t.json();
  return L(e);
}
function L(t) {
  const e = N(t.status) ? t.status : "anonymous";
  return {
    ...t,
    status: e,
    roles: T(t.roles),
    permissions: T(t.permissions)
  };
}
function T(t) {
  return Array.isArray(t) ? t.filter((e) => typeof e == "string") : [];
}
function N(t) {
  return t === "unknown" || t === "anonymous" || t === "authenticated";
}
function D(t) {
  return "loginPath" in t ? t.loginPath : t.url;
}
function v(t, e) {
  return new URL(t, m(e)).toString();
}
function m(t) {
  return t?.baseUrl ?? t?.location?.origin ?? window.location.origin;
}
function B(t, e, r) {
  const n = new URL(t, r?.location?.origin ?? m(r));
  return n.searchParams.set("authProviderId", e), G(t) ? `${n.pathname}${n.search}${n.hash}` : n.toString();
}
function G(t) {
  return t.startsWith("/");
}
class f extends Error {
  constructor(e) {
    super(e), this.name = "AuthAdapterError";
  }
}
function dt(t = {}) {
  const e = t.baseUrl ?? window.location.origin, r = t.fetch ?? fetch;
  return z({
    bootstrap: () => R(r, e, "/_elsa/identity/bootstrap"),
    capabilities: () => R(r, e, "/_elsa/identity/capabilities"),
    isCallback: t.isCallback,
    getCallbackProviderId: t.getCallbackProviderId,
    adapterFactory: (n) => q({
      id: n.id,
      kind: n.kind,
      baseUrl: e,
      challenge: n.challenge,
      fetch: r
    })
  });
}
async function R(t, e, r) {
  const n = await t(new URL(r, e).toString(), {
    credentials: "include",
    cache: "no-store",
    headers: { Accept: "application/json" }
  });
  if (!n.ok)
    throw new Error(`Auth discovery request failed with ${n.status}.`);
  return await n.json();
}
var P = { exports: {} }, p = {};
var E;
function Y() {
  if (E) return p;
  E = 1;
  var t = /* @__PURE__ */ Symbol.for("react.transitional.element"), e = /* @__PURE__ */ Symbol.for("react.fragment");
  function r(n, s, i) {
    var a = null;
    if (i !== void 0 && (a = "" + i), s.key !== void 0 && (a = "" + s.key), "key" in s) {
      i = {};
      for (var o in s)
        o !== "key" && (i[o] = s[o]);
    } else i = s;
    return s = i.ref, {
      $$typeof: t,
      type: n,
      key: a,
      ref: s !== void 0 ? s : null,
      props: i
    };
  }
  return p.Fragment = e, p.jsx = r, p.jsxs = r, p;
}
var j;
function W() {
  return j || (j = 1, P.exports = Y()), P.exports;
}
var l = W();
const F = c.createContext(null);
function x() {
  const t = c.useContext(F);
  if (!t)
    throw new Error("Auth SDK hooks must be used within <AuthProvider>.");
  return t;
}
function lt({ manager: t, children: e }) {
  const [r, n] = c.useState(() => t.getSession() ?? U), [s, i] = c.useState(null);
  c.useEffect(() => {
    let u = !1;
    async function w() {
      try {
        const h = await t.initialize();
        if (u)
          return;
        if (n(h), h.status !== "authenticated") {
          i(null);
          return;
        }
        try {
          const b = await t.getCapabilities();
          u || i(b);
        } catch (b) {
          u || (console.error("Auth capabilities request failed.", b), i(null));
        }
      } catch (h) {
        u || (console.error("Auth initialization failed.", h), n(k), i(null));
      }
    }
    return w(), () => {
      u = !0;
    };
  }, [t]);
  const a = c.useCallback(async (u) => {
    await t.login(u);
    const w = t.getSession();
    if (n(w), w.status === "authenticated")
      try {
        i(await t.getCapabilities());
      } catch (h) {
        console.error("Auth capabilities request failed.", h), i(null);
      }
    else
      i(null);
  }, [t]), o = c.useCallback(async () => {
    await t.logout(), n(t.getSession()), i(null);
  }, [t]), d = c.useCallback(async () => {
    const u = await t.refresh();
    return n(u), u.status === "authenticated" ? i(await t.getCapabilities()) : i(null), u;
  }, [t]), S = c.useMemo(() => ({
    session: r,
    capabilities: s,
    login: a,
    logout: o,
    refresh: d
  }), [s, a, o, d, r]);
  return /* @__PURE__ */ l.jsx(F.Provider, { value: S, children: e });
}
function Q() {
  return x().session;
}
function V() {
  const { permissions: t } = Q();
  return c.useMemo(() => {
    const e = new Set(t);
    return {
      has: (r) => e.has(r),
      hasAny: (r) => r.some((n) => e.has(n)),
      hasAll: (r) => r.every((n) => e.has(n))
    };
  }, [t]);
}
function ht() {
  return x().capabilities;
}
function ft({ requires: t, requireAll: e = !0, fallback: r = null, children: n }) {
  const s = V(), i = typeof t == "string" ? [t] : t ?? [];
  return i.length === 0 || (e ? s.hasAll(i) : s.hasAny(i)) ? /* @__PURE__ */ l.jsx(l.Fragment, { children: n }) : /* @__PURE__ */ l.jsx(l.Fragment, { children: r });
}
function pt({ children: t, fallback: e = null, loginOptions: r }) {
  const { session: n, login: s } = x(), i = c.useRef(!1);
  return c.useEffect(() => {
    if (n.status === "anonymous") {
      if (i.current)
        return;
      i.current = !0, s(r).catch((a) => {
        i.current = !1, console.error("Auth login failed.", a);
      });
    } else
      i.current = !1;
  }, [s, r, n.status]), n.status !== "authenticated" ? /* @__PURE__ */ l.jsx(l.Fragment, { children: e }) : /* @__PURE__ */ l.jsx(l.Fragment, { children: t });
}
function wt(t) {
  return q({
    ...t,
    kind: "external-oidc"
  });
}
function At(t, e, r = {}) {
  return {
    getJson(n, s) {
      return $(t, n, e, r, Z(s));
    },
    postJson(n, s, i) {
      return $(t, n, e, r, {
        ...i,
        method: "POST",
        headers: K(i?.headers),
        body: JSON.stringify(s)
      });
    }
  };
}
async function $(t, e, r, n, s) {
  const i = n.fetch ?? fetch, a = new URL(e, t).toString(), o = await i(a, await H(r, J(n, s))), d = o.status === 401 && n.refreshOnUnauthorized !== !1 ? await X(i, a, r, J(n, s)) : o;
  if (!d.ok)
    throw new y(d.status, await O(d));
  const S = await d.text();
  try {
    return JSON.parse(S);
  } catch {
    throw new y(d.status, `Expected JSON from ${a}.`);
  }
}
async function X(t, e, r, n) {
  return (await r.refresh()).status !== "authenticated" ? new Response("Authentication required.", { status: 401 }) : t(e, await H(r, n));
}
async function H(t, e) {
  const r = new Headers(e?.headers), n = await t.getAccessToken();
  return n && r.set("Authorization", `Bearer ${n}`), {
    ...e,
    credentials: e?.credentials ?? "include",
    headers: r
  };
}
function Z(t) {
  const e = new Headers(t?.headers);
  return e.has("Accept") || e.set("Accept", "application/json"), {
    ...t,
    cache: t?.cache ?? "no-store",
    headers: e
  };
}
function K(t) {
  const e = new Headers(t);
  return e.has("Content-Type") || e.set("Content-Type", "application/json"), e.has("Accept") || e.set("Accept", "application/json"), e;
}
function J(t, e) {
  return C(t.defaultHeaders ?? t.headers, e);
}
function tt(t, e = {}) {
  return async () => await t.getAccessToken() ?? await e.fallbackAccessTokenFactory?.() ?? e.anonymousToken ?? "";
}
function gt(t, e) {
  const r = et(t.accessTokenFactory) ? t.accessTokenFactory : void 0;
  return {
    ...t,
    accessTokenFactory: tt(e, { fallbackAccessTokenFactory: r })
  };
}
function et(t) {
  return typeof t == "function";
}
function vt() {
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
function yt(t, e = {}) {
  return {
    baseUrl: t,
    headers: e.headers,
    http: rt(t, e.headers)
  };
}
function rt(t, e) {
  return {
    async getJson(r, n) {
      return I(t, r, C(e, at(n)));
    },
    async postJson(r, n, s) {
      return I(t, r, C(e, {
        ...s,
        method: "POST",
        headers: ot(s?.headers),
        body: JSON.stringify(n)
      }));
    }
  };
}
function C(t, e = {}) {
  return t ? {
    ...e,
    headers: nt(t, e.headers)
  } : e;
}
function nt(t, e) {
  const r = new Headers(t);
  return new Headers(e).forEach((n, s) => r.set(s, n)), r;
}
async function I(t, e, r) {
  const n = it(t, e), s = await fetch(n, r);
  if (!s.ok)
    throw new y(s.status, await O(s));
  const i = await s.text();
  try {
    return JSON.parse(i);
  } catch {
    throw new y(
      s.status,
      `Expected JSON from ${n}, but received ${ct(s, i)}. Check Studio:BackendBaseUrl and make sure the backend maps this API route.`
    );
  }
}
async function O(t) {
  const e = t.headers.get("content-type") ?? "";
  if (e.includes("application/json") || e.includes("+json"))
    try {
      const n = await t.json();
      return st(n) ?? `Request failed with ${t.status}.`;
    } catch {
      return `Request failed with ${t.status}.`;
    }
  return (await t.text()).trim() || `Request failed with ${t.status}.`;
}
function st(t) {
  if (typeof t.detail == "string" && t.detail.length > 0) return t.detail;
  if (typeof t.title == "string" && t.title.length > 0) return t.title;
  if (Array.isArray(t.errors) && t.errors.length > 0) return t.errors.map(String).join(" ");
  if (t.errors && typeof t.errors == "object") {
    const e = Object.values(t.errors).flatMap((r) => Array.isArray(r) ? r : [r]).map(String);
    if (e.length > 0) return e.join(" ");
  }
  return null;
}
function it(t, e) {
  return new URL(e, t).toString();
}
function at(t) {
  const e = new Headers(t?.headers);
  return e.has("Accept") || e.set("Accept", "application/json"), {
    ...t,
    cache: t?.cache ?? "no-store",
    headers: e
  };
}
function ot(t) {
  const e = new Headers(t);
  return e.has("Content-Type") || e.set("Content-Type", "application/json"), e.has("Accept") || e.set("Accept", "application/json"), e;
}
function ct(t, e) {
  const r = t.headers.get("content-type") ?? "an unknown content type", n = e.trim(), s = n.length > 0 ? `: ${n.slice(0, 80)}` : "";
  return `${r}${s}`;
}
class y extends Error {
  constructor(e, r) {
    super(r), this.status = e, this.name = "StudioHttpError";
  }
  status;
}
export {
  f as AuthAdapterError,
  A as AuthConfigurationError,
  ft as AuthGuard,
  lt as AuthProvider,
  pt as RequireAuth,
  y as StudioHttpError,
  z as createAuthProviderManager,
  At as createAuthenticatedHttpClient,
  dt as createBackendAuthProviderManager,
  vt as createContributionRegistry,
  yt as createEndpointContext,
  rt as createHttpClient,
  wt as createOidcAuthAdapter,
  q as createRedirectAuthAdapter,
  tt as createSignalRAccessTokenFactory,
  O as readStudioHttpErrorMessage,
  ht as useAuthCapabilities,
  x as useAuthContext,
  Q as useAuthSession,
  V as usePermissions,
  gt as withAuthenticatedSignalROptions,
  C as withDefaultHeaders
};
