import re from "react";
var v = { exports: {} }, h = {};
var $;
function te() {
  if ($) return h;
  $ = 1;
  var s = /* @__PURE__ */ Symbol.for("react.transitional.element"), p = /* @__PURE__ */ Symbol.for("react.fragment");
  function u(d, l, i) {
    var f = null;
    if (i !== void 0 && (f = "" + i), l.key !== void 0 && (f = "" + l.key), "key" in l) {
      i = {};
      for (var m in l)
        m !== "key" && (i[m] = l[m]);
    } else i = l;
    return l = i.ref, {
      $$typeof: s,
      type: d,
      key: f,
      ref: l !== void 0 ? l : null,
      props: i
    };
  }
  return h.Fragment = p, h.jsx = u, h.jsxs = u, h;
}
var b = {};
var I;
function ae() {
  return I || (I = 1, process.env.NODE_ENV !== "production" && (function() {
    function s(e) {
      if (e == null) return null;
      if (typeof e == "function")
        return e.$$typeof === Q ? null : e.displayName || e.name || null;
      if (typeof e == "string") return e;
      switch (e) {
        case R:
          return "Fragment";
        case J:
          return "Profiler";
        case q:
          return "StrictMode";
        case X:
          return "Suspense";
        case B:
          return "SuspenseList";
        case Z:
          return "Activity";
      }
      if (typeof e == "object")
        switch (typeof e.tag == "number" && console.error(
          "Received an unexpected object in getComponentNameFromType(). This is likely a bug in React. Please file an issue."
        ), e.$$typeof) {
          case U:
            return "Portal";
          case z:
            return e.displayName || "Context";
          case V:
            return (e._context.displayName || "Context") + ".Consumer";
          case G:
            var r = e.render;
            return e = e.displayName, e || (e = r.displayName || r.name || "", e = e !== "" ? "ForwardRef(" + e + ")" : "ForwardRef"), e;
          case H:
            return r = e.displayName || null, r !== null ? r : s(e.type) || "Memo";
          case T:
            r = e._payload, e = e._init;
            try {
              return s(e(r));
            } catch {
            }
        }
      return null;
    }
    function p(e) {
      return "" + e;
    }
    function u(e) {
      try {
        p(e);
        var r = !1;
      } catch {
        r = !0;
      }
      if (r) {
        r = console;
        var t = r.error, a = typeof Symbol == "function" && Symbol.toStringTag && e[Symbol.toStringTag] || e.constructor.name || "Object";
        return t.call(
          r,
          "The provided key is an unsupported type %s. This value must be coerced to a string before using it here.",
          a
        ), p(e);
      }
    }
    function d(e) {
      if (e === R) return "<>";
      if (typeof e == "object" && e !== null && e.$$typeof === T)
        return "<...>";
      try {
        var r = s(e);
        return r ? "<" + r + ">" : "<...>";
      } catch {
        return "<...>";
      }
    }
    function l() {
      var e = x.A;
      return e === null ? null : e.getOwner();
    }
    function i() {
      return Error("react-stack-top-frame");
    }
    function f(e) {
      if (P.call(e, "key")) {
        var r = Object.getOwnPropertyDescriptor(e, "key").get;
        if (r && r.isReactWarning) return !1;
      }
      return e.key !== void 0;
    }
    function m(e, r) {
      function t() {
        w || (w = !0, console.error(
          "%s: `key` is not a prop. Trying to access it will result in `undefined` being returned. If you need to access the same value within the child component, you should pass it as a different prop. (https://react.dev/link/special-props)",
          r
        ));
      }
      t.isReactWarning = !0, Object.defineProperty(e, "key", {
        get: t,
        configurable: !0
      });
    }
    function W() {
      var e = s(this.type);
      return N[e] || (N[e] = !0, console.error(
        "Accessing element.ref was removed in React 19. ref is now a regular prop. It will be removed from the JSX Element type in a future release."
      )), e = this.props.ref, e !== void 0 ? e : null;
    }
    function L(e, r, t, a, E, k) {
      var n = t.ref;
      return e = {
        $$typeof: y,
        type: e,
        key: r,
        props: t,
        _owner: a
      }, (n !== void 0 ? n : null) !== null ? Object.defineProperty(e, "ref", {
        enumerable: !1,
        get: W
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
        value: E
      }), Object.defineProperty(e, "_debugTask", {
        configurable: !1,
        enumerable: !1,
        writable: !0,
        value: k
      }), Object.freeze && (Object.freeze(e.props), Object.freeze(e)), e;
    }
    function O(e, r, t, a, E, k) {
      var n = r.children;
      if (n !== void 0)
        if (a)
          if (K(n)) {
            for (a = 0; a < n.length; a++)
              A(n[a]);
            Object.freeze && Object.freeze(n);
          } else
            console.error(
              "React.jsx: Static children should always be an array. You are likely explicitly calling React.jsxs or React.jsxDEV. Use the Babel transform instead."
            );
        else A(n);
      if (P.call(r, "key")) {
        n = s(e);
        var c = Object.keys(r).filter(function(ee) {
          return ee !== "key";
        });
        a = 0 < c.length ? "{key: someKey, " + c.join(": ..., ") + ": ...}" : "{key: someKey}", D[n + a] || (c = 0 < c.length ? "{" + c.join(": ..., ") + ": ...}" : "{}", console.error(
          `A props object containing a "key" prop is being spread into JSX:
  let props = %s;
  <%s {...props} />
React keys must be passed directly to JSX without using spread:
  let props = %s;
  <%s key={someKey} {...props} />`,
          a,
          n,
          c,
          n
        ), D[n + a] = !0);
      }
      if (n = null, t !== void 0 && (u(t), n = "" + t), f(r) && (u(r.key), n = "" + r.key), "key" in r) {
        t = {};
        for (var g in r)
          g !== "key" && (t[g] = r[g]);
      } else t = r;
      return n && m(
        t,
        typeof e == "function" ? e.displayName || e.name || "Unknown" : e
      ), L(
        e,
        n,
        t,
        l(),
        E,
        k
      );
    }
    function A(e) {
      S(e) ? e._store && (e._store.validated = 1) : typeof e == "object" && e !== null && e.$$typeof === T && (e._payload.status === "fulfilled" ? S(e._payload.value) && e._payload.value._store && (e._payload.value._store.validated = 1) : e._store && (e._store.validated = 1));
    }
    function S(e) {
      return typeof e == "object" && e !== null && e.$$typeof === y;
    }
    var _ = re, y = /* @__PURE__ */ Symbol.for("react.transitional.element"), U = /* @__PURE__ */ Symbol.for("react.portal"), R = /* @__PURE__ */ Symbol.for("react.fragment"), q = /* @__PURE__ */ Symbol.for("react.strict_mode"), J = /* @__PURE__ */ Symbol.for("react.profiler"), V = /* @__PURE__ */ Symbol.for("react.consumer"), z = /* @__PURE__ */ Symbol.for("react.context"), G = /* @__PURE__ */ Symbol.for("react.forward_ref"), X = /* @__PURE__ */ Symbol.for("react.suspense"), B = /* @__PURE__ */ Symbol.for("react.suspense_list"), H = /* @__PURE__ */ Symbol.for("react.memo"), T = /* @__PURE__ */ Symbol.for("react.lazy"), Z = /* @__PURE__ */ Symbol.for("react.activity"), Q = /* @__PURE__ */ Symbol.for("react.client.reference"), x = _.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE, P = Object.prototype.hasOwnProperty, K = Array.isArray, j = console.createTask ? console.createTask : function() {
      return null;
    };
    _ = {
      react_stack_bottom_frame: function(e) {
        return e();
      }
    };
    var w, N = {}, C = _.react_stack_bottom_frame.bind(
      _,
      i
    )(), Y = j(d(i)), D = {};
    b.Fragment = R, b.jsx = function(e, r, t) {
      var a = 1e4 > x.recentlyCreatedOwnerStacks++;
      return O(
        e,
        r,
        t,
        !1,
        a ? Error("react-stack-top-frame") : C,
        a ? j(d(e)) : Y
      );
    }, b.jsxs = function(e, r, t) {
      var a = 1e4 > x.recentlyCreatedOwnerStacks++;
      return O(
        e,
        r,
        t,
        !0,
        a ? Error("react-stack-top-frame") : C,
        a ? j(d(e)) : Y
      );
    };
  })()), b;
}
var F;
function ne() {
  return F || (F = 1, process.env.NODE_ENV === "production" ? v.exports = te() : v.exports = ae()), v.exports;
}
var o = ne();
function le(s) {
  s.navigation.add({
    id: "dashboard-sample",
    label: "Dashboard",
    path: "/dashboard",
    order: 100
  }), s.routes.add({
    id: "dashboard-sample",
    label: "Dashboard",
    path: "/dashboard",
    component: oe
  }), s.dashboardWidgets.add({
    id: "dashboard-sample-health",
    title: "Module health",
    order: 100,
    component: M
  });
}
function oe() {
  return /* @__PURE__ */ o.jsxs("section", { children: [
    /* @__PURE__ */ o.jsxs("header", { className: "page-header", children: [
      /* @__PURE__ */ o.jsx("h1", { children: "Dashboard sample" }),
      /* @__PURE__ */ o.jsx("p", { children: "This frontend-only module contributed navigation, a route, and dashboard widgets." })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "dashboard-sample-grid", children: [
      /* @__PURE__ */ o.jsx(M, {}),
      /* @__PURE__ */ o.jsxs("div", { className: "admin-card", children: [
        /* @__PURE__ */ o.jsx("h2", { children: "Registered routes" }),
        /* @__PURE__ */ o.jsx("p", { className: "metric", children: "1" }),
        /* @__PURE__ */ o.jsx("p", { children: "The route was added by the dashboard module at runtime." })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { className: "admin-card", children: [
        /* @__PURE__ */ o.jsx("h2", { children: "Backend endpoints" }),
        /* @__PURE__ */ o.jsx("p", { className: "metric", children: "0" }),
        /* @__PURE__ */ o.jsx("p", { children: "This sample proves a frontend-only contribution." })
      ] })
    ] })
  ] });
}
function M() {
  return /* @__PURE__ */ o.jsxs("div", { className: "admin-card dashboard-sample-card", children: [
    /* @__PURE__ */ o.jsx("h2", { children: "Dashboard module" }),
    /* @__PURE__ */ o.jsx("p", { className: "metric", children: "Loaded" }),
    /* @__PURE__ */ o.jsx("p", { children: "Runtime registration completed through the admin SDK." })
  ] });
}
export {
  oe as DashboardPage,
  M as ModuleHealthWidget,
  le as register
};
