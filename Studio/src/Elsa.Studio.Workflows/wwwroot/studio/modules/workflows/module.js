import Wt, { memo as pe, forwardRef as Cn, useRef as se, useEffect as ae, useCallback as ye, useContext as Xt, useMemo as xe, createContext as Eo, useState as oe, useLayoutEffect as ac, createElement as io } from "react";
import "@tanstack/react-query";
function cc(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var Fn = { exports: {} }, kt = {};
var er;
function lc() {
  if (er) return kt;
  er = 1;
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
  return kt.Fragment = t, kt.jsx = n, kt.jsxs = n, kt;
}
var tr;
function uc() {
  return tr || (tr = 1, Fn.exports = lc()), Fn.exports;
}
var h = uc();
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
var dc = { value: () => {
} };
function kn() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new cn(n);
}
function cn(e) {
  this._ = e;
}
function fc(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
cn.prototype = kn.prototype = {
  constructor: cn,
  on: function(e, t) {
    var n = this._, o = fc(e + "", n), r, i = -1, s = o.length;
    if (arguments.length < 2) {
      for (; ++i < s; ) if ((r = (e = o[i]).type) && (r = hc(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++i < s; )
      if (r = (e = o[i]).type) n[r] = nr(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = nr(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new cn(e);
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
function hc(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function nr(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = dc, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var so = "http://www.w3.org/1999/xhtml";
const or = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: so,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function In(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), or.hasOwnProperty(t) ? { space: or[t], local: e } : e;
}
function pc(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === so && t.documentElement.namespaceURI === so ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function gc(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function gi(e) {
  var t = In(e);
  return (t.local ? gc : pc)(t);
}
function mc() {
}
function No(e) {
  return e == null ? mc : function() {
    return this.querySelector(e);
  };
}
function yc(e) {
  typeof e != "function" && (e = No(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = new Array(s), l, c, u = 0; u < s; ++u)
      (l = i[u]) && (c = e.call(l, l.__data__, u, i)) && ("__data__" in l && (c.__data__ = l.__data__), a[u] = c);
  return new Ee(o, this._parents);
}
function xc(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function wc() {
  return [];
}
function mi(e) {
  return e == null ? wc : function() {
    return this.querySelectorAll(e);
  };
}
function vc(e) {
  return function() {
    return xc(e.apply(this, arguments));
  };
}
function bc(e) {
  typeof e == "function" ? e = vc(e) : e = mi(e);
  for (var t = this._groups, n = t.length, o = [], r = [], i = 0; i < n; ++i)
    for (var s = t[i], a = s.length, l, c = 0; c < a; ++c)
      (l = s[c]) && (o.push(e.call(l, l.__data__, c, s)), r.push(l));
  return new Ee(o, r);
}
function yi(e) {
  return function() {
    return this.matches(e);
  };
}
function xi(e) {
  return function(t) {
    return t.matches(e);
  };
}
var Sc = Array.prototype.find;
function _c(e) {
  return function() {
    return Sc.call(this.children, e);
  };
}
function Ec() {
  return this.firstElementChild;
}
function Nc(e) {
  return this.select(e == null ? Ec : _c(typeof e == "function" ? e : xi(e)));
}
var Cc = Array.prototype.filter;
function kc() {
  return Array.from(this.children);
}
function Ic(e) {
  return function() {
    return Cc.call(this.children, e);
  };
}
function Mc(e) {
  return this.selectAll(e == null ? kc : Ic(typeof e == "function" ? e : xi(e)));
}
function Ac(e) {
  typeof e != "function" && (e = yi(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = [], l, c = 0; c < s; ++c)
      (l = i[c]) && e.call(l, l.__data__, c, i) && a.push(l);
  return new Ee(o, this._parents);
}
function wi(e) {
  return new Array(e.length);
}
function Dc() {
  return new Ee(this._enter || this._groups.map(wi), this._parents);
}
function pn(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
pn.prototype = {
  constructor: pn,
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
function Pc(e) {
  return function() {
    return e;
  };
}
function jc(e, t, n, o, r, i) {
  for (var s = 0, a, l = t.length, c = i.length; s < c; ++s)
    (a = t[s]) ? (a.__data__ = i[s], o[s] = a) : n[s] = new pn(e, i[s]);
  for (; s < l; ++s)
    (a = t[s]) && (r[s] = a);
}
function $c(e, t, n, o, r, i, s) {
  var a, l, c = /* @__PURE__ */ new Map(), u = t.length, d = i.length, f = new Array(u), p;
  for (a = 0; a < u; ++a)
    (l = t[a]) && (f[a] = p = s.call(l, l.__data__, a, t) + "", c.has(p) ? r[a] = l : c.set(p, l));
  for (a = 0; a < d; ++a)
    p = s.call(e, i[a], a, i) + "", (l = c.get(p)) ? (o[a] = l, l.__data__ = i[a], c.delete(p)) : n[a] = new pn(e, i[a]);
  for (a = 0; a < u; ++a)
    (l = t[a]) && c.get(f[a]) === l && (r[a] = l);
}
function Tc(e) {
  return e.__data__;
}
function zc(e, t) {
  if (!arguments.length) return Array.from(this, Tc);
  var n = t ? $c : jc, o = this._parents, r = this._groups;
  typeof e != "function" && (e = Pc(e));
  for (var i = r.length, s = new Array(i), a = new Array(i), l = new Array(i), c = 0; c < i; ++c) {
    var u = o[c], d = r[c], f = d.length, p = Rc(e.call(u, u && u.__data__, c, o)), g = p.length, x = a[c] = new Array(g), w = s[c] = new Array(g), y = l[c] = new Array(f);
    n(u, d, x, w, y, p, t);
    for (var S = 0, m = 0, b, N; S < g; ++S)
      if (b = x[S]) {
        for (S >= m && (m = S + 1); !(N = w[m]) && ++m < g; ) ;
        b._next = N || null;
      }
  }
  return s = new Ee(s, o), s._enter = a, s._exit = l, s;
}
function Rc(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function Lc() {
  return new Ee(this._exit || this._groups.map(wi), this._parents);
}
function Hc(e, t, n) {
  var o = this.enter(), r = this, i = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? i.remove() : n(i), o && r ? o.merge(r).order() : r;
}
function Vc(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, i = o.length, s = Math.min(r, i), a = new Array(r), l = 0; l < s; ++l)
    for (var c = n[l], u = o[l], d = c.length, f = a[l] = new Array(d), p, g = 0; g < d; ++g)
      (p = c[g] || u[g]) && (f[g] = p);
  for (; l < r; ++l)
    a[l] = n[l];
  return new Ee(a, this._parents);
}
function Oc() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, i = o[r], s; --r >= 0; )
      (s = o[r]) && (i && s.compareDocumentPosition(i) ^ 4 && i.parentNode.insertBefore(s, i), i = s);
  return this;
}
function Bc(e) {
  e || (e = Fc);
  function t(d, f) {
    return d && f ? e(d.__data__, f.__data__) : !d - !f;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), i = 0; i < o; ++i) {
    for (var s = n[i], a = s.length, l = r[i] = new Array(a), c, u = 0; u < a; ++u)
      (c = s[u]) && (l[u] = c);
    l.sort(t);
  }
  return new Ee(r, this._parents).order();
}
function Fc(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function Yc() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function Wc() {
  return Array.from(this);
}
function Xc() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length; r < i; ++r) {
      var s = o[r];
      if (s) return s;
    }
  return null;
}
function qc() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function Zc() {
  return !this.node();
}
function Uc(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], i = 0, s = r.length, a; i < s; ++i)
      (a = r[i]) && e.call(a, a.__data__, i, r);
  return this;
}
function Gc(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Kc(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Qc(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function Jc(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function el(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function tl(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function nl(e, t) {
  var n = In(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? Kc : Gc : typeof t == "function" ? n.local ? tl : el : n.local ? Jc : Qc)(n, t));
}
function vi(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function ol(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function rl(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function il(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function sl(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? ol : typeof t == "function" ? il : rl)(e, t, n ?? "")) : pt(this.node(), e);
}
function pt(e, t) {
  return e.style.getPropertyValue(t) || vi(e).getComputedStyle(e, null).getPropertyValue(t);
}
function al(e) {
  return function() {
    delete this[e];
  };
}
function cl(e, t) {
  return function() {
    this[e] = t;
  };
}
function ll(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function ul(e, t) {
  return arguments.length > 1 ? this.each((t == null ? al : typeof t == "function" ? ll : cl)(e, t)) : this.node()[e];
}
function bi(e) {
  return e.trim().split(/^|\s+/);
}
function Co(e) {
  return e.classList || new Si(e);
}
function Si(e) {
  this._node = e, this._names = bi(e.getAttribute("class") || "");
}
Si.prototype = {
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
function _i(e, t) {
  for (var n = Co(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function Ei(e, t) {
  for (var n = Co(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function dl(e) {
  return function() {
    _i(this, e);
  };
}
function fl(e) {
  return function() {
    Ei(this, e);
  };
}
function hl(e, t) {
  return function() {
    (t.apply(this, arguments) ? _i : Ei)(this, e);
  };
}
function pl(e, t) {
  var n = bi(e + "");
  if (arguments.length < 2) {
    for (var o = Co(this.node()), r = -1, i = n.length; ++r < i; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? hl : t ? dl : fl)(n, t));
}
function gl() {
  this.textContent = "";
}
function ml(e) {
  return function() {
    this.textContent = e;
  };
}
function yl(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function xl(e) {
  return arguments.length ? this.each(e == null ? gl : (typeof e == "function" ? yl : ml)(e)) : this.node().textContent;
}
function wl() {
  this.innerHTML = "";
}
function vl(e) {
  return function() {
    this.innerHTML = e;
  };
}
function bl(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function Sl(e) {
  return arguments.length ? this.each(e == null ? wl : (typeof e == "function" ? bl : vl)(e)) : this.node().innerHTML;
}
function _l() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function El() {
  return this.each(_l);
}
function Nl() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Cl() {
  return this.each(Nl);
}
function kl(e) {
  var t = typeof e == "function" ? e : gi(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Il() {
  return null;
}
function Ml(e, t) {
  var n = typeof e == "function" ? e : gi(e), o = t == null ? Il : typeof t == "function" ? t : No(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function Al() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Dl() {
  return this.each(Al);
}
function Pl() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function jl() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function $l(e) {
  return this.select(e ? jl : Pl);
}
function Tl(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function zl(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function Rl(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function Ll(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, i; n < r; ++n)
        i = t[n], (!e.type || i.type === e.type) && i.name === e.name ? this.removeEventListener(i.type, i.listener, i.options) : t[++o] = i;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function Hl(e, t, n) {
  return function() {
    var o = this.__on, r, i = zl(t);
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
function Vl(e, t, n) {
  var o = Rl(e + ""), r, i = o.length, s;
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
  for (a = t ? Hl : Ll, r = 0; r < i; ++r) this.each(a(o[r], t, n));
  return this;
}
function Ni(e, t, n) {
  var o = vi(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function Ol(e, t) {
  return function() {
    return Ni(this, e, t);
  };
}
function Bl(e, t) {
  return function() {
    return Ni(this, e, t.apply(this, arguments));
  };
}
function Fl(e, t) {
  return this.each((typeof t == "function" ? Bl : Ol)(e, t));
}
function* Yl() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length, s; r < i; ++r)
      (s = o[r]) && (yield s);
}
var Ci = [null];
function Ee(e, t) {
  this._groups = e, this._parents = t;
}
function qt() {
  return new Ee([[document.documentElement]], Ci);
}
function Wl() {
  return this;
}
Ee.prototype = qt.prototype = {
  constructor: Ee,
  select: yc,
  selectAll: bc,
  selectChild: Nc,
  selectChildren: Mc,
  filter: Ac,
  data: zc,
  enter: Dc,
  exit: Lc,
  join: Hc,
  merge: Vc,
  selection: Wl,
  order: Oc,
  sort: Bc,
  call: Yc,
  nodes: Wc,
  node: Xc,
  size: qc,
  empty: Zc,
  each: Uc,
  attr: nl,
  style: sl,
  property: ul,
  classed: pl,
  text: xl,
  html: Sl,
  raise: El,
  lower: Cl,
  append: kl,
  insert: Ml,
  remove: Dl,
  clone: $l,
  datum: Tl,
  on: Vl,
  dispatch: Fl,
  [Symbol.iterator]: Yl
};
function _e(e) {
  return typeof e == "string" ? new Ee([[document.querySelector(e)]], [document.documentElement]) : new Ee([[e]], Ci);
}
function Xl(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function De(e, t) {
  if (e = Xl(e), t === void 0 && (t = e.currentTarget), t) {
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
const ql = { passive: !1 }, Tt = { capture: !0, passive: !1 };
function Yn(e) {
  e.stopImmediatePropagation();
}
function ft(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function ki(e) {
  var t = e.document.documentElement, n = _e(e).on("dragstart.drag", ft, Tt);
  "onselectstart" in t ? n.on("selectstart.drag", ft, Tt) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function Ii(e, t) {
  var n = e.document.documentElement, o = _e(e).on("dragstart.drag", null);
  t && (o.on("click.drag", ft, Tt), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const Qt = (e) => () => e;
function ao(e, {
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
ao.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function Zl(e) {
  return !e.ctrlKey && !e.button;
}
function Ul() {
  return this.parentNode;
}
function Gl(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function Kl() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Mi() {
  var e = Zl, t = Ul, n = Gl, o = Kl, r = {}, i = kn("start", "drag", "end"), s = 0, a, l, c, u, d = 0;
  function f(b) {
    b.on("mousedown.drag", p).filter(o).on("touchstart.drag", w).on("touchmove.drag", y, ql).on("touchend.drag touchcancel.drag", S).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function p(b, N) {
    if (!(u || !e.call(this, b, N))) {
      var _ = m(this, t.call(this, b, N), b, N, "mouse");
      _ && (_e(b.view).on("mousemove.drag", g, Tt).on("mouseup.drag", x, Tt), ki(b.view), Yn(b), c = !1, a = b.clientX, l = b.clientY, _("start", b));
    }
  }
  function g(b) {
    if (ft(b), !c) {
      var N = b.clientX - a, _ = b.clientY - l;
      c = N * N + _ * _ > d;
    }
    r.mouse("drag", b);
  }
  function x(b) {
    _e(b.view).on("mousemove.drag mouseup.drag", null), Ii(b.view, c), ft(b), r.mouse("end", b);
  }
  function w(b, N) {
    if (e.call(this, b, N)) {
      var _ = b.changedTouches, C = t.call(this, b, N), A = _.length, D, B;
      for (D = 0; D < A; ++D)
        (B = m(this, C, b, N, _[D].identifier, _[D])) && (Yn(b), B("start", b, _[D]));
    }
  }
  function y(b) {
    var N = b.changedTouches, _ = N.length, C, A;
    for (C = 0; C < _; ++C)
      (A = r[N[C].identifier]) && (ft(b), A("drag", b, N[C]));
  }
  function S(b) {
    var N = b.changedTouches, _ = N.length, C, A;
    for (u && clearTimeout(u), u = setTimeout(function() {
      u = null;
    }, 500), C = 0; C < _; ++C)
      (A = r[N[C].identifier]) && (Yn(b), A("end", b, N[C]));
  }
  function m(b, N, _, C, A, D) {
    var B = i.copy(), I = De(D || _, N), $, H, v;
    if ((v = n.call(b, new ao("beforestart", {
      sourceEvent: _,
      target: f,
      identifier: A,
      active: s,
      x: I[0],
      y: I[1],
      dx: 0,
      dy: 0,
      dispatch: B
    }), C)) != null)
      return $ = v.x - I[0] || 0, H = v.y - I[1] || 0, function k(E, M, j) {
        var P = I, L;
        switch (E) {
          case "start":
            r[A] = k, L = s++;
            break;
          case "end":
            delete r[A], --s;
          // falls through
          case "drag":
            I = De(j || M, N), L = s;
            break;
        }
        B.call(
          E,
          b,
          new ao(E, {
            sourceEvent: M,
            subject: v,
            target: f,
            identifier: A,
            active: L,
            x: I[0] + $,
            y: I[1] + H,
            dx: I[0] - P[0],
            dy: I[1] - P[1],
            dispatch: B
          }),
          C
        );
      };
  }
  return f.filter = function(b) {
    return arguments.length ? (e = typeof b == "function" ? b : Qt(!!b), f) : e;
  }, f.container = function(b) {
    return arguments.length ? (t = typeof b == "function" ? b : Qt(b), f) : t;
  }, f.subject = function(b) {
    return arguments.length ? (n = typeof b == "function" ? b : Qt(b), f) : n;
  }, f.touchable = function(b) {
    return arguments.length ? (o = typeof b == "function" ? b : Qt(!!b), f) : o;
  }, f.on = function() {
    var b = i.on.apply(i, arguments);
    return b === i ? f : b;
  }, f.clickDistance = function(b) {
    return arguments.length ? (d = (b = +b) * b, f) : Math.sqrt(d);
  }, f;
}
function ko(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function Ai(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function Zt() {
}
var zt = 0.7, gn = 1 / zt, ht = "\\s*([+-]?\\d+)\\s*", Rt = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", Ve = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", Ql = /^#([0-9a-f]{3,8})$/, Jl = new RegExp(`^rgb\\(${ht},${ht},${ht}\\)$`), eu = new RegExp(`^rgb\\(${Ve},${Ve},${Ve}\\)$`), tu = new RegExp(`^rgba\\(${ht},${ht},${ht},${Rt}\\)$`), nu = new RegExp(`^rgba\\(${Ve},${Ve},${Ve},${Rt}\\)$`), ou = new RegExp(`^hsl\\(${Rt},${Ve},${Ve}\\)$`), ru = new RegExp(`^hsla\\(${Rt},${Ve},${Ve},${Rt}\\)$`), rr = {
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
ko(Zt, rt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: ir,
  // Deprecated! Use color.formatHex.
  formatHex: ir,
  formatHex8: iu,
  formatHsl: su,
  formatRgb: sr,
  toString: sr
});
function ir() {
  return this.rgb().formatHex();
}
function iu() {
  return this.rgb().formatHex8();
}
function su() {
  return Di(this).formatHsl();
}
function sr() {
  return this.rgb().formatRgb();
}
function rt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = Ql.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? ar(t) : n === 3 ? new be(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? Jt(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? Jt(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = Jl.exec(e)) ? new be(t[1], t[2], t[3], 1) : (t = eu.exec(e)) ? new be(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = tu.exec(e)) ? Jt(t[1], t[2], t[3], t[4]) : (t = nu.exec(e)) ? Jt(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = ou.exec(e)) ? ur(t[1], t[2] / 100, t[3] / 100, 1) : (t = ru.exec(e)) ? ur(t[1], t[2] / 100, t[3] / 100, t[4]) : rr.hasOwnProperty(e) ? ar(rr[e]) : e === "transparent" ? new be(NaN, NaN, NaN, 0) : null;
}
function ar(e) {
  return new be(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function Jt(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new be(e, t, n, o);
}
function au(e) {
  return e instanceof Zt || (e = rt(e)), e ? (e = e.rgb(), new be(e.r, e.g, e.b, e.opacity)) : new be();
}
function co(e, t, n, o) {
  return arguments.length === 1 ? au(e) : new be(e, t, n, o ?? 1);
}
function be(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
ko(be, co, Ai(Zt, {
  brighter(e) {
    return e = e == null ? gn : Math.pow(gn, e), new be(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? zt : Math.pow(zt, e), new be(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new be(nt(this.r), nt(this.g), nt(this.b), mn(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: cr,
  // Deprecated! Use color.formatHex.
  formatHex: cr,
  formatHex8: cu,
  formatRgb: lr,
  toString: lr
}));
function cr() {
  return `#${tt(this.r)}${tt(this.g)}${tt(this.b)}`;
}
function cu() {
  return `#${tt(this.r)}${tt(this.g)}${tt(this.b)}${tt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function lr() {
  const e = mn(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${nt(this.r)}, ${nt(this.g)}, ${nt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function mn(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function nt(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function tt(e) {
  return e = nt(e), (e < 16 ? "0" : "") + e.toString(16);
}
function ur(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Pe(e, t, n, o);
}
function Di(e) {
  if (e instanceof Pe) return new Pe(e.h, e.s, e.l, e.opacity);
  if (e instanceof Zt || (e = rt(e)), !e) return new Pe();
  if (e instanceof Pe) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), i = Math.max(t, n, o), s = NaN, a = i - r, l = (i + r) / 2;
  return a ? (t === i ? s = (n - o) / a + (n < o) * 6 : n === i ? s = (o - t) / a + 2 : s = (t - n) / a + 4, a /= l < 0.5 ? i + r : 2 - i - r, s *= 60) : a = l > 0 && l < 1 ? 0 : s, new Pe(s, a, l, e.opacity);
}
function lu(e, t, n, o) {
  return arguments.length === 1 ? Di(e) : new Pe(e, t, n, o ?? 1);
}
function Pe(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
ko(Pe, lu, Ai(Zt, {
  brighter(e) {
    return e = e == null ? gn : Math.pow(gn, e), new Pe(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? zt : Math.pow(zt, e), new Pe(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new be(
      Wn(e >= 240 ? e - 240 : e + 120, r, o),
      Wn(e, r, o),
      Wn(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new Pe(dr(this.h), en(this.s), en(this.l), mn(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = mn(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${dr(this.h)}, ${en(this.s) * 100}%, ${en(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function dr(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function en(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function Wn(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const Io = (e) => () => e;
function uu(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function du(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function fu(e) {
  return (e = +e) == 1 ? Pi : function(t, n) {
    return n - t ? du(t, n, e) : Io(isNaN(t) ? n : t);
  };
}
function Pi(e, t) {
  var n = t - e;
  return n ? uu(e, n) : Io(isNaN(e) ? t : e);
}
const yn = (function e(t) {
  var n = fu(t);
  function o(r, i) {
    var s = n((r = co(r)).r, (i = co(i)).r), a = n(r.g, i.g), l = n(r.b, i.b), c = Pi(r.opacity, i.opacity);
    return function(u) {
      return r.r = s(u), r.g = a(u), r.b = l(u), r.opacity = c(u), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function hu(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(i) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - i) + t[r] * i;
    return o;
  };
}
function pu(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function gu(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), i = new Array(n), s;
  for (s = 0; s < o; ++s) r[s] = jt(e[s], t[s]);
  for (; s < n; ++s) i[s] = t[s];
  return function(a) {
    for (s = 0; s < o; ++s) i[s] = r[s](a);
    return i;
  };
}
function mu(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(o) {
    return n.setTime(e * (1 - o) + t * o), n;
  };
}
function He(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function yu(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = jt(e[r], t[r]) : o[r] = t[r];
  return function(i) {
    for (r in n) o[r] = n[r](i);
    return o;
  };
}
var lo = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, Xn = new RegExp(lo.source, "g");
function xu(e) {
  return function() {
    return e;
  };
}
function wu(e) {
  return function(t) {
    return e(t) + "";
  };
}
function ji(e, t) {
  var n = lo.lastIndex = Xn.lastIndex = 0, o, r, i, s = -1, a = [], l = [];
  for (e = e + "", t = t + ""; (o = lo.exec(e)) && (r = Xn.exec(t)); )
    (i = r.index) > n && (i = t.slice(n, i), a[s] ? a[s] += i : a[++s] = i), (o = o[0]) === (r = r[0]) ? a[s] ? a[s] += r : a[++s] = r : (a[++s] = null, l.push({ i: s, x: He(o, r) })), n = Xn.lastIndex;
  return n < t.length && (i = t.slice(n), a[s] ? a[s] += i : a[++s] = i), a.length < 2 ? l[0] ? wu(l[0].x) : xu(t) : (t = l.length, function(c) {
    for (var u = 0, d; u < t; ++u) a[(d = l[u]).i] = d.x(c);
    return a.join("");
  });
}
function jt(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? Io(t) : (n === "number" ? He : n === "string" ? (o = rt(t)) ? (t = o, yn) : ji : t instanceof rt ? yn : t instanceof Date ? mu : pu(t) ? hu : Array.isArray(t) ? gu : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? yu : He)(e, t);
}
var fr = 180 / Math.PI, uo = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function $i(e, t, n, o, r, i) {
  var s, a, l;
  return (s = Math.sqrt(e * e + t * t)) && (e /= s, t /= s), (l = e * n + t * o) && (n -= e * l, o -= t * l), (a = Math.sqrt(n * n + o * o)) && (n /= a, o /= a, l /= a), e * o < t * n && (e = -e, t = -t, l = -l, s = -s), {
    translateX: r,
    translateY: i,
    rotate: Math.atan2(t, e) * fr,
    skewX: Math.atan(l) * fr,
    scaleX: s,
    scaleY: a
  };
}
var tn;
function vu(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? uo : $i(t.a, t.b, t.c, t.d, t.e, t.f);
}
function bu(e) {
  return e == null || (tn || (tn = document.createElementNS("http://www.w3.org/2000/svg", "g")), tn.setAttribute("transform", e), !(e = tn.transform.baseVal.consolidate())) ? uo : (e = e.matrix, $i(e.a, e.b, e.c, e.d, e.e, e.f));
}
function Ti(e, t, n, o) {
  function r(c) {
    return c.length ? c.pop() + " " : "";
  }
  function i(c, u, d, f, p, g) {
    if (c !== d || u !== f) {
      var x = p.push("translate(", null, t, null, n);
      g.push({ i: x - 4, x: He(c, d) }, { i: x - 2, x: He(u, f) });
    } else (d || f) && p.push("translate(" + d + t + f + n);
  }
  function s(c, u, d, f) {
    c !== u ? (c - u > 180 ? u += 360 : u - c > 180 && (c += 360), f.push({ i: d.push(r(d) + "rotate(", null, o) - 2, x: He(c, u) })) : u && d.push(r(d) + "rotate(" + u + o);
  }
  function a(c, u, d, f) {
    c !== u ? f.push({ i: d.push(r(d) + "skewX(", null, o) - 2, x: He(c, u) }) : u && d.push(r(d) + "skewX(" + u + o);
  }
  function l(c, u, d, f, p, g) {
    if (c !== d || u !== f) {
      var x = p.push(r(p) + "scale(", null, ",", null, ")");
      g.push({ i: x - 4, x: He(c, d) }, { i: x - 2, x: He(u, f) });
    } else (d !== 1 || f !== 1) && p.push(r(p) + "scale(" + d + "," + f + ")");
  }
  return function(c, u) {
    var d = [], f = [];
    return c = e(c), u = e(u), i(c.translateX, c.translateY, u.translateX, u.translateY, d, f), s(c.rotate, u.rotate, d, f), a(c.skewX, u.skewX, d, f), l(c.scaleX, c.scaleY, u.scaleX, u.scaleY, d, f), c = u = null, function(p) {
      for (var g = -1, x = f.length, w; ++g < x; ) d[(w = f[g]).i] = w.x(p);
      return d.join("");
    };
  };
}
var Su = Ti(vu, "px, ", "px)", "deg)"), _u = Ti(bu, ", ", ")", ")"), Eu = 1e-12;
function hr(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Nu(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Cu(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const ln = (function e(t, n, o) {
  function r(i, s) {
    var a = i[0], l = i[1], c = i[2], u = s[0], d = s[1], f = s[2], p = u - a, g = d - l, x = p * p + g * g, w, y;
    if (x < Eu)
      y = Math.log(f / c) / t, w = function(C) {
        return [
          a + C * p,
          l + C * g,
          c * Math.exp(t * C * y)
        ];
      };
    else {
      var S = Math.sqrt(x), m = (f * f - c * c + o * x) / (2 * c * n * S), b = (f * f - c * c - o * x) / (2 * f * n * S), N = Math.log(Math.sqrt(m * m + 1) - m), _ = Math.log(Math.sqrt(b * b + 1) - b);
      y = (_ - N) / t, w = function(C) {
        var A = C * y, D = hr(N), B = c / (n * S) * (D * Cu(t * A + N) - Nu(N));
        return [
          a + B * p,
          l + B * g,
          c * D / hr(t * A + N)
        ];
      };
    }
    return w.duration = y * 1e3 * t / Math.SQRT2, w;
  }
  return r.rho = function(i) {
    var s = Math.max(1e-3, +i), a = s * s, l = a * a;
    return e(s, a, l);
  }, r;
})(Math.SQRT2, 2, 4);
var gt = 0, Dt = 0, It = 0, zi = 1e3, xn, Pt, wn = 0, it = 0, Mn = 0, Lt = typeof performance == "object" && performance.now ? performance : Date, Ri = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function Mo() {
  return it || (Ri(ku), it = Lt.now() + Mn);
}
function ku() {
  it = 0;
}
function vn() {
  this._call = this._time = this._next = null;
}
vn.prototype = Li.prototype = {
  constructor: vn,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? Mo() : +n) + (t == null ? 0 : +t), !this._next && Pt !== this && (Pt ? Pt._next = this : xn = this, Pt = this), this._call = e, this._time = n, fo();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, fo());
  }
};
function Li(e, t, n) {
  var o = new vn();
  return o.restart(e, t, n), o;
}
function Iu() {
  Mo(), ++gt;
  for (var e = xn, t; e; )
    (t = it - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --gt;
}
function pr() {
  it = (wn = Lt.now()) + Mn, gt = Dt = 0;
  try {
    Iu();
  } finally {
    gt = 0, Au(), it = 0;
  }
}
function Mu() {
  var e = Lt.now(), t = e - wn;
  t > zi && (Mn -= t, wn = e);
}
function Au() {
  for (var e, t = xn, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : xn = n);
  Pt = e, fo(o);
}
function fo(e) {
  if (!gt) {
    Dt && (Dt = clearTimeout(Dt));
    var t = e - it;
    t > 24 ? (e < 1 / 0 && (Dt = setTimeout(pr, e - Lt.now() - Mn)), It && (It = clearInterval(It))) : (It || (wn = Lt.now(), It = setInterval(Mu, zi)), gt = 1, Ri(pr));
  }
}
function gr(e, t, n) {
  var o = new vn();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var Du = kn("start", "end", "cancel", "interrupt"), Pu = [], Hi = 0, mr = 1, ho = 2, un = 3, yr = 4, po = 5, dn = 6;
function An(e, t, n, o, r, i) {
  var s = e.__transition;
  if (!s) e.__transition = {};
  else if (n in s) return;
  ju(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: Du,
    tween: Pu,
    time: i.time,
    delay: i.delay,
    duration: i.duration,
    ease: i.ease,
    timer: null,
    state: Hi
  });
}
function Ao(e, t) {
  var n = Re(e, t);
  if (n.state > Hi) throw new Error("too late; already scheduled");
  return n;
}
function Oe(e, t) {
  var n = Re(e, t);
  if (n.state > un) throw new Error("too late; already running");
  return n;
}
function Re(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function ju(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = Li(i, 0, n.time);
  function i(c) {
    n.state = mr, n.timer.restart(s, n.delay, n.time), n.delay <= c && s(c - n.delay);
  }
  function s(c) {
    var u, d, f, p;
    if (n.state !== mr) return l();
    for (u in o)
      if (p = o[u], p.name === n.name) {
        if (p.state === un) return gr(s);
        p.state === yr ? (p.state = dn, p.timer.stop(), p.on.call("interrupt", e, e.__data__, p.index, p.group), delete o[u]) : +u < t && (p.state = dn, p.timer.stop(), p.on.call("cancel", e, e.__data__, p.index, p.group), delete o[u]);
      }
    if (gr(function() {
      n.state === un && (n.state = yr, n.timer.restart(a, n.delay, n.time), a(c));
    }), n.state = ho, n.on.call("start", e, e.__data__, n.index, n.group), n.state === ho) {
      for (n.state = un, r = new Array(f = n.tween.length), u = 0, d = -1; u < f; ++u)
        (p = n.tween[u].value.call(e, e.__data__, n.index, n.group)) && (r[++d] = p);
      r.length = d + 1;
    }
  }
  function a(c) {
    for (var u = c < n.duration ? n.ease.call(null, c / n.duration) : (n.timer.restart(l), n.state = po, 1), d = -1, f = r.length; ++d < f; )
      r[d].call(e, u);
    n.state === po && (n.on.call("end", e, e.__data__, n.index, n.group), l());
  }
  function l() {
    n.state = dn, n.timer.stop(), delete o[t];
    for (var c in o) return;
    delete e.__transition;
  }
}
function fn(e, t) {
  var n = e.__transition, o, r, i = !0, s;
  if (n) {
    t = t == null ? null : t + "";
    for (s in n) {
      if ((o = n[s]).name !== t) {
        i = !1;
        continue;
      }
      r = o.state > ho && o.state < po, o.state = dn, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[s];
    }
    i && delete e.__transition;
  }
}
function $u(e) {
  return this.each(function() {
    fn(this, e);
  });
}
function Tu(e, t) {
  var n, o;
  return function() {
    var r = Oe(this, e), i = r.tween;
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
function zu(e, t, n) {
  var o, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var i = Oe(this, e), s = i.tween;
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
function Ru(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = Re(this.node(), n).tween, r = 0, i = o.length, s; r < i; ++r)
      if ((s = o[r]).name === e)
        return s.value;
    return null;
  }
  return this.each((t == null ? Tu : zu)(n, e, t));
}
function Do(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = Oe(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return Re(r, o).value[t];
  };
}
function Vi(e, t) {
  var n;
  return (typeof t == "number" ? He : t instanceof rt ? yn : (n = rt(t)) ? (t = n, yn) : ji)(e, t);
}
function Lu(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Hu(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Vu(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttribute(e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Ou(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttributeNS(e.space, e.local);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Bu(e, t, n) {
  var o, r, i;
  return function() {
    var s, a = n(this), l;
    return a == null ? void this.removeAttribute(e) : (s = this.getAttribute(e), l = a + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a)));
  };
}
function Fu(e, t, n) {
  var o, r, i;
  return function() {
    var s, a = n(this), l;
    return a == null ? void this.removeAttributeNS(e.space, e.local) : (s = this.getAttributeNS(e.space, e.local), l = a + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a)));
  };
}
function Yu(e, t) {
  var n = In(e), o = n === "transform" ? _u : Vi;
  return this.attrTween(e, typeof t == "function" ? (n.local ? Fu : Bu)(n, o, Do(this, "attr." + e, t)) : t == null ? (n.local ? Hu : Lu)(n) : (n.local ? Ou : Vu)(n, o, t));
}
function Wu(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function Xu(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function qu(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && Xu(e, i)), n;
  }
  return r._value = t, r;
}
function Zu(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && Wu(e, i)), n;
  }
  return r._value = t, r;
}
function Uu(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = In(e);
  return this.tween(n, (o.local ? qu : Zu)(o, t));
}
function Gu(e, t) {
  return function() {
    Ao(this, e).delay = +t.apply(this, arguments);
  };
}
function Ku(e, t) {
  return t = +t, function() {
    Ao(this, e).delay = t;
  };
}
function Qu(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Gu : Ku)(t, e)) : Re(this.node(), t).delay;
}
function Ju(e, t) {
  return function() {
    Oe(this, e).duration = +t.apply(this, arguments);
  };
}
function ed(e, t) {
  return t = +t, function() {
    Oe(this, e).duration = t;
  };
}
function td(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Ju : ed)(t, e)) : Re(this.node(), t).duration;
}
function nd(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    Oe(this, e).ease = t;
  };
}
function od(e) {
  var t = this._id;
  return arguments.length ? this.each(nd(t, e)) : Re(this.node(), t).ease;
}
function rd(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    Oe(this, e).ease = n;
  };
}
function id(e) {
  if (typeof e != "function") throw new Error();
  return this.each(rd(this._id, e));
}
function sd(e) {
  typeof e != "function" && (e = yi(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = [], l, c = 0; c < s; ++c)
      (l = i[c]) && e.call(l, l.__data__, c, i) && a.push(l);
  return new qe(o, this._parents, this._name, this._id);
}
function ad(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, i = Math.min(o, r), s = new Array(o), a = 0; a < i; ++a)
    for (var l = t[a], c = n[a], u = l.length, d = s[a] = new Array(u), f, p = 0; p < u; ++p)
      (f = l[p] || c[p]) && (d[p] = f);
  for (; a < o; ++a)
    s[a] = t[a];
  return new qe(s, this._parents, this._name, this._id);
}
function cd(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function ld(e, t, n) {
  var o, r, i = cd(t) ? Ao : Oe;
  return function() {
    var s = i(this, e), a = s.on;
    a !== o && (r = (o = a).copy()).on(t, n), s.on = r;
  };
}
function ud(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Re(this.node(), n).on.on(e) : this.each(ld(n, e, t));
}
function dd(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function fd() {
  return this.on("end.remove", dd(this._id));
}
function hd(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = No(e));
  for (var o = this._groups, r = o.length, i = new Array(r), s = 0; s < r; ++s)
    for (var a = o[s], l = a.length, c = i[s] = new Array(l), u, d, f = 0; f < l; ++f)
      (u = a[f]) && (d = e.call(u, u.__data__, f, a)) && ("__data__" in u && (d.__data__ = u.__data__), c[f] = d, An(c[f], t, n, f, c, Re(u, n)));
  return new qe(i, this._parents, t, n);
}
function pd(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = mi(e));
  for (var o = this._groups, r = o.length, i = [], s = [], a = 0; a < r; ++a)
    for (var l = o[a], c = l.length, u, d = 0; d < c; ++d)
      if (u = l[d]) {
        for (var f = e.call(u, u.__data__, d, l), p, g = Re(u, n), x = 0, w = f.length; x < w; ++x)
          (p = f[x]) && An(p, t, n, x, f, g);
        i.push(f), s.push(u);
      }
  return new qe(i, s, t, n);
}
var gd = qt.prototype.constructor;
function md() {
  return new gd(this._groups, this._parents);
}
function yd(e, t) {
  var n, o, r;
  return function() {
    var i = pt(this, e), s = (this.style.removeProperty(e), pt(this, e));
    return i === s ? null : i === n && s === o ? r : r = t(n = i, o = s);
  };
}
function Oi(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function xd(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = pt(this, e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function wd(e, t, n) {
  var o, r, i;
  return function() {
    var s = pt(this, e), a = n(this), l = a + "";
    return a == null && (l = a = (this.style.removeProperty(e), pt(this, e))), s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a));
  };
}
function vd(e, t) {
  var n, o, r, i = "style." + t, s = "end." + i, a;
  return function() {
    var l = Oe(this, e), c = l.on, u = l.value[i] == null ? a || (a = Oi(t)) : void 0;
    (c !== n || r !== u) && (o = (n = c).copy()).on(s, r = u), l.on = o;
  };
}
function bd(e, t, n) {
  var o = (e += "") == "transform" ? Su : Vi;
  return t == null ? this.styleTween(e, yd(e, o)).on("end.style." + e, Oi(e)) : typeof t == "function" ? this.styleTween(e, wd(e, o, Do(this, "style." + e, t))).each(vd(this._id, e)) : this.styleTween(e, xd(e, o, t), n).on("end.style." + e, null);
}
function Sd(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function _d(e, t, n) {
  var o, r;
  function i() {
    var s = t.apply(this, arguments);
    return s !== r && (o = (r = s) && Sd(e, s, n)), o;
  }
  return i._value = t, i;
}
function Ed(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, _d(e, t, n ?? ""));
}
function Nd(e) {
  return function() {
    this.textContent = e;
  };
}
function Cd(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function kd(e) {
  return this.tween("text", typeof e == "function" ? Cd(Do(this, "text", e)) : Nd(e == null ? "" : e + ""));
}
function Id(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Md(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Id(r)), t;
  }
  return o._value = e, o;
}
function Ad(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, Md(e));
}
function Dd() {
  for (var e = this._name, t = this._id, n = Bi(), o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], a = s.length, l, c = 0; c < a; ++c)
      if (l = s[c]) {
        var u = Re(l, t);
        An(l, e, n, c, s, {
          time: u.time + u.delay + u.duration,
          delay: 0,
          duration: u.duration,
          ease: u.ease
        });
      }
  return new qe(o, this._parents, e, n);
}
function Pd() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(i, s) {
    var a = { value: s }, l = { value: function() {
      --r === 0 && i();
    } };
    n.each(function() {
      var c = Oe(this, o), u = c.on;
      u !== e && (t = (e = u).copy(), t._.cancel.push(a), t._.interrupt.push(a), t._.end.push(l)), c.on = t;
    }), r === 0 && i();
  });
}
var jd = 0;
function qe(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function Bi() {
  return ++jd;
}
var Ye = qt.prototype;
qe.prototype = {
  constructor: qe,
  select: hd,
  selectAll: pd,
  selectChild: Ye.selectChild,
  selectChildren: Ye.selectChildren,
  filter: sd,
  merge: ad,
  selection: md,
  transition: Dd,
  call: Ye.call,
  nodes: Ye.nodes,
  node: Ye.node,
  size: Ye.size,
  empty: Ye.empty,
  each: Ye.each,
  on: ud,
  attr: Yu,
  attrTween: Uu,
  style: bd,
  styleTween: Ed,
  text: kd,
  textTween: Ad,
  remove: fd,
  tween: Ru,
  delay: Qu,
  duration: td,
  ease: od,
  easeVarying: id,
  end: Pd,
  [Symbol.iterator]: Ye[Symbol.iterator]
};
function $d(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var Td = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: $d
};
function zd(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function Rd(e) {
  var t, n;
  e instanceof qe ? (t = e._id, e = e._name) : (t = Bi(), (n = Td).time = Mo(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], a = s.length, l, c = 0; c < a; ++c)
      (l = s[c]) && An(l, e, t, c, s, n || zd(l, t));
  return new qe(o, this._parents, e, t);
}
qt.prototype.interrupt = $u;
qt.prototype.transition = Rd;
const nn = (e) => () => e;
function Ld(e, {
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
function We(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
We.prototype = {
  constructor: We,
  scale: function(e) {
    return e === 1 ? this : new We(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new We(this.k, this.x + this.k * e, this.y + this.k * t);
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
var Dn = new We(1, 0, 0);
Fi.prototype = We.prototype;
function Fi(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return Dn;
  return e.__zoom;
}
function qn(e) {
  e.stopImmediatePropagation();
}
function Mt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Hd(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function Vd() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function xr() {
  return this.__zoom || Dn;
}
function Od(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function Bd() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Fd(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], i = e.invertY(t[0][1]) - n[0][1], s = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    s > i ? (i + s) / 2 : Math.min(0, i) || Math.max(0, s)
  );
}
function Yi() {
  var e = Hd, t = Vd, n = Fd, o = Od, r = Bd, i = [0, 1 / 0], s = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], a = 250, l = ln, c = kn("start", "zoom", "end"), u, d, f, p = 500, g = 150, x = 0, w = 10;
  function y(v) {
    v.property("__zoom", xr).on("wheel.zoom", A, { passive: !1 }).on("mousedown.zoom", D).on("dblclick.zoom", B).filter(r).on("touchstart.zoom", I).on("touchmove.zoom", $).on("touchend.zoom touchcancel.zoom", H).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  y.transform = function(v, k, E, M) {
    var j = v.selection ? v.selection() : v;
    j.property("__zoom", xr), v !== j ? N(v, k, E, M) : j.interrupt().each(function() {
      _(this, arguments).event(M).start().zoom(null, typeof k == "function" ? k.apply(this, arguments) : k).end();
    });
  }, y.scaleBy = function(v, k, E, M) {
    y.scaleTo(v, function() {
      var j = this.__zoom.k, P = typeof k == "function" ? k.apply(this, arguments) : k;
      return j * P;
    }, E, M);
  }, y.scaleTo = function(v, k, E, M) {
    y.transform(v, function() {
      var j = t.apply(this, arguments), P = this.__zoom, L = E == null ? b(j) : typeof E == "function" ? E.apply(this, arguments) : E, O = P.invert(L), V = typeof k == "function" ? k.apply(this, arguments) : k;
      return n(m(S(P, V), L, O), j, s);
    }, E, M);
  }, y.translateBy = function(v, k, E, M) {
    y.transform(v, function() {
      return n(this.__zoom.translate(
        typeof k == "function" ? k.apply(this, arguments) : k,
        typeof E == "function" ? E.apply(this, arguments) : E
      ), t.apply(this, arguments), s);
    }, null, M);
  }, y.translateTo = function(v, k, E, M, j) {
    y.transform(v, function() {
      var P = t.apply(this, arguments), L = this.__zoom, O = M == null ? b(P) : typeof M == "function" ? M.apply(this, arguments) : M;
      return n(Dn.translate(O[0], O[1]).scale(L.k).translate(
        typeof k == "function" ? -k.apply(this, arguments) : -k,
        typeof E == "function" ? -E.apply(this, arguments) : -E
      ), P, s);
    }, M, j);
  };
  function S(v, k) {
    return k = Math.max(i[0], Math.min(i[1], k)), k === v.k ? v : new We(k, v.x, v.y);
  }
  function m(v, k, E) {
    var M = k[0] - E[0] * v.k, j = k[1] - E[1] * v.k;
    return M === v.x && j === v.y ? v : new We(v.k, M, j);
  }
  function b(v) {
    return [(+v[0][0] + +v[1][0]) / 2, (+v[0][1] + +v[1][1]) / 2];
  }
  function N(v, k, E, M) {
    v.on("start.zoom", function() {
      _(this, arguments).event(M).start();
    }).on("interrupt.zoom end.zoom", function() {
      _(this, arguments).event(M).end();
    }).tween("zoom", function() {
      var j = this, P = arguments, L = _(j, P).event(M), O = t.apply(j, P), V = E == null ? b(O) : typeof E == "function" ? E.apply(j, P) : E, Y = Math.max(O[1][0] - O[0][0], O[1][1] - O[0][1]), X = j.__zoom, Q = typeof k == "function" ? k.apply(j, P) : k, ne = l(X.invert(V).concat(Y / X.k), Q.invert(V).concat(Y / Q.k));
      return function(Z) {
        if (Z === 1) Z = Q;
        else {
          var z = ne(Z), W = Y / z[2];
          Z = new We(W, V[0] - z[0] * W, V[1] - z[1] * W);
        }
        L.zoom(null, Z);
      };
    });
  }
  function _(v, k, E) {
    return !E && v.__zooming || new C(v, k);
  }
  function C(v, k) {
    this.that = v, this.args = k, this.active = 0, this.sourceEvent = null, this.extent = t.apply(v, k), this.taps = 0;
  }
  C.prototype = {
    event: function(v) {
      return v && (this.sourceEvent = v), this;
    },
    start: function() {
      return ++this.active === 1 && (this.that.__zooming = this, this.emit("start")), this;
    },
    zoom: function(v, k) {
      return this.mouse && v !== "mouse" && (this.mouse[1] = k.invert(this.mouse[0])), this.touch0 && v !== "touch" && (this.touch0[1] = k.invert(this.touch0[0])), this.touch1 && v !== "touch" && (this.touch1[1] = k.invert(this.touch1[0])), this.that.__zoom = k, this.emit("zoom"), this;
    },
    end: function() {
      return --this.active === 0 && (delete this.that.__zooming, this.emit("end")), this;
    },
    emit: function(v) {
      var k = _e(this.that).datum();
      c.call(
        v,
        this.that,
        new Ld(v, {
          sourceEvent: this.sourceEvent,
          target: y,
          transform: this.that.__zoom,
          dispatch: c
        }),
        k
      );
    }
  };
  function A(v, ...k) {
    if (!e.apply(this, arguments)) return;
    var E = _(this, k).event(v), M = this.__zoom, j = Math.max(i[0], Math.min(i[1], M.k * Math.pow(2, o.apply(this, arguments)))), P = De(v);
    if (E.wheel)
      (E.mouse[0][0] !== P[0] || E.mouse[0][1] !== P[1]) && (E.mouse[1] = M.invert(E.mouse[0] = P)), clearTimeout(E.wheel);
    else {
      if (M.k === j) return;
      E.mouse = [P, M.invert(P)], fn(this), E.start();
    }
    Mt(v), E.wheel = setTimeout(L, g), E.zoom("mouse", n(m(S(M, j), E.mouse[0], E.mouse[1]), E.extent, s));
    function L() {
      E.wheel = null, E.end();
    }
  }
  function D(v, ...k) {
    if (f || !e.apply(this, arguments)) return;
    var E = v.currentTarget, M = _(this, k, !0).event(v), j = _e(v.view).on("mousemove.zoom", V, !0).on("mouseup.zoom", Y, !0), P = De(v, E), L = v.clientX, O = v.clientY;
    ki(v.view), qn(v), M.mouse = [P, this.__zoom.invert(P)], fn(this), M.start();
    function V(X) {
      if (Mt(X), !M.moved) {
        var Q = X.clientX - L, ne = X.clientY - O;
        M.moved = Q * Q + ne * ne > x;
      }
      M.event(X).zoom("mouse", n(m(M.that.__zoom, M.mouse[0] = De(X, E), M.mouse[1]), M.extent, s));
    }
    function Y(X) {
      j.on("mousemove.zoom mouseup.zoom", null), Ii(X.view, M.moved), Mt(X), M.event(X).end();
    }
  }
  function B(v, ...k) {
    if (e.apply(this, arguments)) {
      var E = this.__zoom, M = De(v.changedTouches ? v.changedTouches[0] : v, this), j = E.invert(M), P = E.k * (v.shiftKey ? 0.5 : 2), L = n(m(S(E, P), M, j), t.apply(this, k), s);
      Mt(v), a > 0 ? _e(this).transition().duration(a).call(N, L, M, v) : _e(this).call(y.transform, L, M, v);
    }
  }
  function I(v, ...k) {
    if (e.apply(this, arguments)) {
      var E = v.touches, M = E.length, j = _(this, k, v.changedTouches.length === M).event(v), P, L, O, V;
      for (qn(v), L = 0; L < M; ++L)
        O = E[L], V = De(O, this), V = [V, this.__zoom.invert(V), O.identifier], j.touch0 ? !j.touch1 && j.touch0[2] !== V[2] && (j.touch1 = V, j.taps = 0) : (j.touch0 = V, P = !0, j.taps = 1 + !!u);
      u && (u = clearTimeout(u)), P && (j.taps < 2 && (d = V[0], u = setTimeout(function() {
        u = null;
      }, p)), fn(this), j.start());
    }
  }
  function $(v, ...k) {
    if (this.__zooming) {
      var E = _(this, k).event(v), M = v.changedTouches, j = M.length, P, L, O, V;
      for (Mt(v), P = 0; P < j; ++P)
        L = M[P], O = De(L, this), E.touch0 && E.touch0[2] === L.identifier ? E.touch0[0] = O : E.touch1 && E.touch1[2] === L.identifier && (E.touch1[0] = O);
      if (L = E.that.__zoom, E.touch1) {
        var Y = E.touch0[0], X = E.touch0[1], Q = E.touch1[0], ne = E.touch1[1], Z = (Z = Q[0] - Y[0]) * Z + (Z = Q[1] - Y[1]) * Z, z = (z = ne[0] - X[0]) * z + (z = ne[1] - X[1]) * z;
        L = S(L, Math.sqrt(Z / z)), O = [(Y[0] + Q[0]) / 2, (Y[1] + Q[1]) / 2], V = [(X[0] + ne[0]) / 2, (X[1] + ne[1]) / 2];
      } else if (E.touch0) O = E.touch0[0], V = E.touch0[1];
      else return;
      E.zoom("touch", n(m(L, O, V), E.extent, s));
    }
  }
  function H(v, ...k) {
    if (this.__zooming) {
      var E = _(this, k).event(v), M = v.changedTouches, j = M.length, P, L;
      for (qn(v), f && clearTimeout(f), f = setTimeout(function() {
        f = null;
      }, p), P = 0; P < j; ++P)
        L = M[P], E.touch0 && E.touch0[2] === L.identifier ? delete E.touch0 : E.touch1 && E.touch1[2] === L.identifier && delete E.touch1;
      if (E.touch1 && !E.touch0 && (E.touch0 = E.touch1, delete E.touch1), E.touch0) E.touch0[1] = this.__zoom.invert(E.touch0[0]);
      else if (E.end(), E.taps === 2 && (L = De(L, this), Math.hypot(d[0] - L[0], d[1] - L[1]) < w)) {
        var O = _e(this).on("dblclick.zoom");
        O && O.apply(this, arguments);
      }
    }
  }
  return y.wheelDelta = function(v) {
    return arguments.length ? (o = typeof v == "function" ? v : nn(+v), y) : o;
  }, y.filter = function(v) {
    return arguments.length ? (e = typeof v == "function" ? v : nn(!!v), y) : e;
  }, y.touchable = function(v) {
    return arguments.length ? (r = typeof v == "function" ? v : nn(!!v), y) : r;
  }, y.extent = function(v) {
    return arguments.length ? (t = typeof v == "function" ? v : nn([[+v[0][0], +v[0][1]], [+v[1][0], +v[1][1]]]), y) : t;
  }, y.scaleExtent = function(v) {
    return arguments.length ? (i[0] = +v[0], i[1] = +v[1], y) : [i[0], i[1]];
  }, y.translateExtent = function(v) {
    return arguments.length ? (s[0][0] = +v[0][0], s[1][0] = +v[1][0], s[0][1] = +v[0][1], s[1][1] = +v[1][1], y) : [[s[0][0], s[0][1]], [s[1][0], s[1][1]]];
  }, y.constrain = function(v) {
    return arguments.length ? (n = v, y) : n;
  }, y.duration = function(v) {
    return arguments.length ? (a = +v, y) : a;
  }, y.interpolate = function(v) {
    return arguments.length ? (l = v, y) : l;
  }, y.on = function() {
    var v = c.on.apply(c, arguments);
    return v === c ? y : v;
  }, y.clickDistance = function(v) {
    return arguments.length ? (x = (v = +v) * v, y) : Math.sqrt(x);
  }, y.tapDistance = function(v) {
    return arguments.length ? (w = +v, y) : w;
  }, y;
}
const ze = {
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
}, Ht = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], Wi = ["Enter", " ", "Escape"], Xi = {
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
var mt;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(mt || (mt = {}));
var ot;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(ot || (ot = {}));
var Vt;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Vt || (Vt = {}));
const qi = {
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
var Ke;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(Ke || (Ke = {}));
var bn;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(bn || (bn = {}));
var K;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(K || (K = {}));
const wr = {
  [K.Left]: K.Right,
  [K.Right]: K.Left,
  [K.Top]: K.Bottom,
  [K.Bottom]: K.Top
};
function Zi(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Ui = (e) => "id" in e && "source" in e && "target" in e, Yd = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), Po = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), Ut = (e, t = [0, 0]) => {
  const { width: n, height: o } = Ze(e), r = e.origin ?? t, i = n * r[0], s = o * r[1];
  return {
    x: e.position.x - i,
    y: e.position.y - s
  };
}, Wd = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const i = typeof r == "string";
    let s = !t.nodeLookup && !i ? r : void 0;
    t.nodeLookup && (s = i ? t.nodeLookup.get(r) : Po(r) ? r : t.nodeLookup.get(r.id));
    const a = s ? Sn(s, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return Pn(o, a);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return jn(n);
}, Gt = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = Pn(n, Sn(r)), o = !0);
  }), o ? jn(n) : { x: 0, y: 0, width: 0, height: 0 };
}, jo = (e, t, [n, o, r] = [0, 0, 1], i = !1, s = !1) => {
  const a = {
    ...Nt(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, l = [];
  for (const c of e.values()) {
    const { measured: u, selectable: d = !0, hidden: f = !1 } = c;
    if (s && !d || f)
      continue;
    const p = u.width ?? c.width ?? c.initialWidth ?? null, g = u.height ?? c.height ?? c.initialHeight ?? null, x = Ot(a, xt(c)), w = (p ?? 0) * (g ?? 0), y = i && x > 0;
    (!c.internals.handleBounds || y || x >= w || c.dragging) && l.push(c);
  }
  return l;
}, Xd = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function qd(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function Zd({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: i }, s) {
  if (e.size === 0)
    return !0;
  const a = qd(e, s), l = Gt(a), c = To(l, t, n, s?.minZoom ?? r, s?.maxZoom ?? i, s?.padding ?? 0.1);
  return await o.setViewport(c, {
    duration: s?.duration,
    ease: s?.ease,
    interpolate: s?.interpolate
  }), !0;
}
function Gi({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: i }) {
  const s = n.get(e), a = s.parentId ? n.get(s.parentId) : void 0, { x: l, y: c } = a ? a.internals.positionAbsolute : { x: 0, y: 0 }, u = s.origin ?? o;
  let d = s.extent || r;
  if (s.extent === "parent" && !s.expandParent)
    if (!a)
      i?.("005", ze.error005());
    else {
      const p = a.measured.width, g = a.measured.height;
      p && g && (d = [
        [l, c],
        [l + p, c + g]
      ]);
    }
  else a && at(s.extent) && (d = [
    [s.extent[0][0] + l, s.extent[0][1] + c],
    [s.extent[1][0] + l, s.extent[1][1] + c]
  ]);
  const f = at(d) ? st(t, d, s.measured) : t;
  return (s.measured.width === void 0 || s.measured.height === void 0) && i?.("015", ze.error015()), {
    position: {
      x: f.x - l + (s.measured.width ?? 0) * u[0],
      y: f.y - c + (s.measured.height ?? 0) * u[1]
    },
    positionAbsolute: f
  };
}
async function Ud({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const i = new Set(e.map((f) => f.id)), s = [];
  for (const f of n) {
    if (f.deletable === !1)
      continue;
    const p = i.has(f.id), g = !p && f.parentId && s.find((x) => x.id === f.parentId);
    (p || g) && s.push(f);
  }
  const a = new Set(t.map((f) => f.id)), l = o.filter((f) => f.deletable !== !1), u = Xd(s, l);
  for (const f of l)
    a.has(f.id) && !u.find((g) => g.id === f.id) && u.push(f);
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
const yt = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), st = (e = { x: 0, y: 0 }, t, n) => ({
  x: yt(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: yt(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Ki(e, t, n) {
  const { width: o, height: r } = Ze(n), { x: i, y: s } = n.internals.positionAbsolute;
  return st(e, [
    [i, s],
    [i + o, s + r]
  ], t);
}
const vr = (e, t, n) => e < t ? yt(Math.abs(e - t), 1, t) / t : e > n ? -yt(Math.abs(e - n), 1, t) / t : 0, $o = (e, t, n = 15, o = 40) => {
  const r = vr(e.x, o, t.width - o) * n, i = vr(e.y, o, t.height - o) * n;
  return [r, i];
}, Pn = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), go = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), jn = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), xt = (e, t = [0, 0]) => {
  const { x: n, y: o } = Po(e) ? e.internals.positionAbsolute : Ut(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, Sn = (e, t = [0, 0]) => {
  const { x: n, y: o } = Po(e) ? e.internals.positionAbsolute : Ut(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, Qi = (e, t) => jn(Pn(go(e), go(t))), Ot = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, br = (e) => je(e.width) && je(e.height) && je(e.x) && je(e.y), je = (e) => !isNaN(e) && isFinite(e), Ji = (e, t) => (n, o) => {
}, Kt = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), Nt = ({ x: e, y: t }, [n, o, r], i = !1, s = [1, 1]) => {
  const a = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return i ? Kt(a, s) : a;
}, wt = ({ x: e, y: t }, [n, o, r]) => ({
  x: e * r + n,
  y: t * r + o
});
function lt(e, t) {
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
function Gd(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = lt(e, n), r = lt(e, t);
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
    const o = lt(e.top ?? e.y ?? 0, n), r = lt(e.bottom ?? e.y ?? 0, n), i = lt(e.left ?? e.x ?? 0, t), s = lt(e.right ?? e.x ?? 0, t);
    return { top: o, right: s, bottom: r, left: i, x: i + s, y: o + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function Kd(e, t, n, o, r, i) {
  const { x: s, y: a } = wt(e, [t, n, o]), { x: l, y: c } = wt({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), u = r - l, d = i - c;
  return {
    left: Math.floor(s),
    top: Math.floor(a),
    right: Math.floor(u),
    bottom: Math.floor(d)
  };
}
const To = (e, t, n, o, r, i) => {
  const s = Gd(i, t, n), a = (t - s.x) / e.width, l = (n - s.y) / e.height, c = Math.min(a, l), u = yt(c, o, r), d = e.x + e.width / 2, f = e.y + e.height / 2, p = t / 2 - d * u, g = n / 2 - f * u, x = Kd(e, p, g, u, t, n), w = {
    left: Math.min(x.left - s.left, 0),
    top: Math.min(x.top - s.top, 0),
    right: Math.min(x.right - s.right, 0),
    bottom: Math.min(x.bottom - s.bottom, 0)
  };
  return {
    x: p - w.left + w.right,
    y: g - w.top + w.bottom,
    zoom: u
  };
}, Bt = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function at(e) {
  return e != null && e !== "parent";
}
function Ze(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function es(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function ts(e, t = { width: 0, height: 0 }, n, o, r) {
  const i = { ...e }, s = o.get(n);
  if (s) {
    const a = s.origin || r;
    i.x += s.internals.positionAbsolute.x - (t.width ?? 0) * a[0], i.y += s.internals.positionAbsolute.y - (t.height ?? 0) * a[1];
  }
  return i;
}
function Sr(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function Qd() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function Jd(e) {
  return { ...Xi, ...e || {} };
}
function $t(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: i, y: s } = $e(e), a = Nt({ x: i - (r?.left ?? 0), y: s - (r?.top ?? 0) }, o), { x: l, y: c } = n ? Kt(a, t) : a;
  return {
    xSnapped: l,
    ySnapped: c,
    ...a
  };
}
const zo = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), ns = (e) => e?.getRootNode?.() || window?.document, ef = ["INPUT", "SELECT", "TEXTAREA"];
function os(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : ef.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const rs = (e) => "clientX" in e, $e = (e, t) => {
  const n = rs(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, _r = (e, t, n, o, r) => {
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
      ...zo(s)
    };
  });
};
function is({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: i, targetControlX: s, targetControlY: a }) {
  const l = e * 0.125 + r * 0.375 + s * 0.375 + n * 0.125, c = t * 0.125 + i * 0.375 + a * 0.375 + o * 0.125, u = Math.abs(l - e), d = Math.abs(c - t);
  return [l, c, u, d];
}
function on(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function Er({ pos: e, x1: t, y1: n, x2: o, y2: r, c: i }) {
  switch (e) {
    case K.Left:
      return [t - on(t - o, i), n];
    case K.Right:
      return [t + on(o - t, i), n];
    case K.Top:
      return [t, n - on(n - r, i)];
    case K.Bottom:
      return [t, n + on(r - n, i)];
  }
}
function ss({ sourceX: e, sourceY: t, sourcePosition: n = K.Bottom, targetX: o, targetY: r, targetPosition: i = K.Top, curvature: s = 0.25 }) {
  const [a, l] = Er({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: s
  }), [c, u] = Er({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: s
  }), [d, f, p, g] = is({
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
    p,
    g
  ];
}
function as({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, i = n < e ? n + r : n - r, s = Math.abs(o - t) / 2, a = o < t ? o + s : o - s;
  return [i, a, r, s];
}
function tf({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: i = "basic" }) {
  if (i === "manual")
    return o;
  const s = r && n ? o + 1e3 : o, a = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return s + a;
}
function nf({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const i = Pn(Sn(e), Sn(t));
  i.x === i.x2 && (i.x2 += 1), i.y === i.y2 && (i.y2 += 1);
  const s = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return Ot(s, jn(i)) > 0;
}
const of = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, rf = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), sf = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", ze.error006()), t;
  const o = n.getEdgeId || of;
  let r;
  return Ui(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, rf(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
};
function cs({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, i, s, a] = as({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, i, s, a];
}
const Nr = {
  [K.Left]: { x: -1, y: 0 },
  [K.Right]: { x: 1, y: 0 },
  [K.Top]: { x: 0, y: -1 },
  [K.Bottom]: { x: 0, y: 1 }
}, af = ({ source: e, sourcePosition: t = K.Bottom, target: n }) => t === K.Left || t === K.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, Cr = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function cf({ source: e, sourcePosition: t = K.Bottom, target: n, targetPosition: o = K.Top, center: r, offset: i, stepPosition: s }) {
  const a = Nr[t], l = Nr[o], c = { x: e.x + a.x * i, y: e.y + a.y * i }, u = { x: n.x + l.x * i, y: n.y + l.y * i }, d = af({
    source: c,
    sourcePosition: t,
    target: u
  }), f = d.x !== 0 ? "x" : "y", p = d[f];
  let g = [], x, w;
  const y = { x: 0, y: 0 }, S = { x: 0, y: 0 }, [, , m, b] = as({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (a[f] * l[f] === -1) {
    f === "x" ? (x = r.x ?? c.x + (u.x - c.x) * s, w = r.y ?? (c.y + u.y) / 2) : (x = r.x ?? (c.x + u.x) / 2, w = r.y ?? c.y + (u.y - c.y) * s);
    const A = [
      { x, y: c.y },
      { x, y: u.y }
    ], D = [
      { x: c.x, y: w },
      { x: u.x, y: w }
    ];
    a[f] === p ? g = f === "x" ? A : D : g = f === "x" ? D : A;
  } else {
    const A = [{ x: c.x, y: u.y }], D = [{ x: u.x, y: c.y }];
    if (f === "x" ? g = a.x === p ? D : A : g = a.y === p ? A : D, t === o) {
      const v = Math.abs(e[f] - n[f]);
      if (v <= i) {
        const k = Math.min(i - 1, i - v);
        a[f] === p ? y[f] = (c[f] > e[f] ? -1 : 1) * k : S[f] = (u[f] > n[f] ? -1 : 1) * k;
      }
    }
    if (t !== o) {
      const v = f === "x" ? "y" : "x", k = a[f] === l[v], E = c[v] > u[v], M = c[v] < u[v];
      (a[f] === 1 && (!k && E || k && M) || a[f] !== 1 && (!k && M || k && E)) && (g = f === "x" ? A : D);
    }
    const B = { x: c.x + y.x, y: c.y + y.y }, I = { x: u.x + S.x, y: u.y + S.y }, $ = Math.max(Math.abs(B.x - g[0].x), Math.abs(I.x - g[0].x)), H = Math.max(Math.abs(B.y - g[0].y), Math.abs(I.y - g[0].y));
    $ >= H ? (x = (B.x + I.x) / 2, w = g[0].y) : (x = g[0].x, w = (B.y + I.y) / 2);
  }
  const N = { x: c.x + y.x, y: c.y + y.y }, _ = { x: u.x + S.x, y: u.y + S.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...N.x !== g[0].x || N.y !== g[0].y ? [N] : [],
    ...g,
    ..._.x !== g[g.length - 1].x || _.y !== g[g.length - 1].y ? [_] : [],
    n
  ], x, w, m, b];
}
function lf(e, t, n, o) {
  const r = Math.min(Cr(e, t) / 2, Cr(t, n) / 2, o), { x: i, y: s } = t;
  if (e.x === i && i === n.x || e.y === s && s === n.y)
    return `L${i} ${s}`;
  if (e.y === s) {
    const c = e.x < n.x ? -1 : 1, u = e.y < n.y ? 1 : -1;
    return `L ${i + r * c},${s}Q ${i},${s} ${i},${s + r * u}`;
  }
  const a = e.x < n.x ? 1 : -1, l = e.y < n.y ? -1 : 1;
  return `L ${i},${s + r * l}Q ${i},${s} ${i + r * a},${s}`;
}
function mo({ sourceX: e, sourceY: t, sourcePosition: n = K.Bottom, targetX: o, targetY: r, targetPosition: i = K.Top, borderRadius: s = 5, centerX: a, centerY: l, offset: c = 20, stepPosition: u = 0.5 }) {
  const [d, f, p, g, x] = cf({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: i,
    center: { x: a, y: l },
    offset: c,
    stepPosition: u
  });
  let w = `M${d[0].x} ${d[0].y}`;
  for (let y = 1; y < d.length - 1; y++)
    w += lf(d[y - 1], d[y], d[y + 1], s);
  return w += `L${d[d.length - 1].x} ${d[d.length - 1].y}`, [w, f, p, g, x];
}
function kr(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function uf(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!kr(t) || !kr(n))
    return null;
  const o = t.internals.handleBounds || Ir(t.handles), r = n.internals.handleBounds || Ir(n.handles), i = Mr(o?.source ?? [], e.sourceHandle), s = Mr(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === mt.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!i || !s)
    return e.onError?.("008", ze.error008(i ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const a = i?.position || K.Bottom, l = s?.position || K.Top, c = ct(t, i, a), u = ct(n, s, l);
  return {
    sourceX: c.x,
    sourceY: c.y,
    targetX: u.x,
    targetY: u.y,
    sourcePosition: a,
    targetPosition: l
  };
}
function Ir(e) {
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
function ct(e, t, n = K.Left, o = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, i = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: s, height: a } = t ?? Ze(e);
  if (o)
    return { x: r + s / 2, y: i + a / 2 };
  switch (t?.position ?? n) {
    case K.Top:
      return { x: r + s / 2, y: i };
    case K.Right:
      return { x: r + s, y: i + a / 2 };
    case K.Bottom:
      return { x: r + s / 2, y: i + a };
    case K.Left:
      return { x: r, y: i + a / 2 };
  }
}
function Mr(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function yo(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function df(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const i = /* @__PURE__ */ new Set();
  return e.reduce((s, a) => ([a.markerStart || o, a.markerEnd || r].forEach((l) => {
    if (l && typeof l == "object") {
      const c = yo(l, t);
      i.has(c) || (s.push({ id: c, color: l.color || n, ...l }), i.add(c));
    }
  }), s), []).sort((s, a) => s.id.localeCompare(a.id));
}
const ls = 1e3, ff = 10, Ro = {
  nodeOrigin: [0, 0],
  nodeExtent: Ht,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, hf = {
  ...Ro,
  checkEquality: !0
};
function Lo(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function pf(e, t, n) {
  const o = Lo(Ro, n);
  for (const r of e.values())
    if (r.parentId)
      Vo(r, e, t, o);
    else {
      const i = Ut(r, o.nodeOrigin), s = at(r.extent) ? r.extent : o.nodeExtent, a = st(i, s, Ze(r));
      r.internals.positionAbsolute = a;
    }
}
function gf(e, t) {
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
function Ho(e) {
  return e === "manual";
}
function xo(e, t, n, o = {}) {
  const r = Lo(hf, o), i = { i: 0 }, s = new Map(t), a = r?.elevateNodesOnSelect && !Ho(r.zIndexMode) ? ls : 0;
  let l = e.length > 0, c = !1;
  t.clear(), n.clear();
  for (const u of e) {
    let d = s.get(u.id);
    if (r.checkEquality && u === d?.internals.userNode)
      t.set(u.id, d);
    else {
      const f = Ut(u, r.nodeOrigin), p = at(u.extent) ? u.extent : r.nodeExtent, g = st(f, p, Ze(u));
      d = {
        ...r.defaults,
        ...u,
        measured: {
          width: u.measured?.width,
          height: u.measured?.height
        },
        internals: {
          positionAbsolute: g,
          // if user re-initializes the node or removes `measured` for whatever reason, we reset the handleBounds so that the node gets re-measured
          handleBounds: gf(u, d),
          z: us(u, a, r.zIndexMode),
          userNode: u
        }
      }, t.set(u.id, d);
    }
    (d.measured === void 0 || d.measured.width === void 0 || d.measured.height === void 0) && !d.hidden && (l = !1), u.parentId && Vo(d, t, n, o, i), c ||= u.selected ?? !1;
  }
  return { nodesInitialized: l, hasSelectedNodes: c };
}
function mf(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function Vo(e, t, n, o, r) {
  const { elevateNodesOnSelect: i, nodeOrigin: s, nodeExtent: a, zIndexMode: l } = Lo(Ro, o), c = e.parentId, u = t.get(c);
  if (!u) {
    console.warn(`Parent node ${c} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  mf(e, n), r && !u.parentId && u.internals.rootParentIndex === void 0 && l === "auto" && (u.internals.rootParentIndex = ++r.i, u.internals.z = u.internals.z + r.i * ff), r && u.internals.rootParentIndex !== void 0 && (r.i = u.internals.rootParentIndex);
  const d = i && !Ho(l) ? ls : 0, { x: f, y: p, z: g } = yf(e, u, s, a, d, l), { positionAbsolute: x } = e.internals, w = f !== x.x || p !== x.y;
  (w || g !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: w ? { x: f, y: p } : x,
      z: g
    }
  });
}
function us(e, t, n) {
  const o = je(e.zIndex) ? e.zIndex : 0;
  return Ho(n) ? o : o + (e.selected ? t : 0);
}
function yf(e, t, n, o, r, i) {
  const { x: s, y: a } = t.internals.positionAbsolute, l = Ze(e), c = Ut(e, n), u = at(e.extent) ? st(c, e.extent, l) : c;
  let d = st({ x: s + u.x, y: a + u.y }, o, l);
  e.extent === "parent" && (d = Ki(d, l, t));
  const f = us(e, r, i), p = t.internals.z ?? 0;
  return {
    x: d.x,
    y: d.y,
    z: p >= f ? p + 1 : f
  };
}
function Oo(e, t, n, o = [0, 0]) {
  const r = [], i = /* @__PURE__ */ new Map();
  for (const s of e) {
    const a = t.get(s.parentId);
    if (!a)
      continue;
    const l = i.get(s.parentId)?.expandedRect ?? xt(a), c = Qi(l, s.rect);
    i.set(s.parentId, { expandedRect: c, parent: a });
  }
  return i.size > 0 && i.forEach(({ expandedRect: s, parent: a }, l) => {
    const c = a.internals.positionAbsolute, u = Ze(a), d = a.origin ?? o, f = s.x < c.x ? Math.round(Math.abs(c.x - s.x)) : 0, p = s.y < c.y ? Math.round(Math.abs(c.y - s.y)) : 0, g = Math.max(u.width, Math.round(s.width)), x = Math.max(u.height, Math.round(s.height)), w = (g - u.width) * d[0], y = (x - u.height) * d[1];
    (f > 0 || p > 0 || w || y) && (r.push({
      id: l,
      type: "position",
      position: {
        x: a.position.x - f + w,
        y: a.position.y - p + y
      }
    }), n.get(l)?.forEach((S) => {
      e.some((m) => m.id === S.id) || r.push({
        id: S.id,
        type: "position",
        position: {
          x: S.position.x + f,
          y: S.position.y + p
        }
      });
    })), (u.width < s.width || u.height < s.height || f || p) && r.push({
      id: l,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: g + (f ? d[0] * f - w : 0),
        height: x + (p ? d[1] * p - y : 0)
      }
    });
  }), r;
}
function xf(e, t, n, o, r, i, s) {
  const a = o?.querySelector(".xyflow__viewport");
  let l = !1;
  if (!a)
    return { changes: [], updatedInternals: l };
  const c = [], u = window.getComputedStyle(a), { m22: d } = new window.DOMMatrixReadOnly(u.transform), f = [];
  for (const p of e.values()) {
    const g = t.get(p.id);
    if (!g)
      continue;
    if (g.hidden) {
      t.set(g.id, {
        ...g,
        internals: {
          ...g.internals,
          handleBounds: void 0
        }
      }), l = !0;
      continue;
    }
    const x = zo(p.nodeElement), w = g.measured.width !== x.width || g.measured.height !== x.height;
    if (!!(x.width && x.height && (w || !g.internals.handleBounds || p.force))) {
      const S = p.nodeElement.getBoundingClientRect(), m = at(g.extent) ? g.extent : i;
      let { positionAbsolute: b } = g.internals;
      g.parentId && g.extent === "parent" ? b = Ki(b, x, t.get(g.parentId)) : m && (b = st(b, m, x));
      const N = {
        ...g,
        measured: x,
        internals: {
          ...g.internals,
          positionAbsolute: b,
          handleBounds: {
            source: _r("source", p.nodeElement, S, d, g.id),
            target: _r("target", p.nodeElement, S, d, g.id)
          }
        }
      };
      t.set(g.id, N), g.parentId && Vo(N, t, n, { nodeOrigin: r, zIndexMode: s }), l = !0, w && (c.push({
        id: g.id,
        type: "dimensions",
        dimensions: x
      }), g.expandParent && g.parentId && f.push({
        id: g.id,
        parentId: g.parentId,
        rect: xt(N, r)
      }));
    }
  }
  if (f.length > 0) {
    const p = Oo(f, t, n, r);
    c.push(...p);
  }
  return { changes: c, updatedInternals: l };
}
async function wf({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: i }) {
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
function Ar(e, t, n, o, r, i) {
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
function ds(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: i, sourceHandle: s = null, targetHandle: a = null } = o, l = { edgeId: o.id, source: r, target: i, sourceHandle: s, targetHandle: a }, c = `${r}-${s}--${i}-${a}`, u = `${i}-${a}--${r}-${s}`;
    Ar("source", l, u, e, r, s), Ar("target", l, c, e, i, a), t.set(o.id, o);
  }
}
function fs(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : fs(n, t) : !1;
}
function Dr(e, t, n) {
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
function vf(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [i, s] of e)
    if ((s.selected || s.id === o) && (!s.parentId || !fs(s, e)) && (s.draggable || t && typeof s.draggable > "u")) {
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
function Zn({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
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
function bf({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const i = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, s = Kt(i, t);
  return {
    x: s.x - i.x,
    y: s.y - i.y
  };
}
function Sf({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let i = { x: null, y: null }, s = 0, a = /* @__PURE__ */ new Map(), l = !1, c = { x: 0, y: 0 }, u = null, d = !1, f = null, p = !1, g = !1, x = null;
  function w({ noDragClassName: S, handleSelector: m, domNode: b, isSelectable: N, nodeId: _, nodeClickDistance: C = 0 }) {
    f = _e(b);
    function A({ x: $, y: H }) {
      const { nodeLookup: v, nodeExtent: k, snapGrid: E, snapToGrid: M, nodeOrigin: j, onNodeDrag: P, onSelectionDrag: L, onError: O, updateNodePositions: V } = t();
      i = { x: $, y: H };
      let Y = !1;
      const X = a.size > 1, Q = X && k ? go(Gt(a)) : null, ne = X && M ? bf({
        dragItems: a,
        snapGrid: E,
        x: $,
        y: H
      }) : null;
      for (const [Z, z] of a) {
        if (!v.has(Z))
          continue;
        let W = { x: $ - z.distance.x, y: H - z.distance.y };
        M && (W = ne ? {
          x: Math.round(W.x + ne.x),
          y: Math.round(W.y + ne.y)
        } : Kt(W, E));
        let te = null;
        if (X && k && !z.extent && Q) {
          const { positionAbsolute: J } = z.internals, re = J.x - Q.x + k[0][0], R = J.x + z.measured.width - Q.x2 + k[1][0], G = J.y - Q.y + k[0][1], ue = J.y + z.measured.height - Q.y2 + k[1][1];
          te = [
            [re, G],
            [R, ue]
          ];
        }
        const { position: ee, positionAbsolute: U } = Gi({
          nodeId: Z,
          nextPosition: W,
          nodeLookup: v,
          nodeExtent: te || k,
          nodeOrigin: j,
          onError: O
        });
        Y = Y || z.position.x !== ee.x || z.position.y !== ee.y, z.position = ee, z.internals.positionAbsolute = U;
      }
      if (g = g || Y, !!Y && (V(a, !0), x && (o || P || !_ && L))) {
        const [Z, z] = Zn({
          nodeId: _,
          dragItems: a,
          nodeLookup: v
        });
        o?.(x, a, Z, z), P?.(x, Z, z), _ || L?.(x, z);
      }
    }
    async function D() {
      if (!u)
        return;
      const { transform: $, panBy: H, autoPanSpeed: v, autoPanOnNodeDrag: k } = t();
      if (!k) {
        l = !1, cancelAnimationFrame(s);
        return;
      }
      const [E, M] = $o(c, u, v);
      (E !== 0 || M !== 0) && (i.x = (i.x ?? 0) - E / $[2], i.y = (i.y ?? 0) - M / $[2], await H({ x: E, y: M }) && A(i)), s = requestAnimationFrame(D);
    }
    function B($) {
      const { nodeLookup: H, multiSelectionActive: v, nodesDraggable: k, transform: E, snapGrid: M, snapToGrid: j, selectNodesOnDrag: P, onNodeDragStart: L, onSelectionDragStart: O, unselectNodesAndEdges: V } = t();
      d = !0, (!P || !N) && !v && _ && (H.get(_)?.selected || V()), N && P && _ && e?.(_);
      const Y = $t($.sourceEvent, { transform: E, snapGrid: M, snapToGrid: j, containerBounds: u });
      if (i = Y, a = vf(H, k, Y, _), a.size > 0 && (n || L || !_ && O)) {
        const [X, Q] = Zn({
          nodeId: _,
          dragItems: a,
          nodeLookup: H
        });
        n?.($.sourceEvent, a, X, Q), L?.($.sourceEvent, X, Q), _ || O?.($.sourceEvent, Q);
      }
    }
    const I = Mi().clickDistance(C).on("start", ($) => {
      const { domNode: H, nodeDragThreshold: v, transform: k, snapGrid: E, snapToGrid: M } = t();
      u = H?.getBoundingClientRect() || null, p = !1, g = !1, x = $.sourceEvent, v === 0 && B($), i = $t($.sourceEvent, { transform: k, snapGrid: E, snapToGrid: M, containerBounds: u }), c = $e($.sourceEvent, u);
    }).on("drag", ($) => {
      const { autoPanOnNodeDrag: H, transform: v, snapGrid: k, snapToGrid: E, nodeDragThreshold: M, nodeLookup: j } = t(), P = $t($.sourceEvent, { transform: v, snapGrid: k, snapToGrid: E, containerBounds: u });
      if (x = $.sourceEvent, ($.sourceEvent.type === "touchmove" && $.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      _ && !j.has(_)) && (p = !0), !p) {
        if (!l && H && d && (l = !0, D()), !d) {
          const L = $e($.sourceEvent, u), O = L.x - c.x, V = L.y - c.y;
          Math.sqrt(O * O + V * V) > M && B($);
        }
        (i.x !== P.xSnapped || i.y !== P.ySnapped) && a && d && (c = $e($.sourceEvent, u), A(P));
      }
    }).on("end", ($) => {
      if (!d || p) {
        p && a.size > 0 && t().updateNodePositions(a, !1);
        return;
      }
      if (l = !1, d = !1, cancelAnimationFrame(s), a.size > 0) {
        const { nodeLookup: H, updateNodePositions: v, onNodeDragStop: k, onSelectionDragStop: E } = t();
        if (g && (v(a, !1), g = !1), r || k || !_ && E) {
          const [M, j] = Zn({
            nodeId: _,
            dragItems: a,
            nodeLookup: H,
            dragging: !1
          });
          r?.($.sourceEvent, a, M, j), k?.($.sourceEvent, M, j), _ || E?.($.sourceEvent, j);
        }
      }
    }).filter(($) => {
      const H = $.target;
      return !$.button && (!S || !Dr(H, `.${S}`, b)) && (!m || Dr(H, m, b));
    });
    f.call(I);
  }
  function y() {
    f?.on(".drag", null);
  }
  return {
    update: w,
    destroy: y
  };
}
function _f(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const i of t.values())
    Ot(r, xt(i)) > 0 && o.push(i);
  return o;
}
const Ef = 250;
function Nf(e, t, n, o) {
  let r = [], i = 1 / 0;
  const s = _f(e, n, t + Ef);
  for (const a of s) {
    const l = [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []];
    for (const c of l) {
      if (o.nodeId === c.nodeId && o.type === c.type && o.id === c.id)
        continue;
      const { x: u, y: d } = ct(a, c, c.position, !0), f = Math.sqrt(Math.pow(u - e.x, 2) + Math.pow(d - e.y, 2));
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
function hs(e, t, n, o, r, i = !1) {
  const s = o.get(e);
  if (!s)
    return null;
  const a = r === "strict" ? s.internals.handleBounds?.[t] : [...s.internals.handleBounds?.source ?? [], ...s.internals.handleBounds?.target ?? []], l = (n ? a?.find((c) => c.id === n) : a?.[0]) ?? null;
  return l && i ? { ...l, ...ct(s, l, l.position, !0) } : l;
}
function ps(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Cf(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const gs = () => !0;
function kf(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: i, isTarget: s, domNode: a, nodeLookup: l, lib: c, autoPanOnConnect: u, flowId: d, panBy: f, cancelConnection: p, onConnectStart: g, onConnect: x, onConnectEnd: w, isValidConnection: y = gs, onReconnectEnd: S, updateConnection: m, getTransform: b, getFromHandle: N, autoPanSpeed: _, dragThreshold: C = 1, handleDomNode: A }) {
  const D = ns(e.target);
  let B = 0, I;
  const { x: $, y: H } = $e(e), v = ps(i, A), k = a?.getBoundingClientRect();
  let E = !1;
  if (!k || !v)
    return;
  const M = hs(r, v, o, l, t);
  if (!M)
    return;
  let j = $e(e, k), P = !1, L = null, O = !1, V = null;
  function Y() {
    if (!u || !k)
      return;
    const [ee, U] = $o(j, k, _);
    f({ x: ee, y: U }), B = requestAnimationFrame(Y);
  }
  const X = {
    ...M,
    nodeId: r,
    type: v,
    position: M.position
  }, Q = l.get(r);
  let Z = {
    inProgress: !0,
    isValid: null,
    from: ct(Q, X, K.Left, !0),
    fromHandle: X,
    fromPosition: X.position,
    fromNode: Q,
    to: j,
    toHandle: null,
    toPosition: wr[X.position],
    toNode: null,
    pointer: j
  };
  function z() {
    E = !0, m(Z), g?.(e, { nodeId: r, handleId: o, handleType: v });
  }
  C === 0 && z();
  function W(ee) {
    if (!E) {
      const { x: ue, y: he } = $e(ee), Ne = ue - $, Ce = he - H;
      if (!(Ne * Ne + Ce * Ce > C * C))
        return;
      z();
    }
    if (!N() || !X) {
      te(ee);
      return;
    }
    const U = b();
    j = $e(ee, k), I = Nf(Nt(j, U, !1, [1, 1]), n, l, X), P || (Y(), P = !0);
    const J = ms(ee, {
      handle: I,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: s ? "target" : "source",
      isValidConnection: y,
      doc: D,
      lib: c,
      flowId: d,
      nodeLookup: l
    });
    V = J.handleDomNode, L = J.connection, O = Cf(!!I, J.isValid);
    const re = l.get(r), R = re ? ct(re, X, K.Left, !0) : Z.from, G = {
      ...Z,
      from: R,
      isValid: O,
      to: J.toHandle && O ? wt({ x: J.toHandle.x, y: J.toHandle.y }, U) : j,
      toHandle: J.toHandle,
      toPosition: O && J.toHandle ? J.toHandle.position : wr[X.position],
      toNode: J.toHandle ? l.get(J.toHandle.nodeId) : null,
      pointer: j
    };
    m(G), Z = G;
  }
  function te(ee) {
    if (!("touches" in ee && ee.touches.length > 0)) {
      if (E) {
        (I || V) && L && O && x?.(L);
        const { inProgress: U, ...J } = Z, re = {
          ...J,
          toPosition: Z.toHandle ? Z.toPosition : null
        };
        w?.(ee, re), i && S?.(ee, re);
      }
      p(), cancelAnimationFrame(B), P = !1, O = !1, L = null, V = null, D.removeEventListener("mousemove", W), D.removeEventListener("mouseup", te), D.removeEventListener("touchmove", W), D.removeEventListener("touchend", te);
    }
  }
  D.addEventListener("mousemove", W), D.addEventListener("mouseup", te), D.addEventListener("touchmove", W), D.addEventListener("touchend", te);
}
function ms(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: i, doc: s, lib: a, flowId: l, isValidConnection: c = gs, nodeLookup: u }) {
  const d = i === "target", f = t ? s.querySelector(`.${a}-flow__handle[data-id="${l}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: p, y: g } = $e(e), x = s.elementFromPoint(p, g), w = x?.classList.contains(`${a}-flow__handle`) ? x : f, y = {
    handleDomNode: w,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (w) {
    const S = ps(void 0, w), m = w.getAttribute("data-nodeid"), b = w.getAttribute("data-handleid"), N = w.classList.contains("connectable"), _ = w.classList.contains("connectableend");
    if (!m || !S)
      return y;
    const C = {
      source: d ? m : o,
      sourceHandle: d ? b : r,
      target: d ? o : m,
      targetHandle: d ? r : b
    };
    y.connection = C;
    const D = N && _ && (n === mt.Strict ? d && S === "source" || !d && S === "target" : m !== o || b !== r);
    y.isValid = D && c(C), y.toHandle = hs(m, S, b, u, n, !0);
  }
  return y;
}
const wo = {
  onPointerDown: kf,
  isValid: ms
};
function If({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = _e(e);
  function i({ translateExtent: a, width: l, height: c, zoomStep: u = 1, pannable: d = !0, zoomable: f = !0, inversePan: p = !1 }) {
    const g = (m) => {
      if (m.sourceEvent.type !== "wheel" || !t)
        return;
      const b = n(), N = m.sourceEvent.ctrlKey && Bt() ? 10 : 1, _ = -m.sourceEvent.deltaY * (m.sourceEvent.deltaMode === 1 ? 0.05 : m.sourceEvent.deltaMode ? 1 : 2e-3) * u, C = b[2] * Math.pow(2, _ * N);
      t.scaleTo(C);
    };
    let x = [0, 0];
    const w = (m) => {
      (m.sourceEvent.type === "mousedown" || m.sourceEvent.type === "touchstart") && (x = [
        m.sourceEvent.clientX ?? m.sourceEvent.touches[0].clientX,
        m.sourceEvent.clientY ?? m.sourceEvent.touches[0].clientY
      ]);
    }, y = (m) => {
      const b = n();
      if (m.sourceEvent.type !== "mousemove" && m.sourceEvent.type !== "touchmove" || !t)
        return;
      const N = [
        m.sourceEvent.clientX ?? m.sourceEvent.touches[0].clientX,
        m.sourceEvent.clientY ?? m.sourceEvent.touches[0].clientY
      ], _ = [N[0] - x[0], N[1] - x[1]];
      x = N;
      const C = o() * Math.max(b[2], Math.log(b[2])) * (p ? -1 : 1), A = {
        x: b[0] - _[0] * C,
        y: b[1] - _[1] * C
      }, D = [
        [0, 0],
        [l, c]
      ];
      t.setViewportConstrained({
        x: A.x,
        y: A.y,
        zoom: b[2]
      }, D, a);
    }, S = Yi().on("start", w).on("zoom", d ? y : null).on("zoom.wheel", f ? g : null);
    r.call(S, {});
  }
  function s() {
    r.on("zoom", null);
  }
  return {
    update: i,
    destroy: s,
    pointer: De
  };
}
const $n = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), Un = ({ x: e, y: t, zoom: n }) => Dn.translate(e, t).scale(n), ut = (e, t) => e.target.closest(`.${t}`), ys = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Mf = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, Gn = (e, t = 0, n = Mf, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, xs = (e) => {
  const t = e.ctrlKey && Bt() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Af({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: i, zoomOnPinch: s, onPanZoomStart: a, onPanZoom: l, onPanZoomEnd: c }) {
  return (u) => {
    if (ut(u, t))
      return u.ctrlKey && u.preventDefault(), !1;
    u.preventDefault(), u.stopImmediatePropagation();
    const d = n.property("__zoom").k || 1;
    if (u.ctrlKey && s) {
      const w = De(u), y = xs(u), S = d * Math.pow(2, y);
      o.scaleTo(n, S, w, u);
      return;
    }
    const f = u.deltaMode === 1 ? 20 : 1;
    let p = r === ot.Vertical ? 0 : u.deltaX * f, g = r === ot.Horizontal ? 0 : u.deltaY * f;
    !Bt() && u.shiftKey && r !== ot.Vertical && (p = u.deltaY * f, g = 0), o.translateBy(
      n,
      -(p / d) * i,
      -(g / d) * i,
      // @ts-ignore
      { internal: !0 }
    );
    const x = $n(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (l?.(u, x), e.panScrollTimeout = setTimeout(() => {
      c?.(u, x), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, a?.(u, x));
  };
}
function Df({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const i = o.type === "wheel", s = !t && i && !o.ctrlKey, a = ut(o, e);
    if (o.ctrlKey && i && a && o.preventDefault(), s || a)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function Pf({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = $n(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function jf({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (i) => {
    e.usedRightMouseButton = !!(n && ys(t, e.mouseButton ?? 0)), i.sourceEvent?.sync || o([i.transform.x, i.transform.y, i.transform.k]), r && !i.sourceEvent?.internal && r?.(i.sourceEvent, $n(i.transform));
  };
}
function $f({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: i }) {
  return (s) => {
    if (!s.sourceEvent?.internal && (e.isZoomingOrPanning = !1, i && ys(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && s.sourceEvent && i(s.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
      const a = $n(s.transform);
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
function Tf({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: i, userSelectionActive: s, noWheelClassName: a, noPanClassName: l, lib: c, connectionInProgress: u }) {
  return (d) => {
    const f = e || t, p = n && d.ctrlKey, g = d.type === "wheel";
    if (d.button === 1 && d.type === "mousedown" && (ut(d, `${c}-flow__node`) || ut(d, `${c}-flow__edge`)))
      return !0;
    if (!o && !f && !r && !i && !n || s || u && !g || ut(d, a) && g || ut(d, l) && (!g || r && g && !e) || !n && d.ctrlKey && g)
      return !1;
    if (!n && d.type === "touchstart" && d.touches?.length > 1)
      return d.preventDefault(), !1;
    if (!f && !r && !p && g || !o && (d.type === "mousedown" || d.type === "touchstart") || Array.isArray(o) && !o.includes(d.button) && d.type === "mousedown")
      return !1;
    const x = Array.isArray(o) && o.includes(d.button) || !d.button || d.button <= 1;
    return (!d.ctrlKey || g) && x;
  };
}
function zf({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: i, onPanZoomStart: s, onPanZoomEnd: a, onDraggingChange: l }) {
  const c = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, u = e.getBoundingClientRect(), d = Yi().scaleExtent([t, n]).translateExtent(o), f = _e(e).call(d);
  S({
    x: r.x,
    y: r.y,
    zoom: yt(r.zoom, t, n)
  }, [
    [0, 0],
    [u.width, u.height]
  ], o);
  const p = f.on("wheel.zoom"), g = f.on("dblclick.zoom");
  d.wheelDelta(xs);
  async function x(I, $) {
    return f ? new Promise((H) => {
      d?.interpolate($?.interpolate === "linear" ? jt : ln).transform(Gn(f, $?.duration, $?.ease, () => H(!0)), I);
    }) : !1;
  }
  function w({ noWheelClassName: I, noPanClassName: $, onPaneContextMenu: H, userSelectionActive: v, panOnScroll: k, panOnDrag: E, panOnScrollMode: M, panOnScrollSpeed: j, preventScrolling: P, zoomOnPinch: L, zoomOnScroll: O, zoomOnDoubleClick: V, zoomActivationKeyPressed: Y, lib: X, onTransformChange: Q, connectionInProgress: ne, paneClickDistance: Z, selectionOnDrag: z }) {
    v && !c.isZoomingOrPanning && y();
    const W = k && !Y && !v;
    d.clickDistance(z ? 1 / 0 : !je(Z) || Z < 0 ? 0 : Z);
    const te = W ? Af({
      zoomPanValues: c,
      noWheelClassName: I,
      d3Selection: f,
      d3Zoom: d,
      panOnScrollMode: M,
      panOnScrollSpeed: j,
      zoomOnPinch: L,
      onPanZoomStart: s,
      onPanZoom: i,
      onPanZoomEnd: a
    }) : Df({
      noWheelClassName: I,
      preventScrolling: P,
      d3ZoomHandler: p
    });
    f.on("wheel.zoom", te, { passive: !1 });
    const ee = Pf({
      zoomPanValues: c,
      onDraggingChange: l,
      onPanZoomStart: s
    });
    d.on("start", ee);
    const U = jf({
      zoomPanValues: c,
      panOnDrag: E,
      onPaneContextMenu: !!H,
      onPanZoom: i,
      onTransformChange: Q
    });
    d.on("zoom", U);
    const J = $f({
      zoomPanValues: c,
      panOnDrag: E,
      panOnScroll: k,
      onPaneContextMenu: H,
      onPanZoomEnd: a,
      onDraggingChange: l
    });
    d.on("end", J);
    const re = Tf({
      zoomActivationKeyPressed: Y,
      panOnDrag: E,
      zoomOnScroll: O,
      panOnScroll: k,
      zoomOnDoubleClick: V,
      zoomOnPinch: L,
      userSelectionActive: v,
      noPanClassName: $,
      noWheelClassName: I,
      lib: X,
      connectionInProgress: ne
    });
    d.filter(re), V ? f.on("dblclick.zoom", g) : f.on("dblclick.zoom", null);
  }
  function y() {
    d.on("zoom", null);
  }
  async function S(I, $, H) {
    const v = Un(I), k = d?.constrain()(v, $, H);
    return k && await x(k), k;
  }
  async function m(I, $) {
    const H = Un(I);
    return await x(H, $), H;
  }
  function b(I) {
    if (f) {
      const $ = Un(I), H = f.property("__zoom");
      (H.k !== I.zoom || H.x !== I.x || H.y !== I.y) && d?.transform(f, $, null, { sync: !0 });
    }
  }
  function N() {
    const I = f ? Fi(f.node()) : { x: 0, y: 0, k: 1 };
    return { x: I.x, y: I.y, zoom: I.k };
  }
  async function _(I, $) {
    return f ? new Promise((H) => {
      d?.interpolate($?.interpolate === "linear" ? jt : ln).scaleTo(Gn(f, $?.duration, $?.ease, () => H(!0)), I);
    }) : !1;
  }
  async function C(I, $) {
    return f ? new Promise((H) => {
      d?.interpolate($?.interpolate === "linear" ? jt : ln).scaleBy(Gn(f, $?.duration, $?.ease, () => H(!0)), I);
    }) : !1;
  }
  function A(I) {
    d?.scaleExtent(I);
  }
  function D(I) {
    d?.translateExtent(I);
  }
  function B(I) {
    const $ = !je(I) || I < 0 ? 0 : I;
    d?.clickDistance($);
  }
  return {
    update: w,
    destroy: y,
    setViewport: m,
    setViewportConstrained: S,
    getViewport: N,
    scaleTo: _,
    scaleBy: C,
    setScaleExtent: A,
    setTranslateExtent: D,
    syncViewport: b,
    setClickDistance: B
  };
}
var vt;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(vt || (vt = {}));
function Rf({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: i }) {
  const s = e - t, a = n - o, l = [s > 0 ? 1 : s < 0 ? -1 : 0, a > 0 ? 1 : a < 0 ? -1 : 0];
  return s && r && (l[0] = l[0] * -1), a && i && (l[1] = l[1] * -1), l;
}
function Pr(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: r
  };
}
function Ue(e, t) {
  return Math.max(0, t - e);
}
function Ge(e, t) {
  return Math.max(0, e - t);
}
function rn(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function jr(e, t) {
  return e ? !t : t;
}
function Lf(e, t, n, o, r, i, s, a) {
  let { affectsX: l, affectsY: c } = t;
  const { isHorizontal: u, isVertical: d } = t, f = u && d, { xSnapped: p, ySnapped: g } = n, { minWidth: x, maxWidth: w, minHeight: y, maxHeight: S } = o, { x: m, y: b, width: N, height: _, aspectRatio: C } = e;
  let A = Math.floor(u ? p - e.pointerX : 0), D = Math.floor(d ? g - e.pointerY : 0);
  const B = N + (l ? -A : A), I = _ + (c ? -D : D), $ = -i[0] * N, H = -i[1] * _;
  let v = rn(B, x, w), k = rn(I, y, S);
  if (s) {
    let j = 0, P = 0;
    l && A < 0 ? j = Ue(m + A + $, s[0][0]) : !l && A > 0 && (j = Ge(m + B + $, s[1][0])), c && D < 0 ? P = Ue(b + D + H, s[0][1]) : !c && D > 0 && (P = Ge(b + I + H, s[1][1])), v = Math.max(v, j), k = Math.max(k, P);
  }
  if (a) {
    let j = 0, P = 0;
    l && A > 0 ? j = Ge(m + A, a[0][0]) : !l && A < 0 && (j = Ue(m + B, a[1][0])), c && D > 0 ? P = Ge(b + D, a[0][1]) : !c && D < 0 && (P = Ue(b + I, a[1][1])), v = Math.max(v, j), k = Math.max(k, P);
  }
  if (r) {
    if (u) {
      const j = rn(B / C, y, S) * C;
      if (v = Math.max(v, j), s) {
        let P = 0;
        !l && !c || l && !c && f ? P = Ge(b + H + B / C, s[1][1]) * C : P = Ue(b + H + (l ? A : -A) / C, s[0][1]) * C, v = Math.max(v, P);
      }
      if (a) {
        let P = 0;
        !l && !c || l && !c && f ? P = Ue(b + B / C, a[1][1]) * C : P = Ge(b + (l ? A : -A) / C, a[0][1]) * C, v = Math.max(v, P);
      }
    }
    if (d) {
      const j = rn(I * C, x, w) / C;
      if (k = Math.max(k, j), s) {
        let P = 0;
        !l && !c || c && !l && f ? P = Ge(m + I * C + $, s[1][0]) / C : P = Ue(m + (c ? D : -D) * C + $, s[0][0]) / C, k = Math.max(k, P);
      }
      if (a) {
        let P = 0;
        !l && !c || c && !l && f ? P = Ue(m + I * C, a[1][0]) / C : P = Ge(m + (c ? D : -D) * C, a[0][0]) / C, k = Math.max(k, P);
      }
    }
  }
  D = D + (D < 0 ? k : -k), A = A + (A < 0 ? v : -v), r && (f ? B > I * C ? D = (jr(l, c) ? -A : A) / C : A = (jr(l, c) ? -D : D) * C : u ? (D = A / C, c = l) : (A = D * C, l = c));
  const E = l ? m + A : m, M = c ? b + D : b;
  return {
    width: N + (l ? -A : A),
    height: _ + (c ? -D : D),
    x: i[0] * A * (l ? -1 : 1) + E,
    y: i[1] * D * (c ? -1 : 1) + M
  };
}
const ws = { width: 0, height: 0, x: 0, y: 0 }, Hf = {
  ...ws,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function Vf(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, i = e.measured.width ?? 0, s = e.measured.height ?? 0, a = n[0] * i, l = n[1] * s;
  return [
    [o - a, r - l],
    [o + i - a, r + s - l]
  ];
}
function Of({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const i = _e(e);
  let s = {
    controlDirection: Pr("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function a({ controlPosition: c, boundaries: u, keepAspectRatio: d, resizeDirection: f, onResizeStart: p, onResize: g, onResizeEnd: x, shouldResize: w }) {
    let y = { ...ws }, S = { ...Hf };
    s = {
      boundaries: u,
      resizeDirection: f,
      keepAspectRatio: d,
      controlDirection: Pr(c)
    };
    let m, b = null, N = [], _, C, A, D = !1;
    const B = Mi().on("start", (I) => {
      const { nodeLookup: $, transform: H, snapGrid: v, snapToGrid: k, nodeOrigin: E, paneDomNode: M } = n();
      if (m = $.get(t), !m)
        return;
      b = M?.getBoundingClientRect() ?? null;
      const { xSnapped: j, ySnapped: P } = $t(I.sourceEvent, {
        transform: H,
        snapGrid: v,
        snapToGrid: k,
        containerBounds: b
      });
      y = {
        width: m.measured.width ?? 0,
        height: m.measured.height ?? 0,
        x: m.position.x ?? 0,
        y: m.position.y ?? 0
      }, S = {
        ...y,
        pointerX: j,
        pointerY: P,
        aspectRatio: y.width / y.height
      }, _ = void 0, C = at(m.extent) ? m.extent : void 0, m.parentId && (m.extent === "parent" || m.expandParent) && (_ = $.get(m.parentId)), _ && m.extent === "parent" && (C = [
        [0, 0],
        [_.measured.width, _.measured.height]
      ]), N = [], A = void 0;
      for (const [L, O] of $)
        if (O.parentId === t && (N.push({
          id: L,
          position: { ...O.position },
          extent: O.extent
        }), O.extent === "parent" || O.expandParent)) {
          const V = Vf(O, m, O.origin ?? E);
          A ? A = [
            [Math.min(V[0][0], A[0][0]), Math.min(V[0][1], A[0][1])],
            [Math.max(V[1][0], A[1][0]), Math.max(V[1][1], A[1][1])]
          ] : A = V;
        }
      p?.(I, { ...y });
    }).on("drag", (I) => {
      const { transform: $, snapGrid: H, snapToGrid: v, nodeOrigin: k } = n(), E = $t(I.sourceEvent, {
        transform: $,
        snapGrid: H,
        snapToGrid: v,
        containerBounds: b
      }), M = [];
      if (!m)
        return;
      const { x: j, y: P, width: L, height: O } = y, V = {}, Y = m.origin ?? k, { width: X, height: Q, x: ne, y: Z } = Lf(S, s.controlDirection, E, s.boundaries, s.keepAspectRatio, Y, C, A), z = X !== L, W = Q !== O, te = ne !== j && z, ee = Z !== P && W;
      if (!te && !ee && !z && !W)
        return;
      if ((te || ee || Y[0] === 1 || Y[1] === 1) && (V.x = te ? ne : y.x, V.y = ee ? Z : y.y, y.x = V.x, y.y = V.y, N.length > 0)) {
        const R = ne - j, G = Z - P;
        for (const ue of N)
          ue.position = {
            x: ue.position.x - R + Y[0] * (X - L),
            y: ue.position.y - G + Y[1] * (Q - O)
          }, M.push(ue);
      }
      if ((z || W) && (V.width = z && (!s.resizeDirection || s.resizeDirection === "horizontal") ? X : y.width, V.height = W && (!s.resizeDirection || s.resizeDirection === "vertical") ? Q : y.height, y.width = V.width, y.height = V.height), _ && m.expandParent) {
        const R = Y[0] * (V.width ?? 0);
        V.x && V.x < R && (y.x = R, S.x = S.x - (V.x - R));
        const G = Y[1] * (V.height ?? 0);
        V.y && V.y < G && (y.y = G, S.y = S.y - (V.y - G));
      }
      const U = Rf({
        width: y.width,
        prevWidth: L,
        height: y.height,
        prevHeight: O,
        affectsX: s.controlDirection.affectsX,
        affectsY: s.controlDirection.affectsY
      }), J = { ...y, direction: U };
      w?.(I, J) !== !1 && (D = !0, g?.(I, J), o(V, M));
    }).on("end", (I) => {
      D && (x?.(I, { ...y }), r?.({ ...y }), D = !1);
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
var Kn = { exports: {} }, Qn = {}, Jn = { exports: {} }, eo = {};
var $r;
function Bf() {
  if ($r) return eo;
  $r = 1;
  var e = Wt;
  function t(d, f) {
    return d === f && (d !== 0 || 1 / d === 1 / f) || d !== d && f !== f;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, i = e.useLayoutEffect, s = e.useDebugValue;
  function a(d, f) {
    var p = f(), g = o({ inst: { value: p, getSnapshot: f } }), x = g[0].inst, w = g[1];
    return i(
      function() {
        x.value = p, x.getSnapshot = f, l(x) && w({ inst: x });
      },
      [d, p, f]
    ), r(
      function() {
        return l(x) && w({ inst: x }), d(function() {
          l(x) && w({ inst: x });
        });
      },
      [d]
    ), s(p), p;
  }
  function l(d) {
    var f = d.getSnapshot;
    d = d.value;
    try {
      var p = f();
      return !n(d, p);
    } catch {
      return !0;
    }
  }
  function c(d, f) {
    return f();
  }
  var u = typeof window > "u" || typeof window.document > "u" || typeof window.document.createElement > "u" ? c : a;
  return eo.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : u, eo;
}
var Tr;
function Ff() {
  return Tr || (Tr = 1, Jn.exports = Bf()), Jn.exports;
}
var zr;
function Yf() {
  if (zr) return Qn;
  zr = 1;
  var e = Wt, t = Ff();
  function n(c, u) {
    return c === u && (c !== 0 || 1 / c === 1 / u) || c !== c && u !== u;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, i = e.useRef, s = e.useEffect, a = e.useMemo, l = e.useDebugValue;
  return Qn.useSyncExternalStoreWithSelector = function(c, u, d, f, p) {
    var g = i(null);
    if (g.current === null) {
      var x = { hasValue: !1, value: null };
      g.current = x;
    } else x = g.current;
    g = a(
      function() {
        function y(_) {
          if (!S) {
            if (S = !0, m = _, _ = f(_), p !== void 0 && x.hasValue) {
              var C = x.value;
              if (p(C, _))
                return b = C;
            }
            return b = _;
          }
          if (C = b, o(m, _)) return C;
          var A = f(_);
          return p !== void 0 && p(C, A) ? (m = _, C) : (m = _, b = A);
        }
        var S = !1, m, b, N = d === void 0 ? null : d;
        return [
          function() {
            return y(u());
          },
          N === null ? void 0 : function() {
            return y(N());
          }
        ];
      },
      [u, d, f, p]
    );
    var w = r(c, g[0], g[1]);
    return s(
      function() {
        x.hasValue = !0, x.value = w;
      },
      [w]
    ), l(w), w;
  }, Qn;
}
var Rr;
function Wf() {
  return Rr || (Rr = 1, Kn.exports = Yf()), Kn.exports;
}
var Xf = Wf();
const qf = /* @__PURE__ */ cc(Xf), Zf = {}, Lr = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (u, d) => {
    const f = typeof u == "function" ? u(t) : u;
    if (!Object.is(f, t)) {
      const p = t;
      t = d ?? (typeof f != "object" || f === null) ? f : Object.assign({}, t, f), n.forEach((g) => g(t, p));
    }
  }, r = () => t, l = { setState: o, getState: r, getInitialState: () => c, subscribe: (u) => (n.add(u), () => n.delete(u)), destroy: () => {
    (Zf ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, c = t = e(o, r, l);
  return l;
}, Uf = (e) => e ? Lr(e) : Lr, { useDebugValue: Gf } = Wt, { useSyncExternalStoreWithSelector: Kf } = qf, Qf = (e) => e;
function vs(e, t = Qf, n) {
  const o = Kf(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return Gf(o), o;
}
const Hr = (e, t) => {
  const n = Uf(e), o = (r, i = t) => vs(n, r, i);
  return Object.assign(o, n), o;
}, Jf = (e, t) => e ? Hr(e, t) : Hr;
function de(e, t) {
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
var to = { exports: {} }, we = {};
var Vr;
function eh() {
  if (Vr) return we;
  Vr = 1;
  var e = Wt;
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
      var u = c.as, d = a(u, c.crossOrigin), f = typeof c.integrity == "string" ? c.integrity : void 0, p = typeof c.fetchPriority == "string" ? c.fetchPriority : void 0;
      u === "style" ? o.d.S(
        l,
        typeof c.precedence == "string" ? c.precedence : void 0,
        {
          crossOrigin: d,
          integrity: f,
          fetchPriority: p
        }
      ) : u === "script" && o.d.X(l, {
        crossOrigin: d,
        integrity: f,
        fetchPriority: p,
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
var Or;
function th() {
  if (Or) return to.exports;
  Or = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), to.exports = eh(), to.exports;
}
th();
const Tn = Eo(null), nh = Tn.Provider, bs = ze.error001("react");
function ce(e, t) {
  const n = Xt(Tn);
  if (n === null)
    throw new Error(bs);
  return vs(n, e, t);
}
function fe() {
  const e = Xt(Tn);
  if (e === null)
    throw new Error(bs);
  return xe(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const Br = { display: "none" }, oh = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, Ss = "react-flow__node-desc", _s = "react-flow__edge-desc", rh = "react-flow__aria-live", ih = (e) => e.ariaLiveMessage, sh = (e) => e.ariaLabelConfig;
function ah({ rfId: e }) {
  const t = ce(ih);
  return h.jsx("div", { id: `${rh}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: oh, children: t });
}
function ch({ rfId: e, disableKeyboardA11y: t }) {
  const n = ce(sh);
  return h.jsxs(h.Fragment, { children: [h.jsx("div", { id: `${Ss}-${e}`, style: Br, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), h.jsx("div", { id: `${_s}-${e}`, style: Br, children: n["edge.a11yDescription.default"] }), !t && h.jsx(ah, { rfId: e })] });
}
const zn = Cn(({ position: e = "top-left", children: t, className: n, style: o, ...r }, i) => {
  const s = `${e}`.split("-");
  return h.jsx("div", { className: ge(["react-flow__panel", n, ...s]), style: o, ref: i, ...r, children: t });
});
zn.displayName = "Panel";
function lh({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : h.jsx(zn, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: h.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const uh = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, sn = (e) => e.id;
function dh(e, t) {
  return de(e.selectedNodes.map(sn), t.selectedNodes.map(sn)) && de(e.selectedEdges.map(sn), t.selectedEdges.map(sn));
}
function fh({ onSelectionChange: e }) {
  const t = fe(), { selectedNodes: n, selectedEdges: o } = ce(uh, dh);
  return ae(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((i) => i(r));
  }, [n, o, e]), null;
}
const hh = (e) => !!e.onSelectionChangeHandlers;
function ph({ onSelectionChange: e }) {
  const t = ce(hh);
  return e || t ? h.jsx(fh, { onSelectionChange: e }) : null;
}
const Es = [0, 0], gh = { x: 0, y: 0, zoom: 1 }, mh = [
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
], Fr = [...mh, "rfId"], yh = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), Yr = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: Ht,
  nodeOrigin: Es,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function xh(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: i, setNodeExtent: s, reset: a, setDefaultNodesAndEdges: l } = ce(yh, de), c = fe();
  ae(() => (l(e.defaultNodes, e.defaultEdges), () => {
    u.current = Yr, a();
  }), []);
  const u = se(Yr);
  return ae(
    () => {
      for (const d of Fr) {
        const f = e[d], p = u.current[d];
        f !== p && (typeof e[d] > "u" || (d === "nodes" ? t(f) : d === "edges" ? n(f) : d === "minZoom" ? o(f) : d === "maxZoom" ? r(f) : d === "translateExtent" ? i(f) : d === "nodeExtent" ? s(f) : d === "ariaLabelConfig" ? c.setState({ ariaLabelConfig: Jd(f) }) : d === "fitView" ? c.setState({ fitViewQueued: f }) : d === "fitViewOptions" ? c.setState({ fitViewOptions: f }) : c.setState({ [d]: f })));
      }
      u.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    Fr.map((d) => e[d])
  ), null;
}
function Wr() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function wh(e) {
  const [t, n] = oe(e === "system" ? null : e);
  return ae(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = Wr(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : Wr()?.matches ? "dark" : "light";
}
const Xr = typeof document < "u" ? document : null;
function Ft(e = null, t = { target: Xr, actInsideInputWithModifier: !0 }) {
  const [n, o] = oe(!1), r = se(!1), i = se(/* @__PURE__ */ new Set([])), [s, a] = xe(() => {
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
  return ae(() => {
    const l = t?.target ?? Xr, c = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const u = (p) => {
        if (r.current = p.ctrlKey || p.metaKey || p.shiftKey || p.altKey, (!r.current || r.current && !c) && os(p))
          return !1;
        const x = Zr(p.code, a);
        if (i.current.add(p[x]), qr(s, i.current, !1)) {
          const w = p.composedPath?.()?.[0] || p.target, y = w?.nodeName === "BUTTON" || w?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !y) && p.preventDefault(), o(!0);
        }
      }, d = (p) => {
        const g = Zr(p.code, a);
        qr(s, i.current, !0) ? (o(!1), i.current.clear()) : i.current.delete(p[g]), p.key === "Meta" && i.current.clear(), r.current = !1;
      }, f = () => {
        i.current.clear(), o(!1);
      };
      return l?.addEventListener("keydown", u), l?.addEventListener("keyup", d), window.addEventListener("blur", f), window.addEventListener("contextmenu", f), () => {
        l?.removeEventListener("keydown", u), l?.removeEventListener("keyup", d), window.removeEventListener("blur", f), window.removeEventListener("contextmenu", f);
      };
    }
  }, [e, o]), n;
}
function qr(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function Zr(e, t) {
  return t.includes(e) ? "code" : "key";
}
const vh = () => {
  const e = fe();
  return xe(() => ({
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
      const { width: o, height: r, minZoom: i, maxZoom: s, panZoom: a } = e.getState(), l = To(t, o, r, i, s, n?.padding ?? 0.1);
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
      return Nt(c, o, d, u);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: r, y: i } = o.getBoundingClientRect(), s = wt(t, n);
      return {
        x: s.x + r,
        y: s.y + i
      };
    }
  }), []);
};
function Ns(e, t) {
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
      bh(l, a);
    n.push(a);
  }
  return r.length && r.forEach((i) => {
    i.index !== void 0 ? n.splice(i.index, 0, { ...i.item }) : n.push({ ...i.item });
  }), n;
}
function bh(e, t) {
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
function Cs(e, t) {
  return Ns(e, t);
}
function ks(e, t) {
  return Ns(e, t);
}
function et(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function dt(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [r, i] of e) {
    const s = t.has(r);
    !(i.selected === void 0 && !s) && i.selected !== s && (n && (i.selected = s), o.push(et(i.id, s)));
  }
  return o;
}
function Ur({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((r) => [r.id, r]));
  for (const [r, i] of e.entries()) {
    const s = t.get(i.id), a = s?.internals?.userNode ?? s;
    a !== void 0 && a !== i && n.push({ id: i.id, item: i, type: "replace" }), a === void 0 && n.push({ item: i, type: "add", index: r });
  }
  for (const [r] of t)
    o.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function Gr(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const Sh = Ji();
function Is(e, t, n = {}) {
  return sf(e, t, {
    ...n,
    onError: n.onError ?? Sh
  });
}
const Kr = (e) => Yd(e), _h = (e) => Ui(e);
function Ms(e) {
  return Cn(e);
}
const Eh = typeof window < "u" ? ac : ae;
function Qr(e) {
  const [t, n] = oe(BigInt(0)), [o] = oe(() => Nh(() => n((r) => r + BigInt(1))));
  return Eh(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function Nh(e) {
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
const As = Eo(null);
function Ch({ children: e }) {
  const t = fe(), n = ye((a) => {
    const { nodes: l = [], setNodes: c, hasDefaultNodes: u, onNodesChange: d, nodeLookup: f, fitViewQueued: p, onNodesChangeMiddlewareMap: g } = t.getState();
    let x = l;
    for (const y of a)
      x = typeof y == "function" ? y(x) : y;
    let w = Ur({
      items: x,
      lookup: f
    });
    for (const y of g.values())
      w = y(w);
    u && c(x), w.length > 0 ? d?.(w) : p && window.requestAnimationFrame(() => {
      const { fitViewQueued: y, nodes: S, setNodes: m } = t.getState();
      y && m(S);
    });
  }, []), o = Qr(n), r = ye((a) => {
    const { edges: l = [], setEdges: c, hasDefaultEdges: u, onEdgesChange: d, edgeLookup: f } = t.getState();
    let p = l;
    for (const g of a)
      p = typeof g == "function" ? g(p) : g;
    u ? c(p) : d && d(Ur({
      items: p,
      lookup: f
    }));
  }, []), i = Qr(r), s = xe(() => ({ nodeQueue: o, edgeQueue: i }), []);
  return h.jsx(As.Provider, { value: s, children: e });
}
function kh() {
  const e = Xt(As);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Ih = (e) => !!e.panZoom;
function Bo() {
  const e = vh(), t = fe(), n = kh(), o = ce(Ih), r = xe(() => {
    const i = (d) => t.getState().nodeLookup.get(d), s = (d) => {
      n.nodeQueue.push(d);
    }, a = (d) => {
      n.edgeQueue.push(d);
    }, l = (d) => {
      const { nodeLookup: f, nodeOrigin: p } = t.getState(), g = Kr(d) ? d : f.get(d.id), x = g.parentId ? ts(g.position, g.measured, g.parentId, f, p) : g.position, w = {
        ...g,
        position: x,
        width: g.measured?.width ?? g.width,
        height: g.measured?.height ?? g.height
      };
      return xt(w);
    }, c = (d, f, p = { replace: !1 }) => {
      s((g) => g.map((x) => {
        if (x.id === d) {
          const w = typeof f == "function" ? f(x) : f;
          return p.replace && Kr(w) ? w : { ...x, ...w };
        }
        return x;
      }));
    }, u = (d, f, p = { replace: !1 }) => {
      a((g) => g.map((x) => {
        if (x.id === d) {
          const w = typeof f == "function" ? f(x) : f;
          return p.replace && _h(w) ? w : { ...x, ...w };
        }
        return x;
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
        n.nodeQueue.push((p) => [...p, ...f]);
      },
      addEdges: (d) => {
        const f = Array.isArray(d) ? d : [d];
        n.edgeQueue.push((p) => [...p, ...f]);
      },
      toObject: () => {
        const { nodes: d = [], edges: f = [], transform: p } = t.getState(), [g, x, w] = p;
        return {
          nodes: d.map((y) => ({ ...y })),
          edges: f.map((y) => ({ ...y })),
          viewport: {
            x: g,
            y: x,
            zoom: w
          }
        };
      },
      deleteElements: async ({ nodes: d = [], edges: f = [] }) => {
        const { nodes: p, edges: g, onNodesDelete: x, onEdgesDelete: w, triggerNodeChanges: y, triggerEdgeChanges: S, onDelete: m, onBeforeDelete: b } = t.getState(), { nodes: N, edges: _ } = await Ud({
          nodesToRemove: d,
          edgesToRemove: f,
          nodes: p,
          edges: g,
          onBeforeDelete: b
        }), C = _.length > 0, A = N.length > 0;
        if (C) {
          const D = _.map(Gr);
          w?.(_), S(D);
        }
        if (A) {
          const D = N.map(Gr);
          x?.(N), y(D);
        }
        return (A || C) && m?.({ nodes: N, edges: _ }), { deletedNodes: N, deletedEdges: _ };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (d, f = !0, p) => {
        const g = br(d), x = g ? d : l(d), w = p !== void 0;
        return x ? (p || t.getState().nodes).filter((y) => {
          const S = t.getState().nodeLookup.get(y.id);
          if (S && !g && (y.id === d.id || !S.internals.positionAbsolute))
            return !1;
          const m = xt(w ? y : S), b = Ot(m, x);
          return f && b > 0 || b >= m.width * m.height || b >= x.width * x.height;
        }) : [];
      },
      isNodeIntersecting: (d, f, p = !0) => {
        const x = br(d) ? d : l(d);
        if (!x)
          return !1;
        const w = Ot(x, f);
        return p && w > 0 || w >= f.width * f.height || w >= x.width * x.height;
      },
      updateNode: c,
      updateNodeData: (d, f, p = { replace: !1 }) => {
        c(d, (g) => {
          const x = typeof f == "function" ? f(g) : f;
          return p.replace ? { ...g, data: x } : { ...g, data: { ...g.data, ...x } };
        }, p);
      },
      updateEdge: u,
      updateEdgeData: (d, f, p = { replace: !1 }) => {
        u(d, (g) => {
          const x = typeof f == "function" ? f(g) : f;
          return p.replace ? { ...g, data: x } : { ...g, data: { ...g.data, ...x } };
        }, p);
      },
      getNodesBounds: (d) => {
        const { nodeLookup: f, nodeOrigin: p } = t.getState();
        return Wd(d, { nodeLookup: f, nodeOrigin: p });
      },
      getHandleConnections: ({ type: d, id: f, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}-${d}${f ? `-${f}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: d, handleId: f, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}${d ? f ? `-${d}-${f}` : `-${d}` : ""}`)?.values() ?? []),
      fitView: async (d) => {
        const f = t.getState().fitViewResolver ?? Qd();
        return t.setState({ fitViewQueued: !0, fitViewOptions: d, fitViewResolver: f }), n.nodeQueue.push((p) => [...p]), f.promise;
      }
    };
  }, []);
  return xe(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const Jr = (e) => e.selected, Mh = typeof window < "u" ? window : void 0;
function Ah({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = fe(), { deleteElements: o } = Bo(), r = Ft(e, { actInsideInputWithModifier: !1 }), i = Ft(t, { target: Mh });
  ae(() => {
    if (r) {
      const { edges: s, nodes: a } = n.getState();
      o({ nodes: a.filter(Jr), edges: s.filter(Jr) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), ae(() => {
    n.setState({ multiSelectionActive: i });
  }, [i]);
}
function Dh(e) {
  const t = fe();
  ae(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = zo(e.current);
      (o.height === 0 || o.width === 0) && t.getState().onError?.("004", ze.error004()), t.setState({ width: o.width || 500, height: o.height || 500 });
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
const Rn = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, Ph = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function jh({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: i = ot.Free, zoomOnDoubleClick: s = !0, panOnDrag: a = !0, defaultViewport: l, translateExtent: c, minZoom: u, maxZoom: d, zoomActivationKeyCode: f, preventScrolling: p = !0, children: g, noWheelClassName: x, noPanClassName: w, onViewportChange: y, isControlledViewport: S, paneClickDistance: m, selectionOnDrag: b }) {
  const N = fe(), _ = se(null), { userSelectionActive: C, lib: A, connectionInProgress: D } = ce(Ph, de), B = Ft(f), I = se();
  Dh(_);
  const $ = ye((H) => {
    y?.({ x: H[0], y: H[1], zoom: H[2] }), S || N.setState({ transform: H });
  }, [y, S]);
  return ae(() => {
    if (_.current) {
      I.current = zf({
        domNode: _.current,
        minZoom: u,
        maxZoom: d,
        translateExtent: c,
        viewport: l,
        onDraggingChange: (E) => N.setState((M) => M.paneDragging === E ? M : { paneDragging: E }),
        onPanZoomStart: (E, M) => {
          const { onViewportChangeStart: j, onMoveStart: P } = N.getState();
          P?.(E, M), j?.(M);
        },
        onPanZoom: (E, M) => {
          const { onViewportChange: j, onMove: P } = N.getState();
          P?.(E, M), j?.(M);
        },
        onPanZoomEnd: (E, M) => {
          const { onViewportChangeEnd: j, onMoveEnd: P } = N.getState();
          P?.(E, M), j?.(M);
        }
      });
      const { x: H, y: v, zoom: k } = I.current.getViewport();
      return N.setState({
        panZoom: I.current,
        transform: [H, v, k],
        domNode: _.current.closest(".react-flow")
      }), () => {
        I.current?.destroy();
      };
    }
  }, []), ae(() => {
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
      preventScrolling: p,
      noPanClassName: w,
      userSelectionActive: C,
      noWheelClassName: x,
      lib: A,
      onTransformChange: $,
      connectionInProgress: D,
      selectionOnDrag: b,
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
    p,
    w,
    C,
    x,
    A,
    $,
    D,
    b,
    m
  ]), h.jsx("div", { className: "react-flow__renderer", ref: _, style: Rn, children: g });
}
const $h = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function Th() {
  const { userSelectionActive: e, userSelectionRect: t } = ce($h, de);
  return e && t ? h.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const no = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, zh = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function Rh({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Vt.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: i, selectionOnDrag: s, onSelectionStart: a, onSelectionEnd: l, onPaneClick: c, onPaneContextMenu: u, onPaneScroll: d, onPaneMouseEnter: f, onPaneMouseMove: p, onPaneMouseLeave: g, children: x }) {
  const w = se(0), y = fe(), { userSelectionActive: S, elementsSelectable: m, dragging: b, connectionInProgress: N, panBy: _, autoPanSpeed: C } = ce(zh, de), A = m && (e || S), D = se(null), B = se(), I = se(/* @__PURE__ */ new Set()), $ = se(/* @__PURE__ */ new Set()), H = se(!1), v = se({ x: 0, y: 0 }), k = se(!1), E = (z) => {
    if (H.current || N) {
      H.current = !1;
      return;
    }
    c?.(z), y.getState().resetSelectedElements(), y.setState({ nodesSelectionActive: !1 });
  }, M = (z) => {
    if (Array.isArray(o) && o?.includes(2)) {
      z.preventDefault();
      return;
    }
    u?.(z);
  }, j = d ? (z) => d(z) : void 0, P = (z) => {
    H.current && (z.stopPropagation(), H.current = !1);
  }, L = (z) => {
    const { domNode: W, transform: te } = y.getState();
    if (B.current = W?.getBoundingClientRect(), !B.current)
      return;
    const ee = z.target === D.current;
    if (!ee && !!z.target.closest(".nokey") || !e || !(s && ee || t) || z.button !== 0 || !z.isPrimary)
      return;
    z.target?.setPointerCapture?.(z.pointerId), H.current = !1;
    const { x: re, y: R } = $e(z.nativeEvent, B.current), G = Nt({ x: re, y: R }, te);
    y.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: G.x,
        startY: G.y,
        x: re,
        y: R
      }
    }), ee || (z.stopPropagation(), z.preventDefault());
  };
  function O(z, W) {
    const { userSelectionRect: te } = y.getState();
    if (!te)
      return;
    const { transform: ee, nodeLookup: U, edgeLookup: J, connectionLookup: re, triggerNodeChanges: R, triggerEdgeChanges: G, defaultEdgeOptions: ue } = y.getState(), he = { x: te.startX, y: te.startY }, { x: Ne, y: Ce } = wt(he, ee), ke = {
      startX: he.x,
      startY: he.y,
      x: z < Ne ? z : Ne,
      y: W < Ce ? W : Ce,
      width: Math.abs(z - Ne),
      height: Math.abs(W - Ce)
    }, Je = I.current, Be = $.current;
    I.current = new Set(jo(U, ke, ee, n === Vt.Partial, !0).map((Se) => Se.id)), $.current = /* @__PURE__ */ new Set();
    const Fe = ue?.selectable ?? !0;
    for (const Se of I.current) {
      const Ie = re.get(Se);
      if (Ie)
        for (const { edgeId: Me } of Ie.values()) {
          const T = J.get(Me);
          T && (T.selectable ?? Fe) && $.current.add(Me);
        }
    }
    if (!Sr(Je, I.current)) {
      const Se = dt(U, I.current, !0);
      R(Se);
    }
    if (!Sr(Be, $.current)) {
      const Se = dt(J, $.current);
      G(Se);
    }
    y.setState({
      userSelectionRect: ke,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function V() {
    if (!r || !B.current)
      return;
    const [z, W] = $o(v.current, B.current, C);
    _({ x: z, y: W }).then((te) => {
      if (!H.current || !te) {
        w.current = requestAnimationFrame(V);
        return;
      }
      const { x: ee, y: U } = v.current;
      O(ee, U), w.current = requestAnimationFrame(V);
    });
  }
  const Y = () => {
    cancelAnimationFrame(w.current), w.current = 0, k.current = !1;
  };
  ae(() => () => Y(), []);
  const X = (z) => {
    const { userSelectionRect: W, transform: te, resetSelectedElements: ee } = y.getState();
    if (!B.current || !W)
      return;
    const { x: U, y: J } = $e(z.nativeEvent, B.current);
    v.current = { x: U, y: J };
    const re = wt({ x: W.startX, y: W.startY }, te);
    if (!H.current) {
      const R = t ? 0 : i;
      if (Math.hypot(U - re.x, J - re.y) <= R)
        return;
      ee(), a?.(z);
    }
    H.current = !0, k.current || (V(), k.current = !0), O(U, J);
  }, Q = (z) => {
    z.button === 0 && (z.target?.releasePointerCapture?.(z.pointerId), !S && z.target === D.current && y.getState().userSelectionRect && E?.(z), y.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), H.current && (l?.(z), y.setState({
      nodesSelectionActive: I.current.size > 0
    })), Y());
  }, ne = (z) => {
    z.target?.releasePointerCapture?.(z.pointerId), Y();
  }, Z = o === !0 || Array.isArray(o) && o.includes(0);
  return h.jsxs("div", { className: ge(["react-flow__pane", { draggable: Z, dragging: b, selection: e }]), onClick: A ? void 0 : no(E, D), onContextMenu: no(M, D), onWheel: no(j, D), onPointerEnter: A ? void 0 : f, onPointerMove: A ? X : p, onPointerUp: A ? Q : void 0, onPointerCancel: A ? ne : void 0, onPointerDownCapture: A ? L : void 0, onClickCapture: A ? P : void 0, onPointerLeave: g, ref: D, style: Rn, children: [x, h.jsx(Th, {})] });
}
function vo({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: i, multiSelectionActive: s, nodeLookup: a, onError: l } = t.getState(), c = a.get(e);
  if (!c) {
    l?.("012", ze.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), c.selected ? (n || c.selected && s) && (i({ nodes: [c], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function Ds({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: i, nodeClickDistance: s }) {
  const a = fe(), [l, c] = oe(!1), u = se();
  return ae(() => {
    u.current = Sf({
      getStoreItems: () => a.getState(),
      onNodeMouseDown: (d) => {
        vo({
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
  }, []), ae(() => {
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
const Lh = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function Ps() {
  const e = fe();
  return ye((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: i, nodesDraggable: s, onError: a, updateNodePositions: l, nodeLookup: c, nodeOrigin: u } = e.getState(), d = /* @__PURE__ */ new Map(), f = Lh(s), p = r ? i[0] : 5, g = r ? i[1] : 5, x = n.direction.x * p * n.factor, w = n.direction.y * g * n.factor;
    for (const [, y] of c) {
      if (!f(y))
        continue;
      let S = {
        x: y.internals.positionAbsolute.x + x,
        y: y.internals.positionAbsolute.y + w
      };
      r && (S = Kt(S, i));
      const { position: m, positionAbsolute: b } = Gi({
        nodeId: y.id,
        nextPosition: S,
        nodeLookup: c,
        nodeExtent: o,
        nodeOrigin: u,
        onError: a
      });
      y.position = m, y.internals.positionAbsolute = b, d.set(y.id, y);
    }
    l(d);
  }, []);
}
const Fo = Eo(null), Hh = Fo.Provider;
Fo.Consumer;
const js = () => Xt(Fo), Vh = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), Oh = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: i, connection: s } = o, { fromHandle: a, toHandle: l, isValid: c } = s, u = l?.nodeId === e && l?.id === t && l?.type === n;
  return {
    connectingFrom: a?.nodeId === e && a?.id === t && a?.type === n,
    connectingTo: u,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: i === mt.Strict ? a?.type !== n : e !== a?.nodeId || t !== a?.id,
    connectionInProcess: !!a,
    clickConnectionInProcess: !!r,
    valid: u && c
  };
};
function Bh({ type: e = "source", position: t = K.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: i = !0, id: s, onConnect: a, children: l, className: c, onMouseDown: u, onTouchStart: d, ...f }, p) {
  const g = s || null, x = e === "target", w = fe(), y = js(), { connectOnClick: S, noPanClassName: m, rfId: b } = ce(Vh, de), { connectingFrom: N, connectingTo: _, clickConnecting: C, isPossibleEndHandle: A, connectionInProcess: D, clickConnectionInProcess: B, valid: I } = ce(Oh(y, g, e), de);
  y || w.getState().onError?.("010", ze.error010());
  const $ = (k) => {
    const { defaultEdgeOptions: E, onConnect: M, hasDefaultEdges: j } = w.getState(), P = {
      ...E,
      ...k
    };
    if (j) {
      const { edges: L, setEdges: O, onError: V } = w.getState();
      O(Is(P, L, { onError: V }));
    }
    M?.(P), a?.(P);
  }, H = (k) => {
    if (!y)
      return;
    const E = rs(k.nativeEvent);
    if (r && (E && k.button === 0 || !E)) {
      const M = w.getState();
      wo.onPointerDown(k.nativeEvent, {
        handleDomNode: k.currentTarget,
        autoPanOnConnect: M.autoPanOnConnect,
        connectionMode: M.connectionMode,
        connectionRadius: M.connectionRadius,
        domNode: M.domNode,
        nodeLookup: M.nodeLookup,
        lib: M.lib,
        isTarget: x,
        handleId: g,
        nodeId: y,
        flowId: M.rfId,
        panBy: M.panBy,
        cancelConnection: M.cancelConnection,
        onConnectStart: M.onConnectStart,
        onConnectEnd: (...j) => w.getState().onConnectEnd?.(...j),
        updateConnection: M.updateConnection,
        onConnect: $,
        isValidConnection: n || ((...j) => w.getState().isValidConnection?.(...j) ?? !0),
        getTransform: () => w.getState().transform,
        getFromHandle: () => w.getState().connection.fromHandle,
        autoPanSpeed: M.autoPanSpeed,
        dragThreshold: M.connectionDragThreshold
      });
    }
    E ? u?.(k) : d?.(k);
  }, v = (k) => {
    const { onClickConnectStart: E, onClickConnectEnd: M, connectionClickStartHandle: j, connectionMode: P, isValidConnection: L, lib: O, rfId: V, nodeLookup: Y, connection: X } = w.getState();
    if (!y || !j && !r)
      return;
    if (!j) {
      E?.(k.nativeEvent, { nodeId: y, handleId: g, handleType: e }), w.setState({ connectionClickStartHandle: { nodeId: y, type: e, id: g } });
      return;
    }
    const Q = ns(k.target), ne = n || L, { connection: Z, isValid: z } = wo.isValid(k.nativeEvent, {
      handle: {
        nodeId: y,
        id: g,
        type: e
      },
      connectionMode: P,
      fromNodeId: j.nodeId,
      fromHandleId: j.id || null,
      fromType: j.type,
      isValidConnection: ne,
      flowId: V,
      doc: Q,
      lib: O,
      nodeLookup: Y
    });
    z && Z && $(Z);
    const W = structuredClone(X);
    delete W.inProgress, W.toPosition = W.toHandle ? W.toHandle.position : null, M?.(k, W), w.setState({ connectionClickStartHandle: null });
  };
  return h.jsx("div", { "data-handleid": g, "data-nodeid": y, "data-handlepos": t, "data-id": `${b}-${y}-${g}-${e}`, className: ge([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    m,
    c,
    {
      source: !x,
      target: x,
      connectable: o,
      connectablestart: r,
      connectableend: i,
      clickconnecting: C,
      connectingfrom: N,
      connectingto: _,
      valid: I,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!D || A) && (D || B ? i : r)
    }
  ]), onMouseDown: H, onTouchStart: H, onClick: S ? v : void 0, ref: p, ...f, children: l });
}
const bt = pe(Ms(Bh));
function Fh({ data: e, isConnectable: t, sourcePosition: n = K.Bottom }) {
  return h.jsxs(h.Fragment, { children: [e?.label, h.jsx(bt, { type: "source", position: n, isConnectable: t })] });
}
function Yh({ data: e, isConnectable: t, targetPosition: n = K.Top, sourcePosition: o = K.Bottom }) {
  return h.jsxs(h.Fragment, { children: [h.jsx(bt, { type: "target", position: n, isConnectable: t }), e?.label, h.jsx(bt, { type: "source", position: o, isConnectable: t })] });
}
function Wh() {
  return null;
}
function Xh({ data: e, isConnectable: t, targetPosition: n = K.Top }) {
  return h.jsxs(h.Fragment, { children: [h.jsx(bt, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const _n = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, ei = {
  input: Fh,
  default: Yh,
  output: Xh,
  group: Wh
};
function qh(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const Zh = (e) => {
  const { width: t, height: n, x: o, y: r } = Gt(e.nodeLookup, {
    filter: (i) => !!i.selected
  });
  return {
    width: je(t) ? t : null,
    height: je(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function Uh({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = fe(), { width: r, height: i, transformString: s, userSelectionActive: a } = ce(Zh, de), l = Ps(), c = se(null);
  ae(() => {
    n || c.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const u = !a && r !== null && i !== null;
  if (Ds({
    nodeRef: c,
    disabled: !u
  }), !u)
    return null;
  const d = e ? (p) => {
    const g = o.getState().nodes.filter((x) => x.selected);
    e(p, g);
  } : void 0, f = (p) => {
    Object.prototype.hasOwnProperty.call(_n, p.key) && (p.preventDefault(), l({
      direction: _n[p.key],
      factor: p.shiftKey ? 4 : 1
    }));
  };
  return h.jsx("div", { className: ge(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: s
  }, children: h.jsx("div", { ref: c, className: "react-flow__nodesselection-rect", onContextMenu: d, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : f, style: {
    width: r,
    height: i
  } }) });
}
const ti = typeof window < "u" ? window : void 0, Gh = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function $s({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, paneClickDistance: a, deleteKeyCode: l, selectionKeyCode: c, selectionOnDrag: u, selectionMode: d, onSelectionStart: f, onSelectionEnd: p, multiSelectionKeyCode: g, panActivationKeyCode: x, zoomActivationKeyCode: w, elementsSelectable: y, zoomOnScroll: S, zoomOnPinch: m, panOnScroll: b, panOnScrollSpeed: N, panOnScrollMode: _, zoomOnDoubleClick: C, panOnDrag: A, autoPanOnSelection: D, defaultViewport: B, translateExtent: I, minZoom: $, maxZoom: H, preventScrolling: v, onSelectionContextMenu: k, noWheelClassName: E, noPanClassName: M, disableKeyboardA11y: j, onViewportChange: P, isControlledViewport: L }) {
  const { nodesSelectionActive: O, userSelectionActive: V } = ce(Gh, de), Y = Ft(c, { target: ti }), X = Ft(x, { target: ti }), Q = X || A, ne = X || b, Z = u && Q !== !0, z = Y || V || Z;
  return Ah({ deleteKeyCode: l, multiSelectionKeyCode: g }), h.jsx(jh, { onPaneContextMenu: i, elementsSelectable: y, zoomOnScroll: S, zoomOnPinch: m, panOnScroll: ne, panOnScrollSpeed: N, panOnScrollMode: _, zoomOnDoubleClick: C, panOnDrag: !Y && Q, defaultViewport: B, translateExtent: I, minZoom: $, maxZoom: H, zoomActivationKeyCode: w, preventScrolling: v, noWheelClassName: E, noPanClassName: M, onViewportChange: P, isControlledViewport: L, paneClickDistance: a, selectionOnDrag: Z, children: h.jsxs(Rh, { onSelectionStart: f, onSelectionEnd: p, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, panOnDrag: Q, autoPanOnSelection: D, isSelecting: !!z, selectionMode: d, selectionKeyPressed: Y, paneClickDistance: a, selectionOnDrag: Z, children: [e, O && h.jsx(Uh, { onSelectionContextMenu: k, noPanClassName: M, disableKeyboardA11y: j })] }) });
}
$s.displayName = "FlowRenderer";
const Kh = pe($s), Qh = (e) => (t) => e ? jo(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function Jh(e) {
  return ce(ye(Qh(e), [e]), de);
}
const ep = (e) => e.updateNodeInternals;
function tp() {
  const e = ce(ep), [t] = oe(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
  return ae(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function np({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = fe(), i = se(null), s = se(null), a = se(e.sourcePosition), l = se(e.targetPosition), c = se(t), u = n && !!e.internals.handleBounds;
  return ae(() => {
    i.current && !e.hidden && (!u || s.current !== i.current) && (s.current && o?.unobserve(s.current), o?.observe(i.current), s.current = i.current);
  }, [u, e.hidden]), ae(() => () => {
    s.current && (o?.unobserve(s.current), s.current = null);
  }, []), ae(() => {
    if (i.current) {
      const d = c.current !== t, f = a.current !== e.sourcePosition, p = l.current !== e.targetPosition;
      (d || f || p) && (c.current = t, a.current = e.sourcePosition, l.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: i.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), i;
}
function op({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: i, onDoubleClick: s, nodesDraggable: a, elementsSelectable: l, nodesConnectable: c, nodesFocusable: u, resizeObserver: d, noDragClassName: f, noPanClassName: p, disableKeyboardA11y: g, rfId: x, nodeTypes: w, nodeClickDistance: y, onError: S }) {
  const { node: m, internals: b, isParent: N } = ce((z) => {
    const W = z.nodeLookup.get(e), te = z.parentLookup.has(e);
    return {
      node: W,
      internals: W.internals,
      isParent: te
    };
  }, de);
  let _ = m.type || "default", C = w?.[_] || ei[_];
  C === void 0 && (S?.("003", ze.error003(_)), _ = "default", C = w?.default || ei.default);
  const A = !!(m.draggable || a && typeof m.draggable > "u"), D = !!(m.selectable || l && typeof m.selectable > "u"), B = !!(m.connectable || c && typeof m.connectable > "u"), I = !!(m.focusable || u && typeof m.focusable > "u"), $ = fe(), H = es(m), v = np({ node: m, nodeType: _, hasDimensions: H, resizeObserver: d }), k = Ds({
    nodeRef: v,
    disabled: m.hidden || !A,
    noDragClassName: f,
    handleSelector: m.dragHandle,
    nodeId: e,
    isSelectable: D,
    nodeClickDistance: y
  }), E = Ps();
  if (m.hidden)
    return null;
  const M = Ze(m), j = qh(m), P = D || A || t || n || o || r, L = n ? (z) => n(z, { ...b.userNode }) : void 0, O = o ? (z) => o(z, { ...b.userNode }) : void 0, V = r ? (z) => r(z, { ...b.userNode }) : void 0, Y = i ? (z) => i(z, { ...b.userNode }) : void 0, X = s ? (z) => s(z, { ...b.userNode }) : void 0, Q = (z) => {
    const { selectNodesOnDrag: W, nodeDragThreshold: te } = $.getState();
    D && (!W || !A || te > 0) && vo({
      id: e,
      store: $,
      nodeRef: v
    }), t && t(z, { ...b.userNode });
  }, ne = (z) => {
    if (!(os(z.nativeEvent) || g)) {
      if (Wi.includes(z.key) && D) {
        const W = z.key === "Escape";
        vo({
          id: e,
          store: $,
          unselect: W,
          nodeRef: v
        });
      } else if (A && m.selected && Object.prototype.hasOwnProperty.call(_n, z.key)) {
        z.preventDefault();
        const { ariaLabelConfig: W } = $.getState();
        $.setState({
          ariaLiveMessage: W["node.a11yDescription.ariaLiveMessage"]({
            direction: z.key.replace("Arrow", "").toLowerCase(),
            x: ~~b.positionAbsolute.x,
            y: ~~b.positionAbsolute.y
          })
        }), E({
          direction: _n[z.key],
          factor: z.shiftKey ? 4 : 1
        });
      }
    }
  }, Z = () => {
    if (g || !v.current?.matches(":focus-visible"))
      return;
    const { transform: z, width: W, height: te, autoPanOnNodeFocus: ee, setCenter: U } = $.getState();
    if (!ee)
      return;
    jo(/* @__PURE__ */ new Map([[e, m]]), { x: 0, y: 0, width: W, height: te }, z, !0).length > 0 || U(m.position.x + M.width / 2, m.position.y + M.height / 2, {
      zoom: z[2]
    });
  };
  return h.jsx("div", { className: ge([
    "react-flow__node",
    `react-flow__node-${_}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [p]: A
    },
    m.className,
    {
      selected: m.selected,
      selectable: D,
      parent: N,
      draggable: A,
      dragging: k
    }
  ]), ref: v, style: {
    zIndex: b.z,
    transform: `translate(${b.positionAbsolute.x}px,${b.positionAbsolute.y}px)`,
    pointerEvents: P ? "all" : "none",
    visibility: H ? "visible" : "hidden",
    ...m.style,
    ...j
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: L, onMouseMove: O, onMouseLeave: V, onContextMenu: Y, onClick: Q, onDoubleClick: X, onKeyDown: I ? ne : void 0, tabIndex: I ? 0 : void 0, onFocus: I ? Z : void 0, role: m.ariaRole ?? (I ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": g ? void 0 : `${Ss}-${x}`, "aria-label": m.ariaLabel, ...m.domAttributes, children: h.jsx(Hh, { value: e, children: h.jsx(C, { id: e, data: m.data, type: _, positionAbsoluteX: b.positionAbsolute.x, positionAbsoluteY: b.positionAbsolute.y, selected: m.selected ?? !1, selectable: D, draggable: A, deletable: m.deletable ?? !0, isConnectable: B, sourcePosition: m.sourcePosition, targetPosition: m.targetPosition, dragging: k, dragHandle: m.dragHandle, zIndex: b.z, parentId: m.parentId, ...M }) }) });
}
var rp = pe(op);
const ip = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function Ts(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: i } = ce(ip, de), s = Jh(e.onlyRenderVisibleElements), a = tp();
  return h.jsx("div", { className: "react-flow__nodes", style: Rn, children: s.map((l) => (
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
    h.jsx(rp, { id: l, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: a, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: i }, l)
  )) });
}
Ts.displayName = "NodeRenderer";
const sp = pe(Ts);
function ap(e) {
  return ce(ye((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const i = n.nodeLookup.get(r.source), s = n.nodeLookup.get(r.target);
        i && s && nf({
          sourceNode: i,
          targetNode: s,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && o.push(r.id);
      }
    return o;
  }, [e]), de);
}
const cp = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return h.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, lp = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return h.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, ni = {
  [bn.Arrow]: cp,
  [bn.ArrowClosed]: lp
};
function up(e) {
  const t = fe();
  return xe(() => Object.prototype.hasOwnProperty.call(ni, e) ? ni[e] : (t.getState().onError?.("009", ze.error009(e)), null), [e]);
}
const dp = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: i = "strokeWidth", strokeWidth: s, orient: a = "auto-start-reverse" }) => {
  const l = up(t);
  return l ? h.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: i, orient: a, refX: "0", refY: "0", children: h.jsx(l, { color: n, strokeWidth: s }) }) : null;
}, zs = ({ defaultColor: e, rfId: t }) => {
  const n = ce((i) => i.edges), o = ce((i) => i.defaultEdgeOptions), r = xe(() => df(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? h.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: h.jsx("defs", { children: r.map((i) => h.jsx(dp, { id: i.id, type: i.type, color: i.color, width: i.width, height: i.height, markerUnits: i.markerUnits, strokeWidth: i.strokeWidth, orient: i.orient }, i.id)) }) }) : null;
};
zs.displayName = "MarkerDefinitions";
var fp = pe(zs);
function Rs({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: i, labelBgPadding: s = [2, 4], labelBgBorderRadius: a = 2, children: l, className: c, ...u }) {
  const [d, f] = oe({ x: 1, y: 0, width: 0, height: 0 }), p = ge(["react-flow__edge-textwrapper", c]), g = se(null);
  return ae(() => {
    if (g.current) {
      const x = g.current.getBBox();
      f({
        x: x.x,
        y: x.y,
        width: x.width,
        height: x.height
      });
    }
  }, [n]), n ? h.jsxs("g", { transform: `translate(${e - d.width / 2} ${t - d.height / 2})`, className: p, visibility: d.width ? "visible" : "hidden", ...u, children: [r && h.jsx("rect", { width: d.width + 2 * s[0], x: -s[0], y: -s[1], height: d.height + 2 * s[1], className: "react-flow__edge-textbg", style: i, rx: a, ry: a }), h.jsx("text", { className: "react-flow__edge-text", y: d.height / 2, dy: "0.3em", ref: g, style: o, children: n }), l] }) : null;
}
Rs.displayName = "EdgeText";
const hp = pe(Rs);
function Ln({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: a, labelBgBorderRadius: l, interactionWidth: c = 20, ...u }) {
  return h.jsxs(h.Fragment, { children: [h.jsx("path", { ...u, d: e, fill: "none", className: ge(["react-flow__edge-path", u.className]) }), c ? h.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: c, className: "react-flow__edge-interaction" }) : null, o && je(t) && je(n) ? h.jsx(hp, { x: t, y: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: a, labelBgBorderRadius: l }) : null] });
}
function oi({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === K.Left || e === K.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function Ls({ sourceX: e, sourceY: t, sourcePosition: n = K.Bottom, targetX: o, targetY: r, targetPosition: i = K.Top }) {
  const [s, a] = oi({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [l, c] = oi({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [u, d, f, p] = is({
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
    p
  ];
}
function Hs(e) {
  return pe(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: a, label: l, labelStyle: c, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: p, style: g, markerEnd: x, markerStart: w, interactionWidth: y }) => {
    const [S, m, b] = Ls({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: a
    }), N = e.isInternal ? void 0 : t;
    return h.jsx(Ln, { id: N, path: S, labelX: m, labelY: b, label: l, labelStyle: c, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: p, style: g, markerEnd: x, markerStart: w, interactionWidth: y });
  });
}
const pp = Hs({ isInternal: !1 }), Vs = Hs({ isInternal: !0 });
pp.displayName = "SimpleBezierEdge";
Vs.displayName = "SimpleBezierEdgeInternal";
function Os(e) {
  return pe(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: u, labelBgBorderRadius: d, style: f, sourcePosition: p = K.Bottom, targetPosition: g = K.Top, markerEnd: x, markerStart: w, pathOptions: y, interactionWidth: S }) => {
    const [m, b, N] = mo({
      sourceX: n,
      sourceY: o,
      sourcePosition: p,
      targetX: r,
      targetY: i,
      targetPosition: g,
      borderRadius: y?.borderRadius,
      offset: y?.offset,
      stepPosition: y?.stepPosition
    }), _ = e.isInternal ? void 0 : t;
    return h.jsx(Ln, { id: _, path: m, labelX: b, labelY: N, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: u, labelBgBorderRadius: d, style: f, markerEnd: x, markerStart: w, interactionWidth: S });
  });
}
const Bs = Os({ isInternal: !1 }), Fs = Os({ isInternal: !0 });
Bs.displayName = "SmoothStepEdge";
Fs.displayName = "SmoothStepEdgeInternal";
function Ys(e) {
  return pe(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return h.jsx(Bs, { ...n, id: o, pathOptions: xe(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const gp = Ys({ isInternal: !1 }), Ws = Ys({ isInternal: !0 });
gp.displayName = "StepEdge";
Ws.displayName = "StepEdgeInternal";
function Xs(e) {
  return pe(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: u, labelBgBorderRadius: d, style: f, markerEnd: p, markerStart: g, interactionWidth: x }) => {
    const [w, y, S] = cs({ sourceX: n, sourceY: o, targetX: r, targetY: i }), m = e.isInternal ? void 0 : t;
    return h.jsx(Ln, { id: m, path: w, labelX: y, labelY: S, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: u, labelBgBorderRadius: d, style: f, markerEnd: p, markerStart: g, interactionWidth: x });
  });
}
const mp = Xs({ isInternal: !1 }), qs = Xs({ isInternal: !0 });
mp.displayName = "StraightEdge";
qs.displayName = "StraightEdgeInternal";
function Zs(e) {
  return pe(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s = K.Bottom, targetPosition: a = K.Top, label: l, labelStyle: c, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: p, style: g, markerEnd: x, markerStart: w, pathOptions: y, interactionWidth: S }) => {
    const [m, b, N] = ss({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: a,
      curvature: y?.curvature
    }), _ = e.isInternal ? void 0 : t;
    return h.jsx(Ln, { id: _, path: m, labelX: b, labelY: N, label: l, labelStyle: c, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: p, style: g, markerEnd: x, markerStart: w, interactionWidth: S });
  });
}
const yp = Zs({ isInternal: !1 }), Us = Zs({ isInternal: !0 });
yp.displayName = "BezierEdge";
Us.displayName = "BezierEdgeInternal";
const ri = {
  default: Us,
  straight: qs,
  step: Ws,
  smoothstep: Fs,
  simplebezier: Vs
}, ii = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, xp = (e, t, n) => n === K.Left ? e - t : n === K.Right ? e + t : e, wp = (e, t, n) => n === K.Top ? e - t : n === K.Bottom ? e + t : e, si = "react-flow__edgeupdater";
function ai({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: i, onMouseOut: s, type: a }) {
  return h.jsx("circle", { onMouseDown: r, onMouseEnter: i, onMouseOut: s, className: ge([si, `${si}-${a}`]), cx: xp(t, o, e), cy: wp(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function vp({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: i, targetY: s, sourcePosition: a, targetPosition: l, onReconnect: c, onReconnectStart: u, onReconnectEnd: d, setReconnecting: f, setUpdateHover: p }) {
  const g = fe(), x = (b, N) => {
    if (b.button !== 0)
      return;
    const { autoPanOnConnect: _, domNode: C, connectionMode: A, connectionRadius: D, lib: B, onConnectStart: I, cancelConnection: $, nodeLookup: H, rfId: v, panBy: k, updateConnection: E } = g.getState(), M = N.type === "target", j = (O, V) => {
      f(!1), d?.(O, n, N.type, V);
    }, P = (O) => c?.(n, O), L = (O, V) => {
      f(!0), u?.(b, n, N.type), I?.(O, V);
    };
    wo.onPointerDown(b.nativeEvent, {
      autoPanOnConnect: _,
      connectionMode: A,
      connectionRadius: D,
      domNode: C,
      handleId: N.id,
      nodeId: N.nodeId,
      nodeLookup: H,
      isTarget: M,
      edgeUpdaterType: N.type,
      lib: B,
      flowId: v,
      cancelConnection: $,
      panBy: k,
      isValidConnection: (...O) => g.getState().isValidConnection?.(...O) ?? !0,
      onConnect: P,
      onConnectStart: L,
      onConnectEnd: (...O) => g.getState().onConnectEnd?.(...O),
      onReconnectEnd: j,
      updateConnection: E,
      getTransform: () => g.getState().transform,
      getFromHandle: () => g.getState().connection.fromHandle,
      dragThreshold: g.getState().connectionDragThreshold,
      handleDomNode: b.currentTarget
    });
  }, w = (b) => x(b, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), y = (b) => x(b, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), S = () => p(!0), m = () => p(!1);
  return h.jsxs(h.Fragment, { children: [(e === !0 || e === "source") && h.jsx(ai, { position: a, centerX: o, centerY: r, radius: t, onMouseDown: w, onMouseEnter: S, onMouseOut: m, type: "source" }), (e === !0 || e === "target") && h.jsx(ai, { position: l, centerX: i, centerY: s, radius: t, onMouseDown: y, onMouseEnter: S, onMouseOut: m, type: "target" })] });
}
function bp({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: i, onContextMenu: s, onMouseEnter: a, onMouseMove: l, onMouseLeave: c, reconnectRadius: u, onReconnect: d, onReconnectStart: f, onReconnectEnd: p, rfId: g, edgeTypes: x, noPanClassName: w, onError: y, disableKeyboardA11y: S }) {
  let m = ce((U) => U.edgeLookup.get(e));
  const b = ce((U) => U.defaultEdgeOptions);
  m = b ? { ...b, ...m } : m;
  let N = m.type || "default", _ = x?.[N] || ri[N];
  _ === void 0 && (y?.("011", ze.error011(N)), N = "default", _ = x?.default || ri.default);
  const C = !!(m.focusable || t && typeof m.focusable > "u"), A = typeof d < "u" && (m.reconnectable || n && typeof m.reconnectable > "u"), D = !!(m.selectable || o && typeof m.selectable > "u"), B = se(null), [I, $] = oe(!1), [H, v] = oe(!1), k = fe(), { zIndex: E, sourceX: M, sourceY: j, targetX: P, targetY: L, sourcePosition: O, targetPosition: V } = ce(ye((U) => {
    const J = U.nodeLookup.get(m.source), re = U.nodeLookup.get(m.target);
    if (!J || !re)
      return {
        zIndex: m.zIndex,
        ...ii
      };
    const R = uf({
      id: e,
      sourceNode: J,
      targetNode: re,
      sourceHandle: m.sourceHandle || null,
      targetHandle: m.targetHandle || null,
      connectionMode: U.connectionMode,
      onError: y
    });
    return {
      zIndex: tf({
        selected: m.selected,
        zIndex: m.zIndex,
        sourceNode: J,
        targetNode: re,
        elevateOnSelect: U.elevateEdgesOnSelect,
        zIndexMode: U.zIndexMode
      }),
      ...R || ii
    };
  }, [m.source, m.target, m.sourceHandle, m.targetHandle, m.selected, m.zIndex]), de), Y = xe(() => m.markerStart ? `url('#${yo(m.markerStart, g)}')` : void 0, [m.markerStart, g]), X = xe(() => m.markerEnd ? `url('#${yo(m.markerEnd, g)}')` : void 0, [m.markerEnd, g]);
  if (m.hidden || M === null || j === null || P === null || L === null)
    return null;
  const Q = (U) => {
    const { addSelectedEdges: J, unselectNodesAndEdges: re, multiSelectionActive: R } = k.getState();
    D && (k.setState({ nodesSelectionActive: !1 }), m.selected && R ? (re({ nodes: [], edges: [m] }), B.current?.blur()) : J([e])), r && r(U, m);
  }, ne = i ? (U) => {
    i(U, { ...m });
  } : void 0, Z = s ? (U) => {
    s(U, { ...m });
  } : void 0, z = a ? (U) => {
    a(U, { ...m });
  } : void 0, W = l ? (U) => {
    l(U, { ...m });
  } : void 0, te = c ? (U) => {
    c(U, { ...m });
  } : void 0, ee = (U) => {
    if (!S && Wi.includes(U.key) && D) {
      const { unselectNodesAndEdges: J, addSelectedEdges: re } = k.getState();
      U.key === "Escape" ? (B.current?.blur(), J({ edges: [m] })) : re([e]);
    }
  };
  return h.jsx("svg", { style: { zIndex: E }, children: h.jsxs("g", { className: ge([
    "react-flow__edge",
    `react-flow__edge-${N}`,
    m.className,
    w,
    {
      selected: m.selected,
      animated: m.animated,
      inactive: !D && !r,
      updating: I,
      selectable: D
    }
  ]), onClick: Q, onDoubleClick: ne, onContextMenu: Z, onMouseEnter: z, onMouseMove: W, onMouseLeave: te, onKeyDown: C ? ee : void 0, tabIndex: C ? 0 : void 0, role: m.ariaRole ?? (C ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": m.ariaLabel === null ? void 0 : m.ariaLabel || `Edge from ${m.source} to ${m.target}`, "aria-describedby": C ? `${_s}-${g}` : void 0, ref: B, ...m.domAttributes, children: [!H && h.jsx(_, { id: e, source: m.source, target: m.target, type: m.type, selected: m.selected, animated: m.animated, selectable: D, deletable: m.deletable ?? !0, label: m.label, labelStyle: m.labelStyle, labelShowBg: m.labelShowBg, labelBgStyle: m.labelBgStyle, labelBgPadding: m.labelBgPadding, labelBgBorderRadius: m.labelBgBorderRadius, sourceX: M, sourceY: j, targetX: P, targetY: L, sourcePosition: O, targetPosition: V, data: m.data, style: m.style, sourceHandleId: m.sourceHandle, targetHandleId: m.targetHandle, markerStart: Y, markerEnd: X, pathOptions: "pathOptions" in m ? m.pathOptions : void 0, interactionWidth: m.interactionWidth }), A && h.jsx(vp, { edge: m, isReconnectable: A, reconnectRadius: u, onReconnect: d, onReconnectStart: f, onReconnectEnd: p, sourceX: M, sourceY: j, targetX: P, targetY: L, sourcePosition: O, targetPosition: V, setUpdateHover: $, setReconnecting: v })] }) });
}
var Sp = pe(bp);
const _p = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function Gs({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: i, onEdgeContextMenu: s, onEdgeMouseEnter: a, onEdgeMouseMove: l, onEdgeMouseLeave: c, onEdgeClick: u, reconnectRadius: d, onEdgeDoubleClick: f, onReconnectStart: p, onReconnectEnd: g, disableKeyboardA11y: x }) {
  const { edgesFocusable: w, edgesReconnectable: y, elementsSelectable: S, onError: m } = ce(_p, de), b = ap(t);
  return h.jsxs("div", { className: "react-flow__edges", children: [h.jsx(fp, { defaultColor: e, rfId: n }), b.map((N) => h.jsx(Sp, { id: N, edgesFocusable: w, edgesReconnectable: y, elementsSelectable: S, noPanClassName: r, onReconnect: i, onContextMenu: s, onMouseEnter: a, onMouseMove: l, onMouseLeave: c, onClick: u, reconnectRadius: d, onDoubleClick: f, onReconnectStart: p, onReconnectEnd: g, rfId: n, onError: m, edgeTypes: o, disableKeyboardA11y: x }, N))] });
}
Gs.displayName = "EdgeRenderer";
const Ep = pe(Gs), Np = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function Cp({ children: e }) {
  const t = ce(Np);
  return h.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function kp(e) {
  const t = Bo(), n = se(!1);
  ae(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Ip = (e) => e.panZoom?.syncViewport;
function Mp(e) {
  const t = ce(Ip), n = fe();
  return ae(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function Ap(e) {
  return e.connection.inProgress ? { ...e.connection, to: Nt(e.connection.to, e.transform) } : { ...e.connection };
}
function Dp(e) {
  return Ap;
}
function Pp(e) {
  const t = Dp();
  return ce(t, de);
}
const jp = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function $p({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: i, height: s, isValid: a, inProgress: l } = ce(jp, de);
  return !(i && r && l) ? null : h.jsx("svg", { style: e, width: i, height: s, className: "react-flow__connectionline react-flow__container", children: h.jsx("g", { className: ge(["react-flow__connection", Zi(a)]), children: h.jsx(Ks, { style: t, type: n, CustomComponent: o, isValid: a }) }) });
}
const Ks = ({ style: e, type: t = Ke.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: i, fromNode: s, fromHandle: a, fromPosition: l, to: c, toNode: u, toHandle: d, toPosition: f, pointer: p } = Pp();
  if (!r)
    return;
  if (n)
    return h.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: s, fromHandle: a, fromX: i.x, fromY: i.y, toX: c.x, toY: c.y, fromPosition: l, toPosition: f, connectionStatus: Zi(o), toNode: u, toHandle: d, pointer: p });
  let g = "";
  const x = {
    sourceX: i.x,
    sourceY: i.y,
    sourcePosition: l,
    targetX: c.x,
    targetY: c.y,
    targetPosition: f
  };
  switch (t) {
    case Ke.Bezier:
      [g] = ss(x);
      break;
    case Ke.SimpleBezier:
      [g] = Ls(x);
      break;
    case Ke.Step:
      [g] = mo({
        ...x,
        borderRadius: 0
      });
      break;
    case Ke.SmoothStep:
      [g] = mo(x);
      break;
    default:
      [g] = cs(x);
  }
  return h.jsx("path", { d: g, fill: "none", className: "react-flow__connection-path", style: e });
};
Ks.displayName = "ConnectionLine";
const Tp = {};
function ci(e = Tp) {
  se(e), fe(), ae(() => {
  }, [e]);
}
function zp() {
  fe(), se(!1), ae(() => {
  }, []);
}
function Qs({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: i, onEdgeDoubleClick: s, onNodeMouseEnter: a, onNodeMouseMove: l, onNodeMouseLeave: c, onNodeContextMenu: u, onSelectionContextMenu: d, onSelectionStart: f, onSelectionEnd: p, connectionLineType: g, connectionLineStyle: x, connectionLineComponent: w, connectionLineContainerStyle: y, selectionKeyCode: S, selectionOnDrag: m, selectionMode: b, multiSelectionKeyCode: N, panActivationKeyCode: _, zoomActivationKeyCode: C, deleteKeyCode: A, onlyRenderVisibleElements: D, elementsSelectable: B, defaultViewport: I, translateExtent: $, minZoom: H, maxZoom: v, preventScrolling: k, defaultMarkerColor: E, zoomOnScroll: M, zoomOnPinch: j, panOnScroll: P, panOnScrollSpeed: L, panOnScrollMode: O, zoomOnDoubleClick: V, panOnDrag: Y, autoPanOnSelection: X, onPaneClick: Q, onPaneMouseEnter: ne, onPaneMouseMove: Z, onPaneMouseLeave: z, onPaneScroll: W, onPaneContextMenu: te, paneClickDistance: ee, nodeClickDistance: U, onEdgeContextMenu: J, onEdgeMouseEnter: re, onEdgeMouseMove: R, onEdgeMouseLeave: G, reconnectRadius: ue, onReconnect: he, onReconnectStart: Ne, onReconnectEnd: Ce, noDragClassName: ke, noWheelClassName: Je, noPanClassName: Be, disableKeyboardA11y: Fe, nodeExtent: Se, rfId: Ie, viewport: Me, onViewportChange: T }) {
  return ci(e), ci(t), zp(), kp(n), Mp(Me), h.jsx(Kh, { onPaneClick: Q, onPaneMouseEnter: ne, onPaneMouseMove: Z, onPaneMouseLeave: z, onPaneContextMenu: te, onPaneScroll: W, paneClickDistance: ee, deleteKeyCode: A, selectionKeyCode: S, selectionOnDrag: m, selectionMode: b, onSelectionStart: f, onSelectionEnd: p, multiSelectionKeyCode: N, panActivationKeyCode: _, zoomActivationKeyCode: C, elementsSelectable: B, zoomOnScroll: M, zoomOnPinch: j, zoomOnDoubleClick: V, panOnScroll: P, panOnScrollSpeed: L, panOnScrollMode: O, panOnDrag: Y, autoPanOnSelection: X, defaultViewport: I, translateExtent: $, minZoom: H, maxZoom: v, onSelectionContextMenu: d, preventScrolling: k, noDragClassName: ke, noWheelClassName: Je, noPanClassName: Be, disableKeyboardA11y: Fe, onViewportChange: T, isControlledViewport: !!Me, children: h.jsxs(Cp, { children: [h.jsx(Ep, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: s, onReconnect: he, onReconnectStart: Ne, onReconnectEnd: Ce, onlyRenderVisibleElements: D, onEdgeContextMenu: J, onEdgeMouseEnter: re, onEdgeMouseMove: R, onEdgeMouseLeave: G, reconnectRadius: ue, defaultMarkerColor: E, noPanClassName: Be, disableKeyboardA11y: Fe, rfId: Ie }), h.jsx($p, { style: x, type: g, component: w, containerStyle: y }), h.jsx("div", { className: "react-flow__edgelabel-renderer" }), h.jsx(sp, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: i, onNodeMouseEnter: a, onNodeMouseMove: l, onNodeMouseLeave: c, onNodeContextMenu: u, nodeClickDistance: U, onlyRenderVisibleElements: D, noPanClassName: Be, noDragClassName: ke, disableKeyboardA11y: Fe, nodeExtent: Se, rfId: Ie }), h.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Qs.displayName = "GraphView";
const Rp = pe(Qs), Lp = Ji(), li = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: a, minZoom: l = 0.5, maxZoom: c = 2, nodeOrigin: u, nodeExtent: d, zIndexMode: f = "basic" } = {}) => {
  const p = /* @__PURE__ */ new Map(), g = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), y = o ?? t ?? [], S = n ?? e ?? [], m = u ?? [0, 0], b = d ?? Ht;
  ds(x, w, y);
  const { nodesInitialized: N } = xo(S, p, g, {
    nodeOrigin: m,
    nodeExtent: b,
    zIndexMode: f
  });
  let _ = [0, 0, 1];
  if (s && r && i) {
    const C = Gt(p, {
      filter: (I) => !!((I.width || I.initialWidth) && (I.height || I.initialHeight))
    }), { x: A, y: D, zoom: B } = To(C, r, i, l, c, a?.padding ?? 0.1);
    _ = [A, D, B];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: i ?? 0,
    transform: _,
    nodes: S,
    nodesInitialized: N,
    nodeLookup: p,
    parentLookup: g,
    edges: y,
    edgeLookup: w,
    connectionLookup: x,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: o !== void 0,
    panZoom: null,
    minZoom: l,
    maxZoom: c,
    translateExtent: Ht,
    nodeExtent: b,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: mt.Strict,
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
    connection: { ...qi },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: Lp,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Xi,
    zIndexMode: f,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, Hp = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: a, minZoom: l, maxZoom: c, nodeOrigin: u, nodeExtent: d, zIndexMode: f }) => Jf((p, g) => {
  async function x() {
    const { nodeLookup: w, panZoom: y, fitViewOptions: S, fitViewResolver: m, width: b, height: N, minZoom: _, maxZoom: C } = g();
    y && (await Zd({
      nodes: w,
      width: b,
      height: N,
      panZoom: y,
      minZoom: _,
      maxZoom: C
    }, S), m?.resolve(!0), p({ fitViewResolver: null }));
  }
  return {
    ...li({
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
    setNodes: (w) => {
      const { nodeLookup: y, parentLookup: S, nodeOrigin: m, elevateNodesOnSelect: b, fitViewQueued: N, zIndexMode: _, nodesSelectionActive: C } = g(), { nodesInitialized: A, hasSelectedNodes: D } = xo(w, y, S, {
        nodeOrigin: m,
        nodeExtent: d,
        elevateNodesOnSelect: b,
        checkEquality: !0,
        zIndexMode: _
      }), B = C && D;
      N && A ? (x(), p({
        nodes: w,
        nodesInitialized: A,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: B
      })) : p({ nodes: w, nodesInitialized: A, nodesSelectionActive: B });
    },
    setEdges: (w) => {
      const { connectionLookup: y, edgeLookup: S } = g();
      ds(y, S, w), p({ edges: w });
    },
    setDefaultNodesAndEdges: (w, y) => {
      if (w) {
        const { setNodes: S } = g();
        S(w), p({ hasDefaultNodes: !0 });
      }
      if (y) {
        const { setEdges: S } = g();
        S(y), p({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (w) => {
      const { triggerNodeChanges: y, nodeLookup: S, parentLookup: m, domNode: b, nodeOrigin: N, nodeExtent: _, debug: C, fitViewQueued: A, zIndexMode: D } = g(), { changes: B, updatedInternals: I } = xf(w, S, m, b, N, _, D);
      I && (pf(S, m, { nodeOrigin: N, nodeExtent: _, zIndexMode: D }), A ? (x(), p({ fitViewQueued: !1, fitViewOptions: void 0 })) : p({}), B?.length > 0 && (C && console.log("React Flow: trigger node changes", B), y?.(B)));
    },
    updateNodePositions: (w, y = !1) => {
      const S = [];
      let m = [];
      const { nodeLookup: b, triggerNodeChanges: N, connection: _, updateConnection: C, onNodesChangeMiddlewareMap: A } = g();
      for (const [D, B] of w) {
        const I = b.get(D), $ = !!(I?.expandParent && I?.parentId && B?.position), H = {
          id: D,
          type: "position",
          position: $ ? {
            x: Math.max(0, B.position.x),
            y: Math.max(0, B.position.y)
          } : B.position,
          dragging: y
        };
        if (I && _.inProgress && _.fromNode.id === I.id) {
          const v = ct(I, _.fromHandle, K.Left, !0);
          C({ ..._, from: v });
        }
        $ && I.parentId && S.push({
          id: D,
          parentId: I.parentId,
          rect: {
            ...B.internals.positionAbsolute,
            width: B.measured.width ?? 0,
            height: B.measured.height ?? 0
          }
        }), m.push(H);
      }
      if (S.length > 0) {
        const { parentLookup: D, nodeOrigin: B } = g(), I = Oo(S, b, D, B);
        m.push(...I);
      }
      for (const D of A.values())
        m = D(m);
      N(m);
    },
    triggerNodeChanges: (w) => {
      const { onNodesChange: y, setNodes: S, nodes: m, hasDefaultNodes: b, debug: N } = g();
      if (w?.length) {
        if (b) {
          const _ = Cs(w, m);
          S(_);
        }
        N && console.log("React Flow: trigger node changes", w), y?.(w);
      }
    },
    triggerEdgeChanges: (w) => {
      const { onEdgesChange: y, setEdges: S, edges: m, hasDefaultEdges: b, debug: N } = g();
      if (w?.length) {
        if (b) {
          const _ = ks(w, m);
          S(_);
        }
        N && console.log("React Flow: trigger edge changes", w), y?.(w);
      }
    },
    addSelectedNodes: (w) => {
      const { multiSelectionActive: y, edgeLookup: S, nodeLookup: m, triggerNodeChanges: b, triggerEdgeChanges: N } = g();
      if (y) {
        const _ = w.map((C) => et(C, !0));
        b(_);
        return;
      }
      b(dt(m, /* @__PURE__ */ new Set([...w]), !0)), N(dt(S));
    },
    addSelectedEdges: (w) => {
      const { multiSelectionActive: y, edgeLookup: S, nodeLookup: m, triggerNodeChanges: b, triggerEdgeChanges: N } = g();
      if (y) {
        const _ = w.map((C) => et(C, !0));
        N(_);
        return;
      }
      N(dt(S, /* @__PURE__ */ new Set([...w]))), b(dt(m, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: w, edges: y } = {}) => {
      const { edges: S, nodes: m, nodeLookup: b, triggerNodeChanges: N, triggerEdgeChanges: _ } = g(), C = w || m, A = y || S, D = [];
      for (const I of C) {
        if (!I.selected)
          continue;
        const $ = b.get(I.id);
        $ && ($.selected = !1), D.push(et(I.id, !1));
      }
      const B = [];
      for (const I of A)
        I.selected && B.push(et(I.id, !1));
      N(D), _(B);
    },
    setMinZoom: (w) => {
      const { panZoom: y, maxZoom: S } = g();
      y?.setScaleExtent([w, S]), p({ minZoom: w });
    },
    setMaxZoom: (w) => {
      const { panZoom: y, minZoom: S } = g();
      y?.setScaleExtent([S, w]), p({ maxZoom: w });
    },
    setTranslateExtent: (w) => {
      g().panZoom?.setTranslateExtent(w), p({ translateExtent: w });
    },
    resetSelectedElements: () => {
      const { edges: w, nodes: y, triggerNodeChanges: S, triggerEdgeChanges: m, elementsSelectable: b } = g();
      if (!b)
        return;
      const N = y.reduce((C, A) => A.selected ? [...C, et(A.id, !1)] : C, []), _ = w.reduce((C, A) => A.selected ? [...C, et(A.id, !1)] : C, []);
      S(N), m(_);
    },
    setNodeExtent: (w) => {
      const { nodes: y, nodeLookup: S, parentLookup: m, nodeOrigin: b, elevateNodesOnSelect: N, nodeExtent: _, zIndexMode: C } = g();
      w[0][0] === _[0][0] && w[0][1] === _[0][1] && w[1][0] === _[1][0] && w[1][1] === _[1][1] || (xo(y, S, m, {
        nodeOrigin: b,
        nodeExtent: w,
        elevateNodesOnSelect: N,
        checkEquality: !1,
        zIndexMode: C
      }), p({ nodeExtent: w }));
    },
    panBy: (w) => {
      const { transform: y, width: S, height: m, panZoom: b, translateExtent: N } = g();
      return wf({ delta: w, panZoom: b, transform: y, translateExtent: N, width: S, height: m });
    },
    setCenter: async (w, y, S) => {
      const { width: m, height: b, maxZoom: N, panZoom: _ } = g();
      if (!_)
        return !1;
      const C = typeof S?.zoom < "u" ? S.zoom : N;
      return await _.setViewport({
        x: m / 2 - w * C,
        y: b / 2 - y * C,
        zoom: C
      }, { duration: S?.duration, ease: S?.ease, interpolate: S?.interpolate }), !0;
    },
    cancelConnection: () => {
      p({
        connection: { ...qi }
      });
    },
    updateConnection: (w) => {
      p({ connection: w });
    },
    reset: () => p({ ...li() })
  };
}, Object.is);
function Vp({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: i, initialMinZoom: s, initialMaxZoom: a, initialFitViewOptions: l, fitView: c, nodeOrigin: u, nodeExtent: d, zIndexMode: f, children: p }) {
  const [g] = oe(() => Hp({
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
  return h.jsx(nh, { value: g, children: h.jsx(Ch, { children: p }) });
}
function Op({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: i, height: s, fitView: a, fitViewOptions: l, minZoom: c, maxZoom: u, nodeOrigin: d, nodeExtent: f, zIndexMode: p }) {
  return Xt(Tn) ? h.jsx(h.Fragment, { children: e }) : h.jsx(Vp, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: i, initialHeight: s, fitView: a, initialFitViewOptions: l, initialMinZoom: c, initialMaxZoom: u, nodeOrigin: d, nodeExtent: f, zIndexMode: p, children: e });
}
const Bp = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function Fp({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: i, edgeTypes: s, onNodeClick: a, onEdgeClick: l, onInit: c, onMove: u, onMoveStart: d, onMoveEnd: f, onConnect: p, onConnectStart: g, onConnectEnd: x, onClickConnectStart: w, onClickConnectEnd: y, onNodeMouseEnter: S, onNodeMouseMove: m, onNodeMouseLeave: b, onNodeContextMenu: N, onNodeDoubleClick: _, onNodeDragStart: C, onNodeDrag: A, onNodeDragStop: D, onNodesDelete: B, onEdgesDelete: I, onDelete: $, onSelectionChange: H, onSelectionDragStart: v, onSelectionDrag: k, onSelectionDragStop: E, onSelectionContextMenu: M, onSelectionStart: j, onSelectionEnd: P, onBeforeDelete: L, connectionMode: O, connectionLineType: V = Ke.Bezier, connectionLineStyle: Y, connectionLineComponent: X, connectionLineContainerStyle: Q, deleteKeyCode: ne = "Backspace", selectionKeyCode: Z = "Shift", selectionOnDrag: z = !1, selectionMode: W = Vt.Full, panActivationKeyCode: te = "Space", multiSelectionKeyCode: ee = Bt() ? "Meta" : "Control", zoomActivationKeyCode: U = Bt() ? "Meta" : "Control", snapToGrid: J, snapGrid: re, onlyRenderVisibleElements: R = !1, selectNodesOnDrag: G, nodesDraggable: ue, autoPanOnNodeFocus: he, nodesConnectable: Ne, nodesFocusable: Ce, nodeOrigin: ke = Es, edgesFocusable: Je, edgesReconnectable: Be, elementsSelectable: Fe = !0, defaultViewport: Se = gh, minZoom: Ie = 0.5, maxZoom: Me = 2, translateExtent: T = Ht, preventScrolling: F = !0, nodeExtent: q, defaultMarkerColor: ie = "#b1b1b7", zoomOnScroll: le = !0, zoomOnPinch: me = !0, panOnScroll: Ae = !1, panOnScrollSpeed: Ct = 0.5, panOnScrollMode: On = ot.Free, zoomOnDoubleClick: ua = !0, panOnDrag: da = !0, onPaneClick: fa, onPaneMouseEnter: ha, onPaneMouseMove: pa, onPaneMouseLeave: ga, onPaneScroll: ma, onPaneContextMenu: ya, paneClickDistance: xa = 1, nodeClickDistance: wa = 0, children: va, onReconnect: ba, onReconnectStart: Sa, onReconnectEnd: _a, onEdgeContextMenu: Ea, onEdgeDoubleClick: Na, onEdgeMouseEnter: Ca, onEdgeMouseMove: ka, onEdgeMouseLeave: Ia, reconnectRadius: Ma = 10, onNodesChange: Aa, onEdgesChange: Da, noDragClassName: Pa = "nodrag", noWheelClassName: ja = "nowheel", noPanClassName: Xo = "nopan", fitView: qo, fitViewOptions: Zo, connectOnClick: $a, attributionPosition: Ta, proOptions: za, defaultEdgeOptions: Ra, elevateNodesOnSelect: La = !0, elevateEdgesOnSelect: Ha = !1, disableKeyboardA11y: Uo = !1, autoPanOnConnect: Va, autoPanOnNodeDrag: Oa, autoPanOnSelection: Ba = !0, autoPanSpeed: Fa, connectionRadius: Ya, isValidConnection: Wa, onError: Xa, style: qa, id: Go, nodeDragThreshold: Za, connectionDragThreshold: Ua, viewport: Ga, onViewportChange: Ka, width: Qa, height: Ja, colorMode: ec = "light", debug: tc, onScroll: Ko, ariaLabelConfig: nc, zIndexMode: Qo = "basic", ...oc }, rc) {
  const Bn = Go || "1", ic = wh(ec), sc = ye((Jo) => {
    Jo.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), Ko?.(Jo);
  }, [Ko]);
  return h.jsx("div", { "data-testid": "rf__wrapper", ...oc, onScroll: sc, style: { ...qa, ...Bp }, ref: rc, className: ge(["react-flow", r, ic]), id: Go, role: "application", children: h.jsxs(Op, { nodes: e, edges: t, width: Qa, height: Ja, fitView: qo, fitViewOptions: Zo, minZoom: Ie, maxZoom: Me, nodeOrigin: ke, nodeExtent: q, zIndexMode: Qo, children: [h.jsx(xh, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: p, onConnectStart: g, onConnectEnd: x, onClickConnectStart: w, onClickConnectEnd: y, nodesDraggable: ue, autoPanOnNodeFocus: he, nodesConnectable: Ne, nodesFocusable: Ce, edgesFocusable: Je, edgesReconnectable: Be, elementsSelectable: Fe, elevateNodesOnSelect: La, elevateEdgesOnSelect: Ha, minZoom: Ie, maxZoom: Me, nodeExtent: q, onNodesChange: Aa, onEdgesChange: Da, snapToGrid: J, snapGrid: re, connectionMode: O, translateExtent: T, connectOnClick: $a, defaultEdgeOptions: Ra, fitView: qo, fitViewOptions: Zo, onNodesDelete: B, onEdgesDelete: I, onDelete: $, onNodeDragStart: C, onNodeDrag: A, onNodeDragStop: D, onSelectionDrag: k, onSelectionDragStart: v, onSelectionDragStop: E, onMove: u, onMoveStart: d, onMoveEnd: f, noPanClassName: Xo, nodeOrigin: ke, rfId: Bn, autoPanOnConnect: Va, autoPanOnNodeDrag: Oa, autoPanSpeed: Fa, onError: Xa, connectionRadius: Ya, isValidConnection: Wa, selectNodesOnDrag: G, nodeDragThreshold: Za, connectionDragThreshold: Ua, onBeforeDelete: L, debug: tc, ariaLabelConfig: nc, zIndexMode: Qo }), h.jsx(Rp, { onInit: c, onNodeClick: a, onEdgeClick: l, onNodeMouseEnter: S, onNodeMouseMove: m, onNodeMouseLeave: b, onNodeContextMenu: N, onNodeDoubleClick: _, nodeTypes: i, edgeTypes: s, connectionLineType: V, connectionLineStyle: Y, connectionLineComponent: X, connectionLineContainerStyle: Q, selectionKeyCode: Z, selectionOnDrag: z, selectionMode: W, deleteKeyCode: ne, multiSelectionKeyCode: ee, panActivationKeyCode: te, zoomActivationKeyCode: U, onlyRenderVisibleElements: R, defaultViewport: Se, translateExtent: T, minZoom: Ie, maxZoom: Me, preventScrolling: F, zoomOnScroll: le, zoomOnPinch: me, zoomOnDoubleClick: ua, panOnScroll: Ae, panOnScrollSpeed: Ct, panOnScrollMode: On, panOnDrag: da, autoPanOnSelection: Ba, onPaneClick: fa, onPaneMouseEnter: ha, onPaneMouseMove: pa, onPaneMouseLeave: ga, onPaneScroll: ma, onPaneContextMenu: ya, paneClickDistance: xa, nodeClickDistance: wa, onSelectionContextMenu: M, onSelectionStart: j, onSelectionEnd: P, onReconnect: ba, onReconnectStart: Sa, onReconnectEnd: _a, onEdgeContextMenu: Ea, onEdgeDoubleClick: Na, onEdgeMouseEnter: Ca, onEdgeMouseMove: ka, onEdgeMouseLeave: Ia, reconnectRadius: Ma, defaultMarkerColor: ie, noDragClassName: Pa, noWheelClassName: ja, noPanClassName: Xo, rfId: Bn, disableKeyboardA11y: Uo, nodeExtent: q, viewport: Ga, onViewportChange: Ka }), h.jsx(ph, { onSelectionChange: H }), va, h.jsx(lh, { proOptions: za, position: Ta }), h.jsx(ch, { rfId: Bn, disableKeyboardA11y: Uo })] }) });
}
var Yp = Ms(Fp);
function Wp({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return h.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: ge(["react-flow__background-pattern", n, o]) });
}
function Xp({ radius: e, className: t }) {
  return h.jsx("circle", { cx: e, cy: e, r: e, className: ge(["react-flow__background-pattern", "dots", t]) });
}
var Qe;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(Qe || (Qe = {}));
const qp = {
  [Qe.Dots]: 1,
  [Qe.Lines]: 1,
  [Qe.Cross]: 6
}, Zp = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function Js({
  id: e,
  variant: t = Qe.Dots,
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
  const d = se(null), { transform: f, patternId: p } = ce(Zp, de), g = o || qp[t], x = t === Qe.Dots, w = t === Qe.Cross, y = Array.isArray(n) ? n : [n, n], S = [y[0] * f[2] || 1, y[1] * f[2] || 1], m = g * f[2], b = Array.isArray(i) ? i : [i, i], N = w ? [m, m] : S, _ = [
    b[0] * f[2] || 1 + N[0] / 2,
    b[1] * f[2] || 1 + N[1] / 2
  ], C = `${p}${e || ""}`;
  return h.jsxs("svg", { className: ge(["react-flow__background", c]), style: {
    ...l,
    ...Rn,
    "--xy-background-color-props": a,
    "--xy-background-pattern-color-props": s
  }, ref: d, "data-testid": "rf__background", children: [h.jsx("pattern", { id: C, x: f[0] % S[0], y: f[1] % S[1], width: S[0], height: S[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${_[0]},-${_[1]})`, children: x ? h.jsx(Xp, { radius: m / 2, className: u }) : h.jsx(Wp, { dimensions: N, lineWidth: r, variant: t, className: u }) }), h.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${C})` })] });
}
Js.displayName = "Background";
const Up = pe(Js);
function Gp() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: h.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function Kp() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: h.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function Qp() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: h.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function Jp() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: h.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function eg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: h.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function an({ children: e, className: t, ...n }) {
  return h.jsx("button", { type: "button", className: ge(["react-flow__controls-button", t]), ...n, children: e });
}
const tg = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function ea({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: i, onZoomOut: s, onFitView: a, onInteractiveChange: l, className: c, children: u, position: d = "bottom-left", orientation: f = "vertical", "aria-label": p }) {
  const g = fe(), { isInteractive: x, minZoomReached: w, maxZoomReached: y, ariaLabelConfig: S } = ce(tg, de), { zoomIn: m, zoomOut: b, fitView: N } = Bo(), _ = () => {
    m(), i?.();
  }, C = () => {
    b(), s?.();
  }, A = () => {
    N(r), a?.();
  }, D = () => {
    g.setState({
      nodesDraggable: !x,
      nodesConnectable: !x,
      elementsSelectable: !x
    }), l?.(!x);
  }, B = f === "horizontal" ? "horizontal" : "vertical";
  return h.jsxs(zn, { className: ge(["react-flow__controls", B, c]), position: d, style: e, "data-testid": "rf__controls", "aria-label": p ?? S["controls.ariaLabel"], children: [t && h.jsxs(h.Fragment, { children: [h.jsx(an, { onClick: _, className: "react-flow__controls-zoomin", title: S["controls.zoomIn.ariaLabel"], "aria-label": S["controls.zoomIn.ariaLabel"], disabled: y, children: h.jsx(Gp, {}) }), h.jsx(an, { onClick: C, className: "react-flow__controls-zoomout", title: S["controls.zoomOut.ariaLabel"], "aria-label": S["controls.zoomOut.ariaLabel"], disabled: w, children: h.jsx(Kp, {}) })] }), n && h.jsx(an, { className: "react-flow__controls-fitview", onClick: A, title: S["controls.fitView.ariaLabel"], "aria-label": S["controls.fitView.ariaLabel"], children: h.jsx(Qp, {}) }), o && h.jsx(an, { className: "react-flow__controls-interactive", onClick: D, title: S["controls.interactive.ariaLabel"], "aria-label": S["controls.interactive.ariaLabel"], children: x ? h.jsx(eg, {}) : h.jsx(Jp, {}) }), u] });
}
ea.displayName = "Controls";
const ng = pe(ea);
function og({ id: e, x: t, y: n, width: o, height: r, style: i, color: s, strokeColor: a, strokeWidth: l, className: c, borderRadius: u, shapeRendering: d, selected: f, onClick: p }) {
  const { background: g, backgroundColor: x } = i || {}, w = s || g || x;
  return h.jsx("rect", { className: ge(["react-flow__minimap-node", { selected: f }, c]), x: t, y: n, rx: u, ry: u, width: o, height: r, style: {
    fill: w,
    stroke: a,
    strokeWidth: l
  }, shapeRendering: d, onClick: p ? (y) => p(y, e) : void 0 });
}
const rg = pe(og), ig = (e) => e.nodes.map((t) => t.id), oo = (e) => e instanceof Function ? e : () => e;
function sg({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: i = rg,
  onClick: s
}) {
  const a = ce(ig, de), l = oo(t), c = oo(e), u = oo(n), d = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return h.jsx(h.Fragment, { children: a.map((f) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    h.jsx(cg, { id: f, nodeColorFunc: l, nodeStrokeColorFunc: c, nodeClassNameFunc: u, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: i, onClick: s, shapeRendering: d }, f)
  )) });
}
function ag({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: i, shapeRendering: s, NodeComponent: a, onClick: l }) {
  const { node: c, x: u, y: d, width: f, height: p } = ce((g) => {
    const x = g.nodeLookup.get(e);
    if (!x)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const w = x.internals.userNode, { x: y, y: S } = x.internals.positionAbsolute, { width: m, height: b } = Ze(w);
    return {
      node: w,
      x: y,
      y: S,
      width: m,
      height: b
    };
  }, de);
  return !c || c.hidden || !es(c) ? null : h.jsx(a, { x: u, y: d, width: f, height: p, style: c.style, selected: !!c.selected, className: o(c), color: t(c), borderRadius: r, strokeColor: n(c), strokeWidth: i, shapeRendering: s, onClick: l, id: c.id });
}
const cg = pe(ag);
var lg = pe(sg);
const ug = 200, dg = 150, fg = (e) => !e.hidden, hg = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? Qi(Gt(e.nodeLookup, { filter: fg }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, pg = "react-flow__minimap-desc";
function ta({
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
  onClick: p,
  onNodeClick: g,
  pannable: x = !1,
  zoomable: w = !1,
  ariaLabel: y,
  inversePan: S,
  zoomStep: m = 1,
  offsetScale: b = 5
}) {
  const N = fe(), _ = se(null), { boundingRect: C, viewBB: A, rfId: D, panZoom: B, translateExtent: I, flowWidth: $, flowHeight: H, ariaLabelConfig: v } = ce(hg, de), k = e?.width ?? ug, E = e?.height ?? dg, M = C.width / k, j = C.height / E, P = Math.max(M, j), L = P * k, O = P * E, V = b * P, Y = C.x - (L - C.width) / 2 - V, X = C.y - (O - C.height) / 2 - V, Q = L + V * 2, ne = O + V * 2, Z = `${pg}-${D}`, z = se(0), W = se();
  z.current = P, ae(() => {
    if (_.current && B)
      return W.current = If({
        domNode: _.current,
        panZoom: B,
        getTransform: () => N.getState().transform,
        getViewScale: () => z.current
      }), () => {
        W.current?.destroy();
      };
  }, [B]), ae(() => {
    W.current?.update({
      translateExtent: I,
      width: $,
      height: H,
      inversePan: S,
      pannable: x,
      zoomStep: m,
      zoomable: w
    });
  }, [x, w, S, m, I, $, H]);
  const te = p ? (J) => {
    const [re, R] = W.current?.pointer(J) || [0, 0];
    p(J, { x: re, y: R });
  } : void 0, ee = g ? ye((J, re) => {
    const R = N.getState().nodeLookup.get(re).internals.userNode;
    g(J, R);
  }, []) : void 0, U = y ?? v["minimap.ariaLabel"];
  return h.jsx(zn, { position: f, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-background-color-props": typeof c == "string" ? c : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof d == "number" ? d * P : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof s == "number" ? s : void 0
  }, className: ge(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: h.jsxs("svg", { width: k, height: E, viewBox: `${Y} ${X} ${Q} ${ne}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": Z, ref: _, onClick: te, children: [U && h.jsx("title", { id: Z, children: U }), h.jsx(lg, { onClick: ee, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: i, nodeClassName: r, nodeStrokeWidth: s, nodeComponent: a }), h.jsx("path", { className: "react-flow__minimap-mask", d: `M${Y - V},${X - V}h${Q + V * 2}v${ne + V * 2}h${-Q - V * 2}z
        M${A.x},${A.y}h${A.width}v${A.height}h${-A.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
ta.displayName = "MiniMap";
const gg = pe(ta), mg = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, yg = {
  [vt.Line]: "right",
  [vt.Handle]: "bottom-right"
};
function xg({ nodeId: e, position: t, variant: n = vt.Handle, className: o, style: r = void 0, children: i, color: s, minWidth: a = 10, minHeight: l = 10, maxWidth: c = Number.MAX_VALUE, maxHeight: u = Number.MAX_VALUE, keepAspectRatio: d = !1, resizeDirection: f, autoScale: p = !0, shouldResize: g, onResizeStart: x, onResize: w, onResizeEnd: y }) {
  const S = js(), m = typeof e == "string" ? e : S, b = fe(), N = se(null), _ = n === vt.Handle, C = ce(ye(mg(_ && p), [_, p]), de), A = se(null), D = t ?? yg[n];
  ae(() => {
    if (!(!N.current || !m))
      return A.current || (A.current = Of({
        domNode: N.current,
        nodeId: m,
        getStoreItems: () => {
          const { nodeLookup: I, transform: $, snapGrid: H, snapToGrid: v, nodeOrigin: k, domNode: E } = b.getState();
          return {
            nodeLookup: I,
            transform: $,
            snapGrid: H,
            snapToGrid: v,
            nodeOrigin: k,
            paneDomNode: E
          };
        },
        onChange: (I, $) => {
          const { triggerNodeChanges: H, nodeLookup: v, parentLookup: k, nodeOrigin: E } = b.getState(), M = [], j = { x: I.x, y: I.y }, P = v.get(m);
          if (P && P.expandParent && P.parentId) {
            const L = P.origin ?? E, O = I.width ?? P.measured.width ?? 0, V = I.height ?? P.measured.height ?? 0, Y = {
              id: P.id,
              parentId: P.parentId,
              rect: {
                width: O,
                height: V,
                ...ts({
                  x: I.x ?? P.position.x,
                  y: I.y ?? P.position.y
                }, { width: O, height: V }, P.parentId, v, L)
              }
            }, X = Oo([Y], v, k, E);
            M.push(...X), j.x = I.x ? Math.max(L[0] * O, I.x) : void 0, j.y = I.y ? Math.max(L[1] * V, I.y) : void 0;
          }
          if (j.x !== void 0 && j.y !== void 0) {
            const L = {
              id: m,
              type: "position",
              position: { ...j }
            };
            M.push(L);
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
            M.push(O);
          }
          for (const L of $) {
            const O = {
              ...L,
              type: "position"
            };
            M.push(O);
          }
          H(M);
        },
        onEnd: ({ width: I, height: $ }) => {
          const H = {
            id: m,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: I,
              height: $
            }
          };
          b.getState().triggerNodeChanges([H]);
        }
      })), A.current.update({
        controlPosition: D,
        boundaries: {
          minWidth: a,
          minHeight: l,
          maxWidth: c,
          maxHeight: u
        },
        keepAspectRatio: d,
        resizeDirection: f,
        onResizeStart: x,
        onResize: w,
        onResizeEnd: y,
        shouldResize: g
      }), () => {
        A.current?.destroy();
      };
  }, [
    D,
    a,
    l,
    c,
    u,
    d,
    x,
    w,
    y,
    g
  ]);
  const B = D.split("-");
  return h.jsx("div", { className: ge(["react-flow__resize-control", "nodrag", ...B, n, o]), ref: N, style: {
    ...r,
    scale: C,
    ...s && { [_ ? "backgroundColor" : "borderColor"]: s }
  }, children: i });
}
pe(xg);
const wg = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), na = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var vg = {
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
const bg = Cn(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: o,
    className: r = "",
    children: i,
    iconNode: s,
    ...a
  }, l) => io(
    "svg",
    {
      ref: l,
      ...vg,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: na("lucide", r),
      ...a
    },
    [
      ...s.map(([c, u]) => io(c, u)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const ve = (e, t) => {
  const n = Cn(
    ({ className: o, ...r }, i) => io(bg, {
      ref: i,
      iconNode: t,
      className: na(`lucide-${wg(e)}`, o),
      ...r
    })
  );
  return n.displayName = `${e}`, n;
};
const Sg = ve("Boxes", [
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
const Hn = ve("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const _g = ve("ChevronDown", [
  ["path", { d: "m6 9 6 6 6-6", key: "qrunsl" }]
]);
const Eg = ve("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const hn = ve("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const Yt = ve("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const Ng = ve("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const Cg = ve("ListTree", [
  ["path", { d: "M21 12h-8", key: "1bmf0i" }],
  ["path", { d: "M21 6H8", key: "1pqkrb" }],
  ["path", { d: "M21 18h-8", key: "1tm79t" }],
  ["path", { d: "M3 6v4c0 1.1.9 2 2 2h3", key: "1ywdgy" }],
  ["path", { d: "M3 10v6c0 1.1.9 2 2 2h3", key: "2wc746" }]
]);
const oa = ve("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const kg = ve("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const Ig = ve("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const Mg = ve("Save", [
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
const Ag = ve("Search", [
  ["circle", { cx: "11", cy: "11", r: "8", key: "4ej97u" }],
  ["path", { d: "m21 21-4.3-4.3", key: "1qie3q" }]
]);
const St = ve("Sparkles", [
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
const ui = ve("Trash2", [
  ["path", { d: "M3 6h18", key: "d0wm0j" }],
  ["path", { d: "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", key: "4alrt4" }],
  ["path", { d: "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", key: "v07s0e" }],
  ["line", { x1: "10", x2: "10", y1: "11", y2: "17", key: "1uufr5" }],
  ["line", { x1: "14", x2: "14", y1: "11", y2: "17", key: "xtxkd" }]
]), Le = "/_elsa/workflow-management";
async function Dg(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${Le}/definitions?${n.toString()}`);
}
async function Pg(e, t) {
  return e.http.getJson(`${Le}/definitions/${encodeURIComponent(t)}`);
}
async function jg(e, t) {
  return e.http.postJson(`${Le}/definitions`, t);
}
async function $g(e, t) {
  await e.http.deleteJson(`${Le}/definitions/${encodeURIComponent(t)}`);
}
async function Tg(e, t) {
  await e.http.postJson(`${Le}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function zg(e, t) {
  await e.http.deleteJson(`${Le}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function Rg(e, t) {
  return e.http.putJson(`${Le}/drafts/${encodeURIComponent(t.id)}`, { state: t.state, layout: t.layout });
}
async function Lg(e, t) {
  return e.http.postJson(`${Le}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function Hg(e, t) {
  return e.http.postJson(`${Le}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function ra(e, t) {
  return e.http.postJson(`${Le}/executables/${encodeURIComponent(t)}/run`, {});
}
async function Vg(e) {
  return e.http.getJson("/_demo/workflows/executables");
}
async function ia(e) {
  return e.http.getJson(`${Le}/activities`);
}
const Yo = "elsa.sequence.structure", Vn = "elsa.flowchart.structure";
function di(e, t) {
  if (!e) return null;
  let n = e, o = Xe(n)[0];
  if (!o) return null;
  for (const r of t) {
    const i = Xe(n).find((a) => a.id === r.slotId);
    if (!i) return null;
    const s = i.activities.find((a) => a.nodeId === r.ownerNodeId);
    if (!s || (n = s, o = Xe(n)[0], !o)) return null;
  }
  return { owner: n, slot: o };
}
function Xe(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = Qg(t), r = ro(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: Jg(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, i]) => ro(i)).map(([i, s]) => ({
    id: `${t.kind}:${i}`,
    label: t0(i),
    property: i,
    mode: "generic",
    activities: ro(s) ?? []
  }));
}
function Og(e, t, n) {
  const o = new Map(t.map((s) => [s.activityVersionId, s])), r = new Map(n.map((s) => [s.nodeId, s])), i = e.slot.activities.map((s, a) => {
    const l = o.get(s.activityVersionId), c = r.get(s.nodeId) ?? e0(e.slot.mode, a);
    return {
      id: s.nodeId,
      type: "workflowActivity",
      position: { x: c.x, y: c.y },
      data: {
        label: l?.displayName ?? s.activityVersionId,
        activityVersionId: s.activityVersionId,
        activityTypeKey: l?.activityTypeKey,
        childSlots: Xe(s)
      }
    };
  });
  return {
    nodes: i,
    edges: e.slot.mode === "flowchart" ? Gg(e.owner) : Ug(e.slot, i)
  };
}
function bo(e, t, n) {
  if (t.length === 0) {
    const a = Xe(e)[0];
    return a ? En(e, a, n) : e;
  }
  const [o, ...r] = t, i = Xe(e).find((a) => a.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((a) => a.nodeId === o.ownerNodeId ? bo(a, r, n) : a);
  return En(e, i, s);
}
function So(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, i = Xe(e).find((a) => a.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((a) => a.nodeId === o.ownerNodeId ? So(a, r, n) : a);
  return En(e, i, s);
}
function En(e, t, n) {
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
function Bg(e, t, n) {
  const o = new Map(e.slot.activities.map((i) => [i.nodeId, i])), r = t.map((i) => o.get(i.id)).filter((i) => !!i);
  return e.slot.mode === "sequence" && r.sort((i, s) => {
    const a = t.find((c) => c.id === i.nodeId), l = t.find((c) => c.id === s.nodeId);
    return (a?.position.x ?? 0) - (l?.position.x ?? 0);
  }), En(e.owner, e.slot, r);
}
function Fg(e, t) {
  return {
    ...e,
    structure: Zg(e.structure, t)
  };
}
function Yg(e, t) {
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
function Wg(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: qg(e)
  };
}
function Te(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? Xg(t) : n;
}
function Xg(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function qg(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: Yo,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: Vn,
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
function Zg(e, t) {
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
function Ug(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function Gg(e) {
  if (e.structure?.kind !== Vn) return [];
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
function ro(e) {
  return Array.isArray(e) ? e.filter(Kg) : null;
}
function Kg(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Qg(e) {
  return e.kind === Yo ? "sequence" : e.kind === Vn ? "flowchart" : "generic";
}
function Jg(e) {
  return e.kind === Yo || e.kind === Vn, "Activities";
}
function e0(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function t0(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const n0 = { workflowActivity: E0 }, fi = "application/x-elsa-activity-version-id", o0 = 6, r0 = 1200, i0 = [10, 25, 50], s0 = 10;
function M0(e) {
  e.featureAreas.add({
    id: "workflows",
    title: "Workflows",
    description: "Design, publish and run workflow definitions and inspect instances.",
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
        { title: "Instances", path: "/workflows/instances", iconColor: "#0ea5e9" }
      ]
    },
    routes: [
      {
        id: "workflows-definitions",
        path: "/workflows/definitions",
        label: "Workflow definitions",
        component: () => /* @__PURE__ */ h.jsx(a0, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ h.jsx(c0, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow instances",
        component: () => /* @__PURE__ */ h.jsx(l0, { ai: e.ai })
      }
    ]
  });
}
function a0({ context: e, ai: t }) {
  const [n, o] = oe(hi);
  ae(() => {
    const i = () => o(hi());
    return window.addEventListener("popstate", i), () => window.removeEventListener("popstate", i);
  }, []);
  const r = (i) => {
    const s = i ? `/workflows/definitions?definition=${encodeURIComponent(i)}` : "/workflows/definitions";
    window.history.pushState({}, "", s), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return n ? /* @__PURE__ */ h.jsx(_0, { context: e, definitionId: n, ai: t, onBack: () => r(null) }) : /* @__PURE__ */ h.jsx(Wo, { activePath: "/workflows/definitions", title: "Definitions", children: /* @__PURE__ */ h.jsx(u0, { context: e, ai: t, onOpen: r }) });
}
function c0({ context: e, ai: t }) {
  const [n, o] = oe(pi);
  return ae(() => {
    const r = () => o(pi());
    return window.addEventListener("popstate", r), () => window.removeEventListener("popstate", r);
  }, []), /* @__PURE__ */ h.jsx(Wo, { activePath: "/workflows/executables", title: "Executables", children: /* @__PURE__ */ h.jsx(f0, { context: e, ai: t, definitionFilter: n }) });
}
function l0({ ai: e }) {
  const t = _t(e, "weaver.workflows.explain-instance");
  return /* @__PURE__ */ h.jsx(Wo, { activePath: "/workflows/instances", title: "Instances", children: /* @__PURE__ */ h.jsxs("div", { className: "wf-empty", children: [
    "Workflow instance history will appear here when the runtime exposes an instance query endpoint.",
    t ? /* @__PURE__ */ h.jsxs("button", { type: "button", className: "wf-ai-inline-action", onClick: () => Et(e, t, { scope: "workflow-instances" }), children: [
      /* @__PURE__ */ h.jsx(St, { size: 13 }),
      " Ask Weaver about instances"
    ] }) : null
  ] }) });
}
function Wo({ activePath: e, title: t, children: n }) {
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
function hi() {
  return new URLSearchParams(window.location.search).get("definition");
}
function pi() {
  return new URLSearchParams(window.location.search).get("definition");
}
function u0({ context: e, ai: t, onOpen: n }) {
  const [o, r] = oe(""), [i, s] = oe("active"), [a, l] = oe(1), [c, u] = oe(s0), [d, f] = oe("loading"), [p, g] = oe(""), [x, w] = oe(""), [y, S] = oe([]), [m, b] = oe(0), [N, _] = oe(() => /* @__PURE__ */ new Set()), [C, A] = oe(null), [D, B] = oe(!1), [I, $] = oe([]), [H, v] = oe("idle"), k = se(null), E = xe(() => y.map((R) => R.id), [y]), M = _t(t, "weaver.workflows.suggest-create-metadata"), j = _t(t, "weaver.workflows.explain-definition"), P = E.filter((R) => N.has(R)).length, L = E.length > 0 && P === E.length, O = ye(async () => {
    f("loading"), g("");
    try {
      const R = await Dg(e, { search: o, state: i, page: a, pageSize: c }), G = typeof R.totalCount == "number", ue = R.totalCount ?? R.definitions.length, he = sa(ue, c);
      if (ue > 0 && a > he) {
        l(he);
        return;
      }
      S(G ? R.definitions : p0(R.definitions, a, c)), b(ue), f("ready");
    } catch (R) {
      g(R instanceof Error ? R.message : String(R)), f("failed");
    }
  }, [e, o, i, a, c]);
  ae(() => {
    O();
  }, [O]), ae(() => {
    k.current && (k.current.indeterminate = P > 0 && !L);
  }, [L, P]);
  const V = ye(async () => {
    if (!(H === "loading" || H === "ready")) {
      v("loading");
      try {
        const R = await ia(e);
        $(R.activities ?? []), v("ready");
      } catch (R) {
        v("failed"), g(R instanceof Error ? R.message : String(R));
      }
    }
  }, [H, e]), Y = () => {
    g(""), w(""), A({ name: "", description: "", rootKind: "flowchart" }), V();
  }, X = async () => {
    if (C?.name.trim()) {
      B(!0), g(""), w("");
      try {
        const R = await jg(e, {
          name: C.name.trim(),
          description: C.description.trim() || null,
          rootKind: C.rootKind,
          rootActivityVersionId: y0(C, I)
        });
        A(null), n(R.definition.id);
      } catch (R) {
        g(R instanceof Error ? R.message : String(R));
      } finally {
        B(!1);
      }
    }
  }, Q = (R) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(R)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, ne = async () => {
    if (y.length === 1 && a > 1) {
      l(a - 1);
      return;
    }
    await O();
  }, Z = () => _(/* @__PURE__ */ new Set()), z = (R, G) => {
    _((ue) => {
      const he = new Set(ue);
      return G ? he.add(R) : he.delete(R), he;
    });
  }, W = (R) => {
    _((G) => {
      const ue = new Set(G);
      for (const he of E)
        R ? ue.add(he) : ue.delete(he);
      return ue;
    });
  }, te = (R) => {
    s(R), l(1), Z();
  }, ee = (R) => {
    r(R), l(1), Z();
  }, U = async (R) => {
    if (window.confirm(`Delete workflow definition "${R.name}"? You can restore it from the Deleted view.`)) {
      w(""), g("");
      try {
        await $g(e, R.id), z(R.id, !1), w(`Deleted ${R.name}`), await ne();
      } catch (G) {
        g(G instanceof Error ? G.message : String(G));
      }
    }
  }, J = async (R) => {
    w(""), g("");
    try {
      await Tg(e, R.id), z(R.id, !1), w(`Restored ${R.name}`), await ne();
    } catch (G) {
      g(G instanceof Error ? G.message : String(G));
    }
  }, re = async (R) => {
    if (window.confirm(`Permanently delete workflow definition "${R.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`)) {
      w(""), g("");
      try {
        await zg(e, R.id), z(R.id, !1), w(`Permanently deleted ${R.name}`), await ne();
      } catch (G) {
        g(G instanceof Error ? G.message : String(G));
      }
    }
  };
  return /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
    /* @__PURE__ */ h.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ h.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ h.jsx("button", { type: "button", className: i === "active" ? "active" : "", "aria-selected": i === "active", onClick: () => te("active"), children: "Active" }),
        /* @__PURE__ */ h.jsx("button", { type: "button", className: i === "deleted" ? "active" : "", "aria-selected": i === "deleted", onClick: () => te("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ h.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ h.jsx(Ag, { size: 15 }),
        /* @__PURE__ */ h.jsx("input", { value: o, onChange: (R) => ee(R.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
        O();
      }, children: "Refresh" }),
      /* @__PURE__ */ h.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ h.jsxs("button", { type: "button", title: "Create workflow", onClick: Y, children: [
        /* @__PURE__ */ h.jsx(kg, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    d === "failed" ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Yt, { size: 16 }),
      " ",
      p
    ] }) : null,
    d !== "failed" && p ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Yt, { size: 16 }),
      " ",
      p
    ] }) : null,
    x ? /* @__PURE__ */ h.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ h.jsx(Hn, { size: 14 }),
      " ",
      x
    ] }) : null,
    N.size > 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ h.jsxs("span", { children: [
        N.size,
        " selected"
      ] }),
      /* @__PURE__ */ h.jsx("button", { type: "button", onClick: Z, children: "Clear selection" })
    ] }) : null,
    d === "loading" ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: "Loading workflow definitions..." }) : null,
    d === "ready" && y.length === 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-empty", children: [
      "No ",
      i,
      " workflow definitions found."
    ] }) : null,
    d === "ready" && y.length > 0 ? /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
      /* @__PURE__ */ h.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ h.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ h.jsx(
            "input",
            {
              ref: k,
              type: "checkbox",
              checked: L,
              onChange: (R) => W(R.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ h.jsx("span", { children: "Name" }),
          /* @__PURE__ */ h.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ h.jsx("span", { children: i === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ h.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ h.jsx("span", { children: "Actions" })
        ] }),
        y.map((R) => /* @__PURE__ */ h.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${R.name}`,
            "aria-selected": N.has(R.id),
            tabIndex: 0,
            onClick: () => n(R.id),
            onKeyDown: (G) => {
              G.currentTarget === G.target && (G.key !== "Enter" && G.key !== " " || (G.preventDefault(), n(R.id)));
            },
            children: [
              /* @__PURE__ */ h.jsx("label", { className: "wf-row-select", onClick: (G) => G.stopPropagation(), children: /* @__PURE__ */ h.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: N.has(R.id),
                  onChange: (G) => z(R.id, G.target.checked),
                  "aria-label": `Select workflow definition ${R.name}`
                }
              ) }),
              /* @__PURE__ */ h.jsxs("span", { children: [
                /* @__PURE__ */ h.jsx("strong", { children: R.name }),
                /* @__PURE__ */ h.jsx("small", { children: R.description || R.id })
              ] }),
              /* @__PURE__ */ h.jsx("span", { children: R.latestVersion ?? "No version" }),
              /* @__PURE__ */ h.jsx("span", { children: i === "deleted" ? _o(R.deletedAt) : R.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ h.jsx("span", { children: _o(R.lastModifiedAt) }),
              /* @__PURE__ */ h.jsx("span", { className: "wf-row-actions", onClick: (G) => G.stopPropagation(), children: i === "active" ? /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
                /* @__PURE__ */ h.jsx("button", { type: "button", onClick: (G) => {
                  G.stopPropagation(), n(R.id);
                }, children: "Open" }),
                /* @__PURE__ */ h.jsx("button", { type: "button", onClick: (G) => {
                  G.stopPropagation(), Q(R.id);
                }, children: "Artifacts" }),
                j ? /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => Et(t, j, R), children: [
                  /* @__PURE__ */ h.jsx(St, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ h.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  U(R);
                }, children: [
                  /* @__PURE__ */ h.jsx(ui, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
                /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
                  J(R);
                }, children: [
                  /* @__PURE__ */ h.jsx(Ig, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ h.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  re(R);
                }, children: [
                  /* @__PURE__ */ h.jsx(ui, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          R.id
        ))
      ] }),
      /* @__PURE__ */ h.jsx(
        h0,
        {
          page: a,
          pageSize: c,
          totalCount: m,
          onPageChange: l,
          onPageSizeChange: (R) => {
            u(R), l(1);
          }
        }
      )
    ] }) : null,
    C ? /* @__PURE__ */ h.jsx(
      d0,
      {
        draft: C,
        activities: I,
        catalogState: H,
        creating: D,
        suggestMetadataAction: M,
        onSuggestMetadata: M ? () => Et(t, M, { draft: C, activities: I }) : void 0,
        onChange: (R) => A(R),
        onClose: () => A(null),
        onSubmit: X
      }
    ) : null
  ] });
}
function d0({ draft: e, activities: t, catalogState: n, creating: o, suggestMetadataAction: r, onSuggestMetadata: i, onChange: s, onClose: a, onSubmit: l }) {
  const c = xe(() => g0(t), [t]), u = m0(e, t), d = (f) => {
    if (f.startsWith("kind:")) {
      s({ ...e, rootKind: f.slice(5), rootActivityVersionId: null });
      return;
    }
    const p = t.find((g) => g.activityVersionId === f);
    s({
      ...e,
      rootKind: aa(p) ?? e.rootKind,
      rootActivityVersionId: f
    });
  };
  return /* @__PURE__ */ h.jsx("div", { className: "wf-dialog-backdrop", role: "presentation", children: /* @__PURE__ */ h.jsx("section", { className: "wf-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "workflow-create-title", children: /* @__PURE__ */ h.jsxs(
    "form",
    {
      onSubmit: (f) => {
        f.preventDefault(), l();
      },
      children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-dialog-heading", children: [
          /* @__PURE__ */ h.jsx("h3", { id: "workflow-create-title", children: "Create Workflow" }),
          r ? /* @__PURE__ */ h.jsxs("button", { type: "button", className: "wf-ai-action", onClick: i, title: r.description ?? r.label, children: [
            /* @__PURE__ */ h.jsx(St, { size: 13 }),
            " ",
            r.label
          ] }) : null
        ] }),
        /* @__PURE__ */ h.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ h.jsx("span", { children: "Display name" }),
          /* @__PURE__ */ h.jsx(
            "input",
            {
              autoFocus: !0,
              "aria-label": "Display name",
              value: e.name,
              onChange: (f) => s({ ...e, name: f.target.value })
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
              onChange: (f) => s({ ...e, description: f.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ h.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ h.jsx("span", { children: "Root activity" }),
          /* @__PURE__ */ h.jsxs(
            "select",
            {
              "aria-label": "Root activity",
              value: u,
              onChange: (f) => d(f.target.value),
              disabled: n === "loading",
              children: [
                /* @__PURE__ */ h.jsx("optgroup", { label: "Composite roots", children: c.compositeRoots.map((f) => /* @__PURE__ */ h.jsx("option", { value: f.value, children: f.label }, f.value)) }),
                c.otherCategories.map((f) => /* @__PURE__ */ h.jsx("optgroup", { label: f.name, children: f.activities.map((p) => /* @__PURE__ */ h.jsx("option", { value: p.activityVersionId, children: Te(p) }, p.activityVersionId)) }, f.name))
              ]
            }
          )
        ] }),
        n === "loading" ? /* @__PURE__ */ h.jsx("div", { className: "wf-dialog-note", children: "Loading activity catalog..." }) : null,
        n === "failed" ? /* @__PURE__ */ h.jsx("div", { className: "wf-dialog-note", children: "Activity catalog could not be loaded. Composite roots remain available." }) : null,
        /* @__PURE__ */ h.jsxs("div", { className: "wf-dialog-actions", children: [
          /* @__PURE__ */ h.jsx("button", { type: "button", onClick: a, disabled: o, children: "Cancel" }),
          /* @__PURE__ */ h.jsx("button", { type: "submit", disabled: o || !e.name.trim(), children: o ? "Creating..." : "Create" })
        ] })
      ]
    }
  ) }) });
}
function f0({ context: e, ai: t, definitionFilter: n }) {
  const [o, r] = oe("loading"), [i, s] = oe(""), [a, l] = oe(""), [c, u] = oe([]), d = xe(
    () => n ? c.filter((x) => x.definitionId === n || x.sourceId === n) : c,
    [n, c]
  ), f = _t(t, "weaver.workflows.explain-executable"), p = ye(async () => {
    r("loading"), s("");
    try {
      u(await Vg(e)), r("ready");
    } catch (x) {
      s(x instanceof Error ? x.message : String(x)), r("failed");
    }
  }, [e]);
  ae(() => {
    p();
  }, [p]);
  const g = async (x) => {
    l(""), s("");
    try {
      await ra(e, x.artifactId), l(`Started ${x.artifactId}`);
    } catch (w) {
      s(w instanceof Error ? w.message : String(w));
    }
  };
  return /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
    /* @__PURE__ */ h.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
        p();
      }, children: "Refresh" }),
      n ? /* @__PURE__ */ h.jsxs("span", { className: "wf-filter-chip", children: [
        "Definition ",
        n
      ] }) : null
    ] }),
    o === "failed" ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Yt, { size: 16 }),
      " ",
      i
    ] }) : null,
    a ? /* @__PURE__ */ h.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ h.jsx(Hn, { size: 14 }),
      " ",
      a
    ] }) : null,
    o === "loading" ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: "Loading workflow executables..." }) : null,
    o === "ready" && d.length === 0 ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: n ? "No workflow executables found for this definition." : "No workflow executables found. Publish a workflow definition to create one." }) : null,
    o === "ready" && d.length > 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ h.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ h.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ h.jsx("span", { children: "Version" }),
        /* @__PURE__ */ h.jsx("span", { children: "Source" }),
        /* @__PURE__ */ h.jsx("span", { children: "Root" }),
        /* @__PURE__ */ h.jsx("span", { children: "Published" }),
        /* @__PURE__ */ h.jsx("span", { children: "Actions" })
      ] }),
      d.map((x) => /* @__PURE__ */ h.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ h.jsxs("span", { children: [
          /* @__PURE__ */ h.jsx("strong", { children: x.artifactId }),
          /* @__PURE__ */ h.jsx("small", { children: x.artifactHash })
        ] }),
        /* @__PURE__ */ h.jsx("span", { children: x.artifactVersion }),
        /* @__PURE__ */ h.jsx("span", { children: v0(x) }),
        /* @__PURE__ */ h.jsx("span", { children: b0(x) }),
        /* @__PURE__ */ h.jsx("span", { children: _o(x.publishedAt ?? x.createdAt) }),
        /* @__PURE__ */ h.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
            g(x);
          }, children: [
            /* @__PURE__ */ h.jsx(oa, { size: 13 }),
            " Run"
          ] }),
          f ? /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => Et(t, f, x), children: [
            /* @__PURE__ */ h.jsx(St, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, x.artifactId))
    ] }) : null
  ] });
}
function h0({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const i = sa(n, t), s = n === 0 ? 0 : (e - 1) * t + 1, a = Math.min(e * t, n);
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
      /* @__PURE__ */ h.jsx("select", { value: t, onChange: (l) => r(Number(l.target.value)), children: i0.map((l) => /* @__PURE__ */ h.jsx("option", { value: l, children: l }, l)) })
    ] }),
    /* @__PURE__ */ h.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ h.jsx(Eg, { size: 14 }),
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
        /* @__PURE__ */ h.jsx(hn, { size: 14 })
      ] })
    ] })
  ] });
}
function p0(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function sa(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function _t(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function Et(e, t, n) {
  const o = t.createPrompt(n);
  o && e.dispatchPrompt(o);
}
function g0(e) {
  const t = Nn(e, "flowchart"), n = Nn(e, "sequence"), o = [
    { value: t?.activityVersionId ?? "kind:flowchart", label: "Flowchart" },
    { value: n?.activityVersionId ?? "kind:sequence", label: "Sequence" }
  ], r = /* @__PURE__ */ new Map();
  for (const s of e) {
    if (w0(s)) continue;
    const a = s.category || "Uncategorized";
    r.set(a, [...r.get(a) ?? [], s]);
  }
  const i = Array.from(r.entries()).sort(([s], [a]) => s.localeCompare(a)).map(([s, a]) => ({
    name: s,
    activities: a.sort((l, c) => Te(l).localeCompare(Te(c)))
  }));
  return { compositeRoots: o, otherCategories: i };
}
function m0(e, t) {
  return e.rootActivityVersionId ?? Nn(t, e.rootKind)?.activityVersionId ?? `kind:${e.rootKind}`;
}
function y0(e, t) {
  return e.rootActivityVersionId ?? Nn(t, e.rootKind)?.activityVersionId ?? null;
}
function Nn(e, t) {
  return e.find((n) => aa(n) === t);
}
function aa(e) {
  return e ? ca(e) ? "flowchart" : la(e) ? "sequence" : null : null;
}
function x0(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "Uncategorized";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [o]) => n.localeCompare(o)).map(([n, o]) => ({
    category: n,
    activities: o.sort((r, i) => Te(r).localeCompare(Te(i)))
  }));
}
function w0(e) {
  return ca(e) || la(e);
}
function ca(e) {
  return Te(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function la(e) {
  return Te(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function v0(e) {
  return e.sourceKind || e.sourceId || e.sourceVersion ? [e.sourceKind, e.sourceId, e.sourceVersion].filter(Boolean).join(" / ") : e.definitionId;
}
function b0(e) {
  return S0(e.rootActivityType) || e.rootActivityType;
}
function S0(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function _0({ context: e, definitionId: t, ai: n, onBack: o }) {
  const [r, i] = oe(null), [s, a] = oe(null), [l, c] = oe([]), [u, d] = oe([]), [f, p] = oe([]), [g, x] = oe([]), [w, y] = oe(null), [S, m] = oe(null), [b, N] = oe(""), [_, C] = oe(""), [A, D] = oe(!1), [B, I] = oe(null), [$, H] = oe(() => /* @__PURE__ */ new Set()), v = se(null), k = se(""), E = se(0), M = se(null), j = se(!1), P = s?.state.rootActivity ?? null, L = xe(() => di(P, u), [P, u]), O = xe(() => new Map(l.map((T) => [T.activityVersionId, T])), [l]), V = xe(() => x0(l), [l]), Y = xe(() => L?.slot.activities.find((T) => T.nodeId === S) ?? null, [L, S]), X = Y ? Xe(Y) : [], Q = _t(n, "weaver.workflows.find-draft-risks"), ne = _t(n, "weaver.workflows.propose-update"), Z = ye(async () => {
    N("");
    const [T, F] = await Promise.all([
      Pg(e, t),
      ia(e)
    ]), q = T.draft ?? null;
    i(T), k.current = q ? At(q) : "", a(q), c(F.activities ?? []), d([]), m(null);
  }, [e, t]);
  ae(() => {
    Z().catch((T) => N(T instanceof Error ? T.message : String(T)));
  }, [Z]), ae(() => {
    H((T) => {
      let F = !1;
      const q = new Set(T);
      for (const ie of V)
        q.has(ie.category) || (q.add(ie.category), F = !0);
      return F ? q : T;
    });
  }, [V]), ae(() => {
    if (!L) {
      p([]), x([]);
      return;
    }
    const T = Og(L, l, s?.layout ?? []);
    p(T.nodes), x(T.edges);
  }, [L, l, s?.layout]);
  const z = (T) => {
    a((F) => F && { ...F, state: { ...F.state, rootActivity: T } });
  }, W = ye((T, F) => {
    const q = Wg(T, C0(T));
    if (!s?.state.rootActivity) {
      z(q), m(q.nodeId);
      return;
    }
    if (!L) {
      if (!Xe(q)[0]) {
        C(""), N("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      a((le) => {
        if (!le?.state.rootActivity) return le;
        const me = le.state.rootActivity, Ae = bo(q, [], [me]), Ct = F ? [
          ...le.layout.filter((On) => On.nodeId !== me.nodeId),
          {
            nodeId: me.nodeId,
            x: Math.round(F.x),
            y: Math.round(F.y)
          }
        ] : le.layout;
        return {
          ...le,
          layout: Ct,
          state: {
            ...le.state,
            rootActivity: Ae
          }
        };
      }), m(s.state.rootActivity.nodeId), N(""), C(`Wrapped root in ${Te(T)}`);
      return;
    }
    a((ie) => {
      if (!ie?.state.rootActivity) return ie;
      const le = di(ie.state.rootActivity, u);
      if (!le) return ie;
      const me = bo(ie.state.rootActivity, u, [...le.slot.activities, q]), Ae = F ? [
        ...ie.layout.filter((Ct) => Ct.nodeId !== q.nodeId),
        {
          nodeId: q.nodeId,
          x: Math.round(F.x),
          y: Math.round(F.y)
        }
      ] : ie.layout;
      return {
        ...ie,
        layout: Ae,
        state: {
          ...ie.state,
          rootActivity: me
        }
      };
    }), m(q.nodeId);
  }, [s?.state.rootActivity, u, L]), te = ye((T, F) => {
    if (!v.current) return null;
    const q = v.current.getBoundingClientRect();
    return w ? w.screenToFlowPosition({ x: T, y: F }) : {
      x: T - q.left,
      y: F - q.top
    };
  }, [w]), ee = ye((T, F, q) => {
    if (!v.current) return !1;
    const ie = v.current.getBoundingClientRect();
    if (!(F >= ie.left && F <= ie.right && q >= ie.top && q <= ie.bottom)) return !1;
    const me = te(F, q);
    return me ? (W(T, me), !0) : !1;
  }, [W, te]);
  ae(() => {
    const T = (q) => {
      const ie = M.current;
      if (!ie) return;
      Math.hypot(q.clientX - ie.startX, q.clientY - ie.startY) >= o0 && (ie.dragging = !0);
    }, F = (q) => {
      const ie = M.current;
      if (M.current = null, !ie?.dragging || !v.current) return;
      const le = v.current.getBoundingClientRect();
      q.clientX >= le.left && q.clientX <= le.right && q.clientY >= le.top && q.clientY <= le.bottom && (j.current = !0, window.setTimeout(() => {
        j.current = !1;
      }, 0), ee(ie.activity, q.clientX, q.clientY));
    };
    return window.addEventListener("pointermove", T), window.addEventListener("pointerup", F), window.addEventListener("pointercancel", F), () => {
      window.removeEventListener("pointermove", T), window.removeEventListener("pointerup", F), window.removeEventListener("pointercancel", F);
    };
  }, [w, ee]);
  const U = (T, F) => {
    T.dataTransfer.setData(fi, F.activityVersionId), T.dataTransfer.setData("text/plain", F.activityVersionId), T.dataTransfer.effectAllowed = "copy";
  }, J = (T, F) => {
    T.clientX === 0 && T.clientY === 0 || ee(F, T.clientX, T.clientY) && (j.current = !0, window.setTimeout(() => {
      j.current = !1;
    }, 0));
  }, re = (T, F) => {
    T.button === 0 && (M.current = {
      activity: F,
      startX: T.clientX,
      startY: T.clientY,
      dragging: !1
    });
  }, R = (T) => {
    j.current || W(T);
  }, G = (T) => {
    T.preventDefault(), T.dataTransfer.dropEffect = "copy";
  }, ue = (T) => {
    T.preventDefault();
    const F = T.dataTransfer.getData(fi) || T.dataTransfer.getData("text/plain"), q = O.get(F);
    q && ee(q, T.clientX, T.clientY);
  }, he = ye(async (T, F) => {
    const q = ++E.current, ie = At(T);
    N("");
    try {
      const le = await Rg(e, T), me = At(le);
      k.current = me, a((Ae) => !Ae || Ae.id !== le.id ? Ae : At(Ae) === ie ? le : { ...Ae, validationErrors: le.validationErrors }), q === E.current && C(F);
    } catch (le) {
      q === E.current && (C(""), N(le instanceof Error ? le.message : String(le)));
    }
  }, [e]);
  ae(() => {
    if (!A || !s || At(s) === k.current) return;
    C("Autosaving...");
    const F = window.setTimeout(() => {
      he(s, "Autosaved");
    }, r0);
    return () => window.clearTimeout(F);
  }, [A, s, he]);
  const Ne = async () => {
    s && (C("Saving..."), await he(s, "Saved"));
  }, Ce = async () => {
    if (s) {
      C("Promoting...");
      try {
        const T = await Lg(e, s.id), F = await Hg(e, T.versionId);
        I(F.artifactId), C(`Published ${F.artifactVersion}`), await Z();
      } catch (T) {
        C(""), N(T instanceof Error ? T.message : String(T));
      }
    }
  }, ke = async () => {
    if (B) {
      C("Running...");
      try {
        await ra(e, B), C("Run dispatched");
      } catch (T) {
        C(""), N(T instanceof Error ? T.message : String(T));
      }
    }
  }, Je = (T) => p((F) => Cs(T, F)), Be = (T) => x((F) => ks(T, F)), Fe = (T) => {
    if (!s?.state.rootActivity || !L || L.slot.mode !== "flowchart") return;
    const F = Is(T, g), q = Fg(L.owner, F);
    x(F), z(So(s.state.rootActivity, u, q));
  }, Se = () => {
    a((T) => {
      if (!T) return T;
      const F = Yg(T.layout, f);
      if (!T.state.rootActivity || !L) return { ...T, layout: F };
      const q = Bg(L, f);
      return {
        ...T,
        layout: F,
        state: {
          ...T.state,
          rootActivity: So(T.state.rootActivity, u, q)
        }
      };
    });
  }, Ie = (T, F, q) => {
    d((ie) => [...ie, { ownerNodeId: T.nodeId, slotId: F, label: q }]), m(null);
  }, Me = (T) => {
    H((F) => {
      const q = new Set(F);
      return q.has(T) ? q.delete(T) : q.add(T), q;
    });
  };
  return !r || !s ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: b || "Loading workflow editor..." }) : /* @__PURE__ */ h.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ h.jsx("button", { type: "button", className: "wf-link-button", onClick: o, children: "Definitions" }),
      /* @__PURE__ */ h.jsx(hn, { size: 14 }),
      /* @__PURE__ */ h.jsx("strong", { children: r.definition.name }),
      /* @__PURE__ */ h.jsx("span", { className: "wf-chip", children: "Draft" }),
      _ ? /* @__PURE__ */ h.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ h.jsx(Hn, { size: 13 }),
        " ",
        _
      ] }) : null,
      /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ h.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ h.jsx("input", { type: "checkbox", checked: A, onChange: (T) => D(T.target.checked) }),
          /* @__PURE__ */ h.jsx("span", { children: "Autosave" })
        ] }),
        Q ? /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => Et(n, Q, { definition: r.definition, draft: s }), children: [
          /* @__PURE__ */ h.jsx(St, { size: 15 }),
          " Risks"
        ] }) : null,
        ne ? /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => Et(n, ne, { definition: r.definition, draft: s }), children: [
          /* @__PURE__ */ h.jsx(St, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
          Ne();
        }, children: [
          /* @__PURE__ */ h.jsx(Mg, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
          Ce();
        }, children: [
          /* @__PURE__ */ h.jsx(Ng, { size: 15 }),
          " Promote"
        ] }),
        /* @__PURE__ */ h.jsxs("button", { type: "button", disabled: !B, onClick: () => {
          ke();
        }, children: [
          /* @__PURE__ */ h.jsx(oa, { size: 15 }),
          " Run"
        ] })
      ] })
    ] }),
    b ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Yt, { size: 16 }),
      " ",
      b
    ] }) : null,
    /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-body", children: [
      /* @__PURE__ */ h.jsxs("aside", { className: "wf-palette", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ h.jsx(Sg, { size: 15 }),
          " Activities"
        ] }),
        /* @__PURE__ */ h.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: V.map((T) => {
          const F = $.has(T.category);
          return /* @__PURE__ */ h.jsxs("div", { className: "wf-palette-category", children: [
            /* @__PURE__ */ h.jsxs(
              "button",
              {
                type: "button",
                className: "wf-palette-category-toggle",
                role: "treeitem",
                "aria-expanded": F,
                onClick: () => Me(T.category),
                children: [
                  F ? /* @__PURE__ */ h.jsx(_g, { size: 14 }) : /* @__PURE__ */ h.jsx(hn, { size: 14 }),
                  /* @__PURE__ */ h.jsx("span", { children: T.category }),
                  /* @__PURE__ */ h.jsx("small", { children: T.activities.length })
                ]
              }
            ),
            F ? /* @__PURE__ */ h.jsx("div", { className: "wf-palette-activities", role: "group", children: T.activities.map((q) => {
              const ie = q.description?.trim(), le = ie ? `wf-palette-description-${q.activityVersionId}` : void 0;
              return /* @__PURE__ */ h.jsxs(
                "button",
                {
                  type: "button",
                  className: "wf-palette-activity",
                  role: "treeitem",
                  draggable: !0,
                  title: ie || Te(q),
                  "aria-describedby": le,
                  onClick: () => R(q),
                  onDragStart: (me) => U(me, q),
                  onDragEnd: (me) => J(me, q),
                  onPointerDown: (me) => re(me, q),
                  children: [
                    /* @__PURE__ */ h.jsx("strong", { children: Te(q) }),
                    ie ? /* @__PURE__ */ h.jsx("small", { id: le, children: ie }) : null
                  ]
                },
                q.activityVersionId
              );
            }) }) : null
          ] }, T.category);
        }) })
      ] }),
      /* @__PURE__ */ h.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-breadcrumb", children: [
          /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
            d([]), m(null);
          }, children: "Root" }),
          u.map((T, F) => /* @__PURE__ */ h.jsxs(Wt.Fragment, { children: [
            /* @__PURE__ */ h.jsx(hn, { size: 13 }),
            /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
              d(u.slice(0, F + 1)), m(null);
            }, children: T.label })
          ] }, `${T.ownerNodeId}-${T.slotId}-${F}`))
        ] }),
        /* @__PURE__ */ h.jsx("div", { className: "wf-canvas", ref: v, onDragOver: G, onDrop: ue, children: /* @__PURE__ */ h.jsxs(
          Yp,
          {
            nodes: f,
            edges: g,
            nodeTypes: n0,
            onInit: y,
            onNodesChange: Je,
            onEdgesChange: Be,
            onConnect: Fe,
            onDragOver: G,
            onDrop: ue,
            onNodeClick: (T, F) => m(F.id),
            onNodeDragStop: Se,
            fitView: !0,
            children: [
              /* @__PURE__ */ h.jsx(Up, { gap: 18, size: 1 }),
              /* @__PURE__ */ h.jsx(ng, {}),
              /* @__PURE__ */ h.jsx(gg, { pannable: !0, zoomable: !0 })
            ]
          }
        ) }),
        /* @__PURE__ */ h.jsx(N0, { draft: s })
      ] }),
      /* @__PURE__ */ h.jsxs("aside", { className: "wf-inspector", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ h.jsx(Cg, { size: 15 }),
          " Inspector"
        ] }),
        Y ? /* @__PURE__ */ h.jsxs("div", { className: "wf-inspector-content", children: [
          /* @__PURE__ */ h.jsx("h3", { children: f.find((T) => T.id === Y.nodeId)?.data.label ?? Y.nodeId }),
          /* @__PURE__ */ h.jsxs("dl", { children: [
            /* @__PURE__ */ h.jsx("dt", { children: "Node ID" }),
            /* @__PURE__ */ h.jsx("dd", { children: Y.nodeId }),
            /* @__PURE__ */ h.jsx("dt", { children: "Activity version" }),
            /* @__PURE__ */ h.jsx("dd", { children: Y.activityVersionId })
          ] }),
          X.length > 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-slot-list", children: [
            /* @__PURE__ */ h.jsx("span", { children: "Embedded slots" }),
            X.map((T) => /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => Ie(Y, T.id, `${f.find((F) => F.id === Y.nodeId)?.data.label ?? Y.nodeId} / ${T.label}`), children: [
              T.label,
              /* @__PURE__ */ h.jsxs("small", { children: [
                T.activities.length,
                " activit",
                T.activities.length === 1 ? "y" : "ies"
              ] })
            ] }, T.id))
          ] }) : /* @__PURE__ */ h.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
        ] }) : /* @__PURE__ */ h.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." })
      ] })
    ] })
  ] });
}
function E0({ data: e, selected: t }) {
  const n = e;
  return /* @__PURE__ */ h.jsxs("div", { className: t ? "wf-node selected" : "wf-node", children: [
    /* @__PURE__ */ h.jsx(bt, { type: "target", position: K.Left }),
    /* @__PURE__ */ h.jsx("strong", { children: n.label }),
    /* @__PURE__ */ h.jsx("small", { children: n.activityTypeKey ?? n.activityVersionId }),
    n.childSlots.length > 0 ? /* @__PURE__ */ h.jsxs("span", { children: [
      n.childSlots.length,
      " embedded slot",
      n.childSlots.length === 1 ? "" : "s"
    ] }) : null,
    /* @__PURE__ */ h.jsx(bt, { type: "source", position: K.Right })
  ] });
}
function N0({ draft: e }) {
  return e.validationErrors.length ? /* @__PURE__ */ h.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ h.jsx(Yt, { size: 14 }),
    e.validationErrors.length,
    " validation issue",
    e.validationErrors.length === 1 ? "" : "s"
  ] }) : /* @__PURE__ */ h.jsxs("div", { className: "wf-validation ok", children: [
    /* @__PURE__ */ h.jsx(Hn, { size: 14 }),
    " No validation errors"
  ] });
}
function C0(e) {
  return `${Te(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function At(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function _o(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
export {
  M0 as register
};
