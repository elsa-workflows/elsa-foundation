import zt, { memo as he, forwardRef as mn, useRef as ie, useEffect as se, useCallback as me, useContext as Rt, useMemo as ye, createContext as mo, useState as te, useLayoutEffect as nc, createElement as Gn } from "react";
function oc(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var jn = { exports: {} }, xt = {};
var Wo;
function rc() {
  if (Wo) return xt;
  Wo = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function n(o, r, i) {
    var s = null;
    if (i !== void 0 && (s = "" + i), r.key !== void 0 && (s = "" + r.key), "key" in r) {
      i = {};
      for (var a in r)
        a !== "key" && (i[a] = r[a]);
    } else i = r;
    return r = i.ref, {
      $$typeof: e,
      type: o,
      key: s,
      ref: r !== void 0 ? r : null,
      props: i
    };
  }
  return xt.Fragment = t, xt.jsx = n, xt.jsxs = n, xt;
}
var qo;
function ic() {
  return qo || (qo = 1, jn.exports = rc()), jn.exports;
}
var h = ic();
function ge(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, o; n < e.length; n++)
      (o = ge(e[n])) !== "" && (t += (t && " ") + o);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var sc = { value: () => {
} };
function yn() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new Jt(n);
}
function Jt(e) {
  this._ = e;
}
function ac(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
Jt.prototype = yn.prototype = {
  constructor: Jt,
  on: function(e, t) {
    var n = this._, o = ac(e + "", n), r, i = -1, s = o.length;
    if (arguments.length < 2) {
      for (; ++i < s; ) if ((r = (e = o[i]).type) && (r = cc(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++i < s; )
      if (r = (e = o[i]).type) n[r] = Zo(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = Zo(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new Jt(e);
  },
  call: function(e, t) {
    if ((r = arguments.length - 2) > 0) for (var n = new Array(r), o = 0, r, i; o < r; ++o) n[o] = arguments[o + 2];
    if (!this._.hasOwnProperty(e)) throw new Error("unknown type: " + e);
    for (i = this._[e], o = 0, r = i.length; o < r; ++o) i[o].value.apply(t, n);
  },
  apply: function(e, t, n) {
    if (!this._.hasOwnProperty(e)) throw new Error("unknown type: " + e);
    for (var o = this._[e], r = 0, i = o.length; r < i; ++r) o[r].value.apply(t, n);
  }
};
function cc(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function Zo(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = sc, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var Kn = "http://www.w3.org/1999/xhtml";
const Uo = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: Kn,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function xn(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Uo.hasOwnProperty(t) ? { space: Uo[t], local: e } : e;
}
function lc(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === Kn && t.documentElement.namespaceURI === Kn ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function uc(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function ai(e) {
  var t = xn(e);
  return (t.local ? uc : lc)(t);
}
function dc() {
}
function yo(e) {
  return e == null ? dc : function() {
    return this.querySelector(e);
  };
}
function fc(e) {
  typeof e != "function" && (e = yo(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = new Array(s), l, c, u = 0; u < s; ++u)
      (l = i[u]) && (c = e.call(l, l.__data__, u, i)) && ("__data__" in l && (c.__data__ = l.__data__), a[u] = c);
  return new Se(o, this._parents);
}
function hc(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function gc() {
  return [];
}
function ci(e) {
  return e == null ? gc : function() {
    return this.querySelectorAll(e);
  };
}
function pc(e) {
  return function() {
    return hc(e.apply(this, arguments));
  };
}
function mc(e) {
  typeof e == "function" ? e = pc(e) : e = ci(e);
  for (var t = this._groups, n = t.length, o = [], r = [], i = 0; i < n; ++i)
    for (var s = t[i], a = s.length, l, c = 0; c < a; ++c)
      (l = s[c]) && (o.push(e.call(l, l.__data__, c, s)), r.push(l));
  return new Se(o, r);
}
function li(e) {
  return function() {
    return this.matches(e);
  };
}
function ui(e) {
  return function(t) {
    return t.matches(e);
  };
}
var yc = Array.prototype.find;
function xc(e) {
  return function() {
    return yc.call(this.children, e);
  };
}
function wc() {
  return this.firstElementChild;
}
function vc(e) {
  return this.select(e == null ? wc : xc(typeof e == "function" ? e : ui(e)));
}
var bc = Array.prototype.filter;
function Sc() {
  return Array.from(this.children);
}
function _c(e) {
  return function() {
    return bc.call(this.children, e);
  };
}
function Ec(e) {
  return this.selectAll(e == null ? Sc : _c(typeof e == "function" ? e : ui(e)));
}
function Nc(e) {
  typeof e != "function" && (e = li(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = [], l, c = 0; c < s; ++c)
      (l = i[c]) && e.call(l, l.__data__, c, i) && a.push(l);
  return new Se(o, this._parents);
}
function di(e) {
  return new Array(e.length);
}
function Cc() {
  return new Se(this._enter || this._groups.map(di), this._parents);
}
function rn(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
rn.prototype = {
  constructor: rn,
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
function kc(e) {
  return function() {
    return e;
  };
}
function Ic(e, t, n, o, r, i) {
  for (var s = 0, a, l = t.length, c = i.length; s < c; ++s)
    (a = t[s]) ? (a.__data__ = i[s], o[s] = a) : n[s] = new rn(e, i[s]);
  for (; s < l; ++s)
    (a = t[s]) && (r[s] = a);
}
function Mc(e, t, n, o, r, i, s) {
  var a, l, c = /* @__PURE__ */ new Map(), u = t.length, d = i.length, f = new Array(u), g;
  for (a = 0; a < u; ++a)
    (l = t[a]) && (f[a] = g = s.call(l, l.__data__, a, t) + "", c.has(g) ? r[a] = l : c.set(g, l));
  for (a = 0; a < d; ++a)
    g = s.call(e, i[a], a, i) + "", (l = c.get(g)) ? (o[a] = l, l.__data__ = i[a], c.delete(g)) : n[a] = new rn(e, i[a]);
  for (a = 0; a < u; ++a)
    (l = t[a]) && c.get(f[a]) === l && (r[a] = l);
}
function Ac(e) {
  return e.__data__;
}
function Dc(e, t) {
  if (!arguments.length) return Array.from(this, Ac);
  var n = t ? Mc : Ic, o = this._parents, r = this._groups;
  typeof e != "function" && (e = kc(e));
  for (var i = r.length, s = new Array(i), a = new Array(i), l = new Array(i), c = 0; c < i; ++c) {
    var u = o[c], d = r[c], f = d.length, g = Pc(e.call(u, u && u.__data__, c, o)), p = g.length, v = a[c] = new Array(p), x = s[c] = new Array(p), y = l[c] = new Array(f);
    n(u, d, v, x, y, g, t);
    for (var _ = 0, m = 0, w, C; _ < p; ++_)
      if (w = v[_]) {
        for (_ >= m && (m = _ + 1); !(C = x[m]) && ++m < p; ) ;
        w._next = C || null;
      }
  }
  return s = new Se(s, o), s._enter = a, s._exit = l, s;
}
function Pc(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function $c() {
  return new Se(this._exit || this._groups.map(di), this._parents);
}
function jc(e, t, n) {
  var o = this.enter(), r = this, i = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? i.remove() : n(i), o && r ? o.merge(r).order() : r;
}
function Tc(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, i = o.length, s = Math.min(r, i), a = new Array(r), l = 0; l < s; ++l)
    for (var c = n[l], u = o[l], d = c.length, f = a[l] = new Array(d), g, p = 0; p < d; ++p)
      (g = c[p] || u[p]) && (f[p] = g);
  for (; l < r; ++l)
    a[l] = n[l];
  return new Se(a, this._parents);
}
function zc() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, i = o[r], s; --r >= 0; )
      (s = o[r]) && (i && s.compareDocumentPosition(i) ^ 4 && i.parentNode.insertBefore(s, i), i = s);
  return this;
}
function Rc(e) {
  e || (e = Lc);
  function t(d, f) {
    return d && f ? e(d.__data__, f.__data__) : !d - !f;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), i = 0; i < o; ++i) {
    for (var s = n[i], a = s.length, l = r[i] = new Array(a), c, u = 0; u < a; ++u)
      (c = s[u]) && (l[u] = c);
    l.sort(t);
  }
  return new Se(r, this._parents).order();
}
function Lc(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function Hc() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function Oc() {
  return Array.from(this);
}
function Vc() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length; r < i; ++r) {
      var s = o[r];
      if (s) return s;
    }
  return null;
}
function Bc() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function Fc() {
  return !this.node();
}
function Yc(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], i = 0, s = r.length, a; i < s; ++i)
      (a = r[i]) && e.call(a, a.__data__, i, r);
  return this;
}
function Xc(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Wc(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function qc(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function Zc(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function Uc(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function Gc(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function Kc(e, t) {
  var n = xn(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? Wc : Xc : typeof t == "function" ? n.local ? Gc : Uc : n.local ? Zc : qc)(n, t));
}
function fi(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function Qc(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Jc(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function el(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function tl(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? Qc : typeof t == "function" ? el : Jc)(e, t, n ?? "")) : lt(this.node(), e);
}
function lt(e, t) {
  return e.style.getPropertyValue(t) || fi(e).getComputedStyle(e, null).getPropertyValue(t);
}
function nl(e) {
  return function() {
    delete this[e];
  };
}
function ol(e, t) {
  return function() {
    this[e] = t;
  };
}
function rl(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function il(e, t) {
  return arguments.length > 1 ? this.each((t == null ? nl : typeof t == "function" ? rl : ol)(e, t)) : this.node()[e];
}
function hi(e) {
  return e.trim().split(/^|\s+/);
}
function xo(e) {
  return e.classList || new gi(e);
}
function gi(e) {
  this._node = e, this._names = hi(e.getAttribute("class") || "");
}
gi.prototype = {
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
function pi(e, t) {
  for (var n = xo(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function mi(e, t) {
  for (var n = xo(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function sl(e) {
  return function() {
    pi(this, e);
  };
}
function al(e) {
  return function() {
    mi(this, e);
  };
}
function cl(e, t) {
  return function() {
    (t.apply(this, arguments) ? pi : mi)(this, e);
  };
}
function ll(e, t) {
  var n = hi(e + "");
  if (arguments.length < 2) {
    for (var o = xo(this.node()), r = -1, i = n.length; ++r < i; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? cl : t ? sl : al)(n, t));
}
function ul() {
  this.textContent = "";
}
function dl(e) {
  return function() {
    this.textContent = e;
  };
}
function fl(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function hl(e) {
  return arguments.length ? this.each(e == null ? ul : (typeof e == "function" ? fl : dl)(e)) : this.node().textContent;
}
function gl() {
  this.innerHTML = "";
}
function pl(e) {
  return function() {
    this.innerHTML = e;
  };
}
function ml(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function yl(e) {
  return arguments.length ? this.each(e == null ? gl : (typeof e == "function" ? ml : pl)(e)) : this.node().innerHTML;
}
function xl() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function wl() {
  return this.each(xl);
}
function vl() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function bl() {
  return this.each(vl);
}
function Sl(e) {
  var t = typeof e == "function" ? e : ai(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function _l() {
  return null;
}
function El(e, t) {
  var n = typeof e == "function" ? e : ai(e), o = t == null ? _l : typeof t == "function" ? t : yo(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function Nl() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Cl() {
  return this.each(Nl);
}
function kl() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Il() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Ml(e) {
  return this.select(e ? Il : kl);
}
function Al(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function Dl(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function Pl(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function $l(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, i; n < r; ++n)
        i = t[n], (!e.type || i.type === e.type) && i.name === e.name ? this.removeEventListener(i.type, i.listener, i.options) : t[++o] = i;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function jl(e, t, n) {
  return function() {
    var o = this.__on, r, i = Dl(t);
    if (o) {
      for (var s = 0, a = o.length; s < a; ++s)
        if ((r = o[s]).type === e.type && r.name === e.name) {
          this.removeEventListener(r.type, r.listener, r.options), this.addEventListener(r.type, r.listener = i, r.options = n), r.value = t;
          return;
        }
    }
    this.addEventListener(e.type, i, n), r = { type: e.type, name: e.name, value: t, listener: i, options: n }, o ? o.push(r) : this.__on = [r];
  };
}
function Tl(e, t, n) {
  var o = Pl(e + ""), r, i = o.length, s;
  if (arguments.length < 2) {
    var a = this.node().__on;
    if (a) {
      for (var l = 0, c = a.length, u; l < c; ++l)
        for (r = 0, u = a[l]; r < i; ++r)
          if ((s = o[r]).type === u.type && s.name === u.name)
            return u.value;
    }
    return;
  }
  for (a = t ? jl : $l, r = 0; r < i; ++r) this.each(a(o[r], t, n));
  return this;
}
function yi(e, t, n) {
  var o = fi(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function zl(e, t) {
  return function() {
    return yi(this, e, t);
  };
}
function Rl(e, t) {
  return function() {
    return yi(this, e, t.apply(this, arguments));
  };
}
function Ll(e, t) {
  return this.each((typeof t == "function" ? Rl : zl)(e, t));
}
function* Hl() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length, s; r < i; ++r)
      (s = o[r]) && (yield s);
}
var xi = [null];
function Se(e, t) {
  this._groups = e, this._parents = t;
}
function Lt() {
  return new Se([[document.documentElement]], xi);
}
function Ol() {
  return this;
}
Se.prototype = Lt.prototype = {
  constructor: Se,
  select: fc,
  selectAll: mc,
  selectChild: vc,
  selectChildren: Ec,
  filter: Nc,
  data: Dc,
  enter: Cc,
  exit: $c,
  join: jc,
  merge: Tc,
  selection: Ol,
  order: zc,
  sort: Rc,
  call: Hc,
  nodes: Oc,
  node: Vc,
  size: Bc,
  empty: Fc,
  each: Yc,
  attr: Kc,
  style: tl,
  property: il,
  classed: ll,
  text: hl,
  html: yl,
  raise: wl,
  lower: bl,
  append: Sl,
  insert: El,
  remove: Cl,
  clone: Ml,
  datum: Al,
  on: Tl,
  dispatch: Ll,
  [Symbol.iterator]: Hl
};
function be(e) {
  return typeof e == "string" ? new Se([[document.querySelector(e)]], [document.documentElement]) : new Se([[e]], xi);
}
function Vl(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Ie(e, t) {
  if (e = Vl(e), t === void 0 && (t = e.currentTarget), t) {
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
const Bl = { passive: !1 }, Ct = { capture: !0, passive: !1 };
function Tn(e) {
  e.stopImmediatePropagation();
}
function at(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function wi(e) {
  var t = e.document.documentElement, n = be(e).on("dragstart.drag", at, Ct);
  "onselectstart" in t ? n.on("selectstart.drag", at, Ct) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function vi(e, t) {
  var n = e.document.documentElement, o = be(e).on("dragstart.drag", null);
  t && (o.on("click.drag", at, Ct), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const Yt = (e) => () => e;
function Qn(e, {
  sourceEvent: t,
  subject: n,
  target: o,
  identifier: r,
  active: i,
  x: s,
  y: a,
  dx: l,
  dy: c,
  dispatch: u
}) {
  Object.defineProperties(this, {
    type: { value: e, enumerable: !0, configurable: !0 },
    sourceEvent: { value: t, enumerable: !0, configurable: !0 },
    subject: { value: n, enumerable: !0, configurable: !0 },
    target: { value: o, enumerable: !0, configurable: !0 },
    identifier: { value: r, enumerable: !0, configurable: !0 },
    active: { value: i, enumerable: !0, configurable: !0 },
    x: { value: s, enumerable: !0, configurable: !0 },
    y: { value: a, enumerable: !0, configurable: !0 },
    dx: { value: l, enumerable: !0, configurable: !0 },
    dy: { value: c, enumerable: !0, configurable: !0 },
    _: { value: u }
  });
}
Qn.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function Fl(e) {
  return !e.ctrlKey && !e.button;
}
function Yl() {
  return this.parentNode;
}
function Xl(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function Wl() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function bi() {
  var e = Fl, t = Yl, n = Xl, o = Wl, r = {}, i = yn("start", "drag", "end"), s = 0, a, l, c, u, d = 0;
  function f(w) {
    w.on("mousedown.drag", g).filter(o).on("touchstart.drag", x).on("touchmove.drag", y, Bl).on("touchend.drag touchcancel.drag", _).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function g(w, C) {
    if (!(u || !e.call(this, w, C))) {
      var S = m(this, t.call(this, w, C), w, C, "mouse");
      S && (be(w.view).on("mousemove.drag", p, Ct).on("mouseup.drag", v, Ct), wi(w.view), Tn(w), c = !1, a = w.clientX, l = w.clientY, S("start", w));
    }
  }
  function p(w) {
    if (at(w), !c) {
      var C = w.clientX - a, S = w.clientY - l;
      c = C * C + S * S > d;
    }
    r.mouse("drag", w);
  }
  function v(w) {
    be(w.view).on("mousemove.drag mouseup.drag", null), vi(w.view, c), at(w), r.mouse("end", w);
  }
  function x(w, C) {
    if (e.call(this, w, C)) {
      var S = w.changedTouches, M = t.call(this, w, C), A = S.length, P, B;
      for (P = 0; P < A; ++P)
        (B = m(this, M, w, C, S[P].identifier, S[P])) && (Tn(w), B("start", w, S[P]));
    }
  }
  function y(w) {
    var C = w.changedTouches, S = C.length, M, A;
    for (M = 0; M < S; ++M)
      (A = r[C[M].identifier]) && (at(w), A("drag", w, C[M]));
  }
  function _(w) {
    var C = w.changedTouches, S = C.length, M, A;
    for (u && clearTimeout(u), u = setTimeout(function() {
      u = null;
    }, 500), M = 0; M < S; ++M)
      (A = r[C[M].identifier]) && (Tn(w), A("end", w, C[M]));
  }
  function m(w, C, S, M, A, P) {
    var B = i.copy(), I = Ie(P || S, C), T, L, b;
    if ((b = n.call(w, new Qn("beforestart", {
      sourceEvent: S,
      target: f,
      identifier: A,
      active: s,
      x: I[0],
      y: I[1],
      dx: 0,
      dy: 0,
      dispatch: B
    }), M)) != null)
      return T = b.x - I[0] || 0, L = b.y - I[1] || 0, function N(E, k, j) {
        var D = I, V;
        switch (E) {
          case "start":
            r[A] = N, V = s++;
            break;
          case "end":
            delete r[A], --s;
          // falls through
          case "drag":
            I = Ie(j || k, C), V = s;
            break;
        }
        B.call(
          E,
          w,
          new Qn(E, {
            sourceEvent: k,
            subject: b,
            target: f,
            identifier: A,
            active: V,
            x: I[0] + T,
            y: I[1] + L,
            dx: I[0] - D[0],
            dy: I[1] - D[1],
            dispatch: B
          }),
          M
        );
      };
  }
  return f.filter = function(w) {
    return arguments.length ? (e = typeof w == "function" ? w : Yt(!!w), f) : e;
  }, f.container = function(w) {
    return arguments.length ? (t = typeof w == "function" ? w : Yt(w), f) : t;
  }, f.subject = function(w) {
    return arguments.length ? (n = typeof w == "function" ? w : Yt(w), f) : n;
  }, f.touchable = function(w) {
    return arguments.length ? (o = typeof w == "function" ? w : Yt(!!w), f) : o;
  }, f.on = function() {
    var w = i.on.apply(i, arguments);
    return w === i ? f : w;
  }, f.clickDistance = function(w) {
    return arguments.length ? (d = (w = +w) * w, f) : Math.sqrt(d);
  }, f;
}
function wo(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function Si(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function Ht() {
}
var kt = 0.7, sn = 1 / kt, ct = "\\s*([+-]?\\d+)\\s*", It = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", ze = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", ql = /^#([0-9a-f]{3,8})$/, Zl = new RegExp(`^rgb\\(${ct},${ct},${ct}\\)$`), Ul = new RegExp(`^rgb\\(${ze},${ze},${ze}\\)$`), Gl = new RegExp(`^rgba\\(${ct},${ct},${ct},${It}\\)$`), Kl = new RegExp(`^rgba\\(${ze},${ze},${ze},${It}\\)$`), Ql = new RegExp(`^hsl\\(${It},${ze},${ze}\\)$`), Jl = new RegExp(`^hsla\\(${It},${ze},${ze},${It}\\)$`), Go = {
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
wo(Ht, Qe, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Ko,
  // Deprecated! Use color.formatHex.
  formatHex: Ko,
  formatHex8: eu,
  formatHsl: tu,
  formatRgb: Qo,
  toString: Qo
});
function Ko() {
  return this.rgb().formatHex();
}
function eu() {
  return this.rgb().formatHex8();
}
function tu() {
  return _i(this).formatHsl();
}
function Qo() {
  return this.rgb().formatRgb();
}
function Qe(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = ql.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? Jo(t) : n === 3 ? new ve(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? Xt(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? Xt(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = Zl.exec(e)) ? new ve(t[1], t[2], t[3], 1) : (t = Ul.exec(e)) ? new ve(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = Gl.exec(e)) ? Xt(t[1], t[2], t[3], t[4]) : (t = Kl.exec(e)) ? Xt(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = Ql.exec(e)) ? nr(t[1], t[2] / 100, t[3] / 100, 1) : (t = Jl.exec(e)) ? nr(t[1], t[2] / 100, t[3] / 100, t[4]) : Go.hasOwnProperty(e) ? Jo(Go[e]) : e === "transparent" ? new ve(NaN, NaN, NaN, 0) : null;
}
function Jo(e) {
  return new ve(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function Xt(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new ve(e, t, n, o);
}
function nu(e) {
  return e instanceof Ht || (e = Qe(e)), e ? (e = e.rgb(), new ve(e.r, e.g, e.b, e.opacity)) : new ve();
}
function Jn(e, t, n, o) {
  return arguments.length === 1 ? nu(e) : new ve(e, t, n, o ?? 1);
}
function ve(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
wo(ve, Jn, Si(Ht, {
  brighter(e) {
    return e = e == null ? sn : Math.pow(sn, e), new ve(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? kt : Math.pow(kt, e), new ve(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new ve(Ge(this.r), Ge(this.g), Ge(this.b), an(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: er,
  // Deprecated! Use color.formatHex.
  formatHex: er,
  formatHex8: ou,
  formatRgb: tr,
  toString: tr
}));
function er() {
  return `#${Ue(this.r)}${Ue(this.g)}${Ue(this.b)}`;
}
function ou() {
  return `#${Ue(this.r)}${Ue(this.g)}${Ue(this.b)}${Ue((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function tr() {
  const e = an(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${Ge(this.r)}, ${Ge(this.g)}, ${Ge(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function an(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function Ge(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function Ue(e) {
  return e = Ge(e), (e < 16 ? "0" : "") + e.toString(16);
}
function nr(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Me(e, t, n, o);
}
function _i(e) {
  if (e instanceof Me) return new Me(e.h, e.s, e.l, e.opacity);
  if (e instanceof Ht || (e = Qe(e)), !e) return new Me();
  if (e instanceof Me) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), i = Math.max(t, n, o), s = NaN, a = i - r, l = (i + r) / 2;
  return a ? (t === i ? s = (n - o) / a + (n < o) * 6 : n === i ? s = (o - t) / a + 2 : s = (t - n) / a + 4, a /= l < 0.5 ? i + r : 2 - i - r, s *= 60) : a = l > 0 && l < 1 ? 0 : s, new Me(s, a, l, e.opacity);
}
function ru(e, t, n, o) {
  return arguments.length === 1 ? _i(e) : new Me(e, t, n, o ?? 1);
}
function Me(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
wo(Me, ru, Si(Ht, {
  brighter(e) {
    return e = e == null ? sn : Math.pow(sn, e), new Me(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? kt : Math.pow(kt, e), new Me(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new ve(
      zn(e >= 240 ? e - 240 : e + 120, r, o),
      zn(e, r, o),
      zn(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new Me(or(this.h), Wt(this.s), Wt(this.l), an(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = an(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${or(this.h)}, ${Wt(this.s) * 100}%, ${Wt(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function or(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function Wt(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function zn(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const vo = (e) => () => e;
function iu(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function su(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function au(e) {
  return (e = +e) == 1 ? Ei : function(t, n) {
    return n - t ? su(t, n, e) : vo(isNaN(t) ? n : t);
  };
}
function Ei(e, t) {
  var n = t - e;
  return n ? iu(e, n) : vo(isNaN(e) ? t : e);
}
const cn = (function e(t) {
  var n = au(t);
  function o(r, i) {
    var s = n((r = Jn(r)).r, (i = Jn(i)).r), a = n(r.g, i.g), l = n(r.b, i.b), c = Ei(r.opacity, i.opacity);
    return function(u) {
      return r.r = s(u), r.g = a(u), r.b = l(u), r.opacity = c(u), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function cu(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(i) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - i) + t[r] * i;
    return o;
  };
}
function lu(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function uu(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), i = new Array(n), s;
  for (s = 0; s < o; ++s) r[s] = Et(e[s], t[s]);
  for (; s < n; ++s) i[s] = t[s];
  return function(a) {
    for (s = 0; s < o; ++s) i[s] = r[s](a);
    return i;
  };
}
function du(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(o) {
    return n.setTime(e * (1 - o) + t * o), n;
  };
}
function Te(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function fu(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = Et(e[r], t[r]) : o[r] = t[r];
  return function(i) {
    for (r in n) o[r] = n[r](i);
    return o;
  };
}
var eo = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, Rn = new RegExp(eo.source, "g");
function hu(e) {
  return function() {
    return e;
  };
}
function gu(e) {
  return function(t) {
    return e(t) + "";
  };
}
function Ni(e, t) {
  var n = eo.lastIndex = Rn.lastIndex = 0, o, r, i, s = -1, a = [], l = [];
  for (e = e + "", t = t + ""; (o = eo.exec(e)) && (r = Rn.exec(t)); )
    (i = r.index) > n && (i = t.slice(n, i), a[s] ? a[s] += i : a[++s] = i), (o = o[0]) === (r = r[0]) ? a[s] ? a[s] += r : a[++s] = r : (a[++s] = null, l.push({ i: s, x: Te(o, r) })), n = Rn.lastIndex;
  return n < t.length && (i = t.slice(n), a[s] ? a[s] += i : a[++s] = i), a.length < 2 ? l[0] ? gu(l[0].x) : hu(t) : (t = l.length, function(c) {
    for (var u = 0, d; u < t; ++u) a[(d = l[u]).i] = d.x(c);
    return a.join("");
  });
}
function Et(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? vo(t) : (n === "number" ? Te : n === "string" ? (o = Qe(t)) ? (t = o, cn) : Ni : t instanceof Qe ? cn : t instanceof Date ? du : lu(t) ? cu : Array.isArray(t) ? uu : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? fu : Te)(e, t);
}
var rr = 180 / Math.PI, to = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function Ci(e, t, n, o, r, i) {
  var s, a, l;
  return (s = Math.sqrt(e * e + t * t)) && (e /= s, t /= s), (l = e * n + t * o) && (n -= e * l, o -= t * l), (a = Math.sqrt(n * n + o * o)) && (n /= a, o /= a, l /= a), e * o < t * n && (e = -e, t = -t, l = -l, s = -s), {
    translateX: r,
    translateY: i,
    rotate: Math.atan2(t, e) * rr,
    skewX: Math.atan(l) * rr,
    scaleX: s,
    scaleY: a
  };
}
var qt;
function pu(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? to : Ci(t.a, t.b, t.c, t.d, t.e, t.f);
}
function mu(e) {
  return e == null || (qt || (qt = document.createElementNS("http://www.w3.org/2000/svg", "g")), qt.setAttribute("transform", e), !(e = qt.transform.baseVal.consolidate())) ? to : (e = e.matrix, Ci(e.a, e.b, e.c, e.d, e.e, e.f));
}
function ki(e, t, n, o) {
  function r(c) {
    return c.length ? c.pop() + " " : "";
  }
  function i(c, u, d, f, g, p) {
    if (c !== d || u !== f) {
      var v = g.push("translate(", null, t, null, n);
      p.push({ i: v - 4, x: Te(c, d) }, { i: v - 2, x: Te(u, f) });
    } else (d || f) && g.push("translate(" + d + t + f + n);
  }
  function s(c, u, d, f) {
    c !== u ? (c - u > 180 ? u += 360 : u - c > 180 && (c += 360), f.push({ i: d.push(r(d) + "rotate(", null, o) - 2, x: Te(c, u) })) : u && d.push(r(d) + "rotate(" + u + o);
  }
  function a(c, u, d, f) {
    c !== u ? f.push({ i: d.push(r(d) + "skewX(", null, o) - 2, x: Te(c, u) }) : u && d.push(r(d) + "skewX(" + u + o);
  }
  function l(c, u, d, f, g, p) {
    if (c !== d || u !== f) {
      var v = g.push(r(g) + "scale(", null, ",", null, ")");
      p.push({ i: v - 4, x: Te(c, d) }, { i: v - 2, x: Te(u, f) });
    } else (d !== 1 || f !== 1) && g.push(r(g) + "scale(" + d + "," + f + ")");
  }
  return function(c, u) {
    var d = [], f = [];
    return c = e(c), u = e(u), i(c.translateX, c.translateY, u.translateX, u.translateY, d, f), s(c.rotate, u.rotate, d, f), a(c.skewX, u.skewX, d, f), l(c.scaleX, c.scaleY, u.scaleX, u.scaleY, d, f), c = u = null, function(g) {
      for (var p = -1, v = f.length, x; ++p < v; ) d[(x = f[p]).i] = x.x(g);
      return d.join("");
    };
  };
}
var yu = ki(pu, "px, ", "px)", "deg)"), xu = ki(mu, ", ", ")", ")"), wu = 1e-12;
function ir(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function vu(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function bu(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const en = (function e(t, n, o) {
  function r(i, s) {
    var a = i[0], l = i[1], c = i[2], u = s[0], d = s[1], f = s[2], g = u - a, p = d - l, v = g * g + p * p, x, y;
    if (v < wu)
      y = Math.log(f / c) / t, x = function(M) {
        return [
          a + M * g,
          l + M * p,
          c * Math.exp(t * M * y)
        ];
      };
    else {
      var _ = Math.sqrt(v), m = (f * f - c * c + o * v) / (2 * c * n * _), w = (f * f - c * c - o * v) / (2 * f * n * _), C = Math.log(Math.sqrt(m * m + 1) - m), S = Math.log(Math.sqrt(w * w + 1) - w);
      y = (S - C) / t, x = function(M) {
        var A = M * y, P = ir(C), B = c / (n * _) * (P * bu(t * A + C) - vu(C));
        return [
          a + B * g,
          l + B * p,
          c * P / ir(t * A + C)
        ];
      };
    }
    return x.duration = y * 1e3 * t / Math.SQRT2, x;
  }
  return r.rho = function(i) {
    var s = Math.max(1e-3, +i), a = s * s, l = a * a;
    return e(s, a, l);
  }, r;
})(Math.SQRT2, 2, 4);
var ut = 0, St = 0, wt = 0, Ii = 1e3, ln, _t, un = 0, Je = 0, wn = 0, Mt = typeof performance == "object" && performance.now ? performance : Date, Mi = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function bo() {
  return Je || (Mi(Su), Je = Mt.now() + wn);
}
function Su() {
  Je = 0;
}
function dn() {
  this._call = this._time = this._next = null;
}
dn.prototype = Ai.prototype = {
  constructor: dn,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? bo() : +n) + (t == null ? 0 : +t), !this._next && _t !== this && (_t ? _t._next = this : ln = this, _t = this), this._call = e, this._time = n, no();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, no());
  }
};
function Ai(e, t, n) {
  var o = new dn();
  return o.restart(e, t, n), o;
}
function _u() {
  bo(), ++ut;
  for (var e = ln, t; e; )
    (t = Je - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --ut;
}
function sr() {
  Je = (un = Mt.now()) + wn, ut = St = 0;
  try {
    _u();
  } finally {
    ut = 0, Nu(), Je = 0;
  }
}
function Eu() {
  var e = Mt.now(), t = e - un;
  t > Ii && (wn -= t, un = e);
}
function Nu() {
  for (var e, t = ln, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : ln = n);
  _t = e, no(o);
}
function no(e) {
  if (!ut) {
    St && (St = clearTimeout(St));
    var t = e - Je;
    t > 24 ? (e < 1 / 0 && (St = setTimeout(sr, e - Mt.now() - wn)), wt && (wt = clearInterval(wt))) : (wt || (un = Mt.now(), wt = setInterval(Eu, Ii)), ut = 1, Mi(sr));
  }
}
function ar(e, t, n) {
  var o = new dn();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var Cu = yn("start", "end", "cancel", "interrupt"), ku = [], Di = 0, cr = 1, oo = 2, tn = 3, lr = 4, ro = 5, nn = 6;
function vn(e, t, n, o, r, i) {
  var s = e.__transition;
  if (!s) e.__transition = {};
  else if (n in s) return;
  Iu(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: Cu,
    tween: ku,
    time: i.time,
    delay: i.delay,
    duration: i.duration,
    ease: i.ease,
    timer: null,
    state: Di
  });
}
function So(e, t) {
  var n = $e(e, t);
  if (n.state > Di) throw new Error("too late; already scheduled");
  return n;
}
function Re(e, t) {
  var n = $e(e, t);
  if (n.state > tn) throw new Error("too late; already running");
  return n;
}
function $e(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function Iu(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = Ai(i, 0, n.time);
  function i(c) {
    n.state = cr, n.timer.restart(s, n.delay, n.time), n.delay <= c && s(c - n.delay);
  }
  function s(c) {
    var u, d, f, g;
    if (n.state !== cr) return l();
    for (u in o)
      if (g = o[u], g.name === n.name) {
        if (g.state === tn) return ar(s);
        g.state === lr ? (g.state = nn, g.timer.stop(), g.on.call("interrupt", e, e.__data__, g.index, g.group), delete o[u]) : +u < t && (g.state = nn, g.timer.stop(), g.on.call("cancel", e, e.__data__, g.index, g.group), delete o[u]);
      }
    if (ar(function() {
      n.state === tn && (n.state = lr, n.timer.restart(a, n.delay, n.time), a(c));
    }), n.state = oo, n.on.call("start", e, e.__data__, n.index, n.group), n.state === oo) {
      for (n.state = tn, r = new Array(f = n.tween.length), u = 0, d = -1; u < f; ++u)
        (g = n.tween[u].value.call(e, e.__data__, n.index, n.group)) && (r[++d] = g);
      r.length = d + 1;
    }
  }
  function a(c) {
    for (var u = c < n.duration ? n.ease.call(null, c / n.duration) : (n.timer.restart(l), n.state = ro, 1), d = -1, f = r.length; ++d < f; )
      r[d].call(e, u);
    n.state === ro && (n.on.call("end", e, e.__data__, n.index, n.group), l());
  }
  function l() {
    n.state = nn, n.timer.stop(), delete o[t];
    for (var c in o) return;
    delete e.__transition;
  }
}
function on(e, t) {
  var n = e.__transition, o, r, i = !0, s;
  if (n) {
    t = t == null ? null : t + "";
    for (s in n) {
      if ((o = n[s]).name !== t) {
        i = !1;
        continue;
      }
      r = o.state > oo && o.state < ro, o.state = nn, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[s];
    }
    i && delete e.__transition;
  }
}
function Mu(e) {
  return this.each(function() {
    on(this, e);
  });
}
function Au(e, t) {
  var n, o;
  return function() {
    var r = Re(this, e), i = r.tween;
    if (i !== n) {
      o = n = i;
      for (var s = 0, a = o.length; s < a; ++s)
        if (o[s].name === t) {
          o = o.slice(), o.splice(s, 1);
          break;
        }
    }
    r.tween = o;
  };
}
function Du(e, t, n) {
  var o, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var i = Re(this, e), s = i.tween;
    if (s !== o) {
      r = (o = s).slice();
      for (var a = { name: t, value: n }, l = 0, c = r.length; l < c; ++l)
        if (r[l].name === t) {
          r[l] = a;
          break;
        }
      l === c && r.push(a);
    }
    i.tween = r;
  };
}
function Pu(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = $e(this.node(), n).tween, r = 0, i = o.length, s; r < i; ++r)
      if ((s = o[r]).name === e)
        return s.value;
    return null;
  }
  return this.each((t == null ? Au : Du)(n, e, t));
}
function _o(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = Re(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return $e(r, o).value[t];
  };
}
function Pi(e, t) {
  var n;
  return (typeof t == "number" ? Te : t instanceof Qe ? cn : (n = Qe(t)) ? (t = n, cn) : Ni)(e, t);
}
function $u(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function ju(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Tu(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttribute(e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function zu(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttributeNS(e.space, e.local);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Ru(e, t, n) {
  var o, r, i;
  return function() {
    var s, a = n(this), l;
    return a == null ? void this.removeAttribute(e) : (s = this.getAttribute(e), l = a + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a)));
  };
}
function Lu(e, t, n) {
  var o, r, i;
  return function() {
    var s, a = n(this), l;
    return a == null ? void this.removeAttributeNS(e.space, e.local) : (s = this.getAttributeNS(e.space, e.local), l = a + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a)));
  };
}
function Hu(e, t) {
  var n = xn(e), o = n === "transform" ? xu : Pi;
  return this.attrTween(e, typeof t == "function" ? (n.local ? Lu : Ru)(n, o, _o(this, "attr." + e, t)) : t == null ? (n.local ? ju : $u)(n) : (n.local ? zu : Tu)(n, o, t));
}
function Ou(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function Vu(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function Bu(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && Vu(e, i)), n;
  }
  return r._value = t, r;
}
function Fu(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && Ou(e, i)), n;
  }
  return r._value = t, r;
}
function Yu(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = xn(e);
  return this.tween(n, (o.local ? Bu : Fu)(o, t));
}
function Xu(e, t) {
  return function() {
    So(this, e).delay = +t.apply(this, arguments);
  };
}
function Wu(e, t) {
  return t = +t, function() {
    So(this, e).delay = t;
  };
}
function qu(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Xu : Wu)(t, e)) : $e(this.node(), t).delay;
}
function Zu(e, t) {
  return function() {
    Re(this, e).duration = +t.apply(this, arguments);
  };
}
function Uu(e, t) {
  return t = +t, function() {
    Re(this, e).duration = t;
  };
}
function Gu(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Zu : Uu)(t, e)) : $e(this.node(), t).duration;
}
function Ku(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    Re(this, e).ease = t;
  };
}
function Qu(e) {
  var t = this._id;
  return arguments.length ? this.each(Ku(t, e)) : $e(this.node(), t).ease;
}
function Ju(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    Re(this, e).ease = n;
  };
}
function ed(e) {
  if (typeof e != "function") throw new Error();
  return this.each(Ju(this._id, e));
}
function td(e) {
  typeof e != "function" && (e = li(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = [], l, c = 0; c < s; ++c)
      (l = i[c]) && e.call(l, l.__data__, c, i) && a.push(l);
  return new Ve(o, this._parents, this._name, this._id);
}
function nd(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, i = Math.min(o, r), s = new Array(o), a = 0; a < i; ++a)
    for (var l = t[a], c = n[a], u = l.length, d = s[a] = new Array(u), f, g = 0; g < u; ++g)
      (f = l[g] || c[g]) && (d[g] = f);
  for (; a < o; ++a)
    s[a] = t[a];
  return new Ve(s, this._parents, this._name, this._id);
}
function od(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function rd(e, t, n) {
  var o, r, i = od(t) ? So : Re;
  return function() {
    var s = i(this, e), a = s.on;
    a !== o && (r = (o = a).copy()).on(t, n), s.on = r;
  };
}
function id(e, t) {
  var n = this._id;
  return arguments.length < 2 ? $e(this.node(), n).on.on(e) : this.each(rd(n, e, t));
}
function sd(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function ad() {
  return this.on("end.remove", sd(this._id));
}
function cd(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = yo(e));
  for (var o = this._groups, r = o.length, i = new Array(r), s = 0; s < r; ++s)
    for (var a = o[s], l = a.length, c = i[s] = new Array(l), u, d, f = 0; f < l; ++f)
      (u = a[f]) && (d = e.call(u, u.__data__, f, a)) && ("__data__" in u && (d.__data__ = u.__data__), c[f] = d, vn(c[f], t, n, f, c, $e(u, n)));
  return new Ve(i, this._parents, t, n);
}
function ld(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = ci(e));
  for (var o = this._groups, r = o.length, i = [], s = [], a = 0; a < r; ++a)
    for (var l = o[a], c = l.length, u, d = 0; d < c; ++d)
      if (u = l[d]) {
        for (var f = e.call(u, u.__data__, d, l), g, p = $e(u, n), v = 0, x = f.length; v < x; ++v)
          (g = f[v]) && vn(g, t, n, v, f, p);
        i.push(f), s.push(u);
      }
  return new Ve(i, s, t, n);
}
var ud = Lt.prototype.constructor;
function dd() {
  return new ud(this._groups, this._parents);
}
function fd(e, t) {
  var n, o, r;
  return function() {
    var i = lt(this, e), s = (this.style.removeProperty(e), lt(this, e));
    return i === s ? null : i === n && s === o ? r : r = t(n = i, o = s);
  };
}
function $i(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function hd(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = lt(this, e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function gd(e, t, n) {
  var o, r, i;
  return function() {
    var s = lt(this, e), a = n(this), l = a + "";
    return a == null && (l = a = (this.style.removeProperty(e), lt(this, e))), s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a));
  };
}
function pd(e, t) {
  var n, o, r, i = "style." + t, s = "end." + i, a;
  return function() {
    var l = Re(this, e), c = l.on, u = l.value[i] == null ? a || (a = $i(t)) : void 0;
    (c !== n || r !== u) && (o = (n = c).copy()).on(s, r = u), l.on = o;
  };
}
function md(e, t, n) {
  var o = (e += "") == "transform" ? yu : Pi;
  return t == null ? this.styleTween(e, fd(e, o)).on("end.style." + e, $i(e)) : typeof t == "function" ? this.styleTween(e, gd(e, o, _o(this, "style." + e, t))).each(pd(this._id, e)) : this.styleTween(e, hd(e, o, t), n).on("end.style." + e, null);
}
function yd(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function xd(e, t, n) {
  var o, r;
  function i() {
    var s = t.apply(this, arguments);
    return s !== r && (o = (r = s) && yd(e, s, n)), o;
  }
  return i._value = t, i;
}
function wd(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, xd(e, t, n ?? ""));
}
function vd(e) {
  return function() {
    this.textContent = e;
  };
}
function bd(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function Sd(e) {
  return this.tween("text", typeof e == "function" ? bd(_o(this, "text", e)) : vd(e == null ? "" : e + ""));
}
function _d(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Ed(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && _d(r)), t;
  }
  return o._value = e, o;
}
function Nd(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, Ed(e));
}
function Cd() {
  for (var e = this._name, t = this._id, n = ji(), o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], a = s.length, l, c = 0; c < a; ++c)
      if (l = s[c]) {
        var u = $e(l, t);
        vn(l, e, n, c, s, {
          time: u.time + u.delay + u.duration,
          delay: 0,
          duration: u.duration,
          ease: u.ease
        });
      }
  return new Ve(o, this._parents, e, n);
}
function kd() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(i, s) {
    var a = { value: s }, l = { value: function() {
      --r === 0 && i();
    } };
    n.each(function() {
      var c = Re(this, o), u = c.on;
      u !== e && (t = (e = u).copy(), t._.cancel.push(a), t._.interrupt.push(a), t._.end.push(l)), c.on = t;
    }), r === 0 && i();
  });
}
var Id = 0;
function Ve(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function ji() {
  return ++Id;
}
var Le = Lt.prototype;
Ve.prototype = {
  constructor: Ve,
  select: cd,
  selectAll: ld,
  selectChild: Le.selectChild,
  selectChildren: Le.selectChildren,
  filter: td,
  merge: nd,
  selection: dd,
  transition: Cd,
  call: Le.call,
  nodes: Le.nodes,
  node: Le.node,
  size: Le.size,
  empty: Le.empty,
  each: Le.each,
  on: id,
  attr: Hu,
  attrTween: Yu,
  style: md,
  styleTween: wd,
  text: Sd,
  textTween: Nd,
  remove: ad,
  tween: Pu,
  delay: qu,
  duration: Gu,
  ease: Qu,
  easeVarying: ed,
  end: kd,
  [Symbol.iterator]: Le[Symbol.iterator]
};
function Md(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var Ad = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: Md
};
function Dd(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function Pd(e) {
  var t, n;
  e instanceof Ve ? (t = e._id, e = e._name) : (t = ji(), (n = Ad).time = bo(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], a = s.length, l, c = 0; c < a; ++c)
      (l = s[c]) && vn(l, e, t, c, s, n || Dd(l, t));
  return new Ve(o, this._parents, e, t);
}
Lt.prototype.interrupt = Mu;
Lt.prototype.transition = Pd;
const Zt = (e) => () => e;
function $d(e, {
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
function He(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
He.prototype = {
  constructor: He,
  scale: function(e) {
    return e === 1 ? this : new He(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new He(this.k, this.x + this.k * e, this.y + this.k * t);
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
var bn = new He(1, 0, 0);
Ti.prototype = He.prototype;
function Ti(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return bn;
  return e.__zoom;
}
function Ln(e) {
  e.stopImmediatePropagation();
}
function vt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function jd(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function Td() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function ur() {
  return this.__zoom || bn;
}
function zd(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function Rd() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Ld(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], i = e.invertY(t[0][1]) - n[0][1], s = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    s > i ? (i + s) / 2 : Math.min(0, i) || Math.max(0, s)
  );
}
function zi() {
  var e = jd, t = Td, n = Ld, o = zd, r = Rd, i = [0, 1 / 0], s = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], a = 250, l = en, c = yn("start", "zoom", "end"), u, d, f, g = 500, p = 150, v = 0, x = 10;
  function y(b) {
    b.property("__zoom", ur).on("wheel.zoom", A, { passive: !1 }).on("mousedown.zoom", P).on("dblclick.zoom", B).filter(r).on("touchstart.zoom", I).on("touchmove.zoom", T).on("touchend.zoom touchcancel.zoom", L).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  y.transform = function(b, N, E, k) {
    var j = b.selection ? b.selection() : b;
    j.property("__zoom", ur), b !== j ? C(b, N, E, k) : j.interrupt().each(function() {
      S(this, arguments).event(k).start().zoom(null, typeof N == "function" ? N.apply(this, arguments) : N).end();
    });
  }, y.scaleBy = function(b, N, E, k) {
    y.scaleTo(b, function() {
      var j = this.__zoom.k, D = typeof N == "function" ? N.apply(this, arguments) : N;
      return j * D;
    }, E, k);
  }, y.scaleTo = function(b, N, E, k) {
    y.transform(b, function() {
      var j = t.apply(this, arguments), D = this.__zoom, V = E == null ? w(j) : typeof E == "function" ? E.apply(this, arguments) : E, O = D.invert(V), H = typeof N == "function" ? N.apply(this, arguments) : N;
      return n(m(_(D, H), V, O), j, s);
    }, E, k);
  }, y.translateBy = function(b, N, E, k) {
    y.transform(b, function() {
      return n(this.__zoom.translate(
        typeof N == "function" ? N.apply(this, arguments) : N,
        typeof E == "function" ? E.apply(this, arguments) : E
      ), t.apply(this, arguments), s);
    }, null, k);
  }, y.translateTo = function(b, N, E, k, j) {
    y.transform(b, function() {
      var D = t.apply(this, arguments), V = this.__zoom, O = k == null ? w(D) : typeof k == "function" ? k.apply(this, arguments) : k;
      return n(bn.translate(O[0], O[1]).scale(V.k).translate(
        typeof N == "function" ? -N.apply(this, arguments) : -N,
        typeof E == "function" ? -E.apply(this, arguments) : -E
      ), D, s);
    }, k, j);
  };
  function _(b, N) {
    return N = Math.max(i[0], Math.min(i[1], N)), N === b.k ? b : new He(N, b.x, b.y);
  }
  function m(b, N, E) {
    var k = N[0] - E[0] * b.k, j = N[1] - E[1] * b.k;
    return k === b.x && j === b.y ? b : new He(b.k, k, j);
  }
  function w(b) {
    return [(+b[0][0] + +b[1][0]) / 2, (+b[0][1] + +b[1][1]) / 2];
  }
  function C(b, N, E, k) {
    b.on("start.zoom", function() {
      S(this, arguments).event(k).start();
    }).on("interrupt.zoom end.zoom", function() {
      S(this, arguments).event(k).end();
    }).tween("zoom", function() {
      var j = this, D = arguments, V = S(j, D).event(k), O = t.apply(j, D), H = E == null ? w(O) : typeof E == "function" ? E.apply(j, D) : E, q = Math.max(O[1][0] - O[0][0], O[1][1] - O[0][1]), X = j.__zoom, Z = typeof N == "function" ? N.apply(j, D) : N, ne = l(X.invert(H).concat(q / X.k), Z.invert(H).concat(q / Z.k));
      return function(U) {
        if (U === 1) U = Z;
        else {
          var R = ne(U), W = q / R[2];
          U = new He(W, H[0] - R[0] * W, H[1] - R[1] * W);
        }
        V.zoom(null, U);
      };
    });
  }
  function S(b, N, E) {
    return !E && b.__zooming || new M(b, N);
  }
  function M(b, N) {
    this.that = b, this.args = N, this.active = 0, this.sourceEvent = null, this.extent = t.apply(b, N), this.taps = 0;
  }
  M.prototype = {
    event: function(b) {
      return b && (this.sourceEvent = b), this;
    },
    start: function() {
      return ++this.active === 1 && (this.that.__zooming = this, this.emit("start")), this;
    },
    zoom: function(b, N) {
      return this.mouse && b !== "mouse" && (this.mouse[1] = N.invert(this.mouse[0])), this.touch0 && b !== "touch" && (this.touch0[1] = N.invert(this.touch0[0])), this.touch1 && b !== "touch" && (this.touch1[1] = N.invert(this.touch1[0])), this.that.__zoom = N, this.emit("zoom"), this;
    },
    end: function() {
      return --this.active === 0 && (delete this.that.__zooming, this.emit("end")), this;
    },
    emit: function(b) {
      var N = be(this.that).datum();
      c.call(
        b,
        this.that,
        new $d(b, {
          sourceEvent: this.sourceEvent,
          target: y,
          transform: this.that.__zoom,
          dispatch: c
        }),
        N
      );
    }
  };
  function A(b, ...N) {
    if (!e.apply(this, arguments)) return;
    var E = S(this, N).event(b), k = this.__zoom, j = Math.max(i[0], Math.min(i[1], k.k * Math.pow(2, o.apply(this, arguments)))), D = Ie(b);
    if (E.wheel)
      (E.mouse[0][0] !== D[0] || E.mouse[0][1] !== D[1]) && (E.mouse[1] = k.invert(E.mouse[0] = D)), clearTimeout(E.wheel);
    else {
      if (k.k === j) return;
      E.mouse = [D, k.invert(D)], on(this), E.start();
    }
    vt(b), E.wheel = setTimeout(V, p), E.zoom("mouse", n(m(_(k, j), E.mouse[0], E.mouse[1]), E.extent, s));
    function V() {
      E.wheel = null, E.end();
    }
  }
  function P(b, ...N) {
    if (f || !e.apply(this, arguments)) return;
    var E = b.currentTarget, k = S(this, N, !0).event(b), j = be(b.view).on("mousemove.zoom", H, !0).on("mouseup.zoom", q, !0), D = Ie(b, E), V = b.clientX, O = b.clientY;
    wi(b.view), Ln(b), k.mouse = [D, this.__zoom.invert(D)], on(this), k.start();
    function H(X) {
      if (vt(X), !k.moved) {
        var Z = X.clientX - V, ne = X.clientY - O;
        k.moved = Z * Z + ne * ne > v;
      }
      k.event(X).zoom("mouse", n(m(k.that.__zoom, k.mouse[0] = Ie(X, E), k.mouse[1]), k.extent, s));
    }
    function q(X) {
      j.on("mousemove.zoom mouseup.zoom", null), vi(X.view, k.moved), vt(X), k.event(X).end();
    }
  }
  function B(b, ...N) {
    if (e.apply(this, arguments)) {
      var E = this.__zoom, k = Ie(b.changedTouches ? b.changedTouches[0] : b, this), j = E.invert(k), D = E.k * (b.shiftKey ? 0.5 : 2), V = n(m(_(E, D), k, j), t.apply(this, N), s);
      vt(b), a > 0 ? be(this).transition().duration(a).call(C, V, k, b) : be(this).call(y.transform, V, k, b);
    }
  }
  function I(b, ...N) {
    if (e.apply(this, arguments)) {
      var E = b.touches, k = E.length, j = S(this, N, b.changedTouches.length === k).event(b), D, V, O, H;
      for (Ln(b), V = 0; V < k; ++V)
        O = E[V], H = Ie(O, this), H = [H, this.__zoom.invert(H), O.identifier], j.touch0 ? !j.touch1 && j.touch0[2] !== H[2] && (j.touch1 = H, j.taps = 0) : (j.touch0 = H, D = !0, j.taps = 1 + !!u);
      u && (u = clearTimeout(u)), D && (j.taps < 2 && (d = H[0], u = setTimeout(function() {
        u = null;
      }, g)), on(this), j.start());
    }
  }
  function T(b, ...N) {
    if (this.__zooming) {
      var E = S(this, N).event(b), k = b.changedTouches, j = k.length, D, V, O, H;
      for (vt(b), D = 0; D < j; ++D)
        V = k[D], O = Ie(V, this), E.touch0 && E.touch0[2] === V.identifier ? E.touch0[0] = O : E.touch1 && E.touch1[2] === V.identifier && (E.touch1[0] = O);
      if (V = E.that.__zoom, E.touch1) {
        var q = E.touch0[0], X = E.touch0[1], Z = E.touch1[0], ne = E.touch1[1], U = (U = Z[0] - q[0]) * U + (U = Z[1] - q[1]) * U, R = (R = ne[0] - X[0]) * R + (R = ne[1] - X[1]) * R;
        V = _(V, Math.sqrt(U / R)), O = [(q[0] + Z[0]) / 2, (q[1] + Z[1]) / 2], H = [(X[0] + ne[0]) / 2, (X[1] + ne[1]) / 2];
      } else if (E.touch0) O = E.touch0[0], H = E.touch0[1];
      else return;
      E.zoom("touch", n(m(V, O, H), E.extent, s));
    }
  }
  function L(b, ...N) {
    if (this.__zooming) {
      var E = S(this, N).event(b), k = b.changedTouches, j = k.length, D, V;
      for (Ln(b), f && clearTimeout(f), f = setTimeout(function() {
        f = null;
      }, g), D = 0; D < j; ++D)
        V = k[D], E.touch0 && E.touch0[2] === V.identifier ? delete E.touch0 : E.touch1 && E.touch1[2] === V.identifier && delete E.touch1;
      if (E.touch1 && !E.touch0 && (E.touch0 = E.touch1, delete E.touch1), E.touch0) E.touch0[1] = this.__zoom.invert(E.touch0[0]);
      else if (E.end(), E.taps === 2 && (V = Ie(V, this), Math.hypot(d[0] - V[0], d[1] - V[1]) < x)) {
        var O = be(this).on("dblclick.zoom");
        O && O.apply(this, arguments);
      }
    }
  }
  return y.wheelDelta = function(b) {
    return arguments.length ? (o = typeof b == "function" ? b : Zt(+b), y) : o;
  }, y.filter = function(b) {
    return arguments.length ? (e = typeof b == "function" ? b : Zt(!!b), y) : e;
  }, y.touchable = function(b) {
    return arguments.length ? (r = typeof b == "function" ? b : Zt(!!b), y) : r;
  }, y.extent = function(b) {
    return arguments.length ? (t = typeof b == "function" ? b : Zt([[+b[0][0], +b[0][1]], [+b[1][0], +b[1][1]]]), y) : t;
  }, y.scaleExtent = function(b) {
    return arguments.length ? (i[0] = +b[0], i[1] = +b[1], y) : [i[0], i[1]];
  }, y.translateExtent = function(b) {
    return arguments.length ? (s[0][0] = +b[0][0], s[1][0] = +b[1][0], s[0][1] = +b[0][1], s[1][1] = +b[1][1], y) : [[s[0][0], s[0][1]], [s[1][0], s[1][1]]];
  }, y.constrain = function(b) {
    return arguments.length ? (n = b, y) : n;
  }, y.duration = function(b) {
    return arguments.length ? (a = +b, y) : a;
  }, y.interpolate = function(b) {
    return arguments.length ? (l = b, y) : l;
  }, y.on = function() {
    var b = c.on.apply(c, arguments);
    return b === c ? y : b;
  }, y.clickDistance = function(b) {
    return arguments.length ? (v = (b = +b) * b, y) : Math.sqrt(v);
  }, y.tapDistance = function(b) {
    return arguments.length ? (x = +b, y) : x;
  }, y;
}
const Pe = {
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
}, At = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], Ri = ["Enter", " ", "Escape"], Li = {
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
var dt;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(dt || (dt = {}));
var Ke;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(Ke || (Ke = {}));
var Dt;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Dt || (Dt = {}));
const Hi = {
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
var Xe;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(Xe || (Xe = {}));
var fn;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(fn || (fn = {}));
var G;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(G || (G = {}));
const dr = {
  [G.Left]: G.Right,
  [G.Right]: G.Left,
  [G.Top]: G.Bottom,
  [G.Bottom]: G.Top
};
function Oi(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Vi = (e) => "id" in e && "source" in e && "target" in e, Hd = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), Eo = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), Ot = (e, t = [0, 0]) => {
  const { width: n, height: o } = Be(e), r = e.origin ?? t, i = n * r[0], s = o * r[1];
  return {
    x: e.position.x - i,
    y: e.position.y - s
  };
}, Od = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const i = typeof r == "string";
    let s = !t.nodeLookup && !i ? r : void 0;
    t.nodeLookup && (s = i ? t.nodeLookup.get(r) : Eo(r) ? r : t.nodeLookup.get(r.id));
    const a = s ? hn(s, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return Sn(o, a);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return _n(n);
}, Vt = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = Sn(n, hn(r)), o = !0);
  }), o ? _n(n) : { x: 0, y: 0, width: 0, height: 0 };
}, No = (e, t, [n, o, r] = [0, 0, 1], i = !1, s = !1) => {
  const a = {
    ...yt(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, l = [];
  for (const c of e.values()) {
    const { measured: u, selectable: d = !0, hidden: f = !1 } = c;
    if (s && !d || f)
      continue;
    const g = u.width ?? c.width ?? c.initialWidth ?? null, p = u.height ?? c.height ?? c.initialHeight ?? null, v = Pt(a, ht(c)), x = (g ?? 0) * (p ?? 0), y = i && v > 0;
    (!c.internals.handleBounds || y || v >= x || c.dragging) && l.push(c);
  }
  return l;
}, Vd = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function Bd(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function Fd({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: i }, s) {
  if (e.size === 0)
    return !0;
  const a = Bd(e, s), l = Vt(a), c = ko(l, t, n, s?.minZoom ?? r, s?.maxZoom ?? i, s?.padding ?? 0.1);
  return await o.setViewport(c, {
    duration: s?.duration,
    ease: s?.ease,
    interpolate: s?.interpolate
  }), !0;
}
function Bi({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: i }) {
  const s = n.get(e), a = s.parentId ? n.get(s.parentId) : void 0, { x: l, y: c } = a ? a.internals.positionAbsolute : { x: 0, y: 0 }, u = s.origin ?? o;
  let d = s.extent || r;
  if (s.extent === "parent" && !s.expandParent)
    if (!a)
      i?.("005", Pe.error005());
    else {
      const g = a.measured.width, p = a.measured.height;
      g && p && (d = [
        [l, c],
        [l + g, c + p]
      ]);
    }
  else a && tt(s.extent) && (d = [
    [s.extent[0][0] + l, s.extent[0][1] + c],
    [s.extent[1][0] + l, s.extent[1][1] + c]
  ]);
  const f = tt(d) ? et(t, d, s.measured) : t;
  return (s.measured.width === void 0 || s.measured.height === void 0) && i?.("015", Pe.error015()), {
    position: {
      x: f.x - l + (s.measured.width ?? 0) * u[0],
      y: f.y - c + (s.measured.height ?? 0) * u[1]
    },
    positionAbsolute: f
  };
}
async function Yd({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const i = new Set(e.map((f) => f.id)), s = [];
  for (const f of n) {
    if (f.deletable === !1)
      continue;
    const g = i.has(f.id), p = !g && f.parentId && s.find((v) => v.id === f.parentId);
    (g || p) && s.push(f);
  }
  const a = new Set(t.map((f) => f.id)), l = o.filter((f) => f.deletable !== !1), u = Vd(s, l);
  for (const f of l)
    a.has(f.id) && !u.find((p) => p.id === f.id) && u.push(f);
  if (!r)
    return {
      edges: u,
      nodes: s
    };
  const d = await r({
    nodes: s,
    edges: u
  });
  return typeof d == "boolean" ? d ? { edges: u, nodes: s } : { edges: [], nodes: [] } : d;
}
const ft = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), et = (e = { x: 0, y: 0 }, t, n) => ({
  x: ft(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: ft(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Fi(e, t, n) {
  const { width: o, height: r } = Be(n), { x: i, y: s } = n.internals.positionAbsolute;
  return et(e, [
    [i, s],
    [i + o, s + r]
  ], t);
}
const fr = (e, t, n) => e < t ? ft(Math.abs(e - t), 1, t) / t : e > n ? -ft(Math.abs(e - n), 1, t) / t : 0, Co = (e, t, n = 15, o = 40) => {
  const r = fr(e.x, o, t.width - o) * n, i = fr(e.y, o, t.height - o) * n;
  return [r, i];
}, Sn = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), io = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), _n = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), ht = (e, t = [0, 0]) => {
  const { x: n, y: o } = Eo(e) ? e.internals.positionAbsolute : Ot(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, hn = (e, t = [0, 0]) => {
  const { x: n, y: o } = Eo(e) ? e.internals.positionAbsolute : Ot(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, Yi = (e, t) => _n(Sn(io(e), io(t))), Pt = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, hr = (e) => Ae(e.width) && Ae(e.height) && Ae(e.x) && Ae(e.y), Ae = (e) => !isNaN(e) && isFinite(e), Xi = (e, t) => (n, o) => {
}, Bt = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), yt = ({ x: e, y: t }, [n, o, r], i = !1, s = [1, 1]) => {
  const a = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return i ? Bt(a, s) : a;
}, gt = ({ x: e, y: t }, [n, o, r]) => ({
  x: e * r + n,
  y: t * r + o
});
function rt(e, t) {
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
function Xd(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = rt(e, n), r = rt(e, t);
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
    const o = rt(e.top ?? e.y ?? 0, n), r = rt(e.bottom ?? e.y ?? 0, n), i = rt(e.left ?? e.x ?? 0, t), s = rt(e.right ?? e.x ?? 0, t);
    return { top: o, right: s, bottom: r, left: i, x: i + s, y: o + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function Wd(e, t, n, o, r, i) {
  const { x: s, y: a } = gt(e, [t, n, o]), { x: l, y: c } = gt({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), u = r - l, d = i - c;
  return {
    left: Math.floor(s),
    top: Math.floor(a),
    right: Math.floor(u),
    bottom: Math.floor(d)
  };
}
const ko = (e, t, n, o, r, i) => {
  const s = Xd(i, t, n), a = (t - s.x) / e.width, l = (n - s.y) / e.height, c = Math.min(a, l), u = ft(c, o, r), d = e.x + e.width / 2, f = e.y + e.height / 2, g = t / 2 - d * u, p = n / 2 - f * u, v = Wd(e, g, p, u, t, n), x = {
    left: Math.min(v.left - s.left, 0),
    top: Math.min(v.top - s.top, 0),
    right: Math.min(v.right - s.right, 0),
    bottom: Math.min(v.bottom - s.bottom, 0)
  };
  return {
    x: g - x.left + x.right,
    y: p - x.top + x.bottom,
    zoom: u
  };
}, $t = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function tt(e) {
  return e != null && e !== "parent";
}
function Be(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function Wi(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function qi(e, t = { width: 0, height: 0 }, n, o, r) {
  const i = { ...e }, s = o.get(n);
  if (s) {
    const a = s.origin || r;
    i.x += s.internals.positionAbsolute.x - (t.width ?? 0) * a[0], i.y += s.internals.positionAbsolute.y - (t.height ?? 0) * a[1];
  }
  return i;
}
function gr(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function qd() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function Zd(e) {
  return { ...Li, ...e || {} };
}
function Nt(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: i, y: s } = De(e), a = yt({ x: i - (r?.left ?? 0), y: s - (r?.top ?? 0) }, o), { x: l, y: c } = n ? Bt(a, t) : a;
  return {
    xSnapped: l,
    ySnapped: c,
    ...a
  };
}
const Io = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), Zi = (e) => e?.getRootNode?.() || window?.document, Ud = ["INPUT", "SELECT", "TEXTAREA"];
function Ui(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : Ud.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const Gi = (e) => "clientX" in e, De = (e, t) => {
  const n = Gi(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, pr = (e, t, n, o, r) => {
  const i = t.querySelectorAll(`.${e}`);
  return !i || !i.length ? null : Array.from(i).map((s) => {
    const a = s.getBoundingClientRect();
    return {
      id: s.getAttribute("data-handleid"),
      type: e,
      nodeId: r,
      position: s.getAttribute("data-handlepos"),
      x: (a.left - n.left) / o,
      y: (a.top - n.top) / o,
      ...Io(s)
    };
  });
};
function Ki({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: i, targetControlX: s, targetControlY: a }) {
  const l = e * 0.125 + r * 0.375 + s * 0.375 + n * 0.125, c = t * 0.125 + i * 0.375 + a * 0.375 + o * 0.125, u = Math.abs(l - e), d = Math.abs(c - t);
  return [l, c, u, d];
}
function Ut(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function mr({ pos: e, x1: t, y1: n, x2: o, y2: r, c: i }) {
  switch (e) {
    case G.Left:
      return [t - Ut(t - o, i), n];
    case G.Right:
      return [t + Ut(o - t, i), n];
    case G.Top:
      return [t, n - Ut(n - r, i)];
    case G.Bottom:
      return [t, n + Ut(r - n, i)];
  }
}
function Qi({ sourceX: e, sourceY: t, sourcePosition: n = G.Bottom, targetX: o, targetY: r, targetPosition: i = G.Top, curvature: s = 0.25 }) {
  const [a, l] = mr({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: s
  }), [c, u] = mr({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: s
  }), [d, f, g, p] = Ki({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: r,
    sourceControlX: a,
    sourceControlY: l,
    targetControlX: c,
    targetControlY: u
  });
  return [
    `M${e},${t} C${a},${l} ${c},${u} ${o},${r}`,
    d,
    f,
    g,
    p
  ];
}
function Ji({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, i = n < e ? n + r : n - r, s = Math.abs(o - t) / 2, a = o < t ? o + s : o - s;
  return [i, a, r, s];
}
function Gd({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: i = "basic" }) {
  if (i === "manual")
    return o;
  const s = r && n ? o + 1e3 : o, a = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return s + a;
}
function Kd({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const i = Sn(hn(e), hn(t));
  i.x === i.x2 && (i.x2 += 1), i.y === i.y2 && (i.y2 += 1);
  const s = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return Pt(s, _n(i)) > 0;
}
const Qd = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, Jd = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), ef = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Pe.error006()), t;
  const o = n.getEdgeId || Qd;
  let r;
  return Vi(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, Jd(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
};
function es({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, i, s, a] = Ji({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, i, s, a];
}
const yr = {
  [G.Left]: { x: -1, y: 0 },
  [G.Right]: { x: 1, y: 0 },
  [G.Top]: { x: 0, y: -1 },
  [G.Bottom]: { x: 0, y: 1 }
}, tf = ({ source: e, sourcePosition: t = G.Bottom, target: n }) => t === G.Left || t === G.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, xr = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function nf({ source: e, sourcePosition: t = G.Bottom, target: n, targetPosition: o = G.Top, center: r, offset: i, stepPosition: s }) {
  const a = yr[t], l = yr[o], c = { x: e.x + a.x * i, y: e.y + a.y * i }, u = { x: n.x + l.x * i, y: n.y + l.y * i }, d = tf({
    source: c,
    sourcePosition: t,
    target: u
  }), f = d.x !== 0 ? "x" : "y", g = d[f];
  let p = [], v, x;
  const y = { x: 0, y: 0 }, _ = { x: 0, y: 0 }, [, , m, w] = Ji({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (a[f] * l[f] === -1) {
    f === "x" ? (v = r.x ?? c.x + (u.x - c.x) * s, x = r.y ?? (c.y + u.y) / 2) : (v = r.x ?? (c.x + u.x) / 2, x = r.y ?? c.y + (u.y - c.y) * s);
    const A = [
      { x: v, y: c.y },
      { x: v, y: u.y }
    ], P = [
      { x: c.x, y: x },
      { x: u.x, y: x }
    ];
    a[f] === g ? p = f === "x" ? A : P : p = f === "x" ? P : A;
  } else {
    const A = [{ x: c.x, y: u.y }], P = [{ x: u.x, y: c.y }];
    if (f === "x" ? p = a.x === g ? P : A : p = a.y === g ? A : P, t === o) {
      const b = Math.abs(e[f] - n[f]);
      if (b <= i) {
        const N = Math.min(i - 1, i - b);
        a[f] === g ? y[f] = (c[f] > e[f] ? -1 : 1) * N : _[f] = (u[f] > n[f] ? -1 : 1) * N;
      }
    }
    if (t !== o) {
      const b = f === "x" ? "y" : "x", N = a[f] === l[b], E = c[b] > u[b], k = c[b] < u[b];
      (a[f] === 1 && (!N && E || N && k) || a[f] !== 1 && (!N && k || N && E)) && (p = f === "x" ? A : P);
    }
    const B = { x: c.x + y.x, y: c.y + y.y }, I = { x: u.x + _.x, y: u.y + _.y }, T = Math.max(Math.abs(B.x - p[0].x), Math.abs(I.x - p[0].x)), L = Math.max(Math.abs(B.y - p[0].y), Math.abs(I.y - p[0].y));
    T >= L ? (v = (B.x + I.x) / 2, x = p[0].y) : (v = p[0].x, x = (B.y + I.y) / 2);
  }
  const C = { x: c.x + y.x, y: c.y + y.y }, S = { x: u.x + _.x, y: u.y + _.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...C.x !== p[0].x || C.y !== p[0].y ? [C] : [],
    ...p,
    ...S.x !== p[p.length - 1].x || S.y !== p[p.length - 1].y ? [S] : [],
    n
  ], v, x, m, w];
}
function of(e, t, n, o) {
  const r = Math.min(xr(e, t) / 2, xr(t, n) / 2, o), { x: i, y: s } = t;
  if (e.x === i && i === n.x || e.y === s && s === n.y)
    return `L${i} ${s}`;
  if (e.y === s) {
    const c = e.x < n.x ? -1 : 1, u = e.y < n.y ? 1 : -1;
    return `L ${i + r * c},${s}Q ${i},${s} ${i},${s + r * u}`;
  }
  const a = e.x < n.x ? 1 : -1, l = e.y < n.y ? -1 : 1;
  return `L ${i},${s + r * l}Q ${i},${s} ${i + r * a},${s}`;
}
function so({ sourceX: e, sourceY: t, sourcePosition: n = G.Bottom, targetX: o, targetY: r, targetPosition: i = G.Top, borderRadius: s = 5, centerX: a, centerY: l, offset: c = 20, stepPosition: u = 0.5 }) {
  const [d, f, g, p, v] = nf({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: i,
    center: { x: a, y: l },
    offset: c,
    stepPosition: u
  });
  let x = `M${d[0].x} ${d[0].y}`;
  for (let y = 1; y < d.length - 1; y++)
    x += of(d[y - 1], d[y], d[y + 1], s);
  return x += `L${d[d.length - 1].x} ${d[d.length - 1].y}`, [x, f, g, p, v];
}
function wr(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function rf(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!wr(t) || !wr(n))
    return null;
  const o = t.internals.handleBounds || vr(t.handles), r = n.internals.handleBounds || vr(n.handles), i = br(o?.source ?? [], e.sourceHandle), s = br(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === dt.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!i || !s)
    return e.onError?.("008", Pe.error008(i ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const a = i?.position || G.Bottom, l = s?.position || G.Top, c = nt(t, i, a), u = nt(n, s, l);
  return {
    sourceX: c.x,
    sourceY: c.y,
    targetX: u.x,
    targetY: u.y,
    sourcePosition: a,
    targetPosition: l
  };
}
function vr(e) {
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
function nt(e, t, n = G.Left, o = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, i = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: s, height: a } = t ?? Be(e);
  if (o)
    return { x: r + s / 2, y: i + a / 2 };
  switch (t?.position ?? n) {
    case G.Top:
      return { x: r + s / 2, y: i };
    case G.Right:
      return { x: r + s, y: i + a / 2 };
    case G.Bottom:
      return { x: r + s / 2, y: i + a };
    case G.Left:
      return { x: r, y: i + a / 2 };
  }
}
function br(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function ao(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function sf(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const i = /* @__PURE__ */ new Set();
  return e.reduce((s, a) => ([a.markerStart || o, a.markerEnd || r].forEach((l) => {
    if (l && typeof l == "object") {
      const c = ao(l, t);
      i.has(c) || (s.push({ id: c, color: l.color || n, ...l }), i.add(c));
    }
  }), s), []).sort((s, a) => s.id.localeCompare(a.id));
}
const ts = 1e3, af = 10, Mo = {
  nodeOrigin: [0, 0],
  nodeExtent: At,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, cf = {
  ...Mo,
  checkEquality: !0
};
function Ao(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function lf(e, t, n) {
  const o = Ao(Mo, n);
  for (const r of e.values())
    if (r.parentId)
      Po(r, e, t, o);
    else {
      const i = Ot(r, o.nodeOrigin), s = tt(r.extent) ? r.extent : o.nodeExtent, a = et(i, s, Be(r));
      r.internals.positionAbsolute = a;
    }
}
function uf(e, t) {
  if (!e.handles)
    return e.measured ? t?.internals.handleBounds : void 0;
  const n = [], o = [];
  for (const r of e.handles) {
    const i = {
      id: r.id,
      width: r.width ?? 1,
      height: r.height ?? 1,
      nodeId: e.id,
      x: r.x,
      y: r.y,
      position: r.position,
      type: r.type
    };
    r.type === "source" ? n.push(i) : r.type === "target" && o.push(i);
  }
  return {
    source: n,
    target: o
  };
}
function Do(e) {
  return e === "manual";
}
function co(e, t, n, o = {}) {
  const r = Ao(cf, o), i = { i: 0 }, s = new Map(t), a = r?.elevateNodesOnSelect && !Do(r.zIndexMode) ? ts : 0;
  let l = e.length > 0, c = !1;
  t.clear(), n.clear();
  for (const u of e) {
    let d = s.get(u.id);
    if (r.checkEquality && u === d?.internals.userNode)
      t.set(u.id, d);
    else {
      const f = Ot(u, r.nodeOrigin), g = tt(u.extent) ? u.extent : r.nodeExtent, p = et(f, g, Be(u));
      d = {
        ...r.defaults,
        ...u,
        measured: {
          width: u.measured?.width,
          height: u.measured?.height
        },
        internals: {
          positionAbsolute: p,
          // if user re-initializes the node or removes `measured` for whatever reason, we reset the handleBounds so that the node gets re-measured
          handleBounds: uf(u, d),
          z: ns(u, a, r.zIndexMode),
          userNode: u
        }
      }, t.set(u.id, d);
    }
    (d.measured === void 0 || d.measured.width === void 0 || d.measured.height === void 0) && !d.hidden && (l = !1), u.parentId && Po(d, t, n, o, i), c ||= u.selected ?? !1;
  }
  return { nodesInitialized: l, hasSelectedNodes: c };
}
function df(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function Po(e, t, n, o, r) {
  const { elevateNodesOnSelect: i, nodeOrigin: s, nodeExtent: a, zIndexMode: l } = Ao(Mo, o), c = e.parentId, u = t.get(c);
  if (!u) {
    console.warn(`Parent node ${c} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  df(e, n), r && !u.parentId && u.internals.rootParentIndex === void 0 && l === "auto" && (u.internals.rootParentIndex = ++r.i, u.internals.z = u.internals.z + r.i * af), r && u.internals.rootParentIndex !== void 0 && (r.i = u.internals.rootParentIndex);
  const d = i && !Do(l) ? ts : 0, { x: f, y: g, z: p } = ff(e, u, s, a, d, l), { positionAbsolute: v } = e.internals, x = f !== v.x || g !== v.y;
  (x || p !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: x ? { x: f, y: g } : v,
      z: p
    }
  });
}
function ns(e, t, n) {
  const o = Ae(e.zIndex) ? e.zIndex : 0;
  return Do(n) ? o : o + (e.selected ? t : 0);
}
function ff(e, t, n, o, r, i) {
  const { x: s, y: a } = t.internals.positionAbsolute, l = Be(e), c = Ot(e, n), u = tt(e.extent) ? et(c, e.extent, l) : c;
  let d = et({ x: s + u.x, y: a + u.y }, o, l);
  e.extent === "parent" && (d = Fi(d, l, t));
  const f = ns(e, r, i), g = t.internals.z ?? 0;
  return {
    x: d.x,
    y: d.y,
    z: g >= f ? g + 1 : f
  };
}
function $o(e, t, n, o = [0, 0]) {
  const r = [], i = /* @__PURE__ */ new Map();
  for (const s of e) {
    const a = t.get(s.parentId);
    if (!a)
      continue;
    const l = i.get(s.parentId)?.expandedRect ?? ht(a), c = Yi(l, s.rect);
    i.set(s.parentId, { expandedRect: c, parent: a });
  }
  return i.size > 0 && i.forEach(({ expandedRect: s, parent: a }, l) => {
    const c = a.internals.positionAbsolute, u = Be(a), d = a.origin ?? o, f = s.x < c.x ? Math.round(Math.abs(c.x - s.x)) : 0, g = s.y < c.y ? Math.round(Math.abs(c.y - s.y)) : 0, p = Math.max(u.width, Math.round(s.width)), v = Math.max(u.height, Math.round(s.height)), x = (p - u.width) * d[0], y = (v - u.height) * d[1];
    (f > 0 || g > 0 || x || y) && (r.push({
      id: l,
      type: "position",
      position: {
        x: a.position.x - f + x,
        y: a.position.y - g + y
      }
    }), n.get(l)?.forEach((_) => {
      e.some((m) => m.id === _.id) || r.push({
        id: _.id,
        type: "position",
        position: {
          x: _.position.x + f,
          y: _.position.y + g
        }
      });
    })), (u.width < s.width || u.height < s.height || f || g) && r.push({
      id: l,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: p + (f ? d[0] * f - x : 0),
        height: v + (g ? d[1] * g - y : 0)
      }
    });
  }), r;
}
function hf(e, t, n, o, r, i, s) {
  const a = o?.querySelector(".xyflow__viewport");
  let l = !1;
  if (!a)
    return { changes: [], updatedInternals: l };
  const c = [], u = window.getComputedStyle(a), { m22: d } = new window.DOMMatrixReadOnly(u.transform), f = [];
  for (const g of e.values()) {
    const p = t.get(g.id);
    if (!p)
      continue;
    if (p.hidden) {
      t.set(p.id, {
        ...p,
        internals: {
          ...p.internals,
          handleBounds: void 0
        }
      }), l = !0;
      continue;
    }
    const v = Io(g.nodeElement), x = p.measured.width !== v.width || p.measured.height !== v.height;
    if (!!(v.width && v.height && (x || !p.internals.handleBounds || g.force))) {
      const _ = g.nodeElement.getBoundingClientRect(), m = tt(p.extent) ? p.extent : i;
      let { positionAbsolute: w } = p.internals;
      p.parentId && p.extent === "parent" ? w = Fi(w, v, t.get(p.parentId)) : m && (w = et(w, m, v));
      const C = {
        ...p,
        measured: v,
        internals: {
          ...p.internals,
          positionAbsolute: w,
          handleBounds: {
            source: pr("source", g.nodeElement, _, d, p.id),
            target: pr("target", g.nodeElement, _, d, p.id)
          }
        }
      };
      t.set(p.id, C), p.parentId && Po(C, t, n, { nodeOrigin: r, zIndexMode: s }), l = !0, x && (c.push({
        id: p.id,
        type: "dimensions",
        dimensions: v
      }), p.expandParent && p.parentId && f.push({
        id: p.id,
        parentId: p.parentId,
        rect: ht(C, r)
      }));
    }
  }
  if (f.length > 0) {
    const g = $o(f, t, n, r);
    c.push(...g);
  }
  return { changes: c, updatedInternals: l };
}
async function gf({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: i }) {
  if (!t || !e.x && !e.y)
    return !1;
  const s = await t.setViewportConstrained({
    x: n[0] + e.x,
    y: n[1] + e.y,
    zoom: n[2]
  }, [
    [0, 0],
    [r, i]
  ], o);
  return !!s && (s.x !== n[0] || s.y !== n[1] || s.k !== n[2]);
}
function Sr(e, t, n, o, r, i) {
  let s = r;
  const a = o.get(s) || /* @__PURE__ */ new Map();
  o.set(s, a.set(n, t)), s = `${r}-${e}`;
  const l = o.get(s) || /* @__PURE__ */ new Map();
  if (o.set(s, l.set(n, t)), i) {
    s = `${r}-${e}-${i}`;
    const c = o.get(s) || /* @__PURE__ */ new Map();
    o.set(s, c.set(n, t));
  }
}
function os(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: i, sourceHandle: s = null, targetHandle: a = null } = o, l = { edgeId: o.id, source: r, target: i, sourceHandle: s, targetHandle: a }, c = `${r}-${s}--${i}-${a}`, u = `${i}-${a}--${r}-${s}`;
    Sr("source", l, u, e, r, s), Sr("target", l, c, e, i, a), t.set(o.id, o);
  }
}
function rs(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : rs(n, t) : !1;
}
function _r(e, t, n) {
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
function pf(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [i, s] of e)
    if ((s.selected || s.id === o) && (!s.parentId || !rs(s, e)) && (s.draggable || t && typeof s.draggable > "u")) {
      const a = e.get(i);
      a && r.set(i, {
        id: i,
        position: a.position || { x: 0, y: 0 },
        distance: {
          x: n.x - a.internals.positionAbsolute.x,
          y: n.y - a.internals.positionAbsolute.y
        },
        extent: a.extent,
        parentId: a.parentId,
        origin: a.origin,
        expandParent: a.expandParent,
        internals: {
          positionAbsolute: a.internals.positionAbsolute || { x: 0, y: 0 }
        },
        measured: {
          width: a.measured.width ?? 0,
          height: a.measured.height ?? 0
        }
      });
    }
  return r;
}
function Hn({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
  const r = [];
  for (const [s, a] of t) {
    const l = n.get(s)?.internals.userNode;
    l && r.push({
      ...l,
      position: a.position,
      dragging: o
    });
  }
  if (!e)
    return [r[0], r];
  const i = n.get(e)?.internals.userNode;
  return [
    i ? {
      ...i,
      position: t.get(e)?.position || i.position,
      dragging: o
    } : r[0],
    r
  ];
}
function mf({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const i = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, s = Bt(i, t);
  return {
    x: s.x - i.x,
    y: s.y - i.y
  };
}
function yf({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let i = { x: null, y: null }, s = 0, a = /* @__PURE__ */ new Map(), l = !1, c = { x: 0, y: 0 }, u = null, d = !1, f = null, g = !1, p = !1, v = null;
  function x({ noDragClassName: _, handleSelector: m, domNode: w, isSelectable: C, nodeId: S, nodeClickDistance: M = 0 }) {
    f = be(w);
    function A({ x: T, y: L }) {
      const { nodeLookup: b, nodeExtent: N, snapGrid: E, snapToGrid: k, nodeOrigin: j, onNodeDrag: D, onSelectionDrag: V, onError: O, updateNodePositions: H } = t();
      i = { x: T, y: L };
      let q = !1;
      const X = a.size > 1, Z = X && N ? io(Vt(a)) : null, ne = X && k ? mf({
        dragItems: a,
        snapGrid: E,
        x: T,
        y: L
      }) : null;
      for (const [U, R] of a) {
        if (!b.has(U))
          continue;
        let W = { x: T - R.distance.x, y: L - R.distance.y };
        k && (W = ne ? {
          x: Math.round(W.x + ne.x),
          y: Math.round(W.y + ne.y)
        } : Bt(W, E));
        let ee = null;
        if (X && N && !R.extent && Z) {
          const { positionAbsolute: F } = R.internals, J = F.x - Z.x + N[0][0], oe = F.x + R.measured.width - Z.x2 + N[1][0], de = F.y - Z.y + N[0][1], xe = F.y + R.measured.height - Z.y2 + N[1][1];
          ee = [
            [J, de],
            [oe, xe]
          ];
        }
        const { position: Q, positionAbsolute: $ } = Bi({
          nodeId: U,
          nextPosition: W,
          nodeLookup: b,
          nodeExtent: ee || N,
          nodeOrigin: j,
          onError: O
        });
        q = q || R.position.x !== Q.x || R.position.y !== Q.y, R.position = Q, R.internals.positionAbsolute = $;
      }
      if (p = p || q, !!q && (H(a, !0), v && (o || D || !S && V))) {
        const [U, R] = Hn({
          nodeId: S,
          dragItems: a,
          nodeLookup: b
        });
        o?.(v, a, U, R), D?.(v, U, R), S || V?.(v, R);
      }
    }
    async function P() {
      if (!u)
        return;
      const { transform: T, panBy: L, autoPanSpeed: b, autoPanOnNodeDrag: N } = t();
      if (!N) {
        l = !1, cancelAnimationFrame(s);
        return;
      }
      const [E, k] = Co(c, u, b);
      (E !== 0 || k !== 0) && (i.x = (i.x ?? 0) - E / T[2], i.y = (i.y ?? 0) - k / T[2], await L({ x: E, y: k }) && A(i)), s = requestAnimationFrame(P);
    }
    function B(T) {
      const { nodeLookup: L, multiSelectionActive: b, nodesDraggable: N, transform: E, snapGrid: k, snapToGrid: j, selectNodesOnDrag: D, onNodeDragStart: V, onSelectionDragStart: O, unselectNodesAndEdges: H } = t();
      d = !0, (!D || !C) && !b && S && (L.get(S)?.selected || H()), C && D && S && e?.(S);
      const q = Nt(T.sourceEvent, { transform: E, snapGrid: k, snapToGrid: j, containerBounds: u });
      if (i = q, a = pf(L, N, q, S), a.size > 0 && (n || V || !S && O)) {
        const [X, Z] = Hn({
          nodeId: S,
          dragItems: a,
          nodeLookup: L
        });
        n?.(T.sourceEvent, a, X, Z), V?.(T.sourceEvent, X, Z), S || O?.(T.sourceEvent, Z);
      }
    }
    const I = bi().clickDistance(M).on("start", (T) => {
      const { domNode: L, nodeDragThreshold: b, transform: N, snapGrid: E, snapToGrid: k } = t();
      u = L?.getBoundingClientRect() || null, g = !1, p = !1, v = T.sourceEvent, b === 0 && B(T), i = Nt(T.sourceEvent, { transform: N, snapGrid: E, snapToGrid: k, containerBounds: u }), c = De(T.sourceEvent, u);
    }).on("drag", (T) => {
      const { autoPanOnNodeDrag: L, transform: b, snapGrid: N, snapToGrid: E, nodeDragThreshold: k, nodeLookup: j } = t(), D = Nt(T.sourceEvent, { transform: b, snapGrid: N, snapToGrid: E, containerBounds: u });
      if (v = T.sourceEvent, (T.sourceEvent.type === "touchmove" && T.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      S && !j.has(S)) && (g = !0), !g) {
        if (!l && L && d && (l = !0, P()), !d) {
          const V = De(T.sourceEvent, u), O = V.x - c.x, H = V.y - c.y;
          Math.sqrt(O * O + H * H) > k && B(T);
        }
        (i.x !== D.xSnapped || i.y !== D.ySnapped) && a && d && (c = De(T.sourceEvent, u), A(D));
      }
    }).on("end", (T) => {
      if (!d || g) {
        g && a.size > 0 && t().updateNodePositions(a, !1);
        return;
      }
      if (l = !1, d = !1, cancelAnimationFrame(s), a.size > 0) {
        const { nodeLookup: L, updateNodePositions: b, onNodeDragStop: N, onSelectionDragStop: E } = t();
        if (p && (b(a, !1), p = !1), r || N || !S && E) {
          const [k, j] = Hn({
            nodeId: S,
            dragItems: a,
            nodeLookup: L,
            dragging: !1
          });
          r?.(T.sourceEvent, a, k, j), N?.(T.sourceEvent, k, j), S || E?.(T.sourceEvent, j);
        }
      }
    }).filter((T) => {
      const L = T.target;
      return !T.button && (!_ || !_r(L, `.${_}`, w)) && (!m || _r(L, m, w));
    });
    f.call(I);
  }
  function y() {
    f?.on(".drag", null);
  }
  return {
    update: x,
    destroy: y
  };
}
function xf(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const i of t.values())
    Pt(r, ht(i)) > 0 && o.push(i);
  return o;
}
const wf = 250;
function vf(e, t, n, o) {
  let r = [], i = 1 / 0;
  const s = xf(e, n, t + wf);
  for (const a of s) {
    const l = [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []];
    for (const c of l) {
      if (o.nodeId === c.nodeId && o.type === c.type && o.id === c.id)
        continue;
      const { x: u, y: d } = nt(a, c, c.position, !0), f = Math.sqrt(Math.pow(u - e.x, 2) + Math.pow(d - e.y, 2));
      f > t || (f < i ? (r = [{ ...c, x: u, y: d }], i = f) : f === i && r.push({ ...c, x: u, y: d }));
    }
  }
  if (!r.length)
    return null;
  if (r.length > 1) {
    const a = o.type === "source" ? "target" : "source";
    return r.find((l) => l.type === a) ?? r[0];
  }
  return r[0];
}
function is(e, t, n, o, r, i = !1) {
  const s = o.get(e);
  if (!s)
    return null;
  const a = r === "strict" ? s.internals.handleBounds?.[t] : [...s.internals.handleBounds?.source ?? [], ...s.internals.handleBounds?.target ?? []], l = (n ? a?.find((c) => c.id === n) : a?.[0]) ?? null;
  return l && i ? { ...l, ...nt(s, l, l.position, !0) } : l;
}
function ss(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function bf(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const as = () => !0;
function Sf(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: i, isTarget: s, domNode: a, nodeLookup: l, lib: c, autoPanOnConnect: u, flowId: d, panBy: f, cancelConnection: g, onConnectStart: p, onConnect: v, onConnectEnd: x, isValidConnection: y = as, onReconnectEnd: _, updateConnection: m, getTransform: w, getFromHandle: C, autoPanSpeed: S, dragThreshold: M = 1, handleDomNode: A }) {
  const P = Zi(e.target);
  let B = 0, I;
  const { x: T, y: L } = De(e), b = ss(i, A), N = a?.getBoundingClientRect();
  let E = !1;
  if (!N || !b)
    return;
  const k = is(r, b, o, l, t);
  if (!k)
    return;
  let j = De(e, N), D = !1, V = null, O = !1, H = null;
  function q() {
    if (!u || !N)
      return;
    const [Q, $] = Co(j, N, S);
    f({ x: Q, y: $ }), B = requestAnimationFrame(q);
  }
  const X = {
    ...k,
    nodeId: r,
    type: b,
    position: k.position
  }, Z = l.get(r);
  let U = {
    inProgress: !0,
    isValid: null,
    from: nt(Z, X, G.Left, !0),
    fromHandle: X,
    fromPosition: X.position,
    fromNode: Z,
    to: j,
    toHandle: null,
    toPosition: dr[X.position],
    toNode: null,
    pointer: j
  };
  function R() {
    E = !0, m(U), p?.(e, { nodeId: r, handleId: o, handleType: b });
  }
  M === 0 && R();
  function W(Q) {
    if (!E) {
      const { x: xe, y: Ce } = De(Q), Ee = xe - T, Ne = Ce - L;
      if (!(Ee * Ee + Ne * Ne > M * M))
        return;
      R();
    }
    if (!C() || !X) {
      ee(Q);
      return;
    }
    const $ = w();
    j = De(Q, N), I = vf(yt(j, $, !1, [1, 1]), n, l, X), D || (q(), D = !0);
    const F = cs(Q, {
      handle: I,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: s ? "target" : "source",
      isValidConnection: y,
      doc: P,
      lib: c,
      flowId: d,
      nodeLookup: l
    });
    H = F.handleDomNode, V = F.connection, O = bf(!!I, F.isValid);
    const J = l.get(r), oe = J ? nt(J, X, G.Left, !0) : U.from, de = {
      ...U,
      from: oe,
      isValid: O,
      to: F.toHandle && O ? gt({ x: F.toHandle.x, y: F.toHandle.y }, $) : j,
      toHandle: F.toHandle,
      toPosition: O && F.toHandle ? F.toHandle.position : dr[X.position],
      toNode: F.toHandle ? l.get(F.toHandle.nodeId) : null,
      pointer: j
    };
    m(de), U = de;
  }
  function ee(Q) {
    if (!("touches" in Q && Q.touches.length > 0)) {
      if (E) {
        (I || H) && V && O && v?.(V);
        const { inProgress: $, ...F } = U, J = {
          ...F,
          toPosition: U.toHandle ? U.toPosition : null
        };
        x?.(Q, J), i && _?.(Q, J);
      }
      g(), cancelAnimationFrame(B), D = !1, O = !1, V = null, H = null, P.removeEventListener("mousemove", W), P.removeEventListener("mouseup", ee), P.removeEventListener("touchmove", W), P.removeEventListener("touchend", ee);
    }
  }
  P.addEventListener("mousemove", W), P.addEventListener("mouseup", ee), P.addEventListener("touchmove", W), P.addEventListener("touchend", ee);
}
function cs(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: i, doc: s, lib: a, flowId: l, isValidConnection: c = as, nodeLookup: u }) {
  const d = i === "target", f = t ? s.querySelector(`.${a}-flow__handle[data-id="${l}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: g, y: p } = De(e), v = s.elementFromPoint(g, p), x = v?.classList.contains(`${a}-flow__handle`) ? v : f, y = {
    handleDomNode: x,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (x) {
    const _ = ss(void 0, x), m = x.getAttribute("data-nodeid"), w = x.getAttribute("data-handleid"), C = x.classList.contains("connectable"), S = x.classList.contains("connectableend");
    if (!m || !_)
      return y;
    const M = {
      source: d ? m : o,
      sourceHandle: d ? w : r,
      target: d ? o : m,
      targetHandle: d ? r : w
    };
    y.connection = M;
    const P = C && S && (n === dt.Strict ? d && _ === "source" || !d && _ === "target" : m !== o || w !== r);
    y.isValid = P && c(M), y.toHandle = is(m, _, w, u, n, !0);
  }
  return y;
}
const lo = {
  onPointerDown: Sf,
  isValid: cs
};
function _f({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = be(e);
  function i({ translateExtent: a, width: l, height: c, zoomStep: u = 1, pannable: d = !0, zoomable: f = !0, inversePan: g = !1 }) {
    const p = (m) => {
      if (m.sourceEvent.type !== "wheel" || !t)
        return;
      const w = n(), C = m.sourceEvent.ctrlKey && $t() ? 10 : 1, S = -m.sourceEvent.deltaY * (m.sourceEvent.deltaMode === 1 ? 0.05 : m.sourceEvent.deltaMode ? 1 : 2e-3) * u, M = w[2] * Math.pow(2, S * C);
      t.scaleTo(M);
    };
    let v = [0, 0];
    const x = (m) => {
      (m.sourceEvent.type === "mousedown" || m.sourceEvent.type === "touchstart") && (v = [
        m.sourceEvent.clientX ?? m.sourceEvent.touches[0].clientX,
        m.sourceEvent.clientY ?? m.sourceEvent.touches[0].clientY
      ]);
    }, y = (m) => {
      const w = n();
      if (m.sourceEvent.type !== "mousemove" && m.sourceEvent.type !== "touchmove" || !t)
        return;
      const C = [
        m.sourceEvent.clientX ?? m.sourceEvent.touches[0].clientX,
        m.sourceEvent.clientY ?? m.sourceEvent.touches[0].clientY
      ], S = [C[0] - v[0], C[1] - v[1]];
      v = C;
      const M = o() * Math.max(w[2], Math.log(w[2])) * (g ? -1 : 1), A = {
        x: w[0] - S[0] * M,
        y: w[1] - S[1] * M
      }, P = [
        [0, 0],
        [l, c]
      ];
      t.setViewportConstrained({
        x: A.x,
        y: A.y,
        zoom: w[2]
      }, P, a);
    }, _ = zi().on("start", x).on("zoom", d ? y : null).on("zoom.wheel", f ? p : null);
    r.call(_, {});
  }
  function s() {
    r.on("zoom", null);
  }
  return {
    update: i,
    destroy: s,
    pointer: Ie
  };
}
const En = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), On = ({ x: e, y: t, zoom: n }) => bn.translate(e, t).scale(n), it = (e, t) => e.target.closest(`.${t}`), ls = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Ef = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, Vn = (e, t = 0, n = Ef, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, us = (e) => {
  const t = e.ctrlKey && $t() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Nf({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: i, zoomOnPinch: s, onPanZoomStart: a, onPanZoom: l, onPanZoomEnd: c }) {
  return (u) => {
    if (it(u, t))
      return u.ctrlKey && u.preventDefault(), !1;
    u.preventDefault(), u.stopImmediatePropagation();
    const d = n.property("__zoom").k || 1;
    if (u.ctrlKey && s) {
      const x = Ie(u), y = us(u), _ = d * Math.pow(2, y);
      o.scaleTo(n, _, x, u);
      return;
    }
    const f = u.deltaMode === 1 ? 20 : 1;
    let g = r === Ke.Vertical ? 0 : u.deltaX * f, p = r === Ke.Horizontal ? 0 : u.deltaY * f;
    !$t() && u.shiftKey && r !== Ke.Vertical && (g = u.deltaY * f, p = 0), o.translateBy(
      n,
      -(g / d) * i,
      -(p / d) * i,
      // @ts-ignore
      { internal: !0 }
    );
    const v = En(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (l?.(u, v), e.panScrollTimeout = setTimeout(() => {
      c?.(u, v), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, a?.(u, v));
  };
}
function Cf({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const i = o.type === "wheel", s = !t && i && !o.ctrlKey, a = it(o, e);
    if (o.ctrlKey && i && a && o.preventDefault(), s || a)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function kf({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = En(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function If({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (i) => {
    e.usedRightMouseButton = !!(n && ls(t, e.mouseButton ?? 0)), i.sourceEvent?.sync || o([i.transform.x, i.transform.y, i.transform.k]), r && !i.sourceEvent?.internal && r?.(i.sourceEvent, En(i.transform));
  };
}
function Mf({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: i }) {
  return (s) => {
    if (!s.sourceEvent?.internal && (e.isZoomingOrPanning = !1, i && ls(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && s.sourceEvent && i(s.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
      const a = En(s.transform);
      e.prevViewport = a, clearTimeout(e.timerId), e.timerId = setTimeout(
        () => {
          r?.(s.sourceEvent, a);
        },
        // we need a setTimeout for panOnScroll to suppress multiple end events fired during scroll
        n ? 150 : 0
      );
    }
  };
}
function Af({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: i, userSelectionActive: s, noWheelClassName: a, noPanClassName: l, lib: c, connectionInProgress: u }) {
  return (d) => {
    const f = e || t, g = n && d.ctrlKey, p = d.type === "wheel";
    if (d.button === 1 && d.type === "mousedown" && (it(d, `${c}-flow__node`) || it(d, `${c}-flow__edge`)))
      return !0;
    if (!o && !f && !r && !i && !n || s || u && !p || it(d, a) && p || it(d, l) && (!p || r && p && !e) || !n && d.ctrlKey && p)
      return !1;
    if (!n && d.type === "touchstart" && d.touches?.length > 1)
      return d.preventDefault(), !1;
    if (!f && !r && !g && p || !o && (d.type === "mousedown" || d.type === "touchstart") || Array.isArray(o) && !o.includes(d.button) && d.type === "mousedown")
      return !1;
    const v = Array.isArray(o) && o.includes(d.button) || !d.button || d.button <= 1;
    return (!d.ctrlKey || p) && v;
  };
}
function Df({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: i, onPanZoomStart: s, onPanZoomEnd: a, onDraggingChange: l }) {
  const c = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, u = e.getBoundingClientRect(), d = zi().scaleExtent([t, n]).translateExtent(o), f = be(e).call(d);
  _({
    x: r.x,
    y: r.y,
    zoom: ft(r.zoom, t, n)
  }, [
    [0, 0],
    [u.width, u.height]
  ], o);
  const g = f.on("wheel.zoom"), p = f.on("dblclick.zoom");
  d.wheelDelta(us);
  async function v(I, T) {
    return f ? new Promise((L) => {
      d?.interpolate(T?.interpolate === "linear" ? Et : en).transform(Vn(f, T?.duration, T?.ease, () => L(!0)), I);
    }) : !1;
  }
  function x({ noWheelClassName: I, noPanClassName: T, onPaneContextMenu: L, userSelectionActive: b, panOnScroll: N, panOnDrag: E, panOnScrollMode: k, panOnScrollSpeed: j, preventScrolling: D, zoomOnPinch: V, zoomOnScroll: O, zoomOnDoubleClick: H, zoomActivationKeyPressed: q, lib: X, onTransformChange: Z, connectionInProgress: ne, paneClickDistance: U, selectionOnDrag: R }) {
    b && !c.isZoomingOrPanning && y();
    const W = N && !q && !b;
    d.clickDistance(R ? 1 / 0 : !Ae(U) || U < 0 ? 0 : U);
    const ee = W ? Nf({
      zoomPanValues: c,
      noWheelClassName: I,
      d3Selection: f,
      d3Zoom: d,
      panOnScrollMode: k,
      panOnScrollSpeed: j,
      zoomOnPinch: V,
      onPanZoomStart: s,
      onPanZoom: i,
      onPanZoomEnd: a
    }) : Cf({
      noWheelClassName: I,
      preventScrolling: D,
      d3ZoomHandler: g
    });
    f.on("wheel.zoom", ee, { passive: !1 });
    const Q = kf({
      zoomPanValues: c,
      onDraggingChange: l,
      onPanZoomStart: s
    });
    d.on("start", Q);
    const $ = If({
      zoomPanValues: c,
      panOnDrag: E,
      onPaneContextMenu: !!L,
      onPanZoom: i,
      onTransformChange: Z
    });
    d.on("zoom", $);
    const F = Mf({
      zoomPanValues: c,
      panOnDrag: E,
      panOnScroll: N,
      onPaneContextMenu: L,
      onPanZoomEnd: a,
      onDraggingChange: l
    });
    d.on("end", F);
    const J = Af({
      zoomActivationKeyPressed: q,
      panOnDrag: E,
      zoomOnScroll: O,
      panOnScroll: N,
      zoomOnDoubleClick: H,
      zoomOnPinch: V,
      userSelectionActive: b,
      noPanClassName: T,
      noWheelClassName: I,
      lib: X,
      connectionInProgress: ne
    });
    d.filter(J), H ? f.on("dblclick.zoom", p) : f.on("dblclick.zoom", null);
  }
  function y() {
    d.on("zoom", null);
  }
  async function _(I, T, L) {
    const b = On(I), N = d?.constrain()(b, T, L);
    return N && await v(N), N;
  }
  async function m(I, T) {
    const L = On(I);
    return await v(L, T), L;
  }
  function w(I) {
    if (f) {
      const T = On(I), L = f.property("__zoom");
      (L.k !== I.zoom || L.x !== I.x || L.y !== I.y) && d?.transform(f, T, null, { sync: !0 });
    }
  }
  function C() {
    const I = f ? Ti(f.node()) : { x: 0, y: 0, k: 1 };
    return { x: I.x, y: I.y, zoom: I.k };
  }
  async function S(I, T) {
    return f ? new Promise((L) => {
      d?.interpolate(T?.interpolate === "linear" ? Et : en).scaleTo(Vn(f, T?.duration, T?.ease, () => L(!0)), I);
    }) : !1;
  }
  async function M(I, T) {
    return f ? new Promise((L) => {
      d?.interpolate(T?.interpolate === "linear" ? Et : en).scaleBy(Vn(f, T?.duration, T?.ease, () => L(!0)), I);
    }) : !1;
  }
  function A(I) {
    d?.scaleExtent(I);
  }
  function P(I) {
    d?.translateExtent(I);
  }
  function B(I) {
    const T = !Ae(I) || I < 0 ? 0 : I;
    d?.clickDistance(T);
  }
  return {
    update: x,
    destroy: y,
    setViewport: m,
    setViewportConstrained: _,
    getViewport: C,
    scaleTo: S,
    scaleBy: M,
    setScaleExtent: A,
    setTranslateExtent: P,
    syncViewport: w,
    setClickDistance: B
  };
}
var pt;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(pt || (pt = {}));
function Pf({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: i }) {
  const s = e - t, a = n - o, l = [s > 0 ? 1 : s < 0 ? -1 : 0, a > 0 ? 1 : a < 0 ? -1 : 0];
  return s && r && (l[0] = l[0] * -1), a && i && (l[1] = l[1] * -1), l;
}
function Er(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: r
  };
}
function Fe(e, t) {
  return Math.max(0, t - e);
}
function Ye(e, t) {
  return Math.max(0, e - t);
}
function Gt(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function Nr(e, t) {
  return e ? !t : t;
}
function $f(e, t, n, o, r, i, s, a) {
  let { affectsX: l, affectsY: c } = t;
  const { isHorizontal: u, isVertical: d } = t, f = u && d, { xSnapped: g, ySnapped: p } = n, { minWidth: v, maxWidth: x, minHeight: y, maxHeight: _ } = o, { x: m, y: w, width: C, height: S, aspectRatio: M } = e;
  let A = Math.floor(u ? g - e.pointerX : 0), P = Math.floor(d ? p - e.pointerY : 0);
  const B = C + (l ? -A : A), I = S + (c ? -P : P), T = -i[0] * C, L = -i[1] * S;
  let b = Gt(B, v, x), N = Gt(I, y, _);
  if (s) {
    let j = 0, D = 0;
    l && A < 0 ? j = Fe(m + A + T, s[0][0]) : !l && A > 0 && (j = Ye(m + B + T, s[1][0])), c && P < 0 ? D = Fe(w + P + L, s[0][1]) : !c && P > 0 && (D = Ye(w + I + L, s[1][1])), b = Math.max(b, j), N = Math.max(N, D);
  }
  if (a) {
    let j = 0, D = 0;
    l && A > 0 ? j = Ye(m + A, a[0][0]) : !l && A < 0 && (j = Fe(m + B, a[1][0])), c && P > 0 ? D = Ye(w + P, a[0][1]) : !c && P < 0 && (D = Fe(w + I, a[1][1])), b = Math.max(b, j), N = Math.max(N, D);
  }
  if (r) {
    if (u) {
      const j = Gt(B / M, y, _) * M;
      if (b = Math.max(b, j), s) {
        let D = 0;
        !l && !c || l && !c && f ? D = Ye(w + L + B / M, s[1][1]) * M : D = Fe(w + L + (l ? A : -A) / M, s[0][1]) * M, b = Math.max(b, D);
      }
      if (a) {
        let D = 0;
        !l && !c || l && !c && f ? D = Fe(w + B / M, a[1][1]) * M : D = Ye(w + (l ? A : -A) / M, a[0][1]) * M, b = Math.max(b, D);
      }
    }
    if (d) {
      const j = Gt(I * M, v, x) / M;
      if (N = Math.max(N, j), s) {
        let D = 0;
        !l && !c || c && !l && f ? D = Ye(m + I * M + T, s[1][0]) / M : D = Fe(m + (c ? P : -P) * M + T, s[0][0]) / M, N = Math.max(N, D);
      }
      if (a) {
        let D = 0;
        !l && !c || c && !l && f ? D = Fe(m + I * M, a[1][0]) / M : D = Ye(m + (c ? P : -P) * M, a[0][0]) / M, N = Math.max(N, D);
      }
    }
  }
  P = P + (P < 0 ? N : -N), A = A + (A < 0 ? b : -b), r && (f ? B > I * M ? P = (Nr(l, c) ? -A : A) / M : A = (Nr(l, c) ? -P : P) * M : u ? (P = A / M, c = l) : (A = P * M, l = c));
  const E = l ? m + A : m, k = c ? w + P : w;
  return {
    width: C + (l ? -A : A),
    height: S + (c ? -P : P),
    x: i[0] * A * (l ? -1 : 1) + E,
    y: i[1] * P * (c ? -1 : 1) + k
  };
}
const ds = { width: 0, height: 0, x: 0, y: 0 }, jf = {
  ...ds,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function Tf(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, i = e.measured.width ?? 0, s = e.measured.height ?? 0, a = n[0] * i, l = n[1] * s;
  return [
    [o - a, r - l],
    [o + i - a, r + s - l]
  ];
}
function zf({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const i = be(e);
  let s = {
    controlDirection: Er("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function a({ controlPosition: c, boundaries: u, keepAspectRatio: d, resizeDirection: f, onResizeStart: g, onResize: p, onResizeEnd: v, shouldResize: x }) {
    let y = { ...ds }, _ = { ...jf };
    s = {
      boundaries: u,
      resizeDirection: f,
      keepAspectRatio: d,
      controlDirection: Er(c)
    };
    let m, w = null, C = [], S, M, A, P = !1;
    const B = bi().on("start", (I) => {
      const { nodeLookup: T, transform: L, snapGrid: b, snapToGrid: N, nodeOrigin: E, paneDomNode: k } = n();
      if (m = T.get(t), !m)
        return;
      w = k?.getBoundingClientRect() ?? null;
      const { xSnapped: j, ySnapped: D } = Nt(I.sourceEvent, {
        transform: L,
        snapGrid: b,
        snapToGrid: N,
        containerBounds: w
      });
      y = {
        width: m.measured.width ?? 0,
        height: m.measured.height ?? 0,
        x: m.position.x ?? 0,
        y: m.position.y ?? 0
      }, _ = {
        ...y,
        pointerX: j,
        pointerY: D,
        aspectRatio: y.width / y.height
      }, S = void 0, M = tt(m.extent) ? m.extent : void 0, m.parentId && (m.extent === "parent" || m.expandParent) && (S = T.get(m.parentId)), S && m.extent === "parent" && (M = [
        [0, 0],
        [S.measured.width, S.measured.height]
      ]), C = [], A = void 0;
      for (const [V, O] of T)
        if (O.parentId === t && (C.push({
          id: V,
          position: { ...O.position },
          extent: O.extent
        }), O.extent === "parent" || O.expandParent)) {
          const H = Tf(O, m, O.origin ?? E);
          A ? A = [
            [Math.min(H[0][0], A[0][0]), Math.min(H[0][1], A[0][1])],
            [Math.max(H[1][0], A[1][0]), Math.max(H[1][1], A[1][1])]
          ] : A = H;
        }
      g?.(I, { ...y });
    }).on("drag", (I) => {
      const { transform: T, snapGrid: L, snapToGrid: b, nodeOrigin: N } = n(), E = Nt(I.sourceEvent, {
        transform: T,
        snapGrid: L,
        snapToGrid: b,
        containerBounds: w
      }), k = [];
      if (!m)
        return;
      const { x: j, y: D, width: V, height: O } = y, H = {}, q = m.origin ?? N, { width: X, height: Z, x: ne, y: U } = $f(_, s.controlDirection, E, s.boundaries, s.keepAspectRatio, q, M, A), R = X !== V, W = Z !== O, ee = ne !== j && R, Q = U !== D && W;
      if (!ee && !Q && !R && !W)
        return;
      if ((ee || Q || q[0] === 1 || q[1] === 1) && (H.x = ee ? ne : y.x, H.y = Q ? U : y.y, y.x = H.x, y.y = H.y, C.length > 0)) {
        const oe = ne - j, de = U - D;
        for (const xe of C)
          xe.position = {
            x: xe.position.x - oe + q[0] * (X - V),
            y: xe.position.y - de + q[1] * (Z - O)
          }, k.push(xe);
      }
      if ((R || W) && (H.width = R && (!s.resizeDirection || s.resizeDirection === "horizontal") ? X : y.width, H.height = W && (!s.resizeDirection || s.resizeDirection === "vertical") ? Z : y.height, y.width = H.width, y.height = H.height), S && m.expandParent) {
        const oe = q[0] * (H.width ?? 0);
        H.x && H.x < oe && (y.x = oe, _.x = _.x - (H.x - oe));
        const de = q[1] * (H.height ?? 0);
        H.y && H.y < de && (y.y = de, _.y = _.y - (H.y - de));
      }
      const $ = Pf({
        width: y.width,
        prevWidth: V,
        height: y.height,
        prevHeight: O,
        affectsX: s.controlDirection.affectsX,
        affectsY: s.controlDirection.affectsY
      }), F = { ...y, direction: $ };
      x?.(I, F) !== !1 && (P = !0, p?.(I, F), o(H, k));
    }).on("end", (I) => {
      P && (v?.(I, { ...y }), r?.({ ...y }), P = !1);
    });
    i.call(B);
  }
  function l() {
    i.on(".drag", null);
  }
  return {
    update: a,
    destroy: l
  };
}
var Bn = { exports: {} }, Fn = {}, Yn = { exports: {} }, Xn = {};
var Cr;
function Rf() {
  if (Cr) return Xn;
  Cr = 1;
  var e = zt;
  function t(d, f) {
    return d === f && (d !== 0 || 1 / d === 1 / f) || d !== d && f !== f;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, i = e.useLayoutEffect, s = e.useDebugValue;
  function a(d, f) {
    var g = f(), p = o({ inst: { value: g, getSnapshot: f } }), v = p[0].inst, x = p[1];
    return i(
      function() {
        v.value = g, v.getSnapshot = f, l(v) && x({ inst: v });
      },
      [d, g, f]
    ), r(
      function() {
        return l(v) && x({ inst: v }), d(function() {
          l(v) && x({ inst: v });
        });
      },
      [d]
    ), s(g), g;
  }
  function l(d) {
    var f = d.getSnapshot;
    d = d.value;
    try {
      var g = f();
      return !n(d, g);
    } catch {
      return !0;
    }
  }
  function c(d, f) {
    return f();
  }
  var u = typeof window > "u" || typeof window.document > "u" || typeof window.document.createElement > "u" ? c : a;
  return Xn.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : u, Xn;
}
var kr;
function Lf() {
  return kr || (kr = 1, Yn.exports = Rf()), Yn.exports;
}
var Ir;
function Hf() {
  if (Ir) return Fn;
  Ir = 1;
  var e = zt, t = Lf();
  function n(c, u) {
    return c === u && (c !== 0 || 1 / c === 1 / u) || c !== c && u !== u;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, i = e.useRef, s = e.useEffect, a = e.useMemo, l = e.useDebugValue;
  return Fn.useSyncExternalStoreWithSelector = function(c, u, d, f, g) {
    var p = i(null);
    if (p.current === null) {
      var v = { hasValue: !1, value: null };
      p.current = v;
    } else v = p.current;
    p = a(
      function() {
        function y(S) {
          if (!_) {
            if (_ = !0, m = S, S = f(S), g !== void 0 && v.hasValue) {
              var M = v.value;
              if (g(M, S))
                return w = M;
            }
            return w = S;
          }
          if (M = w, o(m, S)) return M;
          var A = f(S);
          return g !== void 0 && g(M, A) ? (m = S, M) : (m = S, w = A);
        }
        var _ = !1, m, w, C = d === void 0 ? null : d;
        return [
          function() {
            return y(u());
          },
          C === null ? void 0 : function() {
            return y(C());
          }
        ];
      },
      [u, d, f, g]
    );
    var x = r(c, p[0], p[1]);
    return s(
      function() {
        v.hasValue = !0, v.value = x;
      },
      [x]
    ), l(x), x;
  }, Fn;
}
var Mr;
function Of() {
  return Mr || (Mr = 1, Bn.exports = Hf()), Bn.exports;
}
var Vf = Of();
const Bf = /* @__PURE__ */ oc(Vf), Ff = {}, Ar = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (u, d) => {
    const f = typeof u == "function" ? u(t) : u;
    if (!Object.is(f, t)) {
      const g = t;
      t = d ?? (typeof f != "object" || f === null) ? f : Object.assign({}, t, f), n.forEach((p) => p(t, g));
    }
  }, r = () => t, l = { setState: o, getState: r, getInitialState: () => c, subscribe: (u) => (n.add(u), () => n.delete(u)), destroy: () => {
    (Ff ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, c = t = e(o, r, l);
  return l;
}, Yf = (e) => e ? Ar(e) : Ar, { useDebugValue: Xf } = zt, { useSyncExternalStoreWithSelector: Wf } = Bf, qf = (e) => e;
function fs(e, t = qf, n) {
  const o = Wf(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return Xf(o), o;
}
const Dr = (e, t) => {
  const n = Yf(e), o = (r, i = t) => fs(n, r, i);
  return Object.assign(o, n), o;
}, Zf = (e, t) => e ? Dr(e, t) : Dr;
function le(e, t) {
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
var Wn = { exports: {} }, we = {};
var Pr;
function Uf() {
  if (Pr) return we;
  Pr = 1;
  var e = zt;
  function t(l) {
    var c = "https://react.dev/errors/" + l;
    if (1 < arguments.length) {
      c += "?args[]=" + encodeURIComponent(arguments[1]);
      for (var u = 2; u < arguments.length; u++)
        c += "&args[]=" + encodeURIComponent(arguments[u]);
    }
    return "Minified React error #" + l + "; visit " + c + " for the full message or use the non-minified dev environment for full errors and additional helpful warnings.";
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
  function i(l, c, u) {
    var d = 3 < arguments.length && arguments[3] !== void 0 ? arguments[3] : null;
    return {
      $$typeof: r,
      key: d == null ? null : "" + d,
      children: l,
      containerInfo: c,
      implementation: u
    };
  }
  var s = e.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE;
  function a(l, c) {
    if (l === "font") return "";
    if (typeof c == "string")
      return c === "use-credentials" ? c : "";
  }
  return we.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, we.createPortal = function(l, c) {
    var u = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!c || c.nodeType !== 1 && c.nodeType !== 9 && c.nodeType !== 11)
      throw Error(t(299));
    return i(l, c, null, u);
  }, we.flushSync = function(l) {
    var c = s.T, u = o.p;
    try {
      if (s.T = null, o.p = 2, l) return l();
    } finally {
      s.T = c, o.p = u, o.d.f();
    }
  }, we.preconnect = function(l, c) {
    typeof l == "string" && (c ? (c = c.crossOrigin, c = typeof c == "string" ? c === "use-credentials" ? c : "" : void 0) : c = null, o.d.C(l, c));
  }, we.prefetchDNS = function(l) {
    typeof l == "string" && o.d.D(l);
  }, we.preinit = function(l, c) {
    if (typeof l == "string" && c && typeof c.as == "string") {
      var u = c.as, d = a(u, c.crossOrigin), f = typeof c.integrity == "string" ? c.integrity : void 0, g = typeof c.fetchPriority == "string" ? c.fetchPriority : void 0;
      u === "style" ? o.d.S(
        l,
        typeof c.precedence == "string" ? c.precedence : void 0,
        {
          crossOrigin: d,
          integrity: f,
          fetchPriority: g
        }
      ) : u === "script" && o.d.X(l, {
        crossOrigin: d,
        integrity: f,
        fetchPriority: g,
        nonce: typeof c.nonce == "string" ? c.nonce : void 0
      });
    }
  }, we.preinitModule = function(l, c) {
    if (typeof l == "string")
      if (typeof c == "object" && c !== null) {
        if (c.as == null || c.as === "script") {
          var u = a(
            c.as,
            c.crossOrigin
          );
          o.d.M(l, {
            crossOrigin: u,
            integrity: typeof c.integrity == "string" ? c.integrity : void 0,
            nonce: typeof c.nonce == "string" ? c.nonce : void 0
          });
        }
      } else c == null && o.d.M(l);
  }, we.preload = function(l, c) {
    if (typeof l == "string" && typeof c == "object" && c !== null && typeof c.as == "string") {
      var u = c.as, d = a(u, c.crossOrigin);
      o.d.L(l, u, {
        crossOrigin: d,
        integrity: typeof c.integrity == "string" ? c.integrity : void 0,
        nonce: typeof c.nonce == "string" ? c.nonce : void 0,
        type: typeof c.type == "string" ? c.type : void 0,
        fetchPriority: typeof c.fetchPriority == "string" ? c.fetchPriority : void 0,
        referrerPolicy: typeof c.referrerPolicy == "string" ? c.referrerPolicy : void 0,
        imageSrcSet: typeof c.imageSrcSet == "string" ? c.imageSrcSet : void 0,
        imageSizes: typeof c.imageSizes == "string" ? c.imageSizes : void 0,
        media: typeof c.media == "string" ? c.media : void 0
      });
    }
  }, we.preloadModule = function(l, c) {
    if (typeof l == "string")
      if (c) {
        var u = a(c.as, c.crossOrigin);
        o.d.m(l, {
          as: typeof c.as == "string" && c.as !== "script" ? c.as : void 0,
          crossOrigin: u,
          integrity: typeof c.integrity == "string" ? c.integrity : void 0
        });
      } else o.d.m(l);
  }, we.requestFormReset = function(l) {
    o.d.r(l);
  }, we.unstable_batchedUpdates = function(l, c) {
    return l(c);
  }, we.useFormState = function(l, c, u) {
    return s.H.useFormState(l, c, u);
  }, we.useFormStatus = function() {
    return s.H.useHostTransitionStatus();
  }, we.version = "19.2.7", we;
}
var $r;
function Gf() {
  if ($r) return Wn.exports;
  $r = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), Wn.exports = Uf(), Wn.exports;
}
Gf();
const Nn = mo(null), Kf = Nn.Provider, hs = Pe.error001("react");
function ce(e, t) {
  const n = Rt(Nn);
  if (n === null)
    throw new Error(hs);
  return fs(n, e, t);
}
function ue() {
  const e = Rt(Nn);
  if (e === null)
    throw new Error(hs);
  return ye(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const jr = { display: "none" }, Qf = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, gs = "react-flow__node-desc", ps = "react-flow__edge-desc", Jf = "react-flow__aria-live", eh = (e) => e.ariaLiveMessage, th = (e) => e.ariaLabelConfig;
function nh({ rfId: e }) {
  const t = ce(eh);
  return h.jsx("div", { id: `${Jf}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: Qf, children: t });
}
function oh({ rfId: e, disableKeyboardA11y: t }) {
  const n = ce(th);
  return h.jsxs(h.Fragment, { children: [h.jsx("div", { id: `${gs}-${e}`, style: jr, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), h.jsx("div", { id: `${ps}-${e}`, style: jr, children: n["edge.a11yDescription.default"] }), !t && h.jsx(nh, { rfId: e })] });
}
const Cn = mn(({ position: e = "top-left", children: t, className: n, style: o, ...r }, i) => {
  const s = `${e}`.split("-");
  return h.jsx("div", { className: ge(["react-flow__panel", n, ...s]), style: o, ref: i, ...r, children: t });
});
Cn.displayName = "Panel";
function rh({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : h.jsx(Cn, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: h.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const ih = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, Kt = (e) => e.id;
function sh(e, t) {
  return le(e.selectedNodes.map(Kt), t.selectedNodes.map(Kt)) && le(e.selectedEdges.map(Kt), t.selectedEdges.map(Kt));
}
function ah({ onSelectionChange: e }) {
  const t = ue(), { selectedNodes: n, selectedEdges: o } = ce(ih, sh);
  return se(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((i) => i(r));
  }, [n, o, e]), null;
}
const ch = (e) => !!e.onSelectionChangeHandlers;
function lh({ onSelectionChange: e }) {
  const t = ce(ch);
  return e || t ? h.jsx(ah, { onSelectionChange: e }) : null;
}
const ms = [0, 0], uh = { x: 0, y: 0, zoom: 1 }, dh = [
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
], Tr = [...dh, "rfId"], fh = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), zr = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: At,
  nodeOrigin: ms,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function hh(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: i, setNodeExtent: s, reset: a, setDefaultNodesAndEdges: l } = ce(fh, le), c = ue();
  se(() => (l(e.defaultNodes, e.defaultEdges), () => {
    u.current = zr, a();
  }), []);
  const u = ie(zr);
  return se(
    () => {
      for (const d of Tr) {
        const f = e[d], g = u.current[d];
        f !== g && (typeof e[d] > "u" || (d === "nodes" ? t(f) : d === "edges" ? n(f) : d === "minZoom" ? o(f) : d === "maxZoom" ? r(f) : d === "translateExtent" ? i(f) : d === "nodeExtent" ? s(f) : d === "ariaLabelConfig" ? c.setState({ ariaLabelConfig: Zd(f) }) : d === "fitView" ? c.setState({ fitViewQueued: f }) : d === "fitViewOptions" ? c.setState({ fitViewOptions: f }) : c.setState({ [d]: f })));
      }
      u.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    Tr.map((d) => e[d])
  ), null;
}
function Rr() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function gh(e) {
  const [t, n] = te(e === "system" ? null : e);
  return se(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = Rr(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : Rr()?.matches ? "dark" : "light";
}
const Lr = typeof document < "u" ? document : null;
function jt(e = null, t = { target: Lr, actInsideInputWithModifier: !0 }) {
  const [n, o] = te(!1), r = ie(!1), i = ie(/* @__PURE__ */ new Set([])), [s, a] = ye(() => {
    if (e !== null) {
      const c = (Array.isArray(e) ? e : [e]).filter((d) => typeof d == "string").map((d) => d.replace("+", `
`).replace(`

`, `
+`).split(`
`)), u = c.reduce((d, f) => d.concat(...f), []);
      return [c, u];
    }
    return [[], []];
  }, [e]);
  return se(() => {
    const l = t?.target ?? Lr, c = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const u = (g) => {
        if (r.current = g.ctrlKey || g.metaKey || g.shiftKey || g.altKey, (!r.current || r.current && !c) && Ui(g))
          return !1;
        const v = Or(g.code, a);
        if (i.current.add(g[v]), Hr(s, i.current, !1)) {
          const x = g.composedPath?.()?.[0] || g.target, y = x?.nodeName === "BUTTON" || x?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !y) && g.preventDefault(), o(!0);
        }
      }, d = (g) => {
        const p = Or(g.code, a);
        Hr(s, i.current, !0) ? (o(!1), i.current.clear()) : i.current.delete(g[p]), g.key === "Meta" && i.current.clear(), r.current = !1;
      }, f = () => {
        i.current.clear(), o(!1);
      };
      return l?.addEventListener("keydown", u), l?.addEventListener("keyup", d), window.addEventListener("blur", f), window.addEventListener("contextmenu", f), () => {
        l?.removeEventListener("keydown", u), l?.removeEventListener("keyup", d), window.removeEventListener("blur", f), window.removeEventListener("contextmenu", f);
      };
    }
  }, [e, o]), n;
}
function Hr(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function Or(e, t) {
  return t.includes(e) ? "code" : "key";
}
const ph = () => {
  const e = ue();
  return ye(() => ({
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
      const { transform: [o, r, i], panZoom: s } = e.getState();
      return s ? (await s.setViewport({
        x: t.x ?? o,
        y: t.y ?? r,
        zoom: t.zoom ?? i
      }, n), !0) : !1;
    },
    getViewport: () => {
      const [t, n, o] = e.getState().transform;
      return { x: t, y: n, zoom: o };
    },
    setCenter: async (t, n, o) => e.getState().setCenter(t, n, o),
    fitBounds: async (t, n) => {
      const { width: o, height: r, minZoom: i, maxZoom: s, panZoom: a } = e.getState(), l = ko(t, o, r, i, s, n?.padding ?? 0.1);
      return a ? (await a.setViewport(l, {
        duration: n?.duration,
        ease: n?.ease,
        interpolate: n?.interpolate
      }), !0) : !1;
    },
    screenToFlowPosition: (t, n = {}) => {
      const { transform: o, snapGrid: r, snapToGrid: i, domNode: s } = e.getState();
      if (!s)
        return t;
      const { x: a, y: l } = s.getBoundingClientRect(), c = {
        x: t.x - a,
        y: t.y - l
      }, u = n.snapGrid ?? r, d = n.snapToGrid ?? i;
      return yt(c, o, d, u);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: r, y: i } = o.getBoundingClientRect(), s = gt(t, n);
      return {
        x: s.x + r,
        y: s.y + i
      };
    }
  }), []);
};
function ys(e, t) {
  const n = [], o = /* @__PURE__ */ new Map(), r = [];
  for (const i of e)
    if (i.type === "add") {
      r.push(i);
      continue;
    } else if (i.type === "remove" || i.type === "replace")
      o.set(i.id, [i]);
    else {
      const s = o.get(i.id);
      s ? s.push(i) : o.set(i.id, [i]);
    }
  for (const i of t) {
    const s = o.get(i.id);
    if (!s) {
      n.push(i);
      continue;
    }
    if (s[0].type === "remove")
      continue;
    if (s[0].type === "replace") {
      n.push({ ...s[0].item });
      continue;
    }
    const a = { ...i };
    for (const l of s)
      mh(l, a);
    n.push(a);
  }
  return r.length && r.forEach((i) => {
    i.index !== void 0 ? n.splice(i.index, 0, { ...i.item }) : n.push({ ...i.item });
  }), n;
}
function mh(e, t) {
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
function xs(e, t) {
  return ys(e, t);
}
function ws(e, t) {
  return ys(e, t);
}
function Ze(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function st(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [r, i] of e) {
    const s = t.has(r);
    !(i.selected === void 0 && !s) && i.selected !== s && (n && (i.selected = s), o.push(Ze(i.id, s)));
  }
  return o;
}
function Vr({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((r) => [r.id, r]));
  for (const [r, i] of e.entries()) {
    const s = t.get(i.id), a = s?.internals?.userNode ?? s;
    a !== void 0 && a !== i && n.push({ id: i.id, item: i, type: "replace" }), a === void 0 && n.push({ item: i, type: "add", index: r });
  }
  for (const [r] of t)
    o.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function Br(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const yh = Xi();
function vs(e, t, n = {}) {
  return ef(e, t, {
    ...n,
    onError: n.onError ?? yh
  });
}
const Fr = (e) => Hd(e), xh = (e) => Vi(e);
function bs(e) {
  return mn(e);
}
const wh = typeof window < "u" ? nc : se;
function Yr(e) {
  const [t, n] = te(BigInt(0)), [o] = te(() => vh(() => n((r) => r + BigInt(1))));
  return wh(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function vh(e) {
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
const Ss = mo(null);
function bh({ children: e }) {
  const t = ue(), n = me((a) => {
    const { nodes: l = [], setNodes: c, hasDefaultNodes: u, onNodesChange: d, nodeLookup: f, fitViewQueued: g, onNodesChangeMiddlewareMap: p } = t.getState();
    let v = l;
    for (const y of a)
      v = typeof y == "function" ? y(v) : y;
    let x = Vr({
      items: v,
      lookup: f
    });
    for (const y of p.values())
      x = y(x);
    u && c(v), x.length > 0 ? d?.(x) : g && window.requestAnimationFrame(() => {
      const { fitViewQueued: y, nodes: _, setNodes: m } = t.getState();
      y && m(_);
    });
  }, []), o = Yr(n), r = me((a) => {
    const { edges: l = [], setEdges: c, hasDefaultEdges: u, onEdgesChange: d, edgeLookup: f } = t.getState();
    let g = l;
    for (const p of a)
      g = typeof p == "function" ? p(g) : p;
    u ? c(g) : d && d(Vr({
      items: g,
      lookup: f
    }));
  }, []), i = Yr(r), s = ye(() => ({ nodeQueue: o, edgeQueue: i }), []);
  return h.jsx(Ss.Provider, { value: s, children: e });
}
function Sh() {
  const e = Rt(Ss);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const _h = (e) => !!e.panZoom;
function jo() {
  const e = ph(), t = ue(), n = Sh(), o = ce(_h), r = ye(() => {
    const i = (d) => t.getState().nodeLookup.get(d), s = (d) => {
      n.nodeQueue.push(d);
    }, a = (d) => {
      n.edgeQueue.push(d);
    }, l = (d) => {
      const { nodeLookup: f, nodeOrigin: g } = t.getState(), p = Fr(d) ? d : f.get(d.id), v = p.parentId ? qi(p.position, p.measured, p.parentId, f, g) : p.position, x = {
        ...p,
        position: v,
        width: p.measured?.width ?? p.width,
        height: p.measured?.height ?? p.height
      };
      return ht(x);
    }, c = (d, f, g = { replace: !1 }) => {
      s((p) => p.map((v) => {
        if (v.id === d) {
          const x = typeof f == "function" ? f(v) : f;
          return g.replace && Fr(x) ? x : { ...v, ...x };
        }
        return v;
      }));
    }, u = (d, f, g = { replace: !1 }) => {
      a((p) => p.map((v) => {
        if (v.id === d) {
          const x = typeof f == "function" ? f(v) : f;
          return g.replace && xh(x) ? x : { ...v, ...x };
        }
        return v;
      }));
    };
    return {
      getNodes: () => t.getState().nodes.map((d) => ({ ...d })),
      getNode: (d) => i(d)?.internals.userNode,
      getInternalNode: i,
      getEdges: () => {
        const { edges: d = [] } = t.getState();
        return d.map((f) => ({ ...f }));
      },
      getEdge: (d) => t.getState().edgeLookup.get(d),
      setNodes: s,
      setEdges: a,
      addNodes: (d) => {
        const f = Array.isArray(d) ? d : [d];
        n.nodeQueue.push((g) => [...g, ...f]);
      },
      addEdges: (d) => {
        const f = Array.isArray(d) ? d : [d];
        n.edgeQueue.push((g) => [...g, ...f]);
      },
      toObject: () => {
        const { nodes: d = [], edges: f = [], transform: g } = t.getState(), [p, v, x] = g;
        return {
          nodes: d.map((y) => ({ ...y })),
          edges: f.map((y) => ({ ...y })),
          viewport: {
            x: p,
            y: v,
            zoom: x
          }
        };
      },
      deleteElements: async ({ nodes: d = [], edges: f = [] }) => {
        const { nodes: g, edges: p, onNodesDelete: v, onEdgesDelete: x, triggerNodeChanges: y, triggerEdgeChanges: _, onDelete: m, onBeforeDelete: w } = t.getState(), { nodes: C, edges: S } = await Yd({
          nodesToRemove: d,
          edgesToRemove: f,
          nodes: g,
          edges: p,
          onBeforeDelete: w
        }), M = S.length > 0, A = C.length > 0;
        if (M) {
          const P = S.map(Br);
          x?.(S), _(P);
        }
        if (A) {
          const P = C.map(Br);
          v?.(C), y(P);
        }
        return (A || M) && m?.({ nodes: C, edges: S }), { deletedNodes: C, deletedEdges: S };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (d, f = !0, g) => {
        const p = hr(d), v = p ? d : l(d), x = g !== void 0;
        return v ? (g || t.getState().nodes).filter((y) => {
          const _ = t.getState().nodeLookup.get(y.id);
          if (_ && !p && (y.id === d.id || !_.internals.positionAbsolute))
            return !1;
          const m = ht(x ? y : _), w = Pt(m, v);
          return f && w > 0 || w >= m.width * m.height || w >= v.width * v.height;
        }) : [];
      },
      isNodeIntersecting: (d, f, g = !0) => {
        const v = hr(d) ? d : l(d);
        if (!v)
          return !1;
        const x = Pt(v, f);
        return g && x > 0 || x >= f.width * f.height || x >= v.width * v.height;
      },
      updateNode: c,
      updateNodeData: (d, f, g = { replace: !1 }) => {
        c(d, (p) => {
          const v = typeof f == "function" ? f(p) : f;
          return g.replace ? { ...p, data: v } : { ...p, data: { ...p.data, ...v } };
        }, g);
      },
      updateEdge: u,
      updateEdgeData: (d, f, g = { replace: !1 }) => {
        u(d, (p) => {
          const v = typeof f == "function" ? f(p) : f;
          return g.replace ? { ...p, data: v } : { ...p, data: { ...p.data, ...v } };
        }, g);
      },
      getNodesBounds: (d) => {
        const { nodeLookup: f, nodeOrigin: g } = t.getState();
        return Od(d, { nodeLookup: f, nodeOrigin: g });
      },
      getHandleConnections: ({ type: d, id: f, nodeId: g }) => Array.from(t.getState().connectionLookup.get(`${g}-${d}${f ? `-${f}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: d, handleId: f, nodeId: g }) => Array.from(t.getState().connectionLookup.get(`${g}${d ? f ? `-${d}-${f}` : `-${d}` : ""}`)?.values() ?? []),
      fitView: async (d) => {
        const f = t.getState().fitViewResolver ?? qd();
        return t.setState({ fitViewQueued: !0, fitViewOptions: d, fitViewResolver: f }), n.nodeQueue.push((g) => [...g]), f.promise;
      }
    };
  }, []);
  return ye(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const Xr = (e) => e.selected, Eh = typeof window < "u" ? window : void 0;
function Nh({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = ue(), { deleteElements: o } = jo(), r = jt(e, { actInsideInputWithModifier: !1 }), i = jt(t, { target: Eh });
  se(() => {
    if (r) {
      const { edges: s, nodes: a } = n.getState();
      o({ nodes: a.filter(Xr), edges: s.filter(Xr) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), se(() => {
    n.setState({ multiSelectionActive: i });
  }, [i]);
}
function Ch(e) {
  const t = ue();
  se(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = Io(e.current);
      (o.height === 0 || o.width === 0) && t.getState().onError?.("004", Pe.error004()), t.setState({ width: o.width || 500, height: o.height || 500 });
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
const kn = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, kh = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function Ih({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: i = Ke.Free, zoomOnDoubleClick: s = !0, panOnDrag: a = !0, defaultViewport: l, translateExtent: c, minZoom: u, maxZoom: d, zoomActivationKeyCode: f, preventScrolling: g = !0, children: p, noWheelClassName: v, noPanClassName: x, onViewportChange: y, isControlledViewport: _, paneClickDistance: m, selectionOnDrag: w }) {
  const C = ue(), S = ie(null), { userSelectionActive: M, lib: A, connectionInProgress: P } = ce(kh, le), B = jt(f), I = ie();
  Ch(S);
  const T = me((L) => {
    y?.({ x: L[0], y: L[1], zoom: L[2] }), _ || C.setState({ transform: L });
  }, [y, _]);
  return se(() => {
    if (S.current) {
      I.current = Df({
        domNode: S.current,
        minZoom: u,
        maxZoom: d,
        translateExtent: c,
        viewport: l,
        onDraggingChange: (E) => C.setState((k) => k.paneDragging === E ? k : { paneDragging: E }),
        onPanZoomStart: (E, k) => {
          const { onViewportChangeStart: j, onMoveStart: D } = C.getState();
          D?.(E, k), j?.(k);
        },
        onPanZoom: (E, k) => {
          const { onViewportChange: j, onMove: D } = C.getState();
          D?.(E, k), j?.(k);
        },
        onPanZoomEnd: (E, k) => {
          const { onViewportChangeEnd: j, onMoveEnd: D } = C.getState();
          D?.(E, k), j?.(k);
        }
      });
      const { x: L, y: b, zoom: N } = I.current.getViewport();
      return C.setState({
        panZoom: I.current,
        transform: [L, b, N],
        domNode: S.current.closest(".react-flow")
      }), () => {
        I.current?.destroy();
      };
    }
  }, []), se(() => {
    I.current?.update({
      onPaneContextMenu: e,
      zoomOnScroll: t,
      zoomOnPinch: n,
      panOnScroll: o,
      panOnScrollSpeed: r,
      panOnScrollMode: i,
      zoomOnDoubleClick: s,
      panOnDrag: a,
      zoomActivationKeyPressed: B,
      preventScrolling: g,
      noPanClassName: x,
      userSelectionActive: M,
      noWheelClassName: v,
      lib: A,
      onTransformChange: T,
      connectionInProgress: P,
      selectionOnDrag: w,
      paneClickDistance: m
    });
  }, [
    e,
    t,
    n,
    o,
    r,
    i,
    s,
    a,
    B,
    g,
    x,
    M,
    v,
    A,
    T,
    P,
    w,
    m
  ]), h.jsx("div", { className: "react-flow__renderer", ref: S, style: kn, children: p });
}
const Mh = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function Ah() {
  const { userSelectionActive: e, userSelectionRect: t } = ce(Mh, le);
  return e && t ? h.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const qn = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, Dh = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function Ph({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Dt.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: i, selectionOnDrag: s, onSelectionStart: a, onSelectionEnd: l, onPaneClick: c, onPaneContextMenu: u, onPaneScroll: d, onPaneMouseEnter: f, onPaneMouseMove: g, onPaneMouseLeave: p, children: v }) {
  const x = ie(0), y = ue(), { userSelectionActive: _, elementsSelectable: m, dragging: w, connectionInProgress: C, panBy: S, autoPanSpeed: M } = ce(Dh, le), A = m && (e || _), P = ie(null), B = ie(), I = ie(/* @__PURE__ */ new Set()), T = ie(/* @__PURE__ */ new Set()), L = ie(!1), b = ie({ x: 0, y: 0 }), N = ie(!1), E = (R) => {
    if (L.current || C) {
      L.current = !1;
      return;
    }
    c?.(R), y.getState().resetSelectedElements(), y.setState({ nodesSelectionActive: !1 });
  }, k = (R) => {
    if (Array.isArray(o) && o?.includes(2)) {
      R.preventDefault();
      return;
    }
    u?.(R);
  }, j = d ? (R) => d(R) : void 0, D = (R) => {
    L.current && (R.stopPropagation(), L.current = !1);
  }, V = (R) => {
    const { domNode: W, transform: ee } = y.getState();
    if (B.current = W?.getBoundingClientRect(), !B.current)
      return;
    const Q = R.target === P.current;
    if (!Q && !!R.target.closest(".nokey") || !e || !(s && Q || t) || R.button !== 0 || !R.isPrimary)
      return;
    R.target?.setPointerCapture?.(R.pointerId), L.current = !1;
    const { x: J, y: oe } = De(R.nativeEvent, B.current), de = yt({ x: J, y: oe }, ee);
    y.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: de.x,
        startY: de.y,
        x: J,
        y: oe
      }
    }), Q || (R.stopPropagation(), R.preventDefault());
  };
  function O(R, W) {
    const { userSelectionRect: ee } = y.getState();
    if (!ee)
      return;
    const { transform: Q, nodeLookup: $, edgeLookup: F, connectionLookup: J, triggerNodeChanges: oe, triggerEdgeChanges: de, defaultEdgeOptions: xe } = y.getState(), Ce = { x: ee.startX, y: ee.startY }, { x: Ee, y: Ne } = gt(Ce, Q), z = {
      startX: Ce.x,
      startY: Ce.y,
      x: R < Ee ? R : Ee,
      y: W < Ne ? W : Ne,
      width: Math.abs(R - Ee),
      height: Math.abs(W - Ne)
    }, Y = I.current, K = T.current;
    I.current = new Set(No($, z, Q, n === Dt.Partial, !0).map((re) => re.id)), T.current = /* @__PURE__ */ new Set();
    const ae = xe?.selectable ?? !0;
    for (const re of I.current) {
      const fe = J.get(re);
      if (fe)
        for (const { edgeId: pe } of fe.values()) {
          const ke = F.get(pe);
          ke && (ke.selectable ?? ae) && T.current.add(pe);
        }
    }
    if (!gr(Y, I.current)) {
      const re = st($, I.current, !0);
      oe(re);
    }
    if (!gr(K, T.current)) {
      const re = st(F, T.current);
      de(re);
    }
    y.setState({
      userSelectionRect: z,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function H() {
    if (!r || !B.current)
      return;
    const [R, W] = Co(b.current, B.current, M);
    S({ x: R, y: W }).then((ee) => {
      if (!L.current || !ee) {
        x.current = requestAnimationFrame(H);
        return;
      }
      const { x: Q, y: $ } = b.current;
      O(Q, $), x.current = requestAnimationFrame(H);
    });
  }
  const q = () => {
    cancelAnimationFrame(x.current), x.current = 0, N.current = !1;
  };
  se(() => () => q(), []);
  const X = (R) => {
    const { userSelectionRect: W, transform: ee, resetSelectedElements: Q } = y.getState();
    if (!B.current || !W)
      return;
    const { x: $, y: F } = De(R.nativeEvent, B.current);
    b.current = { x: $, y: F };
    const J = gt({ x: W.startX, y: W.startY }, ee);
    if (!L.current) {
      const oe = t ? 0 : i;
      if (Math.hypot($ - J.x, F - J.y) <= oe)
        return;
      Q(), a?.(R);
    }
    L.current = !0, N.current || (H(), N.current = !0), O($, F);
  }, Z = (R) => {
    R.button === 0 && (R.target?.releasePointerCapture?.(R.pointerId), !_ && R.target === P.current && y.getState().userSelectionRect && E?.(R), y.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), L.current && (l?.(R), y.setState({
      nodesSelectionActive: I.current.size > 0
    })), q());
  }, ne = (R) => {
    R.target?.releasePointerCapture?.(R.pointerId), q();
  }, U = o === !0 || Array.isArray(o) && o.includes(0);
  return h.jsxs("div", { className: ge(["react-flow__pane", { draggable: U, dragging: w, selection: e }]), onClick: A ? void 0 : qn(E, P), onContextMenu: qn(k, P), onWheel: qn(j, P), onPointerEnter: A ? void 0 : f, onPointerMove: A ? X : g, onPointerUp: A ? Z : void 0, onPointerCancel: A ? ne : void 0, onPointerDownCapture: A ? V : void 0, onClickCapture: A ? D : void 0, onPointerLeave: p, ref: P, style: kn, children: [v, h.jsx(Ah, {})] });
}
function uo({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: i, multiSelectionActive: s, nodeLookup: a, onError: l } = t.getState(), c = a.get(e);
  if (!c) {
    l?.("012", Pe.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), c.selected ? (n || c.selected && s) && (i({ nodes: [c], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function _s({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: i, nodeClickDistance: s }) {
  const a = ue(), [l, c] = te(!1), u = ie();
  return se(() => {
    u.current = yf({
      getStoreItems: () => a.getState(),
      onNodeMouseDown: (d) => {
        uo({
          id: d,
          store: a,
          nodeRef: e
        });
      },
      onDragStart: () => {
        c(!0);
      },
      onDragStop: () => {
        c(!1);
      }
    });
  }, []), se(() => {
    if (!(t || !e.current || !u.current))
      return u.current.update({
        noDragClassName: n,
        handleSelector: o,
        domNode: e.current,
        isSelectable: i,
        nodeId: r,
        nodeClickDistance: s
      }), () => {
        u.current?.destroy();
      };
  }, [n, o, t, i, e, r, s]), l;
}
const $h = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function Es() {
  const e = ue();
  return me((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: i, nodesDraggable: s, onError: a, updateNodePositions: l, nodeLookup: c, nodeOrigin: u } = e.getState(), d = /* @__PURE__ */ new Map(), f = $h(s), g = r ? i[0] : 5, p = r ? i[1] : 5, v = n.direction.x * g * n.factor, x = n.direction.y * p * n.factor;
    for (const [, y] of c) {
      if (!f(y))
        continue;
      let _ = {
        x: y.internals.positionAbsolute.x + v,
        y: y.internals.positionAbsolute.y + x
      };
      r && (_ = Bt(_, i));
      const { position: m, positionAbsolute: w } = Bi({
        nodeId: y.id,
        nextPosition: _,
        nodeLookup: c,
        nodeExtent: o,
        nodeOrigin: u,
        onError: a
      });
      y.position = m, y.internals.positionAbsolute = w, d.set(y.id, y);
    }
    l(d);
  }, []);
}
const To = mo(null), jh = To.Provider;
To.Consumer;
const Ns = () => Rt(To), Th = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), zh = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: i, connection: s } = o, { fromHandle: a, toHandle: l, isValid: c } = s, u = l?.nodeId === e && l?.id === t && l?.type === n;
  return {
    connectingFrom: a?.nodeId === e && a?.id === t && a?.type === n,
    connectingTo: u,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: i === dt.Strict ? a?.type !== n : e !== a?.nodeId || t !== a?.id,
    connectionInProcess: !!a,
    clickConnectionInProcess: !!r,
    valid: u && c
  };
};
function Rh({ type: e = "source", position: t = G.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: i = !0, id: s, onConnect: a, children: l, className: c, onMouseDown: u, onTouchStart: d, ...f }, g) {
  const p = s || null, v = e === "target", x = ue(), y = Ns(), { connectOnClick: _, noPanClassName: m, rfId: w } = ce(Th, le), { connectingFrom: C, connectingTo: S, clickConnecting: M, isPossibleEndHandle: A, connectionInProcess: P, clickConnectionInProcess: B, valid: I } = ce(zh(y, p, e), le);
  y || x.getState().onError?.("010", Pe.error010());
  const T = (N) => {
    const { defaultEdgeOptions: E, onConnect: k, hasDefaultEdges: j } = x.getState(), D = {
      ...E,
      ...N
    };
    if (j) {
      const { edges: V, setEdges: O, onError: H } = x.getState();
      O(vs(D, V, { onError: H }));
    }
    k?.(D), a?.(D);
  }, L = (N) => {
    if (!y)
      return;
    const E = Gi(N.nativeEvent);
    if (r && (E && N.button === 0 || !E)) {
      const k = x.getState();
      lo.onPointerDown(N.nativeEvent, {
        handleDomNode: N.currentTarget,
        autoPanOnConnect: k.autoPanOnConnect,
        connectionMode: k.connectionMode,
        connectionRadius: k.connectionRadius,
        domNode: k.domNode,
        nodeLookup: k.nodeLookup,
        lib: k.lib,
        isTarget: v,
        handleId: p,
        nodeId: y,
        flowId: k.rfId,
        panBy: k.panBy,
        cancelConnection: k.cancelConnection,
        onConnectStart: k.onConnectStart,
        onConnectEnd: (...j) => x.getState().onConnectEnd?.(...j),
        updateConnection: k.updateConnection,
        onConnect: T,
        isValidConnection: n || ((...j) => x.getState().isValidConnection?.(...j) ?? !0),
        getTransform: () => x.getState().transform,
        getFromHandle: () => x.getState().connection.fromHandle,
        autoPanSpeed: k.autoPanSpeed,
        dragThreshold: k.connectionDragThreshold
      });
    }
    E ? u?.(N) : d?.(N);
  }, b = (N) => {
    const { onClickConnectStart: E, onClickConnectEnd: k, connectionClickStartHandle: j, connectionMode: D, isValidConnection: V, lib: O, rfId: H, nodeLookup: q, connection: X } = x.getState();
    if (!y || !j && !r)
      return;
    if (!j) {
      E?.(N.nativeEvent, { nodeId: y, handleId: p, handleType: e }), x.setState({ connectionClickStartHandle: { nodeId: y, type: e, id: p } });
      return;
    }
    const Z = Zi(N.target), ne = n || V, { connection: U, isValid: R } = lo.isValid(N.nativeEvent, {
      handle: {
        nodeId: y,
        id: p,
        type: e
      },
      connectionMode: D,
      fromNodeId: j.nodeId,
      fromHandleId: j.id || null,
      fromType: j.type,
      isValidConnection: ne,
      flowId: H,
      doc: Z,
      lib: O,
      nodeLookup: q
    });
    R && U && T(U);
    const W = structuredClone(X);
    delete W.inProgress, W.toPosition = W.toHandle ? W.toHandle.position : null, k?.(N, W), x.setState({ connectionClickStartHandle: null });
  };
  return h.jsx("div", { "data-handleid": p, "data-nodeid": y, "data-handlepos": t, "data-id": `${w}-${y}-${p}-${e}`, className: ge([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    m,
    c,
    {
      source: !v,
      target: v,
      connectable: o,
      connectablestart: r,
      connectableend: i,
      clickconnecting: M,
      connectingfrom: C,
      connectingto: S,
      valid: I,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!P || A) && (P || B ? i : r)
    }
  ]), onMouseDown: L, onTouchStart: L, onClick: _ ? b : void 0, ref: g, ...f, children: l });
}
const mt = he(bs(Rh));
function Lh({ data: e, isConnectable: t, sourcePosition: n = G.Bottom }) {
  return h.jsxs(h.Fragment, { children: [e?.label, h.jsx(mt, { type: "source", position: n, isConnectable: t })] });
}
function Hh({ data: e, isConnectable: t, targetPosition: n = G.Top, sourcePosition: o = G.Bottom }) {
  return h.jsxs(h.Fragment, { children: [h.jsx(mt, { type: "target", position: n, isConnectable: t }), e?.label, h.jsx(mt, { type: "source", position: o, isConnectable: t })] });
}
function Oh() {
  return null;
}
function Vh({ data: e, isConnectable: t, targetPosition: n = G.Top }) {
  return h.jsxs(h.Fragment, { children: [h.jsx(mt, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const gn = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, Wr = {
  input: Lh,
  default: Hh,
  output: Vh,
  group: Oh
};
function Bh(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const Fh = (e) => {
  const { width: t, height: n, x: o, y: r } = Vt(e.nodeLookup, {
    filter: (i) => !!i.selected
  });
  return {
    width: Ae(t) ? t : null,
    height: Ae(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function Yh({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = ue(), { width: r, height: i, transformString: s, userSelectionActive: a } = ce(Fh, le), l = Es(), c = ie(null);
  se(() => {
    n || c.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const u = !a && r !== null && i !== null;
  if (_s({
    nodeRef: c,
    disabled: !u
  }), !u)
    return null;
  const d = e ? (g) => {
    const p = o.getState().nodes.filter((v) => v.selected);
    e(g, p);
  } : void 0, f = (g) => {
    Object.prototype.hasOwnProperty.call(gn, g.key) && (g.preventDefault(), l({
      direction: gn[g.key],
      factor: g.shiftKey ? 4 : 1
    }));
  };
  return h.jsx("div", { className: ge(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: s
  }, children: h.jsx("div", { ref: c, className: "react-flow__nodesselection-rect", onContextMenu: d, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : f, style: {
    width: r,
    height: i
  } }) });
}
const qr = typeof window < "u" ? window : void 0, Xh = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function Cs({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, paneClickDistance: a, deleteKeyCode: l, selectionKeyCode: c, selectionOnDrag: u, selectionMode: d, onSelectionStart: f, onSelectionEnd: g, multiSelectionKeyCode: p, panActivationKeyCode: v, zoomActivationKeyCode: x, elementsSelectable: y, zoomOnScroll: _, zoomOnPinch: m, panOnScroll: w, panOnScrollSpeed: C, panOnScrollMode: S, zoomOnDoubleClick: M, panOnDrag: A, autoPanOnSelection: P, defaultViewport: B, translateExtent: I, minZoom: T, maxZoom: L, preventScrolling: b, onSelectionContextMenu: N, noWheelClassName: E, noPanClassName: k, disableKeyboardA11y: j, onViewportChange: D, isControlledViewport: V }) {
  const { nodesSelectionActive: O, userSelectionActive: H } = ce(Xh, le), q = jt(c, { target: qr }), X = jt(v, { target: qr }), Z = X || A, ne = X || w, U = u && Z !== !0, R = q || H || U;
  return Nh({ deleteKeyCode: l, multiSelectionKeyCode: p }), h.jsx(Ih, { onPaneContextMenu: i, elementsSelectable: y, zoomOnScroll: _, zoomOnPinch: m, panOnScroll: ne, panOnScrollSpeed: C, panOnScrollMode: S, zoomOnDoubleClick: M, panOnDrag: !q && Z, defaultViewport: B, translateExtent: I, minZoom: T, maxZoom: L, zoomActivationKeyCode: x, preventScrolling: b, noWheelClassName: E, noPanClassName: k, onViewportChange: D, isControlledViewport: V, paneClickDistance: a, selectionOnDrag: U, children: h.jsxs(Ph, { onSelectionStart: f, onSelectionEnd: g, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, panOnDrag: Z, autoPanOnSelection: P, isSelecting: !!R, selectionMode: d, selectionKeyPressed: q, paneClickDistance: a, selectionOnDrag: U, children: [e, O && h.jsx(Yh, { onSelectionContextMenu: N, noPanClassName: k, disableKeyboardA11y: j })] }) });
}
Cs.displayName = "FlowRenderer";
const Wh = he(Cs), qh = (e) => (t) => e ? No(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function Zh(e) {
  return ce(me(qh(e), [e]), le);
}
const Uh = (e) => e.updateNodeInternals;
function Gh() {
  const e = ce(Uh), [t] = te(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
    const o = /* @__PURE__ */ new Map();
    n.forEach((r) => {
      const i = r.target.getAttribute("data-id");
      o.set(i, {
        id: i,
        nodeElement: r.target,
        force: !0
      });
    }), e(o);
  }));
  return se(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function Kh({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = ue(), i = ie(null), s = ie(null), a = ie(e.sourcePosition), l = ie(e.targetPosition), c = ie(t), u = n && !!e.internals.handleBounds;
  return se(() => {
    i.current && !e.hidden && (!u || s.current !== i.current) && (s.current && o?.unobserve(s.current), o?.observe(i.current), s.current = i.current);
  }, [u, e.hidden]), se(() => () => {
    s.current && (o?.unobserve(s.current), s.current = null);
  }, []), se(() => {
    if (i.current) {
      const d = c.current !== t, f = a.current !== e.sourcePosition, g = l.current !== e.targetPosition;
      (d || f || g) && (c.current = t, a.current = e.sourcePosition, l.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: i.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), i;
}
function Qh({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: i, onDoubleClick: s, nodesDraggable: a, elementsSelectable: l, nodesConnectable: c, nodesFocusable: u, resizeObserver: d, noDragClassName: f, noPanClassName: g, disableKeyboardA11y: p, rfId: v, nodeTypes: x, nodeClickDistance: y, onError: _ }) {
  const { node: m, internals: w, isParent: C } = ce((R) => {
    const W = R.nodeLookup.get(e), ee = R.parentLookup.has(e);
    return {
      node: W,
      internals: W.internals,
      isParent: ee
    };
  }, le);
  let S = m.type || "default", M = x?.[S] || Wr[S];
  M === void 0 && (_?.("003", Pe.error003(S)), S = "default", M = x?.default || Wr.default);
  const A = !!(m.draggable || a && typeof m.draggable > "u"), P = !!(m.selectable || l && typeof m.selectable > "u"), B = !!(m.connectable || c && typeof m.connectable > "u"), I = !!(m.focusable || u && typeof m.focusable > "u"), T = ue(), L = Wi(m), b = Kh({ node: m, nodeType: S, hasDimensions: L, resizeObserver: d }), N = _s({
    nodeRef: b,
    disabled: m.hidden || !A,
    noDragClassName: f,
    handleSelector: m.dragHandle,
    nodeId: e,
    isSelectable: P,
    nodeClickDistance: y
  }), E = Es();
  if (m.hidden)
    return null;
  const k = Be(m), j = Bh(m), D = P || A || t || n || o || r, V = n ? (R) => n(R, { ...w.userNode }) : void 0, O = o ? (R) => o(R, { ...w.userNode }) : void 0, H = r ? (R) => r(R, { ...w.userNode }) : void 0, q = i ? (R) => i(R, { ...w.userNode }) : void 0, X = s ? (R) => s(R, { ...w.userNode }) : void 0, Z = (R) => {
    const { selectNodesOnDrag: W, nodeDragThreshold: ee } = T.getState();
    P && (!W || !A || ee > 0) && uo({
      id: e,
      store: T,
      nodeRef: b
    }), t && t(R, { ...w.userNode });
  }, ne = (R) => {
    if (!(Ui(R.nativeEvent) || p)) {
      if (Ri.includes(R.key) && P) {
        const W = R.key === "Escape";
        uo({
          id: e,
          store: T,
          unselect: W,
          nodeRef: b
        });
      } else if (A && m.selected && Object.prototype.hasOwnProperty.call(gn, R.key)) {
        R.preventDefault();
        const { ariaLabelConfig: W } = T.getState();
        T.setState({
          ariaLiveMessage: W["node.a11yDescription.ariaLiveMessage"]({
            direction: R.key.replace("Arrow", "").toLowerCase(),
            x: ~~w.positionAbsolute.x,
            y: ~~w.positionAbsolute.y
          })
        }), E({
          direction: gn[R.key],
          factor: R.shiftKey ? 4 : 1
        });
      }
    }
  }, U = () => {
    if (p || !b.current?.matches(":focus-visible"))
      return;
    const { transform: R, width: W, height: ee, autoPanOnNodeFocus: Q, setCenter: $ } = T.getState();
    if (!Q)
      return;
    No(/* @__PURE__ */ new Map([[e, m]]), { x: 0, y: 0, width: W, height: ee }, R, !0).length > 0 || $(m.position.x + k.width / 2, m.position.y + k.height / 2, {
      zoom: R[2]
    });
  };
  return h.jsx("div", { className: ge([
    "react-flow__node",
    `react-flow__node-${S}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [g]: A
    },
    m.className,
    {
      selected: m.selected,
      selectable: P,
      parent: C,
      draggable: A,
      dragging: N
    }
  ]), ref: b, style: {
    zIndex: w.z,
    transform: `translate(${w.positionAbsolute.x}px,${w.positionAbsolute.y}px)`,
    pointerEvents: D ? "all" : "none",
    visibility: L ? "visible" : "hidden",
    ...m.style,
    ...j
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: V, onMouseMove: O, onMouseLeave: H, onContextMenu: q, onClick: Z, onDoubleClick: X, onKeyDown: I ? ne : void 0, tabIndex: I ? 0 : void 0, onFocus: I ? U : void 0, role: m.ariaRole ?? (I ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": p ? void 0 : `${gs}-${v}`, "aria-label": m.ariaLabel, ...m.domAttributes, children: h.jsx(jh, { value: e, children: h.jsx(M, { id: e, data: m.data, type: S, positionAbsoluteX: w.positionAbsolute.x, positionAbsoluteY: w.positionAbsolute.y, selected: m.selected ?? !1, selectable: P, draggable: A, deletable: m.deletable ?? !0, isConnectable: B, sourcePosition: m.sourcePosition, targetPosition: m.targetPosition, dragging: N, dragHandle: m.dragHandle, zIndex: w.z, parentId: m.parentId, ...k }) }) });
}
var Jh = he(Qh);
const eg = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function ks(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: i } = ce(eg, le), s = Zh(e.onlyRenderVisibleElements), a = Gh();
  return h.jsx("div", { className: "react-flow__nodes", style: kn, children: s.map((l) => (
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
    h.jsx(Jh, { id: l, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: a, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: i }, l)
  )) });
}
ks.displayName = "NodeRenderer";
const tg = he(ks);
function ng(e) {
  return ce(me((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const i = n.nodeLookup.get(r.source), s = n.nodeLookup.get(r.target);
        i && s && Kd({
          sourceNode: i,
          targetNode: s,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && o.push(r.id);
      }
    return o;
  }, [e]), le);
}
const og = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return h.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, rg = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return h.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Zr = {
  [fn.Arrow]: og,
  [fn.ArrowClosed]: rg
};
function ig(e) {
  const t = ue();
  return ye(() => Object.prototype.hasOwnProperty.call(Zr, e) ? Zr[e] : (t.getState().onError?.("009", Pe.error009(e)), null), [e]);
}
const sg = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: i = "strokeWidth", strokeWidth: s, orient: a = "auto-start-reverse" }) => {
  const l = ig(t);
  return l ? h.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: i, orient: a, refX: "0", refY: "0", children: h.jsx(l, { color: n, strokeWidth: s }) }) : null;
}, Is = ({ defaultColor: e, rfId: t }) => {
  const n = ce((i) => i.edges), o = ce((i) => i.defaultEdgeOptions), r = ye(() => sf(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? h.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: h.jsx("defs", { children: r.map((i) => h.jsx(sg, { id: i.id, type: i.type, color: i.color, width: i.width, height: i.height, markerUnits: i.markerUnits, strokeWidth: i.strokeWidth, orient: i.orient }, i.id)) }) }) : null;
};
Is.displayName = "MarkerDefinitions";
var ag = he(Is);
function Ms({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: i, labelBgPadding: s = [2, 4], labelBgBorderRadius: a = 2, children: l, className: c, ...u }) {
  const [d, f] = te({ x: 1, y: 0, width: 0, height: 0 }), g = ge(["react-flow__edge-textwrapper", c]), p = ie(null);
  return se(() => {
    if (p.current) {
      const v = p.current.getBBox();
      f({
        x: v.x,
        y: v.y,
        width: v.width,
        height: v.height
      });
    }
  }, [n]), n ? h.jsxs("g", { transform: `translate(${e - d.width / 2} ${t - d.height / 2})`, className: g, visibility: d.width ? "visible" : "hidden", ...u, children: [r && h.jsx("rect", { width: d.width + 2 * s[0], x: -s[0], y: -s[1], height: d.height + 2 * s[1], className: "react-flow__edge-textbg", style: i, rx: a, ry: a }), h.jsx("text", { className: "react-flow__edge-text", y: d.height / 2, dy: "0.3em", ref: p, style: o, children: n }), l] }) : null;
}
Ms.displayName = "EdgeText";
const cg = he(Ms);
function In({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: a, labelBgBorderRadius: l, interactionWidth: c = 20, ...u }) {
  return h.jsxs(h.Fragment, { children: [h.jsx("path", { ...u, d: e, fill: "none", className: ge(["react-flow__edge-path", u.className]) }), c ? h.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: c, className: "react-flow__edge-interaction" }) : null, o && Ae(t) && Ae(n) ? h.jsx(cg, { x: t, y: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: a, labelBgBorderRadius: l }) : null] });
}
function Ur({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === G.Left || e === G.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function As({ sourceX: e, sourceY: t, sourcePosition: n = G.Bottom, targetX: o, targetY: r, targetPosition: i = G.Top }) {
  const [s, a] = Ur({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [l, c] = Ur({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [u, d, f, g] = Ki({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: r,
    sourceControlX: s,
    sourceControlY: a,
    targetControlX: l,
    targetControlY: c
  });
  return [
    `M${e},${t} C${s},${a} ${l},${c} ${o},${r}`,
    u,
    d,
    f,
    g
  ];
}
function Ds(e) {
  return he(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: a, label: l, labelStyle: c, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: g, style: p, markerEnd: v, markerStart: x, interactionWidth: y }) => {
    const [_, m, w] = As({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: a
    }), C = e.isInternal ? void 0 : t;
    return h.jsx(In, { id: C, path: _, labelX: m, labelY: w, label: l, labelStyle: c, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: g, style: p, markerEnd: v, markerStart: x, interactionWidth: y });
  });
}
const lg = Ds({ isInternal: !1 }), Ps = Ds({ isInternal: !0 });
lg.displayName = "SimpleBezierEdge";
Ps.displayName = "SimpleBezierEdgeInternal";
function $s(e) {
  return he(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: u, labelBgBorderRadius: d, style: f, sourcePosition: g = G.Bottom, targetPosition: p = G.Top, markerEnd: v, markerStart: x, pathOptions: y, interactionWidth: _ }) => {
    const [m, w, C] = so({
      sourceX: n,
      sourceY: o,
      sourcePosition: g,
      targetX: r,
      targetY: i,
      targetPosition: p,
      borderRadius: y?.borderRadius,
      offset: y?.offset,
      stepPosition: y?.stepPosition
    }), S = e.isInternal ? void 0 : t;
    return h.jsx(In, { id: S, path: m, labelX: w, labelY: C, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: u, labelBgBorderRadius: d, style: f, markerEnd: v, markerStart: x, interactionWidth: _ });
  });
}
const js = $s({ isInternal: !1 }), Ts = $s({ isInternal: !0 });
js.displayName = "SmoothStepEdge";
Ts.displayName = "SmoothStepEdgeInternal";
function zs(e) {
  return he(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return h.jsx(js, { ...n, id: o, pathOptions: ye(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const ug = zs({ isInternal: !1 }), Rs = zs({ isInternal: !0 });
ug.displayName = "StepEdge";
Rs.displayName = "StepEdgeInternal";
function Ls(e) {
  return he(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: u, labelBgBorderRadius: d, style: f, markerEnd: g, markerStart: p, interactionWidth: v }) => {
    const [x, y, _] = es({ sourceX: n, sourceY: o, targetX: r, targetY: i }), m = e.isInternal ? void 0 : t;
    return h.jsx(In, { id: m, path: x, labelX: y, labelY: _, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: u, labelBgBorderRadius: d, style: f, markerEnd: g, markerStart: p, interactionWidth: v });
  });
}
const dg = Ls({ isInternal: !1 }), Hs = Ls({ isInternal: !0 });
dg.displayName = "StraightEdge";
Hs.displayName = "StraightEdgeInternal";
function Os(e) {
  return he(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s = G.Bottom, targetPosition: a = G.Top, label: l, labelStyle: c, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: g, style: p, markerEnd: v, markerStart: x, pathOptions: y, interactionWidth: _ }) => {
    const [m, w, C] = Qi({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: a,
      curvature: y?.curvature
    }), S = e.isInternal ? void 0 : t;
    return h.jsx(In, { id: S, path: m, labelX: w, labelY: C, label: l, labelStyle: c, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: g, style: p, markerEnd: v, markerStart: x, interactionWidth: _ });
  });
}
const fg = Os({ isInternal: !1 }), Vs = Os({ isInternal: !0 });
fg.displayName = "BezierEdge";
Vs.displayName = "BezierEdgeInternal";
const Gr = {
  default: Vs,
  straight: Hs,
  step: Rs,
  smoothstep: Ts,
  simplebezier: Ps
}, Kr = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, hg = (e, t, n) => n === G.Left ? e - t : n === G.Right ? e + t : e, gg = (e, t, n) => n === G.Top ? e - t : n === G.Bottom ? e + t : e, Qr = "react-flow__edgeupdater";
function Jr({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: i, onMouseOut: s, type: a }) {
  return h.jsx("circle", { onMouseDown: r, onMouseEnter: i, onMouseOut: s, className: ge([Qr, `${Qr}-${a}`]), cx: hg(t, o, e), cy: gg(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function pg({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: i, targetY: s, sourcePosition: a, targetPosition: l, onReconnect: c, onReconnectStart: u, onReconnectEnd: d, setReconnecting: f, setUpdateHover: g }) {
  const p = ue(), v = (w, C) => {
    if (w.button !== 0)
      return;
    const { autoPanOnConnect: S, domNode: M, connectionMode: A, connectionRadius: P, lib: B, onConnectStart: I, cancelConnection: T, nodeLookup: L, rfId: b, panBy: N, updateConnection: E } = p.getState(), k = C.type === "target", j = (O, H) => {
      f(!1), d?.(O, n, C.type, H);
    }, D = (O) => c?.(n, O), V = (O, H) => {
      f(!0), u?.(w, n, C.type), I?.(O, H);
    };
    lo.onPointerDown(w.nativeEvent, {
      autoPanOnConnect: S,
      connectionMode: A,
      connectionRadius: P,
      domNode: M,
      handleId: C.id,
      nodeId: C.nodeId,
      nodeLookup: L,
      isTarget: k,
      edgeUpdaterType: C.type,
      lib: B,
      flowId: b,
      cancelConnection: T,
      panBy: N,
      isValidConnection: (...O) => p.getState().isValidConnection?.(...O) ?? !0,
      onConnect: D,
      onConnectStart: V,
      onConnectEnd: (...O) => p.getState().onConnectEnd?.(...O),
      onReconnectEnd: j,
      updateConnection: E,
      getTransform: () => p.getState().transform,
      getFromHandle: () => p.getState().connection.fromHandle,
      dragThreshold: p.getState().connectionDragThreshold,
      handleDomNode: w.currentTarget
    });
  }, x = (w) => v(w, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), y = (w) => v(w, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), _ = () => g(!0), m = () => g(!1);
  return h.jsxs(h.Fragment, { children: [(e === !0 || e === "source") && h.jsx(Jr, { position: a, centerX: o, centerY: r, radius: t, onMouseDown: x, onMouseEnter: _, onMouseOut: m, type: "source" }), (e === !0 || e === "target") && h.jsx(Jr, { position: l, centerX: i, centerY: s, radius: t, onMouseDown: y, onMouseEnter: _, onMouseOut: m, type: "target" })] });
}
function mg({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: i, onContextMenu: s, onMouseEnter: a, onMouseMove: l, onMouseLeave: c, reconnectRadius: u, onReconnect: d, onReconnectStart: f, onReconnectEnd: g, rfId: p, edgeTypes: v, noPanClassName: x, onError: y, disableKeyboardA11y: _ }) {
  let m = ce(($) => $.edgeLookup.get(e));
  const w = ce(($) => $.defaultEdgeOptions);
  m = w ? { ...w, ...m } : m;
  let C = m.type || "default", S = v?.[C] || Gr[C];
  S === void 0 && (y?.("011", Pe.error011(C)), C = "default", S = v?.default || Gr.default);
  const M = !!(m.focusable || t && typeof m.focusable > "u"), A = typeof d < "u" && (m.reconnectable || n && typeof m.reconnectable > "u"), P = !!(m.selectable || o && typeof m.selectable > "u"), B = ie(null), [I, T] = te(!1), [L, b] = te(!1), N = ue(), { zIndex: E, sourceX: k, sourceY: j, targetX: D, targetY: V, sourcePosition: O, targetPosition: H } = ce(me(($) => {
    const F = $.nodeLookup.get(m.source), J = $.nodeLookup.get(m.target);
    if (!F || !J)
      return {
        zIndex: m.zIndex,
        ...Kr
      };
    const oe = rf({
      id: e,
      sourceNode: F,
      targetNode: J,
      sourceHandle: m.sourceHandle || null,
      targetHandle: m.targetHandle || null,
      connectionMode: $.connectionMode,
      onError: y
    });
    return {
      zIndex: Gd({
        selected: m.selected,
        zIndex: m.zIndex,
        sourceNode: F,
        targetNode: J,
        elevateOnSelect: $.elevateEdgesOnSelect,
        zIndexMode: $.zIndexMode
      }),
      ...oe || Kr
    };
  }, [m.source, m.target, m.sourceHandle, m.targetHandle, m.selected, m.zIndex]), le), q = ye(() => m.markerStart ? `url('#${ao(m.markerStart, p)}')` : void 0, [m.markerStart, p]), X = ye(() => m.markerEnd ? `url('#${ao(m.markerEnd, p)}')` : void 0, [m.markerEnd, p]);
  if (m.hidden || k === null || j === null || D === null || V === null)
    return null;
  const Z = ($) => {
    const { addSelectedEdges: F, unselectNodesAndEdges: J, multiSelectionActive: oe } = N.getState();
    P && (N.setState({ nodesSelectionActive: !1 }), m.selected && oe ? (J({ nodes: [], edges: [m] }), B.current?.blur()) : F([e])), r && r($, m);
  }, ne = i ? ($) => {
    i($, { ...m });
  } : void 0, U = s ? ($) => {
    s($, { ...m });
  } : void 0, R = a ? ($) => {
    a($, { ...m });
  } : void 0, W = l ? ($) => {
    l($, { ...m });
  } : void 0, ee = c ? ($) => {
    c($, { ...m });
  } : void 0, Q = ($) => {
    if (!_ && Ri.includes($.key) && P) {
      const { unselectNodesAndEdges: F, addSelectedEdges: J } = N.getState();
      $.key === "Escape" ? (B.current?.blur(), F({ edges: [m] })) : J([e]);
    }
  };
  return h.jsx("svg", { style: { zIndex: E }, children: h.jsxs("g", { className: ge([
    "react-flow__edge",
    `react-flow__edge-${C}`,
    m.className,
    x,
    {
      selected: m.selected,
      animated: m.animated,
      inactive: !P && !r,
      updating: I,
      selectable: P
    }
  ]), onClick: Z, onDoubleClick: ne, onContextMenu: U, onMouseEnter: R, onMouseMove: W, onMouseLeave: ee, onKeyDown: M ? Q : void 0, tabIndex: M ? 0 : void 0, role: m.ariaRole ?? (M ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": m.ariaLabel === null ? void 0 : m.ariaLabel || `Edge from ${m.source} to ${m.target}`, "aria-describedby": M ? `${ps}-${p}` : void 0, ref: B, ...m.domAttributes, children: [!L && h.jsx(S, { id: e, source: m.source, target: m.target, type: m.type, selected: m.selected, animated: m.animated, selectable: P, deletable: m.deletable ?? !0, label: m.label, labelStyle: m.labelStyle, labelShowBg: m.labelShowBg, labelBgStyle: m.labelBgStyle, labelBgPadding: m.labelBgPadding, labelBgBorderRadius: m.labelBgBorderRadius, sourceX: k, sourceY: j, targetX: D, targetY: V, sourcePosition: O, targetPosition: H, data: m.data, style: m.style, sourceHandleId: m.sourceHandle, targetHandleId: m.targetHandle, markerStart: q, markerEnd: X, pathOptions: "pathOptions" in m ? m.pathOptions : void 0, interactionWidth: m.interactionWidth }), A && h.jsx(pg, { edge: m, isReconnectable: A, reconnectRadius: u, onReconnect: d, onReconnectStart: f, onReconnectEnd: g, sourceX: k, sourceY: j, targetX: D, targetY: V, sourcePosition: O, targetPosition: H, setUpdateHover: T, setReconnecting: b })] }) });
}
var yg = he(mg);
const xg = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function Bs({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: i, onEdgeContextMenu: s, onEdgeMouseEnter: a, onEdgeMouseMove: l, onEdgeMouseLeave: c, onEdgeClick: u, reconnectRadius: d, onEdgeDoubleClick: f, onReconnectStart: g, onReconnectEnd: p, disableKeyboardA11y: v }) {
  const { edgesFocusable: x, edgesReconnectable: y, elementsSelectable: _, onError: m } = ce(xg, le), w = ng(t);
  return h.jsxs("div", { className: "react-flow__edges", children: [h.jsx(ag, { defaultColor: e, rfId: n }), w.map((C) => h.jsx(yg, { id: C, edgesFocusable: x, edgesReconnectable: y, elementsSelectable: _, noPanClassName: r, onReconnect: i, onContextMenu: s, onMouseEnter: a, onMouseMove: l, onMouseLeave: c, onClick: u, reconnectRadius: d, onDoubleClick: f, onReconnectStart: g, onReconnectEnd: p, rfId: n, onError: m, edgeTypes: o, disableKeyboardA11y: v }, C))] });
}
Bs.displayName = "EdgeRenderer";
const wg = he(Bs), vg = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function bg({ children: e }) {
  const t = ce(vg);
  return h.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function Sg(e) {
  const t = jo(), n = ie(!1);
  se(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const _g = (e) => e.panZoom?.syncViewport;
function Eg(e) {
  const t = ce(_g), n = ue();
  return se(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function Ng(e) {
  return e.connection.inProgress ? { ...e.connection, to: yt(e.connection.to, e.transform) } : { ...e.connection };
}
function Cg(e) {
  return Ng;
}
function kg(e) {
  const t = Cg();
  return ce(t, le);
}
const Ig = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function Mg({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: i, height: s, isValid: a, inProgress: l } = ce(Ig, le);
  return !(i && r && l) ? null : h.jsx("svg", { style: e, width: i, height: s, className: "react-flow__connectionline react-flow__container", children: h.jsx("g", { className: ge(["react-flow__connection", Oi(a)]), children: h.jsx(Fs, { style: t, type: n, CustomComponent: o, isValid: a }) }) });
}
const Fs = ({ style: e, type: t = Xe.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: i, fromNode: s, fromHandle: a, fromPosition: l, to: c, toNode: u, toHandle: d, toPosition: f, pointer: g } = kg();
  if (!r)
    return;
  if (n)
    return h.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: s, fromHandle: a, fromX: i.x, fromY: i.y, toX: c.x, toY: c.y, fromPosition: l, toPosition: f, connectionStatus: Oi(o), toNode: u, toHandle: d, pointer: g });
  let p = "";
  const v = {
    sourceX: i.x,
    sourceY: i.y,
    sourcePosition: l,
    targetX: c.x,
    targetY: c.y,
    targetPosition: f
  };
  switch (t) {
    case Xe.Bezier:
      [p] = Qi(v);
      break;
    case Xe.SimpleBezier:
      [p] = As(v);
      break;
    case Xe.Step:
      [p] = so({
        ...v,
        borderRadius: 0
      });
      break;
    case Xe.SmoothStep:
      [p] = so(v);
      break;
    default:
      [p] = es(v);
  }
  return h.jsx("path", { d: p, fill: "none", className: "react-flow__connection-path", style: e });
};
Fs.displayName = "ConnectionLine";
const Ag = {};
function ei(e = Ag) {
  ie(e), ue(), se(() => {
  }, [e]);
}
function Dg() {
  ue(), ie(!1), se(() => {
  }, []);
}
function Ys({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: i, onEdgeDoubleClick: s, onNodeMouseEnter: a, onNodeMouseMove: l, onNodeMouseLeave: c, onNodeContextMenu: u, onSelectionContextMenu: d, onSelectionStart: f, onSelectionEnd: g, connectionLineType: p, connectionLineStyle: v, connectionLineComponent: x, connectionLineContainerStyle: y, selectionKeyCode: _, selectionOnDrag: m, selectionMode: w, multiSelectionKeyCode: C, panActivationKeyCode: S, zoomActivationKeyCode: M, deleteKeyCode: A, onlyRenderVisibleElements: P, elementsSelectable: B, defaultViewport: I, translateExtent: T, minZoom: L, maxZoom: b, preventScrolling: N, defaultMarkerColor: E, zoomOnScroll: k, zoomOnPinch: j, panOnScroll: D, panOnScrollSpeed: V, panOnScrollMode: O, zoomOnDoubleClick: H, panOnDrag: q, autoPanOnSelection: X, onPaneClick: Z, onPaneMouseEnter: ne, onPaneMouseMove: U, onPaneMouseLeave: R, onPaneScroll: W, onPaneContextMenu: ee, paneClickDistance: Q, nodeClickDistance: $, onEdgeContextMenu: F, onEdgeMouseEnter: J, onEdgeMouseMove: oe, onEdgeMouseLeave: de, reconnectRadius: xe, onReconnect: Ce, onReconnectStart: Ee, onReconnectEnd: Ne, noDragClassName: z, noWheelClassName: Y, noPanClassName: K, disableKeyboardA11y: ae, nodeExtent: re, rfId: fe, viewport: pe, onViewportChange: ke }) {
  return ei(e), ei(t), Dg(), Sg(n), Eg(pe), h.jsx(Wh, { onPaneClick: Z, onPaneMouseEnter: ne, onPaneMouseMove: U, onPaneMouseLeave: R, onPaneContextMenu: ee, onPaneScroll: W, paneClickDistance: Q, deleteKeyCode: A, selectionKeyCode: _, selectionOnDrag: m, selectionMode: w, onSelectionStart: f, onSelectionEnd: g, multiSelectionKeyCode: C, panActivationKeyCode: S, zoomActivationKeyCode: M, elementsSelectable: B, zoomOnScroll: k, zoomOnPinch: j, zoomOnDoubleClick: H, panOnScroll: D, panOnScrollSpeed: V, panOnScrollMode: O, panOnDrag: q, autoPanOnSelection: X, defaultViewport: I, translateExtent: T, minZoom: L, maxZoom: b, onSelectionContextMenu: d, preventScrolling: N, noDragClassName: z, noWheelClassName: Y, noPanClassName: K, disableKeyboardA11y: ae, onViewportChange: ke, isControlledViewport: !!pe, children: h.jsxs(bg, { children: [h.jsx(wg, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: s, onReconnect: Ce, onReconnectStart: Ee, onReconnectEnd: Ne, onlyRenderVisibleElements: P, onEdgeContextMenu: F, onEdgeMouseEnter: J, onEdgeMouseMove: oe, onEdgeMouseLeave: de, reconnectRadius: xe, defaultMarkerColor: E, noPanClassName: K, disableKeyboardA11y: ae, rfId: fe }), h.jsx(Mg, { style: v, type: p, component: x, containerStyle: y }), h.jsx("div", { className: "react-flow__edgelabel-renderer" }), h.jsx(tg, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: i, onNodeMouseEnter: a, onNodeMouseMove: l, onNodeMouseLeave: c, onNodeContextMenu: u, nodeClickDistance: $, onlyRenderVisibleElements: P, noPanClassName: K, noDragClassName: z, disableKeyboardA11y: ae, nodeExtent: re, rfId: fe }), h.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Ys.displayName = "GraphView";
const Pg = he(Ys), $g = Xi(), ti = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: a, minZoom: l = 0.5, maxZoom: c = 2, nodeOrigin: u, nodeExtent: d, zIndexMode: f = "basic" } = {}) => {
  const g = /* @__PURE__ */ new Map(), p = /* @__PURE__ */ new Map(), v = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), y = o ?? t ?? [], _ = n ?? e ?? [], m = u ?? [0, 0], w = d ?? At;
  os(v, x, y);
  const { nodesInitialized: C } = co(_, g, p, {
    nodeOrigin: m,
    nodeExtent: w,
    zIndexMode: f
  });
  let S = [0, 0, 1];
  if (s && r && i) {
    const M = Vt(g, {
      filter: (I) => !!((I.width || I.initialWidth) && (I.height || I.initialHeight))
    }), { x: A, y: P, zoom: B } = ko(M, r, i, l, c, a?.padding ?? 0.1);
    S = [A, P, B];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: i ?? 0,
    transform: S,
    nodes: _,
    nodesInitialized: C,
    nodeLookup: g,
    parentLookup: p,
    edges: y,
    edgeLookup: x,
    connectionLookup: v,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: o !== void 0,
    panZoom: null,
    minZoom: l,
    maxZoom: c,
    translateExtent: At,
    nodeExtent: w,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: dt.Strict,
    domNode: null,
    paneDragging: !1,
    noPanClassName: "nopan",
    nodeOrigin: m,
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
    fitViewQueued: s ?? !1,
    fitViewOptions: a,
    fitViewResolver: null,
    connection: { ...Hi },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: $g,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Li,
    zIndexMode: f,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, jg = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: a, minZoom: l, maxZoom: c, nodeOrigin: u, nodeExtent: d, zIndexMode: f }) => Zf((g, p) => {
  async function v() {
    const { nodeLookup: x, panZoom: y, fitViewOptions: _, fitViewResolver: m, width: w, height: C, minZoom: S, maxZoom: M } = p();
    y && (await Fd({
      nodes: x,
      width: w,
      height: C,
      panZoom: y,
      minZoom: S,
      maxZoom: M
    }, _), m?.resolve(!0), g({ fitViewResolver: null }));
  }
  return {
    ...ti({
      nodes: e,
      edges: t,
      width: r,
      height: i,
      fitView: s,
      fitViewOptions: a,
      minZoom: l,
      maxZoom: c,
      nodeOrigin: u,
      nodeExtent: d,
      defaultNodes: n,
      defaultEdges: o,
      zIndexMode: f
    }),
    setNodes: (x) => {
      const { nodeLookup: y, parentLookup: _, nodeOrigin: m, elevateNodesOnSelect: w, fitViewQueued: C, zIndexMode: S, nodesSelectionActive: M } = p(), { nodesInitialized: A, hasSelectedNodes: P } = co(x, y, _, {
        nodeOrigin: m,
        nodeExtent: d,
        elevateNodesOnSelect: w,
        checkEquality: !0,
        zIndexMode: S
      }), B = M && P;
      C && A ? (v(), g({
        nodes: x,
        nodesInitialized: A,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: B
      })) : g({ nodes: x, nodesInitialized: A, nodesSelectionActive: B });
    },
    setEdges: (x) => {
      const { connectionLookup: y, edgeLookup: _ } = p();
      os(y, _, x), g({ edges: x });
    },
    setDefaultNodesAndEdges: (x, y) => {
      if (x) {
        const { setNodes: _ } = p();
        _(x), g({ hasDefaultNodes: !0 });
      }
      if (y) {
        const { setEdges: _ } = p();
        _(y), g({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (x) => {
      const { triggerNodeChanges: y, nodeLookup: _, parentLookup: m, domNode: w, nodeOrigin: C, nodeExtent: S, debug: M, fitViewQueued: A, zIndexMode: P } = p(), { changes: B, updatedInternals: I } = hf(x, _, m, w, C, S, P);
      I && (lf(_, m, { nodeOrigin: C, nodeExtent: S, zIndexMode: P }), A ? (v(), g({ fitViewQueued: !1, fitViewOptions: void 0 })) : g({}), B?.length > 0 && (M && console.log("React Flow: trigger node changes", B), y?.(B)));
    },
    updateNodePositions: (x, y = !1) => {
      const _ = [];
      let m = [];
      const { nodeLookup: w, triggerNodeChanges: C, connection: S, updateConnection: M, onNodesChangeMiddlewareMap: A } = p();
      for (const [P, B] of x) {
        const I = w.get(P), T = !!(I?.expandParent && I?.parentId && B?.position), L = {
          id: P,
          type: "position",
          position: T ? {
            x: Math.max(0, B.position.x),
            y: Math.max(0, B.position.y)
          } : B.position,
          dragging: y
        };
        if (I && S.inProgress && S.fromNode.id === I.id) {
          const b = nt(I, S.fromHandle, G.Left, !0);
          M({ ...S, from: b });
        }
        T && I.parentId && _.push({
          id: P,
          parentId: I.parentId,
          rect: {
            ...B.internals.positionAbsolute,
            width: B.measured.width ?? 0,
            height: B.measured.height ?? 0
          }
        }), m.push(L);
      }
      if (_.length > 0) {
        const { parentLookup: P, nodeOrigin: B } = p(), I = $o(_, w, P, B);
        m.push(...I);
      }
      for (const P of A.values())
        m = P(m);
      C(m);
    },
    triggerNodeChanges: (x) => {
      const { onNodesChange: y, setNodes: _, nodes: m, hasDefaultNodes: w, debug: C } = p();
      if (x?.length) {
        if (w) {
          const S = xs(x, m);
          _(S);
        }
        C && console.log("React Flow: trigger node changes", x), y?.(x);
      }
    },
    triggerEdgeChanges: (x) => {
      const { onEdgesChange: y, setEdges: _, edges: m, hasDefaultEdges: w, debug: C } = p();
      if (x?.length) {
        if (w) {
          const S = ws(x, m);
          _(S);
        }
        C && console.log("React Flow: trigger edge changes", x), y?.(x);
      }
    },
    addSelectedNodes: (x) => {
      const { multiSelectionActive: y, edgeLookup: _, nodeLookup: m, triggerNodeChanges: w, triggerEdgeChanges: C } = p();
      if (y) {
        const S = x.map((M) => Ze(M, !0));
        w(S);
        return;
      }
      w(st(m, /* @__PURE__ */ new Set([...x]), !0)), C(st(_));
    },
    addSelectedEdges: (x) => {
      const { multiSelectionActive: y, edgeLookup: _, nodeLookup: m, triggerNodeChanges: w, triggerEdgeChanges: C } = p();
      if (y) {
        const S = x.map((M) => Ze(M, !0));
        C(S);
        return;
      }
      C(st(_, /* @__PURE__ */ new Set([...x]))), w(st(m, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: x, edges: y } = {}) => {
      const { edges: _, nodes: m, nodeLookup: w, triggerNodeChanges: C, triggerEdgeChanges: S } = p(), M = x || m, A = y || _, P = [];
      for (const I of M) {
        if (!I.selected)
          continue;
        const T = w.get(I.id);
        T && (T.selected = !1), P.push(Ze(I.id, !1));
      }
      const B = [];
      for (const I of A)
        I.selected && B.push(Ze(I.id, !1));
      C(P), S(B);
    },
    setMinZoom: (x) => {
      const { panZoom: y, maxZoom: _ } = p();
      y?.setScaleExtent([x, _]), g({ minZoom: x });
    },
    setMaxZoom: (x) => {
      const { panZoom: y, minZoom: _ } = p();
      y?.setScaleExtent([_, x]), g({ maxZoom: x });
    },
    setTranslateExtent: (x) => {
      p().panZoom?.setTranslateExtent(x), g({ translateExtent: x });
    },
    resetSelectedElements: () => {
      const { edges: x, nodes: y, triggerNodeChanges: _, triggerEdgeChanges: m, elementsSelectable: w } = p();
      if (!w)
        return;
      const C = y.reduce((M, A) => A.selected ? [...M, Ze(A.id, !1)] : M, []), S = x.reduce((M, A) => A.selected ? [...M, Ze(A.id, !1)] : M, []);
      _(C), m(S);
    },
    setNodeExtent: (x) => {
      const { nodes: y, nodeLookup: _, parentLookup: m, nodeOrigin: w, elevateNodesOnSelect: C, nodeExtent: S, zIndexMode: M } = p();
      x[0][0] === S[0][0] && x[0][1] === S[0][1] && x[1][0] === S[1][0] && x[1][1] === S[1][1] || (co(y, _, m, {
        nodeOrigin: w,
        nodeExtent: x,
        elevateNodesOnSelect: C,
        checkEquality: !1,
        zIndexMode: M
      }), g({ nodeExtent: x }));
    },
    panBy: (x) => {
      const { transform: y, width: _, height: m, panZoom: w, translateExtent: C } = p();
      return gf({ delta: x, panZoom: w, transform: y, translateExtent: C, width: _, height: m });
    },
    setCenter: async (x, y, _) => {
      const { width: m, height: w, maxZoom: C, panZoom: S } = p();
      if (!S)
        return !1;
      const M = typeof _?.zoom < "u" ? _.zoom : C;
      return await S.setViewport({
        x: m / 2 - x * M,
        y: w / 2 - y * M,
        zoom: M
      }, { duration: _?.duration, ease: _?.ease, interpolate: _?.interpolate }), !0;
    },
    cancelConnection: () => {
      g({
        connection: { ...Hi }
      });
    },
    updateConnection: (x) => {
      g({ connection: x });
    },
    reset: () => g({ ...ti() })
  };
}, Object.is);
function Tg({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: i, initialMinZoom: s, initialMaxZoom: a, initialFitViewOptions: l, fitView: c, nodeOrigin: u, nodeExtent: d, zIndexMode: f, children: g }) {
  const [p] = te(() => jg({
    nodes: e,
    edges: t,
    defaultNodes: n,
    defaultEdges: o,
    width: r,
    height: i,
    fitView: c,
    minZoom: s,
    maxZoom: a,
    fitViewOptions: l,
    nodeOrigin: u,
    nodeExtent: d,
    zIndexMode: f
  }));
  return h.jsx(Kf, { value: p, children: h.jsx(bh, { children: g }) });
}
function zg({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: i, height: s, fitView: a, fitViewOptions: l, minZoom: c, maxZoom: u, nodeOrigin: d, nodeExtent: f, zIndexMode: g }) {
  return Rt(Nn) ? h.jsx(h.Fragment, { children: e }) : h.jsx(Tg, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: i, initialHeight: s, fitView: a, initialFitViewOptions: l, initialMinZoom: c, initialMaxZoom: u, nodeOrigin: d, nodeExtent: f, zIndexMode: g, children: e });
}
const Rg = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function Lg({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: i, edgeTypes: s, onNodeClick: a, onEdgeClick: l, onInit: c, onMove: u, onMoveStart: d, onMoveEnd: f, onConnect: g, onConnectStart: p, onConnectEnd: v, onClickConnectStart: x, onClickConnectEnd: y, onNodeMouseEnter: _, onNodeMouseMove: m, onNodeMouseLeave: w, onNodeContextMenu: C, onNodeDoubleClick: S, onNodeDragStart: M, onNodeDrag: A, onNodeDragStop: P, onNodesDelete: B, onEdgesDelete: I, onDelete: T, onSelectionChange: L, onSelectionDragStart: b, onSelectionDrag: N, onSelectionDragStop: E, onSelectionContextMenu: k, onSelectionStart: j, onSelectionEnd: D, onBeforeDelete: V, connectionMode: O, connectionLineType: H = Xe.Bezier, connectionLineStyle: q, connectionLineComponent: X, connectionLineContainerStyle: Z, deleteKeyCode: ne = "Backspace", selectionKeyCode: U = "Shift", selectionOnDrag: R = !1, selectionMode: W = Dt.Full, panActivationKeyCode: ee = "Space", multiSelectionKeyCode: Q = $t() ? "Meta" : "Control", zoomActivationKeyCode: $ = $t() ? "Meta" : "Control", snapToGrid: F, snapGrid: J, onlyRenderVisibleElements: oe = !1, selectNodesOnDrag: de, nodesDraggable: xe, autoPanOnNodeFocus: Ce, nodesConnectable: Ee, nodesFocusable: Ne, nodeOrigin: z = ms, edgesFocusable: Y, edgesReconnectable: K, elementsSelectable: ae = !0, defaultViewport: re = uh, minZoom: fe = 0.5, maxZoom: pe = 2, translateExtent: ke = At, preventScrolling: Dn = !0, nodeExtent: Pn, defaultMarkerColor: Js = "#b1b1b7", zoomOnScroll: ea = !0, zoomOnPinch: ta = !0, panOnScroll: na = !1, panOnScrollSpeed: oa = 0.5, panOnScrollMode: ra = Ke.Free, zoomOnDoubleClick: ia = !0, panOnDrag: sa = !0, onPaneClick: aa, onPaneMouseEnter: ca, onPaneMouseMove: la, onPaneMouseLeave: ua, onPaneScroll: da, onPaneContextMenu: fa, paneClickDistance: ha = 1, nodeClickDistance: ga = 0, children: pa, onReconnect: ma, onReconnectStart: ya, onReconnectEnd: xa, onEdgeContextMenu: wa, onEdgeDoubleClick: va, onEdgeMouseEnter: ba, onEdgeMouseMove: Sa, onEdgeMouseLeave: _a, reconnectRadius: Ea = 10, onNodesChange: Na, onEdgesChange: Ca, noDragClassName: ka = "nodrag", noWheelClassName: Ia = "nowheel", noPanClassName: Lo = "nopan", fitView: Ho, fitViewOptions: Oo, connectOnClick: Ma, attributionPosition: Aa, proOptions: Da, defaultEdgeOptions: Pa, elevateNodesOnSelect: $a = !0, elevateEdgesOnSelect: ja = !1, disableKeyboardA11y: Vo = !1, autoPanOnConnect: Ta, autoPanOnNodeDrag: za, autoPanOnSelection: Ra = !0, autoPanSpeed: La, connectionRadius: Ha, isValidConnection: Oa, onError: Va, style: Ba, id: Bo, nodeDragThreshold: Fa, connectionDragThreshold: Ya, viewport: Xa, onViewportChange: Wa, width: qa, height: Za, colorMode: Ua = "light", debug: Ga, onScroll: Fo, ariaLabelConfig: Ka, zIndexMode: Yo = "basic", ...Qa }, Ja) {
  const $n = Bo || "1", ec = gh(Ua), tc = me((Xo) => {
    Xo.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), Fo?.(Xo);
  }, [Fo]);
  return h.jsx("div", { "data-testid": "rf__wrapper", ...Qa, onScroll: tc, style: { ...Ba, ...Rg }, ref: Ja, className: ge(["react-flow", r, ec]), id: Bo, role: "application", children: h.jsxs(zg, { nodes: e, edges: t, width: qa, height: Za, fitView: Ho, fitViewOptions: Oo, minZoom: fe, maxZoom: pe, nodeOrigin: z, nodeExtent: Pn, zIndexMode: Yo, children: [h.jsx(hh, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: g, onConnectStart: p, onConnectEnd: v, onClickConnectStart: x, onClickConnectEnd: y, nodesDraggable: xe, autoPanOnNodeFocus: Ce, nodesConnectable: Ee, nodesFocusable: Ne, edgesFocusable: Y, edgesReconnectable: K, elementsSelectable: ae, elevateNodesOnSelect: $a, elevateEdgesOnSelect: ja, minZoom: fe, maxZoom: pe, nodeExtent: Pn, onNodesChange: Na, onEdgesChange: Ca, snapToGrid: F, snapGrid: J, connectionMode: O, translateExtent: ke, connectOnClick: Ma, defaultEdgeOptions: Pa, fitView: Ho, fitViewOptions: Oo, onNodesDelete: B, onEdgesDelete: I, onDelete: T, onNodeDragStart: M, onNodeDrag: A, onNodeDragStop: P, onSelectionDrag: N, onSelectionDragStart: b, onSelectionDragStop: E, onMove: u, onMoveStart: d, onMoveEnd: f, noPanClassName: Lo, nodeOrigin: z, rfId: $n, autoPanOnConnect: Ta, autoPanOnNodeDrag: za, autoPanSpeed: La, onError: Va, connectionRadius: Ha, isValidConnection: Oa, selectNodesOnDrag: de, nodeDragThreshold: Fa, connectionDragThreshold: Ya, onBeforeDelete: V, debug: Ga, ariaLabelConfig: Ka, zIndexMode: Yo }), h.jsx(Pg, { onInit: c, onNodeClick: a, onEdgeClick: l, onNodeMouseEnter: _, onNodeMouseMove: m, onNodeMouseLeave: w, onNodeContextMenu: C, onNodeDoubleClick: S, nodeTypes: i, edgeTypes: s, connectionLineType: H, connectionLineStyle: q, connectionLineComponent: X, connectionLineContainerStyle: Z, selectionKeyCode: U, selectionOnDrag: R, selectionMode: W, deleteKeyCode: ne, multiSelectionKeyCode: Q, panActivationKeyCode: ee, zoomActivationKeyCode: $, onlyRenderVisibleElements: oe, defaultViewport: re, translateExtent: ke, minZoom: fe, maxZoom: pe, preventScrolling: Dn, zoomOnScroll: ea, zoomOnPinch: ta, zoomOnDoubleClick: ia, panOnScroll: na, panOnScrollSpeed: oa, panOnScrollMode: ra, panOnDrag: sa, autoPanOnSelection: Ra, onPaneClick: aa, onPaneMouseEnter: ca, onPaneMouseMove: la, onPaneMouseLeave: ua, onPaneScroll: da, onPaneContextMenu: fa, paneClickDistance: ha, nodeClickDistance: ga, onSelectionContextMenu: k, onSelectionStart: j, onSelectionEnd: D, onReconnect: ma, onReconnectStart: ya, onReconnectEnd: xa, onEdgeContextMenu: wa, onEdgeDoubleClick: va, onEdgeMouseEnter: ba, onEdgeMouseMove: Sa, onEdgeMouseLeave: _a, reconnectRadius: Ea, defaultMarkerColor: Js, noDragClassName: ka, noWheelClassName: Ia, noPanClassName: Lo, rfId: $n, disableKeyboardA11y: Vo, nodeExtent: Pn, viewport: Xa, onViewportChange: Wa }), h.jsx(lh, { onSelectionChange: L }), pa, h.jsx(rh, { proOptions: Da, position: Aa }), h.jsx(oh, { rfId: $n, disableKeyboardA11y: Vo })] }) });
}
var Hg = bs(Lg);
function Og({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return h.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: ge(["react-flow__background-pattern", n, o]) });
}
function Vg({ radius: e, className: t }) {
  return h.jsx("circle", { cx: e, cy: e, r: e, className: ge(["react-flow__background-pattern", "dots", t]) });
}
var We;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(We || (We = {}));
const Bg = {
  [We.Dots]: 1,
  [We.Lines]: 1,
  [We.Cross]: 6
}, Fg = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function Xs({
  id: e,
  variant: t = We.Dots,
  // only used for dots and cross
  gap: n = 20,
  // only used for lines and cross
  size: o,
  lineWidth: r = 1,
  offset: i = 0,
  color: s,
  bgColor: a,
  style: l,
  className: c,
  patternClassName: u
}) {
  const d = ie(null), { transform: f, patternId: g } = ce(Fg, le), p = o || Bg[t], v = t === We.Dots, x = t === We.Cross, y = Array.isArray(n) ? n : [n, n], _ = [y[0] * f[2] || 1, y[1] * f[2] || 1], m = p * f[2], w = Array.isArray(i) ? i : [i, i], C = x ? [m, m] : _, S = [
    w[0] * f[2] || 1 + C[0] / 2,
    w[1] * f[2] || 1 + C[1] / 2
  ], M = `${g}${e || ""}`;
  return h.jsxs("svg", { className: ge(["react-flow__background", c]), style: {
    ...l,
    ...kn,
    "--xy-background-color-props": a,
    "--xy-background-pattern-color-props": s
  }, ref: d, "data-testid": "rf__background", children: [h.jsx("pattern", { id: M, x: f[0] % _[0], y: f[1] % _[1], width: _[0], height: _[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${S[0]},-${S[1]})`, children: v ? h.jsx(Vg, { radius: m / 2, className: u }) : h.jsx(Og, { dimensions: C, lineWidth: r, variant: t, className: u }) }), h.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${M})` })] });
}
Xs.displayName = "Background";
const Yg = he(Xs);
function Xg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: h.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function Wg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: h.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function qg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: h.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function Zg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: h.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function Ug() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: h.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function Qt({ children: e, className: t, ...n }) {
  return h.jsx("button", { type: "button", className: ge(["react-flow__controls-button", t]), ...n, children: e });
}
const Gg = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function Ws({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: i, onZoomOut: s, onFitView: a, onInteractiveChange: l, className: c, children: u, position: d = "bottom-left", orientation: f = "vertical", "aria-label": g }) {
  const p = ue(), { isInteractive: v, minZoomReached: x, maxZoomReached: y, ariaLabelConfig: _ } = ce(Gg, le), { zoomIn: m, zoomOut: w, fitView: C } = jo(), S = () => {
    m(), i?.();
  }, M = () => {
    w(), s?.();
  }, A = () => {
    C(r), a?.();
  }, P = () => {
    p.setState({
      nodesDraggable: !v,
      nodesConnectable: !v,
      elementsSelectable: !v
    }), l?.(!v);
  }, B = f === "horizontal" ? "horizontal" : "vertical";
  return h.jsxs(Cn, { className: ge(["react-flow__controls", B, c]), position: d, style: e, "data-testid": "rf__controls", "aria-label": g ?? _["controls.ariaLabel"], children: [t && h.jsxs(h.Fragment, { children: [h.jsx(Qt, { onClick: S, className: "react-flow__controls-zoomin", title: _["controls.zoomIn.ariaLabel"], "aria-label": _["controls.zoomIn.ariaLabel"], disabled: y, children: h.jsx(Xg, {}) }), h.jsx(Qt, { onClick: M, className: "react-flow__controls-zoomout", title: _["controls.zoomOut.ariaLabel"], "aria-label": _["controls.zoomOut.ariaLabel"], disabled: x, children: h.jsx(Wg, {}) })] }), n && h.jsx(Qt, { className: "react-flow__controls-fitview", onClick: A, title: _["controls.fitView.ariaLabel"], "aria-label": _["controls.fitView.ariaLabel"], children: h.jsx(qg, {}) }), o && h.jsx(Qt, { className: "react-flow__controls-interactive", onClick: P, title: _["controls.interactive.ariaLabel"], "aria-label": _["controls.interactive.ariaLabel"], children: v ? h.jsx(Ug, {}) : h.jsx(Zg, {}) }), u] });
}
Ws.displayName = "Controls";
const Kg = he(Ws);
function Qg({ id: e, x: t, y: n, width: o, height: r, style: i, color: s, strokeColor: a, strokeWidth: l, className: c, borderRadius: u, shapeRendering: d, selected: f, onClick: g }) {
  const { background: p, backgroundColor: v } = i || {}, x = s || p || v;
  return h.jsx("rect", { className: ge(["react-flow__minimap-node", { selected: f }, c]), x: t, y: n, rx: u, ry: u, width: o, height: r, style: {
    fill: x,
    stroke: a,
    strokeWidth: l
  }, shapeRendering: d, onClick: g ? (y) => g(y, e) : void 0 });
}
const Jg = he(Qg), ep = (e) => e.nodes.map((t) => t.id), Zn = (e) => e instanceof Function ? e : () => e;
function tp({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: i = Jg,
  onClick: s
}) {
  const a = ce(ep, le), l = Zn(t), c = Zn(e), u = Zn(n), d = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return h.jsx(h.Fragment, { children: a.map((f) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    h.jsx(op, { id: f, nodeColorFunc: l, nodeStrokeColorFunc: c, nodeClassNameFunc: u, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: i, onClick: s, shapeRendering: d }, f)
  )) });
}
function np({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: i, shapeRendering: s, NodeComponent: a, onClick: l }) {
  const { node: c, x: u, y: d, width: f, height: g } = ce((p) => {
    const v = p.nodeLookup.get(e);
    if (!v)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const x = v.internals.userNode, { x: y, y: _ } = v.internals.positionAbsolute, { width: m, height: w } = Be(x);
    return {
      node: x,
      x: y,
      y: _,
      width: m,
      height: w
    };
  }, le);
  return !c || c.hidden || !Wi(c) ? null : h.jsx(a, { x: u, y: d, width: f, height: g, style: c.style, selected: !!c.selected, className: o(c), color: t(c), borderRadius: r, strokeColor: n(c), strokeWidth: i, shapeRendering: s, onClick: l, id: c.id });
}
const op = he(np);
var rp = he(tp);
const ip = 200, sp = 150, ap = (e) => !e.hidden, cp = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? Yi(Vt(e.nodeLookup, { filter: ap }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, lp = "react-flow__minimap-desc";
function qs({
  style: e,
  className: t,
  nodeStrokeColor: n,
  nodeColor: o,
  nodeClassName: r = "",
  nodeBorderRadius: i = 5,
  nodeStrokeWidth: s,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: a,
  bgColor: l,
  maskColor: c,
  maskStrokeColor: u,
  maskStrokeWidth: d,
  position: f = "bottom-right",
  onClick: g,
  onNodeClick: p,
  pannable: v = !1,
  zoomable: x = !1,
  ariaLabel: y,
  inversePan: _,
  zoomStep: m = 1,
  offsetScale: w = 5
}) {
  const C = ue(), S = ie(null), { boundingRect: M, viewBB: A, rfId: P, panZoom: B, translateExtent: I, flowWidth: T, flowHeight: L, ariaLabelConfig: b } = ce(cp, le), N = e?.width ?? ip, E = e?.height ?? sp, k = M.width / N, j = M.height / E, D = Math.max(k, j), V = D * N, O = D * E, H = w * D, q = M.x - (V - M.width) / 2 - H, X = M.y - (O - M.height) / 2 - H, Z = V + H * 2, ne = O + H * 2, U = `${lp}-${P}`, R = ie(0), W = ie();
  R.current = D, se(() => {
    if (S.current && B)
      return W.current = _f({
        domNode: S.current,
        panZoom: B,
        getTransform: () => C.getState().transform,
        getViewScale: () => R.current
      }), () => {
        W.current?.destroy();
      };
  }, [B]), se(() => {
    W.current?.update({
      translateExtent: I,
      width: T,
      height: L,
      inversePan: _,
      pannable: v,
      zoomStep: m,
      zoomable: x
    });
  }, [v, x, _, m, I, T, L]);
  const ee = g ? (F) => {
    const [J, oe] = W.current?.pointer(F) || [0, 0];
    g(F, { x: J, y: oe });
  } : void 0, Q = p ? me((F, J) => {
    const oe = C.getState().nodeLookup.get(J).internals.userNode;
    p(F, oe);
  }, []) : void 0, $ = y ?? b["minimap.ariaLabel"];
  return h.jsx(Cn, { position: f, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-background-color-props": typeof c == "string" ? c : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof d == "number" ? d * D : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof s == "number" ? s : void 0
  }, className: ge(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: h.jsxs("svg", { width: N, height: E, viewBox: `${q} ${X} ${Z} ${ne}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": U, ref: S, onClick: ee, children: [$ && h.jsx("title", { id: U, children: $ }), h.jsx(rp, { onClick: Q, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: i, nodeClassName: r, nodeStrokeWidth: s, nodeComponent: a }), h.jsx("path", { className: "react-flow__minimap-mask", d: `M${q - H},${X - H}h${Z + H * 2}v${ne + H * 2}h${-Z - H * 2}z
        M${A.x},${A.y}h${A.width}v${A.height}h${-A.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
qs.displayName = "MiniMap";
const up = he(qs), dp = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, fp = {
  [pt.Line]: "right",
  [pt.Handle]: "bottom-right"
};
function hp({ nodeId: e, position: t, variant: n = pt.Handle, className: o, style: r = void 0, children: i, color: s, minWidth: a = 10, minHeight: l = 10, maxWidth: c = Number.MAX_VALUE, maxHeight: u = Number.MAX_VALUE, keepAspectRatio: d = !1, resizeDirection: f, autoScale: g = !0, shouldResize: p, onResizeStart: v, onResize: x, onResizeEnd: y }) {
  const _ = Ns(), m = typeof e == "string" ? e : _, w = ue(), C = ie(null), S = n === pt.Handle, M = ce(me(dp(S && g), [S, g]), le), A = ie(null), P = t ?? fp[n];
  se(() => {
    if (!(!C.current || !m))
      return A.current || (A.current = zf({
        domNode: C.current,
        nodeId: m,
        getStoreItems: () => {
          const { nodeLookup: I, transform: T, snapGrid: L, snapToGrid: b, nodeOrigin: N, domNode: E } = w.getState();
          return {
            nodeLookup: I,
            transform: T,
            snapGrid: L,
            snapToGrid: b,
            nodeOrigin: N,
            paneDomNode: E
          };
        },
        onChange: (I, T) => {
          const { triggerNodeChanges: L, nodeLookup: b, parentLookup: N, nodeOrigin: E } = w.getState(), k = [], j = { x: I.x, y: I.y }, D = b.get(m);
          if (D && D.expandParent && D.parentId) {
            const V = D.origin ?? E, O = I.width ?? D.measured.width ?? 0, H = I.height ?? D.measured.height ?? 0, q = {
              id: D.id,
              parentId: D.parentId,
              rect: {
                width: O,
                height: H,
                ...qi({
                  x: I.x ?? D.position.x,
                  y: I.y ?? D.position.y
                }, { width: O, height: H }, D.parentId, b, V)
              }
            }, X = $o([q], b, N, E);
            k.push(...X), j.x = I.x ? Math.max(V[0] * O, I.x) : void 0, j.y = I.y ? Math.max(V[1] * H, I.y) : void 0;
          }
          if (j.x !== void 0 && j.y !== void 0) {
            const V = {
              id: m,
              type: "position",
              position: { ...j }
            };
            k.push(V);
          }
          if (I.width !== void 0 && I.height !== void 0) {
            const O = {
              id: m,
              type: "dimensions",
              resizing: !0,
              setAttributes: f ? f === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: I.width,
                height: I.height
              }
            };
            k.push(O);
          }
          for (const V of T) {
            const O = {
              ...V,
              type: "position"
            };
            k.push(O);
          }
          L(k);
        },
        onEnd: ({ width: I, height: T }) => {
          const L = {
            id: m,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: I,
              height: T
            }
          };
          w.getState().triggerNodeChanges([L]);
        }
      })), A.current.update({
        controlPosition: P,
        boundaries: {
          minWidth: a,
          minHeight: l,
          maxWidth: c,
          maxHeight: u
        },
        keepAspectRatio: d,
        resizeDirection: f,
        onResizeStart: v,
        onResize: x,
        onResizeEnd: y,
        shouldResize: p
      }), () => {
        A.current?.destroy();
      };
  }, [
    P,
    a,
    l,
    c,
    u,
    d,
    v,
    x,
    y,
    p
  ]);
  const B = P.split("-");
  return h.jsx("div", { className: ge(["react-flow__resize-control", "nodrag", ...B, n, o]), ref: C, style: {
    ...r,
    scale: M,
    ...s && { [S ? "backgroundColor" : "borderColor"]: s }
  }, children: i });
}
he(hp);
const gp = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), Zs = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var pp = {
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
const mp = mn(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: o,
    className: r = "",
    children: i,
    iconNode: s,
    ...a
  }, l) => Gn(
    "svg",
    {
      ref: l,
      ...pp,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: Zs("lucide", r),
      ...a
    },
    [
      ...s.map(([c, u]) => Gn(c, u)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const _e = (e, t) => {
  const n = mn(
    ({ className: o, ...r }, i) => Gn(mp, {
      ref: i,
      iconNode: t,
      className: Zs(`lucide-${gp(e)}`, o),
      ...r
    })
  );
  return n.displayName = `${e}`, n;
};
const yp = _e("Boxes", [
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
const Mn = _e("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const xp = _e("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const fo = _e("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const Tt = _e("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const wp = _e("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const vp = _e("ListTree", [
  ["path", { d: "M21 12h-8", key: "1bmf0i" }],
  ["path", { d: "M21 6H8", key: "1pqkrb" }],
  ["path", { d: "M21 18h-8", key: "1tm79t" }],
  ["path", { d: "M3 6v4c0 1.1.9 2 2 2h3", key: "1ywdgy" }],
  ["path", { d: "M3 10v6c0 1.1.9 2 2 2h3", key: "2wc746" }]
]);
const Us = _e("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const bp = _e("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const Sp = _e("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const _p = _e("Save", [
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
const Ep = _e("Search", [
  ["circle", { cx: "11", cy: "11", r: "8", key: "4ej97u" }],
  ["path", { d: "m21 21-4.3-4.3", key: "1qie3q" }]
]);
const ni = _e("Trash2", [
  ["path", { d: "M3 6h18", key: "d0wm0j" }],
  ["path", { d: "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", key: "4alrt4" }],
  ["path", { d: "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", key: "v07s0e" }],
  ["line", { x1: "10", x2: "10", y1: "11", y2: "17", key: "1uufr5" }],
  ["line", { x1: "14", x2: "14", y1: "11", y2: "17", key: "xtxkd" }]
]), je = "/_elsa/workflow-management";
async function Np(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), qe(e, `${je}/definitions?${n.toString()}`);
}
async function Cp(e, t) {
  return qe(e, `${je}/definitions/${encodeURIComponent(t)}`);
}
async function kp(e, t) {
  return Ft(e, `${je}/definitions`, t);
}
async function Ip(e, t) {
  await qe(e, `${je}/definitions/${encodeURIComponent(t)}`, {
    method: "DELETE"
  });
}
async function Mp(e, t) {
  await Ft(e, `${je}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function Ap(e, t) {
  await qe(e, `${je}/definitions/${encodeURIComponent(t)}/permanent`, {
    method: "DELETE"
  });
}
async function Dp(e, t) {
  return qe(e, `${je}/drafts/${encodeURIComponent(t.id)}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify({ state: t.state, layout: t.layout })
  });
}
async function Pp(e, t) {
  return Ft(e, `${je}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function $p(e, t) {
  return Ft(e, `${je}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function Gs(e, t) {
  return Ft(e, `${je}/executables/${encodeURIComponent(t)}/run`, {});
}
async function jp(e) {
  return qe(e, "/_demo/workflows/executables");
}
async function Ks(e) {
  return qe(e, `${je}/activities`);
}
async function Ft(e, t, n) {
  return qe(e, t, {
    method: "POST",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify(n)
  });
}
async function qe(e, t, n) {
  const o = await fetch(new URL(t, e.baseUrl).toString(), n), r = await o.text();
  if (!o.ok)
    throw new Error(Tp(r) || `Request failed with ${o.status}.`);
  return r ? JSON.parse(r) : {};
}
function Tp(e) {
  if (!e.trim()) return "";
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
    if (typeof t.detail == "string") return t.detail;
    if (typeof t.title == "string") return t.title;
  } catch {
    return e;
  }
  return e;
}
const zo = "elsa.sequence.structure", An = "elsa.flowchart.structure";
function oi(e, t) {
  if (!e) return null;
  let n = e, o = Oe(n)[0];
  if (!o) return null;
  for (const r of t) {
    const i = Oe(n).find((a) => a.id === r.slotId);
    if (!i) return null;
    const s = i.activities.find((a) => a.nodeId === r.ownerNodeId);
    if (!s || (n = s, o = Oe(n)[0], !o)) return null;
  }
  return { owner: n, slot: o };
}
function Oe(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = Wp(t), r = Un(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: qp(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, i]) => Un(i)).map(([i, s]) => ({
    id: `${t.kind}:${i}`,
    label: Up(i),
    property: i,
    mode: "generic",
    activities: Un(s) ?? []
  }));
}
function zp(e, t, n) {
  const o = new Map(t.map((s) => [s.activityVersionId, s])), r = new Map(n.map((s) => [s.nodeId, s])), i = e.slot.activities.map((s, a) => {
    const l = o.get(s.activityVersionId), c = r.get(s.nodeId) ?? Zp(e.slot.mode, a);
    return {
      id: s.nodeId,
      type: "workflowActivity",
      position: { x: c.x, y: c.y },
      data: {
        label: l?.displayName ?? s.activityVersionId,
        activityVersionId: s.activityVersionId,
        activityTypeKey: l?.activityTypeKey,
        childSlots: Oe(s)
      }
    };
  });
  return {
    nodes: i,
    edges: e.slot.mode === "flowchart" ? Yp(e.owner) : Fp(e.slot, i)
  };
}
function ho(e, t, n) {
  if (t.length === 0) {
    const a = Oe(e)[0];
    return a ? pn(e, a, n) : e;
  }
  const [o, ...r] = t, i = Oe(e).find((a) => a.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((a) => a.nodeId === o.ownerNodeId ? ho(a, r, n) : a);
  return pn(e, i, s);
}
function go(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, i = Oe(e).find((a) => a.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((a) => a.nodeId === o.ownerNodeId ? go(a, r, n) : a);
  return pn(e, i, s);
}
function pn(e, t, n) {
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
function Rp(e, t, n) {
  const o = new Map(e.slot.activities.map((i) => [i.nodeId, i])), r = t.map((i) => o.get(i.id)).filter((i) => !!i);
  return e.slot.mode === "sequence" && r.sort((i, s) => {
    const a = t.find((c) => c.id === i.nodeId), l = t.find((c) => c.id === s.nodeId);
    return (a?.position.x ?? 0) - (l?.position.x ?? 0);
  }), pn(e.owner, e.slot, r);
}
function Lp(e, t) {
  return {
    ...e,
    structure: Bp(e.structure, t)
  };
}
function Hp(e, t) {
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
function Op(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: Vp(e)
  };
}
function ot(e) {
  return e.displayName || e.activityTypeKey.split(".").at(-1) || e.activityTypeKey;
}
function Vp(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: zo,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: An,
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
function Bp(e, t) {
  return e ? {
    ...e,
    payload: {
      ...e.payload,
      connections: t.map((n) => ({
        id: n.id,
        source: { nodeId: n.source, port: n.sourceHandle ?? "Done" },
        target: { nodeId: n.target, port: n.targetHandle ?? "Done" }
      }))
    }
  } : e ?? null;
}
function Fp(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function Yp(e) {
  if (e.structure?.kind !== An) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, i = n.target;
    return !r?.nodeId || !i?.nodeId ? null : {
      id: typeof n.id == "string" ? String(n.id) : `flow-${o}-${r.nodeId}-${i.nodeId}`,
      source: r.nodeId,
      target: i.nodeId,
      sourceHandle: r.port,
      targetHandle: i.port,
      type: "smoothstep",
      label: r.port && r.port !== "Done" ? r.port : void 0
    };
  }).filter((n) => !!n) : [];
}
function Un(e) {
  return Array.isArray(e) ? e.filter(Xp) : null;
}
function Xp(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Wp(e) {
  return e.kind === zo ? "sequence" : e.kind === An ? "flowchart" : "generic";
}
function qp(e) {
  return e.kind === zo || e.kind === An, "Activities";
}
function Zp(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function Up(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Gp = { workflowActivity: p0 }, ri = "application/x-elsa-activity-version-id", Kp = 6, Qp = 1200, Jp = [10, 25, 50], e0 = 10;
function w0(e) {
  e.navigation.add({
    id: "workflows",
    label: "Workflows",
    path: "/workflows/definitions",
    activePathPrefix: "/workflows",
    order: 20,
    iconColor: "#0ea5e9"
  }), e.routes.add({
    id: "workflows-definitions",
    path: "/workflows/definitions",
    label: "Workflow definitions",
    component: () => /* @__PURE__ */ h.jsx(t0, { context: e.backend })
  }), e.routes.add({
    id: "workflows-executables",
    path: "/workflows/executables",
    label: "Workflow executables",
    component: () => /* @__PURE__ */ h.jsx(n0, { context: e.backend })
  }), e.routes.add({
    id: "workflows-instances",
    path: "/workflows/instances",
    label: "Workflow instances",
    component: () => /* @__PURE__ */ h.jsx(o0, {})
  });
}
function t0({ context: e }) {
  const [t, n] = te(ii);
  se(() => {
    const r = () => n(ii());
    return window.addEventListener("popstate", r), () => window.removeEventListener("popstate", r);
  }, []);
  const o = (r) => {
    const i = r ? `/workflows/definitions?definition=${encodeURIComponent(r)}` : "/workflows/definitions";
    window.history.pushState({}, "", i), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return t ? /* @__PURE__ */ h.jsx(g0, { context: e, definitionId: t, onBack: () => o(null) }) : /* @__PURE__ */ h.jsx(Ro, { activePath: "/workflows/definitions", title: "Definitions", children: /* @__PURE__ */ h.jsx(r0, { context: e, onOpen: o }) });
}
function n0({ context: e }) {
  const [t, n] = te(si);
  return se(() => {
    const o = () => n(si());
    return window.addEventListener("popstate", o), () => window.removeEventListener("popstate", o);
  }, []), /* @__PURE__ */ h.jsx(Ro, { activePath: "/workflows/executables", title: "Executables", children: /* @__PURE__ */ h.jsx(s0, { context: e, definitionFilter: t }) });
}
function o0() {
  return /* @__PURE__ */ h.jsx(Ro, { activePath: "/workflows/instances", title: "Instances", children: /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: "Workflow instance history will appear here when the runtime exposes an instance query endpoint." }) });
}
function Ro({ activePath: e, title: t, children: n }) {
  const o = (r) => {
    window.history.pushState({}, "", r), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ h.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ h.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ h.jsxs("div", { children: [
      /* @__PURE__ */ h.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ h.jsx("h2", { children: t })
    ] }) }),
    /* @__PURE__ */ h.jsxs("nav", { className: "wf-section-tabs", "aria-label": "Workflow views", children: [
      /* @__PURE__ */ h.jsx("a", { className: e === "/workflows/definitions" ? "active" : "", href: "/workflows/definitions", onClick: (r) => {
        r.preventDefault(), o("/workflows/definitions");
      }, children: "Definitions" }),
      /* @__PURE__ */ h.jsx("a", { className: e === "/workflows/executables" ? "active" : "", href: "/workflows/executables", onClick: (r) => {
        r.preventDefault(), o("/workflows/executables");
      }, children: "Executables" }),
      /* @__PURE__ */ h.jsx("a", { className: e === "/workflows/instances" ? "active" : "", href: "/workflows/instances", onClick: (r) => {
        r.preventDefault(), o("/workflows/instances");
      }, children: "Instances" })
    ] }),
    n
  ] });
}
function ii() {
  return new URLSearchParams(window.location.search).get("definition");
}
function si() {
  return new URLSearchParams(window.location.search).get("definition");
}
function r0({ context: e, onOpen: t }) {
  const [n, o] = te(""), [r, i] = te("active"), [s, a] = te(1), [l, c] = te(e0), [u, d] = te("loading"), [f, g] = te(""), [p, v] = te(""), [x, y] = te([]), [_, m] = te(0), [w, C] = te(() => /* @__PURE__ */ new Set()), [S, M] = te(null), [A, P] = te(!1), [B, I] = te([]), [T, L] = te("idle"), b = ie(null), N = ye(() => x.map(($) => $.id), [x]), E = N.filter(($) => w.has($)).length, k = N.length > 0 && E === N.length, j = me(async () => {
    d("loading"), g("");
    try {
      const $ = await Np(e, { search: n, state: r, page: s, pageSize: l }), F = typeof $.totalCount == "number", J = $.totalCount ?? $.definitions.length, oe = Qs(J, l);
      if (J > 0 && s > oe) {
        a(oe);
        return;
      }
      y(F ? $.definitions : c0($.definitions, s, l)), m(J), d("ready");
    } catch ($) {
      g($ instanceof Error ? $.message : String($)), d("failed");
    }
  }, [e, n, r, s, l]);
  se(() => {
    j();
  }, [j]), se(() => {
    b.current && (b.current.indeterminate = E > 0 && !k);
  }, [k, E]);
  const D = me(async () => {
    if (!(T === "loading" || T === "ready")) {
      L("loading");
      try {
        const $ = await Ks(e);
        I($.activities ?? []), L("ready");
      } catch ($) {
        L("failed"), g($ instanceof Error ? $.message : String($));
      }
    }
  }, [T, e]), V = () => {
    g(""), v(""), M({ name: "", description: "", rootKind: "flowchart" }), D();
  }, O = async () => {
    if (S?.name.trim()) {
      P(!0), g(""), v("");
      try {
        const $ = await kp(e, {
          name: S.name.trim(),
          description: S.description.trim() || null,
          rootKind: S.rootKind
        });
        M(null), t($.definition.id);
      } catch ($) {
        g($ instanceof Error ? $.message : String($));
      } finally {
        P(!1);
      }
    }
  }, H = ($) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent($)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, q = async () => {
    if (x.length === 1 && s > 1) {
      a(s - 1);
      return;
    }
    await j();
  }, X = () => C(/* @__PURE__ */ new Set()), Z = ($, F) => {
    C((J) => {
      const oe = new Set(J);
      return F ? oe.add($) : oe.delete($), oe;
    });
  }, ne = ($) => {
    C((F) => {
      const J = new Set(F);
      for (const oe of N)
        $ ? J.add(oe) : J.delete(oe);
      return J;
    });
  }, U = ($) => {
    i($), a(1), X();
  }, R = ($) => {
    o($), a(1), X();
  }, W = async ($) => {
    if (window.confirm(`Delete workflow definition "${$.name}"? You can restore it from the Deleted view.`)) {
      v(""), g("");
      try {
        await Ip(e, $.id), Z($.id, !1), v(`Deleted ${$.name}`), await q();
      } catch (F) {
        g(F instanceof Error ? F.message : String(F));
      }
    }
  }, ee = async ($) => {
    v(""), g("");
    try {
      await Mp(e, $.id), Z($.id, !1), v(`Restored ${$.name}`), await q();
    } catch (F) {
      g(F instanceof Error ? F.message : String(F));
    }
  }, Q = async ($) => {
    if (window.confirm(`Permanently delete workflow definition "${$.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`)) {
      v(""), g("");
      try {
        await Ap(e, $.id), Z($.id, !1), v(`Permanently deleted ${$.name}`), await q();
      } catch (F) {
        g(F instanceof Error ? F.message : String(F));
      }
    }
  };
  return /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
    /* @__PURE__ */ h.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ h.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ h.jsx("button", { type: "button", className: r === "active" ? "active" : "", "aria-selected": r === "active", onClick: () => U("active"), children: "Active" }),
        /* @__PURE__ */ h.jsx("button", { type: "button", className: r === "deleted" ? "active" : "", "aria-selected": r === "deleted", onClick: () => U("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ h.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ h.jsx(Ep, { size: 15 }),
        /* @__PURE__ */ h.jsx("input", { value: n, onChange: ($) => R($.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
        j();
      }, children: "Refresh" }),
      /* @__PURE__ */ h.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ h.jsxs("button", { type: "button", title: "Create workflow", onClick: V, children: [
        /* @__PURE__ */ h.jsx(bp, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    u === "failed" ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Tt, { size: 16 }),
      " ",
      f
    ] }) : null,
    u !== "failed" && f ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Tt, { size: 16 }),
      " ",
      f
    ] }) : null,
    p ? /* @__PURE__ */ h.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ h.jsx(Mn, { size: 14 }),
      " ",
      p
    ] }) : null,
    w.size > 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ h.jsxs("span", { children: [
        w.size,
        " selected"
      ] }),
      /* @__PURE__ */ h.jsx("button", { type: "button", onClick: X, children: "Clear selection" })
    ] }) : null,
    u === "loading" ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: "Loading workflow definitions..." }) : null,
    u === "ready" && x.length === 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-empty", children: [
      "No ",
      r,
      " workflow definitions found."
    ] }) : null,
    u === "ready" && x.length > 0 ? /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
      /* @__PURE__ */ h.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ h.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ h.jsx(
            "input",
            {
              ref: b,
              type: "checkbox",
              checked: k,
              onChange: ($) => ne($.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ h.jsx("span", { children: "Name" }),
          /* @__PURE__ */ h.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ h.jsx("span", { children: r === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ h.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ h.jsx("span", { children: "Actions" })
        ] }),
        x.map(($) => /* @__PURE__ */ h.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${$.name}`,
            "aria-selected": w.has($.id),
            tabIndex: 0,
            onClick: () => t($.id),
            onKeyDown: (F) => {
              F.currentTarget === F.target && (F.key !== "Enter" && F.key !== " " || (F.preventDefault(), t($.id)));
            },
            children: [
              /* @__PURE__ */ h.jsx("label", { className: "wf-row-select", onClick: (F) => F.stopPropagation(), children: /* @__PURE__ */ h.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: w.has($.id),
                  onChange: (F) => Z($.id, F.target.checked),
                  "aria-label": `Select workflow definition ${$.name}`
                }
              ) }),
              /* @__PURE__ */ h.jsxs("span", { children: [
                /* @__PURE__ */ h.jsx("strong", { children: $.name }),
                /* @__PURE__ */ h.jsx("small", { children: $.description || $.id })
              ] }),
              /* @__PURE__ */ h.jsx("span", { children: $.latestVersion ?? "No version" }),
              /* @__PURE__ */ h.jsx("span", { children: r === "deleted" ? po($.deletedAt) : $.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ h.jsx("span", { children: po($.lastModifiedAt) }),
              /* @__PURE__ */ h.jsx("span", { className: "wf-row-actions", onClick: (F) => F.stopPropagation(), children: r === "active" ? /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
                /* @__PURE__ */ h.jsx("button", { type: "button", onClick: (F) => {
                  F.stopPropagation(), t($.id);
                }, children: "Open" }),
                /* @__PURE__ */ h.jsx("button", { type: "button", onClick: (F) => {
                  F.stopPropagation(), H($.id);
                }, children: "Artifacts" }),
                /* @__PURE__ */ h.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  W($);
                }, children: [
                  /* @__PURE__ */ h.jsx(ni, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
                /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
                  ee($);
                }, children: [
                  /* @__PURE__ */ h.jsx(Sp, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ h.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  Q($);
                }, children: [
                  /* @__PURE__ */ h.jsx(ni, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          $.id
        ))
      ] }),
      /* @__PURE__ */ h.jsx(
        a0,
        {
          page: s,
          pageSize: l,
          totalCount: _,
          onPageChange: a,
          onPageSizeChange: ($) => {
            c($), a(1);
          }
        }
      )
    ] }) : null,
    S ? /* @__PURE__ */ h.jsx(
      i0,
      {
        draft: S,
        activities: B,
        catalogState: T,
        creating: A,
        onChange: ($) => M($),
        onClose: () => M(null),
        onSubmit: O
      }
    ) : null
  ] });
}
function i0({ draft: e, activities: t, catalogState: n, creating: o, onChange: r, onClose: i, onSubmit: s }) {
  const a = ye(() => l0(t), [t]);
  return /* @__PURE__ */ h.jsx("div", { className: "wf-dialog-backdrop", role: "presentation", children: /* @__PURE__ */ h.jsx("section", { className: "wf-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "workflow-create-title", children: /* @__PURE__ */ h.jsxs(
    "form",
    {
      onSubmit: (l) => {
        l.preventDefault(), s();
      },
      children: [
        /* @__PURE__ */ h.jsx("div", { className: "wf-dialog-heading", children: /* @__PURE__ */ h.jsx("h3", { id: "workflow-create-title", children: "Create Workflow" }) }),
        /* @__PURE__ */ h.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ h.jsx("span", { children: "Display name" }),
          /* @__PURE__ */ h.jsx(
            "input",
            {
              autoFocus: !0,
              "aria-label": "Display name",
              value: e.name,
              onChange: (l) => r({ ...e, name: l.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ h.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ h.jsx("span", { children: "Description" }),
          /* @__PURE__ */ h.jsx(
            "textarea",
            {
              "aria-label": "Description",
              rows: 3,
              value: e.description,
              onChange: (l) => r({ ...e, description: l.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ h.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ h.jsx("span", { children: "Root activity" }),
          /* @__PURE__ */ h.jsxs(
            "select",
            {
              "aria-label": "Root activity",
              value: e.rootKind,
              onChange: (l) => r({ ...e, rootKind: l.target.value }),
              disabled: n === "loading",
              children: [
                /* @__PURE__ */ h.jsx("optgroup", { label: "Composite roots", children: a.compositeRoots.map((l) => /* @__PURE__ */ h.jsx("option", { value: l.value, children: l.label }, l.value)) }),
                a.otherCategories.map((l) => /* @__PURE__ */ h.jsx("optgroup", { label: l.name, children: l.activities.map((c) => /* @__PURE__ */ h.jsx("option", { value: `unsupported:${c.activityVersionId}`, disabled: !0, children: ot(c) }, c.activityVersionId)) }, l.name))
              ]
            }
          )
        ] }),
        n === "loading" ? /* @__PURE__ */ h.jsx("div", { className: "wf-dialog-note", children: "Loading activity catalog..." }) : null,
        n === "failed" ? /* @__PURE__ */ h.jsx("div", { className: "wf-dialog-note", children: "Activity catalog could not be loaded. Composite roots remain available." }) : null,
        /* @__PURE__ */ h.jsxs("div", { className: "wf-dialog-actions", children: [
          /* @__PURE__ */ h.jsx("button", { type: "button", onClick: i, disabled: o, children: "Cancel" }),
          /* @__PURE__ */ h.jsx("button", { type: "submit", disabled: o || !e.name.trim(), children: o ? "Creating..." : "Create" })
        ] })
      ]
    }
  ) }) });
}
function s0({ context: e, definitionFilter: t }) {
  const [n, o] = te("loading"), [r, i] = te(""), [s, a] = te(""), [l, c] = te([]), u = ye(
    () => t ? l.filter((g) => g.definitionId === t || g.sourceId === t) : l,
    [t, l]
  ), d = me(async () => {
    o("loading"), i("");
    try {
      c(await jp(e)), o("ready");
    } catch (g) {
      i(g instanceof Error ? g.message : String(g)), o("failed");
    }
  }, [e]);
  se(() => {
    d();
  }, [d]);
  const f = async (g) => {
    a(""), i("");
    try {
      await Gs(e, g.artifactId), a(`Started ${g.artifactId}`);
    } catch (p) {
      i(p instanceof Error ? p.message : String(p));
    }
  };
  return /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
    /* @__PURE__ */ h.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
        d();
      }, children: "Refresh" }),
      t ? /* @__PURE__ */ h.jsxs("span", { className: "wf-filter-chip", children: [
        "Definition ",
        t
      ] }) : null
    ] }),
    n === "failed" ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Tt, { size: 16 }),
      " ",
      r
    ] }) : null,
    s ? /* @__PURE__ */ h.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ h.jsx(Mn, { size: 14 }),
      " ",
      s
    ] }) : null,
    n === "loading" ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: "Loading workflow executables..." }) : null,
    n === "ready" && u.length === 0 ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: t ? "No workflow executables found for this definition." : "No workflow executables found. Publish a workflow definition to create one." }) : null,
    n === "ready" && u.length > 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ h.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ h.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ h.jsx("span", { children: "Version" }),
        /* @__PURE__ */ h.jsx("span", { children: "Source" }),
        /* @__PURE__ */ h.jsx("span", { children: "Root" }),
        /* @__PURE__ */ h.jsx("span", { children: "Published" }),
        /* @__PURE__ */ h.jsx("span", { children: "Actions" })
      ] }),
      u.map((g) => /* @__PURE__ */ h.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ h.jsxs("span", { children: [
          /* @__PURE__ */ h.jsx("strong", { children: g.artifactId }),
          /* @__PURE__ */ h.jsx("small", { children: g.artifactHash })
        ] }),
        /* @__PURE__ */ h.jsx("span", { children: g.artifactVersion }),
        /* @__PURE__ */ h.jsx("span", { children: d0(g) }),
        /* @__PURE__ */ h.jsx("span", { children: f0(g) }),
        /* @__PURE__ */ h.jsx("span", { children: po(g.publishedAt ?? g.createdAt) }),
        /* @__PURE__ */ h.jsx("span", { className: "wf-row-actions", children: /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
          f(g);
        }, children: [
          /* @__PURE__ */ h.jsx(Us, { size: 13 }),
          " Run"
        ] }) })
      ] }, g.artifactId))
    ] }) : null
  ] });
}
function a0({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const i = Qs(n, t), s = n === 0 ? 0 : (e - 1) * t + 1, a = Math.min(e * t, n);
  return /* @__PURE__ */ h.jsxs("div", { className: "wf-pagination", "aria-label": "Workflow definition pagination", children: [
    /* @__PURE__ */ h.jsxs("span", { className: "wf-pagination-summary", "aria-live": "polite", children: [
      "Showing ",
      s,
      "-",
      a,
      " of ",
      n
    ] }),
    /* @__PURE__ */ h.jsxs("label", { className: "wf-page-size", children: [
      "Rows",
      /* @__PURE__ */ h.jsx("select", { value: t, onChange: (l) => r(Number(l.target.value)), children: Jp.map((l) => /* @__PURE__ */ h.jsx("option", { value: l, children: l }, l)) })
    ] }),
    /* @__PURE__ */ h.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ h.jsx(xp, { size: 14 }),
        " Previous"
      ] }),
      /* @__PURE__ */ h.jsxs("span", { children: [
        "Page ",
        e,
        " of ",
        i
      ] }),
      /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => o(e + 1), disabled: e >= i, "aria-label": "Next page", title: "Next page", children: [
        "Next ",
        /* @__PURE__ */ h.jsx(fo, { size: 14 })
      ] })
    ] })
  ] });
}
function c0(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function Qs(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function l0(e) {
  const t = [
    { value: "flowchart", label: "Flowchart" },
    { value: "sequence", label: "Sequence" }
  ], n = /* @__PURE__ */ new Map();
  for (const r of e) {
    if (u0(r)) continue;
    const i = r.category || "Uncategorized";
    n.set(i, [...n.get(i) ?? [], r]);
  }
  const o = Array.from(n.entries()).sort(([r], [i]) => r.localeCompare(i)).map(([r, i]) => ({
    name: r,
    activities: i.sort((s, a) => ot(s).localeCompare(ot(a)))
  }));
  return { compositeRoots: t, otherCategories: o };
}
function u0(e) {
  const t = ot(e);
  return t === "Flowchart" || t === "Sequence" || e.activityTypeKey.endsWith(".Flowchart") || e.activityTypeKey.endsWith(".Sequence");
}
function d0(e) {
  return e.sourceKind || e.sourceId || e.sourceVersion ? [e.sourceKind, e.sourceId, e.sourceVersion].filter(Boolean).join(" / ") : e.definitionId;
}
function f0(e) {
  return h0(e.rootActivityType) || e.rootActivityType;
}
function h0(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function g0({ context: e, definitionId: t, onBack: n }) {
  const [o, r] = te(null), [i, s] = te(null), [a, l] = te([]), [c, u] = te([]), [d, f] = te([]), [g, p] = te([]), [v, x] = te(null), [y, _] = te(null), [m, w] = te(""), [C, S] = te(""), [M, A] = te(!1), [P, B] = te(null), I = ie(null), T = ie(""), L = ie(0), b = ie(null), N = ie(!1), E = i?.state.rootActivity ?? null, k = ye(() => oi(E, c), [E, c]), j = ye(() => new Map(a.map((z) => [z.activityVersionId, z])), [a]), D = ye(() => k?.slot.activities.find((z) => z.nodeId === y) ?? null, [k, y]), V = D ? Oe(D) : [], O = me(async () => {
    w("");
    const [z, Y] = await Promise.all([
      Cp(e, t),
      Ks(e)
    ]), K = z.draft ?? null;
    r(z), T.current = K ? bt(K) : "", s(K), l(Y.activities ?? []), u([]), _(null);
  }, [e, t]);
  se(() => {
    O().catch((z) => w(z instanceof Error ? z.message : String(z)));
  }, [O]), se(() => {
    if (!k) {
      f([]), p([]);
      return;
    }
    const z = zp(k, a, i?.layout ?? []);
    f(z.nodes), p(z.edges);
  }, [k, a, i?.layout]);
  const H = (z) => {
    s((Y) => Y && { ...Y, state: { ...Y.state, rootActivity: z } });
  }, q = me((z, Y) => {
    const K = Op(z, y0(z));
    if (!i?.state.rootActivity) {
      H(K), _(K.nodeId);
      return;
    }
    if (!k) {
      if (!Oe(K)[0]) {
        S(""), w("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      s((re) => {
        if (!re?.state.rootActivity) return re;
        const fe = re.state.rootActivity, pe = ho(K, [], [fe]), ke = Y ? [
          ...re.layout.filter((Dn) => Dn.nodeId !== fe.nodeId),
          {
            nodeId: fe.nodeId,
            x: Math.round(Y.x),
            y: Math.round(Y.y)
          }
        ] : re.layout;
        return {
          ...re,
          layout: ke,
          state: {
            ...re.state,
            rootActivity: pe
          }
        };
      }), _(i.state.rootActivity.nodeId), w(""), S(`Wrapped root in ${ot(z)}`);
      return;
    }
    s((ae) => {
      if (!ae?.state.rootActivity) return ae;
      const re = oi(ae.state.rootActivity, c);
      if (!re) return ae;
      const fe = ho(ae.state.rootActivity, c, [...re.slot.activities, K]), pe = Y ? [
        ...ae.layout.filter((ke) => ke.nodeId !== K.nodeId),
        {
          nodeId: K.nodeId,
          x: Math.round(Y.x),
          y: Math.round(Y.y)
        }
      ] : ae.layout;
      return {
        ...ae,
        layout: pe,
        state: {
          ...ae.state,
          rootActivity: fe
        }
      };
    }), _(K.nodeId);
  }, [i?.state.rootActivity, c, k]), X = me((z, Y) => {
    if (!I.current) return null;
    const K = I.current.getBoundingClientRect();
    return v ? v.screenToFlowPosition({ x: z, y: Y }) : {
      x: z - K.left,
      y: Y - K.top
    };
  }, [v]), Z = me((z, Y, K) => {
    if (!I.current) return !1;
    const ae = I.current.getBoundingClientRect();
    if (!(Y >= ae.left && Y <= ae.right && K >= ae.top && K <= ae.bottom)) return !1;
    const fe = X(Y, K);
    return fe ? (q(z, fe), !0) : !1;
  }, [q, X]);
  se(() => {
    const z = (K) => {
      const ae = b.current;
      if (!ae) return;
      Math.hypot(K.clientX - ae.startX, K.clientY - ae.startY) >= Kp && (ae.dragging = !0);
    }, Y = (K) => {
      const ae = b.current;
      if (b.current = null, !ae?.dragging || !I.current) return;
      const re = I.current.getBoundingClientRect();
      K.clientX >= re.left && K.clientX <= re.right && K.clientY >= re.top && K.clientY <= re.bottom && (N.current = !0, window.setTimeout(() => {
        N.current = !1;
      }, 0), Z(ae.activity, K.clientX, K.clientY));
    };
    return window.addEventListener("pointermove", z), window.addEventListener("pointerup", Y), window.addEventListener("pointercancel", Y), () => {
      window.removeEventListener("pointermove", z), window.removeEventListener("pointerup", Y), window.removeEventListener("pointercancel", Y);
    };
  }, [v, Z]);
  const ne = (z, Y) => {
    z.dataTransfer.setData(ri, Y.activityVersionId), z.dataTransfer.setData("text/plain", Y.activityVersionId), z.dataTransfer.effectAllowed = "copy";
  }, U = (z, Y) => {
    z.clientX === 0 && z.clientY === 0 || Z(Y, z.clientX, z.clientY) && (N.current = !0, window.setTimeout(() => {
      N.current = !1;
    }, 0));
  }, R = (z, Y) => {
    z.button === 0 && (b.current = {
      activity: Y,
      startX: z.clientX,
      startY: z.clientY,
      dragging: !1
    });
  }, W = (z) => {
    N.current || q(z);
  }, ee = (z) => {
    z.preventDefault(), z.dataTransfer.dropEffect = "copy";
  }, Q = (z) => {
    z.preventDefault();
    const Y = z.dataTransfer.getData(ri) || z.dataTransfer.getData("text/plain"), K = j.get(Y);
    K && Z(K, z.clientX, z.clientY);
  }, $ = me(async (z, Y) => {
    const K = ++L.current, ae = bt(z);
    w("");
    try {
      const re = await Dp(e, z), fe = bt(re);
      T.current = fe, s((pe) => !pe || pe.id !== re.id ? pe : bt(pe) === ae ? re : { ...pe, validationErrors: re.validationErrors }), K === L.current && S(Y);
    } catch (re) {
      K === L.current && (S(""), w(re instanceof Error ? re.message : String(re)));
    }
  }, [e]);
  se(() => {
    if (!M || !i || bt(i) === T.current) return;
    S("Autosaving...");
    const Y = window.setTimeout(() => {
      $(i, "Autosaved");
    }, Qp);
    return () => window.clearTimeout(Y);
  }, [M, i, $]);
  const F = async () => {
    i && (S("Saving..."), await $(i, "Saved"));
  }, J = async () => {
    if (i) {
      S("Promoting...");
      try {
        const z = await Pp(e, i.id), Y = await $p(e, z.versionId);
        B(Y.artifactId), S(`Published ${Y.artifactVersion}`), await O();
      } catch (z) {
        S(""), w(z instanceof Error ? z.message : String(z));
      }
    }
  }, oe = async () => {
    if (P) {
      S("Running...");
      try {
        await Gs(e, P), S("Run dispatched");
      } catch (z) {
        S(""), w(z instanceof Error ? z.message : String(z));
      }
    }
  }, de = (z) => f((Y) => xs(z, Y)), xe = (z) => p((Y) => ws(z, Y)), Ce = (z) => {
    if (!i?.state.rootActivity || !k || k.slot.mode !== "flowchart") return;
    const Y = vs(z, g), K = Lp(k.owner, Y);
    p(Y), H(go(i.state.rootActivity, c, K));
  }, Ee = () => {
    s((z) => {
      if (!z) return z;
      const Y = Hp(z.layout, d);
      if (!z.state.rootActivity || !k) return { ...z, layout: Y };
      const K = Rp(k, d);
      return {
        ...z,
        layout: Y,
        state: {
          ...z.state,
          rootActivity: go(z.state.rootActivity, c, K)
        }
      };
    });
  }, Ne = (z, Y, K) => {
    u((ae) => [...ae, { ownerNodeId: z.nodeId, slotId: Y, label: K }]), _(null);
  };
  return !o || !i ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: m || "Loading workflow editor..." }) : /* @__PURE__ */ h.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ h.jsx("button", { type: "button", className: "wf-link-button", onClick: n, children: "Definitions" }),
      /* @__PURE__ */ h.jsx(fo, { size: 14 }),
      /* @__PURE__ */ h.jsx("strong", { children: o.definition.name }),
      /* @__PURE__ */ h.jsx("span", { className: "wf-chip", children: "Draft" }),
      C ? /* @__PURE__ */ h.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ h.jsx(Mn, { size: 13 }),
        " ",
        C
      ] }) : null,
      /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ h.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ h.jsx("input", { type: "checkbox", checked: M, onChange: (z) => A(z.target.checked) }),
          /* @__PURE__ */ h.jsx("span", { children: "Autosave" })
        ] }),
        /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
          F();
        }, children: [
          /* @__PURE__ */ h.jsx(_p, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
          J();
        }, children: [
          /* @__PURE__ */ h.jsx(wp, { size: 15 }),
          " Promote"
        ] }),
        /* @__PURE__ */ h.jsxs("button", { type: "button", disabled: !P, onClick: () => {
          oe();
        }, children: [
          /* @__PURE__ */ h.jsx(Us, { size: 15 }),
          " Run"
        ] })
      ] })
    ] }),
    m ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Tt, { size: 16 }),
      " ",
      m
    ] }) : null,
    /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-body", children: [
      /* @__PURE__ */ h.jsxs("aside", { className: "wf-palette", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ h.jsx(yp, { size: 15 }),
          " Activities"
        ] }),
        /* @__PURE__ */ h.jsx("div", { className: "wf-palette-list", children: a.map((z) => /* @__PURE__ */ h.jsxs(
          "button",
          {
            type: "button",
            draggable: !0,
            onClick: () => W(z),
            onDragStart: (Y) => ne(Y, z),
            onDragEnd: (Y) => U(Y, z),
            onPointerDown: (Y) => R(Y, z),
            children: [
              /* @__PURE__ */ h.jsx("strong", { children: ot(z) }),
              /* @__PURE__ */ h.jsx("small", { children: z.category })
            ]
          },
          z.activityVersionId
        )) })
      ] }),
      /* @__PURE__ */ h.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-breadcrumb", children: [
          /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
            u([]), _(null);
          }, children: "Root" }),
          c.map((z, Y) => /* @__PURE__ */ h.jsxs(zt.Fragment, { children: [
            /* @__PURE__ */ h.jsx(fo, { size: 13 }),
            /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
              u(c.slice(0, Y + 1)), _(null);
            }, children: z.label })
          ] }, `${z.ownerNodeId}-${z.slotId}-${Y}`))
        ] }),
        /* @__PURE__ */ h.jsx("div", { className: "wf-canvas", ref: I, onDragOver: ee, onDrop: Q, children: /* @__PURE__ */ h.jsxs(
          Hg,
          {
            nodes: d,
            edges: g,
            nodeTypes: Gp,
            onInit: x,
            onNodesChange: de,
            onEdgesChange: xe,
            onConnect: Ce,
            onDragOver: ee,
            onDrop: Q,
            onNodeClick: (z, Y) => _(Y.id),
            onNodeDragStop: Ee,
            fitView: !0,
            children: [
              /* @__PURE__ */ h.jsx(Yg, { gap: 18, size: 1 }),
              /* @__PURE__ */ h.jsx(Kg, {}),
              /* @__PURE__ */ h.jsx(up, { pannable: !0, zoomable: !0 })
            ]
          }
        ) }),
        /* @__PURE__ */ h.jsx(m0, { draft: i })
      ] }),
      /* @__PURE__ */ h.jsxs("aside", { className: "wf-inspector", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ h.jsx(vp, { size: 15 }),
          " Inspector"
        ] }),
        D ? /* @__PURE__ */ h.jsxs("div", { className: "wf-inspector-content", children: [
          /* @__PURE__ */ h.jsx("h3", { children: d.find((z) => z.id === D.nodeId)?.data.label ?? D.nodeId }),
          /* @__PURE__ */ h.jsxs("dl", { children: [
            /* @__PURE__ */ h.jsx("dt", { children: "Node ID" }),
            /* @__PURE__ */ h.jsx("dd", { children: D.nodeId }),
            /* @__PURE__ */ h.jsx("dt", { children: "Activity version" }),
            /* @__PURE__ */ h.jsx("dd", { children: D.activityVersionId })
          ] }),
          V.length > 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-slot-list", children: [
            /* @__PURE__ */ h.jsx("span", { children: "Embedded slots" }),
            V.map((z) => /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => Ne(D, z.id, `${d.find((Y) => Y.id === D.nodeId)?.data.label ?? D.nodeId} / ${z.label}`), children: [
              z.label,
              /* @__PURE__ */ h.jsxs("small", { children: [
                z.activities.length,
                " activit",
                z.activities.length === 1 ? "y" : "ies"
              ] })
            ] }, z.id))
          ] }) : /* @__PURE__ */ h.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
        ] }) : /* @__PURE__ */ h.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." })
      ] })
    ] })
  ] });
}
function p0({ data: e, selected: t }) {
  const n = e;
  return /* @__PURE__ */ h.jsxs("div", { className: t ? "wf-node selected" : "wf-node", children: [
    /* @__PURE__ */ h.jsx(mt, { type: "target", position: G.Left }),
    /* @__PURE__ */ h.jsx("strong", { children: n.label }),
    /* @__PURE__ */ h.jsx("small", { children: n.activityTypeKey ?? n.activityVersionId }),
    n.childSlots.length > 0 ? /* @__PURE__ */ h.jsxs("span", { children: [
      n.childSlots.length,
      " embedded slot",
      n.childSlots.length === 1 ? "" : "s"
    ] }) : null,
    /* @__PURE__ */ h.jsx(mt, { type: "source", position: G.Right })
  ] });
}
function m0({ draft: e }) {
  return e.validationErrors.length ? /* @__PURE__ */ h.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ h.jsx(Tt, { size: 14 }),
    e.validationErrors.length,
    " validation issue",
    e.validationErrors.length === 1 ? "" : "s"
  ] }) : /* @__PURE__ */ h.jsxs("div", { className: "wf-validation ok", children: [
    /* @__PURE__ */ h.jsx(Mn, { size: 14 }),
    " No validation errors"
  ] });
}
function y0(e) {
  return `${ot(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function bt(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function po(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
export {
  w0 as register
};
