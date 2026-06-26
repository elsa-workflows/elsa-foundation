import xt, { memo as Se, forwardRef as er, useRef as ce, useEffect as oe, useCallback as he, useContext as Wn, useMemo as me, useState as Y, createContext as vi, useLayoutEffect as jd, createElement as ei, useId as Oa } from "react";
import "@tanstack/react-query";
function _d(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var Rr = { exports: {} }, wn = {};
var rs;
function Ad() {
  if (rs) return wn;
  rs = 1;
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
  return wn.Fragment = t, wn.jsx = n, wn.jsxs = n, wn;
}
var is;
function Md() {
  return is || (is = 1, Rr.exports = Ad()), Rr.exports;
}
var a = Md();
function Ie(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, o; n < e.length; n++)
      (o = Ie(e[n])) !== "" && (t += (t && " ") + o);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var Dd = { value: () => {
} };
function tr() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new Ao(n);
}
function Ao(e) {
  this._ = e;
}
function Pd(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
Ao.prototype = tr.prototype = {
  constructor: Ao,
  on: function(e, t) {
    var n = this._, o = Pd(e + "", n), r, i = -1, s = o.length;
    if (arguments.length < 2) {
      for (; ++i < s; ) if ((r = (e = o[i]).type) && (r = $d(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++i < s; )
      if (r = (e = o[i]).type) n[r] = ss(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = ss(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new Ao(e);
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
function $d(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function ss(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = Dd, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var ti = "http://www.w3.org/1999/xhtml";
const as = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: ti,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function nr(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), as.hasOwnProperty(t) ? { space: as[t], local: e } : e;
}
function Td(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === ti && t.documentElement.namespaceURI === ti ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function zd(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function Fa(e) {
  var t = nr(e);
  return (t.local ? zd : Td)(t);
}
function Rd() {
}
function bi(e) {
  return e == null ? Rd : function() {
    return this.querySelector(e);
  };
}
function Ld(e) {
  typeof e != "function" && (e = bi(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = new Array(s), d, l, u = 0; u < s; ++u)
      (d = i[u]) && (l = e.call(d, d.__data__, u, i)) && ("__data__" in d && (l.__data__ = d.__data__), c[u] = l);
  return new Oe(o, this._parents);
}
function Vd(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function Hd() {
  return [];
}
function Ba(e) {
  return e == null ? Hd : function() {
    return this.querySelectorAll(e);
  };
}
function Od(e) {
  return function() {
    return Vd(e.apply(this, arguments));
  };
}
function Fd(e) {
  typeof e == "function" ? e = Od(e) : e = Ba(e);
  for (var t = this._groups, n = t.length, o = [], r = [], i = 0; i < n; ++i)
    for (var s = t[i], c = s.length, d, l = 0; l < c; ++l)
      (d = s[l]) && (o.push(e.call(d, d.__data__, l, s)), r.push(d));
  return new Oe(o, r);
}
function Wa(e) {
  return function() {
    return this.matches(e);
  };
}
function Xa(e) {
  return function(t) {
    return t.matches(e);
  };
}
var Bd = Array.prototype.find;
function Wd(e) {
  return function() {
    return Bd.call(this.children, e);
  };
}
function Xd() {
  return this.firstElementChild;
}
function Yd(e) {
  return this.select(e == null ? Xd : Wd(typeof e == "function" ? e : Xa(e)));
}
var qd = Array.prototype.filter;
function Ud() {
  return Array.from(this.children);
}
function Zd(e) {
  return function() {
    return qd.call(this.children, e);
  };
}
function Kd(e) {
  return this.selectAll(e == null ? Ud : Zd(typeof e == "function" ? e : Xa(e)));
}
function Gd(e) {
  typeof e != "function" && (e = Wa(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = [], d, l = 0; l < s; ++l)
      (d = i[l]) && e.call(d, d.__data__, l, i) && c.push(d);
  return new Oe(o, this._parents);
}
function Ya(e) {
  return new Array(e.length);
}
function Jd() {
  return new Oe(this._enter || this._groups.map(Ya), this._parents);
}
function Lo(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
Lo.prototype = {
  constructor: Lo,
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
function Qd(e) {
  return function() {
    return e;
  };
}
function eu(e, t, n, o, r, i) {
  for (var s = 0, c, d = t.length, l = i.length; s < l; ++s)
    (c = t[s]) ? (c.__data__ = i[s], o[s] = c) : n[s] = new Lo(e, i[s]);
  for (; s < d; ++s)
    (c = t[s]) && (r[s] = c);
}
function tu(e, t, n, o, r, i, s) {
  var c, d, l = /* @__PURE__ */ new Map(), u = t.length, f = i.length, h = new Array(u), p;
  for (c = 0; c < u; ++c)
    (d = t[c]) && (h[c] = p = s.call(d, d.__data__, c, t) + "", l.has(p) ? r[c] = d : l.set(p, d));
  for (c = 0; c < f; ++c)
    p = s.call(e, i[c], c, i) + "", (d = l.get(p)) ? (o[c] = d, d.__data__ = i[c], l.delete(p)) : n[c] = new Lo(e, i[c]);
  for (c = 0; c < u; ++c)
    (d = t[c]) && l.get(h[c]) === d && (r[c] = d);
}
function nu(e) {
  return e.__data__;
}
function ou(e, t) {
  if (!arguments.length) return Array.from(this, nu);
  var n = t ? tu : eu, o = this._parents, r = this._groups;
  typeof e != "function" && (e = Qd(e));
  for (var i = r.length, s = new Array(i), c = new Array(i), d = new Array(i), l = 0; l < i; ++l) {
    var u = o[l], f = r[l], h = f.length, p = ru(e.call(u, u && u.__data__, l, o)), y = p.length, v = c[l] = new Array(y), w = s[l] = new Array(y), m = d[l] = new Array(h);
    n(u, f, v, w, m, p, t);
    for (var b = 0, g = 0, x, I; b < y; ++b)
      if (x = v[b]) {
        for (b >= g && (g = b + 1); !(I = w[g]) && ++g < y; ) ;
        x._next = I || null;
      }
  }
  return s = new Oe(s, o), s._enter = c, s._exit = d, s;
}
function ru(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function iu() {
  return new Oe(this._exit || this._groups.map(Ya), this._parents);
}
function su(e, t, n) {
  var o = this.enter(), r = this, i = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? i.remove() : n(i), o && r ? o.merge(r).order() : r;
}
function au(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, i = o.length, s = Math.min(r, i), c = new Array(r), d = 0; d < s; ++d)
    for (var l = n[d], u = o[d], f = l.length, h = c[d] = new Array(f), p, y = 0; y < f; ++y)
      (p = l[y] || u[y]) && (h[y] = p);
  for (; d < r; ++d)
    c[d] = n[d];
  return new Oe(c, this._parents);
}
function cu() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, i = o[r], s; --r >= 0; )
      (s = o[r]) && (i && s.compareDocumentPosition(i) ^ 4 && i.parentNode.insertBefore(s, i), i = s);
  return this;
}
function lu(e) {
  e || (e = du);
  function t(f, h) {
    return f && h ? e(f.__data__, h.__data__) : !f - !h;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), i = 0; i < o; ++i) {
    for (var s = n[i], c = s.length, d = r[i] = new Array(c), l, u = 0; u < c; ++u)
      (l = s[u]) && (d[u] = l);
    d.sort(t);
  }
  return new Oe(r, this._parents).order();
}
function du(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function uu() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function fu() {
  return Array.from(this);
}
function hu() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length; r < i; ++r) {
      var s = o[r];
      if (s) return s;
    }
  return null;
}
function pu() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function gu() {
  return !this.node();
}
function yu(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], i = 0, s = r.length, c; i < s; ++i)
      (c = r[i]) && e.call(c, c.__data__, i, r);
  return this;
}
function mu(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function xu(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function wu(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function vu(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function bu(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function Nu(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function Su(e, t) {
  var n = nr(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? xu : mu : typeof t == "function" ? n.local ? Nu : bu : n.local ? vu : wu)(n, t));
}
function qa(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function Eu(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function ku(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function Iu(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function Cu(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? Eu : typeof t == "function" ? Iu : ku)(e, t, n ?? "")) : Kt(this.node(), e);
}
function Kt(e, t) {
  return e.style.getPropertyValue(t) || qa(e).getComputedStyle(e, null).getPropertyValue(t);
}
function ju(e) {
  return function() {
    delete this[e];
  };
}
function _u(e, t) {
  return function() {
    this[e] = t;
  };
}
function Au(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function Mu(e, t) {
  return arguments.length > 1 ? this.each((t == null ? ju : typeof t == "function" ? Au : _u)(e, t)) : this.node()[e];
}
function Ua(e) {
  return e.trim().split(/^|\s+/);
}
function Ni(e) {
  return e.classList || new Za(e);
}
function Za(e) {
  this._node = e, this._names = Ua(e.getAttribute("class") || "");
}
Za.prototype = {
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
function Ka(e, t) {
  for (var n = Ni(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function Ga(e, t) {
  for (var n = Ni(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function Du(e) {
  return function() {
    Ka(this, e);
  };
}
function Pu(e) {
  return function() {
    Ga(this, e);
  };
}
function $u(e, t) {
  return function() {
    (t.apply(this, arguments) ? Ka : Ga)(this, e);
  };
}
function Tu(e, t) {
  var n = Ua(e + "");
  if (arguments.length < 2) {
    for (var o = Ni(this.node()), r = -1, i = n.length; ++r < i; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? $u : t ? Du : Pu)(n, t));
}
function zu() {
  this.textContent = "";
}
function Ru(e) {
  return function() {
    this.textContent = e;
  };
}
function Lu(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function Vu(e) {
  return arguments.length ? this.each(e == null ? zu : (typeof e == "function" ? Lu : Ru)(e)) : this.node().textContent;
}
function Hu() {
  this.innerHTML = "";
}
function Ou(e) {
  return function() {
    this.innerHTML = e;
  };
}
function Fu(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function Bu(e) {
  return arguments.length ? this.each(e == null ? Hu : (typeof e == "function" ? Fu : Ou)(e)) : this.node().innerHTML;
}
function Wu() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function Xu() {
  return this.each(Wu);
}
function Yu() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function qu() {
  return this.each(Yu);
}
function Uu(e) {
  var t = typeof e == "function" ? e : Fa(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Zu() {
  return null;
}
function Ku(e, t) {
  var n = typeof e == "function" ? e : Fa(e), o = t == null ? Zu : typeof t == "function" ? t : bi(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function Gu() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Ju() {
  return this.each(Gu);
}
function Qu() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function ef() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function tf(e) {
  return this.select(e ? ef : Qu);
}
function nf(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function of(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function rf(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function sf(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, i; n < r; ++n)
        i = t[n], (!e.type || i.type === e.type) && i.name === e.name ? this.removeEventListener(i.type, i.listener, i.options) : t[++o] = i;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function af(e, t, n) {
  return function() {
    var o = this.__on, r, i = of(t);
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
function cf(e, t, n) {
  var o = rf(e + ""), r, i = o.length, s;
  if (arguments.length < 2) {
    var c = this.node().__on;
    if (c) {
      for (var d = 0, l = c.length, u; d < l; ++d)
        for (r = 0, u = c[d]; r < i; ++r)
          if ((s = o[r]).type === u.type && s.name === u.name)
            return u.value;
    }
    return;
  }
  for (c = t ? af : sf, r = 0; r < i; ++r) this.each(c(o[r], t, n));
  return this;
}
function Ja(e, t, n) {
  var o = qa(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function lf(e, t) {
  return function() {
    return Ja(this, e, t);
  };
}
function df(e, t) {
  return function() {
    return Ja(this, e, t.apply(this, arguments));
  };
}
function uf(e, t) {
  return this.each((typeof t == "function" ? df : lf)(e, t));
}
function* ff() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length, s; r < i; ++r)
      (s = o[r]) && (yield s);
}
var Qa = [null];
function Oe(e, t) {
  this._groups = e, this._parents = t;
}
function Xn() {
  return new Oe([[document.documentElement]], Qa);
}
function hf() {
  return this;
}
Oe.prototype = Xn.prototype = {
  constructor: Oe,
  select: Ld,
  selectAll: Fd,
  selectChild: Yd,
  selectChildren: Kd,
  filter: Gd,
  data: ou,
  enter: Jd,
  exit: iu,
  join: su,
  merge: au,
  selection: hf,
  order: cu,
  sort: lu,
  call: uu,
  nodes: fu,
  node: hu,
  size: pu,
  empty: gu,
  each: yu,
  attr: Su,
  style: Cu,
  property: Mu,
  classed: Tu,
  text: Vu,
  html: Bu,
  raise: Xu,
  lower: qu,
  append: Uu,
  insert: Ku,
  remove: Ju,
  clone: tf,
  datum: nf,
  on: cf,
  dispatch: uf,
  [Symbol.iterator]: ff
};
function He(e) {
  return typeof e == "string" ? new Oe([[document.querySelector(e)]], [document.documentElement]) : new Oe([[e]], Qa);
}
function pf(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Ye(e, t) {
  if (e = pf(e), t === void 0 && (t = e.currentTarget), t) {
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
const gf = { passive: !1 }, Pn = { capture: !0, passive: !1 };
function Lr(e) {
  e.stopImmediatePropagation();
}
function qt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function ec(e) {
  var t = e.document.documentElement, n = He(e).on("dragstart.drag", qt, Pn);
  "onselectstart" in t ? n.on("selectstart.drag", qt, Pn) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function tc(e, t) {
  var n = e.document.documentElement, o = He(e).on("dragstart.drag", null);
  t && (o.on("click.drag", qt, Pn), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const wo = (e) => () => e;
function ni(e, {
  sourceEvent: t,
  subject: n,
  target: o,
  identifier: r,
  active: i,
  x: s,
  y: c,
  dx: d,
  dy: l,
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
    dx: { value: d, enumerable: !0, configurable: !0 },
    dy: { value: l, enumerable: !0, configurable: !0 },
    _: { value: u }
  });
}
ni.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function yf(e) {
  return !e.ctrlKey && !e.button;
}
function mf() {
  return this.parentNode;
}
function xf(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function wf() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function nc() {
  var e = yf, t = mf, n = xf, o = wf, r = {}, i = tr("start", "drag", "end"), s = 0, c, d, l, u, f = 0;
  function h(x) {
    x.on("mousedown.drag", p).filter(o).on("touchstart.drag", w).on("touchmove.drag", m, gf).on("touchend.drag touchcancel.drag", b).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function p(x, I) {
    if (!(u || !e.call(this, x, I))) {
      var E = g(this, t.call(this, x, I), x, I, "mouse");
      E && (He(x.view).on("mousemove.drag", y, Pn).on("mouseup.drag", v, Pn), ec(x.view), Lr(x), l = !1, c = x.clientX, d = x.clientY, E("start", x));
    }
  }
  function y(x) {
    if (qt(x), !l) {
      var I = x.clientX - c, E = x.clientY - d;
      l = I * I + E * E > f;
    }
    r.mouse("drag", x);
  }
  function v(x) {
    He(x.view).on("mousemove.drag mouseup.drag", null), tc(x.view, l), qt(x), r.mouse("end", x);
  }
  function w(x, I) {
    if (e.call(this, x, I)) {
      var E = x.changedTouches, j = t.call(this, x, I), P = E.length, T, A;
      for (T = 0; T < P; ++T)
        (A = g(this, j, x, I, E[T].identifier, E[T])) && (Lr(x), A("start", x, E[T]));
    }
  }
  function m(x) {
    var I = x.changedTouches, E = I.length, j, P;
    for (j = 0; j < E; ++j)
      (P = r[I[j].identifier]) && (qt(x), P("drag", x, I[j]));
  }
  function b(x) {
    var I = x.changedTouches, E = I.length, j, P;
    for (u && clearTimeout(u), u = setTimeout(function() {
      u = null;
    }, 500), j = 0; j < E; ++j)
      (P = r[I[j].identifier]) && (Lr(x), P("end", x, I[j]));
  }
  function g(x, I, E, j, P, T) {
    var A = i.copy(), k = Ye(T || E, I), R, O, S;
    if ((S = n.call(x, new ni("beforestart", {
      sourceEvent: E,
      target: h,
      identifier: P,
      active: s,
      x: k[0],
      y: k[1],
      dx: 0,
      dy: 0,
      dispatch: A
    }), j)) != null)
      return R = S.x - k[0] || 0, O = S.y - k[1] || 0, function _(C, M, z) {
        var $ = k, W;
        switch (C) {
          case "start":
            r[P] = _, W = s++;
            break;
          case "end":
            delete r[P], --s;
          // falls through
          case "drag":
            k = Ye(z || M, I), W = s;
            break;
        }
        A.call(
          C,
          x,
          new ni(C, {
            sourceEvent: M,
            subject: S,
            target: h,
            identifier: P,
            active: W,
            x: k[0] + R,
            y: k[1] + O,
            dx: k[0] - $[0],
            dy: k[1] - $[1],
            dispatch: A
          }),
          j
        );
      };
  }
  return h.filter = function(x) {
    return arguments.length ? (e = typeof x == "function" ? x : wo(!!x), h) : e;
  }, h.container = function(x) {
    return arguments.length ? (t = typeof x == "function" ? x : wo(x), h) : t;
  }, h.subject = function(x) {
    return arguments.length ? (n = typeof x == "function" ? x : wo(x), h) : n;
  }, h.touchable = function(x) {
    return arguments.length ? (o = typeof x == "function" ? x : wo(!!x), h) : o;
  }, h.on = function() {
    var x = i.on.apply(i, arguments);
    return x === i ? h : x;
  }, h.clickDistance = function(x) {
    return arguments.length ? (f = (x = +x) * x, h) : Math.sqrt(f);
  }, h;
}
function Si(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function oc(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function Yn() {
}
var $n = 0.7, Vo = 1 / $n, Ut = "\\s*([+-]?\\d+)\\s*", Tn = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", tt = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", vf = /^#([0-9a-f]{3,8})$/, bf = new RegExp(`^rgb\\(${Ut},${Ut},${Ut}\\)$`), Nf = new RegExp(`^rgb\\(${tt},${tt},${tt}\\)$`), Sf = new RegExp(`^rgba\\(${Ut},${Ut},${Ut},${Tn}\\)$`), Ef = new RegExp(`^rgba\\(${tt},${tt},${tt},${Tn}\\)$`), kf = new RegExp(`^hsl\\(${Tn},${tt},${tt}\\)$`), If = new RegExp(`^hsla\\(${Tn},${tt},${tt},${Tn}\\)$`), cs = {
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
Si(Yn, At, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: ls,
  // Deprecated! Use color.formatHex.
  formatHex: ls,
  formatHex8: Cf,
  formatHsl: jf,
  formatRgb: ds,
  toString: ds
});
function ls() {
  return this.rgb().formatHex();
}
function Cf() {
  return this.rgb().formatHex8();
}
function jf() {
  return rc(this).formatHsl();
}
function ds() {
  return this.rgb().formatRgb();
}
function At(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = vf.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? us(t) : n === 3 ? new Re(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? vo(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? vo(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = bf.exec(e)) ? new Re(t[1], t[2], t[3], 1) : (t = Nf.exec(e)) ? new Re(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = Sf.exec(e)) ? vo(t[1], t[2], t[3], t[4]) : (t = Ef.exec(e)) ? vo(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = kf.exec(e)) ? ps(t[1], t[2] / 100, t[3] / 100, 1) : (t = If.exec(e)) ? ps(t[1], t[2] / 100, t[3] / 100, t[4]) : cs.hasOwnProperty(e) ? us(cs[e]) : e === "transparent" ? new Re(NaN, NaN, NaN, 0) : null;
}
function us(e) {
  return new Re(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function vo(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new Re(e, t, n, o);
}
function _f(e) {
  return e instanceof Yn || (e = At(e)), e ? (e = e.rgb(), new Re(e.r, e.g, e.b, e.opacity)) : new Re();
}
function oi(e, t, n, o) {
  return arguments.length === 1 ? _f(e) : new Re(e, t, n, o ?? 1);
}
function Re(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
Si(Re, oi, oc(Yn, {
  brighter(e) {
    return e = e == null ? Vo : Math.pow(Vo, e), new Re(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? $n : Math.pow($n, e), new Re(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new Re(Ct(this.r), Ct(this.g), Ct(this.b), Ho(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: fs,
  // Deprecated! Use color.formatHex.
  formatHex: fs,
  formatHex8: Af,
  formatRgb: hs,
  toString: hs
}));
function fs() {
  return `#${It(this.r)}${It(this.g)}${It(this.b)}`;
}
function Af() {
  return `#${It(this.r)}${It(this.g)}${It(this.b)}${It((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function hs() {
  const e = Ho(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${Ct(this.r)}, ${Ct(this.g)}, ${Ct(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Ho(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function Ct(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function It(e) {
  return e = Ct(e), (e < 16 ? "0" : "") + e.toString(16);
}
function ps(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new qe(e, t, n, o);
}
function rc(e) {
  if (e instanceof qe) return new qe(e.h, e.s, e.l, e.opacity);
  if (e instanceof Yn || (e = At(e)), !e) return new qe();
  if (e instanceof qe) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), i = Math.max(t, n, o), s = NaN, c = i - r, d = (i + r) / 2;
  return c ? (t === i ? s = (n - o) / c + (n < o) * 6 : n === i ? s = (o - t) / c + 2 : s = (t - n) / c + 4, c /= d < 0.5 ? i + r : 2 - i - r, s *= 60) : c = d > 0 && d < 1 ? 0 : s, new qe(s, c, d, e.opacity);
}
function Mf(e, t, n, o) {
  return arguments.length === 1 ? rc(e) : new qe(e, t, n, o ?? 1);
}
function qe(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
Si(qe, Mf, oc(Yn, {
  brighter(e) {
    return e = e == null ? Vo : Math.pow(Vo, e), new qe(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? $n : Math.pow($n, e), new qe(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new Re(
      Vr(e >= 240 ? e - 240 : e + 120, r, o),
      Vr(e, r, o),
      Vr(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new qe(gs(this.h), bo(this.s), bo(this.l), Ho(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Ho(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${gs(this.h)}, ${bo(this.s) * 100}%, ${bo(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function gs(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function bo(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function Vr(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const Ei = (e) => () => e;
function Df(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function Pf(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function $f(e) {
  return (e = +e) == 1 ? ic : function(t, n) {
    return n - t ? Pf(t, n, e) : Ei(isNaN(t) ? n : t);
  };
}
function ic(e, t) {
  var n = t - e;
  return n ? Df(e, n) : Ei(isNaN(e) ? t : e);
}
const Oo = (function e(t) {
  var n = $f(t);
  function o(r, i) {
    var s = n((r = oi(r)).r, (i = oi(i)).r), c = n(r.g, i.g), d = n(r.b, i.b), l = ic(r.opacity, i.opacity);
    return function(u) {
      return r.r = s(u), r.g = c(u), r.b = d(u), r.opacity = l(u), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function Tf(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(i) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - i) + t[r] * i;
    return o;
  };
}
function zf(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Rf(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), i = new Array(n), s;
  for (s = 0; s < o; ++s) r[s] = An(e[s], t[s]);
  for (; s < n; ++s) i[s] = t[s];
  return function(c) {
    for (s = 0; s < o; ++s) i[s] = r[s](c);
    return i;
  };
}
function Lf(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(o) {
    return n.setTime(e * (1 - o) + t * o), n;
  };
}
function et(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function Vf(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = An(e[r], t[r]) : o[r] = t[r];
  return function(i) {
    for (r in n) o[r] = n[r](i);
    return o;
  };
}
var ri = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, Hr = new RegExp(ri.source, "g");
function Hf(e) {
  return function() {
    return e;
  };
}
function Of(e) {
  return function(t) {
    return e(t) + "";
  };
}
function sc(e, t) {
  var n = ri.lastIndex = Hr.lastIndex = 0, o, r, i, s = -1, c = [], d = [];
  for (e = e + "", t = t + ""; (o = ri.exec(e)) && (r = Hr.exec(t)); )
    (i = r.index) > n && (i = t.slice(n, i), c[s] ? c[s] += i : c[++s] = i), (o = o[0]) === (r = r[0]) ? c[s] ? c[s] += r : c[++s] = r : (c[++s] = null, d.push({ i: s, x: et(o, r) })), n = Hr.lastIndex;
  return n < t.length && (i = t.slice(n), c[s] ? c[s] += i : c[++s] = i), c.length < 2 ? d[0] ? Of(d[0].x) : Hf(t) : (t = d.length, function(l) {
    for (var u = 0, f; u < t; ++u) c[(f = d[u]).i] = f.x(l);
    return c.join("");
  });
}
function An(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? Ei(t) : (n === "number" ? et : n === "string" ? (o = At(t)) ? (t = o, Oo) : sc : t instanceof At ? Oo : t instanceof Date ? Lf : zf(t) ? Tf : Array.isArray(t) ? Rf : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Vf : et)(e, t);
}
var ys = 180 / Math.PI, ii = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function ac(e, t, n, o, r, i) {
  var s, c, d;
  return (s = Math.sqrt(e * e + t * t)) && (e /= s, t /= s), (d = e * n + t * o) && (n -= e * d, o -= t * d), (c = Math.sqrt(n * n + o * o)) && (n /= c, o /= c, d /= c), e * o < t * n && (e = -e, t = -t, d = -d, s = -s), {
    translateX: r,
    translateY: i,
    rotate: Math.atan2(t, e) * ys,
    skewX: Math.atan(d) * ys,
    scaleX: s,
    scaleY: c
  };
}
var No;
function Ff(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? ii : ac(t.a, t.b, t.c, t.d, t.e, t.f);
}
function Bf(e) {
  return e == null || (No || (No = document.createElementNS("http://www.w3.org/2000/svg", "g")), No.setAttribute("transform", e), !(e = No.transform.baseVal.consolidate())) ? ii : (e = e.matrix, ac(e.a, e.b, e.c, e.d, e.e, e.f));
}
function cc(e, t, n, o) {
  function r(l) {
    return l.length ? l.pop() + " " : "";
  }
  function i(l, u, f, h, p, y) {
    if (l !== f || u !== h) {
      var v = p.push("translate(", null, t, null, n);
      y.push({ i: v - 4, x: et(l, f) }, { i: v - 2, x: et(u, h) });
    } else (f || h) && p.push("translate(" + f + t + h + n);
  }
  function s(l, u, f, h) {
    l !== u ? (l - u > 180 ? u += 360 : u - l > 180 && (l += 360), h.push({ i: f.push(r(f) + "rotate(", null, o) - 2, x: et(l, u) })) : u && f.push(r(f) + "rotate(" + u + o);
  }
  function c(l, u, f, h) {
    l !== u ? h.push({ i: f.push(r(f) + "skewX(", null, o) - 2, x: et(l, u) }) : u && f.push(r(f) + "skewX(" + u + o);
  }
  function d(l, u, f, h, p, y) {
    if (l !== f || u !== h) {
      var v = p.push(r(p) + "scale(", null, ",", null, ")");
      y.push({ i: v - 4, x: et(l, f) }, { i: v - 2, x: et(u, h) });
    } else (f !== 1 || h !== 1) && p.push(r(p) + "scale(" + f + "," + h + ")");
  }
  return function(l, u) {
    var f = [], h = [];
    return l = e(l), u = e(u), i(l.translateX, l.translateY, u.translateX, u.translateY, f, h), s(l.rotate, u.rotate, f, h), c(l.skewX, u.skewX, f, h), d(l.scaleX, l.scaleY, u.scaleX, u.scaleY, f, h), l = u = null, function(p) {
      for (var y = -1, v = h.length, w; ++y < v; ) f[(w = h[y]).i] = w.x(p);
      return f.join("");
    };
  };
}
var Wf = cc(Ff, "px, ", "px)", "deg)"), Xf = cc(Bf, ", ", ")", ")"), Yf = 1e-12;
function ms(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function qf(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Uf(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const Mo = (function e(t, n, o) {
  function r(i, s) {
    var c = i[0], d = i[1], l = i[2], u = s[0], f = s[1], h = s[2], p = u - c, y = f - d, v = p * p + y * y, w, m;
    if (v < Yf)
      m = Math.log(h / l) / t, w = function(j) {
        return [
          c + j * p,
          d + j * y,
          l * Math.exp(t * j * m)
        ];
      };
    else {
      var b = Math.sqrt(v), g = (h * h - l * l + o * v) / (2 * l * n * b), x = (h * h - l * l - o * v) / (2 * h * n * b), I = Math.log(Math.sqrt(g * g + 1) - g), E = Math.log(Math.sqrt(x * x + 1) - x);
      m = (E - I) / t, w = function(j) {
        var P = j * m, T = ms(I), A = l / (n * b) * (T * Uf(t * P + I) - qf(I));
        return [
          c + A * p,
          d + A * y,
          l * T / ms(t * P + I)
        ];
      };
    }
    return w.duration = m * 1e3 * t / Math.SQRT2, w;
  }
  return r.rho = function(i) {
    var s = Math.max(1e-3, +i), c = s * s, d = c * c;
    return e(s, c, d);
  }, r;
})(Math.SQRT2, 2, 4);
var Gt = 0, Cn = 0, vn = 0, lc = 1e3, Fo, jn, Bo = 0, Mt = 0, or = 0, zn = typeof performance == "object" && performance.now ? performance : Date, dc = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function ki() {
  return Mt || (dc(Zf), Mt = zn.now() + or);
}
function Zf() {
  Mt = 0;
}
function Wo() {
  this._call = this._time = this._next = null;
}
Wo.prototype = uc.prototype = {
  constructor: Wo,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? ki() : +n) + (t == null ? 0 : +t), !this._next && jn !== this && (jn ? jn._next = this : Fo = this, jn = this), this._call = e, this._time = n, si();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, si());
  }
};
function uc(e, t, n) {
  var o = new Wo();
  return o.restart(e, t, n), o;
}
function Kf() {
  ki(), ++Gt;
  for (var e = Fo, t; e; )
    (t = Mt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --Gt;
}
function xs() {
  Mt = (Bo = zn.now()) + or, Gt = Cn = 0;
  try {
    Kf();
  } finally {
    Gt = 0, Jf(), Mt = 0;
  }
}
function Gf() {
  var e = zn.now(), t = e - Bo;
  t > lc && (or -= t, Bo = e);
}
function Jf() {
  for (var e, t = Fo, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Fo = n);
  jn = e, si(o);
}
function si(e) {
  if (!Gt) {
    Cn && (Cn = clearTimeout(Cn));
    var t = e - Mt;
    t > 24 ? (e < 1 / 0 && (Cn = setTimeout(xs, e - zn.now() - or)), vn && (vn = clearInterval(vn))) : (vn || (Bo = zn.now(), vn = setInterval(Gf, lc)), Gt = 1, dc(xs));
  }
}
function ws(e, t, n) {
  var o = new Wo();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var Qf = tr("start", "end", "cancel", "interrupt"), eh = [], fc = 0, vs = 1, ai = 2, Do = 3, bs = 4, ci = 5, Po = 6;
function rr(e, t, n, o, r, i) {
  var s = e.__transition;
  if (!s) e.__transition = {};
  else if (n in s) return;
  th(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: Qf,
    tween: eh,
    time: i.time,
    delay: i.delay,
    duration: i.duration,
    ease: i.ease,
    timer: null,
    state: fc
  });
}
function Ii(e, t) {
  var n = Je(e, t);
  if (n.state > fc) throw new Error("too late; already scheduled");
  return n;
}
function nt(e, t) {
  var n = Je(e, t);
  if (n.state > Do) throw new Error("too late; already running");
  return n;
}
function Je(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function th(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = uc(i, 0, n.time);
  function i(l) {
    n.state = vs, n.timer.restart(s, n.delay, n.time), n.delay <= l && s(l - n.delay);
  }
  function s(l) {
    var u, f, h, p;
    if (n.state !== vs) return d();
    for (u in o)
      if (p = o[u], p.name === n.name) {
        if (p.state === Do) return ws(s);
        p.state === bs ? (p.state = Po, p.timer.stop(), p.on.call("interrupt", e, e.__data__, p.index, p.group), delete o[u]) : +u < t && (p.state = Po, p.timer.stop(), p.on.call("cancel", e, e.__data__, p.index, p.group), delete o[u]);
      }
    if (ws(function() {
      n.state === Do && (n.state = bs, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = ai, n.on.call("start", e, e.__data__, n.index, n.group), n.state === ai) {
      for (n.state = Do, r = new Array(h = n.tween.length), u = 0, f = -1; u < h; ++u)
        (p = n.tween[u].value.call(e, e.__data__, n.index, n.group)) && (r[++f] = p);
      r.length = f + 1;
    }
  }
  function c(l) {
    for (var u = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(d), n.state = ci, 1), f = -1, h = r.length; ++f < h; )
      r[f].call(e, u);
    n.state === ci && (n.on.call("end", e, e.__data__, n.index, n.group), d());
  }
  function d() {
    n.state = Po, n.timer.stop(), delete o[t];
    for (var l in o) return;
    delete e.__transition;
  }
}
function $o(e, t) {
  var n = e.__transition, o, r, i = !0, s;
  if (n) {
    t = t == null ? null : t + "";
    for (s in n) {
      if ((o = n[s]).name !== t) {
        i = !1;
        continue;
      }
      r = o.state > ai && o.state < ci, o.state = Po, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[s];
    }
    i && delete e.__transition;
  }
}
function nh(e) {
  return this.each(function() {
    $o(this, e);
  });
}
function oh(e, t) {
  var n, o;
  return function() {
    var r = nt(this, e), i = r.tween;
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
function rh(e, t, n) {
  var o, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var i = nt(this, e), s = i.tween;
    if (s !== o) {
      r = (o = s).slice();
      for (var c = { name: t, value: n }, d = 0, l = r.length; d < l; ++d)
        if (r[d].name === t) {
          r[d] = c;
          break;
        }
      d === l && r.push(c);
    }
    i.tween = r;
  };
}
function ih(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = Je(this.node(), n).tween, r = 0, i = o.length, s; r < i; ++r)
      if ((s = o[r]).name === e)
        return s.value;
    return null;
  }
  return this.each((t == null ? oh : rh)(n, e, t));
}
function Ci(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = nt(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return Je(r, o).value[t];
  };
}
function hc(e, t) {
  var n;
  return (typeof t == "number" ? et : t instanceof At ? Oo : (n = At(t)) ? (t = n, Oo) : sc)(e, t);
}
function sh(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function ah(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function ch(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttribute(e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function lh(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttributeNS(e.space, e.local);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function dh(e, t, n) {
  var o, r, i;
  return function() {
    var s, c = n(this), d;
    return c == null ? void this.removeAttribute(e) : (s = this.getAttribute(e), d = c + "", s === d ? null : s === o && d === r ? i : (r = d, i = t(o = s, c)));
  };
}
function uh(e, t, n) {
  var o, r, i;
  return function() {
    var s, c = n(this), d;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (s = this.getAttributeNS(e.space, e.local), d = c + "", s === d ? null : s === o && d === r ? i : (r = d, i = t(o = s, c)));
  };
}
function fh(e, t) {
  var n = nr(e), o = n === "transform" ? Xf : hc;
  return this.attrTween(e, typeof t == "function" ? (n.local ? uh : dh)(n, o, Ci(this, "attr." + e, t)) : t == null ? (n.local ? ah : sh)(n) : (n.local ? lh : ch)(n, o, t));
}
function hh(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function ph(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function gh(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && ph(e, i)), n;
  }
  return r._value = t, r;
}
function yh(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && hh(e, i)), n;
  }
  return r._value = t, r;
}
function mh(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = nr(e);
  return this.tween(n, (o.local ? gh : yh)(o, t));
}
function xh(e, t) {
  return function() {
    Ii(this, e).delay = +t.apply(this, arguments);
  };
}
function wh(e, t) {
  return t = +t, function() {
    Ii(this, e).delay = t;
  };
}
function vh(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? xh : wh)(t, e)) : Je(this.node(), t).delay;
}
function bh(e, t) {
  return function() {
    nt(this, e).duration = +t.apply(this, arguments);
  };
}
function Nh(e, t) {
  return t = +t, function() {
    nt(this, e).duration = t;
  };
}
function Sh(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? bh : Nh)(t, e)) : Je(this.node(), t).duration;
}
function Eh(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    nt(this, e).ease = t;
  };
}
function kh(e) {
  var t = this._id;
  return arguments.length ? this.each(Eh(t, e)) : Je(this.node(), t).ease;
}
function Ih(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    nt(this, e).ease = n;
  };
}
function Ch(e) {
  if (typeof e != "function") throw new Error();
  return this.each(Ih(this._id, e));
}
function jh(e) {
  typeof e != "function" && (e = Wa(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = [], d, l = 0; l < s; ++l)
      (d = i[l]) && e.call(d, d.__data__, l, i) && c.push(d);
  return new ct(o, this._parents, this._name, this._id);
}
function _h(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, i = Math.min(o, r), s = new Array(o), c = 0; c < i; ++c)
    for (var d = t[c], l = n[c], u = d.length, f = s[c] = new Array(u), h, p = 0; p < u; ++p)
      (h = d[p] || l[p]) && (f[p] = h);
  for (; c < o; ++c)
    s[c] = t[c];
  return new ct(s, this._parents, this._name, this._id);
}
function Ah(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function Mh(e, t, n) {
  var o, r, i = Ah(t) ? Ii : nt;
  return function() {
    var s = i(this, e), c = s.on;
    c !== o && (r = (o = c).copy()).on(t, n), s.on = r;
  };
}
function Dh(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Je(this.node(), n).on.on(e) : this.each(Mh(n, e, t));
}
function Ph(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function $h() {
  return this.on("end.remove", Ph(this._id));
}
function Th(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = bi(e));
  for (var o = this._groups, r = o.length, i = new Array(r), s = 0; s < r; ++s)
    for (var c = o[s], d = c.length, l = i[s] = new Array(d), u, f, h = 0; h < d; ++h)
      (u = c[h]) && (f = e.call(u, u.__data__, h, c)) && ("__data__" in u && (f.__data__ = u.__data__), l[h] = f, rr(l[h], t, n, h, l, Je(u, n)));
  return new ct(i, this._parents, t, n);
}
function zh(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Ba(e));
  for (var o = this._groups, r = o.length, i = [], s = [], c = 0; c < r; ++c)
    for (var d = o[c], l = d.length, u, f = 0; f < l; ++f)
      if (u = d[f]) {
        for (var h = e.call(u, u.__data__, f, d), p, y = Je(u, n), v = 0, w = h.length; v < w; ++v)
          (p = h[v]) && rr(p, t, n, v, h, y);
        i.push(h), s.push(u);
      }
  return new ct(i, s, t, n);
}
var Rh = Xn.prototype.constructor;
function Lh() {
  return new Rh(this._groups, this._parents);
}
function Vh(e, t) {
  var n, o, r;
  return function() {
    var i = Kt(this, e), s = (this.style.removeProperty(e), Kt(this, e));
    return i === s ? null : i === n && s === o ? r : r = t(n = i, o = s);
  };
}
function pc(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Hh(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = Kt(this, e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Oh(e, t, n) {
  var o, r, i;
  return function() {
    var s = Kt(this, e), c = n(this), d = c + "";
    return c == null && (d = c = (this.style.removeProperty(e), Kt(this, e))), s === d ? null : s === o && d === r ? i : (r = d, i = t(o = s, c));
  };
}
function Fh(e, t) {
  var n, o, r, i = "style." + t, s = "end." + i, c;
  return function() {
    var d = nt(this, e), l = d.on, u = d.value[i] == null ? c || (c = pc(t)) : void 0;
    (l !== n || r !== u) && (o = (n = l).copy()).on(s, r = u), d.on = o;
  };
}
function Bh(e, t, n) {
  var o = (e += "") == "transform" ? Wf : hc;
  return t == null ? this.styleTween(e, Vh(e, o)).on("end.style." + e, pc(e)) : typeof t == "function" ? this.styleTween(e, Oh(e, o, Ci(this, "style." + e, t))).each(Fh(this._id, e)) : this.styleTween(e, Hh(e, o, t), n).on("end.style." + e, null);
}
function Wh(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function Xh(e, t, n) {
  var o, r;
  function i() {
    var s = t.apply(this, arguments);
    return s !== r && (o = (r = s) && Wh(e, s, n)), o;
  }
  return i._value = t, i;
}
function Yh(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, Xh(e, t, n ?? ""));
}
function qh(e) {
  return function() {
    this.textContent = e;
  };
}
function Uh(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function Zh(e) {
  return this.tween("text", typeof e == "function" ? Uh(Ci(this, "text", e)) : qh(e == null ? "" : e + ""));
}
function Kh(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Gh(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Kh(r)), t;
  }
  return o._value = e, o;
}
function Jh(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, Gh(e));
}
function Qh() {
  for (var e = this._name, t = this._id, n = gc(), o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], c = s.length, d, l = 0; l < c; ++l)
      if (d = s[l]) {
        var u = Je(d, t);
        rr(d, e, n, l, s, {
          time: u.time + u.delay + u.duration,
          delay: 0,
          duration: u.duration,
          ease: u.ease
        });
      }
  return new ct(o, this._parents, e, n);
}
function ep() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(i, s) {
    var c = { value: s }, d = { value: function() {
      --r === 0 && i();
    } };
    n.each(function() {
      var l = nt(this, o), u = l.on;
      u !== e && (t = (e = u).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(d)), l.on = t;
    }), r === 0 && i();
  });
}
var tp = 0;
function ct(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function gc() {
  return ++tp;
}
var it = Xn.prototype;
ct.prototype = {
  constructor: ct,
  select: Th,
  selectAll: zh,
  selectChild: it.selectChild,
  selectChildren: it.selectChildren,
  filter: jh,
  merge: _h,
  selection: Lh,
  transition: Qh,
  call: it.call,
  nodes: it.nodes,
  node: it.node,
  size: it.size,
  empty: it.empty,
  each: it.each,
  on: Dh,
  attr: fh,
  attrTween: mh,
  style: Bh,
  styleTween: Yh,
  text: Zh,
  textTween: Jh,
  remove: $h,
  tween: ih,
  delay: vh,
  duration: Sh,
  ease: kh,
  easeVarying: Ch,
  end: ep,
  [Symbol.iterator]: it[Symbol.iterator]
};
function np(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var op = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: np
};
function rp(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function ip(e) {
  var t, n;
  e instanceof ct ? (t = e._id, e = e._name) : (t = gc(), (n = op).time = ki(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], c = s.length, d, l = 0; l < c; ++l)
      (d = s[l]) && rr(d, e, t, l, s, n || rp(d, t));
  return new ct(o, this._parents, e, t);
}
Xn.prototype.interrupt = nh;
Xn.prototype.transition = ip;
const So = (e) => () => e;
function sp(e, {
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
function at(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
at.prototype = {
  constructor: at,
  scale: function(e) {
    return e === 1 ? this : new at(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new at(this.k, this.x + this.k * e, this.y + this.k * t);
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
var ir = new at(1, 0, 0);
yc.prototype = at.prototype;
function yc(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return ir;
  return e.__zoom;
}
function Or(e) {
  e.stopImmediatePropagation();
}
function bn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function ap(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function cp() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function Ns() {
  return this.__zoom || ir;
}
function lp(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function dp() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function up(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], i = e.invertY(t[0][1]) - n[0][1], s = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    s > i ? (i + s) / 2 : Math.min(0, i) || Math.max(0, s)
  );
}
function mc() {
  var e = ap, t = cp, n = up, o = lp, r = dp, i = [0, 1 / 0], s = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, d = Mo, l = tr("start", "zoom", "end"), u, f, h, p = 500, y = 150, v = 0, w = 10;
  function m(S) {
    S.property("__zoom", Ns).on("wheel.zoom", P, { passive: !1 }).on("mousedown.zoom", T).on("dblclick.zoom", A).filter(r).on("touchstart.zoom", k).on("touchmove.zoom", R).on("touchend.zoom touchcancel.zoom", O).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  m.transform = function(S, _, C, M) {
    var z = S.selection ? S.selection() : S;
    z.property("__zoom", Ns), S !== z ? I(S, _, C, M) : z.interrupt().each(function() {
      E(this, arguments).event(M).start().zoom(null, typeof _ == "function" ? _.apply(this, arguments) : _).end();
    });
  }, m.scaleBy = function(S, _, C, M) {
    m.scaleTo(S, function() {
      var z = this.__zoom.k, $ = typeof _ == "function" ? _.apply(this, arguments) : _;
      return z * $;
    }, C, M);
  }, m.scaleTo = function(S, _, C, M) {
    m.transform(S, function() {
      var z = t.apply(this, arguments), $ = this.__zoom, W = C == null ? x(z) : typeof C == "function" ? C.apply(this, arguments) : C, F = $.invert(W), B = typeof _ == "function" ? _.apply(this, arguments) : _;
      return n(g(b($, B), W, F), z, s);
    }, C, M);
  }, m.translateBy = function(S, _, C, M) {
    m.transform(S, function() {
      return n(this.__zoom.translate(
        typeof _ == "function" ? _.apply(this, arguments) : _,
        typeof C == "function" ? C.apply(this, arguments) : C
      ), t.apply(this, arguments), s);
    }, null, M);
  }, m.translateTo = function(S, _, C, M, z) {
    m.transform(S, function() {
      var $ = t.apply(this, arguments), W = this.__zoom, F = M == null ? x($) : typeof M == "function" ? M.apply(this, arguments) : M;
      return n(ir.translate(F[0], F[1]).scale(W.k).translate(
        typeof _ == "function" ? -_.apply(this, arguments) : -_,
        typeof C == "function" ? -C.apply(this, arguments) : -C
      ), $, s);
    }, M, z);
  };
  function b(S, _) {
    return _ = Math.max(i[0], Math.min(i[1], _)), _ === S.k ? S : new at(_, S.x, S.y);
  }
  function g(S, _, C) {
    var M = _[0] - C[0] * S.k, z = _[1] - C[1] * S.k;
    return M === S.x && z === S.y ? S : new at(S.k, M, z);
  }
  function x(S) {
    return [(+S[0][0] + +S[1][0]) / 2, (+S[0][1] + +S[1][1]) / 2];
  }
  function I(S, _, C, M) {
    S.on("start.zoom", function() {
      E(this, arguments).event(M).start();
    }).on("interrupt.zoom end.zoom", function() {
      E(this, arguments).event(M).end();
    }).tween("zoom", function() {
      var z = this, $ = arguments, W = E(z, $).event(M), F = t.apply(z, $), B = C == null ? x(F) : typeof C == "function" ? C.apply(z, $) : C, Z = Math.max(F[1][0] - F[0][0], F[1][1] - F[0][1]), K = z.__zoom, ne = typeof _ == "function" ? _.apply(z, $) : _, se = d(K.invert(B).concat(Z / K.k), ne.invert(B).concat(Z / ne.k));
      return function(Q) {
        if (Q === 1) Q = ne;
        else {
          var L = se(Q), q = Z / L[2];
          Q = new at(q, B[0] - L[0] * q, B[1] - L[1] * q);
        }
        W.zoom(null, Q);
      };
    });
  }
  function E(S, _, C) {
    return !C && S.__zooming || new j(S, _);
  }
  function j(S, _) {
    this.that = S, this.args = _, this.active = 0, this.sourceEvent = null, this.extent = t.apply(S, _), this.taps = 0;
  }
  j.prototype = {
    event: function(S) {
      return S && (this.sourceEvent = S), this;
    },
    start: function() {
      return ++this.active === 1 && (this.that.__zooming = this, this.emit("start")), this;
    },
    zoom: function(S, _) {
      return this.mouse && S !== "mouse" && (this.mouse[1] = _.invert(this.mouse[0])), this.touch0 && S !== "touch" && (this.touch0[1] = _.invert(this.touch0[0])), this.touch1 && S !== "touch" && (this.touch1[1] = _.invert(this.touch1[0])), this.that.__zoom = _, this.emit("zoom"), this;
    },
    end: function() {
      return --this.active === 0 && (delete this.that.__zooming, this.emit("end")), this;
    },
    emit: function(S) {
      var _ = He(this.that).datum();
      l.call(
        S,
        this.that,
        new sp(S, {
          sourceEvent: this.sourceEvent,
          target: m,
          transform: this.that.__zoom,
          dispatch: l
        }),
        _
      );
    }
  };
  function P(S, ..._) {
    if (!e.apply(this, arguments)) return;
    var C = E(this, _).event(S), M = this.__zoom, z = Math.max(i[0], Math.min(i[1], M.k * Math.pow(2, o.apply(this, arguments)))), $ = Ye(S);
    if (C.wheel)
      (C.mouse[0][0] !== $[0] || C.mouse[0][1] !== $[1]) && (C.mouse[1] = M.invert(C.mouse[0] = $)), clearTimeout(C.wheel);
    else {
      if (M.k === z) return;
      C.mouse = [$, M.invert($)], $o(this), C.start();
    }
    bn(S), C.wheel = setTimeout(W, y), C.zoom("mouse", n(g(b(M, z), C.mouse[0], C.mouse[1]), C.extent, s));
    function W() {
      C.wheel = null, C.end();
    }
  }
  function T(S, ..._) {
    if (h || !e.apply(this, arguments)) return;
    var C = S.currentTarget, M = E(this, _, !0).event(S), z = He(S.view).on("mousemove.zoom", B, !0).on("mouseup.zoom", Z, !0), $ = Ye(S, C), W = S.clientX, F = S.clientY;
    ec(S.view), Or(S), M.mouse = [$, this.__zoom.invert($)], $o(this), M.start();
    function B(K) {
      if (bn(K), !M.moved) {
        var ne = K.clientX - W, se = K.clientY - F;
        M.moved = ne * ne + se * se > v;
      }
      M.event(K).zoom("mouse", n(g(M.that.__zoom, M.mouse[0] = Ye(K, C), M.mouse[1]), M.extent, s));
    }
    function Z(K) {
      z.on("mousemove.zoom mouseup.zoom", null), tc(K.view, M.moved), bn(K), M.event(K).end();
    }
  }
  function A(S, ..._) {
    if (e.apply(this, arguments)) {
      var C = this.__zoom, M = Ye(S.changedTouches ? S.changedTouches[0] : S, this), z = C.invert(M), $ = C.k * (S.shiftKey ? 0.5 : 2), W = n(g(b(C, $), M, z), t.apply(this, _), s);
      bn(S), c > 0 ? He(this).transition().duration(c).call(I, W, M, S) : He(this).call(m.transform, W, M, S);
    }
  }
  function k(S, ..._) {
    if (e.apply(this, arguments)) {
      var C = S.touches, M = C.length, z = E(this, _, S.changedTouches.length === M).event(S), $, W, F, B;
      for (Or(S), W = 0; W < M; ++W)
        F = C[W], B = Ye(F, this), B = [B, this.__zoom.invert(B), F.identifier], z.touch0 ? !z.touch1 && z.touch0[2] !== B[2] && (z.touch1 = B, z.taps = 0) : (z.touch0 = B, $ = !0, z.taps = 1 + !!u);
      u && (u = clearTimeout(u)), $ && (z.taps < 2 && (f = B[0], u = setTimeout(function() {
        u = null;
      }, p)), $o(this), z.start());
    }
  }
  function R(S, ..._) {
    if (this.__zooming) {
      var C = E(this, _).event(S), M = S.changedTouches, z = M.length, $, W, F, B;
      for (bn(S), $ = 0; $ < z; ++$)
        W = M[$], F = Ye(W, this), C.touch0 && C.touch0[2] === W.identifier ? C.touch0[0] = F : C.touch1 && C.touch1[2] === W.identifier && (C.touch1[0] = F);
      if (W = C.that.__zoom, C.touch1) {
        var Z = C.touch0[0], K = C.touch0[1], ne = C.touch1[0], se = C.touch1[1], Q = (Q = ne[0] - Z[0]) * Q + (Q = ne[1] - Z[1]) * Q, L = (L = se[0] - K[0]) * L + (L = se[1] - K[1]) * L;
        W = b(W, Math.sqrt(Q / L)), F = [(Z[0] + ne[0]) / 2, (Z[1] + ne[1]) / 2], B = [(K[0] + se[0]) / 2, (K[1] + se[1]) / 2];
      } else if (C.touch0) F = C.touch0[0], B = C.touch0[1];
      else return;
      C.zoom("touch", n(g(W, F, B), C.extent, s));
    }
  }
  function O(S, ..._) {
    if (this.__zooming) {
      var C = E(this, _).event(S), M = S.changedTouches, z = M.length, $, W;
      for (Or(S), h && clearTimeout(h), h = setTimeout(function() {
        h = null;
      }, p), $ = 0; $ < z; ++$)
        W = M[$], C.touch0 && C.touch0[2] === W.identifier ? delete C.touch0 : C.touch1 && C.touch1[2] === W.identifier && delete C.touch1;
      if (C.touch1 && !C.touch0 && (C.touch0 = C.touch1, delete C.touch1), C.touch0) C.touch0[1] = this.__zoom.invert(C.touch0[0]);
      else if (C.end(), C.taps === 2 && (W = Ye(W, this), Math.hypot(f[0] - W[0], f[1] - W[1]) < w)) {
        var F = He(this).on("dblclick.zoom");
        F && F.apply(this, arguments);
      }
    }
  }
  return m.wheelDelta = function(S) {
    return arguments.length ? (o = typeof S == "function" ? S : So(+S), m) : o;
  }, m.filter = function(S) {
    return arguments.length ? (e = typeof S == "function" ? S : So(!!S), m) : e;
  }, m.touchable = function(S) {
    return arguments.length ? (r = typeof S == "function" ? S : So(!!S), m) : r;
  }, m.extent = function(S) {
    return arguments.length ? (t = typeof S == "function" ? S : So([[+S[0][0], +S[0][1]], [+S[1][0], +S[1][1]]]), m) : t;
  }, m.scaleExtent = function(S) {
    return arguments.length ? (i[0] = +S[0], i[1] = +S[1], m) : [i[0], i[1]];
  }, m.translateExtent = function(S) {
    return arguments.length ? (s[0][0] = +S[0][0], s[1][0] = +S[1][0], s[0][1] = +S[0][1], s[1][1] = +S[1][1], m) : [[s[0][0], s[0][1]], [s[1][0], s[1][1]]];
  }, m.constrain = function(S) {
    return arguments.length ? (n = S, m) : n;
  }, m.duration = function(S) {
    return arguments.length ? (c = +S, m) : c;
  }, m.interpolate = function(S) {
    return arguments.length ? (d = S, m) : d;
  }, m.on = function() {
    var S = l.on.apply(l, arguments);
    return S === l ? m : S;
  }, m.clickDistance = function(S) {
    return arguments.length ? (v = (S = +S) * S, m) : Math.sqrt(v);
  }, m.tapDistance = function(S) {
    return arguments.length ? (w = +S, m) : w;
  }, m;
}
const Fe = {
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
}, Rn = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], xc = ["Enter", " ", "Escape"], wc = {
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
var Jt;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(Jt || (Jt = {}));
var jt;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(jt || (jt = {}));
var Ln;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Ln || (Ln = {}));
const vc = {
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
var pt;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(pt || (pt = {}));
var Xo;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(Xo || (Xo = {}));
var te;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(te || (te = {}));
const Ss = {
  [te.Left]: te.Right,
  [te.Right]: te.Left,
  [te.Top]: te.Bottom,
  [te.Bottom]: te.Top
};
function bc(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Nc = (e) => "id" in e && "source" in e && "target" in e, fp = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), ji = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), qn = (e, t = [0, 0]) => {
  const { width: n, height: o } = lt(e), r = e.origin ?? t, i = n * r[0], s = o * r[1];
  return {
    x: e.position.x - i,
    y: e.position.y - s
  };
}, hp = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const i = typeof r == "string";
    let s = !t.nodeLookup && !i ? r : void 0;
    t.nodeLookup && (s = i ? t.nodeLookup.get(r) : ji(r) ? r : t.nodeLookup.get(r.id));
    const c = s ? Yo(s, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return sr(o, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return ar(n);
}, Un = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = sr(n, Yo(r)), o = !0);
  }), o ? ar(n) : { x: 0, y: 0, width: 0, height: 0 };
}, _i = (e, t, [n, o, r] = [0, 0, 1], i = !1, s = !1) => {
  const c = {
    ...rn(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, d = [];
  for (const l of e.values()) {
    const { measured: u, selectable: f = !0, hidden: h = !1 } = l;
    if (s && !f || h)
      continue;
    const p = u.width ?? l.width ?? l.initialWidth ?? null, y = u.height ?? l.height ?? l.initialHeight ?? null, v = Vn(c, en(l)), w = (p ?? 0) * (y ?? 0), m = i && v > 0;
    (!l.internals.handleBounds || m || v >= w || l.dragging) && d.push(l);
  }
  return d;
}, pp = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function gp(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function yp({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: i }, s) {
  if (e.size === 0)
    return !0;
  const c = gp(e, s), d = Un(c), l = Mi(d, t, n, s?.minZoom ?? r, s?.maxZoom ?? i, s?.padding ?? 0.1);
  return await o.setViewport(l, {
    duration: s?.duration,
    ease: s?.ease,
    interpolate: s?.interpolate
  }), !0;
}
function Sc({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: i }) {
  const s = n.get(e), c = s.parentId ? n.get(s.parentId) : void 0, { x: d, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, u = s.origin ?? o;
  let f = s.extent || r;
  if (s.extent === "parent" && !s.expandParent)
    if (!c)
      i?.("005", Fe.error005());
    else {
      const p = c.measured.width, y = c.measured.height;
      p && y && (f = [
        [d, l],
        [d + p, l + y]
      ]);
    }
  else c && Pt(s.extent) && (f = [
    [s.extent[0][0] + d, s.extent[0][1] + l],
    [s.extent[1][0] + d, s.extent[1][1] + l]
  ]);
  const h = Pt(f) ? Dt(t, f, s.measured) : t;
  return (s.measured.width === void 0 || s.measured.height === void 0) && i?.("015", Fe.error015()), {
    position: {
      x: h.x - d + (s.measured.width ?? 0) * u[0],
      y: h.y - l + (s.measured.height ?? 0) * u[1]
    },
    positionAbsolute: h
  };
}
async function mp({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const i = new Set(e.map((h) => h.id)), s = [];
  for (const h of n) {
    if (h.deletable === !1)
      continue;
    const p = i.has(h.id), y = !p && h.parentId && s.find((v) => v.id === h.parentId);
    (p || y) && s.push(h);
  }
  const c = new Set(t.map((h) => h.id)), d = o.filter((h) => h.deletable !== !1), u = pp(s, d);
  for (const h of d)
    c.has(h.id) && !u.find((y) => y.id === h.id) && u.push(h);
  if (!r)
    return {
      edges: u,
      nodes: s
    };
  const f = await r({
    nodes: s,
    edges: u
  });
  return typeof f == "boolean" ? f ? { edges: u, nodes: s } : { edges: [], nodes: [] } : f;
}
const Qt = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), Dt = (e = { x: 0, y: 0 }, t, n) => ({
  x: Qt(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: Qt(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Ec(e, t, n) {
  const { width: o, height: r } = lt(n), { x: i, y: s } = n.internals.positionAbsolute;
  return Dt(e, [
    [i, s],
    [i + o, s + r]
  ], t);
}
const Es = (e, t, n) => e < t ? Qt(Math.abs(e - t), 1, t) / t : e > n ? -Qt(Math.abs(e - n), 1, t) / t : 0, Ai = (e, t, n = 15, o = 40) => {
  const r = Es(e.x, o, t.width - o) * n, i = Es(e.y, o, t.height - o) * n;
  return [r, i];
}, sr = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), li = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), ar = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), en = (e, t = [0, 0]) => {
  const { x: n, y: o } = ji(e) ? e.internals.positionAbsolute : qn(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, Yo = (e, t = [0, 0]) => {
  const { x: n, y: o } = ji(e) ? e.internals.positionAbsolute : qn(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, kc = (e, t) => ar(sr(li(e), li(t))), Vn = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, ks = (e) => Ue(e.width) && Ue(e.height) && Ue(e.x) && Ue(e.y), Ue = (e) => !isNaN(e) && isFinite(e), Ic = (e, t) => (n, o) => {
}, Zn = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), rn = ({ x: e, y: t }, [n, o, r], i = !1, s = [1, 1]) => {
  const c = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return i ? Zn(c, s) : c;
}, tn = ({ x: e, y: t }, [n, o, r]) => ({
  x: e * r + n,
  y: t * r + o
});
function Bt(e, t) {
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
function xp(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = Bt(e, n), r = Bt(e, t);
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
    const o = Bt(e.top ?? e.y ?? 0, n), r = Bt(e.bottom ?? e.y ?? 0, n), i = Bt(e.left ?? e.x ?? 0, t), s = Bt(e.right ?? e.x ?? 0, t);
    return { top: o, right: s, bottom: r, left: i, x: i + s, y: o + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function wp(e, t, n, o, r, i) {
  const { x: s, y: c } = tn(e, [t, n, o]), { x: d, y: l } = tn({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), u = r - d, f = i - l;
  return {
    left: Math.floor(s),
    top: Math.floor(c),
    right: Math.floor(u),
    bottom: Math.floor(f)
  };
}
const Mi = (e, t, n, o, r, i) => {
  const s = xp(i, t, n), c = (t - s.x) / e.width, d = (n - s.y) / e.height, l = Math.min(c, d), u = Qt(l, o, r), f = e.x + e.width / 2, h = e.y + e.height / 2, p = t / 2 - f * u, y = n / 2 - h * u, v = wp(e, p, y, u, t, n), w = {
    left: Math.min(v.left - s.left, 0),
    top: Math.min(v.top - s.top, 0),
    right: Math.min(v.right - s.right, 0),
    bottom: Math.min(v.bottom - s.bottom, 0)
  };
  return {
    x: p - w.left + w.right,
    y: y - w.top + w.bottom,
    zoom: u
  };
}, Hn = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function Pt(e) {
  return e != null && e !== "parent";
}
function lt(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function Cc(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function jc(e, t = { width: 0, height: 0 }, n, o, r) {
  const i = { ...e }, s = o.get(n);
  if (s) {
    const c = s.origin || r;
    i.x += s.internals.positionAbsolute.x - (t.width ?? 0) * c[0], i.y += s.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return i;
}
function Is(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function vp() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function bp(e) {
  return { ...wc, ...e || {} };
}
function Mn(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: i, y: s } = Ze(e), c = rn({ x: i - (r?.left ?? 0), y: s - (r?.top ?? 0) }, o), { x: d, y: l } = n ? Zn(c, t) : c;
  return {
    xSnapped: d,
    ySnapped: l,
    ...c
  };
}
const Di = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), _c = (e) => e?.getRootNode?.() || window?.document, Np = ["INPUT", "SELECT", "TEXTAREA"];
function Ac(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : Np.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const Mc = (e) => "clientX" in e, Ze = (e, t) => {
  const n = Mc(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, Cs = (e, t, n, o, r) => {
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
      ...Di(s)
    };
  });
};
function Dc({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: i, targetControlX: s, targetControlY: c }) {
  const d = e * 0.125 + r * 0.375 + s * 0.375 + n * 0.125, l = t * 0.125 + i * 0.375 + c * 0.375 + o * 0.125, u = Math.abs(d - e), f = Math.abs(l - t);
  return [d, l, u, f];
}
function Eo(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function js({ pos: e, x1: t, y1: n, x2: o, y2: r, c: i }) {
  switch (e) {
    case te.Left:
      return [t - Eo(t - o, i), n];
    case te.Right:
      return [t + Eo(o - t, i), n];
    case te.Top:
      return [t, n - Eo(n - r, i)];
    case te.Bottom:
      return [t, n + Eo(r - n, i)];
  }
}
function Pc({ sourceX: e, sourceY: t, sourcePosition: n = te.Bottom, targetX: o, targetY: r, targetPosition: i = te.Top, curvature: s = 0.25 }) {
  const [c, d] = js({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: s
  }), [l, u] = js({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: s
  }), [f, h, p, y] = Dc({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: r,
    sourceControlX: c,
    sourceControlY: d,
    targetControlX: l,
    targetControlY: u
  });
  return [
    `M${e},${t} C${c},${d} ${l},${u} ${o},${r}`,
    f,
    h,
    p,
    y
  ];
}
function $c({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, i = n < e ? n + r : n - r, s = Math.abs(o - t) / 2, c = o < t ? o + s : o - s;
  return [i, c, r, s];
}
function Sp({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: i = "basic" }) {
  if (i === "manual")
    return o;
  const s = r && n ? o + 1e3 : o, c = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return s + c;
}
function Ep({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const i = sr(Yo(e), Yo(t));
  i.x === i.x2 && (i.x2 += 1), i.y === i.y2 && (i.y2 += 1);
  const s = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return Vn(s, ar(i)) > 0;
}
const Tc = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, kp = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), Ip = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Fe.error006()), t;
  const o = n.getEdgeId || Tc;
  let r;
  return Nc(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, kp(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, Cp = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: r, ...i } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", Fe.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return o.onError?.("007", Fe.error007(r)), n;
  const c = o.getEdgeId || Tc, d = {
    ...i,
    id: o.shouldReplaceId ? c(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== r).concat(d);
};
function zc({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, i, s, c] = $c({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, i, s, c];
}
const _s = {
  [te.Left]: { x: -1, y: 0 },
  [te.Right]: { x: 1, y: 0 },
  [te.Top]: { x: 0, y: -1 },
  [te.Bottom]: { x: 0, y: 1 }
}, jp = ({ source: e, sourcePosition: t = te.Bottom, target: n }) => t === te.Left || t === te.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, As = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function _p({ source: e, sourcePosition: t = te.Bottom, target: n, targetPosition: o = te.Top, center: r, offset: i, stepPosition: s }) {
  const c = _s[t], d = _s[o], l = { x: e.x + c.x * i, y: e.y + c.y * i }, u = { x: n.x + d.x * i, y: n.y + d.y * i }, f = jp({
    source: l,
    sourcePosition: t,
    target: u
  }), h = f.x !== 0 ? "x" : "y", p = f[h];
  let y = [], v, w;
  const m = { x: 0, y: 0 }, b = { x: 0, y: 0 }, [, , g, x] = $c({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[h] * d[h] === -1) {
    h === "x" ? (v = r.x ?? l.x + (u.x - l.x) * s, w = r.y ?? (l.y + u.y) / 2) : (v = r.x ?? (l.x + u.x) / 2, w = r.y ?? l.y + (u.y - l.y) * s);
    const P = [
      { x: v, y: l.y },
      { x: v, y: u.y }
    ], T = [
      { x: l.x, y: w },
      { x: u.x, y: w }
    ];
    c[h] === p ? y = h === "x" ? P : T : y = h === "x" ? T : P;
  } else {
    const P = [{ x: l.x, y: u.y }], T = [{ x: u.x, y: l.y }];
    if (h === "x" ? y = c.x === p ? T : P : y = c.y === p ? P : T, t === o) {
      const S = Math.abs(e[h] - n[h]);
      if (S <= i) {
        const _ = Math.min(i - 1, i - S);
        c[h] === p ? m[h] = (l[h] > e[h] ? -1 : 1) * _ : b[h] = (u[h] > n[h] ? -1 : 1) * _;
      }
    }
    if (t !== o) {
      const S = h === "x" ? "y" : "x", _ = c[h] === d[S], C = l[S] > u[S], M = l[S] < u[S];
      (c[h] === 1 && (!_ && C || _ && M) || c[h] !== 1 && (!_ && M || _ && C)) && (y = h === "x" ? P : T);
    }
    const A = { x: l.x + m.x, y: l.y + m.y }, k = { x: u.x + b.x, y: u.y + b.y }, R = Math.max(Math.abs(A.x - y[0].x), Math.abs(k.x - y[0].x)), O = Math.max(Math.abs(A.y - y[0].y), Math.abs(k.y - y[0].y));
    R >= O ? (v = (A.x + k.x) / 2, w = y[0].y) : (v = y[0].x, w = (A.y + k.y) / 2);
  }
  const I = { x: l.x + m.x, y: l.y + m.y }, E = { x: u.x + b.x, y: u.y + b.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...I.x !== y[0].x || I.y !== y[0].y ? [I] : [],
    ...y,
    ...E.x !== y[y.length - 1].x || E.y !== y[y.length - 1].y ? [E] : [],
    n
  ], v, w, g, x];
}
function Ap(e, t, n, o) {
  const r = Math.min(As(e, t) / 2, As(t, n) / 2, o), { x: i, y: s } = t;
  if (e.x === i && i === n.x || e.y === s && s === n.y)
    return `L${i} ${s}`;
  if (e.y === s) {
    const l = e.x < n.x ? -1 : 1, u = e.y < n.y ? 1 : -1;
    return `L ${i + r * l},${s}Q ${i},${s} ${i},${s + r * u}`;
  }
  const c = e.x < n.x ? 1 : -1, d = e.y < n.y ? -1 : 1;
  return `L ${i},${s + r * d}Q ${i},${s} ${i + r * c},${s}`;
}
function qo({ sourceX: e, sourceY: t, sourcePosition: n = te.Bottom, targetX: o, targetY: r, targetPosition: i = te.Top, borderRadius: s = 5, centerX: c, centerY: d, offset: l = 20, stepPosition: u = 0.5 }) {
  const [f, h, p, y, v] = _p({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: i,
    center: { x: c, y: d },
    offset: l,
    stepPosition: u
  });
  let w = `M${f[0].x} ${f[0].y}`;
  for (let m = 1; m < f.length - 1; m++)
    w += Ap(f[m - 1], f[m], f[m + 1], s);
  return w += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [w, h, p, y, v];
}
function Ms(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function Mp(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!Ms(t) || !Ms(n))
    return null;
  const o = t.internals.handleBounds || Ds(t.handles), r = n.internals.handleBounds || Ds(n.handles), i = Ps(o?.source ?? [], e.sourceHandle), s = Ps(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === Jt.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!i || !s)
    return e.onError?.("008", Fe.error008(i ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = i?.position || te.Bottom, d = s?.position || te.Top, l = $t(t, i, c), u = $t(n, s, d);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: u.x,
    targetY: u.y,
    sourcePosition: c,
    targetPosition: d
  };
}
function Ds(e) {
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
function $t(e, t, n = te.Left, o = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, i = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: s, height: c } = t ?? lt(e);
  if (o)
    return { x: r + s / 2, y: i + c / 2 };
  switch (t?.position ?? n) {
    case te.Top:
      return { x: r + s / 2, y: i };
    case te.Right:
      return { x: r + s, y: i + c / 2 };
    case te.Bottom:
      return { x: r + s / 2, y: i + c };
    case te.Left:
      return { x: r, y: i + c / 2 };
  }
}
function Ps(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function di(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function Dp(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const i = /* @__PURE__ */ new Set();
  return e.reduce((s, c) => ([c.markerStart || o, c.markerEnd || r].forEach((d) => {
    if (d && typeof d == "object") {
      const l = di(d, t);
      i.has(l) || (s.push({ id: l, color: d.color || n, ...d }), i.add(l));
    }
  }), s), []).sort((s, c) => s.id.localeCompare(c.id));
}
const Rc = 1e3, Pp = 10, Pi = {
  nodeOrigin: [0, 0],
  nodeExtent: Rn,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, $p = {
  ...Pi,
  checkEquality: !0
};
function $i(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function Tp(e, t, n) {
  const o = $i(Pi, n);
  for (const r of e.values())
    if (r.parentId)
      zi(r, e, t, o);
    else {
      const i = qn(r, o.nodeOrigin), s = Pt(r.extent) ? r.extent : o.nodeExtent, c = Dt(i, s, lt(r));
      r.internals.positionAbsolute = c;
    }
}
function zp(e, t) {
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
function Ti(e) {
  return e === "manual";
}
function ui(e, t, n, o = {}) {
  const r = $i($p, o), i = { i: 0 }, s = new Map(t), c = r?.elevateNodesOnSelect && !Ti(r.zIndexMode) ? Rc : 0;
  let d = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const u of e) {
    let f = s.get(u.id);
    if (r.checkEquality && u === f?.internals.userNode)
      t.set(u.id, f);
    else {
      const h = qn(u, r.nodeOrigin), p = Pt(u.extent) ? u.extent : r.nodeExtent, y = Dt(h, p, lt(u));
      f = {
        ...r.defaults,
        ...u,
        measured: {
          width: u.measured?.width,
          height: u.measured?.height
        },
        internals: {
          positionAbsolute: y,
          // if user re-initializes the node or removes `measured` for whatever reason, we reset the handleBounds so that the node gets re-measured
          handleBounds: zp(u, f),
          z: Lc(u, c, r.zIndexMode),
          userNode: u
        }
      }, t.set(u.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (d = !1), u.parentId && zi(f, t, n, o, i), l ||= u.selected ?? !1;
  }
  return { nodesInitialized: d, hasSelectedNodes: l };
}
function Rp(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function zi(e, t, n, o, r) {
  const { elevateNodesOnSelect: i, nodeOrigin: s, nodeExtent: c, zIndexMode: d } = $i(Pi, o), l = e.parentId, u = t.get(l);
  if (!u) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Rp(e, n), r && !u.parentId && u.internals.rootParentIndex === void 0 && d === "auto" && (u.internals.rootParentIndex = ++r.i, u.internals.z = u.internals.z + r.i * Pp), r && u.internals.rootParentIndex !== void 0 && (r.i = u.internals.rootParentIndex);
  const f = i && !Ti(d) ? Rc : 0, { x: h, y: p, z: y } = Lp(e, u, s, c, f, d), { positionAbsolute: v } = e.internals, w = h !== v.x || p !== v.y;
  (w || y !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: w ? { x: h, y: p } : v,
      z: y
    }
  });
}
function Lc(e, t, n) {
  const o = Ue(e.zIndex) ? e.zIndex : 0;
  return Ti(n) ? o : o + (e.selected ? t : 0);
}
function Lp(e, t, n, o, r, i) {
  const { x: s, y: c } = t.internals.positionAbsolute, d = lt(e), l = qn(e, n), u = Pt(e.extent) ? Dt(l, e.extent, d) : l;
  let f = Dt({ x: s + u.x, y: c + u.y }, o, d);
  e.extent === "parent" && (f = Ec(f, d, t));
  const h = Lc(e, r, i), p = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: p >= h ? p + 1 : h
  };
}
function Ri(e, t, n, o = [0, 0]) {
  const r = [], i = /* @__PURE__ */ new Map();
  for (const s of e) {
    const c = t.get(s.parentId);
    if (!c)
      continue;
    const d = i.get(s.parentId)?.expandedRect ?? en(c), l = kc(d, s.rect);
    i.set(s.parentId, { expandedRect: l, parent: c });
  }
  return i.size > 0 && i.forEach(({ expandedRect: s, parent: c }, d) => {
    const l = c.internals.positionAbsolute, u = lt(c), f = c.origin ?? o, h = s.x < l.x ? Math.round(Math.abs(l.x - s.x)) : 0, p = s.y < l.y ? Math.round(Math.abs(l.y - s.y)) : 0, y = Math.max(u.width, Math.round(s.width)), v = Math.max(u.height, Math.round(s.height)), w = (y - u.width) * f[0], m = (v - u.height) * f[1];
    (h > 0 || p > 0 || w || m) && (r.push({
      id: d,
      type: "position",
      position: {
        x: c.position.x - h + w,
        y: c.position.y - p + m
      }
    }), n.get(d)?.forEach((b) => {
      e.some((g) => g.id === b.id) || r.push({
        id: b.id,
        type: "position",
        position: {
          x: b.position.x + h,
          y: b.position.y + p
        }
      });
    })), (u.width < s.width || u.height < s.height || h || p) && r.push({
      id: d,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: y + (h ? f[0] * h - w : 0),
        height: v + (p ? f[1] * p - m : 0)
      }
    });
  }), r;
}
function Vp(e, t, n, o, r, i, s) {
  const c = o?.querySelector(".xyflow__viewport");
  let d = !1;
  if (!c)
    return { changes: [], updatedInternals: d };
  const l = [], u = window.getComputedStyle(c), { m22: f } = new window.DOMMatrixReadOnly(u.transform), h = [];
  for (const p of e.values()) {
    const y = t.get(p.id);
    if (!y)
      continue;
    if (y.hidden) {
      t.set(y.id, {
        ...y,
        internals: {
          ...y.internals,
          handleBounds: void 0
        }
      }), d = !0;
      continue;
    }
    const v = Di(p.nodeElement), w = y.measured.width !== v.width || y.measured.height !== v.height;
    if (!!(v.width && v.height && (w || !y.internals.handleBounds || p.force))) {
      const b = p.nodeElement.getBoundingClientRect(), g = Pt(y.extent) ? y.extent : i;
      let { positionAbsolute: x } = y.internals;
      y.parentId && y.extent === "parent" ? x = Ec(x, v, t.get(y.parentId)) : g && (x = Dt(x, g, v));
      const I = {
        ...y,
        measured: v,
        internals: {
          ...y.internals,
          positionAbsolute: x,
          handleBounds: {
            source: Cs("source", p.nodeElement, b, f, y.id),
            target: Cs("target", p.nodeElement, b, f, y.id)
          }
        }
      };
      t.set(y.id, I), y.parentId && zi(I, t, n, { nodeOrigin: r, zIndexMode: s }), d = !0, w && (l.push({
        id: y.id,
        type: "dimensions",
        dimensions: v
      }), y.expandParent && y.parentId && h.push({
        id: y.id,
        parentId: y.parentId,
        rect: en(I, r)
      }));
    }
  }
  if (h.length > 0) {
    const p = Ri(h, t, n, r);
    l.push(...p);
  }
  return { changes: l, updatedInternals: d };
}
async function Hp({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: i }) {
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
function $s(e, t, n, o, r, i) {
  let s = r;
  const c = o.get(s) || /* @__PURE__ */ new Map();
  o.set(s, c.set(n, t)), s = `${r}-${e}`;
  const d = o.get(s) || /* @__PURE__ */ new Map();
  if (o.set(s, d.set(n, t)), i) {
    s = `${r}-${e}-${i}`;
    const l = o.get(s) || /* @__PURE__ */ new Map();
    o.set(s, l.set(n, t));
  }
}
function Vc(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: i, sourceHandle: s = null, targetHandle: c = null } = o, d = { edgeId: o.id, source: r, target: i, sourceHandle: s, targetHandle: c }, l = `${r}-${s}--${i}-${c}`, u = `${i}-${c}--${r}-${s}`;
    $s("source", d, u, e, r, s), $s("target", d, l, e, i, c), t.set(o.id, o);
  }
}
function Hc(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : Hc(n, t) : !1;
}
function Ts(e, t, n) {
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
function Op(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [i, s] of e)
    if ((s.selected || s.id === o) && (!s.parentId || !Hc(s, e)) && (s.draggable || t && typeof s.draggable > "u")) {
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
function Fr({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
  const r = [];
  for (const [s, c] of t) {
    const d = n.get(s)?.internals.userNode;
    d && r.push({
      ...d,
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
function Fp({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const i = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, s = Zn(i, t);
  return {
    x: s.x - i.x,
    y: s.y - i.y
  };
}
function Bp({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let i = { x: null, y: null }, s = 0, c = /* @__PURE__ */ new Map(), d = !1, l = { x: 0, y: 0 }, u = null, f = !1, h = null, p = !1, y = !1, v = null;
  function w({ noDragClassName: b, handleSelector: g, domNode: x, isSelectable: I, nodeId: E, nodeClickDistance: j = 0 }) {
    h = He(x);
    function P({ x: R, y: O }) {
      const { nodeLookup: S, nodeExtent: _, snapGrid: C, snapToGrid: M, nodeOrigin: z, onNodeDrag: $, onSelectionDrag: W, onError: F, updateNodePositions: B } = t();
      i = { x: R, y: O };
      let Z = !1;
      const K = c.size > 1, ne = K && _ ? li(Un(c)) : null, se = K && M ? Fp({
        dragItems: c,
        snapGrid: C,
        x: R,
        y: O
      }) : null;
      for (const [Q, L] of c) {
        if (!S.has(Q))
          continue;
        let q = { x: R - L.distance.x, y: O - L.distance.y };
        M && (q = se ? {
          x: Math.round(q.x + se.x),
          y: Math.round(q.y + se.y)
        } : Zn(q, C));
        let ae = null;
        if (K && _ && !L.extent && ne) {
          const { positionAbsolute: ee } = L.internals, le = ee.x - ne.x + _[0][0], H = ee.x + L.measured.width - ne.x2 + _[1][0], U = ee.y - ne.y + _[0][1], pe = ee.y + L.measured.height - ne.y2 + _[1][1];
          ae = [
            [le, U],
            [H, pe]
          ];
        }
        const { position: ie, positionAbsolute: G } = Sc({
          nodeId: Q,
          nextPosition: q,
          nodeLookup: S,
          nodeExtent: ae || _,
          nodeOrigin: z,
          onError: F
        });
        Z = Z || L.position.x !== ie.x || L.position.y !== ie.y, L.position = ie, L.internals.positionAbsolute = G;
      }
      if (y = y || Z, !!Z && (B(c, !0), v && (o || $ || !E && W))) {
        const [Q, L] = Fr({
          nodeId: E,
          dragItems: c,
          nodeLookup: S
        });
        o?.(v, c, Q, L), $?.(v, Q, L), E || W?.(v, L);
      }
    }
    async function T() {
      if (!u)
        return;
      const { transform: R, panBy: O, autoPanSpeed: S, autoPanOnNodeDrag: _ } = t();
      if (!_) {
        d = !1, cancelAnimationFrame(s);
        return;
      }
      const [C, M] = Ai(l, u, S);
      (C !== 0 || M !== 0) && (i.x = (i.x ?? 0) - C / R[2], i.y = (i.y ?? 0) - M / R[2], await O({ x: C, y: M }) && P(i)), s = requestAnimationFrame(T);
    }
    function A(R) {
      const { nodeLookup: O, multiSelectionActive: S, nodesDraggable: _, transform: C, snapGrid: M, snapToGrid: z, selectNodesOnDrag: $, onNodeDragStart: W, onSelectionDragStart: F, unselectNodesAndEdges: B } = t();
      f = !0, (!$ || !I) && !S && E && (O.get(E)?.selected || B()), I && $ && E && e?.(E);
      const Z = Mn(R.sourceEvent, { transform: C, snapGrid: M, snapToGrid: z, containerBounds: u });
      if (i = Z, c = Op(O, _, Z, E), c.size > 0 && (n || W || !E && F)) {
        const [K, ne] = Fr({
          nodeId: E,
          dragItems: c,
          nodeLookup: O
        });
        n?.(R.sourceEvent, c, K, ne), W?.(R.sourceEvent, K, ne), E || F?.(R.sourceEvent, ne);
      }
    }
    const k = nc().clickDistance(j).on("start", (R) => {
      const { domNode: O, nodeDragThreshold: S, transform: _, snapGrid: C, snapToGrid: M } = t();
      u = O?.getBoundingClientRect() || null, p = !1, y = !1, v = R.sourceEvent, S === 0 && A(R), i = Mn(R.sourceEvent, { transform: _, snapGrid: C, snapToGrid: M, containerBounds: u }), l = Ze(R.sourceEvent, u);
    }).on("drag", (R) => {
      const { autoPanOnNodeDrag: O, transform: S, snapGrid: _, snapToGrid: C, nodeDragThreshold: M, nodeLookup: z } = t(), $ = Mn(R.sourceEvent, { transform: S, snapGrid: _, snapToGrid: C, containerBounds: u });
      if (v = R.sourceEvent, (R.sourceEvent.type === "touchmove" && R.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      E && !z.has(E)) && (p = !0), !p) {
        if (!d && O && f && (d = !0, T()), !f) {
          const W = Ze(R.sourceEvent, u), F = W.x - l.x, B = W.y - l.y;
          Math.sqrt(F * F + B * B) > M && A(R);
        }
        (i.x !== $.xSnapped || i.y !== $.ySnapped) && c && f && (l = Ze(R.sourceEvent, u), P($));
      }
    }).on("end", (R) => {
      if (!f || p) {
        p && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (d = !1, f = !1, cancelAnimationFrame(s), c.size > 0) {
        const { nodeLookup: O, updateNodePositions: S, onNodeDragStop: _, onSelectionDragStop: C } = t();
        if (y && (S(c, !1), y = !1), r || _ || !E && C) {
          const [M, z] = Fr({
            nodeId: E,
            dragItems: c,
            nodeLookup: O,
            dragging: !1
          });
          r?.(R.sourceEvent, c, M, z), _?.(R.sourceEvent, M, z), E || C?.(R.sourceEvent, z);
        }
      }
    }).filter((R) => {
      const O = R.target;
      return !R.button && (!b || !Ts(O, `.${b}`, x)) && (!g || Ts(O, g, x));
    });
    h.call(k);
  }
  function m() {
    h?.on(".drag", null);
  }
  return {
    update: w,
    destroy: m
  };
}
function Wp(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const i of t.values())
    Vn(r, en(i)) > 0 && o.push(i);
  return o;
}
const Xp = 250;
function Yp(e, t, n, o) {
  let r = [], i = 1 / 0;
  const s = Wp(e, n, t + Xp);
  for (const c of s) {
    const d = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of d) {
      if (o.nodeId === l.nodeId && o.type === l.type && o.id === l.id)
        continue;
      const { x: u, y: f } = $t(c, l, l.position, !0), h = Math.sqrt(Math.pow(u - e.x, 2) + Math.pow(f - e.y, 2));
      h > t || (h < i ? (r = [{ ...l, x: u, y: f }], i = h) : h === i && r.push({ ...l, x: u, y: f }));
    }
  }
  if (!r.length)
    return null;
  if (r.length > 1) {
    const c = o.type === "source" ? "target" : "source";
    return r.find((d) => d.type === c) ?? r[0];
  }
  return r[0];
}
function Oc(e, t, n, o, r, i = !1) {
  const s = o.get(e);
  if (!s)
    return null;
  const c = r === "strict" ? s.internals.handleBounds?.[t] : [...s.internals.handleBounds?.source ?? [], ...s.internals.handleBounds?.target ?? []], d = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return d && i ? { ...d, ...$t(s, d, d.position, !0) } : d;
}
function Fc(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function qp(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const Bc = () => !0;
function Up(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: i, isTarget: s, domNode: c, nodeLookup: d, lib: l, autoPanOnConnect: u, flowId: f, panBy: h, cancelConnection: p, onConnectStart: y, onConnect: v, onConnectEnd: w, isValidConnection: m = Bc, onReconnectEnd: b, updateConnection: g, getTransform: x, getFromHandle: I, autoPanSpeed: E, dragThreshold: j = 1, handleDomNode: P }) {
  const T = _c(e.target);
  let A = 0, k;
  const { x: R, y: O } = Ze(e), S = Fc(i, P), _ = c?.getBoundingClientRect();
  let C = !1;
  if (!_ || !S)
    return;
  const M = Oc(r, S, o, d, t);
  if (!M)
    return;
  let z = Ze(e, _), $ = !1, W = null, F = !1, B = null;
  function Z() {
    if (!u || !_)
      return;
    const [ie, G] = Ai(z, _, E);
    h({ x: ie, y: G }), A = requestAnimationFrame(Z);
  }
  const K = {
    ...M,
    nodeId: r,
    type: S,
    position: M.position
  }, ne = d.get(r);
  let Q = {
    inProgress: !0,
    isValid: null,
    from: $t(ne, K, te.Left, !0),
    fromHandle: K,
    fromPosition: K.position,
    fromNode: ne,
    to: z,
    toHandle: null,
    toPosition: Ss[K.position],
    toNode: null,
    pointer: z
  };
  function L() {
    C = !0, g(Q), y?.(e, { nodeId: r, handleId: o, handleType: S });
  }
  j === 0 && L();
  function q(ie) {
    if (!C) {
      const { x: pe, y: ge } = Ze(ie), Ae = pe - R, ye = ge - O;
      if (!(Ae * Ae + ye * ye > j * j))
        return;
      L();
    }
    if (!I() || !K) {
      ae(ie);
      return;
    }
    const G = x();
    z = Ze(ie, _), k = Yp(rn(z, G, !1, [1, 1]), n, d, K), $ || (Z(), $ = !0);
    const ee = Wc(ie, {
      handle: k,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: s ? "target" : "source",
      isValidConnection: m,
      doc: T,
      lib: l,
      flowId: f,
      nodeLookup: d
    });
    B = ee.handleDomNode, W = ee.connection, F = qp(!!k, ee.isValid);
    const le = d.get(r), H = le ? $t(le, K, te.Left, !0) : Q.from, U = {
      ...Q,
      from: H,
      isValid: F,
      to: ee.toHandle && F ? tn({ x: ee.toHandle.x, y: ee.toHandle.y }, G) : z,
      toHandle: ee.toHandle,
      toPosition: F && ee.toHandle ? ee.toHandle.position : Ss[K.position],
      toNode: ee.toHandle ? d.get(ee.toHandle.nodeId) : null,
      pointer: z
    };
    g(U), Q = U;
  }
  function ae(ie) {
    if (!("touches" in ie && ie.touches.length > 0)) {
      if (C) {
        (k || B) && W && F && v?.(W);
        const { inProgress: G, ...ee } = Q, le = {
          ...ee,
          toPosition: Q.toHandle ? Q.toPosition : null
        };
        w?.(ie, le), i && b?.(ie, le);
      }
      p(), cancelAnimationFrame(A), $ = !1, F = !1, W = null, B = null, T.removeEventListener("mousemove", q), T.removeEventListener("mouseup", ae), T.removeEventListener("touchmove", q), T.removeEventListener("touchend", ae);
    }
  }
  T.addEventListener("mousemove", q), T.addEventListener("mouseup", ae), T.addEventListener("touchmove", q), T.addEventListener("touchend", ae);
}
function Wc(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: i, doc: s, lib: c, flowId: d, isValidConnection: l = Bc, nodeLookup: u }) {
  const f = i === "target", h = t ? s.querySelector(`.${c}-flow__handle[data-id="${d}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: p, y } = Ze(e), v = s.elementFromPoint(p, y), w = v?.classList.contains(`${c}-flow__handle`) ? v : h, m = {
    handleDomNode: w,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (w) {
    const b = Fc(void 0, w), g = w.getAttribute("data-nodeid"), x = w.getAttribute("data-handleid"), I = w.classList.contains("connectable"), E = w.classList.contains("connectableend");
    if (!g || !b)
      return m;
    const j = {
      source: f ? g : o,
      sourceHandle: f ? x : r,
      target: f ? o : g,
      targetHandle: f ? r : x
    };
    m.connection = j;
    const T = I && E && (n === Jt.Strict ? f && b === "source" || !f && b === "target" : g !== o || x !== r);
    m.isValid = T && l(j), m.toHandle = Oc(g, b, x, u, n, !0);
  }
  return m;
}
const fi = {
  onPointerDown: Up,
  isValid: Wc
};
function Zp({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = He(e);
  function i({ translateExtent: c, width: d, height: l, zoomStep: u = 1, pannable: f = !0, zoomable: h = !0, inversePan: p = !1 }) {
    const y = (g) => {
      if (g.sourceEvent.type !== "wheel" || !t)
        return;
      const x = n(), I = g.sourceEvent.ctrlKey && Hn() ? 10 : 1, E = -g.sourceEvent.deltaY * (g.sourceEvent.deltaMode === 1 ? 0.05 : g.sourceEvent.deltaMode ? 1 : 2e-3) * u, j = x[2] * Math.pow(2, E * I);
      t.scaleTo(j);
    };
    let v = [0, 0];
    const w = (g) => {
      (g.sourceEvent.type === "mousedown" || g.sourceEvent.type === "touchstart") && (v = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ]);
    }, m = (g) => {
      const x = n();
      if (g.sourceEvent.type !== "mousemove" && g.sourceEvent.type !== "touchmove" || !t)
        return;
      const I = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ], E = [I[0] - v[0], I[1] - v[1]];
      v = I;
      const j = o() * Math.max(x[2], Math.log(x[2])) * (p ? -1 : 1), P = {
        x: x[0] - E[0] * j,
        y: x[1] - E[1] * j
      }, T = [
        [0, 0],
        [d, l]
      ];
      t.setViewportConstrained({
        x: P.x,
        y: P.y,
        zoom: x[2]
      }, T, c);
    }, b = mc().on("start", w).on("zoom", f ? m : null).on("zoom.wheel", h ? y : null);
    r.call(b, {});
  }
  function s() {
    r.on("zoom", null);
  }
  return {
    update: i,
    destroy: s,
    pointer: Ye
  };
}
const cr = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), Br = ({ x: e, y: t, zoom: n }) => ir.translate(e, t).scale(n), Xt = (e, t) => e.target.closest(`.${t}`), Xc = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Kp = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, Wr = (e, t = 0, n = Kp, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, Yc = (e) => {
  const t = e.ctrlKey && Hn() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Gp({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: i, zoomOnPinch: s, onPanZoomStart: c, onPanZoom: d, onPanZoomEnd: l }) {
  return (u) => {
    if (Xt(u, t))
      return u.ctrlKey && u.preventDefault(), !1;
    u.preventDefault(), u.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (u.ctrlKey && s) {
      const w = Ye(u), m = Yc(u), b = f * Math.pow(2, m);
      o.scaleTo(n, b, w, u);
      return;
    }
    const h = u.deltaMode === 1 ? 20 : 1;
    let p = r === jt.Vertical ? 0 : u.deltaX * h, y = r === jt.Horizontal ? 0 : u.deltaY * h;
    !Hn() && u.shiftKey && r !== jt.Vertical && (p = u.deltaY * h, y = 0), o.translateBy(
      n,
      -(p / f) * i,
      -(y / f) * i,
      // @ts-ignore
      { internal: !0 }
    );
    const v = cr(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (d?.(u, v), e.panScrollTimeout = setTimeout(() => {
      l?.(u, v), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(u, v));
  };
}
function Jp({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const i = o.type === "wheel", s = !t && i && !o.ctrlKey, c = Xt(o, e);
    if (o.ctrlKey && i && c && o.preventDefault(), s || c)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function Qp({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = cr(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function eg({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (i) => {
    e.usedRightMouseButton = !!(n && Xc(t, e.mouseButton ?? 0)), i.sourceEvent?.sync || o([i.transform.x, i.transform.y, i.transform.k]), r && !i.sourceEvent?.internal && r?.(i.sourceEvent, cr(i.transform));
  };
}
function tg({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: i }) {
  return (s) => {
    if (!s.sourceEvent?.internal && (e.isZoomingOrPanning = !1, i && Xc(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && s.sourceEvent && i(s.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
      const c = cr(s.transform);
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
function ng({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: i, userSelectionActive: s, noWheelClassName: c, noPanClassName: d, lib: l, connectionInProgress: u }) {
  return (f) => {
    const h = e || t, p = n && f.ctrlKey, y = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (Xt(f, `${l}-flow__node`) || Xt(f, `${l}-flow__edge`)))
      return !0;
    if (!o && !h && !r && !i && !n || s || u && !y || Xt(f, c) && y || Xt(f, d) && (!y || r && y && !e) || !n && f.ctrlKey && y)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!h && !r && !p && y || !o && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(o) && !o.includes(f.button) && f.type === "mousedown")
      return !1;
    const v = Array.isArray(o) && o.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || y) && v;
  };
}
function og({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: i, onPanZoomStart: s, onPanZoomEnd: c, onDraggingChange: d }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, u = e.getBoundingClientRect(), f = mc().scaleExtent([t, n]).translateExtent(o), h = He(e).call(f);
  b({
    x: r.x,
    y: r.y,
    zoom: Qt(r.zoom, t, n)
  }, [
    [0, 0],
    [u.width, u.height]
  ], o);
  const p = h.on("wheel.zoom"), y = h.on("dblclick.zoom");
  f.wheelDelta(Yc);
  async function v(k, R) {
    return h ? new Promise((O) => {
      f?.interpolate(R?.interpolate === "linear" ? An : Mo).transform(Wr(h, R?.duration, R?.ease, () => O(!0)), k);
    }) : !1;
  }
  function w({ noWheelClassName: k, noPanClassName: R, onPaneContextMenu: O, userSelectionActive: S, panOnScroll: _, panOnDrag: C, panOnScrollMode: M, panOnScrollSpeed: z, preventScrolling: $, zoomOnPinch: W, zoomOnScroll: F, zoomOnDoubleClick: B, zoomActivationKeyPressed: Z, lib: K, onTransformChange: ne, connectionInProgress: se, paneClickDistance: Q, selectionOnDrag: L }) {
    S && !l.isZoomingOrPanning && m();
    const q = _ && !Z && !S;
    f.clickDistance(L ? 1 / 0 : !Ue(Q) || Q < 0 ? 0 : Q);
    const ae = q ? Gp({
      zoomPanValues: l,
      noWheelClassName: k,
      d3Selection: h,
      d3Zoom: f,
      panOnScrollMode: M,
      panOnScrollSpeed: z,
      zoomOnPinch: W,
      onPanZoomStart: s,
      onPanZoom: i,
      onPanZoomEnd: c
    }) : Jp({
      noWheelClassName: k,
      preventScrolling: $,
      d3ZoomHandler: p
    });
    h.on("wheel.zoom", ae, { passive: !1 });
    const ie = Qp({
      zoomPanValues: l,
      onDraggingChange: d,
      onPanZoomStart: s
    });
    f.on("start", ie);
    const G = eg({
      zoomPanValues: l,
      panOnDrag: C,
      onPaneContextMenu: !!O,
      onPanZoom: i,
      onTransformChange: ne
    });
    f.on("zoom", G);
    const ee = tg({
      zoomPanValues: l,
      panOnDrag: C,
      panOnScroll: _,
      onPaneContextMenu: O,
      onPanZoomEnd: c,
      onDraggingChange: d
    });
    f.on("end", ee);
    const le = ng({
      zoomActivationKeyPressed: Z,
      panOnDrag: C,
      zoomOnScroll: F,
      panOnScroll: _,
      zoomOnDoubleClick: B,
      zoomOnPinch: W,
      userSelectionActive: S,
      noPanClassName: R,
      noWheelClassName: k,
      lib: K,
      connectionInProgress: se
    });
    f.filter(le), B ? h.on("dblclick.zoom", y) : h.on("dblclick.zoom", null);
  }
  function m() {
    f.on("zoom", null);
  }
  async function b(k, R, O) {
    const S = Br(k), _ = f?.constrain()(S, R, O);
    return _ && await v(_), _;
  }
  async function g(k, R) {
    const O = Br(k);
    return await v(O, R), O;
  }
  function x(k) {
    if (h) {
      const R = Br(k), O = h.property("__zoom");
      (O.k !== k.zoom || O.x !== k.x || O.y !== k.y) && f?.transform(h, R, null, { sync: !0 });
    }
  }
  function I() {
    const k = h ? yc(h.node()) : { x: 0, y: 0, k: 1 };
    return { x: k.x, y: k.y, zoom: k.k };
  }
  async function E(k, R) {
    return h ? new Promise((O) => {
      f?.interpolate(R?.interpolate === "linear" ? An : Mo).scaleTo(Wr(h, R?.duration, R?.ease, () => O(!0)), k);
    }) : !1;
  }
  async function j(k, R) {
    return h ? new Promise((O) => {
      f?.interpolate(R?.interpolate === "linear" ? An : Mo).scaleBy(Wr(h, R?.duration, R?.ease, () => O(!0)), k);
    }) : !1;
  }
  function P(k) {
    f?.scaleExtent(k);
  }
  function T(k) {
    f?.translateExtent(k);
  }
  function A(k) {
    const R = !Ue(k) || k < 0 ? 0 : k;
    f?.clickDistance(R);
  }
  return {
    update: w,
    destroy: m,
    setViewport: g,
    setViewportConstrained: b,
    getViewport: I,
    scaleTo: E,
    scaleBy: j,
    setScaleExtent: P,
    setTranslateExtent: T,
    syncViewport: x,
    setClickDistance: A
  };
}
var nn;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(nn || (nn = {}));
function rg({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: i }) {
  const s = e - t, c = n - o, d = [s > 0 ? 1 : s < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return s && r && (d[0] = d[0] * -1), c && i && (d[1] = d[1] * -1), d;
}
function zs(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: r
  };
}
function ft(e, t) {
  return Math.max(0, t - e);
}
function ht(e, t) {
  return Math.max(0, e - t);
}
function ko(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function Rs(e, t) {
  return e ? !t : t;
}
function ig(e, t, n, o, r, i, s, c) {
  let { affectsX: d, affectsY: l } = t;
  const { isHorizontal: u, isVertical: f } = t, h = u && f, { xSnapped: p, ySnapped: y } = n, { minWidth: v, maxWidth: w, minHeight: m, maxHeight: b } = o, { x: g, y: x, width: I, height: E, aspectRatio: j } = e;
  let P = Math.floor(u ? p - e.pointerX : 0), T = Math.floor(f ? y - e.pointerY : 0);
  const A = I + (d ? -P : P), k = E + (l ? -T : T), R = -i[0] * I, O = -i[1] * E;
  let S = ko(A, v, w), _ = ko(k, m, b);
  if (s) {
    let z = 0, $ = 0;
    d && P < 0 ? z = ft(g + P + R, s[0][0]) : !d && P > 0 && (z = ht(g + A + R, s[1][0])), l && T < 0 ? $ = ft(x + T + O, s[0][1]) : !l && T > 0 && ($ = ht(x + k + O, s[1][1])), S = Math.max(S, z), _ = Math.max(_, $);
  }
  if (c) {
    let z = 0, $ = 0;
    d && P > 0 ? z = ht(g + P, c[0][0]) : !d && P < 0 && (z = ft(g + A, c[1][0])), l && T > 0 ? $ = ht(x + T, c[0][1]) : !l && T < 0 && ($ = ft(x + k, c[1][1])), S = Math.max(S, z), _ = Math.max(_, $);
  }
  if (r) {
    if (u) {
      const z = ko(A / j, m, b) * j;
      if (S = Math.max(S, z), s) {
        let $ = 0;
        !d && !l || d && !l && h ? $ = ht(x + O + A / j, s[1][1]) * j : $ = ft(x + O + (d ? P : -P) / j, s[0][1]) * j, S = Math.max(S, $);
      }
      if (c) {
        let $ = 0;
        !d && !l || d && !l && h ? $ = ft(x + A / j, c[1][1]) * j : $ = ht(x + (d ? P : -P) / j, c[0][1]) * j, S = Math.max(S, $);
      }
    }
    if (f) {
      const z = ko(k * j, v, w) / j;
      if (_ = Math.max(_, z), s) {
        let $ = 0;
        !d && !l || l && !d && h ? $ = ht(g + k * j + R, s[1][0]) / j : $ = ft(g + (l ? T : -T) * j + R, s[0][0]) / j, _ = Math.max(_, $);
      }
      if (c) {
        let $ = 0;
        !d && !l || l && !d && h ? $ = ft(g + k * j, c[1][0]) / j : $ = ht(g + (l ? T : -T) * j, c[0][0]) / j, _ = Math.max(_, $);
      }
    }
  }
  T = T + (T < 0 ? _ : -_), P = P + (P < 0 ? S : -S), r && (h ? A > k * j ? T = (Rs(d, l) ? -P : P) / j : P = (Rs(d, l) ? -T : T) * j : u ? (T = P / j, l = d) : (P = T * j, d = l));
  const C = d ? g + P : g, M = l ? x + T : x;
  return {
    width: I + (d ? -P : P),
    height: E + (l ? -T : T),
    x: i[0] * P * (d ? -1 : 1) + C,
    y: i[1] * T * (l ? -1 : 1) + M
  };
}
const qc = { width: 0, height: 0, x: 0, y: 0 }, sg = {
  ...qc,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function ag(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, i = e.measured.width ?? 0, s = e.measured.height ?? 0, c = n[0] * i, d = n[1] * s;
  return [
    [o - c, r - d],
    [o + i - c, r + s - d]
  ];
}
function cg({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const i = He(e);
  let s = {
    controlDirection: zs("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: l, boundaries: u, keepAspectRatio: f, resizeDirection: h, onResizeStart: p, onResize: y, onResizeEnd: v, shouldResize: w }) {
    let m = { ...qc }, b = { ...sg };
    s = {
      boundaries: u,
      resizeDirection: h,
      keepAspectRatio: f,
      controlDirection: zs(l)
    };
    let g, x = null, I = [], E, j, P, T = !1;
    const A = nc().on("start", (k) => {
      const { nodeLookup: R, transform: O, snapGrid: S, snapToGrid: _, nodeOrigin: C, paneDomNode: M } = n();
      if (g = R.get(t), !g)
        return;
      x = M?.getBoundingClientRect() ?? null;
      const { xSnapped: z, ySnapped: $ } = Mn(k.sourceEvent, {
        transform: O,
        snapGrid: S,
        snapToGrid: _,
        containerBounds: x
      });
      m = {
        width: g.measured.width ?? 0,
        height: g.measured.height ?? 0,
        x: g.position.x ?? 0,
        y: g.position.y ?? 0
      }, b = {
        ...m,
        pointerX: z,
        pointerY: $,
        aspectRatio: m.width / m.height
      }, E = void 0, j = Pt(g.extent) ? g.extent : void 0, g.parentId && (g.extent === "parent" || g.expandParent) && (E = R.get(g.parentId)), E && g.extent === "parent" && (j = [
        [0, 0],
        [E.measured.width, E.measured.height]
      ]), I = [], P = void 0;
      for (const [W, F] of R)
        if (F.parentId === t && (I.push({
          id: W,
          position: { ...F.position },
          extent: F.extent
        }), F.extent === "parent" || F.expandParent)) {
          const B = ag(F, g, F.origin ?? C);
          P ? P = [
            [Math.min(B[0][0], P[0][0]), Math.min(B[0][1], P[0][1])],
            [Math.max(B[1][0], P[1][0]), Math.max(B[1][1], P[1][1])]
          ] : P = B;
        }
      p?.(k, { ...m });
    }).on("drag", (k) => {
      const { transform: R, snapGrid: O, snapToGrid: S, nodeOrigin: _ } = n(), C = Mn(k.sourceEvent, {
        transform: R,
        snapGrid: O,
        snapToGrid: S,
        containerBounds: x
      }), M = [];
      if (!g)
        return;
      const { x: z, y: $, width: W, height: F } = m, B = {}, Z = g.origin ?? _, { width: K, height: ne, x: se, y: Q } = ig(b, s.controlDirection, C, s.boundaries, s.keepAspectRatio, Z, j, P), L = K !== W, q = ne !== F, ae = se !== z && L, ie = Q !== $ && q;
      if (!ae && !ie && !L && !q)
        return;
      if ((ae || ie || Z[0] === 1 || Z[1] === 1) && (B.x = ae ? se : m.x, B.y = ie ? Q : m.y, m.x = B.x, m.y = B.y, I.length > 0)) {
        const H = se - z, U = Q - $;
        for (const pe of I)
          pe.position = {
            x: pe.position.x - H + Z[0] * (K - W),
            y: pe.position.y - U + Z[1] * (ne - F)
          }, M.push(pe);
      }
      if ((L || q) && (B.width = L && (!s.resizeDirection || s.resizeDirection === "horizontal") ? K : m.width, B.height = q && (!s.resizeDirection || s.resizeDirection === "vertical") ? ne : m.height, m.width = B.width, m.height = B.height), E && g.expandParent) {
        const H = Z[0] * (B.width ?? 0);
        B.x && B.x < H && (m.x = H, b.x = b.x - (B.x - H));
        const U = Z[1] * (B.height ?? 0);
        B.y && B.y < U && (m.y = U, b.y = b.y - (B.y - U));
      }
      const G = rg({
        width: m.width,
        prevWidth: W,
        height: m.height,
        prevHeight: F,
        affectsX: s.controlDirection.affectsX,
        affectsY: s.controlDirection.affectsY
      }), ee = { ...m, direction: G };
      w?.(k, ee) !== !1 && (T = !0, y?.(k, ee), o(B, M));
    }).on("end", (k) => {
      T && (v?.(k, { ...m }), r?.({ ...m }), T = !1);
    });
    i.call(A);
  }
  function d() {
    i.on(".drag", null);
  }
  return {
    update: c,
    destroy: d
  };
}
var Xr = { exports: {} }, Yr = {}, qr = { exports: {} }, Ur = {};
var Ls;
function lg() {
  if (Ls) return Ur;
  Ls = 1;
  var e = xt;
  function t(f, h) {
    return f === h && (f !== 0 || 1 / f === 1 / h) || f !== f && h !== h;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, i = e.useLayoutEffect, s = e.useDebugValue;
  function c(f, h) {
    var p = h(), y = o({ inst: { value: p, getSnapshot: h } }), v = y[0].inst, w = y[1];
    return i(
      function() {
        v.value = p, v.getSnapshot = h, d(v) && w({ inst: v });
      },
      [f, p, h]
    ), r(
      function() {
        return d(v) && w({ inst: v }), f(function() {
          d(v) && w({ inst: v });
        });
      },
      [f]
    ), s(p), p;
  }
  function d(f) {
    var h = f.getSnapshot;
    f = f.value;
    try {
      var p = h();
      return !n(f, p);
    } catch {
      return !0;
    }
  }
  function l(f, h) {
    return h();
  }
  var u = typeof window > "u" || typeof window.document > "u" || typeof window.document.createElement > "u" ? l : c;
  return Ur.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : u, Ur;
}
var Vs;
function dg() {
  return Vs || (Vs = 1, qr.exports = lg()), qr.exports;
}
var Hs;
function ug() {
  if (Hs) return Yr;
  Hs = 1;
  var e = xt, t = dg();
  function n(l, u) {
    return l === u && (l !== 0 || 1 / l === 1 / u) || l !== l && u !== u;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, i = e.useRef, s = e.useEffect, c = e.useMemo, d = e.useDebugValue;
  return Yr.useSyncExternalStoreWithSelector = function(l, u, f, h, p) {
    var y = i(null);
    if (y.current === null) {
      var v = { hasValue: !1, value: null };
      y.current = v;
    } else v = y.current;
    y = c(
      function() {
        function m(E) {
          if (!b) {
            if (b = !0, g = E, E = h(E), p !== void 0 && v.hasValue) {
              var j = v.value;
              if (p(j, E))
                return x = j;
            }
            return x = E;
          }
          if (j = x, o(g, E)) return j;
          var P = h(E);
          return p !== void 0 && p(j, P) ? (g = E, j) : (g = E, x = P);
        }
        var b = !1, g, x, I = f === void 0 ? null : f;
        return [
          function() {
            return m(u());
          },
          I === null ? void 0 : function() {
            return m(I());
          }
        ];
      },
      [u, f, h, p]
    );
    var w = r(l, y[0], y[1]);
    return s(
      function() {
        v.hasValue = !0, v.value = w;
      },
      [w]
    ), d(w), w;
  }, Yr;
}
var Os;
function fg() {
  return Os || (Os = 1, Xr.exports = ug()), Xr.exports;
}
var hg = fg();
const pg = /* @__PURE__ */ _d(hg), gg = {}, Fs = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (u, f) => {
    const h = typeof u == "function" ? u(t) : u;
    if (!Object.is(h, t)) {
      const p = t;
      t = f ?? (typeof h != "object" || h === null) ? h : Object.assign({}, t, h), n.forEach((y) => y(t, p));
    }
  }, r = () => t, d = { setState: o, getState: r, getInitialState: () => l, subscribe: (u) => (n.add(u), () => n.delete(u)), destroy: () => {
    (gg ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(o, r, d);
  return d;
}, yg = (e) => e ? Fs(e) : Fs, { useDebugValue: mg } = xt, { useSyncExternalStoreWithSelector: xg } = pg, wg = (e) => e;
function Uc(e, t = wg, n) {
  const o = xg(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return mg(o), o;
}
const Bs = (e, t) => {
  const n = yg(e), o = (r, i = t) => Uc(n, r, i);
  return Object.assign(o, n), o;
}, vg = (e, t) => e ? Bs(e, t) : Bs;
function xe(e, t) {
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
var Zr = { exports: {} }, Pe = {};
var Ws;
function bg() {
  if (Ws) return Pe;
  Ws = 1;
  var e = xt;
  function t(d) {
    var l = "https://react.dev/errors/" + d;
    if (1 < arguments.length) {
      l += "?args[]=" + encodeURIComponent(arguments[1]);
      for (var u = 2; u < arguments.length; u++)
        l += "&args[]=" + encodeURIComponent(arguments[u]);
    }
    return "Minified React error #" + d + "; visit " + l + " for the full message or use the non-minified dev environment for full errors and additional helpful warnings.";
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
  function i(d, l, u) {
    var f = 3 < arguments.length && arguments[3] !== void 0 ? arguments[3] : null;
    return {
      $$typeof: r,
      key: f == null ? null : "" + f,
      children: d,
      containerInfo: l,
      implementation: u
    };
  }
  var s = e.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE;
  function c(d, l) {
    if (d === "font") return "";
    if (typeof l == "string")
      return l === "use-credentials" ? l : "";
  }
  return Pe.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, Pe.createPortal = function(d, l) {
    var u = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!l || l.nodeType !== 1 && l.nodeType !== 9 && l.nodeType !== 11)
      throw Error(t(299));
    return i(d, l, null, u);
  }, Pe.flushSync = function(d) {
    var l = s.T, u = o.p;
    try {
      if (s.T = null, o.p = 2, d) return d();
    } finally {
      s.T = l, o.p = u, o.d.f();
    }
  }, Pe.preconnect = function(d, l) {
    typeof d == "string" && (l ? (l = l.crossOrigin, l = typeof l == "string" ? l === "use-credentials" ? l : "" : void 0) : l = null, o.d.C(d, l));
  }, Pe.prefetchDNS = function(d) {
    typeof d == "string" && o.d.D(d);
  }, Pe.preinit = function(d, l) {
    if (typeof d == "string" && l && typeof l.as == "string") {
      var u = l.as, f = c(u, l.crossOrigin), h = typeof l.integrity == "string" ? l.integrity : void 0, p = typeof l.fetchPriority == "string" ? l.fetchPriority : void 0;
      u === "style" ? o.d.S(
        d,
        typeof l.precedence == "string" ? l.precedence : void 0,
        {
          crossOrigin: f,
          integrity: h,
          fetchPriority: p
        }
      ) : u === "script" && o.d.X(d, {
        crossOrigin: f,
        integrity: h,
        fetchPriority: p,
        nonce: typeof l.nonce == "string" ? l.nonce : void 0
      });
    }
  }, Pe.preinitModule = function(d, l) {
    if (typeof d == "string")
      if (typeof l == "object" && l !== null) {
        if (l.as == null || l.as === "script") {
          var u = c(
            l.as,
            l.crossOrigin
          );
          o.d.M(d, {
            crossOrigin: u,
            integrity: typeof l.integrity == "string" ? l.integrity : void 0,
            nonce: typeof l.nonce == "string" ? l.nonce : void 0
          });
        }
      } else l == null && o.d.M(d);
  }, Pe.preload = function(d, l) {
    if (typeof d == "string" && typeof l == "object" && l !== null && typeof l.as == "string") {
      var u = l.as, f = c(u, l.crossOrigin);
      o.d.L(d, u, {
        crossOrigin: f,
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
  }, Pe.preloadModule = function(d, l) {
    if (typeof d == "string")
      if (l) {
        var u = c(l.as, l.crossOrigin);
        o.d.m(d, {
          as: typeof l.as == "string" && l.as !== "script" ? l.as : void 0,
          crossOrigin: u,
          integrity: typeof l.integrity == "string" ? l.integrity : void 0
        });
      } else o.d.m(d);
  }, Pe.requestFormReset = function(d) {
    o.d.r(d);
  }, Pe.unstable_batchedUpdates = function(d, l) {
    return d(l);
  }, Pe.useFormState = function(d, l, u) {
    return s.H.useFormState(d, l, u);
  }, Pe.useFormStatus = function() {
    return s.H.useHostTransitionStatus();
  }, Pe.version = "19.2.7", Pe;
}
var Xs;
function Ng() {
  if (Xs) return Zr.exports;
  Xs = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), Zr.exports = bg(), Zr.exports;
}
var Sg = Ng();
const lr = vi(null), Eg = lr.Provider, Zc = Fe.error001("react");
function ue(e, t) {
  const n = Wn(lr);
  if (n === null)
    throw new Error(Zc);
  return Uc(n, e, t);
}
function we() {
  const e = Wn(lr);
  if (e === null)
    throw new Error(Zc);
  return me(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const Ys = { display: "none" }, kg = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, Kc = "react-flow__node-desc", Gc = "react-flow__edge-desc", Ig = "react-flow__aria-live", Cg = (e) => e.ariaLiveMessage, jg = (e) => e.ariaLabelConfig;
function _g({ rfId: e }) {
  const t = ue(Cg);
  return a.jsx("div", { id: `${Ig}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: kg, children: t });
}
function Ag({ rfId: e, disableKeyboardA11y: t }) {
  const n = ue(jg);
  return a.jsxs(a.Fragment, { children: [a.jsx("div", { id: `${Kc}-${e}`, style: Ys, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), a.jsx("div", { id: `${Gc}-${e}`, style: Ys, children: n["edge.a11yDescription.default"] }), !t && a.jsx(_g, { rfId: e })] });
}
const dr = er(({ position: e = "top-left", children: t, className: n, style: o, ...r }, i) => {
  const s = `${e}`.split("-");
  return a.jsx("div", { className: Ie(["react-flow__panel", n, ...s]), style: o, ref: i, ...r, children: t });
});
dr.displayName = "Panel";
function Mg({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : a.jsx(dr, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: a.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const Dg = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, Io = (e) => e.id;
function Pg(e, t) {
  return xe(e.selectedNodes.map(Io), t.selectedNodes.map(Io)) && xe(e.selectedEdges.map(Io), t.selectedEdges.map(Io));
}
function $g({ onSelectionChange: e }) {
  const t = we(), { selectedNodes: n, selectedEdges: o } = ue(Dg, Pg);
  return oe(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((i) => i(r));
  }, [n, o, e]), null;
}
const Tg = (e) => !!e.onSelectionChangeHandlers;
function zg({ onSelectionChange: e }) {
  const t = ue(Tg);
  return e || t ? a.jsx($g, { onSelectionChange: e }) : null;
}
const Jc = [0, 0], Rg = { x: 0, y: 0, zoom: 1 }, Lg = [
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
], qs = [...Lg, "rfId"], Vg = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), Us = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: Rn,
  nodeOrigin: Jc,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function Hg(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: i, setNodeExtent: s, reset: c, setDefaultNodesAndEdges: d } = ue(Vg, xe), l = we();
  oe(() => (d(e.defaultNodes, e.defaultEdges), () => {
    u.current = Us, c();
  }), []);
  const u = ce(Us);
  return oe(
    () => {
      for (const f of qs) {
        const h = e[f], p = u.current[f];
        h !== p && (typeof e[f] > "u" || (f === "nodes" ? t(h) : f === "edges" ? n(h) : f === "minZoom" ? o(h) : f === "maxZoom" ? r(h) : f === "translateExtent" ? i(h) : f === "nodeExtent" ? s(h) : f === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: bp(h) }) : f === "fitView" ? l.setState({ fitViewQueued: h }) : f === "fitViewOptions" ? l.setState({ fitViewOptions: h }) : l.setState({ [f]: h })));
      }
      u.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    qs.map((f) => e[f])
  ), null;
}
function Zs() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function Og(e) {
  const [t, n] = Y(e === "system" ? null : e);
  return oe(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = Zs(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : Zs()?.matches ? "dark" : "light";
}
const Ks = typeof document < "u" ? document : null;
function On(e = null, t = { target: Ks, actInsideInputWithModifier: !0 }) {
  const [n, o] = Y(!1), r = ce(!1), i = ce(/* @__PURE__ */ new Set([])), [s, c] = me(() => {
    if (e !== null) {
      const l = (Array.isArray(e) ? e : [e]).filter((f) => typeof f == "string").map((f) => f.replace("+", `
`).replace(`

`, `
+`).split(`
`)), u = l.reduce((f, h) => f.concat(...h), []);
      return [l, u];
    }
    return [[], []];
  }, [e]);
  return oe(() => {
    const d = t?.target ?? Ks, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const u = (p) => {
        if (r.current = p.ctrlKey || p.metaKey || p.shiftKey || p.altKey, (!r.current || r.current && !l) && Ac(p))
          return !1;
        const v = Js(p.code, c);
        if (i.current.add(p[v]), Gs(s, i.current, !1)) {
          const w = p.composedPath?.()?.[0] || p.target, m = w?.nodeName === "BUTTON" || w?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !m) && p.preventDefault(), o(!0);
        }
      }, f = (p) => {
        const y = Js(p.code, c);
        Gs(s, i.current, !0) ? (o(!1), i.current.clear()) : i.current.delete(p[y]), p.key === "Meta" && i.current.clear(), r.current = !1;
      }, h = () => {
        i.current.clear(), o(!1);
      };
      return d?.addEventListener("keydown", u), d?.addEventListener("keyup", f), window.addEventListener("blur", h), window.addEventListener("contextmenu", h), () => {
        d?.removeEventListener("keydown", u), d?.removeEventListener("keyup", f), window.removeEventListener("blur", h), window.removeEventListener("contextmenu", h);
      };
    }
  }, [e, o]), n;
}
function Gs(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function Js(e, t) {
  return t.includes(e) ? "code" : "key";
}
const Fg = () => {
  const e = we();
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
      const { width: o, height: r, minZoom: i, maxZoom: s, panZoom: c } = e.getState(), d = Mi(t, o, r, i, s, n?.padding ?? 0.1);
      return c ? (await c.setViewport(d, {
        duration: n?.duration,
        ease: n?.ease,
        interpolate: n?.interpolate
      }), !0) : !1;
    },
    screenToFlowPosition: (t, n = {}) => {
      const { transform: o, snapGrid: r, snapToGrid: i, domNode: s } = e.getState();
      if (!s)
        return t;
      const { x: c, y: d } = s.getBoundingClientRect(), l = {
        x: t.x - c,
        y: t.y - d
      }, u = n.snapGrid ?? r, f = n.snapToGrid ?? i;
      return rn(l, o, f, u);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: r, y: i } = o.getBoundingClientRect(), s = tn(t, n);
      return {
        x: s.x + r,
        y: s.y + i
      };
    }
  }), []);
};
function Qc(e, t) {
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
    for (const d of s)
      Bg(d, c);
    n.push(c);
  }
  return r.length && r.forEach((i) => {
    i.index !== void 0 ? n.splice(i.index, 0, { ...i.item }) : n.push({ ...i.item });
  }), n;
}
function Bg(e, t) {
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
function el(e, t) {
  return Qc(e, t);
}
function tl(e, t) {
  return Qc(e, t);
}
function kt(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function Yt(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [r, i] of e) {
    const s = t.has(r);
    !(i.selected === void 0 && !s) && i.selected !== s && (n && (i.selected = s), o.push(kt(i.id, s)));
  }
  return o;
}
function Qs({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((r) => [r.id, r]));
  for (const [r, i] of e.entries()) {
    const s = t.get(i.id), c = s?.internals?.userNode ?? s;
    c !== void 0 && c !== i && n.push({ id: i.id, item: i, type: "replace" }), c === void 0 && n.push({ item: i, type: "add", index: r });
  }
  for (const [r] of t)
    o.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function ea(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const nl = Ic();
function ol(e, t, n = {}) {
  return Ip(e, t, {
    ...n,
    onError: n.onError ?? nl
  });
}
function Wg(e, t, n, o = { shouldReplaceId: !0 }) {
  return Cp(e, t, n, {
    ...o,
    onError: o.onError ?? nl
  });
}
const ta = (e) => fp(e), Xg = (e) => Nc(e);
function rl(e) {
  return er(e);
}
const Yg = typeof window < "u" ? jd : oe;
function na(e) {
  const [t, n] = Y(BigInt(0)), [o] = Y(() => qg(() => n((r) => r + BigInt(1))));
  return Yg(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function qg(e) {
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
const il = vi(null);
function Ug({ children: e }) {
  const t = we(), n = he((c) => {
    const { nodes: d = [], setNodes: l, hasDefaultNodes: u, onNodesChange: f, nodeLookup: h, fitViewQueued: p, onNodesChangeMiddlewareMap: y } = t.getState();
    let v = d;
    for (const m of c)
      v = typeof m == "function" ? m(v) : m;
    let w = Qs({
      items: v,
      lookup: h
    });
    for (const m of y.values())
      w = m(w);
    u && l(v), w.length > 0 ? f?.(w) : p && window.requestAnimationFrame(() => {
      const { fitViewQueued: m, nodes: b, setNodes: g } = t.getState();
      m && g(b);
    });
  }, []), o = na(n), r = he((c) => {
    const { edges: d = [], setEdges: l, hasDefaultEdges: u, onEdgesChange: f, edgeLookup: h } = t.getState();
    let p = d;
    for (const y of c)
      p = typeof y == "function" ? y(p) : y;
    u ? l(p) : f && f(Qs({
      items: p,
      lookup: h
    }));
  }, []), i = na(r), s = me(() => ({ nodeQueue: o, edgeQueue: i }), []);
  return a.jsx(il.Provider, { value: s, children: e });
}
function Zg() {
  const e = Wn(il);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Kg = (e) => !!e.panZoom;
function Li() {
  const e = Fg(), t = we(), n = Zg(), o = ue(Kg), r = me(() => {
    const i = (f) => t.getState().nodeLookup.get(f), s = (f) => {
      n.nodeQueue.push(f);
    }, c = (f) => {
      n.edgeQueue.push(f);
    }, d = (f) => {
      const { nodeLookup: h, nodeOrigin: p } = t.getState(), y = ta(f) ? f : h.get(f.id), v = y.parentId ? jc(y.position, y.measured, y.parentId, h, p) : y.position, w = {
        ...y,
        position: v,
        width: y.measured?.width ?? y.width,
        height: y.measured?.height ?? y.height
      };
      return en(w);
    }, l = (f, h, p = { replace: !1 }) => {
      s((y) => y.map((v) => {
        if (v.id === f) {
          const w = typeof h == "function" ? h(v) : h;
          return p.replace && ta(w) ? w : { ...v, ...w };
        }
        return v;
      }));
    }, u = (f, h, p = { replace: !1 }) => {
      c((y) => y.map((v) => {
        if (v.id === f) {
          const w = typeof h == "function" ? h(v) : h;
          return p.replace && Xg(w) ? w : { ...v, ...w };
        }
        return v;
      }));
    };
    return {
      getNodes: () => t.getState().nodes.map((f) => ({ ...f })),
      getNode: (f) => i(f)?.internals.userNode,
      getInternalNode: i,
      getEdges: () => {
        const { edges: f = [] } = t.getState();
        return f.map((h) => ({ ...h }));
      },
      getEdge: (f) => t.getState().edgeLookup.get(f),
      setNodes: s,
      setEdges: c,
      addNodes: (f) => {
        const h = Array.isArray(f) ? f : [f];
        n.nodeQueue.push((p) => [...p, ...h]);
      },
      addEdges: (f) => {
        const h = Array.isArray(f) ? f : [f];
        n.edgeQueue.push((p) => [...p, ...h]);
      },
      toObject: () => {
        const { nodes: f = [], edges: h = [], transform: p } = t.getState(), [y, v, w] = p;
        return {
          nodes: f.map((m) => ({ ...m })),
          edges: h.map((m) => ({ ...m })),
          viewport: {
            x: y,
            y: v,
            zoom: w
          }
        };
      },
      deleteElements: async ({ nodes: f = [], edges: h = [] }) => {
        const { nodes: p, edges: y, onNodesDelete: v, onEdgesDelete: w, triggerNodeChanges: m, triggerEdgeChanges: b, onDelete: g, onBeforeDelete: x } = t.getState(), { nodes: I, edges: E } = await mp({
          nodesToRemove: f,
          edgesToRemove: h,
          nodes: p,
          edges: y,
          onBeforeDelete: x
        }), j = E.length > 0, P = I.length > 0;
        if (j) {
          const T = E.map(ea);
          w?.(E), b(T);
        }
        if (P) {
          const T = I.map(ea);
          v?.(I), m(T);
        }
        return (P || j) && g?.({ nodes: I, edges: E }), { deletedNodes: I, deletedEdges: E };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, h = !0, p) => {
        const y = ks(f), v = y ? f : d(f), w = p !== void 0;
        return v ? (p || t.getState().nodes).filter((m) => {
          const b = t.getState().nodeLookup.get(m.id);
          if (b && !y && (m.id === f.id || !b.internals.positionAbsolute))
            return !1;
          const g = en(w ? m : b), x = Vn(g, v);
          return h && x > 0 || x >= g.width * g.height || x >= v.width * v.height;
        }) : [];
      },
      isNodeIntersecting: (f, h, p = !0) => {
        const v = ks(f) ? f : d(f);
        if (!v)
          return !1;
        const w = Vn(v, h);
        return p && w > 0 || w >= h.width * h.height || w >= v.width * v.height;
      },
      updateNode: l,
      updateNodeData: (f, h, p = { replace: !1 }) => {
        l(f, (y) => {
          const v = typeof h == "function" ? h(y) : h;
          return p.replace ? { ...y, data: v } : { ...y, data: { ...y.data, ...v } };
        }, p);
      },
      updateEdge: u,
      updateEdgeData: (f, h, p = { replace: !1 }) => {
        u(f, (y) => {
          const v = typeof h == "function" ? h(y) : h;
          return p.replace ? { ...y, data: v } : { ...y, data: { ...y.data, ...v } };
        }, p);
      },
      getNodesBounds: (f) => {
        const { nodeLookup: h, nodeOrigin: p } = t.getState();
        return hp(f, { nodeLookup: h, nodeOrigin: p });
      },
      getHandleConnections: ({ type: f, id: h, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}-${f}${h ? `-${h}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: h, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}${f ? h ? `-${f}-${h}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const h = t.getState().fitViewResolver ?? vp();
        return t.setState({ fitViewQueued: !0, fitViewOptions: f, fitViewResolver: h }), n.nodeQueue.push((p) => [...p]), h.promise;
      }
    };
  }, []);
  return me(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const oa = (e) => e.selected, Gg = typeof window < "u" ? window : void 0;
function Jg({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = we(), { deleteElements: o } = Li(), r = On(e, { actInsideInputWithModifier: !1 }), i = On(t, { target: Gg });
  oe(() => {
    if (r) {
      const { edges: s, nodes: c } = n.getState();
      o({ nodes: c.filter(oa), edges: s.filter(oa) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), oe(() => {
    n.setState({ multiSelectionActive: i });
  }, [i]);
}
function Qg(e) {
  const t = we();
  oe(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = Di(e.current);
      (o.height === 0 || o.width === 0) && t.getState().onError?.("004", Fe.error004()), t.setState({ width: o.width || 500, height: o.height || 500 });
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
const ur = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, ey = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function ty({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: i = jt.Free, zoomOnDoubleClick: s = !0, panOnDrag: c = !0, defaultViewport: d, translateExtent: l, minZoom: u, maxZoom: f, zoomActivationKeyCode: h, preventScrolling: p = !0, children: y, noWheelClassName: v, noPanClassName: w, onViewportChange: m, isControlledViewport: b, paneClickDistance: g, selectionOnDrag: x }) {
  const I = we(), E = ce(null), { userSelectionActive: j, lib: P, connectionInProgress: T } = ue(ey, xe), A = On(h), k = ce();
  Qg(E);
  const R = he((O) => {
    m?.({ x: O[0], y: O[1], zoom: O[2] }), b || I.setState({ transform: O });
  }, [m, b]);
  return oe(() => {
    if (E.current) {
      k.current = og({
        domNode: E.current,
        minZoom: u,
        maxZoom: f,
        translateExtent: l,
        viewport: d,
        onDraggingChange: (C) => I.setState((M) => M.paneDragging === C ? M : { paneDragging: C }),
        onPanZoomStart: (C, M) => {
          const { onViewportChangeStart: z, onMoveStart: $ } = I.getState();
          $?.(C, M), z?.(M);
        },
        onPanZoom: (C, M) => {
          const { onViewportChange: z, onMove: $ } = I.getState();
          $?.(C, M), z?.(M);
        },
        onPanZoomEnd: (C, M) => {
          const { onViewportChangeEnd: z, onMoveEnd: $ } = I.getState();
          $?.(C, M), z?.(M);
        }
      });
      const { x: O, y: S, zoom: _ } = k.current.getViewport();
      return I.setState({
        panZoom: k.current,
        transform: [O, S, _],
        domNode: E.current.closest(".react-flow")
      }), () => {
        k.current?.destroy();
      };
    }
  }, []), oe(() => {
    k.current?.update({
      onPaneContextMenu: e,
      zoomOnScroll: t,
      zoomOnPinch: n,
      panOnScroll: o,
      panOnScrollSpeed: r,
      panOnScrollMode: i,
      zoomOnDoubleClick: s,
      panOnDrag: c,
      zoomActivationKeyPressed: A,
      preventScrolling: p,
      noPanClassName: w,
      userSelectionActive: j,
      noWheelClassName: v,
      lib: P,
      onTransformChange: R,
      connectionInProgress: T,
      selectionOnDrag: x,
      paneClickDistance: g
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
    A,
    p,
    w,
    j,
    v,
    P,
    R,
    T,
    x,
    g
  ]), a.jsx("div", { className: "react-flow__renderer", ref: E, style: ur, children: y });
}
const ny = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function oy() {
  const { userSelectionActive: e, userSelectionRect: t } = ue(ny, xe);
  return e && t ? a.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const Kr = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, ry = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function iy({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Ln.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: i, selectionOnDrag: s, onSelectionStart: c, onSelectionEnd: d, onPaneClick: l, onPaneContextMenu: u, onPaneScroll: f, onPaneMouseEnter: h, onPaneMouseMove: p, onPaneMouseLeave: y, children: v }) {
  const w = ce(0), m = we(), { userSelectionActive: b, elementsSelectable: g, dragging: x, connectionInProgress: I, panBy: E, autoPanSpeed: j } = ue(ry, xe), P = g && (e || b), T = ce(null), A = ce(), k = ce(/* @__PURE__ */ new Set()), R = ce(/* @__PURE__ */ new Set()), O = ce(!1), S = ce({ x: 0, y: 0 }), _ = ce(!1), C = (L) => {
    if (O.current || I) {
      O.current = !1;
      return;
    }
    l?.(L), m.getState().resetSelectedElements(), m.setState({ nodesSelectionActive: !1 });
  }, M = (L) => {
    if (Array.isArray(o) && o?.includes(2)) {
      L.preventDefault();
      return;
    }
    u?.(L);
  }, z = f ? (L) => f(L) : void 0, $ = (L) => {
    O.current && (L.stopPropagation(), O.current = !1);
  }, W = (L) => {
    const { domNode: q, transform: ae } = m.getState();
    if (A.current = q?.getBoundingClientRect(), !A.current)
      return;
    const ie = L.target === T.current;
    if (!ie && !!L.target.closest(".nokey") || !e || !(s && ie || t) || L.button !== 0 || !L.isPrimary)
      return;
    L.target?.setPointerCapture?.(L.pointerId), O.current = !1;
    const { x: le, y: H } = Ze(L.nativeEvent, A.current), U = rn({ x: le, y: H }, ae);
    m.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: U.x,
        startY: U.y,
        x: le,
        y: H
      }
    }), ie || (L.stopPropagation(), L.preventDefault());
  };
  function F(L, q) {
    const { userSelectionRect: ae } = m.getState();
    if (!ae)
      return;
    const { transform: ie, nodeLookup: G, edgeLookup: ee, connectionLookup: le, triggerNodeChanges: H, triggerEdgeChanges: U, defaultEdgeOptions: pe } = m.getState(), ge = { x: ae.startX, y: ae.startY }, { x: Ae, y: ye } = tn(ge, ie), Me = {
      startX: ge.x,
      startY: ge.y,
      x: L < Ae ? L : Ae,
      y: q < ye ? q : ye,
      width: Math.abs(L - Ae),
      height: Math.abs(q - ye)
    }, wt = k.current, ot = R.current;
    k.current = new Set(_i(G, Me, ie, n === Ln.Partial, !0).map(($e) => $e.id)), R.current = /* @__PURE__ */ new Set();
    const rt = pe?.selectable ?? !0;
    for (const $e of k.current) {
      const Ee = le.get($e);
      if (Ee)
        for (const { edgeId: Te } of Ee.values()) {
          const We = ee.get(Te);
          We && (We.selectable ?? rt) && R.current.add(Te);
        }
    }
    if (!Is(wt, k.current)) {
      const $e = Yt(G, k.current, !0);
      H($e);
    }
    if (!Is(ot, R.current)) {
      const $e = Yt(ee, R.current);
      U($e);
    }
    m.setState({
      userSelectionRect: Me,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function B() {
    if (!r || !A.current)
      return;
    const [L, q] = Ai(S.current, A.current, j);
    E({ x: L, y: q }).then((ae) => {
      if (!O.current || !ae) {
        w.current = requestAnimationFrame(B);
        return;
      }
      const { x: ie, y: G } = S.current;
      F(ie, G), w.current = requestAnimationFrame(B);
    });
  }
  const Z = () => {
    cancelAnimationFrame(w.current), w.current = 0, _.current = !1;
  };
  oe(() => () => Z(), []);
  const K = (L) => {
    const { userSelectionRect: q, transform: ae, resetSelectedElements: ie } = m.getState();
    if (!A.current || !q)
      return;
    const { x: G, y: ee } = Ze(L.nativeEvent, A.current);
    S.current = { x: G, y: ee };
    const le = tn({ x: q.startX, y: q.startY }, ae);
    if (!O.current) {
      const H = t ? 0 : i;
      if (Math.hypot(G - le.x, ee - le.y) <= H)
        return;
      ie(), c?.(L);
    }
    O.current = !0, _.current || (B(), _.current = !0), F(G, ee);
  }, ne = (L) => {
    L.button === 0 && (L.target?.releasePointerCapture?.(L.pointerId), !b && L.target === T.current && m.getState().userSelectionRect && C?.(L), m.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), O.current && (d?.(L), m.setState({
      nodesSelectionActive: k.current.size > 0
    })), Z());
  }, se = (L) => {
    L.target?.releasePointerCapture?.(L.pointerId), Z();
  }, Q = o === !0 || Array.isArray(o) && o.includes(0);
  return a.jsxs("div", { className: Ie(["react-flow__pane", { draggable: Q, dragging: x, selection: e }]), onClick: P ? void 0 : Kr(C, T), onContextMenu: Kr(M, T), onWheel: Kr(z, T), onPointerEnter: P ? void 0 : h, onPointerMove: P ? K : p, onPointerUp: P ? ne : void 0, onPointerCancel: P ? se : void 0, onPointerDownCapture: P ? W : void 0, onClickCapture: P ? $ : void 0, onPointerLeave: y, ref: T, style: ur, children: [v, a.jsx(oy, {})] });
}
function hi({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: i, multiSelectionActive: s, nodeLookup: c, onError: d } = t.getState(), l = c.get(e);
  if (!l) {
    d?.("012", Fe.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && s) && (i({ nodes: [l], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function sl({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: i, nodeClickDistance: s }) {
  const c = we(), [d, l] = Y(!1), u = ce();
  return oe(() => {
    u.current = Bp({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (f) => {
        hi({
          id: f,
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
  }, []), oe(() => {
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
  }, [n, o, t, i, e, r, s]), d;
}
const sy = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function al() {
  const e = we();
  return he((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: i, nodesDraggable: s, onError: c, updateNodePositions: d, nodeLookup: l, nodeOrigin: u } = e.getState(), f = /* @__PURE__ */ new Map(), h = sy(s), p = r ? i[0] : 5, y = r ? i[1] : 5, v = n.direction.x * p * n.factor, w = n.direction.y * y * n.factor;
    for (const [, m] of l) {
      if (!h(m))
        continue;
      let b = {
        x: m.internals.positionAbsolute.x + v,
        y: m.internals.positionAbsolute.y + w
      };
      r && (b = Zn(b, i));
      const { position: g, positionAbsolute: x } = Sc({
        nodeId: m.id,
        nextPosition: b,
        nodeLookup: l,
        nodeExtent: o,
        nodeOrigin: u,
        onError: c
      });
      m.position = g, m.internals.positionAbsolute = x, f.set(m.id, m);
    }
    d(f);
  }, []);
}
const Vi = vi(null), ay = Vi.Provider;
Vi.Consumer;
const cl = () => Wn(Vi), cy = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), ly = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: i, connection: s } = o, { fromHandle: c, toHandle: d, isValid: l } = s, u = d?.nodeId === e && d?.id === t && d?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: u,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: i === Jt.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!r,
    valid: u && l
  };
};
function dy({ type: e = "source", position: t = te.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: i = !0, id: s, onConnect: c, children: d, className: l, onMouseDown: u, onTouchStart: f, ...h }, p) {
  const y = s || null, v = e === "target", w = we(), m = cl(), { connectOnClick: b, noPanClassName: g, rfId: x } = ue(cy, xe), { connectingFrom: I, connectingTo: E, clickConnecting: j, isPossibleEndHandle: P, connectionInProcess: T, clickConnectionInProcess: A, valid: k } = ue(ly(m, y, e), xe);
  m || w.getState().onError?.("010", Fe.error010());
  const R = (_) => {
    const { defaultEdgeOptions: C, onConnect: M, hasDefaultEdges: z } = w.getState(), $ = {
      ...C,
      ..._
    };
    if (z) {
      const { edges: W, setEdges: F, onError: B } = w.getState();
      F(ol($, W, { onError: B }));
    }
    M?.($), c?.($);
  }, O = (_) => {
    if (!m)
      return;
    const C = Mc(_.nativeEvent);
    if (r && (C && _.button === 0 || !C)) {
      const M = w.getState();
      fi.onPointerDown(_.nativeEvent, {
        handleDomNode: _.currentTarget,
        autoPanOnConnect: M.autoPanOnConnect,
        connectionMode: M.connectionMode,
        connectionRadius: M.connectionRadius,
        domNode: M.domNode,
        nodeLookup: M.nodeLookup,
        lib: M.lib,
        isTarget: v,
        handleId: y,
        nodeId: m,
        flowId: M.rfId,
        panBy: M.panBy,
        cancelConnection: M.cancelConnection,
        onConnectStart: M.onConnectStart,
        onConnectEnd: (...z) => w.getState().onConnectEnd?.(...z),
        updateConnection: M.updateConnection,
        onConnect: R,
        isValidConnection: n || ((...z) => w.getState().isValidConnection?.(...z) ?? !0),
        getTransform: () => w.getState().transform,
        getFromHandle: () => w.getState().connection.fromHandle,
        autoPanSpeed: M.autoPanSpeed,
        dragThreshold: M.connectionDragThreshold
      });
    }
    C ? u?.(_) : f?.(_);
  }, S = (_) => {
    const { onClickConnectStart: C, onClickConnectEnd: M, connectionClickStartHandle: z, connectionMode: $, isValidConnection: W, lib: F, rfId: B, nodeLookup: Z, connection: K } = w.getState();
    if (!m || !z && !r)
      return;
    if (!z) {
      C?.(_.nativeEvent, { nodeId: m, handleId: y, handleType: e }), w.setState({ connectionClickStartHandle: { nodeId: m, type: e, id: y } });
      return;
    }
    const ne = _c(_.target), se = n || W, { connection: Q, isValid: L } = fi.isValid(_.nativeEvent, {
      handle: {
        nodeId: m,
        id: y,
        type: e
      },
      connectionMode: $,
      fromNodeId: z.nodeId,
      fromHandleId: z.id || null,
      fromType: z.type,
      isValidConnection: se,
      flowId: B,
      doc: ne,
      lib: F,
      nodeLookup: Z
    });
    L && Q && R(Q);
    const q = structuredClone(K);
    delete q.inProgress, q.toPosition = q.toHandle ? q.toHandle.position : null, M?.(_, q), w.setState({ connectionClickStartHandle: null });
  };
  return a.jsx("div", { "data-handleid": y, "data-nodeid": m, "data-handlepos": t, "data-id": `${x}-${m}-${y}-${e}`, className: Ie([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    g,
    l,
    {
      source: !v,
      target: v,
      connectable: o,
      connectablestart: r,
      connectableend: i,
      clickconnecting: j,
      connectingfrom: I,
      connectingto: E,
      valid: k,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!T || P) && (T || A ? i : r)
    }
  ]), onMouseDown: O, onTouchStart: O, onClick: b ? S : void 0, ref: p, ...h, children: d });
}
const on = Se(rl(dy));
function uy({ data: e, isConnectable: t, sourcePosition: n = te.Bottom }) {
  return a.jsxs(a.Fragment, { children: [e?.label, a.jsx(on, { type: "source", position: n, isConnectable: t })] });
}
function fy({ data: e, isConnectable: t, targetPosition: n = te.Top, sourcePosition: o = te.Bottom }) {
  return a.jsxs(a.Fragment, { children: [a.jsx(on, { type: "target", position: n, isConnectable: t }), e?.label, a.jsx(on, { type: "source", position: o, isConnectable: t })] });
}
function hy() {
  return null;
}
function py({ data: e, isConnectable: t, targetPosition: n = te.Top }) {
  return a.jsxs(a.Fragment, { children: [a.jsx(on, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const Uo = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, ra = {
  input: uy,
  default: fy,
  output: py,
  group: hy
};
function gy(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const yy = (e) => {
  const { width: t, height: n, x: o, y: r } = Un(e.nodeLookup, {
    filter: (i) => !!i.selected
  });
  return {
    width: Ue(t) ? t : null,
    height: Ue(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function my({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = we(), { width: r, height: i, transformString: s, userSelectionActive: c } = ue(yy, xe), d = al(), l = ce(null);
  oe(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const u = !c && r !== null && i !== null;
  if (sl({
    nodeRef: l,
    disabled: !u
  }), !u)
    return null;
  const f = e ? (p) => {
    const y = o.getState().nodes.filter((v) => v.selected);
    e(p, y);
  } : void 0, h = (p) => {
    Object.prototype.hasOwnProperty.call(Uo, p.key) && (p.preventDefault(), d({
      direction: Uo[p.key],
      factor: p.shiftKey ? 4 : 1
    }));
  };
  return a.jsx("div", { className: Ie(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: s
  }, children: a.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : h, style: {
    width: r,
    height: i
  } }) });
}
const ia = typeof window < "u" ? window : void 0, xy = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function ll({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, paneClickDistance: c, deleteKeyCode: d, selectionKeyCode: l, selectionOnDrag: u, selectionMode: f, onSelectionStart: h, onSelectionEnd: p, multiSelectionKeyCode: y, panActivationKeyCode: v, zoomActivationKeyCode: w, elementsSelectable: m, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: x, panOnScrollSpeed: I, panOnScrollMode: E, zoomOnDoubleClick: j, panOnDrag: P, autoPanOnSelection: T, defaultViewport: A, translateExtent: k, minZoom: R, maxZoom: O, preventScrolling: S, onSelectionContextMenu: _, noWheelClassName: C, noPanClassName: M, disableKeyboardA11y: z, onViewportChange: $, isControlledViewport: W }) {
  const { nodesSelectionActive: F, userSelectionActive: B } = ue(xy, xe), Z = On(l, { target: ia }), K = On(v, { target: ia }), ne = K || P, se = K || x, Q = u && ne !== !0, L = Z || B || Q;
  return Jg({ deleteKeyCode: d, multiSelectionKeyCode: y }), a.jsx(ty, { onPaneContextMenu: i, elementsSelectable: m, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: se, panOnScrollSpeed: I, panOnScrollMode: E, zoomOnDoubleClick: j, panOnDrag: !Z && ne, defaultViewport: A, translateExtent: k, minZoom: R, maxZoom: O, zoomActivationKeyCode: w, preventScrolling: S, noWheelClassName: C, noPanClassName: M, onViewportChange: $, isControlledViewport: W, paneClickDistance: c, selectionOnDrag: Q, children: a.jsxs(iy, { onSelectionStart: h, onSelectionEnd: p, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, panOnDrag: ne, autoPanOnSelection: T, isSelecting: !!L, selectionMode: f, selectionKeyPressed: Z, paneClickDistance: c, selectionOnDrag: Q, children: [e, F && a.jsx(my, { onSelectionContextMenu: _, noPanClassName: M, disableKeyboardA11y: z })] }) });
}
ll.displayName = "FlowRenderer";
const wy = Se(ll), vy = (e) => (t) => e ? _i(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function by(e) {
  return ue(he(vy(e), [e]), xe);
}
const Ny = (e) => e.updateNodeInternals;
function Sy() {
  const e = ue(Ny), [t] = Y(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
  return oe(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function Ey({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = we(), i = ce(null), s = ce(null), c = ce(e.sourcePosition), d = ce(e.targetPosition), l = ce(t), u = n && !!e.internals.handleBounds;
  return oe(() => {
    i.current && !e.hidden && (!u || s.current !== i.current) && (s.current && o?.unobserve(s.current), o?.observe(i.current), s.current = i.current);
  }, [u, e.hidden]), oe(() => () => {
    s.current && (o?.unobserve(s.current), s.current = null);
  }, []), oe(() => {
    if (i.current) {
      const f = l.current !== t, h = c.current !== e.sourcePosition, p = d.current !== e.targetPosition;
      (f || h || p) && (l.current = t, c.current = e.sourcePosition, d.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: i.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), i;
}
function ky({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: i, onDoubleClick: s, nodesDraggable: c, elementsSelectable: d, nodesConnectable: l, nodesFocusable: u, resizeObserver: f, noDragClassName: h, noPanClassName: p, disableKeyboardA11y: y, rfId: v, nodeTypes: w, nodeClickDistance: m, onError: b }) {
  const { node: g, internals: x, isParent: I } = ue((L) => {
    const q = L.nodeLookup.get(e), ae = L.parentLookup.has(e);
    return {
      node: q,
      internals: q.internals,
      isParent: ae
    };
  }, xe);
  let E = g.type || "default", j = w?.[E] || ra[E];
  j === void 0 && (b?.("003", Fe.error003(E)), E = "default", j = w?.default || ra.default);
  const P = !!(g.draggable || c && typeof g.draggable > "u"), T = !!(g.selectable || d && typeof g.selectable > "u"), A = !!(g.connectable || l && typeof g.connectable > "u"), k = !!(g.focusable || u && typeof g.focusable > "u"), R = we(), O = Cc(g), S = Ey({ node: g, nodeType: E, hasDimensions: O, resizeObserver: f }), _ = sl({
    nodeRef: S,
    disabled: g.hidden || !P,
    noDragClassName: h,
    handleSelector: g.dragHandle,
    nodeId: e,
    isSelectable: T,
    nodeClickDistance: m
  }), C = al();
  if (g.hidden)
    return null;
  const M = lt(g), z = gy(g), $ = T || P || t || n || o || r, W = n ? (L) => n(L, { ...x.userNode }) : void 0, F = o ? (L) => o(L, { ...x.userNode }) : void 0, B = r ? (L) => r(L, { ...x.userNode }) : void 0, Z = i ? (L) => i(L, { ...x.userNode }) : void 0, K = s ? (L) => s(L, { ...x.userNode }) : void 0, ne = (L) => {
    const { selectNodesOnDrag: q, nodeDragThreshold: ae } = R.getState();
    T && (!q || !P || ae > 0) && hi({
      id: e,
      store: R,
      nodeRef: S
    }), t && t(L, { ...x.userNode });
  }, se = (L) => {
    if (!(Ac(L.nativeEvent) || y)) {
      if (xc.includes(L.key) && T) {
        const q = L.key === "Escape";
        hi({
          id: e,
          store: R,
          unselect: q,
          nodeRef: S
        });
      } else if (P && g.selected && Object.prototype.hasOwnProperty.call(Uo, L.key)) {
        L.preventDefault();
        const { ariaLabelConfig: q } = R.getState();
        R.setState({
          ariaLiveMessage: q["node.a11yDescription.ariaLiveMessage"]({
            direction: L.key.replace("Arrow", "").toLowerCase(),
            x: ~~x.positionAbsolute.x,
            y: ~~x.positionAbsolute.y
          })
        }), C({
          direction: Uo[L.key],
          factor: L.shiftKey ? 4 : 1
        });
      }
    }
  }, Q = () => {
    if (y || !S.current?.matches(":focus-visible"))
      return;
    const { transform: L, width: q, height: ae, autoPanOnNodeFocus: ie, setCenter: G } = R.getState();
    if (!ie)
      return;
    _i(/* @__PURE__ */ new Map([[e, g]]), { x: 0, y: 0, width: q, height: ae }, L, !0).length > 0 || G(g.position.x + M.width / 2, g.position.y + M.height / 2, {
      zoom: L[2]
    });
  };
  return a.jsx("div", { className: Ie([
    "react-flow__node",
    `react-flow__node-${E}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [p]: P
    },
    g.className,
    {
      selected: g.selected,
      selectable: T,
      parent: I,
      draggable: P,
      dragging: _
    }
  ]), ref: S, style: {
    zIndex: x.z,
    transform: `translate(${x.positionAbsolute.x}px,${x.positionAbsolute.y}px)`,
    pointerEvents: $ ? "all" : "none",
    visibility: O ? "visible" : "hidden",
    ...g.style,
    ...z
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: W, onMouseMove: F, onMouseLeave: B, onContextMenu: Z, onClick: ne, onDoubleClick: K, onKeyDown: k ? se : void 0, tabIndex: k ? 0 : void 0, onFocus: k ? Q : void 0, role: g.ariaRole ?? (k ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": y ? void 0 : `${Kc}-${v}`, "aria-label": g.ariaLabel, ...g.domAttributes, children: a.jsx(ay, { value: e, children: a.jsx(j, { id: e, data: g.data, type: E, positionAbsoluteX: x.positionAbsolute.x, positionAbsoluteY: x.positionAbsolute.y, selected: g.selected ?? !1, selectable: T, draggable: P, deletable: g.deletable ?? !0, isConnectable: A, sourcePosition: g.sourcePosition, targetPosition: g.targetPosition, dragging: _, dragHandle: g.dragHandle, zIndex: x.z, parentId: g.parentId, ...M }) }) });
}
var Iy = Se(ky);
const Cy = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function dl(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: i } = ue(Cy, xe), s = by(e.onlyRenderVisibleElements), c = Sy();
  return a.jsx("div", { className: "react-flow__nodes", style: ur, children: s.map((d) => (
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
    a.jsx(Iy, { id: d, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: i }, d)
  )) });
}
dl.displayName = "NodeRenderer";
const jy = Se(dl);
function _y(e) {
  return ue(he((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const i = n.nodeLookup.get(r.source), s = n.nodeLookup.get(r.target);
        i && s && Ep({
          sourceNode: i,
          targetNode: s,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && o.push(r.id);
      }
    return o;
  }, [e]), xe);
}
const Ay = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return a.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, My = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return a.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, sa = {
  [Xo.Arrow]: Ay,
  [Xo.ArrowClosed]: My
};
function Dy(e) {
  const t = we();
  return me(() => Object.prototype.hasOwnProperty.call(sa, e) ? sa[e] : (t.getState().onError?.("009", Fe.error009(e)), null), [e]);
}
const Py = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: i = "strokeWidth", strokeWidth: s, orient: c = "auto-start-reverse" }) => {
  const d = Dy(t);
  return d ? a.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: i, orient: c, refX: "0", refY: "0", children: a.jsx(d, { color: n, strokeWidth: s }) }) : null;
}, ul = ({ defaultColor: e, rfId: t }) => {
  const n = ue((i) => i.edges), o = ue((i) => i.defaultEdgeOptions), r = me(() => Dp(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? a.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: a.jsx("defs", { children: r.map((i) => a.jsx(Py, { id: i.id, type: i.type, color: i.color, width: i.width, height: i.height, markerUnits: i.markerUnits, strokeWidth: i.strokeWidth, orient: i.orient }, i.id)) }) }) : null;
};
ul.displayName = "MarkerDefinitions";
var $y = Se(ul);
function fl({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: i, labelBgPadding: s = [2, 4], labelBgBorderRadius: c = 2, children: d, className: l, ...u }) {
  const [f, h] = Y({ x: 1, y: 0, width: 0, height: 0 }), p = Ie(["react-flow__edge-textwrapper", l]), y = ce(null);
  return oe(() => {
    if (y.current) {
      const v = y.current.getBBox();
      h({
        x: v.x,
        y: v.y,
        width: v.width,
        height: v.height
      });
    }
  }, [n]), n ? a.jsxs("g", { transform: `translate(${e - f.width / 2} ${t - f.height / 2})`, className: p, visibility: f.width ? "visible" : "hidden", ...u, children: [r && a.jsx("rect", { width: f.width + 2 * s[0], x: -s[0], y: -s[1], height: f.height + 2 * s[1], className: "react-flow__edge-textbg", style: i, rx: c, ry: c }), a.jsx("text", { className: "react-flow__edge-text", y: f.height / 2, dy: "0.3em", ref: y, style: o, children: n }), d] }) : null;
}
fl.displayName = "EdgeText";
const Ty = Se(fl);
function Kn({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: c, labelBgBorderRadius: d, interactionWidth: l = 20, ...u }) {
  return a.jsxs(a.Fragment, { children: [a.jsx("path", { ...u, d: e, fill: "none", className: Ie(["react-flow__edge-path", u.className]) }), l ? a.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, o && Ue(t) && Ue(n) ? a.jsx(Ty, { x: t, y: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: c, labelBgBorderRadius: d }) : null] });
}
function aa({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === te.Left || e === te.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function hl({ sourceX: e, sourceY: t, sourcePosition: n = te.Bottom, targetX: o, targetY: r, targetPosition: i = te.Top }) {
  const [s, c] = aa({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [d, l] = aa({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [u, f, h, p] = Dc({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: r,
    sourceControlX: s,
    sourceControlY: c,
    targetControlX: d,
    targetControlY: l
  });
  return [
    `M${e},${t} C${s},${c} ${d},${l} ${o},${r}`,
    u,
    f,
    h,
    p
  ];
}
function pl(e) {
  return Se(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: c, label: d, labelStyle: l, labelShowBg: u, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: y, markerEnd: v, markerStart: w, interactionWidth: m }) => {
    const [b, g, x] = hl({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: c
    }), I = e.isInternal ? void 0 : t;
    return a.jsx(Kn, { id: I, path: b, labelX: g, labelY: x, label: d, labelStyle: l, labelShowBg: u, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: y, markerEnd: v, markerStart: w, interactionWidth: m });
  });
}
const zy = pl({ isInternal: !1 }), gl = pl({ isInternal: !0 });
zy.displayName = "SimpleBezierEdge";
gl.displayName = "SimpleBezierEdgeInternal";
function yl(e) {
  return Se(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: c, labelShowBg: d, labelBgStyle: l, labelBgPadding: u, labelBgBorderRadius: f, style: h, sourcePosition: p = te.Bottom, targetPosition: y = te.Top, markerEnd: v, markerStart: w, pathOptions: m, interactionWidth: b }) => {
    const [g, x, I] = qo({
      sourceX: n,
      sourceY: o,
      sourcePosition: p,
      targetX: r,
      targetY: i,
      targetPosition: y,
      borderRadius: m?.borderRadius,
      offset: m?.offset,
      stepPosition: m?.stepPosition
    }), E = e.isInternal ? void 0 : t;
    return a.jsx(Kn, { id: E, path: g, labelX: x, labelY: I, label: s, labelStyle: c, labelShowBg: d, labelBgStyle: l, labelBgPadding: u, labelBgBorderRadius: f, style: h, markerEnd: v, markerStart: w, interactionWidth: b });
  });
}
const ml = yl({ isInternal: !1 }), xl = yl({ isInternal: !0 });
ml.displayName = "SmoothStepEdge";
xl.displayName = "SmoothStepEdgeInternal";
function wl(e) {
  return Se(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return a.jsx(ml, { ...n, id: o, pathOptions: me(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Ry = wl({ isInternal: !1 }), vl = wl({ isInternal: !0 });
Ry.displayName = "StepEdge";
vl.displayName = "StepEdgeInternal";
function bl(e) {
  return Se(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: c, labelShowBg: d, labelBgStyle: l, labelBgPadding: u, labelBgBorderRadius: f, style: h, markerEnd: p, markerStart: y, interactionWidth: v }) => {
    const [w, m, b] = zc({ sourceX: n, sourceY: o, targetX: r, targetY: i }), g = e.isInternal ? void 0 : t;
    return a.jsx(Kn, { id: g, path: w, labelX: m, labelY: b, label: s, labelStyle: c, labelShowBg: d, labelBgStyle: l, labelBgPadding: u, labelBgBorderRadius: f, style: h, markerEnd: p, markerStart: y, interactionWidth: v });
  });
}
const Ly = bl({ isInternal: !1 }), Nl = bl({ isInternal: !0 });
Ly.displayName = "StraightEdge";
Nl.displayName = "StraightEdgeInternal";
function Sl(e) {
  return Se(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s = te.Bottom, targetPosition: c = te.Top, label: d, labelStyle: l, labelShowBg: u, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: y, markerEnd: v, markerStart: w, pathOptions: m, interactionWidth: b }) => {
    const [g, x, I] = Pc({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: c,
      curvature: m?.curvature
    }), E = e.isInternal ? void 0 : t;
    return a.jsx(Kn, { id: E, path: g, labelX: x, labelY: I, label: d, labelStyle: l, labelShowBg: u, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: y, markerEnd: v, markerStart: w, interactionWidth: b });
  });
}
const Vy = Sl({ isInternal: !1 }), El = Sl({ isInternal: !0 });
Vy.displayName = "BezierEdge";
El.displayName = "BezierEdgeInternal";
const ca = {
  default: El,
  straight: Nl,
  step: vl,
  smoothstep: xl,
  simplebezier: gl
}, la = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, Hy = (e, t, n) => n === te.Left ? e - t : n === te.Right ? e + t : e, Oy = (e, t, n) => n === te.Top ? e - t : n === te.Bottom ? e + t : e, da = "react-flow__edgeupdater";
function ua({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: i, onMouseOut: s, type: c }) {
  return a.jsx("circle", { onMouseDown: r, onMouseEnter: i, onMouseOut: s, className: Ie([da, `${da}-${c}`]), cx: Hy(t, o, e), cy: Oy(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function Fy({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: i, targetY: s, sourcePosition: c, targetPosition: d, onReconnect: l, onReconnectStart: u, onReconnectEnd: f, setReconnecting: h, setUpdateHover: p }) {
  const y = we(), v = (x, I) => {
    if (x.button !== 0)
      return;
    const { autoPanOnConnect: E, domNode: j, connectionMode: P, connectionRadius: T, lib: A, onConnectStart: k, cancelConnection: R, nodeLookup: O, rfId: S, panBy: _, updateConnection: C } = y.getState(), M = I.type === "target", z = (F, B) => {
      h(!1), f?.(F, n, I.type, B);
    }, $ = (F) => l?.(n, F), W = (F, B) => {
      h(!0), u?.(x, n, I.type), k?.(F, B);
    };
    fi.onPointerDown(x.nativeEvent, {
      autoPanOnConnect: E,
      connectionMode: P,
      connectionRadius: T,
      domNode: j,
      handleId: I.id,
      nodeId: I.nodeId,
      nodeLookup: O,
      isTarget: M,
      edgeUpdaterType: I.type,
      lib: A,
      flowId: S,
      cancelConnection: R,
      panBy: _,
      isValidConnection: (...F) => y.getState().isValidConnection?.(...F) ?? !0,
      onConnect: $,
      onConnectStart: W,
      onConnectEnd: (...F) => y.getState().onConnectEnd?.(...F),
      onReconnectEnd: z,
      updateConnection: C,
      getTransform: () => y.getState().transform,
      getFromHandle: () => y.getState().connection.fromHandle,
      dragThreshold: y.getState().connectionDragThreshold,
      handleDomNode: x.currentTarget
    });
  }, w = (x) => v(x, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), m = (x) => v(x, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), b = () => p(!0), g = () => p(!1);
  return a.jsxs(a.Fragment, { children: [(e === !0 || e === "source") && a.jsx(ua, { position: c, centerX: o, centerY: r, radius: t, onMouseDown: w, onMouseEnter: b, onMouseOut: g, type: "source" }), (e === !0 || e === "target") && a.jsx(ua, { position: d, centerX: i, centerY: s, radius: t, onMouseDown: m, onMouseEnter: b, onMouseOut: g, type: "target" })] });
}
function By({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: i, onContextMenu: s, onMouseEnter: c, onMouseMove: d, onMouseLeave: l, reconnectRadius: u, onReconnect: f, onReconnectStart: h, onReconnectEnd: p, rfId: y, edgeTypes: v, noPanClassName: w, onError: m, disableKeyboardA11y: b }) {
  let g = ue((G) => G.edgeLookup.get(e));
  const x = ue((G) => G.defaultEdgeOptions);
  g = x ? { ...x, ...g } : g;
  let I = g.type || "default", E = v?.[I] || ca[I];
  E === void 0 && (m?.("011", Fe.error011(I)), I = "default", E = v?.default || ca.default);
  const j = !!(g.focusable || t && typeof g.focusable > "u"), P = typeof f < "u" && (g.reconnectable || n && typeof g.reconnectable > "u"), T = !!(g.selectable || o && typeof g.selectable > "u"), A = ce(null), [k, R] = Y(!1), [O, S] = Y(!1), _ = we(), { zIndex: C, sourceX: M, sourceY: z, targetX: $, targetY: W, sourcePosition: F, targetPosition: B } = ue(he((G) => {
    const ee = G.nodeLookup.get(g.source), le = G.nodeLookup.get(g.target);
    if (!ee || !le)
      return {
        zIndex: g.zIndex,
        ...la
      };
    const H = Mp({
      id: e,
      sourceNode: ee,
      targetNode: le,
      sourceHandle: g.sourceHandle || null,
      targetHandle: g.targetHandle || null,
      connectionMode: G.connectionMode,
      onError: m
    });
    return {
      zIndex: Sp({
        selected: g.selected,
        zIndex: g.zIndex,
        sourceNode: ee,
        targetNode: le,
        elevateOnSelect: G.elevateEdgesOnSelect,
        zIndexMode: G.zIndexMode
      }),
      ...H || la
    };
  }, [g.source, g.target, g.sourceHandle, g.targetHandle, g.selected, g.zIndex]), xe), Z = me(() => g.markerStart ? `url('#${di(g.markerStart, y)}')` : void 0, [g.markerStart, y]), K = me(() => g.markerEnd ? `url('#${di(g.markerEnd, y)}')` : void 0, [g.markerEnd, y]);
  if (g.hidden || M === null || z === null || $ === null || W === null)
    return null;
  const ne = (G) => {
    const { addSelectedEdges: ee, unselectNodesAndEdges: le, multiSelectionActive: H } = _.getState();
    T && (_.setState({ nodesSelectionActive: !1 }), g.selected && H ? (le({ nodes: [], edges: [g] }), A.current?.blur()) : ee([e])), r && r(G, g);
  }, se = i ? (G) => {
    i(G, { ...g });
  } : void 0, Q = s ? (G) => {
    s(G, { ...g });
  } : void 0, L = c ? (G) => {
    c(G, { ...g });
  } : void 0, q = d ? (G) => {
    d(G, { ...g });
  } : void 0, ae = l ? (G) => {
    l(G, { ...g });
  } : void 0, ie = (G) => {
    if (!b && xc.includes(G.key) && T) {
      const { unselectNodesAndEdges: ee, addSelectedEdges: le } = _.getState();
      G.key === "Escape" ? (A.current?.blur(), ee({ edges: [g] })) : le([e]);
    }
  };
  return a.jsx("svg", { style: { zIndex: C }, children: a.jsxs("g", { className: Ie([
    "react-flow__edge",
    `react-flow__edge-${I}`,
    g.className,
    w,
    {
      selected: g.selected,
      animated: g.animated,
      inactive: !T && !r,
      updating: k,
      selectable: T
    }
  ]), onClick: ne, onDoubleClick: se, onContextMenu: Q, onMouseEnter: L, onMouseMove: q, onMouseLeave: ae, onKeyDown: j ? ie : void 0, tabIndex: j ? 0 : void 0, role: g.ariaRole ?? (j ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": g.ariaLabel === null ? void 0 : g.ariaLabel || `Edge from ${g.source} to ${g.target}`, "aria-describedby": j ? `${Gc}-${y}` : void 0, ref: A, ...g.domAttributes, children: [!O && a.jsx(E, { id: e, source: g.source, target: g.target, type: g.type, selected: g.selected, animated: g.animated, selectable: T, deletable: g.deletable ?? !0, label: g.label, labelStyle: g.labelStyle, labelShowBg: g.labelShowBg, labelBgStyle: g.labelBgStyle, labelBgPadding: g.labelBgPadding, labelBgBorderRadius: g.labelBgBorderRadius, sourceX: M, sourceY: z, targetX: $, targetY: W, sourcePosition: F, targetPosition: B, data: g.data, style: g.style, sourceHandleId: g.sourceHandle, targetHandleId: g.targetHandle, markerStart: Z, markerEnd: K, pathOptions: "pathOptions" in g ? g.pathOptions : void 0, interactionWidth: g.interactionWidth }), P && a.jsx(Fy, { edge: g, isReconnectable: P, reconnectRadius: u, onReconnect: f, onReconnectStart: h, onReconnectEnd: p, sourceX: M, sourceY: z, targetX: $, targetY: W, sourcePosition: F, targetPosition: B, setUpdateHover: R, setReconnecting: S })] }) });
}
var Wy = Se(By);
const Xy = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function kl({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: i, onEdgeContextMenu: s, onEdgeMouseEnter: c, onEdgeMouseMove: d, onEdgeMouseLeave: l, onEdgeClick: u, reconnectRadius: f, onEdgeDoubleClick: h, onReconnectStart: p, onReconnectEnd: y, disableKeyboardA11y: v }) {
  const { edgesFocusable: w, edgesReconnectable: m, elementsSelectable: b, onError: g } = ue(Xy, xe), x = _y(t);
  return a.jsxs("div", { className: "react-flow__edges", children: [a.jsx($y, { defaultColor: e, rfId: n }), x.map((I) => a.jsx(Wy, { id: I, edgesFocusable: w, edgesReconnectable: m, elementsSelectable: b, noPanClassName: r, onReconnect: i, onContextMenu: s, onMouseEnter: c, onMouseMove: d, onMouseLeave: l, onClick: u, reconnectRadius: f, onDoubleClick: h, onReconnectStart: p, onReconnectEnd: y, rfId: n, onError: g, edgeTypes: o, disableKeyboardA11y: v }, I))] });
}
kl.displayName = "EdgeRenderer";
const Yy = Se(kl), qy = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function Uy({ children: e }) {
  const t = ue(qy);
  return a.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function Zy(e) {
  const t = Li(), n = ce(!1);
  oe(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Ky = (e) => e.panZoom?.syncViewport;
function Gy(e) {
  const t = ue(Ky), n = we();
  return oe(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function Jy(e) {
  return e.connection.inProgress ? { ...e.connection, to: rn(e.connection.to, e.transform) } : { ...e.connection };
}
function Qy(e) {
  return Jy;
}
function em(e) {
  const t = Qy();
  return ue(t, xe);
}
const tm = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function nm({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: i, height: s, isValid: c, inProgress: d } = ue(tm, xe);
  return !(i && r && d) ? null : a.jsx("svg", { style: e, width: i, height: s, className: "react-flow__connectionline react-flow__container", children: a.jsx("g", { className: Ie(["react-flow__connection", bc(c)]), children: a.jsx(Il, { style: t, type: n, CustomComponent: o, isValid: c }) }) });
}
const Il = ({ style: e, type: t = pt.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: i, fromNode: s, fromHandle: c, fromPosition: d, to: l, toNode: u, toHandle: f, toPosition: h, pointer: p } = em();
  if (!r)
    return;
  if (n)
    return a.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: s, fromHandle: c, fromX: i.x, fromY: i.y, toX: l.x, toY: l.y, fromPosition: d, toPosition: h, connectionStatus: bc(o), toNode: u, toHandle: f, pointer: p });
  let y = "";
  const v = {
    sourceX: i.x,
    sourceY: i.y,
    sourcePosition: d,
    targetX: l.x,
    targetY: l.y,
    targetPosition: h
  };
  switch (t) {
    case pt.Bezier:
      [y] = Pc(v);
      break;
    case pt.SimpleBezier:
      [y] = hl(v);
      break;
    case pt.Step:
      [y] = qo({
        ...v,
        borderRadius: 0
      });
      break;
    case pt.SmoothStep:
      [y] = qo(v);
      break;
    default:
      [y] = zc(v);
  }
  return a.jsx("path", { d: y, fill: "none", className: "react-flow__connection-path", style: e });
};
Il.displayName = "ConnectionLine";
const om = {};
function fa(e = om) {
  ce(e), we(), oe(() => {
  }, [e]);
}
function rm() {
  we(), ce(!1), oe(() => {
  }, []);
}
function Cl({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: i, onEdgeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: d, onNodeMouseLeave: l, onNodeContextMenu: u, onSelectionContextMenu: f, onSelectionStart: h, onSelectionEnd: p, connectionLineType: y, connectionLineStyle: v, connectionLineComponent: w, connectionLineContainerStyle: m, selectionKeyCode: b, selectionOnDrag: g, selectionMode: x, multiSelectionKeyCode: I, panActivationKeyCode: E, zoomActivationKeyCode: j, deleteKeyCode: P, onlyRenderVisibleElements: T, elementsSelectable: A, defaultViewport: k, translateExtent: R, minZoom: O, maxZoom: S, preventScrolling: _, defaultMarkerColor: C, zoomOnScroll: M, zoomOnPinch: z, panOnScroll: $, panOnScrollSpeed: W, panOnScrollMode: F, zoomOnDoubleClick: B, panOnDrag: Z, autoPanOnSelection: K, onPaneClick: ne, onPaneMouseEnter: se, onPaneMouseMove: Q, onPaneMouseLeave: L, onPaneScroll: q, onPaneContextMenu: ae, paneClickDistance: ie, nodeClickDistance: G, onEdgeContextMenu: ee, onEdgeMouseEnter: le, onEdgeMouseMove: H, onEdgeMouseLeave: U, reconnectRadius: pe, onReconnect: ge, onReconnectStart: Ae, onReconnectEnd: ye, noDragClassName: Me, noWheelClassName: wt, noPanClassName: ot, disableKeyboardA11y: rt, nodeExtent: $e, rfId: Ee, viewport: Te, onViewportChange: We }) {
  return fa(e), fa(t), rm(), Zy(n), Gy(Te), a.jsx(wy, { onPaneClick: ne, onPaneMouseEnter: se, onPaneMouseMove: Q, onPaneMouseLeave: L, onPaneContextMenu: ae, onPaneScroll: q, paneClickDistance: ie, deleteKeyCode: P, selectionKeyCode: b, selectionOnDrag: g, selectionMode: x, onSelectionStart: h, onSelectionEnd: p, multiSelectionKeyCode: I, panActivationKeyCode: E, zoomActivationKeyCode: j, elementsSelectable: A, zoomOnScroll: M, zoomOnPinch: z, zoomOnDoubleClick: B, panOnScroll: $, panOnScrollSpeed: W, panOnScrollMode: F, panOnDrag: Z, autoPanOnSelection: K, defaultViewport: k, translateExtent: R, minZoom: O, maxZoom: S, onSelectionContextMenu: f, preventScrolling: _, noDragClassName: Me, noWheelClassName: wt, noPanClassName: ot, disableKeyboardA11y: rt, onViewportChange: We, isControlledViewport: !!Te, children: a.jsxs(Uy, { children: [a.jsx(Yy, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: s, onReconnect: ge, onReconnectStart: Ae, onReconnectEnd: ye, onlyRenderVisibleElements: T, onEdgeContextMenu: ee, onEdgeMouseEnter: le, onEdgeMouseMove: H, onEdgeMouseLeave: U, reconnectRadius: pe, defaultMarkerColor: C, noPanClassName: ot, disableKeyboardA11y: rt, rfId: Ee }), a.jsx(nm, { style: v, type: y, component: w, containerStyle: m }), a.jsx("div", { className: "react-flow__edgelabel-renderer" }), a.jsx(jy, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: i, onNodeMouseEnter: c, onNodeMouseMove: d, onNodeMouseLeave: l, onNodeContextMenu: u, nodeClickDistance: G, onlyRenderVisibleElements: T, noPanClassName: ot, noDragClassName: Me, disableKeyboardA11y: rt, nodeExtent: $e, rfId: Ee }), a.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Cl.displayName = "GraphView";
const im = Se(Cl), sm = Ic(), ha = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: c, minZoom: d = 0.5, maxZoom: l = 2, nodeOrigin: u, nodeExtent: f, zIndexMode: h = "basic" } = {}) => {
  const p = /* @__PURE__ */ new Map(), y = /* @__PURE__ */ new Map(), v = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), m = o ?? t ?? [], b = n ?? e ?? [], g = u ?? [0, 0], x = f ?? Rn;
  Vc(v, w, m);
  const { nodesInitialized: I } = ui(b, p, y, {
    nodeOrigin: g,
    nodeExtent: x,
    zIndexMode: h
  });
  let E = [0, 0, 1];
  if (s && r && i) {
    const j = Un(p, {
      filter: (k) => !!((k.width || k.initialWidth) && (k.height || k.initialHeight))
    }), { x: P, y: T, zoom: A } = Mi(j, r, i, d, l, c?.padding ?? 0.1);
    E = [P, T, A];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: i ?? 0,
    transform: E,
    nodes: b,
    nodesInitialized: I,
    nodeLookup: p,
    parentLookup: y,
    edges: m,
    edgeLookup: w,
    connectionLookup: v,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: o !== void 0,
    panZoom: null,
    minZoom: d,
    maxZoom: l,
    translateExtent: Rn,
    nodeExtent: x,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: Jt.Strict,
    domNode: null,
    paneDragging: !1,
    noPanClassName: "nopan",
    nodeOrigin: g,
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
    connection: { ...vc },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: sm,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: wc,
    zIndexMode: h,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, am = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: c, minZoom: d, maxZoom: l, nodeOrigin: u, nodeExtent: f, zIndexMode: h }) => vg((p, y) => {
  async function v() {
    const { nodeLookup: w, panZoom: m, fitViewOptions: b, fitViewResolver: g, width: x, height: I, minZoom: E, maxZoom: j } = y();
    m && (await yp({
      nodes: w,
      width: x,
      height: I,
      panZoom: m,
      minZoom: E,
      maxZoom: j
    }, b), g?.resolve(!0), p({ fitViewResolver: null }));
  }
  return {
    ...ha({
      nodes: e,
      edges: t,
      width: r,
      height: i,
      fitView: s,
      fitViewOptions: c,
      minZoom: d,
      maxZoom: l,
      nodeOrigin: u,
      nodeExtent: f,
      defaultNodes: n,
      defaultEdges: o,
      zIndexMode: h
    }),
    setNodes: (w) => {
      const { nodeLookup: m, parentLookup: b, nodeOrigin: g, elevateNodesOnSelect: x, fitViewQueued: I, zIndexMode: E, nodesSelectionActive: j } = y(), { nodesInitialized: P, hasSelectedNodes: T } = ui(w, m, b, {
        nodeOrigin: g,
        nodeExtent: f,
        elevateNodesOnSelect: x,
        checkEquality: !0,
        zIndexMode: E
      }), A = j && T;
      I && P ? (v(), p({
        nodes: w,
        nodesInitialized: P,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: A
      })) : p({ nodes: w, nodesInitialized: P, nodesSelectionActive: A });
    },
    setEdges: (w) => {
      const { connectionLookup: m, edgeLookup: b } = y();
      Vc(m, b, w), p({ edges: w });
    },
    setDefaultNodesAndEdges: (w, m) => {
      if (w) {
        const { setNodes: b } = y();
        b(w), p({ hasDefaultNodes: !0 });
      }
      if (m) {
        const { setEdges: b } = y();
        b(m), p({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (w) => {
      const { triggerNodeChanges: m, nodeLookup: b, parentLookup: g, domNode: x, nodeOrigin: I, nodeExtent: E, debug: j, fitViewQueued: P, zIndexMode: T } = y(), { changes: A, updatedInternals: k } = Vp(w, b, g, x, I, E, T);
      k && (Tp(b, g, { nodeOrigin: I, nodeExtent: E, zIndexMode: T }), P ? (v(), p({ fitViewQueued: !1, fitViewOptions: void 0 })) : p({}), A?.length > 0 && (j && console.log("React Flow: trigger node changes", A), m?.(A)));
    },
    updateNodePositions: (w, m = !1) => {
      const b = [];
      let g = [];
      const { nodeLookup: x, triggerNodeChanges: I, connection: E, updateConnection: j, onNodesChangeMiddlewareMap: P } = y();
      for (const [T, A] of w) {
        const k = x.get(T), R = !!(k?.expandParent && k?.parentId && A?.position), O = {
          id: T,
          type: "position",
          position: R ? {
            x: Math.max(0, A.position.x),
            y: Math.max(0, A.position.y)
          } : A.position,
          dragging: m
        };
        if (k && E.inProgress && E.fromNode.id === k.id) {
          const S = $t(k, E.fromHandle, te.Left, !0);
          j({ ...E, from: S });
        }
        R && k.parentId && b.push({
          id: T,
          parentId: k.parentId,
          rect: {
            ...A.internals.positionAbsolute,
            width: A.measured.width ?? 0,
            height: A.measured.height ?? 0
          }
        }), g.push(O);
      }
      if (b.length > 0) {
        const { parentLookup: T, nodeOrigin: A } = y(), k = Ri(b, x, T, A);
        g.push(...k);
      }
      for (const T of P.values())
        g = T(g);
      I(g);
    },
    triggerNodeChanges: (w) => {
      const { onNodesChange: m, setNodes: b, nodes: g, hasDefaultNodes: x, debug: I } = y();
      if (w?.length) {
        if (x) {
          const E = el(w, g);
          b(E);
        }
        I && console.log("React Flow: trigger node changes", w), m?.(w);
      }
    },
    triggerEdgeChanges: (w) => {
      const { onEdgesChange: m, setEdges: b, edges: g, hasDefaultEdges: x, debug: I } = y();
      if (w?.length) {
        if (x) {
          const E = tl(w, g);
          b(E);
        }
        I && console.log("React Flow: trigger edge changes", w), m?.(w);
      }
    },
    addSelectedNodes: (w) => {
      const { multiSelectionActive: m, edgeLookup: b, nodeLookup: g, triggerNodeChanges: x, triggerEdgeChanges: I } = y();
      if (m) {
        const E = w.map((j) => kt(j, !0));
        x(E);
        return;
      }
      x(Yt(g, /* @__PURE__ */ new Set([...w]), !0)), I(Yt(b));
    },
    addSelectedEdges: (w) => {
      const { multiSelectionActive: m, edgeLookup: b, nodeLookup: g, triggerNodeChanges: x, triggerEdgeChanges: I } = y();
      if (m) {
        const E = w.map((j) => kt(j, !0));
        I(E);
        return;
      }
      I(Yt(b, /* @__PURE__ */ new Set([...w]))), x(Yt(g, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: w, edges: m } = {}) => {
      const { edges: b, nodes: g, nodeLookup: x, triggerNodeChanges: I, triggerEdgeChanges: E } = y(), j = w || g, P = m || b, T = [];
      for (const k of j) {
        if (!k.selected)
          continue;
        const R = x.get(k.id);
        R && (R.selected = !1), T.push(kt(k.id, !1));
      }
      const A = [];
      for (const k of P)
        k.selected && A.push(kt(k.id, !1));
      I(T), E(A);
    },
    setMinZoom: (w) => {
      const { panZoom: m, maxZoom: b } = y();
      m?.setScaleExtent([w, b]), p({ minZoom: w });
    },
    setMaxZoom: (w) => {
      const { panZoom: m, minZoom: b } = y();
      m?.setScaleExtent([b, w]), p({ maxZoom: w });
    },
    setTranslateExtent: (w) => {
      y().panZoom?.setTranslateExtent(w), p({ translateExtent: w });
    },
    resetSelectedElements: () => {
      const { edges: w, nodes: m, triggerNodeChanges: b, triggerEdgeChanges: g, elementsSelectable: x } = y();
      if (!x)
        return;
      const I = m.reduce((j, P) => P.selected ? [...j, kt(P.id, !1)] : j, []), E = w.reduce((j, P) => P.selected ? [...j, kt(P.id, !1)] : j, []);
      b(I), g(E);
    },
    setNodeExtent: (w) => {
      const { nodes: m, nodeLookup: b, parentLookup: g, nodeOrigin: x, elevateNodesOnSelect: I, nodeExtent: E, zIndexMode: j } = y();
      w[0][0] === E[0][0] && w[0][1] === E[0][1] && w[1][0] === E[1][0] && w[1][1] === E[1][1] || (ui(m, b, g, {
        nodeOrigin: x,
        nodeExtent: w,
        elevateNodesOnSelect: I,
        checkEquality: !1,
        zIndexMode: j
      }), p({ nodeExtent: w }));
    },
    panBy: (w) => {
      const { transform: m, width: b, height: g, panZoom: x, translateExtent: I } = y();
      return Hp({ delta: w, panZoom: x, transform: m, translateExtent: I, width: b, height: g });
    },
    setCenter: async (w, m, b) => {
      const { width: g, height: x, maxZoom: I, panZoom: E } = y();
      if (!E)
        return !1;
      const j = typeof b?.zoom < "u" ? b.zoom : I;
      return await E.setViewport({
        x: g / 2 - w * j,
        y: x / 2 - m * j,
        zoom: j
      }, { duration: b?.duration, ease: b?.ease, interpolate: b?.interpolate }), !0;
    },
    cancelConnection: () => {
      p({
        connection: { ...vc }
      });
    },
    updateConnection: (w) => {
      p({ connection: w });
    },
    reset: () => p({ ...ha() })
  };
}, Object.is);
function cm({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: i, initialMinZoom: s, initialMaxZoom: c, initialFitViewOptions: d, fitView: l, nodeOrigin: u, nodeExtent: f, zIndexMode: h, children: p }) {
  const [y] = Y(() => am({
    nodes: e,
    edges: t,
    defaultNodes: n,
    defaultEdges: o,
    width: r,
    height: i,
    fitView: l,
    minZoom: s,
    maxZoom: c,
    fitViewOptions: d,
    nodeOrigin: u,
    nodeExtent: f,
    zIndexMode: h
  }));
  return a.jsx(Eg, { value: y, children: a.jsx(Ug, { children: p }) });
}
function lm({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: i, height: s, fitView: c, fitViewOptions: d, minZoom: l, maxZoom: u, nodeOrigin: f, nodeExtent: h, zIndexMode: p }) {
  return Wn(lr) ? a.jsx(a.Fragment, { children: e }) : a.jsx(cm, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: i, initialHeight: s, fitView: c, initialFitViewOptions: d, initialMinZoom: l, initialMaxZoom: u, nodeOrigin: f, nodeExtent: h, zIndexMode: p, children: e });
}
const dm = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function um({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: i, edgeTypes: s, onNodeClick: c, onEdgeClick: d, onInit: l, onMove: u, onMoveStart: f, onMoveEnd: h, onConnect: p, onConnectStart: y, onConnectEnd: v, onClickConnectStart: w, onClickConnectEnd: m, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: x, onNodeContextMenu: I, onNodeDoubleClick: E, onNodeDragStart: j, onNodeDrag: P, onNodeDragStop: T, onNodesDelete: A, onEdgesDelete: k, onDelete: R, onSelectionChange: O, onSelectionDragStart: S, onSelectionDrag: _, onSelectionDragStop: C, onSelectionContextMenu: M, onSelectionStart: z, onSelectionEnd: $, onBeforeDelete: W, connectionMode: F, connectionLineType: B = pt.Bezier, connectionLineStyle: Z, connectionLineComponent: K, connectionLineContainerStyle: ne, deleteKeyCode: se = "Backspace", selectionKeyCode: Q = "Shift", selectionOnDrag: L = !1, selectionMode: q = Ln.Full, panActivationKeyCode: ae = "Space", multiSelectionKeyCode: ie = Hn() ? "Meta" : "Control", zoomActivationKeyCode: G = Hn() ? "Meta" : "Control", snapToGrid: ee, snapGrid: le, onlyRenderVisibleElements: H = !1, selectNodesOnDrag: U, nodesDraggable: pe, autoPanOnNodeFocus: ge, nodesConnectable: Ae, nodesFocusable: ye, nodeOrigin: Me = Jc, edgesFocusable: wt, edgesReconnectable: ot, elementsSelectable: rt = !0, defaultViewport: $e = Rg, minZoom: Ee = 0.5, maxZoom: Te = 2, translateExtent: We = Rn, preventScrolling: an = !0, nodeExtent: Lt, defaultMarkerColor: cn = "#b1b1b7", zoomOnScroll: Vt = !0, zoomOnPinch: dt = !0, panOnScroll: vt = !1, panOnScrollSpeed: bt = 0.5, panOnScrollMode: Qe = jt.Free, zoomOnDoubleClick: Jn = !0, panOnDrag: Le = !0, onPaneClick: Qn, onPaneMouseEnter: be, onPaneMouseMove: Xe, onPaneMouseLeave: ln, onPaneScroll: Ne, onPaneContextMenu: Nt, paneClickDistance: dn = 1, nodeClickDistance: _e = 0, children: St, onReconnect: ut, onReconnectStart: yr, onReconnectEnd: eo, onEdgeContextMenu: to, onEdgeDoubleClick: un, onEdgeMouseEnter: mr, onEdgeMouseMove: fn, onEdgeMouseLeave: Ht, reconnectRadius: Ve = 10, onNodesChange: hn, onEdgesChange: pn, noDragClassName: gn = "nodrag", noWheelClassName: Ot = "nowheel", noPanClassName: no = "nopan", fitView: oo, fitViewOptions: ro, connectOnClick: xr, attributionPosition: io, proOptions: so, defaultEdgeOptions: ao, elevateNodesOnSelect: wr = !0, elevateEdgesOnSelect: yn = !1, disableKeyboardA11y: co = !1, autoPanOnConnect: vr, autoPanOnNodeDrag: br, autoPanOnSelection: Nr = !0, autoPanSpeed: Sr, connectionRadius: mn, isValidConnection: Er, onError: kr, style: Ir, id: lo, nodeDragThreshold: Cr, connectionDragThreshold: jr, viewport: _r, onViewportChange: uo, width: fo, height: Ar, colorMode: Mr = "light", debug: Dr, onScroll: ho, ariaLabelConfig: Pr, zIndexMode: xn = "basic", ...po }, go) {
  const Ft = lo || "1", yo = Og(Mr), $r = he((mo) => {
    mo.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), ho?.(mo);
  }, [ho]);
  return a.jsx("div", { "data-testid": "rf__wrapper", ...po, onScroll: $r, style: { ...Ir, ...dm }, ref: go, className: Ie(["react-flow", r, yo]), id: lo, role: "application", children: a.jsxs(lm, { nodes: e, edges: t, width: fo, height: Ar, fitView: oo, fitViewOptions: ro, minZoom: Ee, maxZoom: Te, nodeOrigin: Me, nodeExtent: Lt, zIndexMode: xn, children: [a.jsx(Hg, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: p, onConnectStart: y, onConnectEnd: v, onClickConnectStart: w, onClickConnectEnd: m, nodesDraggable: pe, autoPanOnNodeFocus: ge, nodesConnectable: Ae, nodesFocusable: ye, edgesFocusable: wt, edgesReconnectable: ot, elementsSelectable: rt, elevateNodesOnSelect: wr, elevateEdgesOnSelect: yn, minZoom: Ee, maxZoom: Te, nodeExtent: Lt, onNodesChange: hn, onEdgesChange: pn, snapToGrid: ee, snapGrid: le, connectionMode: F, translateExtent: We, connectOnClick: xr, defaultEdgeOptions: ao, fitView: oo, fitViewOptions: ro, onNodesDelete: A, onEdgesDelete: k, onDelete: R, onNodeDragStart: j, onNodeDrag: P, onNodeDragStop: T, onSelectionDrag: _, onSelectionDragStart: S, onSelectionDragStop: C, onMove: u, onMoveStart: f, onMoveEnd: h, noPanClassName: no, nodeOrigin: Me, rfId: Ft, autoPanOnConnect: vr, autoPanOnNodeDrag: br, autoPanSpeed: Sr, onError: kr, connectionRadius: mn, isValidConnection: Er, selectNodesOnDrag: U, nodeDragThreshold: Cr, connectionDragThreshold: jr, onBeforeDelete: W, debug: Dr, ariaLabelConfig: Pr, zIndexMode: xn }), a.jsx(im, { onInit: l, onNodeClick: c, onEdgeClick: d, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: x, onNodeContextMenu: I, onNodeDoubleClick: E, nodeTypes: i, edgeTypes: s, connectionLineType: B, connectionLineStyle: Z, connectionLineComponent: K, connectionLineContainerStyle: ne, selectionKeyCode: Q, selectionOnDrag: L, selectionMode: q, deleteKeyCode: se, multiSelectionKeyCode: ie, panActivationKeyCode: ae, zoomActivationKeyCode: G, onlyRenderVisibleElements: H, defaultViewport: $e, translateExtent: We, minZoom: Ee, maxZoom: Te, preventScrolling: an, zoomOnScroll: Vt, zoomOnPinch: dt, zoomOnDoubleClick: Jn, panOnScroll: vt, panOnScrollSpeed: bt, panOnScrollMode: Qe, panOnDrag: Le, autoPanOnSelection: Nr, onPaneClick: Qn, onPaneMouseEnter: be, onPaneMouseMove: Xe, onPaneMouseLeave: ln, onPaneScroll: Ne, onPaneContextMenu: Nt, paneClickDistance: dn, nodeClickDistance: _e, onSelectionContextMenu: M, onSelectionStart: z, onSelectionEnd: $, onReconnect: ut, onReconnectStart: yr, onReconnectEnd: eo, onEdgeContextMenu: to, onEdgeDoubleClick: un, onEdgeMouseEnter: mr, onEdgeMouseMove: fn, onEdgeMouseLeave: Ht, reconnectRadius: Ve, defaultMarkerColor: cn, noDragClassName: gn, noWheelClassName: Ot, noPanClassName: no, rfId: Ft, disableKeyboardA11y: co, nodeExtent: Lt, viewport: _r, onViewportChange: uo }), a.jsx(zg, { onSelectionChange: O }), St, a.jsx(Mg, { proOptions: so, position: io }), a.jsx(Ag, { rfId: Ft, disableKeyboardA11y: co })] }) });
}
var jl = rl(um);
const fm = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function hm({ children: e }) {
  const t = ue(fm);
  return t ? Sg.createPortal(e, t) : null;
}
function pm({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return a.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ie(["react-flow__background-pattern", n, o]) });
}
function gm({ radius: e, className: t }) {
  return a.jsx("circle", { cx: e, cy: e, r: e, className: Ie(["react-flow__background-pattern", "dots", t]) });
}
var gt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(gt || (gt = {}));
const ym = {
  [gt.Dots]: 1,
  [gt.Lines]: 1,
  [gt.Cross]: 6
}, mm = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function _l({
  id: e,
  variant: t = gt.Dots,
  // only used for dots and cross
  gap: n = 20,
  // only used for lines and cross
  size: o,
  lineWidth: r = 1,
  offset: i = 0,
  color: s,
  bgColor: c,
  style: d,
  className: l,
  patternClassName: u
}) {
  const f = ce(null), { transform: h, patternId: p } = ue(mm, xe), y = o || ym[t], v = t === gt.Dots, w = t === gt.Cross, m = Array.isArray(n) ? n : [n, n], b = [m[0] * h[2] || 1, m[1] * h[2] || 1], g = y * h[2], x = Array.isArray(i) ? i : [i, i], I = w ? [g, g] : b, E = [
    x[0] * h[2] || 1 + I[0] / 2,
    x[1] * h[2] || 1 + I[1] / 2
  ], j = `${p}${e || ""}`;
  return a.jsxs("svg", { className: Ie(["react-flow__background", l]), style: {
    ...d,
    ...ur,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": s
  }, ref: f, "data-testid": "rf__background", children: [a.jsx("pattern", { id: j, x: h[0] % b[0], y: h[1] % b[1], width: b[0], height: b[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${E[0]},-${E[1]})`, children: v ? a.jsx(gm, { radius: g / 2, className: u }) : a.jsx(pm, { dimensions: I, lineWidth: r, variant: t, className: u }) }), a.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${j})` })] });
}
_l.displayName = "Background";
const Al = Se(_l);
function xm() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: a.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function wm() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: a.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function vm() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: a.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function bm() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: a.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function Nm() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: a.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function Co({ children: e, className: t, ...n }) {
  return a.jsx("button", { type: "button", className: Ie(["react-flow__controls-button", t]), ...n, children: e });
}
const Sm = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function Ml({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: i, onZoomOut: s, onFitView: c, onInteractiveChange: d, className: l, children: u, position: f = "bottom-left", orientation: h = "vertical", "aria-label": p }) {
  const y = we(), { isInteractive: v, minZoomReached: w, maxZoomReached: m, ariaLabelConfig: b } = ue(Sm, xe), { zoomIn: g, zoomOut: x, fitView: I } = Li(), E = () => {
    g(), i?.();
  }, j = () => {
    x(), s?.();
  }, P = () => {
    I(r), c?.();
  }, T = () => {
    y.setState({
      nodesDraggable: !v,
      nodesConnectable: !v,
      elementsSelectable: !v
    }), d?.(!v);
  }, A = h === "horizontal" ? "horizontal" : "vertical";
  return a.jsxs(dr, { className: Ie(["react-flow__controls", A, l]), position: f, style: e, "data-testid": "rf__controls", "aria-label": p ?? b["controls.ariaLabel"], children: [t && a.jsxs(a.Fragment, { children: [a.jsx(Co, { onClick: E, className: "react-flow__controls-zoomin", title: b["controls.zoomIn.ariaLabel"], "aria-label": b["controls.zoomIn.ariaLabel"], disabled: m, children: a.jsx(xm, {}) }), a.jsx(Co, { onClick: j, className: "react-flow__controls-zoomout", title: b["controls.zoomOut.ariaLabel"], "aria-label": b["controls.zoomOut.ariaLabel"], disabled: w, children: a.jsx(wm, {}) })] }), n && a.jsx(Co, { className: "react-flow__controls-fitview", onClick: P, title: b["controls.fitView.ariaLabel"], "aria-label": b["controls.fitView.ariaLabel"], children: a.jsx(vm, {}) }), o && a.jsx(Co, { className: "react-flow__controls-interactive", onClick: T, title: b["controls.interactive.ariaLabel"], "aria-label": b["controls.interactive.ariaLabel"], children: v ? a.jsx(Nm, {}) : a.jsx(bm, {}) }), u] });
}
Ml.displayName = "Controls";
const Dl = Se(Ml);
function Em({ id: e, x: t, y: n, width: o, height: r, style: i, color: s, strokeColor: c, strokeWidth: d, className: l, borderRadius: u, shapeRendering: f, selected: h, onClick: p }) {
  const { background: y, backgroundColor: v } = i || {}, w = s || y || v;
  return a.jsx("rect", { className: Ie(["react-flow__minimap-node", { selected: h }, l]), x: t, y: n, rx: u, ry: u, width: o, height: r, style: {
    fill: w,
    stroke: c,
    strokeWidth: d
  }, shapeRendering: f, onClick: p ? (m) => p(m, e) : void 0 });
}
const km = Se(Em), Im = (e) => e.nodes.map((t) => t.id), Gr = (e) => e instanceof Function ? e : () => e;
function Cm({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: i = km,
  onClick: s
}) {
  const c = ue(Im, xe), d = Gr(t), l = Gr(e), u = Gr(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return a.jsx(a.Fragment, { children: c.map((h) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    a.jsx(_m, { id: h, nodeColorFunc: d, nodeStrokeColorFunc: l, nodeClassNameFunc: u, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: i, onClick: s, shapeRendering: f }, h)
  )) });
}
function jm({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: i, shapeRendering: s, NodeComponent: c, onClick: d }) {
  const { node: l, x: u, y: f, width: h, height: p } = ue((y) => {
    const v = y.nodeLookup.get(e);
    if (!v)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const w = v.internals.userNode, { x: m, y: b } = v.internals.positionAbsolute, { width: g, height: x } = lt(w);
    return {
      node: w,
      x: m,
      y: b,
      width: g,
      height: x
    };
  }, xe);
  return !l || l.hidden || !Cc(l) ? null : a.jsx(c, { x: u, y: f, width: h, height: p, style: l.style, selected: !!l.selected, className: o(l), color: t(l), borderRadius: r, strokeColor: n(l), strokeWidth: i, shapeRendering: s, onClick: d, id: l.id });
}
const _m = Se(jm);
var Am = Se(Cm);
const Mm = 200, Dm = 150, Pm = (e) => !e.hidden, $m = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? kc(Un(e.nodeLookup, { filter: Pm }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, Tm = "react-flow__minimap-desc";
function Pl({
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
  bgColor: d,
  maskColor: l,
  maskStrokeColor: u,
  maskStrokeWidth: f,
  position: h = "bottom-right",
  onClick: p,
  onNodeClick: y,
  pannable: v = !1,
  zoomable: w = !1,
  ariaLabel: m,
  inversePan: b,
  zoomStep: g = 1,
  offsetScale: x = 5
}) {
  const I = we(), E = ce(null), { boundingRect: j, viewBB: P, rfId: T, panZoom: A, translateExtent: k, flowWidth: R, flowHeight: O, ariaLabelConfig: S } = ue($m, xe), _ = e?.width ?? Mm, C = e?.height ?? Dm, M = j.width / _, z = j.height / C, $ = Math.max(M, z), W = $ * _, F = $ * C, B = x * $, Z = j.x - (W - j.width) / 2 - B, K = j.y - (F - j.height) / 2 - B, ne = W + B * 2, se = F + B * 2, Q = `${Tm}-${T}`, L = ce(0), q = ce();
  L.current = $, oe(() => {
    if (E.current && A)
      return q.current = Zp({
        domNode: E.current,
        panZoom: A,
        getTransform: () => I.getState().transform,
        getViewScale: () => L.current
      }), () => {
        q.current?.destroy();
      };
  }, [A]), oe(() => {
    q.current?.update({
      translateExtent: k,
      width: R,
      height: O,
      inversePan: b,
      pannable: v,
      zoomStep: g,
      zoomable: w
    });
  }, [v, w, b, g, k, R, O]);
  const ae = p ? (ee) => {
    const [le, H] = q.current?.pointer(ee) || [0, 0];
    p(ee, { x: le, y: H });
  } : void 0, ie = y ? he((ee, le) => {
    const H = I.getState().nodeLookup.get(le).internals.userNode;
    y(ee, H);
  }, []) : void 0, G = m ?? S["minimap.ariaLabel"];
  return a.jsx(dr, { position: h, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * $ : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof s == "number" ? s : void 0
  }, className: Ie(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: a.jsxs("svg", { width: _, height: C, viewBox: `${Z} ${K} ${ne} ${se}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": Q, ref: E, onClick: ae, children: [G && a.jsx("title", { id: Q, children: G }), a.jsx(Am, { onClick: ie, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: i, nodeClassName: r, nodeStrokeWidth: s, nodeComponent: c }), a.jsx("path", { className: "react-flow__minimap-mask", d: `M${Z - B},${K - B}h${ne + B * 2}v${se + B * 2}h${-ne - B * 2}z
        M${P.x},${P.y}h${P.width}v${P.height}h${-P.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
Pl.displayName = "MiniMap";
const $l = Se(Pl), zm = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, Rm = {
  [nn.Line]: "right",
  [nn.Handle]: "bottom-right"
};
function Lm({ nodeId: e, position: t, variant: n = nn.Handle, className: o, style: r = void 0, children: i, color: s, minWidth: c = 10, minHeight: d = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: u = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: h, autoScale: p = !0, shouldResize: y, onResizeStart: v, onResize: w, onResizeEnd: m }) {
  const b = cl(), g = typeof e == "string" ? e : b, x = we(), I = ce(null), E = n === nn.Handle, j = ue(he(zm(E && p), [E, p]), xe), P = ce(null), T = t ?? Rm[n];
  oe(() => {
    if (!(!I.current || !g))
      return P.current || (P.current = cg({
        domNode: I.current,
        nodeId: g,
        getStoreItems: () => {
          const { nodeLookup: k, transform: R, snapGrid: O, snapToGrid: S, nodeOrigin: _, domNode: C } = x.getState();
          return {
            nodeLookup: k,
            transform: R,
            snapGrid: O,
            snapToGrid: S,
            nodeOrigin: _,
            paneDomNode: C
          };
        },
        onChange: (k, R) => {
          const { triggerNodeChanges: O, nodeLookup: S, parentLookup: _, nodeOrigin: C } = x.getState(), M = [], z = { x: k.x, y: k.y }, $ = S.get(g);
          if ($ && $.expandParent && $.parentId) {
            const W = $.origin ?? C, F = k.width ?? $.measured.width ?? 0, B = k.height ?? $.measured.height ?? 0, Z = {
              id: $.id,
              parentId: $.parentId,
              rect: {
                width: F,
                height: B,
                ...jc({
                  x: k.x ?? $.position.x,
                  y: k.y ?? $.position.y
                }, { width: F, height: B }, $.parentId, S, W)
              }
            }, K = Ri([Z], S, _, C);
            M.push(...K), z.x = k.x ? Math.max(W[0] * F, k.x) : void 0, z.y = k.y ? Math.max(W[1] * B, k.y) : void 0;
          }
          if (z.x !== void 0 && z.y !== void 0) {
            const W = {
              id: g,
              type: "position",
              position: { ...z }
            };
            M.push(W);
          }
          if (k.width !== void 0 && k.height !== void 0) {
            const F = {
              id: g,
              type: "dimensions",
              resizing: !0,
              setAttributes: h ? h === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: k.width,
                height: k.height
              }
            };
            M.push(F);
          }
          for (const W of R) {
            const F = {
              ...W,
              type: "position"
            };
            M.push(F);
          }
          O(M);
        },
        onEnd: ({ width: k, height: R }) => {
          const O = {
            id: g,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: k,
              height: R
            }
          };
          x.getState().triggerNodeChanges([O]);
        }
      })), P.current.update({
        controlPosition: T,
        boundaries: {
          minWidth: c,
          minHeight: d,
          maxWidth: l,
          maxHeight: u
        },
        keepAspectRatio: f,
        resizeDirection: h,
        onResizeStart: v,
        onResize: w,
        onResizeEnd: m,
        shouldResize: y
      }), () => {
        P.current?.destroy();
      };
  }, [
    T,
    c,
    d,
    l,
    u,
    f,
    v,
    w,
    m,
    y
  ]);
  const A = T.split("-");
  return a.jsx("div", { className: Ie(["react-flow__resize-control", "nodrag", ...A, n, o]), ref: I, style: {
    ...r,
    scale: j,
    ...s && { [E ? "backgroundColor" : "borderColor"]: s }
  }, children: i });
}
Se(Lm);
const Vm = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), Tl = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var Hm = {
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
const Om = er(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: o,
    className: r = "",
    children: i,
    iconNode: s,
    ...c
  }, d) => ei(
    "svg",
    {
      ref: d,
      ...Hm,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: Tl("lucide", r),
      ...c
    },
    [
      ...s.map(([l, u]) => ei(l, u)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const ve = (e, t) => {
  const n = er(
    ({ className: o, ...r }, i) => ei(Om, {
      ref: i,
      iconNode: t,
      className: Tl(`lucide-${Vm(e)}`, o),
      ...r
    })
  );
  return n.displayName = `${e}`, n;
};
const zl = ve("Boxes", [
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
const Rt = ve("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const Rl = ve("ChevronDown", [
  ["path", { d: "m6 9 6 6 6-6", key: "qrunsl" }]
]);
const Zo = ve("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const Wt = ve("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const Be = ve("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const Fm = ve("Copy", [
  ["rect", { width: "14", height: "14", x: "8", y: "8", rx: "2", ry: "2", key: "17jyea" }],
  ["path", { d: "M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2", key: "zix9uf" }]
]);
const Ll = ve("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const Bm = ve("GripVertical", [
  ["circle", { cx: "9", cy: "12", r: "1", key: "1vctgf" }],
  ["circle", { cx: "9", cy: "5", r: "1", key: "hp0tcf" }],
  ["circle", { cx: "9", cy: "19", r: "1", key: "fkjjf6" }],
  ["circle", { cx: "15", cy: "12", r: "1", key: "1tmaij" }],
  ["circle", { cx: "15", cy: "5", r: "1", key: "19l28e" }],
  ["circle", { cx: "15", cy: "19", r: "1", key: "f4zoj3" }]
]);
const Vl = ve("ListTree", [
  ["path", { d: "M21 12h-8", key: "1bmf0i" }],
  ["path", { d: "M21 6H8", key: "1pqkrb" }],
  ["path", { d: "M21 18h-8", key: "1tm79t" }],
  ["path", { d: "M3 6v4c0 1.1.9 2 2 2h3", key: "1ywdgy" }],
  ["path", { d: "M3 10v6c0 1.1.9 2 2 2h3", key: "2wc746" }]
]);
const Ko = ve("Maximize2", [
  ["polyline", { points: "15 3 21 3 21 9", key: "mznyad" }],
  ["polyline", { points: "9 21 3 21 3 15", key: "1avn1i" }],
  ["line", { x1: "21", x2: "14", y1: "3", y2: "10", key: "ota7mn" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const pa = ve("Minimize2", [
  ["polyline", { points: "4 14 10 14 10 20", key: "11kfnr" }],
  ["polyline", { points: "20 10 14 10 14 4", key: "rlmsce" }],
  ["line", { x1: "14", x2: "21", y1: "10", y2: "3", key: "o5lafz" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const Wm = ve("Package", [
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
const fr = ve("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const Hi = ve("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const Oi = ve("RefreshCcw", [
  ["path", { d: "M21 12a9 9 0 0 0-9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "14sxne" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }],
  ["path", { d: "M3 12a9 9 0 0 0 9 9 9.75 9.75 0 0 0 6.74-2.74L21 16", key: "1hlbsb" }],
  ["path", { d: "M16 16h5v5", key: "ccwih5" }]
]);
const Fi = ve("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const Xm = ve("Save", [
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
const Hl = ve("Search", [
  ["circle", { cx: "11", cy: "11", r: "8", key: "4ej97u" }],
  ["path", { d: "m21 21-4.3-4.3", key: "1qie3q" }]
]);
const yt = ve("Sparkles", [
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
const Ol = ve("Terminal", [
  ["polyline", { points: "4 17 10 11 4 5", key: "akl6gq" }],
  ["line", { x1: "12", x2: "20", y1: "19", y2: "19", key: "q2wloq" }]
]);
const Fn = ve("Trash2", [
  ["path", { d: "M3 6h18", key: "d0wm0j" }],
  ["path", { d: "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", key: "4alrt4" }],
  ["path", { d: "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", key: "v07s0e" }],
  ["line", { x1: "10", x2: "10", y1: "11", y2: "17", key: "1uufr5" }],
  ["line", { x1: "14", x2: "14", y1: "11", y2: "17", key: "xtxkd" }]
]);
const Fl = ve("X", [
  ["path", { d: "M18 6 6 18", key: "1bl5f8" }],
  ["path", { d: "m6 6 12 12", key: "d8bk6v" }]
]);
const Ym = ve("Zap", [
  [
    "path",
    {
      d: "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z",
      key: "1xq2db"
    }
  ]
]), je = "/_elsa/workflow-management", qm = "/publishing";
async function Um(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${je}/definitions?${n.toString()}`);
}
async function Zm(e, t) {
  return e.http.getJson(`${je}/definitions/${encodeURIComponent(t)}`);
}
async function Km(e, t) {
  return e.http.getJson(`${je}/versions/${encodeURIComponent(t)}`);
}
async function Gm(e, t) {
  return e.http.postJson(`${je}/definitions`, t);
}
async function Jm(e, t) {
  await e.http.deleteJson(`${je}/definitions/${encodeURIComponent(t)}`);
}
async function Qm(e, t) {
  await e.http.postJson(`${je}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function ex(e, t) {
  await e.http.deleteJson(`${je}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function tx(e, t) {
  return e.http.putJson(`${je}/drafts/${encodeURIComponent(t.id)}`, { state: t.state, layout: t.layout });
}
async function nx(e, t) {
  return e.http.postJson(`${je}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function ox(e, t) {
  return e.http.postJson(`${je}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function rx(e, t) {
  try {
    return await e.http.postJson(`${qm}/workflows/drafts/test-runs`, t);
  } catch (n) {
    const o = ux(n);
    if (o) return o;
    throw n;
  }
}
async function Bl(e, t) {
  return e.http.postJson(`${je}/executables/${encodeURIComponent(t)}/run`, {});
}
async function Wl(e, t = "active") {
  const n = await e.http.getJson(`${je}/executables?state=${encodeURIComponent(t)}`);
  return Array.isArray(n) ? n : n.executables;
}
async function ix(e, t) {
  await e.http.deleteJson(`${je}/executables/${encodeURIComponent(t)}`);
}
async function sx(e, t) {
  await e.http.postJson(`${je}/executables/${encodeURIComponent(t)}/restore`, {});
}
async function ax(e, t) {
  await e.http.deleteJson(`${je}/executables/${encodeURIComponent(t)}/permanent`);
}
async function Xl(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const o = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${o ? `?${o}` : ""}`);
}
async function cx(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function Bi(e) {
  return e.http.getJson(`${je}/activities`);
}
async function lx(e) {
  const t = await Yl(e, [
    `${je}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? ga(t) : ga(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function dx(e) {
  const t = await Yl(e, [
    `${je}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : To;
}
async function Yl(e, t) {
  let n;
  for (const o of t)
    try {
      return await e.http.getJson(o);
    } catch (r) {
      n = r;
    }
  throw n;
}
function ga(e) {
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
function ux(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = ya(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return ya(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function ya(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const To = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
], hr = "elsa.sequence.structure", Gn = "elsa.flowchart.structure";
function ql(e, t) {
  if (!e) return null;
  let n = e;
  for (const o of t) {
    const r = Ke(n).find((s) => s.id === o.slotId);
    if (!r) return null;
    const i = r.activities.find((s) => s.nodeId === o.ownerNodeId);
    if (!i) return null;
    n = i;
  }
  return n;
}
function Dn(e, t) {
  const n = ql(e, t);
  if (!n) return null;
  let o = Ke(n)[0];
  return o ? { owner: n, slot: o } : null;
}
function Ke(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = Mx(t), r = Jr(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: Dx(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, i]) => Jr(i)).map(([i, s]) => ({
    id: `${t.kind}:${i}`,
    label: $x(i),
    property: i,
    mode: "generic",
    activities: Jr(s) ?? []
  }));
}
function Ul(e, t, n) {
  const o = new Map(t.map((s) => [s.activityVersionId, s])), r = new Map(n.map((s) => [s.nodeId, s])), i = e.slot.activities.map((s, c) => {
    const d = o.get(s.activityVersionId), l = r.get(s.nodeId) ?? Px(e.slot.mode, c);
    return Gl(s, d, { x: l.x, y: l.y });
  });
  return {
    nodes: i,
    edges: e.slot.mode === "flowchart" ? Sx(e.owner) : Nx(e.slot, i)
  };
}
function pi(e, t, n) {
  const o = t.find((i) => i.activityVersionId === e.activityVersionId), r = n.find((i) => i.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [Gl(e, o, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function fx(e, t, n, o = null) {
  const r = new Map(t.map((c) => [c.activityExecutionId, c])), i = xa(t, (c) => c.authoredActivityId || c.executableNodeId), s = xa(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? r.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const d = i.get(c.id) ?? [], l = s.get(c.id) ?? [];
    if (d.length === 0 && l.length === 0) return c;
    const u = jx(d), f = o === c.id || d.some((p) => p.activityExecutionId === o) || l.some((p) => p.incidentId === o), h = {
      status: u?.status,
      subStatus: u?.subStatus,
      activityExecutionId: u?.activityExecutionId,
      faultCount: d.reduce((p, y) => p + y.faultCount + y.aggregateFaultCount, 0),
      incidentCount: l.length,
      hasBlockingIncident: l.some((p) => p.isBlocking),
      selected: f
    };
    return {
      ...c,
      selected: f,
      className: f ? "wf-runtime-node-selected" : c.className,
      data: {
        ...c.data,
        runtime: h
      }
    };
  });
}
function Wi(e, t) {
  return e?.structure?.kind === Gn || mx(t) ? "flowchart" : e?.structure?.kind === hr || xx(t) ? "sequence" : "unsupported";
}
function gi(e, t, n) {
  if (t.length === 0) {
    const c = Ke(e)[0];
    return c ? Bn(e, c, n) : e;
  }
  const [o, ...r] = t, i = Ke(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((c) => c.nodeId === o.ownerNodeId ? gi(c, r, n) : c);
  return Bn(e, i, s);
}
function Zl(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, i = Ke(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((c) => c.nodeId === o.ownerNodeId ? Zl(c, r, n) : c);
  return Bn(e, i, s);
}
function Kl(e, t, n) {
  if (e.nodeId === t) return n(e);
  const o = Ke(e);
  if (o.length === 0) return e;
  let r = !1, i = e;
  for (const s of o) {
    const c = s.activities.map((d) => {
      const l = Kl(d, t, n);
      return l !== d && (r = !0), l;
    });
    r && (i = Bn(i, s, c));
  }
  return r ? i : e;
}
function Bn(e, t, n) {
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
function hx(e, t, n, o = []) {
  const r = new Map(e.slot.activities.map((s) => [s.nodeId, s]));
  for (const s of o)
    r.set(s.nodeId, s);
  const i = t.map((s) => r.get(s.id)).filter((s) => !!s);
  return e.slot.mode === "sequence" && i.sort((s, c) => {
    const d = t.find((u) => u.id === s.nodeId), l = t.find((u) => u.id === c.nodeId);
    return (d?.position.x ?? 0) - (l?.position.x ?? 0);
  }), Bn(e.owner, e.slot, i);
}
function px(e, t) {
  return {
    ...e,
    structure: bx(e.structure, t)
  };
}
function gx(e, t) {
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
function yi(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: vx(e)
  };
}
function Ce(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? wx(t) : n;
}
function Gl(e, t, n, o = {}) {
  return {
    id: e.nodeId,
    type: "workflowActivity",
    position: n,
    connectable: o.connectable,
    deletable: o.deletable,
    draggable: o.draggable,
    data: {
      label: t ? Ce(t) : e.activityVersionId,
      activityVersionId: e.activityVersionId,
      activityTypeKey: t?.activityTypeKey,
      category: t?.category,
      executionType: t?.executionType,
      icon: mi(t),
      childSlots: Ke(e),
      acceptsInbound: Ex(e, t),
      sourcePorts: o.suppressFlowPorts ? [] : Jl(e, t),
      suppressFlowPorts: o.suppressFlowPorts
    }
  };
}
function mi(e) {
  if (!e) return "activity";
  const t = yx(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), o = Ce(e).toLowerCase(), r = e.category?.toLowerCase() ?? "", i = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || o === "flowchart" ? "flowchart" : n.endsWith(".sequence") || o === "sequence" ? "sequence" : n.includes("writeline") || o.includes("write line") ? "terminal" : r.includes("runtime") ? "runtime" : i === "trigger" ? "trigger" : "activity";
}
function yx(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function mx(e) {
  return !!e && (Ce(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function xx(e) {
  return !!e && (Ce(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function wx(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function vx(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: hr,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: Gn,
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
function bx(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], o = /* @__PURE__ */ new Map();
  for (const r of n) {
    if (!Xi(r)) continue;
    const i = r.id;
    typeof i == "string" && o.set(i, r);
  }
  return {
    ...e,
    payload: {
      ...e.payload,
      connections: t.map((r) => {
        const i = o.get(r.id) ?? {}, s = r.data?.vertices, { vertices: c, ...d } = i;
        return {
          ...d,
          id: r.id,
          source: { nodeId: r.source, port: r.sourceHandle ?? "Done" },
          target: r.targetHandle ? { nodeId: r.target, port: r.targetHandle } : { nodeId: r.target },
          ...s?.length ? { vertices: s.map((l) => ({ x: Math.round(l.x), y: Math.round(l.y) })) } : {}
        };
      })
    }
  };
}
function Nx(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function Sx(e) {
  if (e.structure?.kind !== Gn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, i = n.target;
    if (!r?.nodeId || !i?.nodeId) return null;
    const s = Array.isArray(n.vertices) ? n.vertices.filter(_x) : [];
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
function Jl(e, t) {
  const n = ma(e.cases);
  if (Ix(e, t) && n.length > 0)
    return [...n.map((i) => ({ name: i, displayName: i })), { name: "Default", displayName: "Default" }];
  const o = [
    ...zo(t?.designFacets),
    ...zo(t?.ports),
    ...zo(t?.outputs)
  ];
  if (o.length > 0) return Cx(o);
  const r = ma(e.outcomes);
  return r.length > 0 ? r.map((i) => ({ name: i, displayName: i })) : [{ name: "Done", displayName: "Done" }];
}
function Ex(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function Go(e, t, n, o) {
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
function kx(e, t, n) {
  const o = Go(t.source, n, t.sourceHandle ?? "Done", void 0), r = Go(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((i) => i.id !== t.id).concat(o, r);
}
function Jr(e) {
  return Array.isArray(e) ? e.filter(Ax) : null;
}
function Ix(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function zo(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!Xi(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...zo(n.ports));
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
function Cx(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function ma(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function xa(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const o of e) {
    const r = t(o);
    r && n.set(r, [...n.get(r) ?? [], o]);
  }
  return n;
}
function jx(e) {
  return [...e].sort((t, n) => wa(n).localeCompare(wa(t)))[0];
}
function wa(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function _x(e) {
  return Xi(e) && typeof e.x == "number" && typeof e.y == "number";
}
function Xi(e) {
  return typeof e == "object" && e !== null;
}
function Ax(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Mx(e) {
  return e.kind === hr ? "sequence" : e.kind === Gn ? "flowchart" : "generic";
}
function Dx(e) {
  return e.kind === hr || e.kind === Gn, "Activities";
}
function Px(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function $x(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Tx = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function Ql(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function Yi(e) {
  return Ql(e.name);
}
function zx(e, t) {
  const n = Yi(t), o = e[n];
  return t.isWrapped === !1 ? o ?? t.defaultValue ?? "" : td(o, t);
}
function ed(e, t) {
  return td(e[Yi(t)], t);
}
function Rx(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function Lx(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function va(e, t, n) {
  return {
    ...e,
    [Yi(t)]: n
  };
}
function Vx(e, t) {
  return t.isWrapped === !1 ? zx(e, t) : ed(e, t).expression.value;
}
function td(e, t) {
  return Hx(e) ? {
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
function Hx(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
const nd = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]);
function Ox({
  activity: e,
  descriptor: t,
  editors: n,
  expressionDescriptors: o,
  descriptorStatus: r,
  onChange: i
}) {
  if (r === "loading")
    return /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "Loading activity properties..." });
  if (!t)
    return /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "No activity descriptor is available for this activity." });
  const s = t.inputs.filter((l) => l.isBrowsable !== !1).sort((l, u) => (l.order ?? 0) - (u.order ?? 0) || l.name.localeCompare(u.name));
  if (s.length === 0)
    return /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "This activity does not expose editable properties." });
  const c = Xx(s), d = o.length > 0 ? o : Tx;
  return /* @__PURE__ */ a.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ a.jsx("span", { className: "wf-section-label", children: "Properties" }),
    c.map((l) => /* @__PURE__ */ a.jsxs("section", { className: "wf-property-group", children: [
      c.length > 1 ? /* @__PURE__ */ a.jsx("h4", { children: l.category }) : null,
      l.inputs.map((u) => /* @__PURE__ */ a.jsx(
        Fx,
        {
          activity: e,
          input: u,
          editors: n,
          expressionDescriptors: d,
          onChange: i
        },
        u.name
      ))
    ] }, l.category))
  ] });
}
function Fx({
  activity: e,
  input: t,
  editors: n,
  expressionDescriptors: o,
  onChange: r
}) {
  const i = t.isReadOnly === !0, s = { activity: e, expressionDescriptors: o, readOnly: i }, c = Wx(n, t, s), d = c?.component, l = t.isWrapped !== !1 ? ed(e, t) : null, u = l?.expression.type ?? "Literal", f = Vx(e, t), h = !!(l && Yx(t, c?.id)), p = !!(l && qx(t, c?.id)), [y, v] = Y(!1), w = (b) => {
    const g = l ? Rx(l, b) : b;
    r(va(e, t, g));
  }, m = (b) => {
    l && r(va(e, t, Lx(l, b)));
  };
  return /* @__PURE__ */ a.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ a.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ a.jsx("span", { children: od(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ a.jsx("p", { children: t.description }) : null,
    l && !h ? /* @__PURE__ */ a.jsx(
      xi,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: u,
        descriptors: o,
        disabled: i,
        onChange: m
      }
    ) : null,
    h ? /* @__PURE__ */ a.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ a.jsx("div", { className: "wf-expression-editor", children: ba(d, t, f, i, s, w) }),
      /* @__PURE__ */ a.jsx(
        xi,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: u,
          descriptors: o,
          disabled: i,
          variant: "inline",
          onChange: m
        }
      ),
      p ? /* @__PURE__ */ a.jsx(
        "button",
        {
          type: "button",
          className: "wf-expression-expand-button",
          "aria-label": `Open expanded ${t.displayName || t.name} editor`,
          title: "Open expanded editor",
          onClick: () => v(!0),
          children: /* @__PURE__ */ a.jsx(Ko, { size: 13 })
        }
      ) : null
    ] }) : ba(d, t, f, i, s, w),
    p && !h ? /* @__PURE__ */ a.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => v(!0),
        children: [
          /* @__PURE__ */ a.jsx(Ko, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    y ? /* @__PURE__ */ a.jsx(
      Bx,
      {
        input: t,
        value: f,
        syntax: u,
        descriptors: o,
        disabled: i,
        onChange: w,
        onSyntaxChange: m,
        onClose: () => v(!1)
      }
    ) : null
  ] });
}
function Bx({
  input: e,
  value: t,
  syntax: n,
  descriptors: o,
  disabled: r,
  onChange: i,
  onSyntaxChange: s,
  onClose: c
}) {
  const d = Oa(), l = e.displayName || e.name;
  return oe(() => {
    const u = (f) => {
      f.key === "Escape" && c();
    };
    return window.addEventListener("keydown", u), () => window.removeEventListener("keydown", u);
  }, [c]), /* @__PURE__ */ a.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ a.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": d, children: [
    /* @__PURE__ */ a.jsxs("header", { children: [
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ a.jsx("h3", { id: d, children: l })
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", "aria-label": `Close ${l} editor`, onClick: c, children: /* @__PURE__ */ a.jsx(Fl, { size: 16 }) })
    ] }),
    /* @__PURE__ */ a.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ a.jsx(
          xi,
          {
            label: `${l} expression syntax`,
            value: n,
            descriptors: o,
            disabled: r,
            onChange: s
          }
        ),
        /* @__PURE__ */ a.jsx("span", { children: od(e.typeName) })
      ] }),
      e.description ? /* @__PURE__ */ a.jsx("p", { children: e.description }) : null,
      /* @__PURE__ */ a.jsx(
        "textarea",
        {
          "aria-label": `${l} expanded value`,
          value: t == null ? "" : String(t),
          disabled: r,
          spellCheck: !1,
          onChange: (u) => i(u.target.value)
        }
      )
    ] }),
    /* @__PURE__ */ a.jsxs("footer", { children: [
      /* @__PURE__ */ a.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: c, children: "Close" })
    ] })
  ] }) });
}
function ba(e, t, n, o, r, i) {
  return e ? /* @__PURE__ */ a.jsx(
    e,
    {
      descriptor: t,
      value: n,
      disabled: o,
      context: r,
      onChange: i
    }
  ) : /* @__PURE__ */ a.jsx("input", { type: "text", value: n == null ? "" : String(n), disabled: o, onChange: (s) => i(s.target.value) });
}
function xi({
  label: e,
  value: t,
  descriptors: n,
  disabled: o,
  variant: r = "block",
  onChange: i
}) {
  const [s, c] = Y(!1), d = Oa(), l = n.find((f) => f.type === t), u = [
    "wf-syntax-picker-trigger",
    r === "inline" ? "inline" : "",
    s ? "open" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ a.jsxs("div", { className: r === "inline" ? "wf-syntax-picker inline" : "wf-syntax-picker", onBlur: (f) => {
    f.currentTarget.contains(f.relatedTarget) || c(!1);
  }, children: [
    /* @__PURE__ */ a.jsx(
      "button",
      {
        type: "button",
        className: u,
        "aria-label": e,
        "aria-haspopup": "listbox",
        "aria-expanded": s,
        "aria-controls": d,
        disabled: o,
        onClick: () => c((f) => !f),
        children: /* @__PURE__ */ a.jsx("span", { children: l?.displayName || l?.type || t })
      }
    ),
    s ? /* @__PURE__ */ a.jsx("div", { id: d, role: "listbox", className: "wf-syntax-picker-menu", "aria-label": e, children: n.map((f) => {
      const h = f.displayName || f.type, p = f.type === t;
      return /* @__PURE__ */ a.jsx(
        "button",
        {
          type: "button",
          role: "option",
          "aria-selected": p,
          className: p ? "selected" : "",
          onClick: () => {
            i(f.type), c(!1);
          },
          children: h
        },
        f.type
      );
    }) }) : null
  ] });
}
function Wx(e, t, n) {
  return [...e].sort((o, r) => (o.order ?? 500) - (r.order ?? 500)).find((o) => o.supports(t, n));
}
function Xx(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "General";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return [...t.entries()].map(([n, o]) => ({ category: n, inputs: o }));
}
function od(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function Yx(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !nd.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function qx(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !nd.has(t) && n !== "multiline") return !1;
  const o = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(o) || n === "singleline" || n === "multiline";
}
const Na = "elsa-studio:apply-workflow-graph-operation-batch", Sa = "elsa-studio:undo-workflow-graph-operation-batch", Ux = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function Zx(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const o = o0(e), r = id(o.state.rootActivity), i = /* @__PURE__ */ new Map(), s = /* @__PURE__ */ new Map(), c = [];
  for (const d of t.operations) {
    const l = n0(d.kind), u = d.parameters ?? {};
    if (l === "add-activity") {
      const f = ze(u.activityId) ?? d.temporaryReferences?.[0], h = t0(f ?? ze(u.displayName) ?? ze(u.activityType) ?? "weaver-activity", r), p = Kx(d, h, n);
      s.set(h, p), c.push(h), f && i.set(f, h), o.state.rootActivity && Gx(o.state.rootActivity, p);
      const y = _t(u.position) ? wi(u.position, { x: 280, y: 160 }) : null;
      y && (o.layout = Ea(o.layout, h, y));
      continue;
    }
    if (l === "set-root") {
      const f = Qr(o, u.activityId, i, s);
      if (!f) throw new Error("Weaver batch referenced an unknown root activity.");
      o.state.rootActivity = f;
      continue;
    }
    if (l === "set-designer-position") {
      const f = Tt(u.activityId, i);
      if (!f || !qi(o.state.rootActivity, f)) throw new Error("Weaver batch referenced an unknown activity position.");
      o.layout = Ea(o.layout, f, wi(u, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const f = Qr(o, u.activityId, i, s);
      if (!f) throw new Error("Weaver batch referenced an unknown activity property target.");
      e0(f, ze(u.propertyName) ?? "Value", u.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const f = Qr(o, u.activityId, i, s);
      if (!f) throw new Error("Weaver batch referenced an unknown activity update target.");
      const h = _t(u.patch) ? u.patch : u;
      Object.assign(f, h);
      continue;
    }
    if (l === "remove-activity") {
      const f = Tt(u.activityId, i);
      if (!f) throw new Error("Weaver batch referenced an unknown activity remove target.");
      o.state.rootActivity = rd(o.state.rootActivity, f), o.layout = o.layout.filter((h) => h.nodeId !== f);
      continue;
    }
    if (l === "connect-activities") {
      Jx(o, u, i);
      continue;
    }
    if (l === "disconnect-activities") {
      Qx(o, u, i);
      continue;
    }
    throw new Error(`Weaver batch operation '${String(d.kind || "unknown")}' is not supported by this designer apply path.`);
  }
  if (!o.state.rootActivity) throw new Error("Weaver batch did not produce a root activity.");
  return o.sourceVersionId = null, {
    draft: o,
    appliedCount: t.operations.length,
    finalActivityIds: c,
    temporaryReferences: Object.fromEntries(i),
    summary: `Applied ${t.operations.length} workflow operation${t.operations.length === 1 ? "" : "s"} to the working draft.`
  };
}
function Kx(e, t, n) {
  const o = e.parameters ?? {}, r = ze(o.activityVersionId) ?? ze(o.activityType) ?? "Elsa.Workflows.Activity", i = n.find((s) => s.activityVersionId === r || s.activityTypeKey === r || s.displayName === ze(o.displayName));
  return i ? yi(i, t) : {
    nodeId: t,
    activityVersionId: i?.activityVersionId ?? r,
    inputs: [],
    outputs: [],
    ...ze(o.displayName) ? { displayName: ze(o.displayName) } : {},
    designer: { position: wi(o.position, { x: 280, y: 160 }) }
  };
}
function Gx(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = Ui(e);
  n && !n.some((o) => o.nodeId === t.nodeId) && n.push(t);
}
function Jx(e, t, n) {
  const o = e.state.rootActivity;
  if (!o?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const r = Tt(t.sourceActivityId ?? t.sourceId ?? t.from, n), i = Tt(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!r || !i) throw new Error("Weaver batch connection is missing source or target activity.");
  const s = o.structure.payload, c = Array.isArray(s.connections) ? s.connections : [], d = ze(t.connectionId) ?? `flow-${r}-${i}`;
  s.connections = [
    ...c.filter((l) => !_t(l) || l.id !== d),
    {
      id: d,
      source: { nodeId: r, port: ze(t.outcome) ?? ze(t.sourcePort) ?? "Done" },
      target: { nodeId: i }
    }
  ];
}
function Qx(e, t, n) {
  const o = e.state.rootActivity, r = o?.structure?.payload.connections;
  if (!Array.isArray(r)) return;
  const i = ze(t.connectionId), s = Tt(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = Tt(t.targetActivityId ?? t.targetId ?? t.to, n);
  o.structure.payload.connections = r.filter((d) => {
    if (!_t(d)) return !0;
    if (i && d.id === i) return !1;
    const l = _t(d.source) ? d.source.nodeId : void 0, u = _t(d.target) ? d.target.nodeId : void 0;
    return l !== s || u !== c;
  });
}
function e0(e, t, n) {
  e[Ql(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: "Literal", value: n }
  };
}
function Qr(e, t, n, o) {
  const r = Tt(t, n);
  return r ? qi(e.state.rootActivity, r) ?? o.get(r) ?? null : null;
}
function Tt(e, t) {
  const n = ze(e);
  return n ? t.get(n) ?? n : null;
}
function qi(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of sd(e)) {
    const o = qi(n, t);
    if (o) return o;
  }
  return null;
}
function rd(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = Ui(e);
  if (n) {
    const o = n.map((r) => rd(r, t)).filter((r) => !!r);
    n.splice(0, n.length, ...o);
  }
  return e;
}
function id(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of sd(e)) id(n, t);
  return t;
}
function sd(e) {
  return Ui(e) ?? [];
}
function Ui(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function Ea(e, t, n) {
  return [
    ...e.filter((o) => o.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function wi(e, t) {
  const n = _t(e) ? e : {}, o = Number(n.x), r = Number(n.y);
  return {
    x: Number.isFinite(o) ? Math.max(40, Math.round(o)) : t.x,
    y: Number.isFinite(r) ? Math.max(40, Math.round(r)) : t.y
  };
}
function t0(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let o = n, r = 2;
  for (; t.has(o); )
    o = `${n}-${r}`, r += 1;
  return t.add(o), o;
}
function n0(e) {
  return typeof e == "number" ? Ux[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function ze(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function o0(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function _t(e) {
  return typeof e == "object" && e !== null;
}
const ad = { workflowActivity: q0 }, cd = { workflow: Z0 }, ka = "application/x-elsa-activity-version-id", r0 = 6, i0 = 1200, s0 = [10, 25, 50], a0 = 10, Ia = "elsa-studio-workflow-palette-width", Ca = "elsa-studio-workflow-inspector-width", ja = "elsa-studio-workflow-palette-collapsed", _a = "elsa-studio-workflow-inspector-collapsed", ld = "elsa-studio-workflow-side-panel-maximized", Nn = 180, Sn = 460, c0 = 260, En = 260, kn = 560, l0 = 320, Aa = 42, jo = 16, dd = xt.createContext(null);
function cw(e) {
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
        component: () => /* @__PURE__ */ a.jsx(d0, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), workflowDesignerPanels: e.workflowDesigner.panels.list() })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ a.jsx(u0, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow instances",
        component: () => /* @__PURE__ */ a.jsx(f0, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow instance",
        component: () => /* @__PURE__ */ a.jsx(h0, { context: e.backend, ai: e.ai })
      }
    ]
  });
}
function d0({
  context: e,
  ai: t,
  propertyEditors: n,
  workflowDesignerPanels: o
}) {
  const [r, i] = Y(Ma);
  oe(() => {
    const c = () => i(Ma());
    return window.addEventListener("popstate", c), () => window.removeEventListener("popstate", c);
  }, []);
  const s = (c) => {
    const d = c ? `/workflows/definitions?definition=${encodeURIComponent(c)}` : "/workflows/definitions";
    window.history.pushState({}, "", d), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return r ? /* @__PURE__ */ a.jsx(Y0, { context: e, definitionId: r, ai: t, propertyEditors: n, workflowDesignerPanels: o, onBack: () => s(null) }) : /* @__PURE__ */ a.jsx(pr, { title: "Definitions", children: /* @__PURE__ */ a.jsx(g0, { context: e, ai: t, onOpen: s }) });
}
function u0({ context: e, ai: t }) {
  const [n, o] = Y(Jo);
  oe(() => {
    const i = () => o(Jo());
    return window.addEventListener("popstate", i), () => window.removeEventListener("popstate", i);
  }, []);
  const r = he((i) => {
    const s = i?.trim() ?? "", c = new URL(window.location.href);
    s ? c.searchParams.set("definition", s) : c.searchParams.delete("definition"), o(s || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ a.jsx(pr, { title: "Executables", children: /* @__PURE__ */ a.jsx(m0, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: r }) });
}
function f0({ context: e, ai: t }) {
  const [n, o] = Y(Jo);
  oe(() => {
    const i = () => o(Jo());
    return window.addEventListener("popstate", i), () => window.removeEventListener("popstate", i);
  }, []);
  const r = he((i) => {
    const s = i?.trim() ?? "", c = new URL(window.location.href);
    s ? c.searchParams.set("definition", s) : c.searchParams.delete("definition"), o(s || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ a.jsx(pr, { title: "Instances", children: /* @__PURE__ */ a.jsx(S0, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: r }) });
}
function h0({ context: e, ai: t }) {
  const n = p0();
  return /* @__PURE__ */ a.jsx(pr, { title: "Instance", children: /* @__PURE__ */ a.jsx(E0, { context: e, ai: t, workflowExecutionId: n }) });
}
function pr({ title: e, children: t }) {
  return /* @__PURE__ */ a.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ a.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ a.jsxs("div", { children: [
      /* @__PURE__ */ a.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ a.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function Ma() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Jo() {
  return new URLSearchParams(window.location.search).get("definition");
}
function p0() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function g0({ context: e, ai: t, onOpen: n }) {
  const [o, r] = Y(""), [i, s] = Y("active"), [c, d] = Y(1), [l, u] = Y(a0), [f, h] = Y("loading"), [p, y] = Y(""), [v, w] = Y(""), [m, b] = Y([]), [g, x] = Y(0), [I, E] = Y(() => /* @__PURE__ */ new Set()), [j, P] = Y(null), [T, A] = Y(!1), [k, R] = Y([]), [O, S] = Y("idle"), _ = ce(null), C = me(() => m.map((H) => H.id), [m]), M = zt(t, "weaver.workflows.suggest-create-metadata"), z = zt(t, "weaver.workflows.explain-definition"), $ = C.filter((H) => I.has(H)).length, W = C.length > 0 && $ === C.length, F = he(async () => {
    h("loading"), y("");
    try {
      const H = await Um(e, { search: o, state: i, page: c, pageSize: l }), U = typeof H.totalCount == "number", pe = H.totalCount ?? H.definitions.length, ge = ud(pe, l);
      if (pe > 0 && c > ge) {
        d(ge);
        return;
      }
      b(U ? H.definitions : D0(H.definitions, c, l)), x(pe), h("ready");
    } catch (H) {
      y(H instanceof Error ? H.message : String(H)), h("failed");
    }
  }, [e, o, i, c, l]);
  oe(() => {
    F();
  }, [F]), oe(() => {
    _.current && (_.current.indeterminate = $ > 0 && !W);
  }, [W, $]);
  const B = he(async () => {
    if (!(O === "loading" || O === "ready")) {
      S("loading");
      try {
        const H = await Bi(e);
        R(H.activities ?? []), S("ready");
      } catch (H) {
        S("failed"), y(H instanceof Error ? H.message : String(H));
      }
    }
  }, [O, e]), Z = () => {
    y(""), w(""), P({ name: "", description: "", rootKind: "flowchart" }), B();
  }, K = async () => {
    if (j?.name.trim()) {
      A(!0), y(""), w("");
      try {
        const H = await Gm(e, {
          name: j.name.trim(),
          description: j.description.trim() || null,
          rootKind: j.rootKind,
          rootActivityVersionId: T0(j, k)
        });
        P(null), n(H.definition.id);
      } catch (H) {
        y(H instanceof Error ? H.message : String(H));
      } finally {
        A(!1);
      }
    }
  }, ne = (H) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(H)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, se = async () => {
    if (m.length === 1 && c > 1) {
      d(c - 1);
      return;
    }
    await F();
  }, Q = () => E(/* @__PURE__ */ new Set()), L = (H, U) => {
    E((pe) => {
      const ge = new Set(pe);
      return U ? ge.add(H) : ge.delete(H), ge;
    });
  }, q = (H) => {
    E((U) => {
      const pe = new Set(U);
      for (const ge of C)
        H ? pe.add(ge) : pe.delete(ge);
      return pe;
    });
  }, ae = (H) => {
    s(H), d(1), Q();
  }, ie = (H) => {
    r(H), d(1), Q();
  }, G = async (H) => {
    if (window.confirm(`Delete workflow definition "${H.name}"? You can restore it from the Deleted view.`)) {
      w(""), y("");
      try {
        await Jm(e, H.id), L(H.id, !1), w(`Deleted ${H.name}`), await se();
      } catch (U) {
        y(U instanceof Error ? U.message : String(U));
      }
    }
  }, ee = async (H) => {
    w(""), y("");
    try {
      await Qm(e, H.id), L(H.id, !1), w(`Restored ${H.name}`), await se();
    } catch (U) {
      y(U instanceof Error ? U.message : String(U));
    }
  }, le = async (H) => {
    if (window.confirm(`Permanently delete workflow definition "${H.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`)) {
      w(""), y("");
      try {
        await ex(e, H.id), L(H.id, !1), w(`Permanently deleted ${H.name}`), await se();
      } catch (U) {
        y(U instanceof Error ? U.message : String(U));
      }
    }
  };
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ a.jsx("button", { type: "button", className: i === "active" ? "active" : "", "aria-selected": i === "active", onClick: () => ae("active"), children: "Active" }),
        /* @__PURE__ */ a.jsx("button", { type: "button", className: i === "deleted" ? "active" : "", "aria-selected": i === "deleted", onClick: () => ae("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ a.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ a.jsx(Hl, { size: 15 }),
        /* @__PURE__ */ a.jsx("input", { value: o, onChange: (H) => ie(H.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
        F();
      }, children: "Refresh" }),
      /* @__PURE__ */ a.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ a.jsxs("button", { type: "button", title: "Create workflow", onClick: Z, children: [
        /* @__PURE__ */ a.jsx(Hi, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(Be, { size: 16 }),
      " ",
      p
    ] }) : null,
    f !== "failed" && p ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(Be, { size: 16 }),
      " ",
      p
    ] }) : null,
    v ? /* @__PURE__ */ a.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ a.jsx(Rt, { size: 14 }),
      " ",
      v
    ] }) : null,
    I.size > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ a.jsxs("span", { children: [
        I.size,
        " selected"
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: Q, children: "Clear selection" })
    ] }) : null,
    f === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading workflow definitions..." }) : null,
    f === "ready" && m.length === 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-empty", children: [
      "No ",
      i,
      " workflow definitions found."
    ] }) : null,
    f === "ready" && m.length > 0 ? /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ a.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ a.jsx(
            "input",
            {
              ref: _,
              type: "checkbox",
              checked: W,
              onChange: (H) => q(H.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ a.jsx("span", { children: "Name" }),
          /* @__PURE__ */ a.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ a.jsx("span", { children: i === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ a.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ a.jsx("span", { children: "Actions" })
        ] }),
        m.map((H) => /* @__PURE__ */ a.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${H.name}`,
            "aria-selected": I.has(H.id),
            tabIndex: 0,
            onClick: () => n(H.id),
            onKeyDown: (U) => {
              U.currentTarget === U.target && (U.key !== "Enter" && U.key !== " " || (U.preventDefault(), n(H.id)));
            },
            children: [
              /* @__PURE__ */ a.jsx("label", { className: "wf-row-select", onClick: (U) => U.stopPropagation(), children: /* @__PURE__ */ a.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: I.has(H.id),
                  onChange: (U) => L(H.id, U.target.checked),
                  "aria-label": `Select workflow definition ${H.name}`
                }
              ) }),
              /* @__PURE__ */ a.jsxs("span", { children: [
                /* @__PURE__ */ a.jsx("strong", { children: H.name }),
                /* @__PURE__ */ a.jsx("small", { children: H.description || H.id })
              ] }),
              /* @__PURE__ */ a.jsx("span", { children: H.latestVersion ?? "No version" }),
              /* @__PURE__ */ a.jsx("span", { children: i === "deleted" ? Ge(H.deletedAt) : H.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ a.jsx("span", { children: Ge(H.lastModifiedAt) }),
              /* @__PURE__ */ a.jsx("span", { className: "wf-row-actions", onClick: (U) => U.stopPropagation(), children: i === "active" ? /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
                /* @__PURE__ */ a.jsx("button", { type: "button", onClick: (U) => {
                  U.stopPropagation(), n(H.id);
                }, children: "Open" }),
                /* @__PURE__ */ a.jsx("button", { type: "button", onClick: (U) => {
                  U.stopPropagation(), ne(H.id);
                }, children: "Artifacts" }),
                z ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => mt(t, z, H), children: [
                  /* @__PURE__ */ a.jsx(yt, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ a.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  G(H);
                }, children: [
                  /* @__PURE__ */ a.jsx(Fn, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
                /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
                  ee(H);
                }, children: [
                  /* @__PURE__ */ a.jsx(Fi, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ a.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  le(H);
                }, children: [
                  /* @__PURE__ */ a.jsx(Fn, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          H.id
        ))
      ] }),
      /* @__PURE__ */ a.jsx(
        M0,
        {
          page: c,
          pageSize: l,
          totalCount: g,
          onPageChange: d,
          onPageSizeChange: (H) => {
            u(H), d(1);
          }
        }
      )
    ] }) : null,
    j ? /* @__PURE__ */ a.jsx(
      y0,
      {
        draft: j,
        activities: k,
        catalogState: O,
        creating: T,
        suggestMetadataAction: M,
        onSuggestMetadata: M ? () => mt(t, M, { draft: j, activities: k }) : void 0,
        onChange: (H) => P(H),
        onClose: () => P(null),
        onSubmit: K
      }
    ) : null
  ] });
}
function y0({ draft: e, activities: t, catalogState: n, creating: o, suggestMetadataAction: r, onSuggestMetadata: i, onChange: s, onClose: c, onSubmit: d }) {
  const l = me(() => P0(t), [t]), u = $0(e, t), f = (h) => {
    if (h.startsWith("kind:")) {
      s({ ...e, rootKind: h.slice(5), rootActivityVersionId: null });
      return;
    }
    const p = t.find((y) => y.activityVersionId === h);
    s({
      ...e,
      rootKind: fd(p) ?? e.rootKind,
      rootActivityVersionId: h
    });
  };
  return /* @__PURE__ */ a.jsx("div", { className: "wf-dialog-backdrop", role: "presentation", children: /* @__PURE__ */ a.jsx("section", { className: "wf-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "workflow-create-title", children: /* @__PURE__ */ a.jsxs(
    "form",
    {
      onSubmit: (h) => {
        h.preventDefault(), d();
      },
      children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-dialog-heading", children: [
          /* @__PURE__ */ a.jsx("h3", { id: "workflow-create-title", children: "Create Workflow" }),
          r ? /* @__PURE__ */ a.jsxs("button", { type: "button", className: "wf-ai-action", onClick: i, title: r.description ?? r.label, children: [
            /* @__PURE__ */ a.jsx(yt, { size: 13 }),
            " ",
            r.label
          ] }) : null
        ] }),
        /* @__PURE__ */ a.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ a.jsx("span", { children: "Display name" }),
          /* @__PURE__ */ a.jsx(
            "input",
            {
              autoFocus: !0,
              "aria-label": "Display name",
              value: e.name,
              onChange: (h) => s({ ...e, name: h.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ a.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ a.jsx("span", { children: "Description" }),
          /* @__PURE__ */ a.jsx(
            "textarea",
            {
              "aria-label": "Description",
              rows: 3,
              value: e.description,
              onChange: (h) => s({ ...e, description: h.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ a.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ a.jsx("span", { children: "Root activity" }),
          /* @__PURE__ */ a.jsxs(
            "select",
            {
              "aria-label": "Root activity",
              value: u,
              onChange: (h) => f(h.target.value),
              disabled: n === "loading",
              children: [
                /* @__PURE__ */ a.jsx("optgroup", { label: "Composite roots", children: l.compositeRoots.map((h) => /* @__PURE__ */ a.jsx("option", { value: h.value, children: h.label }, h.value)) }),
                l.otherCategories.map((h) => /* @__PURE__ */ a.jsx("optgroup", { label: h.name, children: h.activities.map((p) => /* @__PURE__ */ a.jsx("option", { value: p.activityVersionId, children: Ce(p) }, p.activityVersionId)) }, h.name))
              ]
            }
          )
        ] }),
        n === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-dialog-note", children: "Loading activity catalog..." }) : null,
        n === "failed" ? /* @__PURE__ */ a.jsx("div", { className: "wf-dialog-note", children: "Activity catalog could not be loaded. Composite roots remain available." }) : null,
        /* @__PURE__ */ a.jsxs("div", { className: "wf-dialog-actions", children: [
          /* @__PURE__ */ a.jsx("button", { type: "button", onClick: c, disabled: o, children: "Cancel" }),
          /* @__PURE__ */ a.jsx("button", { type: "submit", disabled: o || !e.name.trim(), children: o ? "Creating..." : "Create" })
        ] })
      ]
    }
  ) }) });
}
function m0({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: o }) {
  const [r, i] = Y("active"), [s, c] = Y("loading"), [d, l] = Y(""), [u, f] = Y(""), [h, p] = Y([]), y = n?.trim().toLowerCase() ?? "", v = me(
    () => y ? h.filter((A) => R0(A, y)) : h,
    [y, h]
  ), w = me(
    () => Array.from(new Set(h.flatMap((A) => [
      A.definitionId,
      A.definitionVersionId,
      A.sourceId
    ]).filter((A) => !!A))).sort((A, k) => A.localeCompare(k)),
    [h]
  ), m = zt(t, "weaver.workflows.explain-executable"), b = he(async () => {
    c("loading"), l("");
    try {
      p(await Wl(e, r)), c("ready");
    } catch (A) {
      l(A instanceof Error ? A.message : String(A)), c("failed");
    }
  }, [e, r]);
  oe(() => {
    b();
  }, [b]);
  const g = async (A) => {
    f(""), l("");
    try {
      await Bl(e, A.artifactId), f(`Started ${A.artifactId}`);
    } catch (k) {
      l(k instanceof Error ? k.message : String(k));
    }
  }, x = async (A) => {
    if (window.confirm(`Delete executable artifact "${A.artifactId}"? You can restore it from the Deleted view.`)) {
      f(""), l("");
      try {
        await ix(e, A.artifactId), f(`Deleted ${A.artifactId}`), await b();
      } catch (k) {
        l(k instanceof Error ? k.message : String(k));
      }
    }
  }, I = async (A) => {
    f(""), l("");
    try {
      await sx(e, A.artifactId), f(`Restored ${A.artifactId}`), await b();
    } catch (k) {
      l(k instanceof Error ? k.message : String(k));
    }
  }, E = async (A) => {
    if (window.confirm(`Permanently delete executable artifact "${A.artifactId}"? This cannot be undone.`)) {
      f(""), l("");
      try {
        await ax(e, A.artifactId), f(`Permanently deleted ${A.artifactId}`), await b();
      } catch (k) {
        l(k instanceof Error ? k.message : String(k));
      }
    }
  }, j = (A) => {
    m && mt(t, m, A) && (l(""), f(`Sent ${A.artifactId} to Weaver`));
  }, P = (A) => {
    l(""), f(`Copied ${A}`);
  }, T = (A) => {
    f(""), l(`Could not copy ${A}.`);
  };
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Executable state", children: [
        /* @__PURE__ */ a.jsx("button", { type: "button", className: r === "active" ? "active" : "", "aria-selected": r === "active", onClick: () => {
          i("active"), f("");
        }, children: "Active" }),
        /* @__PURE__ */ a.jsx("button", { type: "button", className: r === "deleted" ? "active" : "", "aria-selected": r === "deleted", onClick: () => {
          i("deleted"), f("");
        }, children: "Deleted" })
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", className: "wf-icon-button", "aria-label": "Refresh workflow executables", title: "Refresh", onClick: () => {
        b();
      }, children: /* @__PURE__ */ a.jsx(Oi, { size: 15 }) }),
      /* @__PURE__ */ a.jsxs("label", { className: "wf-search wf-executable-definition-filter", children: [
        /* @__PURE__ */ a.jsx(Hl, { size: 14 }),
        /* @__PURE__ */ a.jsx(
          "input",
          {
            "aria-label": "Filter executables by workflow definition",
            list: "wf-executable-definition-options",
            placeholder: "Filter by definition ID",
            value: n ?? "",
            onChange: (A) => o(A.currentTarget.value || null)
          }
        )
      ] }),
      /* @__PURE__ */ a.jsx("datalist", { id: "wf-executable-definition-options", children: w.map((A) => /* @__PURE__ */ a.jsx("option", { value: A }, A)) }),
      n ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => o(null), children: [
        /* @__PURE__ */ a.jsx(Fl, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    s === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(Be, { size: 16 }),
      " ",
      d
    ] }) : null,
    s !== "failed" && d ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(Be, { size: 16 }),
      " ",
      d
    ] }) : null,
    u ? /* @__PURE__ */ a.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ a.jsx(Rt, { size: 14 }),
      " ",
      u
    ] }) : null,
    s === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading workflow executables..." }) : null,
    s === "ready" && v.length === 0 ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: L0(r, !!n) }) : null,
    s === "ready" && v.length > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ a.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ a.jsx("span", { children: "Version" }),
        /* @__PURE__ */ a.jsx("span", { children: "Source" }),
        /* @__PURE__ */ a.jsx("span", { children: "Root" }),
        /* @__PURE__ */ a.jsx("span", { children: r === "deleted" ? "Deleted" : "Published" }),
        /* @__PURE__ */ a.jsx("span", { children: "Actions" })
      ] }),
      v.map((A) => /* @__PURE__ */ a.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ a.jsxs("span", { className: "wf-artifact-cell", children: [
          /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line", children: [
            /* @__PURE__ */ a.jsx("strong", { title: A.artifactId, children: A.artifactId }),
            /* @__PURE__ */ a.jsx(Zt, { value: A.artifactId, ariaLabel: `Copy artifact ID ${A.artifactId}`, copiedLabel: "artifact ID", onCopied: P, onCopyFailed: T })
          ] }),
          /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
            /* @__PURE__ */ a.jsx("small", { title: A.artifactHash, children: A.artifactHash }),
            /* @__PURE__ */ a.jsx(Zt, { value: A.artifactHash, ariaLabel: `Copy artifact hash ${A.artifactHash}`, copiedLabel: "artifact hash", onCopied: P, onCopyFailed: T })
          ] })
        ] }),
        /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line wf-version-cell", children: [
          /* @__PURE__ */ a.jsx("span", { children: A.artifactVersion }),
          /* @__PURE__ */ a.jsx(Zt, { value: A.artifactVersion, ariaLabel: `Copy artifact version ${A.artifactVersion}`, copiedLabel: "artifact version", onCopied: P, onCopyFailed: T })
        ] }),
        /* @__PURE__ */ a.jsx(x0, { executable: A, onCopied: P, onCopyFailed: T }),
        /* @__PURE__ */ a.jsx("span", { children: md(A) }),
        /* @__PURE__ */ a.jsx("span", { children: Ge(r === "deleted" ? A.deletedAt : A.publishedAt ?? A.createdAt) }),
        /* @__PURE__ */ a.jsx("span", { className: "wf-row-actions", children: r === "active" ? /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
          /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
            g(A);
          }, children: [
            /* @__PURE__ */ a.jsx(fr, { size: 13 }),
            " Run"
          ] }),
          m ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => j(A), children: [
            /* @__PURE__ */ a.jsx(yt, { size: 13 }),
            " Explain"
          ] }) : null,
          /* @__PURE__ */ a.jsxs("button", { type: "button", className: "danger", onClick: () => {
            x(A);
          }, children: [
            /* @__PURE__ */ a.jsx(Fn, { size: 13 }),
            " Delete"
          ] })
        ] }) : /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
          /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
            I(A);
          }, children: [
            /* @__PURE__ */ a.jsx(Fi, { size: 13 }),
            " Restore"
          ] }),
          /* @__PURE__ */ a.jsxs("button", { type: "button", className: "danger", onClick: () => {
            E(A);
          }, children: [
            /* @__PURE__ */ a.jsx(Fn, { size: 13 }),
            " Delete permanently"
          ] })
        ] }) })
      ] }, A.artifactId))
    ] }) : null
  ] });
}
function x0({ executable: e, onCopied: t, onCopyFailed: n }) {
  const o = e.sourceId || e.definitionVersionId || e.definitionId, r = e.sourceVersion;
  return /* @__PURE__ */ a.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ a.jsx("span", { className: "wf-source-kind", children: xd(e.sourceKind) }),
    o ? /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line", children: [
      /* @__PURE__ */ a.jsx("code", { title: o, children: o }),
      /* @__PURE__ */ a.jsx(Zt, { value: o, ariaLabel: `Copy source ID ${o}`, copiedLabel: "source ID", onCopied: t, onCopyFailed: n })
    ] }) : null,
    r ? /* @__PURE__ */ a.jsxs("small", { children: [
      "Version ",
      r
    ] }) : null
  ] });
}
function Zt({ value: e, ariaLabel: t, copiedLabel: n, onCopied: o, onCopyFailed: r }) {
  if (!e) return null;
  const i = async (s) => {
    s.preventDefault(), s.stopPropagation();
    try {
      await O0(e), o(n);
    } catch {
      r(n);
    }
  };
  return /* @__PURE__ */ a.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (s) => {
    i(s);
  }, children: /* @__PURE__ */ a.jsx(Fm, { size: 12 }) });
}
function w0({ context: e, ai: t, definitionId: n, publishedArtifactId: o }) {
  const [r, i] = Y("loading"), [s, c] = Y(""), [d, l] = Y(""), [u, f] = Y([]), h = zt(t, "weaver.workflows.explain-executable"), p = he(async () => {
    i("loading"), c("");
    try {
      const g = await Wl(e);
      f(g.filter((x) => V0(x, n)).sort(H0)), i("ready");
    } catch (g) {
      c(g instanceof Error ? g.message : String(g)), f([]), i("failed");
    }
  }, [e, n]);
  oe(() => {
    p();
  }, [p, o]);
  const y = async (g) => {
    l(""), c("");
    try {
      await Bl(e, g.artifactId), l(`Started ${g.artifactId}`);
    } catch (x) {
      c(x instanceof Error ? x.message : String(x));
    }
  }, v = (g) => {
    h && mt(t, h, g) && (c(""), l(`Sent ${g.artifactId} to Weaver`));
  }, w = () => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(n)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, m = (g) => {
    c(""), l(`Copied ${g}`);
  }, b = (g) => {
    l(""), c(`Could not copy ${g}.`);
  };
  return /* @__PURE__ */ a.jsxs("div", { className: "wf-artifacts-panel", children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-artifacts-toolbar", children: [
      /* @__PURE__ */ a.jsxs("span", { children: [
        u.length,
        " artifact",
        u.length === 1 ? "" : "s"
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", className: "wf-icon-button", "aria-label": "Refresh workflow artifacts", title: "Refresh", onClick: () => {
        p();
      }, children: /* @__PURE__ */ a.jsx(Oi, { size: 13 }) }),
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: w, children: "Open list" })
    ] }),
    r === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ a.jsx(Be, { size: 14 }),
      " ",
      s
    ] }) : null,
    d ? /* @__PURE__ */ a.jsxs("div", { className: "wf-status-line compact", children: [
      /* @__PURE__ */ a.jsx(Rt, { size: 13 }),
      " ",
      d
    ] }) : null,
    r === "loading" ? /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "Loading artifacts..." }) : null,
    r === "ready" && u.length === 0 ? /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "No published artifacts for this workflow yet." }) : null,
    r === "ready" && u.length > 0 ? /* @__PURE__ */ a.jsx("div", { className: "wf-artifact-list", role: "list", "aria-label": "Workflow artifacts", children: u.map((g) => /* @__PURE__ */ a.jsxs("article", { className: "wf-artifact-card", role: "listitem", "data-active": g.artifactId === o ? "true" : void 0, children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-artifact-card-heading", children: [
        /* @__PURE__ */ a.jsxs("div", { children: [
          /* @__PURE__ */ a.jsxs("span", { className: "wf-artifact-version", children: [
            "Version ",
            g.artifactVersion
          ] }),
          g.artifactId === o ? /* @__PURE__ */ a.jsx("span", { className: "wf-chip", children: "Latest publish" }) : null
        ] }),
        /* @__PURE__ */ a.jsx("span", { children: Ge(g.publishedAt ?? g.createdAt) })
      ] }),
      /* @__PURE__ */ a.jsxs("div", { className: "wf-artifact-card-values", children: [
        /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line", children: [
          /* @__PURE__ */ a.jsx("code", { title: g.artifactId, children: g.artifactId }),
          /* @__PURE__ */ a.jsx(Zt, { value: g.artifactId, ariaLabel: `Copy artifact ID ${g.artifactId}`, copiedLabel: "artifact ID", onCopied: m, onCopyFailed: b })
        ] }),
        /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ a.jsx("code", { title: g.artifactHash, children: g.artifactHash }),
          /* @__PURE__ */ a.jsx(Zt, { value: g.artifactHash, ariaLabel: `Copy artifact hash ${g.artifactHash}`, copiedLabel: "artifact hash", onCopied: m, onCopyFailed: b })
        ] })
      ] }),
      /* @__PURE__ */ a.jsxs("dl", { children: [
        /* @__PURE__ */ a.jsxs("div", { children: [
          /* @__PURE__ */ a.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ a.jsxs("dd", { children: [
            xd(g.sourceKind),
            " ",
            g.sourceVersion ? `v${g.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ a.jsxs("div", { children: [
          /* @__PURE__ */ a.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ a.jsx("dd", { children: md(g) })
        ] })
      ] }),
      /* @__PURE__ */ a.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
          y(g);
        }, children: [
          /* @__PURE__ */ a.jsx(fr, { size: 13 }),
          " Run"
        ] }),
        h ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => v(g), children: [
          /* @__PURE__ */ a.jsx(yt, { size: 13 }),
          " Explain"
        ] }) : null
      ] })
    ] }, g.artifactId)) }) : null
  ] });
}
function v0({ context: e, definitionId: t, currentRun: n, runs: o }) {
  const [r, i] = Y("loading"), [s, c] = Y(""), [d, l] = Y([]), u = b0(n ? [n, ...o] : o), f = n ? u.slice(1) : u, h = he(async () => {
    i("loading"), c("");
    try {
      const p = await Xl(e, { definitionId: t, take: 8 });
      l(p), i("ready");
    } catch (p) {
      c(p instanceof Error ? p.message : String(p)), l([]), i("failed");
    }
  }, [e, t]);
  return oe(() => {
    h();
  }, [h, n?.workflowExecutionId]), /* @__PURE__ */ a.jsxs("div", { id: "wf-test-runs-panel", className: "wf-test-runs-panel", children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-artifacts-toolbar", children: [
      /* @__PURE__ */ a.jsxs("span", { children: [
        u.length,
        " transient run",
        u.length === 1 ? "" : "s"
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", className: "wf-icon-button", "aria-label": "Refresh workflow test runs", title: "Refresh", onClick: () => {
        h();
      }, children: /* @__PURE__ */ a.jsx(Oi, { size: 13 }) }),
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => N0(t), children: "Open list" })
    ] }),
    n ? /* @__PURE__ */ a.jsx(Da, { testRun: n, current: !0 }) : /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "Run the current draft to inspect transient run details here." }),
    f.length > 0 ? /* @__PURE__ */ a.jsxs("section", { className: "wf-test-run-section", "aria-label": "Transient run session history", children: [
      /* @__PURE__ */ a.jsx("h4", { children: "Session history" }),
      /* @__PURE__ */ a.jsx("div", { className: "wf-test-run-list", children: f.map((p) => /* @__PURE__ */ a.jsx(Da, { testRun: p }, p.testRunId)) })
    ] }) : null,
    /* @__PURE__ */ a.jsxs("section", { className: "wf-test-run-section", "aria-label": "Persisted workflow instance history", children: [
      /* @__PURE__ */ a.jsx("h4", { children: "Recent instances" }),
      r === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert compact", children: [
        /* @__PURE__ */ a.jsx(Be, { size: 14 }),
        " ",
        s
      ] }) : null,
      r === "loading" ? /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "Loading recent instances..." }) : null,
      r === "ready" && d.length === 0 ? /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "No persisted instances for this workflow yet." }) : null,
      r === "ready" && d.length > 0 ? /* @__PURE__ */ a.jsx("div", { className: "wf-test-run-list", role: "list", "aria-label": "Recent workflow instances", children: d.map((p) => /* @__PURE__ */ a.jsx("div", { role: "listitem", children: /* @__PURE__ */ a.jsxs(
        "button",
        {
          type: "button",
          className: "wf-test-run-instance",
          onClick: () => Zi(p.workflowExecutionId),
          children: [
            /* @__PURE__ */ a.jsxs("span", { children: [
              /* @__PURE__ */ a.jsx("strong", { children: p.workflowExecutionId }),
              /* @__PURE__ */ a.jsx("small", { children: p.artifactId })
            ] }),
            /* @__PURE__ */ a.jsx(sn, { status: p.status, subStatus: p.subStatus }),
            /* @__PURE__ */ a.jsxs("small", { children: [
              p.activityCount,
              " activities · ",
              p.incidentCount,
              " incidents"
            ] }),
            /* @__PURE__ */ a.jsx("small", { children: Sd(p.startedAt ?? p.createdAt, p.completedAt ?? p.updatedAt) })
          ]
        }
      ) }, p.workflowExecutionId)) }) : null
    ] })
  ] });
}
function Da({ testRun: e, current: t = !1 }) {
  const n = Gi(e), o = e.workflowExecutionId;
  return /* @__PURE__ */ a.jsxs("article", { className: "wf-test-run-card", "data-state": n ? "rejected" : "accepted", "data-current": t ? "true" : void 0, children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-test-run-card-heading", children: [
      /* @__PURE__ */ a.jsxs("div", { children: [
        n ? /* @__PURE__ */ a.jsx(Be, { size: 15 }) : /* @__PURE__ */ a.jsx(Rt, { size: 15 }),
        /* @__PURE__ */ a.jsx("strong", { children: n ? "Test run rejected" : "Test run dispatched" })
      ] }),
      /* @__PURE__ */ a.jsx("span", { children: t ? "Current draft" : "Session" })
    ] }),
    /* @__PURE__ */ a.jsx("small", { className: "wf-test-run-scope", children: "Ephemeral - not promoted" }),
    n && e.reason ? /* @__PURE__ */ a.jsx("p", { children: e.reason }) : null,
    /* @__PURE__ */ a.jsxs("dl", { children: [
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ a.jsx("dd", { title: e.status, children: e.status })
      ] }),
      e.commandDispatchStatus ? /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Dispatch" }),
        /* @__PURE__ */ a.jsx("dd", { title: e.commandDispatchStatus, children: e.commandDispatchStatus })
      ] }) : null,
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Test run" }),
        /* @__PURE__ */ a.jsx("dd", { title: e.testRunId, children: e.testRunId })
      ] }),
      e.artifactId ? /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Artifact" }),
        /* @__PURE__ */ a.jsx("dd", { title: e.artifactId, children: e.artifactId })
      ] }) : null,
      o ? /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Execution" }),
        /* @__PURE__ */ a.jsx("dd", { title: o, children: o })
      ] }) : null,
      e.expiresAt ? /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ a.jsx("dd", { title: Ge(e.expiresAt), children: Ge(e.expiresAt) })
      ] }) : null
    ] }),
    o ? /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => Zi(o), children: "Open instance" }) : null
  ] });
}
function b0(e) {
  const t = /* @__PURE__ */ new Set();
  return e.filter((n) => t.has(n.testRunId) ? !1 : (t.add(n.testRunId), !0));
}
function Zi(e) {
  window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(e)}`), window.dispatchEvent(new PopStateEvent("popstate"));
}
function N0(e) {
  window.history.pushState({}, "", `/workflows/instances?definition=${encodeURIComponent(e)}`), window.dispatchEvent(new PopStateEvent("popstate"));
}
function S0({
  context: e,
  definitionFilter: t,
  onDefinitionFilterChange: n
}) {
  const [o, r] = Y("loading"), [i, s] = Y(""), [c, d] = Y(""), [l, u] = Y([]), f = t?.trim() || "", h = he(async () => {
    r("loading"), s("");
    try {
      const p = await Xl(e, {
        definitionId: f || void 0,
        status: c || void 0,
        take: 100
      });
      u(p), r("ready");
    } catch (p) {
      s(p instanceof Error ? p.message : String(p)), u([]), r("failed");
    }
  }, [e, f, c]);
  return oe(() => {
    h();
  }, [h]), /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
        h();
      }, children: "Refresh" }),
      /* @__PURE__ */ a.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ a.jsx("span", { children: "Definition" }),
        /* @__PURE__ */ a.jsx(
          "input",
          {
            "aria-label": "Filter workflow instances by definition",
            placeholder: "Definition id",
            value: f,
            onChange: (p) => n(p.target.value || null)
          }
        )
      ] }),
      /* @__PURE__ */ a.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ a.jsx("span", { children: "Status" }),
        /* @__PURE__ */ a.jsxs("select", { "aria-label": "Workflow instance status", value: c, onChange: (p) => d(p.target.value), children: [
          /* @__PURE__ */ a.jsx("option", { value: "", children: "All statuses" }),
          /* @__PURE__ */ a.jsx("option", { value: "Pending", children: "Pending" }),
          /* @__PURE__ */ a.jsx("option", { value: "Running", children: "Running" }),
          /* @__PURE__ */ a.jsx("option", { value: "Suspended", children: "Suspended" }),
          /* @__PURE__ */ a.jsx("option", { value: "Completed", children: "Completed" }),
          /* @__PURE__ */ a.jsx("option", { value: "Faulted", children: "Faulted" }),
          /* @__PURE__ */ a.jsx("option", { value: "Cancelled", children: "Cancelled" })
        ] })
      ] })
    ] }),
    o === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(Be, { size: 16 }),
      " ",
      i
    ] }) : null,
    o === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading workflow instances..." }) : null,
    o === "ready" && l.length === 0 ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "No workflow instances found. Run a published workflow executable to create instance history." }) : null,
    o === "ready" && l.length > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-grid wf-instance-grid", role: "table", "aria-label": "Workflow instances", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ a.jsx("span", { children: "Instance" }),
        /* @__PURE__ */ a.jsx("span", { children: "Status" }),
        /* @__PURE__ */ a.jsx("span", { children: "Definition" }),
        /* @__PURE__ */ a.jsx("span", { children: "Activity" }),
        /* @__PURE__ */ a.jsx("span", { children: "Started" }),
        /* @__PURE__ */ a.jsx("span", { children: "Duration" })
      ] }),
      l.map((p) => /* @__PURE__ */ a.jsxs(
        "button",
        {
          type: "button",
          className: "wf-grid-row",
          role: "row",
          "aria-label": `Inspect workflow instance ${p.workflowExecutionId}`,
          onClick: () => Zi(p.workflowExecutionId),
          children: [
            /* @__PURE__ */ a.jsxs("span", { children: [
              /* @__PURE__ */ a.jsx("strong", { children: p.workflowExecutionId }),
              /* @__PURE__ */ a.jsx("small", { children: p.artifactId })
            ] }),
            /* @__PURE__ */ a.jsx("span", { children: /* @__PURE__ */ a.jsx(sn, { status: p.status, subStatus: p.subStatus }) }),
            /* @__PURE__ */ a.jsxs("span", { children: [
              /* @__PURE__ */ a.jsx("strong", { children: p.definitionId }),
              /* @__PURE__ */ a.jsx("small", { children: p.definitionVersionId })
            ] }),
            /* @__PURE__ */ a.jsxs("span", { children: [
              /* @__PURE__ */ a.jsxs("strong", { children: [
                p.activityCount,
                " activities"
              ] }),
              /* @__PURE__ */ a.jsxs("small", { children: [
                p.incidentCount,
                " incidents"
              ] })
            ] }),
            /* @__PURE__ */ a.jsx("span", { children: Ge(p.startedAt ?? p.createdAt) }),
            /* @__PURE__ */ a.jsx("span", { children: Sd(p.startedAt ?? p.createdAt, p.completedAt ?? p.updatedAt) })
          ]
        },
        p.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function E0({ context: e, ai: t, workflowExecutionId: n }) {
  const [o, r] = Y("loading"), [i, s] = Y(""), [c, d] = Y(null), [l, u] = Y(null), f = zt(t, "weaver.workflows.explain-instance"), h = he(async () => {
    if (!n) {
      s("No workflow execution id was provided."), r("failed");
      return;
    }
    r("loading"), s("");
    try {
      const y = await cx(e, n), [v, w] = await Promise.all([
        Km(e, y.instance.definitionVersionId).then(
          (m) => ({ definitionVersion: m, error: "" }),
          (m) => ({ definitionVersion: null, error: m instanceof Error ? m.message : String(m) })
        ),
        Bi(e)
      ]);
      d({
        details: y,
        definitionVersion: v.definitionVersion,
        definitionVersionError: v.error,
        activityCatalog: w.activities
      }), u(null), r("ready");
    } catch (y) {
      d(null), s(y instanceof Error ? y.message : String(y)), r("failed");
    }
  }, [e, n]);
  oe(() => {
    h();
  }, [h]);
  const p = () => {
    window.history.pushState({}, "", "/workflows/instances"), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: p, children: [
        /* @__PURE__ */ a.jsx(Zo, { size: 14 }),
        " Instances"
      ] }),
      /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
        h();
      }, children: [
        /* @__PURE__ */ a.jsx(Fi, { size: 14 }),
        " Refresh"
      ] }),
      c && f ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => mt(t, f, c.details), children: [
        /* @__PURE__ */ a.jsx(yt, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    o === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading workflow instance..." }) : null,
    o === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(Be, { size: 16 }),
      " ",
      i
    ] }) : null,
    o === "ready" && c ? /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-detail-workbench", children: [
      /* @__PURE__ */ a.jsx(
        k0,
        {
          definitionVersion: c.definitionVersion,
          definitionVersionError: c.definitionVersionError,
          activityCatalog: c.activityCatalog,
          details: c.details,
          selectedEvidenceId: l,
          onSelectEvidence: u
        }
      ),
      /* @__PURE__ */ a.jsx(
        I0,
        {
          ai: t,
          action: f,
          summary: c.details.instance,
          details: c.details,
          state: "ready",
          error: "",
          selectedEvidenceId: l,
          onSelectEvidence: u,
          graphNodeIds: c.definitionVersion ? A0(c.definitionVersion, c.activityCatalog) : void 0
        }
      )
    ] }) : null
  ] });
}
function k0({ definitionVersion: e, definitionVersionError: t, activityCatalog: n, details: o, selectedEvidenceId: r, onSelectEvidence: i }) {
  const s = me(() => {
    if (!e) return { nodes: [], edges: [] };
    const c = e.state.rootActivity;
    if (!c) return { nodes: [], edges: [] };
    const d = n.find((p) => p.activityVersionId === c.activityVersionId), l = Wi(c, d), u = l === "unsupported" ? null : Dn(c, []), f = l === "unsupported" ? pi(c, n, e.layout) : u ? Ul(u, n, e.layout) : pi(c, n, e.layout), h = f.nodes.map((p) => ({
      ...p,
      draggable: !1,
      connectable: !1,
      deletable: !1
    }));
    return {
      nodes: fx(h, o.activities, o.incidents, r),
      edges: f.edges.map((p) => ({ ...p, deletable: !1 }))
    };
  }, [n, e, o, r]);
  return /* @__PURE__ */ a.jsxs("section", { className: "wf-instance-canvas-shell", "aria-label": "Workflow instance canvas", children: [
    /* @__PURE__ */ a.jsxs("header", { children: [
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("span", { children: "Definition version" }),
        /* @__PURE__ */ a.jsx("h3", { children: e ? /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
          e.definition.name,
          " ",
          /* @__PURE__ */ a.jsx("small", { children: e.version })
        ] }) : /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
          "Definition graph unavailable ",
          /* @__PURE__ */ a.jsx("small", { children: o.instance.definitionVersionId })
        ] }) })
      ] }),
      /* @__PURE__ */ a.jsx(sn, { status: o.instance.status, subStatus: o.instance.subStatus })
    ] }),
    /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ a.jsxs("div", { className: "wf-empty", children: [
        "The workflow instance loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ a.jsx("small", { children: iw(t) }) : null
      ] }),
      e && s.nodes.length === 0 ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      s.nodes.length > 0 ? /* @__PURE__ */ a.jsxs(
        jl,
        {
          nodes: s.nodes,
          edges: s.edges,
          nodeTypes: ad,
          edgeTypes: cd,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (c, d) => i(d.id),
          onPaneClick: () => i(null),
          children: [
            /* @__PURE__ */ a.jsx(Al, {}),
            /* @__PURE__ */ a.jsx($l, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ a.jsx(Dl, {})
          ]
        }
      ) : null
    ] })
  ] });
}
function I0({ ai: e, action: t, summary: n, details: o, state: r, error: i, selectedEvidenceId: s = null, onSelectEvidence: c, graphNodeIds: d }) {
  return n ? /* @__PURE__ */ a.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Workflow instance details", children: [
    /* @__PURE__ */ a.jsxs("header", { children: [
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("span", { children: "Workflow instance" }),
        /* @__PURE__ */ a.jsx("h3", { children: n.workflowExecutionId })
      ] }),
      t ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => mt(e, t, o ?? n), children: [
        /* @__PURE__ */ a.jsx(yt, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    /* @__PURE__ */ a.jsxs("dl", { className: "wf-instance-meta", children: [
      /* @__PURE__ */ a.jsx("dt", { children: "Status" }),
      /* @__PURE__ */ a.jsx("dd", { children: /* @__PURE__ */ a.jsx(sn, { status: n.status, subStatus: n.subStatus }) }),
      /* @__PURE__ */ a.jsx("dt", { children: "Artifact" }),
      /* @__PURE__ */ a.jsxs("dd", { children: [
        n.artifactId,
        " ",
        /* @__PURE__ */ a.jsx("small", { children: n.artifactVersion })
      ] }),
      /* @__PURE__ */ a.jsx("dt", { children: "Definition" }),
      /* @__PURE__ */ a.jsxs("dd", { children: [
        n.definitionId,
        " ",
        /* @__PURE__ */ a.jsx("small", { children: n.definitionVersionId })
      ] }),
      /* @__PURE__ */ a.jsx("dt", { children: "Created" }),
      /* @__PURE__ */ a.jsx("dd", { children: Ge(n.createdAt) }),
      /* @__PURE__ */ a.jsx("dt", { children: "Started" }),
      /* @__PURE__ */ a.jsx("dd", { children: Ge(n.startedAt) }),
      /* @__PURE__ */ a.jsx("dt", { children: "Completed" }),
      /* @__PURE__ */ a.jsx("dd", { children: Ge(n.completedAt) }),
      /* @__PURE__ */ a.jsx("dt", { children: "Correlation" }),
      /* @__PURE__ */ a.jsx("dd", { children: n.correlationId || "None" })
    ] }),
    r === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading instance details..." }) : null,
    r === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(Be, { size: 16 }),
      " ",
      i
    ] }) : null,
    r === "ready" && o ? /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
      /* @__PURE__ */ a.jsx(C0, { activities: o.activities, selectedEvidenceId: s, onSelectEvidence: c }),
      /* @__PURE__ */ a.jsx(j0, { incidents: o.incidents, selectedEvidenceId: s, onSelectEvidence: c }),
      /* @__PURE__ */ a.jsx(_0, { details: o, graphNodeIds: d })
    ] }) : null
  ] }) : /* @__PURE__ */ a.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Select a workflow instance to inspect activity history." }) });
}
function C0({ activities: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
  return /* @__PURE__ */ a.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ a.jsx("h4", { children: "Activity history" }),
    e.length === 0 ? /* @__PURE__ */ a.jsx("p", { children: "No activity executions recorded yet." }) : null,
    e.length > 0 ? /* @__PURE__ */ a.jsx("div", { className: "wf-instance-activity-list", children: e.map((o) => /* @__PURE__ */ a.jsxs(
      "button",
      {
        type: "button",
        className: "wf-instance-activity",
        "data-selected": o.activityExecutionId === t,
        onClick: () => n?.(o.activityExecutionId),
        children: [
          /* @__PURE__ */ a.jsx("span", { children: /* @__PURE__ */ a.jsx(sn, { status: o.status, subStatus: o.subStatus }) }),
          /* @__PURE__ */ a.jsx("strong", { children: Ki(o.activityType) ?? o.activityType }),
          /* @__PURE__ */ a.jsx("small", { children: o.activityExecutionId }),
          /* @__PURE__ */ a.jsx("time", { children: Ge(o.scheduledAt) })
        ]
      },
      o.activityExecutionId
    )) }) : null
  ] });
}
function j0({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
  return /* @__PURE__ */ a.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ a.jsx("h4", { children: "Incidents" }),
    e.length === 0 ? /* @__PURE__ */ a.jsx("p", { children: "No incidents recorded." }) : null,
    e.map((o) => /* @__PURE__ */ a.jsxs(
      "button",
      {
        type: "button",
        className: "wf-instance-incident",
        "data-severity": o.severity.toLowerCase(),
        "data-selected": o.incidentId === t,
        onClick: () => n?.(o.incidentId),
        children: [
          /* @__PURE__ */ a.jsx("strong", { children: o.failureType }),
          /* @__PURE__ */ a.jsxs("span", { children: [
            o.status,
            " · ",
            o.severity
          ] }),
          /* @__PURE__ */ a.jsx("p", { children: o.message })
        ]
      },
      o.incidentId
    ))
  ] });
}
function _0({ details: e, graphNodeIds: t }) {
  if (!t) return null;
  const n = new Map(e.activities.map((i) => [i.activityExecutionId, i])), o = e.activities.filter((i) => !t.has(Pa(i))), r = e.incidents.filter((i) => {
    const s = i.activityExecutionId ? n.get(i.activityExecutionId) : null, c = i.executableNodeId ?? (s ? Pa(s) : "");
    return !c || !t.has(c);
  });
  return o.length === 0 && r.length === 0 ? null : /* @__PURE__ */ a.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ a.jsx("h4", { children: "Unmatched runtime evidence" }),
    /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-unmatched-list", children: [
      o.map((i) => /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ a.jsx("strong", { children: Ki(i.activityType) ?? i.activityType }),
        /* @__PURE__ */ a.jsx("small", { children: i.activityExecutionId })
      ] }, `activity-${i.activityExecutionId}`)),
      r.map((i) => /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ a.jsx("strong", { children: i.failureType }),
        /* @__PURE__ */ a.jsx("small", { children: i.incidentId })
      ] }, `incident-${i.incidentId}`))
    ] })
  ] });
}
function sn({ status: e, subStatus: t }) {
  return /* @__PURE__ */ a.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
function A0(e, t) {
  const n = e.state.rootActivity;
  if (!n) return /* @__PURE__ */ new Set();
  const o = t.find((i) => i.activityVersionId === n.activityVersionId);
  if (Wi(n, o) === "unsupported") return /* @__PURE__ */ new Set([n.nodeId]);
  const r = Dn(n, []);
  return new Set(r?.slot.activities.map((i) => i.nodeId) ?? [n.nodeId]);
}
function Pa(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function M0({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const i = ud(n, t), s = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
  return /* @__PURE__ */ a.jsxs("div", { className: "wf-pagination", "aria-label": "Workflow definition pagination", children: [
    /* @__PURE__ */ a.jsxs("span", { className: "wf-pagination-summary", "aria-live": "polite", children: [
      "Showing ",
      s,
      "-",
      c,
      " of ",
      n
    ] }),
    /* @__PURE__ */ a.jsxs("label", { className: "wf-page-size", children: [
      "Rows",
      /* @__PURE__ */ a.jsx("select", { value: t, onChange: (d) => r(Number(d.target.value)), children: s0.map((d) => /* @__PURE__ */ a.jsx("option", { value: d, children: d }, d)) })
    ] }),
    /* @__PURE__ */ a.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ a.jsx(Zo, { size: 14 }),
        " Previous"
      ] }),
      /* @__PURE__ */ a.jsxs("span", { children: [
        "Page ",
        e,
        " of ",
        i
      ] }),
      /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => o(e + 1), disabled: e >= i, "aria-label": "Next page", title: "Next page", children: [
        "Next ",
        /* @__PURE__ */ a.jsx(Wt, { size: 14 })
      ] })
    ] })
  ] });
}
function D0(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function ud(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function zt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function mt(e, t, n) {
  const o = t.createPrompt(n);
  return o ? (e.dispatchPrompt(o), !0) : !1;
}
function P0(e) {
  const t = Qo(e, "flowchart"), n = Qo(e, "sequence"), o = [
    { value: t?.activityVersionId ?? "kind:flowchart", label: "Flowchart" },
    { value: n?.activityVersionId ?? "kind:sequence", label: "Sequence" }
  ], r = /* @__PURE__ */ new Map();
  for (const s of e.filter(yd)) {
    if (z0(s)) continue;
    const c = s.category || "Uncategorized";
    r.set(c, [...r.get(c) ?? [], s]);
  }
  const i = Array.from(r.entries()).sort(([s], [c]) => s.localeCompare(c)).map(([s, c]) => ({
    name: s,
    activities: c.sort((d, l) => Ce(d).localeCompare(Ce(l)))
  }));
  return { compositeRoots: o, otherCategories: i };
}
function $0(e, t) {
  return e.rootActivityVersionId ?? Qo(t, e.rootKind)?.activityVersionId ?? `kind:${e.rootKind}`;
}
function T0(e, t) {
  return e.rootActivityVersionId ?? Qo(t, e.rootKind)?.activityVersionId ?? null;
}
function Qo(e, t) {
  return e.find((n) => fd(n) === t);
}
function fd(e) {
  return e ? pd(e) ? "flowchart" : gd(e) ? "sequence" : null : null;
}
function hd(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "Uncategorized";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [o]) => n.localeCompare(o)).map(([n, o]) => ({
    category: n,
    activities: o.sort((r, i) => Ce(r).localeCompare(Ce(i)))
  }));
}
function z0(e) {
  return pd(e) || gd(e);
}
function pd(e) {
  return Ce(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function gd(e) {
  return Ce(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function yd(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function md(e) {
  return F0(e.rootActivityType) || e.rootActivityType;
}
function R0(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function L0(e, t) {
  return t ? "No workflow executables match this definition filter." : e === "deleted" ? "No deleted workflow executables found." : "No workflow executables found. Publish a workflow definition to create one.";
}
function V0(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function H0(e, t) {
  return $a(t) - $a(e);
}
function $a(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function xd(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
async function O0(e) {
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
function F0(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function B0(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    _o(t, n.typeName, n), _o(t, n.name, n), _o(t, n.displayName, n);
    const o = n.typeName.split(".").filter(Boolean).at(-1);
    _o(t, o, n);
  }
  return t;
}
function W0(e, t, n) {
  const o = t.get(e.activityVersionId);
  return n.get(_n(o?.activityTypeKey)) ?? n.get(_n(Ki(o?.activityTypeKey))) ?? n.get(_n(o?.displayName)) ?? n.get(_n(e.activityVersionId)) ?? null;
}
function _o(e, t, n) {
  const o = _n(t);
  o && !e.has(o) && e.set(o, n);
}
function _n(e) {
  return e?.trim().toLowerCase() ?? "";
}
function Ki(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function Ta(e, t, n, o) {
  const r = gr();
  if (!r) return t;
  const i = r.getItem(e);
  if (i == null) return t;
  const s = Number(i);
  return Number.isFinite(s) ? Ro(s, n, o) : t;
}
function za(e, t) {
  const n = gr();
  if (!n) return t;
  const o = n.getItem(e);
  return o === "true" ? !0 : o === "false" ? !1 : t;
}
function X0() {
  const e = gr();
  if (!e) return null;
  const t = e.getItem(ld);
  return t === "palette" || t === "inspector" ? t : null;
}
function gr() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function In(e, t) {
  const n = gr();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function Ro(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function Y0({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: o,
  workflowDesignerPanels: r,
  onBack: i
}) {
  const [s, c] = Y(null), [d, l] = Y(null), [u, f] = Y([]), [h, p] = Y([]), [y, v] = Y(To), [w, m] = Y("loading"), [b, g] = Y([]), [x, I] = Y([]), [E, j] = Y([]), [P, T] = Y(null), [A, k] = Y(null), [R, O] = Y(null), [S, _] = Y(null), [C, M] = Y(""), [z, $] = Y(""), [W, F] = Y("idle"), [B, Z] = Y(null), [K, ne] = Y([]), [se, Q] = Y(!1), [L, q] = Y(null), [ae, ie] = Y(() => /* @__PURE__ */ new Set()), [G, ee] = Y(() => Ta(Ia, c0, Nn, Sn)), [le, H] = Y(() => Ta(Ca, l0, En, kn)), [U, pe] = Y(() => za(ja, !1)), [ge, Ae] = Y(() => za(_a, !1)), [ye, Me] = Y(X0), [wt, ot] = Y("activities"), [rt, $e] = Y("inspector"), Ee = ce(null), Te = ce(null), We = ce(""), an = ce(0), Lt = ce(Promise.resolve()), cn = ce(/* @__PURE__ */ new Map()), Vt = ce(null), dt = ce(null), vt = ce(!1), bt = d?.state.rootActivity ?? null, Qe = me(() => new Map(u.map((N) => [N.activityVersionId, N])), [u]), Jn = me(() => B0(h), [h]), Le = me(() => ql(bt, b), [bt, b]), Qn = Wi(Le, Le ? Qe.get(Le.activityVersionId) : void 0), be = !!Le && Qn === "unsupported", Xe = me(() => be ? null : Dn(bt, b), [bt, b, be]), ln = me(() => hd(u), [u]), Ne = me(() => be && Le?.nodeId === A ? Le : Xe?.slot.activities.find((N) => N.nodeId === A) ?? null, [be, Xe, Le, A]), Nt = me(
    () => Ne ? W0(Ne, Qe, Jn) : null,
    [Qe, Jn, Ne]
  ), dn = Ne ? Ke(Ne) : [], _e = Qn === "flowchart" && Xe?.slot.mode === "flowchart", St = !bt || !be, ut = W !== "idle", yr = !!d?.state.rootActivity && !ut, eo = zt(n, "weaver.workflows.find-draft-risks"), to = zt(n, "weaver.workflows.propose-update");
  oe(() => {
    if (!(!s || !d))
      return window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
        workflowId: s.definition.id,
        workflowDefinitionId: s.definition.id,
        workflowVersionId: d.sourceVersionId ?? null,
        draftId: d.id,
        revision: nw(d),
        selectedNodeId: A,
        selectedActivityType: Nt?.typeName ?? (Ne ? Qe.get(Ne.activityVersionId)?.activityTypeKey ?? Ne.activityVersionId : null),
        summary: s.definition.name,
        activities: bd(d.state.rootActivity, Qe),
        diagnostics: d.validationErrors.map((N) => ({ severity: N.code ?? "warning", message: N.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === s.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [Qe, s, d, Nt, Ne, A]), oe(() => {
    const N = (V) => {
      const X = V.detail;
      if (!X?.batch || !X.respond) return;
      if (!d || !s) {
        X.respond({ ok: !1, message: "No active workflow draft is open." });
        return;
      }
      const J = X.batch.workflowDefinitionId;
      if (J && J !== "active-draft" && J !== s.definition.id) {
        X.respond({ ok: !1, message: `Batch targets workflow '${J}', but '${s.definition.id}' is active.` });
        return;
      }
      try {
        const de = ow(d), re = Zx(d, X.batch, u), fe = `weaver-batch-${Date.now()}`;
        cn.current.set(fe, de), l(re.draft), g([]), k(re.finalActivityIds.at(-1) ?? null), q(null), Z(null), $(re.summary), M(""), X.respond({ ok: !0, result: { ...re, undoToken: fe } });
      } catch (de) {
        const re = de instanceof Error ? de.message : String(de);
        M(re), X.respond({ ok: !1, message: re });
      }
    }, D = (V) => {
      const X = V.detail;
      if (!X?.undoToken || !X.respond) return;
      const J = cn.current.get(X.undoToken);
      if (!J) {
        X.respond({ ok: !1, message: "The Weaver batch undo point is no longer available." });
        return;
      }
      cn.current.delete(X.undoToken), l(J), g([]), k(null), q(null), Z(null), $("Restored workflow draft before Weaver batch."), M(""), X.respond({ ok: !0, summary: "Restored workflow draft before Weaver batch." });
    };
    return window.addEventListener(Na, N), window.addEventListener(Sa, D), () => {
      window.removeEventListener(Na, N), window.removeEventListener(Sa, D);
    };
  }, [u, s, d]), oe(() => {
    In(Ia, String(G));
  }, [G]), oe(() => {
    In(Ca, String(le));
  }, [le]), oe(() => {
    In(ja, String(U));
  }, [U]), oe(() => {
    In(_a, String(ge));
  }, [ge]), oe(() => {
    In(ld, ye);
  }, [ye]), oe(() => {
    if (!ye) return;
    const N = (D) => {
      D.key === "Escape" && Me(null);
    };
    return window.addEventListener("keydown", N), () => window.removeEventListener("keydown", N);
  }, [ye]);
  const un = he(async () => {
    M(""), m("loading");
    const [N, D, V, X] = await Promise.all([
      Zm(e, t),
      Bi(e),
      lx(e).then(
        (de) => ({ ok: !0, descriptors: de }),
        () => ({ ok: !1, descriptors: [] })
      ),
      dx(e).then(
        (de) => ({ ok: !0, descriptors: de }),
        () => ({ ok: !1, descriptors: To })
      )
    ]), J = N.draft ?? null;
    c(N), We.current = J ? st(J) : "", l(J), f(D.activities ?? []), p(V.descriptors), v(X.descriptors.length > 0 ? X.descriptors : To), m(V.ok ? "ready" : "failed"), g([]), k(null);
  }, [e, t]);
  oe(() => {
    un().catch((N) => M(N instanceof Error ? N.message : String(N)));
  }, [un]), oe(() => {
    ie((N) => {
      let D = !1;
      const V = new Set(N);
      for (const X of ln)
        V.has(X.category) || (V.add(X.category), D = !0);
      return D ? V : N;
    });
  }, [ln]), oe(() => {
    if (!Le) {
      I([]), j([]);
      return;
    }
    const N = be ? pi(Le, u, d?.layout ?? []) : Xe ? Ul(Xe, u, d?.layout ?? []) : { nodes: [], edges: [] };
    I(N.nodes), j(N.edges);
  }, [u, d?.layout, be, Xe, Le]);
  const mr = (N) => {
    l((D) => D && { ...D, state: { ...D.state, rootActivity: N } });
  }, fn = he((N, D) => {
    if (d?.state.rootActivity && be)
      return;
    const V = yi(N, Va(N));
    if (!d?.state.rootActivity) {
      mr(V), k(V.nodeId);
      return;
    }
    if (!Xe) {
      if (!Ke(V)[0]) {
        $(""), M("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      l((J) => {
        if (!J?.state.rootActivity) return J;
        const de = J.state.rootActivity, re = gi(V, [], [de]), fe = D ? [
          ...J.layout.filter((ke) => ke.nodeId !== de.nodeId),
          {
            nodeId: de.nodeId,
            x: Math.round(D.x),
            y: Math.round(D.y)
          }
        ] : J.layout;
        return {
          ...J,
          layout: fe,
          state: {
            ...J.state,
            rootActivity: re
          }
        };
      }), k(d.state.rootActivity.nodeId), M(""), $(`Wrapped root in ${Ce(N)}`);
      return;
    }
    l((X) => {
      if (!X?.state.rootActivity) return X;
      const J = Dn(X.state.rootActivity, b);
      if (!J) return X;
      const de = gi(X.state.rootActivity, b, [...J.slot.activities, V]), re = D ? [
        ...X.layout.filter((fe) => fe.nodeId !== V.nodeId),
        {
          nodeId: V.nodeId,
          x: Math.round(D.x),
          y: Math.round(D.y)
        }
      ] : X.layout;
      return {
        ...X,
        layout: re,
        state: {
          ...X.state,
          rootActivity: de
        }
      };
    }), k(V.nodeId);
  }, [d?.state.rootActivity, b, be, Xe]), Ht = he((N, D) => {
    const V = yi(N, Va(N)), X = {
      id: V.nodeId,
      type: "workflowActivity",
      position: D,
      selected: !0,
      data: {
        label: Ce(N),
        activityVersionId: N.activityVersionId,
        activityTypeKey: N.activityTypeKey,
        category: N.category,
        executionType: N.executionType,
        icon: mi(N),
        childSlots: Ke(V),
        acceptsInbound: String(N.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: Jl(V, N)
      }
    };
    return { activityNode: V, node: X };
  }, []), Ve = he((N, D, V = []) => {
    be || l((X) => {
      if (!X) return X;
      const J = gx(X.layout, N), de = X.state.rootActivity;
      if (!de) return { ...X, layout: J };
      const re = Dn(de, b);
      if (!re) return { ...X, layout: J };
      const fe = hx(re, N, D, V), ke = re.slot.mode === "flowchart" ? px(fe, D) : fe;
      return {
        ...X,
        layout: J,
        state: {
          ...X.state,
          rootActivity: Zl(de, b, ke)
        }
      };
    });
  }, [b, be]), hn = he((N, D) => {
    if (!Ee.current) return null;
    const V = Ee.current.getBoundingClientRect();
    return P ? P.screenToFlowPosition({ x: N, y: D }) : {
      x: N - V.left,
      y: D - V.top
    };
  }, [P]), pn = he((N, D) => document.elementFromPoint(N, D)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), gn = he((N, D, V) => {
    const X = x.find((De) => De.id === D.source), J = x.find((De) => De.id === D.target), de = X && J ? Q0(X, J) : X ? Ha(X) : V, re = Ht(N, de), ke = [...x.map((De) => De.selected ? { ...De, selected: !1 } : De), re.node], Et = kx(E, D, re.node.id);
    I(ke), j(Et), k(re.node.id), Ve(ke, Et, [re.activityNode]);
  }, [Ve, Ht, E, x]), Ot = he((N, D, V) => {
    if (!St || !Ee.current) return !1;
    const X = Ee.current.getBoundingClientRect();
    if (!(D >= X.left && D <= X.right && V >= X.top && V <= X.bottom)) return !1;
    const de = hn(D, V);
    if (!de) return !1;
    if (_e) {
      const re = pn(D, V), fe = re ? E.find((ke) => ke.id === re) : void 0;
      if (fe)
        return gn(N, fe, de), !0;
    }
    return fn(N, de), !0;
  }, [fn, St, E, pn, _e, gn, hn]);
  oe(() => {
    const N = (V) => {
      const X = Vt.current;
      if (!X) return;
      Math.hypot(V.clientX - X.startX, V.clientY - X.startY) >= r0 && (X.dragging = !0);
    }, D = (V) => {
      const X = Vt.current;
      if (Vt.current = null, !X?.dragging || !Ee.current || dt.current) return;
      const J = Ee.current.getBoundingClientRect();
      V.clientX >= J.left && V.clientX <= J.right && V.clientY >= J.top && V.clientY <= J.bottom && (vt.current = !0, window.setTimeout(() => {
        vt.current = !1;
      }, 0), Ot(X.activity, V.clientX, V.clientY));
    };
    return window.addEventListener("pointermove", N), window.addEventListener("pointerup", D), window.addEventListener("pointercancel", D), () => {
      window.removeEventListener("pointermove", N), window.removeEventListener("pointerup", D), window.removeEventListener("pointercancel", D);
    };
  }, [P, Ot]);
  const no = (N, D) => {
    dt.current = { activityVersionId: D.activityVersionId, handledDrop: !1 }, N.dataTransfer.setData(ka, D.activityVersionId), N.dataTransfer.setData("text/plain", D.activityVersionId), N.dataTransfer.effectAllowed = "copy";
  }, oo = (N, D) => {
    const V = dt.current;
    dt.current = null, !V?.handledDrop && (N.clientX === 0 && N.clientY === 0 || Ot(D, N.clientX, N.clientY) && (vt.current = !0, window.setTimeout(() => {
      vt.current = !1;
    }, 0)));
  }, ro = (N, D) => {
    N.button === 0 && (Vt.current = {
      activity: D,
      startX: N.clientX,
      startY: N.clientY,
      dragging: !1
    });
  }, xr = (N) => {
    vt.current || St && fn(N);
  }, io = (N) => {
    if (!St) {
      N.dataTransfer.dropEffect = "none";
      return;
    }
    if (N.preventDefault(), N.dataTransfer.dropEffect = "copy", !_e) return;
    const D = pn(N.clientX, N.clientY);
    _(D);
  }, so = (N) => {
    if (!Ee.current) return;
    const D = N.relatedTarget;
    D && Ee.current.contains(D) || _(null);
  }, ao = (N) => {
    N.preventDefault(), _(null);
    const D = N.dataTransfer.getData(ka) || N.dataTransfer.getData("text/plain");
    if (!D || (N.stopPropagation(), dt.current?.activityVersionId === D && (dt.current.handledDrop = !0), !St)) return;
    const V = Qe.get(D);
    V && Ot(V, N.clientX, N.clientY);
  }, wr = () => {
    if (!_e) return;
    const N = Ee.current?.getBoundingClientRect();
    N && O({
      kind: "fromEmpty",
      clientX: N.left + N.width / 2,
      clientY: N.top + N.height / 2
    });
  }, yn = he(async (N, D) => {
    const V = async () => {
      const J = ++an.current, de = st(N);
      M("");
      try {
        const re = await tx(e, N), fe = st(re);
        return We.current = fe, l((ke) => !ke || ke.id !== re.id ? ke : st(ke) === de ? re : { ...ke, validationErrors: re.validationErrors }), J === an.current && $(D), re;
      } catch (re) {
        throw J === an.current && ($(""), M(re instanceof Error ? re.message : String(re))), re;
      }
    }, X = Lt.current.then(V, V);
    return Lt.current = X.catch(() => {
    }), X;
  }, [e]);
  oe(() => {
    if (!se || !d || st(d) === We.current) return;
    $("Autosaving...");
    const D = window.setTimeout(() => {
      yn(d, "Autosaved").catch(() => {
      });
    }, i0);
    return () => window.clearTimeout(D);
  }, [se, d, yn]);
  const co = async () => {
    if (!(!d || ut)) {
      F("saving"), $("Saving...");
      try {
        await yn(d, "Saved");
      } catch {
      } finally {
        F("idle");
      }
    }
  }, vr = async () => {
    if (!(!d || ut)) {
      F("promoting"), $("Promoting...");
      try {
        const N = await nx(e, d.id), D = await ox(e, N.versionId);
        q(D.artifactId), $(`Published ${D.artifactVersion}`), await un();
      } catch (N) {
        $(""), M(N instanceof Error ? N.message : String(N));
      } finally {
        F("idle");
      }
    }
  }, br = async () => {
    if (!d?.state.rootActivity || ut) return;
    const N = d, D = st(N);
    Z(null), $("Preparing test run...");
    try {
      F("testRunPreparing"), $("Preparing test run...");
      const V = rw(N);
      F("testRunStarting"), $("Starting test run...");
      const X = await rx(e, {
        definitionId: N.definitionId,
        snapshotId: V,
        state: N.state
      }), J = { draftSignature: D, view: X };
      Z(J), ne((de) => [
        J,
        ...de.filter((re) => re.view.testRunId !== X.testRunId)
      ].slice(0, 10)), $(Gi(X) ? "Test run rejected" : "Test run dispatched");
    } catch (V) {
      $(""), M(V instanceof Error ? V.message : String(V));
    } finally {
      F("idle");
    }
  }, Nr = (N) => {
    const D = be ? N.filter((V) => V.type === "select") : N;
    D.length !== 0 && I((V) => el(D, V));
  }, Sr = (N) => {
    be || j((D) => tl(N, D));
  }, mn = (N) => !N.source || !N.target || N.source === N.target || !_e ? !1 : !N.targetHandle, Er = (N) => {
    if (!d?.state.rootActivity || !Xe || !_e || !mn(N)) return;
    const D = Go(N.source, N.target, N.sourceHandle ?? "Done", N.targetHandle ?? void 0), V = ol(D, E);
    j(V), Ve(x, V);
  }, kr = () => {
    Ve(x, E);
  }, Ir = (N, D) => {
    if (!D.nodeId || D.handleType === "target") {
      Te.current = null;
      return;
    }
    Te.current = {
      nodeId: D.nodeId,
      handleId: D.handleId ?? null
    };
  }, lo = (N, D) => {
    const V = tw(Te.current, D);
    if (Te.current = null, !V || !_e || D.toNode || D.toHandle || ew(N)) return;
    const X = vd(N);
    O({
      kind: "fromPort",
      sourceNodeId: V.nodeId,
      sourceHandleId: V.handleId,
      clientX: X.x,
      clientY: X.y
    });
  }, Cr = (N, D) => {
    if (!_e || !mn(D)) return;
    const V = Wg(N, {
      ...D,
      sourceHandle: D.sourceHandle ?? "Done",
      targetHandle: D.targetHandle ?? void 0
    }, E, { shouldReplaceId: !1 });
    j(V), Ve(x, V);
  }, jr = (N) => {
    if (be || N.length === 0) return;
    const D = new Set(N.map((J) => J.id)), V = x.filter((J) => !D.has(J.id)), X = E.filter((J) => !D.has(J.source) && !D.has(J.target));
    I(V), j(X), A && D.has(A) && k(null), Ve(V, X);
  }, _r = (N) => {
    if (be || N.length === 0) return;
    const D = new Set(N.map((X) => X.id)), V = E.filter((X) => !D.has(X.id));
    j(V), Ve(x, V);
  }, uo = he((N) => {
    if (be) return;
    const D = E.filter((V) => V.id !== N);
    j(D), Ve(x, D);
  }, [Ve, E, be, x]), fo = he((N, D, V) => {
    _e && O({ kind: "spliceEdge", edgeId: N, clientX: D, clientY: V });
  }, [_e]), Ar = (N) => {
    const D = R;
    if (!D) return;
    O(null);
    const V = hn(D.clientX, D.clientY) ?? { x: 0, y: 0 };
    if (D.kind === "fromEmpty") {
      const J = Ht(N, V), re = [...x.map((fe) => fe.selected ? { ...fe, selected: !1 } : fe), J.node];
      I(re), k(J.node.id), Ve(re, E, [J.activityNode]);
      return;
    }
    if (D.kind === "fromPort") {
      const J = x.find((De) => De.id === D.sourceNodeId), de = J ? Ha(J) : V, re = Ht(N, de), ke = [...x.map((De) => De.selected ? { ...De, selected: !1 } : De), re.node], Et = [...E, Go(D.sourceNodeId, re.node.id, D.sourceHandleId ?? "Done")];
      I(ke), j(Et), k(re.node.id), Ve(ke, Et, [re.activityNode]);
      return;
    }
    const X = E.find((J) => J.id === D.edgeId);
    X && gn(N, X, V);
  }, Mr = me(() => ({
    highlightedEdgeId: S,
    deleteEdge: uo,
    requestInsertActivity: fo
  }), [uo, S, fo]), Dr = (N, D, V) => {
    g((X) => [...X, { ownerNodeId: N.nodeId, slotId: D, label: V }]), k(null);
  }, ho = he((N) => {
    l((D) => {
      const V = D?.state.rootActivity;
      return !D || !V ? D : {
        ...D,
        state: {
          ...D.state,
          rootActivity: Kl(V, N.nodeId, () => N)
        }
      };
    });
  }, []), Pr = (N) => {
    ie((D) => {
      const V = new Set(D);
      return V.has(N) ? V.delete(N) : V.add(N), V;
    });
  }, xn = (N) => {
    Me((D) => D === N ? null : D), N === "palette" ? pe((D) => !D) : Ae((D) => !D);
  }, po = (N) => {
    N === "palette" ? pe(!1) : Ae(!1), Me((D) => D === N ? null : N);
  }, go = (N, D) => {
    Me(null), N === "palette" ? (pe(!1), ee((V) => Ro(V + D, Nn, Sn))) : (Ae(!1), H((V) => Ro(V + D, En, kn)));
  }, Ft = (N, D) => {
    D.preventDefault(), Me(null), N === "palette" ? pe(!1) : Ae(!1);
    const V = D.clientX, X = N === "palette" ? G : le, J = N === "palette" ? Nn : En, de = N === "palette" ? Sn : kn;
    document.body.classList.add("wf-side-panel-resizing");
    const re = (ke) => {
      const Et = N === "palette" ? ke.clientX - V : V - ke.clientX, De = Ro(X + Et, J, de);
      N === "palette" ? ee(De) : H(De);
    }, fe = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", re), window.removeEventListener("pointerup", fe), window.removeEventListener("pointercancel", fe);
    };
    window.addEventListener("pointermove", re), window.addEventListener("pointerup", fe), window.addEventListener("pointercancel", fe);
  }, yo = (N, D) => {
    D.key === "ArrowLeft" ? (D.preventDefault(), go(N, N === "palette" ? -jo : jo)) : D.key === "ArrowRight" ? (D.preventDefault(), go(N, N === "palette" ? jo : -jo)) : D.key === "Home" ? (D.preventDefault(), N === "palette" ? ee(Nn) : H(En)) : D.key === "End" && (D.preventDefault(), N === "palette" ? ee(Sn) : H(kn));
  };
  if (!s || !d)
    return /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: C || "Loading workflow editor..." });
  const $r = [
    "wf-editor-body",
    U ? "palette-collapsed" : "",
    ge ? "inspector-collapsed" : "",
    ye === "palette" ? "palette-maximized" : "",
    ye === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), mo = {
    "--wf-palette-width": `${U ? Aa : G}px`,
    "--wf-inspector-width": `${ge ? Aa : le}px`
  }, Ji = !U && ye !== "inspector", Qi = !ge && ye !== "palette", xo = B?.draftSignature === st(d) ? B.view : null, Ed = K.filter((N) => N.draftSignature === st(d)).map((N) => N.view), es = xo && z.startsWith("Test run") ? "" : z, kd = {
    definition: s.definition,
    draft: d,
    selectedActivity: Ne,
    selectedActivityDescriptor: Nt,
    selectedActivitySlots: dn,
    catalog: u,
    currentScopeOwner: Le,
    frames: b
  }, ts = r.map((N) => {
    const D = N.component;
    return {
      id: N.id,
      title: N.title,
      side: N.side,
      order: N.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ a.jsx(D, { context: kd })
    };
  }), Tr = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ a.jsx(zl, { size: 15 }),
      render: Id
    },
    ...ts.filter((N) => N.side === "left")
  ].sort(La), zr = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ a.jsx(Vl, { size: 15 }),
      render: Cd
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ a.jsx(Wm, { size: 15 }),
      render: () => /* @__PURE__ */ a.jsx(
        w0,
        {
          context: e,
          ai: n,
          definitionId: s.definition.id,
          publishedArtifactId: L
        }
      )
    },
    {
      id: "test-runs",
      title: "Test runs",
      order: 20,
      icon: /* @__PURE__ */ a.jsx(Ol, { size: 15 }),
      render: () => /* @__PURE__ */ a.jsx(
        v0,
        {
          context: e,
          definitionId: s.definition.id,
          currentRun: xo,
          runs: Ed
        }
      )
    },
    ...ts.filter((N) => N.side === "right")
  ].sort(La), ns = Tr.find((N) => N.id === wt) ?? Tr[0], os = zr.find((N) => N.id === rt) ?? zr[0];
  function Id() {
    return /* @__PURE__ */ a.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: ln.map((N) => {
      const D = ae.has(N.category);
      return /* @__PURE__ */ a.jsxs("div", { className: "wf-palette-category", children: [
        /* @__PURE__ */ a.jsxs(
          "button",
          {
            type: "button",
            className: "wf-palette-category-toggle",
            role: "treeitem",
            "aria-expanded": D,
            onClick: () => Pr(N.category),
            children: [
              D ? /* @__PURE__ */ a.jsx(Rl, { size: 14 }) : /* @__PURE__ */ a.jsx(Wt, { size: 14 }),
              /* @__PURE__ */ a.jsx("span", { children: N.category }),
              /* @__PURE__ */ a.jsx("small", { children: N.activities.length })
            ]
          }
        ),
        D ? /* @__PURE__ */ a.jsx("div", { className: "wf-palette-activities", role: "group", children: N.activities.map((V) => {
          const X = V.description?.trim(), J = X ? `wf-palette-description-${V.activityVersionId}` : void 0, de = Ce(V), re = mi(V);
          return /* @__PURE__ */ a.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-activity",
              role: "treeitem",
              draggable: !0,
              title: X || Ce(V),
              "aria-describedby": J,
              onClick: () => xr(V),
              onDragStart: (fe) => no(fe, V),
              onDragEnd: (fe) => oo(fe, V),
              onPointerDown: (fe) => ro(fe, V),
              children: [
                /* @__PURE__ */ a.jsx("span", { className: "wf-activity-icon", "data-icon": re, "aria-hidden": "true", children: wd(re) }),
                /* @__PURE__ */ a.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ a.jsx("strong", { children: de }),
                  X ? /* @__PURE__ */ a.jsx("small", { id: J, children: X }) : null
                ] }),
                /* @__PURE__ */ a.jsx(Bm, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            V.activityVersionId
          );
        }) }) : null
      ] }, N.category);
    }) });
  }
  function Cd() {
    return Ne ? /* @__PURE__ */ a.jsxs("div", { className: "wf-inspector-content", children: [
      /* @__PURE__ */ a.jsx("h3", { children: x.find((N) => N.id === Ne.nodeId)?.data.label ?? Ne.nodeId }),
      /* @__PURE__ */ a.jsxs("dl", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Node ID" }),
        /* @__PURE__ */ a.jsx("dd", { children: Ne.nodeId }),
        /* @__PURE__ */ a.jsx("dt", { children: "Activity type" }),
        /* @__PURE__ */ a.jsx("dd", { children: Nt?.typeName ?? Qe.get(Ne.activityVersionId)?.activityTypeKey ?? "Unknown" }),
        /* @__PURE__ */ a.jsx("dt", { children: "Activity version" }),
        /* @__PURE__ */ a.jsx("dd", { children: Ne.activityVersionId })
      ] }),
      /* @__PURE__ */ a.jsx(
        Ox,
        {
          activity: Ne,
          descriptor: Nt,
          editors: o,
          expressionDescriptors: y,
          descriptorStatus: w,
          onChange: ho
        }
      ),
      dn.length > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-slot-list", children: [
        /* @__PURE__ */ a.jsx("span", { children: "Embedded slots" }),
        dn.map((N) => /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => Dr(Ne, N.id, `${x.find((D) => D.id === Ne.nodeId)?.data.label ?? Ne.nodeId} / ${N.label}`), children: [
          N.label,
          /* @__PURE__ */ a.jsxs("small", { children: [
            N.activities.length,
            " activit",
            N.activities.length === 1 ? "y" : "ies"
          ] })
        ] }, N.id))
      ] }) : /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
    ] }) : /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
  }
  return /* @__PURE__ */ a.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ a.jsx("button", { type: "button", className: "wf-link-button", onClick: i, children: "Definitions" }),
      /* @__PURE__ */ a.jsx(Wt, { size: 14 }),
      /* @__PURE__ */ a.jsx("strong", { children: s.definition.name }),
      /* @__PURE__ */ a.jsx("span", { className: "wf-chip", children: "Draft" }),
      es ? /* @__PURE__ */ a.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ a.jsx(Rt, { size: 13 }),
        " ",
        es
      ] }) : null,
      /* @__PURE__ */ a.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ a.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ a.jsx("input", { className: "wf-autosave-switch-input", type: "checkbox", checked: se, onChange: (N) => Q(N.target.checked) }),
          /* @__PURE__ */ a.jsx("span", { children: "Autosave" })
        ] }),
        eo ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => mt(n, eo, { definition: s.definition, draft: d }), children: [
          /* @__PURE__ */ a.jsx(yt, { size: 15 }),
          " Risks"
        ] }) : null,
        to ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => mt(n, to, { definition: s.definition, draft: d }), children: [
          /* @__PURE__ */ a.jsx(yt, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ a.jsxs("button", { type: "button", disabled: ut, onClick: () => {
          co();
        }, children: [
          /* @__PURE__ */ a.jsx(Xm, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ a.jsxs("button", { type: "button", disabled: ut, onClick: () => {
          vr();
        }, children: [
          /* @__PURE__ */ a.jsx(Ll, { size: 15 }),
          " Publish"
        ] }),
        xo ? /* @__PURE__ */ a.jsx(
          J0,
          {
            testRun: xo,
            onOpen: () => {
              Ae(!1), Me((N) => N === "palette" ? null : N), $e("test-runs");
            }
          }
        ) : null,
        /* @__PURE__ */ a.jsxs(
          "button",
          {
            type: "button",
            disabled: !yr,
            title: d.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              br();
            },
            children: [
              /* @__PURE__ */ a.jsx(fr, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    C ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(Be, { size: 16 }),
      " ",
      C
    ] }) : null,
    /* @__PURE__ */ a.jsxs("div", { className: $r, style: mo, children: [
      /* @__PURE__ */ a.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ a.jsx(
            Ra,
            {
              label: "Activities panel tabs",
              tabs: Tr,
              activeTabId: ns.id,
              onSelect: ot
            }
          ),
          /* @__PURE__ */ a.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ a.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": U ? "Expand activities panel" : "Collapse activities panel",
                title: U ? "Expand" : "Collapse",
                onClick: () => xn("palette"),
                children: U ? /* @__PURE__ */ a.jsx(Wt, { size: 14 }) : /* @__PURE__ */ a.jsx(Zo, { size: 14 })
              }
            ),
            U ? null : /* @__PURE__ */ a.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": ye === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: ye === "palette" ? "Restore" : "Maximize",
                onClick: () => po("palette"),
                children: ye === "palette" ? /* @__PURE__ */ a.jsx(pa, { size: 14 }) : /* @__PURE__ */ a.jsx(Ko, { size: 14 })
              }
            )
          ] })
        ] }),
        Ji ? ns.render() : null
      ] }),
      Ji && !ye ? /* @__PURE__ */ a.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Nn,
          "aria-valuemax": Sn,
          "aria-valuenow": G,
          tabIndex: 0,
          onPointerDown: (N) => Ft("palette", N),
          onKeyDown: (N) => yo("palette", N)
        }
      ) : /* @__PURE__ */ a.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ a.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-breadcrumb", children: [
          /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
            g([]), k(null);
          }, children: "Root" }),
          b.map((N, D) => /* @__PURE__ */ a.jsxs(xt.Fragment, { children: [
            /* @__PURE__ */ a.jsx(Wt, { size: 13 }),
            /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
              g(b.slice(0, D + 1)), k(null);
            }, children: N.label })
          ] }, `${N.ownerNodeId}-${N.slotId}-${D}`))
        ] }),
        /* @__PURE__ */ a.jsxs("div", { className: "wf-canvas", ref: Ee, onDragOver: io, onDragLeave: so, onDrop: ao, children: [
          /* @__PURE__ */ a.jsx(dd.Provider, { value: Mr, children: /* @__PURE__ */ a.jsxs(
            jl,
            {
              nodes: x,
              edges: E,
              nodeTypes: ad,
              edgeTypes: cd,
              onInit: T,
              onNodesChange: Nr,
              onEdgesChange: Sr,
              onNodesDelete: jr,
              onEdgesDelete: _r,
              onConnect: Er,
              onConnectStart: _e ? Ir : void 0,
              onConnectEnd: _e ? lo : void 0,
              onReconnect: _e ? Cr : void 0,
              isValidConnection: mn,
              onDragOver: io,
              onDragLeave: so,
              onDrop: ao,
              onPaneClick: () => k(null),
              onNodeClick: (N, D) => {
                k(D.id), $e("inspector");
              },
              onNodeDragStop: be ? void 0 : kr,
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
                /* @__PURE__ */ a.jsx(Al, { gap: 18, size: 1 }),
                /* @__PURE__ */ a.jsx(Dl, {}),
                /* @__PURE__ */ a.jsx($l, { pannable: !0, zoomable: !0 })
              ]
            }
          ) }),
          _e && x.length === 0 ? /* @__PURE__ */ a.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => wr(), children: [
            /* @__PURE__ */ a.jsx(Hi, { size: 15 }),
            " Add activity"
          ] }) : null,
          R ? /* @__PURE__ */ a.jsx(
            K0,
            {
              clientX: R.clientX,
              clientY: R.clientY,
              activities: u,
              onPick: Ar,
              onClose: () => O(null)
            }
          ) : null
        ] }),
        /* @__PURE__ */ a.jsx(G0, { draft: d })
      ] }),
      Qi && !ye ? /* @__PURE__ */ a.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": En,
          "aria-valuemax": kn,
          "aria-valuenow": le,
          tabIndex: 0,
          onPointerDown: (N) => Ft("inspector", N),
          onKeyDown: (N) => yo("inspector", N)
        }
      ) : /* @__PURE__ */ a.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ a.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ a.jsx(
            Ra,
            {
              label: "Inspector panel tabs",
              tabs: zr,
              activeTabId: os.id,
              onSelect: $e
            }
          ),
          /* @__PURE__ */ a.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ a.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": ge ? "Expand inspector panel" : "Collapse inspector panel",
                title: ge ? "Expand" : "Collapse",
                onClick: () => xn("inspector"),
                children: ge ? /* @__PURE__ */ a.jsx(Zo, { size: 14 }) : /* @__PURE__ */ a.jsx(Wt, { size: 14 })
              }
            ),
            ge ? null : /* @__PURE__ */ a.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": ye === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: ye === "inspector" ? "Restore" : "Maximize",
                onClick: () => po("inspector"),
                children: ye === "inspector" ? /* @__PURE__ */ a.jsx(pa, { size: 14 }) : /* @__PURE__ */ a.jsx(Ko, { size: 14 })
              }
            )
          ] })
        ] }),
        Qi ? os.render() : null
      ] })
    ] })
  ] });
}
function Ra({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: o
}) {
  return /* @__PURE__ */ a.jsx("div", { className: "wf-panel-tab-list", role: "tablist", "aria-label": e, children: t.map((r) => /* @__PURE__ */ a.jsxs(
    "button",
    {
      type: "button",
      role: "tab",
      "aria-selected": r.id === n,
      className: r.id === n ? "active" : "",
      title: r.title,
      onClick: () => o(r.id),
      children: [
        r.icon ? /* @__PURE__ */ a.jsx("span", { className: "wf-panel-tab-icon", "aria-hidden": "true", children: r.icon }) : null,
        /* @__PURE__ */ a.jsx("span", { children: r.title })
      ]
    },
    r.id
  )) });
}
function La(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
function q0({ data: e, selected: t }) {
  const n = e, o = n.runtime, r = !n.suppressFlowPorts, i = r ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], s = U0(n);
  return /* @__PURE__ */ a.jsxs(
    "div",
    {
      className: ["wf-node", t ? "selected" : "", o ? "wf-node-runtime" : "", o?.hasBlockingIncident ? "faulted" : ""].filter(Boolean).join(" "),
      "data-icon": n.icon ?? "activity",
      children: [
        r && n.acceptsInbound ? /* @__PURE__ */ a.jsx(on, { type: "target", position: te.Left }) : null,
        /* @__PURE__ */ a.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ a.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: wd(n.icon) }),
          /* @__PURE__ */ a.jsxs("span", { className: "wf-node-copy", children: [
            /* @__PURE__ */ a.jsx("strong", { children: n.label }),
            s ? /* @__PURE__ */ a.jsx("small", { children: s }) : null
          ] })
        ] }),
        n.childSlots.length > 0 ? /* @__PURE__ */ a.jsxs("span", { className: "wf-node-slot-badge", children: [
          n.childSlots.length,
          " slot",
          n.childSlots.length === 1 ? "" : "s"
        ] }) : null,
        o ? /* @__PURE__ */ a.jsxs("div", { className: "wf-node-runtime-strip", children: [
          o.status ? /* @__PURE__ */ a.jsx(sn, { status: o.status, subStatus: o.subStatus }) : null,
          o.incidentCount > 0 ? /* @__PURE__ */ a.jsxs("span", { className: "wf-node-runtime-count", children: [
            o.incidentCount,
            " incident",
            o.incidentCount === 1 ? "" : "s"
          ] }) : null,
          o.faultCount > 0 ? /* @__PURE__ */ a.jsxs("span", { className: "wf-node-runtime-count", children: [
            o.faultCount,
            " faults"
          ] }) : null
        ] }) : null,
        i.map((c, d) => {
          const l = `${(d + 1) / (i.length + 1) * 100}%`;
          return /* @__PURE__ */ a.jsxs(xt.Fragment, { children: [
            /* @__PURE__ */ a.jsx("span", { className: "wf-node-port-label", style: { top: l }, children: c.displayName }),
            /* @__PURE__ */ a.jsx(on, { type: "source", position: te.Right, id: c.name, style: { top: l } })
          ] }, c.name);
        })
      ]
    }
  );
}
function U0(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((r) => !!r).join(" · ");
}
function wd(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ a.jsx(Ll, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ a.jsx(Vl, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ a.jsx(Ol, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ a.jsx(fr, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ a.jsx(Ym, { size: 15 });
    default:
      return /* @__PURE__ */ a.jsx(zl, { size: 15 });
  }
}
function Z0(e) {
  const {
    id: t,
    sourceX: n,
    sourceY: o,
    targetX: r,
    targetY: i,
    sourcePosition: s,
    targetPosition: c,
    markerEnd: d,
    style: l,
    label: u,
    labelStyle: f
  } = e, h = xt.useContext(dd), [p, y] = Y(!1), [v, w, m] = qo({ sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: c }), b = h?.highlightedEdgeId === t;
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsx(
      Kn,
      {
        id: t,
        path: v,
        markerEnd: d,
        style: {
          ...l,
          strokeWidth: b ? 2.5 : l?.strokeWidth
        },
        label: u,
        labelX: w,
        labelY: m,
        labelStyle: f,
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1)
      }
    ),
    h ? /* @__PURE__ */ a.jsx(hm, { children: /* @__PURE__ */ a.jsxs(
      "div",
      {
        className: ["wf-edge-actions", p ? "visible" : "", b ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${w}px, ${m}px)` },
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1),
        children: [
          /* @__PURE__ */ a.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (g) => h.requestInsertActivity(t, g.clientX, g.clientY), children: /* @__PURE__ */ a.jsx(Hi, { size: 12 }) }),
          /* @__PURE__ */ a.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => h.deleteEdge(t), children: /* @__PURE__ */ a.jsx(Fn, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function K0({ clientX: e, clientY: t, activities: n, onPick: o, onClose: r }) {
  const [i, s] = Y(""), [c, d] = Y(0), l = ce(null), u = ce(null), f = me(() => {
    const b = i.trim().toLowerCase(), g = n.filter(yd);
    return b ? g.filter((x) => Ce(x).toLowerCase().includes(b) || x.activityTypeKey.toLowerCase().includes(b) || (x.category ?? "").toLowerCase().includes(b) || (x.description ?? "").toLowerCase().includes(b)) : g;
  }, [n, i]), h = me(() => hd(f), [f]), p = me(() => h.flatMap((b) => b.activities), [h]);
  oe(() => {
    requestAnimationFrame(() => u.current?.focus());
  }, []), oe(() => {
    const b = (x) => {
      l.current?.contains(x.target) || r();
    }, g = (x) => {
      x.key === "Escape" && r();
    };
    return document.addEventListener("mousedown", b, !0), document.addEventListener("keydown", g), () => {
      document.removeEventListener("mousedown", b, !0), document.removeEventListener("keydown", g);
    };
  }, [r]);
  const y = (b) => {
    if (b.key === "ArrowDown")
      b.preventDefault(), d((g) => Math.min(g + 1, p.length - 1));
    else if (b.key === "ArrowUp")
      b.preventDefault(), d((g) => Math.max(g - 1, 0));
    else if (b.key === "Enter") {
      b.preventDefault();
      const g = p[c];
      g && o(g);
    }
  }, v = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), w = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let m = -1;
  return /* @__PURE__ */ a.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: v, top: w }, onMouseDown: (b) => b.stopPropagation(), onClick: (b) => b.stopPropagation(), children: [
    /* @__PURE__ */ a.jsx(
      "input",
      {
        ref: u,
        type: "search",
        value: i,
        placeholder: "Search activities...",
        "aria-label": "Search activities",
        onChange: (b) => {
          s(b.target.value), d(0);
        },
        onKeyDown: y
      }
    ),
    /* @__PURE__ */ a.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: h.length === 0 ? /* @__PURE__ */ a.jsx("p", { children: "No matching activities." }) : h.map((b) => /* @__PURE__ */ a.jsxs("section", { children: [
      /* @__PURE__ */ a.jsx("h4", { children: b.category }),
      b.activities.map((g) => {
        m += 1;
        const x = m, I = x === c;
        return /* @__PURE__ */ a.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": I,
            className: I ? "active" : "",
            onMouseEnter: () => d(x),
            onClick: () => o(g),
            children: [
              /* @__PURE__ */ a.jsx("strong", { children: Ce(g) }),
              /* @__PURE__ */ a.jsx("small", { children: g.category || g.activityTypeKey })
            ]
          },
          g.activityVersionId
        );
      })
    ] }, b.category)) })
  ] });
}
function G0({ draft: e }) {
  return e.validationErrors.length ? /* @__PURE__ */ a.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ a.jsx(Be, { size: 14 }),
    e.validationErrors.length,
    " validation issue",
    e.validationErrors.length === 1 ? "" : "s"
  ] }) : /* @__PURE__ */ a.jsxs("div", { className: "wf-validation ok", children: [
    /* @__PURE__ */ a.jsx(Rt, { size: 14 }),
    " No validation errors"
  ] });
}
function J0({ testRun: e, onOpen: t }) {
  const n = Gi(e);
  return /* @__PURE__ */ a.jsx("div", { className: "wf-test-run-status", "data-state": n ? "rejected" : "accepted", children: /* @__PURE__ */ a.jsxs(
    "button",
    {
      type: "button",
      className: "wf-test-run-trigger",
      "aria-controls": "wf-test-runs-panel",
      onClick: t,
      children: [
        n ? /* @__PURE__ */ a.jsx(Be, { size: 16 }) : /* @__PURE__ */ a.jsx(Rt, { size: 16 }),
        n ? "Test run rejected" : "Test run dispatched",
        /* @__PURE__ */ a.jsx(Rl, { size: 14 })
      ]
    }
  ) });
}
function Va(e) {
  return `${Ce(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function Ha(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function Q0(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function vd(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function ew(e) {
  const t = vd(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function tw(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function st(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function nw(e) {
  return Nd(st(e));
}
function bd(e, t, n = []) {
  if (!e) return n;
  const o = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: o?.activityTypeKey ?? e.activityVersionId,
    displayName: o ? Ce(o) : void 0
  });
  for (const r of Ke(e))
    for (const i of r.activities) bd(i, t, n);
  return n;
}
function ow(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function rw(e) {
  return `${e.id}-${Nd(JSON.stringify(e.state))}`;
}
function Nd(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function Gi(e) {
  return e.status.toLowerCase() === "rejected";
}
function Ge(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function iw(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function Sd(e, t) {
  if (!e || !t) return "";
  const n = Date.parse(e), o = Date.parse(t);
  if (Number.isNaN(n) || Number.isNaN(o) || o < n) return "";
  const r = Math.round((o - n) / 1e3);
  if (r < 60) return `${r}s`;
  const i = Math.floor(r / 60), s = r % 60;
  if (i < 60) return s ? `${i}m ${s}s` : `${i}m`;
  const c = Math.floor(i / 60), d = i % 60;
  return d ? `${c}h ${d}m` : `${c}h`;
}
export {
  ew as isConnectEndOverExistingWorkflowNode,
  cw as register,
  tw as resolveConnectEndSource
};
