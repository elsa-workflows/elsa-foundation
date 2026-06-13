var le = { exports: {} }, f = {};
var je;
function Ie() {
  if (je) return f;
  je = 1;
  var N = /* @__PURE__ */ Symbol.for("react.transitional.element"), i = /* @__PURE__ */ Symbol.for("react.portal"), pe = /* @__PURE__ */ Symbol.for("react.fragment"), de = /* @__PURE__ */ Symbol.for("react.strict_mode"), z = /* @__PURE__ */ Symbol.for("react.profiler"), b = /* @__PURE__ */ Symbol.for("react.consumer"), te = /* @__PURE__ */ Symbol.for("react.context"), W = /* @__PURE__ */ Symbol.for("react.forward_ref"), G = /* @__PURE__ */ Symbol.for("react.suspense"), ne = /* @__PURE__ */ Symbol.for("react.memo"), M = /* @__PURE__ */ Symbol.for("react.lazy"), Y = /* @__PURE__ */ Symbol.for("react.activity"), B = Symbol.iterator;
  function re(t) {
    return t === null || typeof t != "object" ? null : (t = B && t[B] || t["@@iterator"], typeof t == "function" ? t : null);
  }
  var Q = {
    isMounted: function() {
      return !1;
    },
    enqueueForceUpdate: function() {
    },
    enqueueReplaceState: function() {
    },
    enqueueSetState: function() {
    }
  }, K = Object.assign, oe = {};
  function S(t, o, c) {
    this.props = t, this.context = o, this.refs = oe, this.updater = c || Q;
  }
  S.prototype.isReactComponent = {}, S.prototype.setState = function(t, o) {
    if (typeof t != "object" && typeof t != "function" && t != null)
      throw Error(
        "takes an object of state variables to update or a function which returns an object of state variables."
      );
    this.updater.enqueueSetState(this, t, o, "setState");
  }, S.prototype.forceUpdate = function(t) {
    this.updater.enqueueForceUpdate(this, t, "forceUpdate");
  };
  function D() {
  }
  D.prototype = S.prototype;
  function X(t, o, c) {
    this.props = t, this.context = o, this.refs = oe, this.updater = c || Q;
  }
  var H = X.prototype = new D();
  H.constructor = X, K(H, S.prototype), H.isPureReactComponent = !0;
  var A = Array.isArray;
  function Z() {
  }
  var m = { H: null, A: null, T: null, S: null }, ue = Object.prototype.hasOwnProperty;
  function C(t, o, c) {
    var a = c.ref;
    return {
      $$typeof: N,
      type: t,
      key: o,
      ref: a !== void 0 ? a : null,
      props: c
    };
  }
  function I(t, o) {
    return C(t.type, o, t.props);
  }
  function F(t) {
    return typeof t == "object" && t !== null && t.$$typeof === N;
  }
  function g(t) {
    var o = { "=": "=0", ":": "=2" };
    return "$" + t.replace(/[=:]/g, function(c) {
      return o[c];
    });
  }
  var V = /\/+/g;
  function k(t, o) {
    return typeof t == "object" && t !== null && t.key != null ? g("" + t.key) : o.toString(36);
  }
  function $(t) {
    switch (t.status) {
      case "fulfilled":
        return t.value;
      case "rejected":
        throw t.reason;
      default:
        switch (typeof t.status == "string" ? t.then(Z, Z) : (t.status = "pending", t.then(
          function(o) {
            t.status === "pending" && (t.status = "fulfilled", t.value = o);
          },
          function(o) {
            t.status === "pending" && (t.status = "rejected", t.reason = o);
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
  function O(t, o, c, a, y) {
    var v = typeof t;
    (v === "undefined" || v === "boolean") && (t = null);
    var E = !1;
    if (t === null) E = !0;
    else
      switch (v) {
        case "bigint":
        case "string":
        case "number":
          E = !0;
          break;
        case "object":
          switch (t.$$typeof) {
            case N:
            case i:
              E = !0;
              break;
            case M:
              return E = t._init, O(
                E(t._payload),
                o,
                c,
                a,
                y
              );
          }
      }
    if (E)
      return y = y(t), E = a === "" ? "." + k(t, 0) : a, A(y) ? (c = "", E != null && (c = E.replace(V, "$&/") + "/"), O(y, o, c, "", function(L) {
        return L;
      })) : y != null && (F(y) && (y = I(
        y,
        c + (y.key == null || t && t.key === y.key ? "" : ("" + y.key).replace(
          V,
          "$&/"
        ) + "/") + E
      )), o.push(y)), 1;
    E = 0;
    var R = a === "" ? "." : a + ":";
    if (A(t))
      for (var w = 0; w < t.length; w++)
        a = t[w], v = R + k(a, w), E += O(
          a,
          o,
          c,
          v,
          y
        );
    else if (w = re(t), typeof w == "function")
      for (t = w.call(t), w = 0; !(a = t.next()).done; )
        a = a.value, v = R + k(a, w++), E += O(
          a,
          o,
          c,
          v,
          y
        );
    else if (v === "object") {
      if (typeof t.then == "function")
        return O(
          $(t),
          o,
          c,
          a,
          y
        );
      throw o = String(t), Error(
        "Objects are not valid as a React child (found: " + (o === "[object Object]" ? "object with keys {" + Object.keys(t).join(", ") + "}" : o) + "). If you meant to render a collection of children, use an array instead."
      );
    }
    return E;
  }
  function P(t, o, c) {
    if (t == null) return t;
    var a = [], y = 0;
    return O(t, a, "", "", function(v) {
      return o.call(c, v, y++);
    }), a;
  }
  function x(t) {
    if (t._status === -1) {
      var o = t._result;
      o = o(), o.then(
        function(c) {
          (t._status === 0 || t._status === -1) && (t._status = 1, t._result = c);
        },
        function(c) {
          (t._status === 0 || t._status === -1) && (t._status = 2, t._result = c);
        }
      ), t._status === -1 && (t._status = 0, t._result = o);
    }
    if (t._status === 1) return t._result.default;
    throw t._result;
  }
  var U = typeof reportError == "function" ? reportError : function(t) {
    if (typeof window == "object" && typeof window.ErrorEvent == "function") {
      var o = new window.ErrorEvent("error", {
        bubbles: !0,
        cancelable: !0,
        message: typeof t == "object" && t !== null && typeof t.message == "string" ? String(t.message) : String(t),
        error: t
      });
      if (!window.dispatchEvent(o)) return;
    } else if (typeof process == "object" && typeof process.emit == "function") {
      process.emit("uncaughtException", t);
      return;
    }
    console.error(t);
  }, se = {
    map: P,
    forEach: function(t, o, c) {
      P(
        t,
        function() {
          o.apply(this, arguments);
        },
        c
      );
    },
    count: function(t) {
      var o = 0;
      return P(t, function() {
        o++;
      }), o;
    },
    toArray: function(t) {
      return P(t, function(o) {
        return o;
      }) || [];
    },
    only: function(t) {
      if (!F(t))
        throw Error(
          "React.Children.only expected to receive a single React element child."
        );
      return t;
    }
  };
  return f.Activity = Y, f.Children = se, f.Component = S, f.Fragment = pe, f.Profiler = z, f.PureComponent = X, f.StrictMode = de, f.Suspense = G, f.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = m, f.__COMPILER_RUNTIME = {
    __proto__: null,
    c: function(t) {
      return m.H.useMemoCache(t);
    }
  }, f.cache = function(t) {
    return function() {
      return t.apply(null, arguments);
    };
  }, f.cacheSignal = function() {
    return null;
  }, f.cloneElement = function(t, o, c) {
    if (t == null)
      throw Error(
        "The argument must be a React element, but you passed " + t + "."
      );
    var a = K({}, t.props), y = t.key;
    if (o != null)
      for (v in o.key !== void 0 && (y = "" + o.key), o)
        !ue.call(o, v) || v === "key" || v === "__self" || v === "__source" || v === "ref" && o.ref === void 0 || (a[v] = o[v]);
    var v = arguments.length - 2;
    if (v === 1) a.children = c;
    else if (1 < v) {
      for (var E = Array(v), R = 0; R < v; R++)
        E[R] = arguments[R + 2];
      a.children = E;
    }
    return C(t.type, y, a);
  }, f.createContext = function(t) {
    return t = {
      $$typeof: te,
      _currentValue: t,
      _currentValue2: t,
      _threadCount: 0,
      Provider: null,
      Consumer: null
    }, t.Provider = t, t.Consumer = {
      $$typeof: b,
      _context: t
    }, t;
  }, f.createElement = function(t, o, c) {
    var a, y = {}, v = null;
    if (o != null)
      for (a in o.key !== void 0 && (v = "" + o.key), o)
        ue.call(o, a) && a !== "key" && a !== "__self" && a !== "__source" && (y[a] = o[a]);
    var E = arguments.length - 2;
    if (E === 1) y.children = c;
    else if (1 < E) {
      for (var R = Array(E), w = 0; w < E; w++)
        R[w] = arguments[w + 2];
      y.children = R;
    }
    if (t && t.defaultProps)
      for (a in E = t.defaultProps, E)
        y[a] === void 0 && (y[a] = E[a]);
    return C(t, v, y);
  }, f.createRef = function() {
    return { current: null };
  }, f.forwardRef = function(t) {
    return { $$typeof: W, render: t };
  }, f.isValidElement = F, f.lazy = function(t) {
    return {
      $$typeof: M,
      _payload: { _status: -1, _result: t },
      _init: x
    };
  }, f.memo = function(t, o) {
    return {
      $$typeof: ne,
      type: t,
      compare: o === void 0 ? null : o
    };
  }, f.startTransition = function(t) {
    var o = m.T, c = {};
    m.T = c;
    try {
      var a = t(), y = m.S;
      y !== null && y(c, a), typeof a == "object" && a !== null && typeof a.then == "function" && a.then(Z, U);
    } catch (v) {
      U(v);
    } finally {
      o !== null && c.types !== null && (o.types = c.types), m.T = o;
    }
  }, f.unstable_useCacheRefresh = function() {
    return m.H.useCacheRefresh();
  }, f.use = function(t) {
    return m.H.use(t);
  }, f.useActionState = function(t, o, c) {
    return m.H.useActionState(t, o, c);
  }, f.useCallback = function(t, o) {
    return m.H.useCallback(t, o);
  }, f.useContext = function(t) {
    return m.H.useContext(t);
  }, f.useDebugValue = function() {
  }, f.useDeferredValue = function(t, o) {
    return m.H.useDeferredValue(t, o);
  }, f.useEffect = function(t, o) {
    return m.H.useEffect(t, o);
  }, f.useEffectEvent = function(t) {
    return m.H.useEffectEvent(t);
  }, f.useId = function() {
    return m.H.useId();
  }, f.useImperativeHandle = function(t, o, c) {
    return m.H.useImperativeHandle(t, o, c);
  }, f.useInsertionEffect = function(t, o) {
    return m.H.useInsertionEffect(t, o);
  }, f.useLayoutEffect = function(t, o) {
    return m.H.useLayoutEffect(t, o);
  }, f.useMemo = function(t, o) {
    return m.H.useMemo(t, o);
  }, f.useOptimistic = function(t, o) {
    return m.H.useOptimistic(t, o);
  }, f.useReducer = function(t, o, c) {
    return m.H.useReducer(t, o, c);
  }, f.useRef = function(t) {
    return m.H.useRef(t);
  }, f.useState = function(t) {
    return m.H.useState(t);
  }, f.useSyncExternalStore = function(t, o, c) {
    return m.H.useSyncExternalStore(
      t,
      o,
      c
    );
  }, f.useTransition = function() {
    return m.H.useTransition();
  }, f.version = "19.2.7", f;
}
var ee = { exports: {} };
ee.exports;
var Ne;
function Ue() {
  return Ne || (Ne = 1, (function(N, i) {
    process.env.NODE_ENV !== "production" && (function() {
      function pe(e, n) {
        Object.defineProperty(b.prototype, e, {
          get: function() {
            console.warn(
              "%s(...) is deprecated in plain JavaScript React classes. %s",
              n[0],
              n[1]
            );
          }
        });
      }
      function de(e) {
        return e === null || typeof e != "object" ? null : (e = me && e[me] || e["@@iterator"], typeof e == "function" ? e : null);
      }
      function z(e, n) {
        e = (e = e.constructor) && (e.displayName || e.name) || "ReactClass";
        var r = e + "." + n;
        Ee[r] || (console.error(
          "Can't call %s on a component that is not yet mounted. This is a no-op, but it might indicate a bug in your application. Instead, assign to `this.state` directly or define a `state = {};` class property with the desired state in the %s component.",
          n,
          e
        ), Ee[r] = !0);
      }
      function b(e, n, r) {
        this.props = e, this.context = n, this.refs = _e, this.updater = r || he;
      }
      function te() {
      }
      function W(e, n, r) {
        this.props = e, this.context = n, this.refs = _e, this.updater = r || he;
      }
      function G() {
      }
      function ne(e) {
        return "" + e;
      }
      function M(e) {
        try {
          ne(e);
          var n = !1;
        } catch {
          n = !0;
        }
        if (n) {
          n = console;
          var r = n.error, u = typeof Symbol == "function" && Symbol.toStringTag && e[Symbol.toStringTag] || e.constructor.name || "Object";
          return r.call(
            n,
            "The provided key is an unsupported type %s. This value must be coerced to a string before using it here.",
            u
          ), ne(e);
        }
      }
      function Y(e) {
        if (e == null) return null;
        if (typeof e == "function")
          return e.$$typeof === $e ? null : e.displayName || e.name || null;
        if (typeof e == "string") return e;
        switch (e) {
          case t:
            return "Fragment";
          case c:
            return "Profiler";
          case o:
            return "StrictMode";
          case E:
            return "Suspense";
          case R:
            return "SuspenseList";
          case ve:
            return "Activity";
        }
        if (typeof e == "object")
          switch (typeof e.tag == "number" && console.error(
            "Received an unexpected object in getComponentNameFromType(). This is likely a bug in React. Please file an issue."
          ), e.$$typeof) {
            case se:
              return "Portal";
            case y:
              return e.displayName || "Context";
            case a:
              return (e._context.displayName || "Context") + ".Consumer";
            case v:
              var n = e.render;
              return e = e.displayName, e || (e = n.displayName || n.name || "", e = e !== "" ? "ForwardRef(" + e + ")" : "ForwardRef"), e;
            case w:
              return n = e.displayName || null, n !== null ? n : Y(e.type) || "Memo";
            case L:
              n = e._payload, e = e._init;
              try {
                return Y(e(n));
              } catch {
              }
          }
        return null;
      }
      function B(e) {
        if (e === t) return "<>";
        if (typeof e == "object" && e !== null && e.$$typeof === L)
          return "<...>";
        try {
          var n = Y(e);
          return n ? "<" + n + ">" : "<...>";
        } catch {
          return "<...>";
        }
      }
      function re() {
        var e = p.A;
        return e === null ? null : e.getOwner();
      }
      function Q() {
        return Error("react-stack-top-frame");
      }
      function K(e) {
        if (ae.call(e, "key")) {
          var n = Object.getOwnPropertyDescriptor(e, "key").get;
          if (n && n.isReactWarning) return !1;
        }
        return e.key !== void 0;
      }
      function oe(e, n) {
        function r() {
          Te || (Te = !0, console.error(
            "%s: `key` is not a prop. Trying to access it will result in `undefined` being returned. If you need to access the same value within the child component, you should pass it as a different prop. (https://react.dev/link/special-props)",
            n
          ));
        }
        r.isReactWarning = !0, Object.defineProperty(e, "key", {
          get: r,
          configurable: !0
        });
      }
      function S() {
        var e = Y(this.type);
        return Ce[e] || (Ce[e] = !0, console.error(
          "Accessing element.ref was removed in React 19. ref is now a regular prop. It will be removed from the JSX Element type in a future release."
        )), e = this.props.ref, e !== void 0 ? e : null;
      }
      function D(e, n, r, u, s, d) {
        var l = r.ref;
        return e = {
          $$typeof: U,
          type: e,
          key: n,
          props: r,
          _owner: u
        }, (l !== void 0 ? l : null) !== null ? Object.defineProperty(e, "ref", {
          enumerable: !1,
          get: S
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
          value: s
        }), Object.defineProperty(e, "_debugTask", {
          configurable: !1,
          enumerable: !1,
          writable: !0,
          value: d
        }), Object.freeze && (Object.freeze(e.props), Object.freeze(e)), e;
      }
      function X(e, n) {
        return n = D(
          e.type,
          n,
          e.props,
          e._owner,
          e._debugStack,
          e._debugTask
        ), e._store && (n._store.validated = e._store.validated), n;
      }
      function H(e) {
        A(e) ? e._store && (e._store.validated = 1) : typeof e == "object" && e !== null && e.$$typeof === L && (e._payload.status === "fulfilled" ? A(e._payload.value) && e._payload.value._store && (e._payload.value._store.validated = 1) : e._store && (e._store.validated = 1));
      }
      function A(e) {
        return typeof e == "object" && e !== null && e.$$typeof === U;
      }
      function Z(e) {
        var n = { "=": "=0", ":": "=2" };
        return "$" + e.replace(/[=:]/g, function(r) {
          return n[r];
        });
      }
      function m(e, n) {
        return typeof e == "object" && e !== null && e.key != null ? (M(e.key), Z("" + e.key)) : n.toString(36);
      }
      function ue(e) {
        switch (e.status) {
          case "fulfilled":
            return e.value;
          case "rejected":
            throw e.reason;
          default:
            switch (typeof e.status == "string" ? e.then(G, G) : (e.status = "pending", e.then(
              function(n) {
                e.status === "pending" && (e.status = "fulfilled", e.value = n);
              },
              function(n) {
                e.status === "pending" && (e.status = "rejected", e.reason = n);
              }
            )), e.status) {
              case "fulfilled":
                return e.value;
              case "rejected":
                throw e.reason;
            }
        }
        throw e;
      }
      function C(e, n, r, u, s) {
        var d = typeof e;
        (d === "undefined" || d === "boolean") && (e = null);
        var l = !1;
        if (e === null) l = !0;
        else
          switch (d) {
            case "bigint":
            case "string":
            case "number":
              l = !0;
              break;
            case "object":
              switch (e.$$typeof) {
                case U:
                case se:
                  l = !0;
                  break;
                case L:
                  return l = e._init, C(
                    l(e._payload),
                    n,
                    r,
                    u,
                    s
                  );
              }
          }
        if (l) {
          l = e, s = s(l);
          var h = u === "" ? "." + m(l, 0) : u;
          return we(s) ? (r = "", h != null && (r = h.replace(Ae, "$&/") + "/"), C(s, n, r, "", function(j) {
            return j;
          })) : s != null && (A(s) && (s.key != null && (l && l.key === s.key || M(s.key)), r = X(
            s,
            r + (s.key == null || l && l.key === s.key ? "" : ("" + s.key).replace(
              Ae,
              "$&/"
            ) + "/") + h
          ), u !== "" && l != null && A(l) && l.key == null && l._store && !l._store.validated && (r._store.validated = 2), s = r), n.push(s)), 1;
        }
        if (l = 0, h = u === "" ? "." : u + ":", we(e))
          for (var _ = 0; _ < e.length; _++)
            u = e[_], d = h + m(u, _), l += C(
              u,
              n,
              r,
              d,
              s
            );
        else if (_ = de(e), typeof _ == "function")
          for (_ === e.entries && (be || console.warn(
            "Using Maps as children is not supported. Use an array of keyed ReactElements instead."
          ), be = !0), e = _.call(e), _ = 0; !(u = e.next()).done; )
            u = u.value, d = h + m(u, _++), l += C(
              u,
              n,
              r,
              d,
              s
            );
        else if (d === "object") {
          if (typeof e.then == "function")
            return C(
              ue(e),
              n,
              r,
              u,
              s
            );
          throw n = String(e), Error(
            "Objects are not valid as a React child (found: " + (n === "[object Object]" ? "object with keys {" + Object.keys(e).join(", ") + "}" : n) + "). If you meant to render a collection of children, use an array instead."
          );
        }
        return l;
      }
      function I(e, n, r) {
        if (e == null) return e;
        var u = [], s = 0;
        return C(e, u, "", "", function(d) {
          return n.call(r, d, s++);
        }), u;
      }
      function F(e) {
        if (e._status === -1) {
          var n = e._ioInfo;
          n != null && (n.start = n.end = performance.now()), n = e._result;
          var r = n();
          if (r.then(
            function(s) {
              if (e._status === 0 || e._status === -1) {
                e._status = 1, e._result = s;
                var d = e._ioInfo;
                d != null && (d.end = performance.now()), r.status === void 0 && (r.status = "fulfilled", r.value = s);
              }
            },
            function(s) {
              if (e._status === 0 || e._status === -1) {
                e._status = 2, e._result = s;
                var d = e._ioInfo;
                d != null && (d.end = performance.now()), r.status === void 0 && (r.status = "rejected", r.reason = s);
              }
            }
          ), n = e._ioInfo, n != null) {
            n.value = r;
            var u = r.displayName;
            typeof u == "string" && (n.name = u);
          }
          e._status === -1 && (e._status = 0, e._result = r);
        }
        if (e._status === 1)
          return n = e._result, n === void 0 && console.error(
            `lazy: Expected the result of a dynamic import() call. Instead received: %s

Your code should look like:
  const MyComponent = lazy(() => import('./MyComponent'))

Did you accidentally put curly braces around the import?`,
            n
          ), "default" in n || console.error(
            `lazy: Expected the result of a dynamic import() call. Instead received: %s

Your code should look like:
  const MyComponent = lazy(() => import('./MyComponent'))`,
            n
          ), n.default;
        throw e._result;
      }
      function g() {
        var e = p.H;
        return e === null && console.error(
          `Invalid hook call. Hooks can only be called inside of the body of a function component. This could happen for one of the following reasons:
1. You might have mismatching versions of React and the renderer (such as React DOM)
2. You might be breaking the Rules of Hooks
3. You might have more than one copy of React in the same app
See https://react.dev/link/invalid-hook-call for tips about how to debug and fix this problem.`
        ), e;
      }
      function V() {
        p.asyncTransitions--;
      }
      function k(e) {
        if (ie === null)
          try {
            var n = ("require" + Math.random()).slice(0, 7);
            ie = (N && N[n]).call(
              N,
              "timers"
            ).setImmediate;
          } catch {
            ie = function(u) {
              ke === !1 && (ke = !0, typeof MessageChannel > "u" && console.error(
                "This browser does not have a MessageChannel implementation, so enqueuing tasks via await act(async () => ...) will fail. Please file an issue at https://github.com/facebook/react/issues if you encounter this warning."
              ));
              var s = new MessageChannel();
              s.port1.onmessage = u, s.port2.postMessage(void 0);
            };
          }
        return ie(e);
      }
      function $(e) {
        return 1 < e.length && typeof AggregateError == "function" ? new AggregateError(e) : e[0];
      }
      function O(e, n) {
        n !== ce - 1 && console.error(
          "You seem to have overlapping act() calls, this is not supported. Be sure to await previous act() calls before making a new one. "
        ), ce = n;
      }
      function P(e, n, r) {
        var u = p.actQueue;
        if (u !== null)
          if (u.length !== 0)
            try {
              x(u), k(function() {
                return P(e, n, r);
              });
              return;
            } catch (s) {
              p.thrownErrors.push(s);
            }
          else p.actQueue = null;
        0 < p.thrownErrors.length ? (u = $(p.thrownErrors), p.thrownErrors.length = 0, r(u)) : n(e);
      }
      function x(e) {
        if (!ye) {
          ye = !0;
          var n = 0;
          try {
            for (; n < e.length; n++) {
              var r = e[n];
              do {
                p.didUsePromise = !1;
                var u = r(!1);
                if (u !== null) {
                  if (p.didUsePromise) {
                    e[n] = r, e.splice(0, n);
                    return;
                  }
                  r = u;
                } else break;
              } while (!0);
            }
            e.length = 0;
          } catch (s) {
            e.splice(0, n + 1), p.thrownErrors.push(s);
          } finally {
            ye = !1;
          }
        }
      }
      typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ < "u" && typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.registerInternalModuleStart == "function" && __REACT_DEVTOOLS_GLOBAL_HOOK__.registerInternalModuleStart(Error());
      var U = /* @__PURE__ */ Symbol.for("react.transitional.element"), se = /* @__PURE__ */ Symbol.for("react.portal"), t = /* @__PURE__ */ Symbol.for("react.fragment"), o = /* @__PURE__ */ Symbol.for("react.strict_mode"), c = /* @__PURE__ */ Symbol.for("react.profiler"), a = /* @__PURE__ */ Symbol.for("react.consumer"), y = /* @__PURE__ */ Symbol.for("react.context"), v = /* @__PURE__ */ Symbol.for("react.forward_ref"), E = /* @__PURE__ */ Symbol.for("react.suspense"), R = /* @__PURE__ */ Symbol.for("react.suspense_list"), w = /* @__PURE__ */ Symbol.for("react.memo"), L = /* @__PURE__ */ Symbol.for("react.lazy"), ve = /* @__PURE__ */ Symbol.for("react.activity"), me = Symbol.iterator, Ee = {}, he = {
        isMounted: function() {
          return !1;
        },
        enqueueForceUpdate: function(e) {
          z(e, "forceUpdate");
        },
        enqueueReplaceState: function(e) {
          z(e, "replaceState");
        },
        enqueueSetState: function(e) {
          z(e, "setState");
        }
      }, ge = Object.assign, _e = {};
      Object.freeze(_e), b.prototype.isReactComponent = {}, b.prototype.setState = function(e, n) {
        if (typeof e != "object" && typeof e != "function" && e != null)
          throw Error(
            "takes an object of state variables to update or a function which returns an object of state variables."
          );
        this.updater.enqueueSetState(this, e, n, "setState");
      }, b.prototype.forceUpdate = function(e) {
        this.updater.enqueueForceUpdate(this, e, "forceUpdate");
      };
      var T = {
        isMounted: [
          "isMounted",
          "Instead, make sure to clean up subscriptions and pending requests in componentWillUnmount to prevent memory leaks."
        ],
        replaceState: [
          "replaceState",
          "Refactor your code to use setState instead (see https://github.com/facebook/react/issues/3236)."
        ]
      };
      for (J in T)
        T.hasOwnProperty(J) && pe(J, T[J]);
      te.prototype = b.prototype, T = W.prototype = new te(), T.constructor = W, ge(T, b.prototype), T.isPureReactComponent = !0;
      var we = Array.isArray, $e = /* @__PURE__ */ Symbol.for("react.client.reference"), p = {
        H: null,
        A: null,
        T: null,
        S: null,
        actQueue: null,
        asyncTransitions: 0,
        isBatchingLegacy: !1,
        didScheduleLegacyUpdate: !1,
        didUsePromise: !1,
        thrownErrors: [],
        getCurrentStack: null,
        recentlyCreatedOwnerStacks: 0
      }, ae = Object.prototype.hasOwnProperty, Re = console.createTask ? console.createTask : function() {
        return null;
      };
      T = {
        react_stack_bottom_frame: function(e) {
          return e();
        }
      };
      var Te, Oe, Ce = {}, Le = T.react_stack_bottom_frame.bind(
        T,
        Q
      )(), Ye = Re(B(Q)), be = !1, Ae = /\/+/g, Se = typeof reportError == "function" ? reportError : function(e) {
        if (typeof window == "object" && typeof window.ErrorEvent == "function") {
          var n = new window.ErrorEvent("error", {
            bubbles: !0,
            cancelable: !0,
            message: typeof e == "object" && e !== null && typeof e.message == "string" ? String(e.message) : String(e),
            error: e
          });
          if (!window.dispatchEvent(n)) return;
        } else if (typeof process == "object" && typeof process.emit == "function") {
          process.emit("uncaughtException", e);
          return;
        }
        console.error(e);
      }, ke = !1, ie = null, ce = 0, fe = !1, ye = !1, Pe = typeof queueMicrotask == "function" ? function(e) {
        queueMicrotask(function() {
          return queueMicrotask(e);
        });
      } : k;
      T = Object.freeze({
        __proto__: null,
        c: function(e) {
          return g().useMemoCache(e);
        }
      });
      var J = {
        map: I,
        forEach: function(e, n, r) {
          I(
            e,
            function() {
              n.apply(this, arguments);
            },
            r
          );
        },
        count: function(e) {
          var n = 0;
          return I(e, function() {
            n++;
          }), n;
        },
        toArray: function(e) {
          return I(e, function(n) {
            return n;
          }) || [];
        },
        only: function(e) {
          if (!A(e))
            throw Error(
              "React.Children.only expected to receive a single React element child."
            );
          return e;
        }
      };
      i.Activity = ve, i.Children = J, i.Component = b, i.Fragment = t, i.Profiler = c, i.PureComponent = W, i.StrictMode = o, i.Suspense = E, i.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = p, i.__COMPILER_RUNTIME = T, i.act = function(e) {
        var n = p.actQueue, r = ce;
        ce++;
        var u = p.actQueue = n !== null ? n : [], s = !1;
        try {
          var d = e();
        } catch (_) {
          p.thrownErrors.push(_);
        }
        if (0 < p.thrownErrors.length)
          throw O(n, r), e = $(p.thrownErrors), p.thrownErrors.length = 0, e;
        if (d !== null && typeof d == "object" && typeof d.then == "function") {
          var l = d;
          return Pe(function() {
            s || fe || (fe = !0, console.error(
              "You called act(async () => ...) without await. This could lead to unexpected testing behaviour, interleaving multiple act calls and mixing their scopes. You should - await act(async () => ...);"
            ));
          }), {
            then: function(_, j) {
              s = !0, l.then(
                function(q) {
                  if (O(n, r), r === 0) {
                    try {
                      x(u), k(function() {
                        return P(
                          q,
                          _,
                          j
                        );
                      });
                    } catch (He) {
                      p.thrownErrors.push(He);
                    }
                    if (0 < p.thrownErrors.length) {
                      var De = $(
                        p.thrownErrors
                      );
                      p.thrownErrors.length = 0, j(De);
                    }
                  } else _(q);
                },
                function(q) {
                  O(n, r), 0 < p.thrownErrors.length && (q = $(
                    p.thrownErrors
                  ), p.thrownErrors.length = 0), j(q);
                }
              );
            }
          };
        }
        var h = d;
        if (O(n, r), r === 0 && (x(u), u.length !== 0 && Pe(function() {
          s || fe || (fe = !0, console.error(
            "A component suspended inside an `act` scope, but the `act` call was not awaited. When testing React components that depend on asynchronous data, you must await the result:\n\nawait act(() => ...)"
          ));
        }), p.actQueue = null), 0 < p.thrownErrors.length)
          throw e = $(p.thrownErrors), p.thrownErrors.length = 0, e;
        return {
          then: function(_, j) {
            s = !0, r === 0 ? (p.actQueue = u, k(function() {
              return P(
                h,
                _,
                j
              );
            })) : _(h);
          }
        };
      }, i.cache = function(e) {
        return function() {
          return e.apply(null, arguments);
        };
      }, i.cacheSignal = function() {
        return null;
      }, i.captureOwnerStack = function() {
        var e = p.getCurrentStack;
        return e === null ? null : e();
      }, i.cloneElement = function(e, n, r) {
        if (e == null)
          throw Error(
            "The argument must be a React element, but you passed " + e + "."
          );
        var u = ge({}, e.props), s = e.key, d = e._owner;
        if (n != null) {
          var l;
          e: {
            if (ae.call(n, "ref") && (l = Object.getOwnPropertyDescriptor(
              n,
              "ref"
            ).get) && l.isReactWarning) {
              l = !1;
              break e;
            }
            l = n.ref !== void 0;
          }
          l && (d = re()), K(n) && (M(n.key), s = "" + n.key);
          for (h in n)
            !ae.call(n, h) || h === "key" || h === "__self" || h === "__source" || h === "ref" && n.ref === void 0 || (u[h] = n[h]);
        }
        var h = arguments.length - 2;
        if (h === 1) u.children = r;
        else if (1 < h) {
          l = Array(h);
          for (var _ = 0; _ < h; _++)
            l[_] = arguments[_ + 2];
          u.children = l;
        }
        for (u = D(
          e.type,
          s,
          u,
          d,
          e._debugStack,
          e._debugTask
        ), s = 2; s < arguments.length; s++)
          H(arguments[s]);
        return u;
      }, i.createContext = function(e) {
        return e = {
          $$typeof: y,
          _currentValue: e,
          _currentValue2: e,
          _threadCount: 0,
          Provider: null,
          Consumer: null
        }, e.Provider = e, e.Consumer = {
          $$typeof: a,
          _context: e
        }, e._currentRenderer = null, e._currentRenderer2 = null, e;
      }, i.createElement = function(e, n, r) {
        for (var u = 2; u < arguments.length; u++)
          H(arguments[u]);
        u = {};
        var s = null;
        if (n != null)
          for (_ in Oe || !("__self" in n) || "key" in n || (Oe = !0, console.warn(
            "Your app (or one of its dependencies) is using an outdated JSX transform. Update to the modern JSX transform for faster performance: https://react.dev/link/new-jsx-transform"
          )), K(n) && (M(n.key), s = "" + n.key), n)
            ae.call(n, _) && _ !== "key" && _ !== "__self" && _ !== "__source" && (u[_] = n[_]);
        var d = arguments.length - 2;
        if (d === 1) u.children = r;
        else if (1 < d) {
          for (var l = Array(d), h = 0; h < d; h++)
            l[h] = arguments[h + 2];
          Object.freeze && Object.freeze(l), u.children = l;
        }
        if (e && e.defaultProps)
          for (_ in d = e.defaultProps, d)
            u[_] === void 0 && (u[_] = d[_]);
        s && oe(
          u,
          typeof e == "function" ? e.displayName || e.name || "Unknown" : e
        );
        var _ = 1e4 > p.recentlyCreatedOwnerStacks++;
        return D(
          e,
          s,
          u,
          re(),
          _ ? Error("react-stack-top-frame") : Le,
          _ ? Re(B(e)) : Ye
        );
      }, i.createRef = function() {
        var e = { current: null };
        return Object.seal(e), e;
      }, i.forwardRef = function(e) {
        e != null && e.$$typeof === w ? console.error(
          "forwardRef requires a render function but received a `memo` component. Instead of forwardRef(memo(...)), use memo(forwardRef(...))."
        ) : typeof e != "function" ? console.error(
          "forwardRef requires a render function but was given %s.",
          e === null ? "null" : typeof e
        ) : e.length !== 0 && e.length !== 2 && console.error(
          "forwardRef render functions accept exactly two parameters: props and ref. %s",
          e.length === 1 ? "Did you forget to use the ref parameter?" : "Any additional parameter will be undefined."
        ), e != null && e.defaultProps != null && console.error(
          "forwardRef render functions do not support defaultProps. Did you accidentally pass a React component?"
        );
        var n = { $$typeof: v, render: e }, r;
        return Object.defineProperty(n, "displayName", {
          enumerable: !1,
          configurable: !0,
          get: function() {
            return r;
          },
          set: function(u) {
            r = u, e.name || e.displayName || (Object.defineProperty(e, "name", { value: u }), e.displayName = u);
          }
        }), n;
      }, i.isValidElement = A, i.lazy = function(e) {
        e = { _status: -1, _result: e };
        var n = {
          $$typeof: L,
          _payload: e,
          _init: F
        }, r = {
          name: "lazy",
          start: -1,
          end: -1,
          value: null,
          owner: null,
          debugStack: Error("react-stack-top-frame"),
          debugTask: console.createTask ? console.createTask("lazy()") : null
        };
        return e._ioInfo = r, n._debugInfo = [{ awaited: r }], n;
      }, i.memo = function(e, n) {
        e == null && console.error(
          "memo: The first argument must be a component. Instead received: %s",
          e === null ? "null" : typeof e
        ), n = {
          $$typeof: w,
          type: e,
          compare: n === void 0 ? null : n
        };
        var r;
        return Object.defineProperty(n, "displayName", {
          enumerable: !1,
          configurable: !0,
          get: function() {
            return r;
          },
          set: function(u) {
            r = u, e.name || e.displayName || (Object.defineProperty(e, "name", { value: u }), e.displayName = u);
          }
        }), n;
      }, i.startTransition = function(e) {
        var n = p.T, r = {};
        r._updatedFibers = /* @__PURE__ */ new Set(), p.T = r;
        try {
          var u = e(), s = p.S;
          s !== null && s(r, u), typeof u == "object" && u !== null && typeof u.then == "function" && (p.asyncTransitions++, u.then(V, V), u.then(G, Se));
        } catch (d) {
          Se(d);
        } finally {
          n === null && r._updatedFibers && (e = r._updatedFibers.size, r._updatedFibers.clear(), 10 < e && console.warn(
            "Detected a large number of updates inside startTransition. If this is due to a subscription please re-write it to use React provided hooks. Otherwise concurrent mode guarantees are off the table."
          )), n !== null && r.types !== null && (n.types !== null && n.types !== r.types && console.error(
            "We expected inner Transitions to have transferred the outer types set and that you cannot add to the outer Transition while inside the inner.This is a bug in React."
          ), n.types = r.types), p.T = n;
        }
      }, i.unstable_useCacheRefresh = function() {
        return g().useCacheRefresh();
      }, i.use = function(e) {
        return g().use(e);
      }, i.useActionState = function(e, n, r) {
        return g().useActionState(
          e,
          n,
          r
        );
      }, i.useCallback = function(e, n) {
        return g().useCallback(e, n);
      }, i.useContext = function(e) {
        var n = g();
        return e.$$typeof === a && console.error(
          "Calling useContext(Context.Consumer) is not supported and will cause bugs. Did you mean to call useContext(Context) instead?"
        ), n.useContext(e);
      }, i.useDebugValue = function(e, n) {
        return g().useDebugValue(e, n);
      }, i.useDeferredValue = function(e, n) {
        return g().useDeferredValue(e, n);
      }, i.useEffect = function(e, n) {
        return e == null && console.warn(
          "React Hook useEffect requires an effect callback. Did you forget to pass a callback to the hook?"
        ), g().useEffect(e, n);
      }, i.useEffectEvent = function(e) {
        return g().useEffectEvent(e);
      }, i.useId = function() {
        return g().useId();
      }, i.useImperativeHandle = function(e, n, r) {
        return g().useImperativeHandle(e, n, r);
      }, i.useInsertionEffect = function(e, n) {
        return e == null && console.warn(
          "React Hook useInsertionEffect requires an effect callback. Did you forget to pass a callback to the hook?"
        ), g().useInsertionEffect(e, n);
      }, i.useLayoutEffect = function(e, n) {
        return e == null && console.warn(
          "React Hook useLayoutEffect requires an effect callback. Did you forget to pass a callback to the hook?"
        ), g().useLayoutEffect(e, n);
      }, i.useMemo = function(e, n) {
        return g().useMemo(e, n);
      }, i.useOptimistic = function(e, n) {
        return g().useOptimistic(e, n);
      }, i.useReducer = function(e, n, r) {
        return g().useReducer(e, n, r);
      }, i.useRef = function(e) {
        return g().useRef(e);
      }, i.useState = function(e) {
        return g().useState(e);
      }, i.useSyncExternalStore = function(e, n, r) {
        return g().useSyncExternalStore(
          e,
          n,
          r
        );
      }, i.useTransition = function() {
        return g().useTransition();
      }, i.version = "19.2.7", typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ < "u" && typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.registerInternalModuleStop == "function" && __REACT_DEVTOOLS_GLOBAL_HOOK__.registerInternalModuleStop(Error());
    })();
  })(ee, ee.exports)), ee.exports;
}
var Me;
function qe() {
  return Me || (Me = 1, process.env.NODE_ENV === "production" ? le.exports = Ie() : le.exports = Ue()), le.exports;
}
export {
  qe as r
};
