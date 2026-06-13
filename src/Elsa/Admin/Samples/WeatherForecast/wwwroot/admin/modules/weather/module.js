import te, { useState as $, useEffect as ae } from "react";
var b = { exports: {} }, p = {};
var I;
function ne() {
  if (I) return p;
  I = 1;
  var l = /* @__PURE__ */ Symbol.for("react.transitional.element"), f = /* @__PURE__ */ Symbol.for("react.fragment");
  function u(i, a, c) {
    var m = null;
    if (c !== void 0 && (m = "" + c), a.key !== void 0 && (m = "" + a.key), "key" in a) {
      c = {};
      for (var E in a)
        E !== "key" && (c[E] = a[E]);
    } else c = a;
    return a = c.ref, {
      $$typeof: l,
      type: i,
      key: m,
      ref: a !== void 0 ? a : null,
      props: c
    };
  }
  return p.Fragment = f, p.jsx = u, p.jsxs = u, p;
}
var _ = {};
var W;
function oe() {
  return W || (W = 1, process.env.NODE_ENV !== "production" && (function() {
    function l(e) {
      if (e == null) return null;
      if (typeof e == "function")
        return e.$$typeof === K ? null : e.displayName || e.name || null;
      if (typeof e == "string") return e;
      switch (e) {
        case R:
          return "Fragment";
        case V:
          return "Profiler";
        case q:
          return "StrictMode";
        case B:
          return "Suspense";
        case H:
          return "SuspenseList";
        case Q:
          return "Activity";
      }
      if (typeof e == "object")
        switch (typeof e.tag == "number" && console.error(
          "Received an unexpected object in getComponentNameFromType(). This is likely a bug in React. Please file an issue."
        ), e.$$typeof) {
          case J:
            return "Portal";
          case G:
            return e.displayName || "Context";
          case z:
            return (e._context.displayName || "Context") + ".Consumer";
          case X:
            var r = e.render;
            return e = e.displayName, e || (e = r.displayName || r.name || "", e = e !== "" ? "ForwardRef(" + e + ")" : "ForwardRef"), e;
          case Z:
            return r = e.displayName || null, r !== null ? r : l(e.type) || "Memo";
          case T:
            r = e._payload, e = e._init;
            try {
              return l(e(r));
            } catch {
            }
        }
      return null;
    }
    function f(e) {
      return "" + e;
    }
    function u(e) {
      try {
        f(e);
        var r = !1;
      } catch {
        r = !0;
      }
      if (r) {
        r = console;
        var t = r.error, n = typeof Symbol == "function" && Symbol.toStringTag && e[Symbol.toStringTag] || e.constructor.name || "Object";
        return t.call(
          r,
          "The provided key is an unsupported type %s. This value must be coerced to a string before using it here.",
          n
        ), f(e);
      }
    }
    function i(e) {
      if (e === R) return "<>";
      if (typeof e == "object" && e !== null && e.$$typeof === T)
        return "<...>";
      try {
        var r = l(e);
        return r ? "<" + r + ">" : "<...>";
      } catch {
        return "<...>";
      }
    }
    function a() {
      var e = x.A;
      return e === null ? null : e.getOwner();
    }
    function c() {
      return Error("react-stack-top-frame");
    }
    function m(e) {
      if (P.call(e, "key")) {
        var r = Object.getOwnPropertyDescriptor(e, "key").get;
        if (r && r.isReactWarning) return !1;
      }
      return e.key !== void 0;
    }
    function E(e, r) {
      function t() {
        y || (y = !0, console.error(
          "%s: `key` is not a prop. Trying to access it will result in `undefined` being returned. If you need to access the same value within the child component, you should pass it as a different prop. (https://react.dev/link/special-props)",
          r
        ));
      }
      t.isReactWarning = !0, Object.defineProperty(e, "key", {
        get: t,
        configurable: !0
      });
    }
    function M() {
      var e = l(this.type);
      return N[e] || (N[e] = !0, console.error(
        "Accessing element.ref was removed in React 19. ref is now a regular prop. It will be removed from the JSX Element type in a future release."
      )), e = this.props.ref, e !== void 0 ? e : null;
    }
    function U(e, r, t, n, v, w) {
      var o = t.ref;
      return e = {
        $$typeof: A,
        type: e,
        key: r,
        props: t,
        _owner: n
      }, (o !== void 0 ? o : null) !== null ? Object.defineProperty(e, "ref", {
        enumerable: !1,
        get: M
      }) : Object.defineProperty(e, "ref", { enumerable: !1, value: null }), e._store = {}, Object.defineProperty(e._store, "validated", {
        configurable: !1,
        enumerable: !1,
        writable: !0,
        value: 0
      }), Object.defineProperty(e, "_debugInfo", {
        configurable: !1,
        enumerable: !1,
        writable: !0,
        value: null
      }), Object.defineProperty(e, "_debugStack", {
        configurable: !1,
        enumerable: !1,
        writable: !0,
        value: v
      }), Object.defineProperty(e, "_debugTask", {
        configurable: !1,
        enumerable: !1,
        writable: !0,
        value: w
      }), Object.freeze && (Object.freeze(e.props), Object.freeze(e)), e;
    }
    function g(e, r, t, n, v, w) {
      var o = r.children;
      if (o !== void 0)
        if (n)
          if (ee(o)) {
            for (n = 0; n < o.length; n++)
              O(o[n]);
            Object.freeze && Object.freeze(o);
          } else
            console.error(
              "React.jsx: Static children should always be an array. You are likely explicitly calling React.jsxs or React.jsxDEV. Use the Babel transform instead."
            );
        else O(o);
      if (P.call(r, "key")) {
        o = l(e);
        var d = Object.keys(r).filter(function(re) {
          return re !== "key";
        });
        n = 0 < d.length ? "{key: someKey, " + d.join(": ..., ") + ": ...}" : "{key: someKey}", F[o + n] || (d = 0 < d.length ? "{" + d.join(": ..., ") + ": ...}" : "{}", console.error(
          `A props object containing a "key" prop is being spread into JSX:
  let props = %s;
  <%s {...props} />
React keys must be passed directly to JSX without using spread:
  let props = %s;
  <%s key={someKey} {...props} />`,
          n,
          o,
          d,
          o
        ), F[o + n] = !0);
      }
      if (o = null, t !== void 0 && (u(t), o = "" + t), m(r) && (u(r.key), o = "" + r.key), "key" in r) {
        t = {};
        for (var k in r)
          k !== "key" && (t[k] = r[k]);
      } else t = r;
      return o && E(
        t,
        typeof e == "function" ? e.displayName || e.name || "Unknown" : e
      ), U(
        e,
        o,
        t,
        a(),
        v,
        w
      );
    }
    function O(e) {
      S(e) ? e._store && (e._store.validated = 1) : typeof e == "object" && e !== null && e.$$typeof === T && (e._payload.status === "fulfilled" ? S(e._payload.value) && e._payload.value._store && (e._payload.value._store.validated = 1) : e._store && (e._store.validated = 1));
    }
    function S(e) {
      return typeof e == "object" && e !== null && e.$$typeof === A;
    }
    var h = te, A = /* @__PURE__ */ Symbol.for("react.transitional.element"), J = /* @__PURE__ */ Symbol.for("react.portal"), R = /* @__PURE__ */ Symbol.for("react.fragment"), q = /* @__PURE__ */ Symbol.for("react.strict_mode"), V = /* @__PURE__ */ Symbol.for("react.profiler"), z = /* @__PURE__ */ Symbol.for("react.consumer"), G = /* @__PURE__ */ Symbol.for("react.context"), X = /* @__PURE__ */ Symbol.for("react.forward_ref"), B = /* @__PURE__ */ Symbol.for("react.suspense"), H = /* @__PURE__ */ Symbol.for("react.suspense_list"), Z = /* @__PURE__ */ Symbol.for("react.memo"), T = /* @__PURE__ */ Symbol.for("react.lazy"), Q = /* @__PURE__ */ Symbol.for("react.activity"), K = /* @__PURE__ */ Symbol.for("react.client.reference"), x = h.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE, P = Object.prototype.hasOwnProperty, ee = Array.isArray, j = console.createTask ? console.createTask : function() {
      return null;
    };
    h = {
      react_stack_bottom_frame: function(e) {
        return e();
      }
    };
    var y, N = {}, C = h.react_stack_bottom_frame.bind(
      h,
      c
    )(), Y = j(i(c)), F = {};
    _.Fragment = R, _.jsx = function(e, r, t) {
      var n = 1e4 > x.recentlyCreatedOwnerStacks++;
      return g(
        e,
        r,
        t,
        !1,
        n ? Error("react-stack-top-frame") : C,
        n ? j(i(e)) : Y
      );
    }, _.jsxs = function(e, r, t) {
      var n = 1e4 > x.recentlyCreatedOwnerStacks++;
      return g(
        e,
        r,
        t,
        !0,
        n ? Error("react-stack-top-frame") : C,
        n ? j(i(e)) : Y
      );
    };
  })()), _;
}
var D;
function se() {
  return D || (D = 1, process.env.NODE_ENV === "production" ? b.exports = ne() : b.exports = oe()), b.exports;
}
var s = se();
let L;
function ue(l) {
  L = l, l.navigation.add({
    id: "weather-forecast-sample",
    label: "Weather",
    path: "/admin/weather",
    order: 120
  }), l.routes.add({
    id: "weather-forecast-sample",
    label: "Weather",
    path: "/admin/weather",
    component: le
  });
}
function le() {
  const [l, f] = $([]), [u, i] = $(null);
  return ae(() => {
    L.host.http.getJson("/_elsa/samples/weather-forecast").then(f).catch((a) => i(a instanceof Error ? a.message : String(a)));
  }, []), /* @__PURE__ */ s.jsxs("section", { children: [
    /* @__PURE__ */ s.jsxs("header", { className: "page-header", children: [
      /* @__PURE__ */ s.jsx("h1", { children: "Weather forecast" }),
      /* @__PURE__ */ s.jsx("p", { children: "This module contributes both server behavior and React UI." })
    ] }),
    u ? /* @__PURE__ */ s.jsx("div", { className: "error-state", children: u }) : null,
    /* @__PURE__ */ s.jsxs("div", { className: "weather-table", children: [
      /* @__PURE__ */ s.jsxs("div", { className: "weather-row weather-heading", children: [
        /* @__PURE__ */ s.jsx("span", { children: "Date" }),
        /* @__PURE__ */ s.jsx("span", { children: "Summary" }),
        /* @__PURE__ */ s.jsx("span", { children: "Temperature" })
      ] }),
      l.map((a) => /* @__PURE__ */ s.jsxs("div", { className: "weather-row", children: [
        /* @__PURE__ */ s.jsx("span", { children: a.date }),
        /* @__PURE__ */ s.jsx("strong", { children: a.summary }),
        /* @__PURE__ */ s.jsxs("span", { children: [
          a.temperatureC,
          "C / ",
          a.temperatureF,
          "F"
        ] })
      ] }, a.date))
    ] })
  ] });
}
export {
  le as WeatherPage,
  ue as register
};
