var i = { exports: {} }, s = {};
var l;
function p() {
  if (l) return s;
  l = 1;
  var t = /* @__PURE__ */ Symbol.for("react.transitional.element"), c = /* @__PURE__ */ Symbol.for("react.fragment");
  function o(h, r, d) {
    var n = null;
    if (d !== void 0 && (n = "" + d), r.key !== void 0 && (n = "" + r.key), "key" in r) {
      d = {};
      for (var a in r)
        a !== "key" && (d[a] = r[a]);
    } else d = r;
    return r = d.ref, {
      $$typeof: t,
      type: h,
      key: n,
      ref: r !== void 0 ? r : null,
      props: d
    };
  }
  return s.Fragment = c, s.jsx = o, s.jsxs = o, s;
}
var u;
function m() {
  return u || (u = 1, i.exports = p()), i.exports;
}
var e = m();
function v(t) {
  t.dashboardWidgets.add({
    id: "dashboard-sample-health",
    title: "Module health",
    order: 100,
    component: x
  }), t.dashboardWidgets.add({
    id: "dashboard-sample-route",
    title: "Route ownership",
    order: 110,
    component: R
  }), t.dashboardWidgets.add({
    id: "dashboard-sample-backend",
    title: "Backend endpoints",
    order: 120,
    component: j
  });
}
function x() {
  return /* @__PURE__ */ e.jsxs("div", { className: "admin-card dashboard-sample-card", children: [
    /* @__PURE__ */ e.jsx("h2", { children: "Dashboard module" }),
    /* @__PURE__ */ e.jsx("p", { className: "metric", children: "Loaded" }),
    /* @__PURE__ */ e.jsx("p", { children: "Runtime widget registration completed through the Studio SDK." })
  ] });
}
function R() {
  return /* @__PURE__ */ e.jsxs("div", { className: "admin-card dashboard-sample-card", children: [
    /* @__PURE__ */ e.jsx("h2", { children: "Route ownership" }),
    /* @__PURE__ */ e.jsx("p", { className: "metric", children: "Host" }),
    /* @__PURE__ */ e.jsx("p", { children: "Dashboard navigation and routing are provided by Studio." })
  ] });
}
function j() {
  return /* @__PURE__ */ e.jsxs("div", { className: "admin-card dashboard-sample-card", children: [
    /* @__PURE__ */ e.jsx("h2", { children: "Backend endpoints" }),
    /* @__PURE__ */ e.jsx("p", { className: "metric", children: "0" }),
    /* @__PURE__ */ e.jsx("p", { children: "This widget is contributed by a frontend-only module." })
  ] });
}
export {
  j as BackendEndpointsWidget,
  x as ModuleHealthWidget,
  R as RouteOwnershipWidget,
  v as register
};
