import { forwardRef as G, createElement as _, useState as h, useMemo as S, useCallback as ie, useEffect as q } from "react";
var L = { exports: {} }, C = {};
var F;
function ce() {
  if (F) return C;
  F = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function r(n, o, i) {
    var l = null;
    if (i !== void 0 && (l = "" + i), o.key !== void 0 && (l = "" + o.key), "key" in o) {
      i = {};
      for (var v in o)
        v !== "key" && (i[v] = o[v]);
    } else i = o;
    return o = i.ref, {
      $$typeof: e,
      type: n,
      key: l,
      ref: o !== void 0 ? o : null,
      props: i
    };
  }
  return C.Fragment = t, C.jsx = r, C.jsxs = r, C;
}
var H;
function de() {
  return H || (H = 1, L.exports = ce()), L.exports;
}
var s = de();
const le = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), X = (...e) => e.filter((t, r, n) => !!t && t.trim() !== "" && n.indexOf(t) === r).join(" ").trim();
var ue = {
  xmlns: "http://www.w3.org/2000/svg",
  width: 24,
  height: 24,
  viewBox: "0 0 24 24",
  fill: "none",
  stroke: "currentColor",
  strokeWidth: 2,
  strokeLinecap: "round",
  strokeLinejoin: "round"
};
const pe = G(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: r = 2,
    absoluteStrokeWidth: n,
    className: o = "",
    children: i,
    iconNode: l,
    ...v
  }, x) => _(
    "svg",
    {
      ref: x,
      ...ue,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: n ? Number(r) * 24 / Number(t) : r,
      className: X("lucide", o),
      ...v
    },
    [
      ...l.map(([m, u]) => _(m, u)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const $ = (e, t) => {
  const r = G(
    ({ className: n, ...o }, i) => _(pe, {
      ref: i,
      iconNode: t,
      className: X(`lucide-${le(e)}`, n),
      ...o
    })
  );
  return r.displayName = `${e}`, r;
};
const fe = $("Bot", [
  ["path", { d: "M12 8V4H8", key: "hb8ula" }],
  ["rect", { width: "16", height: "12", x: "4", y: "8", rx: "2", key: "enze0r" }],
  ["path", { d: "M2 14h2", key: "vft8re" }],
  ["path", { d: "M20 14h2", key: "4cs60a" }],
  ["path", { d: "M15 13v2", key: "1xurst" }],
  ["path", { d: "M9 13v2", key: "rq6x2g" }]
]);
const ve = $("MessageSquare", [
  ["path", { d: "M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z", key: "1lielz" }]
]);
const he = $("RefreshCcw", [
  ["path", { d: "M21 12a9 9 0 0 0-9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "14sxne" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }],
  ["path", { d: "M3 12a9 9 0 0 0 9 9 9.75 9.75 0 0 0 6.74-2.74L21 16", key: "1hlbsb" }],
  ["path", { d: "M16 16h5v5", key: "ccwih5" }]
]);
const me = $("Send", [
  [
    "path",
    {
      d: "M14.536 21.686a.5.5 0 0 0 .937-.024l6.5-19a.496.496 0 0 0-.635-.635l-19 6.5a.5.5 0 0 0-.024.937l7.93 3.18a2 2 0 0 1 1.112 1.11z",
      key: "1ffxy3"
    }
  ],
  ["path", { d: "m21.854 2.147-10.94 10.939", key: "12cjpa" }]
]);
async function we(e) {
  const t = N(await e.http.getJson("/_elsa/agent/bootstrap"));
  return ke(t);
}
async function ge(e, t) {
  const r = N(await e.http.getJson("/_elsa/agent/bootstrap"));
  return ye(r, t);
}
async function xe(e, t, r) {
  const n = N(await e.http.getJson("/_elsa/agent/bootstrap")), o = t.agent || n.providers.find((b) => b.isAvailable)?.providerId, i = t.conversationId || await je(e, o);
  await e.http.postJson(`/_elsa/agent/sessions/${encodeURIComponent(i)}/messages`, {
    role: "user",
    content: t.message,
    message: t.message,
    mode: "explain",
    contextAttachments: (t.attachments ?? []).map(Ie)
  });
  const l = await fetch(new URL(`/_elsa/agent/sessions/${encodeURIComponent(i)}/stream`, e.baseUrl).toString(), be(e.headers, {
    headers: { Accept: "text/event-stream" }
  }));
  if (!l.ok)
    throw new Error(await l.text() || `Weaver chat failed with ${l.status}.`);
  if (!l.body)
    return;
  const v = l.body.getReader(), x = new TextDecoder();
  let m = "";
  for (; ; ) {
    const { value: b, done: I } = await v.read();
    if (I) break;
    m += x.decode(b, { stream: !0 });
    const k = m.split(`

`);
    m = k.pop() ?? "";
    for (const w of k) {
      const y = Q(w);
      y && r({ ...y, conversationId: y.conversationId || i });
    }
  }
  const u = Q(m);
  u && r({ ...u, conversationId: u.conversationId || i });
}
function Q(e) {
  const t = e.split(`
`).map((r) => r.trim()).find((r) => r.startsWith("data:"));
  return t ? Ae(JSON.parse(t.slice(5).trim())) : null;
}
function be(e, t = {}) {
  if (!e) return t;
  const r = new Headers(e);
  return new Headers(t.headers).forEach((n, o) => r.set(o, n)), { ...t, headers: r };
}
function N(e) {
  if (e.error) throw new Error(e.error.message || e.error.code || "Weaver request failed.");
  if (e.data === void 0 || e.data === null) throw new Error("Weaver backend returned an empty response.");
  return e.data;
}
function ke(e) {
  const r = (e.providers ?? []).filter((n) => n.isAvailable);
  return {
    streaming: e.enabled !== !1 && e.providerStatus !== "unavailable" && r.length > 0,
    conversationPersistence: !0,
    proposalReview: e.policy?.requiresApprovalForMutations ?? (e.capabilities ?? []).some((n) => n.requiresApproval || n.risk === "review-required"),
    supportedAttachmentKinds: ["workflow.definition", "workflow.instance", "workflow.execution", "workflow.diagnostics", "studio.context"],
    agents: r.map((n) => ({
      name: n.providerId,
      displayName: n.providerId,
      description: n.status ?? "Weaver provider"
    }))
  };
}
function ye(e, t) {
  return (e.providers ?? []).filter((n) => n.isAvailable && (!t || n.providerId === t)).flatMap((n) => (n.supportedOperations ?? []).map((o) => ({
    name: `${n.providerId}.${o}`,
    displayName: T(o),
    description: `${T(o)} support exposed by ${n.providerId}.`,
    mutability: o === "chat" || o === "streaming" ? "read-only" : "proposal",
    dangerLevel: n.riskProfile === "privileged-execution" ? "high" : "low",
    permissions: [],
    agentScopes: [n.providerId],
    isEnabled: !0
  })));
}
async function je(e, t) {
  const r = N(await e.http.postJson("/_elsa/agent/sessions", {
    conversationId: "weaver-chat",
    ...t ? { providerId: t } : {},
    mode: "explain",
    activeSurface: { route: "/weaver" },
    clientContext: { studioVersion: "studio", sdkVersion: "studio", moduleIds: ["Elsa.Studio.Weaver.Chat"] },
    metadata: { route: "/weaver", source: "weaver-chat" }
  })), n = r.sessionId ?? r.id;
  if (!n) throw new Error("Weaver backend did not return an agent session.");
  return n;
}
function Ie(e) {
  const t = Se(e.kind), r = Ce(e, t), n = r.sourceId || e.referenceId || "";
  return {
    id: e.id ?? `${t}:${n || e.scope || "context"}`,
    kind: t,
    displayName: T(t),
    sensitivity: "public",
    summary: n || e.scope || t,
    references: r,
    metadata: e.metadata ?? {}
  };
}
function Se(e) {
  const t = e.trim().toLowerCase();
  return {
    "workflow-definition": "workflow.definition",
    "workflow-create-draft": "workflow.definition",
    "workflow-executable": "workflow.execution",
    "workflow-instance": "workflow.instance",
    "workflow-diagnostics": "workflow.diagnostics",
    "studio-context": "studio.context"
  }[t] ?? t;
}
function Ce(e, t) {
  const r = e.metadata ?? {}, n = {
    source: t.startsWith("workflow.") ? "workflow" : t,
    sourceId: e.referenceId ?? f(r, "artifactId") ?? f(r, "definitionId") ?? f(r, "id") ?? "",
    scope: e.scope ?? ""
  };
  return A(n, "workflowDefinitionId", f(r, "workflowDefinitionId") ?? f(r, "definitionId")), A(n, "workflowVersionId", f(r, "workflowVersionId") ?? f(r, "definitionVersionId") ?? f(r, "sourceId")), A(n, "artifactId", f(r, "artifactId")), A(n, "workflowExecutionId", f(r, "workflowExecutionId") ?? f(r, "instanceId")), A(n, "selectedActivityId", e.activityId ?? f(r, "selectedActivityId")), n;
}
function A(e, t, r) {
  r?.trim() && (e[t] = r);
}
function f(e, t) {
  const r = e[t];
  return typeof r == "string" && r.trim() ? r : void 0;
}
function Ae(e) {
  if (!e || typeof e != "object") return null;
  const t = e, r = R(t, "type") ?? R(t, "kind"), n = Re(r), o = Y(t, "error"), i = Y(t, "data"), l = j(t, "content") ?? j(i, "content") ?? j(o, "message") ?? "";
  return n === "assistant.delta" ? z("assistant.delta", t, { content: l }) : n === "conversation.error" ? z("conversation.error", t, { content: l || "Weaver is unavailable for this request." }) : n === "conversation.completed" ? z("conversation.completed", t, {}) : n === "conversation.started" ? null : e;
}
function z(e, t, r) {
  return {
    type: e,
    conversationId: j(t, "conversationId") ?? "",
    sequence: Number(R(t, "sequence") ?? 0),
    timestamp: j(t, "timestamp") ?? j(t, "createdAt") ?? (/* @__PURE__ */ new Date()).toISOString(),
    data: r
  };
}
function Re(e) {
  if (typeof e == "number")
    return ["conversation.started", "assistant.delta", "tool.approval", "proposal.created", "conversation.completed", "conversation.error"][e] ?? "";
  const t = String(e ?? "").replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
  return t === "message-delta" ? "assistant.delta" : t === "completed" ? "conversation.completed" : t === "error" ? "conversation.error" : String(e ?? "");
}
function T(e) {
  return e.replace(/[-_.]+/g, " ").replace(/\b\w/g, (t) => t.toUpperCase());
}
function j(e, t) {
  const r = R(e, t);
  return typeof r == "string" ? r : void 0;
}
function Y(e, t) {
  const r = R(e, t);
  return r && typeof r == "object" ? r : void 0;
}
function R(e, t) {
  if (!e) return;
  if (t in e) return e[t];
  const r = t.toLowerCase(), n = Object.keys(e).find((o) => o.toLowerCase() === r);
  return n ? e[n] : void 0;
}
const E = /* @__PURE__ */ new Set(), J = [];
let We = 0;
function qe(e) {
  e.navigation.add({
    id: "weaver",
    label: "Weaver",
    path: "/weaver",
    order: 30,
    iconColor: "#2563eb"
  }), e.routes.add({
    id: "weaver-chat",
    path: "/weaver",
    label: "Weaver",
    component: () => /* @__PURE__ */ s.jsx(Z, { api: e, variant: "route" })
  }), e.panels.add({
    id: "weaver-chat",
    title: "Weaver",
    order: 40,
    component: () => /* @__PURE__ */ s.jsx(Z, { api: e, variant: "panel" })
  }), e.ai.surfaces.add({
    id: "weaver-chat",
    title: "Weaver chat",
    placement: "panel",
    moduleId: "Elsa.Studio.Weaver.Chat"
  }), e.ai.onPrompt((t) => Ne(t));
}
function Z({ api: e, variant: t }) {
  const [r, n] = h("loading"), [o, i] = h(null), [l, v] = h([]), [x, m] = h([]), [u, b] = h([]), [I, k] = h(""), [w, y] = h(""), [K, ee] = h(null), [g, V] = h("idle"), [O, W] = h(""), te = S(() => e.ai.promptActions.list(), [e]), re = S(() => e.ai.contextProviders.list(), [e]), ne = S(() => e.ai.proposalRenderers.list(), [e]), se = S(() => e.ai.surfaces.list(), [e]), ae = S(
    () => u.flatMap((a) => a.attachments ?? []).slice(0, 8),
    [u]
  ), M = ie(async () => {
    n("loading"), W("");
    try {
      const a = await we(e.backend);
      i(a), y((d) => d || a.agents[0]?.name || ""), n(a.streaming ? "ready" : "unavailable"), a.streaming && v(await ge(e.backend, w || a.agents[0]?.name));
    } catch (a) {
      i(null), v([]), n("unavailable"), W(a instanceof Error ? a.message : String(a));
    }
  }, [e.backend, w]);
  q(() => {
    M();
  }, [M]), q(() => {
    const a = (d) => {
      b((p) => [...p, d]), d.mode === "steer" && k(d.message);
    };
    if (E.add(a), J.length > 0) {
      const d = J.splice(0);
      for (const p of d)
        a(p);
    }
    return () => {
      E.delete(a);
    };
  }, []), q(() => {
    if (r !== "ready" || g !== "idle") return;
    const a = u.find((d) => d.mode !== "steer");
    a && B(a);
  }, [r, g, u]);
  async function U(a) {
    const d = a.message.trim();
    if (!d || r !== "ready" || g === "streaming") return;
    const p = D("assistant");
    V("streaming"), W(""), k(""), m((c) => [
      ...c,
      { id: D("user"), role: "user", content: d },
      { id: p, role: "assistant", content: "" }
    ]);
    try {
      await xe(e.backend, {
        conversationId: K,
        message: d,
        agent: a.agent ?? w,
        attachments: a.attachments
      }, (c) => {
        c.conversationId && ee(c.conversationId), (c.type === "assistant.delta" && typeof c.data.content == "string" || c.type === "conversation.error" && typeof c.data.content == "string") && P(p, c.data.content);
      });
    } catch (c) {
      W(c instanceof Error ? c.message : String(c)), P(p, "Weaver is unavailable for this request.");
    } finally {
      V("idle");
    }
  }
  function P(a, d) {
    m((p) => p.map((c) => c.id === a ? { ...c, content: `${c.content}${d}` } : c));
  }
  function B(a) {
    b((d) => d.filter((p) => p.id !== a.id)), U(a);
  }
  function oe() {
    U({ message: I, agent: w || null, mode: "enqueue" });
  }
  return /* @__PURE__ */ s.jsxs("section", { className: t === "panel" ? "weaver-surface panel" : "weaver-surface", children: [
    /* @__PURE__ */ s.jsxs("div", { className: "weaver-header", children: [
      /* @__PURE__ */ s.jsxs("div", { children: [
        /* @__PURE__ */ s.jsx("h2", { children: "Weaver" }),
        /* @__PURE__ */ s.jsx("p", { children: Me(r, o) })
      ] }),
      /* @__PURE__ */ s.jsx("button", { type: "button", className: "weaver-icon-button", "aria-label": "Refresh Weaver capabilities", title: "Refresh", onClick: () => {
        M();
      }, children: /* @__PURE__ */ s.jsx(he, { size: 15 }) })
    ] }),
    O ? /* @__PURE__ */ s.jsx("div", { className: "weaver-alert", children: O }) : null,
    /* @__PURE__ */ s.jsxs("div", { className: "weaver-layout", children: [
      /* @__PURE__ */ s.jsxs("aside", { className: "weaver-sidebar", children: [
        /* @__PURE__ */ s.jsxs("label", { className: "weaver-field", children: [
          /* @__PURE__ */ s.jsx("span", { children: "Agent" }),
          /* @__PURE__ */ s.jsx("select", { value: w, onChange: (a) => y(a.target.value), disabled: r !== "ready", children: o?.agents.length ? o.agents.map((a) => /* @__PURE__ */ s.jsx("option", { value: a.name, children: a.displayName || a.name }, a.name)) : /* @__PURE__ */ s.jsx("option", { value: "", children: "No agent" }) })
        ] }),
        /* @__PURE__ */ s.jsx(Ee, { attachments: ae }),
        /* @__PURE__ */ s.jsx(
          $e,
          {
            promptActions: te.length,
            contextProviders: re.length,
            proposalRenderers: ne.length,
            surfaces: se.length,
            tools: l.length
          }
        )
      ] }),
      /* @__PURE__ */ s.jsxs("main", { className: "weaver-chat", children: [
        /* @__PURE__ */ s.jsx("div", { className: "weaver-messages", "aria-live": "polite", children: x.length === 0 ? /* @__PURE__ */ s.jsxs("div", { className: "weaver-empty", children: [
          /* @__PURE__ */ s.jsx(fe, { size: 24 }),
          /* @__PURE__ */ s.jsx("strong", { children: "Ask Weaver about the current Studio context." }),
          /* @__PURE__ */ s.jsx("span", { children: "Workflow modules and third-party modules can attach resources, tools, and proposals when their Weaver features are enabled." })
        ] }) : x.map((a) => /* @__PURE__ */ s.jsxs("article", { className: `weaver-message ${a.role}`, children: [
          /* @__PURE__ */ s.jsx("span", { children: a.role === "user" ? "You" : "Weaver" }),
          /* @__PURE__ */ s.jsx("p", { children: a.content || (g === "streaming" && a.role === "assistant" ? "Thinking..." : "") })
        ] }, a.id)) }),
        u.length > 0 ? /* @__PURE__ */ s.jsx("div", { className: "weaver-queue", "aria-label": "Queued Weaver prompts", children: u.map((a) => /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => B(a), disabled: g === "streaming" || r !== "ready", children: [
          /* @__PURE__ */ s.jsx(ve, { size: 13 }),
          /* @__PURE__ */ s.jsx("span", { children: a.source?.label ?? a.message })
        ] }, a.id)) }) : null,
        /* @__PURE__ */ s.jsxs("div", { className: "weaver-composer", children: [
          /* @__PURE__ */ s.jsx(
            "textarea",
            {
              "aria-label": "Prompt Weaver",
              placeholder: r === "ready" ? "Ask Weaver..." : "Weaver backend is unavailable",
              value: I,
              onChange: (a) => k(a.target.value),
              disabled: r !== "ready",
              rows: t === "panel" ? 2 : 3
            }
          ),
          /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: oe, disabled: !I.trim() || r !== "ready" || g === "streaming", children: [
            /* @__PURE__ */ s.jsx(me, { size: 15 }),
            g === "streaming" ? "Streaming" : "Send"
          ] })
        ] })
      ] })
    ] })
  ] });
}
function Ee({ attachments: e }) {
  return /* @__PURE__ */ s.jsxs("section", { className: "weaver-context-stack", children: [
    /* @__PURE__ */ s.jsx("h3", { children: "Context stack" }),
    e.length === 0 ? /* @__PURE__ */ s.jsx("p", { children: "No context attached." }) : e.map((t, r) => /* @__PURE__ */ s.jsxs("div", { className: "weaver-context-item", children: [
      /* @__PURE__ */ s.jsx("strong", { children: t.kind }),
      /* @__PURE__ */ s.jsx("span", { children: t.referenceId ?? t.scope ?? "current selection" })
    ] }, `${t.kind}-${t.referenceId ?? r}`))
  ] });
}
function $e({ promptActions: e, contextProviders: t, proposalRenderers: r, surfaces: n, tools: o }) {
  return /* @__PURE__ */ s.jsxs("section", { className: "weaver-contributions", children: [
    /* @__PURE__ */ s.jsx("h3", { children: "Enabled contributions" }),
    /* @__PURE__ */ s.jsxs("span", { children: [
      e,
      " prompt actions"
    ] }),
    /* @__PURE__ */ s.jsxs("span", { children: [
      t,
      " context providers"
    ] }),
    /* @__PURE__ */ s.jsxs("span", { children: [
      r,
      " proposal renderers"
    ] }),
    /* @__PURE__ */ s.jsxs("span", { children: [
      n,
      " surfaces"
    ] }),
    /* @__PURE__ */ s.jsxs("span", { children: [
      o,
      " backend tools"
    ] })
  ] });
}
function Ne(e) {
  const t = { ...e, id: D("prompt") };
  if (E.size === 0) {
    J.push(t);
    return;
  }
  for (const r of E)
    r(t);
}
function Me(e, t) {
  if (e === "loading") return "Checking backend AI capabilities.";
  if (e === "unavailable") return "Enable Elsa Server AI capabilities to use Weaver chat.";
  const r = t?.conversationPersistence ? "durable sessions" : "ephemeral sessions", n = t?.proposalReview ? "proposal review" : "draft-only suggestions";
  return `${t?.agents.length ?? 0} agent(s), ${r}, ${n}.`;
}
function D(e) {
  return `${e}-${Date.now()}-${++We}`;
}
export {
  qe as register
};
