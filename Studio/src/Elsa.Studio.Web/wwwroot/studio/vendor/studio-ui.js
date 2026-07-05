import { j as t } from "./chunks/jsx-runtime.js";
import { useId as f, useCallback as j } from "react";
function w({
  columns: e,
  items: s,
  getKey: a,
  selectedKey: r,
  onSelect: n,
  gridColumns: o,
  emptyState: d
}) {
  const i = !!n, p = s.some((l) => a(l) === r) ? r : s.length > 0 ? a(s[0]) : void 0;
  return /* @__PURE__ */ t.jsxs(
    "div",
    {
      className: "studio-data-grid",
      role: "table",
      style: o ? { "--studio-grid-columns": o } : void 0,
      children: [
        /* @__PURE__ */ t.jsx("div", { className: "studio-data-grid-header", role: "row", children: e.map((l) => /* @__PURE__ */ t.jsx("span", { role: "columnheader", children: l.header }, l.id)) }),
        s.length === 0 && d !== void 0 ? /* @__PURE__ */ t.jsx("div", { className: "studio-data-grid-empty", role: "row", children: /* @__PURE__ */ t.jsx("span", { role: "cell", className: "studio-data-grid-empty-cell", children: d }) }) : null,
        s.map((l) => {
          const c = a(l), h = c === r, x = i ? () => n?.(l) : void 0;
          return /* @__PURE__ */ t.jsx(
            "div",
            {
              className: h ? "studio-data-grid-row selected" : "studio-data-grid-row",
              role: "row",
              "aria-selected": i ? h : void 0,
              tabIndex: i ? c === p ? 0 : -1 : void 0,
              onClick: x,
              onKeyDown: i ? (u) => {
                (u.key === "Enter" || u.key === " ") && (u.preventDefault(), x?.());
              } : void 0,
              children: e.map((u) => /* @__PURE__ */ t.jsx("span", { role: "cell", children: u.render(l) }, u.id))
            },
            c
          );
        })
      ]
    }
  );
}
function k({
  tone: e = "info",
  children: s
}) {
  const a = e === "danger" || e === "warning" ? "alert" : "status";
  return /* @__PURE__ */ t.jsx("div", { className: "studio-alert", role: a, "data-tone": e, children: s });
}
function y({
  icon: e,
  children: s
}) {
  return /* @__PURE__ */ t.jsxs("div", { className: "studio-empty-state", children: [
    e,
    /* @__PURE__ */ t.jsx("span", { children: s })
  ] });
}
function T({
  tone: e = "neutral",
  children: s
}) {
  return /* @__PURE__ */ t.jsx("span", { className: "studio-status-chip", "data-tone": e, children: s });
}
function S({
  label: e,
  description: s,
  meta: a,
  children: r
}) {
  return /* @__PURE__ */ t.jsxs("label", { className: "studio-field", children: [
    /* @__PURE__ */ t.jsxs("span", { className: "studio-field-label", children: [
      /* @__PURE__ */ t.jsx("strong", { children: e }),
      a
    ] }),
    s ? /* @__PURE__ */ t.jsx("small", { children: s }) : null,
    r
  ] });
}
function I({
  title: e,
  eyebrow: s,
  actions: a,
  children: r
}) {
  return /* @__PURE__ */ t.jsxs("aside", { className: "studio-inspector", children: [
    /* @__PURE__ */ t.jsxs("div", { className: "studio-inspector-heading", children: [
      /* @__PURE__ */ t.jsxs("div", { children: [
        s ? /* @__PURE__ */ t.jsx("span", { children: s }) : null,
        /* @__PURE__ */ t.jsx("h3", { children: e })
      ] }),
      a ? /* @__PURE__ */ t.jsx("div", { className: "studio-inspector-actions", children: a }) : null
    ] }),
    r
  ] });
}
function m(e, s, a) {
  return j(
    (r) => {
      if (e.length === 0)
        return;
      let n = null;
      const o = Math.max(0, e.indexOf(s));
      switch (r.key) {
        case "ArrowRight":
        case "ArrowDown":
          n = (o + 1) % e.length;
          break;
        case "ArrowLeft":
        case "ArrowUp":
          n = (o - 1 + e.length) % e.length;
          break;
        case "Home":
          n = 0;
          break;
        case "End":
          n = e.length - 1;
          break;
        default:
          return;
      }
      r.preventDefault();
      const d = e[n];
      a(d), (r.currentTarget.closest('[role="tablist"]') ?? r.currentTarget).querySelector(`[data-tab-id="${g(d)}"]`)?.focus();
    },
    [e, s, a]
  );
}
function b(e, s) {
  return { tabId: `${e}-tab-${s}`, panelId: `${e}-panel-${s}` };
}
function g(e) {
  const s = typeof globalThis < "u" ? globalThis.CSS : void 0;
  return s?.escape ? s.escape(e) : e.replace(/["\\]/g, "\\$&");
}
function A({
  tabs: e,
  activeTab: s,
  onSelect: a,
  ariaLabel: r
}) {
  const n = f(), o = e.map((i) => i.id), d = m(o, s, a);
  return /* @__PURE__ */ t.jsx("div", { className: "studio-tabs", role: "tablist", "aria-label": r, onKeyDown: d, children: e.map((i, p) => {
    const l = i.id === s, c = b(n, p);
    return /* @__PURE__ */ t.jsx(
      "button",
      {
        id: c.tabId,
        "data-tab-id": i.id,
        type: "button",
        role: "tab",
        "aria-selected": l,
        "aria-controls": c.panelId,
        tabIndex: l ? 0 : -1,
        className: l ? "active" : "",
        onClick: () => a(i.id),
        children: i.label
      },
      i.id
    );
  }) });
}
function D({
  baseId: e,
  index: s,
  children: a,
  className: r
}) {
  const n = b(e, s);
  return /* @__PURE__ */ t.jsx("div", { id: n.panelId, role: "tabpanel", "aria-labelledby": n.tabId, tabIndex: 0, className: r, children: a });
}
function E({ children: e }) {
  return /* @__PURE__ */ t.jsx("div", { className: "studio-toolbar", children: e });
}
function $({ children: e }) {
  return /* @__PURE__ */ t.jsx("div", { className: "studio-toolbar-group", children: e });
}
export {
  y as EmptyState,
  T as StatusChip,
  k as StudioAlert,
  w as StudioDataGrid,
  S as StudioField,
  I as StudioInspector,
  D as StudioTabPanel,
  A as StudioTabs,
  E as StudioToolbar,
  $ as StudioToolbarGroup,
  b as tabElementIds,
  m as useTablistKeyboard
};
