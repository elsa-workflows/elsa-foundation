import { r as h1 } from "./chunks/index.js";
var ti = { exports: {} }, me = {}, ai = { exports: {} }, ui = {};
var c1;
function Cd() {
  return c1 || (c1 = 1, (function(D) {
    function Nl(z, E) {
      var N = z.length;
      z.push(E);
      l: for (; 0 < N; ) {
        var w = N - 1 >>> 1, $ = z[w];
        if (0 < dt($, E))
          z[w] = E, z[N] = $, N = w;
        else break l;
      }
    }
    function al(z) {
      return z.length === 0 ? null : z[0];
    }
    function g(z) {
      if (z.length === 0) return null;
      var E = z[0], N = z.pop();
      if (N !== E) {
        z[0] = N;
        l: for (var w = 0, $ = z.length, Al = $ >>> 1; w < Al; ) {
          var P = 2 * (w + 1) - 1, V = z[P], ml = P + 1, ct = z[ml];
          if (0 > dt(V, N))
            ml < $ && 0 > dt(ct, V) ? (z[w] = ct, z[ml] = N, w = ml) : (z[w] = V, z[P] = N, w = P);
          else if (ml < $ && 0 > dt(ct, N))
            z[w] = ct, z[ml] = N, w = ml;
          else break l;
        }
      }
      return E;
    }
    function dt(z, E) {
      var N = z.sortIndex - E.sortIndex;
      return N !== 0 ? N : z.id - E.id;
    }
    if (D.unstable_now = void 0, typeof performance == "object" && typeof performance.now == "function") {
      var ht = performance;
      D.unstable_now = function() {
        return ht.now();
      };
    } else {
      var Pl = Date, St = Pl.now();
      D.unstable_now = function() {
        return Pl.now() - St;
      };
    }
    var U = [], A = [], r = 1, H = null, ul = 3, lt = !1, tt = !1, Vl = !1, Ha = !1, Na = typeof setTimeout == "function" ? setTimeout : null, bu = typeof clearTimeout == "function" ? clearTimeout : null, pl = typeof setImmediate < "u" ? setImmediate : null;
    function Xt(z) {
      for (var E = al(A); E !== null; ) {
        if (E.callback === null) g(A);
        else if (E.startTime <= z)
          g(A), E.sortIndex = E.expirationTime, Nl(U, E);
        else break;
        E = al(A);
      }
    }
    function ia(z) {
      if (Vl = !1, Xt(z), !tt)
        if (al(U) !== null)
          tt = !0, ft || (ft = !0, Kl());
        else {
          var E = al(A);
          E !== null && st(ia, E.startTime - z);
        }
    }
    var ft = !1, gt = -1, at = 5, qa = -1;
    function de() {
      return Ha ? !0 : !(D.unstable_now() - qa < at);
    }
    function Ba() {
      if (Ha = !1, ft) {
        var z = D.unstable_now();
        qa = z;
        var E = !0;
        try {
          l: {
            tt = !1, Vl && (Vl = !1, bu(gt), gt = -1), lt = !0;
            var N = ul;
            try {
              t: {
                for (Xt(z), H = al(U); H !== null && !(H.expirationTime > z && de()); ) {
                  var w = H.callback;
                  if (typeof w == "function") {
                    H.callback = null, ul = H.priorityLevel;
                    var $ = w(
                      H.expirationTime <= z
                    );
                    if (z = D.unstable_now(), typeof $ == "function") {
                      H.callback = $, Xt(z), E = !0;
                      break t;
                    }
                    H === al(U) && g(U), Xt(z);
                  } else g(U);
                  H = al(U);
                }
                if (H !== null) E = !0;
                else {
                  var Al = al(A);
                  Al !== null && st(
                    ia,
                    Al.startTime - z
                  ), E = !1;
                }
              }
              break l;
            } finally {
              H = null, ul = N, lt = !1;
            }
            E = void 0;
          }
        } finally {
          E ? Kl() : ft = !1;
        }
      }
    }
    var Kl;
    if (typeof pl == "function")
      Kl = function() {
        pl(Ba);
      };
    else if (typeof MessageChannel < "u") {
      var he = new MessageChannel(), zu = he.port2;
      he.port1.onmessage = Ba, Kl = function() {
        zu.postMessage(null);
      };
    } else
      Kl = function() {
        Na(Ba, 0);
      };
    function st(z, E) {
      gt = Na(function() {
        z(D.unstable_now());
      }, E);
    }
    D.unstable_IdlePriority = 5, D.unstable_ImmediatePriority = 1, D.unstable_LowPriority = 4, D.unstable_NormalPriority = 3, D.unstable_Profiling = null, D.unstable_UserBlockingPriority = 2, D.unstable_cancelCallback = function(z) {
      z.callback = null;
    }, D.unstable_forceFrameRate = function(z) {
      0 > z || 125 < z ? console.error(
        "forceFrameRate takes a positive int between 0 and 125, forcing frame rates higher than 125 fps is not supported"
      ) : at = 0 < z ? Math.floor(1e3 / z) : 5;
    }, D.unstable_getCurrentPriorityLevel = function() {
      return ul;
    }, D.unstable_next = function(z) {
      switch (ul) {
        case 1:
        case 2:
        case 3:
          var E = 3;
          break;
        default:
          E = ul;
      }
      var N = ul;
      ul = E;
      try {
        return z();
      } finally {
        ul = N;
      }
    }, D.unstable_requestPaint = function() {
      Ha = !0;
    }, D.unstable_runWithPriority = function(z, E) {
      switch (z) {
        case 1:
        case 2:
        case 3:
        case 4:
        case 5:
          break;
        default:
          z = 3;
      }
      var N = ul;
      ul = z;
      try {
        return E();
      } finally {
        ul = N;
      }
    }, D.unstable_scheduleCallback = function(z, E, N) {
      var w = D.unstable_now();
      switch (typeof N == "object" && N !== null ? (N = N.delay, N = typeof N == "number" && 0 < N ? w + N : w) : N = w, z) {
        case 1:
          var $ = -1;
          break;
        case 2:
          $ = 250;
          break;
        case 5:
          $ = 1073741823;
          break;
        case 4:
          $ = 1e4;
          break;
        default:
          $ = 5e3;
      }
      return $ = N + $, z = {
        id: r++,
        callback: E,
        priorityLevel: z,
        startTime: N,
        expirationTime: $,
        sortIndex: -1
      }, N > w ? (z.sortIndex = N, Nl(A, z), al(U) === null && z === al(A) && (Vl ? (bu(gt), gt = -1) : Vl = !0, st(ia, N - w))) : (z.sortIndex = $, Nl(U, z), tt || lt || (tt = !0, ft || (ft = !0, Kl()))), z;
    }, D.unstable_shouldYield = de, D.unstable_wrapCallback = function(z) {
      var E = ul;
      return function() {
        var N = ul;
        ul = E;
        try {
          return z.apply(this, arguments);
        } finally {
          ul = N;
        }
      };
    };
  })(ui)), ui;
}
var i1;
function Zd() {
  return i1 || (i1 = 1, ai.exports = Cd()), ai.exports;
}
var ei = { exports: {} }, Tl = {};
var v1;
function jd() {
  if (v1) return Tl;
  v1 = 1;
  var D = h1();
  function Nl(U) {
    var A = "https://react.dev/errors/" + U;
    if (1 < arguments.length) {
      A += "?args[]=" + encodeURIComponent(arguments[1]);
      for (var r = 2; r < arguments.length; r++)
        A += "&args[]=" + encodeURIComponent(arguments[r]);
    }
    return "Minified React error #" + U + "; visit " + A + " for the full message or use the non-minified dev environment for full errors and additional helpful warnings.";
  }
  function al() {
  }
  var g = {
    d: {
      f: al,
      r: function() {
        throw Error(Nl(522));
      },
      D: al,
      C: al,
      L: al,
      m: al,
      X: al,
      S: al,
      M: al
    },
    p: 0,
    findDOMNode: null
  }, dt = /* @__PURE__ */ Symbol.for("react.portal");
  function ht(U, A, r) {
    var H = 3 < arguments.length && arguments[3] !== void 0 ? arguments[3] : null;
    return {
      $$typeof: dt,
      key: H == null ? null : "" + H,
      children: U,
      containerInfo: A,
      implementation: r
    };
  }
  var Pl = D.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE;
  function St(U, A) {
    if (U === "font") return "";
    if (typeof A == "string")
      return A === "use-credentials" ? A : "";
  }
  return Tl.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = g, Tl.createPortal = function(U, A) {
    var r = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!A || A.nodeType !== 1 && A.nodeType !== 9 && A.nodeType !== 11)
      throw Error(Nl(299));
    return ht(U, A, null, r);
  }, Tl.flushSync = function(U) {
    var A = Pl.T, r = g.p;
    try {
      if (Pl.T = null, g.p = 2, U) return U();
    } finally {
      Pl.T = A, g.p = r, g.d.f();
    }
  }, Tl.preconnect = function(U, A) {
    typeof U == "string" && (A ? (A = A.crossOrigin, A = typeof A == "string" ? A === "use-credentials" ? A : "" : void 0) : A = null, g.d.C(U, A));
  }, Tl.prefetchDNS = function(U) {
    typeof U == "string" && g.d.D(U);
  }, Tl.preinit = function(U, A) {
    if (typeof U == "string" && A && typeof A.as == "string") {
      var r = A.as, H = St(r, A.crossOrigin), ul = typeof A.integrity == "string" ? A.integrity : void 0, lt = typeof A.fetchPriority == "string" ? A.fetchPriority : void 0;
      r === "style" ? g.d.S(
        U,
        typeof A.precedence == "string" ? A.precedence : void 0,
        {
          crossOrigin: H,
          integrity: ul,
          fetchPriority: lt
        }
      ) : r === "script" && g.d.X(U, {
        crossOrigin: H,
        integrity: ul,
        fetchPriority: lt,
        nonce: typeof A.nonce == "string" ? A.nonce : void 0
      });
    }
  }, Tl.preinitModule = function(U, A) {
    if (typeof U == "string")
      if (typeof A == "object" && A !== null) {
        if (A.as == null || A.as === "script") {
          var r = St(
            A.as,
            A.crossOrigin
          );
          g.d.M(U, {
            crossOrigin: r,
            integrity: typeof A.integrity == "string" ? A.integrity : void 0,
            nonce: typeof A.nonce == "string" ? A.nonce : void 0
          });
        }
      } else A == null && g.d.M(U);
  }, Tl.preload = function(U, A) {
    if (typeof U == "string" && typeof A == "object" && A !== null && typeof A.as == "string") {
      var r = A.as, H = St(r, A.crossOrigin);
      g.d.L(U, r, {
        crossOrigin: H,
        integrity: typeof A.integrity == "string" ? A.integrity : void 0,
        nonce: typeof A.nonce == "string" ? A.nonce : void 0,
        type: typeof A.type == "string" ? A.type : void 0,
        fetchPriority: typeof A.fetchPriority == "string" ? A.fetchPriority : void 0,
        referrerPolicy: typeof A.referrerPolicy == "string" ? A.referrerPolicy : void 0,
        imageSrcSet: typeof A.imageSrcSet == "string" ? A.imageSrcSet : void 0,
        imageSizes: typeof A.imageSizes == "string" ? A.imageSizes : void 0,
        media: typeof A.media == "string" ? A.media : void 0
      });
    }
  }, Tl.preloadModule = function(U, A) {
    if (typeof U == "string")
      if (A) {
        var r = St(A.as, A.crossOrigin);
        g.d.m(U, {
          as: typeof A.as == "string" && A.as !== "script" ? A.as : void 0,
          crossOrigin: r,
          integrity: typeof A.integrity == "string" ? A.integrity : void 0
        });
      } else g.d.m(U);
  }, Tl.requestFormReset = function(U) {
    g.d.r(U);
  }, Tl.unstable_batchedUpdates = function(U, A) {
    return U(A);
  }, Tl.useFormState = function(U, A, r) {
    return Pl.H.useFormState(U, A, r);
  }, Tl.useFormStatus = function() {
    return Pl.H.useHostTransitionStatus();
  }, Tl.version = "19.2.7", Tl;
}
var y1;
function Vd() {
  if (y1) return ei.exports;
  y1 = 1;
  function D() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(D);
      } catch (Nl) {
        console.error(Nl);
      }
  }
  return D(), ei.exports = jd(), ei.exports;
}
var m1;
function pd() {
  if (m1) return me;
  m1 = 1;
  var D = Zd(), Nl = h1(), al = Vd();
  function g(l) {
    var t = "https://react.dev/errors/" + l;
    if (1 < arguments.length) {
      t += "?args[]=" + encodeURIComponent(arguments[1]);
      for (var a = 2; a < arguments.length; a++)
        t += "&args[]=" + encodeURIComponent(arguments[a]);
    }
    return "Minified React error #" + l + "; visit " + t + " for the full message or use the non-minified dev environment for full errors and additional helpful warnings.";
  }
  function dt(l) {
    return !(!l || l.nodeType !== 1 && l.nodeType !== 9 && l.nodeType !== 11);
  }
  function ht(l) {
    var t = l, a = l;
    if (l.alternate) for (; t.return; ) t = t.return;
    else {
      l = t;
      do
        t = l, (t.flags & 4098) !== 0 && (a = t.return), l = t.return;
      while (l);
    }
    return t.tag === 3 ? a : null;
  }
  function Pl(l) {
    if (l.tag === 13) {
      var t = l.memoizedState;
      if (t === null && (l = l.alternate, l !== null && (t = l.memoizedState)), t !== null) return t.dehydrated;
    }
    return null;
  }
  function St(l) {
    if (l.tag === 31) {
      var t = l.memoizedState;
      if (t === null && (l = l.alternate, l !== null && (t = l.memoizedState)), t !== null) return t.dehydrated;
    }
    return null;
  }
  function U(l) {
    if (ht(l) !== l)
      throw Error(g(188));
  }
  function A(l) {
    var t = l.alternate;
    if (!t) {
      if (t = ht(l), t === null) throw Error(g(188));
      return t !== l ? null : l;
    }
    for (var a = l, u = t; ; ) {
      var e = a.return;
      if (e === null) break;
      var n = e.alternate;
      if (n === null) {
        if (u = e.return, u !== null) {
          a = u;
          continue;
        }
        break;
      }
      if (e.child === n.child) {
        for (n = e.child; n; ) {
          if (n === a) return U(e), l;
          if (n === u) return U(e), t;
          n = n.sibling;
        }
        throw Error(g(188));
      }
      if (a.return !== u.return) a = e, u = n;
      else {
        for (var f = !1, c = e.child; c; ) {
          if (c === a) {
            f = !0, a = e, u = n;
            break;
          }
          if (c === u) {
            f = !0, u = e, a = n;
            break;
          }
          c = c.sibling;
        }
        if (!f) {
          for (c = n.child; c; ) {
            if (c === a) {
              f = !0, a = n, u = e;
              break;
            }
            if (c === u) {
              f = !0, u = n, a = e;
              break;
            }
            c = c.sibling;
          }
          if (!f) throw Error(g(189));
        }
      }
      if (a.alternate !== u) throw Error(g(190));
    }
    if (a.tag !== 3) throw Error(g(188));
    return a.stateNode.current === a ? l : t;
  }
  function r(l) {
    var t = l.tag;
    if (t === 5 || t === 26 || t === 27 || t === 6) return l;
    for (l = l.child; l !== null; ) {
      if (t = r(l), t !== null) return t;
      l = l.sibling;
    }
    return null;
  }
  var H = Object.assign, ul = /* @__PURE__ */ Symbol.for("react.element"), lt = /* @__PURE__ */ Symbol.for("react.transitional.element"), tt = /* @__PURE__ */ Symbol.for("react.portal"), Vl = /* @__PURE__ */ Symbol.for("react.fragment"), Ha = /* @__PURE__ */ Symbol.for("react.strict_mode"), Na = /* @__PURE__ */ Symbol.for("react.profiler"), bu = /* @__PURE__ */ Symbol.for("react.consumer"), pl = /* @__PURE__ */ Symbol.for("react.context"), Xt = /* @__PURE__ */ Symbol.for("react.forward_ref"), ia = /* @__PURE__ */ Symbol.for("react.suspense"), ft = /* @__PURE__ */ Symbol.for("react.suspense_list"), gt = /* @__PURE__ */ Symbol.for("react.memo"), at = /* @__PURE__ */ Symbol.for("react.lazy"), qa = /* @__PURE__ */ Symbol.for("react.activity"), de = /* @__PURE__ */ Symbol.for("react.memo_cache_sentinel"), Ba = Symbol.iterator;
  function Kl(l) {
    return l === null || typeof l != "object" ? null : (l = Ba && l[Ba] || l["@@iterator"], typeof l == "function" ? l : null);
  }
  var he = /* @__PURE__ */ Symbol.for("react.client.reference");
  function zu(l) {
    if (l == null) return null;
    if (typeof l == "function")
      return l.$$typeof === he ? null : l.displayName || l.name || null;
    if (typeof l == "string") return l;
    switch (l) {
      case Vl:
        return "Fragment";
      case Na:
        return "Profiler";
      case Ha:
        return "StrictMode";
      case ia:
        return "Suspense";
      case ft:
        return "SuspenseList";
      case qa:
        return "Activity";
    }
    if (typeof l == "object")
      switch (l.$$typeof) {
        case tt:
          return "Portal";
        case pl:
          return l.displayName || "Context";
        case bu:
          return (l._context.displayName || "Context") + ".Consumer";
        case Xt:
          var t = l.render;
          return l = l.displayName, l || (l = t.displayName || t.name || "", l = l !== "" ? "ForwardRef(" + l + ")" : "ForwardRef"), l;
        case gt:
          return t = l.displayName || null, t !== null ? t : zu(l.type) || "Memo";
        case at:
          t = l._payload, l = l._init;
          try {
            return zu(l(t));
          } catch {
          }
      }
    return null;
  }
  var st = Array.isArray, z = Nl.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE, E = al.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE, N = {
    pending: !1,
    data: null,
    method: null,
    action: null
  }, w = [], $ = -1;
  function Al(l) {
    return { current: l };
  }
  function P(l) {
    0 > $ || (l.current = w[$], w[$] = null, $--);
  }
  function V(l, t) {
    $++, w[$] = l.current, l.current = t;
  }
  var ml = Al(null), ct = Al(null), Qt = Al(null), Se = Al(null);
  function ge(l, t) {
    switch (V(Qt, t), V(ct, l), V(ml, null), t.nodeType) {
      case 9:
      case 11:
        l = (l = t.documentElement) && (l = l.namespaceURI) ? qy(l) : 0;
        break;
      default:
        if (l = t.tagName, t = t.namespaceURI)
          t = qy(t), l = By(t, l);
        else
          switch (l) {
            case "svg":
              l = 1;
              break;
            case "math":
              l = 2;
              break;
            default:
              l = 0;
          }
    }
    P(ml), V(ml, l);
  }
  function Ya() {
    P(ml), P(ct), P(Qt);
  }
  function Xn(l) {
    l.memoizedState !== null && V(Se, l);
    var t = ml.current, a = By(t, l.type);
    t !== a && (V(ct, l), V(ml, a));
  }
  function se(l) {
    ct.current === l && (P(ml), P(ct)), Se.current === l && (P(Se), ce._currentValue = N);
  }
  var Qn, fi;
  function va(l) {
    if (Qn === void 0)
      try {
        throw Error();
      } catch (a) {
        var t = a.stack.trim().match(/\n( *(at )?)/);
        Qn = t && t[1] || "", fi = -1 < a.stack.indexOf(`
    at`) ? " (<anonymous>)" : -1 < a.stack.indexOf("@") ? "@unknown:0:0" : "";
      }
    return `
` + Qn + l + fi;
  }
  var Cn = !1;
  function Zn(l, t) {
    if (!l || Cn) return "";
    Cn = !0;
    var a = Error.prepareStackTrace;
    Error.prepareStackTrace = void 0;
    try {
      var u = {
        DetermineComponentFrameRoot: function() {
          try {
            if (t) {
              var T = function() {
                throw Error();
              };
              if (Object.defineProperty(T.prototype, "props", {
                set: function() {
                  throw Error();
                }
              }), typeof Reflect == "object" && Reflect.construct) {
                try {
                  Reflect.construct(T, []);
                } catch (S) {
                  var h = S;
                }
                Reflect.construct(l, [], T);
              } else {
                try {
                  T.call();
                } catch (S) {
                  h = S;
                }
                l.call(T.prototype);
              }
            } else {
              try {
                throw Error();
              } catch (S) {
                h = S;
              }
              (T = l()) && typeof T.catch == "function" && T.catch(function() {
              });
            }
          } catch (S) {
            if (S && h && typeof S.stack == "string")
              return [S.stack, h.stack];
          }
          return [null, null];
        }
      };
      u.DetermineComponentFrameRoot.displayName = "DetermineComponentFrameRoot";
      var e = Object.getOwnPropertyDescriptor(
        u.DetermineComponentFrameRoot,
        "name"
      );
      e && e.configurable && Object.defineProperty(
        u.DetermineComponentFrameRoot,
        "name",
        { value: "DetermineComponentFrameRoot" }
      );
      var n = u.DetermineComponentFrameRoot(), f = n[0], c = n[1];
      if (f && c) {
        var i = f.split(`
`), d = c.split(`
`);
        for (e = u = 0; u < i.length && !i[u].includes("DetermineComponentFrameRoot"); )
          u++;
        for (; e < d.length && !d[e].includes(
          "DetermineComponentFrameRoot"
        ); )
          e++;
        if (u === i.length || e === d.length)
          for (u = i.length - 1, e = d.length - 1; 1 <= u && 0 <= e && i[u] !== d[e]; )
            e--;
        for (; 1 <= u && 0 <= e; u--, e--)
          if (i[u] !== d[e]) {
            if (u !== 1 || e !== 1)
              do
                if (u--, e--, 0 > e || i[u] !== d[e]) {
                  var s = `
` + i[u].replace(" at new ", " at ");
                  return l.displayName && s.includes("<anonymous>") && (s = s.replace("<anonymous>", l.displayName)), s;
                }
              while (1 <= u && 0 <= e);
            break;
          }
      }
    } finally {
      Cn = !1, Error.prepareStackTrace = a;
    }
    return (a = l ? l.displayName || l.name : "") ? va(a) : "";
  }
  function S1(l, t) {
    switch (l.tag) {
      case 26:
      case 27:
      case 5:
        return va(l.type);
      case 16:
        return va("Lazy");
      case 13:
        return l.child !== t && t !== null ? va("Suspense Fallback") : va("Suspense");
      case 19:
        return va("SuspenseList");
      case 0:
      case 15:
        return Zn(l.type, !1);
      case 11:
        return Zn(l.type.render, !1);
      case 1:
        return Zn(l.type, !0);
      case 31:
        return va("Activity");
      default:
        return "";
    }
  }
  function ci(l) {
    try {
      var t = "", a = null;
      do
        t += S1(l, a), a = l, l = l.return;
      while (l);
      return t;
    } catch (u) {
      return `
Error generating stack: ` + u.message + `
` + u.stack;
    }
  }
  var jn = Object.prototype.hasOwnProperty, Vn = D.unstable_scheduleCallback, pn = D.unstable_cancelCallback, g1 = D.unstable_shouldYield, s1 = D.unstable_requestPaint, ql = D.unstable_now, b1 = D.unstable_getCurrentPriorityLevel, ii = D.unstable_ImmediatePriority, vi = D.unstable_UserBlockingPriority, be = D.unstable_NormalPriority, z1 = D.unstable_LowPriority, yi = D.unstable_IdlePriority, T1 = D.log, A1 = D.unstable_setDisableYieldValue, Tu = null, Bl = null;
  function Ct(l) {
    if (typeof T1 == "function" && A1(l), Bl && typeof Bl.setStrictMode == "function")
      try {
        Bl.setStrictMode(Tu, l);
      } catch {
      }
  }
  var Yl = Math.clz32 ? Math.clz32 : O1, E1 = Math.log, o1 = Math.LN2;
  function O1(l) {
    return l >>>= 0, l === 0 ? 32 : 31 - (E1(l) / o1 | 0) | 0;
  }
  var ze = 256, Te = 262144, Ae = 4194304;
  function ya(l) {
    var t = l & 42;
    if (t !== 0) return t;
    switch (l & -l) {
      case 1:
        return 1;
      case 2:
        return 2;
      case 4:
        return 4;
      case 8:
        return 8;
      case 16:
        return 16;
      case 32:
        return 32;
      case 64:
        return 64;
      case 128:
        return 128;
      case 256:
      case 512:
      case 1024:
      case 2048:
      case 4096:
      case 8192:
      case 16384:
      case 32768:
      case 65536:
      case 131072:
        return l & 261888;
      case 262144:
      case 524288:
      case 1048576:
      case 2097152:
        return l & 3932160;
      case 4194304:
      case 8388608:
      case 16777216:
      case 33554432:
        return l & 62914560;
      case 67108864:
        return 67108864;
      case 134217728:
        return 134217728;
      case 268435456:
        return 268435456;
      case 536870912:
        return 536870912;
      case 1073741824:
        return 0;
      default:
        return l;
    }
  }
  function Ee(l, t, a) {
    var u = l.pendingLanes;
    if (u === 0) return 0;
    var e = 0, n = l.suspendedLanes, f = l.pingedLanes;
    l = l.warmLanes;
    var c = u & 134217727;
    return c !== 0 ? (u = c & ~n, u !== 0 ? e = ya(u) : (f &= c, f !== 0 ? e = ya(f) : a || (a = c & ~l, a !== 0 && (e = ya(a))))) : (c = u & ~n, c !== 0 ? e = ya(c) : f !== 0 ? e = ya(f) : a || (a = u & ~l, a !== 0 && (e = ya(a)))), e === 0 ? 0 : t !== 0 && t !== e && (t & n) === 0 && (n = e & -e, a = t & -t, n >= a || n === 32 && (a & 4194048) !== 0) ? t : e;
  }
  function Au(l, t) {
    return (l.pendingLanes & ~(l.suspendedLanes & ~l.pingedLanes) & t) === 0;
  }
  function M1(l, t) {
    switch (l) {
      case 1:
      case 2:
      case 4:
      case 8:
      case 64:
        return t + 250;
      case 16:
      case 32:
      case 128:
      case 256:
      case 512:
      case 1024:
      case 2048:
      case 4096:
      case 8192:
      case 16384:
      case 32768:
      case 65536:
      case 131072:
      case 262144:
      case 524288:
      case 1048576:
      case 2097152:
        return t + 5e3;
      case 4194304:
      case 8388608:
      case 16777216:
      case 33554432:
        return -1;
      case 67108864:
      case 134217728:
      case 268435456:
      case 536870912:
      case 1073741824:
        return -1;
      default:
        return -1;
    }
  }
  function mi() {
    var l = Ae;
    return Ae <<= 1, (Ae & 62914560) === 0 && (Ae = 4194304), l;
  }
  function Kn(l) {
    for (var t = [], a = 0; 31 > a; a++) t.push(l);
    return t;
  }
  function Eu(l, t) {
    l.pendingLanes |= t, t !== 268435456 && (l.suspendedLanes = 0, l.pingedLanes = 0, l.warmLanes = 0);
  }
  function D1(l, t, a, u, e, n) {
    var f = l.pendingLanes;
    l.pendingLanes = a, l.suspendedLanes = 0, l.pingedLanes = 0, l.warmLanes = 0, l.expiredLanes &= a, l.entangledLanes &= a, l.errorRecoveryDisabledLanes &= a, l.shellSuspendCounter = 0;
    var c = l.entanglements, i = l.expirationTimes, d = l.hiddenUpdates;
    for (a = f & ~a; 0 < a; ) {
      var s = 31 - Yl(a), T = 1 << s;
      c[s] = 0, i[s] = -1;
      var h = d[s];
      if (h !== null)
        for (d[s] = null, s = 0; s < h.length; s++) {
          var S = h[s];
          S !== null && (S.lane &= -536870913);
        }
      a &= ~T;
    }
    u !== 0 && di(l, u, 0), n !== 0 && e === 0 && l.tag !== 0 && (l.suspendedLanes |= n & ~(f & ~t));
  }
  function di(l, t, a) {
    l.pendingLanes |= t, l.suspendedLanes &= ~t;
    var u = 31 - Yl(t);
    l.entangledLanes |= t, l.entanglements[u] = l.entanglements[u] | 1073741824 | a & 261930;
  }
  function hi(l, t) {
    var a = l.entangledLanes |= t;
    for (l = l.entanglements; a; ) {
      var u = 31 - Yl(a), e = 1 << u;
      e & t | l[u] & t && (l[u] |= t), a &= ~e;
    }
  }
  function Si(l, t) {
    var a = t & -t;
    return a = (a & 42) !== 0 ? 1 : Ln(a), (a & (l.suspendedLanes | t)) !== 0 ? 0 : a;
  }
  function Ln(l) {
    switch (l) {
      case 2:
        l = 1;
        break;
      case 8:
        l = 4;
        break;
      case 32:
        l = 16;
        break;
      case 256:
      case 512:
      case 1024:
      case 2048:
      case 4096:
      case 8192:
      case 16384:
      case 32768:
      case 65536:
      case 131072:
      case 262144:
      case 524288:
      case 1048576:
      case 2097152:
      case 4194304:
      case 8388608:
      case 16777216:
      case 33554432:
        l = 128;
        break;
      case 268435456:
        l = 134217728;
        break;
      default:
        l = 0;
    }
    return l;
  }
  function xn(l) {
    return l &= -l, 2 < l ? 8 < l ? (l & 134217727) !== 0 ? 32 : 268435456 : 8 : 2;
  }
  function gi() {
    var l = E.p;
    return l !== 0 ? l : (l = window.event, l === void 0 ? 32 : l1(l.type));
  }
  function si(l, t) {
    var a = E.p;
    try {
      return E.p = l, t();
    } finally {
      E.p = a;
    }
  }
  var Zt = Math.random().toString(36).slice(2), Sl = "__reactFiber$" + Zt, ol = "__reactProps$" + Zt, Ga = "__reactContainer$" + Zt, Jn = "__reactEvents$" + Zt, U1 = "__reactListeners$" + Zt, _1 = "__reactHandles$" + Zt, bi = "__reactResources$" + Zt, ou = "__reactMarker$" + Zt;
  function rn(l) {
    delete l[Sl], delete l[ol], delete l[Jn], delete l[U1], delete l[_1];
  }
  function Ra(l) {
    var t = l[Sl];
    if (t) return t;
    for (var a = l.parentNode; a; ) {
      if (t = a[Ga] || a[Sl]) {
        if (a = t.alternate, t.child !== null || a !== null && a.child !== null)
          for (l = Zy(l); l !== null; ) {
            if (a = l[Sl]) return a;
            l = Zy(l);
          }
        return t;
      }
      l = a, a = l.parentNode;
    }
    return null;
  }
  function Xa(l) {
    if (l = l[Sl] || l[Ga]) {
      var t = l.tag;
      if (t === 5 || t === 6 || t === 13 || t === 31 || t === 26 || t === 27 || t === 3)
        return l;
    }
    return null;
  }
  function Ou(l) {
    var t = l.tag;
    if (t === 5 || t === 26 || t === 27 || t === 6) return l.stateNode;
    throw Error(g(33));
  }
  function Qa(l) {
    var t = l[bi];
    return t || (t = l[bi] = { hoistableStyles: /* @__PURE__ */ new Map(), hoistableScripts: /* @__PURE__ */ new Map() }), t;
  }
  function dl(l) {
    l[ou] = !0;
  }
  var zi = /* @__PURE__ */ new Set(), Ti = {};
  function ma(l, t) {
    Ca(l, t), Ca(l + "Capture", t);
  }
  function Ca(l, t) {
    for (Ti[l] = t, l = 0; l < t.length; l++)
      zi.add(t[l]);
  }
  var H1 = RegExp(
    "^[:A-Z_a-z\\u00C0-\\u00D6\\u00D8-\\u00F6\\u00F8-\\u02FF\\u0370-\\u037D\\u037F-\\u1FFF\\u200C-\\u200D\\u2070-\\u218F\\u2C00-\\u2FEF\\u3001-\\uD7FF\\uF900-\\uFDCF\\uFDF0-\\uFFFD][:A-Z_a-z\\u00C0-\\u00D6\\u00D8-\\u00F6\\u00F8-\\u02FF\\u0370-\\u037D\\u037F-\\u1FFF\\u200C-\\u200D\\u2070-\\u218F\\u2C00-\\u2FEF\\u3001-\\uD7FF\\uF900-\\uFDCF\\uFDF0-\\uFFFD\\-.0-9\\u00B7\\u0300-\\u036F\\u203F-\\u2040]*$"
  ), Ai = {}, Ei = {};
  function N1(l) {
    return jn.call(Ei, l) ? !0 : jn.call(Ai, l) ? !1 : H1.test(l) ? Ei[l] = !0 : (Ai[l] = !0, !1);
  }
  function oe(l, t, a) {
    if (N1(t))
      if (a === null) l.removeAttribute(t);
      else {
        switch (typeof a) {
          case "undefined":
          case "function":
          case "symbol":
            l.removeAttribute(t);
            return;
          case "boolean":
            var u = t.toLowerCase().slice(0, 5);
            if (u !== "data-" && u !== "aria-") {
              l.removeAttribute(t);
              return;
            }
        }
        l.setAttribute(t, "" + a);
      }
  }
  function Oe(l, t, a) {
    if (a === null) l.removeAttribute(t);
    else {
      switch (typeof a) {
        case "undefined":
        case "function":
        case "symbol":
        case "boolean":
          l.removeAttribute(t);
          return;
      }
      l.setAttribute(t, "" + a);
    }
  }
  function bt(l, t, a, u) {
    if (u === null) l.removeAttribute(a);
    else {
      switch (typeof u) {
        case "undefined":
        case "function":
        case "symbol":
        case "boolean":
          l.removeAttribute(a);
          return;
      }
      l.setAttributeNS(t, a, "" + u);
    }
  }
  function Ll(l) {
    switch (typeof l) {
      case "bigint":
      case "boolean":
      case "number":
      case "string":
      case "undefined":
        return l;
      case "object":
        return l;
      default:
        return "";
    }
  }
  function oi(l) {
    var t = l.type;
    return (l = l.nodeName) && l.toLowerCase() === "input" && (t === "checkbox" || t === "radio");
  }
  function q1(l, t, a) {
    var u = Object.getOwnPropertyDescriptor(
      l.constructor.prototype,
      t
    );
    if (!l.hasOwnProperty(t) && typeof u < "u" && typeof u.get == "function" && typeof u.set == "function") {
      var e = u.get, n = u.set;
      return Object.defineProperty(l, t, {
        configurable: !0,
        get: function() {
          return e.call(this);
        },
        set: function(f) {
          a = "" + f, n.call(this, f);
        }
      }), Object.defineProperty(l, t, {
        enumerable: u.enumerable
      }), {
        getValue: function() {
          return a;
        },
        setValue: function(f) {
          a = "" + f;
        },
        stopTracking: function() {
          l._valueTracker = null, delete l[t];
        }
      };
    }
  }
  function Wn(l) {
    if (!l._valueTracker) {
      var t = oi(l) ? "checked" : "value";
      l._valueTracker = q1(
        l,
        t,
        "" + l[t]
      );
    }
  }
  function Oi(l) {
    if (!l) return !1;
    var t = l._valueTracker;
    if (!t) return !0;
    var a = t.getValue(), u = "";
    return l && (u = oi(l) ? l.checked ? "true" : "false" : l.value), l = u, l !== a ? (t.setValue(l), !0) : !1;
  }
  function Me(l) {
    if (l = l || (typeof document < "u" ? document : void 0), typeof l > "u") return null;
    try {
      return l.activeElement || l.body;
    } catch {
      return l.body;
    }
  }
  var B1 = /[\n"\\]/g;
  function xl(l) {
    return l.replace(
      B1,
      function(t) {
        return "\\" + t.charCodeAt(0).toString(16) + " ";
      }
    );
  }
  function wn(l, t, a, u, e, n, f, c) {
    l.name = "", f != null && typeof f != "function" && typeof f != "symbol" && typeof f != "boolean" ? l.type = f : l.removeAttribute("type"), t != null ? f === "number" ? (t === 0 && l.value === "" || l.value != t) && (l.value = "" + Ll(t)) : l.value !== "" + Ll(t) && (l.value = "" + Ll(t)) : f !== "submit" && f !== "reset" || l.removeAttribute("value"), t != null ? $n(l, f, Ll(t)) : a != null ? $n(l, f, Ll(a)) : u != null && l.removeAttribute("value"), e == null && n != null && (l.defaultChecked = !!n), e != null && (l.checked = e && typeof e != "function" && typeof e != "symbol"), c != null && typeof c != "function" && typeof c != "symbol" && typeof c != "boolean" ? l.name = "" + Ll(c) : l.removeAttribute("name");
  }
  function Mi(l, t, a, u, e, n, f, c) {
    if (n != null && typeof n != "function" && typeof n != "symbol" && typeof n != "boolean" && (l.type = n), t != null || a != null) {
      if (!(n !== "submit" && n !== "reset" || t != null)) {
        Wn(l);
        return;
      }
      a = a != null ? "" + Ll(a) : "", t = t != null ? "" + Ll(t) : a, c || t === l.value || (l.value = t), l.defaultValue = t;
    }
    u = u ?? e, u = typeof u != "function" && typeof u != "symbol" && !!u, l.checked = c ? l.checked : !!u, l.defaultChecked = !!u, f != null && typeof f != "function" && typeof f != "symbol" && typeof f != "boolean" && (l.name = f), Wn(l);
  }
  function $n(l, t, a) {
    t === "number" && Me(l.ownerDocument) === l || l.defaultValue === "" + a || (l.defaultValue = "" + a);
  }
  function Za(l, t, a, u) {
    if (l = l.options, t) {
      t = {};
      for (var e = 0; e < a.length; e++)
        t["$" + a[e]] = !0;
      for (a = 0; a < l.length; a++)
        e = t.hasOwnProperty("$" + l[a].value), l[a].selected !== e && (l[a].selected = e), e && u && (l[a].defaultSelected = !0);
    } else {
      for (a = "" + Ll(a), t = null, e = 0; e < l.length; e++) {
        if (l[e].value === a) {
          l[e].selected = !0, u && (l[e].defaultSelected = !0);
          return;
        }
        t !== null || l[e].disabled || (t = l[e]);
      }
      t !== null && (t.selected = !0);
    }
  }
  function Di(l, t, a) {
    if (t != null && (t = "" + Ll(t), t !== l.value && (l.value = t), a == null)) {
      l.defaultValue !== t && (l.defaultValue = t);
      return;
    }
    l.defaultValue = a != null ? "" + Ll(a) : "";
  }
  function Ui(l, t, a, u) {
    if (t == null) {
      if (u != null) {
        if (a != null) throw Error(g(92));
        if (st(u)) {
          if (1 < u.length) throw Error(g(93));
          u = u[0];
        }
        a = u;
      }
      a == null && (a = ""), t = a;
    }
    a = Ll(t), l.defaultValue = a, u = l.textContent, u === a && u !== "" && u !== null && (l.value = u), Wn(l);
  }
  function ja(l, t) {
    if (t) {
      var a = l.firstChild;
      if (a && a === l.lastChild && a.nodeType === 3) {
        a.nodeValue = t;
        return;
      }
    }
    l.textContent = t;
  }
  var Y1 = new Set(
    "animationIterationCount aspectRatio borderImageOutset borderImageSlice borderImageWidth boxFlex boxFlexGroup boxOrdinalGroup columnCount columns flex flexGrow flexPositive flexShrink flexNegative flexOrder gridArea gridRow gridRowEnd gridRowSpan gridRowStart gridColumn gridColumnEnd gridColumnSpan gridColumnStart fontWeight lineClamp lineHeight opacity order orphans scale tabSize widows zIndex zoom fillOpacity floodOpacity stopOpacity strokeDasharray strokeDashoffset strokeMiterlimit strokeOpacity strokeWidth MozAnimationIterationCount MozBoxFlex MozBoxFlexGroup MozLineClamp msAnimationIterationCount msFlex msZoom msFlexGrow msFlexNegative msFlexOrder msFlexPositive msFlexShrink msGridColumn msGridColumnSpan msGridRow msGridRowSpan WebkitAnimationIterationCount WebkitBoxFlex WebKitBoxFlexGroup WebkitBoxOrdinalGroup WebkitColumnCount WebkitColumns WebkitFlex WebkitFlexGrow WebkitFlexPositive WebkitFlexShrink WebkitLineClamp".split(
      " "
    )
  );
  function _i(l, t, a) {
    var u = t.indexOf("--") === 0;
    a == null || typeof a == "boolean" || a === "" ? u ? l.setProperty(t, "") : t === "float" ? l.cssFloat = "" : l[t] = "" : u ? l.setProperty(t, a) : typeof a != "number" || a === 0 || Y1.has(t) ? t === "float" ? l.cssFloat = a : l[t] = ("" + a).trim() : l[t] = a + "px";
  }
  function Hi(l, t, a) {
    if (t != null && typeof t != "object")
      throw Error(g(62));
    if (l = l.style, a != null) {
      for (var u in a)
        !a.hasOwnProperty(u) || t != null && t.hasOwnProperty(u) || (u.indexOf("--") === 0 ? l.setProperty(u, "") : u === "float" ? l.cssFloat = "" : l[u] = "");
      for (var e in t)
        u = t[e], t.hasOwnProperty(e) && a[e] !== u && _i(l, e, u);
    } else
      for (var n in t)
        t.hasOwnProperty(n) && _i(l, n, t[n]);
  }
  function Fn(l) {
    if (l.indexOf("-") === -1) return !1;
    switch (l) {
      case "annotation-xml":
      case "color-profile":
      case "font-face":
      case "font-face-src":
      case "font-face-uri":
      case "font-face-format":
      case "font-face-name":
      case "missing-glyph":
        return !1;
      default:
        return !0;
    }
  }
  var G1 = /* @__PURE__ */ new Map([
    ["acceptCharset", "accept-charset"],
    ["htmlFor", "for"],
    ["httpEquiv", "http-equiv"],
    ["crossOrigin", "crossorigin"],
    ["accentHeight", "accent-height"],
    ["alignmentBaseline", "alignment-baseline"],
    ["arabicForm", "arabic-form"],
    ["baselineShift", "baseline-shift"],
    ["capHeight", "cap-height"],
    ["clipPath", "clip-path"],
    ["clipRule", "clip-rule"],
    ["colorInterpolation", "color-interpolation"],
    ["colorInterpolationFilters", "color-interpolation-filters"],
    ["colorProfile", "color-profile"],
    ["colorRendering", "color-rendering"],
    ["dominantBaseline", "dominant-baseline"],
    ["enableBackground", "enable-background"],
    ["fillOpacity", "fill-opacity"],
    ["fillRule", "fill-rule"],
    ["floodColor", "flood-color"],
    ["floodOpacity", "flood-opacity"],
    ["fontFamily", "font-family"],
    ["fontSize", "font-size"],
    ["fontSizeAdjust", "font-size-adjust"],
    ["fontStretch", "font-stretch"],
    ["fontStyle", "font-style"],
    ["fontVariant", "font-variant"],
    ["fontWeight", "font-weight"],
    ["glyphName", "glyph-name"],
    ["glyphOrientationHorizontal", "glyph-orientation-horizontal"],
    ["glyphOrientationVertical", "glyph-orientation-vertical"],
    ["horizAdvX", "horiz-adv-x"],
    ["horizOriginX", "horiz-origin-x"],
    ["imageRendering", "image-rendering"],
    ["letterSpacing", "letter-spacing"],
    ["lightingColor", "lighting-color"],
    ["markerEnd", "marker-end"],
    ["markerMid", "marker-mid"],
    ["markerStart", "marker-start"],
    ["overlinePosition", "overline-position"],
    ["overlineThickness", "overline-thickness"],
    ["paintOrder", "paint-order"],
    ["panose-1", "panose-1"],
    ["pointerEvents", "pointer-events"],
    ["renderingIntent", "rendering-intent"],
    ["shapeRendering", "shape-rendering"],
    ["stopColor", "stop-color"],
    ["stopOpacity", "stop-opacity"],
    ["strikethroughPosition", "strikethrough-position"],
    ["strikethroughThickness", "strikethrough-thickness"],
    ["strokeDasharray", "stroke-dasharray"],
    ["strokeDashoffset", "stroke-dashoffset"],
    ["strokeLinecap", "stroke-linecap"],
    ["strokeLinejoin", "stroke-linejoin"],
    ["strokeMiterlimit", "stroke-miterlimit"],
    ["strokeOpacity", "stroke-opacity"],
    ["strokeWidth", "stroke-width"],
    ["textAnchor", "text-anchor"],
    ["textDecoration", "text-decoration"],
    ["textRendering", "text-rendering"],
    ["transformOrigin", "transform-origin"],
    ["underlinePosition", "underline-position"],
    ["underlineThickness", "underline-thickness"],
    ["unicodeBidi", "unicode-bidi"],
    ["unicodeRange", "unicode-range"],
    ["unitsPerEm", "units-per-em"],
    ["vAlphabetic", "v-alphabetic"],
    ["vHanging", "v-hanging"],
    ["vIdeographic", "v-ideographic"],
    ["vMathematical", "v-mathematical"],
    ["vectorEffect", "vector-effect"],
    ["vertAdvY", "vert-adv-y"],
    ["vertOriginX", "vert-origin-x"],
    ["vertOriginY", "vert-origin-y"],
    ["wordSpacing", "word-spacing"],
    ["writingMode", "writing-mode"],
    ["xmlnsXlink", "xmlns:xlink"],
    ["xHeight", "x-height"]
  ]), R1 = /^[\u0000-\u001F ]*j[\r\n\t]*a[\r\n\t]*v[\r\n\t]*a[\r\n\t]*s[\r\n\t]*c[\r\n\t]*r[\r\n\t]*i[\r\n\t]*p[\r\n\t]*t[\r\n\t]*:/i;
  function De(l) {
    return R1.test("" + l) ? "javascript:throw new Error('React has blocked a javascript: URL as a security precaution.')" : l;
  }
  function zt() {
  }
  var kn = null;
  function In(l) {
    return l = l.target || l.srcElement || window, l.correspondingUseElement && (l = l.correspondingUseElement), l.nodeType === 3 ? l.parentNode : l;
  }
  var Va = null, pa = null;
  function Ni(l) {
    var t = Xa(l);
    if (t && (l = t.stateNode)) {
      var a = l[ol] || null;
      l: switch (l = t.stateNode, t.type) {
        case "input":
          if (wn(
            l,
            a.value,
            a.defaultValue,
            a.defaultValue,
            a.checked,
            a.defaultChecked,
            a.type,
            a.name
          ), t = a.name, a.type === "radio" && t != null) {
            for (a = l; a.parentNode; ) a = a.parentNode;
            for (a = a.querySelectorAll(
              'input[name="' + xl(
                "" + t
              ) + '"][type="radio"]'
            ), t = 0; t < a.length; t++) {
              var u = a[t];
              if (u !== l && u.form === l.form) {
                var e = u[ol] || null;
                if (!e) throw Error(g(90));
                wn(
                  u,
                  e.value,
                  e.defaultValue,
                  e.defaultValue,
                  e.checked,
                  e.defaultChecked,
                  e.type,
                  e.name
                );
              }
            }
            for (t = 0; t < a.length; t++)
              u = a[t], u.form === l.form && Oi(u);
          }
          break l;
        case "textarea":
          Di(l, a.value, a.defaultValue);
          break l;
        case "select":
          t = a.value, t != null && Za(l, !!a.multiple, t, !1);
      }
    }
  }
  var Pn = !1;
  function qi(l, t, a) {
    if (Pn) return l(t, a);
    Pn = !0;
    try {
      var u = l(t);
      return u;
    } finally {
      if (Pn = !1, (Va !== null || pa !== null) && (hn(), Va && (t = Va, l = pa, pa = Va = null, Ni(t), l)))
        for (t = 0; t < l.length; t++) Ni(l[t]);
    }
  }
  function Mu(l, t) {
    var a = l.stateNode;
    if (a === null) return null;
    var u = a[ol] || null;
    if (u === null) return null;
    a = u[t];
    l: switch (t) {
      case "onClick":
      case "onClickCapture":
      case "onDoubleClick":
      case "onDoubleClickCapture":
      case "onMouseDown":
      case "onMouseDownCapture":
      case "onMouseMove":
      case "onMouseMoveCapture":
      case "onMouseUp":
      case "onMouseUpCapture":
      case "onMouseEnter":
        (u = !u.disabled) || (l = l.type, u = !(l === "button" || l === "input" || l === "select" || l === "textarea")), l = !u;
        break l;
      default:
        l = !1;
    }
    if (l) return null;
    if (a && typeof a != "function")
      throw Error(
        g(231, t, typeof a)
      );
    return a;
  }
  var Tt = !(typeof window > "u" || typeof window.document > "u" || typeof window.document.createElement > "u"), lf = !1;
  if (Tt)
    try {
      var Du = {};
      Object.defineProperty(Du, "passive", {
        get: function() {
          lf = !0;
        }
      }), window.addEventListener("test", Du, Du), window.removeEventListener("test", Du, Du);
    } catch {
      lf = !1;
    }
  var jt = null, tf = null, Ue = null;
  function Bi() {
    if (Ue) return Ue;
    var l, t = tf, a = t.length, u, e = "value" in jt ? jt.value : jt.textContent, n = e.length;
    for (l = 0; l < a && t[l] === e[l]; l++) ;
    var f = a - l;
    for (u = 1; u <= f && t[a - u] === e[n - u]; u++) ;
    return Ue = e.slice(l, 1 < u ? 1 - u : void 0);
  }
  function _e(l) {
    var t = l.keyCode;
    return "charCode" in l ? (l = l.charCode, l === 0 && t === 13 && (l = 13)) : l = t, l === 10 && (l = 13), 32 <= l || l === 13 ? l : 0;
  }
  function He() {
    return !0;
  }
  function Yi() {
    return !1;
  }
  function Ol(l) {
    function t(a, u, e, n, f) {
      this._reactName = a, this._targetInst = e, this.type = u, this.nativeEvent = n, this.target = f, this.currentTarget = null;
      for (var c in l)
        l.hasOwnProperty(c) && (a = l[c], this[c] = a ? a(n) : n[c]);
      return this.isDefaultPrevented = (n.defaultPrevented != null ? n.defaultPrevented : n.returnValue === !1) ? He : Yi, this.isPropagationStopped = Yi, this;
    }
    return H(t.prototype, {
      preventDefault: function() {
        this.defaultPrevented = !0;
        var a = this.nativeEvent;
        a && (a.preventDefault ? a.preventDefault() : typeof a.returnValue != "unknown" && (a.returnValue = !1), this.isDefaultPrevented = He);
      },
      stopPropagation: function() {
        var a = this.nativeEvent;
        a && (a.stopPropagation ? a.stopPropagation() : typeof a.cancelBubble != "unknown" && (a.cancelBubble = !0), this.isPropagationStopped = He);
      },
      persist: function() {
      },
      isPersistent: He
    }), t;
  }
  var da = {
    eventPhase: 0,
    bubbles: 0,
    cancelable: 0,
    timeStamp: function(l) {
      return l.timeStamp || Date.now();
    },
    defaultPrevented: 0,
    isTrusted: 0
  }, Ne = Ol(da), Uu = H({}, da, { view: 0, detail: 0 }), X1 = Ol(Uu), af, uf, _u, qe = H({}, Uu, {
    screenX: 0,
    screenY: 0,
    clientX: 0,
    clientY: 0,
    pageX: 0,
    pageY: 0,
    ctrlKey: 0,
    shiftKey: 0,
    altKey: 0,
    metaKey: 0,
    getModifierState: nf,
    button: 0,
    buttons: 0,
    relatedTarget: function(l) {
      return l.relatedTarget === void 0 ? l.fromElement === l.srcElement ? l.toElement : l.fromElement : l.relatedTarget;
    },
    movementX: function(l) {
      return "movementX" in l ? l.movementX : (l !== _u && (_u && l.type === "mousemove" ? (af = l.screenX - _u.screenX, uf = l.screenY - _u.screenY) : uf = af = 0, _u = l), af);
    },
    movementY: function(l) {
      return "movementY" in l ? l.movementY : uf;
    }
  }), Gi = Ol(qe), Q1 = H({}, qe, { dataTransfer: 0 }), C1 = Ol(Q1), Z1 = H({}, Uu, { relatedTarget: 0 }), ef = Ol(Z1), j1 = H({}, da, {
    animationName: 0,
    elapsedTime: 0,
    pseudoElement: 0
  }), V1 = Ol(j1), p1 = H({}, da, {
    clipboardData: function(l) {
      return "clipboardData" in l ? l.clipboardData : window.clipboardData;
    }
  }), K1 = Ol(p1), L1 = H({}, da, { data: 0 }), Ri = Ol(L1), x1 = {
    Esc: "Escape",
    Spacebar: " ",
    Left: "ArrowLeft",
    Up: "ArrowUp",
    Right: "ArrowRight",
    Down: "ArrowDown",
    Del: "Delete",
    Win: "OS",
    Menu: "ContextMenu",
    Apps: "ContextMenu",
    Scroll: "ScrollLock",
    MozPrintableKey: "Unidentified"
  }, J1 = {
    8: "Backspace",
    9: "Tab",
    12: "Clear",
    13: "Enter",
    16: "Shift",
    17: "Control",
    18: "Alt",
    19: "Pause",
    20: "CapsLock",
    27: "Escape",
    32: " ",
    33: "PageUp",
    34: "PageDown",
    35: "End",
    36: "Home",
    37: "ArrowLeft",
    38: "ArrowUp",
    39: "ArrowRight",
    40: "ArrowDown",
    45: "Insert",
    46: "Delete",
    112: "F1",
    113: "F2",
    114: "F3",
    115: "F4",
    116: "F5",
    117: "F6",
    118: "F7",
    119: "F8",
    120: "F9",
    121: "F10",
    122: "F11",
    123: "F12",
    144: "NumLock",
    145: "ScrollLock",
    224: "Meta"
  }, r1 = {
    Alt: "altKey",
    Control: "ctrlKey",
    Meta: "metaKey",
    Shift: "shiftKey"
  };
  function W1(l) {
    var t = this.nativeEvent;
    return t.getModifierState ? t.getModifierState(l) : (l = r1[l]) ? !!t[l] : !1;
  }
  function nf() {
    return W1;
  }
  var w1 = H({}, Uu, {
    key: function(l) {
      if (l.key) {
        var t = x1[l.key] || l.key;
        if (t !== "Unidentified") return t;
      }
      return l.type === "keypress" ? (l = _e(l), l === 13 ? "Enter" : String.fromCharCode(l)) : l.type === "keydown" || l.type === "keyup" ? J1[l.keyCode] || "Unidentified" : "";
    },
    code: 0,
    location: 0,
    ctrlKey: 0,
    shiftKey: 0,
    altKey: 0,
    metaKey: 0,
    repeat: 0,
    locale: 0,
    getModifierState: nf,
    charCode: function(l) {
      return l.type === "keypress" ? _e(l) : 0;
    },
    keyCode: function(l) {
      return l.type === "keydown" || l.type === "keyup" ? l.keyCode : 0;
    },
    which: function(l) {
      return l.type === "keypress" ? _e(l) : l.type === "keydown" || l.type === "keyup" ? l.keyCode : 0;
    }
  }), $1 = Ol(w1), F1 = H({}, qe, {
    pointerId: 0,
    width: 0,
    height: 0,
    pressure: 0,
    tangentialPressure: 0,
    tiltX: 0,
    tiltY: 0,
    twist: 0,
    pointerType: 0,
    isPrimary: 0
  }), Xi = Ol(F1), k1 = H({}, Uu, {
    touches: 0,
    targetTouches: 0,
    changedTouches: 0,
    altKey: 0,
    metaKey: 0,
    ctrlKey: 0,
    shiftKey: 0,
    getModifierState: nf
  }), I1 = Ol(k1), P1 = H({}, da, {
    propertyName: 0,
    elapsedTime: 0,
    pseudoElement: 0
  }), lm = Ol(P1), tm = H({}, qe, {
    deltaX: function(l) {
      return "deltaX" in l ? l.deltaX : "wheelDeltaX" in l ? -l.wheelDeltaX : 0;
    },
    deltaY: function(l) {
      return "deltaY" in l ? l.deltaY : "wheelDeltaY" in l ? -l.wheelDeltaY : "wheelDelta" in l ? -l.wheelDelta : 0;
    },
    deltaZ: 0,
    deltaMode: 0
  }), am = Ol(tm), um = H({}, da, {
    newState: 0,
    oldState: 0
  }), em = Ol(um), nm = [9, 13, 27, 32], ff = Tt && "CompositionEvent" in window, Hu = null;
  Tt && "documentMode" in document && (Hu = document.documentMode);
  var fm = Tt && "TextEvent" in window && !Hu, Qi = Tt && (!ff || Hu && 8 < Hu && 11 >= Hu), Ci = " ", Zi = !1;
  function ji(l, t) {
    switch (l) {
      case "keyup":
        return nm.indexOf(t.keyCode) !== -1;
      case "keydown":
        return t.keyCode !== 229;
      case "keypress":
      case "mousedown":
      case "focusout":
        return !0;
      default:
        return !1;
    }
  }
  function Vi(l) {
    return l = l.detail, typeof l == "object" && "data" in l ? l.data : null;
  }
  var Ka = !1;
  function cm(l, t) {
    switch (l) {
      case "compositionend":
        return Vi(t);
      case "keypress":
        return t.which !== 32 ? null : (Zi = !0, Ci);
      case "textInput":
        return l = t.data, l === Ci && Zi ? null : l;
      default:
        return null;
    }
  }
  function im(l, t) {
    if (Ka)
      return l === "compositionend" || !ff && ji(l, t) ? (l = Bi(), Ue = tf = jt = null, Ka = !1, l) : null;
    switch (l) {
      case "paste":
        return null;
      case "keypress":
        if (!(t.ctrlKey || t.altKey || t.metaKey) || t.ctrlKey && t.altKey) {
          if (t.char && 1 < t.char.length)
            return t.char;
          if (t.which) return String.fromCharCode(t.which);
        }
        return null;
      case "compositionend":
        return Qi && t.locale !== "ko" ? null : t.data;
      default:
        return null;
    }
  }
  var vm = {
    color: !0,
    date: !0,
    datetime: !0,
    "datetime-local": !0,
    email: !0,
    month: !0,
    number: !0,
    password: !0,
    range: !0,
    search: !0,
    tel: !0,
    text: !0,
    time: !0,
    url: !0,
    week: !0
  };
  function pi(l) {
    var t = l && l.nodeName && l.nodeName.toLowerCase();
    return t === "input" ? !!vm[l.type] : t === "textarea";
  }
  function Ki(l, t, a, u) {
    Va ? pa ? pa.push(u) : pa = [u] : Va = u, t = An(t, "onChange"), 0 < t.length && (a = new Ne(
      "onChange",
      "change",
      null,
      a,
      u
    ), l.push({ event: a, listeners: t }));
  }
  var Nu = null, qu = null;
  function ym(l) {
    My(l, 0);
  }
  function Be(l) {
    var t = Ou(l);
    if (Oi(t)) return l;
  }
  function Li(l, t) {
    if (l === "change") return t;
  }
  var xi = !1;
  if (Tt) {
    var cf;
    if (Tt) {
      var vf = "oninput" in document;
      if (!vf) {
        var Ji = document.createElement("div");
        Ji.setAttribute("oninput", "return;"), vf = typeof Ji.oninput == "function";
      }
      cf = vf;
    } else cf = !1;
    xi = cf && (!document.documentMode || 9 < document.documentMode);
  }
  function ri() {
    Nu && (Nu.detachEvent("onpropertychange", Wi), qu = Nu = null);
  }
  function Wi(l) {
    if (l.propertyName === "value" && Be(qu)) {
      var t = [];
      Ki(
        t,
        qu,
        l,
        In(l)
      ), qi(ym, t);
    }
  }
  function mm(l, t, a) {
    l === "focusin" ? (ri(), Nu = t, qu = a, Nu.attachEvent("onpropertychange", Wi)) : l === "focusout" && ri();
  }
  function dm(l) {
    if (l === "selectionchange" || l === "keyup" || l === "keydown")
      return Be(qu);
  }
  function hm(l, t) {
    if (l === "click") return Be(t);
  }
  function Sm(l, t) {
    if (l === "input" || l === "change")
      return Be(t);
  }
  function gm(l, t) {
    return l === t && (l !== 0 || 1 / l === 1 / t) || l !== l && t !== t;
  }
  var Gl = typeof Object.is == "function" ? Object.is : gm;
  function Bu(l, t) {
    if (Gl(l, t)) return !0;
    if (typeof l != "object" || l === null || typeof t != "object" || t === null)
      return !1;
    var a = Object.keys(l), u = Object.keys(t);
    if (a.length !== u.length) return !1;
    for (u = 0; u < a.length; u++) {
      var e = a[u];
      if (!jn.call(t, e) || !Gl(l[e], t[e]))
        return !1;
    }
    return !0;
  }
  function wi(l) {
    for (; l && l.firstChild; ) l = l.firstChild;
    return l;
  }
  function $i(l, t) {
    var a = wi(l);
    l = 0;
    for (var u; a; ) {
      if (a.nodeType === 3) {
        if (u = l + a.textContent.length, l <= t && u >= t)
          return { node: a, offset: t - l };
        l = u;
      }
      l: {
        for (; a; ) {
          if (a.nextSibling) {
            a = a.nextSibling;
            break l;
          }
          a = a.parentNode;
        }
        a = void 0;
      }
      a = wi(a);
    }
  }
  function Fi(l, t) {
    return l && t ? l === t ? !0 : l && l.nodeType === 3 ? !1 : t && t.nodeType === 3 ? Fi(l, t.parentNode) : "contains" in l ? l.contains(t) : l.compareDocumentPosition ? !!(l.compareDocumentPosition(t) & 16) : !1 : !1;
  }
  function ki(l) {
    l = l != null && l.ownerDocument != null && l.ownerDocument.defaultView != null ? l.ownerDocument.defaultView : window;
    for (var t = Me(l.document); t instanceof l.HTMLIFrameElement; ) {
      try {
        var a = typeof t.contentWindow.location.href == "string";
      } catch {
        a = !1;
      }
      if (a) l = t.contentWindow;
      else break;
      t = Me(l.document);
    }
    return t;
  }
  function yf(l) {
    var t = l && l.nodeName && l.nodeName.toLowerCase();
    return t && (t === "input" && (l.type === "text" || l.type === "search" || l.type === "tel" || l.type === "url" || l.type === "password") || t === "textarea" || l.contentEditable === "true");
  }
  var sm = Tt && "documentMode" in document && 11 >= document.documentMode, La = null, mf = null, Yu = null, df = !1;
  function Ii(l, t, a) {
    var u = a.window === a ? a.document : a.nodeType === 9 ? a : a.ownerDocument;
    df || La == null || La !== Me(u) || (u = La, "selectionStart" in u && yf(u) ? u = { start: u.selectionStart, end: u.selectionEnd } : (u = (u.ownerDocument && u.ownerDocument.defaultView || window).getSelection(), u = {
      anchorNode: u.anchorNode,
      anchorOffset: u.anchorOffset,
      focusNode: u.focusNode,
      focusOffset: u.focusOffset
    }), Yu && Bu(Yu, u) || (Yu = u, u = An(mf, "onSelect"), 0 < u.length && (t = new Ne(
      "onSelect",
      "select",
      null,
      t,
      a
    ), l.push({ event: t, listeners: u }), t.target = La)));
  }
  function ha(l, t) {
    var a = {};
    return a[l.toLowerCase()] = t.toLowerCase(), a["Webkit" + l] = "webkit" + t, a["Moz" + l] = "moz" + t, a;
  }
  var xa = {
    animationend: ha("Animation", "AnimationEnd"),
    animationiteration: ha("Animation", "AnimationIteration"),
    animationstart: ha("Animation", "AnimationStart"),
    transitionrun: ha("Transition", "TransitionRun"),
    transitionstart: ha("Transition", "TransitionStart"),
    transitioncancel: ha("Transition", "TransitionCancel"),
    transitionend: ha("Transition", "TransitionEnd")
  }, hf = {}, Pi = {};
  Tt && (Pi = document.createElement("div").style, "AnimationEvent" in window || (delete xa.animationend.animation, delete xa.animationiteration.animation, delete xa.animationstart.animation), "TransitionEvent" in window || delete xa.transitionend.transition);
  function Sa(l) {
    if (hf[l]) return hf[l];
    if (!xa[l]) return l;
    var t = xa[l], a;
    for (a in t)
      if (t.hasOwnProperty(a) && a in Pi)
        return hf[l] = t[a];
    return l;
  }
  var l0 = Sa("animationend"), t0 = Sa("animationiteration"), a0 = Sa("animationstart"), bm = Sa("transitionrun"), zm = Sa("transitionstart"), Tm = Sa("transitioncancel"), u0 = Sa("transitionend"), e0 = /* @__PURE__ */ new Map(), Sf = "abort auxClick beforeToggle cancel canPlay canPlayThrough click close contextMenu copy cut drag dragEnd dragEnter dragExit dragLeave dragOver dragStart drop durationChange emptied encrypted ended error gotPointerCapture input invalid keyDown keyPress keyUp load loadedData loadedMetadata loadStart lostPointerCapture mouseDown mouseMove mouseOut mouseOver mouseUp paste pause play playing pointerCancel pointerDown pointerMove pointerOut pointerOver pointerUp progress rateChange reset resize seeked seeking stalled submit suspend timeUpdate touchCancel touchEnd touchStart volumeChange scroll toggle touchMove waiting wheel".split(
    " "
  );
  Sf.push("scrollEnd");
  function ut(l, t) {
    e0.set(l, t), ma(t, [l]);
  }
  var Ye = typeof reportError == "function" ? reportError : function(l) {
    if (typeof window == "object" && typeof window.ErrorEvent == "function") {
      var t = new window.ErrorEvent("error", {
        bubbles: !0,
        cancelable: !0,
        message: typeof l == "object" && l !== null && typeof l.message == "string" ? String(l.message) : String(l),
        error: l
      });
      if (!window.dispatchEvent(t)) return;
    } else if (typeof process == "object" && typeof process.emit == "function") {
      process.emit("uncaughtException", l);
      return;
    }
    console.error(l);
  }, Jl = [], Ja = 0, gf = 0;
  function Ge() {
    for (var l = Ja, t = gf = Ja = 0; t < l; ) {
      var a = Jl[t];
      Jl[t++] = null;
      var u = Jl[t];
      Jl[t++] = null;
      var e = Jl[t];
      Jl[t++] = null;
      var n = Jl[t];
      if (Jl[t++] = null, u !== null && e !== null) {
        var f = u.pending;
        f === null ? e.next = e : (e.next = f.next, f.next = e), u.pending = e;
      }
      n !== 0 && n0(a, e, n);
    }
  }
  function Re(l, t, a, u) {
    Jl[Ja++] = l, Jl[Ja++] = t, Jl[Ja++] = a, Jl[Ja++] = u, gf |= u, l.lanes |= u, l = l.alternate, l !== null && (l.lanes |= u);
  }
  function sf(l, t, a, u) {
    return Re(l, t, a, u), Xe(l);
  }
  function ga(l, t) {
    return Re(l, null, null, t), Xe(l);
  }
  function n0(l, t, a) {
    l.lanes |= a;
    var u = l.alternate;
    u !== null && (u.lanes |= a);
    for (var e = !1, n = l.return; n !== null; )
      n.childLanes |= a, u = n.alternate, u !== null && (u.childLanes |= a), n.tag === 22 && (l = n.stateNode, l === null || l._visibility & 1 || (e = !0)), l = n, n = n.return;
    return l.tag === 3 ? (n = l.stateNode, e && t !== null && (e = 31 - Yl(a), l = n.hiddenUpdates, u = l[e], u === null ? l[e] = [t] : u.push(t), t.lane = a | 536870912), n) : null;
  }
  function Xe(l) {
    if (50 < le)
      throw le = 0, Dc = null, Error(g(185));
    for (var t = l.return; t !== null; )
      l = t, t = l.return;
    return l.tag === 3 ? l.stateNode : null;
  }
  var ra = {};
  function Am(l, t, a, u) {
    this.tag = l, this.key = a, this.sibling = this.child = this.return = this.stateNode = this.type = this.elementType = null, this.index = 0, this.refCleanup = this.ref = null, this.pendingProps = t, this.dependencies = this.memoizedState = this.updateQueue = this.memoizedProps = null, this.mode = u, this.subtreeFlags = this.flags = 0, this.deletions = null, this.childLanes = this.lanes = 0, this.alternate = null;
  }
  function Rl(l, t, a, u) {
    return new Am(l, t, a, u);
  }
  function bf(l) {
    return l = l.prototype, !(!l || !l.isReactComponent);
  }
  function At(l, t) {
    var a = l.alternate;
    return a === null ? (a = Rl(
      l.tag,
      t,
      l.key,
      l.mode
    ), a.elementType = l.elementType, a.type = l.type, a.stateNode = l.stateNode, a.alternate = l, l.alternate = a) : (a.pendingProps = t, a.type = l.type, a.flags = 0, a.subtreeFlags = 0, a.deletions = null), a.flags = l.flags & 65011712, a.childLanes = l.childLanes, a.lanes = l.lanes, a.child = l.child, a.memoizedProps = l.memoizedProps, a.memoizedState = l.memoizedState, a.updateQueue = l.updateQueue, t = l.dependencies, a.dependencies = t === null ? null : { lanes: t.lanes, firstContext: t.firstContext }, a.sibling = l.sibling, a.index = l.index, a.ref = l.ref, a.refCleanup = l.refCleanup, a;
  }
  function f0(l, t) {
    l.flags &= 65011714;
    var a = l.alternate;
    return a === null ? (l.childLanes = 0, l.lanes = t, l.child = null, l.subtreeFlags = 0, l.memoizedProps = null, l.memoizedState = null, l.updateQueue = null, l.dependencies = null, l.stateNode = null) : (l.childLanes = a.childLanes, l.lanes = a.lanes, l.child = a.child, l.subtreeFlags = 0, l.deletions = null, l.memoizedProps = a.memoizedProps, l.memoizedState = a.memoizedState, l.updateQueue = a.updateQueue, l.type = a.type, t = a.dependencies, l.dependencies = t === null ? null : {
      lanes: t.lanes,
      firstContext: t.firstContext
    }), l;
  }
  function Qe(l, t, a, u, e, n) {
    var f = 0;
    if (u = l, typeof l == "function") bf(l) && (f = 1);
    else if (typeof l == "string")
      f = Dd(
        l,
        a,
        ml.current
      ) ? 26 : l === "html" || l === "head" || l === "body" ? 27 : 5;
    else
      l: switch (l) {
        case qa:
          return l = Rl(31, a, t, e), l.elementType = qa, l.lanes = n, l;
        case Vl:
          return sa(a.children, e, n, t);
        case Ha:
          f = 8, e |= 24;
          break;
        case Na:
          return l = Rl(12, a, t, e | 2), l.elementType = Na, l.lanes = n, l;
        case ia:
          return l = Rl(13, a, t, e), l.elementType = ia, l.lanes = n, l;
        case ft:
          return l = Rl(19, a, t, e), l.elementType = ft, l.lanes = n, l;
        default:
          if (typeof l == "object" && l !== null)
            switch (l.$$typeof) {
              case pl:
                f = 10;
                break l;
              case bu:
                f = 9;
                break l;
              case Xt:
                f = 11;
                break l;
              case gt:
                f = 14;
                break l;
              case at:
                f = 16, u = null;
                break l;
            }
          f = 29, a = Error(
            g(130, l === null ? "null" : typeof l, "")
          ), u = null;
      }
    return t = Rl(f, a, t, e), t.elementType = l, t.type = u, t.lanes = n, t;
  }
  function sa(l, t, a, u) {
    return l = Rl(7, l, u, t), l.lanes = a, l;
  }
  function zf(l, t, a) {
    return l = Rl(6, l, null, t), l.lanes = a, l;
  }
  function c0(l) {
    var t = Rl(18, null, null, 0);
    return t.stateNode = l, t;
  }
  function Tf(l, t, a) {
    return t = Rl(
      4,
      l.children !== null ? l.children : [],
      l.key,
      t
    ), t.lanes = a, t.stateNode = {
      containerInfo: l.containerInfo,
      pendingChildren: null,
      implementation: l.implementation
    }, t;
  }
  var i0 = /* @__PURE__ */ new WeakMap();
  function rl(l, t) {
    if (typeof l == "object" && l !== null) {
      var a = i0.get(l);
      return a !== void 0 ? a : (t = {
        value: l,
        source: t,
        stack: ci(t)
      }, i0.set(l, t), t);
    }
    return {
      value: l,
      source: t,
      stack: ci(t)
    };
  }
  var Wa = [], wa = 0, Ce = null, Gu = 0, Wl = [], wl = 0, Vt = null, it = 1, vt = "";
  function Et(l, t) {
    Wa[wa++] = Gu, Wa[wa++] = Ce, Ce = l, Gu = t;
  }
  function v0(l, t, a) {
    Wl[wl++] = it, Wl[wl++] = vt, Wl[wl++] = Vt, Vt = l;
    var u = it;
    l = vt;
    var e = 32 - Yl(u) - 1;
    u &= ~(1 << e), a += 1;
    var n = 32 - Yl(t) + e;
    if (30 < n) {
      var f = e - e % 5;
      n = (u & (1 << f) - 1).toString(32), u >>= f, e -= f, it = 1 << 32 - Yl(t) + e | a << e | u, vt = n + l;
    } else
      it = 1 << n | a << e | u, vt = l;
  }
  function Af(l) {
    l.return !== null && (Et(l, 1), v0(l, 1, 0));
  }
  function Ef(l) {
    for (; l === Ce; )
      Ce = Wa[--wa], Wa[wa] = null, Gu = Wa[--wa], Wa[wa] = null;
    for (; l === Vt; )
      Vt = Wl[--wl], Wl[wl] = null, vt = Wl[--wl], Wl[wl] = null, it = Wl[--wl], Wl[wl] = null;
  }
  function y0(l, t) {
    Wl[wl++] = it, Wl[wl++] = vt, Wl[wl++] = Vt, it = t.id, vt = t.overflow, Vt = l;
  }
  var gl = null, F = null, Q = !1, pt = null, $l = !1, of = Error(g(519));
  function Kt(l) {
    var t = Error(
      g(
        418,
        1 < arguments.length && arguments[1] !== void 0 && arguments[1] ? "text" : "HTML",
        ""
      )
    );
    throw Ru(rl(t, l)), of;
  }
  function m0(l) {
    var t = l.stateNode, a = l.type, u = l.memoizedProps;
    switch (t[Sl] = l, t[ol] = u, a) {
      case "dialog":
        G("cancel", t), G("close", t);
        break;
      case "iframe":
      case "object":
      case "embed":
        G("load", t);
        break;
      case "video":
      case "audio":
        for (a = 0; a < ae.length; a++)
          G(ae[a], t);
        break;
      case "source":
        G("error", t);
        break;
      case "img":
      case "image":
      case "link":
        G("error", t), G("load", t);
        break;
      case "details":
        G("toggle", t);
        break;
      case "input":
        G("invalid", t), Mi(
          t,
          u.value,
          u.defaultValue,
          u.checked,
          u.defaultChecked,
          u.type,
          u.name,
          !0
        );
        break;
      case "select":
        G("invalid", t);
        break;
      case "textarea":
        G("invalid", t), Ui(t, u.value, u.defaultValue, u.children);
    }
    a = u.children, typeof a != "string" && typeof a != "number" && typeof a != "bigint" || t.textContent === "" + a || u.suppressHydrationWarning === !0 || Hy(t.textContent, a) ? (u.popover != null && (G("beforetoggle", t), G("toggle", t)), u.onScroll != null && G("scroll", t), u.onScrollEnd != null && G("scrollend", t), u.onClick != null && (t.onclick = zt), t = !0) : t = !1, t || Kt(l, !0);
  }
  function d0(l) {
    for (gl = l.return; gl; )
      switch (gl.tag) {
        case 5:
        case 31:
        case 13:
          $l = !1;
          return;
        case 27:
        case 3:
          $l = !0;
          return;
        default:
          gl = gl.return;
      }
  }
  function $a(l) {
    if (l !== gl) return !1;
    if (!Q) return d0(l), Q = !0, !1;
    var t = l.tag, a;
    if ((a = t !== 3 && t !== 27) && ((a = t === 5) && (a = l.type, a = !(a !== "form" && a !== "button") || Vc(l.type, l.memoizedProps)), a = !a), a && F && Kt(l), d0(l), t === 13) {
      if (l = l.memoizedState, l = l !== null ? l.dehydrated : null, !l) throw Error(g(317));
      F = Cy(l);
    } else if (t === 31) {
      if (l = l.memoizedState, l = l !== null ? l.dehydrated : null, !l) throw Error(g(317));
      F = Cy(l);
    } else
      t === 27 ? (t = F, aa(l.type) ? (l = Jc, Jc = null, F = l) : F = t) : F = gl ? kl(l.stateNode.nextSibling) : null;
    return !0;
  }
  function ba() {
    F = gl = null, Q = !1;
  }
  function Of() {
    var l = pt;
    return l !== null && (_l === null ? _l = l : _l.push.apply(
      _l,
      l
    ), pt = null), l;
  }
  function Ru(l) {
    pt === null ? pt = [l] : pt.push(l);
  }
  var Mf = Al(null), za = null, ot = null;
  function Lt(l, t, a) {
    V(Mf, t._currentValue), t._currentValue = a;
  }
  function Ot(l) {
    l._currentValue = Mf.current, P(Mf);
  }
  function Df(l, t, a) {
    for (; l !== null; ) {
      var u = l.alternate;
      if ((l.childLanes & t) !== t ? (l.childLanes |= t, u !== null && (u.childLanes |= t)) : u !== null && (u.childLanes & t) !== t && (u.childLanes |= t), l === a) break;
      l = l.return;
    }
  }
  function Uf(l, t, a, u) {
    var e = l.child;
    for (e !== null && (e.return = l); e !== null; ) {
      var n = e.dependencies;
      if (n !== null) {
        var f = e.child;
        n = n.firstContext;
        l: for (; n !== null; ) {
          var c = n;
          n = e;
          for (var i = 0; i < t.length; i++)
            if (c.context === t[i]) {
              n.lanes |= a, c = n.alternate, c !== null && (c.lanes |= a), Df(
                n.return,
                a,
                l
              ), u || (f = null);
              break l;
            }
          n = c.next;
        }
      } else if (e.tag === 18) {
        if (f = e.return, f === null) throw Error(g(341));
        f.lanes |= a, n = f.alternate, n !== null && (n.lanes |= a), Df(f, a, l), f = null;
      } else f = e.child;
      if (f !== null) f.return = e;
      else
        for (f = e; f !== null; ) {
          if (f === l) {
            f = null;
            break;
          }
          if (e = f.sibling, e !== null) {
            e.return = f.return, f = e;
            break;
          }
          f = f.return;
        }
      e = f;
    }
  }
  function Fa(l, t, a, u) {
    l = null;
    for (var e = t, n = !1; e !== null; ) {
      if (!n) {
        if ((e.flags & 524288) !== 0) n = !0;
        else if ((e.flags & 262144) !== 0) break;
      }
      if (e.tag === 10) {
        var f = e.alternate;
        if (f === null) throw Error(g(387));
        if (f = f.memoizedProps, f !== null) {
          var c = e.type;
          Gl(e.pendingProps.value, f.value) || (l !== null ? l.push(c) : l = [c]);
        }
      } else if (e === Se.current) {
        if (f = e.alternate, f === null) throw Error(g(387));
        f.memoizedState.memoizedState !== e.memoizedState.memoizedState && (l !== null ? l.push(ce) : l = [ce]);
      }
      e = e.return;
    }
    l !== null && Uf(
      t,
      l,
      a,
      u
    ), t.flags |= 262144;
  }
  function Ze(l) {
    for (l = l.firstContext; l !== null; ) {
      if (!Gl(
        l.context._currentValue,
        l.memoizedValue
      ))
        return !0;
      l = l.next;
    }
    return !1;
  }
  function Ta(l) {
    za = l, ot = null, l = l.dependencies, l !== null && (l.firstContext = null);
  }
  function sl(l) {
    return h0(za, l);
  }
  function je(l, t) {
    return za === null && Ta(l), h0(l, t);
  }
  function h0(l, t) {
    var a = t._currentValue;
    if (t = { context: t, memoizedValue: a, next: null }, ot === null) {
      if (l === null) throw Error(g(308));
      ot = t, l.dependencies = { lanes: 0, firstContext: t }, l.flags |= 524288;
    } else ot = ot.next = t;
    return a;
  }
  var Em = typeof AbortController < "u" ? AbortController : function() {
    var l = [], t = this.signal = {
      aborted: !1,
      addEventListener: function(a, u) {
        l.push(u);
      }
    };
    this.abort = function() {
      t.aborted = !0, l.forEach(function(a) {
        return a();
      });
    };
  }, om = D.unstable_scheduleCallback, Om = D.unstable_NormalPriority, fl = {
    $$typeof: pl,
    Consumer: null,
    Provider: null,
    _currentValue: null,
    _currentValue2: null,
    _threadCount: 0
  };
  function _f() {
    return {
      controller: new Em(),
      data: /* @__PURE__ */ new Map(),
      refCount: 0
    };
  }
  function Xu(l) {
    l.refCount--, l.refCount === 0 && om(Om, function() {
      l.controller.abort();
    });
  }
  var Qu = null, Hf = 0, ka = 0, Ia = null;
  function Mm(l, t) {
    if (Qu === null) {
      var a = Qu = [];
      Hf = 0, ka = Bc(), Ia = {
        status: "pending",
        value: void 0,
        then: function(u) {
          a.push(u);
        }
      };
    }
    return Hf++, t.then(S0, S0), t;
  }
  function S0() {
    if (--Hf === 0 && Qu !== null) {
      Ia !== null && (Ia.status = "fulfilled");
      var l = Qu;
      Qu = null, ka = 0, Ia = null;
      for (var t = 0; t < l.length; t++) (0, l[t])();
    }
  }
  function Dm(l, t) {
    var a = [], u = {
      status: "pending",
      value: null,
      reason: null,
      then: function(e) {
        a.push(e);
      }
    };
    return l.then(
      function() {
        u.status = "fulfilled", u.value = t;
        for (var e = 0; e < a.length; e++) (0, a[e])(t);
      },
      function(e) {
        for (u.status = "rejected", u.reason = e, e = 0; e < a.length; e++)
          (0, a[e])(void 0);
      }
    ), u;
  }
  var g0 = z.S;
  z.S = function(l, t) {
    Iv = ql(), typeof t == "object" && t !== null && typeof t.then == "function" && Mm(l, t), g0 !== null && g0(l, t);
  };
  var Aa = Al(null);
  function Nf() {
    var l = Aa.current;
    return l !== null ? l : W.pooledCache;
  }
  function Ve(l, t) {
    t === null ? V(Aa, Aa.current) : V(Aa, t.pool);
  }
  function s0() {
    var l = Nf();
    return l === null ? null : { parent: fl._currentValue, pool: l };
  }
  var Pa = Error(g(460)), qf = Error(g(474)), pe = Error(g(542)), Ke = { then: function() {
  } };
  function b0(l) {
    return l = l.status, l === "fulfilled" || l === "rejected";
  }
  function z0(l, t, a) {
    switch (a = l[a], a === void 0 ? l.push(t) : a !== t && (t.then(zt, zt), t = a), t.status) {
      case "fulfilled":
        return t.value;
      case "rejected":
        throw l = t.reason, A0(l), l;
      default:
        if (typeof t.status == "string") t.then(zt, zt);
        else {
          if (l = W, l !== null && 100 < l.shellSuspendCounter)
            throw Error(g(482));
          l = t, l.status = "pending", l.then(
            function(u) {
              if (t.status === "pending") {
                var e = t;
                e.status = "fulfilled", e.value = u;
              }
            },
            function(u) {
              if (t.status === "pending") {
                var e = t;
                e.status = "rejected", e.reason = u;
              }
            }
          );
        }
        switch (t.status) {
          case "fulfilled":
            return t.value;
          case "rejected":
            throw l = t.reason, A0(l), l;
        }
        throw oa = t, Pa;
    }
  }
  function Ea(l) {
    try {
      var t = l._init;
      return t(l._payload);
    } catch (a) {
      throw a !== null && typeof a == "object" && typeof a.then == "function" ? (oa = a, Pa) : a;
    }
  }
  var oa = null;
  function T0() {
    if (oa === null) throw Error(g(459));
    var l = oa;
    return oa = null, l;
  }
  function A0(l) {
    if (l === Pa || l === pe)
      throw Error(g(483));
  }
  var lu = null, Cu = 0;
  function Le(l) {
    var t = Cu;
    return Cu += 1, lu === null && (lu = []), z0(lu, l, t);
  }
  function Zu(l, t) {
    t = t.props.ref, l.ref = t !== void 0 ? t : null;
  }
  function xe(l, t) {
    throw t.$$typeof === ul ? Error(g(525)) : (l = Object.prototype.toString.call(t), Error(
      g(
        31,
        l === "[object Object]" ? "object with keys {" + Object.keys(t).join(", ") + "}" : l
      )
    ));
  }
  function E0(l) {
    function t(y, v) {
      if (l) {
        var m = y.deletions;
        m === null ? (y.deletions = [v], y.flags |= 16) : m.push(v);
      }
    }
    function a(y, v) {
      if (!l) return null;
      for (; v !== null; )
        t(y, v), v = v.sibling;
      return null;
    }
    function u(y) {
      for (var v = /* @__PURE__ */ new Map(); y !== null; )
        y.key !== null ? v.set(y.key, y) : v.set(y.index, y), y = y.sibling;
      return v;
    }
    function e(y, v) {
      return y = At(y, v), y.index = 0, y.sibling = null, y;
    }
    function n(y, v, m) {
      return y.index = m, l ? (m = y.alternate, m !== null ? (m = m.index, m < v ? (y.flags |= 67108866, v) : m) : (y.flags |= 67108866, v)) : (y.flags |= 1048576, v);
    }
    function f(y) {
      return l && y.alternate === null && (y.flags |= 67108866), y;
    }
    function c(y, v, m, b) {
      return v === null || v.tag !== 6 ? (v = zf(m, y.mode, b), v.return = y, v) : (v = e(v, m), v.return = y, v);
    }
    function i(y, v, m, b) {
      var M = m.type;
      return M === Vl ? s(
        y,
        v,
        m.props.children,
        b,
        m.key
      ) : v !== null && (v.elementType === M || typeof M == "object" && M !== null && M.$$typeof === at && Ea(M) === v.type) ? (v = e(v, m.props), Zu(v, m), v.return = y, v) : (v = Qe(
        m.type,
        m.key,
        m.props,
        null,
        y.mode,
        b
      ), Zu(v, m), v.return = y, v);
    }
    function d(y, v, m, b) {
      return v === null || v.tag !== 4 || v.stateNode.containerInfo !== m.containerInfo || v.stateNode.implementation !== m.implementation ? (v = Tf(m, y.mode, b), v.return = y, v) : (v = e(v, m.children || []), v.return = y, v);
    }
    function s(y, v, m, b, M) {
      return v === null || v.tag !== 7 ? (v = sa(
        m,
        y.mode,
        b,
        M
      ), v.return = y, v) : (v = e(v, m), v.return = y, v);
    }
    function T(y, v, m) {
      if (typeof v == "string" && v !== "" || typeof v == "number" || typeof v == "bigint")
        return v = zf(
          "" + v,
          y.mode,
          m
        ), v.return = y, v;
      if (typeof v == "object" && v !== null) {
        switch (v.$$typeof) {
          case lt:
            return m = Qe(
              v.type,
              v.key,
              v.props,
              null,
              y.mode,
              m
            ), Zu(m, v), m.return = y, m;
          case tt:
            return v = Tf(
              v,
              y.mode,
              m
            ), v.return = y, v;
          case at:
            return v = Ea(v), T(y, v, m);
        }
        if (st(v) || Kl(v))
          return v = sa(
            v,
            y.mode,
            m,
            null
          ), v.return = y, v;
        if (typeof v.then == "function")
          return T(y, Le(v), m);
        if (v.$$typeof === pl)
          return T(
            y,
            je(y, v),
            m
          );
        xe(y, v);
      }
      return null;
    }
    function h(y, v, m, b) {
      var M = v !== null ? v.key : null;
      if (typeof m == "string" && m !== "" || typeof m == "number" || typeof m == "bigint")
        return M !== null ? null : c(y, v, "" + m, b);
      if (typeof m == "object" && m !== null) {
        switch (m.$$typeof) {
          case lt:
            return m.key === M ? i(y, v, m, b) : null;
          case tt:
            return m.key === M ? d(y, v, m, b) : null;
          case at:
            return m = Ea(m), h(y, v, m, b);
        }
        if (st(m) || Kl(m))
          return M !== null ? null : s(y, v, m, b, null);
        if (typeof m.then == "function")
          return h(
            y,
            v,
            Le(m),
            b
          );
        if (m.$$typeof === pl)
          return h(
            y,
            v,
            je(y, m),
            b
          );
        xe(y, m);
      }
      return null;
    }
    function S(y, v, m, b, M) {
      if (typeof b == "string" && b !== "" || typeof b == "number" || typeof b == "bigint")
        return y = y.get(m) || null, c(v, y, "" + b, M);
      if (typeof b == "object" && b !== null) {
        switch (b.$$typeof) {
          case lt:
            return y = y.get(
              b.key === null ? m : b.key
            ) || null, i(v, y, b, M);
          case tt:
            return y = y.get(
              b.key === null ? m : b.key
            ) || null, d(v, y, b, M);
          case at:
            return b = Ea(b), S(
              y,
              v,
              m,
              b,
              M
            );
        }
        if (st(b) || Kl(b))
          return y = y.get(m) || null, s(v, y, b, M, null);
        if (typeof b.then == "function")
          return S(
            y,
            v,
            m,
            Le(b),
            M
          );
        if (b.$$typeof === pl)
          return S(
            y,
            v,
            m,
            je(v, b),
            M
          );
        xe(v, b);
      }
      return null;
    }
    function o(y, v, m, b) {
      for (var M = null, C = null, O = v, B = v = 0, X = null; O !== null && B < m.length; B++) {
        O.index > B ? (X = O, O = null) : X = O.sibling;
        var Z = h(
          y,
          O,
          m[B],
          b
        );
        if (Z === null) {
          O === null && (O = X);
          break;
        }
        l && O && Z.alternate === null && t(y, O), v = n(Z, v, B), C === null ? M = Z : C.sibling = Z, C = Z, O = X;
      }
      if (B === m.length)
        return a(y, O), Q && Et(y, B), M;
      if (O === null) {
        for (; B < m.length; B++)
          O = T(y, m[B], b), O !== null && (v = n(
            O,
            v,
            B
          ), C === null ? M = O : C.sibling = O, C = O);
        return Q && Et(y, B), M;
      }
      for (O = u(O); B < m.length; B++)
        X = S(
          O,
          y,
          B,
          m[B],
          b
        ), X !== null && (l && X.alternate !== null && O.delete(
          X.key === null ? B : X.key
        ), v = n(
          X,
          v,
          B
        ), C === null ? M = X : C.sibling = X, C = X);
      return l && O.forEach(function(ca) {
        return t(y, ca);
      }), Q && Et(y, B), M;
    }
    function _(y, v, m, b) {
      if (m == null) throw Error(g(151));
      for (var M = null, C = null, O = v, B = v = 0, X = null, Z = m.next(); O !== null && !Z.done; B++, Z = m.next()) {
        O.index > B ? (X = O, O = null) : X = O.sibling;
        var ca = h(y, O, Z.value, b);
        if (ca === null) {
          O === null && (O = X);
          break;
        }
        l && O && ca.alternate === null && t(y, O), v = n(ca, v, B), C === null ? M = ca : C.sibling = ca, C = ca, O = X;
      }
      if (Z.done)
        return a(y, O), Q && Et(y, B), M;
      if (O === null) {
        for (; !Z.done; B++, Z = m.next())
          Z = T(y, Z.value, b), Z !== null && (v = n(Z, v, B), C === null ? M = Z : C.sibling = Z, C = Z);
        return Q && Et(y, B), M;
      }
      for (O = u(O); !Z.done; B++, Z = m.next())
        Z = S(O, y, B, Z.value, b), Z !== null && (l && Z.alternate !== null && O.delete(Z.key === null ? B : Z.key), v = n(Z, v, B), C === null ? M = Z : C.sibling = Z, C = Z);
      return l && O.forEach(function(Qd) {
        return t(y, Qd);
      }), Q && Et(y, B), M;
    }
    function J(y, v, m, b) {
      if (typeof m == "object" && m !== null && m.type === Vl && m.key === null && (m = m.props.children), typeof m == "object" && m !== null) {
        switch (m.$$typeof) {
          case lt:
            l: {
              for (var M = m.key; v !== null; ) {
                if (v.key === M) {
                  if (M = m.type, M === Vl) {
                    if (v.tag === 7) {
                      a(
                        y,
                        v.sibling
                      ), b = e(
                        v,
                        m.props.children
                      ), b.return = y, y = b;
                      break l;
                    }
                  } else if (v.elementType === M || typeof M == "object" && M !== null && M.$$typeof === at && Ea(M) === v.type) {
                    a(
                      y,
                      v.sibling
                    ), b = e(v, m.props), Zu(b, m), b.return = y, y = b;
                    break l;
                  }
                  a(y, v);
                  break;
                } else t(y, v);
                v = v.sibling;
              }
              m.type === Vl ? (b = sa(
                m.props.children,
                y.mode,
                b,
                m.key
              ), b.return = y, y = b) : (b = Qe(
                m.type,
                m.key,
                m.props,
                null,
                y.mode,
                b
              ), Zu(b, m), b.return = y, y = b);
            }
            return f(y);
          case tt:
            l: {
              for (M = m.key; v !== null; ) {
                if (v.key === M)
                  if (v.tag === 4 && v.stateNode.containerInfo === m.containerInfo && v.stateNode.implementation === m.implementation) {
                    a(
                      y,
                      v.sibling
                    ), b = e(v, m.children || []), b.return = y, y = b;
                    break l;
                  } else {
                    a(y, v);
                    break;
                  }
                else t(y, v);
                v = v.sibling;
              }
              b = Tf(m, y.mode, b), b.return = y, y = b;
            }
            return f(y);
          case at:
            return m = Ea(m), J(
              y,
              v,
              m,
              b
            );
        }
        if (st(m))
          return o(
            y,
            v,
            m,
            b
          );
        if (Kl(m)) {
          if (M = Kl(m), typeof M != "function") throw Error(g(150));
          return m = M.call(m), _(
            y,
            v,
            m,
            b
          );
        }
        if (typeof m.then == "function")
          return J(
            y,
            v,
            Le(m),
            b
          );
        if (m.$$typeof === pl)
          return J(
            y,
            v,
            je(y, m),
            b
          );
        xe(y, m);
      }
      return typeof m == "string" && m !== "" || typeof m == "number" || typeof m == "bigint" ? (m = "" + m, v !== null && v.tag === 6 ? (a(y, v.sibling), b = e(v, m), b.return = y, y = b) : (a(y, v), b = zf(m, y.mode, b), b.return = y, y = b), f(y)) : a(y, v);
    }
    return function(y, v, m, b) {
      try {
        Cu = 0;
        var M = J(
          y,
          v,
          m,
          b
        );
        return lu = null, M;
      } catch (O) {
        if (O === Pa || O === pe) throw O;
        var C = Rl(29, O, null, y.mode);
        return C.lanes = b, C.return = y, C;
      }
    };
  }
  var Oa = E0(!0), o0 = E0(!1), xt = !1;
  function Bf(l) {
    l.updateQueue = {
      baseState: l.memoizedState,
      firstBaseUpdate: null,
      lastBaseUpdate: null,
      shared: { pending: null, lanes: 0, hiddenCallbacks: null },
      callbacks: null
    };
  }
  function Yf(l, t) {
    l = l.updateQueue, t.updateQueue === l && (t.updateQueue = {
      baseState: l.baseState,
      firstBaseUpdate: l.firstBaseUpdate,
      lastBaseUpdate: l.lastBaseUpdate,
      shared: l.shared,
      callbacks: null
    });
  }
  function Jt(l) {
    return { lane: l, tag: 0, payload: null, callback: null, next: null };
  }
  function rt(l, t, a) {
    var u = l.updateQueue;
    if (u === null) return null;
    if (u = u.shared, (j & 2) !== 0) {
      var e = u.pending;
      return e === null ? t.next = t : (t.next = e.next, e.next = t), u.pending = t, t = Xe(l), n0(l, null, a), t;
    }
    return Re(l, u, t, a), Xe(l);
  }
  function ju(l, t, a) {
    if (t = t.updateQueue, t !== null && (t = t.shared, (a & 4194048) !== 0)) {
      var u = t.lanes;
      u &= l.pendingLanes, a |= u, t.lanes = a, hi(l, a);
    }
  }
  function Gf(l, t) {
    var a = l.updateQueue, u = l.alternate;
    if (u !== null && (u = u.updateQueue, a === u)) {
      var e = null, n = null;
      if (a = a.firstBaseUpdate, a !== null) {
        do {
          var f = {
            lane: a.lane,
            tag: a.tag,
            payload: a.payload,
            callback: null,
            next: null
          };
          n === null ? e = n = f : n = n.next = f, a = a.next;
        } while (a !== null);
        n === null ? e = n = t : n = n.next = t;
      } else e = n = t;
      a = {
        baseState: u.baseState,
        firstBaseUpdate: e,
        lastBaseUpdate: n,
        shared: u.shared,
        callbacks: u.callbacks
      }, l.updateQueue = a;
      return;
    }
    l = a.lastBaseUpdate, l === null ? a.firstBaseUpdate = t : l.next = t, a.lastBaseUpdate = t;
  }
  var Rf = !1;
  function Vu() {
    if (Rf) {
      var l = Ia;
      if (l !== null) throw l;
    }
  }
  function pu(l, t, a, u) {
    Rf = !1;
    var e = l.updateQueue;
    xt = !1;
    var n = e.firstBaseUpdate, f = e.lastBaseUpdate, c = e.shared.pending;
    if (c !== null) {
      e.shared.pending = null;
      var i = c, d = i.next;
      i.next = null, f === null ? n = d : f.next = d, f = i;
      var s = l.alternate;
      s !== null && (s = s.updateQueue, c = s.lastBaseUpdate, c !== f && (c === null ? s.firstBaseUpdate = d : c.next = d, s.lastBaseUpdate = i));
    }
    if (n !== null) {
      var T = e.baseState;
      f = 0, s = d = i = null, c = n;
      do {
        var h = c.lane & -536870913, S = h !== c.lane;
        if (S ? (R & h) === h : (u & h) === h) {
          h !== 0 && h === ka && (Rf = !0), s !== null && (s = s.next = {
            lane: 0,
            tag: c.tag,
            payload: c.payload,
            callback: null,
            next: null
          });
          l: {
            var o = l, _ = c;
            h = t;
            var J = a;
            switch (_.tag) {
              case 1:
                if (o = _.payload, typeof o == "function") {
                  T = o.call(J, T, h);
                  break l;
                }
                T = o;
                break l;
              case 3:
                o.flags = o.flags & -65537 | 128;
              case 0:
                if (o = _.payload, h = typeof o == "function" ? o.call(J, T, h) : o, h == null) break l;
                T = H({}, T, h);
                break l;
              case 2:
                xt = !0;
            }
          }
          h = c.callback, h !== null && (l.flags |= 64, S && (l.flags |= 8192), S = e.callbacks, S === null ? e.callbacks = [h] : S.push(h));
        } else
          S = {
            lane: h,
            tag: c.tag,
            payload: c.payload,
            callback: c.callback,
            next: null
          }, s === null ? (d = s = S, i = T) : s = s.next = S, f |= h;
        if (c = c.next, c === null) {
          if (c = e.shared.pending, c === null)
            break;
          S = c, c = S.next, S.next = null, e.lastBaseUpdate = S, e.shared.pending = null;
        }
      } while (!0);
      s === null && (i = T), e.baseState = i, e.firstBaseUpdate = d, e.lastBaseUpdate = s, n === null && (e.shared.lanes = 0), kt |= f, l.lanes = f, l.memoizedState = T;
    }
  }
  function O0(l, t) {
    if (typeof l != "function")
      throw Error(g(191, l));
    l.call(t);
  }
  function M0(l, t) {
    var a = l.callbacks;
    if (a !== null)
      for (l.callbacks = null, l = 0; l < a.length; l++)
        O0(a[l], t);
  }
  var tu = Al(null), Je = Al(0);
  function D0(l, t) {
    l = Yt, V(Je, l), V(tu, t), Yt = l | t.baseLanes;
  }
  function Xf() {
    V(Je, Yt), V(tu, tu.current);
  }
  function Qf() {
    Yt = Je.current, P(tu), P(Je);
  }
  var Xl = Al(null), Fl = null;
  function Wt(l) {
    var t = l.alternate;
    V(el, el.current & 1), V(Xl, l), Fl === null && (t === null || tu.current !== null || t.memoizedState !== null) && (Fl = l);
  }
  function Cf(l) {
    V(el, el.current), V(Xl, l), Fl === null && (Fl = l);
  }
  function U0(l) {
    l.tag === 22 ? (V(el, el.current), V(Xl, l), Fl === null && (Fl = l)) : wt();
  }
  function wt() {
    V(el, el.current), V(Xl, Xl.current);
  }
  function Ql(l) {
    P(Xl), Fl === l && (Fl = null), P(el);
  }
  var el = Al(0);
  function re(l) {
    for (var t = l; t !== null; ) {
      if (t.tag === 13) {
        var a = t.memoizedState;
        if (a !== null && (a = a.dehydrated, a === null || Lc(a) || xc(a)))
          return t;
      } else if (t.tag === 19 && (t.memoizedProps.revealOrder === "forwards" || t.memoizedProps.revealOrder === "backwards" || t.memoizedProps.revealOrder === "unstable_legacy-backwards" || t.memoizedProps.revealOrder === "together")) {
        if ((t.flags & 128) !== 0) return t;
      } else if (t.child !== null) {
        t.child.return = t, t = t.child;
        continue;
      }
      if (t === l) break;
      for (; t.sibling === null; ) {
        if (t.return === null || t.return === l) return null;
        t = t.return;
      }
      t.sibling.return = t.return, t = t.sibling;
    }
    return null;
  }
  var Mt = 0, q = null, L = null, cl = null, We = !1, au = !1, Ma = !1, we = 0, Ku = 0, uu = null, Um = 0;
  function ll() {
    throw Error(g(321));
  }
  function Zf(l, t) {
    if (t === null) return !1;
    for (var a = 0; a < t.length && a < l.length; a++)
      if (!Gl(l[a], t[a])) return !1;
    return !0;
  }
  function jf(l, t, a, u, e, n) {
    return Mt = n, q = t, t.memoizedState = null, t.updateQueue = null, t.lanes = 0, z.H = l === null || l.memoizedState === null ? vv : lc, Ma = !1, n = a(u, e), Ma = !1, au && (n = H0(
      t,
      a,
      u,
      e
    )), _0(l), n;
  }
  function _0(l) {
    z.H = Ju;
    var t = L !== null && L.next !== null;
    if (Mt = 0, cl = L = q = null, We = !1, Ku = 0, uu = null, t) throw Error(g(300));
    l === null || il || (l = l.dependencies, l !== null && Ze(l) && (il = !0));
  }
  function H0(l, t, a, u) {
    q = l;
    var e = 0;
    do {
      if (au && (uu = null), Ku = 0, au = !1, 25 <= e) throw Error(g(301));
      if (e += 1, cl = L = null, l.updateQueue != null) {
        var n = l.updateQueue;
        n.lastEffect = null, n.events = null, n.stores = null, n.memoCache != null && (n.memoCache.index = 0);
      }
      z.H = yv, n = t(a, u);
    } while (au);
    return n;
  }
  function _m() {
    var l = z.H, t = l.useState()[0];
    return t = typeof t.then == "function" ? Lu(t) : t, l = l.useState()[0], (L !== null ? L.memoizedState : null) !== l && (q.flags |= 1024), t;
  }
  function Vf() {
    var l = we !== 0;
    return we = 0, l;
  }
  function pf(l, t, a) {
    t.updateQueue = l.updateQueue, t.flags &= -2053, l.lanes &= ~a;
  }
  function Kf(l) {
    if (We) {
      for (l = l.memoizedState; l !== null; ) {
        var t = l.queue;
        t !== null && (t.pending = null), l = l.next;
      }
      We = !1;
    }
    Mt = 0, cl = L = q = null, au = !1, Ku = we = 0, uu = null;
  }
  function El() {
    var l = {
      memoizedState: null,
      baseState: null,
      baseQueue: null,
      queue: null,
      next: null
    };
    return cl === null ? q.memoizedState = cl = l : cl = cl.next = l, cl;
  }
  function nl() {
    if (L === null) {
      var l = q.alternate;
      l = l !== null ? l.memoizedState : null;
    } else l = L.next;
    var t = cl === null ? q.memoizedState : cl.next;
    if (t !== null)
      cl = t, L = l;
    else {
      if (l === null)
        throw q.alternate === null ? Error(g(467)) : Error(g(310));
      L = l, l = {
        memoizedState: L.memoizedState,
        baseState: L.baseState,
        baseQueue: L.baseQueue,
        queue: L.queue,
        next: null
      }, cl === null ? q.memoizedState = cl = l : cl = cl.next = l;
    }
    return cl;
  }
  function $e() {
    return { lastEffect: null, events: null, stores: null, memoCache: null };
  }
  function Lu(l) {
    var t = Ku;
    return Ku += 1, uu === null && (uu = []), l = z0(uu, l, t), t = q, (cl === null ? t.memoizedState : cl.next) === null && (t = t.alternate, z.H = t === null || t.memoizedState === null ? vv : lc), l;
  }
  function Fe(l) {
    if (l !== null && typeof l == "object") {
      if (typeof l.then == "function") return Lu(l);
      if (l.$$typeof === pl) return sl(l);
    }
    throw Error(g(438, String(l)));
  }
  function Lf(l) {
    var t = null, a = q.updateQueue;
    if (a !== null && (t = a.memoCache), t == null) {
      var u = q.alternate;
      u !== null && (u = u.updateQueue, u !== null && (u = u.memoCache, u != null && (t = {
        data: u.data.map(function(e) {
          return e.slice();
        }),
        index: 0
      })));
    }
    if (t == null && (t = { data: [], index: 0 }), a === null && (a = $e(), q.updateQueue = a), a.memoCache = t, a = t.data[t.index], a === void 0)
      for (a = t.data[t.index] = Array(l), u = 0; u < l; u++)
        a[u] = de;
    return t.index++, a;
  }
  function Dt(l, t) {
    return typeof t == "function" ? t(l) : t;
  }
  function ke(l) {
    var t = nl();
    return xf(t, L, l);
  }
  function xf(l, t, a) {
    var u = l.queue;
    if (u === null) throw Error(g(311));
    u.lastRenderedReducer = a;
    var e = l.baseQueue, n = u.pending;
    if (n !== null) {
      if (e !== null) {
        var f = e.next;
        e.next = n.next, n.next = f;
      }
      t.baseQueue = e = n, u.pending = null;
    }
    if (n = l.baseState, e === null) l.memoizedState = n;
    else {
      t = e.next;
      var c = f = null, i = null, d = t, s = !1;
      do {
        var T = d.lane & -536870913;
        if (T !== d.lane ? (R & T) === T : (Mt & T) === T) {
          var h = d.revertLane;
          if (h === 0)
            i !== null && (i = i.next = {
              lane: 0,
              revertLane: 0,
              gesture: null,
              action: d.action,
              hasEagerState: d.hasEagerState,
              eagerState: d.eagerState,
              next: null
            }), T === ka && (s = !0);
          else if ((Mt & h) === h) {
            d = d.next, h === ka && (s = !0);
            continue;
          } else
            T = {
              lane: 0,
              revertLane: d.revertLane,
              gesture: null,
              action: d.action,
              hasEagerState: d.hasEagerState,
              eagerState: d.eagerState,
              next: null
            }, i === null ? (c = i = T, f = n) : i = i.next = T, q.lanes |= h, kt |= h;
          T = d.action, Ma && a(n, T), n = d.hasEagerState ? d.eagerState : a(n, T);
        } else
          h = {
            lane: T,
            revertLane: d.revertLane,
            gesture: d.gesture,
            action: d.action,
            hasEagerState: d.hasEagerState,
            eagerState: d.eagerState,
            next: null
          }, i === null ? (c = i = h, f = n) : i = i.next = h, q.lanes |= T, kt |= T;
        d = d.next;
      } while (d !== null && d !== t);
      if (i === null ? f = n : i.next = c, !Gl(n, l.memoizedState) && (il = !0, s && (a = Ia, a !== null)))
        throw a;
      l.memoizedState = n, l.baseState = f, l.baseQueue = i, u.lastRenderedState = n;
    }
    return e === null && (u.lanes = 0), [l.memoizedState, u.dispatch];
  }
  function Jf(l) {
    var t = nl(), a = t.queue;
    if (a === null) throw Error(g(311));
    a.lastRenderedReducer = l;
    var u = a.dispatch, e = a.pending, n = t.memoizedState;
    if (e !== null) {
      a.pending = null;
      var f = e = e.next;
      do
        n = l(n, f.action), f = f.next;
      while (f !== e);
      Gl(n, t.memoizedState) || (il = !0), t.memoizedState = n, t.baseQueue === null && (t.baseState = n), a.lastRenderedState = n;
    }
    return [n, u];
  }
  function N0(l, t, a) {
    var u = q, e = nl(), n = Q;
    if (n) {
      if (a === void 0) throw Error(g(407));
      a = a();
    } else a = t();
    var f = !Gl(
      (L || e).memoizedState,
      a
    );
    if (f && (e.memoizedState = a, il = !0), e = e.queue, wf(Y0.bind(null, u, e, l), [
      l
    ]), e.getSnapshot !== t || f || cl !== null && cl.memoizedState.tag & 1) {
      if (u.flags |= 2048, eu(
        9,
        { destroy: void 0 },
        B0.bind(
          null,
          u,
          e,
          a,
          t
        ),
        null
      ), W === null) throw Error(g(349));
      n || (Mt & 127) !== 0 || q0(u, t, a);
    }
    return a;
  }
  function q0(l, t, a) {
    l.flags |= 16384, l = { getSnapshot: t, value: a }, t = q.updateQueue, t === null ? (t = $e(), q.updateQueue = t, t.stores = [l]) : (a = t.stores, a === null ? t.stores = [l] : a.push(l));
  }
  function B0(l, t, a, u) {
    t.value = a, t.getSnapshot = u, G0(t) && R0(l);
  }
  function Y0(l, t, a) {
    return a(function() {
      G0(t) && R0(l);
    });
  }
  function G0(l) {
    var t = l.getSnapshot;
    l = l.value;
    try {
      var a = t();
      return !Gl(l, a);
    } catch {
      return !0;
    }
  }
  function R0(l) {
    var t = ga(l, 2);
    t !== null && Hl(t, l, 2);
  }
  function rf(l) {
    var t = El();
    if (typeof l == "function") {
      var a = l;
      if (l = a(), Ma) {
        Ct(!0);
        try {
          a();
        } finally {
          Ct(!1);
        }
      }
    }
    return t.memoizedState = t.baseState = l, t.queue = {
      pending: null,
      lanes: 0,
      dispatch: null,
      lastRenderedReducer: Dt,
      lastRenderedState: l
    }, t;
  }
  function X0(l, t, a, u) {
    return l.baseState = a, xf(
      l,
      L,
      typeof u == "function" ? u : Dt
    );
  }
  function Hm(l, t, a, u, e) {
    if (ln(l)) throw Error(g(485));
    if (l = t.action, l !== null) {
      var n = {
        payload: e,
        action: l,
        next: null,
        isTransition: !0,
        status: "pending",
        value: null,
        reason: null,
        listeners: [],
        then: function(f) {
          n.listeners.push(f);
        }
      };
      z.T !== null ? a(!0) : n.isTransition = !1, u(n), a = t.pending, a === null ? (n.next = t.pending = n, Q0(t, n)) : (n.next = a.next, t.pending = a.next = n);
    }
  }
  function Q0(l, t) {
    var a = t.action, u = t.payload, e = l.state;
    if (t.isTransition) {
      var n = z.T, f = {};
      z.T = f;
      try {
        var c = a(e, u), i = z.S;
        i !== null && i(f, c), C0(l, t, c);
      } catch (d) {
        Wf(l, t, d);
      } finally {
        n !== null && f.types !== null && (n.types = f.types), z.T = n;
      }
    } else
      try {
        n = a(e, u), C0(l, t, n);
      } catch (d) {
        Wf(l, t, d);
      }
  }
  function C0(l, t, a) {
    a !== null && typeof a == "object" && typeof a.then == "function" ? a.then(
      function(u) {
        Z0(l, t, u);
      },
      function(u) {
        return Wf(l, t, u);
      }
    ) : Z0(l, t, a);
  }
  function Z0(l, t, a) {
    t.status = "fulfilled", t.value = a, j0(t), l.state = a, t = l.pending, t !== null && (a = t.next, a === t ? l.pending = null : (a = a.next, t.next = a, Q0(l, a)));
  }
  function Wf(l, t, a) {
    var u = l.pending;
    if (l.pending = null, u !== null) {
      u = u.next;
      do
        t.status = "rejected", t.reason = a, j0(t), t = t.next;
      while (t !== u);
    }
    l.action = null;
  }
  function j0(l) {
    l = l.listeners;
    for (var t = 0; t < l.length; t++) (0, l[t])();
  }
  function V0(l, t) {
    return t;
  }
  function p0(l, t) {
    if (Q) {
      var a = W.formState;
      if (a !== null) {
        l: {
          var u = q;
          if (Q) {
            if (F) {
              t: {
                for (var e = F, n = $l; e.nodeType !== 8; ) {
                  if (!n) {
                    e = null;
                    break t;
                  }
                  if (e = kl(
                    e.nextSibling
                  ), e === null) {
                    e = null;
                    break t;
                  }
                }
                n = e.data, e = n === "F!" || n === "F" ? e : null;
              }
              if (e) {
                F = kl(
                  e.nextSibling
                ), u = e.data === "F!";
                break l;
              }
            }
            Kt(u);
          }
          u = !1;
        }
        u && (t = a[0]);
      }
    }
    return a = El(), a.memoizedState = a.baseState = t, u = {
      pending: null,
      lanes: 0,
      dispatch: null,
      lastRenderedReducer: V0,
      lastRenderedState: t
    }, a.queue = u, a = fv.bind(
      null,
      q,
      u
    ), u.dispatch = a, u = rf(!1), n = Pf.bind(
      null,
      q,
      !1,
      u.queue
    ), u = El(), e = {
      state: t,
      dispatch: null,
      action: l,
      pending: null
    }, u.queue = e, a = Hm.bind(
      null,
      q,
      e,
      n,
      a
    ), e.dispatch = a, u.memoizedState = l, [t, a, !1];
  }
  function K0(l) {
    var t = nl();
    return L0(t, L, l);
  }
  function L0(l, t, a) {
    if (t = xf(
      l,
      t,
      V0
    )[0], l = ke(Dt)[0], typeof t == "object" && t !== null && typeof t.then == "function")
      try {
        var u = Lu(t);
      } catch (f) {
        throw f === Pa ? pe : f;
      }
    else u = t;
    t = nl();
    var e = t.queue, n = e.dispatch;
    return a !== t.memoizedState && (q.flags |= 2048, eu(
      9,
      { destroy: void 0 },
      Nm.bind(null, e, a),
      null
    )), [u, n, l];
  }
  function Nm(l, t) {
    l.action = t;
  }
  function x0(l) {
    var t = nl(), a = L;
    if (a !== null)
      return L0(t, a, l);
    nl(), t = t.memoizedState, a = nl();
    var u = a.queue.dispatch;
    return a.memoizedState = l, [t, u, !1];
  }
  function eu(l, t, a, u) {
    return l = { tag: l, create: a, deps: u, inst: t, next: null }, t = q.updateQueue, t === null && (t = $e(), q.updateQueue = t), a = t.lastEffect, a === null ? t.lastEffect = l.next = l : (u = a.next, a.next = l, l.next = u, t.lastEffect = l), l;
  }
  function J0() {
    return nl().memoizedState;
  }
  function Ie(l, t, a, u) {
    var e = El();
    q.flags |= l, e.memoizedState = eu(
      1 | t,
      { destroy: void 0 },
      a,
      u === void 0 ? null : u
    );
  }
  function Pe(l, t, a, u) {
    var e = nl();
    u = u === void 0 ? null : u;
    var n = e.memoizedState.inst;
    L !== null && u !== null && Zf(u, L.memoizedState.deps) ? e.memoizedState = eu(t, n, a, u) : (q.flags |= l, e.memoizedState = eu(
      1 | t,
      n,
      a,
      u
    ));
  }
  function r0(l, t) {
    Ie(8390656, 8, l, t);
  }
  function wf(l, t) {
    Pe(2048, 8, l, t);
  }
  function qm(l) {
    q.flags |= 4;
    var t = q.updateQueue;
    if (t === null)
      t = $e(), q.updateQueue = t, t.events = [l];
    else {
      var a = t.events;
      a === null ? t.events = [l] : a.push(l);
    }
  }
  function W0(l) {
    var t = nl().memoizedState;
    return qm({ ref: t, nextImpl: l }), function() {
      if ((j & 2) !== 0) throw Error(g(440));
      return t.impl.apply(void 0, arguments);
    };
  }
  function w0(l, t) {
    return Pe(4, 2, l, t);
  }
  function $0(l, t) {
    return Pe(4, 4, l, t);
  }
  function F0(l, t) {
    if (typeof t == "function") {
      l = l();
      var a = t(l);
      return function() {
        typeof a == "function" ? a() : t(null);
      };
    }
    if (t != null)
      return l = l(), t.current = l, function() {
        t.current = null;
      };
  }
  function k0(l, t, a) {
    a = a != null ? a.concat([l]) : null, Pe(4, 4, F0.bind(null, t, l), a);
  }
  function $f() {
  }
  function I0(l, t) {
    var a = nl();
    t = t === void 0 ? null : t;
    var u = a.memoizedState;
    return t !== null && Zf(t, u[1]) ? u[0] : (a.memoizedState = [l, t], l);
  }
  function P0(l, t) {
    var a = nl();
    t = t === void 0 ? null : t;
    var u = a.memoizedState;
    if (t !== null && Zf(t, u[1]))
      return u[0];
    if (u = l(), Ma) {
      Ct(!0);
      try {
        l();
      } finally {
        Ct(!1);
      }
    }
    return a.memoizedState = [u, t], u;
  }
  function Ff(l, t, a) {
    return a === void 0 || (Mt & 1073741824) !== 0 && (R & 261930) === 0 ? l.memoizedState = t : (l.memoizedState = a, l = ly(), q.lanes |= l, kt |= l, a);
  }
  function lv(l, t, a, u) {
    return Gl(a, t) ? a : tu.current !== null ? (l = Ff(l, a, u), Gl(l, t) || (il = !0), l) : (Mt & 42) === 0 || (Mt & 1073741824) !== 0 && (R & 261930) === 0 ? (il = !0, l.memoizedState = a) : (l = ly(), q.lanes |= l, kt |= l, t);
  }
  function tv(l, t, a, u, e) {
    var n = E.p;
    E.p = n !== 0 && 8 > n ? n : 8;
    var f = z.T, c = {};
    z.T = c, Pf(l, !1, t, a);
    try {
      var i = e(), d = z.S;
      if (d !== null && d(c, i), i !== null && typeof i == "object" && typeof i.then == "function") {
        var s = Dm(
          i,
          u
        );
        xu(
          l,
          t,
          s,
          jl(l)
        );
      } else
        xu(
          l,
          t,
          u,
          jl(l)
        );
    } catch (T) {
      xu(
        l,
        t,
        { then: function() {
        }, status: "rejected", reason: T },
        jl()
      );
    } finally {
      E.p = n, f !== null && c.types !== null && (f.types = c.types), z.T = f;
    }
  }
  function Bm() {
  }
  function kf(l, t, a, u) {
    if (l.tag !== 5) throw Error(g(476));
    var e = av(l).queue;
    tv(
      l,
      e,
      t,
      N,
      a === null ? Bm : function() {
        return uv(l), a(u);
      }
    );
  }
  function av(l) {
    var t = l.memoizedState;
    if (t !== null) return t;
    t = {
      memoizedState: N,
      baseState: N,
      baseQueue: null,
      queue: {
        pending: null,
        lanes: 0,
        dispatch: null,
        lastRenderedReducer: Dt,
        lastRenderedState: N
      },
      next: null
    };
    var a = {};
    return t.next = {
      memoizedState: a,
      baseState: a,
      baseQueue: null,
      queue: {
        pending: null,
        lanes: 0,
        dispatch: null,
        lastRenderedReducer: Dt,
        lastRenderedState: a
      },
      next: null
    }, l.memoizedState = t, l = l.alternate, l !== null && (l.memoizedState = t), t;
  }
  function uv(l) {
    var t = av(l);
    t.next === null && (t = l.alternate.memoizedState), xu(
      l,
      t.next.queue,
      {},
      jl()
    );
  }
  function If() {
    return sl(ce);
  }
  function ev() {
    return nl().memoizedState;
  }
  function nv() {
    return nl().memoizedState;
  }
  function Ym(l) {
    for (var t = l.return; t !== null; ) {
      switch (t.tag) {
        case 24:
        case 3:
          var a = jl();
          l = Jt(a);
          var u = rt(t, l, a);
          u !== null && (Hl(u, t, a), ju(u, t, a)), t = { cache: _f() }, l.payload = t;
          return;
      }
      t = t.return;
    }
  }
  function Gm(l, t, a) {
    var u = jl();
    a = {
      lane: u,
      revertLane: 0,
      gesture: null,
      action: a,
      hasEagerState: !1,
      eagerState: null,
      next: null
    }, ln(l) ? cv(t, a) : (a = sf(l, t, a, u), a !== null && (Hl(a, l, u), iv(a, t, u)));
  }
  function fv(l, t, a) {
    var u = jl();
    xu(l, t, a, u);
  }
  function xu(l, t, a, u) {
    var e = {
      lane: u,
      revertLane: 0,
      gesture: null,
      action: a,
      hasEagerState: !1,
      eagerState: null,
      next: null
    };
    if (ln(l)) cv(t, e);
    else {
      var n = l.alternate;
      if (l.lanes === 0 && (n === null || n.lanes === 0) && (n = t.lastRenderedReducer, n !== null))
        try {
          var f = t.lastRenderedState, c = n(f, a);
          if (e.hasEagerState = !0, e.eagerState = c, Gl(c, f))
            return Re(l, t, e, 0), W === null && Ge(), !1;
        } catch {
        }
      if (a = sf(l, t, e, u), a !== null)
        return Hl(a, l, u), iv(a, t, u), !0;
    }
    return !1;
  }
  function Pf(l, t, a, u) {
    if (u = {
      lane: 2,
      revertLane: Bc(),
      gesture: null,
      action: u,
      hasEagerState: !1,
      eagerState: null,
      next: null
    }, ln(l)) {
      if (t) throw Error(g(479));
    } else
      t = sf(
        l,
        a,
        u,
        2
      ), t !== null && Hl(t, l, 2);
  }
  function ln(l) {
    var t = l.alternate;
    return l === q || t !== null && t === q;
  }
  function cv(l, t) {
    au = We = !0;
    var a = l.pending;
    a === null ? t.next = t : (t.next = a.next, a.next = t), l.pending = t;
  }
  function iv(l, t, a) {
    if ((a & 4194048) !== 0) {
      var u = t.lanes;
      u &= l.pendingLanes, a |= u, t.lanes = a, hi(l, a);
    }
  }
  var Ju = {
    readContext: sl,
    use: Fe,
    useCallback: ll,
    useContext: ll,
    useEffect: ll,
    useImperativeHandle: ll,
    useLayoutEffect: ll,
    useInsertionEffect: ll,
    useMemo: ll,
    useReducer: ll,
    useRef: ll,
    useState: ll,
    useDebugValue: ll,
    useDeferredValue: ll,
    useTransition: ll,
    useSyncExternalStore: ll,
    useId: ll,
    useHostTransitionStatus: ll,
    useFormState: ll,
    useActionState: ll,
    useOptimistic: ll,
    useMemoCache: ll,
    useCacheRefresh: ll
  };
  Ju.useEffectEvent = ll;
  var vv = {
    readContext: sl,
    use: Fe,
    useCallback: function(l, t) {
      return El().memoizedState = [
        l,
        t === void 0 ? null : t
      ], l;
    },
    useContext: sl,
    useEffect: r0,
    useImperativeHandle: function(l, t, a) {
      a = a != null ? a.concat([l]) : null, Ie(
        4194308,
        4,
        F0.bind(null, t, l),
        a
      );
    },
    useLayoutEffect: function(l, t) {
      return Ie(4194308, 4, l, t);
    },
    useInsertionEffect: function(l, t) {
      Ie(4, 2, l, t);
    },
    useMemo: function(l, t) {
      var a = El();
      t = t === void 0 ? null : t;
      var u = l();
      if (Ma) {
        Ct(!0);
        try {
          l();
        } finally {
          Ct(!1);
        }
      }
      return a.memoizedState = [u, t], u;
    },
    useReducer: function(l, t, a) {
      var u = El();
      if (a !== void 0) {
        var e = a(t);
        if (Ma) {
          Ct(!0);
          try {
            a(t);
          } finally {
            Ct(!1);
          }
        }
      } else e = t;
      return u.memoizedState = u.baseState = e, l = {
        pending: null,
        lanes: 0,
        dispatch: null,
        lastRenderedReducer: l,
        lastRenderedState: e
      }, u.queue = l, l = l.dispatch = Gm.bind(
        null,
        q,
        l
      ), [u.memoizedState, l];
    },
    useRef: function(l) {
      var t = El();
      return l = { current: l }, t.memoizedState = l;
    },
    useState: function(l) {
      l = rf(l);
      var t = l.queue, a = fv.bind(null, q, t);
      return t.dispatch = a, [l.memoizedState, a];
    },
    useDebugValue: $f,
    useDeferredValue: function(l, t) {
      var a = El();
      return Ff(a, l, t);
    },
    useTransition: function() {
      var l = rf(!1);
      return l = tv.bind(
        null,
        q,
        l.queue,
        !0,
        !1
      ), El().memoizedState = l, [!1, l];
    },
    useSyncExternalStore: function(l, t, a) {
      var u = q, e = El();
      if (Q) {
        if (a === void 0)
          throw Error(g(407));
        a = a();
      } else {
        if (a = t(), W === null)
          throw Error(g(349));
        (R & 127) !== 0 || q0(u, t, a);
      }
      e.memoizedState = a;
      var n = { value: a, getSnapshot: t };
      return e.queue = n, r0(Y0.bind(null, u, n, l), [
        l
      ]), u.flags |= 2048, eu(
        9,
        { destroy: void 0 },
        B0.bind(
          null,
          u,
          n,
          a,
          t
        ),
        null
      ), a;
    },
    useId: function() {
      var l = El(), t = W.identifierPrefix;
      if (Q) {
        var a = vt, u = it;
        a = (u & ~(1 << 32 - Yl(u) - 1)).toString(32) + a, t = "_" + t + "R_" + a, a = we++, 0 < a && (t += "H" + a.toString(32)), t += "_";
      } else
        a = Um++, t = "_" + t + "r_" + a.toString(32) + "_";
      return l.memoizedState = t;
    },
    useHostTransitionStatus: If,
    useFormState: p0,
    useActionState: p0,
    useOptimistic: function(l) {
      var t = El();
      t.memoizedState = t.baseState = l;
      var a = {
        pending: null,
        lanes: 0,
        dispatch: null,
        lastRenderedReducer: null,
        lastRenderedState: null
      };
      return t.queue = a, t = Pf.bind(
        null,
        q,
        !0,
        a
      ), a.dispatch = t, [l, t];
    },
    useMemoCache: Lf,
    useCacheRefresh: function() {
      return El().memoizedState = Ym.bind(
        null,
        q
      );
    },
    useEffectEvent: function(l) {
      var t = El(), a = { impl: l };
      return t.memoizedState = a, function() {
        if ((j & 2) !== 0)
          throw Error(g(440));
        return a.impl.apply(void 0, arguments);
      };
    }
  }, lc = {
    readContext: sl,
    use: Fe,
    useCallback: I0,
    useContext: sl,
    useEffect: wf,
    useImperativeHandle: k0,
    useInsertionEffect: w0,
    useLayoutEffect: $0,
    useMemo: P0,
    useReducer: ke,
    useRef: J0,
    useState: function() {
      return ke(Dt);
    },
    useDebugValue: $f,
    useDeferredValue: function(l, t) {
      var a = nl();
      return lv(
        a,
        L.memoizedState,
        l,
        t
      );
    },
    useTransition: function() {
      var l = ke(Dt)[0], t = nl().memoizedState;
      return [
        typeof l == "boolean" ? l : Lu(l),
        t
      ];
    },
    useSyncExternalStore: N0,
    useId: ev,
    useHostTransitionStatus: If,
    useFormState: K0,
    useActionState: K0,
    useOptimistic: function(l, t) {
      var a = nl();
      return X0(a, L, l, t);
    },
    useMemoCache: Lf,
    useCacheRefresh: nv
  };
  lc.useEffectEvent = W0;
  var yv = {
    readContext: sl,
    use: Fe,
    useCallback: I0,
    useContext: sl,
    useEffect: wf,
    useImperativeHandle: k0,
    useInsertionEffect: w0,
    useLayoutEffect: $0,
    useMemo: P0,
    useReducer: Jf,
    useRef: J0,
    useState: function() {
      return Jf(Dt);
    },
    useDebugValue: $f,
    useDeferredValue: function(l, t) {
      var a = nl();
      return L === null ? Ff(a, l, t) : lv(
        a,
        L.memoizedState,
        l,
        t
      );
    },
    useTransition: function() {
      var l = Jf(Dt)[0], t = nl().memoizedState;
      return [
        typeof l == "boolean" ? l : Lu(l),
        t
      ];
    },
    useSyncExternalStore: N0,
    useId: ev,
    useHostTransitionStatus: If,
    useFormState: x0,
    useActionState: x0,
    useOptimistic: function(l, t) {
      var a = nl();
      return L !== null ? X0(a, L, l, t) : (a.baseState = l, [l, a.queue.dispatch]);
    },
    useMemoCache: Lf,
    useCacheRefresh: nv
  };
  yv.useEffectEvent = W0;
  function tc(l, t, a, u) {
    t = l.memoizedState, a = a(u, t), a = a == null ? t : H({}, t, a), l.memoizedState = a, l.lanes === 0 && (l.updateQueue.baseState = a);
  }
  var ac = {
    enqueueSetState: function(l, t, a) {
      l = l._reactInternals;
      var u = jl(), e = Jt(u);
      e.payload = t, a != null && (e.callback = a), t = rt(l, e, u), t !== null && (Hl(t, l, u), ju(t, l, u));
    },
    enqueueReplaceState: function(l, t, a) {
      l = l._reactInternals;
      var u = jl(), e = Jt(u);
      e.tag = 1, e.payload = t, a != null && (e.callback = a), t = rt(l, e, u), t !== null && (Hl(t, l, u), ju(t, l, u));
    },
    enqueueForceUpdate: function(l, t) {
      l = l._reactInternals;
      var a = jl(), u = Jt(a);
      u.tag = 2, t != null && (u.callback = t), t = rt(l, u, a), t !== null && (Hl(t, l, a), ju(t, l, a));
    }
  };
  function mv(l, t, a, u, e, n, f) {
    return l = l.stateNode, typeof l.shouldComponentUpdate == "function" ? l.shouldComponentUpdate(u, n, f) : t.prototype && t.prototype.isPureReactComponent ? !Bu(a, u) || !Bu(e, n) : !0;
  }
  function dv(l, t, a, u) {
    l = t.state, typeof t.componentWillReceiveProps == "function" && t.componentWillReceiveProps(a, u), typeof t.UNSAFE_componentWillReceiveProps == "function" && t.UNSAFE_componentWillReceiveProps(a, u), t.state !== l && ac.enqueueReplaceState(t, t.state, null);
  }
  function Da(l, t) {
    var a = t;
    if ("ref" in t) {
      a = {};
      for (var u in t)
        u !== "ref" && (a[u] = t[u]);
    }
    if (l = l.defaultProps) {
      a === t && (a = H({}, a));
      for (var e in l)
        a[e] === void 0 && (a[e] = l[e]);
    }
    return a;
  }
  function hv(l) {
    Ye(l);
  }
  function Sv(l) {
    console.error(l);
  }
  function gv(l) {
    Ye(l);
  }
  function tn(l, t) {
    try {
      var a = l.onUncaughtError;
      a(t.value, { componentStack: t.stack });
    } catch (u) {
      setTimeout(function() {
        throw u;
      });
    }
  }
  function sv(l, t, a) {
    try {
      var u = l.onCaughtError;
      u(a.value, {
        componentStack: a.stack,
        errorBoundary: t.tag === 1 ? t.stateNode : null
      });
    } catch (e) {
      setTimeout(function() {
        throw e;
      });
    }
  }
  function uc(l, t, a) {
    return a = Jt(a), a.tag = 3, a.payload = { element: null }, a.callback = function() {
      tn(l, t);
    }, a;
  }
  function bv(l) {
    return l = Jt(l), l.tag = 3, l;
  }
  function zv(l, t, a, u) {
    var e = a.type.getDerivedStateFromError;
    if (typeof e == "function") {
      var n = u.value;
      l.payload = function() {
        return e(n);
      }, l.callback = function() {
        sv(t, a, u);
      };
    }
    var f = a.stateNode;
    f !== null && typeof f.componentDidCatch == "function" && (l.callback = function() {
      sv(t, a, u), typeof e != "function" && (It === null ? It = /* @__PURE__ */ new Set([this]) : It.add(this));
      var c = u.stack;
      this.componentDidCatch(u.value, {
        componentStack: c !== null ? c : ""
      });
    });
  }
  function Rm(l, t, a, u, e) {
    if (a.flags |= 32768, u !== null && typeof u == "object" && typeof u.then == "function") {
      if (t = a.alternate, t !== null && Fa(
        t,
        a,
        e,
        !0
      ), a = Xl.current, a !== null) {
        switch (a.tag) {
          case 31:
          case 13:
            return Fl === null ? Sn() : a.alternate === null && tl === 0 && (tl = 3), a.flags &= -257, a.flags |= 65536, a.lanes = e, u === Ke ? a.flags |= 16384 : (t = a.updateQueue, t === null ? a.updateQueue = /* @__PURE__ */ new Set([u]) : t.add(u), Hc(l, u, e)), !1;
          case 22:
            return a.flags |= 65536, u === Ke ? a.flags |= 16384 : (t = a.updateQueue, t === null ? (t = {
              transitions: null,
              markerInstances: null,
              retryQueue: /* @__PURE__ */ new Set([u])
            }, a.updateQueue = t) : (a = t.retryQueue, a === null ? t.retryQueue = /* @__PURE__ */ new Set([u]) : a.add(u)), Hc(l, u, e)), !1;
        }
        throw Error(g(435, a.tag));
      }
      return Hc(l, u, e), Sn(), !1;
    }
    if (Q)
      return t = Xl.current, t !== null ? ((t.flags & 65536) === 0 && (t.flags |= 256), t.flags |= 65536, t.lanes = e, u !== of && (l = Error(g(422), { cause: u }), Ru(rl(l, a)))) : (u !== of && (t = Error(g(423), {
        cause: u
      }), Ru(
        rl(t, a)
      )), l = l.current.alternate, l.flags |= 65536, e &= -e, l.lanes |= e, u = rl(u, a), e = uc(
        l.stateNode,
        u,
        e
      ), Gf(l, e), tl !== 4 && (tl = 2)), !1;
    var n = Error(g(520), { cause: u });
    if (n = rl(n, a), Pu === null ? Pu = [n] : Pu.push(n), tl !== 4 && (tl = 2), t === null) return !0;
    u = rl(u, a), a = t;
    do {
      switch (a.tag) {
        case 3:
          return a.flags |= 65536, l = e & -e, a.lanes |= l, l = uc(a.stateNode, u, l), Gf(a, l), !1;
        case 1:
          if (t = a.type, n = a.stateNode, (a.flags & 128) === 0 && (typeof t.getDerivedStateFromError == "function" || n !== null && typeof n.componentDidCatch == "function" && (It === null || !It.has(n))))
            return a.flags |= 65536, e &= -e, a.lanes |= e, e = bv(e), zv(
              e,
              l,
              a,
              u
            ), Gf(a, e), !1;
      }
      a = a.return;
    } while (a !== null);
    return !1;
  }
  var ec = Error(g(461)), il = !1;
  function bl(l, t, a, u) {
    t.child = l === null ? o0(t, null, a, u) : Oa(
      t,
      l.child,
      a,
      u
    );
  }
  function Tv(l, t, a, u, e) {
    a = a.render;
    var n = t.ref;
    if ("ref" in u) {
      var f = {};
      for (var c in u)
        c !== "ref" && (f[c] = u[c]);
    } else f = u;
    return Ta(t), u = jf(
      l,
      t,
      a,
      f,
      n,
      e
    ), c = Vf(), l !== null && !il ? (pf(l, t, e), Ut(l, t, e)) : (Q && c && Af(t), t.flags |= 1, bl(l, t, u, e), t.child);
  }
  function Av(l, t, a, u, e) {
    if (l === null) {
      var n = a.type;
      return typeof n == "function" && !bf(n) && n.defaultProps === void 0 && a.compare === null ? (t.tag = 15, t.type = n, Ev(
        l,
        t,
        n,
        u,
        e
      )) : (l = Qe(
        a.type,
        null,
        u,
        t,
        t.mode,
        e
      ), l.ref = t.ref, l.return = t, t.child = l);
    }
    if (n = l.child, !dc(l, e)) {
      var f = n.memoizedProps;
      if (a = a.compare, a = a !== null ? a : Bu, a(f, u) && l.ref === t.ref)
        return Ut(l, t, e);
    }
    return t.flags |= 1, l = At(n, u), l.ref = t.ref, l.return = t, t.child = l;
  }
  function Ev(l, t, a, u, e) {
    if (l !== null) {
      var n = l.memoizedProps;
      if (Bu(n, u) && l.ref === t.ref)
        if (il = !1, t.pendingProps = u = n, dc(l, e))
          (l.flags & 131072) !== 0 && (il = !0);
        else
          return t.lanes = l.lanes, Ut(l, t, e);
    }
    return nc(
      l,
      t,
      a,
      u,
      e
    );
  }
  function ov(l, t, a, u) {
    var e = u.children, n = l !== null ? l.memoizedState : null;
    if (l === null && t.stateNode === null && (t.stateNode = {
      _visibility: 1,
      _pendingMarkers: null,
      _retryCache: null,
      _transitions: null
    }), u.mode === "hidden") {
      if ((t.flags & 128) !== 0) {
        if (n = n !== null ? n.baseLanes | a : a, l !== null) {
          for (u = t.child = l.child, e = 0; u !== null; )
            e = e | u.lanes | u.childLanes, u = u.sibling;
          u = e & ~n;
        } else u = 0, t.child = null;
        return Ov(
          l,
          t,
          n,
          a,
          u
        );
      }
      if ((a & 536870912) !== 0)
        t.memoizedState = { baseLanes: 0, cachePool: null }, l !== null && Ve(
          t,
          n !== null ? n.cachePool : null
        ), n !== null ? D0(t, n) : Xf(), U0(t);
      else
        return u = t.lanes = 536870912, Ov(
          l,
          t,
          n !== null ? n.baseLanes | a : a,
          a,
          u
        );
    } else
      n !== null ? (Ve(t, n.cachePool), D0(t, n), wt(), t.memoizedState = null) : (l !== null && Ve(t, null), Xf(), wt());
    return bl(l, t, e, a), t.child;
  }
  function ru(l, t) {
    return l !== null && l.tag === 22 || t.stateNode !== null || (t.stateNode = {
      _visibility: 1,
      _pendingMarkers: null,
      _retryCache: null,
      _transitions: null
    }), t.sibling;
  }
  function Ov(l, t, a, u, e) {
    var n = Nf();
    return n = n === null ? null : { parent: fl._currentValue, pool: n }, t.memoizedState = {
      baseLanes: a,
      cachePool: n
    }, l !== null && Ve(t, null), Xf(), U0(t), l !== null && Fa(l, t, u, !0), t.childLanes = e, null;
  }
  function an(l, t) {
    return t = en(
      { mode: t.mode, children: t.children },
      l.mode
    ), t.ref = l.ref, l.child = t, t.return = l, t;
  }
  function Mv(l, t, a) {
    return Oa(t, l.child, null, a), l = an(t, t.pendingProps), l.flags |= 2, Ql(t), t.memoizedState = null, l;
  }
  function Xm(l, t, a) {
    var u = t.pendingProps, e = (t.flags & 128) !== 0;
    if (t.flags &= -129, l === null) {
      if (Q) {
        if (u.mode === "hidden")
          return l = an(t, u), t.lanes = 536870912, ru(null, l);
        if (Cf(t), (l = F) ? (l = Qy(
          l,
          $l
        ), l = l !== null && l.data === "&" ? l : null, l !== null && (t.memoizedState = {
          dehydrated: l,
          treeContext: Vt !== null ? { id: it, overflow: vt } : null,
          retryLane: 536870912,
          hydrationErrors: null
        }, a = c0(l), a.return = t, t.child = a, gl = t, F = null)) : l = null, l === null) throw Kt(t);
        return t.lanes = 536870912, null;
      }
      return an(t, u);
    }
    var n = l.memoizedState;
    if (n !== null) {
      var f = n.dehydrated;
      if (Cf(t), e)
        if (t.flags & 256)
          t.flags &= -257, t = Mv(
            l,
            t,
            a
          );
        else if (t.memoizedState !== null)
          t.child = l.child, t.flags |= 128, t = null;
        else throw Error(g(558));
      else if (il || Fa(l, t, a, !1), e = (a & l.childLanes) !== 0, il || e) {
        if (u = W, u !== null && (f = Si(u, a), f !== 0 && f !== n.retryLane))
          throw n.retryLane = f, ga(l, f), Hl(u, l, f), ec;
        Sn(), t = Mv(
          l,
          t,
          a
        );
      } else
        l = n.treeContext, F = kl(f.nextSibling), gl = t, Q = !0, pt = null, $l = !1, l !== null && y0(t, l), t = an(t, u), t.flags |= 4096;
      return t;
    }
    return l = At(l.child, {
      mode: u.mode,
      children: u.children
    }), l.ref = t.ref, t.child = l, l.return = t, l;
  }
  function un(l, t) {
    var a = t.ref;
    if (a === null)
      l !== null && l.ref !== null && (t.flags |= 4194816);
    else {
      if (typeof a != "function" && typeof a != "object")
        throw Error(g(284));
      (l === null || l.ref !== a) && (t.flags |= 4194816);
    }
  }
  function nc(l, t, a, u, e) {
    return Ta(t), a = jf(
      l,
      t,
      a,
      u,
      void 0,
      e
    ), u = Vf(), l !== null && !il ? (pf(l, t, e), Ut(l, t, e)) : (Q && u && Af(t), t.flags |= 1, bl(l, t, a, e), t.child);
  }
  function Dv(l, t, a, u, e, n) {
    return Ta(t), t.updateQueue = null, a = H0(
      t,
      u,
      a,
      e
    ), _0(l), u = Vf(), l !== null && !il ? (pf(l, t, n), Ut(l, t, n)) : (Q && u && Af(t), t.flags |= 1, bl(l, t, a, n), t.child);
  }
  function Uv(l, t, a, u, e) {
    if (Ta(t), t.stateNode === null) {
      var n = ra, f = a.contextType;
      typeof f == "object" && f !== null && (n = sl(f)), n = new a(u, n), t.memoizedState = n.state !== null && n.state !== void 0 ? n.state : null, n.updater = ac, t.stateNode = n, n._reactInternals = t, n = t.stateNode, n.props = u, n.state = t.memoizedState, n.refs = {}, Bf(t), f = a.contextType, n.context = typeof f == "object" && f !== null ? sl(f) : ra, n.state = t.memoizedState, f = a.getDerivedStateFromProps, typeof f == "function" && (tc(
        t,
        a,
        f,
        u
      ), n.state = t.memoizedState), typeof a.getDerivedStateFromProps == "function" || typeof n.getSnapshotBeforeUpdate == "function" || typeof n.UNSAFE_componentWillMount != "function" && typeof n.componentWillMount != "function" || (f = n.state, typeof n.componentWillMount == "function" && n.componentWillMount(), typeof n.UNSAFE_componentWillMount == "function" && n.UNSAFE_componentWillMount(), f !== n.state && ac.enqueueReplaceState(n, n.state, null), pu(t, u, n, e), Vu(), n.state = t.memoizedState), typeof n.componentDidMount == "function" && (t.flags |= 4194308), u = !0;
    } else if (l === null) {
      n = t.stateNode;
      var c = t.memoizedProps, i = Da(a, c);
      n.props = i;
      var d = n.context, s = a.contextType;
      f = ra, typeof s == "object" && s !== null && (f = sl(s));
      var T = a.getDerivedStateFromProps;
      s = typeof T == "function" || typeof n.getSnapshotBeforeUpdate == "function", c = t.pendingProps !== c, s || typeof n.UNSAFE_componentWillReceiveProps != "function" && typeof n.componentWillReceiveProps != "function" || (c || d !== f) && dv(
        t,
        n,
        u,
        f
      ), xt = !1;
      var h = t.memoizedState;
      n.state = h, pu(t, u, n, e), Vu(), d = t.memoizedState, c || h !== d || xt ? (typeof T == "function" && (tc(
        t,
        a,
        T,
        u
      ), d = t.memoizedState), (i = xt || mv(
        t,
        a,
        i,
        u,
        h,
        d,
        f
      )) ? (s || typeof n.UNSAFE_componentWillMount != "function" && typeof n.componentWillMount != "function" || (typeof n.componentWillMount == "function" && n.componentWillMount(), typeof n.UNSAFE_componentWillMount == "function" && n.UNSAFE_componentWillMount()), typeof n.componentDidMount == "function" && (t.flags |= 4194308)) : (typeof n.componentDidMount == "function" && (t.flags |= 4194308), t.memoizedProps = u, t.memoizedState = d), n.props = u, n.state = d, n.context = f, u = i) : (typeof n.componentDidMount == "function" && (t.flags |= 4194308), u = !1);
    } else {
      n = t.stateNode, Yf(l, t), f = t.memoizedProps, s = Da(a, f), n.props = s, T = t.pendingProps, h = n.context, d = a.contextType, i = ra, typeof d == "object" && d !== null && (i = sl(d)), c = a.getDerivedStateFromProps, (d = typeof c == "function" || typeof n.getSnapshotBeforeUpdate == "function") || typeof n.UNSAFE_componentWillReceiveProps != "function" && typeof n.componentWillReceiveProps != "function" || (f !== T || h !== i) && dv(
        t,
        n,
        u,
        i
      ), xt = !1, h = t.memoizedState, n.state = h, pu(t, u, n, e), Vu();
      var S = t.memoizedState;
      f !== T || h !== S || xt || l !== null && l.dependencies !== null && Ze(l.dependencies) ? (typeof c == "function" && (tc(
        t,
        a,
        c,
        u
      ), S = t.memoizedState), (s = xt || mv(
        t,
        a,
        s,
        u,
        h,
        S,
        i
      ) || l !== null && l.dependencies !== null && Ze(l.dependencies)) ? (d || typeof n.UNSAFE_componentWillUpdate != "function" && typeof n.componentWillUpdate != "function" || (typeof n.componentWillUpdate == "function" && n.componentWillUpdate(u, S, i), typeof n.UNSAFE_componentWillUpdate == "function" && n.UNSAFE_componentWillUpdate(
        u,
        S,
        i
      )), typeof n.componentDidUpdate == "function" && (t.flags |= 4), typeof n.getSnapshotBeforeUpdate == "function" && (t.flags |= 1024)) : (typeof n.componentDidUpdate != "function" || f === l.memoizedProps && h === l.memoizedState || (t.flags |= 4), typeof n.getSnapshotBeforeUpdate != "function" || f === l.memoizedProps && h === l.memoizedState || (t.flags |= 1024), t.memoizedProps = u, t.memoizedState = S), n.props = u, n.state = S, n.context = i, u = s) : (typeof n.componentDidUpdate != "function" || f === l.memoizedProps && h === l.memoizedState || (t.flags |= 4), typeof n.getSnapshotBeforeUpdate != "function" || f === l.memoizedProps && h === l.memoizedState || (t.flags |= 1024), u = !1);
    }
    return n = u, un(l, t), u = (t.flags & 128) !== 0, n || u ? (n = t.stateNode, a = u && typeof a.getDerivedStateFromError != "function" ? null : n.render(), t.flags |= 1, l !== null && u ? (t.child = Oa(
      t,
      l.child,
      null,
      e
    ), t.child = Oa(
      t,
      null,
      a,
      e
    )) : bl(l, t, a, e), t.memoizedState = n.state, l = t.child) : l = Ut(
      l,
      t,
      e
    ), l;
  }
  function _v(l, t, a, u) {
    return ba(), t.flags |= 256, bl(l, t, a, u), t.child;
  }
  var fc = {
    dehydrated: null,
    treeContext: null,
    retryLane: 0,
    hydrationErrors: null
  };
  function cc(l) {
    return { baseLanes: l, cachePool: s0() };
  }
  function ic(l, t, a) {
    return l = l !== null ? l.childLanes & ~a : 0, t && (l |= Zl), l;
  }
  function Hv(l, t, a) {
    var u = t.pendingProps, e = !1, n = (t.flags & 128) !== 0, f;
    if ((f = n) || (f = l !== null && l.memoizedState === null ? !1 : (el.current & 2) !== 0), f && (e = !0, t.flags &= -129), f = (t.flags & 32) !== 0, t.flags &= -33, l === null) {
      if (Q) {
        if (e ? Wt(t) : wt(), (l = F) ? (l = Qy(
          l,
          $l
        ), l = l !== null && l.data !== "&" ? l : null, l !== null && (t.memoizedState = {
          dehydrated: l,
          treeContext: Vt !== null ? { id: it, overflow: vt } : null,
          retryLane: 536870912,
          hydrationErrors: null
        }, a = c0(l), a.return = t, t.child = a, gl = t, F = null)) : l = null, l === null) throw Kt(t);
        return xc(l) ? t.lanes = 32 : t.lanes = 536870912, null;
      }
      var c = u.children;
      return u = u.fallback, e ? (wt(), e = t.mode, c = en(
        { mode: "hidden", children: c },
        e
      ), u = sa(
        u,
        e,
        a,
        null
      ), c.return = t, u.return = t, c.sibling = u, t.child = c, u = t.child, u.memoizedState = cc(a), u.childLanes = ic(
        l,
        f,
        a
      ), t.memoizedState = fc, ru(null, u)) : (Wt(t), vc(t, c));
    }
    var i = l.memoizedState;
    if (i !== null && (c = i.dehydrated, c !== null)) {
      if (n)
        t.flags & 256 ? (Wt(t), t.flags &= -257, t = yc(
          l,
          t,
          a
        )) : t.memoizedState !== null ? (wt(), t.child = l.child, t.flags |= 128, t = null) : (wt(), c = u.fallback, e = t.mode, u = en(
          { mode: "visible", children: u.children },
          e
        ), c = sa(
          c,
          e,
          a,
          null
        ), c.flags |= 2, u.return = t, c.return = t, u.sibling = c, t.child = u, Oa(
          t,
          l.child,
          null,
          a
        ), u = t.child, u.memoizedState = cc(a), u.childLanes = ic(
          l,
          f,
          a
        ), t.memoizedState = fc, t = ru(null, u));
      else if (Wt(t), xc(c)) {
        if (f = c.nextSibling && c.nextSibling.dataset, f) var d = f.dgst;
        f = d, u = Error(g(419)), u.stack = "", u.digest = f, Ru({ value: u, source: null, stack: null }), t = yc(
          l,
          t,
          a
        );
      } else if (il || Fa(l, t, a, !1), f = (a & l.childLanes) !== 0, il || f) {
        if (f = W, f !== null && (u = Si(f, a), u !== 0 && u !== i.retryLane))
          throw i.retryLane = u, ga(l, u), Hl(f, l, u), ec;
        Lc(c) || Sn(), t = yc(
          l,
          t,
          a
        );
      } else
        Lc(c) ? (t.flags |= 192, t.child = l.child, t = null) : (l = i.treeContext, F = kl(
          c.nextSibling
        ), gl = t, Q = !0, pt = null, $l = !1, l !== null && y0(t, l), t = vc(
          t,
          u.children
        ), t.flags |= 4096);
      return t;
    }
    return e ? (wt(), c = u.fallback, e = t.mode, i = l.child, d = i.sibling, u = At(i, {
      mode: "hidden",
      children: u.children
    }), u.subtreeFlags = i.subtreeFlags & 65011712, d !== null ? c = At(
      d,
      c
    ) : (c = sa(
      c,
      e,
      a,
      null
    ), c.flags |= 2), c.return = t, u.return = t, u.sibling = c, t.child = u, ru(null, u), u = t.child, c = l.child.memoizedState, c === null ? c = cc(a) : (e = c.cachePool, e !== null ? (i = fl._currentValue, e = e.parent !== i ? { parent: i, pool: i } : e) : e = s0(), c = {
      baseLanes: c.baseLanes | a,
      cachePool: e
    }), u.memoizedState = c, u.childLanes = ic(
      l,
      f,
      a
    ), t.memoizedState = fc, ru(l.child, u)) : (Wt(t), a = l.child, l = a.sibling, a = At(a, {
      mode: "visible",
      children: u.children
    }), a.return = t, a.sibling = null, l !== null && (f = t.deletions, f === null ? (t.deletions = [l], t.flags |= 16) : f.push(l)), t.child = a, t.memoizedState = null, a);
  }
  function vc(l, t) {
    return t = en(
      { mode: "visible", children: t },
      l.mode
    ), t.return = l, l.child = t;
  }
  function en(l, t) {
    return l = Rl(22, l, null, t), l.lanes = 0, l;
  }
  function yc(l, t, a) {
    return Oa(t, l.child, null, a), l = vc(
      t,
      t.pendingProps.children
    ), l.flags |= 2, t.memoizedState = null, l;
  }
  function Nv(l, t, a) {
    l.lanes |= t;
    var u = l.alternate;
    u !== null && (u.lanes |= t), Df(l.return, t, a);
  }
  function mc(l, t, a, u, e, n) {
    var f = l.memoizedState;
    f === null ? l.memoizedState = {
      isBackwards: t,
      rendering: null,
      renderingStartTime: 0,
      last: u,
      tail: a,
      tailMode: e,
      treeForkCount: n
    } : (f.isBackwards = t, f.rendering = null, f.renderingStartTime = 0, f.last = u, f.tail = a, f.tailMode = e, f.treeForkCount = n);
  }
  function qv(l, t, a) {
    var u = t.pendingProps, e = u.revealOrder, n = u.tail;
    u = u.children;
    var f = el.current, c = (f & 2) !== 0;
    if (c ? (f = f & 1 | 2, t.flags |= 128) : f &= 1, V(el, f), bl(l, t, u, a), u = Q ? Gu : 0, !c && l !== null && (l.flags & 128) !== 0)
      l: for (l = t.child; l !== null; ) {
        if (l.tag === 13)
          l.memoizedState !== null && Nv(l, a, t);
        else if (l.tag === 19)
          Nv(l, a, t);
        else if (l.child !== null) {
          l.child.return = l, l = l.child;
          continue;
        }
        if (l === t) break l;
        for (; l.sibling === null; ) {
          if (l.return === null || l.return === t)
            break l;
          l = l.return;
        }
        l.sibling.return = l.return, l = l.sibling;
      }
    switch (e) {
      case "forwards":
        for (a = t.child, e = null; a !== null; )
          l = a.alternate, l !== null && re(l) === null && (e = a), a = a.sibling;
        a = e, a === null ? (e = t.child, t.child = null) : (e = a.sibling, a.sibling = null), mc(
          t,
          !1,
          e,
          a,
          n,
          u
        );
        break;
      case "backwards":
      case "unstable_legacy-backwards":
        for (a = null, e = t.child, t.child = null; e !== null; ) {
          if (l = e.alternate, l !== null && re(l) === null) {
            t.child = e;
            break;
          }
          l = e.sibling, e.sibling = a, a = e, e = l;
        }
        mc(
          t,
          !0,
          a,
          null,
          n,
          u
        );
        break;
      case "together":
        mc(
          t,
          !1,
          null,
          null,
          void 0,
          u
        );
        break;
      default:
        t.memoizedState = null;
    }
    return t.child;
  }
  function Ut(l, t, a) {
    if (l !== null && (t.dependencies = l.dependencies), kt |= t.lanes, (a & t.childLanes) === 0)
      if (l !== null) {
        if (Fa(
          l,
          t,
          a,
          !1
        ), (a & t.childLanes) === 0)
          return null;
      } else return null;
    if (l !== null && t.child !== l.child)
      throw Error(g(153));
    if (t.child !== null) {
      for (l = t.child, a = At(l, l.pendingProps), t.child = a, a.return = t; l.sibling !== null; )
        l = l.sibling, a = a.sibling = At(l, l.pendingProps), a.return = t;
      a.sibling = null;
    }
    return t.child;
  }
  function dc(l, t) {
    return (l.lanes & t) !== 0 ? !0 : (l = l.dependencies, !!(l !== null && Ze(l)));
  }
  function Qm(l, t, a) {
    switch (t.tag) {
      case 3:
        ge(t, t.stateNode.containerInfo), Lt(t, fl, l.memoizedState.cache), ba();
        break;
      case 27:
      case 5:
        Xn(t);
        break;
      case 4:
        ge(t, t.stateNode.containerInfo);
        break;
      case 10:
        Lt(
          t,
          t.type,
          t.memoizedProps.value
        );
        break;
      case 31:
        if (t.memoizedState !== null)
          return t.flags |= 128, Cf(t), null;
        break;
      case 13:
        var u = t.memoizedState;
        if (u !== null)
          return u.dehydrated !== null ? (Wt(t), t.flags |= 128, null) : (a & t.child.childLanes) !== 0 ? Hv(l, t, a) : (Wt(t), l = Ut(
            l,
            t,
            a
          ), l !== null ? l.sibling : null);
        Wt(t);
        break;
      case 19:
        var e = (l.flags & 128) !== 0;
        if (u = (a & t.childLanes) !== 0, u || (Fa(
          l,
          t,
          a,
          !1
        ), u = (a & t.childLanes) !== 0), e) {
          if (u)
            return qv(
              l,
              t,
              a
            );
          t.flags |= 128;
        }
        if (e = t.memoizedState, e !== null && (e.rendering = null, e.tail = null, e.lastEffect = null), V(el, el.current), u) break;
        return null;
      case 22:
        return t.lanes = 0, ov(
          l,
          t,
          a,
          t.pendingProps
        );
      case 24:
        Lt(t, fl, l.memoizedState.cache);
    }
    return Ut(l, t, a);
  }
  function Bv(l, t, a) {
    if (l !== null)
      if (l.memoizedProps !== t.pendingProps)
        il = !0;
      else {
        if (!dc(l, a) && (t.flags & 128) === 0)
          return il = !1, Qm(
            l,
            t,
            a
          );
        il = (l.flags & 131072) !== 0;
      }
    else
      il = !1, Q && (t.flags & 1048576) !== 0 && v0(t, Gu, t.index);
    switch (t.lanes = 0, t.tag) {
      case 16:
        l: {
          var u = t.pendingProps;
          if (l = Ea(t.elementType), t.type = l, typeof l == "function")
            bf(l) ? (u = Da(l, u), t.tag = 1, t = Uv(
              null,
              t,
              l,
              u,
              a
            )) : (t.tag = 0, t = nc(
              null,
              t,
              l,
              u,
              a
            ));
          else {
            if (l != null) {
              var e = l.$$typeof;
              if (e === Xt) {
                t.tag = 11, t = Tv(
                  null,
                  t,
                  l,
                  u,
                  a
                );
                break l;
              } else if (e === gt) {
                t.tag = 14, t = Av(
                  null,
                  t,
                  l,
                  u,
                  a
                );
                break l;
              }
            }
            throw t = zu(l) || l, Error(g(306, t, ""));
          }
        }
        return t;
      case 0:
        return nc(
          l,
          t,
          t.type,
          t.pendingProps,
          a
        );
      case 1:
        return u = t.type, e = Da(
          u,
          t.pendingProps
        ), Uv(
          l,
          t,
          u,
          e,
          a
        );
      case 3:
        l: {
          if (ge(
            t,
            t.stateNode.containerInfo
          ), l === null) throw Error(g(387));
          u = t.pendingProps;
          var n = t.memoizedState;
          e = n.element, Yf(l, t), pu(t, u, null, a);
          var f = t.memoizedState;
          if (u = f.cache, Lt(t, fl, u), u !== n.cache && Uf(
            t,
            [fl],
            a,
            !0
          ), Vu(), u = f.element, n.isDehydrated)
            if (n = {
              element: u,
              isDehydrated: !1,
              cache: f.cache
            }, t.updateQueue.baseState = n, t.memoizedState = n, t.flags & 256) {
              t = _v(
                l,
                t,
                u,
                a
              );
              break l;
            } else if (u !== e) {
              e = rl(
                Error(g(424)),
                t
              ), Ru(e), t = _v(
                l,
                t,
                u,
                a
              );
              break l;
            } else
              for (l = t.stateNode.containerInfo, l.nodeType === 9 ? l = l.body : l = l.nodeName === "HTML" ? l.ownerDocument.body : l, F = kl(l.firstChild), gl = t, Q = !0, pt = null, $l = !0, a = o0(
                t,
                null,
                u,
                a
              ), t.child = a; a; )
                a.flags = a.flags & -3 | 4096, a = a.sibling;
          else {
            if (ba(), u === e) {
              t = Ut(
                l,
                t,
                a
              );
              break l;
            }
            bl(l, t, u, a);
          }
          t = t.child;
        }
        return t;
      case 26:
        return un(l, t), l === null ? (a = Ky(
          t.type,
          null,
          t.pendingProps,
          null
        )) ? t.memoizedState = a : Q || (a = t.type, l = t.pendingProps, u = En(
          Qt.current
        ).createElement(a), u[Sl] = t, u[ol] = l, zl(u, a, l), dl(u), t.stateNode = u) : t.memoizedState = Ky(
          t.type,
          l.memoizedProps,
          t.pendingProps,
          l.memoizedState
        ), null;
      case 27:
        return Xn(t), l === null && Q && (u = t.stateNode = jy(
          t.type,
          t.pendingProps,
          Qt.current
        ), gl = t, $l = !0, e = F, aa(t.type) ? (Jc = e, F = kl(u.firstChild)) : F = e), bl(
          l,
          t,
          t.pendingProps.children,
          a
        ), un(l, t), l === null && (t.flags |= 4194304), t.child;
      case 5:
        return l === null && Q && ((e = u = F) && (u = dd(
          u,
          t.type,
          t.pendingProps,
          $l
        ), u !== null ? (t.stateNode = u, gl = t, F = kl(u.firstChild), $l = !1, e = !0) : e = !1), e || Kt(t)), Xn(t), e = t.type, n = t.pendingProps, f = l !== null ? l.memoizedProps : null, u = n.children, Vc(e, n) ? u = null : f !== null && Vc(e, f) && (t.flags |= 32), t.memoizedState !== null && (e = jf(
          l,
          t,
          _m,
          null,
          null,
          a
        ), ce._currentValue = e), un(l, t), bl(l, t, u, a), t.child;
      case 6:
        return l === null && Q && ((l = a = F) && (a = hd(
          a,
          t.pendingProps,
          $l
        ), a !== null ? (t.stateNode = a, gl = t, F = null, l = !0) : l = !1), l || Kt(t)), null;
      case 13:
        return Hv(l, t, a);
      case 4:
        return ge(
          t,
          t.stateNode.containerInfo
        ), u = t.pendingProps, l === null ? t.child = Oa(
          t,
          null,
          u,
          a
        ) : bl(l, t, u, a), t.child;
      case 11:
        return Tv(
          l,
          t,
          t.type,
          t.pendingProps,
          a
        );
      case 7:
        return bl(
          l,
          t,
          t.pendingProps,
          a
        ), t.child;
      case 8:
        return bl(
          l,
          t,
          t.pendingProps.children,
          a
        ), t.child;
      case 12:
        return bl(
          l,
          t,
          t.pendingProps.children,
          a
        ), t.child;
      case 10:
        return u = t.pendingProps, Lt(t, t.type, u.value), bl(l, t, u.children, a), t.child;
      case 9:
        return e = t.type._context, u = t.pendingProps.children, Ta(t), e = sl(e), u = u(e), t.flags |= 1, bl(l, t, u, a), t.child;
      case 14:
        return Av(
          l,
          t,
          t.type,
          t.pendingProps,
          a
        );
      case 15:
        return Ev(
          l,
          t,
          t.type,
          t.pendingProps,
          a
        );
      case 19:
        return qv(l, t, a);
      case 31:
        return Xm(l, t, a);
      case 22:
        return ov(
          l,
          t,
          a,
          t.pendingProps
        );
      case 24:
        return Ta(t), u = sl(fl), l === null ? (e = Nf(), e === null && (e = W, n = _f(), e.pooledCache = n, n.refCount++, n !== null && (e.pooledCacheLanes |= a), e = n), t.memoizedState = { parent: u, cache: e }, Bf(t), Lt(t, fl, e)) : ((l.lanes & a) !== 0 && (Yf(l, t), pu(t, null, null, a), Vu()), e = l.memoizedState, n = t.memoizedState, e.parent !== u ? (e = { parent: u, cache: u }, t.memoizedState = e, t.lanes === 0 && (t.memoizedState = t.updateQueue.baseState = e), Lt(t, fl, u)) : (u = n.cache, Lt(t, fl, u), u !== e.cache && Uf(
          t,
          [fl],
          a,
          !0
        ))), bl(
          l,
          t,
          t.pendingProps.children,
          a
        ), t.child;
      case 29:
        throw t.pendingProps;
    }
    throw Error(g(156, t.tag));
  }
  function _t(l) {
    l.flags |= 4;
  }
  function hc(l, t, a, u, e) {
    if ((t = (l.mode & 32) !== 0) && (t = !1), t) {
      if (l.flags |= 16777216, (e & 335544128) === e)
        if (l.stateNode.complete) l.flags |= 8192;
        else if (ey()) l.flags |= 8192;
        else
          throw oa = Ke, qf;
    } else l.flags &= -16777217;
  }
  function Yv(l, t) {
    if (t.type !== "stylesheet" || (t.state.loading & 4) !== 0)
      l.flags &= -16777217;
    else if (l.flags |= 16777216, !Wy(t))
      if (ey()) l.flags |= 8192;
      else
        throw oa = Ke, qf;
  }
  function nn(l, t) {
    t !== null && (l.flags |= 4), l.flags & 16384 && (t = l.tag !== 22 ? mi() : 536870912, l.lanes |= t, iu |= t);
  }
  function Wu(l, t) {
    if (!Q)
      switch (l.tailMode) {
        case "hidden":
          t = l.tail;
          for (var a = null; t !== null; )
            t.alternate !== null && (a = t), t = t.sibling;
          a === null ? l.tail = null : a.sibling = null;
          break;
        case "collapsed":
          a = l.tail;
          for (var u = null; a !== null; )
            a.alternate !== null && (u = a), a = a.sibling;
          u === null ? t || l.tail === null ? l.tail = null : l.tail.sibling = null : u.sibling = null;
      }
  }
  function k(l) {
    var t = l.alternate !== null && l.alternate.child === l.child, a = 0, u = 0;
    if (t)
      for (var e = l.child; e !== null; )
        a |= e.lanes | e.childLanes, u |= e.subtreeFlags & 65011712, u |= e.flags & 65011712, e.return = l, e = e.sibling;
    else
      for (e = l.child; e !== null; )
        a |= e.lanes | e.childLanes, u |= e.subtreeFlags, u |= e.flags, e.return = l, e = e.sibling;
    return l.subtreeFlags |= u, l.childLanes = a, t;
  }
  function Cm(l, t, a) {
    var u = t.pendingProps;
    switch (Ef(t), t.tag) {
      case 16:
      case 15:
      case 0:
      case 11:
      case 7:
      case 8:
      case 12:
      case 9:
      case 14:
        return k(t), null;
      case 1:
        return k(t), null;
      case 3:
        return a = t.stateNode, u = null, l !== null && (u = l.memoizedState.cache), t.memoizedState.cache !== u && (t.flags |= 2048), Ot(fl), Ya(), a.pendingContext && (a.context = a.pendingContext, a.pendingContext = null), (l === null || l.child === null) && ($a(t) ? _t(t) : l === null || l.memoizedState.isDehydrated && (t.flags & 256) === 0 || (t.flags |= 1024, Of())), k(t), null;
      case 26:
        var e = t.type, n = t.memoizedState;
        return l === null ? (_t(t), n !== null ? (k(t), Yv(t, n)) : (k(t), hc(
          t,
          e,
          null,
          u,
          a
        ))) : n ? n !== l.memoizedState ? (_t(t), k(t), Yv(t, n)) : (k(t), t.flags &= -16777217) : (l = l.memoizedProps, l !== u && _t(t), k(t), hc(
          t,
          e,
          l,
          u,
          a
        )), null;
      case 27:
        if (se(t), a = Qt.current, e = t.type, l !== null && t.stateNode != null)
          l.memoizedProps !== u && _t(t);
        else {
          if (!u) {
            if (t.stateNode === null)
              throw Error(g(166));
            return k(t), null;
          }
          l = ml.current, $a(t) ? m0(t) : (l = jy(e, u, a), t.stateNode = l, _t(t));
        }
        return k(t), null;
      case 5:
        if (se(t), e = t.type, l !== null && t.stateNode != null)
          l.memoizedProps !== u && _t(t);
        else {
          if (!u) {
            if (t.stateNode === null)
              throw Error(g(166));
            return k(t), null;
          }
          if (n = ml.current, $a(t))
            m0(t);
          else {
            var f = En(
              Qt.current
            );
            switch (n) {
              case 1:
                n = f.createElementNS(
                  "http://www.w3.org/2000/svg",
                  e
                );
                break;
              case 2:
                n = f.createElementNS(
                  "http://www.w3.org/1998/Math/MathML",
                  e
                );
                break;
              default:
                switch (e) {
                  case "svg":
                    n = f.createElementNS(
                      "http://www.w3.org/2000/svg",
                      e
                    );
                    break;
                  case "math":
                    n = f.createElementNS(
                      "http://www.w3.org/1998/Math/MathML",
                      e
                    );
                    break;
                  case "script":
                    n = f.createElement("div"), n.innerHTML = "<script><\/script>", n = n.removeChild(
                      n.firstChild
                    );
                    break;
                  case "select":
                    n = typeof u.is == "string" ? f.createElement("select", {
                      is: u.is
                    }) : f.createElement("select"), u.multiple ? n.multiple = !0 : u.size && (n.size = u.size);
                    break;
                  default:
                    n = typeof u.is == "string" ? f.createElement(e, { is: u.is }) : f.createElement(e);
                }
            }
            n[Sl] = t, n[ol] = u;
            l: for (f = t.child; f !== null; ) {
              if (f.tag === 5 || f.tag === 6)
                n.appendChild(f.stateNode);
              else if (f.tag !== 4 && f.tag !== 27 && f.child !== null) {
                f.child.return = f, f = f.child;
                continue;
              }
              if (f === t) break l;
              for (; f.sibling === null; ) {
                if (f.return === null || f.return === t)
                  break l;
                f = f.return;
              }
              f.sibling.return = f.return, f = f.sibling;
            }
            t.stateNode = n;
            l: switch (zl(n, e, u), e) {
              case "button":
              case "input":
              case "select":
              case "textarea":
                u = !!u.autoFocus;
                break l;
              case "img":
                u = !0;
                break l;
              default:
                u = !1;
            }
            u && _t(t);
          }
        }
        return k(t), hc(
          t,
          t.type,
          l === null ? null : l.memoizedProps,
          t.pendingProps,
          a
        ), null;
      case 6:
        if (l && t.stateNode != null)
          l.memoizedProps !== u && _t(t);
        else {
          if (typeof u != "string" && t.stateNode === null)
            throw Error(g(166));
          if (l = Qt.current, $a(t)) {
            if (l = t.stateNode, a = t.memoizedProps, u = null, e = gl, e !== null)
              switch (e.tag) {
                case 27:
                case 5:
                  u = e.memoizedProps;
              }
            l[Sl] = t, l = !!(l.nodeValue === a || u !== null && u.suppressHydrationWarning === !0 || Hy(l.nodeValue, a)), l || Kt(t, !0);
          } else
            l = En(l).createTextNode(
              u
            ), l[Sl] = t, t.stateNode = l;
        }
        return k(t), null;
      case 31:
        if (a = t.memoizedState, l === null || l.memoizedState !== null) {
          if (u = $a(t), a !== null) {
            if (l === null) {
              if (!u) throw Error(g(318));
              if (l = t.memoizedState, l = l !== null ? l.dehydrated : null, !l) throw Error(g(557));
              l[Sl] = t;
            } else
              ba(), (t.flags & 128) === 0 && (t.memoizedState = null), t.flags |= 4;
            k(t), l = !1;
          } else
            a = Of(), l !== null && l.memoizedState !== null && (l.memoizedState.hydrationErrors = a), l = !0;
          if (!l)
            return t.flags & 256 ? (Ql(t), t) : (Ql(t), null);
          if ((t.flags & 128) !== 0)
            throw Error(g(558));
        }
        return k(t), null;
      case 13:
        if (u = t.memoizedState, l === null || l.memoizedState !== null && l.memoizedState.dehydrated !== null) {
          if (e = $a(t), u !== null && u.dehydrated !== null) {
            if (l === null) {
              if (!e) throw Error(g(318));
              if (e = t.memoizedState, e = e !== null ? e.dehydrated : null, !e) throw Error(g(317));
              e[Sl] = t;
            } else
              ba(), (t.flags & 128) === 0 && (t.memoizedState = null), t.flags |= 4;
            k(t), e = !1;
          } else
            e = Of(), l !== null && l.memoizedState !== null && (l.memoizedState.hydrationErrors = e), e = !0;
          if (!e)
            return t.flags & 256 ? (Ql(t), t) : (Ql(t), null);
        }
        return Ql(t), (t.flags & 128) !== 0 ? (t.lanes = a, t) : (a = u !== null, l = l !== null && l.memoizedState !== null, a && (u = t.child, e = null, u.alternate !== null && u.alternate.memoizedState !== null && u.alternate.memoizedState.cachePool !== null && (e = u.alternate.memoizedState.cachePool.pool), n = null, u.memoizedState !== null && u.memoizedState.cachePool !== null && (n = u.memoizedState.cachePool.pool), n !== e && (u.flags |= 2048)), a !== l && a && (t.child.flags |= 8192), nn(t, t.updateQueue), k(t), null);
      case 4:
        return Ya(), l === null && Xc(t.stateNode.containerInfo), k(t), null;
      case 10:
        return Ot(t.type), k(t), null;
      case 19:
        if (P(el), u = t.memoizedState, u === null) return k(t), null;
        if (e = (t.flags & 128) !== 0, n = u.rendering, n === null)
          if (e) Wu(u, !1);
          else {
            if (tl !== 0 || l !== null && (l.flags & 128) !== 0)
              for (l = t.child; l !== null; ) {
                if (n = re(l), n !== null) {
                  for (t.flags |= 128, Wu(u, !1), l = n.updateQueue, t.updateQueue = l, nn(t, l), t.subtreeFlags = 0, l = a, a = t.child; a !== null; )
                    f0(a, l), a = a.sibling;
                  return V(
                    el,
                    el.current & 1 | 2
                  ), Q && Et(t, u.treeForkCount), t.child;
                }
                l = l.sibling;
              }
            u.tail !== null && ql() > mn && (t.flags |= 128, e = !0, Wu(u, !1), t.lanes = 4194304);
          }
        else {
          if (!e)
            if (l = re(n), l !== null) {
              if (t.flags |= 128, e = !0, l = l.updateQueue, t.updateQueue = l, nn(t, l), Wu(u, !0), u.tail === null && u.tailMode === "hidden" && !n.alternate && !Q)
                return k(t), null;
            } else
              2 * ql() - u.renderingStartTime > mn && a !== 536870912 && (t.flags |= 128, e = !0, Wu(u, !1), t.lanes = 4194304);
          u.isBackwards ? (n.sibling = t.child, t.child = n) : (l = u.last, l !== null ? l.sibling = n : t.child = n, u.last = n);
        }
        return u.tail !== null ? (l = u.tail, u.rendering = l, u.tail = l.sibling, u.renderingStartTime = ql(), l.sibling = null, a = el.current, V(
          el,
          e ? a & 1 | 2 : a & 1
        ), Q && Et(t, u.treeForkCount), l) : (k(t), null);
      case 22:
      case 23:
        return Ql(t), Qf(), u = t.memoizedState !== null, l !== null ? l.memoizedState !== null !== u && (t.flags |= 8192) : u && (t.flags |= 8192), u ? (a & 536870912) !== 0 && (t.flags & 128) === 0 && (k(t), t.subtreeFlags & 6 && (t.flags |= 8192)) : k(t), a = t.updateQueue, a !== null && nn(t, a.retryQueue), a = null, l !== null && l.memoizedState !== null && l.memoizedState.cachePool !== null && (a = l.memoizedState.cachePool.pool), u = null, t.memoizedState !== null && t.memoizedState.cachePool !== null && (u = t.memoizedState.cachePool.pool), u !== a && (t.flags |= 2048), l !== null && P(Aa), null;
      case 24:
        return a = null, l !== null && (a = l.memoizedState.cache), t.memoizedState.cache !== a && (t.flags |= 2048), Ot(fl), k(t), null;
      case 25:
        return null;
      case 30:
        return null;
    }
    throw Error(g(156, t.tag));
  }
  function Zm(l, t) {
    switch (Ef(t), t.tag) {
      case 1:
        return l = t.flags, l & 65536 ? (t.flags = l & -65537 | 128, t) : null;
      case 3:
        return Ot(fl), Ya(), l = t.flags, (l & 65536) !== 0 && (l & 128) === 0 ? (t.flags = l & -65537 | 128, t) : null;
      case 26:
      case 27:
      case 5:
        return se(t), null;
      case 31:
        if (t.memoizedState !== null) {
          if (Ql(t), t.alternate === null)
            throw Error(g(340));
          ba();
        }
        return l = t.flags, l & 65536 ? (t.flags = l & -65537 | 128, t) : null;
      case 13:
        if (Ql(t), l = t.memoizedState, l !== null && l.dehydrated !== null) {
          if (t.alternate === null)
            throw Error(g(340));
          ba();
        }
        return l = t.flags, l & 65536 ? (t.flags = l & -65537 | 128, t) : null;
      case 19:
        return P(el), null;
      case 4:
        return Ya(), null;
      case 10:
        return Ot(t.type), null;
      case 22:
      case 23:
        return Ql(t), Qf(), l !== null && P(Aa), l = t.flags, l & 65536 ? (t.flags = l & -65537 | 128, t) : null;
      case 24:
        return Ot(fl), null;
      case 25:
        return null;
      default:
        return null;
    }
  }
  function Gv(l, t) {
    switch (Ef(t), t.tag) {
      case 3:
        Ot(fl), Ya();
        break;
      case 26:
      case 27:
      case 5:
        se(t);
        break;
      case 4:
        Ya();
        break;
      case 31:
        t.memoizedState !== null && Ql(t);
        break;
      case 13:
        Ql(t);
        break;
      case 19:
        P(el);
        break;
      case 10:
        Ot(t.type);
        break;
      case 22:
      case 23:
        Ql(t), Qf(), l !== null && P(Aa);
        break;
      case 24:
        Ot(fl);
    }
  }
  function wu(l, t) {
    try {
      var a = t.updateQueue, u = a !== null ? a.lastEffect : null;
      if (u !== null) {
        var e = u.next;
        a = e;
        do {
          if ((a.tag & l) === l) {
            u = void 0;
            var n = a.create, f = a.inst;
            u = n(), f.destroy = u;
          }
          a = a.next;
        } while (a !== e);
      }
    } catch (c) {
      K(t, t.return, c);
    }
  }
  function $t(l, t, a) {
    try {
      var u = t.updateQueue, e = u !== null ? u.lastEffect : null;
      if (e !== null) {
        var n = e.next;
        u = n;
        do {
          if ((u.tag & l) === l) {
            var f = u.inst, c = f.destroy;
            if (c !== void 0) {
              f.destroy = void 0, e = t;
              var i = a, d = c;
              try {
                d();
              } catch (s) {
                K(
                  e,
                  i,
                  s
                );
              }
            }
          }
          u = u.next;
        } while (u !== n);
      }
    } catch (s) {
      K(t, t.return, s);
    }
  }
  function Rv(l) {
    var t = l.updateQueue;
    if (t !== null) {
      var a = l.stateNode;
      try {
        M0(t, a);
      } catch (u) {
        K(l, l.return, u);
      }
    }
  }
  function Xv(l, t, a) {
    a.props = Da(
      l.type,
      l.memoizedProps
    ), a.state = l.memoizedState;
    try {
      a.componentWillUnmount();
    } catch (u) {
      K(l, t, u);
    }
  }
  function $u(l, t) {
    try {
      var a = l.ref;
      if (a !== null) {
        switch (l.tag) {
          case 26:
          case 27:
          case 5:
            var u = l.stateNode;
            break;
          case 30:
            u = l.stateNode;
            break;
          default:
            u = l.stateNode;
        }
        typeof a == "function" ? l.refCleanup = a(u) : a.current = u;
      }
    } catch (e) {
      K(l, t, e);
    }
  }
  function yt(l, t) {
    var a = l.ref, u = l.refCleanup;
    if (a !== null)
      if (typeof u == "function")
        try {
          u();
        } catch (e) {
          K(l, t, e);
        } finally {
          l.refCleanup = null, l = l.alternate, l != null && (l.refCleanup = null);
        }
      else if (typeof a == "function")
        try {
          a(null);
        } catch (e) {
          K(l, t, e);
        }
      else a.current = null;
  }
  function Qv(l) {
    var t = l.type, a = l.memoizedProps, u = l.stateNode;
    try {
      l: switch (t) {
        case "button":
        case "input":
        case "select":
        case "textarea":
          a.autoFocus && u.focus();
          break l;
        case "img":
          a.src ? u.src = a.src : a.srcSet && (u.srcset = a.srcSet);
      }
    } catch (e) {
      K(l, l.return, e);
    }
  }
  function Sc(l, t, a) {
    try {
      var u = l.stateNode;
      fd(u, l.type, a, t), u[ol] = t;
    } catch (e) {
      K(l, l.return, e);
    }
  }
  function Cv(l) {
    return l.tag === 5 || l.tag === 3 || l.tag === 26 || l.tag === 27 && aa(l.type) || l.tag === 4;
  }
  function gc(l) {
    l: for (; ; ) {
      for (; l.sibling === null; ) {
        if (l.return === null || Cv(l.return)) return null;
        l = l.return;
      }
      for (l.sibling.return = l.return, l = l.sibling; l.tag !== 5 && l.tag !== 6 && l.tag !== 18; ) {
        if (l.tag === 27 && aa(l.type) || l.flags & 2 || l.child === null || l.tag === 4) continue l;
        l.child.return = l, l = l.child;
      }
      if (!(l.flags & 2)) return l.stateNode;
    }
  }
  function sc(l, t, a) {
    var u = l.tag;
    if (u === 5 || u === 6)
      l = l.stateNode, t ? (a.nodeType === 9 ? a.body : a.nodeName === "HTML" ? a.ownerDocument.body : a).insertBefore(l, t) : (t = a.nodeType === 9 ? a.body : a.nodeName === "HTML" ? a.ownerDocument.body : a, t.appendChild(l), a = a._reactRootContainer, a != null || t.onclick !== null || (t.onclick = zt));
    else if (u !== 4 && (u === 27 && aa(l.type) && (a = l.stateNode, t = null), l = l.child, l !== null))
      for (sc(l, t, a), l = l.sibling; l !== null; )
        sc(l, t, a), l = l.sibling;
  }
  function fn(l, t, a) {
    var u = l.tag;
    if (u === 5 || u === 6)
      l = l.stateNode, t ? a.insertBefore(l, t) : a.appendChild(l);
    else if (u !== 4 && (u === 27 && aa(l.type) && (a = l.stateNode), l = l.child, l !== null))
      for (fn(l, t, a), l = l.sibling; l !== null; )
        fn(l, t, a), l = l.sibling;
  }
  function Zv(l) {
    var t = l.stateNode, a = l.memoizedProps;
    try {
      for (var u = l.type, e = t.attributes; e.length; )
        t.removeAttributeNode(e[0]);
      zl(t, u, a), t[Sl] = l, t[ol] = a;
    } catch (n) {
      K(l, l.return, n);
    }
  }
  var Ht = !1, vl = !1, bc = !1, jv = typeof WeakSet == "function" ? WeakSet : Set, hl = null;
  function jm(l, t) {
    if (l = l.containerInfo, Zc = Hn, l = ki(l), yf(l)) {
      if ("selectionStart" in l)
        var a = {
          start: l.selectionStart,
          end: l.selectionEnd
        };
      else
        l: {
          a = (a = l.ownerDocument) && a.defaultView || window;
          var u = a.getSelection && a.getSelection();
          if (u && u.rangeCount !== 0) {
            a = u.anchorNode;
            var e = u.anchorOffset, n = u.focusNode;
            u = u.focusOffset;
            try {
              a.nodeType, n.nodeType;
            } catch {
              a = null;
              break l;
            }
            var f = 0, c = -1, i = -1, d = 0, s = 0, T = l, h = null;
            t: for (; ; ) {
              for (var S; T !== a || e !== 0 && T.nodeType !== 3 || (c = f + e), T !== n || u !== 0 && T.nodeType !== 3 || (i = f + u), T.nodeType === 3 && (f += T.nodeValue.length), (S = T.firstChild) !== null; )
                h = T, T = S;
              for (; ; ) {
                if (T === l) break t;
                if (h === a && ++d === e && (c = f), h === n && ++s === u && (i = f), (S = T.nextSibling) !== null) break;
                T = h, h = T.parentNode;
              }
              T = S;
            }
            a = c === -1 || i === -1 ? null : { start: c, end: i };
          } else a = null;
        }
      a = a || { start: 0, end: 0 };
    } else a = null;
    for (jc = { focusedElem: l, selectionRange: a }, Hn = !1, hl = t; hl !== null; )
      if (t = hl, l = t.child, (t.subtreeFlags & 1028) !== 0 && l !== null)
        l.return = t, hl = l;
      else
        for (; hl !== null; ) {
          switch (t = hl, n = t.alternate, l = t.flags, t.tag) {
            case 0:
              if ((l & 4) !== 0 && (l = t.updateQueue, l = l !== null ? l.events : null, l !== null))
                for (a = 0; a < l.length; a++)
                  e = l[a], e.ref.impl = e.nextImpl;
              break;
            case 11:
            case 15:
              break;
            case 1:
              if ((l & 1024) !== 0 && n !== null) {
                l = void 0, a = t, e = n.memoizedProps, n = n.memoizedState, u = a.stateNode;
                try {
                  var o = Da(
                    a.type,
                    e
                  );
                  l = u.getSnapshotBeforeUpdate(
                    o,
                    n
                  ), u.__reactInternalSnapshotBeforeUpdate = l;
                } catch (_) {
                  K(
                    a,
                    a.return,
                    _
                  );
                }
              }
              break;
            case 3:
              if ((l & 1024) !== 0) {
                if (l = t.stateNode.containerInfo, a = l.nodeType, a === 9)
                  Kc(l);
                else if (a === 1)
                  switch (l.nodeName) {
                    case "HEAD":
                    case "HTML":
                    case "BODY":
                      Kc(l);
                      break;
                    default:
                      l.textContent = "";
                  }
              }
              break;
            case 5:
            case 26:
            case 27:
            case 6:
            case 4:
            case 17:
              break;
            default:
              if ((l & 1024) !== 0) throw Error(g(163));
          }
          if (l = t.sibling, l !== null) {
            l.return = t.return, hl = l;
            break;
          }
          hl = t.return;
        }
  }
  function Vv(l, t, a) {
    var u = a.flags;
    switch (a.tag) {
      case 0:
      case 11:
      case 15:
        qt(l, a), u & 4 && wu(5, a);
        break;
      case 1:
        if (qt(l, a), u & 4)
          if (l = a.stateNode, t === null)
            try {
              l.componentDidMount();
            } catch (f) {
              K(a, a.return, f);
            }
          else {
            var e = Da(
              a.type,
              t.memoizedProps
            );
            t = t.memoizedState;
            try {
              l.componentDidUpdate(
                e,
                t,
                l.__reactInternalSnapshotBeforeUpdate
              );
            } catch (f) {
              K(
                a,
                a.return,
                f
              );
            }
          }
        u & 64 && Rv(a), u & 512 && $u(a, a.return);
        break;
      case 3:
        if (qt(l, a), u & 64 && (l = a.updateQueue, l !== null)) {
          if (t = null, a.child !== null)
            switch (a.child.tag) {
              case 27:
              case 5:
                t = a.child.stateNode;
                break;
              case 1:
                t = a.child.stateNode;
            }
          try {
            M0(l, t);
          } catch (f) {
            K(a, a.return, f);
          }
        }
        break;
      case 27:
        t === null && u & 4 && Zv(a);
      case 26:
      case 5:
        qt(l, a), t === null && u & 4 && Qv(a), u & 512 && $u(a, a.return);
        break;
      case 12:
        qt(l, a);
        break;
      case 31:
        qt(l, a), u & 4 && Lv(l, a);
        break;
      case 13:
        qt(l, a), u & 4 && xv(l, a), u & 64 && (l = a.memoizedState, l !== null && (l = l.dehydrated, l !== null && (a = wm.bind(
          null,
          a
        ), Sd(l, a))));
        break;
      case 22:
        if (u = a.memoizedState !== null || Ht, !u) {
          t = t !== null && t.memoizedState !== null || vl, e = Ht;
          var n = vl;
          Ht = u, (vl = t) && !n ? Bt(
            l,
            a,
            (a.subtreeFlags & 8772) !== 0
          ) : qt(l, a), Ht = e, vl = n;
        }
        break;
      case 30:
        break;
      default:
        qt(l, a);
    }
  }
  function pv(l) {
    var t = l.alternate;
    t !== null && (l.alternate = null, pv(t)), l.child = null, l.deletions = null, l.sibling = null, l.tag === 5 && (t = l.stateNode, t !== null && rn(t)), l.stateNode = null, l.return = null, l.dependencies = null, l.memoizedProps = null, l.memoizedState = null, l.pendingProps = null, l.stateNode = null, l.updateQueue = null;
  }
  var I = null, Ml = !1;
  function Nt(l, t, a) {
    for (a = a.child; a !== null; )
      Kv(l, t, a), a = a.sibling;
  }
  function Kv(l, t, a) {
    if (Bl && typeof Bl.onCommitFiberUnmount == "function")
      try {
        Bl.onCommitFiberUnmount(Tu, a);
      } catch {
      }
    switch (a.tag) {
      case 26:
        vl || yt(a, t), Nt(
          l,
          t,
          a
        ), a.memoizedState ? a.memoizedState.count-- : a.stateNode && (a = a.stateNode, a.parentNode.removeChild(a));
        break;
      case 27:
        vl || yt(a, t);
        var u = I, e = Ml;
        aa(a.type) && (I = a.stateNode, Ml = !1), Nt(
          l,
          t,
          a
        ), ee(a.stateNode), I = u, Ml = e;
        break;
      case 5:
        vl || yt(a, t);
      case 6:
        if (u = I, e = Ml, I = null, Nt(
          l,
          t,
          a
        ), I = u, Ml = e, I !== null)
          if (Ml)
            try {
              (I.nodeType === 9 ? I.body : I.nodeName === "HTML" ? I.ownerDocument.body : I).removeChild(a.stateNode);
            } catch (n) {
              K(
                a,
                t,
                n
              );
            }
          else
            try {
              I.removeChild(a.stateNode);
            } catch (n) {
              K(
                a,
                t,
                n
              );
            }
        break;
      case 18:
        I !== null && (Ml ? (l = I, Ry(
          l.nodeType === 9 ? l.body : l.nodeName === "HTML" ? l.ownerDocument.body : l,
          a.stateNode
        ), su(l)) : Ry(I, a.stateNode));
        break;
      case 4:
        u = I, e = Ml, I = a.stateNode.containerInfo, Ml = !0, Nt(
          l,
          t,
          a
        ), I = u, Ml = e;
        break;
      case 0:
      case 11:
      case 14:
      case 15:
        $t(2, a, t), vl || $t(4, a, t), Nt(
          l,
          t,
          a
        );
        break;
      case 1:
        vl || (yt(a, t), u = a.stateNode, typeof u.componentWillUnmount == "function" && Xv(
          a,
          t,
          u
        )), Nt(
          l,
          t,
          a
        );
        break;
      case 21:
        Nt(
          l,
          t,
          a
        );
        break;
      case 22:
        vl = (u = vl) || a.memoizedState !== null, Nt(
          l,
          t,
          a
        ), vl = u;
        break;
      default:
        Nt(
          l,
          t,
          a
        );
    }
  }
  function Lv(l, t) {
    if (t.memoizedState === null && (l = t.alternate, l !== null && (l = l.memoizedState, l !== null))) {
      l = l.dehydrated;
      try {
        su(l);
      } catch (a) {
        K(t, t.return, a);
      }
    }
  }
  function xv(l, t) {
    if (t.memoizedState === null && (l = t.alternate, l !== null && (l = l.memoizedState, l !== null && (l = l.dehydrated, l !== null))))
      try {
        su(l);
      } catch (a) {
        K(t, t.return, a);
      }
  }
  function Vm(l) {
    switch (l.tag) {
      case 31:
      case 13:
      case 19:
        var t = l.stateNode;
        return t === null && (t = l.stateNode = new jv()), t;
      case 22:
        return l = l.stateNode, t = l._retryCache, t === null && (t = l._retryCache = new jv()), t;
      default:
        throw Error(g(435, l.tag));
    }
  }
  function cn(l, t) {
    var a = Vm(l);
    t.forEach(function(u) {
      if (!a.has(u)) {
        a.add(u);
        var e = $m.bind(null, l, u);
        u.then(e, e);
      }
    });
  }
  function Dl(l, t) {
    var a = t.deletions;
    if (a !== null)
      for (var u = 0; u < a.length; u++) {
        var e = a[u], n = l, f = t, c = f;
        l: for (; c !== null; ) {
          switch (c.tag) {
            case 27:
              if (aa(c.type)) {
                I = c.stateNode, Ml = !1;
                break l;
              }
              break;
            case 5:
              I = c.stateNode, Ml = !1;
              break l;
            case 3:
            case 4:
              I = c.stateNode.containerInfo, Ml = !0;
              break l;
          }
          c = c.return;
        }
        if (I === null) throw Error(g(160));
        Kv(n, f, e), I = null, Ml = !1, n = e.alternate, n !== null && (n.return = null), e.return = null;
      }
    if (t.subtreeFlags & 13886)
      for (t = t.child; t !== null; )
        Jv(t, l), t = t.sibling;
  }
  var et = null;
  function Jv(l, t) {
    var a = l.alternate, u = l.flags;
    switch (l.tag) {
      case 0:
      case 11:
      case 14:
      case 15:
        Dl(t, l), Ul(l), u & 4 && ($t(3, l, l.return), wu(3, l), $t(5, l, l.return));
        break;
      case 1:
        Dl(t, l), Ul(l), u & 512 && (vl || a === null || yt(a, a.return)), u & 64 && Ht && (l = l.updateQueue, l !== null && (u = l.callbacks, u !== null && (a = l.shared.hiddenCallbacks, l.shared.hiddenCallbacks = a === null ? u : a.concat(u))));
        break;
      case 26:
        var e = et;
        if (Dl(t, l), Ul(l), u & 512 && (vl || a === null || yt(a, a.return)), u & 4) {
          var n = a !== null ? a.memoizedState : null;
          if (u = l.memoizedState, a === null)
            if (u === null)
              if (l.stateNode === null) {
                l: {
                  u = l.type, a = l.memoizedProps, e = e.ownerDocument || e;
                  t: switch (u) {
                    case "title":
                      n = e.getElementsByTagName("title")[0], (!n || n[ou] || n[Sl] || n.namespaceURI === "http://www.w3.org/2000/svg" || n.hasAttribute("itemprop")) && (n = e.createElement(u), e.head.insertBefore(
                        n,
                        e.querySelector("head > title")
                      )), zl(n, u, a), n[Sl] = l, dl(n), u = n;
                      break l;
                    case "link":
                      var f = Jy(
                        "link",
                        "href",
                        e
                      ).get(u + (a.href || ""));
                      if (f) {
                        for (var c = 0; c < f.length; c++)
                          if (n = f[c], n.getAttribute("href") === (a.href == null || a.href === "" ? null : a.href) && n.getAttribute("rel") === (a.rel == null ? null : a.rel) && n.getAttribute("title") === (a.title == null ? null : a.title) && n.getAttribute("crossorigin") === (a.crossOrigin == null ? null : a.crossOrigin)) {
                            f.splice(c, 1);
                            break t;
                          }
                      }
                      n = e.createElement(u), zl(n, u, a), e.head.appendChild(n);
                      break;
                    case "meta":
                      if (f = Jy(
                        "meta",
                        "content",
                        e
                      ).get(u + (a.content || ""))) {
                        for (c = 0; c < f.length; c++)
                          if (n = f[c], n.getAttribute("content") === (a.content == null ? null : "" + a.content) && n.getAttribute("name") === (a.name == null ? null : a.name) && n.getAttribute("property") === (a.property == null ? null : a.property) && n.getAttribute("http-equiv") === (a.httpEquiv == null ? null : a.httpEquiv) && n.getAttribute("charset") === (a.charSet == null ? null : a.charSet)) {
                            f.splice(c, 1);
                            break t;
                          }
                      }
                      n = e.createElement(u), zl(n, u, a), e.head.appendChild(n);
                      break;
                    default:
                      throw Error(g(468, u));
                  }
                  n[Sl] = l, dl(n), u = n;
                }
                l.stateNode = u;
              } else
                ry(
                  e,
                  l.type,
                  l.stateNode
                );
            else
              l.stateNode = xy(
                e,
                u,
                l.memoizedProps
              );
          else
            n !== u ? (n === null ? a.stateNode !== null && (a = a.stateNode, a.parentNode.removeChild(a)) : n.count--, u === null ? ry(
              e,
              l.type,
              l.stateNode
            ) : xy(
              e,
              u,
              l.memoizedProps
            )) : u === null && l.stateNode !== null && Sc(
              l,
              l.memoizedProps,
              a.memoizedProps
            );
        }
        break;
      case 27:
        Dl(t, l), Ul(l), u & 512 && (vl || a === null || yt(a, a.return)), a !== null && u & 4 && Sc(
          l,
          l.memoizedProps,
          a.memoizedProps
        );
        break;
      case 5:
        if (Dl(t, l), Ul(l), u & 512 && (vl || a === null || yt(a, a.return)), l.flags & 32) {
          e = l.stateNode;
          try {
            ja(e, "");
          } catch (o) {
            K(l, l.return, o);
          }
        }
        u & 4 && l.stateNode != null && (e = l.memoizedProps, Sc(
          l,
          e,
          a !== null ? a.memoizedProps : e
        )), u & 1024 && (bc = !0);
        break;
      case 6:
        if (Dl(t, l), Ul(l), u & 4) {
          if (l.stateNode === null)
            throw Error(g(162));
          u = l.memoizedProps, a = l.stateNode;
          try {
            a.nodeValue = u;
          } catch (o) {
            K(l, l.return, o);
          }
        }
        break;
      case 3:
        if (Mn = null, e = et, et = on(t.containerInfo), Dl(t, l), et = e, Ul(l), u & 4 && a !== null && a.memoizedState.isDehydrated)
          try {
            su(t.containerInfo);
          } catch (o) {
            K(l, l.return, o);
          }
        bc && (bc = !1, rv(l));
        break;
      case 4:
        u = et, et = on(
          l.stateNode.containerInfo
        ), Dl(t, l), Ul(l), et = u;
        break;
      case 12:
        Dl(t, l), Ul(l);
        break;
      case 31:
        Dl(t, l), Ul(l), u & 4 && (u = l.updateQueue, u !== null && (l.updateQueue = null, cn(l, u)));
        break;
      case 13:
        Dl(t, l), Ul(l), l.child.flags & 8192 && l.memoizedState !== null != (a !== null && a.memoizedState !== null) && (yn = ql()), u & 4 && (u = l.updateQueue, u !== null && (l.updateQueue = null, cn(l, u)));
        break;
      case 22:
        e = l.memoizedState !== null;
        var i = a !== null && a.memoizedState !== null, d = Ht, s = vl;
        if (Ht = d || e, vl = s || i, Dl(t, l), vl = s, Ht = d, Ul(l), u & 8192)
          l: for (t = l.stateNode, t._visibility = e ? t._visibility & -2 : t._visibility | 1, e && (a === null || i || Ht || vl || Ua(l)), a = null, t = l; ; ) {
            if (t.tag === 5 || t.tag === 26) {
              if (a === null) {
                i = a = t;
                try {
                  if (n = i.stateNode, e)
                    f = n.style, typeof f.setProperty == "function" ? f.setProperty("display", "none", "important") : f.display = "none";
                  else {
                    c = i.stateNode;
                    var T = i.memoizedProps.style, h = T != null && T.hasOwnProperty("display") ? T.display : null;
                    c.style.display = h == null || typeof h == "boolean" ? "" : ("" + h).trim();
                  }
                } catch (o) {
                  K(i, i.return, o);
                }
              }
            } else if (t.tag === 6) {
              if (a === null) {
                i = t;
                try {
                  i.stateNode.nodeValue = e ? "" : i.memoizedProps;
                } catch (o) {
                  K(i, i.return, o);
                }
              }
            } else if (t.tag === 18) {
              if (a === null) {
                i = t;
                try {
                  var S = i.stateNode;
                  e ? Xy(S, !0) : Xy(i.stateNode, !1);
                } catch (o) {
                  K(i, i.return, o);
                }
              }
            } else if ((t.tag !== 22 && t.tag !== 23 || t.memoizedState === null || t === l) && t.child !== null) {
              t.child.return = t, t = t.child;
              continue;
            }
            if (t === l) break l;
            for (; t.sibling === null; ) {
              if (t.return === null || t.return === l) break l;
              a === t && (a = null), t = t.return;
            }
            a === t && (a = null), t.sibling.return = t.return, t = t.sibling;
          }
        u & 4 && (u = l.updateQueue, u !== null && (a = u.retryQueue, a !== null && (u.retryQueue = null, cn(l, a))));
        break;
      case 19:
        Dl(t, l), Ul(l), u & 4 && (u = l.updateQueue, u !== null && (l.updateQueue = null, cn(l, u)));
        break;
      case 30:
        break;
      case 21:
        break;
      default:
        Dl(t, l), Ul(l);
    }
  }
  function Ul(l) {
    var t = l.flags;
    if (t & 2) {
      try {
        for (var a, u = l.return; u !== null; ) {
          if (Cv(u)) {
            a = u;
            break;
          }
          u = u.return;
        }
        if (a == null) throw Error(g(160));
        switch (a.tag) {
          case 27:
            var e = a.stateNode, n = gc(l);
            fn(l, n, e);
            break;
          case 5:
            var f = a.stateNode;
            a.flags & 32 && (ja(f, ""), a.flags &= -33);
            var c = gc(l);
            fn(l, c, f);
            break;
          case 3:
          case 4:
            var i = a.stateNode.containerInfo, d = gc(l);
            sc(
              l,
              d,
              i
            );
            break;
          default:
            throw Error(g(161));
        }
      } catch (s) {
        K(l, l.return, s);
      }
      l.flags &= -3;
    }
    t & 4096 && (l.flags &= -4097);
  }
  function rv(l) {
    if (l.subtreeFlags & 1024)
      for (l = l.child; l !== null; ) {
        var t = l;
        rv(t), t.tag === 5 && t.flags & 1024 && t.stateNode.reset(), l = l.sibling;
      }
  }
  function qt(l, t) {
    if (t.subtreeFlags & 8772)
      for (t = t.child; t !== null; )
        Vv(l, t.alternate, t), t = t.sibling;
  }
  function Ua(l) {
    for (l = l.child; l !== null; ) {
      var t = l;
      switch (t.tag) {
        case 0:
        case 11:
        case 14:
        case 15:
          $t(4, t, t.return), Ua(t);
          break;
        case 1:
          yt(t, t.return);
          var a = t.stateNode;
          typeof a.componentWillUnmount == "function" && Xv(
            t,
            t.return,
            a
          ), Ua(t);
          break;
        case 27:
          ee(t.stateNode);
        case 26:
        case 5:
          yt(t, t.return), Ua(t);
          break;
        case 22:
          t.memoizedState === null && Ua(t);
          break;
        case 30:
          Ua(t);
          break;
        default:
          Ua(t);
      }
      l = l.sibling;
    }
  }
  function Bt(l, t, a) {
    for (a = a && (t.subtreeFlags & 8772) !== 0, t = t.child; t !== null; ) {
      var u = t.alternate, e = l, n = t, f = n.flags;
      switch (n.tag) {
        case 0:
        case 11:
        case 15:
          Bt(
            e,
            n,
            a
          ), wu(4, n);
          break;
        case 1:
          if (Bt(
            e,
            n,
            a
          ), u = n, e = u.stateNode, typeof e.componentDidMount == "function")
            try {
              e.componentDidMount();
            } catch (d) {
              K(u, u.return, d);
            }
          if (u = n, e = u.updateQueue, e !== null) {
            var c = u.stateNode;
            try {
              var i = e.shared.hiddenCallbacks;
              if (i !== null)
                for (e.shared.hiddenCallbacks = null, e = 0; e < i.length; e++)
                  O0(i[e], c);
            } catch (d) {
              K(u, u.return, d);
            }
          }
          a && f & 64 && Rv(n), $u(n, n.return);
          break;
        case 27:
          Zv(n);
        case 26:
        case 5:
          Bt(
            e,
            n,
            a
          ), a && u === null && f & 4 && Qv(n), $u(n, n.return);
          break;
        case 12:
          Bt(
            e,
            n,
            a
          );
          break;
        case 31:
          Bt(
            e,
            n,
            a
          ), a && f & 4 && Lv(e, n);
          break;
        case 13:
          Bt(
            e,
            n,
            a
          ), a && f & 4 && xv(e, n);
          break;
        case 22:
          n.memoizedState === null && Bt(
            e,
            n,
            a
          ), $u(n, n.return);
          break;
        case 30:
          break;
        default:
          Bt(
            e,
            n,
            a
          );
      }
      t = t.sibling;
    }
  }
  function zc(l, t) {
    var a = null;
    l !== null && l.memoizedState !== null && l.memoizedState.cachePool !== null && (a = l.memoizedState.cachePool.pool), l = null, t.memoizedState !== null && t.memoizedState.cachePool !== null && (l = t.memoizedState.cachePool.pool), l !== a && (l != null && l.refCount++, a != null && Xu(a));
  }
  function Tc(l, t) {
    l = null, t.alternate !== null && (l = t.alternate.memoizedState.cache), t = t.memoizedState.cache, t !== l && (t.refCount++, l != null && Xu(l));
  }
  function nt(l, t, a, u) {
    if (t.subtreeFlags & 10256)
      for (t = t.child; t !== null; )
        Wv(
          l,
          t,
          a,
          u
        ), t = t.sibling;
  }
  function Wv(l, t, a, u) {
    var e = t.flags;
    switch (t.tag) {
      case 0:
      case 11:
      case 15:
        nt(
          l,
          t,
          a,
          u
        ), e & 2048 && wu(9, t);
        break;
      case 1:
        nt(
          l,
          t,
          a,
          u
        );
        break;
      case 3:
        nt(
          l,
          t,
          a,
          u
        ), e & 2048 && (l = null, t.alternate !== null && (l = t.alternate.memoizedState.cache), t = t.memoizedState.cache, t !== l && (t.refCount++, l != null && Xu(l)));
        break;
      case 12:
        if (e & 2048) {
          nt(
            l,
            t,
            a,
            u
          ), l = t.stateNode;
          try {
            var n = t.memoizedProps, f = n.id, c = n.onPostCommit;
            typeof c == "function" && c(
              f,
              t.alternate === null ? "mount" : "update",
              l.passiveEffectDuration,
              -0
            );
          } catch (i) {
            K(t, t.return, i);
          }
        } else
          nt(
            l,
            t,
            a,
            u
          );
        break;
      case 31:
        nt(
          l,
          t,
          a,
          u
        );
        break;
      case 13:
        nt(
          l,
          t,
          a,
          u
        );
        break;
      case 23:
        break;
      case 22:
        n = t.stateNode, f = t.alternate, t.memoizedState !== null ? n._visibility & 2 ? nt(
          l,
          t,
          a,
          u
        ) : Fu(l, t) : n._visibility & 2 ? nt(
          l,
          t,
          a,
          u
        ) : (n._visibility |= 2, nu(
          l,
          t,
          a,
          u,
          (t.subtreeFlags & 10256) !== 0 || !1
        )), e & 2048 && zc(f, t);
        break;
      case 24:
        nt(
          l,
          t,
          a,
          u
        ), e & 2048 && Tc(t.alternate, t);
        break;
      default:
        nt(
          l,
          t,
          a,
          u
        );
    }
  }
  function nu(l, t, a, u, e) {
    for (e = e && ((t.subtreeFlags & 10256) !== 0 || !1), t = t.child; t !== null; ) {
      var n = l, f = t, c = a, i = u, d = f.flags;
      switch (f.tag) {
        case 0:
        case 11:
        case 15:
          nu(
            n,
            f,
            c,
            i,
            e
          ), wu(8, f);
          break;
        case 23:
          break;
        case 22:
          var s = f.stateNode;
          f.memoizedState !== null ? s._visibility & 2 ? nu(
            n,
            f,
            c,
            i,
            e
          ) : Fu(
            n,
            f
          ) : (s._visibility |= 2, nu(
            n,
            f,
            c,
            i,
            e
          )), e && d & 2048 && zc(
            f.alternate,
            f
          );
          break;
        case 24:
          nu(
            n,
            f,
            c,
            i,
            e
          ), e && d & 2048 && Tc(f.alternate, f);
          break;
        default:
          nu(
            n,
            f,
            c,
            i,
            e
          );
      }
      t = t.sibling;
    }
  }
  function Fu(l, t) {
    if (t.subtreeFlags & 10256)
      for (t = t.child; t !== null; ) {
        var a = l, u = t, e = u.flags;
        switch (u.tag) {
          case 22:
            Fu(a, u), e & 2048 && zc(
              u.alternate,
              u
            );
            break;
          case 24:
            Fu(a, u), e & 2048 && Tc(u.alternate, u);
            break;
          default:
            Fu(a, u);
        }
        t = t.sibling;
      }
  }
  var ku = 8192;
  function fu(l, t, a) {
    if (l.subtreeFlags & ku)
      for (l = l.child; l !== null; )
        wv(
          l,
          t,
          a
        ), l = l.sibling;
  }
  function wv(l, t, a) {
    switch (l.tag) {
      case 26:
        fu(
          l,
          t,
          a
        ), l.flags & ku && l.memoizedState !== null && Ud(
          a,
          et,
          l.memoizedState,
          l.memoizedProps
        );
        break;
      case 5:
        fu(
          l,
          t,
          a
        );
        break;
      case 3:
      case 4:
        var u = et;
        et = on(l.stateNode.containerInfo), fu(
          l,
          t,
          a
        ), et = u;
        break;
      case 22:
        l.memoizedState === null && (u = l.alternate, u !== null && u.memoizedState !== null ? (u = ku, ku = 16777216, fu(
          l,
          t,
          a
        ), ku = u) : fu(
          l,
          t,
          a
        ));
        break;
      default:
        fu(
          l,
          t,
          a
        );
    }
  }
  function $v(l) {
    var t = l.alternate;
    if (t !== null && (l = t.child, l !== null)) {
      t.child = null;
      do
        t = l.sibling, l.sibling = null, l = t;
      while (l !== null);
    }
  }
  function Iu(l) {
    var t = l.deletions;
    if ((l.flags & 16) !== 0) {
      if (t !== null)
        for (var a = 0; a < t.length; a++) {
          var u = t[a];
          hl = u, kv(
            u,
            l
          );
        }
      $v(l);
    }
    if (l.subtreeFlags & 10256)
      for (l = l.child; l !== null; )
        Fv(l), l = l.sibling;
  }
  function Fv(l) {
    switch (l.tag) {
      case 0:
      case 11:
      case 15:
        Iu(l), l.flags & 2048 && $t(9, l, l.return);
        break;
      case 3:
        Iu(l);
        break;
      case 12:
        Iu(l);
        break;
      case 22:
        var t = l.stateNode;
        l.memoizedState !== null && t._visibility & 2 && (l.return === null || l.return.tag !== 13) ? (t._visibility &= -3, vn(l)) : Iu(l);
        break;
      default:
        Iu(l);
    }
  }
  function vn(l) {
    var t = l.deletions;
    if ((l.flags & 16) !== 0) {
      if (t !== null)
        for (var a = 0; a < t.length; a++) {
          var u = t[a];
          hl = u, kv(
            u,
            l
          );
        }
      $v(l);
    }
    for (l = l.child; l !== null; ) {
      switch (t = l, t.tag) {
        case 0:
        case 11:
        case 15:
          $t(8, t, t.return), vn(t);
          break;
        case 22:
          a = t.stateNode, a._visibility & 2 && (a._visibility &= -3, vn(t));
          break;
        default:
          vn(t);
      }
      l = l.sibling;
    }
  }
  function kv(l, t) {
    for (; hl !== null; ) {
      var a = hl;
      switch (a.tag) {
        case 0:
        case 11:
        case 15:
          $t(8, a, t);
          break;
        case 23:
        case 22:
          if (a.memoizedState !== null && a.memoizedState.cachePool !== null) {
            var u = a.memoizedState.cachePool.pool;
            u != null && u.refCount++;
          }
          break;
        case 24:
          Xu(a.memoizedState.cache);
      }
      if (u = a.child, u !== null) u.return = a, hl = u;
      else
        l: for (a = l; hl !== null; ) {
          u = hl;
          var e = u.sibling, n = u.return;
          if (pv(u), u === a) {
            hl = null;
            break l;
          }
          if (e !== null) {
            e.return = n, hl = e;
            break l;
          }
          hl = n;
        }
    }
  }
  var pm = {
    getCacheForType: function(l) {
      var t = sl(fl), a = t.data.get(l);
      return a === void 0 && (a = l(), t.data.set(l, a)), a;
    },
    cacheSignal: function() {
      return sl(fl).controller.signal;
    }
  }, Km = typeof WeakMap == "function" ? WeakMap : Map, j = 0, W = null, Y = null, R = 0, p = 0, Cl = null, Ft = !1, cu = !1, Ac = !1, Yt = 0, tl = 0, kt = 0, _a = 0, Ec = 0, Zl = 0, iu = 0, Pu = null, _l = null, oc = !1, yn = 0, Iv = 0, mn = 1 / 0, dn = null, It = null, yl = 0, Pt = null, vu = null, Gt = 0, Oc = 0, Mc = null, Pv = null, le = 0, Dc = null;
  function jl() {
    return (j & 2) !== 0 && R !== 0 ? R & -R : z.T !== null ? Bc() : gi();
  }
  function ly() {
    if (Zl === 0)
      if ((R & 536870912) === 0 || Q) {
        var l = Te;
        Te <<= 1, (Te & 3932160) === 0 && (Te = 262144), Zl = l;
      } else Zl = 536870912;
    return l = Xl.current, l !== null && (l.flags |= 32), Zl;
  }
  function Hl(l, t, a) {
    (l === W && (p === 2 || p === 9) || l.cancelPendingCommit !== null) && (yu(l, 0), la(
      l,
      R,
      Zl,
      !1
    )), Eu(l, a), ((j & 2) === 0 || l !== W) && (l === W && ((j & 2) === 0 && (_a |= a), tl === 4 && la(
      l,
      R,
      Zl,
      !1
    )), mt(l));
  }
  function ty(l, t, a) {
    if ((j & 6) !== 0) throw Error(g(327));
    var u = !a && (t & 127) === 0 && (t & l.expiredLanes) === 0 || Au(l, t), e = u ? Jm(l, t) : _c(l, t, !0), n = u;
    do {
      if (e === 0) {
        cu && !u && la(l, t, 0, !1);
        break;
      } else {
        if (a = l.current.alternate, n && !Lm(a)) {
          e = _c(l, t, !1), n = !1;
          continue;
        }
        if (e === 2) {
          if (n = t, l.errorRecoveryDisabledLanes & n)
            var f = 0;
          else
            f = l.pendingLanes & -536870913, f = f !== 0 ? f : f & 536870912 ? 536870912 : 0;
          if (f !== 0) {
            t = f;
            l: {
              var c = l;
              e = Pu;
              var i = c.current.memoizedState.isDehydrated;
              if (i && (yu(c, f).flags |= 256), f = _c(
                c,
                f,
                !1
              ), f !== 2) {
                if (Ac && !i) {
                  c.errorRecoveryDisabledLanes |= n, _a |= n, e = 4;
                  break l;
                }
                n = _l, _l = e, n !== null && (_l === null ? _l = n : _l.push.apply(
                  _l,
                  n
                ));
              }
              e = f;
            }
            if (n = !1, e !== 2) continue;
          }
        }
        if (e === 1) {
          yu(l, 0), la(l, t, 0, !0);
          break;
        }
        l: {
          switch (u = l, n = e, n) {
            case 0:
            case 1:
              throw Error(g(345));
            case 4:
              if ((t & 4194048) !== t) break;
            case 6:
              la(
                u,
                t,
                Zl,
                !Ft
              );
              break l;
            case 2:
              _l = null;
              break;
            case 3:
            case 5:
              break;
            default:
              throw Error(g(329));
          }
          if ((t & 62914560) === t && (e = yn + 300 - ql(), 10 < e)) {
            if (la(
              u,
              t,
              Zl,
              !Ft
            ), Ee(u, 0, !0) !== 0) break l;
            Gt = t, u.timeoutHandle = Yy(
              ay.bind(
                null,
                u,
                a,
                _l,
                dn,
                oc,
                t,
                Zl,
                _a,
                iu,
                Ft,
                n,
                "Throttled",
                -0,
                0
              ),
              e
            );
            break l;
          }
          ay(
            u,
            a,
            _l,
            dn,
            oc,
            t,
            Zl,
            _a,
            iu,
            Ft,
            n,
            null,
            -0,
            0
          );
        }
      }
      break;
    } while (!0);
    mt(l);
  }
  function ay(l, t, a, u, e, n, f, c, i, d, s, T, h, S) {
    if (l.timeoutHandle = -1, T = t.subtreeFlags, T & 8192 || (T & 16785408) === 16785408) {
      T = {
        stylesheets: null,
        count: 0,
        imgCount: 0,
        imgBytes: 0,
        suspenseyImages: [],
        waitingForImages: !0,
        waitingForViewTransition: !1,
        unsuspend: zt
      }, wv(
        t,
        n,
        T
      );
      var o = (n & 62914560) === n ? yn - ql() : (n & 4194048) === n ? Iv - ql() : 0;
      if (o = _d(
        T,
        o
      ), o !== null) {
        Gt = n, l.cancelPendingCommit = o(
          yy.bind(
            null,
            l,
            t,
            n,
            a,
            u,
            e,
            f,
            c,
            i,
            s,
            T,
            null,
            h,
            S
          )
        ), la(l, n, f, !d);
        return;
      }
    }
    yy(
      l,
      t,
      n,
      a,
      u,
      e,
      f,
      c,
      i
    );
  }
  function Lm(l) {
    for (var t = l; ; ) {
      var a = t.tag;
      if ((a === 0 || a === 11 || a === 15) && t.flags & 16384 && (a = t.updateQueue, a !== null && (a = a.stores, a !== null)))
        for (var u = 0; u < a.length; u++) {
          var e = a[u], n = e.getSnapshot;
          e = e.value;
          try {
            if (!Gl(n(), e)) return !1;
          } catch {
            return !1;
          }
        }
      if (a = t.child, t.subtreeFlags & 16384 && a !== null)
        a.return = t, t = a;
      else {
        if (t === l) break;
        for (; t.sibling === null; ) {
          if (t.return === null || t.return === l) return !0;
          t = t.return;
        }
        t.sibling.return = t.return, t = t.sibling;
      }
    }
    return !0;
  }
  function la(l, t, a, u) {
    t &= ~Ec, t &= ~_a, l.suspendedLanes |= t, l.pingedLanes &= ~t, u && (l.warmLanes |= t), u = l.expirationTimes;
    for (var e = t; 0 < e; ) {
      var n = 31 - Yl(e), f = 1 << n;
      u[n] = -1, e &= ~f;
    }
    a !== 0 && di(l, a, t);
  }
  function hn() {
    return (j & 6) === 0 ? (te(0), !1) : !0;
  }
  function Uc() {
    if (Y !== null) {
      if (p === 0)
        var l = Y.return;
      else
        l = Y, ot = za = null, Kf(l), lu = null, Cu = 0, l = Y;
      for (; l !== null; )
        Gv(l.alternate, l), l = l.return;
      Y = null;
    }
  }
  function yu(l, t) {
    var a = l.timeoutHandle;
    a !== -1 && (l.timeoutHandle = -1, vd(a)), a = l.cancelPendingCommit, a !== null && (l.cancelPendingCommit = null, a()), Gt = 0, Uc(), W = l, Y = a = At(l.current, null), R = t, p = 0, Cl = null, Ft = !1, cu = Au(l, t), Ac = !1, iu = Zl = Ec = _a = kt = tl = 0, _l = Pu = null, oc = !1, (t & 8) !== 0 && (t |= t & 32);
    var u = l.entangledLanes;
    if (u !== 0)
      for (l = l.entanglements, u &= t; 0 < u; ) {
        var e = 31 - Yl(u), n = 1 << e;
        t |= l[e], u &= ~n;
      }
    return Yt = t, Ge(), a;
  }
  function uy(l, t) {
    q = null, z.H = Ju, t === Pa || t === pe ? (t = T0(), p = 3) : t === qf ? (t = T0(), p = 4) : p = t === ec ? 8 : t !== null && typeof t == "object" && typeof t.then == "function" ? 6 : 1, Cl = t, Y === null && (tl = 1, tn(
      l,
      rl(t, l.current)
    ));
  }
  function ey() {
    var l = Xl.current;
    return l === null ? !0 : (R & 4194048) === R ? Fl === null : (R & 62914560) === R || (R & 536870912) !== 0 ? l === Fl : !1;
  }
  function ny() {
    var l = z.H;
    return z.H = Ju, l === null ? Ju : l;
  }
  function fy() {
    var l = z.A;
    return z.A = pm, l;
  }
  function Sn() {
    tl = 4, Ft || (R & 4194048) !== R && Xl.current !== null || (cu = !0), (kt & 134217727) === 0 && (_a & 134217727) === 0 || W === null || la(
      W,
      R,
      Zl,
      !1
    );
  }
  function _c(l, t, a) {
    var u = j;
    j |= 2;
    var e = ny(), n = fy();
    (W !== l || R !== t) && (dn = null, yu(l, t)), t = !1;
    var f = tl;
    l: do
      try {
        if (p !== 0 && Y !== null) {
          var c = Y, i = Cl;
          switch (p) {
            case 8:
              Uc(), f = 6;
              break l;
            case 3:
            case 2:
            case 9:
            case 6:
              Xl.current === null && (t = !0);
              var d = p;
              if (p = 0, Cl = null, mu(l, c, i, d), a && cu) {
                f = 0;
                break l;
              }
              break;
            default:
              d = p, p = 0, Cl = null, mu(l, c, i, d);
          }
        }
        xm(), f = tl;
        break;
      } catch (s) {
        uy(l, s);
      }
    while (!0);
    return t && l.shellSuspendCounter++, ot = za = null, j = u, z.H = e, z.A = n, Y === null && (W = null, R = 0, Ge()), f;
  }
  function xm() {
    for (; Y !== null; ) cy(Y);
  }
  function Jm(l, t) {
    var a = j;
    j |= 2;
    var u = ny(), e = fy();
    W !== l || R !== t ? (dn = null, mn = ql() + 500, yu(l, t)) : cu = Au(
      l,
      t
    );
    l: do
      try {
        if (p !== 0 && Y !== null) {
          t = Y;
          var n = Cl;
          t: switch (p) {
            case 1:
              p = 0, Cl = null, mu(l, t, n, 1);
              break;
            case 2:
            case 9:
              if (b0(n)) {
                p = 0, Cl = null, iy(t);
                break;
              }
              t = function() {
                p !== 2 && p !== 9 || W !== l || (p = 7), mt(l);
              }, n.then(t, t);
              break l;
            case 3:
              p = 7;
              break l;
            case 4:
              p = 5;
              break l;
            case 7:
              b0(n) ? (p = 0, Cl = null, iy(t)) : (p = 0, Cl = null, mu(l, t, n, 7));
              break;
            case 5:
              var f = null;
              switch (Y.tag) {
                case 26:
                  f = Y.memoizedState;
                case 5:
                case 27:
                  var c = Y;
                  if (f ? Wy(f) : c.stateNode.complete) {
                    p = 0, Cl = null;
                    var i = c.sibling;
                    if (i !== null) Y = i;
                    else {
                      var d = c.return;
                      d !== null ? (Y = d, gn(d)) : Y = null;
                    }
                    break t;
                  }
              }
              p = 0, Cl = null, mu(l, t, n, 5);
              break;
            case 6:
              p = 0, Cl = null, mu(l, t, n, 6);
              break;
            case 8:
              Uc(), tl = 6;
              break l;
            default:
              throw Error(g(462));
          }
        }
        rm();
        break;
      } catch (s) {
        uy(l, s);
      }
    while (!0);
    return ot = za = null, z.H = u, z.A = e, j = a, Y !== null ? 0 : (W = null, R = 0, Ge(), tl);
  }
  function rm() {
    for (; Y !== null && !g1(); )
      cy(Y);
  }
  function cy(l) {
    var t = Bv(l.alternate, l, Yt);
    l.memoizedProps = l.pendingProps, t === null ? gn(l) : Y = t;
  }
  function iy(l) {
    var t = l, a = t.alternate;
    switch (t.tag) {
      case 15:
      case 0:
        t = Dv(
          a,
          t,
          t.pendingProps,
          t.type,
          void 0,
          R
        );
        break;
      case 11:
        t = Dv(
          a,
          t,
          t.pendingProps,
          t.type.render,
          t.ref,
          R
        );
        break;
      case 5:
        Kf(t);
      default:
        Gv(a, t), t = Y = f0(t, Yt), t = Bv(a, t, Yt);
    }
    l.memoizedProps = l.pendingProps, t === null ? gn(l) : Y = t;
  }
  function mu(l, t, a, u) {
    ot = za = null, Kf(t), lu = null, Cu = 0;
    var e = t.return;
    try {
      if (Rm(
        l,
        e,
        t,
        a,
        R
      )) {
        tl = 1, tn(
          l,
          rl(a, l.current)
        ), Y = null;
        return;
      }
    } catch (n) {
      if (e !== null) throw Y = e, n;
      tl = 1, tn(
        l,
        rl(a, l.current)
      ), Y = null;
      return;
    }
    t.flags & 32768 ? (Q || u === 1 ? l = !0 : cu || (R & 536870912) !== 0 ? l = !1 : (Ft = l = !0, (u === 2 || u === 9 || u === 3 || u === 6) && (u = Xl.current, u !== null && u.tag === 13 && (u.flags |= 16384))), vy(t, l)) : gn(t);
  }
  function gn(l) {
    var t = l;
    do {
      if ((t.flags & 32768) !== 0) {
        vy(
          t,
          Ft
        );
        return;
      }
      l = t.return;
      var a = Cm(
        t.alternate,
        t,
        Yt
      );
      if (a !== null) {
        Y = a;
        return;
      }
      if (t = t.sibling, t !== null) {
        Y = t;
        return;
      }
      Y = t = l;
    } while (t !== null);
    tl === 0 && (tl = 5);
  }
  function vy(l, t) {
    do {
      var a = Zm(l.alternate, l);
      if (a !== null) {
        a.flags &= 32767, Y = a;
        return;
      }
      if (a = l.return, a !== null && (a.flags |= 32768, a.subtreeFlags = 0, a.deletions = null), !t && (l = l.sibling, l !== null)) {
        Y = l;
        return;
      }
      Y = l = a;
    } while (l !== null);
    tl = 6, Y = null;
  }
  function yy(l, t, a, u, e, n, f, c, i) {
    l.cancelPendingCommit = null;
    do
      sn();
    while (yl !== 0);
    if ((j & 6) !== 0) throw Error(g(327));
    if (t !== null) {
      if (t === l.current) throw Error(g(177));
      if (n = t.lanes | t.childLanes, n |= gf, D1(
        l,
        a,
        n,
        f,
        c,
        i
      ), l === W && (Y = W = null, R = 0), vu = t, Pt = l, Gt = a, Oc = n, Mc = e, Pv = u, (t.subtreeFlags & 10256) !== 0 || (t.flags & 10256) !== 0 ? (l.callbackNode = null, l.callbackPriority = 0, Fm(be, function() {
        return gy(), null;
      })) : (l.callbackNode = null, l.callbackPriority = 0), u = (t.flags & 13878) !== 0, (t.subtreeFlags & 13878) !== 0 || u) {
        u = z.T, z.T = null, e = E.p, E.p = 2, f = j, j |= 4;
        try {
          jm(l, t, a);
        } finally {
          j = f, E.p = e, z.T = u;
        }
      }
      yl = 1, my(), dy(), hy();
    }
  }
  function my() {
    if (yl === 1) {
      yl = 0;
      var l = Pt, t = vu, a = (t.flags & 13878) !== 0;
      if ((t.subtreeFlags & 13878) !== 0 || a) {
        a = z.T, z.T = null;
        var u = E.p;
        E.p = 2;
        var e = j;
        j |= 4;
        try {
          Jv(t, l);
          var n = jc, f = ki(l.containerInfo), c = n.focusedElem, i = n.selectionRange;
          if (f !== c && c && c.ownerDocument && Fi(
            c.ownerDocument.documentElement,
            c
          )) {
            if (i !== null && yf(c)) {
              var d = i.start, s = i.end;
              if (s === void 0 && (s = d), "selectionStart" in c)
                c.selectionStart = d, c.selectionEnd = Math.min(
                  s,
                  c.value.length
                );
              else {
                var T = c.ownerDocument || document, h = T && T.defaultView || window;
                if (h.getSelection) {
                  var S = h.getSelection(), o = c.textContent.length, _ = Math.min(i.start, o), J = i.end === void 0 ? _ : Math.min(i.end, o);
                  !S.extend && _ > J && (f = J, J = _, _ = f);
                  var y = $i(
                    c,
                    _
                  ), v = $i(
                    c,
                    J
                  );
                  if (y && v && (S.rangeCount !== 1 || S.anchorNode !== y.node || S.anchorOffset !== y.offset || S.focusNode !== v.node || S.focusOffset !== v.offset)) {
                    var m = T.createRange();
                    m.setStart(y.node, y.offset), S.removeAllRanges(), _ > J ? (S.addRange(m), S.extend(v.node, v.offset)) : (m.setEnd(v.node, v.offset), S.addRange(m));
                  }
                }
              }
            }
            for (T = [], S = c; S = S.parentNode; )
              S.nodeType === 1 && T.push({
                element: S,
                left: S.scrollLeft,
                top: S.scrollTop
              });
            for (typeof c.focus == "function" && c.focus(), c = 0; c < T.length; c++) {
              var b = T[c];
              b.element.scrollLeft = b.left, b.element.scrollTop = b.top;
            }
          }
          Hn = !!Zc, jc = Zc = null;
        } finally {
          j = e, E.p = u, z.T = a;
        }
      }
      l.current = t, yl = 2;
    }
  }
  function dy() {
    if (yl === 2) {
      yl = 0;
      var l = Pt, t = vu, a = (t.flags & 8772) !== 0;
      if ((t.subtreeFlags & 8772) !== 0 || a) {
        a = z.T, z.T = null;
        var u = E.p;
        E.p = 2;
        var e = j;
        j |= 4;
        try {
          Vv(l, t.alternate, t);
        } finally {
          j = e, E.p = u, z.T = a;
        }
      }
      yl = 3;
    }
  }
  function hy() {
    if (yl === 4 || yl === 3) {
      yl = 0, s1();
      var l = Pt, t = vu, a = Gt, u = Pv;
      (t.subtreeFlags & 10256) !== 0 || (t.flags & 10256) !== 0 ? yl = 5 : (yl = 0, vu = Pt = null, Sy(l, l.pendingLanes));
      var e = l.pendingLanes;
      if (e === 0 && (It = null), xn(a), t = t.stateNode, Bl && typeof Bl.onCommitFiberRoot == "function")
        try {
          Bl.onCommitFiberRoot(
            Tu,
            t,
            void 0,
            (t.current.flags & 128) === 128
          );
        } catch {
        }
      if (u !== null) {
        t = z.T, e = E.p, E.p = 2, z.T = null;
        try {
          for (var n = l.onRecoverableError, f = 0; f < u.length; f++) {
            var c = u[f];
            n(c.value, {
              componentStack: c.stack
            });
          }
        } finally {
          z.T = t, E.p = e;
        }
      }
      (Gt & 3) !== 0 && sn(), mt(l), e = l.pendingLanes, (a & 261930) !== 0 && (e & 42) !== 0 ? l === Dc ? le++ : (le = 0, Dc = l) : le = 0, te(0);
    }
  }
  function Sy(l, t) {
    (l.pooledCacheLanes &= t) === 0 && (t = l.pooledCache, t != null && (l.pooledCache = null, Xu(t)));
  }
  function sn() {
    return my(), dy(), hy(), gy();
  }
  function gy() {
    if (yl !== 5) return !1;
    var l = Pt, t = Oc;
    Oc = 0;
    var a = xn(Gt), u = z.T, e = E.p;
    try {
      E.p = 32 > a ? 32 : a, z.T = null, a = Mc, Mc = null;
      var n = Pt, f = Gt;
      if (yl = 0, vu = Pt = null, Gt = 0, (j & 6) !== 0) throw Error(g(331));
      var c = j;
      if (j |= 4, Fv(n.current), Wv(
        n,
        n.current,
        f,
        a
      ), j = c, te(0, !1), Bl && typeof Bl.onPostCommitFiberRoot == "function")
        try {
          Bl.onPostCommitFiberRoot(Tu, n);
        } catch {
        }
      return !0;
    } finally {
      E.p = e, z.T = u, Sy(l, t);
    }
  }
  function sy(l, t, a) {
    t = rl(a, t), t = uc(l.stateNode, t, 2), l = rt(l, t, 2), l !== null && (Eu(l, 2), mt(l));
  }
  function K(l, t, a) {
    if (l.tag === 3)
      sy(l, l, a);
    else
      for (; t !== null; ) {
        if (t.tag === 3) {
          sy(
            t,
            l,
            a
          );
          break;
        } else if (t.tag === 1) {
          var u = t.stateNode;
          if (typeof t.type.getDerivedStateFromError == "function" || typeof u.componentDidCatch == "function" && (It === null || !It.has(u))) {
            l = rl(a, l), a = bv(2), u = rt(t, a, 2), u !== null && (zv(
              a,
              u,
              t,
              l
            ), Eu(u, 2), mt(u));
            break;
          }
        }
        t = t.return;
      }
  }
  function Hc(l, t, a) {
    var u = l.pingCache;
    if (u === null) {
      u = l.pingCache = new Km();
      var e = /* @__PURE__ */ new Set();
      u.set(t, e);
    } else
      e = u.get(t), e === void 0 && (e = /* @__PURE__ */ new Set(), u.set(t, e));
    e.has(a) || (Ac = !0, e.add(a), l = Wm.bind(null, l, t, a), t.then(l, l));
  }
  function Wm(l, t, a) {
    var u = l.pingCache;
    u !== null && u.delete(t), l.pingedLanes |= l.suspendedLanes & a, l.warmLanes &= ~a, W === l && (R & a) === a && (tl === 4 || tl === 3 && (R & 62914560) === R && 300 > ql() - yn ? (j & 2) === 0 && yu(l, 0) : Ec |= a, iu === R && (iu = 0)), mt(l);
  }
  function by(l, t) {
    t === 0 && (t = mi()), l = ga(l, t), l !== null && (Eu(l, t), mt(l));
  }
  function wm(l) {
    var t = l.memoizedState, a = 0;
    t !== null && (a = t.retryLane), by(l, a);
  }
  function $m(l, t) {
    var a = 0;
    switch (l.tag) {
      case 31:
      case 13:
        var u = l.stateNode, e = l.memoizedState;
        e !== null && (a = e.retryLane);
        break;
      case 19:
        u = l.stateNode;
        break;
      case 22:
        u = l.stateNode._retryCache;
        break;
      default:
        throw Error(g(314));
    }
    u !== null && u.delete(t), by(l, a);
  }
  function Fm(l, t) {
    return Vn(l, t);
  }
  var bn = null, du = null, Nc = !1, zn = !1, qc = !1, ta = 0;
  function mt(l) {
    l !== du && l.next === null && (du === null ? bn = du = l : du = du.next = l), zn = !0, Nc || (Nc = !0, Im());
  }
  function te(l, t) {
    if (!qc && zn) {
      qc = !0;
      do
        for (var a = !1, u = bn; u !== null; ) {
          if (l !== 0) {
            var e = u.pendingLanes;
            if (e === 0) var n = 0;
            else {
              var f = u.suspendedLanes, c = u.pingedLanes;
              n = (1 << 31 - Yl(42 | l) + 1) - 1, n &= e & ~(f & ~c), n = n & 201326741 ? n & 201326741 | 1 : n ? n | 2 : 0;
            }
            n !== 0 && (a = !0, Ey(u, n));
          } else
            n = R, n = Ee(
              u,
              u === W ? n : 0,
              u.cancelPendingCommit !== null || u.timeoutHandle !== -1
            ), (n & 3) === 0 || Au(u, n) || (a = !0, Ey(u, n));
          u = u.next;
        }
      while (a);
      qc = !1;
    }
  }
  function km() {
    zy();
  }
  function zy() {
    zn = Nc = !1;
    var l = 0;
    ta !== 0 && id() && (l = ta);
    for (var t = ql(), a = null, u = bn; u !== null; ) {
      var e = u.next, n = Ty(u, t);
      n === 0 ? (u.next = null, a === null ? bn = e : a.next = e, e === null && (du = a)) : (a = u, (l !== 0 || (n & 3) !== 0) && (zn = !0)), u = e;
    }
    yl !== 0 && yl !== 5 || te(l), ta !== 0 && (ta = 0);
  }
  function Ty(l, t) {
    for (var a = l.suspendedLanes, u = l.pingedLanes, e = l.expirationTimes, n = l.pendingLanes & -62914561; 0 < n; ) {
      var f = 31 - Yl(n), c = 1 << f, i = e[f];
      i === -1 ? ((c & a) === 0 || (c & u) !== 0) && (e[f] = M1(c, t)) : i <= t && (l.expiredLanes |= c), n &= ~c;
    }
    if (t = W, a = R, a = Ee(
      l,
      l === t ? a : 0,
      l.cancelPendingCommit !== null || l.timeoutHandle !== -1
    ), u = l.callbackNode, a === 0 || l === t && (p === 2 || p === 9) || l.cancelPendingCommit !== null)
      return u !== null && u !== null && pn(u), l.callbackNode = null, l.callbackPriority = 0;
    if ((a & 3) === 0 || Au(l, a)) {
      if (t = a & -a, t === l.callbackPriority) return t;
      switch (u !== null && pn(u), xn(a)) {
        case 2:
        case 8:
          a = vi;
          break;
        case 32:
          a = be;
          break;
        case 268435456:
          a = yi;
          break;
        default:
          a = be;
      }
      return u = Ay.bind(null, l), a = Vn(a, u), l.callbackPriority = t, l.callbackNode = a, t;
    }
    return u !== null && u !== null && pn(u), l.callbackPriority = 2, l.callbackNode = null, 2;
  }
  function Ay(l, t) {
    if (yl !== 0 && yl !== 5)
      return l.callbackNode = null, l.callbackPriority = 0, null;
    var a = l.callbackNode;
    if (sn() && l.callbackNode !== a)
      return null;
    var u = R;
    return u = Ee(
      l,
      l === W ? u : 0,
      l.cancelPendingCommit !== null || l.timeoutHandle !== -1
    ), u === 0 ? null : (ty(l, u, t), Ty(l, ql()), l.callbackNode != null && l.callbackNode === a ? Ay.bind(null, l) : null);
  }
  function Ey(l, t) {
    if (sn()) return null;
    ty(l, t, !0);
  }
  function Im() {
    yd(function() {
      (j & 6) !== 0 ? Vn(
        ii,
        km
      ) : zy();
    });
  }
  function Bc() {
    if (ta === 0) {
      var l = ka;
      l === 0 && (l = ze, ze <<= 1, (ze & 261888) === 0 && (ze = 256)), ta = l;
    }
    return ta;
  }
  function oy(l) {
    return l == null || typeof l == "symbol" || typeof l == "boolean" ? null : typeof l == "function" ? l : De("" + l);
  }
  function Oy(l, t) {
    var a = t.ownerDocument.createElement("input");
    return a.name = t.name, a.value = t.value, l.id && a.setAttribute("form", l.id), t.parentNode.insertBefore(a, t), l = new FormData(l), a.parentNode.removeChild(a), l;
  }
  function Pm(l, t, a, u, e) {
    if (t === "submit" && a && a.stateNode === e) {
      var n = oy(
        (e[ol] || null).action
      ), f = u.submitter;
      f && (t = (t = f[ol] || null) ? oy(t.formAction) : f.getAttribute("formAction"), t !== null && (n = t, f = null));
      var c = new Ne(
        "action",
        "action",
        null,
        u,
        e
      );
      l.push({
        event: c,
        listeners: [
          {
            instance: null,
            listener: function() {
              if (u.defaultPrevented) {
                if (ta !== 0) {
                  var i = f ? Oy(e, f) : new FormData(e);
                  kf(
                    a,
                    {
                      pending: !0,
                      data: i,
                      method: e.method,
                      action: n
                    },
                    null,
                    i
                  );
                }
              } else
                typeof n == "function" && (c.preventDefault(), i = f ? Oy(e, f) : new FormData(e), kf(
                  a,
                  {
                    pending: !0,
                    data: i,
                    method: e.method,
                    action: n
                  },
                  n,
                  i
                ));
            },
            currentTarget: e
          }
        ]
      });
    }
  }
  for (var Yc = 0; Yc < Sf.length; Yc++) {
    var Gc = Sf[Yc], ld = Gc.toLowerCase(), td = Gc[0].toUpperCase() + Gc.slice(1);
    ut(
      ld,
      "on" + td
    );
  }
  ut(l0, "onAnimationEnd"), ut(t0, "onAnimationIteration"), ut(a0, "onAnimationStart"), ut("dblclick", "onDoubleClick"), ut("focusin", "onFocus"), ut("focusout", "onBlur"), ut(bm, "onTransitionRun"), ut(zm, "onTransitionStart"), ut(Tm, "onTransitionCancel"), ut(u0, "onTransitionEnd"), Ca("onMouseEnter", ["mouseout", "mouseover"]), Ca("onMouseLeave", ["mouseout", "mouseover"]), Ca("onPointerEnter", ["pointerout", "pointerover"]), Ca("onPointerLeave", ["pointerout", "pointerover"]), ma(
    "onChange",
    "change click focusin focusout input keydown keyup selectionchange".split(" ")
  ), ma(
    "onSelect",
    "focusout contextmenu dragend focusin keydown keyup mousedown mouseup selectionchange".split(
      " "
    )
  ), ma("onBeforeInput", [
    "compositionend",
    "keypress",
    "textInput",
    "paste"
  ]), ma(
    "onCompositionEnd",
    "compositionend focusout keydown keypress keyup mousedown".split(" ")
  ), ma(
    "onCompositionStart",
    "compositionstart focusout keydown keypress keyup mousedown".split(" ")
  ), ma(
    "onCompositionUpdate",
    "compositionupdate focusout keydown keypress keyup mousedown".split(" ")
  );
  var ae = "abort canplay canplaythrough durationchange emptied encrypted ended error loadeddata loadedmetadata loadstart pause play playing progress ratechange resize seeked seeking stalled suspend timeupdate volumechange waiting".split(
    " "
  ), ad = new Set(
    "beforetoggle cancel close invalid load scroll scrollend toggle".split(" ").concat(ae)
  );
  function My(l, t) {
    t = (t & 4) !== 0;
    for (var a = 0; a < l.length; a++) {
      var u = l[a], e = u.event;
      u = u.listeners;
      l: {
        var n = void 0;
        if (t)
          for (var f = u.length - 1; 0 <= f; f--) {
            var c = u[f], i = c.instance, d = c.currentTarget;
            if (c = c.listener, i !== n && e.isPropagationStopped())
              break l;
            n = c, e.currentTarget = d;
            try {
              n(e);
            } catch (s) {
              Ye(s);
            }
            e.currentTarget = null, n = i;
          }
        else
          for (f = 0; f < u.length; f++) {
            if (c = u[f], i = c.instance, d = c.currentTarget, c = c.listener, i !== n && e.isPropagationStopped())
              break l;
            n = c, e.currentTarget = d;
            try {
              n(e);
            } catch (s) {
              Ye(s);
            }
            e.currentTarget = null, n = i;
          }
      }
    }
  }
  function G(l, t) {
    var a = t[Jn];
    a === void 0 && (a = t[Jn] = /* @__PURE__ */ new Set());
    var u = l + "__bubble";
    a.has(u) || (Dy(t, l, 2, !1), a.add(u));
  }
  function Rc(l, t, a) {
    var u = 0;
    t && (u |= 4), Dy(
      a,
      l,
      u,
      t
    );
  }
  var Tn = "_reactListening" + Math.random().toString(36).slice(2);
  function Xc(l) {
    if (!l[Tn]) {
      l[Tn] = !0, zi.forEach(function(a) {
        a !== "selectionchange" && (ad.has(a) || Rc(a, !1, l), Rc(a, !0, l));
      });
      var t = l.nodeType === 9 ? l : l.ownerDocument;
      t === null || t[Tn] || (t[Tn] = !0, Rc("selectionchange", !1, t));
    }
  }
  function Dy(l, t, a, u) {
    switch (l1(t)) {
      case 2:
        var e = qd;
        break;
      case 8:
        e = Bd;
        break;
      default:
        e = Fc;
    }
    a = e.bind(
      null,
      t,
      a,
      l
    ), e = void 0, !lf || t !== "touchstart" && t !== "touchmove" && t !== "wheel" || (e = !0), u ? e !== void 0 ? l.addEventListener(t, a, {
      capture: !0,
      passive: e
    }) : l.addEventListener(t, a, !0) : e !== void 0 ? l.addEventListener(t, a, {
      passive: e
    }) : l.addEventListener(t, a, !1);
  }
  function Qc(l, t, a, u, e) {
    var n = u;
    if ((t & 1) === 0 && (t & 2) === 0 && u !== null)
      l: for (; ; ) {
        if (u === null) return;
        var f = u.tag;
        if (f === 3 || f === 4) {
          var c = u.stateNode.containerInfo;
          if (c === e) break;
          if (f === 4)
            for (f = u.return; f !== null; ) {
              var i = f.tag;
              if ((i === 3 || i === 4) && f.stateNode.containerInfo === e)
                return;
              f = f.return;
            }
          for (; c !== null; ) {
            if (f = Ra(c), f === null) return;
            if (i = f.tag, i === 5 || i === 6 || i === 26 || i === 27) {
              u = n = f;
              continue l;
            }
            c = c.parentNode;
          }
        }
        u = u.return;
      }
    qi(function() {
      var d = n, s = In(a), T = [];
      l: {
        var h = e0.get(l);
        if (h !== void 0) {
          var S = Ne, o = l;
          switch (l) {
            case "keypress":
              if (_e(a) === 0) break l;
            case "keydown":
            case "keyup":
              S = $1;
              break;
            case "focusin":
              o = "focus", S = ef;
              break;
            case "focusout":
              o = "blur", S = ef;
              break;
            case "beforeblur":
            case "afterblur":
              S = ef;
              break;
            case "click":
              if (a.button === 2) break l;
            case "auxclick":
            case "dblclick":
            case "mousedown":
            case "mousemove":
            case "mouseup":
            case "mouseout":
            case "mouseover":
            case "contextmenu":
              S = Gi;
              break;
            case "drag":
            case "dragend":
            case "dragenter":
            case "dragexit":
            case "dragleave":
            case "dragover":
            case "dragstart":
            case "drop":
              S = C1;
              break;
            case "touchcancel":
            case "touchend":
            case "touchmove":
            case "touchstart":
              S = I1;
              break;
            case l0:
            case t0:
            case a0:
              S = V1;
              break;
            case u0:
              S = lm;
              break;
            case "scroll":
            case "scrollend":
              S = X1;
              break;
            case "wheel":
              S = am;
              break;
            case "copy":
            case "cut":
            case "paste":
              S = K1;
              break;
            case "gotpointercapture":
            case "lostpointercapture":
            case "pointercancel":
            case "pointerdown":
            case "pointermove":
            case "pointerout":
            case "pointerover":
            case "pointerup":
              S = Xi;
              break;
            case "toggle":
            case "beforetoggle":
              S = em;
          }
          var _ = (t & 4) !== 0, J = !_ && (l === "scroll" || l === "scrollend"), y = _ ? h !== null ? h + "Capture" : null : h;
          _ = [];
          for (var v = d, m; v !== null; ) {
            var b = v;
            if (m = b.stateNode, b = b.tag, b !== 5 && b !== 26 && b !== 27 || m === null || y === null || (b = Mu(v, y), b != null && _.push(
              ue(v, b, m)
            )), J) break;
            v = v.return;
          }
          0 < _.length && (h = new S(
            h,
            o,
            null,
            a,
            s
          ), T.push({ event: h, listeners: _ }));
        }
      }
      if ((t & 7) === 0) {
        l: {
          if (h = l === "mouseover" || l === "pointerover", S = l === "mouseout" || l === "pointerout", h && a !== kn && (o = a.relatedTarget || a.fromElement) && (Ra(o) || o[Ga]))
            break l;
          if ((S || h) && (h = s.window === s ? s : (h = s.ownerDocument) ? h.defaultView || h.parentWindow : window, S ? (o = a.relatedTarget || a.toElement, S = d, o = o ? Ra(o) : null, o !== null && (J = ht(o), _ = o.tag, o !== J || _ !== 5 && _ !== 27 && _ !== 6) && (o = null)) : (S = null, o = d), S !== o)) {
            if (_ = Gi, b = "onMouseLeave", y = "onMouseEnter", v = "mouse", (l === "pointerout" || l === "pointerover") && (_ = Xi, b = "onPointerLeave", y = "onPointerEnter", v = "pointer"), J = S == null ? h : Ou(S), m = o == null ? h : Ou(o), h = new _(
              b,
              v + "leave",
              S,
              a,
              s
            ), h.target = J, h.relatedTarget = m, b = null, Ra(s) === d && (_ = new _(
              y,
              v + "enter",
              o,
              a,
              s
            ), _.target = m, _.relatedTarget = J, b = _), J = b, S && o)
              t: {
                for (_ = ud, y = S, v = o, m = 0, b = y; b; b = _(b))
                  m++;
                b = 0;
                for (var M = v; M; M = _(M))
                  b++;
                for (; 0 < m - b; )
                  y = _(y), m--;
                for (; 0 < b - m; )
                  v = _(v), b--;
                for (; m--; ) {
                  if (y === v || v !== null && y === v.alternate) {
                    _ = y;
                    break t;
                  }
                  y = _(y), v = _(v);
                }
                _ = null;
              }
            else _ = null;
            S !== null && Uy(
              T,
              h,
              S,
              _,
              !1
            ), o !== null && J !== null && Uy(
              T,
              J,
              o,
              _,
              !0
            );
          }
        }
        l: {
          if (h = d ? Ou(d) : window, S = h.nodeName && h.nodeName.toLowerCase(), S === "select" || S === "input" && h.type === "file")
            var C = Li;
          else if (pi(h))
            if (xi)
              C = Sm;
            else {
              C = dm;
              var O = mm;
            }
          else
            S = h.nodeName, !S || S.toLowerCase() !== "input" || h.type !== "checkbox" && h.type !== "radio" ? d && Fn(d.elementType) && (C = Li) : C = hm;
          if (C && (C = C(l, d))) {
            Ki(
              T,
              C,
              a,
              s
            );
            break l;
          }
          O && O(l, h, d), l === "focusout" && d && h.type === "number" && d.memoizedProps.value != null && $n(h, "number", h.value);
        }
        switch (O = d ? Ou(d) : window, l) {
          case "focusin":
            (pi(O) || O.contentEditable === "true") && (La = O, mf = d, Yu = null);
            break;
          case "focusout":
            Yu = mf = La = null;
            break;
          case "mousedown":
            df = !0;
            break;
          case "contextmenu":
          case "mouseup":
          case "dragend":
            df = !1, Ii(T, a, s);
            break;
          case "selectionchange":
            if (sm) break;
          case "keydown":
          case "keyup":
            Ii(T, a, s);
        }
        var B;
        if (ff)
          l: {
            switch (l) {
              case "compositionstart":
                var X = "onCompositionStart";
                break l;
              case "compositionend":
                X = "onCompositionEnd";
                break l;
              case "compositionupdate":
                X = "onCompositionUpdate";
                break l;
            }
            X = void 0;
          }
        else
          Ka ? ji(l, a) && (X = "onCompositionEnd") : l === "keydown" && a.keyCode === 229 && (X = "onCompositionStart");
        X && (Qi && a.locale !== "ko" && (Ka || X !== "onCompositionStart" ? X === "onCompositionEnd" && Ka && (B = Bi()) : (jt = s, tf = "value" in jt ? jt.value : jt.textContent, Ka = !0)), O = An(d, X), 0 < O.length && (X = new Ri(
          X,
          l,
          null,
          a,
          s
        ), T.push({ event: X, listeners: O }), B ? X.data = B : (B = Vi(a), B !== null && (X.data = B)))), (B = fm ? cm(l, a) : im(l, a)) && (X = An(d, "onBeforeInput"), 0 < X.length && (O = new Ri(
          "onBeforeInput",
          "beforeinput",
          null,
          a,
          s
        ), T.push({
          event: O,
          listeners: X
        }), O.data = B)), Pm(
          T,
          l,
          d,
          a,
          s
        );
      }
      My(T, t);
    });
  }
  function ue(l, t, a) {
    return {
      instance: l,
      listener: t,
      currentTarget: a
    };
  }
  function An(l, t) {
    for (var a = t + "Capture", u = []; l !== null; ) {
      var e = l, n = e.stateNode;
      if (e = e.tag, e !== 5 && e !== 26 && e !== 27 || n === null || (e = Mu(l, a), e != null && u.unshift(
        ue(l, e, n)
      ), e = Mu(l, t), e != null && u.push(
        ue(l, e, n)
      )), l.tag === 3) return u;
      l = l.return;
    }
    return [];
  }
  function ud(l) {
    if (l === null) return null;
    do
      l = l.return;
    while (l && l.tag !== 5 && l.tag !== 27);
    return l || null;
  }
  function Uy(l, t, a, u, e) {
    for (var n = t._reactName, f = []; a !== null && a !== u; ) {
      var c = a, i = c.alternate, d = c.stateNode;
      if (c = c.tag, i !== null && i === u) break;
      c !== 5 && c !== 26 && c !== 27 || d === null || (i = d, e ? (d = Mu(a, n), d != null && f.unshift(
        ue(a, d, i)
      )) : e || (d = Mu(a, n), d != null && f.push(
        ue(a, d, i)
      ))), a = a.return;
    }
    f.length !== 0 && l.push({ event: t, listeners: f });
  }
  var ed = /\r\n?/g, nd = /\u0000|\uFFFD/g;
  function _y(l) {
    return (typeof l == "string" ? l : "" + l).replace(ed, `
`).replace(nd, "");
  }
  function Hy(l, t) {
    return t = _y(t), _y(l) === t;
  }
  function x(l, t, a, u, e, n) {
    switch (a) {
      case "children":
        typeof u == "string" ? t === "body" || t === "textarea" && u === "" || ja(l, u) : (typeof u == "number" || typeof u == "bigint") && t !== "body" && ja(l, "" + u);
        break;
      case "className":
        Oe(l, "class", u);
        break;
      case "tabIndex":
        Oe(l, "tabindex", u);
        break;
      case "dir":
      case "role":
      case "viewBox":
      case "width":
      case "height":
        Oe(l, a, u);
        break;
      case "style":
        Hi(l, u, n);
        break;
      case "data":
        if (t !== "object") {
          Oe(l, "data", u);
          break;
        }
      case "src":
      case "href":
        if (u === "" && (t !== "a" || a !== "href")) {
          l.removeAttribute(a);
          break;
        }
        if (u == null || typeof u == "function" || typeof u == "symbol" || typeof u == "boolean") {
          l.removeAttribute(a);
          break;
        }
        u = De("" + u), l.setAttribute(a, u);
        break;
      case "action":
      case "formAction":
        if (typeof u == "function") {
          l.setAttribute(
            a,
            "javascript:throw new Error('A React form was unexpectedly submitted. If you called form.submit() manually, consider using form.requestSubmit() instead. If you\\'re trying to use event.stopPropagation() in a submit event handler, consider also calling event.preventDefault().')"
          );
          break;
        } else
          typeof n == "function" && (a === "formAction" ? (t !== "input" && x(l, t, "name", e.name, e, null), x(
            l,
            t,
            "formEncType",
            e.formEncType,
            e,
            null
          ), x(
            l,
            t,
            "formMethod",
            e.formMethod,
            e,
            null
          ), x(
            l,
            t,
            "formTarget",
            e.formTarget,
            e,
            null
          )) : (x(l, t, "encType", e.encType, e, null), x(l, t, "method", e.method, e, null), x(l, t, "target", e.target, e, null)));
        if (u == null || typeof u == "symbol" || typeof u == "boolean") {
          l.removeAttribute(a);
          break;
        }
        u = De("" + u), l.setAttribute(a, u);
        break;
      case "onClick":
        u != null && (l.onclick = zt);
        break;
      case "onScroll":
        u != null && G("scroll", l);
        break;
      case "onScrollEnd":
        u != null && G("scrollend", l);
        break;
      case "dangerouslySetInnerHTML":
        if (u != null) {
          if (typeof u != "object" || !("__html" in u))
            throw Error(g(61));
          if (a = u.__html, a != null) {
            if (e.children != null) throw Error(g(60));
            l.innerHTML = a;
          }
        }
        break;
      case "multiple":
        l.multiple = u && typeof u != "function" && typeof u != "symbol";
        break;
      case "muted":
        l.muted = u && typeof u != "function" && typeof u != "symbol";
        break;
      case "suppressContentEditableWarning":
      case "suppressHydrationWarning":
      case "defaultValue":
      case "defaultChecked":
      case "innerHTML":
      case "ref":
        break;
      case "autoFocus":
        break;
      case "xlinkHref":
        if (u == null || typeof u == "function" || typeof u == "boolean" || typeof u == "symbol") {
          l.removeAttribute("xlink:href");
          break;
        }
        a = De("" + u), l.setAttributeNS(
          "http://www.w3.org/1999/xlink",
          "xlink:href",
          a
        );
        break;
      case "contentEditable":
      case "spellCheck":
      case "draggable":
      case "value":
      case "autoReverse":
      case "externalResourcesRequired":
      case "focusable":
      case "preserveAlpha":
        u != null && typeof u != "function" && typeof u != "symbol" ? l.setAttribute(a, "" + u) : l.removeAttribute(a);
        break;
      case "inert":
      case "allowFullScreen":
      case "async":
      case "autoPlay":
      case "controls":
      case "default":
      case "defer":
      case "disabled":
      case "disablePictureInPicture":
      case "disableRemotePlayback":
      case "formNoValidate":
      case "hidden":
      case "loop":
      case "noModule":
      case "noValidate":
      case "open":
      case "playsInline":
      case "readOnly":
      case "required":
      case "reversed":
      case "scoped":
      case "seamless":
      case "itemScope":
        u && typeof u != "function" && typeof u != "symbol" ? l.setAttribute(a, "") : l.removeAttribute(a);
        break;
      case "capture":
      case "download":
        u === !0 ? l.setAttribute(a, "") : u !== !1 && u != null && typeof u != "function" && typeof u != "symbol" ? l.setAttribute(a, u) : l.removeAttribute(a);
        break;
      case "cols":
      case "rows":
      case "size":
      case "span":
        u != null && typeof u != "function" && typeof u != "symbol" && !isNaN(u) && 1 <= u ? l.setAttribute(a, u) : l.removeAttribute(a);
        break;
      case "rowSpan":
      case "start":
        u == null || typeof u == "function" || typeof u == "symbol" || isNaN(u) ? l.removeAttribute(a) : l.setAttribute(a, u);
        break;
      case "popover":
        G("beforetoggle", l), G("toggle", l), oe(l, "popover", u);
        break;
      case "xlinkActuate":
        bt(
          l,
          "http://www.w3.org/1999/xlink",
          "xlink:actuate",
          u
        );
        break;
      case "xlinkArcrole":
        bt(
          l,
          "http://www.w3.org/1999/xlink",
          "xlink:arcrole",
          u
        );
        break;
      case "xlinkRole":
        bt(
          l,
          "http://www.w3.org/1999/xlink",
          "xlink:role",
          u
        );
        break;
      case "xlinkShow":
        bt(
          l,
          "http://www.w3.org/1999/xlink",
          "xlink:show",
          u
        );
        break;
      case "xlinkTitle":
        bt(
          l,
          "http://www.w3.org/1999/xlink",
          "xlink:title",
          u
        );
        break;
      case "xlinkType":
        bt(
          l,
          "http://www.w3.org/1999/xlink",
          "xlink:type",
          u
        );
        break;
      case "xmlBase":
        bt(
          l,
          "http://www.w3.org/XML/1998/namespace",
          "xml:base",
          u
        );
        break;
      case "xmlLang":
        bt(
          l,
          "http://www.w3.org/XML/1998/namespace",
          "xml:lang",
          u
        );
        break;
      case "xmlSpace":
        bt(
          l,
          "http://www.w3.org/XML/1998/namespace",
          "xml:space",
          u
        );
        break;
      case "is":
        oe(l, "is", u);
        break;
      case "innerText":
      case "textContent":
        break;
      default:
        (!(2 < a.length) || a[0] !== "o" && a[0] !== "O" || a[1] !== "n" && a[1] !== "N") && (a = G1.get(a) || a, oe(l, a, u));
    }
  }
  function Cc(l, t, a, u, e, n) {
    switch (a) {
      case "style":
        Hi(l, u, n);
        break;
      case "dangerouslySetInnerHTML":
        if (u != null) {
          if (typeof u != "object" || !("__html" in u))
            throw Error(g(61));
          if (a = u.__html, a != null) {
            if (e.children != null) throw Error(g(60));
            l.innerHTML = a;
          }
        }
        break;
      case "children":
        typeof u == "string" ? ja(l, u) : (typeof u == "number" || typeof u == "bigint") && ja(l, "" + u);
        break;
      case "onScroll":
        u != null && G("scroll", l);
        break;
      case "onScrollEnd":
        u != null && G("scrollend", l);
        break;
      case "onClick":
        u != null && (l.onclick = zt);
        break;
      case "suppressContentEditableWarning":
      case "suppressHydrationWarning":
      case "innerHTML":
      case "ref":
        break;
      case "innerText":
      case "textContent":
        break;
      default:
        if (!Ti.hasOwnProperty(a))
          l: {
            if (a[0] === "o" && a[1] === "n" && (e = a.endsWith("Capture"), t = a.slice(2, e ? a.length - 7 : void 0), n = l[ol] || null, n = n != null ? n[a] : null, typeof n == "function" && l.removeEventListener(t, n, e), typeof u == "function")) {
              typeof n != "function" && n !== null && (a in l ? l[a] = null : l.hasAttribute(a) && l.removeAttribute(a)), l.addEventListener(t, u, e);
              break l;
            }
            a in l ? l[a] = u : u === !0 ? l.setAttribute(a, "") : oe(l, a, u);
          }
    }
  }
  function zl(l, t, a) {
    switch (t) {
      case "div":
      case "span":
      case "svg":
      case "path":
      case "a":
      case "g":
      case "p":
      case "li":
        break;
      case "img":
        G("error", l), G("load", l);
        var u = !1, e = !1, n;
        for (n in a)
          if (a.hasOwnProperty(n)) {
            var f = a[n];
            if (f != null)
              switch (n) {
                case "src":
                  u = !0;
                  break;
                case "srcSet":
                  e = !0;
                  break;
                case "children":
                case "dangerouslySetInnerHTML":
                  throw Error(g(137, t));
                default:
                  x(l, t, n, f, a, null);
              }
          }
        e && x(l, t, "srcSet", a.srcSet, a, null), u && x(l, t, "src", a.src, a, null);
        return;
      case "input":
        G("invalid", l);
        var c = n = f = e = null, i = null, d = null;
        for (u in a)
          if (a.hasOwnProperty(u)) {
            var s = a[u];
            if (s != null)
              switch (u) {
                case "name":
                  e = s;
                  break;
                case "type":
                  f = s;
                  break;
                case "checked":
                  i = s;
                  break;
                case "defaultChecked":
                  d = s;
                  break;
                case "value":
                  n = s;
                  break;
                case "defaultValue":
                  c = s;
                  break;
                case "children":
                case "dangerouslySetInnerHTML":
                  if (s != null)
                    throw Error(g(137, t));
                  break;
                default:
                  x(l, t, u, s, a, null);
              }
          }
        Mi(
          l,
          n,
          c,
          i,
          d,
          f,
          e,
          !1
        );
        return;
      case "select":
        G("invalid", l), u = f = n = null;
        for (e in a)
          if (a.hasOwnProperty(e) && (c = a[e], c != null))
            switch (e) {
              case "value":
                n = c;
                break;
              case "defaultValue":
                f = c;
                break;
              case "multiple":
                u = c;
              default:
                x(l, t, e, c, a, null);
            }
        t = n, a = f, l.multiple = !!u, t != null ? Za(l, !!u, t, !1) : a != null && Za(l, !!u, a, !0);
        return;
      case "textarea":
        G("invalid", l), n = e = u = null;
        for (f in a)
          if (a.hasOwnProperty(f) && (c = a[f], c != null))
            switch (f) {
              case "value":
                u = c;
                break;
              case "defaultValue":
                e = c;
                break;
              case "children":
                n = c;
                break;
              case "dangerouslySetInnerHTML":
                if (c != null) throw Error(g(91));
                break;
              default:
                x(l, t, f, c, a, null);
            }
        Ui(l, u, e, n);
        return;
      case "option":
        for (i in a)
          a.hasOwnProperty(i) && (u = a[i], u != null) && (i === "selected" ? l.selected = u && typeof u != "function" && typeof u != "symbol" : x(l, t, i, u, a, null));
        return;
      case "dialog":
        G("beforetoggle", l), G("toggle", l), G("cancel", l), G("close", l);
        break;
      case "iframe":
      case "object":
        G("load", l);
        break;
      case "video":
      case "audio":
        for (u = 0; u < ae.length; u++)
          G(ae[u], l);
        break;
      case "image":
        G("error", l), G("load", l);
        break;
      case "details":
        G("toggle", l);
        break;
      case "embed":
      case "source":
      case "link":
        G("error", l), G("load", l);
      case "area":
      case "base":
      case "br":
      case "col":
      case "hr":
      case "keygen":
      case "meta":
      case "param":
      case "track":
      case "wbr":
      case "menuitem":
        for (d in a)
          if (a.hasOwnProperty(d) && (u = a[d], u != null))
            switch (d) {
              case "children":
              case "dangerouslySetInnerHTML":
                throw Error(g(137, t));
              default:
                x(l, t, d, u, a, null);
            }
        return;
      default:
        if (Fn(t)) {
          for (s in a)
            a.hasOwnProperty(s) && (u = a[s], u !== void 0 && Cc(
              l,
              t,
              s,
              u,
              a,
              void 0
            ));
          return;
        }
    }
    for (c in a)
      a.hasOwnProperty(c) && (u = a[c], u != null && x(l, t, c, u, a, null));
  }
  function fd(l, t, a, u) {
    switch (t) {
      case "div":
      case "span":
      case "svg":
      case "path":
      case "a":
      case "g":
      case "p":
      case "li":
        break;
      case "input":
        var e = null, n = null, f = null, c = null, i = null, d = null, s = null;
        for (S in a) {
          var T = a[S];
          if (a.hasOwnProperty(S) && T != null)
            switch (S) {
              case "checked":
                break;
              case "value":
                break;
              case "defaultValue":
                i = T;
              default:
                u.hasOwnProperty(S) || x(l, t, S, null, u, T);
            }
        }
        for (var h in u) {
          var S = u[h];
          if (T = a[h], u.hasOwnProperty(h) && (S != null || T != null))
            switch (h) {
              case "type":
                n = S;
                break;
              case "name":
                e = S;
                break;
              case "checked":
                d = S;
                break;
              case "defaultChecked":
                s = S;
                break;
              case "value":
                f = S;
                break;
              case "defaultValue":
                c = S;
                break;
              case "children":
              case "dangerouslySetInnerHTML":
                if (S != null)
                  throw Error(g(137, t));
                break;
              default:
                S !== T && x(
                  l,
                  t,
                  h,
                  S,
                  u,
                  T
                );
            }
        }
        wn(
          l,
          f,
          c,
          i,
          d,
          s,
          n,
          e
        );
        return;
      case "select":
        S = f = c = h = null;
        for (n in a)
          if (i = a[n], a.hasOwnProperty(n) && i != null)
            switch (n) {
              case "value":
                break;
              case "multiple":
                S = i;
              default:
                u.hasOwnProperty(n) || x(
                  l,
                  t,
                  n,
                  null,
                  u,
                  i
                );
            }
        for (e in u)
          if (n = u[e], i = a[e], u.hasOwnProperty(e) && (n != null || i != null))
            switch (e) {
              case "value":
                h = n;
                break;
              case "defaultValue":
                c = n;
                break;
              case "multiple":
                f = n;
              default:
                n !== i && x(
                  l,
                  t,
                  e,
                  n,
                  u,
                  i
                );
            }
        t = c, a = f, u = S, h != null ? Za(l, !!a, h, !1) : !!u != !!a && (t != null ? Za(l, !!a, t, !0) : Za(l, !!a, a ? [] : "", !1));
        return;
      case "textarea":
        S = h = null;
        for (c in a)
          if (e = a[c], a.hasOwnProperty(c) && e != null && !u.hasOwnProperty(c))
            switch (c) {
              case "value":
                break;
              case "children":
                break;
              default:
                x(l, t, c, null, u, e);
            }
        for (f in u)
          if (e = u[f], n = a[f], u.hasOwnProperty(f) && (e != null || n != null))
            switch (f) {
              case "value":
                h = e;
                break;
              case "defaultValue":
                S = e;
                break;
              case "children":
                break;
              case "dangerouslySetInnerHTML":
                if (e != null) throw Error(g(91));
                break;
              default:
                e !== n && x(l, t, f, e, u, n);
            }
        Di(l, h, S);
        return;
      case "option":
        for (var o in a)
          h = a[o], a.hasOwnProperty(o) && h != null && !u.hasOwnProperty(o) && (o === "selected" ? l.selected = !1 : x(
            l,
            t,
            o,
            null,
            u,
            h
          ));
        for (i in u)
          h = u[i], S = a[i], u.hasOwnProperty(i) && h !== S && (h != null || S != null) && (i === "selected" ? l.selected = h && typeof h != "function" && typeof h != "symbol" : x(
            l,
            t,
            i,
            h,
            u,
            S
          ));
        return;
      case "img":
      case "link":
      case "area":
      case "base":
      case "br":
      case "col":
      case "embed":
      case "hr":
      case "keygen":
      case "meta":
      case "param":
      case "source":
      case "track":
      case "wbr":
      case "menuitem":
        for (var _ in a)
          h = a[_], a.hasOwnProperty(_) && h != null && !u.hasOwnProperty(_) && x(l, t, _, null, u, h);
        for (d in u)
          if (h = u[d], S = a[d], u.hasOwnProperty(d) && h !== S && (h != null || S != null))
            switch (d) {
              case "children":
              case "dangerouslySetInnerHTML":
                if (h != null)
                  throw Error(g(137, t));
                break;
              default:
                x(
                  l,
                  t,
                  d,
                  h,
                  u,
                  S
                );
            }
        return;
      default:
        if (Fn(t)) {
          for (var J in a)
            h = a[J], a.hasOwnProperty(J) && h !== void 0 && !u.hasOwnProperty(J) && Cc(
              l,
              t,
              J,
              void 0,
              u,
              h
            );
          for (s in u)
            h = u[s], S = a[s], !u.hasOwnProperty(s) || h === S || h === void 0 && S === void 0 || Cc(
              l,
              t,
              s,
              h,
              u,
              S
            );
          return;
        }
    }
    for (var y in a)
      h = a[y], a.hasOwnProperty(y) && h != null && !u.hasOwnProperty(y) && x(l, t, y, null, u, h);
    for (T in u)
      h = u[T], S = a[T], !u.hasOwnProperty(T) || h === S || h == null && S == null || x(l, t, T, h, u, S);
  }
  function Ny(l) {
    switch (l) {
      case "css":
      case "script":
      case "font":
      case "img":
      case "image":
      case "input":
      case "link":
        return !0;
      default:
        return !1;
    }
  }
  function cd() {
    if (typeof performance.getEntriesByType == "function") {
      for (var l = 0, t = 0, a = performance.getEntriesByType("resource"), u = 0; u < a.length; u++) {
        var e = a[u], n = e.transferSize, f = e.initiatorType, c = e.duration;
        if (n && c && Ny(f)) {
          for (f = 0, c = e.responseEnd, u += 1; u < a.length; u++) {
            var i = a[u], d = i.startTime;
            if (d > c) break;
            var s = i.transferSize, T = i.initiatorType;
            s && Ny(T) && (i = i.responseEnd, f += s * (i < c ? 1 : (c - d) / (i - d)));
          }
          if (--u, t += 8 * (n + f) / (e.duration / 1e3), l++, 10 < l) break;
        }
      }
      if (0 < l) return t / l / 1e6;
    }
    return navigator.connection && (l = navigator.connection.downlink, typeof l == "number") ? l : 5;
  }
  var Zc = null, jc = null;
  function En(l) {
    return l.nodeType === 9 ? l : l.ownerDocument;
  }
  function qy(l) {
    switch (l) {
      case "http://www.w3.org/2000/svg":
        return 1;
      case "http://www.w3.org/1998/Math/MathML":
        return 2;
      default:
        return 0;
    }
  }
  function By(l, t) {
    if (l === 0)
      switch (t) {
        case "svg":
          return 1;
        case "math":
          return 2;
        default:
          return 0;
      }
    return l === 1 && t === "foreignObject" ? 0 : l;
  }
  function Vc(l, t) {
    return l === "textarea" || l === "noscript" || typeof t.children == "string" || typeof t.children == "number" || typeof t.children == "bigint" || typeof t.dangerouslySetInnerHTML == "object" && t.dangerouslySetInnerHTML !== null && t.dangerouslySetInnerHTML.__html != null;
  }
  var pc = null;
  function id() {
    var l = window.event;
    return l && l.type === "popstate" ? l === pc ? !1 : (pc = l, !0) : (pc = null, !1);
  }
  var Yy = typeof setTimeout == "function" ? setTimeout : void 0, vd = typeof clearTimeout == "function" ? clearTimeout : void 0, Gy = typeof Promise == "function" ? Promise : void 0, yd = typeof queueMicrotask == "function" ? queueMicrotask : typeof Gy < "u" ? function(l) {
    return Gy.resolve(null).then(l).catch(md);
  } : Yy;
  function md(l) {
    setTimeout(function() {
      throw l;
    });
  }
  function aa(l) {
    return l === "head";
  }
  function Ry(l, t) {
    var a = t, u = 0;
    do {
      var e = a.nextSibling;
      if (l.removeChild(a), e && e.nodeType === 8)
        if (a = e.data, a === "/$" || a === "/&") {
          if (u === 0) {
            l.removeChild(e), su(t);
            return;
          }
          u--;
        } else if (a === "$" || a === "$?" || a === "$~" || a === "$!" || a === "&")
          u++;
        else if (a === "html")
          ee(l.ownerDocument.documentElement);
        else if (a === "head") {
          a = l.ownerDocument.head, ee(a);
          for (var n = a.firstChild; n; ) {
            var f = n.nextSibling, c = n.nodeName;
            n[ou] || c === "SCRIPT" || c === "STYLE" || c === "LINK" && n.rel.toLowerCase() === "stylesheet" || a.removeChild(n), n = f;
          }
        } else
          a === "body" && ee(l.ownerDocument.body);
      a = e;
    } while (a);
    su(t);
  }
  function Xy(l, t) {
    var a = l;
    l = 0;
    do {
      var u = a.nextSibling;
      if (a.nodeType === 1 ? t ? (a._stashedDisplay = a.style.display, a.style.display = "none") : (a.style.display = a._stashedDisplay || "", a.getAttribute("style") === "" && a.removeAttribute("style")) : a.nodeType === 3 && (t ? (a._stashedText = a.nodeValue, a.nodeValue = "") : a.nodeValue = a._stashedText || ""), u && u.nodeType === 8)
        if (a = u.data, a === "/$") {
          if (l === 0) break;
          l--;
        } else
          a !== "$" && a !== "$?" && a !== "$~" && a !== "$!" || l++;
      a = u;
    } while (a);
  }
  function Kc(l) {
    var t = l.firstChild;
    for (t && t.nodeType === 10 && (t = t.nextSibling); t; ) {
      var a = t;
      switch (t = t.nextSibling, a.nodeName) {
        case "HTML":
        case "HEAD":
        case "BODY":
          Kc(a), rn(a);
          continue;
        case "SCRIPT":
        case "STYLE":
          continue;
        case "LINK":
          if (a.rel.toLowerCase() === "stylesheet") continue;
      }
      l.removeChild(a);
    }
  }
  function dd(l, t, a, u) {
    for (; l.nodeType === 1; ) {
      var e = a;
      if (l.nodeName.toLowerCase() !== t.toLowerCase()) {
        if (!u && (l.nodeName !== "INPUT" || l.type !== "hidden"))
          break;
      } else if (u) {
        if (!l[ou])
          switch (t) {
            case "meta":
              if (!l.hasAttribute("itemprop")) break;
              return l;
            case "link":
              if (n = l.getAttribute("rel"), n === "stylesheet" && l.hasAttribute("data-precedence"))
                break;
              if (n !== e.rel || l.getAttribute("href") !== (e.href == null || e.href === "" ? null : e.href) || l.getAttribute("crossorigin") !== (e.crossOrigin == null ? null : e.crossOrigin) || l.getAttribute("title") !== (e.title == null ? null : e.title))
                break;
              return l;
            case "style":
              if (l.hasAttribute("data-precedence")) break;
              return l;
            case "script":
              if (n = l.getAttribute("src"), (n !== (e.src == null ? null : e.src) || l.getAttribute("type") !== (e.type == null ? null : e.type) || l.getAttribute("crossorigin") !== (e.crossOrigin == null ? null : e.crossOrigin)) && n && l.hasAttribute("async") && !l.hasAttribute("itemprop"))
                break;
              return l;
            default:
              return l;
          }
      } else if (t === "input" && l.type === "hidden") {
        var n = e.name == null ? null : "" + e.name;
        if (e.type === "hidden" && l.getAttribute("name") === n)
          return l;
      } else return l;
      if (l = kl(l.nextSibling), l === null) break;
    }
    return null;
  }
  function hd(l, t, a) {
    if (t === "") return null;
    for (; l.nodeType !== 3; )
      if ((l.nodeType !== 1 || l.nodeName !== "INPUT" || l.type !== "hidden") && !a || (l = kl(l.nextSibling), l === null)) return null;
    return l;
  }
  function Qy(l, t) {
    for (; l.nodeType !== 8; )
      if ((l.nodeType !== 1 || l.nodeName !== "INPUT" || l.type !== "hidden") && !t || (l = kl(l.nextSibling), l === null)) return null;
    return l;
  }
  function Lc(l) {
    return l.data === "$?" || l.data === "$~";
  }
  function xc(l) {
    return l.data === "$!" || l.data === "$?" && l.ownerDocument.readyState !== "loading";
  }
  function Sd(l, t) {
    var a = l.ownerDocument;
    if (l.data === "$~") l._reactRetry = t;
    else if (l.data !== "$?" || a.readyState !== "loading")
      t();
    else {
      var u = function() {
        t(), a.removeEventListener("DOMContentLoaded", u);
      };
      a.addEventListener("DOMContentLoaded", u), l._reactRetry = u;
    }
  }
  function kl(l) {
    for (; l != null; l = l.nextSibling) {
      var t = l.nodeType;
      if (t === 1 || t === 3) break;
      if (t === 8) {
        if (t = l.data, t === "$" || t === "$!" || t === "$?" || t === "$~" || t === "&" || t === "F!" || t === "F")
          break;
        if (t === "/$" || t === "/&") return null;
      }
    }
    return l;
  }
  var Jc = null;
  function Cy(l) {
    l = l.nextSibling;
    for (var t = 0; l; ) {
      if (l.nodeType === 8) {
        var a = l.data;
        if (a === "/$" || a === "/&") {
          if (t === 0)
            return kl(l.nextSibling);
          t--;
        } else
          a !== "$" && a !== "$!" && a !== "$?" && a !== "$~" && a !== "&" || t++;
      }
      l = l.nextSibling;
    }
    return null;
  }
  function Zy(l) {
    l = l.previousSibling;
    for (var t = 0; l; ) {
      if (l.nodeType === 8) {
        var a = l.data;
        if (a === "$" || a === "$!" || a === "$?" || a === "$~" || a === "&") {
          if (t === 0) return l;
          t--;
        } else a !== "/$" && a !== "/&" || t++;
      }
      l = l.previousSibling;
    }
    return null;
  }
  function jy(l, t, a) {
    switch (t = En(a), l) {
      case "html":
        if (l = t.documentElement, !l) throw Error(g(452));
        return l;
      case "head":
        if (l = t.head, !l) throw Error(g(453));
        return l;
      case "body":
        if (l = t.body, !l) throw Error(g(454));
        return l;
      default:
        throw Error(g(451));
    }
  }
  function ee(l) {
    for (var t = l.attributes; t.length; )
      l.removeAttributeNode(t[0]);
    rn(l);
  }
  var Il = /* @__PURE__ */ new Map(), Vy = /* @__PURE__ */ new Set();
  function on(l) {
    return typeof l.getRootNode == "function" ? l.getRootNode() : l.nodeType === 9 ? l : l.ownerDocument;
  }
  var Rt = E.d;
  E.d = {
    f: gd,
    r: sd,
    D: bd,
    C: zd,
    L: Td,
    m: Ad,
    X: od,
    S: Ed,
    M: Od
  };
  function gd() {
    var l = Rt.f(), t = hn();
    return l || t;
  }
  function sd(l) {
    var t = Xa(l);
    t !== null && t.tag === 5 && t.type === "form" ? uv(t) : Rt.r(l);
  }
  var hu = typeof document > "u" ? null : document;
  function py(l, t, a) {
    var u = hu;
    if (u && typeof t == "string" && t) {
      var e = xl(t);
      e = 'link[rel="' + l + '"][href="' + e + '"]', typeof a == "string" && (e += '[crossorigin="' + a + '"]'), Vy.has(e) || (Vy.add(e), l = { rel: l, crossOrigin: a, href: t }, u.querySelector(e) === null && (t = u.createElement("link"), zl(t, "link", l), dl(t), u.head.appendChild(t)));
    }
  }
  function bd(l) {
    Rt.D(l), py("dns-prefetch", l, null);
  }
  function zd(l, t) {
    Rt.C(l, t), py("preconnect", l, t);
  }
  function Td(l, t, a) {
    Rt.L(l, t, a);
    var u = hu;
    if (u && l && t) {
      var e = 'link[rel="preload"][as="' + xl(t) + '"]';
      t === "image" && a && a.imageSrcSet ? (e += '[imagesrcset="' + xl(
        a.imageSrcSet
      ) + '"]', typeof a.imageSizes == "string" && (e += '[imagesizes="' + xl(
        a.imageSizes
      ) + '"]')) : e += '[href="' + xl(l) + '"]';
      var n = e;
      switch (t) {
        case "style":
          n = Su(l);
          break;
        case "script":
          n = gu(l);
      }
      Il.has(n) || (l = H(
        {
          rel: "preload",
          href: t === "image" && a && a.imageSrcSet ? void 0 : l,
          as: t
        },
        a
      ), Il.set(n, l), u.querySelector(e) !== null || t === "style" && u.querySelector(ne(n)) || t === "script" && u.querySelector(fe(n)) || (t = u.createElement("link"), zl(t, "link", l), dl(t), u.head.appendChild(t)));
    }
  }
  function Ad(l, t) {
    Rt.m(l, t);
    var a = hu;
    if (a && l) {
      var u = t && typeof t.as == "string" ? t.as : "script", e = 'link[rel="modulepreload"][as="' + xl(u) + '"][href="' + xl(l) + '"]', n = e;
      switch (u) {
        case "audioworklet":
        case "paintworklet":
        case "serviceworker":
        case "sharedworker":
        case "worker":
        case "script":
          n = gu(l);
      }
      if (!Il.has(n) && (l = H({ rel: "modulepreload", href: l }, t), Il.set(n, l), a.querySelector(e) === null)) {
        switch (u) {
          case "audioworklet":
          case "paintworklet":
          case "serviceworker":
          case "sharedworker":
          case "worker":
          case "script":
            if (a.querySelector(fe(n)))
              return;
        }
        u = a.createElement("link"), zl(u, "link", l), dl(u), a.head.appendChild(u);
      }
    }
  }
  function Ed(l, t, a) {
    Rt.S(l, t, a);
    var u = hu;
    if (u && l) {
      var e = Qa(u).hoistableStyles, n = Su(l);
      t = t || "default";
      var f = e.get(n);
      if (!f) {
        var c = { loading: 0, preload: null };
        if (f = u.querySelector(
          ne(n)
        ))
          c.loading = 5;
        else {
          l = H(
            { rel: "stylesheet", href: l, "data-precedence": t },
            a
          ), (a = Il.get(n)) && rc(l, a);
          var i = f = u.createElement("link");
          dl(i), zl(i, "link", l), i._p = new Promise(function(d, s) {
            i.onload = d, i.onerror = s;
          }), i.addEventListener("load", function() {
            c.loading |= 1;
          }), i.addEventListener("error", function() {
            c.loading |= 2;
          }), c.loading |= 4, On(f, t, u);
        }
        f = {
          type: "stylesheet",
          instance: f,
          count: 1,
          state: c
        }, e.set(n, f);
      }
    }
  }
  function od(l, t) {
    Rt.X(l, t);
    var a = hu;
    if (a && l) {
      var u = Qa(a).hoistableScripts, e = gu(l), n = u.get(e);
      n || (n = a.querySelector(fe(e)), n || (l = H({ src: l, async: !0 }, t), (t = Il.get(e)) && Wc(l, t), n = a.createElement("script"), dl(n), zl(n, "link", l), a.head.appendChild(n)), n = {
        type: "script",
        instance: n,
        count: 1,
        state: null
      }, u.set(e, n));
    }
  }
  function Od(l, t) {
    Rt.M(l, t);
    var a = hu;
    if (a && l) {
      var u = Qa(a).hoistableScripts, e = gu(l), n = u.get(e);
      n || (n = a.querySelector(fe(e)), n || (l = H({ src: l, async: !0, type: "module" }, t), (t = Il.get(e)) && Wc(l, t), n = a.createElement("script"), dl(n), zl(n, "link", l), a.head.appendChild(n)), n = {
        type: "script",
        instance: n,
        count: 1,
        state: null
      }, u.set(e, n));
    }
  }
  function Ky(l, t, a, u) {
    var e = (e = Qt.current) ? on(e) : null;
    if (!e) throw Error(g(446));
    switch (l) {
      case "meta":
      case "title":
        return null;
      case "style":
        return typeof a.precedence == "string" && typeof a.href == "string" ? (t = Su(a.href), a = Qa(
          e
        ).hoistableStyles, u = a.get(t), u || (u = {
          type: "style",
          instance: null,
          count: 0,
          state: null
        }, a.set(t, u)), u) : { type: "void", instance: null, count: 0, state: null };
      case "link":
        if (a.rel === "stylesheet" && typeof a.href == "string" && typeof a.precedence == "string") {
          l = Su(a.href);
          var n = Qa(
            e
          ).hoistableStyles, f = n.get(l);
          if (f || (e = e.ownerDocument || e, f = {
            type: "stylesheet",
            instance: null,
            count: 0,
            state: { loading: 0, preload: null }
          }, n.set(l, f), (n = e.querySelector(
            ne(l)
          )) && !n._p && (f.instance = n, f.state.loading = 5), Il.has(l) || (a = {
            rel: "preload",
            as: "style",
            href: a.href,
            crossOrigin: a.crossOrigin,
            integrity: a.integrity,
            media: a.media,
            hrefLang: a.hrefLang,
            referrerPolicy: a.referrerPolicy
          }, Il.set(l, a), n || Md(
            e,
            l,
            a,
            f.state
          ))), t && u === null)
            throw Error(g(528, ""));
          return f;
        }
        if (t && u !== null)
          throw Error(g(529, ""));
        return null;
      case "script":
        return t = a.async, a = a.src, typeof a == "string" && t && typeof t != "function" && typeof t != "symbol" ? (t = gu(a), a = Qa(
          e
        ).hoistableScripts, u = a.get(t), u || (u = {
          type: "script",
          instance: null,
          count: 0,
          state: null
        }, a.set(t, u)), u) : { type: "void", instance: null, count: 0, state: null };
      default:
        throw Error(g(444, l));
    }
  }
  function Su(l) {
    return 'href="' + xl(l) + '"';
  }
  function ne(l) {
    return 'link[rel="stylesheet"][' + l + "]";
  }
  function Ly(l) {
    return H({}, l, {
      "data-precedence": l.precedence,
      precedence: null
    });
  }
  function Md(l, t, a, u) {
    l.querySelector('link[rel="preload"][as="style"][' + t + "]") ? u.loading = 1 : (t = l.createElement("link"), u.preload = t, t.addEventListener("load", function() {
      return u.loading |= 1;
    }), t.addEventListener("error", function() {
      return u.loading |= 2;
    }), zl(t, "link", a), dl(t), l.head.appendChild(t));
  }
  function gu(l) {
    return '[src="' + xl(l) + '"]';
  }
  function fe(l) {
    return "script[async]" + l;
  }
  function xy(l, t, a) {
    if (t.count++, t.instance === null)
      switch (t.type) {
        case "style":
          var u = l.querySelector(
            'style[data-href~="' + xl(a.href) + '"]'
          );
          if (u)
            return t.instance = u, dl(u), u;
          var e = H({}, a, {
            "data-href": a.href,
            "data-precedence": a.precedence,
            href: null,
            precedence: null
          });
          return u = (l.ownerDocument || l).createElement(
            "style"
          ), dl(u), zl(u, "style", e), On(u, a.precedence, l), t.instance = u;
        case "stylesheet":
          e = Su(a.href);
          var n = l.querySelector(
            ne(e)
          );
          if (n)
            return t.state.loading |= 4, t.instance = n, dl(n), n;
          u = Ly(a), (e = Il.get(e)) && rc(u, e), n = (l.ownerDocument || l).createElement("link"), dl(n);
          var f = n;
          return f._p = new Promise(function(c, i) {
            f.onload = c, f.onerror = i;
          }), zl(n, "link", u), t.state.loading |= 4, On(n, a.precedence, l), t.instance = n;
        case "script":
          return n = gu(a.src), (e = l.querySelector(
            fe(n)
          )) ? (t.instance = e, dl(e), e) : (u = a, (e = Il.get(n)) && (u = H({}, a), Wc(u, e)), l = l.ownerDocument || l, e = l.createElement("script"), dl(e), zl(e, "link", u), l.head.appendChild(e), t.instance = e);
        case "void":
          return null;
        default:
          throw Error(g(443, t.type));
      }
    else
      t.type === "stylesheet" && (t.state.loading & 4) === 0 && (u = t.instance, t.state.loading |= 4, On(u, a.precedence, l));
    return t.instance;
  }
  function On(l, t, a) {
    for (var u = a.querySelectorAll(
      'link[rel="stylesheet"][data-precedence],style[data-precedence]'
    ), e = u.length ? u[u.length - 1] : null, n = e, f = 0; f < u.length; f++) {
      var c = u[f];
      if (c.dataset.precedence === t) n = c;
      else if (n !== e) break;
    }
    n ? n.parentNode.insertBefore(l, n.nextSibling) : (t = a.nodeType === 9 ? a.head : a, t.insertBefore(l, t.firstChild));
  }
  function rc(l, t) {
    l.crossOrigin == null && (l.crossOrigin = t.crossOrigin), l.referrerPolicy == null && (l.referrerPolicy = t.referrerPolicy), l.title == null && (l.title = t.title);
  }
  function Wc(l, t) {
    l.crossOrigin == null && (l.crossOrigin = t.crossOrigin), l.referrerPolicy == null && (l.referrerPolicy = t.referrerPolicy), l.integrity == null && (l.integrity = t.integrity);
  }
  var Mn = null;
  function Jy(l, t, a) {
    if (Mn === null) {
      var u = /* @__PURE__ */ new Map(), e = Mn = /* @__PURE__ */ new Map();
      e.set(a, u);
    } else
      e = Mn, u = e.get(a), u || (u = /* @__PURE__ */ new Map(), e.set(a, u));
    if (u.has(l)) return u;
    for (u.set(l, null), a = a.getElementsByTagName(l), e = 0; e < a.length; e++) {
      var n = a[e];
      if (!(n[ou] || n[Sl] || l === "link" && n.getAttribute("rel") === "stylesheet") && n.namespaceURI !== "http://www.w3.org/2000/svg") {
        var f = n.getAttribute(t) || "";
        f = l + f;
        var c = u.get(f);
        c ? c.push(n) : u.set(f, [n]);
      }
    }
    return u;
  }
  function ry(l, t, a) {
    l = l.ownerDocument || l, l.head.insertBefore(
      a,
      t === "title" ? l.querySelector("head > title") : null
    );
  }
  function Dd(l, t, a) {
    if (a === 1 || t.itemProp != null) return !1;
    switch (l) {
      case "meta":
      case "title":
        return !0;
      case "style":
        if (typeof t.precedence != "string" || typeof t.href != "string" || t.href === "")
          break;
        return !0;
      case "link":
        if (typeof t.rel != "string" || typeof t.href != "string" || t.href === "" || t.onLoad || t.onError)
          break;
        return t.rel === "stylesheet" ? (l = t.disabled, typeof t.precedence == "string" && l == null) : !0;
      case "script":
        if (t.async && typeof t.async != "function" && typeof t.async != "symbol" && !t.onLoad && !t.onError && t.src && typeof t.src == "string")
          return !0;
    }
    return !1;
  }
  function Wy(l) {
    return !(l.type === "stylesheet" && (l.state.loading & 3) === 0);
  }
  function Ud(l, t, a, u) {
    if (a.type === "stylesheet" && (typeof u.media != "string" || matchMedia(u.media).matches !== !1) && (a.state.loading & 4) === 0) {
      if (a.instance === null) {
        var e = Su(u.href), n = t.querySelector(
          ne(e)
        );
        if (n) {
          t = n._p, t !== null && typeof t == "object" && typeof t.then == "function" && (l.count++, l = Dn.bind(l), t.then(l, l)), a.state.loading |= 4, a.instance = n, dl(n);
          return;
        }
        n = t.ownerDocument || t, u = Ly(u), (e = Il.get(e)) && rc(u, e), n = n.createElement("link"), dl(n);
        var f = n;
        f._p = new Promise(function(c, i) {
          f.onload = c, f.onerror = i;
        }), zl(n, "link", u), a.instance = n;
      }
      l.stylesheets === null && (l.stylesheets = /* @__PURE__ */ new Map()), l.stylesheets.set(a, t), (t = a.state.preload) && (a.state.loading & 3) === 0 && (l.count++, a = Dn.bind(l), t.addEventListener("load", a), t.addEventListener("error", a));
    }
  }
  var wc = 0;
  function _d(l, t) {
    return l.stylesheets && l.count === 0 && _n(l, l.stylesheets), 0 < l.count || 0 < l.imgCount ? function(a) {
      var u = setTimeout(function() {
        if (l.stylesheets && _n(l, l.stylesheets), l.unsuspend) {
          var n = l.unsuspend;
          l.unsuspend = null, n();
        }
      }, 6e4 + t);
      0 < l.imgBytes && wc === 0 && (wc = 62500 * cd());
      var e = setTimeout(
        function() {
          if (l.waitingForImages = !1, l.count === 0 && (l.stylesheets && _n(l, l.stylesheets), l.unsuspend)) {
            var n = l.unsuspend;
            l.unsuspend = null, n();
          }
        },
        (l.imgBytes > wc ? 50 : 800) + t
      );
      return l.unsuspend = a, function() {
        l.unsuspend = null, clearTimeout(u), clearTimeout(e);
      };
    } : null;
  }
  function Dn() {
    if (this.count--, this.count === 0 && (this.imgCount === 0 || !this.waitingForImages)) {
      if (this.stylesheets) _n(this, this.stylesheets);
      else if (this.unsuspend) {
        var l = this.unsuspend;
        this.unsuspend = null, l();
      }
    }
  }
  var Un = null;
  function _n(l, t) {
    l.stylesheets = null, l.unsuspend !== null && (l.count++, Un = /* @__PURE__ */ new Map(), t.forEach(Hd, l), Un = null, Dn.call(l));
  }
  function Hd(l, t) {
    if (!(t.state.loading & 4)) {
      var a = Un.get(l);
      if (a) var u = a.get(null);
      else {
        a = /* @__PURE__ */ new Map(), Un.set(l, a);
        for (var e = l.querySelectorAll(
          "link[data-precedence],style[data-precedence]"
        ), n = 0; n < e.length; n++) {
          var f = e[n];
          (f.nodeName === "LINK" || f.getAttribute("media") !== "not all") && (a.set(f.dataset.precedence, f), u = f);
        }
        u && a.set(null, u);
      }
      e = t.instance, f = e.getAttribute("data-precedence"), n = a.get(f) || u, n === u && a.set(null, e), a.set(f, e), this.count++, u = Dn.bind(this), e.addEventListener("load", u), e.addEventListener("error", u), n ? n.parentNode.insertBefore(e, n.nextSibling) : (l = l.nodeType === 9 ? l.head : l, l.insertBefore(e, l.firstChild)), t.state.loading |= 4;
    }
  }
  var ce = {
    $$typeof: pl,
    Provider: null,
    Consumer: null,
    _currentValue: N,
    _currentValue2: N,
    _threadCount: 0
  };
  function Nd(l, t, a, u, e, n, f, c, i) {
    this.tag = 1, this.containerInfo = l, this.pingCache = this.current = this.pendingChildren = null, this.timeoutHandle = -1, this.callbackNode = this.next = this.pendingContext = this.context = this.cancelPendingCommit = null, this.callbackPriority = 0, this.expirationTimes = Kn(-1), this.entangledLanes = this.shellSuspendCounter = this.errorRecoveryDisabledLanes = this.expiredLanes = this.warmLanes = this.pingedLanes = this.suspendedLanes = this.pendingLanes = 0, this.entanglements = Kn(0), this.hiddenUpdates = Kn(null), this.identifierPrefix = u, this.onUncaughtError = e, this.onCaughtError = n, this.onRecoverableError = f, this.pooledCache = null, this.pooledCacheLanes = 0, this.formState = i, this.incompleteTransitions = /* @__PURE__ */ new Map();
  }
  function wy(l, t, a, u, e, n, f, c, i, d, s, T) {
    return l = new Nd(
      l,
      t,
      a,
      f,
      i,
      d,
      s,
      T,
      c
    ), t = 1, n === !0 && (t |= 24), n = Rl(3, null, null, t), l.current = n, n.stateNode = l, t = _f(), t.refCount++, l.pooledCache = t, t.refCount++, n.memoizedState = {
      element: u,
      isDehydrated: a,
      cache: t
    }, Bf(n), l;
  }
  function $y(l) {
    return l ? (l = ra, l) : ra;
  }
  function Fy(l, t, a, u, e, n) {
    e = $y(e), u.context === null ? u.context = e : u.pendingContext = e, u = Jt(t), u.payload = { element: a }, n = n === void 0 ? null : n, n !== null && (u.callback = n), a = rt(l, u, t), a !== null && (Hl(a, l, t), ju(a, l, t));
  }
  function ky(l, t) {
    if (l = l.memoizedState, l !== null && l.dehydrated !== null) {
      var a = l.retryLane;
      l.retryLane = a !== 0 && a < t ? a : t;
    }
  }
  function $c(l, t) {
    ky(l, t), (l = l.alternate) && ky(l, t);
  }
  function Iy(l) {
    if (l.tag === 13 || l.tag === 31) {
      var t = ga(l, 67108864);
      t !== null && Hl(t, l, 67108864), $c(l, 67108864);
    }
  }
  function Py(l) {
    if (l.tag === 13 || l.tag === 31) {
      var t = jl();
      t = Ln(t);
      var a = ga(l, t);
      a !== null && Hl(a, l, t), $c(l, t);
    }
  }
  var Hn = !0;
  function qd(l, t, a, u) {
    var e = z.T;
    z.T = null;
    var n = E.p;
    try {
      E.p = 2, Fc(l, t, a, u);
    } finally {
      E.p = n, z.T = e;
    }
  }
  function Bd(l, t, a, u) {
    var e = z.T;
    z.T = null;
    var n = E.p;
    try {
      E.p = 8, Fc(l, t, a, u);
    } finally {
      E.p = n, z.T = e;
    }
  }
  function Fc(l, t, a, u) {
    if (Hn) {
      var e = kc(u);
      if (e === null)
        Qc(
          l,
          t,
          u,
          Nn,
          a
        ), t1(l, u);
      else if (Gd(
        e,
        l,
        t,
        a,
        u
      ))
        u.stopPropagation();
      else if (t1(l, u), t & 4 && -1 < Yd.indexOf(l)) {
        for (; e !== null; ) {
          var n = Xa(e);
          if (n !== null)
            switch (n.tag) {
              case 3:
                if (n = n.stateNode, n.current.memoizedState.isDehydrated) {
                  var f = ya(n.pendingLanes);
                  if (f !== 0) {
                    var c = n;
                    for (c.pendingLanes |= 2, c.entangledLanes |= 2; f; ) {
                      var i = 1 << 31 - Yl(f);
                      c.entanglements[1] |= i, f &= ~i;
                    }
                    mt(n), (j & 6) === 0 && (mn = ql() + 500, te(0));
                  }
                }
                break;
              case 31:
              case 13:
                c = ga(n, 2), c !== null && Hl(c, n, 2), hn(), $c(n, 2);
            }
          if (n = kc(u), n === null && Qc(
            l,
            t,
            u,
            Nn,
            a
          ), n === e) break;
          e = n;
        }
        e !== null && u.stopPropagation();
      } else
        Qc(
          l,
          t,
          u,
          null,
          a
        );
    }
  }
  function kc(l) {
    return l = In(l), Ic(l);
  }
  var Nn = null;
  function Ic(l) {
    if (Nn = null, l = Ra(l), l !== null) {
      var t = ht(l);
      if (t === null) l = null;
      else {
        var a = t.tag;
        if (a === 13) {
          if (l = Pl(t), l !== null) return l;
          l = null;
        } else if (a === 31) {
          if (l = St(t), l !== null) return l;
          l = null;
        } else if (a === 3) {
          if (t.stateNode.current.memoizedState.isDehydrated)
            return t.tag === 3 ? t.stateNode.containerInfo : null;
          l = null;
        } else t !== l && (l = null);
      }
    }
    return Nn = l, null;
  }
  function l1(l) {
    switch (l) {
      case "beforetoggle":
      case "cancel":
      case "click":
      case "close":
      case "contextmenu":
      case "copy":
      case "cut":
      case "auxclick":
      case "dblclick":
      case "dragend":
      case "dragstart":
      case "drop":
      case "focusin":
      case "focusout":
      case "input":
      case "invalid":
      case "keydown":
      case "keypress":
      case "keyup":
      case "mousedown":
      case "mouseup":
      case "paste":
      case "pause":
      case "play":
      case "pointercancel":
      case "pointerdown":
      case "pointerup":
      case "ratechange":
      case "reset":
      case "resize":
      case "seeked":
      case "submit":
      case "toggle":
      case "touchcancel":
      case "touchend":
      case "touchstart":
      case "volumechange":
      case "change":
      case "selectionchange":
      case "textInput":
      case "compositionstart":
      case "compositionend":
      case "compositionupdate":
      case "beforeblur":
      case "afterblur":
      case "beforeinput":
      case "blur":
      case "fullscreenchange":
      case "focus":
      case "hashchange":
      case "popstate":
      case "select":
      case "selectstart":
        return 2;
      case "drag":
      case "dragenter":
      case "dragexit":
      case "dragleave":
      case "dragover":
      case "mousemove":
      case "mouseout":
      case "mouseover":
      case "pointermove":
      case "pointerout":
      case "pointerover":
      case "scroll":
      case "touchmove":
      case "wheel":
      case "mouseenter":
      case "mouseleave":
      case "pointerenter":
      case "pointerleave":
        return 8;
      case "message":
        switch (b1()) {
          case ii:
            return 2;
          case vi:
            return 8;
          case be:
          case z1:
            return 32;
          case yi:
            return 268435456;
          default:
            return 32;
        }
      default:
        return 32;
    }
  }
  var Pc = !1, ua = null, ea = null, na = null, ie = /* @__PURE__ */ new Map(), ve = /* @__PURE__ */ new Map(), fa = [], Yd = "mousedown mouseup touchcancel touchend touchstart auxclick dblclick pointercancel pointerdown pointerup dragend dragstart drop compositionend compositionstart keydown keypress keyup input textInput copy cut paste click change contextmenu reset".split(
    " "
  );
  function t1(l, t) {
    switch (l) {
      case "focusin":
      case "focusout":
        ua = null;
        break;
      case "dragenter":
      case "dragleave":
        ea = null;
        break;
      case "mouseover":
      case "mouseout":
        na = null;
        break;
      case "pointerover":
      case "pointerout":
        ie.delete(t.pointerId);
        break;
      case "gotpointercapture":
      case "lostpointercapture":
        ve.delete(t.pointerId);
    }
  }
  function ye(l, t, a, u, e, n) {
    return l === null || l.nativeEvent !== n ? (l = {
      blockedOn: t,
      domEventName: a,
      eventSystemFlags: u,
      nativeEvent: n,
      targetContainers: [e]
    }, t !== null && (t = Xa(t), t !== null && Iy(t)), l) : (l.eventSystemFlags |= u, t = l.targetContainers, e !== null && t.indexOf(e) === -1 && t.push(e), l);
  }
  function Gd(l, t, a, u, e) {
    switch (t) {
      case "focusin":
        return ua = ye(
          ua,
          l,
          t,
          a,
          u,
          e
        ), !0;
      case "dragenter":
        return ea = ye(
          ea,
          l,
          t,
          a,
          u,
          e
        ), !0;
      case "mouseover":
        return na = ye(
          na,
          l,
          t,
          a,
          u,
          e
        ), !0;
      case "pointerover":
        var n = e.pointerId;
        return ie.set(
          n,
          ye(
            ie.get(n) || null,
            l,
            t,
            a,
            u,
            e
          )
        ), !0;
      case "gotpointercapture":
        return n = e.pointerId, ve.set(
          n,
          ye(
            ve.get(n) || null,
            l,
            t,
            a,
            u,
            e
          )
        ), !0;
    }
    return !1;
  }
  function a1(l) {
    var t = Ra(l.target);
    if (t !== null) {
      var a = ht(t);
      if (a !== null) {
        if (t = a.tag, t === 13) {
          if (t = Pl(a), t !== null) {
            l.blockedOn = t, si(l.priority, function() {
              Py(a);
            });
            return;
          }
        } else if (t === 31) {
          if (t = St(a), t !== null) {
            l.blockedOn = t, si(l.priority, function() {
              Py(a);
            });
            return;
          }
        } else if (t === 3 && a.stateNode.current.memoizedState.isDehydrated) {
          l.blockedOn = a.tag === 3 ? a.stateNode.containerInfo : null;
          return;
        }
      }
    }
    l.blockedOn = null;
  }
  function qn(l) {
    if (l.blockedOn !== null) return !1;
    for (var t = l.targetContainers; 0 < t.length; ) {
      var a = kc(l.nativeEvent);
      if (a === null) {
        a = l.nativeEvent;
        var u = new a.constructor(
          a.type,
          a
        );
        kn = u, a.target.dispatchEvent(u), kn = null;
      } else
        return t = Xa(a), t !== null && Iy(t), l.blockedOn = a, !1;
      t.shift();
    }
    return !0;
  }
  function u1(l, t, a) {
    qn(l) && a.delete(t);
  }
  function Rd() {
    Pc = !1, ua !== null && qn(ua) && (ua = null), ea !== null && qn(ea) && (ea = null), na !== null && qn(na) && (na = null), ie.forEach(u1), ve.forEach(u1);
  }
  function Bn(l, t) {
    l.blockedOn === t && (l.blockedOn = null, Pc || (Pc = !0, D.unstable_scheduleCallback(
      D.unstable_NormalPriority,
      Rd
    )));
  }
  var Yn = null;
  function e1(l) {
    Yn !== l && (Yn = l, D.unstable_scheduleCallback(
      D.unstable_NormalPriority,
      function() {
        Yn === l && (Yn = null);
        for (var t = 0; t < l.length; t += 3) {
          var a = l[t], u = l[t + 1], e = l[t + 2];
          if (typeof u != "function") {
            if (Ic(u || a) === null)
              continue;
            break;
          }
          var n = Xa(a);
          n !== null && (l.splice(t, 3), t -= 3, kf(
            n,
            {
              pending: !0,
              data: e,
              method: a.method,
              action: u
            },
            u,
            e
          ));
        }
      }
    ));
  }
  function su(l) {
    function t(i) {
      return Bn(i, l);
    }
    ua !== null && Bn(ua, l), ea !== null && Bn(ea, l), na !== null && Bn(na, l), ie.forEach(t), ve.forEach(t);
    for (var a = 0; a < fa.length; a++) {
      var u = fa[a];
      u.blockedOn === l && (u.blockedOn = null);
    }
    for (; 0 < fa.length && (a = fa[0], a.blockedOn === null); )
      a1(a), a.blockedOn === null && fa.shift();
    if (a = (l.ownerDocument || l).$$reactFormReplay, a != null)
      for (u = 0; u < a.length; u += 3) {
        var e = a[u], n = a[u + 1], f = e[ol] || null;
        if (typeof n == "function")
          f || e1(a);
        else if (f) {
          var c = null;
          if (n && n.hasAttribute("formAction")) {
            if (e = n, f = n[ol] || null)
              c = f.formAction;
            else if (Ic(e) !== null) continue;
          } else c = f.action;
          typeof c == "function" ? a[u + 1] = c : (a.splice(u, 3), u -= 3), e1(a);
        }
      }
  }
  function n1() {
    function l(n) {
      n.canIntercept && n.info === "react-transition" && n.intercept({
        handler: function() {
          return new Promise(function(f) {
            return e = f;
          });
        },
        focusReset: "manual",
        scroll: "manual"
      });
    }
    function t() {
      e !== null && (e(), e = null), u || setTimeout(a, 20);
    }
    function a() {
      if (!u && !navigation.transition) {
        var n = navigation.currentEntry;
        n && n.url != null && navigation.navigate(n.url, {
          state: n.getState(),
          info: "react-transition",
          history: "replace"
        });
      }
    }
    if (typeof navigation == "object") {
      var u = !1, e = null;
      return navigation.addEventListener("navigate", l), navigation.addEventListener("navigatesuccess", t), navigation.addEventListener("navigateerror", t), setTimeout(a, 100), function() {
        u = !0, navigation.removeEventListener("navigate", l), navigation.removeEventListener("navigatesuccess", t), navigation.removeEventListener("navigateerror", t), e !== null && (e(), e = null);
      };
    }
  }
  function li(l) {
    this._internalRoot = l;
  }
  Gn.prototype.render = li.prototype.render = function(l) {
    var t = this._internalRoot;
    if (t === null) throw Error(g(409));
    var a = t.current, u = jl();
    Fy(a, u, l, t, null, null);
  }, Gn.prototype.unmount = li.prototype.unmount = function() {
    var l = this._internalRoot;
    if (l !== null) {
      this._internalRoot = null;
      var t = l.containerInfo;
      Fy(l.current, 2, null, l, null, null), hn(), t[Ga] = null;
    }
  };
  function Gn(l) {
    this._internalRoot = l;
  }
  Gn.prototype.unstable_scheduleHydration = function(l) {
    if (l) {
      var t = gi();
      l = { blockedOn: null, target: l, priority: t };
      for (var a = 0; a < fa.length && t !== 0 && t < fa[a].priority; a++) ;
      fa.splice(a, 0, l), a === 0 && a1(l);
    }
  };
  var f1 = Nl.version;
  if (f1 !== "19.2.7")
    throw Error(
      g(
        527,
        f1,
        "19.2.7"
      )
    );
  E.findDOMNode = function(l) {
    var t = l._reactInternals;
    if (t === void 0)
      throw typeof l.render == "function" ? Error(g(188)) : (l = Object.keys(l).join(","), Error(g(268, l)));
    return l = A(t), l = l !== null ? r(l) : null, l = l === null ? null : l.stateNode, l;
  };
  var Xd = {
    bundleType: 0,
    version: "19.2.7",
    rendererPackageName: "react-dom",
    currentDispatcherRef: z,
    reconcilerVersion: "19.2.7"
  };
  if (typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ < "u") {
    var Rn = __REACT_DEVTOOLS_GLOBAL_HOOK__;
    if (!Rn.isDisabled && Rn.supportsFiber)
      try {
        Tu = Rn.inject(
          Xd
        ), Bl = Rn;
      } catch {
      }
  }
  return me.createRoot = function(l, t) {
    if (!dt(l)) throw Error(g(299));
    var a = !1, u = "", e = hv, n = Sv, f = gv;
    return t != null && (t.unstable_strictMode === !0 && (a = !0), t.identifierPrefix !== void 0 && (u = t.identifierPrefix), t.onUncaughtError !== void 0 && (e = t.onUncaughtError), t.onCaughtError !== void 0 && (n = t.onCaughtError), t.onRecoverableError !== void 0 && (f = t.onRecoverableError)), t = wy(
      l,
      1,
      !1,
      null,
      null,
      a,
      u,
      null,
      e,
      n,
      f,
      n1
    ), l[Ga] = t.current, Xc(l), new li(t);
  }, me.hydrateRoot = function(l, t, a) {
    if (!dt(l)) throw Error(g(299));
    var u = !1, e = "", n = hv, f = Sv, c = gv, i = null;
    return a != null && (a.unstable_strictMode === !0 && (u = !0), a.identifierPrefix !== void 0 && (e = a.identifierPrefix), a.onUncaughtError !== void 0 && (n = a.onUncaughtError), a.onCaughtError !== void 0 && (f = a.onCaughtError), a.onRecoverableError !== void 0 && (c = a.onRecoverableError), a.formState !== void 0 && (i = a.formState)), t = wy(
      l,
      1,
      !0,
      t,
      a ?? null,
      u,
      e,
      i,
      n,
      f,
      c,
      n1
    ), t.context = $y(null), a = t.current, u = jl(), u = Ln(u), e = Jt(u), e.callback = null, rt(a, e, u), a = u, t.current.lanes = a, Eu(t, a), mt(t), l[Ga] = t.current, Xc(l), new Gn(t);
  }, me.version = "19.2.7", me;
}
var d1;
function Kd() {
  if (d1) return ti.exports;
  d1 = 1;
  function D() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(D);
      } catch (Nl) {
        console.error(Nl);
      }
  }
  return D(), ti.exports = pd(), ti.exports;
}
var ni = Kd();
const xd = ni.createRoot, Jd = ni.hydrateRoot, rd = ni.version;
export {
  xd as createRoot,
  Jd as hydrateRoot,
  rd as version
};
