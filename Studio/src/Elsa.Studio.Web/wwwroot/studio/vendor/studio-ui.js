import { j as s } from "./chunks/jsx-runtime.js";
function c({
  columns: e,
  items: t,
  getKey: d,
  selectedKey: i,
  onSelect: o,
  gridColumns: r
}) {
  return /* @__PURE__ */ s.jsxs("div", { className: "studio-data-grid", role: "table", style: r ? { "--studio-grid-columns": r } : void 0, children: [
    /* @__PURE__ */ s.jsx("div", { className: "studio-data-grid-header", role: "row", children: e.map((a) => /* @__PURE__ */ s.jsx("span", { role: "columnheader", children: a.header }, a.id)) }),
    t.map((a) => {
      const l = d(a);
      return /* @__PURE__ */ s.jsx(
        "button",
        {
          type: "button",
          className: l === i ? "studio-data-grid-row selected" : "studio-data-grid-row",
          role: "row",
          onClick: () => o?.(a),
          children: e.map((n) => /* @__PURE__ */ s.jsx("span", { role: "cell", children: n.render(a) }, n.id))
        },
        l
      );
    })
  ] });
}
function x({
  tone: e = "info",
  children: t
}) {
  return /* @__PURE__ */ s.jsx("div", { className: "studio-alert", "data-tone": e, children: t });
}
function h({
  icon: e,
  children: t
}) {
  return /* @__PURE__ */ s.jsxs("div", { className: "studio-empty-state", children: [
    e,
    /* @__PURE__ */ s.jsx("span", { children: t })
  ] });
}
function j({
  tone: e = "neutral",
  children: t
}) {
  return /* @__PURE__ */ s.jsx("span", { className: "studio-status-chip", "data-tone": e, children: t });
}
function p({
  label: e,
  description: t,
  meta: d,
  children: i
}) {
  return /* @__PURE__ */ s.jsxs("label", { className: "studio-field", children: [
    /* @__PURE__ */ s.jsxs("span", { className: "studio-field-label", children: [
      /* @__PURE__ */ s.jsx("strong", { children: e }),
      d
    ] }),
    t ? /* @__PURE__ */ s.jsx("small", { children: t }) : null,
    i
  ] });
}
function m({
  title: e,
  eyebrow: t,
  actions: d,
  children: i
}) {
  return /* @__PURE__ */ s.jsxs("aside", { className: "studio-inspector", children: [
    /* @__PURE__ */ s.jsxs("div", { className: "studio-inspector-heading", children: [
      /* @__PURE__ */ s.jsxs("div", { children: [
        t ? /* @__PURE__ */ s.jsx("span", { children: t }) : null,
        /* @__PURE__ */ s.jsx("h3", { children: e })
      ] }),
      d ? /* @__PURE__ */ s.jsx("div", { className: "studio-inspector-actions", children: d }) : null
    ] }),
    i
  ] });
}
function N({
  tabs: e,
  activeTab: t,
  onSelect: d
}) {
  return /* @__PURE__ */ s.jsx("div", { className: "studio-tabs", role: "tablist", children: e.map((i) => /* @__PURE__ */ s.jsx(
    "button",
    {
      type: "button",
      role: "tab",
      "aria-selected": i.id === t,
      className: i.id === t ? "active" : "",
      onClick: () => d(i.id),
      children: i.label
    },
    i.id
  )) });
}
function f({ children: e }) {
  return /* @__PURE__ */ s.jsx("div", { className: "studio-toolbar", children: e });
}
function v({ children: e }) {
  return /* @__PURE__ */ s.jsx("div", { className: "studio-toolbar-group", children: e });
}
export {
  h as EmptyState,
  j as StatusChip,
  x as StudioAlert,
  c as StudioDataGrid,
  p as StudioField,
  m as StudioInspector,
  N as StudioTabs,
  f as StudioToolbar,
  v as StudioToolbarGroup
};
