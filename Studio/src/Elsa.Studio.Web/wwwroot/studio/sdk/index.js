import { j as w } from "../vendor/chunks/jsx-runtime.js";
import { r as f } from "../vendor/chunks/index.js";
const $ = {
  status: "anonymous",
  roles: [],
  permissions: []
}, K = {
  status: "unknown",
  roles: [],
  permissions: []
};
function re(e) {
  return new ne(e);
}
class ne {
  constructor(t) {
    this.options = t;
    for (const r of t.adapters ?? []) {
      if (this.adapters.has(r.id))
        throw new C(`Duplicate auth provider adapter '${r.id}'.`);
      this.adapters.set(r.id, r);
    }
  }
  options;
  adapters = /* @__PURE__ */ new Map();
  activeAdapter = null;
  pendingLoginProviderId = null;
  session = K;
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
    const t = await this.resolveActiveAdapter();
    return await this.applySession(await t.initialize(), t), this.pendingLoginProviderId = null, this.session;
  }
  async login(t) {
    const r = t?.providerId ? await this.getProviderAdapter(t.providerId) : await this.resolveActiveAdapter();
    this.pendingLoginProviderId = r.id;
    try {
      const n = await r.login({ ...t, providerId: r.id });
      n ? (await this.applySession(n, r), this.pendingLoginProviderId = null) : this.session.status !== "authenticated" && (this.activeAdapter = r);
    } catch (n) {
      throw this.pendingLoginProviderId = null, n;
    }
  }
  async handleCallback(t) {
    const r = t ? await this.getProviderAdapter(t) : await this.resolveActiveAdapter();
    return await this.applySession(await r.handleCallback(), r), this.pendingLoginProviderId = null, this.session;
  }
  async logout() {
    await (await this.resolveActiveAdapter()).logout(), this.session = $;
  }
  async getAccessToken() {
    return (await this.resolveActiveAdapter()).getAccessToken();
  }
  async refresh() {
    const t = await this.resolveActiveAdapter();
    return await this.applySession(await t.refresh(), t), this.session;
  }
  async resolveActiveAdapter() {
    if (this.activeAdapter)
      return this.activeAdapter;
    const t = await this.options.bootstrap(), r = t.providers.find((i) => i.enabled && i.isDefault) ?? t.providers.find((i) => i.enabled);
    if (!r)
      throw new C("No enabled authentication provider was returned by /_elsa/identity/bootstrap.");
    const n = this.resolveProviderAdapter(r);
    return this.activeAdapter = n, n;
  }
  async getProviderAdapter(t) {
    const r = this.adapters.get(t);
    if (r)
      return r;
    const i = (await this.options.bootstrap()).providers.find((s) => s.enabled && s.id === t);
    if (!i)
      throw new C(`No auth provider adapter is registered for '${t}'.`);
    return this.resolveProviderAdapter(i);
  }
  resolveProviderAdapter(t) {
    const r = this.adapters.get(t.id);
    if (r)
      return r;
    if (!this.options.adapterFactory)
      throw new C(`No auth provider adapter is registered for '${t.id}'.`);
    const n = this.options.adapterFactory(t);
    return this.adapters.set(t.id, n), n;
  }
  getCallbackProviderId() {
    const t = this.options.getCallbackProviderId?.();
    return t || (this.pendingLoginProviderId ? this.pendingLoginProviderId : typeof window > "u" ? null : new URLSearchParams(window.location.search).get("authProviderId"));
  }
  async applySession(t, r) {
    const n = t.provider?.id ? await this.getProviderAdapter(t.provider.id) : r;
    this.session = t, this.activeAdapter = n;
  }
}
class C extends Error {
  constructor(t) {
    super(t), this.name = "AuthConfigurationError";
  }
}
function _(e) {
  const t = e.fetch ?? fetch, r = e.sessionEndpoint ?? "/_elsa/identity/session", n = e.logoutEndpoint ?? `/_elsa/identity/logout/${encodeURIComponent(e.id)}`;
  return {
    id: e.id,
    kind: e.kind,
    initialize: () => x(t, r, e),
    login: (i) => {
      const s = e.challenge;
      if (!s || s.type === "none")
        throw new b(`Provider '${e.id}' does not expose a redirect challenge.`);
      const o = "method" in s ? s.method.toUpperCase() : "GET";
      if (o !== "GET")
        throw new b(`Provider '${e.id}' exposes an unsupported ${o} challenge.`);
      const c = new URL(oe(s), L(e)), a = i?.returnUrl ?? e.location?.href ?? window.location.href;
      return c.searchParams.set("returnUrl", ae(a, i?.providerId ?? e.id, e)), (e.location ?? window.location).assign(c.toString()), Promise.resolve();
    },
    handleCallback: () => x(t, r, e),
    logout: async () => {
      const i = await t(I(n, e), { method: "POST", credentials: "include" });
      if (!i.ok)
        throw new b(`Sign-out failed with ${i.status}.`);
    },
    getAccessToken: async () => {
      if (!e.tokenEndpoint)
        return null;
      const i = await t(I(e.tokenEndpoint, e), { credentials: "include", cache: "no-store" });
      if (i.status === 401)
        return null;
      if (!i.ok)
        throw new b(`Access-token request failed with ${i.status}.`);
      const s = await i.json();
      return typeof s.accessToken == "string" ? s.accessToken : null;
    },
    refresh: async () => {
      const i = await e.getRefreshToken?.(), s = e.refreshEndpoint;
      if (!s || !i)
        return x(t, r, e);
      const o = await t(I(s, e), {
        method: "POST",
        credentials: "include",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json"
        },
        body: JSON.stringify({ refreshToken: i })
      });
      if (o.status === 401)
        return $;
      if (!o.ok)
        throw new b(`Session refresh failed with ${o.status}.`);
      const c = await o.json();
      return c.status ? G(c) : x(t, r, e);
    }
  };
}
async function x(e, t, r) {
  const n = await e(I(t, r), { credentials: "include", cache: "no-store" });
  if (n.status === 401)
    return $;
  if (!n.ok)
    throw new b(`Session request failed with ${n.status}.`);
  return ie(n);
}
async function ie(e) {
  const t = await e.json();
  return G(t);
}
function G(e) {
  const t = se(e.status) ? e.status : "anonymous";
  return {
    ...e,
    status: t,
    roles: O(e.roles),
    permissions: O(e.permissions)
  };
}
function O(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string") : [];
}
function se(e) {
  return e === "unknown" || e === "anonymous" || e === "authenticated";
}
function oe(e) {
  return "loginPath" in e ? e.loginPath : e.url;
}
function I(e, t) {
  return new URL(e, L(t)).toString();
}
function L(e) {
  return e?.baseUrl ?? e?.location?.origin ?? window.location.origin;
}
function ae(e, t, r) {
  const n = new URL(e, ce(r));
  return n.searchParams.set("authProviderId", t), de(e) ? `${n.pathname}${n.search}${n.hash}` : n.toString();
}
function ce(e) {
  return e?.location?.href ?? (typeof window < "u" ? window.location.href : void 0) ?? e?.location?.origin ?? L(e);
}
function de(e) {
  try {
    return new URL(e), !1;
  } catch {
    return !0;
  }
}
class b extends Error {
  constructor(t) {
    super(t), this.name = "AuthAdapterError";
  }
}
function Le(e = {}) {
  const t = e.baseUrl ?? window.location.origin, r = e.fetch ?? fetch;
  return re({
    bootstrap: () => N(r, t, "/_elsa/identity/bootstrap"),
    capabilities: () => N(r, t, "/_elsa/identity/capabilities"),
    isCallback: e.isCallback,
    getCallbackProviderId: e.getCallbackProviderId,
    adapterFactory: (n) => _({
      id: n.id,
      kind: n.kind,
      baseUrl: t,
      challenge: n.challenge,
      fetch: r
    })
  });
}
async function N(e, t, r) {
  const n = await e(new URL(r, t).toString(), {
    credentials: "include",
    cache: "no-store",
    headers: { Accept: "application/json" }
  });
  if (!n.ok)
    throw new Error(`Auth discovery request failed with ${n.status}.`);
  return await n.json();
}
const V = f.createContext(null);
function D() {
  const e = f.useContext(V);
  if (!e)
    throw new Error("Auth SDK hooks must be used within <AuthProvider>.");
  return e;
}
function De({ manager: e, children: t }) {
  const [r, n] = f.useState(() => e.getSession() ?? K), [i, s] = f.useState(null), o = f.useRef(!1), c = f.useRef(0), a = f.useCallback((l) => o.current && c.current === l, []), h = f.useCallback(async (l) => {
    if (a(l)) {
      s(null);
      try {
        const p = await e.getCapabilities();
        a(l) && s(p);
      } catch (p) {
        a(l) && (console.error("Auth capabilities request failed.", p), s(null));
      }
    }
  }, [e, a]);
  f.useLayoutEffect(() => {
    o.current = !0;
    const l = ++c.current;
    async function p() {
      try {
        const g = await e.initialize();
        if (!a(l))
          return;
        if (n(g), g.status !== "authenticated") {
          s(null);
          return;
        }
        await h(l);
      } catch (g) {
        a(l) && (console.error("Auth initialization failed.", g), n($), s(null));
      }
    }
    return p(), () => {
      o.current = !1, c.current += 1;
    };
  }, [h, e, a]);
  const k = f.useCallback(async (l) => {
    const p = ++c.current;
    if (await e.login(l), !a(p))
      return;
    const g = e.getSession();
    n(g), g.status === "authenticated" ? await h(p) : s(null);
  }, [h, e, a]), U = f.useCallback(async () => {
    const l = ++c.current;
    await e.logout(), a(l) && (n(e.getSession()), s(null));
  }, [e, a]), F = f.useCallback(async () => {
    const l = ++c.current, p = await e.refresh();
    return a(l) && (n(p), p.status === "authenticated" ? await h(l) : s(null)), p;
  }, [h, e, a]), te = f.useMemo(() => ({
    session: r,
    capabilities: i,
    login: k,
    logout: U,
    refresh: F
  }), [i, k, U, F, r]);
  return /* @__PURE__ */ w.jsx(V.Provider, { value: te, children: t });
}
function ue() {
  return D().session;
}
function le() {
  const { permissions: e } = ue();
  return f.useMemo(() => {
    const t = new Set(e);
    return {
      has: (r) => t.has(r),
      hasAny: (r) => r.some((n) => t.has(n)),
      hasAll: (r) => r.every((n) => t.has(n))
    };
  }, [e]);
}
function Ue() {
  return D().capabilities;
}
function Fe({ requires: e, requireAll: t = !0, fallback: r = null, children: n }) {
  const i = le(), s = typeof e == "string" ? [e] : e ?? [];
  return s.length === 0 || (t ? i.hasAll(s) : i.hasAny(s)) ? /* @__PURE__ */ w.jsx(w.Fragment, { children: n }) : /* @__PURE__ */ w.jsx(w.Fragment, { children: r });
}
function Oe({ children: e, fallback: t = null, loginOptions: r }) {
  const { session: n, login: i } = D(), s = f.useRef(null);
  return f.useEffect(() => {
    if (n.status === "anonymous") {
      const o = fe(r), c = s.current;
      if (c?.key === o && c.login === i)
        return;
      const a = { key: o, login: i };
      s.current = a, i(r).catch((h) => {
        s.current === a && (s.current = null), console.error("Auth login failed.", h);
      });
    } else
      s.current = null;
  }, [i, r, n.status]), n.status !== "authenticated" ? /* @__PURE__ */ w.jsx(w.Fragment, { children: t }) : /* @__PURE__ */ w.jsx(w.Fragment, { children: e });
}
function fe(e) {
  return `${e?.providerId ?? ""}
${e?.returnUrl ?? ""}`;
}
function Ne(e) {
  return _({
    ...e,
    kind: "external-oidc"
  });
}
function qe(e, t, r = {}) {
  return {
    requestJson(n, i) {
      return v(e, n, t, r, E(i));
    },
    getJson(n, i) {
      return v(e, n, t, r, E(i));
    },
    postJson(n, i, s) {
      return v(e, n, t, r, {
        ...s,
        method: "POST",
        headers: q(s?.headers),
        body: JSON.stringify(i)
      });
    },
    putJson(n, i, s) {
      return v(e, n, t, r, {
        ...s,
        method: "PUT",
        headers: q(s?.headers),
        body: JSON.stringify(i)
      });
    },
    deleteJson(n, i) {
      return v(e, n, t, r, E({
        ...i,
        method: "DELETE"
      }));
    },
    postForm(n, i, s) {
      return v(e, n, t, r, E({
        ...s,
        method: "POST",
        body: i
      }));
    }
  };
}
const J = /* @__PURE__ */ new Map();
async function v(e, t, r, n, i) {
  const s = n.fetch ?? fetch, o = new URL(t, e).toString(), c = await s(o, await Q(r, M(n, i))), a = c.status === 401 && n.refreshOnUnauthorized !== !1 ? await he(s, o, r, M(n, i)) : c;
  if (!a.ok)
    throw await X(a);
  const h = await a.text();
  if (!h.trim())
    return {};
  try {
    return JSON.parse(h);
  } catch {
    throw new P(a.status, `Expected JSON from ${o}.`);
  }
}
async function he(e, t, r, n) {
  return await pe(t, r) ? e(t, await Q(r, n)) : new Response("Authentication required.", { status: 401 });
}
async function pe(e, t) {
  const r = new URL(e).origin, n = J.get(r);
  if (n)
    return n;
  const i = t.refresh().then((s) => s.status === "authenticated").finally(() => J.delete(r));
  return J.set(r, i), i;
}
async function Q(e, t) {
  const r = new Headers(t?.headers), n = await e.getAccessToken();
  return n && r.set("Authorization", `Bearer ${n}`), {
    ...t,
    credentials: t?.credentials ?? "include",
    headers: r
  };
}
function E(e) {
  const t = new Headers(e?.headers);
  return t.has("Accept") || t.set("Accept", "application/json"), {
    ...e,
    cache: e?.cache ?? "no-store",
    headers: t
  };
}
function q(e) {
  const t = new Headers(e);
  return t.has("Content-Type") || t.set("Content-Type", "application/json"), t.has("Accept") || t.set("Accept", "application/json"), t;
}
function M(e, t) {
  return y(e.defaultHeaders ?? e.headers, t);
}
function we(e, t = {}) {
  return async () => await e.getAccessToken() ?? await t.fallbackAccessTokenFactory?.() ?? t.anonymousToken ?? "";
}
function Me(e, t) {
  const r = ge(e.accessTokenFactory) ? e.accessTokenFactory.bind(e) : void 0;
  return {
    ...e,
    accessTokenFactory: we(t, { fallbackAccessTokenFactory: r })
  };
}
function ge(e) {
  return typeof e == "function";
}
const z = 1e4, u = {
  featureAreas: "studio.feature-areas",
  navigation: "studio.navigation",
  routes: "studio.routes",
  dashboardWidgets: "studio.dashboard.widgets",
  panels: "studio.panels",
  toolbarActions: "studio.toolbar.actions",
  activityEditors: "workflow.activity.editors",
  propertyEditors: "workflow.activity.property-editors",
  expressionEditors: "workflow.expression-editors",
  settingEditors: "studio.setting-editors",
  agentContextProviders: "studio.weaver.context-providers",
  agentPromptStarters: "studio.weaver.prompt-starters",
  agentCapabilities: "studio.weaver.capabilities",
  agentActions: "studio.weaver.actions",
  workflowDesignerNodeRenderers: "workflow.designer.node-renderers",
  workflowDesignerToolboxItems: "workflow.designer.toolbox-items",
  workflowDesignerPanels: "workflow.designer.panels",
  aiContextProviders: "studio.ai.context-providers",
  aiPromptActions: "studio.ai.prompt-actions",
  aiTools: "studio.ai.tools",
  aiProposalRenderers: "studio.ai.proposal-renderers",
  aiSurfaces: "studio.ai.surfaces",
  diagnostics: "studio.diagnostics",
  diagnosticsWidgets: "studio.diagnostics.widgets"
}, m = {
  featureAreas: { id: u.featureAreas, kind: "feature-area", title: "Feature areas", owner: d() },
  navigation: { id: u.navigation, kind: "navigation", title: "Navigation", owner: d() },
  routes: { id: u.routes, kind: "route", title: "Routes", owner: d() },
  dashboardWidgets: { id: u.dashboardWidgets, kind: "dashboard-widget", title: "Dashboard widgets", owner: d() },
  panels: { id: u.panels, kind: "panel", title: "Panels", owner: d() },
  toolbarActions: { id: u.toolbarActions, kind: "toolbar-action", title: "Toolbar actions", owner: d() },
  activityEditors: { id: u.activityEditors, kind: "activity-editor", title: "Activity editors", owner: d() },
  propertyEditors: { id: u.propertyEditors, kind: "property-editor", title: "Activity property editors", owner: d() },
  expressionEditors: { id: u.expressionEditors, kind: "expression-editor", title: "Expression editors", owner: d() },
  settingEditors: { id: u.settingEditors, kind: "setting-editor", title: "Setting editors", owner: d() },
  agentContextProviders: { id: u.agentContextProviders, kind: "weaver-context-provider", title: "Weaver context providers", owner: d() },
  agentPromptStarters: { id: u.agentPromptStarters, kind: "weaver-prompt-starter", title: "Weaver prompt starters", owner: d() },
  agentCapabilities: { id: u.agentCapabilities, kind: "weaver-capability", title: "Weaver capabilities", owner: d() },
  agentActions: { id: u.agentActions, kind: "weaver-action", title: "Weaver actions", owner: d() },
  workflowDesignerNodeRenderers: { id: u.workflowDesignerNodeRenderers, kind: "workflow-designer-node-renderer", title: "Workflow designer node renderers", owner: d() },
  workflowDesignerToolboxItems: { id: u.workflowDesignerToolboxItems, kind: "workflow-designer-toolbox-item", title: "Workflow designer toolbox items", owner: d() },
  workflowDesignerPanels: { id: u.workflowDesignerPanels, kind: "workflow-designer-panel", title: "Workflow designer panels", owner: d() },
  aiContextProviders: { id: u.aiContextProviders, kind: "ai-context-provider", title: "AI context providers", owner: d() },
  aiPromptActions: { id: u.aiPromptActions, kind: "ai-prompt-action", title: "AI prompt actions", owner: d() },
  aiTools: { id: u.aiTools, kind: "ai-tool", title: "AI tools", owner: d() },
  aiProposalRenderers: { id: u.aiProposalRenderers, kind: "ai-proposal-renderer", title: "AI proposal renderers", owner: d() },
  aiSurfaces: { id: u.aiSurfaces, kind: "ai-surface", title: "AI surfaces", owner: d() },
  diagnostics: { id: u.diagnostics, kind: "diagnostic", title: "Diagnostics", owner: d() },
  diagnosticsWidgets: { id: u.diagnosticsWidgets, kind: "diagnostics-widget", title: "Diagnostics widgets", owner: d() }
};
function ze(e) {
  return e;
}
function d(e = "studio-host") {
  return { kind: "host", id: e };
}
function He(e) {
  return { kind: "module", id: e, moduleId: e };
}
function S(e = {}) {
  const t = [], r = e.slot ?? {
    id: "studio.unknown",
    kind: "unknown",
    owner: d(),
    title: "Unknown contributions"
  };
  return {
    slot: r,
    add(n) {
      t.push(n);
    },
    list(n) {
      return H(t, r, e, n).map((i) => i.contribution);
    },
    compose(n) {
      return H(t, r, e, n);
    }
  };
}
function We() {
  const e = /* @__PURE__ */ new Set();
  return {
    contextProviders: S({ slot: m.aiContextProviders }),
    promptActions: S({ slot: m.aiPromptActions }),
    tools: S({ slot: m.aiTools }),
    proposalRenderers: S({ slot: m.aiProposalRenderers }),
    surfaces: S({ slot: m.aiSurfaces }),
    dispatchPrompt(t) {
      for (const r of e)
        r(t);
    },
    onPrompt(t) {
      return e.add(t), () => e.delete(t);
    }
  };
}
function H(e, t, r, n = {}) {
  return e.map((i, s) => ({
    contribution: i,
    slot: t,
    availability: ye(i, t, r, n),
    order: r.getOrder?.(i) ?? be(i),
    stableKey: r.getStableKey?.(i) ?? ke(i, s),
    index: s
  })).filter((i) => Ae(i.availability, n)).sort((i, s) => i.order - s.order || i.stableKey.localeCompare(s.stableKey) || i.index - s.index).map(({ index: i, ...s }) => s);
}
function ye(e, t, r, n) {
  const i = { contribution: e, slot: t, context: n.context }, s = T(r.slotOwner?.(i), "slot-owner");
  if (s.state !== "available")
    return s;
  const o = W(e, "moduleId");
  if (o && n.disabledModuleIds?.includes(o))
    return { state: "hidden", reason: `Module ${o} is disabled.`, source: "module" };
  const c = W(e, "featureId");
  if (c && n.disabledFeatureIds?.includes(c))
    return { state: "hidden", reason: `Feature ${c} is disabled.`, source: "feature" };
  const a = T(ve(e, n.context), "runtime"), h = T(r.hostPolicy?.(i), "host-policy");
  if (h.state !== "available")
    return h;
  const k = T(n.hostPolicy?.(i), "host-policy");
  return k.state !== "available" ? k : a;
}
function T(e, t) {
  return e === !1 ? { state: "hidden", source: t } : e && typeof e == "object" ? { ...e, source: e.source ?? t } : { state: "available" };
}
function ve(e, t) {
  if (!j(e) || !("availability" in e))
    return !0;
  const r = e.availability;
  return typeof r == "function" ? r(t) : r;
}
function Ae(e, t) {
  return e.state === "available" ? !0 : e.state === "hidden" ? t.includeHidden === !0 : t.includeUnavailable === !0;
}
function be(e) {
  return me(e, "order") ?? 500;
}
function ke(e, t) {
  if (!j(e))
    return `_${t.toString().padStart(4, "0")}`;
  for (const r of ["id", "name", "label", "title", "path"]) {
    const n = e[r];
    if (typeof n == "string" && n.length > 0)
      return n;
  }
  return `_${t.toString().padStart(4, "0")}`;
}
function W(e, t) {
  if (!j(e))
    return;
  const r = e[t];
  return typeof r == "string" ? r : void 0;
}
function me(e, t) {
  if (!j(e))
    return;
  const r = e[t];
  return typeof r == "number" ? r : void 0;
}
function j(e) {
  return typeof e == "object" && e !== null;
}
function Be(e, t = {}) {
  return {
    baseUrl: e,
    headers: t.headers,
    http: Se(e, t.headers)
  };
}
function Se(e, t) {
  return {
    requestJson(r, n) {
      return A(e, r, y(t, R(n)));
    },
    async getJson(r, n) {
      return A(e, r, y(t, R(n)));
    },
    async postJson(r, n, i) {
      return A(e, r, y(t, {
        ...i,
        method: "POST",
        headers: B(i?.headers),
        body: JSON.stringify(n)
      }));
    },
    async putJson(r, n, i) {
      return A(e, r, y(t, {
        ...i,
        method: "PUT",
        headers: B(i?.headers),
        body: JSON.stringify(n)
      }));
    },
    async deleteJson(r, n) {
      return A(e, r, y(t, R({
        ...n,
        method: "DELETE"
      })));
    },
    async postForm(r, n, i) {
      return A(e, r, y(t, R({
        ...i,
        method: "POST",
        body: n
      })));
    }
  };
}
function y(e, t = {}) {
  return e ? {
    ...t,
    headers: Pe(e, t.headers)
  } : t;
}
function Pe(e, t) {
  const r = new Headers(e);
  return new Headers(t).forEach((n, i) => r.set(i, n)), r;
}
async function A(e, t, r) {
  const n = Ie(e, t), i = new AbortController(), s = globalThis.setTimeout(() => i.abort(), z);
  let o;
  try {
    o = await fetch(n, {
      ...r,
      signal: Ce(r?.signal, i.signal)
    });
  } catch (a) {
    throw i.signal.aborted && !r?.signal?.aborted ? new Error(`Request to ${n} timed out after ${z / 1e3} seconds. Check Studio:BackendBaseUrl and make sure the backend API is responding.`) : a;
  } finally {
    globalThis.clearTimeout(s);
  }
  if (!o.ok)
    throw await X(o);
  const c = await o.text();
  if (!c.trim())
    return {};
  try {
    return JSON.parse(c);
  } catch {
    throw new P(
      o.status,
      `Expected JSON from ${n}, but received ${$e(o, c)}. Check Studio:BackendBaseUrl and make sure the backend maps this API route.`
    );
  }
}
function Ce(e, t) {
  if (!e)
    return t;
  if (typeof AbortSignal.any == "function")
    return AbortSignal.any([e, t]);
  const r = new AbortController(), n = () => r.abort();
  return e.aborted || t.aborted ? r.abort() : (e.addEventListener("abort", n, { once: !0 }), t.addEventListener("abort", n, { once: !0 })), r.signal;
}
async function xe(e) {
  return (await Y(e)).message;
}
async function X(e) {
  const t = await Y(e);
  return new P(e.status, t.message, t.validationErrors, t.payload);
}
async function Y(e) {
  const t = e.headers.get("content-type") ?? "";
  if (Ee(t))
    try {
      const n = await e.json(), i = Z(n);
      return {
        message: Te(n) ?? Re(i) ?? `Request failed with ${e.status}.`,
        validationErrors: i,
        payload: n
      };
    } catch {
      return { message: `Request failed with ${e.status}.`, validationErrors: null, payload: null };
    }
  return { message: (await e.text()).trim() || `Request failed with ${e.status}.`, validationErrors: null, payload: null };
}
function Ee(e) {
  return e.toLowerCase().includes("json");
}
async function Ke(e) {
  if (e instanceof P)
    return e.message;
  if (ee(e))
    try {
      return await xe(e.response.clone());
    } catch {
      return e.response.statusText || "Request failed.";
    }
  return e instanceof Error ? e.message : "Unknown error.";
}
async function _e(e) {
  if (e instanceof P)
    return e.validationErrors;
  if (!ee(e))
    return null;
  try {
    const t = await e.response.clone().json();
    return Z(t);
  } catch {
    return null;
  }
}
function Te(e) {
  if (typeof e.detail == "string" && e.detail.length > 0) return e.detail;
  if (typeof e.title == "string" && e.title.length > 0) return e.title;
  if (typeof e.reason == "string" && e.reason.length > 0) return e.reason;
  if (Array.isArray(e.errors) && e.errors.length > 0) return e.errors.map(String).join(" ");
  if (e.errors && typeof e.errors == "object") {
    const t = Object.values(e.errors).flatMap((r) => Array.isArray(r) ? r : [r]).map(String);
    if (t.length > 0) return t.join(" ");
  }
  return null;
}
function Z(e) {
  const t = e.errors;
  if (!t || typeof t != "object" || Array.isArray(t))
    return null;
  const r = {};
  for (const [n, i] of Object.entries(t)) {
    const s = Array.isArray(i) ? i.map(String) : [String(i)];
    s.length > 0 && (r[n] = s);
  }
  return Object.keys(r).length > 0 ? r : null;
}
function Re(e) {
  return e ? Object.values(e).flat().join(" ") : null;
}
function ee(e) {
  return typeof e == "object" && e !== null && "response" in e && e.response instanceof Response;
}
function Ie(e, t) {
  return new URL(t, e).toString();
}
function R(e) {
  const t = new Headers(e?.headers);
  return t.has("Accept") || t.set("Accept", "application/json"), {
    ...e,
    cache: e?.cache ?? "no-store",
    headers: t
  };
}
function B(e) {
  const t = new Headers(e);
  return t.has("Content-Type") || t.set("Content-Type", "application/json"), t.has("Accept") || t.set("Accept", "application/json"), t;
}
function $e(e, t) {
  const r = e.headers.get("content-type") ?? "an unknown content type", n = t.trim(), i = n.length > 0 ? `: ${n.slice(0, 80)}` : "";
  return `${r}${i}`;
}
class P extends Error {
  constructor(t, r, n = null, i = null) {
    super(r), this.status = t, this.validationErrors = n, this.payload = i, this.name = "StudioHttpError";
  }
  status;
  validationErrors;
  payload;
}
export {
  b as AuthAdapterError,
  C as AuthConfigurationError,
  Fe as AuthGuard,
  De as AuthProvider,
  Oe as RequireAuth,
  P as StudioHttpError,
  We as createAiContributionApi,
  re as createAuthProviderManager,
  qe as createAuthenticatedHttpClient,
  Le as createBackendAuthProviderManager,
  S as createContributionRegistry,
  Be as createEndpointContext,
  Se as createHttpClient,
  Ne as createOidcAuthAdapter,
  _ as createRedirectAuthAdapter,
  we as createSignalRAccessTokenFactory,
  X as createStudioHttpError,
  ze as defineStudioSlot,
  Ke as describeApiError,
  d as hostSlotOwner,
  He as moduleSlotOwner,
  xe as readStudioHttpErrorMessage,
  u as studioSlotIds,
  m as studioSlots,
  _e as tryExtractValidationErrors,
  Ue as useAuthCapabilities,
  D as useAuthContext,
  ue as useAuthSession,
  le as usePermissions,
  Me as withAuthenticatedSignalROptions,
  y as withDefaultHeaders
};
