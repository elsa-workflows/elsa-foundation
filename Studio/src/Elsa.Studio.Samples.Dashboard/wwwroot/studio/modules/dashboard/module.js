import p from "react";
import { StatusChip as x, StudioStatTile as j, StudioListContainer as b, StudioListRow as v, StatusPill as k } from "@elsa-workflows/studio-ui";
var l = { exports: {} }, s = {};
var c;
function R() {
  if (c) return s;
  c = 1;
  var a = /* @__PURE__ */ Symbol.for("react.transitional.element"), n = /* @__PURE__ */ Symbol.for("react.fragment");
  function t(m, d, r) {
    var i = null;
    if (r !== void 0 && (i = "" + r), d.key !== void 0 && (i = "" + d.key), "key" in d) {
      r = {};
      for (var o in d)
        o !== "key" && (r[o] = d[o]);
    } else r = d;
    return d = r.ref, {
      $$typeof: a,
      type: m,
      key: i,
      ref: d !== void 0 ? d : null,
      props: r
    };
  }
  return s.Fragment = n, s.jsx = t, s.jsxs = t, s;
}
var u;
function S() {
  return u || (u = 1, l.exports = R()), l.exports;
}
var e = S();
function w(a) {
  a.dashboardWidgets.add({
    id: "dashboard-sample-health",
    title: "Module health",
    order: 100,
    component: T
  }), a.dashboardWidgets.add({
    id: "dashboard-sample-route",
    title: "Route ownership",
    order: 110,
    component: f
  }), a.dashboardWidgets.add({
    id: "dashboard-sample-backend",
    title: "Backend endpoints",
    order: 120,
    component: g
  }), a.dashboardWidgets.add({
    id: "dashboard-sample-kit",
    title: "Shared UI kit",
    order: 130,
    component: N
  });
}
function T() {
  return /* @__PURE__ */ e.jsxs("div", { className: "admin-card dashboard-sample-card", children: [
    /* @__PURE__ */ e.jsx("h2", { children: "Dashboard module" }),
    /* @__PURE__ */ e.jsx("p", { className: "metric", children: /* @__PURE__ */ e.jsx(x, { tone: "success", children: "Loaded" }) }),
    /* @__PURE__ */ e.jsx("p", { children: "Runtime widget registration completed through the Studio SDK." })
  ] });
}
function f() {
  return /* @__PURE__ */ e.jsxs("div", { className: "admin-card dashboard-sample-card", children: [
    /* @__PURE__ */ e.jsx("h2", { children: "Route ownership" }),
    /* @__PURE__ */ e.jsx("p", { className: "metric", children: "Host" }),
    /* @__PURE__ */ e.jsx("p", { children: "Dashboard navigation and routing are provided by Studio." })
  ] });
}
function g() {
  return /* @__PURE__ */ e.jsxs("div", { className: "admin-card dashboard-sample-card", children: [
    /* @__PURE__ */ e.jsx("h2", { children: "Backend endpoints" }),
    /* @__PURE__ */ e.jsx("p", { className: "metric", children: "0" }),
    /* @__PURE__ */ e.jsx("p", { children: "This widget is contributed by a frontend-only module." })
  ] });
}
const E = [
  { label: "Executions", value: "12.4k", delta: "+8.2%", deltaTone: "success", trend: [4, 6, 5, 8, 7, 11, 12] },
  { label: "Faulted", value: "37", delta: "-14%", deltaTone: "danger", trend: [9, 7, 8, 6, 5, 4, 3] }
], h = [
  { name: "Dashboard", kind: "Frontend", tone: "accent", status: "Loaded" },
  { name: "Workflows", kind: "Full-stack", tone: "success", status: "Loaded" },
  { name: "Diagnostics", kind: "Frontend", tone: "neutral", status: "Idle" }
];
function N() {
  const [a, n] = p.useState(h[0]?.name);
  return /* @__PURE__ */ e.jsxs("div", { className: "admin-card dashboard-sample-card", children: [
    /* @__PURE__ */ e.jsx("h2", { children: "Shared UI kit" }),
    /* @__PURE__ */ e.jsx("div", { className: "dashboard-sample-stats", children: E.map((t) => /* @__PURE__ */ e.jsx(
      j,
      {
        label: t.label,
        value: t.value,
        delta: t.delta,
        deltaTone: t.deltaTone,
        trend: t.trend
      },
      t.label
    )) }),
    /* @__PURE__ */ e.jsx(b, { className: "dashboard-sample-list", children: h.map((t) => /* @__PURE__ */ e.jsx(
      v,
      {
        title: t.name,
        subtitle: t.kind,
        selected: a === t.name,
        onSelect: () => n(t.name),
        trailing: /* @__PURE__ */ e.jsx(k, { tone: t.tone, children: t.status })
      },
      t.name
    )) })
  ] });
}
export {
  g as BackendEndpointsWidget,
  T as ModuleHealthWidget,
  f as RouteOwnershipWidget,
  N as SharedKitWidget,
  w as register
};
