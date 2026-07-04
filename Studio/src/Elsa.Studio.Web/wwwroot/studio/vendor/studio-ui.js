import { j as a } from "./chunks/jsx-runtime.js";
import { useId as f, useCallback as j } from "react";
function w({
  columns: e,
  items: s,
  getKey: t,
  selectedKey: n,
  onSelect: i,
  gridColumns: o,
  emptyState: d
}) {
  const r = !!i, c = s.some((l) => t(l) === n) ? n : s.length > 0 ? t(s[0]) : void 0;
  return /* @__PURE__ */ a.jsxs(
    "div",
    {
      className: "studio-data-grid",
      role: "table",
      style: o ? { "--studio-grid-columns": o } : void 0,
      children: [
        /* @__PURE__ */ a.jsx("div", { className: "studio-data-grid-header", role: "row", children: e.map((l) => /* @__PURE__ */ a.jsx("span", { role: "columnheader", children: l.header }, l.id)) }),
        s.length === 0 && d !== void 0 ? /* @__PURE__ */ a.jsx("div", { className: "studio-data-grid-empty", role: "row", children: /* @__PURE__ */ a.jsx("span", { role: "cell", className: "studio-data-grid-empty-cell", children: d }) }) : null,
        s.map((l) => {
          const p = t(l), x = p === n, h = r ? () => i?.(l) : void 0;
          return /* @__PURE__ */ a.jsx(
            "div",
            {
              className: x ? "studio-data-grid-row selected" : "studio-data-grid-row",
              role: "row",
              "aria-selected": r ? x : void 0,
              tabIndex: r ? p === c ? 0 : -1 : void 0,
              onClick: h,
              onKeyDown: r ? (u) => {
                (u.key === "Enter" || u.key === " ") && (u.preventDefault(), h?.());
              } : void 0,
              children: e.map((u) => /* @__PURE__ */ a.jsx("span", { role: "cell", children: u.render(l) }, u.id))
            },
            p
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
  const t = e === "danger" || e === "warning" ? "alert" : "status";
  return /* @__PURE__ */ a.jsx("div", { className: "studio-alert", role: t, "data-tone": e, children: s });
}
function y({
  icon: e,
  children: s
}) {
  return /* @__PURE__ */ a.jsxs("div", { className: "studio-empty-state", children: [
    e,
    /* @__PURE__ */ a.jsx("span", { children: s })
  ] });
}
function T({
  tone: e = "neutral",
  children: s
}) {
  return /* @__PURE__ */ a.jsx("span", { className: "studio-status-chip", "data-tone": e, children: s });
}
function S({
  label: e,
  description: s,
  meta: t,
  children: n
}) {
  return /* @__PURE__ */ a.jsxs("label", { className: "studio-field", children: [
    /* @__PURE__ */ a.jsxs("span", { className: "studio-field-label", children: [
      /* @__PURE__ */ a.jsx("strong", { children: e }),
      t
    ] }),
    s ? /* @__PURE__ */ a.jsx("small", { children: s }) : null,
    n
  ] });
}
function A({
  title: e,
  eyebrow: s,
  actions: t,
  children: n
}) {
  return /* @__PURE__ */ a.jsxs("aside", { className: "studio-inspector", children: [
    /* @__PURE__ */ a.jsxs("div", { className: "studio-inspector-heading", children: [
      /* @__PURE__ */ a.jsxs("div", { children: [
        s ? /* @__PURE__ */ a.jsx("span", { children: s }) : null,
        /* @__PURE__ */ a.jsx("h3", { children: e })
      ] }),
      t ? /* @__PURE__ */ a.jsx("div", { className: "studio-inspector-actions", children: t }) : null
    ] }),
    n
  ] });
}
function m(e, s, t) {
  return j(
    (n) => {
      if (e.length === 0)
        return;
      let i = null;
      const o = Math.max(0, e.indexOf(s));
      switch (n.key) {
        case "ArrowRight":
        case "ArrowDown":
          i = (o + 1) % e.length;
          break;
        case "ArrowLeft":
        case "ArrowUp":
          i = (o - 1 + e.length) % e.length;
          break;
        case "Home":
          i = 0;
          break;
        case "End":
          i = e.length - 1;
          break;
        default:
          return;
      }
      n.preventDefault();
      const d = e[i];
      t(d), (n.currentTarget.closest('[role="tablist"]') ?? n.currentTarget).querySelector(`[data-tab-id="${g(d)}"]`)?.focus();
    },
    [e, s, t]
  );
}
function b(e, s) {
  const t = s.replace(/[^a-zA-Z0-9_-]/g, "-");
  return { tabId: `${e}-tab-${t}`, panelId: `${e}-panel-${t}` };
}
function g(e) {
  const s = typeof globalThis < "u" ? globalThis.CSS : void 0;
  return s?.escape ? s.escape(e) : e.replace(/["\\]/g, "\\$&");
}
function I({
  tabs: e,
  activeTab: s,
  onSelect: t,
  ariaLabel: n
}) {
  const i = f(), o = e.map((r) => r.id), d = m(o, s, t);
  return /* @__PURE__ */ a.jsx("div", { className: "studio-tabs", role: "tablist", "aria-label": n, onKeyDown: d, children: e.map((r) => {
    const c = r.id === s, l = b(i, r.id);
    return /* @__PURE__ */ a.jsx(
      "button",
      {
        id: l.tabId,
        "data-tab-id": r.id,
        type: "button",
        role: "tab",
        "aria-selected": c,
        "aria-controls": l.panelId,
        tabIndex: c ? 0 : -1,
        className: c ? "active" : "",
        onClick: () => t(r.id),
        children: r.label
      },
      r.id
    );
  }) });
}
function D({
  baseId: e,
  tabId: s,
  children: t,
  className: n
}) {
  const i = b(e, s);
  return /* @__PURE__ */ a.jsx("div", { id: i.panelId, role: "tabpanel", "aria-labelledby": i.tabId, tabIndex: 0, className: n, children: t });
}
function E({ children: e }) {
  return /* @__PURE__ */ a.jsx("div", { className: "studio-toolbar", children: e });
}
function $({ children: e }) {
  return /* @__PURE__ */ a.jsx("div", { className: "studio-toolbar-group", children: e });
}
export {
  y as EmptyState,
  T as StatusChip,
  k as StudioAlert,
  w as StudioDataGrid,
  S as StudioField,
  A as StudioInspector,
  D as StudioTabPanel,
  I as StudioTabs,
  E as StudioToolbar,
  $ as StudioToolbarGroup,
  b as tabElementIds,
  m as useTablistKeyboard
};
