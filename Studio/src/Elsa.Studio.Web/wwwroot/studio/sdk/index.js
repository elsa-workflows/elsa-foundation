import { r as c } from "../vendor/chunks/index.js";
const g = {
  status: "anonymous",
  roles: [],
  permissions: []
}, T = {
  status: "unknown",
  roles: [],
  permissions: []
};
function K(t) {
  return new q(t);
}
class q {
  constructor(e) {
    this.options = e;
    for (const s of e.adapters) {
      if (this.adapters.has(s.id))
        throw new A(`Duplicate auth provider adapter '${s.id}'.`);
      this.adapters.set(s.id, s);
    }
  }
  options;
  adapters = /* @__PURE__ */ new Map();
  activeAdapter = null;
  session = T;
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
    await (await this.resolveAdapter()).logout(), this.session = g;
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
    const s = await this.options.bootstrap(), r = s.providers.find((n) => n.enabled && n.isDefault) ?? s.providers.find((n) => n.enabled);
    if (!r)
      throw new A("No enabled authentication provider was returned by /_elsa/identity/bootstrap.");
    return this.activeAdapter = this.requireAdapter(r.id), this.activeAdapter;
  }
  requireAdapter(e) {
    const s = this.adapters.get(e);
    if (!s)
      throw new A(`No auth provider adapter is registered for '${e}'.`);
    return this.activeAdapter = s, s;
  }
}
class A extends Error {
  constructor(e) {
    super(e), this.name = "AuthConfigurationError";
  }
}
var y = { exports: {} }, l = {};
var v;
function O() {
  if (v) return l;
  v = 1;
  var t = /* @__PURE__ */ Symbol.for("react.transitional.element"), e = /* @__PURE__ */ Symbol.for("react.fragment");
  function s(r, n, i) {
    var a = null;
    if (i !== void 0 && (a = "" + i), n.key !== void 0 && (a = "" + n.key), "key" in n) {
      i = {};
      for (var o in n)
        o !== "key" && (i[o] = n[o]);
    } else i = n;
    return n = i.ref, {
      $$typeof: t,
      type: r,
      key: a,
      ref: n !== void 0 ? n : null,
      props: i
    };
  }
  return l.Fragment = e, l.jsx = s, l.jsxs = s, l;
}
var S;
function H() {
  return S || (S = 1, y.exports = O()), y.exports;
}
var h = H();
const b = c.createContext(null);
function x() {
  const t = c.useContext(b);
  if (!t)
    throw new Error("Auth SDK hooks must be used within <AuthProvider>.");
  return t;
}
function tt({ manager: t, children: e }) {
  const [s, r] = c.useState(() => t.getSession() ?? T), [n, i] = c.useState(null);
  c.useEffect(() => {
    let d = !1;
    async function J() {
      const k = await t.initialize();
      if (!d && (r(k), k.status === "authenticated")) {
        const P = await t.getCapabilities();
        d || i(P);
      }
    }
    return J(), () => {
      d = !0;
    };
  }, [t]);
  const a = c.useCallback((d) => t.login(d), [t]), o = c.useCallback(async () => {
    await t.logout(), r(t.getSession()), i(null);
  }, [t]), u = c.useCallback(async () => {
    const d = await t.refresh();
    return r(d), d.status === "authenticated" ? i(await t.getCapabilities()) : i(null), d;
  }, [t]), w = c.useMemo(() => ({
    session: s,
    capabilities: n,
    login: a,
    logout: o,
    refresh: u
  }), [n, a, o, u, s]);
  return /* @__PURE__ */ h.jsx(b.Provider, { value: w, children: e });
}
function M() {
  return x().session;
}
function _() {
  const { permissions: t } = M();
  return c.useMemo(() => {
    const e = new Set(t);
    return {
      has: (s) => e.has(s),
      hasAny: (s) => s.some((r) => e.has(r)),
      hasAll: (s) => s.every((r) => e.has(r))
    };
  }, [t]);
}
function et() {
  return x().capabilities;
}
function st({ requires: t, requireAll: e = !0, fallback: s = null, children: r }) {
  const n = _(), i = typeof t == "string" ? [t] : t ?? [];
  return i.length === 0 || (e ? n.hasAll(i) : n.hasAny(i)) ? /* @__PURE__ */ h.jsx(h.Fragment, { children: r }) : /* @__PURE__ */ h.jsx(h.Fragment, { children: s });
}
function nt({ children: t, fallback: e = null, loginOptions: s }) {
  const { session: r, login: n } = x();
  return c.useEffect(() => {
    r.status === "anonymous" && n(s);
  }, [n, s, r.status]), r.status !== "authenticated" ? /* @__PURE__ */ h.jsx(h.Fragment, { children: e }) : /* @__PURE__ */ h.jsx(h.Fragment, { children: t });
}
function z(t) {
  const e = t.fetch ?? fetch, s = t.sessionEndpoint ?? "/_elsa/identity/session", r = t.logoutEndpoint ?? "/_elsa/identity/logout", n = t.refreshEndpoint ?? "/_elsa/identity/refresh";
  return {
    id: t.id,
    kind: t.kind,
    initialize: () => m(e, s),
    login: (i) => {
      const a = t.challenge;
      if (!a || a.type !== "redirect")
        throw new f(`Provider '${t.id}' does not expose a redirect challenge.`);
      const o = new URL(a.loginPath, t.location?.origin ?? window.location.origin), u = i?.returnUrl ?? t.location?.href ?? window.location.href;
      return o.searchParams.set("returnUrl", u), (t.location ?? window.location).assign(o.toString()), Promise.resolve();
    },
    handleCallback: () => m(e, s),
    logout: async () => {
      const i = await e(r, { method: "POST", credentials: "include" });
      if (!i.ok)
        throw new f(`Sign-out failed with ${i.status}.`);
    },
    getAccessToken: async () => {
      if (!t.tokenEndpoint)
        return null;
      const i = await e(t.tokenEndpoint, { credentials: "include", cache: "no-store" });
      if (i.status === 401)
        return null;
      if (!i.ok)
        throw new f(`Access-token request failed with ${i.status}.`);
      const a = await i.json();
      return typeof a.accessToken == "string" ? a.accessToken : null;
    },
    refresh: async () => {
      const i = await e(n, { method: "POST", credentials: "include" });
      if (i.status === 401)
        return g;
      if (!i.ok)
        throw new f(`Session refresh failed with ${i.status}.`);
      return j(i);
    }
  };
}
async function m(t, e) {
  const s = await t(e, { credentials: "include", cache: "no-store" });
  if (s.status === 401)
    return g;
  if (!s.ok)
    throw new f(`Session request failed with ${s.status}.`);
  return j(s);
}
async function j(t) {
  const e = await t.json();
  return {
    ...e,
    roles: e.roles ?? [],
    permissions: e.permissions ?? []
  };
}
class f extends Error {
  constructor(e) {
    super(e), this.name = "AuthAdapterError";
  }
}
function rt(t) {
  return z({
    ...t,
    kind: "external-oidc"
  });
}
function it(t, e, s = {}) {
  return {
    getJson(r, n) {
      return E(t, r, e, s, U(n));
    },
    postJson(r, n, i) {
      return E(t, r, e, s, {
        ...i,
        method: "POST",
        headers: D(i?.headers),
        body: JSON.stringify(n)
      });
    }
  };
}
async function E(t, e, s, r, n) {
  const i = r.fetch ?? fetch, a = new URL(e, t).toString(), o = await i(a, await $(s, n)), u = o.status === 401 && r.refreshOnUnauthorized !== !1 ? await F(i, a, s, n) : o;
  if (!u.ok)
    throw new p(u.status, await N(u));
  const w = await u.text();
  try {
    return JSON.parse(w);
  } catch {
    throw new p(u.status, `Expected JSON from ${a}.`);
  }
}
async function F(t, e, s, r) {
  return (await s.refresh()).status !== "authenticated" ? new Response("Authentication required.", { status: 401 }) : t(e, await $(s, r));
}
async function $(t, e) {
  const s = new Headers(e?.headers), r = await t.getAccessToken();
  return r && s.set("Authorization", `Bearer ${r}`), {
    ...e,
    credentials: e?.credentials ?? "include",
    headers: s
  };
}
async function N(t) {
  return (await t.text()).trim() || `Request failed with ${t.status}.`;
}
function U(t) {
  const e = new Headers(t?.headers);
  return e.has("Accept") || e.set("Accept", "application/json"), {
    ...t,
    cache: t?.cache ?? "no-store",
    headers: e
  };
}
function D(t) {
  const e = new Headers(t);
  return e.has("Content-Type") || e.set("Content-Type", "application/json"), e.has("Accept") || e.set("Accept", "application/json"), e;
}
function B(t, e = {}) {
  return async () => await t.getAccessToken() ?? e.anonymousToken ?? "";
}
function at(t, e) {
  return {
    ...t,
    accessTokenFactory: B(e)
  };
}
function ot() {
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
function ct(t, e = {}) {
  return {
    baseUrl: t,
    headers: e.headers,
    http: L(t, e.headers)
  };
}
function L(t, e) {
  return {
    async getJson(s, r) {
      return R(t, s, C(e, V(r)));
    },
    async postJson(s, r, n) {
      return R(t, s, C(e, {
        ...n,
        method: "POST",
        headers: W(n?.headers),
        body: JSON.stringify(r)
      }));
    }
  };
}
function C(t, e = {}) {
  return t ? {
    ...e,
    headers: G(t, e.headers)
  } : e;
}
function G(t, e) {
  const s = new Headers(t);
  return new Headers(e).forEach((r, n) => s.set(n, r)), s;
}
async function R(t, e, s) {
  const r = Q(t, e), n = await fetch(r, s);
  if (!n.ok)
    throw new p(n.status, await Y(n));
  const i = await n.text();
  try {
    return JSON.parse(i);
  } catch {
    throw new p(
      n.status,
      `Expected JSON from ${r}, but received ${X(n, i)}. Check Studio:BackendBaseUrl and make sure the backend maps this API route.`
    );
  }
}
async function Y(t) {
  if ((t.headers.get("content-type") ?? "").includes("application/json"))
    try {
      const r = await t.json();
      return I(r) ?? `Request failed with ${t.status}.`;
    } catch {
      return `Request failed with ${t.status}.`;
    }
  return (await t.text()).trim() || `Request failed with ${t.status}.`;
}
function I(t) {
  if (typeof t.detail == "string" && t.detail.length > 0) return t.detail;
  if (typeof t.title == "string" && t.title.length > 0) return t.title;
  if (Array.isArray(t.errors) && t.errors.length > 0) return t.errors.map(String).join(" ");
  if (t.errors && typeof t.errors == "object") {
    const e = Object.values(t.errors).flatMap((s) => Array.isArray(s) ? s : [s]).map(String);
    if (e.length > 0) return e.join(" ");
  }
  return null;
}
function Q(t, e) {
  return new URL(e, t).toString();
}
function V(t) {
  const e = new Headers(t?.headers);
  return e.has("Accept") || e.set("Accept", "application/json"), {
    ...t,
    cache: t?.cache ?? "no-store",
    headers: e
  };
}
function W(t) {
  const e = new Headers(t);
  return e.has("Content-Type") || e.set("Content-Type", "application/json"), e.has("Accept") || e.set("Accept", "application/json"), e;
}
function X(t, e) {
  const s = t.headers.get("content-type") ?? "an unknown content type", r = e.trim(), n = r.length > 0 ? `: ${r.slice(0, 80)}` : "";
  return `${s}${n}`;
}
class p extends Error {
  constructor(e, s) {
    super(s), this.status = e, this.name = "StudioHttpError";
  }
  status;
}
export {
  f as AuthAdapterError,
  A as AuthConfigurationError,
  st as AuthGuard,
  tt as AuthProvider,
  nt as RequireAuth,
  p as StudioHttpError,
  K as createAuthProviderManager,
  it as createAuthenticatedHttpClient,
  ot as createContributionRegistry,
  ct as createEndpointContext,
  L as createHttpClient,
  rt as createOidcAuthAdapter,
  z as createRedirectAuthAdapter,
  B as createSignalRAccessTokenFactory,
  et as useAuthCapabilities,
  x as useAuthContext,
  M as useAuthSession,
  _ as usePermissions,
  at as withAuthenticatedSignalROptions,
  C as withDefaultHeaders
};
