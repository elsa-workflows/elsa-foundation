import { useState as p, useEffect as I, useMemo as Q } from "react";
var N = { exports: {} }, j = {};
var M;
function X() {
  if (M) return j;
  M = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), n = /* @__PURE__ */ Symbol.for("react.fragment");
  function s(r, l, i) {
    var a = null;
    if (i !== void 0 && (a = "" + i), l.key !== void 0 && (a = "" + l.key), "key" in l) {
      i = {};
      for (var d in l)
        d !== "key" && (i[d] = l[d]);
    } else i = l;
    return l = i.ref, {
      $$typeof: e,
      type: r,
      key: a,
      ref: l !== void 0 ? l : null,
      props: i
    };
  }
  return j.Fragment = n, j.jsx = s, j.jsxs = s, j;
}
var R;
function Z() {
  return R || (R = 1, N.exports = X()), N.exports;
}
var t = Z();
const O = "/_elsa/studio/diagnostics/otel", E = {
  serviceName: "",
  operation: "",
  status: "",
  traceId: "",
  take: 100
};
let y;
function je(e) {
  y = e, e.navigation.add({
    id: "open-telemetry",
    label: "OpenTelemetry",
    path: "/diagnostics/open-telemetry",
    activePathPrefix: "/diagnostics/open-telemetry",
    order: 880,
    iconColor: "#22c55e",
    parentId: "diagnostics"
  }), e.routes.add({
    id: "open-telemetry",
    label: "OpenTelemetry",
    path: "/diagnostics/open-telemetry",
    component: ee
  }), e.panels.add({
    id: "open-telemetry",
    title: "OpenTelemetry",
    order: 1020,
    component: K
  });
}
function K() {
  const e = P(), n = e.selectedTrace, s = n?.spans.find((r) => r.spanId === e.selectedSpanId) ?? n?.spans[0] ?? null;
  return /* @__PURE__ */ t.jsx("section", { className: "otel-panel", children: /* @__PURE__ */ t.jsx(J, { state: e, selectedTrace: n, selectedSpan: s }) });
}
function ee() {
  const e = P(), n = e.selectedTrace, s = n?.spans.find((r) => r.spanId === e.selectedSpanId) ?? n?.spans[0] ?? null;
  return /* @__PURE__ */ t.jsxs("section", { className: "otel-page", children: [
    /* @__PURE__ */ t.jsxs("header", { className: "otel-header", children: [
      /* @__PURE__ */ t.jsxs("div", { children: [
        /* @__PURE__ */ t.jsx("h2", { children: "OpenTelemetry" }),
        /* @__PURE__ */ t.jsx("p", { children: e.message })
      ] }),
      /* @__PURE__ */ t.jsxs("div", { className: "otel-header-summary", children: [
        /* @__PURE__ */ t.jsx(S, { label: "Traces", value: e.overview?.traceCount }),
        /* @__PURE__ */ t.jsx(S, { label: "Spans", value: e.overview?.spanCount }),
        /* @__PURE__ */ t.jsx(S, { label: "Metrics", value: e.overview?.metricCount }),
        /* @__PURE__ */ t.jsx(S, { label: "Logs", value: e.overview?.logCount })
      ] })
    ] }),
    /* @__PURE__ */ t.jsx("div", { className: "otel-tabs", role: "tablist", "aria-label": "OpenTelemetry signals", children: ["overview", "traces", "metrics", "logs"].map((r) => /* @__PURE__ */ t.jsx("button", { type: "button", role: "tab", "aria-selected": e.activeTab === r, className: e.activeTab === r ? "active" : "", onClick: () => e.setActiveTab(r), children: ve(r) }, r)) }),
    e.activeTab === "overview" ? /* @__PURE__ */ t.jsx(te, { state: e }) : null,
    e.activeTab === "traces" ? /* @__PURE__ */ t.jsx(J, { state: e, selectedTrace: n, selectedSpan: s }) : null,
    e.activeTab === "metrics" ? /* @__PURE__ */ t.jsx(L, { title: "Metrics", status: e.status, message: "Metric charts will appear here when the OpenTelemetry backend exposes metric instruments and datapoints." }) : null,
    e.activeTab === "logs" ? /* @__PURE__ */ t.jsx(L, { title: "Logs", status: e.status, message: "OpenTelemetry log records will appear here when the backend exposes the log signal. Structured application logs are available under Diagnostics / Structured logs." }) : null
  ] });
}
function P() {
  const [e, n] = p("traces"), [s, r] = p(E), [l, i] = p(null), [a, d] = p([]), [m, f] = p(null), [v, D] = p(null), [q, w] = p(null), [B, T] = p("loading"), [H, b] = p("Loading OpenTelemetry diagnostics.");
  I(() => {
    let c = !1;
    async function h() {
      try {
        const o = await y.backend.http.getJson(`${O}/overview`);
        c || i(o);
      } catch {
        c || i(null);
      }
    }
    return h(), () => {
      c = !0;
    };
  }, []), I(() => {
    let c = !1;
    T("loading"), b("Loading trace summaries.");
    async function h() {
      try {
        const o = await y.backend.http.getJson(ie(s));
        if (c) return;
        const u = o.map(U).sort(me);
        d(u), T("ready"), b(`${u.length} trace${u.length === 1 ? "" : "s"} loaded.`), f((V) => V ?? u[0]?.traceId ?? null);
      } catch (o) {
        if (c) return;
        const u = $(o);
        T(u.status), b(u.message), d([]), f(null);
      }
    }
    return h(), () => {
      c = !0;
    };
  }, [s]), I(() => {
    let c = !1;
    if (D(null), w(null), !m) return () => {
      c = !0;
    };
    async function h() {
      try {
        const o = await y.backend.http.getJson(`${O}/traces/${encodeURIComponent(m)}`);
        if (c) return;
        const u = oe(o);
        D(u), w(u.spans[0]?.spanId ?? null);
      } catch (o) {
        if (c) return;
        const u = $(o);
        T(u.status), b(u.message);
      }
    }
    return h(), () => {
      c = !0;
    };
  }, [m]);
  function W() {
    r(E);
  }
  async function Y() {
    !v || !navigator.clipboard || await navigator.clipboard.writeText(JSON.stringify(v, null, 2));
  }
  function G() {
    if (!v || typeof document > "u") return;
    const c = new Blob([JSON.stringify(v, null, 2)], { type: "application/json;charset=utf-8" }), h = URL.createObjectURL(c), o = document.createElement("a");
    o.href = h, o.download = `trace-${v.traceId}.json`, o.rel = "noopener", document.body.appendChild(o), o.click(), o.remove(), window.setTimeout(() => URL.revokeObjectURL(h), 0);
  }
  return {
    activeTab: e,
    setActiveTab: n,
    filters: s,
    setFilters: r,
    resetFilters: W,
    overview: l,
    traces: a,
    selectedTraceId: m,
    setSelectedTraceId: f,
    selectedTrace: v,
    selectedSpanId: q,
    setSelectedSpanId: w,
    status: B,
    message: H,
    copyTrace: Y,
    downloadTrace: G
  };
}
function te({ state: e }) {
  const n = e.overview?.sources ?? [];
  return /* @__PURE__ */ t.jsxs("div", { className: "otel-overview", children: [
    /* @__PURE__ */ t.jsxs("section", { children: [
      /* @__PURE__ */ t.jsx("h3", { children: "Signal Health" }),
      /* @__PURE__ */ t.jsxs("div", { className: "otel-signal-cards", children: [
        /* @__PURE__ */ t.jsx(C, { title: "Traces", detail: "Distributed request and workflow execution spans.", status: e.status, count: e.overview?.traceCount }),
        /* @__PURE__ */ t.jsx(C, { title: "Metrics", detail: "Measurements, counters, gauges, and histograms.", status: e.overview?.metricCount ? "ready" : "unavailable", count: e.overview?.metricCount }),
        /* @__PURE__ */ t.jsx(C, { title: "Logs", detail: "OpenTelemetry log records correlated by trace/span IDs.", status: e.overview?.logCount ? "ready" : "unavailable", count: e.overview?.logCount })
      ] })
    ] }),
    /* @__PURE__ */ t.jsxs("section", { children: [
      /* @__PURE__ */ t.jsx("h3", { children: "Sources" }),
      n.length === 0 ? /* @__PURE__ */ t.jsx("p", { className: "otel-muted", children: "No OpenTelemetry sources reported yet." }) : null,
      /* @__PURE__ */ t.jsx("div", { className: "otel-source-list", children: n.map((s) => /* @__PURE__ */ t.jsxs("div", { className: "otel-source-row", children: [
        /* @__PURE__ */ t.jsx("strong", { children: xe(s) }),
        /* @__PURE__ */ t.jsx("span", { children: [s.environment, s.instanceId, s.lastSeen ? `last seen ${_(s.lastSeen)}` : null].filter(Boolean).join(" · ") })
      ] }, s.id)) })
    ] })
  ] });
}
function J({ state: e, selectedTrace: n, selectedSpan: s }) {
  return /* @__PURE__ */ t.jsxs("div", { className: "otel-traces-layout", children: [
    /* @__PURE__ */ t.jsxs("section", { className: "otel-trace-list", children: [
      /* @__PURE__ */ t.jsx(ne, { state: e }),
      /* @__PURE__ */ t.jsxs("div", { className: "otel-trace-table", role: "table", "aria-label": "Trace summaries", children: [
        /* @__PURE__ */ t.jsxs("div", { className: "otel-trace-row header", role: "row", children: [
          /* @__PURE__ */ t.jsx("span", { children: "Start" }),
          /* @__PURE__ */ t.jsx("span", { children: "Service" }),
          /* @__PURE__ */ t.jsx("span", { children: "Operation" }),
          /* @__PURE__ */ t.jsx("span", { children: "Duration" }),
          /* @__PURE__ */ t.jsx("span", { children: "Spans" }),
          /* @__PURE__ */ t.jsx("span", { children: "Status" })
        ] }),
        e.traces.length === 0 ? /* @__PURE__ */ t.jsx("div", { className: "otel-empty", children: e.status === "ready" ? "No traces match the current filters." : e.message }) : null,
        e.traces.map((r) => /* @__PURE__ */ t.jsxs("button", { type: "button", role: "row", className: r.traceId === e.selectedTraceId ? "otel-trace-row selected" : "otel-trace-row", onClick: () => e.setSelectedTraceId(r.traceId), children: [
          /* @__PURE__ */ t.jsx("time", { title: r.startTime, children: he(r.startTime) }),
          /* @__PURE__ */ t.jsx("span", { title: r.serviceName, children: r.serviceName }),
          /* @__PURE__ */ t.jsx("strong", { title: r.name, children: r.name }),
          /* @__PURE__ */ t.jsx("span", { children: g(r.durationMs) }),
          /* @__PURE__ */ t.jsx("span", { children: r.spanCount }),
          /* @__PURE__ */ t.jsx(le, { status: r.status, errors: r.errorCount })
        ] }, r.traceId))
      ] })
    ] }),
    /* @__PURE__ */ t.jsx("section", { className: "otel-trace-detail", children: n ? /* @__PURE__ */ t.jsxs(t.Fragment, { children: [
      /* @__PURE__ */ t.jsx(se, { trace: n, onCopy: e.copyTrace, onDownload: e.downloadTrace }),
      /* @__PURE__ */ t.jsx(re, { trace: n, selectedSpanId: e.selectedSpanId, onSelectSpan: e.setSelectedSpanId }),
      s ? /* @__PURE__ */ t.jsx(ae, { span: s }) : null
    ] }) : /* @__PURE__ */ t.jsx("div", { className: "otel-empty", children: "Select a trace to inspect its spans." }) })
  ] });
}
function ne({ state: e }) {
  return /* @__PURE__ */ t.jsxs("div", { className: "otel-toolbar", children: [
    /* @__PURE__ */ t.jsx("input", { "aria-label": "Filter by trace ID", placeholder: "Trace ID", value: e.filters.traceId, onChange: (n) => e.setFilters({ ...e.filters, traceId: n.target.value }) }),
    /* @__PURE__ */ t.jsx("input", { "aria-label": "Filter by service", placeholder: "Service", value: e.filters.serviceName, onChange: (n) => e.setFilters({ ...e.filters, serviceName: n.target.value }) }),
    /* @__PURE__ */ t.jsx("input", { "aria-label": "Filter by operation", placeholder: "Operation", value: e.filters.operation, onChange: (n) => e.setFilters({ ...e.filters, operation: n.target.value }) }),
    /* @__PURE__ */ t.jsxs("select", { "aria-label": "Filter by span status", value: e.filters.status, onChange: (n) => e.setFilters({ ...e.filters, status: n.target.value }), children: [
      /* @__PURE__ */ t.jsx("option", { value: "", children: "All statuses" }),
      /* @__PURE__ */ t.jsx("option", { value: "Ok", children: "Ok" }),
      /* @__PURE__ */ t.jsx("option", { value: "Error", children: "Error" }),
      /* @__PURE__ */ t.jsx("option", { value: "Unset", children: "Unset" })
    ] }),
    /* @__PURE__ */ t.jsx("select", { "aria-label": "Trace result limit", value: e.filters.take, onChange: (n) => e.setFilters({ ...e.filters, take: Number(n.target.value) }), children: [50, 100, 250, 500].map((n) => /* @__PURE__ */ t.jsx("option", { value: n, children: n }, n)) }),
    /* @__PURE__ */ t.jsx("button", { type: "button", onClick: e.resetFilters, children: "Reset" })
  ] });
}
function se({ trace: e, onCopy: n, onDownload: s }) {
  return /* @__PURE__ */ t.jsxs("header", { className: "otel-trace-detail-header", children: [
    /* @__PURE__ */ t.jsxs("div", { children: [
      /* @__PURE__ */ t.jsxs("span", { children: [
        e.serviceName,
        " · ",
        g(e.durationMs),
        " · ",
        e.spanCount,
        " spans"
      ] }),
      /* @__PURE__ */ t.jsx("h3", { children: e.name }),
      /* @__PURE__ */ t.jsx("p", { children: e.traceId })
    ] }),
    /* @__PURE__ */ t.jsxs("div", { children: [
      /* @__PURE__ */ t.jsx("button", { type: "button", onClick: () => {
        n();
      }, children: "Copy JSON" }),
      /* @__PURE__ */ t.jsx("button", { type: "button", onClick: s, children: "Download" })
    ] })
  ] });
}
function re({ trace: e, selectedSpanId: n, onSelectSpan: s }) {
  const r = Q(() => de(e.spans), [e.spans]), l = Math.min(...e.spans.map((a) => Date.parse(a.startTime))), i = Math.max(1, e.durationMs);
  return /* @__PURE__ */ t.jsxs("div", { className: "otel-waterfall", "aria-label": "Trace waterfall", children: [
    /* @__PURE__ */ t.jsxs("div", { className: "otel-waterfall-scale", children: [
      /* @__PURE__ */ t.jsx("span", { children: "0ms" }),
      /* @__PURE__ */ t.jsx("span", { children: g(i) })
    ] }),
    r.map((a) => {
      const d = Math.max(0, Date.parse(a.span.startTime) - l), m = Math.max(0, Math.min(96, d / i * 100)), f = Math.max(1, Math.min(100 - m, a.span.durationMs / i * 100));
      return /* @__PURE__ */ t.jsxs("button", { type: "button", className: a.span.spanId === n ? "otel-waterfall-row selected" : "otel-waterfall-row", onClick: () => s(a.span.spanId), children: [
        /* @__PURE__ */ t.jsx("span", { className: "otel-waterfall-label", style: { paddingLeft: `${a.depth * 14}px` }, title: a.span.name, children: a.span.name }),
        /* @__PURE__ */ t.jsx("span", { className: "otel-waterfall-track", children: /* @__PURE__ */ t.jsx("span", { className: "otel-waterfall-bar", "data-status": a.span.status.toLowerCase(), style: { left: `${m}%`, width: `${f}%` } }) }),
        /* @__PURE__ */ t.jsx("span", { children: g(a.span.durationMs) })
      ] }, a.span.spanId);
    })
  ] });
}
function ae({ span: e }) {
  return /* @__PURE__ */ t.jsxs("aside", { className: "otel-span-inspector", "aria-label": "Span details", children: [
    /* @__PURE__ */ t.jsxs("header", { children: [
      /* @__PURE__ */ t.jsxs("span", { children: [
        e.kind,
        " · ",
        e.status
      ] }),
      /* @__PURE__ */ t.jsx("h3", { children: e.name })
    ] }),
    /* @__PURE__ */ t.jsxs("dl", { children: [
      /* @__PURE__ */ t.jsx(x, { name: "Span ID", value: e.spanId }),
      /* @__PURE__ */ t.jsx(x, { name: "Parent", value: e.parentSpanId }),
      /* @__PURE__ */ t.jsx(x, { name: "Service", value: e.serviceName }),
      /* @__PURE__ */ t.jsx(x, { name: "Started", value: _(e.startTime) }),
      /* @__PURE__ */ t.jsx(x, { name: "Duration", value: g(e.durationMs) }),
      /* @__PURE__ */ t.jsx(x, { name: "Status description", value: e.statusDescription })
    ] }),
    e.attributes.length > 0 ? /* @__PURE__ */ t.jsxs("section", { children: [
      /* @__PURE__ */ t.jsx("h4", { children: "Attributes" }),
      /* @__PURE__ */ t.jsx("dl", { children: e.attributes.map((n) => /* @__PURE__ */ t.jsx(x, { name: n.name, value: n.value }, n.name)) })
    ] }) : null,
    e.events.length > 0 ? /* @__PURE__ */ t.jsxs("section", { children: [
      /* @__PURE__ */ t.jsx("h4", { children: "Events" }),
      e.events.map((n, s) => /* @__PURE__ */ t.jsx("pre", { children: JSON.stringify(n, null, 2) }, s))
    ] }) : null,
    e.resource ? /* @__PURE__ */ t.jsxs("section", { children: [
      /* @__PURE__ */ t.jsx("h4", { children: "Resource" }),
      /* @__PURE__ */ t.jsx("pre", { children: JSON.stringify(e.resource, null, 2) })
    ] }) : null
  ] });
}
function L({ title: e, status: n, message: s }) {
  return /* @__PURE__ */ t.jsxs("div", { className: "otel-placeholder", children: [
    /* @__PURE__ */ t.jsx("h3", { children: e }),
    /* @__PURE__ */ t.jsx("p", { children: n === "unavailable" ? "OpenTelemetry diagnostics endpoint is unavailable." : s })
  ] });
}
function S({ label: e, value: n }) {
  return /* @__PURE__ */ t.jsxs("span", { children: [
    /* @__PURE__ */ t.jsx("strong", { children: n ?? "-" }),
    e
  ] });
}
function C({ title: e, detail: n, status: s, count: r }) {
  return /* @__PURE__ */ t.jsxs("div", { className: "otel-signal-card", "data-status": s, children: [
    /* @__PURE__ */ t.jsx("span", { children: s }),
    /* @__PURE__ */ t.jsx("strong", { children: r ?? "-" }),
    /* @__PURE__ */ t.jsx("h4", { children: e }),
    /* @__PURE__ */ t.jsx("p", { children: n })
  ] });
}
function le({ status: e, errors: n = 0 }) {
  return /* @__PURE__ */ t.jsx("span", { className: "otel-status-chip", "data-status": e.toLowerCase(), children: n > 0 ? `${n} errors` : e });
}
function x({ name: e, value: n }) {
  return n ? /* @__PURE__ */ t.jsxs(t.Fragment, { children: [
    /* @__PURE__ */ t.jsx("dt", { children: e }),
    /* @__PURE__ */ t.jsx("dd", { children: n })
  ] }) : null;
}
function ie(e) {
  const n = new URLSearchParams();
  return e.traceId.trim() && n.set("traceId", e.traceId.trim()), e.serviceName.trim() && n.set("service", e.serviceName.trim()), e.operation.trim() && n.set("operation", e.operation.trim()), e.status && n.set("status", e.status), n.set("take", String(e.take)), `${O}/traces?${n}`;
}
function U(e) {
  const n = e.startTime ?? (/* @__PURE__ */ new Date()).toISOString(), s = e.endTime ?? null;
  return {
    traceId: e.traceId ?? "",
    rootSpanId: e.rootSpanId ?? null,
    name: e.name || "Unknown operation",
    serviceName: e.serviceName || "unknown-service",
    startTime: n,
    endTime: s,
    durationMs: z(e.durationMs, n, s),
    spanCount: Number(e.spanCount ?? 0),
    errorCount: Number(e.errorCount ?? (k(e.status) === "Error" ? 1 : 0)),
    status: k(e.status)
  };
}
function oe(e) {
  const n = U(e), s = (e.spans ?? []).map((l) => ce(l, n.traceId)).sort(A), r = n.durationMs || pe(s);
  return { ...n, durationMs: r, spanCount: s.length || n.spanCount, errorCount: s.filter((l) => l.status === "Error").length || n.errorCount, spans: s };
}
function ce(e, n = "") {
  const s = e.startTime ?? (/* @__PURE__ */ new Date()).toISOString(), r = e.endTime ?? null;
  return {
    traceId: e.traceId ?? n,
    spanId: e.spanId ?? `${Date.now()}-${Math.random()}`,
    parentSpanId: e.parentSpanId ?? null,
    name: e.name || "Unnamed span",
    kind: e.kind || "Internal",
    serviceName: e.serviceName || "unknown-service",
    startTime: s,
    endTime: r,
    durationMs: z(e.durationMs, s, r),
    status: k(e.status),
    statusDescription: e.statusDescription ?? null,
    attributes: ue(e.attributes),
    events: e.events ?? [],
    links: e.links ?? [],
    resource: e.resource ?? null
  };
}
function de(e) {
  const n = /* @__PURE__ */ new Map();
  for (const a of e)
    n.set(a.parentSpanId, [...n.get(a.parentSpanId) ?? [], a]);
  for (const a of n.values()) a.sort(A);
  const s = [], r = /* @__PURE__ */ new Set(), l = n.get(null) ?? e.filter((a) => !e.some((d) => d.spanId === a.parentSpanId));
  function i(a, d) {
    if (!r.has(a.spanId)) {
      r.add(a.spanId), s.push({ span: a, depth: d });
      for (const m of n.get(a.spanId) ?? []) i(m, d + 1);
    }
  }
  for (const a of l) i(a, 0);
  for (const a of e) i(a, 0);
  return s;
}
function $(e) {
  const n = e instanceof Error ? e.message : String(e);
  return /\b(401|403)\b/.test(n) ? { status: "unauthorized", message: "Permission denied while loading OpenTelemetry diagnostics." } : /\b404\b/.test(n) ? { status: "unavailable", message: "OpenTelemetry diagnostics are unavailable. Enable the backend OpenTelemetry diagnostics module." } : { status: "failed", message: `OpenTelemetry diagnostics failed: ${n}` };
}
function k(e) {
  const n = String(e ?? "Unset").toLowerCase();
  return n === "ok" ? "Ok" : n === "error" ? "Error" : "Unset";
}
function ue(e) {
  return e ? Array.isArray(e) ? e.map((n) => ({ name: n.name, value: F(n.value) })) : Object.entries(e).map(([n, s]) => ({ name: n, value: F(s) })) : [];
}
function me(e, n) {
  return Date.parse(n.startTime) - Date.parse(e.startTime);
}
function A(e, n) {
  return Date.parse(e.startTime) - Date.parse(n.startTime);
}
function pe(e) {
  if (e.length === 0) return 0;
  const n = e.map((r) => Date.parse(r.startTime)), s = e.map((r) => r.endTime ? Date.parse(r.endTime) : Date.parse(r.startTime) + r.durationMs);
  return Math.max(0, Math.max(...s) - Math.min(...n));
}
function z(e, n, s) {
  return typeof e == "number" && Number.isFinite(e) ? Math.max(0, e) : s ? Math.max(0, Date.parse(s) - Date.parse(n)) : 0;
}
function g(e) {
  return e >= 1e3 ? `${(e / 1e3).toFixed(e >= 1e4 ? 1 : 2)}s` : `${Math.round(e * 10) / 10}ms`;
}
function he(e) {
  return new Date(e).toLocaleTimeString();
}
function _(e) {
  return new Date(e).toLocaleString();
}
function xe(e) {
  return e.displayName || e.serviceName || e.id;
}
function F(e) {
  return e == null ? "" : typeof e == "object" ? JSON.stringify(e) : String(e);
}
function ve(e) {
  return e === "overview" ? "Overview" : e === "traces" ? "Traces" : e === "metrics" ? "Metrics" : "Logs";
}
export {
  ee as OpenTelemetryPage,
  K as OpenTelemetryPanel,
  de as buildWaterfallRows,
  $ as classifyOpenTelemetryError,
  ie as createTracesPath,
  ce as normalizeSpan,
  oe as normalizeTraceDetail,
  U as normalizeTraceSummary,
  je as register
};
