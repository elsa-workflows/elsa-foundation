import { j as f } from "../vendor/chunks/jsx-runtime.js";
import { r as d } from "../vendor/chunks/index.js";
const C = {
  status: "anonymous",
  roles: [],
  permissions: []
}, z = {
  status: "unknown",
  roles: [],
  permissions: []
};
function Q(t) {
  return new W(t);
}
class W {
  constructor(e) {
    this.options = e;
    for (const n of e.adapters ?? []) {
      if (this.adapters.has(n.id))
        throw new m(`Duplicate auth provider adapter '${n.id}'.`);
      this.adapters.set(n.id, n);
    }
  }
  options;
  adapters = /* @__PURE__ */ new Map();
  activeAdapter = null;
  pendingLoginProviderId = null;
  session = z;
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
    await (await this.resolveActiveAdapter()).logout(), this.session = C;
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
      throw new m("No enabled authentication provider was returned by /_elsa/identity/bootstrap.");
    const r = this.resolveProviderAdapter(n);
    return this.activeAdapter = r, r;
  }
  async getProviderAdapter(e) {
    const n = this.adapters.get(e);
    if (n)
      return n;
    const s = (await this.options.bootstrap()).providers.find((i) => i.enabled && i.id === e);
    if (!s)
      throw new m(`No auth provider adapter is registered for '${e}'.`);
    return this.resolveProviderAdapter(s);
  }
  resolveProviderAdapter(e) {
    const n = this.adapters.get(e.id);
    if (n)
      return n;
    if (!this.options.adapterFactory)
      throw new m(`No auth provider adapter is registered for '${e.id}'.`);
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
class m extends Error {
  constructor(e) {
    super(e), this.name = "AuthConfigurationError";
  }
}
function H(t) {
  const e = t.fetch ?? fetch, n = t.sessionEndpoint ?? "/_elsa/identity/session", r = t.logoutEndpoint ?? `/_elsa/identity/logout/${encodeURIComponent(t.id)}`;
  return {
    id: t.id,
    kind: t.kind,
    initialize: () => k(e, n, t),
    login: (s) => {
      const i = t.challenge;
      if (!i || i.type === "none")
        throw new A(`Provider '${t.id}' does not expose a redirect challenge.`);
      const o = "method" in i ? i.method.toUpperCase() : "GET";
      if (o !== "GET")
        throw new A(`Provider '${t.id}' exposes an unsupported ${o} challenge.`);
      const c = new URL(Z(i), x(t)), a = s?.returnUrl ?? t.location?.href ?? window.location.href;
      return c.searchParams.set("returnUrl", tt(a, s?.providerId ?? t.id, t)), (t.location ?? window.location).assign(c.toString()), Promise.resolve();
    },
    handleCallback: () => k(e, n, t),
    logout: async () => {
      const s = await e(E(r, t), { method: "POST", credentials: "include" });
      if (!s.ok)
        throw new A(`Sign-out failed with ${s.status}.`);
    },
    getAccessToken: async () => {
      if (!t.tokenEndpoint)
        return null;
      const s = await e(E(t.tokenEndpoint, t), { credentials: "include", cache: "no-store" });
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
        return k(e, n, t);
      const o = await e(E(i, t), {
        method: "POST",
        credentials: "include",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json"
        },
        body: JSON.stringify({ refreshToken: s })
      });
      if (o.status === 401)
        return C;
      if (!o.ok)
        throw new A(`Session refresh failed with ${o.status}.`);
      const c = await o.json();
      return c.status ? M(c) : k(e, n, t);
    }
  };
}
async function k(t, e, n) {
  const r = await t(E(e, n), { credentials: "include", cache: "no-store" });
  if (r.status === 401)
    return C;
  if (!r.ok)
    throw new A(`Session request failed with ${r.status}.`);
  return X(r);
}
async function X(t) {
  const e = await t.json();
  return M(e);
}
function M(t) {
  const e = Y(t.status) ? t.status : "anonymous";
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
function Y(t) {
  return t === "unknown" || t === "anonymous" || t === "authenticated";
}
function Z(t) {
  return "loginPath" in t ? t.loginPath : t.url;
}
function E(t, e) {
  return new URL(t, x(e)).toString();
}
function x(t) {
  return t?.baseUrl ?? t?.location?.origin ?? window.location.origin;
}
function tt(t, e, n) {
  const r = new URL(t, et(n));
  return r.searchParams.set("authProviderId", e), nt(t) ? `${r.pathname}${r.search}${r.hash}` : r.toString();
}
function et(t) {
  return t?.location?.href ?? (typeof window < "u" ? window.location.href : void 0) ?? t?.location?.origin ?? x(t);
}
function nt(t) {
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
function mt(t = {}) {
  const e = t.baseUrl ?? window.location.origin, n = t.fetch ?? fetch;
  return Q({
    bootstrap: () => I(n, e, "/_elsa/identity/bootstrap"),
    capabilities: () => I(n, e, "/_elsa/identity/capabilities"),
    isCallback: t.isCallback,
    getCallbackProviderId: t.getCallbackProviderId,
    adapterFactory: (r) => H({
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
const N = d.createContext(null);
function R() {
  const t = d.useContext(N);
  if (!t)
    throw new Error("Auth SDK hooks must be used within <AuthProvider>.");
  return t;
}
function kt({ manager: t, children: e }) {
  const [n, r] = d.useState(() => t.getSession() ?? z), [s, i] = d.useState(null), o = d.useRef(!1), c = d.useRef(0), a = d.useCallback((u) => o.current && c.current === u, []), h = d.useCallback(async (u) => {
    if (a(u)) {
      i(null);
      try {
        const l = await t.getCapabilities();
        a(u) && i(l);
      } catch (l) {
        a(u) && (console.error("Auth capabilities request failed.", l), i(null));
      }
    }
  }, [t, a]);
  d.useLayoutEffect(() => {
    o.current = !0;
    const u = ++c.current;
    async function l() {
      try {
        const p = await t.initialize();
        if (!a(u))
          return;
        if (r(p), p.status !== "authenticated") {
          i(null);
          return;
        }
        await h(u);
      } catch (p) {
        a(u) && (console.error("Auth initialization failed.", p), r(C), i(null));
      }
    }
    return l(), () => {
      o.current = !1, c.current += 1;
    };
  }, [h, t, a]);
  const j = d.useCallback(async (u) => {
    const l = ++c.current;
    if (await t.login(u), !a(l))
      return;
    const p = t.getSession();
    r(p), p.status === "authenticated" ? await h(l) : i(null);
  }, [h, t, a]), $ = d.useCallback(async () => {
    const u = ++c.current;
    await t.logout(), a(u) && (r(t.getSession()), i(null));
  }, [t, a]), J = d.useCallback(async () => {
    const u = ++c.current, l = await t.refresh();
    return a(u) && (r(l), l.status === "authenticated" ? await h(u) : i(null)), l;
  }, [h, t, a]), V = d.useMemo(() => ({
    session: n,
    capabilities: s,
    login: j,
    logout: $,
    refresh: J
  }), [s, j, $, J, n]);
  return /* @__PURE__ */ f.jsx(N.Provider, { value: V, children: e });
}
function rt() {
  return R().session;
}
function st() {
  const { permissions: t } = rt();
  return d.useMemo(() => {
    const e = new Set(t);
    return {
      has: (n) => e.has(n),
      hasAny: (n) => n.some((r) => e.has(r)),
      hasAll: (n) => n.every((r) => e.has(r))
    };
  }, [t]);
}
function St() {
  return R().capabilities;
}
function Pt({ requires: t, requireAll: e = !0, fallback: n = null, children: r }) {
  const s = st(), i = typeof t == "string" ? [t] : t ?? [];
  return i.length === 0 || (e ? s.hasAll(i) : s.hasAny(i)) ? /* @__PURE__ */ f.jsx(f.Fragment, { children: r }) : /* @__PURE__ */ f.jsx(f.Fragment, { children: n });
}
function Et({ children: t, fallback: e = null, loginOptions: n }) {
  const { session: r, login: s } = R(), i = d.useRef(null);
  return d.useEffect(() => {
    if (r.status === "anonymous") {
      const o = it(n), c = i.current;
      if (c?.key === o && c.login === s)
        return;
      const a = { key: o, login: s };
      i.current = a, s(n).catch((h) => {
        i.current === a && (i.current = null), console.error("Auth login failed.", h);
      });
    } else
      i.current = null;
  }, [s, n, r.status]), r.status !== "authenticated" ? /* @__PURE__ */ f.jsx(f.Fragment, { children: e }) : /* @__PURE__ */ f.jsx(f.Fragment, { children: t });
}
function it(t) {
  return `${t?.providerId ?? ""}
${t?.returnUrl ?? ""}`;
}
function Ct(t) {
  return H({
    ...t,
    kind: "external-oidc"
  });
}
function Tt(t, e, n = {}) {
  return {
    requestJson(r, s) {
      return g(t, r, e, n, S(s));
    },
    getJson(r, s) {
      return g(t, r, e, n, S(s));
    },
    postJson(r, s, i) {
      return g(t, r, e, n, {
        ...i,
        method: "POST",
        headers: O(i?.headers),
        body: JSON.stringify(s)
      });
    },
    putJson(r, s, i) {
      return g(t, r, e, n, {
        ...i,
        method: "PUT",
        headers: O(i?.headers),
        body: JSON.stringify(s)
      });
    },
    deleteJson(r, s) {
      return g(t, r, e, n, S({
        ...s,
        method: "DELETE"
      }));
    },
    postForm(r, s, i) {
      return g(t, r, e, n, S({
        ...i,
        method: "POST",
        body: s
      }));
    }
  };
}
const T = /* @__PURE__ */ new Map();
async function g(t, e, n, r, s) {
  const i = r.fetch ?? fetch, o = new URL(e, t).toString(), c = await i(o, await B(n, U(r, s))), a = c.status === 401 && r.refreshOnUnauthorized !== !1 ? await ot(i, o, n, U(r, s)) : c;
  if (!a.ok)
    throw await D(a);
  const h = await a.text();
  if (!h.trim())
    return {};
  try {
    return JSON.parse(h);
  } catch {
    throw new b(a.status, `Expected JSON from ${o}.`);
  }
}
async function ot(t, e, n, r) {
  return await at(e, n) ? t(e, await B(n, r)) : new Response("Authentication required.", { status: 401 });
}
async function at(t, e) {
  const n = new URL(t).origin, r = T.get(n);
  if (r)
    return r;
  const s = e.refresh().then((i) => i.status === "authenticated").finally(() => T.delete(n));
  return T.set(n, s), s;
}
async function B(t, e) {
  const n = new Headers(e?.headers), r = await t.getAccessToken();
  return r && n.set("Authorization", `Bearer ${r}`), {
    ...e,
    credentials: e?.credentials ?? "include",
    headers: n
  };
}
function S(t) {
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
function U(t, e) {
  return w(t.defaultHeaders ?? t.headers, e);
}
function ct(t, e = {}) {
  return async () => await t.getAccessToken() ?? await e.fallbackAccessTokenFactory?.() ?? e.anonymousToken ?? "";
}
function xt(t, e) {
  const n = ut(t.accessTokenFactory) ? t.accessTokenFactory.bind(t) : void 0;
  return {
    ...t,
    accessTokenFactory: ct(e, { fallbackAccessTokenFactory: n })
  };
}
function ut(t) {
  return typeof t == "function";
}
const F = 1e4;
function v() {
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
function Rt() {
  const t = /* @__PURE__ */ new Set();
  return {
    contextProviders: v(),
    promptActions: v(),
    tools: v(),
    proposalRenderers: v(),
    surfaces: v(),
    dispatchPrompt(e) {
      for (const n of t)
        n(e);
    },
    onPrompt(e) {
      return t.add(e), () => t.delete(e);
    }
  };
}
function jt(t, e = {}) {
  return {
    baseUrl: t,
    headers: e.headers,
    http: dt(t, e.headers)
  };
}
function dt(t, e) {
  return {
    requestJson(n, r) {
      return y(t, n, w(e, P(r)));
    },
    async getJson(n, r) {
      return y(t, n, w(e, P(r)));
    },
    async postJson(n, r, s) {
      return y(t, n, w(e, {
        ...s,
        method: "POST",
        headers: q(s?.headers),
        body: JSON.stringify(r)
      }));
    },
    async putJson(n, r, s) {
      return y(t, n, w(e, {
        ...s,
        method: "PUT",
        headers: q(s?.headers),
        body: JSON.stringify(r)
      }));
    },
    async deleteJson(n, r) {
      return y(t, n, w(e, P({
        ...r,
        method: "DELETE"
      })));
    },
    async postForm(n, r, s) {
      return y(t, n, w(e, P({
        ...s,
        method: "POST",
        body: r
      })));
    }
  };
}
function w(t, e = {}) {
  return t ? {
    ...e,
    headers: lt(t, e.headers)
  } : e;
}
function lt(t, e) {
  const n = new Headers(t);
  return new Headers(e).forEach((r, s) => n.set(s, r)), n;
}
async function y(t, e, n) {
  const r = yt(t, e), s = new AbortController(), i = globalThis.setTimeout(() => s.abort(), F);
  let o;
  try {
    o = await fetch(r, {
      ...n,
      signal: ht(n?.signal, s.signal)
    });
  } catch (a) {
    throw s.signal.aborted && !n?.signal?.aborted ? new Error(`Request to ${r} timed out after ${F / 1e3} seconds. Check Studio:BackendBaseUrl and make sure the backend API is responding.`) : a;
  } finally {
    globalThis.clearTimeout(i);
  }
  if (!o.ok)
    throw await D(o);
  const c = await o.text();
  if (!c.trim())
    return {};
  try {
    return JSON.parse(c);
  } catch {
    throw new b(
      o.status,
      `Expected JSON from ${r}, but received ${At(o, c)}. Check Studio:BackendBaseUrl and make sure the backend maps this API route.`
    );
  }
}
function ht(t, e) {
  if (!t)
    return e;
  if (typeof AbortSignal.any == "function")
    return AbortSignal.any([t, e]);
  const n = new AbortController(), r = () => n.abort();
  return t.aborted || e.aborted ? n.abort() : (t.addEventListener("abort", r, { once: !0 }), e.addEventListener("abort", r, { once: !0 })), n.signal;
}
async function ft(t) {
  return (await _(t)).message;
}
async function D(t) {
  const e = await _(t);
  return new b(t.status, e.message, e.validationErrors, e.payload);
}
async function _(t) {
  const e = t.headers.get("content-type") ?? "";
  if (pt(e))
    try {
      const r = await t.json(), s = G(r);
      return {
        message: wt(r) ?? gt(s) ?? `Request failed with ${t.status}.`,
        validationErrors: s,
        payload: r
      };
    } catch {
      return { message: `Request failed with ${t.status}.`, validationErrors: null, payload: null };
    }
  return { message: (await t.text()).trim() || `Request failed with ${t.status}.`, validationErrors: null, payload: null };
}
function pt(t) {
  return t.toLowerCase().includes("json");
}
async function $t(t) {
  if (t instanceof b)
    return t.message;
  if (K(t))
    try {
      return await ft(t.response.clone());
    } catch {
      return t.response.statusText || "Request failed.";
    }
  return t instanceof Error ? t.message : "Unknown error.";
}
async function Jt(t) {
  if (t instanceof b)
    return t.validationErrors;
  if (!K(t))
    return null;
  try {
    const e = await t.response.clone().json();
    return G(e);
  } catch {
    return null;
  }
}
function wt(t) {
  if (typeof t.detail == "string" && t.detail.length > 0) return t.detail;
  if (typeof t.title == "string" && t.title.length > 0) return t.title;
  if (typeof t.reason == "string" && t.reason.length > 0) return t.reason;
  if (Array.isArray(t.errors) && t.errors.length > 0) return t.errors.map(String).join(" ");
  if (t.errors && typeof t.errors == "object") {
    const e = Object.values(t.errors).flatMap((n) => Array.isArray(n) ? n : [n]).map(String);
    if (e.length > 0) return e.join(" ");
  }
  return null;
}
function G(t) {
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
function gt(t) {
  return t ? Object.values(t).flat().join(" ") : null;
}
function K(t) {
  return typeof t == "object" && t !== null && "response" in t && t.response instanceof Response;
}
function yt(t, e) {
  return new URL(e, t).toString();
}
function P(t) {
  const e = new Headers(t?.headers);
  return e.has("Accept") || e.set("Accept", "application/json"), {
    ...t,
    cache: t?.cache ?? "no-store",
    headers: e
  };
}
function q(t) {
  const e = new Headers(t);
  return e.has("Content-Type") || e.set("Content-Type", "application/json"), e.has("Accept") || e.set("Accept", "application/json"), e;
}
function At(t, e) {
  const n = t.headers.get("content-type") ?? "an unknown content type", r = e.trim(), s = r.length > 0 ? `: ${r.slice(0, 80)}` : "";
  return `${n}${s}`;
}
class b extends Error {
  constructor(e, n, r = null, s = null) {
    super(n), this.status = e, this.validationErrors = r, this.payload = s, this.name = "StudioHttpError";
  }
  status;
  validationErrors;
  payload;
}
export {
  A as AuthAdapterError,
  m as AuthConfigurationError,
  Pt as AuthGuard,
  kt as AuthProvider,
  Et as RequireAuth,
  b as StudioHttpError,
  Rt as createAiContributionApi,
  Q as createAuthProviderManager,
  Tt as createAuthenticatedHttpClient,
  mt as createBackendAuthProviderManager,
  v as createContributionRegistry,
  jt as createEndpointContext,
  dt as createHttpClient,
  Ct as createOidcAuthAdapter,
  H as createRedirectAuthAdapter,
  ct as createSignalRAccessTokenFactory,
  D as createStudioHttpError,
  $t as describeApiError,
  ft as readStudioHttpErrorMessage,
  Jt as tryExtractValidationErrors,
  St as useAuthCapabilities,
  R as useAuthContext,
  rt as useAuthSession,
  st as usePermissions,
  xt as withAuthenticatedSignalROptions,
  w as withDefaultHeaders
};
