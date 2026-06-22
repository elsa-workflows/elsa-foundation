import { useState as d, useRef as B, useEffect as x, useCallback as be } from "react";
var D = { exports: {} }, w = {};
var W;
function we() {
  if (W) return w;
  W = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), s = /* @__PURE__ */ Symbol.for("react.fragment");
  function l(r, o, n) {
    var p = null;
    if (n !== void 0 && (p = "" + n), o.key !== void 0 && (p = "" + o.key), "key" in o) {
      n = {};
      for (var j in o)
        j !== "key" && (n[j] = o[j]);
    } else n = o;
    return o = n.ref, {
      $$typeof: e,
      type: r,
      key: p,
      ref: o !== void 0 ? o : null,
      props: n
    };
  }
  return w.Fragment = s, w.jsx = l, w.jsxs = l, w;
}
var Y;
function ye() {
  return Y || (Y = 1, D.exports = we()), D.exports;
}
var t = ye();
const J = "/_elsa/studio/diagnostics/structured-logs", Ce = 2e3, P = 2e3, Le = {
  minLevel: "Information",
  sourceId: "",
  category: ""
}, H = "elsa-studio-structured-logs-autoscroll", Q = "elsa-studio-structured-logs-wrap", Z = "elsa-studio-structured-logs-compact", A = ["Trace", "Debug", "Information", "Warning", "Error", "Critical"];
let R;
function ze(e) {
  R = e, e.navigation.add({
    id: "structured-logs",
    label: "Structured logs",
    path: "/diagnostics/structured-logs",
    order: 860,
    iconColor: "#10b981",
    parentId: "diagnostics"
  }), e.routes.add({
    id: "structured-logs",
    label: "Structured logs",
    path: "/diagnostics/structured-logs",
    component: Re
  }), e.panels.add({
    id: "structured-logs",
    title: "Structured Logs",
    order: 1010,
    component: Ie
  });
}
function Ie() {
  const e = ee({ includeInspector: !1 });
  return /* @__PURE__ */ t.jsxs("section", { className: "structured-logs-panel", children: [
    /* @__PURE__ */ t.jsx(te, { state: e, compactControls: !0 }),
    /* @__PURE__ */ t.jsx(se, { state: e, panelMode: !0 })
  ] });
}
function Re() {
  const e = ee({ includeInspector: !0 }), s = e.rows.find((l) => l.id === e.selectedEntryId) ?? null;
  return /* @__PURE__ */ t.jsxs("section", { className: "structured-logs-page", children: [
    /* @__PURE__ */ t.jsx(te, { state: e }),
    /* @__PURE__ */ t.jsxs("div", { className: s ? "structured-logs-workbench" : "structured-logs-workbench no-selection", children: [
      /* @__PURE__ */ t.jsx(se, { state: e }),
      s ? /* @__PURE__ */ t.jsx(Ee, { entry: s, sources: e.sources, onClose: () => e.setSelectedEntryId(null) }) : null
    ] })
  ] });
}
function ee({ includeInspector: e }) {
  const [s, l] = d(Le), [r, o] = d([]), [n, p] = d([]), [j, E] = d(null), [ce, g] = d("connecting"), [le, h] = d("Connecting to structured logs."), [v, ie] = d(!1), [O, C] = d([]), [ae, L] = d(0), [ue, z] = d(0), [de, I] = d(0), [S, pe] = d(q(H, !0)), [N, me] = d(q(Q, !0)), [k, ge] = d(q(Z, !e)), U = B(null), T = B(v);
  x(() => {
    T.current = v;
  }, [v]);
  const _ = be((c) => {
    if (c.length !== 0) {
      if (T.current) {
        C((i) => $(i, c).rows), L((i) => i + c.length);
        return;
      }
      p((i) => {
        const a = $(i, c);
        return a.discarded > 0 && I((u) => u + a.discarded), a.rows;
      });
    }
  }, []);
  x(() => {
    M(H, S);
  }, [S]), x(() => {
    M(Q, N);
  }, [N]), x(() => {
    M(Z, k);
  }, [k]), x(() => {
    if (!S || v)
      return;
    const c = U.current;
    c && (c.scrollTop = c.scrollHeight);
  }, [S, v, n]), x(() => {
    let c = !1;
    g("connecting"), h("Loading structured log sources.");
    async function i() {
      try {
        const a = await R.backend.http.getJson(`${J}/sources`);
        c || o(a.sort(Ae));
      } catch (a) {
        if (!c) {
          const u = V(a);
          g(u.status), h(u.message), o([]);
        }
      }
    }
    return i(), () => {
      c = !0;
    };
  }, []), x(() => {
    let c = !1, i = null;
    p([]), E(null), C([]), L(0), z(0), I(0), g("connecting"), h("Loading recent structured logs.");
    async function a() {
      try {
        const f = await R.backend.http.getJson(Te(s));
        c || p(f.map(G).sort(ne).slice(-P));
      } catch (f) {
        if (!c) {
          const b = V(f);
          g(b.status), h(b.message);
        }
      }
    }
    function u() {
      i = new EventSource(De(R.backend, s)), i.onopen = () => {
        c || (g(T.current ? "paused" : "live"), h("Live structured log stream connected."));
      }, i.addEventListener("entry", (f) => {
        c || _([G(X(f))]);
      }), i.addEventListener("dropped", (f) => {
        if (c)
          return;
        const b = X(f);
        z((Se) => Se + (b.droppedCount ?? b.count ?? 0));
      }), i.onerror = () => {
        c || (g("disconnected"), h("Live stream disconnected. The browser will retry automatically."));
      };
    }
    return a().then(() => {
      c || u();
    }), () => {
      c = !0, i?.close();
    };
  }, [_, s]);
  function fe() {
    ie((c) => {
      const i = !c;
      if (c && O.length > 0) {
        const a = $(n, O);
        p(a.rows), a.discarded > 0 && I((u) => u + a.discarded), C([]), L(0);
      }
      return g(i ? "paused" : "live"), i;
    });
  }
  function xe() {
    p([]), E(null), C([]), L(0), I(0);
  }
  async function he(c) {
    const i = c.map(Je).join(`
`);
    i && await navigator.clipboard?.writeText(i);
  }
  async function ve(c) {
    c.message && await navigator.clipboard?.writeText(c.message);
  }
  function je(c) {
    if (c.length === 0 || typeof document > "u")
      return;
    const i = new Blob([qe(c)], { type: "application/x-ndjson;charset=utf-8" }), a = URL.createObjectURL(i), u = document.createElement("a");
    u.href = a, u.download = Me(), u.rel = "noopener", document.body.appendChild(u), u.click(), u.remove(), window.setTimeout(() => URL.revokeObjectURL(a), 0);
  }
  return {
    filters: s,
    setFilters: l,
    sources: r,
    rows: n,
    selectedEntryId: j,
    setSelectedEntryId: E,
    status: ce,
    detail: le,
    paused: v,
    togglePaused: fe,
    queuedCount: ae,
    upstreamDroppedCount: ue,
    localDiscardedCount: de,
    autoScroll: S,
    setAutoScroll: pe,
    wrapMessages: N,
    setWrapMessages: me,
    compact: k,
    setCompact: ge,
    viewportRef: U,
    clear: xe,
    copyRows: he,
    copyMessage: ve,
    downloadRows: je
  };
}
function te({ state: e, compactControls: s = !1 }) {
  const l = Fe(e.status), r = e.selectedEntryId ? e.rows.filter((n) => n.id === e.selectedEntryId) : [], o = /* @__PURE__ */ t.jsxs("select", { "aria-label": "Minimum log level", value: e.filters.minLevel, onChange: (n) => e.setFilters({ ...e.filters, minLevel: n.target.value }), children: [
    /* @__PURE__ */ t.jsx("option", { value: "", children: "All levels" }),
    A.map((n) => /* @__PURE__ */ t.jsx("option", { value: n, children: n }, n))
  ] });
  return /* @__PURE__ */ t.jsxs("header", { className: "structured-logs-header", children: [
    /* @__PURE__ */ t.jsxs("div", { children: [
      /* @__PURE__ */ t.jsx("h2", { children: "Structured logs" }),
      /* @__PURE__ */ t.jsx("p", { children: e.queuedCount > 0 ? `${e.queuedCount} buffered while paused` : e.detail })
    ] }),
    /* @__PURE__ */ t.jsxs("div", { className: "structured-logs-tools", children: [
      s ? /* @__PURE__ */ t.jsxs(t.Fragment, { children: [
        o,
        /* @__PURE__ */ t.jsxs("select", { "aria-label": "Structured log source", value: e.filters.sourceId, onChange: (n) => e.setFilters({ ...e.filters, sourceId: n.target.value }), children: [
          /* @__PURE__ */ t.jsx("option", { value: "", children: "All sources" }),
          e.sources.map((n) => /* @__PURE__ */ t.jsx("option", { value: n.id, children: y(n) }, n.id))
        ] })
      ] }) : /* @__PURE__ */ t.jsxs(t.Fragment, { children: [
        o,
        /* @__PURE__ */ t.jsxs("select", { "aria-label": "Structured log source", value: e.filters.sourceId, onChange: (n) => e.setFilters({ ...e.filters, sourceId: n.target.value }), children: [
          /* @__PURE__ */ t.jsx("option", { value: "", children: "All sources" }),
          e.sources.map((n) => /* @__PURE__ */ t.jsx("option", { value: n.id, children: y(n) }, n.id))
        ] }),
        /* @__PURE__ */ t.jsx(
          "input",
          {
            "aria-label": "Filter by category",
            placeholder: "Category",
            value: e.filters.category,
            onChange: (n) => e.setFilters({ ...e.filters, category: n.target.value })
          }
        )
      ] }),
      /* @__PURE__ */ t.jsx("span", { className: `structured-logs-status ${e.status}`, "aria-label": l }),
      /* @__PURE__ */ t.jsx("span", { children: l }),
      e.upstreamDroppedCount > 0 ? /* @__PURE__ */ t.jsxs("span", { children: [
        e.upstreamDroppedCount,
        " dropped upstream"
      ] }) : null,
      e.localDiscardedCount > 0 ? /* @__PURE__ */ t.jsxs("span", { children: [
        e.localDiscardedCount,
        " discarded locally"
      ] }) : null,
      /* @__PURE__ */ t.jsx("button", { type: "button", className: e.paused ? "active" : "", onClick: e.togglePaused, "aria-pressed": e.paused, children: e.paused ? "Resume" : "Pause" }),
      /* @__PURE__ */ t.jsx("button", { type: "button", className: e.autoScroll ? "active" : "", onClick: () => e.setAutoScroll((n) => !n), "aria-pressed": e.autoScroll, children: "Autoscroll" }),
      s ? /* @__PURE__ */ t.jsx("button", { type: "button", onClick: () => e.downloadRows(e.rows), disabled: e.rows.length === 0, children: "Download" }) : /* @__PURE__ */ t.jsxs(t.Fragment, { children: [
        /* @__PURE__ */ t.jsx("button", { type: "button", className: e.wrapMessages ? "active" : "", onClick: () => e.setWrapMessages((n) => !n), "aria-pressed": e.wrapMessages, children: "Wrap" }),
        /* @__PURE__ */ t.jsx("button", { type: "button", className: e.compact ? "active" : "", onClick: () => e.setCompact((n) => !n), "aria-pressed": e.compact, children: "Compact" }),
        /* @__PURE__ */ t.jsx("button", { type: "button", onClick: () => {
          e.copyRows(r);
        }, disabled: r.length === 0, children: "Copy selected" }),
        /* @__PURE__ */ t.jsx("button", { type: "button", onClick: () => {
          e.copyRows(e.rows);
        }, children: "Copy visible" }),
        /* @__PURE__ */ t.jsx("button", { type: "button", onClick: () => e.downloadRows(e.rows), disabled: e.rows.length === 0, children: "Download visible" })
      ] }),
      /* @__PURE__ */ t.jsx("button", { type: "button", onClick: e.clear, children: "Clear" })
    ] })
  ] });
}
function se({ state: e, panelMode: s = !1 }) {
  const l = [
    "structured-logs-rows",
    e.wrapMessages ? "wrap" : "",
    e.compact ? "compact" : "",
    s ? "panel-mode" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ t.jsxs("div", { className: l, ref: e.viewportRef, children: [
    /* @__PURE__ */ t.jsxs("div", { className: "structured-logs-grid-header", role: "row", children: [
      /* @__PURE__ */ t.jsx("span", { children: "Time" }),
      /* @__PURE__ */ t.jsx("span", { children: "Level" }),
      /* @__PURE__ */ t.jsx("span", { children: "Source" }),
      /* @__PURE__ */ t.jsx("span", { children: "Category" }),
      /* @__PURE__ */ t.jsx("span", { children: "Message" }),
      /* @__PURE__ */ t.jsx("span", { "aria-hidden": "true" })
    ] }),
    e.rows.length === 0 ? /* @__PURE__ */ t.jsx("div", { className: "structured-logs-empty", children: e.status === "unavailable" || e.status === "unauthorized" ? e.detail : "No structured logs received yet." }) : null,
    e.rows.map((r) => /* @__PURE__ */ t.jsxs(
      "div",
      {
        className: r.id === e.selectedEntryId ? "structured-log-row selected" : "structured-log-row",
        onClick: () => e.setSelectedEntryId(r.id),
        onKeyDown: (o) => ke(o, () => e.setSelectedEntryId(r.id)),
        role: "row",
        tabIndex: 0,
        "aria-label": `Select ${r.level} structured log from ${r.category}`,
        children: [
          /* @__PURE__ */ t.jsx("time", { title: r.timestamp, children: oe(r.timestamp) }),
          /* @__PURE__ */ t.jsx(Ne, { level: r.level }),
          /* @__PURE__ */ t.jsx("span", { title: r.sourceId ?? void 0, children: r.sourceId ?? "local" }),
          /* @__PURE__ */ t.jsx("span", { title: r.category, children: r.category }),
          /* @__PURE__ */ t.jsxs("span", { className: "structured-log-message", children: [
            r.exception ? /* @__PURE__ */ t.jsx("strong", { title: r.exception.message ?? void 0, children: "Exception" }) : null,
            r.message
          ] }),
          /* @__PURE__ */ t.jsx(
            "button",
            {
              type: "button",
              className: "structured-log-copy-message",
              onClick: (o) => {
                o.stopPropagation(), e.copyMessage(r);
              },
              "aria-label": `Copy structured log message from ${r.category}`,
              title: "Copy message",
              children: /* @__PURE__ */ t.jsx("span", { "aria-hidden": "true" })
            }
          )
        ]
      },
      r.id
    ))
  ] });
}
function Ee({ entry: e, sources: s, onClose: l }) {
  const r = s.find((o) => o.id === e.sourceId);
  return /* @__PURE__ */ t.jsxs("aside", { className: "structured-log-inspector", "aria-label": "Structured log details", children: [
    /* @__PURE__ */ t.jsxs("header", { children: [
      /* @__PURE__ */ t.jsxs("div", { children: [
        /* @__PURE__ */ t.jsxs("span", { children: [
          e.level,
          " · ",
          oe(e.timestamp)
        ] }),
        /* @__PURE__ */ t.jsx("h3", { children: e.category })
      ] }),
      /* @__PURE__ */ t.jsx("button", { type: "button", onClick: l, "aria-label": "Close structured log details", children: "Close" })
    ] }),
    /* @__PURE__ */ t.jsxs("section", { children: [
      /* @__PURE__ */ t.jsx("h4", { children: "Message" }),
      /* @__PURE__ */ t.jsx("p", { children: e.message }),
      e.messageTemplate ? /* @__PURE__ */ t.jsx("pre", { children: e.messageTemplate }) : null
    ] }),
    /* @__PURE__ */ t.jsxs("section", { children: [
      /* @__PURE__ */ t.jsx("h4", { children: "Metadata" }),
      /* @__PURE__ */ t.jsxs("dl", { children: [
        /* @__PURE__ */ t.jsx(m, { name: "Sequence", value: String(e.sequence) }),
        /* @__PURE__ */ t.jsx(m, { name: "Timestamp", value: e.timestamp }),
        /* @__PURE__ */ t.jsx(m, { name: "Source", value: r ? y(r) : e.sourceId }),
        /* @__PURE__ */ t.jsx(m, { name: "Event ID", value: e.eventId == null ? null : String(e.eventId) }),
        /* @__PURE__ */ t.jsx(m, { name: "Event name", value: e.eventName })
      ] })
    ] }),
    e.properties.length > 0 ? /* @__PURE__ */ t.jsxs("section", { children: [
      /* @__PURE__ */ t.jsx("h4", { children: "Properties" }),
      /* @__PURE__ */ t.jsx("dl", { children: e.properties.map((o) => /* @__PURE__ */ t.jsx(m, { name: o.name, value: K(o.value) }, o.name)) })
    ] }) : null,
    e.scopes.length > 0 ? /* @__PURE__ */ t.jsxs("section", { children: [
      /* @__PURE__ */ t.jsx("h4", { children: "Scopes" }),
      e.scopes.map((o, n) => /* @__PURE__ */ t.jsxs("dl", { children: [
        o.items?.map((p) => /* @__PURE__ */ t.jsx(m, { name: p.name, value: K(p.value) }, p.name)),
        o.message ? /* @__PURE__ */ t.jsx(m, { name: "Scope", value: o.message }) : null
      ] }, n))
    ] }) : null,
    e.exception ? /* @__PURE__ */ t.jsxs("section", { children: [
      /* @__PURE__ */ t.jsx("h4", { children: "Exception" }),
      /* @__PURE__ */ t.jsxs("dl", { children: [
        /* @__PURE__ */ t.jsx(m, { name: "Type", value: e.exception.type }),
        /* @__PURE__ */ t.jsx(m, { name: "Message", value: e.exception.message })
      ] }),
      e.exception.stackTrace ? /* @__PURE__ */ t.jsx("pre", { children: e.exception.stackTrace }) : null
    ] }) : null,
    /* @__PURE__ */ t.jsxs("section", { children: [
      /* @__PURE__ */ t.jsx("h4", { children: "Raw JSON" }),
      /* @__PURE__ */ t.jsx("pre", { children: JSON.stringify(e.raw, null, 2) })
    ] })
  ] });
}
function Ne({ level: e }) {
  return /* @__PURE__ */ t.jsx("span", { className: "structured-log-level", "data-level": e.toLowerCase(), children: e });
}
function ke(e, s) {
  e.key !== "Enter" && e.key !== " " || (e.preventDefault(), s());
}
function m({ name: e, value: s }) {
  return s ? /* @__PURE__ */ t.jsxs(t.Fragment, { children: [
    /* @__PURE__ */ t.jsx("dt", { children: e }),
    /* @__PURE__ */ t.jsx("dd", { children: s })
  ] }) : null;
}
function Te(e, s = Ce) {
  const l = re(e);
  return l.set("take", String(s)), `${J}/recent?${l}`;
}
function De(e, s) {
  const r = re(s).toString(), o = r ? `?${r}` : "";
  return new URL(`${J}/stream${o}`, e.baseUrl).toString();
}
function $(e, s) {
  const l = new Map(e.map((n) => [n.id, n]));
  for (const n of s)
    l.set(n.id, n);
  const r = [...l.values()].sort(ne), o = Math.max(0, r.length - P);
  return {
    rows: r.slice(-P),
    discarded: o
  };
}
function G(e) {
  const s = Number(e.sequence ?? Date.now());
  return {
    id: String(s),
    sequence: s,
    timestamp: e.timestamp ?? (/* @__PURE__ */ new Date()).toISOString(),
    level: $e(e.level),
    category: e.category ?? "Unknown",
    eventId: e.eventId ?? null,
    eventName: e.eventName ?? null,
    message: e.message ?? "",
    messageTemplate: e.messageTemplate ?? null,
    properties: F(e.properties),
    scopes: Pe(e.scopes),
    exception: e.exception ?? null,
    sourceId: e.sourceId ?? null,
    raw: e
  };
}
function $e(e) {
  return typeof e == "number" ? A[e] ?? "Information" : A.find((l) => l.toLowerCase() === String(e ?? "").toLowerCase()) ?? "Information";
}
function y(e) {
  const s = e.displayName || e.serviceName || e.id;
  return e.machineName && e.processId ? `${s} · ${e.machineName}:${e.processId}` : s;
}
function V(e) {
  const s = e instanceof Error ? e.message : String(e);
  return /\b401\b|\b403\b/i.test(s) ? { status: "unauthorized", message: "You do not have permission to view structured logs." } : /\b404\b/i.test(s) ? { status: "unavailable", message: "Structured logs are not available on this backend." } : { status: "failed", message: `Structured logs failed: ${s}` };
}
function ne(e, s) {
  return e.sequence !== s.sequence ? e.sequence - s.sequence : Date.parse(e.timestamp) - Date.parse(s.timestamp);
}
function qe(e) {
  return e.map((s) => JSON.stringify({
    sequence: s.sequence,
    timestamp: s.timestamp,
    level: s.level,
    sourceId: s.sourceId,
    category: s.category,
    eventId: s.eventId,
    eventName: s.eventName,
    message: s.message,
    messageTemplate: s.messageTemplate,
    properties: s.properties,
    scopes: s.scopes,
    exception: s.exception,
    raw: s.raw
  })).join(`
`);
}
function Me(e = /* @__PURE__ */ new Date()) {
  return `structured-logs-${e.toISOString().replace(/\.\d{3}Z$/, "Z").replace(/[:.]/g, "-")}.jsonl`;
}
function re(e) {
  const s = new URLSearchParams();
  return e.minLevel && s.set("minLevel", e.minLevel), e.category.trim() && s.set("category", e.category.trim()), e.sourceId && s.set("source", e.sourceId), s;
}
function F(e) {
  return e ? Array.isArray(e) ? e.map((s) => ({ name: s.name, value: s.value == null ? null : String(s.value) })) : Object.entries(e).map(([s, l]) => ({ name: s, value: l == null ? null : String(l) })) : [];
}
function Pe(e) {
  return e ? Array.isArray(e) ? e.map((s) => ({
    message: s.message ?? null,
    items: F(s.items ?? [])
  })) : [{ items: F(e) }] : [];
}
function X(e) {
  return JSON.parse(e.data);
}
function Ae(e, s) {
  return y(e).localeCompare(y(s));
}
function Fe(e) {
  return e === "live" ? "live" : e === "paused" ? "paused" : e === "connecting" ? "connecting" : e === "unavailable" ? "unavailable" : e === "unauthorized" ? "unauthorized" : e === "failed" ? "failed" : "reconnecting";
}
function oe(e) {
  const s = new Date(e);
  return Number.isNaN(s.getTime()) ? e : s.toLocaleTimeString();
}
function K(e) {
  return e == null ? null : typeof e == "string" ? e : JSON.stringify(e);
}
function Je(e) {
  return `${e.timestamp} ${e.level} ${e.category} ${e.message}`;
}
function q(e, s) {
  if (typeof window > "u" || typeof window.localStorage?.getItem != "function")
    return s;
  try {
    const l = window.localStorage.getItem(e);
    return l === null ? s : l === "true";
  } catch {
    return s;
  }
}
function M(e, s) {
  if (!(typeof window > "u" || typeof window.localStorage?.setItem != "function"))
    try {
      window.localStorage.setItem(e, String(s));
    } catch {
    }
}
export {
  Re as StructuredLogsPage,
  Ie as StructuredLogsPanel,
  $ as appendStructuredLogEntries,
  V as classifyStructuredLogError,
  ne as compareStructuredLogEntries,
  Te as createRecentPath,
  De as createStreamUrl,
  qe as createStructuredLogExportContent,
  Me as createStructuredLogExportFilename,
  y as formatStructuredLogSourceLabel,
  $e as normalizeLogLevel,
  G as normalizeStructuredLogEntry,
  ze as register
};
