var i = { exports: {} }, s = {};
var l;
function x() {
  if (l) return s;
  l = 1;
  var a = /* @__PURE__ */ Symbol.for("react.transitional.element"), u = /* @__PURE__ */ Symbol.for("react.fragment");
  function o(m, r, d) {
    var t = null;
    if (d !== void 0 && (t = "" + d), r.key !== void 0 && (t = "" + r.key), "key" in r) {
      d = {};
      for (var n in r)
        n !== "key" && (d[n] = r[n]);
    } else d = r;
    return r = d.ref, {
      $$typeof: a,
      type: m,
      key: t,
      ref: r !== void 0 ? r : null,
      props: d
    };
  }
  return s.Fragment = u, s.jsx = o, s.jsxs = o, s;
}
var h;
function p() {
  return h || (h = 1, i.exports = x()), i.exports;
}
var e = p();
function v(a) {
  a.navigation.add({
    id: "dashboard-sample",
    label: "Dashboard",
    path: "/dashboard",
    order: 100
  }), a.routes.add({
    id: "dashboard-sample",
    label: "Dashboard",
    path: "/dashboard",
    component: j
  }), a.dashboardWidgets.add({
    id: "dashboard-sample-health",
    title: "Module health",
    order: 100,
    component: c
  });
}
function j() {
  return /* @__PURE__ */ e.jsxs("section", { children: [
    /* @__PURE__ */ e.jsxs("header", { className: "page-header", children: [
      /* @__PURE__ */ e.jsx("h1", { children: "Dashboard sample" }),
      /* @__PURE__ */ e.jsx("p", { children: "This frontend-only module contributed navigation, a route, and dashboard widgets." })
    ] }),
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
    /* @__PURE__ */ e.jsx("p", { children: "Runtime registration completed through the admin SDK." })
  ] });
}
export {
  j as DashboardPage,
  c as ModuleHealthWidget,
  v as register
};
