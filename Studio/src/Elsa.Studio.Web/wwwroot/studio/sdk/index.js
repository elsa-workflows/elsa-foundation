import { j as f } from "../vendor/chunks/jsx-runtime.js";
import { r as u } from "../vendor/chunks/index.js";
const T = {
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
    for (const r of e.adapters ?? []) {
      if (this.adapters.has(r.id))
        throw new k(`Duplicate auth provider adapter '${r.id}'.`);
      this.adapters.set(r.id, r);
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
    const e = await this.options.bootstrap(), r = e.providers.find((s) => s.enabled && s.isDefault) ?? e.providers.find((s) => s.enabled);
    if (!r)
      throw new k("No enabled authentication provider was returned by /_elsa/identity/bootstrap.");
    const n = this.resolveProviderAdapter(r);
    return this.activeAdapter = n, n;
  }
  async getProviderAdapter(e) {
    const r = this.adapters.get(e);
    if (r)
      return r;
    const s = (await this.options.bootstrap()).providers.find((i) => i.enabled && i.id === e);
    if (!s)
      throw new k(`No auth provider adapter is registered for '${e}'.`);
    return this.resolveProviderAdapter(s);
  }
  resolveProviderAdapter(e) {
    const r = this.adapters.get(e.id);
    if (r)
      return r;
    if (!this.options.adapterFactory)
      throw new k(`No auth provider adapter is registered for '${e.id}'.`);
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
class k extends Error {
  constructor(e) {
    super(e), this.name = "AuthConfigurationError";
  }
}
function H(t) {
  const e = t.fetch ?? fetch, r = t.sessionEndpoint ?? "/_elsa/identity/session", n = t.logoutEndpoint ?? `/_elsa/identity/logout/${encodeURIComponent(t.id)}`;
  return {
    id: t.id,
    kind: t.kind,
    initialize: () => S(e, r, t),
    login: (s) => {
      const i = t.challenge;
      if (!i || i.type === "none")
        throw new v(`Provider '${t.id}' does not expose a redirect challenge.`);
      const a = "method" in i ? i.method.toUpperCase() : "GET";
      if (a !== "GET")
        throw new v(`Provider '${t.id}' exposes an unsupported ${a} challenge.`);
      const c = new URL(Z(i), R(t)), o = s?.returnUrl ?? t.location?.href ?? window.location.href;
      return c.searchParams.set("returnUrl", tt(o, s?.providerId ?? t.id, t)), (t.location ?? window.location).assign(c.toString()), Promise.resolve();
    },
    handleCallback: () => S(e, r, t),
    logout: async () => {
      const s = await e(E(n, t), { method: "POST", credentials: "include" });
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
        return S(e, r, t);
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
      return c.status ? M(c) : S(e, r, t);
    }
  };
}
async function S(t, e, r) {
  const n = await t(E(e, r), { credentials: "include", cache: "no-store" });
  if (n.status === 401)
    return T;
  if (!n.ok)
    throw new v(`Session request failed with ${n.status}.`);
  return X(n);
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
  return new URL(t, R(e)).toString();
}
function R(t) {
  return t?.baseUrl ?? t?.location?.origin ?? window.location.origin;
}
function tt(t, e, r) {
  const n = new URL(t, et(r));
  return n.searchParams.set("authProviderId", e), rt(t) ? `${n.pathname}${n.search}${n.hash}` : n.toString();
}
function et(t) {
  return t?.location?.href ?? (typeof window < "u" ? window.location.href : void 0) ?? t?.location?.origin ?? R(t);
}
function rt(t) {
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
function mt(t = {}) {
  const e = t.baseUrl ?? window.location.origin, r = t.fetch ?? fetch;
  return Q({
    bootstrap: () => I(r, e, "/_elsa/identity/bootstrap"),
    capabilities: () => I(r, e, "/_elsa/identity/capabilities"),
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
async function I(t, e, r) {
  const n = await t(new URL(r, e).toString(), {
    credentials: "include",
    cache: "no-store",
    headers: { Accept: "application/json" }
  });
  if (!n.ok)
    throw new Error(`Auth discovery request failed with ${n.status}.`);
  return await n.json();
}
const N = u.createContext(null);
function j() {
  const t = u.useContext(N);
  if (!t)
    throw new Error("Auth SDK hooks must be used within <AuthProvider>.");
  return t;
}
function kt({ manager: t, children: e }) {
  const [r, n] = u.useState(() => t.getSession() ?? z), [s, i] = u.useState(null), a = u.useRef(!1), c = u.useRef(0), o = u.useCallback((d) => a.current && c.current === d, []);
  u.useLayoutEffect(() => {
    a.current = !0;
    const d = ++c.current;
    async function h() {
      try {
        const l = await t.initialize();
        if (!o(d))
          return;
        if (n(l), l.status !== "authenticated") {
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
        o(d) && (console.error("Auth initialization failed.", l), n(T), i(null));
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
    if (n(l), l.status === "authenticated")
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
    await t.logout(), o(d) && (n(t.getSession()), i(null));
  }, [t, o]), J = u.useCallback(async () => {
    const d = ++c.current, h = await t.refresh();
    if (!o(d))
      return h;
    if (n(h), h.status === "authenticated")
      try {
        const l = await t.getCapabilities();
        o(d) && i(l);
      } catch (l) {
        o(d) && (console.error("Auth capabilities request failed.", l), i(null));
      }
    else
      i(null);
    return h;
  }, [t, o]), V = u.useMemo(() => ({
    session: r,
    capabilities: s,
    login: p,
    logout: $,
    refresh: J
  }), [s, p, $, J, r]);
  return /* @__PURE__ */ f.jsx(N.Provider, { value: V, children: e });
}
function nt() {
  return j().session;
}
function st() {
  const { permissions: t } = nt();
  return u.useMemo(() => {
    const e = new Set(t);
    return {
      has: (r) => e.has(r),
      hasAny: (r) => r.some((n) => e.has(n)),
      hasAll: (r) => r.every((n) => e.has(n))
    };
  }, [t]);
}
function St() {
  return j().capabilities;
}
function Pt({ requires: t, requireAll: e = !0, fallback: r = null, children: n }) {
  const s = st(), i = typeof t == "string" ? [t] : t ?? [];
  return i.length === 0 || (e ? s.hasAll(i) : s.hasAny(i)) ? /* @__PURE__ */ f.jsx(f.Fragment, { children: n }) : /* @__PURE__ */ f.jsx(f.Fragment, { children: r });
}
function Ct({ children: t, fallback: e = null, loginOptions: r }) {
  const { session: n, login: s } = j(), i = u.useRef(null);
  return u.useEffect(() => {
    if (n.status === "anonymous") {
      const a = it(r), c = i.current;
      if (c?.key === a && c.login === s)
        return;
      const o = { key: a, login: s };
      i.current = o, s(r).catch((p) => {
        i.current === o && (i.current = null), console.error("Auth login failed.", p);
      });
    } else
      i.current = null;
  }, [s, r, n.status]), n.status !== "authenticated" ? /* @__PURE__ */ f.jsx(f.Fragment, { children: e }) : /* @__PURE__ */ f.jsx(f.Fragment, { children: t });
}
function it(t) {
  return `${t?.providerId ?? ""}
${t?.returnUrl ?? ""}`;
}
function Et(t) {
  return H({
    ...t,
    kind: "external-oidc"
  });
}
function Tt(t, e, r = {}) {
  return {
    requestJson(n, s) {
      return y(t, n, e, r, P(s));
    },
    getJson(n, s) {
      return y(t, n, e, r, P(s));
    },
    postJson(n, s, i) {
      return y(t, n, e, r, {
        ...i,
        method: "POST",
        headers: O(i?.headers),
        body: JSON.stringify(s)
      });
    },
    putJson(n, s, i) {
      return y(t, n, e, r, {
        ...i,
        method: "PUT",
        headers: O(i?.headers),
        body: JSON.stringify(s)
      });
    },
    deleteJson(n, s) {
      return y(t, n, e, r, P({
        ...s,
        method: "DELETE"
      }));
    },
    postForm(n, s, i) {
      return y(t, n, e, r, P({
        ...i,
        method: "POST",
        body: s
      }));
    }
  };
}
const x = /* @__PURE__ */ new Map();
async function y(t, e, r, n, s) {
  const i = n.fetch ?? fetch, a = new URL(e, t).toString(), c = await i(a, await B(r, U(n, s))), o = c.status === 401 && n.refreshOnUnauthorized !== !1 ? await ot(i, a, r, U(n, s)) : c;
  if (!o.ok)
    throw await D(o);
  const p = await o.text();
  if (!p.trim())
    return {};
  try {
    return JSON.parse(p);
  } catch {
    throw new m(o.status, `Expected JSON from ${a}.`);
  }
}
async function ot(t, e, r, n) {
  return await at(e, r) ? t(e, await B(r, n)) : new Response("Authentication required.", { status: 401 });
}
async function at(t, e) {
  const r = new URL(t).origin, n = x.get(r);
  if (n)
    return n;
  const s = e.refresh().then((i) => i.status === "authenticated").finally(() => x.delete(r));
  return x.set(r, s), s;
}
async function B(t, e) {
  const r = new Headers(e?.headers), n = await t.getAccessToken();
  return n && r.set("Authorization", `Bearer ${n}`), {
    ...e,
    credentials: e?.credentials ?? "include",
    headers: r
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
function U(t, e) {
  return g(t.defaultHeaders ?? t.headers, e);
}
function ct(t, e = {}) {
  return async () => await t.getAccessToken() ?? await e.fallbackAccessTokenFactory?.() ?? e.anonymousToken ?? "";
}
function xt(t, e) {
  const r = ut(t.accessTokenFactory) ? t.accessTokenFactory.bind(t) : void 0;
  return {
    ...t,
    accessTokenFactory: ct(e, { fallbackAccessTokenFactory: r })
  };
}
function ut(t) {
  return typeof t == "function";
}
const q = 1e4;
function b() {
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
    contextProviders: b(),
    promptActions: b(),
    tools: b(),
    proposalRenderers: b(),
    surfaces: b(),
    dispatchPrompt(e) {
      for (const r of t)
        r(e);
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
    requestJson(r, n) {
      return A(t, r, g(e, C(n)));
    },
    async getJson(r, n) {
      return A(t, r, g(e, C(n)));
    },
    async postJson(r, n, s) {
      return A(t, r, g(e, {
        ...s,
        method: "POST",
        headers: F(s?.headers),
        body: JSON.stringify(n)
      }));
    },
    async putJson(r, n, s) {
      return A(t, r, g(e, {
        ...s,
        method: "PUT",
        headers: F(s?.headers),
        body: JSON.stringify(n)
      }));
    },
    async deleteJson(r, n) {
      return A(t, r, g(e, C({
        ...n,
        method: "DELETE"
      })));
    },
    async postForm(r, n, s) {
      return A(t, r, g(e, C({
        ...s,
        method: "POST",
        body: n
      })));
    }
  };
}
function g(t, e = {}) {
  return t ? {
    ...e,
    headers: lt(t, e.headers)
  } : e;
}
function lt(t, e) {
  const r = new Headers(t);
  return new Headers(e).forEach((n, s) => r.set(s, n)), r;
}
async function A(t, e, r) {
  const n = yt(t, e), s = new AbortController(), i = globalThis.setTimeout(() => s.abort(), q);
  let a;
  try {
    a = await fetch(n, {
      ...r,
      signal: ht(r?.signal, s.signal)
    });
  } catch (o) {
    throw s.signal.aborted && !r?.signal?.aborted ? new Error(`Request to ${n} timed out after ${q / 1e3} seconds. Check Studio:BackendBaseUrl and make sure the backend API is responding.`) : o;
  } finally {
    globalThis.clearTimeout(i);
  }
  if (!a.ok)
    throw await D(a);
  const c = await a.text();
  if (!c.trim())
    return {};
  try {
    return JSON.parse(c);
  } catch {
    throw new m(
      a.status,
      `Expected JSON from ${n}, but received ${At(a, c)}. Check Studio:BackendBaseUrl and make sure the backend maps this API route.`
    );
  }
}
function ht(t, e) {
  if (!t)
    return e;
  if (typeof AbortSignal.any == "function")
    return AbortSignal.any([t, e]);
  const r = new AbortController(), n = () => r.abort();
  return t.aborted || e.aborted ? r.abort() : (t.addEventListener("abort", n, { once: !0 }), e.addEventListener("abort", n, { once: !0 })), r.signal;
}
async function ft(t) {
  return (await _(t)).message;
}
async function D(t) {
  const e = await _(t);
  return new m(t.status, e.message, e.validationErrors);
}
async function _(t) {
  const e = t.headers.get("content-type") ?? "";
  if (pt(e))
    try {
      const n = await t.json(), s = G(n);
      return {
        message: wt(n) ?? gt(s) ?? `Request failed with ${t.status}.`,
        validationErrors: s
      };
    } catch {
      return { message: `Request failed with ${t.status}.`, validationErrors: null };
    }
  return { message: (await t.text()).trim() || `Request failed with ${t.status}.`, validationErrors: null };
}
function pt(t) {
  return t.toLowerCase().includes("json");
}
async function $t(t) {
  if (t instanceof m)
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
  if (t instanceof m)
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
  if (Array.isArray(t.errors) && t.errors.length > 0) return t.errors.map(String).join(" ");
  if (t.errors && typeof t.errors == "object") {
    const e = Object.values(t.errors).flatMap((r) => Array.isArray(r) ? r : [r]).map(String);
    if (e.length > 0) return e.join(" ");
  }
  return null;
}
function G(t) {
  const e = t.errors;
  if (!e || typeof e != "object" || Array.isArray(e))
    return null;
  const r = {};
  for (const [n, s] of Object.entries(e)) {
    const i = Array.isArray(s) ? s.map(String) : [String(s)];
    i.length > 0 && (r[n] = i);
  }
  return Object.keys(r).length > 0 ? r : null;
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
function C(t) {
  const e = new Headers(t?.headers);
  return e.has("Accept") || e.set("Accept", "application/json"), {
    ...t,
    cache: t?.cache ?? "no-store",
    headers: e
  };
}
function F(t) {
  const e = new Headers(t);
  return e.has("Content-Type") || e.set("Content-Type", "application/json"), e.has("Accept") || e.set("Accept", "application/json"), e;
}
function At(t, e) {
  const r = t.headers.get("content-type") ?? "an unknown content type", n = e.trim(), s = n.length > 0 ? `: ${n.slice(0, 80)}` : "";
  return `${r}${s}`;
}
class m extends Error {
  constructor(e, r, n = null) {
    super(r), this.status = e, this.validationErrors = n, this.name = "StudioHttpError";
  }
  status;
  validationErrors;
}
export {
  v as AuthAdapterError,
  k as AuthConfigurationError,
  Pt as AuthGuard,
  kt as AuthProvider,
  Ct as RequireAuth,
  m as StudioHttpError,
  Rt as createAiContributionApi,
  Q as createAuthProviderManager,
  Tt as createAuthenticatedHttpClient,
  mt as createBackendAuthProviderManager,
  b as createContributionRegistry,
  jt as createEndpointContext,
  dt as createHttpClient,
  Et as createOidcAuthAdapter,
  H as createRedirectAuthAdapter,
  ct as createSignalRAccessTokenFactory,
  D as createStudioHttpError,
  $t as describeApiError,
  ft as readStudioHttpErrorMessage,
  Jt as tryExtractValidationErrors,
  St as useAuthCapabilities,
  j as useAuthContext,
  nt as useAuthSession,
  st as usePermissions,
  xt as withAuthenticatedSignalROptions,
  g as withDefaultHeaders
};
