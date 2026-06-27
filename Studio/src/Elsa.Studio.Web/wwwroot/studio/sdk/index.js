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
    const t = await this.options.bootstrap(), r = t.providers.find((o) => o.enabled && o.isDefault) ?? t.providers.find((o) => o.enabled);
    if (!r)
      throw new C("No enabled authentication provider was returned by /_elsa/identity/bootstrap.");
    const n = this.resolveProviderAdapter(r);
    return this.activeAdapter = n, n;
  }
  async getProviderAdapter(t) {
    const r = this.adapters.get(t);
    if (r)
      return r;
    const o = (await this.options.bootstrap()).providers.find((s) => s.enabled && s.id === t);
    if (!o)
      throw new C(`No auth provider adapter is registered for '${t}'.`);
    return this.resolveProviderAdapter(o);
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
    login: (o) => {
      const s = e.challenge;
      if (!s || s.type === "none")
        throw new b(`Provider '${e.id}' does not expose a redirect challenge.`);
      const i = "method" in s ? s.method.toUpperCase() : "GET";
      if (i !== "GET")
        throw new b(`Provider '${e.id}' exposes an unsupported ${i} challenge.`);
      const u = new URL(ie(s), L(e)), c = o?.returnUrl ?? e.location?.href ?? window.location.href;
      return u.searchParams.set("returnUrl", ae(c, o?.providerId ?? e.id, e)), (e.location ?? window.location).assign(u.toString()), Promise.resolve();
    },
    handleCallback: () => x(t, r, e),
    logout: async () => {
      const o = await t(I(n, e), { method: "POST", credentials: "include" });
      if (!o.ok)
        throw new b(`Sign-out failed with ${o.status}.`);
    },
    getAccessToken: async () => {
      if (!e.tokenEndpoint)
        return null;
      const o = await t(I(e.tokenEndpoint, e), { credentials: "include", cache: "no-store" });
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
        return x(t, r, e);
      const i = await t(I(s, e), {
        method: "POST",
        credentials: "include",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json"
        },
        body: JSON.stringify({ refreshToken: o })
      });
      if (i.status === 401)
        return $;
      if (!i.ok)
        throw new b(`Session refresh failed with ${i.status}.`);
      const u = await i.json();
      return u.status ? G(u) : x(t, r, e);
    }
  };
}
async function x(e, t, r) {
  const n = await e(I(t, r), { credentials: "include", cache: "no-store" });
  if (n.status === 401)
    return $;
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
function ie(e) {
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
  const [r, n] = f.useState(() => e.getSession() ?? K), [o, s] = f.useState(null), i = f.useRef(!1), u = f.useRef(0), c = f.useCallback((l) => i.current && u.current === l, []), h = f.useCallback(async (l) => {
    if (c(l)) {
      s(null);
      try {
        const p = await e.getCapabilities();
        c(l) && s(p);
      } catch (p) {
        c(l) && (console.error("Auth capabilities request failed.", p), s(null));
      }
    }
  }, [e, c]);
  f.useLayoutEffect(() => {
    i.current = !0;
    const l = ++u.current;
    async function p() {
      try {
        const g = await e.initialize();
        if (!c(l))
          return;
        if (n(g), g.status !== "authenticated") {
          s(null);
          return;
        }
        await h(l);
      } catch (g) {
        c(l) && (console.error("Auth initialization failed.", g), n($), s(null));
      }
    }
    return p(), () => {
      i.current = !1, u.current += 1;
    };
  }, [h, e, c]);
  const k = f.useCallback(async (l) => {
    const p = ++u.current;
    if (await e.login(l), !c(p))
      return;
    const g = e.getSession();
    n(g), g.status === "authenticated" ? await h(p) : s(null);
  }, [h, e, c]), U = f.useCallback(async () => {
    const l = ++u.current;
    await e.logout(), c(l) && (n(e.getSession()), s(null));
  }, [e, c]), F = f.useCallback(async () => {
    const l = ++u.current, p = await e.refresh();
    return c(l) && (n(p), p.status === "authenticated" ? await h(l) : s(null)), p;
  }, [h, e, c]), te = f.useMemo(() => ({
    session: r,
    capabilities: o,
    login: k,
    logout: U,
    refresh: F
  }), [o, k, U, F, r]);
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
  const o = le(), s = typeof e == "string" ? [e] : e ?? [];
  return s.length === 0 || (t ? o.hasAll(s) : o.hasAny(s)) ? /* @__PURE__ */ w.jsx(w.Fragment, { children: n }) : /* @__PURE__ */ w.jsx(w.Fragment, { children: r });
}
function Oe({ children: e, fallback: t = null, loginOptions: r }) {
  const { session: n, login: o } = D(), s = f.useRef(null);
  return f.useEffect(() => {
    if (n.status === "anonymous") {
      const i = fe(r), u = s.current;
      if (u?.key === i && u.login === o)
        return;
      const c = { key: i, login: o };
      s.current = c, o(r).catch((h) => {
        s.current === c && (s.current = null), console.error("Auth login failed.", h);
      });
    } else
      s.current = null;
  }, [o, r, n.status]), n.status !== "authenticated" ? /* @__PURE__ */ w.jsx(w.Fragment, { children: t }) : /* @__PURE__ */ w.jsx(w.Fragment, { children: e });
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
    requestJson(n, o) {
      return v(e, n, t, r, E(o));
    },
    getJson(n, o) {
      return v(e, n, t, r, E(o));
    },
    postJson(n, o, s) {
      return v(e, n, t, r, {
        ...s,
        method: "POST",
        headers: q(s?.headers),
        body: JSON.stringify(o)
      });
    },
    putJson(n, o, s) {
      return v(e, n, t, r, {
        ...s,
        method: "PUT",
        headers: q(s?.headers),
        body: JSON.stringify(o)
      });
    },
    deleteJson(n, o) {
      return v(e, n, t, r, E({
        ...o,
        method: "DELETE"
      }));
    },
    postForm(n, o, s) {
      return v(e, n, t, r, E({
        ...s,
        method: "POST",
        body: o
      }));
    }
  };
}
const J = /* @__PURE__ */ new Map();
async function v(e, t, r, n, o) {
  const s = n.fetch ?? fetch, i = new URL(t, e).toString(), u = await s(i, await Q(r, W(n, o))), c = u.status === 401 && n.refreshOnUnauthorized !== !1 ? await he(s, i, r, W(n, o)) : u;
  if (!c.ok)
    throw await X(c);
  const h = await c.text();
  if (!h.trim())
    return {};
  try {
    return JSON.parse(h);
  } catch {
    throw new P(c.status, `Expected JSON from ${i}.`);
  }
}
async function he(e, t, r, n) {
  return await pe(t, r) ? e(t, await Q(r, n)) : new Response("Authentication required.", { status: 401 });
}
async function pe(e, t) {
  const r = new URL(e).origin, n = J.get(r);
  if (n)
    return n;
  const o = t.refresh().then((s) => s.status === "authenticated").finally(() => J.delete(r));
  return J.set(r, o), o;
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
function W(e, t) {
  return y(e.defaultHeaders ?? e.headers, t);
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
const M = 1e4, d = {
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
  featureAreas: { id: d.featureAreas, kind: "feature-area", title: "Feature areas", owner: a() },
  navigation: { id: d.navigation, kind: "navigation", title: "Navigation", owner: a() },
  routes: { id: d.routes, kind: "route", title: "Routes", owner: a() },
  dashboardWidgets: { id: d.dashboardWidgets, kind: "dashboard-widget", title: "Dashboard widgets", owner: a() },
  panels: { id: d.panels, kind: "panel", title: "Panels", owner: a() },
  toolbarActions: { id: d.toolbarActions, kind: "toolbar-action", title: "Toolbar actions", owner: a() },
  activityEditors: { id: d.activityEditors, kind: "activity-editor", title: "Activity editors", owner: a() },
  propertyEditors: { id: d.propertyEditors, kind: "property-editor", title: "Activity property editors", owner: a() },
  expressionEditors: { id: d.expressionEditors, kind: "expression-editor", title: "Expression editors", owner: a() },
  settingEditors: { id: d.settingEditors, kind: "setting-editor", title: "Setting editors", owner: a() },
  agentContextProviders: { id: d.agentContextProviders, kind: "weaver-context-provider", title: "Weaver context providers", owner: a() },
  agentPromptStarters: { id: d.agentPromptStarters, kind: "weaver-prompt-starter", title: "Weaver prompt starters", owner: a() },
  agentCapabilities: { id: d.agentCapabilities, kind: "weaver-capability", title: "Weaver capabilities", owner: a() },
  agentActions: { id: d.agentActions, kind: "weaver-action", title: "Weaver actions", owner: a() },
  agentToolSlots: { id: d.agentToolSlots, kind: "weaver-tool-slot", title: "Weaver tool slots", owner: a() },
  agentToolContracts: { id: d.agentToolContracts, kind: "weaver-tool-contract", title: "Weaver tool contracts", owner: a() },
  agentResultRenderers: { id: d.agentResultRenderers, kind: "weaver-result-renderer", title: "Weaver result renderers", owner: a() },
  workflowDesignerNodeRenderers: { id: d.workflowDesignerNodeRenderers, kind: "workflow-designer-node-renderer", title: "Workflow designer node renderers", owner: a() },
  workflowDesignerToolboxItems: { id: d.workflowDesignerToolboxItems, kind: "workflow-designer-toolbox-item", title: "Workflow designer toolbox items", owner: a() },
  workflowDesignerPanels: { id: d.workflowDesignerPanels, kind: "workflow-designer-panel", title: "Workflow designer panels", owner: a() },
  aiContextProviders: { id: d.aiContextProviders, kind: "ai-context-provider", title: "AI context providers", owner: a() },
  aiPromptActions: { id: d.aiPromptActions, kind: "ai-prompt-action", title: "AI prompt actions", owner: a() },
  aiTools: { id: d.aiTools, kind: "ai-tool", title: "AI tools", owner: a() },
  aiProposalRenderers: { id: d.aiProposalRenderers, kind: "ai-proposal-renderer", title: "AI proposal renderers", owner: a() },
  aiSurfaces: { id: d.aiSurfaces, kind: "ai-surface", title: "AI surfaces", owner: a() },
  diagnostics: { id: d.diagnostics, kind: "diagnostic", title: "Diagnostics", owner: a() },
  diagnosticsWidgets: { id: d.diagnosticsWidgets, kind: "diagnostics-widget", title: "Diagnostics widgets", owner: a() }
};
function Me(e) {
  return e;
}
function a(e = "studio-host") {
  return { kind: "host", id: e };
}
function ze(e) {
  return { kind: "module", id: e, moduleId: e };
}
function S(e = {}) {
  const t = [], r = e.slot ?? {
    id: "studio.unknown",
    kind: "unknown",
    owner: a(),
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
function z(e, t, r, n = {}) {
  return e.map((o, s) => ({
    contribution: o,
    slot: t,
    availability: ye(o, t, r, n),
    order: r.getOrder?.(o) ?? be(o),
    stableKey: r.getStableKey?.(o) ?? ke(o, s),
    index: s
  })).filter((o) => Ae(o.availability, n)).sort((o, s) => o.order - s.order || o.stableKey.localeCompare(s.stableKey) || o.index - s.index).map(({ index: o, ...s }) => s);
}
function ye(e, t, r, n) {
  const o = { contribution: e, slot: t, context: n.context }, s = T(r.slotOwner?.(o), "slot-owner");
  if (s.state !== "available")
    return s;
  const i = H(e, "moduleId");
  if (i && n.disabledModuleIds?.includes(i))
    return { state: "hidden", reason: `Module ${i} is disabled.`, source: "module" };
  const u = H(e, "featureId");
  if (u && n.disabledFeatureIds?.includes(u))
    return { state: "hidden", reason: `Feature ${u} is disabled.`, source: "feature" };
  const c = T(ve(e, n.context), "runtime"), h = T(r.hostPolicy?.(o), "host-policy");
  if (h.state !== "available")
    return h;
  const k = T(n.hostPolicy?.(o), "host-policy");
  return k.state !== "available" ? k : c;
}
function T(e, t) {
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
function H(e, t) {
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
    async postJson(r, n, o) {
      return A(e, r, y(t, {
        ...o,
        method: "POST",
        headers: B(o?.headers),
        body: JSON.stringify(n)
      }));
    },
    async putJson(r, n, o) {
      return A(e, r, y(t, {
        ...o,
        method: "PUT",
        headers: B(o?.headers),
        body: JSON.stringify(n)
      }));
    },
    async deleteJson(r, n) {
      return A(e, r, y(t, R({
        ...n,
        method: "DELETE"
      })));
    },
    async postForm(r, n, o) {
      return A(e, r, y(t, R({
        ...o,
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
  return new Headers(t).forEach((n, o) => r.set(o, n)), r;
}
async function A(e, t, r) {
  const n = Ie(e, t), o = new AbortController(), s = globalThis.setTimeout(() => o.abort(), M);
  let i;
  try {
    i = await fetch(n, {
      ...r,
      signal: Ce(r?.signal, o.signal)
    });
  } catch (c) {
    throw o.signal.aborted && !r?.signal?.aborted ? new Error(`Request to ${n} timed out after ${M / 1e3} seconds. Check Studio:BackendBaseUrl and make sure the backend API is responding.`) : c;
  } finally {
    globalThis.clearTimeout(s);
  }
  if (!i.ok)
    throw await X(i);
  const u = await i.text();
  if (!u.trim())
    return {};
  try {
    return JSON.parse(u);
  } catch {
    throw new P(
      i.status,
      `Expected JSON from ${n}, but received ${$e(i, u)}. Check Studio:BackendBaseUrl and make sure the backend maps this API route.`
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
  C as AuthConfigurationError,
  Fe as AuthGuard,
  De as AuthProvider,
  Oe as RequireAuth,
  P as StudioHttpError,
  He as createAiContributionApi,
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
  Me as defineStudioSlot,
  Ke as describeApiError,
  a as hostSlotOwner,
  ze as moduleSlotOwner,
  xe as readStudioHttpErrorMessage,
  d as studioSlotIds,
  m as studioSlots,
  _e as tryExtractValidationErrors,
  Ue as useAuthCapabilities,
  D as useAuthContext,
  ue as useAuthSession,
  le as usePermissions,
  We as withAuthenticatedSignalROptions,
  y as withDefaultHeaders
};
