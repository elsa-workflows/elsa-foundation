import { j as l } from "../vendor/chunks/jsx-runtime.js";
import { r as c } from "../vendor/chunks/index.js";
const b = {
  status: "anonymous",
  roles: [],
  permissions: []
}, P = {
  status: "unknown",
  roles: [],
  permissions: []
};
function H(t) {
  return new B(t);
}
class B {
  constructor(e) {
    this.options = e;
    for (const n of e.adapters ?? []) {
      if (this.adapters.has(n.id))
        throw new A(`Duplicate auth provider adapter '${n.id}'.`);
      this.adapters.set(n.id, n);
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
    const n = e ? await this.resolveAdapter(e) : await this.resolveAdapter();
    return this.session = await n.handleCallback(), this.session;
  }
  async logout() {
    await (await this.resolveAdapter()).logout(), this.session = b;
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
    const n = await this.options.bootstrap(), s = n.providers.find((r) => r.enabled && r.isDefault) ?? n.providers.find((r) => r.enabled);
    if (!s)
      throw new A("No enabled authentication provider was returned by /_elsa/identity/bootstrap.");
    return this.activeAdapter = this.resolveProviderAdapter(s), this.activeAdapter;
  }
  requireAdapter(e) {
    const n = this.adapters.get(e);
    if (!n)
      throw new A(`No auth provider adapter is registered for '${e}'.`);
    return this.activeAdapter = n, n;
  }
  resolveProviderAdapter(e) {
    const n = this.adapters.get(e.id);
    if (n)
      return this.activeAdapter = n, n;
    if (!this.options.adapterFactory)
      throw new A(`No auth provider adapter is registered for '${e.id}'.`);
    const s = this.options.adapterFactory(e);
    return this.adapters.set(e.id, s), this.activeAdapter = s, s;
  }
}
class A extends Error {
  constructor(e) {
    super(e), this.name = "AuthConfigurationError";
  }
}
function $(t) {
  const e = t.fetch ?? fetch, n = t.sessionEndpoint ?? "/_elsa/identity/session", s = t.logoutEndpoint ?? `/_elsa/identity/logout/${encodeURIComponent(t.id)}`;
  return {
    id: t.id,
    kind: t.kind,
    initialize: () => m(e, n, t),
    login: (r) => {
      const i = t.challenge;
      if (!i || i.type === "none")
        throw new w(`Provider '${t.id}' does not expose a redirect challenge.`);
      const a = "method" in i ? i.method.toUpperCase() : "GET";
      if (a !== "GET")
        throw new w(`Provider '${t.id}' exposes an unsupported ${a} challenge.`);
      const o = new URL(G(i), O(t)), u = r?.returnUrl ?? t.location?.href ?? window.location.href;
      return o.searchParams.set("returnUrl", u), (t.location ?? window.location).assign(o.toString()), Promise.resolve();
    },
    handleCallback: () => m(e, n, t),
    logout: async () => {
      const r = await e(S(s, t), { method: "POST", credentials: "include" });
      if (!r.ok)
        throw new w(`Sign-out failed with ${r.status}.`);
    },
    getAccessToken: async () => {
      if (!t.tokenEndpoint)
        return null;
      const r = await e(S(t.tokenEndpoint, t), { credentials: "include", cache: "no-store" });
      if (r.status === 401)
        return null;
      if (!r.ok)
        throw new w(`Access-token request failed with ${r.status}.`);
      const i = await r.json();
      return typeof i.accessToken == "string" ? i.accessToken : null;
    },
    refresh: async () => {
      const r = await t.getRefreshToken?.(), i = t.refreshEndpoint;
      if (!i || !r)
        return m(e, n, t);
      const a = await e(S(i, t), {
        method: "POST",
        credentials: "include",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json"
        },
        body: JSON.stringify({ refreshToken: r })
      });
      if (a.status === 401)
        return b;
      if (!a.ok)
        throw new w(`Session refresh failed with ${a.status}.`);
      const o = await a.json();
      return o.status ? R(o) : m(e, n, t);
    }
  };
}
async function m(t, e, n) {
  const s = await t(S(e, n), { credentials: "include", cache: "no-store" });
  if (s.status === 401)
    return b;
  if (!s.ok)
    throw new w(`Session request failed with ${s.status}.`);
  return _(s);
}
async function _(t) {
  const e = await t.json();
  return R(e);
}
function R(t) {
  return {
    ...t,
    roles: t.roles ?? [],
    permissions: t.permissions ?? []
  };
}
function G(t) {
  return "loginPath" in t ? t.loginPath : t.url;
}
function S(t, e) {
  return new URL(t, O(e)).toString();
}
function O(t) {
  return t?.baseUrl ?? t?.location?.origin ?? window.location.origin;
}
class w extends Error {
  constructor(e) {
    super(e), this.name = "AuthAdapterError";
  }
}
function ot(t = {}) {
  const e = t.baseUrl ?? window.location.origin, n = t.fetch ?? fetch;
  return H({
    bootstrap: () => T(n, e, "/_elsa/identity/bootstrap"),
    capabilities: () => T(n, e, "/_elsa/identity/capabilities"),
    isCallback: t.isCallback,
    adapterFactory: (s) => $({
      id: s.id,
      kind: s.kind,
      baseUrl: e,
      challenge: s.challenge,
      fetch: n
    })
  });
}
async function T(t, e, n) {
  const s = await t(new URL(n, e).toString(), {
    credentials: "include",
    cache: "no-store",
    headers: { Accept: "application/json" }
  });
  if (!s.ok)
    throw new Error(`Auth discovery request failed with ${s.status}.`);
  return await s.json();
}
const q = c.createContext(null);
function C() {
  const t = c.useContext(q);
  if (!t)
    throw new Error("Auth SDK hooks must be used within <AuthProvider>.");
  return t;
}
function ct({ manager: t, children: e }) {
  const [n, s] = c.useState(() => t.getSession() ?? P), [r, i] = c.useState(null);
  c.useEffect(() => {
    let d = !1;
    async function L() {
      const x = await t.initialize();
      if (!d && (s(x), x.status === "authenticated")) {
        const D = await t.getCapabilities();
        d || i(D);
      }
    }
    return L(), () => {
      d = !0;
    };
  }, [t]);
  const a = c.useCallback((d) => t.login(d), [t]), o = c.useCallback(async () => {
    await t.logout(), s(t.getSession()), i(null);
  }, [t]), u = c.useCallback(async () => {
    const d = await t.refresh();
    return s(d), d.status === "authenticated" ? i(await t.getCapabilities()) : i(null), d;
  }, [t]), g = c.useMemo(() => ({
    session: n,
    capabilities: r,
    login: a,
    logout: o,
    refresh: u
  }), [r, a, o, u, n]);
  return /* @__PURE__ */ l.jsx(q.Provider, { value: g, children: e });
}
function V() {
  return C().session;
}
function I() {
  const { permissions: t } = V();
  return c.useMemo(() => {
    const e = new Set(t);
    return {
      has: (n) => e.has(n),
      hasAny: (n) => n.some((s) => e.has(s)),
      hasAll: (n) => n.every((s) => e.has(s))
    };
  }, [t]);
}
function ut() {
  return C().capabilities;
}
function dt({ requires: t, requireAll: e = !0, fallback: n = null, children: s }) {
  const r = I(), i = typeof t == "string" ? [t] : t ?? [];
  return i.length === 0 || (e ? r.hasAll(i) : r.hasAny(i)) ? /* @__PURE__ */ l.jsx(l.Fragment, { children: s }) : /* @__PURE__ */ l.jsx(l.Fragment, { children: n });
}
function lt({ children: t, fallback: e = null, loginOptions: n }) {
  const { session: s, login: r } = C();
  return c.useEffect(() => {
    s.status === "anonymous" && r(n);
  }, [r, n, s.status]), s.status !== "authenticated" ? /* @__PURE__ */ l.jsx(l.Fragment, { children: e }) : /* @__PURE__ */ l.jsx(l.Fragment, { children: t });
}
function ht(t) {
  return $({
    ...t,
    kind: "external-oidc"
  });
}
function ft(t, e, n = {}) {
  return {
    requestJson(s, r) {
      return h(t, s, e, n, k(r));
    },
    getJson(s, r) {
      return h(t, s, e, n, k(r));
    },
    postJson(s, r, i) {
      return h(t, s, e, n, {
        ...i,
        method: "POST",
        headers: j(i?.headers),
        body: JSON.stringify(r)
      });
    },
    putJson(s, r, i) {
      return h(t, s, e, n, {
        ...i,
        method: "PUT",
        headers: j(i?.headers),
        body: JSON.stringify(r)
      });
    },
    deleteJson(s, r) {
      return h(t, s, e, n, k({
        ...r,
        method: "DELETE"
      }));
    },
    postForm(s, r, i) {
      return h(t, s, e, n, k({
        ...i,
        method: "POST",
        body: r
      }));
    }
  };
}
const v = /* @__PURE__ */ new Map();
async function h(t, e, n, s, r) {
  const i = s.fetch ?? fetch, a = new URL(e, t).toString(), o = await i(a, await U(n, r)), u = o.status === 401 && s.refreshOnUnauthorized !== !1 ? await K(i, a, n, r) : o;
  if (!u.ok)
    throw await F(u);
  const g = await u.text();
  if (!g.trim())
    return {};
  try {
    return JSON.parse(g);
  } catch {
    throw new y(u.status, `Expected JSON from ${a}.`);
  }
}
async function K(t, e, n, s) {
  return await Q(e, n) ? t(e, await U(n, s)) : new Response("Authentication required.", { status: 401 });
}
async function Q(t, e) {
  const n = new URL(t).origin, s = v.get(n);
  if (s)
    return s;
  const r = e.refresh().then((i) => i.status === "authenticated").finally(() => v.delete(n));
  return v.set(n, r), r;
}
async function U(t, e) {
  const n = new Headers(e?.headers), s = await t.getAccessToken();
  return s && n.set("Authorization", `Bearer ${s}`), {
    ...e,
    credentials: e?.credentials ?? "include",
    headers: n
  };
}
function k(t) {
  const e = new Headers(t?.headers);
  return e.has("Accept") || e.set("Accept", "application/json"), {
    ...t,
    cache: t?.cache ?? "no-store",
    headers: e
  };
}
function j(t) {
  const e = new Headers(t);
  return e.has("Content-Type") || e.set("Content-Type", "application/json"), e.has("Accept") || e.set("Accept", "application/json"), e;
}
function W(t, e = {}) {
  return async () => await t.getAccessToken() ?? e.anonymousToken ?? "";
}
function pt(t, e) {
  return {
    ...t,
    accessTokenFactory: W(e)
  };
}
function wt() {
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
    http: X(t, e.headers)
  };
}
function X(t, e) {
  return {
    requestJson(n, s) {
      return p(t, n, f(e, E(s)));
    },
    async getJson(n, s) {
      return p(t, n, f(e, E(s)));
    },
    async postJson(n, s, r) {
      return p(t, n, f(e, {
        ...r,
        method: "POST",
        headers: J(r?.headers),
        body: JSON.stringify(s)
      }));
    },
    async putJson(n, s, r) {
      return p(t, n, f(e, {
        ...r,
        method: "PUT",
        headers: J(r?.headers),
        body: JSON.stringify(s)
      }));
    },
    async deleteJson(n, s) {
      return p(t, n, f(e, E({
        ...s,
        method: "DELETE"
      })));
    },
    async postForm(n, s, r) {
      return p(t, n, f(e, E({
        ...r,
        method: "POST",
        body: s
      })));
    }
  };
}
function f(t, e = {}) {
  return t ? {
    ...e,
    headers: Y(t, e.headers)
  } : e;
}
function Y(t, e) {
  const n = new Headers(t);
  return new Headers(e).forEach((s, r) => n.set(r, s)), n;
}
async function p(t, e, n) {
  const s = st(t, e), r = await fetch(s, n);
  if (!r.ok)
    throw await F(r);
  const i = await r.text();
  if (!i.trim())
    return {};
  try {
    return JSON.parse(i);
  } catch {
    throw new y(
      r.status,
      `Expected JSON from ${s}, but received ${rt(r, i)}. Check Studio:BackendBaseUrl and make sure the backend maps this API route.`
    );
  }
}
async function Z(t) {
  return (await N(t)).message;
}
async function F(t) {
  const e = await N(t);
  return new y(t.status, e.message, e.validationErrors);
}
async function N(t) {
  const e = t.headers.get("content-type") ?? "";
  if (tt(e))
    try {
      const s = await t.json(), r = M(s);
      return {
        message: et(s) ?? nt(r) ?? `Request failed with ${t.status}.`,
        validationErrors: r
      };
    } catch {
      return { message: `Request failed with ${t.status}.`, validationErrors: null };
    }
  return { message: (await t.text()).trim() || `Request failed with ${t.status}.`, validationErrors: null };
}
function tt(t) {
  return t.toLowerCase().includes("json");
}
async function gt(t) {
  if (t instanceof y)
    return t.message;
  if (z(t))
    try {
      return await Z(t.response.clone());
    } catch {
      return t.response.statusText || "Request failed.";
    }
  return t instanceof Error ? t.message : "Unknown error.";
}
async function At(t) {
  if (t instanceof y)
    return t.validationErrors;
  if (!z(t))
    return null;
  try {
    const e = await t.response.clone().json();
    return M(e);
  } catch {
    return null;
  }
}
function et(t) {
  if (typeof t.detail == "string" && t.detail.length > 0) return t.detail;
  if (typeof t.title == "string" && t.title.length > 0) return t.title;
  if (Array.isArray(t.errors) && t.errors.length > 0) return t.errors.map(String).join(" ");
  if (t.errors && typeof t.errors == "object") {
    const e = Object.values(t.errors).flatMap((n) => Array.isArray(n) ? n : [n]).map(String);
    if (e.length > 0) return e.join(" ");
  }
  return null;
}
function M(t) {
  const e = t.errors;
  if (!e || typeof e != "object" || Array.isArray(e))
    return null;
  const n = {};
  for (const [s, r] of Object.entries(e)) {
    const i = Array.isArray(r) ? r.map(String) : [String(r)];
    i.length > 0 && (n[s] = i);
  }
  return Object.keys(n).length > 0 ? n : null;
}
function nt(t) {
  return t ? Object.values(t).flat().join(" ") : null;
}
function z(t) {
  return typeof t == "object" && t !== null && "response" in t && t.response instanceof Response;
}
function st(t, e) {
  return new URL(e, t).toString();
}
function E(t) {
  const e = new Headers(t?.headers);
  return e.has("Accept") || e.set("Accept", "application/json"), {
    ...t,
    cache: t?.cache ?? "no-store",
    headers: e
  };
}
function J(t) {
  const e = new Headers(t);
  return e.has("Content-Type") || e.set("Content-Type", "application/json"), e.has("Accept") || e.set("Accept", "application/json"), e;
}
function rt(t, e) {
  const n = t.headers.get("content-type") ?? "an unknown content type", s = e.trim(), r = s.length > 0 ? `: ${s.slice(0, 80)}` : "";
  return `${n}${r}`;
}
class y extends Error {
  constructor(e, n, s = null) {
    super(n), this.status = e, this.validationErrors = s, this.name = "StudioHttpError";
  }
  status;
  validationErrors;
}
export {
  w as AuthAdapterError,
  A as AuthConfigurationError,
  dt as AuthGuard,
  ct as AuthProvider,
  lt as RequireAuth,
  y as StudioHttpError,
  H as createAuthProviderManager,
  ft as createAuthenticatedHttpClient,
  ot as createBackendAuthProviderManager,
  wt as createContributionRegistry,
  yt as createEndpointContext,
  X as createHttpClient,
  ht as createOidcAuthAdapter,
  $ as createRedirectAuthAdapter,
  W as createSignalRAccessTokenFactory,
  F as createStudioHttpError,
  gt as describeApiError,
  At as tryExtractValidationErrors,
  ut as useAuthCapabilities,
  C as useAuthContext,
  V as useAuthSession,
  I as usePermissions,
  pt as withAuthenticatedSignalROptions,
  f as withDefaultHeaders
};
