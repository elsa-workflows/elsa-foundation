import it, { memo as je, forwardRef as Fc, useRef as ie, useEffect as Q, useCallback as le, useContext as dr, useMemo as de, useState as U, createContext as Xi, useLayoutEffect as hf, useId as Bc, lazy as gf, Suspense as yf } from "react";
import { Maximize2 as ro, GripVertical as Kc, ChevronUp as mf, ChevronDown as Uc, Trash2 as er, Plus as dn, X as Xc, Boxes as Eo, Zap as xf, Play as fn, Terminal as wf, ListTree as qi, GitBranch as qc, Check as bn, ListChecks as vf, Save as Yc, EyeOff as Fs, Shield as Bs, AlertTriangle as oo, SlidersHorizontal as Yi, Activity as bf, Search as Co, Package as Zc, ChevronRight as on, Undo2 as Nf, Redo2 as jf, Network as Sf, Sparkles as pt, Download as Ef, AlertCircle as St, ChevronLeft as io, Minimize2 as Ks, RotateCcw as Zi, Workflow as Cf, Code2 as If, Wrench as kf, Copy as Af } from "lucide-react";
import { useQuery as Gc, useQueryClient as _f, useMutation as Df } from "@tanstack/react-query";
function Tf(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var ri = { exports: {} }, Ln = {};
var Us;
function $f() {
  if (Us) return Ln;
  Us = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function n(r, o, s) {
    var a = null;
    if (s !== void 0 && (a = "" + s), o.key !== void 0 && (a = "" + o.key), "key" in o) {
      s = {};
      for (var c in o)
        c !== "key" && (s[c] = o[c]);
    } else s = o;
    return o = s.ref, {
      $$typeof: e,
      type: r,
      key: a,
      ref: o !== void 0 ? o : null,
      props: s
    };
  }
  return Ln.Fragment = t, Ln.jsx = n, Ln.jsxs = n, Ln;
}
var Xs;
function Mf() {
  return Xs || (Xs = 1, ri.exports = $f()), ri.exports;
}
var i = Mf();
function Ee(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, r; n < e.length; n++)
      (r = Ee(e[n])) !== "" && (t += (t && " ") + r);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var Pf = { value: () => {
} };
function Io() {
  for (var e = 0, t = arguments.length, n = {}, r; e < t; ++e) {
    if (!(r = arguments[e] + "") || r in n || /[\s.]/.test(r)) throw new Error("illegal type: " + r);
    n[r] = [];
  }
  return new Ur(n);
}
function Ur(e) {
  this._ = e;
}
function Rf(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var r = "", o = n.indexOf(".");
    if (o >= 0 && (r = n.slice(o + 1), n = n.slice(0, o)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: r };
  });
}
Ur.prototype = Io.prototype = {
  constructor: Ur,
  on: function(e, t) {
    var n = this._, r = Rf(e + "", n), o, s = -1, a = r.length;
    if (arguments.length < 2) {
      for (; ++s < a; ) if ((o = (e = r[s]).type) && (o = Lf(n[o], e.name))) return o;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++s < a; )
      if (o = (e = r[s]).type) n[o] = qs(n[o], e.name, t);
      else if (t == null) for (o in n) n[o] = qs(n[o], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new Ur(e);
  },
  call: function(e, t) {
    if ((o = arguments.length - 2) > 0) for (var n = new Array(o), r = 0, o, s; r < o; ++r) n[r] = arguments[r + 2];
    if (!this._.hasOwnProperty(e)) throw new Error("unknown type: " + e);
    for (s = this._[e], r = 0, o = s.length; r < o; ++r) s[r].value.apply(t, n);
  },
  apply: function(e, t, n) {
    if (!this._.hasOwnProperty(e)) throw new Error("unknown type: " + e);
    for (var r = this._[e], o = 0, s = r.length; o < s; ++o) r[o].value.apply(t, n);
  }
};
function Lf(e, t) {
  for (var n = 0, r = e.length, o; n < r; ++n)
    if ((o = e[n]).name === t)
      return o.value;
}
function qs(e, t, n) {
  for (var r = 0, o = e.length; r < o; ++r)
    if (e[r].name === t) {
      e[r] = Pf, e = e.slice(0, r).concat(e.slice(r + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var Ni = "http://www.w3.org/1999/xhtml";
const Ys = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: Ni,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function ko(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Ys.hasOwnProperty(t) ? { space: Ys[t], local: e } : e;
}
function zf(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === Ni && t.documentElement.namespaceURI === Ni ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Vf(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function Jc(e) {
  var t = ko(e);
  return (t.local ? Vf : zf)(t);
}
function Of() {
}
function Gi(e) {
  return e == null ? Of : function() {
    return this.querySelector(e);
  };
}
function Hf(e) {
  typeof e != "function" && (e = Gi(e));
  for (var t = this._groups, n = t.length, r = new Array(n), o = 0; o < n; ++o)
    for (var s = t[o], a = s.length, c = r[o] = new Array(a), u, l, f = 0; f < a; ++f)
      (u = s[f]) && (l = e.call(u, u.__data__, f, s)) && ("__data__" in u && (l.__data__ = u.__data__), c[f] = l);
  return new Xe(r, this._parents);
}
function Wf(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function Ff() {
  return [];
}
function Qc(e) {
  return e == null ? Ff : function() {
    return this.querySelectorAll(e);
  };
}
function Bf(e) {
  return function() {
    return Wf(e.apply(this, arguments));
  };
}
function Kf(e) {
  typeof e == "function" ? e = Bf(e) : e = Qc(e);
  for (var t = this._groups, n = t.length, r = [], o = [], s = 0; s < n; ++s)
    for (var a = t[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && (r.push(e.call(u, u.__data__, l, a)), o.push(u));
  return new Xe(r, o);
}
function el(e) {
  return function() {
    return this.matches(e);
  };
}
function tl(e) {
  return function(t) {
    return t.matches(e);
  };
}
var Uf = Array.prototype.find;
function Xf(e) {
  return function() {
    return Uf.call(this.children, e);
  };
}
function qf() {
  return this.firstElementChild;
}
function Yf(e) {
  return this.select(e == null ? qf : Xf(typeof e == "function" ? e : tl(e)));
}
var Zf = Array.prototype.filter;
function Gf() {
  return Array.from(this.children);
}
function Jf(e) {
  return function() {
    return Zf.call(this.children, e);
  };
}
function Qf(e) {
  return this.selectAll(e == null ? Gf : Jf(typeof e == "function" ? e : tl(e)));
}
function ep(e) {
  typeof e != "function" && (e = el(e));
  for (var t = this._groups, n = t.length, r = new Array(n), o = 0; o < n; ++o)
    for (var s = t[o], a = s.length, c = r[o] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new Xe(r, this._parents);
}
function nl(e) {
  return new Array(e.length);
}
function tp() {
  return new Xe(this._enter || this._groups.map(nl), this._parents);
}
function so(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
so.prototype = {
  constructor: so,
  appendChild: function(e) {
    return this._parent.insertBefore(e, this._next);
  },
  insertBefore: function(e, t) {
    return this._parent.insertBefore(e, t);
  },
  querySelector: function(e) {
    return this._parent.querySelector(e);
  },
  querySelectorAll: function(e) {
    return this._parent.querySelectorAll(e);
  }
};
function np(e) {
  return function() {
    return e;
  };
}
function rp(e, t, n, r, o, s) {
  for (var a = 0, c, u = t.length, l = s.length; a < l; ++a)
    (c = t[a]) ? (c.__data__ = s[a], r[a] = c) : n[a] = new so(e, s[a]);
  for (; a < u; ++a)
    (c = t[a]) && (o[a] = c);
}
function op(e, t, n, r, o, s, a) {
  var c, u, l = /* @__PURE__ */ new Map(), f = t.length, p = s.length, d = new Array(f), h;
  for (c = 0; c < f; ++c)
    (u = t[c]) && (d[c] = h = a.call(u, u.__data__, c, t) + "", l.has(h) ? o[c] = u : l.set(h, u));
  for (c = 0; c < p; ++c)
    h = a.call(e, s[c], c, s) + "", (u = l.get(h)) ? (r[c] = u, u.__data__ = s[c], l.delete(h)) : n[c] = new so(e, s[c]);
  for (c = 0; c < f; ++c)
    (u = t[c]) && l.get(d[c]) === u && (o[c] = u);
}
function ip(e) {
  return e.__data__;
}
function sp(e, t) {
  if (!arguments.length) return Array.from(this, ip);
  var n = t ? op : rp, r = this._parents, o = this._groups;
  typeof e != "function" && (e = np(e));
  for (var s = o.length, a = new Array(s), c = new Array(s), u = new Array(s), l = 0; l < s; ++l) {
    var f = r[l], p = o[l], d = p.length, h = ap(e.call(f, f && f.__data__, l, r)), g = h.length, N = c[l] = new Array(g), w = a[l] = new Array(g), m = u[l] = new Array(d);
    n(f, p, N, w, m, h, t);
    for (var C = 0, y = 0, b, j; C < g; ++C)
      if (b = N[C]) {
        for (C >= y && (y = C + 1); !(j = w[y]) && ++y < g; ) ;
        b._next = j || null;
      }
  }
  return a = new Xe(a, r), a._enter = c, a._exit = u, a;
}
function ap(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function cp() {
  return new Xe(this._exit || this._groups.map(nl), this._parents);
}
function lp(e, t, n) {
  var r = this.enter(), o = this, s = this.exit();
  return typeof e == "function" ? (r = e(r), r && (r = r.selection())) : r = r.append(e + ""), t != null && (o = t(o), o && (o = o.selection())), n == null ? s.remove() : n(s), r && o ? r.merge(o).order() : o;
}
function up(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, r = t._groups, o = n.length, s = r.length, a = Math.min(o, s), c = new Array(o), u = 0; u < a; ++u)
    for (var l = n[u], f = r[u], p = l.length, d = c[u] = new Array(p), h, g = 0; g < p; ++g)
      (h = l[g] || f[g]) && (d[g] = h);
  for (; u < o; ++u)
    c[u] = n[u];
  return new Xe(c, this._parents);
}
function dp() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var r = e[t], o = r.length - 1, s = r[o], a; --o >= 0; )
      (a = r[o]) && (s && a.compareDocumentPosition(s) ^ 4 && s.parentNode.insertBefore(a, s), s = a);
  return this;
}
function fp(e) {
  e || (e = pp);
  function t(p, d) {
    return p && d ? e(p.__data__, d.__data__) : !p - !d;
  }
  for (var n = this._groups, r = n.length, o = new Array(r), s = 0; s < r; ++s) {
    for (var a = n[s], c = a.length, u = o[s] = new Array(c), l, f = 0; f < c; ++f)
      (l = a[f]) && (u[f] = l);
    u.sort(t);
  }
  return new Xe(o, this._parents).order();
}
function pp(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function hp() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function gp() {
  return Array.from(this);
}
function yp() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var r = e[t], o = 0, s = r.length; o < s; ++o) {
      var a = r[o];
      if (a) return a;
    }
  return null;
}
function mp() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function xp() {
  return !this.node();
}
function wp(e) {
  for (var t = this._groups, n = 0, r = t.length; n < r; ++n)
    for (var o = t[n], s = 0, a = o.length, c; s < a; ++s)
      (c = o[s]) && e.call(c, c.__data__, s, o);
  return this;
}
function vp(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function bp(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Np(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function jp(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function Sp(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function Ep(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function Cp(e, t) {
  var n = ko(e);
  if (arguments.length < 2) {
    var r = this.node();
    return n.local ? r.getAttributeNS(n.space, n.local) : r.getAttribute(n);
  }
  return this.each((t == null ? n.local ? bp : vp : typeof t == "function" ? n.local ? Ep : Sp : n.local ? jp : Np)(n, t));
}
function rl(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function Ip(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function kp(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function Ap(e, t, n) {
  return function() {
    var r = t.apply(this, arguments);
    r == null ? this.style.removeProperty(e) : this.style.setProperty(e, r, n);
  };
}
function _p(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? Ip : typeof t == "function" ? Ap : kp)(e, t, n ?? "")) : pn(this.node(), e);
}
function pn(e, t) {
  return e.style.getPropertyValue(t) || rl(e).getComputedStyle(e, null).getPropertyValue(t);
}
function Dp(e) {
  return function() {
    delete this[e];
  };
}
function Tp(e, t) {
  return function() {
    this[e] = t;
  };
}
function $p(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function Mp(e, t) {
  return arguments.length > 1 ? this.each((t == null ? Dp : typeof t == "function" ? $p : Tp)(e, t)) : this.node()[e];
}
function ol(e) {
  return e.trim().split(/^|\s+/);
}
function Ji(e) {
  return e.classList || new il(e);
}
function il(e) {
  this._node = e, this._names = ol(e.getAttribute("class") || "");
}
il.prototype = {
  add: function(e) {
    var t = this._names.indexOf(e);
    t < 0 && (this._names.push(e), this._node.setAttribute("class", this._names.join(" ")));
  },
  remove: function(e) {
    var t = this._names.indexOf(e);
    t >= 0 && (this._names.splice(t, 1), this._node.setAttribute("class", this._names.join(" ")));
  },
  contains: function(e) {
    return this._names.indexOf(e) >= 0;
  }
};
function sl(e, t) {
  for (var n = Ji(e), r = -1, o = t.length; ++r < o; ) n.add(t[r]);
}
function al(e, t) {
  for (var n = Ji(e), r = -1, o = t.length; ++r < o; ) n.remove(t[r]);
}
function Pp(e) {
  return function() {
    sl(this, e);
  };
}
function Rp(e) {
  return function() {
    al(this, e);
  };
}
function Lp(e, t) {
  return function() {
    (t.apply(this, arguments) ? sl : al)(this, e);
  };
}
function zp(e, t) {
  var n = ol(e + "");
  if (arguments.length < 2) {
    for (var r = Ji(this.node()), o = -1, s = n.length; ++o < s; ) if (!r.contains(n[o])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? Lp : t ? Pp : Rp)(n, t));
}
function Vp() {
  this.textContent = "";
}
function Op(e) {
  return function() {
    this.textContent = e;
  };
}
function Hp(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function Wp(e) {
  return arguments.length ? this.each(e == null ? Vp : (typeof e == "function" ? Hp : Op)(e)) : this.node().textContent;
}
function Fp() {
  this.innerHTML = "";
}
function Bp(e) {
  return function() {
    this.innerHTML = e;
  };
}
function Kp(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function Up(e) {
  return arguments.length ? this.each(e == null ? Fp : (typeof e == "function" ? Kp : Bp)(e)) : this.node().innerHTML;
}
function Xp() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function qp() {
  return this.each(Xp);
}
function Yp() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Zp() {
  return this.each(Yp);
}
function Gp(e) {
  var t = typeof e == "function" ? e : Jc(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Jp() {
  return null;
}
function Qp(e, t) {
  var n = typeof e == "function" ? e : Jc(e), r = t == null ? Jp : typeof t == "function" ? t : Gi(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), r.apply(this, arguments) || null);
  });
}
function eh() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function th() {
  return this.each(eh);
}
function nh() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function rh() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function oh(e) {
  return this.select(e ? rh : nh);
}
function ih(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function sh(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function ah(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", r = t.indexOf(".");
    return r >= 0 && (n = t.slice(r + 1), t = t.slice(0, r)), { type: t, name: n };
  });
}
function ch(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, r = -1, o = t.length, s; n < o; ++n)
        s = t[n], (!e.type || s.type === e.type) && s.name === e.name ? this.removeEventListener(s.type, s.listener, s.options) : t[++r] = s;
      ++r ? t.length = r : delete this.__on;
    }
  };
}
function lh(e, t, n) {
  return function() {
    var r = this.__on, o, s = sh(t);
    if (r) {
      for (var a = 0, c = r.length; a < c; ++a)
        if ((o = r[a]).type === e.type && o.name === e.name) {
          this.removeEventListener(o.type, o.listener, o.options), this.addEventListener(o.type, o.listener = s, o.options = n), o.value = t;
          return;
        }
    }
    this.addEventListener(e.type, s, n), o = { type: e.type, name: e.name, value: t, listener: s, options: n }, r ? r.push(o) : this.__on = [o];
  };
}
function uh(e, t, n) {
  var r = ah(e + ""), o, s = r.length, a;
  if (arguments.length < 2) {
    var c = this.node().__on;
    if (c) {
      for (var u = 0, l = c.length, f; u < l; ++u)
        for (o = 0, f = c[u]; o < s; ++o)
          if ((a = r[o]).type === f.type && a.name === f.name)
            return f.value;
    }
    return;
  }
  for (c = t ? lh : ch, o = 0; o < s; ++o) this.each(c(r[o], t, n));
  return this;
}
function cl(e, t, n) {
  var r = rl(e), o = r.CustomEvent;
  typeof o == "function" ? o = new o(t, n) : (o = r.document.createEvent("Event"), n ? (o.initEvent(t, n.bubbles, n.cancelable), o.detail = n.detail) : o.initEvent(t, !1, !1)), e.dispatchEvent(o);
}
function dh(e, t) {
  return function() {
    return cl(this, e, t);
  };
}
function fh(e, t) {
  return function() {
    return cl(this, e, t.apply(this, arguments));
  };
}
function ph(e, t) {
  return this.each((typeof t == "function" ? fh : dh)(e, t));
}
function* hh() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var r = e[t], o = 0, s = r.length, a; o < s; ++o)
      (a = r[o]) && (yield a);
}
var ll = [null];
function Xe(e, t) {
  this._groups = e, this._parents = t;
}
function fr() {
  return new Xe([[document.documentElement]], ll);
}
function gh() {
  return this;
}
Xe.prototype = fr.prototype = {
  constructor: Xe,
  select: Hf,
  selectAll: Kf,
  selectChild: Yf,
  selectChildren: Qf,
  filter: ep,
  data: sp,
  enter: tp,
  exit: cp,
  join: lp,
  merge: up,
  selection: gh,
  order: dp,
  sort: fp,
  call: hp,
  nodes: gp,
  node: yp,
  size: mp,
  empty: xp,
  each: wp,
  attr: Cp,
  style: _p,
  property: Mp,
  classed: zp,
  text: Wp,
  html: Up,
  raise: qp,
  lower: Zp,
  append: Gp,
  insert: Qp,
  remove: th,
  clone: oh,
  datum: ih,
  on: uh,
  dispatch: ph,
  [Symbol.iterator]: hh
};
function Ue(e) {
  return typeof e == "string" ? new Xe([[document.querySelector(e)]], [document.documentElement]) : new Xe([[e]], ll);
}
function yh(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Ge(e, t) {
  if (e = yh(e), t === void 0 && (t = e.currentTarget), t) {
    var n = t.ownerSVGElement || t;
    if (n.createSVGPoint) {
      var r = n.createSVGPoint();
      return r.x = e.clientX, r.y = e.clientY, r = r.matrixTransform(t.getScreenCTM().inverse()), [r.x, r.y];
    }
    if (t.getBoundingClientRect) {
      var o = t.getBoundingClientRect();
      return [e.clientX - o.left - t.clientLeft, e.clientY - o.top - t.clientTop];
    }
  }
  return [e.pageX, e.pageY];
}
const mh = { passive: !1 }, tr = { capture: !0, passive: !1 };
function oi(e) {
  e.stopImmediatePropagation();
}
function cn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function ul(e) {
  var t = e.document.documentElement, n = Ue(e).on("dragstart.drag", cn, tr);
  "onselectstart" in t ? n.on("selectstart.drag", cn, tr) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function dl(e, t) {
  var n = e.document.documentElement, r = Ue(e).on("dragstart.drag", null);
  t && (r.on("click.drag", cn, tr), setTimeout(function() {
    r.on("click.drag", null);
  }, 0)), "onselectstart" in n ? r.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const Pr = (e) => () => e;
function ji(e, {
  sourceEvent: t,
  subject: n,
  target: r,
  identifier: o,
  active: s,
  x: a,
  y: c,
  dx: u,
  dy: l,
  dispatch: f
}) {
  Object.defineProperties(this, {
    type: { value: e, enumerable: !0, configurable: !0 },
    sourceEvent: { value: t, enumerable: !0, configurable: !0 },
    subject: { value: n, enumerable: !0, configurable: !0 },
    target: { value: r, enumerable: !0, configurable: !0 },
    identifier: { value: o, enumerable: !0, configurable: !0 },
    active: { value: s, enumerable: !0, configurable: !0 },
    x: { value: a, enumerable: !0, configurable: !0 },
    y: { value: c, enumerable: !0, configurable: !0 },
    dx: { value: u, enumerable: !0, configurable: !0 },
    dy: { value: l, enumerable: !0, configurable: !0 },
    _: { value: f }
  });
}
ji.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function xh(e) {
  return !e.ctrlKey && !e.button;
}
function wh() {
  return this.parentNode;
}
function vh(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function bh() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function fl() {
  var e = xh, t = wh, n = vh, r = bh, o = {}, s = Io("start", "drag", "end"), a = 0, c, u, l, f, p = 0;
  function d(b) {
    b.on("mousedown.drag", h).filter(r).on("touchstart.drag", w).on("touchmove.drag", m, mh).on("touchend.drag touchcancel.drag", C).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function h(b, j) {
    if (!(f || !e.call(this, b, j))) {
      var v = y(this, t.call(this, b, j), b, j, "mouse");
      v && (Ue(b.view).on("mousemove.drag", g, tr).on("mouseup.drag", N, tr), ul(b.view), oi(b), l = !1, c = b.clientX, u = b.clientY, v("start", b));
    }
  }
  function g(b) {
    if (cn(b), !l) {
      var j = b.clientX - c, v = b.clientY - u;
      l = j * j + v * v > p;
    }
    o.mouse("drag", b);
  }
  function N(b) {
    Ue(b.view).on("mousemove.drag mouseup.drag", null), dl(b.view, l), cn(b), o.mouse("end", b);
  }
  function w(b, j) {
    if (e.call(this, b, j)) {
      var v = b.changedTouches, S = t.call(this, b, j), A = v.length, M, R;
      for (M = 0; M < A; ++M)
        (R = y(this, S, b, j, v[M].identifier, v[M])) && (oi(b), R("start", b, v[M]));
    }
  }
  function m(b) {
    var j = b.changedTouches, v = j.length, S, A;
    for (S = 0; S < v; ++S)
      (A = o[j[S].identifier]) && (cn(b), A("drag", b, j[S]));
  }
  function C(b) {
    var j = b.changedTouches, v = j.length, S, A;
    for (f && clearTimeout(f), f = setTimeout(function() {
      f = null;
    }, 500), S = 0; S < v; ++S)
      (A = o[j[S].identifier]) && (oi(b), A("end", b, j[S]));
  }
  function y(b, j, v, S, A, M) {
    var R = s.copy(), $ = Ge(M || v, j), _, V, E;
    if ((E = n.call(b, new ji("beforestart", {
      sourceEvent: v,
      target: d,
      identifier: A,
      active: a,
      x: $[0],
      y: $[1],
      dx: 0,
      dy: 0,
      dispatch: R
    }), S)) != null)
      return _ = E.x - $[0] || 0, V = E.y - $[1] || 0, function D(k, T, L) {
        var P = $, K;
        switch (k) {
          case "start":
            o[A] = D, K = a++;
            break;
          case "end":
            delete o[A], --a;
          // falls through
          case "drag":
            $ = Ge(L || T, j), K = a;
            break;
        }
        R.call(
          k,
          b,
          new ji(k, {
            sourceEvent: T,
            subject: E,
            target: d,
            identifier: A,
            active: K,
            x: $[0] + _,
            y: $[1] + V,
            dx: $[0] - P[0],
            dy: $[1] - P[1],
            dispatch: R
          }),
          S
        );
      };
  }
  return d.filter = function(b) {
    return arguments.length ? (e = typeof b == "function" ? b : Pr(!!b), d) : e;
  }, d.container = function(b) {
    return arguments.length ? (t = typeof b == "function" ? b : Pr(b), d) : t;
  }, d.subject = function(b) {
    return arguments.length ? (n = typeof b == "function" ? b : Pr(b), d) : n;
  }, d.touchable = function(b) {
    return arguments.length ? (r = typeof b == "function" ? b : Pr(!!b), d) : r;
  }, d.on = function() {
    var b = s.on.apply(s, arguments);
    return b === s ? d : b;
  }, d.clickDistance = function(b) {
    return arguments.length ? (p = (b = +b) * b, d) : Math.sqrt(p);
  }, d;
}
function Qi(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function pl(e, t) {
  var n = Object.create(e.prototype);
  for (var r in t) n[r] = t[r];
  return n;
}
function pr() {
}
var nr = 0.7, ao = 1 / nr, ln = "\\s*([+-]?\\d+)\\s*", rr = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", ot = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", Nh = /^#([0-9a-f]{3,8})$/, jh = new RegExp(`^rgb\\(${ln},${ln},${ln}\\)$`), Sh = new RegExp(`^rgb\\(${ot},${ot},${ot}\\)$`), Eh = new RegExp(`^rgba\\(${ln},${ln},${ln},${rr}\\)$`), Ch = new RegExp(`^rgba\\(${ot},${ot},${ot},${rr}\\)$`), Ih = new RegExp(`^hsl\\(${rr},${ot},${ot}\\)$`), kh = new RegExp(`^hsla\\(${rr},${ot},${ot},${rr}\\)$`), Zs = {
  aliceblue: 15792383,
  antiquewhite: 16444375,
  aqua: 65535,
  aquamarine: 8388564,
  azure: 15794175,
  beige: 16119260,
  bisque: 16770244,
  black: 0,
  blanchedalmond: 16772045,
  blue: 255,
  blueviolet: 9055202,
  brown: 10824234,
  burlywood: 14596231,
  cadetblue: 6266528,
  chartreuse: 8388352,
  chocolate: 13789470,
  coral: 16744272,
  cornflowerblue: 6591981,
  cornsilk: 16775388,
  crimson: 14423100,
  cyan: 65535,
  darkblue: 139,
  darkcyan: 35723,
  darkgoldenrod: 12092939,
  darkgray: 11119017,
  darkgreen: 25600,
  darkgrey: 11119017,
  darkkhaki: 12433259,
  darkmagenta: 9109643,
  darkolivegreen: 5597999,
  darkorange: 16747520,
  darkorchid: 10040012,
  darkred: 9109504,
  darksalmon: 15308410,
  darkseagreen: 9419919,
  darkslateblue: 4734347,
  darkslategray: 3100495,
  darkslategrey: 3100495,
  darkturquoise: 52945,
  darkviolet: 9699539,
  deeppink: 16716947,
  deepskyblue: 49151,
  dimgray: 6908265,
  dimgrey: 6908265,
  dodgerblue: 2003199,
  firebrick: 11674146,
  floralwhite: 16775920,
  forestgreen: 2263842,
  fuchsia: 16711935,
  gainsboro: 14474460,
  ghostwhite: 16316671,
  gold: 16766720,
  goldenrod: 14329120,
  gray: 8421504,
  green: 32768,
  greenyellow: 11403055,
  grey: 8421504,
  honeydew: 15794160,
  hotpink: 16738740,
  indianred: 13458524,
  indigo: 4915330,
  ivory: 16777200,
  khaki: 15787660,
  lavender: 15132410,
  lavenderblush: 16773365,
  lawngreen: 8190976,
  lemonchiffon: 16775885,
  lightblue: 11393254,
  lightcoral: 15761536,
  lightcyan: 14745599,
  lightgoldenrodyellow: 16448210,
  lightgray: 13882323,
  lightgreen: 9498256,
  lightgrey: 13882323,
  lightpink: 16758465,
  lightsalmon: 16752762,
  lightseagreen: 2142890,
  lightskyblue: 8900346,
  lightslategray: 7833753,
  lightslategrey: 7833753,
  lightsteelblue: 11584734,
  lightyellow: 16777184,
  lime: 65280,
  limegreen: 3329330,
  linen: 16445670,
  magenta: 16711935,
  maroon: 8388608,
  mediumaquamarine: 6737322,
  mediumblue: 205,
  mediumorchid: 12211667,
  mediumpurple: 9662683,
  mediumseagreen: 3978097,
  mediumslateblue: 8087790,
  mediumspringgreen: 64154,
  mediumturquoise: 4772300,
  mediumvioletred: 13047173,
  midnightblue: 1644912,
  mintcream: 16121850,
  mistyrose: 16770273,
  moccasin: 16770229,
  navajowhite: 16768685,
  navy: 128,
  oldlace: 16643558,
  olive: 8421376,
  olivedrab: 7048739,
  orange: 16753920,
  orangered: 16729344,
  orchid: 14315734,
  palegoldenrod: 15657130,
  palegreen: 10025880,
  paleturquoise: 11529966,
  palevioletred: 14381203,
  papayawhip: 16773077,
  peachpuff: 16767673,
  peru: 13468991,
  pink: 16761035,
  plum: 14524637,
  powderblue: 11591910,
  purple: 8388736,
  rebeccapurple: 6697881,
  red: 16711680,
  rosybrown: 12357519,
  royalblue: 4286945,
  saddlebrown: 9127187,
  salmon: 16416882,
  sandybrown: 16032864,
  seagreen: 3050327,
  seashell: 16774638,
  sienna: 10506797,
  silver: 12632256,
  skyblue: 8900331,
  slateblue: 6970061,
  slategray: 7372944,
  slategrey: 7372944,
  snow: 16775930,
  springgreen: 65407,
  steelblue: 4620980,
  tan: 13808780,
  teal: 32896,
  thistle: 14204888,
  tomato: 16737095,
  turquoise: 4251856,
  violet: 15631086,
  wheat: 16113331,
  white: 16777215,
  whitesmoke: 16119285,
  yellow: 16776960,
  yellowgreen: 10145074
};
Qi(pr, Lt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Gs,
  // Deprecated! Use color.formatHex.
  formatHex: Gs,
  formatHex8: Ah,
  formatHsl: _h,
  formatRgb: Js,
  toString: Js
});
function Gs() {
  return this.rgb().formatHex();
}
function Ah() {
  return this.rgb().formatHex8();
}
function _h() {
  return hl(this).formatHsl();
}
function Js() {
  return this.rgb().formatRgb();
}
function Lt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = Nh.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? Qs(t) : n === 3 ? new Oe(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? Rr(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? Rr(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = jh.exec(e)) ? new Oe(t[1], t[2], t[3], 1) : (t = Sh.exec(e)) ? new Oe(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = Eh.exec(e)) ? Rr(t[1], t[2], t[3], t[4]) : (t = Ch.exec(e)) ? Rr(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = Ih.exec(e)) ? na(t[1], t[2] / 100, t[3] / 100, 1) : (t = kh.exec(e)) ? na(t[1], t[2] / 100, t[3] / 100, t[4]) : Zs.hasOwnProperty(e) ? Qs(Zs[e]) : e === "transparent" ? new Oe(NaN, NaN, NaN, 0) : null;
}
function Qs(e) {
  return new Oe(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function Rr(e, t, n, r) {
  return r <= 0 && (e = t = n = NaN), new Oe(e, t, n, r);
}
function Dh(e) {
  return e instanceof pr || (e = Lt(e)), e ? (e = e.rgb(), new Oe(e.r, e.g, e.b, e.opacity)) : new Oe();
}
function Si(e, t, n, r) {
  return arguments.length === 1 ? Dh(e) : new Oe(e, t, n, r ?? 1);
}
function Oe(e, t, n, r) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +r;
}
Qi(Oe, Si, pl(pr, {
  brighter(e) {
    return e = e == null ? ao : Math.pow(ao, e), new Oe(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? nr : Math.pow(nr, e), new Oe(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new Oe(Mt(this.r), Mt(this.g), Mt(this.b), co(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: ea,
  // Deprecated! Use color.formatHex.
  formatHex: ea,
  formatHex8: Th,
  formatRgb: ta,
  toString: ta
}));
function ea() {
  return `#${$t(this.r)}${$t(this.g)}${$t(this.b)}`;
}
function Th() {
  return `#${$t(this.r)}${$t(this.g)}${$t(this.b)}${$t((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function ta() {
  const e = co(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${Mt(this.r)}, ${Mt(this.g)}, ${Mt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function co(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function Mt(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function $t(e) {
  return e = Mt(e), (e < 16 ? "0" : "") + e.toString(16);
}
function na(e, t, n, r) {
  return r <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Je(e, t, n, r);
}
function hl(e) {
  if (e instanceof Je) return new Je(e.h, e.s, e.l, e.opacity);
  if (e instanceof pr || (e = Lt(e)), !e) return new Je();
  if (e instanceof Je) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, r = e.b / 255, o = Math.min(t, n, r), s = Math.max(t, n, r), a = NaN, c = s - o, u = (s + o) / 2;
  return c ? (t === s ? a = (n - r) / c + (n < r) * 6 : n === s ? a = (r - t) / c + 2 : a = (t - n) / c + 4, c /= u < 0.5 ? s + o : 2 - s - o, a *= 60) : c = u > 0 && u < 1 ? 0 : a, new Je(a, c, u, e.opacity);
}
function $h(e, t, n, r) {
  return arguments.length === 1 ? hl(e) : new Je(e, t, n, r ?? 1);
}
function Je(e, t, n, r) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +r;
}
Qi(Je, $h, pl(pr, {
  brighter(e) {
    return e = e == null ? ao : Math.pow(ao, e), new Je(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? nr : Math.pow(nr, e), new Je(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, r = n + (n < 0.5 ? n : 1 - n) * t, o = 2 * n - r;
    return new Oe(
      ii(e >= 240 ? e - 240 : e + 120, o, r),
      ii(e, o, r),
      ii(e < 120 ? e + 240 : e - 120, o, r),
      this.opacity
    );
  },
  clamp() {
    return new Je(ra(this.h), Lr(this.s), Lr(this.l), co(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = co(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${ra(this.h)}, ${Lr(this.s) * 100}%, ${Lr(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function ra(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function Lr(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function ii(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const es = (e) => () => e;
function Mh(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function Ph(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(r) {
    return Math.pow(e + r * t, n);
  };
}
function Rh(e) {
  return (e = +e) == 1 ? gl : function(t, n) {
    return n - t ? Ph(t, n, e) : es(isNaN(t) ? n : t);
  };
}
function gl(e, t) {
  var n = t - e;
  return n ? Mh(e, n) : es(isNaN(e) ? t : e);
}
const lo = (function e(t) {
  var n = Rh(t);
  function r(o, s) {
    var a = n((o = Si(o)).r, (s = Si(s)).r), c = n(o.g, s.g), u = n(o.b, s.b), l = gl(o.opacity, s.opacity);
    return function(f) {
      return o.r = a(f), o.g = c(f), o.b = u(f), o.opacity = l(f), o + "";
    };
  }
  return r.gamma = e, r;
})(1);
function Lh(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, r = t.slice(), o;
  return function(s) {
    for (o = 0; o < n; ++o) r[o] = e[o] * (1 - s) + t[o] * s;
    return r;
  };
}
function zh(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Vh(e, t) {
  var n = t ? t.length : 0, r = e ? Math.min(n, e.length) : 0, o = new Array(r), s = new Array(n), a;
  for (a = 0; a < r; ++a) o[a] = Yn(e[a], t[a]);
  for (; a < n; ++a) s[a] = t[a];
  return function(c) {
    for (a = 0; a < r; ++a) s[a] = o[a](c);
    return s;
  };
}
function Oh(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(r) {
    return n.setTime(e * (1 - r) + t * r), n;
  };
}
function rt(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function Hh(e, t) {
  var n = {}, r = {}, o;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (o in t)
    o in e ? n[o] = Yn(e[o], t[o]) : r[o] = t[o];
  return function(s) {
    for (o in n) r[o] = n[o](s);
    return r;
  };
}
var Ei = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, si = new RegExp(Ei.source, "g");
function Wh(e) {
  return function() {
    return e;
  };
}
function Fh(e) {
  return function(t) {
    return e(t) + "";
  };
}
function yl(e, t) {
  var n = Ei.lastIndex = si.lastIndex = 0, r, o, s, a = -1, c = [], u = [];
  for (e = e + "", t = t + ""; (r = Ei.exec(e)) && (o = si.exec(t)); )
    (s = o.index) > n && (s = t.slice(n, s), c[a] ? c[a] += s : c[++a] = s), (r = r[0]) === (o = o[0]) ? c[a] ? c[a] += o : c[++a] = o : (c[++a] = null, u.push({ i: a, x: rt(r, o) })), n = si.lastIndex;
  return n < t.length && (s = t.slice(n), c[a] ? c[a] += s : c[++a] = s), c.length < 2 ? u[0] ? Fh(u[0].x) : Wh(t) : (t = u.length, function(l) {
    for (var f = 0, p; f < t; ++f) c[(p = u[f]).i] = p.x(l);
    return c.join("");
  });
}
function Yn(e, t) {
  var n = typeof t, r;
  return t == null || n === "boolean" ? es(t) : (n === "number" ? rt : n === "string" ? (r = Lt(t)) ? (t = r, lo) : yl : t instanceof Lt ? lo : t instanceof Date ? Oh : zh(t) ? Lh : Array.isArray(t) ? Vh : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Hh : rt)(e, t);
}
var oa = 180 / Math.PI, Ci = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function ml(e, t, n, r, o, s) {
  var a, c, u;
  return (a = Math.sqrt(e * e + t * t)) && (e /= a, t /= a), (u = e * n + t * r) && (n -= e * u, r -= t * u), (c = Math.sqrt(n * n + r * r)) && (n /= c, r /= c, u /= c), e * r < t * n && (e = -e, t = -t, u = -u, a = -a), {
    translateX: o,
    translateY: s,
    rotate: Math.atan2(t, e) * oa,
    skewX: Math.atan(u) * oa,
    scaleX: a,
    scaleY: c
  };
}
var zr;
function Bh(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Ci : ml(t.a, t.b, t.c, t.d, t.e, t.f);
}
function Kh(e) {
  return e == null || (zr || (zr = document.createElementNS("http://www.w3.org/2000/svg", "g")), zr.setAttribute("transform", e), !(e = zr.transform.baseVal.consolidate())) ? Ci : (e = e.matrix, ml(e.a, e.b, e.c, e.d, e.e, e.f));
}
function xl(e, t, n, r) {
  function o(l) {
    return l.length ? l.pop() + " " : "";
  }
  function s(l, f, p, d, h, g) {
    if (l !== p || f !== d) {
      var N = h.push("translate(", null, t, null, n);
      g.push({ i: N - 4, x: rt(l, p) }, { i: N - 2, x: rt(f, d) });
    } else (p || d) && h.push("translate(" + p + t + d + n);
  }
  function a(l, f, p, d) {
    l !== f ? (l - f > 180 ? f += 360 : f - l > 180 && (l += 360), d.push({ i: p.push(o(p) + "rotate(", null, r) - 2, x: rt(l, f) })) : f && p.push(o(p) + "rotate(" + f + r);
  }
  function c(l, f, p, d) {
    l !== f ? d.push({ i: p.push(o(p) + "skewX(", null, r) - 2, x: rt(l, f) }) : f && p.push(o(p) + "skewX(" + f + r);
  }
  function u(l, f, p, d, h, g) {
    if (l !== p || f !== d) {
      var N = h.push(o(h) + "scale(", null, ",", null, ")");
      g.push({ i: N - 4, x: rt(l, p) }, { i: N - 2, x: rt(f, d) });
    } else (p !== 1 || d !== 1) && h.push(o(h) + "scale(" + p + "," + d + ")");
  }
  return function(l, f) {
    var p = [], d = [];
    return l = e(l), f = e(f), s(l.translateX, l.translateY, f.translateX, f.translateY, p, d), a(l.rotate, f.rotate, p, d), c(l.skewX, f.skewX, p, d), u(l.scaleX, l.scaleY, f.scaleX, f.scaleY, p, d), l = f = null, function(h) {
      for (var g = -1, N = d.length, w; ++g < N; ) p[(w = d[g]).i] = w.x(h);
      return p.join("");
    };
  };
}
var Uh = xl(Bh, "px, ", "px)", "deg)"), Xh = xl(Kh, ", ", ")", ")"), qh = 1e-12;
function ia(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Yh(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Zh(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const Xr = (function e(t, n, r) {
  function o(s, a) {
    var c = s[0], u = s[1], l = s[2], f = a[0], p = a[1], d = a[2], h = f - c, g = p - u, N = h * h + g * g, w, m;
    if (N < qh)
      m = Math.log(d / l) / t, w = function(S) {
        return [
          c + S * h,
          u + S * g,
          l * Math.exp(t * S * m)
        ];
      };
    else {
      var C = Math.sqrt(N), y = (d * d - l * l + r * N) / (2 * l * n * C), b = (d * d - l * l - r * N) / (2 * d * n * C), j = Math.log(Math.sqrt(y * y + 1) - y), v = Math.log(Math.sqrt(b * b + 1) - b);
      m = (v - j) / t, w = function(S) {
        var A = S * m, M = ia(j), R = l / (n * C) * (M * Zh(t * A + j) - Yh(j));
        return [
          c + R * h,
          u + R * g,
          l * M / ia(t * A + j)
        ];
      };
    }
    return w.duration = m * 1e3 * t / Math.SQRT2, w;
  }
  return o.rho = function(s) {
    var a = Math.max(1e-3, +s), c = a * a, u = c * c;
    return e(a, c, u);
  }, o;
})(Math.SQRT2, 2, 4);
var hn = 0, Un = 0, zn = 0, wl = 1e3, uo, Xn, fo = 0, zt = 0, Ao = 0, or = typeof performance == "object" && performance.now ? performance : Date, vl = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function ts() {
  return zt || (vl(Gh), zt = or.now() + Ao);
}
function Gh() {
  zt = 0;
}
function po() {
  this._call = this._time = this._next = null;
}
po.prototype = bl.prototype = {
  constructor: po,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? ts() : +n) + (t == null ? 0 : +t), !this._next && Xn !== this && (Xn ? Xn._next = this : uo = this, Xn = this), this._call = e, this._time = n, Ii();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Ii());
  }
};
function bl(e, t, n) {
  var r = new po();
  return r.restart(e, t, n), r;
}
function Jh() {
  ts(), ++hn;
  for (var e = uo, t; e; )
    (t = zt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --hn;
}
function sa() {
  zt = (fo = or.now()) + Ao, hn = Un = 0;
  try {
    Jh();
  } finally {
    hn = 0, eg(), zt = 0;
  }
}
function Qh() {
  var e = or.now(), t = e - fo;
  t > wl && (Ao -= t, fo = e);
}
function eg() {
  for (var e, t = uo, n, r = 1 / 0; t; )
    t._call ? (r > t._time && (r = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : uo = n);
  Xn = e, Ii(r);
}
function Ii(e) {
  if (!hn) {
    Un && (Un = clearTimeout(Un));
    var t = e - zt;
    t > 24 ? (e < 1 / 0 && (Un = setTimeout(sa, e - or.now() - Ao)), zn && (zn = clearInterval(zn))) : (zn || (fo = or.now(), zn = setInterval(Qh, wl)), hn = 1, vl(sa));
  }
}
function aa(e, t, n) {
  var r = new po();
  return t = t == null ? 0 : +t, r.restart((o) => {
    r.stop(), e(o + t);
  }, t, n), r;
}
var tg = Io("start", "end", "cancel", "interrupt"), ng = [], Nl = 0, ca = 1, ki = 2, qr = 3, la = 4, Ai = 5, Yr = 6;
function _o(e, t, n, r, o, s) {
  var a = e.__transition;
  if (!a) e.__transition = {};
  else if (n in a) return;
  rg(e, n, {
    name: t,
    index: r,
    // For context during callback.
    group: o,
    // For context during callback.
    on: tg,
    tween: ng,
    time: s.time,
    delay: s.delay,
    duration: s.duration,
    ease: s.ease,
    timer: null,
    state: Nl
  });
}
function ns(e, t) {
  var n = nt(e, t);
  if (n.state > Nl) throw new Error("too late; already scheduled");
  return n;
}
function at(e, t) {
  var n = nt(e, t);
  if (n.state > qr) throw new Error("too late; already running");
  return n;
}
function nt(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function rg(e, t, n) {
  var r = e.__transition, o;
  r[t] = n, n.timer = bl(s, 0, n.time);
  function s(l) {
    n.state = ca, n.timer.restart(a, n.delay, n.time), n.delay <= l && a(l - n.delay);
  }
  function a(l) {
    var f, p, d, h;
    if (n.state !== ca) return u();
    for (f in r)
      if (h = r[f], h.name === n.name) {
        if (h.state === qr) return aa(a);
        h.state === la ? (h.state = Yr, h.timer.stop(), h.on.call("interrupt", e, e.__data__, h.index, h.group), delete r[f]) : +f < t && (h.state = Yr, h.timer.stop(), h.on.call("cancel", e, e.__data__, h.index, h.group), delete r[f]);
      }
    if (aa(function() {
      n.state === qr && (n.state = la, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = ki, n.on.call("start", e, e.__data__, n.index, n.group), n.state === ki) {
      for (n.state = qr, o = new Array(d = n.tween.length), f = 0, p = -1; f < d; ++f)
        (h = n.tween[f].value.call(e, e.__data__, n.index, n.group)) && (o[++p] = h);
      o.length = p + 1;
    }
  }
  function c(l) {
    for (var f = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(u), n.state = Ai, 1), p = -1, d = o.length; ++p < d; )
      o[p].call(e, f);
    n.state === Ai && (n.on.call("end", e, e.__data__, n.index, n.group), u());
  }
  function u() {
    n.state = Yr, n.timer.stop(), delete r[t];
    for (var l in r) return;
    delete e.__transition;
  }
}
function Zr(e, t) {
  var n = e.__transition, r, o, s = !0, a;
  if (n) {
    t = t == null ? null : t + "";
    for (a in n) {
      if ((r = n[a]).name !== t) {
        s = !1;
        continue;
      }
      o = r.state > ki && r.state < Ai, r.state = Yr, r.timer.stop(), r.on.call(o ? "interrupt" : "cancel", e, e.__data__, r.index, r.group), delete n[a];
    }
    s && delete e.__transition;
  }
}
function og(e) {
  return this.each(function() {
    Zr(this, e);
  });
}
function ig(e, t) {
  var n, r;
  return function() {
    var o = at(this, e), s = o.tween;
    if (s !== n) {
      r = n = s;
      for (var a = 0, c = r.length; a < c; ++a)
        if (r[a].name === t) {
          r = r.slice(), r.splice(a, 1);
          break;
        }
    }
    o.tween = r;
  };
}
function sg(e, t, n) {
  var r, o;
  if (typeof n != "function") throw new Error();
  return function() {
    var s = at(this, e), a = s.tween;
    if (a !== r) {
      o = (r = a).slice();
      for (var c = { name: t, value: n }, u = 0, l = o.length; u < l; ++u)
        if (o[u].name === t) {
          o[u] = c;
          break;
        }
      u === l && o.push(c);
    }
    s.tween = o;
  };
}
function ag(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var r = nt(this.node(), n).tween, o = 0, s = r.length, a; o < s; ++o)
      if ((a = r[o]).name === e)
        return a.value;
    return null;
  }
  return this.each((t == null ? ig : sg)(n, e, t));
}
function rs(e, t, n) {
  var r = e._id;
  return e.each(function() {
    var o = at(this, r);
    (o.value || (o.value = {}))[t] = n.apply(this, arguments);
  }), function(o) {
    return nt(o, r).value[t];
  };
}
function jl(e, t) {
  var n;
  return (typeof t == "number" ? rt : t instanceof Lt ? lo : (n = Lt(t)) ? (t = n, lo) : yl)(e, t);
}
function cg(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function lg(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function ug(e, t, n) {
  var r, o = n + "", s;
  return function() {
    var a = this.getAttribute(e);
    return a === o ? null : a === r ? s : s = t(r = a, n);
  };
}
function dg(e, t, n) {
  var r, o = n + "", s;
  return function() {
    var a = this.getAttributeNS(e.space, e.local);
    return a === o ? null : a === r ? s : s = t(r = a, n);
  };
}
function fg(e, t, n) {
  var r, o, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttribute(e) : (a = this.getAttribute(e), u = c + "", a === u ? null : a === r && u === o ? s : (o = u, s = t(r = a, c)));
  };
}
function pg(e, t, n) {
  var r, o, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (a = this.getAttributeNS(e.space, e.local), u = c + "", a === u ? null : a === r && u === o ? s : (o = u, s = t(r = a, c)));
  };
}
function hg(e, t) {
  var n = ko(e), r = n === "transform" ? Xh : jl;
  return this.attrTween(e, typeof t == "function" ? (n.local ? pg : fg)(n, r, rs(this, "attr." + e, t)) : t == null ? (n.local ? lg : cg)(n) : (n.local ? dg : ug)(n, r, t));
}
function gg(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function yg(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function mg(e, t) {
  var n, r;
  function o() {
    var s = t.apply(this, arguments);
    return s !== r && (n = (r = s) && yg(e, s)), n;
  }
  return o._value = t, o;
}
function xg(e, t) {
  var n, r;
  function o() {
    var s = t.apply(this, arguments);
    return s !== r && (n = (r = s) && gg(e, s)), n;
  }
  return o._value = t, o;
}
function wg(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var r = ko(e);
  return this.tween(n, (r.local ? mg : xg)(r, t));
}
function vg(e, t) {
  return function() {
    ns(this, e).delay = +t.apply(this, arguments);
  };
}
function bg(e, t) {
  return t = +t, function() {
    ns(this, e).delay = t;
  };
}
function Ng(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? vg : bg)(t, e)) : nt(this.node(), t).delay;
}
function jg(e, t) {
  return function() {
    at(this, e).duration = +t.apply(this, arguments);
  };
}
function Sg(e, t) {
  return t = +t, function() {
    at(this, e).duration = t;
  };
}
function Eg(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? jg : Sg)(t, e)) : nt(this.node(), t).duration;
}
function Cg(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    at(this, e).ease = t;
  };
}
function Ig(e) {
  var t = this._id;
  return arguments.length ? this.each(Cg(t, e)) : nt(this.node(), t).ease;
}
function kg(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    at(this, e).ease = n;
  };
}
function Ag(e) {
  if (typeof e != "function") throw new Error();
  return this.each(kg(this._id, e));
}
function _g(e) {
  typeof e != "function" && (e = el(e));
  for (var t = this._groups, n = t.length, r = new Array(n), o = 0; o < n; ++o)
    for (var s = t[o], a = s.length, c = r[o] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new ht(r, this._parents, this._name, this._id);
}
function Dg(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, r = t.length, o = n.length, s = Math.min(r, o), a = new Array(r), c = 0; c < s; ++c)
    for (var u = t[c], l = n[c], f = u.length, p = a[c] = new Array(f), d, h = 0; h < f; ++h)
      (d = u[h] || l[h]) && (p[h] = d);
  for (; c < r; ++c)
    a[c] = t[c];
  return new ht(a, this._parents, this._name, this._id);
}
function Tg(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function $g(e, t, n) {
  var r, o, s = Tg(t) ? ns : at;
  return function() {
    var a = s(this, e), c = a.on;
    c !== r && (o = (r = c).copy()).on(t, n), a.on = o;
  };
}
function Mg(e, t) {
  var n = this._id;
  return arguments.length < 2 ? nt(this.node(), n).on.on(e) : this.each($g(n, e, t));
}
function Pg(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function Rg() {
  return this.on("end.remove", Pg(this._id));
}
function Lg(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Gi(e));
  for (var r = this._groups, o = r.length, s = new Array(o), a = 0; a < o; ++a)
    for (var c = r[a], u = c.length, l = s[a] = new Array(u), f, p, d = 0; d < u; ++d)
      (f = c[d]) && (p = e.call(f, f.__data__, d, c)) && ("__data__" in f && (p.__data__ = f.__data__), l[d] = p, _o(l[d], t, n, d, l, nt(f, n)));
  return new ht(s, this._parents, t, n);
}
function zg(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Qc(e));
  for (var r = this._groups, o = r.length, s = [], a = [], c = 0; c < o; ++c)
    for (var u = r[c], l = u.length, f, p = 0; p < l; ++p)
      if (f = u[p]) {
        for (var d = e.call(f, f.__data__, p, u), h, g = nt(f, n), N = 0, w = d.length; N < w; ++N)
          (h = d[N]) && _o(h, t, n, N, d, g);
        s.push(d), a.push(f);
      }
  return new ht(s, a, t, n);
}
var Vg = fr.prototype.constructor;
function Og() {
  return new Vg(this._groups, this._parents);
}
function Hg(e, t) {
  var n, r, o;
  return function() {
    var s = pn(this, e), a = (this.style.removeProperty(e), pn(this, e));
    return s === a ? null : s === n && a === r ? o : o = t(n = s, r = a);
  };
}
function Sl(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Wg(e, t, n) {
  var r, o = n + "", s;
  return function() {
    var a = pn(this, e);
    return a === o ? null : a === r ? s : s = t(r = a, n);
  };
}
function Fg(e, t, n) {
  var r, o, s;
  return function() {
    var a = pn(this, e), c = n(this), u = c + "";
    return c == null && (u = c = (this.style.removeProperty(e), pn(this, e))), a === u ? null : a === r && u === o ? s : (o = u, s = t(r = a, c));
  };
}
function Bg(e, t) {
  var n, r, o, s = "style." + t, a = "end." + s, c;
  return function() {
    var u = at(this, e), l = u.on, f = u.value[s] == null ? c || (c = Sl(t)) : void 0;
    (l !== n || o !== f) && (r = (n = l).copy()).on(a, o = f), u.on = r;
  };
}
function Kg(e, t, n) {
  var r = (e += "") == "transform" ? Uh : jl;
  return t == null ? this.styleTween(e, Hg(e, r)).on("end.style." + e, Sl(e)) : typeof t == "function" ? this.styleTween(e, Fg(e, r, rs(this, "style." + e, t))).each(Bg(this._id, e)) : this.styleTween(e, Wg(e, r, t), n).on("end.style." + e, null);
}
function Ug(e, t, n) {
  return function(r) {
    this.style.setProperty(e, t.call(this, r), n);
  };
}
function Xg(e, t, n) {
  var r, o;
  function s() {
    var a = t.apply(this, arguments);
    return a !== o && (r = (o = a) && Ug(e, a, n)), r;
  }
  return s._value = t, s;
}
function qg(e, t, n) {
  var r = "style." + (e += "");
  if (arguments.length < 2) return (r = this.tween(r)) && r._value;
  if (t == null) return this.tween(r, null);
  if (typeof t != "function") throw new Error();
  return this.tween(r, Xg(e, t, n ?? ""));
}
function Yg(e) {
  return function() {
    this.textContent = e;
  };
}
function Zg(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function Gg(e) {
  return this.tween("text", typeof e == "function" ? Zg(rs(this, "text", e)) : Yg(e == null ? "" : e + ""));
}
function Jg(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Qg(e) {
  var t, n;
  function r() {
    var o = e.apply(this, arguments);
    return o !== n && (t = (n = o) && Jg(o)), t;
  }
  return r._value = e, r;
}
function ey(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, Qg(e));
}
function ty() {
  for (var e = this._name, t = this._id, n = El(), r = this._groups, o = r.length, s = 0; s < o; ++s)
    for (var a = r[s], c = a.length, u, l = 0; l < c; ++l)
      if (u = a[l]) {
        var f = nt(u, t);
        _o(u, e, n, l, a, {
          time: f.time + f.delay + f.duration,
          delay: 0,
          duration: f.duration,
          ease: f.ease
        });
      }
  return new ht(r, this._parents, e, n);
}
function ny() {
  var e, t, n = this, r = n._id, o = n.size();
  return new Promise(function(s, a) {
    var c = { value: a }, u = { value: function() {
      --o === 0 && s();
    } };
    n.each(function() {
      var l = at(this, r), f = l.on;
      f !== e && (t = (e = f).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(u)), l.on = t;
    }), o === 0 && s();
  });
}
var ry = 0;
function ht(e, t, n, r) {
  this._groups = e, this._parents = t, this._name = n, this._id = r;
}
function El() {
  return ++ry;
}
var dt = fr.prototype;
ht.prototype = {
  constructor: ht,
  select: Lg,
  selectAll: zg,
  selectChild: dt.selectChild,
  selectChildren: dt.selectChildren,
  filter: _g,
  merge: Dg,
  selection: Og,
  transition: ty,
  call: dt.call,
  nodes: dt.nodes,
  node: dt.node,
  size: dt.size,
  empty: dt.empty,
  each: dt.each,
  on: Mg,
  attr: hg,
  attrTween: wg,
  style: Kg,
  styleTween: qg,
  text: Gg,
  textTween: ey,
  remove: Rg,
  tween: ag,
  delay: Ng,
  duration: Eg,
  ease: Ig,
  easeVarying: Ag,
  end: ny,
  [Symbol.iterator]: dt[Symbol.iterator]
};
function oy(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var iy = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: oy
};
function sy(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function ay(e) {
  var t, n;
  e instanceof ht ? (t = e._id, e = e._name) : (t = El(), (n = iy).time = ts(), e = e == null ? null : e + "");
  for (var r = this._groups, o = r.length, s = 0; s < o; ++s)
    for (var a = r[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && _o(u, e, t, l, a, n || sy(u, t));
  return new ht(r, this._parents, e, t);
}
fr.prototype.interrupt = og;
fr.prototype.transition = ay;
const Vr = (e) => () => e;
function cy(e, {
  sourceEvent: t,
  target: n,
  transform: r,
  dispatch: o
}) {
  Object.defineProperties(this, {
    type: { value: e, enumerable: !0, configurable: !0 },
    sourceEvent: { value: t, enumerable: !0, configurable: !0 },
    target: { value: n, enumerable: !0, configurable: !0 },
    transform: { value: r, enumerable: !0, configurable: !0 },
    _: { value: o }
  });
}
function ft(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
ft.prototype = {
  constructor: ft,
  scale: function(e) {
    return e === 1 ? this : new ft(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new ft(this.k, this.x + this.k * e, this.y + this.k * t);
  },
  apply: function(e) {
    return [e[0] * this.k + this.x, e[1] * this.k + this.y];
  },
  applyX: function(e) {
    return e * this.k + this.x;
  },
  applyY: function(e) {
    return e * this.k + this.y;
  },
  invert: function(e) {
    return [(e[0] - this.x) / this.k, (e[1] - this.y) / this.k];
  },
  invertX: function(e) {
    return (e - this.x) / this.k;
  },
  invertY: function(e) {
    return (e - this.y) / this.k;
  },
  rescaleX: function(e) {
    return e.copy().domain(e.range().map(this.invertX, this).map(e.invert, e));
  },
  rescaleY: function(e) {
    return e.copy().domain(e.range().map(this.invertY, this).map(e.invert, e));
  },
  toString: function() {
    return "translate(" + this.x + "," + this.y + ") scale(" + this.k + ")";
  }
};
var Do = new ft(1, 0, 0);
Cl.prototype = ft.prototype;
function Cl(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return Do;
  return e.__zoom;
}
function ai(e) {
  e.stopImmediatePropagation();
}
function Vn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function ly(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function uy() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function ua() {
  return this.__zoom || Do;
}
function dy(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function fy() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function py(e, t, n) {
  var r = e.invertX(t[0][0]) - n[0][0], o = e.invertX(t[1][0]) - n[1][0], s = e.invertY(t[0][1]) - n[0][1], a = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    o > r ? (r + o) / 2 : Math.min(0, r) || Math.max(0, o),
    a > s ? (s + a) / 2 : Math.min(0, s) || Math.max(0, a)
  );
}
function Il() {
  var e = ly, t = uy, n = py, r = dy, o = fy, s = [0, 1 / 0], a = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, u = Xr, l = Io("start", "zoom", "end"), f, p, d, h = 500, g = 150, N = 0, w = 10;
  function m(E) {
    E.property("__zoom", ua).on("wheel.zoom", A, { passive: !1 }).on("mousedown.zoom", M).on("dblclick.zoom", R).filter(o).on("touchstart.zoom", $).on("touchmove.zoom", _).on("touchend.zoom touchcancel.zoom", V).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  m.transform = function(E, D, k, T) {
    var L = E.selection ? E.selection() : E;
    L.property("__zoom", ua), E !== L ? j(E, D, k, T) : L.interrupt().each(function() {
      v(this, arguments).event(T).start().zoom(null, typeof D == "function" ? D.apply(this, arguments) : D).end();
    });
  }, m.scaleBy = function(E, D, k, T) {
    m.scaleTo(E, function() {
      var L = this.__zoom.k, P = typeof D == "function" ? D.apply(this, arguments) : D;
      return L * P;
    }, k, T);
  }, m.scaleTo = function(E, D, k, T) {
    m.transform(E, function() {
      var L = t.apply(this, arguments), P = this.__zoom, K = k == null ? b(L) : typeof k == "function" ? k.apply(this, arguments) : k, F = P.invert(K), B = typeof D == "function" ? D.apply(this, arguments) : D;
      return n(y(C(P, B), K, F), L, a);
    }, k, T);
  }, m.translateBy = function(E, D, k, T) {
    m.transform(E, function() {
      return n(this.__zoom.translate(
        typeof D == "function" ? D.apply(this, arguments) : D,
        typeof k == "function" ? k.apply(this, arguments) : k
      ), t.apply(this, arguments), a);
    }, null, T);
  }, m.translateTo = function(E, D, k, T, L) {
    m.transform(E, function() {
      var P = t.apply(this, arguments), K = this.__zoom, F = T == null ? b(P) : typeof T == "function" ? T.apply(this, arguments) : T;
      return n(Do.translate(F[0], F[1]).scale(K.k).translate(
        typeof D == "function" ? -D.apply(this, arguments) : -D,
        typeof k == "function" ? -k.apply(this, arguments) : -k
      ), P, a);
    }, T, L);
  };
  function C(E, D) {
    return D = Math.max(s[0], Math.min(s[1], D)), D === E.k ? E : new ft(D, E.x, E.y);
  }
  function y(E, D, k) {
    var T = D[0] - k[0] * E.k, L = D[1] - k[1] * E.k;
    return T === E.x && L === E.y ? E : new ft(E.k, T, L);
  }
  function b(E) {
    return [(+E[0][0] + +E[1][0]) / 2, (+E[0][1] + +E[1][1]) / 2];
  }
  function j(E, D, k, T) {
    E.on("start.zoom", function() {
      v(this, arguments).event(T).start();
    }).on("interrupt.zoom end.zoom", function() {
      v(this, arguments).event(T).end();
    }).tween("zoom", function() {
      var L = this, P = arguments, K = v(L, P).event(T), F = t.apply(L, P), B = k == null ? b(F) : typeof k == "function" ? k.apply(L, P) : k, q = Math.max(F[1][0] - F[0][0], F[1][1] - F[0][1]), J = L.__zoom, X = typeof D == "function" ? D.apply(L, P) : D, ce = u(J.invert(B).concat(q / J.k), X.invert(B).concat(q / X.k));
      return function(G) {
        if (G === 1) G = X;
        else {
          var O = ce(G), Y = q / O[2];
          G = new ft(Y, B[0] - O[0] * Y, B[1] - O[1] * Y);
        }
        K.zoom(null, G);
      };
    });
  }
  function v(E, D, k) {
    return !k && E.__zooming || new S(E, D);
  }
  function S(E, D) {
    this.that = E, this.args = D, this.active = 0, this.sourceEvent = null, this.extent = t.apply(E, D), this.taps = 0;
  }
  S.prototype = {
    event: function(E) {
      return E && (this.sourceEvent = E), this;
    },
    start: function() {
      return ++this.active === 1 && (this.that.__zooming = this, this.emit("start")), this;
    },
    zoom: function(E, D) {
      return this.mouse && E !== "mouse" && (this.mouse[1] = D.invert(this.mouse[0])), this.touch0 && E !== "touch" && (this.touch0[1] = D.invert(this.touch0[0])), this.touch1 && E !== "touch" && (this.touch1[1] = D.invert(this.touch1[0])), this.that.__zoom = D, this.emit("zoom"), this;
    },
    end: function() {
      return --this.active === 0 && (delete this.that.__zooming, this.emit("end")), this;
    },
    emit: function(E) {
      var D = Ue(this.that).datum();
      l.call(
        E,
        this.that,
        new cy(E, {
          sourceEvent: this.sourceEvent,
          target: m,
          transform: this.that.__zoom,
          dispatch: l
        }),
        D
      );
    }
  };
  function A(E, ...D) {
    if (!e.apply(this, arguments)) return;
    var k = v(this, D).event(E), T = this.__zoom, L = Math.max(s[0], Math.min(s[1], T.k * Math.pow(2, r.apply(this, arguments)))), P = Ge(E);
    if (k.wheel)
      (k.mouse[0][0] !== P[0] || k.mouse[0][1] !== P[1]) && (k.mouse[1] = T.invert(k.mouse[0] = P)), clearTimeout(k.wheel);
    else {
      if (T.k === L) return;
      k.mouse = [P, T.invert(P)], Zr(this), k.start();
    }
    Vn(E), k.wheel = setTimeout(K, g), k.zoom("mouse", n(y(C(T, L), k.mouse[0], k.mouse[1]), k.extent, a));
    function K() {
      k.wheel = null, k.end();
    }
  }
  function M(E, ...D) {
    if (d || !e.apply(this, arguments)) return;
    var k = E.currentTarget, T = v(this, D, !0).event(E), L = Ue(E.view).on("mousemove.zoom", B, !0).on("mouseup.zoom", q, !0), P = Ge(E, k), K = E.clientX, F = E.clientY;
    ul(E.view), ai(E), T.mouse = [P, this.__zoom.invert(P)], Zr(this), T.start();
    function B(J) {
      if (Vn(J), !T.moved) {
        var X = J.clientX - K, ce = J.clientY - F;
        T.moved = X * X + ce * ce > N;
      }
      T.event(J).zoom("mouse", n(y(T.that.__zoom, T.mouse[0] = Ge(J, k), T.mouse[1]), T.extent, a));
    }
    function q(J) {
      L.on("mousemove.zoom mouseup.zoom", null), dl(J.view, T.moved), Vn(J), T.event(J).end();
    }
  }
  function R(E, ...D) {
    if (e.apply(this, arguments)) {
      var k = this.__zoom, T = Ge(E.changedTouches ? E.changedTouches[0] : E, this), L = k.invert(T), P = k.k * (E.shiftKey ? 0.5 : 2), K = n(y(C(k, P), T, L), t.apply(this, D), a);
      Vn(E), c > 0 ? Ue(this).transition().duration(c).call(j, K, T, E) : Ue(this).call(m.transform, K, T, E);
    }
  }
  function $(E, ...D) {
    if (e.apply(this, arguments)) {
      var k = E.touches, T = k.length, L = v(this, D, E.changedTouches.length === T).event(E), P, K, F, B;
      for (ai(E), K = 0; K < T; ++K)
        F = k[K], B = Ge(F, this), B = [B, this.__zoom.invert(B), F.identifier], L.touch0 ? !L.touch1 && L.touch0[2] !== B[2] && (L.touch1 = B, L.taps = 0) : (L.touch0 = B, P = !0, L.taps = 1 + !!f);
      f && (f = clearTimeout(f)), P && (L.taps < 2 && (p = B[0], f = setTimeout(function() {
        f = null;
      }, h)), Zr(this), L.start());
    }
  }
  function _(E, ...D) {
    if (this.__zooming) {
      var k = v(this, D).event(E), T = E.changedTouches, L = T.length, P, K, F, B;
      for (Vn(E), P = 0; P < L; ++P)
        K = T[P], F = Ge(K, this), k.touch0 && k.touch0[2] === K.identifier ? k.touch0[0] = F : k.touch1 && k.touch1[2] === K.identifier && (k.touch1[0] = F);
      if (K = k.that.__zoom, k.touch1) {
        var q = k.touch0[0], J = k.touch0[1], X = k.touch1[0], ce = k.touch1[1], G = (G = X[0] - q[0]) * G + (G = X[1] - q[1]) * G, O = (O = ce[0] - J[0]) * O + (O = ce[1] - J[1]) * O;
        K = C(K, Math.sqrt(G / O)), F = [(q[0] + X[0]) / 2, (q[1] + X[1]) / 2], B = [(J[0] + ce[0]) / 2, (J[1] + ce[1]) / 2];
      } else if (k.touch0) F = k.touch0[0], B = k.touch0[1];
      else return;
      k.zoom("touch", n(y(K, F, B), k.extent, a));
    }
  }
  function V(E, ...D) {
    if (this.__zooming) {
      var k = v(this, D).event(E), T = E.changedTouches, L = T.length, P, K;
      for (ai(E), d && clearTimeout(d), d = setTimeout(function() {
        d = null;
      }, h), P = 0; P < L; ++P)
        K = T[P], k.touch0 && k.touch0[2] === K.identifier ? delete k.touch0 : k.touch1 && k.touch1[2] === K.identifier && delete k.touch1;
      if (k.touch1 && !k.touch0 && (k.touch0 = k.touch1, delete k.touch1), k.touch0) k.touch0[1] = this.__zoom.invert(k.touch0[0]);
      else if (k.end(), k.taps === 2 && (K = Ge(K, this), Math.hypot(p[0] - K[0], p[1] - K[1]) < w)) {
        var F = Ue(this).on("dblclick.zoom");
        F && F.apply(this, arguments);
      }
    }
  }
  return m.wheelDelta = function(E) {
    return arguments.length ? (r = typeof E == "function" ? E : Vr(+E), m) : r;
  }, m.filter = function(E) {
    return arguments.length ? (e = typeof E == "function" ? E : Vr(!!E), m) : e;
  }, m.touchable = function(E) {
    return arguments.length ? (o = typeof E == "function" ? E : Vr(!!E), m) : o;
  }, m.extent = function(E) {
    return arguments.length ? (t = typeof E == "function" ? E : Vr([[+E[0][0], +E[0][1]], [+E[1][0], +E[1][1]]]), m) : t;
  }, m.scaleExtent = function(E) {
    return arguments.length ? (s[0] = +E[0], s[1] = +E[1], m) : [s[0], s[1]];
  }, m.translateExtent = function(E) {
    return arguments.length ? (a[0][0] = +E[0][0], a[1][0] = +E[1][0], a[0][1] = +E[0][1], a[1][1] = +E[1][1], m) : [[a[0][0], a[0][1]], [a[1][0], a[1][1]]];
  }, m.constrain = function(E) {
    return arguments.length ? (n = E, m) : n;
  }, m.duration = function(E) {
    return arguments.length ? (c = +E, m) : c;
  }, m.interpolate = function(E) {
    return arguments.length ? (u = E, m) : u;
  }, m.on = function() {
    var E = l.on.apply(l, arguments);
    return E === l ? m : E;
  }, m.clickDistance = function(E) {
    return arguments.length ? (N = (E = +E) * E, m) : Math.sqrt(N);
  }, m.tapDistance = function(E) {
    return arguments.length ? (w = +E, m) : w;
  }, m;
}
const qe = {
  error001: (e = "react") => `Seems like you have not used zustand provider as an ancestor. Help: https://${e}flow.dev/error#001`,
  error002: () => "It looks like you've created a new nodeTypes or edgeTypes object. If this wasn't on purpose please define the nodeTypes/edgeTypes outside of the component or memoize them.",
  error003: (e) => `Node type "${e}" not found. Using fallback type "default".`,
  error004: () => "The parent container needs a width and a height to render the graph.",
  error005: () => "Only child nodes can use a parent extent.",
  error006: () => "Can't create edge. An edge needs a source and a target.",
  error007: (e) => `The old edge with id=${e} does not exist.`,
  error009: (e) => `Marker type "${e}" doesn't exist.`,
  error008: (e, { id: t, sourceHandle: n, targetHandle: r }) => `Couldn't create edge for ${e} handle id: "${e === "source" ? n : r}", edge id: ${t}.`,
  error010: () => "Handle: No node id found. Make sure to only use a Handle inside a custom Node.",
  error011: (e) => `Edge type "${e}" not found. Using fallback type "default".`,
  error012: (e) => `Node with id "${e}" does not exist, it may have been removed. This can happen when a node is deleted before the "onNodeClick" handler is called.`,
  error013: (e = "react") => `It seems that you haven't loaded the styles. Please import '@xyflow/${e}/dist/style.css' or base.css to make sure everything is working properly.`,
  error014: () => "useNodeConnections: No node ID found. Call useNodeConnections inside a custom Node or provide a node ID.",
  error015: () => "It seems that you are trying to drag a node that is not initialized. Please use onNodesChange as explained in the docs.",
  error016: (e) => `Edge with id "${e}" does not exist, it may have been removed. This can happen when an edge is deleted before the "onEdgeClick" handler is called.`
}, ir = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], kl = ["Enter", " ", "Escape"], Al = {
  "node.a11yDescription.default": "Press enter or space to select a node. Press delete to remove it and escape to cancel.",
  "node.a11yDescription.keyboardDisabled": "Press enter or space to select a node. You can then use the arrow keys to move the node around. Press delete to remove it and escape to cancel.",
  "node.a11yDescription.ariaLiveMessage": ({ direction: e, x: t, y: n }) => `Moved selected node ${e}. New position, x: ${t}, y: ${n}`,
  "edge.a11yDescription.default": "Press enter or space to select an edge. You can then press delete to remove it or escape to cancel.",
  // Control elements
  "controls.ariaLabel": "Control Panel",
  "controls.zoomIn.ariaLabel": "Zoom In",
  "controls.zoomOut.ariaLabel": "Zoom Out",
  "controls.fitView.ariaLabel": "Fit View",
  "controls.interactive.ariaLabel": "Toggle Interactivity",
  // Mini map
  "minimap.ariaLabel": "Mini Map",
  // Handle
  "handle.ariaLabel": "Handle"
};
var gn;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(gn || (gn = {}));
var Pt;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(Pt || (Pt = {}));
var sr;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(sr || (sr = {}));
const _l = {
  inProgress: !1,
  isValid: null,
  from: null,
  fromHandle: null,
  fromPosition: null,
  fromNode: null,
  to: null,
  toHandle: null,
  toPosition: null,
  toNode: null,
  pointer: null
};
var bt;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(bt || (bt = {}));
var ho;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(ho || (ho = {}));
var ne;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(ne || (ne = {}));
const da = {
  [ne.Left]: ne.Right,
  [ne.Right]: ne.Left,
  [ne.Top]: ne.Bottom,
  [ne.Bottom]: ne.Top
};
function Dl(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Tl = (e) => "id" in e && "source" in e && "target" in e, hy = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), os = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), hr = (e, t = [0, 0]) => {
  const { width: n, height: r } = gt(e), o = e.origin ?? t, s = n * o[0], a = r * o[1];
  return {
    x: e.position.x - s,
    y: e.position.y - a
  };
}, gy = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((r, o) => {
    const s = typeof o == "string";
    let a = !t.nodeLookup && !s ? o : void 0;
    t.nodeLookup && (a = s ? t.nodeLookup.get(o) : os(o) ? o : t.nodeLookup.get(o.id));
    const c = a ? go(a, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return To(r, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return $o(n);
}, gr = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, r = !1;
  return e.forEach((o) => {
    (t.filter === void 0 || t.filter(o)) && (n = To(n, go(o)), r = !0);
  }), r ? $o(n) : { x: 0, y: 0, width: 0, height: 0 };
}, is = (e, t, [n, r, o] = [0, 0, 1], s = !1, a = !1) => {
  const c = {
    ...Nn(t, [n, r, o]),
    width: t.width / o,
    height: t.height / o
  }, u = [];
  for (const l of e.values()) {
    const { measured: f, selectable: p = !0, hidden: d = !1 } = l;
    if (a && !p || d)
      continue;
    const h = f.width ?? l.width ?? l.initialWidth ?? null, g = f.height ?? l.height ?? l.initialHeight ?? null, N = ar(c, mn(l)), w = (h ?? 0) * (g ?? 0), m = s && N > 0;
    (!l.internals.handleBounds || m || N >= w || l.dragging) && u.push(l);
  }
  return u;
}, yy = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((r) => {
    n.add(r.id);
  }), t.filter((r) => n.has(r.source) || n.has(r.target));
};
function my(e, t) {
  const n = /* @__PURE__ */ new Map(), r = t?.nodes ? new Set(t.nodes.map((o) => o.id)) : null;
  return e.forEach((o) => {
    o.measured.width && o.measured.height && (t?.includeHiddenNodes || !o.hidden) && (!r || r.has(o.id)) && n.set(o.id, o);
  }), n;
}
async function xy({ nodes: e, width: t, height: n, panZoom: r, minZoom: o, maxZoom: s }, a) {
  if (e.size === 0)
    return !0;
  const c = my(e, a), u = gr(c), l = as(u, t, n, a?.minZoom ?? o, a?.maxZoom ?? s, a?.padding ?? 0.1);
  return await r.setViewport(l, {
    duration: a?.duration,
    ease: a?.ease,
    interpolate: a?.interpolate
  }), !0;
}
function $l({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: r = [0, 0], nodeExtent: o, onError: s }) {
  const a = n.get(e), c = a.parentId ? n.get(a.parentId) : void 0, { x: u, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, f = a.origin ?? r;
  let p = a.extent || o;
  if (a.extent === "parent" && !a.expandParent)
    if (!c)
      s?.("005", qe.error005());
    else {
      const h = c.measured.width, g = c.measured.height;
      h && g && (p = [
        [u, l],
        [u + h, l + g]
      ]);
    }
  else c && Ot(a.extent) && (p = [
    [a.extent[0][0] + u, a.extent[0][1] + l],
    [a.extent[1][0] + u, a.extent[1][1] + l]
  ]);
  const d = Ot(p) ? Vt(t, p, a.measured) : t;
  return (a.measured.width === void 0 || a.measured.height === void 0) && s?.("015", qe.error015()), {
    position: {
      x: d.x - u + (a.measured.width ?? 0) * f[0],
      y: d.y - l + (a.measured.height ?? 0) * f[1]
    },
    positionAbsolute: d
  };
}
async function wy({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: r, onBeforeDelete: o }) {
  const s = new Set(e.map((d) => d.id)), a = [];
  for (const d of n) {
    if (d.deletable === !1)
      continue;
    const h = s.has(d.id), g = !h && d.parentId && a.find((N) => N.id === d.parentId);
    (h || g) && a.push(d);
  }
  const c = new Set(t.map((d) => d.id)), u = r.filter((d) => d.deletable !== !1), f = yy(a, u);
  for (const d of u)
    c.has(d.id) && !f.find((g) => g.id === d.id) && f.push(d);
  if (!o)
    return {
      edges: f,
      nodes: a
    };
  const p = await o({
    nodes: a,
    edges: f
  });
  return typeof p == "boolean" ? p ? { edges: f, nodes: a } : { edges: [], nodes: [] } : p;
}
const yn = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), Vt = (e = { x: 0, y: 0 }, t, n) => ({
  x: yn(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: yn(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Ml(e, t, n) {
  const { width: r, height: o } = gt(n), { x: s, y: a } = n.internals.positionAbsolute;
  return Vt(e, [
    [s, a],
    [s + r, a + o]
  ], t);
}
const fa = (e, t, n) => e < t ? yn(Math.abs(e - t), 1, t) / t : e > n ? -yn(Math.abs(e - n), 1, t) / t : 0, ss = (e, t, n = 15, r = 40) => {
  const o = fa(e.x, r, t.width - r) * n, s = fa(e.y, r, t.height - r) * n;
  return [o, s];
}, To = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), _i = ({ x: e, y: t, width: n, height: r }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + r
}), $o = ({ x: e, y: t, x2: n, y2: r }) => ({
  x: e,
  y: t,
  width: n - e,
  height: r - t
}), mn = (e, t = [0, 0]) => {
  const { x: n, y: r } = os(e) ? e.internals.positionAbsolute : hr(e, t);
  return {
    x: n,
    y: r,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, go = (e, t = [0, 0]) => {
  const { x: n, y: r } = os(e) ? e.internals.positionAbsolute : hr(e, t);
  return {
    x: n,
    y: r,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: r + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, Pl = (e, t) => $o(To(_i(e), _i(t))), ar = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), r = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * r);
}, pa = (e) => Qe(e.width) && Qe(e.height) && Qe(e.x) && Qe(e.y), Qe = (e) => !isNaN(e) && isFinite(e), Rl = (e, t) => (n, r) => {
}, yr = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), Nn = ({ x: e, y: t }, [n, r, o], s = !1, a = [1, 1]) => {
  const c = {
    x: (e - n) / o,
    y: (t - r) / o
  };
  return s ? yr(c, a) : c;
}, xn = ({ x: e, y: t }, [n, r, o]) => ({
  x: e * o + n,
  y: t * o + r
});
function rn(e, t) {
  if (typeof e == "number")
    return Math.floor((t - t / (1 + e)) * 0.5);
  if (typeof e == "string" && e.endsWith("px")) {
    const n = parseFloat(e);
    if (!Number.isNaN(n))
      return Math.floor(n);
  }
  if (typeof e == "string" && e.endsWith("%")) {
    const n = parseFloat(e);
    if (!Number.isNaN(n))
      return Math.floor(t * n * 0.01);
  }
  return console.error(`The padding value "${e}" is invalid. Please provide a number or a string with a valid unit (px or %).`), 0;
}
function vy(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const r = rn(e, n), o = rn(e, t);
    return {
      top: r,
      right: o,
      bottom: r,
      left: o,
      x: o * 2,
      y: r * 2
    };
  }
  if (typeof e == "object") {
    const r = rn(e.top ?? e.y ?? 0, n), o = rn(e.bottom ?? e.y ?? 0, n), s = rn(e.left ?? e.x ?? 0, t), a = rn(e.right ?? e.x ?? 0, t);
    return { top: r, right: a, bottom: o, left: s, x: s + a, y: r + o };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function by(e, t, n, r, o, s) {
  const { x: a, y: c } = xn(e, [t, n, r]), { x: u, y: l } = xn({ x: e.x + e.width, y: e.y + e.height }, [t, n, r]), f = o - u, p = s - l;
  return {
    left: Math.floor(a),
    top: Math.floor(c),
    right: Math.floor(f),
    bottom: Math.floor(p)
  };
}
const as = (e, t, n, r, o, s) => {
  const a = vy(s, t, n), c = (t - a.x) / e.width, u = (n - a.y) / e.height, l = Math.min(c, u), f = yn(l, r, o), p = e.x + e.width / 2, d = e.y + e.height / 2, h = t / 2 - p * f, g = n / 2 - d * f, N = by(e, h, g, f, t, n), w = {
    left: Math.min(N.left - a.left, 0),
    top: Math.min(N.top - a.top, 0),
    right: Math.min(N.right - a.right, 0),
    bottom: Math.min(N.bottom - a.bottom, 0)
  };
  return {
    x: h - w.left + w.right,
    y: g - w.top + w.bottom,
    zoom: f
  };
}, cr = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function Ot(e) {
  return e != null && e !== "parent";
}
function gt(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function Ll(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function zl(e, t = { width: 0, height: 0 }, n, r, o) {
  const s = { ...e }, a = r.get(n);
  if (a) {
    const c = a.origin || o;
    s.x += a.internals.positionAbsolute.x - (t.width ?? 0) * c[0], s.y += a.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return s;
}
function ha(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function Ny() {
  let e, t;
  return { promise: new Promise((r, o) => {
    e = r, t = o;
  }), resolve: e, reject: t };
}
function jy(e) {
  return { ...Al, ...e || {} };
}
function Zn(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: r, containerBounds: o }) {
  const { x: s, y: a } = et(e), c = Nn({ x: s - (o?.left ?? 0), y: a - (o?.top ?? 0) }, r), { x: u, y: l } = n ? yr(c, t) : c;
  return {
    xSnapped: u,
    ySnapped: l,
    ...c
  };
}
const cs = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), Vl = (e) => e?.getRootNode?.() || window?.document, Sy = ["INPUT", "SELECT", "TEXTAREA"];
function Ol(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : Sy.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const Hl = (e) => "clientX" in e, et = (e, t) => {
  const n = Hl(e), r = n ? e.clientX : e.touches?.[0].clientX, o = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: r - (t?.left ?? 0),
    y: o - (t?.top ?? 0)
  };
}, ga = (e, t, n, r, o) => {
  const s = t.querySelectorAll(`.${e}`);
  return !s || !s.length ? null : Array.from(s).map((a) => {
    const c = a.getBoundingClientRect();
    return {
      id: a.getAttribute("data-handleid"),
      type: e,
      nodeId: o,
      position: a.getAttribute("data-handlepos"),
      x: (c.left - n.left) / r,
      y: (c.top - n.top) / r,
      ...cs(a)
    };
  });
};
function Wl({ sourceX: e, sourceY: t, targetX: n, targetY: r, sourceControlX: o, sourceControlY: s, targetControlX: a, targetControlY: c }) {
  const u = e * 0.125 + o * 0.375 + a * 0.375 + n * 0.125, l = t * 0.125 + s * 0.375 + c * 0.375 + r * 0.125, f = Math.abs(u - e), p = Math.abs(l - t);
  return [u, l, f, p];
}
function Or(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function ya({ pos: e, x1: t, y1: n, x2: r, y2: o, c: s }) {
  switch (e) {
    case ne.Left:
      return [t - Or(t - r, s), n];
    case ne.Right:
      return [t + Or(r - t, s), n];
    case ne.Top:
      return [t, n - Or(n - o, s)];
    case ne.Bottom:
      return [t, n + Or(o - n, s)];
  }
}
function Fl({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: r, targetY: o, targetPosition: s = ne.Top, curvature: a = 0.25 }) {
  const [c, u] = ya({
    pos: n,
    x1: e,
    y1: t,
    x2: r,
    y2: o,
    c: a
  }), [l, f] = ya({
    pos: s,
    x1: r,
    y1: o,
    x2: e,
    y2: t,
    c: a
  }), [p, d, h, g] = Wl({
    sourceX: e,
    sourceY: t,
    targetX: r,
    targetY: o,
    sourceControlX: c,
    sourceControlY: u,
    targetControlX: l,
    targetControlY: f
  });
  return [
    `M${e},${t} C${c},${u} ${l},${f} ${r},${o}`,
    p,
    d,
    h,
    g
  ];
}
function Bl({ sourceX: e, sourceY: t, targetX: n, targetY: r }) {
  const o = Math.abs(n - e) / 2, s = n < e ? n + o : n - o, a = Math.abs(r - t) / 2, c = r < t ? r + a : r - a;
  return [s, c, o, a];
}
function Ey({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: r = 0, elevateOnSelect: o = !1, zIndexMode: s = "basic" }) {
  if (s === "manual")
    return r;
  const a = o && n ? r + 1e3 : r, c = Math.max(e.parentId || o && e.selected ? e.internals.z : 0, t.parentId || o && t.selected ? t.internals.z : 0);
  return a + c;
}
function Cy({ sourceNode: e, targetNode: t, width: n, height: r, transform: o }) {
  const s = To(go(e), go(t));
  s.x === s.x2 && (s.x2 += 1), s.y === s.y2 && (s.y2 += 1);
  const a = {
    x: -o[0] / o[2],
    y: -o[1] / o[2],
    width: n / o[2],
    height: r / o[2]
  };
  return ar(a, $o(s)) > 0;
}
const Kl = ({ source: e, sourceHandle: t, target: n, targetHandle: r }) => `xy-edge__${e}${t || ""}-${n}${r || ""}`, Iy = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), ky = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", qe.error006()), t;
  const r = n.getEdgeId || Kl;
  let o;
  return Tl(e) ? o = { ...e } : o = {
    ...e,
    id: r(e)
  }, Iy(o, t) ? t : (o.sourceHandle === null && delete o.sourceHandle, o.targetHandle === null && delete o.targetHandle, t.concat(o));
}, Ay = (e, t, n, r = { shouldReplaceId: !0 }) => {
  const { id: o, ...s } = e;
  if (!t.source || !t.target)
    return r.onError?.("006", qe.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return r.onError?.("007", qe.error007(o)), n;
  const c = r.getEdgeId || Kl, u = {
    ...s,
    id: r.shouldReplaceId ? c(t) : o,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== o).concat(u);
};
function Ul({ sourceX: e, sourceY: t, targetX: n, targetY: r }) {
  const [o, s, a, c] = Bl({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: r
  });
  return [`M ${e},${t}L ${n},${r}`, o, s, a, c];
}
const ma = {
  [ne.Left]: { x: -1, y: 0 },
  [ne.Right]: { x: 1, y: 0 },
  [ne.Top]: { x: 0, y: -1 },
  [ne.Bottom]: { x: 0, y: 1 }
}, _y = ({ source: e, sourcePosition: t = ne.Bottom, target: n }) => t === ne.Left || t === ne.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, xa = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Dy({ source: e, sourcePosition: t = ne.Bottom, target: n, targetPosition: r = ne.Top, center: o, offset: s, stepPosition: a }) {
  const c = ma[t], u = ma[r], l = { x: e.x + c.x * s, y: e.y + c.y * s }, f = { x: n.x + u.x * s, y: n.y + u.y * s }, p = _y({
    source: l,
    sourcePosition: t,
    target: f
  }), d = p.x !== 0 ? "x" : "y", h = p[d];
  let g = [], N, w;
  const m = { x: 0, y: 0 }, C = { x: 0, y: 0 }, [, , y, b] = Bl({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[d] * u[d] === -1) {
    d === "x" ? (N = o.x ?? l.x + (f.x - l.x) * a, w = o.y ?? (l.y + f.y) / 2) : (N = o.x ?? (l.x + f.x) / 2, w = o.y ?? l.y + (f.y - l.y) * a);
    const A = [
      { x: N, y: l.y },
      { x: N, y: f.y }
    ], M = [
      { x: l.x, y: w },
      { x: f.x, y: w }
    ];
    c[d] === h ? g = d === "x" ? A : M : g = d === "x" ? M : A;
  } else {
    const A = [{ x: l.x, y: f.y }], M = [{ x: f.x, y: l.y }];
    if (d === "x" ? g = c.x === h ? M : A : g = c.y === h ? A : M, t === r) {
      const E = Math.abs(e[d] - n[d]);
      if (E <= s) {
        const D = Math.min(s - 1, s - E);
        c[d] === h ? m[d] = (l[d] > e[d] ? -1 : 1) * D : C[d] = (f[d] > n[d] ? -1 : 1) * D;
      }
    }
    if (t !== r) {
      const E = d === "x" ? "y" : "x", D = c[d] === u[E], k = l[E] > f[E], T = l[E] < f[E];
      (c[d] === 1 && (!D && k || D && T) || c[d] !== 1 && (!D && T || D && k)) && (g = d === "x" ? A : M);
    }
    const R = { x: l.x + m.x, y: l.y + m.y }, $ = { x: f.x + C.x, y: f.y + C.y }, _ = Math.max(Math.abs(R.x - g[0].x), Math.abs($.x - g[0].x)), V = Math.max(Math.abs(R.y - g[0].y), Math.abs($.y - g[0].y));
    _ >= V ? (N = (R.x + $.x) / 2, w = g[0].y) : (N = g[0].x, w = (R.y + $.y) / 2);
  }
  const j = { x: l.x + m.x, y: l.y + m.y }, v = { x: f.x + C.x, y: f.y + C.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...j.x !== g[0].x || j.y !== g[0].y ? [j] : [],
    ...g,
    ...v.x !== g[g.length - 1].x || v.y !== g[g.length - 1].y ? [v] : [],
    n
  ], N, w, y, b];
}
function Ty(e, t, n, r) {
  const o = Math.min(xa(e, t) / 2, xa(t, n) / 2, r), { x: s, y: a } = t;
  if (e.x === s && s === n.x || e.y === a && a === n.y)
    return `L${s} ${a}`;
  if (e.y === a) {
    const l = e.x < n.x ? -1 : 1, f = e.y < n.y ? 1 : -1;
    return `L ${s + o * l},${a}Q ${s},${a} ${s},${a + o * f}`;
  }
  const c = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${s},${a + o * u}Q ${s},${a} ${s + o * c},${a}`;
}
function yo({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: r, targetY: o, targetPosition: s = ne.Top, borderRadius: a = 5, centerX: c, centerY: u, offset: l = 20, stepPosition: f = 0.5 }) {
  const [p, d, h, g, N] = Dy({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: r, y: o },
    targetPosition: s,
    center: { x: c, y: u },
    offset: l,
    stepPosition: f
  });
  let w = `M${p[0].x} ${p[0].y}`;
  for (let m = 1; m < p.length - 1; m++)
    w += Ty(p[m - 1], p[m], p[m + 1], a);
  return w += `L${p[p.length - 1].x} ${p[p.length - 1].y}`, [w, d, h, g, N];
}
function wa(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function $y(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!wa(t) || !wa(n))
    return null;
  const r = t.internals.handleBounds || va(t.handles), o = n.internals.handleBounds || va(n.handles), s = ba(r?.source ?? [], e.sourceHandle), a = ba(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === gn.Strict ? o?.target ?? [] : (o?.target ?? []).concat(o?.source ?? []),
    e.targetHandle
  );
  if (!s || !a)
    return e.onError?.("008", qe.error008(s ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = s?.position || ne.Bottom, u = a?.position || ne.Top, l = Ht(t, s, c), f = Ht(n, a, u);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: f.x,
    targetY: f.y,
    sourcePosition: c,
    targetPosition: u
  };
}
function va(e) {
  if (!e)
    return null;
  const t = [], n = [];
  for (const r of e)
    r.width = r.width ?? 1, r.height = r.height ?? 1, r.type === "source" ? t.push(r) : r.type === "target" && n.push(r);
  return {
    source: t,
    target: n
  };
}
function Ht(e, t, n = ne.Left, r = !1) {
  const o = (t?.x ?? 0) + e.internals.positionAbsolute.x, s = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: a, height: c } = t ?? gt(e);
  if (r)
    return { x: o + a / 2, y: s + c / 2 };
  switch (t?.position ?? n) {
    case ne.Top:
      return { x: o + a / 2, y: s };
    case ne.Right:
      return { x: o + a, y: s + c / 2 };
    case ne.Bottom:
      return { x: o + a / 2, y: s + c };
    case ne.Left:
      return { x: o, y: s + c / 2 };
  }
}
function ba(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Di(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((r) => `${r}=${e[r]}`).join("&")}` : "";
}
function My(e, { id: t, defaultColor: n, defaultMarkerStart: r, defaultMarkerEnd: o }) {
  const s = /* @__PURE__ */ new Set();
  return e.reduce((a, c) => ([c.markerStart || r, c.markerEnd || o].forEach((u) => {
    if (u && typeof u == "object") {
      const l = Di(u, t);
      s.has(l) || (a.push({ id: l, color: u.color || n, ...u }), s.add(l));
    }
  }), a), []).sort((a, c) => a.id.localeCompare(c.id));
}
const Xl = 1e3, Py = 10, ls = {
  nodeOrigin: [0, 0],
  nodeExtent: ir,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Ry = {
  ...ls,
  checkEquality: !0
};
function us(e, t) {
  const n = { ...e };
  for (const r in t)
    t[r] !== void 0 && (n[r] = t[r]);
  return n;
}
function Ly(e, t, n) {
  const r = us(ls, n);
  for (const o of e.values())
    if (o.parentId)
      fs(o, e, t, r);
    else {
      const s = hr(o, r.nodeOrigin), a = Ot(o.extent) ? o.extent : r.nodeExtent, c = Vt(s, a, gt(o));
      o.internals.positionAbsolute = c;
    }
}
function zy(e, t) {
  if (!e.handles)
    return e.measured ? t?.internals.handleBounds : void 0;
  const n = [], r = [];
  for (const o of e.handles) {
    const s = {
      id: o.id,
      width: o.width ?? 1,
      height: o.height ?? 1,
      nodeId: e.id,
      x: o.x,
      y: o.y,
      position: o.position,
      type: o.type
    };
    o.type === "source" ? n.push(s) : o.type === "target" && r.push(s);
  }
  return {
    source: n,
    target: r
  };
}
function ds(e) {
  return e === "manual";
}
function Ti(e, t, n, r = {}) {
  const o = us(Ry, r), s = { i: 0 }, a = new Map(t), c = o?.elevateNodesOnSelect && !ds(o.zIndexMode) ? Xl : 0;
  let u = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const f of e) {
    let p = a.get(f.id);
    if (o.checkEquality && f === p?.internals.userNode)
      t.set(f.id, p);
    else {
      const d = hr(f, o.nodeOrigin), h = Ot(f.extent) ? f.extent : o.nodeExtent, g = Vt(d, h, gt(f));
      p = {
        ...o.defaults,
        ...f,
        measured: {
          width: f.measured?.width,
          height: f.measured?.height
        },
        internals: {
          positionAbsolute: g,
          // if user re-initializes the node or removes `measured` for whatever reason, we reset the handleBounds so that the node gets re-measured
          handleBounds: zy(f, p),
          z: ql(f, c, o.zIndexMode),
          userNode: f
        }
      }, t.set(f.id, p);
    }
    (p.measured === void 0 || p.measured.width === void 0 || p.measured.height === void 0) && !p.hidden && (u = !1), f.parentId && fs(p, t, n, r, s), l ||= f.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: l };
}
function Vy(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function fs(e, t, n, r, o) {
  const { elevateNodesOnSelect: s, nodeOrigin: a, nodeExtent: c, zIndexMode: u } = us(ls, r), l = e.parentId, f = t.get(l);
  if (!f) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Vy(e, n), o && !f.parentId && f.internals.rootParentIndex === void 0 && u === "auto" && (f.internals.rootParentIndex = ++o.i, f.internals.z = f.internals.z + o.i * Py), o && f.internals.rootParentIndex !== void 0 && (o.i = f.internals.rootParentIndex);
  const p = s && !ds(u) ? Xl : 0, { x: d, y: h, z: g } = Oy(e, f, a, c, p, u), { positionAbsolute: N } = e.internals, w = d !== N.x || h !== N.y;
  (w || g !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: w ? { x: d, y: h } : N,
      z: g
    }
  });
}
function ql(e, t, n) {
  const r = Qe(e.zIndex) ? e.zIndex : 0;
  return ds(n) ? r : r + (e.selected ? t : 0);
}
function Oy(e, t, n, r, o, s) {
  const { x: a, y: c } = t.internals.positionAbsolute, u = gt(e), l = hr(e, n), f = Ot(e.extent) ? Vt(l, e.extent, u) : l;
  let p = Vt({ x: a + f.x, y: c + f.y }, r, u);
  e.extent === "parent" && (p = Ml(p, u, t));
  const d = ql(e, o, s), h = t.internals.z ?? 0;
  return {
    x: p.x,
    y: p.y,
    z: h >= d ? h + 1 : d
  };
}
function ps(e, t, n, r = [0, 0]) {
  const o = [], s = /* @__PURE__ */ new Map();
  for (const a of e) {
    const c = t.get(a.parentId);
    if (!c)
      continue;
    const u = s.get(a.parentId)?.expandedRect ?? mn(c), l = Pl(u, a.rect);
    s.set(a.parentId, { expandedRect: l, parent: c });
  }
  return s.size > 0 && s.forEach(({ expandedRect: a, parent: c }, u) => {
    const l = c.internals.positionAbsolute, f = gt(c), p = c.origin ?? r, d = a.x < l.x ? Math.round(Math.abs(l.x - a.x)) : 0, h = a.y < l.y ? Math.round(Math.abs(l.y - a.y)) : 0, g = Math.max(f.width, Math.round(a.width)), N = Math.max(f.height, Math.round(a.height)), w = (g - f.width) * p[0], m = (N - f.height) * p[1];
    (d > 0 || h > 0 || w || m) && (o.push({
      id: u,
      type: "position",
      position: {
        x: c.position.x - d + w,
        y: c.position.y - h + m
      }
    }), n.get(u)?.forEach((C) => {
      e.some((y) => y.id === C.id) || o.push({
        id: C.id,
        type: "position",
        position: {
          x: C.position.x + d,
          y: C.position.y + h
        }
      });
    })), (f.width < a.width || f.height < a.height || d || h) && o.push({
      id: u,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: g + (d ? p[0] * d - w : 0),
        height: N + (h ? p[1] * h - m : 0)
      }
    });
  }), o;
}
function Hy(e, t, n, r, o, s, a) {
  const c = r?.querySelector(".xyflow__viewport");
  let u = !1;
  if (!c)
    return { changes: [], updatedInternals: u };
  const l = [], f = window.getComputedStyle(c), { m22: p } = new window.DOMMatrixReadOnly(f.transform), d = [];
  for (const h of e.values()) {
    const g = t.get(h.id);
    if (!g)
      continue;
    if (g.hidden) {
      t.set(g.id, {
        ...g,
        internals: {
          ...g.internals,
          handleBounds: void 0
        }
      }), u = !0;
      continue;
    }
    const N = cs(h.nodeElement), w = g.measured.width !== N.width || g.measured.height !== N.height;
    if (!!(N.width && N.height && (w || !g.internals.handleBounds || h.force))) {
      const C = h.nodeElement.getBoundingClientRect(), y = Ot(g.extent) ? g.extent : s;
      let { positionAbsolute: b } = g.internals;
      g.parentId && g.extent === "parent" ? b = Ml(b, N, t.get(g.parentId)) : y && (b = Vt(b, y, N));
      const j = {
        ...g,
        measured: N,
        internals: {
          ...g.internals,
          positionAbsolute: b,
          handleBounds: {
            source: ga("source", h.nodeElement, C, p, g.id),
            target: ga("target", h.nodeElement, C, p, g.id)
          }
        }
      };
      t.set(g.id, j), g.parentId && fs(j, t, n, { nodeOrigin: o, zIndexMode: a }), u = !0, w && (l.push({
        id: g.id,
        type: "dimensions",
        dimensions: N
      }), g.expandParent && g.parentId && d.push({
        id: g.id,
        parentId: g.parentId,
        rect: mn(j, o)
      }));
    }
  }
  if (d.length > 0) {
    const h = ps(d, t, n, o);
    l.push(...h);
  }
  return { changes: l, updatedInternals: u };
}
async function Wy({ delta: e, panZoom: t, transform: n, translateExtent: r, width: o, height: s }) {
  if (!t || !e.x && !e.y)
    return !1;
  const a = await t.setViewportConstrained({
    x: n[0] + e.x,
    y: n[1] + e.y,
    zoom: n[2]
  }, [
    [0, 0],
    [o, s]
  ], r);
  return !!a && (a.x !== n[0] || a.y !== n[1] || a.k !== n[2]);
}
function Na(e, t, n, r, o, s) {
  let a = o;
  const c = r.get(a) || /* @__PURE__ */ new Map();
  r.set(a, c.set(n, t)), a = `${o}-${e}`;
  const u = r.get(a) || /* @__PURE__ */ new Map();
  if (r.set(a, u.set(n, t)), s) {
    a = `${o}-${e}-${s}`;
    const l = r.get(a) || /* @__PURE__ */ new Map();
    r.set(a, l.set(n, t));
  }
}
function Yl(e, t, n) {
  e.clear(), t.clear();
  for (const r of n) {
    const { source: o, target: s, sourceHandle: a = null, targetHandle: c = null } = r, u = { edgeId: r.id, source: o, target: s, sourceHandle: a, targetHandle: c }, l = `${o}-${a}--${s}-${c}`, f = `${s}-${c}--${o}-${a}`;
    Na("source", u, f, e, o, a), Na("target", u, l, e, s, c), t.set(r.id, r);
  }
}
function Zl(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : Zl(n, t) : !1;
}
function ja(e, t, n) {
  let r = e;
  do {
    if (r?.matches?.(t))
      return !0;
    if (r === n)
      return !1;
    r = r?.parentElement;
  } while (r);
  return !1;
}
function Fy(e, t, n, r) {
  const o = /* @__PURE__ */ new Map();
  for (const [s, a] of e)
    if ((a.selected || a.id === r) && (!a.parentId || !Zl(a, e)) && (a.draggable || t && typeof a.draggable > "u")) {
      const c = e.get(s);
      c && o.set(s, {
        id: s,
        position: c.position || { x: 0, y: 0 },
        distance: {
          x: n.x - c.internals.positionAbsolute.x,
          y: n.y - c.internals.positionAbsolute.y
        },
        extent: c.extent,
        parentId: c.parentId,
        origin: c.origin,
        expandParent: c.expandParent,
        internals: {
          positionAbsolute: c.internals.positionAbsolute || { x: 0, y: 0 }
        },
        measured: {
          width: c.measured.width ?? 0,
          height: c.measured.height ?? 0
        }
      });
    }
  return o;
}
function ci({ nodeId: e, dragItems: t, nodeLookup: n, dragging: r = !0 }) {
  const o = [];
  for (const [a, c] of t) {
    const u = n.get(a)?.internals.userNode;
    u && o.push({
      ...u,
      position: c.position,
      dragging: r
    });
  }
  if (!e)
    return [o[0], o];
  const s = n.get(e)?.internals.userNode;
  return [
    s ? {
      ...s,
      position: t.get(e)?.position || s.position,
      dragging: r
    } : o[0],
    o
  ];
}
function By({ dragItems: e, snapGrid: t, x: n, y: r }) {
  const o = e.values().next().value;
  if (!o)
    return null;
  const s = {
    x: n - o.distance.x,
    y: r - o.distance.y
  }, a = yr(s, t);
  return {
    x: a.x - s.x,
    y: a.y - s.y
  };
}
function Ky({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: r, onDragStop: o }) {
  let s = { x: null, y: null }, a = 0, c = /* @__PURE__ */ new Map(), u = !1, l = { x: 0, y: 0 }, f = null, p = !1, d = null, h = !1, g = !1, N = null;
  function w({ noDragClassName: C, handleSelector: y, domNode: b, isSelectable: j, nodeId: v, nodeClickDistance: S = 0 }) {
    d = Ue(b);
    function A({ x: _, y: V }) {
      const { nodeLookup: E, nodeExtent: D, snapGrid: k, snapToGrid: T, nodeOrigin: L, onNodeDrag: P, onSelectionDrag: K, onError: F, updateNodePositions: B } = t();
      s = { x: _, y: V };
      let q = !1;
      const J = c.size > 1, X = J && D ? _i(gr(c)) : null, ce = J && T ? By({
        dragItems: c,
        snapGrid: k,
        x: _,
        y: V
      }) : null;
      for (const [G, O] of c) {
        if (!E.has(G))
          continue;
        let Y = { x: _ - O.distance.x, y: V - O.distance.y };
        T && (Y = ce ? {
          x: Math.round(Y.x + ce.x),
          y: Math.round(Y.y + ce.y)
        } : yr(Y, k));
        let se = null;
        if (J && D && !O.extent && X) {
          const { positionAbsolute: re } = O.internals, fe = re.x - X.x + D[0][0], W = re.x + O.measured.width - X.x2 + D[1][0], ee = re.y - X.y + D[0][1], ye = re.y + O.measured.height - X.y2 + D[1][1];
          se = [
            [fe, ee],
            [W, ye]
          ];
        }
        const { position: ae, positionAbsolute: te } = $l({
          nodeId: G,
          nextPosition: Y,
          nodeLookup: E,
          nodeExtent: se || D,
          nodeOrigin: L,
          onError: F
        });
        q = q || O.position.x !== ae.x || O.position.y !== ae.y, O.position = ae, O.internals.positionAbsolute = te;
      }
      if (g = g || q, !!q && (B(c, !0), N && (r || P || !v && K))) {
        const [G, O] = ci({
          nodeId: v,
          dragItems: c,
          nodeLookup: E
        });
        r?.(N, c, G, O), P?.(N, G, O), v || K?.(N, O);
      }
    }
    async function M() {
      if (!f)
        return;
      const { transform: _, panBy: V, autoPanSpeed: E, autoPanOnNodeDrag: D } = t();
      if (!D) {
        u = !1, cancelAnimationFrame(a);
        return;
      }
      const [k, T] = ss(l, f, E);
      (k !== 0 || T !== 0) && (s.x = (s.x ?? 0) - k / _[2], s.y = (s.y ?? 0) - T / _[2], await V({ x: k, y: T }) && A(s)), a = requestAnimationFrame(M);
    }
    function R(_) {
      const { nodeLookup: V, multiSelectionActive: E, nodesDraggable: D, transform: k, snapGrid: T, snapToGrid: L, selectNodesOnDrag: P, onNodeDragStart: K, onSelectionDragStart: F, unselectNodesAndEdges: B } = t();
      p = !0, (!P || !j) && !E && v && (V.get(v)?.selected || B()), j && P && v && e?.(v);
      const q = Zn(_.sourceEvent, { transform: k, snapGrid: T, snapToGrid: L, containerBounds: f });
      if (s = q, c = Fy(V, D, q, v), c.size > 0 && (n || K || !v && F)) {
        const [J, X] = ci({
          nodeId: v,
          dragItems: c,
          nodeLookup: V
        });
        n?.(_.sourceEvent, c, J, X), K?.(_.sourceEvent, J, X), v || F?.(_.sourceEvent, X);
      }
    }
    const $ = fl().clickDistance(S).on("start", (_) => {
      const { domNode: V, nodeDragThreshold: E, transform: D, snapGrid: k, snapToGrid: T } = t();
      f = V?.getBoundingClientRect() || null, h = !1, g = !1, N = _.sourceEvent, E === 0 && R(_), s = Zn(_.sourceEvent, { transform: D, snapGrid: k, snapToGrid: T, containerBounds: f }), l = et(_.sourceEvent, f);
    }).on("drag", (_) => {
      const { autoPanOnNodeDrag: V, transform: E, snapGrid: D, snapToGrid: k, nodeDragThreshold: T, nodeLookup: L } = t(), P = Zn(_.sourceEvent, { transform: E, snapGrid: D, snapToGrid: k, containerBounds: f });
      if (N = _.sourceEvent, (_.sourceEvent.type === "touchmove" && _.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      v && !L.has(v)) && (h = !0), !h) {
        if (!u && V && p && (u = !0, M()), !p) {
          const K = et(_.sourceEvent, f), F = K.x - l.x, B = K.y - l.y;
          Math.sqrt(F * F + B * B) > T && R(_);
        }
        (s.x !== P.xSnapped || s.y !== P.ySnapped) && c && p && (l = et(_.sourceEvent, f), A(P));
      }
    }).on("end", (_) => {
      if (!p || h) {
        h && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (u = !1, p = !1, cancelAnimationFrame(a), c.size > 0) {
        const { nodeLookup: V, updateNodePositions: E, onNodeDragStop: D, onSelectionDragStop: k } = t();
        if (g && (E(c, !1), g = !1), o || D || !v && k) {
          const [T, L] = ci({
            nodeId: v,
            dragItems: c,
            nodeLookup: V,
            dragging: !1
          });
          o?.(_.sourceEvent, c, T, L), D?.(_.sourceEvent, T, L), v || k?.(_.sourceEvent, L);
        }
      }
    }).filter((_) => {
      const V = _.target;
      return !_.button && (!C || !ja(V, `.${C}`, b)) && (!y || ja(V, y, b));
    });
    d.call($);
  }
  function m() {
    d?.on(".drag", null);
  }
  return {
    update: w,
    destroy: m
  };
}
function Uy(e, t, n) {
  const r = [], o = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const s of t.values())
    ar(o, mn(s)) > 0 && r.push(s);
  return r;
}
const Xy = 250;
function qy(e, t, n, r) {
  let o = [], s = 1 / 0;
  const a = Uy(e, n, t + Xy);
  for (const c of a) {
    const u = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of u) {
      if (r.nodeId === l.nodeId && r.type === l.type && r.id === l.id)
        continue;
      const { x: f, y: p } = Ht(c, l, l.position, !0), d = Math.sqrt(Math.pow(f - e.x, 2) + Math.pow(p - e.y, 2));
      d > t || (d < s ? (o = [{ ...l, x: f, y: p }], s = d) : d === s && o.push({ ...l, x: f, y: p }));
    }
  }
  if (!o.length)
    return null;
  if (o.length > 1) {
    const c = r.type === "source" ? "target" : "source";
    return o.find((u) => u.type === c) ?? o[0];
  }
  return o[0];
}
function Gl(e, t, n, r, o, s = !1) {
  const a = r.get(e);
  if (!a)
    return null;
  const c = o === "strict" ? a.internals.handleBounds?.[t] : [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []], u = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return u && s ? { ...u, ...Ht(a, u, u.position, !0) } : u;
}
function Jl(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Yy(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const Ql = () => !0;
function Zy(e, { connectionMode: t, connectionRadius: n, handleId: r, nodeId: o, edgeUpdaterType: s, isTarget: a, domNode: c, nodeLookup: u, lib: l, autoPanOnConnect: f, flowId: p, panBy: d, cancelConnection: h, onConnectStart: g, onConnect: N, onConnectEnd: w, isValidConnection: m = Ql, onReconnectEnd: C, updateConnection: y, getTransform: b, getFromHandle: j, autoPanSpeed: v, dragThreshold: S = 1, handleDomNode: A }) {
  const M = Vl(e.target);
  let R = 0, $;
  const { x: _, y: V } = et(e), E = Jl(s, A), D = c?.getBoundingClientRect();
  let k = !1;
  if (!D || !E)
    return;
  const T = Gl(o, E, r, u, t);
  if (!T)
    return;
  let L = et(e, D), P = !1, K = null, F = !1, B = null;
  function q() {
    if (!f || !D)
      return;
    const [ae, te] = ss(L, D, v);
    d({ x: ae, y: te }), R = requestAnimationFrame(q);
  }
  const J = {
    ...T,
    nodeId: o,
    type: E,
    position: T.position
  }, X = u.get(o);
  let G = {
    inProgress: !0,
    isValid: null,
    from: Ht(X, J, ne.Left, !0),
    fromHandle: J,
    fromPosition: J.position,
    fromNode: X,
    to: L,
    toHandle: null,
    toPosition: da[J.position],
    toNode: null,
    pointer: L
  };
  function O() {
    k = !0, y(G), g?.(e, { nodeId: o, handleId: r, handleType: E });
  }
  S === 0 && O();
  function Y(ae) {
    if (!k) {
      const { x: ye, y: we } = et(ae), $e = ye - _, _e = we - V;
      if (!($e * $e + _e * _e > S * S))
        return;
      O();
    }
    if (!j() || !J) {
      se(ae);
      return;
    }
    const te = b();
    L = et(ae, D), $ = qy(Nn(L, te, !1, [1, 1]), n, u, J), P || (q(), P = !0);
    const re = eu(ae, {
      handle: $,
      connectionMode: t,
      fromNodeId: o,
      fromHandleId: r,
      fromType: a ? "target" : "source",
      isValidConnection: m,
      doc: M,
      lib: l,
      flowId: p,
      nodeLookup: u
    });
    B = re.handleDomNode, K = re.connection, F = Yy(!!$, re.isValid);
    const fe = u.get(o), W = fe ? Ht(fe, J, ne.Left, !0) : G.from, ee = {
      ...G,
      from: W,
      isValid: F,
      to: re.toHandle && F ? xn({ x: re.toHandle.x, y: re.toHandle.y }, te) : L,
      toHandle: re.toHandle,
      toPosition: F && re.toHandle ? re.toHandle.position : da[J.position],
      toNode: re.toHandle ? u.get(re.toHandle.nodeId) : null,
      pointer: L
    };
    y(ee), G = ee;
  }
  function se(ae) {
    if (!("touches" in ae && ae.touches.length > 0)) {
      if (k) {
        ($ || B) && K && F && N?.(K);
        const { inProgress: te, ...re } = G, fe = {
          ...re,
          toPosition: G.toHandle ? G.toPosition : null
        };
        w?.(ae, fe), s && C?.(ae, fe);
      }
      h(), cancelAnimationFrame(R), P = !1, F = !1, K = null, B = null, M.removeEventListener("mousemove", Y), M.removeEventListener("mouseup", se), M.removeEventListener("touchmove", Y), M.removeEventListener("touchend", se);
    }
  }
  M.addEventListener("mousemove", Y), M.addEventListener("mouseup", se), M.addEventListener("touchmove", Y), M.addEventListener("touchend", se);
}
function eu(e, { handle: t, connectionMode: n, fromNodeId: r, fromHandleId: o, fromType: s, doc: a, lib: c, flowId: u, isValidConnection: l = Ql, nodeLookup: f }) {
  const p = s === "target", d = t ? a.querySelector(`.${c}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: h, y: g } = et(e), N = a.elementFromPoint(h, g), w = N?.classList.contains(`${c}-flow__handle`) ? N : d, m = {
    handleDomNode: w,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (w) {
    const C = Jl(void 0, w), y = w.getAttribute("data-nodeid"), b = w.getAttribute("data-handleid"), j = w.classList.contains("connectable"), v = w.classList.contains("connectableend");
    if (!y || !C)
      return m;
    const S = {
      source: p ? y : r,
      sourceHandle: p ? b : o,
      target: p ? r : y,
      targetHandle: p ? o : b
    };
    m.connection = S;
    const M = j && v && (n === gn.Strict ? p && C === "source" || !p && C === "target" : y !== r || b !== o);
    m.isValid = M && l(S), m.toHandle = Gl(y, C, b, f, n, !0);
  }
  return m;
}
const $i = {
  onPointerDown: Zy,
  isValid: eu
};
function Gy({ domNode: e, panZoom: t, getTransform: n, getViewScale: r }) {
  const o = Ue(e);
  function s({ translateExtent: c, width: u, height: l, zoomStep: f = 1, pannable: p = !0, zoomable: d = !0, inversePan: h = !1 }) {
    const g = (y) => {
      if (y.sourceEvent.type !== "wheel" || !t)
        return;
      const b = n(), j = y.sourceEvent.ctrlKey && cr() ? 10 : 1, v = -y.sourceEvent.deltaY * (y.sourceEvent.deltaMode === 1 ? 0.05 : y.sourceEvent.deltaMode ? 1 : 2e-3) * f, S = b[2] * Math.pow(2, v * j);
      t.scaleTo(S);
    };
    let N = [0, 0];
    const w = (y) => {
      (y.sourceEvent.type === "mousedown" || y.sourceEvent.type === "touchstart") && (N = [
        y.sourceEvent.clientX ?? y.sourceEvent.touches[0].clientX,
        y.sourceEvent.clientY ?? y.sourceEvent.touches[0].clientY
      ]);
    }, m = (y) => {
      const b = n();
      if (y.sourceEvent.type !== "mousemove" && y.sourceEvent.type !== "touchmove" || !t)
        return;
      const j = [
        y.sourceEvent.clientX ?? y.sourceEvent.touches[0].clientX,
        y.sourceEvent.clientY ?? y.sourceEvent.touches[0].clientY
      ], v = [j[0] - N[0], j[1] - N[1]];
      N = j;
      const S = r() * Math.max(b[2], Math.log(b[2])) * (h ? -1 : 1), A = {
        x: b[0] - v[0] * S,
        y: b[1] - v[1] * S
      }, M = [
        [0, 0],
        [u, l]
      ];
      t.setViewportConstrained({
        x: A.x,
        y: A.y,
        zoom: b[2]
      }, M, c);
    }, C = Il().on("start", w).on("zoom", p ? m : null).on("zoom.wheel", d ? g : null);
    o.call(C, {});
  }
  function a() {
    o.on("zoom", null);
  }
  return {
    update: s,
    destroy: a,
    pointer: Ge
  };
}
const Mo = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), li = ({ x: e, y: t, zoom: n }) => Do.translate(e, t).scale(n), sn = (e, t) => e.target.closest(`.${t}`), tu = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Jy = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, ui = (e, t = 0, n = Jy, r = () => {
}) => {
  const o = typeof t == "number" && t > 0;
  return o || r(), o ? e.transition().duration(t).ease(n).on("end", r) : e;
}, nu = (e) => {
  const t = e.ctrlKey && cr() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Qy({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: r, panOnScrollMode: o, panOnScrollSpeed: s, zoomOnPinch: a, onPanZoomStart: c, onPanZoom: u, onPanZoomEnd: l }) {
  return (f) => {
    if (sn(f, t))
      return f.ctrlKey && f.preventDefault(), !1;
    f.preventDefault(), f.stopImmediatePropagation();
    const p = n.property("__zoom").k || 1;
    if (f.ctrlKey && a) {
      const w = Ge(f), m = nu(f), C = p * Math.pow(2, m);
      r.scaleTo(n, C, w, f);
      return;
    }
    const d = f.deltaMode === 1 ? 20 : 1;
    let h = o === Pt.Vertical ? 0 : f.deltaX * d, g = o === Pt.Horizontal ? 0 : f.deltaY * d;
    !cr() && f.shiftKey && o !== Pt.Vertical && (h = f.deltaY * d, g = 0), r.translateBy(
      n,
      -(h / p) * s,
      -(g / p) * s,
      // @ts-ignore
      { internal: !0 }
    );
    const N = Mo(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(f, N), e.panScrollTimeout = setTimeout(() => {
      l?.(f, N), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(f, N));
  };
}
function em({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(r, o) {
    const s = r.type === "wheel", a = !t && s && !r.ctrlKey, c = sn(r, e);
    if (r.ctrlKey && s && c && r.preventDefault(), a || c)
      return null;
    r.preventDefault(), n.call(this, r, o);
  };
}
function tm({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (r) => {
    if (r.sourceEvent?.internal)
      return;
    const o = Mo(r.transform);
    e.mouseButton = r.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = o, r.sourceEvent?.type === "mousedown" && t(!0), n && n?.(r.sourceEvent, o);
  };
}
function nm({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: r, onPanZoom: o }) {
  return (s) => {
    e.usedRightMouseButton = !!(n && tu(t, e.mouseButton ?? 0)), s.sourceEvent?.sync || r([s.transform.x, s.transform.y, s.transform.k]), o && !s.sourceEvent?.internal && o?.(s.sourceEvent, Mo(s.transform));
  };
}
function rm({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: r, onPanZoomEnd: o, onPaneContextMenu: s }) {
  return (a) => {
    if (!a.sourceEvent?.internal && (e.isZoomingOrPanning = !1, s && tu(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && a.sourceEvent && s(a.sourceEvent), e.usedRightMouseButton = !1, r(!1), o)) {
      const c = Mo(a.transform);
      e.prevViewport = c, clearTimeout(e.timerId), e.timerId = setTimeout(
        () => {
          o?.(a.sourceEvent, c);
        },
        // we need a setTimeout for panOnScroll to suppress multiple end events fired during scroll
        n ? 150 : 0
      );
    }
  };
}
function om({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: r, panOnScroll: o, zoomOnDoubleClick: s, userSelectionActive: a, noWheelClassName: c, noPanClassName: u, lib: l, connectionInProgress: f }) {
  return (p) => {
    const d = e || t, h = n && p.ctrlKey, g = p.type === "wheel";
    if (p.button === 1 && p.type === "mousedown" && (sn(p, `${l}-flow__node`) || sn(p, `${l}-flow__edge`)))
      return !0;
    if (!r && !d && !o && !s && !n || a || f && !g || sn(p, c) && g || sn(p, u) && (!g || o && g && !e) || !n && p.ctrlKey && g)
      return !1;
    if (!n && p.type === "touchstart" && p.touches?.length > 1)
      return p.preventDefault(), !1;
    if (!d && !o && !h && g || !r && (p.type === "mousedown" || p.type === "touchstart") || Array.isArray(r) && !r.includes(p.button) && p.type === "mousedown")
      return !1;
    const N = Array.isArray(r) && r.includes(p.button) || !p.button || p.button <= 1;
    return (!p.ctrlKey || g) && N;
  };
}
function im({ domNode: e, minZoom: t, maxZoom: n, translateExtent: r, viewport: o, onPanZoom: s, onPanZoomStart: a, onPanZoomEnd: c, onDraggingChange: u }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, f = e.getBoundingClientRect(), p = Il().scaleExtent([t, n]).translateExtent(r), d = Ue(e).call(p);
  C({
    x: o.x,
    y: o.y,
    zoom: yn(o.zoom, t, n)
  }, [
    [0, 0],
    [f.width, f.height]
  ], r);
  const h = d.on("wheel.zoom"), g = d.on("dblclick.zoom");
  p.wheelDelta(nu);
  async function N($, _) {
    return d ? new Promise((V) => {
      p?.interpolate(_?.interpolate === "linear" ? Yn : Xr).transform(ui(d, _?.duration, _?.ease, () => V(!0)), $);
    }) : !1;
  }
  function w({ noWheelClassName: $, noPanClassName: _, onPaneContextMenu: V, userSelectionActive: E, panOnScroll: D, panOnDrag: k, panOnScrollMode: T, panOnScrollSpeed: L, preventScrolling: P, zoomOnPinch: K, zoomOnScroll: F, zoomOnDoubleClick: B, zoomActivationKeyPressed: q, lib: J, onTransformChange: X, connectionInProgress: ce, paneClickDistance: G, selectionOnDrag: O }) {
    E && !l.isZoomingOrPanning && m();
    const Y = D && !q && !E;
    p.clickDistance(O ? 1 / 0 : !Qe(G) || G < 0 ? 0 : G);
    const se = Y ? Qy({
      zoomPanValues: l,
      noWheelClassName: $,
      d3Selection: d,
      d3Zoom: p,
      panOnScrollMode: T,
      panOnScrollSpeed: L,
      zoomOnPinch: K,
      onPanZoomStart: a,
      onPanZoom: s,
      onPanZoomEnd: c
    }) : em({
      noWheelClassName: $,
      preventScrolling: P,
      d3ZoomHandler: h
    });
    d.on("wheel.zoom", se, { passive: !1 });
    const ae = tm({
      zoomPanValues: l,
      onDraggingChange: u,
      onPanZoomStart: a
    });
    p.on("start", ae);
    const te = nm({
      zoomPanValues: l,
      panOnDrag: k,
      onPaneContextMenu: !!V,
      onPanZoom: s,
      onTransformChange: X
    });
    p.on("zoom", te);
    const re = rm({
      zoomPanValues: l,
      panOnDrag: k,
      panOnScroll: D,
      onPaneContextMenu: V,
      onPanZoomEnd: c,
      onDraggingChange: u
    });
    p.on("end", re);
    const fe = om({
      zoomActivationKeyPressed: q,
      panOnDrag: k,
      zoomOnScroll: F,
      panOnScroll: D,
      zoomOnDoubleClick: B,
      zoomOnPinch: K,
      userSelectionActive: E,
      noPanClassName: _,
      noWheelClassName: $,
      lib: J,
      connectionInProgress: ce
    });
    p.filter(fe), B ? d.on("dblclick.zoom", g) : d.on("dblclick.zoom", null);
  }
  function m() {
    p.on("zoom", null);
  }
  async function C($, _, V) {
    const E = li($), D = p?.constrain()(E, _, V);
    return D && await N(D), D;
  }
  async function y($, _) {
    const V = li($);
    return await N(V, _), V;
  }
  function b($) {
    if (d) {
      const _ = li($), V = d.property("__zoom");
      (V.k !== $.zoom || V.x !== $.x || V.y !== $.y) && p?.transform(d, _, null, { sync: !0 });
    }
  }
  function j() {
    const $ = d ? Cl(d.node()) : { x: 0, y: 0, k: 1 };
    return { x: $.x, y: $.y, zoom: $.k };
  }
  async function v($, _) {
    return d ? new Promise((V) => {
      p?.interpolate(_?.interpolate === "linear" ? Yn : Xr).scaleTo(ui(d, _?.duration, _?.ease, () => V(!0)), $);
    }) : !1;
  }
  async function S($, _) {
    return d ? new Promise((V) => {
      p?.interpolate(_?.interpolate === "linear" ? Yn : Xr).scaleBy(ui(d, _?.duration, _?.ease, () => V(!0)), $);
    }) : !1;
  }
  function A($) {
    p?.scaleExtent($);
  }
  function M($) {
    p?.translateExtent($);
  }
  function R($) {
    const _ = !Qe($) || $ < 0 ? 0 : $;
    p?.clickDistance(_);
  }
  return {
    update: w,
    destroy: m,
    setViewport: y,
    setViewportConstrained: C,
    getViewport: j,
    scaleTo: v,
    scaleBy: S,
    setScaleExtent: A,
    setTranslateExtent: M,
    syncViewport: b,
    setClickDistance: R
  };
}
var wn;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(wn || (wn = {}));
function sm({ width: e, prevWidth: t, height: n, prevHeight: r, affectsX: o, affectsY: s }) {
  const a = e - t, c = n - r, u = [a > 0 ? 1 : a < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return a && o && (u[0] = u[0] * -1), c && s && (u[1] = u[1] * -1), u;
}
function Sa(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), r = e.includes("left"), o = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: r,
    affectsY: o
  };
}
function wt(e, t) {
  return Math.max(0, t - e);
}
function vt(e, t) {
  return Math.max(0, e - t);
}
function Hr(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function Ea(e, t) {
  return e ? !t : t;
}
function am(e, t, n, r, o, s, a, c) {
  let { affectsX: u, affectsY: l } = t;
  const { isHorizontal: f, isVertical: p } = t, d = f && p, { xSnapped: h, ySnapped: g } = n, { minWidth: N, maxWidth: w, minHeight: m, maxHeight: C } = r, { x: y, y: b, width: j, height: v, aspectRatio: S } = e;
  let A = Math.floor(f ? h - e.pointerX : 0), M = Math.floor(p ? g - e.pointerY : 0);
  const R = j + (u ? -A : A), $ = v + (l ? -M : M), _ = -s[0] * j, V = -s[1] * v;
  let E = Hr(R, N, w), D = Hr($, m, C);
  if (a) {
    let L = 0, P = 0;
    u && A < 0 ? L = wt(y + A + _, a[0][0]) : !u && A > 0 && (L = vt(y + R + _, a[1][0])), l && M < 0 ? P = wt(b + M + V, a[0][1]) : !l && M > 0 && (P = vt(b + $ + V, a[1][1])), E = Math.max(E, L), D = Math.max(D, P);
  }
  if (c) {
    let L = 0, P = 0;
    u && A > 0 ? L = vt(y + A, c[0][0]) : !u && A < 0 && (L = wt(y + R, c[1][0])), l && M > 0 ? P = vt(b + M, c[0][1]) : !l && M < 0 && (P = wt(b + $, c[1][1])), E = Math.max(E, L), D = Math.max(D, P);
  }
  if (o) {
    if (f) {
      const L = Hr(R / S, m, C) * S;
      if (E = Math.max(E, L), a) {
        let P = 0;
        !u && !l || u && !l && d ? P = vt(b + V + R / S, a[1][1]) * S : P = wt(b + V + (u ? A : -A) / S, a[0][1]) * S, E = Math.max(E, P);
      }
      if (c) {
        let P = 0;
        !u && !l || u && !l && d ? P = wt(b + R / S, c[1][1]) * S : P = vt(b + (u ? A : -A) / S, c[0][1]) * S, E = Math.max(E, P);
      }
    }
    if (p) {
      const L = Hr($ * S, N, w) / S;
      if (D = Math.max(D, L), a) {
        let P = 0;
        !u && !l || l && !u && d ? P = vt(y + $ * S + _, a[1][0]) / S : P = wt(y + (l ? M : -M) * S + _, a[0][0]) / S, D = Math.max(D, P);
      }
      if (c) {
        let P = 0;
        !u && !l || l && !u && d ? P = wt(y + $ * S, c[1][0]) / S : P = vt(y + (l ? M : -M) * S, c[0][0]) / S, D = Math.max(D, P);
      }
    }
  }
  M = M + (M < 0 ? D : -D), A = A + (A < 0 ? E : -E), o && (d ? R > $ * S ? M = (Ea(u, l) ? -A : A) / S : A = (Ea(u, l) ? -M : M) * S : f ? (M = A / S, l = u) : (A = M * S, u = l));
  const k = u ? y + A : y, T = l ? b + M : b;
  return {
    width: j + (u ? -A : A),
    height: v + (l ? -M : M),
    x: s[0] * A * (u ? -1 : 1) + k,
    y: s[1] * M * (l ? -1 : 1) + T
  };
}
const ru = { width: 0, height: 0, x: 0, y: 0 }, cm = {
  ...ru,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function lm(e, t, n) {
  const r = t.position.x + e.position.x, o = t.position.y + e.position.y, s = e.measured.width ?? 0, a = e.measured.height ?? 0, c = n[0] * s, u = n[1] * a;
  return [
    [r - c, o - u],
    [r + s - c, o + a - u]
  ];
}
function um({ domNode: e, nodeId: t, getStoreItems: n, onChange: r, onEnd: o }) {
  const s = Ue(e);
  let a = {
    controlDirection: Sa("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: l, boundaries: f, keepAspectRatio: p, resizeDirection: d, onResizeStart: h, onResize: g, onResizeEnd: N, shouldResize: w }) {
    let m = { ...ru }, C = { ...cm };
    a = {
      boundaries: f,
      resizeDirection: d,
      keepAspectRatio: p,
      controlDirection: Sa(l)
    };
    let y, b = null, j = [], v, S, A, M = !1;
    const R = fl().on("start", ($) => {
      const { nodeLookup: _, transform: V, snapGrid: E, snapToGrid: D, nodeOrigin: k, paneDomNode: T } = n();
      if (y = _.get(t), !y)
        return;
      b = T?.getBoundingClientRect() ?? null;
      const { xSnapped: L, ySnapped: P } = Zn($.sourceEvent, {
        transform: V,
        snapGrid: E,
        snapToGrid: D,
        containerBounds: b
      });
      m = {
        width: y.measured.width ?? 0,
        height: y.measured.height ?? 0,
        x: y.position.x ?? 0,
        y: y.position.y ?? 0
      }, C = {
        ...m,
        pointerX: L,
        pointerY: P,
        aspectRatio: m.width / m.height
      }, v = void 0, S = Ot(y.extent) ? y.extent : void 0, y.parentId && (y.extent === "parent" || y.expandParent) && (v = _.get(y.parentId)), v && y.extent === "parent" && (S = [
        [0, 0],
        [v.measured.width, v.measured.height]
      ]), j = [], A = void 0;
      for (const [K, F] of _)
        if (F.parentId === t && (j.push({
          id: K,
          position: { ...F.position },
          extent: F.extent
        }), F.extent === "parent" || F.expandParent)) {
          const B = lm(F, y, F.origin ?? k);
          A ? A = [
            [Math.min(B[0][0], A[0][0]), Math.min(B[0][1], A[0][1])],
            [Math.max(B[1][0], A[1][0]), Math.max(B[1][1], A[1][1])]
          ] : A = B;
        }
      h?.($, { ...m });
    }).on("drag", ($) => {
      const { transform: _, snapGrid: V, snapToGrid: E, nodeOrigin: D } = n(), k = Zn($.sourceEvent, {
        transform: _,
        snapGrid: V,
        snapToGrid: E,
        containerBounds: b
      }), T = [];
      if (!y)
        return;
      const { x: L, y: P, width: K, height: F } = m, B = {}, q = y.origin ?? D, { width: J, height: X, x: ce, y: G } = am(C, a.controlDirection, k, a.boundaries, a.keepAspectRatio, q, S, A), O = J !== K, Y = X !== F, se = ce !== L && O, ae = G !== P && Y;
      if (!se && !ae && !O && !Y)
        return;
      if ((se || ae || q[0] === 1 || q[1] === 1) && (B.x = se ? ce : m.x, B.y = ae ? G : m.y, m.x = B.x, m.y = B.y, j.length > 0)) {
        const W = ce - L, ee = G - P;
        for (const ye of j)
          ye.position = {
            x: ye.position.x - W + q[0] * (J - K),
            y: ye.position.y - ee + q[1] * (X - F)
          }, T.push(ye);
      }
      if ((O || Y) && (B.width = O && (!a.resizeDirection || a.resizeDirection === "horizontal") ? J : m.width, B.height = Y && (!a.resizeDirection || a.resizeDirection === "vertical") ? X : m.height, m.width = B.width, m.height = B.height), v && y.expandParent) {
        const W = q[0] * (B.width ?? 0);
        B.x && B.x < W && (m.x = W, C.x = C.x - (B.x - W));
        const ee = q[1] * (B.height ?? 0);
        B.y && B.y < ee && (m.y = ee, C.y = C.y - (B.y - ee));
      }
      const te = sm({
        width: m.width,
        prevWidth: K,
        height: m.height,
        prevHeight: F,
        affectsX: a.controlDirection.affectsX,
        affectsY: a.controlDirection.affectsY
      }), re = { ...m, direction: te };
      w?.($, re) !== !1 && (M = !0, g?.($, re), r(B, T));
    }).on("end", ($) => {
      M && (N?.($, { ...m }), o?.({ ...m }), M = !1);
    });
    s.call(R);
  }
  function u() {
    s.on(".drag", null);
  }
  return {
    update: c,
    destroy: u
  };
}
var di = { exports: {} }, fi = {}, pi = { exports: {} }, hi = {};
var Ca;
function dm() {
  if (Ca) return hi;
  Ca = 1;
  var e = it;
  function t(p, d) {
    return p === d && (p !== 0 || 1 / p === 1 / d) || p !== p && d !== d;
  }
  var n = typeof Object.is == "function" ? Object.is : t, r = e.useState, o = e.useEffect, s = e.useLayoutEffect, a = e.useDebugValue;
  function c(p, d) {
    var h = d(), g = r({ inst: { value: h, getSnapshot: d } }), N = g[0].inst, w = g[1];
    return s(
      function() {
        N.value = h, N.getSnapshot = d, u(N) && w({ inst: N });
      },
      [p, h, d]
    ), o(
      function() {
        return u(N) && w({ inst: N }), p(function() {
          u(N) && w({ inst: N });
        });
      },
      [p]
    ), a(h), h;
  }
  function u(p) {
    var d = p.getSnapshot;
    p = p.value;
    try {
      var h = d();
      return !n(p, h);
    } catch {
      return !0;
    }
  }
  function l(p, d) {
    return d();
  }
  var f = typeof window > "u" || typeof window.document > "u" || typeof window.document.createElement > "u" ? l : c;
  return hi.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : f, hi;
}
var Ia;
function fm() {
  return Ia || (Ia = 1, pi.exports = dm()), pi.exports;
}
var ka;
function pm() {
  if (ka) return fi;
  ka = 1;
  var e = it, t = fm();
  function n(l, f) {
    return l === f && (l !== 0 || 1 / l === 1 / f) || l !== l && f !== f;
  }
  var r = typeof Object.is == "function" ? Object.is : n, o = t.useSyncExternalStore, s = e.useRef, a = e.useEffect, c = e.useMemo, u = e.useDebugValue;
  return fi.useSyncExternalStoreWithSelector = function(l, f, p, d, h) {
    var g = s(null);
    if (g.current === null) {
      var N = { hasValue: !1, value: null };
      g.current = N;
    } else N = g.current;
    g = c(
      function() {
        function m(v) {
          if (!C) {
            if (C = !0, y = v, v = d(v), h !== void 0 && N.hasValue) {
              var S = N.value;
              if (h(S, v))
                return b = S;
            }
            return b = v;
          }
          if (S = b, r(y, v)) return S;
          var A = d(v);
          return h !== void 0 && h(S, A) ? (y = v, S) : (y = v, b = A);
        }
        var C = !1, y, b, j = p === void 0 ? null : p;
        return [
          function() {
            return m(f());
          },
          j === null ? void 0 : function() {
            return m(j());
          }
        ];
      },
      [f, p, d, h]
    );
    var w = o(l, g[0], g[1]);
    return a(
      function() {
        N.hasValue = !0, N.value = w;
      },
      [w]
    ), u(w), w;
  }, fi;
}
var Aa;
function hm() {
  return Aa || (Aa = 1, di.exports = pm()), di.exports;
}
var gm = hm();
const ym = /* @__PURE__ */ Tf(gm), mm = {}, _a = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), r = (f, p) => {
    const d = typeof f == "function" ? f(t) : f;
    if (!Object.is(d, t)) {
      const h = t;
      t = p ?? (typeof d != "object" || d === null) ? d : Object.assign({}, t, d), n.forEach((g) => g(t, h));
    }
  }, o = () => t, u = { setState: r, getState: o, getInitialState: () => l, subscribe: (f) => (n.add(f), () => n.delete(f)), destroy: () => {
    (mm ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(r, o, u);
  return u;
}, xm = (e) => e ? _a(e) : _a, { useDebugValue: wm } = it, { useSyncExternalStoreWithSelector: vm } = ym, bm = (e) => e;
function ou(e, t = bm, n) {
  const r = vm(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return wm(r), r;
}
const Da = (e, t) => {
  const n = xm(e), r = (o, s = t) => ou(n, o, s);
  return Object.assign(r, n), r;
}, Nm = (e, t) => e ? Da(e, t) : Da;
function be(e, t) {
  if (Object.is(e, t))
    return !0;
  if (typeof e != "object" || e === null || typeof t != "object" || t === null)
    return !1;
  if (e instanceof Map && t instanceof Map) {
    if (e.size !== t.size) return !1;
    for (const [r, o] of e)
      if (!Object.is(o, t.get(r)))
        return !1;
    return !0;
  }
  if (e instanceof Set && t instanceof Set) {
    if (e.size !== t.size) return !1;
    for (const r of e)
      if (!t.has(r))
        return !1;
    return !0;
  }
  const n = Object.keys(e);
  if (n.length !== Object.keys(t).length)
    return !1;
  for (const r of n)
    if (!Object.prototype.hasOwnProperty.call(t, r) || !Object.is(e[r], t[r]))
      return !1;
  return !0;
}
var gi = { exports: {} }, Te = {};
var Ta;
function jm() {
  if (Ta) return Te;
  Ta = 1;
  var e = it;
  function t(u) {
    var l = "https://react.dev/errors/" + u;
    if (1 < arguments.length) {
      l += "?args[]=" + encodeURIComponent(arguments[1]);
      for (var f = 2; f < arguments.length; f++)
        l += "&args[]=" + encodeURIComponent(arguments[f]);
    }
    return "Minified React error #" + u + "; visit " + l + " for the full message or use the non-minified dev environment for full errors and additional helpful warnings.";
  }
  function n() {
  }
  var r = {
    d: {
      f: n,
      r: function() {
        throw Error(t(522));
      },
      D: n,
      C: n,
      L: n,
      m: n,
      X: n,
      S: n,
      M: n
    },
    p: 0,
    findDOMNode: null
  }, o = /* @__PURE__ */ Symbol.for("react.portal");
  function s(u, l, f) {
    var p = 3 < arguments.length && arguments[3] !== void 0 ? arguments[3] : null;
    return {
      $$typeof: o,
      key: p == null ? null : "" + p,
      children: u,
      containerInfo: l,
      implementation: f
    };
  }
  var a = e.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE;
  function c(u, l) {
    if (u === "font") return "";
    if (typeof l == "string")
      return l === "use-credentials" ? l : "";
  }
  return Te.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = r, Te.createPortal = function(u, l) {
    var f = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!l || l.nodeType !== 1 && l.nodeType !== 9 && l.nodeType !== 11)
      throw Error(t(299));
    return s(u, l, null, f);
  }, Te.flushSync = function(u) {
    var l = a.T, f = r.p;
    try {
      if (a.T = null, r.p = 2, u) return u();
    } finally {
      a.T = l, r.p = f, r.d.f();
    }
  }, Te.preconnect = function(u, l) {
    typeof u == "string" && (l ? (l = l.crossOrigin, l = typeof l == "string" ? l === "use-credentials" ? l : "" : void 0) : l = null, r.d.C(u, l));
  }, Te.prefetchDNS = function(u) {
    typeof u == "string" && r.d.D(u);
  }, Te.preinit = function(u, l) {
    if (typeof u == "string" && l && typeof l.as == "string") {
      var f = l.as, p = c(f, l.crossOrigin), d = typeof l.integrity == "string" ? l.integrity : void 0, h = typeof l.fetchPriority == "string" ? l.fetchPriority : void 0;
      f === "style" ? r.d.S(
        u,
        typeof l.precedence == "string" ? l.precedence : void 0,
        {
          crossOrigin: p,
          integrity: d,
          fetchPriority: h
        }
      ) : f === "script" && r.d.X(u, {
        crossOrigin: p,
        integrity: d,
        fetchPriority: h,
        nonce: typeof l.nonce == "string" ? l.nonce : void 0
      });
    }
  }, Te.preinitModule = function(u, l) {
    if (typeof u == "string")
      if (typeof l == "object" && l !== null) {
        if (l.as == null || l.as === "script") {
          var f = c(
            l.as,
            l.crossOrigin
          );
          r.d.M(u, {
            crossOrigin: f,
            integrity: typeof l.integrity == "string" ? l.integrity : void 0,
            nonce: typeof l.nonce == "string" ? l.nonce : void 0
          });
        }
      } else l == null && r.d.M(u);
  }, Te.preload = function(u, l) {
    if (typeof u == "string" && typeof l == "object" && l !== null && typeof l.as == "string") {
      var f = l.as, p = c(f, l.crossOrigin);
      r.d.L(u, f, {
        crossOrigin: p,
        integrity: typeof l.integrity == "string" ? l.integrity : void 0,
        nonce: typeof l.nonce == "string" ? l.nonce : void 0,
        type: typeof l.type == "string" ? l.type : void 0,
        fetchPriority: typeof l.fetchPriority == "string" ? l.fetchPriority : void 0,
        referrerPolicy: typeof l.referrerPolicy == "string" ? l.referrerPolicy : void 0,
        imageSrcSet: typeof l.imageSrcSet == "string" ? l.imageSrcSet : void 0,
        imageSizes: typeof l.imageSizes == "string" ? l.imageSizes : void 0,
        media: typeof l.media == "string" ? l.media : void 0
      });
    }
  }, Te.preloadModule = function(u, l) {
    if (typeof u == "string")
      if (l) {
        var f = c(l.as, l.crossOrigin);
        r.d.m(u, {
          as: typeof l.as == "string" && l.as !== "script" ? l.as : void 0,
          crossOrigin: f,
          integrity: typeof l.integrity == "string" ? l.integrity : void 0
        });
      } else r.d.m(u);
  }, Te.requestFormReset = function(u) {
    r.d.r(u);
  }, Te.unstable_batchedUpdates = function(u, l) {
    return u(l);
  }, Te.useFormState = function(u, l, f) {
    return a.H.useFormState(u, l, f);
  }, Te.useFormStatus = function() {
    return a.H.useHostTransitionStatus();
  }, Te.version = "19.2.7", Te;
}
var $a;
function Sm() {
  if ($a) return gi.exports;
  $a = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), gi.exports = jm(), gi.exports;
}
var Em = Sm();
const Po = Xi(null), Cm = Po.Provider, iu = qe.error001("react");
function pe(e, t) {
  const n = dr(Po);
  if (n === null)
    throw new Error(iu);
  return ou(n, e, t);
}
function Ne() {
  const e = dr(Po);
  if (e === null)
    throw new Error(iu);
  return de(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const Ma = { display: "none" }, Im = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, su = "react-flow__node-desc", au = "react-flow__edge-desc", km = "react-flow__aria-live", Am = (e) => e.ariaLiveMessage, _m = (e) => e.ariaLabelConfig;
function Dm({ rfId: e }) {
  const t = pe(Am);
  return i.jsx("div", { id: `${km}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: Im, children: t });
}
function Tm({ rfId: e, disableKeyboardA11y: t }) {
  const n = pe(_m);
  return i.jsxs(i.Fragment, { children: [i.jsx("div", { id: `${su}-${e}`, style: Ma, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), i.jsx("div", { id: `${au}-${e}`, style: Ma, children: n["edge.a11yDescription.default"] }), !t && i.jsx(Dm, { rfId: e })] });
}
const Ro = Fc(({ position: e = "top-left", children: t, className: n, style: r, ...o }, s) => {
  const a = `${e}`.split("-");
  return i.jsx("div", { className: Ee(["react-flow__panel", n, ...a]), style: r, ref: s, ...o, children: t });
});
Ro.displayName = "Panel";
function $m({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : i.jsx(Ro, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: i.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const Mm = (e) => {
  const t = [], n = [];
  for (const [, r] of e.nodeLookup)
    r.selected && t.push(r.internals.userNode);
  for (const [, r] of e.edgeLookup)
    r.selected && n.push(r);
  return { selectedNodes: t, selectedEdges: n };
}, Wr = (e) => e.id;
function Pm(e, t) {
  return be(e.selectedNodes.map(Wr), t.selectedNodes.map(Wr)) && be(e.selectedEdges.map(Wr), t.selectedEdges.map(Wr));
}
function Rm({ onSelectionChange: e }) {
  const t = Ne(), { selectedNodes: n, selectedEdges: r } = pe(Mm, Pm);
  return Q(() => {
    const o = { nodes: n, edges: r };
    e?.(o), t.getState().onSelectionChangeHandlers.forEach((s) => s(o));
  }, [n, r, e]), null;
}
const Lm = (e) => !!e.onSelectionChangeHandlers;
function zm({ onSelectionChange: e }) {
  const t = pe(Lm);
  return e || t ? i.jsx(Rm, { onSelectionChange: e }) : null;
}
const cu = [0, 0], Vm = { x: 0, y: 0, zoom: 1 }, Om = [
  "nodes",
  "edges",
  "defaultNodes",
  "defaultEdges",
  "onConnect",
  "onConnectStart",
  "onConnectEnd",
  "onClickConnectStart",
  "onClickConnectEnd",
  "nodesDraggable",
  "autoPanOnNodeFocus",
  "nodesConnectable",
  "nodesFocusable",
  "edgesFocusable",
  "edgesReconnectable",
  "elevateNodesOnSelect",
  "elevateEdgesOnSelect",
  "minZoom",
  "maxZoom",
  "nodeExtent",
  "onNodesChange",
  "onEdgesChange",
  "elementsSelectable",
  "connectionMode",
  "snapGrid",
  "snapToGrid",
  "translateExtent",
  "connectOnClick",
  "defaultEdgeOptions",
  "fitView",
  "fitViewOptions",
  "onNodesDelete",
  "onEdgesDelete",
  "onDelete",
  "onNodeDrag",
  "onNodeDragStart",
  "onNodeDragStop",
  "onSelectionDrag",
  "onSelectionDragStart",
  "onSelectionDragStop",
  "onMoveStart",
  "onMove",
  "onMoveEnd",
  "noPanClassName",
  "nodeOrigin",
  "autoPanOnConnect",
  "autoPanOnNodeDrag",
  "onError",
  "connectionRadius",
  "isValidConnection",
  "selectNodesOnDrag",
  "nodeDragThreshold",
  "connectionDragThreshold",
  "onBeforeDelete",
  "debug",
  "autoPanSpeed",
  "ariaLabelConfig",
  "zIndexMode"
], Pa = [...Om, "rfId"], Hm = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), Ra = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: ir,
  nodeOrigin: cu,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function Wm(e) {
  const { setNodes: t, setEdges: n, setMinZoom: r, setMaxZoom: o, setTranslateExtent: s, setNodeExtent: a, reset: c, setDefaultNodesAndEdges: u } = pe(Hm, be), l = Ne();
  Q(() => (u(e.defaultNodes, e.defaultEdges), () => {
    f.current = Ra, c();
  }), []);
  const f = ie(Ra);
  return Q(
    () => {
      for (const p of Pa) {
        const d = e[p], h = f.current[p];
        d !== h && (typeof e[p] > "u" || (p === "nodes" ? t(d) : p === "edges" ? n(d) : p === "minZoom" ? r(d) : p === "maxZoom" ? o(d) : p === "translateExtent" ? s(d) : p === "nodeExtent" ? a(d) : p === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: jy(d) }) : p === "fitView" ? l.setState({ fitViewQueued: d }) : p === "fitViewOptions" ? l.setState({ fitViewOptions: d }) : l.setState({ [p]: d })));
      }
      f.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    Pa.map((p) => e[p])
  ), null;
}
function La() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function Fm(e) {
  const [t, n] = U(e === "system" ? null : e);
  return Q(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const r = La(), o = () => n(r?.matches ? "dark" : "light");
    return o(), r?.addEventListener("change", o), () => {
      r?.removeEventListener("change", o);
    };
  }, [e]), t !== null ? t : La()?.matches ? "dark" : "light";
}
const za = typeof document < "u" ? document : null;
function lr(e = null, t = { target: za, actInsideInputWithModifier: !0 }) {
  const [n, r] = U(!1), o = ie(!1), s = ie(/* @__PURE__ */ new Set([])), [a, c] = de(() => {
    if (e !== null) {
      const l = (Array.isArray(e) ? e : [e]).filter((p) => typeof p == "string").map((p) => p.replace("+", `
`).replace(`

`, `
+`).split(`
`)), f = l.reduce((p, d) => p.concat(...d), []);
      return [l, f];
    }
    return [[], []];
  }, [e]);
  return Q(() => {
    const u = t?.target ?? za, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const f = (h) => {
        if (o.current = h.ctrlKey || h.metaKey || h.shiftKey || h.altKey, (!o.current || o.current && !l) && Ol(h))
          return !1;
        const N = Oa(h.code, c);
        if (s.current.add(h[N]), Va(a, s.current, !1)) {
          const w = h.composedPath?.()?.[0] || h.target, m = w?.nodeName === "BUTTON" || w?.nodeName === "A";
          t.preventDefault !== !1 && (o.current || !m) && h.preventDefault(), r(!0);
        }
      }, p = (h) => {
        const g = Oa(h.code, c);
        Va(a, s.current, !0) ? (r(!1), s.current.clear()) : s.current.delete(h[g]), h.key === "Meta" && s.current.clear(), o.current = !1;
      }, d = () => {
        s.current.clear(), r(!1);
      };
      return u?.addEventListener("keydown", f), u?.addEventListener("keyup", p), window.addEventListener("blur", d), window.addEventListener("contextmenu", d), () => {
        u?.removeEventListener("keydown", f), u?.removeEventListener("keyup", p), window.removeEventListener("blur", d), window.removeEventListener("contextmenu", d);
      };
    }
  }, [e, r]), n;
}
function Va(e, t, n) {
  return e.filter((r) => n || r.length === t.size).some((r) => r.every((o) => t.has(o)));
}
function Oa(e, t) {
  return t.includes(e) ? "code" : "key";
}
const Bm = () => {
  const e = Ne();
  return de(() => ({
    zoomIn: async (t) => {
      const { panZoom: n } = e.getState();
      return n ? n.scaleBy(1.2, t) : !1;
    },
    zoomOut: async (t) => {
      const { panZoom: n } = e.getState();
      return n ? n.scaleBy(1 / 1.2, t) : !1;
    },
    zoomTo: async (t, n) => {
      const { panZoom: r } = e.getState();
      return r ? r.scaleTo(t, n) : !1;
    },
    getZoom: () => e.getState().transform[2],
    setViewport: async (t, n) => {
      const { transform: [r, o, s], panZoom: a } = e.getState();
      return a ? (await a.setViewport({
        x: t.x ?? r,
        y: t.y ?? o,
        zoom: t.zoom ?? s
      }, n), !0) : !1;
    },
    getViewport: () => {
      const [t, n, r] = e.getState().transform;
      return { x: t, y: n, zoom: r };
    },
    setCenter: async (t, n, r) => e.getState().setCenter(t, n, r),
    fitBounds: async (t, n) => {
      const { width: r, height: o, minZoom: s, maxZoom: a, panZoom: c } = e.getState(), u = as(t, r, o, s, a, n?.padding ?? 0.1);
      return c ? (await c.setViewport(u, {
        duration: n?.duration,
        ease: n?.ease,
        interpolate: n?.interpolate
      }), !0) : !1;
    },
    screenToFlowPosition: (t, n = {}) => {
      const { transform: r, snapGrid: o, snapToGrid: s, domNode: a } = e.getState();
      if (!a)
        return t;
      const { x: c, y: u } = a.getBoundingClientRect(), l = {
        x: t.x - c,
        y: t.y - u
      }, f = n.snapGrid ?? o, p = n.snapToGrid ?? s;
      return Nn(l, r, p, f);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: r } = e.getState();
      if (!r)
        return t;
      const { x: o, y: s } = r.getBoundingClientRect(), a = xn(t, n);
      return {
        x: a.x + o,
        y: a.y + s
      };
    }
  }), []);
};
function lu(e, t) {
  const n = [], r = /* @__PURE__ */ new Map(), o = [];
  for (const s of e)
    if (s.type === "add") {
      o.push(s);
      continue;
    } else if (s.type === "remove" || s.type === "replace")
      r.set(s.id, [s]);
    else {
      const a = r.get(s.id);
      a ? a.push(s) : r.set(s.id, [s]);
    }
  for (const s of t) {
    const a = r.get(s.id);
    if (!a) {
      n.push(s);
      continue;
    }
    if (a[0].type === "remove")
      continue;
    if (a[0].type === "replace") {
      n.push({ ...a[0].item });
      continue;
    }
    const c = { ...s };
    for (const u of a)
      Km(u, c);
    n.push(c);
  }
  return o.length && o.forEach((s) => {
    s.index !== void 0 ? n.splice(s.index, 0, { ...s.item }) : n.push({ ...s.item });
  }), n;
}
function Km(e, t) {
  switch (e.type) {
    case "select": {
      t.selected = e.selected;
      break;
    }
    case "position": {
      typeof e.position < "u" && (t.position = e.position), typeof e.dragging < "u" && (t.dragging = e.dragging);
      break;
    }
    case "dimensions": {
      typeof e.dimensions < "u" && (t.measured = {
        ...e.dimensions
      }, e.setAttributes && ((e.setAttributes === !0 || e.setAttributes === "width") && (t.width = e.dimensions.width), (e.setAttributes === !0 || e.setAttributes === "height") && (t.height = e.dimensions.height))), typeof e.resizing == "boolean" && (t.resizing = e.resizing);
      break;
    }
  }
}
function uu(e, t) {
  return lu(e, t);
}
function du(e, t) {
  return lu(e, t);
}
function Tt(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function an(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const r = [];
  for (const [o, s] of e) {
    const a = t.has(o);
    !(s.selected === void 0 && !a) && s.selected !== a && (n && (s.selected = a), r.push(Tt(s.id, a)));
  }
  return r;
}
function Ha({ items: e = [], lookup: t }) {
  const n = [], r = new Map(e.map((o) => [o.id, o]));
  for (const [o, s] of e.entries()) {
    const a = t.get(s.id), c = a?.internals?.userNode ?? a;
    c !== void 0 && c !== s && n.push({ id: s.id, item: s, type: "replace" }), c === void 0 && n.push({ item: s, type: "add", index: o });
  }
  for (const [o] of t)
    r.get(o) === void 0 && n.push({ id: o, type: "remove" });
  return n;
}
function Wa(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const fu = Rl();
function pu(e, t, n = {}) {
  return ky(e, t, {
    ...n,
    onError: n.onError ?? fu
  });
}
function Um(e, t, n, r = { shouldReplaceId: !0 }) {
  return Ay(e, t, n, {
    ...r,
    onError: r.onError ?? fu
  });
}
const Fa = (e) => hy(e), Xm = (e) => Tl(e);
function hu(e) {
  return Fc(e);
}
const qm = typeof window < "u" ? hf : Q;
function Ba(e) {
  const [t, n] = U(BigInt(0)), [r] = U(() => Ym(() => n((o) => o + BigInt(1))));
  return qm(() => {
    const o = r.get();
    o.length && (e(o), r.reset());
  }, [t]), r;
}
function Ym(e) {
  let t = [];
  return {
    get: () => t,
    reset: () => {
      t = [];
    },
    push: (n) => {
      t.push(n), e();
    }
  };
}
const gu = Xi(null);
function Zm({ children: e }) {
  const t = Ne(), n = le((c) => {
    const { nodes: u = [], setNodes: l, hasDefaultNodes: f, onNodesChange: p, nodeLookup: d, fitViewQueued: h, onNodesChangeMiddlewareMap: g } = t.getState();
    let N = u;
    for (const m of c)
      N = typeof m == "function" ? m(N) : m;
    let w = Ha({
      items: N,
      lookup: d
    });
    for (const m of g.values())
      w = m(w);
    f && l(N), w.length > 0 ? p?.(w) : h && window.requestAnimationFrame(() => {
      const { fitViewQueued: m, nodes: C, setNodes: y } = t.getState();
      m && y(C);
    });
  }, []), r = Ba(n), o = le((c) => {
    const { edges: u = [], setEdges: l, hasDefaultEdges: f, onEdgesChange: p, edgeLookup: d } = t.getState();
    let h = u;
    for (const g of c)
      h = typeof g == "function" ? g(h) : g;
    f ? l(h) : p && p(Ha({
      items: h,
      lookup: d
    }));
  }, []), s = Ba(o), a = de(() => ({ nodeQueue: r, edgeQueue: s }), []);
  return i.jsx(gu.Provider, { value: a, children: e });
}
function Gm() {
  const e = dr(gu);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Jm = (e) => !!e.panZoom;
function hs() {
  const e = Bm(), t = Ne(), n = Gm(), r = pe(Jm), o = de(() => {
    const s = (p) => t.getState().nodeLookup.get(p), a = (p) => {
      n.nodeQueue.push(p);
    }, c = (p) => {
      n.edgeQueue.push(p);
    }, u = (p) => {
      const { nodeLookup: d, nodeOrigin: h } = t.getState(), g = Fa(p) ? p : d.get(p.id), N = g.parentId ? zl(g.position, g.measured, g.parentId, d, h) : g.position, w = {
        ...g,
        position: N,
        width: g.measured?.width ?? g.width,
        height: g.measured?.height ?? g.height
      };
      return mn(w);
    }, l = (p, d, h = { replace: !1 }) => {
      a((g) => g.map((N) => {
        if (N.id === p) {
          const w = typeof d == "function" ? d(N) : d;
          return h.replace && Fa(w) ? w : { ...N, ...w };
        }
        return N;
      }));
    }, f = (p, d, h = { replace: !1 }) => {
      c((g) => g.map((N) => {
        if (N.id === p) {
          const w = typeof d == "function" ? d(N) : d;
          return h.replace && Xm(w) ? w : { ...N, ...w };
        }
        return N;
      }));
    };
    return {
      getNodes: () => t.getState().nodes.map((p) => ({ ...p })),
      getNode: (p) => s(p)?.internals.userNode,
      getInternalNode: s,
      getEdges: () => {
        const { edges: p = [] } = t.getState();
        return p.map((d) => ({ ...d }));
      },
      getEdge: (p) => t.getState().edgeLookup.get(p),
      setNodes: a,
      setEdges: c,
      addNodes: (p) => {
        const d = Array.isArray(p) ? p : [p];
        n.nodeQueue.push((h) => [...h, ...d]);
      },
      addEdges: (p) => {
        const d = Array.isArray(p) ? p : [p];
        n.edgeQueue.push((h) => [...h, ...d]);
      },
      toObject: () => {
        const { nodes: p = [], edges: d = [], transform: h } = t.getState(), [g, N, w] = h;
        return {
          nodes: p.map((m) => ({ ...m })),
          edges: d.map((m) => ({ ...m })),
          viewport: {
            x: g,
            y: N,
            zoom: w
          }
        };
      },
      deleteElements: async ({ nodes: p = [], edges: d = [] }) => {
        const { nodes: h, edges: g, onNodesDelete: N, onEdgesDelete: w, triggerNodeChanges: m, triggerEdgeChanges: C, onDelete: y, onBeforeDelete: b } = t.getState(), { nodes: j, edges: v } = await wy({
          nodesToRemove: p,
          edgesToRemove: d,
          nodes: h,
          edges: g,
          onBeforeDelete: b
        }), S = v.length > 0, A = j.length > 0;
        if (S) {
          const M = v.map(Wa);
          w?.(v), C(M);
        }
        if (A) {
          const M = j.map(Wa);
          N?.(j), m(M);
        }
        return (A || S) && y?.({ nodes: j, edges: v }), { deletedNodes: j, deletedEdges: v };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (p, d = !0, h) => {
        const g = pa(p), N = g ? p : u(p), w = h !== void 0;
        return N ? (h || t.getState().nodes).filter((m) => {
          const C = t.getState().nodeLookup.get(m.id);
          if (C && !g && (m.id === p.id || !C.internals.positionAbsolute))
            return !1;
          const y = mn(w ? m : C), b = ar(y, N);
          return d && b > 0 || b >= y.width * y.height || b >= N.width * N.height;
        }) : [];
      },
      isNodeIntersecting: (p, d, h = !0) => {
        const N = pa(p) ? p : u(p);
        if (!N)
          return !1;
        const w = ar(N, d);
        return h && w > 0 || w >= d.width * d.height || w >= N.width * N.height;
      },
      updateNode: l,
      updateNodeData: (p, d, h = { replace: !1 }) => {
        l(p, (g) => {
          const N = typeof d == "function" ? d(g) : d;
          return h.replace ? { ...g, data: N } : { ...g, data: { ...g.data, ...N } };
        }, h);
      },
      updateEdge: f,
      updateEdgeData: (p, d, h = { replace: !1 }) => {
        f(p, (g) => {
          const N = typeof d == "function" ? d(g) : d;
          return h.replace ? { ...g, data: N } : { ...g, data: { ...g.data, ...N } };
        }, h);
      },
      getNodesBounds: (p) => {
        const { nodeLookup: d, nodeOrigin: h } = t.getState();
        return gy(p, { nodeLookup: d, nodeOrigin: h });
      },
      getHandleConnections: ({ type: p, id: d, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}-${p}${d ? `-${d}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: p, handleId: d, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}${p ? d ? `-${p}-${d}` : `-${p}` : ""}`)?.values() ?? []),
      fitView: async (p) => {
        const d = t.getState().fitViewResolver ?? Ny();
        return t.setState({ fitViewQueued: !0, fitViewOptions: p, fitViewResolver: d }), n.nodeQueue.push((h) => [...h]), d.promise;
      }
    };
  }, []);
  return de(() => ({
    ...o,
    ...e,
    viewportInitialized: r
  }), [r]);
}
const Ka = (e) => e.selected, Qm = typeof window < "u" ? window : void 0;
function ex({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = Ne(), { deleteElements: r } = hs(), o = lr(e, { actInsideInputWithModifier: !1 }), s = lr(t, { target: Qm });
  Q(() => {
    if (o) {
      const { edges: a, nodes: c } = n.getState();
      r({ nodes: c.filter(Ka), edges: a.filter(Ka) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [o]), Q(() => {
    n.setState({ multiSelectionActive: s });
  }, [s]);
}
function tx(e) {
  const t = Ne();
  Q(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const r = cs(e.current);
      (r.height === 0 || r.width === 0) && t.getState().onError?.("004", qe.error004()), t.setState({ width: r.width || 500, height: r.height || 500 });
    };
    if (e.current) {
      n(), window.addEventListener("resize", n);
      const r = new ResizeObserver(() => n());
      return r.observe(e.current), () => {
        window.removeEventListener("resize", n), r && e.current && r.unobserve(e.current);
      };
    }
  }, []);
}
const Lo = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, nx = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function rx({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: r = !1, panOnScrollSpeed: o = 0.5, panOnScrollMode: s = Pt.Free, zoomOnDoubleClick: a = !0, panOnDrag: c = !0, defaultViewport: u, translateExtent: l, minZoom: f, maxZoom: p, zoomActivationKeyCode: d, preventScrolling: h = !0, children: g, noWheelClassName: N, noPanClassName: w, onViewportChange: m, isControlledViewport: C, paneClickDistance: y, selectionOnDrag: b }) {
  const j = Ne(), v = ie(null), { userSelectionActive: S, lib: A, connectionInProgress: M } = pe(nx, be), R = lr(d), $ = ie();
  tx(v);
  const _ = le((V) => {
    m?.({ x: V[0], y: V[1], zoom: V[2] }), C || j.setState({ transform: V });
  }, [m, C]);
  return Q(() => {
    if (v.current) {
      $.current = im({
        domNode: v.current,
        minZoom: f,
        maxZoom: p,
        translateExtent: l,
        viewport: u,
        onDraggingChange: (k) => j.setState((T) => T.paneDragging === k ? T : { paneDragging: k }),
        onPanZoomStart: (k, T) => {
          const { onViewportChangeStart: L, onMoveStart: P } = j.getState();
          P?.(k, T), L?.(T);
        },
        onPanZoom: (k, T) => {
          const { onViewportChange: L, onMove: P } = j.getState();
          P?.(k, T), L?.(T);
        },
        onPanZoomEnd: (k, T) => {
          const { onViewportChangeEnd: L, onMoveEnd: P } = j.getState();
          P?.(k, T), L?.(T);
        }
      });
      const { x: V, y: E, zoom: D } = $.current.getViewport();
      return j.setState({
        panZoom: $.current,
        transform: [V, E, D],
        domNode: v.current.closest(".react-flow")
      }), () => {
        $.current?.destroy();
      };
    }
  }, []), Q(() => {
    $.current?.update({
      onPaneContextMenu: e,
      zoomOnScroll: t,
      zoomOnPinch: n,
      panOnScroll: r,
      panOnScrollSpeed: o,
      panOnScrollMode: s,
      zoomOnDoubleClick: a,
      panOnDrag: c,
      zoomActivationKeyPressed: R,
      preventScrolling: h,
      noPanClassName: w,
      userSelectionActive: S,
      noWheelClassName: N,
      lib: A,
      onTransformChange: _,
      connectionInProgress: M,
      selectionOnDrag: b,
      paneClickDistance: y
    });
  }, [
    e,
    t,
    n,
    r,
    o,
    s,
    a,
    c,
    R,
    h,
    w,
    S,
    N,
    A,
    _,
    M,
    b,
    y
  ]), i.jsx("div", { className: "react-flow__renderer", ref: v, style: Lo, children: g });
}
const ox = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function ix() {
  const { userSelectionActive: e, userSelectionRect: t } = pe(ox, be);
  return e && t ? i.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const yi = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, sx = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function ax({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = sr.Full, panOnDrag: r, autoPanOnSelection: o, paneClickDistance: s, selectionOnDrag: a, onSelectionStart: c, onSelectionEnd: u, onPaneClick: l, onPaneContextMenu: f, onPaneScroll: p, onPaneMouseEnter: d, onPaneMouseMove: h, onPaneMouseLeave: g, children: N }) {
  const w = ie(0), m = Ne(), { userSelectionActive: C, elementsSelectable: y, dragging: b, connectionInProgress: j, panBy: v, autoPanSpeed: S } = pe(sx, be), A = y && (e || C), M = ie(null), R = ie(), $ = ie(/* @__PURE__ */ new Set()), _ = ie(/* @__PURE__ */ new Set()), V = ie(!1), E = ie({ x: 0, y: 0 }), D = ie(!1), k = (O) => {
    if (V.current || j) {
      V.current = !1;
      return;
    }
    l?.(O), m.getState().resetSelectedElements(), m.setState({ nodesSelectionActive: !1 });
  }, T = (O) => {
    if (Array.isArray(r) && r?.includes(2)) {
      O.preventDefault();
      return;
    }
    f?.(O);
  }, L = p ? (O) => p(O) : void 0, P = (O) => {
    V.current && (O.stopPropagation(), V.current = !1);
  }, K = (O) => {
    const { domNode: Y, transform: se } = m.getState();
    if (R.current = Y?.getBoundingClientRect(), !R.current)
      return;
    const ae = O.target === M.current;
    if (!ae && !!O.target.closest(".nokey") || !e || !(a && ae || t) || O.button !== 0 || !O.isPrimary)
      return;
    O.target?.setPointerCapture?.(O.pointerId), V.current = !1;
    const { x: fe, y: W } = et(O.nativeEvent, R.current), ee = Nn({ x: fe, y: W }, se);
    m.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: ee.x,
        startY: ee.y,
        x: fe,
        y: W
      }
    }), ae || (O.stopPropagation(), O.preventDefault());
  };
  function F(O, Y) {
    const { userSelectionRect: se } = m.getState();
    if (!se)
      return;
    const { transform: ae, nodeLookup: te, edgeLookup: re, connectionLookup: fe, triggerNodeChanges: W, triggerEdgeChanges: ee, defaultEdgeOptions: ye } = m.getState(), we = { x: se.startX, y: se.startY }, { x: $e, y: _e } = xn(we, ae), Le = {
      startX: we.x,
      startY: we.y,
      x: O < $e ? O : $e,
      y: Y < _e ? Y : _e,
      width: Math.abs(O - $e),
      height: Math.abs(Y - _e)
    }, Me = $.current, Ye = _.current;
    $.current = new Set(is(te, Le, ae, n === sr.Partial, !0).map((Ce) => Ce.id)), _.current = /* @__PURE__ */ new Set();
    const ke = ye?.selectable ?? !0;
    for (const Ce of $.current) {
      const me = fe.get(Ce);
      if (me)
        for (const { edgeId: Pe } of me.values()) {
          const ct = re.get(Pe);
          ct && (ct.selectable ?? ke) && _.current.add(Pe);
        }
    }
    if (!ha(Me, $.current)) {
      const Ce = an(te, $.current, !0);
      W(Ce);
    }
    if (!ha(Ye, _.current)) {
      const Ce = an(re, _.current);
      ee(Ce);
    }
    m.setState({
      userSelectionRect: Le,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function B() {
    if (!o || !R.current)
      return;
    const [O, Y] = ss(E.current, R.current, S);
    v({ x: O, y: Y }).then((se) => {
      if (!V.current || !se) {
        w.current = requestAnimationFrame(B);
        return;
      }
      const { x: ae, y: te } = E.current;
      F(ae, te), w.current = requestAnimationFrame(B);
    });
  }
  const q = () => {
    cancelAnimationFrame(w.current), w.current = 0, D.current = !1;
  };
  Q(() => () => q(), []);
  const J = (O) => {
    const { userSelectionRect: Y, transform: se, resetSelectedElements: ae } = m.getState();
    if (!R.current || !Y)
      return;
    const { x: te, y: re } = et(O.nativeEvent, R.current);
    E.current = { x: te, y: re };
    const fe = xn({ x: Y.startX, y: Y.startY }, se);
    if (!V.current) {
      const W = t ? 0 : s;
      if (Math.hypot(te - fe.x, re - fe.y) <= W)
        return;
      ae(), c?.(O);
    }
    V.current = !0, D.current || (B(), D.current = !0), F(te, re);
  }, X = (O) => {
    O.button === 0 && (O.target?.releasePointerCapture?.(O.pointerId), !C && O.target === M.current && m.getState().userSelectionRect && k?.(O), m.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), V.current && (u?.(O), m.setState({
      nodesSelectionActive: $.current.size > 0
    })), q());
  }, ce = (O) => {
    O.target?.releasePointerCapture?.(O.pointerId), q();
  }, G = r === !0 || Array.isArray(r) && r.includes(0);
  return i.jsxs("div", { className: Ee(["react-flow__pane", { draggable: G, dragging: b, selection: e }]), onClick: A ? void 0 : yi(k, M), onContextMenu: yi(T, M), onWheel: yi(L, M), onPointerEnter: A ? void 0 : d, onPointerMove: A ? J : h, onPointerUp: A ? X : void 0, onPointerCancel: A ? ce : void 0, onPointerDownCapture: A ? K : void 0, onClickCapture: A ? P : void 0, onPointerLeave: g, ref: M, style: Lo, children: [N, i.jsx(ix, {})] });
}
function Mi({ id: e, store: t, unselect: n = !1, nodeRef: r }) {
  const { addSelectedNodes: o, unselectNodesAndEdges: s, multiSelectionActive: a, nodeLookup: c, onError: u } = t.getState(), l = c.get(e);
  if (!l) {
    u?.("012", qe.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && a) && (s({ nodes: [l], edges: [] }), requestAnimationFrame(() => r?.current?.blur())) : o([e]);
}
function yu({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: r, nodeId: o, isSelectable: s, nodeClickDistance: a }) {
  const c = Ne(), [u, l] = U(!1), f = ie();
  return Q(() => {
    f.current = Ky({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (p) => {
        Mi({
          id: p,
          store: c,
          nodeRef: e
        });
      },
      onDragStart: () => {
        l(!0);
      },
      onDragStop: () => {
        l(!1);
      }
    });
  }, []), Q(() => {
    if (!(t || !e.current || !f.current))
      return f.current.update({
        noDragClassName: n,
        handleSelector: r,
        domNode: e.current,
        isSelectable: s,
        nodeId: o,
        nodeClickDistance: a
      }), () => {
        f.current?.destroy();
      };
  }, [n, r, t, s, e, o, a]), u;
}
const cx = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function mu() {
  const e = Ne();
  return le((n) => {
    const { nodeExtent: r, snapToGrid: o, snapGrid: s, nodesDraggable: a, onError: c, updateNodePositions: u, nodeLookup: l, nodeOrigin: f } = e.getState(), p = /* @__PURE__ */ new Map(), d = cx(a), h = o ? s[0] : 5, g = o ? s[1] : 5, N = n.direction.x * h * n.factor, w = n.direction.y * g * n.factor;
    for (const [, m] of l) {
      if (!d(m))
        continue;
      let C = {
        x: m.internals.positionAbsolute.x + N,
        y: m.internals.positionAbsolute.y + w
      };
      o && (C = yr(C, s));
      const { position: y, positionAbsolute: b } = $l({
        nodeId: m.id,
        nextPosition: C,
        nodeLookup: l,
        nodeExtent: r,
        nodeOrigin: f,
        onError: c
      });
      m.position = y, m.internals.positionAbsolute = b, p.set(m.id, m);
    }
    u(p);
  }, []);
}
const gs = Xi(null), lx = gs.Provider;
gs.Consumer;
const xu = () => dr(gs), ux = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), dx = (e, t, n) => (r) => {
  const { connectionClickStartHandle: o, connectionMode: s, connection: a } = r, { fromHandle: c, toHandle: u, isValid: l } = a, f = u?.nodeId === e && u?.id === t && u?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: f,
    clickConnecting: o?.nodeId === e && o?.id === t && o?.type === n,
    isPossibleEndHandle: s === gn.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!o,
    valid: f && l
  };
};
function fx({ type: e = "source", position: t = ne.Top, isValidConnection: n, isConnectable: r = !0, isConnectableStart: o = !0, isConnectableEnd: s = !0, id: a, onConnect: c, children: u, className: l, onMouseDown: f, onTouchStart: p, ...d }, h) {
  const g = a || null, N = e === "target", w = Ne(), m = xu(), { connectOnClick: C, noPanClassName: y, rfId: b } = pe(ux, be), { connectingFrom: j, connectingTo: v, clickConnecting: S, isPossibleEndHandle: A, connectionInProcess: M, clickConnectionInProcess: R, valid: $ } = pe(dx(m, g, e), be);
  m || w.getState().onError?.("010", qe.error010());
  const _ = (D) => {
    const { defaultEdgeOptions: k, onConnect: T, hasDefaultEdges: L } = w.getState(), P = {
      ...k,
      ...D
    };
    if (L) {
      const { edges: K, setEdges: F, onError: B } = w.getState();
      F(pu(P, K, { onError: B }));
    }
    T?.(P), c?.(P);
  }, V = (D) => {
    if (!m)
      return;
    const k = Hl(D.nativeEvent);
    if (o && (k && D.button === 0 || !k)) {
      const T = w.getState();
      $i.onPointerDown(D.nativeEvent, {
        handleDomNode: D.currentTarget,
        autoPanOnConnect: T.autoPanOnConnect,
        connectionMode: T.connectionMode,
        connectionRadius: T.connectionRadius,
        domNode: T.domNode,
        nodeLookup: T.nodeLookup,
        lib: T.lib,
        isTarget: N,
        handleId: g,
        nodeId: m,
        flowId: T.rfId,
        panBy: T.panBy,
        cancelConnection: T.cancelConnection,
        onConnectStart: T.onConnectStart,
        onConnectEnd: (...L) => w.getState().onConnectEnd?.(...L),
        updateConnection: T.updateConnection,
        onConnect: _,
        isValidConnection: n || ((...L) => w.getState().isValidConnection?.(...L) ?? !0),
        getTransform: () => w.getState().transform,
        getFromHandle: () => w.getState().connection.fromHandle,
        autoPanSpeed: T.autoPanSpeed,
        dragThreshold: T.connectionDragThreshold
      });
    }
    k ? f?.(D) : p?.(D);
  }, E = (D) => {
    const { onClickConnectStart: k, onClickConnectEnd: T, connectionClickStartHandle: L, connectionMode: P, isValidConnection: K, lib: F, rfId: B, nodeLookup: q, connection: J } = w.getState();
    if (!m || !L && !o)
      return;
    if (!L) {
      k?.(D.nativeEvent, { nodeId: m, handleId: g, handleType: e }), w.setState({ connectionClickStartHandle: { nodeId: m, type: e, id: g } });
      return;
    }
    const X = Vl(D.target), ce = n || K, { connection: G, isValid: O } = $i.isValid(D.nativeEvent, {
      handle: {
        nodeId: m,
        id: g,
        type: e
      },
      connectionMode: P,
      fromNodeId: L.nodeId,
      fromHandleId: L.id || null,
      fromType: L.type,
      isValidConnection: ce,
      flowId: B,
      doc: X,
      lib: F,
      nodeLookup: q
    });
    O && G && _(G);
    const Y = structuredClone(J);
    delete Y.inProgress, Y.toPosition = Y.toHandle ? Y.toHandle.position : null, T?.(D, Y), w.setState({ connectionClickStartHandle: null });
  };
  return i.jsx("div", { "data-handleid": g, "data-nodeid": m, "data-handlepos": t, "data-id": `${b}-${m}-${g}-${e}`, className: Ee([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    y,
    l,
    {
      source: !N,
      target: N,
      connectable: r,
      connectablestart: o,
      connectableend: s,
      clickconnecting: S,
      connectingfrom: j,
      connectingto: v,
      valid: $,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: r && (!M || A) && (M || R ? s : o)
    }
  ]), onMouseDown: V, onTouchStart: V, onClick: C ? E : void 0, ref: h, ...d, children: u });
}
const vn = je(hu(fx));
function px({ data: e, isConnectable: t, sourcePosition: n = ne.Bottom }) {
  return i.jsxs(i.Fragment, { children: [e?.label, i.jsx(vn, { type: "source", position: n, isConnectable: t })] });
}
function hx({ data: e, isConnectable: t, targetPosition: n = ne.Top, sourcePosition: r = ne.Bottom }) {
  return i.jsxs(i.Fragment, { children: [i.jsx(vn, { type: "target", position: n, isConnectable: t }), e?.label, i.jsx(vn, { type: "source", position: r, isConnectable: t })] });
}
function gx() {
  return null;
}
function yx({ data: e, isConnectable: t, targetPosition: n = ne.Top }) {
  return i.jsxs(i.Fragment, { children: [i.jsx(vn, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const mo = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, Ua = {
  input: px,
  default: hx,
  output: yx,
  group: gx
};
function mx(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const xx = (e) => {
  const { width: t, height: n, x: r, y: o } = gr(e.nodeLookup, {
    filter: (s) => !!s.selected
  });
  return {
    width: Qe(t) ? t : null,
    height: Qe(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${r}px,${o}px)`
  };
};
function wx({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const r = Ne(), { width: o, height: s, transformString: a, userSelectionActive: c } = pe(xx, be), u = mu(), l = ie(null);
  Q(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const f = !c && o !== null && s !== null;
  if (yu({
    nodeRef: l,
    disabled: !f
  }), !f)
    return null;
  const p = e ? (h) => {
    const g = r.getState().nodes.filter((N) => N.selected);
    e(h, g);
  } : void 0, d = (h) => {
    Object.prototype.hasOwnProperty.call(mo, h.key) && (h.preventDefault(), u({
      direction: mo[h.key],
      factor: h.shiftKey ? 4 : 1
    }));
  };
  return i.jsx("div", { className: Ee(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: a
  }, children: i.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: p, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : d, style: {
    width: o,
    height: s
  } }) });
}
const Xa = typeof window < "u" ? window : void 0, vx = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function wu({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: r, onPaneMouseLeave: o, onPaneContextMenu: s, onPaneScroll: a, paneClickDistance: c, deleteKeyCode: u, selectionKeyCode: l, selectionOnDrag: f, selectionMode: p, onSelectionStart: d, onSelectionEnd: h, multiSelectionKeyCode: g, panActivationKeyCode: N, zoomActivationKeyCode: w, elementsSelectable: m, zoomOnScroll: C, zoomOnPinch: y, panOnScroll: b, panOnScrollSpeed: j, panOnScrollMode: v, zoomOnDoubleClick: S, panOnDrag: A, autoPanOnSelection: M, defaultViewport: R, translateExtent: $, minZoom: _, maxZoom: V, preventScrolling: E, onSelectionContextMenu: D, noWheelClassName: k, noPanClassName: T, disableKeyboardA11y: L, onViewportChange: P, isControlledViewport: K }) {
  const { nodesSelectionActive: F, userSelectionActive: B } = pe(vx, be), q = lr(l, { target: Xa }), J = lr(N, { target: Xa }), X = J || A, ce = J || b, G = f && X !== !0, O = q || B || G;
  return ex({ deleteKeyCode: u, multiSelectionKeyCode: g }), i.jsx(rx, { onPaneContextMenu: s, elementsSelectable: m, zoomOnScroll: C, zoomOnPinch: y, panOnScroll: ce, panOnScrollSpeed: j, panOnScrollMode: v, zoomOnDoubleClick: S, panOnDrag: !q && X, defaultViewport: R, translateExtent: $, minZoom: _, maxZoom: V, zoomActivationKeyCode: w, preventScrolling: E, noWheelClassName: k, noPanClassName: T, onViewportChange: P, isControlledViewport: K, paneClickDistance: c, selectionOnDrag: G, children: i.jsxs(ax, { onSelectionStart: d, onSelectionEnd: h, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: r, onPaneMouseLeave: o, onPaneContextMenu: s, onPaneScroll: a, panOnDrag: X, autoPanOnSelection: M, isSelecting: !!O, selectionMode: p, selectionKeyPressed: q, paneClickDistance: c, selectionOnDrag: G, children: [e, F && i.jsx(wx, { onSelectionContextMenu: D, noPanClassName: T, disableKeyboardA11y: L })] }) });
}
wu.displayName = "FlowRenderer";
const bx = je(wu), Nx = (e) => (t) => e ? is(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function jx(e) {
  return pe(le(Nx(e), [e]), be);
}
const Sx = (e) => e.updateNodeInternals;
function Ex() {
  const e = pe(Sx), [t] = U(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
    const r = /* @__PURE__ */ new Map();
    n.forEach((o) => {
      const s = o.target.getAttribute("data-id");
      r.set(s, {
        id: s,
        nodeElement: o.target,
        force: !0
      });
    }), e(r);
  }));
  return Q(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function Cx({ node: e, nodeType: t, hasDimensions: n, resizeObserver: r }) {
  const o = Ne(), s = ie(null), a = ie(null), c = ie(e.sourcePosition), u = ie(e.targetPosition), l = ie(t), f = n && !!e.internals.handleBounds;
  return Q(() => {
    s.current && !e.hidden && (!f || a.current !== s.current) && (a.current && r?.unobserve(a.current), r?.observe(s.current), a.current = s.current);
  }, [f, e.hidden]), Q(() => () => {
    a.current && (r?.unobserve(a.current), a.current = null);
  }, []), Q(() => {
    if (s.current) {
      const p = l.current !== t, d = c.current !== e.sourcePosition, h = u.current !== e.targetPosition;
      (p || d || h) && (l.current = t, c.current = e.sourcePosition, u.current = e.targetPosition, o.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: s.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), s;
}
function Ix({ id: e, onClick: t, onMouseEnter: n, onMouseMove: r, onMouseLeave: o, onContextMenu: s, onDoubleClick: a, nodesDraggable: c, elementsSelectable: u, nodesConnectable: l, nodesFocusable: f, resizeObserver: p, noDragClassName: d, noPanClassName: h, disableKeyboardA11y: g, rfId: N, nodeTypes: w, nodeClickDistance: m, onError: C }) {
  const { node: y, internals: b, isParent: j } = pe((O) => {
    const Y = O.nodeLookup.get(e), se = O.parentLookup.has(e);
    return {
      node: Y,
      internals: Y.internals,
      isParent: se
    };
  }, be);
  let v = y.type || "default", S = w?.[v] || Ua[v];
  S === void 0 && (C?.("003", qe.error003(v)), v = "default", S = w?.default || Ua.default);
  const A = !!(y.draggable || c && typeof y.draggable > "u"), M = !!(y.selectable || u && typeof y.selectable > "u"), R = !!(y.connectable || l && typeof y.connectable > "u"), $ = !!(y.focusable || f && typeof y.focusable > "u"), _ = Ne(), V = Ll(y), E = Cx({ node: y, nodeType: v, hasDimensions: V, resizeObserver: p }), D = yu({
    nodeRef: E,
    disabled: y.hidden || !A,
    noDragClassName: d,
    handleSelector: y.dragHandle,
    nodeId: e,
    isSelectable: M,
    nodeClickDistance: m
  }), k = mu();
  if (y.hidden)
    return null;
  const T = gt(y), L = mx(y), P = M || A || t || n || r || o, K = n ? (O) => n(O, { ...b.userNode }) : void 0, F = r ? (O) => r(O, { ...b.userNode }) : void 0, B = o ? (O) => o(O, { ...b.userNode }) : void 0, q = s ? (O) => s(O, { ...b.userNode }) : void 0, J = a ? (O) => a(O, { ...b.userNode }) : void 0, X = (O) => {
    const { selectNodesOnDrag: Y, nodeDragThreshold: se } = _.getState();
    M && (!Y || !A || se > 0) && Mi({
      id: e,
      store: _,
      nodeRef: E
    }), t && t(O, { ...b.userNode });
  }, ce = (O) => {
    if (!(Ol(O.nativeEvent) || g)) {
      if (kl.includes(O.key) && M) {
        const Y = O.key === "Escape";
        Mi({
          id: e,
          store: _,
          unselect: Y,
          nodeRef: E
        });
      } else if (A && y.selected && Object.prototype.hasOwnProperty.call(mo, O.key)) {
        O.preventDefault();
        const { ariaLabelConfig: Y } = _.getState();
        _.setState({
          ariaLiveMessage: Y["node.a11yDescription.ariaLiveMessage"]({
            direction: O.key.replace("Arrow", "").toLowerCase(),
            x: ~~b.positionAbsolute.x,
            y: ~~b.positionAbsolute.y
          })
        }), k({
          direction: mo[O.key],
          factor: O.shiftKey ? 4 : 1
        });
      }
    }
  }, G = () => {
    if (g || !E.current?.matches(":focus-visible"))
      return;
    const { transform: O, width: Y, height: se, autoPanOnNodeFocus: ae, setCenter: te } = _.getState();
    if (!ae)
      return;
    is(/* @__PURE__ */ new Map([[e, y]]), { x: 0, y: 0, width: Y, height: se }, O, !0).length > 0 || te(y.position.x + T.width / 2, y.position.y + T.height / 2, {
      zoom: O[2]
    });
  };
  return i.jsx("div", { className: Ee([
    "react-flow__node",
    `react-flow__node-${v}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [h]: A
    },
    y.className,
    {
      selected: y.selected,
      selectable: M,
      parent: j,
      draggable: A,
      dragging: D
    }
  ]), ref: E, style: {
    zIndex: b.z,
    transform: `translate(${b.positionAbsolute.x}px,${b.positionAbsolute.y}px)`,
    pointerEvents: P ? "all" : "none",
    visibility: V ? "visible" : "hidden",
    ...y.style,
    ...L
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: K, onMouseMove: F, onMouseLeave: B, onContextMenu: q, onClick: X, onDoubleClick: J, onKeyDown: $ ? ce : void 0, tabIndex: $ ? 0 : void 0, onFocus: $ ? G : void 0, role: y.ariaRole ?? ($ ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": g ? void 0 : `${su}-${N}`, "aria-label": y.ariaLabel, ...y.domAttributes, children: i.jsx(lx, { value: e, children: i.jsx(S, { id: e, data: y.data, type: v, positionAbsoluteX: b.positionAbsolute.x, positionAbsoluteY: b.positionAbsolute.y, selected: y.selected ?? !1, selectable: M, draggable: A, deletable: y.deletable ?? !0, isConnectable: R, sourcePosition: y.sourcePosition, targetPosition: y.targetPosition, dragging: D, dragHandle: y.dragHandle, zIndex: b.z, parentId: y.parentId, ...T }) }) });
}
var kx = je(Ix);
const Ax = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function vu(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: r, elementsSelectable: o, onError: s } = pe(Ax, be), a = jx(e.onlyRenderVisibleElements), c = Ex();
  return i.jsx("div", { className: "react-flow__nodes", style: Lo, children: a.map((u) => (
    /*
     * The split of responsibilities between NodeRenderer and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For example, when you’re dragging a single node, that node gets
     * updated multiple times per second. If `NodeRenderer` were to update
     * every time, it would have to re-run the `nodes.map()` loop every
     * time. This gets pricey with hundreds of nodes, especially if every
     * loop cycle does more than just rendering a JSX element!
     *
     * As a result of this choice, we took the following implementation
     * decisions:
     * - NodeRenderer subscribes *only* to node IDs – and therefore
     *   rerender *only* when visible nodes are added or removed.
     * - NodeRenderer performs all operations the result of which can be
     *   shared between nodes (such as creating the `ResizeObserver`
     *   instance, or subscribing to `selector`). This means extra prop
     *   drilling into `NodeComponentWrapper`, but it means we need to run
     *   these operations only once – instead of once per node.
     * - Any operations that you’d normally write inside `nodes.map` are
     *   moved into `NodeComponentWrapper`. This ensures they are
     *   memorized – so if `NodeRenderer` *has* to rerender, it only
     *   needs to regenerate the list of nodes, nothing else.
     */
    i.jsx(kx, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: r, elementsSelectable: o, nodeClickDistance: e.nodeClickDistance, onError: s }, u)
  )) });
}
vu.displayName = "NodeRenderer";
const _x = je(vu);
function Dx(e) {
  return pe(le((n) => {
    if (!e)
      return n.edges.map((o) => o.id);
    const r = [];
    if (n.width && n.height)
      for (const o of n.edges) {
        const s = n.nodeLookup.get(o.source), a = n.nodeLookup.get(o.target);
        s && a && Cy({
          sourceNode: s,
          targetNode: a,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && r.push(o.id);
      }
    return r;
  }, [e]), be);
}
const Tx = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return i.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, $x = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return i.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, qa = {
  [ho.Arrow]: Tx,
  [ho.ArrowClosed]: $x
};
function Mx(e) {
  const t = Ne();
  return de(() => Object.prototype.hasOwnProperty.call(qa, e) ? qa[e] : (t.getState().onError?.("009", qe.error009(e)), null), [e]);
}
const Px = ({ id: e, type: t, color: n, width: r = 12.5, height: o = 12.5, markerUnits: s = "strokeWidth", strokeWidth: a, orient: c = "auto-start-reverse" }) => {
  const u = Mx(t);
  return u ? i.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${r}`, markerHeight: `${o}`, viewBox: "-10 -10 20 20", markerUnits: s, orient: c, refX: "0", refY: "0", children: i.jsx(u, { color: n, strokeWidth: a }) }) : null;
}, bu = ({ defaultColor: e, rfId: t }) => {
  const n = pe((s) => s.edges), r = pe((s) => s.defaultEdgeOptions), o = de(() => My(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: r?.markerStart,
    defaultMarkerEnd: r?.markerEnd
  }), [n, r, t, e]);
  return o.length ? i.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: i.jsx("defs", { children: o.map((s) => i.jsx(Px, { id: s.id, type: s.type, color: s.color, width: s.width, height: s.height, markerUnits: s.markerUnits, strokeWidth: s.strokeWidth, orient: s.orient }, s.id)) }) }) : null;
};
bu.displayName = "MarkerDefinitions";
var Rx = je(bu);
function Nu({ x: e, y: t, label: n, labelStyle: r, labelShowBg: o = !0, labelBgStyle: s, labelBgPadding: a = [2, 4], labelBgBorderRadius: c = 2, children: u, className: l, ...f }) {
  const [p, d] = U({ x: 1, y: 0, width: 0, height: 0 }), h = Ee(["react-flow__edge-textwrapper", l]), g = ie(null);
  return Q(() => {
    if (g.current) {
      const N = g.current.getBBox();
      d({
        x: N.x,
        y: N.y,
        width: N.width,
        height: N.height
      });
    }
  }, [n]), n ? i.jsxs("g", { transform: `translate(${e - p.width / 2} ${t - p.height / 2})`, className: h, visibility: p.width ? "visible" : "hidden", ...f, children: [o && i.jsx("rect", { width: p.width + 2 * a[0], x: -a[0], y: -a[1], height: p.height + 2 * a[1], className: "react-flow__edge-textbg", style: s, rx: c, ry: c }), i.jsx("text", { className: "react-flow__edge-text", y: p.height / 2, dy: "0.3em", ref: g, style: r, children: n }), u] }) : null;
}
Nu.displayName = "EdgeText";
const Lx = je(Nu);
function mr({ path: e, labelX: t, labelY: n, label: r, labelStyle: o, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u, interactionWidth: l = 20, ...f }) {
  return i.jsxs(i.Fragment, { children: [i.jsx("path", { ...f, d: e, fill: "none", className: Ee(["react-flow__edge-path", f.className]) }), l ? i.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, r && Qe(t) && Qe(n) ? i.jsx(Lx, { x: t, y: n, label: r, labelStyle: o, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u }) : null] });
}
function Ya({ pos: e, x1: t, y1: n, x2: r, y2: o }) {
  return e === ne.Left || e === ne.Right ? [0.5 * (t + r), n] : [t, 0.5 * (n + o)];
}
function ju({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: r, targetY: o, targetPosition: s = ne.Top }) {
  const [a, c] = Ya({
    pos: n,
    x1: e,
    y1: t,
    x2: r,
    y2: o
  }), [u, l] = Ya({
    pos: s,
    x1: r,
    y1: o,
    x2: e,
    y2: t
  }), [f, p, d, h] = Wl({
    sourceX: e,
    sourceY: t,
    targetX: r,
    targetY: o,
    sourceControlX: a,
    sourceControlY: c,
    targetControlX: u,
    targetControlY: l
  });
  return [
    `M${e},${t} C${a},${c} ${u},${l} ${r},${o}`,
    f,
    p,
    d,
    h
  ];
}
function Su(e) {
  return je(({ id: t, sourceX: n, sourceY: r, targetX: o, targetY: s, sourcePosition: a, targetPosition: c, label: u, labelStyle: l, labelShowBg: f, labelBgStyle: p, labelBgPadding: d, labelBgBorderRadius: h, style: g, markerEnd: N, markerStart: w, interactionWidth: m }) => {
    const [C, y, b] = ju({
      sourceX: n,
      sourceY: r,
      sourcePosition: a,
      targetX: o,
      targetY: s,
      targetPosition: c
    }), j = e.isInternal ? void 0 : t;
    return i.jsx(mr, { id: j, path: C, labelX: y, labelY: b, label: u, labelStyle: l, labelShowBg: f, labelBgStyle: p, labelBgPadding: d, labelBgBorderRadius: h, style: g, markerEnd: N, markerStart: w, interactionWidth: m });
  });
}
const zx = Su({ isInternal: !1 }), Eu = Su({ isInternal: !0 });
zx.displayName = "SimpleBezierEdge";
Eu.displayName = "SimpleBezierEdgeInternal";
function Cu(e) {
  return je(({ id: t, sourceX: n, sourceY: r, targetX: o, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: p, style: d, sourcePosition: h = ne.Bottom, targetPosition: g = ne.Top, markerEnd: N, markerStart: w, pathOptions: m, interactionWidth: C }) => {
    const [y, b, j] = yo({
      sourceX: n,
      sourceY: r,
      sourcePosition: h,
      targetX: o,
      targetY: s,
      targetPosition: g,
      borderRadius: m?.borderRadius,
      offset: m?.offset,
      stepPosition: m?.stepPosition
    }), v = e.isInternal ? void 0 : t;
    return i.jsx(mr, { id: v, path: y, labelX: b, labelY: j, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: p, style: d, markerEnd: N, markerStart: w, interactionWidth: C });
  });
}
const Iu = Cu({ isInternal: !1 }), ku = Cu({ isInternal: !0 });
Iu.displayName = "SmoothStepEdge";
ku.displayName = "SmoothStepEdgeInternal";
function Au(e) {
  return je(({ id: t, ...n }) => {
    const r = e.isInternal ? void 0 : t;
    return i.jsx(Iu, { ...n, id: r, pathOptions: de(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Vx = Au({ isInternal: !1 }), _u = Au({ isInternal: !0 });
Vx.displayName = "StepEdge";
_u.displayName = "StepEdgeInternal";
function Du(e) {
  return je(({ id: t, sourceX: n, sourceY: r, targetX: o, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: p, style: d, markerEnd: h, markerStart: g, interactionWidth: N }) => {
    const [w, m, C] = Ul({ sourceX: n, sourceY: r, targetX: o, targetY: s }), y = e.isInternal ? void 0 : t;
    return i.jsx(mr, { id: y, path: w, labelX: m, labelY: C, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: p, style: d, markerEnd: h, markerStart: g, interactionWidth: N });
  });
}
const Ox = Du({ isInternal: !1 }), Tu = Du({ isInternal: !0 });
Ox.displayName = "StraightEdge";
Tu.displayName = "StraightEdgeInternal";
function $u(e) {
  return je(({ id: t, sourceX: n, sourceY: r, targetX: o, targetY: s, sourcePosition: a = ne.Bottom, targetPosition: c = ne.Top, label: u, labelStyle: l, labelShowBg: f, labelBgStyle: p, labelBgPadding: d, labelBgBorderRadius: h, style: g, markerEnd: N, markerStart: w, pathOptions: m, interactionWidth: C }) => {
    const [y, b, j] = Fl({
      sourceX: n,
      sourceY: r,
      sourcePosition: a,
      targetX: o,
      targetY: s,
      targetPosition: c,
      curvature: m?.curvature
    }), v = e.isInternal ? void 0 : t;
    return i.jsx(mr, { id: v, path: y, labelX: b, labelY: j, label: u, labelStyle: l, labelShowBg: f, labelBgStyle: p, labelBgPadding: d, labelBgBorderRadius: h, style: g, markerEnd: N, markerStart: w, interactionWidth: C });
  });
}
const Hx = $u({ isInternal: !1 }), Mu = $u({ isInternal: !0 });
Hx.displayName = "BezierEdge";
Mu.displayName = "BezierEdgeInternal";
const Za = {
  default: Mu,
  straight: Tu,
  step: _u,
  smoothstep: ku,
  simplebezier: Eu
}, Ga = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, Wx = (e, t, n) => n === ne.Left ? e - t : n === ne.Right ? e + t : e, Fx = (e, t, n) => n === ne.Top ? e - t : n === ne.Bottom ? e + t : e, Ja = "react-flow__edgeupdater";
function Qa({ position: e, centerX: t, centerY: n, radius: r = 10, onMouseDown: o, onMouseEnter: s, onMouseOut: a, type: c }) {
  return i.jsx("circle", { onMouseDown: o, onMouseEnter: s, onMouseOut: a, className: Ee([Ja, `${Ja}-${c}`]), cx: Wx(t, r, e), cy: Fx(n, r, e), r, stroke: "transparent", fill: "transparent" });
}
function Bx({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: r, sourceY: o, targetX: s, targetY: a, sourcePosition: c, targetPosition: u, onReconnect: l, onReconnectStart: f, onReconnectEnd: p, setReconnecting: d, setUpdateHover: h }) {
  const g = Ne(), N = (b, j) => {
    if (b.button !== 0)
      return;
    const { autoPanOnConnect: v, domNode: S, connectionMode: A, connectionRadius: M, lib: R, onConnectStart: $, cancelConnection: _, nodeLookup: V, rfId: E, panBy: D, updateConnection: k } = g.getState(), T = j.type === "target", L = (F, B) => {
      d(!1), p?.(F, n, j.type, B);
    }, P = (F) => l?.(n, F), K = (F, B) => {
      d(!0), f?.(b, n, j.type), $?.(F, B);
    };
    $i.onPointerDown(b.nativeEvent, {
      autoPanOnConnect: v,
      connectionMode: A,
      connectionRadius: M,
      domNode: S,
      handleId: j.id,
      nodeId: j.nodeId,
      nodeLookup: V,
      isTarget: T,
      edgeUpdaterType: j.type,
      lib: R,
      flowId: E,
      cancelConnection: _,
      panBy: D,
      isValidConnection: (...F) => g.getState().isValidConnection?.(...F) ?? !0,
      onConnect: P,
      onConnectStart: K,
      onConnectEnd: (...F) => g.getState().onConnectEnd?.(...F),
      onReconnectEnd: L,
      updateConnection: k,
      getTransform: () => g.getState().transform,
      getFromHandle: () => g.getState().connection.fromHandle,
      dragThreshold: g.getState().connectionDragThreshold,
      handleDomNode: b.currentTarget
    });
  }, w = (b) => N(b, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), m = (b) => N(b, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), C = () => h(!0), y = () => h(!1);
  return i.jsxs(i.Fragment, { children: [(e === !0 || e === "source") && i.jsx(Qa, { position: c, centerX: r, centerY: o, radius: t, onMouseDown: w, onMouseEnter: C, onMouseOut: y, type: "source" }), (e === !0 || e === "target") && i.jsx(Qa, { position: u, centerX: s, centerY: a, radius: t, onMouseDown: m, onMouseEnter: C, onMouseOut: y, type: "target" })] });
}
function Kx({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: r, onClick: o, onDoubleClick: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, reconnectRadius: f, onReconnect: p, onReconnectStart: d, onReconnectEnd: h, rfId: g, edgeTypes: N, noPanClassName: w, onError: m, disableKeyboardA11y: C }) {
  let y = pe((te) => te.edgeLookup.get(e));
  const b = pe((te) => te.defaultEdgeOptions);
  y = b ? { ...b, ...y } : y;
  let j = y.type || "default", v = N?.[j] || Za[j];
  v === void 0 && (m?.("011", qe.error011(j)), j = "default", v = N?.default || Za.default);
  const S = !!(y.focusable || t && typeof y.focusable > "u"), A = typeof p < "u" && (y.reconnectable || n && typeof y.reconnectable > "u"), M = !!(y.selectable || r && typeof y.selectable > "u"), R = ie(null), [$, _] = U(!1), [V, E] = U(!1), D = Ne(), { zIndex: k, sourceX: T, sourceY: L, targetX: P, targetY: K, sourcePosition: F, targetPosition: B } = pe(le((te) => {
    const re = te.nodeLookup.get(y.source), fe = te.nodeLookup.get(y.target);
    if (!re || !fe)
      return {
        zIndex: y.zIndex,
        ...Ga
      };
    const W = $y({
      id: e,
      sourceNode: re,
      targetNode: fe,
      sourceHandle: y.sourceHandle || null,
      targetHandle: y.targetHandle || null,
      connectionMode: te.connectionMode,
      onError: m
    });
    return {
      zIndex: Ey({
        selected: y.selected,
        zIndex: y.zIndex,
        sourceNode: re,
        targetNode: fe,
        elevateOnSelect: te.elevateEdgesOnSelect,
        zIndexMode: te.zIndexMode
      }),
      ...W || Ga
    };
  }, [y.source, y.target, y.sourceHandle, y.targetHandle, y.selected, y.zIndex]), be), q = de(() => y.markerStart ? `url('#${Di(y.markerStart, g)}')` : void 0, [y.markerStart, g]), J = de(() => y.markerEnd ? `url('#${Di(y.markerEnd, g)}')` : void 0, [y.markerEnd, g]);
  if (y.hidden || T === null || L === null || P === null || K === null)
    return null;
  const X = (te) => {
    const { addSelectedEdges: re, unselectNodesAndEdges: fe, multiSelectionActive: W } = D.getState();
    M && (D.setState({ nodesSelectionActive: !1 }), y.selected && W ? (fe({ nodes: [], edges: [y] }), R.current?.blur()) : re([e])), o && o(te, y);
  }, ce = s ? (te) => {
    s(te, { ...y });
  } : void 0, G = a ? (te) => {
    a(te, { ...y });
  } : void 0, O = c ? (te) => {
    c(te, { ...y });
  } : void 0, Y = u ? (te) => {
    u(te, { ...y });
  } : void 0, se = l ? (te) => {
    l(te, { ...y });
  } : void 0, ae = (te) => {
    if (!C && kl.includes(te.key) && M) {
      const { unselectNodesAndEdges: re, addSelectedEdges: fe } = D.getState();
      te.key === "Escape" ? (R.current?.blur(), re({ edges: [y] })) : fe([e]);
    }
  };
  return i.jsx("svg", { style: { zIndex: k }, children: i.jsxs("g", { className: Ee([
    "react-flow__edge",
    `react-flow__edge-${j}`,
    y.className,
    w,
    {
      selected: y.selected,
      animated: y.animated,
      inactive: !M && !o,
      updating: $,
      selectable: M
    }
  ]), onClick: X, onDoubleClick: ce, onContextMenu: G, onMouseEnter: O, onMouseMove: Y, onMouseLeave: se, onKeyDown: S ? ae : void 0, tabIndex: S ? 0 : void 0, role: y.ariaRole ?? (S ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": y.ariaLabel === null ? void 0 : y.ariaLabel || `Edge from ${y.source} to ${y.target}`, "aria-describedby": S ? `${au}-${g}` : void 0, ref: R, ...y.domAttributes, children: [!V && i.jsx(v, { id: e, source: y.source, target: y.target, type: y.type, selected: y.selected, animated: y.animated, selectable: M, deletable: y.deletable ?? !0, label: y.label, labelStyle: y.labelStyle, labelShowBg: y.labelShowBg, labelBgStyle: y.labelBgStyle, labelBgPadding: y.labelBgPadding, labelBgBorderRadius: y.labelBgBorderRadius, sourceX: T, sourceY: L, targetX: P, targetY: K, sourcePosition: F, targetPosition: B, data: y.data, style: y.style, sourceHandleId: y.sourceHandle, targetHandleId: y.targetHandle, markerStart: q, markerEnd: J, pathOptions: "pathOptions" in y ? y.pathOptions : void 0, interactionWidth: y.interactionWidth }), A && i.jsx(Bx, { edge: y, isReconnectable: A, reconnectRadius: f, onReconnect: p, onReconnectStart: d, onReconnectEnd: h, sourceX: T, sourceY: L, targetX: P, targetY: K, sourcePosition: F, targetPosition: B, setUpdateHover: _, setReconnecting: E })] }) });
}
var Ux = je(Kx);
const Xx = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function Pu({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: r, noPanClassName: o, onReconnect: s, onEdgeContextMenu: a, onEdgeMouseEnter: c, onEdgeMouseMove: u, onEdgeMouseLeave: l, onEdgeClick: f, reconnectRadius: p, onEdgeDoubleClick: d, onReconnectStart: h, onReconnectEnd: g, disableKeyboardA11y: N }) {
  const { edgesFocusable: w, edgesReconnectable: m, elementsSelectable: C, onError: y } = pe(Xx, be), b = Dx(t);
  return i.jsxs("div", { className: "react-flow__edges", children: [i.jsx(Rx, { defaultColor: e, rfId: n }), b.map((j) => i.jsx(Ux, { id: j, edgesFocusable: w, edgesReconnectable: m, elementsSelectable: C, noPanClassName: o, onReconnect: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, onClick: f, reconnectRadius: p, onDoubleClick: d, onReconnectStart: h, onReconnectEnd: g, rfId: n, onError: y, edgeTypes: r, disableKeyboardA11y: N }, j))] });
}
Pu.displayName = "EdgeRenderer";
const qx = je(Pu), Yx = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function Zx({ children: e }) {
  const t = pe(Yx);
  return i.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function Gx(e) {
  const t = hs(), n = ie(!1);
  Q(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Jx = (e) => e.panZoom?.syncViewport;
function Qx(e) {
  const t = pe(Jx), n = Ne();
  return Q(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function ew(e) {
  return e.connection.inProgress ? { ...e.connection, to: Nn(e.connection.to, e.transform) } : { ...e.connection };
}
function tw(e) {
  return ew;
}
function nw(e) {
  const t = tw();
  return pe(t, be);
}
const rw = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function ow({ containerStyle: e, style: t, type: n, component: r }) {
  const { nodesConnectable: o, width: s, height: a, isValid: c, inProgress: u } = pe(rw, be);
  return !(s && o && u) ? null : i.jsx("svg", { style: e, width: s, height: a, className: "react-flow__connectionline react-flow__container", children: i.jsx("g", { className: Ee(["react-flow__connection", Dl(c)]), children: i.jsx(Ru, { style: t, type: n, CustomComponent: r, isValid: c }) }) });
}
const Ru = ({ style: e, type: t = bt.Bezier, CustomComponent: n, isValid: r }) => {
  const { inProgress: o, from: s, fromNode: a, fromHandle: c, fromPosition: u, to: l, toNode: f, toHandle: p, toPosition: d, pointer: h } = nw();
  if (!o)
    return;
  if (n)
    return i.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: a, fromHandle: c, fromX: s.x, fromY: s.y, toX: l.x, toY: l.y, fromPosition: u, toPosition: d, connectionStatus: Dl(r), toNode: f, toHandle: p, pointer: h });
  let g = "";
  const N = {
    sourceX: s.x,
    sourceY: s.y,
    sourcePosition: u,
    targetX: l.x,
    targetY: l.y,
    targetPosition: d
  };
  switch (t) {
    case bt.Bezier:
      [g] = Fl(N);
      break;
    case bt.SimpleBezier:
      [g] = ju(N);
      break;
    case bt.Step:
      [g] = yo({
        ...N,
        borderRadius: 0
      });
      break;
    case bt.SmoothStep:
      [g] = yo(N);
      break;
    default:
      [g] = Ul(N);
  }
  return i.jsx("path", { d: g, fill: "none", className: "react-flow__connection-path", style: e });
};
Ru.displayName = "ConnectionLine";
const iw = {};
function ec(e = iw) {
  ie(e), Ne(), Q(() => {
  }, [e]);
}
function sw() {
  Ne(), ie(!1), Q(() => {
  }, []);
}
function Lu({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: r, onEdgeClick: o, onNodeDoubleClick: s, onEdgeDoubleClick: a, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: f, onSelectionContextMenu: p, onSelectionStart: d, onSelectionEnd: h, connectionLineType: g, connectionLineStyle: N, connectionLineComponent: w, connectionLineContainerStyle: m, selectionKeyCode: C, selectionOnDrag: y, selectionMode: b, multiSelectionKeyCode: j, panActivationKeyCode: v, zoomActivationKeyCode: S, deleteKeyCode: A, onlyRenderVisibleElements: M, elementsSelectable: R, defaultViewport: $, translateExtent: _, minZoom: V, maxZoom: E, preventScrolling: D, defaultMarkerColor: k, zoomOnScroll: T, zoomOnPinch: L, panOnScroll: P, panOnScrollSpeed: K, panOnScrollMode: F, zoomOnDoubleClick: B, panOnDrag: q, autoPanOnSelection: J, onPaneClick: X, onPaneMouseEnter: ce, onPaneMouseMove: G, onPaneMouseLeave: O, onPaneScroll: Y, onPaneContextMenu: se, paneClickDistance: ae, nodeClickDistance: te, onEdgeContextMenu: re, onEdgeMouseEnter: fe, onEdgeMouseMove: W, onEdgeMouseLeave: ee, reconnectRadius: ye, onReconnect: we, onReconnectStart: $e, onReconnectEnd: _e, noDragClassName: Le, noWheelClassName: Me, noPanClassName: Ye, disableKeyboardA11y: ke, nodeExtent: Ce, rfId: me, viewport: Pe, onViewportChange: ct }) {
  return ec(e), ec(t), sw(), Gx(n), Qx(Pe), i.jsx(bx, { onPaneClick: X, onPaneMouseEnter: ce, onPaneMouseMove: G, onPaneMouseLeave: O, onPaneContextMenu: se, onPaneScroll: Y, paneClickDistance: ae, deleteKeyCode: A, selectionKeyCode: C, selectionOnDrag: y, selectionMode: b, onSelectionStart: d, onSelectionEnd: h, multiSelectionKeyCode: j, panActivationKeyCode: v, zoomActivationKeyCode: S, elementsSelectable: R, zoomOnScroll: T, zoomOnPinch: L, zoomOnDoubleClick: B, panOnScroll: P, panOnScrollSpeed: K, panOnScrollMode: F, panOnDrag: q, autoPanOnSelection: J, defaultViewport: $, translateExtent: _, minZoom: V, maxZoom: E, onSelectionContextMenu: p, preventScrolling: D, noDragClassName: Le, noWheelClassName: Me, noPanClassName: Ye, disableKeyboardA11y: ke, onViewportChange: ct, isControlledViewport: !!Pe, children: i.jsxs(Zx, { children: [i.jsx(qx, { edgeTypes: t, onEdgeClick: o, onEdgeDoubleClick: a, onReconnect: we, onReconnectStart: $e, onReconnectEnd: _e, onlyRenderVisibleElements: M, onEdgeContextMenu: re, onEdgeMouseEnter: fe, onEdgeMouseMove: W, onEdgeMouseLeave: ee, reconnectRadius: ye, defaultMarkerColor: k, noPanClassName: Ye, disableKeyboardA11y: ke, rfId: me }), i.jsx(ow, { style: N, type: g, component: w, containerStyle: m }), i.jsx("div", { className: "react-flow__edgelabel-renderer" }), i.jsx(_x, { nodeTypes: e, onNodeClick: r, onNodeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: f, nodeClickDistance: te, onlyRenderVisibleElements: M, noPanClassName: Ye, noDragClassName: Le, disableKeyboardA11y: ke, nodeExtent: Ce, rfId: me }), i.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Lu.displayName = "GraphView";
const aw = je(Lu), cw = Rl(), tc = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: r, width: o, height: s, fitView: a, fitViewOptions: c, minZoom: u = 0.5, maxZoom: l = 2, nodeOrigin: f, nodeExtent: p, zIndexMode: d = "basic" } = {}) => {
  const h = /* @__PURE__ */ new Map(), g = /* @__PURE__ */ new Map(), N = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), m = r ?? t ?? [], C = n ?? e ?? [], y = f ?? [0, 0], b = p ?? ir;
  Yl(N, w, m);
  const { nodesInitialized: j } = Ti(C, h, g, {
    nodeOrigin: y,
    nodeExtent: b,
    zIndexMode: d
  });
  let v = [0, 0, 1];
  if (a && o && s) {
    const S = gr(h, {
      filter: ($) => !!(($.width || $.initialWidth) && ($.height || $.initialHeight))
    }), { x: A, y: M, zoom: R } = as(S, o, s, u, l, c?.padding ?? 0.1);
    v = [A, M, R];
  }
  return {
    rfId: "1",
    width: o ?? 0,
    height: s ?? 0,
    transform: v,
    nodes: C,
    nodesInitialized: j,
    nodeLookup: h,
    parentLookup: g,
    edges: m,
    edgeLookup: w,
    connectionLookup: N,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: r !== void 0,
    panZoom: null,
    minZoom: u,
    maxZoom: l,
    translateExtent: ir,
    nodeExtent: b,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: gn.Strict,
    domNode: null,
    paneDragging: !1,
    noPanClassName: "nopan",
    nodeOrigin: y,
    nodeDragThreshold: 1,
    connectionDragThreshold: 1,
    snapGrid: [15, 15],
    snapToGrid: !1,
    nodesDraggable: !0,
    nodesConnectable: !0,
    nodesFocusable: !0,
    edgesFocusable: !0,
    edgesReconnectable: !0,
    elementsSelectable: !0,
    elevateNodesOnSelect: !0,
    elevateEdgesOnSelect: !0,
    selectNodesOnDrag: !0,
    multiSelectionActive: !1,
    fitViewQueued: a ?? !1,
    fitViewOptions: c,
    fitViewResolver: null,
    connection: { ..._l },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: cw,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Al,
    zIndexMode: d,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, lw = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: r, width: o, height: s, fitView: a, fitViewOptions: c, minZoom: u, maxZoom: l, nodeOrigin: f, nodeExtent: p, zIndexMode: d }) => Nm((h, g) => {
  async function N() {
    const { nodeLookup: w, panZoom: m, fitViewOptions: C, fitViewResolver: y, width: b, height: j, minZoom: v, maxZoom: S } = g();
    m && (await xy({
      nodes: w,
      width: b,
      height: j,
      panZoom: m,
      minZoom: v,
      maxZoom: S
    }, C), y?.resolve(!0), h({ fitViewResolver: null }));
  }
  return {
    ...tc({
      nodes: e,
      edges: t,
      width: o,
      height: s,
      fitView: a,
      fitViewOptions: c,
      minZoom: u,
      maxZoom: l,
      nodeOrigin: f,
      nodeExtent: p,
      defaultNodes: n,
      defaultEdges: r,
      zIndexMode: d
    }),
    setNodes: (w) => {
      const { nodeLookup: m, parentLookup: C, nodeOrigin: y, elevateNodesOnSelect: b, fitViewQueued: j, zIndexMode: v, nodesSelectionActive: S } = g(), { nodesInitialized: A, hasSelectedNodes: M } = Ti(w, m, C, {
        nodeOrigin: y,
        nodeExtent: p,
        elevateNodesOnSelect: b,
        checkEquality: !0,
        zIndexMode: v
      }), R = S && M;
      j && A ? (N(), h({
        nodes: w,
        nodesInitialized: A,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: R
      })) : h({ nodes: w, nodesInitialized: A, nodesSelectionActive: R });
    },
    setEdges: (w) => {
      const { connectionLookup: m, edgeLookup: C } = g();
      Yl(m, C, w), h({ edges: w });
    },
    setDefaultNodesAndEdges: (w, m) => {
      if (w) {
        const { setNodes: C } = g();
        C(w), h({ hasDefaultNodes: !0 });
      }
      if (m) {
        const { setEdges: C } = g();
        C(m), h({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (w) => {
      const { triggerNodeChanges: m, nodeLookup: C, parentLookup: y, domNode: b, nodeOrigin: j, nodeExtent: v, debug: S, fitViewQueued: A, zIndexMode: M } = g(), { changes: R, updatedInternals: $ } = Hy(w, C, y, b, j, v, M);
      $ && (Ly(C, y, { nodeOrigin: j, nodeExtent: v, zIndexMode: M }), A ? (N(), h({ fitViewQueued: !1, fitViewOptions: void 0 })) : h({}), R?.length > 0 && (S && console.log("React Flow: trigger node changes", R), m?.(R)));
    },
    updateNodePositions: (w, m = !1) => {
      const C = [];
      let y = [];
      const { nodeLookup: b, triggerNodeChanges: j, connection: v, updateConnection: S, onNodesChangeMiddlewareMap: A } = g();
      for (const [M, R] of w) {
        const $ = b.get(M), _ = !!($?.expandParent && $?.parentId && R?.position), V = {
          id: M,
          type: "position",
          position: _ ? {
            x: Math.max(0, R.position.x),
            y: Math.max(0, R.position.y)
          } : R.position,
          dragging: m
        };
        if ($ && v.inProgress && v.fromNode.id === $.id) {
          const E = Ht($, v.fromHandle, ne.Left, !0);
          S({ ...v, from: E });
        }
        _ && $.parentId && C.push({
          id: M,
          parentId: $.parentId,
          rect: {
            ...R.internals.positionAbsolute,
            width: R.measured.width ?? 0,
            height: R.measured.height ?? 0
          }
        }), y.push(V);
      }
      if (C.length > 0) {
        const { parentLookup: M, nodeOrigin: R } = g(), $ = ps(C, b, M, R);
        y.push(...$);
      }
      for (const M of A.values())
        y = M(y);
      j(y);
    },
    triggerNodeChanges: (w) => {
      const { onNodesChange: m, setNodes: C, nodes: y, hasDefaultNodes: b, debug: j } = g();
      if (w?.length) {
        if (b) {
          const v = uu(w, y);
          C(v);
        }
        j && console.log("React Flow: trigger node changes", w), m?.(w);
      }
    },
    triggerEdgeChanges: (w) => {
      const { onEdgesChange: m, setEdges: C, edges: y, hasDefaultEdges: b, debug: j } = g();
      if (w?.length) {
        if (b) {
          const v = du(w, y);
          C(v);
        }
        j && console.log("React Flow: trigger edge changes", w), m?.(w);
      }
    },
    addSelectedNodes: (w) => {
      const { multiSelectionActive: m, edgeLookup: C, nodeLookup: y, triggerNodeChanges: b, triggerEdgeChanges: j } = g();
      if (m) {
        const v = w.map((S) => Tt(S, !0));
        b(v);
        return;
      }
      b(an(y, /* @__PURE__ */ new Set([...w]), !0)), j(an(C));
    },
    addSelectedEdges: (w) => {
      const { multiSelectionActive: m, edgeLookup: C, nodeLookup: y, triggerNodeChanges: b, triggerEdgeChanges: j } = g();
      if (m) {
        const v = w.map((S) => Tt(S, !0));
        j(v);
        return;
      }
      j(an(C, /* @__PURE__ */ new Set([...w]))), b(an(y, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: w, edges: m } = {}) => {
      const { edges: C, nodes: y, nodeLookup: b, triggerNodeChanges: j, triggerEdgeChanges: v } = g(), S = w || y, A = m || C, M = [];
      for (const $ of S) {
        if (!$.selected)
          continue;
        const _ = b.get($.id);
        _ && (_.selected = !1), M.push(Tt($.id, !1));
      }
      const R = [];
      for (const $ of A)
        $.selected && R.push(Tt($.id, !1));
      j(M), v(R);
    },
    setMinZoom: (w) => {
      const { panZoom: m, maxZoom: C } = g();
      m?.setScaleExtent([w, C]), h({ minZoom: w });
    },
    setMaxZoom: (w) => {
      const { panZoom: m, minZoom: C } = g();
      m?.setScaleExtent([C, w]), h({ maxZoom: w });
    },
    setTranslateExtent: (w) => {
      g().panZoom?.setTranslateExtent(w), h({ translateExtent: w });
    },
    resetSelectedElements: () => {
      const { edges: w, nodes: m, triggerNodeChanges: C, triggerEdgeChanges: y, elementsSelectable: b } = g();
      if (!b)
        return;
      const j = m.reduce((S, A) => A.selected ? [...S, Tt(A.id, !1)] : S, []), v = w.reduce((S, A) => A.selected ? [...S, Tt(A.id, !1)] : S, []);
      C(j), y(v);
    },
    setNodeExtent: (w) => {
      const { nodes: m, nodeLookup: C, parentLookup: y, nodeOrigin: b, elevateNodesOnSelect: j, nodeExtent: v, zIndexMode: S } = g();
      w[0][0] === v[0][0] && w[0][1] === v[0][1] && w[1][0] === v[1][0] && w[1][1] === v[1][1] || (Ti(m, C, y, {
        nodeOrigin: b,
        nodeExtent: w,
        elevateNodesOnSelect: j,
        checkEquality: !1,
        zIndexMode: S
      }), h({ nodeExtent: w }));
    },
    panBy: (w) => {
      const { transform: m, width: C, height: y, panZoom: b, translateExtent: j } = g();
      return Wy({ delta: w, panZoom: b, transform: m, translateExtent: j, width: C, height: y });
    },
    setCenter: async (w, m, C) => {
      const { width: y, height: b, maxZoom: j, panZoom: v } = g();
      if (!v)
        return !1;
      const S = typeof C?.zoom < "u" ? C.zoom : j;
      return await v.setViewport({
        x: y / 2 - w * S,
        y: b / 2 - m * S,
        zoom: S
      }, { duration: C?.duration, ease: C?.ease, interpolate: C?.interpolate }), !0;
    },
    cancelConnection: () => {
      h({
        connection: { ..._l }
      });
    },
    updateConnection: (w) => {
      h({ connection: w });
    },
    reset: () => h({ ...tc() })
  };
}, Object.is);
function uw({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: r, initialWidth: o, initialHeight: s, initialMinZoom: a, initialMaxZoom: c, initialFitViewOptions: u, fitView: l, nodeOrigin: f, nodeExtent: p, zIndexMode: d, children: h }) {
  const [g] = U(() => lw({
    nodes: e,
    edges: t,
    defaultNodes: n,
    defaultEdges: r,
    width: o,
    height: s,
    fitView: l,
    minZoom: a,
    maxZoom: c,
    fitViewOptions: u,
    nodeOrigin: f,
    nodeExtent: p,
    zIndexMode: d
  }));
  return i.jsx(Cm, { value: g, children: i.jsx(Zm, { children: h }) });
}
function dw({ children: e, nodes: t, edges: n, defaultNodes: r, defaultEdges: o, width: s, height: a, fitView: c, fitViewOptions: u, minZoom: l, maxZoom: f, nodeOrigin: p, nodeExtent: d, zIndexMode: h }) {
  return dr(Po) ? i.jsx(i.Fragment, { children: e }) : i.jsx(uw, { initialNodes: t, initialEdges: n, defaultNodes: r, defaultEdges: o, initialWidth: s, initialHeight: a, fitView: c, initialFitViewOptions: u, initialMinZoom: l, initialMaxZoom: f, nodeOrigin: p, nodeExtent: d, zIndexMode: h, children: e });
}
const fw = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function pw({ nodes: e, edges: t, defaultNodes: n, defaultEdges: r, className: o, nodeTypes: s, edgeTypes: a, onNodeClick: c, onEdgeClick: u, onInit: l, onMove: f, onMoveStart: p, onMoveEnd: d, onConnect: h, onConnectStart: g, onConnectEnd: N, onClickConnectStart: w, onClickConnectEnd: m, onNodeMouseEnter: C, onNodeMouseMove: y, onNodeMouseLeave: b, onNodeContextMenu: j, onNodeDoubleClick: v, onNodeDragStart: S, onNodeDrag: A, onNodeDragStop: M, onNodesDelete: R, onEdgesDelete: $, onDelete: _, onSelectionChange: V, onSelectionDragStart: E, onSelectionDrag: D, onSelectionDragStop: k, onSelectionContextMenu: T, onSelectionStart: L, onSelectionEnd: P, onBeforeDelete: K, connectionMode: F, connectionLineType: B = bt.Bezier, connectionLineStyle: q, connectionLineComponent: J, connectionLineContainerStyle: X, deleteKeyCode: ce = "Backspace", selectionKeyCode: G = "Shift", selectionOnDrag: O = !1, selectionMode: Y = sr.Full, panActivationKeyCode: se = "Space", multiSelectionKeyCode: ae = cr() ? "Meta" : "Control", zoomActivationKeyCode: te = cr() ? "Meta" : "Control", snapToGrid: re, snapGrid: fe, onlyRenderVisibleElements: W = !1, selectNodesOnDrag: ee, nodesDraggable: ye, autoPanOnNodeFocus: we, nodesConnectable: $e, nodesFocusable: _e, nodeOrigin: Le = cu, edgesFocusable: Me, edgesReconnectable: Ye, elementsSelectable: ke = !0, defaultViewport: Ce = Vm, minZoom: me = 0.5, maxZoom: Pe = 2, translateExtent: ct = ir, preventScrolling: Fo = !0, nodeExtent: Sn, defaultMarkerColor: En = "#b1b1b7", zoomOnScroll: Et = !0, zoomOnPinch: Nr = !0, panOnScroll: Ze = !1, panOnScrollSpeed: Kt = 0.5, panOnScrollMode: Cn = Pt.Free, zoomOnDoubleClick: In = !0, panOnDrag: jr = !0, onPaneClick: kn, onPaneMouseEnter: We, onPaneMouseMove: lt, onPaneMouseLeave: ut, onPaneScroll: Ut, onPaneContextMenu: Bo, paneClickDistance: An = 1, nodeClickDistance: Xt = 0, children: yt, onReconnect: Ct, onReconnectStart: It, onReconnectEnd: ze, onEdgeContextMenu: _n, onEdgeDoubleClick: Sr, onEdgeMouseEnter: Fe, onEdgeMouseMove: Er, onEdgeMouseLeave: ve, reconnectRadius: Be = 10, onNodesChange: qt, onEdgesChange: Cr, noDragClassName: he = "nodrag", noWheelClassName: kt = "nowheel", noPanClassName: Dn = "nopan", fitView: Yt, fitViewOptions: Zt, connectOnClick: Ae, attributionPosition: At, proOptions: mt, defaultEdgeOptions: Ko, elevateNodesOnSelect: Ir = !0, elevateEdgesOnSelect: kr = !1, disableKeyboardA11y: Gt = !1, autoPanOnConnect: Uo, autoPanOnNodeDrag: Tn, autoPanOnSelection: Jt = !0, autoPanSpeed: Re, connectionRadius: $n, isValidConnection: Mn, onError: Pn, style: Qt, id: Ar, nodeDragThreshold: Xo, connectionDragThreshold: qo, viewport: Yo, onViewportChange: _r, width: Dr, height: Tr, colorMode: Zo = "light", debug: en, onScroll: $r, ariaLabelConfig: Go, zIndexMode: _t = "basic", ...Jo }, Qo) {
  const xt = Ar || "1", tn = Fm(Zo), Rn = le((nn) => {
    nn.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), $r?.(nn);
  }, [$r]);
  return i.jsx("div", { "data-testid": "rf__wrapper", ...Jo, onScroll: Rn, style: { ...Qt, ...fw }, ref: Qo, className: Ee(["react-flow", o, tn]), id: Ar, role: "application", children: i.jsxs(dw, { nodes: e, edges: t, width: Dr, height: Tr, fitView: Yt, fitViewOptions: Zt, minZoom: me, maxZoom: Pe, nodeOrigin: Le, nodeExtent: Sn, zIndexMode: _t, children: [i.jsx(Wm, { nodes: e, edges: t, defaultNodes: n, defaultEdges: r, onConnect: h, onConnectStart: g, onConnectEnd: N, onClickConnectStart: w, onClickConnectEnd: m, nodesDraggable: ye, autoPanOnNodeFocus: we, nodesConnectable: $e, nodesFocusable: _e, edgesFocusable: Me, edgesReconnectable: Ye, elementsSelectable: ke, elevateNodesOnSelect: Ir, elevateEdgesOnSelect: kr, minZoom: me, maxZoom: Pe, nodeExtent: Sn, onNodesChange: qt, onEdgesChange: Cr, snapToGrid: re, snapGrid: fe, connectionMode: F, translateExtent: ct, connectOnClick: Ae, defaultEdgeOptions: Ko, fitView: Yt, fitViewOptions: Zt, onNodesDelete: R, onEdgesDelete: $, onDelete: _, onNodeDragStart: S, onNodeDrag: A, onNodeDragStop: M, onSelectionDrag: D, onSelectionDragStart: E, onSelectionDragStop: k, onMove: f, onMoveStart: p, onMoveEnd: d, noPanClassName: Dn, nodeOrigin: Le, rfId: xt, autoPanOnConnect: Uo, autoPanOnNodeDrag: Tn, autoPanSpeed: Re, onError: Pn, connectionRadius: $n, isValidConnection: Mn, selectNodesOnDrag: ee, nodeDragThreshold: Xo, connectionDragThreshold: qo, onBeforeDelete: K, debug: en, ariaLabelConfig: Go, zIndexMode: _t }), i.jsx(aw, { onInit: l, onNodeClick: c, onEdgeClick: u, onNodeMouseEnter: C, onNodeMouseMove: y, onNodeMouseLeave: b, onNodeContextMenu: j, onNodeDoubleClick: v, nodeTypes: s, edgeTypes: a, connectionLineType: B, connectionLineStyle: q, connectionLineComponent: J, connectionLineContainerStyle: X, selectionKeyCode: G, selectionOnDrag: O, selectionMode: Y, deleteKeyCode: ce, multiSelectionKeyCode: ae, panActivationKeyCode: se, zoomActivationKeyCode: te, onlyRenderVisibleElements: W, defaultViewport: Ce, translateExtent: ct, minZoom: me, maxZoom: Pe, preventScrolling: Fo, zoomOnScroll: Et, zoomOnPinch: Nr, zoomOnDoubleClick: In, panOnScroll: Ze, panOnScrollSpeed: Kt, panOnScrollMode: Cn, panOnDrag: jr, autoPanOnSelection: Jt, onPaneClick: kn, onPaneMouseEnter: We, onPaneMouseMove: lt, onPaneMouseLeave: ut, onPaneScroll: Ut, onPaneContextMenu: Bo, paneClickDistance: An, nodeClickDistance: Xt, onSelectionContextMenu: T, onSelectionStart: L, onSelectionEnd: P, onReconnect: Ct, onReconnectStart: It, onReconnectEnd: ze, onEdgeContextMenu: _n, onEdgeDoubleClick: Sr, onEdgeMouseEnter: Fe, onEdgeMouseMove: Er, onEdgeMouseLeave: ve, reconnectRadius: Be, defaultMarkerColor: En, noDragClassName: he, noWheelClassName: kt, noPanClassName: Dn, rfId: xt, disableKeyboardA11y: Gt, nodeExtent: Sn, viewport: Yo, onViewportChange: _r }), i.jsx(zm, { onSelectionChange: V }), yt, i.jsx($m, { proOptions: mt, position: At }), i.jsx(Tm, { rfId: xt, disableKeyboardA11y: Gt })] }) });
}
var zu = hu(pw);
const hw = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function gw({ children: e }) {
  const t = pe(hw);
  return t ? Em.createPortal(e, t) : null;
}
function yw({ dimensions: e, lineWidth: t, variant: n, className: r }) {
  return i.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ee(["react-flow__background-pattern", n, r]) });
}
function mw({ radius: e, className: t }) {
  return i.jsx("circle", { cx: e, cy: e, r: e, className: Ee(["react-flow__background-pattern", "dots", t]) });
}
var Nt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(Nt || (Nt = {}));
const xw = {
  [Nt.Dots]: 1,
  [Nt.Lines]: 1,
  [Nt.Cross]: 6
}, ww = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function Vu({
  id: e,
  variant: t = Nt.Dots,
  // only used for dots and cross
  gap: n = 20,
  // only used for lines and cross
  size: r,
  lineWidth: o = 1,
  offset: s = 0,
  color: a,
  bgColor: c,
  style: u,
  className: l,
  patternClassName: f
}) {
  const p = ie(null), { transform: d, patternId: h } = pe(ww, be), g = r || xw[t], N = t === Nt.Dots, w = t === Nt.Cross, m = Array.isArray(n) ? n : [n, n], C = [m[0] * d[2] || 1, m[1] * d[2] || 1], y = g * d[2], b = Array.isArray(s) ? s : [s, s], j = w ? [y, y] : C, v = [
    b[0] * d[2] || 1 + j[0] / 2,
    b[1] * d[2] || 1 + j[1] / 2
  ], S = `${h}${e || ""}`;
  return i.jsxs("svg", { className: Ee(["react-flow__background", l]), style: {
    ...u,
    ...Lo,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": a
  }, ref: p, "data-testid": "rf__background", children: [i.jsx("pattern", { id: S, x: d[0] % C[0], y: d[1] % C[1], width: C[0], height: C[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${v[0]},-${v[1]})`, children: N ? i.jsx(mw, { radius: y / 2, className: f }) : i.jsx(yw, { dimensions: j, lineWidth: o, variant: t, className: f }) }), i.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${S})` })] });
}
Vu.displayName = "Background";
const Ou = je(Vu);
function vw() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: i.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function bw() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: i.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function Nw() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: i.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function jw() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: i.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function Sw() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: i.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function Fr({ children: e, className: t, ...n }) {
  return i.jsx("button", { type: "button", className: Ee(["react-flow__controls-button", t]), ...n, children: e });
}
const Ew = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function Hu({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: r = !0, fitViewOptions: o, onZoomIn: s, onZoomOut: a, onFitView: c, onInteractiveChange: u, className: l, children: f, position: p = "bottom-left", orientation: d = "vertical", "aria-label": h }) {
  const g = Ne(), { isInteractive: N, minZoomReached: w, maxZoomReached: m, ariaLabelConfig: C } = pe(Ew, be), { zoomIn: y, zoomOut: b, fitView: j } = hs(), v = () => {
    y(), s?.();
  }, S = () => {
    b(), a?.();
  }, A = () => {
    j(o), c?.();
  }, M = () => {
    g.setState({
      nodesDraggable: !N,
      nodesConnectable: !N,
      elementsSelectable: !N
    }), u?.(!N);
  }, R = d === "horizontal" ? "horizontal" : "vertical";
  return i.jsxs(Ro, { className: Ee(["react-flow__controls", R, l]), position: p, style: e, "data-testid": "rf__controls", "aria-label": h ?? C["controls.ariaLabel"], children: [t && i.jsxs(i.Fragment, { children: [i.jsx(Fr, { onClick: v, className: "react-flow__controls-zoomin", title: C["controls.zoomIn.ariaLabel"], "aria-label": C["controls.zoomIn.ariaLabel"], disabled: m, children: i.jsx(vw, {}) }), i.jsx(Fr, { onClick: S, className: "react-flow__controls-zoomout", title: C["controls.zoomOut.ariaLabel"], "aria-label": C["controls.zoomOut.ariaLabel"], disabled: w, children: i.jsx(bw, {}) })] }), n && i.jsx(Fr, { className: "react-flow__controls-fitview", onClick: A, title: C["controls.fitView.ariaLabel"], "aria-label": C["controls.fitView.ariaLabel"], children: i.jsx(Nw, {}) }), r && i.jsx(Fr, { className: "react-flow__controls-interactive", onClick: M, title: C["controls.interactive.ariaLabel"], "aria-label": C["controls.interactive.ariaLabel"], children: N ? i.jsx(Sw, {}) : i.jsx(jw, {}) }), f] });
}
Hu.displayName = "Controls";
const Wu = je(Hu);
function Cw({ id: e, x: t, y: n, width: r, height: o, style: s, color: a, strokeColor: c, strokeWidth: u, className: l, borderRadius: f, shapeRendering: p, selected: d, onClick: h }) {
  const { background: g, backgroundColor: N } = s || {}, w = a || g || N;
  return i.jsx("rect", { className: Ee(["react-flow__minimap-node", { selected: d }, l]), x: t, y: n, rx: f, ry: f, width: r, height: o, style: {
    fill: w,
    stroke: c,
    strokeWidth: u
  }, shapeRendering: p, onClick: h ? (m) => h(m, e) : void 0 });
}
const Iw = je(Cw), kw = (e) => e.nodes.map((t) => t.id), mi = (e) => e instanceof Function ? e : () => e;
function Aw({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: r = 5,
  nodeStrokeWidth: o,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: s = Iw,
  onClick: a
}) {
  const c = pe(kw, be), u = mi(t), l = mi(e), f = mi(n), p = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return i.jsx(i.Fragment, { children: c.map((d) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    i.jsx(Dw, { id: d, nodeColorFunc: u, nodeStrokeColorFunc: l, nodeClassNameFunc: f, nodeBorderRadius: r, nodeStrokeWidth: o, NodeComponent: s, onClick: a, shapeRendering: p }, d)
  )) });
}
function _w({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: r, nodeBorderRadius: o, nodeStrokeWidth: s, shapeRendering: a, NodeComponent: c, onClick: u }) {
  const { node: l, x: f, y: p, width: d, height: h } = pe((g) => {
    const N = g.nodeLookup.get(e);
    if (!N)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const w = N.internals.userNode, { x: m, y: C } = N.internals.positionAbsolute, { width: y, height: b } = gt(w);
    return {
      node: w,
      x: m,
      y: C,
      width: y,
      height: b
    };
  }, be);
  return !l || l.hidden || !Ll(l) ? null : i.jsx(c, { x: f, y: p, width: d, height: h, style: l.style, selected: !!l.selected, className: r(l), color: t(l), borderRadius: o, strokeColor: n(l), strokeWidth: s, shapeRendering: a, onClick: u, id: l.id });
}
const Dw = je(_w);
var Tw = je(Aw);
const $w = 200, Mw = 150, Pw = (e) => !e.hidden, Rw = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? Pl(gr(e.nodeLookup, { filter: Pw }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, Lw = "react-flow__minimap-desc";
function Fu({
  style: e,
  className: t,
  nodeStrokeColor: n,
  nodeColor: r,
  nodeClassName: o = "",
  nodeBorderRadius: s = 5,
  nodeStrokeWidth: a,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: c,
  bgColor: u,
  maskColor: l,
  maskStrokeColor: f,
  maskStrokeWidth: p,
  position: d = "bottom-right",
  onClick: h,
  onNodeClick: g,
  pannable: N = !1,
  zoomable: w = !1,
  ariaLabel: m,
  inversePan: C,
  zoomStep: y = 1,
  offsetScale: b = 5
}) {
  const j = Ne(), v = ie(null), { boundingRect: S, viewBB: A, rfId: M, panZoom: R, translateExtent: $, flowWidth: _, flowHeight: V, ariaLabelConfig: E } = pe(Rw, be), D = e?.width ?? $w, k = e?.height ?? Mw, T = S.width / D, L = S.height / k, P = Math.max(T, L), K = P * D, F = P * k, B = b * P, q = S.x - (K - S.width) / 2 - B, J = S.y - (F - S.height) / 2 - B, X = K + B * 2, ce = F + B * 2, G = `${Lw}-${M}`, O = ie(0), Y = ie();
  O.current = P, Q(() => {
    if (v.current && R)
      return Y.current = Gy({
        domNode: v.current,
        panZoom: R,
        getTransform: () => j.getState().transform,
        getViewScale: () => O.current
      }), () => {
        Y.current?.destroy();
      };
  }, [R]), Q(() => {
    Y.current?.update({
      translateExtent: $,
      width: _,
      height: V,
      inversePan: C,
      pannable: N,
      zoomStep: y,
      zoomable: w
    });
  }, [N, w, C, y, $, _, V]);
  const se = h ? (re) => {
    const [fe, W] = Y.current?.pointer(re) || [0, 0];
    h(re, { x: fe, y: W });
  } : void 0, ae = g ? le((re, fe) => {
    const W = j.getState().nodeLookup.get(fe).internals.userNode;
    g(re, W);
  }, []) : void 0, te = m ?? E["minimap.ariaLabel"];
  return i.jsx(Ro, { position: d, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof f == "string" ? f : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof p == "number" ? p * P : void 0,
    "--xy-minimap-node-background-color-props": typeof r == "string" ? r : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof a == "number" ? a : void 0
  }, className: Ee(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: i.jsxs("svg", { width: D, height: k, viewBox: `${q} ${J} ${X} ${ce}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": G, ref: v, onClick: se, children: [te && i.jsx("title", { id: G, children: te }), i.jsx(Tw, { onClick: ae, nodeColor: r, nodeStrokeColor: n, nodeBorderRadius: s, nodeClassName: o, nodeStrokeWidth: a, nodeComponent: c }), i.jsx("path", { className: "react-flow__minimap-mask", d: `M${q - B},${J - B}h${X + B * 2}v${ce + B * 2}h${-X - B * 2}z
        M${A.x},${A.y}h${A.width}v${A.height}h${-A.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
Fu.displayName = "MiniMap";
const Bu = je(Fu), zw = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, Vw = {
  [wn.Line]: "right",
  [wn.Handle]: "bottom-right"
};
function Ow({ nodeId: e, position: t, variant: n = wn.Handle, className: r, style: o = void 0, children: s, color: a, minWidth: c = 10, minHeight: u = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: f = Number.MAX_VALUE, keepAspectRatio: p = !1, resizeDirection: d, autoScale: h = !0, shouldResize: g, onResizeStart: N, onResize: w, onResizeEnd: m }) {
  const C = xu(), y = typeof e == "string" ? e : C, b = Ne(), j = ie(null), v = n === wn.Handle, S = pe(le(zw(v && h), [v, h]), be), A = ie(null), M = t ?? Vw[n];
  Q(() => {
    if (!(!j.current || !y))
      return A.current || (A.current = um({
        domNode: j.current,
        nodeId: y,
        getStoreItems: () => {
          const { nodeLookup: $, transform: _, snapGrid: V, snapToGrid: E, nodeOrigin: D, domNode: k } = b.getState();
          return {
            nodeLookup: $,
            transform: _,
            snapGrid: V,
            snapToGrid: E,
            nodeOrigin: D,
            paneDomNode: k
          };
        },
        onChange: ($, _) => {
          const { triggerNodeChanges: V, nodeLookup: E, parentLookup: D, nodeOrigin: k } = b.getState(), T = [], L = { x: $.x, y: $.y }, P = E.get(y);
          if (P && P.expandParent && P.parentId) {
            const K = P.origin ?? k, F = $.width ?? P.measured.width ?? 0, B = $.height ?? P.measured.height ?? 0, q = {
              id: P.id,
              parentId: P.parentId,
              rect: {
                width: F,
                height: B,
                ...zl({
                  x: $.x ?? P.position.x,
                  y: $.y ?? P.position.y
                }, { width: F, height: B }, P.parentId, E, K)
              }
            }, J = ps([q], E, D, k);
            T.push(...J), L.x = $.x ? Math.max(K[0] * F, $.x) : void 0, L.y = $.y ? Math.max(K[1] * B, $.y) : void 0;
          }
          if (L.x !== void 0 && L.y !== void 0) {
            const K = {
              id: y,
              type: "position",
              position: { ...L }
            };
            T.push(K);
          }
          if ($.width !== void 0 && $.height !== void 0) {
            const F = {
              id: y,
              type: "dimensions",
              resizing: !0,
              setAttributes: d ? d === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: $.width,
                height: $.height
              }
            };
            T.push(F);
          }
          for (const K of _) {
            const F = {
              ...K,
              type: "position"
            };
            T.push(F);
          }
          V(T);
        },
        onEnd: ({ width: $, height: _ }) => {
          const V = {
            id: y,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: $,
              height: _
            }
          };
          b.getState().triggerNodeChanges([V]);
        }
      })), A.current.update({
        controlPosition: M,
        boundaries: {
          minWidth: c,
          minHeight: u,
          maxWidth: l,
          maxHeight: f
        },
        keepAspectRatio: p,
        resizeDirection: d,
        onResizeStart: N,
        onResize: w,
        onResizeEnd: m,
        shouldResize: g
      }), () => {
        A.current?.destroy();
      };
  }, [
    M,
    c,
    u,
    l,
    f,
    p,
    N,
    w,
    m,
    g
  ]);
  const R = M.split("-");
  return i.jsx("div", { className: Ee(["react-flow__resize-control", "nodrag", ...R, n, r]), ref: j, style: {
    ...o,
    scale: S,
    ...a && { [v ? "backgroundColor" : "borderColor"]: a }
  }, children: s });
}
je(Ow);
const Hw = "String", Ww = "singleline";
function Fw(e) {
  return e === "Single" || e === "Array" || e === "List" || e === "HashSet";
}
function ys(e, t = "Single") {
  return { alias: (e ?? "").trim() || Hw, collectionKind: t };
}
function Ku(e) {
  const t = e.type ?? e.Type;
  if (xo(t))
    return { alias: typeof t.alias == "string" ? t.alias : typeof t.typeName == "string" ? t.typeName : "", collectionKind: Fw(t.collectionKind) ? t.collectionKind : "Single" };
  const n = e.typeInformation ?? e.TypeInformation;
  return xo(n) ? { alias: typeof n.typeName == "string" ? n.typeName : "", collectionKind: nc(e) ? "Array" : "Single" } : { alias: typeof t == "string" ? t : "", collectionKind: nc(e) ? "Array" : "Single" };
}
function nc(e) {
  const t = e.isArray ?? e.IsArray;
  return t === !0 || t === "true";
}
function rc(e) {
  return (e.alias ?? e.typeName ?? "").trim();
}
function zo() {
  return typeof crypto < "u" && typeof crypto.randomUUID == "function" ? crypto.randomUUID() : `id-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
}
function Bw(e, t) {
  const n = new Set(t);
  let r = 1, o = `${e}${r}`;
  for (; n.has(o); )
    r += 1, o = `${e}${r}`;
  return o;
}
function Kw(e) {
  return {
    referenceKey: zo(),
    name: e.name,
    type: ys(e.alias),
    storageDriverType: e.storageDriver?.trim() || null,
    default: null
  };
}
function Uw(e, t) {
  return { ...e, ...t };
}
function Xw(e) {
  return e === "" ? null : { value: e, expressionType: "Literal" };
}
function qw(e) {
  return e == null || e.value == null ? "" : typeof e.value == "object" ? JSON.stringify(e.value) : String(e.value);
}
function Yw(e) {
  return {
    referenceKey: zo(),
    name: e.name,
    type: ys(e.alias),
    displayName: e.name,
    description: "",
    category: "",
    uiHint: Ww,
    storageDriverType: e.storageDriver ?? null,
    isRequired: !1
  };
}
function Zw(e, t) {
  return { ...e, ...t };
}
function Gw(e) {
  return {
    referenceKey: zo(),
    name: e.name,
    type: ys(e.alias),
    displayName: e.name,
    description: "",
    category: ""
  };
}
function Jw(e, t) {
  return { ...e, ...t };
}
function Qw(e) {
  const t = e.split(",")[0].trim();
  return (t.split(".").pop() ?? t).split("`")[0];
}
function Uu(e, t) {
  return e && !e.includes(",") && !e.includes(".") ? e : Qw(e || t);
}
function ev(e, t) {
  return Uu(e, t).replace(/StorageDriver$/, "");
}
function xo(e) {
  return !!e && typeof e == "object" && !Array.isArray(e);
}
function Gn(e, t) {
  for (const n of t) {
    const r = e[n];
    if (r != null && typeof r != "object") return String(r);
  }
  return "";
}
const tv = ["name", "Name"], Xu = ["name", "Name"], nv = ["storageDriverType", "StorageDriverType"], qu = ["referenceKey", "ReferenceKey"], rv = /* @__PURE__ */ new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);
function xr(e) {
  return Yu(e, av);
}
function Vo(e) {
  return Yu(e, cv);
}
function Yu(e, t) {
  if (!e) return e;
  const n = { ...e };
  return e.rootActivity && (n.rootActivity = Zu(e.rootActivity, t)), Array.isArray(e.variables) && (n.variables = Gr(e.variables, Pi)), Array.isArray(e.inputs) && (n.inputs = Gr(e.inputs, Pi)), Array.isArray(e.outputs) && (n.outputs = Gr(e.outputs, (r) => Gu(r, !1))), n;
}
function Zu(e, t) {
  const n = t(e), r = n.structure;
  if (!r || !st(r.payload)) return n;
  let o = !1;
  const s = { ...r.payload };
  for (const [a, c] of Object.entries(r.payload))
    Array.isArray(c) && c.length > 0 && c.every(pv) && (s[a] = c.map((u) => Zu(u, t)), o = !0);
  return Array.isArray(r.payload.variables) && r.payload.variables.length > 0 && (s.variables = Gr(r.payload.variables, Pi), o = !0), o ? { ...n, structure: { ...r, payload: s } } : n;
}
function Gr(e, t) {
  return e.map((n) => st(n) && !Array.isArray(n) ? t(n) : n);
}
function Pi(e) {
  return Gu(e, !0);
}
const ov = [
  "type",
  "Type",
  "typeInformation",
  "TypeInformation",
  "isArray",
  "IsArray",
  "storageDriverType",
  "StorageDriverType",
  "defaultValue",
  "DefaultValue",
  "defaultSyntax",
  "DefaultSyntax",
  "isReadOnly",
  "IsReadOnly"
];
function Gu(e, t) {
  const n = iv(e, ov);
  return Gn(e, qu).trim() || (n.referenceKey = zo()), n.type = Ku(e), t && (n.storageDriverType = sv(e.storageDriverType ?? e.StorageDriverType)), n;
}
function iv(e, t) {
  const n = new Set(t), r = {};
  for (const [o, s] of Object.entries(e))
    n.has(o) || (r[o] = s);
  return r;
}
function sv(e) {
  if (typeof e == "string") return e.trim() ? e : null;
  if (st(e)) {
    const t = typeof e.typeName == "string" ? e.typeName : "";
    if (!t) return null;
    const n = typeof e.namespace == "string" ? e.namespace : "";
    return n ? `${n}.${t}` : t;
  }
  return null;
}
function av(e) {
  const t = [], n = {};
  for (const [o, s] of Object.entries(e))
    rv.has(o) || (fv(s) ? t.push({
      referenceKey: lv(o),
      value: dv(s.expression)
    }) : n[o] = s);
  const r = Array.isArray(e.inputs) ? e.inputs : [];
  return {
    ...n,
    nodeId: e.nodeId,
    activityVersionId: e.activityVersionId,
    inputs: [...r, ...t],
    outputs: Array.isArray(e.outputs) ? e.outputs : [],
    structure: e.structure
  };
}
function cv(e) {
  const t = Array.isArray(e.inputs) ? e.inputs : [], n = {};
  for (const r of t) {
    if (!st(r) || typeof r.referenceKey != "string") continue;
    const o = st(r.value) ? r.value : {};
    n[uv(r.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof o.expressionType == "string" ? o.expressionType : "Literal",
        value: o.value ?? ""
      }
    };
  }
  return { ...e, ...n, inputs: [] };
}
function lv(e) {
  return e && e.charAt(0).toUpperCase() + e.slice(1);
}
function uv(e) {
  return e && e.charAt(0).toLowerCase() + e.slice(1);
}
function dv(e) {
  const t = e.type || "Literal";
  return t === "Variable" && st(e.value) ? { value: e.value, expressionType: t } : t === "Literal" && st(e.value) ? { value: oc(e.value), expressionType: "Object" } : { value: oc(e.value), expressionType: t };
}
function oc(e) {
  return e == null ? null : typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function fv(e) {
  if (!st(e) || Array.isArray(e)) return !1;
  const t = e.expression;
  return st(t) && typeof t.type == "string";
}
function pv(e) {
  return st(e) && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function st(e) {
  return typeof e == "object" && e !== null;
}
const wr = "elsa.sequence.structure", jn = "elsa.flowchart.structure";
function Ju(e, t) {
  if (!e) return null;
  let n = e;
  for (const r of t) {
    const o = He(n).find((a) => a.id === r.slotId);
    if (!o) return null;
    const s = o.activities.find((a) => a.nodeId === r.ownerNodeId);
    if (!s) return null;
    n = s;
  }
  return n;
}
function hv(e, t, n = (r) => r.nodeId) {
  if (!e) return null;
  if (e.nodeId === t) return [];
  const r = (o, s) => {
    const a = He(o);
    if (a[0]?.activities.some((c) => c.nodeId === t)) return s;
    for (const c of a)
      for (const u of c.activities) {
        const l = r(u, [...s, { ownerNodeId: u.nodeId, slotId: c.id, label: n(u) }]);
        if (l) return l;
      }
    return null;
  };
  return r(e, []);
}
function Jn(e, t) {
  const n = Ju(e, t);
  if (!n) return null;
  const r = He(n)[0];
  return r ? { owner: n, slot: r } : null;
}
function He(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, r = $v(t), o = xi(n.activities);
  return o ? [{
    id: `${t.kind}:activities`,
    label: Mv(t),
    property: "activities",
    mode: r,
    activities: o
  }] : Object.entries(n).filter(([, s]) => xi(s)).map(([s, a]) => ({
    id: `${t.kind}:${s}`,
    label: Rv(s),
    property: s,
    mode: "generic",
    activities: xi(a) ?? []
  }));
}
function Qu(e, t, n) {
  const r = new Map(t.map((a) => [a.activityVersionId, a])), o = new Map(n.map((a) => [a.nodeId, a])), s = e.slot.activities.map((a, c) => {
    const u = r.get(a.activityVersionId), l = o.get(a.nodeId) ?? Pv(e.slot.mode, c);
    return nd(a, u, { x: l.x, y: l.y });
  });
  return {
    nodes: s,
    edges: e.slot.mode === "flowchart" ? rd(e.owner) : Ev(e.slot, s)
  };
}
function Ri(e, t, n) {
  const r = t.find((s) => s.activityVersionId === e.activityVersionId), o = n.find((s) => s.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [nd(e, r, { x: o.x, y: o.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function gv(e, t, n, r = null) {
  const o = new Map(t.map((c) => [c.activityExecutionId, c])), s = sc(t, (c) => c.authoredActivityId || c.executableNodeId), a = sc(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? o.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const u = s.get(c.id) ?? [], l = a.get(c.id) ?? [];
    if (u.length === 0 && l.length === 0) return c;
    const f = _v(u), p = r === c.id || u.some((h) => h.activityExecutionId === r) || l.some((h) => h.incidentId === r), d = {
      status: f?.status,
      subStatus: f?.subStatus,
      activityExecutionId: f?.activityExecutionId,
      faultCount: u.reduce((h, g) => h + g.faultCount + g.aggregateFaultCount, 0),
      incidentCount: l.length,
      hasBlockingIncident: l.some((h) => h.isBlocking),
      selected: p
    };
    return {
      ...c,
      selected: p,
      className: p ? "wf-runtime-node-selected" : c.className,
      data: {
        ...c.data,
        runtime: d
      }
    };
  });
}
function ms(e, t) {
  return e?.structure?.kind === jn || vv(t) ? "flowchart" : e?.structure?.kind === wr || bv(t) ? "sequence" : "unsupported";
}
function Li(e, t, n) {
  if (t.length === 0) {
    const c = He(e)[0];
    return c ? ur(e, c, n) : e;
  }
  const [r, ...o] = t, s = He(e).find((c) => c.id === r.slotId);
  if (!s) return e;
  const a = s.activities.map((c) => c.nodeId === r.ownerNodeId ? Li(c, o, n) : c);
  return ur(e, s, a);
}
function ed(e, t, n) {
  if (t.length === 0) return n;
  const [r, ...o] = t, s = He(e).find((c) => c.id === r.slotId);
  if (!s) return e;
  const a = s.activities.map((c) => c.nodeId === r.ownerNodeId ? ed(c, o, n) : c);
  return ur(e, s, a);
}
function td(e, t, n) {
  if (e.nodeId === t) return n(e);
  const r = He(e);
  if (r.length === 0) return e;
  let o = !1, s = e;
  for (const a of r) {
    const c = a.activities.map((u) => {
      const l = td(u, t, n);
      return l !== u && (o = !0), l;
    });
    o && (s = ur(s, a, c));
  }
  return o ? s : e;
}
function ur(e, t, n) {
  return e.structure ? {
    ...e,
    structure: {
      ...e.structure,
      payload: {
        ...e.structure.payload,
        [t.property]: n
      }
    }
  } : e;
}
function yv(e, t, n, r = []) {
  const o = new Map(e.slot.activities.map((a) => [a.nodeId, a]));
  for (const a of r)
    o.set(a.nodeId, a);
  const s = t.map((a) => o.get(a.id)).filter((a) => !!a);
  return e.slot.mode === "sequence" && s.sort((a, c) => {
    const u = t.find((f) => f.id === a.nodeId), l = t.find((f) => f.id === c.nodeId);
    return (u?.position.x ?? 0) - (l?.position.x ?? 0);
  }), ur(e.owner, e.slot, s);
}
function mv(e, t) {
  return {
    ...e,
    structure: Sv(e.structure, t)
  };
}
function xv(e, t) {
  const n = new Map(e.map((r) => [r.nodeId, r]));
  for (const r of t)
    n.set(r.id, {
      ...n.get(r.id) ?? { nodeId: r.id },
      nodeId: r.id,
      x: Math.round(r.position.x),
      y: Math.round(r.position.y)
    });
  return [...n.values()];
}
function zi(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: jv(e)
  };
}
function Ie(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? Nv(t) : n;
}
function nd(e, t, n, r = {}) {
  return {
    id: e.nodeId,
    type: "workflowActivity",
    position: n,
    connectable: r.connectable,
    deletable: r.deletable,
    draggable: r.draggable,
    data: {
      label: t ? Ie(t) : e.activityVersionId,
      activityVersionId: e.activityVersionId,
      activityTypeKey: t?.activityTypeKey,
      category: t?.category,
      executionType: t?.executionType,
      icon: wo(t),
      childSlots: He(e),
      acceptsInbound: Cv(e, t),
      sourcePorts: r.suppressFlowPorts ? [] : od(e, t),
      suppressFlowPorts: r.suppressFlowPorts
    }
  };
}
function wo(e) {
  if (!e) return "activity";
  const t = wv(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), r = Ie(e).toLowerCase(), o = e.category?.toLowerCase() ?? "", s = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || r === "flowchart" ? "flowchart" : n.endsWith(".sequence") || r === "sequence" ? "sequence" : n.includes("writeline") || r.includes("write line") ? "terminal" : o.includes("runtime") ? "runtime" : s === "trigger" ? "trigger" : "activity";
}
function wv(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function vv(e) {
  return !!e && (Ie(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function bv(e) {
  return !!e && (Ie(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function Nv(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function jv(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: wr,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: jn,
    schemaVersion: "1.0.0",
    payload: {
      activities: [],
      connections: [],
      startNodeId: null,
      nodeMetadata: {},
      connectionMetadata: {}
    }
  } : null;
}
function Sv(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], r = /* @__PURE__ */ new Map();
  for (const o of n) {
    if (!xs(o)) continue;
    const s = o.id;
    typeof s == "string" && r.set(s, o);
  }
  return {
    ...e,
    payload: {
      ...e.payload,
      connections: t.map((o) => {
        const s = r.get(o.id) ?? {}, a = o.data?.vertices, { vertices: c, ...u } = s;
        return {
          ...u,
          id: o.id,
          source: { nodeId: o.source, port: o.sourceHandle ?? "Done" },
          target: o.targetHandle ? { nodeId: o.target, port: o.targetHandle } : { nodeId: o.target },
          ...a?.length ? { vertices: a.map((l) => ({ x: Math.round(l.x), y: Math.round(l.y) })) } : {}
        };
      })
    }
  };
}
function Ev(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, r) => ({
    id: `sequence-${n.id}-${t[r + 1].id}`,
    source: n.id,
    target: t[r + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function rd(e) {
  if (e.structure?.kind !== jn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, r) => {
    if (!n || typeof n != "object") return null;
    const o = n.source, s = n.target;
    if (!o?.nodeId || !s?.nodeId) return null;
    const a = Array.isArray(n.vertices) ? n.vertices.filter(Dv) : [];
    return {
      id: typeof n.id == "string" ? String(n.id) : `flow-${r}-${o.nodeId}-${s.nodeId}`,
      source: o.nodeId,
      target: s.nodeId,
      sourceHandle: o.port,
      targetHandle: s.port && s.port !== "Done" ? s.port : void 0,
      type: "workflow",
      label: o.port && o.port !== "Done" ? o.port : void 0,
      data: a.length ? { vertices: a } : void 0
    };
  }).filter((n) => !!n) : [];
}
function od(e, t) {
  const n = ic(e.cases);
  if (kv(e, t) && n.length > 0)
    return [...n.map((s) => ({ name: s, displayName: s })), { name: "Default", displayName: "Default" }];
  const r = [
    ...Jr(t?.designFacets),
    ...Jr(t?.ports),
    ...Jr(t?.outputs)
  ];
  if (r.length > 0) return Av(r);
  const o = ic(e.outcomes);
  return o.length > 0 ? o.map((s) => ({ name: s, displayName: s })) : [{ name: "Done", displayName: "Done" }];
}
function Cv(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function vo(e, t, n, r) {
  const o = n ?? "Done";
  return {
    id: `flow-${e}-${t}-${o}-${crypto.randomUUID().slice(0, 8)}`,
    source: e,
    target: t,
    sourceHandle: o,
    targetHandle: r ?? void 0,
    type: "workflow",
    label: o !== "Done" ? o : void 0
  };
}
function Iv(e, t, n) {
  const r = vo(t.source, n, t.sourceHandle ?? "Done", void 0), o = vo(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((s) => s.id !== t.id).concat(r, o);
}
function xi(e) {
  return Array.isArray(e) ? e.filter(Tv) : null;
}
function kv(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, r = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || r === "FlowSwitch";
}
function Jr(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!xs(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...Jr(n.ports));
      continue;
    }
    const r = typeof n.type == "string" ? n.type : typeof n.portType == "string" ? n.portType : "", o = n.isBrowsable !== !1 && n.browsable !== !1, s = typeof n.name == "string" ? n.name : typeof n.id == "string" ? n.id : "";
    if (o && r.toLowerCase() === "flow" && s) {
      const a = typeof n.displayName == "string" ? n.displayName : s;
      t.push({ name: s, displayName: a });
    }
  }
  return t;
}
function Av(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function ic(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function sc(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const r of e) {
    const o = t(r);
    o && n.set(o, [...n.get(o) ?? [], r]);
  }
  return n;
}
function _v(e) {
  return [...e].sort((t, n) => ac(n).localeCompare(ac(t)))[0];
}
function ac(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function Dv(e) {
  return xs(e) && typeof e.x == "number" && typeof e.y == "number";
}
function xs(e) {
  return typeof e == "object" && e !== null;
}
function Tv(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function $v(e) {
  return e.kind === wr ? "sequence" : e.kind === jn ? "flowchart" : "generic";
}
function Mv(e) {
  return e.kind === wr || e.kind === jn, "Activities";
}
function Pv(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function Rv(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const bo = "workflow", Lv = /* @__PURE__ */ new Set([wr, jn]);
function zv(e) {
  const t = e?.structure?.kind;
  return !!t && Lv.has(t);
}
function id(e) {
  const t = e?.structure?.payload?.variables;
  return Array.isArray(t) ? t.filter(xo) : [];
}
function Vv(e, t) {
  return e.structure ? {
    ...e,
    structure: {
      ...e.structure,
      payload: { ...e.structure.payload, variables: t }
    }
  } : e;
}
function Ov(e, t) {
  return {
    referenceKey: e,
    declaringScopeId: t && t !== bo ? t : bo
  };
}
function sd(e) {
  if (e && typeof e == "object") {
    const t = e;
    return typeof t.referenceKey == "string" && t.referenceKey ? { referenceKey: t.referenceKey, declaringScopeId: typeof t.declaringScopeId == "string" ? t.declaringScopeId : null } : null;
  }
  if (typeof e == "string") {
    const t = e.trim();
    if (t.startsWith("{"))
      try {
        return sd(JSON.parse(t));
      } catch {
      }
    return t ? { referenceKey: t, declaringScopeId: null } : null;
  }
  return null;
}
function Hv(e) {
  if (!e) return "";
  const t = [`workflow:${cc(e.variables)}`], n = (r) => {
    const o = He(r), s = o.flatMap((a) => a.activities.map((c) => c.nodeId));
    t.push(`${r.nodeId}:${cc(id(r))}>${s.join(",")}`), o.forEach((a) => a.activities.forEach(n));
  };
  return e.rootActivity && n(e.rootActivity), t.join(";");
}
function cc(e) {
  return (e ?? []).map((t) => `${t.referenceKey}=${t.name}`).join(",");
}
function Wv(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const r of e ?? [])
    r.scopeId === t && n.set(r.referenceKey, `Shadows "${r.name}" declared in an outer scope.`);
  return n;
}
const Se = "/_elsa/workflow-management", Fv = "/publishing", Qn = {
  activities: ["workflows", "activities"],
  activityAvailabilitySettings: ["workflows", "activity-availability", "settings"],
  activityAvailabilityDiagnostics: ["workflows", "activity-availability", "diagnostics"]
};
function Bv(e) {
  return Gc({
    queryKey: Qn.activityAvailabilitySettings,
    queryFn: () => l0(e)
  });
}
function Kv(e) {
  return Gc({
    queryKey: Qn.activityAvailabilityDiagnostics,
    queryFn: () => ld(e)
  });
}
function Uv(e) {
  const t = _f();
  return Df({
    mutationFn: (n) => u0(e, n),
    onSuccess: () => {
      t.invalidateQueries({ queryKey: Qn.activityAvailabilitySettings }), t.invalidateQueries({ queryKey: Qn.activityAvailabilityDiagnostics }), t.invalidateQueries({ queryKey: Qn.activities });
    }
  });
}
async function Xv(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), r = t.search.trim();
  return r && n.set("search", r), e.http.getJson(`${Se}/definitions?${n.toString()}`);
}
async function qv(e, t) {
  const n = await e.http.getJson(`${Se}/definitions/${encodeURIComponent(t)}`);
  return n.draft ? { ...n, draft: { ...n.draft, state: Vo(n.draft.state) } } : n;
}
async function Yv(e, t, n) {
  const r = await e.http.postJson(
    `${Se}/design/scoped-variables/analyze`,
    { state: xr(t), nodeId: n }
  );
  return {
    visibleVariables: Array.isArray(r?.visibleVariables) ? r.visibleVariables : [],
    shadowingWarnings: Array.isArray(r?.shadowingWarnings) ? r.shadowingWarnings : []
  };
}
const wi = (e) => ({ visibleVariables: [], shadowingWarnings: [], status: e });
function Zv(e, t, n) {
  const r = de(() => Hv(t), [t]), [o, s] = U(() => wi("loading"));
  return Q(() => {
    if (!t) {
      s(wi("unavailable"));
      return;
    }
    let a = !1;
    return s((c) => ({ ...c, status: "loading" })), Yv(e, t, n).then(
      (c) => {
        a || s({ ...c, status: "ready" });
      },
      () => {
        a || s(wi("unavailable"));
      }
    ), () => {
      a = !0;
    };
  }, [e, n, r]), o;
}
async function Gv(e, t) {
  const n = await e.http.getJson(`${Se}/versions/${encodeURIComponent(t)}`);
  return { ...n, state: Vo(n.state) };
}
async function Jv(e, t) {
  return e.http.postJson(`${Se}/definitions`, t);
}
async function Qv(e, t) {
  await e.http.deleteJson(`${Se}/definitions/${encodeURIComponent(t)}`);
}
async function e0(e, t) {
  await e.http.postJson(`${Se}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function t0(e, t) {
  await e.http.deleteJson(`${Se}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function n0(e, t, n) {
  return e.http.requestJson(
    `${Se}/definitions/${encodeURIComponent(t)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(n)
    }
  );
}
async function r0(e, t) {
  const n = await e.http.putJson(
    `${Se}/drafts/${encodeURIComponent(t.id)}`,
    { state: xr(t.state), layout: t.layout }
  );
  return { ...n, state: Vo(n.state) };
}
async function o0(e, t) {
  return e.http.postJson(`${Se}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function i0(e, t) {
  return e.http.postJson(`${Se}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function s0(e, t) {
  const n = { ...t, state: xr(t.state) };
  try {
    return await e.http.postJson(`${Fv}/workflows/drafts/test-runs`, n);
  } catch (r) {
    const o = m0(r);
    if (o) return o;
    throw r;
  }
}
async function ad(e, t) {
  return e.http.postJson(`${Se}/executables/${encodeURIComponent(t)}/run`, {});
}
async function cd(e) {
  return e.http.getJson("/_demo/workflows/executables");
}
async function a0(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.runKind && n.set("runKind", t.runKind), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const r = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${r ? `?${r}` : ""}`);
}
async function c0(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function ws(e) {
  return e.http.getJson(`${Se}/activities`);
}
async function l0(e) {
  return e.http.getJson(`${Se}/activities/availability/settings`);
}
async function u0(e, t) {
  return e.http.putJson(`${Se}/activities/availability/settings`, t);
}
async function ld(e) {
  return e.http.getJson(`${Se}/activities/availability/diagnostics`);
}
async function d0(e) {
  const t = await Oo(e, [
    `${Se}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? lc(t) : lc(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function f0(e) {
  const t = await Oo(e, [
    `${Se}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : Qr;
}
async function p0(e) {
  const t = await Oo(e, [
    `${Se}/descriptors/variables`,
    "/descriptors/variables"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((r) => h0(r));
}
function h0(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = typeof t.alias == "string" && t.alias.length > 0, r = typeof t.typeName == "string" && t.typeName.length > 0;
  return n || r;
}
async function g0(e) {
  const t = await Oo(e, [
    `${Se}/descriptors/storage-drivers`,
    "/descriptors/storage-drivers"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((r) => y0(r));
}
function y0(e) {
  return !!e && typeof e == "object" && typeof e.typeName == "string" && e.typeName.length > 0;
}
async function Oo(e, t) {
  let n;
  for (const r of t)
    try {
      return await e.http.getJson(r);
    } catch (o) {
      n = o;
    }
  throw n;
}
function lc(e) {
  return Array.isArray(e) ? e.filter((t) => {
    if (!t || typeof t != "object") return !1;
    const n = t;
    return typeof n.typeName == "string" && Array.isArray(n.inputs);
  }).map((t) => ({
    ...t,
    inputs: t.inputs ?? [],
    outputs: t.outputs ?? [],
    ports: t.ports ?? []
  })) : [];
}
function m0(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = uc(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return uc(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function uc(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const Qr = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
], x0 = [
  "Available",
  "BlockedByHostBaseline",
  "HiddenByManagementSettings",
  "RemovedFromCatalog",
  "UnresolvedReference"
], w0 = {
  Available: "Available",
  BlockedByHostBaseline: "Host blocked",
  HiddenByManagementSettings: "Management hidden",
  RemovedFromCatalog: "Removed",
  UnresolvedReference: "Unresolved"
};
function Rt(e) {
  return typeof e == "string" ? e : typeof e == "number" ? x0[e] ?? "Available" : "Available";
}
function No(e) {
  const t = Rt(e);
  return w0[t] ?? t;
}
function v0(e) {
  return Rt(e).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function b0(e) {
  return Rt(e) !== "Available";
}
function N0(e) {
  return e === "Only" || e === 1 ? "Only" : "AllExcept";
}
function j0(e) {
  return e === "Only" ? 1 : 0;
}
function dc(e) {
  const t = e?.rules;
  return {
    mode: N0(e?.mode),
    activityTypes: t?.activityTypes ?? [],
    sets: t?.sets ?? []
  };
}
function S0(e) {
  return e.referenceKind === 0 || e.referenceKind === "ActivityType";
}
function E0(e) {
  return [...e?.items ?? []].filter(S0).filter((t) => t.activityTypeKey && t.activityDefinitionId).sort((t, n) => jo(t).localeCompare(jo(n)));
}
function C0(e) {
  return [...e?.items ?? []].filter((t) => {
    const n = Rt(t.state);
    return n === "RemovedFromCatalog" || n === "UnresolvedReference";
  }).sort((t, n) => (t.referenceName ?? "").localeCompare(n.referenceName ?? ""));
}
function fc(e, t) {
  return e.includes(t) ? e.filter((n) => n !== t) : [...e, t].sort((n, r) => n.localeCompare(r));
}
function jo(e) {
  const t = e?.displayName?.trim();
  if (t) return t;
  const n = e?.activityTypeKey?.split(".").filter(Boolean).at(-1) ?? "";
  return I0(n) || e?.activityTypeKey || "Activity";
}
function I0(e) {
  return e.replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").replace(/([a-z0-9])([A-Z])/g, "$1 $2").trim();
}
function k0(e, t) {
  const n = new Set(e.filter((r) => !!r));
  return (t?.items ?? []).find((r) => b0(r.state) ? [r.activityDefinitionId, r.activityTypeKey, r.referenceName].some((o) => o && n.has(o)) : !1) ?? null;
}
const A0 = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function ud(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function vs(e) {
  return ud(e.name);
}
function _0(e, t) {
  const n = vs(t), r = e[n];
  return t.isWrapped === !1 ? r ?? t.defaultValue ?? "" : fd(r, t);
}
function dd(e, t) {
  return fd(e[vs(t)], t);
}
function D0(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function T0(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function pc(e, t, n) {
  return {
    ...e,
    [vs(t)]: n
  };
}
function $0(e, t) {
  return t.isWrapped === !1 ? _0(e, t) : dd(e, t).expression.value;
}
function fd(e, t) {
  return H0(e) ? {
    typeName: e.typeName || t.typeName,
    expression: {
      type: e.expression.type || t.defaultSyntax || "Literal",
      value: e.expression.value
    },
    ...e.memoryReference ? { memoryReference: e.memoryReference } : {}
  } : {
    typeName: t.typeName,
    expression: {
      type: t.defaultSyntax || "Literal",
      value: e ?? t.defaultValue ?? ""
    }
  };
}
const M0 = /* @__PURE__ */ new Set([
  "icollection",
  "ireadonlycollection",
  "ilist",
  "ireadonlylist",
  "list",
  "collection",
  "ienumerable",
  "iset",
  "hashset",
  "sortedset",
  "observablecollection",
  "array"
]);
function P0(e) {
  const t = e?.trim();
  if (!t) return null;
  const n = /^([\w.+]+)\[\]/.exec(t);
  if (n) return { elementTypeName: n[1] };
  const r = t.indexOf("`");
  if (r < 0) return null;
  const o = t.slice(0, r), s = (o.split(".").pop() ?? o).toLowerCase();
  return M0.has(s) ? { elementTypeName: R0(t.slice(r)) } : null;
}
function R0(e) {
  const t = /\[\[([\w.+]+)/.exec(e);
  if (t) return t[1];
  const n = /\[([\w.+]+)/.exec(e);
  return n ? n[1] : null;
}
function L0(e) {
  const t = e.uiHint?.toLowerCase();
  return t === "json" || t === "code" ? !0 : e.uiSpecifications?.repeater === !1;
}
function z0(e) {
  if (Array.isArray(e)) return e;
  if (e == null) return [];
  if (typeof e == "string") {
    const t = e.trim();
    if (!t) return [];
    if (t.startsWith("["))
      try {
        const n = JSON.parse(t);
        if (Array.isArray(n)) return n;
      } catch {
      }
    return [e];
  }
  return [e];
}
function V0(e) {
  const t = (e ?? "").toLowerCase();
  return t === "system.boolean" || t === "boolean" || t === "bool" ? !1 : "";
}
function O0(e, t) {
  return {
    ...e,
    typeName: t ?? "System.String",
    isWrapped: !1,
    // The description documents the collection as a whole; repeating it on every row is noise.
    description: null
  };
}
function vi(e, t, n) {
  if (t === n || t < 0 || n < 0 || t >= e.length || n >= e.length) return e;
  const r = [...e], [o] = r.splice(t, 1);
  return r.splice(n, 0, o), r;
}
function H0(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
function pd(e) {
  return bs(e?.trim() ?? "") || e;
}
function bs(e) {
  if (!e) return "";
  const t = W0(e);
  if (!t) return "";
  const n = /^(.*)((?:\[\])+)$/.exec(t);
  if (n) return `${bs(n[1])}${n[2]}`;
  const r = t.indexOf("`");
  if (r >= 0) {
    const o = hc(t.slice(0, r)), s = F0(t.slice(r));
    return s.length > 0 ? `${o}<${s.join(", ")}>` : o;
  }
  return hc(t);
}
function W0(e) {
  let t = 0;
  for (let n = 0; n < e.length; n++) {
    const r = e[n];
    if (r === "[") t++;
    else if (r === "]") t--;
    else if (r === "," && t === 0) return e.slice(0, n).trim();
  }
  return e.trim();
}
function hc(e) {
  const t = e.split(".").filter(Boolean).at(-1) ?? e;
  return t.split("+").filter(Boolean).at(-1) ?? t;
}
function F0(e) {
  const t = e.indexOf("[");
  if (t < 0) return [];
  const n = gc(e, t);
  return n == null ? [] : B0(n).map((r) => {
    const o = r.trim(), s = o.startsWith("[") ? gc(o, 0) ?? o : o;
    return bs(s);
  }).filter(Boolean);
}
function gc(e, t) {
  let n = 0;
  for (let r = t; r < e.length; r++)
    if (e[r] === "[") n++;
    else if (e[r] === "]" && --n === 0) return e.slice(t + 1, r);
  return null;
}
function B0(e) {
  const t = [];
  let n = 0, r = 0;
  for (let o = 0; o < e.length; o++) {
    const s = e[o];
    s === "[" ? n++ : s === "]" ? n-- : s === "," && n === 0 && (t.push(e.slice(r, o)), r = o + 1);
  }
  return t.push(e.slice(r)), t.map((o) => o.trim()).filter(Boolean);
}
const hd = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]), K0 = "Variable";
function U0({
  activity: e,
  descriptor: t,
  editors: n,
  expressionEditors: r,
  expressionDescriptors: o,
  descriptorStatus: s,
  visibleVariables: a,
  scopeStatus: c,
  onChange: u
}) {
  if (s === "loading")
    return /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "Loading activity properties..." });
  if (!t)
    return /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "No activity descriptor is available for this activity." });
  const l = t.inputs.filter((d) => d.isBrowsable !== !1).sort((d, h) => (d.order ?? 0) - (h.order ?? 0) || d.name.localeCompare(h.name));
  if (l.length === 0)
    return /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "This activity does not expose editable properties." });
  const f = eb(l), p = o.length > 0 ? o : A0;
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ i.jsx("span", { className: "wf-section-label", children: "Properties" }),
    f.map((d) => /* @__PURE__ */ i.jsxs("section", { className: "wf-property-group", children: [
      f.length > 1 ? /* @__PURE__ */ i.jsx("h4", { children: d.category }) : null,
      d.inputs.map((h) => /* @__PURE__ */ i.jsx(
        X0,
        {
          activity: e,
          input: h,
          editors: n,
          expressionEditors: r,
          expressionDescriptors: p,
          visibleVariables: a,
          scopeStatus: c,
          onChange: u
        },
        h.name
      ))
    ] }, d.category))
  ] });
}
function X0({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: r,
  expressionDescriptors: o,
  visibleVariables: s,
  scopeStatus: a,
  onChange: c
}) {
  const u = t.isReadOnly === !0, l = { activity: e, expressionDescriptors: o, readOnly: u }, f = Wi(n, t, l), p = f?.component, d = t.isWrapped !== !1 ? dd(e, t) : null, h = d?.expression.type ?? "Literal", g = $0(e, t), N = h.toLowerCase(), m = d && (N === "literal" || N === "object") && !L0(t) ? P0(t.typeName) : null, C = m ? Wi(n, t, { ...l, scope: "collection" }) : void 0, y = d ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: o,
    readOnly: u,
    surface: "inline",
    syntax: h
  } : null, b = y ? yd(r, y) : null, j = b?.surfaces.inline, v = b && y ? md(b, y, g) : [], S = m != null, A = !!(d && !S && tb(t, f?.id)), M = !!(d && !S && nb(t, f?.id)), [R, $] = U(!1), _ = (k) => {
    const T = d ? D0(d, k) : k;
    c(pc(e, t, T));
  }, V = (k) => {
    d && c(pc(e, t, T0(d, k)));
  }, E = m ? C ? Vi(C.component, t, g, u, { ...l, scope: "collection" }, _) : /* @__PURE__ */ i.jsx(
    Y0,
    {
      input: t,
      elementTypeName: m.elementTypeName,
      value: g,
      editors: n,
      context: l,
      disabled: u,
      onChange: _
    }
  ) : null, D = h === K0 && d ? /* @__PURE__ */ i.jsx(
    J0,
    {
      value: g,
      visibleVariables: s,
      scopeStatus: a,
      disabled: u,
      onChange: _
    }
  ) : E ?? (j && y ? /* @__PURE__ */ i.jsx(
    j,
    {
      descriptor: t,
      syntax: h,
      value: g,
      disabled: u,
      context: y,
      onChange: _
    }
  ) : Vi(p, t, g, u, l, _));
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ i.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ i.jsx("span", { children: pd(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ i.jsx("p", { children: t.description }) : null,
    d && !A ? /* @__PURE__ */ i.jsx(
      Oi,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: h,
        descriptors: o,
        disabled: u,
        onChange: V
      }
    ) : null,
    A ? /* @__PURE__ */ i.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-expression-editor", children: [
        D,
        Fi(v)
      ] }),
      /* @__PURE__ */ i.jsx(
        Oi,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: h,
          descriptors: o,
          disabled: u,
          variant: "inline",
          onChange: V
        }
      ),
      M ? /* @__PURE__ */ i.jsx(
        "button",
        {
          type: "button",
          className: "wf-expression-expand-button",
          "aria-label": `Open expanded ${t.displayName || t.name} editor`,
          title: "Open expanded editor",
          onClick: () => $(!0),
          children: /* @__PURE__ */ i.jsx(ro, { size: 13 })
        }
      ) : null
    ] }) : /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
      D,
      Fi(v)
    ] }),
    M && !A ? /* @__PURE__ */ i.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => $(!0),
        children: [
          /* @__PURE__ */ i.jsx(ro, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    R ? /* @__PURE__ */ i.jsx(
      Z0,
      {
        input: t,
        value: g,
        syntax: h,
        descriptors: o,
        activity: e,
        expressionEditors: r,
        disabled: u,
        onChange: _,
        onSyntaxChange: V,
        onClose: () => $(!1)
      }
    ) : null
  ] });
}
function q0(e, t, n) {
  return [
    "wf-collection-item",
    t === e ? "dragging" : "",
    t !== null && t !== e && n === e ? "drop-target" : ""
  ].filter(Boolean).join(" ");
}
function Y0({
  input: e,
  elementTypeName: t,
  value: n,
  editors: r,
  context: o,
  disabled: s,
  onChange: a
}) {
  const c = z0(n), u = O0(e, t), l = { ...o, scope: "element" }, f = Wi(r, u, l)?.component, p = e.displayName || e.name, d = (j, v) => a(c.map((S, A) => A === j ? v : S)), [h, g] = U(null), [N, w] = U(null), m = () => {
    g(null), w(null);
  }, C = (j) => (v) => {
    g(j), v.dataTransfer.effectAllowed = "move", v.dataTransfer.setData("text/plain", String(j));
  }, y = (j) => (v) => {
    h !== null && (v.preventDefault(), v.dataTransfer.dropEffect = "move", N !== j && w(j));
  }, b = (j) => (v) => {
    v.preventDefault(), h !== null && h !== j && a(vi(c, h, j)), m();
  };
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-collection-editor", children: [
    c.length === 0 ? /* @__PURE__ */ i.jsx("p", { className: "wf-collection-empty", children: "No items yet." }) : /* @__PURE__ */ i.jsx("ul", { className: "wf-collection-items", children: c.map((j, v) => /* @__PURE__ */ i.jsxs(
      "li",
      {
        className: q0(v, h, N),
        onDragOver: y(v),
        onDrop: b(v),
        children: [
          /* @__PURE__ */ i.jsx(
            "span",
            {
              className: "wf-collection-item-handle",
              draggable: !s,
              "aria-label": `Drag ${p} item ${v + 1} to reorder`,
              title: "Drag to reorder",
              onDragStart: C(v),
              onDragEnd: m,
              children: /* @__PURE__ */ i.jsx(Kc, { size: 13, "aria-hidden": "true" })
            }
          ),
          /* @__PURE__ */ i.jsx("div", { className: "wf-collection-item-editor", children: Vi(f, u, j, s, l, (S) => d(v, S)) }),
          /* @__PURE__ */ i.jsxs("div", { className: "wf-collection-item-actions", children: [
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${p} item ${v + 1} up`,
                disabled: s || v === 0,
                onClick: () => a(vi(c, v, v - 1)),
                children: /* @__PURE__ */ i.jsx(mf, { size: 13 })
              }
            ),
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${p} item ${v + 1} down`,
                disabled: s || v === c.length - 1,
                onClick: () => a(vi(c, v, v + 1)),
                children: /* @__PURE__ */ i.jsx(Uc, { size: 13 })
              }
            ),
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button danger",
                "aria-label": `Remove ${p} item ${v + 1}`,
                disabled: s,
                onClick: () => a(c.filter((S, A) => A !== v)),
                children: /* @__PURE__ */ i.jsx(er, { size: 13 })
              }
            )
          ] })
        ]
      },
      v
    )) }),
    /* @__PURE__ */ i.jsxs(
      "button",
      {
        type: "button",
        className: "wf-collection-add",
        disabled: s,
        onClick: () => a([...c, V0(t)]),
        children: [
          /* @__PURE__ */ i.jsx(dn, { size: 13 }),
          " Add item"
        ]
      }
    )
  ] });
}
function Z0({
  input: e,
  value: t,
  syntax: n,
  descriptors: r,
  activity: o,
  expressionEditors: s,
  disabled: a,
  onChange: c,
  onSyntaxChange: u,
  onClose: l
}) {
  const f = Bc(), p = e.displayName || e.name, d = {
    activity: o,
    descriptor: e,
    expressionDescriptors: r,
    readOnly: a,
    surface: "expanded",
    syntax: n
  }, h = yd(s, d), g = h?.surfaces.expanded, N = h ? md(h, d, t) : [], w = g ? null : Q0(s, d);
  return Q(() => {
    const m = (C) => {
      C.key === "Escape" && l();
    };
    return window.addEventListener("keydown", m), () => window.removeEventListener("keydown", m);
  }, [l]), /* @__PURE__ */ i.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ i.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": f, children: [
    /* @__PURE__ */ i.jsxs("header", { children: [
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ i.jsx("h3", { id: f, children: p })
      ] }),
      /* @__PURE__ */ i.jsx("button", { type: "button", "aria-label": `Close ${p} editor`, onClick: l, children: /* @__PURE__ */ i.jsx(Xc, { size: 16 }) })
    ] }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ i.jsx(
          Oi,
          {
            label: `${p} expression syntax`,
            value: n,
            descriptors: r,
            disabled: a,
            onChange: u
          }
        ),
        /* @__PURE__ */ i.jsx("span", { children: pd(e.typeName) })
      ] }),
      e.description ? /* @__PURE__ */ i.jsx("p", { children: e.description }) : null,
      g ? /* @__PURE__ */ i.jsx(
        g,
        {
          descriptor: e,
          syntax: n,
          value: t,
          disabled: a,
          context: d,
          onChange: c
        }
      ) : /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
        w ? /* @__PURE__ */ i.jsx("p", { className: "wf-expression-editor-hint", children: w }) : null,
        /* @__PURE__ */ i.jsx(
          "textarea",
          {
            "aria-label": `${p} expanded value`,
            value: t == null ? "" : String(t),
            disabled: a,
            spellCheck: !1,
            onChange: (m) => c(m.target.value)
          }
        )
      ] }),
      Fi(N)
    ] }),
    /* @__PURE__ */ i.jsxs("footer", { children: [
      /* @__PURE__ */ i.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: l, children: "Close" })
    ] })
  ] }) });
}
function Vi(e, t, n, r, o, s) {
  return e ? /* @__PURE__ */ i.jsx(
    e,
    {
      descriptor: t,
      value: n,
      disabled: r,
      context: o,
      onChange: s
    }
  ) : /* @__PURE__ */ i.jsx("input", { type: "text", value: n == null ? "" : String(n), disabled: r, onChange: (a) => s(a.target.value) });
}
function Oi({
  label: e,
  value: t,
  descriptors: n,
  disabled: r,
  variant: o = "block",
  onChange: s
}) {
  const [a, c] = U(!1), u = Bc(), l = n.find((p) => p.type === t), f = [
    "wf-syntax-picker-trigger",
    o === "inline" ? "inline" : "",
    a ? "open" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ i.jsxs("div", { className: o === "inline" ? "wf-syntax-picker inline" : "wf-syntax-picker", onBlur: (p) => {
    p.currentTarget.contains(p.relatedTarget) || c(!1);
  }, children: [
    /* @__PURE__ */ i.jsx(
      "button",
      {
        type: "button",
        className: f,
        "aria-label": e,
        "aria-haspopup": "listbox",
        "aria-expanded": a,
        "aria-controls": u,
        disabled: r,
        onClick: () => c((p) => !p),
        children: /* @__PURE__ */ i.jsx("span", { children: l?.displayName || l?.type || t })
      }
    ),
    a ? /* @__PURE__ */ i.jsx("div", { id: u, role: "listbox", className: "wf-syntax-picker-menu", "aria-label": e, children: n.map((p) => {
      const d = p.displayName || p.type, h = p.type === t;
      return /* @__PURE__ */ i.jsx(
        "button",
        {
          type: "button",
          role: "option",
          "aria-selected": h,
          className: h ? "selected" : "",
          onClick: () => {
            s(p.type), c(!1);
          },
          children: d
        },
        p.type
      );
    }) }) : null
  ] });
}
const Hi = "::";
function gd(e) {
  return !e || e === bo ? bo : e;
}
function yc(e, t) {
  return `${gd(t)}${Hi}${e}`;
}
function G0(e) {
  const t = e.indexOf(Hi);
  if (t < 0) return null;
  const n = e.slice(t + Hi.length);
  return n ? { scopeId: e.slice(0, t), referenceKey: n } : null;
}
function J0({ value: e, visibleVariables: t, scopeStatus: n, disabled: r, onChange: o }) {
  const s = sd(e), a = !!e && typeof e == "object" || typeof e == "string" && e.trim().startsWith("{"), c = s && (a || t.some((f) => f.referenceKey === s.referenceKey)) ? s : null, u = c ? yc(c.referenceKey, c.declaringScopeId) : "", l = !!c && t.some(
    (f) => f.referenceKey === c.referenceKey && f.scopeId === gd(c.declaringScopeId)
  );
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-variable-picker", children: [
    /* @__PURE__ */ i.jsxs(
      "select",
      {
        "aria-label": "Variable reference",
        value: u,
        disabled: r,
        onChange: (f) => {
          const p = G0(f.target.value);
          p && o(Ov(p.referenceKey, p.scopeId));
        },
        children: [
          /* @__PURE__ */ i.jsx("option", { value: "", children: "Select a variable…" }),
          c && !l ? /* @__PURE__ */ i.jsxs("option", { value: u, children: [
            c.referenceKey,
            " (not visible from this scope)"
          ] }) : null,
          t.map((f) => {
            const p = yc(f.referenceKey, f.scopeId);
            return /* @__PURE__ */ i.jsxs("option", { value: p, children: [
              f.name,
              f.isWorkflowScope ? " · workflow" : " · container"
            ] }, p);
          })
        ]
      }
    ),
    n === "unavailable" ? /* @__PURE__ */ i.jsx("p", { className: "wf-variable-picker-note", children: "Variable scope information is unavailable (pending backend support). Existing references are preserved." }) : n === "ready" && t.length === 0 ? /* @__PURE__ */ i.jsx("p", { className: "wf-variable-picker-note", children: "No variables are visible here. Declare one on the workflow or a container scope." }) : null
  ] });
}
function Wi(e, t, n) {
  return [...e].sort((r, o) => (r.order ?? 500) - (o.order ?? 500)).find((r) => r.supports(t, n));
}
function yd(e, t) {
  return [...e].sort((n, r) => (n.order ?? 500) - (r.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function md(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function Q0(e, t) {
  if (t.syntax.toLowerCase() === "literal") return null;
  const n = [...e].sort((a, c) => (a.order ?? 500) - (c.order ?? 500)).find((a) => a.supports(t) && a.metadata)?.metadata;
  if (!n) return `No enhanced editor is registered for ${t.syntax}. Using the generic text editor.`;
  const r = n.displayName?.trim() || "enhanced editor", o = n.installHint?.trim(), s = `No ${r} is registered for ${t.syntax}. Using the generic text editor.`;
  return o ? `${s} ${o}` : s;
}
function Fi(e) {
  return e.length === 0 ? null : /* @__PURE__ */ i.jsx("div", { className: "wf-expression-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const r = t.severity ?? "info";
    return /* @__PURE__ */ i.jsxs("p", { className: `wf-expression-editor-diagnostic ${r}`, children: [
      t.code ? /* @__PURE__ */ i.jsx("span", { children: t.code }) : null,
      t.message
    ] }, `${t.code ?? "diagnostic"}-${n}`);
  }) });
}
function eb(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const r = n.category?.trim() || "General";
    t.set(r, [...t.get(r) ?? [], n]);
  }
  return [...t.entries()].map(([n, r]) => ({ category: n, inputs: r }));
}
function tb(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !hd.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function nb(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !hd.has(t) && n !== "multiline") return !1;
  const r = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(r) || n === "singleline" || n === "multiline";
}
function rb(e, t) {
  return {
    ...t ? { name: t } : {},
    definitionId: e.definitionId,
    state: xr(e.state),
    layout: e.layout
  };
}
function ob(e) {
  return JSON.stringify(
    {
      state: xr(e.state),
      layout: e.layout
    },
    null,
    2
  );
}
function ib(e, t) {
  let n;
  try {
    n = JSON.parse(e);
  } catch (o) {
    return { ok: !1, error: o instanceof Error ? o.message : String(o) };
  }
  if (!n || typeof n != "object")
    return { ok: !1, error: "Workflow JSON must be an object with a 'state' property." };
  const r = n;
  return !r.state || typeof r.state != "object" ? { ok: !1, error: "Workflow JSON is missing a valid 'state' object." } : r.layout !== void 0 && !Array.isArray(r.layout) ? { ok: !1, error: "'layout' must be an array when present." } : {
    ok: !0,
    draft: {
      ...t,
      state: Vo(r.state),
      layout: r.layout ?? t.layout
    }
  };
}
function sb(e, t) {
  const n = (t ?? "workflow").trim().replace(/[^\w.-]+/g, "-") || "workflow", r = new Blob([JSON.stringify(e, null, 2)], { type: "application/json" }), o = URL.createObjectURL(r), s = document.createElement("a");
  s.href = o, s.download = `${n}.json`, document.body.appendChild(s), s.click(), s.remove(), URL.revokeObjectURL(o);
}
const ab = 320, cb = 140;
function lb(e, t, n) {
  return n === "sequence" ? ub(e) : db(e, t);
}
function ub(e) {
  const t = /* @__PURE__ */ new Map();
  return e.forEach((n, r) => {
    t.set(n.id, { x: r * 280, y: 0 });
  }), t;
}
function db(e, t) {
  const n = /* @__PURE__ */ new Map();
  if (e.length === 0) return n;
  const r = new Set(e.map((f) => f.id)), o = t.filter((f) => r.has(f.source) && r.has(f.target)), s = /* @__PURE__ */ new Set();
  for (const f of o)
    s.add(f.source), s.add(f.target);
  const a = /* @__PURE__ */ new Map();
  for (const f of e) a.set(f.id, 0);
  for (let f = 0; f < e.length; f += 1) {
    let p = !1;
    for (const d of o) {
      const h = (a.get(d.source) ?? 0) + 1;
      h > (a.get(d.target) ?? 0) && h <= e.length && (a.set(d.target, h), p = !0);
    }
    if (!p) break;
  }
  const c = Math.max(0, ...e.filter((f) => s.has(f.id)).map((f) => a.get(f.id) ?? 0)), u = s.size > 0 ? c + 1 : 0, l = /* @__PURE__ */ new Map();
  for (const f of e) {
    const p = s.has(f.id) ? a.get(f.id) ?? 0 : u, d = l.get(p);
    d ? d.push(f.id) : l.set(p, [f.id]);
  }
  for (const [f, p] of l)
    p.forEach((d, h) => {
      n.set(d, { x: f * ab, y: h * cb });
    });
  return n;
}
const fb = 50;
function mc() {
  return { past: [], future: [] };
}
function pb(e) {
  return e.past.length > 0;
}
function hb(e) {
  return e.future.length > 0;
}
function xc(e, t, n = fb) {
  const r = [...e.past, t];
  return r.length > n && r.splice(0, r.length - n), { past: r, future: [] };
}
function gb(e, t) {
  if (e.past.length === 0) return null;
  const n = e.past.slice(), r = n.pop();
  return { history: { past: n, future: [...e.future, t] }, snapshot: r };
}
function yb(e, t) {
  if (e.future.length === 0) return null;
  const n = e.future.slice(), r = n.pop();
  return { history: { past: [...e.past, t], future: n }, snapshot: r };
}
function tt(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function xd(e, t) {
  if (!e || !t) return "";
  const n = Date.parse(e), r = Date.parse(t);
  if (Number.isNaN(n) || Number.isNaN(r) || r < n) return "";
  const o = Math.round((r - n) / 1e3);
  if (o < 60) return `${o}s`;
  const s = Math.floor(o / 60), a = o % 60;
  if (s < 60) return a ? `${s}m ${a}s` : `${s}m`;
  const c = Math.floor(s / 60), u = s % 60;
  return u ? `${c}h ${u}m` : `${c}h`;
}
function So(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function Ns(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ i.jsx(qc, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ i.jsx(qi, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ i.jsx(wf, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ i.jsx(fn, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ i.jsx(xf, { size: 15 });
    default:
      return /* @__PURE__ */ i.jsx(Eo, { size: 15 });
  }
}
function wc({
  document: e,
  readOnly: t,
  minHeight: n,
  ariaLabel: r,
  onChange: o
}) {
  const s = (a) => {
    t || o({ ...e, value: a.target.value });
  };
  return /* @__PURE__ */ i.jsx(
    "textarea",
    {
      "aria-label": r,
      "aria-readonly": t,
      className: "studio-code-editor-input",
      readOnly: t,
      spellCheck: !1,
      autoCapitalize: "off",
      autoCorrect: "off",
      value: e.value,
      style: { minHeight: n },
      onChange: s
    }
  );
}
function mb({
  document: e,
  diagnostics: t = [],
  readOnly: n = !1,
  theme: r = "studio",
  minHeight: o = "220px",
  ariaLabel: s,
  languageAdapter: a,
  onChange: c
}) {
  const u = t.filter((d) => !d.uri || d.uri === e.uri), l = a?.displayName ?? e.language, f = a?.loadEditor, p = de(
    () => f ? gf(f) : null,
    [f]
  );
  return /* @__PURE__ */ i.jsxs(
    "section",
    {
      className: "studio-code-editor",
      "data-language": e.language,
      "data-theme": r,
      "data-readonly": n,
      children: [
        /* @__PURE__ */ i.jsxs("div", { className: "studio-code-editor-header", children: [
          /* @__PURE__ */ i.jsx("span", { children: l }),
          /* @__PURE__ */ i.jsx("code", { children: e.uri })
        ] }),
        p ? /* @__PURE__ */ i.jsx(yf, { fallback: /* @__PURE__ */ i.jsx(
          wc,
          {
            document: e,
            readOnly: n,
            minHeight: o,
            ariaLabel: s,
            onChange: c
          }
        ), children: /* @__PURE__ */ i.jsx(
          p,
          {
            document: e,
            readOnly: n,
            theme: r,
            minHeight: o,
            ariaLabel: s,
            onChange: c
          }
        ) }) : /* @__PURE__ */ i.jsx(
          wc,
          {
            document: e,
            readOnly: n,
            minHeight: o,
            ariaLabel: s,
            onChange: c
          }
        ),
        /* @__PURE__ */ i.jsx(xb, { diagnostics: u })
      ]
    }
  );
}
function xb({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ i.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const r = t.severity ?? "info", o = wb(t);
    return /* @__PURE__ */ i.jsxs(
      "p",
      {
        className: `studio-code-editor-diagnostic ${r}`,
        children: [
          t.code ? /* @__PURE__ */ i.jsx("span", { children: t.code }) : null,
          o ? /* @__PURE__ */ i.jsx("small", { children: o }) : null,
          t.message
        ]
      },
      `${t.uri ?? "document"}-${t.code ?? "diagnostic"}-${n}`
    );
  }) });
}
function wb(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const vb = { language: "json", displayName: "JSON" };
function bb({ draft: e, onApply: t }) {
  const n = de(() => ob(e), [e]), [r, o] = U(n), [s, a] = U(n), [c, u] = U(null);
  Q(() => {
    o(n), a(n), u(null);
  }, [n]);
  const l = r !== s, f = c ? [{ severity: "error", message: c }] : [], p = () => u(t(r));
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-code-view", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-code-view-toolbar", children: [
      /* @__PURE__ */ i.jsx("span", { className: "wf-muted", children: "Edit the workflow definition as JSON. Changes apply to the draft when you click Apply." }),
      /* @__PURE__ */ i.jsxs("span", { className: "wf-code-view-actions", children: [
        /* @__PURE__ */ i.jsx("button", { type: "button", disabled: !l, onClick: () => {
          o(s), u(null);
        }, children: "Reset" }),
        /* @__PURE__ */ i.jsxs("button", { type: "button", disabled: !l, onClick: p, children: [
          /* @__PURE__ */ i.jsx(bn, { size: 14 }),
          " Apply"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ i.jsx("div", { className: "wf-code-view-editor", children: /* @__PURE__ */ i.jsx(
      mb,
      {
        ariaLabel: "Workflow JSON",
        document: { uri: "elsa://workflows/definition.json", language: "json", value: r },
        languageAdapter: vb,
        diagnostics: f,
        minHeight: "100%",
        theme: "studio",
        onChange: (d) => {
          o(d.value), c && u(null);
        }
      }
    ) })
  ] });
}
const Nb = ["Single", "Array", "List", "HashSet"];
function wd(e) {
  const [t, n] = U(null), [r, o] = U(null);
  Q(() => {
    let u = !1;
    return p0(e).then(
      (l) => {
        u || n(l);
      },
      () => {
        u || n([]);
      }
    ), g0(e).then(
      (l) => {
        u || o(l);
      },
      () => {
        u || o([]);
      }
    ), () => {
      u = !0;
    };
  }, [e]);
  const s = de(
    () => t && t.length > 0 ? t.map((u) => {
      const l = rc(u);
      return {
        value: l,
        label: Uu(u.displayName, l),
        group: u.category?.trim() || "Other"
      };
    }) : null,
    [t]
  ), a = de(
    () => r && r.length > 0 ? r.filter((u) => !u.deprecated).map((u) => ({
      value: u.typeName,
      label: ev(u.displayName, u.typeName)
    })) : null,
    [r]
  ), c = de(() => {
    const u = /* @__PURE__ */ new Map();
    for (const l of t ?? []) {
      const f = rc(l), p = l.defaultEditor?.trim();
      f && u.set(f, p && p.length > 0 ? p : "text");
    }
    return (l) => u.get(l) ?? "text";
  }, [t]);
  return { typeOptions: s, storageOptions: a, editorForAlias: c };
}
function jb(e) {
  return !e || e.length === 0 ? void 0 : (e.find((n) => /(^|\.)String$/i.test(n.value) || n.label.toLowerCase() === "string") ?? e[0]).value;
}
function Sb(e, t, n) {
  return {
    add: () => {
      const r = Bw(n.namePrefix, e.map((o) => Gn(o, n.nameKeys)));
      t([...e, n.create(r)]);
    },
    update: (r, o) => t(e.map((s, a) => a === r ? n.patch(s, o) : s)),
    remove: (r) => t(e.filter((o, s) => s !== r))
  };
}
function vc({ value: e, options: t, placeholder: n, allowEmpty: r, ariaLabel: o, onChange: s }) {
  if (!t)
    return /* @__PURE__ */ i.jsx(
      "input",
      {
        type: "text",
        "aria-label": o,
        value: e,
        placeholder: n,
        onChange: (l) => s(l.target.value)
      }
    );
  const a = e === "" || t.some((l) => l.value === e), c = Array.from(new Set(t.map((l) => l.group).filter((l) => !!l))), u = c.length > 0;
  return /* @__PURE__ */ i.jsxs("select", { "aria-label": o, value: e, onChange: (l) => s(l.target.value), children: [
    r ? /* @__PURE__ */ i.jsx("option", { value: "", children: n ?? "—" }) : null,
    a ? null : /* @__PURE__ */ i.jsxs("option", { value: e, disabled: !0, children: [
      e,
      " (unresolved)"
    ] }),
    u ? c.map((l) => /* @__PURE__ */ i.jsx("optgroup", { label: l, children: t.filter((f) => f.group === l).map((f) => /* @__PURE__ */ i.jsx("option", { value: f.value, children: f.label }, f.value)) }, l)) : t.map((l) => /* @__PURE__ */ i.jsx("option", { value: l.value, children: l.label }, l.value))
  ] });
}
const Eb = {
  Single: "Single",
  Array: "Array",
  List: "List",
  HashSet: "Hash set"
};
function Cb({ value: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ i.jsx("select", { "aria-label": t, value: e, onChange: (r) => n(r.target.value), children: Nb.map((r) => /* @__PURE__ */ i.jsx("option", { value: r, children: Eb[r] }, r)) });
}
function Ib(e, t) {
  return t === "" ? !0 : e === "checkbox" ? t === "true" || t === "false" || t === "True" || t === "False" : e === "number" ? Number.isFinite(Number(t.trim())) && t.trim() !== "" : e === "date" ? /^\d{4}-\d{2}-\d{2}$/.test(t) : !0;
}
function kb({ value: e, editor: t, ariaLabel: n, onChange: r }) {
  const o = Ib(t, e);
  return o && t === "checkbox" ? /* @__PURE__ */ i.jsx(
    "input",
    {
      type: "checkbox",
      "aria-label": n,
      checked: e === "true" || e === "True",
      onChange: (s) => r(s.target.checked ? "true" : "false")
    }
  ) : o && (t === "number" || t === "date") ? /* @__PURE__ */ i.jsx(
    "input",
    {
      type: t,
      "aria-label": n,
      value: e,
      placeholder: "(empty)",
      onChange: (s) => r(s.target.value)
    }
  ) : /* @__PURE__ */ i.jsx(
    "input",
    {
      type: "text",
      "aria-label": n,
      value: e,
      placeholder: "(empty)",
      disabled: t === "none",
      onChange: (s) => r(s.target.value)
    }
  );
}
function Ab({ title: e, addLabel: t, emptyLabel: n, headers: r, isEmpty: o, onAdd: s, children: a }) {
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-properties-section-head", children: [
      /* @__PURE__ */ i.jsx("h3", { children: e }),
      /* @__PURE__ */ i.jsxs("button", { type: "button", className: "wf-properties-add", onClick: s, children: [
        /* @__PURE__ */ i.jsx(dn, { size: 14 }),
        " ",
        t
      ] })
    ] }),
    o ? /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: n }) : /* @__PURE__ */ i.jsxs("table", { className: "wf-properties-table", children: [
      /* @__PURE__ */ i.jsx("thead", { children: /* @__PURE__ */ i.jsxs("tr", { children: [
        r.map((c) => /* @__PURE__ */ i.jsx("th", { children: c }, c)),
        /* @__PURE__ */ i.jsx("th", { "aria-label": "Actions" })
      ] }) }),
      /* @__PURE__ */ i.jsx("tbody", { children: a })
    ] })
  ] });
}
function _b({ label: e, onRemove: t }) {
  return /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx("button", { type: "button", className: "wf-properties-remove", "aria-label": e, title: e, onClick: t, children: /* @__PURE__ */ i.jsx(er, { size: 14 }) }) });
}
function Db({ checked: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ i.jsx("input", { type: "checkbox", "aria-label": t, checked: e, onChange: (r) => n(r.target.checked) });
}
function js({
  items: e,
  typeOptions: t,
  storageOptions: n,
  editorForAlias: r,
  namePrefix: o,
  nameKeys: s,
  title: a,
  addLabel: c,
  emptyLabel: u,
  create: l,
  patch: f,
  columns: p,
  warnings: d,
  onChange: h
}) {
  const { add: g, update: N, remove: w } = Sb(e, h, {
    namePrefix: o,
    nameKeys: s,
    create: (y) => l(y, jb(t)),
    patch: f
  }), m = ["Name", "Type", "Collection", ...p.default ? ["Default"] : [], ...p.storage ? ["Storage"] : [], ...p.required ? ["Required"] : []], C = o.toLowerCase();
  return /* @__PURE__ */ i.jsx(
    Ab,
    {
      title: a,
      addLabel: c,
      emptyLabel: u,
      headers: m,
      isEmpty: e.length === 0,
      onAdd: g,
      children: e.map((y, b) => {
        const j = Gn(y, s), v = Ku(y), S = Gn(y, qu), A = S ? d?.get(S) : void 0, M = v.collectionKind === "Single" ? r(v.alias) : "text";
        return /* @__PURE__ */ i.jsxs("tr", { children: [
          /* @__PURE__ */ i.jsxs("td", { children: [
            /* @__PURE__ */ i.jsx("input", { type: "text", "aria-label": `${o} name`, value: j, onChange: (R) => N(b, { name: R.target.value }) }),
            A ? /* @__PURE__ */ i.jsx("span", { className: "wf-properties-warning", role: "note", title: A, children: A }) : null
          ] }),
          /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            vc,
            {
              ariaLabel: `${o} type`,
              value: v.alias,
              options: t,
              placeholder: "Type",
              onChange: (R) => N(b, { type: { alias: R, collectionKind: v.collectionKind } })
            }
          ) }),
          /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            Cb,
            {
              ariaLabel: `${o} collection kind`,
              value: v.collectionKind,
              onChange: (R) => N(b, { type: { alias: v.alias, collectionKind: R } })
            }
          ) }),
          p.default ? /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            kb,
            {
              ariaLabel: `${o} default value`,
              value: qw(y.default),
              editor: M,
              onChange: (R) => N(b, { default: Xw(R) })
            }
          ) }) : null,
          p.storage ? /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            vc,
            {
              ariaLabel: `${o} storage driver`,
              value: Gn(y, nv),
              options: n,
              placeholder: "—",
              allowEmpty: !0,
              onChange: (R) => N(b, { storageDriverType: R || null })
            }
          ) }) : null,
          p.required ? /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            Db,
            {
              ariaLabel: `${o} required`,
              checked: y.isRequired === !0,
              onChange: (R) => N(b, { isRequired: R })
            }
          ) }) : null,
          /* @__PURE__ */ i.jsx(_b, { label: `Remove ${C} ${j || b + 1}`, onRemove: () => w(b) })
        ] }, b);
      })
    }
  );
}
function vd({ items: e, typeOptions: t, storageOptions: n, editorForAlias: r, title: o = "Variables", addLabel: s = "Add variable", emptyLabel: a = "No variables defined.", warnings: c, onChange: u }) {
  return /* @__PURE__ */ i.jsx(
    js,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: r,
      namePrefix: "Variable",
      nameKeys: tv,
      title: o,
      addLabel: s,
      emptyLabel: a,
      create: (l, f) => Kw({ name: l, alias: f }),
      patch: (l, f) => Uw(l, f),
      columns: { default: !0, storage: !0 },
      warnings: c,
      onChange: u
    }
  );
}
function Tb({ items: e, typeOptions: t, storageOptions: n, editorForAlias: r, onChange: o }) {
  return /* @__PURE__ */ i.jsx(
    js,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: r,
      namePrefix: "Input",
      nameKeys: Xu,
      title: "Inputs",
      addLabel: "Add input",
      emptyLabel: "No inputs defined.",
      create: (s, a) => Yw({ name: s, alias: a }),
      patch: (s, a) => Zw(s, a),
      columns: { default: !1, storage: !0, required: !0 },
      onChange: o
    }
  );
}
function $b({ items: e, typeOptions: t, storageOptions: n, editorForAlias: r, onChange: o }) {
  return /* @__PURE__ */ i.jsx(
    js,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: r,
      namePrefix: "Output",
      nameKeys: Xu,
      title: "Outputs",
      addLabel: "Add output",
      emptyLabel: "No outputs defined.",
      create: (s, a) => Gw({ name: s, alias: a }),
      patch: (s, a) => Jw(s, a),
      columns: { default: !1, storage: !1 },
      onChange: o
    }
  );
}
function eo(e) {
  return (e ?? []).filter(xo);
}
function Mb({ context: e, variables: t, title: n, addLabel: r, emptyLabel: o, warnings: s, onChange: a }) {
  const { typeOptions: c, storageOptions: u, editorForAlias: l } = wd(e);
  return /* @__PURE__ */ i.jsx(
    vd,
    {
      items: eo(t),
      typeOptions: c,
      storageOptions: u,
      editorForAlias: l,
      title: n,
      addLabel: r,
      emptyLabel: o,
      warnings: s,
      onChange: a
    }
  );
}
function Pb({ definition: e, definitionId: t, onMetaChange: n }) {
  const r = !!n, [o, s] = U(e?.name ?? ""), [a, c] = U(e?.description ?? "");
  Q(() => {
    s(e?.name ?? "");
  }, [e?.name]), Q(() => {
    c(e?.description ?? "");
  }, [e?.description]);
  const u = () => {
    const f = o.trim();
    f && f !== (e?.name ?? "") ? n?.({ name: f }) : f || s(e?.name ?? "");
  }, l = () => {
    a !== (e?.description ?? "") && n?.({ description: a });
  };
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ i.jsx("h3", { children: "Information" }),
    /* @__PURE__ */ i.jsxs("dl", { className: "wf-properties-info", children: [
      /* @__PURE__ */ i.jsx("dt", { children: /* @__PURE__ */ i.jsx("label", { htmlFor: "wf-def-name", children: "Name" }) }),
      /* @__PURE__ */ i.jsx("dd", { children: r ? /* @__PURE__ */ i.jsx(
        "input",
        {
          id: "wf-def-name",
          type: "text",
          "aria-label": "Workflow name",
          value: o,
          onChange: (f) => s(f.target.value),
          onBlur: u
        }
      ) : e?.name ?? "—" }),
      /* @__PURE__ */ i.jsx("dt", { children: /* @__PURE__ */ i.jsx("label", { htmlFor: "wf-def-description", children: "Description" }) }),
      /* @__PURE__ */ i.jsx("dd", { children: r ? /* @__PURE__ */ i.jsx(
        "textarea",
        {
          id: "wf-def-description",
          "aria-label": "Workflow description",
          rows: 2,
          value: a,
          placeholder: "No description",
          onChange: (f) => c(f.target.value),
          onBlur: l
        }
      ) : e?.description?.trim() ? e.description : /* @__PURE__ */ i.jsx("span", { className: "wf-muted", children: "No description" }) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Definition ID" }),
      /* @__PURE__ */ i.jsx("dd", { children: /* @__PURE__ */ i.jsx("code", { children: t }) })
    ] })
  ] });
}
function Rb({ details: e, draft: t, context: n, onStateChange: r, onDefinitionMetaChange: o }) {
  const { typeOptions: s, storageOptions: a, editorForAlias: c } = wd(n), u = eo(t.state.variables), l = eo(t.state.inputs), f = eo(t.state.outputs), p = e?.versions ?? [];
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-properties-view", children: [
    /* @__PURE__ */ i.jsx(
      Pb,
      {
        definition: e?.definition,
        definitionId: t.definitionId,
        onMetaChange: o
      }
    ),
    /* @__PURE__ */ i.jsx(
      vd,
      {
        items: u,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (d) => r((h) => ({ ...h, variables: d }))
      }
    ),
    /* @__PURE__ */ i.jsx(
      Tb,
      {
        items: l,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (d) => r((h) => ({ ...h, inputs: d }))
      }
    ),
    /* @__PURE__ */ i.jsx(
      $b,
      {
        items: f,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (d) => r((h) => ({ ...h, outputs: d }))
      }
    ),
    /* @__PURE__ */ i.jsxs("section", { className: "wf-properties-section", children: [
      /* @__PURE__ */ i.jsx("h3", { children: "Versions" }),
      p.length === 0 ? /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "No published versions yet." }) : /* @__PURE__ */ i.jsx("ul", { className: "wf-properties-versions", children: p.map((d) => /* @__PURE__ */ i.jsxs("li", { children: [
        /* @__PURE__ */ i.jsxs("span", { className: "wf-properties-version-tag", children: [
          "v",
          d.version
        ] }),
        /* @__PURE__ */ i.jsx("time", { children: tt(d.createdAt) })
      ] }, d.id)) })
    ] })
  ] });
}
const Lb = "Expressions/UnresolvedVariable";
function zb(e) {
  return String(e.type ?? e.code ?? "");
}
function Vb(e) {
  return zb(e) === Lb;
}
function Ob(e) {
  const t = String(e ?? "").trim().split("/").filter(Boolean), [n, r, ...o] = t, s = n === "$workflow";
  return {
    nodeId: !n || s ? null : n,
    isWorkflowScope: s,
    bag: r === "inputs" || r === "outputs" || r === "variables" ? r : null,
    referenceKey: o.length > 0 ? o.join("/") : null
  };
}
function Hb(e) {
  return (e ?? []).filter(Vb).map((t) => ({
    error: t,
    path: Ob(t.path),
    message: t.message ?? "Variable reference is not visible from this activity's scope."
  }));
}
function Wb({ activities: e, activityCatalog: t, selectedEvidenceId: n = null, onSelectEvidence: r }) {
  const o = de(
    () => new Map(t.map((a) => [a.activityTypeKey, a])),
    [t]
  ), s = de(() => Bb(e), [e]);
  return s.length === 0 ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty wf-timeline-empty", children: "No activity executions recorded yet." }) : /* @__PURE__ */ i.jsx("ol", { className: "wf-timeline", "aria-label": "Execution timeline", children: s.map((a) => {
    const c = o.get(a.activityType), u = wo(c), l = c ? Ie(c) : So(a.activityType) ?? a.activityType, f = So(a.activityType) ?? a.activityType, p = Kb(a.startedAt ?? a.scheduledAt), d = xd(a.startedAt, a.completedAt);
    return /* @__PURE__ */ i.jsx("li", { children: /* @__PURE__ */ i.jsxs(
      "button",
      {
        type: "button",
        className: "wf-timeline-entry",
        "data-selected": a.activityExecutionId === n,
        onClick: () => r?.(a.activityExecutionId),
        children: [
          /* @__PURE__ */ i.jsx("span", { className: "wf-timeline-icon wf-activity-icon", "data-icon": u, "aria-hidden": "true", children: Ns(u) }),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-timeline-body", children: [
            /* @__PURE__ */ i.jsx("strong", { title: l, children: l }),
            /* @__PURE__ */ i.jsx("small", { title: f, children: f })
          ] }),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-timeline-meta", children: [
            p ? /* @__PURE__ */ i.jsx("time", { children: p }) : null,
            d ? /* @__PURE__ */ i.jsxs("small", { children: [
              "took ",
              d
            ] }) : null
          ] }),
          /* @__PURE__ */ i.jsx(Fb, { status: a.status })
        ]
      }
    ) }, a.activityExecutionId);
  }) });
}
function Fb({ status: e }) {
  const t = e.toLowerCase(), n = t === "completed" ? "OK" : e;
  return /* @__PURE__ */ i.jsx("span", { className: "wf-status-badge wf-timeline-pill", "data-status": t, children: n });
}
function Bb(e) {
  return e.map((t, n) => ({ activity: t, index: n })).sort((t, n) => bc(t.activity) - bc(n.activity) || t.index - n.index).map((t) => t.activity);
}
function bc(e) {
  const t = e.startedAt ?? e.scheduledAt, n = t ? Date.parse(t) : Number.NaN;
  return Number.isNaN(n) ? Number.MAX_SAFE_INTEGER : n;
}
function Kb(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? "" : t.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: !1 });
}
function Ub({ context: e }) {
  const t = Bv(e), n = Kv(e), r = Uv(e), o = t.data ?? null, s = n.data ?? null, a = t.isLoading || n.isLoading, c = r.isPending, [u, l] = U(() => dc(o)), [f, p] = U(""), [d, h] = U(null);
  Q(() => {
    l(dc(o));
  }, [o]);
  const g = de(() => E0(s), [s]), N = de(() => C0(s), [s]), w = s?.sets ?? [], m = de(() => {
    const _ = f.trim().toLowerCase();
    return _ ? g.filter(
      (V) => jo(V).toLowerCase().includes(_) || (V.activityTypeKey ?? "").toLowerCase().includes(_)
    ) : g;
  }, [g, f]), C = new Set(u.activityTypes), y = new Set(u.sets), b = g.filter((_) => Rt(_.state) === "BlockedByHostBaseline").length, j = g.filter((_) => Rt(_.state) === "HiddenByManagementSettings").length, v = r.error ?? t.error ?? n.error, S = v instanceof Error ? v.message : v ? "Activity availability could not be loaded." : null, A = (_) => l((V) => ({ ...V, mode: _ })), M = (_) => l((V) => ({ ...V, activityTypes: fc(V.activityTypes, _) })), R = (_) => l((V) => ({ ...V, sets: fc(V.sets, _) })), $ = () => {
    h(null), r.mutate(
      {
        scope: o?.scope ?? "host-default",
        mode: j0(u.mode),
        rules: { activityTypes: u.activityTypes, sets: u.sets }
      },
      { onSuccess: () => h("Activity availability saved.") }
    );
  };
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-page availability-page", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-page-header", children: [
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("div", { className: "wf-kicker", children: "Workflows" }),
        /* @__PURE__ */ i.jsxs("h2", { children: [
          /* @__PURE__ */ i.jsx(vf, { size: 18 }),
          " Activity availability"
        ] }),
        /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "Control which activities can be added to new workflows. Host baseline rules always take precedence." })
      ] }),
      /* @__PURE__ */ i.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ i.jsxs("button", { type: "button", className: "availability-save", onClick: $, disabled: a || c, children: [
        /* @__PURE__ */ i.jsx(Yc, { size: 15 }),
        c ? "Saving…" : "Save"
      ] }) })
    ] }),
    /* @__PURE__ */ i.jsxs("div", { className: "availability-body", children: [
      S && /* @__PURE__ */ i.jsx("div", { className: "availability-banner availability-banner-error", children: S }),
      d && !S && /* @__PURE__ */ i.jsx("div", { className: "availability-banner availability-banner-success", children: d }),
      /* @__PURE__ */ i.jsxs("div", { className: "availability-mode", role: "group", "aria-label": "Activity availability mode", children: [
        /* @__PURE__ */ i.jsxs("button", { type: "button", className: u.mode === "AllExcept" ? "active" : "", onClick: () => A("AllExcept"), disabled: a || c, children: [
          /* @__PURE__ */ i.jsx(Fs, { size: 15 }),
          /* @__PURE__ */ i.jsxs("span", { children: [
            /* @__PURE__ */ i.jsx("strong", { children: "All except" }),
            /* @__PURE__ */ i.jsx("em", { children: "Show everything except the selected activities" })
          ] })
        ] }),
        /* @__PURE__ */ i.jsxs("button", { type: "button", className: u.mode === "Only" ? "active" : "", onClick: () => A("Only"), disabled: a || c, children: [
          /* @__PURE__ */ i.jsx(Bs, { size: 15 }),
          /* @__PURE__ */ i.jsxs("span", { children: [
            /* @__PURE__ */ i.jsx("strong", { children: "Only" }),
            /* @__PURE__ */ i.jsx("em", { children: "Show only the selected activities" })
          ] })
        ] })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { className: "availability-counts", children: [
        /* @__PURE__ */ i.jsxs("span", { children: [
          /* @__PURE__ */ i.jsx(Bs, { size: 14 }),
          " ",
          b,
          " host blocked"
        ] }),
        /* @__PURE__ */ i.jsxs("span", { children: [
          /* @__PURE__ */ i.jsx(Fs, { size: 14 }),
          " ",
          j,
          " management hidden"
        ] }),
        /* @__PURE__ */ i.jsxs("span", { children: [
          /* @__PURE__ */ i.jsx(oo, { size: 14 }),
          " ",
          N.length,
          " unresolved"
        ] })
      ] }),
      w.length > 0 && /* @__PURE__ */ i.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ i.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ i.jsx(Yi, { size: 14 }),
          " Sets"
        ] }),
        /* @__PURE__ */ i.jsx("div", { className: "availability-set-list", children: w.map((_) => /* @__PURE__ */ i.jsxs("label", { className: "availability-set-option", children: [
          /* @__PURE__ */ i.jsx("input", { type: "checkbox", checked: y.has(_.name), disabled: a || c, onChange: () => R(_.name) }),
          /* @__PURE__ */ i.jsx("span", { children: _.name }),
          /* @__PURE__ */ i.jsx("code", { children: (_.activityTypeKeys ?? []).length })
        ] }, _.name)) })
      ] }),
      /* @__PURE__ */ i.jsxs("section", { className: "availability-section availability-section-grow", children: [
        /* @__PURE__ */ i.jsxs("div", { className: "availability-section-head", children: [
          /* @__PURE__ */ i.jsxs("h3", { className: "wf-section-label", children: [
            /* @__PURE__ */ i.jsx(bf, { size: 14 }),
            " Activities"
          ] }),
          /* @__PURE__ */ i.jsxs("div", { className: "wf-search availability-search", children: [
            /* @__PURE__ */ i.jsx(Co, { size: 14 }),
            /* @__PURE__ */ i.jsx("input", { type: "search", value: f, placeholder: "Filter activities…", onChange: (_) => p(_.target.value) })
          ] })
        ] }),
        /* @__PURE__ */ i.jsxs("div", { className: "availability-activity-list", children: [
          a && g.length === 0 && /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "Loading availability…" }),
          !a && g.length === 0 && /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "No availability diagnostics reported." }),
          !a && g.length > 0 && m.length === 0 && /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "No activities match the filter." }),
          m.map((_) => {
            const E = Rt(_.state) === "BlockedByHostBaseline", D = _.activityTypeKey ?? _.activityDefinitionId ?? "";
            return /* @__PURE__ */ i.jsxs("label", { className: `availability-activity-option ${E ? "disabled" : ""}`, children: [
              /* @__PURE__ */ i.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: C.has(D),
                  disabled: a || c || E,
                  onChange: () => M(D)
                }
              ),
              /* @__PURE__ */ i.jsxs("span", { className: "availability-activity-main", children: [
                /* @__PURE__ */ i.jsx("strong", { children: jo(_) }),
                /* @__PURE__ */ i.jsx("code", { children: _.activityTypeKey })
              ] }),
              /* @__PURE__ */ i.jsx("em", { className: `availability-state ${v0(_.state)}`, children: No(_.state) })
            ] }, D);
          })
        ] })
      ] }),
      N.length > 0 && /* @__PURE__ */ i.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ i.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ i.jsx(oo, { size: 14 }),
          " Unresolved references"
        ] }),
        /* @__PURE__ */ i.jsx("div", { className: "availability-unresolved-list", children: N.map((_) => /* @__PURE__ */ i.jsxs("span", { children: [
          /* @__PURE__ */ i.jsx("strong", { children: _.referenceName }),
          /* @__PURE__ */ i.jsx("em", { children: No(_.state) })
        ] }, `${_.layer}-${_.referenceKind}-${_.referenceName}`)) })
      ] })
    ] })
  ] });
}
const Nc = "elsa-studio:apply-workflow-graph-operation-batch", jc = "elsa-studio:undo-workflow-graph-operation-batch", Xb = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function qb(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const r = nN(e), o = Nd(r.state.rootActivity), s = /* @__PURE__ */ new Map(), a = /* @__PURE__ */ new Map(), c = [];
  for (const u of t.operations) {
    const l = tN(u.kind), f = u.parameters ?? {};
    if (l === "add-activity") {
      const p = Ve(f.activityId) ?? u.temporaryReferences?.[0], d = eN(p ?? Ve(f.displayName) ?? Ve(f.activityType) ?? "weaver-activity", o), h = Yb(u, d, n);
      a.set(d, h), c.push(d), p && s.set(p, d), r.state.rootActivity && Zb(r.state.rootActivity, h);
      const g = jt(f.position) ? Bi(f.position, { x: 280, y: 160 }) : null;
      g && (r.layout = Sc(r.layout, d, g));
      continue;
    }
    if (l === "set-root") {
      const p = bi(r, f.activityId, s, a);
      if (!p) throw new Error("Weaver batch referenced an unknown root activity.");
      r.state.rootActivity = p;
      continue;
    }
    if (l === "set-designer-position") {
      const p = Wt(f.activityId, s);
      if (!p || !Ss(r.state.rootActivity, p)) throw new Error("Weaver batch referenced an unknown activity position.");
      r.layout = Sc(r.layout, p, Bi(f, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const p = bi(r, f.activityId, s, a);
      if (!p) throw new Error("Weaver batch referenced an unknown activity property target.");
      Qb(p, Ve(f.propertyName) ?? "Value", f.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const p = bi(r, f.activityId, s, a);
      if (!p) throw new Error("Weaver batch referenced an unknown activity update target.");
      const d = jt(f.patch) ? f.patch : f;
      Object.assign(p, d);
      continue;
    }
    if (l === "remove-activity") {
      const p = Wt(f.activityId, s);
      if (!p) throw new Error("Weaver batch referenced an unknown activity remove target.");
      r.state.rootActivity = bd(r.state.rootActivity, p), r.layout = r.layout.filter((d) => d.nodeId !== p);
      continue;
    }
    if (l === "connect-activities") {
      Gb(r, f, s);
      continue;
    }
    if (l === "disconnect-activities") {
      Jb(r, f, s);
      continue;
    }
    throw new Error(`Weaver batch operation '${String(u.kind || "unknown")}' is not supported by this designer apply path.`);
  }
  if (!r.state.rootActivity) throw new Error("Weaver batch did not produce a root activity.");
  return r.sourceVersionId = null, {
    draft: r,
    appliedCount: t.operations.length,
    finalActivityIds: c,
    temporaryReferences: Object.fromEntries(s),
    summary: `Applied ${t.operations.length} workflow operation${t.operations.length === 1 ? "" : "s"} to the working draft.`
  };
}
function Yb(e, t, n) {
  const r = e.parameters ?? {}, o = Ve(r.activityVersionId) ?? Ve(r.activityType) ?? "Elsa.Workflows.Activity", s = n.find((a) => a.activityVersionId === o || a.activityTypeKey === o || a.displayName === Ve(r.displayName));
  return s ? zi(s, t) : {
    nodeId: t,
    activityVersionId: s?.activityVersionId ?? o,
    inputs: [],
    outputs: [],
    ...Ve(r.displayName) ? { displayName: Ve(r.displayName) } : {},
    designer: { position: Bi(r.position, { x: 280, y: 160 }) }
  };
}
function Zb(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = Es(e);
  n && !n.some((r) => r.nodeId === t.nodeId) && n.push(t);
}
function Gb(e, t, n) {
  const r = e.state.rootActivity;
  if (!r?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const o = Wt(t.sourceActivityId ?? t.sourceId ?? t.from, n), s = Wt(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!o || !s) throw new Error("Weaver batch connection is missing source or target activity.");
  const a = r.structure.payload, c = Array.isArray(a.connections) ? a.connections : [], u = Ve(t.connectionId) ?? `flow-${o}-${s}`;
  a.connections = [
    ...c.filter((l) => !jt(l) || l.id !== u),
    {
      id: u,
      source: { nodeId: o, port: Ve(t.outcome) ?? Ve(t.sourcePort) ?? "Done" },
      target: { nodeId: s }
    }
  ];
}
function Jb(e, t, n) {
  const r = e.state.rootActivity, o = r?.structure?.payload.connections;
  if (!Array.isArray(o)) return;
  const s = Ve(t.connectionId), a = Wt(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = Wt(t.targetActivityId ?? t.targetId ?? t.to, n);
  r.structure.payload.connections = o.filter((u) => {
    if (!jt(u)) return !0;
    if (s && u.id === s) return !1;
    const l = jt(u.source) ? u.source.nodeId : void 0, f = jt(u.target) ? u.target.nodeId : void 0;
    return l !== a || f !== c;
  });
}
function Qb(e, t, n) {
  const r = jt(n);
  e[ud(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: r ? "Object" : "Literal", value: n }
  };
}
function bi(e, t, n, r) {
  const o = Wt(t, n);
  return o ? Ss(e.state.rootActivity, o) ?? r.get(o) ?? null : null;
}
function Wt(e, t) {
  const n = Ve(e);
  return n ? t.get(n) ?? n : null;
}
function Ss(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of jd(e)) {
    const r = Ss(n, t);
    if (r) return r;
  }
  return null;
}
function bd(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = Es(e);
  if (n) {
    const r = n.map((o) => bd(o, t)).filter((o) => !!o);
    n.splice(0, n.length, ...r);
  }
  return e;
}
function Nd(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of jd(e)) Nd(n, t);
  return t;
}
function jd(e) {
  return Es(e) ?? [];
}
function Es(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function Sc(e, t, n) {
  return [
    ...e.filter((r) => r.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function Bi(e, t) {
  const n = jt(e) ? e : {}, r = Number(n.x), o = Number(n.y);
  return {
    x: Number.isFinite(r) ? Math.max(40, Math.round(r)) : t.x,
    y: Number.isFinite(o) ? Math.max(40, Math.round(o)) : t.y
  };
}
function eN(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let r = n, o = 2;
  for (; t.has(r); )
    r = `${n}-${o}`, o += 1;
  return t.add(r), r;
}
function tN(e) {
  return typeof e == "number" ? Xb[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Ve(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function nN(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function jt(e) {
  return typeof e == "object" && e !== null;
}
function Cs({ rows: e = 5 }) {
  return /* @__PURE__ */ i.jsx("div", { className: "wf-grid", "aria-busy": "true", "aria-label": "Loading", children: Array.from({ length: e }).map((t, n) => /* @__PURE__ */ i.jsx("div", { className: "wf-skeleton wf-skeleton-row", style: { width: `${90 - n % 3 * 12}%` } }, n)) });
}
function Is({ icon: e, title: t, description: n, action: r }) {
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-empty-state", role: "status", children: [
    /* @__PURE__ */ i.jsx("div", { className: "wf-empty-state-icon", "aria-hidden": !0, children: e ?? /* @__PURE__ */ i.jsx(Eo, { size: 22 }) }),
    /* @__PURE__ */ i.jsx("h3", { children: t }),
    n ? /* @__PURE__ */ i.jsx("p", { children: n }) : null,
    r ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty-state-action", children: r }) : null
  ] });
}
function vr({ message: e, title: t = "Something went wrong" }) {
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-error-card", role: "alert", children: [
    /* @__PURE__ */ i.jsx(St, { size: 18 }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-error-card-body", children: [
      /* @__PURE__ */ i.jsx("strong", { children: t }),
      /* @__PURE__ */ i.jsx("span", { children: e || "Please try again, or check that the Elsa server is reachable." })
    ] })
  ] });
}
const Sd = { workflowActivity: XN }, Ed = { workflow: YN }, Ec = "application/x-elsa-activity-version-id", rN = 6, oN = 1200, iN = 250, sN = [10, 25, 50], aN = 10, Cc = "elsa-studio-workflow-palette-width", Ic = "elsa-studio-workflow-inspector-width", kc = "elsa-studio-workflow-palette-collapsed", Ac = "elsa-studio-workflow-inspector-collapsed", Cd = "elsa-studio-workflow-side-panel-maximized", On = 180, Hn = 460, cN = 260, Wn = 260, Fn = 560, lN = 320, _c = 42, Br = 16, uN = [
  {
    value: "flowchart",
    label: "Flowchart",
    hint: "Free-form activities with explicit connections between ports."
  },
  {
    value: "sequence",
    label: "Sequence",
    hint: "Activities run top-to-bottom in the order you place them."
  }
], Id = it.createContext(null), kd = it.createContext(null);
let Ki;
function dj(e) {
  Ki = e.dialogs, e.featureAreas.add({
    id: "workflows",
    title: "Workflows",
    description: "Design, publish and run workflow definitions and inspect runs.",
    navGroup: "Workspace",
    ownedPaths: ["/workflows"],
    required: !0,
    defaultEnabled: !0,
    order: 20,
    nav: {
      title: "Workflows",
      path: "/workflows/definitions",
      iconColor: "#0ea5e9",
      items: [
        { title: "Definitions", path: "/workflows/definitions", iconColor: "#0ea5e9" },
        { title: "Executables", path: "/workflows/executables", iconColor: "#0ea5e9" },
        { title: "Runs", path: "/workflows/instances", iconColor: "#0ea5e9" },
        { title: "Activity Availability", path: "/workflows/activity-availability", iconColor: "#0ea5e9" }
      ]
    },
    routes: [
      {
        id: "workflows-definitions",
        path: "/workflows/definitions",
        label: "Workflow definitions",
        component: () => /* @__PURE__ */ i.jsx(dN, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list() })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ i.jsx(fN, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => /* @__PURE__ */ i.jsx(pN, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => /* @__PURE__ */ i.jsx(hN, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => /* @__PURE__ */ i.jsx(Ub, { context: e.backend })
      }
    ]
  });
}
function dN({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: r,
  workflowDesignerPanels: o
}) {
  const [s, a] = U(Dc);
  Q(() => {
    const u = () => a(Dc());
    return window.addEventListener("popstate", u), () => window.removeEventListener("popstate", u);
  }, []);
  const c = (u) => {
    const l = u ? `/workflows/definitions?definition=${encodeURIComponent(u)}` : "/workflows/definitions";
    window.history.pushState({}, "", l), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return s ? /* @__PURE__ */ i.jsx(UN, { context: e, definitionId: s, ai: t, propertyEditors: n, expressionEditors: r, workflowDesignerPanels: o, onBack: () => c(null) }) : /* @__PURE__ */ i.jsx(Ho, { title: "Definitions", children: /* @__PURE__ */ i.jsx(yN, { context: e, ai: t, onOpen: c }) });
}
function fN({ context: e, ai: t }) {
  const [n, r] = U(Tc);
  Q(() => {
    const s = () => r(Tc());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const o = le((s) => {
    const a = s?.trim() ?? "", c = new URL(window.location.href);
    a ? c.searchParams.set("definition", a) : c.searchParams.delete("definition"), r(a || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ i.jsx(Ho, { title: "Executables", children: /* @__PURE__ */ i.jsx(xN, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: o }) });
}
function pN({ context: e, ai: t }) {
  return /* @__PURE__ */ i.jsx(Ho, { title: "Runs", children: /* @__PURE__ */ i.jsx(bN, { context: e, ai: t }) });
}
function hN({ context: e, ai: t }) {
  const n = gN();
  return /* @__PURE__ */ i.jsx(Ho, { title: "Run", children: /* @__PURE__ */ i.jsx(NN, { context: e, ai: t, workflowExecutionId: n }) });
}
function Ho({ title: e, children: t }) {
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ i.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ i.jsxs("div", { children: [
      /* @__PURE__ */ i.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ i.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function Dc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Tc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function gN() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function yN({ context: e, ai: t, onOpen: n }) {
  const [r, o] = U(""), [s, a] = U("active"), [c, u] = U(1), [l, f] = U(aN), [p, d] = U("loading"), [h, g] = U(""), [N, w] = U(""), [m, C] = U([]), [y, b] = U(0), [j, v] = U(() => /* @__PURE__ */ new Set()), [S, A] = U(null), [M, R] = U(!1), [$, _] = U([]), [V, E] = U("idle"), D = ie(null), k = de(() => m.map((W) => W.id), [m]), T = Ft(t, "weaver.workflows.suggest-create-metadata"), L = Ft(t, "weaver.workflows.explain-definition"), P = k.filter((W) => j.has(W)).length, K = k.length > 0 && P === k.length, F = le(async () => {
    d("loading"), g("");
    try {
      const W = await Xv(e, { search: r, state: s, page: c, pageSize: l }), ee = typeof W.totalCount == "number", ye = W.totalCount ?? W.definitions.length, we = Dd(ye, l);
      if (ye > 0 && c > we) {
        u(we);
        return;
      }
      C(ee ? W.definitions : _N(W.definitions, c, l)), b(ye), d("ready");
    } catch (W) {
      g(W instanceof Error ? W.message : String(W)), d("failed");
    }
  }, [e, r, s, c, l]);
  Q(() => {
    F();
  }, [F]), Q(() => {
    D.current && (D.current.indeterminate = P > 0 && !K);
  }, [K, P]);
  const B = le(async () => {
    if (!(V === "loading" || V === "ready")) {
      E("loading");
      try {
        const W = await ws(e);
        _(W.activities ?? []), E("ready");
      } catch (W) {
        E("failed"), g(W instanceof Error ? W.message : String(W));
      }
    }
  }, [V, e]), q = () => {
    g(""), w(""), A({ name: "", description: "", rootKind: "flowchart" }), B();
  }, J = async () => {
    if (S?.name.trim()) {
      R(!0), g(""), w("");
      try {
        const W = await Jv(e, {
          name: S.name.trim(),
          description: S.description.trim() || null,
          rootKind: S.rootKind,
          rootActivityVersionId: TN(S, $)
        });
        A(null), n(W.definition.id);
      } catch (W) {
        g(W instanceof Error ? W.message : String(W));
      } finally {
        R(!1);
      }
    }
  }, X = (W) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(W)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, ce = async () => {
    if (m.length === 1 && c > 1) {
      u(c - 1);
      return;
    }
    await F();
  }, G = () => v(/* @__PURE__ */ new Set()), O = (W, ee) => {
    v((ye) => {
      const we = new Set(ye);
      return ee ? we.add(W) : we.delete(W), we;
    });
  }, Y = (W) => {
    v((ee) => {
      const ye = new Set(ee);
      for (const we of k)
        W ? ye.add(we) : ye.delete(we);
      return ye;
    });
  }, se = (W) => {
    a(W), u(1), G();
  }, ae = (W) => {
    o(W), u(1), G();
  }, te = async (W) => {
    if (await Ki.confirm({ message: `Delete workflow definition "${W.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" })) {
      w(""), g("");
      try {
        await Qv(e, W.id), O(W.id, !1), w(`Deleted ${W.name}`), await ce();
      } catch (ee) {
        g(ee instanceof Error ? ee.message : String(ee));
      }
    }
  }, re = async (W) => {
    w(""), g("");
    try {
      await e0(e, W.id), O(W.id, !1), w(`Restored ${W.name}`), await ce();
    } catch (ee) {
      g(ee instanceof Error ? ee.message : String(ee));
    }
  }, fe = async (W) => {
    if (await Ki.confirm({ message: `Permanently delete workflow definition "${W.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) {
      w(""), g("");
      try {
        await t0(e, W.id), O(W.id, !1), w(`Permanently deleted ${W.name}`), await ce();
      } catch (ee) {
        g(ee instanceof Error ? ee.message : String(ee));
      }
    }
  };
  return /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ i.jsx("button", { type: "button", className: s === "active" ? "active" : "", "aria-selected": s === "active", onClick: () => se("active"), children: "Active" }),
        /* @__PURE__ */ i.jsx("button", { type: "button", className: s === "deleted" ? "active" : "", "aria-selected": s === "deleted", onClick: () => se("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ i.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ i.jsx(Co, { size: 15 }),
        /* @__PURE__ */ i.jsx("input", { value: r, onChange: (W) => ae(W.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => {
        F();
      }, children: "Refresh" }),
      /* @__PURE__ */ i.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ i.jsxs("button", { type: "button", title: "Create workflow", onClick: q, children: [
        /* @__PURE__ */ i.jsx(dn, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    p === "failed" ? /* @__PURE__ */ i.jsx(vr, { message: h, title: "Couldn't load workflow definitions" }) : null,
    p !== "failed" && h ? /* @__PURE__ */ i.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ i.jsx(St, { size: 16 }),
      " ",
      h
    ] }) : null,
    N ? /* @__PURE__ */ i.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ i.jsx(bn, { size: 14 }),
      " ",
      N
    ] }) : null,
    j.size > 0 ? /* @__PURE__ */ i.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ i.jsxs("span", { children: [
        j.size,
        " selected"
      ] }),
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: G, children: "Clear selection" })
    ] }) : null,
    p === "loading" ? /* @__PURE__ */ i.jsx(Cs, {}) : null,
    p === "ready" && m.length === 0 ? /* @__PURE__ */ i.jsx(
      Is,
      {
        icon: /* @__PURE__ */ i.jsx(Zc, { size: 22 }),
        title: `No ${s} workflow definitions`,
        description: "Create a workflow to start designing automation, or adjust your filters to see more.",
        action: /* @__PURE__ */ i.jsxs("button", { type: "button", className: "wf-link-button", onClick: q, children: [
          /* @__PURE__ */ i.jsx(dn, { size: 15 }),
          " Create workflow"
        ] })
      }
    ) : null,
    p === "ready" && m.length > 0 ? /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ i.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ i.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ i.jsx(
            "input",
            {
              ref: D,
              type: "checkbox",
              checked: K,
              onChange: (W) => Y(W.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ i.jsx("span", { children: "Name" }),
          /* @__PURE__ */ i.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ i.jsx("span", { children: s === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ i.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ i.jsx("span", { children: "Actions" })
        ] }),
        m.map((W) => /* @__PURE__ */ i.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${W.name}`,
            "aria-selected": j.has(W.id),
            tabIndex: 0,
            onClick: () => n(W.id),
            onKeyDown: (ee) => {
              ee.currentTarget === ee.target && (ee.key !== "Enter" && ee.key !== " " || (ee.preventDefault(), n(W.id)));
            },
            children: [
              /* @__PURE__ */ i.jsx("label", { className: "wf-row-select", onClick: (ee) => ee.stopPropagation(), children: /* @__PURE__ */ i.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: j.has(W.id),
                  onChange: (ee) => O(W.id, ee.target.checked),
                  "aria-label": `Select workflow definition ${W.name}`
                }
              ) }),
              /* @__PURE__ */ i.jsxs("span", { children: [
                /* @__PURE__ */ i.jsx("strong", { children: W.name }),
                /* @__PURE__ */ i.jsx("small", { children: W.description || W.id })
              ] }),
              /* @__PURE__ */ i.jsx("span", { children: W.latestVersion ?? "No version" }),
              /* @__PURE__ */ i.jsx("span", { children: s === "deleted" ? tt(W.deletedAt) : W.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ i.jsx("span", { children: tt(W.lastModifiedAt) }),
              /* @__PURE__ */ i.jsx("span", { className: "wf-row-actions", onClick: (ee) => ee.stopPropagation(), children: s === "active" ? /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
                /* @__PURE__ */ i.jsx("button", { type: "button", onClick: (ee) => {
                  ee.stopPropagation(), n(W.id);
                }, children: "Open" }),
                /* @__PURE__ */ i.jsx("button", { type: "button", onClick: (ee) => {
                  ee.stopPropagation(), X(W.id);
                }, children: "Artifacts" }),
                L ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => Bt(t, L, W), children: [
                  /* @__PURE__ */ i.jsx(pt, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ i.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  te(W);
                }, children: [
                  /* @__PURE__ */ i.jsx(er, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
                /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => {
                  re(W);
                }, children: [
                  /* @__PURE__ */ i.jsx(Zi, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ i.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  fe(W);
                }, children: [
                  /* @__PURE__ */ i.jsx(er, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          W.id
        ))
      ] }),
      /* @__PURE__ */ i.jsx(
        AN,
        {
          page: c,
          pageSize: l,
          totalCount: y,
          onPageChange: u,
          onPageSizeChange: (W) => {
            f(W), u(1);
          }
        }
      )
    ] }) : null,
    S ? /* @__PURE__ */ i.jsx(
      mN,
      {
        draft: S,
        creating: M,
        ai: t,
        suggestMetadataAction: T,
        onChange: (W) => A(W),
        onClose: () => A(null),
        onSubmit: J
      }
    ) : null
  ] });
}
function mN({ draft: e, creating: t, ai: n, suggestMetadataAction: r, onChange: o, onClose: s, onSubmit: a }) {
  const [c, u] = U(!1), [l, f] = U(""), [p, d] = U(!1), [h, g] = U(null), [N, w] = U(null), m = ie(null), C = ie(e);
  C.current = e;
  const y = ie(o);
  y.current = o;
  const b = le((v) => {
    const S = { ...C.current };
    v.name && (S.name = v.name), v.description && (S.description = v.description), y.current(S), g(null), w(null);
  }, []);
  Q(() => {
    if (r)
      return n.onPromptResult((v) => {
        if (v.requestId !== m.current) return;
        if (m.current = null, d(!1), v.status !== "completed") {
          w(v.status === "cancelled" ? "Weaver needs more detail — continue in the assistant panel." : "Weaver couldn't generate a suggestion. Try again or fill the fields manually.");
          return;
        }
        const S = DN(v.text);
        if (!S) {
          w("Couldn't read a suggestion from Weaver's reply. See the assistant panel.");
          return;
        }
        v.autoApply ? b(S) : g(S);
      });
  }, [n, r, b]);
  const j = () => {
    if (!r) return;
    const v = r.createPrompt({ draft: C.current, intent: l });
    if (!v) return;
    const S = `wf-suggest-${Date.now()}-${Math.round(Math.random() * 1e6)}`;
    m.current = S, d(!0), g(null), w(null), n.dispatchPrompt({ ...v, requestId: S });
  };
  return /* @__PURE__ */ i.jsx("div", { className: "wf-dialog-backdrop", role: "presentation", children: /* @__PURE__ */ i.jsx("section", { className: "wf-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "workflow-create-title", children: /* @__PURE__ */ i.jsxs(
    "form",
    {
      onSubmit: (v) => {
        v.preventDefault(), a();
      },
      children: [
        /* @__PURE__ */ i.jsxs("div", { className: "wf-dialog-heading", children: [
          /* @__PURE__ */ i.jsx("h3", { id: "workflow-create-title", children: "Create Workflow" }),
          r ? /* @__PURE__ */ i.jsxs(
            "button",
            {
              type: "button",
              className: "wf-ai-action",
              "aria-expanded": c,
              onClick: () => u((v) => !v),
              title: r.description ?? r.label,
              children: [
                /* @__PURE__ */ i.jsx(pt, { size: 13 }),
                " ",
                r.label
              ]
            }
          ) : null
        ] }),
        r && c ? /* @__PURE__ */ i.jsxs("div", { className: "wf-ai-suggest", role: "group", "aria-label": "Suggest name and description", children: [
          /* @__PURE__ */ i.jsxs("label", { className: "wf-form-field", children: [
            /* @__PURE__ */ i.jsx("span", { children: "What should this workflow do?" }),
            /* @__PURE__ */ i.jsx(
              "textarea",
              {
                autoFocus: !0,
                "aria-label": "Workflow intent",
                rows: 2,
                placeholder: "e.g. Route incoming support tickets to the right team and notify on SLA breach (optional)",
                value: l,
                disabled: p,
                onChange: (v) => f(v.target.value),
                onKeyDown: (v) => {
                  (v.metaKey || v.ctrlKey) && v.key === "Enter" && (v.preventDefault(), j());
                }
              }
            )
          ] }),
          /* @__PURE__ */ i.jsx("div", { className: "wf-ai-suggest-actions", children: /* @__PURE__ */ i.jsxs("button", { type: "button", className: "wf-ai-action", onClick: j, disabled: p, children: [
            /* @__PURE__ */ i.jsx(pt, { size: 13 }),
            " ",
            p ? "Generating…" : "Generate"
          ] }) }),
          N ? /* @__PURE__ */ i.jsx("p", { className: "wf-ai-suggest-error", role: "alert", children: N }) : null,
          h ? /* @__PURE__ */ i.jsxs("div", { className: "wf-ai-suggest-preview", children: [
            h.name ? /* @__PURE__ */ i.jsx("p", { children: /* @__PURE__ */ i.jsx("strong", { children: h.name }) }) : null,
            h.description ? /* @__PURE__ */ i.jsx("p", { children: h.description }) : null,
            /* @__PURE__ */ i.jsxs("div", { className: "wf-ai-suggest-actions", children: [
              /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => b(h), children: "Apply" }),
              /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => g(null), children: "Dismiss" })
            ] })
          ] }) : null
        ] }) : null,
        /* @__PURE__ */ i.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ i.jsx("span", { children: "Display name" }),
          /* @__PURE__ */ i.jsx(
            "input",
            {
              autoFocus: !0,
              "aria-label": "Display name",
              value: e.name,
              onChange: (v) => o({ ...e, name: v.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ i.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ i.jsx("span", { children: "Description" }),
          /* @__PURE__ */ i.jsx(
            "textarea",
            {
              "aria-label": "Description",
              rows: 3,
              value: e.description,
              onChange: (v) => o({ ...e, description: v.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ i.jsxs("fieldset", { className: "wf-form-field wf-root-field", children: [
          /* @__PURE__ */ i.jsx("legend", { children: "Root activity" }),
          /* @__PURE__ */ i.jsx("div", { className: "wf-root-cards", role: "radiogroup", "aria-label": "Root activity", children: uN.map((v) => {
            const S = e.rootKind === v.value;
            return /* @__PURE__ */ i.jsxs("label", { className: "wf-root-card", "data-checked": S || void 0, children: [
              /* @__PURE__ */ i.jsx(
                "input",
                {
                  type: "radio",
                  name: "wf-root-kind",
                  "aria-label": v.label,
                  value: v.value,
                  checked: S,
                  onChange: () => o({ ...e, rootKind: v.value, rootActivityVersionId: null })
                }
              ),
              /* @__PURE__ */ i.jsx("span", { className: "wf-root-card-title", children: v.label }),
              /* @__PURE__ */ i.jsx("span", { className: "wf-root-card-hint", children: v.hint })
            ] }, v.value);
          }) })
        ] }),
        /* @__PURE__ */ i.jsxs("div", { className: "wf-dialog-actions", children: [
          /* @__PURE__ */ i.jsx("button", { type: "button", onClick: s, disabled: t, children: "Cancel" }),
          /* @__PURE__ */ i.jsx("button", { type: "submit", disabled: t || !e.name.trim(), children: t ? "Creating..." : "Create" })
        ] })
      ]
    }
  ) }) });
}
function xN({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: r }) {
  const [o, s] = U("loading"), [a, c] = U(""), [u, l] = U(""), [f, p] = U(null), [d, h] = U([]), g = n?.trim().toLowerCase() ?? "", N = de(
    () => g ? d.filter((S) => zN(S, g)) : d,
    [g, d]
  ), w = de(
    () => Array.from(new Set(d.flatMap((S) => [
      S.definitionId,
      S.definitionVersionId,
      S.sourceId
    ]).filter((S) => !!S))).sort((S, A) => S.localeCompare(A)),
    [d]
  ), m = Ft(t, "weaver.workflows.explain-executable"), C = le(async () => {
    s("loading"), c("");
    try {
      h(await cd(e)), s("ready");
    } catch (S) {
      c(S instanceof Error ? S.message : String(S)), s("failed");
    }
  }, [e]);
  Q(() => {
    C();
  }, [C]);
  const y = async (S) => {
    l(""), p(null), c("");
    try {
      const A = await ad(e, S.artifactId), M = Md(A);
      p({ artifactId: S.artifactId, workflowExecutionId: M }), l(`Started ${S.artifactId}`);
    } catch (A) {
      c(A instanceof Error ? A.message : String(A));
    }
  }, b = (S) => {
    m && Bt(t, m, S) && (c(""), p(null), l(`Sent ${S.artifactId} to Weaver`));
  }, j = (S) => {
    c(""), p(null), l(`Copied ${S}`);
  }, v = (S) => {
    l(""), p(null), c(`Could not copy ${S}.`);
  };
  return /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => {
        C();
      }, children: "Refresh" }),
      /* @__PURE__ */ i.jsxs("label", { className: "wf-search wf-executable-definition-filter", children: [
        /* @__PURE__ */ i.jsx(Co, { size: 14 }),
        /* @__PURE__ */ i.jsx(
          "input",
          {
            "aria-label": "Filter executables by workflow definition",
            list: "wf-executable-definition-options",
            placeholder: "Filter by definition ID",
            value: n ?? "",
            onChange: (S) => r(S.currentTarget.value || null)
          }
        )
      ] }),
      /* @__PURE__ */ i.jsx("datalist", { id: "wf-executable-definition-options", children: w.map((S) => /* @__PURE__ */ i.jsx("option", { value: S }, S)) }),
      n ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => r(null), children: [
        /* @__PURE__ */ i.jsx(Xc, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    o === "failed" ? /* @__PURE__ */ i.jsx(vr, { message: a }) : null,
    u ? /* @__PURE__ */ i.jsx(Ad, { status: u, run: f }) : null,
    o === "loading" ? /* @__PURE__ */ i.jsx(Cs, {}) : null,
    o === "ready" && N.length === 0 ? /* @__PURE__ */ i.jsx(
      Is,
      {
        icon: /* @__PURE__ */ i.jsx(fn, { size: 22 }),
        title: "No workflow executables",
        description: n ? "No executables match this definition filter." : "Publish a workflow definition to make it executable."
      }
    ) : null,
    o === "ready" && N.length > 0 ? /* @__PURE__ */ i.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ i.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ i.jsx("span", { children: "Version" }),
        /* @__PURE__ */ i.jsx("span", { children: "Source" }),
        /* @__PURE__ */ i.jsx("span", { children: "Root" }),
        /* @__PURE__ */ i.jsx("span", { children: "Published" }),
        /* @__PURE__ */ i.jsx("span", { children: "Actions" })
      ] }),
      N.map((S) => /* @__PURE__ */ i.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ i.jsxs("span", { className: "wf-artifact-cell", children: [
          /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line", children: [
            /* @__PURE__ */ i.jsx("strong", { title: S.artifactId, children: S.artifactId }),
            /* @__PURE__ */ i.jsx(un, { value: S.artifactId, ariaLabel: `Copy artifact ID ${S.artifactId}`, copiedLabel: "artifact ID", onCopied: j, onCopyFailed: v })
          ] }),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
            /* @__PURE__ */ i.jsx("small", { title: S.artifactHash, children: S.artifactHash }),
            /* @__PURE__ */ i.jsx(un, { value: S.artifactHash, ariaLabel: `Copy artifact hash ${S.artifactHash}`, copiedLabel: "artifact hash", onCopied: j, onCopyFailed: v })
          ] })
        ] }),
        /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line wf-version-cell", children: [
          /* @__PURE__ */ i.jsx("span", { children: S.artifactVersion }),
          /* @__PURE__ */ i.jsx(un, { value: S.artifactVersion, ariaLabel: `Copy artifact version ${S.artifactVersion}`, copiedLabel: "artifact version", onCopied: j, onCopyFailed: v })
        ] }),
        /* @__PURE__ */ i.jsx(wN, { executable: S, onCopied: j, onCopyFailed: v }),
        /* @__PURE__ */ i.jsx("span", { children: Td(S) }),
        /* @__PURE__ */ i.jsx("span", { children: tt(S.publishedAt ?? S.createdAt) }),
        /* @__PURE__ */ i.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => {
            y(S);
          }, children: [
            /* @__PURE__ */ i.jsx(fn, { size: 13 }),
            " Run"
          ] }),
          m ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => b(S), children: [
            /* @__PURE__ */ i.jsx(pt, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, S.artifactId))
    ] }) : null
  ] });
}
function wN({ executable: e, onCopied: t, onCopyFailed: n }) {
  const r = e.sourceId || e.definitionVersionId || e.definitionId, o = e.sourceVersion;
  return /* @__PURE__ */ i.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ i.jsx("span", { className: "wf-source-kind", children: $d(e.sourceKind) }),
    r ? /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line", children: [
      /* @__PURE__ */ i.jsx("code", { title: r, children: r }),
      /* @__PURE__ */ i.jsx(un, { value: r, ariaLabel: `Copy source ID ${r}`, copiedLabel: "source ID", onCopied: t, onCopyFailed: n })
    ] }) : null,
    o ? /* @__PURE__ */ i.jsxs("small", { children: [
      "Version ",
      o
    ] }) : null
  ] });
}
function Ad({ status: e, run: t, compact: n = !1 }) {
  const r = () => {
    t?.workflowExecutionId && (window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(t.workflowExecutionId)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  };
  return /* @__PURE__ */ i.jsxs("div", { className: `wf-status-line${n ? " compact" : ""}`, children: [
    /* @__PURE__ */ i.jsx(bn, { size: n ? 13 : 14 }),
    /* @__PURE__ */ i.jsx("span", { children: e }),
    t?.workflowExecutionId ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: r, children: [
      "Open Run ",
      t.workflowExecutionId
    ] }) : null
  ] });
}
function un({ value: e, ariaLabel: t, copiedLabel: n, onCopied: r, onCopyFailed: o }) {
  if (!e) return null;
  const s = async (a) => {
    a.preventDefault(), a.stopPropagation();
    try {
      await HN(e), r(n);
    } catch {
      o(n);
    }
  };
  return /* @__PURE__ */ i.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (a) => {
    s(a);
  }, children: /* @__PURE__ */ i.jsx(Af, { size: 12 }) });
}
function vN({ context: e, ai: t, definitionId: n, publishedArtifactId: r }) {
  const [o, s] = U("loading"), [a, c] = U(""), [u, l] = U(""), [f, p] = U(null), [d, h] = U([]), g = Ft(t, "weaver.workflows.explain-executable"), N = le(async () => {
    s("loading"), c("");
    try {
      const j = await cd(e);
      h(j.filter((v) => VN(v, n)).sort(ON)), s("ready");
    } catch (j) {
      c(j instanceof Error ? j.message : String(j)), h([]), s("failed");
    }
  }, [e, n]);
  Q(() => {
    N();
  }, [N, r]);
  const w = async (j) => {
    l(""), p(null), c("");
    try {
      const v = await ad(e, j.artifactId);
      p({ artifactId: j.artifactId, workflowExecutionId: Md(v) }), l(`Started ${j.artifactId}`);
    } catch (v) {
      c(v instanceof Error ? v.message : String(v));
    }
  }, m = (j) => {
    g && Bt(t, g, j) && (c(""), p(null), l(`Sent ${j.artifactId} to Weaver`));
  }, C = () => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(n)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, y = (j) => {
    c(""), p(null), l(`Copied ${j}`);
  }, b = (j) => {
    l(""), p(null), c(`Could not copy ${j}.`);
  };
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-artifacts-panel", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-artifacts-toolbar", children: [
      /* @__PURE__ */ i.jsxs("span", { children: [
        d.length,
        " artifact",
        d.length === 1 ? "" : "s"
      ] }),
      /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => {
        N();
      }, children: [
        /* @__PURE__ */ i.jsx(Zi, { size: 13 }),
        " Refresh"
      ] }),
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: C, children: "Open list" })
    ] }),
    o === "failed" ? /* @__PURE__ */ i.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ i.jsx(St, { size: 14 }),
      " ",
      a
    ] }) : null,
    u ? /* @__PURE__ */ i.jsx(Ad, { status: u, run: f, compact: !0 }) : null,
    o === "loading" ? /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "Loading artifacts..." }) : null,
    o === "ready" && d.length === 0 ? /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "No published artifacts for this workflow yet." }) : null,
    o === "ready" && d.length > 0 ? /* @__PURE__ */ i.jsx("div", { className: "wf-artifact-list", role: "list", "aria-label": "Workflow artifacts", children: d.map((j) => /* @__PURE__ */ i.jsxs("article", { className: "wf-artifact-card", role: "listitem", "data-active": j.artifactId === r ? "true" : void 0, children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-artifact-card-heading", children: [
        /* @__PURE__ */ i.jsxs("div", { children: [
          /* @__PURE__ */ i.jsxs("span", { className: "wf-artifact-version", children: [
            "Version ",
            j.artifactVersion
          ] }),
          j.artifactId === r ? /* @__PURE__ */ i.jsx("span", { className: "wf-chip", children: "Latest publish" }) : null
        ] }),
        /* @__PURE__ */ i.jsx("span", { children: tt(j.publishedAt ?? j.createdAt) })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { className: "wf-artifact-card-values", children: [
        /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line", children: [
          /* @__PURE__ */ i.jsx("code", { title: j.artifactId, children: j.artifactId }),
          /* @__PURE__ */ i.jsx(un, { value: j.artifactId, ariaLabel: `Copy artifact ID ${j.artifactId}`, copiedLabel: "artifact ID", onCopied: y, onCopyFailed: b })
        ] }),
        /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ i.jsx("code", { title: j.artifactHash, children: j.artifactHash }),
          /* @__PURE__ */ i.jsx(un, { value: j.artifactHash, ariaLabel: `Copy artifact hash ${j.artifactHash}`, copiedLabel: "artifact hash", onCopied: y, onCopyFailed: b })
        ] })
      ] }),
      /* @__PURE__ */ i.jsxs("dl", { children: [
        /* @__PURE__ */ i.jsxs("div", { children: [
          /* @__PURE__ */ i.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ i.jsxs("dd", { children: [
            $d(j.sourceKind),
            " ",
            j.sourceVersion ? `v${j.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ i.jsxs("div", { children: [
          /* @__PURE__ */ i.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ i.jsx("dd", { children: Td(j) })
        ] })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => {
          w(j);
        }, children: [
          /* @__PURE__ */ i.jsx(fn, { size: 13 }),
          " Run"
        ] }),
        g ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => m(j), children: [
          /* @__PURE__ */ i.jsx(pt, { size: 13 }),
          " Explain"
        ] }) : null
      ] })
    ] }, j.artifactId)) }) : null
  ] });
}
function bN({ context: e }) {
  const [t, n] = U("loading"), [r, o] = U(""), [s, a] = U(""), [c, u] = U(""), [l, f] = U([]), p = le(async () => {
    n("loading"), o("");
    try {
      const h = await a0(e, {
        status: s || void 0,
        runKind: c || void 0,
        take: 100
      });
      f(h), n("ready");
    } catch (h) {
      o(h instanceof Error ? h.message : String(h)), f([]), n("failed");
    }
  }, [e, c, s]);
  Q(() => {
    p();
  }, [p]);
  const d = (h) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(h)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => {
        p();
      }, children: "Refresh" }),
      /* @__PURE__ */ i.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ i.jsx("span", { children: "Status" }),
        /* @__PURE__ */ i.jsxs("select", { "aria-label": "Workflow run status", value: s, onChange: (h) => a(h.target.value), children: [
          /* @__PURE__ */ i.jsx("option", { value: "", children: "All statuses" }),
          /* @__PURE__ */ i.jsx("option", { value: "Pending", children: "Pending" }),
          /* @__PURE__ */ i.jsx("option", { value: "Running", children: "Running" }),
          /* @__PURE__ */ i.jsx("option", { value: "Suspended", children: "Suspended" }),
          /* @__PURE__ */ i.jsx("option", { value: "Completed", children: "Completed" }),
          /* @__PURE__ */ i.jsx("option", { value: "Faulted", children: "Faulted" }),
          /* @__PURE__ */ i.jsx("option", { value: "Cancelled", children: "Cancelled" })
        ] })
      ] }),
      /* @__PURE__ */ i.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ i.jsx("span", { children: "Kind" }),
        /* @__PURE__ */ i.jsxs("select", { "aria-label": "Run Kind", value: c, onChange: (h) => u(h.target.value), children: [
          /* @__PURE__ */ i.jsx("option", { value: "", children: "All kinds" }),
          /* @__PURE__ */ i.jsx("option", { value: "TestRun", children: "Test Run" }),
          /* @__PURE__ */ i.jsx("option", { value: "PublishedRun", children: "Published Run" }),
          /* @__PURE__ */ i.jsx("option", { value: "BackgroundWeaverRun", children: "Background Weaver Run" }),
          /* @__PURE__ */ i.jsx("option", { value: "Unknown", children: "Unknown / legacy" })
        ] })
      ] })
    ] }),
    t === "failed" ? /* @__PURE__ */ i.jsx(vr, { message: r }) : null,
    t === "loading" ? /* @__PURE__ */ i.jsx(Cs, {}) : null,
    t === "ready" && l.length === 0 ? /* @__PURE__ */ i.jsx(
      Is,
      {
        icon: /* @__PURE__ */ i.jsx(Eo, { size: 22 }),
        title: "No workflow runs yet",
        description: "Run a published workflow executable to create execution history here."
      }
    ) : null,
    t === "ready" && l.length > 0 ? /* @__PURE__ */ i.jsxs("div", { className: "wf-grid wf-instance-grid", role: "table", "aria-label": "Workflow runs", children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ i.jsx("span", { children: "Run" }),
        /* @__PURE__ */ i.jsx("span", { children: "Kind" }),
        /* @__PURE__ */ i.jsx("span", { children: "Status" }),
        /* @__PURE__ */ i.jsx("span", { children: "Definition" }),
        /* @__PURE__ */ i.jsx("span", { children: "Activity" }),
        /* @__PURE__ */ i.jsx("span", { children: "Started" }),
        /* @__PURE__ */ i.jsx("span", { children: "Duration" })
      ] }),
      l.map((h) => /* @__PURE__ */ i.jsxs(
        "button",
        {
          type: "button",
          className: "wf-grid-row",
          role: "row",
          "aria-label": `Inspect workflow run ${h.workflowExecutionId}`,
          onClick: () => d(h.workflowExecutionId),
          children: [
            /* @__PURE__ */ i.jsxs("span", { children: [
              /* @__PURE__ */ i.jsx("strong", { children: h.workflowExecutionId }),
              /* @__PURE__ */ i.jsx("small", { children: h.artifactId })
            ] }),
            /* @__PURE__ */ i.jsx("span", { children: _d(h.runKind) }),
            /* @__PURE__ */ i.jsx("span", { children: /* @__PURE__ */ i.jsx(br, { status: h.status, subStatus: h.subStatus }) }),
            /* @__PURE__ */ i.jsxs("span", { children: [
              /* @__PURE__ */ i.jsx("strong", { children: h.definitionId }),
              /* @__PURE__ */ i.jsx("small", { children: h.definitionVersionId })
            ] }),
            /* @__PURE__ */ i.jsxs("span", { children: [
              /* @__PURE__ */ i.jsxs("strong", { children: [
                h.activityCount,
                " activities"
              ] }),
              /* @__PURE__ */ i.jsxs("small", { children: [
                h.incidentCount,
                " incidents"
              ] })
            ] }),
            /* @__PURE__ */ i.jsx("span", { children: tt(h.startedAt ?? h.createdAt) }),
            /* @__PURE__ */ i.jsx("span", { children: xd(h.startedAt ?? h.createdAt, h.completedAt ?? h.updatedAt) })
          ]
        },
        h.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function NN({ context: e, ai: t, workflowExecutionId: n }) {
  const [r, o] = U("loading"), [s, a] = U(""), [c, u] = U(null), [l, f] = U(null), p = Ft(t, "weaver.workflows.explain-instance"), d = le(async () => {
    if (!n) {
      a("No workflow execution id was provided."), o("failed");
      return;
    }
    o("loading"), a("");
    try {
      const g = await c0(e, n), [N, w] = await Promise.all([
        Gv(e, g.instance.definitionVersionId).then(
          (m) => ({ definitionVersion: m, error: "" }),
          (m) => ({ definitionVersion: null, error: m instanceof Error ? m.message : String(m) })
        ),
        ws(e)
      ]);
      u({
        details: g,
        definitionVersion: N.definitionVersion,
        definitionVersionError: N.error,
        activityCatalog: w.activities
      }), f(null), o("ready");
    } catch (g) {
      u(null), a(sj(g, n)), o("failed");
    }
  }, [e, n]);
  Q(() => {
    d();
  }, [d]);
  const h = () => {
    window.history.pushState({}, "", "/workflows/instances"), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: h, children: [
        /* @__PURE__ */ i.jsx(io, { size: 14 }),
        " Runs"
      ] }),
      /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => {
        d();
      }, children: [
        /* @__PURE__ */ i.jsx(Zi, { size: 14 }),
        " Refresh"
      ] }),
      c && p ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => Bt(t, p, c.details), children: [
        /* @__PURE__ */ i.jsx(pt, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    r === "loading" ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "Loading workflow run..." }) : null,
    r === "failed" ? /* @__PURE__ */ i.jsx(vr, { message: s }) : null,
    r === "ready" && c ? /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-detail-workbench", children: [
      /* @__PURE__ */ i.jsx(
        jN,
        {
          definitionVersion: c.definitionVersion,
          definitionVersionError: c.definitionVersionError,
          activityCatalog: c.activityCatalog,
          details: c.details,
          selectedEvidenceId: l,
          onSelectEvidence: f
        }
      ),
      /* @__PURE__ */ i.jsx(
        SN,
        {
          ai: t,
          action: p,
          summary: c.details.instance,
          details: c.details,
          state: "ready",
          error: "",
          selectedEvidenceId: l,
          onSelectEvidence: f,
          activityCatalog: c.activityCatalog,
          graphNodeIds: c.definitionVersion ? kN(c.definitionVersion, c.activityCatalog) : void 0
        }
      )
    ] }) : null
  ] });
}
function jN({ definitionVersion: e, definitionVersionError: t, activityCatalog: n, details: r, selectedEvidenceId: o, onSelectEvidence: s }) {
  const a = de(() => {
    if (!e) return { nodes: [], edges: [] };
    const c = e.state.rootActivity;
    if (!c) return { nodes: [], edges: [] };
    const u = n.find((h) => h.activityVersionId === c.activityVersionId), l = ms(c, u), f = l === "unsupported" ? null : Jn(c, []), p = l === "unsupported" ? Ri(c, n, e.layout) : f ? Qu(f, n, e.layout) : Ri(c, n, e.layout), d = p.nodes.map((h) => ({
      ...h,
      draggable: !1,
      connectable: !1,
      deletable: !1
    }));
    return {
      nodes: gv(d, r.activities, r.incidents, o),
      edges: p.edges.map((h) => ({ ...h, deletable: !1 }))
    };
  }, [n, e, r, o]);
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-instance-canvas-shell", "aria-label": "Workflow run canvas", children: [
    /* @__PURE__ */ i.jsxs("header", { children: [
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("span", { children: "Definition version" }),
        /* @__PURE__ */ i.jsx("h3", { children: e ? /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
          e.definition.name,
          " ",
          /* @__PURE__ */ i.jsx("small", { children: e.version })
        ] }) : /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
          "Definition graph unavailable ",
          /* @__PURE__ */ i.jsx("small", { children: r.instance.definitionVersionId })
        ] }) })
      ] }),
      /* @__PURE__ */ i.jsx(br, { status: r.instance.status, subStatus: r.instance.subStatus })
    ] }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ i.jsxs("div", { className: "wf-empty", children: [
        "The workflow run loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ i.jsx("small", { children: ij(t) }) : null
      ] }),
      e && a.nodes.length === 0 ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      a.nodes.length > 0 ? /* @__PURE__ */ i.jsxs(
        zu,
        {
          nodes: a.nodes,
          edges: a.edges,
          nodeTypes: Sd,
          edgeTypes: Ed,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (c, u) => s(u.id),
          onPaneClick: () => s(null),
          children: [
            /* @__PURE__ */ i.jsx(Ou, {}),
            /* @__PURE__ */ i.jsx(Bu, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ i.jsx(Wu, {})
          ]
        }
      ) : null
    ] })
  ] });
}
function SN({ ai: e, action: t, summary: n, details: r, state: o, error: s, selectedEvidenceId: a = null, onSelectEvidence: c, graphNodeIds: u, activityCatalog: l = [] }) {
  const [f, p] = U("timeline");
  if (!n)
    return /* @__PURE__ */ i.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "Select a workflow run to inspect its timeline." }) });
  const d = r?.incidents.length ?? 0, h = [
    { id: "timeline", title: "Timeline", order: 0, icon: /* @__PURE__ */ i.jsx(qi, { size: 14 }), render: () => null },
    { id: "issues", title: d > 0 ? `Issues (${d})` : "Issues", order: 1, icon: /* @__PURE__ */ i.jsx(St, { size: 14 }), render: () => null },
    { id: "details", title: "Details", order: 2, icon: /* @__PURE__ */ i.jsx(Yi, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ i.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Workflow run details", children: [
    /* @__PURE__ */ i.jsxs("header", { children: [
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("span", { children: "Workflow Instance ID" }),
        /* @__PURE__ */ i.jsx("h3", { children: n.workflowExecutionId })
      ] }),
      t ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => Bt(e, t, r ?? n), children: [
        /* @__PURE__ */ i.jsx(pt, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    /* @__PURE__ */ i.jsx("div", { className: "wf-instance-tabs", children: /* @__PURE__ */ i.jsx(no, { label: "Workflow run tabs", tabs: h, activeTabId: f, onSelect: (g) => p(g) }) }),
    o === "loading" ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "Loading run details..." }) : null,
    o === "failed" ? /* @__PURE__ */ i.jsx(vr, { message: s }) : null,
    o === "ready" && r ? /* @__PURE__ */ i.jsx("div", { className: "wf-instance-tab-content", children: f === "timeline" ? /* @__PURE__ */ i.jsx(
      Wb,
      {
        activities: r.activities,
        activityCatalog: l,
        selectedEvidenceId: a,
        onSelectEvidence: c
      }
    ) : f === "issues" ? /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
      /* @__PURE__ */ i.jsx(EN, { incidents: r.incidents, selectedEvidenceId: a, onSelectEvidence: c }),
      /* @__PURE__ */ i.jsx(CN, { details: r, graphNodeIds: u })
    ] }) : /* @__PURE__ */ i.jsxs("dl", { className: "wf-instance-meta", children: [
      /* @__PURE__ */ i.jsx("dt", { children: "Status" }),
      /* @__PURE__ */ i.jsx("dd", { children: /* @__PURE__ */ i.jsx(br, { status: n.status, subStatus: n.subStatus }) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Run Kind" }),
      /* @__PURE__ */ i.jsx("dd", { children: _d(n.runKind) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Artifact" }),
      /* @__PURE__ */ i.jsxs("dd", { children: [
        n.artifactId,
        " ",
        /* @__PURE__ */ i.jsx("small", { children: n.artifactVersion })
      ] }),
      /* @__PURE__ */ i.jsx("dt", { children: "Definition" }),
      /* @__PURE__ */ i.jsxs("dd", { children: [
        n.definitionId,
        " ",
        /* @__PURE__ */ i.jsx("small", { children: n.definitionVersionId })
      ] }),
      /* @__PURE__ */ i.jsx("dt", { children: "Created" }),
      /* @__PURE__ */ i.jsx("dd", { children: tt(n.createdAt) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Started" }),
      /* @__PURE__ */ i.jsx("dd", { children: tt(n.startedAt) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Completed" }),
      /* @__PURE__ */ i.jsx("dd", { children: tt(n.completedAt) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Correlation" }),
      /* @__PURE__ */ i.jsx("dd", { children: n.correlationId || "None" })
    ] }) }) : null
  ] });
}
function EN({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ i.jsx("h4", { children: "Incidents" }),
    e.length === 0 ? /* @__PURE__ */ i.jsx("p", { children: "No incidents recorded." }) : null,
    e.map((r) => /* @__PURE__ */ i.jsxs(
      "button",
      {
        type: "button",
        className: "wf-instance-incident",
        "data-severity": r.severity.toLowerCase(),
        "data-selected": r.incidentId === t,
        onClick: () => n?.(r.incidentId),
        children: [
          /* @__PURE__ */ i.jsx("strong", { children: r.failureType }),
          /* @__PURE__ */ i.jsxs("span", { children: [
            r.status,
            " · ",
            r.severity
          ] }),
          /* @__PURE__ */ i.jsx("p", { children: r.message })
        ]
      },
      r.incidentId
    ))
  ] });
}
function CN({ details: e, graphNodeIds: t }) {
  if (!t) return null;
  const n = new Map(e.activities.map((s) => [s.activityExecutionId, s])), r = e.activities.filter((s) => !t.has($c(s))), o = e.incidents.filter((s) => {
    const a = s.activityExecutionId ? n.get(s.activityExecutionId) : null, c = s.executableNodeId ?? (a ? $c(a) : "");
    return !c || !t.has(c);
  });
  return r.length === 0 && o.length === 0 ? null : /* @__PURE__ */ i.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ i.jsx("h4", { children: "Unmatched runtime evidence" }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-unmatched-list", children: [
      r.map((s) => /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ i.jsx("strong", { children: So(s.activityType) ?? s.activityType }),
        /* @__PURE__ */ i.jsx("small", { children: s.activityExecutionId })
      ] }, `activity-${s.activityExecutionId}`)),
      o.map((s) => /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ i.jsx("strong", { children: s.failureType }),
        /* @__PURE__ */ i.jsx("small", { children: s.incidentId })
      ] }, `incident-${s.incidentId}`))
    ] })
  ] });
}
function br({ status: e, subStatus: t }) {
  return /* @__PURE__ */ i.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
function _d(e) {
  switch (IN(e)) {
    case "testrun":
      return "Test Run";
    case "publishedrun":
      return "Published Run";
    case "backgroundweaverrun":
      return "Background Weaver Run";
    default:
      return "Unknown / legacy";
  }
}
function IN(e) {
  return (e ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}
function kN(e, t) {
  const n = e.state.rootActivity;
  if (!n) return /* @__PURE__ */ new Set();
  const r = t.find((s) => s.activityVersionId === n.activityVersionId);
  if (ms(n, r) === "unsupported") return /* @__PURE__ */ new Set([n.nodeId]);
  const o = Jn(n, []);
  return new Set(o?.slot.activities.map((s) => s.nodeId) ?? [n.nodeId]);
}
function $c(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function AN({ page: e, pageSize: t, totalCount: n, onPageChange: r, onPageSizeChange: o }) {
  const s = Dd(n, t), a = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-pagination", "aria-label": "Workflow definition pagination", children: [
    /* @__PURE__ */ i.jsxs("span", { className: "wf-pagination-summary", "aria-live": "polite", children: [
      "Showing ",
      a,
      "-",
      c,
      " of ",
      n
    ] }),
    /* @__PURE__ */ i.jsxs("label", { className: "wf-page-size", children: [
      "Rows",
      /* @__PURE__ */ i.jsx("select", { value: t, onChange: (u) => o(Number(u.target.value)), children: sN.map((u) => /* @__PURE__ */ i.jsx("option", { value: u, children: u }, u)) })
    ] }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => r(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ i.jsx(io, { size: 14 }),
        " Previous"
      ] }),
      /* @__PURE__ */ i.jsxs("span", { children: [
        "Page ",
        e,
        " of ",
        s
      ] }),
      /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => r(e + 1), disabled: e >= s, "aria-label": "Next page", title: "Next page", children: [
        "Next ",
        /* @__PURE__ */ i.jsx(on, { size: 14 })
      ] })
    ] })
  ] });
}
function _N(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function Dd(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Ft(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function Bt(e, t, n) {
  const r = t.createPrompt(n);
  return r ? (e.dispatchPrompt(r), !0) : !1;
}
function DN(e) {
  if (!e) return null;
  const t = [], n = e.match(/```(?:json)?\s*([\s\S]*?)```/i);
  n && t.push(n[1]);
  const r = e.match(/\{[\s\S]*?\}/);
  r && t.push(r[0]);
  for (const a of t)
    try {
      const c = JSON.parse(a.trim()), u = Mc(c.name, c.description);
      if (u) return u;
    } catch {
    }
  const o = e.match(/name\s*[:-]\s*(.+)/i)?.[1], s = e.match(/description\s*[:-]\s*(.+)/i)?.[1];
  return Mc(o, s);
}
function Mc(e, t) {
  const n = typeof e == "string" ? Pc(e) : void 0, r = typeof t == "string" ? Pc(t) : void 0;
  return n || r ? { name: n || void 0, description: r || void 0 } : null;
}
function Pc(e) {
  return e.trim().replace(/,$/, "").trim().replace(/^["']/, "").replace(/["']$/, "").trim();
}
function TN(e, t) {
  return e.rootActivityVersionId ?? $N(t, e.rootKind)?.activityVersionId ?? null;
}
function $N(e, t) {
  return e.find((n) => MN(n) === t);
}
function MN(e) {
  return e ? PN(e) ? "flowchart" : RN(e) ? "sequence" : null : null;
}
function Ui(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const r = n.category?.trim() || "Uncategorized";
    t.set(r, [...t.get(r) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [r]) => n.localeCompare(r)).map(([n, r]) => ({
    category: n,
    activities: r.sort((o, s) => Ie(o).localeCompare(Ie(s)))
  }));
}
function PN(e) {
  return Ie(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function RN(e) {
  return Ie(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function LN(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function Td(e) {
  return WN(e.rootActivityType) || e.rootActivityType;
}
function zN(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function VN(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function ON(e, t) {
  return Rc(t) - Rc(e);
}
function Rc(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function $d(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
function Md(e) {
  const t = e?.workflowExecutionId ?? e?.runId ?? e?.executionId;
  return typeof t == "string" && t.trim() ? t : null;
}
async function HN(e) {
  if (navigator.clipboard?.writeText) {
    await navigator.clipboard.writeText(e);
    return;
  }
  const t = document.createElement("textarea");
  t.value = e, t.setAttribute("readonly", ""), t.style.position = "fixed", t.style.opacity = "0", document.body.appendChild(t), t.select();
  const n = document.execCommand("copy");
  if (t.remove(), !n)
    throw new Error("Clipboard copy failed.");
}
function WN(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function FN(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    Kr(t, n.typeName, n), Kr(t, n.name, n), Kr(t, n.displayName, n);
    const r = n.typeName.split(".").filter(Boolean).at(-1);
    Kr(t, r, n);
  }
  return t;
}
function BN(e, t, n) {
  const r = t.get(e.activityVersionId);
  return n.get(qn(r?.activityTypeKey)) ?? n.get(qn(So(r?.activityTypeKey))) ?? n.get(qn(r?.displayName)) ?? n.get(qn(e.activityVersionId)) ?? null;
}
function Kr(e, t, n) {
  const r = qn(t);
  r && !e.has(r) && e.set(r, n);
}
function qn(e) {
  return e?.trim().toLowerCase() ?? "";
}
function Lc(e, t, n, r) {
  const o = Wo();
  if (!o) return t;
  const s = o.getItem(e);
  if (s == null) return t;
  const a = Number(s);
  return Number.isFinite(a) ? to(a, n, r) : t;
}
function zc(e, t) {
  const n = Wo();
  if (!n) return t;
  const r = n.getItem(e);
  return r === "true" ? !0 : r === "false" ? !1 : t;
}
function KN() {
  const e = Wo();
  if (!e) return null;
  const t = e.getItem(Cd);
  return t === "palette" || t === "inspector" ? t : null;
}
function Wo() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function Bn(e, t) {
  const n = Wo();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function to(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function UN({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: r,
  expressionEditors: o,
  workflowDesignerPanels: s,
  onBack: a
}) {
  const [c, u] = U(null), l = ie(null), f = ie(null), p = ie({});
  Q(() => {
    l.current = c;
  }, [c]);
  const [d, h] = U(null), [g, N] = U([]), [w, m] = U([]), [C, y] = U(null), [b, j] = U(Qr), [v, S] = U("loading"), [A, M] = U([]), [R, $] = U([]), [_, V] = U([]), [E, D] = U(null), [k, T] = U(null), [L, P] = U(null), [K, F] = U(null), [B, q] = U(""), [J, X] = U(""), [ce, G] = U("idle"), [O, Y] = U(null), [se, ae] = U(!1), [te, re] = U(null), [fe, W] = U(() => /* @__PURE__ */ new Set()), [ee, ye] = U(""), [we, $e] = U(() => Lc(Cc, cN, On, Hn)), [_e, Le] = U(() => Lc(Ic, lN, Wn, Fn)), [Me, Ye] = U(() => zc(kc, !1)), [ke, Ce] = U(() => zc(Ac, !1)), [me, Pe] = U(KN), [ct, Fo] = U("activities"), [Sn, En] = U("inspector"), [Et, Nr] = U("designer"), Ze = ie(null), Kt = ie(null), Cn = ie(""), In = ie(0), jr = ie(Promise.resolve()), kn = ie(/* @__PURE__ */ new Map()), We = ie(mc()), lt = ie(null), ut = ie(""), Ut = ie(!1), [Bo, An] = U(0), Xt = ie(null), yt = ie(null), Ct = ie(!1), It = d?.state.rootActivity ?? null, ze = de(() => new Map(g.map((x) => [x.activityVersionId, x])), [g]), _n = le(
    (x) => k0([x.activityVersionId, x.activityTypeKey], C),
    [C]
  ), Sr = de(() => FN(w), [w]), Fe = de(() => Ju(It, A), [It, A]), Er = ms(Fe, Fe ? ze.get(Fe.activityVersionId) : void 0), ve = !!Fe && Er === "unsupported", Be = de(() => ve ? null : Jn(It, A), [It, A, ve]), qt = de(() => Ui(g), [g]), Cr = de(() => {
    const x = ee.trim().toLowerCase();
    if (!x) return qt;
    const I = g.filter((z) => Ie(z).toLowerCase().includes(x) || z.activityTypeKey.toLowerCase().includes(x) || (z.category ?? "").toLowerCase().includes(x) || (z.description ?? "").toLowerCase().includes(x));
    return Ui(I);
  }, [g, ee, qt]), he = de(() => ve && Fe?.nodeId === k ? Fe : Be?.slot.activities.find((x) => x.nodeId === k) ?? null, [ve, Be, Fe, k]), kt = de(
    () => he ? BN(he, ze, Sr) : null,
    [ze, Sr, he]
  ), Dn = de(
    () => he ? _n({ activityVersionId: he.activityVersionId, activityTypeKey: ze.get(he.activityVersionId)?.activityTypeKey }) : null,
    [_n, ze, he]
  ), Yt = he ? He(he) : [], Zt = Zv(e, d?.state, k), Ae = Er === "flowchart" && Be?.slot.mode === "flowchart", At = !It || !ve, mt = ce !== "idle", Ko = !!d?.state.rootActivity && !mt, Ir = Ft(n, "weaver.workflows.find-draft-risks"), kr = Ft(n, "weaver.workflows.propose-update");
  Q(() => {
    if (!(!c || !d))
      return window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
        workflowId: c.definition.id,
        workflowDefinitionId: c.definition.id,
        workflowVersionId: d.sourceVersionId ?? null,
        draftId: d.id,
        revision: rj(d),
        selectedNodeId: k,
        selectedActivityType: kt?.typeName ?? (he ? ze.get(he.activityVersionId)?.activityTypeKey ?? he.activityVersionId : null),
        summary: c.definition.name,
        activities: Rd(d.state.rootActivity, ze),
        connections: Ld(d.state.rootActivity),
        diagnostics: d.validationErrors.map((x) => ({ severity: x.code ?? "warning", message: x.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === c.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [ze, c, d, kt, he, k]), Q(() => {
    const x = (z) => {
      const H = z.detail;
      if (!H?.batch || !H.respond) return;
      if (!d || !c) {
        H.respond({ ok: !1, message: "No active workflow draft is open." });
        return;
      }
      const Z = H.batch.workflowDefinitionId;
      if (Z && Z !== "active-draft" && Z !== c.definition.id) {
        H.respond({ ok: !1, message: `Batch targets workflow '${Z}', but '${c.definition.id}' is active.` });
        return;
      }
      try {
        const ue = Kn(d), oe = qb(d, H.batch, g), ge = `weaver-batch-${Date.now()}`;
        kn.current.set(ge, ue), h(oe.draft), M([]), T(oe.finalActivityIds.at(-1) ?? null), re(null), Y(null), X(oe.summary), q(""), H.respond({ ok: !0, result: { ...oe, undoToken: ge } });
      } catch (ue) {
        const oe = ue instanceof Error ? ue.message : String(ue);
        q(oe), H.respond({ ok: !1, message: oe });
      }
    }, I = (z) => {
      const H = z.detail;
      if (!H?.undoToken || !H.respond) return;
      const Z = kn.current.get(H.undoToken);
      if (!Z) {
        H.respond({ ok: !1, message: "The Weaver batch undo point is no longer available." });
        return;
      }
      kn.current.delete(H.undoToken), h(Z), M([]), T(null), re(null), Y(null), X("Restored workflow draft before Weaver batch."), q(""), H.respond({ ok: !0, summary: "Restored workflow draft before Weaver batch." });
    };
    return window.addEventListener(Nc, x), window.addEventListener(jc, I), () => {
      window.removeEventListener(Nc, x), window.removeEventListener(jc, I);
    };
  }, [g, c, d]), Q(() => {
    Bn(Cc, String(we));
  }, [we]), Q(() => {
    Bn(Ic, String(_e));
  }, [_e]), Q(() => {
    Bn(kc, String(Me));
  }, [Me]), Q(() => {
    Bn(Ac, String(ke));
  }, [ke]), Q(() => {
    Bn(Cd, me);
  }, [me]), Q(() => {
    if (!me) return;
    const x = (I) => {
      I.key === "Escape" && Pe(null);
    };
    return window.addEventListener("keydown", x), () => window.removeEventListener("keydown", x);
  }, [me]);
  const Gt = le(async () => {
    q(""), S("loading");
    const [x, I, z, H, Z] = await Promise.all([
      qv(e, t),
      ws(e),
      d0(e).then(
        (oe) => ({ ok: !0, descriptors: oe }),
        () => ({ ok: !1, descriptors: [] })
      ),
      f0(e).then(
        (oe) => ({ ok: !0, descriptors: oe }),
        () => ({ ok: !1, descriptors: Qr })
      ),
      // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
      ld(e).then(
        (oe) => oe,
        () => null
      )
    ]), ue = x.draft ?? null;
    u(x), Cn.current = ue ? Ke(ue) : "", We.current = mc(), lt.current = ue ? Kn(ue) : null, ut.current = ue ? Ke(ue) : "", Ut.current = !1, An(0), h(ue), N(I.activities ?? []), m(z.descriptors), y(Z), j(H.descriptors.length > 0 ? H.descriptors : Qr), S(z.ok ? "ready" : "failed"), M([]), T(null);
  }, [e, t]);
  Q(() => {
    Gt().catch((x) => q(x instanceof Error ? x.message : String(x)));
  }, [Gt]), Q(() => {
    W((x) => {
      let I = !1;
      const z = new Set(x);
      for (const H of qt)
        z.has(H.category) || (z.add(H.category), I = !0);
      return I ? z : x;
    });
  }, [qt]), Q(() => {
    if (!Fe) {
      $([]), V([]);
      return;
    }
    const x = ve ? Ri(Fe, g, d?.layout ?? []) : Be ? Qu(Be, g, d?.layout ?? []) : { nodes: [], edges: [] };
    $(x.nodes), V(x.edges);
  }, [g, d?.layout, ve, Be, Fe]);
  const Uo = (x) => {
    h((I) => I && { ...I, state: { ...I.state, rootActivity: x } });
  }, Tn = le((x, I) => {
    if (d?.state.rootActivity && ve)
      return;
    const z = zi(x, Hc(x));
    if (!d?.state.rootActivity) {
      Uo(z), T(z.nodeId);
      return;
    }
    if (!Be) {
      if (!He(z)[0]) {
        X(""), q("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      h((Z) => {
        if (!Z?.state.rootActivity) return Z;
        const ue = Z.state.rootActivity, oe = Li(z, [], [ue]), ge = I ? [
          ...Z.layout.filter((xe) => xe.nodeId !== ue.nodeId),
          {
            nodeId: ue.nodeId,
            x: Math.round(I.x),
            y: Math.round(I.y)
          }
        ] : Z.layout;
        return {
          ...Z,
          layout: ge,
          state: {
            ...Z.state,
            rootActivity: oe
          }
        };
      }), T(d.state.rootActivity.nodeId), q(""), X(`Wrapped root in ${Ie(x)}`);
      return;
    }
    h((H) => {
      if (!H?.state.rootActivity) return H;
      const Z = Jn(H.state.rootActivity, A);
      if (!Z) return H;
      const ue = Li(H.state.rootActivity, A, [...Z.slot.activities, z]), oe = I ? [
        ...H.layout.filter((ge) => ge.nodeId !== z.nodeId),
        {
          nodeId: z.nodeId,
          x: Math.round(I.x),
          y: Math.round(I.y)
        }
      ] : H.layout;
      return {
        ...H,
        layout: oe,
        state: {
          ...H.state,
          rootActivity: ue
        }
      };
    }), T(z.nodeId);
  }, [d?.state.rootActivity, A, ve, Be]), Jt = le((x, I) => {
    const z = zi(x, Hc(x)), H = {
      id: z.nodeId,
      type: "workflowActivity",
      position: I,
      selected: !0,
      data: {
        label: Ie(x),
        activityVersionId: x.activityVersionId,
        activityTypeKey: x.activityTypeKey,
        category: x.category,
        executionType: x.executionType,
        icon: wo(x),
        childSlots: He(z),
        acceptsInbound: String(x.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: od(z, x)
      }
    };
    return { activityNode: z, node: H };
  }, []), Re = le((x, I, z = []) => {
    ve || h((H) => {
      if (!H) return H;
      const Z = xv(H.layout, x), ue = H.state.rootActivity;
      if (!ue) return { ...H, layout: Z };
      const oe = Jn(ue, A);
      if (!oe) return { ...H, layout: Z };
      const ge = yv(oe, x, I, z), xe = oe.slot.mode === "flowchart" ? mv(ge, I) : ge;
      return {
        ...H,
        layout: Z,
        state: {
          ...H.state,
          rootActivity: ed(ue, A, xe)
        }
      };
    });
  }, [A, ve]), $n = le((x, I) => {
    if (!Ze.current) return null;
    const z = Ze.current.getBoundingClientRect();
    return E ? E.screenToFlowPosition({ x, y: I }) : {
      x: x - z.left,
      y: I - z.top
    };
  }, [E]), Mn = le((x, I) => document.elementFromPoint(x, I)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), Pn = le((x, I, z) => {
    const H = R.find((De) => De.id === I.source), Z = R.find((De) => De.id === I.target), ue = H && Z ? ej(H, Z) : H ? Wc(H) : z, oe = Jt(x, ue), xe = [...R.map((De) => De.selected ? { ...De, selected: !1 } : De), oe.node], Dt = Iv(_, I, oe.node.id);
    $(xe), V(Dt), T(oe.node.id), Re(xe, Dt, [oe.activityNode]);
  }, [Re, Jt, _, R]), Qt = le((x, I, z) => {
    if (!At || !Ze.current) return !1;
    const H = Ze.current.getBoundingClientRect();
    if (!(I >= H.left && I <= H.right && z >= H.top && z <= H.bottom)) return !1;
    const ue = $n(I, z);
    if (!ue) return !1;
    if (Ae) {
      const oe = Mn(I, z), ge = oe ? _.find((xe) => xe.id === oe) : void 0;
      if (ge)
        return Pn(x, ge, ue), !0;
    }
    return Tn(x, ue), !0;
  }, [Tn, At, _, Mn, Ae, Pn, $n]);
  Q(() => {
    const x = (z) => {
      const H = Xt.current;
      if (!H) return;
      Math.hypot(z.clientX - H.startX, z.clientY - H.startY) >= rN && (H.dragging = !0);
    }, I = (z) => {
      const H = Xt.current;
      if (Xt.current = null, !H?.dragging || !Ze.current || yt.current) return;
      const Z = Ze.current.getBoundingClientRect();
      z.clientX >= Z.left && z.clientX <= Z.right && z.clientY >= Z.top && z.clientY <= Z.bottom && (Ct.current = !0, window.setTimeout(() => {
        Ct.current = !1;
      }, 0), Qt(H.activity, z.clientX, z.clientY));
    };
    return window.addEventListener("pointermove", x), window.addEventListener("pointerup", I), window.addEventListener("pointercancel", I), () => {
      window.removeEventListener("pointermove", x), window.removeEventListener("pointerup", I), window.removeEventListener("pointercancel", I);
    };
  }, [E, Qt]);
  const Ar = (x, I) => {
    yt.current = { activityVersionId: I.activityVersionId, handledDrop: !1 }, x.dataTransfer.setData(Ec, I.activityVersionId), x.dataTransfer.setData("text/plain", I.activityVersionId), x.dataTransfer.effectAllowed = "copy";
  }, Xo = (x, I) => {
    const z = yt.current;
    yt.current = null, !z?.handledDrop && (x.clientX === 0 && x.clientY === 0 || Qt(I, x.clientX, x.clientY) && (Ct.current = !0, window.setTimeout(() => {
      Ct.current = !1;
    }, 0)));
  }, qo = (x, I) => {
    x.button === 0 && (Xt.current = {
      activity: I,
      startX: x.clientX,
      startY: x.clientY,
      dragging: !1
    });
  }, Yo = (x) => {
    Ct.current || At && Tn(x);
  }, _r = (x) => {
    if (!At) {
      x.dataTransfer.dropEffect = "none";
      return;
    }
    if (x.preventDefault(), x.dataTransfer.dropEffect = "copy", !Ae) return;
    const I = Mn(x.clientX, x.clientY);
    F(I);
  }, Dr = (x) => {
    if (!Ze.current) return;
    const I = x.relatedTarget;
    I && Ze.current.contains(I) || F(null);
  }, Tr = (x) => {
    x.preventDefault(), F(null);
    const I = x.dataTransfer.getData(Ec) || x.dataTransfer.getData("text/plain");
    if (!I || (x.stopPropagation(), yt.current?.activityVersionId === I && (yt.current.handledDrop = !0), !At)) return;
    const z = ze.get(I);
    z && Qt(z, x.clientX, x.clientY);
  }, Zo = () => {
    if (!Ae) return;
    const x = Ze.current?.getBoundingClientRect();
    x && P({
      kind: "fromEmpty",
      clientX: x.left + x.width / 2,
      clientY: x.top + x.height / 2
    });
  }, en = le(async (x, I) => {
    const z = async () => {
      const Z = ++In.current, ue = Ke(x);
      q("");
      try {
        const oe = await r0(e, x), ge = Ke(oe);
        return Cn.current = ge, h((xe) => !xe || xe.id !== oe.id ? xe : Ke(xe) === ue ? oe : { ...xe, validationErrors: oe.validationErrors }), Z === In.current && X(I), oe;
      } catch (oe) {
        throw Z === In.current && (X(""), q(oe instanceof Error ? oe.message : String(oe))), oe;
      }
    }, H = jr.current.then(z, z);
    return jr.current = H.catch(() => {
    }), H;
  }, [e]);
  Q(() => {
    if (!se || !d || Ke(d) === Cn.current) return;
    X("Autosaving...");
    const I = window.setTimeout(() => {
      en(d, "Autosaved").catch(() => {
      });
    }, oN);
    return () => window.clearTimeout(I);
  }, [se, d, en]), Q(() => {
    if (!d) return;
    if (Ut.current) {
      Ut.current = !1;
      return;
    }
    const x = Ke(d);
    if (x === ut.current) return;
    const I = window.setTimeout(() => {
      const z = lt.current;
      z && (We.current = xc(We.current, z), An((H) => H + 1)), lt.current = Kn(d), ut.current = x;
    }, iN);
    return () => window.clearTimeout(I);
  }, [d]);
  const $r = le(() => {
    if (!d) return;
    const x = c?.definition.name;
    sb(rb(d, x), x), X("Exported workflow as JSON.");
  }, [d, c]), Go = le((x) => {
    h((I) => I && { ...I, state: x(I.state) });
  }, []), _t = le(() => {
    f.current !== null && (window.clearTimeout(f.current), f.current = null);
    const x = p.current;
    p.current = {};
    const I = l.current?.definition;
    !I || x.name === void 0 && x.description === void 0 || n0(e, I.id, {
      name: x.name ?? I.name,
      description: x.description ?? I.description ?? null
    }).then((z) => u((H) => H && H.definition.id === z.definition.id ? { ...H, definition: z.definition } : H)).catch(() => X("Couldn't save name/description."));
  }, [e]), Jo = le((x) => {
    u((I) => I && { ...I, definition: { ...I.definition, ...x } }), p.current = { ...p.current, ...x }, f.current !== null && window.clearTimeout(f.current), f.current = window.setTimeout(_t, 800);
  }, [_t]);
  Q(() => () => {
    _t();
  }, [_t]);
  const Qo = le((x) => {
    if (!d) return "No draft is loaded.";
    const I = ib(x, d);
    return I.ok ? (h(I.draft), T(null), M([]), X("Applied workflow JSON."), null) : I.error;
  }, [d]), xt = le(() => {
    if (!d) return;
    const x = Ke(d);
    if (x === ut.current) return;
    const I = lt.current;
    I && (We.current = xc(We.current, I)), lt.current = Kn(d), ut.current = x;
  }, [d]), tn = le((x) => {
    Ut.current = !0, lt.current = Kn(x), ut.current = Ke(x), h(x), T(null), M([]), An((I) => I + 1);
  }, []), Rn = le(() => {
    if (!d) return;
    xt();
    const x = gb(We.current, d);
    x && (We.current = x.history, tn(x.snapshot));
  }, [d, xt, tn]), nn = le(() => {
    if (!d) return;
    xt();
    const x = yb(We.current, d);
    x && (We.current = x.history, tn(x.snapshot));
  }, [d, xt, tn]), { canUndoNow: Vd, canRedoNow: Od } = de(() => {
    const x = !!d && !!lt.current && Ke(d) !== ut.current;
    return {
      canUndoNow: pb(We.current) || x,
      canRedoNow: hb(We.current) && !x
    };
  }, [d, Bo]);
  Q(() => {
    const x = (I) => {
      if (Et !== "designer" || !(I.metaKey || I.ctrlKey)) return;
      const z = I.target;
      if (z && (z.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(z.tagName))) return;
      const H = I.key.toLowerCase();
      H === "z" && !I.shiftKey ? (I.preventDefault(), Rn()) : (H === "z" && I.shiftKey || H === "y") && (I.preventDefault(), nn());
    };
    return window.addEventListener("keydown", x), () => window.removeEventListener("keydown", x);
  }, [Et, Rn, nn]);
  const Hd = async () => {
    if (!(!d || mt)) {
      G("saving"), X("Saving...");
      try {
        await en(d, "Saved");
      } catch {
      } finally {
        G("idle");
      }
    }
  }, Wd = async () => {
    if (!(!d || mt)) {
      G("promoting"), X("Saving...");
      try {
        await en(d, "Saved"), X("Promoting...");
        const x = await o0(e, d.id), I = await i0(e, x.versionId);
        re(I.artifactId), X(`Published ${I.artifactVersion}`), await Gt();
      } catch (x) {
        X(""), q(x instanceof Error ? x.message : String(x));
      } finally {
        G("idle");
      }
    }
  }, Fd = async () => {
    if (!d?.state.rootActivity || mt) return;
    const x = d, I = Ke(x);
    Y(null), X("Preparing test run...");
    try {
      G("testRunPreparing"), X("Preparing test run...");
      const z = oj(x);
      G("testRunStarting"), X("Starting test run...");
      const H = await s0(e, {
        definitionId: x.definitionId,
        snapshotId: z,
        state: x.state
      });
      Y({ draftSignature: I, view: H }), En("runtime"), Ce(!1), X(ks(H) ? "Test run rejected" : "Test run dispatched");
    } catch (z) {
      X(""), q(z instanceof Error ? z.message : String(z));
    } finally {
      G("idle");
    }
  }, Bd = (x) => {
    const I = ve ? x.filter((z) => z.type === "select") : x;
    I.length !== 0 && $((z) => uu(I, z));
  }, Kd = (x) => {
    ve || V((I) => du(x, I));
  }, ei = (x) => !x.source || !x.target || x.source === x.target || !Ae ? !1 : !x.targetHandle, Ud = (x) => {
    if (!d?.state.rootActivity || !Be || !Ae || !ei(x)) return;
    const I = vo(x.source, x.target, x.sourceHandle ?? "Done", x.targetHandle ?? void 0), z = pu(I, _);
    V(z), Re(R, z);
  }, Xd = () => {
    Re(R, _);
  }, qd = !ve && R.length > 0, Yd = le(() => {
    if (ve || R.length === 0) return;
    const x = Be?.slot.mode === "sequence" ? "sequence" : "flowchart", I = lb(R, _, x), z = R.map((H) => {
      const Z = I.get(H.id);
      return Z ? { ...H, position: Z } : H;
    });
    $(z), Re(z, _), window.requestAnimationFrame(() => E?.fitView({ padding: 0.2 })), X("Rearranged the canvas.");
  }, [_, R, Be, ve, Re, E]), Zd = (x, I) => {
    if (!I.nodeId || I.handleType === "target") {
      Kt.current = null;
      return;
    }
    Kt.current = {
      nodeId: I.nodeId,
      handleId: I.handleId ?? null
    };
  }, Gd = (x, I) => {
    const z = nj(Kt.current, I);
    if (Kt.current = null, !z || !Ae || I.toNode || I.toHandle || tj(x)) return;
    const H = Pd(x);
    P({
      kind: "fromPort",
      sourceNodeId: z.nodeId,
      sourceHandleId: z.handleId,
      clientX: H.x,
      clientY: H.y
    });
  }, Jd = (x, I) => {
    if (!Ae || !ei(I)) return;
    const z = Um(x, {
      ...I,
      sourceHandle: I.sourceHandle ?? "Done",
      targetHandle: I.targetHandle ?? void 0
    }, _, { shouldReplaceId: !1 });
    V(z), Re(R, z);
  }, Qd = (x) => {
    if (ve || x.length === 0) return;
    const I = new Set(x.map((Z) => Z.id)), z = R.filter((Z) => !I.has(Z.id)), H = _.filter((Z) => !I.has(Z.source) && !I.has(Z.target));
    $(z), V(H), k && I.has(k) && T(null), Re(z, H);
  }, ef = (x) => {
    if (ve || x.length === 0) return;
    const I = new Set(x.map((H) => H.id)), z = _.filter((H) => !I.has(H.id));
    V(z), Re(R, z);
  }, As = le((x) => {
    if (ve) return;
    const I = _.filter((z) => z.id !== x);
    V(I), Re(R, I);
  }, [Re, _, ve, R]), _s = le((x, I, z) => {
    Ae && P({ kind: "spliceEdge", edgeId: x, clientX: I, clientY: z });
  }, [Ae]), tf = (x) => {
    const I = L;
    if (!I) return;
    P(null);
    const z = $n(I.clientX, I.clientY) ?? { x: 0, y: 0 };
    if (I.kind === "fromEmpty") {
      const Z = Jt(x, z), oe = [...R.map((ge) => ge.selected ? { ...ge, selected: !1 } : ge), Z.node];
      $(oe), T(Z.node.id), Re(oe, _, [Z.activityNode]);
      return;
    }
    if (I.kind === "fromPort") {
      const Z = R.find((De) => De.id === I.sourceNodeId), ue = Z ? Wc(Z) : z, oe = Jt(x, ue), xe = [...R.map((De) => De.selected ? { ...De, selected: !1 } : De), oe.node], Dt = [..._, vo(I.sourceNodeId, oe.node.id, I.sourceHandleId ?? "Done")];
      $(xe), V(Dt), T(oe.node.id), Re(xe, Dt, [oe.activityNode]);
      return;
    }
    const H = _.find((Z) => Z.id === I.edgeId);
    H && Pn(x, H, z);
  }, nf = de(() => ({
    highlightedEdgeId: K,
    deleteEdge: As,
    requestInsertActivity: _s
  }), [As, K, _s]), rf = (x, I, z) => {
    M((H) => [...H, { ownerNodeId: x.nodeId, slotId: I, label: z }]), T(null);
  }, Ds = le((x) => {
    h((I) => {
      const z = I?.state.rootActivity;
      return !I || !z ? I : {
        ...I,
        state: {
          ...I.state,
          rootActivity: td(z, x.nodeId, () => x)
        }
      };
    });
  }, []), of = le((x) => {
    if (!x) return;
    const I = d?.state.rootActivity;
    if (!I) return;
    const z = hv(I, x, (H) => {
      const Z = ze.get(H.activityVersionId);
      return Z ? Ie(Z) : H.nodeId;
    });
    z && (Nr("designer"), M(z), T(x), Ce(!1));
  }, [d?.state.rootActivity, ze]), sf = (x) => {
    W((I) => {
      const z = new Set(I);
      return z.has(x) ? z.delete(x) : z.add(x), z;
    });
  }, Ts = (x) => {
    Pe((I) => I === x ? null : I), x === "palette" ? Ye((I) => !I) : Ce((I) => !I);
  }, $s = (x) => {
    x === "palette" ? Ye(!1) : Ce(!1), Pe((I) => I === x ? null : x);
  }, Ms = (x, I) => {
    Pe(null), x === "palette" ? (Ye(!1), $e((z) => to(z + I, On, Hn))) : (Ce(!1), Le((z) => to(z + I, Wn, Fn)));
  }, Ps = (x, I) => {
    I.preventDefault(), Pe(null), x === "palette" ? Ye(!1) : Ce(!1);
    const z = I.clientX, H = x === "palette" ? we : _e, Z = x === "palette" ? On : Wn, ue = x === "palette" ? Hn : Fn;
    document.body.classList.add("wf-side-panel-resizing");
    const oe = (xe) => {
      const Dt = x === "palette" ? xe.clientX - z : z - xe.clientX, De = to(H + Dt, Z, ue);
      x === "palette" ? $e(De) : Le(De);
    }, ge = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", oe), window.removeEventListener("pointerup", ge), window.removeEventListener("pointercancel", ge);
    };
    window.addEventListener("pointermove", oe), window.addEventListener("pointerup", ge), window.addEventListener("pointercancel", ge);
  }, Rs = (x, I) => {
    I.key === "ArrowLeft" ? (I.preventDefault(), Ms(x, x === "palette" ? -Br : Br)) : I.key === "ArrowRight" ? (I.preventDefault(), Ms(x, x === "palette" ? Br : -Br)) : I.key === "Home" ? (I.preventDefault(), x === "palette" ? $e(On) : Le(Wn)) : I.key === "End" && (I.preventDefault(), x === "palette" ? $e(Hn) : Le(Fn));
  };
  if (!c || !d)
    return /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: B || "Loading workflow editor..." });
  const af = [
    "wf-editor-body",
    Me ? "palette-collapsed" : "",
    ke ? "inspector-collapsed" : "",
    me === "palette" ? "palette-maximized" : "",
    me === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), cf = {
    "--wf-palette-width": `${Me ? _c : we}px`,
    "--wf-inspector-width": `${ke ? _c : _e}px`
  }, Ls = !Me && me !== "inspector", zs = !ke && me !== "palette", Mr = O?.draftSignature === Ke(d) ? O.view : null, Vs = Mr && J.startsWith("Test run") ? "" : J, lf = (x) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(x)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, uf = {
    definition: c.definition,
    draft: d,
    selectedActivity: he,
    selectedActivityDescriptor: kt,
    selectedActivitySlots: Yt,
    catalog: g,
    currentScopeOwner: Fe,
    frames: A
  }, Os = s.map((x) => {
    const I = x.component;
    return {
      id: x.id,
      title: x.title,
      side: x.side,
      order: x.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ i.jsx(I, { context: uf })
    };
  }), ti = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ i.jsx(Eo, { size: 15 }),
      render: ff
    },
    ...Os.filter((x) => x.side === "left")
  ].sort(Vc), ni = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ i.jsx(qi, { size: 15 }),
      render: pf
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: /* @__PURE__ */ i.jsx(fn, { size: 15 }),
      render: () => /* @__PURE__ */ i.jsx(QN, { testRun: Mr, onOpenRun: lf })
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ i.jsx(Zc, { size: 15 }),
      render: () => /* @__PURE__ */ i.jsx(
        vN,
        {
          context: e,
          ai: n,
          definitionId: c.definition.id,
          publishedArtifactId: te
        }
      )
    },
    ...Os.filter((x) => x.side === "right")
  ].sort(Vc), Hs = ti.find((x) => x.id === ct) ?? ti[0], Ws = ni.find((x) => x.id === Sn) ?? ni[0], df = [
    { id: "designer", title: "Designer", order: 0, icon: /* @__PURE__ */ i.jsx(Cf, { size: 14 }), render: () => null },
    { id: "code", title: "Code", order: 1, icon: /* @__PURE__ */ i.jsx(If, { size: 14 }), render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: /* @__PURE__ */ i.jsx(Yi, { size: 14 }), render: () => null }
  ];
  function ff() {
    const x = ee.trim().length > 0;
    return /* @__PURE__ */ i.jsxs("div", { className: "wf-palette-body", children: [
      /* @__PURE__ */ i.jsxs("label", { className: "wf-palette-search", children: [
        /* @__PURE__ */ i.jsx(Co, { size: 14, "aria-hidden": "true" }),
        /* @__PURE__ */ i.jsx(
          "input",
          {
            type: "search",
            value: ee,
            placeholder: "Search activities",
            "aria-label": "Search activity palette",
            onChange: (I) => ye(I.target.value)
          }
        )
      ] }),
      /* @__PURE__ */ i.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: Cr.length === 0 ? /* @__PURE__ */ i.jsx("p", { className: "wf-muted wf-palette-empty", children: "No matching activities." }) : Cr.map((I) => {
        const z = x || fe.has(I.category);
        return /* @__PURE__ */ i.jsxs("div", { className: "wf-palette-category", children: [
          /* @__PURE__ */ i.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-category-toggle",
              role: "treeitem",
              "aria-expanded": z,
              onClick: () => sf(I.category),
              children: [
                z ? /* @__PURE__ */ i.jsx(Uc, { size: 14 }) : /* @__PURE__ */ i.jsx(on, { size: 14 }),
                /* @__PURE__ */ i.jsx("span", { children: I.category }),
                /* @__PURE__ */ i.jsx("small", { children: I.activities.length })
              ]
            }
          ),
          z ? /* @__PURE__ */ i.jsx("div", { className: "wf-palette-activities", role: "group", children: I.activities.map((H) => {
            const Z = H.description?.trim(), ue = Z ? `wf-palette-description-${H.activityVersionId}` : void 0, oe = Ie(H), ge = wo(H);
            return /* @__PURE__ */ i.jsxs(
              "button",
              {
                type: "button",
                className: "wf-palette-activity",
                role: "treeitem",
                draggable: !0,
                title: Z || Ie(H),
                "aria-describedby": ue,
                onClick: () => Yo(H),
                onDragStart: (xe) => Ar(xe, H),
                onDragEnd: (xe) => Xo(xe, H),
                onPointerDown: (xe) => qo(xe, H),
                children: [
                  /* @__PURE__ */ i.jsx("span", { className: "wf-activity-icon", "data-icon": ge, "aria-hidden": "true", children: Ns(ge) }),
                  /* @__PURE__ */ i.jsxs("span", { className: "wf-palette-activity-text", children: [
                    /* @__PURE__ */ i.jsx("strong", { children: oe }),
                    Z ? /* @__PURE__ */ i.jsx("small", { id: ue, children: Z }) : null
                  ] }),
                  /* @__PURE__ */ i.jsx(Kc, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
                ]
              },
              H.activityVersionId
            );
          }) }) : null
        ] }, I.category);
      }) })
    ] });
  }
  function pf() {
    return he ? /* @__PURE__ */ i.jsxs("div", { className: "wf-inspector-content", children: [
      /* @__PURE__ */ i.jsx("h3", { children: R.find((x) => x.id === he.nodeId)?.data.label ?? he.nodeId }),
      /* @__PURE__ */ i.jsxs("dl", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Node ID" }),
        /* @__PURE__ */ i.jsx("dd", { children: he.nodeId }),
        /* @__PURE__ */ i.jsx("dt", { children: "Activity type" }),
        /* @__PURE__ */ i.jsx("dd", { children: kt?.typeName ?? ze.get(he.activityVersionId)?.activityTypeKey ?? "Unknown" }),
        /* @__PURE__ */ i.jsx("dt", { children: "Activity version" }),
        /* @__PURE__ */ i.jsx("dd", { children: he.activityVersionId })
      ] }),
      Dn ? /* @__PURE__ */ i.jsxs("div", { className: "wf-availability-notice", children: [
        /* @__PURE__ */ i.jsx(oo, { size: 14 }),
        /* @__PURE__ */ i.jsxs("span", { children: [
          "No longer available for new use · ",
          No(Dn.state)
        ] })
      ] }) : null,
      /* @__PURE__ */ i.jsx(
        U0,
        {
          activity: he,
          descriptor: kt,
          editors: r,
          expressionEditors: o,
          expressionDescriptors: b,
          descriptorStatus: v,
          visibleVariables: Zt.visibleVariables,
          scopeStatus: Zt.status,
          onChange: Ds
        }
      ),
      zv(he) ? /* @__PURE__ */ i.jsx("div", { className: "wf-container-variables", children: /* @__PURE__ */ i.jsx(
        Mb,
        {
          context: e,
          variables: id(he),
          title: "Container variables",
          addLabel: "Add container variable",
          emptyLabel: "No container variables declared on this activity.",
          warnings: Wv(Zt.shadowingWarnings, he.nodeId),
          onChange: (x) => Ds(Vv(he, x))
        }
      ) }) : null,
      Yt.length > 0 ? /* @__PURE__ */ i.jsxs("div", { className: "wf-slot-list", children: [
        /* @__PURE__ */ i.jsx("span", { children: "Embedded slots" }),
        Yt.map((x) => /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => rf(he, x.id, `${R.find((I) => I.id === he.nodeId)?.data.label ?? he.nodeId} / ${x.label}`), children: [
          x.label,
          /* @__PURE__ */ i.jsxs("small", { children: [
            x.activities.length,
            " activit",
            x.activities.length === 1 ? "y" : "ies"
          ] })
        ] }, x.id))
      ] }) : /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
    ] }) : /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
  }
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ i.jsx("button", { type: "button", className: "wf-link-button", onClick: a, children: "Definitions" }),
      /* @__PURE__ */ i.jsx(on, { size: 14 }),
      /* @__PURE__ */ i.jsx("strong", { children: c.definition.name }),
      /* @__PURE__ */ i.jsx("span", { className: "wf-chip", children: "Draft" }),
      Vs ? /* @__PURE__ */ i.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ i.jsx(bn, { size: 13 }),
        " ",
        Vs
      ] }) : null,
      /* @__PURE__ */ i.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ i.jsxs("div", { className: "wf-canvas-tools", role: "group", "aria-label": "Canvas tools", children: [
          /* @__PURE__ */ i.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Undo",
              title: "Undo (Ctrl+Z)",
              disabled: !Vd,
              onClick: Rn,
              children: /* @__PURE__ */ i.jsx(Nf, { size: 16 })
            }
          ),
          /* @__PURE__ */ i.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Redo",
              title: "Redo (Ctrl+Shift+Z)",
              disabled: !Od,
              onClick: nn,
              children: /* @__PURE__ */ i.jsx(jf, { size: 16 })
            }
          ),
          /* @__PURE__ */ i.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Auto-layout",
              title: "Auto-layout the canvas",
              disabled: !qd,
              onClick: Yd,
              children: /* @__PURE__ */ i.jsx(Sf, { size: 16 })
            }
          )
        ] }),
        /* @__PURE__ */ i.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ i.jsx("input", { className: "wf-autosave-switch-input", type: "checkbox", checked: se, onChange: (x) => ae(x.target.checked) }),
          /* @__PURE__ */ i.jsx("span", { children: "Autosave" })
        ] }),
        Ir ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => Bt(n, Ir, { definition: c.definition, draft: d }), children: [
          /* @__PURE__ */ i.jsx(pt, { size: 15 }),
          " Risks"
        ] }) : null,
        kr ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => Bt(n, kr, { definition: c.definition, draft: d }), children: [
          /* @__PURE__ */ i.jsx(pt, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ i.jsxs("button", { type: "button", title: "Export workflow as JSON", onClick: $r, children: [
          /* @__PURE__ */ i.jsx(Ef, { size: 15 }),
          " Export"
        ] }),
        /* @__PURE__ */ i.jsxs("button", { type: "button", disabled: mt, onClick: () => {
          Hd();
        }, children: [
          /* @__PURE__ */ i.jsx(Yc, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ i.jsxs("button", { type: "button", disabled: mt, onClick: () => {
          Wd();
        }, children: [
          /* @__PURE__ */ i.jsx(qc, { size: 15 }),
          " Promote"
        ] }),
        Mr ? /* @__PURE__ */ i.jsx(
          JN,
          {
            testRun: Mr,
            onOpenDetails: () => {
              En("runtime"), Ce(!1);
            }
          }
        ) : null,
        /* @__PURE__ */ i.jsxs(
          "button",
          {
            type: "button",
            disabled: !Ko,
            title: d.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              Fd();
            },
            children: [
              /* @__PURE__ */ i.jsx(fn, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    B ? /* @__PURE__ */ i.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ i.jsx(St, { size: 16 }),
      " ",
      B
    ] }) : null,
    /* @__PURE__ */ i.jsxs("div", { className: af, style: cf, children: [
      /* @__PURE__ */ i.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ i.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ i.jsx(
            no,
            {
              label: "Activities panel tabs",
              tabs: ti,
              activeTabId: Hs.id,
              onSelect: Fo
            }
          ),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": Me ? "Expand activities panel" : "Collapse activities panel",
                title: Me ? "Expand" : "Collapse",
                onClick: () => Ts("palette"),
                children: Me ? /* @__PURE__ */ i.jsx(on, { size: 14 }) : /* @__PURE__ */ i.jsx(io, { size: 14 })
              }
            ),
            Me ? null : /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": me === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: me === "palette" ? "Restore" : "Maximize",
                onClick: () => $s("palette"),
                children: me === "palette" ? /* @__PURE__ */ i.jsx(Ks, { size: 14 }) : /* @__PURE__ */ i.jsx(ro, { size: 14 })
              }
            )
          ] })
        ] }),
        Ls ? Hs.render() : null
      ] }),
      Ls && !me ? /* @__PURE__ */ i.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": On,
          "aria-valuemax": Hn,
          "aria-valuenow": we,
          tabIndex: 0,
          onPointerDown: (x) => Ps("palette", x),
          onKeyDown: (x) => Rs("palette", x)
        }
      ) : /* @__PURE__ */ i.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ i.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ i.jsx("div", { className: "wf-canvas-tabs", children: /* @__PURE__ */ i.jsx(
          no,
          {
            label: "Editor view tabs",
            tabs: df,
            activeTabId: Et,
            onSelect: (x) => Nr(x)
          }
        ) }),
        Et === "code" ? /* @__PURE__ */ i.jsx(bb, { draft: d, onApply: Qo }) : Et === "properties" ? /* @__PURE__ */ i.jsx(Rb, { details: c, draft: d, context: e, onStateChange: Go, onDefinitionMetaChange: Jo }) : /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
          /* @__PURE__ */ i.jsxs("div", { className: "wf-breadcrumb", children: [
            /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => {
              M([]), T(null);
            }, children: "Root" }),
            A.map((x, I) => /* @__PURE__ */ i.jsxs(it.Fragment, { children: [
              /* @__PURE__ */ i.jsx(on, { size: 13 }),
              /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => {
                M(A.slice(0, I + 1)), T(null);
              }, children: x.label })
            ] }, `${x.ownerNodeId}-${x.slotId}-${I}`))
          ] }),
          /* @__PURE__ */ i.jsxs("div", { className: "wf-canvas", ref: Ze, onDragOver: _r, onDragLeave: Dr, onDrop: Tr, children: [
            /* @__PURE__ */ i.jsx(Id.Provider, { value: nf, children: /* @__PURE__ */ i.jsx(kd.Provider, { value: _n, children: /* @__PURE__ */ i.jsxs(
              zu,
              {
                nodes: R,
                edges: _,
                nodeTypes: Sd,
                edgeTypes: Ed,
                onInit: D,
                onNodesChange: Bd,
                onEdgesChange: Kd,
                onNodesDelete: Qd,
                onEdgesDelete: ef,
                onConnect: Ud,
                onConnectStart: Ae ? Zd : void 0,
                onConnectEnd: Ae ? Gd : void 0,
                onReconnect: Ae ? Jd : void 0,
                isValidConnection: ei,
                onDragOver: _r,
                onDragLeave: Dr,
                onDrop: Tr,
                onPaneClick: () => T(null),
                onNodeClick: (x, I) => T(I.id),
                onNodeDragStop: ve ? void 0 : Xd,
                fitView: !0,
                minZoom: 0.2,
                maxZoom: 1.8,
                nodesConnectable: Ae,
                nodesDraggable: !ve,
                selectionOnDrag: !0,
                multiSelectionKeyCode: ["Shift", "Meta", "Control"],
                deleteKeyCode: ve ? null : ["Backspace", "Delete"],
                panActivationKeyCode: null,
                defaultEdgeOptions: { type: "workflow" },
                children: [
                  /* @__PURE__ */ i.jsx(Ou, { gap: 18, size: 1 }),
                  /* @__PURE__ */ i.jsx(Wu, {}),
                  /* @__PURE__ */ i.jsx(Bu, { pannable: !0, zoomable: !0 })
                ]
              }
            ) }) }),
            Ae && R.length === 0 ? /* @__PURE__ */ i.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => Zo(), children: [
              /* @__PURE__ */ i.jsx(dn, { size: 15 }),
              " Add activity"
            ] }) : null,
            L ? /* @__PURE__ */ i.jsx(
              ZN,
              {
                clientX: L.clientX,
                clientY: L.clientY,
                activities: g,
                onPick: tf,
                onClose: () => P(null)
              }
            ) : null
          ] }),
          /* @__PURE__ */ i.jsx(GN, { draft: d, onRepair: of })
        ] })
      ] }),
      zs && !me ? /* @__PURE__ */ i.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Wn,
          "aria-valuemax": Fn,
          "aria-valuenow": _e,
          tabIndex: 0,
          onPointerDown: (x) => Ps("inspector", x),
          onKeyDown: (x) => Rs("inspector", x)
        }
      ) : /* @__PURE__ */ i.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ i.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ i.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ i.jsx(
            no,
            {
              label: "Inspector panel tabs",
              tabs: ni,
              activeTabId: Ws.id,
              onSelect: En
            }
          ),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": ke ? "Expand inspector panel" : "Collapse inspector panel",
                title: ke ? "Expand" : "Collapse",
                onClick: () => Ts("inspector"),
                children: ke ? /* @__PURE__ */ i.jsx(io, { size: 14 }) : /* @__PURE__ */ i.jsx(on, { size: 14 })
              }
            ),
            ke ? null : /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": me === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: me === "inspector" ? "Restore" : "Maximize",
                onClick: () => $s("inspector"),
                children: me === "inspector" ? /* @__PURE__ */ i.jsx(Ks, { size: 14 }) : /* @__PURE__ */ i.jsx(ro, { size: 14 })
              }
            )
          ] })
        ] }),
        zs ? Ws.render() : null
      ] })
    ] })
  ] });
}
function no({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: r
}) {
  return /* @__PURE__ */ i.jsx("div", { className: "wf-panel-tab-list", role: "tablist", "aria-label": e, children: t.map((o) => /* @__PURE__ */ i.jsxs(
    "button",
    {
      type: "button",
      role: "tab",
      "aria-selected": o.id === n,
      className: o.id === n ? "active" : "",
      title: o.title,
      onClick: () => r(o.id),
      children: [
        o.icon ? /* @__PURE__ */ i.jsx("span", { className: "wf-panel-tab-icon", "aria-hidden": "true", children: o.icon }) : null,
        /* @__PURE__ */ i.jsx("span", { children: o.title })
      ]
    },
    o.id
  )) });
}
function Vc(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
function XN({ data: e, selected: t }) {
  const n = e, r = n.runtime, o = !n.suppressFlowPorts, s = o ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], a = qN(n), u = it.useContext(kd)?.({ activityVersionId: n.activityVersionId, activityTypeKey: n.activityTypeKey }) ?? null;
  return /* @__PURE__ */ i.jsxs(
    "div",
    {
      className: ["wf-node", t ? "selected" : "", r ? "wf-node-runtime" : "", r?.hasBlockingIncident ? "faulted" : "", u ? "wf-node-unavailable" : ""].filter(Boolean).join(" "),
      "data-icon": n.icon ?? "activity",
      children: [
        o && n.acceptsInbound ? /* @__PURE__ */ i.jsx(vn, { type: "target", position: ne.Left }) : null,
        u ? /* @__PURE__ */ i.jsx("span", { className: "wf-node-availability", title: `No longer available for new use · ${No(u.state)}`, children: /* @__PURE__ */ i.jsx(oo, { size: 13 }) }) : null,
        /* @__PURE__ */ i.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ i.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: Ns(n.icon) }),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-node-copy", children: [
            /* @__PURE__ */ i.jsx("strong", { children: n.label }),
            a ? /* @__PURE__ */ i.jsx("small", { children: a }) : null
          ] })
        ] }),
        n.childSlots.length > 0 ? /* @__PURE__ */ i.jsxs("span", { className: "wf-node-slot-badge", children: [
          n.childSlots.length,
          " slot",
          n.childSlots.length === 1 ? "" : "s"
        ] }) : null,
        r ? /* @__PURE__ */ i.jsxs("div", { className: "wf-node-runtime-strip", children: [
          r.status ? /* @__PURE__ */ i.jsx(br, { status: r.status, subStatus: r.subStatus }) : null,
          r.incidentCount > 0 ? /* @__PURE__ */ i.jsxs("span", { className: "wf-node-runtime-count", children: [
            r.incidentCount,
            " incident",
            r.incidentCount === 1 ? "" : "s"
          ] }) : null,
          r.faultCount > 0 ? /* @__PURE__ */ i.jsxs("span", { className: "wf-node-runtime-count", children: [
            r.faultCount,
            " faults"
          ] }) : null
        ] }) : null,
        s.map((l, f) => {
          const p = `${(f + 1) / (s.length + 1) * 100}%`;
          return /* @__PURE__ */ i.jsxs(it.Fragment, { children: [
            /* @__PURE__ */ i.jsx("span", { className: "wf-node-port-label", style: { top: p }, children: l.displayName }),
            /* @__PURE__ */ i.jsx(vn, { type: "source", position: ne.Right, id: l.name, style: { top: p } })
          ] }, l.name);
        })
      ]
    }
  );
}
function qN(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((o) => !!o).join(" · ");
}
function YN(e) {
  const {
    id: t,
    sourceX: n,
    sourceY: r,
    targetX: o,
    targetY: s,
    sourcePosition: a,
    targetPosition: c,
    markerEnd: u,
    style: l,
    label: f,
    labelStyle: p
  } = e, d = it.useContext(Id), [h, g] = U(!1), [N, w, m] = yo({ sourceX: n, sourceY: r, targetX: o, targetY: s, sourcePosition: a, targetPosition: c }), C = d?.highlightedEdgeId === t;
  return /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
    /* @__PURE__ */ i.jsx(
      mr,
      {
        id: t,
        path: N,
        markerEnd: u,
        style: {
          ...l,
          strokeWidth: C ? 2.5 : l?.strokeWidth
        },
        label: f,
        labelX: w,
        labelY: m,
        labelStyle: p,
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1)
      }
    ),
    d ? /* @__PURE__ */ i.jsx(gw, { children: /* @__PURE__ */ i.jsxs(
      "div",
      {
        className: ["wf-edge-actions", h ? "visible" : "", C ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${w}px, ${m}px)` },
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1),
        children: [
          /* @__PURE__ */ i.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (y) => d.requestInsertActivity(t, y.clientX, y.clientY), children: /* @__PURE__ */ i.jsx(dn, { size: 12 }) }),
          /* @__PURE__ */ i.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => d.deleteEdge(t), children: /* @__PURE__ */ i.jsx(er, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function ZN({ clientX: e, clientY: t, activities: n, onPick: r, onClose: o }) {
  const [s, a] = U(""), [c, u] = U(0), l = ie(null), f = ie(null), p = de(() => {
    const C = s.trim().toLowerCase(), y = n.filter(LN);
    return C ? y.filter((b) => Ie(b).toLowerCase().includes(C) || b.activityTypeKey.toLowerCase().includes(C) || (b.category ?? "").toLowerCase().includes(C) || (b.description ?? "").toLowerCase().includes(C)) : y;
  }, [n, s]), d = de(() => Ui(p), [p]), h = de(() => d.flatMap((C) => C.activities), [d]);
  Q(() => {
    requestAnimationFrame(() => f.current?.focus());
  }, []), Q(() => {
    const C = (b) => {
      l.current?.contains(b.target) || o();
    }, y = (b) => {
      b.key === "Escape" && o();
    };
    return document.addEventListener("mousedown", C, !0), document.addEventListener("keydown", y), () => {
      document.removeEventListener("mousedown", C, !0), document.removeEventListener("keydown", y);
    };
  }, [o]);
  const g = (C) => {
    if (C.key === "ArrowDown")
      C.preventDefault(), u((y) => Math.min(y + 1, h.length - 1));
    else if (C.key === "ArrowUp")
      C.preventDefault(), u((y) => Math.max(y - 1, 0));
    else if (C.key === "Enter") {
      C.preventDefault();
      const y = h[c];
      y && r(y);
    }
  }, N = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), w = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let m = -1;
  return /* @__PURE__ */ i.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: N, top: w }, onMouseDown: (C) => C.stopPropagation(), onClick: (C) => C.stopPropagation(), children: [
    /* @__PURE__ */ i.jsx(
      "input",
      {
        ref: f,
        type: "search",
        value: s,
        placeholder: "Search activities...",
        "aria-label": "Search activities",
        onChange: (C) => {
          a(C.target.value), u(0);
        },
        onKeyDown: g
      }
    ),
    /* @__PURE__ */ i.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: d.length === 0 ? /* @__PURE__ */ i.jsx("p", { children: "No matching activities." }) : d.map((C) => /* @__PURE__ */ i.jsxs("section", { children: [
      /* @__PURE__ */ i.jsx("h4", { children: C.category }),
      C.activities.map((y) => {
        m += 1;
        const b = m, j = b === c;
        return /* @__PURE__ */ i.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": j,
            className: j ? "active" : "",
            onMouseEnter: () => u(b),
            onClick: () => r(y),
            children: [
              /* @__PURE__ */ i.jsx("strong", { children: Ie(y) }),
              /* @__PURE__ */ i.jsx("small", { children: y.category || y.activityTypeKey })
            ]
          },
          y.activityVersionId
        );
      })
    ] }, C.category)) })
  ] });
}
function GN({ draft: e, onRepair: t }) {
  const n = e.validationErrors;
  if (!n.length)
    return /* @__PURE__ */ i.jsxs("div", { className: "wf-validation ok", children: [
      /* @__PURE__ */ i.jsx(bn, { size: 14 }),
      " No validation errors"
    ] });
  const r = Hb(n), o = new Map(r.map((s) => [s.error, s]));
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-validation-summary", children: [
      /* @__PURE__ */ i.jsx(St, { size: 14 }),
      n.length,
      " validation issue",
      n.length === 1 ? "" : "s",
      r.length > 0 ? /* @__PURE__ */ i.jsxs("span", { className: "wf-validation-variable-count", children: [
        " · ",
        r.length,
        " invalid variable reference",
        r.length === 1 ? "" : "s"
      ] }) : null
    ] }),
    /* @__PURE__ */ i.jsx("ul", { className: "wf-validation-list", children: n.map((s, a) => {
      const c = o.get(s);
      return /* @__PURE__ */ i.jsxs("li", { className: c ? "wf-validation-item repairable" : "wf-validation-item", children: [
        /* @__PURE__ */ i.jsx("span", { className: "wf-validation-message", children: s.message ?? "Validation issue." }),
        c?.path.nodeId ? /* @__PURE__ */ i.jsxs("button", { type: "button", className: "wf-validation-repair", onClick: () => t(c.path.nodeId), children: [
          /* @__PURE__ */ i.jsx(kf, { size: 12 }),
          " Repair"
        ] }) : null
      ] }, a);
    }) })
  ] });
}
function JN({
  testRun: e,
  onOpenDetails: t
}) {
  const n = ks(e);
  return /* @__PURE__ */ i.jsx("div", { className: "wf-test-run-status", "data-state": n ? "rejected" : "accepted", children: /* @__PURE__ */ i.jsxs(
    "button",
    {
      type: "button",
      className: "wf-test-run-trigger",
      onClick: t,
      children: [
        n ? /* @__PURE__ */ i.jsx(St, { size: 16 }) : /* @__PURE__ */ i.jsx(bn, { size: 16 }),
        n ? "Test run rejected" : "Test run dispatched"
      ]
    }
  ) });
}
function QN({ testRun: e, onOpenRun: t }) {
  if (!e)
    return /* @__PURE__ */ i.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "Run the draft to see Runtime Evidence." }) });
  const n = ks(e), r = e.workflowExecutionId;
  return /* @__PURE__ */ i.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ i.jsxs("section", { className: "wf-runtime-card", "data-state": n ? "rejected" : "accepted", children: [
    /* @__PURE__ */ i.jsxs("header", { children: [
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("span", { children: "Latest Test Run" }),
        /* @__PURE__ */ i.jsx("h3", { children: n ? "Rejected by the server" : "Transient run accepted" })
      ] }),
      /* @__PURE__ */ i.jsx(br, { status: e.status, subStatus: e.commandDispatchStatus ?? void 0 })
    ] }),
    /* @__PURE__ */ i.jsx("p", { children: "Ephemeral - not saved, promoted, or published." }),
    n && e.reason ? /* @__PURE__ */ i.jsxs("div", { className: "wf-runtime-reason", children: [
      /* @__PURE__ */ i.jsx(St, { size: 14 }),
      " ",
      e.reason
    ] }) : null,
    /* @__PURE__ */ i.jsxs("dl", { className: "wf-runtime-meta", children: [
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Dispatch" }),
        /* @__PURE__ */ i.jsx("dd", { title: e.commandDispatchStatus ?? e.status, children: e.commandDispatchStatus ?? e.status })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Test Run" }),
        /* @__PURE__ */ i.jsx("dd", { title: e.testRunId, children: e.testRunId })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Artifact" }),
        /* @__PURE__ */ i.jsx("dd", { title: e.artifactId ?? "None", children: e.artifactId ?? "None" })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Run / Instance" }),
        /* @__PURE__ */ i.jsx("dd", { title: r ?? "None", children: r ? /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => t(r), children: r }) : "None" })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Activities" }),
        /* @__PURE__ */ i.jsx("dd", { children: Oc(e.activityCount, "activity") })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ i.jsx("dd", { children: Oc(e.incidentCount, "incident") })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ i.jsx("dd", { title: e.expiresAt ? tt(e.expiresAt) : "None", children: e.expiresAt ? tt(e.expiresAt) : "None" })
      ] })
    ] })
  ] }) });
}
function Oc(e, t) {
  return typeof e != "number" ? "Available on linked Run" : `${e} ${t}${e === 1 ? "" : "s"}`;
}
function Hc(e) {
  return `${Ie(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function Wc(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function ej(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function Pd(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function tj(e) {
  const t = Pd(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function nj(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function Ke(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function rj(e) {
  return zd(Ke(e));
}
function Rd(e, t, n = []) {
  if (!e) return n;
  const r = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: r?.activityTypeKey ?? e.activityVersionId,
    displayName: r ? Ie(r) : void 0
  });
  for (const o of He(e))
    for (const s of o.activities) Rd(s, t, n);
  return n;
}
function Ld(e, t = []) {
  if (!e) return t;
  for (const n of rd(e))
    t.push({ source: n.source, target: n.target, sourcePort: n.sourceHandle ?? void 0, targetPort: n.targetHandle ?? void 0 });
  for (const n of He(e))
    for (const r of n.activities) Ld(r, t);
  return t;
}
function Kn(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function oj(e) {
  return `${e.id}-${zd(JSON.stringify(e.state))}`;
}
function zd(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function ks(e) {
  return e.status.toLowerCase() === "rejected";
}
function ij(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function sj(e, t) {
  const n = e instanceof Error ? e.message : String(e);
  return aj(e, n) ? `Run ${t} was not found.` : n;
}
function aj(e, t) {
  if ((typeof e == "object" && e ? e.response?.status ?? e.status : void 0) === 404 || /\b404\b/.test(t)) return !0;
  try {
    const r = JSON.parse(t);
    return [r.error, r.title, r.detail].some((o) => typeof o == "string" && /not found/i.test(o));
  } catch {
    return /not found/i.test(t);
  }
}
export {
  tj as isConnectEndOverExistingWorkflowNode,
  dj as register,
  nj as resolveConnectEndSource
};
