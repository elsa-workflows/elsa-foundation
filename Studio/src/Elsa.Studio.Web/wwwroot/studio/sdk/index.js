import { j as p } from "../vendor/chunks/jsx-runtime.js";
import { createContext as tt, useContext as et, useState as F, useRef as $, useCallback as b, useLayoutEffect as rt, useMemo as O, useEffect as nt } from "react";
const T = {
  status: "anonymous",
  roles: [],
  permissions: []
}, B = {
  status: "unknown",
  roles: [],
  permissions: []
};
function it(t) {
  return new ot(t);
}
class ot {
  constructor(e) {
    this.options = e;
    for (const r of e.adapters ?? []) {
      if (this.adapters.has(r.id))
        throw new P(`Duplicate auth provider adapter '${r.id}'.`);
      this.adapters.set(r.id, r);
    }
  }
  options;
  adapters = /* @__PURE__ */ new Map();
  activeAdapter = null;
  pendingLoginProviderId = null;
  session = B;
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
    const e = await this.options.bootstrap(), r = e.providers.find((i) => i.enabled && i.isDefault) ?? e.providers.find((i) => i.enabled);
    if (!r)
      throw new P("No enabled authentication provider was returned by /_elsa/identity/bootstrap.");
    const n = this.resolveProviderAdapter(r);
    return this.activeAdapter = n, n;
  }
  async getProviderAdapter(e) {
    const r = this.adapters.get(e);
    if (r)
      return r;
    const i = (await this.options.bootstrap()).providers.find((o) => o.enabled && o.id === e);
    if (!i)
      throw new P(`No auth provider adapter is registered for '${e}'.`);
    return this.resolveProviderAdapter(i);
  }
  resolveProviderAdapter(e) {
    const r = this.adapters.get(e.id);
    if (r)
      return r;
    if (!this.options.adapterFactory)
      throw new P(`No auth provider adapter is registered for '${e.id}'.`);
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
class P extends Error {
  constructor(e) {
    super(e), this.name = "AuthConfigurationError";
  }
}
function K(t) {
  const e = t.fetch ?? fetch, r = t.sessionEndpoint ?? "/_elsa/identity/session", n = t.logoutEndpoint ?? `/_elsa/identity/logout/${encodeURIComponent(t.id)}`;
  return {
    id: t.id,
    kind: t.kind,
    initialize: () => S(e, r, t),
    login: (i) => {
      const o = t.challenge;
      if (!o || o.type === "none")
        throw new v(`Provider '${t.id}' does not expose a redirect challenge.`);
      const a = "method" in o ? o.method.toUpperCase() : "GET";
      if (a !== "GET")
        throw new v(`Provider '${t.id}' exposes an unsupported ${a} challenge.`);
      const s = new URL(ct(o), U(t)), c = i?.returnUrl ?? t.location?.href ?? window.location.href;
      return s.searchParams.set("returnUrl", dt(c, i?.providerId ?? t.id, t)), (t.location ?? window.location).assign(s.toString()), Promise.resolve();
    },
    handleCallback: () => S(e, r, t),
    logout: async () => {
      const i = await e(E(n, t), { method: "POST", credentials: "include" });
      if (!i.ok)
        throw new v(`Sign-out failed with ${i.status}.`);
    },
    getAccessToken: async () => {
      if (!t.tokenEndpoint)
        return null;
      const i = await e(E(t.tokenEndpoint, t), { credentials: "include", cache: "no-store" });
      if (i.status === 401)
        return null;
      if (!i.ok)
        throw new v(`Access-token request failed with ${i.status}.`);
      const o = await i.json();
      return typeof o.accessToken == "string" ? o.accessToken : null;
    },
    refresh: async () => {
      const i = await t.getRefreshToken?.(), o = t.refreshEndpoint;
      if (!o || !i)
        return S(e, r, t);
      const a = await e(E(o, t), {
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
      return s.status ? _(s) : S(e, r, t);
    }
  };
}
async function S(t, e, r) {
  const n = await t(E(e, r), { credentials: "include", cache: "no-store" });
  if (n.status === 401)
    return T;
  if (!n.ok)
    throw new v(`Session request failed with ${n.status}.`);
  return st(n);
}
async function st(t) {
  const e = await t.json();
  return _(e);
}
function _(t) {
  const e = at(t.status) ? t.status : "anonymous";
  return {
    ...t,
    status: e,
    roles: H(t.roles),
    permissions: H(t.permissions)
  };
}
function H(t) {
  return Array.isArray(t) ? t.filter((e) => typeof e == "string") : [];
}
function at(t) {
  return t === "unknown" || t === "anonymous" || t === "authenticated";
}
function ct(t) {
  return "loginPath" in t ? t.loginPath : t.url;
}
function E(t, e) {
  return new URL(t, U(e)).toString();
}
function U(t) {
  return t?.baseUrl ?? t?.location?.origin ?? window.location.origin;
}
function dt(t, e, r) {
  const n = new URL(t, ut(r));
  return n.searchParams.set("authProviderId", e), lt(t) ? `${n.pathname}${n.search}${n.hash}` : n.toString();
}
function ut(t) {
  return t?.location?.href ?? (typeof window < "u" ? window.location.href : void 0) ?? t?.location?.origin ?? U(t);
}
function lt(t) {
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
function zt(t = {}) {
  const e = t.baseUrl ?? window.location.origin, r = t.fetch ?? fetch;
  return it({
    bootstrap: () => q(r, e, "/_elsa/identity/bootstrap"),
    capabilities: () => q(r, e, "/_elsa/identity/capabilities"),
    isCallback: t.isCallback,
    getCallbackProviderId: t.getCallbackProviderId,
    adapterFactory: (n) => K({
      id: n.id,
      kind: n.kind,
      baseUrl: e,
      challenge: n.challenge,
      fetch: r
    })
  });
}
async function q(t, e, r) {
  const n = await t(new URL(r, e).toString(), {
    credentials: "include",
    cache: "no-store",
    headers: { Accept: "application/json" }
  });
  if (!n.ok)
    throw new Error(`Auth discovery request failed with ${n.status}.`);
  return await n.json();
}
const G = tt(null);
function L() {
  const t = et(G);
  if (!t)
    throw new Error("Auth SDK hooks must be used within <AuthProvider>.");
  return t;
}
function Mt({ manager: t, children: e }) {
  const [r, n] = F(() => t.getSession() ?? B), [i, o] = F(null), a = $(!1), s = $(0), c = b((f) => a.current && s.current === f, []), l = b(async (f) => {
    if (c(f)) {
      o(null);
      try {
        const h = await t.getCapabilities();
        c(f) && o(h);
      } catch (h) {
        c(f) && (console.error("Auth capabilities request failed.", h), o(null));
      }
    }
  }, [t, c]);
  rt(() => {
    a.current = !0;
    const f = ++s.current;
    async function h() {
      try {
        const g = await t.initialize();
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
  }, [l, t, c]);
  const w = b(async (f) => {
    const h = ++s.current;
    if (await t.login(f), !c(h))
      return;
    const g = t.getSession();
    n(g), g.status === "authenticated" ? await l(h) : o(null);
  }, [l, t, c]), m = b(async () => {
    const f = ++s.current;
    await t.logout(), c(f) && (n(t.getSession()), o(null));
  }, [t, c]), D = b(async () => {
    const f = ++s.current, h = await t.refresh();
    return c(f) && (n(h), h.status === "authenticated" ? await l(f) : o(null)), h;
  }, [l, t, c]), Z = O(() => ({
    session: r,
    capabilities: i,
    login: w,
    logout: m,
    refresh: D
  }), [i, w, m, D, r]);
  return /* @__PURE__ */ p.jsx(G.Provider, { value: Z, children: e });
}
function ft() {
  return L().session;
}
function ht() {
  const { permissions: t } = ft();
  return O(() => {
    const e = new Set(t);
    return {
      has: (r) => e.has(r),
      hasAny: (r) => r.some((n) => e.has(n)),
      hasAll: (r) => r.every((n) => e.has(n))
    };
  }, [t]);
}
function Nt() {
  return L().capabilities;
}
function Ot({ requires: t, requireAll: e = !0, fallback: r = null, children: n }) {
  const i = ht(), o = typeof t == "string" ? [t] : t ?? [];
  return o.length === 0 || (e ? i.hasAll(o) : i.hasAny(o)) ? /* @__PURE__ */ p.jsx(p.Fragment, { children: n }) : /* @__PURE__ */ p.jsx(p.Fragment, { children: r });
}
function Bt({ children: t, fallback: e = null, loginOptions: r }) {
  const { session: n, login: i } = L(), o = $(null);
  return nt(() => {
    if (n.status === "anonymous") {
      const a = pt(r), s = o.current;
      if (s?.key === a && s.login === i)
        return;
      const c = { key: a, login: i };
      o.current = c, i(r).catch((l) => {
        o.current === c && (o.current = null), console.error("Auth login failed.", l);
      });
    } else
      o.current = null;
  }, [i, r, n.status]), n.status !== "authenticated" ? /* @__PURE__ */ p.jsx(p.Fragment, { children: e }) : /* @__PURE__ */ p.jsx(p.Fragment, { children: t });
}
function pt(t) {
  return `${t?.providerId ?? ""}
${t?.returnUrl ?? ""}`;
}
function Kt(t) {
  return K({
    ...t,
    kind: "external-oidc"
  });
}
function _t(t, e, r = {}) {
  return V(t, {
    defaultHeaders: r.defaultHeaders ?? r.headers,
    applyTimeout: !1,
    fetch: wt(e, r)
  });
}
const j = /* @__PURE__ */ new Map();
function wt(t, e) {
  const r = e.fetch ?? fetch;
  return (async (n, i) => {
    const o = typeof n == "string" ? n : n.toString(), a = await r(o, await W(t, i));
    return a.status !== 401 || e.refreshOnUnauthorized === !1 ? a : await gt(o, t) ? r(o, await W(t, i)) : new Response("Authentication required.", { status: 401 });
  });
}
async function gt(t, e) {
  const r = new URL(t).origin, n = j.get(r);
  if (n)
    return n;
  const i = e.refresh().then((o) => o.status === "authenticated").finally(() => j.delete(r));
  return j.set(r, i), i;
}
async function W(t, e) {
  const r = new Headers(e?.headers), n = await t.getAccessToken();
  return n && r.set("Authorization", `Bearer ${n}`), {
    ...e,
    credentials: e?.credentials ?? "include",
    headers: r
  };
}
function yt(t, e = {}) {
  return async () => await t.getAccessToken() ?? await e.fallbackAccessTokenFactory?.() ?? e.anonymousToken ?? "";
}
function Gt(t, e) {
  const r = vt(t.accessTokenFactory) ? t.accessTokenFactory.bind(t) : void 0;
  return {
    ...t,
    accessTokenFactory: yt(e, { fallbackAccessTokenFactory: r })
  };
}
function vt(t) {
  return typeof t == "function";
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
function Vt(t) {
  return t;
}
function d(t = "studio-host") {
  return { kind: "host", id: t };
}
function Qt(t) {
  return { kind: "module", id: t, moduleId: t };
}
function k(t = {}) {
  const e = [], r = t.slot ?? {
    id: "studio.unknown",
    kind: "unknown",
    owner: d(),
    title: "Unknown contributions"
  };
  return {
    slot: r,
    add(n) {
      e.push(n);
    },
    list(n) {
      return z(e, r, t, n).map((i) => i.contribution);
    },
    compose(n) {
      return z(e, r, t, n);
    }
  };
}
function Xt() {
  const t = /* @__PURE__ */ new Set(), e = /* @__PURE__ */ new Set();
  return {
    contextProviders: k({ slot: A.aiContextProviders }),
    promptActions: k({ slot: A.aiPromptActions }),
    tools: k({ slot: A.aiTools }),
    proposalRenderers: k({ slot: A.aiProposalRenderers }),
    surfaces: k({ slot: A.aiSurfaces }),
    dispatchPrompt(r) {
      for (const n of t)
        n(r);
    },
    onPrompt(r) {
      return t.add(r), () => t.delete(r);
    },
    publishPromptResult(r) {
      for (const n of e)
        n(r);
    },
    onPromptResult(r) {
      return e.add(r), () => e.delete(r);
    }
  };
}
function Yt() {
  const t = [], e = /* @__PURE__ */ new Set();
  let r = 1;
  const n = () => t[0] ?? null, i = () => {
    const s = n();
    for (const c of e)
      c(s);
  };
  function o(s) {
    t.push({ ...s, id: r++ }), t.length === 1 && i();
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
      return e.add(s), s(n()), () => {
        e.delete(s);
      };
    },
    getCurrent: n,
    respond(s, c) {
      const l = t.findIndex((m) => m.id === s);
      if (l === -1)
        return;
      const [w] = t.splice(l, 1);
      w.settle(c), l === 0 && i();
    },
    cancelAll() {
      if (t.length === 0)
        return;
      const s = t.splice(0);
      for (const c of s)
        c.settle(c.kind === "prompt" ? null : !1);
      i();
    }
  };
}
function z(t, e, r, n = {}) {
  return t.map((i, o) => ({
    contribution: i,
    slot: e,
    availability: bt(i, e, r, n),
    order: r.getOrder?.(i) ?? mt(i),
    stableKey: r.getStableKey?.(i) ?? Pt(i, o),
    index: o
  })).filter((i) => kt(i.availability, n)).sort((i, o) => i.order - o.order || i.stableKey.localeCompare(o.stableKey) || i.index - o.index).map(({ index: i, ...o }) => o);
}
function bt(t, e, r, n) {
  const i = { contribution: t, slot: e, context: n.context }, o = C(r.slotOwner?.(i), "slot-owner");
  if (o.state !== "available")
    return o;
  const a = M(t, "moduleId");
  if (a && n.disabledModuleIds?.includes(a))
    return { state: "hidden", reason: `Module ${a} is disabled.`, source: "module" };
  const s = M(t, "featureId");
  if (s && n.disabledFeatureIds?.includes(s))
    return { state: "hidden", reason: `Feature ${s} is disabled.`, source: "feature" };
  const c = C(At(t, n.context), "runtime"), l = C(r.hostPolicy?.(i), "host-policy");
  if (l.state !== "available")
    return l;
  const w = C(n.hostPolicy?.(i), "host-policy");
  return w.state !== "available" ? w : c;
}
function C(t, e) {
  if (t === !1)
    return { state: "hidden", source: e };
  if (t && typeof t == "object") {
    const r = t;
    return !("state" in r) && typeof r.status == "string" ? {
      state: r.status === "available" ? "available" : "unavailable",
      reason: typeof r.reason == "string" ? r.reason : void 0,
      source: e
    } : { ...t, source: t.source ?? e };
  }
  return { state: "available" };
}
function At(t, e) {
  if (!R(t) || !("availability" in t))
    return !0;
  const r = t.availability;
  return typeof r == "function" ? r(e) : r;
}
function kt(t, e) {
  return t.state === "available" ? !0 : t.state === "hidden" ? e.includeHidden === !0 : e.includeUnavailable === !0;
}
function mt(t) {
  return St(t, "order") ?? 500;
}
function Pt(t, e) {
  if (!R(t))
    return `_${e.toString().padStart(4, "0")}`;
  for (const r of ["id", "name", "label", "title", "path"]) {
    const n = t[r];
    if (typeof n == "string" && n.length > 0)
      return n;
  }
  return `_${e.toString().padStart(4, "0")}`;
}
function M(t, e) {
  if (!R(t))
    return;
  const r = t[e];
  return typeof r == "string" ? r : void 0;
}
function St(t, e) {
  if (!R(t))
    return;
  const r = t[e];
  return typeof r == "number" ? r : void 0;
}
function R(t) {
  return typeof t == "object" && t !== null;
}
function Zt(t, e = {}) {
  return {
    baseUrl: t,
    headers: e.headers,
    http: V(t, e.headers)
  };
}
function V(t, e) {
  const r = Ct(e), { defaultHeaders: n } = r, i = (o, a) => Tt(t, o, a, r);
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
function Ct(t) {
  return xt(t) ? t : { defaultHeaders: t };
}
function xt(t) {
  return typeof t == "object" && t !== null && !(t instanceof Headers) && !Array.isArray(t) && ("fetch" in t || "applyTimeout" in t || "defaultHeaders" in t);
}
function y(t, e = {}) {
  return t ? {
    ...e,
    headers: Et(t, e.headers)
  } : e;
}
function Et(t, e) {
  const r = new Headers(t);
  return new Headers(e).forEach((n, i) => r.set(i, n)), r;
}
async function Tt(t, e, r, n = {}) {
  const i = Ht(t, e), o = n.fetch ?? fetch, a = n.applyTimeout === !1 ? await o(i, r) : await Rt(o, i, r);
  return It(i, a);
}
async function Rt(t, e, r) {
  const n = new AbortController(), i = globalThis.setTimeout(() => n.abort(), J);
  try {
    return await t(e, {
      ...r,
      signal: jt(r?.signal, n.signal)
    });
  } catch (o) {
    throw n.signal.aborted && !r?.signal?.aborted ? new Error(`Request to ${e} timed out after ${J / 1e3} seconds. Check Studio:BackendBaseUrl and make sure the backend API is responding.`) : o;
  } finally {
    globalThis.clearTimeout(i);
  }
}
async function It(t, e) {
  if (!e.ok)
    throw await Ut(e);
  const r = await e.text();
  if (!r.trim())
    return {};
  try {
    return JSON.parse(r);
  } catch {
    throw new I(
      e.status,
      `Expected JSON from ${t}, but received ${qt(e, r)}. Check Studio:BackendBaseUrl and make sure the backend maps this API route.`
    );
  }
}
function jt(t, e) {
  if (!t)
    return e;
  if (typeof AbortSignal.any == "function")
    return AbortSignal.any([t, e]);
  const r = new AbortController(), n = () => r.abort();
  return t.aborted || e.aborted ? r.abort() : (t.addEventListener("abort", n, { once: !0 }), e.addEventListener("abort", n, { once: !0 })), r.signal;
}
async function $t(t) {
  return (await Q(t)).message;
}
async function Ut(t) {
  const e = await Q(t);
  return new I(t.status, e.message, e.validationErrors, e.payload);
}
async function Q(t) {
  const e = t.headers.get("content-type") ?? "";
  if (Lt(e))
    try {
      const n = await t.json(), i = X(n);
      return {
        message: Dt(n) ?? Ft(i) ?? `Request failed with ${t.status}.`,
        validationErrors: i,
        payload: n
      };
    } catch {
      return { message: `Request failed with ${t.status}.`, validationErrors: null, payload: null };
    }
  return { message: (await t.text()).trim() || `Request failed with ${t.status}.`, validationErrors: null, payload: null };
}
function Lt(t) {
  return t.toLowerCase().includes("json");
}
async function te(t) {
  if (t instanceof I)
    return t.message;
  if (Y(t))
    try {
      return await $t(t.response.clone());
    } catch {
      return t.response.statusText || "Request failed.";
    }
  return t instanceof Error ? t.message : "Unknown error.";
}
async function ee(t) {
  if (t instanceof I)
    return t.validationErrors;
  if (!Y(t))
    return null;
  try {
    const e = await t.response.clone().json();
    return X(e);
  } catch {
    return null;
  }
}
function Dt(t) {
  if (typeof t.detail == "string" && t.detail.length > 0) return t.detail;
  if (typeof t.title == "string" && t.title.length > 0) return t.title;
  if (typeof t.reason == "string" && t.reason.length > 0) return t.reason;
  if (Array.isArray(t.errors) && t.errors.length > 0) return t.errors.map(String).join(" ");
  if (t.errors && typeof t.errors == "object") {
    const e = Object.values(t.errors).flatMap((r) => Array.isArray(r) ? r : [r]).map(String);
    if (e.length > 0) return e.join(" ");
  }
  return null;
}
function X(t) {
  const e = t.errors;
  if (!e || typeof e != "object" || Array.isArray(e))
    return null;
  const r = {};
  for (const [n, i] of Object.entries(e)) {
    const o = Array.isArray(i) ? i.map(String) : [String(i)];
    o.length > 0 && (r[n] = o);
  }
  return Object.keys(r).length > 0 ? r : null;
}
function Ft(t) {
  return t ? Object.values(t).flat().join(" ") : null;
}
function Y(t) {
  return typeof t == "object" && t !== null && "response" in t && t.response instanceof Response;
}
function Ht(t, e) {
  return new URL(e, t).toString();
}
function x(t) {
  const e = new Headers(t?.headers);
  return e.has("Accept") || e.set("Accept", "application/json"), {
    ...t,
    cache: t?.cache ?? "no-store",
    headers: e
  };
}
function N(t) {
  const e = new Headers(t);
  return e.has("Content-Type") || e.set("Content-Type", "application/json"), e.has("Accept") || e.set("Accept", "application/json"), e;
}
function qt(t, e) {
  const r = t.headers.get("content-type") ?? "an unknown content type", n = e.trim(), i = n.length > 0 ? `: ${n.slice(0, 80)}` : "";
  return `${r}${i}`;
}
class I extends Error {
  constructor(e, r, n = null, i = null) {
    super(r), this.status = e, this.validationErrors = n, this.payload = i, this.name = "StudioHttpError";
  }
  status;
  validationErrors;
  payload;
}
export {
  v as AuthAdapterError,
  P as AuthConfigurationError,
  Ot as AuthGuard,
  Mt as AuthProvider,
  Bt as RequireAuth,
  I as StudioHttpError,
  Xt as createAiContributionApi,
  it as createAuthProviderManager,
  _t as createAuthenticatedHttpClient,
  zt as createBackendAuthProviderManager,
  k as createContributionRegistry,
  Yt as createDialogController,
  Zt as createEndpointContext,
  V as createHttpClient,
  Kt as createOidcAuthAdapter,
  K as createRedirectAuthAdapter,
  yt as createSignalRAccessTokenFactory,
  Ut as createStudioHttpError,
  Vt as defineStudioSlot,
  te as describeApiError,
  d as hostSlotOwner,
  Qt as moduleSlotOwner,
  $t as readStudioHttpErrorMessage,
  u as studioSlotIds,
  A as studioSlots,
  ee as tryExtractValidationErrors,
  Nt as useAuthCapabilities,
  L as useAuthContext,
  ft as useAuthSession,
  ht as usePermissions,
  Gt as withAuthenticatedSignalROptions,
  y as withDefaultHeaders
};
