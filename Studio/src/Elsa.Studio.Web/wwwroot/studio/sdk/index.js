import { j as f } from "../vendor/chunks/jsx-runtime.js";
import { r as u } from "../vendor/chunks/index.js";
const T = {
  status: "anonymous",
  roles: [],
  permissions: []
}, F = {
  status: "unknown",
  roles: [],
  permissions: []
};
function V(t) {
  return new Q(t);
}
class Q {
  constructor(e) {
    this.options = e;
    for (const n of e.adapters ?? []) {
      if (this.adapters.has(n.id))
        throw new S(`Duplicate auth provider adapter '${n.id}'.`);
      this.adapters.set(n.id, n);
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
      const n = this.getCallbackProviderId(), r = n ? await this.getProviderAdapter(n) : await this.resolveActiveAdapter();
      return await this.applySession(await r.handleCallback(), r), this.pendingLoginProviderId = null, this.session;
    }
    const e = await this.resolveActiveAdapter();
    return await this.applySession(await e.initialize(), e), this.pendingLoginProviderId = null, this.session;
  }
  async login(e) {
    const n = e?.providerId ? await this.getProviderAdapter(e.providerId) : await this.resolveActiveAdapter();
    this.pendingLoginProviderId = n.id;
    try {
      const r = await n.login({ ...e, providerId: n.id });
      r ? (await this.applySession(r, n), this.pendingLoginProviderId = null) : this.session.status !== "authenticated" && (this.activeAdapter = n);
    } catch (r) {
      throw this.pendingLoginProviderId = null, r;
    }
  }
  async handleCallback(e) {
    const n = e ? await this.getProviderAdapter(e) : await this.resolveActiveAdapter();
    return await this.applySession(await n.handleCallback(), n), this.pendingLoginProviderId = null, this.session;
  }
  async logout() {
    await (await this.resolveActiveAdapter()).logout(), this.session = T;
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
    const e = await this.options.bootstrap(), n = e.providers.find((s) => s.enabled && s.isDefault) ?? e.providers.find((s) => s.enabled);
    if (!n)
      throw new S("No enabled authentication provider was returned by /_elsa/identity/bootstrap.");
    const r = this.resolveProviderAdapter(n);
    return this.activeAdapter = r, r;
  }
  async getProviderAdapter(e) {
    const n = this.adapters.get(e);
    if (n)
      return n;
    const s = (await this.options.bootstrap()).providers.find((i) => i.enabled && i.id === e);
    if (!s)
      throw new S(`No auth provider adapter is registered for '${e}'.`);
    return this.resolveProviderAdapter(s);
  }
  resolveProviderAdapter(e) {
    const n = this.adapters.get(e.id);
    if (n)
      return n;
    if (!this.options.adapterFactory)
      throw new S(`No auth provider adapter is registered for '${e.id}'.`);
    const r = this.options.adapterFactory(e);
    return this.adapters.set(e.id, r), r;
  }
  getCallbackProviderId() {
    const e = this.options.getCallbackProviderId?.();
    return e || (this.pendingLoginProviderId ? this.pendingLoginProviderId : typeof window > "u" ? null : new URLSearchParams(window.location.search).get("authProviderId"));
  }
  async applySession(e, n) {
    const r = e.provider?.id ? await this.getProviderAdapter(e.provider.id) : n;
    this.session = e, this.activeAdapter = r;
  }
}
class S extends Error {
  constructor(e) {
    super(e), this.name = "AuthConfigurationError";
  }
}
function z(t) {
  const e = t.fetch ?? fetch, n = t.sessionEndpoint ?? "/_elsa/identity/session", r = t.logoutEndpoint ?? `/_elsa/identity/logout/${encodeURIComponent(t.id)}`;
  return {
    id: t.id,
    kind: t.kind,
    initialize: () => b(e, n, t),
    login: (s) => {
      const i = t.challenge;
      if (!i || i.type === "none")
        throw new v(`Provider '${t.id}' does not expose a redirect challenge.`);
      const a = "method" in i ? i.method.toUpperCase() : "GET";
      if (a !== "GET")
        throw new v(`Provider '${t.id}' exposes an unsupported ${a} challenge.`);
      const c = new URL(Y(i), R(t)), o = s?.returnUrl ?? t.location?.href ?? window.location.href;
      return c.searchParams.set("returnUrl", Z(o, s?.providerId ?? t.id, t)), (t.location ?? window.location).assign(c.toString()), Promise.resolve();
    },
    handleCallback: () => b(e, n, t),
    logout: async () => {
      const s = await e(E(r, t), { method: "POST", credentials: "include" });
      if (!s.ok)
        throw new v(`Sign-out failed with ${s.status}.`);
    },
    getAccessToken: async () => {
      if (!t.tokenEndpoint)
        return null;
      const s = await e(E(t.tokenEndpoint, t), { credentials: "include", cache: "no-store" });
      if (s.status === 401)
        return null;
      if (!s.ok)
        throw new v(`Access-token request failed with ${s.status}.`);
      const i = await s.json();
      return typeof i.accessToken == "string" ? i.accessToken : null;
    },
    refresh: async () => {
      const s = await t.getRefreshToken?.(), i = t.refreshEndpoint;
      if (!i || !s)
        return b(e, n, t);
      const a = await e(E(i, t), {
        method: "POST",
        credentials: "include",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json"
        },
        body: JSON.stringify({ refreshToken: s })
      });
      if (a.status === 401)
        return T;
      if (!a.ok)
        throw new v(`Session refresh failed with ${a.status}.`);
      const c = await a.json();
      return c.status ? H(c) : b(e, n, t);
    }
  };
}
async function b(t, e, n) {
  const r = await t(E(e, n), { credentials: "include", cache: "no-store" });
  if (r.status === 401)
    return T;
  if (!r.ok)
    throw new v(`Session request failed with ${r.status}.`);
  return W(r);
}
async function W(t) {
  const e = await t.json();
  return H(e);
}
function H(t) {
  const e = X(t.status) ? t.status : "anonymous";
  return {
    ...t,
    status: e,
    roles: L(t.roles),
    permissions: L(t.permissions)
  };
}
function L(t) {
  return Array.isArray(t) ? t.filter((e) => typeof e == "string") : [];
}
function X(t) {
  return t === "unknown" || t === "anonymous" || t === "authenticated";
}
function Y(t) {
  return "loginPath" in t ? t.loginPath : t.url;
}
function E(t, e) {
  return new URL(t, R(e)).toString();
}
function R(t) {
  return t?.baseUrl ?? t?.location?.origin ?? window.location.origin;
}
function Z(t, e, n) {
  const r = new URL(t, tt(n));
  return r.searchParams.set("authProviderId", e), et(t) ? `${r.pathname}${r.search}${r.hash}` : r.toString();
}
function tt(t) {
  return t?.location?.href ?? (typeof window < "u" ? window.location.href : void 0) ?? t?.location?.origin ?? R(t);
}
function et(t) {
  try {
    return new URL(t), !1;
  } catch {
    return !0;
  }
}
class v extends Error {
  constructor(e) {
    super(e), this.name = "AuthAdapterError";
  }
}
function vt(t = {}) {
  const e = t.baseUrl ?? window.location.origin, n = t.fetch ?? fetch;
  return V({
    bootstrap: () => I(n, e, "/_elsa/identity/bootstrap"),
    capabilities: () => I(n, e, "/_elsa/identity/capabilities"),
    isCallback: t.isCallback,
    getCallbackProviderId: t.getCallbackProviderId,
    adapterFactory: (r) => z({
      id: r.id,
      kind: r.kind,
      baseUrl: e,
      challenge: r.challenge,
      fetch: n
    })
  });
}
async function I(t, e, n) {
  const r = await t(new URL(n, e).toString(), {
    credentials: "include",
    cache: "no-store",
    headers: { Accept: "application/json" }
  });
  if (!r.ok)
    throw new Error(`Auth discovery request failed with ${r.status}.`);
  return await r.json();
}
const N = u.createContext(null);
function j() {
  const t = u.useContext(N);
  if (!t)
    throw new Error("Auth SDK hooks must be used within <AuthProvider>.");
  return t;
}
function mt({ manager: t, children: e }) {
  const [n, r] = u.useState(() => t.getSession() ?? F), [s, i] = u.useState(null), a = u.useRef(!1), c = u.useRef(0), o = u.useCallback((d) => a.current && c.current === d, []);
  u.useLayoutEffect(() => {
    a.current = !0;
    const d = ++c.current;
    async function h() {
      try {
        const l = await t.initialize();
        if (!o(d))
          return;
        if (r(l), l.status !== "authenticated") {
          i(null);
          return;
        }
        try {
          const w = await t.getCapabilities();
          o(d) && i(w);
        } catch (w) {
          o(d) && (console.error("Auth capabilities request failed.", w), i(null));
        }
      } catch (l) {
        o(d) && (console.error("Auth initialization failed.", l), r(T), i(null));
      }
    }
    return h(), () => {
      a.current = !1, c.current += 1;
    };
  }, [t, o]);
  const p = u.useCallback(async (d) => {
    const h = ++c.current;
    if (await t.login(d), !o(h))
      return;
    const l = t.getSession();
    if (r(l), l.status === "authenticated")
      try {
        const w = await t.getCapabilities();
        o(h) && i(w);
      } catch (w) {
        o(h) && (console.error("Auth capabilities request failed.", w), i(null));
      }
    else
      i(null);
  }, [t, o]), $ = u.useCallback(async () => {
    const d = ++c.current;
    await t.logout(), o(d) && (r(t.getSession()), i(null));
  }, [t, o]), J = u.useCallback(async () => {
    const d = ++c.current, h = await t.refresh();
    if (!o(d))
      return h;
    if (r(h), h.status === "authenticated")
      try {
        const l = await t.getCapabilities();
        o(d) && i(l);
      } catch (l) {
        o(d) && (console.error("Auth capabilities request failed.", l), i(null));
      }
    else
      i(null);
    return h;
  }, [t, o]), K = u.useMemo(() => ({
    session: n,
    capabilities: s,
    login: p,
    logout: $,
    refresh: J
  }), [s, p, $, J, n]);
  return /* @__PURE__ */ f.jsx(N.Provider, { value: K, children: e });
}
function nt() {
  return j().session;
}
function rt() {
  const { permissions: t } = nt();
  return u.useMemo(() => {
    const e = new Set(t);
    return {
      has: (n) => e.has(n),
      hasAny: (n) => n.some((r) => e.has(r)),
      hasAll: (n) => n.every((r) => e.has(r))
    };
  }, [t]);
}
function kt() {
  return j().capabilities;
}
function St({ requires: t, requireAll: e = !0, fallback: n = null, children: r }) {
  const s = rt(), i = typeof t == "string" ? [t] : t ?? [];
  return i.length === 0 || (e ? s.hasAll(i) : s.hasAny(i)) ? /* @__PURE__ */ f.jsx(f.Fragment, { children: r }) : /* @__PURE__ */ f.jsx(f.Fragment, { children: n });
}
function bt({ children: t, fallback: e = null, loginOptions: n }) {
  const { session: r, login: s } = j(), i = u.useRef(null);
  return u.useEffect(() => {
    if (r.status === "anonymous") {
      const a = st(n), c = i.current;
      if (c?.key === a && c.login === s)
        return;
      const o = { key: a, login: s };
      i.current = o, s(n).catch((p) => {
        i.current === o && (i.current = null), console.error("Auth login failed.", p);
      });
    } else
      i.current = null;
  }, [s, n, r.status]), r.status !== "authenticated" ? /* @__PURE__ */ f.jsx(f.Fragment, { children: e }) : /* @__PURE__ */ f.jsx(f.Fragment, { children: t });
}
function st(t) {
  return `${t?.providerId ?? ""}
${t?.returnUrl ?? ""}`;
}
function Pt(t) {
  return z({
    ...t,
    kind: "external-oidc"
  });
}
function Ct(t, e, n = {}) {
  return {
    requestJson(r, s) {
      return y(t, r, e, n, P(s));
    },
    getJson(r, s) {
      return y(t, r, e, n, P(s));
    },
    postJson(r, s, i) {
      return y(t, r, e, n, {
        ...i,
        method: "POST",
        headers: O(i?.headers),
        body: JSON.stringify(s)
      });
    },
    putJson(r, s, i) {
      return y(t, r, e, n, {
        ...i,
        method: "PUT",
        headers: O(i?.headers),
        body: JSON.stringify(s)
      });
    },
    deleteJson(r, s) {
      return y(t, r, e, n, P({
        ...s,
        method: "DELETE"
      }));
    },
    postForm(r, s, i) {
      return y(t, r, e, n, P({
        ...i,
        method: "POST",
        body: s
      }));
    }
  };
}
const x = /* @__PURE__ */ new Map();
async function y(t, e, n, r, s) {
  const i = r.fetch ?? fetch, a = new URL(e, t).toString(), c = await i(a, await M(n, q(r, s))), o = c.status === 401 && r.refreshOnUnauthorized !== !1 ? await it(i, a, n, q(r, s)) : c;
  if (!o.ok)
    throw await D(o);
  const p = await o.text();
  if (!p.trim())
    return {};
  try {
    return JSON.parse(p);
  } catch {
    throw new k(o.status, `Expected JSON from ${a}.`);
  }
}
async function it(t, e, n, r) {
  return await ot(e, n) ? t(e, await M(n, r)) : new Response("Authentication required.", { status: 401 });
}
async function ot(t, e) {
  const n = new URL(t).origin, r = x.get(n);
  if (r)
    return r;
  const s = e.refresh().then((i) => i.status === "authenticated").finally(() => x.delete(n));
  return x.set(n, s), s;
}
async function M(t, e) {
  const n = new Headers(e?.headers), r = await t.getAccessToken();
  return r && n.set("Authorization", `Bearer ${r}`), {
    ...e,
    credentials: e?.credentials ?? "include",
    headers: n
  };
}
function P(t) {
  const e = new Headers(t?.headers);
  return e.has("Accept") || e.set("Accept", "application/json"), {
    ...t,
    cache: t?.cache ?? "no-store",
    headers: e
  };
}
function O(t) {
  const e = new Headers(t);
  return e.has("Content-Type") || e.set("Content-Type", "application/json"), e.has("Accept") || e.set("Accept", "application/json"), e;
}
function q(t, e) {
  return g(t.defaultHeaders ?? t.headers, e);
}
function at(t, e = {}) {
  return async () => await t.getAccessToken() ?? await e.fallbackAccessTokenFactory?.() ?? e.anonymousToken ?? "";
}
function Et(t, e) {
  const n = ct(t.accessTokenFactory) ? t.accessTokenFactory.bind(t) : void 0;
  return {
    ...t,
    accessTokenFactory: at(e, { fallbackAccessTokenFactory: n })
  };
}
function ct(t) {
  return typeof t == "function";
}
function m() {
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
function Tt() {
  const t = /* @__PURE__ */ new Set();
  return {
    contextProviders: m(),
    promptActions: m(),
    tools: m(),
    proposalRenderers: m(),
    surfaces: m(),
    dispatchPrompt(e) {
      for (const n of t)
        n(e);
    },
    onPrompt(e) {
      return t.add(e), () => t.delete(e);
    }
  };
}
function xt(t, e = {}) {
  return {
    baseUrl: t,
    headers: e.headers,
    http: ut(t, e.headers)
  };
}
function ut(t, e) {
  return {
    requestJson(n, r) {
      return A(t, n, g(e, C(r)));
    },
    async getJson(n, r) {
      return A(t, n, g(e, C(r)));
    },
    async postJson(n, r, s) {
      return A(t, n, g(e, {
        ...s,
        method: "POST",
        headers: U(s?.headers),
        body: JSON.stringify(r)
      }));
    },
    async putJson(n, r, s) {
      return A(t, n, g(e, {
        ...s,
        method: "PUT",
        headers: U(s?.headers),
        body: JSON.stringify(r)
      }));
    },
    async deleteJson(n, r) {
      return A(t, n, g(e, C({
        ...r,
        method: "DELETE"
      })));
    },
    async postForm(n, r, s) {
      return A(t, n, g(e, C({
        ...s,
        method: "POST",
        body: r
      })));
    }
  };
}
function g(t, e = {}) {
  return t ? {
    ...e,
    headers: dt(t, e.headers)
  } : e;
}
function dt(t, e) {
  const n = new Headers(t);
  return new Headers(e).forEach((r, s) => n.set(s, r)), n;
}
async function A(t, e, n) {
  const r = wt(t, e), s = await fetch(r, n);
  if (!s.ok)
    throw await D(s);
  const i = await s.text();
  if (!i.trim())
    return {};
  try {
    return JSON.parse(i);
  } catch {
    throw new k(
      s.status,
      `Expected JSON from ${r}, but received ${gt(s, i)}. Check Studio:BackendBaseUrl and make sure the backend maps this API route.`
    );
  }
}
async function lt(t) {
  return (await B(t)).message;
}
async function D(t) {
  const e = await B(t);
  return new k(t.status, e.message, e.validationErrors);
}
async function B(t) {
  const e = t.headers.get("content-type") ?? "";
  if (ht(e))
    try {
      const r = await t.json(), s = _(r);
      return {
        message: ft(r) ?? pt(s) ?? `Request failed with ${t.status}.`,
        validationErrors: s
      };
    } catch {
      return { message: `Request failed with ${t.status}.`, validationErrors: null };
    }
  return { message: (await t.text()).trim() || `Request failed with ${t.status}.`, validationErrors: null };
}
function ht(t) {
  return t.toLowerCase().includes("json");
}
async function Rt(t) {
  if (t instanceof k)
    return t.message;
  if (G(t))
    try {
      return await lt(t.response.clone());
    } catch {
      return t.response.statusText || "Request failed.";
    }
  return t instanceof Error ? t.message : "Unknown error.";
}
async function jt(t) {
  if (t instanceof k)
    return t.validationErrors;
  if (!G(t))
    return null;
  try {
    const e = await t.response.clone().json();
    return _(e);
  } catch {
    return null;
  }
}
function ft(t) {
  if (typeof t.detail == "string" && t.detail.length > 0) return t.detail;
  if (typeof t.title == "string" && t.title.length > 0) return t.title;
  if (Array.isArray(t.errors) && t.errors.length > 0) return t.errors.map(String).join(" ");
  if (t.errors && typeof t.errors == "object") {
    const e = Object.values(t.errors).flatMap((n) => Array.isArray(n) ? n : [n]).map(String);
    if (e.length > 0) return e.join(" ");
  }
  return null;
}
function _(t) {
  const e = t.errors;
  if (!e || typeof e != "object" || Array.isArray(e))
    return null;
  const n = {};
  for (const [r, s] of Object.entries(e)) {
    const i = Array.isArray(s) ? s.map(String) : [String(s)];
    i.length > 0 && (n[r] = i);
  }
  return Object.keys(n).length > 0 ? n : null;
}
function pt(t) {
  return t ? Object.values(t).flat().join(" ") : null;
}
function G(t) {
  return typeof t == "object" && t !== null && "response" in t && t.response instanceof Response;
}
function wt(t, e) {
  return new URL(e, t).toString();
}
function C(t) {
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
function gt(t, e) {
  const n = t.headers.get("content-type") ?? "an unknown content type", r = e.trim(), s = r.length > 0 ? `: ${r.slice(0, 80)}` : "";
  return `${n}${s}`;
}
class k extends Error {
  constructor(e, n, r = null) {
    super(n), this.status = e, this.validationErrors = r, this.name = "StudioHttpError";
  }
  status;
  validationErrors;
}
export {
  v as AuthAdapterError,
  S as AuthConfigurationError,
  St as AuthGuard,
  mt as AuthProvider,
  bt as RequireAuth,
  k as StudioHttpError,
  Tt as createAiContributionApi,
  V as createAuthProviderManager,
  Ct as createAuthenticatedHttpClient,
  vt as createBackendAuthProviderManager,
  m as createContributionRegistry,
  xt as createEndpointContext,
  ut as createHttpClient,
  Pt as createOidcAuthAdapter,
  z as createRedirectAuthAdapter,
  at as createSignalRAccessTokenFactory,
  D as createStudioHttpError,
  Rt as describeApiError,
  lt as readStudioHttpErrorMessage,
  jt as tryExtractValidationErrors,
  kt as useAuthCapabilities,
  j as useAuthContext,
  nt as useAuthSession,
  rt as usePermissions,
  Et as withAuthenticatedSignalROptions,
  g as withDefaultHeaders
};
