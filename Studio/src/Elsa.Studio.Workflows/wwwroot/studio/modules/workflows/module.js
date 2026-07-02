import st, { memo as Se, forwardRef as Er, useRef as ie, useEffect as Q, useCallback as le, useContext as po, useMemo as de, useState as q, createContext as Yi, useLayoutEffect as gf, createElement as ji, useId as Kc, lazy as yf, Suspense as mf } from "react";
import { useQuery as qc, useQueryClient as xf, useMutation as wf } from "@tanstack/react-query";
function vf(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var ri = { exports: {} }, Ln = {};
var Ks;
function bf() {
  if (Ks) return Ln;
  Ks = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function n(o, r, s) {
    var a = null;
    if (s !== void 0 && (a = "" + s), r.key !== void 0 && (a = "" + r.key), "key" in r) {
      s = {};
      for (var c in r)
        c !== "key" && (s[c] = r[c]);
    } else s = r;
    return r = s.ref, {
      $$typeof: e,
      type: o,
      key: a,
      ref: r !== void 0 ? r : null,
      props: s
    };
  }
  return Ln.Fragment = t, Ln.jsx = n, Ln.jsxs = n, Ln;
}
var qs;
function Nf() {
  return qs || (qs = 1, ri.exports = bf()), ri.exports;
}
var i = Nf();
function Ee(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, o; n < e.length; n++)
      (o = Ee(e[n])) !== "" && (t += (t && " ") + o);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var jf = { value: () => {
} };
function Cr() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new Xo(n);
}
function Xo(e) {
  this._ = e;
}
function Sf(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
Xo.prototype = Cr.prototype = {
  constructor: Xo,
  on: function(e, t) {
    var n = this._, o = Sf(e + "", n), r, s = -1, a = o.length;
    if (arguments.length < 2) {
      for (; ++s < a; ) if ((r = (e = o[s]).type) && (r = kf(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++s < a; )
      if (r = (e = o[s]).type) n[r] = Us(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = Us(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new Xo(e);
  },
  call: function(e, t) {
    if ((r = arguments.length - 2) > 0) for (var n = new Array(r), o = 0, r, s; o < r; ++o) n[o] = arguments[o + 2];
    if (!this._.hasOwnProperty(e)) throw new Error("unknown type: " + e);
    for (s = this._[e], o = 0, r = s.length; o < r; ++o) s[o].value.apply(t, n);
  },
  apply: function(e, t, n) {
    if (!this._.hasOwnProperty(e)) throw new Error("unknown type: " + e);
    for (var o = this._[e], r = 0, s = o.length; r < s; ++r) o[r].value.apply(t, n);
  }
};
function kf(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function Us(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = jf, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var Si = "http://www.w3.org/1999/xhtml";
const Xs = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: Si,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function Ir(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Xs.hasOwnProperty(t) ? { space: Xs[t], local: e } : e;
}
function Ef(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === Si && t.documentElement.namespaceURI === Si ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Cf(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function Uc(e) {
  var t = Ir(e);
  return (t.local ? Cf : Ef)(t);
}
function If() {
}
function Zi(e) {
  return e == null ? If : function() {
    return this.querySelector(e);
  };
}
function Af(e) {
  typeof e != "function" && (e = Zi(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = o[r] = new Array(a), u, l, f = 0; f < a; ++f)
      (u = s[f]) && (l = e.call(u, u.__data__, f, s)) && ("__data__" in u && (l.__data__ = u.__data__), c[f] = l);
  return new Xe(o, this._parents);
}
function _f(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function Df() {
  return [];
}
function Xc(e) {
  return e == null ? Df : function() {
    return this.querySelectorAll(e);
  };
}
function Mf(e) {
  return function() {
    return _f(e.apply(this, arguments));
  };
}
function Tf(e) {
  typeof e == "function" ? e = Mf(e) : e = Xc(e);
  for (var t = this._groups, n = t.length, o = [], r = [], s = 0; s < n; ++s)
    for (var a = t[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && (o.push(e.call(u, u.__data__, l, a)), r.push(u));
  return new Xe(o, r);
}
function Yc(e) {
  return function() {
    return this.matches(e);
  };
}
function Zc(e) {
  return function(t) {
    return t.matches(e);
  };
}
var $f = Array.prototype.find;
function Pf(e) {
  return function() {
    return $f.call(this.children, e);
  };
}
function zf() {
  return this.firstElementChild;
}
function Rf(e) {
  return this.select(e == null ? zf : Pf(typeof e == "function" ? e : Zc(e)));
}
var Lf = Array.prototype.filter;
function Vf() {
  return Array.from(this.children);
}
function Of(e) {
  return function() {
    return Lf.call(this.children, e);
  };
}
function Hf(e) {
  return this.selectAll(e == null ? Vf : Of(typeof e == "function" ? e : Zc(e)));
}
function Wf(e) {
  typeof e != "function" && (e = Yc(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = o[r] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new Xe(o, this._parents);
}
function Gc(e) {
  return new Array(e.length);
}
function Bf() {
  return new Xe(this._enter || this._groups.map(Gc), this._parents);
}
function ir(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
ir.prototype = {
  constructor: ir,
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
function Ff(e) {
  return function() {
    return e;
  };
}
function Kf(e, t, n, o, r, s) {
  for (var a = 0, c, u = t.length, l = s.length; a < l; ++a)
    (c = t[a]) ? (c.__data__ = s[a], o[a] = c) : n[a] = new ir(e, s[a]);
  for (; a < u; ++a)
    (c = t[a]) && (r[a] = c);
}
function qf(e, t, n, o, r, s, a) {
  var c, u, l = /* @__PURE__ */ new Map(), f = t.length, p = s.length, d = new Array(f), h;
  for (c = 0; c < f; ++c)
    (u = t[c]) && (d[c] = h = a.call(u, u.__data__, c, t) + "", l.has(h) ? r[c] = u : l.set(h, u));
  for (c = 0; c < p; ++c)
    h = a.call(e, s[c], c, s) + "", (u = l.get(h)) ? (o[c] = u, u.__data__ = s[c], l.delete(h)) : n[c] = new ir(e, s[c]);
  for (c = 0; c < f; ++c)
    (u = t[c]) && l.get(d[c]) === u && (r[c] = u);
}
function Uf(e) {
  return e.__data__;
}
function Xf(e, t) {
  if (!arguments.length) return Array.from(this, Uf);
  var n = t ? qf : Kf, o = this._parents, r = this._groups;
  typeof e != "function" && (e = Ff(e));
  for (var s = r.length, a = new Array(s), c = new Array(s), u = new Array(s), l = 0; l < s; ++l) {
    var f = o[l], p = r[l], d = p.length, h = Yf(e.call(f, f && f.__data__, l, o)), g = h.length, N = c[l] = new Array(g), w = a[l] = new Array(g), m = u[l] = new Array(d);
    n(f, p, N, w, m, h, t);
    for (var E = 0, y = 0, b, j; E < g; ++E)
      if (b = N[E]) {
        for (E >= y && (y = E + 1); !(j = w[y]) && ++y < g; ) ;
        b._next = j || null;
      }
  }
  return a = new Xe(a, o), a._enter = c, a._exit = u, a;
}
function Yf(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function Zf() {
  return new Xe(this._exit || this._groups.map(Gc), this._parents);
}
function Gf(e, t, n) {
  var o = this.enter(), r = this, s = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? s.remove() : n(s), o && r ? o.merge(r).order() : r;
}
function Jf(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, s = o.length, a = Math.min(r, s), c = new Array(r), u = 0; u < a; ++u)
    for (var l = n[u], f = o[u], p = l.length, d = c[u] = new Array(p), h, g = 0; g < p; ++g)
      (h = l[g] || f[g]) && (d[g] = h);
  for (; u < r; ++u)
    c[u] = n[u];
  return new Xe(c, this._parents);
}
function Qf() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, s = o[r], a; --r >= 0; )
      (a = o[r]) && (s && a.compareDocumentPosition(s) ^ 4 && s.parentNode.insertBefore(a, s), s = a);
  return this;
}
function ep(e) {
  e || (e = tp);
  function t(p, d) {
    return p && d ? e(p.__data__, d.__data__) : !p - !d;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), s = 0; s < o; ++s) {
    for (var a = n[s], c = a.length, u = r[s] = new Array(c), l, f = 0; f < c; ++f)
      (l = a[f]) && (u[f] = l);
    u.sort(t);
  }
  return new Xe(r, this._parents).order();
}
function tp(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function np() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function op() {
  return Array.from(this);
}
function rp() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, s = o.length; r < s; ++r) {
      var a = o[r];
      if (a) return a;
    }
  return null;
}
function ip() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function sp() {
  return !this.node();
}
function ap(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], s = 0, a = r.length, c; s < a; ++s)
      (c = r[s]) && e.call(c, c.__data__, s, r);
  return this;
}
function cp(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function lp(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function up(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function dp(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function fp(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function pp(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function hp(e, t) {
  var n = Ir(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? lp : cp : typeof t == "function" ? n.local ? pp : fp : n.local ? dp : up)(n, t));
}
function Jc(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function gp(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function yp(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function mp(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function xp(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? gp : typeof t == "function" ? mp : yp)(e, t, n ?? "")) : fn(this.node(), e);
}
function fn(e, t) {
  return e.style.getPropertyValue(t) || Jc(e).getComputedStyle(e, null).getPropertyValue(t);
}
function wp(e) {
  return function() {
    delete this[e];
  };
}
function vp(e, t) {
  return function() {
    this[e] = t;
  };
}
function bp(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function Np(e, t) {
  return arguments.length > 1 ? this.each((t == null ? wp : typeof t == "function" ? bp : vp)(e, t)) : this.node()[e];
}
function Qc(e) {
  return e.trim().split(/^|\s+/);
}
function Gi(e) {
  return e.classList || new el(e);
}
function el(e) {
  this._node = e, this._names = Qc(e.getAttribute("class") || "");
}
el.prototype = {
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
function tl(e, t) {
  for (var n = Gi(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function nl(e, t) {
  for (var n = Gi(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function jp(e) {
  return function() {
    tl(this, e);
  };
}
function Sp(e) {
  return function() {
    nl(this, e);
  };
}
function kp(e, t) {
  return function() {
    (t.apply(this, arguments) ? tl : nl)(this, e);
  };
}
function Ep(e, t) {
  var n = Qc(e + "");
  if (arguments.length < 2) {
    for (var o = Gi(this.node()), r = -1, s = n.length; ++r < s; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? kp : t ? jp : Sp)(n, t));
}
function Cp() {
  this.textContent = "";
}
function Ip(e) {
  return function() {
    this.textContent = e;
  };
}
function Ap(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function _p(e) {
  return arguments.length ? this.each(e == null ? Cp : (typeof e == "function" ? Ap : Ip)(e)) : this.node().textContent;
}
function Dp() {
  this.innerHTML = "";
}
function Mp(e) {
  return function() {
    this.innerHTML = e;
  };
}
function Tp(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function $p(e) {
  return arguments.length ? this.each(e == null ? Dp : (typeof e == "function" ? Tp : Mp)(e)) : this.node().innerHTML;
}
function Pp() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function zp() {
  return this.each(Pp);
}
function Rp() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Lp() {
  return this.each(Rp);
}
function Vp(e) {
  var t = typeof e == "function" ? e : Uc(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Op() {
  return null;
}
function Hp(e, t) {
  var n = typeof e == "function" ? e : Uc(e), o = t == null ? Op : typeof t == "function" ? t : Zi(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function Wp() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Bp() {
  return this.each(Wp);
}
function Fp() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Kp() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function qp(e) {
  return this.select(e ? Kp : Fp);
}
function Up(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function Xp(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function Yp(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function Zp(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, s; n < r; ++n)
        s = t[n], (!e.type || s.type === e.type) && s.name === e.name ? this.removeEventListener(s.type, s.listener, s.options) : t[++o] = s;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function Gp(e, t, n) {
  return function() {
    var o = this.__on, r, s = Xp(t);
    if (o) {
      for (var a = 0, c = o.length; a < c; ++a)
        if ((r = o[a]).type === e.type && r.name === e.name) {
          this.removeEventListener(r.type, r.listener, r.options), this.addEventListener(r.type, r.listener = s, r.options = n), r.value = t;
          return;
        }
    }
    this.addEventListener(e.type, s, n), r = { type: e.type, name: e.name, value: t, listener: s, options: n }, o ? o.push(r) : this.__on = [r];
  };
}
function Jp(e, t, n) {
  var o = Yp(e + ""), r, s = o.length, a;
  if (arguments.length < 2) {
    var c = this.node().__on;
    if (c) {
      for (var u = 0, l = c.length, f; u < l; ++u)
        for (r = 0, f = c[u]; r < s; ++r)
          if ((a = o[r]).type === f.type && a.name === f.name)
            return f.value;
    }
    return;
  }
  for (c = t ? Gp : Zp, r = 0; r < s; ++r) this.each(c(o[r], t, n));
  return this;
}
function ol(e, t, n) {
  var o = Jc(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function Qp(e, t) {
  return function() {
    return ol(this, e, t);
  };
}
function eh(e, t) {
  return function() {
    return ol(this, e, t.apply(this, arguments));
  };
}
function th(e, t) {
  return this.each((typeof t == "function" ? eh : Qp)(e, t));
}
function* nh() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, s = o.length, a; r < s; ++r)
      (a = o[r]) && (yield a);
}
var rl = [null];
function Xe(e, t) {
  this._groups = e, this._parents = t;
}
function ho() {
  return new Xe([[document.documentElement]], rl);
}
function oh() {
  return this;
}
Xe.prototype = ho.prototype = {
  constructor: Xe,
  select: Af,
  selectAll: Tf,
  selectChild: Rf,
  selectChildren: Hf,
  filter: Wf,
  data: Xf,
  enter: Bf,
  exit: Zf,
  join: Gf,
  merge: Jf,
  selection: oh,
  order: Qf,
  sort: ep,
  call: np,
  nodes: op,
  node: rp,
  size: ip,
  empty: sp,
  each: ap,
  attr: hp,
  style: xp,
  property: Np,
  classed: Ep,
  text: _p,
  html: $p,
  raise: zp,
  lower: Lp,
  append: Vp,
  insert: Hp,
  remove: Bp,
  clone: qp,
  datum: Up,
  on: Jp,
  dispatch: th,
  [Symbol.iterator]: nh
};
function Ue(e) {
  return typeof e == "string" ? new Xe([[document.querySelector(e)]], [document.documentElement]) : new Xe([[e]], rl);
}
function rh(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Je(e, t) {
  if (e = rh(e), t === void 0 && (t = e.currentTarget), t) {
    var n = t.ownerSVGElement || t;
    if (n.createSVGPoint) {
      var o = n.createSVGPoint();
      return o.x = e.clientX, o.y = e.clientY, o = o.matrixTransform(t.getScreenCTM().inverse()), [o.x, o.y];
    }
    if (t.getBoundingClientRect) {
      var r = t.getBoundingClientRect();
      return [e.clientX - r.left - t.clientLeft, e.clientY - r.top - t.clientTop];
    }
  }
  return [e.pageX, e.pageY];
}
const ih = { passive: !1 }, to = { capture: !0, passive: !1 };
function ii(e) {
  e.stopImmediatePropagation();
}
function ln(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function il(e) {
  var t = e.document.documentElement, n = Ue(e).on("dragstart.drag", ln, to);
  "onselectstart" in t ? n.on("selectstart.drag", ln, to) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function sl(e, t) {
  var n = e.document.documentElement, o = Ue(e).on("dragstart.drag", null);
  t && (o.on("click.drag", ln, to), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const Ro = (e) => () => e;
function ki(e, {
  sourceEvent: t,
  subject: n,
  target: o,
  identifier: r,
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
    target: { value: o, enumerable: !0, configurable: !0 },
    identifier: { value: r, enumerable: !0, configurable: !0 },
    active: { value: s, enumerable: !0, configurable: !0 },
    x: { value: a, enumerable: !0, configurable: !0 },
    y: { value: c, enumerable: !0, configurable: !0 },
    dx: { value: u, enumerable: !0, configurable: !0 },
    dy: { value: l, enumerable: !0, configurable: !0 },
    _: { value: f }
  });
}
ki.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function sh(e) {
  return !e.ctrlKey && !e.button;
}
function ah() {
  return this.parentNode;
}
function ch(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function lh() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function al() {
  var e = sh, t = ah, n = ch, o = lh, r = {}, s = Cr("start", "drag", "end"), a = 0, c, u, l, f, p = 0;
  function d(b) {
    b.on("mousedown.drag", h).filter(o).on("touchstart.drag", w).on("touchmove.drag", m, ih).on("touchend.drag touchcancel.drag", E).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function h(b, j) {
    if (!(f || !e.call(this, b, j))) {
      var v = y(this, t.call(this, b, j), b, j, "mouse");
      v && (Ue(b.view).on("mousemove.drag", g, to).on("mouseup.drag", N, to), il(b.view), ii(b), l = !1, c = b.clientX, u = b.clientY, v("start", b));
    }
  }
  function g(b) {
    if (ln(b), !l) {
      var j = b.clientX - c, v = b.clientY - u;
      l = j * j + v * v > p;
    }
    r.mouse("drag", b);
  }
  function N(b) {
    Ue(b.view).on("mousemove.drag mouseup.drag", null), sl(b.view, l), ln(b), r.mouse("end", b);
  }
  function w(b, j) {
    if (e.call(this, b, j)) {
      var v = b.changedTouches, S = t.call(this, b, j), _ = v.length, $, V;
      for ($ = 0; $ < _; ++$)
        (V = y(this, S, b, j, v[$].identifier, v[$])) && (ii(b), V("start", b, v[$]));
    }
  }
  function m(b) {
    var j = b.changedTouches, v = j.length, S, _;
    for (S = 0; S < v; ++S)
      (_ = r[j[S].identifier]) && (ln(b), _("drag", b, j[S]));
  }
  function E(b) {
    var j = b.changedTouches, v = j.length, S, _;
    for (f && clearTimeout(f), f = setTimeout(function() {
      f = null;
    }, 500), S = 0; S < v; ++S)
      (_ = r[j[S].identifier]) && (ii(b), _("end", b, j[S]));
  }
  function y(b, j, v, S, _, $) {
    var V = s.copy(), D = Je($ || v, j), A, L, k;
    if ((k = n.call(b, new ki("beforestart", {
      sourceEvent: v,
      target: d,
      identifier: _,
      active: a,
      x: D[0],
      y: D[1],
      dx: 0,
      dy: 0,
      dispatch: V
    }), S)) != null)
      return A = k.x - D[0] || 0, L = k.y - D[1] || 0, function M(I, T, z) {
        var P = D, K;
        switch (I) {
          case "start":
            r[_] = M, K = a++;
            break;
          case "end":
            delete r[_], --a;
          // falls through
          case "drag":
            D = Je(z || T, j), K = a;
            break;
        }
        V.call(
          I,
          b,
          new ki(I, {
            sourceEvent: T,
            subject: k,
            target: d,
            identifier: _,
            active: K,
            x: D[0] + A,
            y: D[1] + L,
            dx: D[0] - P[0],
            dy: D[1] - P[1],
            dispatch: V
          }),
          S
        );
      };
  }
  return d.filter = function(b) {
    return arguments.length ? (e = typeof b == "function" ? b : Ro(!!b), d) : e;
  }, d.container = function(b) {
    return arguments.length ? (t = typeof b == "function" ? b : Ro(b), d) : t;
  }, d.subject = function(b) {
    return arguments.length ? (n = typeof b == "function" ? b : Ro(b), d) : n;
  }, d.touchable = function(b) {
    return arguments.length ? (o = typeof b == "function" ? b : Ro(!!b), d) : o;
  }, d.on = function() {
    var b = s.on.apply(s, arguments);
    return b === s ? d : b;
  }, d.clickDistance = function(b) {
    return arguments.length ? (p = (b = +b) * b, d) : Math.sqrt(p);
  }, d;
}
function Ji(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function cl(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function go() {
}
var no = 0.7, sr = 1 / no, un = "\\s*([+-]?\\d+)\\s*", oo = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", it = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", uh = /^#([0-9a-f]{3,8})$/, dh = new RegExp(`^rgb\\(${un},${un},${un}\\)$`), fh = new RegExp(`^rgb\\(${it},${it},${it}\\)$`), ph = new RegExp(`^rgba\\(${un},${un},${un},${oo}\\)$`), hh = new RegExp(`^rgba\\(${it},${it},${it},${oo}\\)$`), gh = new RegExp(`^hsl\\(${oo},${it},${it}\\)$`), yh = new RegExp(`^hsla\\(${oo},${it},${it},${oo}\\)$`), Ys = {
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
Ji(go, Lt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Zs,
  // Deprecated! Use color.formatHex.
  formatHex: Zs,
  formatHex8: mh,
  formatHsl: xh,
  formatRgb: Gs,
  toString: Gs
});
function Zs() {
  return this.rgb().formatHex();
}
function mh() {
  return this.rgb().formatHex8();
}
function xh() {
  return ll(this).formatHsl();
}
function Gs() {
  return this.rgb().formatRgb();
}
function Lt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = uh.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? Js(t) : n === 3 ? new He(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? Lo(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? Lo(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = dh.exec(e)) ? new He(t[1], t[2], t[3], 1) : (t = fh.exec(e)) ? new He(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = ph.exec(e)) ? Lo(t[1], t[2], t[3], t[4]) : (t = hh.exec(e)) ? Lo(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = gh.exec(e)) ? ta(t[1], t[2] / 100, t[3] / 100, 1) : (t = yh.exec(e)) ? ta(t[1], t[2] / 100, t[3] / 100, t[4]) : Ys.hasOwnProperty(e) ? Js(Ys[e]) : e === "transparent" ? new He(NaN, NaN, NaN, 0) : null;
}
function Js(e) {
  return new He(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function Lo(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new He(e, t, n, o);
}
function wh(e) {
  return e instanceof go || (e = Lt(e)), e ? (e = e.rgb(), new He(e.r, e.g, e.b, e.opacity)) : new He();
}
function Ei(e, t, n, o) {
  return arguments.length === 1 ? wh(e) : new He(e, t, n, o ?? 1);
}
function He(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
Ji(He, Ei, cl(go, {
  brighter(e) {
    return e = e == null ? sr : Math.pow(sr, e), new He(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? no : Math.pow(no, e), new He(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new He(Pt(this.r), Pt(this.g), Pt(this.b), ar(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: Qs,
  // Deprecated! Use color.formatHex.
  formatHex: Qs,
  formatHex8: vh,
  formatRgb: ea,
  toString: ea
}));
function Qs() {
  return `#${$t(this.r)}${$t(this.g)}${$t(this.b)}`;
}
function vh() {
  return `#${$t(this.r)}${$t(this.g)}${$t(this.b)}${$t((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function ea() {
  const e = ar(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${Pt(this.r)}, ${Pt(this.g)}, ${Pt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function ar(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function Pt(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function $t(e) {
  return e = Pt(e), (e < 16 ? "0" : "") + e.toString(16);
}
function ta(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Qe(e, t, n, o);
}
function ll(e) {
  if (e instanceof Qe) return new Qe(e.h, e.s, e.l, e.opacity);
  if (e instanceof go || (e = Lt(e)), !e) return new Qe();
  if (e instanceof Qe) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), s = Math.max(t, n, o), a = NaN, c = s - r, u = (s + r) / 2;
  return c ? (t === s ? a = (n - o) / c + (n < o) * 6 : n === s ? a = (o - t) / c + 2 : a = (t - n) / c + 4, c /= u < 0.5 ? s + r : 2 - s - r, a *= 60) : c = u > 0 && u < 1 ? 0 : a, new Qe(a, c, u, e.opacity);
}
function bh(e, t, n, o) {
  return arguments.length === 1 ? ll(e) : new Qe(e, t, n, o ?? 1);
}
function Qe(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
Ji(Qe, bh, cl(go, {
  brighter(e) {
    return e = e == null ? sr : Math.pow(sr, e), new Qe(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? no : Math.pow(no, e), new Qe(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new He(
      si(e >= 240 ? e - 240 : e + 120, r, o),
      si(e, r, o),
      si(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new Qe(na(this.h), Vo(this.s), Vo(this.l), ar(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = ar(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${na(this.h)}, ${Vo(this.s) * 100}%, ${Vo(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function na(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function Vo(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function si(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const Qi = (e) => () => e;
function Nh(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function jh(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function Sh(e) {
  return (e = +e) == 1 ? ul : function(t, n) {
    return n - t ? jh(t, n, e) : Qi(isNaN(t) ? n : t);
  };
}
function ul(e, t) {
  var n = t - e;
  return n ? Nh(e, n) : Qi(isNaN(e) ? t : e);
}
const cr = (function e(t) {
  var n = Sh(t);
  function o(r, s) {
    var a = n((r = Ei(r)).r, (s = Ei(s)).r), c = n(r.g, s.g), u = n(r.b, s.b), l = ul(r.opacity, s.opacity);
    return function(f) {
      return r.r = a(f), r.g = c(f), r.b = u(f), r.opacity = l(f), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function kh(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(s) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - s) + t[r] * s;
    return o;
  };
}
function Eh(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Ch(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), s = new Array(n), a;
  for (a = 0; a < o; ++a) r[a] = Zn(e[a], t[a]);
  for (; a < n; ++a) s[a] = t[a];
  return function(c) {
    for (a = 0; a < o; ++a) s[a] = r[a](c);
    return s;
  };
}
function Ih(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(o) {
    return n.setTime(e * (1 - o) + t * o), n;
  };
}
function rt(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function Ah(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = Zn(e[r], t[r]) : o[r] = t[r];
  return function(s) {
    for (r in n) o[r] = n[r](s);
    return o;
  };
}
var Ci = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, ai = new RegExp(Ci.source, "g");
function _h(e) {
  return function() {
    return e;
  };
}
function Dh(e) {
  return function(t) {
    return e(t) + "";
  };
}
function dl(e, t) {
  var n = Ci.lastIndex = ai.lastIndex = 0, o, r, s, a = -1, c = [], u = [];
  for (e = e + "", t = t + ""; (o = Ci.exec(e)) && (r = ai.exec(t)); )
    (s = r.index) > n && (s = t.slice(n, s), c[a] ? c[a] += s : c[++a] = s), (o = o[0]) === (r = r[0]) ? c[a] ? c[a] += r : c[++a] = r : (c[++a] = null, u.push({ i: a, x: rt(o, r) })), n = ai.lastIndex;
  return n < t.length && (s = t.slice(n), c[a] ? c[a] += s : c[++a] = s), c.length < 2 ? u[0] ? Dh(u[0].x) : _h(t) : (t = u.length, function(l) {
    for (var f = 0, p; f < t; ++f) c[(p = u[f]).i] = p.x(l);
    return c.join("");
  });
}
function Zn(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? Qi(t) : (n === "number" ? rt : n === "string" ? (o = Lt(t)) ? (t = o, cr) : dl : t instanceof Lt ? cr : t instanceof Date ? Ih : Eh(t) ? kh : Array.isArray(t) ? Ch : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Ah : rt)(e, t);
}
var oa = 180 / Math.PI, Ii = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function fl(e, t, n, o, r, s) {
  var a, c, u;
  return (a = Math.sqrt(e * e + t * t)) && (e /= a, t /= a), (u = e * n + t * o) && (n -= e * u, o -= t * u), (c = Math.sqrt(n * n + o * o)) && (n /= c, o /= c, u /= c), e * o < t * n && (e = -e, t = -t, u = -u, a = -a), {
    translateX: r,
    translateY: s,
    rotate: Math.atan2(t, e) * oa,
    skewX: Math.atan(u) * oa,
    scaleX: a,
    scaleY: c
  };
}
var Oo;
function Mh(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Ii : fl(t.a, t.b, t.c, t.d, t.e, t.f);
}
function Th(e) {
  return e == null || (Oo || (Oo = document.createElementNS("http://www.w3.org/2000/svg", "g")), Oo.setAttribute("transform", e), !(e = Oo.transform.baseVal.consolidate())) ? Ii : (e = e.matrix, fl(e.a, e.b, e.c, e.d, e.e, e.f));
}
function pl(e, t, n, o) {
  function r(l) {
    return l.length ? l.pop() + " " : "";
  }
  function s(l, f, p, d, h, g) {
    if (l !== p || f !== d) {
      var N = h.push("translate(", null, t, null, n);
      g.push({ i: N - 4, x: rt(l, p) }, { i: N - 2, x: rt(f, d) });
    } else (p || d) && h.push("translate(" + p + t + d + n);
  }
  function a(l, f, p, d) {
    l !== f ? (l - f > 180 ? f += 360 : f - l > 180 && (l += 360), d.push({ i: p.push(r(p) + "rotate(", null, o) - 2, x: rt(l, f) })) : f && p.push(r(p) + "rotate(" + f + o);
  }
  function c(l, f, p, d) {
    l !== f ? d.push({ i: p.push(r(p) + "skewX(", null, o) - 2, x: rt(l, f) }) : f && p.push(r(p) + "skewX(" + f + o);
  }
  function u(l, f, p, d, h, g) {
    if (l !== p || f !== d) {
      var N = h.push(r(h) + "scale(", null, ",", null, ")");
      g.push({ i: N - 4, x: rt(l, p) }, { i: N - 2, x: rt(f, d) });
    } else (p !== 1 || d !== 1) && h.push(r(h) + "scale(" + p + "," + d + ")");
  }
  return function(l, f) {
    var p = [], d = [];
    return l = e(l), f = e(f), s(l.translateX, l.translateY, f.translateX, f.translateY, p, d), a(l.rotate, f.rotate, p, d), c(l.skewX, f.skewX, p, d), u(l.scaleX, l.scaleY, f.scaleX, f.scaleY, p, d), l = f = null, function(h) {
      for (var g = -1, N = d.length, w; ++g < N; ) p[(w = d[g]).i] = w.x(h);
      return p.join("");
    };
  };
}
var $h = pl(Mh, "px, ", "px)", "deg)"), Ph = pl(Th, ", ", ")", ")"), zh = 1e-12;
function ra(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Rh(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Lh(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const Yo = (function e(t, n, o) {
  function r(s, a) {
    var c = s[0], u = s[1], l = s[2], f = a[0], p = a[1], d = a[2], h = f - c, g = p - u, N = h * h + g * g, w, m;
    if (N < zh)
      m = Math.log(d / l) / t, w = function(S) {
        return [
          c + S * h,
          u + S * g,
          l * Math.exp(t * S * m)
        ];
      };
    else {
      var E = Math.sqrt(N), y = (d * d - l * l + o * N) / (2 * l * n * E), b = (d * d - l * l - o * N) / (2 * d * n * E), j = Math.log(Math.sqrt(y * y + 1) - y), v = Math.log(Math.sqrt(b * b + 1) - b);
      m = (v - j) / t, w = function(S) {
        var _ = S * m, $ = ra(j), V = l / (n * E) * ($ * Lh(t * _ + j) - Rh(j));
        return [
          c + V * h,
          u + V * g,
          l * $ / ra(t * _ + j)
        ];
      };
    }
    return w.duration = m * 1e3 * t / Math.SQRT2, w;
  }
  return r.rho = function(s) {
    var a = Math.max(1e-3, +s), c = a * a, u = c * c;
    return e(a, c, u);
  }, r;
})(Math.SQRT2, 2, 4);
var pn = 0, Un = 0, Vn = 0, hl = 1e3, lr, Xn, ur = 0, Vt = 0, Ar = 0, ro = typeof performance == "object" && performance.now ? performance : Date, gl = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function es() {
  return Vt || (gl(Vh), Vt = ro.now() + Ar);
}
function Vh() {
  Vt = 0;
}
function dr() {
  this._call = this._time = this._next = null;
}
dr.prototype = yl.prototype = {
  constructor: dr,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? es() : +n) + (t == null ? 0 : +t), !this._next && Xn !== this && (Xn ? Xn._next = this : lr = this, Xn = this), this._call = e, this._time = n, Ai();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Ai());
  }
};
function yl(e, t, n) {
  var o = new dr();
  return o.restart(e, t, n), o;
}
function Oh() {
  es(), ++pn;
  for (var e = lr, t; e; )
    (t = Vt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --pn;
}
function ia() {
  Vt = (ur = ro.now()) + Ar, pn = Un = 0;
  try {
    Oh();
  } finally {
    pn = 0, Wh(), Vt = 0;
  }
}
function Hh() {
  var e = ro.now(), t = e - ur;
  t > hl && (Ar -= t, ur = e);
}
function Wh() {
  for (var e, t = lr, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : lr = n);
  Xn = e, Ai(o);
}
function Ai(e) {
  if (!pn) {
    Un && (Un = clearTimeout(Un));
    var t = e - Vt;
    t > 24 ? (e < 1 / 0 && (Un = setTimeout(ia, e - ro.now() - Ar)), Vn && (Vn = clearInterval(Vn))) : (Vn || (ur = ro.now(), Vn = setInterval(Hh, hl)), pn = 1, gl(ia));
  }
}
function sa(e, t, n) {
  var o = new dr();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var Bh = Cr("start", "end", "cancel", "interrupt"), Fh = [], ml = 0, aa = 1, _i = 2, Zo = 3, ca = 4, Di = 5, Go = 6;
function _r(e, t, n, o, r, s) {
  var a = e.__transition;
  if (!a) e.__transition = {};
  else if (n in a) return;
  Kh(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: Bh,
    tween: Fh,
    time: s.time,
    delay: s.delay,
    duration: s.duration,
    ease: s.ease,
    timer: null,
    state: ml
  });
}
function ts(e, t) {
  var n = ot(e, t);
  if (n.state > ml) throw new Error("too late; already scheduled");
  return n;
}
function ct(e, t) {
  var n = ot(e, t);
  if (n.state > Zo) throw new Error("too late; already running");
  return n;
}
function ot(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function Kh(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = yl(s, 0, n.time);
  function s(l) {
    n.state = aa, n.timer.restart(a, n.delay, n.time), n.delay <= l && a(l - n.delay);
  }
  function a(l) {
    var f, p, d, h;
    if (n.state !== aa) return u();
    for (f in o)
      if (h = o[f], h.name === n.name) {
        if (h.state === Zo) return sa(a);
        h.state === ca ? (h.state = Go, h.timer.stop(), h.on.call("interrupt", e, e.__data__, h.index, h.group), delete o[f]) : +f < t && (h.state = Go, h.timer.stop(), h.on.call("cancel", e, e.__data__, h.index, h.group), delete o[f]);
      }
    if (sa(function() {
      n.state === Zo && (n.state = ca, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = _i, n.on.call("start", e, e.__data__, n.index, n.group), n.state === _i) {
      for (n.state = Zo, r = new Array(d = n.tween.length), f = 0, p = -1; f < d; ++f)
        (h = n.tween[f].value.call(e, e.__data__, n.index, n.group)) && (r[++p] = h);
      r.length = p + 1;
    }
  }
  function c(l) {
    for (var f = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(u), n.state = Di, 1), p = -1, d = r.length; ++p < d; )
      r[p].call(e, f);
    n.state === Di && (n.on.call("end", e, e.__data__, n.index, n.group), u());
  }
  function u() {
    n.state = Go, n.timer.stop(), delete o[t];
    for (var l in o) return;
    delete e.__transition;
  }
}
function Jo(e, t) {
  var n = e.__transition, o, r, s = !0, a;
  if (n) {
    t = t == null ? null : t + "";
    for (a in n) {
      if ((o = n[a]).name !== t) {
        s = !1;
        continue;
      }
      r = o.state > _i && o.state < Di, o.state = Go, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[a];
    }
    s && delete e.__transition;
  }
}
function qh(e) {
  return this.each(function() {
    Jo(this, e);
  });
}
function Uh(e, t) {
  var n, o;
  return function() {
    var r = ct(this, e), s = r.tween;
    if (s !== n) {
      o = n = s;
      for (var a = 0, c = o.length; a < c; ++a)
        if (o[a].name === t) {
          o = o.slice(), o.splice(a, 1);
          break;
        }
    }
    r.tween = o;
  };
}
function Xh(e, t, n) {
  var o, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var s = ct(this, e), a = s.tween;
    if (a !== o) {
      r = (o = a).slice();
      for (var c = { name: t, value: n }, u = 0, l = r.length; u < l; ++u)
        if (r[u].name === t) {
          r[u] = c;
          break;
        }
      u === l && r.push(c);
    }
    s.tween = r;
  };
}
function Yh(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = ot(this.node(), n).tween, r = 0, s = o.length, a; r < s; ++r)
      if ((a = o[r]).name === e)
        return a.value;
    return null;
  }
  return this.each((t == null ? Uh : Xh)(n, e, t));
}
function ns(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = ct(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return ot(r, o).value[t];
  };
}
function xl(e, t) {
  var n;
  return (typeof t == "number" ? rt : t instanceof Lt ? cr : (n = Lt(t)) ? (t = n, cr) : dl)(e, t);
}
function Zh(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Gh(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Jh(e, t, n) {
  var o, r = n + "", s;
  return function() {
    var a = this.getAttribute(e);
    return a === r ? null : a === o ? s : s = t(o = a, n);
  };
}
function Qh(e, t, n) {
  var o, r = n + "", s;
  return function() {
    var a = this.getAttributeNS(e.space, e.local);
    return a === r ? null : a === o ? s : s = t(o = a, n);
  };
}
function eg(e, t, n) {
  var o, r, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttribute(e) : (a = this.getAttribute(e), u = c + "", a === u ? null : a === o && u === r ? s : (r = u, s = t(o = a, c)));
  };
}
function tg(e, t, n) {
  var o, r, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (a = this.getAttributeNS(e.space, e.local), u = c + "", a === u ? null : a === o && u === r ? s : (r = u, s = t(o = a, c)));
  };
}
function ng(e, t) {
  var n = Ir(e), o = n === "transform" ? Ph : xl;
  return this.attrTween(e, typeof t == "function" ? (n.local ? tg : eg)(n, o, ns(this, "attr." + e, t)) : t == null ? (n.local ? Gh : Zh)(n) : (n.local ? Qh : Jh)(n, o, t));
}
function og(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function rg(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function ig(e, t) {
  var n, o;
  function r() {
    var s = t.apply(this, arguments);
    return s !== o && (n = (o = s) && rg(e, s)), n;
  }
  return r._value = t, r;
}
function sg(e, t) {
  var n, o;
  function r() {
    var s = t.apply(this, arguments);
    return s !== o && (n = (o = s) && og(e, s)), n;
  }
  return r._value = t, r;
}
function ag(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = Ir(e);
  return this.tween(n, (o.local ? ig : sg)(o, t));
}
function cg(e, t) {
  return function() {
    ts(this, e).delay = +t.apply(this, arguments);
  };
}
function lg(e, t) {
  return t = +t, function() {
    ts(this, e).delay = t;
  };
}
function ug(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? cg : lg)(t, e)) : ot(this.node(), t).delay;
}
function dg(e, t) {
  return function() {
    ct(this, e).duration = +t.apply(this, arguments);
  };
}
function fg(e, t) {
  return t = +t, function() {
    ct(this, e).duration = t;
  };
}
function pg(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? dg : fg)(t, e)) : ot(this.node(), t).duration;
}
function hg(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    ct(this, e).ease = t;
  };
}
function gg(e) {
  var t = this._id;
  return arguments.length ? this.each(hg(t, e)) : ot(this.node(), t).ease;
}
function yg(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    ct(this, e).ease = n;
  };
}
function mg(e) {
  if (typeof e != "function") throw new Error();
  return this.each(yg(this._id, e));
}
function xg(e) {
  typeof e != "function" && (e = Yc(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = o[r] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new ht(o, this._parents, this._name, this._id);
}
function wg(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, s = Math.min(o, r), a = new Array(o), c = 0; c < s; ++c)
    for (var u = t[c], l = n[c], f = u.length, p = a[c] = new Array(f), d, h = 0; h < f; ++h)
      (d = u[h] || l[h]) && (p[h] = d);
  for (; c < o; ++c)
    a[c] = t[c];
  return new ht(a, this._parents, this._name, this._id);
}
function vg(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function bg(e, t, n) {
  var o, r, s = vg(t) ? ts : ct;
  return function() {
    var a = s(this, e), c = a.on;
    c !== o && (r = (o = c).copy()).on(t, n), a.on = r;
  };
}
function Ng(e, t) {
  var n = this._id;
  return arguments.length < 2 ? ot(this.node(), n).on.on(e) : this.each(bg(n, e, t));
}
function jg(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function Sg() {
  return this.on("end.remove", jg(this._id));
}
function kg(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Zi(e));
  for (var o = this._groups, r = o.length, s = new Array(r), a = 0; a < r; ++a)
    for (var c = o[a], u = c.length, l = s[a] = new Array(u), f, p, d = 0; d < u; ++d)
      (f = c[d]) && (p = e.call(f, f.__data__, d, c)) && ("__data__" in f && (p.__data__ = f.__data__), l[d] = p, _r(l[d], t, n, d, l, ot(f, n)));
  return new ht(s, this._parents, t, n);
}
function Eg(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Xc(e));
  for (var o = this._groups, r = o.length, s = [], a = [], c = 0; c < r; ++c)
    for (var u = o[c], l = u.length, f, p = 0; p < l; ++p)
      if (f = u[p]) {
        for (var d = e.call(f, f.__data__, p, u), h, g = ot(f, n), N = 0, w = d.length; N < w; ++N)
          (h = d[N]) && _r(h, t, n, N, d, g);
        s.push(d), a.push(f);
      }
  return new ht(s, a, t, n);
}
var Cg = ho.prototype.constructor;
function Ig() {
  return new Cg(this._groups, this._parents);
}
function Ag(e, t) {
  var n, o, r;
  return function() {
    var s = fn(this, e), a = (this.style.removeProperty(e), fn(this, e));
    return s === a ? null : s === n && a === o ? r : r = t(n = s, o = a);
  };
}
function wl(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function _g(e, t, n) {
  var o, r = n + "", s;
  return function() {
    var a = fn(this, e);
    return a === r ? null : a === o ? s : s = t(o = a, n);
  };
}
function Dg(e, t, n) {
  var o, r, s;
  return function() {
    var a = fn(this, e), c = n(this), u = c + "";
    return c == null && (u = c = (this.style.removeProperty(e), fn(this, e))), a === u ? null : a === o && u === r ? s : (r = u, s = t(o = a, c));
  };
}
function Mg(e, t) {
  var n, o, r, s = "style." + t, a = "end." + s, c;
  return function() {
    var u = ct(this, e), l = u.on, f = u.value[s] == null ? c || (c = wl(t)) : void 0;
    (l !== n || r !== f) && (o = (n = l).copy()).on(a, r = f), u.on = o;
  };
}
function Tg(e, t, n) {
  var o = (e += "") == "transform" ? $h : xl;
  return t == null ? this.styleTween(e, Ag(e, o)).on("end.style." + e, wl(e)) : typeof t == "function" ? this.styleTween(e, Dg(e, o, ns(this, "style." + e, t))).each(Mg(this._id, e)) : this.styleTween(e, _g(e, o, t), n).on("end.style." + e, null);
}
function $g(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function Pg(e, t, n) {
  var o, r;
  function s() {
    var a = t.apply(this, arguments);
    return a !== r && (o = (r = a) && $g(e, a, n)), o;
  }
  return s._value = t, s;
}
function zg(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, Pg(e, t, n ?? ""));
}
function Rg(e) {
  return function() {
    this.textContent = e;
  };
}
function Lg(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function Vg(e) {
  return this.tween("text", typeof e == "function" ? Lg(ns(this, "text", e)) : Rg(e == null ? "" : e + ""));
}
function Og(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Hg(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Og(r)), t;
  }
  return o._value = e, o;
}
function Wg(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, Hg(e));
}
function Bg() {
  for (var e = this._name, t = this._id, n = vl(), o = this._groups, r = o.length, s = 0; s < r; ++s)
    for (var a = o[s], c = a.length, u, l = 0; l < c; ++l)
      if (u = a[l]) {
        var f = ot(u, t);
        _r(u, e, n, l, a, {
          time: f.time + f.delay + f.duration,
          delay: 0,
          duration: f.duration,
          ease: f.ease
        });
      }
  return new ht(o, this._parents, e, n);
}
function Fg() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(s, a) {
    var c = { value: a }, u = { value: function() {
      --r === 0 && s();
    } };
    n.each(function() {
      var l = ct(this, o), f = l.on;
      f !== e && (t = (e = f).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(u)), l.on = t;
    }), r === 0 && s();
  });
}
var Kg = 0;
function ht(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function vl() {
  return ++Kg;
}
var ft = ho.prototype;
ht.prototype = {
  constructor: ht,
  select: kg,
  selectAll: Eg,
  selectChild: ft.selectChild,
  selectChildren: ft.selectChildren,
  filter: xg,
  merge: wg,
  selection: Ig,
  transition: Bg,
  call: ft.call,
  nodes: ft.nodes,
  node: ft.node,
  size: ft.size,
  empty: ft.empty,
  each: ft.each,
  on: Ng,
  attr: ng,
  attrTween: ag,
  style: Tg,
  styleTween: zg,
  text: Vg,
  textTween: Wg,
  remove: Sg,
  tween: Yh,
  delay: ug,
  duration: pg,
  ease: gg,
  easeVarying: mg,
  end: Fg,
  [Symbol.iterator]: ft[Symbol.iterator]
};
function qg(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var Ug = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: qg
};
function Xg(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function Yg(e) {
  var t, n;
  e instanceof ht ? (t = e._id, e = e._name) : (t = vl(), (n = Ug).time = es(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, s = 0; s < r; ++s)
    for (var a = o[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && _r(u, e, t, l, a, n || Xg(u, t));
  return new ht(o, this._parents, e, t);
}
ho.prototype.interrupt = qh;
ho.prototype.transition = Yg;
const Ho = (e) => () => e;
function Zg(e, {
  sourceEvent: t,
  target: n,
  transform: o,
  dispatch: r
}) {
  Object.defineProperties(this, {
    type: { value: e, enumerable: !0, configurable: !0 },
    sourceEvent: { value: t, enumerable: !0, configurable: !0 },
    target: { value: n, enumerable: !0, configurable: !0 },
    transform: { value: o, enumerable: !0, configurable: !0 },
    _: { value: r }
  });
}
function pt(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
pt.prototype = {
  constructor: pt,
  scale: function(e) {
    return e === 1 ? this : new pt(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new pt(this.k, this.x + this.k * e, this.y + this.k * t);
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
var Dr = new pt(1, 0, 0);
bl.prototype = pt.prototype;
function bl(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return Dr;
  return e.__zoom;
}
function ci(e) {
  e.stopImmediatePropagation();
}
function On(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Gg(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function Jg() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function la() {
  return this.__zoom || Dr;
}
function Qg(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function ey() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function ty(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], s = e.invertY(t[0][1]) - n[0][1], a = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    a > s ? (s + a) / 2 : Math.min(0, s) || Math.max(0, a)
  );
}
function Nl() {
  var e = Gg, t = Jg, n = ty, o = Qg, r = ey, s = [0, 1 / 0], a = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, u = Yo, l = Cr("start", "zoom", "end"), f, p, d, h = 500, g = 150, N = 0, w = 10;
  function m(k) {
    k.property("__zoom", la).on("wheel.zoom", _, { passive: !1 }).on("mousedown.zoom", $).on("dblclick.zoom", V).filter(r).on("touchstart.zoom", D).on("touchmove.zoom", A).on("touchend.zoom touchcancel.zoom", L).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  m.transform = function(k, M, I, T) {
    var z = k.selection ? k.selection() : k;
    z.property("__zoom", la), k !== z ? j(k, M, I, T) : z.interrupt().each(function() {
      v(this, arguments).event(T).start().zoom(null, typeof M == "function" ? M.apply(this, arguments) : M).end();
    });
  }, m.scaleBy = function(k, M, I, T) {
    m.scaleTo(k, function() {
      var z = this.__zoom.k, P = typeof M == "function" ? M.apply(this, arguments) : M;
      return z * P;
    }, I, T);
  }, m.scaleTo = function(k, M, I, T) {
    m.transform(k, function() {
      var z = t.apply(this, arguments), P = this.__zoom, K = I == null ? b(z) : typeof I == "function" ? I.apply(this, arguments) : I, B = P.invert(K), F = typeof M == "function" ? M.apply(this, arguments) : M;
      return n(y(E(P, F), K, B), z, a);
    }, I, T);
  }, m.translateBy = function(k, M, I, T) {
    m.transform(k, function() {
      return n(this.__zoom.translate(
        typeof M == "function" ? M.apply(this, arguments) : M,
        typeof I == "function" ? I.apply(this, arguments) : I
      ), t.apply(this, arguments), a);
    }, null, T);
  }, m.translateTo = function(k, M, I, T, z) {
    m.transform(k, function() {
      var P = t.apply(this, arguments), K = this.__zoom, B = T == null ? b(P) : typeof T == "function" ? T.apply(this, arguments) : T;
      return n(Dr.translate(B[0], B[1]).scale(K.k).translate(
        typeof M == "function" ? -M.apply(this, arguments) : -M,
        typeof I == "function" ? -I.apply(this, arguments) : -I
      ), P, a);
    }, T, z);
  };
  function E(k, M) {
    return M = Math.max(s[0], Math.min(s[1], M)), M === k.k ? k : new pt(M, k.x, k.y);
  }
  function y(k, M, I) {
    var T = M[0] - I[0] * k.k, z = M[1] - I[1] * k.k;
    return T === k.x && z === k.y ? k : new pt(k.k, T, z);
  }
  function b(k) {
    return [(+k[0][0] + +k[1][0]) / 2, (+k[0][1] + +k[1][1]) / 2];
  }
  function j(k, M, I, T) {
    k.on("start.zoom", function() {
      v(this, arguments).event(T).start();
    }).on("interrupt.zoom end.zoom", function() {
      v(this, arguments).event(T).end();
    }).tween("zoom", function() {
      var z = this, P = arguments, K = v(z, P).event(T), B = t.apply(z, P), F = I == null ? b(B) : typeof I == "function" ? I.apply(z, P) : I, X = Math.max(B[1][0] - B[0][0], B[1][1] - B[0][1]), J = z.__zoom, U = typeof M == "function" ? M.apply(z, P) : M, ce = u(J.invert(F).concat(X / J.k), U.invert(F).concat(X / U.k));
      return function(G) {
        if (G === 1) G = U;
        else {
          var O = ce(G), Y = X / O[2];
          G = new pt(Y, F[0] - O[0] * Y, F[1] - O[1] * Y);
        }
        K.zoom(null, G);
      };
    });
  }
  function v(k, M, I) {
    return !I && k.__zooming || new S(k, M);
  }
  function S(k, M) {
    this.that = k, this.args = M, this.active = 0, this.sourceEvent = null, this.extent = t.apply(k, M), this.taps = 0;
  }
  S.prototype = {
    event: function(k) {
      return k && (this.sourceEvent = k), this;
    },
    start: function() {
      return ++this.active === 1 && (this.that.__zooming = this, this.emit("start")), this;
    },
    zoom: function(k, M) {
      return this.mouse && k !== "mouse" && (this.mouse[1] = M.invert(this.mouse[0])), this.touch0 && k !== "touch" && (this.touch0[1] = M.invert(this.touch0[0])), this.touch1 && k !== "touch" && (this.touch1[1] = M.invert(this.touch1[0])), this.that.__zoom = M, this.emit("zoom"), this;
    },
    end: function() {
      return --this.active === 0 && (delete this.that.__zooming, this.emit("end")), this;
    },
    emit: function(k) {
      var M = Ue(this.that).datum();
      l.call(
        k,
        this.that,
        new Zg(k, {
          sourceEvent: this.sourceEvent,
          target: m,
          transform: this.that.__zoom,
          dispatch: l
        }),
        M
      );
    }
  };
  function _(k, ...M) {
    if (!e.apply(this, arguments)) return;
    var I = v(this, M).event(k), T = this.__zoom, z = Math.max(s[0], Math.min(s[1], T.k * Math.pow(2, o.apply(this, arguments)))), P = Je(k);
    if (I.wheel)
      (I.mouse[0][0] !== P[0] || I.mouse[0][1] !== P[1]) && (I.mouse[1] = T.invert(I.mouse[0] = P)), clearTimeout(I.wheel);
    else {
      if (T.k === z) return;
      I.mouse = [P, T.invert(P)], Jo(this), I.start();
    }
    On(k), I.wheel = setTimeout(K, g), I.zoom("mouse", n(y(E(T, z), I.mouse[0], I.mouse[1]), I.extent, a));
    function K() {
      I.wheel = null, I.end();
    }
  }
  function $(k, ...M) {
    if (d || !e.apply(this, arguments)) return;
    var I = k.currentTarget, T = v(this, M, !0).event(k), z = Ue(k.view).on("mousemove.zoom", F, !0).on("mouseup.zoom", X, !0), P = Je(k, I), K = k.clientX, B = k.clientY;
    il(k.view), ci(k), T.mouse = [P, this.__zoom.invert(P)], Jo(this), T.start();
    function F(J) {
      if (On(J), !T.moved) {
        var U = J.clientX - K, ce = J.clientY - B;
        T.moved = U * U + ce * ce > N;
      }
      T.event(J).zoom("mouse", n(y(T.that.__zoom, T.mouse[0] = Je(J, I), T.mouse[1]), T.extent, a));
    }
    function X(J) {
      z.on("mousemove.zoom mouseup.zoom", null), sl(J.view, T.moved), On(J), T.event(J).end();
    }
  }
  function V(k, ...M) {
    if (e.apply(this, arguments)) {
      var I = this.__zoom, T = Je(k.changedTouches ? k.changedTouches[0] : k, this), z = I.invert(T), P = I.k * (k.shiftKey ? 0.5 : 2), K = n(y(E(I, P), T, z), t.apply(this, M), a);
      On(k), c > 0 ? Ue(this).transition().duration(c).call(j, K, T, k) : Ue(this).call(m.transform, K, T, k);
    }
  }
  function D(k, ...M) {
    if (e.apply(this, arguments)) {
      var I = k.touches, T = I.length, z = v(this, M, k.changedTouches.length === T).event(k), P, K, B, F;
      for (ci(k), K = 0; K < T; ++K)
        B = I[K], F = Je(B, this), F = [F, this.__zoom.invert(F), B.identifier], z.touch0 ? !z.touch1 && z.touch0[2] !== F[2] && (z.touch1 = F, z.taps = 0) : (z.touch0 = F, P = !0, z.taps = 1 + !!f);
      f && (f = clearTimeout(f)), P && (z.taps < 2 && (p = F[0], f = setTimeout(function() {
        f = null;
      }, h)), Jo(this), z.start());
    }
  }
  function A(k, ...M) {
    if (this.__zooming) {
      var I = v(this, M).event(k), T = k.changedTouches, z = T.length, P, K, B, F;
      for (On(k), P = 0; P < z; ++P)
        K = T[P], B = Je(K, this), I.touch0 && I.touch0[2] === K.identifier ? I.touch0[0] = B : I.touch1 && I.touch1[2] === K.identifier && (I.touch1[0] = B);
      if (K = I.that.__zoom, I.touch1) {
        var X = I.touch0[0], J = I.touch0[1], U = I.touch1[0], ce = I.touch1[1], G = (G = U[0] - X[0]) * G + (G = U[1] - X[1]) * G, O = (O = ce[0] - J[0]) * O + (O = ce[1] - J[1]) * O;
        K = E(K, Math.sqrt(G / O)), B = [(X[0] + U[0]) / 2, (X[1] + U[1]) / 2], F = [(J[0] + ce[0]) / 2, (J[1] + ce[1]) / 2];
      } else if (I.touch0) B = I.touch0[0], F = I.touch0[1];
      else return;
      I.zoom("touch", n(y(K, B, F), I.extent, a));
    }
  }
  function L(k, ...M) {
    if (this.__zooming) {
      var I = v(this, M).event(k), T = k.changedTouches, z = T.length, P, K;
      for (ci(k), d && clearTimeout(d), d = setTimeout(function() {
        d = null;
      }, h), P = 0; P < z; ++P)
        K = T[P], I.touch0 && I.touch0[2] === K.identifier ? delete I.touch0 : I.touch1 && I.touch1[2] === K.identifier && delete I.touch1;
      if (I.touch1 && !I.touch0 && (I.touch0 = I.touch1, delete I.touch1), I.touch0) I.touch0[1] = this.__zoom.invert(I.touch0[0]);
      else if (I.end(), I.taps === 2 && (K = Je(K, this), Math.hypot(p[0] - K[0], p[1] - K[1]) < w)) {
        var B = Ue(this).on("dblclick.zoom");
        B && B.apply(this, arguments);
      }
    }
  }
  return m.wheelDelta = function(k) {
    return arguments.length ? (o = typeof k == "function" ? k : Ho(+k), m) : o;
  }, m.filter = function(k) {
    return arguments.length ? (e = typeof k == "function" ? k : Ho(!!k), m) : e;
  }, m.touchable = function(k) {
    return arguments.length ? (r = typeof k == "function" ? k : Ho(!!k), m) : r;
  }, m.extent = function(k) {
    return arguments.length ? (t = typeof k == "function" ? k : Ho([[+k[0][0], +k[0][1]], [+k[1][0], +k[1][1]]]), m) : t;
  }, m.scaleExtent = function(k) {
    return arguments.length ? (s[0] = +k[0], s[1] = +k[1], m) : [s[0], s[1]];
  }, m.translateExtent = function(k) {
    return arguments.length ? (a[0][0] = +k[0][0], a[1][0] = +k[1][0], a[0][1] = +k[0][1], a[1][1] = +k[1][1], m) : [[a[0][0], a[0][1]], [a[1][0], a[1][1]]];
  }, m.constrain = function(k) {
    return arguments.length ? (n = k, m) : n;
  }, m.duration = function(k) {
    return arguments.length ? (c = +k, m) : c;
  }, m.interpolate = function(k) {
    return arguments.length ? (u = k, m) : u;
  }, m.on = function() {
    var k = l.on.apply(l, arguments);
    return k === l ? m : k;
  }, m.clickDistance = function(k) {
    return arguments.length ? (N = (k = +k) * k, m) : Math.sqrt(N);
  }, m.tapDistance = function(k) {
    return arguments.length ? (w = +k, m) : w;
  }, m;
}
const Ye = {
  error001: (e = "react") => `Seems like you have not used zustand provider as an ancestor. Help: https://${e}flow.dev/error#001`,
  error002: () => "It looks like you've created a new nodeTypes or edgeTypes object. If this wasn't on purpose please define the nodeTypes/edgeTypes outside of the component or memoize them.",
  error003: (e) => `Node type "${e}" not found. Using fallback type "default".`,
  error004: () => "The parent container needs a width and a height to render the graph.",
  error005: () => "Only child nodes can use a parent extent.",
  error006: () => "Can't create edge. An edge needs a source and a target.",
  error007: (e) => `The old edge with id=${e} does not exist.`,
  error009: (e) => `Marker type "${e}" doesn't exist.`,
  error008: (e, { id: t, sourceHandle: n, targetHandle: o }) => `Couldn't create edge for ${e} handle id: "${e === "source" ? n : o}", edge id: ${t}.`,
  error010: () => "Handle: No node id found. Make sure to only use a Handle inside a custom Node.",
  error011: (e) => `Edge type "${e}" not found. Using fallback type "default".`,
  error012: (e) => `Node with id "${e}" does not exist, it may have been removed. This can happen when a node is deleted before the "onNodeClick" handler is called.`,
  error013: (e = "react") => `It seems that you haven't loaded the styles. Please import '@xyflow/${e}/dist/style.css' or base.css to make sure everything is working properly.`,
  error014: () => "useNodeConnections: No node ID found. Call useNodeConnections inside a custom Node or provide a node ID.",
  error015: () => "It seems that you are trying to drag a node that is not initialized. Please use onNodesChange as explained in the docs.",
  error016: (e) => `Edge with id "${e}" does not exist, it may have been removed. This can happen when an edge is deleted before the "onEdgeClick" handler is called.`
}, io = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], jl = ["Enter", " ", "Escape"], Sl = {
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
var hn;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(hn || (hn = {}));
var zt;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(zt || (zt = {}));
var so;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(so || (so = {}));
const kl = {
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
var Nt;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(Nt || (Nt = {}));
var fr;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(fr || (fr = {}));
var ne;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(ne || (ne = {}));
const ua = {
  [ne.Left]: ne.Right,
  [ne.Right]: ne.Left,
  [ne.Top]: ne.Bottom,
  [ne.Bottom]: ne.Top
};
function El(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Cl = (e) => "id" in e && "source" in e && "target" in e, ny = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), os = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), yo = (e, t = [0, 0]) => {
  const { width: n, height: o } = yt(e), r = e.origin ?? t, s = n * r[0], a = o * r[1];
  return {
    x: e.position.x - s,
    y: e.position.y - a
  };
}, oy = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const s = typeof r == "string";
    let a = !t.nodeLookup && !s ? r : void 0;
    t.nodeLookup && (a = s ? t.nodeLookup.get(r) : os(r) ? r : t.nodeLookup.get(r.id));
    const c = a ? pr(a, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return Mr(o, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return Tr(n);
}, mo = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = Mr(n, pr(r)), o = !0);
  }), o ? Tr(n) : { x: 0, y: 0, width: 0, height: 0 };
}, rs = (e, t, [n, o, r] = [0, 0, 1], s = !1, a = !1) => {
  const c = {
    ...Nn(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, u = [];
  for (const l of e.values()) {
    const { measured: f, selectable: p = !0, hidden: d = !1 } = l;
    if (a && !p || d)
      continue;
    const h = f.width ?? l.width ?? l.initialWidth ?? null, g = f.height ?? l.height ?? l.initialHeight ?? null, N = ao(c, yn(l)), w = (h ?? 0) * (g ?? 0), m = s && N > 0;
    (!l.internals.handleBounds || m || N >= w || l.dragging) && u.push(l);
  }
  return u;
}, ry = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function iy(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function sy({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: s }, a) {
  if (e.size === 0)
    return !0;
  const c = iy(e, a), u = mo(c), l = ss(u, t, n, a?.minZoom ?? r, a?.maxZoom ?? s, a?.padding ?? 0.1);
  return await o.setViewport(l, {
    duration: a?.duration,
    ease: a?.ease,
    interpolate: a?.interpolate
  }), !0;
}
function Il({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: s }) {
  const a = n.get(e), c = a.parentId ? n.get(a.parentId) : void 0, { x: u, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, f = a.origin ?? o;
  let p = a.extent || r;
  if (a.extent === "parent" && !a.expandParent)
    if (!c)
      s?.("005", Ye.error005());
    else {
      const h = c.measured.width, g = c.measured.height;
      h && g && (p = [
        [u, l],
        [u + h, l + g]
      ]);
    }
  else c && Ht(a.extent) && (p = [
    [a.extent[0][0] + u, a.extent[0][1] + l],
    [a.extent[1][0] + u, a.extent[1][1] + l]
  ]);
  const d = Ht(p) ? Ot(t, p, a.measured) : t;
  return (a.measured.width === void 0 || a.measured.height === void 0) && s?.("015", Ye.error015()), {
    position: {
      x: d.x - u + (a.measured.width ?? 0) * f[0],
      y: d.y - l + (a.measured.height ?? 0) * f[1]
    },
    positionAbsolute: d
  };
}
async function ay({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const s = new Set(e.map((d) => d.id)), a = [];
  for (const d of n) {
    if (d.deletable === !1)
      continue;
    const h = s.has(d.id), g = !h && d.parentId && a.find((N) => N.id === d.parentId);
    (h || g) && a.push(d);
  }
  const c = new Set(t.map((d) => d.id)), u = o.filter((d) => d.deletable !== !1), f = ry(a, u);
  for (const d of u)
    c.has(d.id) && !f.find((g) => g.id === d.id) && f.push(d);
  if (!r)
    return {
      edges: f,
      nodes: a
    };
  const p = await r({
    nodes: a,
    edges: f
  });
  return typeof p == "boolean" ? p ? { edges: f, nodes: a } : { edges: [], nodes: [] } : p;
}
const gn = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), Ot = (e = { x: 0, y: 0 }, t, n) => ({
  x: gn(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: gn(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Al(e, t, n) {
  const { width: o, height: r } = yt(n), { x: s, y: a } = n.internals.positionAbsolute;
  return Ot(e, [
    [s, a],
    [s + o, a + r]
  ], t);
}
const da = (e, t, n) => e < t ? gn(Math.abs(e - t), 1, t) / t : e > n ? -gn(Math.abs(e - n), 1, t) / t : 0, is = (e, t, n = 15, o = 40) => {
  const r = da(e.x, o, t.width - o) * n, s = da(e.y, o, t.height - o) * n;
  return [r, s];
}, Mr = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), Mi = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), Tr = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), yn = (e, t = [0, 0]) => {
  const { x: n, y: o } = os(e) ? e.internals.positionAbsolute : yo(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, pr = (e, t = [0, 0]) => {
  const { x: n, y: o } = os(e) ? e.internals.positionAbsolute : yo(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, _l = (e, t) => Tr(Mr(Mi(e), Mi(t))), ao = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, fa = (e) => et(e.width) && et(e.height) && et(e.x) && et(e.y), et = (e) => !isNaN(e) && isFinite(e), Dl = (e, t) => (n, o) => {
}, xo = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), Nn = ({ x: e, y: t }, [n, o, r], s = !1, a = [1, 1]) => {
  const c = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return s ? xo(c, a) : c;
}, mn = ({ x: e, y: t }, [n, o, r]) => ({
  x: e * r + n,
  y: t * r + o
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
function cy(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = rn(e, n), r = rn(e, t);
    return {
      top: o,
      right: r,
      bottom: o,
      left: r,
      x: r * 2,
      y: o * 2
    };
  }
  if (typeof e == "object") {
    const o = rn(e.top ?? e.y ?? 0, n), r = rn(e.bottom ?? e.y ?? 0, n), s = rn(e.left ?? e.x ?? 0, t), a = rn(e.right ?? e.x ?? 0, t);
    return { top: o, right: a, bottom: r, left: s, x: s + a, y: o + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function ly(e, t, n, o, r, s) {
  const { x: a, y: c } = mn(e, [t, n, o]), { x: u, y: l } = mn({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), f = r - u, p = s - l;
  return {
    left: Math.floor(a),
    top: Math.floor(c),
    right: Math.floor(f),
    bottom: Math.floor(p)
  };
}
const ss = (e, t, n, o, r, s) => {
  const a = cy(s, t, n), c = (t - a.x) / e.width, u = (n - a.y) / e.height, l = Math.min(c, u), f = gn(l, o, r), p = e.x + e.width / 2, d = e.y + e.height / 2, h = t / 2 - p * f, g = n / 2 - d * f, N = ly(e, h, g, f, t, n), w = {
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
}, co = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function Ht(e) {
  return e != null && e !== "parent";
}
function yt(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function Ml(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function Tl(e, t = { width: 0, height: 0 }, n, o, r) {
  const s = { ...e }, a = o.get(n);
  if (a) {
    const c = a.origin || r;
    s.x += a.internals.positionAbsolute.x - (t.width ?? 0) * c[0], s.y += a.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return s;
}
function pa(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function uy() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function dy(e) {
  return { ...Sl, ...e || {} };
}
function Gn(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: s, y: a } = tt(e), c = Nn({ x: s - (r?.left ?? 0), y: a - (r?.top ?? 0) }, o), { x: u, y: l } = n ? xo(c, t) : c;
  return {
    xSnapped: u,
    ySnapped: l,
    ...c
  };
}
const as = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), $l = (e) => e?.getRootNode?.() || window?.document, fy = ["INPUT", "SELECT", "TEXTAREA"];
function Pl(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : fy.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const zl = (e) => "clientX" in e, tt = (e, t) => {
  const n = zl(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, ha = (e, t, n, o, r) => {
  const s = t.querySelectorAll(`.${e}`);
  return !s || !s.length ? null : Array.from(s).map((a) => {
    const c = a.getBoundingClientRect();
    return {
      id: a.getAttribute("data-handleid"),
      type: e,
      nodeId: r,
      position: a.getAttribute("data-handlepos"),
      x: (c.left - n.left) / o,
      y: (c.top - n.top) / o,
      ...as(a)
    };
  });
};
function Rl({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: s, targetControlX: a, targetControlY: c }) {
  const u = e * 0.125 + r * 0.375 + a * 0.375 + n * 0.125, l = t * 0.125 + s * 0.375 + c * 0.375 + o * 0.125, f = Math.abs(u - e), p = Math.abs(l - t);
  return [u, l, f, p];
}
function Wo(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function ga({ pos: e, x1: t, y1: n, x2: o, y2: r, c: s }) {
  switch (e) {
    case ne.Left:
      return [t - Wo(t - o, s), n];
    case ne.Right:
      return [t + Wo(o - t, s), n];
    case ne.Top:
      return [t, n - Wo(n - r, s)];
    case ne.Bottom:
      return [t, n + Wo(r - n, s)];
  }
}
function Ll({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: r, targetPosition: s = ne.Top, curvature: a = 0.25 }) {
  const [c, u] = ga({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: a
  }), [l, f] = ga({
    pos: s,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: a
  }), [p, d, h, g] = Rl({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: r,
    sourceControlX: c,
    sourceControlY: u,
    targetControlX: l,
    targetControlY: f
  });
  return [
    `M${e},${t} C${c},${u} ${l},${f} ${o},${r}`,
    p,
    d,
    h,
    g
  ];
}
function Vl({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, s = n < e ? n + r : n - r, a = Math.abs(o - t) / 2, c = o < t ? o + a : o - a;
  return [s, c, r, a];
}
function py({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: s = "basic" }) {
  if (s === "manual")
    return o;
  const a = r && n ? o + 1e3 : o, c = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return a + c;
}
function hy({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const s = Mr(pr(e), pr(t));
  s.x === s.x2 && (s.x2 += 1), s.y === s.y2 && (s.y2 += 1);
  const a = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return ao(a, Tr(s)) > 0;
}
const Ol = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, gy = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), yy = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Ye.error006()), t;
  const o = n.getEdgeId || Ol;
  let r;
  return Cl(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, gy(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, my = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: r, ...s } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", Ye.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return o.onError?.("007", Ye.error007(r)), n;
  const c = o.getEdgeId || Ol, u = {
    ...s,
    id: o.shouldReplaceId ? c(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== r).concat(u);
};
function Hl({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, s, a, c] = Vl({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, s, a, c];
}
const ya = {
  [ne.Left]: { x: -1, y: 0 },
  [ne.Right]: { x: 1, y: 0 },
  [ne.Top]: { x: 0, y: -1 },
  [ne.Bottom]: { x: 0, y: 1 }
}, xy = ({ source: e, sourcePosition: t = ne.Bottom, target: n }) => t === ne.Left || t === ne.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, ma = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function wy({ source: e, sourcePosition: t = ne.Bottom, target: n, targetPosition: o = ne.Top, center: r, offset: s, stepPosition: a }) {
  const c = ya[t], u = ya[o], l = { x: e.x + c.x * s, y: e.y + c.y * s }, f = { x: n.x + u.x * s, y: n.y + u.y * s }, p = xy({
    source: l,
    sourcePosition: t,
    target: f
  }), d = p.x !== 0 ? "x" : "y", h = p[d];
  let g = [], N, w;
  const m = { x: 0, y: 0 }, E = { x: 0, y: 0 }, [, , y, b] = Vl({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[d] * u[d] === -1) {
    d === "x" ? (N = r.x ?? l.x + (f.x - l.x) * a, w = r.y ?? (l.y + f.y) / 2) : (N = r.x ?? (l.x + f.x) / 2, w = r.y ?? l.y + (f.y - l.y) * a);
    const _ = [
      { x: N, y: l.y },
      { x: N, y: f.y }
    ], $ = [
      { x: l.x, y: w },
      { x: f.x, y: w }
    ];
    c[d] === h ? g = d === "x" ? _ : $ : g = d === "x" ? $ : _;
  } else {
    const _ = [{ x: l.x, y: f.y }], $ = [{ x: f.x, y: l.y }];
    if (d === "x" ? g = c.x === h ? $ : _ : g = c.y === h ? _ : $, t === o) {
      const k = Math.abs(e[d] - n[d]);
      if (k <= s) {
        const M = Math.min(s - 1, s - k);
        c[d] === h ? m[d] = (l[d] > e[d] ? -1 : 1) * M : E[d] = (f[d] > n[d] ? -1 : 1) * M;
      }
    }
    if (t !== o) {
      const k = d === "x" ? "y" : "x", M = c[d] === u[k], I = l[k] > f[k], T = l[k] < f[k];
      (c[d] === 1 && (!M && I || M && T) || c[d] !== 1 && (!M && T || M && I)) && (g = d === "x" ? _ : $);
    }
    const V = { x: l.x + m.x, y: l.y + m.y }, D = { x: f.x + E.x, y: f.y + E.y }, A = Math.max(Math.abs(V.x - g[0].x), Math.abs(D.x - g[0].x)), L = Math.max(Math.abs(V.y - g[0].y), Math.abs(D.y - g[0].y));
    A >= L ? (N = (V.x + D.x) / 2, w = g[0].y) : (N = g[0].x, w = (V.y + D.y) / 2);
  }
  const j = { x: l.x + m.x, y: l.y + m.y }, v = { x: f.x + E.x, y: f.y + E.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...j.x !== g[0].x || j.y !== g[0].y ? [j] : [],
    ...g,
    ...v.x !== g[g.length - 1].x || v.y !== g[g.length - 1].y ? [v] : [],
    n
  ], N, w, y, b];
}
function vy(e, t, n, o) {
  const r = Math.min(ma(e, t) / 2, ma(t, n) / 2, o), { x: s, y: a } = t;
  if (e.x === s && s === n.x || e.y === a && a === n.y)
    return `L${s} ${a}`;
  if (e.y === a) {
    const l = e.x < n.x ? -1 : 1, f = e.y < n.y ? 1 : -1;
    return `L ${s + r * l},${a}Q ${s},${a} ${s},${a + r * f}`;
  }
  const c = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${s},${a + r * u}Q ${s},${a} ${s + r * c},${a}`;
}
function hr({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: r, targetPosition: s = ne.Top, borderRadius: a = 5, centerX: c, centerY: u, offset: l = 20, stepPosition: f = 0.5 }) {
  const [p, d, h, g, N] = wy({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: s,
    center: { x: c, y: u },
    offset: l,
    stepPosition: f
  });
  let w = `M${p[0].x} ${p[0].y}`;
  for (let m = 1; m < p.length - 1; m++)
    w += vy(p[m - 1], p[m], p[m + 1], a);
  return w += `L${p[p.length - 1].x} ${p[p.length - 1].y}`, [w, d, h, g, N];
}
function xa(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function by(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!xa(t) || !xa(n))
    return null;
  const o = t.internals.handleBounds || wa(t.handles), r = n.internals.handleBounds || wa(n.handles), s = va(o?.source ?? [], e.sourceHandle), a = va(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === hn.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!s || !a)
    return e.onError?.("008", Ye.error008(s ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = s?.position || ne.Bottom, u = a?.position || ne.Top, l = Wt(t, s, c), f = Wt(n, a, u);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: f.x,
    targetY: f.y,
    sourcePosition: c,
    targetPosition: u
  };
}
function wa(e) {
  if (!e)
    return null;
  const t = [], n = [];
  for (const o of e)
    o.width = o.width ?? 1, o.height = o.height ?? 1, o.type === "source" ? t.push(o) : o.type === "target" && n.push(o);
  return {
    source: t,
    target: n
  };
}
function Wt(e, t, n = ne.Left, o = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, s = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: a, height: c } = t ?? yt(e);
  if (o)
    return { x: r + a / 2, y: s + c / 2 };
  switch (t?.position ?? n) {
    case ne.Top:
      return { x: r + a / 2, y: s };
    case ne.Right:
      return { x: r + a, y: s + c / 2 };
    case ne.Bottom:
      return { x: r + a / 2, y: s + c };
    case ne.Left:
      return { x: r, y: s + c / 2 };
  }
}
function va(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Ti(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function Ny(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const s = /* @__PURE__ */ new Set();
  return e.reduce((a, c) => ([c.markerStart || o, c.markerEnd || r].forEach((u) => {
    if (u && typeof u == "object") {
      const l = Ti(u, t);
      s.has(l) || (a.push({ id: l, color: u.color || n, ...u }), s.add(l));
    }
  }), a), []).sort((a, c) => a.id.localeCompare(c.id));
}
const Wl = 1e3, jy = 10, cs = {
  nodeOrigin: [0, 0],
  nodeExtent: io,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Sy = {
  ...cs,
  checkEquality: !0
};
function ls(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function ky(e, t, n) {
  const o = ls(cs, n);
  for (const r of e.values())
    if (r.parentId)
      ds(r, e, t, o);
    else {
      const s = yo(r, o.nodeOrigin), a = Ht(r.extent) ? r.extent : o.nodeExtent, c = Ot(s, a, yt(r));
      r.internals.positionAbsolute = c;
    }
}
function Ey(e, t) {
  if (!e.handles)
    return e.measured ? t?.internals.handleBounds : void 0;
  const n = [], o = [];
  for (const r of e.handles) {
    const s = {
      id: r.id,
      width: r.width ?? 1,
      height: r.height ?? 1,
      nodeId: e.id,
      x: r.x,
      y: r.y,
      position: r.position,
      type: r.type
    };
    r.type === "source" ? n.push(s) : r.type === "target" && o.push(s);
  }
  return {
    source: n,
    target: o
  };
}
function us(e) {
  return e === "manual";
}
function $i(e, t, n, o = {}) {
  const r = ls(Sy, o), s = { i: 0 }, a = new Map(t), c = r?.elevateNodesOnSelect && !us(r.zIndexMode) ? Wl : 0;
  let u = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const f of e) {
    let p = a.get(f.id);
    if (r.checkEquality && f === p?.internals.userNode)
      t.set(f.id, p);
    else {
      const d = yo(f, r.nodeOrigin), h = Ht(f.extent) ? f.extent : r.nodeExtent, g = Ot(d, h, yt(f));
      p = {
        ...r.defaults,
        ...f,
        measured: {
          width: f.measured?.width,
          height: f.measured?.height
        },
        internals: {
          positionAbsolute: g,
          // if user re-initializes the node or removes `measured` for whatever reason, we reset the handleBounds so that the node gets re-measured
          handleBounds: Ey(f, p),
          z: Bl(f, c, r.zIndexMode),
          userNode: f
        }
      }, t.set(f.id, p);
    }
    (p.measured === void 0 || p.measured.width === void 0 || p.measured.height === void 0) && !p.hidden && (u = !1), f.parentId && ds(p, t, n, o, s), l ||= f.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: l };
}
function Cy(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function ds(e, t, n, o, r) {
  const { elevateNodesOnSelect: s, nodeOrigin: a, nodeExtent: c, zIndexMode: u } = ls(cs, o), l = e.parentId, f = t.get(l);
  if (!f) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Cy(e, n), r && !f.parentId && f.internals.rootParentIndex === void 0 && u === "auto" && (f.internals.rootParentIndex = ++r.i, f.internals.z = f.internals.z + r.i * jy), r && f.internals.rootParentIndex !== void 0 && (r.i = f.internals.rootParentIndex);
  const p = s && !us(u) ? Wl : 0, { x: d, y: h, z: g } = Iy(e, f, a, c, p, u), { positionAbsolute: N } = e.internals, w = d !== N.x || h !== N.y;
  (w || g !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: w ? { x: d, y: h } : N,
      z: g
    }
  });
}
function Bl(e, t, n) {
  const o = et(e.zIndex) ? e.zIndex : 0;
  return us(n) ? o : o + (e.selected ? t : 0);
}
function Iy(e, t, n, o, r, s) {
  const { x: a, y: c } = t.internals.positionAbsolute, u = yt(e), l = yo(e, n), f = Ht(e.extent) ? Ot(l, e.extent, u) : l;
  let p = Ot({ x: a + f.x, y: c + f.y }, o, u);
  e.extent === "parent" && (p = Al(p, u, t));
  const d = Bl(e, r, s), h = t.internals.z ?? 0;
  return {
    x: p.x,
    y: p.y,
    z: h >= d ? h + 1 : d
  };
}
function fs(e, t, n, o = [0, 0]) {
  const r = [], s = /* @__PURE__ */ new Map();
  for (const a of e) {
    const c = t.get(a.parentId);
    if (!c)
      continue;
    const u = s.get(a.parentId)?.expandedRect ?? yn(c), l = _l(u, a.rect);
    s.set(a.parentId, { expandedRect: l, parent: c });
  }
  return s.size > 0 && s.forEach(({ expandedRect: a, parent: c }, u) => {
    const l = c.internals.positionAbsolute, f = yt(c), p = c.origin ?? o, d = a.x < l.x ? Math.round(Math.abs(l.x - a.x)) : 0, h = a.y < l.y ? Math.round(Math.abs(l.y - a.y)) : 0, g = Math.max(f.width, Math.round(a.width)), N = Math.max(f.height, Math.round(a.height)), w = (g - f.width) * p[0], m = (N - f.height) * p[1];
    (d > 0 || h > 0 || w || m) && (r.push({
      id: u,
      type: "position",
      position: {
        x: c.position.x - d + w,
        y: c.position.y - h + m
      }
    }), n.get(u)?.forEach((E) => {
      e.some((y) => y.id === E.id) || r.push({
        id: E.id,
        type: "position",
        position: {
          x: E.position.x + d,
          y: E.position.y + h
        }
      });
    })), (f.width < a.width || f.height < a.height || d || h) && r.push({
      id: u,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: g + (d ? p[0] * d - w : 0),
        height: N + (h ? p[1] * h - m : 0)
      }
    });
  }), r;
}
function Ay(e, t, n, o, r, s, a) {
  const c = o?.querySelector(".xyflow__viewport");
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
    const N = as(h.nodeElement), w = g.measured.width !== N.width || g.measured.height !== N.height;
    if (!!(N.width && N.height && (w || !g.internals.handleBounds || h.force))) {
      const E = h.nodeElement.getBoundingClientRect(), y = Ht(g.extent) ? g.extent : s;
      let { positionAbsolute: b } = g.internals;
      g.parentId && g.extent === "parent" ? b = Al(b, N, t.get(g.parentId)) : y && (b = Ot(b, y, N));
      const j = {
        ...g,
        measured: N,
        internals: {
          ...g.internals,
          positionAbsolute: b,
          handleBounds: {
            source: ha("source", h.nodeElement, E, p, g.id),
            target: ha("target", h.nodeElement, E, p, g.id)
          }
        }
      };
      t.set(g.id, j), g.parentId && ds(j, t, n, { nodeOrigin: r, zIndexMode: a }), u = !0, w && (l.push({
        id: g.id,
        type: "dimensions",
        dimensions: N
      }), g.expandParent && g.parentId && d.push({
        id: g.id,
        parentId: g.parentId,
        rect: yn(j, r)
      }));
    }
  }
  if (d.length > 0) {
    const h = fs(d, t, n, r);
    l.push(...h);
  }
  return { changes: l, updatedInternals: u };
}
async function _y({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: s }) {
  if (!t || !e.x && !e.y)
    return !1;
  const a = await t.setViewportConstrained({
    x: n[0] + e.x,
    y: n[1] + e.y,
    zoom: n[2]
  }, [
    [0, 0],
    [r, s]
  ], o);
  return !!a && (a.x !== n[0] || a.y !== n[1] || a.k !== n[2]);
}
function ba(e, t, n, o, r, s) {
  let a = r;
  const c = o.get(a) || /* @__PURE__ */ new Map();
  o.set(a, c.set(n, t)), a = `${r}-${e}`;
  const u = o.get(a) || /* @__PURE__ */ new Map();
  if (o.set(a, u.set(n, t)), s) {
    a = `${r}-${e}-${s}`;
    const l = o.get(a) || /* @__PURE__ */ new Map();
    o.set(a, l.set(n, t));
  }
}
function Fl(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: s, sourceHandle: a = null, targetHandle: c = null } = o, u = { edgeId: o.id, source: r, target: s, sourceHandle: a, targetHandle: c }, l = `${r}-${a}--${s}-${c}`, f = `${s}-${c}--${r}-${a}`;
    ba("source", u, f, e, r, a), ba("target", u, l, e, s, c), t.set(o.id, o);
  }
}
function Kl(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : Kl(n, t) : !1;
}
function Na(e, t, n) {
  let o = e;
  do {
    if (o?.matches?.(t))
      return !0;
    if (o === n)
      return !1;
    o = o?.parentElement;
  } while (o);
  return !1;
}
function Dy(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [s, a] of e)
    if ((a.selected || a.id === o) && (!a.parentId || !Kl(a, e)) && (a.draggable || t && typeof a.draggable > "u")) {
      const c = e.get(s);
      c && r.set(s, {
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
  return r;
}
function li({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
  const r = [];
  for (const [a, c] of t) {
    const u = n.get(a)?.internals.userNode;
    u && r.push({
      ...u,
      position: c.position,
      dragging: o
    });
  }
  if (!e)
    return [r[0], r];
  const s = n.get(e)?.internals.userNode;
  return [
    s ? {
      ...s,
      position: t.get(e)?.position || s.position,
      dragging: o
    } : r[0],
    r
  ];
}
function My({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const s = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, a = xo(s, t);
  return {
    x: a.x - s.x,
    y: a.y - s.y
  };
}
function Ty({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let s = { x: null, y: null }, a = 0, c = /* @__PURE__ */ new Map(), u = !1, l = { x: 0, y: 0 }, f = null, p = !1, d = null, h = !1, g = !1, N = null;
  function w({ noDragClassName: E, handleSelector: y, domNode: b, isSelectable: j, nodeId: v, nodeClickDistance: S = 0 }) {
    d = Ue(b);
    function _({ x: A, y: L }) {
      const { nodeLookup: k, nodeExtent: M, snapGrid: I, snapToGrid: T, nodeOrigin: z, onNodeDrag: P, onSelectionDrag: K, onError: B, updateNodePositions: F } = t();
      s = { x: A, y: L };
      let X = !1;
      const J = c.size > 1, U = J && M ? Mi(mo(c)) : null, ce = J && T ? My({
        dragItems: c,
        snapGrid: I,
        x: A,
        y: L
      }) : null;
      for (const [G, O] of c) {
        if (!k.has(G))
          continue;
        let Y = { x: A - O.distance.x, y: L - O.distance.y };
        T && (Y = ce ? {
          x: Math.round(Y.x + ce.x),
          y: Math.round(Y.y + ce.y)
        } : xo(Y, I));
        let se = null;
        if (J && M && !O.extent && U) {
          const { positionAbsolute: oe } = O.internals, fe = oe.x - U.x + M[0][0], W = oe.x + O.measured.width - U.x2 + M[1][0], ee = oe.y - U.y + M[0][1], me = oe.y + O.measured.height - U.y2 + M[1][1];
          se = [
            [fe, ee],
            [W, me]
          ];
        }
        const { position: ae, positionAbsolute: te } = Il({
          nodeId: G,
          nextPosition: Y,
          nodeLookup: k,
          nodeExtent: se || M,
          nodeOrigin: z,
          onError: B
        });
        X = X || O.position.x !== ae.x || O.position.y !== ae.y, O.position = ae, O.internals.positionAbsolute = te;
      }
      if (g = g || X, !!X && (F(c, !0), N && (o || P || !v && K))) {
        const [G, O] = li({
          nodeId: v,
          dragItems: c,
          nodeLookup: k
        });
        o?.(N, c, G, O), P?.(N, G, O), v || K?.(N, O);
      }
    }
    async function $() {
      if (!f)
        return;
      const { transform: A, panBy: L, autoPanSpeed: k, autoPanOnNodeDrag: M } = t();
      if (!M) {
        u = !1, cancelAnimationFrame(a);
        return;
      }
      const [I, T] = is(l, f, k);
      (I !== 0 || T !== 0) && (s.x = (s.x ?? 0) - I / A[2], s.y = (s.y ?? 0) - T / A[2], await L({ x: I, y: T }) && _(s)), a = requestAnimationFrame($);
    }
    function V(A) {
      const { nodeLookup: L, multiSelectionActive: k, nodesDraggable: M, transform: I, snapGrid: T, snapToGrid: z, selectNodesOnDrag: P, onNodeDragStart: K, onSelectionDragStart: B, unselectNodesAndEdges: F } = t();
      p = !0, (!P || !j) && !k && v && (L.get(v)?.selected || F()), j && P && v && e?.(v);
      const X = Gn(A.sourceEvent, { transform: I, snapGrid: T, snapToGrid: z, containerBounds: f });
      if (s = X, c = Dy(L, M, X, v), c.size > 0 && (n || K || !v && B)) {
        const [J, U] = li({
          nodeId: v,
          dragItems: c,
          nodeLookup: L
        });
        n?.(A.sourceEvent, c, J, U), K?.(A.sourceEvent, J, U), v || B?.(A.sourceEvent, U);
      }
    }
    const D = al().clickDistance(S).on("start", (A) => {
      const { domNode: L, nodeDragThreshold: k, transform: M, snapGrid: I, snapToGrid: T } = t();
      f = L?.getBoundingClientRect() || null, h = !1, g = !1, N = A.sourceEvent, k === 0 && V(A), s = Gn(A.sourceEvent, { transform: M, snapGrid: I, snapToGrid: T, containerBounds: f }), l = tt(A.sourceEvent, f);
    }).on("drag", (A) => {
      const { autoPanOnNodeDrag: L, transform: k, snapGrid: M, snapToGrid: I, nodeDragThreshold: T, nodeLookup: z } = t(), P = Gn(A.sourceEvent, { transform: k, snapGrid: M, snapToGrid: I, containerBounds: f });
      if (N = A.sourceEvent, (A.sourceEvent.type === "touchmove" && A.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      v && !z.has(v)) && (h = !0), !h) {
        if (!u && L && p && (u = !0, $()), !p) {
          const K = tt(A.sourceEvent, f), B = K.x - l.x, F = K.y - l.y;
          Math.sqrt(B * B + F * F) > T && V(A);
        }
        (s.x !== P.xSnapped || s.y !== P.ySnapped) && c && p && (l = tt(A.sourceEvent, f), _(P));
      }
    }).on("end", (A) => {
      if (!p || h) {
        h && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (u = !1, p = !1, cancelAnimationFrame(a), c.size > 0) {
        const { nodeLookup: L, updateNodePositions: k, onNodeDragStop: M, onSelectionDragStop: I } = t();
        if (g && (k(c, !1), g = !1), r || M || !v && I) {
          const [T, z] = li({
            nodeId: v,
            dragItems: c,
            nodeLookup: L,
            dragging: !1
          });
          r?.(A.sourceEvent, c, T, z), M?.(A.sourceEvent, T, z), v || I?.(A.sourceEvent, z);
        }
      }
    }).filter((A) => {
      const L = A.target;
      return !A.button && (!E || !Na(L, `.${E}`, b)) && (!y || Na(L, y, b));
    });
    d.call(D);
  }
  function m() {
    d?.on(".drag", null);
  }
  return {
    update: w,
    destroy: m
  };
}
function $y(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const s of t.values())
    ao(r, yn(s)) > 0 && o.push(s);
  return o;
}
const Py = 250;
function zy(e, t, n, o) {
  let r = [], s = 1 / 0;
  const a = $y(e, n, t + Py);
  for (const c of a) {
    const u = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of u) {
      if (o.nodeId === l.nodeId && o.type === l.type && o.id === l.id)
        continue;
      const { x: f, y: p } = Wt(c, l, l.position, !0), d = Math.sqrt(Math.pow(f - e.x, 2) + Math.pow(p - e.y, 2));
      d > t || (d < s ? (r = [{ ...l, x: f, y: p }], s = d) : d === s && r.push({ ...l, x: f, y: p }));
    }
  }
  if (!r.length)
    return null;
  if (r.length > 1) {
    const c = o.type === "source" ? "target" : "source";
    return r.find((u) => u.type === c) ?? r[0];
  }
  return r[0];
}
function ql(e, t, n, o, r, s = !1) {
  const a = o.get(e);
  if (!a)
    return null;
  const c = r === "strict" ? a.internals.handleBounds?.[t] : [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []], u = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return u && s ? { ...u, ...Wt(a, u, u.position, !0) } : u;
}
function Ul(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Ry(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const Xl = () => !0;
function Ly(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: s, isTarget: a, domNode: c, nodeLookup: u, lib: l, autoPanOnConnect: f, flowId: p, panBy: d, cancelConnection: h, onConnectStart: g, onConnect: N, onConnectEnd: w, isValidConnection: m = Xl, onReconnectEnd: E, updateConnection: y, getTransform: b, getFromHandle: j, autoPanSpeed: v, dragThreshold: S = 1, handleDomNode: _ }) {
  const $ = $l(e.target);
  let V = 0, D;
  const { x: A, y: L } = tt(e), k = Ul(s, _), M = c?.getBoundingClientRect();
  let I = !1;
  if (!M || !k)
    return;
  const T = ql(r, k, o, u, t);
  if (!T)
    return;
  let z = tt(e, M), P = !1, K = null, B = !1, F = null;
  function X() {
    if (!f || !M)
      return;
    const [ae, te] = is(z, M, v);
    d({ x: ae, y: te }), V = requestAnimationFrame(X);
  }
  const J = {
    ...T,
    nodeId: r,
    type: k,
    position: T.position
  }, U = u.get(r);
  let G = {
    inProgress: !0,
    isValid: null,
    from: Wt(U, J, ne.Left, !0),
    fromHandle: J,
    fromPosition: J.position,
    fromNode: U,
    to: z,
    toHandle: null,
    toPosition: ua[J.position],
    toNode: null,
    pointer: z
  };
  function O() {
    I = !0, y(G), g?.(e, { nodeId: r, handleId: o, handleType: k });
  }
  S === 0 && O();
  function Y(ae) {
    if (!I) {
      const { x: me, y: ve } = tt(ae), $e = me - A, De = ve - L;
      if (!($e * $e + De * De > S * S))
        return;
      O();
    }
    if (!j() || !J) {
      se(ae);
      return;
    }
    const te = b();
    z = tt(ae, M), D = zy(Nn(z, te, !1, [1, 1]), n, u, J), P || (X(), P = !0);
    const oe = Yl(ae, {
      handle: D,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: a ? "target" : "source",
      isValidConnection: m,
      doc: $,
      lib: l,
      flowId: p,
      nodeLookup: u
    });
    F = oe.handleDomNode, K = oe.connection, B = Ry(!!D, oe.isValid);
    const fe = u.get(r), W = fe ? Wt(fe, J, ne.Left, !0) : G.from, ee = {
      ...G,
      from: W,
      isValid: B,
      to: oe.toHandle && B ? mn({ x: oe.toHandle.x, y: oe.toHandle.y }, te) : z,
      toHandle: oe.toHandle,
      toPosition: B && oe.toHandle ? oe.toHandle.position : ua[J.position],
      toNode: oe.toHandle ? u.get(oe.toHandle.nodeId) : null,
      pointer: z
    };
    y(ee), G = ee;
  }
  function se(ae) {
    if (!("touches" in ae && ae.touches.length > 0)) {
      if (I) {
        (D || F) && K && B && N?.(K);
        const { inProgress: te, ...oe } = G, fe = {
          ...oe,
          toPosition: G.toHandle ? G.toPosition : null
        };
        w?.(ae, fe), s && E?.(ae, fe);
      }
      h(), cancelAnimationFrame(V), P = !1, B = !1, K = null, F = null, $.removeEventListener("mousemove", Y), $.removeEventListener("mouseup", se), $.removeEventListener("touchmove", Y), $.removeEventListener("touchend", se);
    }
  }
  $.addEventListener("mousemove", Y), $.addEventListener("mouseup", se), $.addEventListener("touchmove", Y), $.addEventListener("touchend", se);
}
function Yl(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: s, doc: a, lib: c, flowId: u, isValidConnection: l = Xl, nodeLookup: f }) {
  const p = s === "target", d = t ? a.querySelector(`.${c}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: h, y: g } = tt(e), N = a.elementFromPoint(h, g), w = N?.classList.contains(`${c}-flow__handle`) ? N : d, m = {
    handleDomNode: w,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (w) {
    const E = Ul(void 0, w), y = w.getAttribute("data-nodeid"), b = w.getAttribute("data-handleid"), j = w.classList.contains("connectable"), v = w.classList.contains("connectableend");
    if (!y || !E)
      return m;
    const S = {
      source: p ? y : o,
      sourceHandle: p ? b : r,
      target: p ? o : y,
      targetHandle: p ? r : b
    };
    m.connection = S;
    const $ = j && v && (n === hn.Strict ? p && E === "source" || !p && E === "target" : y !== o || b !== r);
    m.isValid = $ && l(S), m.toHandle = ql(y, E, b, f, n, !0);
  }
  return m;
}
const Pi = {
  onPointerDown: Ly,
  isValid: Yl
};
function Vy({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = Ue(e);
  function s({ translateExtent: c, width: u, height: l, zoomStep: f = 1, pannable: p = !0, zoomable: d = !0, inversePan: h = !1 }) {
    const g = (y) => {
      if (y.sourceEvent.type !== "wheel" || !t)
        return;
      const b = n(), j = y.sourceEvent.ctrlKey && co() ? 10 : 1, v = -y.sourceEvent.deltaY * (y.sourceEvent.deltaMode === 1 ? 0.05 : y.sourceEvent.deltaMode ? 1 : 2e-3) * f, S = b[2] * Math.pow(2, v * j);
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
      const S = o() * Math.max(b[2], Math.log(b[2])) * (h ? -1 : 1), _ = {
        x: b[0] - v[0] * S,
        y: b[1] - v[1] * S
      }, $ = [
        [0, 0],
        [u, l]
      ];
      t.setViewportConstrained({
        x: _.x,
        y: _.y,
        zoom: b[2]
      }, $, c);
    }, E = Nl().on("start", w).on("zoom", p ? m : null).on("zoom.wheel", d ? g : null);
    r.call(E, {});
  }
  function a() {
    r.on("zoom", null);
  }
  return {
    update: s,
    destroy: a,
    pointer: Je
  };
}
const $r = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), ui = ({ x: e, y: t, zoom: n }) => Dr.translate(e, t).scale(n), an = (e, t) => e.target.closest(`.${t}`), Zl = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Oy = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, di = (e, t = 0, n = Oy, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, Gl = (e) => {
  const t = e.ctrlKey && co() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Hy({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: s, zoomOnPinch: a, onPanZoomStart: c, onPanZoom: u, onPanZoomEnd: l }) {
  return (f) => {
    if (an(f, t))
      return f.ctrlKey && f.preventDefault(), !1;
    f.preventDefault(), f.stopImmediatePropagation();
    const p = n.property("__zoom").k || 1;
    if (f.ctrlKey && a) {
      const w = Je(f), m = Gl(f), E = p * Math.pow(2, m);
      o.scaleTo(n, E, w, f);
      return;
    }
    const d = f.deltaMode === 1 ? 20 : 1;
    let h = r === zt.Vertical ? 0 : f.deltaX * d, g = r === zt.Horizontal ? 0 : f.deltaY * d;
    !co() && f.shiftKey && r !== zt.Vertical && (h = f.deltaY * d, g = 0), o.translateBy(
      n,
      -(h / p) * s,
      -(g / p) * s,
      // @ts-ignore
      { internal: !0 }
    );
    const N = $r(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(f, N), e.panScrollTimeout = setTimeout(() => {
      l?.(f, N), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(f, N));
  };
}
function Wy({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const s = o.type === "wheel", a = !t && s && !o.ctrlKey, c = an(o, e);
    if (o.ctrlKey && s && c && o.preventDefault(), a || c)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function By({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = $r(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function Fy({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (s) => {
    e.usedRightMouseButton = !!(n && Zl(t, e.mouseButton ?? 0)), s.sourceEvent?.sync || o([s.transform.x, s.transform.y, s.transform.k]), r && !s.sourceEvent?.internal && r?.(s.sourceEvent, $r(s.transform));
  };
}
function Ky({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: s }) {
  return (a) => {
    if (!a.sourceEvent?.internal && (e.isZoomingOrPanning = !1, s && Zl(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && a.sourceEvent && s(a.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
      const c = $r(a.transform);
      e.prevViewport = c, clearTimeout(e.timerId), e.timerId = setTimeout(
        () => {
          r?.(a.sourceEvent, c);
        },
        // we need a setTimeout for panOnScroll to suppress multiple end events fired during scroll
        n ? 150 : 0
      );
    }
  };
}
function qy({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: s, userSelectionActive: a, noWheelClassName: c, noPanClassName: u, lib: l, connectionInProgress: f }) {
  return (p) => {
    const d = e || t, h = n && p.ctrlKey, g = p.type === "wheel";
    if (p.button === 1 && p.type === "mousedown" && (an(p, `${l}-flow__node`) || an(p, `${l}-flow__edge`)))
      return !0;
    if (!o && !d && !r && !s && !n || a || f && !g || an(p, c) && g || an(p, u) && (!g || r && g && !e) || !n && p.ctrlKey && g)
      return !1;
    if (!n && p.type === "touchstart" && p.touches?.length > 1)
      return p.preventDefault(), !1;
    if (!d && !r && !h && g || !o && (p.type === "mousedown" || p.type === "touchstart") || Array.isArray(o) && !o.includes(p.button) && p.type === "mousedown")
      return !1;
    const N = Array.isArray(o) && o.includes(p.button) || !p.button || p.button <= 1;
    return (!p.ctrlKey || g) && N;
  };
}
function Uy({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: s, onPanZoomStart: a, onPanZoomEnd: c, onDraggingChange: u }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, f = e.getBoundingClientRect(), p = Nl().scaleExtent([t, n]).translateExtent(o), d = Ue(e).call(p);
  E({
    x: r.x,
    y: r.y,
    zoom: gn(r.zoom, t, n)
  }, [
    [0, 0],
    [f.width, f.height]
  ], o);
  const h = d.on("wheel.zoom"), g = d.on("dblclick.zoom");
  p.wheelDelta(Gl);
  async function N(D, A) {
    return d ? new Promise((L) => {
      p?.interpolate(A?.interpolate === "linear" ? Zn : Yo).transform(di(d, A?.duration, A?.ease, () => L(!0)), D);
    }) : !1;
  }
  function w({ noWheelClassName: D, noPanClassName: A, onPaneContextMenu: L, userSelectionActive: k, panOnScroll: M, panOnDrag: I, panOnScrollMode: T, panOnScrollSpeed: z, preventScrolling: P, zoomOnPinch: K, zoomOnScroll: B, zoomOnDoubleClick: F, zoomActivationKeyPressed: X, lib: J, onTransformChange: U, connectionInProgress: ce, paneClickDistance: G, selectionOnDrag: O }) {
    k && !l.isZoomingOrPanning && m();
    const Y = M && !X && !k;
    p.clickDistance(O ? 1 / 0 : !et(G) || G < 0 ? 0 : G);
    const se = Y ? Hy({
      zoomPanValues: l,
      noWheelClassName: D,
      d3Selection: d,
      d3Zoom: p,
      panOnScrollMode: T,
      panOnScrollSpeed: z,
      zoomOnPinch: K,
      onPanZoomStart: a,
      onPanZoom: s,
      onPanZoomEnd: c
    }) : Wy({
      noWheelClassName: D,
      preventScrolling: P,
      d3ZoomHandler: h
    });
    d.on("wheel.zoom", se, { passive: !1 });
    const ae = By({
      zoomPanValues: l,
      onDraggingChange: u,
      onPanZoomStart: a
    });
    p.on("start", ae);
    const te = Fy({
      zoomPanValues: l,
      panOnDrag: I,
      onPaneContextMenu: !!L,
      onPanZoom: s,
      onTransformChange: U
    });
    p.on("zoom", te);
    const oe = Ky({
      zoomPanValues: l,
      panOnDrag: I,
      panOnScroll: M,
      onPaneContextMenu: L,
      onPanZoomEnd: c,
      onDraggingChange: u
    });
    p.on("end", oe);
    const fe = qy({
      zoomActivationKeyPressed: X,
      panOnDrag: I,
      zoomOnScroll: B,
      panOnScroll: M,
      zoomOnDoubleClick: F,
      zoomOnPinch: K,
      userSelectionActive: k,
      noPanClassName: A,
      noWheelClassName: D,
      lib: J,
      connectionInProgress: ce
    });
    p.filter(fe), F ? d.on("dblclick.zoom", g) : d.on("dblclick.zoom", null);
  }
  function m() {
    p.on("zoom", null);
  }
  async function E(D, A, L) {
    const k = ui(D), M = p?.constrain()(k, A, L);
    return M && await N(M), M;
  }
  async function y(D, A) {
    const L = ui(D);
    return await N(L, A), L;
  }
  function b(D) {
    if (d) {
      const A = ui(D), L = d.property("__zoom");
      (L.k !== D.zoom || L.x !== D.x || L.y !== D.y) && p?.transform(d, A, null, { sync: !0 });
    }
  }
  function j() {
    const D = d ? bl(d.node()) : { x: 0, y: 0, k: 1 };
    return { x: D.x, y: D.y, zoom: D.k };
  }
  async function v(D, A) {
    return d ? new Promise((L) => {
      p?.interpolate(A?.interpolate === "linear" ? Zn : Yo).scaleTo(di(d, A?.duration, A?.ease, () => L(!0)), D);
    }) : !1;
  }
  async function S(D, A) {
    return d ? new Promise((L) => {
      p?.interpolate(A?.interpolate === "linear" ? Zn : Yo).scaleBy(di(d, A?.duration, A?.ease, () => L(!0)), D);
    }) : !1;
  }
  function _(D) {
    p?.scaleExtent(D);
  }
  function $(D) {
    p?.translateExtent(D);
  }
  function V(D) {
    const A = !et(D) || D < 0 ? 0 : D;
    p?.clickDistance(A);
  }
  return {
    update: w,
    destroy: m,
    setViewport: y,
    setViewportConstrained: E,
    getViewport: j,
    scaleTo: v,
    scaleBy: S,
    setScaleExtent: _,
    setTranslateExtent: $,
    syncViewport: b,
    setClickDistance: V
  };
}
var xn;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(xn || (xn = {}));
function Xy({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: s }) {
  const a = e - t, c = n - o, u = [a > 0 ? 1 : a < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return a && r && (u[0] = u[0] * -1), c && s && (u[1] = u[1] * -1), u;
}
function ja(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: r
  };
}
function vt(e, t) {
  return Math.max(0, t - e);
}
function bt(e, t) {
  return Math.max(0, e - t);
}
function Bo(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function Sa(e, t) {
  return e ? !t : t;
}
function Yy(e, t, n, o, r, s, a, c) {
  let { affectsX: u, affectsY: l } = t;
  const { isHorizontal: f, isVertical: p } = t, d = f && p, { xSnapped: h, ySnapped: g } = n, { minWidth: N, maxWidth: w, minHeight: m, maxHeight: E } = o, { x: y, y: b, width: j, height: v, aspectRatio: S } = e;
  let _ = Math.floor(f ? h - e.pointerX : 0), $ = Math.floor(p ? g - e.pointerY : 0);
  const V = j + (u ? -_ : _), D = v + (l ? -$ : $), A = -s[0] * j, L = -s[1] * v;
  let k = Bo(V, N, w), M = Bo(D, m, E);
  if (a) {
    let z = 0, P = 0;
    u && _ < 0 ? z = vt(y + _ + A, a[0][0]) : !u && _ > 0 && (z = bt(y + V + A, a[1][0])), l && $ < 0 ? P = vt(b + $ + L, a[0][1]) : !l && $ > 0 && (P = bt(b + D + L, a[1][1])), k = Math.max(k, z), M = Math.max(M, P);
  }
  if (c) {
    let z = 0, P = 0;
    u && _ > 0 ? z = bt(y + _, c[0][0]) : !u && _ < 0 && (z = vt(y + V, c[1][0])), l && $ > 0 ? P = bt(b + $, c[0][1]) : !l && $ < 0 && (P = vt(b + D, c[1][1])), k = Math.max(k, z), M = Math.max(M, P);
  }
  if (r) {
    if (f) {
      const z = Bo(V / S, m, E) * S;
      if (k = Math.max(k, z), a) {
        let P = 0;
        !u && !l || u && !l && d ? P = bt(b + L + V / S, a[1][1]) * S : P = vt(b + L + (u ? _ : -_) / S, a[0][1]) * S, k = Math.max(k, P);
      }
      if (c) {
        let P = 0;
        !u && !l || u && !l && d ? P = vt(b + V / S, c[1][1]) * S : P = bt(b + (u ? _ : -_) / S, c[0][1]) * S, k = Math.max(k, P);
      }
    }
    if (p) {
      const z = Bo(D * S, N, w) / S;
      if (M = Math.max(M, z), a) {
        let P = 0;
        !u && !l || l && !u && d ? P = bt(y + D * S + A, a[1][0]) / S : P = vt(y + (l ? $ : -$) * S + A, a[0][0]) / S, M = Math.max(M, P);
      }
      if (c) {
        let P = 0;
        !u && !l || l && !u && d ? P = vt(y + D * S, c[1][0]) / S : P = bt(y + (l ? $ : -$) * S, c[0][0]) / S, M = Math.max(M, P);
      }
    }
  }
  $ = $ + ($ < 0 ? M : -M), _ = _ + (_ < 0 ? k : -k), r && (d ? V > D * S ? $ = (Sa(u, l) ? -_ : _) / S : _ = (Sa(u, l) ? -$ : $) * S : f ? ($ = _ / S, l = u) : (_ = $ * S, u = l));
  const I = u ? y + _ : y, T = l ? b + $ : b;
  return {
    width: j + (u ? -_ : _),
    height: v + (l ? -$ : $),
    x: s[0] * _ * (u ? -1 : 1) + I,
    y: s[1] * $ * (l ? -1 : 1) + T
  };
}
const Jl = { width: 0, height: 0, x: 0, y: 0 }, Zy = {
  ...Jl,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function Gy(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, s = e.measured.width ?? 0, a = e.measured.height ?? 0, c = n[0] * s, u = n[1] * a;
  return [
    [o - c, r - u],
    [o + s - c, r + a - u]
  ];
}
function Jy({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const s = Ue(e);
  let a = {
    controlDirection: ja("bottom-right"),
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
    let m = { ...Jl }, E = { ...Zy };
    a = {
      boundaries: f,
      resizeDirection: d,
      keepAspectRatio: p,
      controlDirection: ja(l)
    };
    let y, b = null, j = [], v, S, _, $ = !1;
    const V = al().on("start", (D) => {
      const { nodeLookup: A, transform: L, snapGrid: k, snapToGrid: M, nodeOrigin: I, paneDomNode: T } = n();
      if (y = A.get(t), !y)
        return;
      b = T?.getBoundingClientRect() ?? null;
      const { xSnapped: z, ySnapped: P } = Gn(D.sourceEvent, {
        transform: L,
        snapGrid: k,
        snapToGrid: M,
        containerBounds: b
      });
      m = {
        width: y.measured.width ?? 0,
        height: y.measured.height ?? 0,
        x: y.position.x ?? 0,
        y: y.position.y ?? 0
      }, E = {
        ...m,
        pointerX: z,
        pointerY: P,
        aspectRatio: m.width / m.height
      }, v = void 0, S = Ht(y.extent) ? y.extent : void 0, y.parentId && (y.extent === "parent" || y.expandParent) && (v = A.get(y.parentId)), v && y.extent === "parent" && (S = [
        [0, 0],
        [v.measured.width, v.measured.height]
      ]), j = [], _ = void 0;
      for (const [K, B] of A)
        if (B.parentId === t && (j.push({
          id: K,
          position: { ...B.position },
          extent: B.extent
        }), B.extent === "parent" || B.expandParent)) {
          const F = Gy(B, y, B.origin ?? I);
          _ ? _ = [
            [Math.min(F[0][0], _[0][0]), Math.min(F[0][1], _[0][1])],
            [Math.max(F[1][0], _[1][0]), Math.max(F[1][1], _[1][1])]
          ] : _ = F;
        }
      h?.(D, { ...m });
    }).on("drag", (D) => {
      const { transform: A, snapGrid: L, snapToGrid: k, nodeOrigin: M } = n(), I = Gn(D.sourceEvent, {
        transform: A,
        snapGrid: L,
        snapToGrid: k,
        containerBounds: b
      }), T = [];
      if (!y)
        return;
      const { x: z, y: P, width: K, height: B } = m, F = {}, X = y.origin ?? M, { width: J, height: U, x: ce, y: G } = Yy(E, a.controlDirection, I, a.boundaries, a.keepAspectRatio, X, S, _), O = J !== K, Y = U !== B, se = ce !== z && O, ae = G !== P && Y;
      if (!se && !ae && !O && !Y)
        return;
      if ((se || ae || X[0] === 1 || X[1] === 1) && (F.x = se ? ce : m.x, F.y = ae ? G : m.y, m.x = F.x, m.y = F.y, j.length > 0)) {
        const W = ce - z, ee = G - P;
        for (const me of j)
          me.position = {
            x: me.position.x - W + X[0] * (J - K),
            y: me.position.y - ee + X[1] * (U - B)
          }, T.push(me);
      }
      if ((O || Y) && (F.width = O && (!a.resizeDirection || a.resizeDirection === "horizontal") ? J : m.width, F.height = Y && (!a.resizeDirection || a.resizeDirection === "vertical") ? U : m.height, m.width = F.width, m.height = F.height), v && y.expandParent) {
        const W = X[0] * (F.width ?? 0);
        F.x && F.x < W && (m.x = W, E.x = E.x - (F.x - W));
        const ee = X[1] * (F.height ?? 0);
        F.y && F.y < ee && (m.y = ee, E.y = E.y - (F.y - ee));
      }
      const te = Xy({
        width: m.width,
        prevWidth: K,
        height: m.height,
        prevHeight: B,
        affectsX: a.controlDirection.affectsX,
        affectsY: a.controlDirection.affectsY
      }), oe = { ...m, direction: te };
      w?.(D, oe) !== !1 && ($ = !0, g?.(D, oe), o(F, T));
    }).on("end", (D) => {
      $ && (N?.(D, { ...m }), r?.({ ...m }), $ = !1);
    });
    s.call(V);
  }
  function u() {
    s.on(".drag", null);
  }
  return {
    update: c,
    destroy: u
  };
}
var fi = { exports: {} }, pi = {}, hi = { exports: {} }, gi = {};
var ka;
function Qy() {
  if (ka) return gi;
  ka = 1;
  var e = st;
  function t(p, d) {
    return p === d && (p !== 0 || 1 / p === 1 / d) || p !== p && d !== d;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, s = e.useLayoutEffect, a = e.useDebugValue;
  function c(p, d) {
    var h = d(), g = o({ inst: { value: h, getSnapshot: d } }), N = g[0].inst, w = g[1];
    return s(
      function() {
        N.value = h, N.getSnapshot = d, u(N) && w({ inst: N });
      },
      [p, h, d]
    ), r(
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
  return gi.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : f, gi;
}
var Ea;
function em() {
  return Ea || (Ea = 1, hi.exports = Qy()), hi.exports;
}
var Ca;
function tm() {
  if (Ca) return pi;
  Ca = 1;
  var e = st, t = em();
  function n(l, f) {
    return l === f && (l !== 0 || 1 / l === 1 / f) || l !== l && f !== f;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, s = e.useRef, a = e.useEffect, c = e.useMemo, u = e.useDebugValue;
  return pi.useSyncExternalStoreWithSelector = function(l, f, p, d, h) {
    var g = s(null);
    if (g.current === null) {
      var N = { hasValue: !1, value: null };
      g.current = N;
    } else N = g.current;
    g = c(
      function() {
        function m(v) {
          if (!E) {
            if (E = !0, y = v, v = d(v), h !== void 0 && N.hasValue) {
              var S = N.value;
              if (h(S, v))
                return b = S;
            }
            return b = v;
          }
          if (S = b, o(y, v)) return S;
          var _ = d(v);
          return h !== void 0 && h(S, _) ? (y = v, S) : (y = v, b = _);
        }
        var E = !1, y, b, j = p === void 0 ? null : p;
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
    var w = r(l, g[0], g[1]);
    return a(
      function() {
        N.hasValue = !0, N.value = w;
      },
      [w]
    ), u(w), w;
  }, pi;
}
var Ia;
function nm() {
  return Ia || (Ia = 1, fi.exports = tm()), fi.exports;
}
var om = nm();
const rm = /* @__PURE__ */ vf(om), im = {}, Aa = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (f, p) => {
    const d = typeof f == "function" ? f(t) : f;
    if (!Object.is(d, t)) {
      const h = t;
      t = p ?? (typeof d != "object" || d === null) ? d : Object.assign({}, t, d), n.forEach((g) => g(t, h));
    }
  }, r = () => t, u = { setState: o, getState: r, getInitialState: () => l, subscribe: (f) => (n.add(f), () => n.delete(f)), destroy: () => {
    (im ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(o, r, u);
  return u;
}, sm = (e) => e ? Aa(e) : Aa, { useDebugValue: am } = st, { useSyncExternalStoreWithSelector: cm } = rm, lm = (e) => e;
function Ql(e, t = lm, n) {
  const o = cm(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return am(o), o;
}
const _a = (e, t) => {
  const n = sm(e), o = (r, s = t) => Ql(n, r, s);
  return Object.assign(o, n), o;
}, um = (e, t) => e ? _a(e, t) : _a;
function Ne(e, t) {
  if (Object.is(e, t))
    return !0;
  if (typeof e != "object" || e === null || typeof t != "object" || t === null)
    return !1;
  if (e instanceof Map && t instanceof Map) {
    if (e.size !== t.size) return !1;
    for (const [o, r] of e)
      if (!Object.is(r, t.get(o)))
        return !1;
    return !0;
  }
  if (e instanceof Set && t instanceof Set) {
    if (e.size !== t.size) return !1;
    for (const o of e)
      if (!t.has(o))
        return !1;
    return !0;
  }
  const n = Object.keys(e);
  if (n.length !== Object.keys(t).length)
    return !1;
  for (const o of n)
    if (!Object.prototype.hasOwnProperty.call(t, o) || !Object.is(e[o], t[o]))
      return !1;
  return !0;
}
var yi = { exports: {} }, Te = {};
var Da;
function dm() {
  if (Da) return Te;
  Da = 1;
  var e = st;
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
  var o = {
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
  }, r = /* @__PURE__ */ Symbol.for("react.portal");
  function s(u, l, f) {
    var p = 3 < arguments.length && arguments[3] !== void 0 ? arguments[3] : null;
    return {
      $$typeof: r,
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
  return Te.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, Te.createPortal = function(u, l) {
    var f = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!l || l.nodeType !== 1 && l.nodeType !== 9 && l.nodeType !== 11)
      throw Error(t(299));
    return s(u, l, null, f);
  }, Te.flushSync = function(u) {
    var l = a.T, f = o.p;
    try {
      if (a.T = null, o.p = 2, u) return u();
    } finally {
      a.T = l, o.p = f, o.d.f();
    }
  }, Te.preconnect = function(u, l) {
    typeof u == "string" && (l ? (l = l.crossOrigin, l = typeof l == "string" ? l === "use-credentials" ? l : "" : void 0) : l = null, o.d.C(u, l));
  }, Te.prefetchDNS = function(u) {
    typeof u == "string" && o.d.D(u);
  }, Te.preinit = function(u, l) {
    if (typeof u == "string" && l && typeof l.as == "string") {
      var f = l.as, p = c(f, l.crossOrigin), d = typeof l.integrity == "string" ? l.integrity : void 0, h = typeof l.fetchPriority == "string" ? l.fetchPriority : void 0;
      f === "style" ? o.d.S(
        u,
        typeof l.precedence == "string" ? l.precedence : void 0,
        {
          crossOrigin: p,
          integrity: d,
          fetchPriority: h
        }
      ) : f === "script" && o.d.X(u, {
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
          o.d.M(u, {
            crossOrigin: f,
            integrity: typeof l.integrity == "string" ? l.integrity : void 0,
            nonce: typeof l.nonce == "string" ? l.nonce : void 0
          });
        }
      } else l == null && o.d.M(u);
  }, Te.preload = function(u, l) {
    if (typeof u == "string" && typeof l == "object" && l !== null && typeof l.as == "string") {
      var f = l.as, p = c(f, l.crossOrigin);
      o.d.L(u, f, {
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
        o.d.m(u, {
          as: typeof l.as == "string" && l.as !== "script" ? l.as : void 0,
          crossOrigin: f,
          integrity: typeof l.integrity == "string" ? l.integrity : void 0
        });
      } else o.d.m(u);
  }, Te.requestFormReset = function(u) {
    o.d.r(u);
  }, Te.unstable_batchedUpdates = function(u, l) {
    return u(l);
  }, Te.useFormState = function(u, l, f) {
    return a.H.useFormState(u, l, f);
  }, Te.useFormStatus = function() {
    return a.H.useHostTransitionStatus();
  }, Te.version = "19.2.7", Te;
}
var Ma;
function fm() {
  if (Ma) return yi.exports;
  Ma = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), yi.exports = dm(), yi.exports;
}
var pm = fm();
const Pr = Yi(null), hm = Pr.Provider, eu = Ye.error001("react");
function he(e, t) {
  const n = po(Pr);
  if (n === null)
    throw new Error(eu);
  return Ql(n, e, t);
}
function je() {
  const e = po(Pr);
  if (e === null)
    throw new Error(eu);
  return de(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const Ta = { display: "none" }, gm = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, tu = "react-flow__node-desc", nu = "react-flow__edge-desc", ym = "react-flow__aria-live", mm = (e) => e.ariaLiveMessage, xm = (e) => e.ariaLabelConfig;
function wm({ rfId: e }) {
  const t = he(mm);
  return i.jsx("div", { id: `${ym}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: gm, children: t });
}
function vm({ rfId: e, disableKeyboardA11y: t }) {
  const n = he(xm);
  return i.jsxs(i.Fragment, { children: [i.jsx("div", { id: `${tu}-${e}`, style: Ta, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), i.jsx("div", { id: `${nu}-${e}`, style: Ta, children: n["edge.a11yDescription.default"] }), !t && i.jsx(wm, { rfId: e })] });
}
const zr = Er(({ position: e = "top-left", children: t, className: n, style: o, ...r }, s) => {
  const a = `${e}`.split("-");
  return i.jsx("div", { className: Ee(["react-flow__panel", n, ...a]), style: o, ref: s, ...r, children: t });
});
zr.displayName = "Panel";
function bm({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : i.jsx(zr, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: i.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const Nm = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, Fo = (e) => e.id;
function jm(e, t) {
  return Ne(e.selectedNodes.map(Fo), t.selectedNodes.map(Fo)) && Ne(e.selectedEdges.map(Fo), t.selectedEdges.map(Fo));
}
function Sm({ onSelectionChange: e }) {
  const t = je(), { selectedNodes: n, selectedEdges: o } = he(Nm, jm);
  return Q(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((s) => s(r));
  }, [n, o, e]), null;
}
const km = (e) => !!e.onSelectionChangeHandlers;
function Em({ onSelectionChange: e }) {
  const t = he(km);
  return e || t ? i.jsx(Sm, { onSelectionChange: e }) : null;
}
const ou = [0, 0], Cm = { x: 0, y: 0, zoom: 1 }, Im = [
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
], $a = [...Im, "rfId"], Am = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), Pa = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: io,
  nodeOrigin: ou,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function _m(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: s, setNodeExtent: a, reset: c, setDefaultNodesAndEdges: u } = he(Am, Ne), l = je();
  Q(() => (u(e.defaultNodes, e.defaultEdges), () => {
    f.current = Pa, c();
  }), []);
  const f = ie(Pa);
  return Q(
    () => {
      for (const p of $a) {
        const d = e[p], h = f.current[p];
        d !== h && (typeof e[p] > "u" || (p === "nodes" ? t(d) : p === "edges" ? n(d) : p === "minZoom" ? o(d) : p === "maxZoom" ? r(d) : p === "translateExtent" ? s(d) : p === "nodeExtent" ? a(d) : p === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: dy(d) }) : p === "fitView" ? l.setState({ fitViewQueued: d }) : p === "fitViewOptions" ? l.setState({ fitViewOptions: d }) : l.setState({ [p]: d })));
      }
      f.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    $a.map((p) => e[p])
  ), null;
}
function za() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function Dm(e) {
  const [t, n] = q(e === "system" ? null : e);
  return Q(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = za(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : za()?.matches ? "dark" : "light";
}
const Ra = typeof document < "u" ? document : null;
function lo(e = null, t = { target: Ra, actInsideInputWithModifier: !0 }) {
  const [n, o] = q(!1), r = ie(!1), s = ie(/* @__PURE__ */ new Set([])), [a, c] = de(() => {
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
    const u = t?.target ?? Ra, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const f = (h) => {
        if (r.current = h.ctrlKey || h.metaKey || h.shiftKey || h.altKey, (!r.current || r.current && !l) && Pl(h))
          return !1;
        const N = Va(h.code, c);
        if (s.current.add(h[N]), La(a, s.current, !1)) {
          const w = h.composedPath?.()?.[0] || h.target, m = w?.nodeName === "BUTTON" || w?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !m) && h.preventDefault(), o(!0);
        }
      }, p = (h) => {
        const g = Va(h.code, c);
        La(a, s.current, !0) ? (o(!1), s.current.clear()) : s.current.delete(h[g]), h.key === "Meta" && s.current.clear(), r.current = !1;
      }, d = () => {
        s.current.clear(), o(!1);
      };
      return u?.addEventListener("keydown", f), u?.addEventListener("keyup", p), window.addEventListener("blur", d), window.addEventListener("contextmenu", d), () => {
        u?.removeEventListener("keydown", f), u?.removeEventListener("keyup", p), window.removeEventListener("blur", d), window.removeEventListener("contextmenu", d);
      };
    }
  }, [e, o]), n;
}
function La(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function Va(e, t) {
  return t.includes(e) ? "code" : "key";
}
const Mm = () => {
  const e = je();
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
      const { panZoom: o } = e.getState();
      return o ? o.scaleTo(t, n) : !1;
    },
    getZoom: () => e.getState().transform[2],
    setViewport: async (t, n) => {
      const { transform: [o, r, s], panZoom: a } = e.getState();
      return a ? (await a.setViewport({
        x: t.x ?? o,
        y: t.y ?? r,
        zoom: t.zoom ?? s
      }, n), !0) : !1;
    },
    getViewport: () => {
      const [t, n, o] = e.getState().transform;
      return { x: t, y: n, zoom: o };
    },
    setCenter: async (t, n, o) => e.getState().setCenter(t, n, o),
    fitBounds: async (t, n) => {
      const { width: o, height: r, minZoom: s, maxZoom: a, panZoom: c } = e.getState(), u = ss(t, o, r, s, a, n?.padding ?? 0.1);
      return c ? (await c.setViewport(u, {
        duration: n?.duration,
        ease: n?.ease,
        interpolate: n?.interpolate
      }), !0) : !1;
    },
    screenToFlowPosition: (t, n = {}) => {
      const { transform: o, snapGrid: r, snapToGrid: s, domNode: a } = e.getState();
      if (!a)
        return t;
      const { x: c, y: u } = a.getBoundingClientRect(), l = {
        x: t.x - c,
        y: t.y - u
      }, f = n.snapGrid ?? r, p = n.snapToGrid ?? s;
      return Nn(l, o, p, f);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: r, y: s } = o.getBoundingClientRect(), a = mn(t, n);
      return {
        x: a.x + r,
        y: a.y + s
      };
    }
  }), []);
};
function ru(e, t) {
  const n = [], o = /* @__PURE__ */ new Map(), r = [];
  for (const s of e)
    if (s.type === "add") {
      r.push(s);
      continue;
    } else if (s.type === "remove" || s.type === "replace")
      o.set(s.id, [s]);
    else {
      const a = o.get(s.id);
      a ? a.push(s) : o.set(s.id, [s]);
    }
  for (const s of t) {
    const a = o.get(s.id);
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
      Tm(u, c);
    n.push(c);
  }
  return r.length && r.forEach((s) => {
    s.index !== void 0 ? n.splice(s.index, 0, { ...s.item }) : n.push({ ...s.item });
  }), n;
}
function Tm(e, t) {
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
function iu(e, t) {
  return ru(e, t);
}
function su(e, t) {
  return ru(e, t);
}
function Tt(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function cn(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [r, s] of e) {
    const a = t.has(r);
    !(s.selected === void 0 && !a) && s.selected !== a && (n && (s.selected = a), o.push(Tt(s.id, a)));
  }
  return o;
}
function Oa({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((r) => [r.id, r]));
  for (const [r, s] of e.entries()) {
    const a = t.get(s.id), c = a?.internals?.userNode ?? a;
    c !== void 0 && c !== s && n.push({ id: s.id, item: s, type: "replace" }), c === void 0 && n.push({ item: s, type: "add", index: r });
  }
  for (const [r] of t)
    o.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function Ha(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const au = Dl();
function cu(e, t, n = {}) {
  return yy(e, t, {
    ...n,
    onError: n.onError ?? au
  });
}
function $m(e, t, n, o = { shouldReplaceId: !0 }) {
  return my(e, t, n, {
    ...o,
    onError: o.onError ?? au
  });
}
const Wa = (e) => ny(e), Pm = (e) => Cl(e);
function lu(e) {
  return Er(e);
}
const zm = typeof window < "u" ? gf : Q;
function Ba(e) {
  const [t, n] = q(BigInt(0)), [o] = q(() => Rm(() => n((r) => r + BigInt(1))));
  return zm(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function Rm(e) {
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
const uu = Yi(null);
function Lm({ children: e }) {
  const t = je(), n = le((c) => {
    const { nodes: u = [], setNodes: l, hasDefaultNodes: f, onNodesChange: p, nodeLookup: d, fitViewQueued: h, onNodesChangeMiddlewareMap: g } = t.getState();
    let N = u;
    for (const m of c)
      N = typeof m == "function" ? m(N) : m;
    let w = Oa({
      items: N,
      lookup: d
    });
    for (const m of g.values())
      w = m(w);
    f && l(N), w.length > 0 ? p?.(w) : h && window.requestAnimationFrame(() => {
      const { fitViewQueued: m, nodes: E, setNodes: y } = t.getState();
      m && y(E);
    });
  }, []), o = Ba(n), r = le((c) => {
    const { edges: u = [], setEdges: l, hasDefaultEdges: f, onEdgesChange: p, edgeLookup: d } = t.getState();
    let h = u;
    for (const g of c)
      h = typeof g == "function" ? g(h) : g;
    f ? l(h) : p && p(Oa({
      items: h,
      lookup: d
    }));
  }, []), s = Ba(r), a = de(() => ({ nodeQueue: o, edgeQueue: s }), []);
  return i.jsx(uu.Provider, { value: a, children: e });
}
function Vm() {
  const e = po(uu);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Om = (e) => !!e.panZoom;
function ps() {
  const e = Mm(), t = je(), n = Vm(), o = he(Om), r = de(() => {
    const s = (p) => t.getState().nodeLookup.get(p), a = (p) => {
      n.nodeQueue.push(p);
    }, c = (p) => {
      n.edgeQueue.push(p);
    }, u = (p) => {
      const { nodeLookup: d, nodeOrigin: h } = t.getState(), g = Wa(p) ? p : d.get(p.id), N = g.parentId ? Tl(g.position, g.measured, g.parentId, d, h) : g.position, w = {
        ...g,
        position: N,
        width: g.measured?.width ?? g.width,
        height: g.measured?.height ?? g.height
      };
      return yn(w);
    }, l = (p, d, h = { replace: !1 }) => {
      a((g) => g.map((N) => {
        if (N.id === p) {
          const w = typeof d == "function" ? d(N) : d;
          return h.replace && Wa(w) ? w : { ...N, ...w };
        }
        return N;
      }));
    }, f = (p, d, h = { replace: !1 }) => {
      c((g) => g.map((N) => {
        if (N.id === p) {
          const w = typeof d == "function" ? d(N) : d;
          return h.replace && Pm(w) ? w : { ...N, ...w };
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
        const { nodes: h, edges: g, onNodesDelete: N, onEdgesDelete: w, triggerNodeChanges: m, triggerEdgeChanges: E, onDelete: y, onBeforeDelete: b } = t.getState(), { nodes: j, edges: v } = await ay({
          nodesToRemove: p,
          edgesToRemove: d,
          nodes: h,
          edges: g,
          onBeforeDelete: b
        }), S = v.length > 0, _ = j.length > 0;
        if (S) {
          const $ = v.map(Ha);
          w?.(v), E($);
        }
        if (_) {
          const $ = j.map(Ha);
          N?.(j), m($);
        }
        return (_ || S) && y?.({ nodes: j, edges: v }), { deletedNodes: j, deletedEdges: v };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (p, d = !0, h) => {
        const g = fa(p), N = g ? p : u(p), w = h !== void 0;
        return N ? (h || t.getState().nodes).filter((m) => {
          const E = t.getState().nodeLookup.get(m.id);
          if (E && !g && (m.id === p.id || !E.internals.positionAbsolute))
            return !1;
          const y = yn(w ? m : E), b = ao(y, N);
          return d && b > 0 || b >= y.width * y.height || b >= N.width * N.height;
        }) : [];
      },
      isNodeIntersecting: (p, d, h = !0) => {
        const N = fa(p) ? p : u(p);
        if (!N)
          return !1;
        const w = ao(N, d);
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
        return oy(p, { nodeLookup: d, nodeOrigin: h });
      },
      getHandleConnections: ({ type: p, id: d, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}-${p}${d ? `-${d}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: p, handleId: d, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}${p ? d ? `-${p}-${d}` : `-${p}` : ""}`)?.values() ?? []),
      fitView: async (p) => {
        const d = t.getState().fitViewResolver ?? uy();
        return t.setState({ fitViewQueued: !0, fitViewOptions: p, fitViewResolver: d }), n.nodeQueue.push((h) => [...h]), d.promise;
      }
    };
  }, []);
  return de(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const Fa = (e) => e.selected, Hm = typeof window < "u" ? window : void 0;
function Wm({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = je(), { deleteElements: o } = ps(), r = lo(e, { actInsideInputWithModifier: !1 }), s = lo(t, { target: Hm });
  Q(() => {
    if (r) {
      const { edges: a, nodes: c } = n.getState();
      o({ nodes: c.filter(Fa), edges: a.filter(Fa) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), Q(() => {
    n.setState({ multiSelectionActive: s });
  }, [s]);
}
function Bm(e) {
  const t = je();
  Q(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = as(e.current);
      (o.height === 0 || o.width === 0) && t.getState().onError?.("004", Ye.error004()), t.setState({ width: o.width || 500, height: o.height || 500 });
    };
    if (e.current) {
      n(), window.addEventListener("resize", n);
      const o = new ResizeObserver(() => n());
      return o.observe(e.current), () => {
        window.removeEventListener("resize", n), o && e.current && o.unobserve(e.current);
      };
    }
  }, []);
}
const Rr = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, Fm = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function Km({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: s = zt.Free, zoomOnDoubleClick: a = !0, panOnDrag: c = !0, defaultViewport: u, translateExtent: l, minZoom: f, maxZoom: p, zoomActivationKeyCode: d, preventScrolling: h = !0, children: g, noWheelClassName: N, noPanClassName: w, onViewportChange: m, isControlledViewport: E, paneClickDistance: y, selectionOnDrag: b }) {
  const j = je(), v = ie(null), { userSelectionActive: S, lib: _, connectionInProgress: $ } = he(Fm, Ne), V = lo(d), D = ie();
  Bm(v);
  const A = le((L) => {
    m?.({ x: L[0], y: L[1], zoom: L[2] }), E || j.setState({ transform: L });
  }, [m, E]);
  return Q(() => {
    if (v.current) {
      D.current = Uy({
        domNode: v.current,
        minZoom: f,
        maxZoom: p,
        translateExtent: l,
        viewport: u,
        onDraggingChange: (I) => j.setState((T) => T.paneDragging === I ? T : { paneDragging: I }),
        onPanZoomStart: (I, T) => {
          const { onViewportChangeStart: z, onMoveStart: P } = j.getState();
          P?.(I, T), z?.(T);
        },
        onPanZoom: (I, T) => {
          const { onViewportChange: z, onMove: P } = j.getState();
          P?.(I, T), z?.(T);
        },
        onPanZoomEnd: (I, T) => {
          const { onViewportChangeEnd: z, onMoveEnd: P } = j.getState();
          P?.(I, T), z?.(T);
        }
      });
      const { x: L, y: k, zoom: M } = D.current.getViewport();
      return j.setState({
        panZoom: D.current,
        transform: [L, k, M],
        domNode: v.current.closest(".react-flow")
      }), () => {
        D.current?.destroy();
      };
    }
  }, []), Q(() => {
    D.current?.update({
      onPaneContextMenu: e,
      zoomOnScroll: t,
      zoomOnPinch: n,
      panOnScroll: o,
      panOnScrollSpeed: r,
      panOnScrollMode: s,
      zoomOnDoubleClick: a,
      panOnDrag: c,
      zoomActivationKeyPressed: V,
      preventScrolling: h,
      noPanClassName: w,
      userSelectionActive: S,
      noWheelClassName: N,
      lib: _,
      onTransformChange: A,
      connectionInProgress: $,
      selectionOnDrag: b,
      paneClickDistance: y
    });
  }, [
    e,
    t,
    n,
    o,
    r,
    s,
    a,
    c,
    V,
    h,
    w,
    S,
    N,
    _,
    A,
    $,
    b,
    y
  ]), i.jsx("div", { className: "react-flow__renderer", ref: v, style: Rr, children: g });
}
const qm = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function Um() {
  const { userSelectionActive: e, userSelectionRect: t } = he(qm, Ne);
  return e && t ? i.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const mi = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, Xm = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function Ym({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = so.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: s, selectionOnDrag: a, onSelectionStart: c, onSelectionEnd: u, onPaneClick: l, onPaneContextMenu: f, onPaneScroll: p, onPaneMouseEnter: d, onPaneMouseMove: h, onPaneMouseLeave: g, children: N }) {
  const w = ie(0), m = je(), { userSelectionActive: E, elementsSelectable: y, dragging: b, connectionInProgress: j, panBy: v, autoPanSpeed: S } = he(Xm, Ne), _ = y && (e || E), $ = ie(null), V = ie(), D = ie(/* @__PURE__ */ new Set()), A = ie(/* @__PURE__ */ new Set()), L = ie(!1), k = ie({ x: 0, y: 0 }), M = ie(!1), I = (O) => {
    if (L.current || j) {
      L.current = !1;
      return;
    }
    l?.(O), m.getState().resetSelectedElements(), m.setState({ nodesSelectionActive: !1 });
  }, T = (O) => {
    if (Array.isArray(o) && o?.includes(2)) {
      O.preventDefault();
      return;
    }
    f?.(O);
  }, z = p ? (O) => p(O) : void 0, P = (O) => {
    L.current && (O.stopPropagation(), L.current = !1);
  }, K = (O) => {
    const { domNode: Y, transform: se } = m.getState();
    if (V.current = Y?.getBoundingClientRect(), !V.current)
      return;
    const ae = O.target === $.current;
    if (!ae && !!O.target.closest(".nokey") || !e || !(a && ae || t) || O.button !== 0 || !O.isPrimary)
      return;
    O.target?.setPointerCapture?.(O.pointerId), L.current = !1;
    const { x: fe, y: W } = tt(O.nativeEvent, V.current), ee = Nn({ x: fe, y: W }, se);
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
  function B(O, Y) {
    const { userSelectionRect: se } = m.getState();
    if (!se)
      return;
    const { transform: ae, nodeLookup: te, edgeLookup: oe, connectionLookup: fe, triggerNodeChanges: W, triggerEdgeChanges: ee, defaultEdgeOptions: me } = m.getState(), ve = { x: se.startX, y: se.startY }, { x: $e, y: De } = mn(ve, ae), Le = {
      startX: ve.x,
      startY: ve.y,
      x: O < $e ? O : $e,
      y: Y < De ? Y : De,
      width: Math.abs(O - $e),
      height: Math.abs(Y - De)
    }, Pe = D.current, Ze = A.current;
    D.current = new Set(rs(te, Le, ae, n === so.Partial, !0).map((Ce) => Ce.id)), A.current = /* @__PURE__ */ new Set();
    const Ae = me?.selectable ?? !0;
    for (const Ce of D.current) {
      const xe = fe.get(Ce);
      if (xe)
        for (const { edgeId: ze } of xe.values()) {
          const lt = oe.get(ze);
          lt && (lt.selectable ?? Ae) && A.current.add(ze);
        }
    }
    if (!pa(Pe, D.current)) {
      const Ce = cn(te, D.current, !0);
      W(Ce);
    }
    if (!pa(Ze, A.current)) {
      const Ce = cn(oe, A.current);
      ee(Ce);
    }
    m.setState({
      userSelectionRect: Le,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function F() {
    if (!r || !V.current)
      return;
    const [O, Y] = is(k.current, V.current, S);
    v({ x: O, y: Y }).then((se) => {
      if (!L.current || !se) {
        w.current = requestAnimationFrame(F);
        return;
      }
      const { x: ae, y: te } = k.current;
      B(ae, te), w.current = requestAnimationFrame(F);
    });
  }
  const X = () => {
    cancelAnimationFrame(w.current), w.current = 0, M.current = !1;
  };
  Q(() => () => X(), []);
  const J = (O) => {
    const { userSelectionRect: Y, transform: se, resetSelectedElements: ae } = m.getState();
    if (!V.current || !Y)
      return;
    const { x: te, y: oe } = tt(O.nativeEvent, V.current);
    k.current = { x: te, y: oe };
    const fe = mn({ x: Y.startX, y: Y.startY }, se);
    if (!L.current) {
      const W = t ? 0 : s;
      if (Math.hypot(te - fe.x, oe - fe.y) <= W)
        return;
      ae(), c?.(O);
    }
    L.current = !0, M.current || (F(), M.current = !0), B(te, oe);
  }, U = (O) => {
    O.button === 0 && (O.target?.releasePointerCapture?.(O.pointerId), !E && O.target === $.current && m.getState().userSelectionRect && I?.(O), m.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), L.current && (u?.(O), m.setState({
      nodesSelectionActive: D.current.size > 0
    })), X());
  }, ce = (O) => {
    O.target?.releasePointerCapture?.(O.pointerId), X();
  }, G = o === !0 || Array.isArray(o) && o.includes(0);
  return i.jsxs("div", { className: Ee(["react-flow__pane", { draggable: G, dragging: b, selection: e }]), onClick: _ ? void 0 : mi(I, $), onContextMenu: mi(T, $), onWheel: mi(z, $), onPointerEnter: _ ? void 0 : d, onPointerMove: _ ? J : h, onPointerUp: _ ? U : void 0, onPointerCancel: _ ? ce : void 0, onPointerDownCapture: _ ? K : void 0, onClickCapture: _ ? P : void 0, onPointerLeave: g, ref: $, style: Rr, children: [N, i.jsx(Um, {})] });
}
function zi({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: s, multiSelectionActive: a, nodeLookup: c, onError: u } = t.getState(), l = c.get(e);
  if (!l) {
    u?.("012", Ye.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && a) && (s({ nodes: [l], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function du({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: s, nodeClickDistance: a }) {
  const c = je(), [u, l] = q(!1), f = ie();
  return Q(() => {
    f.current = Ty({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (p) => {
        zi({
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
        handleSelector: o,
        domNode: e.current,
        isSelectable: s,
        nodeId: r,
        nodeClickDistance: a
      }), () => {
        f.current?.destroy();
      };
  }, [n, o, t, s, e, r, a]), u;
}
const Zm = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function fu() {
  const e = je();
  return le((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: s, nodesDraggable: a, onError: c, updateNodePositions: u, nodeLookup: l, nodeOrigin: f } = e.getState(), p = /* @__PURE__ */ new Map(), d = Zm(a), h = r ? s[0] : 5, g = r ? s[1] : 5, N = n.direction.x * h * n.factor, w = n.direction.y * g * n.factor;
    for (const [, m] of l) {
      if (!d(m))
        continue;
      let E = {
        x: m.internals.positionAbsolute.x + N,
        y: m.internals.positionAbsolute.y + w
      };
      r && (E = xo(E, s));
      const { position: y, positionAbsolute: b } = Il({
        nodeId: m.id,
        nextPosition: E,
        nodeLookup: l,
        nodeExtent: o,
        nodeOrigin: f,
        onError: c
      });
      m.position = y, m.internals.positionAbsolute = b, p.set(m.id, m);
    }
    u(p);
  }, []);
}
const hs = Yi(null), Gm = hs.Provider;
hs.Consumer;
const pu = () => po(hs), Jm = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), Qm = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: s, connection: a } = o, { fromHandle: c, toHandle: u, isValid: l } = a, f = u?.nodeId === e && u?.id === t && u?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: f,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: s === hn.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!r,
    valid: f && l
  };
};
function ex({ type: e = "source", position: t = ne.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: s = !0, id: a, onConnect: c, children: u, className: l, onMouseDown: f, onTouchStart: p, ...d }, h) {
  const g = a || null, N = e === "target", w = je(), m = pu(), { connectOnClick: E, noPanClassName: y, rfId: b } = he(Jm, Ne), { connectingFrom: j, connectingTo: v, clickConnecting: S, isPossibleEndHandle: _, connectionInProcess: $, clickConnectionInProcess: V, valid: D } = he(Qm(m, g, e), Ne);
  m || w.getState().onError?.("010", Ye.error010());
  const A = (M) => {
    const { defaultEdgeOptions: I, onConnect: T, hasDefaultEdges: z } = w.getState(), P = {
      ...I,
      ...M
    };
    if (z) {
      const { edges: K, setEdges: B, onError: F } = w.getState();
      B(cu(P, K, { onError: F }));
    }
    T?.(P), c?.(P);
  }, L = (M) => {
    if (!m)
      return;
    const I = zl(M.nativeEvent);
    if (r && (I && M.button === 0 || !I)) {
      const T = w.getState();
      Pi.onPointerDown(M.nativeEvent, {
        handleDomNode: M.currentTarget,
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
        onConnectEnd: (...z) => w.getState().onConnectEnd?.(...z),
        updateConnection: T.updateConnection,
        onConnect: A,
        isValidConnection: n || ((...z) => w.getState().isValidConnection?.(...z) ?? !0),
        getTransform: () => w.getState().transform,
        getFromHandle: () => w.getState().connection.fromHandle,
        autoPanSpeed: T.autoPanSpeed,
        dragThreshold: T.connectionDragThreshold
      });
    }
    I ? f?.(M) : p?.(M);
  }, k = (M) => {
    const { onClickConnectStart: I, onClickConnectEnd: T, connectionClickStartHandle: z, connectionMode: P, isValidConnection: K, lib: B, rfId: F, nodeLookup: X, connection: J } = w.getState();
    if (!m || !z && !r)
      return;
    if (!z) {
      I?.(M.nativeEvent, { nodeId: m, handleId: g, handleType: e }), w.setState({ connectionClickStartHandle: { nodeId: m, type: e, id: g } });
      return;
    }
    const U = $l(M.target), ce = n || K, { connection: G, isValid: O } = Pi.isValid(M.nativeEvent, {
      handle: {
        nodeId: m,
        id: g,
        type: e
      },
      connectionMode: P,
      fromNodeId: z.nodeId,
      fromHandleId: z.id || null,
      fromType: z.type,
      isValidConnection: ce,
      flowId: F,
      doc: U,
      lib: B,
      nodeLookup: X
    });
    O && G && A(G);
    const Y = structuredClone(J);
    delete Y.inProgress, Y.toPosition = Y.toHandle ? Y.toHandle.position : null, T?.(M, Y), w.setState({ connectionClickStartHandle: null });
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
      connectable: o,
      connectablestart: r,
      connectableend: s,
      clickconnecting: S,
      connectingfrom: j,
      connectingto: v,
      valid: D,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!$ || _) && ($ || V ? s : r)
    }
  ]), onMouseDown: L, onTouchStart: L, onClick: E ? k : void 0, ref: h, ...d, children: u });
}
const wn = Se(lu(ex));
function tx({ data: e, isConnectable: t, sourcePosition: n = ne.Bottom }) {
  return i.jsxs(i.Fragment, { children: [e?.label, i.jsx(wn, { type: "source", position: n, isConnectable: t })] });
}
function nx({ data: e, isConnectable: t, targetPosition: n = ne.Top, sourcePosition: o = ne.Bottom }) {
  return i.jsxs(i.Fragment, { children: [i.jsx(wn, { type: "target", position: n, isConnectable: t }), e?.label, i.jsx(wn, { type: "source", position: o, isConnectable: t })] });
}
function ox() {
  return null;
}
function rx({ data: e, isConnectable: t, targetPosition: n = ne.Top }) {
  return i.jsxs(i.Fragment, { children: [i.jsx(wn, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const gr = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, Ka = {
  input: tx,
  default: nx,
  output: rx,
  group: ox
};
function ix(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const sx = (e) => {
  const { width: t, height: n, x: o, y: r } = mo(e.nodeLookup, {
    filter: (s) => !!s.selected
  });
  return {
    width: et(t) ? t : null,
    height: et(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function ax({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = je(), { width: r, height: s, transformString: a, userSelectionActive: c } = he(sx, Ne), u = fu(), l = ie(null);
  Q(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const f = !c && r !== null && s !== null;
  if (du({
    nodeRef: l,
    disabled: !f
  }), !f)
    return null;
  const p = e ? (h) => {
    const g = o.getState().nodes.filter((N) => N.selected);
    e(h, g);
  } : void 0, d = (h) => {
    Object.prototype.hasOwnProperty.call(gr, h.key) && (h.preventDefault(), u({
      direction: gr[h.key],
      factor: h.shiftKey ? 4 : 1
    }));
  };
  return i.jsx("div", { className: Ee(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: a
  }, children: i.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: p, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : d, style: {
    width: r,
    height: s
  } }) });
}
const qa = typeof window < "u" ? window : void 0, cx = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function hu({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: s, onPaneScroll: a, paneClickDistance: c, deleteKeyCode: u, selectionKeyCode: l, selectionOnDrag: f, selectionMode: p, onSelectionStart: d, onSelectionEnd: h, multiSelectionKeyCode: g, panActivationKeyCode: N, zoomActivationKeyCode: w, elementsSelectable: m, zoomOnScroll: E, zoomOnPinch: y, panOnScroll: b, panOnScrollSpeed: j, panOnScrollMode: v, zoomOnDoubleClick: S, panOnDrag: _, autoPanOnSelection: $, defaultViewport: V, translateExtent: D, minZoom: A, maxZoom: L, preventScrolling: k, onSelectionContextMenu: M, noWheelClassName: I, noPanClassName: T, disableKeyboardA11y: z, onViewportChange: P, isControlledViewport: K }) {
  const { nodesSelectionActive: B, userSelectionActive: F } = he(cx, Ne), X = lo(l, { target: qa }), J = lo(N, { target: qa }), U = J || _, ce = J || b, G = f && U !== !0, O = X || F || G;
  return Wm({ deleteKeyCode: u, multiSelectionKeyCode: g }), i.jsx(Km, { onPaneContextMenu: s, elementsSelectable: m, zoomOnScroll: E, zoomOnPinch: y, panOnScroll: ce, panOnScrollSpeed: j, panOnScrollMode: v, zoomOnDoubleClick: S, panOnDrag: !X && U, defaultViewport: V, translateExtent: D, minZoom: A, maxZoom: L, zoomActivationKeyCode: w, preventScrolling: k, noWheelClassName: I, noPanClassName: T, onViewportChange: P, isControlledViewport: K, paneClickDistance: c, selectionOnDrag: G, children: i.jsxs(Ym, { onSelectionStart: d, onSelectionEnd: h, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: s, onPaneScroll: a, panOnDrag: U, autoPanOnSelection: $, isSelecting: !!O, selectionMode: p, selectionKeyPressed: X, paneClickDistance: c, selectionOnDrag: G, children: [e, B && i.jsx(ax, { onSelectionContextMenu: M, noPanClassName: T, disableKeyboardA11y: z })] }) });
}
hu.displayName = "FlowRenderer";
const lx = Se(hu), ux = (e) => (t) => e ? rs(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function dx(e) {
  return he(le(ux(e), [e]), Ne);
}
const fx = (e) => e.updateNodeInternals;
function px() {
  const e = he(fx), [t] = q(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
    const o = /* @__PURE__ */ new Map();
    n.forEach((r) => {
      const s = r.target.getAttribute("data-id");
      o.set(s, {
        id: s,
        nodeElement: r.target,
        force: !0
      });
    }), e(o);
  }));
  return Q(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function hx({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = je(), s = ie(null), a = ie(null), c = ie(e.sourcePosition), u = ie(e.targetPosition), l = ie(t), f = n && !!e.internals.handleBounds;
  return Q(() => {
    s.current && !e.hidden && (!f || a.current !== s.current) && (a.current && o?.unobserve(a.current), o?.observe(s.current), a.current = s.current);
  }, [f, e.hidden]), Q(() => () => {
    a.current && (o?.unobserve(a.current), a.current = null);
  }, []), Q(() => {
    if (s.current) {
      const p = l.current !== t, d = c.current !== e.sourcePosition, h = u.current !== e.targetPosition;
      (p || d || h) && (l.current = t, c.current = e.sourcePosition, u.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: s.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), s;
}
function gx({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: s, onDoubleClick: a, nodesDraggable: c, elementsSelectable: u, nodesConnectable: l, nodesFocusable: f, resizeObserver: p, noDragClassName: d, noPanClassName: h, disableKeyboardA11y: g, rfId: N, nodeTypes: w, nodeClickDistance: m, onError: E }) {
  const { node: y, internals: b, isParent: j } = he((O) => {
    const Y = O.nodeLookup.get(e), se = O.parentLookup.has(e);
    return {
      node: Y,
      internals: Y.internals,
      isParent: se
    };
  }, Ne);
  let v = y.type || "default", S = w?.[v] || Ka[v];
  S === void 0 && (E?.("003", Ye.error003(v)), v = "default", S = w?.default || Ka.default);
  const _ = !!(y.draggable || c && typeof y.draggable > "u"), $ = !!(y.selectable || u && typeof y.selectable > "u"), V = !!(y.connectable || l && typeof y.connectable > "u"), D = !!(y.focusable || f && typeof y.focusable > "u"), A = je(), L = Ml(y), k = hx({ node: y, nodeType: v, hasDimensions: L, resizeObserver: p }), M = du({
    nodeRef: k,
    disabled: y.hidden || !_,
    noDragClassName: d,
    handleSelector: y.dragHandle,
    nodeId: e,
    isSelectable: $,
    nodeClickDistance: m
  }), I = fu();
  if (y.hidden)
    return null;
  const T = yt(y), z = ix(y), P = $ || _ || t || n || o || r, K = n ? (O) => n(O, { ...b.userNode }) : void 0, B = o ? (O) => o(O, { ...b.userNode }) : void 0, F = r ? (O) => r(O, { ...b.userNode }) : void 0, X = s ? (O) => s(O, { ...b.userNode }) : void 0, J = a ? (O) => a(O, { ...b.userNode }) : void 0, U = (O) => {
    const { selectNodesOnDrag: Y, nodeDragThreshold: se } = A.getState();
    $ && (!Y || !_ || se > 0) && zi({
      id: e,
      store: A,
      nodeRef: k
    }), t && t(O, { ...b.userNode });
  }, ce = (O) => {
    if (!(Pl(O.nativeEvent) || g)) {
      if (jl.includes(O.key) && $) {
        const Y = O.key === "Escape";
        zi({
          id: e,
          store: A,
          unselect: Y,
          nodeRef: k
        });
      } else if (_ && y.selected && Object.prototype.hasOwnProperty.call(gr, O.key)) {
        O.preventDefault();
        const { ariaLabelConfig: Y } = A.getState();
        A.setState({
          ariaLiveMessage: Y["node.a11yDescription.ariaLiveMessage"]({
            direction: O.key.replace("Arrow", "").toLowerCase(),
            x: ~~b.positionAbsolute.x,
            y: ~~b.positionAbsolute.y
          })
        }), I({
          direction: gr[O.key],
          factor: O.shiftKey ? 4 : 1
        });
      }
    }
  }, G = () => {
    if (g || !k.current?.matches(":focus-visible"))
      return;
    const { transform: O, width: Y, height: se, autoPanOnNodeFocus: ae, setCenter: te } = A.getState();
    if (!ae)
      return;
    rs(/* @__PURE__ */ new Map([[e, y]]), { x: 0, y: 0, width: Y, height: se }, O, !0).length > 0 || te(y.position.x + T.width / 2, y.position.y + T.height / 2, {
      zoom: O[2]
    });
  };
  return i.jsx("div", { className: Ee([
    "react-flow__node",
    `react-flow__node-${v}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [h]: _
    },
    y.className,
    {
      selected: y.selected,
      selectable: $,
      parent: j,
      draggable: _,
      dragging: M
    }
  ]), ref: k, style: {
    zIndex: b.z,
    transform: `translate(${b.positionAbsolute.x}px,${b.positionAbsolute.y}px)`,
    pointerEvents: P ? "all" : "none",
    visibility: L ? "visible" : "hidden",
    ...y.style,
    ...z
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: K, onMouseMove: B, onMouseLeave: F, onContextMenu: X, onClick: U, onDoubleClick: J, onKeyDown: D ? ce : void 0, tabIndex: D ? 0 : void 0, onFocus: D ? G : void 0, role: y.ariaRole ?? (D ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": g ? void 0 : `${tu}-${N}`, "aria-label": y.ariaLabel, ...y.domAttributes, children: i.jsx(Gm, { value: e, children: i.jsx(S, { id: e, data: y.data, type: v, positionAbsoluteX: b.positionAbsolute.x, positionAbsoluteY: b.positionAbsolute.y, selected: y.selected ?? !1, selectable: $, draggable: _, deletable: y.deletable ?? !0, isConnectable: V, sourcePosition: y.sourcePosition, targetPosition: y.targetPosition, dragging: M, dragHandle: y.dragHandle, zIndex: b.z, parentId: y.parentId, ...T }) }) });
}
var yx = Se(gx);
const mx = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function gu(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: s } = he(mx, Ne), a = dx(e.onlyRenderVisibleElements), c = px();
  return i.jsx("div", { className: "react-flow__nodes", style: Rr, children: a.map((u) => (
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
    i.jsx(yx, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: s }, u)
  )) });
}
gu.displayName = "NodeRenderer";
const xx = Se(gu);
function wx(e) {
  return he(le((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const s = n.nodeLookup.get(r.source), a = n.nodeLookup.get(r.target);
        s && a && hy({
          sourceNode: s,
          targetNode: a,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && o.push(r.id);
      }
    return o;
  }, [e]), Ne);
}
const vx = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return i.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, bx = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return i.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Ua = {
  [fr.Arrow]: vx,
  [fr.ArrowClosed]: bx
};
function Nx(e) {
  const t = je();
  return de(() => Object.prototype.hasOwnProperty.call(Ua, e) ? Ua[e] : (t.getState().onError?.("009", Ye.error009(e)), null), [e]);
}
const jx = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: s = "strokeWidth", strokeWidth: a, orient: c = "auto-start-reverse" }) => {
  const u = Nx(t);
  return u ? i.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: s, orient: c, refX: "0", refY: "0", children: i.jsx(u, { color: n, strokeWidth: a }) }) : null;
}, yu = ({ defaultColor: e, rfId: t }) => {
  const n = he((s) => s.edges), o = he((s) => s.defaultEdgeOptions), r = de(() => Ny(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? i.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: i.jsx("defs", { children: r.map((s) => i.jsx(jx, { id: s.id, type: s.type, color: s.color, width: s.width, height: s.height, markerUnits: s.markerUnits, strokeWidth: s.strokeWidth, orient: s.orient }, s.id)) }) }) : null;
};
yu.displayName = "MarkerDefinitions";
var Sx = Se(yu);
function mu({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: s, labelBgPadding: a = [2, 4], labelBgBorderRadius: c = 2, children: u, className: l, ...f }) {
  const [p, d] = q({ x: 1, y: 0, width: 0, height: 0 }), h = Ee(["react-flow__edge-textwrapper", l]), g = ie(null);
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
  }, [n]), n ? i.jsxs("g", { transform: `translate(${e - p.width / 2} ${t - p.height / 2})`, className: h, visibility: p.width ? "visible" : "hidden", ...f, children: [r && i.jsx("rect", { width: p.width + 2 * a[0], x: -a[0], y: -a[1], height: p.height + 2 * a[1], className: "react-flow__edge-textbg", style: s, rx: c, ry: c }), i.jsx("text", { className: "react-flow__edge-text", y: p.height / 2, dy: "0.3em", ref: g, style: o, children: n }), u] }) : null;
}
mu.displayName = "EdgeText";
const kx = Se(mu);
function wo({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u, interactionWidth: l = 20, ...f }) {
  return i.jsxs(i.Fragment, { children: [i.jsx("path", { ...f, d: e, fill: "none", className: Ee(["react-flow__edge-path", f.className]) }), l ? i.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, o && et(t) && et(n) ? i.jsx(kx, { x: t, y: n, label: o, labelStyle: r, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u }) : null] });
}
function Xa({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === ne.Left || e === ne.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function xu({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: r, targetPosition: s = ne.Top }) {
  const [a, c] = Xa({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [u, l] = Xa({
    pos: s,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [f, p, d, h] = Rl({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: r,
    sourceControlX: a,
    sourceControlY: c,
    targetControlX: u,
    targetControlY: l
  });
  return [
    `M${e},${t} C${a},${c} ${u},${l} ${o},${r}`,
    f,
    p,
    d,
    h
  ];
}
function wu(e) {
  return Se(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: s, sourcePosition: a, targetPosition: c, label: u, labelStyle: l, labelShowBg: f, labelBgStyle: p, labelBgPadding: d, labelBgBorderRadius: h, style: g, markerEnd: N, markerStart: w, interactionWidth: m }) => {
    const [E, y, b] = xu({
      sourceX: n,
      sourceY: o,
      sourcePosition: a,
      targetX: r,
      targetY: s,
      targetPosition: c
    }), j = e.isInternal ? void 0 : t;
    return i.jsx(wo, { id: j, path: E, labelX: y, labelY: b, label: u, labelStyle: l, labelShowBg: f, labelBgStyle: p, labelBgPadding: d, labelBgBorderRadius: h, style: g, markerEnd: N, markerStart: w, interactionWidth: m });
  });
}
const Ex = wu({ isInternal: !1 }), vu = wu({ isInternal: !0 });
Ex.displayName = "SimpleBezierEdge";
vu.displayName = "SimpleBezierEdgeInternal";
function bu(e) {
  return Se(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: p, style: d, sourcePosition: h = ne.Bottom, targetPosition: g = ne.Top, markerEnd: N, markerStart: w, pathOptions: m, interactionWidth: E }) => {
    const [y, b, j] = hr({
      sourceX: n,
      sourceY: o,
      sourcePosition: h,
      targetX: r,
      targetY: s,
      targetPosition: g,
      borderRadius: m?.borderRadius,
      offset: m?.offset,
      stepPosition: m?.stepPosition
    }), v = e.isInternal ? void 0 : t;
    return i.jsx(wo, { id: v, path: y, labelX: b, labelY: j, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: p, style: d, markerEnd: N, markerStart: w, interactionWidth: E });
  });
}
const Nu = bu({ isInternal: !1 }), ju = bu({ isInternal: !0 });
Nu.displayName = "SmoothStepEdge";
ju.displayName = "SmoothStepEdgeInternal";
function Su(e) {
  return Se(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return i.jsx(Nu, { ...n, id: o, pathOptions: de(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Cx = Su({ isInternal: !1 }), ku = Su({ isInternal: !0 });
Cx.displayName = "StepEdge";
ku.displayName = "StepEdgeInternal";
function Eu(e) {
  return Se(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: p, style: d, markerEnd: h, markerStart: g, interactionWidth: N }) => {
    const [w, m, E] = Hl({ sourceX: n, sourceY: o, targetX: r, targetY: s }), y = e.isInternal ? void 0 : t;
    return i.jsx(wo, { id: y, path: w, labelX: m, labelY: E, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: p, style: d, markerEnd: h, markerStart: g, interactionWidth: N });
  });
}
const Ix = Eu({ isInternal: !1 }), Cu = Eu({ isInternal: !0 });
Ix.displayName = "StraightEdge";
Cu.displayName = "StraightEdgeInternal";
function Iu(e) {
  return Se(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: s, sourcePosition: a = ne.Bottom, targetPosition: c = ne.Top, label: u, labelStyle: l, labelShowBg: f, labelBgStyle: p, labelBgPadding: d, labelBgBorderRadius: h, style: g, markerEnd: N, markerStart: w, pathOptions: m, interactionWidth: E }) => {
    const [y, b, j] = Ll({
      sourceX: n,
      sourceY: o,
      sourcePosition: a,
      targetX: r,
      targetY: s,
      targetPosition: c,
      curvature: m?.curvature
    }), v = e.isInternal ? void 0 : t;
    return i.jsx(wo, { id: v, path: y, labelX: b, labelY: j, label: u, labelStyle: l, labelShowBg: f, labelBgStyle: p, labelBgPadding: d, labelBgBorderRadius: h, style: g, markerEnd: N, markerStart: w, interactionWidth: E });
  });
}
const Ax = Iu({ isInternal: !1 }), Au = Iu({ isInternal: !0 });
Ax.displayName = "BezierEdge";
Au.displayName = "BezierEdgeInternal";
const Ya = {
  default: Au,
  straight: Cu,
  step: ku,
  smoothstep: ju,
  simplebezier: vu
}, Za = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, _x = (e, t, n) => n === ne.Left ? e - t : n === ne.Right ? e + t : e, Dx = (e, t, n) => n === ne.Top ? e - t : n === ne.Bottom ? e + t : e, Ga = "react-flow__edgeupdater";
function Ja({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: s, onMouseOut: a, type: c }) {
  return i.jsx("circle", { onMouseDown: r, onMouseEnter: s, onMouseOut: a, className: Ee([Ga, `${Ga}-${c}`]), cx: _x(t, o, e), cy: Dx(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function Mx({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: s, targetY: a, sourcePosition: c, targetPosition: u, onReconnect: l, onReconnectStart: f, onReconnectEnd: p, setReconnecting: d, setUpdateHover: h }) {
  const g = je(), N = (b, j) => {
    if (b.button !== 0)
      return;
    const { autoPanOnConnect: v, domNode: S, connectionMode: _, connectionRadius: $, lib: V, onConnectStart: D, cancelConnection: A, nodeLookup: L, rfId: k, panBy: M, updateConnection: I } = g.getState(), T = j.type === "target", z = (B, F) => {
      d(!1), p?.(B, n, j.type, F);
    }, P = (B) => l?.(n, B), K = (B, F) => {
      d(!0), f?.(b, n, j.type), D?.(B, F);
    };
    Pi.onPointerDown(b.nativeEvent, {
      autoPanOnConnect: v,
      connectionMode: _,
      connectionRadius: $,
      domNode: S,
      handleId: j.id,
      nodeId: j.nodeId,
      nodeLookup: L,
      isTarget: T,
      edgeUpdaterType: j.type,
      lib: V,
      flowId: k,
      cancelConnection: A,
      panBy: M,
      isValidConnection: (...B) => g.getState().isValidConnection?.(...B) ?? !0,
      onConnect: P,
      onConnectStart: K,
      onConnectEnd: (...B) => g.getState().onConnectEnd?.(...B),
      onReconnectEnd: z,
      updateConnection: I,
      getTransform: () => g.getState().transform,
      getFromHandle: () => g.getState().connection.fromHandle,
      dragThreshold: g.getState().connectionDragThreshold,
      handleDomNode: b.currentTarget
    });
  }, w = (b) => N(b, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), m = (b) => N(b, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), E = () => h(!0), y = () => h(!1);
  return i.jsxs(i.Fragment, { children: [(e === !0 || e === "source") && i.jsx(Ja, { position: c, centerX: o, centerY: r, radius: t, onMouseDown: w, onMouseEnter: E, onMouseOut: y, type: "source" }), (e === !0 || e === "target") && i.jsx(Ja, { position: u, centerX: s, centerY: a, radius: t, onMouseDown: m, onMouseEnter: E, onMouseOut: y, type: "target" })] });
}
function Tx({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, reconnectRadius: f, onReconnect: p, onReconnectStart: d, onReconnectEnd: h, rfId: g, edgeTypes: N, noPanClassName: w, onError: m, disableKeyboardA11y: E }) {
  let y = he((te) => te.edgeLookup.get(e));
  const b = he((te) => te.defaultEdgeOptions);
  y = b ? { ...b, ...y } : y;
  let j = y.type || "default", v = N?.[j] || Ya[j];
  v === void 0 && (m?.("011", Ye.error011(j)), j = "default", v = N?.default || Ya.default);
  const S = !!(y.focusable || t && typeof y.focusable > "u"), _ = typeof p < "u" && (y.reconnectable || n && typeof y.reconnectable > "u"), $ = !!(y.selectable || o && typeof y.selectable > "u"), V = ie(null), [D, A] = q(!1), [L, k] = q(!1), M = je(), { zIndex: I, sourceX: T, sourceY: z, targetX: P, targetY: K, sourcePosition: B, targetPosition: F } = he(le((te) => {
    const oe = te.nodeLookup.get(y.source), fe = te.nodeLookup.get(y.target);
    if (!oe || !fe)
      return {
        zIndex: y.zIndex,
        ...Za
      };
    const W = by({
      id: e,
      sourceNode: oe,
      targetNode: fe,
      sourceHandle: y.sourceHandle || null,
      targetHandle: y.targetHandle || null,
      connectionMode: te.connectionMode,
      onError: m
    });
    return {
      zIndex: py({
        selected: y.selected,
        zIndex: y.zIndex,
        sourceNode: oe,
        targetNode: fe,
        elevateOnSelect: te.elevateEdgesOnSelect,
        zIndexMode: te.zIndexMode
      }),
      ...W || Za
    };
  }, [y.source, y.target, y.sourceHandle, y.targetHandle, y.selected, y.zIndex]), Ne), X = de(() => y.markerStart ? `url('#${Ti(y.markerStart, g)}')` : void 0, [y.markerStart, g]), J = de(() => y.markerEnd ? `url('#${Ti(y.markerEnd, g)}')` : void 0, [y.markerEnd, g]);
  if (y.hidden || T === null || z === null || P === null || K === null)
    return null;
  const U = (te) => {
    const { addSelectedEdges: oe, unselectNodesAndEdges: fe, multiSelectionActive: W } = M.getState();
    $ && (M.setState({ nodesSelectionActive: !1 }), y.selected && W ? (fe({ nodes: [], edges: [y] }), V.current?.blur()) : oe([e])), r && r(te, y);
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
    if (!E && jl.includes(te.key) && $) {
      const { unselectNodesAndEdges: oe, addSelectedEdges: fe } = M.getState();
      te.key === "Escape" ? (V.current?.blur(), oe({ edges: [y] })) : fe([e]);
    }
  };
  return i.jsx("svg", { style: { zIndex: I }, children: i.jsxs("g", { className: Ee([
    "react-flow__edge",
    `react-flow__edge-${j}`,
    y.className,
    w,
    {
      selected: y.selected,
      animated: y.animated,
      inactive: !$ && !r,
      updating: D,
      selectable: $
    }
  ]), onClick: U, onDoubleClick: ce, onContextMenu: G, onMouseEnter: O, onMouseMove: Y, onMouseLeave: se, onKeyDown: S ? ae : void 0, tabIndex: S ? 0 : void 0, role: y.ariaRole ?? (S ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": y.ariaLabel === null ? void 0 : y.ariaLabel || `Edge from ${y.source} to ${y.target}`, "aria-describedby": S ? `${nu}-${g}` : void 0, ref: V, ...y.domAttributes, children: [!L && i.jsx(v, { id: e, source: y.source, target: y.target, type: y.type, selected: y.selected, animated: y.animated, selectable: $, deletable: y.deletable ?? !0, label: y.label, labelStyle: y.labelStyle, labelShowBg: y.labelShowBg, labelBgStyle: y.labelBgStyle, labelBgPadding: y.labelBgPadding, labelBgBorderRadius: y.labelBgBorderRadius, sourceX: T, sourceY: z, targetX: P, targetY: K, sourcePosition: B, targetPosition: F, data: y.data, style: y.style, sourceHandleId: y.sourceHandle, targetHandleId: y.targetHandle, markerStart: X, markerEnd: J, pathOptions: "pathOptions" in y ? y.pathOptions : void 0, interactionWidth: y.interactionWidth }), _ && i.jsx(Mx, { edge: y, isReconnectable: _, reconnectRadius: f, onReconnect: p, onReconnectStart: d, onReconnectEnd: h, sourceX: T, sourceY: z, targetX: P, targetY: K, sourcePosition: B, targetPosition: F, setUpdateHover: A, setReconnecting: k })] }) });
}
var $x = Se(Tx);
const Px = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function _u({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: s, onEdgeContextMenu: a, onEdgeMouseEnter: c, onEdgeMouseMove: u, onEdgeMouseLeave: l, onEdgeClick: f, reconnectRadius: p, onEdgeDoubleClick: d, onReconnectStart: h, onReconnectEnd: g, disableKeyboardA11y: N }) {
  const { edgesFocusable: w, edgesReconnectable: m, elementsSelectable: E, onError: y } = he(Px, Ne), b = wx(t);
  return i.jsxs("div", { className: "react-flow__edges", children: [i.jsx(Sx, { defaultColor: e, rfId: n }), b.map((j) => i.jsx($x, { id: j, edgesFocusable: w, edgesReconnectable: m, elementsSelectable: E, noPanClassName: r, onReconnect: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, onClick: f, reconnectRadius: p, onDoubleClick: d, onReconnectStart: h, onReconnectEnd: g, rfId: n, onError: y, edgeTypes: o, disableKeyboardA11y: N }, j))] });
}
_u.displayName = "EdgeRenderer";
const zx = Se(_u), Rx = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function Lx({ children: e }) {
  const t = he(Rx);
  return i.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function Vx(e) {
  const t = ps(), n = ie(!1);
  Q(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Ox = (e) => e.panZoom?.syncViewport;
function Hx(e) {
  const t = he(Ox), n = je();
  return Q(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function Wx(e) {
  return e.connection.inProgress ? { ...e.connection, to: Nn(e.connection.to, e.transform) } : { ...e.connection };
}
function Bx(e) {
  return Wx;
}
function Fx(e) {
  const t = Bx();
  return he(t, Ne);
}
const Kx = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function qx({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: s, height: a, isValid: c, inProgress: u } = he(Kx, Ne);
  return !(s && r && u) ? null : i.jsx("svg", { style: e, width: s, height: a, className: "react-flow__connectionline react-flow__container", children: i.jsx("g", { className: Ee(["react-flow__connection", El(c)]), children: i.jsx(Du, { style: t, type: n, CustomComponent: o, isValid: c }) }) });
}
const Du = ({ style: e, type: t = Nt.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: s, fromNode: a, fromHandle: c, fromPosition: u, to: l, toNode: f, toHandle: p, toPosition: d, pointer: h } = Fx();
  if (!r)
    return;
  if (n)
    return i.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: a, fromHandle: c, fromX: s.x, fromY: s.y, toX: l.x, toY: l.y, fromPosition: u, toPosition: d, connectionStatus: El(o), toNode: f, toHandle: p, pointer: h });
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
    case Nt.Bezier:
      [g] = Ll(N);
      break;
    case Nt.SimpleBezier:
      [g] = xu(N);
      break;
    case Nt.Step:
      [g] = hr({
        ...N,
        borderRadius: 0
      });
      break;
    case Nt.SmoothStep:
      [g] = hr(N);
      break;
    default:
      [g] = Hl(N);
  }
  return i.jsx("path", { d: g, fill: "none", className: "react-flow__connection-path", style: e });
};
Du.displayName = "ConnectionLine";
const Ux = {};
function Qa(e = Ux) {
  ie(e), je(), Q(() => {
  }, [e]);
}
function Xx() {
  je(), ie(!1), Q(() => {
  }, []);
}
function Mu({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: s, onEdgeDoubleClick: a, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: f, onSelectionContextMenu: p, onSelectionStart: d, onSelectionEnd: h, connectionLineType: g, connectionLineStyle: N, connectionLineComponent: w, connectionLineContainerStyle: m, selectionKeyCode: E, selectionOnDrag: y, selectionMode: b, multiSelectionKeyCode: j, panActivationKeyCode: v, zoomActivationKeyCode: S, deleteKeyCode: _, onlyRenderVisibleElements: $, elementsSelectable: V, defaultViewport: D, translateExtent: A, minZoom: L, maxZoom: k, preventScrolling: M, defaultMarkerColor: I, zoomOnScroll: T, zoomOnPinch: z, panOnScroll: P, panOnScrollSpeed: K, panOnScrollMode: B, zoomOnDoubleClick: F, panOnDrag: X, autoPanOnSelection: J, onPaneClick: U, onPaneMouseEnter: ce, onPaneMouseMove: G, onPaneMouseLeave: O, onPaneScroll: Y, onPaneContextMenu: se, paneClickDistance: ae, nodeClickDistance: te, onEdgeContextMenu: oe, onEdgeMouseEnter: fe, onEdgeMouseMove: W, onEdgeMouseLeave: ee, reconnectRadius: me, onReconnect: ve, onReconnectStart: $e, onReconnectEnd: De, noDragClassName: Le, noWheelClassName: Pe, noPanClassName: Ze, disableKeyboardA11y: Ae, nodeExtent: Ce, rfId: xe, viewport: ze, onViewportChange: lt }) {
  return Qa(e), Qa(t), Xx(), Vx(n), Hx(ze), i.jsx(lx, { onPaneClick: U, onPaneMouseEnter: ce, onPaneMouseMove: G, onPaneMouseLeave: O, onPaneContextMenu: se, onPaneScroll: Y, paneClickDistance: ae, deleteKeyCode: _, selectionKeyCode: E, selectionOnDrag: y, selectionMode: b, onSelectionStart: d, onSelectionEnd: h, multiSelectionKeyCode: j, panActivationKeyCode: v, zoomActivationKeyCode: S, elementsSelectable: V, zoomOnScroll: T, zoomOnPinch: z, zoomOnDoubleClick: F, panOnScroll: P, panOnScrollSpeed: K, panOnScrollMode: B, panOnDrag: X, autoPanOnSelection: J, defaultViewport: D, translateExtent: A, minZoom: L, maxZoom: k, onSelectionContextMenu: p, preventScrolling: M, noDragClassName: Le, noWheelClassName: Pe, noPanClassName: Ze, disableKeyboardA11y: Ae, onViewportChange: lt, isControlledViewport: !!ze, children: i.jsxs(Lx, { children: [i.jsx(zx, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: a, onReconnect: ve, onReconnectStart: $e, onReconnectEnd: De, onlyRenderVisibleElements: $, onEdgeContextMenu: oe, onEdgeMouseEnter: fe, onEdgeMouseMove: W, onEdgeMouseLeave: ee, reconnectRadius: me, defaultMarkerColor: I, noPanClassName: Ze, disableKeyboardA11y: Ae, rfId: xe }), i.jsx(qx, { style: N, type: g, component: w, containerStyle: m }), i.jsx("div", { className: "react-flow__edgelabel-renderer" }), i.jsx(xx, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: f, nodeClickDistance: te, onlyRenderVisibleElements: $, noPanClassName: Ze, noDragClassName: Le, disableKeyboardA11y: Ae, nodeExtent: Ce, rfId: xe }), i.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Mu.displayName = "GraphView";
const Yx = Se(Mu), Zx = Dl(), ec = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: s, fitView: a, fitViewOptions: c, minZoom: u = 0.5, maxZoom: l = 2, nodeOrigin: f, nodeExtent: p, zIndexMode: d = "basic" } = {}) => {
  const h = /* @__PURE__ */ new Map(), g = /* @__PURE__ */ new Map(), N = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), m = o ?? t ?? [], E = n ?? e ?? [], y = f ?? [0, 0], b = p ?? io;
  Fl(N, w, m);
  const { nodesInitialized: j } = $i(E, h, g, {
    nodeOrigin: y,
    nodeExtent: b,
    zIndexMode: d
  });
  let v = [0, 0, 1];
  if (a && r && s) {
    const S = mo(h, {
      filter: (D) => !!((D.width || D.initialWidth) && (D.height || D.initialHeight))
    }), { x: _, y: $, zoom: V } = ss(S, r, s, u, l, c?.padding ?? 0.1);
    v = [_, $, V];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: s ?? 0,
    transform: v,
    nodes: E,
    nodesInitialized: j,
    nodeLookup: h,
    parentLookup: g,
    edges: m,
    edgeLookup: w,
    connectionLookup: N,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: o !== void 0,
    panZoom: null,
    minZoom: u,
    maxZoom: l,
    translateExtent: io,
    nodeExtent: b,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: hn.Strict,
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
    connection: { ...kl },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: Zx,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Sl,
    zIndexMode: d,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, Gx = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: s, fitView: a, fitViewOptions: c, minZoom: u, maxZoom: l, nodeOrigin: f, nodeExtent: p, zIndexMode: d }) => um((h, g) => {
  async function N() {
    const { nodeLookup: w, panZoom: m, fitViewOptions: E, fitViewResolver: y, width: b, height: j, minZoom: v, maxZoom: S } = g();
    m && (await sy({
      nodes: w,
      width: b,
      height: j,
      panZoom: m,
      minZoom: v,
      maxZoom: S
    }, E), y?.resolve(!0), h({ fitViewResolver: null }));
  }
  return {
    ...ec({
      nodes: e,
      edges: t,
      width: r,
      height: s,
      fitView: a,
      fitViewOptions: c,
      minZoom: u,
      maxZoom: l,
      nodeOrigin: f,
      nodeExtent: p,
      defaultNodes: n,
      defaultEdges: o,
      zIndexMode: d
    }),
    setNodes: (w) => {
      const { nodeLookup: m, parentLookup: E, nodeOrigin: y, elevateNodesOnSelect: b, fitViewQueued: j, zIndexMode: v, nodesSelectionActive: S } = g(), { nodesInitialized: _, hasSelectedNodes: $ } = $i(w, m, E, {
        nodeOrigin: y,
        nodeExtent: p,
        elevateNodesOnSelect: b,
        checkEquality: !0,
        zIndexMode: v
      }), V = S && $;
      j && _ ? (N(), h({
        nodes: w,
        nodesInitialized: _,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: V
      })) : h({ nodes: w, nodesInitialized: _, nodesSelectionActive: V });
    },
    setEdges: (w) => {
      const { connectionLookup: m, edgeLookup: E } = g();
      Fl(m, E, w), h({ edges: w });
    },
    setDefaultNodesAndEdges: (w, m) => {
      if (w) {
        const { setNodes: E } = g();
        E(w), h({ hasDefaultNodes: !0 });
      }
      if (m) {
        const { setEdges: E } = g();
        E(m), h({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (w) => {
      const { triggerNodeChanges: m, nodeLookup: E, parentLookup: y, domNode: b, nodeOrigin: j, nodeExtent: v, debug: S, fitViewQueued: _, zIndexMode: $ } = g(), { changes: V, updatedInternals: D } = Ay(w, E, y, b, j, v, $);
      D && (ky(E, y, { nodeOrigin: j, nodeExtent: v, zIndexMode: $ }), _ ? (N(), h({ fitViewQueued: !1, fitViewOptions: void 0 })) : h({}), V?.length > 0 && (S && console.log("React Flow: trigger node changes", V), m?.(V)));
    },
    updateNodePositions: (w, m = !1) => {
      const E = [];
      let y = [];
      const { nodeLookup: b, triggerNodeChanges: j, connection: v, updateConnection: S, onNodesChangeMiddlewareMap: _ } = g();
      for (const [$, V] of w) {
        const D = b.get($), A = !!(D?.expandParent && D?.parentId && V?.position), L = {
          id: $,
          type: "position",
          position: A ? {
            x: Math.max(0, V.position.x),
            y: Math.max(0, V.position.y)
          } : V.position,
          dragging: m
        };
        if (D && v.inProgress && v.fromNode.id === D.id) {
          const k = Wt(D, v.fromHandle, ne.Left, !0);
          S({ ...v, from: k });
        }
        A && D.parentId && E.push({
          id: $,
          parentId: D.parentId,
          rect: {
            ...V.internals.positionAbsolute,
            width: V.measured.width ?? 0,
            height: V.measured.height ?? 0
          }
        }), y.push(L);
      }
      if (E.length > 0) {
        const { parentLookup: $, nodeOrigin: V } = g(), D = fs(E, b, $, V);
        y.push(...D);
      }
      for (const $ of _.values())
        y = $(y);
      j(y);
    },
    triggerNodeChanges: (w) => {
      const { onNodesChange: m, setNodes: E, nodes: y, hasDefaultNodes: b, debug: j } = g();
      if (w?.length) {
        if (b) {
          const v = iu(w, y);
          E(v);
        }
        j && console.log("React Flow: trigger node changes", w), m?.(w);
      }
    },
    triggerEdgeChanges: (w) => {
      const { onEdgesChange: m, setEdges: E, edges: y, hasDefaultEdges: b, debug: j } = g();
      if (w?.length) {
        if (b) {
          const v = su(w, y);
          E(v);
        }
        j && console.log("React Flow: trigger edge changes", w), m?.(w);
      }
    },
    addSelectedNodes: (w) => {
      const { multiSelectionActive: m, edgeLookup: E, nodeLookup: y, triggerNodeChanges: b, triggerEdgeChanges: j } = g();
      if (m) {
        const v = w.map((S) => Tt(S, !0));
        b(v);
        return;
      }
      b(cn(y, /* @__PURE__ */ new Set([...w]), !0)), j(cn(E));
    },
    addSelectedEdges: (w) => {
      const { multiSelectionActive: m, edgeLookup: E, nodeLookup: y, triggerNodeChanges: b, triggerEdgeChanges: j } = g();
      if (m) {
        const v = w.map((S) => Tt(S, !0));
        j(v);
        return;
      }
      j(cn(E, /* @__PURE__ */ new Set([...w]))), b(cn(y, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: w, edges: m } = {}) => {
      const { edges: E, nodes: y, nodeLookup: b, triggerNodeChanges: j, triggerEdgeChanges: v } = g(), S = w || y, _ = m || E, $ = [];
      for (const D of S) {
        if (!D.selected)
          continue;
        const A = b.get(D.id);
        A && (A.selected = !1), $.push(Tt(D.id, !1));
      }
      const V = [];
      for (const D of _)
        D.selected && V.push(Tt(D.id, !1));
      j($), v(V);
    },
    setMinZoom: (w) => {
      const { panZoom: m, maxZoom: E } = g();
      m?.setScaleExtent([w, E]), h({ minZoom: w });
    },
    setMaxZoom: (w) => {
      const { panZoom: m, minZoom: E } = g();
      m?.setScaleExtent([E, w]), h({ maxZoom: w });
    },
    setTranslateExtent: (w) => {
      g().panZoom?.setTranslateExtent(w), h({ translateExtent: w });
    },
    resetSelectedElements: () => {
      const { edges: w, nodes: m, triggerNodeChanges: E, triggerEdgeChanges: y, elementsSelectable: b } = g();
      if (!b)
        return;
      const j = m.reduce((S, _) => _.selected ? [...S, Tt(_.id, !1)] : S, []), v = w.reduce((S, _) => _.selected ? [...S, Tt(_.id, !1)] : S, []);
      E(j), y(v);
    },
    setNodeExtent: (w) => {
      const { nodes: m, nodeLookup: E, parentLookup: y, nodeOrigin: b, elevateNodesOnSelect: j, nodeExtent: v, zIndexMode: S } = g();
      w[0][0] === v[0][0] && w[0][1] === v[0][1] && w[1][0] === v[1][0] && w[1][1] === v[1][1] || ($i(m, E, y, {
        nodeOrigin: b,
        nodeExtent: w,
        elevateNodesOnSelect: j,
        checkEquality: !1,
        zIndexMode: S
      }), h({ nodeExtent: w }));
    },
    panBy: (w) => {
      const { transform: m, width: E, height: y, panZoom: b, translateExtent: j } = g();
      return _y({ delta: w, panZoom: b, transform: m, translateExtent: j, width: E, height: y });
    },
    setCenter: async (w, m, E) => {
      const { width: y, height: b, maxZoom: j, panZoom: v } = g();
      if (!v)
        return !1;
      const S = typeof E?.zoom < "u" ? E.zoom : j;
      return await v.setViewport({
        x: y / 2 - w * S,
        y: b / 2 - m * S,
        zoom: S
      }, { duration: E?.duration, ease: E?.ease, interpolate: E?.interpolate }), !0;
    },
    cancelConnection: () => {
      h({
        connection: { ...kl }
      });
    },
    updateConnection: (w) => {
      h({ connection: w });
    },
    reset: () => h({ ...ec() })
  };
}, Object.is);
function Jx({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: s, initialMinZoom: a, initialMaxZoom: c, initialFitViewOptions: u, fitView: l, nodeOrigin: f, nodeExtent: p, zIndexMode: d, children: h }) {
  const [g] = q(() => Gx({
    nodes: e,
    edges: t,
    defaultNodes: n,
    defaultEdges: o,
    width: r,
    height: s,
    fitView: l,
    minZoom: a,
    maxZoom: c,
    fitViewOptions: u,
    nodeOrigin: f,
    nodeExtent: p,
    zIndexMode: d
  }));
  return i.jsx(hm, { value: g, children: i.jsx(Lm, { children: h }) });
}
function Qx({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: s, height: a, fitView: c, fitViewOptions: u, minZoom: l, maxZoom: f, nodeOrigin: p, nodeExtent: d, zIndexMode: h }) {
  return po(Pr) ? i.jsx(i.Fragment, { children: e }) : i.jsx(Jx, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: s, initialHeight: a, fitView: c, initialFitViewOptions: u, initialMinZoom: l, initialMaxZoom: f, nodeOrigin: p, nodeExtent: d, zIndexMode: h, children: e });
}
const ew = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function tw({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: s, edgeTypes: a, onNodeClick: c, onEdgeClick: u, onInit: l, onMove: f, onMoveStart: p, onMoveEnd: d, onConnect: h, onConnectStart: g, onConnectEnd: N, onClickConnectStart: w, onClickConnectEnd: m, onNodeMouseEnter: E, onNodeMouseMove: y, onNodeMouseLeave: b, onNodeContextMenu: j, onNodeDoubleClick: v, onNodeDragStart: S, onNodeDrag: _, onNodeDragStop: $, onNodesDelete: V, onEdgesDelete: D, onDelete: A, onSelectionChange: L, onSelectionDragStart: k, onSelectionDrag: M, onSelectionDragStop: I, onSelectionContextMenu: T, onSelectionStart: z, onSelectionEnd: P, onBeforeDelete: K, connectionMode: B, connectionLineType: F = Nt.Bezier, connectionLineStyle: X, connectionLineComponent: J, connectionLineContainerStyle: U, deleteKeyCode: ce = "Backspace", selectionKeyCode: G = "Shift", selectionOnDrag: O = !1, selectionMode: Y = so.Full, panActivationKeyCode: se = "Space", multiSelectionKeyCode: ae = co() ? "Meta" : "Control", zoomActivationKeyCode: te = co() ? "Meta" : "Control", snapToGrid: oe, snapGrid: fe, onlyRenderVisibleElements: W = !1, selectNodesOnDrag: ee, nodesDraggable: me, autoPanOnNodeFocus: ve, nodesConnectable: $e, nodesFocusable: De, nodeOrigin: Le = ou, edgesFocusable: Pe, edgesReconnectable: Ze, elementsSelectable: Ae = !0, defaultViewport: Ce = Cm, minZoom: xe = 0.5, maxZoom: ze = 2, translateExtent: lt = io, preventScrolling: Fr = !0, nodeExtent: kn, defaultMarkerColor: En = "#b1b1b7", zoomOnScroll: Et = !0, zoomOnPinch: So = !0, panOnScroll: Ge = !1, panOnScrollSpeed: qt = 0.5, panOnScrollMode: Cn = zt.Free, zoomOnDoubleClick: In = !0, panOnDrag: ko = !0, onPaneClick: An, onPaneMouseEnter: Be, onPaneMouseMove: ut, onPaneMouseLeave: dt, onPaneScroll: Ut, onPaneContextMenu: Kr, paneClickDistance: _n = 1, nodeClickDistance: Xt = 0, children: mt, onReconnect: Ct, onReconnectStart: It, onReconnectEnd: Ve, onEdgeContextMenu: Dn, onEdgeDoubleClick: Eo, onEdgeMouseEnter: Fe, onEdgeMouseMove: Co, onEdgeMouseLeave: be, reconnectRadius: Ke = 10, onNodesChange: Yt, onEdgesChange: Io, noDragClassName: ge = "nodrag", noWheelClassName: At = "nowheel", noPanClassName: Mn = "nopan", fitView: Zt, fitViewOptions: Gt, connectOnClick: _e, attributionPosition: _t, proOptions: xt, defaultEdgeOptions: qr, elevateNodesOnSelect: Ao = !0, elevateEdgesOnSelect: _o = !1, disableKeyboardA11y: Jt = !1, autoPanOnConnect: Ur, autoPanOnNodeDrag: Tn, autoPanOnSelection: Qt = !0, autoPanSpeed: Re, connectionRadius: $n, isValidConnection: Pn, onError: zn, style: en, id: Do, nodeDragThreshold: Xr, connectionDragThreshold: Yr, viewport: Zr, onViewportChange: Mo, width: To, height: $o, colorMode: Gr = "light", debug: tn, onScroll: Po, ariaLabelConfig: Jr, zIndexMode: Dt = "basic", ...Qr }, ei) {
  const wt = Do || "1", nn = Dm(Gr), Rn = le((on) => {
    on.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), Po?.(on);
  }, [Po]);
  return i.jsx("div", { "data-testid": "rf__wrapper", ...Qr, onScroll: Rn, style: { ...en, ...ew }, ref: ei, className: Ee(["react-flow", r, nn]), id: Do, role: "application", children: i.jsxs(Qx, { nodes: e, edges: t, width: To, height: $o, fitView: Zt, fitViewOptions: Gt, minZoom: xe, maxZoom: ze, nodeOrigin: Le, nodeExtent: kn, zIndexMode: Dt, children: [i.jsx(_m, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: h, onConnectStart: g, onConnectEnd: N, onClickConnectStart: w, onClickConnectEnd: m, nodesDraggable: me, autoPanOnNodeFocus: ve, nodesConnectable: $e, nodesFocusable: De, edgesFocusable: Pe, edgesReconnectable: Ze, elementsSelectable: Ae, elevateNodesOnSelect: Ao, elevateEdgesOnSelect: _o, minZoom: xe, maxZoom: ze, nodeExtent: kn, onNodesChange: Yt, onEdgesChange: Io, snapToGrid: oe, snapGrid: fe, connectionMode: B, translateExtent: lt, connectOnClick: _e, defaultEdgeOptions: qr, fitView: Zt, fitViewOptions: Gt, onNodesDelete: V, onEdgesDelete: D, onDelete: A, onNodeDragStart: S, onNodeDrag: _, onNodeDragStop: $, onSelectionDrag: M, onSelectionDragStart: k, onSelectionDragStop: I, onMove: f, onMoveStart: p, onMoveEnd: d, noPanClassName: Mn, nodeOrigin: Le, rfId: wt, autoPanOnConnect: Ur, autoPanOnNodeDrag: Tn, autoPanSpeed: Re, onError: zn, connectionRadius: $n, isValidConnection: Pn, selectNodesOnDrag: ee, nodeDragThreshold: Xr, connectionDragThreshold: Yr, onBeforeDelete: K, debug: tn, ariaLabelConfig: Jr, zIndexMode: Dt }), i.jsx(Yx, { onInit: l, onNodeClick: c, onEdgeClick: u, onNodeMouseEnter: E, onNodeMouseMove: y, onNodeMouseLeave: b, onNodeContextMenu: j, onNodeDoubleClick: v, nodeTypes: s, edgeTypes: a, connectionLineType: F, connectionLineStyle: X, connectionLineComponent: J, connectionLineContainerStyle: U, selectionKeyCode: G, selectionOnDrag: O, selectionMode: Y, deleteKeyCode: ce, multiSelectionKeyCode: ae, panActivationKeyCode: se, zoomActivationKeyCode: te, onlyRenderVisibleElements: W, defaultViewport: Ce, translateExtent: lt, minZoom: xe, maxZoom: ze, preventScrolling: Fr, zoomOnScroll: Et, zoomOnPinch: So, zoomOnDoubleClick: In, panOnScroll: Ge, panOnScrollSpeed: qt, panOnScrollMode: Cn, panOnDrag: ko, autoPanOnSelection: Qt, onPaneClick: An, onPaneMouseEnter: Be, onPaneMouseMove: ut, onPaneMouseLeave: dt, onPaneScroll: Ut, onPaneContextMenu: Kr, paneClickDistance: _n, nodeClickDistance: Xt, onSelectionContextMenu: T, onSelectionStart: z, onSelectionEnd: P, onReconnect: Ct, onReconnectStart: It, onReconnectEnd: Ve, onEdgeContextMenu: Dn, onEdgeDoubleClick: Eo, onEdgeMouseEnter: Fe, onEdgeMouseMove: Co, onEdgeMouseLeave: be, reconnectRadius: Ke, defaultMarkerColor: En, noDragClassName: ge, noWheelClassName: At, noPanClassName: Mn, rfId: wt, disableKeyboardA11y: Jt, nodeExtent: kn, viewport: Zr, onViewportChange: Mo }), i.jsx(Em, { onSelectionChange: L }), mt, i.jsx(bm, { proOptions: xt, position: _t }), i.jsx(vm, { rfId: wt, disableKeyboardA11y: Jt })] }) });
}
var Tu = lu(tw);
const nw = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function ow({ children: e }) {
  const t = he(nw);
  return t ? pm.createPortal(e, t) : null;
}
function rw({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return i.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ee(["react-flow__background-pattern", n, o]) });
}
function iw({ radius: e, className: t }) {
  return i.jsx("circle", { cx: e, cy: e, r: e, className: Ee(["react-flow__background-pattern", "dots", t]) });
}
var jt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(jt || (jt = {}));
const sw = {
  [jt.Dots]: 1,
  [jt.Lines]: 1,
  [jt.Cross]: 6
}, aw = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function $u({
  id: e,
  variant: t = jt.Dots,
  // only used for dots and cross
  gap: n = 20,
  // only used for lines and cross
  size: o,
  lineWidth: r = 1,
  offset: s = 0,
  color: a,
  bgColor: c,
  style: u,
  className: l,
  patternClassName: f
}) {
  const p = ie(null), { transform: d, patternId: h } = he(aw, Ne), g = o || sw[t], N = t === jt.Dots, w = t === jt.Cross, m = Array.isArray(n) ? n : [n, n], E = [m[0] * d[2] || 1, m[1] * d[2] || 1], y = g * d[2], b = Array.isArray(s) ? s : [s, s], j = w ? [y, y] : E, v = [
    b[0] * d[2] || 1 + j[0] / 2,
    b[1] * d[2] || 1 + j[1] / 2
  ], S = `${h}${e || ""}`;
  return i.jsxs("svg", { className: Ee(["react-flow__background", l]), style: {
    ...u,
    ...Rr,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": a
  }, ref: p, "data-testid": "rf__background", children: [i.jsx("pattern", { id: S, x: d[0] % E[0], y: d[1] % E[1], width: E[0], height: E[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${v[0]},-${v[1]})`, children: N ? i.jsx(iw, { radius: y / 2, className: f }) : i.jsx(rw, { dimensions: j, lineWidth: r, variant: t, className: f }) }), i.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${S})` })] });
}
$u.displayName = "Background";
const Pu = Se($u);
function cw() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: i.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function lw() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: i.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function uw() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: i.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function dw() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: i.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function fw() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: i.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function Ko({ children: e, className: t, ...n }) {
  return i.jsx("button", { type: "button", className: Ee(["react-flow__controls-button", t]), ...n, children: e });
}
const pw = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function zu({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: s, onZoomOut: a, onFitView: c, onInteractiveChange: u, className: l, children: f, position: p = "bottom-left", orientation: d = "vertical", "aria-label": h }) {
  const g = je(), { isInteractive: N, minZoomReached: w, maxZoomReached: m, ariaLabelConfig: E } = he(pw, Ne), { zoomIn: y, zoomOut: b, fitView: j } = ps(), v = () => {
    y(), s?.();
  }, S = () => {
    b(), a?.();
  }, _ = () => {
    j(r), c?.();
  }, $ = () => {
    g.setState({
      nodesDraggable: !N,
      nodesConnectable: !N,
      elementsSelectable: !N
    }), u?.(!N);
  }, V = d === "horizontal" ? "horizontal" : "vertical";
  return i.jsxs(zr, { className: Ee(["react-flow__controls", V, l]), position: p, style: e, "data-testid": "rf__controls", "aria-label": h ?? E["controls.ariaLabel"], children: [t && i.jsxs(i.Fragment, { children: [i.jsx(Ko, { onClick: v, className: "react-flow__controls-zoomin", title: E["controls.zoomIn.ariaLabel"], "aria-label": E["controls.zoomIn.ariaLabel"], disabled: m, children: i.jsx(cw, {}) }), i.jsx(Ko, { onClick: S, className: "react-flow__controls-zoomout", title: E["controls.zoomOut.ariaLabel"], "aria-label": E["controls.zoomOut.ariaLabel"], disabled: w, children: i.jsx(lw, {}) })] }), n && i.jsx(Ko, { className: "react-flow__controls-fitview", onClick: _, title: E["controls.fitView.ariaLabel"], "aria-label": E["controls.fitView.ariaLabel"], children: i.jsx(uw, {}) }), o && i.jsx(Ko, { className: "react-flow__controls-interactive", onClick: $, title: E["controls.interactive.ariaLabel"], "aria-label": E["controls.interactive.ariaLabel"], children: N ? i.jsx(fw, {}) : i.jsx(dw, {}) }), f] });
}
zu.displayName = "Controls";
const Ru = Se(zu);
function hw({ id: e, x: t, y: n, width: o, height: r, style: s, color: a, strokeColor: c, strokeWidth: u, className: l, borderRadius: f, shapeRendering: p, selected: d, onClick: h }) {
  const { background: g, backgroundColor: N } = s || {}, w = a || g || N;
  return i.jsx("rect", { className: Ee(["react-flow__minimap-node", { selected: d }, l]), x: t, y: n, rx: f, ry: f, width: o, height: r, style: {
    fill: w,
    stroke: c,
    strokeWidth: u
  }, shapeRendering: p, onClick: h ? (m) => h(m, e) : void 0 });
}
const gw = Se(hw), yw = (e) => e.nodes.map((t) => t.id), xi = (e) => e instanceof Function ? e : () => e;
function mw({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: s = gw,
  onClick: a
}) {
  const c = he(yw, Ne), u = xi(t), l = xi(e), f = xi(n), p = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return i.jsx(i.Fragment, { children: c.map((d) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    i.jsx(ww, { id: d, nodeColorFunc: u, nodeStrokeColorFunc: l, nodeClassNameFunc: f, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: s, onClick: a, shapeRendering: p }, d)
  )) });
}
function xw({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: s, shapeRendering: a, NodeComponent: c, onClick: u }) {
  const { node: l, x: f, y: p, width: d, height: h } = he((g) => {
    const N = g.nodeLookup.get(e);
    if (!N)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const w = N.internals.userNode, { x: m, y: E } = N.internals.positionAbsolute, { width: y, height: b } = yt(w);
    return {
      node: w,
      x: m,
      y: E,
      width: y,
      height: b
    };
  }, Ne);
  return !l || l.hidden || !Ml(l) ? null : i.jsx(c, { x: f, y: p, width: d, height: h, style: l.style, selected: !!l.selected, className: o(l), color: t(l), borderRadius: r, strokeColor: n(l), strokeWidth: s, shapeRendering: a, onClick: u, id: l.id });
}
const ww = Se(xw);
var vw = Se(mw);
const bw = 200, Nw = 150, jw = (e) => !e.hidden, Sw = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? _l(mo(e.nodeLookup, { filter: jw }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, kw = "react-flow__minimap-desc";
function Lu({
  style: e,
  className: t,
  nodeStrokeColor: n,
  nodeColor: o,
  nodeClassName: r = "",
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
  inversePan: E,
  zoomStep: y = 1,
  offsetScale: b = 5
}) {
  const j = je(), v = ie(null), { boundingRect: S, viewBB: _, rfId: $, panZoom: V, translateExtent: D, flowWidth: A, flowHeight: L, ariaLabelConfig: k } = he(Sw, Ne), M = e?.width ?? bw, I = e?.height ?? Nw, T = S.width / M, z = S.height / I, P = Math.max(T, z), K = P * M, B = P * I, F = b * P, X = S.x - (K - S.width) / 2 - F, J = S.y - (B - S.height) / 2 - F, U = K + F * 2, ce = B + F * 2, G = `${kw}-${$}`, O = ie(0), Y = ie();
  O.current = P, Q(() => {
    if (v.current && V)
      return Y.current = Vy({
        domNode: v.current,
        panZoom: V,
        getTransform: () => j.getState().transform,
        getViewScale: () => O.current
      }), () => {
        Y.current?.destroy();
      };
  }, [V]), Q(() => {
    Y.current?.update({
      translateExtent: D,
      width: A,
      height: L,
      inversePan: E,
      pannable: N,
      zoomStep: y,
      zoomable: w
    });
  }, [N, w, E, y, D, A, L]);
  const se = h ? (oe) => {
    const [fe, W] = Y.current?.pointer(oe) || [0, 0];
    h(oe, { x: fe, y: W });
  } : void 0, ae = g ? le((oe, fe) => {
    const W = j.getState().nodeLookup.get(fe).internals.userNode;
    g(oe, W);
  }, []) : void 0, te = m ?? k["minimap.ariaLabel"];
  return i.jsx(zr, { position: d, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof f == "string" ? f : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof p == "number" ? p * P : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof a == "number" ? a : void 0
  }, className: Ee(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: i.jsxs("svg", { width: M, height: I, viewBox: `${X} ${J} ${U} ${ce}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": G, ref: v, onClick: se, children: [te && i.jsx("title", { id: G, children: te }), i.jsx(vw, { onClick: ae, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: s, nodeClassName: r, nodeStrokeWidth: a, nodeComponent: c }), i.jsx("path", { className: "react-flow__minimap-mask", d: `M${X - F},${J - F}h${U + F * 2}v${ce + F * 2}h${-U - F * 2}z
        M${_.x},${_.y}h${_.width}v${_.height}h${-_.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
Lu.displayName = "MiniMap";
const Vu = Se(Lu), Ew = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, Cw = {
  [xn.Line]: "right",
  [xn.Handle]: "bottom-right"
};
function Iw({ nodeId: e, position: t, variant: n = xn.Handle, className: o, style: r = void 0, children: s, color: a, minWidth: c = 10, minHeight: u = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: f = Number.MAX_VALUE, keepAspectRatio: p = !1, resizeDirection: d, autoScale: h = !0, shouldResize: g, onResizeStart: N, onResize: w, onResizeEnd: m }) {
  const E = pu(), y = typeof e == "string" ? e : E, b = je(), j = ie(null), v = n === xn.Handle, S = he(le(Ew(v && h), [v, h]), Ne), _ = ie(null), $ = t ?? Cw[n];
  Q(() => {
    if (!(!j.current || !y))
      return _.current || (_.current = Jy({
        domNode: j.current,
        nodeId: y,
        getStoreItems: () => {
          const { nodeLookup: D, transform: A, snapGrid: L, snapToGrid: k, nodeOrigin: M, domNode: I } = b.getState();
          return {
            nodeLookup: D,
            transform: A,
            snapGrid: L,
            snapToGrid: k,
            nodeOrigin: M,
            paneDomNode: I
          };
        },
        onChange: (D, A) => {
          const { triggerNodeChanges: L, nodeLookup: k, parentLookup: M, nodeOrigin: I } = b.getState(), T = [], z = { x: D.x, y: D.y }, P = k.get(y);
          if (P && P.expandParent && P.parentId) {
            const K = P.origin ?? I, B = D.width ?? P.measured.width ?? 0, F = D.height ?? P.measured.height ?? 0, X = {
              id: P.id,
              parentId: P.parentId,
              rect: {
                width: B,
                height: F,
                ...Tl({
                  x: D.x ?? P.position.x,
                  y: D.y ?? P.position.y
                }, { width: B, height: F }, P.parentId, k, K)
              }
            }, J = fs([X], k, M, I);
            T.push(...J), z.x = D.x ? Math.max(K[0] * B, D.x) : void 0, z.y = D.y ? Math.max(K[1] * F, D.y) : void 0;
          }
          if (z.x !== void 0 && z.y !== void 0) {
            const K = {
              id: y,
              type: "position",
              position: { ...z }
            };
            T.push(K);
          }
          if (D.width !== void 0 && D.height !== void 0) {
            const B = {
              id: y,
              type: "dimensions",
              resizing: !0,
              setAttributes: d ? d === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: D.width,
                height: D.height
              }
            };
            T.push(B);
          }
          for (const K of A) {
            const B = {
              ...K,
              type: "position"
            };
            T.push(B);
          }
          L(T);
        },
        onEnd: ({ width: D, height: A }) => {
          const L = {
            id: y,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: D,
              height: A
            }
          };
          b.getState().triggerNodeChanges([L]);
        }
      })), _.current.update({
        controlPosition: $,
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
        _.current?.destroy();
      };
  }, [
    $,
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
  const V = $.split("-");
  return i.jsx("div", { className: Ee(["react-flow__resize-control", "nodrag", ...V, n, o]), ref: j, style: {
    ...r,
    scale: S,
    ...a && { [v ? "backgroundColor" : "borderColor"]: a }
  }, children: s });
}
Se(Iw);
const Aw = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), Ou = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var _w = {
  xmlns: "http://www.w3.org/2000/svg",
  width: 24,
  height: 24,
  viewBox: "0 0 24 24",
  fill: "none",
  stroke: "currentColor",
  strokeWidth: 2,
  strokeLinecap: "round",
  strokeLinejoin: "round"
};
const Dw = Er(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: o,
    className: r = "",
    children: s,
    iconNode: a,
    ...c
  }, u) => ji(
    "svg",
    {
      ref: u,
      ..._w,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: Ou("lucide", r),
      ...c
    },
    [
      ...a.map(([l, f]) => ji(l, f)),
      ...Array.isArray(s) ? s : [s]
    ]
  )
);
const pe = (e, t) => {
  const n = Er(
    ({ className: o, ...r }, s) => ji(Dw, {
      ref: s,
      iconNode: t,
      className: Ou(`lucide-${Aw(e)}`, o),
      ...r
    })
  );
  return n.displayName = `${e}`, n;
};
const Mw = pe("Activity", [
  [
    "path",
    {
      d: "M22 12h-2.48a2 2 0 0 0-1.93 1.46l-2.35 8.36a.25.25 0 0 1-.48 0L9.24 2.18a.25.25 0 0 0-.48 0l-2.35 8.36A2 2 0 0 1 4.49 12H2",
      key: "169zse"
    }
  ]
]);
const Lr = pe("Boxes", [
  [
    "path",
    {
      d: "M2.97 12.92A2 2 0 0 0 2 14.63v3.24a2 2 0 0 0 .97 1.71l3 1.8a2 2 0 0 0 2.06 0L12 19v-5.5l-5-3-4.03 2.42Z",
      key: "lc1i9w"
    }
  ],
  ["path", { d: "m7 16.5-4.74-2.85", key: "1o9zyk" }],
  ["path", { d: "m7 16.5 5-3", key: "va8pkn" }],
  ["path", { d: "M7 16.5v5.17", key: "jnp8gn" }],
  [
    "path",
    {
      d: "M12 13.5V19l3.97 2.38a2 2 0 0 0 2.06 0l3-1.8a2 2 0 0 0 .97-1.71v-3.24a2 2 0 0 0-.97-1.71L17 10.5l-5 3Z",
      key: "8zsnat"
    }
  ],
  ["path", { d: "m17 16.5-5-3", key: "8arw3v" }],
  ["path", { d: "m17 16.5 4.74-2.85", key: "8rfmw" }],
  ["path", { d: "M17 16.5v5.17", key: "k6z78m" }],
  [
    "path",
    {
      d: "M7.97 4.42A2 2 0 0 0 7 6.13v4.37l5 3 5-3V6.13a2 2 0 0 0-.97-1.71l-3-1.8a2 2 0 0 0-2.06 0l-3 1.8Z",
      key: "1xygjf"
    }
  ],
  ["path", { d: "M12 8 7.26 5.15", key: "1vbdud" }],
  ["path", { d: "m12 8 4.74-2.85", key: "3rx089" }],
  ["path", { d: "M12 13.5V8", key: "1io7kd" }]
]);
const jn = pe("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const Hu = pe("ChevronDown", [
  ["path", { d: "m6 9 6 6 6-6", key: "qrunsl" }]
]);
const yr = pe("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const sn = pe("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const Tw = pe("ChevronUp", [["path", { d: "m18 15-6-6-6 6", key: "153udz" }]]);
const kt = pe("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const $w = pe("CodeXml", [
  ["path", { d: "m18 16 4-4-4-4", key: "1inbqp" }],
  ["path", { d: "m6 8-4 4 4 4", key: "15zrgr" }],
  ["path", { d: "m14.5 4-5 16", key: "e7oirm" }]
]);
const Pw = pe("Copy", [
  ["rect", { width: "14", height: "14", x: "8", y: "8", rx: "2", ry: "2", key: "17jyea" }],
  ["path", { d: "M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2", key: "zix9uf" }]
]);
const zw = pe("Download", [
  ["path", { d: "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4", key: "ih7n3h" }],
  ["polyline", { points: "7 10 12 15 17 10", key: "2ggqvy" }],
  ["line", { x1: "12", x2: "12", y1: "15", y2: "3", key: "1vk2je" }]
]);
const tc = pe("EyeOff", [
  [
    "path",
    {
      d: "M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49",
      key: "ct8e1f"
    }
  ],
  ["path", { d: "M14.084 14.158a3 3 0 0 1-4.242-4.242", key: "151rxh" }],
  [
    "path",
    {
      d: "M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143",
      key: "13bj9a"
    }
  ],
  ["path", { d: "m2 2 20 20", key: "1ooewy" }]
]);
const Wu = pe("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const Bu = pe("GripVertical", [
  ["circle", { cx: "9", cy: "12", r: "1", key: "1vctgf" }],
  ["circle", { cx: "9", cy: "5", r: "1", key: "hp0tcf" }],
  ["circle", { cx: "9", cy: "19", r: "1", key: "fkjjf6" }],
  ["circle", { cx: "15", cy: "12", r: "1", key: "1tmaij" }],
  ["circle", { cx: "15", cy: "5", r: "1", key: "19l28e" }],
  ["circle", { cx: "15", cy: "19", r: "1", key: "f4zoj3" }]
]);
const Rw = pe("ListChecks", [
  ["path", { d: "m3 17 2 2 4-4", key: "1jhpwq" }],
  ["path", { d: "m3 7 2 2 4-4", key: "1obspn" }],
  ["path", { d: "M13 6h8", key: "15sg57" }],
  ["path", { d: "M13 12h8", key: "h98zly" }],
  ["path", { d: "M13 18h8", key: "oe0vm4" }]
]);
const gs = pe("ListTree", [
  ["path", { d: "M21 12h-8", key: "1bmf0i" }],
  ["path", { d: "M21 6H8", key: "1pqkrb" }],
  ["path", { d: "M21 18h-8", key: "1tm79t" }],
  ["path", { d: "M3 6v4c0 1.1.9 2 2 2h3", key: "1ywdgy" }],
  ["path", { d: "M3 10v6c0 1.1.9 2 2 2h3", key: "2wc746" }]
]);
const mr = pe("Maximize2", [
  ["polyline", { points: "15 3 21 3 21 9", key: "mznyad" }],
  ["polyline", { points: "9 21 3 21 3 15", key: "1avn1i" }],
  ["line", { x1: "21", x2: "14", y1: "3", y2: "10", key: "ota7mn" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const nc = pe("Minimize2", [
  ["polyline", { points: "4 14 10 14 10 20", key: "11kfnr" }],
  ["polyline", { points: "20 10 14 10 14 4", key: "rlmsce" }],
  ["line", { x1: "14", x2: "21", y1: "10", y2: "3", key: "o5lafz" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const Lw = pe("Network", [
  ["rect", { x: "16", y: "16", width: "6", height: "6", rx: "1", key: "4q2zg0" }],
  ["rect", { x: "2", y: "16", width: "6", height: "6", rx: "1", key: "8cvhb9" }],
  ["rect", { x: "9", y: "2", width: "6", height: "6", rx: "1", key: "1egb70" }],
  ["path", { d: "M5 16v-3a1 1 0 0 1 1-1h12a1 1 0 0 1 1 1v3", key: "1jsf9p" }],
  ["path", { d: "M12 12V8", key: "2874zd" }]
]);
const Fu = pe("Package", [
  [
    "path",
    {
      d: "M11 21.73a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73z",
      key: "1a0edw"
    }
  ],
  ["path", { d: "M12 22V12", key: "d0xqtd" }],
  ["path", { d: "m3.3 7 7.703 4.734a2 2 0 0 0 1.994 0L20.7 7", key: "yx3hmr" }],
  ["path", { d: "m7.5 4.27 9 5.15", key: "1c824w" }]
]);
const vn = pe("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const bn = pe("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const Vw = pe("Redo2", [
  ["path", { d: "m15 14 5-5-5-5", key: "12vg1m" }],
  ["path", { d: "M20 9H9.5A5.5 5.5 0 0 0 4 14.5A5.5 5.5 0 0 0 9.5 20H13", key: "6uklza" }]
]);
const ys = pe("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const Ku = pe("Save", [
  [
    "path",
    {
      d: "M15.2 3a2 2 0 0 1 1.4.6l3.8 3.8a2 2 0 0 1 .6 1.4V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z",
      key: "1c8476"
    }
  ],
  ["path", { d: "M17 21v-7a1 1 0 0 0-1-1H8a1 1 0 0 0-1 1v7", key: "1ydtos" }],
  ["path", { d: "M7 3v4a1 1 0 0 0 1 1h7", key: "t51u73" }]
]);
const Vr = pe("Search", [
  ["circle", { cx: "11", cy: "11", r: "8", key: "4ej97u" }],
  ["path", { d: "m21 21-4.3-4.3", key: "1qie3q" }]
]);
const oc = pe("Shield", [
  [
    "path",
    {
      d: "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z",
      key: "oel41y"
    }
  ]
]);
const ms = pe("SlidersHorizontal", [
  ["line", { x1: "21", x2: "14", y1: "4", y2: "4", key: "obuewd" }],
  ["line", { x1: "10", x2: "3", y1: "4", y2: "4", key: "1q6298" }],
  ["line", { x1: "21", x2: "12", y1: "12", y2: "12", key: "1iu8h1" }],
  ["line", { x1: "8", x2: "3", y1: "12", y2: "12", key: "ntss68" }],
  ["line", { x1: "21", x2: "16", y1: "20", y2: "20", key: "14d8ph" }],
  ["line", { x1: "12", x2: "3", y1: "20", y2: "20", key: "m0wm8r" }],
  ["line", { x1: "14", x2: "14", y1: "2", y2: "6", key: "14e1ph" }],
  ["line", { x1: "8", x2: "8", y1: "10", y2: "14", key: "1i6ji0" }],
  ["line", { x1: "16", x2: "16", y1: "18", y2: "22", key: "1lctlv" }]
]);
const gt = pe("Sparkles", [
  [
    "path",
    {
      d: "M9.937 15.5A2 2 0 0 0 8.5 14.063l-6.135-1.582a.5.5 0 0 1 0-.962L8.5 9.936A2 2 0 0 0 9.937 8.5l1.582-6.135a.5.5 0 0 1 .963 0L14.063 8.5A2 2 0 0 0 15.5 9.937l6.135 1.581a.5.5 0 0 1 0 .964L15.5 14.063a2 2 0 0 0-1.437 1.437l-1.582 6.135a.5.5 0 0 1-.963 0z",
      key: "4pj2yx"
    }
  ],
  ["path", { d: "M20 3v4", key: "1olli1" }],
  ["path", { d: "M22 5h-4", key: "1gvqau" }],
  ["path", { d: "M4 17v2", key: "vumght" }],
  ["path", { d: "M5 18H3", key: "zchphs" }]
]);
const Ow = pe("Terminal", [
  ["polyline", { points: "4 17 10 11 4 5", key: "akl6gq" }],
  ["line", { x1: "12", x2: "20", y1: "19", y2: "19", key: "q2wloq" }]
]);
const uo = pe("Trash2", [
  ["path", { d: "M3 6h18", key: "d0wm0j" }],
  ["path", { d: "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", key: "4alrt4" }],
  ["path", { d: "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", key: "v07s0e" }],
  ["line", { x1: "10", x2: "10", y1: "11", y2: "17", key: "1uufr5" }],
  ["line", { x1: "14", x2: "14", y1: "11", y2: "17", key: "xtxkd" }]
]);
const xr = pe("TriangleAlert", [
  [
    "path",
    {
      d: "m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3",
      key: "wmoenq"
    }
  ],
  ["path", { d: "M12 9v4", key: "juzpu7" }],
  ["path", { d: "M12 17h.01", key: "p32p05" }]
]);
const Hw = pe("Undo2", [
  ["path", { d: "M9 14 4 9l5-5", key: "102s5s" }],
  ["path", { d: "M4 9h10.5a5.5 5.5 0 0 1 5.5 5.5a5.5 5.5 0 0 1-5.5 5.5H11", key: "f3b9sd" }]
]);
const Ww = pe("Workflow", [
  ["rect", { width: "8", height: "8", x: "3", y: "3", rx: "2", key: "by2w9f" }],
  ["path", { d: "M7 11v4a2 2 0 0 0 2 2h4", key: "xkn7yn" }],
  ["rect", { width: "8", height: "8", x: "13", y: "13", rx: "2", key: "1cgmvn" }]
]);
const Bw = pe("Wrench", [
  [
    "path",
    {
      d: "M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z",
      key: "cbrjhi"
    }
  ]
]);
const qu = pe("X", [
  ["path", { d: "M18 6 6 18", key: "1bl5f8" }],
  ["path", { d: "m6 6 12 12", key: "d8bk6v" }]
]);
const Fw = pe("Zap", [
  [
    "path",
    {
      d: "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z",
      key: "1xq2db"
    }
  ]
]), Kw = "String", qw = "singleline";
function Uw(e) {
  return e === "Single" || e === "Array" || e === "List" || e === "HashSet";
}
function xs(e, t = "Single") {
  return { alias: (e ?? "").trim() || Kw, collectionKind: t };
}
function Uu(e) {
  const t = e.type ?? e.Type;
  if (wr(t))
    return { alias: typeof t.alias == "string" ? t.alias : typeof t.typeName == "string" ? t.typeName : "", collectionKind: Uw(t.collectionKind) ? t.collectionKind : "Single" };
  const n = e.typeInformation ?? e.TypeInformation;
  return wr(n) ? { alias: typeof n.typeName == "string" ? n.typeName : "", collectionKind: rc(e) ? "Array" : "Single" } : { alias: typeof t == "string" ? t : "", collectionKind: rc(e) ? "Array" : "Single" };
}
function rc(e) {
  const t = e.isArray ?? e.IsArray;
  return t === !0 || t === "true";
}
function ic(e) {
  return (e.alias ?? e.typeName ?? "").trim();
}
function Xw() {
  return typeof crypto < "u" && typeof crypto.randomUUID == "function" ? crypto.randomUUID() : `id-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
}
function Yw(e, t) {
  const n = new Set(t);
  let o = 1, r = `${e}${o}`;
  for (; n.has(r); )
    o += 1, r = `${e}${o}`;
  return r;
}
function Zw(e) {
  return {
    referenceKey: Xw(),
    name: e.name,
    type: xs(e.alias),
    storageDriverType: e.storageDriver?.trim() || null,
    default: null
  };
}
function Gw(e, t) {
  return { ...e, ...t };
}
function Jw(e) {
  return e === "" ? null : { value: e, expressionType: "Literal" };
}
function Qw(e) {
  return e == null || e.value == null ? "" : typeof e.value == "object" ? JSON.stringify(e.value) : String(e.value);
}
function e0(e) {
  return {
    name: e.name,
    type: xs(e.alias),
    displayName: e.name,
    description: "",
    category: "",
    uiHint: qw,
    storageDriverType: e.storageDriver ?? null,
    defaultValue: null,
    defaultSyntax: null,
    isReadOnly: null
  };
}
function t0(e, t) {
  return { ...e, ...t };
}
function n0(e) {
  return {
    name: e.name,
    type: xs(e.alias),
    displayName: e.name,
    description: "",
    category: ""
  };
}
function o0(e, t) {
  return { ...e, ...t };
}
function r0(e) {
  const t = e.split(",")[0].trim();
  return (t.split(".").pop() ?? t).split("`")[0];
}
function Xu(e, t) {
  return e && !e.includes(",") && !e.includes(".") ? e : r0(e || t);
}
function i0(e, t) {
  return Xu(e, t).replace(/StorageDriver$/, "");
}
function wr(e) {
  return !!e && typeof e == "object" && !Array.isArray(e);
}
function Jn(e, t) {
  for (const n of t) {
    const o = e[n];
    if (o != null && typeof o != "object") return String(o);
  }
  return "";
}
const s0 = ["name", "Name"], Yu = ["name", "Name"], a0 = ["storageDriverType", "StorageDriverType"], c0 = ["defaultValue", "DefaultValue"], l0 = /* @__PURE__ */ new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);
function vo(e) {
  return Zu(e, p0);
}
function Or(e) {
  return Zu(e, h0);
}
function Zu(e, t) {
  if (!e) return e;
  const n = { ...e };
  return e.rootActivity && (n.rootActivity = Gu(e.rootActivity, t)), Array.isArray(e.variables) && (n.variables = Qo(e.variables, Ri)), Array.isArray(e.inputs) && (n.inputs = Qo(e.inputs, Ri)), Array.isArray(e.outputs) && (n.outputs = Qo(e.outputs, (o) => Ju(o, !1))), n;
}
function Gu(e, t) {
  const n = t(e), o = n.structure;
  if (!o || !at(o.payload)) return n;
  let r = !1;
  const s = { ...o.payload };
  for (const [a, c] of Object.entries(o.payload))
    Array.isArray(c) && c.length > 0 && c.every(w0) && (s[a] = c.map((u) => Gu(u, t)), r = !0);
  return Array.isArray(o.payload.variables) && o.payload.variables.length > 0 && (s.variables = Qo(o.payload.variables, Ri), r = !0), r ? { ...n, structure: { ...o, payload: s } } : n;
}
function Qo(e, t) {
  return e.map((n) => at(n) && !Array.isArray(n) ? t(n) : n);
}
function Ri(e) {
  return Ju(e, !0);
}
const u0 = ["type", "Type", "typeInformation", "TypeInformation", "isArray", "IsArray", "storageDriverType", "StorageDriverType"];
function Ju(e, t) {
  const n = d0(e, u0);
  return n.type = Uu(e), t && (n.storageDriverType = f0(e.storageDriverType ?? e.StorageDriverType)), n;
}
function d0(e, t) {
  const n = new Set(t), o = {};
  for (const [r, s] of Object.entries(e))
    n.has(r) || (o[r] = s);
  return o;
}
function f0(e) {
  if (typeof e == "string") return e.trim() ? e : null;
  if (at(e)) {
    const t = typeof e.typeName == "string" ? e.typeName : "";
    if (!t) return null;
    const n = typeof e.namespace == "string" ? e.namespace : "";
    return n ? `${n}.${t}` : t;
  }
  return null;
}
function p0(e) {
  const t = [], n = {};
  for (const [r, s] of Object.entries(e))
    l0.has(r) || (x0(s) ? t.push({
      referenceKey: g0(r),
      value: m0(s.expression)
    }) : n[r] = s);
  const o = Array.isArray(e.inputs) ? e.inputs : [];
  return {
    ...n,
    nodeId: e.nodeId,
    activityVersionId: e.activityVersionId,
    inputs: [...o, ...t],
    outputs: Array.isArray(e.outputs) ? e.outputs : [],
    structure: e.structure
  };
}
function h0(e) {
  const t = Array.isArray(e.inputs) ? e.inputs : [], n = {};
  for (const o of t) {
    if (!at(o) || typeof o.referenceKey != "string") continue;
    const r = at(o.value) ? o.value : {};
    n[y0(o.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof r.expressionType == "string" ? r.expressionType : "Literal",
        value: r.value ?? ""
      }
    };
  }
  return { ...e, ...n, inputs: [] };
}
function g0(e) {
  return e && e.charAt(0).toUpperCase() + e.slice(1);
}
function y0(e) {
  return e && e.charAt(0).toLowerCase() + e.slice(1);
}
function m0(e) {
  const t = e.type || "Literal";
  return t === "Variable" && at(e.value) ? { value: e.value, expressionType: t } : t === "Literal" && at(e.value) ? { value: sc(e.value), expressionType: "Object" } : { value: sc(e.value), expressionType: t };
}
function sc(e) {
  return e == null ? null : typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function x0(e) {
  if (!at(e) || Array.isArray(e)) return !1;
  const t = e.expression;
  return at(t) && typeof t.type == "string";
}
function w0(e) {
  return at(e) && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function at(e) {
  return typeof e == "object" && e !== null;
}
const bo = "elsa.sequence.structure", Sn = "elsa.flowchart.structure";
function Qu(e, t) {
  if (!e) return null;
  let n = e;
  for (const o of t) {
    const r = We(n).find((a) => a.id === o.slotId);
    if (!r) return null;
    const s = r.activities.find((a) => a.nodeId === o.ownerNodeId);
    if (!s) return null;
    n = s;
  }
  return n;
}
function v0(e, t, n = (o) => o.nodeId) {
  if (!e) return null;
  if (e.nodeId === t) return [];
  const o = (r, s) => {
    const a = We(r);
    if (a[0]?.activities.some((c) => c.nodeId === t)) return s;
    for (const c of a)
      for (const u of c.activities) {
        const l = o(u, [...s, { ownerNodeId: u.nodeId, slotId: c.id, label: n(u) }]);
        if (l) return l;
      }
    return null;
  };
  return o(e, []);
}
function Qn(e, t) {
  const n = Qu(e, t);
  if (!n) return null;
  let o = We(n)[0];
  return o ? { owner: n, slot: o } : null;
}
function We(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = V0(t), r = wi(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: O0(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, s]) => wi(s)).map(([s, a]) => ({
    id: `${t.kind}:${s}`,
    label: W0(s),
    property: s,
    mode: "generic",
    activities: wi(a) ?? []
  }));
}
function ed(e, t, n) {
  const o = new Map(t.map((a) => [a.activityVersionId, a])), r = new Map(n.map((a) => [a.nodeId, a])), s = e.slot.activities.map((a, c) => {
    const u = o.get(a.activityVersionId), l = r.get(a.nodeId) ?? H0(e.slot.mode, c);
    return od(a, u, { x: l.x, y: l.y });
  });
  return {
    nodes: s,
    edges: e.slot.mode === "flowchart" ? rd(e.owner) : D0(e.slot, s)
  };
}
function Li(e, t, n) {
  const o = t.find((s) => s.activityVersionId === e.activityVersionId), r = n.find((s) => s.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [od(e, o, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function b0(e, t, n, o = null) {
  const r = new Map(t.map((c) => [c.activityExecutionId, c])), s = cc(t, (c) => c.authoredActivityId || c.executableNodeId), a = cc(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? r.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const u = s.get(c.id) ?? [], l = a.get(c.id) ?? [];
    if (u.length === 0 && l.length === 0) return c;
    const f = z0(u), p = o === c.id || u.some((h) => h.activityExecutionId === o) || l.some((h) => h.incidentId === o), d = {
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
function ws(e, t) {
  return e?.structure?.kind === Sn || E0(t) ? "flowchart" : e?.structure?.kind === bo || C0(t) ? "sequence" : "unsupported";
}
function Vi(e, t, n) {
  if (t.length === 0) {
    const c = We(e)[0];
    return c ? fo(e, c, n) : e;
  }
  const [o, ...r] = t, s = We(e).find((c) => c.id === o.slotId);
  if (!s) return e;
  const a = s.activities.map((c) => c.nodeId === o.ownerNodeId ? Vi(c, r, n) : c);
  return fo(e, s, a);
}
function td(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, s = We(e).find((c) => c.id === o.slotId);
  if (!s) return e;
  const a = s.activities.map((c) => c.nodeId === o.ownerNodeId ? td(c, r, n) : c);
  return fo(e, s, a);
}
function nd(e, t, n) {
  if (e.nodeId === t) return n(e);
  const o = We(e);
  if (o.length === 0) return e;
  let r = !1, s = e;
  for (const a of o) {
    const c = a.activities.map((u) => {
      const l = nd(u, t, n);
      return l !== u && (r = !0), l;
    });
    r && (s = fo(s, a, c));
  }
  return r ? s : e;
}
function fo(e, t, n) {
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
function N0(e, t, n, o = []) {
  const r = new Map(e.slot.activities.map((a) => [a.nodeId, a]));
  for (const a of o)
    r.set(a.nodeId, a);
  const s = t.map((a) => r.get(a.id)).filter((a) => !!a);
  return e.slot.mode === "sequence" && s.sort((a, c) => {
    const u = t.find((f) => f.id === a.nodeId), l = t.find((f) => f.id === c.nodeId);
    return (u?.position.x ?? 0) - (l?.position.x ?? 0);
  }), fo(e.owner, e.slot, s);
}
function j0(e, t) {
  return {
    ...e,
    structure: _0(e.structure, t)
  };
}
function S0(e, t) {
  const n = new Map(e.map((o) => [o.nodeId, o]));
  for (const o of t)
    n.set(o.id, {
      ...n.get(o.id) ?? { nodeId: o.id },
      nodeId: o.id,
      x: Math.round(o.position.x),
      y: Math.round(o.position.y)
    });
  return [...n.values()];
}
function Oi(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: A0(e)
  };
}
function Ie(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? I0(t) : n;
}
function od(e, t, n, o = {}) {
  return {
    id: e.nodeId,
    type: "workflowActivity",
    position: n,
    connectable: o.connectable,
    deletable: o.deletable,
    draggable: o.draggable,
    data: {
      label: t ? Ie(t) : e.activityVersionId,
      activityVersionId: e.activityVersionId,
      activityTypeKey: t?.activityTypeKey,
      category: t?.category,
      executionType: t?.executionType,
      icon: vr(t),
      childSlots: We(e),
      acceptsInbound: M0(e, t),
      sourcePorts: o.suppressFlowPorts ? [] : id(e, t),
      suppressFlowPorts: o.suppressFlowPorts
    }
  };
}
function vr(e) {
  if (!e) return "activity";
  const t = k0(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), o = Ie(e).toLowerCase(), r = e.category?.toLowerCase() ?? "", s = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || o === "flowchart" ? "flowchart" : n.endsWith(".sequence") || o === "sequence" ? "sequence" : n.includes("writeline") || o.includes("write line") ? "terminal" : r.includes("runtime") ? "runtime" : s === "trigger" ? "trigger" : "activity";
}
function k0(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function E0(e) {
  return !!e && (Ie(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function C0(e) {
  return !!e && (Ie(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function I0(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function A0(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: bo,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: Sn,
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
function _0(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], o = /* @__PURE__ */ new Map();
  for (const r of n) {
    if (!vs(r)) continue;
    const s = r.id;
    typeof s == "string" && o.set(s, r);
  }
  return {
    ...e,
    payload: {
      ...e.payload,
      connections: t.map((r) => {
        const s = o.get(r.id) ?? {}, a = r.data?.vertices, { vertices: c, ...u } = s;
        return {
          ...u,
          id: r.id,
          source: { nodeId: r.source, port: r.sourceHandle ?? "Done" },
          target: r.targetHandle ? { nodeId: r.target, port: r.targetHandle } : { nodeId: r.target },
          ...a?.length ? { vertices: a.map((l) => ({ x: Math.round(l.x), y: Math.round(l.y) })) } : {}
        };
      })
    }
  };
}
function D0(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function rd(e) {
  if (e.structure?.kind !== Sn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, s = n.target;
    if (!r?.nodeId || !s?.nodeId) return null;
    const a = Array.isArray(n.vertices) ? n.vertices.filter(R0) : [];
    return {
      id: typeof n.id == "string" ? String(n.id) : `flow-${o}-${r.nodeId}-${s.nodeId}`,
      source: r.nodeId,
      target: s.nodeId,
      sourceHandle: r.port,
      targetHandle: s.port && s.port !== "Done" ? s.port : void 0,
      type: "workflow",
      label: r.port && r.port !== "Done" ? r.port : void 0,
      data: a.length ? { vertices: a } : void 0
    };
  }).filter((n) => !!n) : [];
}
function id(e, t) {
  const n = ac(e.cases);
  if ($0(e, t) && n.length > 0)
    return [...n.map((s) => ({ name: s, displayName: s })), { name: "Default", displayName: "Default" }];
  const o = [
    ...er(t?.designFacets),
    ...er(t?.ports),
    ...er(t?.outputs)
  ];
  if (o.length > 0) return P0(o);
  const r = ac(e.outcomes);
  return r.length > 0 ? r.map((s) => ({ name: s, displayName: s })) : [{ name: "Done", displayName: "Done" }];
}
function M0(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function br(e, t, n, o) {
  const r = n ?? "Done";
  return {
    id: `flow-${e}-${t}-${r}-${crypto.randomUUID().slice(0, 8)}`,
    source: e,
    target: t,
    sourceHandle: r,
    targetHandle: o ?? void 0,
    type: "workflow",
    label: r !== "Done" ? r : void 0
  };
}
function T0(e, t, n) {
  const o = br(t.source, n, t.sourceHandle ?? "Done", void 0), r = br(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((s) => s.id !== t.id).concat(o, r);
}
function wi(e) {
  return Array.isArray(e) ? e.filter(L0) : null;
}
function $0(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function er(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!vs(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...er(n.ports));
      continue;
    }
    const o = typeof n.type == "string" ? n.type : typeof n.portType == "string" ? n.portType : "", r = n.isBrowsable !== !1 && n.browsable !== !1, s = typeof n.name == "string" ? n.name : typeof n.id == "string" ? n.id : "";
    if (r && o.toLowerCase() === "flow" && s) {
      const a = typeof n.displayName == "string" ? n.displayName : s;
      t.push({ name: s, displayName: a });
    }
  }
  return t;
}
function P0(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function ac(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function cc(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const o of e) {
    const r = t(o);
    r && n.set(r, [...n.get(r) ?? [], o]);
  }
  return n;
}
function z0(e) {
  return [...e].sort((t, n) => lc(n).localeCompare(lc(t)))[0];
}
function lc(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function R0(e) {
  return vs(e) && typeof e.x == "number" && typeof e.y == "number";
}
function vs(e) {
  return typeof e == "object" && e !== null;
}
function L0(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function V0(e) {
  return e.kind === bo ? "sequence" : e.kind === Sn ? "flowchart" : "generic";
}
function O0(e) {
  return e.kind === bo || e.kind === Sn, "Activities";
}
function H0(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function W0(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Nr = "workflow", B0 = /* @__PURE__ */ new Set([bo, Sn]);
function F0(e) {
  const t = e?.structure?.kind;
  return !!t && B0.has(t);
}
function sd(e) {
  const t = e?.structure?.payload?.variables;
  return Array.isArray(t) ? t.filter(wr) : [];
}
function K0(e, t) {
  return e.structure ? {
    ...e,
    structure: {
      ...e.structure,
      payload: { ...e.structure.payload, variables: t }
    }
  } : e;
}
function q0(e, t) {
  return {
    referenceKey: e,
    declaringScopeId: t && t !== Nr ? t : Nr
  };
}
function ad(e) {
  if (e && typeof e == "object") {
    const t = e;
    return typeof t.referenceKey == "string" && t.referenceKey ? { referenceKey: t.referenceKey, declaringScopeId: typeof t.declaringScopeId == "string" ? t.declaringScopeId : null } : null;
  }
  if (typeof e == "string") {
    const t = e.trim();
    if (t.startsWith("{"))
      try {
        return ad(JSON.parse(t));
      } catch {
      }
    return t ? { referenceKey: t, declaringScopeId: null } : null;
  }
  return null;
}
function U0(e) {
  if (!e) return "";
  const t = [`workflow:${uc(e.variables)}`], n = (o) => {
    const r = We(o), s = r.flatMap((a) => a.activities.map((c) => c.nodeId));
    t.push(`${o.nodeId}:${uc(sd(o))}>${s.join(",")}`), r.forEach((a) => a.activities.forEach(n));
  };
  return e.rootActivity && n(e.rootActivity), t.join(";");
}
function uc(e) {
  return (e ?? []).map((t) => `${t.referenceKey}=${t.name}`).join(",");
}
function X0(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const o of e ?? [])
    o.scopeId === t && n.set(o.referenceKey, `Shadows "${o.name}" declared in an outer scope.`);
  return n;
}
const ke = "/_elsa/workflow-management", Y0 = "/publishing", eo = {
  activities: ["workflows", "activities"],
  activityAvailabilitySettings: ["workflows", "activity-availability", "settings"],
  activityAvailabilityDiagnostics: ["workflows", "activity-availability", "diagnostics"]
};
function Z0(e) {
  return qc({
    queryKey: eo.activityAvailabilitySettings,
    queryFn: () => gv(e)
  });
}
function G0(e) {
  return qc({
    queryKey: eo.activityAvailabilityDiagnostics,
    queryFn: () => ud(e)
  });
}
function J0(e) {
  const t = xf();
  return wf({
    mutationFn: (n) => yv(e, n),
    onSuccess: () => {
      t.invalidateQueries({ queryKey: eo.activityAvailabilitySettings }), t.invalidateQueries({ queryKey: eo.activityAvailabilityDiagnostics }), t.invalidateQueries({ queryKey: eo.activities });
    }
  });
}
async function Q0(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${ke}/definitions?${n.toString()}`);
}
async function ev(e, t) {
  const n = await e.http.getJson(`${ke}/definitions/${encodeURIComponent(t)}`);
  return n.draft ? { ...n, draft: { ...n.draft, state: Or(n.draft.state) } } : n;
}
async function tv(e, t, n) {
  const o = await e.http.postJson(
    `${ke}/design/scoped-variables/analyze`,
    { state: vo(t), nodeId: n }
  );
  return {
    visibleVariables: Array.isArray(o?.visibleVariables) ? o.visibleVariables : [],
    shadowingWarnings: Array.isArray(o?.shadowingWarnings) ? o.shadowingWarnings : []
  };
}
const vi = (e) => ({ visibleVariables: [], shadowingWarnings: [], status: e });
function nv(e, t, n) {
  const o = de(() => U0(t), [t]), [r, s] = q(() => vi("loading"));
  return Q(() => {
    if (!t) {
      s(vi("unavailable"));
      return;
    }
    let a = !1;
    return s((c) => ({ ...c, status: "loading" })), tv(e, t, n).then(
      (c) => {
        a || s({ ...c, status: "ready" });
      },
      () => {
        a || s(vi("unavailable"));
      }
    ), () => {
      a = !0;
    };
  }, [e, n, o]), r;
}
async function ov(e, t) {
  const n = await e.http.getJson(`${ke}/versions/${encodeURIComponent(t)}`);
  return { ...n, state: Or(n.state) };
}
async function rv(e, t) {
  return e.http.postJson(`${ke}/definitions`, t);
}
async function iv(e, t) {
  await e.http.deleteJson(`${ke}/definitions/${encodeURIComponent(t)}`);
}
async function sv(e, t) {
  await e.http.postJson(`${ke}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function av(e, t) {
  await e.http.deleteJson(`${ke}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function cv(e, t, n) {
  return e.http.requestJson(
    `${ke}/definitions/${encodeURIComponent(t)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(n)
    }
  );
}
async function lv(e, t) {
  const n = await e.http.putJson(
    `${ke}/drafts/${encodeURIComponent(t.id)}`,
    { state: vo(t.state), layout: t.layout }
  );
  return { ...n, state: Or(n.state) };
}
async function uv(e, t) {
  return e.http.postJson(`${ke}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function dv(e, t) {
  return e.http.postJson(`${ke}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function fv(e, t) {
  const n = { ...t, state: vo(t.state) };
  try {
    return await e.http.postJson(`${Y0}/workflows/drafts/test-runs`, n);
  } catch (o) {
    const r = jv(o);
    if (r) return r;
    throw o;
  }
}
async function cd(e, t) {
  return e.http.postJson(`${ke}/executables/${encodeURIComponent(t)}/run`, {});
}
async function ld(e) {
  return e.http.getJson("/_demo/workflows/executables");
}
async function pv(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.runKind && n.set("runKind", t.runKind), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const o = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${o ? `?${o}` : ""}`);
}
async function hv(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function bs(e) {
  return e.http.getJson(`${ke}/activities`);
}
async function gv(e) {
  return e.http.getJson(`${ke}/activities/availability/settings`);
}
async function yv(e, t) {
  return e.http.putJson(`${ke}/activities/availability/settings`, t);
}
async function ud(e) {
  return e.http.getJson(`${ke}/activities/availability/diagnostics`);
}
async function mv(e) {
  const t = await Hr(e, [
    `${ke}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? dc(t) : dc(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function xv(e) {
  const t = await Hr(e, [
    `${ke}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : tr;
}
async function wv(e) {
  const t = await Hr(e, [
    `${ke}/descriptors/variables`,
    "/descriptors/variables"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((o) => vv(o));
}
function vv(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = typeof t.alias == "string" && t.alias.length > 0, o = typeof t.typeName == "string" && t.typeName.length > 0;
  return n || o;
}
async function bv(e) {
  const t = await Hr(e, [
    `${ke}/descriptors/storage-drivers`,
    "/descriptors/storage-drivers"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((o) => Nv(o));
}
function Nv(e) {
  return !!e && typeof e == "object" && typeof e.typeName == "string" && e.typeName.length > 0;
}
async function Hr(e, t) {
  let n;
  for (const o of t)
    try {
      return await e.http.getJson(o);
    } catch (r) {
      n = r;
    }
  throw n;
}
function dc(e) {
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
function jv(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = fc(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return fc(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function fc(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const tr = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
], Sv = [
  "Available",
  "BlockedByHostBaseline",
  "HiddenByManagementSettings",
  "RemovedFromCatalog",
  "UnresolvedReference"
], kv = {
  Available: "Available",
  BlockedByHostBaseline: "Host blocked",
  HiddenByManagementSettings: "Management hidden",
  RemovedFromCatalog: "Removed",
  UnresolvedReference: "Unresolved"
};
function Rt(e) {
  return typeof e == "string" ? e : typeof e == "number" ? Sv[e] ?? "Available" : "Available";
}
function jr(e) {
  const t = Rt(e);
  return kv[t] ?? t;
}
function Ev(e) {
  return Rt(e).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Cv(e) {
  return Rt(e) !== "Available";
}
function Iv(e) {
  return e === "Only" || e === 1 ? "Only" : "AllExcept";
}
function Av(e) {
  return e === "Only" ? 1 : 0;
}
function pc(e) {
  const t = e?.rules;
  return {
    mode: Iv(e?.mode),
    activityTypes: t?.activityTypes ?? [],
    sets: t?.sets ?? []
  };
}
function _v(e) {
  return e.referenceKind === 0 || e.referenceKind === "ActivityType";
}
function Dv(e) {
  return [...e?.items ?? []].filter(_v).filter((t) => t.activityTypeKey && t.activityDefinitionId).sort((t, n) => Sr(t).localeCompare(Sr(n)));
}
function Mv(e) {
  return [...e?.items ?? []].filter((t) => {
    const n = Rt(t.state);
    return n === "RemovedFromCatalog" || n === "UnresolvedReference";
  }).sort((t, n) => (t.referenceName ?? "").localeCompare(n.referenceName ?? ""));
}
function hc(e, t) {
  return e.includes(t) ? e.filter((n) => n !== t) : [...e, t].sort((n, o) => n.localeCompare(o));
}
function Sr(e) {
  const t = e?.displayName?.trim();
  if (t) return t;
  const n = e?.activityTypeKey?.split(".").filter(Boolean).at(-1) ?? "";
  return Tv(n) || e?.activityTypeKey || "Activity";
}
function Tv(e) {
  return e.replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").replace(/([a-z0-9])([A-Z])/g, "$1 $2").trim();
}
function $v(e, t) {
  const n = new Set(e.filter((o) => !!o));
  return (t?.items ?? []).find((o) => Cv(o.state) ? [o.activityDefinitionId, o.activityTypeKey, o.referenceName].some((r) => r && n.has(r)) : !1) ?? null;
}
const Pv = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function dd(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function Ns(e) {
  return dd(e.name);
}
function zv(e, t) {
  const n = Ns(t), o = e[n];
  return t.isWrapped === !1 ? o ?? t.defaultValue ?? "" : pd(o, t);
}
function fd(e, t) {
  return pd(e[Ns(t)], t);
}
function Rv(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function Lv(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function gc(e, t, n) {
  return {
    ...e,
    [Ns(t)]: n
  };
}
function Vv(e, t) {
  return t.isWrapped === !1 ? zv(e, t) : fd(e, t).expression.value;
}
function pd(e, t) {
  return Uv(e) ? {
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
const Ov = /* @__PURE__ */ new Set([
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
function Hv(e) {
  const t = e?.trim();
  if (!t) return null;
  const n = /^([\w.+]+)\[\]/.exec(t);
  if (n) return { elementTypeName: n[1] };
  const o = t.indexOf("`");
  if (o < 0) return null;
  const r = t.slice(0, o), s = (r.split(".").pop() ?? r).toLowerCase();
  return Ov.has(s) ? { elementTypeName: Wv(t.slice(o)) } : null;
}
function Wv(e) {
  const t = /\[\[([\w.+]+)/.exec(e);
  if (t) return t[1];
  const n = /\[([\w.+]+)/.exec(e);
  return n ? n[1] : null;
}
function Bv(e) {
  const t = e.uiHint?.toLowerCase();
  return t === "json" || t === "code" ? !0 : e.uiSpecifications?.repeater === !1;
}
function Fv(e) {
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
function Kv(e) {
  const t = (e ?? "").toLowerCase();
  return t === "system.boolean" || t === "boolean" || t === "bool" ? !1 : "";
}
function qv(e, t) {
  return {
    ...e,
    typeName: t ?? "System.String",
    isWrapped: !1,
    // The description documents the collection as a whole; repeating it on every row is noise.
    description: null
  };
}
function bi(e, t, n) {
  if (t === n || t < 0 || n < 0 || t >= e.length || n >= e.length) return e;
  const o = [...e], [r] = o.splice(t, 1);
  return o.splice(n, 0, r), o;
}
function Uv(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
function hd(e) {
  return js(e?.trim() ?? "") || e;
}
function js(e) {
  if (!e) return "";
  const t = Xv(e);
  if (!t) return "";
  const n = /^(.*)((?:\[\])+)$/.exec(t);
  if (n) return `${js(n[1])}${n[2]}`;
  const o = t.indexOf("`");
  if (o >= 0) {
    const r = yc(t.slice(0, o)), s = Yv(t.slice(o));
    return s.length > 0 ? `${r}<${s.join(", ")}>` : r;
  }
  return yc(t);
}
function Xv(e) {
  let t = 0;
  for (let n = 0; n < e.length; n++) {
    const o = e[n];
    if (o === "[") t++;
    else if (o === "]") t--;
    else if (o === "," && t === 0) return e.slice(0, n).trim();
  }
  return e.trim();
}
function yc(e) {
  const t = e.split(".").filter(Boolean).at(-1) ?? e;
  return t.split("+").filter(Boolean).at(-1) ?? t;
}
function Yv(e) {
  const t = e.indexOf("[");
  if (t < 0) return [];
  const n = mc(e, t);
  return n == null ? [] : Zv(n).map((o) => {
    const r = o.trim(), s = r.startsWith("[") ? mc(r, 0) ?? r : r;
    return js(s);
  }).filter(Boolean);
}
function mc(e, t) {
  let n = 0;
  for (let o = t; o < e.length; o++)
    if (e[o] === "[") n++;
    else if (e[o] === "]" && --n === 0) return e.slice(t + 1, o);
  return null;
}
function Zv(e) {
  const t = [];
  let n = 0, o = 0;
  for (let r = 0; r < e.length; r++) {
    const s = e[r];
    s === "[" ? n++ : s === "]" ? n-- : s === "," && n === 0 && (t.push(e.slice(o, r)), o = r + 1);
  }
  return t.push(e.slice(o)), t.map((r) => r.trim()).filter(Boolean);
}
const gd = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]), Gv = "Variable";
function Jv({
  activity: e,
  descriptor: t,
  editors: n,
  expressionEditors: o,
  expressionDescriptors: r,
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
  const f = sb(l), p = r.length > 0 ? r : Pv;
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ i.jsx("span", { className: "wf-section-label", children: "Properties" }),
    f.map((d) => /* @__PURE__ */ i.jsxs("section", { className: "wf-property-group", children: [
      f.length > 1 ? /* @__PURE__ */ i.jsx("h4", { children: d.category }) : null,
      d.inputs.map((h) => /* @__PURE__ */ i.jsx(
        Qv,
        {
          activity: e,
          input: h,
          editors: n,
          expressionEditors: o,
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
function Qv({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: o,
  expressionDescriptors: r,
  visibleVariables: s,
  scopeStatus: a,
  onChange: c
}) {
  const u = t.isReadOnly === !0, l = { activity: e, expressionDescriptors: r, readOnly: u }, f = Fi(n, t, l), p = f?.component, d = t.isWrapped !== !1 ? fd(e, t) : null, h = d?.expression.type ?? "Literal", g = Vv(e, t), N = h.toLowerCase(), m = d && (N === "literal" || N === "object") && !Bv(t) ? Hv(t.typeName) : null, E = m ? Fi(n, t, { ...l, scope: "collection" }) : void 0, y = d ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: r,
    readOnly: u,
    surface: "inline",
    syntax: h
  } : null, b = y ? md(o, y) : null, j = b?.surfaces.inline, v = b && y ? xd(b, y, g) : [], S = m != null, _ = !!(d && !S && ab(t, f?.id)), $ = !!(d && !S && cb(t, f?.id)), [V, D] = q(!1), A = (I) => {
    const T = d ? Rv(d, I) : I;
    c(gc(e, t, T));
  }, L = (I) => {
    d && c(gc(e, t, Lv(d, I)));
  }, k = m ? E ? Hi(E.component, t, g, u, { ...l, scope: "collection" }, A) : /* @__PURE__ */ i.jsx(
    tb,
    {
      input: t,
      elementTypeName: m.elementTypeName,
      value: g,
      editors: n,
      context: l,
      disabled: u,
      onChange: A
    }
  ) : null, M = h === Gv && d ? /* @__PURE__ */ i.jsx(
    rb,
    {
      value: g,
      visibleVariables: s,
      scopeStatus: a,
      disabled: u,
      onChange: A
    }
  ) : k ?? (j && y ? /* @__PURE__ */ i.jsx(
    j,
    {
      descriptor: t,
      syntax: h,
      value: g,
      disabled: u,
      context: y,
      onChange: A
    }
  ) : Hi(p, t, g, u, l, A));
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ i.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ i.jsx("span", { children: hd(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ i.jsx("p", { children: t.description }) : null,
    d && !_ ? /* @__PURE__ */ i.jsx(
      Wi,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: h,
        descriptors: r,
        disabled: u,
        onChange: L
      }
    ) : null,
    _ ? /* @__PURE__ */ i.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-expression-editor", children: [
        M,
        Ki(v)
      ] }),
      /* @__PURE__ */ i.jsx(
        Wi,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: h,
          descriptors: r,
          disabled: u,
          variant: "inline",
          onChange: L
        }
      ),
      $ ? /* @__PURE__ */ i.jsx(
        "button",
        {
          type: "button",
          className: "wf-expression-expand-button",
          "aria-label": `Open expanded ${t.displayName || t.name} editor`,
          title: "Open expanded editor",
          onClick: () => D(!0),
          children: /* @__PURE__ */ i.jsx(mr, { size: 13 })
        }
      ) : null
    ] }) : /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
      M,
      Ki(v)
    ] }),
    $ && !_ ? /* @__PURE__ */ i.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => D(!0),
        children: [
          /* @__PURE__ */ i.jsx(mr, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    V ? /* @__PURE__ */ i.jsx(
      nb,
      {
        input: t,
        value: g,
        syntax: h,
        descriptors: r,
        activity: e,
        expressionEditors: o,
        disabled: u,
        onChange: A,
        onSyntaxChange: L,
        onClose: () => D(!1)
      }
    ) : null
  ] });
}
function eb(e, t, n) {
  return [
    "wf-collection-item",
    t === e ? "dragging" : "",
    t !== null && t !== e && n === e ? "drop-target" : ""
  ].filter(Boolean).join(" ");
}
function tb({
  input: e,
  elementTypeName: t,
  value: n,
  editors: o,
  context: r,
  disabled: s,
  onChange: a
}) {
  const c = Fv(n), u = qv(e, t), l = { ...r, scope: "element" }, f = Fi(o, u, l)?.component, p = e.displayName || e.name, d = (j, v) => a(c.map((S, _) => _ === j ? v : S)), [h, g] = q(null), [N, w] = q(null), m = () => {
    g(null), w(null);
  }, E = (j) => (v) => {
    g(j), v.dataTransfer.effectAllowed = "move", v.dataTransfer.setData("text/plain", String(j));
  }, y = (j) => (v) => {
    h !== null && (v.preventDefault(), v.dataTransfer.dropEffect = "move", N !== j && w(j));
  }, b = (j) => (v) => {
    v.preventDefault(), h !== null && h !== j && a(bi(c, h, j)), m();
  };
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-collection-editor", children: [
    c.length === 0 ? /* @__PURE__ */ i.jsx("p", { className: "wf-collection-empty", children: "No items yet." }) : /* @__PURE__ */ i.jsx("ul", { className: "wf-collection-items", children: c.map((j, v) => /* @__PURE__ */ i.jsxs(
      "li",
      {
        className: eb(v, h, N),
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
              onDragStart: E(v),
              onDragEnd: m,
              children: /* @__PURE__ */ i.jsx(Bu, { size: 13, "aria-hidden": "true" })
            }
          ),
          /* @__PURE__ */ i.jsx("div", { className: "wf-collection-item-editor", children: Hi(f, u, j, s, l, (S) => d(v, S)) }),
          /* @__PURE__ */ i.jsxs("div", { className: "wf-collection-item-actions", children: [
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${p} item ${v + 1} up`,
                disabled: s || v === 0,
                onClick: () => a(bi(c, v, v - 1)),
                children: /* @__PURE__ */ i.jsx(Tw, { size: 13 })
              }
            ),
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${p} item ${v + 1} down`,
                disabled: s || v === c.length - 1,
                onClick: () => a(bi(c, v, v + 1)),
                children: /* @__PURE__ */ i.jsx(Hu, { size: 13 })
              }
            ),
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button danger",
                "aria-label": `Remove ${p} item ${v + 1}`,
                disabled: s,
                onClick: () => a(c.filter((S, _) => _ !== v)),
                children: /* @__PURE__ */ i.jsx(uo, { size: 13 })
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
        onClick: () => a([...c, Kv(t)]),
        children: [
          /* @__PURE__ */ i.jsx(bn, { size: 13 }),
          " Add item"
        ]
      }
    )
  ] });
}
function nb({
  input: e,
  value: t,
  syntax: n,
  descriptors: o,
  activity: r,
  expressionEditors: s,
  disabled: a,
  onChange: c,
  onSyntaxChange: u,
  onClose: l
}) {
  const f = Kc(), p = e.displayName || e.name, d = {
    activity: r,
    descriptor: e,
    expressionDescriptors: o,
    readOnly: a,
    surface: "expanded",
    syntax: n
  }, h = md(s, d), g = h?.surfaces.expanded, N = h ? xd(h, d, t) : [], w = g ? null : ib(s, d);
  return Q(() => {
    const m = (E) => {
      E.key === "Escape" && l();
    };
    return window.addEventListener("keydown", m), () => window.removeEventListener("keydown", m);
  }, [l]), /* @__PURE__ */ i.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ i.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": f, children: [
    /* @__PURE__ */ i.jsxs("header", { children: [
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ i.jsx("h3", { id: f, children: p })
      ] }),
      /* @__PURE__ */ i.jsx("button", { type: "button", "aria-label": `Close ${p} editor`, onClick: l, children: /* @__PURE__ */ i.jsx(qu, { size: 16 }) })
    ] }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ i.jsx(
          Wi,
          {
            label: `${p} expression syntax`,
            value: n,
            descriptors: o,
            disabled: a,
            onChange: u
          }
        ),
        /* @__PURE__ */ i.jsx("span", { children: hd(e.typeName) })
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
      Ki(N)
    ] }),
    /* @__PURE__ */ i.jsxs("footer", { children: [
      /* @__PURE__ */ i.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: l, children: "Close" })
    ] })
  ] }) });
}
function Hi(e, t, n, o, r, s) {
  return e ? /* @__PURE__ */ i.jsx(
    e,
    {
      descriptor: t,
      value: n,
      disabled: o,
      context: r,
      onChange: s
    }
  ) : /* @__PURE__ */ i.jsx("input", { type: "text", value: n == null ? "" : String(n), disabled: o, onChange: (a) => s(a.target.value) });
}
function Wi({
  label: e,
  value: t,
  descriptors: n,
  disabled: o,
  variant: r = "block",
  onChange: s
}) {
  const [a, c] = q(!1), u = Kc(), l = n.find((p) => p.type === t), f = [
    "wf-syntax-picker-trigger",
    r === "inline" ? "inline" : "",
    a ? "open" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ i.jsxs("div", { className: r === "inline" ? "wf-syntax-picker inline" : "wf-syntax-picker", onBlur: (p) => {
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
        disabled: o,
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
const Bi = "::";
function yd(e) {
  return !e || e === Nr ? Nr : e;
}
function xc(e, t) {
  return `${yd(t)}${Bi}${e}`;
}
function ob(e) {
  const t = e.indexOf(Bi);
  if (t < 0) return null;
  const n = e.slice(t + Bi.length);
  return n ? { scopeId: e.slice(0, t), referenceKey: n } : null;
}
function rb({ value: e, visibleVariables: t, scopeStatus: n, disabled: o, onChange: r }) {
  const s = ad(e), a = !!e && typeof e == "object" || typeof e == "string" && e.trim().startsWith("{"), c = s && (a || t.some((f) => f.referenceKey === s.referenceKey)) ? s : null, u = c ? xc(c.referenceKey, c.declaringScopeId) : "", l = !!c && t.some(
    (f) => f.referenceKey === c.referenceKey && f.scopeId === yd(c.declaringScopeId)
  );
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-variable-picker", children: [
    /* @__PURE__ */ i.jsxs(
      "select",
      {
        "aria-label": "Variable reference",
        value: u,
        disabled: o,
        onChange: (f) => {
          const p = ob(f.target.value);
          p && r(q0(p.referenceKey, p.scopeId));
        },
        children: [
          /* @__PURE__ */ i.jsx("option", { value: "", children: "Select a variable…" }),
          c && !l ? /* @__PURE__ */ i.jsxs("option", { value: u, children: [
            c.referenceKey,
            " (not visible from this scope)"
          ] }) : null,
          t.map((f) => {
            const p = xc(f.referenceKey, f.scopeId);
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
function Fi(e, t, n) {
  return [...e].sort((o, r) => (o.order ?? 500) - (r.order ?? 500)).find((o) => o.supports(t, n));
}
function md(e, t) {
  return [...e].sort((n, o) => (n.order ?? 500) - (o.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function xd(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function ib(e, t) {
  if (t.syntax.toLowerCase() === "literal") return null;
  const n = [...e].sort((a, c) => (a.order ?? 500) - (c.order ?? 500)).find((a) => a.supports(t) && a.metadata)?.metadata;
  if (!n) return `No enhanced editor is registered for ${t.syntax}. Using the generic text editor.`;
  const o = n.displayName?.trim() || "enhanced editor", r = n.installHint?.trim(), s = `No ${o} is registered for ${t.syntax}. Using the generic text editor.`;
  return r ? `${s} ${r}` : s;
}
function Ki(e) {
  return e.length === 0 ? null : /* @__PURE__ */ i.jsx("div", { className: "wf-expression-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const o = t.severity ?? "info";
    return /* @__PURE__ */ i.jsxs("p", { className: `wf-expression-editor-diagnostic ${o}`, children: [
      t.code ? /* @__PURE__ */ i.jsx("span", { children: t.code }) : null,
      t.message
    ] }, `${t.code ?? "diagnostic"}-${n}`);
  }) });
}
function sb(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "General";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return [...t.entries()].map(([n, o]) => ({ category: n, inputs: o }));
}
function ab(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !gd.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function cb(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !gd.has(t) && n !== "multiline") return !1;
  const o = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(o) || n === "singleline" || n === "multiline";
}
function lb(e, t) {
  return {
    ...t ? { name: t } : {},
    definitionId: e.definitionId,
    state: vo(e.state),
    layout: e.layout
  };
}
function ub(e) {
  return JSON.stringify(
    {
      state: vo(e.state),
      layout: e.layout
    },
    null,
    2
  );
}
function db(e, t) {
  let n;
  try {
    n = JSON.parse(e);
  } catch (r) {
    return { ok: !1, error: r instanceof Error ? r.message : String(r) };
  }
  if (!n || typeof n != "object")
    return { ok: !1, error: "Workflow JSON must be an object with a 'state' property." };
  const o = n;
  return !o.state || typeof o.state != "object" ? { ok: !1, error: "Workflow JSON is missing a valid 'state' object." } : o.layout !== void 0 && !Array.isArray(o.layout) ? { ok: !1, error: "'layout' must be an array when present." } : {
    ok: !0,
    draft: {
      ...t,
      state: Or(o.state),
      layout: o.layout ?? t.layout
    }
  };
}
function fb(e, t) {
  const n = (t ?? "workflow").trim().replace(/[^\w.-]+/g, "-") || "workflow", o = new Blob([JSON.stringify(e, null, 2)], { type: "application/json" }), r = URL.createObjectURL(o), s = document.createElement("a");
  s.href = r, s.download = `${n}.json`, document.body.appendChild(s), s.click(), s.remove(), URL.revokeObjectURL(r);
}
const pb = 320, hb = 140;
function gb(e, t, n) {
  return n === "sequence" ? yb(e) : mb(e, t);
}
function yb(e) {
  const t = /* @__PURE__ */ new Map();
  return e.forEach((n, o) => {
    t.set(n.id, { x: o * 280, y: 0 });
  }), t;
}
function mb(e, t) {
  const n = /* @__PURE__ */ new Map();
  if (e.length === 0) return n;
  const o = new Set(e.map((f) => f.id)), r = t.filter((f) => o.has(f.source) && o.has(f.target)), s = /* @__PURE__ */ new Set();
  for (const f of r)
    s.add(f.source), s.add(f.target);
  const a = /* @__PURE__ */ new Map();
  for (const f of e) a.set(f.id, 0);
  for (let f = 0; f < e.length; f += 1) {
    let p = !1;
    for (const d of r) {
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
      n.set(d, { x: f * pb, y: h * hb });
    });
  return n;
}
const xb = 50;
function wc() {
  return { past: [], future: [] };
}
function wb(e) {
  return e.past.length > 0;
}
function vb(e) {
  return e.future.length > 0;
}
function vc(e, t, n = xb) {
  const o = [...e.past, t];
  return o.length > n && o.splice(0, o.length - n), { past: o, future: [] };
}
function bb(e, t) {
  if (e.past.length === 0) return null;
  const n = e.past.slice(), o = n.pop();
  return { history: { past: n, future: [...e.future, t] }, snapshot: o };
}
function Nb(e, t) {
  if (e.future.length === 0) return null;
  const n = e.future.slice(), o = n.pop();
  return { history: { past: [...e.past, t], future: n }, snapshot: o };
}
function nt(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function wd(e, t) {
  if (!e || !t) return "";
  const n = Date.parse(e), o = Date.parse(t);
  if (Number.isNaN(n) || Number.isNaN(o) || o < n) return "";
  const r = Math.round((o - n) / 1e3);
  if (r < 60) return `${r}s`;
  const s = Math.floor(r / 60), a = r % 60;
  if (s < 60) return a ? `${s}m ${a}s` : `${s}m`;
  const c = Math.floor(s / 60), u = s % 60;
  return u ? `${c}h ${u}m` : `${c}h`;
}
function kr(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function Ss(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ i.jsx(Wu, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ i.jsx(gs, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ i.jsx(Ow, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ i.jsx(vn, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ i.jsx(Fw, { size: 15 });
    default:
      return /* @__PURE__ */ i.jsx(Lr, { size: 15 });
  }
}
function bc({
  document: e,
  readOnly: t,
  minHeight: n,
  ariaLabel: o,
  onChange: r
}) {
  const s = (a) => {
    t || r({ ...e, value: a.target.value });
  };
  return /* @__PURE__ */ i.jsx(
    "textarea",
    {
      "aria-label": o,
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
function jb({
  document: e,
  diagnostics: t = [],
  readOnly: n = !1,
  theme: o = "studio",
  minHeight: r = "220px",
  ariaLabel: s,
  languageAdapter: a,
  onChange: c
}) {
  const u = t.filter((d) => !d.uri || d.uri === e.uri), l = a?.displayName ?? e.language, f = a?.loadEditor, p = de(
    () => f ? yf(f) : null,
    [f]
  );
  return /* @__PURE__ */ i.jsxs(
    "section",
    {
      className: "studio-code-editor",
      "data-language": e.language,
      "data-theme": o,
      "data-readonly": n,
      children: [
        /* @__PURE__ */ i.jsxs("div", { className: "studio-code-editor-header", children: [
          /* @__PURE__ */ i.jsx("span", { children: l }),
          /* @__PURE__ */ i.jsx("code", { children: e.uri })
        ] }),
        p ? /* @__PURE__ */ i.jsx(mf, { fallback: /* @__PURE__ */ i.jsx(
          bc,
          {
            document: e,
            readOnly: n,
            minHeight: r,
            ariaLabel: s,
            onChange: c
          }
        ), children: /* @__PURE__ */ i.jsx(
          p,
          {
            document: e,
            readOnly: n,
            theme: o,
            minHeight: r,
            ariaLabel: s,
            onChange: c
          }
        ) }) : /* @__PURE__ */ i.jsx(
          bc,
          {
            document: e,
            readOnly: n,
            minHeight: r,
            ariaLabel: s,
            onChange: c
          }
        ),
        /* @__PURE__ */ i.jsx(Sb, { diagnostics: u })
      ]
    }
  );
}
function Sb({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ i.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const o = t.severity ?? "info", r = kb(t);
    return /* @__PURE__ */ i.jsxs(
      "p",
      {
        className: `studio-code-editor-diagnostic ${o}`,
        children: [
          t.code ? /* @__PURE__ */ i.jsx("span", { children: t.code }) : null,
          r ? /* @__PURE__ */ i.jsx("small", { children: r }) : null,
          t.message
        ]
      },
      `${t.uri ?? "document"}-${t.code ?? "diagnostic"}-${n}`
    );
  }) });
}
function kb(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const Eb = { language: "json", displayName: "JSON" };
function Cb({ draft: e, onApply: t }) {
  const n = de(() => ub(e), [e]), [o, r] = q(n), [s, a] = q(n), [c, u] = q(null);
  Q(() => {
    r(n), a(n), u(null);
  }, [n]);
  const l = o !== s, f = c ? [{ severity: "error", message: c }] : [], p = () => u(t(o));
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-code-view", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-code-view-toolbar", children: [
      /* @__PURE__ */ i.jsx("span", { className: "wf-muted", children: "Edit the workflow definition as JSON. Changes apply to the draft when you click Apply." }),
      /* @__PURE__ */ i.jsxs("span", { className: "wf-code-view-actions", children: [
        /* @__PURE__ */ i.jsx("button", { type: "button", disabled: !l, onClick: () => {
          r(s), u(null);
        }, children: "Reset" }),
        /* @__PURE__ */ i.jsxs("button", { type: "button", disabled: !l, onClick: p, children: [
          /* @__PURE__ */ i.jsx(jn, { size: 14 }),
          " Apply"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ i.jsx("div", { className: "wf-code-view-editor", children: /* @__PURE__ */ i.jsx(
      jb,
      {
        ariaLabel: "Workflow JSON",
        document: { uri: "elsa://workflows/definition.json", language: "json", value: o },
        languageAdapter: Eb,
        diagnostics: f,
        minHeight: "100%",
        theme: "studio",
        onChange: (d) => {
          r(d.value), c && u(null);
        }
      }
    ) })
  ] });
}
const Ib = ["Single", "Array", "List", "HashSet"];
function vd(e) {
  const [t, n] = q(null), [o, r] = q(null);
  Q(() => {
    let u = !1;
    return wv(e).then(
      (l) => {
        u || n(l);
      },
      () => {
        u || n([]);
      }
    ), bv(e).then(
      (l) => {
        u || r(l);
      },
      () => {
        u || r([]);
      }
    ), () => {
      u = !0;
    };
  }, [e]);
  const s = de(
    () => t && t.length > 0 ? t.map((u) => {
      const l = ic(u);
      return {
        value: l,
        label: Xu(u.displayName, l),
        group: u.category?.trim() || "Other"
      };
    }) : null,
    [t]
  ), a = de(
    () => o && o.length > 0 ? o.filter((u) => !u.deprecated).map((u) => ({
      value: u.typeName,
      label: i0(u.displayName, u.typeName)
    })) : null,
    [o]
  ), c = de(() => {
    const u = /* @__PURE__ */ new Map();
    for (const l of t ?? []) {
      const f = ic(l), p = l.defaultEditor?.trim();
      f && u.set(f, p && p.length > 0 ? p : "text");
    }
    return (l) => u.get(l) ?? "text";
  }, [t]);
  return { typeOptions: s, storageOptions: a, editorForAlias: c };
}
function Ab(e) {
  return !e || e.length === 0 ? void 0 : (e.find((n) => /(^|\.)String$/i.test(n.value) || n.label.toLowerCase() === "string") ?? e[0]).value;
}
function _b(e, t, n) {
  return {
    add: () => {
      const o = Yw(n.namePrefix, e.map((r) => Jn(r, n.nameKeys)));
      t([...e, n.create(o)]);
    },
    update: (o, r) => t(e.map((s, a) => a === o ? n.patch(s, r) : s)),
    remove: (o) => t(e.filter((r, s) => s !== o))
  };
}
function Nc({ value: e, options: t, placeholder: n, allowEmpty: o, ariaLabel: r, onChange: s }) {
  if (!t)
    return /* @__PURE__ */ i.jsx(
      "input",
      {
        type: "text",
        "aria-label": r,
        value: e,
        placeholder: n,
        onChange: (l) => s(l.target.value)
      }
    );
  const a = e === "" || t.some((l) => l.value === e), c = Array.from(new Set(t.map((l) => l.group).filter((l) => !!l))), u = c.length > 0;
  return /* @__PURE__ */ i.jsxs("select", { "aria-label": r, value: e, onChange: (l) => s(l.target.value), children: [
    o ? /* @__PURE__ */ i.jsx("option", { value: "", children: n ?? "—" }) : null,
    a ? null : /* @__PURE__ */ i.jsxs("option", { value: e, disabled: !0, children: [
      e,
      " (unresolved)"
    ] }),
    u ? c.map((l) => /* @__PURE__ */ i.jsx("optgroup", { label: l, children: t.filter((f) => f.group === l).map((f) => /* @__PURE__ */ i.jsx("option", { value: f.value, children: f.label }, f.value)) }, l)) : t.map((l) => /* @__PURE__ */ i.jsx("option", { value: l.value, children: l.label }, l.value))
  ] });
}
const Db = {
  Single: "Single",
  Array: "Array",
  List: "List",
  HashSet: "Hash set"
};
function Mb({ value: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ i.jsx("select", { "aria-label": t, value: e, onChange: (o) => n(o.target.value), children: Ib.map((o) => /* @__PURE__ */ i.jsx("option", { value: o, children: Db[o] }, o)) });
}
function Tb(e, t) {
  return t === "" ? !0 : e === "checkbox" ? t === "true" || t === "false" || t === "True" || t === "False" : e === "number" ? Number.isFinite(Number(t.trim())) && t.trim() !== "" : e === "date" ? /^\d{4}-\d{2}-\d{2}$/.test(t) : !0;
}
function $b({ value: e, editor: t, ariaLabel: n, onChange: o }) {
  const r = Tb(t, e);
  return r && t === "checkbox" ? /* @__PURE__ */ i.jsx(
    "input",
    {
      type: "checkbox",
      "aria-label": n,
      checked: e === "true" || e === "True",
      onChange: (s) => o(s.target.checked ? "true" : "false")
    }
  ) : r && (t === "number" || t === "date") ? /* @__PURE__ */ i.jsx(
    "input",
    {
      type: t,
      "aria-label": n,
      value: e,
      placeholder: "(empty)",
      onChange: (s) => o(s.target.value)
    }
  ) : /* @__PURE__ */ i.jsx(
    "input",
    {
      type: "text",
      "aria-label": n,
      value: e,
      placeholder: "(empty)",
      disabled: t === "none",
      onChange: (s) => o(s.target.value)
    }
  );
}
function Pb({ title: e, addLabel: t, emptyLabel: n, headers: o, isEmpty: r, onAdd: s, children: a }) {
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-properties-section-head", children: [
      /* @__PURE__ */ i.jsx("h3", { children: e }),
      /* @__PURE__ */ i.jsxs("button", { type: "button", className: "wf-properties-add", onClick: s, children: [
        /* @__PURE__ */ i.jsx(bn, { size: 14 }),
        " ",
        t
      ] })
    ] }),
    r ? /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: n }) : /* @__PURE__ */ i.jsxs("table", { className: "wf-properties-table", children: [
      /* @__PURE__ */ i.jsx("thead", { children: /* @__PURE__ */ i.jsxs("tr", { children: [
        o.map((c) => /* @__PURE__ */ i.jsx("th", { children: c }, c)),
        /* @__PURE__ */ i.jsx("th", { "aria-label": "Actions" })
      ] }) }),
      /* @__PURE__ */ i.jsx("tbody", { children: a })
    ] })
  ] });
}
function zb({ label: e, onRemove: t }) {
  return /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx("button", { type: "button", className: "wf-properties-remove", "aria-label": e, title: e, onClick: t, children: /* @__PURE__ */ i.jsx(uo, { size: 14 }) }) });
}
const Rb = {
  read: (e) => Qw(e.default),
  write: (e) => ({ default: Jw(e) })
}, Lb = {
  read: (e) => Jn(e, c0),
  write: (e) => ({ defaultValue: e === "" ? null : e })
};
function ks({
  items: e,
  typeOptions: t,
  storageOptions: n,
  editorForAlias: o,
  namePrefix: r,
  nameKeys: s,
  title: a,
  addLabel: c,
  emptyLabel: u,
  create: l,
  patch: f,
  columns: p,
  defaultAdapter: d,
  warnings: h,
  onChange: g
}) {
  const { add: N, update: w, remove: m } = _b(e, g, {
    namePrefix: r,
    nameKeys: s,
    create: (b) => l(b, Ab(t)),
    patch: f
  }), E = ["Name", "Type", "Collection", ...p.default ? ["Default"] : [], ...p.storage ? ["Storage"] : []], y = r.toLowerCase();
  return /* @__PURE__ */ i.jsx(
    Pb,
    {
      title: a,
      addLabel: c,
      emptyLabel: u,
      headers: E,
      isEmpty: e.length === 0,
      onAdd: N,
      children: e.map((b, j) => {
        const v = Jn(b, s), S = Uu(b), _ = Jn(b, ["referenceKey", "ReferenceKey"]), $ = _ ? h?.get(_) : void 0, V = S.collectionKind === "Single" ? o(S.alias) : "text";
        return /* @__PURE__ */ i.jsxs("tr", { children: [
          /* @__PURE__ */ i.jsxs("td", { children: [
            /* @__PURE__ */ i.jsx("input", { type: "text", "aria-label": `${r} name`, value: v, onChange: (D) => w(j, { name: D.target.value }) }),
            $ ? /* @__PURE__ */ i.jsx("span", { className: "wf-properties-warning", role: "note", title: $, children: $ }) : null
          ] }),
          /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            Nc,
            {
              ariaLabel: `${r} type`,
              value: S.alias,
              options: t,
              placeholder: "Type",
              onChange: (D) => w(j, { type: { alias: D, collectionKind: S.collectionKind } })
            }
          ) }),
          /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            Mb,
            {
              ariaLabel: `${r} collection kind`,
              value: S.collectionKind,
              onChange: (D) => w(j, { type: { alias: S.alias, collectionKind: D } })
            }
          ) }),
          p.default && d ? /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            $b,
            {
              ariaLabel: `${r} default value`,
              value: d.read(b),
              editor: V,
              onChange: (D) => w(j, d.write(D))
            }
          ) }) : null,
          p.storage ? /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            Nc,
            {
              ariaLabel: `${r} storage driver`,
              value: Jn(b, a0),
              options: n,
              placeholder: "—",
              allowEmpty: !0,
              onChange: (D) => w(j, { storageDriverType: D || null })
            }
          ) }) : null,
          /* @__PURE__ */ i.jsx(zb, { label: `Remove ${y} ${v || j + 1}`, onRemove: () => m(j) })
        ] }, j);
      })
    }
  );
}
function bd({ items: e, typeOptions: t, storageOptions: n, editorForAlias: o, title: r = "Variables", addLabel: s = "Add variable", emptyLabel: a = "No variables defined.", warnings: c, onChange: u }) {
  return /* @__PURE__ */ i.jsx(
    ks,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: o,
      namePrefix: "Variable",
      nameKeys: s0,
      title: r,
      addLabel: s,
      emptyLabel: a,
      create: (l, f) => Zw({ name: l, alias: f }),
      patch: (l, f) => Gw(l, f),
      columns: { default: !0, storage: !0 },
      defaultAdapter: Rb,
      warnings: c,
      onChange: u
    }
  );
}
function Vb({ items: e, typeOptions: t, storageOptions: n, editorForAlias: o, onChange: r }) {
  return /* @__PURE__ */ i.jsx(
    ks,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: o,
      namePrefix: "Input",
      nameKeys: Yu,
      title: "Inputs",
      addLabel: "Add input",
      emptyLabel: "No inputs defined.",
      create: (s, a) => e0({ name: s, alias: a }),
      patch: (s, a) => t0(s, a),
      columns: { default: !0, storage: !0 },
      defaultAdapter: Lb,
      onChange: r
    }
  );
}
function Ob({ items: e, typeOptions: t, storageOptions: n, editorForAlias: o, onChange: r }) {
  return /* @__PURE__ */ i.jsx(
    ks,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: o,
      namePrefix: "Output",
      nameKeys: Yu,
      title: "Outputs",
      addLabel: "Add output",
      emptyLabel: "No outputs defined.",
      create: (s, a) => n0({ name: s, alias: a }),
      patch: (s, a) => o0(s, a),
      columns: { default: !1, storage: !1 },
      onChange: r
    }
  );
}
function nr(e) {
  return (e ?? []).filter(wr);
}
function Hb({ context: e, variables: t, title: n, addLabel: o, emptyLabel: r, warnings: s, onChange: a }) {
  const { typeOptions: c, storageOptions: u, editorForAlias: l } = vd(e);
  return /* @__PURE__ */ i.jsx(
    bd,
    {
      items: nr(t),
      typeOptions: c,
      storageOptions: u,
      editorForAlias: l,
      title: n,
      addLabel: o,
      emptyLabel: r,
      warnings: s,
      onChange: a
    }
  );
}
function Wb({ definition: e, definitionId: t, onMetaChange: n }) {
  const o = !!n, [r, s] = q(e?.name ?? ""), [a, c] = q(e?.description ?? "");
  Q(() => {
    s(e?.name ?? "");
  }, [e?.name]), Q(() => {
    c(e?.description ?? "");
  }, [e?.description]);
  const u = () => {
    const f = r.trim();
    f && f !== (e?.name ?? "") ? n?.({ name: f }) : f || s(e?.name ?? "");
  }, l = () => {
    a !== (e?.description ?? "") && n?.({ description: a });
  };
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ i.jsx("h3", { children: "Information" }),
    /* @__PURE__ */ i.jsxs("dl", { className: "wf-properties-info", children: [
      /* @__PURE__ */ i.jsx("dt", { children: /* @__PURE__ */ i.jsx("label", { htmlFor: "wf-def-name", children: "Name" }) }),
      /* @__PURE__ */ i.jsx("dd", { children: o ? /* @__PURE__ */ i.jsx(
        "input",
        {
          id: "wf-def-name",
          type: "text",
          "aria-label": "Workflow name",
          value: r,
          onChange: (f) => s(f.target.value),
          onBlur: u
        }
      ) : e?.name ?? "—" }),
      /* @__PURE__ */ i.jsx("dt", { children: /* @__PURE__ */ i.jsx("label", { htmlFor: "wf-def-description", children: "Description" }) }),
      /* @__PURE__ */ i.jsx("dd", { children: o ? /* @__PURE__ */ i.jsx(
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
function Bb({ details: e, draft: t, context: n, onStateChange: o, onDefinitionMetaChange: r }) {
  const { typeOptions: s, storageOptions: a, editorForAlias: c } = vd(n), u = nr(t.state.variables), l = nr(t.state.inputs), f = nr(t.state.outputs), p = e?.versions ?? [];
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-properties-view", children: [
    /* @__PURE__ */ i.jsx(
      Wb,
      {
        definition: e?.definition,
        definitionId: t.definitionId,
        onMetaChange: r
      }
    ),
    /* @__PURE__ */ i.jsx(
      bd,
      {
        items: u,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (d) => o((h) => ({ ...h, variables: d }))
      }
    ),
    /* @__PURE__ */ i.jsx(
      Vb,
      {
        items: l,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (d) => o((h) => ({ ...h, inputs: d }))
      }
    ),
    /* @__PURE__ */ i.jsx(
      Ob,
      {
        items: f,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (d) => o((h) => ({ ...h, outputs: d }))
      }
    ),
    /* @__PURE__ */ i.jsxs("section", { className: "wf-properties-section", children: [
      /* @__PURE__ */ i.jsx("h3", { children: "Versions" }),
      p.length === 0 ? /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "No published versions yet." }) : /* @__PURE__ */ i.jsx("ul", { className: "wf-properties-versions", children: p.map((d) => /* @__PURE__ */ i.jsxs("li", { children: [
        /* @__PURE__ */ i.jsxs("span", { className: "wf-properties-version-tag", children: [
          "v",
          d.version
        ] }),
        /* @__PURE__ */ i.jsx("time", { children: nt(d.createdAt) })
      ] }, d.id)) })
    ] })
  ] });
}
const Fb = "Expressions/UnresolvedVariable";
function Kb(e) {
  return String(e.type ?? e.code ?? "");
}
function qb(e) {
  return Kb(e) === Fb;
}
function Ub(e) {
  const t = String(e ?? "").trim().split("/").filter(Boolean), [n, o, ...r] = t, s = n === "$workflow";
  return {
    nodeId: !n || s ? null : n,
    isWorkflowScope: s,
    bag: o === "inputs" || o === "outputs" || o === "variables" ? o : null,
    referenceKey: r.length > 0 ? r.join("/") : null
  };
}
function Xb(e) {
  return (e ?? []).filter(qb).map((t) => ({
    error: t,
    path: Ub(t.path),
    message: t.message ?? "Variable reference is not visible from this activity's scope."
  }));
}
function Yb({ activities: e, activityCatalog: t, selectedEvidenceId: n = null, onSelectEvidence: o }) {
  const r = de(
    () => new Map(t.map((a) => [a.activityTypeKey, a])),
    [t]
  ), s = de(() => Gb(e), [e]);
  return s.length === 0 ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty wf-timeline-empty", children: "No activity executions recorded yet." }) : /* @__PURE__ */ i.jsx("ol", { className: "wf-timeline", "aria-label": "Execution timeline", children: s.map((a) => {
    const c = r.get(a.activityType), u = vr(c), l = c ? Ie(c) : kr(a.activityType) ?? a.activityType, f = kr(a.activityType) ?? a.activityType, p = Jb(a.startedAt ?? a.scheduledAt), d = wd(a.startedAt, a.completedAt);
    return /* @__PURE__ */ i.jsx("li", { children: /* @__PURE__ */ i.jsxs(
      "button",
      {
        type: "button",
        className: "wf-timeline-entry",
        "data-selected": a.activityExecutionId === n,
        onClick: () => o?.(a.activityExecutionId),
        children: [
          /* @__PURE__ */ i.jsx("span", { className: "wf-timeline-icon wf-activity-icon", "data-icon": u, "aria-hidden": "true", children: Ss(u) }),
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
          /* @__PURE__ */ i.jsx(Zb, { status: a.status })
        ]
      }
    ) }, a.activityExecutionId);
  }) });
}
function Zb({ status: e }) {
  const t = e.toLowerCase(), n = t === "completed" ? "OK" : e;
  return /* @__PURE__ */ i.jsx("span", { className: "wf-status-badge wf-timeline-pill", "data-status": t, children: n });
}
function Gb(e) {
  return e.map((t, n) => ({ activity: t, index: n })).sort((t, n) => jc(t.activity) - jc(n.activity) || t.index - n.index).map((t) => t.activity);
}
function jc(e) {
  const t = e.startedAt ?? e.scheduledAt, n = t ? Date.parse(t) : Number.NaN;
  return Number.isNaN(n) ? Number.MAX_SAFE_INTEGER : n;
}
function Jb(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? "" : t.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: !1 });
}
function Qb({ context: e }) {
  const t = Z0(e), n = G0(e), o = J0(e), r = t.data ?? null, s = n.data ?? null, a = t.isLoading || n.isLoading, c = o.isPending, [u, l] = q(() => pc(r)), [f, p] = q(""), [d, h] = q(null);
  Q(() => {
    l(pc(r));
  }, [r]);
  const g = de(() => Dv(s), [s]), N = de(() => Mv(s), [s]), w = s?.sets ?? [], m = de(() => {
    const A = f.trim().toLowerCase();
    return A ? g.filter(
      (L) => Sr(L).toLowerCase().includes(A) || (L.activityTypeKey ?? "").toLowerCase().includes(A)
    ) : g;
  }, [g, f]), E = new Set(u.activityTypes), y = new Set(u.sets), b = g.filter((A) => Rt(A.state) === "BlockedByHostBaseline").length, j = g.filter((A) => Rt(A.state) === "HiddenByManagementSettings").length, v = o.error ?? t.error ?? n.error, S = v instanceof Error ? v.message : v ? "Activity availability could not be loaded." : null, _ = (A) => l((L) => ({ ...L, mode: A })), $ = (A) => l((L) => ({ ...L, activityTypes: hc(L.activityTypes, A) })), V = (A) => l((L) => ({ ...L, sets: hc(L.sets, A) })), D = () => {
    h(null), o.mutate(
      {
        scope: r?.scope ?? "host-default",
        mode: Av(u.mode),
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
          /* @__PURE__ */ i.jsx(Rw, { size: 18 }),
          " Activity availability"
        ] }),
        /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "Control which activities can be added to new workflows. Host baseline rules always take precedence." })
      ] }),
      /* @__PURE__ */ i.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ i.jsxs("button", { type: "button", className: "availability-save", onClick: D, disabled: a || c, children: [
        /* @__PURE__ */ i.jsx(Ku, { size: 15 }),
        c ? "Saving…" : "Save"
      ] }) })
    ] }),
    /* @__PURE__ */ i.jsxs("div", { className: "availability-body", children: [
      S && /* @__PURE__ */ i.jsx("div", { className: "availability-banner availability-banner-error", children: S }),
      d && !S && /* @__PURE__ */ i.jsx("div", { className: "availability-banner availability-banner-success", children: d }),
      /* @__PURE__ */ i.jsxs("div", { className: "availability-mode", role: "group", "aria-label": "Activity availability mode", children: [
        /* @__PURE__ */ i.jsxs("button", { type: "button", className: u.mode === "AllExcept" ? "active" : "", onClick: () => _("AllExcept"), disabled: a || c, children: [
          /* @__PURE__ */ i.jsx(tc, { size: 15 }),
          /* @__PURE__ */ i.jsxs("span", { children: [
            /* @__PURE__ */ i.jsx("strong", { children: "All except" }),
            /* @__PURE__ */ i.jsx("em", { children: "Show everything except the selected activities" })
          ] })
        ] }),
        /* @__PURE__ */ i.jsxs("button", { type: "button", className: u.mode === "Only" ? "active" : "", onClick: () => _("Only"), disabled: a || c, children: [
          /* @__PURE__ */ i.jsx(oc, { size: 15 }),
          /* @__PURE__ */ i.jsxs("span", { children: [
            /* @__PURE__ */ i.jsx("strong", { children: "Only" }),
            /* @__PURE__ */ i.jsx("em", { children: "Show only the selected activities" })
          ] })
        ] })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { className: "availability-counts", children: [
        /* @__PURE__ */ i.jsxs("span", { children: [
          /* @__PURE__ */ i.jsx(oc, { size: 14 }),
          " ",
          b,
          " host blocked"
        ] }),
        /* @__PURE__ */ i.jsxs("span", { children: [
          /* @__PURE__ */ i.jsx(tc, { size: 14 }),
          " ",
          j,
          " management hidden"
        ] }),
        /* @__PURE__ */ i.jsxs("span", { children: [
          /* @__PURE__ */ i.jsx(xr, { size: 14 }),
          " ",
          N.length,
          " unresolved"
        ] })
      ] }),
      w.length > 0 && /* @__PURE__ */ i.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ i.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ i.jsx(ms, { size: 14 }),
          " Sets"
        ] }),
        /* @__PURE__ */ i.jsx("div", { className: "availability-set-list", children: w.map((A) => /* @__PURE__ */ i.jsxs("label", { className: "availability-set-option", children: [
          /* @__PURE__ */ i.jsx("input", { type: "checkbox", checked: y.has(A.name), disabled: a || c, onChange: () => V(A.name) }),
          /* @__PURE__ */ i.jsx("span", { children: A.name }),
          /* @__PURE__ */ i.jsx("code", { children: (A.activityTypeKeys ?? []).length })
        ] }, A.name)) })
      ] }),
      /* @__PURE__ */ i.jsxs("section", { className: "availability-section availability-section-grow", children: [
        /* @__PURE__ */ i.jsxs("div", { className: "availability-section-head", children: [
          /* @__PURE__ */ i.jsxs("h3", { className: "wf-section-label", children: [
            /* @__PURE__ */ i.jsx(Mw, { size: 14 }),
            " Activities"
          ] }),
          /* @__PURE__ */ i.jsxs("div", { className: "wf-search availability-search", children: [
            /* @__PURE__ */ i.jsx(Vr, { size: 14 }),
            /* @__PURE__ */ i.jsx("input", { type: "search", value: f, placeholder: "Filter activities…", onChange: (A) => p(A.target.value) })
          ] })
        ] }),
        /* @__PURE__ */ i.jsxs("div", { className: "availability-activity-list", children: [
          a && g.length === 0 && /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "Loading availability…" }),
          !a && g.length === 0 && /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "No availability diagnostics reported." }),
          !a && g.length > 0 && m.length === 0 && /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "No activities match the filter." }),
          m.map((A) => {
            const k = Rt(A.state) === "BlockedByHostBaseline", M = A.activityTypeKey ?? A.activityDefinitionId ?? "";
            return /* @__PURE__ */ i.jsxs("label", { className: `availability-activity-option ${k ? "disabled" : ""}`, children: [
              /* @__PURE__ */ i.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: E.has(M),
                  disabled: a || c || k,
                  onChange: () => $(M)
                }
              ),
              /* @__PURE__ */ i.jsxs("span", { className: "availability-activity-main", children: [
                /* @__PURE__ */ i.jsx("strong", { children: Sr(A) }),
                /* @__PURE__ */ i.jsx("code", { children: A.activityTypeKey })
              ] }),
              /* @__PURE__ */ i.jsx("em", { className: `availability-state ${Ev(A.state)}`, children: jr(A.state) })
            ] }, M);
          })
        ] })
      ] }),
      N.length > 0 && /* @__PURE__ */ i.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ i.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ i.jsx(xr, { size: 14 }),
          " Unresolved references"
        ] }),
        /* @__PURE__ */ i.jsx("div", { className: "availability-unresolved-list", children: N.map((A) => /* @__PURE__ */ i.jsxs("span", { children: [
          /* @__PURE__ */ i.jsx("strong", { children: A.referenceName }),
          /* @__PURE__ */ i.jsx("em", { children: jr(A.state) })
        ] }, `${A.layer}-${A.referenceKind}-${A.referenceName}`)) })
      ] })
    ] })
  ] });
}
const Sc = "elsa-studio:apply-workflow-graph-operation-batch", kc = "elsa-studio:undo-workflow-graph-operation-batch", e1 = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function t1(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const o = l1(e), r = jd(o.state.rootActivity), s = /* @__PURE__ */ new Map(), a = /* @__PURE__ */ new Map(), c = [];
  for (const u of t.operations) {
    const l = c1(u.kind), f = u.parameters ?? {};
    if (l === "add-activity") {
      const p = Oe(f.activityId) ?? u.temporaryReferences?.[0], d = a1(p ?? Oe(f.displayName) ?? Oe(f.activityType) ?? "weaver-activity", r), h = n1(u, d, n);
      a.set(d, h), c.push(d), p && s.set(p, d), o.state.rootActivity && o1(o.state.rootActivity, h);
      const g = St(f.position) ? qi(f.position, { x: 280, y: 160 }) : null;
      g && (o.layout = Ec(o.layout, d, g));
      continue;
    }
    if (l === "set-root") {
      const p = Ni(o, f.activityId, s, a);
      if (!p) throw new Error("Weaver batch referenced an unknown root activity.");
      o.state.rootActivity = p;
      continue;
    }
    if (l === "set-designer-position") {
      const p = Bt(f.activityId, s);
      if (!p || !Es(o.state.rootActivity, p)) throw new Error("Weaver batch referenced an unknown activity position.");
      o.layout = Ec(o.layout, p, qi(f, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const p = Ni(o, f.activityId, s, a);
      if (!p) throw new Error("Weaver batch referenced an unknown activity property target.");
      s1(p, Oe(f.propertyName) ?? "Value", f.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const p = Ni(o, f.activityId, s, a);
      if (!p) throw new Error("Weaver batch referenced an unknown activity update target.");
      const d = St(f.patch) ? f.patch : f;
      Object.assign(p, d);
      continue;
    }
    if (l === "remove-activity") {
      const p = Bt(f.activityId, s);
      if (!p) throw new Error("Weaver batch referenced an unknown activity remove target.");
      o.state.rootActivity = Nd(o.state.rootActivity, p), o.layout = o.layout.filter((d) => d.nodeId !== p);
      continue;
    }
    if (l === "connect-activities") {
      r1(o, f, s);
      continue;
    }
    if (l === "disconnect-activities") {
      i1(o, f, s);
      continue;
    }
    throw new Error(`Weaver batch operation '${String(u.kind || "unknown")}' is not supported by this designer apply path.`);
  }
  if (!o.state.rootActivity) throw new Error("Weaver batch did not produce a root activity.");
  return o.sourceVersionId = null, {
    draft: o,
    appliedCount: t.operations.length,
    finalActivityIds: c,
    temporaryReferences: Object.fromEntries(s),
    summary: `Applied ${t.operations.length} workflow operation${t.operations.length === 1 ? "" : "s"} to the working draft.`
  };
}
function n1(e, t, n) {
  const o = e.parameters ?? {}, r = Oe(o.activityVersionId) ?? Oe(o.activityType) ?? "Elsa.Workflows.Activity", s = n.find((a) => a.activityVersionId === r || a.activityTypeKey === r || a.displayName === Oe(o.displayName));
  return s ? Oi(s, t) : {
    nodeId: t,
    activityVersionId: s?.activityVersionId ?? r,
    inputs: [],
    outputs: [],
    ...Oe(o.displayName) ? { displayName: Oe(o.displayName) } : {},
    designer: { position: qi(o.position, { x: 280, y: 160 }) }
  };
}
function o1(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = Cs(e);
  n && !n.some((o) => o.nodeId === t.nodeId) && n.push(t);
}
function r1(e, t, n) {
  const o = e.state.rootActivity;
  if (!o?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const r = Bt(t.sourceActivityId ?? t.sourceId ?? t.from, n), s = Bt(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!r || !s) throw new Error("Weaver batch connection is missing source or target activity.");
  const a = o.structure.payload, c = Array.isArray(a.connections) ? a.connections : [], u = Oe(t.connectionId) ?? `flow-${r}-${s}`;
  a.connections = [
    ...c.filter((l) => !St(l) || l.id !== u),
    {
      id: u,
      source: { nodeId: r, port: Oe(t.outcome) ?? Oe(t.sourcePort) ?? "Done" },
      target: { nodeId: s }
    }
  ];
}
function i1(e, t, n) {
  const o = e.state.rootActivity, r = o?.structure?.payload.connections;
  if (!Array.isArray(r)) return;
  const s = Oe(t.connectionId), a = Bt(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = Bt(t.targetActivityId ?? t.targetId ?? t.to, n);
  o.structure.payload.connections = r.filter((u) => {
    if (!St(u)) return !0;
    if (s && u.id === s) return !1;
    const l = St(u.source) ? u.source.nodeId : void 0, f = St(u.target) ? u.target.nodeId : void 0;
    return l !== a || f !== c;
  });
}
function s1(e, t, n) {
  const o = St(n);
  e[dd(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: o ? "Object" : "Literal", value: n }
  };
}
function Ni(e, t, n, o) {
  const r = Bt(t, n);
  return r ? Es(e.state.rootActivity, r) ?? o.get(r) ?? null : null;
}
function Bt(e, t) {
  const n = Oe(e);
  return n ? t.get(n) ?? n : null;
}
function Es(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of Sd(e)) {
    const o = Es(n, t);
    if (o) return o;
  }
  return null;
}
function Nd(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = Cs(e);
  if (n) {
    const o = n.map((r) => Nd(r, t)).filter((r) => !!r);
    n.splice(0, n.length, ...o);
  }
  return e;
}
function jd(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of Sd(e)) jd(n, t);
  return t;
}
function Sd(e) {
  return Cs(e) ?? [];
}
function Cs(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function Ec(e, t, n) {
  return [
    ...e.filter((o) => o.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function qi(e, t) {
  const n = St(e) ? e : {}, o = Number(n.x), r = Number(n.y);
  return {
    x: Number.isFinite(o) ? Math.max(40, Math.round(o)) : t.x,
    y: Number.isFinite(r) ? Math.max(40, Math.round(r)) : t.y
  };
}
function a1(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let o = n, r = 2;
  for (; t.has(o); )
    o = `${n}-${r}`, r += 1;
  return t.add(o), o;
}
function c1(e) {
  return typeof e == "number" ? e1[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Oe(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function l1(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function St(e) {
  return typeof e == "object" && e !== null;
}
function Is({ rows: e = 5 }) {
  return /* @__PURE__ */ i.jsx("div", { className: "wf-grid", "aria-busy": "true", "aria-label": "Loading", children: Array.from({ length: e }).map((t, n) => /* @__PURE__ */ i.jsx("div", { className: "wf-skeleton wf-skeleton-row", style: { width: `${90 - n % 3 * 12}%` } }, n)) });
}
function As({ icon: e, title: t, description: n, action: o }) {
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-empty-state", role: "status", children: [
    /* @__PURE__ */ i.jsx("div", { className: "wf-empty-state-icon", "aria-hidden": !0, children: e ?? /* @__PURE__ */ i.jsx(Lr, { size: 22 }) }),
    /* @__PURE__ */ i.jsx("h3", { children: t }),
    n ? /* @__PURE__ */ i.jsx("p", { children: n }) : null,
    o ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty-state-action", children: o }) : null
  ] });
}
function No({ message: e, title: t = "Something went wrong" }) {
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-error-card", role: "alert", children: [
    /* @__PURE__ */ i.jsx(kt, { size: 18 }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-error-card-body", children: [
      /* @__PURE__ */ i.jsx("strong", { children: t }),
      /* @__PURE__ */ i.jsx("span", { children: e || "Please try again, or check that the Elsa server is reachable." })
    ] })
  ] });
}
const kd = { workflowActivity: eN }, Ed = { workflow: nN }, Cc = "application/x-elsa-activity-version-id", u1 = 6, d1 = 1200, f1 = 250, p1 = [10, 25, 50], h1 = 10, Ic = "elsa-studio-workflow-palette-width", Ac = "elsa-studio-workflow-inspector-width", _c = "elsa-studio-workflow-palette-collapsed", Dc = "elsa-studio-workflow-inspector-collapsed", Cd = "elsa-studio-workflow-side-panel-maximized", Hn = 180, Wn = 460, g1 = 260, Bn = 260, Fn = 560, y1 = 320, Mc = 42, qo = 16, m1 = [
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
], Id = st.createContext(null), Ad = st.createContext(null);
let Ui;
function mN(e) {
  Ui = e.dialogs, e.featureAreas.add({
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
        component: () => /* @__PURE__ */ i.jsx(x1, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list() })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ i.jsx(w1, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => /* @__PURE__ */ i.jsx(v1, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => /* @__PURE__ */ i.jsx(b1, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => /* @__PURE__ */ i.jsx(Qb, { context: e.backend })
      }
    ]
  });
}
function x1({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: o,
  workflowDesignerPanels: r
}) {
  const [s, a] = q(Tc);
  Q(() => {
    const u = () => a(Tc());
    return window.addEventListener("popstate", u), () => window.removeEventListener("popstate", u);
  }, []);
  const c = (u) => {
    const l = u ? `/workflows/definitions?definition=${encodeURIComponent(u)}` : "/workflows/definitions";
    window.history.pushState({}, "", l), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return s ? /* @__PURE__ */ i.jsx(Q1, { context: e, definitionId: s, ai: t, propertyEditors: n, expressionEditors: o, workflowDesignerPanels: r, onBack: () => c(null) }) : /* @__PURE__ */ i.jsx(Wr, { title: "Definitions", children: /* @__PURE__ */ i.jsx(j1, { context: e, ai: t, onOpen: c }) });
}
function w1({ context: e, ai: t }) {
  const [n, o] = q($c);
  Q(() => {
    const s = () => o($c());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const r = le((s) => {
    const a = s?.trim() ?? "", c = new URL(window.location.href);
    a ? c.searchParams.set("definition", a) : c.searchParams.delete("definition"), o(a || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ i.jsx(Wr, { title: "Executables", children: /* @__PURE__ */ i.jsx(k1, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: r }) });
}
function v1({ context: e, ai: t }) {
  return /* @__PURE__ */ i.jsx(Wr, { title: "Runs", children: /* @__PURE__ */ i.jsx(I1, { context: e, ai: t }) });
}
function b1({ context: e, ai: t }) {
  const n = N1();
  return /* @__PURE__ */ i.jsx(Wr, { title: "Run", children: /* @__PURE__ */ i.jsx(A1, { context: e, ai: t, workflowExecutionId: n }) });
}
function Wr({ title: e, children: t }) {
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ i.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ i.jsxs("div", { children: [
      /* @__PURE__ */ i.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ i.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function Tc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function $c() {
  return new URLSearchParams(window.location.search).get("definition");
}
function N1() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function j1({ context: e, ai: t, onOpen: n }) {
  const [o, r] = q(""), [s, a] = q("active"), [c, u] = q(1), [l, f] = q(h1), [p, d] = q("loading"), [h, g] = q(""), [N, w] = q(""), [m, E] = q([]), [y, b] = q(0), [j, v] = q(() => /* @__PURE__ */ new Set()), [S, _] = q(null), [$, V] = q(!1), [D, A] = q([]), [L, k] = q("idle"), M = ie(null), I = de(() => m.map((W) => W.id), [m]), T = Ft(t, "weaver.workflows.suggest-create-metadata"), z = Ft(t, "weaver.workflows.explain-definition"), P = I.filter((W) => j.has(W)).length, K = I.length > 0 && P === I.length, B = le(async () => {
    d("loading"), g("");
    try {
      const W = await Q0(e, { search: o, state: s, page: c, pageSize: l }), ee = typeof W.totalCount == "number", me = W.totalCount ?? W.definitions.length, ve = Md(me, l);
      if (me > 0 && c > ve) {
        u(ve);
        return;
      }
      E(ee ? W.definitions : R1(W.definitions, c, l)), b(me), d("ready");
    } catch (W) {
      g(W instanceof Error ? W.message : String(W)), d("failed");
    }
  }, [e, o, s, c, l]);
  Q(() => {
    B();
  }, [B]), Q(() => {
    M.current && (M.current.indeterminate = P > 0 && !K);
  }, [K, P]);
  const F = le(async () => {
    if (!(L === "loading" || L === "ready")) {
      k("loading");
      try {
        const W = await bs(e);
        A(W.activities ?? []), k("ready");
      } catch (W) {
        k("failed"), g(W instanceof Error ? W.message : String(W));
      }
    }
  }, [L, e]), X = () => {
    g(""), w(""), _({ name: "", description: "", rootKind: "flowchart" }), F();
  }, J = async () => {
    if (S?.name.trim()) {
      V(!0), g(""), w("");
      try {
        const W = await rv(e, {
          name: S.name.trim(),
          description: S.description.trim() || null,
          rootKind: S.rootKind,
          rootActivityVersionId: V1(S, D)
        });
        _(null), n(W.definition.id);
      } catch (W) {
        g(W instanceof Error ? W.message : String(W));
      } finally {
        V(!1);
      }
    }
  }, U = (W) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(W)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, ce = async () => {
    if (m.length === 1 && c > 1) {
      u(c - 1);
      return;
    }
    await B();
  }, G = () => v(/* @__PURE__ */ new Set()), O = (W, ee) => {
    v((me) => {
      const ve = new Set(me);
      return ee ? ve.add(W) : ve.delete(W), ve;
    });
  }, Y = (W) => {
    v((ee) => {
      const me = new Set(ee);
      for (const ve of I)
        W ? me.add(ve) : me.delete(ve);
      return me;
    });
  }, se = (W) => {
    a(W), u(1), G();
  }, ae = (W) => {
    r(W), u(1), G();
  }, te = async (W) => {
    if (await Ui.confirm({ message: `Delete workflow definition "${W.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" })) {
      w(""), g("");
      try {
        await iv(e, W.id), O(W.id, !1), w(`Deleted ${W.name}`), await ce();
      } catch (ee) {
        g(ee instanceof Error ? ee.message : String(ee));
      }
    }
  }, oe = async (W) => {
    w(""), g("");
    try {
      await sv(e, W.id), O(W.id, !1), w(`Restored ${W.name}`), await ce();
    } catch (ee) {
      g(ee instanceof Error ? ee.message : String(ee));
    }
  }, fe = async (W) => {
    if (await Ui.confirm({ message: `Permanently delete workflow definition "${W.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) {
      w(""), g("");
      try {
        await av(e, W.id), O(W.id, !1), w(`Permanently deleted ${W.name}`), await ce();
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
        /* @__PURE__ */ i.jsx(Vr, { size: 15 }),
        /* @__PURE__ */ i.jsx("input", { value: o, onChange: (W) => ae(W.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => {
        B();
      }, children: "Refresh" }),
      /* @__PURE__ */ i.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ i.jsxs("button", { type: "button", title: "Create workflow", onClick: X, children: [
        /* @__PURE__ */ i.jsx(bn, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    p === "failed" ? /* @__PURE__ */ i.jsx(No, { message: h, title: "Couldn't load workflow definitions" }) : null,
    p !== "failed" && h ? /* @__PURE__ */ i.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ i.jsx(kt, { size: 16 }),
      " ",
      h
    ] }) : null,
    N ? /* @__PURE__ */ i.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ i.jsx(jn, { size: 14 }),
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
    p === "loading" ? /* @__PURE__ */ i.jsx(Is, {}) : null,
    p === "ready" && m.length === 0 ? /* @__PURE__ */ i.jsx(
      As,
      {
        icon: /* @__PURE__ */ i.jsx(Fu, { size: 22 }),
        title: `No ${s} workflow definitions`,
        description: "Create a workflow to start designing automation, or adjust your filters to see more.",
        action: /* @__PURE__ */ i.jsxs("button", { type: "button", className: "wf-link-button", onClick: X, children: [
          /* @__PURE__ */ i.jsx(bn, { size: 15 }),
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
              ref: M,
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
              /* @__PURE__ */ i.jsx("span", { children: s === "deleted" ? nt(W.deletedAt) : W.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ i.jsx("span", { children: nt(W.lastModifiedAt) }),
              /* @__PURE__ */ i.jsx("span", { className: "wf-row-actions", onClick: (ee) => ee.stopPropagation(), children: s === "active" ? /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
                /* @__PURE__ */ i.jsx("button", { type: "button", onClick: (ee) => {
                  ee.stopPropagation(), n(W.id);
                }, children: "Open" }),
                /* @__PURE__ */ i.jsx("button", { type: "button", onClick: (ee) => {
                  ee.stopPropagation(), U(W.id);
                }, children: "Artifacts" }),
                z ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => Kt(t, z, W), children: [
                  /* @__PURE__ */ i.jsx(gt, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ i.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  te(W);
                }, children: [
                  /* @__PURE__ */ i.jsx(uo, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
                /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => {
                  oe(W);
                }, children: [
                  /* @__PURE__ */ i.jsx(ys, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ i.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  fe(W);
                }, children: [
                  /* @__PURE__ */ i.jsx(uo, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          W.id
        ))
      ] }),
      /* @__PURE__ */ i.jsx(
        z1,
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
      S1,
      {
        draft: S,
        creating: $,
        ai: t,
        suggestMetadataAction: T,
        onChange: (W) => _(W),
        onClose: () => _(null),
        onSubmit: J
      }
    ) : null
  ] });
}
function S1({ draft: e, creating: t, ai: n, suggestMetadataAction: o, onChange: r, onClose: s, onSubmit: a }) {
  const [c, u] = q(!1), [l, f] = q(""), [p, d] = q(!1), [h, g] = q(null), [N, w] = q(null), m = ie(null), E = ie(e);
  E.current = e;
  const y = ie(r);
  y.current = r;
  const b = le((v) => {
    const S = { ...E.current };
    v.name && (S.name = v.name), v.description && (S.description = v.description), y.current(S), g(null), w(null);
  }, []);
  Q(() => {
    if (o)
      return n.onPromptResult((v) => {
        if (v.requestId !== m.current) return;
        if (m.current = null, d(!1), v.status !== "completed") {
          w(v.status === "cancelled" ? "Weaver needs more detail — continue in the assistant panel." : "Weaver couldn't generate a suggestion. Try again or fill the fields manually.");
          return;
        }
        const S = L1(v.text);
        if (!S) {
          w("Couldn't read a suggestion from Weaver's reply. See the assistant panel.");
          return;
        }
        v.autoApply ? b(S) : g(S);
      });
  }, [n, o, b]);
  const j = () => {
    if (!o) return;
    const v = o.createPrompt({ draft: E.current, intent: l });
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
          o ? /* @__PURE__ */ i.jsxs(
            "button",
            {
              type: "button",
              className: "wf-ai-action",
              "aria-expanded": c,
              onClick: () => u((v) => !v),
              title: o.description ?? o.label,
              children: [
                /* @__PURE__ */ i.jsx(gt, { size: 13 }),
                " ",
                o.label
              ]
            }
          ) : null
        ] }),
        o && c ? /* @__PURE__ */ i.jsxs("div", { className: "wf-ai-suggest", role: "group", "aria-label": "Suggest name and description", children: [
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
            /* @__PURE__ */ i.jsx(gt, { size: 13 }),
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
              onChange: (v) => r({ ...e, name: v.target.value })
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
              onChange: (v) => r({ ...e, description: v.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ i.jsxs("fieldset", { className: "wf-form-field wf-root-field", children: [
          /* @__PURE__ */ i.jsx("legend", { children: "Root activity" }),
          /* @__PURE__ */ i.jsx("div", { className: "wf-root-cards", role: "radiogroup", "aria-label": "Root activity", children: m1.map((v) => {
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
                  onChange: () => r({ ...e, rootKind: v.value, rootActivityVersionId: null })
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
function k1({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: o }) {
  const [r, s] = q("loading"), [a, c] = q(""), [u, l] = q(""), [f, p] = q(null), [d, h] = q([]), g = n?.trim().toLowerCase() ?? "", N = de(
    () => g ? d.filter((S) => K1(S, g)) : d,
    [g, d]
  ), w = de(
    () => Array.from(new Set(d.flatMap((S) => [
      S.definitionId,
      S.definitionVersionId,
      S.sourceId
    ]).filter((S) => !!S))).sort((S, _) => S.localeCompare(_)),
    [d]
  ), m = Ft(t, "weaver.workflows.explain-executable"), E = le(async () => {
    s("loading"), c("");
    try {
      h(await ld(e)), s("ready");
    } catch (S) {
      c(S instanceof Error ? S.message : String(S)), s("failed");
    }
  }, [e]);
  Q(() => {
    E();
  }, [E]);
  const y = async (S) => {
    l(""), p(null), c("");
    try {
      const _ = await cd(e, S.artifactId), $ = Pd(_);
      p({ artifactId: S.artifactId, workflowExecutionId: $ }), l(`Started ${S.artifactId}`);
    } catch (_) {
      c(_ instanceof Error ? _.message : String(_));
    }
  }, b = (S) => {
    m && Kt(t, m, S) && (c(""), p(null), l(`Sent ${S.artifactId} to Weaver`));
  }, j = (S) => {
    c(""), p(null), l(`Copied ${S}`);
  }, v = (S) => {
    l(""), p(null), c(`Could not copy ${S}.`);
  };
  return /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => {
        E();
      }, children: "Refresh" }),
      /* @__PURE__ */ i.jsxs("label", { className: "wf-search wf-executable-definition-filter", children: [
        /* @__PURE__ */ i.jsx(Vr, { size: 14 }),
        /* @__PURE__ */ i.jsx(
          "input",
          {
            "aria-label": "Filter executables by workflow definition",
            list: "wf-executable-definition-options",
            placeholder: "Filter by definition ID",
            value: n ?? "",
            onChange: (S) => o(S.currentTarget.value || null)
          }
        )
      ] }),
      /* @__PURE__ */ i.jsx("datalist", { id: "wf-executable-definition-options", children: w.map((S) => /* @__PURE__ */ i.jsx("option", { value: S }, S)) }),
      n ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => o(null), children: [
        /* @__PURE__ */ i.jsx(qu, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    r === "failed" ? /* @__PURE__ */ i.jsx(No, { message: a }) : null,
    u ? /* @__PURE__ */ i.jsx(_d, { status: u, run: f }) : null,
    r === "loading" ? /* @__PURE__ */ i.jsx(Is, {}) : null,
    r === "ready" && N.length === 0 ? /* @__PURE__ */ i.jsx(
      As,
      {
        icon: /* @__PURE__ */ i.jsx(vn, { size: 22 }),
        title: "No workflow executables",
        description: n ? "No executables match this definition filter." : "Publish a workflow definition to make it executable."
      }
    ) : null,
    r === "ready" && N.length > 0 ? /* @__PURE__ */ i.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
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
            /* @__PURE__ */ i.jsx(dn, { value: S.artifactId, ariaLabel: `Copy artifact ID ${S.artifactId}`, copiedLabel: "artifact ID", onCopied: j, onCopyFailed: v })
          ] }),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
            /* @__PURE__ */ i.jsx("small", { title: S.artifactHash, children: S.artifactHash }),
            /* @__PURE__ */ i.jsx(dn, { value: S.artifactHash, ariaLabel: `Copy artifact hash ${S.artifactHash}`, copiedLabel: "artifact hash", onCopied: j, onCopyFailed: v })
          ] })
        ] }),
        /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line wf-version-cell", children: [
          /* @__PURE__ */ i.jsx("span", { children: S.artifactVersion }),
          /* @__PURE__ */ i.jsx(dn, { value: S.artifactVersion, ariaLabel: `Copy artifact version ${S.artifactVersion}`, copiedLabel: "artifact version", onCopied: j, onCopyFailed: v })
        ] }),
        /* @__PURE__ */ i.jsx(E1, { executable: S, onCopied: j, onCopyFailed: v }),
        /* @__PURE__ */ i.jsx("span", { children: Td(S) }),
        /* @__PURE__ */ i.jsx("span", { children: nt(S.publishedAt ?? S.createdAt) }),
        /* @__PURE__ */ i.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => {
            y(S);
          }, children: [
            /* @__PURE__ */ i.jsx(vn, { size: 13 }),
            " Run"
          ] }),
          m ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => b(S), children: [
            /* @__PURE__ */ i.jsx(gt, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, S.artifactId))
    ] }) : null
  ] });
}
function E1({ executable: e, onCopied: t, onCopyFailed: n }) {
  const o = e.sourceId || e.definitionVersionId || e.definitionId, r = e.sourceVersion;
  return /* @__PURE__ */ i.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ i.jsx("span", { className: "wf-source-kind", children: $d(e.sourceKind) }),
    o ? /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line", children: [
      /* @__PURE__ */ i.jsx("code", { title: o, children: o }),
      /* @__PURE__ */ i.jsx(dn, { value: o, ariaLabel: `Copy source ID ${o}`, copiedLabel: "source ID", onCopied: t, onCopyFailed: n })
    ] }) : null,
    r ? /* @__PURE__ */ i.jsxs("small", { children: [
      "Version ",
      r
    ] }) : null
  ] });
}
function _d({ status: e, run: t, compact: n = !1 }) {
  const o = () => {
    t?.workflowExecutionId && (window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(t.workflowExecutionId)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  };
  return /* @__PURE__ */ i.jsxs("div", { className: `wf-status-line${n ? " compact" : ""}`, children: [
    /* @__PURE__ */ i.jsx(jn, { size: n ? 13 : 14 }),
    /* @__PURE__ */ i.jsx("span", { children: e }),
    t?.workflowExecutionId ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: o, children: [
      "Open Run ",
      t.workflowExecutionId
    ] }) : null
  ] });
}
function dn({ value: e, ariaLabel: t, copiedLabel: n, onCopied: o, onCopyFailed: r }) {
  if (!e) return null;
  const s = async (a) => {
    a.preventDefault(), a.stopPropagation();
    try {
      await X1(e), o(n);
    } catch {
      r(n);
    }
  };
  return /* @__PURE__ */ i.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (a) => {
    s(a);
  }, children: /* @__PURE__ */ i.jsx(Pw, { size: 12 }) });
}
function C1({ context: e, ai: t, definitionId: n, publishedArtifactId: o }) {
  const [r, s] = q("loading"), [a, c] = q(""), [u, l] = q(""), [f, p] = q(null), [d, h] = q([]), g = Ft(t, "weaver.workflows.explain-executable"), N = le(async () => {
    s("loading"), c("");
    try {
      const j = await ld(e);
      h(j.filter((v) => q1(v, n)).sort(U1)), s("ready");
    } catch (j) {
      c(j instanceof Error ? j.message : String(j)), h([]), s("failed");
    }
  }, [e, n]);
  Q(() => {
    N();
  }, [N, o]);
  const w = async (j) => {
    l(""), p(null), c("");
    try {
      const v = await cd(e, j.artifactId);
      p({ artifactId: j.artifactId, workflowExecutionId: Pd(v) }), l(`Started ${j.artifactId}`);
    } catch (v) {
      c(v instanceof Error ? v.message : String(v));
    }
  }, m = (j) => {
    g && Kt(t, g, j) && (c(""), p(null), l(`Sent ${j.artifactId} to Weaver`));
  }, E = () => {
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
        /* @__PURE__ */ i.jsx(ys, { size: 13 }),
        " Refresh"
      ] }),
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: E, children: "Open list" })
    ] }),
    r === "failed" ? /* @__PURE__ */ i.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ i.jsx(kt, { size: 14 }),
      " ",
      a
    ] }) : null,
    u ? /* @__PURE__ */ i.jsx(_d, { status: u, run: f, compact: !0 }) : null,
    r === "loading" ? /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "Loading artifacts..." }) : null,
    r === "ready" && d.length === 0 ? /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "No published artifacts for this workflow yet." }) : null,
    r === "ready" && d.length > 0 ? /* @__PURE__ */ i.jsx("div", { className: "wf-artifact-list", role: "list", "aria-label": "Workflow artifacts", children: d.map((j) => /* @__PURE__ */ i.jsxs("article", { className: "wf-artifact-card", role: "listitem", "data-active": j.artifactId === o ? "true" : void 0, children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-artifact-card-heading", children: [
        /* @__PURE__ */ i.jsxs("div", { children: [
          /* @__PURE__ */ i.jsxs("span", { className: "wf-artifact-version", children: [
            "Version ",
            j.artifactVersion
          ] }),
          j.artifactId === o ? /* @__PURE__ */ i.jsx("span", { className: "wf-chip", children: "Latest publish" }) : null
        ] }),
        /* @__PURE__ */ i.jsx("span", { children: nt(j.publishedAt ?? j.createdAt) })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { className: "wf-artifact-card-values", children: [
        /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line", children: [
          /* @__PURE__ */ i.jsx("code", { title: j.artifactId, children: j.artifactId }),
          /* @__PURE__ */ i.jsx(dn, { value: j.artifactId, ariaLabel: `Copy artifact ID ${j.artifactId}`, copiedLabel: "artifact ID", onCopied: y, onCopyFailed: b })
        ] }),
        /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ i.jsx("code", { title: j.artifactHash, children: j.artifactHash }),
          /* @__PURE__ */ i.jsx(dn, { value: j.artifactHash, ariaLabel: `Copy artifact hash ${j.artifactHash}`, copiedLabel: "artifact hash", onCopied: y, onCopyFailed: b })
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
          /* @__PURE__ */ i.jsx(vn, { size: 13 }),
          " Run"
        ] }),
        g ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => m(j), children: [
          /* @__PURE__ */ i.jsx(gt, { size: 13 }),
          " Explain"
        ] }) : null
      ] })
    ] }, j.artifactId)) }) : null
  ] });
}
function I1({ context: e }) {
  const [t, n] = q("loading"), [o, r] = q(""), [s, a] = q(""), [c, u] = q(""), [l, f] = q([]), p = le(async () => {
    n("loading"), r("");
    try {
      const h = await pv(e, {
        status: s || void 0,
        runKind: c || void 0,
        take: 100
      });
      f(h), n("ready");
    } catch (h) {
      r(h instanceof Error ? h.message : String(h)), f([]), n("failed");
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
    t === "failed" ? /* @__PURE__ */ i.jsx(No, { message: o }) : null,
    t === "loading" ? /* @__PURE__ */ i.jsx(Is, {}) : null,
    t === "ready" && l.length === 0 ? /* @__PURE__ */ i.jsx(
      As,
      {
        icon: /* @__PURE__ */ i.jsx(Lr, { size: 22 }),
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
            /* @__PURE__ */ i.jsx("span", { children: Dd(h.runKind) }),
            /* @__PURE__ */ i.jsx("span", { children: /* @__PURE__ */ i.jsx(jo, { status: h.status, subStatus: h.subStatus }) }),
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
            /* @__PURE__ */ i.jsx("span", { children: nt(h.startedAt ?? h.createdAt) }),
            /* @__PURE__ */ i.jsx("span", { children: wd(h.startedAt ?? h.createdAt, h.completedAt ?? h.updatedAt) })
          ]
        },
        h.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function A1({ context: e, ai: t, workflowExecutionId: n }) {
  const [o, r] = q("loading"), [s, a] = q(""), [c, u] = q(null), [l, f] = q(null), p = Ft(t, "weaver.workflows.explain-instance"), d = le(async () => {
    if (!n) {
      a("No workflow execution id was provided."), r("failed");
      return;
    }
    r("loading"), a("");
    try {
      const g = await hv(e, n), [N, w] = await Promise.all([
        ov(e, g.instance.definitionVersionId).then(
          (m) => ({ definitionVersion: m, error: "" }),
          (m) => ({ definitionVersion: null, error: m instanceof Error ? m.message : String(m) })
        ),
        bs(e)
      ]);
      u({
        details: g,
        definitionVersion: N.definitionVersion,
        definitionVersionError: N.error,
        activityCatalog: w.activities
      }), f(null), r("ready");
    } catch (g) {
      u(null), a(pN(g, n)), r("failed");
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
        /* @__PURE__ */ i.jsx(yr, { size: 14 }),
        " Runs"
      ] }),
      /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => {
        d();
      }, children: [
        /* @__PURE__ */ i.jsx(ys, { size: 14 }),
        " Refresh"
      ] }),
      c && p ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => Kt(t, p, c.details), children: [
        /* @__PURE__ */ i.jsx(gt, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    o === "loading" ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "Loading workflow run..." }) : null,
    o === "failed" ? /* @__PURE__ */ i.jsx(No, { message: s }) : null,
    o === "ready" && c ? /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-detail-workbench", children: [
      /* @__PURE__ */ i.jsx(
        _1,
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
        D1,
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
          graphNodeIds: c.definitionVersion ? P1(c.definitionVersion, c.activityCatalog) : void 0
        }
      )
    ] }) : null
  ] });
}
function _1({ definitionVersion: e, definitionVersionError: t, activityCatalog: n, details: o, selectedEvidenceId: r, onSelectEvidence: s }) {
  const a = de(() => {
    if (!e) return { nodes: [], edges: [] };
    const c = e.state.rootActivity;
    if (!c) return { nodes: [], edges: [] };
    const u = n.find((h) => h.activityVersionId === c.activityVersionId), l = ws(c, u), f = l === "unsupported" ? null : Qn(c, []), p = l === "unsupported" ? Li(c, n, e.layout) : f ? ed(f, n, e.layout) : Li(c, n, e.layout), d = p.nodes.map((h) => ({
      ...h,
      draggable: !1,
      connectable: !1,
      deletable: !1
    }));
    return {
      nodes: b0(d, o.activities, o.incidents, r),
      edges: p.edges.map((h) => ({ ...h, deletable: !1 }))
    };
  }, [n, e, o, r]);
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
          /* @__PURE__ */ i.jsx("small", { children: o.instance.definitionVersionId })
        ] }) })
      ] }),
      /* @__PURE__ */ i.jsx(jo, { status: o.instance.status, subStatus: o.instance.subStatus })
    ] }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ i.jsxs("div", { className: "wf-empty", children: [
        "The workflow run loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ i.jsx("small", { children: fN(t) }) : null
      ] }),
      e && a.nodes.length === 0 ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      a.nodes.length > 0 ? /* @__PURE__ */ i.jsxs(
        Tu,
        {
          nodes: a.nodes,
          edges: a.edges,
          nodeTypes: kd,
          edgeTypes: Ed,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (c, u) => s(u.id),
          onPaneClick: () => s(null),
          children: [
            /* @__PURE__ */ i.jsx(Pu, {}),
            /* @__PURE__ */ i.jsx(Vu, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ i.jsx(Ru, {})
          ]
        }
      ) : null
    ] })
  ] });
}
function D1({ ai: e, action: t, summary: n, details: o, state: r, error: s, selectedEvidenceId: a = null, onSelectEvidence: c, graphNodeIds: u, activityCatalog: l = [] }) {
  const [f, p] = q("timeline");
  if (!n)
    return /* @__PURE__ */ i.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "Select a workflow run to inspect its timeline." }) });
  const d = o?.incidents.length ?? 0, h = [
    { id: "timeline", title: "Timeline", order: 0, icon: /* @__PURE__ */ i.jsx(gs, { size: 14 }), render: () => null },
    { id: "issues", title: d > 0 ? `Issues (${d})` : "Issues", order: 1, icon: /* @__PURE__ */ i.jsx(kt, { size: 14 }), render: () => null },
    { id: "details", title: "Details", order: 2, icon: /* @__PURE__ */ i.jsx(ms, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ i.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Workflow run details", children: [
    /* @__PURE__ */ i.jsxs("header", { children: [
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("span", { children: "Workflow Instance ID" }),
        /* @__PURE__ */ i.jsx("h3", { children: n.workflowExecutionId })
      ] }),
      t ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => Kt(e, t, o ?? n), children: [
        /* @__PURE__ */ i.jsx(gt, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    /* @__PURE__ */ i.jsx("div", { className: "wf-instance-tabs", children: /* @__PURE__ */ i.jsx(rr, { label: "Workflow run tabs", tabs: h, activeTabId: f, onSelect: (g) => p(g) }) }),
    r === "loading" ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "Loading run details..." }) : null,
    r === "failed" ? /* @__PURE__ */ i.jsx(No, { message: s }) : null,
    r === "ready" && o ? /* @__PURE__ */ i.jsx("div", { className: "wf-instance-tab-content", children: f === "timeline" ? /* @__PURE__ */ i.jsx(
      Yb,
      {
        activities: o.activities,
        activityCatalog: l,
        selectedEvidenceId: a,
        onSelectEvidence: c
      }
    ) : f === "issues" ? /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
      /* @__PURE__ */ i.jsx(M1, { incidents: o.incidents, selectedEvidenceId: a, onSelectEvidence: c }),
      /* @__PURE__ */ i.jsx(T1, { details: o, graphNodeIds: u })
    ] }) : /* @__PURE__ */ i.jsxs("dl", { className: "wf-instance-meta", children: [
      /* @__PURE__ */ i.jsx("dt", { children: "Status" }),
      /* @__PURE__ */ i.jsx("dd", { children: /* @__PURE__ */ i.jsx(jo, { status: n.status, subStatus: n.subStatus }) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Run Kind" }),
      /* @__PURE__ */ i.jsx("dd", { children: Dd(n.runKind) }),
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
      /* @__PURE__ */ i.jsx("dd", { children: nt(n.createdAt) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Started" }),
      /* @__PURE__ */ i.jsx("dd", { children: nt(n.startedAt) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Completed" }),
      /* @__PURE__ */ i.jsx("dd", { children: nt(n.completedAt) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Correlation" }),
      /* @__PURE__ */ i.jsx("dd", { children: n.correlationId || "None" })
    ] }) }) : null
  ] });
}
function M1({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ i.jsx("h4", { children: "Incidents" }),
    e.length === 0 ? /* @__PURE__ */ i.jsx("p", { children: "No incidents recorded." }) : null,
    e.map((o) => /* @__PURE__ */ i.jsxs(
      "button",
      {
        type: "button",
        className: "wf-instance-incident",
        "data-severity": o.severity.toLowerCase(),
        "data-selected": o.incidentId === t,
        onClick: () => n?.(o.incidentId),
        children: [
          /* @__PURE__ */ i.jsx("strong", { children: o.failureType }),
          /* @__PURE__ */ i.jsxs("span", { children: [
            o.status,
            " · ",
            o.severity
          ] }),
          /* @__PURE__ */ i.jsx("p", { children: o.message })
        ]
      },
      o.incidentId
    ))
  ] });
}
function T1({ details: e, graphNodeIds: t }) {
  if (!t) return null;
  const n = new Map(e.activities.map((s) => [s.activityExecutionId, s])), o = e.activities.filter((s) => !t.has(Pc(s))), r = e.incidents.filter((s) => {
    const a = s.activityExecutionId ? n.get(s.activityExecutionId) : null, c = s.executableNodeId ?? (a ? Pc(a) : "");
    return !c || !t.has(c);
  });
  return o.length === 0 && r.length === 0 ? null : /* @__PURE__ */ i.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ i.jsx("h4", { children: "Unmatched runtime evidence" }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-unmatched-list", children: [
      o.map((s) => /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ i.jsx("strong", { children: kr(s.activityType) ?? s.activityType }),
        /* @__PURE__ */ i.jsx("small", { children: s.activityExecutionId })
      ] }, `activity-${s.activityExecutionId}`)),
      r.map((s) => /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ i.jsx("strong", { children: s.failureType }),
        /* @__PURE__ */ i.jsx("small", { children: s.incidentId })
      ] }, `incident-${s.incidentId}`))
    ] })
  ] });
}
function jo({ status: e, subStatus: t }) {
  return /* @__PURE__ */ i.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
function Dd(e) {
  switch ($1(e)) {
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
function $1(e) {
  return (e ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}
function P1(e, t) {
  const n = e.state.rootActivity;
  if (!n) return /* @__PURE__ */ new Set();
  const o = t.find((s) => s.activityVersionId === n.activityVersionId);
  if (ws(n, o) === "unsupported") return /* @__PURE__ */ new Set([n.nodeId]);
  const r = Qn(n, []);
  return new Set(r?.slot.activities.map((s) => s.nodeId) ?? [n.nodeId]);
}
function Pc(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function z1({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const s = Md(n, t), a = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
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
      /* @__PURE__ */ i.jsx("select", { value: t, onChange: (u) => r(Number(u.target.value)), children: p1.map((u) => /* @__PURE__ */ i.jsx("option", { value: u, children: u }, u)) })
    ] }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ i.jsx(yr, { size: 14 }),
        " Previous"
      ] }),
      /* @__PURE__ */ i.jsxs("span", { children: [
        "Page ",
        e,
        " of ",
        s
      ] }),
      /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => o(e + 1), disabled: e >= s, "aria-label": "Next page", title: "Next page", children: [
        "Next ",
        /* @__PURE__ */ i.jsx(sn, { size: 14 })
      ] })
    ] })
  ] });
}
function R1(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function Md(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Ft(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function Kt(e, t, n) {
  const o = t.createPrompt(n);
  return o ? (e.dispatchPrompt(o), !0) : !1;
}
function L1(e) {
  if (!e) return null;
  const t = [], n = e.match(/```(?:json)?\s*([\s\S]*?)```/i);
  n && t.push(n[1]);
  const o = e.match(/\{[\s\S]*?\}/);
  o && t.push(o[0]);
  for (const a of t)
    try {
      const c = JSON.parse(a.trim()), u = zc(c.name, c.description);
      if (u) return u;
    } catch {
    }
  const r = e.match(/name\s*[:\-]\s*(.+)/i)?.[1], s = e.match(/description\s*[:\-]\s*(.+)/i)?.[1];
  return zc(r, s);
}
function zc(e, t) {
  const n = typeof e == "string" ? Rc(e) : void 0, o = typeof t == "string" ? Rc(t) : void 0;
  return n || o ? { name: n || void 0, description: o || void 0 } : null;
}
function Rc(e) {
  return e.trim().replace(/,$/, "").trim().replace(/^["']/, "").replace(/["']$/, "").trim();
}
function V1(e, t) {
  return e.rootActivityVersionId ?? O1(t, e.rootKind)?.activityVersionId ?? null;
}
function O1(e, t) {
  return e.find((n) => H1(n) === t);
}
function H1(e) {
  return e ? W1(e) ? "flowchart" : B1(e) ? "sequence" : null : null;
}
function Xi(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "Uncategorized";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [o]) => n.localeCompare(o)).map(([n, o]) => ({
    category: n,
    activities: o.sort((r, s) => Ie(r).localeCompare(Ie(s)))
  }));
}
function W1(e) {
  return Ie(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function B1(e) {
  return Ie(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function F1(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function Td(e) {
  return Y1(e.rootActivityType) || e.rootActivityType;
}
function K1(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function q1(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function U1(e, t) {
  return Lc(t) - Lc(e);
}
function Lc(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function $d(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
function Pd(e) {
  const t = e?.workflowExecutionId ?? e?.runId ?? e?.executionId;
  return typeof t == "string" && t.trim() ? t : null;
}
async function X1(e) {
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
function Y1(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function Z1(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    Uo(t, n.typeName, n), Uo(t, n.name, n), Uo(t, n.displayName, n);
    const o = n.typeName.split(".").filter(Boolean).at(-1);
    Uo(t, o, n);
  }
  return t;
}
function G1(e, t, n) {
  const o = t.get(e.activityVersionId);
  return n.get(Yn(o?.activityTypeKey)) ?? n.get(Yn(kr(o?.activityTypeKey))) ?? n.get(Yn(o?.displayName)) ?? n.get(Yn(e.activityVersionId)) ?? null;
}
function Uo(e, t, n) {
  const o = Yn(t);
  o && !e.has(o) && e.set(o, n);
}
function Yn(e) {
  return e?.trim().toLowerCase() ?? "";
}
function Vc(e, t, n, o) {
  const r = Br();
  if (!r) return t;
  const s = r.getItem(e);
  if (s == null) return t;
  const a = Number(s);
  return Number.isFinite(a) ? or(a, n, o) : t;
}
function Oc(e, t) {
  const n = Br();
  if (!n) return t;
  const o = n.getItem(e);
  return o === "true" ? !0 : o === "false" ? !1 : t;
}
function J1() {
  const e = Br();
  if (!e) return null;
  const t = e.getItem(Cd);
  return t === "palette" || t === "inspector" ? t : null;
}
function Br() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function Kn(e, t) {
  const n = Br();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function or(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function Q1({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: o,
  expressionEditors: r,
  workflowDesignerPanels: s,
  onBack: a
}) {
  const [c, u] = q(null), l = ie(null), f = ie(null), p = ie({});
  Q(() => {
    l.current = c;
  }, [c]);
  const [d, h] = q(null), [g, N] = q([]), [w, m] = q([]), [E, y] = q(null), [b, j] = q(tr), [v, S] = q("loading"), [_, $] = q([]), [V, D] = q([]), [A, L] = q([]), [k, M] = q(null), [I, T] = q(null), [z, P] = q(null), [K, B] = q(null), [F, X] = q(""), [J, U] = q(""), [ce, G] = q("idle"), [O, Y] = q(null), [se, ae] = q(!1), [te, oe] = q(null), [fe, W] = q(() => /* @__PURE__ */ new Set()), [ee, me] = q(""), [ve, $e] = q(() => Vc(Ic, g1, Hn, Wn)), [De, Le] = q(() => Vc(Ac, y1, Bn, Fn)), [Pe, Ze] = q(() => Oc(_c, !1)), [Ae, Ce] = q(() => Oc(Dc, !1)), [xe, ze] = q(J1), [lt, Fr] = q("activities"), [kn, En] = q("inspector"), [Et, So] = q("designer"), Ge = ie(null), qt = ie(null), Cn = ie(""), In = ie(0), ko = ie(Promise.resolve()), An = ie(/* @__PURE__ */ new Map()), Be = ie(wc()), ut = ie(null), dt = ie(""), Ut = ie(!1), [Kr, _n] = q(0), Xt = ie(null), mt = ie(null), Ct = ie(!1), It = d?.state.rootActivity ?? null, Ve = de(() => new Map(g.map((x) => [x.activityVersionId, x])), [g]), Dn = le(
    (x) => $v([x.activityVersionId, x.activityTypeKey], E),
    [E]
  ), Eo = de(() => Z1(w), [w]), Fe = de(() => Qu(It, _), [It, _]), Co = ws(Fe, Fe ? Ve.get(Fe.activityVersionId) : void 0), be = !!Fe && Co === "unsupported", Ke = de(() => be ? null : Qn(It, _), [It, _, be]), Yt = de(() => Xi(g), [g]), Io = de(() => {
    const x = ee.trim().toLowerCase();
    if (!x) return Yt;
    const C = g.filter((R) => Ie(R).toLowerCase().includes(x) || R.activityTypeKey.toLowerCase().includes(x) || (R.category ?? "").toLowerCase().includes(x) || (R.description ?? "").toLowerCase().includes(x));
    return Xi(C);
  }, [g, ee, Yt]), ge = de(() => be && Fe?.nodeId === I ? Fe : Ke?.slot.activities.find((x) => x.nodeId === I) ?? null, [be, Ke, Fe, I]), At = de(
    () => ge ? G1(ge, Ve, Eo) : null,
    [Ve, Eo, ge]
  ), Mn = de(
    () => ge ? Dn({ activityVersionId: ge.activityVersionId, activityTypeKey: Ve.get(ge.activityVersionId)?.activityTypeKey }) : null,
    [Dn, Ve, ge]
  ), Zt = ge ? We(ge) : [], Gt = nv(e, d?.state, I), _e = Co === "flowchart" && Ke?.slot.mode === "flowchart", _t = !It || !be, xt = ce !== "idle", qr = !!d?.state.rootActivity && !xt, Ao = Ft(n, "weaver.workflows.find-draft-risks"), _o = Ft(n, "weaver.workflows.propose-update");
  Q(() => {
    if (!(!c || !d))
      return window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
        workflowId: c.definition.id,
        workflowDefinitionId: c.definition.id,
        workflowVersionId: d.sourceVersionId ?? null,
        draftId: d.id,
        revision: uN(d),
        selectedNodeId: I,
        selectedActivityType: At?.typeName ?? (ge ? Ve.get(ge.activityVersionId)?.activityTypeKey ?? ge.activityVersionId : null),
        summary: c.definition.name,
        activities: Rd(d.state.rootActivity, Ve),
        connections: Ld(d.state.rootActivity),
        diagnostics: d.validationErrors.map((x) => ({ severity: x.code ?? "warning", message: x.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === c.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [Ve, c, d, At, ge, I]), Q(() => {
    const x = (R) => {
      const H = R.detail;
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
        const ue = qn(d), re = t1(d, H.batch, g), ye = `weaver-batch-${Date.now()}`;
        An.current.set(ye, ue), h(re.draft), $([]), T(re.finalActivityIds.at(-1) ?? null), oe(null), Y(null), U(re.summary), X(""), H.respond({ ok: !0, result: { ...re, undoToken: ye } });
      } catch (ue) {
        const re = ue instanceof Error ? ue.message : String(ue);
        X(re), H.respond({ ok: !1, message: re });
      }
    }, C = (R) => {
      const H = R.detail;
      if (!H?.undoToken || !H.respond) return;
      const Z = An.current.get(H.undoToken);
      if (!Z) {
        H.respond({ ok: !1, message: "The Weaver batch undo point is no longer available." });
        return;
      }
      An.current.delete(H.undoToken), h(Z), $([]), T(null), oe(null), Y(null), U("Restored workflow draft before Weaver batch."), X(""), H.respond({ ok: !0, summary: "Restored workflow draft before Weaver batch." });
    };
    return window.addEventListener(Sc, x), window.addEventListener(kc, C), () => {
      window.removeEventListener(Sc, x), window.removeEventListener(kc, C);
    };
  }, [g, c, d]), Q(() => {
    Kn(Ic, String(ve));
  }, [ve]), Q(() => {
    Kn(Ac, String(De));
  }, [De]), Q(() => {
    Kn(_c, String(Pe));
  }, [Pe]), Q(() => {
    Kn(Dc, String(Ae));
  }, [Ae]), Q(() => {
    Kn(Cd, xe);
  }, [xe]), Q(() => {
    if (!xe) return;
    const x = (C) => {
      C.key === "Escape" && ze(null);
    };
    return window.addEventListener("keydown", x), () => window.removeEventListener("keydown", x);
  }, [xe]);
  const Jt = le(async () => {
    X(""), S("loading");
    const [x, C, R, H, Z] = await Promise.all([
      ev(e, t),
      bs(e),
      mv(e).then(
        (re) => ({ ok: !0, descriptors: re }),
        () => ({ ok: !1, descriptors: [] })
      ),
      xv(e).then(
        (re) => ({ ok: !0, descriptors: re }),
        () => ({ ok: !1, descriptors: tr })
      ),
      // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
      ud(e).then(
        (re) => re,
        () => null
      )
    ]), ue = x.draft ?? null;
    u(x), Cn.current = ue ? qe(ue) : "", Be.current = wc(), ut.current = ue ? qn(ue) : null, dt.current = ue ? qe(ue) : "", Ut.current = !1, _n(0), h(ue), N(C.activities ?? []), m(R.descriptors), y(Z), j(H.descriptors.length > 0 ? H.descriptors : tr), S(R.ok ? "ready" : "failed"), $([]), T(null);
  }, [e, t]);
  Q(() => {
    Jt().catch((x) => X(x instanceof Error ? x.message : String(x)));
  }, [Jt]), Q(() => {
    W((x) => {
      let C = !1;
      const R = new Set(x);
      for (const H of Yt)
        R.has(H.category) || (R.add(H.category), C = !0);
      return C ? R : x;
    });
  }, [Yt]), Q(() => {
    if (!Fe) {
      D([]), L([]);
      return;
    }
    const x = be ? Li(Fe, g, d?.layout ?? []) : Ke ? ed(Ke, g, d?.layout ?? []) : { nodes: [], edges: [] };
    D(x.nodes), L(x.edges);
  }, [g, d?.layout, be, Ke, Fe]);
  const Ur = (x) => {
    h((C) => C && { ...C, state: { ...C.state, rootActivity: x } });
  }, Tn = le((x, C) => {
    if (d?.state.rootActivity && be)
      return;
    const R = Oi(x, Bc(x));
    if (!d?.state.rootActivity) {
      Ur(R), T(R.nodeId);
      return;
    }
    if (!Ke) {
      if (!We(R)[0]) {
        U(""), X("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      h((Z) => {
        if (!Z?.state.rootActivity) return Z;
        const ue = Z.state.rootActivity, re = Vi(R, [], [ue]), ye = C ? [
          ...Z.layout.filter((we) => we.nodeId !== ue.nodeId),
          {
            nodeId: ue.nodeId,
            x: Math.round(C.x),
            y: Math.round(C.y)
          }
        ] : Z.layout;
        return {
          ...Z,
          layout: ye,
          state: {
            ...Z.state,
            rootActivity: re
          }
        };
      }), T(d.state.rootActivity.nodeId), X(""), U(`Wrapped root in ${Ie(x)}`);
      return;
    }
    h((H) => {
      if (!H?.state.rootActivity) return H;
      const Z = Qn(H.state.rootActivity, _);
      if (!Z) return H;
      const ue = Vi(H.state.rootActivity, _, [...Z.slot.activities, R]), re = C ? [
        ...H.layout.filter((ye) => ye.nodeId !== R.nodeId),
        {
          nodeId: R.nodeId,
          x: Math.round(C.x),
          y: Math.round(C.y)
        }
      ] : H.layout;
      return {
        ...H,
        layout: re,
        state: {
          ...H.state,
          rootActivity: ue
        }
      };
    }), T(R.nodeId);
  }, [d?.state.rootActivity, _, be, Ke]), Qt = le((x, C) => {
    const R = Oi(x, Bc(x)), H = {
      id: R.nodeId,
      type: "workflowActivity",
      position: C,
      selected: !0,
      data: {
        label: Ie(x),
        activityVersionId: x.activityVersionId,
        activityTypeKey: x.activityTypeKey,
        category: x.category,
        executionType: x.executionType,
        icon: vr(x),
        childSlots: We(R),
        acceptsInbound: String(x.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: id(R, x)
      }
    };
    return { activityNode: R, node: H };
  }, []), Re = le((x, C, R = []) => {
    be || h((H) => {
      if (!H) return H;
      const Z = S0(H.layout, x), ue = H.state.rootActivity;
      if (!ue) return { ...H, layout: Z };
      const re = Qn(ue, _);
      if (!re) return { ...H, layout: Z };
      const ye = N0(re, x, C, R), we = re.slot.mode === "flowchart" ? j0(ye, C) : ye;
      return {
        ...H,
        layout: Z,
        state: {
          ...H.state,
          rootActivity: td(ue, _, we)
        }
      };
    });
  }, [_, be]), $n = le((x, C) => {
    if (!Ge.current) return null;
    const R = Ge.current.getBoundingClientRect();
    return k ? k.screenToFlowPosition({ x, y: C }) : {
      x: x - R.left,
      y: C - R.top
    };
  }, [k]), Pn = le((x, C) => document.elementFromPoint(x, C)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), zn = le((x, C, R) => {
    const H = V.find((Me) => Me.id === C.source), Z = V.find((Me) => Me.id === C.target), ue = H && Z ? aN(H, Z) : H ? Fc(H) : R, re = Qt(x, ue), we = [...V.map((Me) => Me.selected ? { ...Me, selected: !1 } : Me), re.node], Mt = T0(A, C, re.node.id);
    D(we), L(Mt), T(re.node.id), Re(we, Mt, [re.activityNode]);
  }, [Re, Qt, A, V]), en = le((x, C, R) => {
    if (!_t || !Ge.current) return !1;
    const H = Ge.current.getBoundingClientRect();
    if (!(C >= H.left && C <= H.right && R >= H.top && R <= H.bottom)) return !1;
    const ue = $n(C, R);
    if (!ue) return !1;
    if (_e) {
      const re = Pn(C, R), ye = re ? A.find((we) => we.id === re) : void 0;
      if (ye)
        return zn(x, ye, ue), !0;
    }
    return Tn(x, ue), !0;
  }, [Tn, _t, A, Pn, _e, zn, $n]);
  Q(() => {
    const x = (R) => {
      const H = Xt.current;
      if (!H) return;
      Math.hypot(R.clientX - H.startX, R.clientY - H.startY) >= u1 && (H.dragging = !0);
    }, C = (R) => {
      const H = Xt.current;
      if (Xt.current = null, !H?.dragging || !Ge.current || mt.current) return;
      const Z = Ge.current.getBoundingClientRect();
      R.clientX >= Z.left && R.clientX <= Z.right && R.clientY >= Z.top && R.clientY <= Z.bottom && (Ct.current = !0, window.setTimeout(() => {
        Ct.current = !1;
      }, 0), en(H.activity, R.clientX, R.clientY));
    };
    return window.addEventListener("pointermove", x), window.addEventListener("pointerup", C), window.addEventListener("pointercancel", C), () => {
      window.removeEventListener("pointermove", x), window.removeEventListener("pointerup", C), window.removeEventListener("pointercancel", C);
    };
  }, [k, en]);
  const Do = (x, C) => {
    mt.current = { activityVersionId: C.activityVersionId, handledDrop: !1 }, x.dataTransfer.setData(Cc, C.activityVersionId), x.dataTransfer.setData("text/plain", C.activityVersionId), x.dataTransfer.effectAllowed = "copy";
  }, Xr = (x, C) => {
    const R = mt.current;
    mt.current = null, !R?.handledDrop && (x.clientX === 0 && x.clientY === 0 || en(C, x.clientX, x.clientY) && (Ct.current = !0, window.setTimeout(() => {
      Ct.current = !1;
    }, 0)));
  }, Yr = (x, C) => {
    x.button === 0 && (Xt.current = {
      activity: C,
      startX: x.clientX,
      startY: x.clientY,
      dragging: !1
    });
  }, Zr = (x) => {
    Ct.current || _t && Tn(x);
  }, Mo = (x) => {
    if (!_t) {
      x.dataTransfer.dropEffect = "none";
      return;
    }
    if (x.preventDefault(), x.dataTransfer.dropEffect = "copy", !_e) return;
    const C = Pn(x.clientX, x.clientY);
    B(C);
  }, To = (x) => {
    if (!Ge.current) return;
    const C = x.relatedTarget;
    C && Ge.current.contains(C) || B(null);
  }, $o = (x) => {
    x.preventDefault(), B(null);
    const C = x.dataTransfer.getData(Cc) || x.dataTransfer.getData("text/plain");
    if (!C || (x.stopPropagation(), mt.current?.activityVersionId === C && (mt.current.handledDrop = !0), !_t)) return;
    const R = Ve.get(C);
    R && en(R, x.clientX, x.clientY);
  }, Gr = () => {
    if (!_e) return;
    const x = Ge.current?.getBoundingClientRect();
    x && P({
      kind: "fromEmpty",
      clientX: x.left + x.width / 2,
      clientY: x.top + x.height / 2
    });
  }, tn = le(async (x, C) => {
    const R = async () => {
      const Z = ++In.current, ue = qe(x);
      X("");
      try {
        const re = await lv(e, x), ye = qe(re);
        return Cn.current = ye, h((we) => !we || we.id !== re.id ? we : qe(we) === ue ? re : { ...we, validationErrors: re.validationErrors }), Z === In.current && U(C), re;
      } catch (re) {
        throw Z === In.current && (U(""), X(re instanceof Error ? re.message : String(re))), re;
      }
    }, H = ko.current.then(R, R);
    return ko.current = H.catch(() => {
    }), H;
  }, [e]);
  Q(() => {
    if (!se || !d || qe(d) === Cn.current) return;
    U("Autosaving...");
    const C = window.setTimeout(() => {
      tn(d, "Autosaved").catch(() => {
      });
    }, d1);
    return () => window.clearTimeout(C);
  }, [se, d, tn]), Q(() => {
    if (!d) return;
    if (Ut.current) {
      Ut.current = !1;
      return;
    }
    const x = qe(d);
    if (x === dt.current) return;
    const C = window.setTimeout(() => {
      const R = ut.current;
      R && (Be.current = vc(Be.current, R), _n((H) => H + 1)), ut.current = qn(d), dt.current = x;
    }, f1);
    return () => window.clearTimeout(C);
  }, [d]);
  const Po = le(() => {
    if (!d) return;
    const x = c?.definition.name;
    fb(lb(d, x), x), U("Exported workflow as JSON.");
  }, [d, c]), Jr = le((x) => {
    h((C) => C && { ...C, state: x(C.state) });
  }, []), Dt = le(() => {
    f.current !== null && (window.clearTimeout(f.current), f.current = null);
    const x = p.current;
    p.current = {};
    const C = l.current?.definition;
    !C || x.name === void 0 && x.description === void 0 || cv(e, C.id, {
      name: x.name ?? C.name,
      description: x.description ?? C.description ?? null
    }).then((R) => u((H) => H && H.definition.id === R.definition.id ? { ...H, definition: R.definition } : H)).catch(() => U("Couldn't save name/description."));
  }, [e]), Qr = le((x) => {
    u((C) => C && { ...C, definition: { ...C.definition, ...x } }), p.current = { ...p.current, ...x }, f.current !== null && window.clearTimeout(f.current), f.current = window.setTimeout(Dt, 800);
  }, [Dt]);
  Q(() => () => {
    Dt();
  }, [Dt]);
  const ei = le((x) => {
    if (!d) return "No draft is loaded.";
    const C = db(x, d);
    return C.ok ? (h(C.draft), T(null), $([]), U("Applied workflow JSON."), null) : C.error;
  }, [d]), wt = le(() => {
    if (!d) return;
    const x = qe(d);
    if (x === dt.current) return;
    const C = ut.current;
    C && (Be.current = vc(Be.current, C)), ut.current = qn(d), dt.current = x;
  }, [d]), nn = le((x) => {
    Ut.current = !0, ut.current = qn(x), dt.current = qe(x), h(x), T(null), $([]), _n((C) => C + 1);
  }, []), Rn = le(() => {
    if (!d) return;
    wt();
    const x = bb(Be.current, d);
    x && (Be.current = x.history, nn(x.snapshot));
  }, [d, wt, nn]), on = le(() => {
    if (!d) return;
    wt();
    const x = Nb(Be.current, d);
    x && (Be.current = x.history, nn(x.snapshot));
  }, [d, wt, nn]), { canUndoNow: Od, canRedoNow: Hd } = de(() => {
    const x = !!d && !!ut.current && qe(d) !== dt.current;
    return {
      canUndoNow: wb(Be.current) || x,
      canRedoNow: vb(Be.current) && !x
    };
  }, [d, Kr]);
  Q(() => {
    const x = (C) => {
      if (Et !== "designer" || !(C.metaKey || C.ctrlKey)) return;
      const R = C.target;
      if (R && (R.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(R.tagName))) return;
      const H = C.key.toLowerCase();
      H === "z" && !C.shiftKey ? (C.preventDefault(), Rn()) : (H === "z" && C.shiftKey || H === "y") && (C.preventDefault(), on());
    };
    return window.addEventListener("keydown", x), () => window.removeEventListener("keydown", x);
  }, [Et, Rn, on]);
  const Wd = async () => {
    if (!(!d || xt)) {
      G("saving"), U("Saving...");
      try {
        await tn(d, "Saved");
      } catch {
      } finally {
        G("idle");
      }
    }
  }, Bd = async () => {
    if (!(!d || xt)) {
      G("promoting"), U("Saving...");
      try {
        await tn(d, "Saved"), U("Promoting...");
        const x = await uv(e, d.id), C = await dv(e, x.versionId);
        oe(C.artifactId), U(`Published ${C.artifactVersion}`), await Jt();
      } catch (x) {
        U(""), X(x instanceof Error ? x.message : String(x));
      } finally {
        G("idle");
      }
    }
  }, Fd = async () => {
    if (!d?.state.rootActivity || xt) return;
    const x = d, C = qe(x);
    Y(null), U("Preparing test run...");
    try {
      G("testRunPreparing"), U("Preparing test run...");
      const R = dN(x);
      G("testRunStarting"), U("Starting test run...");
      const H = await fv(e, {
        definitionId: x.definitionId,
        snapshotId: R,
        state: x.state
      });
      Y({ draftSignature: C, view: H }), En("runtime"), Ce(!1), U(_s(H) ? "Test run rejected" : "Test run dispatched");
    } catch (R) {
      U(""), X(R instanceof Error ? R.message : String(R));
    } finally {
      G("idle");
    }
  }, Kd = (x) => {
    const C = be ? x.filter((R) => R.type === "select") : x;
    C.length !== 0 && D((R) => iu(C, R));
  }, qd = (x) => {
    be || L((C) => su(x, C));
  }, ti = (x) => !x.source || !x.target || x.source === x.target || !_e ? !1 : !x.targetHandle, Ud = (x) => {
    if (!d?.state.rootActivity || !Ke || !_e || !ti(x)) return;
    const C = br(x.source, x.target, x.sourceHandle ?? "Done", x.targetHandle ?? void 0), R = cu(C, A);
    L(R), Re(V, R);
  }, Xd = () => {
    Re(V, A);
  }, Yd = !be && V.length > 0, Zd = le(() => {
    if (be || V.length === 0) return;
    const x = Ke?.slot.mode === "sequence" ? "sequence" : "flowchart", C = gb(V, A, x), R = V.map((H) => {
      const Z = C.get(H.id);
      return Z ? { ...H, position: Z } : H;
    });
    D(R), Re(R, A), window.requestAnimationFrame(() => k?.fitView({ padding: 0.2 })), U("Rearranged the canvas.");
  }, [A, V, Ke, be, Re, k]), Gd = (x, C) => {
    if (!C.nodeId || C.handleType === "target") {
      qt.current = null;
      return;
    }
    qt.current = {
      nodeId: C.nodeId,
      handleId: C.handleId ?? null
    };
  }, Jd = (x, C) => {
    const R = lN(qt.current, C);
    if (qt.current = null, !R || !_e || C.toNode || C.toHandle || cN(x)) return;
    const H = zd(x);
    P({
      kind: "fromPort",
      sourceNodeId: R.nodeId,
      sourceHandleId: R.handleId,
      clientX: H.x,
      clientY: H.y
    });
  }, Qd = (x, C) => {
    if (!_e || !ti(C)) return;
    const R = $m(x, {
      ...C,
      sourceHandle: C.sourceHandle ?? "Done",
      targetHandle: C.targetHandle ?? void 0
    }, A, { shouldReplaceId: !1 });
    L(R), Re(V, R);
  }, ef = (x) => {
    if (be || x.length === 0) return;
    const C = new Set(x.map((Z) => Z.id)), R = V.filter((Z) => !C.has(Z.id)), H = A.filter((Z) => !C.has(Z.source) && !C.has(Z.target));
    D(R), L(H), I && C.has(I) && T(null), Re(R, H);
  }, tf = (x) => {
    if (be || x.length === 0) return;
    const C = new Set(x.map((H) => H.id)), R = A.filter((H) => !C.has(H.id));
    L(R), Re(V, R);
  }, Ds = le((x) => {
    if (be) return;
    const C = A.filter((R) => R.id !== x);
    L(C), Re(V, C);
  }, [Re, A, be, V]), Ms = le((x, C, R) => {
    _e && P({ kind: "spliceEdge", edgeId: x, clientX: C, clientY: R });
  }, [_e]), nf = (x) => {
    const C = z;
    if (!C) return;
    P(null);
    const R = $n(C.clientX, C.clientY) ?? { x: 0, y: 0 };
    if (C.kind === "fromEmpty") {
      const Z = Qt(x, R), re = [...V.map((ye) => ye.selected ? { ...ye, selected: !1 } : ye), Z.node];
      D(re), T(Z.node.id), Re(re, A, [Z.activityNode]);
      return;
    }
    if (C.kind === "fromPort") {
      const Z = V.find((Me) => Me.id === C.sourceNodeId), ue = Z ? Fc(Z) : R, re = Qt(x, ue), we = [...V.map((Me) => Me.selected ? { ...Me, selected: !1 } : Me), re.node], Mt = [...A, br(C.sourceNodeId, re.node.id, C.sourceHandleId ?? "Done")];
      D(we), L(Mt), T(re.node.id), Re(we, Mt, [re.activityNode]);
      return;
    }
    const H = A.find((Z) => Z.id === C.edgeId);
    H && zn(x, H, R);
  }, of = de(() => ({
    highlightedEdgeId: K,
    deleteEdge: Ds,
    requestInsertActivity: Ms
  }), [Ds, K, Ms]), rf = (x, C, R) => {
    $((H) => [...H, { ownerNodeId: x.nodeId, slotId: C, label: R }]), T(null);
  }, Ts = le((x) => {
    h((C) => {
      const R = C?.state.rootActivity;
      return !C || !R ? C : {
        ...C,
        state: {
          ...C.state,
          rootActivity: nd(R, x.nodeId, () => x)
        }
      };
    });
  }, []), sf = le((x) => {
    if (!x) return;
    const C = d?.state.rootActivity;
    if (!C) return;
    const R = v0(C, x, (H) => {
      const Z = Ve.get(H.activityVersionId);
      return Z ? Ie(Z) : H.nodeId;
    });
    R && (So("designer"), $(R), T(x), Ce(!1));
  }, [d?.state.rootActivity, Ve]), af = (x) => {
    W((C) => {
      const R = new Set(C);
      return R.has(x) ? R.delete(x) : R.add(x), R;
    });
  }, $s = (x) => {
    ze((C) => C === x ? null : C), x === "palette" ? Ze((C) => !C) : Ce((C) => !C);
  }, Ps = (x) => {
    x === "palette" ? Ze(!1) : Ce(!1), ze((C) => C === x ? null : x);
  }, zs = (x, C) => {
    ze(null), x === "palette" ? (Ze(!1), $e((R) => or(R + C, Hn, Wn))) : (Ce(!1), Le((R) => or(R + C, Bn, Fn)));
  }, Rs = (x, C) => {
    C.preventDefault(), ze(null), x === "palette" ? Ze(!1) : Ce(!1);
    const R = C.clientX, H = x === "palette" ? ve : De, Z = x === "palette" ? Hn : Bn, ue = x === "palette" ? Wn : Fn;
    document.body.classList.add("wf-side-panel-resizing");
    const re = (we) => {
      const Mt = x === "palette" ? we.clientX - R : R - we.clientX, Me = or(H + Mt, Z, ue);
      x === "palette" ? $e(Me) : Le(Me);
    }, ye = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", re), window.removeEventListener("pointerup", ye), window.removeEventListener("pointercancel", ye);
    };
    window.addEventListener("pointermove", re), window.addEventListener("pointerup", ye), window.addEventListener("pointercancel", ye);
  }, Ls = (x, C) => {
    C.key === "ArrowLeft" ? (C.preventDefault(), zs(x, x === "palette" ? -qo : qo)) : C.key === "ArrowRight" ? (C.preventDefault(), zs(x, x === "palette" ? qo : -qo)) : C.key === "Home" ? (C.preventDefault(), x === "palette" ? $e(Hn) : Le(Bn)) : C.key === "End" && (C.preventDefault(), x === "palette" ? $e(Wn) : Le(Fn));
  };
  if (!c || !d)
    return /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: F || "Loading workflow editor..." });
  const cf = [
    "wf-editor-body",
    Pe ? "palette-collapsed" : "",
    Ae ? "inspector-collapsed" : "",
    xe === "palette" ? "palette-maximized" : "",
    xe === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), lf = {
    "--wf-palette-width": `${Pe ? Mc : ve}px`,
    "--wf-inspector-width": `${Ae ? Mc : De}px`
  }, Vs = !Pe && xe !== "inspector", Os = !Ae && xe !== "palette", zo = O?.draftSignature === qe(d) ? O.view : null, Hs = zo && J.startsWith("Test run") ? "" : J, uf = (x) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(x)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, df = {
    definition: c.definition,
    draft: d,
    selectedActivity: ge,
    selectedActivityDescriptor: At,
    selectedActivitySlots: Zt,
    catalog: g,
    currentScopeOwner: Fe,
    frames: _
  }, Ws = s.map((x) => {
    const C = x.component;
    return {
      id: x.id,
      title: x.title,
      side: x.side,
      order: x.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ i.jsx(C, { context: df })
    };
  }), ni = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ i.jsx(Lr, { size: 15 }),
      render: pf
    },
    ...Ws.filter((x) => x.side === "left")
  ].sort(Hc), oi = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ i.jsx(gs, { size: 15 }),
      render: hf
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: /* @__PURE__ */ i.jsx(vn, { size: 15 }),
      render: () => /* @__PURE__ */ i.jsx(sN, { testRun: zo, onOpenRun: uf })
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ i.jsx(Fu, { size: 15 }),
      render: () => /* @__PURE__ */ i.jsx(
        C1,
        {
          context: e,
          ai: n,
          definitionId: c.definition.id,
          publishedArtifactId: te
        }
      )
    },
    ...Ws.filter((x) => x.side === "right")
  ].sort(Hc), Bs = ni.find((x) => x.id === lt) ?? ni[0], Fs = oi.find((x) => x.id === kn) ?? oi[0], ff = [
    { id: "designer", title: "Designer", order: 0, icon: /* @__PURE__ */ i.jsx(Ww, { size: 14 }), render: () => null },
    { id: "code", title: "Code", order: 1, icon: /* @__PURE__ */ i.jsx($w, { size: 14 }), render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: /* @__PURE__ */ i.jsx(ms, { size: 14 }), render: () => null }
  ];
  function pf() {
    const x = ee.trim().length > 0;
    return /* @__PURE__ */ i.jsxs("div", { className: "wf-palette-body", children: [
      /* @__PURE__ */ i.jsxs("label", { className: "wf-palette-search", children: [
        /* @__PURE__ */ i.jsx(Vr, { size: 14, "aria-hidden": "true" }),
        /* @__PURE__ */ i.jsx(
          "input",
          {
            type: "search",
            value: ee,
            placeholder: "Search activities",
            "aria-label": "Search activity palette",
            onChange: (C) => me(C.target.value)
          }
        )
      ] }),
      /* @__PURE__ */ i.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: Io.length === 0 ? /* @__PURE__ */ i.jsx("p", { className: "wf-muted wf-palette-empty", children: "No matching activities." }) : Io.map((C) => {
        const R = x || fe.has(C.category);
        return /* @__PURE__ */ i.jsxs("div", { className: "wf-palette-category", children: [
          /* @__PURE__ */ i.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-category-toggle",
              role: "treeitem",
              "aria-expanded": R,
              onClick: () => af(C.category),
              children: [
                R ? /* @__PURE__ */ i.jsx(Hu, { size: 14 }) : /* @__PURE__ */ i.jsx(sn, { size: 14 }),
                /* @__PURE__ */ i.jsx("span", { children: C.category }),
                /* @__PURE__ */ i.jsx("small", { children: C.activities.length })
              ]
            }
          ),
          R ? /* @__PURE__ */ i.jsx("div", { className: "wf-palette-activities", role: "group", children: C.activities.map((H) => {
            const Z = H.description?.trim(), ue = Z ? `wf-palette-description-${H.activityVersionId}` : void 0, re = Ie(H), ye = vr(H);
            return /* @__PURE__ */ i.jsxs(
              "button",
              {
                type: "button",
                className: "wf-palette-activity",
                role: "treeitem",
                draggable: !0,
                title: Z || Ie(H),
                "aria-describedby": ue,
                onClick: () => Zr(H),
                onDragStart: (we) => Do(we, H),
                onDragEnd: (we) => Xr(we, H),
                onPointerDown: (we) => Yr(we, H),
                children: [
                  /* @__PURE__ */ i.jsx("span", { className: "wf-activity-icon", "data-icon": ye, "aria-hidden": "true", children: Ss(ye) }),
                  /* @__PURE__ */ i.jsxs("span", { className: "wf-palette-activity-text", children: [
                    /* @__PURE__ */ i.jsx("strong", { children: re }),
                    Z ? /* @__PURE__ */ i.jsx("small", { id: ue, children: Z }) : null
                  ] }),
                  /* @__PURE__ */ i.jsx(Bu, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
                ]
              },
              H.activityVersionId
            );
          }) }) : null
        ] }, C.category);
      }) })
    ] });
  }
  function hf() {
    return ge ? /* @__PURE__ */ i.jsxs("div", { className: "wf-inspector-content", children: [
      /* @__PURE__ */ i.jsx("h3", { children: V.find((x) => x.id === ge.nodeId)?.data.label ?? ge.nodeId }),
      /* @__PURE__ */ i.jsxs("dl", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Node ID" }),
        /* @__PURE__ */ i.jsx("dd", { children: ge.nodeId }),
        /* @__PURE__ */ i.jsx("dt", { children: "Activity type" }),
        /* @__PURE__ */ i.jsx("dd", { children: At?.typeName ?? Ve.get(ge.activityVersionId)?.activityTypeKey ?? "Unknown" }),
        /* @__PURE__ */ i.jsx("dt", { children: "Activity version" }),
        /* @__PURE__ */ i.jsx("dd", { children: ge.activityVersionId })
      ] }),
      Mn ? /* @__PURE__ */ i.jsxs("div", { className: "wf-availability-notice", children: [
        /* @__PURE__ */ i.jsx(xr, { size: 14 }),
        /* @__PURE__ */ i.jsxs("span", { children: [
          "No longer available for new use · ",
          jr(Mn.state)
        ] })
      ] }) : null,
      /* @__PURE__ */ i.jsx(
        Jv,
        {
          activity: ge,
          descriptor: At,
          editors: o,
          expressionEditors: r,
          expressionDescriptors: b,
          descriptorStatus: v,
          visibleVariables: Gt.visibleVariables,
          scopeStatus: Gt.status,
          onChange: Ts
        }
      ),
      F0(ge) ? /* @__PURE__ */ i.jsx("div", { className: "wf-container-variables", children: /* @__PURE__ */ i.jsx(
        Hb,
        {
          context: e,
          variables: sd(ge),
          title: "Container variables",
          addLabel: "Add container variable",
          emptyLabel: "No container variables declared on this activity.",
          warnings: X0(Gt.shadowingWarnings, ge.nodeId),
          onChange: (x) => Ts(K0(ge, x))
        }
      ) }) : null,
      Zt.length > 0 ? /* @__PURE__ */ i.jsxs("div", { className: "wf-slot-list", children: [
        /* @__PURE__ */ i.jsx("span", { children: "Embedded slots" }),
        Zt.map((x) => /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => rf(ge, x.id, `${V.find((C) => C.id === ge.nodeId)?.data.label ?? ge.nodeId} / ${x.label}`), children: [
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
      /* @__PURE__ */ i.jsx(sn, { size: 14 }),
      /* @__PURE__ */ i.jsx("strong", { children: c.definition.name }),
      /* @__PURE__ */ i.jsx("span", { className: "wf-chip", children: "Draft" }),
      Hs ? /* @__PURE__ */ i.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ i.jsx(jn, { size: 13 }),
        " ",
        Hs
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
              disabled: !Od,
              onClick: Rn,
              children: /* @__PURE__ */ i.jsx(Hw, { size: 16 })
            }
          ),
          /* @__PURE__ */ i.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Redo",
              title: "Redo (Ctrl+Shift+Z)",
              disabled: !Hd,
              onClick: on,
              children: /* @__PURE__ */ i.jsx(Vw, { size: 16 })
            }
          ),
          /* @__PURE__ */ i.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Auto-layout",
              title: "Auto-layout the canvas",
              disabled: !Yd,
              onClick: Zd,
              children: /* @__PURE__ */ i.jsx(Lw, { size: 16 })
            }
          )
        ] }),
        /* @__PURE__ */ i.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ i.jsx("input", { className: "wf-autosave-switch-input", type: "checkbox", checked: se, onChange: (x) => ae(x.target.checked) }),
          /* @__PURE__ */ i.jsx("span", { children: "Autosave" })
        ] }),
        Ao ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => Kt(n, Ao, { definition: c.definition, draft: d }), children: [
          /* @__PURE__ */ i.jsx(gt, { size: 15 }),
          " Risks"
        ] }) : null,
        _o ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => Kt(n, _o, { definition: c.definition, draft: d }), children: [
          /* @__PURE__ */ i.jsx(gt, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ i.jsxs("button", { type: "button", title: "Export workflow as JSON", onClick: Po, children: [
          /* @__PURE__ */ i.jsx(zw, { size: 15 }),
          " Export"
        ] }),
        /* @__PURE__ */ i.jsxs("button", { type: "button", disabled: xt, onClick: () => {
          Wd();
        }, children: [
          /* @__PURE__ */ i.jsx(Ku, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ i.jsxs("button", { type: "button", disabled: xt, onClick: () => {
          Bd();
        }, children: [
          /* @__PURE__ */ i.jsx(Wu, { size: 15 }),
          " Promote"
        ] }),
        zo ? /* @__PURE__ */ i.jsx(
          iN,
          {
            testRun: zo,
            onOpenDetails: () => {
              En("runtime"), Ce(!1);
            }
          }
        ) : null,
        /* @__PURE__ */ i.jsxs(
          "button",
          {
            type: "button",
            disabled: !qr,
            title: d.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              Fd();
            },
            children: [
              /* @__PURE__ */ i.jsx(vn, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    F ? /* @__PURE__ */ i.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ i.jsx(kt, { size: 16 }),
      " ",
      F
    ] }) : null,
    /* @__PURE__ */ i.jsxs("div", { className: cf, style: lf, children: [
      /* @__PURE__ */ i.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ i.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ i.jsx(
            rr,
            {
              label: "Activities panel tabs",
              tabs: ni,
              activeTabId: Bs.id,
              onSelect: Fr
            }
          ),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": Pe ? "Expand activities panel" : "Collapse activities panel",
                title: Pe ? "Expand" : "Collapse",
                onClick: () => $s("palette"),
                children: Pe ? /* @__PURE__ */ i.jsx(sn, { size: 14 }) : /* @__PURE__ */ i.jsx(yr, { size: 14 })
              }
            ),
            Pe ? null : /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": xe === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: xe === "palette" ? "Restore" : "Maximize",
                onClick: () => Ps("palette"),
                children: xe === "palette" ? /* @__PURE__ */ i.jsx(nc, { size: 14 }) : /* @__PURE__ */ i.jsx(mr, { size: 14 })
              }
            )
          ] })
        ] }),
        Vs ? Bs.render() : null
      ] }),
      Vs && !xe ? /* @__PURE__ */ i.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Hn,
          "aria-valuemax": Wn,
          "aria-valuenow": ve,
          tabIndex: 0,
          onPointerDown: (x) => Rs("palette", x),
          onKeyDown: (x) => Ls("palette", x)
        }
      ) : /* @__PURE__ */ i.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ i.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ i.jsx("div", { className: "wf-canvas-tabs", children: /* @__PURE__ */ i.jsx(
          rr,
          {
            label: "Editor view tabs",
            tabs: ff,
            activeTabId: Et,
            onSelect: (x) => So(x)
          }
        ) }),
        Et === "code" ? /* @__PURE__ */ i.jsx(Cb, { draft: d, onApply: ei }) : Et === "properties" ? /* @__PURE__ */ i.jsx(Bb, { details: c, draft: d, context: e, onStateChange: Jr, onDefinitionMetaChange: Qr }) : /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
          /* @__PURE__ */ i.jsxs("div", { className: "wf-breadcrumb", children: [
            /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => {
              $([]), T(null);
            }, children: "Root" }),
            _.map((x, C) => /* @__PURE__ */ i.jsxs(st.Fragment, { children: [
              /* @__PURE__ */ i.jsx(sn, { size: 13 }),
              /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => {
                $(_.slice(0, C + 1)), T(null);
              }, children: x.label })
            ] }, `${x.ownerNodeId}-${x.slotId}-${C}`))
          ] }),
          /* @__PURE__ */ i.jsxs("div", { className: "wf-canvas", ref: Ge, onDragOver: Mo, onDragLeave: To, onDrop: $o, children: [
            /* @__PURE__ */ i.jsx(Id.Provider, { value: of, children: /* @__PURE__ */ i.jsx(Ad.Provider, { value: Dn, children: /* @__PURE__ */ i.jsxs(
              Tu,
              {
                nodes: V,
                edges: A,
                nodeTypes: kd,
                edgeTypes: Ed,
                onInit: M,
                onNodesChange: Kd,
                onEdgesChange: qd,
                onNodesDelete: ef,
                onEdgesDelete: tf,
                onConnect: Ud,
                onConnectStart: _e ? Gd : void 0,
                onConnectEnd: _e ? Jd : void 0,
                onReconnect: _e ? Qd : void 0,
                isValidConnection: ti,
                onDragOver: Mo,
                onDragLeave: To,
                onDrop: $o,
                onPaneClick: () => T(null),
                onNodeClick: (x, C) => T(C.id),
                onNodeDragStop: be ? void 0 : Xd,
                fitView: !0,
                minZoom: 0.2,
                maxZoom: 1.8,
                nodesConnectable: _e,
                nodesDraggable: !be,
                selectionOnDrag: !0,
                multiSelectionKeyCode: ["Shift", "Meta", "Control"],
                deleteKeyCode: be ? null : ["Backspace", "Delete"],
                panActivationKeyCode: null,
                defaultEdgeOptions: { type: "workflow" },
                children: [
                  /* @__PURE__ */ i.jsx(Pu, { gap: 18, size: 1 }),
                  /* @__PURE__ */ i.jsx(Ru, {}),
                  /* @__PURE__ */ i.jsx(Vu, { pannable: !0, zoomable: !0 })
                ]
              }
            ) }) }),
            _e && V.length === 0 ? /* @__PURE__ */ i.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => Gr(), children: [
              /* @__PURE__ */ i.jsx(bn, { size: 15 }),
              " Add activity"
            ] }) : null,
            z ? /* @__PURE__ */ i.jsx(
              oN,
              {
                clientX: z.clientX,
                clientY: z.clientY,
                activities: g,
                onPick: nf,
                onClose: () => P(null)
              }
            ) : null
          ] }),
          /* @__PURE__ */ i.jsx(rN, { draft: d, onRepair: sf })
        ] })
      ] }),
      Os && !xe ? /* @__PURE__ */ i.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Bn,
          "aria-valuemax": Fn,
          "aria-valuenow": De,
          tabIndex: 0,
          onPointerDown: (x) => Rs("inspector", x),
          onKeyDown: (x) => Ls("inspector", x)
        }
      ) : /* @__PURE__ */ i.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ i.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ i.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ i.jsx(
            rr,
            {
              label: "Inspector panel tabs",
              tabs: oi,
              activeTabId: Fs.id,
              onSelect: En
            }
          ),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": Ae ? "Expand inspector panel" : "Collapse inspector panel",
                title: Ae ? "Expand" : "Collapse",
                onClick: () => $s("inspector"),
                children: Ae ? /* @__PURE__ */ i.jsx(yr, { size: 14 }) : /* @__PURE__ */ i.jsx(sn, { size: 14 })
              }
            ),
            Ae ? null : /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": xe === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: xe === "inspector" ? "Restore" : "Maximize",
                onClick: () => Ps("inspector"),
                children: xe === "inspector" ? /* @__PURE__ */ i.jsx(nc, { size: 14 }) : /* @__PURE__ */ i.jsx(mr, { size: 14 })
              }
            )
          ] })
        ] }),
        Os ? Fs.render() : null
      ] })
    ] })
  ] });
}
function rr({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: o
}) {
  return /* @__PURE__ */ i.jsx("div", { className: "wf-panel-tab-list", role: "tablist", "aria-label": e, children: t.map((r) => /* @__PURE__ */ i.jsxs(
    "button",
    {
      type: "button",
      role: "tab",
      "aria-selected": r.id === n,
      className: r.id === n ? "active" : "",
      title: r.title,
      onClick: () => o(r.id),
      children: [
        r.icon ? /* @__PURE__ */ i.jsx("span", { className: "wf-panel-tab-icon", "aria-hidden": "true", children: r.icon }) : null,
        /* @__PURE__ */ i.jsx("span", { children: r.title })
      ]
    },
    r.id
  )) });
}
function Hc(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
function eN({ data: e, selected: t }) {
  const n = e, o = n.runtime, r = !n.suppressFlowPorts, s = r ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], a = tN(n), u = st.useContext(Ad)?.({ activityVersionId: n.activityVersionId, activityTypeKey: n.activityTypeKey }) ?? null;
  return /* @__PURE__ */ i.jsxs(
    "div",
    {
      className: ["wf-node", t ? "selected" : "", o ? "wf-node-runtime" : "", o?.hasBlockingIncident ? "faulted" : "", u ? "wf-node-unavailable" : ""].filter(Boolean).join(" "),
      "data-icon": n.icon ?? "activity",
      children: [
        r && n.acceptsInbound ? /* @__PURE__ */ i.jsx(wn, { type: "target", position: ne.Left }) : null,
        u ? /* @__PURE__ */ i.jsx("span", { className: "wf-node-availability", title: `No longer available for new use · ${jr(u.state)}`, children: /* @__PURE__ */ i.jsx(xr, { size: 13 }) }) : null,
        /* @__PURE__ */ i.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ i.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: Ss(n.icon) }),
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
        o ? /* @__PURE__ */ i.jsxs("div", { className: "wf-node-runtime-strip", children: [
          o.status ? /* @__PURE__ */ i.jsx(jo, { status: o.status, subStatus: o.subStatus }) : null,
          o.incidentCount > 0 ? /* @__PURE__ */ i.jsxs("span", { className: "wf-node-runtime-count", children: [
            o.incidentCount,
            " incident",
            o.incidentCount === 1 ? "" : "s"
          ] }) : null,
          o.faultCount > 0 ? /* @__PURE__ */ i.jsxs("span", { className: "wf-node-runtime-count", children: [
            o.faultCount,
            " faults"
          ] }) : null
        ] }) : null,
        s.map((l, f) => {
          const p = `${(f + 1) / (s.length + 1) * 100}%`;
          return /* @__PURE__ */ i.jsxs(st.Fragment, { children: [
            /* @__PURE__ */ i.jsx("span", { className: "wf-node-port-label", style: { top: p }, children: l.displayName }),
            /* @__PURE__ */ i.jsx(wn, { type: "source", position: ne.Right, id: l.name, style: { top: p } })
          ] }, l.name);
        })
      ]
    }
  );
}
function tN(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((r) => !!r).join(" · ");
}
function nN(e) {
  const {
    id: t,
    sourceX: n,
    sourceY: o,
    targetX: r,
    targetY: s,
    sourcePosition: a,
    targetPosition: c,
    markerEnd: u,
    style: l,
    label: f,
    labelStyle: p
  } = e, d = st.useContext(Id), [h, g] = q(!1), [N, w, m] = hr({ sourceX: n, sourceY: o, targetX: r, targetY: s, sourcePosition: a, targetPosition: c }), E = d?.highlightedEdgeId === t;
  return /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
    /* @__PURE__ */ i.jsx(
      wo,
      {
        id: t,
        path: N,
        markerEnd: u,
        style: {
          ...l,
          strokeWidth: E ? 2.5 : l?.strokeWidth
        },
        label: f,
        labelX: w,
        labelY: m,
        labelStyle: p,
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1)
      }
    ),
    d ? /* @__PURE__ */ i.jsx(ow, { children: /* @__PURE__ */ i.jsxs(
      "div",
      {
        className: ["wf-edge-actions", h ? "visible" : "", E ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${w}px, ${m}px)` },
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1),
        children: [
          /* @__PURE__ */ i.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (y) => d.requestInsertActivity(t, y.clientX, y.clientY), children: /* @__PURE__ */ i.jsx(bn, { size: 12 }) }),
          /* @__PURE__ */ i.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => d.deleteEdge(t), children: /* @__PURE__ */ i.jsx(uo, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function oN({ clientX: e, clientY: t, activities: n, onPick: o, onClose: r }) {
  const [s, a] = q(""), [c, u] = q(0), l = ie(null), f = ie(null), p = de(() => {
    const E = s.trim().toLowerCase(), y = n.filter(F1);
    return E ? y.filter((b) => Ie(b).toLowerCase().includes(E) || b.activityTypeKey.toLowerCase().includes(E) || (b.category ?? "").toLowerCase().includes(E) || (b.description ?? "").toLowerCase().includes(E)) : y;
  }, [n, s]), d = de(() => Xi(p), [p]), h = de(() => d.flatMap((E) => E.activities), [d]);
  Q(() => {
    requestAnimationFrame(() => f.current?.focus());
  }, []), Q(() => {
    const E = (b) => {
      l.current?.contains(b.target) || r();
    }, y = (b) => {
      b.key === "Escape" && r();
    };
    return document.addEventListener("mousedown", E, !0), document.addEventListener("keydown", y), () => {
      document.removeEventListener("mousedown", E, !0), document.removeEventListener("keydown", y);
    };
  }, [r]);
  const g = (E) => {
    if (E.key === "ArrowDown")
      E.preventDefault(), u((y) => Math.min(y + 1, h.length - 1));
    else if (E.key === "ArrowUp")
      E.preventDefault(), u((y) => Math.max(y - 1, 0));
    else if (E.key === "Enter") {
      E.preventDefault();
      const y = h[c];
      y && o(y);
    }
  }, N = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), w = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let m = -1;
  return /* @__PURE__ */ i.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: N, top: w }, onMouseDown: (E) => E.stopPropagation(), onClick: (E) => E.stopPropagation(), children: [
    /* @__PURE__ */ i.jsx(
      "input",
      {
        ref: f,
        type: "search",
        value: s,
        placeholder: "Search activities...",
        "aria-label": "Search activities",
        onChange: (E) => {
          a(E.target.value), u(0);
        },
        onKeyDown: g
      }
    ),
    /* @__PURE__ */ i.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: d.length === 0 ? /* @__PURE__ */ i.jsx("p", { children: "No matching activities." }) : d.map((E) => /* @__PURE__ */ i.jsxs("section", { children: [
      /* @__PURE__ */ i.jsx("h4", { children: E.category }),
      E.activities.map((y) => {
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
            onClick: () => o(y),
            children: [
              /* @__PURE__ */ i.jsx("strong", { children: Ie(y) }),
              /* @__PURE__ */ i.jsx("small", { children: y.category || y.activityTypeKey })
            ]
          },
          y.activityVersionId
        );
      })
    ] }, E.category)) })
  ] });
}
function rN({ draft: e, onRepair: t }) {
  const n = e.validationErrors;
  if (!n.length)
    return /* @__PURE__ */ i.jsxs("div", { className: "wf-validation ok", children: [
      /* @__PURE__ */ i.jsx(jn, { size: 14 }),
      " No validation errors"
    ] });
  const o = Xb(n), r = new Map(o.map((s) => [s.error, s]));
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-validation-summary", children: [
      /* @__PURE__ */ i.jsx(kt, { size: 14 }),
      n.length,
      " validation issue",
      n.length === 1 ? "" : "s",
      o.length > 0 ? /* @__PURE__ */ i.jsxs("span", { className: "wf-validation-variable-count", children: [
        " · ",
        o.length,
        " invalid variable reference",
        o.length === 1 ? "" : "s"
      ] }) : null
    ] }),
    /* @__PURE__ */ i.jsx("ul", { className: "wf-validation-list", children: n.map((s, a) => {
      const c = r.get(s);
      return /* @__PURE__ */ i.jsxs("li", { className: c ? "wf-validation-item repairable" : "wf-validation-item", children: [
        /* @__PURE__ */ i.jsx("span", { className: "wf-validation-message", children: s.message ?? "Validation issue." }),
        c?.path.nodeId ? /* @__PURE__ */ i.jsxs("button", { type: "button", className: "wf-validation-repair", onClick: () => t(c.path.nodeId), children: [
          /* @__PURE__ */ i.jsx(Bw, { size: 12 }),
          " Repair"
        ] }) : null
      ] }, a);
    }) })
  ] });
}
function iN({
  testRun: e,
  onOpenDetails: t
}) {
  const n = _s(e);
  return /* @__PURE__ */ i.jsx("div", { className: "wf-test-run-status", "data-state": n ? "rejected" : "accepted", children: /* @__PURE__ */ i.jsxs(
    "button",
    {
      type: "button",
      className: "wf-test-run-trigger",
      onClick: t,
      children: [
        n ? /* @__PURE__ */ i.jsx(kt, { size: 16 }) : /* @__PURE__ */ i.jsx(jn, { size: 16 }),
        n ? "Test run rejected" : "Test run dispatched"
      ]
    }
  ) });
}
function sN({ testRun: e, onOpenRun: t }) {
  if (!e)
    return /* @__PURE__ */ i.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "Run the draft to see Runtime Evidence." }) });
  const n = _s(e), o = e.workflowExecutionId;
  return /* @__PURE__ */ i.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ i.jsxs("section", { className: "wf-runtime-card", "data-state": n ? "rejected" : "accepted", children: [
    /* @__PURE__ */ i.jsxs("header", { children: [
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("span", { children: "Latest Test Run" }),
        /* @__PURE__ */ i.jsx("h3", { children: n ? "Rejected by the server" : "Transient run accepted" })
      ] }),
      /* @__PURE__ */ i.jsx(jo, { status: e.status, subStatus: e.commandDispatchStatus ?? void 0 })
    ] }),
    /* @__PURE__ */ i.jsx("p", { children: "Ephemeral - not saved, promoted, or published." }),
    n && e.reason ? /* @__PURE__ */ i.jsxs("div", { className: "wf-runtime-reason", children: [
      /* @__PURE__ */ i.jsx(kt, { size: 14 }),
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
        /* @__PURE__ */ i.jsx("dd", { title: o ?? "None", children: o ? /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => t(o), children: o }) : "None" })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Activities" }),
        /* @__PURE__ */ i.jsx("dd", { children: Wc(e.activityCount, "activity") })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ i.jsx("dd", { children: Wc(e.incidentCount, "incident") })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ i.jsx("dd", { title: e.expiresAt ? nt(e.expiresAt) : "None", children: e.expiresAt ? nt(e.expiresAt) : "None" })
      ] })
    ] })
  ] }) });
}
function Wc(e, t) {
  return typeof e != "number" ? "Available on linked Run" : `${e} ${t}${e === 1 ? "" : "s"}`;
}
function Bc(e) {
  return `${Ie(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function Fc(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function aN(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function zd(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function cN(e) {
  const t = zd(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function lN(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function qe(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function uN(e) {
  return Vd(qe(e));
}
function Rd(e, t, n = []) {
  if (!e) return n;
  const o = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: o?.activityTypeKey ?? e.activityVersionId,
    displayName: o ? Ie(o) : void 0
  });
  for (const r of We(e))
    for (const s of r.activities) Rd(s, t, n);
  return n;
}
function Ld(e, t = []) {
  if (!e) return t;
  for (const n of rd(e))
    t.push({ source: n.source, target: n.target, sourcePort: n.sourceHandle ?? void 0, targetPort: n.targetHandle ?? void 0 });
  for (const n of We(e))
    for (const o of n.activities) Ld(o, t);
  return t;
}
function qn(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function dN(e) {
  return `${e.id}-${Vd(JSON.stringify(e.state))}`;
}
function Vd(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function _s(e) {
  return e.status.toLowerCase() === "rejected";
}
function fN(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function pN(e, t) {
  const n = e instanceof Error ? e.message : String(e);
  return hN(e, n) ? `Run ${t} was not found.` : n;
}
function hN(e, t) {
  if ((typeof e == "object" && e ? e.response?.status ?? e.status : void 0) === 404 || /\b404\b/.test(t)) return !0;
  try {
    const o = JSON.parse(t);
    return [o.error, o.title, o.detail].some((r) => typeof r == "string" && /not found/i.test(r));
  } catch {
    return /not found/i.test(t);
  }
}
export {
  cN as isConnectEndOverExistingWorkflowNode,
  mN as register,
  lN as resolveConnectEndSource
};
