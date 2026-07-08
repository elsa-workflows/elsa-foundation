import { j as e } from "./chunks/jsx-runtime.js";
import f, { useId as m, useCallback as b } from "react";
function y({
  columns: s,
  items: t,
  getKey: a,
  selectedKey: n,
  onSelect: i,
  gridColumns: r,
  emptyState: c
}) {
  const l = !!i, u = t.some((o) => a(o) === n) ? n : t.length > 0 ? a(t[0]) : void 0;
  return /* @__PURE__ */ e.jsxs(
    "div",
    {
      className: "studio-data-grid",
      role: "table",
      style: r ? { "--studio-grid-columns": r } : void 0,
      children: [
        /* @__PURE__ */ e.jsx("div", { className: "studio-data-grid-header", role: "row", children: s.map((o) => /* @__PURE__ */ e.jsx("span", { role: "columnheader", children: o.header }, o.id)) }),
        t.length === 0 && c !== void 0 ? /* @__PURE__ */ e.jsx("div", { className: "studio-data-grid-empty", role: "row", children: /* @__PURE__ */ e.jsx("span", { role: "cell", className: "studio-data-grid-empty-cell", children: c }) }) : null,
        t.map((o) => {
          const d = a(o), p = d === n, h = l ? () => i?.(o) : void 0;
          return /* @__PURE__ */ e.jsx(
            "div",
            {
              className: p ? "studio-data-grid-row selected" : "studio-data-grid-row",
              role: "row",
              "aria-selected": l ? p : void 0,
              tabIndex: l ? d === u ? 0 : -1 : void 0,
              onClick: h,
              onKeyDown: l ? (x) => {
                (x.key === "Enter" || x.key === " ") && (x.preventDefault(), h?.());
              } : void 0,
              children: s.map((x) => /* @__PURE__ */ e.jsx("span", { role: "cell", children: x.render(o) }, x.id))
            },
            d
          );
        })
      ]
    }
  );
}
function $({
  tone: s = "info",
  children: t
}) {
  const a = s === "danger" || s === "warning" ? "alert" : "status";
  return /* @__PURE__ */ e.jsx("div", { className: "studio-alert", role: a, "data-tone": s, children: t });
}
function S({
  icon: s,
  children: t
}) {
  return /* @__PURE__ */ e.jsxs("div", { className: "studio-empty-state", children: [
    s,
    /* @__PURE__ */ e.jsx("span", { children: t })
  ] });
}
function T({
  tone: s = "neutral",
  children: t
}) {
  return /* @__PURE__ */ e.jsx("span", { className: "studio-status-chip", "data-tone": s, children: t });
}
function I({
  tone: s = "neutral",
  children: t
}) {
  return /* @__PURE__ */ e.jsx("span", { className: "studio-status-pill", "data-tone": s, children: t });
}
function A({
  variant: s = "default",
  size: t = "md",
  className: a,
  type: n = "button",
  ...i
}) {
  return /* @__PURE__ */ e.jsx(
    "button",
    {
      type: n,
      className: a ? `studio-button ${a}` : "studio-button",
      "data-variant": s,
      "data-size": t,
      ...i
    }
  );
}
function C({
  label: s,
  description: t,
  meta: a,
  children: n
}) {
  return /* @__PURE__ */ e.jsxs("label", { className: "studio-field", children: [
    /* @__PURE__ */ e.jsxs("span", { className: "studio-field-label", children: [
      /* @__PURE__ */ e.jsx("strong", { children: s }),
      a
    ] }),
    t ? /* @__PURE__ */ e.jsx("small", { children: t }) : null,
    n
  ] });
}
function D({
  className: s,
  ...t
}) {
  return /* @__PURE__ */ e.jsxs("div", { className: s ? `studio-search ${s}` : "studio-search", children: [
    /* @__PURE__ */ e.jsxs("svg", { className: "studio-search-icon", viewBox: "0 0 16 16", "aria-hidden": "true", focusable: "false", children: [
      /* @__PURE__ */ e.jsx("circle", { cx: "7", cy: "7", r: "4.5", fill: "none", stroke: "currentColor", strokeWidth: "1.5" }),
      /* @__PURE__ */ e.jsx("line", { x1: "10.5", y1: "10.5", x2: "14", y2: "14", stroke: "currentColor", strokeWidth: "1.5", strokeLinecap: "round" })
    ] }),
    /* @__PURE__ */ e.jsx("input", { type: "search", role: "searchbox", className: "studio-search-input", ...t })
  ] });
}
function L({
  title: s,
  eyebrow: t,
  actions: a,
  children: n
}) {
  return /* @__PURE__ */ e.jsxs("aside", { className: "studio-inspector", children: [
    /* @__PURE__ */ e.jsxs("div", { className: "studio-inspector-heading", children: [
      /* @__PURE__ */ e.jsxs("div", { children: [
        t ? /* @__PURE__ */ e.jsx("span", { children: t }) : null,
        /* @__PURE__ */ e.jsx("h3", { children: s })
      ] }),
      a ? /* @__PURE__ */ e.jsx("div", { className: "studio-inspector-actions", children: a }) : null
    ] }),
    n
  ] });
}
function v(s, t, a) {
  return b(
    (n) => {
      if (s.length === 0)
        return;
      let i = null;
      const r = Math.max(0, s.indexOf(t));
      switch (n.key) {
        case "ArrowRight":
        case "ArrowDown":
          i = (r + 1) % s.length;
          break;
        case "ArrowLeft":
        case "ArrowUp":
          i = (r - 1 + s.length) % s.length;
          break;
        case "Home":
          i = 0;
          break;
        case "End":
          i = s.length - 1;
          break;
        default:
          return;
      }
      n.preventDefault();
      const c = s[i];
      a(c), (n.currentTarget.closest('[role="tablist"]') ?? n.currentTarget).querySelector(`[data-tab-id="${N(c)}"]`)?.focus();
    },
    [s, t, a]
  );
}
function j(s, t) {
  return { tabId: `${s}-tab-${t}`, panelId: `${s}-panel-${t}` };
}
function N(s) {
  const t = typeof globalThis < "u" ? globalThis.CSS : void 0;
  return t?.escape ? t.escape(s) : s.replace(/["\\]/g, "\\$&");
}
function E({
  tabs: s,
  activeTab: t,
  onSelect: a,
  ariaLabel: n
}) {
  const i = m(), r = s.map((l) => l.id), c = v(r, t, a);
  return /* @__PURE__ */ e.jsx("div", { className: "studio-tabs", role: "tablist", "aria-label": n, onKeyDown: c, children: s.map((l, u) => {
    const o = l.id === t, d = j(i, u);
    return /* @__PURE__ */ e.jsx(
      "button",
      {
        id: d.tabId,
        "data-tab-id": l.id,
        type: "button",
        role: "tab",
        "aria-selected": o,
        "aria-controls": d.panelId,
        tabIndex: o ? 0 : -1,
        className: o ? "active" : "",
        onClick: () => a(l.id),
        children: l.label
      },
      l.id
    );
  }) });
}
function B({
  baseId: s,
  index: t,
  children: a,
  className: n
}) {
  const i = j(s, t);
  return /* @__PURE__ */ e.jsx("div", { id: i.panelId, role: "tabpanel", "aria-labelledby": i.tabId, tabIndex: 0, className: n, children: a });
}
function M({ children: s }) {
  return /* @__PURE__ */ e.jsx("div", { className: "studio-toolbar", children: s });
}
function R({ children: s }) {
  return /* @__PURE__ */ e.jsx("div", { className: "studio-toolbar-group", children: s });
}
function F({
  className: s,
  children: t,
  ...a
}) {
  return /* @__PURE__ */ e.jsx("div", { className: s ? `studio-list ${s}` : "studio-list", role: "list", ...a, children: t });
}
function W({
  title: s,
  subtitle: t,
  leading: a,
  trailing: n,
  selected: i,
  onSelect: r,
  className: c,
  ...l
}) {
  const u = !!r, o = ["studio-list-row"];
  return i && o.push("selected"), c && o.push(c), /* @__PURE__ */ e.jsxs(
    "div",
    {
      className: o.join(" "),
      role: "listitem",
      "aria-selected": u ? !!i : void 0,
      tabIndex: u ? 0 : void 0,
      onClick: r,
      onKeyDown: u ? (d) => {
        (d.key === "Enter" || d.key === " ") && (d.preventDefault(), r?.());
      } : void 0,
      ...l,
      children: [
        a !== void 0 ? /* @__PURE__ */ e.jsx("span", { className: "studio-list-row-leading", children: a }) : null,
        /* @__PURE__ */ e.jsxs("span", { className: "studio-list-row-body", children: [
          /* @__PURE__ */ e.jsx("span", { className: "studio-list-row-title", children: s }),
          t !== void 0 ? /* @__PURE__ */ e.jsx("span", { className: "studio-list-row-subtitle", children: t }) : null
        ] }),
        n !== void 0 ? /* @__PURE__ */ e.jsx("span", { className: "studio-list-row-trailing", children: n }) : null
      ]
    }
  );
}
function k({
  points: s,
  className: t,
  width: a = 96,
  height: n = 28
}) {
  const i = f.useMemo(() => {
    if (s.length < 2) return `M0 ${n / 2} L${a} ${n / 2}`;
    const r = Math.min(...s), l = Math.max(...s) - r || 1, u = a / (s.length - 1);
    return s.map((o, d) => {
      const p = d * u, h = n - 2 - (o - r) / l * (n - 4);
      return `${d === 0 ? "M" : "L"}${p.toFixed(2)} ${h.toFixed(2)}`;
    }).join(" ");
  }, [s, a, n]);
  return /* @__PURE__ */ e.jsx(
    "svg",
    {
      className: t ? `studio-sparkline ${t}` : "studio-sparkline",
      viewBox: `0 0 ${a} ${n}`,
      preserveAspectRatio: "none",
      "aria-hidden": "true",
      focusable: "false",
      children: /* @__PURE__ */ e.jsx("path", { d: i, fill: "none", stroke: "var(--chart-1)", strokeWidth: "1.5", strokeLinecap: "round", strokeLinejoin: "round" })
    }
  );
}
function G({
  label: s,
  value: t,
  delta: a,
  deltaTone: n,
  trend: i,
  className: r
}) {
  return /* @__PURE__ */ e.jsxs("div", { className: r ? `studio-stat-tile ${r}` : "studio-stat-tile", children: [
    /* @__PURE__ */ e.jsx("span", { className: "studio-stat-tile-label", children: s }),
    /* @__PURE__ */ e.jsx("span", { className: "studio-stat-tile-value", children: t }),
    a !== void 0 ? /* @__PURE__ */ e.jsx("span", { className: "studio-stat-tile-delta", "data-tone": n ?? "neutral", children: a }) : null,
    i && i.length > 0 ? /* @__PURE__ */ e.jsx(k, { points: i, className: "studio-stat-tile-trend" }) : null
  ] });
}
export {
  S as EmptyState,
  T as StatusChip,
  I as StatusPill,
  $ as StudioAlert,
  A as StudioButton,
  y as StudioDataGrid,
  C as StudioField,
  L as StudioInspector,
  F as StudioListContainer,
  W as StudioListRow,
  D as StudioSearchInput,
  k as StudioSparkline,
  G as StudioStatTile,
  B as StudioTabPanel,
  E as StudioTabs,
  M as StudioToolbar,
  R as StudioToolbarGroup,
  j as tabElementIds,
  v as useTablistKeyboard
};
