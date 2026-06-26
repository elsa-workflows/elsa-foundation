import { j as f } from "../vendor/chunks/jsx-runtime.js";
import { r as l } from "../vendor/chunks/index.js";
const T = {
  status: "anonymous",
  roles: [],
  permissions: []
}, M = {
  status: "unknown",
  roles: [],
  permissions: []
};
function X(t) {
  return new Y(t);
}
class Y {
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
  session = M;
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
    const e = await this.options.bootstrap(), n = e.providers.find((i) => i.enabled && i.isDefault) ?? e.providers.find((i) => i.enabled);
    if (!n)
      throw new S("No enabled authentication provider was returned by /_elsa/identity/bootstrap.");
    const r = this.resolveProviderAdapter(n);
    return this.activeAdapter = r, r;
  }
  async getProviderAdapter(e) {
    const n = this.adapters.get(e);
    if (n)
      return n;
    const i = (await this.options.bootstrap()).providers.find((o) => o.enabled && o.id === e);
    if (!i)
      throw new S(`No auth provider adapter is registered for '${e}'.`);
    return this.resolveProviderAdapter(i);
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
function H(t) {
  const e = t.fetch ?? fetch, n = t.sessionEndpoint ?? "/_elsa/identity/session", r = t.logoutEndpoint ?? `/_elsa/identity/logout/${encodeURIComponent(t.id)}`;
  return {
    id: t.id,
    kind: t.kind,
    initialize: () => C(e, n, t),
    login: (i) => {
      const o = t.challenge;
      if (!o || o.type === "none")
        throw new A(`Provider '${t.id}' does not expose a redirect challenge.`);
      const s = "method" in o ? o.method.toUpperCase() : "GET";
      if (s !== "GET")
        throw new A(`Provider '${t.id}' exposes an unsupported ${s} challenge.`);
      const d = new URL(et(o), N(t)), a = i?.returnUrl ?? t.location?.href ?? window.location.href;
      return d.searchParams.set("returnUrl", nt(a, i?.providerId ?? t.id, t)), (t.location ?? window.location).assign(d.toString()), Promise.resolve();
    },
    handleCallback: () => C(e, n, t),
    logout: async () => {
      const i = await e(x(r, t), { method: "POST", credentials: "include" });
      if (!i.ok)
        throw new A(`Sign-out failed with ${i.status}.`);
    },
    getAccessToken: async () => {
      if (!t.tokenEndpoint)
        return null;
      const i = await e(x(t.tokenEndpoint, t), { credentials: "include", cache: "no-store" });
      if (i.status === 401)
        return null;
      if (!i.ok)
        throw new A(`Access-token request failed with ${i.status}.`);
      const o = await i.json();
      return typeof o.accessToken == "string" ? o.accessToken : null;
    },
    refresh: async () => {
      const i = await t.getRefreshToken?.(), o = t.refreshEndpoint;
      if (!o || !i)
        return C(e, n, t);
      const s = await e(x(o, t), {
        method: "POST",
        credentials: "include",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json"
        },
        body: JSON.stringify({ refreshToken: i })
      });
      if (s.status === 401)
        return T;
      if (!s.ok)
        throw new A(`Session refresh failed with ${s.status}.`);
      const d = await s.json();
      return d.status ? z(d) : C(e, n, t);
    }
  };
}
async function C(t, e, n) {
  const r = await t(x(e, n), { credentials: "include", cache: "no-store" });
  if (r.status === 401)
    return T;
  if (!r.ok)
    throw new A(`Session request failed with ${r.status}.`);
  return Z(r);
}
async function Z(t) {
  const e = await t.json();
  return z(e);
}
function z(t) {
  const e = tt(t.status) ? t.status : "anonymous";
  return {
    ...t,
    status: e,
    roles: J(t.roles),
    permissions: J(t.permissions)
  };
}
function J(t) {
  return Array.isArray(t) ? t.filter((e) => typeof e == "string") : [];
}
function tt(t) {
  return t === "unknown" || t === "anonymous" || t === "authenticated";
}
function et(t) {
  return "loginPath" in t ? t.loginPath : t.url;
}
function x(t, e) {
  return new URL(t, N(e)).toString();
}
function N(t) {
  return t?.baseUrl ?? t?.location?.origin ?? window.location.origin;
}
function nt(t, e, n) {
  const r = new URL(t, rt(n));
  return r.searchParams.set("authProviderId", e), it(t) ? `${r.pathname}${r.search}${r.hash}` : r.toString();
}
function rt(t) {
  return t?.location?.href ?? (typeof window < "u" ? window.location.href : void 0) ?? t?.location?.origin ?? N(t);
}
function it(t) {
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
function St(t = {}) {
  const e = t.baseUrl ?? window.location.origin, n = t.fetch ?? fetch;
  return X({
    bootstrap: () => D(n, e, "/_elsa/identity/bootstrap"),
    capabilities: () => D(n, e, "/_elsa/identity/capabilities"),
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
async function D(t, e, n) {
  const r = await t(new URL(n, e).toString(), {
    credentials: "include",
    cache: "no-store",
    headers: { Accept: "application/json" }
  });
  if (!r.ok)
    throw new Error(`Auth discovery request failed with ${r.status}.`);
  return await r.json();
}
const B = l.createContext(null);
function j() {
  const t = l.useContext(B);
  if (!t)
    throw new Error("Auth SDK hooks must be used within <AuthProvider>.");
  return t;
}
function Ct({ manager: t, children: e }) {
  const [n, r] = l.useState(() => t.getSession() ?? M), [i, o] = l.useState(null), s = l.useRef(!1), d = l.useRef(0), a = l.useCallback((u) => s.current && d.current === u, []), p = l.useCallback(async (u) => {
    if (a(u)) {
      o(null);
      try {
        const h = await t.getCapabilities();
        a(u) && o(h);
      } catch (h) {
        a(u) && (console.error("Auth capabilities request failed.", h), o(null));
      }
    }
  }, [t, a]);
  l.useLayoutEffect(() => {
    s.current = !0;
    const u = ++d.current;
    async function h() {
      try {
        const w = await t.initialize();
        if (!a(u))
          return;
        if (r(w), w.status !== "authenticated") {
          o(null);
          return;
        }
        await p(u);
      } catch (w) {
        a(u) && (console.error("Auth initialization failed.", w), r(T), o(null));
      }
    }
    return h(), () => {
      s.current = !1, d.current += 1;
    };
  }, [p, t, a]);
  const $ = l.useCallback(async (u) => {
    const h = ++d.current;
    if (await t.login(u), !a(h))
      return;
    const w = t.getSession();
    r(w), w.status === "authenticated" ? await p(h) : o(null);
  }, [p, t, a]), I = l.useCallback(async () => {
    const u = ++d.current;
    await t.logout(), a(u) && (r(t.getSession()), o(null));
  }, [t, a]), L = l.useCallback(async () => {
    const u = ++d.current, h = await t.refresh();
    return a(u) && (r(h), h.status === "authenticated" ? await p(u) : o(null)), h;
  }, [p, t, a]), Q = l.useMemo(() => ({
    session: n,
    capabilities: i,
    login: $,
    logout: I,
    refresh: L
  }), [i, $, I, L, n]);
  return /* @__PURE__ */ f.jsx(B.Provider, { value: Q, children: e });
}
function ot() {
  return j().session;
}
function st() {
  const { permissions: t } = ot();
  return l.useMemo(() => {
    const e = new Set(t);
    return {
      has: (n) => e.has(n),
      hasAny: (n) => n.some((r) => e.has(r)),
      hasAll: (n) => n.every((r) => e.has(r))
    };
  }, [t]);
}
function Pt() {
  return j().capabilities;
}
function Et({ requires: t, requireAll: e = !0, fallback: n = null, children: r }) {
  const i = st(), o = typeof t == "string" ? [t] : t ?? [];
  return o.length === 0 || (e ? i.hasAll(o) : i.hasAny(o)) ? /* @__PURE__ */ f.jsx(f.Fragment, { children: r }) : /* @__PURE__ */ f.jsx(f.Fragment, { children: n });
}
function xt({ children: t, fallback: e = null, loginOptions: n }) {
  const { session: r, login: i } = j(), o = l.useRef(null);
  return l.useEffect(() => {
    if (r.status === "anonymous") {
      const s = at(n), d = o.current;
      if (d?.key === s && d.login === i)
        return;
      const a = { key: s, login: i };
      o.current = a, i(n).catch((p) => {
        o.current === a && (o.current = null), console.error("Auth login failed.", p);
      });
    } else
      o.current = null;
  }, [i, n, r.status]), r.status !== "authenticated" ? /* @__PURE__ */ f.jsx(f.Fragment, { children: e }) : /* @__PURE__ */ f.jsx(f.Fragment, { children: t });
}
function at(t) {
  return `${t?.providerId ?? ""}
${t?.returnUrl ?? ""}`;
}
function Tt(t) {
  return H({
    ...t,
    kind: "external-oidc"
  });
}
function Rt(t, e, n = {}) {
  return {
    requestJson(r, i) {
      return y(t, r, e, n, P(i));
    },
    getJson(r, i) {
      return y(t, r, e, n, P(i));
    },
    postJson(r, i, o) {
      return y(t, r, e, n, {
        ...o,
        method: "POST",
        headers: O(o?.headers),
        body: JSON.stringify(i)
      });
    },
    putJson(r, i, o) {
      return y(t, r, e, n, {
        ...o,
        method: "PUT",
        headers: O(o?.headers),
        body: JSON.stringify(i)
      });
    },
    deleteJson(r, i) {
      return y(t, r, e, n, P({
        ...i,
        method: "DELETE"
      }));
    },
    postForm(r, i, o) {
      return y(t, r, e, n, P({
        ...o,
        method: "POST",
        body: i
      }));
    }
  };
}
const R = /* @__PURE__ */ new Map();
async function y(t, e, n, r, i) {
  const o = r.fetch ?? fetch, s = new URL(e, t).toString(), d = await o(s, await W(n, U(r, i))), a = d.status === 401 && r.refreshOnUnauthorized !== !1 ? await ct(o, s, n, U(r, i)) : d;
  if (!a.ok)
    throw await G(a);
  const p = await a.text();
  if (!p.trim())
    return {};
  try {
    return JSON.parse(p);
  } catch {
    throw new v(a.status, `Expected JSON from ${s}.`);
  }
}
async function ct(t, e, n, r) {
  return await dt(e, n) ? t(e, await W(n, r)) : new Response("Authentication required.", { status: 401 });
}
async function dt(t, e) {
  const n = new URL(t).origin, r = R.get(n);
  if (r)
    return r;
  const i = e.refresh().then((o) => o.status === "authenticated").finally(() => R.delete(n));
  return R.set(n, i), i;
}
async function W(t, e) {
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
function U(t, e) {
  return g(t.defaultHeaders ?? t.headers, e);
}
function ut(t, e = {}) {
  return async () => await t.getAccessToken() ?? await e.fallbackAccessTokenFactory?.() ?? e.anonymousToken ?? "";
}
function Nt(t, e) {
  const n = lt(t.accessTokenFactory) ? t.accessTokenFactory.bind(t) : void 0;
  return {
    ...t,
    accessTokenFactory: ut(e, { fallbackAccessTokenFactory: n })
  };
}
function lt(t) {
  return typeof t == "function";
}
const F = 1e4, c = {
  host: {
    kind: "host",
    id: "studio.host",
    displayName: "Studio host"
  },
  workflowDesigner: {
    kind: "module",
    id: "studio.workflows",
    moduleId: "elsa.studio.workflows",
    displayName: "Workflows module"
  },
  ai: {
    kind: "host",
    id: "studio.ai",
    displayName: "Studio AI host"
  }
};
const k = {
  featureAreas: {
    id: "studio.feature-areas",
    kind: "feature-area",
    owner: c.host,
    contributionName: "StudioFeatureAreaContribution",
    description: "Top-level module-owned feature areas that expand into navigation and routes."
  },
  navigation: {
    id: "studio.navigation",
    kind: "navigation",
    owner: c.host,
    contributionName: "StudioNavigationContribution",
    description: "Shell navigation entries shown in the Studio workbench."
  },
  routes: {
    id: "studio.routes",
    kind: "route",
    owner: c.host,
    contributionName: "StudioRouteContribution",
    description: "Routable Studio pages and feature-area routes."
  },
  dashboardWidgets: {
    id: "studio.dashboard.widgets",
    kind: "dashboard-widget",
    owner: c.host,
    contributionName: "StudioDashboardWidgetContribution",
    description: "Dashboard workspace summary widgets contributed by modules."
  },
  panels: {
    id: "studio.shell.panels",
    kind: "panel",
    owner: c.host,
    contributionName: "StudioPanelContribution",
    description: "Global shell panels such as assistant or utility panels."
  },
  toolbarActions: {
    id: "studio.shell.toolbar-actions",
    kind: "toolbar-action",
    owner: c.host,
    contributionName: "unknown",
    description: "Global shell toolbar actions."
  },
  activityEditors: {
    id: "studio.activity.editors",
    kind: "activity-editor",
    owner: c.workflowDesigner,
    contributionName: "unknown",
    description: "Activity editing surfaces owned by the workflow designer."
  },
  propertyEditors: {
    id: "workflow.activity.property-editors",
    kind: "property-editor",
    owner: c.workflowDesigner,
    contributionName: "StudioActivityPropertyEditorContribution",
    description: "Activity property editors selected by descriptor and context."
  },
  expressionEditors: {
    id: "workflow.expression.editors",
    kind: "expression-editor",
    owner: c.workflowDesigner,
    contributionName: "StudioExpressionEditorContribution",
    description: "Expression editors selected by expression type and surface."
  },
  settingEditors: {
    id: "studio.settings.editors",
    kind: "setting-editor",
    owner: c.host,
    contributionName: "StudioSettingEditorContribution",
    description: "Host setting editors selected by setting descriptor."
  },
  agentContextProviders: {
    id: "studio.agent.context-providers",
    kind: "agent-context-provider",
    owner: c.ai,
    contributionName: "StudioAgentContextProviderContribution",
    description: "Provider-neutral context collectors for Weaver and agent surfaces."
  },
  agentPromptStarters: {
    id: "studio.agent.prompt-starters",
    kind: "agent-prompt-starter",
    owner: c.ai,
    contributionName: "StudioAgentPromptStarterContribution",
    description: "Route-aware prompt starters."
  },
  agentCapabilities: {
    id: "studio.agent.capabilities",
    kind: "agent-capability",
    owner: c.ai,
    contributionName: "StudioAgentCapabilityContribution",
    description: "Agent capability declarations with risk and permission metadata."
  },
  agentActions: {
    id: "studio.agent.actions",
    kind: "agent-action",
    owner: c.ai,
    contributionName: "StudioAgentActionContribution",
    description: "Reviewable agent actions that produce backend-owned proposals."
  },
  aiContextProviders: {
    id: "studio.ai.context-providers",
    kind: "ai-context-provider",
    owner: c.ai,
    contributionName: "StudioAiContextProviderContribution",
    description: "Legacy AI context attachment providers."
  },
  aiPromptActions: {
    id: "studio.ai.prompt-actions",
    kind: "ai-prompt-action",
    owner: c.ai,
    contributionName: "StudioAiPromptActionContribution",
    description: "Prompt-producing AI actions."
  },
  aiTools: {
    id: "studio.ai.tools",
    kind: "ai-tool",
    owner: c.ai,
    contributionName: "StudioAiToolContribution",
    description: "Tool declarations exposed through backend-governed agent providers."
  },
  aiProposalRenderers: {
    id: "studio.ai.proposal-renderers",
    kind: "ai-proposal-renderer",
    owner: c.ai,
    contributionName: "StudioAiProposalRendererContribution",
    description: "Renderers for proposal summaries and review shells."
  },
  aiSurfaces: {
    id: "studio.ai.surfaces",
    kind: "ai-surface",
    owner: c.ai,
    contributionName: "StudioAiSurfaceContribution",
    description: "AI surfaces such as routes, panels, drawers, and inline placements."
  },
  workflowDesignerNodeRenderers: {
    id: "workflow.designer.node-renderers",
    kind: "workflow-designer-node-renderer",
    owner: c.workflowDesigner,
    contributionName: "unknown",
    description: "Workflow designer node renderer contributions."
  },
  workflowDesignerToolboxItems: {
    id: "workflow.designer.toolbox-items",
    kind: "workflow-designer-toolbox-item",
    owner: c.workflowDesigner,
    contributionName: "unknown",
    description: "Workflow designer toolbox item contributions."
  },
  workflowDesignerPanels: {
    id: "workflow.designer.panels",
    kind: "workflow-designer-panel",
    owner: c.workflowDesigner,
    contributionName: "StudioWorkflowDesignerPanelContribution",
    description: "Workflow designer side-panel tabs and panel surfaces."
  },
  diagnostics: {
    id: "studio.diagnostics",
    kind: "diagnostic",
    owner: c.host,
    contributionName: "StudioModuleDiagnostic",
    description: "Module diagnostics surfaced by the host registry.",
    unavailableContributions: "show-disabled"
  }
};
function m(t) {
  const e = [];
  return {
    slot: t,
    add(n) {
      e.push(n);
    },
    list() {
      return [...e];
    }
  };
}
function jt() {
  const t = /* @__PURE__ */ new Set();
  return {
    contextProviders: m(k.aiContextProviders),
    promptActions: m(k.aiPromptActions),
    tools: m(k.aiTools),
    proposalRenderers: m(k.aiProposalRenderers),
    surfaces: m(k.aiSurfaces),
    dispatchPrompt(e) {
      for (const n of t)
        n(e);
    },
    onPrompt(e) {
      return t.add(e), () => t.delete(e);
    }
  };
}
function $t(t, e = {}) {
  return {
    baseUrl: t,
    headers: e.headers,
    http: ht(t, e.headers)
  };
}
function ht(t, e) {
  return {
    requestJson(n, r) {
      return b(t, n, g(e, E(r)));
    },
    async getJson(n, r) {
      return b(t, n, g(e, E(r)));
    },
    async postJson(n, r, i) {
      return b(t, n, g(e, {
        ...i,
        method: "POST",
        headers: q(i?.headers),
        body: JSON.stringify(r)
      }));
    },
    async putJson(n, r, i) {
      return b(t, n, g(e, {
        ...i,
        method: "PUT",
        headers: q(i?.headers),
        body: JSON.stringify(r)
      }));
    },
    async deleteJson(n, r) {
      return b(t, n, g(e, E({
        ...r,
        method: "DELETE"
      })));
    },
    async postForm(n, r, i) {
      return b(t, n, g(e, E({
        ...i,
        method: "POST",
        body: r
      })));
    }
  };
}
function g(t, e = {}) {
  return t ? {
    ...e,
    headers: pt(t, e.headers)
  } : e;
}
function pt(t, e) {
  const n = new Headers(t);
  return new Headers(e).forEach((r, i) => n.set(i, r)), n;
}
async function b(t, e, n) {
  const r = At(t, e), i = new AbortController(), o = globalThis.setTimeout(() => i.abort(), F);
  let s;
  try {
    s = await fetch(r, {
      ...n,
      signal: ft(n?.signal, i.signal)
    });
  } catch (a) {
    throw i.signal.aborted && !n?.signal?.aborted ? new Error(`Request to ${r} timed out after ${F / 1e3} seconds. Check Studio:BackendBaseUrl and make sure the backend API is responding.`) : a;
  } finally {
    globalThis.clearTimeout(o);
  }
  if (!s.ok)
    throw await G(s);
  const d = await s.text();
  if (!d.trim())
    return {};
  try {
    return JSON.parse(d);
  } catch {
    throw new v(
      s.status,
      `Expected JSON from ${r}, but received ${kt(s, d)}. Check Studio:BackendBaseUrl and make sure the backend maps this API route.`
    );
  }
}
function ft(t, e) {
  if (!t)
    return e;
  if (typeof AbortSignal.any == "function")
    return AbortSignal.any([t, e]);
  const n = new AbortController(), r = () => n.abort();
  return t.aborted || e.aborted ? n.abort() : (t.addEventListener("abort", r, { once: !0 }), e.addEventListener("abort", r, { once: !0 })), n.signal;
}
async function wt(t) {
  return (await _(t)).message;
}
async function G(t) {
  const e = await _(t);
  return new v(t.status, e.message, e.validationErrors, e.payload);
}
async function _(t) {
  const e = t.headers.get("content-type") ?? "";
  if (gt(e))
    try {
      const r = await t.json(), i = K(r);
      return {
        message: yt(r) ?? bt(i) ?? `Request failed with ${t.status}.`,
        validationErrors: i,
        payload: r
      };
    } catch {
      return { message: `Request failed with ${t.status}.`, validationErrors: null, payload: null };
    }
  return { message: (await t.text()).trim() || `Request failed with ${t.status}.`, validationErrors: null, payload: null };
}
function gt(t) {
  return t.toLowerCase().includes("json");
}
async function It(t) {
  if (t instanceof v)
    return t.message;
  if (V(t))
    try {
      return await wt(t.response.clone());
    } catch {
      return t.response.statusText || "Request failed.";
    }
  return t instanceof Error ? t.message : "Unknown error.";
}
async function Lt(t) {
  if (t instanceof v)
    return t.validationErrors;
  if (!V(t))
    return null;
  try {
    const e = await t.response.clone().json();
    return K(e);
  } catch {
    return null;
  }
}
function yt(t) {
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
function K(t) {
  const e = t.errors;
  if (!e || typeof e != "object" || Array.isArray(e))
    return null;
  const n = {};
  for (const [r, i] of Object.entries(e)) {
    const o = Array.isArray(i) ? i.map(String) : [String(i)];
    o.length > 0 && (n[r] = o);
  }
  return Object.keys(n).length > 0 ? n : null;
}
function bt(t) {
  return t ? Object.values(t).flat().join(" ") : null;
}
function V(t) {
  return typeof t == "object" && t !== null && "response" in t && t.response instanceof Response;
}
function At(t, e) {
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
function q(t) {
  const e = new Headers(t);
  return e.has("Content-Type") || e.set("Content-Type", "application/json"), e.has("Accept") || e.set("Accept", "application/json"), e;
}
function kt(t, e) {
  const n = t.headers.get("content-type") ?? "an unknown content type", r = e.trim(), i = r.length > 0 ? `: ${r.slice(0, 80)}` : "";
  return `${n}${i}`;
}
class v extends Error {
  constructor(e, n, r = null, i = null) {
    super(n), this.status = e, this.validationErrors = r, this.payload = i, this.name = "StudioHttpError";
  }
  status;
  validationErrors;
  payload;
}
export {
  A as AuthAdapterError,
  S as AuthConfigurationError,
  Et as AuthGuard,
  Ct as AuthProvider,
  xt as RequireAuth,
  v as StudioHttpError,
  jt as createAiContributionApi,
  X as createAuthProviderManager,
  Rt as createAuthenticatedHttpClient,
  St as createBackendAuthProviderManager,
  m as createContributionRegistry,
  $t as createEndpointContext,
  ht as createHttpClient,
  Tt as createOidcAuthAdapter,
  H as createRedirectAuthAdapter,
  ut as createSignalRAccessTokenFactory,
  G as createStudioHttpError,
  It as describeApiError,
  wt as readStudioHttpErrorMessage,
  c as studioSlotOwners,
  k as studioSlots,
  Lt as tryExtractValidationErrors,
  Pt as useAuthCapabilities,
  j as useAuthContext,
  ot as useAuthSession,
  st as usePermissions,
  Nt as withAuthenticatedSignalROptions,
  g as withDefaultHeaders
};
