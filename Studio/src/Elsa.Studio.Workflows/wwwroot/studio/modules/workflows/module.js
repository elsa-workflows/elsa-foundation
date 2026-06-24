import it, { memo as ve, forwardRef as eo, useRef as ae, useEffect as ce, useCallback as pe, useContext as an, useMemo as ge, useState as ne, createContext as gr, useLayoutEffect as Pc, createElement as Qo, useId as $c } from "react";
import "@tanstack/react-query";
function Tc(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var To = { exports: {} }, Vt = {};
var Fr;
function Rc() {
  if (Fr) return Vt;
  Fr = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function n(o, r, i) {
    var s = null;
    if (i !== void 0 && (s = "" + i), r.key !== void 0 && (s = "" + r.key), "key" in r) {
      i = {};
      for (var c in r)
        c !== "key" && (i[c] = r[c]);
    } else i = r;
    return r = i.ref, {
      $$typeof: e,
      type: o,
      key: s,
      ref: r !== void 0 ? r : null,
      props: i
    };
  }
  return Vt.Fragment = t, Vt.jsx = n, Vt.jsxs = n, Vt;
}
var Wr;
function zc() {
  return Wr || (Wr = 1, To.exports = Rc()), To.exports;
}
var h = zc();
function be(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, o; n < e.length; n++)
      (o = be(e[n])) !== "" && (t += (t && " ") + o);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var Lc = { value: () => {
} };
function to() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new Pn(n);
}
function Pn(e) {
  this._ = e;
}
function Hc(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
Pn.prototype = to.prototype = {
  constructor: Pn,
  on: function(e, t) {
    var n = this._, o = Hc(e + "", n), r, i = -1, s = o.length;
    if (arguments.length < 2) {
      for (; ++i < s; ) if ((r = (e = o[i]).type) && (r = Vc(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++i < s; )
      if (r = (e = o[i]).type) n[r] = Yr(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = Yr(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new Pn(e);
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
function Vc(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function Yr(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = Lc, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var Jo = "http://www.w3.org/1999/xhtml";
const Xr = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: Jo,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function no(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Xr.hasOwnProperty(t) ? { space: Xr[t], local: e } : e;
}
function Oc(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === Jo && t.documentElement.namespaceURI === Jo ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Bc(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function cs(e) {
  var t = no(e);
  return (t.local ? Bc : Oc)(t);
}
function Fc() {
}
function mr(e) {
  return e == null ? Fc : function() {
    return this.querySelector(e);
  };
}
function Wc(e) {
  typeof e != "function" && (e = mr(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = new Array(s), l, a, u = 0; u < s; ++u)
      (l = i[u]) && (a = e.call(l, l.__data__, u, i)) && ("__data__" in l && (a.__data__ = l.__data__), c[u] = a);
  return new $e(o, this._parents);
}
function Yc(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function Xc() {
  return [];
}
function ls(e) {
  return e == null ? Xc : function() {
    return this.querySelectorAll(e);
  };
}
function qc(e) {
  return function() {
    return Yc(e.apply(this, arguments));
  };
}
function Zc(e) {
  typeof e == "function" ? e = qc(e) : e = ls(e);
  for (var t = this._groups, n = t.length, o = [], r = [], i = 0; i < n; ++i)
    for (var s = t[i], c = s.length, l, a = 0; a < c; ++a)
      (l = s[a]) && (o.push(e.call(l, l.__data__, a, s)), r.push(l));
  return new $e(o, r);
}
function us(e) {
  return function() {
    return this.matches(e);
  };
}
function ds(e) {
  return function(t) {
    return t.matches(e);
  };
}
var Uc = Array.prototype.find;
function Kc(e) {
  return function() {
    return Uc.call(this.children, e);
  };
}
function Gc() {
  return this.firstElementChild;
}
function Qc(e) {
  return this.select(e == null ? Gc : Kc(typeof e == "function" ? e : ds(e)));
}
var Jc = Array.prototype.filter;
function el() {
  return Array.from(this.children);
}
function tl(e) {
  return function() {
    return Jc.call(this.children, e);
  };
}
function nl(e) {
  return this.selectAll(e == null ? el : tl(typeof e == "function" ? e : ds(e)));
}
function ol(e) {
  typeof e != "function" && (e = us(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = [], l, a = 0; a < s; ++a)
      (l = i[a]) && e.call(l, l.__data__, a, i) && c.push(l);
  return new $e(o, this._parents);
}
function fs(e) {
  return new Array(e.length);
}
function rl() {
  return new $e(this._enter || this._groups.map(fs), this._parents);
}
function On(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
On.prototype = {
  constructor: On,
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
function il(e) {
  return function() {
    return e;
  };
}
function sl(e, t, n, o, r, i) {
  for (var s = 0, c, l = t.length, a = i.length; s < a; ++s)
    (c = t[s]) ? (c.__data__ = i[s], o[s] = c) : n[s] = new On(e, i[s]);
  for (; s < l; ++s)
    (c = t[s]) && (r[s] = c);
}
function al(e, t, n, o, r, i, s) {
  var c, l, a = /* @__PURE__ */ new Map(), u = t.length, d = i.length, f = new Array(u), m;
  for (c = 0; c < u; ++c)
    (l = t[c]) && (f[c] = m = s.call(l, l.__data__, c, t) + "", a.has(m) ? r[c] = l : a.set(m, l));
  for (c = 0; c < d; ++c)
    m = s.call(e, i[c], c, i) + "", (l = a.get(m)) ? (o[c] = l, l.__data__ = i[c], a.delete(m)) : n[c] = new On(e, i[c]);
  for (c = 0; c < u; ++c)
    (l = t[c]) && a.get(f[c]) === l && (r[c] = l);
}
function cl(e) {
  return e.__data__;
}
function ll(e, t) {
  if (!arguments.length) return Array.from(this, cl);
  var n = t ? al : sl, o = this._parents, r = this._groups;
  typeof e != "function" && (e = il(e));
  for (var i = r.length, s = new Array(i), c = new Array(i), l = new Array(i), a = 0; a < i; ++a) {
    var u = o[a], d = r[a], f = d.length, m = ul(e.call(u, u && u.__data__, a, o)), g = m.length, x = c[a] = new Array(g), w = s[a] = new Array(g), y = l[a] = new Array(f);
    n(u, d, x, w, y, m, t);
    for (var S = 0, p = 0, v, E; S < g; ++S)
      if (v = x[S]) {
        for (S >= p && (p = S + 1); !(E = w[p]) && ++p < g; ) ;
        v._next = E || null;
      }
  }
  return s = new $e(s, o), s._enter = c, s._exit = l, s;
}
function ul(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function dl() {
  return new $e(this._exit || this._groups.map(fs), this._parents);
}
function fl(e, t, n) {
  var o = this.enter(), r = this, i = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? i.remove() : n(i), o && r ? o.merge(r).order() : r;
}
function hl(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, i = o.length, s = Math.min(r, i), c = new Array(r), l = 0; l < s; ++l)
    for (var a = n[l], u = o[l], d = a.length, f = c[l] = new Array(d), m, g = 0; g < d; ++g)
      (m = a[g] || u[g]) && (f[g] = m);
  for (; l < r; ++l)
    c[l] = n[l];
  return new $e(c, this._parents);
}
function pl() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, i = o[r], s; --r >= 0; )
      (s = o[r]) && (i && s.compareDocumentPosition(i) ^ 4 && i.parentNode.insertBefore(s, i), i = s);
  return this;
}
function gl(e) {
  e || (e = ml);
  function t(d, f) {
    return d && f ? e(d.__data__, f.__data__) : !d - !f;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), i = 0; i < o; ++i) {
    for (var s = n[i], c = s.length, l = r[i] = new Array(c), a, u = 0; u < c; ++u)
      (a = s[u]) && (l[u] = a);
    l.sort(t);
  }
  return new $e(r, this._parents).order();
}
function ml(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function yl() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function xl() {
  return Array.from(this);
}
function wl() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length; r < i; ++r) {
      var s = o[r];
      if (s) return s;
    }
  return null;
}
function vl() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function bl() {
  return !this.node();
}
function Sl(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], i = 0, s = r.length, c; i < s; ++i)
      (c = r[i]) && e.call(c, c.__data__, i, r);
  return this;
}
function Nl(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function El(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function _l(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function Cl(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function kl(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function Il(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function Ml(e, t) {
  var n = no(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? El : Nl : typeof t == "function" ? n.local ? Il : kl : n.local ? Cl : _l)(n, t));
}
function hs(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function Al(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Dl(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function jl(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function Pl(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? Al : typeof t == "function" ? jl : Dl)(e, t, n ?? "")) : Nt(this.node(), e);
}
function Nt(e, t) {
  return e.style.getPropertyValue(t) || hs(e).getComputedStyle(e, null).getPropertyValue(t);
}
function $l(e) {
  return function() {
    delete this[e];
  };
}
function Tl(e, t) {
  return function() {
    this[e] = t;
  };
}
function Rl(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function zl(e, t) {
  return arguments.length > 1 ? this.each((t == null ? $l : typeof t == "function" ? Rl : Tl)(e, t)) : this.node()[e];
}
function ps(e) {
  return e.trim().split(/^|\s+/);
}
function yr(e) {
  return e.classList || new gs(e);
}
function gs(e) {
  this._node = e, this._names = ps(e.getAttribute("class") || "");
}
gs.prototype = {
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
function ms(e, t) {
  for (var n = yr(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function ys(e, t) {
  for (var n = yr(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function Ll(e) {
  return function() {
    ms(this, e);
  };
}
function Hl(e) {
  return function() {
    ys(this, e);
  };
}
function Vl(e, t) {
  return function() {
    (t.apply(this, arguments) ? ms : ys)(this, e);
  };
}
function Ol(e, t) {
  var n = ps(e + "");
  if (arguments.length < 2) {
    for (var o = yr(this.node()), r = -1, i = n.length; ++r < i; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? Vl : t ? Ll : Hl)(n, t));
}
function Bl() {
  this.textContent = "";
}
function Fl(e) {
  return function() {
    this.textContent = e;
  };
}
function Wl(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function Yl(e) {
  return arguments.length ? this.each(e == null ? Bl : (typeof e == "function" ? Wl : Fl)(e)) : this.node().textContent;
}
function Xl() {
  this.innerHTML = "";
}
function ql(e) {
  return function() {
    this.innerHTML = e;
  };
}
function Zl(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function Ul(e) {
  return arguments.length ? this.each(e == null ? Xl : (typeof e == "function" ? Zl : ql)(e)) : this.node().innerHTML;
}
function Kl() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function Gl() {
  return this.each(Kl);
}
function Ql() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Jl() {
  return this.each(Ql);
}
function eu(e) {
  var t = typeof e == "function" ? e : cs(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function tu() {
  return null;
}
function nu(e, t) {
  var n = typeof e == "function" ? e : cs(e), o = t == null ? tu : typeof t == "function" ? t : mr(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function ou() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function ru() {
  return this.each(ou);
}
function iu() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function su() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function au(e) {
  return this.select(e ? su : iu);
}
function cu(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function lu(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function uu(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function du(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, i; n < r; ++n)
        i = t[n], (!e.type || i.type === e.type) && i.name === e.name ? this.removeEventListener(i.type, i.listener, i.options) : t[++o] = i;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function fu(e, t, n) {
  return function() {
    var o = this.__on, r, i = lu(t);
    if (o) {
      for (var s = 0, c = o.length; s < c; ++s)
        if ((r = o[s]).type === e.type && r.name === e.name) {
          this.removeEventListener(r.type, r.listener, r.options), this.addEventListener(r.type, r.listener = i, r.options = n), r.value = t;
          return;
        }
    }
    this.addEventListener(e.type, i, n), r = { type: e.type, name: e.name, value: t, listener: i, options: n }, o ? o.push(r) : this.__on = [r];
  };
}
function hu(e, t, n) {
  var o = uu(e + ""), r, i = o.length, s;
  if (arguments.length < 2) {
    var c = this.node().__on;
    if (c) {
      for (var l = 0, a = c.length, u; l < a; ++l)
        for (r = 0, u = c[l]; r < i; ++r)
          if ((s = o[r]).type === u.type && s.name === u.name)
            return u.value;
    }
    return;
  }
  for (c = t ? fu : du, r = 0; r < i; ++r) this.each(c(o[r], t, n));
  return this;
}
function xs(e, t, n) {
  var o = hs(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function pu(e, t) {
  return function() {
    return xs(this, e, t);
  };
}
function gu(e, t) {
  return function() {
    return xs(this, e, t.apply(this, arguments));
  };
}
function mu(e, t) {
  return this.each((typeof t == "function" ? gu : pu)(e, t));
}
function* yu() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length, s; r < i; ++r)
      (s = o[r]) && (yield s);
}
var ws = [null];
function $e(e, t) {
  this._groups = e, this._parents = t;
}
function cn() {
  return new $e([[document.documentElement]], ws);
}
function xu() {
  return this;
}
$e.prototype = cn.prototype = {
  constructor: $e,
  select: Wc,
  selectAll: Zc,
  selectChild: Qc,
  selectChildren: nl,
  filter: ol,
  data: ll,
  enter: rl,
  exit: dl,
  join: fl,
  merge: hl,
  selection: xu,
  order: pl,
  sort: gl,
  call: yl,
  nodes: xl,
  node: wl,
  size: vl,
  empty: bl,
  each: Sl,
  attr: Ml,
  style: Pl,
  property: zl,
  classed: Ol,
  text: Yl,
  html: Ul,
  raise: Gl,
  lower: Jl,
  append: eu,
  insert: nu,
  remove: ru,
  clone: au,
  datum: cu,
  on: hu,
  dispatch: mu,
  [Symbol.iterator]: yu
};
function Pe(e) {
  return typeof e == "string" ? new $e([[document.querySelector(e)]], [document.documentElement]) : new $e([[e]], ws);
}
function wu(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Oe(e, t) {
  if (e = wu(e), t === void 0 && (t = e.currentTarget), t) {
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
const vu = { passive: !1 }, Ut = { capture: !0, passive: !1 };
function Ro(e) {
  e.stopImmediatePropagation();
}
function bt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function vs(e) {
  var t = e.document.documentElement, n = Pe(e).on("dragstart.drag", bt, Ut);
  "onselectstart" in t ? n.on("selectstart.drag", bt, Ut) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function bs(e, t) {
  var n = e.document.documentElement, o = Pe(e).on("dragstart.drag", null);
  t && (o.on("click.drag", bt, Ut), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const Nn = (e) => () => e;
function er(e, {
  sourceEvent: t,
  subject: n,
  target: o,
  identifier: r,
  active: i,
  x: s,
  y: c,
  dx: l,
  dy: a,
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
    y: { value: c, enumerable: !0, configurable: !0 },
    dx: { value: l, enumerable: !0, configurable: !0 },
    dy: { value: a, enumerable: !0, configurable: !0 },
    _: { value: u }
  });
}
er.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function bu(e) {
  return !e.ctrlKey && !e.button;
}
function Su() {
  return this.parentNode;
}
function Nu(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function Eu() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Ss() {
  var e = bu, t = Su, n = Nu, o = Eu, r = {}, i = to("start", "drag", "end"), s = 0, c, l, a, u, d = 0;
  function f(v) {
    v.on("mousedown.drag", m).filter(o).on("touchstart.drag", w).on("touchmove.drag", y, vu).on("touchend.drag touchcancel.drag", S).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function m(v, E) {
    if (!(u || !e.call(this, v, E))) {
      var N = p(this, t.call(this, v, E), v, E, "mouse");
      N && (Pe(v.view).on("mousemove.drag", g, Ut).on("mouseup.drag", x, Ut), vs(v.view), Ro(v), a = !1, c = v.clientX, l = v.clientY, N("start", v));
    }
  }
  function g(v) {
    if (bt(v), !a) {
      var E = v.clientX - c, N = v.clientY - l;
      a = E * E + N * N > d;
    }
    r.mouse("drag", v);
  }
  function x(v) {
    Pe(v.view).on("mousemove.drag mouseup.drag", null), bs(v.view, a), bt(v), r.mouse("end", v);
  }
  function w(v, E) {
    if (e.call(this, v, E)) {
      var N = v.changedTouches, I = t.call(this, v, E), D = N.length, j, H;
      for (j = 0; j < D; ++j)
        (H = p(this, I, v, E, N[j].identifier, N[j])) && (Ro(v), H("start", v, N[j]));
    }
  }
  function y(v) {
    var E = v.changedTouches, N = E.length, I, D;
    for (I = 0; I < N; ++I)
      (D = r[E[I].identifier]) && (bt(v), D("drag", v, E[I]));
  }
  function S(v) {
    var E = v.changedTouches, N = E.length, I, D;
    for (u && clearTimeout(u), u = setTimeout(function() {
      u = null;
    }, 500), I = 0; I < N; ++I)
      (D = r[E[I].identifier]) && (Ro(v), D("end", v, E[I]));
  }
  function p(v, E, N, I, D, j) {
    var H = i.copy(), M = Oe(j || N, E), R, V, b;
    if ((b = n.call(v, new er("beforestart", {
      sourceEvent: N,
      target: f,
      identifier: D,
      active: s,
      x: M[0],
      y: M[1],
      dx: 0,
      dy: 0,
      dispatch: H
    }), I)) != null)
      return R = b.x - M[0] || 0, V = b.y - M[1] || 0, function C(_, A, P) {
        var $ = M, B;
        switch (_) {
          case "start":
            r[D] = C, B = s++;
            break;
          case "end":
            delete r[D], --s;
          // falls through
          case "drag":
            M = Oe(P || A, E), B = s;
            break;
        }
        H.call(
          _,
          v,
          new er(_, {
            sourceEvent: A,
            subject: b,
            target: f,
            identifier: D,
            active: B,
            x: M[0] + R,
            y: M[1] + V,
            dx: M[0] - $[0],
            dy: M[1] - $[1],
            dispatch: H
          }),
          I
        );
      };
  }
  return f.filter = function(v) {
    return arguments.length ? (e = typeof v == "function" ? v : Nn(!!v), f) : e;
  }, f.container = function(v) {
    return arguments.length ? (t = typeof v == "function" ? v : Nn(v), f) : t;
  }, f.subject = function(v) {
    return arguments.length ? (n = typeof v == "function" ? v : Nn(v), f) : n;
  }, f.touchable = function(v) {
    return arguments.length ? (o = typeof v == "function" ? v : Nn(!!v), f) : o;
  }, f.on = function() {
    var v = i.on.apply(i, arguments);
    return v === i ? f : v;
  }, f.clickDistance = function(v) {
    return arguments.length ? (d = (v = +v) * v, f) : Math.sqrt(d);
  }, f;
}
function xr(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function Ns(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function ln() {
}
var Kt = 0.7, Bn = 1 / Kt, St = "\\s*([+-]?\\d+)\\s*", Gt = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", Ze = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", _u = /^#([0-9a-f]{3,8})$/, Cu = new RegExp(`^rgb\\(${St},${St},${St}\\)$`), ku = new RegExp(`^rgb\\(${Ze},${Ze},${Ze}\\)$`), Iu = new RegExp(`^rgba\\(${St},${St},${St},${Gt}\\)$`), Mu = new RegExp(`^rgba\\(${Ze},${Ze},${Ze},${Gt}\\)$`), Au = new RegExp(`^hsl\\(${Gt},${Ze},${Ze}\\)$`), Du = new RegExp(`^hsla\\(${Gt},${Ze},${Ze},${Gt}\\)$`), qr = {
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
xr(ln, dt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Zr,
  // Deprecated! Use color.formatHex.
  formatHex: Zr,
  formatHex8: ju,
  formatHsl: Pu,
  formatRgb: Ur,
  toString: Ur
});
function Zr() {
  return this.rgb().formatHex();
}
function ju() {
  return this.rgb().formatHex8();
}
function Pu() {
  return Es(this).formatHsl();
}
function Ur() {
  return this.rgb().formatRgb();
}
function dt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = _u.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? Kr(t) : n === 3 ? new Me(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? En(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? En(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = Cu.exec(e)) ? new Me(t[1], t[2], t[3], 1) : (t = ku.exec(e)) ? new Me(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = Iu.exec(e)) ? En(t[1], t[2], t[3], t[4]) : (t = Mu.exec(e)) ? En(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = Au.exec(e)) ? Jr(t[1], t[2] / 100, t[3] / 100, 1) : (t = Du.exec(e)) ? Jr(t[1], t[2] / 100, t[3] / 100, t[4]) : qr.hasOwnProperty(e) ? Kr(qr[e]) : e === "transparent" ? new Me(NaN, NaN, NaN, 0) : null;
}
function Kr(e) {
  return new Me(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function En(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new Me(e, t, n, o);
}
function $u(e) {
  return e instanceof ln || (e = dt(e)), e ? (e = e.rgb(), new Me(e.r, e.g, e.b, e.opacity)) : new Me();
}
function tr(e, t, n, o) {
  return arguments.length === 1 ? $u(e) : new Me(e, t, n, o ?? 1);
}
function Me(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
xr(Me, tr, Ns(ln, {
  brighter(e) {
    return e = e == null ? Bn : Math.pow(Bn, e), new Me(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Kt : Math.pow(Kt, e), new Me(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new Me(lt(this.r), lt(this.g), lt(this.b), Fn(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: Gr,
  // Deprecated! Use color.formatHex.
  formatHex: Gr,
  formatHex8: Tu,
  formatRgb: Qr,
  toString: Qr
}));
function Gr() {
  return `#${ct(this.r)}${ct(this.g)}${ct(this.b)}`;
}
function Tu() {
  return `#${ct(this.r)}${ct(this.g)}${ct(this.b)}${ct((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function Qr() {
  const e = Fn(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${lt(this.r)}, ${lt(this.g)}, ${lt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Fn(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function lt(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function ct(e) {
  return e = lt(e), (e < 16 ? "0" : "") + e.toString(16);
}
function Jr(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Be(e, t, n, o);
}
function Es(e) {
  if (e instanceof Be) return new Be(e.h, e.s, e.l, e.opacity);
  if (e instanceof ln || (e = dt(e)), !e) return new Be();
  if (e instanceof Be) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), i = Math.max(t, n, o), s = NaN, c = i - r, l = (i + r) / 2;
  return c ? (t === i ? s = (n - o) / c + (n < o) * 6 : n === i ? s = (o - t) / c + 2 : s = (t - n) / c + 4, c /= l < 0.5 ? i + r : 2 - i - r, s *= 60) : c = l > 0 && l < 1 ? 0 : s, new Be(s, c, l, e.opacity);
}
function Ru(e, t, n, o) {
  return arguments.length === 1 ? Es(e) : new Be(e, t, n, o ?? 1);
}
function Be(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
xr(Be, Ru, Ns(ln, {
  brighter(e) {
    return e = e == null ? Bn : Math.pow(Bn, e), new Be(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Kt : Math.pow(Kt, e), new Be(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new Me(
      zo(e >= 240 ? e - 240 : e + 120, r, o),
      zo(e, r, o),
      zo(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new Be(ei(this.h), _n(this.s), _n(this.l), Fn(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Fn(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${ei(this.h)}, ${_n(this.s) * 100}%, ${_n(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function ei(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function _n(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function zo(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const wr = (e) => () => e;
function zu(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function Lu(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function Hu(e) {
  return (e = +e) == 1 ? _s : function(t, n) {
    return n - t ? Lu(t, n, e) : wr(isNaN(t) ? n : t);
  };
}
function _s(e, t) {
  var n = t - e;
  return n ? zu(e, n) : wr(isNaN(e) ? t : e);
}
const Wn = (function e(t) {
  var n = Hu(t);
  function o(r, i) {
    var s = n((r = tr(r)).r, (i = tr(i)).r), c = n(r.g, i.g), l = n(r.b, i.b), a = _s(r.opacity, i.opacity);
    return function(u) {
      return r.r = s(u), r.g = c(u), r.b = l(u), r.opacity = a(u), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function Vu(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(i) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - i) + t[r] * i;
    return o;
  };
}
function Ou(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Bu(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), i = new Array(n), s;
  for (s = 0; s < o; ++s) r[s] = qt(e[s], t[s]);
  for (; s < n; ++s) i[s] = t[s];
  return function(c) {
    for (s = 0; s < o; ++s) i[s] = r[s](c);
    return i;
  };
}
function Fu(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(o) {
    return n.setTime(e * (1 - o) + t * o), n;
  };
}
function qe(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function Wu(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = qt(e[r], t[r]) : o[r] = t[r];
  return function(i) {
    for (r in n) o[r] = n[r](i);
    return o;
  };
}
var nr = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, Lo = new RegExp(nr.source, "g");
function Yu(e) {
  return function() {
    return e;
  };
}
function Xu(e) {
  return function(t) {
    return e(t) + "";
  };
}
function Cs(e, t) {
  var n = nr.lastIndex = Lo.lastIndex = 0, o, r, i, s = -1, c = [], l = [];
  for (e = e + "", t = t + ""; (o = nr.exec(e)) && (r = Lo.exec(t)); )
    (i = r.index) > n && (i = t.slice(n, i), c[s] ? c[s] += i : c[++s] = i), (o = o[0]) === (r = r[0]) ? c[s] ? c[s] += r : c[++s] = r : (c[++s] = null, l.push({ i: s, x: qe(o, r) })), n = Lo.lastIndex;
  return n < t.length && (i = t.slice(n), c[s] ? c[s] += i : c[++s] = i), c.length < 2 ? l[0] ? Xu(l[0].x) : Yu(t) : (t = l.length, function(a) {
    for (var u = 0, d; u < t; ++u) c[(d = l[u]).i] = d.x(a);
    return c.join("");
  });
}
function qt(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? wr(t) : (n === "number" ? qe : n === "string" ? (o = dt(t)) ? (t = o, Wn) : Cs : t instanceof dt ? Wn : t instanceof Date ? Fu : Ou(t) ? Vu : Array.isArray(t) ? Bu : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Wu : qe)(e, t);
}
var ti = 180 / Math.PI, or = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function ks(e, t, n, o, r, i) {
  var s, c, l;
  return (s = Math.sqrt(e * e + t * t)) && (e /= s, t /= s), (l = e * n + t * o) && (n -= e * l, o -= t * l), (c = Math.sqrt(n * n + o * o)) && (n /= c, o /= c, l /= c), e * o < t * n && (e = -e, t = -t, l = -l, s = -s), {
    translateX: r,
    translateY: i,
    rotate: Math.atan2(t, e) * ti,
    skewX: Math.atan(l) * ti,
    scaleX: s,
    scaleY: c
  };
}
var Cn;
function qu(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? or : ks(t.a, t.b, t.c, t.d, t.e, t.f);
}
function Zu(e) {
  return e == null || (Cn || (Cn = document.createElementNS("http://www.w3.org/2000/svg", "g")), Cn.setAttribute("transform", e), !(e = Cn.transform.baseVal.consolidate())) ? or : (e = e.matrix, ks(e.a, e.b, e.c, e.d, e.e, e.f));
}
function Is(e, t, n, o) {
  function r(a) {
    return a.length ? a.pop() + " " : "";
  }
  function i(a, u, d, f, m, g) {
    if (a !== d || u !== f) {
      var x = m.push("translate(", null, t, null, n);
      g.push({ i: x - 4, x: qe(a, d) }, { i: x - 2, x: qe(u, f) });
    } else (d || f) && m.push("translate(" + d + t + f + n);
  }
  function s(a, u, d, f) {
    a !== u ? (a - u > 180 ? u += 360 : u - a > 180 && (a += 360), f.push({ i: d.push(r(d) + "rotate(", null, o) - 2, x: qe(a, u) })) : u && d.push(r(d) + "rotate(" + u + o);
  }
  function c(a, u, d, f) {
    a !== u ? f.push({ i: d.push(r(d) + "skewX(", null, o) - 2, x: qe(a, u) }) : u && d.push(r(d) + "skewX(" + u + o);
  }
  function l(a, u, d, f, m, g) {
    if (a !== d || u !== f) {
      var x = m.push(r(m) + "scale(", null, ",", null, ")");
      g.push({ i: x - 4, x: qe(a, d) }, { i: x - 2, x: qe(u, f) });
    } else (d !== 1 || f !== 1) && m.push(r(m) + "scale(" + d + "," + f + ")");
  }
  return function(a, u) {
    var d = [], f = [];
    return a = e(a), u = e(u), i(a.translateX, a.translateY, u.translateX, u.translateY, d, f), s(a.rotate, u.rotate, d, f), c(a.skewX, u.skewX, d, f), l(a.scaleX, a.scaleY, u.scaleX, u.scaleY, d, f), a = u = null, function(m) {
      for (var g = -1, x = f.length, w; ++g < x; ) d[(w = f[g]).i] = w.x(m);
      return d.join("");
    };
  };
}
var Uu = Is(qu, "px, ", "px)", "deg)"), Ku = Is(Zu, ", ", ")", ")"), Gu = 1e-12;
function ni(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Qu(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Ju(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const $n = (function e(t, n, o) {
  function r(i, s) {
    var c = i[0], l = i[1], a = i[2], u = s[0], d = s[1], f = s[2], m = u - c, g = d - l, x = m * m + g * g, w, y;
    if (x < Gu)
      y = Math.log(f / a) / t, w = function(I) {
        return [
          c + I * m,
          l + I * g,
          a * Math.exp(t * I * y)
        ];
      };
    else {
      var S = Math.sqrt(x), p = (f * f - a * a + o * x) / (2 * a * n * S), v = (f * f - a * a - o * x) / (2 * f * n * S), E = Math.log(Math.sqrt(p * p + 1) - p), N = Math.log(Math.sqrt(v * v + 1) - v);
      y = (N - E) / t, w = function(I) {
        var D = I * y, j = ni(E), H = a / (n * S) * (j * Ju(t * D + E) - Qu(E));
        return [
          c + H * m,
          l + H * g,
          a * j / ni(t * D + E)
        ];
      };
    }
    return w.duration = y * 1e3 * t / Math.SQRT2, w;
  }
  return r.rho = function(i) {
    var s = Math.max(1e-3, +i), c = s * s, l = c * c;
    return e(s, c, l);
  }, r;
})(Math.SQRT2, 2, 4);
var Et = 0, Wt = 0, Ot = 0, Ms = 1e3, Yn, Yt, Xn = 0, ft = 0, oo = 0, Qt = typeof performance == "object" && performance.now ? performance : Date, As = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function vr() {
  return ft || (As(ed), ft = Qt.now() + oo);
}
function ed() {
  ft = 0;
}
function qn() {
  this._call = this._time = this._next = null;
}
qn.prototype = Ds.prototype = {
  constructor: qn,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? vr() : +n) + (t == null ? 0 : +t), !this._next && Yt !== this && (Yt ? Yt._next = this : Yn = this, Yt = this), this._call = e, this._time = n, rr();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, rr());
  }
};
function Ds(e, t, n) {
  var o = new qn();
  return o.restart(e, t, n), o;
}
function td() {
  vr(), ++Et;
  for (var e = Yn, t; e; )
    (t = ft - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --Et;
}
function oi() {
  ft = (Xn = Qt.now()) + oo, Et = Wt = 0;
  try {
    td();
  } finally {
    Et = 0, od(), ft = 0;
  }
}
function nd() {
  var e = Qt.now(), t = e - Xn;
  t > Ms && (oo -= t, Xn = e);
}
function od() {
  for (var e, t = Yn, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Yn = n);
  Yt = e, rr(o);
}
function rr(e) {
  if (!Et) {
    Wt && (Wt = clearTimeout(Wt));
    var t = e - ft;
    t > 24 ? (e < 1 / 0 && (Wt = setTimeout(oi, e - Qt.now() - oo)), Ot && (Ot = clearInterval(Ot))) : (Ot || (Xn = Qt.now(), Ot = setInterval(nd, Ms)), Et = 1, As(oi));
  }
}
function ri(e, t, n) {
  var o = new qn();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var rd = to("start", "end", "cancel", "interrupt"), id = [], js = 0, ii = 1, ir = 2, Tn = 3, si = 4, sr = 5, Rn = 6;
function ro(e, t, n, o, r, i) {
  var s = e.__transition;
  if (!s) e.__transition = {};
  else if (n in s) return;
  sd(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: rd,
    tween: id,
    time: i.time,
    delay: i.delay,
    duration: i.duration,
    ease: i.ease,
    timer: null,
    state: js
  });
}
function br(e, t) {
  var n = Ye(e, t);
  if (n.state > js) throw new Error("too late; already scheduled");
  return n;
}
function Ke(e, t) {
  var n = Ye(e, t);
  if (n.state > Tn) throw new Error("too late; already running");
  return n;
}
function Ye(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function sd(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = Ds(i, 0, n.time);
  function i(a) {
    n.state = ii, n.timer.restart(s, n.delay, n.time), n.delay <= a && s(a - n.delay);
  }
  function s(a) {
    var u, d, f, m;
    if (n.state !== ii) return l();
    for (u in o)
      if (m = o[u], m.name === n.name) {
        if (m.state === Tn) return ri(s);
        m.state === si ? (m.state = Rn, m.timer.stop(), m.on.call("interrupt", e, e.__data__, m.index, m.group), delete o[u]) : +u < t && (m.state = Rn, m.timer.stop(), m.on.call("cancel", e, e.__data__, m.index, m.group), delete o[u]);
      }
    if (ri(function() {
      n.state === Tn && (n.state = si, n.timer.restart(c, n.delay, n.time), c(a));
    }), n.state = ir, n.on.call("start", e, e.__data__, n.index, n.group), n.state === ir) {
      for (n.state = Tn, r = new Array(f = n.tween.length), u = 0, d = -1; u < f; ++u)
        (m = n.tween[u].value.call(e, e.__data__, n.index, n.group)) && (r[++d] = m);
      r.length = d + 1;
    }
  }
  function c(a) {
    for (var u = a < n.duration ? n.ease.call(null, a / n.duration) : (n.timer.restart(l), n.state = sr, 1), d = -1, f = r.length; ++d < f; )
      r[d].call(e, u);
    n.state === sr && (n.on.call("end", e, e.__data__, n.index, n.group), l());
  }
  function l() {
    n.state = Rn, n.timer.stop(), delete o[t];
    for (var a in o) return;
    delete e.__transition;
  }
}
function zn(e, t) {
  var n = e.__transition, o, r, i = !0, s;
  if (n) {
    t = t == null ? null : t + "";
    for (s in n) {
      if ((o = n[s]).name !== t) {
        i = !1;
        continue;
      }
      r = o.state > ir && o.state < sr, o.state = Rn, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[s];
    }
    i && delete e.__transition;
  }
}
function ad(e) {
  return this.each(function() {
    zn(this, e);
  });
}
function cd(e, t) {
  var n, o;
  return function() {
    var r = Ke(this, e), i = r.tween;
    if (i !== n) {
      o = n = i;
      for (var s = 0, c = o.length; s < c; ++s)
        if (o[s].name === t) {
          o = o.slice(), o.splice(s, 1);
          break;
        }
    }
    r.tween = o;
  };
}
function ld(e, t, n) {
  var o, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var i = Ke(this, e), s = i.tween;
    if (s !== o) {
      r = (o = s).slice();
      for (var c = { name: t, value: n }, l = 0, a = r.length; l < a; ++l)
        if (r[l].name === t) {
          r[l] = c;
          break;
        }
      l === a && r.push(c);
    }
    i.tween = r;
  };
}
function ud(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = Ye(this.node(), n).tween, r = 0, i = o.length, s; r < i; ++r)
      if ((s = o[r]).name === e)
        return s.value;
    return null;
  }
  return this.each((t == null ? cd : ld)(n, e, t));
}
function Sr(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = Ke(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return Ye(r, o).value[t];
  };
}
function Ps(e, t) {
  var n;
  return (typeof t == "number" ? qe : t instanceof dt ? Wn : (n = dt(t)) ? (t = n, Wn) : Cs)(e, t);
}
function dd(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function fd(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function hd(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttribute(e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function pd(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttributeNS(e.space, e.local);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function gd(e, t, n) {
  var o, r, i;
  return function() {
    var s, c = n(this), l;
    return c == null ? void this.removeAttribute(e) : (s = this.getAttribute(e), l = c + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, c)));
  };
}
function md(e, t, n) {
  var o, r, i;
  return function() {
    var s, c = n(this), l;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (s = this.getAttributeNS(e.space, e.local), l = c + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, c)));
  };
}
function yd(e, t) {
  var n = no(e), o = n === "transform" ? Ku : Ps;
  return this.attrTween(e, typeof t == "function" ? (n.local ? md : gd)(n, o, Sr(this, "attr." + e, t)) : t == null ? (n.local ? fd : dd)(n) : (n.local ? pd : hd)(n, o, t));
}
function xd(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function wd(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function vd(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && wd(e, i)), n;
  }
  return r._value = t, r;
}
function bd(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && xd(e, i)), n;
  }
  return r._value = t, r;
}
function Sd(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = no(e);
  return this.tween(n, (o.local ? vd : bd)(o, t));
}
function Nd(e, t) {
  return function() {
    br(this, e).delay = +t.apply(this, arguments);
  };
}
function Ed(e, t) {
  return t = +t, function() {
    br(this, e).delay = t;
  };
}
function _d(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Nd : Ed)(t, e)) : Ye(this.node(), t).delay;
}
function Cd(e, t) {
  return function() {
    Ke(this, e).duration = +t.apply(this, arguments);
  };
}
function kd(e, t) {
  return t = +t, function() {
    Ke(this, e).duration = t;
  };
}
function Id(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Cd : kd)(t, e)) : Ye(this.node(), t).duration;
}
function Md(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    Ke(this, e).ease = t;
  };
}
function Ad(e) {
  var t = this._id;
  return arguments.length ? this.each(Md(t, e)) : Ye(this.node(), t).ease;
}
function Dd(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    Ke(this, e).ease = n;
  };
}
function jd(e) {
  if (typeof e != "function") throw new Error();
  return this.each(Dd(this._id, e));
}
function Pd(e) {
  typeof e != "function" && (e = us(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = [], l, a = 0; a < s; ++a)
      (l = i[a]) && e.call(l, l.__data__, a, i) && c.push(l);
  return new Je(o, this._parents, this._name, this._id);
}
function $d(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, i = Math.min(o, r), s = new Array(o), c = 0; c < i; ++c)
    for (var l = t[c], a = n[c], u = l.length, d = s[c] = new Array(u), f, m = 0; m < u; ++m)
      (f = l[m] || a[m]) && (d[m] = f);
  for (; c < o; ++c)
    s[c] = t[c];
  return new Je(s, this._parents, this._name, this._id);
}
function Td(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function Rd(e, t, n) {
  var o, r, i = Td(t) ? br : Ke;
  return function() {
    var s = i(this, e), c = s.on;
    c !== o && (r = (o = c).copy()).on(t, n), s.on = r;
  };
}
function zd(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Ye(this.node(), n).on.on(e) : this.each(Rd(n, e, t));
}
function Ld(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function Hd() {
  return this.on("end.remove", Ld(this._id));
}
function Vd(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = mr(e));
  for (var o = this._groups, r = o.length, i = new Array(r), s = 0; s < r; ++s)
    for (var c = o[s], l = c.length, a = i[s] = new Array(l), u, d, f = 0; f < l; ++f)
      (u = c[f]) && (d = e.call(u, u.__data__, f, c)) && ("__data__" in u && (d.__data__ = u.__data__), a[f] = d, ro(a[f], t, n, f, a, Ye(u, n)));
  return new Je(i, this._parents, t, n);
}
function Od(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = ls(e));
  for (var o = this._groups, r = o.length, i = [], s = [], c = 0; c < r; ++c)
    for (var l = o[c], a = l.length, u, d = 0; d < a; ++d)
      if (u = l[d]) {
        for (var f = e.call(u, u.__data__, d, l), m, g = Ye(u, n), x = 0, w = f.length; x < w; ++x)
          (m = f[x]) && ro(m, t, n, x, f, g);
        i.push(f), s.push(u);
      }
  return new Je(i, s, t, n);
}
var Bd = cn.prototype.constructor;
function Fd() {
  return new Bd(this._groups, this._parents);
}
function Wd(e, t) {
  var n, o, r;
  return function() {
    var i = Nt(this, e), s = (this.style.removeProperty(e), Nt(this, e));
    return i === s ? null : i === n && s === o ? r : r = t(n = i, o = s);
  };
}
function $s(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Yd(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = Nt(this, e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Xd(e, t, n) {
  var o, r, i;
  return function() {
    var s = Nt(this, e), c = n(this), l = c + "";
    return c == null && (l = c = (this.style.removeProperty(e), Nt(this, e))), s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, c));
  };
}
function qd(e, t) {
  var n, o, r, i = "style." + t, s = "end." + i, c;
  return function() {
    var l = Ke(this, e), a = l.on, u = l.value[i] == null ? c || (c = $s(t)) : void 0;
    (a !== n || r !== u) && (o = (n = a).copy()).on(s, r = u), l.on = o;
  };
}
function Zd(e, t, n) {
  var o = (e += "") == "transform" ? Uu : Ps;
  return t == null ? this.styleTween(e, Wd(e, o)).on("end.style." + e, $s(e)) : typeof t == "function" ? this.styleTween(e, Xd(e, o, Sr(this, "style." + e, t))).each(qd(this._id, e)) : this.styleTween(e, Yd(e, o, t), n).on("end.style." + e, null);
}
function Ud(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function Kd(e, t, n) {
  var o, r;
  function i() {
    var s = t.apply(this, arguments);
    return s !== r && (o = (r = s) && Ud(e, s, n)), o;
  }
  return i._value = t, i;
}
function Gd(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, Kd(e, t, n ?? ""));
}
function Qd(e) {
  return function() {
    this.textContent = e;
  };
}
function Jd(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function ef(e) {
  return this.tween("text", typeof e == "function" ? Jd(Sr(this, "text", e)) : Qd(e == null ? "" : e + ""));
}
function tf(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function nf(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && tf(r)), t;
  }
  return o._value = e, o;
}
function of(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, nf(e));
}
function rf() {
  for (var e = this._name, t = this._id, n = Ts(), o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], c = s.length, l, a = 0; a < c; ++a)
      if (l = s[a]) {
        var u = Ye(l, t);
        ro(l, e, n, a, s, {
          time: u.time + u.delay + u.duration,
          delay: 0,
          duration: u.duration,
          ease: u.ease
        });
      }
  return new Je(o, this._parents, e, n);
}
function sf() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(i, s) {
    var c = { value: s }, l = { value: function() {
      --r === 0 && i();
    } };
    n.each(function() {
      var a = Ke(this, o), u = a.on;
      u !== e && (t = (e = u).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(l)), a.on = t;
    }), r === 0 && i();
  });
}
var af = 0;
function Je(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function Ts() {
  return ++af;
}
var Ge = cn.prototype;
Je.prototype = {
  constructor: Je,
  select: Vd,
  selectAll: Od,
  selectChild: Ge.selectChild,
  selectChildren: Ge.selectChildren,
  filter: Pd,
  merge: $d,
  selection: Fd,
  transition: rf,
  call: Ge.call,
  nodes: Ge.nodes,
  node: Ge.node,
  size: Ge.size,
  empty: Ge.empty,
  each: Ge.each,
  on: zd,
  attr: yd,
  attrTween: Sd,
  style: Zd,
  styleTween: Gd,
  text: ef,
  textTween: of,
  remove: Hd,
  tween: ud,
  delay: _d,
  duration: Id,
  ease: Ad,
  easeVarying: jd,
  end: sf,
  [Symbol.iterator]: Ge[Symbol.iterator]
};
function cf(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var lf = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: cf
};
function uf(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function df(e) {
  var t, n;
  e instanceof Je ? (t = e._id, e = e._name) : (t = Ts(), (n = lf).time = vr(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], c = s.length, l, a = 0; a < c; ++a)
      (l = s[a]) && ro(l, e, t, a, s, n || uf(l, t));
  return new Je(o, this._parents, e, t);
}
cn.prototype.interrupt = ad;
cn.prototype.transition = df;
const kn = (e) => () => e;
function ff(e, {
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
function Qe(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
Qe.prototype = {
  constructor: Qe,
  scale: function(e) {
    return e === 1 ? this : new Qe(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new Qe(this.k, this.x + this.k * e, this.y + this.k * t);
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
var io = new Qe(1, 0, 0);
Rs.prototype = Qe.prototype;
function Rs(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return io;
  return e.__zoom;
}
function Ho(e) {
  e.stopImmediatePropagation();
}
function Bt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function hf(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function pf() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function ai() {
  return this.__zoom || io;
}
function gf(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function mf() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function yf(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], i = e.invertY(t[0][1]) - n[0][1], s = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    s > i ? (i + s) / 2 : Math.min(0, i) || Math.max(0, s)
  );
}
function zs() {
  var e = hf, t = pf, n = yf, o = gf, r = mf, i = [0, 1 / 0], s = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, l = $n, a = to("start", "zoom", "end"), u, d, f, m = 500, g = 150, x = 0, w = 10;
  function y(b) {
    b.property("__zoom", ai).on("wheel.zoom", D, { passive: !1 }).on("mousedown.zoom", j).on("dblclick.zoom", H).filter(r).on("touchstart.zoom", M).on("touchmove.zoom", R).on("touchend.zoom touchcancel.zoom", V).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  y.transform = function(b, C, _, A) {
    var P = b.selection ? b.selection() : b;
    P.property("__zoom", ai), b !== P ? E(b, C, _, A) : P.interrupt().each(function() {
      N(this, arguments).event(A).start().zoom(null, typeof C == "function" ? C.apply(this, arguments) : C).end();
    });
  }, y.scaleBy = function(b, C, _, A) {
    y.scaleTo(b, function() {
      var P = this.__zoom.k, $ = typeof C == "function" ? C.apply(this, arguments) : C;
      return P * $;
    }, _, A);
  }, y.scaleTo = function(b, C, _, A) {
    y.transform(b, function() {
      var P = t.apply(this, arguments), $ = this.__zoom, B = _ == null ? v(P) : typeof _ == "function" ? _.apply(this, arguments) : _, O = $.invert(B), F = typeof C == "function" ? C.apply(this, arguments) : C;
      return n(p(S($, F), B, O), P, s);
    }, _, A);
  }, y.translateBy = function(b, C, _, A) {
    y.transform(b, function() {
      return n(this.__zoom.translate(
        typeof C == "function" ? C.apply(this, arguments) : C,
        typeof _ == "function" ? _.apply(this, arguments) : _
      ), t.apply(this, arguments), s);
    }, null, A);
  }, y.translateTo = function(b, C, _, A, P) {
    y.transform(b, function() {
      var $ = t.apply(this, arguments), B = this.__zoom, O = A == null ? v($) : typeof A == "function" ? A.apply(this, arguments) : A;
      return n(io.translate(O[0], O[1]).scale(B.k).translate(
        typeof C == "function" ? -C.apply(this, arguments) : -C,
        typeof _ == "function" ? -_.apply(this, arguments) : -_
      ), $, s);
    }, A, P);
  };
  function S(b, C) {
    return C = Math.max(i[0], Math.min(i[1], C)), C === b.k ? b : new Qe(C, b.x, b.y);
  }
  function p(b, C, _) {
    var A = C[0] - _[0] * b.k, P = C[1] - _[1] * b.k;
    return A === b.x && P === b.y ? b : new Qe(b.k, A, P);
  }
  function v(b) {
    return [(+b[0][0] + +b[1][0]) / 2, (+b[0][1] + +b[1][1]) / 2];
  }
  function E(b, C, _, A) {
    b.on("start.zoom", function() {
      N(this, arguments).event(A).start();
    }).on("interrupt.zoom end.zoom", function() {
      N(this, arguments).event(A).end();
    }).tween("zoom", function() {
      var P = this, $ = arguments, B = N(P, $).event(A), O = t.apply(P, $), F = _ == null ? v(O) : typeof _ == "function" ? _.apply(P, $) : _, Q = Math.max(O[1][0] - O[0][0], O[1][1] - O[0][1]), Z = P.__zoom, U = typeof C == "function" ? C.apply(P, $) : C, se = l(Z.invert(F).concat(Q / Z.k), U.invert(F).concat(Q / U.k));
      return function(G) {
        if (G === 1) G = U;
        else {
          var z = se(G), q = Q / z[2];
          G = new Qe(q, F[0] - z[0] * q, F[1] - z[1] * q);
        }
        B.zoom(null, G);
      };
    });
  }
  function N(b, C, _) {
    return !_ && b.__zooming || new I(b, C);
  }
  function I(b, C) {
    this.that = b, this.args = C, this.active = 0, this.sourceEvent = null, this.extent = t.apply(b, C), this.taps = 0;
  }
  I.prototype = {
    event: function(b) {
      return b && (this.sourceEvent = b), this;
    },
    start: function() {
      return ++this.active === 1 && (this.that.__zooming = this, this.emit("start")), this;
    },
    zoom: function(b, C) {
      return this.mouse && b !== "mouse" && (this.mouse[1] = C.invert(this.mouse[0])), this.touch0 && b !== "touch" && (this.touch0[1] = C.invert(this.touch0[0])), this.touch1 && b !== "touch" && (this.touch1[1] = C.invert(this.touch1[0])), this.that.__zoom = C, this.emit("zoom"), this;
    },
    end: function() {
      return --this.active === 0 && (delete this.that.__zooming, this.emit("end")), this;
    },
    emit: function(b) {
      var C = Pe(this.that).datum();
      a.call(
        b,
        this.that,
        new ff(b, {
          sourceEvent: this.sourceEvent,
          target: y,
          transform: this.that.__zoom,
          dispatch: a
        }),
        C
      );
    }
  };
  function D(b, ...C) {
    if (!e.apply(this, arguments)) return;
    var _ = N(this, C).event(b), A = this.__zoom, P = Math.max(i[0], Math.min(i[1], A.k * Math.pow(2, o.apply(this, arguments)))), $ = Oe(b);
    if (_.wheel)
      (_.mouse[0][0] !== $[0] || _.mouse[0][1] !== $[1]) && (_.mouse[1] = A.invert(_.mouse[0] = $)), clearTimeout(_.wheel);
    else {
      if (A.k === P) return;
      _.mouse = [$, A.invert($)], zn(this), _.start();
    }
    Bt(b), _.wheel = setTimeout(B, g), _.zoom("mouse", n(p(S(A, P), _.mouse[0], _.mouse[1]), _.extent, s));
    function B() {
      _.wheel = null, _.end();
    }
  }
  function j(b, ...C) {
    if (f || !e.apply(this, arguments)) return;
    var _ = b.currentTarget, A = N(this, C, !0).event(b), P = Pe(b.view).on("mousemove.zoom", F, !0).on("mouseup.zoom", Q, !0), $ = Oe(b, _), B = b.clientX, O = b.clientY;
    vs(b.view), Ho(b), A.mouse = [$, this.__zoom.invert($)], zn(this), A.start();
    function F(Z) {
      if (Bt(Z), !A.moved) {
        var U = Z.clientX - B, se = Z.clientY - O;
        A.moved = U * U + se * se > x;
      }
      A.event(Z).zoom("mouse", n(p(A.that.__zoom, A.mouse[0] = Oe(Z, _), A.mouse[1]), A.extent, s));
    }
    function Q(Z) {
      P.on("mousemove.zoom mouseup.zoom", null), bs(Z.view, A.moved), Bt(Z), A.event(Z).end();
    }
  }
  function H(b, ...C) {
    if (e.apply(this, arguments)) {
      var _ = this.__zoom, A = Oe(b.changedTouches ? b.changedTouches[0] : b, this), P = _.invert(A), $ = _.k * (b.shiftKey ? 0.5 : 2), B = n(p(S(_, $), A, P), t.apply(this, C), s);
      Bt(b), c > 0 ? Pe(this).transition().duration(c).call(E, B, A, b) : Pe(this).call(y.transform, B, A, b);
    }
  }
  function M(b, ...C) {
    if (e.apply(this, arguments)) {
      var _ = b.touches, A = _.length, P = N(this, C, b.changedTouches.length === A).event(b), $, B, O, F;
      for (Ho(b), B = 0; B < A; ++B)
        O = _[B], F = Oe(O, this), F = [F, this.__zoom.invert(F), O.identifier], P.touch0 ? !P.touch1 && P.touch0[2] !== F[2] && (P.touch1 = F, P.taps = 0) : (P.touch0 = F, $ = !0, P.taps = 1 + !!u);
      u && (u = clearTimeout(u)), $ && (P.taps < 2 && (d = F[0], u = setTimeout(function() {
        u = null;
      }, m)), zn(this), P.start());
    }
  }
  function R(b, ...C) {
    if (this.__zooming) {
      var _ = N(this, C).event(b), A = b.changedTouches, P = A.length, $, B, O, F;
      for (Bt(b), $ = 0; $ < P; ++$)
        B = A[$], O = Oe(B, this), _.touch0 && _.touch0[2] === B.identifier ? _.touch0[0] = O : _.touch1 && _.touch1[2] === B.identifier && (_.touch1[0] = O);
      if (B = _.that.__zoom, _.touch1) {
        var Q = _.touch0[0], Z = _.touch0[1], U = _.touch1[0], se = _.touch1[1], G = (G = U[0] - Q[0]) * G + (G = U[1] - Q[1]) * G, z = (z = se[0] - Z[0]) * z + (z = se[1] - Z[1]) * z;
        B = S(B, Math.sqrt(G / z)), O = [(Q[0] + U[0]) / 2, (Q[1] + U[1]) / 2], F = [(Z[0] + se[0]) / 2, (Z[1] + se[1]) / 2];
      } else if (_.touch0) O = _.touch0[0], F = _.touch0[1];
      else return;
      _.zoom("touch", n(p(B, O, F), _.extent, s));
    }
  }
  function V(b, ...C) {
    if (this.__zooming) {
      var _ = N(this, C).event(b), A = b.changedTouches, P = A.length, $, B;
      for (Ho(b), f && clearTimeout(f), f = setTimeout(function() {
        f = null;
      }, m), $ = 0; $ < P; ++$)
        B = A[$], _.touch0 && _.touch0[2] === B.identifier ? delete _.touch0 : _.touch1 && _.touch1[2] === B.identifier && delete _.touch1;
      if (_.touch1 && !_.touch0 && (_.touch0 = _.touch1, delete _.touch1), _.touch0) _.touch0[1] = this.__zoom.invert(_.touch0[0]);
      else if (_.end(), _.taps === 2 && (B = Oe(B, this), Math.hypot(d[0] - B[0], d[1] - B[1]) < w)) {
        var O = Pe(this).on("dblclick.zoom");
        O && O.apply(this, arguments);
      }
    }
  }
  return y.wheelDelta = function(b) {
    return arguments.length ? (o = typeof b == "function" ? b : kn(+b), y) : o;
  }, y.filter = function(b) {
    return arguments.length ? (e = typeof b == "function" ? b : kn(!!b), y) : e;
  }, y.touchable = function(b) {
    return arguments.length ? (r = typeof b == "function" ? b : kn(!!b), y) : r;
  }, y.extent = function(b) {
    return arguments.length ? (t = typeof b == "function" ? b : kn([[+b[0][0], +b[0][1]], [+b[1][0], +b[1][1]]]), y) : t;
  }, y.scaleExtent = function(b) {
    return arguments.length ? (i[0] = +b[0], i[1] = +b[1], y) : [i[0], i[1]];
  }, y.translateExtent = function(b) {
    return arguments.length ? (s[0][0] = +b[0][0], s[1][0] = +b[1][0], s[0][1] = +b[0][1], s[1][1] = +b[1][1], y) : [[s[0][0], s[0][1]], [s[1][0], s[1][1]]];
  }, y.constrain = function(b) {
    return arguments.length ? (n = b, y) : n;
  }, y.duration = function(b) {
    return arguments.length ? (c = +b, y) : c;
  }, y.interpolate = function(b) {
    return arguments.length ? (l = b, y) : l;
  }, y.on = function() {
    var b = a.on.apply(a, arguments);
    return b === a ? y : b;
  }, y.clickDistance = function(b) {
    return arguments.length ? (x = (b = +b) * b, y) : Math.sqrt(x);
  }, y.tapDistance = function(b) {
    return arguments.length ? (w = +b, y) : w;
  }, y;
}
const Te = {
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
}, Jt = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], Ls = ["Enter", " ", "Escape"], Hs = {
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
var _t;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(_t || (_t = {}));
var ut;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(ut || (ut = {}));
var en;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(en || (en = {}));
const Vs = {
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
var ot;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(ot || (ot = {}));
var Zn;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(Zn || (Zn = {}));
var ee;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(ee || (ee = {}));
const ci = {
  [ee.Left]: ee.Right,
  [ee.Right]: ee.Left,
  [ee.Top]: ee.Bottom,
  [ee.Bottom]: ee.Top
};
function Os(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Bs = (e) => "id" in e && "source" in e && "target" in e, xf = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), Nr = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), un = (e, t = [0, 0]) => {
  const { width: n, height: o } = et(e), r = e.origin ?? t, i = n * r[0], s = o * r[1];
  return {
    x: e.position.x - i,
    y: e.position.y - s
  };
}, wf = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const i = typeof r == "string";
    let s = !t.nodeLookup && !i ? r : void 0;
    t.nodeLookup && (s = i ? t.nodeLookup.get(r) : Nr(r) ? r : t.nodeLookup.get(r.id));
    const c = s ? Un(s, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return so(o, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return ao(n);
}, dn = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = so(n, Un(r)), o = !0);
  }), o ? ao(n) : { x: 0, y: 0, width: 0, height: 0 };
}, Er = (e, t, [n, o, r] = [0, 0, 1], i = !1, s = !1) => {
  const c = {
    ...$t(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, l = [];
  for (const a of e.values()) {
    const { measured: u, selectable: d = !0, hidden: f = !1 } = a;
    if (s && !d || f)
      continue;
    const m = u.width ?? a.width ?? a.initialWidth ?? null, g = u.height ?? a.height ?? a.initialHeight ?? null, x = tn(c, kt(a)), w = (m ?? 0) * (g ?? 0), y = i && x > 0;
    (!a.internals.handleBounds || y || x >= w || a.dragging) && l.push(a);
  }
  return l;
}, vf = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function bf(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function Sf({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: i }, s) {
  if (e.size === 0)
    return !0;
  const c = bf(e, s), l = dn(c), a = Cr(l, t, n, s?.minZoom ?? r, s?.maxZoom ?? i, s?.padding ?? 0.1);
  return await o.setViewport(a, {
    duration: s?.duration,
    ease: s?.ease,
    interpolate: s?.interpolate
  }), !0;
}
function Fs({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: i }) {
  const s = n.get(e), c = s.parentId ? n.get(s.parentId) : void 0, { x: l, y: a } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, u = s.origin ?? o;
  let d = s.extent || r;
  if (s.extent === "parent" && !s.expandParent)
    if (!c)
      i?.("005", Te.error005());
    else {
      const m = c.measured.width, g = c.measured.height;
      m && g && (d = [
        [l, a],
        [l + m, a + g]
      ]);
    }
  else c && pt(s.extent) && (d = [
    [s.extent[0][0] + l, s.extent[0][1] + a],
    [s.extent[1][0] + l, s.extent[1][1] + a]
  ]);
  const f = pt(d) ? ht(t, d, s.measured) : t;
  return (s.measured.width === void 0 || s.measured.height === void 0) && i?.("015", Te.error015()), {
    position: {
      x: f.x - l + (s.measured.width ?? 0) * u[0],
      y: f.y - a + (s.measured.height ?? 0) * u[1]
    },
    positionAbsolute: f
  };
}
async function Nf({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const i = new Set(e.map((f) => f.id)), s = [];
  for (const f of n) {
    if (f.deletable === !1)
      continue;
    const m = i.has(f.id), g = !m && f.parentId && s.find((x) => x.id === f.parentId);
    (m || g) && s.push(f);
  }
  const c = new Set(t.map((f) => f.id)), l = o.filter((f) => f.deletable !== !1), u = vf(s, l);
  for (const f of l)
    c.has(f.id) && !u.find((g) => g.id === f.id) && u.push(f);
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
const Ct = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), ht = (e = { x: 0, y: 0 }, t, n) => ({
  x: Ct(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: Ct(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Ws(e, t, n) {
  const { width: o, height: r } = et(n), { x: i, y: s } = n.internals.positionAbsolute;
  return ht(e, [
    [i, s],
    [i + o, s + r]
  ], t);
}
const li = (e, t, n) => e < t ? Ct(Math.abs(e - t), 1, t) / t : e > n ? -Ct(Math.abs(e - n), 1, t) / t : 0, _r = (e, t, n = 15, o = 40) => {
  const r = li(e.x, o, t.width - o) * n, i = li(e.y, o, t.height - o) * n;
  return [r, i];
}, so = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), ar = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), ao = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), kt = (e, t = [0, 0]) => {
  const { x: n, y: o } = Nr(e) ? e.internals.positionAbsolute : un(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, Un = (e, t = [0, 0]) => {
  const { x: n, y: o } = Nr(e) ? e.internals.positionAbsolute : un(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, Ys = (e, t) => ao(so(ar(e), ar(t))), tn = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, ui = (e) => Fe(e.width) && Fe(e.height) && Fe(e.x) && Fe(e.y), Fe = (e) => !isNaN(e) && isFinite(e), Xs = (e, t) => (n, o) => {
}, fn = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), $t = ({ x: e, y: t }, [n, o, r], i = !1, s = [1, 1]) => {
  const c = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return i ? fn(c, s) : c;
}, It = ({ x: e, y: t }, [n, o, r]) => ({
  x: e * r + n,
  y: t * r + o
});
function xt(e, t) {
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
function Ef(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = xt(e, n), r = xt(e, t);
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
    const o = xt(e.top ?? e.y ?? 0, n), r = xt(e.bottom ?? e.y ?? 0, n), i = xt(e.left ?? e.x ?? 0, t), s = xt(e.right ?? e.x ?? 0, t);
    return { top: o, right: s, bottom: r, left: i, x: i + s, y: o + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function _f(e, t, n, o, r, i) {
  const { x: s, y: c } = It(e, [t, n, o]), { x: l, y: a } = It({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), u = r - l, d = i - a;
  return {
    left: Math.floor(s),
    top: Math.floor(c),
    right: Math.floor(u),
    bottom: Math.floor(d)
  };
}
const Cr = (e, t, n, o, r, i) => {
  const s = Ef(i, t, n), c = (t - s.x) / e.width, l = (n - s.y) / e.height, a = Math.min(c, l), u = Ct(a, o, r), d = e.x + e.width / 2, f = e.y + e.height / 2, m = t / 2 - d * u, g = n / 2 - f * u, x = _f(e, m, g, u, t, n), w = {
    left: Math.min(x.left - s.left, 0),
    top: Math.min(x.top - s.top, 0),
    right: Math.min(x.right - s.right, 0),
    bottom: Math.min(x.bottom - s.bottom, 0)
  };
  return {
    x: m - w.left + w.right,
    y: g - w.top + w.bottom,
    zoom: u
  };
}, nn = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function pt(e) {
  return e != null && e !== "parent";
}
function et(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function qs(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function Zs(e, t = { width: 0, height: 0 }, n, o, r) {
  const i = { ...e }, s = o.get(n);
  if (s) {
    const c = s.origin || r;
    i.x += s.internals.positionAbsolute.x - (t.width ?? 0) * c[0], i.y += s.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return i;
}
function di(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function Cf() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function kf(e) {
  return { ...Hs, ...e || {} };
}
function Zt(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: i, y: s } = We(e), c = $t({ x: i - (r?.left ?? 0), y: s - (r?.top ?? 0) }, o), { x: l, y: a } = n ? fn(c, t) : c;
  return {
    xSnapped: l,
    ySnapped: a,
    ...c
  };
}
const kr = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), Us = (e) => e?.getRootNode?.() || window?.document, If = ["INPUT", "SELECT", "TEXTAREA"];
function Ks(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : If.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const Gs = (e) => "clientX" in e, We = (e, t) => {
  const n = Gs(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, fi = (e, t, n, o, r) => {
  const i = t.querySelectorAll(`.${e}`);
  return !i || !i.length ? null : Array.from(i).map((s) => {
    const c = s.getBoundingClientRect();
    return {
      id: s.getAttribute("data-handleid"),
      type: e,
      nodeId: r,
      position: s.getAttribute("data-handlepos"),
      x: (c.left - n.left) / o,
      y: (c.top - n.top) / o,
      ...kr(s)
    };
  });
};
function Qs({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: i, targetControlX: s, targetControlY: c }) {
  const l = e * 0.125 + r * 0.375 + s * 0.375 + n * 0.125, a = t * 0.125 + i * 0.375 + c * 0.375 + o * 0.125, u = Math.abs(l - e), d = Math.abs(a - t);
  return [l, a, u, d];
}
function In(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function hi({ pos: e, x1: t, y1: n, x2: o, y2: r, c: i }) {
  switch (e) {
    case ee.Left:
      return [t - In(t - o, i), n];
    case ee.Right:
      return [t + In(o - t, i), n];
    case ee.Top:
      return [t, n - In(n - r, i)];
    case ee.Bottom:
      return [t, n + In(r - n, i)];
  }
}
function Js({ sourceX: e, sourceY: t, sourcePosition: n = ee.Bottom, targetX: o, targetY: r, targetPosition: i = ee.Top, curvature: s = 0.25 }) {
  const [c, l] = hi({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: s
  }), [a, u] = hi({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: s
  }), [d, f, m, g] = Qs({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: r,
    sourceControlX: c,
    sourceControlY: l,
    targetControlX: a,
    targetControlY: u
  });
  return [
    `M${e},${t} C${c},${l} ${a},${u} ${o},${r}`,
    d,
    f,
    m,
    g
  ];
}
function ea({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, i = n < e ? n + r : n - r, s = Math.abs(o - t) / 2, c = o < t ? o + s : o - s;
  return [i, c, r, s];
}
function Mf({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: i = "basic" }) {
  if (i === "manual")
    return o;
  const s = r && n ? o + 1e3 : o, c = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return s + c;
}
function Af({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const i = so(Un(e), Un(t));
  i.x === i.x2 && (i.x2 += 1), i.y === i.y2 && (i.y2 += 1);
  const s = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return tn(s, ao(i)) > 0;
}
const ta = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, Df = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), jf = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Te.error006()), t;
  const o = n.getEdgeId || ta;
  let r;
  return Bs(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, Df(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, Pf = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: r, ...i } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", Te.error006()), n;
  if (!n.find((a) => a.id === e.id))
    return o.onError?.("007", Te.error007(r)), n;
  const c = o.getEdgeId || ta, l = {
    ...i,
    id: o.shouldReplaceId ? c(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((a) => a.id !== r).concat(l);
};
function na({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, i, s, c] = ea({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, i, s, c];
}
const pi = {
  [ee.Left]: { x: -1, y: 0 },
  [ee.Right]: { x: 1, y: 0 },
  [ee.Top]: { x: 0, y: -1 },
  [ee.Bottom]: { x: 0, y: 1 }
}, $f = ({ source: e, sourcePosition: t = ee.Bottom, target: n }) => t === ee.Left || t === ee.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, gi = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Tf({ source: e, sourcePosition: t = ee.Bottom, target: n, targetPosition: o = ee.Top, center: r, offset: i, stepPosition: s }) {
  const c = pi[t], l = pi[o], a = { x: e.x + c.x * i, y: e.y + c.y * i }, u = { x: n.x + l.x * i, y: n.y + l.y * i }, d = $f({
    source: a,
    sourcePosition: t,
    target: u
  }), f = d.x !== 0 ? "x" : "y", m = d[f];
  let g = [], x, w;
  const y = { x: 0, y: 0 }, S = { x: 0, y: 0 }, [, , p, v] = ea({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[f] * l[f] === -1) {
    f === "x" ? (x = r.x ?? a.x + (u.x - a.x) * s, w = r.y ?? (a.y + u.y) / 2) : (x = r.x ?? (a.x + u.x) / 2, w = r.y ?? a.y + (u.y - a.y) * s);
    const D = [
      { x, y: a.y },
      { x, y: u.y }
    ], j = [
      { x: a.x, y: w },
      { x: u.x, y: w }
    ];
    c[f] === m ? g = f === "x" ? D : j : g = f === "x" ? j : D;
  } else {
    const D = [{ x: a.x, y: u.y }], j = [{ x: u.x, y: a.y }];
    if (f === "x" ? g = c.x === m ? j : D : g = c.y === m ? D : j, t === o) {
      const b = Math.abs(e[f] - n[f]);
      if (b <= i) {
        const C = Math.min(i - 1, i - b);
        c[f] === m ? y[f] = (a[f] > e[f] ? -1 : 1) * C : S[f] = (u[f] > n[f] ? -1 : 1) * C;
      }
    }
    if (t !== o) {
      const b = f === "x" ? "y" : "x", C = c[f] === l[b], _ = a[b] > u[b], A = a[b] < u[b];
      (c[f] === 1 && (!C && _ || C && A) || c[f] !== 1 && (!C && A || C && _)) && (g = f === "x" ? D : j);
    }
    const H = { x: a.x + y.x, y: a.y + y.y }, M = { x: u.x + S.x, y: u.y + S.y }, R = Math.max(Math.abs(H.x - g[0].x), Math.abs(M.x - g[0].x)), V = Math.max(Math.abs(H.y - g[0].y), Math.abs(M.y - g[0].y));
    R >= V ? (x = (H.x + M.x) / 2, w = g[0].y) : (x = g[0].x, w = (H.y + M.y) / 2);
  }
  const E = { x: a.x + y.x, y: a.y + y.y }, N = { x: u.x + S.x, y: u.y + S.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...E.x !== g[0].x || E.y !== g[0].y ? [E] : [],
    ...g,
    ...N.x !== g[g.length - 1].x || N.y !== g[g.length - 1].y ? [N] : [],
    n
  ], x, w, p, v];
}
function Rf(e, t, n, o) {
  const r = Math.min(gi(e, t) / 2, gi(t, n) / 2, o), { x: i, y: s } = t;
  if (e.x === i && i === n.x || e.y === s && s === n.y)
    return `L${i} ${s}`;
  if (e.y === s) {
    const a = e.x < n.x ? -1 : 1, u = e.y < n.y ? 1 : -1;
    return `L ${i + r * a},${s}Q ${i},${s} ${i},${s + r * u}`;
  }
  const c = e.x < n.x ? 1 : -1, l = e.y < n.y ? -1 : 1;
  return `L ${i},${s + r * l}Q ${i},${s} ${i + r * c},${s}`;
}
function Kn({ sourceX: e, sourceY: t, sourcePosition: n = ee.Bottom, targetX: o, targetY: r, targetPosition: i = ee.Top, borderRadius: s = 5, centerX: c, centerY: l, offset: a = 20, stepPosition: u = 0.5 }) {
  const [d, f, m, g, x] = Tf({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: i,
    center: { x: c, y: l },
    offset: a,
    stepPosition: u
  });
  let w = `M${d[0].x} ${d[0].y}`;
  for (let y = 1; y < d.length - 1; y++)
    w += Rf(d[y - 1], d[y], d[y + 1], s);
  return w += `L${d[d.length - 1].x} ${d[d.length - 1].y}`, [w, f, m, g, x];
}
function mi(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function zf(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!mi(t) || !mi(n))
    return null;
  const o = t.internals.handleBounds || yi(t.handles), r = n.internals.handleBounds || yi(n.handles), i = xi(o?.source ?? [], e.sourceHandle), s = xi(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === _t.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!i || !s)
    return e.onError?.("008", Te.error008(i ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = i?.position || ee.Bottom, l = s?.position || ee.Top, a = gt(t, i, c), u = gt(n, s, l);
  return {
    sourceX: a.x,
    sourceY: a.y,
    targetX: u.x,
    targetY: u.y,
    sourcePosition: c,
    targetPosition: l
  };
}
function yi(e) {
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
function gt(e, t, n = ee.Left, o = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, i = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: s, height: c } = t ?? et(e);
  if (o)
    return { x: r + s / 2, y: i + c / 2 };
  switch (t?.position ?? n) {
    case ee.Top:
      return { x: r + s / 2, y: i };
    case ee.Right:
      return { x: r + s, y: i + c / 2 };
    case ee.Bottom:
      return { x: r + s / 2, y: i + c };
    case ee.Left:
      return { x: r, y: i + c / 2 };
  }
}
function xi(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function cr(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function Lf(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const i = /* @__PURE__ */ new Set();
  return e.reduce((s, c) => ([c.markerStart || o, c.markerEnd || r].forEach((l) => {
    if (l && typeof l == "object") {
      const a = cr(l, t);
      i.has(a) || (s.push({ id: a, color: l.color || n, ...l }), i.add(a));
    }
  }), s), []).sort((s, c) => s.id.localeCompare(c.id));
}
const oa = 1e3, Hf = 10, Ir = {
  nodeOrigin: [0, 0],
  nodeExtent: Jt,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Vf = {
  ...Ir,
  checkEquality: !0
};
function Mr(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function Of(e, t, n) {
  const o = Mr(Ir, n);
  for (const r of e.values())
    if (r.parentId)
      Dr(r, e, t, o);
    else {
      const i = un(r, o.nodeOrigin), s = pt(r.extent) ? r.extent : o.nodeExtent, c = ht(i, s, et(r));
      r.internals.positionAbsolute = c;
    }
}
function Bf(e, t) {
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
function Ar(e) {
  return e === "manual";
}
function lr(e, t, n, o = {}) {
  const r = Mr(Vf, o), i = { i: 0 }, s = new Map(t), c = r?.elevateNodesOnSelect && !Ar(r.zIndexMode) ? oa : 0;
  let l = e.length > 0, a = !1;
  t.clear(), n.clear();
  for (const u of e) {
    let d = s.get(u.id);
    if (r.checkEquality && u === d?.internals.userNode)
      t.set(u.id, d);
    else {
      const f = un(u, r.nodeOrigin), m = pt(u.extent) ? u.extent : r.nodeExtent, g = ht(f, m, et(u));
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
          handleBounds: Bf(u, d),
          z: ra(u, c, r.zIndexMode),
          userNode: u
        }
      }, t.set(u.id, d);
    }
    (d.measured === void 0 || d.measured.width === void 0 || d.measured.height === void 0) && !d.hidden && (l = !1), u.parentId && Dr(d, t, n, o, i), a ||= u.selected ?? !1;
  }
  return { nodesInitialized: l, hasSelectedNodes: a };
}
function Ff(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function Dr(e, t, n, o, r) {
  const { elevateNodesOnSelect: i, nodeOrigin: s, nodeExtent: c, zIndexMode: l } = Mr(Ir, o), a = e.parentId, u = t.get(a);
  if (!u) {
    console.warn(`Parent node ${a} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Ff(e, n), r && !u.parentId && u.internals.rootParentIndex === void 0 && l === "auto" && (u.internals.rootParentIndex = ++r.i, u.internals.z = u.internals.z + r.i * Hf), r && u.internals.rootParentIndex !== void 0 && (r.i = u.internals.rootParentIndex);
  const d = i && !Ar(l) ? oa : 0, { x: f, y: m, z: g } = Wf(e, u, s, c, d, l), { positionAbsolute: x } = e.internals, w = f !== x.x || m !== x.y;
  (w || g !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: w ? { x: f, y: m } : x,
      z: g
    }
  });
}
function ra(e, t, n) {
  const o = Fe(e.zIndex) ? e.zIndex : 0;
  return Ar(n) ? o : o + (e.selected ? t : 0);
}
function Wf(e, t, n, o, r, i) {
  const { x: s, y: c } = t.internals.positionAbsolute, l = et(e), a = un(e, n), u = pt(e.extent) ? ht(a, e.extent, l) : a;
  let d = ht({ x: s + u.x, y: c + u.y }, o, l);
  e.extent === "parent" && (d = Ws(d, l, t));
  const f = ra(e, r, i), m = t.internals.z ?? 0;
  return {
    x: d.x,
    y: d.y,
    z: m >= f ? m + 1 : f
  };
}
function jr(e, t, n, o = [0, 0]) {
  const r = [], i = /* @__PURE__ */ new Map();
  for (const s of e) {
    const c = t.get(s.parentId);
    if (!c)
      continue;
    const l = i.get(s.parentId)?.expandedRect ?? kt(c), a = Ys(l, s.rect);
    i.set(s.parentId, { expandedRect: a, parent: c });
  }
  return i.size > 0 && i.forEach(({ expandedRect: s, parent: c }, l) => {
    const a = c.internals.positionAbsolute, u = et(c), d = c.origin ?? o, f = s.x < a.x ? Math.round(Math.abs(a.x - s.x)) : 0, m = s.y < a.y ? Math.round(Math.abs(a.y - s.y)) : 0, g = Math.max(u.width, Math.round(s.width)), x = Math.max(u.height, Math.round(s.height)), w = (g - u.width) * d[0], y = (x - u.height) * d[1];
    (f > 0 || m > 0 || w || y) && (r.push({
      id: l,
      type: "position",
      position: {
        x: c.position.x - f + w,
        y: c.position.y - m + y
      }
    }), n.get(l)?.forEach((S) => {
      e.some((p) => p.id === S.id) || r.push({
        id: S.id,
        type: "position",
        position: {
          x: S.position.x + f,
          y: S.position.y + m
        }
      });
    })), (u.width < s.width || u.height < s.height || f || m) && r.push({
      id: l,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: g + (f ? d[0] * f - w : 0),
        height: x + (m ? d[1] * m - y : 0)
      }
    });
  }), r;
}
function Yf(e, t, n, o, r, i, s) {
  const c = o?.querySelector(".xyflow__viewport");
  let l = !1;
  if (!c)
    return { changes: [], updatedInternals: l };
  const a = [], u = window.getComputedStyle(c), { m22: d } = new window.DOMMatrixReadOnly(u.transform), f = [];
  for (const m of e.values()) {
    const g = t.get(m.id);
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
    const x = kr(m.nodeElement), w = g.measured.width !== x.width || g.measured.height !== x.height;
    if (!!(x.width && x.height && (w || !g.internals.handleBounds || m.force))) {
      const S = m.nodeElement.getBoundingClientRect(), p = pt(g.extent) ? g.extent : i;
      let { positionAbsolute: v } = g.internals;
      g.parentId && g.extent === "parent" ? v = Ws(v, x, t.get(g.parentId)) : p && (v = ht(v, p, x));
      const E = {
        ...g,
        measured: x,
        internals: {
          ...g.internals,
          positionAbsolute: v,
          handleBounds: {
            source: fi("source", m.nodeElement, S, d, g.id),
            target: fi("target", m.nodeElement, S, d, g.id)
          }
        }
      };
      t.set(g.id, E), g.parentId && Dr(E, t, n, { nodeOrigin: r, zIndexMode: s }), l = !0, w && (a.push({
        id: g.id,
        type: "dimensions",
        dimensions: x
      }), g.expandParent && g.parentId && f.push({
        id: g.id,
        parentId: g.parentId,
        rect: kt(E, r)
      }));
    }
  }
  if (f.length > 0) {
    const m = jr(f, t, n, r);
    a.push(...m);
  }
  return { changes: a, updatedInternals: l };
}
async function Xf({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: i }) {
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
function wi(e, t, n, o, r, i) {
  let s = r;
  const c = o.get(s) || /* @__PURE__ */ new Map();
  o.set(s, c.set(n, t)), s = `${r}-${e}`;
  const l = o.get(s) || /* @__PURE__ */ new Map();
  if (o.set(s, l.set(n, t)), i) {
    s = `${r}-${e}-${i}`;
    const a = o.get(s) || /* @__PURE__ */ new Map();
    o.set(s, a.set(n, t));
  }
}
function ia(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: i, sourceHandle: s = null, targetHandle: c = null } = o, l = { edgeId: o.id, source: r, target: i, sourceHandle: s, targetHandle: c }, a = `${r}-${s}--${i}-${c}`, u = `${i}-${c}--${r}-${s}`;
    wi("source", l, u, e, r, s), wi("target", l, a, e, i, c), t.set(o.id, o);
  }
}
function sa(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : sa(n, t) : !1;
}
function vi(e, t, n) {
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
function qf(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [i, s] of e)
    if ((s.selected || s.id === o) && (!s.parentId || !sa(s, e)) && (s.draggable || t && typeof s.draggable > "u")) {
      const c = e.get(i);
      c && r.set(i, {
        id: i,
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
function Vo({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
  const r = [];
  for (const [s, c] of t) {
    const l = n.get(s)?.internals.userNode;
    l && r.push({
      ...l,
      position: c.position,
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
function Zf({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const i = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, s = fn(i, t);
  return {
    x: s.x - i.x,
    y: s.y - i.y
  };
}
function Uf({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let i = { x: null, y: null }, s = 0, c = /* @__PURE__ */ new Map(), l = !1, a = { x: 0, y: 0 }, u = null, d = !1, f = null, m = !1, g = !1, x = null;
  function w({ noDragClassName: S, handleSelector: p, domNode: v, isSelectable: E, nodeId: N, nodeClickDistance: I = 0 }) {
    f = Pe(v);
    function D({ x: R, y: V }) {
      const { nodeLookup: b, nodeExtent: C, snapGrid: _, snapToGrid: A, nodeOrigin: P, onNodeDrag: $, onSelectionDrag: B, onError: O, updateNodePositions: F } = t();
      i = { x: R, y: V };
      let Q = !1;
      const Z = c.size > 1, U = Z && C ? ar(dn(c)) : null, se = Z && A ? Zf({
        dragItems: c,
        snapGrid: _,
        x: R,
        y: V
      }) : null;
      for (const [G, z] of c) {
        if (!b.has(G))
          continue;
        let q = { x: R - z.distance.x, y: V - z.distance.y };
        A && (q = se ? {
          x: Math.round(q.x + se.x),
          y: Math.round(q.y + se.y)
        } : fn(q, _));
        let re = null;
        if (Z && C && !z.extent && U) {
          const { positionAbsolute: te } = z.internals, ie = te.x - U.x + C[0][0], L = te.x + z.measured.width - U.x2 + C[1][0], Y = te.y - U.y + C[0][1], ue = te.y + z.measured.height - U.y2 + C[1][1];
          re = [
            [ie, Y],
            [L, ue]
          ];
        }
        const { position: oe, positionAbsolute: K } = Fs({
          nodeId: G,
          nextPosition: q,
          nodeLookup: b,
          nodeExtent: re || C,
          nodeOrigin: P,
          onError: O
        });
        Q = Q || z.position.x !== oe.x || z.position.y !== oe.y, z.position = oe, z.internals.positionAbsolute = K;
      }
      if (g = g || Q, !!Q && (F(c, !0), x && (o || $ || !N && B))) {
        const [G, z] = Vo({
          nodeId: N,
          dragItems: c,
          nodeLookup: b
        });
        o?.(x, c, G, z), $?.(x, G, z), N || B?.(x, z);
      }
    }
    async function j() {
      if (!u)
        return;
      const { transform: R, panBy: V, autoPanSpeed: b, autoPanOnNodeDrag: C } = t();
      if (!C) {
        l = !1, cancelAnimationFrame(s);
        return;
      }
      const [_, A] = _r(a, u, b);
      (_ !== 0 || A !== 0) && (i.x = (i.x ?? 0) - _ / R[2], i.y = (i.y ?? 0) - A / R[2], await V({ x: _, y: A }) && D(i)), s = requestAnimationFrame(j);
    }
    function H(R) {
      const { nodeLookup: V, multiSelectionActive: b, nodesDraggable: C, transform: _, snapGrid: A, snapToGrid: P, selectNodesOnDrag: $, onNodeDragStart: B, onSelectionDragStart: O, unselectNodesAndEdges: F } = t();
      d = !0, (!$ || !E) && !b && N && (V.get(N)?.selected || F()), E && $ && N && e?.(N);
      const Q = Zt(R.sourceEvent, { transform: _, snapGrid: A, snapToGrid: P, containerBounds: u });
      if (i = Q, c = qf(V, C, Q, N), c.size > 0 && (n || B || !N && O)) {
        const [Z, U] = Vo({
          nodeId: N,
          dragItems: c,
          nodeLookup: V
        });
        n?.(R.sourceEvent, c, Z, U), B?.(R.sourceEvent, Z, U), N || O?.(R.sourceEvent, U);
      }
    }
    const M = Ss().clickDistance(I).on("start", (R) => {
      const { domNode: V, nodeDragThreshold: b, transform: C, snapGrid: _, snapToGrid: A } = t();
      u = V?.getBoundingClientRect() || null, m = !1, g = !1, x = R.sourceEvent, b === 0 && H(R), i = Zt(R.sourceEvent, { transform: C, snapGrid: _, snapToGrid: A, containerBounds: u }), a = We(R.sourceEvent, u);
    }).on("drag", (R) => {
      const { autoPanOnNodeDrag: V, transform: b, snapGrid: C, snapToGrid: _, nodeDragThreshold: A, nodeLookup: P } = t(), $ = Zt(R.sourceEvent, { transform: b, snapGrid: C, snapToGrid: _, containerBounds: u });
      if (x = R.sourceEvent, (R.sourceEvent.type === "touchmove" && R.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      N && !P.has(N)) && (m = !0), !m) {
        if (!l && V && d && (l = !0, j()), !d) {
          const B = We(R.sourceEvent, u), O = B.x - a.x, F = B.y - a.y;
          Math.sqrt(O * O + F * F) > A && H(R);
        }
        (i.x !== $.xSnapped || i.y !== $.ySnapped) && c && d && (a = We(R.sourceEvent, u), D($));
      }
    }).on("end", (R) => {
      if (!d || m) {
        m && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (l = !1, d = !1, cancelAnimationFrame(s), c.size > 0) {
        const { nodeLookup: V, updateNodePositions: b, onNodeDragStop: C, onSelectionDragStop: _ } = t();
        if (g && (b(c, !1), g = !1), r || C || !N && _) {
          const [A, P] = Vo({
            nodeId: N,
            dragItems: c,
            nodeLookup: V,
            dragging: !1
          });
          r?.(R.sourceEvent, c, A, P), C?.(R.sourceEvent, A, P), N || _?.(R.sourceEvent, P);
        }
      }
    }).filter((R) => {
      const V = R.target;
      return !R.button && (!S || !vi(V, `.${S}`, v)) && (!p || vi(V, p, v));
    });
    f.call(M);
  }
  function y() {
    f?.on(".drag", null);
  }
  return {
    update: w,
    destroy: y
  };
}
function Kf(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const i of t.values())
    tn(r, kt(i)) > 0 && o.push(i);
  return o;
}
const Gf = 250;
function Qf(e, t, n, o) {
  let r = [], i = 1 / 0;
  const s = Kf(e, n, t + Gf);
  for (const c of s) {
    const l = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const a of l) {
      if (o.nodeId === a.nodeId && o.type === a.type && o.id === a.id)
        continue;
      const { x: u, y: d } = gt(c, a, a.position, !0), f = Math.sqrt(Math.pow(u - e.x, 2) + Math.pow(d - e.y, 2));
      f > t || (f < i ? (r = [{ ...a, x: u, y: d }], i = f) : f === i && r.push({ ...a, x: u, y: d }));
    }
  }
  if (!r.length)
    return null;
  if (r.length > 1) {
    const c = o.type === "source" ? "target" : "source";
    return r.find((l) => l.type === c) ?? r[0];
  }
  return r[0];
}
function aa(e, t, n, o, r, i = !1) {
  const s = o.get(e);
  if (!s)
    return null;
  const c = r === "strict" ? s.internals.handleBounds?.[t] : [...s.internals.handleBounds?.source ?? [], ...s.internals.handleBounds?.target ?? []], l = (n ? c?.find((a) => a.id === n) : c?.[0]) ?? null;
  return l && i ? { ...l, ...gt(s, l, l.position, !0) } : l;
}
function ca(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Jf(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const la = () => !0;
function eh(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: i, isTarget: s, domNode: c, nodeLookup: l, lib: a, autoPanOnConnect: u, flowId: d, panBy: f, cancelConnection: m, onConnectStart: g, onConnect: x, onConnectEnd: w, isValidConnection: y = la, onReconnectEnd: S, updateConnection: p, getTransform: v, getFromHandle: E, autoPanSpeed: N, dragThreshold: I = 1, handleDomNode: D }) {
  const j = Us(e.target);
  let H = 0, M;
  const { x: R, y: V } = We(e), b = ca(i, D), C = c?.getBoundingClientRect();
  let _ = !1;
  if (!C || !b)
    return;
  const A = aa(r, b, o, l, t);
  if (!A)
    return;
  let P = We(e, C), $ = !1, B = null, O = !1, F = null;
  function Q() {
    if (!u || !C)
      return;
    const [oe, K] = _r(P, C, N);
    f({ x: oe, y: K }), H = requestAnimationFrame(Q);
  }
  const Z = {
    ...A,
    nodeId: r,
    type: b,
    position: A.position
  }, U = l.get(r);
  let G = {
    inProgress: !0,
    isValid: null,
    from: gt(U, Z, ee.Left, !0),
    fromHandle: Z,
    fromPosition: Z.position,
    fromNode: U,
    to: P,
    toHandle: null,
    toPosition: ci[Z.position],
    toNode: null,
    pointer: P
  };
  function z() {
    _ = !0, p(G), g?.(e, { nodeId: r, handleId: o, handleType: b });
  }
  I === 0 && z();
  function q(oe) {
    if (!_) {
      const { x: ue, y: xe } = We(oe), he = ue - R, ze = xe - V;
      if (!(he * he + ze * ze > I * I))
        return;
      z();
    }
    if (!E() || !Z) {
      re(oe);
      return;
    }
    const K = v();
    P = We(oe, C), M = Qf($t(P, K, !1, [1, 1]), n, l, Z), $ || (Q(), $ = !0);
    const te = ua(oe, {
      handle: M,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: s ? "target" : "source",
      isValidConnection: y,
      doc: j,
      lib: a,
      flowId: d,
      nodeLookup: l
    });
    F = te.handleDomNode, B = te.connection, O = Jf(!!M, te.isValid);
    const ie = l.get(r), L = ie ? gt(ie, Z, ee.Left, !0) : G.from, Y = {
      ...G,
      from: L,
      isValid: O,
      to: te.toHandle && O ? It({ x: te.toHandle.x, y: te.toHandle.y }, K) : P,
      toHandle: te.toHandle,
      toPosition: O && te.toHandle ? te.toHandle.position : ci[Z.position],
      toNode: te.toHandle ? l.get(te.toHandle.nodeId) : null,
      pointer: P
    };
    p(Y), G = Y;
  }
  function re(oe) {
    if (!("touches" in oe && oe.touches.length > 0)) {
      if (_) {
        (M || F) && B && O && x?.(B);
        const { inProgress: K, ...te } = G, ie = {
          ...te,
          toPosition: G.toHandle ? G.toPosition : null
        };
        w?.(oe, ie), i && S?.(oe, ie);
      }
      m(), cancelAnimationFrame(H), $ = !1, O = !1, B = null, F = null, j.removeEventListener("mousemove", q), j.removeEventListener("mouseup", re), j.removeEventListener("touchmove", q), j.removeEventListener("touchend", re);
    }
  }
  j.addEventListener("mousemove", q), j.addEventListener("mouseup", re), j.addEventListener("touchmove", q), j.addEventListener("touchend", re);
}
function ua(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: i, doc: s, lib: c, flowId: l, isValidConnection: a = la, nodeLookup: u }) {
  const d = i === "target", f = t ? s.querySelector(`.${c}-flow__handle[data-id="${l}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: m, y: g } = We(e), x = s.elementFromPoint(m, g), w = x?.classList.contains(`${c}-flow__handle`) ? x : f, y = {
    handleDomNode: w,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (w) {
    const S = ca(void 0, w), p = w.getAttribute("data-nodeid"), v = w.getAttribute("data-handleid"), E = w.classList.contains("connectable"), N = w.classList.contains("connectableend");
    if (!p || !S)
      return y;
    const I = {
      source: d ? p : o,
      sourceHandle: d ? v : r,
      target: d ? o : p,
      targetHandle: d ? r : v
    };
    y.connection = I;
    const j = E && N && (n === _t.Strict ? d && S === "source" || !d && S === "target" : p !== o || v !== r);
    y.isValid = j && a(I), y.toHandle = aa(p, S, v, u, n, !0);
  }
  return y;
}
const ur = {
  onPointerDown: eh,
  isValid: ua
};
function th({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = Pe(e);
  function i({ translateExtent: c, width: l, height: a, zoomStep: u = 1, pannable: d = !0, zoomable: f = !0, inversePan: m = !1 }) {
    const g = (p) => {
      if (p.sourceEvent.type !== "wheel" || !t)
        return;
      const v = n(), E = p.sourceEvent.ctrlKey && nn() ? 10 : 1, N = -p.sourceEvent.deltaY * (p.sourceEvent.deltaMode === 1 ? 0.05 : p.sourceEvent.deltaMode ? 1 : 2e-3) * u, I = v[2] * Math.pow(2, N * E);
      t.scaleTo(I);
    };
    let x = [0, 0];
    const w = (p) => {
      (p.sourceEvent.type === "mousedown" || p.sourceEvent.type === "touchstart") && (x = [
        p.sourceEvent.clientX ?? p.sourceEvent.touches[0].clientX,
        p.sourceEvent.clientY ?? p.sourceEvent.touches[0].clientY
      ]);
    }, y = (p) => {
      const v = n();
      if (p.sourceEvent.type !== "mousemove" && p.sourceEvent.type !== "touchmove" || !t)
        return;
      const E = [
        p.sourceEvent.clientX ?? p.sourceEvent.touches[0].clientX,
        p.sourceEvent.clientY ?? p.sourceEvent.touches[0].clientY
      ], N = [E[0] - x[0], E[1] - x[1]];
      x = E;
      const I = o() * Math.max(v[2], Math.log(v[2])) * (m ? -1 : 1), D = {
        x: v[0] - N[0] * I,
        y: v[1] - N[1] * I
      }, j = [
        [0, 0],
        [l, a]
      ];
      t.setViewportConstrained({
        x: D.x,
        y: D.y,
        zoom: v[2]
      }, j, c);
    }, S = zs().on("start", w).on("zoom", d ? y : null).on("zoom.wheel", f ? g : null);
    r.call(S, {});
  }
  function s() {
    r.on("zoom", null);
  }
  return {
    update: i,
    destroy: s,
    pointer: Oe
  };
}
const co = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), Oo = ({ x: e, y: t, zoom: n }) => io.translate(e, t).scale(n), wt = (e, t) => e.target.closest(`.${t}`), da = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), nh = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, Bo = (e, t = 0, n = nh, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, fa = (e) => {
  const t = e.ctrlKey && nn() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function oh({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: i, zoomOnPinch: s, onPanZoomStart: c, onPanZoom: l, onPanZoomEnd: a }) {
  return (u) => {
    if (wt(u, t))
      return u.ctrlKey && u.preventDefault(), !1;
    u.preventDefault(), u.stopImmediatePropagation();
    const d = n.property("__zoom").k || 1;
    if (u.ctrlKey && s) {
      const w = Oe(u), y = fa(u), S = d * Math.pow(2, y);
      o.scaleTo(n, S, w, u);
      return;
    }
    const f = u.deltaMode === 1 ? 20 : 1;
    let m = r === ut.Vertical ? 0 : u.deltaX * f, g = r === ut.Horizontal ? 0 : u.deltaY * f;
    !nn() && u.shiftKey && r !== ut.Vertical && (m = u.deltaY * f, g = 0), o.translateBy(
      n,
      -(m / d) * i,
      -(g / d) * i,
      // @ts-ignore
      { internal: !0 }
    );
    const x = co(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (l?.(u, x), e.panScrollTimeout = setTimeout(() => {
      a?.(u, x), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(u, x));
  };
}
function rh({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const i = o.type === "wheel", s = !t && i && !o.ctrlKey, c = wt(o, e);
    if (o.ctrlKey && i && c && o.preventDefault(), s || c)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function ih({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = co(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function sh({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (i) => {
    e.usedRightMouseButton = !!(n && da(t, e.mouseButton ?? 0)), i.sourceEvent?.sync || o([i.transform.x, i.transform.y, i.transform.k]), r && !i.sourceEvent?.internal && r?.(i.sourceEvent, co(i.transform));
  };
}
function ah({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: i }) {
  return (s) => {
    if (!s.sourceEvent?.internal && (e.isZoomingOrPanning = !1, i && da(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && s.sourceEvent && i(s.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
      const c = co(s.transform);
      e.prevViewport = c, clearTimeout(e.timerId), e.timerId = setTimeout(
        () => {
          r?.(s.sourceEvent, c);
        },
        // we need a setTimeout for panOnScroll to suppress multiple end events fired during scroll
        n ? 150 : 0
      );
    }
  };
}
function ch({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: i, userSelectionActive: s, noWheelClassName: c, noPanClassName: l, lib: a, connectionInProgress: u }) {
  return (d) => {
    const f = e || t, m = n && d.ctrlKey, g = d.type === "wheel";
    if (d.button === 1 && d.type === "mousedown" && (wt(d, `${a}-flow__node`) || wt(d, `${a}-flow__edge`)))
      return !0;
    if (!o && !f && !r && !i && !n || s || u && !g || wt(d, c) && g || wt(d, l) && (!g || r && g && !e) || !n && d.ctrlKey && g)
      return !1;
    if (!n && d.type === "touchstart" && d.touches?.length > 1)
      return d.preventDefault(), !1;
    if (!f && !r && !m && g || !o && (d.type === "mousedown" || d.type === "touchstart") || Array.isArray(o) && !o.includes(d.button) && d.type === "mousedown")
      return !1;
    const x = Array.isArray(o) && o.includes(d.button) || !d.button || d.button <= 1;
    return (!d.ctrlKey || g) && x;
  };
}
function lh({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: i, onPanZoomStart: s, onPanZoomEnd: c, onDraggingChange: l }) {
  const a = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, u = e.getBoundingClientRect(), d = zs().scaleExtent([t, n]).translateExtent(o), f = Pe(e).call(d);
  S({
    x: r.x,
    y: r.y,
    zoom: Ct(r.zoom, t, n)
  }, [
    [0, 0],
    [u.width, u.height]
  ], o);
  const m = f.on("wheel.zoom"), g = f.on("dblclick.zoom");
  d.wheelDelta(fa);
  async function x(M, R) {
    return f ? new Promise((V) => {
      d?.interpolate(R?.interpolate === "linear" ? qt : $n).transform(Bo(f, R?.duration, R?.ease, () => V(!0)), M);
    }) : !1;
  }
  function w({ noWheelClassName: M, noPanClassName: R, onPaneContextMenu: V, userSelectionActive: b, panOnScroll: C, panOnDrag: _, panOnScrollMode: A, panOnScrollSpeed: P, preventScrolling: $, zoomOnPinch: B, zoomOnScroll: O, zoomOnDoubleClick: F, zoomActivationKeyPressed: Q, lib: Z, onTransformChange: U, connectionInProgress: se, paneClickDistance: G, selectionOnDrag: z }) {
    b && !a.isZoomingOrPanning && y();
    const q = C && !Q && !b;
    d.clickDistance(z ? 1 / 0 : !Fe(G) || G < 0 ? 0 : G);
    const re = q ? oh({
      zoomPanValues: a,
      noWheelClassName: M,
      d3Selection: f,
      d3Zoom: d,
      panOnScrollMode: A,
      panOnScrollSpeed: P,
      zoomOnPinch: B,
      onPanZoomStart: s,
      onPanZoom: i,
      onPanZoomEnd: c
    }) : rh({
      noWheelClassName: M,
      preventScrolling: $,
      d3ZoomHandler: m
    });
    f.on("wheel.zoom", re, { passive: !1 });
    const oe = ih({
      zoomPanValues: a,
      onDraggingChange: l,
      onPanZoomStart: s
    });
    d.on("start", oe);
    const K = sh({
      zoomPanValues: a,
      panOnDrag: _,
      onPaneContextMenu: !!V,
      onPanZoom: i,
      onTransformChange: U
    });
    d.on("zoom", K);
    const te = ah({
      zoomPanValues: a,
      panOnDrag: _,
      panOnScroll: C,
      onPaneContextMenu: V,
      onPanZoomEnd: c,
      onDraggingChange: l
    });
    d.on("end", te);
    const ie = ch({
      zoomActivationKeyPressed: Q,
      panOnDrag: _,
      zoomOnScroll: O,
      panOnScroll: C,
      zoomOnDoubleClick: F,
      zoomOnPinch: B,
      userSelectionActive: b,
      noPanClassName: R,
      noWheelClassName: M,
      lib: Z,
      connectionInProgress: se
    });
    d.filter(ie), F ? f.on("dblclick.zoom", g) : f.on("dblclick.zoom", null);
  }
  function y() {
    d.on("zoom", null);
  }
  async function S(M, R, V) {
    const b = Oo(M), C = d?.constrain()(b, R, V);
    return C && await x(C), C;
  }
  async function p(M, R) {
    const V = Oo(M);
    return await x(V, R), V;
  }
  function v(M) {
    if (f) {
      const R = Oo(M), V = f.property("__zoom");
      (V.k !== M.zoom || V.x !== M.x || V.y !== M.y) && d?.transform(f, R, null, { sync: !0 });
    }
  }
  function E() {
    const M = f ? Rs(f.node()) : { x: 0, y: 0, k: 1 };
    return { x: M.x, y: M.y, zoom: M.k };
  }
  async function N(M, R) {
    return f ? new Promise((V) => {
      d?.interpolate(R?.interpolate === "linear" ? qt : $n).scaleTo(Bo(f, R?.duration, R?.ease, () => V(!0)), M);
    }) : !1;
  }
  async function I(M, R) {
    return f ? new Promise((V) => {
      d?.interpolate(R?.interpolate === "linear" ? qt : $n).scaleBy(Bo(f, R?.duration, R?.ease, () => V(!0)), M);
    }) : !1;
  }
  function D(M) {
    d?.scaleExtent(M);
  }
  function j(M) {
    d?.translateExtent(M);
  }
  function H(M) {
    const R = !Fe(M) || M < 0 ? 0 : M;
    d?.clickDistance(R);
  }
  return {
    update: w,
    destroy: y,
    setViewport: p,
    setViewportConstrained: S,
    getViewport: E,
    scaleTo: N,
    scaleBy: I,
    setScaleExtent: D,
    setTranslateExtent: j,
    syncViewport: v,
    setClickDistance: H
  };
}
var Mt;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(Mt || (Mt = {}));
function uh({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: i }) {
  const s = e - t, c = n - o, l = [s > 0 ? 1 : s < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return s && r && (l[0] = l[0] * -1), c && i && (l[1] = l[1] * -1), l;
}
function bi(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: r
  };
}
function tt(e, t) {
  return Math.max(0, t - e);
}
function nt(e, t) {
  return Math.max(0, e - t);
}
function Mn(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function Si(e, t) {
  return e ? !t : t;
}
function dh(e, t, n, o, r, i, s, c) {
  let { affectsX: l, affectsY: a } = t;
  const { isHorizontal: u, isVertical: d } = t, f = u && d, { xSnapped: m, ySnapped: g } = n, { minWidth: x, maxWidth: w, minHeight: y, maxHeight: S } = o, { x: p, y: v, width: E, height: N, aspectRatio: I } = e;
  let D = Math.floor(u ? m - e.pointerX : 0), j = Math.floor(d ? g - e.pointerY : 0);
  const H = E + (l ? -D : D), M = N + (a ? -j : j), R = -i[0] * E, V = -i[1] * N;
  let b = Mn(H, x, w), C = Mn(M, y, S);
  if (s) {
    let P = 0, $ = 0;
    l && D < 0 ? P = tt(p + D + R, s[0][0]) : !l && D > 0 && (P = nt(p + H + R, s[1][0])), a && j < 0 ? $ = tt(v + j + V, s[0][1]) : !a && j > 0 && ($ = nt(v + M + V, s[1][1])), b = Math.max(b, P), C = Math.max(C, $);
  }
  if (c) {
    let P = 0, $ = 0;
    l && D > 0 ? P = nt(p + D, c[0][0]) : !l && D < 0 && (P = tt(p + H, c[1][0])), a && j > 0 ? $ = nt(v + j, c[0][1]) : !a && j < 0 && ($ = tt(v + M, c[1][1])), b = Math.max(b, P), C = Math.max(C, $);
  }
  if (r) {
    if (u) {
      const P = Mn(H / I, y, S) * I;
      if (b = Math.max(b, P), s) {
        let $ = 0;
        !l && !a || l && !a && f ? $ = nt(v + V + H / I, s[1][1]) * I : $ = tt(v + V + (l ? D : -D) / I, s[0][1]) * I, b = Math.max(b, $);
      }
      if (c) {
        let $ = 0;
        !l && !a || l && !a && f ? $ = tt(v + H / I, c[1][1]) * I : $ = nt(v + (l ? D : -D) / I, c[0][1]) * I, b = Math.max(b, $);
      }
    }
    if (d) {
      const P = Mn(M * I, x, w) / I;
      if (C = Math.max(C, P), s) {
        let $ = 0;
        !l && !a || a && !l && f ? $ = nt(p + M * I + R, s[1][0]) / I : $ = tt(p + (a ? j : -j) * I + R, s[0][0]) / I, C = Math.max(C, $);
      }
      if (c) {
        let $ = 0;
        !l && !a || a && !l && f ? $ = tt(p + M * I, c[1][0]) / I : $ = nt(p + (a ? j : -j) * I, c[0][0]) / I, C = Math.max(C, $);
      }
    }
  }
  j = j + (j < 0 ? C : -C), D = D + (D < 0 ? b : -b), r && (f ? H > M * I ? j = (Si(l, a) ? -D : D) / I : D = (Si(l, a) ? -j : j) * I : u ? (j = D / I, a = l) : (D = j * I, l = a));
  const _ = l ? p + D : p, A = a ? v + j : v;
  return {
    width: E + (l ? -D : D),
    height: N + (a ? -j : j),
    x: i[0] * D * (l ? -1 : 1) + _,
    y: i[1] * j * (a ? -1 : 1) + A
  };
}
const ha = { width: 0, height: 0, x: 0, y: 0 }, fh = {
  ...ha,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function hh(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, i = e.measured.width ?? 0, s = e.measured.height ?? 0, c = n[0] * i, l = n[1] * s;
  return [
    [o - c, r - l],
    [o + i - c, r + s - l]
  ];
}
function ph({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const i = Pe(e);
  let s = {
    controlDirection: bi("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: a, boundaries: u, keepAspectRatio: d, resizeDirection: f, onResizeStart: m, onResize: g, onResizeEnd: x, shouldResize: w }) {
    let y = { ...ha }, S = { ...fh };
    s = {
      boundaries: u,
      resizeDirection: f,
      keepAspectRatio: d,
      controlDirection: bi(a)
    };
    let p, v = null, E = [], N, I, D, j = !1;
    const H = Ss().on("start", (M) => {
      const { nodeLookup: R, transform: V, snapGrid: b, snapToGrid: C, nodeOrigin: _, paneDomNode: A } = n();
      if (p = R.get(t), !p)
        return;
      v = A?.getBoundingClientRect() ?? null;
      const { xSnapped: P, ySnapped: $ } = Zt(M.sourceEvent, {
        transform: V,
        snapGrid: b,
        snapToGrid: C,
        containerBounds: v
      });
      y = {
        width: p.measured.width ?? 0,
        height: p.measured.height ?? 0,
        x: p.position.x ?? 0,
        y: p.position.y ?? 0
      }, S = {
        ...y,
        pointerX: P,
        pointerY: $,
        aspectRatio: y.width / y.height
      }, N = void 0, I = pt(p.extent) ? p.extent : void 0, p.parentId && (p.extent === "parent" || p.expandParent) && (N = R.get(p.parentId)), N && p.extent === "parent" && (I = [
        [0, 0],
        [N.measured.width, N.measured.height]
      ]), E = [], D = void 0;
      for (const [B, O] of R)
        if (O.parentId === t && (E.push({
          id: B,
          position: { ...O.position },
          extent: O.extent
        }), O.extent === "parent" || O.expandParent)) {
          const F = hh(O, p, O.origin ?? _);
          D ? D = [
            [Math.min(F[0][0], D[0][0]), Math.min(F[0][1], D[0][1])],
            [Math.max(F[1][0], D[1][0]), Math.max(F[1][1], D[1][1])]
          ] : D = F;
        }
      m?.(M, { ...y });
    }).on("drag", (M) => {
      const { transform: R, snapGrid: V, snapToGrid: b, nodeOrigin: C } = n(), _ = Zt(M.sourceEvent, {
        transform: R,
        snapGrid: V,
        snapToGrid: b,
        containerBounds: v
      }), A = [];
      if (!p)
        return;
      const { x: P, y: $, width: B, height: O } = y, F = {}, Q = p.origin ?? C, { width: Z, height: U, x: se, y: G } = dh(S, s.controlDirection, _, s.boundaries, s.keepAspectRatio, Q, I, D), z = Z !== B, q = U !== O, re = se !== P && z, oe = G !== $ && q;
      if (!re && !oe && !z && !q)
        return;
      if ((re || oe || Q[0] === 1 || Q[1] === 1) && (F.x = re ? se : y.x, F.y = oe ? G : y.y, y.x = F.x, y.y = F.y, E.length > 0)) {
        const L = se - P, Y = G - $;
        for (const ue of E)
          ue.position = {
            x: ue.position.x - L + Q[0] * (Z - B),
            y: ue.position.y - Y + Q[1] * (U - O)
          }, A.push(ue);
      }
      if ((z || q) && (F.width = z && (!s.resizeDirection || s.resizeDirection === "horizontal") ? Z : y.width, F.height = q && (!s.resizeDirection || s.resizeDirection === "vertical") ? U : y.height, y.width = F.width, y.height = F.height), N && p.expandParent) {
        const L = Q[0] * (F.width ?? 0);
        F.x && F.x < L && (y.x = L, S.x = S.x - (F.x - L));
        const Y = Q[1] * (F.height ?? 0);
        F.y && F.y < Y && (y.y = Y, S.y = S.y - (F.y - Y));
      }
      const K = uh({
        width: y.width,
        prevWidth: B,
        height: y.height,
        prevHeight: O,
        affectsX: s.controlDirection.affectsX,
        affectsY: s.controlDirection.affectsY
      }), te = { ...y, direction: K };
      w?.(M, te) !== !1 && (j = !0, g?.(M, te), o(F, A));
    }).on("end", (M) => {
      j && (x?.(M, { ...y }), r?.({ ...y }), j = !1);
    });
    i.call(H);
  }
  function l() {
    i.on(".drag", null);
  }
  return {
    update: c,
    destroy: l
  };
}
var Fo = { exports: {} }, Wo = {}, Yo = { exports: {} }, Xo = {};
var Ni;
function gh() {
  if (Ni) return Xo;
  Ni = 1;
  var e = it;
  function t(d, f) {
    return d === f && (d !== 0 || 1 / d === 1 / f) || d !== d && f !== f;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, i = e.useLayoutEffect, s = e.useDebugValue;
  function c(d, f) {
    var m = f(), g = o({ inst: { value: m, getSnapshot: f } }), x = g[0].inst, w = g[1];
    return i(
      function() {
        x.value = m, x.getSnapshot = f, l(x) && w({ inst: x });
      },
      [d, m, f]
    ), r(
      function() {
        return l(x) && w({ inst: x }), d(function() {
          l(x) && w({ inst: x });
        });
      },
      [d]
    ), s(m), m;
  }
  function l(d) {
    var f = d.getSnapshot;
    d = d.value;
    try {
      var m = f();
      return !n(d, m);
    } catch {
      return !0;
    }
  }
  function a(d, f) {
    return f();
  }
  var u = typeof window > "u" || typeof window.document > "u" || typeof window.document.createElement > "u" ? a : c;
  return Xo.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : u, Xo;
}
var Ei;
function mh() {
  return Ei || (Ei = 1, Yo.exports = gh()), Yo.exports;
}
var _i;
function yh() {
  if (_i) return Wo;
  _i = 1;
  var e = it, t = mh();
  function n(a, u) {
    return a === u && (a !== 0 || 1 / a === 1 / u) || a !== a && u !== u;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, i = e.useRef, s = e.useEffect, c = e.useMemo, l = e.useDebugValue;
  return Wo.useSyncExternalStoreWithSelector = function(a, u, d, f, m) {
    var g = i(null);
    if (g.current === null) {
      var x = { hasValue: !1, value: null };
      g.current = x;
    } else x = g.current;
    g = c(
      function() {
        function y(N) {
          if (!S) {
            if (S = !0, p = N, N = f(N), m !== void 0 && x.hasValue) {
              var I = x.value;
              if (m(I, N))
                return v = I;
            }
            return v = N;
          }
          if (I = v, o(p, N)) return I;
          var D = f(N);
          return m !== void 0 && m(I, D) ? (p = N, I) : (p = N, v = D);
        }
        var S = !1, p, v, E = d === void 0 ? null : d;
        return [
          function() {
            return y(u());
          },
          E === null ? void 0 : function() {
            return y(E());
          }
        ];
      },
      [u, d, f, m]
    );
    var w = r(a, g[0], g[1]);
    return s(
      function() {
        x.hasValue = !0, x.value = w;
      },
      [w]
    ), l(w), w;
  }, Wo;
}
var Ci;
function xh() {
  return Ci || (Ci = 1, Fo.exports = yh()), Fo.exports;
}
var wh = xh();
const vh = /* @__PURE__ */ Tc(wh), bh = {}, ki = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (u, d) => {
    const f = typeof u == "function" ? u(t) : u;
    if (!Object.is(f, t)) {
      const m = t;
      t = d ?? (typeof f != "object" || f === null) ? f : Object.assign({}, t, f), n.forEach((g) => g(t, m));
    }
  }, r = () => t, l = { setState: o, getState: r, getInitialState: () => a, subscribe: (u) => (n.add(u), () => n.delete(u)), destroy: () => {
    (bh ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, a = t = e(o, r, l);
  return l;
}, Sh = (e) => e ? ki(e) : ki, { useDebugValue: Nh } = it, { useSyncExternalStoreWithSelector: Eh } = vh, _h = (e) => e;
function pa(e, t = _h, n) {
  const o = Eh(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return Nh(o), o;
}
const Ii = (e, t) => {
  const n = Sh(e), o = (r, i = t) => pa(n, r, i);
  return Object.assign(o, n), o;
}, Ch = (e, t) => e ? Ii(e, t) : Ii;
function me(e, t) {
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
var qo = { exports: {} }, Ee = {};
var Mi;
function kh() {
  if (Mi) return Ee;
  Mi = 1;
  var e = it;
  function t(l) {
    var a = "https://react.dev/errors/" + l;
    if (1 < arguments.length) {
      a += "?args[]=" + encodeURIComponent(arguments[1]);
      for (var u = 2; u < arguments.length; u++)
        a += "&args[]=" + encodeURIComponent(arguments[u]);
    }
    return "Minified React error #" + l + "; visit " + a + " for the full message or use the non-minified dev environment for full errors and additional helpful warnings.";
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
  function i(l, a, u) {
    var d = 3 < arguments.length && arguments[3] !== void 0 ? arguments[3] : null;
    return {
      $$typeof: r,
      key: d == null ? null : "" + d,
      children: l,
      containerInfo: a,
      implementation: u
    };
  }
  var s = e.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE;
  function c(l, a) {
    if (l === "font") return "";
    if (typeof a == "string")
      return a === "use-credentials" ? a : "";
  }
  return Ee.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, Ee.createPortal = function(l, a) {
    var u = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!a || a.nodeType !== 1 && a.nodeType !== 9 && a.nodeType !== 11)
      throw Error(t(299));
    return i(l, a, null, u);
  }, Ee.flushSync = function(l) {
    var a = s.T, u = o.p;
    try {
      if (s.T = null, o.p = 2, l) return l();
    } finally {
      s.T = a, o.p = u, o.d.f();
    }
  }, Ee.preconnect = function(l, a) {
    typeof l == "string" && (a ? (a = a.crossOrigin, a = typeof a == "string" ? a === "use-credentials" ? a : "" : void 0) : a = null, o.d.C(l, a));
  }, Ee.prefetchDNS = function(l) {
    typeof l == "string" && o.d.D(l);
  }, Ee.preinit = function(l, a) {
    if (typeof l == "string" && a && typeof a.as == "string") {
      var u = a.as, d = c(u, a.crossOrigin), f = typeof a.integrity == "string" ? a.integrity : void 0, m = typeof a.fetchPriority == "string" ? a.fetchPriority : void 0;
      u === "style" ? o.d.S(
        l,
        typeof a.precedence == "string" ? a.precedence : void 0,
        {
          crossOrigin: d,
          integrity: f,
          fetchPriority: m
        }
      ) : u === "script" && o.d.X(l, {
        crossOrigin: d,
        integrity: f,
        fetchPriority: m,
        nonce: typeof a.nonce == "string" ? a.nonce : void 0
      });
    }
  }, Ee.preinitModule = function(l, a) {
    if (typeof l == "string")
      if (typeof a == "object" && a !== null) {
        if (a.as == null || a.as === "script") {
          var u = c(
            a.as,
            a.crossOrigin
          );
          o.d.M(l, {
            crossOrigin: u,
            integrity: typeof a.integrity == "string" ? a.integrity : void 0,
            nonce: typeof a.nonce == "string" ? a.nonce : void 0
          });
        }
      } else a == null && o.d.M(l);
  }, Ee.preload = function(l, a) {
    if (typeof l == "string" && typeof a == "object" && a !== null && typeof a.as == "string") {
      var u = a.as, d = c(u, a.crossOrigin);
      o.d.L(l, u, {
        crossOrigin: d,
        integrity: typeof a.integrity == "string" ? a.integrity : void 0,
        nonce: typeof a.nonce == "string" ? a.nonce : void 0,
        type: typeof a.type == "string" ? a.type : void 0,
        fetchPriority: typeof a.fetchPriority == "string" ? a.fetchPriority : void 0,
        referrerPolicy: typeof a.referrerPolicy == "string" ? a.referrerPolicy : void 0,
        imageSrcSet: typeof a.imageSrcSet == "string" ? a.imageSrcSet : void 0,
        imageSizes: typeof a.imageSizes == "string" ? a.imageSizes : void 0,
        media: typeof a.media == "string" ? a.media : void 0
      });
    }
  }, Ee.preloadModule = function(l, a) {
    if (typeof l == "string")
      if (a) {
        var u = c(a.as, a.crossOrigin);
        o.d.m(l, {
          as: typeof a.as == "string" && a.as !== "script" ? a.as : void 0,
          crossOrigin: u,
          integrity: typeof a.integrity == "string" ? a.integrity : void 0
        });
      } else o.d.m(l);
  }, Ee.requestFormReset = function(l) {
    o.d.r(l);
  }, Ee.unstable_batchedUpdates = function(l, a) {
    return l(a);
  }, Ee.useFormState = function(l, a, u) {
    return s.H.useFormState(l, a, u);
  }, Ee.useFormStatus = function() {
    return s.H.useHostTransitionStatus();
  }, Ee.version = "19.2.7", Ee;
}
var Ai;
function Ih() {
  if (Ai) return qo.exports;
  Ai = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), qo.exports = kh(), qo.exports;
}
var Mh = Ih();
const lo = gr(null), Ah = lo.Provider, ga = Te.error001("react");
function le(e, t) {
  const n = an(lo);
  if (n === null)
    throw new Error(ga);
  return pa(n, e, t);
}
function ye() {
  const e = an(lo);
  if (e === null)
    throw new Error(ga);
  return ge(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const Di = { display: "none" }, Dh = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, ma = "react-flow__node-desc", ya = "react-flow__edge-desc", jh = "react-flow__aria-live", Ph = (e) => e.ariaLiveMessage, $h = (e) => e.ariaLabelConfig;
function Th({ rfId: e }) {
  const t = le(Ph);
  return h.jsx("div", { id: `${jh}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: Dh, children: t });
}
function Rh({ rfId: e, disableKeyboardA11y: t }) {
  const n = le($h);
  return h.jsxs(h.Fragment, { children: [h.jsx("div", { id: `${ma}-${e}`, style: Di, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), h.jsx("div", { id: `${ya}-${e}`, style: Di, children: n["edge.a11yDescription.default"] }), !t && h.jsx(Th, { rfId: e })] });
}
const uo = eo(({ position: e = "top-left", children: t, className: n, style: o, ...r }, i) => {
  const s = `${e}`.split("-");
  return h.jsx("div", { className: be(["react-flow__panel", n, ...s]), style: o, ref: i, ...r, children: t });
});
uo.displayName = "Panel";
function zh({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : h.jsx(uo, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: h.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const Lh = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, An = (e) => e.id;
function Hh(e, t) {
  return me(e.selectedNodes.map(An), t.selectedNodes.map(An)) && me(e.selectedEdges.map(An), t.selectedEdges.map(An));
}
function Vh({ onSelectionChange: e }) {
  const t = ye(), { selectedNodes: n, selectedEdges: o } = le(Lh, Hh);
  return ce(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((i) => i(r));
  }, [n, o, e]), null;
}
const Oh = (e) => !!e.onSelectionChangeHandlers;
function Bh({ onSelectionChange: e }) {
  const t = le(Oh);
  return e || t ? h.jsx(Vh, { onSelectionChange: e }) : null;
}
const xa = [0, 0], Fh = { x: 0, y: 0, zoom: 1 }, Wh = [
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
], ji = [...Wh, "rfId"], Yh = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), Pi = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: Jt,
  nodeOrigin: xa,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function Xh(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: i, setNodeExtent: s, reset: c, setDefaultNodesAndEdges: l } = le(Yh, me), a = ye();
  ce(() => (l(e.defaultNodes, e.defaultEdges), () => {
    u.current = Pi, c();
  }), []);
  const u = ae(Pi);
  return ce(
    () => {
      for (const d of ji) {
        const f = e[d], m = u.current[d];
        f !== m && (typeof e[d] > "u" || (d === "nodes" ? t(f) : d === "edges" ? n(f) : d === "minZoom" ? o(f) : d === "maxZoom" ? r(f) : d === "translateExtent" ? i(f) : d === "nodeExtent" ? s(f) : d === "ariaLabelConfig" ? a.setState({ ariaLabelConfig: kf(f) }) : d === "fitView" ? a.setState({ fitViewQueued: f }) : d === "fitViewOptions" ? a.setState({ fitViewOptions: f }) : a.setState({ [d]: f })));
      }
      u.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    ji.map((d) => e[d])
  ), null;
}
function $i() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function qh(e) {
  const [t, n] = ne(e === "system" ? null : e);
  return ce(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = $i(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : $i()?.matches ? "dark" : "light";
}
const Ti = typeof document < "u" ? document : null;
function on(e = null, t = { target: Ti, actInsideInputWithModifier: !0 }) {
  const [n, o] = ne(!1), r = ae(!1), i = ae(/* @__PURE__ */ new Set([])), [s, c] = ge(() => {
    if (e !== null) {
      const a = (Array.isArray(e) ? e : [e]).filter((d) => typeof d == "string").map((d) => d.replace("+", `
`).replace(`

`, `
+`).split(`
`)), u = a.reduce((d, f) => d.concat(...f), []);
      return [a, u];
    }
    return [[], []];
  }, [e]);
  return ce(() => {
    const l = t?.target ?? Ti, a = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const u = (m) => {
        if (r.current = m.ctrlKey || m.metaKey || m.shiftKey || m.altKey, (!r.current || r.current && !a) && Ks(m))
          return !1;
        const x = zi(m.code, c);
        if (i.current.add(m[x]), Ri(s, i.current, !1)) {
          const w = m.composedPath?.()?.[0] || m.target, y = w?.nodeName === "BUTTON" || w?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !y) && m.preventDefault(), o(!0);
        }
      }, d = (m) => {
        const g = zi(m.code, c);
        Ri(s, i.current, !0) ? (o(!1), i.current.clear()) : i.current.delete(m[g]), m.key === "Meta" && i.current.clear(), r.current = !1;
      }, f = () => {
        i.current.clear(), o(!1);
      };
      return l?.addEventListener("keydown", u), l?.addEventListener("keyup", d), window.addEventListener("blur", f), window.addEventListener("contextmenu", f), () => {
        l?.removeEventListener("keydown", u), l?.removeEventListener("keyup", d), window.removeEventListener("blur", f), window.removeEventListener("contextmenu", f);
      };
    }
  }, [e, o]), n;
}
function Ri(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function zi(e, t) {
  return t.includes(e) ? "code" : "key";
}
const Zh = () => {
  const e = ye();
  return ge(() => ({
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
      const { width: o, height: r, minZoom: i, maxZoom: s, panZoom: c } = e.getState(), l = Cr(t, o, r, i, s, n?.padding ?? 0.1);
      return c ? (await c.setViewport(l, {
        duration: n?.duration,
        ease: n?.ease,
        interpolate: n?.interpolate
      }), !0) : !1;
    },
    screenToFlowPosition: (t, n = {}) => {
      const { transform: o, snapGrid: r, snapToGrid: i, domNode: s } = e.getState();
      if (!s)
        return t;
      const { x: c, y: l } = s.getBoundingClientRect(), a = {
        x: t.x - c,
        y: t.y - l
      }, u = n.snapGrid ?? r, d = n.snapToGrid ?? i;
      return $t(a, o, d, u);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: r, y: i } = o.getBoundingClientRect(), s = It(t, n);
      return {
        x: s.x + r,
        y: s.y + i
      };
    }
  }), []);
};
function wa(e, t) {
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
    const c = { ...i };
    for (const l of s)
      Uh(l, c);
    n.push(c);
  }
  return r.length && r.forEach((i) => {
    i.index !== void 0 ? n.splice(i.index, 0, { ...i.item }) : n.push({ ...i.item });
  }), n;
}
function Uh(e, t) {
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
function va(e, t) {
  return wa(e, t);
}
function ba(e, t) {
  return wa(e, t);
}
function at(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function vt(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [r, i] of e) {
    const s = t.has(r);
    !(i.selected === void 0 && !s) && i.selected !== s && (n && (i.selected = s), o.push(at(i.id, s)));
  }
  return o;
}
function Li({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((r) => [r.id, r]));
  for (const [r, i] of e.entries()) {
    const s = t.get(i.id), c = s?.internals?.userNode ?? s;
    c !== void 0 && c !== i && n.push({ id: i.id, item: i, type: "replace" }), c === void 0 && n.push({ item: i, type: "add", index: r });
  }
  for (const [r] of t)
    o.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function Hi(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const Sa = Xs();
function Na(e, t, n = {}) {
  return jf(e, t, {
    ...n,
    onError: n.onError ?? Sa
  });
}
function Kh(e, t, n, o = { shouldReplaceId: !0 }) {
  return Pf(e, t, n, {
    ...o,
    onError: o.onError ?? Sa
  });
}
const Vi = (e) => xf(e), Gh = (e) => Bs(e);
function Ea(e) {
  return eo(e);
}
const Qh = typeof window < "u" ? Pc : ce;
function Oi(e) {
  const [t, n] = ne(BigInt(0)), [o] = ne(() => Jh(() => n((r) => r + BigInt(1))));
  return Qh(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function Jh(e) {
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
const _a = gr(null);
function ep({ children: e }) {
  const t = ye(), n = pe((c) => {
    const { nodes: l = [], setNodes: a, hasDefaultNodes: u, onNodesChange: d, nodeLookup: f, fitViewQueued: m, onNodesChangeMiddlewareMap: g } = t.getState();
    let x = l;
    for (const y of c)
      x = typeof y == "function" ? y(x) : y;
    let w = Li({
      items: x,
      lookup: f
    });
    for (const y of g.values())
      w = y(w);
    u && a(x), w.length > 0 ? d?.(w) : m && window.requestAnimationFrame(() => {
      const { fitViewQueued: y, nodes: S, setNodes: p } = t.getState();
      y && p(S);
    });
  }, []), o = Oi(n), r = pe((c) => {
    const { edges: l = [], setEdges: a, hasDefaultEdges: u, onEdgesChange: d, edgeLookup: f } = t.getState();
    let m = l;
    for (const g of c)
      m = typeof g == "function" ? g(m) : g;
    u ? a(m) : d && d(Li({
      items: m,
      lookup: f
    }));
  }, []), i = Oi(r), s = ge(() => ({ nodeQueue: o, edgeQueue: i }), []);
  return h.jsx(_a.Provider, { value: s, children: e });
}
function tp() {
  const e = an(_a);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const np = (e) => !!e.panZoom;
function Pr() {
  const e = Zh(), t = ye(), n = tp(), o = le(np), r = ge(() => {
    const i = (d) => t.getState().nodeLookup.get(d), s = (d) => {
      n.nodeQueue.push(d);
    }, c = (d) => {
      n.edgeQueue.push(d);
    }, l = (d) => {
      const { nodeLookup: f, nodeOrigin: m } = t.getState(), g = Vi(d) ? d : f.get(d.id), x = g.parentId ? Zs(g.position, g.measured, g.parentId, f, m) : g.position, w = {
        ...g,
        position: x,
        width: g.measured?.width ?? g.width,
        height: g.measured?.height ?? g.height
      };
      return kt(w);
    }, a = (d, f, m = { replace: !1 }) => {
      s((g) => g.map((x) => {
        if (x.id === d) {
          const w = typeof f == "function" ? f(x) : f;
          return m.replace && Vi(w) ? w : { ...x, ...w };
        }
        return x;
      }));
    }, u = (d, f, m = { replace: !1 }) => {
      c((g) => g.map((x) => {
        if (x.id === d) {
          const w = typeof f == "function" ? f(x) : f;
          return m.replace && Gh(w) ? w : { ...x, ...w };
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
      setEdges: c,
      addNodes: (d) => {
        const f = Array.isArray(d) ? d : [d];
        n.nodeQueue.push((m) => [...m, ...f]);
      },
      addEdges: (d) => {
        const f = Array.isArray(d) ? d : [d];
        n.edgeQueue.push((m) => [...m, ...f]);
      },
      toObject: () => {
        const { nodes: d = [], edges: f = [], transform: m } = t.getState(), [g, x, w] = m;
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
        const { nodes: m, edges: g, onNodesDelete: x, onEdgesDelete: w, triggerNodeChanges: y, triggerEdgeChanges: S, onDelete: p, onBeforeDelete: v } = t.getState(), { nodes: E, edges: N } = await Nf({
          nodesToRemove: d,
          edgesToRemove: f,
          nodes: m,
          edges: g,
          onBeforeDelete: v
        }), I = N.length > 0, D = E.length > 0;
        if (I) {
          const j = N.map(Hi);
          w?.(N), S(j);
        }
        if (D) {
          const j = E.map(Hi);
          x?.(E), y(j);
        }
        return (D || I) && p?.({ nodes: E, edges: N }), { deletedNodes: E, deletedEdges: N };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (d, f = !0, m) => {
        const g = ui(d), x = g ? d : l(d), w = m !== void 0;
        return x ? (m || t.getState().nodes).filter((y) => {
          const S = t.getState().nodeLookup.get(y.id);
          if (S && !g && (y.id === d.id || !S.internals.positionAbsolute))
            return !1;
          const p = kt(w ? y : S), v = tn(p, x);
          return f && v > 0 || v >= p.width * p.height || v >= x.width * x.height;
        }) : [];
      },
      isNodeIntersecting: (d, f, m = !0) => {
        const x = ui(d) ? d : l(d);
        if (!x)
          return !1;
        const w = tn(x, f);
        return m && w > 0 || w >= f.width * f.height || w >= x.width * x.height;
      },
      updateNode: a,
      updateNodeData: (d, f, m = { replace: !1 }) => {
        a(d, (g) => {
          const x = typeof f == "function" ? f(g) : f;
          return m.replace ? { ...g, data: x } : { ...g, data: { ...g.data, ...x } };
        }, m);
      },
      updateEdge: u,
      updateEdgeData: (d, f, m = { replace: !1 }) => {
        u(d, (g) => {
          const x = typeof f == "function" ? f(g) : f;
          return m.replace ? { ...g, data: x } : { ...g, data: { ...g.data, ...x } };
        }, m);
      },
      getNodesBounds: (d) => {
        const { nodeLookup: f, nodeOrigin: m } = t.getState();
        return wf(d, { nodeLookup: f, nodeOrigin: m });
      },
      getHandleConnections: ({ type: d, id: f, nodeId: m }) => Array.from(t.getState().connectionLookup.get(`${m}-${d}${f ? `-${f}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: d, handleId: f, nodeId: m }) => Array.from(t.getState().connectionLookup.get(`${m}${d ? f ? `-${d}-${f}` : `-${d}` : ""}`)?.values() ?? []),
      fitView: async (d) => {
        const f = t.getState().fitViewResolver ?? Cf();
        return t.setState({ fitViewQueued: !0, fitViewOptions: d, fitViewResolver: f }), n.nodeQueue.push((m) => [...m]), f.promise;
      }
    };
  }, []);
  return ge(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const Bi = (e) => e.selected, op = typeof window < "u" ? window : void 0;
function rp({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = ye(), { deleteElements: o } = Pr(), r = on(e, { actInsideInputWithModifier: !1 }), i = on(t, { target: op });
  ce(() => {
    if (r) {
      const { edges: s, nodes: c } = n.getState();
      o({ nodes: c.filter(Bi), edges: s.filter(Bi) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), ce(() => {
    n.setState({ multiSelectionActive: i });
  }, [i]);
}
function ip(e) {
  const t = ye();
  ce(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = kr(e.current);
      (o.height === 0 || o.width === 0) && t.getState().onError?.("004", Te.error004()), t.setState({ width: o.width || 500, height: o.height || 500 });
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
const fo = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, sp = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function ap({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: i = ut.Free, zoomOnDoubleClick: s = !0, panOnDrag: c = !0, defaultViewport: l, translateExtent: a, minZoom: u, maxZoom: d, zoomActivationKeyCode: f, preventScrolling: m = !0, children: g, noWheelClassName: x, noPanClassName: w, onViewportChange: y, isControlledViewport: S, paneClickDistance: p, selectionOnDrag: v }) {
  const E = ye(), N = ae(null), { userSelectionActive: I, lib: D, connectionInProgress: j } = le(sp, me), H = on(f), M = ae();
  ip(N);
  const R = pe((V) => {
    y?.({ x: V[0], y: V[1], zoom: V[2] }), S || E.setState({ transform: V });
  }, [y, S]);
  return ce(() => {
    if (N.current) {
      M.current = lh({
        domNode: N.current,
        minZoom: u,
        maxZoom: d,
        translateExtent: a,
        viewport: l,
        onDraggingChange: (_) => E.setState((A) => A.paneDragging === _ ? A : { paneDragging: _ }),
        onPanZoomStart: (_, A) => {
          const { onViewportChangeStart: P, onMoveStart: $ } = E.getState();
          $?.(_, A), P?.(A);
        },
        onPanZoom: (_, A) => {
          const { onViewportChange: P, onMove: $ } = E.getState();
          $?.(_, A), P?.(A);
        },
        onPanZoomEnd: (_, A) => {
          const { onViewportChangeEnd: P, onMoveEnd: $ } = E.getState();
          $?.(_, A), P?.(A);
        }
      });
      const { x: V, y: b, zoom: C } = M.current.getViewport();
      return E.setState({
        panZoom: M.current,
        transform: [V, b, C],
        domNode: N.current.closest(".react-flow")
      }), () => {
        M.current?.destroy();
      };
    }
  }, []), ce(() => {
    M.current?.update({
      onPaneContextMenu: e,
      zoomOnScroll: t,
      zoomOnPinch: n,
      panOnScroll: o,
      panOnScrollSpeed: r,
      panOnScrollMode: i,
      zoomOnDoubleClick: s,
      panOnDrag: c,
      zoomActivationKeyPressed: H,
      preventScrolling: m,
      noPanClassName: w,
      userSelectionActive: I,
      noWheelClassName: x,
      lib: D,
      onTransformChange: R,
      connectionInProgress: j,
      selectionOnDrag: v,
      paneClickDistance: p
    });
  }, [
    e,
    t,
    n,
    o,
    r,
    i,
    s,
    c,
    H,
    m,
    w,
    I,
    x,
    D,
    R,
    j,
    v,
    p
  ]), h.jsx("div", { className: "react-flow__renderer", ref: N, style: fo, children: g });
}
const cp = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function lp() {
  const { userSelectionActive: e, userSelectionRect: t } = le(cp, me);
  return e && t ? h.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const Zo = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, up = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function dp({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = en.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: i, selectionOnDrag: s, onSelectionStart: c, onSelectionEnd: l, onPaneClick: a, onPaneContextMenu: u, onPaneScroll: d, onPaneMouseEnter: f, onPaneMouseMove: m, onPaneMouseLeave: g, children: x }) {
  const w = ae(0), y = ye(), { userSelectionActive: S, elementsSelectable: p, dragging: v, connectionInProgress: E, panBy: N, autoPanSpeed: I } = le(up, me), D = p && (e || S), j = ae(null), H = ae(), M = ae(/* @__PURE__ */ new Set()), R = ae(/* @__PURE__ */ new Set()), V = ae(!1), b = ae({ x: 0, y: 0 }), C = ae(!1), _ = (z) => {
    if (V.current || E) {
      V.current = !1;
      return;
    }
    a?.(z), y.getState().resetSelectedElements(), y.setState({ nodesSelectionActive: !1 });
  }, A = (z) => {
    if (Array.isArray(o) && o?.includes(2)) {
      z.preventDefault();
      return;
    }
    u?.(z);
  }, P = d ? (z) => d(z) : void 0, $ = (z) => {
    V.current && (z.stopPropagation(), V.current = !1);
  }, B = (z) => {
    const { domNode: q, transform: re } = y.getState();
    if (H.current = q?.getBoundingClientRect(), !H.current)
      return;
    const oe = z.target === j.current;
    if (!oe && !!z.target.closest(".nokey") || !e || !(s && oe || t) || z.button !== 0 || !z.isPrimary)
      return;
    z.target?.setPointerCapture?.(z.pointerId), V.current = !1;
    const { x: ie, y: L } = We(z.nativeEvent, H.current), Y = $t({ x: ie, y: L }, re);
    y.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: Y.x,
        startY: Y.y,
        x: ie,
        y: L
      }
    }), oe || (z.stopPropagation(), z.preventDefault());
  };
  function O(z, q) {
    const { userSelectionRect: re } = y.getState();
    if (!re)
      return;
    const { transform: oe, nodeLookup: K, edgeLookup: te, connectionLookup: ie, triggerNodeChanges: L, triggerEdgeChanges: Y, defaultEdgeOptions: ue } = y.getState(), xe = { x: re.startX, y: re.startY }, { x: he, y: ze } = It(xe, oe), Le = {
      startX: xe.x,
      startY: xe.y,
      x: z < he ? z : he,
      y: q < ze ? q : ze,
      width: Math.abs(z - he),
      height: Math.abs(q - ze)
    }, we = M.current, Ae = R.current;
    M.current = new Set(Er(K, Le, oe, n === en.Partial, !0).map((Ie) => Ie.id)), R.current = /* @__PURE__ */ new Set();
    const Xe = ue?.selectable ?? !0;
    for (const Ie of M.current) {
      const De = ie.get(Ie);
      if (De)
        for (const { edgeId: He } of De.values()) {
          const Ve = te.get(He);
          Ve && (Ve.selectable ?? Xe) && R.current.add(He);
        }
    }
    if (!di(we, M.current)) {
      const Ie = vt(K, M.current, !0);
      L(Ie);
    }
    if (!di(Ae, R.current)) {
      const Ie = vt(te, R.current);
      Y(Ie);
    }
    y.setState({
      userSelectionRect: Le,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function F() {
    if (!r || !H.current)
      return;
    const [z, q] = _r(b.current, H.current, I);
    N({ x: z, y: q }).then((re) => {
      if (!V.current || !re) {
        w.current = requestAnimationFrame(F);
        return;
      }
      const { x: oe, y: K } = b.current;
      O(oe, K), w.current = requestAnimationFrame(F);
    });
  }
  const Q = () => {
    cancelAnimationFrame(w.current), w.current = 0, C.current = !1;
  };
  ce(() => () => Q(), []);
  const Z = (z) => {
    const { userSelectionRect: q, transform: re, resetSelectedElements: oe } = y.getState();
    if (!H.current || !q)
      return;
    const { x: K, y: te } = We(z.nativeEvent, H.current);
    b.current = { x: K, y: te };
    const ie = It({ x: q.startX, y: q.startY }, re);
    if (!V.current) {
      const L = t ? 0 : i;
      if (Math.hypot(K - ie.x, te - ie.y) <= L)
        return;
      oe(), c?.(z);
    }
    V.current = !0, C.current || (F(), C.current = !0), O(K, te);
  }, U = (z) => {
    z.button === 0 && (z.target?.releasePointerCapture?.(z.pointerId), !S && z.target === j.current && y.getState().userSelectionRect && _?.(z), y.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), V.current && (l?.(z), y.setState({
      nodesSelectionActive: M.current.size > 0
    })), Q());
  }, se = (z) => {
    z.target?.releasePointerCapture?.(z.pointerId), Q();
  }, G = o === !0 || Array.isArray(o) && o.includes(0);
  return h.jsxs("div", { className: be(["react-flow__pane", { draggable: G, dragging: v, selection: e }]), onClick: D ? void 0 : Zo(_, j), onContextMenu: Zo(A, j), onWheel: Zo(P, j), onPointerEnter: D ? void 0 : f, onPointerMove: D ? Z : m, onPointerUp: D ? U : void 0, onPointerCancel: D ? se : void 0, onPointerDownCapture: D ? B : void 0, onClickCapture: D ? $ : void 0, onPointerLeave: g, ref: j, style: fo, children: [x, h.jsx(lp, {})] });
}
function dr({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: i, multiSelectionActive: s, nodeLookup: c, onError: l } = t.getState(), a = c.get(e);
  if (!a) {
    l?.("012", Te.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), a.selected ? (n || a.selected && s) && (i({ nodes: [a], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function Ca({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: i, nodeClickDistance: s }) {
  const c = ye(), [l, a] = ne(!1), u = ae();
  return ce(() => {
    u.current = Uf({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (d) => {
        dr({
          id: d,
          store: c,
          nodeRef: e
        });
      },
      onDragStart: () => {
        a(!0);
      },
      onDragStop: () => {
        a(!1);
      }
    });
  }, []), ce(() => {
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
const fp = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function ka() {
  const e = ye();
  return pe((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: i, nodesDraggable: s, onError: c, updateNodePositions: l, nodeLookup: a, nodeOrigin: u } = e.getState(), d = /* @__PURE__ */ new Map(), f = fp(s), m = r ? i[0] : 5, g = r ? i[1] : 5, x = n.direction.x * m * n.factor, w = n.direction.y * g * n.factor;
    for (const [, y] of a) {
      if (!f(y))
        continue;
      let S = {
        x: y.internals.positionAbsolute.x + x,
        y: y.internals.positionAbsolute.y + w
      };
      r && (S = fn(S, i));
      const { position: p, positionAbsolute: v } = Fs({
        nodeId: y.id,
        nextPosition: S,
        nodeLookup: a,
        nodeExtent: o,
        nodeOrigin: u,
        onError: c
      });
      y.position = p, y.internals.positionAbsolute = v, d.set(y.id, y);
    }
    l(d);
  }, []);
}
const $r = gr(null), hp = $r.Provider;
$r.Consumer;
const Ia = () => an($r), pp = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), gp = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: i, connection: s } = o, { fromHandle: c, toHandle: l, isValid: a } = s, u = l?.nodeId === e && l?.id === t && l?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: u,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: i === _t.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!r,
    valid: u && a
  };
};
function mp({ type: e = "source", position: t = ee.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: i = !0, id: s, onConnect: c, children: l, className: a, onMouseDown: u, onTouchStart: d, ...f }, m) {
  const g = s || null, x = e === "target", w = ye(), y = Ia(), { connectOnClick: S, noPanClassName: p, rfId: v } = le(pp, me), { connectingFrom: E, connectingTo: N, clickConnecting: I, isPossibleEndHandle: D, connectionInProcess: j, clickConnectionInProcess: H, valid: M } = le(gp(y, g, e), me);
  y || w.getState().onError?.("010", Te.error010());
  const R = (C) => {
    const { defaultEdgeOptions: _, onConnect: A, hasDefaultEdges: P } = w.getState(), $ = {
      ..._,
      ...C
    };
    if (P) {
      const { edges: B, setEdges: O, onError: F } = w.getState();
      O(Na($, B, { onError: F }));
    }
    A?.($), c?.($);
  }, V = (C) => {
    if (!y)
      return;
    const _ = Gs(C.nativeEvent);
    if (r && (_ && C.button === 0 || !_)) {
      const A = w.getState();
      ur.onPointerDown(C.nativeEvent, {
        handleDomNode: C.currentTarget,
        autoPanOnConnect: A.autoPanOnConnect,
        connectionMode: A.connectionMode,
        connectionRadius: A.connectionRadius,
        domNode: A.domNode,
        nodeLookup: A.nodeLookup,
        lib: A.lib,
        isTarget: x,
        handleId: g,
        nodeId: y,
        flowId: A.rfId,
        panBy: A.panBy,
        cancelConnection: A.cancelConnection,
        onConnectStart: A.onConnectStart,
        onConnectEnd: (...P) => w.getState().onConnectEnd?.(...P),
        updateConnection: A.updateConnection,
        onConnect: R,
        isValidConnection: n || ((...P) => w.getState().isValidConnection?.(...P) ?? !0),
        getTransform: () => w.getState().transform,
        getFromHandle: () => w.getState().connection.fromHandle,
        autoPanSpeed: A.autoPanSpeed,
        dragThreshold: A.connectionDragThreshold
      });
    }
    _ ? u?.(C) : d?.(C);
  }, b = (C) => {
    const { onClickConnectStart: _, onClickConnectEnd: A, connectionClickStartHandle: P, connectionMode: $, isValidConnection: B, lib: O, rfId: F, nodeLookup: Q, connection: Z } = w.getState();
    if (!y || !P && !r)
      return;
    if (!P) {
      _?.(C.nativeEvent, { nodeId: y, handleId: g, handleType: e }), w.setState({ connectionClickStartHandle: { nodeId: y, type: e, id: g } });
      return;
    }
    const U = Us(C.target), se = n || B, { connection: G, isValid: z } = ur.isValid(C.nativeEvent, {
      handle: {
        nodeId: y,
        id: g,
        type: e
      },
      connectionMode: $,
      fromNodeId: P.nodeId,
      fromHandleId: P.id || null,
      fromType: P.type,
      isValidConnection: se,
      flowId: F,
      doc: U,
      lib: O,
      nodeLookup: Q
    });
    z && G && R(G);
    const q = structuredClone(Z);
    delete q.inProgress, q.toPosition = q.toHandle ? q.toHandle.position : null, A?.(C, q), w.setState({ connectionClickStartHandle: null });
  };
  return h.jsx("div", { "data-handleid": g, "data-nodeid": y, "data-handlepos": t, "data-id": `${v}-${y}-${g}-${e}`, className: be([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    p,
    a,
    {
      source: !x,
      target: x,
      connectable: o,
      connectablestart: r,
      connectableend: i,
      clickconnecting: I,
      connectingfrom: E,
      connectingto: N,
      valid: M,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!j || D) && (j || H ? i : r)
    }
  ]), onMouseDown: V, onTouchStart: V, onClick: S ? b : void 0, ref: m, ...f, children: l });
}
const At = ve(Ea(mp));
function yp({ data: e, isConnectable: t, sourcePosition: n = ee.Bottom }) {
  return h.jsxs(h.Fragment, { children: [e?.label, h.jsx(At, { type: "source", position: n, isConnectable: t })] });
}
function xp({ data: e, isConnectable: t, targetPosition: n = ee.Top, sourcePosition: o = ee.Bottom }) {
  return h.jsxs(h.Fragment, { children: [h.jsx(At, { type: "target", position: n, isConnectable: t }), e?.label, h.jsx(At, { type: "source", position: o, isConnectable: t })] });
}
function wp() {
  return null;
}
function vp({ data: e, isConnectable: t, targetPosition: n = ee.Top }) {
  return h.jsxs(h.Fragment, { children: [h.jsx(At, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const Gn = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, Fi = {
  input: yp,
  default: xp,
  output: vp,
  group: wp
};
function bp(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const Sp = (e) => {
  const { width: t, height: n, x: o, y: r } = dn(e.nodeLookup, {
    filter: (i) => !!i.selected
  });
  return {
    width: Fe(t) ? t : null,
    height: Fe(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function Np({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = ye(), { width: r, height: i, transformString: s, userSelectionActive: c } = le(Sp, me), l = ka(), a = ae(null);
  ce(() => {
    n || a.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const u = !c && r !== null && i !== null;
  if (Ca({
    nodeRef: a,
    disabled: !u
  }), !u)
    return null;
  const d = e ? (m) => {
    const g = o.getState().nodes.filter((x) => x.selected);
    e(m, g);
  } : void 0, f = (m) => {
    Object.prototype.hasOwnProperty.call(Gn, m.key) && (m.preventDefault(), l({
      direction: Gn[m.key],
      factor: m.shiftKey ? 4 : 1
    }));
  };
  return h.jsx("div", { className: be(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: s
  }, children: h.jsx("div", { ref: a, className: "react-flow__nodesselection-rect", onContextMenu: d, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : f, style: {
    width: r,
    height: i
  } }) });
}
const Wi = typeof window < "u" ? window : void 0, Ep = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function Ma({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, paneClickDistance: c, deleteKeyCode: l, selectionKeyCode: a, selectionOnDrag: u, selectionMode: d, onSelectionStart: f, onSelectionEnd: m, multiSelectionKeyCode: g, panActivationKeyCode: x, zoomActivationKeyCode: w, elementsSelectable: y, zoomOnScroll: S, zoomOnPinch: p, panOnScroll: v, panOnScrollSpeed: E, panOnScrollMode: N, zoomOnDoubleClick: I, panOnDrag: D, autoPanOnSelection: j, defaultViewport: H, translateExtent: M, minZoom: R, maxZoom: V, preventScrolling: b, onSelectionContextMenu: C, noWheelClassName: _, noPanClassName: A, disableKeyboardA11y: P, onViewportChange: $, isControlledViewport: B }) {
  const { nodesSelectionActive: O, userSelectionActive: F } = le(Ep, me), Q = on(a, { target: Wi }), Z = on(x, { target: Wi }), U = Z || D, se = Z || v, G = u && U !== !0, z = Q || F || G;
  return rp({ deleteKeyCode: l, multiSelectionKeyCode: g }), h.jsx(ap, { onPaneContextMenu: i, elementsSelectable: y, zoomOnScroll: S, zoomOnPinch: p, panOnScroll: se, panOnScrollSpeed: E, panOnScrollMode: N, zoomOnDoubleClick: I, panOnDrag: !Q && U, defaultViewport: H, translateExtent: M, minZoom: R, maxZoom: V, zoomActivationKeyCode: w, preventScrolling: b, noWheelClassName: _, noPanClassName: A, onViewportChange: $, isControlledViewport: B, paneClickDistance: c, selectionOnDrag: G, children: h.jsxs(dp, { onSelectionStart: f, onSelectionEnd: m, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, panOnDrag: U, autoPanOnSelection: j, isSelecting: !!z, selectionMode: d, selectionKeyPressed: Q, paneClickDistance: c, selectionOnDrag: G, children: [e, O && h.jsx(Np, { onSelectionContextMenu: C, noPanClassName: A, disableKeyboardA11y: P })] }) });
}
Ma.displayName = "FlowRenderer";
const _p = ve(Ma), Cp = (e) => (t) => e ? Er(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function kp(e) {
  return le(pe(Cp(e), [e]), me);
}
const Ip = (e) => e.updateNodeInternals;
function Mp() {
  const e = le(Ip), [t] = ne(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
  return ce(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function Ap({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = ye(), i = ae(null), s = ae(null), c = ae(e.sourcePosition), l = ae(e.targetPosition), a = ae(t), u = n && !!e.internals.handleBounds;
  return ce(() => {
    i.current && !e.hidden && (!u || s.current !== i.current) && (s.current && o?.unobserve(s.current), o?.observe(i.current), s.current = i.current);
  }, [u, e.hidden]), ce(() => () => {
    s.current && (o?.unobserve(s.current), s.current = null);
  }, []), ce(() => {
    if (i.current) {
      const d = a.current !== t, f = c.current !== e.sourcePosition, m = l.current !== e.targetPosition;
      (d || f || m) && (a.current = t, c.current = e.sourcePosition, l.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: i.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), i;
}
function Dp({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: i, onDoubleClick: s, nodesDraggable: c, elementsSelectable: l, nodesConnectable: a, nodesFocusable: u, resizeObserver: d, noDragClassName: f, noPanClassName: m, disableKeyboardA11y: g, rfId: x, nodeTypes: w, nodeClickDistance: y, onError: S }) {
  const { node: p, internals: v, isParent: E } = le((z) => {
    const q = z.nodeLookup.get(e), re = z.parentLookup.has(e);
    return {
      node: q,
      internals: q.internals,
      isParent: re
    };
  }, me);
  let N = p.type || "default", I = w?.[N] || Fi[N];
  I === void 0 && (S?.("003", Te.error003(N)), N = "default", I = w?.default || Fi.default);
  const D = !!(p.draggable || c && typeof p.draggable > "u"), j = !!(p.selectable || l && typeof p.selectable > "u"), H = !!(p.connectable || a && typeof p.connectable > "u"), M = !!(p.focusable || u && typeof p.focusable > "u"), R = ye(), V = qs(p), b = Ap({ node: p, nodeType: N, hasDimensions: V, resizeObserver: d }), C = Ca({
    nodeRef: b,
    disabled: p.hidden || !D,
    noDragClassName: f,
    handleSelector: p.dragHandle,
    nodeId: e,
    isSelectable: j,
    nodeClickDistance: y
  }), _ = ka();
  if (p.hidden)
    return null;
  const A = et(p), P = bp(p), $ = j || D || t || n || o || r, B = n ? (z) => n(z, { ...v.userNode }) : void 0, O = o ? (z) => o(z, { ...v.userNode }) : void 0, F = r ? (z) => r(z, { ...v.userNode }) : void 0, Q = i ? (z) => i(z, { ...v.userNode }) : void 0, Z = s ? (z) => s(z, { ...v.userNode }) : void 0, U = (z) => {
    const { selectNodesOnDrag: q, nodeDragThreshold: re } = R.getState();
    j && (!q || !D || re > 0) && dr({
      id: e,
      store: R,
      nodeRef: b
    }), t && t(z, { ...v.userNode });
  }, se = (z) => {
    if (!(Ks(z.nativeEvent) || g)) {
      if (Ls.includes(z.key) && j) {
        const q = z.key === "Escape";
        dr({
          id: e,
          store: R,
          unselect: q,
          nodeRef: b
        });
      } else if (D && p.selected && Object.prototype.hasOwnProperty.call(Gn, z.key)) {
        z.preventDefault();
        const { ariaLabelConfig: q } = R.getState();
        R.setState({
          ariaLiveMessage: q["node.a11yDescription.ariaLiveMessage"]({
            direction: z.key.replace("Arrow", "").toLowerCase(),
            x: ~~v.positionAbsolute.x,
            y: ~~v.positionAbsolute.y
          })
        }), _({
          direction: Gn[z.key],
          factor: z.shiftKey ? 4 : 1
        });
      }
    }
  }, G = () => {
    if (g || !b.current?.matches(":focus-visible"))
      return;
    const { transform: z, width: q, height: re, autoPanOnNodeFocus: oe, setCenter: K } = R.getState();
    if (!oe)
      return;
    Er(/* @__PURE__ */ new Map([[e, p]]), { x: 0, y: 0, width: q, height: re }, z, !0).length > 0 || K(p.position.x + A.width / 2, p.position.y + A.height / 2, {
      zoom: z[2]
    });
  };
  return h.jsx("div", { className: be([
    "react-flow__node",
    `react-flow__node-${N}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [m]: D
    },
    p.className,
    {
      selected: p.selected,
      selectable: j,
      parent: E,
      draggable: D,
      dragging: C
    }
  ]), ref: b, style: {
    zIndex: v.z,
    transform: `translate(${v.positionAbsolute.x}px,${v.positionAbsolute.y}px)`,
    pointerEvents: $ ? "all" : "none",
    visibility: V ? "visible" : "hidden",
    ...p.style,
    ...P
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: B, onMouseMove: O, onMouseLeave: F, onContextMenu: Q, onClick: U, onDoubleClick: Z, onKeyDown: M ? se : void 0, tabIndex: M ? 0 : void 0, onFocus: M ? G : void 0, role: p.ariaRole ?? (M ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": g ? void 0 : `${ma}-${x}`, "aria-label": p.ariaLabel, ...p.domAttributes, children: h.jsx(hp, { value: e, children: h.jsx(I, { id: e, data: p.data, type: N, positionAbsoluteX: v.positionAbsolute.x, positionAbsoluteY: v.positionAbsolute.y, selected: p.selected ?? !1, selectable: j, draggable: D, deletable: p.deletable ?? !0, isConnectable: H, sourcePosition: p.sourcePosition, targetPosition: p.targetPosition, dragging: C, dragHandle: p.dragHandle, zIndex: v.z, parentId: p.parentId, ...A }) }) });
}
var jp = ve(Dp);
const Pp = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function Aa(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: i } = le(Pp, me), s = kp(e.onlyRenderVisibleElements), c = Mp();
  return h.jsx("div", { className: "react-flow__nodes", style: fo, children: s.map((l) => (
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
    h.jsx(jp, { id: l, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: i }, l)
  )) });
}
Aa.displayName = "NodeRenderer";
const $p = ve(Aa);
function Tp(e) {
  return le(pe((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const i = n.nodeLookup.get(r.source), s = n.nodeLookup.get(r.target);
        i && s && Af({
          sourceNode: i,
          targetNode: s,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && o.push(r.id);
      }
    return o;
  }, [e]), me);
}
const Rp = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return h.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, zp = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return h.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Yi = {
  [Zn.Arrow]: Rp,
  [Zn.ArrowClosed]: zp
};
function Lp(e) {
  const t = ye();
  return ge(() => Object.prototype.hasOwnProperty.call(Yi, e) ? Yi[e] : (t.getState().onError?.("009", Te.error009(e)), null), [e]);
}
const Hp = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: i = "strokeWidth", strokeWidth: s, orient: c = "auto-start-reverse" }) => {
  const l = Lp(t);
  return l ? h.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: i, orient: c, refX: "0", refY: "0", children: h.jsx(l, { color: n, strokeWidth: s }) }) : null;
}, Da = ({ defaultColor: e, rfId: t }) => {
  const n = le((i) => i.edges), o = le((i) => i.defaultEdgeOptions), r = ge(() => Lf(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? h.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: h.jsx("defs", { children: r.map((i) => h.jsx(Hp, { id: i.id, type: i.type, color: i.color, width: i.width, height: i.height, markerUnits: i.markerUnits, strokeWidth: i.strokeWidth, orient: i.orient }, i.id)) }) }) : null;
};
Da.displayName = "MarkerDefinitions";
var Vp = ve(Da);
function ja({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: i, labelBgPadding: s = [2, 4], labelBgBorderRadius: c = 2, children: l, className: a, ...u }) {
  const [d, f] = ne({ x: 1, y: 0, width: 0, height: 0 }), m = be(["react-flow__edge-textwrapper", a]), g = ae(null);
  return ce(() => {
    if (g.current) {
      const x = g.current.getBBox();
      f({
        x: x.x,
        y: x.y,
        width: x.width,
        height: x.height
      });
    }
  }, [n]), n ? h.jsxs("g", { transform: `translate(${e - d.width / 2} ${t - d.height / 2})`, className: m, visibility: d.width ? "visible" : "hidden", ...u, children: [r && h.jsx("rect", { width: d.width + 2 * s[0], x: -s[0], y: -s[1], height: d.height + 2 * s[1], className: "react-flow__edge-textbg", style: i, rx: c, ry: c }), h.jsx("text", { className: "react-flow__edge-text", y: d.height / 2, dy: "0.3em", ref: g, style: o, children: n }), l] }) : null;
}
ja.displayName = "EdgeText";
const Op = ve(ja);
function hn({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: c, labelBgBorderRadius: l, interactionWidth: a = 20, ...u }) {
  return h.jsxs(h.Fragment, { children: [h.jsx("path", { ...u, d: e, fill: "none", className: be(["react-flow__edge-path", u.className]) }), a ? h.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: a, className: "react-flow__edge-interaction" }) : null, o && Fe(t) && Fe(n) ? h.jsx(Op, { x: t, y: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: c, labelBgBorderRadius: l }) : null] });
}
function Xi({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === ee.Left || e === ee.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function Pa({ sourceX: e, sourceY: t, sourcePosition: n = ee.Bottom, targetX: o, targetY: r, targetPosition: i = ee.Top }) {
  const [s, c] = Xi({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [l, a] = Xi({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [u, d, f, m] = Qs({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: r,
    sourceControlX: s,
    sourceControlY: c,
    targetControlX: l,
    targetControlY: a
  });
  return [
    `M${e},${t} C${s},${c} ${l},${a} ${o},${r}`,
    u,
    d,
    f,
    m
  ];
}
function $a(e) {
  return ve(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: c, label: l, labelStyle: a, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: m, style: g, markerEnd: x, markerStart: w, interactionWidth: y }) => {
    const [S, p, v] = Pa({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: c
    }), E = e.isInternal ? void 0 : t;
    return h.jsx(hn, { id: E, path: S, labelX: p, labelY: v, label: l, labelStyle: a, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: m, style: g, markerEnd: x, markerStart: w, interactionWidth: y });
  });
}
const Bp = $a({ isInternal: !1 }), Ta = $a({ isInternal: !0 });
Bp.displayName = "SimpleBezierEdge";
Ta.displayName = "SimpleBezierEdgeInternal";
function Ra(e) {
  return ve(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: c, labelShowBg: l, labelBgStyle: a, labelBgPadding: u, labelBgBorderRadius: d, style: f, sourcePosition: m = ee.Bottom, targetPosition: g = ee.Top, markerEnd: x, markerStart: w, pathOptions: y, interactionWidth: S }) => {
    const [p, v, E] = Kn({
      sourceX: n,
      sourceY: o,
      sourcePosition: m,
      targetX: r,
      targetY: i,
      targetPosition: g,
      borderRadius: y?.borderRadius,
      offset: y?.offset,
      stepPosition: y?.stepPosition
    }), N = e.isInternal ? void 0 : t;
    return h.jsx(hn, { id: N, path: p, labelX: v, labelY: E, label: s, labelStyle: c, labelShowBg: l, labelBgStyle: a, labelBgPadding: u, labelBgBorderRadius: d, style: f, markerEnd: x, markerStart: w, interactionWidth: S });
  });
}
const za = Ra({ isInternal: !1 }), La = Ra({ isInternal: !0 });
za.displayName = "SmoothStepEdge";
La.displayName = "SmoothStepEdgeInternal";
function Ha(e) {
  return ve(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return h.jsx(za, { ...n, id: o, pathOptions: ge(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Fp = Ha({ isInternal: !1 }), Va = Ha({ isInternal: !0 });
Fp.displayName = "StepEdge";
Va.displayName = "StepEdgeInternal";
function Oa(e) {
  return ve(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: c, labelShowBg: l, labelBgStyle: a, labelBgPadding: u, labelBgBorderRadius: d, style: f, markerEnd: m, markerStart: g, interactionWidth: x }) => {
    const [w, y, S] = na({ sourceX: n, sourceY: o, targetX: r, targetY: i }), p = e.isInternal ? void 0 : t;
    return h.jsx(hn, { id: p, path: w, labelX: y, labelY: S, label: s, labelStyle: c, labelShowBg: l, labelBgStyle: a, labelBgPadding: u, labelBgBorderRadius: d, style: f, markerEnd: m, markerStart: g, interactionWidth: x });
  });
}
const Wp = Oa({ isInternal: !1 }), Ba = Oa({ isInternal: !0 });
Wp.displayName = "StraightEdge";
Ba.displayName = "StraightEdgeInternal";
function Fa(e) {
  return ve(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s = ee.Bottom, targetPosition: c = ee.Top, label: l, labelStyle: a, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: m, style: g, markerEnd: x, markerStart: w, pathOptions: y, interactionWidth: S }) => {
    const [p, v, E] = Js({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: c,
      curvature: y?.curvature
    }), N = e.isInternal ? void 0 : t;
    return h.jsx(hn, { id: N, path: p, labelX: v, labelY: E, label: l, labelStyle: a, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: m, style: g, markerEnd: x, markerStart: w, interactionWidth: S });
  });
}
const Yp = Fa({ isInternal: !1 }), Wa = Fa({ isInternal: !0 });
Yp.displayName = "BezierEdge";
Wa.displayName = "BezierEdgeInternal";
const qi = {
  default: Wa,
  straight: Ba,
  step: Va,
  smoothstep: La,
  simplebezier: Ta
}, Zi = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, Xp = (e, t, n) => n === ee.Left ? e - t : n === ee.Right ? e + t : e, qp = (e, t, n) => n === ee.Top ? e - t : n === ee.Bottom ? e + t : e, Ui = "react-flow__edgeupdater";
function Ki({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: i, onMouseOut: s, type: c }) {
  return h.jsx("circle", { onMouseDown: r, onMouseEnter: i, onMouseOut: s, className: be([Ui, `${Ui}-${c}`]), cx: Xp(t, o, e), cy: qp(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function Zp({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: i, targetY: s, sourcePosition: c, targetPosition: l, onReconnect: a, onReconnectStart: u, onReconnectEnd: d, setReconnecting: f, setUpdateHover: m }) {
  const g = ye(), x = (v, E) => {
    if (v.button !== 0)
      return;
    const { autoPanOnConnect: N, domNode: I, connectionMode: D, connectionRadius: j, lib: H, onConnectStart: M, cancelConnection: R, nodeLookup: V, rfId: b, panBy: C, updateConnection: _ } = g.getState(), A = E.type === "target", P = (O, F) => {
      f(!1), d?.(O, n, E.type, F);
    }, $ = (O) => a?.(n, O), B = (O, F) => {
      f(!0), u?.(v, n, E.type), M?.(O, F);
    };
    ur.onPointerDown(v.nativeEvent, {
      autoPanOnConnect: N,
      connectionMode: D,
      connectionRadius: j,
      domNode: I,
      handleId: E.id,
      nodeId: E.nodeId,
      nodeLookup: V,
      isTarget: A,
      edgeUpdaterType: E.type,
      lib: H,
      flowId: b,
      cancelConnection: R,
      panBy: C,
      isValidConnection: (...O) => g.getState().isValidConnection?.(...O) ?? !0,
      onConnect: $,
      onConnectStart: B,
      onConnectEnd: (...O) => g.getState().onConnectEnd?.(...O),
      onReconnectEnd: P,
      updateConnection: _,
      getTransform: () => g.getState().transform,
      getFromHandle: () => g.getState().connection.fromHandle,
      dragThreshold: g.getState().connectionDragThreshold,
      handleDomNode: v.currentTarget
    });
  }, w = (v) => x(v, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), y = (v) => x(v, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), S = () => m(!0), p = () => m(!1);
  return h.jsxs(h.Fragment, { children: [(e === !0 || e === "source") && h.jsx(Ki, { position: c, centerX: o, centerY: r, radius: t, onMouseDown: w, onMouseEnter: S, onMouseOut: p, type: "source" }), (e === !0 || e === "target") && h.jsx(Ki, { position: l, centerX: i, centerY: s, radius: t, onMouseDown: y, onMouseEnter: S, onMouseOut: p, type: "target" })] });
}
function Up({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: i, onContextMenu: s, onMouseEnter: c, onMouseMove: l, onMouseLeave: a, reconnectRadius: u, onReconnect: d, onReconnectStart: f, onReconnectEnd: m, rfId: g, edgeTypes: x, noPanClassName: w, onError: y, disableKeyboardA11y: S }) {
  let p = le((K) => K.edgeLookup.get(e));
  const v = le((K) => K.defaultEdgeOptions);
  p = v ? { ...v, ...p } : p;
  let E = p.type || "default", N = x?.[E] || qi[E];
  N === void 0 && (y?.("011", Te.error011(E)), E = "default", N = x?.default || qi.default);
  const I = !!(p.focusable || t && typeof p.focusable > "u"), D = typeof d < "u" && (p.reconnectable || n && typeof p.reconnectable > "u"), j = !!(p.selectable || o && typeof p.selectable > "u"), H = ae(null), [M, R] = ne(!1), [V, b] = ne(!1), C = ye(), { zIndex: _, sourceX: A, sourceY: P, targetX: $, targetY: B, sourcePosition: O, targetPosition: F } = le(pe((K) => {
    const te = K.nodeLookup.get(p.source), ie = K.nodeLookup.get(p.target);
    if (!te || !ie)
      return {
        zIndex: p.zIndex,
        ...Zi
      };
    const L = zf({
      id: e,
      sourceNode: te,
      targetNode: ie,
      sourceHandle: p.sourceHandle || null,
      targetHandle: p.targetHandle || null,
      connectionMode: K.connectionMode,
      onError: y
    });
    return {
      zIndex: Mf({
        selected: p.selected,
        zIndex: p.zIndex,
        sourceNode: te,
        targetNode: ie,
        elevateOnSelect: K.elevateEdgesOnSelect,
        zIndexMode: K.zIndexMode
      }),
      ...L || Zi
    };
  }, [p.source, p.target, p.sourceHandle, p.targetHandle, p.selected, p.zIndex]), me), Q = ge(() => p.markerStart ? `url('#${cr(p.markerStart, g)}')` : void 0, [p.markerStart, g]), Z = ge(() => p.markerEnd ? `url('#${cr(p.markerEnd, g)}')` : void 0, [p.markerEnd, g]);
  if (p.hidden || A === null || P === null || $ === null || B === null)
    return null;
  const U = (K) => {
    const { addSelectedEdges: te, unselectNodesAndEdges: ie, multiSelectionActive: L } = C.getState();
    j && (C.setState({ nodesSelectionActive: !1 }), p.selected && L ? (ie({ nodes: [], edges: [p] }), H.current?.blur()) : te([e])), r && r(K, p);
  }, se = i ? (K) => {
    i(K, { ...p });
  } : void 0, G = s ? (K) => {
    s(K, { ...p });
  } : void 0, z = c ? (K) => {
    c(K, { ...p });
  } : void 0, q = l ? (K) => {
    l(K, { ...p });
  } : void 0, re = a ? (K) => {
    a(K, { ...p });
  } : void 0, oe = (K) => {
    if (!S && Ls.includes(K.key) && j) {
      const { unselectNodesAndEdges: te, addSelectedEdges: ie } = C.getState();
      K.key === "Escape" ? (H.current?.blur(), te({ edges: [p] })) : ie([e]);
    }
  };
  return h.jsx("svg", { style: { zIndex: _ }, children: h.jsxs("g", { className: be([
    "react-flow__edge",
    `react-flow__edge-${E}`,
    p.className,
    w,
    {
      selected: p.selected,
      animated: p.animated,
      inactive: !j && !r,
      updating: M,
      selectable: j
    }
  ]), onClick: U, onDoubleClick: se, onContextMenu: G, onMouseEnter: z, onMouseMove: q, onMouseLeave: re, onKeyDown: I ? oe : void 0, tabIndex: I ? 0 : void 0, role: p.ariaRole ?? (I ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": p.ariaLabel === null ? void 0 : p.ariaLabel || `Edge from ${p.source} to ${p.target}`, "aria-describedby": I ? `${ya}-${g}` : void 0, ref: H, ...p.domAttributes, children: [!V && h.jsx(N, { id: e, source: p.source, target: p.target, type: p.type, selected: p.selected, animated: p.animated, selectable: j, deletable: p.deletable ?? !0, label: p.label, labelStyle: p.labelStyle, labelShowBg: p.labelShowBg, labelBgStyle: p.labelBgStyle, labelBgPadding: p.labelBgPadding, labelBgBorderRadius: p.labelBgBorderRadius, sourceX: A, sourceY: P, targetX: $, targetY: B, sourcePosition: O, targetPosition: F, data: p.data, style: p.style, sourceHandleId: p.sourceHandle, targetHandleId: p.targetHandle, markerStart: Q, markerEnd: Z, pathOptions: "pathOptions" in p ? p.pathOptions : void 0, interactionWidth: p.interactionWidth }), D && h.jsx(Zp, { edge: p, isReconnectable: D, reconnectRadius: u, onReconnect: d, onReconnectStart: f, onReconnectEnd: m, sourceX: A, sourceY: P, targetX: $, targetY: B, sourcePosition: O, targetPosition: F, setUpdateHover: R, setReconnecting: b })] }) });
}
var Kp = ve(Up);
const Gp = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function Ya({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: i, onEdgeContextMenu: s, onEdgeMouseEnter: c, onEdgeMouseMove: l, onEdgeMouseLeave: a, onEdgeClick: u, reconnectRadius: d, onEdgeDoubleClick: f, onReconnectStart: m, onReconnectEnd: g, disableKeyboardA11y: x }) {
  const { edgesFocusable: w, edgesReconnectable: y, elementsSelectable: S, onError: p } = le(Gp, me), v = Tp(t);
  return h.jsxs("div", { className: "react-flow__edges", children: [h.jsx(Vp, { defaultColor: e, rfId: n }), v.map((E) => h.jsx(Kp, { id: E, edgesFocusable: w, edgesReconnectable: y, elementsSelectable: S, noPanClassName: r, onReconnect: i, onContextMenu: s, onMouseEnter: c, onMouseMove: l, onMouseLeave: a, onClick: u, reconnectRadius: d, onDoubleClick: f, onReconnectStart: m, onReconnectEnd: g, rfId: n, onError: p, edgeTypes: o, disableKeyboardA11y: x }, E))] });
}
Ya.displayName = "EdgeRenderer";
const Qp = ve(Ya), Jp = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function eg({ children: e }) {
  const t = le(Jp);
  return h.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function tg(e) {
  const t = Pr(), n = ae(!1);
  ce(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const ng = (e) => e.panZoom?.syncViewport;
function og(e) {
  const t = le(ng), n = ye();
  return ce(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function rg(e) {
  return e.connection.inProgress ? { ...e.connection, to: $t(e.connection.to, e.transform) } : { ...e.connection };
}
function ig(e) {
  return rg;
}
function sg(e) {
  const t = ig();
  return le(t, me);
}
const ag = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function cg({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: i, height: s, isValid: c, inProgress: l } = le(ag, me);
  return !(i && r && l) ? null : h.jsx("svg", { style: e, width: i, height: s, className: "react-flow__connectionline react-flow__container", children: h.jsx("g", { className: be(["react-flow__connection", Os(c)]), children: h.jsx(Xa, { style: t, type: n, CustomComponent: o, isValid: c }) }) });
}
const Xa = ({ style: e, type: t = ot.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: i, fromNode: s, fromHandle: c, fromPosition: l, to: a, toNode: u, toHandle: d, toPosition: f, pointer: m } = sg();
  if (!r)
    return;
  if (n)
    return h.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: s, fromHandle: c, fromX: i.x, fromY: i.y, toX: a.x, toY: a.y, fromPosition: l, toPosition: f, connectionStatus: Os(o), toNode: u, toHandle: d, pointer: m });
  let g = "";
  const x = {
    sourceX: i.x,
    sourceY: i.y,
    sourcePosition: l,
    targetX: a.x,
    targetY: a.y,
    targetPosition: f
  };
  switch (t) {
    case ot.Bezier:
      [g] = Js(x);
      break;
    case ot.SimpleBezier:
      [g] = Pa(x);
      break;
    case ot.Step:
      [g] = Kn({
        ...x,
        borderRadius: 0
      });
      break;
    case ot.SmoothStep:
      [g] = Kn(x);
      break;
    default:
      [g] = na(x);
  }
  return h.jsx("path", { d: g, fill: "none", className: "react-flow__connection-path", style: e });
};
Xa.displayName = "ConnectionLine";
const lg = {};
function Gi(e = lg) {
  ae(e), ye(), ce(() => {
  }, [e]);
}
function ug() {
  ye(), ae(!1), ce(() => {
  }, []);
}
function qa({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: i, onEdgeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: l, onNodeMouseLeave: a, onNodeContextMenu: u, onSelectionContextMenu: d, onSelectionStart: f, onSelectionEnd: m, connectionLineType: g, connectionLineStyle: x, connectionLineComponent: w, connectionLineContainerStyle: y, selectionKeyCode: S, selectionOnDrag: p, selectionMode: v, multiSelectionKeyCode: E, panActivationKeyCode: N, zoomActivationKeyCode: I, deleteKeyCode: D, onlyRenderVisibleElements: j, elementsSelectable: H, defaultViewport: M, translateExtent: R, minZoom: V, maxZoom: b, preventScrolling: C, defaultMarkerColor: _, zoomOnScroll: A, zoomOnPinch: P, panOnScroll: $, panOnScrollSpeed: B, panOnScrollMode: O, zoomOnDoubleClick: F, panOnDrag: Q, autoPanOnSelection: Z, onPaneClick: U, onPaneMouseEnter: se, onPaneMouseMove: G, onPaneMouseLeave: z, onPaneScroll: q, onPaneContextMenu: re, paneClickDistance: oe, nodeClickDistance: K, onEdgeContextMenu: te, onEdgeMouseEnter: ie, onEdgeMouseMove: L, onEdgeMouseLeave: Y, reconnectRadius: ue, onReconnect: xe, onReconnectStart: he, onReconnectEnd: ze, noDragClassName: Le, noWheelClassName: we, noPanClassName: Ae, disableKeyboardA11y: Xe, nodeExtent: Ie, rfId: De, viewport: He, onViewportChange: Ve }) {
  return Gi(e), Gi(t), ug(), tg(n), og(He), h.jsx(_p, { onPaneClick: U, onPaneMouseEnter: se, onPaneMouseMove: G, onPaneMouseLeave: z, onPaneContextMenu: re, onPaneScroll: q, paneClickDistance: oe, deleteKeyCode: D, selectionKeyCode: S, selectionOnDrag: p, selectionMode: v, onSelectionStart: f, onSelectionEnd: m, multiSelectionKeyCode: E, panActivationKeyCode: N, zoomActivationKeyCode: I, elementsSelectable: H, zoomOnScroll: A, zoomOnPinch: P, zoomOnDoubleClick: F, panOnScroll: $, panOnScrollSpeed: B, panOnScrollMode: O, panOnDrag: Q, autoPanOnSelection: Z, defaultViewport: M, translateExtent: R, minZoom: V, maxZoom: b, onSelectionContextMenu: d, preventScrolling: C, noDragClassName: Le, noWheelClassName: we, noPanClassName: Ae, disableKeyboardA11y: Xe, onViewportChange: Ve, isControlledViewport: !!He, children: h.jsxs(eg, { children: [h.jsx(Qp, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: s, onReconnect: xe, onReconnectStart: he, onReconnectEnd: ze, onlyRenderVisibleElements: j, onEdgeContextMenu: te, onEdgeMouseEnter: ie, onEdgeMouseMove: L, onEdgeMouseLeave: Y, reconnectRadius: ue, defaultMarkerColor: _, noPanClassName: Ae, disableKeyboardA11y: Xe, rfId: De }), h.jsx(cg, { style: x, type: g, component: w, containerStyle: y }), h.jsx("div", { className: "react-flow__edgelabel-renderer" }), h.jsx($p, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: i, onNodeMouseEnter: c, onNodeMouseMove: l, onNodeMouseLeave: a, onNodeContextMenu: u, nodeClickDistance: K, onlyRenderVisibleElements: j, noPanClassName: Ae, noDragClassName: Le, disableKeyboardA11y: Xe, nodeExtent: Ie, rfId: De }), h.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
qa.displayName = "GraphView";
const dg = ve(qa), fg = Xs(), Qi = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: c, minZoom: l = 0.5, maxZoom: a = 2, nodeOrigin: u, nodeExtent: d, zIndexMode: f = "basic" } = {}) => {
  const m = /* @__PURE__ */ new Map(), g = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), y = o ?? t ?? [], S = n ?? e ?? [], p = u ?? [0, 0], v = d ?? Jt;
  ia(x, w, y);
  const { nodesInitialized: E } = lr(S, m, g, {
    nodeOrigin: p,
    nodeExtent: v,
    zIndexMode: f
  });
  let N = [0, 0, 1];
  if (s && r && i) {
    const I = dn(m, {
      filter: (M) => !!((M.width || M.initialWidth) && (M.height || M.initialHeight))
    }), { x: D, y: j, zoom: H } = Cr(I, r, i, l, a, c?.padding ?? 0.1);
    N = [D, j, H];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: i ?? 0,
    transform: N,
    nodes: S,
    nodesInitialized: E,
    nodeLookup: m,
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
    maxZoom: a,
    translateExtent: Jt,
    nodeExtent: v,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: _t.Strict,
    domNode: null,
    paneDragging: !1,
    noPanClassName: "nopan",
    nodeOrigin: p,
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
    fitViewOptions: c,
    fitViewResolver: null,
    connection: { ...Vs },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: fg,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Hs,
    zIndexMode: f,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, hg = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: c, minZoom: l, maxZoom: a, nodeOrigin: u, nodeExtent: d, zIndexMode: f }) => Ch((m, g) => {
  async function x() {
    const { nodeLookup: w, panZoom: y, fitViewOptions: S, fitViewResolver: p, width: v, height: E, minZoom: N, maxZoom: I } = g();
    y && (await Sf({
      nodes: w,
      width: v,
      height: E,
      panZoom: y,
      minZoom: N,
      maxZoom: I
    }, S), p?.resolve(!0), m({ fitViewResolver: null }));
  }
  return {
    ...Qi({
      nodes: e,
      edges: t,
      width: r,
      height: i,
      fitView: s,
      fitViewOptions: c,
      minZoom: l,
      maxZoom: a,
      nodeOrigin: u,
      nodeExtent: d,
      defaultNodes: n,
      defaultEdges: o,
      zIndexMode: f
    }),
    setNodes: (w) => {
      const { nodeLookup: y, parentLookup: S, nodeOrigin: p, elevateNodesOnSelect: v, fitViewQueued: E, zIndexMode: N, nodesSelectionActive: I } = g(), { nodesInitialized: D, hasSelectedNodes: j } = lr(w, y, S, {
        nodeOrigin: p,
        nodeExtent: d,
        elevateNodesOnSelect: v,
        checkEquality: !0,
        zIndexMode: N
      }), H = I && j;
      E && D ? (x(), m({
        nodes: w,
        nodesInitialized: D,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: H
      })) : m({ nodes: w, nodesInitialized: D, nodesSelectionActive: H });
    },
    setEdges: (w) => {
      const { connectionLookup: y, edgeLookup: S } = g();
      ia(y, S, w), m({ edges: w });
    },
    setDefaultNodesAndEdges: (w, y) => {
      if (w) {
        const { setNodes: S } = g();
        S(w), m({ hasDefaultNodes: !0 });
      }
      if (y) {
        const { setEdges: S } = g();
        S(y), m({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (w) => {
      const { triggerNodeChanges: y, nodeLookup: S, parentLookup: p, domNode: v, nodeOrigin: E, nodeExtent: N, debug: I, fitViewQueued: D, zIndexMode: j } = g(), { changes: H, updatedInternals: M } = Yf(w, S, p, v, E, N, j);
      M && (Of(S, p, { nodeOrigin: E, nodeExtent: N, zIndexMode: j }), D ? (x(), m({ fitViewQueued: !1, fitViewOptions: void 0 })) : m({}), H?.length > 0 && (I && console.log("React Flow: trigger node changes", H), y?.(H)));
    },
    updateNodePositions: (w, y = !1) => {
      const S = [];
      let p = [];
      const { nodeLookup: v, triggerNodeChanges: E, connection: N, updateConnection: I, onNodesChangeMiddlewareMap: D } = g();
      for (const [j, H] of w) {
        const M = v.get(j), R = !!(M?.expandParent && M?.parentId && H?.position), V = {
          id: j,
          type: "position",
          position: R ? {
            x: Math.max(0, H.position.x),
            y: Math.max(0, H.position.y)
          } : H.position,
          dragging: y
        };
        if (M && N.inProgress && N.fromNode.id === M.id) {
          const b = gt(M, N.fromHandle, ee.Left, !0);
          I({ ...N, from: b });
        }
        R && M.parentId && S.push({
          id: j,
          parentId: M.parentId,
          rect: {
            ...H.internals.positionAbsolute,
            width: H.measured.width ?? 0,
            height: H.measured.height ?? 0
          }
        }), p.push(V);
      }
      if (S.length > 0) {
        const { parentLookup: j, nodeOrigin: H } = g(), M = jr(S, v, j, H);
        p.push(...M);
      }
      for (const j of D.values())
        p = j(p);
      E(p);
    },
    triggerNodeChanges: (w) => {
      const { onNodesChange: y, setNodes: S, nodes: p, hasDefaultNodes: v, debug: E } = g();
      if (w?.length) {
        if (v) {
          const N = va(w, p);
          S(N);
        }
        E && console.log("React Flow: trigger node changes", w), y?.(w);
      }
    },
    triggerEdgeChanges: (w) => {
      const { onEdgesChange: y, setEdges: S, edges: p, hasDefaultEdges: v, debug: E } = g();
      if (w?.length) {
        if (v) {
          const N = ba(w, p);
          S(N);
        }
        E && console.log("React Flow: trigger edge changes", w), y?.(w);
      }
    },
    addSelectedNodes: (w) => {
      const { multiSelectionActive: y, edgeLookup: S, nodeLookup: p, triggerNodeChanges: v, triggerEdgeChanges: E } = g();
      if (y) {
        const N = w.map((I) => at(I, !0));
        v(N);
        return;
      }
      v(vt(p, /* @__PURE__ */ new Set([...w]), !0)), E(vt(S));
    },
    addSelectedEdges: (w) => {
      const { multiSelectionActive: y, edgeLookup: S, nodeLookup: p, triggerNodeChanges: v, triggerEdgeChanges: E } = g();
      if (y) {
        const N = w.map((I) => at(I, !0));
        E(N);
        return;
      }
      E(vt(S, /* @__PURE__ */ new Set([...w]))), v(vt(p, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: w, edges: y } = {}) => {
      const { edges: S, nodes: p, nodeLookup: v, triggerNodeChanges: E, triggerEdgeChanges: N } = g(), I = w || p, D = y || S, j = [];
      for (const M of I) {
        if (!M.selected)
          continue;
        const R = v.get(M.id);
        R && (R.selected = !1), j.push(at(M.id, !1));
      }
      const H = [];
      for (const M of D)
        M.selected && H.push(at(M.id, !1));
      E(j), N(H);
    },
    setMinZoom: (w) => {
      const { panZoom: y, maxZoom: S } = g();
      y?.setScaleExtent([w, S]), m({ minZoom: w });
    },
    setMaxZoom: (w) => {
      const { panZoom: y, minZoom: S } = g();
      y?.setScaleExtent([S, w]), m({ maxZoom: w });
    },
    setTranslateExtent: (w) => {
      g().panZoom?.setTranslateExtent(w), m({ translateExtent: w });
    },
    resetSelectedElements: () => {
      const { edges: w, nodes: y, triggerNodeChanges: S, triggerEdgeChanges: p, elementsSelectable: v } = g();
      if (!v)
        return;
      const E = y.reduce((I, D) => D.selected ? [...I, at(D.id, !1)] : I, []), N = w.reduce((I, D) => D.selected ? [...I, at(D.id, !1)] : I, []);
      S(E), p(N);
    },
    setNodeExtent: (w) => {
      const { nodes: y, nodeLookup: S, parentLookup: p, nodeOrigin: v, elevateNodesOnSelect: E, nodeExtent: N, zIndexMode: I } = g();
      w[0][0] === N[0][0] && w[0][1] === N[0][1] && w[1][0] === N[1][0] && w[1][1] === N[1][1] || (lr(y, S, p, {
        nodeOrigin: v,
        nodeExtent: w,
        elevateNodesOnSelect: E,
        checkEquality: !1,
        zIndexMode: I
      }), m({ nodeExtent: w }));
    },
    panBy: (w) => {
      const { transform: y, width: S, height: p, panZoom: v, translateExtent: E } = g();
      return Xf({ delta: w, panZoom: v, transform: y, translateExtent: E, width: S, height: p });
    },
    setCenter: async (w, y, S) => {
      const { width: p, height: v, maxZoom: E, panZoom: N } = g();
      if (!N)
        return !1;
      const I = typeof S?.zoom < "u" ? S.zoom : E;
      return await N.setViewport({
        x: p / 2 - w * I,
        y: v / 2 - y * I,
        zoom: I
      }, { duration: S?.duration, ease: S?.ease, interpolate: S?.interpolate }), !0;
    },
    cancelConnection: () => {
      m({
        connection: { ...Vs }
      });
    },
    updateConnection: (w) => {
      m({ connection: w });
    },
    reset: () => m({ ...Qi() })
  };
}, Object.is);
function pg({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: i, initialMinZoom: s, initialMaxZoom: c, initialFitViewOptions: l, fitView: a, nodeOrigin: u, nodeExtent: d, zIndexMode: f, children: m }) {
  const [g] = ne(() => hg({
    nodes: e,
    edges: t,
    defaultNodes: n,
    defaultEdges: o,
    width: r,
    height: i,
    fitView: a,
    minZoom: s,
    maxZoom: c,
    fitViewOptions: l,
    nodeOrigin: u,
    nodeExtent: d,
    zIndexMode: f
  }));
  return h.jsx(Ah, { value: g, children: h.jsx(ep, { children: m }) });
}
function gg({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: i, height: s, fitView: c, fitViewOptions: l, minZoom: a, maxZoom: u, nodeOrigin: d, nodeExtent: f, zIndexMode: m }) {
  return an(lo) ? h.jsx(h.Fragment, { children: e }) : h.jsx(pg, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: i, initialHeight: s, fitView: c, initialFitViewOptions: l, initialMinZoom: a, initialMaxZoom: u, nodeOrigin: d, nodeExtent: f, zIndexMode: m, children: e });
}
const mg = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function yg({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: i, edgeTypes: s, onNodeClick: c, onEdgeClick: l, onInit: a, onMove: u, onMoveStart: d, onMoveEnd: f, onConnect: m, onConnectStart: g, onConnectEnd: x, onClickConnectStart: w, onClickConnectEnd: y, onNodeMouseEnter: S, onNodeMouseMove: p, onNodeMouseLeave: v, onNodeContextMenu: E, onNodeDoubleClick: N, onNodeDragStart: I, onNodeDrag: D, onNodeDragStop: j, onNodesDelete: H, onEdgesDelete: M, onDelete: R, onSelectionChange: V, onSelectionDragStart: b, onSelectionDrag: C, onSelectionDragStop: _, onSelectionContextMenu: A, onSelectionStart: P, onSelectionEnd: $, onBeforeDelete: B, connectionMode: O, connectionLineType: F = ot.Bezier, connectionLineStyle: Q, connectionLineComponent: Z, connectionLineContainerStyle: U, deleteKeyCode: se = "Backspace", selectionKeyCode: G = "Shift", selectionOnDrag: z = !1, selectionMode: q = en.Full, panActivationKeyCode: re = "Space", multiSelectionKeyCode: oe = nn() ? "Meta" : "Control", zoomActivationKeyCode: K = nn() ? "Meta" : "Control", snapToGrid: te, snapGrid: ie, onlyRenderVisibleElements: L = !1, selectNodesOnDrag: Y, nodesDraggable: ue, autoPanOnNodeFocus: xe, nodesConnectable: he, nodesFocusable: ze, nodeOrigin: Le = xa, edgesFocusable: we, edgesReconnectable: Ae, elementsSelectable: Xe = !0, defaultViewport: Ie = Fh, minZoom: De = 0.5, maxZoom: He = 2, translateExtent: Ve = Jt, preventScrolling: mt = !0, nodeExtent: _e, defaultMarkerColor: Tt = "#b1b1b7", zoomOnScroll: Rt = !0, zoomOnPinch: zt = !0, panOnScroll: yt = !1, panOnScrollSpeed: go = 0.5, panOnScrollMode: mo = ut.Free, zoomOnDoubleClick: yo = !0, panOnDrag: xo = !0, onPaneClick: gn, onPaneMouseEnter: mn, onPaneMouseMove: yn, onPaneMouseLeave: wo, onPaneScroll: Lt, onPaneContextMenu: vo, paneClickDistance: bo = 1, nodeClickDistance: So = 0, children: No, onReconnect: Eo, onReconnectStart: Ht, onReconnectEnd: _o, onEdgeContextMenu: Co, onEdgeDoubleClick: ko, onEdgeMouseEnter: Io, onEdgeMouseMove: Mo, onEdgeMouseLeave: Ao, reconnectRadius: Do = 10, onNodesChange: xn, onEdgesChange: wn, noDragClassName: jo = "nodrag", noWheelClassName: Po = "nowheel", noPanClassName: vn = "nopan", fitView: bn, fitViewOptions: Sn, connectOnClick: k, attributionPosition: T, proOptions: W, defaultEdgeOptions: X, elevateNodesOnSelect: J = !0, elevateEdgesOnSelect: de = !1, disableKeyboardA11y: fe = !1, autoPanOnConnect: Se, autoPanOnNodeDrag: je, autoPanOnSelection: st = !0, autoPanSpeed: Ce, connectionRadius: mc, isValidConnection: yc, onError: xc, style: wc, id: Hr, nodeDragThreshold: vc, connectionDragThreshold: bc, viewport: Sc, onViewportChange: Nc, width: Ec, height: _c, colorMode: Cc = "light", debug: kc, onScroll: Vr, ariaLabelConfig: Ic, zIndexMode: Or = "basic", ...Mc }, Ac) {
  const $o = Hr || "1", Dc = qh(Cc), jc = pe((Br) => {
    Br.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), Vr?.(Br);
  }, [Vr]);
  return h.jsx("div", { "data-testid": "rf__wrapper", ...Mc, onScroll: jc, style: { ...wc, ...mg }, ref: Ac, className: be(["react-flow", r, Dc]), id: Hr, role: "application", children: h.jsxs(gg, { nodes: e, edges: t, width: Ec, height: _c, fitView: bn, fitViewOptions: Sn, minZoom: De, maxZoom: He, nodeOrigin: Le, nodeExtent: _e, zIndexMode: Or, children: [h.jsx(Xh, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: m, onConnectStart: g, onConnectEnd: x, onClickConnectStart: w, onClickConnectEnd: y, nodesDraggable: ue, autoPanOnNodeFocus: xe, nodesConnectable: he, nodesFocusable: ze, edgesFocusable: we, edgesReconnectable: Ae, elementsSelectable: Xe, elevateNodesOnSelect: J, elevateEdgesOnSelect: de, minZoom: De, maxZoom: He, nodeExtent: _e, onNodesChange: xn, onEdgesChange: wn, snapToGrid: te, snapGrid: ie, connectionMode: O, translateExtent: Ve, connectOnClick: k, defaultEdgeOptions: X, fitView: bn, fitViewOptions: Sn, onNodesDelete: H, onEdgesDelete: M, onDelete: R, onNodeDragStart: I, onNodeDrag: D, onNodeDragStop: j, onSelectionDrag: C, onSelectionDragStart: b, onSelectionDragStop: _, onMove: u, onMoveStart: d, onMoveEnd: f, noPanClassName: vn, nodeOrigin: Le, rfId: $o, autoPanOnConnect: Se, autoPanOnNodeDrag: je, autoPanSpeed: Ce, onError: xc, connectionRadius: mc, isValidConnection: yc, selectNodesOnDrag: Y, nodeDragThreshold: vc, connectionDragThreshold: bc, onBeforeDelete: B, debug: kc, ariaLabelConfig: Ic, zIndexMode: Or }), h.jsx(dg, { onInit: a, onNodeClick: c, onEdgeClick: l, onNodeMouseEnter: S, onNodeMouseMove: p, onNodeMouseLeave: v, onNodeContextMenu: E, onNodeDoubleClick: N, nodeTypes: i, edgeTypes: s, connectionLineType: F, connectionLineStyle: Q, connectionLineComponent: Z, connectionLineContainerStyle: U, selectionKeyCode: G, selectionOnDrag: z, selectionMode: q, deleteKeyCode: se, multiSelectionKeyCode: oe, panActivationKeyCode: re, zoomActivationKeyCode: K, onlyRenderVisibleElements: L, defaultViewport: Ie, translateExtent: Ve, minZoom: De, maxZoom: He, preventScrolling: mt, zoomOnScroll: Rt, zoomOnPinch: zt, zoomOnDoubleClick: yo, panOnScroll: yt, panOnScrollSpeed: go, panOnScrollMode: mo, panOnDrag: xo, autoPanOnSelection: st, onPaneClick: gn, onPaneMouseEnter: mn, onPaneMouseMove: yn, onPaneMouseLeave: wo, onPaneScroll: Lt, onPaneContextMenu: vo, paneClickDistance: bo, nodeClickDistance: So, onSelectionContextMenu: A, onSelectionStart: P, onSelectionEnd: $, onReconnect: Eo, onReconnectStart: Ht, onReconnectEnd: _o, onEdgeContextMenu: Co, onEdgeDoubleClick: ko, onEdgeMouseEnter: Io, onEdgeMouseMove: Mo, onEdgeMouseLeave: Ao, reconnectRadius: Do, defaultMarkerColor: Tt, noDragClassName: jo, noWheelClassName: Po, noPanClassName: vn, rfId: $o, disableKeyboardA11y: fe, nodeExtent: _e, viewport: Sc, onViewportChange: Nc }), h.jsx(Bh, { onSelectionChange: V }), No, h.jsx(zh, { proOptions: W, position: T }), h.jsx(Rh, { rfId: $o, disableKeyboardA11y: fe })] }) });
}
var xg = Ea(yg);
const wg = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function vg({ children: e }) {
  const t = le(wg);
  return t ? Mh.createPortal(e, t) : null;
}
function bg({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return h.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: be(["react-flow__background-pattern", n, o]) });
}
function Sg({ radius: e, className: t }) {
  return h.jsx("circle", { cx: e, cy: e, r: e, className: be(["react-flow__background-pattern", "dots", t]) });
}
var rt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(rt || (rt = {}));
const Ng = {
  [rt.Dots]: 1,
  [rt.Lines]: 1,
  [rt.Cross]: 6
}, Eg = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function Za({
  id: e,
  variant: t = rt.Dots,
  // only used for dots and cross
  gap: n = 20,
  // only used for lines and cross
  size: o,
  lineWidth: r = 1,
  offset: i = 0,
  color: s,
  bgColor: c,
  style: l,
  className: a,
  patternClassName: u
}) {
  const d = ae(null), { transform: f, patternId: m } = le(Eg, me), g = o || Ng[t], x = t === rt.Dots, w = t === rt.Cross, y = Array.isArray(n) ? n : [n, n], S = [y[0] * f[2] || 1, y[1] * f[2] || 1], p = g * f[2], v = Array.isArray(i) ? i : [i, i], E = w ? [p, p] : S, N = [
    v[0] * f[2] || 1 + E[0] / 2,
    v[1] * f[2] || 1 + E[1] / 2
  ], I = `${m}${e || ""}`;
  return h.jsxs("svg", { className: be(["react-flow__background", a]), style: {
    ...l,
    ...fo,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": s
  }, ref: d, "data-testid": "rf__background", children: [h.jsx("pattern", { id: I, x: f[0] % S[0], y: f[1] % S[1], width: S[0], height: S[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${N[0]},-${N[1]})`, children: x ? h.jsx(Sg, { radius: p / 2, className: u }) : h.jsx(bg, { dimensions: E, lineWidth: r, variant: t, className: u }) }), h.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${I})` })] });
}
Za.displayName = "Background";
const _g = ve(Za);
function Cg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: h.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function kg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: h.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function Ig() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: h.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function Mg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: h.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function Ag() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: h.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function Dn({ children: e, className: t, ...n }) {
  return h.jsx("button", { type: "button", className: be(["react-flow__controls-button", t]), ...n, children: e });
}
const Dg = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function Ua({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: i, onZoomOut: s, onFitView: c, onInteractiveChange: l, className: a, children: u, position: d = "bottom-left", orientation: f = "vertical", "aria-label": m }) {
  const g = ye(), { isInteractive: x, minZoomReached: w, maxZoomReached: y, ariaLabelConfig: S } = le(Dg, me), { zoomIn: p, zoomOut: v, fitView: E } = Pr(), N = () => {
    p(), i?.();
  }, I = () => {
    v(), s?.();
  }, D = () => {
    E(r), c?.();
  }, j = () => {
    g.setState({
      nodesDraggable: !x,
      nodesConnectable: !x,
      elementsSelectable: !x
    }), l?.(!x);
  }, H = f === "horizontal" ? "horizontal" : "vertical";
  return h.jsxs(uo, { className: be(["react-flow__controls", H, a]), position: d, style: e, "data-testid": "rf__controls", "aria-label": m ?? S["controls.ariaLabel"], children: [t && h.jsxs(h.Fragment, { children: [h.jsx(Dn, { onClick: N, className: "react-flow__controls-zoomin", title: S["controls.zoomIn.ariaLabel"], "aria-label": S["controls.zoomIn.ariaLabel"], disabled: y, children: h.jsx(Cg, {}) }), h.jsx(Dn, { onClick: I, className: "react-flow__controls-zoomout", title: S["controls.zoomOut.ariaLabel"], "aria-label": S["controls.zoomOut.ariaLabel"], disabled: w, children: h.jsx(kg, {}) })] }), n && h.jsx(Dn, { className: "react-flow__controls-fitview", onClick: D, title: S["controls.fitView.ariaLabel"], "aria-label": S["controls.fitView.ariaLabel"], children: h.jsx(Ig, {}) }), o && h.jsx(Dn, { className: "react-flow__controls-interactive", onClick: j, title: S["controls.interactive.ariaLabel"], "aria-label": S["controls.interactive.ariaLabel"], children: x ? h.jsx(Ag, {}) : h.jsx(Mg, {}) }), u] });
}
Ua.displayName = "Controls";
const jg = ve(Ua);
function Pg({ id: e, x: t, y: n, width: o, height: r, style: i, color: s, strokeColor: c, strokeWidth: l, className: a, borderRadius: u, shapeRendering: d, selected: f, onClick: m }) {
  const { background: g, backgroundColor: x } = i || {}, w = s || g || x;
  return h.jsx("rect", { className: be(["react-flow__minimap-node", { selected: f }, a]), x: t, y: n, rx: u, ry: u, width: o, height: r, style: {
    fill: w,
    stroke: c,
    strokeWidth: l
  }, shapeRendering: d, onClick: m ? (y) => m(y, e) : void 0 });
}
const $g = ve(Pg), Tg = (e) => e.nodes.map((t) => t.id), Uo = (e) => e instanceof Function ? e : () => e;
function Rg({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: i = $g,
  onClick: s
}) {
  const c = le(Tg, me), l = Uo(t), a = Uo(e), u = Uo(n), d = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return h.jsx(h.Fragment, { children: c.map((f) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    h.jsx(Lg, { id: f, nodeColorFunc: l, nodeStrokeColorFunc: a, nodeClassNameFunc: u, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: i, onClick: s, shapeRendering: d }, f)
  )) });
}
function zg({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: i, shapeRendering: s, NodeComponent: c, onClick: l }) {
  const { node: a, x: u, y: d, width: f, height: m } = le((g) => {
    const x = g.nodeLookup.get(e);
    if (!x)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const w = x.internals.userNode, { x: y, y: S } = x.internals.positionAbsolute, { width: p, height: v } = et(w);
    return {
      node: w,
      x: y,
      y: S,
      width: p,
      height: v
    };
  }, me);
  return !a || a.hidden || !qs(a) ? null : h.jsx(c, { x: u, y: d, width: f, height: m, style: a.style, selected: !!a.selected, className: o(a), color: t(a), borderRadius: r, strokeColor: n(a), strokeWidth: i, shapeRendering: s, onClick: l, id: a.id });
}
const Lg = ve(zg);
var Hg = ve(Rg);
const Vg = 200, Og = 150, Bg = (e) => !e.hidden, Fg = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? Ys(dn(e.nodeLookup, { filter: Bg }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, Wg = "react-flow__minimap-desc";
function Ka({
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
  nodeComponent: c,
  bgColor: l,
  maskColor: a,
  maskStrokeColor: u,
  maskStrokeWidth: d,
  position: f = "bottom-right",
  onClick: m,
  onNodeClick: g,
  pannable: x = !1,
  zoomable: w = !1,
  ariaLabel: y,
  inversePan: S,
  zoomStep: p = 1,
  offsetScale: v = 5
}) {
  const E = ye(), N = ae(null), { boundingRect: I, viewBB: D, rfId: j, panZoom: H, translateExtent: M, flowWidth: R, flowHeight: V, ariaLabelConfig: b } = le(Fg, me), C = e?.width ?? Vg, _ = e?.height ?? Og, A = I.width / C, P = I.height / _, $ = Math.max(A, P), B = $ * C, O = $ * _, F = v * $, Q = I.x - (B - I.width) / 2 - F, Z = I.y - (O - I.height) / 2 - F, U = B + F * 2, se = O + F * 2, G = `${Wg}-${j}`, z = ae(0), q = ae();
  z.current = $, ce(() => {
    if (N.current && H)
      return q.current = th({
        domNode: N.current,
        panZoom: H,
        getTransform: () => E.getState().transform,
        getViewScale: () => z.current
      }), () => {
        q.current?.destroy();
      };
  }, [H]), ce(() => {
    q.current?.update({
      translateExtent: M,
      width: R,
      height: V,
      inversePan: S,
      pannable: x,
      zoomStep: p,
      zoomable: w
    });
  }, [x, w, S, p, M, R, V]);
  const re = m ? (te) => {
    const [ie, L] = q.current?.pointer(te) || [0, 0];
    m(te, { x: ie, y: L });
  } : void 0, oe = g ? pe((te, ie) => {
    const L = E.getState().nodeLookup.get(ie).internals.userNode;
    g(te, L);
  }, []) : void 0, K = y ?? b["minimap.ariaLabel"];
  return h.jsx(uo, { position: f, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-background-color-props": typeof a == "string" ? a : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof d == "number" ? d * $ : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof s == "number" ? s : void 0
  }, className: be(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: h.jsxs("svg", { width: C, height: _, viewBox: `${Q} ${Z} ${U} ${se}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": G, ref: N, onClick: re, children: [K && h.jsx("title", { id: G, children: K }), h.jsx(Hg, { onClick: oe, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: i, nodeClassName: r, nodeStrokeWidth: s, nodeComponent: c }), h.jsx("path", { className: "react-flow__minimap-mask", d: `M${Q - F},${Z - F}h${U + F * 2}v${se + F * 2}h${-U - F * 2}z
        M${D.x},${D.y}h${D.width}v${D.height}h${-D.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
Ka.displayName = "MiniMap";
const Yg = ve(Ka), Xg = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, qg = {
  [Mt.Line]: "right",
  [Mt.Handle]: "bottom-right"
};
function Zg({ nodeId: e, position: t, variant: n = Mt.Handle, className: o, style: r = void 0, children: i, color: s, minWidth: c = 10, minHeight: l = 10, maxWidth: a = Number.MAX_VALUE, maxHeight: u = Number.MAX_VALUE, keepAspectRatio: d = !1, resizeDirection: f, autoScale: m = !0, shouldResize: g, onResizeStart: x, onResize: w, onResizeEnd: y }) {
  const S = Ia(), p = typeof e == "string" ? e : S, v = ye(), E = ae(null), N = n === Mt.Handle, I = le(pe(Xg(N && m), [N, m]), me), D = ae(null), j = t ?? qg[n];
  ce(() => {
    if (!(!E.current || !p))
      return D.current || (D.current = ph({
        domNode: E.current,
        nodeId: p,
        getStoreItems: () => {
          const { nodeLookup: M, transform: R, snapGrid: V, snapToGrid: b, nodeOrigin: C, domNode: _ } = v.getState();
          return {
            nodeLookup: M,
            transform: R,
            snapGrid: V,
            snapToGrid: b,
            nodeOrigin: C,
            paneDomNode: _
          };
        },
        onChange: (M, R) => {
          const { triggerNodeChanges: V, nodeLookup: b, parentLookup: C, nodeOrigin: _ } = v.getState(), A = [], P = { x: M.x, y: M.y }, $ = b.get(p);
          if ($ && $.expandParent && $.parentId) {
            const B = $.origin ?? _, O = M.width ?? $.measured.width ?? 0, F = M.height ?? $.measured.height ?? 0, Q = {
              id: $.id,
              parentId: $.parentId,
              rect: {
                width: O,
                height: F,
                ...Zs({
                  x: M.x ?? $.position.x,
                  y: M.y ?? $.position.y
                }, { width: O, height: F }, $.parentId, b, B)
              }
            }, Z = jr([Q], b, C, _);
            A.push(...Z), P.x = M.x ? Math.max(B[0] * O, M.x) : void 0, P.y = M.y ? Math.max(B[1] * F, M.y) : void 0;
          }
          if (P.x !== void 0 && P.y !== void 0) {
            const B = {
              id: p,
              type: "position",
              position: { ...P }
            };
            A.push(B);
          }
          if (M.width !== void 0 && M.height !== void 0) {
            const O = {
              id: p,
              type: "dimensions",
              resizing: !0,
              setAttributes: f ? f === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: M.width,
                height: M.height
              }
            };
            A.push(O);
          }
          for (const B of R) {
            const O = {
              ...B,
              type: "position"
            };
            A.push(O);
          }
          V(A);
        },
        onEnd: ({ width: M, height: R }) => {
          const V = {
            id: p,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: M,
              height: R
            }
          };
          v.getState().triggerNodeChanges([V]);
        }
      })), D.current.update({
        controlPosition: j,
        boundaries: {
          minWidth: c,
          minHeight: l,
          maxWidth: a,
          maxHeight: u
        },
        keepAspectRatio: d,
        resizeDirection: f,
        onResizeStart: x,
        onResize: w,
        onResizeEnd: y,
        shouldResize: g
      }), () => {
        D.current?.destroy();
      };
  }, [
    j,
    c,
    l,
    a,
    u,
    d,
    x,
    w,
    y,
    g
  ]);
  const H = j.split("-");
  return h.jsx("div", { className: be(["react-flow__resize-control", "nodrag", ...H, n, o]), ref: E, style: {
    ...r,
    scale: I,
    ...s && { [N ? "backgroundColor" : "borderColor"]: s }
  }, children: i });
}
ve(Zg);
const Ug = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), Ga = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var Kg = {
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
const Gg = eo(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: o,
    className: r = "",
    children: i,
    iconNode: s,
    ...c
  }, l) => Qo(
    "svg",
    {
      ref: l,
      ...Kg,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: Ga("lucide", r),
      ...c
    },
    [
      ...s.map(([a, u]) => Qo(a, u)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const ke = (e, t) => {
  const n = eo(
    ({ className: o, ...r }, i) => Qo(Gg, {
      ref: i,
      iconNode: t,
      className: Ga(`lucide-${Ug(e)}`, o),
      ...r
    })
  );
  return n.displayName = `${e}`, n;
};
const Qg = ke("Boxes", [
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
const ho = ke("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const Jg = ke("ChevronDown", [
  ["path", { d: "m6 9 6 6 6-6", key: "qrunsl" }]
]);
const em = ke("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const Ln = ke("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const rn = ke("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const tm = ke("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const nm = ke("ListTree", [
  ["path", { d: "M21 12h-8", key: "1bmf0i" }],
  ["path", { d: "M21 6H8", key: "1pqkrb" }],
  ["path", { d: "M21 18h-8", key: "1tm79t" }],
  ["path", { d: "M3 6v4c0 1.1.9 2 2 2h3", key: "1ywdgy" }],
  ["path", { d: "M3 10v6c0 1.1.9 2 2 2h3", key: "2wc746" }]
]);
const Qa = ke("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const Tr = ke("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const om = ke("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const rm = ke("Save", [
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
const im = ke("Search", [
  ["circle", { cx: "11", cy: "11", r: "8", key: "4ej97u" }],
  ["path", { d: "m21 21-4.3-4.3", key: "1qie3q" }]
]);
const Dt = ke("Sparkles", [
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
const fr = ke("Trash2", [
  ["path", { d: "M3 6h18", key: "d0wm0j" }],
  ["path", { d: "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", key: "4alrt4" }],
  ["path", { d: "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", key: "v07s0e" }],
  ["line", { x1: "10", x2: "10", y1: "11", y2: "17", key: "1uufr5" }],
  ["line", { x1: "14", x2: "14", y1: "11", y2: "17", key: "xtxkd" }]
]), Re = "/_elsa/workflow-management";
async function sm(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${Re}/definitions?${n.toString()}`);
}
async function am(e, t) {
  return e.http.getJson(`${Re}/definitions/${encodeURIComponent(t)}`);
}
async function cm(e, t) {
  return e.http.postJson(`${Re}/definitions`, t);
}
async function lm(e, t) {
  await e.http.deleteJson(`${Re}/definitions/${encodeURIComponent(t)}`);
}
async function um(e, t) {
  await e.http.postJson(`${Re}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function dm(e, t) {
  await e.http.deleteJson(`${Re}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function fm(e, t) {
  return e.http.putJson(`${Re}/drafts/${encodeURIComponent(t.id)}`, { state: t.state, layout: t.layout });
}
async function hm(e, t) {
  return e.http.postJson(`${Re}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function pm(e, t) {
  return e.http.postJson(`${Re}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function Ja(e, t) {
  return e.http.postJson(`${Re}/executables/${encodeURIComponent(t)}/run`, {});
}
async function gm(e) {
  return e.http.getJson("/_demo/workflows/executables");
}
async function ec(e) {
  return e.http.getJson(`${Re}/activities`);
}
async function mm(e) {
  const t = await tc(e, [
    `${Re}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? Ji(t) : Ji(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function ym(e) {
  const t = await tc(e, [
    `${Re}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : Hn;
}
async function tc(e, t) {
  let n;
  for (const o of t)
    try {
      return await e.http.getJson(o);
    } catch (r) {
      n = r;
    }
  throw n;
}
function Ji(e) {
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
const Hn = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
], po = "elsa.sequence.structure", pn = "elsa.flowchart.structure";
function nc(e, t) {
  if (!e) return null;
  let n = e;
  for (const o of t) {
    const r = Ue(n).find((s) => s.id === o.slotId);
    if (!r) return null;
    const i = r.activities.find((s) => s.nodeId === o.ownerNodeId);
    if (!i) return null;
    n = i;
  }
  return n;
}
function Ko(e, t) {
  const n = nc(e, t);
  if (!n) return null;
  let o = Ue(n)[0];
  return o ? { owner: n, slot: o } : null;
}
function Ue(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = zm(t), r = Go(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: Lm(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, i]) => Go(i)).map(([i, s]) => ({
    id: `${t.kind}:${i}`,
    label: Vm(i),
    property: i,
    mode: "generic",
    activities: Go(s) ?? []
  }));
}
function xm(e, t, n) {
  const o = new Map(t.map((s) => [s.activityVersionId, s])), r = new Map(n.map((s) => [s.nodeId, s])), i = e.slot.activities.map((s, c) => {
    const l = o.get(s.activityVersionId), a = r.get(s.nodeId) ?? Hm(e.slot.mode, c);
    return ic(s, l, { x: a.x, y: a.y });
  });
  return {
    nodes: i,
    edges: e.slot.mode === "flowchart" ? Am(e.owner) : Mm(e.slot, i)
  };
}
function wm(e, t, n) {
  const o = t.find((i) => i.activityVersionId === e.activityVersionId), r = n.find((i) => i.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [ic(e, o, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function vm(e, t) {
  return e?.structure?.kind === pn || Em(t) ? "flowchart" : e?.structure?.kind === po || _m(t) ? "sequence" : "unsupported";
}
function hr(e, t, n) {
  if (t.length === 0) {
    const c = Ue(e)[0];
    return c ? sn(e, c, n) : e;
  }
  const [o, ...r] = t, i = Ue(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((c) => c.nodeId === o.ownerNodeId ? hr(c, r, n) : c);
  return sn(e, i, s);
}
function oc(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, i = Ue(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((c) => c.nodeId === o.ownerNodeId ? oc(c, r, n) : c);
  return sn(e, i, s);
}
function rc(e, t, n) {
  if (e.nodeId === t) return n(e);
  const o = Ue(e);
  if (o.length === 0) return e;
  let r = !1, i = e;
  for (const s of o) {
    const c = s.activities.map((l) => {
      const a = rc(l, t, n);
      return a !== l && (r = !0), a;
    });
    r && (i = sn(i, s, c));
  }
  return r ? i : e;
}
function sn(e, t, n) {
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
function bm(e, t, n, o = []) {
  const r = new Map(e.slot.activities.map((s) => [s.nodeId, s]));
  for (const s of o)
    r.set(s.nodeId, s);
  const i = t.map((s) => r.get(s.id)).filter((s) => !!s);
  return e.slot.mode === "sequence" && i.sort((s, c) => {
    const l = t.find((u) => u.id === s.nodeId), a = t.find((u) => u.id === c.nodeId);
    return (l?.position.x ?? 0) - (a?.position.x ?? 0);
  }), sn(e.owner, e.slot, i);
}
function Sm(e, t) {
  return {
    ...e,
    structure: Im(e.structure, t)
  };
}
function Nm(e, t) {
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
function es(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: km(e)
  };
}
function Ne(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? Cm(t) : n;
}
function ic(e, t, n, o = {}) {
  return {
    id: e.nodeId,
    type: "workflowActivity",
    position: n,
    connectable: o.connectable,
    deletable: o.deletable,
    draggable: o.draggable,
    data: {
      label: t ? Ne(t) : e.activityVersionId,
      activityVersionId: e.activityVersionId,
      activityTypeKey: t?.activityTypeKey,
      childSlots: Ue(e),
      acceptsInbound: Dm(e, t),
      sourcePorts: o.suppressFlowPorts ? [] : sc(e, t),
      suppressFlowPorts: o.suppressFlowPorts
    }
  };
}
function Em(e) {
  return !!e && (Ne(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function _m(e) {
  return !!e && (Ne(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function Cm(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function km(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: po,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: pn,
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
function Im(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], o = /* @__PURE__ */ new Map();
  for (const r of n) {
    if (!Rr(r)) continue;
    const i = r.id;
    typeof i == "string" && o.set(i, r);
  }
  return {
    ...e,
    payload: {
      ...e.payload,
      connections: t.map((r) => {
        const i = o.get(r.id) ?? {}, s = r.data?.vertices, { vertices: c, ...l } = i;
        return {
          ...l,
          id: r.id,
          source: { nodeId: r.source, port: r.sourceHandle ?? "Done" },
          target: r.targetHandle ? { nodeId: r.target, port: r.targetHandle } : { nodeId: r.target },
          ...s?.length ? { vertices: s.map((a) => ({ x: Math.round(a.x), y: Math.round(a.y) })) } : {}
        };
      })
    }
  };
}
function Mm(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function Am(e) {
  if (e.structure?.kind !== pn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, i = n.target;
    if (!r?.nodeId || !i?.nodeId) return null;
    const s = Array.isArray(n.vertices) ? n.vertices.filter(Tm) : [];
    return {
      id: typeof n.id == "string" ? String(n.id) : `flow-${o}-${r.nodeId}-${i.nodeId}`,
      source: r.nodeId,
      target: i.nodeId,
      sourceHandle: r.port,
      targetHandle: i.port && i.port !== "Done" ? i.port : void 0,
      type: "workflow",
      label: r.port && r.port !== "Done" ? r.port : void 0,
      data: s.length ? { vertices: s } : void 0
    };
  }).filter((n) => !!n) : [];
}
function sc(e, t) {
  const n = ts(e.cases);
  if (Pm(e, t) && n.length > 0)
    return [...n.map((i) => ({ name: i, displayName: i })), { name: "Default", displayName: "Default" }];
  const o = [
    ...Vn(t?.designFacets),
    ...Vn(t?.ports),
    ...Vn(t?.outputs)
  ];
  if (o.length > 0) return $m(o);
  const r = ts(e.outcomes);
  return r.length > 0 ? r.map((i) => ({ name: i, displayName: i })) : [{ name: "Done", displayName: "Done" }];
}
function Dm(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function Qn(e, t, n, o) {
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
function jm(e, t, n) {
  const o = Qn(t.source, n, t.sourceHandle ?? "Done", void 0), r = Qn(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((i) => i.id !== t.id).concat(o, r);
}
function Go(e) {
  return Array.isArray(e) ? e.filter(Rm) : null;
}
function Pm(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function Vn(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!Rr(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...Vn(n.ports));
      continue;
    }
    const o = typeof n.type == "string" ? n.type : typeof n.portType == "string" ? n.portType : "", r = n.isBrowsable !== !1 && n.browsable !== !1, i = typeof n.name == "string" ? n.name : typeof n.id == "string" ? n.id : "";
    if (r && o.toLowerCase() === "flow" && i) {
      const s = typeof n.displayName == "string" ? n.displayName : i;
      t.push({ name: i, displayName: s });
    }
  }
  return t;
}
function $m(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function ts(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function Tm(e) {
  return Rr(e) && typeof e.x == "number" && typeof e.y == "number";
}
function Rr(e) {
  return typeof e == "object" && e !== null;
}
function Rm(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function zm(e) {
  return e.kind === po ? "sequence" : e.kind === pn ? "flowchart" : "generic";
}
function Lm(e) {
  return e.kind === po || e.kind === pn, "Activities";
}
function Hm(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function Vm(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Om = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function Bm(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function zr(e) {
  return Bm(e.name);
}
function Fm(e, t) {
  const n = zr(t), o = e[n];
  return t.isWrapped === !1 ? o ?? t.defaultValue ?? "" : cc(o, t);
}
function ac(e, t) {
  return cc(e[zr(t)], t);
}
function Wm(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function Ym(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function ns(e, t, n) {
  return {
    ...e,
    [zr(t)]: n
  };
}
function Xm(e, t) {
  return t.isWrapped === !1 ? Fm(e, t) : ac(e, t).expression.value;
}
function cc(e, t) {
  return qm(e) ? {
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
function qm(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
function Zm({
  activity: e,
  descriptor: t,
  editors: n,
  expressionDescriptors: o,
  descriptorStatus: r,
  onChange: i
}) {
  if (r === "loading")
    return /* @__PURE__ */ h.jsx("p", { className: "wf-muted", children: "Loading activity properties..." });
  if (!t)
    return /* @__PURE__ */ h.jsx("p", { className: "wf-muted", children: "No activity descriptor is available for this activity." });
  const s = t.inputs.filter((a) => a.isBrowsable !== !1).sort((a, u) => (a.order ?? 0) - (u.order ?? 0) || a.name.localeCompare(u.name));
  if (s.length === 0)
    return /* @__PURE__ */ h.jsx("p", { className: "wf-muted", children: "This activity does not expose editable properties." });
  const c = Qm(s), l = o.length > 0 ? o : Om;
  return /* @__PURE__ */ h.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ h.jsx("span", { className: "wf-section-label", children: "Properties" }),
    c.map((a) => /* @__PURE__ */ h.jsxs("section", { className: "wf-property-group", children: [
      c.length > 1 ? /* @__PURE__ */ h.jsx("h4", { children: a.category }) : null,
      a.inputs.map((u) => /* @__PURE__ */ h.jsx(
        Um,
        {
          activity: e,
          input: u,
          editors: n,
          expressionDescriptors: l,
          onChange: i
        },
        u.name
      ))
    ] }, a.category))
  ] });
}
function Um({
  activity: e,
  input: t,
  editors: n,
  expressionDescriptors: o,
  onChange: r
}) {
  const i = t.isReadOnly === !0, s = { activity: e, expressionDescriptors: o, readOnly: i }, l = Gm(n, t, s)?.component, a = t.isWrapped !== !1 ? ac(e, t) : null, u = a?.expression.type ?? "Literal", d = Xm(e, t), f = (g) => {
    const x = a ? Wm(a, g) : g;
    r(ns(e, t, x));
  }, m = (g) => {
    a && r(ns(e, t, Ym(a, g)));
  };
  return /* @__PURE__ */ h.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ h.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ h.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ h.jsx("span", { children: Jm(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ h.jsx("p", { children: t.description }) : null,
    a ? /* @__PURE__ */ h.jsx(
      Km,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: u,
        descriptors: o,
        disabled: i,
        onChange: m
      }
    ) : null,
    l ? /* @__PURE__ */ h.jsx(
      l,
      {
        descriptor: t,
        value: d,
        disabled: i,
        context: s,
        onChange: f
      }
    ) : /* @__PURE__ */ h.jsx("input", { type: "text", value: d == null ? "" : String(d), disabled: i, onChange: (g) => f(g.target.value) })
  ] });
}
function Km({
  label: e,
  value: t,
  descriptors: n,
  disabled: o,
  onChange: r
}) {
  const [i, s] = ne(!1), c = $c(), l = n.find((a) => a.type === t);
  return /* @__PURE__ */ h.jsxs("div", { className: "wf-syntax-picker", onBlur: (a) => {
    a.currentTarget.contains(a.relatedTarget) || s(!1);
  }, children: [
    /* @__PURE__ */ h.jsx(
      "button",
      {
        type: "button",
        className: i ? "wf-syntax-picker-trigger open" : "wf-syntax-picker-trigger",
        "aria-label": e,
        "aria-haspopup": "listbox",
        "aria-expanded": i,
        "aria-controls": c,
        disabled: o,
        onClick: () => s((a) => !a),
        children: /* @__PURE__ */ h.jsx("span", { children: l?.displayName || l?.type || t })
      }
    ),
    i ? /* @__PURE__ */ h.jsx("div", { id: c, role: "listbox", className: "wf-syntax-picker-menu", "aria-label": e, children: n.map((a) => {
      const u = a.displayName || a.type, d = a.type === t;
      return /* @__PURE__ */ h.jsx(
        "button",
        {
          type: "button",
          role: "option",
          "aria-selected": d,
          className: d ? "selected" : "",
          onClick: () => {
            r(a.type), s(!1);
          },
          children: u
        },
        a.type
      );
    }) }) : null
  ] });
}
function Gm(e, t, n) {
  return [...e].sort((o, r) => (o.order ?? 500) - (r.order ?? 500)).find((o) => o.supports(t, n));
}
function Qm(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "General";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return [...t.entries()].map(([n, o]) => ({ category: n, inputs: o }));
}
function Jm(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
const ey = { workflowActivity: _y }, ty = { workflow: Cy }, os = "application/x-elsa-activity-version-id", ny = 6, oy = 1200, ry = [10, 25, 50], iy = 10, lc = it.createContext(null);
function Py(e) {
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
        component: () => /* @__PURE__ */ h.jsx(sy, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list() })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ h.jsx(ay, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow instances",
        component: () => /* @__PURE__ */ h.jsx(cy, { ai: e.ai })
      }
    ]
  });
}
function sy({
  context: e,
  ai: t,
  propertyEditors: n
}) {
  const [o, r] = ne(rs);
  ce(() => {
    const s = () => r(rs());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const i = (s) => {
    const c = s ? `/workflows/definitions?definition=${encodeURIComponent(s)}` : "/workflows/definitions";
    window.history.pushState({}, "", c), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return o ? /* @__PURE__ */ h.jsx(Ey, { context: e, definitionId: o, ai: t, propertyEditors: n, onBack: () => i(null) }) : /* @__PURE__ */ h.jsx(Lr, { activePath: "/workflows/definitions", title: "Definitions", children: /* @__PURE__ */ h.jsx(ly, { context: e, ai: t, onOpen: i }) });
}
function ay({ context: e, ai: t }) {
  const [n, o] = ne(is);
  return ce(() => {
    const r = () => o(is());
    return window.addEventListener("popstate", r), () => window.removeEventListener("popstate", r);
  }, []), /* @__PURE__ */ h.jsx(Lr, { activePath: "/workflows/executables", title: "Executables", children: /* @__PURE__ */ h.jsx(dy, { context: e, ai: t, definitionFilter: n }) });
}
function cy({ ai: e }) {
  const t = jt(e, "weaver.workflows.explain-instance");
  return /* @__PURE__ */ h.jsx(Lr, { activePath: "/workflows/instances", title: "Instances", children: /* @__PURE__ */ h.jsxs("div", { className: "wf-empty", children: [
    "Workflow instance history will appear here when the runtime exposes an instance query endpoint.",
    t ? /* @__PURE__ */ h.jsxs("button", { type: "button", className: "wf-ai-inline-action", onClick: () => Pt(e, t, { scope: "workflow-instances" }), children: [
      /* @__PURE__ */ h.jsx(Dt, { size: 13 }),
      " Ask Weaver about instances"
    ] }) : null
  ] }) });
}
function Lr({ activePath: e, title: t, children: n }) {
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
function rs() {
  return new URLSearchParams(window.location.search).get("definition");
}
function is() {
  return new URLSearchParams(window.location.search).get("definition");
}
function ly({ context: e, ai: t, onOpen: n }) {
  const [o, r] = ne(""), [i, s] = ne("active"), [c, l] = ne(1), [a, u] = ne(iy), [d, f] = ne("loading"), [m, g] = ne(""), [x, w] = ne(""), [y, S] = ne([]), [p, v] = ne(0), [E, N] = ne(() => /* @__PURE__ */ new Set()), [I, D] = ne(null), [j, H] = ne(!1), [M, R] = ne([]), [V, b] = ne("idle"), C = ae(null), _ = ge(() => y.map((L) => L.id), [y]), A = jt(t, "weaver.workflows.suggest-create-metadata"), P = jt(t, "weaver.workflows.explain-definition"), $ = _.filter((L) => E.has(L)).length, B = _.length > 0 && $ === _.length, O = pe(async () => {
    f("loading"), g("");
    try {
      const L = await sm(e, { search: o, state: i, page: c, pageSize: a }), Y = typeof L.totalCount == "number", ue = L.totalCount ?? L.definitions.length, xe = uc(ue, a);
      if (ue > 0 && c > xe) {
        l(xe);
        return;
      }
      S(Y ? L.definitions : hy(L.definitions, c, a)), v(ue), f("ready");
    } catch (L) {
      g(L instanceof Error ? L.message : String(L)), f("failed");
    }
  }, [e, o, i, c, a]);
  ce(() => {
    O();
  }, [O]), ce(() => {
    C.current && (C.current.indeterminate = $ > 0 && !B);
  }, [B, $]);
  const F = pe(async () => {
    if (!(V === "loading" || V === "ready")) {
      b("loading");
      try {
        const L = await ec(e);
        R(L.activities ?? []), b("ready");
      } catch (L) {
        b("failed"), g(L instanceof Error ? L.message : String(L));
      }
    }
  }, [V, e]), Q = () => {
    g(""), w(""), D({ name: "", description: "", rootKind: "flowchart" }), F();
  }, Z = async () => {
    if (I?.name.trim()) {
      H(!0), g(""), w("");
      try {
        const L = await cm(e, {
          name: I.name.trim(),
          description: I.description.trim() || null,
          rootKind: I.rootKind,
          rootActivityVersionId: my(I, M)
        });
        D(null), n(L.definition.id);
      } catch (L) {
        g(L instanceof Error ? L.message : String(L));
      } finally {
        H(!1);
      }
    }
  }, U = (L) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(L)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, se = async () => {
    if (y.length === 1 && c > 1) {
      l(c - 1);
      return;
    }
    await O();
  }, G = () => N(/* @__PURE__ */ new Set()), z = (L, Y) => {
    N((ue) => {
      const xe = new Set(ue);
      return Y ? xe.add(L) : xe.delete(L), xe;
    });
  }, q = (L) => {
    N((Y) => {
      const ue = new Set(Y);
      for (const xe of _)
        L ? ue.add(xe) : ue.delete(xe);
      return ue;
    });
  }, re = (L) => {
    s(L), l(1), G();
  }, oe = (L) => {
    r(L), l(1), G();
  }, K = async (L) => {
    if (window.confirm(`Delete workflow definition "${L.name}"? You can restore it from the Deleted view.`)) {
      w(""), g("");
      try {
        await lm(e, L.id), z(L.id, !1), w(`Deleted ${L.name}`), await se();
      } catch (Y) {
        g(Y instanceof Error ? Y.message : String(Y));
      }
    }
  }, te = async (L) => {
    w(""), g("");
    try {
      await um(e, L.id), z(L.id, !1), w(`Restored ${L.name}`), await se();
    } catch (Y) {
      g(Y instanceof Error ? Y.message : String(Y));
    }
  }, ie = async (L) => {
    if (window.confirm(`Permanently delete workflow definition "${L.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`)) {
      w(""), g("");
      try {
        await dm(e, L.id), z(L.id, !1), w(`Permanently deleted ${L.name}`), await se();
      } catch (Y) {
        g(Y instanceof Error ? Y.message : String(Y));
      }
    }
  };
  return /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
    /* @__PURE__ */ h.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ h.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ h.jsx("button", { type: "button", className: i === "active" ? "active" : "", "aria-selected": i === "active", onClick: () => re("active"), children: "Active" }),
        /* @__PURE__ */ h.jsx("button", { type: "button", className: i === "deleted" ? "active" : "", "aria-selected": i === "deleted", onClick: () => re("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ h.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ h.jsx(im, { size: 15 }),
        /* @__PURE__ */ h.jsx("input", { value: o, onChange: (L) => oe(L.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
        O();
      }, children: "Refresh" }),
      /* @__PURE__ */ h.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ h.jsxs("button", { type: "button", title: "Create workflow", onClick: Q, children: [
        /* @__PURE__ */ h.jsx(Tr, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    d === "failed" ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(rn, { size: 16 }),
      " ",
      m
    ] }) : null,
    d !== "failed" && m ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(rn, { size: 16 }),
      " ",
      m
    ] }) : null,
    x ? /* @__PURE__ */ h.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ h.jsx(ho, { size: 14 }),
      " ",
      x
    ] }) : null,
    E.size > 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ h.jsxs("span", { children: [
        E.size,
        " selected"
      ] }),
      /* @__PURE__ */ h.jsx("button", { type: "button", onClick: G, children: "Clear selection" })
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
              ref: C,
              type: "checkbox",
              checked: B,
              onChange: (L) => q(L.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ h.jsx("span", { children: "Name" }),
          /* @__PURE__ */ h.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ h.jsx("span", { children: i === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ h.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ h.jsx("span", { children: "Actions" })
        ] }),
        y.map((L) => /* @__PURE__ */ h.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${L.name}`,
            "aria-selected": E.has(L.id),
            tabIndex: 0,
            onClick: () => n(L.id),
            onKeyDown: (Y) => {
              Y.currentTarget === Y.target && (Y.key !== "Enter" && Y.key !== " " || (Y.preventDefault(), n(L.id)));
            },
            children: [
              /* @__PURE__ */ h.jsx("label", { className: "wf-row-select", onClick: (Y) => Y.stopPropagation(), children: /* @__PURE__ */ h.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: E.has(L.id),
                  onChange: (Y) => z(L.id, Y.target.checked),
                  "aria-label": `Select workflow definition ${L.name}`
                }
              ) }),
              /* @__PURE__ */ h.jsxs("span", { children: [
                /* @__PURE__ */ h.jsx("strong", { children: L.name }),
                /* @__PURE__ */ h.jsx("small", { children: L.description || L.id })
              ] }),
              /* @__PURE__ */ h.jsx("span", { children: L.latestVersion ?? "No version" }),
              /* @__PURE__ */ h.jsx("span", { children: i === "deleted" ? pr(L.deletedAt) : L.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ h.jsx("span", { children: pr(L.lastModifiedAt) }),
              /* @__PURE__ */ h.jsx("span", { className: "wf-row-actions", onClick: (Y) => Y.stopPropagation(), children: i === "active" ? /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
                /* @__PURE__ */ h.jsx("button", { type: "button", onClick: (Y) => {
                  Y.stopPropagation(), n(L.id);
                }, children: "Open" }),
                /* @__PURE__ */ h.jsx("button", { type: "button", onClick: (Y) => {
                  Y.stopPropagation(), U(L.id);
                }, children: "Artifacts" }),
                P ? /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => Pt(t, P, L), children: [
                  /* @__PURE__ */ h.jsx(Dt, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ h.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  K(L);
                }, children: [
                  /* @__PURE__ */ h.jsx(fr, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
                /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
                  te(L);
                }, children: [
                  /* @__PURE__ */ h.jsx(om, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ h.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  ie(L);
                }, children: [
                  /* @__PURE__ */ h.jsx(fr, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          L.id
        ))
      ] }),
      /* @__PURE__ */ h.jsx(
        fy,
        {
          page: c,
          pageSize: a,
          totalCount: p,
          onPageChange: l,
          onPageSizeChange: (L) => {
            u(L), l(1);
          }
        }
      )
    ] }) : null,
    I ? /* @__PURE__ */ h.jsx(
      uy,
      {
        draft: I,
        activities: M,
        catalogState: V,
        creating: j,
        suggestMetadataAction: A,
        onSuggestMetadata: A ? () => Pt(t, A, { draft: I, activities: M }) : void 0,
        onChange: (L) => D(L),
        onClose: () => D(null),
        onSubmit: Z
      }
    ) : null
  ] });
}
function uy({ draft: e, activities: t, catalogState: n, creating: o, suggestMetadataAction: r, onSuggestMetadata: i, onChange: s, onClose: c, onSubmit: l }) {
  const a = ge(() => py(t), [t]), u = gy(e, t), d = (f) => {
    if (f.startsWith("kind:")) {
      s({ ...e, rootKind: f.slice(5), rootActivityVersionId: null });
      return;
    }
    const m = t.find((g) => g.activityVersionId === f);
    s({
      ...e,
      rootKind: dc(m) ?? e.rootKind,
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
            /* @__PURE__ */ h.jsx(Dt, { size: 13 }),
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
                /* @__PURE__ */ h.jsx("optgroup", { label: "Composite roots", children: a.compositeRoots.map((f) => /* @__PURE__ */ h.jsx("option", { value: f.value, children: f.label }, f.value)) }),
                a.otherCategories.map((f) => /* @__PURE__ */ h.jsx("optgroup", { label: f.name, children: f.activities.map((m) => /* @__PURE__ */ h.jsx("option", { value: m.activityVersionId, children: Ne(m) }, m.activityVersionId)) }, f.name))
              ]
            }
          )
        ] }),
        n === "loading" ? /* @__PURE__ */ h.jsx("div", { className: "wf-dialog-note", children: "Loading activity catalog..." }) : null,
        n === "failed" ? /* @__PURE__ */ h.jsx("div", { className: "wf-dialog-note", children: "Activity catalog could not be loaded. Composite roots remain available." }) : null,
        /* @__PURE__ */ h.jsxs("div", { className: "wf-dialog-actions", children: [
          /* @__PURE__ */ h.jsx("button", { type: "button", onClick: c, disabled: o, children: "Cancel" }),
          /* @__PURE__ */ h.jsx("button", { type: "submit", disabled: o || !e.name.trim(), children: o ? "Creating..." : "Create" })
        ] })
      ]
    }
  ) }) });
}
function dy({ context: e, ai: t, definitionFilter: n }) {
  const [o, r] = ne("loading"), [i, s] = ne(""), [c, l] = ne(""), [a, u] = ne([]), d = ge(
    () => n ? a.filter((x) => x.definitionId === n || x.sourceId === n) : a,
    [n, a]
  ), f = jt(t, "weaver.workflows.explain-executable"), m = pe(async () => {
    r("loading"), s("");
    try {
      u(await gm(e)), r("ready");
    } catch (x) {
      s(x instanceof Error ? x.message : String(x)), r("failed");
    }
  }, [e]);
  ce(() => {
    m();
  }, [m]);
  const g = async (x) => {
    l(""), s("");
    try {
      await Ja(e, x.artifactId), l(`Started ${x.artifactId}`);
    } catch (w) {
      s(w instanceof Error ? w.message : String(w));
    }
  };
  return /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
    /* @__PURE__ */ h.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
        m();
      }, children: "Refresh" }),
      n ? /* @__PURE__ */ h.jsxs("span", { className: "wf-filter-chip", children: [
        "Definition ",
        n
      ] }) : null
    ] }),
    o === "failed" ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(rn, { size: 16 }),
      " ",
      i
    ] }) : null,
    c ? /* @__PURE__ */ h.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ h.jsx(ho, { size: 14 }),
      " ",
      c
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
        /* @__PURE__ */ h.jsx("span", { children: xy(x) }),
        /* @__PURE__ */ h.jsx("span", { children: wy(x) }),
        /* @__PURE__ */ h.jsx("span", { children: pr(x.publishedAt ?? x.createdAt) }),
        /* @__PURE__ */ h.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
            g(x);
          }, children: [
            /* @__PURE__ */ h.jsx(Qa, { size: 13 }),
            " Run"
          ] }),
          f ? /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => Pt(t, f, x), children: [
            /* @__PURE__ */ h.jsx(Dt, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, x.artifactId))
    ] }) : null
  ] });
}
function fy({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const i = uc(n, t), s = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
  return /* @__PURE__ */ h.jsxs("div", { className: "wf-pagination", "aria-label": "Workflow definition pagination", children: [
    /* @__PURE__ */ h.jsxs("span", { className: "wf-pagination-summary", "aria-live": "polite", children: [
      "Showing ",
      s,
      "-",
      c,
      " of ",
      n
    ] }),
    /* @__PURE__ */ h.jsxs("label", { className: "wf-page-size", children: [
      "Rows",
      /* @__PURE__ */ h.jsx("select", { value: t, onChange: (l) => r(Number(l.target.value)), children: ry.map((l) => /* @__PURE__ */ h.jsx("option", { value: l, children: l }, l)) })
    ] }),
    /* @__PURE__ */ h.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ h.jsx(em, { size: 14 }),
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
        /* @__PURE__ */ h.jsx(Ln, { size: 14 })
      ] })
    ] })
  ] });
}
function hy(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function uc(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function jt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function Pt(e, t, n) {
  const o = t.createPrompt(n);
  o && e.dispatchPrompt(o);
}
function py(e) {
  const t = Jn(e, "flowchart"), n = Jn(e, "sequence"), o = [
    { value: t?.activityVersionId ?? "kind:flowchart", label: "Flowchart" },
    { value: n?.activityVersionId ?? "kind:sequence", label: "Sequence" }
  ], r = /* @__PURE__ */ new Map();
  for (const s of e.filter(gc)) {
    if (yy(s)) continue;
    const c = s.category || "Uncategorized";
    r.set(c, [...r.get(c) ?? [], s]);
  }
  const i = Array.from(r.entries()).sort(([s], [c]) => s.localeCompare(c)).map(([s, c]) => ({
    name: s,
    activities: c.sort((l, a) => Ne(l).localeCompare(Ne(a)))
  }));
  return { compositeRoots: o, otherCategories: i };
}
function gy(e, t) {
  return e.rootActivityVersionId ?? Jn(t, e.rootKind)?.activityVersionId ?? `kind:${e.rootKind}`;
}
function my(e, t) {
  return e.rootActivityVersionId ?? Jn(t, e.rootKind)?.activityVersionId ?? null;
}
function Jn(e, t) {
  return e.find((n) => dc(n) === t);
}
function dc(e) {
  return e ? hc(e) ? "flowchart" : pc(e) ? "sequence" : null : null;
}
function fc(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "Uncategorized";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [o]) => n.localeCompare(o)).map(([n, o]) => ({
    category: n,
    activities: o.sort((r, i) => Ne(r).localeCompare(Ne(i)))
  }));
}
function yy(e) {
  return hc(e) || pc(e);
}
function hc(e) {
  return Ne(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function pc(e) {
  return Ne(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function gc(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function xy(e) {
  return e.sourceKind || e.sourceId || e.sourceVersion ? [e.sourceKind, e.sourceId, e.sourceVersion].filter(Boolean).join(" / ") : e.definitionId;
}
function wy(e) {
  return vy(e.rootActivityType) || e.rootActivityType;
}
function vy(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function by(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    jn(t, n.typeName, n), jn(t, n.name, n), jn(t, n.displayName, n);
    const o = n.typeName.split(".").filter(Boolean).at(-1);
    jn(t, o, n);
  }
  return t;
}
function Sy(e, t, n) {
  const o = t.get(e.activityVersionId);
  return n.get(Xt(o?.activityTypeKey)) ?? n.get(Xt(Ny(o?.activityTypeKey))) ?? n.get(Xt(o?.displayName)) ?? n.get(Xt(e.activityVersionId)) ?? null;
}
function jn(e, t, n) {
  const o = Xt(t);
  o && !e.has(o) && e.set(o, n);
}
function Xt(e) {
  return e?.trim().toLowerCase() ?? "";
}
function Ny(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function Ey({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: o,
  onBack: r
}) {
  const [i, s] = ne(null), [c, l] = ne(null), [a, u] = ne([]), [d, f] = ne([]), [m, g] = ne(Hn), [x, w] = ne("loading"), [y, S] = ne([]), [p, v] = ne([]), [E, N] = ne([]), [I, D] = ne(null), [j, H] = ne(null), [M, R] = ne(null), [V, b] = ne(null), [C, _] = ne(""), [A, P] = ne(""), [$, B] = ne(!1), [O, F] = ne(null), [Q, Z] = ne(() => /* @__PURE__ */ new Set()), U = ae(null), se = ae(null), G = ae(""), z = ae(0), q = ae(null), re = ae(!1), oe = c?.state.rootActivity ?? null, K = ge(() => new Map(a.map((k) => [k.activityVersionId, k])), [a]), te = ge(() => by(d), [d]), ie = ge(() => nc(oe, y), [oe, y]), L = vm(ie, ie ? K.get(ie.activityVersionId) : void 0), Y = !!ie && L === "unsupported", ue = ge(() => Y ? null : Ko(oe, y), [oe, y, Y]), xe = ge(() => fc(a), [a]), he = ge(() => Y && ie?.nodeId === j ? ie : ue?.slot.activities.find((k) => k.nodeId === j) ?? null, [Y, ue, ie, j]), ze = ge(
    () => he ? Sy(he, K, te) : null,
    [K, te, he]
  ), Le = he ? Ue(he) : [], we = L === "flowchart" && ue?.slot.mode === "flowchart", Ae = !oe || !Y, Xe = jt(n, "weaver.workflows.find-draft-risks"), Ie = jt(n, "weaver.workflows.propose-update"), De = pe(async () => {
    _(""), w("loading");
    const [k, T, W, X] = await Promise.all([
      am(e, t),
      ec(e),
      mm(e).then(
        (de) => ({ ok: !0, descriptors: de }),
        () => ({ ok: !1, descriptors: [] })
      ),
      ym(e).then(
        (de) => ({ ok: !0, descriptors: de }),
        () => ({ ok: !1, descriptors: Hn })
      )
    ]), J = k.draft ?? null;
    s(k), G.current = J ? Ft(J) : "", l(J), u(T.activities ?? []), f(W.descriptors), g(X.descriptors.length > 0 ? X.descriptors : Hn), w(W.ok ? "ready" : "failed"), S([]), H(null);
  }, [e, t]);
  ce(() => {
    De().catch((k) => _(k instanceof Error ? k.message : String(k)));
  }, [De]), ce(() => {
    Z((k) => {
      let T = !1;
      const W = new Set(k);
      for (const X of xe)
        W.has(X.category) || (W.add(X.category), T = !0);
      return T ? W : k;
    });
  }, [xe]), ce(() => {
    if (!ie) {
      v([]), N([]);
      return;
    }
    const k = Y ? wm(ie, a, c?.layout ?? []) : ue ? xm(ue, a, c?.layout ?? []) : { nodes: [], edges: [] };
    v(k.nodes), N(k.edges);
  }, [a, c?.layout, Y, ue, ie]);
  const He = (k) => {
    l((T) => T && { ...T, state: { ...T.state, rootActivity: k } });
  }, Ve = pe((k, T) => {
    if (c?.state.rootActivity && Y)
      return;
    const W = es(k, ss(k));
    if (!c?.state.rootActivity) {
      He(W), H(W.nodeId);
      return;
    }
    if (!ue) {
      if (!Ue(W)[0]) {
        P(""), _("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      l((J) => {
        if (!J?.state.rootActivity) return J;
        const de = J.state.rootActivity, fe = hr(W, [], [de]), Se = T ? [
          ...J.layout.filter((je) => je.nodeId !== de.nodeId),
          {
            nodeId: de.nodeId,
            x: Math.round(T.x),
            y: Math.round(T.y)
          }
        ] : J.layout;
        return {
          ...J,
          layout: Se,
          state: {
            ...J.state,
            rootActivity: fe
          }
        };
      }), H(c.state.rootActivity.nodeId), _(""), P(`Wrapped root in ${Ne(k)}`);
      return;
    }
    l((X) => {
      if (!X?.state.rootActivity) return X;
      const J = Ko(X.state.rootActivity, y);
      if (!J) return X;
      const de = hr(X.state.rootActivity, y, [...J.slot.activities, W]), fe = T ? [
        ...X.layout.filter((Se) => Se.nodeId !== W.nodeId),
        {
          nodeId: W.nodeId,
          x: Math.round(T.x),
          y: Math.round(T.y)
        }
      ] : X.layout;
      return {
        ...X,
        layout: fe,
        state: {
          ...X.state,
          rootActivity: de
        }
      };
    }), H(W.nodeId);
  }, [c?.state.rootActivity, y, Y, ue]), mt = pe((k, T) => {
    const W = es(k, ss(k)), X = {
      id: W.nodeId,
      type: "workflowActivity",
      position: T,
      selected: !0,
      data: {
        label: Ne(k),
        activityVersionId: k.activityVersionId,
        activityTypeKey: k.activityTypeKey,
        childSlots: Ue(W),
        acceptsInbound: String(k.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: sc(W, k)
      }
    };
    return { activityNode: W, node: X };
  }, []), _e = pe((k, T, W = []) => {
    Y || l((X) => {
      if (!X) return X;
      const J = Nm(X.layout, k), de = X.state.rootActivity;
      if (!de) return { ...X, layout: J };
      const fe = Ko(de, y);
      if (!fe) return { ...X, layout: J };
      const Se = bm(fe, k, T, W), je = fe.slot.mode === "flowchart" ? Sm(Se, T) : Se;
      return {
        ...X,
        layout: J,
        state: {
          ...X.state,
          rootActivity: oc(de, y, je)
        }
      };
    });
  }, [y, Y]), Tt = pe((k, T) => {
    if (!U.current) return null;
    const W = U.current.getBoundingClientRect();
    return I ? I.screenToFlowPosition({ x: k, y: T }) : {
      x: k - W.left,
      y: T - W.top
    };
  }, [I]), Rt = pe((k, T) => document.elementFromPoint(k, T)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), zt = pe((k, T, W) => {
    const X = p.find((Ce) => Ce.id === T.source), J = p.find((Ce) => Ce.id === T.target), de = X && J ? My(X, J) : X ? as(X) : W, fe = mt(k, de), je = [...p.map((Ce) => Ce.selected ? { ...Ce, selected: !1 } : Ce), fe.node], st = jm(E, T, fe.node.id);
    v(je), N(st), H(fe.node.id), _e(je, st, [fe.activityNode]);
  }, [_e, mt, E, p]), yt = pe((k, T, W) => {
    if (!Ae || !U.current) return !1;
    const X = U.current.getBoundingClientRect();
    if (!(T >= X.left && T <= X.right && W >= X.top && W <= X.bottom)) return !1;
    const de = Tt(T, W);
    if (!de) return !1;
    if (we) {
      const fe = Rt(T, W), Se = fe ? E.find((je) => je.id === fe) : void 0;
      if (Se)
        return zt(k, Se, de), !0;
    }
    return Ve(k, de), !0;
  }, [Ve, Ae, E, Rt, we, zt, Tt]);
  ce(() => {
    const k = (W) => {
      const X = q.current;
      if (!X) return;
      Math.hypot(W.clientX - X.startX, W.clientY - X.startY) >= ny && (X.dragging = !0);
    }, T = (W) => {
      const X = q.current;
      if (q.current = null, !X?.dragging || !U.current) return;
      const J = U.current.getBoundingClientRect();
      W.clientX >= J.left && W.clientX <= J.right && W.clientY >= J.top && W.clientY <= J.bottom && (re.current = !0, window.setTimeout(() => {
        re.current = !1;
      }, 0), yt(X.activity, W.clientX, W.clientY));
    };
    return window.addEventListener("pointermove", k), window.addEventListener("pointerup", T), window.addEventListener("pointercancel", T), () => {
      window.removeEventListener("pointermove", k), window.removeEventListener("pointerup", T), window.removeEventListener("pointercancel", T);
    };
  }, [I, yt]);
  const go = (k, T) => {
    k.dataTransfer.setData(os, T.activityVersionId), k.dataTransfer.setData("text/plain", T.activityVersionId), k.dataTransfer.effectAllowed = "copy";
  }, mo = (k, T) => {
    k.clientX === 0 && k.clientY === 0 || yt(T, k.clientX, k.clientY) && (re.current = !0, window.setTimeout(() => {
      re.current = !1;
    }, 0));
  }, yo = (k, T) => {
    k.button === 0 && (q.current = {
      activity: T,
      startX: k.clientX,
      startY: k.clientY,
      dragging: !1
    });
  }, xo = (k) => {
    re.current || Ae && Ve(k);
  }, gn = (k) => {
    if (!Ae) {
      k.dataTransfer.dropEffect = "none";
      return;
    }
    if (k.preventDefault(), k.dataTransfer.dropEffect = "copy", !we) return;
    const T = Rt(k.clientX, k.clientY);
    b(T);
  }, mn = (k) => {
    if (!U.current) return;
    const T = k.relatedTarget;
    T && U.current.contains(T) || b(null);
  }, yn = (k) => {
    if (k.preventDefault(), b(null), !Ae) return;
    const T = k.dataTransfer.getData(os) || k.dataTransfer.getData("text/plain"), W = K.get(T);
    W && yt(W, k.clientX, k.clientY);
  }, wo = () => {
    if (!we) return;
    const k = U.current?.getBoundingClientRect();
    k && R({
      kind: "fromEmpty",
      clientX: k.left + k.width / 2,
      clientY: k.top + k.height / 2
    });
  }, Lt = pe(async (k, T) => {
    const W = ++z.current, X = Ft(k);
    _("");
    try {
      const J = await fm(e, k), de = Ft(J);
      G.current = de, l((fe) => !fe || fe.id !== J.id ? fe : Ft(fe) === X ? J : { ...fe, validationErrors: J.validationErrors }), W === z.current && P(T);
    } catch (J) {
      W === z.current && (P(""), _(J instanceof Error ? J.message : String(J)));
    }
  }, [e]);
  ce(() => {
    if (!$ || !c || Ft(c) === G.current) return;
    P("Autosaving...");
    const T = window.setTimeout(() => {
      Lt(c, "Autosaved");
    }, oy);
    return () => window.clearTimeout(T);
  }, [$, c, Lt]);
  const vo = async () => {
    c && (P("Saving..."), await Lt(c, "Saved"));
  }, bo = async () => {
    if (c) {
      P("Promoting...");
      try {
        const k = await hm(e, c.id), T = await pm(e, k.versionId);
        F(T.artifactId), P(`Published ${T.artifactVersion}`), await De();
      } catch (k) {
        P(""), _(k instanceof Error ? k.message : String(k));
      }
    }
  }, So = async () => {
    if (O) {
      P("Running...");
      try {
        await Ja(e, O), P("Run dispatched");
      } catch (k) {
        P(""), _(k instanceof Error ? k.message : String(k));
      }
    }
  }, No = (k) => {
    const T = Y ? k.filter((W) => W.type === "select") : k;
    T.length !== 0 && v((W) => va(T, W));
  }, Eo = (k) => {
    Y || N((T) => ba(k, T));
  }, Ht = (k) => !k.source || !k.target || k.source === k.target || !we ? !1 : !k.targetHandle, _o = (k) => {
    if (!c?.state.rootActivity || !ue || !we || !Ht(k)) return;
    const T = Qn(k.source, k.target, k.sourceHandle ?? "Done", k.targetHandle ?? void 0), W = Na(T, E);
    N(W), _e(p, W);
  }, Co = () => {
    _e(p, E);
  }, ko = (k, T) => {
    if (!T.nodeId || T.handleType === "target") {
      se.current = null;
      return;
    }
    se.current = {
      nodeId: T.nodeId,
      handleId: T.handleId ?? null
    };
  }, Io = (k) => {
    const T = se.current;
    if (se.current = null, !T || !we || k.target?.closest(".react-flow__handle, .react-flow__node")) return;
    const X = Ay(k);
    R({
      kind: "fromPort",
      sourceNodeId: T.nodeId,
      sourceHandleId: T.handleId,
      clientX: X.x,
      clientY: X.y
    });
  }, Mo = (k, T) => {
    if (!we || !Ht(T)) return;
    const W = Kh(k, {
      ...T,
      sourceHandle: T.sourceHandle ?? "Done",
      targetHandle: T.targetHandle ?? void 0
    }, E, { shouldReplaceId: !1 });
    N(W), _e(p, W);
  }, Ao = (k) => {
    if (Y || k.length === 0) return;
    const T = new Set(k.map((J) => J.id)), W = p.filter((J) => !T.has(J.id)), X = E.filter((J) => !T.has(J.source) && !T.has(J.target));
    v(W), N(X), j && T.has(j) && H(null), _e(W, X);
  }, Do = (k) => {
    if (Y || k.length === 0) return;
    const T = new Set(k.map((X) => X.id)), W = E.filter((X) => !T.has(X.id));
    N(W), _e(p, W);
  }, xn = pe((k) => {
    if (Y) return;
    const T = E.filter((W) => W.id !== k);
    N(T), _e(p, T);
  }, [_e, E, Y, p]), wn = pe((k, T, W) => {
    we && R({ kind: "spliceEdge", edgeId: k, clientX: T, clientY: W });
  }, [we]), jo = (k) => {
    const T = M;
    if (!T) return;
    R(null);
    const W = Tt(T.clientX, T.clientY) ?? { x: 0, y: 0 };
    if (T.kind === "fromEmpty") {
      const J = mt(k, W), fe = [...p.map((Se) => Se.selected ? { ...Se, selected: !1 } : Se), J.node];
      v(fe), H(J.node.id), _e(fe, E, [J.activityNode]);
      return;
    }
    if (T.kind === "fromPort") {
      const J = p.find((Ce) => Ce.id === T.sourceNodeId), de = J ? as(J) : W, fe = mt(k, de), je = [...p.map((Ce) => Ce.selected ? { ...Ce, selected: !1 } : Ce), fe.node], st = [...E, Qn(T.sourceNodeId, fe.node.id, T.sourceHandleId ?? "Done")];
      v(je), N(st), H(fe.node.id), _e(je, st, [fe.activityNode]);
      return;
    }
    const X = E.find((J) => J.id === T.edgeId);
    X && zt(k, X, W);
  }, Po = ge(() => ({
    highlightedEdgeId: V,
    deleteEdge: xn,
    requestInsertActivity: wn
  }), [xn, V, wn]), vn = (k, T, W) => {
    S((X) => [...X, { ownerNodeId: k.nodeId, slotId: T, label: W }]), H(null);
  }, bn = pe((k) => {
    l((T) => {
      const W = T?.state.rootActivity;
      return !T || !W ? T : {
        ...T,
        state: {
          ...T.state,
          rootActivity: rc(W, k.nodeId, () => k)
        }
      };
    });
  }, []), Sn = (k) => {
    Z((T) => {
      const W = new Set(T);
      return W.has(k) ? W.delete(k) : W.add(k), W;
    });
  };
  return !i || !c ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: C || "Loading workflow editor..." }) : /* @__PURE__ */ h.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ h.jsx("button", { type: "button", className: "wf-link-button", onClick: r, children: "Definitions" }),
      /* @__PURE__ */ h.jsx(Ln, { size: 14 }),
      /* @__PURE__ */ h.jsx("strong", { children: i.definition.name }),
      /* @__PURE__ */ h.jsx("span", { className: "wf-chip", children: "Draft" }),
      A ? /* @__PURE__ */ h.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ h.jsx(ho, { size: 13 }),
        " ",
        A
      ] }) : null,
      /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ h.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ h.jsx("input", { type: "checkbox", checked: $, onChange: (k) => B(k.target.checked) }),
          /* @__PURE__ */ h.jsx("span", { children: "Autosave" })
        ] }),
        Xe ? /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => Pt(n, Xe, { definition: i.definition, draft: c }), children: [
          /* @__PURE__ */ h.jsx(Dt, { size: 15 }),
          " Risks"
        ] }) : null,
        Ie ? /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => Pt(n, Ie, { definition: i.definition, draft: c }), children: [
          /* @__PURE__ */ h.jsx(Dt, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
          vo();
        }, children: [
          /* @__PURE__ */ h.jsx(rm, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
          bo();
        }, children: [
          /* @__PURE__ */ h.jsx(tm, { size: 15 }),
          " Promote"
        ] }),
        /* @__PURE__ */ h.jsxs("button", { type: "button", disabled: !O, onClick: () => {
          So();
        }, children: [
          /* @__PURE__ */ h.jsx(Qa, { size: 15 }),
          " Run"
        ] })
      ] })
    ] }),
    C ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(rn, { size: 16 }),
      " ",
      C
    ] }) : null,
    /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-body", children: [
      /* @__PURE__ */ h.jsxs("aside", { className: "wf-palette", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ h.jsx(Qg, { size: 15 }),
          " Activities"
        ] }),
        /* @__PURE__ */ h.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: xe.map((k) => {
          const T = Q.has(k.category);
          return /* @__PURE__ */ h.jsxs("div", { className: "wf-palette-category", children: [
            /* @__PURE__ */ h.jsxs(
              "button",
              {
                type: "button",
                className: "wf-palette-category-toggle",
                role: "treeitem",
                "aria-expanded": T,
                onClick: () => Sn(k.category),
                children: [
                  T ? /* @__PURE__ */ h.jsx(Jg, { size: 14 }) : /* @__PURE__ */ h.jsx(Ln, { size: 14 }),
                  /* @__PURE__ */ h.jsx("span", { children: k.category }),
                  /* @__PURE__ */ h.jsx("small", { children: k.activities.length })
                ]
              }
            ),
            T ? /* @__PURE__ */ h.jsx("div", { className: "wf-palette-activities", role: "group", children: k.activities.map((W) => {
              const X = W.description?.trim(), J = X ? `wf-palette-description-${W.activityVersionId}` : void 0;
              return /* @__PURE__ */ h.jsxs(
                "button",
                {
                  type: "button",
                  className: "wf-palette-activity",
                  role: "treeitem",
                  draggable: !0,
                  title: X || Ne(W),
                  "aria-describedby": J,
                  onClick: () => xo(W),
                  onDragStart: (de) => go(de, W),
                  onDragEnd: (de) => mo(de, W),
                  onPointerDown: (de) => yo(de, W),
                  children: [
                    /* @__PURE__ */ h.jsx("strong", { children: Ne(W) }),
                    X ? /* @__PURE__ */ h.jsx("small", { id: J, children: X }) : null
                  ]
                },
                W.activityVersionId
              );
            }) }) : null
          ] }, k.category);
        }) })
      ] }),
      /* @__PURE__ */ h.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-breadcrumb", children: [
          /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
            S([]), H(null);
          }, children: "Root" }),
          y.map((k, T) => /* @__PURE__ */ h.jsxs(it.Fragment, { children: [
            /* @__PURE__ */ h.jsx(Ln, { size: 13 }),
            /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
              S(y.slice(0, T + 1)), H(null);
            }, children: k.label })
          ] }, `${k.ownerNodeId}-${k.slotId}-${T}`))
        ] }),
        /* @__PURE__ */ h.jsxs("div", { className: "wf-canvas", ref: U, onDragOver: gn, onDragLeave: mn, onDrop: yn, children: [
          /* @__PURE__ */ h.jsx(lc.Provider, { value: Po, children: /* @__PURE__ */ h.jsxs(
            xg,
            {
              nodes: p,
              edges: E,
              nodeTypes: ey,
              edgeTypes: ty,
              onInit: D,
              onNodesChange: No,
              onEdgesChange: Eo,
              onNodesDelete: Ao,
              onEdgesDelete: Do,
              onConnect: _o,
              onConnectStart: we ? ko : void 0,
              onConnectEnd: we ? Io : void 0,
              onReconnect: we ? Mo : void 0,
              isValidConnection: Ht,
              onDragOver: gn,
              onDragLeave: mn,
              onDrop: yn,
              onPaneClick: () => H(null),
              onNodeClick: (k, T) => H(T.id),
              onNodeDragStop: Y ? void 0 : Co,
              fitView: !0,
              minZoom: 0.2,
              maxZoom: 1.8,
              nodesConnectable: we,
              nodesDraggable: !Y,
              selectionOnDrag: !0,
              multiSelectionKeyCode: ["Shift", "Meta", "Control"],
              deleteKeyCode: Y ? null : ["Backspace", "Delete"],
              panActivationKeyCode: null,
              defaultEdgeOptions: { type: "workflow" },
              children: [
                /* @__PURE__ */ h.jsx(_g, { gap: 18, size: 1 }),
                /* @__PURE__ */ h.jsx(jg, {}),
                /* @__PURE__ */ h.jsx(Yg, { pannable: !0, zoomable: !0 })
              ]
            }
          ) }),
          we && p.length === 0 ? /* @__PURE__ */ h.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => wo(), children: [
            /* @__PURE__ */ h.jsx(Tr, { size: 15 }),
            " Add activity"
          ] }) : null,
          M ? /* @__PURE__ */ h.jsx(
            ky,
            {
              clientX: M.clientX,
              clientY: M.clientY,
              activities: a,
              onPick: jo,
              onClose: () => R(null)
            }
          ) : null
        ] }),
        /* @__PURE__ */ h.jsx(Iy, { draft: c })
      ] }),
      /* @__PURE__ */ h.jsxs("aside", { className: "wf-inspector", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ h.jsx(nm, { size: 15 }),
          " Inspector"
        ] }),
        he ? /* @__PURE__ */ h.jsxs("div", { className: "wf-inspector-content", children: [
          /* @__PURE__ */ h.jsx("h3", { children: p.find((k) => k.id === he.nodeId)?.data.label ?? he.nodeId }),
          /* @__PURE__ */ h.jsxs("dl", { children: [
            /* @__PURE__ */ h.jsx("dt", { children: "Node ID" }),
            /* @__PURE__ */ h.jsx("dd", { children: he.nodeId }),
            /* @__PURE__ */ h.jsx("dt", { children: "Activity version" }),
            /* @__PURE__ */ h.jsx("dd", { children: he.activityVersionId })
          ] }),
          /* @__PURE__ */ h.jsx(
            Zm,
            {
              activity: he,
              descriptor: ze,
              editors: o,
              expressionDescriptors: m,
              descriptorStatus: x,
              onChange: bn
            }
          ),
          Le.length > 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-slot-list", children: [
            /* @__PURE__ */ h.jsx("span", { children: "Embedded slots" }),
            Le.map((k) => /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => vn(he, k.id, `${p.find((T) => T.id === he.nodeId)?.data.label ?? he.nodeId} / ${k.label}`), children: [
              k.label,
              /* @__PURE__ */ h.jsxs("small", { children: [
                k.activities.length,
                " activit",
                k.activities.length === 1 ? "y" : "ies"
              ] })
            ] }, k.id))
          ] }) : /* @__PURE__ */ h.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
        ] }) : /* @__PURE__ */ h.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." })
      ] })
    ] })
  ] });
}
function _y({ data: e, selected: t }) {
  const n = e, o = !n.suppressFlowPorts, r = o ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [];
  return /* @__PURE__ */ h.jsxs("div", { className: t ? "wf-node selected" : "wf-node", children: [
    o && n.acceptsInbound ? /* @__PURE__ */ h.jsx(At, { type: "target", position: ee.Left }) : null,
    /* @__PURE__ */ h.jsx("strong", { children: n.label }),
    /* @__PURE__ */ h.jsx("small", { children: n.activityTypeKey ?? n.activityVersionId }),
    n.childSlots.length > 0 ? /* @__PURE__ */ h.jsxs("span", { children: [
      n.childSlots.length,
      " embedded slot",
      n.childSlots.length === 1 ? "" : "s"
    ] }) : null,
    r.map((i, s) => {
      const c = `${(s + 1) / (r.length + 1) * 100}%`;
      return /* @__PURE__ */ h.jsxs(it.Fragment, { children: [
        /* @__PURE__ */ h.jsx("span", { className: "wf-node-port-label", style: { top: c }, children: i.displayName }),
        /* @__PURE__ */ h.jsx(At, { type: "source", position: ee.Right, id: i.name, style: { top: c } })
      ] }, i.name);
    })
  ] });
}
function Cy(e) {
  const {
    id: t,
    sourceX: n,
    sourceY: o,
    targetX: r,
    targetY: i,
    sourcePosition: s,
    targetPosition: c,
    markerEnd: l,
    style: a,
    label: u,
    labelStyle: d
  } = e, f = it.useContext(lc), [m, g] = ne(!1), [x, w, y] = Kn({ sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: c }), S = f?.highlightedEdgeId === t;
  return /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
    /* @__PURE__ */ h.jsx(
      hn,
      {
        id: t,
        path: x,
        markerEnd: l,
        style: {
          ...a,
          strokeWidth: S ? 2.5 : a?.strokeWidth
        },
        label: u,
        labelX: w,
        labelY: y,
        labelStyle: d,
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1)
      }
    ),
    f ? /* @__PURE__ */ h.jsx(vg, { children: /* @__PURE__ */ h.jsxs(
      "div",
      {
        className: ["wf-edge-actions", m ? "visible" : "", S ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${w}px, ${y}px)` },
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1),
        children: [
          /* @__PURE__ */ h.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (p) => f.requestInsertActivity(t, p.clientX, p.clientY), children: /* @__PURE__ */ h.jsx(Tr, { size: 12 }) }),
          /* @__PURE__ */ h.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => f.deleteEdge(t), children: /* @__PURE__ */ h.jsx(fr, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function ky({ clientX: e, clientY: t, activities: n, onPick: o, onClose: r }) {
  const [i, s] = ne(""), [c, l] = ne(0), a = ae(null), u = ae(null), d = ge(() => {
    const S = i.trim().toLowerCase(), p = n.filter(gc);
    return S ? p.filter((v) => Ne(v).toLowerCase().includes(S) || v.activityTypeKey.toLowerCase().includes(S) || (v.category ?? "").toLowerCase().includes(S) || (v.description ?? "").toLowerCase().includes(S)) : p;
  }, [n, i]), f = ge(() => fc(d), [d]), m = ge(() => f.flatMap((S) => S.activities), [f]);
  ce(() => {
    requestAnimationFrame(() => u.current?.focus());
  }, []), ce(() => {
    const S = (v) => {
      a.current?.contains(v.target) || r();
    }, p = (v) => {
      v.key === "Escape" && r();
    };
    return document.addEventListener("mousedown", S, !0), document.addEventListener("keydown", p), () => {
      document.removeEventListener("mousedown", S, !0), document.removeEventListener("keydown", p);
    };
  }, [r]);
  const g = (S) => {
    if (S.key === "ArrowDown")
      S.preventDefault(), l((p) => Math.min(p + 1, m.length - 1));
    else if (S.key === "ArrowUp")
      S.preventDefault(), l((p) => Math.max(p - 1, 0));
    else if (S.key === "Enter") {
      S.preventDefault();
      const p = m[c];
      p && o(p);
    }
  }, x = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), w = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let y = -1;
  return /* @__PURE__ */ h.jsxs("div", { ref: a, className: "wf-connect-menu", style: { left: x, top: w }, onMouseDown: (S) => S.stopPropagation(), onClick: (S) => S.stopPropagation(), children: [
    /* @__PURE__ */ h.jsx(
      "input",
      {
        ref: u,
        type: "search",
        value: i,
        placeholder: "Search activities...",
        "aria-label": "Search activities",
        onChange: (S) => {
          s(S.target.value), l(0);
        },
        onKeyDown: g
      }
    ),
    /* @__PURE__ */ h.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: f.length === 0 ? /* @__PURE__ */ h.jsx("p", { children: "No matching activities." }) : f.map((S) => /* @__PURE__ */ h.jsxs("section", { children: [
      /* @__PURE__ */ h.jsx("h4", { children: S.category }),
      S.activities.map((p) => {
        y += 1;
        const v = y, E = v === c;
        return /* @__PURE__ */ h.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": E,
            className: E ? "active" : "",
            onMouseEnter: () => l(v),
            onClick: () => o(p),
            children: [
              /* @__PURE__ */ h.jsx("strong", { children: Ne(p) }),
              /* @__PURE__ */ h.jsx("small", { children: p.category || p.activityTypeKey })
            ]
          },
          p.activityVersionId
        );
      })
    ] }, S.category)) })
  ] });
}
function Iy({ draft: e }) {
  return e.validationErrors.length ? /* @__PURE__ */ h.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ h.jsx(rn, { size: 14 }),
    e.validationErrors.length,
    " validation issue",
    e.validationErrors.length === 1 ? "" : "s"
  ] }) : /* @__PURE__ */ h.jsxs("div", { className: "wf-validation ok", children: [
    /* @__PURE__ */ h.jsx(ho, { size: 14 }),
    " No validation errors"
  ] });
}
function ss(e) {
  return `${Ne(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function as(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function My(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function Ay(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function Ft(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function pr(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
export {
  Py as register
};
