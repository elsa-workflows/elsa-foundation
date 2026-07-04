import { j as p } from "../vendor/chunks/jsx-runtime.js";
import { createContext as ie, useContext as se, useState as O, useRef as D, useCallback as b, useLayoutEffect as ae, useMemo as G, useEffect as ce } from "react";
const j = {
  status: "anonymous",
  roles: [],
  permissions: []
}, V = {
  status: "unknown",
  roles: [],
  permissions: []
};
function de(e) {
  return new ue(e);
}
class ue {
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
  session = V;
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
    const o = (await this.options.bootstrap()).providers.find((i) => i.enabled && i.id === t);
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
function Q(e) {
  const t = e.fetch ?? fetch, r = e.sessionEndpoint ?? "/_elsa/identity/session", n = e.logoutEndpoint ?? `/_elsa/identity/logout/${encodeURIComponent(e.id)}`;
  return {
    id: e.id,
    kind: e.kind,
    initialize: () => E(t, r, e),
    login: (o) => {
      const i = e.challenge;
      if (!i || i.type === "none")
        throw new k(`Provider '${e.id}' does not expose a redirect challenge.`);
      const c = "method" in i ? i.method.toUpperCase() : "GET";
      if (c !== "GET")
        throw new k(`Provider '${e.id}' exposes an unsupported ${c} challenge.`);
      const s = new URL(he(i), U(e)), a = o?.returnUrl ?? e.location?.href ?? window.location.href;
      return s.searchParams.set("returnUrl", pe(a, o?.providerId ?? e.id, e)), (e.location ?? window.location).assign(s.toString()), Promise.resolve();
    },
    handleCallback: () => E(t, r, e),
    logout: async () => {
      const o = await t($(n, e), { method: "POST", credentials: "include" });
      if (!o.ok)
        throw new k(`Sign-out failed with ${o.status}.`);
    },
    getAccessToken: async () => {
      if (!e.tokenEndpoint)
        return null;
      const o = await t($(e.tokenEndpoint, e), { credentials: "include", cache: "no-store" });
      if (o.status === 401)
        return null;
      if (!o.ok)
        throw new k(`Access-token request failed with ${o.status}.`);
      const i = await o.json();
      return typeof i.accessToken == "string" ? i.accessToken : null;
    },
    refresh: async () => {
      const o = await e.getRefreshToken?.(), i = e.refreshEndpoint;
      if (!i || !o)
        return E(t, r, e);
      const c = await t($(i, e), {
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
        throw new k(`Session refresh failed with ${c.status}.`);
      const s = await c.json();
      return s.status ? X(s) : E(t, r, e);
    }
  };
}
async function E(e, t, r) {
  const n = await e($(t, r), { credentials: "include", cache: "no-store" });
  if (n.status === 401)
    return j;
  if (!n.ok)
    throw new k(`Session request failed with ${n.status}.`);
  return le(n);
}
async function le(e) {
  const t = await e.json();
  return X(t);
}
function X(e) {
  const t = fe(e.status) ? e.status : "anonymous";
  return {
    ...e,
    status: t,
    roles: N(e.roles),
    permissions: N(e.permissions)
  };
}
function N(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string") : [];
}
function fe(e) {
  return e === "unknown" || e === "anonymous" || e === "authenticated";
}
function he(e) {
  return "loginPath" in e ? e.loginPath : e.url;
}
function $(e, t) {
  return new URL(e, U(t)).toString();
}
function U(e) {
  return e?.baseUrl ?? e?.location?.origin ?? window.location.origin;
}
function pe(e, t, r) {
  const n = new URL(e, we(r));
  return n.searchParams.set("authProviderId", t), ge(e) ? `${n.pathname}${n.search}${n.hash}` : n.toString();
}
function we(e) {
  return e?.location?.href ?? (typeof window < "u" ? window.location.href : void 0) ?? e?.location?.origin ?? U(e);
}
function ge(e) {
  try {
    return new URL(e), !1;
  } catch {
    return !0;
  }
}
class k extends Error {
  constructor(t) {
    super(t), this.name = "AuthAdapterError";
  }
}
function We(e = {}) {
  const t = e.baseUrl ?? window.location.origin, r = e.fetch ?? fetch;
  return de({
    bootstrap: () => W(r, t, "/_elsa/identity/bootstrap"),
    capabilities: () => W(r, t, "/_elsa/identity/capabilities"),
    isCallback: e.isCallback,
    getCallbackProviderId: e.getCallbackProviderId,
    adapterFactory: (n) => Q({
      id: n.id,
      kind: n.kind,
      baseUrl: t,
      challenge: n.challenge,
      fetch: r
    })
  });
}
async function W(e, t, r) {
  const n = await e(new URL(r, t).toString(), {
    credentials: "include",
    cache: "no-store",
    headers: { Accept: "application/json" }
  });
  if (!n.ok)
    throw new Error(`Auth discovery request failed with ${n.status}.`);
  return await n.json();
}
const Y = ie(null);
function q() {
  const e = se(Y);
  if (!e)
    throw new Error("Auth SDK hooks must be used within <AuthProvider>.");
  return e;
}
function ze({ manager: e, children: t }) {
  const [r, n] = O(() => e.getSession() ?? V), [o, i] = O(null), c = D(!1), s = D(0), a = b((f) => c.current && s.current === f, []), l = b(async (f) => {
    if (a(f)) {
      i(null);
      try {
        const h = await e.getCapabilities();
        a(f) && i(h);
      } catch (h) {
        a(f) && (console.error("Auth capabilities request failed.", h), i(null));
      }
    }
  }, [e, a]);
  ae(() => {
    c.current = !0;
    const f = ++s.current;
    async function h() {
      try {
        const g = await e.initialize();
        if (!a(f))
          return;
        if (n(g), g.status !== "authenticated") {
          i(null);
          return;
        }
        await l(f);
      } catch (g) {
        a(f) && (console.error("Auth initialization failed.", g), n(j), i(null));
      }
    }
    return h(), () => {
      c.current = !1, s.current += 1;
    };
  }, [l, e, a]);
  const w = b(async (f) => {
    const h = ++s.current;
    if (await e.login(f), !a(h))
      return;
    const g = e.getSession();
    n(g), g.status === "authenticated" ? await l(h) : i(null);
  }, [l, e, a]), C = b(async () => {
    const f = ++s.current;
    await e.logout(), a(f) && (n(e.getSession()), i(null));
  }, [e, a]), F = b(async () => {
    const f = ++s.current, h = await e.refresh();
    return a(f) && (n(h), h.status === "authenticated" ? await l(f) : i(null)), h;
  }, [l, e, a]), oe = G(() => ({
    session: r,
    capabilities: o,
    login: w,
    logout: C,
    refresh: F
  }), [o, w, C, F, r]);
  return /* @__PURE__ */ p.jsx(Y.Provider, { value: oe, children: t });
}
function ye() {
  return q().session;
}
function ve() {
  const { permissions: e } = ye();
  return G(() => {
    const t = new Set(e);
    return {
      has: (r) => t.has(r),
      hasAny: (r) => r.some((n) => t.has(n)),
      hasAll: (r) => r.every((n) => t.has(n))
    };
  }, [e]);
}
function He() {
  return q().capabilities;
}
function Me({ requires: e, requireAll: t = !0, fallback: r = null, children: n }) {
  const o = ve(), i = typeof e == "string" ? [e] : e ?? [];
  return i.length === 0 || (t ? o.hasAll(i) : o.hasAny(i)) ? /* @__PURE__ */ p.jsx(p.Fragment, { children: n }) : /* @__PURE__ */ p.jsx(p.Fragment, { children: r });
}
function Be({ children: e, fallback: t = null, loginOptions: r }) {
  const { session: n, login: o } = q(), i = D(null);
  return ce(() => {
    if (n.status === "anonymous") {
      const c = Ae(r), s = i.current;
      if (s?.key === c && s.login === o)
        return;
      const a = { key: c, login: o };
      i.current = a, o(r).catch((l) => {
        i.current === a && (i.current = null), console.error("Auth login failed.", l);
      });
    } else
      i.current = null;
  }, [o, r, n.status]), n.status !== "authenticated" ? /* @__PURE__ */ p.jsx(p.Fragment, { children: t }) : /* @__PURE__ */ p.jsx(p.Fragment, { children: e });
}
function Ae(e) {
  return `${e?.providerId ?? ""}
${e?.returnUrl ?? ""}`;
}
function Ke(e) {
  return Q({
    ...e,
    kind: "external-oidc"
  });
}
function _e(e, t, r = {}) {
  return {
    requestJson(n, o) {
      return v(e, n, t, r, T(o));
    },
    getJson(n, o) {
      return v(e, n, t, r, T(o));
    },
    postJson(n, o, i) {
      return v(e, n, t, r, {
        ...i,
        method: "POST",
        headers: z(i?.headers),
        body: JSON.stringify(o)
      });
    },
    putJson(n, o, i) {
      return v(e, n, t, r, {
        ...i,
        method: "PUT",
        headers: z(i?.headers),
        body: JSON.stringify(o)
      });
    },
    deleteJson(n, o) {
      return v(e, n, t, r, T({
        ...o,
        method: "DELETE"
      }));
    },
    postForm(n, o, i) {
      return v(e, n, t, r, T({
        ...i,
        method: "POST",
        body: o
      }));
    }
  };
}
const J = /* @__PURE__ */ new Map();
async function v(e, t, r, n, o) {
  const i = n.fetch ?? fetch, c = new URL(t, e).toString(), s = await i(c, await Z(r, H(n, o))), a = s.status === 401 && n.refreshOnUnauthorized !== !1 ? await ke(i, c, r, H(n, o)) : s;
  if (!a.ok)
    throw await ee(a);
  const l = await a.text();
  if (!l.trim())
    return {};
  try {
    return JSON.parse(l);
  } catch {
    throw new P(a.status, `Expected JSON from ${c}.`);
  }
}
async function ke(e, t, r, n) {
  return await be(t, r) ? e(t, await Z(r, n)) : new Response("Authentication required.", { status: 401 });
}
async function be(e, t) {
  const r = new URL(e).origin, n = J.get(r);
  if (n)
    return n;
  const o = t.refresh().then((i) => i.status === "authenticated").finally(() => J.delete(r));
  return J.set(r, o), o;
}
async function Z(e, t) {
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
function z(e) {
  const t = new Headers(e);
  return t.has("Content-Type") || t.set("Content-Type", "application/json"), t.has("Accept") || t.set("Accept", "application/json"), t;
}
function H(e, t) {
  return y(e.defaultHeaders ?? e.headers, t);
}
function me(e, t = {}) {
  return async () => await e.getAccessToken() ?? await t.fallbackAccessTokenFactory?.() ?? t.anonymousToken ?? "";
}
function Ge(e, t) {
  const r = Se(e.accessTokenFactory) ? e.accessTokenFactory.bind(e) : void 0;
  return {
    ...e,
    accessTokenFactory: me(t, { fallbackAccessTokenFactory: r })
  };
}
function Se(e) {
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
function Ve(e) {
  return e;
}
function d(e = "studio-host") {
  return { kind: "host", id: e };
}
function Qe(e) {
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
      return B(t, r, e, n).map((o) => o.contribution);
    },
    compose(n) {
      return B(t, r, e, n);
    }
  };
}
function Xe() {
  const e = /* @__PURE__ */ new Set(), t = /* @__PURE__ */ new Set();
  return {
    contextProviders: S({ slot: m.aiContextProviders }),
    promptActions: S({ slot: m.aiPromptActions }),
    tools: S({ slot: m.aiTools }),
    proposalRenderers: S({ slot: m.aiProposalRenderers }),
    surfaces: S({ slot: m.aiSurfaces }),
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
function Ye() {
  const e = [], t = /* @__PURE__ */ new Set();
  let r = 1;
  const n = () => e[0] ?? null, o = () => {
    const s = n();
    for (const a of t)
      a(s);
  };
  function i(s) {
    e.push({ ...s, id: r++ }), e.length === 1 && o();
  }
  return {
    api: {
      confirm(s) {
        return new Promise((a) => {
          i({ ...s, kind: "confirm", settle: (l) => a(l === !0) });
        });
      },
      prompt(s) {
        return new Promise((a) => {
          i({ ...s, kind: "prompt", settle: (l) => a(typeof l == "string" ? l : null) });
        });
      },
      alert(s) {
        return new Promise((a) => {
          i({ ...s, kind: "alert", settle: () => a() });
        });
      }
    },
    subscribe(s) {
      return t.add(s), s(n()), () => {
        t.delete(s);
      };
    },
    getCurrent: n,
    respond(s, a) {
      const l = e.findIndex((C) => C.id === s);
      if (l === -1)
        return;
      const [w] = e.splice(l, 1);
      w.settle(a), l === 0 && o();
    },
    cancelAll() {
      if (e.length === 0)
        return;
      const s = e.splice(0);
      for (const a of s)
        a.settle(a.kind === "prompt" ? null : !1);
      o();
    }
  };
}
function B(e, t, r, n = {}) {
  return e.map((o, i) => ({
    contribution: o,
    slot: t,
    availability: Pe(o, t, r, n),
    order: r.getOrder?.(o) ?? Ee(o),
    stableKey: r.getStableKey?.(o) ?? Te(o, i),
    index: i
  })).filter((o) => xe(o.availability, n)).sort((o, i) => o.order - i.order || o.stableKey.localeCompare(i.stableKey) || o.index - i.index).map(({ index: o, ...i }) => i);
}
function Pe(e, t, r, n) {
  const o = { contribution: e, slot: t, context: n.context }, i = R(r.slotOwner?.(o), "slot-owner");
  if (i.state !== "available")
    return i;
  const c = K(e, "moduleId");
  if (c && n.disabledModuleIds?.includes(c))
    return { state: "hidden", reason: `Module ${c} is disabled.`, source: "module" };
  const s = K(e, "featureId");
  if (s && n.disabledFeatureIds?.includes(s))
    return { state: "hidden", reason: `Feature ${s} is disabled.`, source: "feature" };
  const a = R(Ce(e, n.context), "runtime"), l = R(r.hostPolicy?.(o), "host-policy");
  if (l.state !== "available")
    return l;
  const w = R(n.hostPolicy?.(o), "host-policy");
  return w.state !== "available" ? w : a;
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
function Ce(e, t) {
  if (!L(e) || !("availability" in e))
    return !0;
  const r = e.availability;
  return typeof r == "function" ? r(t) : r;
}
function xe(e, t) {
  return e.state === "available" ? !0 : e.state === "hidden" ? t.includeHidden === !0 : t.includeUnavailable === !0;
}
function Ee(e) {
  return Re(e, "order") ?? 500;
}
function Te(e, t) {
  if (!L(e))
    return `_${t.toString().padStart(4, "0")}`;
  for (const r of ["id", "name", "label", "title", "path"]) {
    const n = e[r];
    if (typeof n == "string" && n.length > 0)
      return n;
  }
  return `_${t.toString().padStart(4, "0")}`;
}
function K(e, t) {
  if (!L(e))
    return;
  const r = e[t];
  return typeof r == "string" ? r : void 0;
}
function Re(e, t) {
  if (!L(e))
    return;
  const r = e[t];
  return typeof r == "number" ? r : void 0;
}
function L(e) {
  return typeof e == "object" && e !== null;
}
function Ze(e, t = {}) {
  return {
    baseUrl: e,
    headers: t.headers,
    http: Ie(e, t.headers)
  };
}
function Ie(e, t) {
  return {
    requestJson(r, n) {
      return A(e, r, y(t, I(n)));
    },
    async getJson(r, n) {
      return A(e, r, y(t, I(n)));
    },
    async postJson(r, n, o) {
      return A(e, r, y(t, {
        ...o,
        method: "POST",
        headers: _(o?.headers),
        body: JSON.stringify(n)
      }));
    },
    async putJson(r, n, o) {
      return A(e, r, y(t, {
        ...o,
        method: "PUT",
        headers: _(o?.headers),
        body: JSON.stringify(n)
      }));
    },
    async deleteJson(r, n) {
      return A(e, r, y(t, I({
        ...n,
        method: "DELETE"
      })));
    },
    async postForm(r, n, o) {
      return A(e, r, y(t, I({
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
    headers: $e(e, t.headers)
  } : t;
}
function $e(e, t) {
  const r = new Headers(e);
  return new Headers(t).forEach((n, o) => r.set(o, n)), r;
}
async function A(e, t, r) {
  const n = qe(e, t), o = new AbortController(), i = globalThis.setTimeout(() => o.abort(), M);
  let c;
  try {
    c = await fetch(n, {
      ...r,
      signal: je(r?.signal, o.signal)
    });
  } catch (a) {
    throw o.signal.aborted && !r?.signal?.aborted ? new Error(`Request to ${n} timed out after ${M / 1e3} seconds. Check Studio:BackendBaseUrl and make sure the backend API is responding.`) : a;
  } finally {
    globalThis.clearTimeout(i);
  }
  if (!c.ok)
    throw await ee(c);
  const s = await c.text();
  if (!s.trim())
    return {};
  try {
    return JSON.parse(s);
  } catch {
    throw new P(
      c.status,
      `Expected JSON from ${n}, but received ${Fe(c, s)}. Check Studio:BackendBaseUrl and make sure the backend maps this API route.`
    );
  }
}
function je(e, t) {
  if (!e)
    return t;
  if (typeof AbortSignal.any == "function")
    return AbortSignal.any([e, t]);
  const r = new AbortController(), n = () => r.abort();
  return e.aborted || t.aborted ? r.abort() : (e.addEventListener("abort", n, { once: !0 }), t.addEventListener("abort", n, { once: !0 })), r.signal;
}
async function Le(e) {
  return (await te(e)).message;
}
async function ee(e) {
  const t = await te(e);
  return new P(e.status, t.message, t.validationErrors, t.payload);
}
async function te(e) {
  const t = e.headers.get("content-type") ?? "";
  if (Je(t))
    try {
      const n = await e.json(), o = re(n);
      return {
        message: De(n) ?? Ue(o) ?? `Request failed with ${e.status}.`,
        validationErrors: o,
        payload: n
      };
    } catch {
      return { message: `Request failed with ${e.status}.`, validationErrors: null, payload: null };
    }
  return { message: (await e.text()).trim() || `Request failed with ${e.status}.`, validationErrors: null, payload: null };
}
function Je(e) {
  return e.toLowerCase().includes("json");
}
async function et(e) {
  if (e instanceof P)
    return e.message;
  if (ne(e))
    try {
      return await Le(e.response.clone());
    } catch {
      return e.response.statusText || "Request failed.";
    }
  return e instanceof Error ? e.message : "Unknown error.";
}
async function tt(e) {
  if (e instanceof P)
    return e.validationErrors;
  if (!ne(e))
    return null;
  try {
    const t = await e.response.clone().json();
    return re(t);
  } catch {
    return null;
  }
}
function De(e) {
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
function re(e) {
  const t = e.errors;
  if (!t || typeof t != "object" || Array.isArray(t))
    return null;
  const r = {};
  for (const [n, o] of Object.entries(t)) {
    const i = Array.isArray(o) ? o.map(String) : [String(o)];
    i.length > 0 && (r[n] = i);
  }
  return Object.keys(r).length > 0 ? r : null;
}
function Ue(e) {
  return e ? Object.values(e).flat().join(" ") : null;
}
function ne(e) {
  return typeof e == "object" && e !== null && "response" in e && e.response instanceof Response;
}
function qe(e, t) {
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
function _(e) {
  const t = new Headers(e);
  return t.has("Content-Type") || t.set("Content-Type", "application/json"), t.has("Accept") || t.set("Accept", "application/json"), t;
}
function Fe(e, t) {
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
  k as AuthAdapterError,
  x as AuthConfigurationError,
  Me as AuthGuard,
  ze as AuthProvider,
  Be as RequireAuth,
  P as StudioHttpError,
  Xe as createAiContributionApi,
  de as createAuthProviderManager,
  _e as createAuthenticatedHttpClient,
  We as createBackendAuthProviderManager,
  S as createContributionRegistry,
  Ye as createDialogController,
  Ze as createEndpointContext,
  Ie as createHttpClient,
  Ke as createOidcAuthAdapter,
  Q as createRedirectAuthAdapter,
  me as createSignalRAccessTokenFactory,
  ee as createStudioHttpError,
  Ve as defineStudioSlot,
  et as describeApiError,
  d as hostSlotOwner,
  Qe as moduleSlotOwner,
  Le as readStudioHttpErrorMessage,
  u as studioSlotIds,
  m as studioSlots,
  tt as tryExtractValidationErrors,
  He as useAuthCapabilities,
  q as useAuthContext,
  ye as useAuthSession,
  ve as usePermissions,
  Ge as withAuthenticatedSignalROptions,
  y as withDefaultHeaders
};
