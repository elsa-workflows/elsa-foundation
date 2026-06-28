import { j as w } from "../vendor/chunks/jsx-runtime.js";
import { r as h } from "../vendor/chunks/index.js";
const j = {
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
        throw new x(`Duplicate auth provider adapter '${r.id}'.`);
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
    await (await this.resolveActiveAdapter()).logout(), this.session = j;
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
    const t = await this.options.bootstrap(), r = t.providers.find((o) => o.enabled && o.isDefault) ?? t.providers.find((o) => o.enabled);
    if (!r)
      throw new x("No enabled authentication provider was returned by /_elsa/identity/bootstrap.");
    const n = this.resolveProviderAdapter(r);
    return this.activeAdapter = n, n;
  }
  async getProviderAdapter(t) {
    const r = this.adapters.get(t);
    if (r)
      return r;
    const o = (await this.options.bootstrap()).providers.find((s) => s.enabled && s.id === t);
    if (!o)
      throw new x(`No auth provider adapter is registered for '${t}'.`);
    return this.resolveProviderAdapter(o);
  }
  resolveProviderAdapter(t) {
    const r = this.adapters.get(t.id);
    if (r)
      return r;
    if (!this.options.adapterFactory)
      throw new x(`No auth provider adapter is registered for '${t.id}'.`);
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
class x extends Error {
  constructor(t) {
    super(t), this.name = "AuthConfigurationError";
  }
}
function _(e) {
  const t = e.fetch ?? fetch, r = e.sessionEndpoint ?? "/_elsa/identity/session", n = e.logoutEndpoint ?? `/_elsa/identity/logout/${encodeURIComponent(e.id)}`;
  return {
    id: e.id,
    kind: e.kind,
    initialize: () => E(t, r, e),
    login: (o) => {
      const s = e.challenge;
      if (!s || s.type === "none")
        throw new b(`Provider '${e.id}' does not expose a redirect challenge.`);
      const c = "method" in s ? s.method.toUpperCase() : "GET";
      if (c !== "GET")
        throw new b(`Provider '${e.id}' exposes an unsupported ${c} challenge.`);
      const i = new URL(ie(s), D(e)), a = o?.returnUrl ?? e.location?.href ?? window.location.href;
      return i.searchParams.set("returnUrl", ae(a, o?.providerId ?? e.id, e)), (e.location ?? window.location).assign(i.toString()), Promise.resolve();
    },
    handleCallback: () => E(t, r, e),
    logout: async () => {
      const o = await t($(n, e), { method: "POST", credentials: "include" });
      if (!o.ok)
        throw new b(`Sign-out failed with ${o.status}.`);
    },
    getAccessToken: async () => {
      if (!e.tokenEndpoint)
        return null;
      const o = await t($(e.tokenEndpoint, e), { credentials: "include", cache: "no-store" });
      if (o.status === 401)
        return null;
      if (!o.ok)
        throw new b(`Access-token request failed with ${o.status}.`);
      const s = await o.json();
      return typeof s.accessToken == "string" ? s.accessToken : null;
    },
    refresh: async () => {
      const o = await e.getRefreshToken?.(), s = e.refreshEndpoint;
      if (!s || !o)
        return E(t, r, e);
      const c = await t($(s, e), {
        method: "POST",
        credentials: "include",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json"
        },
        body: JSON.stringify({ refreshToken: o })
      });
      if (c.status === 401)
        return j;
      if (!c.ok)
        throw new b(`Session refresh failed with ${c.status}.`);
      const i = await c.json();
      return i.status ? G(i) : E(t, r, e);
    }
  };
}
async function E(e, t, r) {
  const n = await e($(t, r), { credentials: "include", cache: "no-store" });
  if (n.status === 401)
    return j;
  if (!n.ok)
    throw new b(`Session request failed with ${n.status}.`);
  return oe(n);
}
async function oe(e) {
  const t = await e.json();
  return G(t);
}
function G(e) {
  const t = se(e.status) ? e.status : "anonymous";
  return {
    ...e,
    status: t,
    roles: F(e.roles),
    permissions: F(e.permissions)
  };
}
function F(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string") : [];
}
function se(e) {
  return e === "unknown" || e === "anonymous" || e === "authenticated";
}
function ie(e) {
  return "loginPath" in e ? e.loginPath : e.url;
}
function $(e, t) {
  return new URL(e, D(t)).toString();
}
function D(e) {
  return e?.baseUrl ?? e?.location?.origin ?? window.location.origin;
}
function ae(e, t, r) {
  const n = new URL(e, ce(r));
  return n.searchParams.set("authProviderId", t), de(e) ? `${n.pathname}${n.search}${n.hash}` : n.toString();
}
function ce(e) {
  return e?.location?.href ?? (typeof window < "u" ? window.location.href : void 0) ?? e?.location?.origin ?? D(e);
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
    bootstrap: () => O(r, t, "/_elsa/identity/bootstrap"),
    capabilities: () => O(r, t, "/_elsa/identity/capabilities"),
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
async function O(e, t, r) {
  const n = await e(new URL(r, t).toString(), {
    credentials: "include",
    cache: "no-store",
    headers: { Accept: "application/json" }
  });
  if (!n.ok)
    throw new Error(`Auth discovery request failed with ${n.status}.`);
  return await n.json();
}
const V = h.createContext(null);
function U() {
  const e = h.useContext(V);
  if (!e)
    throw new Error("Auth SDK hooks must be used within <AuthProvider>.");
  return e;
}
function De({ manager: e, children: t }) {
  const [r, n] = h.useState(() => e.getSession() ?? K), [o, s] = h.useState(null), c = h.useRef(!1), i = h.useRef(0), a = h.useCallback((f) => c.current && i.current === f, []), l = h.useCallback(async (f) => {
    if (a(f)) {
      s(null);
      try {
        const p = await e.getCapabilities();
        a(f) && s(p);
      } catch (p) {
        a(f) && (console.error("Auth capabilities request failed.", p), s(null));
      }
    }
  }, [e, a]);
  h.useLayoutEffect(() => {
    c.current = !0;
    const f = ++i.current;
    async function p() {
      try {
        const y = await e.initialize();
        if (!a(f))
          return;
        if (n(y), y.status !== "authenticated") {
          s(null);
          return;
        }
        await l(f);
      } catch (y) {
        a(f) && (console.error("Auth initialization failed.", y), n(j), s(null));
      }
    }
    return p(), () => {
      c.current = !1, i.current += 1;
    };
  }, [l, e, a]);
  const g = h.useCallback(async (f) => {
    const p = ++i.current;
    if (await e.login(f), !a(p))
      return;
    const y = e.getSession();
    n(y), y.status === "authenticated" ? await l(p) : s(null);
  }, [l, e, a]), C = h.useCallback(async () => {
    const f = ++i.current;
    await e.logout(), a(f) && (n(e.getSession()), s(null));
  }, [e, a]), q = h.useCallback(async () => {
    const f = ++i.current, p = await e.refresh();
    return a(f) && (n(p), p.status === "authenticated" ? await l(f) : s(null)), p;
  }, [l, e, a]), te = h.useMemo(() => ({
    session: r,
    capabilities: o,
    login: g,
    logout: C,
    refresh: q
  }), [o, g, C, q, r]);
  return /* @__PURE__ */ w.jsx(V.Provider, { value: te, children: t });
}
function ue() {
  return U().session;
}
function le() {
  const { permissions: e } = ue();
  return h.useMemo(() => {
    const t = new Set(e);
    return {
      has: (r) => t.has(r),
      hasAny: (r) => r.some((n) => t.has(n)),
      hasAll: (r) => r.every((n) => t.has(n))
    };
  }, [e]);
}
function Ue() {
  return U().capabilities;
}
function qe({ requires: e, requireAll: t = !0, fallback: r = null, children: n }) {
  const o = le(), s = typeof e == "string" ? [e] : e ?? [];
  return s.length === 0 || (t ? o.hasAll(s) : o.hasAny(s)) ? /* @__PURE__ */ w.jsx(w.Fragment, { children: n }) : /* @__PURE__ */ w.jsx(w.Fragment, { children: r });
}
function Fe({ children: e, fallback: t = null, loginOptions: r }) {
  const { session: n, login: o } = U(), s = h.useRef(null);
  return h.useEffect(() => {
    if (n.status === "anonymous") {
      const c = fe(r), i = s.current;
      if (i?.key === c && i.login === o)
        return;
      const a = { key: c, login: o };
      s.current = a, o(r).catch((l) => {
        s.current === a && (s.current = null), console.error("Auth login failed.", l);
      });
    } else
      s.current = null;
  }, [o, r, n.status]), n.status !== "authenticated" ? /* @__PURE__ */ w.jsx(w.Fragment, { children: t }) : /* @__PURE__ */ w.jsx(w.Fragment, { children: e });
}
function fe(e) {
  return `${e?.providerId ?? ""}
${e?.returnUrl ?? ""}`;
}
function Oe(e) {
  return _({
    ...e,
    kind: "external-oidc"
  });
}
function Ne(e, t, r = {}) {
  return {
    requestJson(n, o) {
      return A(e, n, t, r, T(o));
    },
    getJson(n, o) {
      return A(e, n, t, r, T(o));
    },
    postJson(n, o, s) {
      return A(e, n, t, r, {
        ...s,
        method: "POST",
        headers: N(s?.headers),
        body: JSON.stringify(o)
      });
    },
    putJson(n, o, s) {
      return A(e, n, t, r, {
        ...s,
        method: "PUT",
        headers: N(s?.headers),
        body: JSON.stringify(o)
      });
    },
    deleteJson(n, o) {
      return A(e, n, t, r, T({
        ...o,
        method: "DELETE"
      }));
    },
    postForm(n, o, s) {
      return A(e, n, t, r, T({
        ...s,
        method: "POST",
        body: o
      }));
    }
  };
}
const L = /* @__PURE__ */ new Map();
async function A(e, t, r, n, o) {
  const s = n.fetch ?? fetch, c = new URL(t, e).toString(), i = await s(c, await Q(r, W(n, o))), a = i.status === 401 && n.refreshOnUnauthorized !== !1 ? await he(s, c, r, W(n, o)) : i;
  if (!a.ok)
    throw await X(a);
  const l = await a.text();
  if (!l.trim())
    return {};
  try {
    return JSON.parse(l);
  } catch {
    throw new P(a.status, `Expected JSON from ${c}.`);
  }
}
async function he(e, t, r, n) {
  return await pe(t, r) ? e(t, await Q(r, n)) : new Response("Authentication required.", { status: 401 });
}
async function pe(e, t) {
  const r = new URL(e).origin, n = L.get(r);
  if (n)
    return n;
  const o = t.refresh().then((s) => s.status === "authenticated").finally(() => L.delete(r));
  return L.set(r, o), o;
}
async function Q(e, t) {
  const r = new Headers(t?.headers), n = await e.getAccessToken();
  return n && r.set("Authorization", `Bearer ${n}`), {
    ...t,
    credentials: t?.credentials ?? "include",
    headers: r
  };
}
function T(e) {
  const t = new Headers(e?.headers);
  return t.has("Accept") || t.set("Accept", "application/json"), {
    ...e,
    cache: e?.cache ?? "no-store",
    headers: t
  };
}
function N(e) {
  const t = new Headers(e);
  return t.has("Content-Type") || t.set("Content-Type", "application/json"), t.has("Accept") || t.set("Accept", "application/json"), t;
}
function W(e, t) {
  return v(e.defaultHeaders ?? e.headers, t);
}
function we(e, t = {}) {
  return async () => await e.getAccessToken() ?? await t.fallbackAccessTokenFactory?.() ?? t.anonymousToken ?? "";
}
function We(e, t) {
  const r = ge(e.accessTokenFactory) ? e.accessTokenFactory.bind(e) : void 0;
  return {
    ...e,
    accessTokenFactory: we(t, { fallbackAccessTokenFactory: r })
  };
}
function ge(e) {
  return typeof e == "function";
}
const M = 1e4, u = {
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
  agentToolSlots: "studio.weaver.tool-slots",
  agentToolContracts: "studio.weaver.tool-contracts",
  agentResultRenderers: "studio.weaver.result-renderers",
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
  agentToolSlots: { id: u.agentToolSlots, kind: "weaver-tool-slot", title: "Weaver tool slots", owner: d() },
  agentToolContracts: { id: u.agentToolContracts, kind: "weaver-tool-contract", title: "Weaver tool contracts", owner: d() },
  agentResultRenderers: { id: u.agentResultRenderers, kind: "weaver-result-renderer", title: "Weaver result renderers", owner: d() },
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
function Me(e) {
  return e;
}
function d(e = "studio-host") {
  return { kind: "host", id: e };
}
function ze(e) {
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
      return z(t, r, e, n).map((o) => o.contribution);
    },
    compose(n) {
      return z(t, r, e, n);
    }
  };
}
function He() {
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
function Be() {
  const e = [], t = /* @__PURE__ */ new Set();
  let r = 1;
  const n = () => e[0] ?? null, o = () => {
    const i = n();
    for (const a of t)
      a(i);
  };
  function s(i) {
    e.push({ ...i, id: r++ }), e.length === 1 && o();
  }
  return {
    api: {
      confirm(i) {
        return new Promise((a) => {
          s({ ...i, kind: "confirm", settle: (l) => a(l === !0) });
        });
      },
      prompt(i) {
        return new Promise((a) => {
          s({ ...i, kind: "prompt", settle: (l) => a(typeof l == "string" ? l : null) });
        });
      },
      alert(i) {
        return new Promise((a) => {
          s({ ...i, kind: "alert", settle: () => a() });
        });
      }
    },
    subscribe(i) {
      return t.add(i), i(n()), () => {
        t.delete(i);
      };
    },
    getCurrent: n,
    respond(i, a) {
      const l = e.findIndex((C) => C.id === i);
      if (l === -1)
        return;
      const [g] = e.splice(l, 1);
      g.settle(a), l === 0 && o();
    },
    cancelAll() {
      if (e.length === 0)
        return;
      const i = e.splice(0);
      for (const a of i)
        a.settle(a.kind === "prompt" ? null : !1);
      o();
    }
  };
}
function z(e, t, r, n = {}) {
  return e.map((o, s) => ({
    contribution: o,
    slot: t,
    availability: ye(o, t, r, n),
    order: r.getOrder?.(o) ?? ke(o),
    stableKey: r.getStableKey?.(o) ?? be(o, s),
    index: s
  })).filter((o) => Ae(o.availability, n)).sort((o, s) => o.order - s.order || o.stableKey.localeCompare(s.stableKey) || o.index - s.index).map(({ index: o, ...s }) => s);
}
function ye(e, t, r, n) {
  const o = { contribution: e, slot: t, context: n.context }, s = R(r.slotOwner?.(o), "slot-owner");
  if (s.state !== "available")
    return s;
  const c = H(e, "moduleId");
  if (c && n.disabledModuleIds?.includes(c))
    return { state: "hidden", reason: `Module ${c} is disabled.`, source: "module" };
  const i = H(e, "featureId");
  if (i && n.disabledFeatureIds?.includes(i))
    return { state: "hidden", reason: `Feature ${i} is disabled.`, source: "feature" };
  const a = R(ve(e, n.context), "runtime"), l = R(r.hostPolicy?.(o), "host-policy");
  if (l.state !== "available")
    return l;
  const g = R(n.hostPolicy?.(o), "host-policy");
  return g.state !== "available" ? g : a;
}
function R(e, t) {
  if (e === !1)
    return { state: "hidden", source: t };
  if (e && typeof e == "object") {
    const r = e;
    return !("state" in r) && typeof r.status == "string" ? {
      state: r.status === "available" ? "available" : "unavailable",
      reason: typeof r.reason == "string" ? r.reason : void 0,
      source: t
    } : { ...e, source: e.source ?? t };
  }
  return { state: "available" };
}
function ve(e, t) {
  if (!J(e) || !("availability" in e))
    return !0;
  const r = e.availability;
  return typeof r == "function" ? r(t) : r;
}
function Ae(e, t) {
  return e.state === "available" ? !0 : e.state === "hidden" ? t.includeHidden === !0 : t.includeUnavailable === !0;
}
function ke(e) {
  return me(e, "order") ?? 500;
}
function be(e, t) {
  if (!J(e))
    return `_${t.toString().padStart(4, "0")}`;
  for (const r of ["id", "name", "label", "title", "path"]) {
    const n = e[r];
    if (typeof n == "string" && n.length > 0)
      return n;
  }
  return `_${t.toString().padStart(4, "0")}`;
}
function H(e, t) {
  if (!J(e))
    return;
  const r = e[t];
  return typeof r == "string" ? r : void 0;
}
function me(e, t) {
  if (!J(e))
    return;
  const r = e[t];
  return typeof r == "number" ? r : void 0;
}
function J(e) {
  return typeof e == "object" && e !== null;
}
function Ke(e, t = {}) {
  return {
    baseUrl: e,
    headers: t.headers,
    http: Se(e, t.headers)
  };
}
function Se(e, t) {
  return {
    requestJson(r, n) {
      return k(e, r, v(t, I(n)));
    },
    async getJson(r, n) {
      return k(e, r, v(t, I(n)));
    },
    async postJson(r, n, o) {
      return k(e, r, v(t, {
        ...o,
        method: "POST",
        headers: B(o?.headers),
        body: JSON.stringify(n)
      }));
    },
    async putJson(r, n, o) {
      return k(e, r, v(t, {
        ...o,
        method: "PUT",
        headers: B(o?.headers),
        body: JSON.stringify(n)
      }));
    },
    async deleteJson(r, n) {
      return k(e, r, v(t, I({
        ...n,
        method: "DELETE"
      })));
    },
    async postForm(r, n, o) {
      return k(e, r, v(t, I({
        ...o,
        method: "POST",
        body: n
      })));
    }
  };
}
function v(e, t = {}) {
  return e ? {
    ...t,
    headers: Pe(e, t.headers)
  } : t;
}
function Pe(e, t) {
  const r = new Headers(e);
  return new Headers(t).forEach((n, o) => r.set(o, n)), r;
}
async function k(e, t, r) {
  const n = Ie(e, t), o = new AbortController(), s = globalThis.setTimeout(() => o.abort(), M);
  let c;
  try {
    c = await fetch(n, {
      ...r,
      signal: Ce(r?.signal, o.signal)
    });
  } catch (a) {
    throw o.signal.aborted && !r?.signal?.aborted ? new Error(`Request to ${n} timed out after ${M / 1e3} seconds. Check Studio:BackendBaseUrl and make sure the backend API is responding.`) : a;
  } finally {
    globalThis.clearTimeout(s);
  }
  if (!c.ok)
    throw await X(c);
  const i = await c.text();
  if (!i.trim())
    return {};
  try {
    return JSON.parse(i);
  } catch {
    throw new P(
      c.status,
      `Expected JSON from ${n}, but received ${$e(c, i)}. Check Studio:BackendBaseUrl and make sure the backend maps this API route.`
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
      const n = await e.json(), o = Z(n);
      return {
        message: Te(n) ?? Re(o) ?? `Request failed with ${e.status}.`,
        validationErrors: o,
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
async function _e(e) {
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
async function Ge(e) {
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
  for (const [n, o] of Object.entries(t)) {
    const s = Array.isArray(o) ? o.map(String) : [String(o)];
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
function I(e) {
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
  const r = e.headers.get("content-type") ?? "an unknown content type", n = t.trim(), o = n.length > 0 ? `: ${n.slice(0, 80)}` : "";
  return `${r}${o}`;
}
class P extends Error {
  constructor(t, r, n = null, o = null) {
    super(r), this.status = t, this.validationErrors = n, this.payload = o, this.name = "StudioHttpError";
  }
  status;
  validationErrors;
  payload;
}
export {
  b as AuthAdapterError,
  x as AuthConfigurationError,
  qe as AuthGuard,
  De as AuthProvider,
  Fe as RequireAuth,
  P as StudioHttpError,
  He as createAiContributionApi,
  re as createAuthProviderManager,
  Ne as createAuthenticatedHttpClient,
  Le as createBackendAuthProviderManager,
  S as createContributionRegistry,
  Be as createDialogController,
  Ke as createEndpointContext,
  Se as createHttpClient,
  Oe as createOidcAuthAdapter,
  _ as createRedirectAuthAdapter,
  we as createSignalRAccessTokenFactory,
  X as createStudioHttpError,
  Me as defineStudioSlot,
  _e as describeApiError,
  d as hostSlotOwner,
  ze as moduleSlotOwner,
  xe as readStudioHttpErrorMessage,
  u as studioSlotIds,
  m as studioSlots,
  Ge as tryExtractValidationErrors,
  Ue as useAuthCapabilities,
  U as useAuthContext,
  ue as useAuthSession,
  le as usePermissions,
  We as withAuthenticatedSignalROptions,
  v as withDefaultHeaders
};
