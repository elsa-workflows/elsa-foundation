import { useState as u, useEffect as p } from "react";
var h = { exports: {} }, n = {};
var c;
function j() {
  if (c) return n;
  c = 1;
  var t = /* @__PURE__ */ Symbol.for("react.transitional.element"), i = /* @__PURE__ */ Symbol.for("react.fragment");
  function a(o, e, s) {
    var d = null;
    if (s !== void 0 && (d = "" + s), e.key !== void 0 && (d = "" + e.key), "key" in e) {
      s = {};
      for (var l in e)
        l !== "key" && (s[l] = e[l]);
    } else s = e;
    return e = s.ref, {
      $$typeof: t,
      type: o,
      key: d,
      ref: e !== void 0 ? e : null,
      props: s
    };
  }
  return n.Fragment = i, n.jsx = a, n.jsxs = a, n;
}
var x;
function v() {
  return x || (x = 1, h.exports = j()), h.exports;
}
var r = v();
let m;
function E(t) {
  m = t, t.navigation.add({
    id: "weather-forecast-sample",
    label: "Weather",
    path: "/weather",
    order: 120
  }), t.routes.add({
    id: "weather-forecast-sample",
    label: "Weather",
    path: "/weather",
    component: R
  });
}
function R() {
  const [t, i] = u([]), [a, o] = u(null);
  return p(() => {
    m.host.http.getJson("/_elsa/studio/samples/weather-forecast").then(i).catch((e) => o(e instanceof Error ? e.message : String(e)));
  }, []), /* @__PURE__ */ r.jsxs("section", { children: [
    /* @__PURE__ */ r.jsx("div", { className: "section-header", children: /* @__PURE__ */ r.jsxs("div", { children: [
      /* @__PURE__ */ r.jsx("h2", { children: "Weather forecast" }),
      /* @__PURE__ */ r.jsx("p", { children: "This module contributes both server behavior and React UI." })
    ] }) }),
    a ? /* @__PURE__ */ r.jsx("div", { className: "error-state", children: a }) : null,
    /* @__PURE__ */ r.jsxs("div", { className: "weather-table", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "weather-row weather-heading", children: [
        /* @__PURE__ */ r.jsx("span", { children: "Date" }),
        /* @__PURE__ */ r.jsx("span", { children: "Summary" }),
        /* @__PURE__ */ r.jsx("span", { children: "Temperature" })
      ] }),
      t.map((e) => /* @__PURE__ */ r.jsxs("div", { className: "weather-row", children: [
        /* @__PURE__ */ r.jsx("span", { children: e.date }),
        /* @__PURE__ */ r.jsx("strong", { children: e.summary }),
        /* @__PURE__ */ r.jsxs("span", { children: [
          e.temperatureC,
          "C / ",
          e.temperatureF,
          "F"
        ] })
      ] }, e.date))
    ] })
  ] });
}
export {
  R as WeatherPage,
  E as register
};
