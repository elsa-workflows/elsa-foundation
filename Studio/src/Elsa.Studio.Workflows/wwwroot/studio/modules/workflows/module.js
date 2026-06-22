import Ht, { memo as he, forwardRef as vn, useRef as ie, useEffect as se, useCallback as pe, useContext as Vt, useMemo as me, createContext as xo, useState as re, useLayoutEffect as rc, createElement as Jn } from "react";
import "@tanstack/react-query";
function ic(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var Rn = { exports: {} }, bt = {};
var Zo;
function sc() {
  if (Zo) return bt;
  Zo = 1;
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
  return bt.Fragment = t, bt.jsx = n, bt.jsxs = n, bt;
}
var Uo;
function ac() {
  return Uo || (Uo = 1, Rn.exports = sc()), Rn.exports;
}
var h = ac();
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
var cc = { value: () => {
} };
function bn() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new tn(n);
}
function tn(e) {
  this._ = e;
}
function lc(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
tn.prototype = bn.prototype = {
  constructor: tn,
  on: function(e, t) {
    var n = this._, o = lc(e + "", n), r, i = -1, s = o.length;
    if (arguments.length < 2) {
      for (; ++i < s; ) if ((r = (e = o[i]).type) && (r = uc(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++i < s; )
      if (r = (e = o[i]).type) n[r] = Go(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = Go(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new tn(e);
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
function uc(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function Go(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = cc, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var eo = "http://www.w3.org/1999/xhtml";
const Ko = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: eo,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function Sn(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Ko.hasOwnProperty(t) ? { space: Ko[t], local: e } : e;
}
function dc(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === eo && t.documentElement.namespaceURI === eo ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function fc(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function li(e) {
  var t = Sn(e);
  return (t.local ? fc : dc)(t);
}
function hc() {
}
function wo(e) {
  return e == null ? hc : function() {
    return this.querySelector(e);
  };
}
function gc(e) {
  typeof e != "function" && (e = wo(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = new Array(s), l, c, u = 0; u < s; ++u)
      (l = i[u]) && (c = e.call(l, l.__data__, u, i)) && ("__data__" in l && (c.__data__ = l.__data__), a[u] = c);
  return new Se(o, this._parents);
}
function pc(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function mc() {
  return [];
}
function ui(e) {
  return e == null ? mc : function() {
    return this.querySelectorAll(e);
  };
}
function yc(e) {
  return function() {
    return pc(e.apply(this, arguments));
  };
}
function xc(e) {
  typeof e == "function" ? e = yc(e) : e = ui(e);
  for (var t = this._groups, n = t.length, o = [], r = [], i = 0; i < n; ++i)
    for (var s = t[i], a = s.length, l, c = 0; c < a; ++c)
      (l = s[c]) && (o.push(e.call(l, l.__data__, c, s)), r.push(l));
  return new Se(o, r);
}
function di(e) {
  return function() {
    return this.matches(e);
  };
}
function fi(e) {
  return function(t) {
    return t.matches(e);
  };
}
var wc = Array.prototype.find;
function vc(e) {
  return function() {
    return wc.call(this.children, e);
  };
}
function bc() {
  return this.firstElementChild;
}
function Sc(e) {
  return this.select(e == null ? bc : vc(typeof e == "function" ? e : fi(e)));
}
var _c = Array.prototype.filter;
function Ec() {
  return Array.from(this.children);
}
function Nc(e) {
  return function() {
    return _c.call(this.children, e);
  };
}
function Cc(e) {
  return this.selectAll(e == null ? Ec : Nc(typeof e == "function" ? e : fi(e)));
}
function kc(e) {
  typeof e != "function" && (e = di(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = [], l, c = 0; c < s; ++c)
      (l = i[c]) && e.call(l, l.__data__, c, i) && a.push(l);
  return new Se(o, this._parents);
}
function hi(e) {
  return new Array(e.length);
}
function Ic() {
  return new Se(this._enter || this._groups.map(hi), this._parents);
}
function cn(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
cn.prototype = {
  constructor: cn,
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
function Mc(e) {
  return function() {
    return e;
  };
}
function Ac(e, t, n, o, r, i) {
  for (var s = 0, a, l = t.length, c = i.length; s < c; ++s)
    (a = t[s]) ? (a.__data__ = i[s], o[s] = a) : n[s] = new cn(e, i[s]);
  for (; s < l; ++s)
    (a = t[s]) && (r[s] = a);
}
function Dc(e, t, n, o, r, i, s) {
  var a, l, c = /* @__PURE__ */ new Map(), u = t.length, d = i.length, f = new Array(u), g;
  for (a = 0; a < u; ++a)
    (l = t[a]) && (f[a] = g = s.call(l, l.__data__, a, t) + "", c.has(g) ? r[a] = l : c.set(g, l));
  for (a = 0; a < d; ++a)
    g = s.call(e, i[a], a, i) + "", (l = c.get(g)) ? (o[a] = l, l.__data__ = i[a], c.delete(g)) : n[a] = new cn(e, i[a]);
  for (a = 0; a < u; ++a)
    (l = t[a]) && c.get(f[a]) === l && (r[a] = l);
}
function Pc(e) {
  return e.__data__;
}
function $c(e, t) {
  if (!arguments.length) return Array.from(this, Pc);
  var n = t ? Dc : Ac, o = this._parents, r = this._groups;
  typeof e != "function" && (e = Mc(e));
  for (var i = r.length, s = new Array(i), a = new Array(i), l = new Array(i), c = 0; c < i; ++c) {
    var u = o[c], d = r[c], f = d.length, g = jc(e.call(u, u && u.__data__, c, o)), p = g.length, v = a[c] = new Array(p), x = s[c] = new Array(p), y = l[c] = new Array(f);
    n(u, d, v, x, y, g, t);
    for (var _ = 0, m = 0, w, C; _ < p; ++_)
      if (w = v[_]) {
        for (_ >= m && (m = _ + 1); !(C = x[m]) && ++m < p; ) ;
        w._next = C || null;
      }
  }
  return s = new Se(s, o), s._enter = a, s._exit = l, s;
}
function jc(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function Tc() {
  return new Se(this._exit || this._groups.map(hi), this._parents);
}
function zc(e, t, n) {
  var o = this.enter(), r = this, i = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? i.remove() : n(i), o && r ? o.merge(r).order() : r;
}
function Rc(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, i = o.length, s = Math.min(r, i), a = new Array(r), l = 0; l < s; ++l)
    for (var c = n[l], u = o[l], d = c.length, f = a[l] = new Array(d), g, p = 0; p < d; ++p)
      (g = c[p] || u[p]) && (f[p] = g);
  for (; l < r; ++l)
    a[l] = n[l];
  return new Se(a, this._parents);
}
function Lc() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, i = o[r], s; --r >= 0; )
      (s = o[r]) && (i && s.compareDocumentPosition(i) ^ 4 && i.parentNode.insertBefore(s, i), i = s);
  return this;
}
function Hc(e) {
  e || (e = Vc);
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
function Vc(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function Oc() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function Bc() {
  return Array.from(this);
}
function Fc() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length; r < i; ++r) {
      var s = o[r];
      if (s) return s;
    }
  return null;
}
function Yc() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function Xc() {
  return !this.node();
}
function Wc(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], i = 0, s = r.length, a; i < s; ++i)
      (a = r[i]) && e.call(a, a.__data__, i, r);
  return this;
}
function qc(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Zc(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Uc(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function Gc(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function Kc(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function Qc(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function Jc(e, t) {
  var n = Sn(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? Zc : qc : typeof t == "function" ? n.local ? Qc : Kc : n.local ? Gc : Uc)(n, t));
}
function gi(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function el(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function tl(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function nl(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function ol(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? el : typeof t == "function" ? nl : tl)(e, t, n ?? "")) : dt(this.node(), e);
}
function dt(e, t) {
  return e.style.getPropertyValue(t) || gi(e).getComputedStyle(e, null).getPropertyValue(t);
}
function rl(e) {
  return function() {
    delete this[e];
  };
}
function il(e, t) {
  return function() {
    this[e] = t;
  };
}
function sl(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function al(e, t) {
  return arguments.length > 1 ? this.each((t == null ? rl : typeof t == "function" ? sl : il)(e, t)) : this.node()[e];
}
function pi(e) {
  return e.trim().split(/^|\s+/);
}
function vo(e) {
  return e.classList || new mi(e);
}
function mi(e) {
  this._node = e, this._names = pi(e.getAttribute("class") || "");
}
mi.prototype = {
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
function yi(e, t) {
  for (var n = vo(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function xi(e, t) {
  for (var n = vo(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function cl(e) {
  return function() {
    yi(this, e);
  };
}
function ll(e) {
  return function() {
    xi(this, e);
  };
}
function ul(e, t) {
  return function() {
    (t.apply(this, arguments) ? yi : xi)(this, e);
  };
}
function dl(e, t) {
  var n = pi(e + "");
  if (arguments.length < 2) {
    for (var o = vo(this.node()), r = -1, i = n.length; ++r < i; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? ul : t ? cl : ll)(n, t));
}
function fl() {
  this.textContent = "";
}
function hl(e) {
  return function() {
    this.textContent = e;
  };
}
function gl(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function pl(e) {
  return arguments.length ? this.each(e == null ? fl : (typeof e == "function" ? gl : hl)(e)) : this.node().textContent;
}
function ml() {
  this.innerHTML = "";
}
function yl(e) {
  return function() {
    this.innerHTML = e;
  };
}
function xl(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function wl(e) {
  return arguments.length ? this.each(e == null ? ml : (typeof e == "function" ? xl : yl)(e)) : this.node().innerHTML;
}
function vl() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function bl() {
  return this.each(vl);
}
function Sl() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function _l() {
  return this.each(Sl);
}
function El(e) {
  var t = typeof e == "function" ? e : li(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Nl() {
  return null;
}
function Cl(e, t) {
  var n = typeof e == "function" ? e : li(e), o = t == null ? Nl : typeof t == "function" ? t : wo(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function kl() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Il() {
  return this.each(kl);
}
function Ml() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Al() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Dl(e) {
  return this.select(e ? Al : Ml);
}
function Pl(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function $l(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function jl(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function Tl(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, i; n < r; ++n)
        i = t[n], (!e.type || i.type === e.type) && i.name === e.name ? this.removeEventListener(i.type, i.listener, i.options) : t[++o] = i;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function zl(e, t, n) {
  return function() {
    var o = this.__on, r, i = $l(t);
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
function Rl(e, t, n) {
  var o = jl(e + ""), r, i = o.length, s;
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
  for (a = t ? zl : Tl, r = 0; r < i; ++r) this.each(a(o[r], t, n));
  return this;
}
function wi(e, t, n) {
  var o = gi(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function Ll(e, t) {
  return function() {
    return wi(this, e, t);
  };
}
function Hl(e, t) {
  return function() {
    return wi(this, e, t.apply(this, arguments));
  };
}
function Vl(e, t) {
  return this.each((typeof t == "function" ? Hl : Ll)(e, t));
}
function* Ol() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length, s; r < i; ++r)
      (s = o[r]) && (yield s);
}
var vi = [null];
function Se(e, t) {
  this._groups = e, this._parents = t;
}
function Ot() {
  return new Se([[document.documentElement]], vi);
}
function Bl() {
  return this;
}
Se.prototype = Ot.prototype = {
  constructor: Se,
  select: gc,
  selectAll: xc,
  selectChild: Sc,
  selectChildren: Cc,
  filter: kc,
  data: $c,
  enter: Ic,
  exit: Tc,
  join: zc,
  merge: Rc,
  selection: Bl,
  order: Lc,
  sort: Hc,
  call: Oc,
  nodes: Bc,
  node: Fc,
  size: Yc,
  empty: Xc,
  each: Wc,
  attr: Jc,
  style: ol,
  property: al,
  classed: dl,
  text: pl,
  html: wl,
  raise: bl,
  lower: _l,
  append: El,
  insert: Cl,
  remove: Il,
  clone: Dl,
  datum: Pl,
  on: Rl,
  dispatch: Vl,
  [Symbol.iterator]: Ol
};
function be(e) {
  return typeof e == "string" ? new Se([[document.querySelector(e)]], [document.documentElement]) : new Se([[e]], vi);
}
function Fl(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Ie(e, t) {
  if (e = Fl(e), t === void 0 && (t = e.currentTarget), t) {
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
const Yl = { passive: !1 }, Mt = { capture: !0, passive: !1 };
function Ln(e) {
  e.stopImmediatePropagation();
}
function lt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function bi(e) {
  var t = e.document.documentElement, n = be(e).on("dragstart.drag", lt, Mt);
  "onselectstart" in t ? n.on("selectstart.drag", lt, Mt) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function Si(e, t) {
  var n = e.document.documentElement, o = be(e).on("dragstart.drag", null);
  t && (o.on("click.drag", lt, Mt), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const Wt = (e) => () => e;
function to(e, {
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
to.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function Xl(e) {
  return !e.ctrlKey && !e.button;
}
function Wl() {
  return this.parentNode;
}
function ql(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function Zl() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function _i() {
  var e = Xl, t = Wl, n = ql, o = Zl, r = {}, i = bn("start", "drag", "end"), s = 0, a, l, c, u, d = 0;
  function f(w) {
    w.on("mousedown.drag", g).filter(o).on("touchstart.drag", x).on("touchmove.drag", y, Yl).on("touchend.drag touchcancel.drag", _).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function g(w, C) {
    if (!(u || !e.call(this, w, C))) {
      var S = m(this, t.call(this, w, C), w, C, "mouse");
      S && (be(w.view).on("mousemove.drag", p, Mt).on("mouseup.drag", v, Mt), bi(w.view), Ln(w), c = !1, a = w.clientX, l = w.clientY, S("start", w));
    }
  }
  function p(w) {
    if (lt(w), !c) {
      var C = w.clientX - a, S = w.clientY - l;
      c = C * C + S * S > d;
    }
    r.mouse("drag", w);
  }
  function v(w) {
    be(w.view).on("mousemove.drag mouseup.drag", null), Si(w.view, c), lt(w), r.mouse("end", w);
  }
  function x(w, C) {
    if (e.call(this, w, C)) {
      var S = w.changedTouches, k = t.call(this, w, C), D = S.length, P, B;
      for (P = 0; P < D; ++P)
        (B = m(this, k, w, C, S[P].identifier, S[P])) && (Ln(w), B("start", w, S[P]));
    }
  }
  function y(w) {
    var C = w.changedTouches, S = C.length, k, D;
    for (k = 0; k < S; ++k)
      (D = r[C[k].identifier]) && (lt(w), D("drag", w, C[k]));
  }
  function _(w) {
    var C = w.changedTouches, S = C.length, k, D;
    for (u && clearTimeout(u), u = setTimeout(function() {
      u = null;
    }, 500), k = 0; k < S; ++k)
      (D = r[C[k].identifier]) && (Ln(w), D("end", w, C[k]));
  }
  function m(w, C, S, k, D, P) {
    var B = i.copy(), M = Ie(P || S, C), T, L, b;
    if ((b = n.call(w, new to("beforestart", {
      sourceEvent: S,
      target: f,
      identifier: D,
      active: s,
      x: M[0],
      y: M[1],
      dx: 0,
      dy: 0,
      dispatch: B
    }), k)) != null)
      return T = b.x - M[0] || 0, L = b.y - M[1] || 0, function N(E, I, j) {
        var A = M, O;
        switch (E) {
          case "start":
            r[D] = N, O = s++;
            break;
          case "end":
            delete r[D], --s;
          // falls through
          case "drag":
            M = Ie(j || I, C), O = s;
            break;
        }
        B.call(
          E,
          w,
          new to(E, {
            sourceEvent: I,
            subject: b,
            target: f,
            identifier: D,
            active: O,
            x: M[0] + T,
            y: M[1] + L,
            dx: M[0] - A[0],
            dy: M[1] - A[1],
            dispatch: B
          }),
          k
        );
      };
  }
  return f.filter = function(w) {
    return arguments.length ? (e = typeof w == "function" ? w : Wt(!!w), f) : e;
  }, f.container = function(w) {
    return arguments.length ? (t = typeof w == "function" ? w : Wt(w), f) : t;
  }, f.subject = function(w) {
    return arguments.length ? (n = typeof w == "function" ? w : Wt(w), f) : n;
  }, f.touchable = function(w) {
    return arguments.length ? (o = typeof w == "function" ? w : Wt(!!w), f) : o;
  }, f.on = function() {
    var w = i.on.apply(i, arguments);
    return w === i ? f : w;
  }, f.clickDistance = function(w) {
    return arguments.length ? (d = (w = +w) * w, f) : Math.sqrt(d);
  }, f;
}
function bo(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function Ei(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function Bt() {
}
var At = 0.7, ln = 1 / At, ut = "\\s*([+-]?\\d+)\\s*", Dt = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", Re = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", Ul = /^#([0-9a-f]{3,8})$/, Gl = new RegExp(`^rgb\\(${ut},${ut},${ut}\\)$`), Kl = new RegExp(`^rgb\\(${Re},${Re},${Re}\\)$`), Ql = new RegExp(`^rgba\\(${ut},${ut},${ut},${Dt}\\)$`), Jl = new RegExp(`^rgba\\(${Re},${Re},${Re},${Dt}\\)$`), eu = new RegExp(`^hsl\\(${Dt},${Re},${Re}\\)$`), tu = new RegExp(`^hsla\\(${Dt},${Re},${Re},${Dt}\\)$`), Qo = {
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
bo(Bt, tt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Jo,
  // Deprecated! Use color.formatHex.
  formatHex: Jo,
  formatHex8: nu,
  formatHsl: ou,
  formatRgb: er,
  toString: er
});
function Jo() {
  return this.rgb().formatHex();
}
function nu() {
  return this.rgb().formatHex8();
}
function ou() {
  return Ni(this).formatHsl();
}
function er() {
  return this.rgb().formatRgb();
}
function tt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = Ul.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? tr(t) : n === 3 ? new we(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? qt(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? qt(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = Gl.exec(e)) ? new we(t[1], t[2], t[3], 1) : (t = Kl.exec(e)) ? new we(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = Ql.exec(e)) ? qt(t[1], t[2], t[3], t[4]) : (t = Jl.exec(e)) ? qt(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = eu.exec(e)) ? rr(t[1], t[2] / 100, t[3] / 100, 1) : (t = tu.exec(e)) ? rr(t[1], t[2] / 100, t[3] / 100, t[4]) : Qo.hasOwnProperty(e) ? tr(Qo[e]) : e === "transparent" ? new we(NaN, NaN, NaN, 0) : null;
}
function tr(e) {
  return new we(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function qt(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new we(e, t, n, o);
}
function ru(e) {
  return e instanceof Bt || (e = tt(e)), e ? (e = e.rgb(), new we(e.r, e.g, e.b, e.opacity)) : new we();
}
function no(e, t, n, o) {
  return arguments.length === 1 ? ru(e) : new we(e, t, n, o ?? 1);
}
function we(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
bo(we, no, Ei(Bt, {
  brighter(e) {
    return e = e == null ? ln : Math.pow(ln, e), new we(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? At : Math.pow(At, e), new we(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new we(Je(this.r), Je(this.g), Je(this.b), un(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: nr,
  // Deprecated! Use color.formatHex.
  formatHex: nr,
  formatHex8: iu,
  formatRgb: or,
  toString: or
}));
function nr() {
  return `#${Qe(this.r)}${Qe(this.g)}${Qe(this.b)}`;
}
function iu() {
  return `#${Qe(this.r)}${Qe(this.g)}${Qe(this.b)}${Qe((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function or() {
  const e = un(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${Je(this.r)}, ${Je(this.g)}, ${Je(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function un(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function Je(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function Qe(e) {
  return e = Je(e), (e < 16 ? "0" : "") + e.toString(16);
}
function rr(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Me(e, t, n, o);
}
function Ni(e) {
  if (e instanceof Me) return new Me(e.h, e.s, e.l, e.opacity);
  if (e instanceof Bt || (e = tt(e)), !e) return new Me();
  if (e instanceof Me) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), i = Math.max(t, n, o), s = NaN, a = i - r, l = (i + r) / 2;
  return a ? (t === i ? s = (n - o) / a + (n < o) * 6 : n === i ? s = (o - t) / a + 2 : s = (t - n) / a + 4, a /= l < 0.5 ? i + r : 2 - i - r, s *= 60) : a = l > 0 && l < 1 ? 0 : s, new Me(s, a, l, e.opacity);
}
function su(e, t, n, o) {
  return arguments.length === 1 ? Ni(e) : new Me(e, t, n, o ?? 1);
}
function Me(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
bo(Me, su, Ei(Bt, {
  brighter(e) {
    return e = e == null ? ln : Math.pow(ln, e), new Me(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? At : Math.pow(At, e), new Me(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new we(
      Hn(e >= 240 ? e - 240 : e + 120, r, o),
      Hn(e, r, o),
      Hn(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new Me(ir(this.h), Zt(this.s), Zt(this.l), un(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = un(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${ir(this.h)}, ${Zt(this.s) * 100}%, ${Zt(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function ir(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function Zt(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function Hn(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const So = (e) => () => e;
function au(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function cu(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function lu(e) {
  return (e = +e) == 1 ? Ci : function(t, n) {
    return n - t ? cu(t, n, e) : So(isNaN(t) ? n : t);
  };
}
function Ci(e, t) {
  var n = t - e;
  return n ? au(e, n) : So(isNaN(e) ? t : e);
}
const dn = (function e(t) {
  var n = lu(t);
  function o(r, i) {
    var s = n((r = no(r)).r, (i = no(i)).r), a = n(r.g, i.g), l = n(r.b, i.b), c = Ci(r.opacity, i.opacity);
    return function(u) {
      return r.r = s(u), r.g = a(u), r.b = l(u), r.opacity = c(u), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function uu(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(i) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - i) + t[r] * i;
    return o;
  };
}
function du(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function fu(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), i = new Array(n), s;
  for (s = 0; s < o; ++s) r[s] = kt(e[s], t[s]);
  for (; s < n; ++s) i[s] = t[s];
  return function(a) {
    for (s = 0; s < o; ++s) i[s] = r[s](a);
    return i;
  };
}
function hu(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(o) {
    return n.setTime(e * (1 - o) + t * o), n;
  };
}
function ze(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function gu(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = kt(e[r], t[r]) : o[r] = t[r];
  return function(i) {
    for (r in n) o[r] = n[r](i);
    return o;
  };
}
var oo = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, Vn = new RegExp(oo.source, "g");
function pu(e) {
  return function() {
    return e;
  };
}
function mu(e) {
  return function(t) {
    return e(t) + "";
  };
}
function ki(e, t) {
  var n = oo.lastIndex = Vn.lastIndex = 0, o, r, i, s = -1, a = [], l = [];
  for (e = e + "", t = t + ""; (o = oo.exec(e)) && (r = Vn.exec(t)); )
    (i = r.index) > n && (i = t.slice(n, i), a[s] ? a[s] += i : a[++s] = i), (o = o[0]) === (r = r[0]) ? a[s] ? a[s] += r : a[++s] = r : (a[++s] = null, l.push({ i: s, x: ze(o, r) })), n = Vn.lastIndex;
  return n < t.length && (i = t.slice(n), a[s] ? a[s] += i : a[++s] = i), a.length < 2 ? l[0] ? mu(l[0].x) : pu(t) : (t = l.length, function(c) {
    for (var u = 0, d; u < t; ++u) a[(d = l[u]).i] = d.x(c);
    return a.join("");
  });
}
function kt(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? So(t) : (n === "number" ? ze : n === "string" ? (o = tt(t)) ? (t = o, dn) : ki : t instanceof tt ? dn : t instanceof Date ? hu : du(t) ? uu : Array.isArray(t) ? fu : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? gu : ze)(e, t);
}
var sr = 180 / Math.PI, ro = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function Ii(e, t, n, o, r, i) {
  var s, a, l;
  return (s = Math.sqrt(e * e + t * t)) && (e /= s, t /= s), (l = e * n + t * o) && (n -= e * l, o -= t * l), (a = Math.sqrt(n * n + o * o)) && (n /= a, o /= a, l /= a), e * o < t * n && (e = -e, t = -t, l = -l, s = -s), {
    translateX: r,
    translateY: i,
    rotate: Math.atan2(t, e) * sr,
    skewX: Math.atan(l) * sr,
    scaleX: s,
    scaleY: a
  };
}
var Ut;
function yu(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? ro : Ii(t.a, t.b, t.c, t.d, t.e, t.f);
}
function xu(e) {
  return e == null || (Ut || (Ut = document.createElementNS("http://www.w3.org/2000/svg", "g")), Ut.setAttribute("transform", e), !(e = Ut.transform.baseVal.consolidate())) ? ro : (e = e.matrix, Ii(e.a, e.b, e.c, e.d, e.e, e.f));
}
function Mi(e, t, n, o) {
  function r(c) {
    return c.length ? c.pop() + " " : "";
  }
  function i(c, u, d, f, g, p) {
    if (c !== d || u !== f) {
      var v = g.push("translate(", null, t, null, n);
      p.push({ i: v - 4, x: ze(c, d) }, { i: v - 2, x: ze(u, f) });
    } else (d || f) && g.push("translate(" + d + t + f + n);
  }
  function s(c, u, d, f) {
    c !== u ? (c - u > 180 ? u += 360 : u - c > 180 && (c += 360), f.push({ i: d.push(r(d) + "rotate(", null, o) - 2, x: ze(c, u) })) : u && d.push(r(d) + "rotate(" + u + o);
  }
  function a(c, u, d, f) {
    c !== u ? f.push({ i: d.push(r(d) + "skewX(", null, o) - 2, x: ze(c, u) }) : u && d.push(r(d) + "skewX(" + u + o);
  }
  function l(c, u, d, f, g, p) {
    if (c !== d || u !== f) {
      var v = g.push(r(g) + "scale(", null, ",", null, ")");
      p.push({ i: v - 4, x: ze(c, d) }, { i: v - 2, x: ze(u, f) });
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
var wu = Mi(yu, "px, ", "px)", "deg)"), vu = Mi(xu, ", ", ")", ")"), bu = 1e-12;
function ar(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Su(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function _u(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const nn = (function e(t, n, o) {
  function r(i, s) {
    var a = i[0], l = i[1], c = i[2], u = s[0], d = s[1], f = s[2], g = u - a, p = d - l, v = g * g + p * p, x, y;
    if (v < bu)
      y = Math.log(f / c) / t, x = function(k) {
        return [
          a + k * g,
          l + k * p,
          c * Math.exp(t * k * y)
        ];
      };
    else {
      var _ = Math.sqrt(v), m = (f * f - c * c + o * v) / (2 * c * n * _), w = (f * f - c * c - o * v) / (2 * f * n * _), C = Math.log(Math.sqrt(m * m + 1) - m), S = Math.log(Math.sqrt(w * w + 1) - w);
      y = (S - C) / t, x = function(k) {
        var D = k * y, P = ar(C), B = c / (n * _) * (P * _u(t * D + C) - Su(C));
        return [
          a + B * g,
          l + B * p,
          c * P / ar(t * D + C)
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
var ft = 0, Nt = 0, St = 0, Ai = 1e3, fn, Ct, hn = 0, nt = 0, _n = 0, Pt = typeof performance == "object" && performance.now ? performance : Date, Di = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function _o() {
  return nt || (Di(Eu), nt = Pt.now() + _n);
}
function Eu() {
  nt = 0;
}
function gn() {
  this._call = this._time = this._next = null;
}
gn.prototype = Pi.prototype = {
  constructor: gn,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? _o() : +n) + (t == null ? 0 : +t), !this._next && Ct !== this && (Ct ? Ct._next = this : fn = this, Ct = this), this._call = e, this._time = n, io();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, io());
  }
};
function Pi(e, t, n) {
  var o = new gn();
  return o.restart(e, t, n), o;
}
function Nu() {
  _o(), ++ft;
  for (var e = fn, t; e; )
    (t = nt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --ft;
}
function cr() {
  nt = (hn = Pt.now()) + _n, ft = Nt = 0;
  try {
    Nu();
  } finally {
    ft = 0, ku(), nt = 0;
  }
}
function Cu() {
  var e = Pt.now(), t = e - hn;
  t > Ai && (_n -= t, hn = e);
}
function ku() {
  for (var e, t = fn, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : fn = n);
  Ct = e, io(o);
}
function io(e) {
  if (!ft) {
    Nt && (Nt = clearTimeout(Nt));
    var t = e - nt;
    t > 24 ? (e < 1 / 0 && (Nt = setTimeout(cr, e - Pt.now() - _n)), St && (St = clearInterval(St))) : (St || (hn = Pt.now(), St = setInterval(Cu, Ai)), ft = 1, Di(cr));
  }
}
function lr(e, t, n) {
  var o = new gn();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var Iu = bn("start", "end", "cancel", "interrupt"), Mu = [], $i = 0, ur = 1, so = 2, on = 3, dr = 4, ao = 5, rn = 6;
function En(e, t, n, o, r, i) {
  var s = e.__transition;
  if (!s) e.__transition = {};
  else if (n in s) return;
  Au(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: Iu,
    tween: Mu,
    time: i.time,
    delay: i.delay,
    duration: i.duration,
    ease: i.ease,
    timer: null,
    state: $i
  });
}
function Eo(e, t) {
  var n = je(e, t);
  if (n.state > $i) throw new Error("too late; already scheduled");
  return n;
}
function Le(e, t) {
  var n = je(e, t);
  if (n.state > on) throw new Error("too late; already running");
  return n;
}
function je(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function Au(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = Pi(i, 0, n.time);
  function i(c) {
    n.state = ur, n.timer.restart(s, n.delay, n.time), n.delay <= c && s(c - n.delay);
  }
  function s(c) {
    var u, d, f, g;
    if (n.state !== ur) return l();
    for (u in o)
      if (g = o[u], g.name === n.name) {
        if (g.state === on) return lr(s);
        g.state === dr ? (g.state = rn, g.timer.stop(), g.on.call("interrupt", e, e.__data__, g.index, g.group), delete o[u]) : +u < t && (g.state = rn, g.timer.stop(), g.on.call("cancel", e, e.__data__, g.index, g.group), delete o[u]);
      }
    if (lr(function() {
      n.state === on && (n.state = dr, n.timer.restart(a, n.delay, n.time), a(c));
    }), n.state = so, n.on.call("start", e, e.__data__, n.index, n.group), n.state === so) {
      for (n.state = on, r = new Array(f = n.tween.length), u = 0, d = -1; u < f; ++u)
        (g = n.tween[u].value.call(e, e.__data__, n.index, n.group)) && (r[++d] = g);
      r.length = d + 1;
    }
  }
  function a(c) {
    for (var u = c < n.duration ? n.ease.call(null, c / n.duration) : (n.timer.restart(l), n.state = ao, 1), d = -1, f = r.length; ++d < f; )
      r[d].call(e, u);
    n.state === ao && (n.on.call("end", e, e.__data__, n.index, n.group), l());
  }
  function l() {
    n.state = rn, n.timer.stop(), delete o[t];
    for (var c in o) return;
    delete e.__transition;
  }
}
function sn(e, t) {
  var n = e.__transition, o, r, i = !0, s;
  if (n) {
    t = t == null ? null : t + "";
    for (s in n) {
      if ((o = n[s]).name !== t) {
        i = !1;
        continue;
      }
      r = o.state > so && o.state < ao, o.state = rn, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[s];
    }
    i && delete e.__transition;
  }
}
function Du(e) {
  return this.each(function() {
    sn(this, e);
  });
}
function Pu(e, t) {
  var n, o;
  return function() {
    var r = Le(this, e), i = r.tween;
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
function $u(e, t, n) {
  var o, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var i = Le(this, e), s = i.tween;
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
function ju(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = je(this.node(), n).tween, r = 0, i = o.length, s; r < i; ++r)
      if ((s = o[r]).name === e)
        return s.value;
    return null;
  }
  return this.each((t == null ? Pu : $u)(n, e, t));
}
function No(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = Le(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return je(r, o).value[t];
  };
}
function ji(e, t) {
  var n;
  return (typeof t == "number" ? ze : t instanceof tt ? dn : (n = tt(t)) ? (t = n, dn) : ki)(e, t);
}
function Tu(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function zu(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Ru(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttribute(e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Lu(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttributeNS(e.space, e.local);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Hu(e, t, n) {
  var o, r, i;
  return function() {
    var s, a = n(this), l;
    return a == null ? void this.removeAttribute(e) : (s = this.getAttribute(e), l = a + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a)));
  };
}
function Vu(e, t, n) {
  var o, r, i;
  return function() {
    var s, a = n(this), l;
    return a == null ? void this.removeAttributeNS(e.space, e.local) : (s = this.getAttributeNS(e.space, e.local), l = a + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a)));
  };
}
function Ou(e, t) {
  var n = Sn(e), o = n === "transform" ? vu : ji;
  return this.attrTween(e, typeof t == "function" ? (n.local ? Vu : Hu)(n, o, No(this, "attr." + e, t)) : t == null ? (n.local ? zu : Tu)(n) : (n.local ? Lu : Ru)(n, o, t));
}
function Bu(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function Fu(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function Yu(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && Fu(e, i)), n;
  }
  return r._value = t, r;
}
function Xu(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && Bu(e, i)), n;
  }
  return r._value = t, r;
}
function Wu(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = Sn(e);
  return this.tween(n, (o.local ? Yu : Xu)(o, t));
}
function qu(e, t) {
  return function() {
    Eo(this, e).delay = +t.apply(this, arguments);
  };
}
function Zu(e, t) {
  return t = +t, function() {
    Eo(this, e).delay = t;
  };
}
function Uu(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? qu : Zu)(t, e)) : je(this.node(), t).delay;
}
function Gu(e, t) {
  return function() {
    Le(this, e).duration = +t.apply(this, arguments);
  };
}
function Ku(e, t) {
  return t = +t, function() {
    Le(this, e).duration = t;
  };
}
function Qu(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Gu : Ku)(t, e)) : je(this.node(), t).duration;
}
function Ju(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    Le(this, e).ease = t;
  };
}
function ed(e) {
  var t = this._id;
  return arguments.length ? this.each(Ju(t, e)) : je(this.node(), t).ease;
}
function td(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    Le(this, e).ease = n;
  };
}
function nd(e) {
  if (typeof e != "function") throw new Error();
  return this.each(td(this._id, e));
}
function od(e) {
  typeof e != "function" && (e = di(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = [], l, c = 0; c < s; ++c)
      (l = i[c]) && e.call(l, l.__data__, c, i) && a.push(l);
  return new Ye(o, this._parents, this._name, this._id);
}
function rd(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, i = Math.min(o, r), s = new Array(o), a = 0; a < i; ++a)
    for (var l = t[a], c = n[a], u = l.length, d = s[a] = new Array(u), f, g = 0; g < u; ++g)
      (f = l[g] || c[g]) && (d[g] = f);
  for (; a < o; ++a)
    s[a] = t[a];
  return new Ye(s, this._parents, this._name, this._id);
}
function id(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function sd(e, t, n) {
  var o, r, i = id(t) ? Eo : Le;
  return function() {
    var s = i(this, e), a = s.on;
    a !== o && (r = (o = a).copy()).on(t, n), s.on = r;
  };
}
function ad(e, t) {
  var n = this._id;
  return arguments.length < 2 ? je(this.node(), n).on.on(e) : this.each(sd(n, e, t));
}
function cd(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function ld() {
  return this.on("end.remove", cd(this._id));
}
function ud(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = wo(e));
  for (var o = this._groups, r = o.length, i = new Array(r), s = 0; s < r; ++s)
    for (var a = o[s], l = a.length, c = i[s] = new Array(l), u, d, f = 0; f < l; ++f)
      (u = a[f]) && (d = e.call(u, u.__data__, f, a)) && ("__data__" in u && (d.__data__ = u.__data__), c[f] = d, En(c[f], t, n, f, c, je(u, n)));
  return new Ye(i, this._parents, t, n);
}
function dd(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = ui(e));
  for (var o = this._groups, r = o.length, i = [], s = [], a = 0; a < r; ++a)
    for (var l = o[a], c = l.length, u, d = 0; d < c; ++d)
      if (u = l[d]) {
        for (var f = e.call(u, u.__data__, d, l), g, p = je(u, n), v = 0, x = f.length; v < x; ++v)
          (g = f[v]) && En(g, t, n, v, f, p);
        i.push(f), s.push(u);
      }
  return new Ye(i, s, t, n);
}
var fd = Ot.prototype.constructor;
function hd() {
  return new fd(this._groups, this._parents);
}
function gd(e, t) {
  var n, o, r;
  return function() {
    var i = dt(this, e), s = (this.style.removeProperty(e), dt(this, e));
    return i === s ? null : i === n && s === o ? r : r = t(n = i, o = s);
  };
}
function Ti(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function pd(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = dt(this, e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function md(e, t, n) {
  var o, r, i;
  return function() {
    var s = dt(this, e), a = n(this), l = a + "";
    return a == null && (l = a = (this.style.removeProperty(e), dt(this, e))), s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a));
  };
}
function yd(e, t) {
  var n, o, r, i = "style." + t, s = "end." + i, a;
  return function() {
    var l = Le(this, e), c = l.on, u = l.value[i] == null ? a || (a = Ti(t)) : void 0;
    (c !== n || r !== u) && (o = (n = c).copy()).on(s, r = u), l.on = o;
  };
}
function xd(e, t, n) {
  var o = (e += "") == "transform" ? wu : ji;
  return t == null ? this.styleTween(e, gd(e, o)).on("end.style." + e, Ti(e)) : typeof t == "function" ? this.styleTween(e, md(e, o, No(this, "style." + e, t))).each(yd(this._id, e)) : this.styleTween(e, pd(e, o, t), n).on("end.style." + e, null);
}
function wd(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function vd(e, t, n) {
  var o, r;
  function i() {
    var s = t.apply(this, arguments);
    return s !== r && (o = (r = s) && wd(e, s, n)), o;
  }
  return i._value = t, i;
}
function bd(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, vd(e, t, n ?? ""));
}
function Sd(e) {
  return function() {
    this.textContent = e;
  };
}
function _d(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function Ed(e) {
  return this.tween("text", typeof e == "function" ? _d(No(this, "text", e)) : Sd(e == null ? "" : e + ""));
}
function Nd(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Cd(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Nd(r)), t;
  }
  return o._value = e, o;
}
function kd(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, Cd(e));
}
function Id() {
  for (var e = this._name, t = this._id, n = zi(), o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], a = s.length, l, c = 0; c < a; ++c)
      if (l = s[c]) {
        var u = je(l, t);
        En(l, e, n, c, s, {
          time: u.time + u.delay + u.duration,
          delay: 0,
          duration: u.duration,
          ease: u.ease
        });
      }
  return new Ye(o, this._parents, e, n);
}
function Md() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(i, s) {
    var a = { value: s }, l = { value: function() {
      --r === 0 && i();
    } };
    n.each(function() {
      var c = Le(this, o), u = c.on;
      u !== e && (t = (e = u).copy(), t._.cancel.push(a), t._.interrupt.push(a), t._.end.push(l)), c.on = t;
    }), r === 0 && i();
  });
}
var Ad = 0;
function Ye(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function zi() {
  return ++Ad;
}
var Oe = Ot.prototype;
Ye.prototype = {
  constructor: Ye,
  select: ud,
  selectAll: dd,
  selectChild: Oe.selectChild,
  selectChildren: Oe.selectChildren,
  filter: od,
  merge: rd,
  selection: hd,
  transition: Id,
  call: Oe.call,
  nodes: Oe.nodes,
  node: Oe.node,
  size: Oe.size,
  empty: Oe.empty,
  each: Oe.each,
  on: ad,
  attr: Ou,
  attrTween: Wu,
  style: xd,
  styleTween: bd,
  text: Ed,
  textTween: kd,
  remove: ld,
  tween: ju,
  delay: Uu,
  duration: Qu,
  ease: ed,
  easeVarying: nd,
  end: Md,
  [Symbol.iterator]: Oe[Symbol.iterator]
};
function Dd(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var Pd = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: Dd
};
function $d(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function jd(e) {
  var t, n;
  e instanceof Ye ? (t = e._id, e = e._name) : (t = zi(), (n = Pd).time = _o(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], a = s.length, l, c = 0; c < a; ++c)
      (l = s[c]) && En(l, e, t, c, s, n || $d(l, t));
  return new Ye(o, this._parents, e, t);
}
Ot.prototype.interrupt = Du;
Ot.prototype.transition = jd;
const Gt = (e) => () => e;
function Td(e, {
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
function Be(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
Be.prototype = {
  constructor: Be,
  scale: function(e) {
    return e === 1 ? this : new Be(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new Be(this.k, this.x + this.k * e, this.y + this.k * t);
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
var Nn = new Be(1, 0, 0);
Ri.prototype = Be.prototype;
function Ri(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return Nn;
  return e.__zoom;
}
function On(e) {
  e.stopImmediatePropagation();
}
function _t(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function zd(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function Rd() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function fr() {
  return this.__zoom || Nn;
}
function Ld(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function Hd() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Vd(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], i = e.invertY(t[0][1]) - n[0][1], s = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    s > i ? (i + s) / 2 : Math.min(0, i) || Math.max(0, s)
  );
}
function Li() {
  var e = zd, t = Rd, n = Vd, o = Ld, r = Hd, i = [0, 1 / 0], s = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], a = 250, l = nn, c = bn("start", "zoom", "end"), u, d, f, g = 500, p = 150, v = 0, x = 10;
  function y(b) {
    b.property("__zoom", fr).on("wheel.zoom", D, { passive: !1 }).on("mousedown.zoom", P).on("dblclick.zoom", B).filter(r).on("touchstart.zoom", M).on("touchmove.zoom", T).on("touchend.zoom touchcancel.zoom", L).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  y.transform = function(b, N, E, I) {
    var j = b.selection ? b.selection() : b;
    j.property("__zoom", fr), b !== j ? C(b, N, E, I) : j.interrupt().each(function() {
      S(this, arguments).event(I).start().zoom(null, typeof N == "function" ? N.apply(this, arguments) : N).end();
    });
  }, y.scaleBy = function(b, N, E, I) {
    y.scaleTo(b, function() {
      var j = this.__zoom.k, A = typeof N == "function" ? N.apply(this, arguments) : N;
      return j * A;
    }, E, I);
  }, y.scaleTo = function(b, N, E, I) {
    y.transform(b, function() {
      var j = t.apply(this, arguments), A = this.__zoom, O = E == null ? w(j) : typeof E == "function" ? E.apply(this, arguments) : E, V = A.invert(O), H = typeof N == "function" ? N.apply(this, arguments) : N;
      return n(m(_(A, H), O, V), j, s);
    }, E, I);
  }, y.translateBy = function(b, N, E, I) {
    y.transform(b, function() {
      return n(this.__zoom.translate(
        typeof N == "function" ? N.apply(this, arguments) : N,
        typeof E == "function" ? E.apply(this, arguments) : E
      ), t.apply(this, arguments), s);
    }, null, I);
  }, y.translateTo = function(b, N, E, I, j) {
    y.transform(b, function() {
      var A = t.apply(this, arguments), O = this.__zoom, V = I == null ? w(A) : typeof I == "function" ? I.apply(this, arguments) : I;
      return n(Nn.translate(V[0], V[1]).scale(O.k).translate(
        typeof N == "function" ? -N.apply(this, arguments) : -N,
        typeof E == "function" ? -E.apply(this, arguments) : -E
      ), A, s);
    }, I, j);
  };
  function _(b, N) {
    return N = Math.max(i[0], Math.min(i[1], N)), N === b.k ? b : new Be(N, b.x, b.y);
  }
  function m(b, N, E) {
    var I = N[0] - E[0] * b.k, j = N[1] - E[1] * b.k;
    return I === b.x && j === b.y ? b : new Be(b.k, I, j);
  }
  function w(b) {
    return [(+b[0][0] + +b[1][0]) / 2, (+b[0][1] + +b[1][1]) / 2];
  }
  function C(b, N, E, I) {
    b.on("start.zoom", function() {
      S(this, arguments).event(I).start();
    }).on("interrupt.zoom end.zoom", function() {
      S(this, arguments).event(I).end();
    }).tween("zoom", function() {
      var j = this, A = arguments, O = S(j, A).event(I), V = t.apply(j, A), H = E == null ? w(V) : typeof E == "function" ? E.apply(j, A) : E, Z = Math.max(V[1][0] - V[0][0], V[1][1] - V[0][1]), W = j.__zoom, G = typeof N == "function" ? N.apply(j, A) : N, ee = l(W.invert(H).concat(Z / W.k), G.invert(H).concat(Z / G.k));
      return function(U) {
        if (U === 1) U = G;
        else {
          var R = ee(U), q = Z / R[2];
          U = new Be(q, H[0] - R[0] * q, H[1] - R[1] * q);
        }
        O.zoom(null, U);
      };
    });
  }
  function S(b, N, E) {
    return !E && b.__zooming || new k(b, N);
  }
  function k(b, N) {
    this.that = b, this.args = N, this.active = 0, this.sourceEvent = null, this.extent = t.apply(b, N), this.taps = 0;
  }
  k.prototype = {
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
        new Td(b, {
          sourceEvent: this.sourceEvent,
          target: y,
          transform: this.that.__zoom,
          dispatch: c
        }),
        N
      );
    }
  };
  function D(b, ...N) {
    if (!e.apply(this, arguments)) return;
    var E = S(this, N).event(b), I = this.__zoom, j = Math.max(i[0], Math.min(i[1], I.k * Math.pow(2, o.apply(this, arguments)))), A = Ie(b);
    if (E.wheel)
      (E.mouse[0][0] !== A[0] || E.mouse[0][1] !== A[1]) && (E.mouse[1] = I.invert(E.mouse[0] = A)), clearTimeout(E.wheel);
    else {
      if (I.k === j) return;
      E.mouse = [A, I.invert(A)], sn(this), E.start();
    }
    _t(b), E.wheel = setTimeout(O, p), E.zoom("mouse", n(m(_(I, j), E.mouse[0], E.mouse[1]), E.extent, s));
    function O() {
      E.wheel = null, E.end();
    }
  }
  function P(b, ...N) {
    if (f || !e.apply(this, arguments)) return;
    var E = b.currentTarget, I = S(this, N, !0).event(b), j = be(b.view).on("mousemove.zoom", H, !0).on("mouseup.zoom", Z, !0), A = Ie(b, E), O = b.clientX, V = b.clientY;
    bi(b.view), On(b), I.mouse = [A, this.__zoom.invert(A)], sn(this), I.start();
    function H(W) {
      if (_t(W), !I.moved) {
        var G = W.clientX - O, ee = W.clientY - V;
        I.moved = G * G + ee * ee > v;
      }
      I.event(W).zoom("mouse", n(m(I.that.__zoom, I.mouse[0] = Ie(W, E), I.mouse[1]), I.extent, s));
    }
    function Z(W) {
      j.on("mousemove.zoom mouseup.zoom", null), Si(W.view, I.moved), _t(W), I.event(W).end();
    }
  }
  function B(b, ...N) {
    if (e.apply(this, arguments)) {
      var E = this.__zoom, I = Ie(b.changedTouches ? b.changedTouches[0] : b, this), j = E.invert(I), A = E.k * (b.shiftKey ? 0.5 : 2), O = n(m(_(E, A), I, j), t.apply(this, N), s);
      _t(b), a > 0 ? be(this).transition().duration(a).call(C, O, I, b) : be(this).call(y.transform, O, I, b);
    }
  }
  function M(b, ...N) {
    if (e.apply(this, arguments)) {
      var E = b.touches, I = E.length, j = S(this, N, b.changedTouches.length === I).event(b), A, O, V, H;
      for (On(b), O = 0; O < I; ++O)
        V = E[O], H = Ie(V, this), H = [H, this.__zoom.invert(H), V.identifier], j.touch0 ? !j.touch1 && j.touch0[2] !== H[2] && (j.touch1 = H, j.taps = 0) : (j.touch0 = H, A = !0, j.taps = 1 + !!u);
      u && (u = clearTimeout(u)), A && (j.taps < 2 && (d = H[0], u = setTimeout(function() {
        u = null;
      }, g)), sn(this), j.start());
    }
  }
  function T(b, ...N) {
    if (this.__zooming) {
      var E = S(this, N).event(b), I = b.changedTouches, j = I.length, A, O, V, H;
      for (_t(b), A = 0; A < j; ++A)
        O = I[A], V = Ie(O, this), E.touch0 && E.touch0[2] === O.identifier ? E.touch0[0] = V : E.touch1 && E.touch1[2] === O.identifier && (E.touch1[0] = V);
      if (O = E.that.__zoom, E.touch1) {
        var Z = E.touch0[0], W = E.touch0[1], G = E.touch1[0], ee = E.touch1[1], U = (U = G[0] - Z[0]) * U + (U = G[1] - Z[1]) * U, R = (R = ee[0] - W[0]) * R + (R = ee[1] - W[1]) * R;
        O = _(O, Math.sqrt(U / R)), V = [(Z[0] + G[0]) / 2, (Z[1] + G[1]) / 2], H = [(W[0] + ee[0]) / 2, (W[1] + ee[1]) / 2];
      } else if (E.touch0) V = E.touch0[0], H = E.touch0[1];
      else return;
      E.zoom("touch", n(m(O, V, H), E.extent, s));
    }
  }
  function L(b, ...N) {
    if (this.__zooming) {
      var E = S(this, N).event(b), I = b.changedTouches, j = I.length, A, O;
      for (On(b), f && clearTimeout(f), f = setTimeout(function() {
        f = null;
      }, g), A = 0; A < j; ++A)
        O = I[A], E.touch0 && E.touch0[2] === O.identifier ? delete E.touch0 : E.touch1 && E.touch1[2] === O.identifier && delete E.touch1;
      if (E.touch1 && !E.touch0 && (E.touch0 = E.touch1, delete E.touch1), E.touch0) E.touch0[1] = this.__zoom.invert(E.touch0[0]);
      else if (E.end(), E.taps === 2 && (O = Ie(O, this), Math.hypot(d[0] - O[0], d[1] - O[1]) < x)) {
        var V = be(this).on("dblclick.zoom");
        V && V.apply(this, arguments);
      }
    }
  }
  return y.wheelDelta = function(b) {
    return arguments.length ? (o = typeof b == "function" ? b : Gt(+b), y) : o;
  }, y.filter = function(b) {
    return arguments.length ? (e = typeof b == "function" ? b : Gt(!!b), y) : e;
  }, y.touchable = function(b) {
    return arguments.length ? (r = typeof b == "function" ? b : Gt(!!b), y) : r;
  }, y.extent = function(b) {
    return arguments.length ? (t = typeof b == "function" ? b : Gt([[+b[0][0], +b[0][1]], [+b[1][0], +b[1][1]]]), y) : t;
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
const $e = {
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
}, $t = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], Hi = ["Enter", " ", "Escape"], Vi = {
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
var ht;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(ht || (ht = {}));
var et;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(et || (et = {}));
var jt;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(jt || (jt = {}));
const Oi = {
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
var Ze;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(Ze || (Ze = {}));
var pn;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(pn || (pn = {}));
var K;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(K || (K = {}));
const hr = {
  [K.Left]: K.Right,
  [K.Right]: K.Left,
  [K.Top]: K.Bottom,
  [K.Bottom]: K.Top
};
function Bi(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Fi = (e) => "id" in e && "source" in e && "target" in e, Od = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), Co = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), Ft = (e, t = [0, 0]) => {
  const { width: n, height: o } = Xe(e), r = e.origin ?? t, i = n * r[0], s = o * r[1];
  return {
    x: e.position.x - i,
    y: e.position.y - s
  };
}, Bd = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const i = typeof r == "string";
    let s = !t.nodeLookup && !i ? r : void 0;
    t.nodeLookup && (s = i ? t.nodeLookup.get(r) : Co(r) ? r : t.nodeLookup.get(r.id));
    const a = s ? mn(s, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return Cn(o, a);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return kn(n);
}, Yt = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = Cn(n, mn(r)), o = !0);
  }), o ? kn(n) : { x: 0, y: 0, width: 0, height: 0 };
}, ko = (e, t, [n, o, r] = [0, 0, 1], i = !1, s = !1) => {
  const a = {
    ...wt(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, l = [];
  for (const c of e.values()) {
    const { measured: u, selectable: d = !0, hidden: f = !1 } = c;
    if (s && !d || f)
      continue;
    const g = u.width ?? c.width ?? c.initialWidth ?? null, p = u.height ?? c.height ?? c.initialHeight ?? null, v = Tt(a, pt(c)), x = (g ?? 0) * (p ?? 0), y = i && v > 0;
    (!c.internals.handleBounds || y || v >= x || c.dragging) && l.push(c);
  }
  return l;
}, Fd = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function Yd(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function Xd({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: i }, s) {
  if (e.size === 0)
    return !0;
  const a = Yd(e, s), l = Yt(a), c = Mo(l, t, n, s?.minZoom ?? r, s?.maxZoom ?? i, s?.padding ?? 0.1);
  return await o.setViewport(c, {
    duration: s?.duration,
    ease: s?.ease,
    interpolate: s?.interpolate
  }), !0;
}
function Yi({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: i }) {
  const s = n.get(e), a = s.parentId ? n.get(s.parentId) : void 0, { x: l, y: c } = a ? a.internals.positionAbsolute : { x: 0, y: 0 }, u = s.origin ?? o;
  let d = s.extent || r;
  if (s.extent === "parent" && !s.expandParent)
    if (!a)
      i?.("005", $e.error005());
    else {
      const g = a.measured.width, p = a.measured.height;
      g && p && (d = [
        [l, c],
        [l + g, c + p]
      ]);
    }
  else a && rt(s.extent) && (d = [
    [s.extent[0][0] + l, s.extent[0][1] + c],
    [s.extent[1][0] + l, s.extent[1][1] + c]
  ]);
  const f = rt(d) ? ot(t, d, s.measured) : t;
  return (s.measured.width === void 0 || s.measured.height === void 0) && i?.("015", $e.error015()), {
    position: {
      x: f.x - l + (s.measured.width ?? 0) * u[0],
      y: f.y - c + (s.measured.height ?? 0) * u[1]
    },
    positionAbsolute: f
  };
}
async function Wd({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const i = new Set(e.map((f) => f.id)), s = [];
  for (const f of n) {
    if (f.deletable === !1)
      continue;
    const g = i.has(f.id), p = !g && f.parentId && s.find((v) => v.id === f.parentId);
    (g || p) && s.push(f);
  }
  const a = new Set(t.map((f) => f.id)), l = o.filter((f) => f.deletable !== !1), u = Fd(s, l);
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
const gt = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), ot = (e = { x: 0, y: 0 }, t, n) => ({
  x: gt(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: gt(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Xi(e, t, n) {
  const { width: o, height: r } = Xe(n), { x: i, y: s } = n.internals.positionAbsolute;
  return ot(e, [
    [i, s],
    [i + o, s + r]
  ], t);
}
const gr = (e, t, n) => e < t ? gt(Math.abs(e - t), 1, t) / t : e > n ? -gt(Math.abs(e - n), 1, t) / t : 0, Io = (e, t, n = 15, o = 40) => {
  const r = gr(e.x, o, t.width - o) * n, i = gr(e.y, o, t.height - o) * n;
  return [r, i];
}, Cn = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), co = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), kn = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), pt = (e, t = [0, 0]) => {
  const { x: n, y: o } = Co(e) ? e.internals.positionAbsolute : Ft(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, mn = (e, t = [0, 0]) => {
  const { x: n, y: o } = Co(e) ? e.internals.positionAbsolute : Ft(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, Wi = (e, t) => kn(Cn(co(e), co(t))), Tt = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, pr = (e) => Ae(e.width) && Ae(e.height) && Ae(e.x) && Ae(e.y), Ae = (e) => !isNaN(e) && isFinite(e), qi = (e, t) => (n, o) => {
}, Xt = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), wt = ({ x: e, y: t }, [n, o, r], i = !1, s = [1, 1]) => {
  const a = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return i ? Xt(a, s) : a;
}, mt = ({ x: e, y: t }, [n, o, r]) => ({
  x: e * r + n,
  y: t * r + o
});
function st(e, t) {
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
function qd(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = st(e, n), r = st(e, t);
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
    const o = st(e.top ?? e.y ?? 0, n), r = st(e.bottom ?? e.y ?? 0, n), i = st(e.left ?? e.x ?? 0, t), s = st(e.right ?? e.x ?? 0, t);
    return { top: o, right: s, bottom: r, left: i, x: i + s, y: o + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function Zd(e, t, n, o, r, i) {
  const { x: s, y: a } = mt(e, [t, n, o]), { x: l, y: c } = mt({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), u = r - l, d = i - c;
  return {
    left: Math.floor(s),
    top: Math.floor(a),
    right: Math.floor(u),
    bottom: Math.floor(d)
  };
}
const Mo = (e, t, n, o, r, i) => {
  const s = qd(i, t, n), a = (t - s.x) / e.width, l = (n - s.y) / e.height, c = Math.min(a, l), u = gt(c, o, r), d = e.x + e.width / 2, f = e.y + e.height / 2, g = t / 2 - d * u, p = n / 2 - f * u, v = Zd(e, g, p, u, t, n), x = {
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
}, zt = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function rt(e) {
  return e != null && e !== "parent";
}
function Xe(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function Zi(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function Ui(e, t = { width: 0, height: 0 }, n, o, r) {
  const i = { ...e }, s = o.get(n);
  if (s) {
    const a = s.origin || r;
    i.x += s.internals.positionAbsolute.x - (t.width ?? 0) * a[0], i.y += s.internals.positionAbsolute.y - (t.height ?? 0) * a[1];
  }
  return i;
}
function mr(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function Ud() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function Gd(e) {
  return { ...Vi, ...e || {} };
}
function It(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: i, y: s } = De(e), a = wt({ x: i - (r?.left ?? 0), y: s - (r?.top ?? 0) }, o), { x: l, y: c } = n ? Xt(a, t) : a;
  return {
    xSnapped: l,
    ySnapped: c,
    ...a
  };
}
const Ao = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), Gi = (e) => e?.getRootNode?.() || window?.document, Kd = ["INPUT", "SELECT", "TEXTAREA"];
function Ki(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : Kd.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const Qi = (e) => "clientX" in e, De = (e, t) => {
  const n = Qi(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, yr = (e, t, n, o, r) => {
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
      ...Ao(s)
    };
  });
};
function Ji({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: i, targetControlX: s, targetControlY: a }) {
  const l = e * 0.125 + r * 0.375 + s * 0.375 + n * 0.125, c = t * 0.125 + i * 0.375 + a * 0.375 + o * 0.125, u = Math.abs(l - e), d = Math.abs(c - t);
  return [l, c, u, d];
}
function Kt(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function xr({ pos: e, x1: t, y1: n, x2: o, y2: r, c: i }) {
  switch (e) {
    case K.Left:
      return [t - Kt(t - o, i), n];
    case K.Right:
      return [t + Kt(o - t, i), n];
    case K.Top:
      return [t, n - Kt(n - r, i)];
    case K.Bottom:
      return [t, n + Kt(r - n, i)];
  }
}
function es({ sourceX: e, sourceY: t, sourcePosition: n = K.Bottom, targetX: o, targetY: r, targetPosition: i = K.Top, curvature: s = 0.25 }) {
  const [a, l] = xr({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: s
  }), [c, u] = xr({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: s
  }), [d, f, g, p] = Ji({
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
function ts({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, i = n < e ? n + r : n - r, s = Math.abs(o - t) / 2, a = o < t ? o + s : o - s;
  return [i, a, r, s];
}
function Qd({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: i = "basic" }) {
  if (i === "manual")
    return o;
  const s = r && n ? o + 1e3 : o, a = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return s + a;
}
function Jd({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const i = Cn(mn(e), mn(t));
  i.x === i.x2 && (i.x2 += 1), i.y === i.y2 && (i.y2 += 1);
  const s = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return Tt(s, kn(i)) > 0;
}
const ef = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, tf = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), nf = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", $e.error006()), t;
  const o = n.getEdgeId || ef;
  let r;
  return Fi(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, tf(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
};
function ns({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, i, s, a] = ts({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, i, s, a];
}
const wr = {
  [K.Left]: { x: -1, y: 0 },
  [K.Right]: { x: 1, y: 0 },
  [K.Top]: { x: 0, y: -1 },
  [K.Bottom]: { x: 0, y: 1 }
}, of = ({ source: e, sourcePosition: t = K.Bottom, target: n }) => t === K.Left || t === K.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, vr = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function rf({ source: e, sourcePosition: t = K.Bottom, target: n, targetPosition: o = K.Top, center: r, offset: i, stepPosition: s }) {
  const a = wr[t], l = wr[o], c = { x: e.x + a.x * i, y: e.y + a.y * i }, u = { x: n.x + l.x * i, y: n.y + l.y * i }, d = of({
    source: c,
    sourcePosition: t,
    target: u
  }), f = d.x !== 0 ? "x" : "y", g = d[f];
  let p = [], v, x;
  const y = { x: 0, y: 0 }, _ = { x: 0, y: 0 }, [, , m, w] = ts({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (a[f] * l[f] === -1) {
    f === "x" ? (v = r.x ?? c.x + (u.x - c.x) * s, x = r.y ?? (c.y + u.y) / 2) : (v = r.x ?? (c.x + u.x) / 2, x = r.y ?? c.y + (u.y - c.y) * s);
    const D = [
      { x: v, y: c.y },
      { x: v, y: u.y }
    ], P = [
      { x: c.x, y: x },
      { x: u.x, y: x }
    ];
    a[f] === g ? p = f === "x" ? D : P : p = f === "x" ? P : D;
  } else {
    const D = [{ x: c.x, y: u.y }], P = [{ x: u.x, y: c.y }];
    if (f === "x" ? p = a.x === g ? P : D : p = a.y === g ? D : P, t === o) {
      const b = Math.abs(e[f] - n[f]);
      if (b <= i) {
        const N = Math.min(i - 1, i - b);
        a[f] === g ? y[f] = (c[f] > e[f] ? -1 : 1) * N : _[f] = (u[f] > n[f] ? -1 : 1) * N;
      }
    }
    if (t !== o) {
      const b = f === "x" ? "y" : "x", N = a[f] === l[b], E = c[b] > u[b], I = c[b] < u[b];
      (a[f] === 1 && (!N && E || N && I) || a[f] !== 1 && (!N && I || N && E)) && (p = f === "x" ? D : P);
    }
    const B = { x: c.x + y.x, y: c.y + y.y }, M = { x: u.x + _.x, y: u.y + _.y }, T = Math.max(Math.abs(B.x - p[0].x), Math.abs(M.x - p[0].x)), L = Math.max(Math.abs(B.y - p[0].y), Math.abs(M.y - p[0].y));
    T >= L ? (v = (B.x + M.x) / 2, x = p[0].y) : (v = p[0].x, x = (B.y + M.y) / 2);
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
function sf(e, t, n, o) {
  const r = Math.min(vr(e, t) / 2, vr(t, n) / 2, o), { x: i, y: s } = t;
  if (e.x === i && i === n.x || e.y === s && s === n.y)
    return `L${i} ${s}`;
  if (e.y === s) {
    const c = e.x < n.x ? -1 : 1, u = e.y < n.y ? 1 : -1;
    return `L ${i + r * c},${s}Q ${i},${s} ${i},${s + r * u}`;
  }
  const a = e.x < n.x ? 1 : -1, l = e.y < n.y ? -1 : 1;
  return `L ${i},${s + r * l}Q ${i},${s} ${i + r * a},${s}`;
}
function lo({ sourceX: e, sourceY: t, sourcePosition: n = K.Bottom, targetX: o, targetY: r, targetPosition: i = K.Top, borderRadius: s = 5, centerX: a, centerY: l, offset: c = 20, stepPosition: u = 0.5 }) {
  const [d, f, g, p, v] = rf({
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
    x += sf(d[y - 1], d[y], d[y + 1], s);
  return x += `L${d[d.length - 1].x} ${d[d.length - 1].y}`, [x, f, g, p, v];
}
function br(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function af(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!br(t) || !br(n))
    return null;
  const o = t.internals.handleBounds || Sr(t.handles), r = n.internals.handleBounds || Sr(n.handles), i = _r(o?.source ?? [], e.sourceHandle), s = _r(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === ht.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!i || !s)
    return e.onError?.("008", $e.error008(i ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const a = i?.position || K.Bottom, l = s?.position || K.Top, c = it(t, i, a), u = it(n, s, l);
  return {
    sourceX: c.x,
    sourceY: c.y,
    targetX: u.x,
    targetY: u.y,
    sourcePosition: a,
    targetPosition: l
  };
}
function Sr(e) {
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
function it(e, t, n = K.Left, o = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, i = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: s, height: a } = t ?? Xe(e);
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
function _r(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function uo(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function cf(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const i = /* @__PURE__ */ new Set();
  return e.reduce((s, a) => ([a.markerStart || o, a.markerEnd || r].forEach((l) => {
    if (l && typeof l == "object") {
      const c = uo(l, t);
      i.has(c) || (s.push({ id: c, color: l.color || n, ...l }), i.add(c));
    }
  }), s), []).sort((s, a) => s.id.localeCompare(a.id));
}
const os = 1e3, lf = 10, Do = {
  nodeOrigin: [0, 0],
  nodeExtent: $t,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, uf = {
  ...Do,
  checkEquality: !0
};
function Po(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function df(e, t, n) {
  const o = Po(Do, n);
  for (const r of e.values())
    if (r.parentId)
      jo(r, e, t, o);
    else {
      const i = Ft(r, o.nodeOrigin), s = rt(r.extent) ? r.extent : o.nodeExtent, a = ot(i, s, Xe(r));
      r.internals.positionAbsolute = a;
    }
}
function ff(e, t) {
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
function $o(e) {
  return e === "manual";
}
function fo(e, t, n, o = {}) {
  const r = Po(uf, o), i = { i: 0 }, s = new Map(t), a = r?.elevateNodesOnSelect && !$o(r.zIndexMode) ? os : 0;
  let l = e.length > 0, c = !1;
  t.clear(), n.clear();
  for (const u of e) {
    let d = s.get(u.id);
    if (r.checkEquality && u === d?.internals.userNode)
      t.set(u.id, d);
    else {
      const f = Ft(u, r.nodeOrigin), g = rt(u.extent) ? u.extent : r.nodeExtent, p = ot(f, g, Xe(u));
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
          handleBounds: ff(u, d),
          z: rs(u, a, r.zIndexMode),
          userNode: u
        }
      }, t.set(u.id, d);
    }
    (d.measured === void 0 || d.measured.width === void 0 || d.measured.height === void 0) && !d.hidden && (l = !1), u.parentId && jo(d, t, n, o, i), c ||= u.selected ?? !1;
  }
  return { nodesInitialized: l, hasSelectedNodes: c };
}
function hf(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function jo(e, t, n, o, r) {
  const { elevateNodesOnSelect: i, nodeOrigin: s, nodeExtent: a, zIndexMode: l } = Po(Do, o), c = e.parentId, u = t.get(c);
  if (!u) {
    console.warn(`Parent node ${c} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  hf(e, n), r && !u.parentId && u.internals.rootParentIndex === void 0 && l === "auto" && (u.internals.rootParentIndex = ++r.i, u.internals.z = u.internals.z + r.i * lf), r && u.internals.rootParentIndex !== void 0 && (r.i = u.internals.rootParentIndex);
  const d = i && !$o(l) ? os : 0, { x: f, y: g, z: p } = gf(e, u, s, a, d, l), { positionAbsolute: v } = e.internals, x = f !== v.x || g !== v.y;
  (x || p !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: x ? { x: f, y: g } : v,
      z: p
    }
  });
}
function rs(e, t, n) {
  const o = Ae(e.zIndex) ? e.zIndex : 0;
  return $o(n) ? o : o + (e.selected ? t : 0);
}
function gf(e, t, n, o, r, i) {
  const { x: s, y: a } = t.internals.positionAbsolute, l = Xe(e), c = Ft(e, n), u = rt(e.extent) ? ot(c, e.extent, l) : c;
  let d = ot({ x: s + u.x, y: a + u.y }, o, l);
  e.extent === "parent" && (d = Xi(d, l, t));
  const f = rs(e, r, i), g = t.internals.z ?? 0;
  return {
    x: d.x,
    y: d.y,
    z: g >= f ? g + 1 : f
  };
}
function To(e, t, n, o = [0, 0]) {
  const r = [], i = /* @__PURE__ */ new Map();
  for (const s of e) {
    const a = t.get(s.parentId);
    if (!a)
      continue;
    const l = i.get(s.parentId)?.expandedRect ?? pt(a), c = Wi(l, s.rect);
    i.set(s.parentId, { expandedRect: c, parent: a });
  }
  return i.size > 0 && i.forEach(({ expandedRect: s, parent: a }, l) => {
    const c = a.internals.positionAbsolute, u = Xe(a), d = a.origin ?? o, f = s.x < c.x ? Math.round(Math.abs(c.x - s.x)) : 0, g = s.y < c.y ? Math.round(Math.abs(c.y - s.y)) : 0, p = Math.max(u.width, Math.round(s.width)), v = Math.max(u.height, Math.round(s.height)), x = (p - u.width) * d[0], y = (v - u.height) * d[1];
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
function pf(e, t, n, o, r, i, s) {
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
    const v = Ao(g.nodeElement), x = p.measured.width !== v.width || p.measured.height !== v.height;
    if (!!(v.width && v.height && (x || !p.internals.handleBounds || g.force))) {
      const _ = g.nodeElement.getBoundingClientRect(), m = rt(p.extent) ? p.extent : i;
      let { positionAbsolute: w } = p.internals;
      p.parentId && p.extent === "parent" ? w = Xi(w, v, t.get(p.parentId)) : m && (w = ot(w, m, v));
      const C = {
        ...p,
        measured: v,
        internals: {
          ...p.internals,
          positionAbsolute: w,
          handleBounds: {
            source: yr("source", g.nodeElement, _, d, p.id),
            target: yr("target", g.nodeElement, _, d, p.id)
          }
        }
      };
      t.set(p.id, C), p.parentId && jo(C, t, n, { nodeOrigin: r, zIndexMode: s }), l = !0, x && (c.push({
        id: p.id,
        type: "dimensions",
        dimensions: v
      }), p.expandParent && p.parentId && f.push({
        id: p.id,
        parentId: p.parentId,
        rect: pt(C, r)
      }));
    }
  }
  if (f.length > 0) {
    const g = To(f, t, n, r);
    c.push(...g);
  }
  return { changes: c, updatedInternals: l };
}
async function mf({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: i }) {
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
function Er(e, t, n, o, r, i) {
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
function is(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: i, sourceHandle: s = null, targetHandle: a = null } = o, l = { edgeId: o.id, source: r, target: i, sourceHandle: s, targetHandle: a }, c = `${r}-${s}--${i}-${a}`, u = `${i}-${a}--${r}-${s}`;
    Er("source", l, u, e, r, s), Er("target", l, c, e, i, a), t.set(o.id, o);
  }
}
function ss(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : ss(n, t) : !1;
}
function Nr(e, t, n) {
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
function yf(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [i, s] of e)
    if ((s.selected || s.id === o) && (!s.parentId || !ss(s, e)) && (s.draggable || t && typeof s.draggable > "u")) {
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
function Bn({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
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
function xf({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const i = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, s = Xt(i, t);
  return {
    x: s.x - i.x,
    y: s.y - i.y
  };
}
function wf({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let i = { x: null, y: null }, s = 0, a = /* @__PURE__ */ new Map(), l = !1, c = { x: 0, y: 0 }, u = null, d = !1, f = null, g = !1, p = !1, v = null;
  function x({ noDragClassName: _, handleSelector: m, domNode: w, isSelectable: C, nodeId: S, nodeClickDistance: k = 0 }) {
    f = be(w);
    function D({ x: T, y: L }) {
      const { nodeLookup: b, nodeExtent: N, snapGrid: E, snapToGrid: I, nodeOrigin: j, onNodeDrag: A, onSelectionDrag: O, onError: V, updateNodePositions: H } = t();
      i = { x: T, y: L };
      let Z = !1;
      const W = a.size > 1, G = W && N ? co(Yt(a)) : null, ee = W && I ? xf({
        dragItems: a,
        snapGrid: E,
        x: T,
        y: L
      }) : null;
      for (const [U, R] of a) {
        if (!b.has(U))
          continue;
        let q = { x: T - R.distance.x, y: L - R.distance.y };
        I && (q = ee ? {
          x: Math.round(q.x + ee.x),
          y: Math.round(q.y + ee.y)
        } : Xt(q, E));
        let te = null;
        if (W && N && !R.extent && G) {
          const { positionAbsolute: F } = R.internals, Q = F.x - G.x + N[0][0], ne = F.x + R.measured.width - G.x2 + N[1][0], de = F.y - G.y + N[0][1], ye = F.y + R.measured.height - G.y2 + N[1][1];
          te = [
            [Q, de],
            [ne, ye]
          ];
        }
        const { position: J, positionAbsolute: $ } = Yi({
          nodeId: U,
          nextPosition: q,
          nodeLookup: b,
          nodeExtent: te || N,
          nodeOrigin: j,
          onError: V
        });
        Z = Z || R.position.x !== J.x || R.position.y !== J.y, R.position = J, R.internals.positionAbsolute = $;
      }
      if (p = p || Z, !!Z && (H(a, !0), v && (o || A || !S && O))) {
        const [U, R] = Bn({
          nodeId: S,
          dragItems: a,
          nodeLookup: b
        });
        o?.(v, a, U, R), A?.(v, U, R), S || O?.(v, R);
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
      const [E, I] = Io(c, u, b);
      (E !== 0 || I !== 0) && (i.x = (i.x ?? 0) - E / T[2], i.y = (i.y ?? 0) - I / T[2], await L({ x: E, y: I }) && D(i)), s = requestAnimationFrame(P);
    }
    function B(T) {
      const { nodeLookup: L, multiSelectionActive: b, nodesDraggable: N, transform: E, snapGrid: I, snapToGrid: j, selectNodesOnDrag: A, onNodeDragStart: O, onSelectionDragStart: V, unselectNodesAndEdges: H } = t();
      d = !0, (!A || !C) && !b && S && (L.get(S)?.selected || H()), C && A && S && e?.(S);
      const Z = It(T.sourceEvent, { transform: E, snapGrid: I, snapToGrid: j, containerBounds: u });
      if (i = Z, a = yf(L, N, Z, S), a.size > 0 && (n || O || !S && V)) {
        const [W, G] = Bn({
          nodeId: S,
          dragItems: a,
          nodeLookup: L
        });
        n?.(T.sourceEvent, a, W, G), O?.(T.sourceEvent, W, G), S || V?.(T.sourceEvent, G);
      }
    }
    const M = _i().clickDistance(k).on("start", (T) => {
      const { domNode: L, nodeDragThreshold: b, transform: N, snapGrid: E, snapToGrid: I } = t();
      u = L?.getBoundingClientRect() || null, g = !1, p = !1, v = T.sourceEvent, b === 0 && B(T), i = It(T.sourceEvent, { transform: N, snapGrid: E, snapToGrid: I, containerBounds: u }), c = De(T.sourceEvent, u);
    }).on("drag", (T) => {
      const { autoPanOnNodeDrag: L, transform: b, snapGrid: N, snapToGrid: E, nodeDragThreshold: I, nodeLookup: j } = t(), A = It(T.sourceEvent, { transform: b, snapGrid: N, snapToGrid: E, containerBounds: u });
      if (v = T.sourceEvent, (T.sourceEvent.type === "touchmove" && T.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      S && !j.has(S)) && (g = !0), !g) {
        if (!l && L && d && (l = !0, P()), !d) {
          const O = De(T.sourceEvent, u), V = O.x - c.x, H = O.y - c.y;
          Math.sqrt(V * V + H * H) > I && B(T);
        }
        (i.x !== A.xSnapped || i.y !== A.ySnapped) && a && d && (c = De(T.sourceEvent, u), D(A));
      }
    }).on("end", (T) => {
      if (!d || g) {
        g && a.size > 0 && t().updateNodePositions(a, !1);
        return;
      }
      if (l = !1, d = !1, cancelAnimationFrame(s), a.size > 0) {
        const { nodeLookup: L, updateNodePositions: b, onNodeDragStop: N, onSelectionDragStop: E } = t();
        if (p && (b(a, !1), p = !1), r || N || !S && E) {
          const [I, j] = Bn({
            nodeId: S,
            dragItems: a,
            nodeLookup: L,
            dragging: !1
          });
          r?.(T.sourceEvent, a, I, j), N?.(T.sourceEvent, I, j), S || E?.(T.sourceEvent, j);
        }
      }
    }).filter((T) => {
      const L = T.target;
      return !T.button && (!_ || !Nr(L, `.${_}`, w)) && (!m || Nr(L, m, w));
    });
    f.call(M);
  }
  function y() {
    f?.on(".drag", null);
  }
  return {
    update: x,
    destroy: y
  };
}
function vf(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const i of t.values())
    Tt(r, pt(i)) > 0 && o.push(i);
  return o;
}
const bf = 250;
function Sf(e, t, n, o) {
  let r = [], i = 1 / 0;
  const s = vf(e, n, t + bf);
  for (const a of s) {
    const l = [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []];
    for (const c of l) {
      if (o.nodeId === c.nodeId && o.type === c.type && o.id === c.id)
        continue;
      const { x: u, y: d } = it(a, c, c.position, !0), f = Math.sqrt(Math.pow(u - e.x, 2) + Math.pow(d - e.y, 2));
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
function as(e, t, n, o, r, i = !1) {
  const s = o.get(e);
  if (!s)
    return null;
  const a = r === "strict" ? s.internals.handleBounds?.[t] : [...s.internals.handleBounds?.source ?? [], ...s.internals.handleBounds?.target ?? []], l = (n ? a?.find((c) => c.id === n) : a?.[0]) ?? null;
  return l && i ? { ...l, ...it(s, l, l.position, !0) } : l;
}
function cs(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function _f(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const ls = () => !0;
function Ef(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: i, isTarget: s, domNode: a, nodeLookup: l, lib: c, autoPanOnConnect: u, flowId: d, panBy: f, cancelConnection: g, onConnectStart: p, onConnect: v, onConnectEnd: x, isValidConnection: y = ls, onReconnectEnd: _, updateConnection: m, getTransform: w, getFromHandle: C, autoPanSpeed: S, dragThreshold: k = 1, handleDomNode: D }) {
  const P = Gi(e.target);
  let B = 0, M;
  const { x: T, y: L } = De(e), b = cs(i, D), N = a?.getBoundingClientRect();
  let E = !1;
  if (!N || !b)
    return;
  const I = as(r, b, o, l, t);
  if (!I)
    return;
  let j = De(e, N), A = !1, O = null, V = !1, H = null;
  function Z() {
    if (!u || !N)
      return;
    const [J, $] = Io(j, N, S);
    f({ x: J, y: $ }), B = requestAnimationFrame(Z);
  }
  const W = {
    ...I,
    nodeId: r,
    type: b,
    position: I.position
  }, G = l.get(r);
  let U = {
    inProgress: !0,
    isValid: null,
    from: it(G, W, K.Left, !0),
    fromHandle: W,
    fromPosition: W.position,
    fromNode: G,
    to: j,
    toHandle: null,
    toPosition: hr[W.position],
    toNode: null,
    pointer: j
  };
  function R() {
    E = !0, m(U), p?.(e, { nodeId: r, handleId: o, handleType: b });
  }
  k === 0 && R();
  function q(J) {
    if (!E) {
      const { x: ye, y: Ne } = De(J), _e = ye - T, Ee = Ne - L;
      if (!(_e * _e + Ee * Ee > k * k))
        return;
      R();
    }
    if (!C() || !W) {
      te(J);
      return;
    }
    const $ = w();
    j = De(J, N), M = Sf(wt(j, $, !1, [1, 1]), n, l, W), A || (Z(), A = !0);
    const F = us(J, {
      handle: M,
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
    H = F.handleDomNode, O = F.connection, V = _f(!!M, F.isValid);
    const Q = l.get(r), ne = Q ? it(Q, W, K.Left, !0) : U.from, de = {
      ...U,
      from: ne,
      isValid: V,
      to: F.toHandle && V ? mt({ x: F.toHandle.x, y: F.toHandle.y }, $) : j,
      toHandle: F.toHandle,
      toPosition: V && F.toHandle ? F.toHandle.position : hr[W.position],
      toNode: F.toHandle ? l.get(F.toHandle.nodeId) : null,
      pointer: j
    };
    m(de), U = de;
  }
  function te(J) {
    if (!("touches" in J && J.touches.length > 0)) {
      if (E) {
        (M || H) && O && V && v?.(O);
        const { inProgress: $, ...F } = U, Q = {
          ...F,
          toPosition: U.toHandle ? U.toPosition : null
        };
        x?.(J, Q), i && _?.(J, Q);
      }
      g(), cancelAnimationFrame(B), A = !1, V = !1, O = null, H = null, P.removeEventListener("mousemove", q), P.removeEventListener("mouseup", te), P.removeEventListener("touchmove", q), P.removeEventListener("touchend", te);
    }
  }
  P.addEventListener("mousemove", q), P.addEventListener("mouseup", te), P.addEventListener("touchmove", q), P.addEventListener("touchend", te);
}
function us(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: i, doc: s, lib: a, flowId: l, isValidConnection: c = ls, nodeLookup: u }) {
  const d = i === "target", f = t ? s.querySelector(`.${a}-flow__handle[data-id="${l}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: g, y: p } = De(e), v = s.elementFromPoint(g, p), x = v?.classList.contains(`${a}-flow__handle`) ? v : f, y = {
    handleDomNode: x,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (x) {
    const _ = cs(void 0, x), m = x.getAttribute("data-nodeid"), w = x.getAttribute("data-handleid"), C = x.classList.contains("connectable"), S = x.classList.contains("connectableend");
    if (!m || !_)
      return y;
    const k = {
      source: d ? m : o,
      sourceHandle: d ? w : r,
      target: d ? o : m,
      targetHandle: d ? r : w
    };
    y.connection = k;
    const P = C && S && (n === ht.Strict ? d && _ === "source" || !d && _ === "target" : m !== o || w !== r);
    y.isValid = P && c(k), y.toHandle = as(m, _, w, u, n, !0);
  }
  return y;
}
const ho = {
  onPointerDown: Ef,
  isValid: us
};
function Nf({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = be(e);
  function i({ translateExtent: a, width: l, height: c, zoomStep: u = 1, pannable: d = !0, zoomable: f = !0, inversePan: g = !1 }) {
    const p = (m) => {
      if (m.sourceEvent.type !== "wheel" || !t)
        return;
      const w = n(), C = m.sourceEvent.ctrlKey && zt() ? 10 : 1, S = -m.sourceEvent.deltaY * (m.sourceEvent.deltaMode === 1 ? 0.05 : m.sourceEvent.deltaMode ? 1 : 2e-3) * u, k = w[2] * Math.pow(2, S * C);
      t.scaleTo(k);
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
      const k = o() * Math.max(w[2], Math.log(w[2])) * (g ? -1 : 1), D = {
        x: w[0] - S[0] * k,
        y: w[1] - S[1] * k
      }, P = [
        [0, 0],
        [l, c]
      ];
      t.setViewportConstrained({
        x: D.x,
        y: D.y,
        zoom: w[2]
      }, P, a);
    }, _ = Li().on("start", x).on("zoom", d ? y : null).on("zoom.wheel", f ? p : null);
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
const In = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), Fn = ({ x: e, y: t, zoom: n }) => Nn.translate(e, t).scale(n), at = (e, t) => e.target.closest(`.${t}`), ds = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Cf = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, Yn = (e, t = 0, n = Cf, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, fs = (e) => {
  const t = e.ctrlKey && zt() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function kf({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: i, zoomOnPinch: s, onPanZoomStart: a, onPanZoom: l, onPanZoomEnd: c }) {
  return (u) => {
    if (at(u, t))
      return u.ctrlKey && u.preventDefault(), !1;
    u.preventDefault(), u.stopImmediatePropagation();
    const d = n.property("__zoom").k || 1;
    if (u.ctrlKey && s) {
      const x = Ie(u), y = fs(u), _ = d * Math.pow(2, y);
      o.scaleTo(n, _, x, u);
      return;
    }
    const f = u.deltaMode === 1 ? 20 : 1;
    let g = r === et.Vertical ? 0 : u.deltaX * f, p = r === et.Horizontal ? 0 : u.deltaY * f;
    !zt() && u.shiftKey && r !== et.Vertical && (g = u.deltaY * f, p = 0), o.translateBy(
      n,
      -(g / d) * i,
      -(p / d) * i,
      // @ts-ignore
      { internal: !0 }
    );
    const v = In(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (l?.(u, v), e.panScrollTimeout = setTimeout(() => {
      c?.(u, v), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, a?.(u, v));
  };
}
function If({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const i = o.type === "wheel", s = !t && i && !o.ctrlKey, a = at(o, e);
    if (o.ctrlKey && i && a && o.preventDefault(), s || a)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function Mf({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = In(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function Af({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (i) => {
    e.usedRightMouseButton = !!(n && ds(t, e.mouseButton ?? 0)), i.sourceEvent?.sync || o([i.transform.x, i.transform.y, i.transform.k]), r && !i.sourceEvent?.internal && r?.(i.sourceEvent, In(i.transform));
  };
}
function Df({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: i }) {
  return (s) => {
    if (!s.sourceEvent?.internal && (e.isZoomingOrPanning = !1, i && ds(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && s.sourceEvent && i(s.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
      const a = In(s.transform);
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
function Pf({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: i, userSelectionActive: s, noWheelClassName: a, noPanClassName: l, lib: c, connectionInProgress: u }) {
  return (d) => {
    const f = e || t, g = n && d.ctrlKey, p = d.type === "wheel";
    if (d.button === 1 && d.type === "mousedown" && (at(d, `${c}-flow__node`) || at(d, `${c}-flow__edge`)))
      return !0;
    if (!o && !f && !r && !i && !n || s || u && !p || at(d, a) && p || at(d, l) && (!p || r && p && !e) || !n && d.ctrlKey && p)
      return !1;
    if (!n && d.type === "touchstart" && d.touches?.length > 1)
      return d.preventDefault(), !1;
    if (!f && !r && !g && p || !o && (d.type === "mousedown" || d.type === "touchstart") || Array.isArray(o) && !o.includes(d.button) && d.type === "mousedown")
      return !1;
    const v = Array.isArray(o) && o.includes(d.button) || !d.button || d.button <= 1;
    return (!d.ctrlKey || p) && v;
  };
}
function $f({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: i, onPanZoomStart: s, onPanZoomEnd: a, onDraggingChange: l }) {
  const c = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, u = e.getBoundingClientRect(), d = Li().scaleExtent([t, n]).translateExtent(o), f = be(e).call(d);
  _({
    x: r.x,
    y: r.y,
    zoom: gt(r.zoom, t, n)
  }, [
    [0, 0],
    [u.width, u.height]
  ], o);
  const g = f.on("wheel.zoom"), p = f.on("dblclick.zoom");
  d.wheelDelta(fs);
  async function v(M, T) {
    return f ? new Promise((L) => {
      d?.interpolate(T?.interpolate === "linear" ? kt : nn).transform(Yn(f, T?.duration, T?.ease, () => L(!0)), M);
    }) : !1;
  }
  function x({ noWheelClassName: M, noPanClassName: T, onPaneContextMenu: L, userSelectionActive: b, panOnScroll: N, panOnDrag: E, panOnScrollMode: I, panOnScrollSpeed: j, preventScrolling: A, zoomOnPinch: O, zoomOnScroll: V, zoomOnDoubleClick: H, zoomActivationKeyPressed: Z, lib: W, onTransformChange: G, connectionInProgress: ee, paneClickDistance: U, selectionOnDrag: R }) {
    b && !c.isZoomingOrPanning && y();
    const q = N && !Z && !b;
    d.clickDistance(R ? 1 / 0 : !Ae(U) || U < 0 ? 0 : U);
    const te = q ? kf({
      zoomPanValues: c,
      noWheelClassName: M,
      d3Selection: f,
      d3Zoom: d,
      panOnScrollMode: I,
      panOnScrollSpeed: j,
      zoomOnPinch: O,
      onPanZoomStart: s,
      onPanZoom: i,
      onPanZoomEnd: a
    }) : If({
      noWheelClassName: M,
      preventScrolling: A,
      d3ZoomHandler: g
    });
    f.on("wheel.zoom", te, { passive: !1 });
    const J = Mf({
      zoomPanValues: c,
      onDraggingChange: l,
      onPanZoomStart: s
    });
    d.on("start", J);
    const $ = Af({
      zoomPanValues: c,
      panOnDrag: E,
      onPaneContextMenu: !!L,
      onPanZoom: i,
      onTransformChange: G
    });
    d.on("zoom", $);
    const F = Df({
      zoomPanValues: c,
      panOnDrag: E,
      panOnScroll: N,
      onPaneContextMenu: L,
      onPanZoomEnd: a,
      onDraggingChange: l
    });
    d.on("end", F);
    const Q = Pf({
      zoomActivationKeyPressed: Z,
      panOnDrag: E,
      zoomOnScroll: V,
      panOnScroll: N,
      zoomOnDoubleClick: H,
      zoomOnPinch: O,
      userSelectionActive: b,
      noPanClassName: T,
      noWheelClassName: M,
      lib: W,
      connectionInProgress: ee
    });
    d.filter(Q), H ? f.on("dblclick.zoom", p) : f.on("dblclick.zoom", null);
  }
  function y() {
    d.on("zoom", null);
  }
  async function _(M, T, L) {
    const b = Fn(M), N = d?.constrain()(b, T, L);
    return N && await v(N), N;
  }
  async function m(M, T) {
    const L = Fn(M);
    return await v(L, T), L;
  }
  function w(M) {
    if (f) {
      const T = Fn(M), L = f.property("__zoom");
      (L.k !== M.zoom || L.x !== M.x || L.y !== M.y) && d?.transform(f, T, null, { sync: !0 });
    }
  }
  function C() {
    const M = f ? Ri(f.node()) : { x: 0, y: 0, k: 1 };
    return { x: M.x, y: M.y, zoom: M.k };
  }
  async function S(M, T) {
    return f ? new Promise((L) => {
      d?.interpolate(T?.interpolate === "linear" ? kt : nn).scaleTo(Yn(f, T?.duration, T?.ease, () => L(!0)), M);
    }) : !1;
  }
  async function k(M, T) {
    return f ? new Promise((L) => {
      d?.interpolate(T?.interpolate === "linear" ? kt : nn).scaleBy(Yn(f, T?.duration, T?.ease, () => L(!0)), M);
    }) : !1;
  }
  function D(M) {
    d?.scaleExtent(M);
  }
  function P(M) {
    d?.translateExtent(M);
  }
  function B(M) {
    const T = !Ae(M) || M < 0 ? 0 : M;
    d?.clickDistance(T);
  }
  return {
    update: x,
    destroy: y,
    setViewport: m,
    setViewportConstrained: _,
    getViewport: C,
    scaleTo: S,
    scaleBy: k,
    setScaleExtent: D,
    setTranslateExtent: P,
    syncViewport: w,
    setClickDistance: B
  };
}
var yt;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(yt || (yt = {}));
function jf({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: i }) {
  const s = e - t, a = n - o, l = [s > 0 ? 1 : s < 0 ? -1 : 0, a > 0 ? 1 : a < 0 ? -1 : 0];
  return s && r && (l[0] = l[0] * -1), a && i && (l[1] = l[1] * -1), l;
}
function Cr(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: r
  };
}
function We(e, t) {
  return Math.max(0, t - e);
}
function qe(e, t) {
  return Math.max(0, e - t);
}
function Qt(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function kr(e, t) {
  return e ? !t : t;
}
function Tf(e, t, n, o, r, i, s, a) {
  let { affectsX: l, affectsY: c } = t;
  const { isHorizontal: u, isVertical: d } = t, f = u && d, { xSnapped: g, ySnapped: p } = n, { minWidth: v, maxWidth: x, minHeight: y, maxHeight: _ } = o, { x: m, y: w, width: C, height: S, aspectRatio: k } = e;
  let D = Math.floor(u ? g - e.pointerX : 0), P = Math.floor(d ? p - e.pointerY : 0);
  const B = C + (l ? -D : D), M = S + (c ? -P : P), T = -i[0] * C, L = -i[1] * S;
  let b = Qt(B, v, x), N = Qt(M, y, _);
  if (s) {
    let j = 0, A = 0;
    l && D < 0 ? j = We(m + D + T, s[0][0]) : !l && D > 0 && (j = qe(m + B + T, s[1][0])), c && P < 0 ? A = We(w + P + L, s[0][1]) : !c && P > 0 && (A = qe(w + M + L, s[1][1])), b = Math.max(b, j), N = Math.max(N, A);
  }
  if (a) {
    let j = 0, A = 0;
    l && D > 0 ? j = qe(m + D, a[0][0]) : !l && D < 0 && (j = We(m + B, a[1][0])), c && P > 0 ? A = qe(w + P, a[0][1]) : !c && P < 0 && (A = We(w + M, a[1][1])), b = Math.max(b, j), N = Math.max(N, A);
  }
  if (r) {
    if (u) {
      const j = Qt(B / k, y, _) * k;
      if (b = Math.max(b, j), s) {
        let A = 0;
        !l && !c || l && !c && f ? A = qe(w + L + B / k, s[1][1]) * k : A = We(w + L + (l ? D : -D) / k, s[0][1]) * k, b = Math.max(b, A);
      }
      if (a) {
        let A = 0;
        !l && !c || l && !c && f ? A = We(w + B / k, a[1][1]) * k : A = qe(w + (l ? D : -D) / k, a[0][1]) * k, b = Math.max(b, A);
      }
    }
    if (d) {
      const j = Qt(M * k, v, x) / k;
      if (N = Math.max(N, j), s) {
        let A = 0;
        !l && !c || c && !l && f ? A = qe(m + M * k + T, s[1][0]) / k : A = We(m + (c ? P : -P) * k + T, s[0][0]) / k, N = Math.max(N, A);
      }
      if (a) {
        let A = 0;
        !l && !c || c && !l && f ? A = We(m + M * k, a[1][0]) / k : A = qe(m + (c ? P : -P) * k, a[0][0]) / k, N = Math.max(N, A);
      }
    }
  }
  P = P + (P < 0 ? N : -N), D = D + (D < 0 ? b : -b), r && (f ? B > M * k ? P = (kr(l, c) ? -D : D) / k : D = (kr(l, c) ? -P : P) * k : u ? (P = D / k, c = l) : (D = P * k, l = c));
  const E = l ? m + D : m, I = c ? w + P : w;
  return {
    width: C + (l ? -D : D),
    height: S + (c ? -P : P),
    x: i[0] * D * (l ? -1 : 1) + E,
    y: i[1] * P * (c ? -1 : 1) + I
  };
}
const hs = { width: 0, height: 0, x: 0, y: 0 }, zf = {
  ...hs,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function Rf(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, i = e.measured.width ?? 0, s = e.measured.height ?? 0, a = n[0] * i, l = n[1] * s;
  return [
    [o - a, r - l],
    [o + i - a, r + s - l]
  ];
}
function Lf({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const i = be(e);
  let s = {
    controlDirection: Cr("bottom-right"),
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
    let y = { ...hs }, _ = { ...zf };
    s = {
      boundaries: u,
      resizeDirection: f,
      keepAspectRatio: d,
      controlDirection: Cr(c)
    };
    let m, w = null, C = [], S, k, D, P = !1;
    const B = _i().on("start", (M) => {
      const { nodeLookup: T, transform: L, snapGrid: b, snapToGrid: N, nodeOrigin: E, paneDomNode: I } = n();
      if (m = T.get(t), !m)
        return;
      w = I?.getBoundingClientRect() ?? null;
      const { xSnapped: j, ySnapped: A } = It(M.sourceEvent, {
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
        pointerY: A,
        aspectRatio: y.width / y.height
      }, S = void 0, k = rt(m.extent) ? m.extent : void 0, m.parentId && (m.extent === "parent" || m.expandParent) && (S = T.get(m.parentId)), S && m.extent === "parent" && (k = [
        [0, 0],
        [S.measured.width, S.measured.height]
      ]), C = [], D = void 0;
      for (const [O, V] of T)
        if (V.parentId === t && (C.push({
          id: O,
          position: { ...V.position },
          extent: V.extent
        }), V.extent === "parent" || V.expandParent)) {
          const H = Rf(V, m, V.origin ?? E);
          D ? D = [
            [Math.min(H[0][0], D[0][0]), Math.min(H[0][1], D[0][1])],
            [Math.max(H[1][0], D[1][0]), Math.max(H[1][1], D[1][1])]
          ] : D = H;
        }
      g?.(M, { ...y });
    }).on("drag", (M) => {
      const { transform: T, snapGrid: L, snapToGrid: b, nodeOrigin: N } = n(), E = It(M.sourceEvent, {
        transform: T,
        snapGrid: L,
        snapToGrid: b,
        containerBounds: w
      }), I = [];
      if (!m)
        return;
      const { x: j, y: A, width: O, height: V } = y, H = {}, Z = m.origin ?? N, { width: W, height: G, x: ee, y: U } = Tf(_, s.controlDirection, E, s.boundaries, s.keepAspectRatio, Z, k, D), R = W !== O, q = G !== V, te = ee !== j && R, J = U !== A && q;
      if (!te && !J && !R && !q)
        return;
      if ((te || J || Z[0] === 1 || Z[1] === 1) && (H.x = te ? ee : y.x, H.y = J ? U : y.y, y.x = H.x, y.y = H.y, C.length > 0)) {
        const ne = ee - j, de = U - A;
        for (const ye of C)
          ye.position = {
            x: ye.position.x - ne + Z[0] * (W - O),
            y: ye.position.y - de + Z[1] * (G - V)
          }, I.push(ye);
      }
      if ((R || q) && (H.width = R && (!s.resizeDirection || s.resizeDirection === "horizontal") ? W : y.width, H.height = q && (!s.resizeDirection || s.resizeDirection === "vertical") ? G : y.height, y.width = H.width, y.height = H.height), S && m.expandParent) {
        const ne = Z[0] * (H.width ?? 0);
        H.x && H.x < ne && (y.x = ne, _.x = _.x - (H.x - ne));
        const de = Z[1] * (H.height ?? 0);
        H.y && H.y < de && (y.y = de, _.y = _.y - (H.y - de));
      }
      const $ = jf({
        width: y.width,
        prevWidth: O,
        height: y.height,
        prevHeight: V,
        affectsX: s.controlDirection.affectsX,
        affectsY: s.controlDirection.affectsY
      }), F = { ...y, direction: $ };
      x?.(M, F) !== !1 && (P = !0, p?.(M, F), o(H, I));
    }).on("end", (M) => {
      P && (v?.(M, { ...y }), r?.({ ...y }), P = !1);
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
var Xn = { exports: {} }, Wn = {}, qn = { exports: {} }, Zn = {};
var Ir;
function Hf() {
  if (Ir) return Zn;
  Ir = 1;
  var e = Ht;
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
  return Zn.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : u, Zn;
}
var Mr;
function Vf() {
  return Mr || (Mr = 1, qn.exports = Hf()), qn.exports;
}
var Ar;
function Of() {
  if (Ar) return Wn;
  Ar = 1;
  var e = Ht, t = Vf();
  function n(c, u) {
    return c === u && (c !== 0 || 1 / c === 1 / u) || c !== c && u !== u;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, i = e.useRef, s = e.useEffect, a = e.useMemo, l = e.useDebugValue;
  return Wn.useSyncExternalStoreWithSelector = function(c, u, d, f, g) {
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
              var k = v.value;
              if (g(k, S))
                return w = k;
            }
            return w = S;
          }
          if (k = w, o(m, S)) return k;
          var D = f(S);
          return g !== void 0 && g(k, D) ? (m = S, k) : (m = S, w = D);
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
  }, Wn;
}
var Dr;
function Bf() {
  return Dr || (Dr = 1, Xn.exports = Of()), Xn.exports;
}
var Ff = Bf();
const Yf = /* @__PURE__ */ ic(Ff), Xf = {}, Pr = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (u, d) => {
    const f = typeof u == "function" ? u(t) : u;
    if (!Object.is(f, t)) {
      const g = t;
      t = d ?? (typeof f != "object" || f === null) ? f : Object.assign({}, t, f), n.forEach((p) => p(t, g));
    }
  }, r = () => t, l = { setState: o, getState: r, getInitialState: () => c, subscribe: (u) => (n.add(u), () => n.delete(u)), destroy: () => {
    (Xf ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, c = t = e(o, r, l);
  return l;
}, Wf = (e) => e ? Pr(e) : Pr, { useDebugValue: qf } = Ht, { useSyncExternalStoreWithSelector: Zf } = Yf, Uf = (e) => e;
function gs(e, t = Uf, n) {
  const o = Zf(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return qf(o), o;
}
const $r = (e, t) => {
  const n = Wf(e), o = (r, i = t) => gs(n, r, i);
  return Object.assign(o, n), o;
}, Gf = (e, t) => e ? $r(e, t) : $r;
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
var Un = { exports: {} }, xe = {};
var jr;
function Kf() {
  if (jr) return xe;
  jr = 1;
  var e = Ht;
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
  return xe.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, xe.createPortal = function(l, c) {
    var u = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!c || c.nodeType !== 1 && c.nodeType !== 9 && c.nodeType !== 11)
      throw Error(t(299));
    return i(l, c, null, u);
  }, xe.flushSync = function(l) {
    var c = s.T, u = o.p;
    try {
      if (s.T = null, o.p = 2, l) return l();
    } finally {
      s.T = c, o.p = u, o.d.f();
    }
  }, xe.preconnect = function(l, c) {
    typeof l == "string" && (c ? (c = c.crossOrigin, c = typeof c == "string" ? c === "use-credentials" ? c : "" : void 0) : c = null, o.d.C(l, c));
  }, xe.prefetchDNS = function(l) {
    typeof l == "string" && o.d.D(l);
  }, xe.preinit = function(l, c) {
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
  }, xe.preinitModule = function(l, c) {
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
  }, xe.preload = function(l, c) {
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
  }, xe.preloadModule = function(l, c) {
    if (typeof l == "string")
      if (c) {
        var u = a(c.as, c.crossOrigin);
        o.d.m(l, {
          as: typeof c.as == "string" && c.as !== "script" ? c.as : void 0,
          crossOrigin: u,
          integrity: typeof c.integrity == "string" ? c.integrity : void 0
        });
      } else o.d.m(l);
  }, xe.requestFormReset = function(l) {
    o.d.r(l);
  }, xe.unstable_batchedUpdates = function(l, c) {
    return l(c);
  }, xe.useFormState = function(l, c, u) {
    return s.H.useFormState(l, c, u);
  }, xe.useFormStatus = function() {
    return s.H.useHostTransitionStatus();
  }, xe.version = "19.2.7", xe;
}
var Tr;
function Qf() {
  if (Tr) return Un.exports;
  Tr = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), Un.exports = Kf(), Un.exports;
}
Qf();
const Mn = xo(null), Jf = Mn.Provider, ps = $e.error001("react");
function ae(e, t) {
  const n = Vt(Mn);
  if (n === null)
    throw new Error(ps);
  return gs(n, e, t);
}
function ue() {
  const e = Vt(Mn);
  if (e === null)
    throw new Error(ps);
  return me(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const zr = { display: "none" }, eh = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, ms = "react-flow__node-desc", ys = "react-flow__edge-desc", th = "react-flow__aria-live", nh = (e) => e.ariaLiveMessage, oh = (e) => e.ariaLabelConfig;
function rh({ rfId: e }) {
  const t = ae(nh);
  return h.jsx("div", { id: `${th}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: eh, children: t });
}
function ih({ rfId: e, disableKeyboardA11y: t }) {
  const n = ae(oh);
  return h.jsxs(h.Fragment, { children: [h.jsx("div", { id: `${ms}-${e}`, style: zr, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), h.jsx("div", { id: `${ys}-${e}`, style: zr, children: n["edge.a11yDescription.default"] }), !t && h.jsx(rh, { rfId: e })] });
}
const An = vn(({ position: e = "top-left", children: t, className: n, style: o, ...r }, i) => {
  const s = `${e}`.split("-");
  return h.jsx("div", { className: ge(["react-flow__panel", n, ...s]), style: o, ref: i, ...r, children: t });
});
An.displayName = "Panel";
function sh({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : h.jsx(An, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: h.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const ah = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, Jt = (e) => e.id;
function ch(e, t) {
  return le(e.selectedNodes.map(Jt), t.selectedNodes.map(Jt)) && le(e.selectedEdges.map(Jt), t.selectedEdges.map(Jt));
}
function lh({ onSelectionChange: e }) {
  const t = ue(), { selectedNodes: n, selectedEdges: o } = ae(ah, ch);
  return se(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((i) => i(r));
  }, [n, o, e]), null;
}
const uh = (e) => !!e.onSelectionChangeHandlers;
function dh({ onSelectionChange: e }) {
  const t = ae(uh);
  return e || t ? h.jsx(lh, { onSelectionChange: e }) : null;
}
const xs = [0, 0], fh = { x: 0, y: 0, zoom: 1 }, hh = [
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
], Rr = [...hh, "rfId"], gh = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), Lr = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: $t,
  nodeOrigin: xs,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function ph(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: i, setNodeExtent: s, reset: a, setDefaultNodesAndEdges: l } = ae(gh, le), c = ue();
  se(() => (l(e.defaultNodes, e.defaultEdges), () => {
    u.current = Lr, a();
  }), []);
  const u = ie(Lr);
  return se(
    () => {
      for (const d of Rr) {
        const f = e[d], g = u.current[d];
        f !== g && (typeof e[d] > "u" || (d === "nodes" ? t(f) : d === "edges" ? n(f) : d === "minZoom" ? o(f) : d === "maxZoom" ? r(f) : d === "translateExtent" ? i(f) : d === "nodeExtent" ? s(f) : d === "ariaLabelConfig" ? c.setState({ ariaLabelConfig: Gd(f) }) : d === "fitView" ? c.setState({ fitViewQueued: f }) : d === "fitViewOptions" ? c.setState({ fitViewOptions: f }) : c.setState({ [d]: f })));
      }
      u.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    Rr.map((d) => e[d])
  ), null;
}
function Hr() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function mh(e) {
  const [t, n] = re(e === "system" ? null : e);
  return se(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = Hr(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : Hr()?.matches ? "dark" : "light";
}
const Vr = typeof document < "u" ? document : null;
function Rt(e = null, t = { target: Vr, actInsideInputWithModifier: !0 }) {
  const [n, o] = re(!1), r = ie(!1), i = ie(/* @__PURE__ */ new Set([])), [s, a] = me(() => {
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
    const l = t?.target ?? Vr, c = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const u = (g) => {
        if (r.current = g.ctrlKey || g.metaKey || g.shiftKey || g.altKey, (!r.current || r.current && !c) && Ki(g))
          return !1;
        const v = Br(g.code, a);
        if (i.current.add(g[v]), Or(s, i.current, !1)) {
          const x = g.composedPath?.()?.[0] || g.target, y = x?.nodeName === "BUTTON" || x?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !y) && g.preventDefault(), o(!0);
        }
      }, d = (g) => {
        const p = Br(g.code, a);
        Or(s, i.current, !0) ? (o(!1), i.current.clear()) : i.current.delete(g[p]), g.key === "Meta" && i.current.clear(), r.current = !1;
      }, f = () => {
        i.current.clear(), o(!1);
      };
      return l?.addEventListener("keydown", u), l?.addEventListener("keyup", d), window.addEventListener("blur", f), window.addEventListener("contextmenu", f), () => {
        l?.removeEventListener("keydown", u), l?.removeEventListener("keyup", d), window.removeEventListener("blur", f), window.removeEventListener("contextmenu", f);
      };
    }
  }, [e, o]), n;
}
function Or(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function Br(e, t) {
  return t.includes(e) ? "code" : "key";
}
const yh = () => {
  const e = ue();
  return me(() => ({
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
      const { width: o, height: r, minZoom: i, maxZoom: s, panZoom: a } = e.getState(), l = Mo(t, o, r, i, s, n?.padding ?? 0.1);
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
      return wt(c, o, d, u);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: r, y: i } = o.getBoundingClientRect(), s = mt(t, n);
      return {
        x: s.x + r,
        y: s.y + i
      };
    }
  }), []);
};
function ws(e, t) {
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
      xh(l, a);
    n.push(a);
  }
  return r.length && r.forEach((i) => {
    i.index !== void 0 ? n.splice(i.index, 0, { ...i.item }) : n.push({ ...i.item });
  }), n;
}
function xh(e, t) {
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
function vs(e, t) {
  return ws(e, t);
}
function bs(e, t) {
  return ws(e, t);
}
function Ke(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function ct(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [r, i] of e) {
    const s = t.has(r);
    !(i.selected === void 0 && !s) && i.selected !== s && (n && (i.selected = s), o.push(Ke(i.id, s)));
  }
  return o;
}
function Fr({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((r) => [r.id, r]));
  for (const [r, i] of e.entries()) {
    const s = t.get(i.id), a = s?.internals?.userNode ?? s;
    a !== void 0 && a !== i && n.push({ id: i.id, item: i, type: "replace" }), a === void 0 && n.push({ item: i, type: "add", index: r });
  }
  for (const [r] of t)
    o.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function Yr(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const wh = qi();
function Ss(e, t, n = {}) {
  return nf(e, t, {
    ...n,
    onError: n.onError ?? wh
  });
}
const Xr = (e) => Od(e), vh = (e) => Fi(e);
function _s(e) {
  return vn(e);
}
const bh = typeof window < "u" ? rc : se;
function Wr(e) {
  const [t, n] = re(BigInt(0)), [o] = re(() => Sh(() => n((r) => r + BigInt(1))));
  return bh(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function Sh(e) {
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
const Es = xo(null);
function _h({ children: e }) {
  const t = ue(), n = pe((a) => {
    const { nodes: l = [], setNodes: c, hasDefaultNodes: u, onNodesChange: d, nodeLookup: f, fitViewQueued: g, onNodesChangeMiddlewareMap: p } = t.getState();
    let v = l;
    for (const y of a)
      v = typeof y == "function" ? y(v) : y;
    let x = Fr({
      items: v,
      lookup: f
    });
    for (const y of p.values())
      x = y(x);
    u && c(v), x.length > 0 ? d?.(x) : g && window.requestAnimationFrame(() => {
      const { fitViewQueued: y, nodes: _, setNodes: m } = t.getState();
      y && m(_);
    });
  }, []), o = Wr(n), r = pe((a) => {
    const { edges: l = [], setEdges: c, hasDefaultEdges: u, onEdgesChange: d, edgeLookup: f } = t.getState();
    let g = l;
    for (const p of a)
      g = typeof p == "function" ? p(g) : p;
    u ? c(g) : d && d(Fr({
      items: g,
      lookup: f
    }));
  }, []), i = Wr(r), s = me(() => ({ nodeQueue: o, edgeQueue: i }), []);
  return h.jsx(Es.Provider, { value: s, children: e });
}
function Eh() {
  const e = Vt(Es);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Nh = (e) => !!e.panZoom;
function zo() {
  const e = yh(), t = ue(), n = Eh(), o = ae(Nh), r = me(() => {
    const i = (d) => t.getState().nodeLookup.get(d), s = (d) => {
      n.nodeQueue.push(d);
    }, a = (d) => {
      n.edgeQueue.push(d);
    }, l = (d) => {
      const { nodeLookup: f, nodeOrigin: g } = t.getState(), p = Xr(d) ? d : f.get(d.id), v = p.parentId ? Ui(p.position, p.measured, p.parentId, f, g) : p.position, x = {
        ...p,
        position: v,
        width: p.measured?.width ?? p.width,
        height: p.measured?.height ?? p.height
      };
      return pt(x);
    }, c = (d, f, g = { replace: !1 }) => {
      s((p) => p.map((v) => {
        if (v.id === d) {
          const x = typeof f == "function" ? f(v) : f;
          return g.replace && Xr(x) ? x : { ...v, ...x };
        }
        return v;
      }));
    }, u = (d, f, g = { replace: !1 }) => {
      a((p) => p.map((v) => {
        if (v.id === d) {
          const x = typeof f == "function" ? f(v) : f;
          return g.replace && vh(x) ? x : { ...v, ...x };
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
        const { nodes: g, edges: p, onNodesDelete: v, onEdgesDelete: x, triggerNodeChanges: y, triggerEdgeChanges: _, onDelete: m, onBeforeDelete: w } = t.getState(), { nodes: C, edges: S } = await Wd({
          nodesToRemove: d,
          edgesToRemove: f,
          nodes: g,
          edges: p,
          onBeforeDelete: w
        }), k = S.length > 0, D = C.length > 0;
        if (k) {
          const P = S.map(Yr);
          x?.(S), _(P);
        }
        if (D) {
          const P = C.map(Yr);
          v?.(C), y(P);
        }
        return (D || k) && m?.({ nodes: C, edges: S }), { deletedNodes: C, deletedEdges: S };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (d, f = !0, g) => {
        const p = pr(d), v = p ? d : l(d), x = g !== void 0;
        return v ? (g || t.getState().nodes).filter((y) => {
          const _ = t.getState().nodeLookup.get(y.id);
          if (_ && !p && (y.id === d.id || !_.internals.positionAbsolute))
            return !1;
          const m = pt(x ? y : _), w = Tt(m, v);
          return f && w > 0 || w >= m.width * m.height || w >= v.width * v.height;
        }) : [];
      },
      isNodeIntersecting: (d, f, g = !0) => {
        const v = pr(d) ? d : l(d);
        if (!v)
          return !1;
        const x = Tt(v, f);
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
        return Bd(d, { nodeLookup: f, nodeOrigin: g });
      },
      getHandleConnections: ({ type: d, id: f, nodeId: g }) => Array.from(t.getState().connectionLookup.get(`${g}-${d}${f ? `-${f}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: d, handleId: f, nodeId: g }) => Array.from(t.getState().connectionLookup.get(`${g}${d ? f ? `-${d}-${f}` : `-${d}` : ""}`)?.values() ?? []),
      fitView: async (d) => {
        const f = t.getState().fitViewResolver ?? Ud();
        return t.setState({ fitViewQueued: !0, fitViewOptions: d, fitViewResolver: f }), n.nodeQueue.push((g) => [...g]), f.promise;
      }
    };
  }, []);
  return me(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const qr = (e) => e.selected, Ch = typeof window < "u" ? window : void 0;
function kh({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = ue(), { deleteElements: o } = zo(), r = Rt(e, { actInsideInputWithModifier: !1 }), i = Rt(t, { target: Ch });
  se(() => {
    if (r) {
      const { edges: s, nodes: a } = n.getState();
      o({ nodes: a.filter(qr), edges: s.filter(qr) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), se(() => {
    n.setState({ multiSelectionActive: i });
  }, [i]);
}
function Ih(e) {
  const t = ue();
  se(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = Ao(e.current);
      (o.height === 0 || o.width === 0) && t.getState().onError?.("004", $e.error004()), t.setState({ width: o.width || 500, height: o.height || 500 });
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
const Dn = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, Mh = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function Ah({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: i = et.Free, zoomOnDoubleClick: s = !0, panOnDrag: a = !0, defaultViewport: l, translateExtent: c, minZoom: u, maxZoom: d, zoomActivationKeyCode: f, preventScrolling: g = !0, children: p, noWheelClassName: v, noPanClassName: x, onViewportChange: y, isControlledViewport: _, paneClickDistance: m, selectionOnDrag: w }) {
  const C = ue(), S = ie(null), { userSelectionActive: k, lib: D, connectionInProgress: P } = ae(Mh, le), B = Rt(f), M = ie();
  Ih(S);
  const T = pe((L) => {
    y?.({ x: L[0], y: L[1], zoom: L[2] }), _ || C.setState({ transform: L });
  }, [y, _]);
  return se(() => {
    if (S.current) {
      M.current = $f({
        domNode: S.current,
        minZoom: u,
        maxZoom: d,
        translateExtent: c,
        viewport: l,
        onDraggingChange: (E) => C.setState((I) => I.paneDragging === E ? I : { paneDragging: E }),
        onPanZoomStart: (E, I) => {
          const { onViewportChangeStart: j, onMoveStart: A } = C.getState();
          A?.(E, I), j?.(I);
        },
        onPanZoom: (E, I) => {
          const { onViewportChange: j, onMove: A } = C.getState();
          A?.(E, I), j?.(I);
        },
        onPanZoomEnd: (E, I) => {
          const { onViewportChangeEnd: j, onMoveEnd: A } = C.getState();
          A?.(E, I), j?.(I);
        }
      });
      const { x: L, y: b, zoom: N } = M.current.getViewport();
      return C.setState({
        panZoom: M.current,
        transform: [L, b, N],
        domNode: S.current.closest(".react-flow")
      }), () => {
        M.current?.destroy();
      };
    }
  }, []), se(() => {
    M.current?.update({
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
      userSelectionActive: k,
      noWheelClassName: v,
      lib: D,
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
    k,
    v,
    D,
    T,
    P,
    w,
    m
  ]), h.jsx("div", { className: "react-flow__renderer", ref: S, style: Dn, children: p });
}
const Dh = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function Ph() {
  const { userSelectionActive: e, userSelectionRect: t } = ae(Dh, le);
  return e && t ? h.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const Gn = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, $h = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function jh({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = jt.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: i, selectionOnDrag: s, onSelectionStart: a, onSelectionEnd: l, onPaneClick: c, onPaneContextMenu: u, onPaneScroll: d, onPaneMouseEnter: f, onPaneMouseMove: g, onPaneMouseLeave: p, children: v }) {
  const x = ie(0), y = ue(), { userSelectionActive: _, elementsSelectable: m, dragging: w, connectionInProgress: C, panBy: S, autoPanSpeed: k } = ae($h, le), D = m && (e || _), P = ie(null), B = ie(), M = ie(/* @__PURE__ */ new Set()), T = ie(/* @__PURE__ */ new Set()), L = ie(!1), b = ie({ x: 0, y: 0 }), N = ie(!1), E = (R) => {
    if (L.current || C) {
      L.current = !1;
      return;
    }
    c?.(R), y.getState().resetSelectedElements(), y.setState({ nodesSelectionActive: !1 });
  }, I = (R) => {
    if (Array.isArray(o) && o?.includes(2)) {
      R.preventDefault();
      return;
    }
    u?.(R);
  }, j = d ? (R) => d(R) : void 0, A = (R) => {
    L.current && (R.stopPropagation(), L.current = !1);
  }, O = (R) => {
    const { domNode: q, transform: te } = y.getState();
    if (B.current = q?.getBoundingClientRect(), !B.current)
      return;
    const J = R.target === P.current;
    if (!J && !!R.target.closest(".nokey") || !e || !(s && J || t) || R.button !== 0 || !R.isPrimary)
      return;
    R.target?.setPointerCapture?.(R.pointerId), L.current = !1;
    const { x: Q, y: ne } = De(R.nativeEvent, B.current), de = wt({ x: Q, y: ne }, te);
    y.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: de.x,
        startY: de.y,
        x: Q,
        y: ne
      }
    }), J || (R.stopPropagation(), R.preventDefault());
  };
  function V(R, q) {
    const { userSelectionRect: te } = y.getState();
    if (!te)
      return;
    const { transform: J, nodeLookup: $, edgeLookup: F, connectionLookup: Q, triggerNodeChanges: ne, triggerEdgeChanges: de, defaultEdgeOptions: ye } = y.getState(), Ne = { x: te.startX, y: te.startY }, { x: _e, y: Ee } = mt(Ne, J), Ce = {
      startX: Ne.x,
      startY: Ne.y,
      x: R < _e ? R : _e,
      y: q < Ee ? q : Ee,
      width: Math.abs(R - _e),
      height: Math.abs(q - Ee)
    }, Ge = M.current, He = T.current;
    M.current = new Set(ko($, Ce, J, n === jt.Partial, !0).map((z) => z.id)), T.current = /* @__PURE__ */ new Set();
    const Ve = ye?.selectable ?? !0;
    for (const z of M.current) {
      const Y = Q.get(z);
      if (Y)
        for (const { edgeId: X } of Y.values()) {
          const oe = F.get(X);
          oe && (oe.selectable ?? Ve) && T.current.add(X);
        }
    }
    if (!mr(Ge, M.current)) {
      const z = ct($, M.current, !0);
      ne(z);
    }
    if (!mr(He, T.current)) {
      const z = ct(F, T.current);
      de(z);
    }
    y.setState({
      userSelectionRect: Ce,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function H() {
    if (!r || !B.current)
      return;
    const [R, q] = Io(b.current, B.current, k);
    S({ x: R, y: q }).then((te) => {
      if (!L.current || !te) {
        x.current = requestAnimationFrame(H);
        return;
      }
      const { x: J, y: $ } = b.current;
      V(J, $), x.current = requestAnimationFrame(H);
    });
  }
  const Z = () => {
    cancelAnimationFrame(x.current), x.current = 0, N.current = !1;
  };
  se(() => () => Z(), []);
  const W = (R) => {
    const { userSelectionRect: q, transform: te, resetSelectedElements: J } = y.getState();
    if (!B.current || !q)
      return;
    const { x: $, y: F } = De(R.nativeEvent, B.current);
    b.current = { x: $, y: F };
    const Q = mt({ x: q.startX, y: q.startY }, te);
    if (!L.current) {
      const ne = t ? 0 : i;
      if (Math.hypot($ - Q.x, F - Q.y) <= ne)
        return;
      J(), a?.(R);
    }
    L.current = !0, N.current || (H(), N.current = !0), V($, F);
  }, G = (R) => {
    R.button === 0 && (R.target?.releasePointerCapture?.(R.pointerId), !_ && R.target === P.current && y.getState().userSelectionRect && E?.(R), y.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), L.current && (l?.(R), y.setState({
      nodesSelectionActive: M.current.size > 0
    })), Z());
  }, ee = (R) => {
    R.target?.releasePointerCapture?.(R.pointerId), Z();
  }, U = o === !0 || Array.isArray(o) && o.includes(0);
  return h.jsxs("div", { className: ge(["react-flow__pane", { draggable: U, dragging: w, selection: e }]), onClick: D ? void 0 : Gn(E, P), onContextMenu: Gn(I, P), onWheel: Gn(j, P), onPointerEnter: D ? void 0 : f, onPointerMove: D ? W : g, onPointerUp: D ? G : void 0, onPointerCancel: D ? ee : void 0, onPointerDownCapture: D ? O : void 0, onClickCapture: D ? A : void 0, onPointerLeave: p, ref: P, style: Dn, children: [v, h.jsx(Ph, {})] });
}
function go({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: i, multiSelectionActive: s, nodeLookup: a, onError: l } = t.getState(), c = a.get(e);
  if (!c) {
    l?.("012", $e.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), c.selected ? (n || c.selected && s) && (i({ nodes: [c], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function Ns({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: i, nodeClickDistance: s }) {
  const a = ue(), [l, c] = re(!1), u = ie();
  return se(() => {
    u.current = wf({
      getStoreItems: () => a.getState(),
      onNodeMouseDown: (d) => {
        go({
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
const Th = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function Cs() {
  const e = ue();
  return pe((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: i, nodesDraggable: s, onError: a, updateNodePositions: l, nodeLookup: c, nodeOrigin: u } = e.getState(), d = /* @__PURE__ */ new Map(), f = Th(s), g = r ? i[0] : 5, p = r ? i[1] : 5, v = n.direction.x * g * n.factor, x = n.direction.y * p * n.factor;
    for (const [, y] of c) {
      if (!f(y))
        continue;
      let _ = {
        x: y.internals.positionAbsolute.x + v,
        y: y.internals.positionAbsolute.y + x
      };
      r && (_ = Xt(_, i));
      const { position: m, positionAbsolute: w } = Yi({
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
const Ro = xo(null), zh = Ro.Provider;
Ro.Consumer;
const ks = () => Vt(Ro), Rh = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), Lh = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: i, connection: s } = o, { fromHandle: a, toHandle: l, isValid: c } = s, u = l?.nodeId === e && l?.id === t && l?.type === n;
  return {
    connectingFrom: a?.nodeId === e && a?.id === t && a?.type === n,
    connectingTo: u,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: i === ht.Strict ? a?.type !== n : e !== a?.nodeId || t !== a?.id,
    connectionInProcess: !!a,
    clickConnectionInProcess: !!r,
    valid: u && c
  };
};
function Hh({ type: e = "source", position: t = K.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: i = !0, id: s, onConnect: a, children: l, className: c, onMouseDown: u, onTouchStart: d, ...f }, g) {
  const p = s || null, v = e === "target", x = ue(), y = ks(), { connectOnClick: _, noPanClassName: m, rfId: w } = ae(Rh, le), { connectingFrom: C, connectingTo: S, clickConnecting: k, isPossibleEndHandle: D, connectionInProcess: P, clickConnectionInProcess: B, valid: M } = ae(Lh(y, p, e), le);
  y || x.getState().onError?.("010", $e.error010());
  const T = (N) => {
    const { defaultEdgeOptions: E, onConnect: I, hasDefaultEdges: j } = x.getState(), A = {
      ...E,
      ...N
    };
    if (j) {
      const { edges: O, setEdges: V, onError: H } = x.getState();
      V(Ss(A, O, { onError: H }));
    }
    I?.(A), a?.(A);
  }, L = (N) => {
    if (!y)
      return;
    const E = Qi(N.nativeEvent);
    if (r && (E && N.button === 0 || !E)) {
      const I = x.getState();
      ho.onPointerDown(N.nativeEvent, {
        handleDomNode: N.currentTarget,
        autoPanOnConnect: I.autoPanOnConnect,
        connectionMode: I.connectionMode,
        connectionRadius: I.connectionRadius,
        domNode: I.domNode,
        nodeLookup: I.nodeLookup,
        lib: I.lib,
        isTarget: v,
        handleId: p,
        nodeId: y,
        flowId: I.rfId,
        panBy: I.panBy,
        cancelConnection: I.cancelConnection,
        onConnectStart: I.onConnectStart,
        onConnectEnd: (...j) => x.getState().onConnectEnd?.(...j),
        updateConnection: I.updateConnection,
        onConnect: T,
        isValidConnection: n || ((...j) => x.getState().isValidConnection?.(...j) ?? !0),
        getTransform: () => x.getState().transform,
        getFromHandle: () => x.getState().connection.fromHandle,
        autoPanSpeed: I.autoPanSpeed,
        dragThreshold: I.connectionDragThreshold
      });
    }
    E ? u?.(N) : d?.(N);
  }, b = (N) => {
    const { onClickConnectStart: E, onClickConnectEnd: I, connectionClickStartHandle: j, connectionMode: A, isValidConnection: O, lib: V, rfId: H, nodeLookup: Z, connection: W } = x.getState();
    if (!y || !j && !r)
      return;
    if (!j) {
      E?.(N.nativeEvent, { nodeId: y, handleId: p, handleType: e }), x.setState({ connectionClickStartHandle: { nodeId: y, type: e, id: p } });
      return;
    }
    const G = Gi(N.target), ee = n || O, { connection: U, isValid: R } = ho.isValid(N.nativeEvent, {
      handle: {
        nodeId: y,
        id: p,
        type: e
      },
      connectionMode: A,
      fromNodeId: j.nodeId,
      fromHandleId: j.id || null,
      fromType: j.type,
      isValidConnection: ee,
      flowId: H,
      doc: G,
      lib: V,
      nodeLookup: Z
    });
    R && U && T(U);
    const q = structuredClone(W);
    delete q.inProgress, q.toPosition = q.toHandle ? q.toHandle.position : null, I?.(N, q), x.setState({ connectionClickStartHandle: null });
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
      clickconnecting: k,
      connectingfrom: C,
      connectingto: S,
      valid: M,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!P || D) && (P || B ? i : r)
    }
  ]), onMouseDown: L, onTouchStart: L, onClick: _ ? b : void 0, ref: g, ...f, children: l });
}
const xt = he(_s(Hh));
function Vh({ data: e, isConnectable: t, sourcePosition: n = K.Bottom }) {
  return h.jsxs(h.Fragment, { children: [e?.label, h.jsx(xt, { type: "source", position: n, isConnectable: t })] });
}
function Oh({ data: e, isConnectable: t, targetPosition: n = K.Top, sourcePosition: o = K.Bottom }) {
  return h.jsxs(h.Fragment, { children: [h.jsx(xt, { type: "target", position: n, isConnectable: t }), e?.label, h.jsx(xt, { type: "source", position: o, isConnectable: t })] });
}
function Bh() {
  return null;
}
function Fh({ data: e, isConnectable: t, targetPosition: n = K.Top }) {
  return h.jsxs(h.Fragment, { children: [h.jsx(xt, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const yn = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, Zr = {
  input: Vh,
  default: Oh,
  output: Fh,
  group: Bh
};
function Yh(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const Xh = (e) => {
  const { width: t, height: n, x: o, y: r } = Yt(e.nodeLookup, {
    filter: (i) => !!i.selected
  });
  return {
    width: Ae(t) ? t : null,
    height: Ae(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function Wh({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = ue(), { width: r, height: i, transformString: s, userSelectionActive: a } = ae(Xh, le), l = Cs(), c = ie(null);
  se(() => {
    n || c.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const u = !a && r !== null && i !== null;
  if (Ns({
    nodeRef: c,
    disabled: !u
  }), !u)
    return null;
  const d = e ? (g) => {
    const p = o.getState().nodes.filter((v) => v.selected);
    e(g, p);
  } : void 0, f = (g) => {
    Object.prototype.hasOwnProperty.call(yn, g.key) && (g.preventDefault(), l({
      direction: yn[g.key],
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
const Ur = typeof window < "u" ? window : void 0, qh = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function Is({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, paneClickDistance: a, deleteKeyCode: l, selectionKeyCode: c, selectionOnDrag: u, selectionMode: d, onSelectionStart: f, onSelectionEnd: g, multiSelectionKeyCode: p, panActivationKeyCode: v, zoomActivationKeyCode: x, elementsSelectable: y, zoomOnScroll: _, zoomOnPinch: m, panOnScroll: w, panOnScrollSpeed: C, panOnScrollMode: S, zoomOnDoubleClick: k, panOnDrag: D, autoPanOnSelection: P, defaultViewport: B, translateExtent: M, minZoom: T, maxZoom: L, preventScrolling: b, onSelectionContextMenu: N, noWheelClassName: E, noPanClassName: I, disableKeyboardA11y: j, onViewportChange: A, isControlledViewport: O }) {
  const { nodesSelectionActive: V, userSelectionActive: H } = ae(qh, le), Z = Rt(c, { target: Ur }), W = Rt(v, { target: Ur }), G = W || D, ee = W || w, U = u && G !== !0, R = Z || H || U;
  return kh({ deleteKeyCode: l, multiSelectionKeyCode: p }), h.jsx(Ah, { onPaneContextMenu: i, elementsSelectable: y, zoomOnScroll: _, zoomOnPinch: m, panOnScroll: ee, panOnScrollSpeed: C, panOnScrollMode: S, zoomOnDoubleClick: k, panOnDrag: !Z && G, defaultViewport: B, translateExtent: M, minZoom: T, maxZoom: L, zoomActivationKeyCode: x, preventScrolling: b, noWheelClassName: E, noPanClassName: I, onViewportChange: A, isControlledViewport: O, paneClickDistance: a, selectionOnDrag: U, children: h.jsxs(jh, { onSelectionStart: f, onSelectionEnd: g, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, panOnDrag: G, autoPanOnSelection: P, isSelecting: !!R, selectionMode: d, selectionKeyPressed: Z, paneClickDistance: a, selectionOnDrag: U, children: [e, V && h.jsx(Wh, { onSelectionContextMenu: N, noPanClassName: I, disableKeyboardA11y: j })] }) });
}
Is.displayName = "FlowRenderer";
const Zh = he(Is), Uh = (e) => (t) => e ? ko(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function Gh(e) {
  return ae(pe(Uh(e), [e]), le);
}
const Kh = (e) => e.updateNodeInternals;
function Qh() {
  const e = ae(Kh), [t] = re(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
function Jh({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
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
function eg({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: i, onDoubleClick: s, nodesDraggable: a, elementsSelectable: l, nodesConnectable: c, nodesFocusable: u, resizeObserver: d, noDragClassName: f, noPanClassName: g, disableKeyboardA11y: p, rfId: v, nodeTypes: x, nodeClickDistance: y, onError: _ }) {
  const { node: m, internals: w, isParent: C } = ae((R) => {
    const q = R.nodeLookup.get(e), te = R.parentLookup.has(e);
    return {
      node: q,
      internals: q.internals,
      isParent: te
    };
  }, le);
  let S = m.type || "default", k = x?.[S] || Zr[S];
  k === void 0 && (_?.("003", $e.error003(S)), S = "default", k = x?.default || Zr.default);
  const D = !!(m.draggable || a && typeof m.draggable > "u"), P = !!(m.selectable || l && typeof m.selectable > "u"), B = !!(m.connectable || c && typeof m.connectable > "u"), M = !!(m.focusable || u && typeof m.focusable > "u"), T = ue(), L = Zi(m), b = Jh({ node: m, nodeType: S, hasDimensions: L, resizeObserver: d }), N = Ns({
    nodeRef: b,
    disabled: m.hidden || !D,
    noDragClassName: f,
    handleSelector: m.dragHandle,
    nodeId: e,
    isSelectable: P,
    nodeClickDistance: y
  }), E = Cs();
  if (m.hidden)
    return null;
  const I = Xe(m), j = Yh(m), A = P || D || t || n || o || r, O = n ? (R) => n(R, { ...w.userNode }) : void 0, V = o ? (R) => o(R, { ...w.userNode }) : void 0, H = r ? (R) => r(R, { ...w.userNode }) : void 0, Z = i ? (R) => i(R, { ...w.userNode }) : void 0, W = s ? (R) => s(R, { ...w.userNode }) : void 0, G = (R) => {
    const { selectNodesOnDrag: q, nodeDragThreshold: te } = T.getState();
    P && (!q || !D || te > 0) && go({
      id: e,
      store: T,
      nodeRef: b
    }), t && t(R, { ...w.userNode });
  }, ee = (R) => {
    if (!(Ki(R.nativeEvent) || p)) {
      if (Hi.includes(R.key) && P) {
        const q = R.key === "Escape";
        go({
          id: e,
          store: T,
          unselect: q,
          nodeRef: b
        });
      } else if (D && m.selected && Object.prototype.hasOwnProperty.call(yn, R.key)) {
        R.preventDefault();
        const { ariaLabelConfig: q } = T.getState();
        T.setState({
          ariaLiveMessage: q["node.a11yDescription.ariaLiveMessage"]({
            direction: R.key.replace("Arrow", "").toLowerCase(),
            x: ~~w.positionAbsolute.x,
            y: ~~w.positionAbsolute.y
          })
        }), E({
          direction: yn[R.key],
          factor: R.shiftKey ? 4 : 1
        });
      }
    }
  }, U = () => {
    if (p || !b.current?.matches(":focus-visible"))
      return;
    const { transform: R, width: q, height: te, autoPanOnNodeFocus: J, setCenter: $ } = T.getState();
    if (!J)
      return;
    ko(/* @__PURE__ */ new Map([[e, m]]), { x: 0, y: 0, width: q, height: te }, R, !0).length > 0 || $(m.position.x + I.width / 2, m.position.y + I.height / 2, {
      zoom: R[2]
    });
  };
  return h.jsx("div", { className: ge([
    "react-flow__node",
    `react-flow__node-${S}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [g]: D
    },
    m.className,
    {
      selected: m.selected,
      selectable: P,
      parent: C,
      draggable: D,
      dragging: N
    }
  ]), ref: b, style: {
    zIndex: w.z,
    transform: `translate(${w.positionAbsolute.x}px,${w.positionAbsolute.y}px)`,
    pointerEvents: A ? "all" : "none",
    visibility: L ? "visible" : "hidden",
    ...m.style,
    ...j
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: O, onMouseMove: V, onMouseLeave: H, onContextMenu: Z, onClick: G, onDoubleClick: W, onKeyDown: M ? ee : void 0, tabIndex: M ? 0 : void 0, onFocus: M ? U : void 0, role: m.ariaRole ?? (M ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": p ? void 0 : `${ms}-${v}`, "aria-label": m.ariaLabel, ...m.domAttributes, children: h.jsx(zh, { value: e, children: h.jsx(k, { id: e, data: m.data, type: S, positionAbsoluteX: w.positionAbsolute.x, positionAbsoluteY: w.positionAbsolute.y, selected: m.selected ?? !1, selectable: P, draggable: D, deletable: m.deletable ?? !0, isConnectable: B, sourcePosition: m.sourcePosition, targetPosition: m.targetPosition, dragging: N, dragHandle: m.dragHandle, zIndex: w.z, parentId: m.parentId, ...I }) }) });
}
var tg = he(eg);
const ng = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function Ms(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: i } = ae(ng, le), s = Gh(e.onlyRenderVisibleElements), a = Qh();
  return h.jsx("div", { className: "react-flow__nodes", style: Dn, children: s.map((l) => (
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
    h.jsx(tg, { id: l, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: a, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: i }, l)
  )) });
}
Ms.displayName = "NodeRenderer";
const og = he(Ms);
function rg(e) {
  return ae(pe((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const i = n.nodeLookup.get(r.source), s = n.nodeLookup.get(r.target);
        i && s && Jd({
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
const ig = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return h.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, sg = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return h.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Gr = {
  [pn.Arrow]: ig,
  [pn.ArrowClosed]: sg
};
function ag(e) {
  const t = ue();
  return me(() => Object.prototype.hasOwnProperty.call(Gr, e) ? Gr[e] : (t.getState().onError?.("009", $e.error009(e)), null), [e]);
}
const cg = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: i = "strokeWidth", strokeWidth: s, orient: a = "auto-start-reverse" }) => {
  const l = ag(t);
  return l ? h.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: i, orient: a, refX: "0", refY: "0", children: h.jsx(l, { color: n, strokeWidth: s }) }) : null;
}, As = ({ defaultColor: e, rfId: t }) => {
  const n = ae((i) => i.edges), o = ae((i) => i.defaultEdgeOptions), r = me(() => cf(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? h.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: h.jsx("defs", { children: r.map((i) => h.jsx(cg, { id: i.id, type: i.type, color: i.color, width: i.width, height: i.height, markerUnits: i.markerUnits, strokeWidth: i.strokeWidth, orient: i.orient }, i.id)) }) }) : null;
};
As.displayName = "MarkerDefinitions";
var lg = he(As);
function Ds({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: i, labelBgPadding: s = [2, 4], labelBgBorderRadius: a = 2, children: l, className: c, ...u }) {
  const [d, f] = re({ x: 1, y: 0, width: 0, height: 0 }), g = ge(["react-flow__edge-textwrapper", c]), p = ie(null);
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
Ds.displayName = "EdgeText";
const ug = he(Ds);
function Pn({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: a, labelBgBorderRadius: l, interactionWidth: c = 20, ...u }) {
  return h.jsxs(h.Fragment, { children: [h.jsx("path", { ...u, d: e, fill: "none", className: ge(["react-flow__edge-path", u.className]) }), c ? h.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: c, className: "react-flow__edge-interaction" }) : null, o && Ae(t) && Ae(n) ? h.jsx(ug, { x: t, y: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: a, labelBgBorderRadius: l }) : null] });
}
function Kr({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === K.Left || e === K.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function Ps({ sourceX: e, sourceY: t, sourcePosition: n = K.Bottom, targetX: o, targetY: r, targetPosition: i = K.Top }) {
  const [s, a] = Kr({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [l, c] = Kr({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [u, d, f, g] = Ji({
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
function $s(e) {
  return he(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: a, label: l, labelStyle: c, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: g, style: p, markerEnd: v, markerStart: x, interactionWidth: y }) => {
    const [_, m, w] = Ps({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: a
    }), C = e.isInternal ? void 0 : t;
    return h.jsx(Pn, { id: C, path: _, labelX: m, labelY: w, label: l, labelStyle: c, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: g, style: p, markerEnd: v, markerStart: x, interactionWidth: y });
  });
}
const dg = $s({ isInternal: !1 }), js = $s({ isInternal: !0 });
dg.displayName = "SimpleBezierEdge";
js.displayName = "SimpleBezierEdgeInternal";
function Ts(e) {
  return he(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: u, labelBgBorderRadius: d, style: f, sourcePosition: g = K.Bottom, targetPosition: p = K.Top, markerEnd: v, markerStart: x, pathOptions: y, interactionWidth: _ }) => {
    const [m, w, C] = lo({
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
    return h.jsx(Pn, { id: S, path: m, labelX: w, labelY: C, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: u, labelBgBorderRadius: d, style: f, markerEnd: v, markerStart: x, interactionWidth: _ });
  });
}
const zs = Ts({ isInternal: !1 }), Rs = Ts({ isInternal: !0 });
zs.displayName = "SmoothStepEdge";
Rs.displayName = "SmoothStepEdgeInternal";
function Ls(e) {
  return he(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return h.jsx(zs, { ...n, id: o, pathOptions: me(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const fg = Ls({ isInternal: !1 }), Hs = Ls({ isInternal: !0 });
fg.displayName = "StepEdge";
Hs.displayName = "StepEdgeInternal";
function Vs(e) {
  return he(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: u, labelBgBorderRadius: d, style: f, markerEnd: g, markerStart: p, interactionWidth: v }) => {
    const [x, y, _] = ns({ sourceX: n, sourceY: o, targetX: r, targetY: i }), m = e.isInternal ? void 0 : t;
    return h.jsx(Pn, { id: m, path: x, labelX: y, labelY: _, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: u, labelBgBorderRadius: d, style: f, markerEnd: g, markerStart: p, interactionWidth: v });
  });
}
const hg = Vs({ isInternal: !1 }), Os = Vs({ isInternal: !0 });
hg.displayName = "StraightEdge";
Os.displayName = "StraightEdgeInternal";
function Bs(e) {
  return he(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s = K.Bottom, targetPosition: a = K.Top, label: l, labelStyle: c, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: g, style: p, markerEnd: v, markerStart: x, pathOptions: y, interactionWidth: _ }) => {
    const [m, w, C] = es({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: a,
      curvature: y?.curvature
    }), S = e.isInternal ? void 0 : t;
    return h.jsx(Pn, { id: S, path: m, labelX: w, labelY: C, label: l, labelStyle: c, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: g, style: p, markerEnd: v, markerStart: x, interactionWidth: _ });
  });
}
const gg = Bs({ isInternal: !1 }), Fs = Bs({ isInternal: !0 });
gg.displayName = "BezierEdge";
Fs.displayName = "BezierEdgeInternal";
const Qr = {
  default: Fs,
  straight: Os,
  step: Hs,
  smoothstep: Rs,
  simplebezier: js
}, Jr = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, pg = (e, t, n) => n === K.Left ? e - t : n === K.Right ? e + t : e, mg = (e, t, n) => n === K.Top ? e - t : n === K.Bottom ? e + t : e, ei = "react-flow__edgeupdater";
function ti({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: i, onMouseOut: s, type: a }) {
  return h.jsx("circle", { onMouseDown: r, onMouseEnter: i, onMouseOut: s, className: ge([ei, `${ei}-${a}`]), cx: pg(t, o, e), cy: mg(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function yg({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: i, targetY: s, sourcePosition: a, targetPosition: l, onReconnect: c, onReconnectStart: u, onReconnectEnd: d, setReconnecting: f, setUpdateHover: g }) {
  const p = ue(), v = (w, C) => {
    if (w.button !== 0)
      return;
    const { autoPanOnConnect: S, domNode: k, connectionMode: D, connectionRadius: P, lib: B, onConnectStart: M, cancelConnection: T, nodeLookup: L, rfId: b, panBy: N, updateConnection: E } = p.getState(), I = C.type === "target", j = (V, H) => {
      f(!1), d?.(V, n, C.type, H);
    }, A = (V) => c?.(n, V), O = (V, H) => {
      f(!0), u?.(w, n, C.type), M?.(V, H);
    };
    ho.onPointerDown(w.nativeEvent, {
      autoPanOnConnect: S,
      connectionMode: D,
      connectionRadius: P,
      domNode: k,
      handleId: C.id,
      nodeId: C.nodeId,
      nodeLookup: L,
      isTarget: I,
      edgeUpdaterType: C.type,
      lib: B,
      flowId: b,
      cancelConnection: T,
      panBy: N,
      isValidConnection: (...V) => p.getState().isValidConnection?.(...V) ?? !0,
      onConnect: A,
      onConnectStart: O,
      onConnectEnd: (...V) => p.getState().onConnectEnd?.(...V),
      onReconnectEnd: j,
      updateConnection: E,
      getTransform: () => p.getState().transform,
      getFromHandle: () => p.getState().connection.fromHandle,
      dragThreshold: p.getState().connectionDragThreshold,
      handleDomNode: w.currentTarget
    });
  }, x = (w) => v(w, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), y = (w) => v(w, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), _ = () => g(!0), m = () => g(!1);
  return h.jsxs(h.Fragment, { children: [(e === !0 || e === "source") && h.jsx(ti, { position: a, centerX: o, centerY: r, radius: t, onMouseDown: x, onMouseEnter: _, onMouseOut: m, type: "source" }), (e === !0 || e === "target") && h.jsx(ti, { position: l, centerX: i, centerY: s, radius: t, onMouseDown: y, onMouseEnter: _, onMouseOut: m, type: "target" })] });
}
function xg({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: i, onContextMenu: s, onMouseEnter: a, onMouseMove: l, onMouseLeave: c, reconnectRadius: u, onReconnect: d, onReconnectStart: f, onReconnectEnd: g, rfId: p, edgeTypes: v, noPanClassName: x, onError: y, disableKeyboardA11y: _ }) {
  let m = ae(($) => $.edgeLookup.get(e));
  const w = ae(($) => $.defaultEdgeOptions);
  m = w ? { ...w, ...m } : m;
  let C = m.type || "default", S = v?.[C] || Qr[C];
  S === void 0 && (y?.("011", $e.error011(C)), C = "default", S = v?.default || Qr.default);
  const k = !!(m.focusable || t && typeof m.focusable > "u"), D = typeof d < "u" && (m.reconnectable || n && typeof m.reconnectable > "u"), P = !!(m.selectable || o && typeof m.selectable > "u"), B = ie(null), [M, T] = re(!1), [L, b] = re(!1), N = ue(), { zIndex: E, sourceX: I, sourceY: j, targetX: A, targetY: O, sourcePosition: V, targetPosition: H } = ae(pe(($) => {
    const F = $.nodeLookup.get(m.source), Q = $.nodeLookup.get(m.target);
    if (!F || !Q)
      return {
        zIndex: m.zIndex,
        ...Jr
      };
    const ne = af({
      id: e,
      sourceNode: F,
      targetNode: Q,
      sourceHandle: m.sourceHandle || null,
      targetHandle: m.targetHandle || null,
      connectionMode: $.connectionMode,
      onError: y
    });
    return {
      zIndex: Qd({
        selected: m.selected,
        zIndex: m.zIndex,
        sourceNode: F,
        targetNode: Q,
        elevateOnSelect: $.elevateEdgesOnSelect,
        zIndexMode: $.zIndexMode
      }),
      ...ne || Jr
    };
  }, [m.source, m.target, m.sourceHandle, m.targetHandle, m.selected, m.zIndex]), le), Z = me(() => m.markerStart ? `url('#${uo(m.markerStart, p)}')` : void 0, [m.markerStart, p]), W = me(() => m.markerEnd ? `url('#${uo(m.markerEnd, p)}')` : void 0, [m.markerEnd, p]);
  if (m.hidden || I === null || j === null || A === null || O === null)
    return null;
  const G = ($) => {
    const { addSelectedEdges: F, unselectNodesAndEdges: Q, multiSelectionActive: ne } = N.getState();
    P && (N.setState({ nodesSelectionActive: !1 }), m.selected && ne ? (Q({ nodes: [], edges: [m] }), B.current?.blur()) : F([e])), r && r($, m);
  }, ee = i ? ($) => {
    i($, { ...m });
  } : void 0, U = s ? ($) => {
    s($, { ...m });
  } : void 0, R = a ? ($) => {
    a($, { ...m });
  } : void 0, q = l ? ($) => {
    l($, { ...m });
  } : void 0, te = c ? ($) => {
    c($, { ...m });
  } : void 0, J = ($) => {
    if (!_ && Hi.includes($.key) && P) {
      const { unselectNodesAndEdges: F, addSelectedEdges: Q } = N.getState();
      $.key === "Escape" ? (B.current?.blur(), F({ edges: [m] })) : Q([e]);
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
      updating: M,
      selectable: P
    }
  ]), onClick: G, onDoubleClick: ee, onContextMenu: U, onMouseEnter: R, onMouseMove: q, onMouseLeave: te, onKeyDown: k ? J : void 0, tabIndex: k ? 0 : void 0, role: m.ariaRole ?? (k ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": m.ariaLabel === null ? void 0 : m.ariaLabel || `Edge from ${m.source} to ${m.target}`, "aria-describedby": k ? `${ys}-${p}` : void 0, ref: B, ...m.domAttributes, children: [!L && h.jsx(S, { id: e, source: m.source, target: m.target, type: m.type, selected: m.selected, animated: m.animated, selectable: P, deletable: m.deletable ?? !0, label: m.label, labelStyle: m.labelStyle, labelShowBg: m.labelShowBg, labelBgStyle: m.labelBgStyle, labelBgPadding: m.labelBgPadding, labelBgBorderRadius: m.labelBgBorderRadius, sourceX: I, sourceY: j, targetX: A, targetY: O, sourcePosition: V, targetPosition: H, data: m.data, style: m.style, sourceHandleId: m.sourceHandle, targetHandleId: m.targetHandle, markerStart: Z, markerEnd: W, pathOptions: "pathOptions" in m ? m.pathOptions : void 0, interactionWidth: m.interactionWidth }), D && h.jsx(yg, { edge: m, isReconnectable: D, reconnectRadius: u, onReconnect: d, onReconnectStart: f, onReconnectEnd: g, sourceX: I, sourceY: j, targetX: A, targetY: O, sourcePosition: V, targetPosition: H, setUpdateHover: T, setReconnecting: b })] }) });
}
var wg = he(xg);
const vg = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function Ys({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: i, onEdgeContextMenu: s, onEdgeMouseEnter: a, onEdgeMouseMove: l, onEdgeMouseLeave: c, onEdgeClick: u, reconnectRadius: d, onEdgeDoubleClick: f, onReconnectStart: g, onReconnectEnd: p, disableKeyboardA11y: v }) {
  const { edgesFocusable: x, edgesReconnectable: y, elementsSelectable: _, onError: m } = ae(vg, le), w = rg(t);
  return h.jsxs("div", { className: "react-flow__edges", children: [h.jsx(lg, { defaultColor: e, rfId: n }), w.map((C) => h.jsx(wg, { id: C, edgesFocusable: x, edgesReconnectable: y, elementsSelectable: _, noPanClassName: r, onReconnect: i, onContextMenu: s, onMouseEnter: a, onMouseMove: l, onMouseLeave: c, onClick: u, reconnectRadius: d, onDoubleClick: f, onReconnectStart: g, onReconnectEnd: p, rfId: n, onError: m, edgeTypes: o, disableKeyboardA11y: v }, C))] });
}
Ys.displayName = "EdgeRenderer";
const bg = he(Ys), Sg = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function _g({ children: e }) {
  const t = ae(Sg);
  return h.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function Eg(e) {
  const t = zo(), n = ie(!1);
  se(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Ng = (e) => e.panZoom?.syncViewport;
function Cg(e) {
  const t = ae(Ng), n = ue();
  return se(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function kg(e) {
  return e.connection.inProgress ? { ...e.connection, to: wt(e.connection.to, e.transform) } : { ...e.connection };
}
function Ig(e) {
  return kg;
}
function Mg(e) {
  const t = Ig();
  return ae(t, le);
}
const Ag = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function Dg({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: i, height: s, isValid: a, inProgress: l } = ae(Ag, le);
  return !(i && r && l) ? null : h.jsx("svg", { style: e, width: i, height: s, className: "react-flow__connectionline react-flow__container", children: h.jsx("g", { className: ge(["react-flow__connection", Bi(a)]), children: h.jsx(Xs, { style: t, type: n, CustomComponent: o, isValid: a }) }) });
}
const Xs = ({ style: e, type: t = Ze.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: i, fromNode: s, fromHandle: a, fromPosition: l, to: c, toNode: u, toHandle: d, toPosition: f, pointer: g } = Mg();
  if (!r)
    return;
  if (n)
    return h.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: s, fromHandle: a, fromX: i.x, fromY: i.y, toX: c.x, toY: c.y, fromPosition: l, toPosition: f, connectionStatus: Bi(o), toNode: u, toHandle: d, pointer: g });
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
    case Ze.Bezier:
      [p] = es(v);
      break;
    case Ze.SimpleBezier:
      [p] = Ps(v);
      break;
    case Ze.Step:
      [p] = lo({
        ...v,
        borderRadius: 0
      });
      break;
    case Ze.SmoothStep:
      [p] = lo(v);
      break;
    default:
      [p] = ns(v);
  }
  return h.jsx("path", { d: p, fill: "none", className: "react-flow__connection-path", style: e });
};
Xs.displayName = "ConnectionLine";
const Pg = {};
function ni(e = Pg) {
  ie(e), ue(), se(() => {
  }, [e]);
}
function $g() {
  ue(), ie(!1), se(() => {
  }, []);
}
function Ws({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: i, onEdgeDoubleClick: s, onNodeMouseEnter: a, onNodeMouseMove: l, onNodeMouseLeave: c, onNodeContextMenu: u, onSelectionContextMenu: d, onSelectionStart: f, onSelectionEnd: g, connectionLineType: p, connectionLineStyle: v, connectionLineComponent: x, connectionLineContainerStyle: y, selectionKeyCode: _, selectionOnDrag: m, selectionMode: w, multiSelectionKeyCode: C, panActivationKeyCode: S, zoomActivationKeyCode: k, deleteKeyCode: D, onlyRenderVisibleElements: P, elementsSelectable: B, defaultViewport: M, translateExtent: T, minZoom: L, maxZoom: b, preventScrolling: N, defaultMarkerColor: E, zoomOnScroll: I, zoomOnPinch: j, panOnScroll: A, panOnScrollSpeed: O, panOnScrollMode: V, zoomOnDoubleClick: H, panOnDrag: Z, autoPanOnSelection: W, onPaneClick: G, onPaneMouseEnter: ee, onPaneMouseMove: U, onPaneMouseLeave: R, onPaneScroll: q, onPaneContextMenu: te, paneClickDistance: J, nodeClickDistance: $, onEdgeContextMenu: F, onEdgeMouseEnter: Q, onEdgeMouseMove: ne, onEdgeMouseLeave: de, reconnectRadius: ye, onReconnect: Ne, onReconnectStart: _e, onReconnectEnd: Ee, noDragClassName: Ce, noWheelClassName: Ge, noPanClassName: He, disableKeyboardA11y: Ve, nodeExtent: z, rfId: Y, viewport: X, onViewportChange: oe }) {
  return ni(e), ni(t), $g(), Eg(n), Cg(X), h.jsx(Zh, { onPaneClick: G, onPaneMouseEnter: ee, onPaneMouseMove: U, onPaneMouseLeave: R, onPaneContextMenu: te, onPaneScroll: q, paneClickDistance: J, deleteKeyCode: D, selectionKeyCode: _, selectionOnDrag: m, selectionMode: w, onSelectionStart: f, onSelectionEnd: g, multiSelectionKeyCode: C, panActivationKeyCode: S, zoomActivationKeyCode: k, elementsSelectable: B, zoomOnScroll: I, zoomOnPinch: j, zoomOnDoubleClick: H, panOnScroll: A, panOnScrollSpeed: O, panOnScrollMode: V, panOnDrag: Z, autoPanOnSelection: W, defaultViewport: M, translateExtent: T, minZoom: L, maxZoom: b, onSelectionContextMenu: d, preventScrolling: N, noDragClassName: Ce, noWheelClassName: Ge, noPanClassName: He, disableKeyboardA11y: Ve, onViewportChange: oe, isControlledViewport: !!X, children: h.jsxs(_g, { children: [h.jsx(bg, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: s, onReconnect: Ne, onReconnectStart: _e, onReconnectEnd: Ee, onlyRenderVisibleElements: P, onEdgeContextMenu: F, onEdgeMouseEnter: Q, onEdgeMouseMove: ne, onEdgeMouseLeave: de, reconnectRadius: ye, defaultMarkerColor: E, noPanClassName: He, disableKeyboardA11y: Ve, rfId: Y }), h.jsx(Dg, { style: v, type: p, component: x, containerStyle: y }), h.jsx("div", { className: "react-flow__edgelabel-renderer" }), h.jsx(og, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: i, onNodeMouseEnter: a, onNodeMouseMove: l, onNodeMouseLeave: c, onNodeContextMenu: u, nodeClickDistance: $, onlyRenderVisibleElements: P, noPanClassName: He, noDragClassName: Ce, disableKeyboardA11y: Ve, nodeExtent: z, rfId: Y }), h.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Ws.displayName = "GraphView";
const jg = he(Ws), Tg = qi(), oi = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: a, minZoom: l = 0.5, maxZoom: c = 2, nodeOrigin: u, nodeExtent: d, zIndexMode: f = "basic" } = {}) => {
  const g = /* @__PURE__ */ new Map(), p = /* @__PURE__ */ new Map(), v = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), y = o ?? t ?? [], _ = n ?? e ?? [], m = u ?? [0, 0], w = d ?? $t;
  is(v, x, y);
  const { nodesInitialized: C } = fo(_, g, p, {
    nodeOrigin: m,
    nodeExtent: w,
    zIndexMode: f
  });
  let S = [0, 0, 1];
  if (s && r && i) {
    const k = Yt(g, {
      filter: (M) => !!((M.width || M.initialWidth) && (M.height || M.initialHeight))
    }), { x: D, y: P, zoom: B } = Mo(k, r, i, l, c, a?.padding ?? 0.1);
    S = [D, P, B];
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
    translateExtent: $t,
    nodeExtent: w,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: ht.Strict,
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
    connection: { ...Oi },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: Tg,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Vi,
    zIndexMode: f,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, zg = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: a, minZoom: l, maxZoom: c, nodeOrigin: u, nodeExtent: d, zIndexMode: f }) => Gf((g, p) => {
  async function v() {
    const { nodeLookup: x, panZoom: y, fitViewOptions: _, fitViewResolver: m, width: w, height: C, minZoom: S, maxZoom: k } = p();
    y && (await Xd({
      nodes: x,
      width: w,
      height: C,
      panZoom: y,
      minZoom: S,
      maxZoom: k
    }, _), m?.resolve(!0), g({ fitViewResolver: null }));
  }
  return {
    ...oi({
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
      const { nodeLookup: y, parentLookup: _, nodeOrigin: m, elevateNodesOnSelect: w, fitViewQueued: C, zIndexMode: S, nodesSelectionActive: k } = p(), { nodesInitialized: D, hasSelectedNodes: P } = fo(x, y, _, {
        nodeOrigin: m,
        nodeExtent: d,
        elevateNodesOnSelect: w,
        checkEquality: !0,
        zIndexMode: S
      }), B = k && P;
      C && D ? (v(), g({
        nodes: x,
        nodesInitialized: D,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: B
      })) : g({ nodes: x, nodesInitialized: D, nodesSelectionActive: B });
    },
    setEdges: (x) => {
      const { connectionLookup: y, edgeLookup: _ } = p();
      is(y, _, x), g({ edges: x });
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
      const { triggerNodeChanges: y, nodeLookup: _, parentLookup: m, domNode: w, nodeOrigin: C, nodeExtent: S, debug: k, fitViewQueued: D, zIndexMode: P } = p(), { changes: B, updatedInternals: M } = pf(x, _, m, w, C, S, P);
      M && (df(_, m, { nodeOrigin: C, nodeExtent: S, zIndexMode: P }), D ? (v(), g({ fitViewQueued: !1, fitViewOptions: void 0 })) : g({}), B?.length > 0 && (k && console.log("React Flow: trigger node changes", B), y?.(B)));
    },
    updateNodePositions: (x, y = !1) => {
      const _ = [];
      let m = [];
      const { nodeLookup: w, triggerNodeChanges: C, connection: S, updateConnection: k, onNodesChangeMiddlewareMap: D } = p();
      for (const [P, B] of x) {
        const M = w.get(P), T = !!(M?.expandParent && M?.parentId && B?.position), L = {
          id: P,
          type: "position",
          position: T ? {
            x: Math.max(0, B.position.x),
            y: Math.max(0, B.position.y)
          } : B.position,
          dragging: y
        };
        if (M && S.inProgress && S.fromNode.id === M.id) {
          const b = it(M, S.fromHandle, K.Left, !0);
          k({ ...S, from: b });
        }
        T && M.parentId && _.push({
          id: P,
          parentId: M.parentId,
          rect: {
            ...B.internals.positionAbsolute,
            width: B.measured.width ?? 0,
            height: B.measured.height ?? 0
          }
        }), m.push(L);
      }
      if (_.length > 0) {
        const { parentLookup: P, nodeOrigin: B } = p(), M = To(_, w, P, B);
        m.push(...M);
      }
      for (const P of D.values())
        m = P(m);
      C(m);
    },
    triggerNodeChanges: (x) => {
      const { onNodesChange: y, setNodes: _, nodes: m, hasDefaultNodes: w, debug: C } = p();
      if (x?.length) {
        if (w) {
          const S = vs(x, m);
          _(S);
        }
        C && console.log("React Flow: trigger node changes", x), y?.(x);
      }
    },
    triggerEdgeChanges: (x) => {
      const { onEdgesChange: y, setEdges: _, edges: m, hasDefaultEdges: w, debug: C } = p();
      if (x?.length) {
        if (w) {
          const S = bs(x, m);
          _(S);
        }
        C && console.log("React Flow: trigger edge changes", x), y?.(x);
      }
    },
    addSelectedNodes: (x) => {
      const { multiSelectionActive: y, edgeLookup: _, nodeLookup: m, triggerNodeChanges: w, triggerEdgeChanges: C } = p();
      if (y) {
        const S = x.map((k) => Ke(k, !0));
        w(S);
        return;
      }
      w(ct(m, /* @__PURE__ */ new Set([...x]), !0)), C(ct(_));
    },
    addSelectedEdges: (x) => {
      const { multiSelectionActive: y, edgeLookup: _, nodeLookup: m, triggerNodeChanges: w, triggerEdgeChanges: C } = p();
      if (y) {
        const S = x.map((k) => Ke(k, !0));
        C(S);
        return;
      }
      C(ct(_, /* @__PURE__ */ new Set([...x]))), w(ct(m, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: x, edges: y } = {}) => {
      const { edges: _, nodes: m, nodeLookup: w, triggerNodeChanges: C, triggerEdgeChanges: S } = p(), k = x || m, D = y || _, P = [];
      for (const M of k) {
        if (!M.selected)
          continue;
        const T = w.get(M.id);
        T && (T.selected = !1), P.push(Ke(M.id, !1));
      }
      const B = [];
      for (const M of D)
        M.selected && B.push(Ke(M.id, !1));
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
      const C = y.reduce((k, D) => D.selected ? [...k, Ke(D.id, !1)] : k, []), S = x.reduce((k, D) => D.selected ? [...k, Ke(D.id, !1)] : k, []);
      _(C), m(S);
    },
    setNodeExtent: (x) => {
      const { nodes: y, nodeLookup: _, parentLookup: m, nodeOrigin: w, elevateNodesOnSelect: C, nodeExtent: S, zIndexMode: k } = p();
      x[0][0] === S[0][0] && x[0][1] === S[0][1] && x[1][0] === S[1][0] && x[1][1] === S[1][1] || (fo(y, _, m, {
        nodeOrigin: w,
        nodeExtent: x,
        elevateNodesOnSelect: C,
        checkEquality: !1,
        zIndexMode: k
      }), g({ nodeExtent: x }));
    },
    panBy: (x) => {
      const { transform: y, width: _, height: m, panZoom: w, translateExtent: C } = p();
      return mf({ delta: x, panZoom: w, transform: y, translateExtent: C, width: _, height: m });
    },
    setCenter: async (x, y, _) => {
      const { width: m, height: w, maxZoom: C, panZoom: S } = p();
      if (!S)
        return !1;
      const k = typeof _?.zoom < "u" ? _.zoom : C;
      return await S.setViewport({
        x: m / 2 - x * k,
        y: w / 2 - y * k,
        zoom: k
      }, { duration: _?.duration, ease: _?.ease, interpolate: _?.interpolate }), !0;
    },
    cancelConnection: () => {
      g({
        connection: { ...Oi }
      });
    },
    updateConnection: (x) => {
      g({ connection: x });
    },
    reset: () => g({ ...oi() })
  };
}, Object.is);
function Rg({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: i, initialMinZoom: s, initialMaxZoom: a, initialFitViewOptions: l, fitView: c, nodeOrigin: u, nodeExtent: d, zIndexMode: f, children: g }) {
  const [p] = re(() => zg({
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
  return h.jsx(Jf, { value: p, children: h.jsx(_h, { children: g }) });
}
function Lg({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: i, height: s, fitView: a, fitViewOptions: l, minZoom: c, maxZoom: u, nodeOrigin: d, nodeExtent: f, zIndexMode: g }) {
  return Vt(Mn) ? h.jsx(h.Fragment, { children: e }) : h.jsx(Rg, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: i, initialHeight: s, fitView: a, initialFitViewOptions: l, initialMinZoom: c, initialMaxZoom: u, nodeOrigin: d, nodeExtent: f, zIndexMode: g, children: e });
}
const Hg = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function Vg({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: i, edgeTypes: s, onNodeClick: a, onEdgeClick: l, onInit: c, onMove: u, onMoveStart: d, onMoveEnd: f, onConnect: g, onConnectStart: p, onConnectEnd: v, onClickConnectStart: x, onClickConnectEnd: y, onNodeMouseEnter: _, onNodeMouseMove: m, onNodeMouseLeave: w, onNodeContextMenu: C, onNodeDoubleClick: S, onNodeDragStart: k, onNodeDrag: D, onNodeDragStop: P, onNodesDelete: B, onEdgesDelete: M, onDelete: T, onSelectionChange: L, onSelectionDragStart: b, onSelectionDrag: N, onSelectionDragStop: E, onSelectionContextMenu: I, onSelectionStart: j, onSelectionEnd: A, onBeforeDelete: O, connectionMode: V, connectionLineType: H = Ze.Bezier, connectionLineStyle: Z, connectionLineComponent: W, connectionLineContainerStyle: G, deleteKeyCode: ee = "Backspace", selectionKeyCode: U = "Shift", selectionOnDrag: R = !1, selectionMode: q = jt.Full, panActivationKeyCode: te = "Space", multiSelectionKeyCode: J = zt() ? "Meta" : "Control", zoomActivationKeyCode: $ = zt() ? "Meta" : "Control", snapToGrid: F, snapGrid: Q, onlyRenderVisibleElements: ne = !1, selectNodesOnDrag: de, nodesDraggable: ye, autoPanOnNodeFocus: Ne, nodesConnectable: _e, nodesFocusable: Ee, nodeOrigin: Ce = xs, edgesFocusable: Ge, edgesReconnectable: He, elementsSelectable: Ve = !0, defaultViewport: z = fh, minZoom: Y = 0.5, maxZoom: X = 2, translateExtent: oe = $t, preventScrolling: ce = !0, nodeExtent: fe, defaultMarkerColor: ke = "#b1b1b7", zoomOnScroll: vt = !0, zoomOnPinch: Tn = !0, panOnScroll: ra = !1, panOnScrollSpeed: ia = 0.5, panOnScrollMode: sa = et.Free, zoomOnDoubleClick: aa = !0, panOnDrag: ca = !0, onPaneClick: la, onPaneMouseEnter: ua, onPaneMouseMove: da, onPaneMouseLeave: fa, onPaneScroll: ha, onPaneContextMenu: ga, paneClickDistance: pa = 1, nodeClickDistance: ma = 0, children: ya, onReconnect: xa, onReconnectStart: wa, onReconnectEnd: va, onEdgeContextMenu: ba, onEdgeDoubleClick: Sa, onEdgeMouseEnter: _a, onEdgeMouseMove: Ea, onEdgeMouseLeave: Na, reconnectRadius: Ca = 10, onNodesChange: ka, onEdgesChange: Ia, noDragClassName: Ma = "nodrag", noWheelClassName: Aa = "nowheel", noPanClassName: Vo = "nopan", fitView: Oo, fitViewOptions: Bo, connectOnClick: Da, attributionPosition: Pa, proOptions: $a, defaultEdgeOptions: ja, elevateNodesOnSelect: Ta = !0, elevateEdgesOnSelect: za = !1, disableKeyboardA11y: Fo = !1, autoPanOnConnect: Ra, autoPanOnNodeDrag: La, autoPanOnSelection: Ha = !0, autoPanSpeed: Va, connectionRadius: Oa, isValidConnection: Ba, onError: Fa, style: Ya, id: Yo, nodeDragThreshold: Xa, connectionDragThreshold: Wa, viewport: qa, onViewportChange: Za, width: Ua, height: Ga, colorMode: Ka = "light", debug: Qa, onScroll: Xo, ariaLabelConfig: Ja, zIndexMode: Wo = "basic", ...ec }, tc) {
  const zn = Yo || "1", nc = mh(Ka), oc = pe((qo) => {
    qo.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), Xo?.(qo);
  }, [Xo]);
  return h.jsx("div", { "data-testid": "rf__wrapper", ...ec, onScroll: oc, style: { ...Ya, ...Hg }, ref: tc, className: ge(["react-flow", r, nc]), id: Yo, role: "application", children: h.jsxs(Lg, { nodes: e, edges: t, width: Ua, height: Ga, fitView: Oo, fitViewOptions: Bo, minZoom: Y, maxZoom: X, nodeOrigin: Ce, nodeExtent: fe, zIndexMode: Wo, children: [h.jsx(ph, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: g, onConnectStart: p, onConnectEnd: v, onClickConnectStart: x, onClickConnectEnd: y, nodesDraggable: ye, autoPanOnNodeFocus: Ne, nodesConnectable: _e, nodesFocusable: Ee, edgesFocusable: Ge, edgesReconnectable: He, elementsSelectable: Ve, elevateNodesOnSelect: Ta, elevateEdgesOnSelect: za, minZoom: Y, maxZoom: X, nodeExtent: fe, onNodesChange: ka, onEdgesChange: Ia, snapToGrid: F, snapGrid: Q, connectionMode: V, translateExtent: oe, connectOnClick: Da, defaultEdgeOptions: ja, fitView: Oo, fitViewOptions: Bo, onNodesDelete: B, onEdgesDelete: M, onDelete: T, onNodeDragStart: k, onNodeDrag: D, onNodeDragStop: P, onSelectionDrag: N, onSelectionDragStart: b, onSelectionDragStop: E, onMove: u, onMoveStart: d, onMoveEnd: f, noPanClassName: Vo, nodeOrigin: Ce, rfId: zn, autoPanOnConnect: Ra, autoPanOnNodeDrag: La, autoPanSpeed: Va, onError: Fa, connectionRadius: Oa, isValidConnection: Ba, selectNodesOnDrag: de, nodeDragThreshold: Xa, connectionDragThreshold: Wa, onBeforeDelete: O, debug: Qa, ariaLabelConfig: Ja, zIndexMode: Wo }), h.jsx(jg, { onInit: c, onNodeClick: a, onEdgeClick: l, onNodeMouseEnter: _, onNodeMouseMove: m, onNodeMouseLeave: w, onNodeContextMenu: C, onNodeDoubleClick: S, nodeTypes: i, edgeTypes: s, connectionLineType: H, connectionLineStyle: Z, connectionLineComponent: W, connectionLineContainerStyle: G, selectionKeyCode: U, selectionOnDrag: R, selectionMode: q, deleteKeyCode: ee, multiSelectionKeyCode: J, panActivationKeyCode: te, zoomActivationKeyCode: $, onlyRenderVisibleElements: ne, defaultViewport: z, translateExtent: oe, minZoom: Y, maxZoom: X, preventScrolling: ce, zoomOnScroll: vt, zoomOnPinch: Tn, zoomOnDoubleClick: aa, panOnScroll: ra, panOnScrollSpeed: ia, panOnScrollMode: sa, panOnDrag: ca, autoPanOnSelection: Ha, onPaneClick: la, onPaneMouseEnter: ua, onPaneMouseMove: da, onPaneMouseLeave: fa, onPaneScroll: ha, onPaneContextMenu: ga, paneClickDistance: pa, nodeClickDistance: ma, onSelectionContextMenu: I, onSelectionStart: j, onSelectionEnd: A, onReconnect: xa, onReconnectStart: wa, onReconnectEnd: va, onEdgeContextMenu: ba, onEdgeDoubleClick: Sa, onEdgeMouseEnter: _a, onEdgeMouseMove: Ea, onEdgeMouseLeave: Na, reconnectRadius: Ca, defaultMarkerColor: ke, noDragClassName: Ma, noWheelClassName: Aa, noPanClassName: Vo, rfId: zn, disableKeyboardA11y: Fo, nodeExtent: fe, viewport: qa, onViewportChange: Za }), h.jsx(dh, { onSelectionChange: L }), ya, h.jsx(sh, { proOptions: $a, position: Pa }), h.jsx(ih, { rfId: zn, disableKeyboardA11y: Fo })] }) });
}
var Og = _s(Vg);
function Bg({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return h.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: ge(["react-flow__background-pattern", n, o]) });
}
function Fg({ radius: e, className: t }) {
  return h.jsx("circle", { cx: e, cy: e, r: e, className: ge(["react-flow__background-pattern", "dots", t]) });
}
var Ue;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(Ue || (Ue = {}));
const Yg = {
  [Ue.Dots]: 1,
  [Ue.Lines]: 1,
  [Ue.Cross]: 6
}, Xg = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function qs({
  id: e,
  variant: t = Ue.Dots,
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
  const d = ie(null), { transform: f, patternId: g } = ae(Xg, le), p = o || Yg[t], v = t === Ue.Dots, x = t === Ue.Cross, y = Array.isArray(n) ? n : [n, n], _ = [y[0] * f[2] || 1, y[1] * f[2] || 1], m = p * f[2], w = Array.isArray(i) ? i : [i, i], C = x ? [m, m] : _, S = [
    w[0] * f[2] || 1 + C[0] / 2,
    w[1] * f[2] || 1 + C[1] / 2
  ], k = `${g}${e || ""}`;
  return h.jsxs("svg", { className: ge(["react-flow__background", c]), style: {
    ...l,
    ...Dn,
    "--xy-background-color-props": a,
    "--xy-background-pattern-color-props": s
  }, ref: d, "data-testid": "rf__background", children: [h.jsx("pattern", { id: k, x: f[0] % _[0], y: f[1] % _[1], width: _[0], height: _[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${S[0]},-${S[1]})`, children: v ? h.jsx(Fg, { radius: m / 2, className: u }) : h.jsx(Bg, { dimensions: C, lineWidth: r, variant: t, className: u }) }), h.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${k})` })] });
}
qs.displayName = "Background";
const Wg = he(qs);
function qg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: h.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function Zg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: h.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function Ug() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: h.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function Gg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: h.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function Kg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: h.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function en({ children: e, className: t, ...n }) {
  return h.jsx("button", { type: "button", className: ge(["react-flow__controls-button", t]), ...n, children: e });
}
const Qg = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function Zs({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: i, onZoomOut: s, onFitView: a, onInteractiveChange: l, className: c, children: u, position: d = "bottom-left", orientation: f = "vertical", "aria-label": g }) {
  const p = ue(), { isInteractive: v, minZoomReached: x, maxZoomReached: y, ariaLabelConfig: _ } = ae(Qg, le), { zoomIn: m, zoomOut: w, fitView: C } = zo(), S = () => {
    m(), i?.();
  }, k = () => {
    w(), s?.();
  }, D = () => {
    C(r), a?.();
  }, P = () => {
    p.setState({
      nodesDraggable: !v,
      nodesConnectable: !v,
      elementsSelectable: !v
    }), l?.(!v);
  }, B = f === "horizontal" ? "horizontal" : "vertical";
  return h.jsxs(An, { className: ge(["react-flow__controls", B, c]), position: d, style: e, "data-testid": "rf__controls", "aria-label": g ?? _["controls.ariaLabel"], children: [t && h.jsxs(h.Fragment, { children: [h.jsx(en, { onClick: S, className: "react-flow__controls-zoomin", title: _["controls.zoomIn.ariaLabel"], "aria-label": _["controls.zoomIn.ariaLabel"], disabled: y, children: h.jsx(qg, {}) }), h.jsx(en, { onClick: k, className: "react-flow__controls-zoomout", title: _["controls.zoomOut.ariaLabel"], "aria-label": _["controls.zoomOut.ariaLabel"], disabled: x, children: h.jsx(Zg, {}) })] }), n && h.jsx(en, { className: "react-flow__controls-fitview", onClick: D, title: _["controls.fitView.ariaLabel"], "aria-label": _["controls.fitView.ariaLabel"], children: h.jsx(Ug, {}) }), o && h.jsx(en, { className: "react-flow__controls-interactive", onClick: P, title: _["controls.interactive.ariaLabel"], "aria-label": _["controls.interactive.ariaLabel"], children: v ? h.jsx(Kg, {}) : h.jsx(Gg, {}) }), u] });
}
Zs.displayName = "Controls";
const Jg = he(Zs);
function ep({ id: e, x: t, y: n, width: o, height: r, style: i, color: s, strokeColor: a, strokeWidth: l, className: c, borderRadius: u, shapeRendering: d, selected: f, onClick: g }) {
  const { background: p, backgroundColor: v } = i || {}, x = s || p || v;
  return h.jsx("rect", { className: ge(["react-flow__minimap-node", { selected: f }, c]), x: t, y: n, rx: u, ry: u, width: o, height: r, style: {
    fill: x,
    stroke: a,
    strokeWidth: l
  }, shapeRendering: d, onClick: g ? (y) => g(y, e) : void 0 });
}
const tp = he(ep), np = (e) => e.nodes.map((t) => t.id), Kn = (e) => e instanceof Function ? e : () => e;
function op({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: i = tp,
  onClick: s
}) {
  const a = ae(np, le), l = Kn(t), c = Kn(e), u = Kn(n), d = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return h.jsx(h.Fragment, { children: a.map((f) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    h.jsx(ip, { id: f, nodeColorFunc: l, nodeStrokeColorFunc: c, nodeClassNameFunc: u, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: i, onClick: s, shapeRendering: d }, f)
  )) });
}
function rp({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: i, shapeRendering: s, NodeComponent: a, onClick: l }) {
  const { node: c, x: u, y: d, width: f, height: g } = ae((p) => {
    const v = p.nodeLookup.get(e);
    if (!v)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const x = v.internals.userNode, { x: y, y: _ } = v.internals.positionAbsolute, { width: m, height: w } = Xe(x);
    return {
      node: x,
      x: y,
      y: _,
      width: m,
      height: w
    };
  }, le);
  return !c || c.hidden || !Zi(c) ? null : h.jsx(a, { x: u, y: d, width: f, height: g, style: c.style, selected: !!c.selected, className: o(c), color: t(c), borderRadius: r, strokeColor: n(c), strokeWidth: i, shapeRendering: s, onClick: l, id: c.id });
}
const ip = he(rp);
var sp = he(op);
const ap = 200, cp = 150, lp = (e) => !e.hidden, up = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? Wi(Yt(e.nodeLookup, { filter: lp }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, dp = "react-flow__minimap-desc";
function Us({
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
  const C = ue(), S = ie(null), { boundingRect: k, viewBB: D, rfId: P, panZoom: B, translateExtent: M, flowWidth: T, flowHeight: L, ariaLabelConfig: b } = ae(up, le), N = e?.width ?? ap, E = e?.height ?? cp, I = k.width / N, j = k.height / E, A = Math.max(I, j), O = A * N, V = A * E, H = w * A, Z = k.x - (O - k.width) / 2 - H, W = k.y - (V - k.height) / 2 - H, G = O + H * 2, ee = V + H * 2, U = `${dp}-${P}`, R = ie(0), q = ie();
  R.current = A, se(() => {
    if (S.current && B)
      return q.current = Nf({
        domNode: S.current,
        panZoom: B,
        getTransform: () => C.getState().transform,
        getViewScale: () => R.current
      }), () => {
        q.current?.destroy();
      };
  }, [B]), se(() => {
    q.current?.update({
      translateExtent: M,
      width: T,
      height: L,
      inversePan: _,
      pannable: v,
      zoomStep: m,
      zoomable: x
    });
  }, [v, x, _, m, M, T, L]);
  const te = g ? (F) => {
    const [Q, ne] = q.current?.pointer(F) || [0, 0];
    g(F, { x: Q, y: ne });
  } : void 0, J = p ? pe((F, Q) => {
    const ne = C.getState().nodeLookup.get(Q).internals.userNode;
    p(F, ne);
  }, []) : void 0, $ = y ?? b["minimap.ariaLabel"];
  return h.jsx(An, { position: f, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-background-color-props": typeof c == "string" ? c : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof d == "number" ? d * A : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof s == "number" ? s : void 0
  }, className: ge(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: h.jsxs("svg", { width: N, height: E, viewBox: `${Z} ${W} ${G} ${ee}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": U, ref: S, onClick: te, children: [$ && h.jsx("title", { id: U, children: $ }), h.jsx(sp, { onClick: J, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: i, nodeClassName: r, nodeStrokeWidth: s, nodeComponent: a }), h.jsx("path", { className: "react-flow__minimap-mask", d: `M${Z - H},${W - H}h${G + H * 2}v${ee + H * 2}h${-G - H * 2}z
        M${D.x},${D.y}h${D.width}v${D.height}h${-D.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
Us.displayName = "MiniMap";
const fp = he(Us), hp = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, gp = {
  [yt.Line]: "right",
  [yt.Handle]: "bottom-right"
};
function pp({ nodeId: e, position: t, variant: n = yt.Handle, className: o, style: r = void 0, children: i, color: s, minWidth: a = 10, minHeight: l = 10, maxWidth: c = Number.MAX_VALUE, maxHeight: u = Number.MAX_VALUE, keepAspectRatio: d = !1, resizeDirection: f, autoScale: g = !0, shouldResize: p, onResizeStart: v, onResize: x, onResizeEnd: y }) {
  const _ = ks(), m = typeof e == "string" ? e : _, w = ue(), C = ie(null), S = n === yt.Handle, k = ae(pe(hp(S && g), [S, g]), le), D = ie(null), P = t ?? gp[n];
  se(() => {
    if (!(!C.current || !m))
      return D.current || (D.current = Lf({
        domNode: C.current,
        nodeId: m,
        getStoreItems: () => {
          const { nodeLookup: M, transform: T, snapGrid: L, snapToGrid: b, nodeOrigin: N, domNode: E } = w.getState();
          return {
            nodeLookup: M,
            transform: T,
            snapGrid: L,
            snapToGrid: b,
            nodeOrigin: N,
            paneDomNode: E
          };
        },
        onChange: (M, T) => {
          const { triggerNodeChanges: L, nodeLookup: b, parentLookup: N, nodeOrigin: E } = w.getState(), I = [], j = { x: M.x, y: M.y }, A = b.get(m);
          if (A && A.expandParent && A.parentId) {
            const O = A.origin ?? E, V = M.width ?? A.measured.width ?? 0, H = M.height ?? A.measured.height ?? 0, Z = {
              id: A.id,
              parentId: A.parentId,
              rect: {
                width: V,
                height: H,
                ...Ui({
                  x: M.x ?? A.position.x,
                  y: M.y ?? A.position.y
                }, { width: V, height: H }, A.parentId, b, O)
              }
            }, W = To([Z], b, N, E);
            I.push(...W), j.x = M.x ? Math.max(O[0] * V, M.x) : void 0, j.y = M.y ? Math.max(O[1] * H, M.y) : void 0;
          }
          if (j.x !== void 0 && j.y !== void 0) {
            const O = {
              id: m,
              type: "position",
              position: { ...j }
            };
            I.push(O);
          }
          if (M.width !== void 0 && M.height !== void 0) {
            const V = {
              id: m,
              type: "dimensions",
              resizing: !0,
              setAttributes: f ? f === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: M.width,
                height: M.height
              }
            };
            I.push(V);
          }
          for (const O of T) {
            const V = {
              ...O,
              type: "position"
            };
            I.push(V);
          }
          L(I);
        },
        onEnd: ({ width: M, height: T }) => {
          const L = {
            id: m,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: M,
              height: T
            }
          };
          w.getState().triggerNodeChanges([L]);
        }
      })), D.current.update({
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
        D.current?.destroy();
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
    scale: k,
    ...s && { [S ? "backgroundColor" : "borderColor"]: s }
  }, children: i });
}
he(pp);
const mp = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), Gs = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var yp = {
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
const xp = vn(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: o,
    className: r = "",
    children: i,
    iconNode: s,
    ...a
  }, l) => Jn(
    "svg",
    {
      ref: l,
      ...yp,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: Gs("lucide", r),
      ...a
    },
    [
      ...s.map(([c, u]) => Jn(c, u)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const ve = (e, t) => {
  const n = vn(
    ({ className: o, ...r }, i) => Jn(xp, {
      ref: i,
      iconNode: t,
      className: Gs(`lucide-${mp(e)}`, o),
      ...r
    })
  );
  return n.displayName = `${e}`, n;
};
const wp = ve("Boxes", [
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
const $n = ve("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const vp = ve("ChevronDown", [
  ["path", { d: "m6 9 6 6 6-6", key: "qrunsl" }]
]);
const bp = ve("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const an = ve("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const Lt = ve("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const Sp = ve("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const _p = ve("ListTree", [
  ["path", { d: "M21 12h-8", key: "1bmf0i" }],
  ["path", { d: "M21 6H8", key: "1pqkrb" }],
  ["path", { d: "M21 18h-8", key: "1tm79t" }],
  ["path", { d: "M3 6v4c0 1.1.9 2 2 2h3", key: "1ywdgy" }],
  ["path", { d: "M3 10v6c0 1.1.9 2 2 2h3", key: "2wc746" }]
]);
const Ks = ve("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const Ep = ve("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const Np = ve("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const Cp = ve("Save", [
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
const kp = ve("Search", [
  ["circle", { cx: "11", cy: "11", r: "8", key: "4ej97u" }],
  ["path", { d: "m21 21-4.3-4.3", key: "1qie3q" }]
]);
const ri = ve("Trash2", [
  ["path", { d: "M3 6h18", key: "d0wm0j" }],
  ["path", { d: "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", key: "4alrt4" }],
  ["path", { d: "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", key: "v07s0e" }],
  ["line", { x1: "10", x2: "10", y1: "11", y2: "17", key: "1uufr5" }],
  ["line", { x1: "14", x2: "14", y1: "11", y2: "17", key: "xtxkd" }]
]), Te = "/_elsa/workflow-management";
async function Ip(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${Te}/definitions?${n.toString()}`);
}
async function Mp(e, t) {
  return e.http.getJson(`${Te}/definitions/${encodeURIComponent(t)}`);
}
async function Ap(e, t) {
  return e.http.postJson(`${Te}/definitions`, t);
}
async function Dp(e, t) {
  await e.http.deleteJson(`${Te}/definitions/${encodeURIComponent(t)}`);
}
async function Pp(e, t) {
  await e.http.postJson(`${Te}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function $p(e, t) {
  await e.http.deleteJson(`${Te}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function jp(e, t) {
  return e.http.putJson(`${Te}/drafts/${encodeURIComponent(t.id)}`, { state: t.state, layout: t.layout });
}
async function Tp(e, t) {
  return e.http.postJson(`${Te}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function zp(e, t) {
  return e.http.postJson(`${Te}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function Qs(e, t) {
  return e.http.postJson(`${Te}/executables/${encodeURIComponent(t)}/run`, {});
}
async function Rp(e) {
  return e.http.getJson("/_demo/workflows/executables");
}
async function Js(e) {
  return e.http.getJson(`${Te}/activities`);
}
const Lo = "elsa.sequence.structure", jn = "elsa.flowchart.structure";
function ii(e, t) {
  if (!e) return null;
  let n = e, o = Fe(n)[0];
  if (!o) return null;
  for (const r of t) {
    const i = Fe(n).find((a) => a.id === r.slotId);
    if (!i) return null;
    const s = i.activities.find((a) => a.nodeId === r.ownerNodeId);
    if (!s || (n = s, o = Fe(n)[0], !o)) return null;
  }
  return { owner: n, slot: o };
}
function Fe(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = Up(t), r = Qn(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: Gp(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, i]) => Qn(i)).map(([i, s]) => ({
    id: `${t.kind}:${i}`,
    label: Qp(i),
    property: i,
    mode: "generic",
    activities: Qn(s) ?? []
  }));
}
function Lp(e, t, n) {
  const o = new Map(t.map((s) => [s.activityVersionId, s])), r = new Map(n.map((s) => [s.nodeId, s])), i = e.slot.activities.map((s, a) => {
    const l = o.get(s.activityVersionId), c = r.get(s.nodeId) ?? Kp(e.slot.mode, a);
    return {
      id: s.nodeId,
      type: "workflowActivity",
      position: { x: c.x, y: c.y },
      data: {
        label: l?.displayName ?? s.activityVersionId,
        activityVersionId: s.activityVersionId,
        activityTypeKey: l?.activityTypeKey,
        childSlots: Fe(s)
      }
    };
  });
  return {
    nodes: i,
    edges: e.slot.mode === "flowchart" ? qp(e.owner) : Wp(e.slot, i)
  };
}
function po(e, t, n) {
  if (t.length === 0) {
    const a = Fe(e)[0];
    return a ? xn(e, a, n) : e;
  }
  const [o, ...r] = t, i = Fe(e).find((a) => a.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((a) => a.nodeId === o.ownerNodeId ? po(a, r, n) : a);
  return xn(e, i, s);
}
function mo(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, i = Fe(e).find((a) => a.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((a) => a.nodeId === o.ownerNodeId ? mo(a, r, n) : a);
  return xn(e, i, s);
}
function xn(e, t, n) {
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
function Hp(e, t, n) {
  const o = new Map(e.slot.activities.map((i) => [i.nodeId, i])), r = t.map((i) => o.get(i.id)).filter((i) => !!i);
  return e.slot.mode === "sequence" && r.sort((i, s) => {
    const a = t.find((c) => c.id === i.nodeId), l = t.find((c) => c.id === s.nodeId);
    return (a?.position.x ?? 0) - (l?.position.x ?? 0);
  }), xn(e.owner, e.slot, r);
}
function Vp(e, t) {
  return {
    ...e,
    structure: Xp(e.structure, t)
  };
}
function Op(e, t) {
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
function Bp(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: Yp(e)
  };
}
function Pe(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? Fp(t) : n;
}
function Fp(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function Yp(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: Lo,
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
function Xp(e, t) {
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
function Wp(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function qp(e) {
  if (e.structure?.kind !== jn) return [];
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
function Qn(e) {
  return Array.isArray(e) ? e.filter(Zp) : null;
}
function Zp(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Up(e) {
  return e.kind === Lo ? "sequence" : e.kind === jn ? "flowchart" : "generic";
}
function Gp(e) {
  return e.kind === Lo || e.kind === jn, "Activities";
}
function Kp(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function Qp(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Jp = { workflowActivity: bm }, si = "application/x-elsa-activity-version-id", em = 6, tm = 1200, nm = [10, 25, 50], om = 10;
function Cm(e) {
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
        component: () => /* @__PURE__ */ h.jsx(rm, { context: e.backend })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ h.jsx(im, { context: e.backend })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow instances",
        component: () => /* @__PURE__ */ h.jsx(sm, {})
      }
    ]
  });
}
function rm({ context: e }) {
  const [t, n] = re(ai);
  se(() => {
    const r = () => n(ai());
    return window.addEventListener("popstate", r), () => window.removeEventListener("popstate", r);
  }, []);
  const o = (r) => {
    const i = r ? `/workflows/definitions?definition=${encodeURIComponent(r)}` : "/workflows/definitions";
    window.history.pushState({}, "", i), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return t ? /* @__PURE__ */ h.jsx(vm, { context: e, definitionId: t, onBack: () => o(null) }) : /* @__PURE__ */ h.jsx(Ho, { activePath: "/workflows/definitions", title: "Definitions", children: /* @__PURE__ */ h.jsx(am, { context: e, onOpen: o }) });
}
function im({ context: e }) {
  const [t, n] = re(ci);
  return se(() => {
    const o = () => n(ci());
    return window.addEventListener("popstate", o), () => window.removeEventListener("popstate", o);
  }, []), /* @__PURE__ */ h.jsx(Ho, { activePath: "/workflows/executables", title: "Executables", children: /* @__PURE__ */ h.jsx(lm, { context: e, definitionFilter: t }) });
}
function sm() {
  return /* @__PURE__ */ h.jsx(Ho, { activePath: "/workflows/instances", title: "Instances", children: /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: "Workflow instance history will appear here when the runtime exposes an instance query endpoint." }) });
}
function Ho({ activePath: e, title: t, children: n }) {
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
function ai() {
  return new URLSearchParams(window.location.search).get("definition");
}
function ci() {
  return new URLSearchParams(window.location.search).get("definition");
}
function am({ context: e, onOpen: t }) {
  const [n, o] = re(""), [r, i] = re("active"), [s, a] = re(1), [l, c] = re(om), [u, d] = re("loading"), [f, g] = re(""), [p, v] = re(""), [x, y] = re([]), [_, m] = re(0), [w, C] = re(() => /* @__PURE__ */ new Set()), [S, k] = re(null), [D, P] = re(!1), [B, M] = re([]), [T, L] = re("idle"), b = ie(null), N = me(() => x.map(($) => $.id), [x]), E = N.filter(($) => w.has($)).length, I = N.length > 0 && E === N.length, j = pe(async () => {
    d("loading"), g("");
    try {
      const $ = await Ip(e, { search: n, state: r, page: s, pageSize: l }), F = typeof $.totalCount == "number", Q = $.totalCount ?? $.definitions.length, ne = ea(Q, l);
      if (Q > 0 && s > ne) {
        a(ne);
        return;
      }
      y(F ? $.definitions : dm($.definitions, s, l)), m(Q), d("ready");
    } catch ($) {
      g($ instanceof Error ? $.message : String($)), d("failed");
    }
  }, [e, n, r, s, l]);
  se(() => {
    j();
  }, [j]), se(() => {
    b.current && (b.current.indeterminate = E > 0 && !I);
  }, [I, E]);
  const A = pe(async () => {
    if (!(T === "loading" || T === "ready")) {
      L("loading");
      try {
        const $ = await Js(e);
        M($.activities ?? []), L("ready");
      } catch ($) {
        L("failed"), g($ instanceof Error ? $.message : String($));
      }
    }
  }, [T, e]), O = () => {
    g(""), v(""), k({ name: "", description: "", rootKind: "flowchart" }), A();
  }, V = async () => {
    if (S?.name.trim()) {
      P(!0), g(""), v("");
      try {
        const $ = await Ap(e, {
          name: S.name.trim(),
          description: S.description.trim() || null,
          rootKind: S.rootKind,
          rootActivityVersionId: gm(S, B)
        });
        k(null), t($.definition.id);
      } catch ($) {
        g($ instanceof Error ? $.message : String($));
      } finally {
        P(!1);
      }
    }
  }, H = ($) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent($)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, Z = async () => {
    if (x.length === 1 && s > 1) {
      a(s - 1);
      return;
    }
    await j();
  }, W = () => C(/* @__PURE__ */ new Set()), G = ($, F) => {
    C((Q) => {
      const ne = new Set(Q);
      return F ? ne.add($) : ne.delete($), ne;
    });
  }, ee = ($) => {
    C((F) => {
      const Q = new Set(F);
      for (const ne of N)
        $ ? Q.add(ne) : Q.delete(ne);
      return Q;
    });
  }, U = ($) => {
    i($), a(1), W();
  }, R = ($) => {
    o($), a(1), W();
  }, q = async ($) => {
    if (window.confirm(`Delete workflow definition "${$.name}"? You can restore it from the Deleted view.`)) {
      v(""), g("");
      try {
        await Dp(e, $.id), G($.id, !1), v(`Deleted ${$.name}`), await Z();
      } catch (F) {
        g(F instanceof Error ? F.message : String(F));
      }
    }
  }, te = async ($) => {
    v(""), g("");
    try {
      await Pp(e, $.id), G($.id, !1), v(`Restored ${$.name}`), await Z();
    } catch (F) {
      g(F instanceof Error ? F.message : String(F));
    }
  }, J = async ($) => {
    if (window.confirm(`Permanently delete workflow definition "${$.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`)) {
      v(""), g("");
      try {
        await $p(e, $.id), G($.id, !1), v(`Permanently deleted ${$.name}`), await Z();
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
        /* @__PURE__ */ h.jsx(kp, { size: 15 }),
        /* @__PURE__ */ h.jsx("input", { value: n, onChange: ($) => R($.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
        j();
      }, children: "Refresh" }),
      /* @__PURE__ */ h.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ h.jsxs("button", { type: "button", title: "Create workflow", onClick: O, children: [
        /* @__PURE__ */ h.jsx(Ep, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    u === "failed" ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Lt, { size: 16 }),
      " ",
      f
    ] }) : null,
    u !== "failed" && f ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Lt, { size: 16 }),
      " ",
      f
    ] }) : null,
    p ? /* @__PURE__ */ h.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ h.jsx($n, { size: 14 }),
      " ",
      p
    ] }) : null,
    w.size > 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ h.jsxs("span", { children: [
        w.size,
        " selected"
      ] }),
      /* @__PURE__ */ h.jsx("button", { type: "button", onClick: W, children: "Clear selection" })
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
              checked: I,
              onChange: ($) => ee($.target.checked),
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
                  onChange: (F) => G($.id, F.target.checked),
                  "aria-label": `Select workflow definition ${$.name}`
                }
              ) }),
              /* @__PURE__ */ h.jsxs("span", { children: [
                /* @__PURE__ */ h.jsx("strong", { children: $.name }),
                /* @__PURE__ */ h.jsx("small", { children: $.description || $.id })
              ] }),
              /* @__PURE__ */ h.jsx("span", { children: $.latestVersion ?? "No version" }),
              /* @__PURE__ */ h.jsx("span", { children: r === "deleted" ? yo($.deletedAt) : $.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ h.jsx("span", { children: yo($.lastModifiedAt) }),
              /* @__PURE__ */ h.jsx("span", { className: "wf-row-actions", onClick: (F) => F.stopPropagation(), children: r === "active" ? /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
                /* @__PURE__ */ h.jsx("button", { type: "button", onClick: (F) => {
                  F.stopPropagation(), t($.id);
                }, children: "Open" }),
                /* @__PURE__ */ h.jsx("button", { type: "button", onClick: (F) => {
                  F.stopPropagation(), H($.id);
                }, children: "Artifacts" }),
                /* @__PURE__ */ h.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  q($);
                }, children: [
                  /* @__PURE__ */ h.jsx(ri, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
                /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
                  te($);
                }, children: [
                  /* @__PURE__ */ h.jsx(Np, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ h.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  J($);
                }, children: [
                  /* @__PURE__ */ h.jsx(ri, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          $.id
        ))
      ] }),
      /* @__PURE__ */ h.jsx(
        um,
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
      cm,
      {
        draft: S,
        activities: B,
        catalogState: T,
        creating: D,
        onChange: ($) => k($),
        onClose: () => k(null),
        onSubmit: V
      }
    ) : null
  ] });
}
function cm({ draft: e, activities: t, catalogState: n, creating: o, onChange: r, onClose: i, onSubmit: s }) {
  const a = me(() => fm(t), [t]), l = hm(e, t), c = (u) => {
    if (u.startsWith("kind:")) {
      r({ ...e, rootKind: u.slice(5), rootActivityVersionId: null });
      return;
    }
    const d = t.find((f) => f.activityVersionId === u);
    r({
      ...e,
      rootKind: ta(d) ?? e.rootKind,
      rootActivityVersionId: u
    });
  };
  return /* @__PURE__ */ h.jsx("div", { className: "wf-dialog-backdrop", role: "presentation", children: /* @__PURE__ */ h.jsx("section", { className: "wf-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "workflow-create-title", children: /* @__PURE__ */ h.jsxs(
    "form",
    {
      onSubmit: (u) => {
        u.preventDefault(), s();
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
              onChange: (u) => r({ ...e, name: u.target.value })
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
              onChange: (u) => r({ ...e, description: u.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ h.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ h.jsx("span", { children: "Root activity" }),
          /* @__PURE__ */ h.jsxs(
            "select",
            {
              "aria-label": "Root activity",
              value: l,
              onChange: (u) => c(u.target.value),
              disabled: n === "loading",
              children: [
                /* @__PURE__ */ h.jsx("optgroup", { label: "Composite roots", children: a.compositeRoots.map((u) => /* @__PURE__ */ h.jsx("option", { value: u.value, children: u.label }, u.value)) }),
                a.otherCategories.map((u) => /* @__PURE__ */ h.jsx("optgroup", { label: u.name, children: u.activities.map((d) => /* @__PURE__ */ h.jsx("option", { value: d.activityVersionId, children: Pe(d) }, d.activityVersionId)) }, u.name))
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
function lm({ context: e, definitionFilter: t }) {
  const [n, o] = re("loading"), [r, i] = re(""), [s, a] = re(""), [l, c] = re([]), u = me(
    () => t ? l.filter((g) => g.definitionId === t || g.sourceId === t) : l,
    [t, l]
  ), d = pe(async () => {
    o("loading"), i("");
    try {
      c(await Rp(e)), o("ready");
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
      await Qs(e, g.artifactId), a(`Started ${g.artifactId}`);
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
      /* @__PURE__ */ h.jsx(Lt, { size: 16 }),
      " ",
      r
    ] }) : null,
    s ? /* @__PURE__ */ h.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ h.jsx($n, { size: 14 }),
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
        /* @__PURE__ */ h.jsx("span", { children: ym(g) }),
        /* @__PURE__ */ h.jsx("span", { children: xm(g) }),
        /* @__PURE__ */ h.jsx("span", { children: yo(g.publishedAt ?? g.createdAt) }),
        /* @__PURE__ */ h.jsx("span", { className: "wf-row-actions", children: /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
          f(g);
        }, children: [
          /* @__PURE__ */ h.jsx(Ks, { size: 13 }),
          " Run"
        ] }) })
      ] }, g.artifactId))
    ] }) : null
  ] });
}
function um({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const i = ea(n, t), s = n === 0 ? 0 : (e - 1) * t + 1, a = Math.min(e * t, n);
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
      /* @__PURE__ */ h.jsx("select", { value: t, onChange: (l) => r(Number(l.target.value)), children: nm.map((l) => /* @__PURE__ */ h.jsx("option", { value: l, children: l }, l)) })
    ] }),
    /* @__PURE__ */ h.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ h.jsx(bp, { size: 14 }),
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
        /* @__PURE__ */ h.jsx(an, { size: 14 })
      ] })
    ] })
  ] });
}
function dm(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function ea(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function fm(e) {
  const t = wn(e, "flowchart"), n = wn(e, "sequence"), o = [
    { value: t?.activityVersionId ?? "kind:flowchart", label: "Flowchart" },
    { value: n?.activityVersionId ?? "kind:sequence", label: "Sequence" }
  ], r = /* @__PURE__ */ new Map();
  for (const s of e) {
    if (mm(s)) continue;
    const a = s.category || "Uncategorized";
    r.set(a, [...r.get(a) ?? [], s]);
  }
  const i = Array.from(r.entries()).sort(([s], [a]) => s.localeCompare(a)).map(([s, a]) => ({
    name: s,
    activities: a.sort((l, c) => Pe(l).localeCompare(Pe(c)))
  }));
  return { compositeRoots: o, otherCategories: i };
}
function hm(e, t) {
  return e.rootActivityVersionId ?? wn(t, e.rootKind)?.activityVersionId ?? `kind:${e.rootKind}`;
}
function gm(e, t) {
  return e.rootActivityVersionId ?? wn(t, e.rootKind)?.activityVersionId ?? null;
}
function wn(e, t) {
  return e.find((n) => ta(n) === t);
}
function ta(e) {
  return e ? na(e) ? "flowchart" : oa(e) ? "sequence" : null : null;
}
function pm(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "Uncategorized";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [o]) => n.localeCompare(o)).map(([n, o]) => ({
    category: n,
    activities: o.sort((r, i) => Pe(r).localeCompare(Pe(i)))
  }));
}
function mm(e) {
  return na(e) || oa(e);
}
function na(e) {
  return Pe(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function oa(e) {
  return Pe(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function ym(e) {
  return e.sourceKind || e.sourceId || e.sourceVersion ? [e.sourceKind, e.sourceId, e.sourceVersion].filter(Boolean).join(" / ") : e.definitionId;
}
function xm(e) {
  return wm(e.rootActivityType) || e.rootActivityType;
}
function wm(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function vm({ context: e, definitionId: t, onBack: n }) {
  const [o, r] = re(null), [i, s] = re(null), [a, l] = re([]), [c, u] = re([]), [d, f] = re([]), [g, p] = re([]), [v, x] = re(null), [y, _] = re(null), [m, w] = re(""), [C, S] = re(""), [k, D] = re(!1), [P, B] = re(null), [M, T] = re(() => /* @__PURE__ */ new Set()), L = ie(null), b = ie(""), N = ie(0), E = ie(null), I = ie(!1), j = i?.state.rootActivity ?? null, A = me(() => ii(j, c), [j, c]), O = me(() => new Map(a.map((z) => [z.activityVersionId, z])), [a]), V = me(() => pm(a), [a]), H = me(() => A?.slot.activities.find((z) => z.nodeId === y) ?? null, [A, y]), Z = H ? Fe(H) : [], W = pe(async () => {
    w("");
    const [z, Y] = await Promise.all([
      Mp(e, t),
      Js(e)
    ]), X = z.draft ?? null;
    r(z), b.current = X ? Et(X) : "", s(X), l(Y.activities ?? []), u([]), _(null);
  }, [e, t]);
  se(() => {
    W().catch((z) => w(z instanceof Error ? z.message : String(z)));
  }, [W]), se(() => {
    T((z) => {
      let Y = !1;
      const X = new Set(z);
      for (const oe of V)
        X.has(oe.category) || (X.add(oe.category), Y = !0);
      return Y ? X : z;
    });
  }, [V]), se(() => {
    if (!A) {
      f([]), p([]);
      return;
    }
    const z = Lp(A, a, i?.layout ?? []);
    f(z.nodes), p(z.edges);
  }, [A, a, i?.layout]);
  const G = (z) => {
    s((Y) => Y && { ...Y, state: { ...Y.state, rootActivity: z } });
  }, ee = pe((z, Y) => {
    const X = Bp(z, _m(z));
    if (!i?.state.rootActivity) {
      G(X), _(X.nodeId);
      return;
    }
    if (!A) {
      if (!Fe(X)[0]) {
        S(""), w("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      s((ce) => {
        if (!ce?.state.rootActivity) return ce;
        const fe = ce.state.rootActivity, ke = po(X, [], [fe]), vt = Y ? [
          ...ce.layout.filter((Tn) => Tn.nodeId !== fe.nodeId),
          {
            nodeId: fe.nodeId,
            x: Math.round(Y.x),
            y: Math.round(Y.y)
          }
        ] : ce.layout;
        return {
          ...ce,
          layout: vt,
          state: {
            ...ce.state,
            rootActivity: ke
          }
        };
      }), _(i.state.rootActivity.nodeId), w(""), S(`Wrapped root in ${Pe(z)}`);
      return;
    }
    s((oe) => {
      if (!oe?.state.rootActivity) return oe;
      const ce = ii(oe.state.rootActivity, c);
      if (!ce) return oe;
      const fe = po(oe.state.rootActivity, c, [...ce.slot.activities, X]), ke = Y ? [
        ...oe.layout.filter((vt) => vt.nodeId !== X.nodeId),
        {
          nodeId: X.nodeId,
          x: Math.round(Y.x),
          y: Math.round(Y.y)
        }
      ] : oe.layout;
      return {
        ...oe,
        layout: ke,
        state: {
          ...oe.state,
          rootActivity: fe
        }
      };
    }), _(X.nodeId);
  }, [i?.state.rootActivity, c, A]), U = pe((z, Y) => {
    if (!L.current) return null;
    const X = L.current.getBoundingClientRect();
    return v ? v.screenToFlowPosition({ x: z, y: Y }) : {
      x: z - X.left,
      y: Y - X.top
    };
  }, [v]), R = pe((z, Y, X) => {
    if (!L.current) return !1;
    const oe = L.current.getBoundingClientRect();
    if (!(Y >= oe.left && Y <= oe.right && X >= oe.top && X <= oe.bottom)) return !1;
    const fe = U(Y, X);
    return fe ? (ee(z, fe), !0) : !1;
  }, [ee, U]);
  se(() => {
    const z = (X) => {
      const oe = E.current;
      if (!oe) return;
      Math.hypot(X.clientX - oe.startX, X.clientY - oe.startY) >= em && (oe.dragging = !0);
    }, Y = (X) => {
      const oe = E.current;
      if (E.current = null, !oe?.dragging || !L.current) return;
      const ce = L.current.getBoundingClientRect();
      X.clientX >= ce.left && X.clientX <= ce.right && X.clientY >= ce.top && X.clientY <= ce.bottom && (I.current = !0, window.setTimeout(() => {
        I.current = !1;
      }, 0), R(oe.activity, X.clientX, X.clientY));
    };
    return window.addEventListener("pointermove", z), window.addEventListener("pointerup", Y), window.addEventListener("pointercancel", Y), () => {
      window.removeEventListener("pointermove", z), window.removeEventListener("pointerup", Y), window.removeEventListener("pointercancel", Y);
    };
  }, [v, R]);
  const q = (z, Y) => {
    z.dataTransfer.setData(si, Y.activityVersionId), z.dataTransfer.setData("text/plain", Y.activityVersionId), z.dataTransfer.effectAllowed = "copy";
  }, te = (z, Y) => {
    z.clientX === 0 && z.clientY === 0 || R(Y, z.clientX, z.clientY) && (I.current = !0, window.setTimeout(() => {
      I.current = !1;
    }, 0));
  }, J = (z, Y) => {
    z.button === 0 && (E.current = {
      activity: Y,
      startX: z.clientX,
      startY: z.clientY,
      dragging: !1
    });
  }, $ = (z) => {
    I.current || ee(z);
  }, F = (z) => {
    z.preventDefault(), z.dataTransfer.dropEffect = "copy";
  }, Q = (z) => {
    z.preventDefault();
    const Y = z.dataTransfer.getData(si) || z.dataTransfer.getData("text/plain"), X = O.get(Y);
    X && R(X, z.clientX, z.clientY);
  }, ne = pe(async (z, Y) => {
    const X = ++N.current, oe = Et(z);
    w("");
    try {
      const ce = await jp(e, z), fe = Et(ce);
      b.current = fe, s((ke) => !ke || ke.id !== ce.id ? ke : Et(ke) === oe ? ce : { ...ke, validationErrors: ce.validationErrors }), X === N.current && S(Y);
    } catch (ce) {
      X === N.current && (S(""), w(ce instanceof Error ? ce.message : String(ce)));
    }
  }, [e]);
  se(() => {
    if (!k || !i || Et(i) === b.current) return;
    S("Autosaving...");
    const Y = window.setTimeout(() => {
      ne(i, "Autosaved");
    }, tm);
    return () => window.clearTimeout(Y);
  }, [k, i, ne]);
  const de = async () => {
    i && (S("Saving..."), await ne(i, "Saved"));
  }, ye = async () => {
    if (i) {
      S("Promoting...");
      try {
        const z = await Tp(e, i.id), Y = await zp(e, z.versionId);
        B(Y.artifactId), S(`Published ${Y.artifactVersion}`), await W();
      } catch (z) {
        S(""), w(z instanceof Error ? z.message : String(z));
      }
    }
  }, Ne = async () => {
    if (P) {
      S("Running...");
      try {
        await Qs(e, P), S("Run dispatched");
      } catch (z) {
        S(""), w(z instanceof Error ? z.message : String(z));
      }
    }
  }, _e = (z) => f((Y) => vs(z, Y)), Ee = (z) => p((Y) => bs(z, Y)), Ce = (z) => {
    if (!i?.state.rootActivity || !A || A.slot.mode !== "flowchart") return;
    const Y = Ss(z, g), X = Vp(A.owner, Y);
    p(Y), G(mo(i.state.rootActivity, c, X));
  }, Ge = () => {
    s((z) => {
      if (!z) return z;
      const Y = Op(z.layout, d);
      if (!z.state.rootActivity || !A) return { ...z, layout: Y };
      const X = Hp(A, d);
      return {
        ...z,
        layout: Y,
        state: {
          ...z.state,
          rootActivity: mo(z.state.rootActivity, c, X)
        }
      };
    });
  }, He = (z, Y, X) => {
    u((oe) => [...oe, { ownerNodeId: z.nodeId, slotId: Y, label: X }]), _(null);
  }, Ve = (z) => {
    T((Y) => {
      const X = new Set(Y);
      return X.has(z) ? X.delete(z) : X.add(z), X;
    });
  };
  return !o || !i ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: m || "Loading workflow editor..." }) : /* @__PURE__ */ h.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ h.jsx("button", { type: "button", className: "wf-link-button", onClick: n, children: "Definitions" }),
      /* @__PURE__ */ h.jsx(an, { size: 14 }),
      /* @__PURE__ */ h.jsx("strong", { children: o.definition.name }),
      /* @__PURE__ */ h.jsx("span", { className: "wf-chip", children: "Draft" }),
      C ? /* @__PURE__ */ h.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ h.jsx($n, { size: 13 }),
        " ",
        C
      ] }) : null,
      /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ h.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ h.jsx("input", { type: "checkbox", checked: k, onChange: (z) => D(z.target.checked) }),
          /* @__PURE__ */ h.jsx("span", { children: "Autosave" })
        ] }),
        /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
          de();
        }, children: [
          /* @__PURE__ */ h.jsx(Cp, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
          ye();
        }, children: [
          /* @__PURE__ */ h.jsx(Sp, { size: 15 }),
          " Promote"
        ] }),
        /* @__PURE__ */ h.jsxs("button", { type: "button", disabled: !P, onClick: () => {
          Ne();
        }, children: [
          /* @__PURE__ */ h.jsx(Ks, { size: 15 }),
          " Run"
        ] })
      ] })
    ] }),
    m ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Lt, { size: 16 }),
      " ",
      m
    ] }) : null,
    /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-body", children: [
      /* @__PURE__ */ h.jsxs("aside", { className: "wf-palette", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ h.jsx(wp, { size: 15 }),
          " Activities"
        ] }),
        /* @__PURE__ */ h.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: V.map((z) => {
          const Y = M.has(z.category);
          return /* @__PURE__ */ h.jsxs("div", { className: "wf-palette-category", children: [
            /* @__PURE__ */ h.jsxs(
              "button",
              {
                type: "button",
                className: "wf-palette-category-toggle",
                role: "treeitem",
                "aria-expanded": Y,
                onClick: () => Ve(z.category),
                children: [
                  Y ? /* @__PURE__ */ h.jsx(vp, { size: 14 }) : /* @__PURE__ */ h.jsx(an, { size: 14 }),
                  /* @__PURE__ */ h.jsx("span", { children: z.category }),
                  /* @__PURE__ */ h.jsx("small", { children: z.activities.length })
                ]
              }
            ),
            Y ? /* @__PURE__ */ h.jsx("div", { className: "wf-palette-activities", role: "group", children: z.activities.map((X) => {
              const oe = X.description?.trim(), ce = oe ? `wf-palette-description-${X.activityVersionId}` : void 0;
              return /* @__PURE__ */ h.jsxs(
                "button",
                {
                  type: "button",
                  className: "wf-palette-activity",
                  role: "treeitem",
                  draggable: !0,
                  title: oe || Pe(X),
                  "aria-describedby": ce,
                  onClick: () => $(X),
                  onDragStart: (fe) => q(fe, X),
                  onDragEnd: (fe) => te(fe, X),
                  onPointerDown: (fe) => J(fe, X),
                  children: [
                    /* @__PURE__ */ h.jsx("strong", { children: Pe(X) }),
                    oe ? /* @__PURE__ */ h.jsx("small", { id: ce, children: oe }) : null
                  ]
                },
                X.activityVersionId
              );
            }) }) : null
          ] }, z.category);
        }) })
      ] }),
      /* @__PURE__ */ h.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-breadcrumb", children: [
          /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
            u([]), _(null);
          }, children: "Root" }),
          c.map((z, Y) => /* @__PURE__ */ h.jsxs(Ht.Fragment, { children: [
            /* @__PURE__ */ h.jsx(an, { size: 13 }),
            /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
              u(c.slice(0, Y + 1)), _(null);
            }, children: z.label })
          ] }, `${z.ownerNodeId}-${z.slotId}-${Y}`))
        ] }),
        /* @__PURE__ */ h.jsx("div", { className: "wf-canvas", ref: L, onDragOver: F, onDrop: Q, children: /* @__PURE__ */ h.jsxs(
          Og,
          {
            nodes: d,
            edges: g,
            nodeTypes: Jp,
            onInit: x,
            onNodesChange: _e,
            onEdgesChange: Ee,
            onConnect: Ce,
            onDragOver: F,
            onDrop: Q,
            onNodeClick: (z, Y) => _(Y.id),
            onNodeDragStop: Ge,
            fitView: !0,
            children: [
              /* @__PURE__ */ h.jsx(Wg, { gap: 18, size: 1 }),
              /* @__PURE__ */ h.jsx(Jg, {}),
              /* @__PURE__ */ h.jsx(fp, { pannable: !0, zoomable: !0 })
            ]
          }
        ) }),
        /* @__PURE__ */ h.jsx(Sm, { draft: i })
      ] }),
      /* @__PURE__ */ h.jsxs("aside", { className: "wf-inspector", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ h.jsx(_p, { size: 15 }),
          " Inspector"
        ] }),
        H ? /* @__PURE__ */ h.jsxs("div", { className: "wf-inspector-content", children: [
          /* @__PURE__ */ h.jsx("h3", { children: d.find((z) => z.id === H.nodeId)?.data.label ?? H.nodeId }),
          /* @__PURE__ */ h.jsxs("dl", { children: [
            /* @__PURE__ */ h.jsx("dt", { children: "Node ID" }),
            /* @__PURE__ */ h.jsx("dd", { children: H.nodeId }),
            /* @__PURE__ */ h.jsx("dt", { children: "Activity version" }),
            /* @__PURE__ */ h.jsx("dd", { children: H.activityVersionId })
          ] }),
          Z.length > 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-slot-list", children: [
            /* @__PURE__ */ h.jsx("span", { children: "Embedded slots" }),
            Z.map((z) => /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => He(H, z.id, `${d.find((Y) => Y.id === H.nodeId)?.data.label ?? H.nodeId} / ${z.label}`), children: [
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
function bm({ data: e, selected: t }) {
  const n = e;
  return /* @__PURE__ */ h.jsxs("div", { className: t ? "wf-node selected" : "wf-node", children: [
    /* @__PURE__ */ h.jsx(xt, { type: "target", position: K.Left }),
    /* @__PURE__ */ h.jsx("strong", { children: n.label }),
    /* @__PURE__ */ h.jsx("small", { children: n.activityTypeKey ?? n.activityVersionId }),
    n.childSlots.length > 0 ? /* @__PURE__ */ h.jsxs("span", { children: [
      n.childSlots.length,
      " embedded slot",
      n.childSlots.length === 1 ? "" : "s"
    ] }) : null,
    /* @__PURE__ */ h.jsx(xt, { type: "source", position: K.Right })
  ] });
}
function Sm({ draft: e }) {
  return e.validationErrors.length ? /* @__PURE__ */ h.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ h.jsx(Lt, { size: 14 }),
    e.validationErrors.length,
    " validation issue",
    e.validationErrors.length === 1 ? "" : "s"
  ] }) : /* @__PURE__ */ h.jsxs("div", { className: "wf-validation ok", children: [
    /* @__PURE__ */ h.jsx($n, { size: 14 }),
    " No validation errors"
  ] });
}
function _m(e) {
  return `${Pe(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function Et(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function yo(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
export {
  Cm as register
};
