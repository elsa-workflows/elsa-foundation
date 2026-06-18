import { r as c } from "../vendor/chunks/index.js";
const g = {
  status: "anonymous",
  roles: [],
  permissions: []
}, R = {
  status: "unknown",
  roles: [],
  permissions: []
};
function X(t) {
  return new P(t);
}
class P {
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
  session = R;
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
    const s = await this.options.bootstrap(), n = s.providers.find((r) => r.enabled && r.isDefault) ?? s.providers.find((r) => r.enabled);
    if (!n)
      throw new A("No enabled authentication provider was returned by /_elsa/identity/bootstrap.");
    return this.activeAdapter = this.requireAdapter(n.id), this.activeAdapter;
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
function q() {
  if (v) return l;
  v = 1;
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
  return l.Fragment = e, l.jsx = s, l.jsxs = s, l;
}
var S;
function O() {
  return S || (S = 1, y.exports = q()), y.exports;
}
var h = O();
const T = c.createContext(null);
function x() {
  const t = c.useContext(T);
  if (!t)
    throw new Error("Auth SDK hooks must be used within <AuthProvider>.");
  return t;
}
function Z({ manager: t, children: e }) {
  const [s, n] = c.useState(() => t.getSession() ?? R), [r, i] = c.useState(null);
  c.useEffect(() => {
    let d = !1;
    async function $() {
      const k = await t.initialize();
      if (!d && (n(k), k.status === "authenticated")) {
        const J = await t.getCapabilities();
        d || i(J);
      }
    }
    return $(), () => {
      d = !0;
    };
  }, [t]);
  const a = c.useCallback((d) => t.login(d), [t]), o = c.useCallback(async () => {
    await t.logout(), n(t.getSession()), i(null);
  }, [t]), u = c.useCallback(async () => {
    const d = await t.refresh();
    return n(d), d.status === "authenticated" ? i(await t.getCapabilities()) : i(null), d;
  }, [t]), w = c.useMemo(() => ({
    session: s,
    capabilities: r,
    login: a,
    logout: o,
    refresh: u
  }), [r, a, o, u, s]);
  return /* @__PURE__ */ h.jsx(T.Provider, { value: w, children: e });
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
      hasAny: (s) => s.some((n) => e.has(n)),
      hasAll: (s) => s.every((n) => e.has(n))
    };
  }, [t]);
}
function K() {
  return x().capabilities;
}
function tt({ requires: t, requireAll: e = !0, fallback: s = null, children: n }) {
  const r = _(), i = typeof t == "string" ? [t] : t ?? [];
  return i.length === 0 || (e ? r.hasAll(i) : r.hasAny(i)) ? /* @__PURE__ */ h.jsx(h.Fragment, { children: n }) : /* @__PURE__ */ h.jsx(h.Fragment, { children: s });
}
function et({ children: t, fallback: e = null, loginOptions: s }) {
  const { session: n, login: r } = x();
  return c.useEffect(() => {
    n.status === "anonymous" && r(s);
  }, [r, s, n.status]), n.status !== "authenticated" ? /* @__PURE__ */ h.jsx(h.Fragment, { children: e }) : /* @__PURE__ */ h.jsx(h.Fragment, { children: t });
}
function z(t) {
  const e = t.fetch ?? fetch, s = t.sessionEndpoint ?? "/_elsa/identity/session", n = t.logoutEndpoint ?? "/_elsa/identity/logout", r = t.refreshEndpoint ?? "/_elsa/identity/refresh";
  return {
    id: t.id,
    kind: t.kind,
    initialize: () => m(e, s),
    login: (i) => {
      const a = t.challenge;
      if (!a || a.type !== "redirect")
        throw new p(`Provider '${t.id}' does not expose a redirect challenge.`);
      const o = new URL(a.loginPath, t.location?.origin ?? window.location.origin), u = i?.returnUrl ?? t.location?.href ?? window.location.href;
      return o.searchParams.set("returnUrl", u), (t.location ?? window.location).assign(o.toString()), Promise.resolve();
    },
    handleCallback: () => m(e, s),
    logout: async () => {
      const i = await e(n, { method: "POST", credentials: "include" });
      if (!i.ok)
        throw new p(`Sign-out failed with ${i.status}.`);
    },
    getAccessToken: async () => {
      if (!t.tokenEndpoint)
        return null;
      const i = await e(t.tokenEndpoint, { credentials: "include", cache: "no-store" });
      if (i.status === 401)
        return null;
      if (!i.ok)
        throw new p(`Access-token request failed with ${i.status}.`);
      const a = await i.json();
      return typeof a.accessToken == "string" ? a.accessToken : null;
    },
    refresh: async () => {
      const i = await e(r, { method: "POST", credentials: "include" });
      if (i.status === 401)
        return g;
      if (!i.ok)
        throw new p(`Session refresh failed with ${i.status}.`);
      return b(i);
    }
  };
}
async function m(t, e) {
  const s = await t(e, { credentials: "include", cache: "no-store" });
  if (s.status === 401)
    return g;
  if (!s.ok)
    throw new p(`Session request failed with ${s.status}.`);
  return b(s);
}
async function b(t) {
  const e = await t.json();
  return {
    ...e,
    roles: e.roles ?? [],
    permissions: e.permissions ?? []
  };
}
class p extends Error {
  constructor(e) {
    super(e), this.name = "AuthAdapterError";
  }
}
function st(t) {
  return z({
    ...t,
    kind: "external-oidc"
  });
}
function nt(t, e, s = {}) {
  return {
    getJson(n, r) {
      return E(t, n, e, s, N(r));
    },
    postJson(n, r, i) {
      return E(t, n, e, s, {
        ...i,
        method: "POST",
        headers: U(i?.headers),
        body: JSON.stringify(r)
      });
    }
  };
}
async function E(t, e, s, n, r) {
  const i = n.fetch ?? fetch, a = new URL(e, t).toString(), o = await i(a, await j(s, r)), u = o.status === 401 && n.refreshOnUnauthorized !== !1 ? await H(i, a, s, r) : o;
  if (!u.ok)
    throw new f(u.status, await F(u));
  const w = await u.text();
  try {
    return JSON.parse(w);
  } catch {
    throw new f(u.status, `Expected JSON from ${a}.`);
  }
}
async function H(t, e, s, n) {
  return (await s.refresh()).status !== "authenticated" ? new Response("Authentication required.", { status: 401 }) : t(e, await j(s, n));
}
async function j(t, e) {
  const s = new Headers(e?.headers), n = await t.getAccessToken();
  return n && s.set("Authorization", `Bearer ${n}`), {
    ...e,
    credentials: e?.credentials ?? "include",
    headers: s
  };
}
async function F(t) {
  return (await t.text()).trim() || `Request failed with ${t.status}.`;
}
function N(t) {
  const e = new Headers(t?.headers);
  return e.has("Accept") || e.set("Accept", "application/json"), {
    ...t,
    cache: t?.cache ?? "no-store",
    headers: e
  };
}
function U(t) {
  const e = new Headers(t);
  return e.has("Content-Type") || e.set("Content-Type", "application/json"), e.has("Accept") || e.set("Accept", "application/json"), e;
}
function D(t, e = {}) {
  return async () => await t.getAccessToken() ?? e.anonymousToken ?? "";
}
function rt(t, e) {
  return {
    ...t,
    accessTokenFactory: D(e)
  };
}
function it() {
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
function at(t) {
  return {
    baseUrl: t,
    http: B(t)
  };
}
function B(t) {
  return {
    async getJson(e, s) {
      return C(t, e, I(s));
    },
    async postJson(e, s, n) {
      return C(t, e, {
        ...n,
        method: "POST",
        headers: Q(n?.headers),
        body: JSON.stringify(s)
      });
    }
  };
}
async function C(t, e, s) {
  const n = Y(t, e), r = await fetch(n, s);
  if (!r.ok)
    throw new f(r.status, await L(r));
  const i = await r.text();
  try {
    return JSON.parse(i);
  } catch {
    throw new f(
      r.status,
      `Expected JSON from ${n}, but received ${V(r, i)}. Check Studio:BackendBaseUrl and make sure the backend maps this API route.`
    );
  }
}
async function L(t) {
  if ((t.headers.get("content-type") ?? "").includes("application/json"))
    try {
      const n = await t.json();
      return G(n) ?? `Request failed with ${t.status}.`;
    } catch {
      return `Request failed with ${t.status}.`;
    }
  return (await t.text()).trim() || `Request failed with ${t.status}.`;
}
function G(t) {
  if (typeof t.detail == "string" && t.detail.length > 0) return t.detail;
  if (typeof t.title == "string" && t.title.length > 0) return t.title;
  if (Array.isArray(t.errors) && t.errors.length > 0) return t.errors.map(String).join(" ");
  if (t.errors && typeof t.errors == "object") {
    const e = Object.values(t.errors).flatMap((s) => Array.isArray(s) ? s : [s]).map(String);
    if (e.length > 0) return e.join(" ");
  }
  return null;
}
function Y(t, e) {
  return new URL(e, t).toString();
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
function V(t, e) {
  const s = t.headers.get("content-type") ?? "an unknown content type", n = e.trim(), r = n.length > 0 ? `: ${n.slice(0, 80)}` : "";
  return `${s}${r}`;
}
class f extends Error {
  constructor(e, s) {
    super(s), this.status = e, this.name = "StudioHttpError";
  }
  status;
}
export {
  p as AuthAdapterError,
  A as AuthConfigurationError,
  tt as AuthGuard,
  Z as AuthProvider,
  et as RequireAuth,
  f as StudioHttpError,
  X as createAuthProviderManager,
  nt as createAuthenticatedHttpClient,
  it as createContributionRegistry,
  at as createEndpointContext,
  B as createHttpClient,
  st as createOidcAuthAdapter,
  z as createRedirectAuthAdapter,
  D as createSignalRAccessTokenFactory,
  K as useAuthCapabilities,
  x as useAuthContext,
  M as useAuthSession,
  _ as usePermissions,
  rt as withAuthenticatedSignalROptions
};
