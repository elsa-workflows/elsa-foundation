import { j as p } from "../vendor/chunks/jsx-runtime.js";
import { createContext as ee, useContext as te, useState as F, useRef as $, useCallback as b, useLayoutEffect as re, useMemo as B, useEffect as ne } from "react";
const T = {
  status: "anonymous",
  roles: [],
  permissions: []
}, O = {
  status: "unknown",
  roles: [],
  permissions: []
};
function ie(e) {
  return new oe(e);
}
class oe {
  constructor(t) {
    this.options = t;
    for (const r of t.adapters ?? []) {
      if (this.adapters.has(r.id))
        throw new P(`Duplicate auth provider adapter '${r.id}'.`);
      this.adapters.set(r.id, r);
    }
  }
  options;
  adapters = /* @__PURE__ */ new Map();
  activeAdapter = null;
  pendingLoginProviderId = null;
  session = O;
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
    await (await this.resolveActiveAdapter()).logout(), this.session = T;
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
      throw new P("No enabled authentication provider was returned by /_elsa/identity/bootstrap.");
    const n = this.resolveProviderAdapter(r);
    return this.activeAdapter = n, n;
  }
  async getProviderAdapter(t) {
    const r = this.adapters.get(t);
    if (r)
      return r;
    const i = (await this.options.bootstrap()).providers.find((o) => o.enabled && o.id === t);
    if (!i)
      throw new P(`No auth provider adapter is registered for '${t}'.`);
    return this.resolveProviderAdapter(i);
  }
  resolveProviderAdapter(t) {
    const r = this.adapters.get(t.id);
    if (r)
      return r;
    if (!this.options.adapterFactory)
      throw new P(`No auth provider adapter is registered for '${t.id}'.`);
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
class P extends Error {
  constructor(t) {
    super(t), this.name = "AuthConfigurationError";
  }
}
function _(e) {
  const t = e.fetch ?? fetch, r = e.sessionEndpoint ?? "/_elsa/identity/session", n = e.logoutEndpoint ?? `/_elsa/identity/logout/${encodeURIComponent(e.id)}`;
  return {
    id: e.id,
    kind: e.kind,
    initialize: () => S(t, r, e),
    login: (i) => {
      const o = e.challenge;
      if (!o || o.type === "none")
        throw new v(`Provider '${e.id}' does not expose a redirect challenge.`);
      const a = "method" in o ? o.method.toUpperCase() : "GET";
      if (a !== "GET")
        throw new v(`Provider '${e.id}' exposes an unsupported ${a} challenge.`);
      const s = new URL(ce(o), U(e)), c = i?.returnUrl ?? e.location?.href ?? window.location.href;
      return s.searchParams.set("returnUrl", de(c, i?.providerId ?? e.id, e)), (e.location ?? window.location).assign(s.toString()), Promise.resolve();
    },
    handleCallback: () => S(t, r, e),
    logout: async () => {
      const i = await t(E(n, e), { method: "POST", credentials: "include" });
      if (!i.ok)
        throw new v(`Sign-out failed with ${i.status}.`);
    },
    getAccessToken: async () => {
      if (!e.tokenEndpoint)
        return null;
      const i = await t(E(e.tokenEndpoint, e), { credentials: "include", cache: "no-store" });
      if (i.status === 401)
        return null;
      if (!i.ok)
        throw new v(`Access-token request failed with ${i.status}.`);
      const o = await i.json();
      return typeof o.accessToken == "string" ? o.accessToken : null;
    },
    refresh: async () => {
      const i = await e.getRefreshToken?.(), o = e.refreshEndpoint;
      if (!o || !i)
        return S(t, r, e);
      const a = await t(E(o, e), {
        method: "POST",
        credentials: "include",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json"
        },
        body: JSON.stringify({ refreshToken: i })
      });
      if (a.status === 401)
        return T;
      if (!a.ok)
        throw new v(`Session refresh failed with ${a.status}.`);
      const s = await a.json();
      return s.status ? K(s) : S(t, r, e);
    }
  };
}
async function S(e, t, r) {
  const n = await e(E(t, r), { credentials: "include", cache: "no-store" });
  if (n.status === 401)
    return T;
  if (!n.ok)
    throw new v(`Session request failed with ${n.status}.`);
  return se(n);
}
async function se(e) {
  const t = await e.json();
  return K(t);
}
function K(e) {
  const t = ae(e.status) ? e.status : "anonymous";
  return {
    ...e,
    status: t,
    roles: H(e.roles),
    permissions: H(e.permissions)
  };
}
function H(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string") : [];
}
function ae(e) {
  return e === "unknown" || e === "anonymous" || e === "authenticated";
}
function ce(e) {
  return "loginPath" in e ? e.loginPath : e.url;
}
function E(e, t) {
  return new URL(e, U(t)).toString();
}
function U(e) {
  return e?.baseUrl ?? e?.location?.origin ?? window.location.origin;
}
function de(e, t, r) {
  const n = new URL(e, ue(r));
  return n.searchParams.set("authProviderId", t), le(e) ? `${n.pathname}${n.search}${n.hash}` : n.toString();
}
function ue(e) {
  return e?.location?.href ?? (typeof window < "u" ? window.location.href : void 0) ?? e?.location?.origin ?? U(e);
}
function le(e) {
  try {
    return new URL(e), !1;
  } catch {
    return !0;
  }
}
class v extends Error {
  constructor(t) {
    super(t), this.name = "AuthAdapterError";
  }
}
const fe = "/_elsa/identity/token";
function Me(e = {}) {
  const t = e.baseUrl ?? window.location.origin, r = e.fetch ?? fetch, n = e.tokenEndpoint ?? fe;
  return ie({
    bootstrap: () => q(r, t, "/_elsa/identity/bootstrap"),
    capabilities: () => q(r, t, "/_elsa/identity/capabilities"),
    isCallback: e.isCallback,
    getCallbackProviderId: e.getCallbackProviderId,
    adapterFactory: (i) => _({
      id: i.id,
      kind: i.kind,
      baseUrl: t,
      challenge: i.challenge,
      tokenEndpoint: n,
      refreshEndpoint: e.refreshEndpoint,
      getRefreshToken: e.getRefreshToken,
      fetch: r
    })
  });
}
async function q(e, t, r) {
  const n = await e(new URL(r, t).toString(), {
    credentials: "include",
    cache: "no-store",
    headers: { Accept: "application/json" }
  });
  if (!n.ok)
    throw new Error(`Auth discovery request failed with ${n.status}.`);
  return await n.json();
}
const G = ee(null);
function L() {
  const e = te(G);
  if (!e)
    throw new Error("Auth SDK hooks must be used within <AuthProvider>.");
  return e;
}
function Ne({ manager: e, children: t }) {
  const [r, n] = F(() => e.getSession() ?? O), [i, o] = F(null), a = $(!1), s = $(0), c = b((f) => a.current && s.current === f, []), l = b(async (f) => {
    if (c(f)) {
      o(null);
      try {
        const h = await e.getCapabilities();
        c(f) && o(h);
      } catch (h) {
        c(f) && (console.error("Auth capabilities request failed.", h), o(null));
      }
    }
  }, [e, c]);
  re(() => {
    a.current = !0;
    const f = ++s.current;
    async function h() {
      try {
        const g = await e.initialize();
        if (!c(f))
          return;
        if (n(g), g.status !== "authenticated") {
          o(null);
          return;
        }
        await l(f);
      } catch (g) {
        c(f) && (console.error("Auth initialization failed.", g), n(T), o(null));
      }
    }
    return h(), () => {
      a.current = !1, s.current += 1;
    };
  }, [l, e, c]);
  const w = b(async (f) => {
    const h = ++s.current;
    if (await e.login(f), !c(h))
      return;
    const g = e.getSession();
    n(g), g.status === "authenticated" ? await l(h) : o(null);
  }, [l, e, c]), m = b(async () => {
    const f = ++s.current;
    await e.logout(), c(f) && (n(e.getSession()), o(null));
  }, [e, c]), D = b(async () => {
    const f = ++s.current, h = await e.refresh();
    return c(f) && (n(h), h.status === "authenticated" ? await l(f) : o(null)), h;
  }, [l, e, c]), Z = B(() => ({
    session: r,
    capabilities: i,
    login: w,
    logout: m,
    refresh: D
  }), [i, w, m, D, r]);
  return /* @__PURE__ */ p.jsx(G.Provider, { value: Z, children: t });
}
function he() {
  return L().session;
}
function pe() {
  const { permissions: e } = he();
  return B(() => {
    const t = new Set(e);
    return {
      has: (r) => t.has(r),
      hasAny: (r) => r.some((n) => t.has(n)),
      hasAll: (r) => r.every((n) => t.has(n))
    };
  }, [e]);
}
function Be() {
  return L().capabilities;
}
function Oe({ requires: e, requireAll: t = !0, fallback: r = null, children: n }) {
  const i = pe(), o = typeof e == "string" ? [e] : e ?? [];
  return o.length === 0 || (t ? i.hasAll(o) : i.hasAny(o)) ? /* @__PURE__ */ p.jsx(p.Fragment, { children: n }) : /* @__PURE__ */ p.jsx(p.Fragment, { children: r });
}
function _e({ children: e, fallback: t = null, loginOptions: r }) {
  const { session: n, login: i } = L(), o = $(null);
  return ne(() => {
    if (n.status === "anonymous") {
      const a = we(r), s = o.current;
      if (s?.key === a && s.login === i)
        return;
      const c = { key: a, login: i };
      o.current = c, i(r).catch((l) => {
        o.current === c && (o.current = null), console.error("Auth login failed.", l);
      });
    } else
      o.current = null;
  }, [i, r, n.status]), n.status !== "authenticated" ? /* @__PURE__ */ p.jsx(p.Fragment, { children: t }) : /* @__PURE__ */ p.jsx(p.Fragment, { children: e });
}
function we(e) {
  return `${e?.providerId ?? ""}
${e?.returnUrl ?? ""}`;
}
function Ke(e) {
  return _({
    ...e,
    kind: "external-oidc"
  });
}
function Ge(e, t, r = {}) {
  return V(e, {
    defaultHeaders: r.defaultHeaders ?? r.headers,
    applyTimeout: !1,
    fetch: ge(t, r)
  });
}
const j = /* @__PURE__ */ new Map();
function ge(e, t) {
  const r = t.fetch ?? fetch;
  return (async (n, i) => {
    const o = typeof n == "string" ? n : n.toString(), a = await r(o, await W(e, i));
    return a.status !== 401 || t.refreshOnUnauthorized === !1 ? a : await ye(o, e) ? r(o, await W(e, i)) : new Response("Authentication required.", { status: 401 });
  });
}
async function ye(e, t) {
  const r = new URL(e).origin, n = j.get(r);
  if (n)
    return n;
  const i = t.refresh().then((o) => o.status === "authenticated").finally(() => j.delete(r));
  return j.set(r, i), i;
}
async function W(e, t) {
  const r = new Headers(t?.headers), n = await e.getAccessToken();
  return n && r.set("Authorization", `Bearer ${n}`), {
    ...t,
    credentials: t?.credentials ?? "include",
    headers: r
  };
}
function ve(e, t = {}) {
  return async () => await e.getAccessToken() ?? await t.fallbackAccessTokenFactory?.() ?? t.anonymousToken ?? "";
}
function Ve(e, t) {
  const r = be(e.accessTokenFactory) ? e.accessTokenFactory.bind(e) : void 0;
  return {
    ...e,
    accessTokenFactory: ve(t, { fallbackAccessTokenFactory: r })
  };
}
function be(e) {
  return typeof e == "function";
}
const J = 1e4, u = {
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
}, A = {
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
function Qe(e) {
  return e;
}
function d(e = "studio-host") {
  return { kind: "host", id: e };
}
function Xe(e) {
  return { kind: "module", id: e, moduleId: e };
}
function k(e = {}) {
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
      return z(t, r, e, n).map((i) => i.contribution);
    },
    compose(n) {
      return z(t, r, e, n);
    }
  };
}
function Ye() {
  const e = /* @__PURE__ */ new Set(), t = /* @__PURE__ */ new Set();
  return {
    contextProviders: k({ slot: A.aiContextProviders }),
    promptActions: k({ slot: A.aiPromptActions }),
    tools: k({ slot: A.aiTools }),
    proposalRenderers: k({ slot: A.aiProposalRenderers }),
    surfaces: k({ slot: A.aiSurfaces }),
    dispatchPrompt(r) {
      for (const n of e)
        n(r);
    },
    onPrompt(r) {
      return e.add(r), () => e.delete(r);
    },
    publishPromptResult(r) {
      for (const n of t)
        n(r);
    },
    onPromptResult(r) {
      return t.add(r), () => t.delete(r);
    }
  };
}
function Ze() {
  const e = [], t = /* @__PURE__ */ new Set();
  let r = 1;
  const n = () => e[0] ?? null, i = () => {
    const s = n();
    for (const c of t)
      c(s);
  };
  function o(s) {
    e.push({ ...s, id: r++ }), e.length === 1 && i();
  }
  return {
    api: {
      confirm(s) {
        return new Promise((c) => {
          o({ ...s, kind: "confirm", settle: (l) => c(l === !0) });
        });
      },
      prompt(s) {
        return new Promise((c) => {
          o({ ...s, kind: "prompt", settle: (l) => c(typeof l == "string" ? l : null) });
        });
      },
      alert(s) {
        return new Promise((c) => {
          o({ ...s, kind: "alert", settle: () => c() });
        });
      }
    },
    subscribe(s) {
      return t.add(s), s(n()), () => {
        t.delete(s);
      };
    },
    getCurrent: n,
    respond(s, c) {
      const l = e.findIndex((m) => m.id === s);
      if (l === -1)
        return;
      const [w] = e.splice(l, 1);
      w.settle(c), l === 0 && i();
    },
    cancelAll() {
      if (e.length === 0)
        return;
      const s = e.splice(0);
      for (const c of s)
        c.settle(c.kind === "prompt" ? null : !1);
      i();
    }
  };
}
function z(e, t, r, n = {}) {
  return e.map((i, o) => ({
    contribution: i,
    slot: t,
    availability: Ae(i, t, r, n),
    order: r.getOrder?.(i) ?? Pe(i),
    stableKey: r.getStableKey?.(i) ?? Se(i, o),
    index: o
  })).filter((i) => me(i.availability, n)).sort((i, o) => i.order - o.order || i.stableKey.localeCompare(o.stableKey) || i.index - o.index).map(({ index: i, ...o }) => o);
}
function Ae(e, t, r, n) {
  const i = { contribution: e, slot: t, context: n.context }, o = C(r.slotOwner?.(i), "slot-owner");
  if (o.state !== "available")
    return o;
  const a = M(e, "moduleId");
  if (a && n.disabledModuleIds?.includes(a))
    return { state: "hidden", reason: `Module ${a} is disabled.`, source: "module" };
  const s = M(e, "featureId");
  if (s && n.disabledFeatureIds?.includes(s))
    return { state: "hidden", reason: `Feature ${s} is disabled.`, source: "feature" };
  const c = C(ke(e, n.context), "runtime"), l = C(r.hostPolicy?.(i), "host-policy");
  if (l.state !== "available")
    return l;
  const w = C(n.hostPolicy?.(i), "host-policy");
  return w.state !== "available" ? w : c;
}
function C(e, t) {
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
function ke(e, t) {
  if (!R(e) || !("availability" in e))
    return !0;
  const r = e.availability;
  return typeof r == "function" ? r(t) : r;
}
function me(e, t) {
  return e.state === "available" ? !0 : e.state === "hidden" ? t.includeHidden === !0 : t.includeUnavailable === !0;
}
function Pe(e) {
  return Ce(e, "order") ?? 500;
}
function Se(e, t) {
  if (!R(e))
    return `_${t.toString().padStart(4, "0")}`;
  for (const r of ["id", "name", "label", "title", "path"]) {
    const n = e[r];
    if (typeof n == "string" && n.length > 0)
      return n;
  }
  return `_${t.toString().padStart(4, "0")}`;
}
function M(e, t) {
  if (!R(e))
    return;
  const r = e[t];
  return typeof r == "string" ? r : void 0;
}
function Ce(e, t) {
  if (!R(e))
    return;
  const r = e[t];
  return typeof r == "number" ? r : void 0;
}
function R(e) {
  return typeof e == "object" && e !== null;
}
function et(e, t = {}) {
  return {
    baseUrl: e,
    headers: t.headers,
    http: V(e, t.headers)
  };
}
function V(e, t) {
  const r = xe(t), { defaultHeaders: n } = r, i = (o, a) => Re(e, o, a, r);
  return {
    requestJson(o, a) {
      return i(o, y(n, x(a)));
    },
    async getJson(o, a) {
      return i(o, y(n, x(a)));
    },
    async postJson(o, a, s) {
      return i(o, y(n, {
        ...s,
        method: "POST",
        headers: N(s?.headers),
        body: JSON.stringify(a)
      }));
    },
    async putJson(o, a, s) {
      return i(o, y(n, {
        ...s,
        method: "PUT",
        headers: N(s?.headers),
        body: JSON.stringify(a)
      }));
    },
    async deleteJson(o, a) {
      return i(o, y(n, x({
        ...a,
        method: "DELETE"
      })));
    },
    async postForm(o, a, s) {
      return i(o, y(n, x({
        ...s,
        method: "POST",
        body: a
      })));
    }
  };
}
function xe(e) {
  return Ee(e) ? e : { defaultHeaders: e };
}
function Ee(e) {
  return typeof e == "object" && e !== null && !(e instanceof Headers) && !Array.isArray(e) && ("fetch" in e || "applyTimeout" in e || "defaultHeaders" in e);
}
function y(e, t = {}) {
  return e ? {
    ...t,
    headers: Te(e, t.headers)
  } : t;
}
function Te(e, t) {
  const r = new Headers(e);
  return new Headers(t).forEach((n, i) => r.set(i, n)), r;
}
async function Re(e, t, r, n = {}) {
  const i = qe(e, t), o = n.fetch ?? fetch, a = n.applyTimeout === !1 ? await o(i, r) : await Ie(o, i, r);
  return je(i, a);
}
async function Ie(e, t, r) {
  const n = new AbortController(), i = globalThis.setTimeout(() => n.abort(), J);
  try {
    return await e(t, {
      ...r,
      signal: $e(r?.signal, n.signal)
    });
  } catch (o) {
    throw n.signal.aborted && !r?.signal?.aborted ? new Error(`Request to ${t} timed out after ${J / 1e3} seconds. Check Studio:BackendBaseUrl and make sure the backend API is responding.`) : o;
  } finally {
    globalThis.clearTimeout(i);
  }
}
async function je(e, t) {
  if (!t.ok)
    throw await Le(t);
  const r = await t.text();
  if (!r.trim())
    return {};
  try {
    return JSON.parse(r);
  } catch {
    throw new I(
      t.status,
      `Expected JSON from ${e}, but received ${We(t, r)}. Check Studio:BackendBaseUrl and make sure the backend maps this API route.`
    );
  }
}
function $e(e, t) {
  if (!e)
    return t;
  if (typeof AbortSignal.any == "function")
    return AbortSignal.any([e, t]);
  const r = new AbortController(), n = () => r.abort();
  return e.aborted || t.aborted ? r.abort() : (e.addEventListener("abort", n, { once: !0 }), t.addEventListener("abort", n, { once: !0 })), r.signal;
}
async function Ue(e) {
  return (await Q(e)).message;
}
async function Le(e) {
  const t = await Q(e);
  return new I(e.status, t.message, t.validationErrors, t.payload);
}
async function Q(e) {
  const t = e.headers.get("content-type") ?? "";
  if (De(t))
    try {
      const n = await e.json(), i = X(n);
      return {
        message: Fe(n) ?? He(i) ?? `Request failed with ${e.status}.`,
        validationErrors: i,
        payload: n
      };
    } catch {
      return { message: `Request failed with ${e.status}.`, validationErrors: null, payload: null };
    }
  return { message: (await e.text()).trim() || `Request failed with ${e.status}.`, validationErrors: null, payload: null };
}
function De(e) {
  return e.toLowerCase().includes("json");
}
async function tt(e) {
  if (e instanceof I)
    return e.message;
  if (Y(e))
    try {
      return await Ue(e.response.clone());
    } catch {
      return e.response.statusText || "Request failed.";
    }
  return e instanceof Error ? e.message : "Unknown error.";
}
async function rt(e) {
  if (e instanceof I)
    return e.validationErrors;
  if (!Y(e))
    return null;
  try {
    const t = await e.response.clone().json();
    return X(t);
  } catch {
    return null;
  }
}
function Fe(e) {
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
function X(e) {
  const t = e.errors;
  if (!t || typeof t != "object" || Array.isArray(t))
    return null;
  const r = {};
  for (const [n, i] of Object.entries(t)) {
    const o = Array.isArray(i) ? i.map(String) : [String(i)];
    o.length > 0 && (r[n] = o);
  }
  return Object.keys(r).length > 0 ? r : null;
}
function He(e) {
  return e ? Object.values(e).flat().join(" ") : null;
}
function Y(e) {
  return typeof e == "object" && e !== null && "response" in e && e.response instanceof Response;
}
function qe(e, t) {
  return new URL(t, e).toString();
}
function x(e) {
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
function We(e, t) {
  const r = e.headers.get("content-type") ?? "an unknown content type", n = t.trim(), i = n.length > 0 ? `: ${n.slice(0, 80)}` : "";
  return `${r}${i}`;
}
class I extends Error {
  constructor(t, r, n = null, i = null) {
    super(r), this.status = t, this.validationErrors = n, this.payload = i, this.name = "StudioHttpError";
  }
  status;
  validationErrors;
  payload;
}
export {
  v as AuthAdapterError,
  P as AuthConfigurationError,
  Oe as AuthGuard,
  Ne as AuthProvider,
  _e as RequireAuth,
  I as StudioHttpError,
  Ye as createAiContributionApi,
  ie as createAuthProviderManager,
  Ge as createAuthenticatedHttpClient,
  Me as createBackendAuthProviderManager,
  k as createContributionRegistry,
  Ze as createDialogController,
  et as createEndpointContext,
  V as createHttpClient,
  Ke as createOidcAuthAdapter,
  _ as createRedirectAuthAdapter,
  ve as createSignalRAccessTokenFactory,
  Le as createStudioHttpError,
  Qe as defineStudioSlot,
  tt as describeApiError,
  d as hostSlotOwner,
  Xe as moduleSlotOwner,
  Ue as readStudioHttpErrorMessage,
  u as studioSlotIds,
  A as studioSlots,
  rt as tryExtractValidationErrors,
  Be as useAuthCapabilities,
  L as useAuthContext,
  he as useAuthSession,
  pe as usePermissions,
  Ve as withAuthenticatedSignalROptions,
  y as withDefaultHeaders
};
