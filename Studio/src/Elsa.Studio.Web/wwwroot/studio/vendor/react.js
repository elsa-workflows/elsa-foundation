import { g as J } from "./chunks/_commonjsHelpers.js";
var S = { exports: {} }, n = {};
var M;
function F() {
  if (M) return n;
  M = 1;
  var y = /* @__PURE__ */ Symbol.for("react.transitional.element"), N = /* @__PURE__ */ Symbol.for("react.portal"), Y = /* @__PURE__ */ Symbol.for("react.fragment"), L = /* @__PURE__ */ Symbol.for("react.strict_mode"), k = /* @__PURE__ */ Symbol.for("react.profiler"), D = /* @__PURE__ */ Symbol.for("react.consumer"), b = /* @__PURE__ */ Symbol.for("react.context"), U = /* @__PURE__ */ Symbol.for("react.forward_ref"), q = /* @__PURE__ */ Symbol.for("react.suspense"), z = /* @__PURE__ */ Symbol.for("react.memo"), A = /* @__PURE__ */ Symbol.for("react.lazy"), G = /* @__PURE__ */ Symbol.for("react.activity"), g = Symbol.iterator;
  function K(t) {
    return t === null || typeof t != "object" ? null : (t = g && t[g] || t["@@iterator"], typeof t == "function" ? t : null);
  }
  var h = {
    isMounted: function() {
      return !1;
    },
    enqueueForceUpdate: function() {
    },
    enqueueReplaceState: function() {
    },
    enqueueSetState: function() {
    }
  }, w = Object.assign, P = {};
  function l(t, e, o) {
    this.props = t, this.context = e, this.refs = P, this.updater = o || h;
  }
  l.prototype.isReactComponent = {}, l.prototype.setState = function(t, e) {
    if (typeof t != "object" && typeof t != "function" && t != null)
      throw Error(
        "takes an object of state variables to update or a function which returns an object of state variables."
      );
    this.updater.enqueueSetState(this, t, e, "setState");
  }, l.prototype.forceUpdate = function(t) {
    this.updater.enqueueForceUpdate(this, t, "forceUpdate");
  };
  function H() {
  }
  H.prototype = l.prototype;
  function m(t, e, o) {
    this.props = t, this.context = e, this.refs = P, this.updater = o || h;
  }
  var v = m.prototype = new H();
  v.constructor = m, w(v, l.prototype), v.isPureReactComponent = !0;
  var O = Array.isArray;
  function R() {
  }
  var f = { H: null, A: null, T: null, S: null }, j = Object.prototype.hasOwnProperty;
  function C(t, e, o) {
    var r = o.ref;
    return {
      $$typeof: y,
      type: t,
      key: e,
      ref: r !== void 0 ? r : null,
      props: o
    };
  }
  function V(t, e) {
    return C(t.type, e, t.props);
  }
  function T(t) {
    return typeof t == "object" && t !== null && t.$$typeof === y;
  }
  function B(t) {
    var e = { "=": "=0", ":": "=2" };
    return "$" + t.replace(/[=:]/g, function(o) {
      return e[o];
    });
  }
  var x = /\/+/g;
  function d(t, e) {
    return typeof t == "object" && t !== null && t.key != null ? B("" + t.key) : e.toString(36);
  }
  function W(t) {
    switch (t.status) {
      case "fulfilled":
        return t.value;
      case "rejected":
        throw t.reason;
      default:
        switch (typeof t.status == "string" ? t.then(R, R) : (t.status = "pending", t.then(
          function(e) {
            t.status === "pending" && (t.status = "fulfilled", t.value = e);
          },
          function(e) {
            t.status === "pending" && (t.status = "rejected", t.reason = e);
          }
        )), t.status) {
          case "fulfilled":
            return t.value;
          case "rejected":
            throw t.reason;
        }
    }
    throw t;
  }
  function E(t, e, o, r, s) {
    var c = typeof t;
    (c === "undefined" || c === "boolean") && (t = null);
    var i = !1;
    if (t === null) i = !0;
    else
      switch (c) {
        case "bigint":
        case "string":
        case "number":
          i = !0;
          break;
        case "object":
          switch (t.$$typeof) {
            case y:
            case N:
              i = !0;
              break;
            case A:
              return i = t._init, E(
                i(t._payload),
                e,
                o,
                r,
                s
              );
          }
      }
    if (i)
      return s = s(t), i = r === "" ? "." + d(t, 0) : r, O(s) ? (o = "", i != null && (o = i.replace(x, "$&/") + "/"), E(s, e, o, "", function(Z) {
        return Z;
      })) : s != null && (T(s) && (s = V(
        s,
        o + (s.key == null || t && t.key === s.key ? "" : ("" + s.key).replace(
          x,
          "$&/"
        ) + "/") + i
      )), e.push(s)), 1;
    i = 0;
    var p = r === "" ? "." : r + ":";
    if (O(t))
      for (var a = 0; a < t.length; a++)
        r = t[a], c = p + d(r, a), i += E(
          r,
          e,
          o,
          c,
          s
        );
    else if (a = K(t), typeof a == "function")
      for (t = a.call(t), a = 0; !(r = t.next()).done; )
        r = r.value, c = p + d(r, a++), i += E(
          r,
          e,
          o,
          c,
          s
        );
    else if (c === "object") {
      if (typeof t.then == "function")
        return E(
          W(t),
          e,
          o,
          r,
          s
        );
      throw e = String(t), Error(
        "Objects are not valid as a React child (found: " + (e === "[object Object]" ? "object with keys {" + Object.keys(t).join(", ") + "}" : e) + "). If you meant to render a collection of children, use an array instead."
      );
    }
    return i;
  }
  function _(t, e, o) {
    if (t == null) return t;
    var r = [], s = 0;
    return E(t, r, "", "", function(c) {
      return e.call(o, c, s++);
    }), r;
  }
  function Q(t) {
    if (t._status === -1) {
      var e = t._result;
      e = e(), e.then(
        function(o) {
          (t._status === 0 || t._status === -1) && (t._status = 1, t._result = o);
        },
        function(o) {
          (t._status === 0 || t._status === -1) && (t._status = 2, t._result = o);
        }
      ), t._status === -1 && (t._status = 0, t._result = e);
    }
    if (t._status === 1) return t._result.default;
    throw t._result;
  }
  var I = typeof reportError == "function" ? reportError : function(t) {
    if (typeof window == "object" && typeof window.ErrorEvent == "function") {
      var e = new window.ErrorEvent("error", {
        bubbles: !0,
        cancelable: !0,
        message: typeof t == "object" && t !== null && typeof t.message == "string" ? String(t.message) : String(t),
        error: t
      });
      if (!window.dispatchEvent(e)) return;
    } else if (typeof process == "object" && typeof process.emit == "function") {
      process.emit("uncaughtException", t);
      return;
    }
    console.error(t);
  }, X = {
    map: _,
    forEach: function(t, e, o) {
      _(
        t,
        function() {
          e.apply(this, arguments);
        },
        o
      );
    },
    count: function(t) {
      var e = 0;
      return _(t, function() {
        e++;
      }), e;
    },
    toArray: function(t) {
      return _(t, function(e) {
        return e;
      }) || [];
    },
    only: function(t) {
      if (!T(t))
        throw Error(
          "React.Children.only expected to receive a single React element child."
        );
      return t;
    }
  };
  return n.Activity = G, n.Children = X, n.Component = l, n.Fragment = Y, n.Profiler = k, n.PureComponent = m, n.StrictMode = L, n.Suspense = q, n.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = f, n.__COMPILER_RUNTIME = {
    __proto__: null,
    c: function(t) {
      return f.H.useMemoCache(t);
    }
  }, n.cache = function(t) {
    return function() {
      return t.apply(null, arguments);
    };
  }, n.cacheSignal = function() {
    return null;
  }, n.cloneElement = function(t, e, o) {
    if (t == null)
      throw Error(
        "The argument must be a React element, but you passed " + t + "."
      );
    var r = w({}, t.props), s = t.key;
    if (e != null)
      for (c in e.key !== void 0 && (s = "" + e.key), e)
        !j.call(e, c) || c === "key" || c === "__self" || c === "__source" || c === "ref" && e.ref === void 0 || (r[c] = e[c]);
    var c = arguments.length - 2;
    if (c === 1) r.children = o;
    else if (1 < c) {
      for (var i = Array(c), p = 0; p < c; p++)
        i[p] = arguments[p + 2];
      r.children = i;
    }
    return C(t.type, s, r);
  }, n.createContext = function(t) {
    return t = {
      $$typeof: b,
      _currentValue: t,
      _currentValue2: t,
      _threadCount: 0,
      Provider: null,
      Consumer: null
    }, t.Provider = t, t.Consumer = {
      $$typeof: D,
      _context: t
    }, t;
  }, n.createElement = function(t, e, o) {
    var r, s = {}, c = null;
    if (e != null)
      for (r in e.key !== void 0 && (c = "" + e.key), e)
        j.call(e, r) && r !== "key" && r !== "__self" && r !== "__source" && (s[r] = e[r]);
    var i = arguments.length - 2;
    if (i === 1) s.children = o;
    else if (1 < i) {
      for (var p = Array(i), a = 0; a < i; a++)
        p[a] = arguments[a + 2];
      s.children = p;
    }
    if (t && t.defaultProps)
      for (r in i = t.defaultProps, i)
        s[r] === void 0 && (s[r] = i[r]);
    return C(t, c, s);
  }, n.createRef = function() {
    return { current: null };
  }, n.forwardRef = function(t) {
    return { $$typeof: U, render: t };
  }, n.isValidElement = T, n.lazy = function(t) {
    return {
      $$typeof: A,
      _payload: { _status: -1, _result: t },
      _init: Q
    };
  }, n.memo = function(t, e) {
    return {
      $$typeof: z,
      type: t,
      compare: e === void 0 ? null : e
    };
  }, n.startTransition = function(t) {
    var e = f.T, o = {};
    f.T = o;
    try {
      var r = t(), s = f.S;
      s !== null && s(o, r), typeof r == "object" && r !== null && typeof r.then == "function" && r.then(R, I);
    } catch (c) {
      I(c);
    } finally {
      e !== null && o.types !== null && (e.types = o.types), f.T = e;
    }
  }, n.unstable_useCacheRefresh = function() {
    return f.H.useCacheRefresh();
  }, n.use = function(t) {
    return f.H.use(t);
  }, n.useActionState = function(t, e, o) {
    return f.H.useActionState(t, e, o);
  }, n.useCallback = function(t, e) {
    return f.H.useCallback(t, e);
  }, n.useContext = function(t) {
    return f.H.useContext(t);
  }, n.useDebugValue = function() {
  }, n.useDeferredValue = function(t, e) {
    return f.H.useDeferredValue(t, e);
  }, n.useEffect = function(t, e) {
    return f.H.useEffect(t, e);
  }, n.useEffectEvent = function(t) {
    return f.H.useEffectEvent(t);
  }, n.useId = function() {
    return f.H.useId();
  }, n.useImperativeHandle = function(t, e, o) {
    return f.H.useImperativeHandle(t, e, o);
  }, n.useInsertionEffect = function(t, e) {
    return f.H.useInsertionEffect(t, e);
  }, n.useLayoutEffect = function(t, e) {
    return f.H.useLayoutEffect(t, e);
  }, n.useMemo = function(t, e) {
    return f.H.useMemo(t, e);
  }, n.useOptimistic = function(t, e) {
    return f.H.useOptimistic(t, e);
  }, n.useReducer = function(t, e, o) {
    return f.H.useReducer(t, e, o);
  }, n.useRef = function(t) {
    return f.H.useRef(t);
  }, n.useState = function(t) {
    return f.H.useState(t);
  }, n.useSyncExternalStore = function(t, e, o) {
    return f.H.useSyncExternalStore(
      t,
      e,
      o
    );
  }, n.useTransition = function() {
    return f.H.useTransition();
  }, n.version = "19.2.7", n;
}
var $;
function tt() {
  return $ || ($ = 1, S.exports = F()), S.exports;
}
var et = tt();
const u = /* @__PURE__ */ J(et), rt = u.Children, ot = u.Component, ut = u.Fragment, st = u.Profiler, ct = u.PureComponent, ft = u.StrictMode, it = u.Suspense, at = u.cloneElement, pt = u.createContext, lt = u.createElement, Et = u.createRef, _t = u.forwardRef, yt = u.isValidElement, mt = u.lazy, vt = u.memo, Rt = u.startTransition, Ct = u.use, Tt = u.useActionState, dt = u.useCallback, St = u.useContext, At = u.useDebugValue, gt = u.useDeferredValue, ht = u.useEffect, wt = u.useId, Pt = u.useImperativeHandle, Ht = u.useInsertionEffect, Ot = u.useLayoutEffect, jt = u.useMemo, xt = u.useOptimistic, It = u.useReducer, Mt = u.useRef, $t = u.useState, Nt = u.useSyncExternalStore, Yt = u.useTransition, Lt = u.version;
export {
  rt as Children,
  ot as Component,
  ut as Fragment,
  st as Profiler,
  ct as PureComponent,
  ft as StrictMode,
  it as Suspense,
  at as cloneElement,
  pt as createContext,
  lt as createElement,
  Et as createRef,
  u as default,
  _t as forwardRef,
  yt as isValidElement,
  mt as lazy,
  vt as memo,
  Rt as startTransition,
  Ct as use,
  Tt as useActionState,
  dt as useCallback,
  St as useContext,
  At as useDebugValue,
  gt as useDeferredValue,
  ht as useEffect,
  wt as useId,
  Pt as useImperativeHandle,
  Ht as useInsertionEffect,
  Ot as useLayoutEffect,
  jt as useMemo,
  xt as useOptimistic,
  It as useReducer,
  Mt as useRef,
  $t as useState,
  Nt as useSyncExternalStore,
  Yt as useTransition,
  Lt as version
};
