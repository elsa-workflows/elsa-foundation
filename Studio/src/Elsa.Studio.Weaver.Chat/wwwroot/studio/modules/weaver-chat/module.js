import { forwardRef as H, createElement as W, useState as p, useMemo as j, useCallback as X, useEffect as L } from "react";
var N = { exports: {} }, w = {};
var T;
function K() {
  if (T) return w;
  T = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), s = /* @__PURE__ */ Symbol.for("react.fragment");
  function n(r, o, c) {
    var d = null;
    if (c !== void 0 && (d = "" + c), o.key !== void 0 && (d = "" + o.key), "key" in o) {
      c = {};
      for (var u in o)
        u !== "key" && (c[u] = o[u]);
    } else c = o;
    return o = c.ref, {
      $$typeof: e,
      type: r,
      key: d,
      ref: o !== void 0 ? o : null,
      props: c
    };
  }
  return w.Fragment = s, w.jsx = n, w.jsxs = n, w;
}
var z;
function ee() {
  return z || (z = 1, N.exports = K()), N.exports;
}
var t = ee();
const te = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), O = (...e) => e.filter((s, n, r) => !!s && s.trim() !== "" && r.indexOf(s) === n).join(" ").trim();
var se = {
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
const ae = H(
  ({
    color: e = "currentColor",
    size: s = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: r,
    className: o = "",
    children: c,
    iconNode: d,
    ...u
  }, m) => W(
    "svg",
    {
      ref: m,
      ...se,
      width: s,
      height: s,
      stroke: e,
      strokeWidth: r ? Number(n) * 24 / Number(s) : n,
      className: O("lucide", o),
      ...u
    },
    [
      ...d.map(([x, v]) => W(x, v)),
      ...Array.isArray(c) ? c : [c]
    ]
  )
);
const y = (e, s) => {
  const n = H(
    ({ className: r, ...o }, c) => W(ae, {
      ref: c,
      iconNode: s,
      className: O(`lucide-${te(e)}`, r),
      ...o
    })
  );
  return n.displayName = `${e}`, n;
};
const ne = y("Bot", [
  ["path", { d: "M12 8V4H8", key: "hb8ula" }],
  ["rect", { width: "16", height: "12", x: "4", y: "8", rx: "2", key: "enze0r" }],
  ["path", { d: "M2 14h2", key: "vft8re" }],
  ["path", { d: "M20 14h2", key: "4cs60a" }],
  ["path", { d: "M15 13v2", key: "1xurst" }],
  ["path", { d: "M9 13v2", key: "rq6x2g" }]
]);
const re = y("MessageSquare", [
  ["path", { d: "M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z", key: "1lielz" }]
]);
const oe = y("RefreshCcw", [
  ["path", { d: "M21 12a9 9 0 0 0-9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "14sxne" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }],
  ["path", { d: "M3 12a9 9 0 0 0 9 9 9.75 9.75 0 0 0 6.74-2.74L21 16", key: "1hlbsb" }],
  ["path", { d: "M16 16h5v5", key: "ccwih5" }]
]);
const ie = y("Send", [
  [
    "path",
    {
      d: "M14.536 21.686a.5.5 0 0 0 .937-.024l6.5-19a.496.496 0 0 0-.635-.635l-19 6.5a.5.5 0 0 0-.024.937l7.93 3.18a2 2 0 0 1 1.112 1.11z",
      key: "1ffxy3"
    }
  ],
  ["path", { d: "m21.854 2.147-10.94 10.939", key: "12cjpa" }]
]);
const ce = y("Sparkles", [
  [
    "path",
    {
      d: "M9.937 15.5A2 2 0 0 0 8.5 14.063l-6.135-1.582a.5.5 0 0 1 0-.962L8.5 9.936A2 2 0 0 0 9.937 8.5l1.582-6.135a.5.5 0 0 1 .963 0L14.063 8.5A2 2 0 0 0 15.5 9.937l6.135 1.581a.5.5 0 0 1 0 .964L15.5 14.063a2 2 0 0 0-1.437 1.437l-1.582 6.135a.5.5 0 0 1-.963 0z",
      key: "4pj2yx"
    }
  ],
  ["path", { d: "M20 3v4", key: "1olli1" }],
  ["path", { d: "M22 5h-4", key: "1gvqau" }],
  ["path", { d: "M4 17v2", key: "vumght" }],
  ["path", { d: "M5 18H3", key: "zchphs" }]
]);
async function le(e) {
  return e.http.getJson("/ai/capabilities");
}
async function de(e, s) {
  const n = new URLSearchParams();
  s && n.set("agent", s);
  const r = n.toString();
  return e.http.getJson(`/ai/tools${r ? `?${r}` : ""}`);
}
async function ue(e, s, n) {
  const r = await fetch(new URL("/ai/chat", e.baseUrl).toString(), he(e.headers, {
    method: "POST",
    headers: {
      Accept: "text/event-stream",
      "Content-Type": "application/json"
    },
    body: JSON.stringify({
      conversationId: s.conversationId || void 0,
      message: s.message,
      agent: s.agent || void 0,
      attachments: s.attachments ?? []
    })
  }));
  if (!r.ok)
    throw new Error(await r.text() || `Weaver chat failed with ${r.status}.`);
  if (!r.body)
    return;
  const o = r.body.getReader(), c = new TextDecoder();
  let d = "";
  for (; ; ) {
    const { value: m, done: x } = await o.read();
    if (x) break;
    d += c.decode(m, { stream: !0 });
    const v = d.split(`

`);
    d = v.pop() ?? "";
    for (const k of v) {
      const f = J(k);
      f && n(f);
    }
  }
  const u = J(d);
  u && n(u);
}
function J(e) {
  const s = e.split(`
`).map((n) => n.trim()).find((n) => n.startsWith("data:"));
  return s ? JSON.parse(s.slice(5).trim()) : null;
}
function he(e, s = {}) {
  if (!e) return s;
  const n = new Headers(e);
  return new Headers(s.headers).forEach((r, o) => n.set(o, r)), { ...s, headers: n };
}
const A = /* @__PURE__ */ new Set();
let pe = 0;
function be(e) {
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
    component: () => /* @__PURE__ */ t.jsx(_, { api: e, variant: "route" })
  }), e.panels.add({
    id: "weaver-chat",
    title: "Weaver",
    order: 40,
    component: () => /* @__PURE__ */ t.jsx(_, { api: e, variant: "panel" })
  }), e.ai.surfaces.add({
    id: "weaver-chat",
    title: "Weaver chat",
    placement: "panel",
    moduleId: "Elsa.Studio.Weaver.Chat"
  }), e.ai.onPrompt((s) => xe(s));
}
function _({ api: e, variant: s }) {
  const [n, r] = p("loading"), [o, c] = p(null), [d, u] = p([]), [m, x] = p([]), [v, k] = p([]), [f, C] = p(""), [g, $] = p(""), [B, D] = p(null), [b, I] = p("idle"), [P, S] = p(""), Q = j(() => e.ai.promptActions.list(), [e]), U = j(() => e.ai.contextProviders.list(), [e]), Y = j(() => e.ai.proposalRenderers.list(), [e]), F = j(() => e.ai.surfaces.list(), [e]), V = j(
    () => v.flatMap((a) => a.attachments ?? []).slice(0, 8),
    [v]
  ), R = X(async () => {
    r("loading"), S("");
    try {
      const a = await le(e.backend);
      c(a), $((l) => l || a.agents[0]?.name || ""), r(a.streaming ? "ready" : "unavailable"), a.streaming && u(await de(e.backend, g || a.agents[0]?.name));
    } catch (a) {
      c(null), u([]), r("unavailable"), S(a instanceof Error ? a.message : String(a));
    }
  }, [e.backend, g]);
  L(() => {
    R();
  }, [R]), L(() => {
    const a = (l) => {
      k((h) => [...h, l]), l.mode === "steer" && C(l.message);
    };
    return A.add(a), () => {
      A.delete(a);
    };
  }, []);
  async function q(a) {
    const l = a.message.trim();
    if (!l || n !== "ready" || b === "streaming") return;
    const h = E("assistant");
    I("streaming"), S(""), C(""), x((i) => [
      ...i,
      { id: E("user"), role: "user", content: l },
      { id: h, role: "assistant", content: "" }
    ]);
    try {
      await ue(e.backend, {
        conversationId: B,
        message: l,
        agent: a.agent ?? g,
        attachments: a.attachments
      }, (i) => {
        i.conversationId && D(i.conversationId), (i.type === "assistant.delta" && typeof i.data.content == "string" || i.type === "conversation.error" && typeof i.data.content == "string") && M(h, i.data.content);
      });
    } catch (i) {
      S(i instanceof Error ? i.message : String(i)), M(h, "Weaver is unavailable for this request.");
    } finally {
      I("idle");
    }
  }
  function M(a, l) {
    x((h) => h.map((i) => i.id === a ? { ...i, content: `${i.content}${l}` } : i));
  }
  function G(a) {
    k((l) => l.filter((h) => h.id !== a.id)), q(a);
  }
  function Z() {
    q({ message: f, agent: g || null, mode: "enqueue" });
  }
  return /* @__PURE__ */ t.jsxs("section", { className: s === "panel" ? "weaver-surface panel" : "weaver-surface", children: [
    /* @__PURE__ */ t.jsxs("div", { className: "weaver-header", children: [
      /* @__PURE__ */ t.jsxs("div", { children: [
        /* @__PURE__ */ t.jsxs("span", { className: "weaver-kicker", children: [
          /* @__PURE__ */ t.jsx(ce, { size: 14 }),
          " Optional module"
        ] }),
        /* @__PURE__ */ t.jsx("h2", { children: "Weaver" }),
        /* @__PURE__ */ t.jsx("p", { children: fe(n, o) })
      ] }),
      /* @__PURE__ */ t.jsx("button", { type: "button", className: "weaver-icon-button", "aria-label": "Refresh Weaver capabilities", title: "Refresh", onClick: () => {
        R();
      }, children: /* @__PURE__ */ t.jsx(oe, { size: 15 }) })
    ] }),
    P ? /* @__PURE__ */ t.jsx("div", { className: "weaver-alert", children: P }) : null,
    /* @__PURE__ */ t.jsxs("div", { className: "weaver-layout", children: [
      /* @__PURE__ */ t.jsxs("aside", { className: "weaver-sidebar", children: [
        /* @__PURE__ */ t.jsxs("label", { className: "weaver-field", children: [
          /* @__PURE__ */ t.jsx("span", { children: "Agent" }),
          /* @__PURE__ */ t.jsx("select", { value: g, onChange: (a) => $(a.target.value), disabled: n !== "ready", children: o?.agents.length ? o.agents.map((a) => /* @__PURE__ */ t.jsx("option", { value: a.name, children: a.displayName || a.name }, a.name)) : /* @__PURE__ */ t.jsx("option", { value: "", children: "No agent" }) })
        ] }),
        /* @__PURE__ */ t.jsx(ve, { attachments: V }),
        /* @__PURE__ */ t.jsx(
          me,
          {
            promptActions: Q.length,
            contextProviders: U.length,
            proposalRenderers: Y.length,
            surfaces: F.length,
            tools: d.length
          }
        )
      ] }),
      /* @__PURE__ */ t.jsxs("main", { className: "weaver-chat", children: [
        /* @__PURE__ */ t.jsx("div", { className: "weaver-messages", "aria-live": "polite", children: m.length === 0 ? /* @__PURE__ */ t.jsxs("div", { className: "weaver-empty", children: [
          /* @__PURE__ */ t.jsx(ne, { size: 24 }),
          /* @__PURE__ */ t.jsx("strong", { children: "Ask Weaver about the current Studio context." }),
          /* @__PURE__ */ t.jsx("span", { children: "Workflow modules and third-party modules can attach resources, tools, and proposals when their Weaver features are enabled." })
        ] }) : m.map((a) => /* @__PURE__ */ t.jsxs("article", { className: `weaver-message ${a.role}`, children: [
          /* @__PURE__ */ t.jsx("span", { children: a.role === "user" ? "You" : "Weaver" }),
          /* @__PURE__ */ t.jsx("p", { children: a.content || (b === "streaming" && a.role === "assistant" ? "Thinking..." : "") })
        ] }, a.id)) }),
        v.length > 0 ? /* @__PURE__ */ t.jsx("div", { className: "weaver-queue", "aria-label": "Queued Weaver prompts", children: v.map((a) => /* @__PURE__ */ t.jsxs("button", { type: "button", onClick: () => G(a), disabled: b === "streaming" || n !== "ready", children: [
          /* @__PURE__ */ t.jsx(re, { size: 13 }),
          /* @__PURE__ */ t.jsx("span", { children: a.source?.label ?? a.message })
        ] }, a.id)) }) : null,
        /* @__PURE__ */ t.jsxs("div", { className: "weaver-composer", children: [
          /* @__PURE__ */ t.jsx(
            "textarea",
            {
              "aria-label": "Prompt Weaver",
              placeholder: n === "ready" ? "Ask Weaver..." : "Weaver backend is unavailable",
              value: f,
              onChange: (a) => C(a.target.value),
              disabled: n !== "ready",
              rows: s === "panel" ? 2 : 3
            }
          ),
          /* @__PURE__ */ t.jsxs("button", { type: "button", onClick: Z, disabled: !f.trim() || n !== "ready" || b === "streaming", children: [
            /* @__PURE__ */ t.jsx(ie, { size: 15 }),
            b === "streaming" ? "Streaming" : "Send"
          ] })
        ] })
      ] })
    ] })
  ] });
}
function ve({ attachments: e }) {
  return /* @__PURE__ */ t.jsxs("section", { className: "weaver-context-stack", children: [
    /* @__PURE__ */ t.jsx("h3", { children: "Context stack" }),
    e.length === 0 ? /* @__PURE__ */ t.jsx("p", { children: "No context attached." }) : e.map((s, n) => /* @__PURE__ */ t.jsxs("div", { className: "weaver-context-item", children: [
      /* @__PURE__ */ t.jsx("strong", { children: s.kind }),
      /* @__PURE__ */ t.jsx("span", { children: s.referenceId ?? s.scope ?? "current selection" })
    ] }, `${s.kind}-${s.referenceId ?? n}`))
  ] });
}
function me({ promptActions: e, contextProviders: s, proposalRenderers: n, surfaces: r, tools: o }) {
  return /* @__PURE__ */ t.jsxs("section", { className: "weaver-contributions", children: [
    /* @__PURE__ */ t.jsx("h3", { children: "Enabled contributions" }),
    /* @__PURE__ */ t.jsxs("span", { children: [
      e,
      " prompt actions"
    ] }),
    /* @__PURE__ */ t.jsxs("span", { children: [
      s,
      " context providers"
    ] }),
    /* @__PURE__ */ t.jsxs("span", { children: [
      n,
      " proposal renderers"
    ] }),
    /* @__PURE__ */ t.jsxs("span", { children: [
      r,
      " surfaces"
    ] }),
    /* @__PURE__ */ t.jsxs("span", { children: [
      o,
      " backend tools"
    ] })
  ] });
}
function xe(e) {
  const s = { ...e, id: E("prompt") };
  for (const n of A)
    n(s);
}
function fe(e, s) {
  if (e === "loading") return "Checking backend AI capabilities.";
  if (e === "unavailable") return "Enable Elsa Server AI capabilities to use Weaver chat.";
  const n = s?.conversationPersistence ? "durable sessions" : "ephemeral sessions", r = s?.proposalReview ? "proposal review" : "draft-only suggestions";
  return `${s?.agents.length ?? 0} agent(s), ${n}, ${r}.`;
}
function E(e) {
  return `${e}-${Date.now()}-${++pe}`;
}
export {
  be as register
};
