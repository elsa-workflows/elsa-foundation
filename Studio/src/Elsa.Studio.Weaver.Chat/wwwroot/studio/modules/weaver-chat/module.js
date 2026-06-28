import { forwardRef as ne, createElement as J, useState as m, useRef as Z, useMemo as S, useCallback as pe, useEffect as E } from "react";
var z = { exports: {} }, C = {};
var G;
function fe() {
  if (G) return C;
  G = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function n(s, o, i) {
    var u = null;
    if (i !== void 0 && (u = "" + i), o.key !== void 0 && (u = "" + o.key), "key" in o) {
      i = {};
      for (var v in o)
        v !== "key" && (i[v] = o[v]);
    } else i = o;
    return o = i.ref, {
      $$typeof: e,
      type: s,
      key: u,
      ref: o !== void 0 ? o : null,
      props: i
    };
  }
  return C.Fragment = t, C.jsx = n, C.jsxs = n, C;
}
var X;
function ve() {
  return X || (X = 1, z.exports = fe()), z.exports;
}
var a = ve();
const he = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), re = (...e) => e.filter((t, n, s) => !!t && t.trim() !== "" && s.indexOf(t) === n).join(" ").trim();
var me = {
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
const we = ne(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: s,
    className: o = "",
    children: i,
    iconNode: u,
    ...v
  }, x) => J(
    "svg",
    {
      ref: x,
      ...me,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: s ? Number(n) * 24 / Number(t) : n,
      className: re("lucide", o),
      ...v
    },
    [
      ...u.map(([h, p]) => J(h, p)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const M = (e, t) => {
  const n = ne(
    ({ className: s, ...o }, i) => J(we, {
      ref: i,
      iconNode: t,
      className: re(`lucide-${he(e)}`, s),
      ...o
    })
  );
  return n.displayName = `${e}`, n;
};
const ge = M("Bot", [
  ["path", { d: "M12 8V4H8", key: "hb8ula" }],
  ["rect", { width: "16", height: "12", x: "4", y: "8", rx: "2", key: "enze0r" }],
  ["path", { d: "M2 14h2", key: "vft8re" }],
  ["path", { d: "M20 14h2", key: "4cs60a" }],
  ["path", { d: "M15 13v2", key: "1xurst" }],
  ["path", { d: "M9 13v2", key: "rq6x2g" }]
]);
const xe = M("MessageSquare", [
  ["path", { d: "M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z", key: "1lielz" }]
]);
const be = M("RefreshCcw", [
  ["path", { d: "M21 12a9 9 0 0 0-9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "14sxne" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }],
  ["path", { d: "M3 12a9 9 0 0 0 9 9 9.75 9.75 0 0 0 6.74-2.74L21 16", key: "1hlbsb" }],
  ["path", { d: "M16 16h5v5", key: "ccwih5" }]
]);
const ke = M("Send", [
  [
    "path",
    {
      d: "M14.536 21.686a.5.5 0 0 0 .937-.024l6.5-19a.496.496 0 0 0-.635-.635l-19 6.5a.5.5 0 0 0-.024.937l7.93 3.18a2 2 0 0 1 1.112 1.11z",
      key: "1ffxy3"
    }
  ],
  ["path", { d: "m21.854 2.147-10.94 10.939", key: "12cjpa" }]
]);
async function ye(e) {
  const t = P(await e.http.getJson("/_elsa/agent/bootstrap"));
  return Ce(t);
}
async function je(e, t) {
  const n = P(await e.http.getJson("/_elsa/agent/bootstrap"));
  return Ae(n, t);
}
async function Ie(e, t, n) {
  const s = P(await e.http.getJson("/_elsa/agent/bootstrap")), o = t.agent || s.providers.find((b) => b.isAvailable)?.providerId, i = t.conversationId || await Re(e, o);
  await e.http.postJson(`/_elsa/agent/sessions/${encodeURIComponent(i)}/messages`, {
    role: "user",
    content: t.message,
    message: t.message,
    mode: "explain",
    contextAttachments: (t.attachments ?? []).map(We)
  });
  const u = await fetch(new URL(`/_elsa/agent/sessions/${encodeURIComponent(i)}/stream`, e.baseUrl).toString(), Se(e.headers, {
    headers: { Accept: "text/event-stream" }
  }));
  if (!u.ok)
    throw new Error(await u.text() || `Weaver chat failed with ${u.status}.`);
  if (!u.body)
    return;
  const v = u.body.getReader(), x = new TextDecoder();
  let h = "";
  for (; ; ) {
    const { value: b, done: I } = await v.read();
    if (I) break;
    h += x.decode(b, { stream: !0 });
    const k = h.split(`

`);
    h = k.pop() ?? "";
    for (const w of k) {
      const y = K(w);
      y && n({ ...y, conversationId: y.conversationId || i });
    }
  }
  const p = K(h);
  p && n({ ...p, conversationId: p.conversationId || i });
}
function K(e) {
  const t = e.split(`
`).map((n) => n.trim()).find((n) => n.startsWith("data:"));
  return t ? Ne(JSON.parse(t.slice(5).trim())) : null;
}
function Se(e, t = {}) {
  if (!e) return t;
  const n = new Headers(e);
  return new Headers(t.headers).forEach((s, o) => n.set(o, s)), { ...t, headers: n };
}
function P(e) {
  if (e.error) throw new Error(e.error.message || e.error.code || "Weaver request failed.");
  if (e.data === void 0 || e.data === null) throw new Error("Weaver backend returned an empty response.");
  return e.data;
}
function Ce(e) {
  const n = (e.providers ?? []).filter((s) => s.isAvailable);
  return {
    streaming: e.enabled !== !1 && e.providerStatus !== "unavailable" && n.length > 0,
    conversationPersistence: !0,
    proposalReview: e.policy?.requiresApprovalForMutations ?? (e.capabilities ?? []).some((s) => s.requiresApproval || s.risk === "review-required"),
    supportedAttachmentKinds: ["workflow.definition", "workflow.instance", "workflow.execution", "workflow.diagnostics", "studio.context"],
    agents: n.map((s) => ({
      name: s.providerId,
      displayName: s.providerId,
      description: s.status ?? "Weaver provider"
    }))
  };
}
function Ae(e, t) {
  return (e.providers ?? []).filter((s) => s.isAvailable && (!t || s.providerId === t)).flatMap((s) => (s.supportedOperations ?? []).map((o) => ({
    name: `${s.providerId}.${o}`,
    displayName: D(o),
    description: `${D(o)} support exposed by ${s.providerId}.`,
    mutability: o === "chat" || o === "streaming" ? "read-only" : "proposal",
    dangerLevel: s.riskProfile === "privileged-execution" ? "high" : "low",
    permissions: [],
    agentScopes: [s.providerId],
    isEnabled: !0
  })));
}
async function Re(e, t) {
  const n = P(await e.http.postJson("/_elsa/agent/sessions", {
    conversationId: "weaver-chat",
    ...t ? { providerId: t } : {},
    mode: "explain",
    activeSurface: { route: "/weaver" },
    clientContext: { studioVersion: "studio", sdkVersion: "studio", moduleIds: ["Elsa.Studio.Weaver.Chat"] },
    metadata: { route: "/weaver", source: "weaver-chat" }
  })), s = n.sessionId ?? n.id;
  if (!s) throw new Error("Weaver backend did not return an agent session.");
  return s;
}
function We(e) {
  const t = Ee(e.kind), n = $e(e, t), s = n.sourceId || e.referenceId || "";
  return {
    id: e.id ?? `${t}:${s || e.scope || "context"}`,
    kind: t,
    displayName: D(t),
    sensitivity: "public",
    summary: s || e.scope || t,
    references: n,
    metadata: e.metadata ?? {}
  };
}
function Ee(e) {
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
function $e(e, t) {
  const n = e.metadata ?? {}, s = {
    source: t.startsWith("workflow.") ? "workflow" : t,
    sourceId: e.referenceId ?? f(n, "artifactId") ?? f(n, "definitionId") ?? f(n, "id") ?? "",
    scope: e.scope ?? ""
  };
  return A(s, "workflowDefinitionId", f(n, "workflowDefinitionId") ?? f(n, "definitionId")), A(s, "workflowVersionId", f(n, "workflowVersionId") ?? f(n, "definitionVersionId") ?? f(n, "sourceId")), A(s, "artifactId", f(n, "artifactId")), A(s, "workflowExecutionId", f(n, "workflowExecutionId") ?? f(n, "instanceId")), A(s, "selectedActivityId", e.activityId ?? f(n, "selectedActivityId")), s;
}
function A(e, t, n) {
  n?.trim() && (e[t] = n);
}
function f(e, t) {
  const n = e[t];
  return typeof n == "string" && n.trim() ? n : void 0;
}
function Ne(e) {
  if (!e || typeof e != "object") return null;
  const t = e, n = R(t, "type") ?? R(t, "kind"), s = Me(n), o = ee(t, "error"), i = ee(t, "data"), u = j(t, "content") ?? j(i, "content") ?? j(o, "message") ?? "";
  return s === "assistant.delta" ? _("assistant.delta", t, { content: u }) : s === "conversation.error" ? _("conversation.error", t, { content: u || "Weaver is unavailable for this request." }) : s === "conversation.completed" ? _("conversation.completed", t, {}) : s === "conversation.started" ? null : e;
}
function _(e, t, n) {
  return {
    type: e,
    conversationId: j(t, "conversationId") ?? "",
    sequence: Number(R(t, "sequence") ?? 0),
    timestamp: j(t, "timestamp") ?? j(t, "createdAt") ?? (/* @__PURE__ */ new Date()).toISOString(),
    data: n
  };
}
function Me(e) {
  if (typeof e == "number")
    return ["conversation.started", "assistant.delta", "tool.approval", "proposal.created", "conversation.completed", "conversation.error"][e] ?? "";
  const t = String(e ?? "").replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
  return t === "message-delta" ? "assistant.delta" : t === "completed" ? "conversation.completed" : t === "error" ? "conversation.error" : String(e ?? "");
}
function D(e) {
  return e.replace(/[-_.]+/g, " ").replace(/\b\w/g, (t) => t.toUpperCase());
}
function j(e, t) {
  const n = R(e, t);
  return typeof n == "string" ? n : void 0;
}
function ee(e, t) {
  const n = R(e, t);
  return n && typeof n == "object" ? n : void 0;
}
function R(e, t) {
  if (!e) return;
  if (t in e) return e[t];
  const n = t.toLowerCase(), s = Object.keys(e).find((o) => o.toLowerCase() === n);
  return s ? e[s] : void 0;
}
const N = /* @__PURE__ */ new Set(), H = [];
let Pe = 0;
function Je(e) {
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
    component: () => /* @__PURE__ */ a.jsx(te, { api: e, variant: "route" })
  }), e.panels.add({
    id: "weaver-chat",
    title: "Weaver",
    order: 40,
    component: () => /* @__PURE__ */ a.jsx(te, { api: e, variant: "panel" })
  }), e.ai.surfaces.add({
    id: "weaver-chat",
    title: "Weaver chat",
    placement: "panel",
    moduleId: "Elsa.Studio.Weaver.Chat"
  }), e.ai.onPrompt((t) => Le(t));
}
function te({ api: e, variant: t }) {
  const [n, s] = m("loading"), [o, i] = m(null), [u, v] = m([]), [x, h] = m([]), [p, b] = m([]), [I, k] = m(""), [w, y] = m(""), [V, B] = m(null), [g, O] = m("idle"), [U, W] = m(""), q = Z(null), F = Z(!0), se = S(() => e.ai.promptActions.list(), [e]), ae = S(() => e.ai.contextProviders.list(), [e]), oe = S(() => e.ai.proposalRenderers.list(), [e]), ie = S(() => e.ai.surfaces.list(), [e]), ce = S(
    () => p.flatMap((r) => r.attachments ?? []).slice(0, 8),
    [p]
  ), T = pe(async () => {
    s("loading"), W("");
    try {
      const r = await ye(e.backend);
      i(r), y((c) => c || r.agents[0]?.name || ""), s(r.streaming ? "ready" : "unavailable"), r.streaming && v(await je(e.backend, w || r.agents[0]?.name));
    } catch (r) {
      i(null), v([]), s("unavailable"), W(r instanceof Error ? r.message : String(r));
    }
  }, [e.backend, w]);
  E(() => {
    T();
  }, [T]), E(() => {
    const r = (c) => {
      b((l) => [...l, c]), c.mode === "steer" && k(c.message);
    };
    if (N.add(r), H.length > 0) {
      const c = H.splice(0);
      for (const l of c)
        r(l);
    }
    return () => {
      N.delete(r);
    };
  }, []), E(() => {
    if (n !== "ready" || g !== "idle") return;
    const r = p.find((c) => c.mode !== "steer");
    r && Y(r);
  }, [n, g, p]), E(() => {
    const r = q.current;
    r && F.current && (r.scrollTop = r.scrollHeight);
  }, [x]);
  function de() {
    const r = q.current;
    r && (F.current = r.scrollHeight - r.scrollTop - r.clientHeight < 24);
  }
  async function Q(r) {
    const c = r.message.trim();
    if (!c || n !== "ready" || g === "streaming") return;
    const l = $("assistant");
    O("streaming"), W(""), k(""), h((d) => [
      ...d,
      { id: $("user"), role: "user", content: c },
      { id: l, role: "assistant", content: "" }
    ]);
    try {
      await Ie(e.backend, {
        conversationId: V,
        message: c,
        agent: r.agent ?? w,
        attachments: r.attachments
      }, (d) => {
        d.conversationId && B(d.conversationId), (d.type === "assistant.delta" && typeof d.data.content == "string" || d.type === "conversation.error" && typeof d.data.content == "string") && L(l, d.data.content);
      });
    } catch (d) {
      W(d instanceof Error ? d.message : String(d)), L(l, "Weaver is unavailable for this request.");
    } finally {
      O("idle");
    }
  }
  function L(r, c) {
    h((l) => l.map((d) => d.id === r ? { ...d, content: `${d.content}${c}` } : d));
  }
  function Y(r) {
    b((c) => c.filter((l) => l.id !== r.id)), Q(r);
  }
  function le() {
    Q({ message: I, agent: w || null, mode: "enqueue" });
  }
  function ue(r) {
    if (r !== w && (y(r), V)) {
      B(null);
      const c = o?.agents.find((l) => l.name === r)?.displayName || r;
      h((l) => l.length === 0 ? l : [...l, { id: $("system"), role: "system", content: `Switched to ${c}. Starting a new conversation.` }]);
    }
  }
  return /* @__PURE__ */ a.jsxs("section", { className: t === "panel" ? "weaver-surface panel" : "weaver-surface", children: [
    /* @__PURE__ */ a.jsxs("div", { className: "weaver-header", children: [
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("h2", { children: "Weaver" }),
        /* @__PURE__ */ a.jsx("p", { children: ze(n, o) })
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", className: "weaver-icon-button", "aria-label": "Refresh Weaver capabilities", title: "Refresh", onClick: () => {
        T();
      }, children: /* @__PURE__ */ a.jsx(be, { size: 15 }) })
    ] }),
    U ? /* @__PURE__ */ a.jsx("div", { className: "weaver-alert", children: U }) : null,
    /* @__PURE__ */ a.jsxs("div", { className: "weaver-layout", children: [
      /* @__PURE__ */ a.jsxs("aside", { className: "weaver-sidebar", children: [
        /* @__PURE__ */ a.jsxs("label", { className: "weaver-field", children: [
          /* @__PURE__ */ a.jsx("span", { children: "Agent" }),
          /* @__PURE__ */ a.jsx("select", { value: w, onChange: (r) => ue(r.target.value), disabled: n !== "ready" || g === "streaming", children: o?.agents.length ? o.agents.map((r) => /* @__PURE__ */ a.jsx("option", { value: r.name, children: r.displayName || r.name }, r.name)) : /* @__PURE__ */ a.jsx("option", { value: "", children: "No agent" }) })
        ] }),
        /* @__PURE__ */ a.jsx(qe, { attachments: ce }),
        /* @__PURE__ */ a.jsx(
          Te,
          {
            promptActions: se.length,
            contextProviders: ae.length,
            proposalRenderers: oe.length,
            surfaces: ie.length,
            tools: u.length
          }
        )
      ] }),
      /* @__PURE__ */ a.jsxs("main", { className: "weaver-chat", children: [
        /* @__PURE__ */ a.jsx("div", { className: "weaver-messages", "aria-live": "polite", ref: q, onScroll: de, children: x.length === 0 ? /* @__PURE__ */ a.jsxs("div", { className: "weaver-empty", children: [
          /* @__PURE__ */ a.jsx(ge, { size: 24 }),
          /* @__PURE__ */ a.jsx("strong", { children: "Ask Weaver about the current Studio context." }),
          /* @__PURE__ */ a.jsx("span", { children: "Workflow modules and third-party modules can attach resources, tools, and proposals when their Weaver features are enabled." })
        ] }) : x.map((r) => /* @__PURE__ */ a.jsxs("article", { className: `weaver-message ${r.role}`, children: [
          /* @__PURE__ */ a.jsx("span", { children: r.role === "user" ? "You" : r.role === "system" ? "System" : "Weaver" }),
          /* @__PURE__ */ a.jsx("p", { children: r.content || (g === "streaming" && r.role === "assistant" ? "Thinking..." : "") })
        ] }, r.id)) }),
        p.length > 0 ? /* @__PURE__ */ a.jsx("div", { className: "weaver-queue", "aria-label": "Queued Weaver prompts", children: p.map((r) => /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => Y(r), disabled: g === "streaming" || n !== "ready", children: [
          /* @__PURE__ */ a.jsx(xe, { size: 13 }),
          /* @__PURE__ */ a.jsx("span", { children: r.source?.label ?? r.message })
        ] }, r.id)) }) : null,
        /* @__PURE__ */ a.jsxs("div", { className: "weaver-composer", children: [
          /* @__PURE__ */ a.jsx(
            "textarea",
            {
              "aria-label": "Prompt Weaver",
              placeholder: n === "ready" ? "Ask Weaver..." : "Weaver backend is unavailable",
              value: I,
              onChange: (r) => k(r.target.value),
              disabled: n !== "ready",
              rows: t === "panel" ? 2 : 3
            }
          ),
          /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: le, disabled: !I.trim() || n !== "ready" || g === "streaming", children: [
            /* @__PURE__ */ a.jsx(ke, { size: 15 }),
            g === "streaming" ? "Streaming" : "Send"
          ] })
        ] })
      ] })
    ] })
  ] });
}
function qe({ attachments: e }) {
  return /* @__PURE__ */ a.jsxs("section", { className: "weaver-context-stack", children: [
    /* @__PURE__ */ a.jsx("h3", { children: "Context stack" }),
    e.length === 0 ? /* @__PURE__ */ a.jsx("p", { children: "No context attached." }) : e.map((t, n) => /* @__PURE__ */ a.jsxs("div", { className: "weaver-context-item", children: [
      /* @__PURE__ */ a.jsx("strong", { children: t.kind }),
      /* @__PURE__ */ a.jsx("span", { children: t.referenceId ?? t.scope ?? "current selection" })
    ] }, `${t.kind}-${t.referenceId ?? n}`))
  ] });
}
function Te({ promptActions: e, contextProviders: t, proposalRenderers: n, surfaces: s, tools: o }) {
  return /* @__PURE__ */ a.jsxs("section", { className: "weaver-contributions", children: [
    /* @__PURE__ */ a.jsx("h3", { children: "Enabled contributions" }),
    /* @__PURE__ */ a.jsxs("span", { children: [
      e,
      " prompt actions"
    ] }),
    /* @__PURE__ */ a.jsxs("span", { children: [
      t,
      " context providers"
    ] }),
    /* @__PURE__ */ a.jsxs("span", { children: [
      n,
      " proposal renderers"
    ] }),
    /* @__PURE__ */ a.jsxs("span", { children: [
      s,
      " surfaces"
    ] }),
    /* @__PURE__ */ a.jsxs("span", { children: [
      o,
      " backend tools"
    ] })
  ] });
}
function Le(e) {
  const t = { ...e, id: $("prompt") };
  if (N.size === 0) {
    H.push(t);
    return;
  }
  for (const n of N)
    n(t);
}
function ze(e, t) {
  if (e === "loading") return "Checking backend AI capabilities.";
  if (e === "unavailable") return "Enable Elsa Server AI capabilities to use Weaver chat.";
  const n = t?.conversationPersistence ? "durable sessions" : "ephemeral sessions", s = t?.proposalReview ? "proposal review" : "draft-only suggestions";
  return `${t?.agents.length ?? 0} agent(s), ${n}, ${s}.`;
}
function $(e) {
  return `${e}-${Date.now()}-${++Pe}`;
}
export {
  Je as register
};
