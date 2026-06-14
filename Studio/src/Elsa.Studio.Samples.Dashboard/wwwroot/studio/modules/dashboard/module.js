var n = { exports: {} }, a = {};
var l;
function m() {
  if (l) return a;
  l = 1;
  var s = /* @__PURE__ */ Symbol.for("react.transitional.element"), u = /* @__PURE__ */ Symbol.for("react.fragment");
  function o(x, r, d) {
    var t = null;
    if (d !== void 0 && (t = "" + d), r.key !== void 0 && (t = "" + r.key), "key" in r) {
      d = {};
      for (var i in r)
        i !== "key" && (d[i] = r[i]);
    } else d = r;
    return r = d.ref, {
      $$typeof: s,
      type: x,
      key: t,
      ref: r !== void 0 ? r : null,
      props: d
    };
  }
  return a.Fragment = u, a.jsx = o, a.jsxs = o, a;
}
var h;
function p() {
  return h || (h = 1, n.exports = m()), n.exports;
}
var e = p();
function v(s) {
  s.navigation.add({
    id: "dashboard-sample",
    label: "Dashboard",
    path: "/dashboard",
    order: 100
  }), s.routes.add({
    id: "dashboard-sample",
    label: "Dashboard",
    path: "/dashboard",
    component: j
  }), s.dashboardWidgets.add({
    id: "dashboard-sample-health",
    title: "Module health",
    order: 100,
    component: c
  });
}
function j() {
  return /* @__PURE__ */ e.jsxs("section", { children: [
    /* @__PURE__ */ e.jsx("div", { className: "section-header", children: /* @__PURE__ */ e.jsxs("div", { children: [
      /* @__PURE__ */ e.jsx("h2", { children: "Dashboard sample" }),
      /* @__PURE__ */ e.jsx("p", { children: "This frontend-only module contributed navigation, a route, and dashboard widgets." })
    ] }) }),
    /* @__PURE__ */ e.jsxs("div", { className: "dashboard-sample-grid", children: [
      /* @__PURE__ */ e.jsx(c, {}),
      /* @__PURE__ */ e.jsxs("div", { className: "admin-card", children: [
        /* @__PURE__ */ e.jsx("h2", { children: "Registered routes" }),
        /* @__PURE__ */ e.jsx("p", { className: "metric", children: "1" }),
        /* @__PURE__ */ e.jsx("p", { children: "The route was added by the dashboard module at runtime." })
      ] }),
      /* @__PURE__ */ e.jsxs("div", { className: "admin-card", children: [
        /* @__PURE__ */ e.jsx("h2", { children: "Backend endpoints" }),
        /* @__PURE__ */ e.jsx("p", { className: "metric", children: "0" }),
        /* @__PURE__ */ e.jsx("p", { children: "This sample proves a frontend-only contribution." })
      ] })
    ] })
  ] });
}
function c() {
  return /* @__PURE__ */ e.jsxs("div", { className: "admin-card dashboard-sample-card", children: [
    /* @__PURE__ */ e.jsx("h2", { children: "Dashboard module" }),
    /* @__PURE__ */ e.jsx("p", { className: "metric", children: "Loaded" }),
    /* @__PURE__ */ e.jsx("p", { children: "Runtime registration completed through the Studio SDK." })
  ] });
}
export {
  j as DashboardPage,
  c as ModuleHealthWidget,
  v as register
};
