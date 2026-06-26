import wt, { memo as Se, forwardRef as er, useRef as ce, useEffect as oe, useCallback as pe, useContext as Bn, useMemo as ge, useState as Y, createContext as bi, useLayoutEffect as Md, createElement as ei, useId as Oa } from "react";
import "@tanstack/react-query";
function Dd(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var Rr = { exports: {} }, xn = {};
var is;
function Pd() {
  if (is) return xn;
  is = 1;
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
  return xn.Fragment = t, xn.jsx = n, xn.jsxs = n, xn;
}
var ss;
function $d() {
  return ss || (ss = 1, Rr.exports = Pd()), Rr.exports;
}
var a = $d();
function Ce(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, o; n < e.length; n++)
      (o = Ce(e[n])) !== "" && (t += (t && " ") + o);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var Td = { value: () => {
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
function zd(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
Ao.prototype = tr.prototype = {
  constructor: Ao,
  on: function(e, t) {
    var n = this._, o = zd(e + "", n), r, i = -1, s = o.length;
    if (arguments.length < 2) {
      for (; ++i < s; ) if ((r = (e = o[i]).type) && (r = Rd(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++i < s; )
      if (r = (e = o[i]).type) n[r] = as(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = as(n[r], e.name, null);
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
function Rd(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function as(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = Td, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var ti = "http://www.w3.org/1999/xhtml";
const cs = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: ti,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function nr(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), cs.hasOwnProperty(t) ? { space: cs[t], local: e } : e;
}
function Ld(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === ti && t.documentElement.namespaceURI === ti ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Vd(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function Fa(e) {
  var t = nr(e);
  return (t.local ? Vd : Ld)(t);
}
function Hd() {
}
function Ni(e) {
  return e == null ? Hd : function() {
    return this.querySelector(e);
  };
}
function Od(e) {
  typeof e != "function" && (e = Ni(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = new Array(s), d, l, f = 0; f < s; ++f)
      (d = i[f]) && (l = e.call(d, d.__data__, f, i)) && ("__data__" in d && (l.__data__ = d.__data__), c[f] = l);
  return new Oe(o, this._parents);
}
function Fd(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function Bd() {
  return [];
}
function Ba(e) {
  return e == null ? Bd : function() {
    return this.querySelectorAll(e);
  };
}
function Wd(e) {
  return function() {
    return Fd(e.apply(this, arguments));
  };
}
function Xd(e) {
  typeof e == "function" ? e = Wd(e) : e = Ba(e);
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
var Yd = Array.prototype.find;
function qd(e) {
  return function() {
    return Yd.call(this.children, e);
  };
}
function Ud() {
  return this.firstElementChild;
}
function Zd(e) {
  return this.select(e == null ? Ud : qd(typeof e == "function" ? e : Xa(e)));
}
var Kd = Array.prototype.filter;
function Gd() {
  return Array.from(this.children);
}
function Jd(e) {
  return function() {
    return Kd.call(this.children, e);
  };
}
function Qd(e) {
  return this.selectAll(e == null ? Gd : Jd(typeof e == "function" ? e : Xa(e)));
}
function eu(e) {
  typeof e != "function" && (e = Wa(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = [], d, l = 0; l < s; ++l)
      (d = i[l]) && e.call(d, d.__data__, l, i) && c.push(d);
  return new Oe(o, this._parents);
}
function Ya(e) {
  return new Array(e.length);
}
function tu() {
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
function nu(e) {
  return function() {
    return e;
  };
}
function ou(e, t, n, o, r, i) {
  for (var s = 0, c, d = t.length, l = i.length; s < l; ++s)
    (c = t[s]) ? (c.__data__ = i[s], o[s] = c) : n[s] = new Lo(e, i[s]);
  for (; s < d; ++s)
    (c = t[s]) && (r[s] = c);
}
function ru(e, t, n, o, r, i, s) {
  var c, d, l = /* @__PURE__ */ new Map(), f = t.length, u = i.length, h = new Array(f), p;
  for (c = 0; c < f; ++c)
    (d = t[c]) && (h[c] = p = s.call(d, d.__data__, c, t) + "", l.has(p) ? r[c] = d : l.set(p, d));
  for (c = 0; c < u; ++c)
    p = s.call(e, i[c], c, i) + "", (d = l.get(p)) ? (o[c] = d, d.__data__ = i[c], l.delete(p)) : n[c] = new Lo(e, i[c]);
  for (c = 0; c < f; ++c)
    (d = t[c]) && l.get(h[c]) === d && (r[c] = d);
}
function iu(e) {
  return e.__data__;
}
function su(e, t) {
  if (!arguments.length) return Array.from(this, iu);
  var n = t ? ru : ou, o = this._parents, r = this._groups;
  typeof e != "function" && (e = nu(e));
  for (var i = r.length, s = new Array(i), c = new Array(i), d = new Array(i), l = 0; l < i; ++l) {
    var f = o[l], u = r[l], h = u.length, p = au(e.call(f, f && f.__data__, l, o)), y = p.length, v = c[l] = new Array(y), x = s[l] = new Array(y), m = d[l] = new Array(h);
    n(f, u, v, x, m, p, t);
    for (var S = 0, g = 0, w, E; S < y; ++S)
      if (w = v[S]) {
        for (S >= g && (g = S + 1); !(E = x[g]) && ++g < y; ) ;
        w._next = E || null;
      }
  }
  return s = new Oe(s, o), s._enter = c, s._exit = d, s;
}
function au(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function cu() {
  return new Oe(this._exit || this._groups.map(Ya), this._parents);
}
function lu(e, t, n) {
  var o = this.enter(), r = this, i = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? i.remove() : n(i), o && r ? o.merge(r).order() : r;
}
function du(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, i = o.length, s = Math.min(r, i), c = new Array(r), d = 0; d < s; ++d)
    for (var l = n[d], f = o[d], u = l.length, h = c[d] = new Array(u), p, y = 0; y < u; ++y)
      (p = l[y] || f[y]) && (h[y] = p);
  for (; d < r; ++d)
    c[d] = n[d];
  return new Oe(c, this._parents);
}
function uu() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, i = o[r], s; --r >= 0; )
      (s = o[r]) && (i && s.compareDocumentPosition(i) ^ 4 && i.parentNode.insertBefore(s, i), i = s);
  return this;
}
function fu(e) {
  e || (e = hu);
  function t(u, h) {
    return u && h ? e(u.__data__, h.__data__) : !u - !h;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), i = 0; i < o; ++i) {
    for (var s = n[i], c = s.length, d = r[i] = new Array(c), l, f = 0; f < c; ++f)
      (l = s[f]) && (d[f] = l);
    d.sort(t);
  }
  return new Oe(r, this._parents).order();
}
function hu(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function pu() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function gu() {
  return Array.from(this);
}
function yu() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length; r < i; ++r) {
      var s = o[r];
      if (s) return s;
    }
  return null;
}
function mu() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function xu() {
  return !this.node();
}
function wu(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], i = 0, s = r.length, c; i < s; ++i)
      (c = r[i]) && e.call(c, c.__data__, i, r);
  return this;
}
function vu(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function bu(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Nu(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function Su(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function Eu(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function ku(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function ju(e, t) {
  var n = nr(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? bu : vu : typeof t == "function" ? n.local ? ku : Eu : n.local ? Su : Nu)(n, t));
}
function qa(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function Cu(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Iu(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function _u(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function Au(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? Cu : typeof t == "function" ? _u : Iu)(e, t, n ?? "")) : Gt(this.node(), e);
}
function Gt(e, t) {
  return e.style.getPropertyValue(t) || qa(e).getComputedStyle(e, null).getPropertyValue(t);
}
function Mu(e) {
  return function() {
    delete this[e];
  };
}
function Du(e, t) {
  return function() {
    this[e] = t;
  };
}
function Pu(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function $u(e, t) {
  return arguments.length > 1 ? this.each((t == null ? Mu : typeof t == "function" ? Pu : Du)(e, t)) : this.node()[e];
}
function Ua(e) {
  return e.trim().split(/^|\s+/);
}
function Si(e) {
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
  for (var n = Si(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function Ga(e, t) {
  for (var n = Si(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function Tu(e) {
  return function() {
    Ka(this, e);
  };
}
function zu(e) {
  return function() {
    Ga(this, e);
  };
}
function Ru(e, t) {
  return function() {
    (t.apply(this, arguments) ? Ka : Ga)(this, e);
  };
}
function Lu(e, t) {
  var n = Ua(e + "");
  if (arguments.length < 2) {
    for (var o = Si(this.node()), r = -1, i = n.length; ++r < i; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? Ru : t ? Tu : zu)(n, t));
}
function Vu() {
  this.textContent = "";
}
function Hu(e) {
  return function() {
    this.textContent = e;
  };
}
function Ou(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function Fu(e) {
  return arguments.length ? this.each(e == null ? Vu : (typeof e == "function" ? Ou : Hu)(e)) : this.node().textContent;
}
function Bu() {
  this.innerHTML = "";
}
function Wu(e) {
  return function() {
    this.innerHTML = e;
  };
}
function Xu(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function Yu(e) {
  return arguments.length ? this.each(e == null ? Bu : (typeof e == "function" ? Xu : Wu)(e)) : this.node().innerHTML;
}
function qu() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function Uu() {
  return this.each(qu);
}
function Zu() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Ku() {
  return this.each(Zu);
}
function Gu(e) {
  var t = typeof e == "function" ? e : Fa(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Ju() {
  return null;
}
function Qu(e, t) {
  var n = typeof e == "function" ? e : Fa(e), o = t == null ? Ju : typeof t == "function" ? t : Ni(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function ef() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function tf() {
  return this.each(ef);
}
function nf() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function of() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function rf(e) {
  return this.select(e ? of : nf);
}
function sf(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function af(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function cf(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function lf(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, i; n < r; ++n)
        i = t[n], (!e.type || i.type === e.type) && i.name === e.name ? this.removeEventListener(i.type, i.listener, i.options) : t[++o] = i;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function df(e, t, n) {
  return function() {
    var o = this.__on, r, i = af(t);
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
function uf(e, t, n) {
  var o = cf(e + ""), r, i = o.length, s;
  if (arguments.length < 2) {
    var c = this.node().__on;
    if (c) {
      for (var d = 0, l = c.length, f; d < l; ++d)
        for (r = 0, f = c[d]; r < i; ++r)
          if ((s = o[r]).type === f.type && s.name === f.name)
            return f.value;
    }
    return;
  }
  for (c = t ? df : lf, r = 0; r < i; ++r) this.each(c(o[r], t, n));
  return this;
}
function Ja(e, t, n) {
  var o = qa(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function ff(e, t) {
  return function() {
    return Ja(this, e, t);
  };
}
function hf(e, t) {
  return function() {
    return Ja(this, e, t.apply(this, arguments));
  };
}
function pf(e, t) {
  return this.each((typeof t == "function" ? hf : ff)(e, t));
}
function* gf() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length, s; r < i; ++r)
      (s = o[r]) && (yield s);
}
var Qa = [null];
function Oe(e, t) {
  this._groups = e, this._parents = t;
}
function Wn() {
  return new Oe([[document.documentElement]], Qa);
}
function yf() {
  return this;
}
Oe.prototype = Wn.prototype = {
  constructor: Oe,
  select: Od,
  selectAll: Xd,
  selectChild: Zd,
  selectChildren: Qd,
  filter: eu,
  data: su,
  enter: tu,
  exit: cu,
  join: lu,
  merge: du,
  selection: yf,
  order: uu,
  sort: fu,
  call: pu,
  nodes: gu,
  node: yu,
  size: mu,
  empty: xu,
  each: wu,
  attr: ju,
  style: Au,
  property: $u,
  classed: Lu,
  text: Fu,
  html: Yu,
  raise: Uu,
  lower: Ku,
  append: Gu,
  insert: Qu,
  remove: tf,
  clone: rf,
  datum: sf,
  on: uf,
  dispatch: pf,
  [Symbol.iterator]: gf
};
function He(e) {
  return typeof e == "string" ? new Oe([[document.querySelector(e)]], [document.documentElement]) : new Oe([[e]], Qa);
}
function mf(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function qe(e, t) {
  if (e = mf(e), t === void 0 && (t = e.currentTarget), t) {
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
const xf = { passive: !1 }, Dn = { capture: !0, passive: !1 };
function Lr(e) {
  e.stopImmediatePropagation();
}
function Ut(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function ec(e) {
  var t = e.document.documentElement, n = He(e).on("dragstart.drag", Ut, Dn);
  "onselectstart" in t ? n.on("selectstart.drag", Ut, Dn) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function tc(e, t) {
  var n = e.document.documentElement, o = He(e).on("dragstart.drag", null);
  t && (o.on("click.drag", Ut, Dn), setTimeout(function() {
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
  dispatch: f
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
    _: { value: f }
  });
}
ni.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function wf(e) {
  return !e.ctrlKey && !e.button;
}
function vf() {
  return this.parentNode;
}
function bf(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function Nf() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function nc() {
  var e = wf, t = vf, n = bf, o = Nf, r = {}, i = tr("start", "drag", "end"), s = 0, c, d, l, f, u = 0;
  function h(w) {
    w.on("mousedown.drag", p).filter(o).on("touchstart.drag", x).on("touchmove.drag", m, xf).on("touchend.drag touchcancel.drag", S).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function p(w, E) {
    if (!(f || !e.call(this, w, E))) {
      var k = g(this, t.call(this, w, E), w, E, "mouse");
      k && (He(w.view).on("mousemove.drag", y, Dn).on("mouseup.drag", v, Dn), ec(w.view), Lr(w), l = !1, c = w.clientX, d = w.clientY, k("start", w));
    }
  }
  function y(w) {
    if (Ut(w), !l) {
      var E = w.clientX - c, k = w.clientY - d;
      l = E * E + k * k > u;
    }
    r.mouse("drag", w);
  }
  function v(w) {
    He(w.view).on("mousemove.drag mouseup.drag", null), tc(w.view, l), Ut(w), r.mouse("end", w);
  }
  function x(w, E) {
    if (e.call(this, w, E)) {
      var k = w.changedTouches, j = t.call(this, w, E), A = k.length, $, D;
      for ($ = 0; $ < A; ++$)
        (D = g(this, j, w, E, k[$].identifier, k[$])) && (Lr(w), D("start", w, k[$]));
    }
  }
  function m(w) {
    var E = w.changedTouches, k = E.length, j, A;
    for (j = 0; j < k; ++j)
      (A = r[E[j].identifier]) && (Ut(w), A("drag", w, E[j]));
  }
  function S(w) {
    var E = w.changedTouches, k = E.length, j, A;
    for (f && clearTimeout(f), f = setTimeout(function() {
      f = null;
    }, 500), j = 0; j < k; ++j)
      (A = r[E[j].identifier]) && (Lr(w), A("end", w, E[j]));
  }
  function g(w, E, k, j, A, $) {
    var D = i.copy(), C = qe($ || k, E), R, F, N;
    if ((N = n.call(w, new ni("beforestart", {
      sourceEvent: k,
      target: h,
      identifier: A,
      active: s,
      x: C[0],
      y: C[1],
      dx: 0,
      dy: 0,
      dispatch: D
    }), j)) != null)
      return R = N.x - C[0] || 0, F = N.y - C[1] || 0, function _(I, P, T) {
        var z = C, O;
        switch (I) {
          case "start":
            r[A] = _, O = s++;
            break;
          case "end":
            delete r[A], --s;
          // falls through
          case "drag":
            C = qe(T || P, E), O = s;
            break;
        }
        D.call(
          I,
          w,
          new ni(I, {
            sourceEvent: P,
            subject: N,
            target: h,
            identifier: A,
            active: O,
            x: C[0] + R,
            y: C[1] + F,
            dx: C[0] - z[0],
            dy: C[1] - z[1],
            dispatch: D
          }),
          j
        );
      };
  }
  return h.filter = function(w) {
    return arguments.length ? (e = typeof w == "function" ? w : wo(!!w), h) : e;
  }, h.container = function(w) {
    return arguments.length ? (t = typeof w == "function" ? w : wo(w), h) : t;
  }, h.subject = function(w) {
    return arguments.length ? (n = typeof w == "function" ? w : wo(w), h) : n;
  }, h.touchable = function(w) {
    return arguments.length ? (o = typeof w == "function" ? w : wo(!!w), h) : o;
  }, h.on = function() {
    var w = i.on.apply(i, arguments);
    return w === i ? h : w;
  }, h.clickDistance = function(w) {
    return arguments.length ? (u = (w = +w) * w, h) : Math.sqrt(u);
  }, h;
}
function Ei(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function oc(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function Xn() {
}
var Pn = 0.7, Vo = 1 / Pn, Zt = "\\s*([+-]?\\d+)\\s*", $n = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", nt = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", Sf = /^#([0-9a-f]{3,8})$/, Ef = new RegExp(`^rgb\\(${Zt},${Zt},${Zt}\\)$`), kf = new RegExp(`^rgb\\(${nt},${nt},${nt}\\)$`), jf = new RegExp(`^rgba\\(${Zt},${Zt},${Zt},${$n}\\)$`), Cf = new RegExp(`^rgba\\(${nt},${nt},${nt},${$n}\\)$`), If = new RegExp(`^hsl\\(${$n},${nt},${nt}\\)$`), _f = new RegExp(`^hsla\\(${$n},${nt},${nt},${$n}\\)$`), ls = {
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
Ei(Xn, Dt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: ds,
  // Deprecated! Use color.formatHex.
  formatHex: ds,
  formatHex8: Af,
  formatHsl: Mf,
  formatRgb: us,
  toString: us
});
function ds() {
  return this.rgb().formatHex();
}
function Af() {
  return this.rgb().formatHex8();
}
function Mf() {
  return rc(this).formatHsl();
}
function us() {
  return this.rgb().formatRgb();
}
function Dt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = Sf.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? fs(t) : n === 3 ? new Te(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? vo(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? vo(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = Ef.exec(e)) ? new Te(t[1], t[2], t[3], 1) : (t = kf.exec(e)) ? new Te(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = jf.exec(e)) ? vo(t[1], t[2], t[3], t[4]) : (t = Cf.exec(e)) ? vo(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = If.exec(e)) ? gs(t[1], t[2] / 100, t[3] / 100, 1) : (t = _f.exec(e)) ? gs(t[1], t[2] / 100, t[3] / 100, t[4]) : ls.hasOwnProperty(e) ? fs(ls[e]) : e === "transparent" ? new Te(NaN, NaN, NaN, 0) : null;
}
function fs(e) {
  return new Te(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function vo(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new Te(e, t, n, o);
}
function Df(e) {
  return e instanceof Xn || (e = Dt(e)), e ? (e = e.rgb(), new Te(e.r, e.g, e.b, e.opacity)) : new Te();
}
function oi(e, t, n, o) {
  return arguments.length === 1 ? Df(e) : new Te(e, t, n, o ?? 1);
}
function Te(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
Ei(Te, oi, oc(Xn, {
  brighter(e) {
    return e = e == null ? Vo : Math.pow(Vo, e), new Te(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Pn : Math.pow(Pn, e), new Te(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new Te(_t(this.r), _t(this.g), _t(this.b), Ho(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: hs,
  // Deprecated! Use color.formatHex.
  formatHex: hs,
  formatHex8: Pf,
  formatRgb: ps,
  toString: ps
}));
function hs() {
  return `#${It(this.r)}${It(this.g)}${It(this.b)}`;
}
function Pf() {
  return `#${It(this.r)}${It(this.g)}${It(this.b)}${It((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function ps() {
  const e = Ho(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${_t(this.r)}, ${_t(this.g)}, ${_t(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Ho(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function _t(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function It(e) {
  return e = _t(e), (e < 16 ? "0" : "") + e.toString(16);
}
function gs(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Ue(e, t, n, o);
}
function rc(e) {
  if (e instanceof Ue) return new Ue(e.h, e.s, e.l, e.opacity);
  if (e instanceof Xn || (e = Dt(e)), !e) return new Ue();
  if (e instanceof Ue) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), i = Math.max(t, n, o), s = NaN, c = i - r, d = (i + r) / 2;
  return c ? (t === i ? s = (n - o) / c + (n < o) * 6 : n === i ? s = (o - t) / c + 2 : s = (t - n) / c + 4, c /= d < 0.5 ? i + r : 2 - i - r, s *= 60) : c = d > 0 && d < 1 ? 0 : s, new Ue(s, c, d, e.opacity);
}
function $f(e, t, n, o) {
  return arguments.length === 1 ? rc(e) : new Ue(e, t, n, o ?? 1);
}
function Ue(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
Ei(Ue, $f, oc(Xn, {
  brighter(e) {
    return e = e == null ? Vo : Math.pow(Vo, e), new Ue(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Pn : Math.pow(Pn, e), new Ue(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new Te(
      Vr(e >= 240 ? e - 240 : e + 120, r, o),
      Vr(e, r, o),
      Vr(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new Ue(ys(this.h), bo(this.s), bo(this.l), Ho(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Ho(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${ys(this.h)}, ${bo(this.s) * 100}%, ${bo(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function ys(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function bo(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function Vr(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const ki = (e) => () => e;
function Tf(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function zf(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function Rf(e) {
  return (e = +e) == 1 ? ic : function(t, n) {
    return n - t ? zf(t, n, e) : ki(isNaN(t) ? n : t);
  };
}
function ic(e, t) {
  var n = t - e;
  return n ? Tf(e, n) : ki(isNaN(e) ? t : e);
}
const Oo = (function e(t) {
  var n = Rf(t);
  function o(r, i) {
    var s = n((r = oi(r)).r, (i = oi(i)).r), c = n(r.g, i.g), d = n(r.b, i.b), l = ic(r.opacity, i.opacity);
    return function(f) {
      return r.r = s(f), r.g = c(f), r.b = d(f), r.opacity = l(f), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function Lf(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(i) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - i) + t[r] * i;
    return o;
  };
}
function Vf(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Hf(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), i = new Array(n), s;
  for (s = 0; s < o; ++s) r[s] = _n(e[s], t[s]);
  for (; s < n; ++s) i[s] = t[s];
  return function(c) {
    for (s = 0; s < o; ++s) i[s] = r[s](c);
    return i;
  };
}
function Of(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(o) {
    return n.setTime(e * (1 - o) + t * o), n;
  };
}
function tt(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function Ff(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = _n(e[r], t[r]) : o[r] = t[r];
  return function(i) {
    for (r in n) o[r] = n[r](i);
    return o;
  };
}
var ri = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, Hr = new RegExp(ri.source, "g");
function Bf(e) {
  return function() {
    return e;
  };
}
function Wf(e) {
  return function(t) {
    return e(t) + "";
  };
}
function sc(e, t) {
  var n = ri.lastIndex = Hr.lastIndex = 0, o, r, i, s = -1, c = [], d = [];
  for (e = e + "", t = t + ""; (o = ri.exec(e)) && (r = Hr.exec(t)); )
    (i = r.index) > n && (i = t.slice(n, i), c[s] ? c[s] += i : c[++s] = i), (o = o[0]) === (r = r[0]) ? c[s] ? c[s] += r : c[++s] = r : (c[++s] = null, d.push({ i: s, x: tt(o, r) })), n = Hr.lastIndex;
  return n < t.length && (i = t.slice(n), c[s] ? c[s] += i : c[++s] = i), c.length < 2 ? d[0] ? Wf(d[0].x) : Bf(t) : (t = d.length, function(l) {
    for (var f = 0, u; f < t; ++f) c[(u = d[f]).i] = u.x(l);
    return c.join("");
  });
}
function _n(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? ki(t) : (n === "number" ? tt : n === "string" ? (o = Dt(t)) ? (t = o, Oo) : sc : t instanceof Dt ? Oo : t instanceof Date ? Of : Vf(t) ? Lf : Array.isArray(t) ? Hf : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Ff : tt)(e, t);
}
var ms = 180 / Math.PI, ii = {
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
    rotate: Math.atan2(t, e) * ms,
    skewX: Math.atan(d) * ms,
    scaleX: s,
    scaleY: c
  };
}
var No;
function Xf(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? ii : ac(t.a, t.b, t.c, t.d, t.e, t.f);
}
function Yf(e) {
  return e == null || (No || (No = document.createElementNS("http://www.w3.org/2000/svg", "g")), No.setAttribute("transform", e), !(e = No.transform.baseVal.consolidate())) ? ii : (e = e.matrix, ac(e.a, e.b, e.c, e.d, e.e, e.f));
}
function cc(e, t, n, o) {
  function r(l) {
    return l.length ? l.pop() + " " : "";
  }
  function i(l, f, u, h, p, y) {
    if (l !== u || f !== h) {
      var v = p.push("translate(", null, t, null, n);
      y.push({ i: v - 4, x: tt(l, u) }, { i: v - 2, x: tt(f, h) });
    } else (u || h) && p.push("translate(" + u + t + h + n);
  }
  function s(l, f, u, h) {
    l !== f ? (l - f > 180 ? f += 360 : f - l > 180 && (l += 360), h.push({ i: u.push(r(u) + "rotate(", null, o) - 2, x: tt(l, f) })) : f && u.push(r(u) + "rotate(" + f + o);
  }
  function c(l, f, u, h) {
    l !== f ? h.push({ i: u.push(r(u) + "skewX(", null, o) - 2, x: tt(l, f) }) : f && u.push(r(u) + "skewX(" + f + o);
  }
  function d(l, f, u, h, p, y) {
    if (l !== u || f !== h) {
      var v = p.push(r(p) + "scale(", null, ",", null, ")");
      y.push({ i: v - 4, x: tt(l, u) }, { i: v - 2, x: tt(f, h) });
    } else (u !== 1 || h !== 1) && p.push(r(p) + "scale(" + u + "," + h + ")");
  }
  return function(l, f) {
    var u = [], h = [];
    return l = e(l), f = e(f), i(l.translateX, l.translateY, f.translateX, f.translateY, u, h), s(l.rotate, f.rotate, u, h), c(l.skewX, f.skewX, u, h), d(l.scaleX, l.scaleY, f.scaleX, f.scaleY, u, h), l = f = null, function(p) {
      for (var y = -1, v = h.length, x; ++y < v; ) u[(x = h[y]).i] = x.x(p);
      return u.join("");
    };
  };
}
var qf = cc(Xf, "px, ", "px)", "deg)"), Uf = cc(Yf, ", ", ")", ")"), Zf = 1e-12;
function xs(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Kf(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Gf(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const Mo = (function e(t, n, o) {
  function r(i, s) {
    var c = i[0], d = i[1], l = i[2], f = s[0], u = s[1], h = s[2], p = f - c, y = u - d, v = p * p + y * y, x, m;
    if (v < Zf)
      m = Math.log(h / l) / t, x = function(j) {
        return [
          c + j * p,
          d + j * y,
          l * Math.exp(t * j * m)
        ];
      };
    else {
      var S = Math.sqrt(v), g = (h * h - l * l + o * v) / (2 * l * n * S), w = (h * h - l * l - o * v) / (2 * h * n * S), E = Math.log(Math.sqrt(g * g + 1) - g), k = Math.log(Math.sqrt(w * w + 1) - w);
      m = (k - E) / t, x = function(j) {
        var A = j * m, $ = xs(E), D = l / (n * S) * ($ * Gf(t * A + E) - Kf(E));
        return [
          c + D * p,
          d + D * y,
          l * $ / xs(t * A + E)
        ];
      };
    }
    return x.duration = m * 1e3 * t / Math.SQRT2, x;
  }
  return r.rho = function(i) {
    var s = Math.max(1e-3, +i), c = s * s, d = c * c;
    return e(s, c, d);
  }, r;
})(Math.SQRT2, 2, 4);
var Jt = 0, jn = 0, wn = 0, lc = 1e3, Fo, Cn, Bo = 0, Pt = 0, or = 0, Tn = typeof performance == "object" && performance.now ? performance : Date, dc = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function ji() {
  return Pt || (dc(Jf), Pt = Tn.now() + or);
}
function Jf() {
  Pt = 0;
}
function Wo() {
  this._call = this._time = this._next = null;
}
Wo.prototype = uc.prototype = {
  constructor: Wo,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? ji() : +n) + (t == null ? 0 : +t), !this._next && Cn !== this && (Cn ? Cn._next = this : Fo = this, Cn = this), this._call = e, this._time = n, si();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, si());
  }
};
function uc(e, t, n) {
  var o = new Wo();
  return o.restart(e, t, n), o;
}
function Qf() {
  ji(), ++Jt;
  for (var e = Fo, t; e; )
    (t = Pt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --Jt;
}
function ws() {
  Pt = (Bo = Tn.now()) + or, Jt = jn = 0;
  try {
    Qf();
  } finally {
    Jt = 0, th(), Pt = 0;
  }
}
function eh() {
  var e = Tn.now(), t = e - Bo;
  t > lc && (or -= t, Bo = e);
}
function th() {
  for (var e, t = Fo, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Fo = n);
  Cn = e, si(o);
}
function si(e) {
  if (!Jt) {
    jn && (jn = clearTimeout(jn));
    var t = e - Pt;
    t > 24 ? (e < 1 / 0 && (jn = setTimeout(ws, e - Tn.now() - or)), wn && (wn = clearInterval(wn))) : (wn || (Bo = Tn.now(), wn = setInterval(eh, lc)), Jt = 1, dc(ws));
  }
}
function vs(e, t, n) {
  var o = new Wo();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var nh = tr("start", "end", "cancel", "interrupt"), oh = [], fc = 0, bs = 1, ai = 2, Do = 3, Ns = 4, ci = 5, Po = 6;
function rr(e, t, n, o, r, i) {
  var s = e.__transition;
  if (!s) e.__transition = {};
  else if (n in s) return;
  rh(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: nh,
    tween: oh,
    time: i.time,
    delay: i.delay,
    duration: i.duration,
    ease: i.ease,
    timer: null,
    state: fc
  });
}
function Ci(e, t) {
  var n = Qe(e, t);
  if (n.state > fc) throw new Error("too late; already scheduled");
  return n;
}
function ot(e, t) {
  var n = Qe(e, t);
  if (n.state > Do) throw new Error("too late; already running");
  return n;
}
function Qe(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function rh(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = uc(i, 0, n.time);
  function i(l) {
    n.state = bs, n.timer.restart(s, n.delay, n.time), n.delay <= l && s(l - n.delay);
  }
  function s(l) {
    var f, u, h, p;
    if (n.state !== bs) return d();
    for (f in o)
      if (p = o[f], p.name === n.name) {
        if (p.state === Do) return vs(s);
        p.state === Ns ? (p.state = Po, p.timer.stop(), p.on.call("interrupt", e, e.__data__, p.index, p.group), delete o[f]) : +f < t && (p.state = Po, p.timer.stop(), p.on.call("cancel", e, e.__data__, p.index, p.group), delete o[f]);
      }
    if (vs(function() {
      n.state === Do && (n.state = Ns, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = ai, n.on.call("start", e, e.__data__, n.index, n.group), n.state === ai) {
      for (n.state = Do, r = new Array(h = n.tween.length), f = 0, u = -1; f < h; ++f)
        (p = n.tween[f].value.call(e, e.__data__, n.index, n.group)) && (r[++u] = p);
      r.length = u + 1;
    }
  }
  function c(l) {
    for (var f = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(d), n.state = ci, 1), u = -1, h = r.length; ++u < h; )
      r[u].call(e, f);
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
function ih(e) {
  return this.each(function() {
    $o(this, e);
  });
}
function sh(e, t) {
  var n, o;
  return function() {
    var r = ot(this, e), i = r.tween;
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
function ah(e, t, n) {
  var o, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var i = ot(this, e), s = i.tween;
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
function ch(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = Qe(this.node(), n).tween, r = 0, i = o.length, s; r < i; ++r)
      if ((s = o[r]).name === e)
        return s.value;
    return null;
  }
  return this.each((t == null ? sh : ah)(n, e, t));
}
function Ii(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = ot(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return Qe(r, o).value[t];
  };
}
function hc(e, t) {
  var n;
  return (typeof t == "number" ? tt : t instanceof Dt ? Oo : (n = Dt(t)) ? (t = n, Oo) : sc)(e, t);
}
function lh(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function dh(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function uh(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttribute(e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function fh(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttributeNS(e.space, e.local);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function hh(e, t, n) {
  var o, r, i;
  return function() {
    var s, c = n(this), d;
    return c == null ? void this.removeAttribute(e) : (s = this.getAttribute(e), d = c + "", s === d ? null : s === o && d === r ? i : (r = d, i = t(o = s, c)));
  };
}
function ph(e, t, n) {
  var o, r, i;
  return function() {
    var s, c = n(this), d;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (s = this.getAttributeNS(e.space, e.local), d = c + "", s === d ? null : s === o && d === r ? i : (r = d, i = t(o = s, c)));
  };
}
function gh(e, t) {
  var n = nr(e), o = n === "transform" ? Uf : hc;
  return this.attrTween(e, typeof t == "function" ? (n.local ? ph : hh)(n, o, Ii(this, "attr." + e, t)) : t == null ? (n.local ? dh : lh)(n) : (n.local ? fh : uh)(n, o, t));
}
function yh(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function mh(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function xh(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && mh(e, i)), n;
  }
  return r._value = t, r;
}
function wh(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && yh(e, i)), n;
  }
  return r._value = t, r;
}
function vh(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = nr(e);
  return this.tween(n, (o.local ? xh : wh)(o, t));
}
function bh(e, t) {
  return function() {
    Ci(this, e).delay = +t.apply(this, arguments);
  };
}
function Nh(e, t) {
  return t = +t, function() {
    Ci(this, e).delay = t;
  };
}
function Sh(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? bh : Nh)(t, e)) : Qe(this.node(), t).delay;
}
function Eh(e, t) {
  return function() {
    ot(this, e).duration = +t.apply(this, arguments);
  };
}
function kh(e, t) {
  return t = +t, function() {
    ot(this, e).duration = t;
  };
}
function jh(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Eh : kh)(t, e)) : Qe(this.node(), t).duration;
}
function Ch(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    ot(this, e).ease = t;
  };
}
function Ih(e) {
  var t = this._id;
  return arguments.length ? this.each(Ch(t, e)) : Qe(this.node(), t).ease;
}
function _h(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    ot(this, e).ease = n;
  };
}
function Ah(e) {
  if (typeof e != "function") throw new Error();
  return this.each(_h(this._id, e));
}
function Mh(e) {
  typeof e != "function" && (e = Wa(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = [], d, l = 0; l < s; ++l)
      (d = i[l]) && e.call(d, d.__data__, l, i) && c.push(d);
  return new lt(o, this._parents, this._name, this._id);
}
function Dh(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, i = Math.min(o, r), s = new Array(o), c = 0; c < i; ++c)
    for (var d = t[c], l = n[c], f = d.length, u = s[c] = new Array(f), h, p = 0; p < f; ++p)
      (h = d[p] || l[p]) && (u[p] = h);
  for (; c < o; ++c)
    s[c] = t[c];
  return new lt(s, this._parents, this._name, this._id);
}
function Ph(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function $h(e, t, n) {
  var o, r, i = Ph(t) ? Ci : ot;
  return function() {
    var s = i(this, e), c = s.on;
    c !== o && (r = (o = c).copy()).on(t, n), s.on = r;
  };
}
function Th(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Qe(this.node(), n).on.on(e) : this.each($h(n, e, t));
}
function zh(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function Rh() {
  return this.on("end.remove", zh(this._id));
}
function Lh(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Ni(e));
  for (var o = this._groups, r = o.length, i = new Array(r), s = 0; s < r; ++s)
    for (var c = o[s], d = c.length, l = i[s] = new Array(d), f, u, h = 0; h < d; ++h)
      (f = c[h]) && (u = e.call(f, f.__data__, h, c)) && ("__data__" in f && (u.__data__ = f.__data__), l[h] = u, rr(l[h], t, n, h, l, Qe(f, n)));
  return new lt(i, this._parents, t, n);
}
function Vh(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Ba(e));
  for (var o = this._groups, r = o.length, i = [], s = [], c = 0; c < r; ++c)
    for (var d = o[c], l = d.length, f, u = 0; u < l; ++u)
      if (f = d[u]) {
        for (var h = e.call(f, f.__data__, u, d), p, y = Qe(f, n), v = 0, x = h.length; v < x; ++v)
          (p = h[v]) && rr(p, t, n, v, h, y);
        i.push(h), s.push(f);
      }
  return new lt(i, s, t, n);
}
var Hh = Wn.prototype.constructor;
function Oh() {
  return new Hh(this._groups, this._parents);
}
function Fh(e, t) {
  var n, o, r;
  return function() {
    var i = Gt(this, e), s = (this.style.removeProperty(e), Gt(this, e));
    return i === s ? null : i === n && s === o ? r : r = t(n = i, o = s);
  };
}
function pc(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Bh(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = Gt(this, e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Wh(e, t, n) {
  var o, r, i;
  return function() {
    var s = Gt(this, e), c = n(this), d = c + "";
    return c == null && (d = c = (this.style.removeProperty(e), Gt(this, e))), s === d ? null : s === o && d === r ? i : (r = d, i = t(o = s, c));
  };
}
function Xh(e, t) {
  var n, o, r, i = "style." + t, s = "end." + i, c;
  return function() {
    var d = ot(this, e), l = d.on, f = d.value[i] == null ? c || (c = pc(t)) : void 0;
    (l !== n || r !== f) && (o = (n = l).copy()).on(s, r = f), d.on = o;
  };
}
function Yh(e, t, n) {
  var o = (e += "") == "transform" ? qf : hc;
  return t == null ? this.styleTween(e, Fh(e, o)).on("end.style." + e, pc(e)) : typeof t == "function" ? this.styleTween(e, Wh(e, o, Ii(this, "style." + e, t))).each(Xh(this._id, e)) : this.styleTween(e, Bh(e, o, t), n).on("end.style." + e, null);
}
function qh(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function Uh(e, t, n) {
  var o, r;
  function i() {
    var s = t.apply(this, arguments);
    return s !== r && (o = (r = s) && qh(e, s, n)), o;
  }
  return i._value = t, i;
}
function Zh(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, Uh(e, t, n ?? ""));
}
function Kh(e) {
  return function() {
    this.textContent = e;
  };
}
function Gh(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function Jh(e) {
  return this.tween("text", typeof e == "function" ? Gh(Ii(this, "text", e)) : Kh(e == null ? "" : e + ""));
}
function Qh(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function ep(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Qh(r)), t;
  }
  return o._value = e, o;
}
function tp(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, ep(e));
}
function np() {
  for (var e = this._name, t = this._id, n = gc(), o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], c = s.length, d, l = 0; l < c; ++l)
      if (d = s[l]) {
        var f = Qe(d, t);
        rr(d, e, n, l, s, {
          time: f.time + f.delay + f.duration,
          delay: 0,
          duration: f.duration,
          ease: f.ease
        });
      }
  return new lt(o, this._parents, e, n);
}
function op() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(i, s) {
    var c = { value: s }, d = { value: function() {
      --r === 0 && i();
    } };
    n.each(function() {
      var l = ot(this, o), f = l.on;
      f !== e && (t = (e = f).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(d)), l.on = t;
    }), r === 0 && i();
  });
}
var rp = 0;
function lt(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function gc() {
  return ++rp;
}
var st = Wn.prototype;
lt.prototype = {
  constructor: lt,
  select: Lh,
  selectAll: Vh,
  selectChild: st.selectChild,
  selectChildren: st.selectChildren,
  filter: Mh,
  merge: Dh,
  selection: Oh,
  transition: np,
  call: st.call,
  nodes: st.nodes,
  node: st.node,
  size: st.size,
  empty: st.empty,
  each: st.each,
  on: Th,
  attr: gh,
  attrTween: vh,
  style: Yh,
  styleTween: Zh,
  text: Jh,
  textTween: tp,
  remove: Rh,
  tween: ch,
  delay: Sh,
  duration: jh,
  ease: Ih,
  easeVarying: Ah,
  end: op,
  [Symbol.iterator]: st[Symbol.iterator]
};
function ip(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var sp = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: ip
};
function ap(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function cp(e) {
  var t, n;
  e instanceof lt ? (t = e._id, e = e._name) : (t = gc(), (n = sp).time = ji(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], c = s.length, d, l = 0; l < c; ++l)
      (d = s[l]) && rr(d, e, t, l, s, n || ap(d, t));
  return new lt(o, this._parents, e, t);
}
Wn.prototype.interrupt = ih;
Wn.prototype.transition = cp;
const So = (e) => () => e;
function lp(e, {
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
function ct(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
ct.prototype = {
  constructor: ct,
  scale: function(e) {
    return e === 1 ? this : new ct(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new ct(this.k, this.x + this.k * e, this.y + this.k * t);
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
var ir = new ct(1, 0, 0);
yc.prototype = ct.prototype;
function yc(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return ir;
  return e.__zoom;
}
function Or(e) {
  e.stopImmediatePropagation();
}
function vn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function dp(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function up() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function Ss() {
  return this.__zoom || ir;
}
function fp(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function hp() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function pp(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], i = e.invertY(t[0][1]) - n[0][1], s = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    s > i ? (i + s) / 2 : Math.min(0, i) || Math.max(0, s)
  );
}
function mc() {
  var e = dp, t = up, n = pp, o = fp, r = hp, i = [0, 1 / 0], s = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, d = Mo, l = tr("start", "zoom", "end"), f, u, h, p = 500, y = 150, v = 0, x = 10;
  function m(N) {
    N.property("__zoom", Ss).on("wheel.zoom", A, { passive: !1 }).on("mousedown.zoom", $).on("dblclick.zoom", D).filter(r).on("touchstart.zoom", C).on("touchmove.zoom", R).on("touchend.zoom touchcancel.zoom", F).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  m.transform = function(N, _, I, P) {
    var T = N.selection ? N.selection() : N;
    T.property("__zoom", Ss), N !== T ? E(N, _, I, P) : T.interrupt().each(function() {
      k(this, arguments).event(P).start().zoom(null, typeof _ == "function" ? _.apply(this, arguments) : _).end();
    });
  }, m.scaleBy = function(N, _, I, P) {
    m.scaleTo(N, function() {
      var T = this.__zoom.k, z = typeof _ == "function" ? _.apply(this, arguments) : _;
      return T * z;
    }, I, P);
  }, m.scaleTo = function(N, _, I, P) {
    m.transform(N, function() {
      var T = t.apply(this, arguments), z = this.__zoom, O = I == null ? w(T) : typeof I == "function" ? I.apply(this, arguments) : I, W = z.invert(O), B = typeof _ == "function" ? _.apply(this, arguments) : _;
      return n(g(S(z, B), O, W), T, s);
    }, I, P);
  }, m.translateBy = function(N, _, I, P) {
    m.transform(N, function() {
      return n(this.__zoom.translate(
        typeof _ == "function" ? _.apply(this, arguments) : _,
        typeof I == "function" ? I.apply(this, arguments) : I
      ), t.apply(this, arguments), s);
    }, null, P);
  }, m.translateTo = function(N, _, I, P, T) {
    m.transform(N, function() {
      var z = t.apply(this, arguments), O = this.__zoom, W = P == null ? w(z) : typeof P == "function" ? P.apply(this, arguments) : P;
      return n(ir.translate(W[0], W[1]).scale(O.k).translate(
        typeof _ == "function" ? -_.apply(this, arguments) : -_,
        typeof I == "function" ? -I.apply(this, arguments) : -I
      ), z, s);
    }, P, T);
  };
  function S(N, _) {
    return _ = Math.max(i[0], Math.min(i[1], _)), _ === N.k ? N : new ct(_, N.x, N.y);
  }
  function g(N, _, I) {
    var P = _[0] - I[0] * N.k, T = _[1] - I[1] * N.k;
    return P === N.x && T === N.y ? N : new ct(N.k, P, T);
  }
  function w(N) {
    return [(+N[0][0] + +N[1][0]) / 2, (+N[0][1] + +N[1][1]) / 2];
  }
  function E(N, _, I, P) {
    N.on("start.zoom", function() {
      k(this, arguments).event(P).start();
    }).on("interrupt.zoom end.zoom", function() {
      k(this, arguments).event(P).end();
    }).tween("zoom", function() {
      var T = this, z = arguments, O = k(T, z).event(P), W = t.apply(T, z), B = I == null ? w(W) : typeof I == "function" ? I.apply(T, z) : I, K = Math.max(W[1][0] - W[0][0], W[1][1] - W[0][1]), q = T.__zoom, ne = typeof _ == "function" ? _.apply(T, z) : _, ae = d(q.invert(B).concat(K / q.k), ne.invert(B).concat(K / ne.k));
      return function(G) {
        if (G === 1) G = ne;
        else {
          var L = ae(G), U = K / L[2];
          G = new ct(U, B[0] - L[0] * U, B[1] - L[1] * U);
        }
        O.zoom(null, G);
      };
    });
  }
  function k(N, _, I) {
    return !I && N.__zooming || new j(N, _);
  }
  function j(N, _) {
    this.that = N, this.args = _, this.active = 0, this.sourceEvent = null, this.extent = t.apply(N, _), this.taps = 0;
  }
  j.prototype = {
    event: function(N) {
      return N && (this.sourceEvent = N), this;
    },
    start: function() {
      return ++this.active === 1 && (this.that.__zooming = this, this.emit("start")), this;
    },
    zoom: function(N, _) {
      return this.mouse && N !== "mouse" && (this.mouse[1] = _.invert(this.mouse[0])), this.touch0 && N !== "touch" && (this.touch0[1] = _.invert(this.touch0[0])), this.touch1 && N !== "touch" && (this.touch1[1] = _.invert(this.touch1[0])), this.that.__zoom = _, this.emit("zoom"), this;
    },
    end: function() {
      return --this.active === 0 && (delete this.that.__zooming, this.emit("end")), this;
    },
    emit: function(N) {
      var _ = He(this.that).datum();
      l.call(
        N,
        this.that,
        new lp(N, {
          sourceEvent: this.sourceEvent,
          target: m,
          transform: this.that.__zoom,
          dispatch: l
        }),
        _
      );
    }
  };
  function A(N, ..._) {
    if (!e.apply(this, arguments)) return;
    var I = k(this, _).event(N), P = this.__zoom, T = Math.max(i[0], Math.min(i[1], P.k * Math.pow(2, o.apply(this, arguments)))), z = qe(N);
    if (I.wheel)
      (I.mouse[0][0] !== z[0] || I.mouse[0][1] !== z[1]) && (I.mouse[1] = P.invert(I.mouse[0] = z)), clearTimeout(I.wheel);
    else {
      if (P.k === T) return;
      I.mouse = [z, P.invert(z)], $o(this), I.start();
    }
    vn(N), I.wheel = setTimeout(O, y), I.zoom("mouse", n(g(S(P, T), I.mouse[0], I.mouse[1]), I.extent, s));
    function O() {
      I.wheel = null, I.end();
    }
  }
  function $(N, ..._) {
    if (h || !e.apply(this, arguments)) return;
    var I = N.currentTarget, P = k(this, _, !0).event(N), T = He(N.view).on("mousemove.zoom", B, !0).on("mouseup.zoom", K, !0), z = qe(N, I), O = N.clientX, W = N.clientY;
    ec(N.view), Or(N), P.mouse = [z, this.__zoom.invert(z)], $o(this), P.start();
    function B(q) {
      if (vn(q), !P.moved) {
        var ne = q.clientX - O, ae = q.clientY - W;
        P.moved = ne * ne + ae * ae > v;
      }
      P.event(q).zoom("mouse", n(g(P.that.__zoom, P.mouse[0] = qe(q, I), P.mouse[1]), P.extent, s));
    }
    function K(q) {
      T.on("mousemove.zoom mouseup.zoom", null), tc(q.view, P.moved), vn(q), P.event(q).end();
    }
  }
  function D(N, ..._) {
    if (e.apply(this, arguments)) {
      var I = this.__zoom, P = qe(N.changedTouches ? N.changedTouches[0] : N, this), T = I.invert(P), z = I.k * (N.shiftKey ? 0.5 : 2), O = n(g(S(I, z), P, T), t.apply(this, _), s);
      vn(N), c > 0 ? He(this).transition().duration(c).call(E, O, P, N) : He(this).call(m.transform, O, P, N);
    }
  }
  function C(N, ..._) {
    if (e.apply(this, arguments)) {
      var I = N.touches, P = I.length, T = k(this, _, N.changedTouches.length === P).event(N), z, O, W, B;
      for (Or(N), O = 0; O < P; ++O)
        W = I[O], B = qe(W, this), B = [B, this.__zoom.invert(B), W.identifier], T.touch0 ? !T.touch1 && T.touch0[2] !== B[2] && (T.touch1 = B, T.taps = 0) : (T.touch0 = B, z = !0, T.taps = 1 + !!f);
      f && (f = clearTimeout(f)), z && (T.taps < 2 && (u = B[0], f = setTimeout(function() {
        f = null;
      }, p)), $o(this), T.start());
    }
  }
  function R(N, ..._) {
    if (this.__zooming) {
      var I = k(this, _).event(N), P = N.changedTouches, T = P.length, z, O, W, B;
      for (vn(N), z = 0; z < T; ++z)
        O = P[z], W = qe(O, this), I.touch0 && I.touch0[2] === O.identifier ? I.touch0[0] = W : I.touch1 && I.touch1[2] === O.identifier && (I.touch1[0] = W);
      if (O = I.that.__zoom, I.touch1) {
        var K = I.touch0[0], q = I.touch0[1], ne = I.touch1[0], ae = I.touch1[1], G = (G = ne[0] - K[0]) * G + (G = ne[1] - K[1]) * G, L = (L = ae[0] - q[0]) * L + (L = ae[1] - q[1]) * L;
        O = S(O, Math.sqrt(G / L)), W = [(K[0] + ne[0]) / 2, (K[1] + ne[1]) / 2], B = [(q[0] + ae[0]) / 2, (q[1] + ae[1]) / 2];
      } else if (I.touch0) W = I.touch0[0], B = I.touch0[1];
      else return;
      I.zoom("touch", n(g(O, W, B), I.extent, s));
    }
  }
  function F(N, ..._) {
    if (this.__zooming) {
      var I = k(this, _).event(N), P = N.changedTouches, T = P.length, z, O;
      for (Or(N), h && clearTimeout(h), h = setTimeout(function() {
        h = null;
      }, p), z = 0; z < T; ++z)
        O = P[z], I.touch0 && I.touch0[2] === O.identifier ? delete I.touch0 : I.touch1 && I.touch1[2] === O.identifier && delete I.touch1;
      if (I.touch1 && !I.touch0 && (I.touch0 = I.touch1, delete I.touch1), I.touch0) I.touch0[1] = this.__zoom.invert(I.touch0[0]);
      else if (I.end(), I.taps === 2 && (O = qe(O, this), Math.hypot(u[0] - O[0], u[1] - O[1]) < x)) {
        var W = He(this).on("dblclick.zoom");
        W && W.apply(this, arguments);
      }
    }
  }
  return m.wheelDelta = function(N) {
    return arguments.length ? (o = typeof N == "function" ? N : So(+N), m) : o;
  }, m.filter = function(N) {
    return arguments.length ? (e = typeof N == "function" ? N : So(!!N), m) : e;
  }, m.touchable = function(N) {
    return arguments.length ? (r = typeof N == "function" ? N : So(!!N), m) : r;
  }, m.extent = function(N) {
    return arguments.length ? (t = typeof N == "function" ? N : So([[+N[0][0], +N[0][1]], [+N[1][0], +N[1][1]]]), m) : t;
  }, m.scaleExtent = function(N) {
    return arguments.length ? (i[0] = +N[0], i[1] = +N[1], m) : [i[0], i[1]];
  }, m.translateExtent = function(N) {
    return arguments.length ? (s[0][0] = +N[0][0], s[1][0] = +N[1][0], s[0][1] = +N[0][1], s[1][1] = +N[1][1], m) : [[s[0][0], s[0][1]], [s[1][0], s[1][1]]];
  }, m.constrain = function(N) {
    return arguments.length ? (n = N, m) : n;
  }, m.duration = function(N) {
    return arguments.length ? (c = +N, m) : c;
  }, m.interpolate = function(N) {
    return arguments.length ? (d = N, m) : d;
  }, m.on = function() {
    var N = l.on.apply(l, arguments);
    return N === l ? m : N;
  }, m.clickDistance = function(N) {
    return arguments.length ? (v = (N = +N) * N, m) : Math.sqrt(v);
  }, m.tapDistance = function(N) {
    return arguments.length ? (x = +N, m) : x;
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
}, zn = [
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
var Qt;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(Qt || (Qt = {}));
var At;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(At || (At = {}));
var Rn;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Rn || (Rn = {}));
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
var gt;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(gt || (gt = {}));
var Xo;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(Xo || (Xo = {}));
var te;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(te || (te = {}));
const Es = {
  [te.Left]: te.Right,
  [te.Right]: te.Left,
  [te.Top]: te.Bottom,
  [te.Bottom]: te.Top
};
function bc(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Nc = (e) => "id" in e && "source" in e && "target" in e, gp = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), _i = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), Yn = (e, t = [0, 0]) => {
  const { width: n, height: o } = dt(e), r = e.origin ?? t, i = n * r[0], s = o * r[1];
  return {
    x: e.position.x - i,
    y: e.position.y - s
  };
}, yp = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const i = typeof r == "string";
    let s = !t.nodeLookup && !i ? r : void 0;
    t.nodeLookup && (s = i ? t.nodeLookup.get(r) : _i(r) ? r : t.nodeLookup.get(r.id));
    const c = s ? Yo(s, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return sr(o, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return ar(n);
}, qn = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = sr(n, Yo(r)), o = !0);
  }), o ? ar(n) : { x: 0, y: 0, width: 0, height: 0 };
}, Ai = (e, t, [n, o, r] = [0, 0, 1], i = !1, s = !1) => {
  const c = {
    ...sn(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, d = [];
  for (const l of e.values()) {
    const { measured: f, selectable: u = !0, hidden: h = !1 } = l;
    if (s && !u || h)
      continue;
    const p = f.width ?? l.width ?? l.initialWidth ?? null, y = f.height ?? l.height ?? l.initialHeight ?? null, v = Ln(c, tn(l)), x = (p ?? 0) * (y ?? 0), m = i && v > 0;
    (!l.internals.handleBounds || m || v >= x || l.dragging) && d.push(l);
  }
  return d;
}, mp = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function xp(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function wp({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: i }, s) {
  if (e.size === 0)
    return !0;
  const c = xp(e, s), d = qn(c), l = Di(d, t, n, s?.minZoom ?? r, s?.maxZoom ?? i, s?.padding ?? 0.1);
  return await o.setViewport(l, {
    duration: s?.duration,
    ease: s?.ease,
    interpolate: s?.interpolate
  }), !0;
}
function Sc({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: i }) {
  const s = n.get(e), c = s.parentId ? n.get(s.parentId) : void 0, { x: d, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, f = s.origin ?? o;
  let u = s.extent || r;
  if (s.extent === "parent" && !s.expandParent)
    if (!c)
      i?.("005", Fe.error005());
    else {
      const p = c.measured.width, y = c.measured.height;
      p && y && (u = [
        [d, l],
        [d + p, l + y]
      ]);
    }
  else c && Tt(s.extent) && (u = [
    [s.extent[0][0] + d, s.extent[0][1] + l],
    [s.extent[1][0] + d, s.extent[1][1] + l]
  ]);
  const h = Tt(u) ? $t(t, u, s.measured) : t;
  return (s.measured.width === void 0 || s.measured.height === void 0) && i?.("015", Fe.error015()), {
    position: {
      x: h.x - d + (s.measured.width ?? 0) * f[0],
      y: h.y - l + (s.measured.height ?? 0) * f[1]
    },
    positionAbsolute: h
  };
}
async function vp({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const i = new Set(e.map((h) => h.id)), s = [];
  for (const h of n) {
    if (h.deletable === !1)
      continue;
    const p = i.has(h.id), y = !p && h.parentId && s.find((v) => v.id === h.parentId);
    (p || y) && s.push(h);
  }
  const c = new Set(t.map((h) => h.id)), d = o.filter((h) => h.deletable !== !1), f = mp(s, d);
  for (const h of d)
    c.has(h.id) && !f.find((y) => y.id === h.id) && f.push(h);
  if (!r)
    return {
      edges: f,
      nodes: s
    };
  const u = await r({
    nodes: s,
    edges: f
  });
  return typeof u == "boolean" ? u ? { edges: f, nodes: s } : { edges: [], nodes: [] } : u;
}
const en = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), $t = (e = { x: 0, y: 0 }, t, n) => ({
  x: en(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: en(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Ec(e, t, n) {
  const { width: o, height: r } = dt(n), { x: i, y: s } = n.internals.positionAbsolute;
  return $t(e, [
    [i, s],
    [i + o, s + r]
  ], t);
}
const ks = (e, t, n) => e < t ? en(Math.abs(e - t), 1, t) / t : e > n ? -en(Math.abs(e - n), 1, t) / t : 0, Mi = (e, t, n = 15, o = 40) => {
  const r = ks(e.x, o, t.width - o) * n, i = ks(e.y, o, t.height - o) * n;
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
}), tn = (e, t = [0, 0]) => {
  const { x: n, y: o } = _i(e) ? e.internals.positionAbsolute : Yn(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, Yo = (e, t = [0, 0]) => {
  const { x: n, y: o } = _i(e) ? e.internals.positionAbsolute : Yn(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, kc = (e, t) => ar(sr(li(e), li(t))), Ln = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, js = (e) => Ze(e.width) && Ze(e.height) && Ze(e.x) && Ze(e.y), Ze = (e) => !isNaN(e) && isFinite(e), jc = (e, t) => (n, o) => {
}, Un = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), sn = ({ x: e, y: t }, [n, o, r], i = !1, s = [1, 1]) => {
  const c = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return i ? Un(c, s) : c;
}, nn = ({ x: e, y: t }, [n, o, r]) => ({
  x: e * r + n,
  y: t * r + o
});
function Wt(e, t) {
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
function bp(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = Wt(e, n), r = Wt(e, t);
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
    const o = Wt(e.top ?? e.y ?? 0, n), r = Wt(e.bottom ?? e.y ?? 0, n), i = Wt(e.left ?? e.x ?? 0, t), s = Wt(e.right ?? e.x ?? 0, t);
    return { top: o, right: s, bottom: r, left: i, x: i + s, y: o + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function Np(e, t, n, o, r, i) {
  const { x: s, y: c } = nn(e, [t, n, o]), { x: d, y: l } = nn({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), f = r - d, u = i - l;
  return {
    left: Math.floor(s),
    top: Math.floor(c),
    right: Math.floor(f),
    bottom: Math.floor(u)
  };
}
const Di = (e, t, n, o, r, i) => {
  const s = bp(i, t, n), c = (t - s.x) / e.width, d = (n - s.y) / e.height, l = Math.min(c, d), f = en(l, o, r), u = e.x + e.width / 2, h = e.y + e.height / 2, p = t / 2 - u * f, y = n / 2 - h * f, v = Np(e, p, y, f, t, n), x = {
    left: Math.min(v.left - s.left, 0),
    top: Math.min(v.top - s.top, 0),
    right: Math.min(v.right - s.right, 0),
    bottom: Math.min(v.bottom - s.bottom, 0)
  };
  return {
    x: p - x.left + x.right,
    y: y - x.top + x.bottom,
    zoom: f
  };
}, Vn = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function Tt(e) {
  return e != null && e !== "parent";
}
function dt(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function Cc(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function Ic(e, t = { width: 0, height: 0 }, n, o, r) {
  const i = { ...e }, s = o.get(n);
  if (s) {
    const c = s.origin || r;
    i.x += s.internals.positionAbsolute.x - (t.width ?? 0) * c[0], i.y += s.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return i;
}
function Cs(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function Sp() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function Ep(e) {
  return { ...wc, ...e || {} };
}
function An(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: i, y: s } = Ke(e), c = sn({ x: i - (r?.left ?? 0), y: s - (r?.top ?? 0) }, o), { x: d, y: l } = n ? Un(c, t) : c;
  return {
    xSnapped: d,
    ySnapped: l,
    ...c
  };
}
const Pi = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), _c = (e) => e?.getRootNode?.() || window?.document, kp = ["INPUT", "SELECT", "TEXTAREA"];
function Ac(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : kp.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const Mc = (e) => "clientX" in e, Ke = (e, t) => {
  const n = Mc(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, Is = (e, t, n, o, r) => {
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
      ...Pi(s)
    };
  });
};
function Dc({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: i, targetControlX: s, targetControlY: c }) {
  const d = e * 0.125 + r * 0.375 + s * 0.375 + n * 0.125, l = t * 0.125 + i * 0.375 + c * 0.375 + o * 0.125, f = Math.abs(d - e), u = Math.abs(l - t);
  return [d, l, f, u];
}
function Eo(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function _s({ pos: e, x1: t, y1: n, x2: o, y2: r, c: i }) {
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
  const [c, d] = _s({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: s
  }), [l, f] = _s({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: s
  }), [u, h, p, y] = Dc({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: r,
    sourceControlX: c,
    sourceControlY: d,
    targetControlX: l,
    targetControlY: f
  });
  return [
    `M${e},${t} C${c},${d} ${l},${f} ${o},${r}`,
    u,
    h,
    p,
    y
  ];
}
function $c({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, i = n < e ? n + r : n - r, s = Math.abs(o - t) / 2, c = o < t ? o + s : o - s;
  return [i, c, r, s];
}
function jp({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: i = "basic" }) {
  if (i === "manual")
    return o;
  const s = r && n ? o + 1e3 : o, c = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return s + c;
}
function Cp({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const i = sr(Yo(e), Yo(t));
  i.x === i.x2 && (i.x2 += 1), i.y === i.y2 && (i.y2 += 1);
  const s = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return Ln(s, ar(i)) > 0;
}
const Tc = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, Ip = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), _p = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Fe.error006()), t;
  const o = n.getEdgeId || Tc;
  let r;
  return Nc(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, Ip(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, Ap = (e, t, n, o = { shouldReplaceId: !0 }) => {
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
const As = {
  [te.Left]: { x: -1, y: 0 },
  [te.Right]: { x: 1, y: 0 },
  [te.Top]: { x: 0, y: -1 },
  [te.Bottom]: { x: 0, y: 1 }
}, Mp = ({ source: e, sourcePosition: t = te.Bottom, target: n }) => t === te.Left || t === te.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, Ms = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Dp({ source: e, sourcePosition: t = te.Bottom, target: n, targetPosition: o = te.Top, center: r, offset: i, stepPosition: s }) {
  const c = As[t], d = As[o], l = { x: e.x + c.x * i, y: e.y + c.y * i }, f = { x: n.x + d.x * i, y: n.y + d.y * i }, u = Mp({
    source: l,
    sourcePosition: t,
    target: f
  }), h = u.x !== 0 ? "x" : "y", p = u[h];
  let y = [], v, x;
  const m = { x: 0, y: 0 }, S = { x: 0, y: 0 }, [, , g, w] = $c({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[h] * d[h] === -1) {
    h === "x" ? (v = r.x ?? l.x + (f.x - l.x) * s, x = r.y ?? (l.y + f.y) / 2) : (v = r.x ?? (l.x + f.x) / 2, x = r.y ?? l.y + (f.y - l.y) * s);
    const A = [
      { x: v, y: l.y },
      { x: v, y: f.y }
    ], $ = [
      { x: l.x, y: x },
      { x: f.x, y: x }
    ];
    c[h] === p ? y = h === "x" ? A : $ : y = h === "x" ? $ : A;
  } else {
    const A = [{ x: l.x, y: f.y }], $ = [{ x: f.x, y: l.y }];
    if (h === "x" ? y = c.x === p ? $ : A : y = c.y === p ? A : $, t === o) {
      const N = Math.abs(e[h] - n[h]);
      if (N <= i) {
        const _ = Math.min(i - 1, i - N);
        c[h] === p ? m[h] = (l[h] > e[h] ? -1 : 1) * _ : S[h] = (f[h] > n[h] ? -1 : 1) * _;
      }
    }
    if (t !== o) {
      const N = h === "x" ? "y" : "x", _ = c[h] === d[N], I = l[N] > f[N], P = l[N] < f[N];
      (c[h] === 1 && (!_ && I || _ && P) || c[h] !== 1 && (!_ && P || _ && I)) && (y = h === "x" ? A : $);
    }
    const D = { x: l.x + m.x, y: l.y + m.y }, C = { x: f.x + S.x, y: f.y + S.y }, R = Math.max(Math.abs(D.x - y[0].x), Math.abs(C.x - y[0].x)), F = Math.max(Math.abs(D.y - y[0].y), Math.abs(C.y - y[0].y));
    R >= F ? (v = (D.x + C.x) / 2, x = y[0].y) : (v = y[0].x, x = (D.y + C.y) / 2);
  }
  const E = { x: l.x + m.x, y: l.y + m.y }, k = { x: f.x + S.x, y: f.y + S.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...E.x !== y[0].x || E.y !== y[0].y ? [E] : [],
    ...y,
    ...k.x !== y[y.length - 1].x || k.y !== y[y.length - 1].y ? [k] : [],
    n
  ], v, x, g, w];
}
function Pp(e, t, n, o) {
  const r = Math.min(Ms(e, t) / 2, Ms(t, n) / 2, o), { x: i, y: s } = t;
  if (e.x === i && i === n.x || e.y === s && s === n.y)
    return `L${i} ${s}`;
  if (e.y === s) {
    const l = e.x < n.x ? -1 : 1, f = e.y < n.y ? 1 : -1;
    return `L ${i + r * l},${s}Q ${i},${s} ${i},${s + r * f}`;
  }
  const c = e.x < n.x ? 1 : -1, d = e.y < n.y ? -1 : 1;
  return `L ${i},${s + r * d}Q ${i},${s} ${i + r * c},${s}`;
}
function qo({ sourceX: e, sourceY: t, sourcePosition: n = te.Bottom, targetX: o, targetY: r, targetPosition: i = te.Top, borderRadius: s = 5, centerX: c, centerY: d, offset: l = 20, stepPosition: f = 0.5 }) {
  const [u, h, p, y, v] = Dp({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: i,
    center: { x: c, y: d },
    offset: l,
    stepPosition: f
  });
  let x = `M${u[0].x} ${u[0].y}`;
  for (let m = 1; m < u.length - 1; m++)
    x += Pp(u[m - 1], u[m], u[m + 1], s);
  return x += `L${u[u.length - 1].x} ${u[u.length - 1].y}`, [x, h, p, y, v];
}
function Ds(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function $p(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!Ds(t) || !Ds(n))
    return null;
  const o = t.internals.handleBounds || Ps(t.handles), r = n.internals.handleBounds || Ps(n.handles), i = $s(o?.source ?? [], e.sourceHandle), s = $s(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === Qt.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!i || !s)
    return e.onError?.("008", Fe.error008(i ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = i?.position || te.Bottom, d = s?.position || te.Top, l = zt(t, i, c), f = zt(n, s, d);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: f.x,
    targetY: f.y,
    sourcePosition: c,
    targetPosition: d
  };
}
function Ps(e) {
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
function zt(e, t, n = te.Left, o = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, i = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: s, height: c } = t ?? dt(e);
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
function $s(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function di(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function Tp(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const i = /* @__PURE__ */ new Set();
  return e.reduce((s, c) => ([c.markerStart || o, c.markerEnd || r].forEach((d) => {
    if (d && typeof d == "object") {
      const l = di(d, t);
      i.has(l) || (s.push({ id: l, color: d.color || n, ...d }), i.add(l));
    }
  }), s), []).sort((s, c) => s.id.localeCompare(c.id));
}
const Rc = 1e3, zp = 10, $i = {
  nodeOrigin: [0, 0],
  nodeExtent: zn,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Rp = {
  ...$i,
  checkEquality: !0
};
function Ti(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function Lp(e, t, n) {
  const o = Ti($i, n);
  for (const r of e.values())
    if (r.parentId)
      Ri(r, e, t, o);
    else {
      const i = Yn(r, o.nodeOrigin), s = Tt(r.extent) ? r.extent : o.nodeExtent, c = $t(i, s, dt(r));
      r.internals.positionAbsolute = c;
    }
}
function Vp(e, t) {
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
function zi(e) {
  return e === "manual";
}
function ui(e, t, n, o = {}) {
  const r = Ti(Rp, o), i = { i: 0 }, s = new Map(t), c = r?.elevateNodesOnSelect && !zi(r.zIndexMode) ? Rc : 0;
  let d = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const f of e) {
    let u = s.get(f.id);
    if (r.checkEquality && f === u?.internals.userNode)
      t.set(f.id, u);
    else {
      const h = Yn(f, r.nodeOrigin), p = Tt(f.extent) ? f.extent : r.nodeExtent, y = $t(h, p, dt(f));
      u = {
        ...r.defaults,
        ...f,
        measured: {
          width: f.measured?.width,
          height: f.measured?.height
        },
        internals: {
          positionAbsolute: y,
          // if user re-initializes the node or removes `measured` for whatever reason, we reset the handleBounds so that the node gets re-measured
          handleBounds: Vp(f, u),
          z: Lc(f, c, r.zIndexMode),
          userNode: f
        }
      }, t.set(f.id, u);
    }
    (u.measured === void 0 || u.measured.width === void 0 || u.measured.height === void 0) && !u.hidden && (d = !1), f.parentId && Ri(u, t, n, o, i), l ||= f.selected ?? !1;
  }
  return { nodesInitialized: d, hasSelectedNodes: l };
}
function Hp(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function Ri(e, t, n, o, r) {
  const { elevateNodesOnSelect: i, nodeOrigin: s, nodeExtent: c, zIndexMode: d } = Ti($i, o), l = e.parentId, f = t.get(l);
  if (!f) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Hp(e, n), r && !f.parentId && f.internals.rootParentIndex === void 0 && d === "auto" && (f.internals.rootParentIndex = ++r.i, f.internals.z = f.internals.z + r.i * zp), r && f.internals.rootParentIndex !== void 0 && (r.i = f.internals.rootParentIndex);
  const u = i && !zi(d) ? Rc : 0, { x: h, y: p, z: y } = Op(e, f, s, c, u, d), { positionAbsolute: v } = e.internals, x = h !== v.x || p !== v.y;
  (x || y !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: x ? { x: h, y: p } : v,
      z: y
    }
  });
}
function Lc(e, t, n) {
  const o = Ze(e.zIndex) ? e.zIndex : 0;
  return zi(n) ? o : o + (e.selected ? t : 0);
}
function Op(e, t, n, o, r, i) {
  const { x: s, y: c } = t.internals.positionAbsolute, d = dt(e), l = Yn(e, n), f = Tt(e.extent) ? $t(l, e.extent, d) : l;
  let u = $t({ x: s + f.x, y: c + f.y }, o, d);
  e.extent === "parent" && (u = Ec(u, d, t));
  const h = Lc(e, r, i), p = t.internals.z ?? 0;
  return {
    x: u.x,
    y: u.y,
    z: p >= h ? p + 1 : h
  };
}
function Li(e, t, n, o = [0, 0]) {
  const r = [], i = /* @__PURE__ */ new Map();
  for (const s of e) {
    const c = t.get(s.parentId);
    if (!c)
      continue;
    const d = i.get(s.parentId)?.expandedRect ?? tn(c), l = kc(d, s.rect);
    i.set(s.parentId, { expandedRect: l, parent: c });
  }
  return i.size > 0 && i.forEach(({ expandedRect: s, parent: c }, d) => {
    const l = c.internals.positionAbsolute, f = dt(c), u = c.origin ?? o, h = s.x < l.x ? Math.round(Math.abs(l.x - s.x)) : 0, p = s.y < l.y ? Math.round(Math.abs(l.y - s.y)) : 0, y = Math.max(f.width, Math.round(s.width)), v = Math.max(f.height, Math.round(s.height)), x = (y - f.width) * u[0], m = (v - f.height) * u[1];
    (h > 0 || p > 0 || x || m) && (r.push({
      id: d,
      type: "position",
      position: {
        x: c.position.x - h + x,
        y: c.position.y - p + m
      }
    }), n.get(d)?.forEach((S) => {
      e.some((g) => g.id === S.id) || r.push({
        id: S.id,
        type: "position",
        position: {
          x: S.position.x + h,
          y: S.position.y + p
        }
      });
    })), (f.width < s.width || f.height < s.height || h || p) && r.push({
      id: d,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: y + (h ? u[0] * h - x : 0),
        height: v + (p ? u[1] * p - m : 0)
      }
    });
  }), r;
}
function Fp(e, t, n, o, r, i, s) {
  const c = o?.querySelector(".xyflow__viewport");
  let d = !1;
  if (!c)
    return { changes: [], updatedInternals: d };
  const l = [], f = window.getComputedStyle(c), { m22: u } = new window.DOMMatrixReadOnly(f.transform), h = [];
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
    const v = Pi(p.nodeElement), x = y.measured.width !== v.width || y.measured.height !== v.height;
    if (!!(v.width && v.height && (x || !y.internals.handleBounds || p.force))) {
      const S = p.nodeElement.getBoundingClientRect(), g = Tt(y.extent) ? y.extent : i;
      let { positionAbsolute: w } = y.internals;
      y.parentId && y.extent === "parent" ? w = Ec(w, v, t.get(y.parentId)) : g && (w = $t(w, g, v));
      const E = {
        ...y,
        measured: v,
        internals: {
          ...y.internals,
          positionAbsolute: w,
          handleBounds: {
            source: Is("source", p.nodeElement, S, u, y.id),
            target: Is("target", p.nodeElement, S, u, y.id)
          }
        }
      };
      t.set(y.id, E), y.parentId && Ri(E, t, n, { nodeOrigin: r, zIndexMode: s }), d = !0, x && (l.push({
        id: y.id,
        type: "dimensions",
        dimensions: v
      }), y.expandParent && y.parentId && h.push({
        id: y.id,
        parentId: y.parentId,
        rect: tn(E, r)
      }));
    }
  }
  if (h.length > 0) {
    const p = Li(h, t, n, r);
    l.push(...p);
  }
  return { changes: l, updatedInternals: d };
}
async function Bp({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: i }) {
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
function Ts(e, t, n, o, r, i) {
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
    const { source: r, target: i, sourceHandle: s = null, targetHandle: c = null } = o, d = { edgeId: o.id, source: r, target: i, sourceHandle: s, targetHandle: c }, l = `${r}-${s}--${i}-${c}`, f = `${i}-${c}--${r}-${s}`;
    Ts("source", d, f, e, r, s), Ts("target", d, l, e, i, c), t.set(o.id, o);
  }
}
function Hc(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : Hc(n, t) : !1;
}
function zs(e, t, n) {
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
function Wp(e, t, n, o) {
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
function Xp({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const i = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, s = Un(i, t);
  return {
    x: s.x - i.x,
    y: s.y - i.y
  };
}
function Yp({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let i = { x: null, y: null }, s = 0, c = /* @__PURE__ */ new Map(), d = !1, l = { x: 0, y: 0 }, f = null, u = !1, h = null, p = !1, y = !1, v = null;
  function x({ noDragClassName: S, handleSelector: g, domNode: w, isSelectable: E, nodeId: k, nodeClickDistance: j = 0 }) {
    h = He(w);
    function A({ x: R, y: F }) {
      const { nodeLookup: N, nodeExtent: _, snapGrid: I, snapToGrid: P, nodeOrigin: T, onNodeDrag: z, onSelectionDrag: O, onError: W, updateNodePositions: B } = t();
      i = { x: R, y: F };
      let K = !1;
      const q = c.size > 1, ne = q && _ ? li(qn(c)) : null, ae = q && P ? Xp({
        dragItems: c,
        snapGrid: I,
        x: R,
        y: F
      }) : null;
      for (const [G, L] of c) {
        if (!N.has(G))
          continue;
        let U = { x: R - L.distance.x, y: F - L.distance.y };
        P && (U = ae ? {
          x: Math.round(U.x + ae.x),
          y: Math.round(U.y + ae.y)
        } : Un(U, I));
        let ie = null;
        if (q && _ && !L.extent && ne) {
          const { positionAbsolute: ee } = L.internals, le = ee.x - ne.x + _[0][0], H = ee.x + L.measured.width - ne.x2 + _[1][0], Q = ee.y - ne.y + _[0][1], fe = ee.y + L.measured.height - ne.y2 + _[1][1];
          ie = [
            [le, Q],
            [H, fe]
          ];
        }
        const { position: se, positionAbsolute: J } = Sc({
          nodeId: G,
          nextPosition: U,
          nodeLookup: N,
          nodeExtent: ie || _,
          nodeOrigin: T,
          onError: W
        });
        K = K || L.position.x !== se.x || L.position.y !== se.y, L.position = se, L.internals.positionAbsolute = J;
      }
      if (y = y || K, !!K && (B(c, !0), v && (o || z || !k && O))) {
        const [G, L] = Fr({
          nodeId: k,
          dragItems: c,
          nodeLookup: N
        });
        o?.(v, c, G, L), z?.(v, G, L), k || O?.(v, L);
      }
    }
    async function $() {
      if (!f)
        return;
      const { transform: R, panBy: F, autoPanSpeed: N, autoPanOnNodeDrag: _ } = t();
      if (!_) {
        d = !1, cancelAnimationFrame(s);
        return;
      }
      const [I, P] = Mi(l, f, N);
      (I !== 0 || P !== 0) && (i.x = (i.x ?? 0) - I / R[2], i.y = (i.y ?? 0) - P / R[2], await F({ x: I, y: P }) && A(i)), s = requestAnimationFrame($);
    }
    function D(R) {
      const { nodeLookup: F, multiSelectionActive: N, nodesDraggable: _, transform: I, snapGrid: P, snapToGrid: T, selectNodesOnDrag: z, onNodeDragStart: O, onSelectionDragStart: W, unselectNodesAndEdges: B } = t();
      u = !0, (!z || !E) && !N && k && (F.get(k)?.selected || B()), E && z && k && e?.(k);
      const K = An(R.sourceEvent, { transform: I, snapGrid: P, snapToGrid: T, containerBounds: f });
      if (i = K, c = Wp(F, _, K, k), c.size > 0 && (n || O || !k && W)) {
        const [q, ne] = Fr({
          nodeId: k,
          dragItems: c,
          nodeLookup: F
        });
        n?.(R.sourceEvent, c, q, ne), O?.(R.sourceEvent, q, ne), k || W?.(R.sourceEvent, ne);
      }
    }
    const C = nc().clickDistance(j).on("start", (R) => {
      const { domNode: F, nodeDragThreshold: N, transform: _, snapGrid: I, snapToGrid: P } = t();
      f = F?.getBoundingClientRect() || null, p = !1, y = !1, v = R.sourceEvent, N === 0 && D(R), i = An(R.sourceEvent, { transform: _, snapGrid: I, snapToGrid: P, containerBounds: f }), l = Ke(R.sourceEvent, f);
    }).on("drag", (R) => {
      const { autoPanOnNodeDrag: F, transform: N, snapGrid: _, snapToGrid: I, nodeDragThreshold: P, nodeLookup: T } = t(), z = An(R.sourceEvent, { transform: N, snapGrid: _, snapToGrid: I, containerBounds: f });
      if (v = R.sourceEvent, (R.sourceEvent.type === "touchmove" && R.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      k && !T.has(k)) && (p = !0), !p) {
        if (!d && F && u && (d = !0, $()), !u) {
          const O = Ke(R.sourceEvent, f), W = O.x - l.x, B = O.y - l.y;
          Math.sqrt(W * W + B * B) > P && D(R);
        }
        (i.x !== z.xSnapped || i.y !== z.ySnapped) && c && u && (l = Ke(R.sourceEvent, f), A(z));
      }
    }).on("end", (R) => {
      if (!u || p) {
        p && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (d = !1, u = !1, cancelAnimationFrame(s), c.size > 0) {
        const { nodeLookup: F, updateNodePositions: N, onNodeDragStop: _, onSelectionDragStop: I } = t();
        if (y && (N(c, !1), y = !1), r || _ || !k && I) {
          const [P, T] = Fr({
            nodeId: k,
            dragItems: c,
            nodeLookup: F,
            dragging: !1
          });
          r?.(R.sourceEvent, c, P, T), _?.(R.sourceEvent, P, T), k || I?.(R.sourceEvent, T);
        }
      }
    }).filter((R) => {
      const F = R.target;
      return !R.button && (!S || !zs(F, `.${S}`, w)) && (!g || zs(F, g, w));
    });
    h.call(C);
  }
  function m() {
    h?.on(".drag", null);
  }
  return {
    update: x,
    destroy: m
  };
}
function qp(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const i of t.values())
    Ln(r, tn(i)) > 0 && o.push(i);
  return o;
}
const Up = 250;
function Zp(e, t, n, o) {
  let r = [], i = 1 / 0;
  const s = qp(e, n, t + Up);
  for (const c of s) {
    const d = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of d) {
      if (o.nodeId === l.nodeId && o.type === l.type && o.id === l.id)
        continue;
      const { x: f, y: u } = zt(c, l, l.position, !0), h = Math.sqrt(Math.pow(f - e.x, 2) + Math.pow(u - e.y, 2));
      h > t || (h < i ? (r = [{ ...l, x: f, y: u }], i = h) : h === i && r.push({ ...l, x: f, y: u }));
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
  return d && i ? { ...d, ...zt(s, d, d.position, !0) } : d;
}
function Fc(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Kp(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const Bc = () => !0;
function Gp(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: i, isTarget: s, domNode: c, nodeLookup: d, lib: l, autoPanOnConnect: f, flowId: u, panBy: h, cancelConnection: p, onConnectStart: y, onConnect: v, onConnectEnd: x, isValidConnection: m = Bc, onReconnectEnd: S, updateConnection: g, getTransform: w, getFromHandle: E, autoPanSpeed: k, dragThreshold: j = 1, handleDomNode: A }) {
  const $ = _c(e.target);
  let D = 0, C;
  const { x: R, y: F } = Ke(e), N = Fc(i, A), _ = c?.getBoundingClientRect();
  let I = !1;
  if (!_ || !N)
    return;
  const P = Oc(r, N, o, d, t);
  if (!P)
    return;
  let T = Ke(e, _), z = !1, O = null, W = !1, B = null;
  function K() {
    if (!f || !_)
      return;
    const [se, J] = Mi(T, _, k);
    h({ x: se, y: J }), D = requestAnimationFrame(K);
  }
  const q = {
    ...P,
    nodeId: r,
    type: N,
    position: P.position
  }, ne = d.get(r);
  let G = {
    inProgress: !0,
    isValid: null,
    from: zt(ne, q, te.Left, !0),
    fromHandle: q,
    fromPosition: q.position,
    fromNode: ne,
    to: T,
    toHandle: null,
    toPosition: Es[q.position],
    toNode: null,
    pointer: T
  };
  function L() {
    I = !0, g(G), y?.(e, { nodeId: r, handleId: o, handleType: N });
  }
  j === 0 && L();
  function U(se) {
    if (!I) {
      const { x: fe, y: me } = Ke(se), Ee = fe - R, Me = me - F;
      if (!(Ee * Ee + Me * Me > j * j))
        return;
      L();
    }
    if (!E() || !q) {
      ie(se);
      return;
    }
    const J = w();
    T = Ke(se, _), C = Zp(sn(T, J, !1, [1, 1]), n, d, q), z || (K(), z = !0);
    const ee = Wc(se, {
      handle: C,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: s ? "target" : "source",
      isValidConnection: m,
      doc: $,
      lib: l,
      flowId: u,
      nodeLookup: d
    });
    B = ee.handleDomNode, O = ee.connection, W = Kp(!!C, ee.isValid);
    const le = d.get(r), H = le ? zt(le, q, te.Left, !0) : G.from, Q = {
      ...G,
      from: H,
      isValid: W,
      to: ee.toHandle && W ? nn({ x: ee.toHandle.x, y: ee.toHandle.y }, J) : T,
      toHandle: ee.toHandle,
      toPosition: W && ee.toHandle ? ee.toHandle.position : Es[q.position],
      toNode: ee.toHandle ? d.get(ee.toHandle.nodeId) : null,
      pointer: T
    };
    g(Q), G = Q;
  }
  function ie(se) {
    if (!("touches" in se && se.touches.length > 0)) {
      if (I) {
        (C || B) && O && W && v?.(O);
        const { inProgress: J, ...ee } = G, le = {
          ...ee,
          toPosition: G.toHandle ? G.toPosition : null
        };
        x?.(se, le), i && S?.(se, le);
      }
      p(), cancelAnimationFrame(D), z = !1, W = !1, O = null, B = null, $.removeEventListener("mousemove", U), $.removeEventListener("mouseup", ie), $.removeEventListener("touchmove", U), $.removeEventListener("touchend", ie);
    }
  }
  $.addEventListener("mousemove", U), $.addEventListener("mouseup", ie), $.addEventListener("touchmove", U), $.addEventListener("touchend", ie);
}
function Wc(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: i, doc: s, lib: c, flowId: d, isValidConnection: l = Bc, nodeLookup: f }) {
  const u = i === "target", h = t ? s.querySelector(`.${c}-flow__handle[data-id="${d}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: p, y } = Ke(e), v = s.elementFromPoint(p, y), x = v?.classList.contains(`${c}-flow__handle`) ? v : h, m = {
    handleDomNode: x,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (x) {
    const S = Fc(void 0, x), g = x.getAttribute("data-nodeid"), w = x.getAttribute("data-handleid"), E = x.classList.contains("connectable"), k = x.classList.contains("connectableend");
    if (!g || !S)
      return m;
    const j = {
      source: u ? g : o,
      sourceHandle: u ? w : r,
      target: u ? o : g,
      targetHandle: u ? r : w
    };
    m.connection = j;
    const $ = E && k && (n === Qt.Strict ? u && S === "source" || !u && S === "target" : g !== o || w !== r);
    m.isValid = $ && l(j), m.toHandle = Oc(g, S, w, f, n, !0);
  }
  return m;
}
const fi = {
  onPointerDown: Gp,
  isValid: Wc
};
function Jp({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = He(e);
  function i({ translateExtent: c, width: d, height: l, zoomStep: f = 1, pannable: u = !0, zoomable: h = !0, inversePan: p = !1 }) {
    const y = (g) => {
      if (g.sourceEvent.type !== "wheel" || !t)
        return;
      const w = n(), E = g.sourceEvent.ctrlKey && Vn() ? 10 : 1, k = -g.sourceEvent.deltaY * (g.sourceEvent.deltaMode === 1 ? 0.05 : g.sourceEvent.deltaMode ? 1 : 2e-3) * f, j = w[2] * Math.pow(2, k * E);
      t.scaleTo(j);
    };
    let v = [0, 0];
    const x = (g) => {
      (g.sourceEvent.type === "mousedown" || g.sourceEvent.type === "touchstart") && (v = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ]);
    }, m = (g) => {
      const w = n();
      if (g.sourceEvent.type !== "mousemove" && g.sourceEvent.type !== "touchmove" || !t)
        return;
      const E = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ], k = [E[0] - v[0], E[1] - v[1]];
      v = E;
      const j = o() * Math.max(w[2], Math.log(w[2])) * (p ? -1 : 1), A = {
        x: w[0] - k[0] * j,
        y: w[1] - k[1] * j
      }, $ = [
        [0, 0],
        [d, l]
      ];
      t.setViewportConstrained({
        x: A.x,
        y: A.y,
        zoom: w[2]
      }, $, c);
    }, S = mc().on("start", x).on("zoom", u ? m : null).on("zoom.wheel", h ? y : null);
    r.call(S, {});
  }
  function s() {
    r.on("zoom", null);
  }
  return {
    update: i,
    destroy: s,
    pointer: qe
  };
}
const cr = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), Br = ({ x: e, y: t, zoom: n }) => ir.translate(e, t).scale(n), Yt = (e, t) => e.target.closest(`.${t}`), Xc = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Qp = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, Wr = (e, t = 0, n = Qp, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, Yc = (e) => {
  const t = e.ctrlKey && Vn() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function eg({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: i, zoomOnPinch: s, onPanZoomStart: c, onPanZoom: d, onPanZoomEnd: l }) {
  return (f) => {
    if (Yt(f, t))
      return f.ctrlKey && f.preventDefault(), !1;
    f.preventDefault(), f.stopImmediatePropagation();
    const u = n.property("__zoom").k || 1;
    if (f.ctrlKey && s) {
      const x = qe(f), m = Yc(f), S = u * Math.pow(2, m);
      o.scaleTo(n, S, x, f);
      return;
    }
    const h = f.deltaMode === 1 ? 20 : 1;
    let p = r === At.Vertical ? 0 : f.deltaX * h, y = r === At.Horizontal ? 0 : f.deltaY * h;
    !Vn() && f.shiftKey && r !== At.Vertical && (p = f.deltaY * h, y = 0), o.translateBy(
      n,
      -(p / u) * i,
      -(y / u) * i,
      // @ts-ignore
      { internal: !0 }
    );
    const v = cr(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (d?.(f, v), e.panScrollTimeout = setTimeout(() => {
      l?.(f, v), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(f, v));
  };
}
function tg({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const i = o.type === "wheel", s = !t && i && !o.ctrlKey, c = Yt(o, e);
    if (o.ctrlKey && i && c && o.preventDefault(), s || c)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function ng({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = cr(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function og({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (i) => {
    e.usedRightMouseButton = !!(n && Xc(t, e.mouseButton ?? 0)), i.sourceEvent?.sync || o([i.transform.x, i.transform.y, i.transform.k]), r && !i.sourceEvent?.internal && r?.(i.sourceEvent, cr(i.transform));
  };
}
function rg({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: i }) {
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
function ig({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: i, userSelectionActive: s, noWheelClassName: c, noPanClassName: d, lib: l, connectionInProgress: f }) {
  return (u) => {
    const h = e || t, p = n && u.ctrlKey, y = u.type === "wheel";
    if (u.button === 1 && u.type === "mousedown" && (Yt(u, `${l}-flow__node`) || Yt(u, `${l}-flow__edge`)))
      return !0;
    if (!o && !h && !r && !i && !n || s || f && !y || Yt(u, c) && y || Yt(u, d) && (!y || r && y && !e) || !n && u.ctrlKey && y)
      return !1;
    if (!n && u.type === "touchstart" && u.touches?.length > 1)
      return u.preventDefault(), !1;
    if (!h && !r && !p && y || !o && (u.type === "mousedown" || u.type === "touchstart") || Array.isArray(o) && !o.includes(u.button) && u.type === "mousedown")
      return !1;
    const v = Array.isArray(o) && o.includes(u.button) || !u.button || u.button <= 1;
    return (!u.ctrlKey || y) && v;
  };
}
function sg({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: i, onPanZoomStart: s, onPanZoomEnd: c, onDraggingChange: d }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, f = e.getBoundingClientRect(), u = mc().scaleExtent([t, n]).translateExtent(o), h = He(e).call(u);
  S({
    x: r.x,
    y: r.y,
    zoom: en(r.zoom, t, n)
  }, [
    [0, 0],
    [f.width, f.height]
  ], o);
  const p = h.on("wheel.zoom"), y = h.on("dblclick.zoom");
  u.wheelDelta(Yc);
  async function v(C, R) {
    return h ? new Promise((F) => {
      u?.interpolate(R?.interpolate === "linear" ? _n : Mo).transform(Wr(h, R?.duration, R?.ease, () => F(!0)), C);
    }) : !1;
  }
  function x({ noWheelClassName: C, noPanClassName: R, onPaneContextMenu: F, userSelectionActive: N, panOnScroll: _, panOnDrag: I, panOnScrollMode: P, panOnScrollSpeed: T, preventScrolling: z, zoomOnPinch: O, zoomOnScroll: W, zoomOnDoubleClick: B, zoomActivationKeyPressed: K, lib: q, onTransformChange: ne, connectionInProgress: ae, paneClickDistance: G, selectionOnDrag: L }) {
    N && !l.isZoomingOrPanning && m();
    const U = _ && !K && !N;
    u.clickDistance(L ? 1 / 0 : !Ze(G) || G < 0 ? 0 : G);
    const ie = U ? eg({
      zoomPanValues: l,
      noWheelClassName: C,
      d3Selection: h,
      d3Zoom: u,
      panOnScrollMode: P,
      panOnScrollSpeed: T,
      zoomOnPinch: O,
      onPanZoomStart: s,
      onPanZoom: i,
      onPanZoomEnd: c
    }) : tg({
      noWheelClassName: C,
      preventScrolling: z,
      d3ZoomHandler: p
    });
    h.on("wheel.zoom", ie, { passive: !1 });
    const se = ng({
      zoomPanValues: l,
      onDraggingChange: d,
      onPanZoomStart: s
    });
    u.on("start", se);
    const J = og({
      zoomPanValues: l,
      panOnDrag: I,
      onPaneContextMenu: !!F,
      onPanZoom: i,
      onTransformChange: ne
    });
    u.on("zoom", J);
    const ee = rg({
      zoomPanValues: l,
      panOnDrag: I,
      panOnScroll: _,
      onPaneContextMenu: F,
      onPanZoomEnd: c,
      onDraggingChange: d
    });
    u.on("end", ee);
    const le = ig({
      zoomActivationKeyPressed: K,
      panOnDrag: I,
      zoomOnScroll: W,
      panOnScroll: _,
      zoomOnDoubleClick: B,
      zoomOnPinch: O,
      userSelectionActive: N,
      noPanClassName: R,
      noWheelClassName: C,
      lib: q,
      connectionInProgress: ae
    });
    u.filter(le), B ? h.on("dblclick.zoom", y) : h.on("dblclick.zoom", null);
  }
  function m() {
    u.on("zoom", null);
  }
  async function S(C, R, F) {
    const N = Br(C), _ = u?.constrain()(N, R, F);
    return _ && await v(_), _;
  }
  async function g(C, R) {
    const F = Br(C);
    return await v(F, R), F;
  }
  function w(C) {
    if (h) {
      const R = Br(C), F = h.property("__zoom");
      (F.k !== C.zoom || F.x !== C.x || F.y !== C.y) && u?.transform(h, R, null, { sync: !0 });
    }
  }
  function E() {
    const C = h ? yc(h.node()) : { x: 0, y: 0, k: 1 };
    return { x: C.x, y: C.y, zoom: C.k };
  }
  async function k(C, R) {
    return h ? new Promise((F) => {
      u?.interpolate(R?.interpolate === "linear" ? _n : Mo).scaleTo(Wr(h, R?.duration, R?.ease, () => F(!0)), C);
    }) : !1;
  }
  async function j(C, R) {
    return h ? new Promise((F) => {
      u?.interpolate(R?.interpolate === "linear" ? _n : Mo).scaleBy(Wr(h, R?.duration, R?.ease, () => F(!0)), C);
    }) : !1;
  }
  function A(C) {
    u?.scaleExtent(C);
  }
  function $(C) {
    u?.translateExtent(C);
  }
  function D(C) {
    const R = !Ze(C) || C < 0 ? 0 : C;
    u?.clickDistance(R);
  }
  return {
    update: x,
    destroy: m,
    setViewport: g,
    setViewportConstrained: S,
    getViewport: E,
    scaleTo: k,
    scaleBy: j,
    setScaleExtent: A,
    setTranslateExtent: $,
    syncViewport: w,
    setClickDistance: D
  };
}
var on;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(on || (on = {}));
function ag({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: i }) {
  const s = e - t, c = n - o, d = [s > 0 ? 1 : s < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return s && r && (d[0] = d[0] * -1), c && i && (d[1] = d[1] * -1), d;
}
function Rs(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: r
  };
}
function ht(e, t) {
  return Math.max(0, t - e);
}
function pt(e, t) {
  return Math.max(0, e - t);
}
function ko(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function Ls(e, t) {
  return e ? !t : t;
}
function cg(e, t, n, o, r, i, s, c) {
  let { affectsX: d, affectsY: l } = t;
  const { isHorizontal: f, isVertical: u } = t, h = f && u, { xSnapped: p, ySnapped: y } = n, { minWidth: v, maxWidth: x, minHeight: m, maxHeight: S } = o, { x: g, y: w, width: E, height: k, aspectRatio: j } = e;
  let A = Math.floor(f ? p - e.pointerX : 0), $ = Math.floor(u ? y - e.pointerY : 0);
  const D = E + (d ? -A : A), C = k + (l ? -$ : $), R = -i[0] * E, F = -i[1] * k;
  let N = ko(D, v, x), _ = ko(C, m, S);
  if (s) {
    let T = 0, z = 0;
    d && A < 0 ? T = ht(g + A + R, s[0][0]) : !d && A > 0 && (T = pt(g + D + R, s[1][0])), l && $ < 0 ? z = ht(w + $ + F, s[0][1]) : !l && $ > 0 && (z = pt(w + C + F, s[1][1])), N = Math.max(N, T), _ = Math.max(_, z);
  }
  if (c) {
    let T = 0, z = 0;
    d && A > 0 ? T = pt(g + A, c[0][0]) : !d && A < 0 && (T = ht(g + D, c[1][0])), l && $ > 0 ? z = pt(w + $, c[0][1]) : !l && $ < 0 && (z = ht(w + C, c[1][1])), N = Math.max(N, T), _ = Math.max(_, z);
  }
  if (r) {
    if (f) {
      const T = ko(D / j, m, S) * j;
      if (N = Math.max(N, T), s) {
        let z = 0;
        !d && !l || d && !l && h ? z = pt(w + F + D / j, s[1][1]) * j : z = ht(w + F + (d ? A : -A) / j, s[0][1]) * j, N = Math.max(N, z);
      }
      if (c) {
        let z = 0;
        !d && !l || d && !l && h ? z = ht(w + D / j, c[1][1]) * j : z = pt(w + (d ? A : -A) / j, c[0][1]) * j, N = Math.max(N, z);
      }
    }
    if (u) {
      const T = ko(C * j, v, x) / j;
      if (_ = Math.max(_, T), s) {
        let z = 0;
        !d && !l || l && !d && h ? z = pt(g + C * j + R, s[1][0]) / j : z = ht(g + (l ? $ : -$) * j + R, s[0][0]) / j, _ = Math.max(_, z);
      }
      if (c) {
        let z = 0;
        !d && !l || l && !d && h ? z = ht(g + C * j, c[1][0]) / j : z = pt(g + (l ? $ : -$) * j, c[0][0]) / j, _ = Math.max(_, z);
      }
    }
  }
  $ = $ + ($ < 0 ? _ : -_), A = A + (A < 0 ? N : -N), r && (h ? D > C * j ? $ = (Ls(d, l) ? -A : A) / j : A = (Ls(d, l) ? -$ : $) * j : f ? ($ = A / j, l = d) : (A = $ * j, d = l));
  const I = d ? g + A : g, P = l ? w + $ : w;
  return {
    width: E + (d ? -A : A),
    height: k + (l ? -$ : $),
    x: i[0] * A * (d ? -1 : 1) + I,
    y: i[1] * $ * (l ? -1 : 1) + P
  };
}
const qc = { width: 0, height: 0, x: 0, y: 0 }, lg = {
  ...qc,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function dg(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, i = e.measured.width ?? 0, s = e.measured.height ?? 0, c = n[0] * i, d = n[1] * s;
  return [
    [o - c, r - d],
    [o + i - c, r + s - d]
  ];
}
function ug({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const i = He(e);
  let s = {
    controlDirection: Rs("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: l, boundaries: f, keepAspectRatio: u, resizeDirection: h, onResizeStart: p, onResize: y, onResizeEnd: v, shouldResize: x }) {
    let m = { ...qc }, S = { ...lg };
    s = {
      boundaries: f,
      resizeDirection: h,
      keepAspectRatio: u,
      controlDirection: Rs(l)
    };
    let g, w = null, E = [], k, j, A, $ = !1;
    const D = nc().on("start", (C) => {
      const { nodeLookup: R, transform: F, snapGrid: N, snapToGrid: _, nodeOrigin: I, paneDomNode: P } = n();
      if (g = R.get(t), !g)
        return;
      w = P?.getBoundingClientRect() ?? null;
      const { xSnapped: T, ySnapped: z } = An(C.sourceEvent, {
        transform: F,
        snapGrid: N,
        snapToGrid: _,
        containerBounds: w
      });
      m = {
        width: g.measured.width ?? 0,
        height: g.measured.height ?? 0,
        x: g.position.x ?? 0,
        y: g.position.y ?? 0
      }, S = {
        ...m,
        pointerX: T,
        pointerY: z,
        aspectRatio: m.width / m.height
      }, k = void 0, j = Tt(g.extent) ? g.extent : void 0, g.parentId && (g.extent === "parent" || g.expandParent) && (k = R.get(g.parentId)), k && g.extent === "parent" && (j = [
        [0, 0],
        [k.measured.width, k.measured.height]
      ]), E = [], A = void 0;
      for (const [O, W] of R)
        if (W.parentId === t && (E.push({
          id: O,
          position: { ...W.position },
          extent: W.extent
        }), W.extent === "parent" || W.expandParent)) {
          const B = dg(W, g, W.origin ?? I);
          A ? A = [
            [Math.min(B[0][0], A[0][0]), Math.min(B[0][1], A[0][1])],
            [Math.max(B[1][0], A[1][0]), Math.max(B[1][1], A[1][1])]
          ] : A = B;
        }
      p?.(C, { ...m });
    }).on("drag", (C) => {
      const { transform: R, snapGrid: F, snapToGrid: N, nodeOrigin: _ } = n(), I = An(C.sourceEvent, {
        transform: R,
        snapGrid: F,
        snapToGrid: N,
        containerBounds: w
      }), P = [];
      if (!g)
        return;
      const { x: T, y: z, width: O, height: W } = m, B = {}, K = g.origin ?? _, { width: q, height: ne, x: ae, y: G } = cg(S, s.controlDirection, I, s.boundaries, s.keepAspectRatio, K, j, A), L = q !== O, U = ne !== W, ie = ae !== T && L, se = G !== z && U;
      if (!ie && !se && !L && !U)
        return;
      if ((ie || se || K[0] === 1 || K[1] === 1) && (B.x = ie ? ae : m.x, B.y = se ? G : m.y, m.x = B.x, m.y = B.y, E.length > 0)) {
        const H = ae - T, Q = G - z;
        for (const fe of E)
          fe.position = {
            x: fe.position.x - H + K[0] * (q - O),
            y: fe.position.y - Q + K[1] * (ne - W)
          }, P.push(fe);
      }
      if ((L || U) && (B.width = L && (!s.resizeDirection || s.resizeDirection === "horizontal") ? q : m.width, B.height = U && (!s.resizeDirection || s.resizeDirection === "vertical") ? ne : m.height, m.width = B.width, m.height = B.height), k && g.expandParent) {
        const H = K[0] * (B.width ?? 0);
        B.x && B.x < H && (m.x = H, S.x = S.x - (B.x - H));
        const Q = K[1] * (B.height ?? 0);
        B.y && B.y < Q && (m.y = Q, S.y = S.y - (B.y - Q));
      }
      const J = ag({
        width: m.width,
        prevWidth: O,
        height: m.height,
        prevHeight: W,
        affectsX: s.controlDirection.affectsX,
        affectsY: s.controlDirection.affectsY
      }), ee = { ...m, direction: J };
      x?.(C, ee) !== !1 && ($ = !0, y?.(C, ee), o(B, P));
    }).on("end", (C) => {
      $ && (v?.(C, { ...m }), r?.({ ...m }), $ = !1);
    });
    i.call(D);
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
var Vs;
function fg() {
  if (Vs) return Ur;
  Vs = 1;
  var e = wt;
  function t(u, h) {
    return u === h && (u !== 0 || 1 / u === 1 / h) || u !== u && h !== h;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, i = e.useLayoutEffect, s = e.useDebugValue;
  function c(u, h) {
    var p = h(), y = o({ inst: { value: p, getSnapshot: h } }), v = y[0].inst, x = y[1];
    return i(
      function() {
        v.value = p, v.getSnapshot = h, d(v) && x({ inst: v });
      },
      [u, p, h]
    ), r(
      function() {
        return d(v) && x({ inst: v }), u(function() {
          d(v) && x({ inst: v });
        });
      },
      [u]
    ), s(p), p;
  }
  function d(u) {
    var h = u.getSnapshot;
    u = u.value;
    try {
      var p = h();
      return !n(u, p);
    } catch {
      return !0;
    }
  }
  function l(u, h) {
    return h();
  }
  var f = typeof window > "u" || typeof window.document > "u" || typeof window.document.createElement > "u" ? l : c;
  return Ur.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : f, Ur;
}
var Hs;
function hg() {
  return Hs || (Hs = 1, qr.exports = fg()), qr.exports;
}
var Os;
function pg() {
  if (Os) return Yr;
  Os = 1;
  var e = wt, t = hg();
  function n(l, f) {
    return l === f && (l !== 0 || 1 / l === 1 / f) || l !== l && f !== f;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, i = e.useRef, s = e.useEffect, c = e.useMemo, d = e.useDebugValue;
  return Yr.useSyncExternalStoreWithSelector = function(l, f, u, h, p) {
    var y = i(null);
    if (y.current === null) {
      var v = { hasValue: !1, value: null };
      y.current = v;
    } else v = y.current;
    y = c(
      function() {
        function m(k) {
          if (!S) {
            if (S = !0, g = k, k = h(k), p !== void 0 && v.hasValue) {
              var j = v.value;
              if (p(j, k))
                return w = j;
            }
            return w = k;
          }
          if (j = w, o(g, k)) return j;
          var A = h(k);
          return p !== void 0 && p(j, A) ? (g = k, j) : (g = k, w = A);
        }
        var S = !1, g, w, E = u === void 0 ? null : u;
        return [
          function() {
            return m(f());
          },
          E === null ? void 0 : function() {
            return m(E());
          }
        ];
      },
      [f, u, h, p]
    );
    var x = r(l, y[0], y[1]);
    return s(
      function() {
        v.hasValue = !0, v.value = x;
      },
      [x]
    ), d(x), x;
  }, Yr;
}
var Fs;
function gg() {
  return Fs || (Fs = 1, Xr.exports = pg()), Xr.exports;
}
var yg = gg();
const mg = /* @__PURE__ */ Dd(yg), xg = {}, Bs = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (f, u) => {
    const h = typeof f == "function" ? f(t) : f;
    if (!Object.is(h, t)) {
      const p = t;
      t = u ?? (typeof h != "object" || h === null) ? h : Object.assign({}, t, h), n.forEach((y) => y(t, p));
    }
  }, r = () => t, d = { setState: o, getState: r, getInitialState: () => l, subscribe: (f) => (n.add(f), () => n.delete(f)), destroy: () => {
    (xg ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(o, r, d);
  return d;
}, wg = (e) => e ? Bs(e) : Bs, { useDebugValue: vg } = wt, { useSyncExternalStoreWithSelector: bg } = mg, Ng = (e) => e;
function Uc(e, t = Ng, n) {
  const o = bg(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return vg(o), o;
}
const Ws = (e, t) => {
  const n = wg(e), o = (r, i = t) => Uc(n, r, i);
  return Object.assign(o, n), o;
}, Sg = (e, t) => e ? Ws(e, t) : Ws;
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
var Xs;
function Eg() {
  if (Xs) return Pe;
  Xs = 1;
  var e = wt;
  function t(d) {
    var l = "https://react.dev/errors/" + d;
    if (1 < arguments.length) {
      l += "?args[]=" + encodeURIComponent(arguments[1]);
      for (var f = 2; f < arguments.length; f++)
        l += "&args[]=" + encodeURIComponent(arguments[f]);
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
  function i(d, l, f) {
    var u = 3 < arguments.length && arguments[3] !== void 0 ? arguments[3] : null;
    return {
      $$typeof: r,
      key: u == null ? null : "" + u,
      children: d,
      containerInfo: l,
      implementation: f
    };
  }
  var s = e.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE;
  function c(d, l) {
    if (d === "font") return "";
    if (typeof l == "string")
      return l === "use-credentials" ? l : "";
  }
  return Pe.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, Pe.createPortal = function(d, l) {
    var f = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!l || l.nodeType !== 1 && l.nodeType !== 9 && l.nodeType !== 11)
      throw Error(t(299));
    return i(d, l, null, f);
  }, Pe.flushSync = function(d) {
    var l = s.T, f = o.p;
    try {
      if (s.T = null, o.p = 2, d) return d();
    } finally {
      s.T = l, o.p = f, o.d.f();
    }
  }, Pe.preconnect = function(d, l) {
    typeof d == "string" && (l ? (l = l.crossOrigin, l = typeof l == "string" ? l === "use-credentials" ? l : "" : void 0) : l = null, o.d.C(d, l));
  }, Pe.prefetchDNS = function(d) {
    typeof d == "string" && o.d.D(d);
  }, Pe.preinit = function(d, l) {
    if (typeof d == "string" && l && typeof l.as == "string") {
      var f = l.as, u = c(f, l.crossOrigin), h = typeof l.integrity == "string" ? l.integrity : void 0, p = typeof l.fetchPriority == "string" ? l.fetchPriority : void 0;
      f === "style" ? o.d.S(
        d,
        typeof l.precedence == "string" ? l.precedence : void 0,
        {
          crossOrigin: u,
          integrity: h,
          fetchPriority: p
        }
      ) : f === "script" && o.d.X(d, {
        crossOrigin: u,
        integrity: h,
        fetchPriority: p,
        nonce: typeof l.nonce == "string" ? l.nonce : void 0
      });
    }
  }, Pe.preinitModule = function(d, l) {
    if (typeof d == "string")
      if (typeof l == "object" && l !== null) {
        if (l.as == null || l.as === "script") {
          var f = c(
            l.as,
            l.crossOrigin
          );
          o.d.M(d, {
            crossOrigin: f,
            integrity: typeof l.integrity == "string" ? l.integrity : void 0,
            nonce: typeof l.nonce == "string" ? l.nonce : void 0
          });
        }
      } else l == null && o.d.M(d);
  }, Pe.preload = function(d, l) {
    if (typeof d == "string" && typeof l == "object" && l !== null && typeof l.as == "string") {
      var f = l.as, u = c(f, l.crossOrigin);
      o.d.L(d, f, {
        crossOrigin: u,
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
        var f = c(l.as, l.crossOrigin);
        o.d.m(d, {
          as: typeof l.as == "string" && l.as !== "script" ? l.as : void 0,
          crossOrigin: f,
          integrity: typeof l.integrity == "string" ? l.integrity : void 0
        });
      } else o.d.m(d);
  }, Pe.requestFormReset = function(d) {
    o.d.r(d);
  }, Pe.unstable_batchedUpdates = function(d, l) {
    return d(l);
  }, Pe.useFormState = function(d, l, f) {
    return s.H.useFormState(d, l, f);
  }, Pe.useFormStatus = function() {
    return s.H.useHostTransitionStatus();
  }, Pe.version = "19.2.7", Pe;
}
var Ys;
function kg() {
  if (Ys) return Zr.exports;
  Ys = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), Zr.exports = Eg(), Zr.exports;
}
var jg = kg();
const lr = bi(null), Cg = lr.Provider, Zc = Fe.error001("react");
function ue(e, t) {
  const n = Bn(lr);
  if (n === null)
    throw new Error(Zc);
  return Uc(n, e, t);
}
function we() {
  const e = Bn(lr);
  if (e === null)
    throw new Error(Zc);
  return ge(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const qs = { display: "none" }, Ig = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, Kc = "react-flow__node-desc", Gc = "react-flow__edge-desc", _g = "react-flow__aria-live", Ag = (e) => e.ariaLiveMessage, Mg = (e) => e.ariaLabelConfig;
function Dg({ rfId: e }) {
  const t = ue(Ag);
  return a.jsx("div", { id: `${_g}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: Ig, children: t });
}
function Pg({ rfId: e, disableKeyboardA11y: t }) {
  const n = ue(Mg);
  return a.jsxs(a.Fragment, { children: [a.jsx("div", { id: `${Kc}-${e}`, style: qs, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), a.jsx("div", { id: `${Gc}-${e}`, style: qs, children: n["edge.a11yDescription.default"] }), !t && a.jsx(Dg, { rfId: e })] });
}
const dr = er(({ position: e = "top-left", children: t, className: n, style: o, ...r }, i) => {
  const s = `${e}`.split("-");
  return a.jsx("div", { className: Ce(["react-flow__panel", n, ...s]), style: o, ref: i, ...r, children: t });
});
dr.displayName = "Panel";
function $g({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : a.jsx(dr, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: a.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const Tg = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, jo = (e) => e.id;
function zg(e, t) {
  return xe(e.selectedNodes.map(jo), t.selectedNodes.map(jo)) && xe(e.selectedEdges.map(jo), t.selectedEdges.map(jo));
}
function Rg({ onSelectionChange: e }) {
  const t = we(), { selectedNodes: n, selectedEdges: o } = ue(Tg, zg);
  return oe(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((i) => i(r));
  }, [n, o, e]), null;
}
const Lg = (e) => !!e.onSelectionChangeHandlers;
function Vg({ onSelectionChange: e }) {
  const t = ue(Lg);
  return e || t ? a.jsx(Rg, { onSelectionChange: e }) : null;
}
const Jc = [0, 0], Hg = { x: 0, y: 0, zoom: 1 }, Og = [
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
], Us = [...Og, "rfId"], Fg = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), Zs = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: zn,
  nodeOrigin: Jc,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function Bg(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: i, setNodeExtent: s, reset: c, setDefaultNodesAndEdges: d } = ue(Fg, xe), l = we();
  oe(() => (d(e.defaultNodes, e.defaultEdges), () => {
    f.current = Zs, c();
  }), []);
  const f = ce(Zs);
  return oe(
    () => {
      for (const u of Us) {
        const h = e[u], p = f.current[u];
        h !== p && (typeof e[u] > "u" || (u === "nodes" ? t(h) : u === "edges" ? n(h) : u === "minZoom" ? o(h) : u === "maxZoom" ? r(h) : u === "translateExtent" ? i(h) : u === "nodeExtent" ? s(h) : u === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: Ep(h) }) : u === "fitView" ? l.setState({ fitViewQueued: h }) : u === "fitViewOptions" ? l.setState({ fitViewOptions: h }) : l.setState({ [u]: h })));
      }
      f.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    Us.map((u) => e[u])
  ), null;
}
function Ks() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function Wg(e) {
  const [t, n] = Y(e === "system" ? null : e);
  return oe(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = Ks(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : Ks()?.matches ? "dark" : "light";
}
const Gs = typeof document < "u" ? document : null;
function Hn(e = null, t = { target: Gs, actInsideInputWithModifier: !0 }) {
  const [n, o] = Y(!1), r = ce(!1), i = ce(/* @__PURE__ */ new Set([])), [s, c] = ge(() => {
    if (e !== null) {
      const l = (Array.isArray(e) ? e : [e]).filter((u) => typeof u == "string").map((u) => u.replace("+", `
`).replace(`

`, `
+`).split(`
`)), f = l.reduce((u, h) => u.concat(...h), []);
      return [l, f];
    }
    return [[], []];
  }, [e]);
  return oe(() => {
    const d = t?.target ?? Gs, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const f = (p) => {
        if (r.current = p.ctrlKey || p.metaKey || p.shiftKey || p.altKey, (!r.current || r.current && !l) && Ac(p))
          return !1;
        const v = Qs(p.code, c);
        if (i.current.add(p[v]), Js(s, i.current, !1)) {
          const x = p.composedPath?.()?.[0] || p.target, m = x?.nodeName === "BUTTON" || x?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !m) && p.preventDefault(), o(!0);
        }
      }, u = (p) => {
        const y = Qs(p.code, c);
        Js(s, i.current, !0) ? (o(!1), i.current.clear()) : i.current.delete(p[y]), p.key === "Meta" && i.current.clear(), r.current = !1;
      }, h = () => {
        i.current.clear(), o(!1);
      };
      return d?.addEventListener("keydown", f), d?.addEventListener("keyup", u), window.addEventListener("blur", h), window.addEventListener("contextmenu", h), () => {
        d?.removeEventListener("keydown", f), d?.removeEventListener("keyup", u), window.removeEventListener("blur", h), window.removeEventListener("contextmenu", h);
      };
    }
  }, [e, o]), n;
}
function Js(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function Qs(e, t) {
  return t.includes(e) ? "code" : "key";
}
const Xg = () => {
  const e = we();
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
      const { width: o, height: r, minZoom: i, maxZoom: s, panZoom: c } = e.getState(), d = Di(t, o, r, i, s, n?.padding ?? 0.1);
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
      }, f = n.snapGrid ?? r, u = n.snapToGrid ?? i;
      return sn(l, o, u, f);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: r, y: i } = o.getBoundingClientRect(), s = nn(t, n);
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
      Yg(d, c);
    n.push(c);
  }
  return r.length && r.forEach((i) => {
    i.index !== void 0 ? n.splice(i.index, 0, { ...i.item }) : n.push({ ...i.item });
  }), n;
}
function Yg(e, t) {
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
function Ct(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function qt(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [r, i] of e) {
    const s = t.has(r);
    !(i.selected === void 0 && !s) && i.selected !== s && (n && (i.selected = s), o.push(Ct(i.id, s)));
  }
  return o;
}
function ea({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((r) => [r.id, r]));
  for (const [r, i] of e.entries()) {
    const s = t.get(i.id), c = s?.internals?.userNode ?? s;
    c !== void 0 && c !== i && n.push({ id: i.id, item: i, type: "replace" }), c === void 0 && n.push({ item: i, type: "add", index: r });
  }
  for (const [r] of t)
    o.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function ta(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const nl = jc();
function ol(e, t, n = {}) {
  return _p(e, t, {
    ...n,
    onError: n.onError ?? nl
  });
}
function qg(e, t, n, o = { shouldReplaceId: !0 }) {
  return Ap(e, t, n, {
    ...o,
    onError: o.onError ?? nl
  });
}
const na = (e) => gp(e), Ug = (e) => Nc(e);
function rl(e) {
  return er(e);
}
const Zg = typeof window < "u" ? Md : oe;
function oa(e) {
  const [t, n] = Y(BigInt(0)), [o] = Y(() => Kg(() => n((r) => r + BigInt(1))));
  return Zg(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function Kg(e) {
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
const il = bi(null);
function Gg({ children: e }) {
  const t = we(), n = pe((c) => {
    const { nodes: d = [], setNodes: l, hasDefaultNodes: f, onNodesChange: u, nodeLookup: h, fitViewQueued: p, onNodesChangeMiddlewareMap: y } = t.getState();
    let v = d;
    for (const m of c)
      v = typeof m == "function" ? m(v) : m;
    let x = ea({
      items: v,
      lookup: h
    });
    for (const m of y.values())
      x = m(x);
    f && l(v), x.length > 0 ? u?.(x) : p && window.requestAnimationFrame(() => {
      const { fitViewQueued: m, nodes: S, setNodes: g } = t.getState();
      m && g(S);
    });
  }, []), o = oa(n), r = pe((c) => {
    const { edges: d = [], setEdges: l, hasDefaultEdges: f, onEdgesChange: u, edgeLookup: h } = t.getState();
    let p = d;
    for (const y of c)
      p = typeof y == "function" ? y(p) : y;
    f ? l(p) : u && u(ea({
      items: p,
      lookup: h
    }));
  }, []), i = oa(r), s = ge(() => ({ nodeQueue: o, edgeQueue: i }), []);
  return a.jsx(il.Provider, { value: s, children: e });
}
function Jg() {
  const e = Bn(il);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Qg = (e) => !!e.panZoom;
function Vi() {
  const e = Xg(), t = we(), n = Jg(), o = ue(Qg), r = ge(() => {
    const i = (u) => t.getState().nodeLookup.get(u), s = (u) => {
      n.nodeQueue.push(u);
    }, c = (u) => {
      n.edgeQueue.push(u);
    }, d = (u) => {
      const { nodeLookup: h, nodeOrigin: p } = t.getState(), y = na(u) ? u : h.get(u.id), v = y.parentId ? Ic(y.position, y.measured, y.parentId, h, p) : y.position, x = {
        ...y,
        position: v,
        width: y.measured?.width ?? y.width,
        height: y.measured?.height ?? y.height
      };
      return tn(x);
    }, l = (u, h, p = { replace: !1 }) => {
      s((y) => y.map((v) => {
        if (v.id === u) {
          const x = typeof h == "function" ? h(v) : h;
          return p.replace && na(x) ? x : { ...v, ...x };
        }
        return v;
      }));
    }, f = (u, h, p = { replace: !1 }) => {
      c((y) => y.map((v) => {
        if (v.id === u) {
          const x = typeof h == "function" ? h(v) : h;
          return p.replace && Ug(x) ? x : { ...v, ...x };
        }
        return v;
      }));
    };
    return {
      getNodes: () => t.getState().nodes.map((u) => ({ ...u })),
      getNode: (u) => i(u)?.internals.userNode,
      getInternalNode: i,
      getEdges: () => {
        const { edges: u = [] } = t.getState();
        return u.map((h) => ({ ...h }));
      },
      getEdge: (u) => t.getState().edgeLookup.get(u),
      setNodes: s,
      setEdges: c,
      addNodes: (u) => {
        const h = Array.isArray(u) ? u : [u];
        n.nodeQueue.push((p) => [...p, ...h]);
      },
      addEdges: (u) => {
        const h = Array.isArray(u) ? u : [u];
        n.edgeQueue.push((p) => [...p, ...h]);
      },
      toObject: () => {
        const { nodes: u = [], edges: h = [], transform: p } = t.getState(), [y, v, x] = p;
        return {
          nodes: u.map((m) => ({ ...m })),
          edges: h.map((m) => ({ ...m })),
          viewport: {
            x: y,
            y: v,
            zoom: x
          }
        };
      },
      deleteElements: async ({ nodes: u = [], edges: h = [] }) => {
        const { nodes: p, edges: y, onNodesDelete: v, onEdgesDelete: x, triggerNodeChanges: m, triggerEdgeChanges: S, onDelete: g, onBeforeDelete: w } = t.getState(), { nodes: E, edges: k } = await vp({
          nodesToRemove: u,
          edgesToRemove: h,
          nodes: p,
          edges: y,
          onBeforeDelete: w
        }), j = k.length > 0, A = E.length > 0;
        if (j) {
          const $ = k.map(ta);
          x?.(k), S($);
        }
        if (A) {
          const $ = E.map(ta);
          v?.(E), m($);
        }
        return (A || j) && g?.({ nodes: E, edges: k }), { deletedNodes: E, deletedEdges: k };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (u, h = !0, p) => {
        const y = js(u), v = y ? u : d(u), x = p !== void 0;
        return v ? (p || t.getState().nodes).filter((m) => {
          const S = t.getState().nodeLookup.get(m.id);
          if (S && !y && (m.id === u.id || !S.internals.positionAbsolute))
            return !1;
          const g = tn(x ? m : S), w = Ln(g, v);
          return h && w > 0 || w >= g.width * g.height || w >= v.width * v.height;
        }) : [];
      },
      isNodeIntersecting: (u, h, p = !0) => {
        const v = js(u) ? u : d(u);
        if (!v)
          return !1;
        const x = Ln(v, h);
        return p && x > 0 || x >= h.width * h.height || x >= v.width * v.height;
      },
      updateNode: l,
      updateNodeData: (u, h, p = { replace: !1 }) => {
        l(u, (y) => {
          const v = typeof h == "function" ? h(y) : h;
          return p.replace ? { ...y, data: v } : { ...y, data: { ...y.data, ...v } };
        }, p);
      },
      updateEdge: f,
      updateEdgeData: (u, h, p = { replace: !1 }) => {
        f(u, (y) => {
          const v = typeof h == "function" ? h(y) : h;
          return p.replace ? { ...y, data: v } : { ...y, data: { ...y.data, ...v } };
        }, p);
      },
      getNodesBounds: (u) => {
        const { nodeLookup: h, nodeOrigin: p } = t.getState();
        return yp(u, { nodeLookup: h, nodeOrigin: p });
      },
      getHandleConnections: ({ type: u, id: h, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}-${u}${h ? `-${h}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: u, handleId: h, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}${u ? h ? `-${u}-${h}` : `-${u}` : ""}`)?.values() ?? []),
      fitView: async (u) => {
        const h = t.getState().fitViewResolver ?? Sp();
        return t.setState({ fitViewQueued: !0, fitViewOptions: u, fitViewResolver: h }), n.nodeQueue.push((p) => [...p]), h.promise;
      }
    };
  }, []);
  return ge(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const ra = (e) => e.selected, ey = typeof window < "u" ? window : void 0;
function ty({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = we(), { deleteElements: o } = Vi(), r = Hn(e, { actInsideInputWithModifier: !1 }), i = Hn(t, { target: ey });
  oe(() => {
    if (r) {
      const { edges: s, nodes: c } = n.getState();
      o({ nodes: c.filter(ra), edges: s.filter(ra) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), oe(() => {
    n.setState({ multiSelectionActive: i });
  }, [i]);
}
function ny(e) {
  const t = we();
  oe(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = Pi(e.current);
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
}, oy = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function ry({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: i = At.Free, zoomOnDoubleClick: s = !0, panOnDrag: c = !0, defaultViewport: d, translateExtent: l, minZoom: f, maxZoom: u, zoomActivationKeyCode: h, preventScrolling: p = !0, children: y, noWheelClassName: v, noPanClassName: x, onViewportChange: m, isControlledViewport: S, paneClickDistance: g, selectionOnDrag: w }) {
  const E = we(), k = ce(null), { userSelectionActive: j, lib: A, connectionInProgress: $ } = ue(oy, xe), D = Hn(h), C = ce();
  ny(k);
  const R = pe((F) => {
    m?.({ x: F[0], y: F[1], zoom: F[2] }), S || E.setState({ transform: F });
  }, [m, S]);
  return oe(() => {
    if (k.current) {
      C.current = sg({
        domNode: k.current,
        minZoom: f,
        maxZoom: u,
        translateExtent: l,
        viewport: d,
        onDraggingChange: (I) => E.setState((P) => P.paneDragging === I ? P : { paneDragging: I }),
        onPanZoomStart: (I, P) => {
          const { onViewportChangeStart: T, onMoveStart: z } = E.getState();
          z?.(I, P), T?.(P);
        },
        onPanZoom: (I, P) => {
          const { onViewportChange: T, onMove: z } = E.getState();
          z?.(I, P), T?.(P);
        },
        onPanZoomEnd: (I, P) => {
          const { onViewportChangeEnd: T, onMoveEnd: z } = E.getState();
          z?.(I, P), T?.(P);
        }
      });
      const { x: F, y: N, zoom: _ } = C.current.getViewport();
      return E.setState({
        panZoom: C.current,
        transform: [F, N, _],
        domNode: k.current.closest(".react-flow")
      }), () => {
        C.current?.destroy();
      };
    }
  }, []), oe(() => {
    C.current?.update({
      onPaneContextMenu: e,
      zoomOnScroll: t,
      zoomOnPinch: n,
      panOnScroll: o,
      panOnScrollSpeed: r,
      panOnScrollMode: i,
      zoomOnDoubleClick: s,
      panOnDrag: c,
      zoomActivationKeyPressed: D,
      preventScrolling: p,
      noPanClassName: x,
      userSelectionActive: j,
      noWheelClassName: v,
      lib: A,
      onTransformChange: R,
      connectionInProgress: $,
      selectionOnDrag: w,
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
    D,
    p,
    x,
    j,
    v,
    A,
    R,
    $,
    w,
    g
  ]), a.jsx("div", { className: "react-flow__renderer", ref: k, style: ur, children: y });
}
const iy = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function sy() {
  const { userSelectionActive: e, userSelectionRect: t } = ue(iy, xe);
  return e && t ? a.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const Kr = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, ay = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function cy({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Rn.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: i, selectionOnDrag: s, onSelectionStart: c, onSelectionEnd: d, onPaneClick: l, onPaneContextMenu: f, onPaneScroll: u, onPaneMouseEnter: h, onPaneMouseMove: p, onPaneMouseLeave: y, children: v }) {
  const x = ce(0), m = we(), { userSelectionActive: S, elementsSelectable: g, dragging: w, connectionInProgress: E, panBy: k, autoPanSpeed: j } = ue(ay, xe), A = g && (e || S), $ = ce(null), D = ce(), C = ce(/* @__PURE__ */ new Set()), R = ce(/* @__PURE__ */ new Set()), F = ce(!1), N = ce({ x: 0, y: 0 }), _ = ce(!1), I = (L) => {
    if (F.current || E) {
      F.current = !1;
      return;
    }
    l?.(L), m.getState().resetSelectedElements(), m.setState({ nodesSelectionActive: !1 });
  }, P = (L) => {
    if (Array.isArray(o) && o?.includes(2)) {
      L.preventDefault();
      return;
    }
    f?.(L);
  }, T = u ? (L) => u(L) : void 0, z = (L) => {
    F.current && (L.stopPropagation(), F.current = !1);
  }, O = (L) => {
    const { domNode: U, transform: ie } = m.getState();
    if (D.current = U?.getBoundingClientRect(), !D.current)
      return;
    const se = L.target === $.current;
    if (!se && !!L.target.closest(".nokey") || !e || !(s && se || t) || L.button !== 0 || !L.isPrimary)
      return;
    L.target?.setPointerCapture?.(L.pointerId), F.current = !1;
    const { x: le, y: H } = Ke(L.nativeEvent, D.current), Q = sn({ x: le, y: H }, ie);
    m.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: Q.x,
        startY: Q.y,
        x: le,
        y: H
      }
    }), se || (L.stopPropagation(), L.preventDefault());
  };
  function W(L, U) {
    const { userSelectionRect: ie } = m.getState();
    if (!ie)
      return;
    const { transform: se, nodeLookup: J, edgeLookup: ee, connectionLookup: le, triggerNodeChanges: H, triggerEdgeChanges: Q, defaultEdgeOptions: fe } = m.getState(), me = { x: ie.startX, y: ie.startY }, { x: Ee, y: Me } = nn(me, se), ye = {
      startX: me.x,
      startY: me.y,
      x: L < Ee ? L : Ee,
      y: U < Me ? U : Me,
      width: Math.abs(L - Ee),
      height: Math.abs(U - Me)
    }, We = C.current, rt = R.current;
    C.current = new Set(Ai(J, ye, se, n === Rn.Partial, !0).map((ze) => ze.id)), R.current = /* @__PURE__ */ new Set();
    const it = fe?.selectable ?? !0;
    for (const ze of C.current) {
      const Re = le.get(ze);
      if (Re)
        for (const { edgeId: ke } of Re.values()) {
          const Xe = ee.get(ke);
          Xe && (Xe.selectable ?? it) && R.current.add(ke);
        }
    }
    if (!Cs(We, C.current)) {
      const ze = qt(J, C.current, !0);
      H(ze);
    }
    if (!Cs(rt, R.current)) {
      const ze = qt(ee, R.current);
      Q(ze);
    }
    m.setState({
      userSelectionRect: ye,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function B() {
    if (!r || !D.current)
      return;
    const [L, U] = Mi(N.current, D.current, j);
    k({ x: L, y: U }).then((ie) => {
      if (!F.current || !ie) {
        x.current = requestAnimationFrame(B);
        return;
      }
      const { x: se, y: J } = N.current;
      W(se, J), x.current = requestAnimationFrame(B);
    });
  }
  const K = () => {
    cancelAnimationFrame(x.current), x.current = 0, _.current = !1;
  };
  oe(() => () => K(), []);
  const q = (L) => {
    const { userSelectionRect: U, transform: ie, resetSelectedElements: se } = m.getState();
    if (!D.current || !U)
      return;
    const { x: J, y: ee } = Ke(L.nativeEvent, D.current);
    N.current = { x: J, y: ee };
    const le = nn({ x: U.startX, y: U.startY }, ie);
    if (!F.current) {
      const H = t ? 0 : i;
      if (Math.hypot(J - le.x, ee - le.y) <= H)
        return;
      se(), c?.(L);
    }
    F.current = !0, _.current || (B(), _.current = !0), W(J, ee);
  }, ne = (L) => {
    L.button === 0 && (L.target?.releasePointerCapture?.(L.pointerId), !S && L.target === $.current && m.getState().userSelectionRect && I?.(L), m.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), F.current && (d?.(L), m.setState({
      nodesSelectionActive: C.current.size > 0
    })), K());
  }, ae = (L) => {
    L.target?.releasePointerCapture?.(L.pointerId), K();
  }, G = o === !0 || Array.isArray(o) && o.includes(0);
  return a.jsxs("div", { className: Ce(["react-flow__pane", { draggable: G, dragging: w, selection: e }]), onClick: A ? void 0 : Kr(I, $), onContextMenu: Kr(P, $), onWheel: Kr(T, $), onPointerEnter: A ? void 0 : h, onPointerMove: A ? q : p, onPointerUp: A ? ne : void 0, onPointerCancel: A ? ae : void 0, onPointerDownCapture: A ? O : void 0, onClickCapture: A ? z : void 0, onPointerLeave: y, ref: $, style: ur, children: [v, a.jsx(sy, {})] });
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
  const c = we(), [d, l] = Y(!1), f = ce();
  return oe(() => {
    f.current = Yp({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (u) => {
        hi({
          id: u,
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
    if (!(t || !e.current || !f.current))
      return f.current.update({
        noDragClassName: n,
        handleSelector: o,
        domNode: e.current,
        isSelectable: i,
        nodeId: r,
        nodeClickDistance: s
      }), () => {
        f.current?.destroy();
      };
  }, [n, o, t, i, e, r, s]), d;
}
const ly = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function al() {
  const e = we();
  return pe((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: i, nodesDraggable: s, onError: c, updateNodePositions: d, nodeLookup: l, nodeOrigin: f } = e.getState(), u = /* @__PURE__ */ new Map(), h = ly(s), p = r ? i[0] : 5, y = r ? i[1] : 5, v = n.direction.x * p * n.factor, x = n.direction.y * y * n.factor;
    for (const [, m] of l) {
      if (!h(m))
        continue;
      let S = {
        x: m.internals.positionAbsolute.x + v,
        y: m.internals.positionAbsolute.y + x
      };
      r && (S = Un(S, i));
      const { position: g, positionAbsolute: w } = Sc({
        nodeId: m.id,
        nextPosition: S,
        nodeLookup: l,
        nodeExtent: o,
        nodeOrigin: f,
        onError: c
      });
      m.position = g, m.internals.positionAbsolute = w, u.set(m.id, m);
    }
    d(u);
  }, []);
}
const Hi = bi(null), dy = Hi.Provider;
Hi.Consumer;
const cl = () => Bn(Hi), uy = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), fy = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: i, connection: s } = o, { fromHandle: c, toHandle: d, isValid: l } = s, f = d?.nodeId === e && d?.id === t && d?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: f,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: i === Qt.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!r,
    valid: f && l
  };
};
function hy({ type: e = "source", position: t = te.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: i = !0, id: s, onConnect: c, children: d, className: l, onMouseDown: f, onTouchStart: u, ...h }, p) {
  const y = s || null, v = e === "target", x = we(), m = cl(), { connectOnClick: S, noPanClassName: g, rfId: w } = ue(uy, xe), { connectingFrom: E, connectingTo: k, clickConnecting: j, isPossibleEndHandle: A, connectionInProcess: $, clickConnectionInProcess: D, valid: C } = ue(fy(m, y, e), xe);
  m || x.getState().onError?.("010", Fe.error010());
  const R = (_) => {
    const { defaultEdgeOptions: I, onConnect: P, hasDefaultEdges: T } = x.getState(), z = {
      ...I,
      ..._
    };
    if (T) {
      const { edges: O, setEdges: W, onError: B } = x.getState();
      W(ol(z, O, { onError: B }));
    }
    P?.(z), c?.(z);
  }, F = (_) => {
    if (!m)
      return;
    const I = Mc(_.nativeEvent);
    if (r && (I && _.button === 0 || !I)) {
      const P = x.getState();
      fi.onPointerDown(_.nativeEvent, {
        handleDomNode: _.currentTarget,
        autoPanOnConnect: P.autoPanOnConnect,
        connectionMode: P.connectionMode,
        connectionRadius: P.connectionRadius,
        domNode: P.domNode,
        nodeLookup: P.nodeLookup,
        lib: P.lib,
        isTarget: v,
        handleId: y,
        nodeId: m,
        flowId: P.rfId,
        panBy: P.panBy,
        cancelConnection: P.cancelConnection,
        onConnectStart: P.onConnectStart,
        onConnectEnd: (...T) => x.getState().onConnectEnd?.(...T),
        updateConnection: P.updateConnection,
        onConnect: R,
        isValidConnection: n || ((...T) => x.getState().isValidConnection?.(...T) ?? !0),
        getTransform: () => x.getState().transform,
        getFromHandle: () => x.getState().connection.fromHandle,
        autoPanSpeed: P.autoPanSpeed,
        dragThreshold: P.connectionDragThreshold
      });
    }
    I ? f?.(_) : u?.(_);
  }, N = (_) => {
    const { onClickConnectStart: I, onClickConnectEnd: P, connectionClickStartHandle: T, connectionMode: z, isValidConnection: O, lib: W, rfId: B, nodeLookup: K, connection: q } = x.getState();
    if (!m || !T && !r)
      return;
    if (!T) {
      I?.(_.nativeEvent, { nodeId: m, handleId: y, handleType: e }), x.setState({ connectionClickStartHandle: { nodeId: m, type: e, id: y } });
      return;
    }
    const ne = _c(_.target), ae = n || O, { connection: G, isValid: L } = fi.isValid(_.nativeEvent, {
      handle: {
        nodeId: m,
        id: y,
        type: e
      },
      connectionMode: z,
      fromNodeId: T.nodeId,
      fromHandleId: T.id || null,
      fromType: T.type,
      isValidConnection: ae,
      flowId: B,
      doc: ne,
      lib: W,
      nodeLookup: K
    });
    L && G && R(G);
    const U = structuredClone(q);
    delete U.inProgress, U.toPosition = U.toHandle ? U.toHandle.position : null, P?.(_, U), x.setState({ connectionClickStartHandle: null });
  };
  return a.jsx("div", { "data-handleid": y, "data-nodeid": m, "data-handlepos": t, "data-id": `${w}-${m}-${y}-${e}`, className: Ce([
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
      connectingfrom: E,
      connectingto: k,
      valid: C,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!$ || A) && ($ || D ? i : r)
    }
  ]), onMouseDown: F, onTouchStart: F, onClick: S ? N : void 0, ref: p, ...h, children: d });
}
const rn = Se(rl(hy));
function py({ data: e, isConnectable: t, sourcePosition: n = te.Bottom }) {
  return a.jsxs(a.Fragment, { children: [e?.label, a.jsx(rn, { type: "source", position: n, isConnectable: t })] });
}
function gy({ data: e, isConnectable: t, targetPosition: n = te.Top, sourcePosition: o = te.Bottom }) {
  return a.jsxs(a.Fragment, { children: [a.jsx(rn, { type: "target", position: n, isConnectable: t }), e?.label, a.jsx(rn, { type: "source", position: o, isConnectable: t })] });
}
function yy() {
  return null;
}
function my({ data: e, isConnectable: t, targetPosition: n = te.Top }) {
  return a.jsxs(a.Fragment, { children: [a.jsx(rn, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const Uo = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, ia = {
  input: py,
  default: gy,
  output: my,
  group: yy
};
function xy(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const wy = (e) => {
  const { width: t, height: n, x: o, y: r } = qn(e.nodeLookup, {
    filter: (i) => !!i.selected
  });
  return {
    width: Ze(t) ? t : null,
    height: Ze(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function vy({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = we(), { width: r, height: i, transformString: s, userSelectionActive: c } = ue(wy, xe), d = al(), l = ce(null);
  oe(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const f = !c && r !== null && i !== null;
  if (sl({
    nodeRef: l,
    disabled: !f
  }), !f)
    return null;
  const u = e ? (p) => {
    const y = o.getState().nodes.filter((v) => v.selected);
    e(p, y);
  } : void 0, h = (p) => {
    Object.prototype.hasOwnProperty.call(Uo, p.key) && (p.preventDefault(), d({
      direction: Uo[p.key],
      factor: p.shiftKey ? 4 : 1
    }));
  };
  return a.jsx("div", { className: Ce(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: s
  }, children: a.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: u, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : h, style: {
    width: r,
    height: i
  } }) });
}
const sa = typeof window < "u" ? window : void 0, by = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function ll({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, paneClickDistance: c, deleteKeyCode: d, selectionKeyCode: l, selectionOnDrag: f, selectionMode: u, onSelectionStart: h, onSelectionEnd: p, multiSelectionKeyCode: y, panActivationKeyCode: v, zoomActivationKeyCode: x, elementsSelectable: m, zoomOnScroll: S, zoomOnPinch: g, panOnScroll: w, panOnScrollSpeed: E, panOnScrollMode: k, zoomOnDoubleClick: j, panOnDrag: A, autoPanOnSelection: $, defaultViewport: D, translateExtent: C, minZoom: R, maxZoom: F, preventScrolling: N, onSelectionContextMenu: _, noWheelClassName: I, noPanClassName: P, disableKeyboardA11y: T, onViewportChange: z, isControlledViewport: O }) {
  const { nodesSelectionActive: W, userSelectionActive: B } = ue(by, xe), K = Hn(l, { target: sa }), q = Hn(v, { target: sa }), ne = q || A, ae = q || w, G = f && ne !== !0, L = K || B || G;
  return ty({ deleteKeyCode: d, multiSelectionKeyCode: y }), a.jsx(ry, { onPaneContextMenu: i, elementsSelectable: m, zoomOnScroll: S, zoomOnPinch: g, panOnScroll: ae, panOnScrollSpeed: E, panOnScrollMode: k, zoomOnDoubleClick: j, panOnDrag: !K && ne, defaultViewport: D, translateExtent: C, minZoom: R, maxZoom: F, zoomActivationKeyCode: x, preventScrolling: N, noWheelClassName: I, noPanClassName: P, onViewportChange: z, isControlledViewport: O, paneClickDistance: c, selectionOnDrag: G, children: a.jsxs(cy, { onSelectionStart: h, onSelectionEnd: p, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, panOnDrag: ne, autoPanOnSelection: $, isSelecting: !!L, selectionMode: u, selectionKeyPressed: K, paneClickDistance: c, selectionOnDrag: G, children: [e, W && a.jsx(vy, { onSelectionContextMenu: _, noPanClassName: P, disableKeyboardA11y: T })] }) });
}
ll.displayName = "FlowRenderer";
const Ny = Se(ll), Sy = (e) => (t) => e ? Ai(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function Ey(e) {
  return ue(pe(Sy(e), [e]), xe);
}
const ky = (e) => e.updateNodeInternals;
function jy() {
  const e = ue(ky), [t] = Y(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
function Cy({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = we(), i = ce(null), s = ce(null), c = ce(e.sourcePosition), d = ce(e.targetPosition), l = ce(t), f = n && !!e.internals.handleBounds;
  return oe(() => {
    i.current && !e.hidden && (!f || s.current !== i.current) && (s.current && o?.unobserve(s.current), o?.observe(i.current), s.current = i.current);
  }, [f, e.hidden]), oe(() => () => {
    s.current && (o?.unobserve(s.current), s.current = null);
  }, []), oe(() => {
    if (i.current) {
      const u = l.current !== t, h = c.current !== e.sourcePosition, p = d.current !== e.targetPosition;
      (u || h || p) && (l.current = t, c.current = e.sourcePosition, d.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: i.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), i;
}
function Iy({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: i, onDoubleClick: s, nodesDraggable: c, elementsSelectable: d, nodesConnectable: l, nodesFocusable: f, resizeObserver: u, noDragClassName: h, noPanClassName: p, disableKeyboardA11y: y, rfId: v, nodeTypes: x, nodeClickDistance: m, onError: S }) {
  const { node: g, internals: w, isParent: E } = ue((L) => {
    const U = L.nodeLookup.get(e), ie = L.parentLookup.has(e);
    return {
      node: U,
      internals: U.internals,
      isParent: ie
    };
  }, xe);
  let k = g.type || "default", j = x?.[k] || ia[k];
  j === void 0 && (S?.("003", Fe.error003(k)), k = "default", j = x?.default || ia.default);
  const A = !!(g.draggable || c && typeof g.draggable > "u"), $ = !!(g.selectable || d && typeof g.selectable > "u"), D = !!(g.connectable || l && typeof g.connectable > "u"), C = !!(g.focusable || f && typeof g.focusable > "u"), R = we(), F = Cc(g), N = Cy({ node: g, nodeType: k, hasDimensions: F, resizeObserver: u }), _ = sl({
    nodeRef: N,
    disabled: g.hidden || !A,
    noDragClassName: h,
    handleSelector: g.dragHandle,
    nodeId: e,
    isSelectable: $,
    nodeClickDistance: m
  }), I = al();
  if (g.hidden)
    return null;
  const P = dt(g), T = xy(g), z = $ || A || t || n || o || r, O = n ? (L) => n(L, { ...w.userNode }) : void 0, W = o ? (L) => o(L, { ...w.userNode }) : void 0, B = r ? (L) => r(L, { ...w.userNode }) : void 0, K = i ? (L) => i(L, { ...w.userNode }) : void 0, q = s ? (L) => s(L, { ...w.userNode }) : void 0, ne = (L) => {
    const { selectNodesOnDrag: U, nodeDragThreshold: ie } = R.getState();
    $ && (!U || !A || ie > 0) && hi({
      id: e,
      store: R,
      nodeRef: N
    }), t && t(L, { ...w.userNode });
  }, ae = (L) => {
    if (!(Ac(L.nativeEvent) || y)) {
      if (xc.includes(L.key) && $) {
        const U = L.key === "Escape";
        hi({
          id: e,
          store: R,
          unselect: U,
          nodeRef: N
        });
      } else if (A && g.selected && Object.prototype.hasOwnProperty.call(Uo, L.key)) {
        L.preventDefault();
        const { ariaLabelConfig: U } = R.getState();
        R.setState({
          ariaLiveMessage: U["node.a11yDescription.ariaLiveMessage"]({
            direction: L.key.replace("Arrow", "").toLowerCase(),
            x: ~~w.positionAbsolute.x,
            y: ~~w.positionAbsolute.y
          })
        }), I({
          direction: Uo[L.key],
          factor: L.shiftKey ? 4 : 1
        });
      }
    }
  }, G = () => {
    if (y || !N.current?.matches(":focus-visible"))
      return;
    const { transform: L, width: U, height: ie, autoPanOnNodeFocus: se, setCenter: J } = R.getState();
    if (!se)
      return;
    Ai(/* @__PURE__ */ new Map([[e, g]]), { x: 0, y: 0, width: U, height: ie }, L, !0).length > 0 || J(g.position.x + P.width / 2, g.position.y + P.height / 2, {
      zoom: L[2]
    });
  };
  return a.jsx("div", { className: Ce([
    "react-flow__node",
    `react-flow__node-${k}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [p]: A
    },
    g.className,
    {
      selected: g.selected,
      selectable: $,
      parent: E,
      draggable: A,
      dragging: _
    }
  ]), ref: N, style: {
    zIndex: w.z,
    transform: `translate(${w.positionAbsolute.x}px,${w.positionAbsolute.y}px)`,
    pointerEvents: z ? "all" : "none",
    visibility: F ? "visible" : "hidden",
    ...g.style,
    ...T
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: O, onMouseMove: W, onMouseLeave: B, onContextMenu: K, onClick: ne, onDoubleClick: q, onKeyDown: C ? ae : void 0, tabIndex: C ? 0 : void 0, onFocus: C ? G : void 0, role: g.ariaRole ?? (C ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": y ? void 0 : `${Kc}-${v}`, "aria-label": g.ariaLabel, ...g.domAttributes, children: a.jsx(dy, { value: e, children: a.jsx(j, { id: e, data: g.data, type: k, positionAbsoluteX: w.positionAbsolute.x, positionAbsoluteY: w.positionAbsolute.y, selected: g.selected ?? !1, selectable: $, draggable: A, deletable: g.deletable ?? !0, isConnectable: D, sourcePosition: g.sourcePosition, targetPosition: g.targetPosition, dragging: _, dragHandle: g.dragHandle, zIndex: w.z, parentId: g.parentId, ...P }) }) });
}
var _y = Se(Iy);
const Ay = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function dl(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: i } = ue(Ay, xe), s = Ey(e.onlyRenderVisibleElements), c = jy();
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
    a.jsx(_y, { id: d, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: i }, d)
  )) });
}
dl.displayName = "NodeRenderer";
const My = Se(dl);
function Dy(e) {
  return ue(pe((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const i = n.nodeLookup.get(r.source), s = n.nodeLookup.get(r.target);
        i && s && Cp({
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
const Py = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return a.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, $y = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return a.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, aa = {
  [Xo.Arrow]: Py,
  [Xo.ArrowClosed]: $y
};
function Ty(e) {
  const t = we();
  return ge(() => Object.prototype.hasOwnProperty.call(aa, e) ? aa[e] : (t.getState().onError?.("009", Fe.error009(e)), null), [e]);
}
const zy = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: i = "strokeWidth", strokeWidth: s, orient: c = "auto-start-reverse" }) => {
  const d = Ty(t);
  return d ? a.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: i, orient: c, refX: "0", refY: "0", children: a.jsx(d, { color: n, strokeWidth: s }) }) : null;
}, ul = ({ defaultColor: e, rfId: t }) => {
  const n = ue((i) => i.edges), o = ue((i) => i.defaultEdgeOptions), r = ge(() => Tp(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? a.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: a.jsx("defs", { children: r.map((i) => a.jsx(zy, { id: i.id, type: i.type, color: i.color, width: i.width, height: i.height, markerUnits: i.markerUnits, strokeWidth: i.strokeWidth, orient: i.orient }, i.id)) }) }) : null;
};
ul.displayName = "MarkerDefinitions";
var Ry = Se(ul);
function fl({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: i, labelBgPadding: s = [2, 4], labelBgBorderRadius: c = 2, children: d, className: l, ...f }) {
  const [u, h] = Y({ x: 1, y: 0, width: 0, height: 0 }), p = Ce(["react-flow__edge-textwrapper", l]), y = ce(null);
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
  }, [n]), n ? a.jsxs("g", { transform: `translate(${e - u.width / 2} ${t - u.height / 2})`, className: p, visibility: u.width ? "visible" : "hidden", ...f, children: [r && a.jsx("rect", { width: u.width + 2 * s[0], x: -s[0], y: -s[1], height: u.height + 2 * s[1], className: "react-flow__edge-textbg", style: i, rx: c, ry: c }), a.jsx("text", { className: "react-flow__edge-text", y: u.height / 2, dy: "0.3em", ref: y, style: o, children: n }), d] }) : null;
}
fl.displayName = "EdgeText";
const Ly = Se(fl);
function Zn({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: c, labelBgBorderRadius: d, interactionWidth: l = 20, ...f }) {
  return a.jsxs(a.Fragment, { children: [a.jsx("path", { ...f, d: e, fill: "none", className: Ce(["react-flow__edge-path", f.className]) }), l ? a.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, o && Ze(t) && Ze(n) ? a.jsx(Ly, { x: t, y: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: c, labelBgBorderRadius: d }) : null] });
}
function ca({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === te.Left || e === te.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function hl({ sourceX: e, sourceY: t, sourcePosition: n = te.Bottom, targetX: o, targetY: r, targetPosition: i = te.Top }) {
  const [s, c] = ca({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [d, l] = ca({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [f, u, h, p] = Dc({
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
    f,
    u,
    h,
    p
  ];
}
function pl(e) {
  return Se(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: c, label: d, labelStyle: l, labelShowBg: f, labelBgStyle: u, labelBgPadding: h, labelBgBorderRadius: p, style: y, markerEnd: v, markerStart: x, interactionWidth: m }) => {
    const [S, g, w] = hl({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: c
    }), E = e.isInternal ? void 0 : t;
    return a.jsx(Zn, { id: E, path: S, labelX: g, labelY: w, label: d, labelStyle: l, labelShowBg: f, labelBgStyle: u, labelBgPadding: h, labelBgBorderRadius: p, style: y, markerEnd: v, markerStart: x, interactionWidth: m });
  });
}
const Vy = pl({ isInternal: !1 }), gl = pl({ isInternal: !0 });
Vy.displayName = "SimpleBezierEdge";
gl.displayName = "SimpleBezierEdgeInternal";
function yl(e) {
  return Se(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: c, labelShowBg: d, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: u, style: h, sourcePosition: p = te.Bottom, targetPosition: y = te.Top, markerEnd: v, markerStart: x, pathOptions: m, interactionWidth: S }) => {
    const [g, w, E] = qo({
      sourceX: n,
      sourceY: o,
      sourcePosition: p,
      targetX: r,
      targetY: i,
      targetPosition: y,
      borderRadius: m?.borderRadius,
      offset: m?.offset,
      stepPosition: m?.stepPosition
    }), k = e.isInternal ? void 0 : t;
    return a.jsx(Zn, { id: k, path: g, labelX: w, labelY: E, label: s, labelStyle: c, labelShowBg: d, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: u, style: h, markerEnd: v, markerStart: x, interactionWidth: S });
  });
}
const ml = yl({ isInternal: !1 }), xl = yl({ isInternal: !0 });
ml.displayName = "SmoothStepEdge";
xl.displayName = "SmoothStepEdgeInternal";
function wl(e) {
  return Se(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return a.jsx(ml, { ...n, id: o, pathOptions: ge(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Hy = wl({ isInternal: !1 }), vl = wl({ isInternal: !0 });
Hy.displayName = "StepEdge";
vl.displayName = "StepEdgeInternal";
function bl(e) {
  return Se(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: c, labelShowBg: d, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: u, style: h, markerEnd: p, markerStart: y, interactionWidth: v }) => {
    const [x, m, S] = zc({ sourceX: n, sourceY: o, targetX: r, targetY: i }), g = e.isInternal ? void 0 : t;
    return a.jsx(Zn, { id: g, path: x, labelX: m, labelY: S, label: s, labelStyle: c, labelShowBg: d, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: u, style: h, markerEnd: p, markerStart: y, interactionWidth: v });
  });
}
const Oy = bl({ isInternal: !1 }), Nl = bl({ isInternal: !0 });
Oy.displayName = "StraightEdge";
Nl.displayName = "StraightEdgeInternal";
function Sl(e) {
  return Se(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s = te.Bottom, targetPosition: c = te.Top, label: d, labelStyle: l, labelShowBg: f, labelBgStyle: u, labelBgPadding: h, labelBgBorderRadius: p, style: y, markerEnd: v, markerStart: x, pathOptions: m, interactionWidth: S }) => {
    const [g, w, E] = Pc({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: c,
      curvature: m?.curvature
    }), k = e.isInternal ? void 0 : t;
    return a.jsx(Zn, { id: k, path: g, labelX: w, labelY: E, label: d, labelStyle: l, labelShowBg: f, labelBgStyle: u, labelBgPadding: h, labelBgBorderRadius: p, style: y, markerEnd: v, markerStart: x, interactionWidth: S });
  });
}
const Fy = Sl({ isInternal: !1 }), El = Sl({ isInternal: !0 });
Fy.displayName = "BezierEdge";
El.displayName = "BezierEdgeInternal";
const la = {
  default: El,
  straight: Nl,
  step: vl,
  smoothstep: xl,
  simplebezier: gl
}, da = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, By = (e, t, n) => n === te.Left ? e - t : n === te.Right ? e + t : e, Wy = (e, t, n) => n === te.Top ? e - t : n === te.Bottom ? e + t : e, ua = "react-flow__edgeupdater";
function fa({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: i, onMouseOut: s, type: c }) {
  return a.jsx("circle", { onMouseDown: r, onMouseEnter: i, onMouseOut: s, className: Ce([ua, `${ua}-${c}`]), cx: By(t, o, e), cy: Wy(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function Xy({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: i, targetY: s, sourcePosition: c, targetPosition: d, onReconnect: l, onReconnectStart: f, onReconnectEnd: u, setReconnecting: h, setUpdateHover: p }) {
  const y = we(), v = (w, E) => {
    if (w.button !== 0)
      return;
    const { autoPanOnConnect: k, domNode: j, connectionMode: A, connectionRadius: $, lib: D, onConnectStart: C, cancelConnection: R, nodeLookup: F, rfId: N, panBy: _, updateConnection: I } = y.getState(), P = E.type === "target", T = (W, B) => {
      h(!1), u?.(W, n, E.type, B);
    }, z = (W) => l?.(n, W), O = (W, B) => {
      h(!0), f?.(w, n, E.type), C?.(W, B);
    };
    fi.onPointerDown(w.nativeEvent, {
      autoPanOnConnect: k,
      connectionMode: A,
      connectionRadius: $,
      domNode: j,
      handleId: E.id,
      nodeId: E.nodeId,
      nodeLookup: F,
      isTarget: P,
      edgeUpdaterType: E.type,
      lib: D,
      flowId: N,
      cancelConnection: R,
      panBy: _,
      isValidConnection: (...W) => y.getState().isValidConnection?.(...W) ?? !0,
      onConnect: z,
      onConnectStart: O,
      onConnectEnd: (...W) => y.getState().onConnectEnd?.(...W),
      onReconnectEnd: T,
      updateConnection: I,
      getTransform: () => y.getState().transform,
      getFromHandle: () => y.getState().connection.fromHandle,
      dragThreshold: y.getState().connectionDragThreshold,
      handleDomNode: w.currentTarget
    });
  }, x = (w) => v(w, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), m = (w) => v(w, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), S = () => p(!0), g = () => p(!1);
  return a.jsxs(a.Fragment, { children: [(e === !0 || e === "source") && a.jsx(fa, { position: c, centerX: o, centerY: r, radius: t, onMouseDown: x, onMouseEnter: S, onMouseOut: g, type: "source" }), (e === !0 || e === "target") && a.jsx(fa, { position: d, centerX: i, centerY: s, radius: t, onMouseDown: m, onMouseEnter: S, onMouseOut: g, type: "target" })] });
}
function Yy({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: i, onContextMenu: s, onMouseEnter: c, onMouseMove: d, onMouseLeave: l, reconnectRadius: f, onReconnect: u, onReconnectStart: h, onReconnectEnd: p, rfId: y, edgeTypes: v, noPanClassName: x, onError: m, disableKeyboardA11y: S }) {
  let g = ue((J) => J.edgeLookup.get(e));
  const w = ue((J) => J.defaultEdgeOptions);
  g = w ? { ...w, ...g } : g;
  let E = g.type || "default", k = v?.[E] || la[E];
  k === void 0 && (m?.("011", Fe.error011(E)), E = "default", k = v?.default || la.default);
  const j = !!(g.focusable || t && typeof g.focusable > "u"), A = typeof u < "u" && (g.reconnectable || n && typeof g.reconnectable > "u"), $ = !!(g.selectable || o && typeof g.selectable > "u"), D = ce(null), [C, R] = Y(!1), [F, N] = Y(!1), _ = we(), { zIndex: I, sourceX: P, sourceY: T, targetX: z, targetY: O, sourcePosition: W, targetPosition: B } = ue(pe((J) => {
    const ee = J.nodeLookup.get(g.source), le = J.nodeLookup.get(g.target);
    if (!ee || !le)
      return {
        zIndex: g.zIndex,
        ...da
      };
    const H = $p({
      id: e,
      sourceNode: ee,
      targetNode: le,
      sourceHandle: g.sourceHandle || null,
      targetHandle: g.targetHandle || null,
      connectionMode: J.connectionMode,
      onError: m
    });
    return {
      zIndex: jp({
        selected: g.selected,
        zIndex: g.zIndex,
        sourceNode: ee,
        targetNode: le,
        elevateOnSelect: J.elevateEdgesOnSelect,
        zIndexMode: J.zIndexMode
      }),
      ...H || da
    };
  }, [g.source, g.target, g.sourceHandle, g.targetHandle, g.selected, g.zIndex]), xe), K = ge(() => g.markerStart ? `url('#${di(g.markerStart, y)}')` : void 0, [g.markerStart, y]), q = ge(() => g.markerEnd ? `url('#${di(g.markerEnd, y)}')` : void 0, [g.markerEnd, y]);
  if (g.hidden || P === null || T === null || z === null || O === null)
    return null;
  const ne = (J) => {
    const { addSelectedEdges: ee, unselectNodesAndEdges: le, multiSelectionActive: H } = _.getState();
    $ && (_.setState({ nodesSelectionActive: !1 }), g.selected && H ? (le({ nodes: [], edges: [g] }), D.current?.blur()) : ee([e])), r && r(J, g);
  }, ae = i ? (J) => {
    i(J, { ...g });
  } : void 0, G = s ? (J) => {
    s(J, { ...g });
  } : void 0, L = c ? (J) => {
    c(J, { ...g });
  } : void 0, U = d ? (J) => {
    d(J, { ...g });
  } : void 0, ie = l ? (J) => {
    l(J, { ...g });
  } : void 0, se = (J) => {
    if (!S && xc.includes(J.key) && $) {
      const { unselectNodesAndEdges: ee, addSelectedEdges: le } = _.getState();
      J.key === "Escape" ? (D.current?.blur(), ee({ edges: [g] })) : le([e]);
    }
  };
  return a.jsx("svg", { style: { zIndex: I }, children: a.jsxs("g", { className: Ce([
    "react-flow__edge",
    `react-flow__edge-${E}`,
    g.className,
    x,
    {
      selected: g.selected,
      animated: g.animated,
      inactive: !$ && !r,
      updating: C,
      selectable: $
    }
  ]), onClick: ne, onDoubleClick: ae, onContextMenu: G, onMouseEnter: L, onMouseMove: U, onMouseLeave: ie, onKeyDown: j ? se : void 0, tabIndex: j ? 0 : void 0, role: g.ariaRole ?? (j ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": g.ariaLabel === null ? void 0 : g.ariaLabel || `Edge from ${g.source} to ${g.target}`, "aria-describedby": j ? `${Gc}-${y}` : void 0, ref: D, ...g.domAttributes, children: [!F && a.jsx(k, { id: e, source: g.source, target: g.target, type: g.type, selected: g.selected, animated: g.animated, selectable: $, deletable: g.deletable ?? !0, label: g.label, labelStyle: g.labelStyle, labelShowBg: g.labelShowBg, labelBgStyle: g.labelBgStyle, labelBgPadding: g.labelBgPadding, labelBgBorderRadius: g.labelBgBorderRadius, sourceX: P, sourceY: T, targetX: z, targetY: O, sourcePosition: W, targetPosition: B, data: g.data, style: g.style, sourceHandleId: g.sourceHandle, targetHandleId: g.targetHandle, markerStart: K, markerEnd: q, pathOptions: "pathOptions" in g ? g.pathOptions : void 0, interactionWidth: g.interactionWidth }), A && a.jsx(Xy, { edge: g, isReconnectable: A, reconnectRadius: f, onReconnect: u, onReconnectStart: h, onReconnectEnd: p, sourceX: P, sourceY: T, targetX: z, targetY: O, sourcePosition: W, targetPosition: B, setUpdateHover: R, setReconnecting: N })] }) });
}
var qy = Se(Yy);
const Uy = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function kl({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: i, onEdgeContextMenu: s, onEdgeMouseEnter: c, onEdgeMouseMove: d, onEdgeMouseLeave: l, onEdgeClick: f, reconnectRadius: u, onEdgeDoubleClick: h, onReconnectStart: p, onReconnectEnd: y, disableKeyboardA11y: v }) {
  const { edgesFocusable: x, edgesReconnectable: m, elementsSelectable: S, onError: g } = ue(Uy, xe), w = Dy(t);
  return a.jsxs("div", { className: "react-flow__edges", children: [a.jsx(Ry, { defaultColor: e, rfId: n }), w.map((E) => a.jsx(qy, { id: E, edgesFocusable: x, edgesReconnectable: m, elementsSelectable: S, noPanClassName: r, onReconnect: i, onContextMenu: s, onMouseEnter: c, onMouseMove: d, onMouseLeave: l, onClick: f, reconnectRadius: u, onDoubleClick: h, onReconnectStart: p, onReconnectEnd: y, rfId: n, onError: g, edgeTypes: o, disableKeyboardA11y: v }, E))] });
}
kl.displayName = "EdgeRenderer";
const Zy = Se(kl), Ky = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function Gy({ children: e }) {
  const t = ue(Ky);
  return a.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function Jy(e) {
  const t = Vi(), n = ce(!1);
  oe(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Qy = (e) => e.panZoom?.syncViewport;
function em(e) {
  const t = ue(Qy), n = we();
  return oe(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function tm(e) {
  return e.connection.inProgress ? { ...e.connection, to: sn(e.connection.to, e.transform) } : { ...e.connection };
}
function nm(e) {
  return tm;
}
function om(e) {
  const t = nm();
  return ue(t, xe);
}
const rm = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function im({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: i, height: s, isValid: c, inProgress: d } = ue(rm, xe);
  return !(i && r && d) ? null : a.jsx("svg", { style: e, width: i, height: s, className: "react-flow__connectionline react-flow__container", children: a.jsx("g", { className: Ce(["react-flow__connection", bc(c)]), children: a.jsx(jl, { style: t, type: n, CustomComponent: o, isValid: c }) }) });
}
const jl = ({ style: e, type: t = gt.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: i, fromNode: s, fromHandle: c, fromPosition: d, to: l, toNode: f, toHandle: u, toPosition: h, pointer: p } = om();
  if (!r)
    return;
  if (n)
    return a.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: s, fromHandle: c, fromX: i.x, fromY: i.y, toX: l.x, toY: l.y, fromPosition: d, toPosition: h, connectionStatus: bc(o), toNode: f, toHandle: u, pointer: p });
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
    case gt.Bezier:
      [y] = Pc(v);
      break;
    case gt.SimpleBezier:
      [y] = hl(v);
      break;
    case gt.Step:
      [y] = qo({
        ...v,
        borderRadius: 0
      });
      break;
    case gt.SmoothStep:
      [y] = qo(v);
      break;
    default:
      [y] = zc(v);
  }
  return a.jsx("path", { d: y, fill: "none", className: "react-flow__connection-path", style: e });
};
jl.displayName = "ConnectionLine";
const sm = {};
function ha(e = sm) {
  ce(e), we(), oe(() => {
  }, [e]);
}
function am() {
  we(), ce(!1), oe(() => {
  }, []);
}
function Cl({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: i, onEdgeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: d, onNodeMouseLeave: l, onNodeContextMenu: f, onSelectionContextMenu: u, onSelectionStart: h, onSelectionEnd: p, connectionLineType: y, connectionLineStyle: v, connectionLineComponent: x, connectionLineContainerStyle: m, selectionKeyCode: S, selectionOnDrag: g, selectionMode: w, multiSelectionKeyCode: E, panActivationKeyCode: k, zoomActivationKeyCode: j, deleteKeyCode: A, onlyRenderVisibleElements: $, elementsSelectable: D, defaultViewport: C, translateExtent: R, minZoom: F, maxZoom: N, preventScrolling: _, defaultMarkerColor: I, zoomOnScroll: P, zoomOnPinch: T, panOnScroll: z, panOnScrollSpeed: O, panOnScrollMode: W, zoomOnDoubleClick: B, panOnDrag: K, autoPanOnSelection: q, onPaneClick: ne, onPaneMouseEnter: ae, onPaneMouseMove: G, onPaneMouseLeave: L, onPaneScroll: U, onPaneContextMenu: ie, paneClickDistance: se, nodeClickDistance: J, onEdgeContextMenu: ee, onEdgeMouseEnter: le, onEdgeMouseMove: H, onEdgeMouseLeave: Q, reconnectRadius: fe, onReconnect: me, onReconnectStart: Ee, onReconnectEnd: Me, noDragClassName: ye, noWheelClassName: We, noPanClassName: rt, disableKeyboardA11y: it, nodeExtent: ze, rfId: Re, viewport: ke, onViewportChange: Xe }) {
  return ha(e), ha(t), am(), Jy(n), em(ke), a.jsx(Ny, { onPaneClick: ne, onPaneMouseEnter: ae, onPaneMouseMove: G, onPaneMouseLeave: L, onPaneContextMenu: ie, onPaneScroll: U, paneClickDistance: se, deleteKeyCode: A, selectionKeyCode: S, selectionOnDrag: g, selectionMode: w, onSelectionStart: h, onSelectionEnd: p, multiSelectionKeyCode: E, panActivationKeyCode: k, zoomActivationKeyCode: j, elementsSelectable: D, zoomOnScroll: P, zoomOnPinch: T, zoomOnDoubleClick: B, panOnScroll: z, panOnScrollSpeed: O, panOnScrollMode: W, panOnDrag: K, autoPanOnSelection: q, defaultViewport: C, translateExtent: R, minZoom: F, maxZoom: N, onSelectionContextMenu: u, preventScrolling: _, noDragClassName: ye, noWheelClassName: We, noPanClassName: rt, disableKeyboardA11y: it, onViewportChange: Xe, isControlledViewport: !!ke, children: a.jsxs(Gy, { children: [a.jsx(Zy, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: s, onReconnect: me, onReconnectStart: Ee, onReconnectEnd: Me, onlyRenderVisibleElements: $, onEdgeContextMenu: ee, onEdgeMouseEnter: le, onEdgeMouseMove: H, onEdgeMouseLeave: Q, reconnectRadius: fe, defaultMarkerColor: I, noPanClassName: rt, disableKeyboardA11y: it, rfId: Re }), a.jsx(im, { style: v, type: y, component: x, containerStyle: m }), a.jsx("div", { className: "react-flow__edgelabel-renderer" }), a.jsx(My, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: i, onNodeMouseEnter: c, onNodeMouseMove: d, onNodeMouseLeave: l, onNodeContextMenu: f, nodeClickDistance: J, onlyRenderVisibleElements: $, noPanClassName: rt, noDragClassName: ye, disableKeyboardA11y: it, nodeExtent: ze, rfId: Re }), a.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Cl.displayName = "GraphView";
const cm = Se(Cl), lm = jc(), pa = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: c, minZoom: d = 0.5, maxZoom: l = 2, nodeOrigin: f, nodeExtent: u, zIndexMode: h = "basic" } = {}) => {
  const p = /* @__PURE__ */ new Map(), y = /* @__PURE__ */ new Map(), v = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), m = o ?? t ?? [], S = n ?? e ?? [], g = f ?? [0, 0], w = u ?? zn;
  Vc(v, x, m);
  const { nodesInitialized: E } = ui(S, p, y, {
    nodeOrigin: g,
    nodeExtent: w,
    zIndexMode: h
  });
  let k = [0, 0, 1];
  if (s && r && i) {
    const j = qn(p, {
      filter: (C) => !!((C.width || C.initialWidth) && (C.height || C.initialHeight))
    }), { x: A, y: $, zoom: D } = Di(j, r, i, d, l, c?.padding ?? 0.1);
    k = [A, $, D];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: i ?? 0,
    transform: k,
    nodes: S,
    nodesInitialized: E,
    nodeLookup: p,
    parentLookup: y,
    edges: m,
    edgeLookup: x,
    connectionLookup: v,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: o !== void 0,
    panZoom: null,
    minZoom: d,
    maxZoom: l,
    translateExtent: zn,
    nodeExtent: w,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: Qt.Strict,
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
    onError: lm,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: wc,
    zIndexMode: h,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, dm = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: c, minZoom: d, maxZoom: l, nodeOrigin: f, nodeExtent: u, zIndexMode: h }) => Sg((p, y) => {
  async function v() {
    const { nodeLookup: x, panZoom: m, fitViewOptions: S, fitViewResolver: g, width: w, height: E, minZoom: k, maxZoom: j } = y();
    m && (await wp({
      nodes: x,
      width: w,
      height: E,
      panZoom: m,
      minZoom: k,
      maxZoom: j
    }, S), g?.resolve(!0), p({ fitViewResolver: null }));
  }
  return {
    ...pa({
      nodes: e,
      edges: t,
      width: r,
      height: i,
      fitView: s,
      fitViewOptions: c,
      minZoom: d,
      maxZoom: l,
      nodeOrigin: f,
      nodeExtent: u,
      defaultNodes: n,
      defaultEdges: o,
      zIndexMode: h
    }),
    setNodes: (x) => {
      const { nodeLookup: m, parentLookup: S, nodeOrigin: g, elevateNodesOnSelect: w, fitViewQueued: E, zIndexMode: k, nodesSelectionActive: j } = y(), { nodesInitialized: A, hasSelectedNodes: $ } = ui(x, m, S, {
        nodeOrigin: g,
        nodeExtent: u,
        elevateNodesOnSelect: w,
        checkEquality: !0,
        zIndexMode: k
      }), D = j && $;
      E && A ? (v(), p({
        nodes: x,
        nodesInitialized: A,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: D
      })) : p({ nodes: x, nodesInitialized: A, nodesSelectionActive: D });
    },
    setEdges: (x) => {
      const { connectionLookup: m, edgeLookup: S } = y();
      Vc(m, S, x), p({ edges: x });
    },
    setDefaultNodesAndEdges: (x, m) => {
      if (x) {
        const { setNodes: S } = y();
        S(x), p({ hasDefaultNodes: !0 });
      }
      if (m) {
        const { setEdges: S } = y();
        S(m), p({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (x) => {
      const { triggerNodeChanges: m, nodeLookup: S, parentLookup: g, domNode: w, nodeOrigin: E, nodeExtent: k, debug: j, fitViewQueued: A, zIndexMode: $ } = y(), { changes: D, updatedInternals: C } = Fp(x, S, g, w, E, k, $);
      C && (Lp(S, g, { nodeOrigin: E, nodeExtent: k, zIndexMode: $ }), A ? (v(), p({ fitViewQueued: !1, fitViewOptions: void 0 })) : p({}), D?.length > 0 && (j && console.log("React Flow: trigger node changes", D), m?.(D)));
    },
    updateNodePositions: (x, m = !1) => {
      const S = [];
      let g = [];
      const { nodeLookup: w, triggerNodeChanges: E, connection: k, updateConnection: j, onNodesChangeMiddlewareMap: A } = y();
      for (const [$, D] of x) {
        const C = w.get($), R = !!(C?.expandParent && C?.parentId && D?.position), F = {
          id: $,
          type: "position",
          position: R ? {
            x: Math.max(0, D.position.x),
            y: Math.max(0, D.position.y)
          } : D.position,
          dragging: m
        };
        if (C && k.inProgress && k.fromNode.id === C.id) {
          const N = zt(C, k.fromHandle, te.Left, !0);
          j({ ...k, from: N });
        }
        R && C.parentId && S.push({
          id: $,
          parentId: C.parentId,
          rect: {
            ...D.internals.positionAbsolute,
            width: D.measured.width ?? 0,
            height: D.measured.height ?? 0
          }
        }), g.push(F);
      }
      if (S.length > 0) {
        const { parentLookup: $, nodeOrigin: D } = y(), C = Li(S, w, $, D);
        g.push(...C);
      }
      for (const $ of A.values())
        g = $(g);
      E(g);
    },
    triggerNodeChanges: (x) => {
      const { onNodesChange: m, setNodes: S, nodes: g, hasDefaultNodes: w, debug: E } = y();
      if (x?.length) {
        if (w) {
          const k = el(x, g);
          S(k);
        }
        E && console.log("React Flow: trigger node changes", x), m?.(x);
      }
    },
    triggerEdgeChanges: (x) => {
      const { onEdgesChange: m, setEdges: S, edges: g, hasDefaultEdges: w, debug: E } = y();
      if (x?.length) {
        if (w) {
          const k = tl(x, g);
          S(k);
        }
        E && console.log("React Flow: trigger edge changes", x), m?.(x);
      }
    },
    addSelectedNodes: (x) => {
      const { multiSelectionActive: m, edgeLookup: S, nodeLookup: g, triggerNodeChanges: w, triggerEdgeChanges: E } = y();
      if (m) {
        const k = x.map((j) => Ct(j, !0));
        w(k);
        return;
      }
      w(qt(g, /* @__PURE__ */ new Set([...x]), !0)), E(qt(S));
    },
    addSelectedEdges: (x) => {
      const { multiSelectionActive: m, edgeLookup: S, nodeLookup: g, triggerNodeChanges: w, triggerEdgeChanges: E } = y();
      if (m) {
        const k = x.map((j) => Ct(j, !0));
        E(k);
        return;
      }
      E(qt(S, /* @__PURE__ */ new Set([...x]))), w(qt(g, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: x, edges: m } = {}) => {
      const { edges: S, nodes: g, nodeLookup: w, triggerNodeChanges: E, triggerEdgeChanges: k } = y(), j = x || g, A = m || S, $ = [];
      for (const C of j) {
        if (!C.selected)
          continue;
        const R = w.get(C.id);
        R && (R.selected = !1), $.push(Ct(C.id, !1));
      }
      const D = [];
      for (const C of A)
        C.selected && D.push(Ct(C.id, !1));
      E($), k(D);
    },
    setMinZoom: (x) => {
      const { panZoom: m, maxZoom: S } = y();
      m?.setScaleExtent([x, S]), p({ minZoom: x });
    },
    setMaxZoom: (x) => {
      const { panZoom: m, minZoom: S } = y();
      m?.setScaleExtent([S, x]), p({ maxZoom: x });
    },
    setTranslateExtent: (x) => {
      y().panZoom?.setTranslateExtent(x), p({ translateExtent: x });
    },
    resetSelectedElements: () => {
      const { edges: x, nodes: m, triggerNodeChanges: S, triggerEdgeChanges: g, elementsSelectable: w } = y();
      if (!w)
        return;
      const E = m.reduce((j, A) => A.selected ? [...j, Ct(A.id, !1)] : j, []), k = x.reduce((j, A) => A.selected ? [...j, Ct(A.id, !1)] : j, []);
      S(E), g(k);
    },
    setNodeExtent: (x) => {
      const { nodes: m, nodeLookup: S, parentLookup: g, nodeOrigin: w, elevateNodesOnSelect: E, nodeExtent: k, zIndexMode: j } = y();
      x[0][0] === k[0][0] && x[0][1] === k[0][1] && x[1][0] === k[1][0] && x[1][1] === k[1][1] || (ui(m, S, g, {
        nodeOrigin: w,
        nodeExtent: x,
        elevateNodesOnSelect: E,
        checkEquality: !1,
        zIndexMode: j
      }), p({ nodeExtent: x }));
    },
    panBy: (x) => {
      const { transform: m, width: S, height: g, panZoom: w, translateExtent: E } = y();
      return Bp({ delta: x, panZoom: w, transform: m, translateExtent: E, width: S, height: g });
    },
    setCenter: async (x, m, S) => {
      const { width: g, height: w, maxZoom: E, panZoom: k } = y();
      if (!k)
        return !1;
      const j = typeof S?.zoom < "u" ? S.zoom : E;
      return await k.setViewport({
        x: g / 2 - x * j,
        y: w / 2 - m * j,
        zoom: j
      }, { duration: S?.duration, ease: S?.ease, interpolate: S?.interpolate }), !0;
    },
    cancelConnection: () => {
      p({
        connection: { ...vc }
      });
    },
    updateConnection: (x) => {
      p({ connection: x });
    },
    reset: () => p({ ...pa() })
  };
}, Object.is);
function um({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: i, initialMinZoom: s, initialMaxZoom: c, initialFitViewOptions: d, fitView: l, nodeOrigin: f, nodeExtent: u, zIndexMode: h, children: p }) {
  const [y] = Y(() => dm({
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
    nodeOrigin: f,
    nodeExtent: u,
    zIndexMode: h
  }));
  return a.jsx(Cg, { value: y, children: a.jsx(Gg, { children: p }) });
}
function fm({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: i, height: s, fitView: c, fitViewOptions: d, minZoom: l, maxZoom: f, nodeOrigin: u, nodeExtent: h, zIndexMode: p }) {
  return Bn(lr) ? a.jsx(a.Fragment, { children: e }) : a.jsx(um, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: i, initialHeight: s, fitView: c, initialFitViewOptions: d, initialMinZoom: l, initialMaxZoom: f, nodeOrigin: u, nodeExtent: h, zIndexMode: p, children: e });
}
const hm = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function pm({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: i, edgeTypes: s, onNodeClick: c, onEdgeClick: d, onInit: l, onMove: f, onMoveStart: u, onMoveEnd: h, onConnect: p, onConnectStart: y, onConnectEnd: v, onClickConnectStart: x, onClickConnectEnd: m, onNodeMouseEnter: S, onNodeMouseMove: g, onNodeMouseLeave: w, onNodeContextMenu: E, onNodeDoubleClick: k, onNodeDragStart: j, onNodeDrag: A, onNodeDragStop: $, onNodesDelete: D, onEdgesDelete: C, onDelete: R, onSelectionChange: F, onSelectionDragStart: N, onSelectionDrag: _, onSelectionDragStop: I, onSelectionContextMenu: P, onSelectionStart: T, onSelectionEnd: z, onBeforeDelete: O, connectionMode: W, connectionLineType: B = gt.Bezier, connectionLineStyle: K, connectionLineComponent: q, connectionLineContainerStyle: ne, deleteKeyCode: ae = "Backspace", selectionKeyCode: G = "Shift", selectionOnDrag: L = !1, selectionMode: U = Rn.Full, panActivationKeyCode: ie = "Space", multiSelectionKeyCode: se = Vn() ? "Meta" : "Control", zoomActivationKeyCode: J = Vn() ? "Meta" : "Control", snapToGrid: ee, snapGrid: le, onlyRenderVisibleElements: H = !1, selectNodesOnDrag: Q, nodesDraggable: fe, autoPanOnNodeFocus: me, nodesConnectable: Ee, nodesFocusable: Me, nodeOrigin: ye = Jc, edgesFocusable: We, edgesReconnectable: rt, elementsSelectable: it = !0, defaultViewport: ze = Hg, minZoom: Re = 0.5, maxZoom: ke = 2, translateExtent: Xe = zn, preventScrolling: cn = !0, nodeExtent: vt, defaultMarkerColor: Gn = "#b1b1b7", zoomOnScroll: ln = !0, zoomOnPinch: Ht = !0, panOnScroll: ut = !1, panOnScrollSpeed: bt = 0.5, panOnScrollMode: Nt = At.Free, zoomOnDoubleClick: et = !0, panOnDrag: Jn = !0, onPaneClick: Le, onPaneMouseEnter: Qn, onPaneMouseMove: be, onPaneMouseLeave: Ye, onPaneScroll: dn, onPaneContextMenu: Ne, paneClickDistance: St = 1, nodeClickDistance: un = 0, children: Ae, onReconnect: Et, onReconnectStart: ft, onReconnectEnd: yr, onEdgeContextMenu: eo, onEdgeDoubleClick: to, onEdgeMouseEnter: fn, onEdgeMouseMove: mr, onEdgeMouseLeave: hn, reconnectRadius: Ot = 10, onNodesChange: Ve, onEdgesChange: pn, noDragClassName: gn = "nodrag", noWheelClassName: yn = "nowheel", noPanClassName: kt = "nopan", fitView: no, fitViewOptions: oo, connectOnClick: xr, attributionPosition: wr, proOptions: ro, defaultEdgeOptions: io, elevateNodesOnSelect: so = !0, elevateEdgesOnSelect: vr = !1, disableKeyboardA11y: Ft = !1, autoPanOnConnect: br, autoPanOnNodeDrag: Nr, autoPanOnSelection: Sr = !0, autoPanSpeed: Er, connectionRadius: kr, isValidConnection: mn, onError: jr, style: Cr, id: ao, nodeDragThreshold: Ir, connectionDragThreshold: _r, viewport: Ar, onViewportChange: Mr, width: co, height: lo, colorMode: Dr = "light", debug: Pr, onScroll: uo, ariaLabelConfig: $r, zIndexMode: fo = "basic", ...ho }, po) {
  const Bt = ao || "1", go = Wg(Dr), yo = pe((mo) => {
    mo.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), uo?.(mo);
  }, [uo]);
  return a.jsx("div", { "data-testid": "rf__wrapper", ...ho, onScroll: yo, style: { ...Cr, ...hm }, ref: po, className: Ce(["react-flow", r, go]), id: ao, role: "application", children: a.jsxs(fm, { nodes: e, edges: t, width: co, height: lo, fitView: no, fitViewOptions: oo, minZoom: Re, maxZoom: ke, nodeOrigin: ye, nodeExtent: vt, zIndexMode: fo, children: [a.jsx(Bg, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: p, onConnectStart: y, onConnectEnd: v, onClickConnectStart: x, onClickConnectEnd: m, nodesDraggable: fe, autoPanOnNodeFocus: me, nodesConnectable: Ee, nodesFocusable: Me, edgesFocusable: We, edgesReconnectable: rt, elementsSelectable: it, elevateNodesOnSelect: so, elevateEdgesOnSelect: vr, minZoom: Re, maxZoom: ke, nodeExtent: vt, onNodesChange: Ve, onEdgesChange: pn, snapToGrid: ee, snapGrid: le, connectionMode: W, translateExtent: Xe, connectOnClick: xr, defaultEdgeOptions: io, fitView: no, fitViewOptions: oo, onNodesDelete: D, onEdgesDelete: C, onDelete: R, onNodeDragStart: j, onNodeDrag: A, onNodeDragStop: $, onSelectionDrag: _, onSelectionDragStart: N, onSelectionDragStop: I, onMove: f, onMoveStart: u, onMoveEnd: h, noPanClassName: kt, nodeOrigin: ye, rfId: Bt, autoPanOnConnect: br, autoPanOnNodeDrag: Nr, autoPanSpeed: Er, onError: jr, connectionRadius: kr, isValidConnection: mn, selectNodesOnDrag: Q, nodeDragThreshold: Ir, connectionDragThreshold: _r, onBeforeDelete: O, debug: Pr, ariaLabelConfig: $r, zIndexMode: fo }), a.jsx(cm, { onInit: l, onNodeClick: c, onEdgeClick: d, onNodeMouseEnter: S, onNodeMouseMove: g, onNodeMouseLeave: w, onNodeContextMenu: E, onNodeDoubleClick: k, nodeTypes: i, edgeTypes: s, connectionLineType: B, connectionLineStyle: K, connectionLineComponent: q, connectionLineContainerStyle: ne, selectionKeyCode: G, selectionOnDrag: L, selectionMode: U, deleteKeyCode: ae, multiSelectionKeyCode: se, panActivationKeyCode: ie, zoomActivationKeyCode: J, onlyRenderVisibleElements: H, defaultViewport: ze, translateExtent: Xe, minZoom: Re, maxZoom: ke, preventScrolling: cn, zoomOnScroll: ln, zoomOnPinch: Ht, zoomOnDoubleClick: et, panOnScroll: ut, panOnScrollSpeed: bt, panOnScrollMode: Nt, panOnDrag: Jn, autoPanOnSelection: Sr, onPaneClick: Le, onPaneMouseEnter: Qn, onPaneMouseMove: be, onPaneMouseLeave: Ye, onPaneScroll: dn, onPaneContextMenu: Ne, paneClickDistance: St, nodeClickDistance: un, onSelectionContextMenu: P, onSelectionStart: T, onSelectionEnd: z, onReconnect: Et, onReconnectStart: ft, onReconnectEnd: yr, onEdgeContextMenu: eo, onEdgeDoubleClick: to, onEdgeMouseEnter: fn, onEdgeMouseMove: mr, onEdgeMouseLeave: hn, reconnectRadius: Ot, defaultMarkerColor: Gn, noDragClassName: gn, noWheelClassName: yn, noPanClassName: kt, rfId: Bt, disableKeyboardA11y: Ft, nodeExtent: vt, viewport: Ar, onViewportChange: Mr }), a.jsx(Vg, { onSelectionChange: F }), Ae, a.jsx($g, { proOptions: ro, position: wr }), a.jsx(Pg, { rfId: Bt, disableKeyboardA11y: Ft })] }) });
}
var Il = rl(pm);
const gm = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function ym({ children: e }) {
  const t = ue(gm);
  return t ? jg.createPortal(e, t) : null;
}
function mm({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return a.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ce(["react-flow__background-pattern", n, o]) });
}
function xm({ radius: e, className: t }) {
  return a.jsx("circle", { cx: e, cy: e, r: e, className: Ce(["react-flow__background-pattern", "dots", t]) });
}
var yt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(yt || (yt = {}));
const wm = {
  [yt.Dots]: 1,
  [yt.Lines]: 1,
  [yt.Cross]: 6
}, vm = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function _l({
  id: e,
  variant: t = yt.Dots,
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
  patternClassName: f
}) {
  const u = ce(null), { transform: h, patternId: p } = ue(vm, xe), y = o || wm[t], v = t === yt.Dots, x = t === yt.Cross, m = Array.isArray(n) ? n : [n, n], S = [m[0] * h[2] || 1, m[1] * h[2] || 1], g = y * h[2], w = Array.isArray(i) ? i : [i, i], E = x ? [g, g] : S, k = [
    w[0] * h[2] || 1 + E[0] / 2,
    w[1] * h[2] || 1 + E[1] / 2
  ], j = `${p}${e || ""}`;
  return a.jsxs("svg", { className: Ce(["react-flow__background", l]), style: {
    ...d,
    ...ur,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": s
  }, ref: u, "data-testid": "rf__background", children: [a.jsx("pattern", { id: j, x: h[0] % S[0], y: h[1] % S[1], width: S[0], height: S[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${k[0]},-${k[1]})`, children: v ? a.jsx(xm, { radius: g / 2, className: f }) : a.jsx(mm, { dimensions: E, lineWidth: r, variant: t, className: f }) }), a.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${j})` })] });
}
_l.displayName = "Background";
const Al = Se(_l);
function bm() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: a.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function Nm() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: a.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function Sm() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: a.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function Em() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: a.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function km() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: a.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function Co({ children: e, className: t, ...n }) {
  return a.jsx("button", { type: "button", className: Ce(["react-flow__controls-button", t]), ...n, children: e });
}
const jm = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function Ml({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: i, onZoomOut: s, onFitView: c, onInteractiveChange: d, className: l, children: f, position: u = "bottom-left", orientation: h = "vertical", "aria-label": p }) {
  const y = we(), { isInteractive: v, minZoomReached: x, maxZoomReached: m, ariaLabelConfig: S } = ue(jm, xe), { zoomIn: g, zoomOut: w, fitView: E } = Vi(), k = () => {
    g(), i?.();
  }, j = () => {
    w(), s?.();
  }, A = () => {
    E(r), c?.();
  }, $ = () => {
    y.setState({
      nodesDraggable: !v,
      nodesConnectable: !v,
      elementsSelectable: !v
    }), d?.(!v);
  }, D = h === "horizontal" ? "horizontal" : "vertical";
  return a.jsxs(dr, { className: Ce(["react-flow__controls", D, l]), position: u, style: e, "data-testid": "rf__controls", "aria-label": p ?? S["controls.ariaLabel"], children: [t && a.jsxs(a.Fragment, { children: [a.jsx(Co, { onClick: k, className: "react-flow__controls-zoomin", title: S["controls.zoomIn.ariaLabel"], "aria-label": S["controls.zoomIn.ariaLabel"], disabled: m, children: a.jsx(bm, {}) }), a.jsx(Co, { onClick: j, className: "react-flow__controls-zoomout", title: S["controls.zoomOut.ariaLabel"], "aria-label": S["controls.zoomOut.ariaLabel"], disabled: x, children: a.jsx(Nm, {}) })] }), n && a.jsx(Co, { className: "react-flow__controls-fitview", onClick: A, title: S["controls.fitView.ariaLabel"], "aria-label": S["controls.fitView.ariaLabel"], children: a.jsx(Sm, {}) }), o && a.jsx(Co, { className: "react-flow__controls-interactive", onClick: $, title: S["controls.interactive.ariaLabel"], "aria-label": S["controls.interactive.ariaLabel"], children: v ? a.jsx(km, {}) : a.jsx(Em, {}) }), f] });
}
Ml.displayName = "Controls";
const Dl = Se(Ml);
function Cm({ id: e, x: t, y: n, width: o, height: r, style: i, color: s, strokeColor: c, strokeWidth: d, className: l, borderRadius: f, shapeRendering: u, selected: h, onClick: p }) {
  const { background: y, backgroundColor: v } = i || {}, x = s || y || v;
  return a.jsx("rect", { className: Ce(["react-flow__minimap-node", { selected: h }, l]), x: t, y: n, rx: f, ry: f, width: o, height: r, style: {
    fill: x,
    stroke: c,
    strokeWidth: d
  }, shapeRendering: u, onClick: p ? (m) => p(m, e) : void 0 });
}
const Im = Se(Cm), _m = (e) => e.nodes.map((t) => t.id), Gr = (e) => e instanceof Function ? e : () => e;
function Am({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: i = Im,
  onClick: s
}) {
  const c = ue(_m, xe), d = Gr(t), l = Gr(e), f = Gr(n), u = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return a.jsx(a.Fragment, { children: c.map((h) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    a.jsx(Dm, { id: h, nodeColorFunc: d, nodeStrokeColorFunc: l, nodeClassNameFunc: f, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: i, onClick: s, shapeRendering: u }, h)
  )) });
}
function Mm({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: i, shapeRendering: s, NodeComponent: c, onClick: d }) {
  const { node: l, x: f, y: u, width: h, height: p } = ue((y) => {
    const v = y.nodeLookup.get(e);
    if (!v)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const x = v.internals.userNode, { x: m, y: S } = v.internals.positionAbsolute, { width: g, height: w } = dt(x);
    return {
      node: x,
      x: m,
      y: S,
      width: g,
      height: w
    };
  }, xe);
  return !l || l.hidden || !Cc(l) ? null : a.jsx(c, { x: f, y: u, width: h, height: p, style: l.style, selected: !!l.selected, className: o(l), color: t(l), borderRadius: r, strokeColor: n(l), strokeWidth: i, shapeRendering: s, onClick: d, id: l.id });
}
const Dm = Se(Mm);
var Pm = Se(Am);
const $m = 200, Tm = 150, zm = (e) => !e.hidden, Rm = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? kc(qn(e.nodeLookup, { filter: zm }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, Lm = "react-flow__minimap-desc";
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
  maskStrokeColor: f,
  maskStrokeWidth: u,
  position: h = "bottom-right",
  onClick: p,
  onNodeClick: y,
  pannable: v = !1,
  zoomable: x = !1,
  ariaLabel: m,
  inversePan: S,
  zoomStep: g = 1,
  offsetScale: w = 5
}) {
  const E = we(), k = ce(null), { boundingRect: j, viewBB: A, rfId: $, panZoom: D, translateExtent: C, flowWidth: R, flowHeight: F, ariaLabelConfig: N } = ue(Rm, xe), _ = e?.width ?? $m, I = e?.height ?? Tm, P = j.width / _, T = j.height / I, z = Math.max(P, T), O = z * _, W = z * I, B = w * z, K = j.x - (O - j.width) / 2 - B, q = j.y - (W - j.height) / 2 - B, ne = O + B * 2, ae = W + B * 2, G = `${Lm}-${$}`, L = ce(0), U = ce();
  L.current = z, oe(() => {
    if (k.current && D)
      return U.current = Jp({
        domNode: k.current,
        panZoom: D,
        getTransform: () => E.getState().transform,
        getViewScale: () => L.current
      }), () => {
        U.current?.destroy();
      };
  }, [D]), oe(() => {
    U.current?.update({
      translateExtent: C,
      width: R,
      height: F,
      inversePan: S,
      pannable: v,
      zoomStep: g,
      zoomable: x
    });
  }, [v, x, S, g, C, R, F]);
  const ie = p ? (ee) => {
    const [le, H] = U.current?.pointer(ee) || [0, 0];
    p(ee, { x: le, y: H });
  } : void 0, se = y ? pe((ee, le) => {
    const H = E.getState().nodeLookup.get(le).internals.userNode;
    y(ee, H);
  }, []) : void 0, J = m ?? N["minimap.ariaLabel"];
  return a.jsx(dr, { position: h, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof f == "string" ? f : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof u == "number" ? u * z : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof s == "number" ? s : void 0
  }, className: Ce(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: a.jsxs("svg", { width: _, height: I, viewBox: `${K} ${q} ${ne} ${ae}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": G, ref: k, onClick: ie, children: [J && a.jsx("title", { id: G, children: J }), a.jsx(Pm, { onClick: se, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: i, nodeClassName: r, nodeStrokeWidth: s, nodeComponent: c }), a.jsx("path", { className: "react-flow__minimap-mask", d: `M${K - B},${q - B}h${ne + B * 2}v${ae + B * 2}h${-ne - B * 2}z
        M${A.x},${A.y}h${A.width}v${A.height}h${-A.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
Pl.displayName = "MiniMap";
const $l = Se(Pl), Vm = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, Hm = {
  [on.Line]: "right",
  [on.Handle]: "bottom-right"
};
function Om({ nodeId: e, position: t, variant: n = on.Handle, className: o, style: r = void 0, children: i, color: s, minWidth: c = 10, minHeight: d = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: f = Number.MAX_VALUE, keepAspectRatio: u = !1, resizeDirection: h, autoScale: p = !0, shouldResize: y, onResizeStart: v, onResize: x, onResizeEnd: m }) {
  const S = cl(), g = typeof e == "string" ? e : S, w = we(), E = ce(null), k = n === on.Handle, j = ue(pe(Vm(k && p), [k, p]), xe), A = ce(null), $ = t ?? Hm[n];
  oe(() => {
    if (!(!E.current || !g))
      return A.current || (A.current = ug({
        domNode: E.current,
        nodeId: g,
        getStoreItems: () => {
          const { nodeLookup: C, transform: R, snapGrid: F, snapToGrid: N, nodeOrigin: _, domNode: I } = w.getState();
          return {
            nodeLookup: C,
            transform: R,
            snapGrid: F,
            snapToGrid: N,
            nodeOrigin: _,
            paneDomNode: I
          };
        },
        onChange: (C, R) => {
          const { triggerNodeChanges: F, nodeLookup: N, parentLookup: _, nodeOrigin: I } = w.getState(), P = [], T = { x: C.x, y: C.y }, z = N.get(g);
          if (z && z.expandParent && z.parentId) {
            const O = z.origin ?? I, W = C.width ?? z.measured.width ?? 0, B = C.height ?? z.measured.height ?? 0, K = {
              id: z.id,
              parentId: z.parentId,
              rect: {
                width: W,
                height: B,
                ...Ic({
                  x: C.x ?? z.position.x,
                  y: C.y ?? z.position.y
                }, { width: W, height: B }, z.parentId, N, O)
              }
            }, q = Li([K], N, _, I);
            P.push(...q), T.x = C.x ? Math.max(O[0] * W, C.x) : void 0, T.y = C.y ? Math.max(O[1] * B, C.y) : void 0;
          }
          if (T.x !== void 0 && T.y !== void 0) {
            const O = {
              id: g,
              type: "position",
              position: { ...T }
            };
            P.push(O);
          }
          if (C.width !== void 0 && C.height !== void 0) {
            const W = {
              id: g,
              type: "dimensions",
              resizing: !0,
              setAttributes: h ? h === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: C.width,
                height: C.height
              }
            };
            P.push(W);
          }
          for (const O of R) {
            const W = {
              ...O,
              type: "position"
            };
            P.push(W);
          }
          F(P);
        },
        onEnd: ({ width: C, height: R }) => {
          const F = {
            id: g,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: C,
              height: R
            }
          };
          w.getState().triggerNodeChanges([F]);
        }
      })), A.current.update({
        controlPosition: $,
        boundaries: {
          minWidth: c,
          minHeight: d,
          maxWidth: l,
          maxHeight: f
        },
        keepAspectRatio: u,
        resizeDirection: h,
        onResizeStart: v,
        onResize: x,
        onResizeEnd: m,
        shouldResize: y
      }), () => {
        A.current?.destroy();
      };
  }, [
    $,
    c,
    d,
    l,
    f,
    u,
    v,
    x,
    m,
    y
  ]);
  const D = $.split("-");
  return a.jsx("div", { className: Ce(["react-flow__resize-control", "nodrag", ...D, n, o]), ref: E, style: {
    ...r,
    scale: j,
    ...s && { [k ? "backgroundColor" : "borderColor"]: s }
  }, children: i });
}
Se(Om);
const Fm = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), Tl = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var Bm = {
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
const Wm = er(
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
      ...Bm,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: Tl("lucide", r),
      ...c
    },
    [
      ...s.map(([l, f]) => ei(l, f)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const ve = (e, t) => {
  const n = er(
    ({ className: o, ...r }, i) => ei(Wm, {
      ref: i,
      iconNode: t,
      className: Tl(`lucide-${Fm(e)}`, o),
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
const Vt = ve("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const Rl = ve("ChevronDown", [
  ["path", { d: "m6 9 6 6 6-6", key: "qrunsl" }]
]);
const Zo = ve("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const Xt = ve("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const Be = ve("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const Xm = ve("Copy", [
  ["rect", { width: "14", height: "14", x: "8", y: "8", rx: "2", ry: "2", key: "17jyea" }],
  ["path", { d: "M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2", key: "zix9uf" }]
]);
const Ll = ve("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const Ym = ve("GripVertical", [
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
const ga = ve("Minimize2", [
  ["polyline", { points: "4 14 10 14 10 20", key: "11kfnr" }],
  ["polyline", { points: "20 10 14 10 14 4", key: "rlmsce" }],
  ["line", { x1: "14", x2: "21", y1: "10", y2: "3", key: "o5lafz" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const qm = ve("Package", [
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
const Oi = ve("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const Fi = ve("RefreshCcw", [
  ["path", { d: "M21 12a9 9 0 0 0-9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "14sxne" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }],
  ["path", { d: "M3 12a9 9 0 0 0 9 9 9.75 9.75 0 0 0 6.74-2.74L21 16", key: "1hlbsb" }],
  ["path", { d: "M16 16h5v5", key: "ccwih5" }]
]);
const Bi = ve("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const Um = ve("Save", [
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
const mt = ve("Sparkles", [
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
const On = ve("Trash2", [
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
const Zm = ve("Zap", [
  [
    "path",
    {
      d: "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z",
      key: "1xq2db"
    }
  ]
]), _e = "/_elsa/workflow-management", Km = "/publishing";
async function Gm(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${_e}/definitions?${n.toString()}`);
}
async function Jm(e, t) {
  return e.http.getJson(`${_e}/definitions/${encodeURIComponent(t)}`);
}
async function Qm(e, t) {
  return e.http.getJson(`${_e}/versions/${encodeURIComponent(t)}`);
}
async function ex(e, t) {
  return e.http.postJson(`${_e}/definitions`, t);
}
async function tx(e, t) {
  await e.http.deleteJson(`${_e}/definitions/${encodeURIComponent(t)}`);
}
async function nx(e, t) {
  await e.http.postJson(`${_e}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function ox(e, t) {
  await e.http.deleteJson(`${_e}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function rx(e, t) {
  return e.http.putJson(`${_e}/drafts/${encodeURIComponent(t.id)}`, { state: t.state, layout: t.layout });
}
async function ix(e, t) {
  return e.http.postJson(`${_e}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function sx(e, t) {
  return e.http.postJson(`${_e}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function ax(e, t) {
  try {
    return await e.http.postJson(`${Km}/workflows/drafts/test-runs`, t);
  } catch (n) {
    const o = px(n);
    if (o) return o;
    throw n;
  }
}
async function Bl(e, t) {
  return e.http.postJson(`${_e}/executables/${encodeURIComponent(t)}/run`, {});
}
async function Wl(e, t = "active") {
  const n = await e.http.getJson(`${_e}/executables?state=${encodeURIComponent(t)}`);
  return Array.isArray(n) ? n : n.executables;
}
async function cx(e, t) {
  await e.http.deleteJson(`${_e}/executables/${encodeURIComponent(t)}`);
}
async function lx(e, t) {
  await e.http.postJson(`${_e}/executables/${encodeURIComponent(t)}/restore`, {});
}
async function dx(e, t) {
  await e.http.deleteJson(`${_e}/executables/${encodeURIComponent(t)}/permanent`);
}
async function Xl(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const o = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${o ? `?${o}` : ""}`);
}
async function ux(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function Wi(e) {
  return e.http.getJson(`${_e}/activities`);
}
async function fx(e) {
  const t = await Yl(e, [
    `${_e}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? ya(t) : ya(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function hx(e) {
  const t = await Yl(e, [
    `${_e}/descriptors/expression-descriptors`,
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
function ya(e) {
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
function px(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = ma(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return ma(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function ma(e) {
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
], hr = "elsa.sequence.structure", Kn = "elsa.flowchart.structure";
function ql(e, t) {
  if (!e) return null;
  let n = e;
  for (const o of t) {
    const r = Ge(n).find((s) => s.id === o.slotId);
    if (!r) return null;
    const i = r.activities.find((s) => s.nodeId === o.ownerNodeId);
    if (!i) return null;
    n = i;
  }
  return n;
}
function Mn(e, t) {
  const n = ql(e, t);
  if (!n) return null;
  let o = Ge(n)[0];
  return o ? { owner: n, slot: o } : null;
}
function Ge(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = $x(t), r = Jr(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: Tx(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, i]) => Jr(i)).map(([i, s]) => ({
    id: `${t.kind}:${i}`,
    label: Rx(i),
    property: i,
    mode: "generic",
    activities: Jr(s) ?? []
  }));
}
function Ul(e, t, n) {
  const o = new Map(t.map((s) => [s.activityVersionId, s])), r = new Map(n.map((s) => [s.nodeId, s])), i = e.slot.activities.map((s, c) => {
    const d = o.get(s.activityVersionId), l = r.get(s.nodeId) ?? zx(e.slot.mode, c);
    return Gl(s, d, { x: l.x, y: l.y });
  });
  return {
    nodes: i,
    edges: e.slot.mode === "flowchart" ? jx(e.owner) : kx(e.slot, i)
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
function gx(e, t, n, o = null) {
  const r = new Map(t.map((c) => [c.activityExecutionId, c])), i = wa(t, (c) => c.authoredActivityId || c.executableNodeId), s = wa(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? r.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const d = i.get(c.id) ?? [], l = s.get(c.id) ?? [];
    if (d.length === 0 && l.length === 0) return c;
    const f = Mx(d), u = o === c.id || d.some((p) => p.activityExecutionId === o) || l.some((p) => p.incidentId === o), h = {
      status: f?.status,
      subStatus: f?.subStatus,
      activityExecutionId: f?.activityExecutionId,
      faultCount: d.reduce((p, y) => p + y.faultCount + y.aggregateFaultCount, 0),
      incidentCount: l.length,
      hasBlockingIncident: l.some((p) => p.isBlocking),
      selected: u
    };
    return {
      ...c,
      selected: u,
      className: u ? "wf-runtime-node-selected" : c.className,
      data: {
        ...c.data,
        runtime: h
      }
    };
  });
}
function Xi(e, t) {
  return e?.structure?.kind === Kn || vx(t) ? "flowchart" : e?.structure?.kind === hr || bx(t) ? "sequence" : "unsupported";
}
function gi(e, t, n) {
  if (t.length === 0) {
    const c = Ge(e)[0];
    return c ? Fn(e, c, n) : e;
  }
  const [o, ...r] = t, i = Ge(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((c) => c.nodeId === o.ownerNodeId ? gi(c, r, n) : c);
  return Fn(e, i, s);
}
function Zl(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, i = Ge(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((c) => c.nodeId === o.ownerNodeId ? Zl(c, r, n) : c);
  return Fn(e, i, s);
}
function Kl(e, t, n) {
  if (e.nodeId === t) return n(e);
  const o = Ge(e);
  if (o.length === 0) return e;
  let r = !1, i = e;
  for (const s of o) {
    const c = s.activities.map((d) => {
      const l = Kl(d, t, n);
      return l !== d && (r = !0), l;
    });
    r && (i = Fn(i, s, c));
  }
  return r ? i : e;
}
function Fn(e, t, n) {
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
function yx(e, t, n, o = []) {
  const r = new Map(e.slot.activities.map((s) => [s.nodeId, s]));
  for (const s of o)
    r.set(s.nodeId, s);
  const i = t.map((s) => r.get(s.id)).filter((s) => !!s);
  return e.slot.mode === "sequence" && i.sort((s, c) => {
    const d = t.find((f) => f.id === s.nodeId), l = t.find((f) => f.id === c.nodeId);
    return (d?.position.x ?? 0) - (l?.position.x ?? 0);
  }), Fn(e.owner, e.slot, i);
}
function mx(e, t) {
  return {
    ...e,
    structure: Ex(e.structure, t)
  };
}
function xx(e, t) {
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
    structure: Sx(e)
  };
}
function Ie(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? Nx(t) : n;
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
      label: t ? Ie(t) : e.activityVersionId,
      activityVersionId: e.activityVersionId,
      activityTypeKey: t?.activityTypeKey,
      category: t?.category,
      executionType: t?.executionType,
      icon: mi(t),
      childSlots: Ge(e),
      acceptsInbound: Cx(e, t),
      sourcePorts: o.suppressFlowPorts ? [] : Jl(e, t),
      suppressFlowPorts: o.suppressFlowPorts
    }
  };
}
function mi(e) {
  if (!e) return "activity";
  const t = wx(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), o = Ie(e).toLowerCase(), r = e.category?.toLowerCase() ?? "", i = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || o === "flowchart" ? "flowchart" : n.endsWith(".sequence") || o === "sequence" ? "sequence" : n.includes("writeline") || o.includes("write line") ? "terminal" : r.includes("runtime") ? "runtime" : i === "trigger" ? "trigger" : "activity";
}
function wx(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function vx(e) {
  return !!e && (Ie(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function bx(e) {
  return !!e && (Ie(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function Nx(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function Sx(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: hr,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: Kn,
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
function Ex(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], o = /* @__PURE__ */ new Map();
  for (const r of n) {
    if (!Yi(r)) continue;
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
function kx(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function jx(e) {
  if (e.structure?.kind !== Kn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, i = n.target;
    if (!r?.nodeId || !i?.nodeId) return null;
    const s = Array.isArray(n.vertices) ? n.vertices.filter(Dx) : [];
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
  const n = xa(e.cases);
  if (_x(e, t) && n.length > 0)
    return [...n.map((i) => ({ name: i, displayName: i })), { name: "Default", displayName: "Default" }];
  const o = [
    ...zo(t?.designFacets),
    ...zo(t?.ports),
    ...zo(t?.outputs)
  ];
  if (o.length > 0) return Ax(o);
  const r = xa(e.outcomes);
  return r.length > 0 ? r.map((i) => ({ name: i, displayName: i })) : [{ name: "Done", displayName: "Done" }];
}
function Cx(e, t) {
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
function Ix(e, t, n) {
  const o = Go(t.source, n, t.sourceHandle ?? "Done", void 0), r = Go(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((i) => i.id !== t.id).concat(o, r);
}
function Jr(e) {
  return Array.isArray(e) ? e.filter(Px) : null;
}
function _x(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function zo(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!Yi(n)) continue;
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
function Ax(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function xa(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function wa(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const o of e) {
    const r = t(o);
    r && n.set(r, [...n.get(r) ?? [], o]);
  }
  return n;
}
function Mx(e) {
  return [...e].sort((t, n) => va(n).localeCompare(va(t)))[0];
}
function va(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function Dx(e) {
  return Yi(e) && typeof e.x == "number" && typeof e.y == "number";
}
function Yi(e) {
  return typeof e == "object" && e !== null;
}
function Px(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function $x(e) {
  return e.kind === hr ? "sequence" : e.kind === Kn ? "flowchart" : "generic";
}
function Tx(e) {
  return e.kind === hr || e.kind === Kn, "Activities";
}
function zx(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function Rx(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Lx = [
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
function qi(e) {
  return Ql(e.name);
}
function Vx(e, t) {
  const n = qi(t), o = e[n];
  return t.isWrapped === !1 ? o ?? t.defaultValue ?? "" : td(o, t);
}
function ed(e, t) {
  return td(e[qi(t)], t);
}
function Hx(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function Ox(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function ba(e, t, n) {
  return {
    ...e,
    [qi(t)]: n
  };
}
function Fx(e, t) {
  return t.isWrapped === !1 ? Vx(e, t) : ed(e, t).expression.value;
}
function td(e, t) {
  return Bx(e) ? {
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
function Bx(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
const nd = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]);
function Wx({
  activity: e,
  descriptor: t,
  editors: n,
  expressionEditors: o,
  expressionDescriptors: r,
  descriptorStatus: i,
  onChange: s
}) {
  if (i === "loading")
    return /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "Loading activity properties..." });
  if (!t)
    return /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "No activity descriptor is available for this activity." });
  const c = t.inputs.filter((f) => f.isBrowsable !== !1).sort((f, u) => (f.order ?? 0) - (u.order ?? 0) || f.name.localeCompare(u.name));
  if (c.length === 0)
    return /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "This activity does not expose editable properties." });
  const d = Kx(c), l = r.length > 0 ? r : Lx;
  return /* @__PURE__ */ a.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ a.jsx("span", { className: "wf-section-label", children: "Properties" }),
    d.map((f) => /* @__PURE__ */ a.jsxs("section", { className: "wf-property-group", children: [
      d.length > 1 ? /* @__PURE__ */ a.jsx("h4", { children: f.category }) : null,
      f.inputs.map((u) => /* @__PURE__ */ a.jsx(
        Xx,
        {
          activity: e,
          input: u,
          editors: n,
          expressionEditors: o,
          expressionDescriptors: l,
          onChange: s
        },
        u.name
      ))
    ] }, f.category))
  ] });
}
function Xx({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: o,
  expressionDescriptors: r,
  onChange: i
}) {
  const s = t.isReadOnly === !0, c = { activity: e, expressionDescriptors: r, readOnly: s }, d = Ux(n, t, c), l = d?.component, f = t.isWrapped !== !1 ? ed(e, t) : null, u = f?.expression.type ?? "Literal", h = Fx(e, t), p = f ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: r,
    readOnly: s,
    surface: "inline",
    syntax: u
  } : null, y = p ? od(o, p) : null, v = y?.surfaces.inline, x = y && p ? rd(y, p, h) : [], m = !!(f && Gx(t, d?.id)), S = !!(f && Jx(t, d?.id)), [g, w] = Y(!1), E = (A) => {
    const $ = f ? Hx(f, A) : A;
    i(ba(e, t, $));
  }, k = (A) => {
    f && i(ba(e, t, Ox(f, A)));
  }, j = v && p ? /* @__PURE__ */ a.jsx(
    v,
    {
      descriptor: t,
      syntax: u,
      value: h,
      disabled: s,
      context: p,
      onChange: E
    }
  ) : qx(l, t, h, s, c, E);
  return /* @__PURE__ */ a.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ a.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ a.jsx("span", { children: id(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ a.jsx("p", { children: t.description }) : null,
    f && !m ? /* @__PURE__ */ a.jsx(
      xi,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: u,
        descriptors: r,
        disabled: s,
        onChange: k
      }
    ) : null,
    m ? /* @__PURE__ */ a.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-expression-editor", children: [
        j,
        wi(x)
      ] }),
      /* @__PURE__ */ a.jsx(
        xi,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: u,
          descriptors: r,
          disabled: s,
          variant: "inline",
          onChange: k
        }
      ),
      S ? /* @__PURE__ */ a.jsx(
        "button",
        {
          type: "button",
          className: "wf-expression-expand-button",
          "aria-label": `Open expanded ${t.displayName || t.name} editor`,
          title: "Open expanded editor",
          onClick: () => w(!0),
          children: /* @__PURE__ */ a.jsx(Ko, { size: 13 })
        }
      ) : null
    ] }) : /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
      j,
      wi(x)
    ] }),
    S && !m ? /* @__PURE__ */ a.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => w(!0),
        children: [
          /* @__PURE__ */ a.jsx(Ko, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    g ? /* @__PURE__ */ a.jsx(
      Yx,
      {
        input: t,
        value: h,
        syntax: u,
        descriptors: r,
        activity: e,
        expressionEditors: o,
        disabled: s,
        onChange: E,
        onSyntaxChange: k,
        onClose: () => w(!1)
      }
    ) : null
  ] });
}
function Yx({
  input: e,
  value: t,
  syntax: n,
  descriptors: o,
  activity: r,
  expressionEditors: i,
  disabled: s,
  onChange: c,
  onSyntaxChange: d,
  onClose: l
}) {
  const f = Oa(), u = e.displayName || e.name, h = {
    activity: r,
    descriptor: e,
    expressionDescriptors: o,
    readOnly: s,
    surface: "expanded",
    syntax: n
  }, p = od(i, h), y = p?.surfaces.expanded, v = p ? rd(p, h, t) : [], x = y ? null : Zx(i, h);
  return oe(() => {
    const m = (S) => {
      S.key === "Escape" && l();
    };
    return window.addEventListener("keydown", m), () => window.removeEventListener("keydown", m);
  }, [l]), /* @__PURE__ */ a.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ a.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": f, children: [
    /* @__PURE__ */ a.jsxs("header", { children: [
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ a.jsx("h3", { id: f, children: u })
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", "aria-label": `Close ${u} editor`, onClick: l, children: /* @__PURE__ */ a.jsx(Fl, { size: 16 }) })
    ] }),
    /* @__PURE__ */ a.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ a.jsx(
          xi,
          {
            label: `${u} expression syntax`,
            value: n,
            descriptors: o,
            disabled: s,
            onChange: d
          }
        ),
        /* @__PURE__ */ a.jsx("span", { children: id(e.typeName) })
      ] }),
      e.description ? /* @__PURE__ */ a.jsx("p", { children: e.description }) : null,
      y ? /* @__PURE__ */ a.jsx(
        y,
        {
          descriptor: e,
          syntax: n,
          value: t,
          disabled: s,
          context: h,
          onChange: c
        }
      ) : /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
        x ? /* @__PURE__ */ a.jsx("p", { className: "wf-expression-editor-hint", children: x }) : null,
        /* @__PURE__ */ a.jsx(
          "textarea",
          {
            "aria-label": `${u} expanded value`,
            value: t == null ? "" : String(t),
            disabled: s,
            spellCheck: !1,
            onChange: (m) => c(m.target.value)
          }
        )
      ] }),
      wi(v)
    ] }),
    /* @__PURE__ */ a.jsxs("footer", { children: [
      /* @__PURE__ */ a.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: l, children: "Close" })
    ] })
  ] }) });
}
function qx(e, t, n, o, r, i) {
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
  const [s, c] = Y(!1), d = Oa(), l = n.find((u) => u.type === t), f = [
    "wf-syntax-picker-trigger",
    r === "inline" ? "inline" : "",
    s ? "open" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ a.jsxs("div", { className: r === "inline" ? "wf-syntax-picker inline" : "wf-syntax-picker", onBlur: (u) => {
    u.currentTarget.contains(u.relatedTarget) || c(!1);
  }, children: [
    /* @__PURE__ */ a.jsx(
      "button",
      {
        type: "button",
        className: f,
        "aria-label": e,
        "aria-haspopup": "listbox",
        "aria-expanded": s,
        "aria-controls": d,
        disabled: o,
        onClick: () => c((u) => !u),
        children: /* @__PURE__ */ a.jsx("span", { children: l?.displayName || l?.type || t })
      }
    ),
    s ? /* @__PURE__ */ a.jsx("div", { id: d, role: "listbox", className: "wf-syntax-picker-menu", "aria-label": e, children: n.map((u) => {
      const h = u.displayName || u.type, p = u.type === t;
      return /* @__PURE__ */ a.jsx(
        "button",
        {
          type: "button",
          role: "option",
          "aria-selected": p,
          className: p ? "selected" : "",
          onClick: () => {
            i(u.type), c(!1);
          },
          children: h
        },
        u.type
      );
    }) }) : null
  ] });
}
function Ux(e, t, n) {
  return [...e].sort((o, r) => (o.order ?? 500) - (r.order ?? 500)).find((o) => o.supports(t, n));
}
function od(e, t) {
  return [...e].sort((n, o) => (n.order ?? 500) - (o.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function rd(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function Zx(e, t) {
  if (t.syntax.toLowerCase() === "literal") return null;
  const n = [...e].sort((s, c) => (s.order ?? 500) - (c.order ?? 500)).find((s) => s.supports(t) && s.metadata)?.metadata;
  if (!n) return `No enhanced editor is registered for ${t.syntax}. Using the generic text editor.`;
  const o = n.displayName?.trim() || "enhanced editor", r = n.installHint?.trim(), i = `No ${o} is registered for ${t.syntax}. Using the generic text editor.`;
  return r ? `${i} ${r}` : i;
}
function wi(e) {
  return e.length === 0 ? null : /* @__PURE__ */ a.jsx("div", { className: "wf-expression-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const o = t.severity ?? "info";
    return /* @__PURE__ */ a.jsxs("p", { className: `wf-expression-editor-diagnostic ${o}`, children: [
      t.code ? /* @__PURE__ */ a.jsx("span", { children: t.code }) : null,
      t.message
    ] }, `${t.code ?? "diagnostic"}-${n}`);
  }) });
}
function Kx(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "General";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return [...t.entries()].map(([n, o]) => ({ category: n, inputs: o }));
}
function id(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function Gx(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !nd.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function Jx(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !nd.has(t) && n !== "multiline") return !1;
  const o = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(o) || n === "singleline" || n === "multiline";
}
const Na = "elsa-studio:apply-workflow-graph-operation-batch", Sa = "elsa-studio:undo-workflow-graph-operation-batch", Qx = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function e0(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const o = c0(e), r = ad(o.state.rootActivity), i = /* @__PURE__ */ new Map(), s = /* @__PURE__ */ new Map(), c = [];
  for (const d of t.operations) {
    const l = a0(d.kind), f = d.parameters ?? {};
    if (l === "add-activity") {
      const u = $e(f.activityId) ?? d.temporaryReferences?.[0], h = s0(u ?? $e(f.displayName) ?? $e(f.activityType) ?? "weaver-activity", r), p = t0(d, h, n);
      s.set(h, p), c.push(h), u && i.set(u, h), o.state.rootActivity && n0(o.state.rootActivity, p);
      const y = Mt(f.position) ? vi(f.position, { x: 280, y: 160 }) : null;
      y && (o.layout = Ea(o.layout, h, y));
      continue;
    }
    if (l === "set-root") {
      const u = Qr(o, f.activityId, i, s);
      if (!u) throw new Error("Weaver batch referenced an unknown root activity.");
      o.state.rootActivity = u;
      continue;
    }
    if (l === "set-designer-position") {
      const u = Rt(f.activityId, i);
      if (!u || !Ui(o.state.rootActivity, u)) throw new Error("Weaver batch referenced an unknown activity position.");
      o.layout = Ea(o.layout, u, vi(f, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const u = Qr(o, f.activityId, i, s);
      if (!u) throw new Error("Weaver batch referenced an unknown activity property target.");
      i0(u, $e(f.propertyName) ?? "Value", f.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const u = Qr(o, f.activityId, i, s);
      if (!u) throw new Error("Weaver batch referenced an unknown activity update target.");
      const h = Mt(f.patch) ? f.patch : f;
      Object.assign(u, h);
      continue;
    }
    if (l === "remove-activity") {
      const u = Rt(f.activityId, i);
      if (!u) throw new Error("Weaver batch referenced an unknown activity remove target.");
      o.state.rootActivity = sd(o.state.rootActivity, u), o.layout = o.layout.filter((h) => h.nodeId !== u);
      continue;
    }
    if (l === "connect-activities") {
      o0(o, f, i);
      continue;
    }
    if (l === "disconnect-activities") {
      r0(o, f, i);
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
function t0(e, t, n) {
  const o = e.parameters ?? {}, r = $e(o.activityVersionId) ?? $e(o.activityType) ?? "Elsa.Workflows.Activity", i = n.find((s) => s.activityVersionId === r || s.activityTypeKey === r || s.displayName === $e(o.displayName));
  return i ? yi(i, t) : {
    nodeId: t,
    activityVersionId: i?.activityVersionId ?? r,
    inputs: [],
    outputs: [],
    ...$e(o.displayName) ? { displayName: $e(o.displayName) } : {},
    designer: { position: vi(o.position, { x: 280, y: 160 }) }
  };
}
function n0(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = Zi(e);
  n && !n.some((o) => o.nodeId === t.nodeId) && n.push(t);
}
function o0(e, t, n) {
  const o = e.state.rootActivity;
  if (!o?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const r = Rt(t.sourceActivityId ?? t.sourceId ?? t.from, n), i = Rt(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!r || !i) throw new Error("Weaver batch connection is missing source or target activity.");
  const s = o.structure.payload, c = Array.isArray(s.connections) ? s.connections : [], d = $e(t.connectionId) ?? `flow-${r}-${i}`;
  s.connections = [
    ...c.filter((l) => !Mt(l) || l.id !== d),
    {
      id: d,
      source: { nodeId: r, port: $e(t.outcome) ?? $e(t.sourcePort) ?? "Done" },
      target: { nodeId: i }
    }
  ];
}
function r0(e, t, n) {
  const o = e.state.rootActivity, r = o?.structure?.payload.connections;
  if (!Array.isArray(r)) return;
  const i = $e(t.connectionId), s = Rt(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = Rt(t.targetActivityId ?? t.targetId ?? t.to, n);
  o.structure.payload.connections = r.filter((d) => {
    if (!Mt(d)) return !0;
    if (i && d.id === i) return !1;
    const l = Mt(d.source) ? d.source.nodeId : void 0, f = Mt(d.target) ? d.target.nodeId : void 0;
    return l !== s || f !== c;
  });
}
function i0(e, t, n) {
  e[Ql(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: "Literal", value: n }
  };
}
function Qr(e, t, n, o) {
  const r = Rt(t, n);
  return r ? Ui(e.state.rootActivity, r) ?? o.get(r) ?? null : null;
}
function Rt(e, t) {
  const n = $e(e);
  return n ? t.get(n) ?? n : null;
}
function Ui(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of cd(e)) {
    const o = Ui(n, t);
    if (o) return o;
  }
  return null;
}
function sd(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = Zi(e);
  if (n) {
    const o = n.map((r) => sd(r, t)).filter((r) => !!r);
    n.splice(0, n.length, ...o);
  }
  return e;
}
function ad(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of cd(e)) ad(n, t);
  return t;
}
function cd(e) {
  return Zi(e) ?? [];
}
function Zi(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function Ea(e, t, n) {
  return [
    ...e.filter((o) => o.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function vi(e, t) {
  const n = Mt(e) ? e : {}, o = Number(n.x), r = Number(n.y);
  return {
    x: Number.isFinite(o) ? Math.max(40, Math.round(o)) : t.x,
    y: Number.isFinite(r) ? Math.max(40, Math.round(r)) : t.y
  };
}
function s0(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let o = n, r = 2;
  for (; t.has(o); )
    o = `${n}-${r}`, r += 1;
  return t.add(o), o;
}
function a0(e) {
  return typeof e == "number" ? Qx[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function $e(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function c0(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function Mt(e) {
  return typeof e == "object" && e !== null;
}
const ld = { workflowActivity: J0 }, dd = { workflow: ew }, ka = "application/x-elsa-activity-version-id", l0 = 6, d0 = 1200, u0 = [10, 25, 50], f0 = 10, ja = "elsa-studio-workflow-palette-width", Ca = "elsa-studio-workflow-inspector-width", Ia = "elsa-studio-workflow-palette-collapsed", _a = "elsa-studio-workflow-inspector-collapsed", ud = "elsa-studio-workflow-side-panel-maximized", bn = 180, Nn = 460, h0 = 260, Sn = 260, En = 560, p0 = 320, Aa = 42, Io = 16, fd = wt.createContext(null);
function hw(e) {
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
        component: () => /* @__PURE__ */ a.jsx(g0, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors.list(), workflowDesignerPanels: e.workflowDesigner.panels.list() })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ a.jsx(y0, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow instances",
        component: () => /* @__PURE__ */ a.jsx(m0, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow instance",
        component: () => /* @__PURE__ */ a.jsx(x0, { context: e.backend, ai: e.ai })
      }
    ]
  });
}
function g0({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: o,
  workflowDesignerPanels: r
}) {
  const [i, s] = Y(Ma);
  oe(() => {
    const d = () => s(Ma());
    return window.addEventListener("popstate", d), () => window.removeEventListener("popstate", d);
  }, []);
  const c = (d) => {
    const l = d ? `/workflows/definitions?definition=${encodeURIComponent(d)}` : "/workflows/definitions";
    window.history.pushState({}, "", l), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return i ? /* @__PURE__ */ a.jsx(G0, { context: e, definitionId: i, ai: t, propertyEditors: n, expressionEditors: o, workflowDesignerPanels: r, onBack: () => c(null) }) : /* @__PURE__ */ a.jsx(pr, { title: "Definitions", children: /* @__PURE__ */ a.jsx(v0, { context: e, ai: t, onOpen: c }) });
}
function y0({ context: e, ai: t }) {
  const [n, o] = Y(Jo);
  oe(() => {
    const i = () => o(Jo());
    return window.addEventListener("popstate", i), () => window.removeEventListener("popstate", i);
  }, []);
  const r = pe((i) => {
    const s = i?.trim() ?? "", c = new URL(window.location.href);
    s ? c.searchParams.set("definition", s) : c.searchParams.delete("definition"), o(s || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ a.jsx(pr, { title: "Executables", children: /* @__PURE__ */ a.jsx(N0, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: r }) });
}
function m0({ context: e, ai: t }) {
  const [n, o] = Y(Jo);
  oe(() => {
    const i = () => o(Jo());
    return window.addEventListener("popstate", i), () => window.removeEventListener("popstate", i);
  }, []);
  const r = pe((i) => {
    const s = i?.trim() ?? "", c = new URL(window.location.href);
    s ? c.searchParams.set("definition", s) : c.searchParams.delete("definition"), o(s || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ a.jsx(pr, { title: "Instances", children: /* @__PURE__ */ a.jsx(I0, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: r }) });
}
function x0({ context: e, ai: t }) {
  const n = w0();
  return /* @__PURE__ */ a.jsx(pr, { title: "Instance", children: /* @__PURE__ */ a.jsx(_0, { context: e, ai: t, workflowExecutionId: n }) });
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
function w0() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function v0({ context: e, ai: t, onOpen: n }) {
  const [o, r] = Y(""), [i, s] = Y("active"), [c, d] = Y(1), [l, f] = Y(f0), [u, h] = Y("loading"), [p, y] = Y(""), [v, x] = Y(""), [m, S] = Y([]), [g, w] = Y(0), [E, k] = Y(() => /* @__PURE__ */ new Set()), [j, A] = Y(null), [$, D] = Y(!1), [C, R] = Y([]), [F, N] = Y("idle"), _ = ce(null), I = ge(() => m.map((H) => H.id), [m]), P = Lt(t, "weaver.workflows.suggest-create-metadata"), T = Lt(t, "weaver.workflows.explain-definition"), z = I.filter((H) => E.has(H)).length, O = I.length > 0 && z === I.length, W = pe(async () => {
    h("loading"), y("");
    try {
      const H = await Gm(e, { search: o, state: i, page: c, pageSize: l }), Q = typeof H.totalCount == "number", fe = H.totalCount ?? H.definitions.length, me = hd(fe, l);
      if (fe > 0 && c > me) {
        d(me);
        return;
      }
      S(Q ? H.definitions : R0(H.definitions, c, l)), w(fe), h("ready");
    } catch (H) {
      y(H instanceof Error ? H.message : String(H)), h("failed");
    }
  }, [e, o, i, c, l]);
  oe(() => {
    W();
  }, [W]), oe(() => {
    _.current && (_.current.indeterminate = z > 0 && !O);
  }, [O, z]);
  const B = pe(async () => {
    if (!(F === "loading" || F === "ready")) {
      N("loading");
      try {
        const H = await Wi(e);
        R(H.activities ?? []), N("ready");
      } catch (H) {
        N("failed"), y(H instanceof Error ? H.message : String(H));
      }
    }
  }, [F, e]), K = () => {
    y(""), x(""), A({ name: "", description: "", rootKind: "flowchart" }), B();
  }, q = async () => {
    if (j?.name.trim()) {
      D(!0), y(""), x("");
      try {
        const H = await ex(e, {
          name: j.name.trim(),
          description: j.description.trim() || null,
          rootKind: j.rootKind,
          rootActivityVersionId: H0(j, C)
        });
        A(null), n(H.definition.id);
      } catch (H) {
        y(H instanceof Error ? H.message : String(H));
      } finally {
        D(!1);
      }
    }
  }, ne = (H) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(H)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, ae = async () => {
    if (m.length === 1 && c > 1) {
      d(c - 1);
      return;
    }
    await W();
  }, G = () => k(/* @__PURE__ */ new Set()), L = (H, Q) => {
    k((fe) => {
      const me = new Set(fe);
      return Q ? me.add(H) : me.delete(H), me;
    });
  }, U = (H) => {
    k((Q) => {
      const fe = new Set(Q);
      for (const me of I)
        H ? fe.add(me) : fe.delete(me);
      return fe;
    });
  }, ie = (H) => {
    s(H), d(1), G();
  }, se = (H) => {
    r(H), d(1), G();
  }, J = async (H) => {
    if (window.confirm(`Delete workflow definition "${H.name}"? You can restore it from the Deleted view.`)) {
      x(""), y("");
      try {
        await tx(e, H.id), L(H.id, !1), x(`Deleted ${H.name}`), await ae();
      } catch (Q) {
        y(Q instanceof Error ? Q.message : String(Q));
      }
    }
  }, ee = async (H) => {
    x(""), y("");
    try {
      await nx(e, H.id), L(H.id, !1), x(`Restored ${H.name}`), await ae();
    } catch (Q) {
      y(Q instanceof Error ? Q.message : String(Q));
    }
  }, le = async (H) => {
    if (window.confirm(`Permanently delete workflow definition "${H.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`)) {
      x(""), y("");
      try {
        await ox(e, H.id), L(H.id, !1), x(`Permanently deleted ${H.name}`), await ae();
      } catch (Q) {
        y(Q instanceof Error ? Q.message : String(Q));
      }
    }
  };
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ a.jsx("button", { type: "button", className: i === "active" ? "active" : "", "aria-selected": i === "active", onClick: () => ie("active"), children: "Active" }),
        /* @__PURE__ */ a.jsx("button", { type: "button", className: i === "deleted" ? "active" : "", "aria-selected": i === "deleted", onClick: () => ie("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ a.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ a.jsx(Hl, { size: 15 }),
        /* @__PURE__ */ a.jsx("input", { value: o, onChange: (H) => se(H.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
        W();
      }, children: "Refresh" }),
      /* @__PURE__ */ a.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ a.jsxs("button", { type: "button", title: "Create workflow", onClick: K, children: [
        /* @__PURE__ */ a.jsx(Oi, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    u === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(Be, { size: 16 }),
      " ",
      p
    ] }) : null,
    u !== "failed" && p ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(Be, { size: 16 }),
      " ",
      p
    ] }) : null,
    v ? /* @__PURE__ */ a.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ a.jsx(Vt, { size: 14 }),
      " ",
      v
    ] }) : null,
    E.size > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ a.jsxs("span", { children: [
        E.size,
        " selected"
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: G, children: "Clear selection" })
    ] }) : null,
    u === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading workflow definitions..." }) : null,
    u === "ready" && m.length === 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-empty", children: [
      "No ",
      i,
      " workflow definitions found."
    ] }) : null,
    u === "ready" && m.length > 0 ? /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ a.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ a.jsx(
            "input",
            {
              ref: _,
              type: "checkbox",
              checked: O,
              onChange: (H) => U(H.target.checked),
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
            "aria-selected": E.has(H.id),
            tabIndex: 0,
            onClick: () => n(H.id),
            onKeyDown: (Q) => {
              Q.currentTarget === Q.target && (Q.key !== "Enter" && Q.key !== " " || (Q.preventDefault(), n(H.id)));
            },
            children: [
              /* @__PURE__ */ a.jsx("label", { className: "wf-row-select", onClick: (Q) => Q.stopPropagation(), children: /* @__PURE__ */ a.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: E.has(H.id),
                  onChange: (Q) => L(H.id, Q.target.checked),
                  "aria-label": `Select workflow definition ${H.name}`
                }
              ) }),
              /* @__PURE__ */ a.jsxs("span", { children: [
                /* @__PURE__ */ a.jsx("strong", { children: H.name }),
                /* @__PURE__ */ a.jsx("small", { children: H.description || H.id })
              ] }),
              /* @__PURE__ */ a.jsx("span", { children: H.latestVersion ?? "No version" }),
              /* @__PURE__ */ a.jsx("span", { children: i === "deleted" ? Je(H.deletedAt) : H.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ a.jsx("span", { children: Je(H.lastModifiedAt) }),
              /* @__PURE__ */ a.jsx("span", { className: "wf-row-actions", onClick: (Q) => Q.stopPropagation(), children: i === "active" ? /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
                /* @__PURE__ */ a.jsx("button", { type: "button", onClick: (Q) => {
                  Q.stopPropagation(), n(H.id);
                }, children: "Open" }),
                /* @__PURE__ */ a.jsx("button", { type: "button", onClick: (Q) => {
                  Q.stopPropagation(), ne(H.id);
                }, children: "Artifacts" }),
                T ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => xt(t, T, H), children: [
                  /* @__PURE__ */ a.jsx(mt, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ a.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  J(H);
                }, children: [
                  /* @__PURE__ */ a.jsx(On, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
                /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
                  ee(H);
                }, children: [
                  /* @__PURE__ */ a.jsx(Bi, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ a.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  le(H);
                }, children: [
                  /* @__PURE__ */ a.jsx(On, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          H.id
        ))
      ] }),
      /* @__PURE__ */ a.jsx(
        z0,
        {
          page: c,
          pageSize: l,
          totalCount: g,
          onPageChange: d,
          onPageSizeChange: (H) => {
            f(H), d(1);
          }
        }
      )
    ] }) : null,
    j ? /* @__PURE__ */ a.jsx(
      b0,
      {
        draft: j,
        activities: C,
        catalogState: F,
        creating: $,
        suggestMetadataAction: P,
        onSuggestMetadata: P ? () => xt(t, P, { draft: j, activities: C }) : void 0,
        onChange: (H) => A(H),
        onClose: () => A(null),
        onSubmit: q
      }
    ) : null
  ] });
}
function b0({ draft: e, activities: t, catalogState: n, creating: o, suggestMetadataAction: r, onSuggestMetadata: i, onChange: s, onClose: c, onSubmit: d }) {
  const l = ge(() => L0(t), [t]), f = V0(e, t), u = (h) => {
    if (h.startsWith("kind:")) {
      s({ ...e, rootKind: h.slice(5), rootActivityVersionId: null });
      return;
    }
    const p = t.find((y) => y.activityVersionId === h);
    s({
      ...e,
      rootKind: pd(p) ?? e.rootKind,
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
            /* @__PURE__ */ a.jsx(mt, { size: 13 }),
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
              value: f,
              onChange: (h) => u(h.target.value),
              disabled: n === "loading",
              children: [
                /* @__PURE__ */ a.jsx("optgroup", { label: "Composite roots", children: l.compositeRoots.map((h) => /* @__PURE__ */ a.jsx("option", { value: h.value, children: h.label }, h.value)) }),
                l.otherCategories.map((h) => /* @__PURE__ */ a.jsx("optgroup", { label: h.name, children: h.activities.map((p) => /* @__PURE__ */ a.jsx("option", { value: p.activityVersionId, children: Ie(p) }, p.activityVersionId)) }, h.name))
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
function N0({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: o }) {
  const [r, i] = Y("active"), [s, c] = Y("loading"), [d, l] = Y(""), [f, u] = Y(""), [h, p] = Y([]), y = n?.trim().toLowerCase() ?? "", v = ge(
    () => y ? h.filter((D) => F0(D, y)) : h,
    [y, h]
  ), x = ge(
    () => Array.from(new Set(h.flatMap((D) => [
      D.definitionId,
      D.definitionVersionId,
      D.sourceId
    ]).filter((D) => !!D))).sort((D, C) => D.localeCompare(C)),
    [h]
  ), m = Lt(t, "weaver.workflows.explain-executable"), S = pe(async () => {
    c("loading"), l("");
    try {
      p(await Wl(e, r)), c("ready");
    } catch (D) {
      l(D instanceof Error ? D.message : String(D)), c("failed");
    }
  }, [e, r]);
  oe(() => {
    S();
  }, [S]);
  const g = async (D) => {
    u(""), l("");
    try {
      await Bl(e, D.artifactId), u(`Started ${D.artifactId}`);
    } catch (C) {
      l(C instanceof Error ? C.message : String(C));
    }
  }, w = async (D) => {
    if (window.confirm(`Delete executable artifact "${D.artifactId}"? You can restore it from the Deleted view.`)) {
      u(""), l("");
      try {
        await cx(e, D.artifactId), u(`Deleted ${D.artifactId}`), await S();
      } catch (C) {
        l(C instanceof Error ? C.message : String(C));
      }
    }
  }, E = async (D) => {
    u(""), l("");
    try {
      await lx(e, D.artifactId), u(`Restored ${D.artifactId}`), await S();
    } catch (C) {
      l(C instanceof Error ? C.message : String(C));
    }
  }, k = async (D) => {
    if (window.confirm(`Permanently delete executable artifact "${D.artifactId}"? This cannot be undone.`)) {
      u(""), l("");
      try {
        await dx(e, D.artifactId), u(`Permanently deleted ${D.artifactId}`), await S();
      } catch (C) {
        l(C instanceof Error ? C.message : String(C));
      }
    }
  }, j = (D) => {
    m && xt(t, m, D) && (l(""), u(`Sent ${D.artifactId} to Weaver`));
  }, A = (D) => {
    l(""), u(`Copied ${D}`);
  }, $ = (D) => {
    u(""), l(`Could not copy ${D}.`);
  };
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Executable state", children: [
        /* @__PURE__ */ a.jsx("button", { type: "button", className: r === "active" ? "active" : "", "aria-selected": r === "active", onClick: () => {
          i("active"), u("");
        }, children: "Active" }),
        /* @__PURE__ */ a.jsx("button", { type: "button", className: r === "deleted" ? "active" : "", "aria-selected": r === "deleted", onClick: () => {
          i("deleted"), u("");
        }, children: "Deleted" })
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", className: "wf-icon-button", "aria-label": "Refresh workflow executables", title: "Refresh", onClick: () => {
        S();
      }, children: /* @__PURE__ */ a.jsx(Fi, { size: 15 }) }),
      /* @__PURE__ */ a.jsxs("label", { className: "wf-search wf-executable-definition-filter", children: [
        /* @__PURE__ */ a.jsx(Hl, { size: 14 }),
        /* @__PURE__ */ a.jsx(
          "input",
          {
            "aria-label": "Filter executables by workflow definition",
            list: "wf-executable-definition-options",
            placeholder: "Filter by definition ID",
            value: n ?? "",
            onChange: (D) => o(D.currentTarget.value || null)
          }
        )
      ] }),
      /* @__PURE__ */ a.jsx("datalist", { id: "wf-executable-definition-options", children: x.map((D) => /* @__PURE__ */ a.jsx("option", { value: D }, D)) }),
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
    f ? /* @__PURE__ */ a.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ a.jsx(Vt, { size: 14 }),
      " ",
      f
    ] }) : null,
    s === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading workflow executables..." }) : null,
    s === "ready" && v.length === 0 ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: B0(r, !!n) }) : null,
    s === "ready" && v.length > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ a.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ a.jsx("span", { children: "Version" }),
        /* @__PURE__ */ a.jsx("span", { children: "Source" }),
        /* @__PURE__ */ a.jsx("span", { children: "Root" }),
        /* @__PURE__ */ a.jsx("span", { children: r === "deleted" ? "Deleted" : "Published" }),
        /* @__PURE__ */ a.jsx("span", { children: "Actions" })
      ] }),
      v.map((D) => /* @__PURE__ */ a.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ a.jsxs("span", { className: "wf-artifact-cell", children: [
          /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line", children: [
            /* @__PURE__ */ a.jsx("strong", { title: D.artifactId, children: D.artifactId }),
            /* @__PURE__ */ a.jsx(Kt, { value: D.artifactId, ariaLabel: `Copy artifact ID ${D.artifactId}`, copiedLabel: "artifact ID", onCopied: A, onCopyFailed: $ })
          ] }),
          /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
            /* @__PURE__ */ a.jsx("small", { title: D.artifactHash, children: D.artifactHash }),
            /* @__PURE__ */ a.jsx(Kt, { value: D.artifactHash, ariaLabel: `Copy artifact hash ${D.artifactHash}`, copiedLabel: "artifact hash", onCopied: A, onCopyFailed: $ })
          ] })
        ] }),
        /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line wf-version-cell", children: [
          /* @__PURE__ */ a.jsx("span", { children: D.artifactVersion }),
          /* @__PURE__ */ a.jsx(Kt, { value: D.artifactVersion, ariaLabel: `Copy artifact version ${D.artifactVersion}`, copiedLabel: "artifact version", onCopied: A, onCopyFailed: $ })
        ] }),
        /* @__PURE__ */ a.jsx(S0, { executable: D, onCopied: A, onCopyFailed: $ }),
        /* @__PURE__ */ a.jsx("span", { children: wd(D) }),
        /* @__PURE__ */ a.jsx("span", { children: Je(r === "deleted" ? D.deletedAt : D.publishedAt ?? D.createdAt) }),
        /* @__PURE__ */ a.jsx("span", { className: "wf-row-actions", children: r === "active" ? /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
          /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
            g(D);
          }, children: [
            /* @__PURE__ */ a.jsx(fr, { size: 13 }),
            " Run"
          ] }),
          m ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => j(D), children: [
            /* @__PURE__ */ a.jsx(mt, { size: 13 }),
            " Explain"
          ] }) : null,
          /* @__PURE__ */ a.jsxs("button", { type: "button", className: "danger", onClick: () => {
            w(D);
          }, children: [
            /* @__PURE__ */ a.jsx(On, { size: 13 }),
            " Delete"
          ] })
        ] }) : /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
          /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
            E(D);
          }, children: [
            /* @__PURE__ */ a.jsx(Bi, { size: 13 }),
            " Restore"
          ] }),
          /* @__PURE__ */ a.jsxs("button", { type: "button", className: "danger", onClick: () => {
            k(D);
          }, children: [
            /* @__PURE__ */ a.jsx(On, { size: 13 }),
            " Delete permanently"
          ] })
        ] }) })
      ] }, D.artifactId))
    ] }) : null
  ] });
}
function S0({ executable: e, onCopied: t, onCopyFailed: n }) {
  const o = e.sourceId || e.definitionVersionId || e.definitionId, r = e.sourceVersion;
  return /* @__PURE__ */ a.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ a.jsx("span", { className: "wf-source-kind", children: vd(e.sourceKind) }),
    o ? /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line", children: [
      /* @__PURE__ */ a.jsx("code", { title: o, children: o }),
      /* @__PURE__ */ a.jsx(Kt, { value: o, ariaLabel: `Copy source ID ${o}`, copiedLabel: "source ID", onCopied: t, onCopyFailed: n })
    ] }) : null,
    r ? /* @__PURE__ */ a.jsxs("small", { children: [
      "Version ",
      r
    ] }) : null
  ] });
}
function Kt({ value: e, ariaLabel: t, copiedLabel: n, onCopied: o, onCopyFailed: r }) {
  if (!e) return null;
  const i = async (s) => {
    s.preventDefault(), s.stopPropagation();
    try {
      await Y0(e), o(n);
    } catch {
      r(n);
    }
  };
  return /* @__PURE__ */ a.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (s) => {
    i(s);
  }, children: /* @__PURE__ */ a.jsx(Xm, { size: 12 }) });
}
function E0({ context: e, ai: t, definitionId: n, publishedArtifactId: o }) {
  const [r, i] = Y("loading"), [s, c] = Y(""), [d, l] = Y(""), [f, u] = Y([]), h = Lt(t, "weaver.workflows.explain-executable"), p = pe(async () => {
    i("loading"), c("");
    try {
      const g = await Wl(e);
      u(g.filter((w) => W0(w, n)).sort(X0)), i("ready");
    } catch (g) {
      c(g instanceof Error ? g.message : String(g)), u([]), i("failed");
    }
  }, [e, n]);
  oe(() => {
    p();
  }, [p, o]);
  const y = async (g) => {
    l(""), c("");
    try {
      await Bl(e, g.artifactId), l(`Started ${g.artifactId}`);
    } catch (w) {
      c(w instanceof Error ? w.message : String(w));
    }
  }, v = (g) => {
    h && xt(t, h, g) && (c(""), l(`Sent ${g.artifactId} to Weaver`));
  }, x = () => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(n)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, m = (g) => {
    c(""), l(`Copied ${g}`);
  }, S = (g) => {
    l(""), c(`Could not copy ${g}.`);
  };
  return /* @__PURE__ */ a.jsxs("div", { className: "wf-artifacts-panel", children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-artifacts-toolbar", children: [
      /* @__PURE__ */ a.jsxs("span", { children: [
        f.length,
        " artifact",
        f.length === 1 ? "" : "s"
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", className: "wf-icon-button", "aria-label": "Refresh workflow artifacts", title: "Refresh", onClick: () => {
        p();
      }, children: /* @__PURE__ */ a.jsx(Fi, { size: 13 }) }),
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: x, children: "Open list" })
    ] }),
    r === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ a.jsx(Be, { size: 14 }),
      " ",
      s
    ] }) : null,
    d ? /* @__PURE__ */ a.jsxs("div", { className: "wf-status-line compact", children: [
      /* @__PURE__ */ a.jsx(Vt, { size: 13 }),
      " ",
      d
    ] }) : null,
    r === "loading" ? /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "Loading artifacts..." }) : null,
    r === "ready" && f.length === 0 ? /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "No published artifacts for this workflow yet." }) : null,
    r === "ready" && f.length > 0 ? /* @__PURE__ */ a.jsx("div", { className: "wf-artifact-list", role: "list", "aria-label": "Workflow artifacts", children: f.map((g) => /* @__PURE__ */ a.jsxs("article", { className: "wf-artifact-card", role: "listitem", "data-active": g.artifactId === o ? "true" : void 0, children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-artifact-card-heading", children: [
        /* @__PURE__ */ a.jsxs("div", { children: [
          /* @__PURE__ */ a.jsxs("span", { className: "wf-artifact-version", children: [
            "Version ",
            g.artifactVersion
          ] }),
          g.artifactId === o ? /* @__PURE__ */ a.jsx("span", { className: "wf-chip", children: "Latest publish" }) : null
        ] }),
        /* @__PURE__ */ a.jsx("span", { children: Je(g.publishedAt ?? g.createdAt) })
      ] }),
      /* @__PURE__ */ a.jsxs("div", { className: "wf-artifact-card-values", children: [
        /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line", children: [
          /* @__PURE__ */ a.jsx("code", { title: g.artifactId, children: g.artifactId }),
          /* @__PURE__ */ a.jsx(Kt, { value: g.artifactId, ariaLabel: `Copy artifact ID ${g.artifactId}`, copiedLabel: "artifact ID", onCopied: m, onCopyFailed: S })
        ] }),
        /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ a.jsx("code", { title: g.artifactHash, children: g.artifactHash }),
          /* @__PURE__ */ a.jsx(Kt, { value: g.artifactHash, ariaLabel: `Copy artifact hash ${g.artifactHash}`, copiedLabel: "artifact hash", onCopied: m, onCopyFailed: S })
        ] })
      ] }),
      /* @__PURE__ */ a.jsxs("dl", { children: [
        /* @__PURE__ */ a.jsxs("div", { children: [
          /* @__PURE__ */ a.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ a.jsxs("dd", { children: [
            vd(g.sourceKind),
            " ",
            g.sourceVersion ? `v${g.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ a.jsxs("div", { children: [
          /* @__PURE__ */ a.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ a.jsx("dd", { children: wd(g) })
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
          /* @__PURE__ */ a.jsx(mt, { size: 13 }),
          " Explain"
        ] }) : null
      ] })
    ] }, g.artifactId)) }) : null
  ] });
}
function k0({ context: e, definitionId: t, currentRun: n, runs: o }) {
  const [r, i] = Y("loading"), [s, c] = Y(""), [d, l] = Y([]), f = j0(n ? [n, ...o] : o), u = n ? f.slice(1) : f, h = pe(async () => {
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
        f.length,
        " transient run",
        f.length === 1 ? "" : "s"
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", className: "wf-icon-button", "aria-label": "Refresh workflow test runs", title: "Refresh", onClick: () => {
        h();
      }, children: /* @__PURE__ */ a.jsx(Fi, { size: 13 }) }),
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => C0(t), children: "Open list" })
    ] }),
    n ? /* @__PURE__ */ a.jsx(Da, { testRun: n, current: !0 }) : /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "Run the current draft to inspect transient run details here." }),
    u.length > 0 ? /* @__PURE__ */ a.jsxs("section", { className: "wf-test-run-section", "aria-label": "Transient run session history", children: [
      /* @__PURE__ */ a.jsx("h4", { children: "Session history" }),
      /* @__PURE__ */ a.jsx("div", { className: "wf-test-run-list", children: u.map((p) => /* @__PURE__ */ a.jsx(Da, { testRun: p }, p.testRunId)) })
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
          onClick: () => Ki(p.workflowExecutionId),
          children: [
            /* @__PURE__ */ a.jsxs("span", { children: [
              /* @__PURE__ */ a.jsx("strong", { children: p.workflowExecutionId }),
              /* @__PURE__ */ a.jsx("small", { children: p.artifactId })
            ] }),
            /* @__PURE__ */ a.jsx(an, { status: p.status, subStatus: p.subStatus }),
            /* @__PURE__ */ a.jsxs("small", { children: [
              p.activityCount,
              " activities · ",
              p.incidentCount,
              " incidents"
            ] }),
            /* @__PURE__ */ a.jsx("small", { children: kd(p.startedAt ?? p.createdAt, p.completedAt ?? p.updatedAt) })
          ]
        }
      ) }, p.workflowExecutionId)) }) : null
    ] })
  ] });
}
function Da({ testRun: e, current: t = !1 }) {
  const n = Ji(e), o = e.workflowExecutionId;
  return /* @__PURE__ */ a.jsxs("article", { className: "wf-test-run-card", "data-state": n ? "rejected" : "accepted", "data-current": t ? "true" : void 0, children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-test-run-card-heading", children: [
      /* @__PURE__ */ a.jsxs("div", { children: [
        n ? /* @__PURE__ */ a.jsx(Be, { size: 15 }) : /* @__PURE__ */ a.jsx(Vt, { size: 15 }),
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
        /* @__PURE__ */ a.jsx("dd", { title: Je(e.expiresAt), children: Je(e.expiresAt) })
      ] }) : null
    ] }),
    o ? /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => Ki(o), children: "Open instance" }) : null
  ] });
}
function j0(e) {
  const t = /* @__PURE__ */ new Set();
  return e.filter((n) => t.has(n.testRunId) ? !1 : (t.add(n.testRunId), !0));
}
function Ki(e) {
  window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(e)}`), window.dispatchEvent(new PopStateEvent("popstate"));
}
function C0(e) {
  window.history.pushState({}, "", `/workflows/instances?definition=${encodeURIComponent(e)}`), window.dispatchEvent(new PopStateEvent("popstate"));
}
function I0({
  context: e,
  definitionFilter: t,
  onDefinitionFilterChange: n
}) {
  const [o, r] = Y("loading"), [i, s] = Y(""), [c, d] = Y(""), [l, f] = Y([]), u = t?.trim() || "", h = pe(async () => {
    r("loading"), s("");
    try {
      const p = await Xl(e, {
        definitionId: u || void 0,
        status: c || void 0,
        take: 100
      });
      f(p), r("ready");
    } catch (p) {
      s(p instanceof Error ? p.message : String(p)), f([]), r("failed");
    }
  }, [e, u, c]);
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
            value: u,
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
          onClick: () => Ki(p.workflowExecutionId),
          children: [
            /* @__PURE__ */ a.jsxs("span", { children: [
              /* @__PURE__ */ a.jsx("strong", { children: p.workflowExecutionId }),
              /* @__PURE__ */ a.jsx("small", { children: p.artifactId })
            ] }),
            /* @__PURE__ */ a.jsx("span", { children: /* @__PURE__ */ a.jsx(an, { status: p.status, subStatus: p.subStatus }) }),
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
            /* @__PURE__ */ a.jsx("span", { children: Je(p.startedAt ?? p.createdAt) }),
            /* @__PURE__ */ a.jsx("span", { children: kd(p.startedAt ?? p.createdAt, p.completedAt ?? p.updatedAt) })
          ]
        },
        p.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function _0({ context: e, ai: t, workflowExecutionId: n }) {
  const [o, r] = Y("loading"), [i, s] = Y(""), [c, d] = Y(null), [l, f] = Y(null), u = Lt(t, "weaver.workflows.explain-instance"), h = pe(async () => {
    if (!n) {
      s("No workflow execution id was provided."), r("failed");
      return;
    }
    r("loading"), s("");
    try {
      const y = await ux(e, n), [v, x] = await Promise.all([
        Qm(e, y.instance.definitionVersionId).then(
          (m) => ({ definitionVersion: m, error: "" }),
          (m) => ({ definitionVersion: null, error: m instanceof Error ? m.message : String(m) })
        ),
        Wi(e)
      ]);
      d({
        details: y,
        definitionVersion: v.definitionVersion,
        definitionVersionError: v.error,
        activityCatalog: x.activities
      }), f(null), r("ready");
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
        /* @__PURE__ */ a.jsx(Bi, { size: 14 }),
        " Refresh"
      ] }),
      c && u ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => xt(t, u, c.details), children: [
        /* @__PURE__ */ a.jsx(mt, { size: 13 }),
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
        A0,
        {
          definitionVersion: c.definitionVersion,
          definitionVersionError: c.definitionVersionError,
          activityCatalog: c.activityCatalog,
          details: c.details,
          selectedEvidenceId: l,
          onSelectEvidence: f
        }
      ),
      /* @__PURE__ */ a.jsx(
        M0,
        {
          ai: t,
          action: u,
          summary: c.details.instance,
          details: c.details,
          state: "ready",
          error: "",
          selectedEvidenceId: l,
          onSelectEvidence: f,
          graphNodeIds: c.definitionVersion ? T0(c.definitionVersion, c.activityCatalog) : void 0
        }
      )
    ] }) : null
  ] });
}
function A0({ definitionVersion: e, definitionVersionError: t, activityCatalog: n, details: o, selectedEvidenceId: r, onSelectEvidence: i }) {
  const s = ge(() => {
    if (!e) return { nodes: [], edges: [] };
    const c = e.state.rootActivity;
    if (!c) return { nodes: [], edges: [] };
    const d = n.find((p) => p.activityVersionId === c.activityVersionId), l = Xi(c, d), f = l === "unsupported" ? null : Mn(c, []), u = l === "unsupported" ? pi(c, n, e.layout) : f ? Ul(f, n, e.layout) : pi(c, n, e.layout), h = u.nodes.map((p) => ({
      ...p,
      draggable: !1,
      connectable: !1,
      deletable: !1
    }));
    return {
      nodes: gx(h, o.activities, o.incidents, r),
      edges: u.edges.map((p) => ({ ...p, deletable: !1 }))
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
      /* @__PURE__ */ a.jsx(an, { status: o.instance.status, subStatus: o.instance.subStatus })
    ] }),
    /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ a.jsxs("div", { className: "wf-empty", children: [
        "The workflow instance loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ a.jsx("small", { children: dw(t) }) : null
      ] }),
      e && s.nodes.length === 0 ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      s.nodes.length > 0 ? /* @__PURE__ */ a.jsxs(
        Il,
        {
          nodes: s.nodes,
          edges: s.edges,
          nodeTypes: ld,
          edgeTypes: dd,
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
function M0({ ai: e, action: t, summary: n, details: o, state: r, error: i, selectedEvidenceId: s = null, onSelectEvidence: c, graphNodeIds: d }) {
  return n ? /* @__PURE__ */ a.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Workflow instance details", children: [
    /* @__PURE__ */ a.jsxs("header", { children: [
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("span", { children: "Workflow instance" }),
        /* @__PURE__ */ a.jsx("h3", { children: n.workflowExecutionId })
      ] }),
      t ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => xt(e, t, o ?? n), children: [
        /* @__PURE__ */ a.jsx(mt, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    /* @__PURE__ */ a.jsxs("dl", { className: "wf-instance-meta", children: [
      /* @__PURE__ */ a.jsx("dt", { children: "Status" }),
      /* @__PURE__ */ a.jsx("dd", { children: /* @__PURE__ */ a.jsx(an, { status: n.status, subStatus: n.subStatus }) }),
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
      /* @__PURE__ */ a.jsx("dd", { children: Je(n.createdAt) }),
      /* @__PURE__ */ a.jsx("dt", { children: "Started" }),
      /* @__PURE__ */ a.jsx("dd", { children: Je(n.startedAt) }),
      /* @__PURE__ */ a.jsx("dt", { children: "Completed" }),
      /* @__PURE__ */ a.jsx("dd", { children: Je(n.completedAt) }),
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
      /* @__PURE__ */ a.jsx(D0, { activities: o.activities, selectedEvidenceId: s, onSelectEvidence: c }),
      /* @__PURE__ */ a.jsx(P0, { incidents: o.incidents, selectedEvidenceId: s, onSelectEvidence: c }),
      /* @__PURE__ */ a.jsx($0, { details: o, graphNodeIds: d })
    ] }) : null
  ] }) : /* @__PURE__ */ a.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Select a workflow instance to inspect activity history." }) });
}
function D0({ activities: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
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
          /* @__PURE__ */ a.jsx("span", { children: /* @__PURE__ */ a.jsx(an, { status: o.status, subStatus: o.subStatus }) }),
          /* @__PURE__ */ a.jsx("strong", { children: Gi(o.activityType) ?? o.activityType }),
          /* @__PURE__ */ a.jsx("small", { children: o.activityExecutionId }),
          /* @__PURE__ */ a.jsx("time", { children: Je(o.scheduledAt) })
        ]
      },
      o.activityExecutionId
    )) }) : null
  ] });
}
function P0({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
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
function $0({ details: e, graphNodeIds: t }) {
  if (!t) return null;
  const n = new Map(e.activities.map((i) => [i.activityExecutionId, i])), o = e.activities.filter((i) => !t.has(Pa(i))), r = e.incidents.filter((i) => {
    const s = i.activityExecutionId ? n.get(i.activityExecutionId) : null, c = i.executableNodeId ?? (s ? Pa(s) : "");
    return !c || !t.has(c);
  });
  return o.length === 0 && r.length === 0 ? null : /* @__PURE__ */ a.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ a.jsx("h4", { children: "Unmatched runtime evidence" }),
    /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-unmatched-list", children: [
      o.map((i) => /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ a.jsx("strong", { children: Gi(i.activityType) ?? i.activityType }),
        /* @__PURE__ */ a.jsx("small", { children: i.activityExecutionId })
      ] }, `activity-${i.activityExecutionId}`)),
      r.map((i) => /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ a.jsx("strong", { children: i.failureType }),
        /* @__PURE__ */ a.jsx("small", { children: i.incidentId })
      ] }, `incident-${i.incidentId}`))
    ] })
  ] });
}
function an({ status: e, subStatus: t }) {
  return /* @__PURE__ */ a.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
function T0(e, t) {
  const n = e.state.rootActivity;
  if (!n) return /* @__PURE__ */ new Set();
  const o = t.find((i) => i.activityVersionId === n.activityVersionId);
  if (Xi(n, o) === "unsupported") return /* @__PURE__ */ new Set([n.nodeId]);
  const r = Mn(n, []);
  return new Set(r?.slot.activities.map((i) => i.nodeId) ?? [n.nodeId]);
}
function Pa(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function z0({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const i = hd(n, t), s = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
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
      /* @__PURE__ */ a.jsx("select", { value: t, onChange: (d) => r(Number(d.target.value)), children: u0.map((d) => /* @__PURE__ */ a.jsx("option", { value: d, children: d }, d)) })
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
        /* @__PURE__ */ a.jsx(Xt, { size: 14 })
      ] })
    ] })
  ] });
}
function R0(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function hd(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Lt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function xt(e, t, n) {
  const o = t.createPrompt(n);
  return o ? (e.dispatchPrompt(o), !0) : !1;
}
function L0(e) {
  const t = Qo(e, "flowchart"), n = Qo(e, "sequence"), o = [
    { value: t?.activityVersionId ?? "kind:flowchart", label: "Flowchart" },
    { value: n?.activityVersionId ?? "kind:sequence", label: "Sequence" }
  ], r = /* @__PURE__ */ new Map();
  for (const s of e.filter(xd)) {
    if (O0(s)) continue;
    const c = s.category || "Uncategorized";
    r.set(c, [...r.get(c) ?? [], s]);
  }
  const i = Array.from(r.entries()).sort(([s], [c]) => s.localeCompare(c)).map(([s, c]) => ({
    name: s,
    activities: c.sort((d, l) => Ie(d).localeCompare(Ie(l)))
  }));
  return { compositeRoots: o, otherCategories: i };
}
function V0(e, t) {
  return e.rootActivityVersionId ?? Qo(t, e.rootKind)?.activityVersionId ?? `kind:${e.rootKind}`;
}
function H0(e, t) {
  return e.rootActivityVersionId ?? Qo(t, e.rootKind)?.activityVersionId ?? null;
}
function Qo(e, t) {
  return e.find((n) => pd(n) === t);
}
function pd(e) {
  return e ? yd(e) ? "flowchart" : md(e) ? "sequence" : null : null;
}
function gd(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "Uncategorized";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [o]) => n.localeCompare(o)).map(([n, o]) => ({
    category: n,
    activities: o.sort((r, i) => Ie(r).localeCompare(Ie(i)))
  }));
}
function O0(e) {
  return yd(e) || md(e);
}
function yd(e) {
  return Ie(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function md(e) {
  return Ie(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function xd(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function wd(e) {
  return q0(e.rootActivityType) || e.rootActivityType;
}
function F0(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function B0(e, t) {
  return t ? "No workflow executables match this definition filter." : e === "deleted" ? "No deleted workflow executables found." : "No workflow executables found. Publish a workflow definition to create one.";
}
function W0(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function X0(e, t) {
  return $a(t) - $a(e);
}
function $a(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function vd(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
async function Y0(e) {
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
function q0(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function U0(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    _o(t, n.typeName, n), _o(t, n.name, n), _o(t, n.displayName, n);
    const o = n.typeName.split(".").filter(Boolean).at(-1);
    _o(t, o, n);
  }
  return t;
}
function Z0(e, t, n) {
  const o = t.get(e.activityVersionId);
  return n.get(In(o?.activityTypeKey)) ?? n.get(In(Gi(o?.activityTypeKey))) ?? n.get(In(o?.displayName)) ?? n.get(In(e.activityVersionId)) ?? null;
}
function _o(e, t, n) {
  const o = In(t);
  o && !e.has(o) && e.set(o, n);
}
function In(e) {
  return e?.trim().toLowerCase() ?? "";
}
function Gi(e) {
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
function K0() {
  const e = gr();
  if (!e) return null;
  const t = e.getItem(ud);
  return t === "palette" || t === "inspector" ? t : null;
}
function gr() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function kn(e, t) {
  const n = gr();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function Ro(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function G0({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: o,
  expressionEditors: r,
  workflowDesignerPanels: i,
  onBack: s
}) {
  const [c, d] = Y(null), [l, f] = Y(null), [u, h] = Y([]), [p, y] = Y([]), [v, x] = Y(To), [m, S] = Y("loading"), [g, w] = Y([]), [E, k] = Y([]), [j, A] = Y([]), [$, D] = Y(null), [C, R] = Y(null), [F, N] = Y(null), [_, I] = Y(null), [P, T] = Y(""), [z, O] = Y(""), [W, B] = Y("idle"), [K, q] = Y(null), [ne, ae] = Y([]), [G, L] = Y(!1), [U, ie] = Y(null), [se, J] = Y(() => /* @__PURE__ */ new Set()), [ee, le] = Y(() => Ta(ja, h0, bn, Nn)), [H, Q] = Y(() => Ta(Ca, p0, Sn, En)), [fe, me] = Y(() => za(Ia, !1)), [Ee, Me] = Y(() => za(_a, !1)), [ye, We] = Y(K0), [rt, it] = Y("activities"), [ze, Re] = Y("inspector"), ke = ce(null), Xe = ce(null), cn = ce(""), vt = ce(0), Gn = ce(Promise.resolve()), ln = ce(/* @__PURE__ */ new Map()), Ht = ce(null), ut = ce(null), bt = ce(!1), Nt = l?.state.rootActivity ?? null, et = ge(() => new Map(u.map((b) => [b.activityVersionId, b])), [u]), Jn = ge(() => U0(p), [p]), Le = ge(() => ql(Nt, g), [Nt, g]), Qn = Xi(Le, Le ? et.get(Le.activityVersionId) : void 0), be = !!Le && Qn === "unsupported", Ye = ge(() => be ? null : Mn(Nt, g), [Nt, g, be]), dn = ge(() => gd(u), [u]), Ne = ge(() => be && Le?.nodeId === C ? Le : Ye?.slot.activities.find((b) => b.nodeId === C) ?? null, [be, Ye, Le, C]), St = ge(
    () => Ne ? Z0(Ne, et, Jn) : null,
    [et, Jn, Ne]
  ), un = Ne ? Ge(Ne) : [], Ae = Qn === "flowchart" && Ye?.slot.mode === "flowchart", Et = !Nt || !be, ft = W !== "idle", yr = !!l?.state.rootActivity && !ft, eo = Lt(n, "weaver.workflows.find-draft-risks"), to = Lt(n, "weaver.workflows.propose-update");
  oe(() => {
    if (!(!c || !l))
      return window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
        workflowId: c.definition.id,
        workflowDefinitionId: c.definition.id,
        workflowVersionId: l.sourceVersionId ?? null,
        draftId: l.id,
        revision: aw(l),
        selectedNodeId: C,
        selectedActivityType: St?.typeName ?? (Ne ? et.get(Ne.activityVersionId)?.activityTypeKey ?? Ne.activityVersionId : null),
        summary: c.definition.name,
        activities: Sd(l.state.rootActivity, et),
        diagnostics: l.validationErrors.map((b) => ({ severity: b.code ?? "warning", message: b.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === c.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [et, c, l, St, Ne, C]), oe(() => {
    const b = (V) => {
      const X = V.detail;
      if (!X?.batch || !X.respond) return;
      if (!l || !c) {
        X.respond({ ok: !1, message: "No active workflow draft is open." });
        return;
      }
      const Z = X.batch.workflowDefinitionId;
      if (Z && Z !== "active-draft" && Z !== c.definition.id) {
        X.respond({ ok: !1, message: `Batch targets workflow '${Z}', but '${c.definition.id}' is active.` });
        return;
      }
      try {
        const de = cw(l), re = e0(l, X.batch, u), he = `weaver-batch-${Date.now()}`;
        ln.current.set(he, de), f(re.draft), w([]), R(re.finalActivityIds.at(-1) ?? null), ie(null), q(null), O(re.summary), T(""), X.respond({ ok: !0, result: { ...re, undoToken: he } });
      } catch (de) {
        const re = de instanceof Error ? de.message : String(de);
        T(re), X.respond({ ok: !1, message: re });
      }
    }, M = (V) => {
      const X = V.detail;
      if (!X?.undoToken || !X.respond) return;
      const Z = ln.current.get(X.undoToken);
      if (!Z) {
        X.respond({ ok: !1, message: "The Weaver batch undo point is no longer available." });
        return;
      }
      ln.current.delete(X.undoToken), f(Z), w([]), R(null), ie(null), q(null), O("Restored workflow draft before Weaver batch."), T(""), X.respond({ ok: !0, summary: "Restored workflow draft before Weaver batch." });
    };
    return window.addEventListener(Na, b), window.addEventListener(Sa, M), () => {
      window.removeEventListener(Na, b), window.removeEventListener(Sa, M);
    };
  }, [u, c, l]), oe(() => {
    kn(ja, String(ee));
  }, [ee]), oe(() => {
    kn(Ca, String(H));
  }, [H]), oe(() => {
    kn(Ia, String(fe));
  }, [fe]), oe(() => {
    kn(_a, String(Ee));
  }, [Ee]), oe(() => {
    kn(ud, ye);
  }, [ye]), oe(() => {
    if (!ye) return;
    const b = (M) => {
      M.key === "Escape" && We(null);
    };
    return window.addEventListener("keydown", b), () => window.removeEventListener("keydown", b);
  }, [ye]);
  const fn = pe(async () => {
    T(""), S("loading");
    const [b, M, V, X] = await Promise.all([
      Jm(e, t),
      Wi(e),
      fx(e).then(
        (de) => ({ ok: !0, descriptors: de }),
        () => ({ ok: !1, descriptors: [] })
      ),
      hx(e).then(
        (de) => ({ ok: !0, descriptors: de }),
        () => ({ ok: !1, descriptors: To })
      )
    ]), Z = b.draft ?? null;
    d(b), cn.current = Z ? at(Z) : "", f(Z), h(M.activities ?? []), y(V.descriptors), x(X.descriptors.length > 0 ? X.descriptors : To), S(V.ok ? "ready" : "failed"), w([]), R(null);
  }, [e, t]);
  oe(() => {
    fn().catch((b) => T(b instanceof Error ? b.message : String(b)));
  }, [fn]), oe(() => {
    J((b) => {
      let M = !1;
      const V = new Set(b);
      for (const X of dn)
        V.has(X.category) || (V.add(X.category), M = !0);
      return M ? V : b;
    });
  }, [dn]), oe(() => {
    if (!Le) {
      k([]), A([]);
      return;
    }
    const b = be ? pi(Le, u, l?.layout ?? []) : Ye ? Ul(Ye, u, l?.layout ?? []) : { nodes: [], edges: [] };
    k(b.nodes), A(b.edges);
  }, [u, l?.layout, be, Ye, Le]);
  const mr = (b) => {
    f((M) => M && { ...M, state: { ...M.state, rootActivity: b } });
  }, hn = pe((b, M) => {
    if (l?.state.rootActivity && be)
      return;
    const V = yi(b, Va(b));
    if (!l?.state.rootActivity) {
      mr(V), R(V.nodeId);
      return;
    }
    if (!Ye) {
      if (!Ge(V)[0]) {
        O(""), T("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      f((Z) => {
        if (!Z?.state.rootActivity) return Z;
        const de = Z.state.rootActivity, re = gi(V, [], [de]), he = M ? [
          ...Z.layout.filter((je) => je.nodeId !== de.nodeId),
          {
            nodeId: de.nodeId,
            x: Math.round(M.x),
            y: Math.round(M.y)
          }
        ] : Z.layout;
        return {
          ...Z,
          layout: he,
          state: {
            ...Z.state,
            rootActivity: re
          }
        };
      }), R(l.state.rootActivity.nodeId), T(""), O(`Wrapped root in ${Ie(b)}`);
      return;
    }
    f((X) => {
      if (!X?.state.rootActivity) return X;
      const Z = Mn(X.state.rootActivity, g);
      if (!Z) return X;
      const de = gi(X.state.rootActivity, g, [...Z.slot.activities, V]), re = M ? [
        ...X.layout.filter((he) => he.nodeId !== V.nodeId),
        {
          nodeId: V.nodeId,
          x: Math.round(M.x),
          y: Math.round(M.y)
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
    }), R(V.nodeId);
  }, [l?.state.rootActivity, g, be, Ye]), Ot = pe((b, M) => {
    const V = yi(b, Va(b)), X = {
      id: V.nodeId,
      type: "workflowActivity",
      position: M,
      selected: !0,
      data: {
        label: Ie(b),
        activityVersionId: b.activityVersionId,
        activityTypeKey: b.activityTypeKey,
        category: b.category,
        executionType: b.executionType,
        icon: mi(b),
        childSlots: Ge(V),
        acceptsInbound: String(b.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: Jl(V, b)
      }
    };
    return { activityNode: V, node: X };
  }, []), Ve = pe((b, M, V = []) => {
    be || f((X) => {
      if (!X) return X;
      const Z = xx(X.layout, b), de = X.state.rootActivity;
      if (!de) return { ...X, layout: Z };
      const re = Mn(de, g);
      if (!re) return { ...X, layout: Z };
      const he = yx(re, b, M, V), je = re.slot.mode === "flowchart" ? mx(he, M) : he;
      return {
        ...X,
        layout: Z,
        state: {
          ...X.state,
          rootActivity: Zl(de, g, je)
        }
      };
    });
  }, [g, be]), pn = pe((b, M) => {
    if (!ke.current) return null;
    const V = ke.current.getBoundingClientRect();
    return $ ? $.screenToFlowPosition({ x: b, y: M }) : {
      x: b - V.left,
      y: M - V.top
    };
  }, [$]), gn = pe((b, M) => document.elementFromPoint(b, M)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), yn = pe((b, M, V) => {
    const X = E.find((De) => De.id === M.source), Z = E.find((De) => De.id === M.target), de = X && Z ? rw(X, Z) : X ? Ha(X) : V, re = Ot(b, de), je = [...E.map((De) => De.selected ? { ...De, selected: !1 } : De), re.node], jt = Ix(j, M, re.node.id);
    k(je), A(jt), R(re.node.id), Ve(je, jt, [re.activityNode]);
  }, [Ve, Ot, j, E]), kt = pe((b, M, V) => {
    if (!Et || !ke.current) return !1;
    const X = ke.current.getBoundingClientRect();
    if (!(M >= X.left && M <= X.right && V >= X.top && V <= X.bottom)) return !1;
    const de = pn(M, V);
    if (!de) return !1;
    if (Ae) {
      const re = gn(M, V), he = re ? j.find((je) => je.id === re) : void 0;
      if (he)
        return yn(b, he, de), !0;
    }
    return hn(b, de), !0;
  }, [hn, Et, j, gn, Ae, yn, pn]);
  oe(() => {
    const b = (V) => {
      const X = Ht.current;
      if (!X) return;
      Math.hypot(V.clientX - X.startX, V.clientY - X.startY) >= l0 && (X.dragging = !0);
    }, M = (V) => {
      const X = Ht.current;
      if (Ht.current = null, !X?.dragging || !ke.current || ut.current) return;
      const Z = ke.current.getBoundingClientRect();
      V.clientX >= Z.left && V.clientX <= Z.right && V.clientY >= Z.top && V.clientY <= Z.bottom && (bt.current = !0, window.setTimeout(() => {
        bt.current = !1;
      }, 0), kt(X.activity, V.clientX, V.clientY));
    };
    return window.addEventListener("pointermove", b), window.addEventListener("pointerup", M), window.addEventListener("pointercancel", M), () => {
      window.removeEventListener("pointermove", b), window.removeEventListener("pointerup", M), window.removeEventListener("pointercancel", M);
    };
  }, [$, kt]);
  const no = (b, M) => {
    ut.current = { activityVersionId: M.activityVersionId, handledDrop: !1 }, b.dataTransfer.setData(ka, M.activityVersionId), b.dataTransfer.setData("text/plain", M.activityVersionId), b.dataTransfer.effectAllowed = "copy";
  }, oo = (b, M) => {
    const V = ut.current;
    ut.current = null, !V?.handledDrop && (b.clientX === 0 && b.clientY === 0 || kt(M, b.clientX, b.clientY) && (bt.current = !0, window.setTimeout(() => {
      bt.current = !1;
    }, 0)));
  }, xr = (b, M) => {
    b.button === 0 && (Ht.current = {
      activity: M,
      startX: b.clientX,
      startY: b.clientY,
      dragging: !1
    });
  }, wr = (b) => {
    bt.current || Et && hn(b);
  }, ro = (b) => {
    if (!Et) {
      b.dataTransfer.dropEffect = "none";
      return;
    }
    if (b.preventDefault(), b.dataTransfer.dropEffect = "copy", !Ae) return;
    const M = gn(b.clientX, b.clientY);
    I(M);
  }, io = (b) => {
    if (!ke.current) return;
    const M = b.relatedTarget;
    M && ke.current.contains(M) || I(null);
  }, so = (b) => {
    b.preventDefault(), I(null);
    const M = b.dataTransfer.getData(ka) || b.dataTransfer.getData("text/plain");
    if (!M || (b.stopPropagation(), ut.current?.activityVersionId === M && (ut.current.handledDrop = !0), !Et)) return;
    const V = et.get(M);
    V && kt(V, b.clientX, b.clientY);
  }, vr = () => {
    if (!Ae) return;
    const b = ke.current?.getBoundingClientRect();
    b && N({
      kind: "fromEmpty",
      clientX: b.left + b.width / 2,
      clientY: b.top + b.height / 2
    });
  }, Ft = pe(async (b, M) => {
    const V = async () => {
      const Z = ++vt.current, de = at(b);
      T("");
      try {
        const re = await rx(e, b), he = at(re);
        return cn.current = he, f((je) => !je || je.id !== re.id ? je : at(je) === de ? re : { ...je, validationErrors: re.validationErrors }), Z === vt.current && O(M), re;
      } catch (re) {
        throw Z === vt.current && (O(""), T(re instanceof Error ? re.message : String(re))), re;
      }
    }, X = Gn.current.then(V, V);
    return Gn.current = X.catch(() => {
    }), X;
  }, [e]);
  oe(() => {
    if (!G || !l || at(l) === cn.current) return;
    O("Autosaving...");
    const M = window.setTimeout(() => {
      Ft(l, "Autosaved").catch(() => {
      });
    }, d0);
    return () => window.clearTimeout(M);
  }, [G, l, Ft]);
  const br = async () => {
    if (!(!l || ft)) {
      B("saving"), O("Saving...");
      try {
        await Ft(l, "Saved");
      } catch {
      } finally {
        B("idle");
      }
    }
  }, Nr = async () => {
    if (!(!l || ft)) {
      B("promoting"), O("Promoting...");
      try {
        const b = await ix(e, l.id), M = await sx(e, b.versionId);
        ie(M.artifactId), O(`Published ${M.artifactVersion}`), await fn();
      } catch (b) {
        O(""), T(b instanceof Error ? b.message : String(b));
      } finally {
        B("idle");
      }
    }
  }, Sr = async () => {
    if (!l?.state.rootActivity || ft) return;
    const b = l, M = at(b);
    q(null), O("Preparing test run...");
    try {
      B("testRunPreparing"), O("Preparing test run...");
      const V = lw(b);
      B("testRunStarting"), O("Starting test run...");
      const X = await ax(e, {
        definitionId: b.definitionId,
        snapshotId: V,
        state: b.state
      }), Z = { draftSignature: M, view: X };
      q(Z), ae((de) => [
        Z,
        ...de.filter((re) => re.view.testRunId !== X.testRunId)
      ].slice(0, 10)), O(Ji(X) ? "Test run rejected" : "Test run dispatched");
    } catch (V) {
      O(""), T(V instanceof Error ? V.message : String(V));
    } finally {
      B("idle");
    }
  }, Er = (b) => {
    const M = be ? b.filter((V) => V.type === "select") : b;
    M.length !== 0 && k((V) => el(M, V));
  }, kr = (b) => {
    be || A((M) => tl(b, M));
  }, mn = (b) => !b.source || !b.target || b.source === b.target || !Ae ? !1 : !b.targetHandle, jr = (b) => {
    if (!l?.state.rootActivity || !Ye || !Ae || !mn(b)) return;
    const M = Go(b.source, b.target, b.sourceHandle ?? "Done", b.targetHandle ?? void 0), V = ol(M, j);
    A(V), Ve(E, V);
  }, Cr = () => {
    Ve(E, j);
  }, ao = (b, M) => {
    if (!M.nodeId || M.handleType === "target") {
      Xe.current = null;
      return;
    }
    Xe.current = {
      nodeId: M.nodeId,
      handleId: M.handleId ?? null
    };
  }, Ir = (b, M) => {
    const V = sw(Xe.current, M);
    if (Xe.current = null, !V || !Ae || M.toNode || M.toHandle || iw(b)) return;
    const X = Nd(b);
    N({
      kind: "fromPort",
      sourceNodeId: V.nodeId,
      sourceHandleId: V.handleId,
      clientX: X.x,
      clientY: X.y
    });
  }, _r = (b, M) => {
    if (!Ae || !mn(M)) return;
    const V = qg(b, {
      ...M,
      sourceHandle: M.sourceHandle ?? "Done",
      targetHandle: M.targetHandle ?? void 0
    }, j, { shouldReplaceId: !1 });
    A(V), Ve(E, V);
  }, Ar = (b) => {
    if (be || b.length === 0) return;
    const M = new Set(b.map((Z) => Z.id)), V = E.filter((Z) => !M.has(Z.id)), X = j.filter((Z) => !M.has(Z.source) && !M.has(Z.target));
    k(V), A(X), C && M.has(C) && R(null), Ve(V, X);
  }, Mr = (b) => {
    if (be || b.length === 0) return;
    const M = new Set(b.map((X) => X.id)), V = j.filter((X) => !M.has(X.id));
    A(V), Ve(E, V);
  }, co = pe((b) => {
    if (be) return;
    const M = j.filter((V) => V.id !== b);
    A(M), Ve(E, M);
  }, [Ve, j, be, E]), lo = pe((b, M, V) => {
    Ae && N({ kind: "spliceEdge", edgeId: b, clientX: M, clientY: V });
  }, [Ae]), Dr = (b) => {
    const M = F;
    if (!M) return;
    N(null);
    const V = pn(M.clientX, M.clientY) ?? { x: 0, y: 0 };
    if (M.kind === "fromEmpty") {
      const Z = Ot(b, V), re = [...E.map((he) => he.selected ? { ...he, selected: !1 } : he), Z.node];
      k(re), R(Z.node.id), Ve(re, j, [Z.activityNode]);
      return;
    }
    if (M.kind === "fromPort") {
      const Z = E.find((De) => De.id === M.sourceNodeId), de = Z ? Ha(Z) : V, re = Ot(b, de), je = [...E.map((De) => De.selected ? { ...De, selected: !1 } : De), re.node], jt = [...j, Go(M.sourceNodeId, re.node.id, M.sourceHandleId ?? "Done")];
      k(je), A(jt), R(re.node.id), Ve(je, jt, [re.activityNode]);
      return;
    }
    const X = j.find((Z) => Z.id === M.edgeId);
    X && yn(b, X, V);
  }, Pr = ge(() => ({
    highlightedEdgeId: _,
    deleteEdge: co,
    requestInsertActivity: lo
  }), [co, _, lo]), uo = (b, M, V) => {
    w((X) => [...X, { ownerNodeId: b.nodeId, slotId: M, label: V }]), R(null);
  }, $r = pe((b) => {
    f((M) => {
      const V = M?.state.rootActivity;
      return !M || !V ? M : {
        ...M,
        state: {
          ...M.state,
          rootActivity: Kl(V, b.nodeId, () => b)
        }
      };
    });
  }, []), fo = (b) => {
    J((M) => {
      const V = new Set(M);
      return V.has(b) ? V.delete(b) : V.add(b), V;
    });
  }, ho = (b) => {
    We((M) => M === b ? null : M), b === "palette" ? me((M) => !M) : Me((M) => !M);
  }, po = (b) => {
    b === "palette" ? me(!1) : Me(!1), We((M) => M === b ? null : b);
  }, Bt = (b, M) => {
    We(null), b === "palette" ? (me(!1), le((V) => Ro(V + M, bn, Nn))) : (Me(!1), Q((V) => Ro(V + M, Sn, En)));
  }, go = (b, M) => {
    M.preventDefault(), We(null), b === "palette" ? me(!1) : Me(!1);
    const V = M.clientX, X = b === "palette" ? ee : H, Z = b === "palette" ? bn : Sn, de = b === "palette" ? Nn : En;
    document.body.classList.add("wf-side-panel-resizing");
    const re = (je) => {
      const jt = b === "palette" ? je.clientX - V : V - je.clientX, De = Ro(X + jt, Z, de);
      b === "palette" ? le(De) : Q(De);
    }, he = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", re), window.removeEventListener("pointerup", he), window.removeEventListener("pointercancel", he);
    };
    window.addEventListener("pointermove", re), window.addEventListener("pointerup", he), window.addEventListener("pointercancel", he);
  }, yo = (b, M) => {
    M.key === "ArrowLeft" ? (M.preventDefault(), Bt(b, b === "palette" ? -Io : Io)) : M.key === "ArrowRight" ? (M.preventDefault(), Bt(b, b === "palette" ? Io : -Io)) : M.key === "Home" ? (M.preventDefault(), b === "palette" ? le(bn) : Q(Sn)) : M.key === "End" && (M.preventDefault(), b === "palette" ? le(Nn) : Q(En));
  };
  if (!c || !l)
    return /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: P || "Loading workflow editor..." });
  const mo = [
    "wf-editor-body",
    fe ? "palette-collapsed" : "",
    Ee ? "inspector-collapsed" : "",
    ye === "palette" ? "palette-maximized" : "",
    ye === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), jd = {
    "--wf-palette-width": `${fe ? Aa : ee}px`,
    "--wf-inspector-width": `${Ee ? Aa : H}px`
  }, Qi = !fe && ye !== "inspector", es = !Ee && ye !== "palette", xo = K?.draftSignature === at(l) ? K.view : null, Cd = ne.filter((b) => b.draftSignature === at(l)).map((b) => b.view), ts = xo && z.startsWith("Test run") ? "" : z, Id = {
    definition: c.definition,
    draft: l,
    selectedActivity: Ne,
    selectedActivityDescriptor: St,
    selectedActivitySlots: un,
    catalog: u,
    currentScopeOwner: Le,
    frames: g
  }, ns = i.map((b) => {
    const M = b.component;
    return {
      id: b.id,
      title: b.title,
      side: b.side,
      order: b.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ a.jsx(M, { context: Id })
    };
  }), Tr = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ a.jsx(zl, { size: 15 }),
      render: _d
    },
    ...ns.filter((b) => b.side === "left")
  ].sort(La), zr = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ a.jsx(Vl, { size: 15 }),
      render: Ad
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ a.jsx(qm, { size: 15 }),
      render: () => /* @__PURE__ */ a.jsx(
        E0,
        {
          context: e,
          ai: n,
          definitionId: c.definition.id,
          publishedArtifactId: U
        }
      )
    },
    {
      id: "test-runs",
      title: "Test runs",
      order: 20,
      icon: /* @__PURE__ */ a.jsx(Ol, { size: 15 }),
      render: () => /* @__PURE__ */ a.jsx(
        k0,
        {
          context: e,
          definitionId: c.definition.id,
          currentRun: xo,
          runs: Cd
        }
      )
    },
    ...ns.filter((b) => b.side === "right")
  ].sort(La), os = Tr.find((b) => b.id === rt) ?? Tr[0], rs = zr.find((b) => b.id === ze) ?? zr[0];
  function _d() {
    return /* @__PURE__ */ a.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: dn.map((b) => {
      const M = se.has(b.category);
      return /* @__PURE__ */ a.jsxs("div", { className: "wf-palette-category", children: [
        /* @__PURE__ */ a.jsxs(
          "button",
          {
            type: "button",
            className: "wf-palette-category-toggle",
            role: "treeitem",
            "aria-expanded": M,
            onClick: () => fo(b.category),
            children: [
              M ? /* @__PURE__ */ a.jsx(Rl, { size: 14 }) : /* @__PURE__ */ a.jsx(Xt, { size: 14 }),
              /* @__PURE__ */ a.jsx("span", { children: b.category }),
              /* @__PURE__ */ a.jsx("small", { children: b.activities.length })
            ]
          }
        ),
        M ? /* @__PURE__ */ a.jsx("div", { className: "wf-palette-activities", role: "group", children: b.activities.map((V) => {
          const X = V.description?.trim(), Z = X ? `wf-palette-description-${V.activityVersionId}` : void 0, de = Ie(V), re = mi(V);
          return /* @__PURE__ */ a.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-activity",
              role: "treeitem",
              draggable: !0,
              title: X || Ie(V),
              "aria-describedby": Z,
              onClick: () => wr(V),
              onDragStart: (he) => no(he, V),
              onDragEnd: (he) => oo(he, V),
              onPointerDown: (he) => xr(he, V),
              children: [
                /* @__PURE__ */ a.jsx("span", { className: "wf-activity-icon", "data-icon": re, "aria-hidden": "true", children: bd(re) }),
                /* @__PURE__ */ a.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ a.jsx("strong", { children: de }),
                  X ? /* @__PURE__ */ a.jsx("small", { id: Z, children: X }) : null
                ] }),
                /* @__PURE__ */ a.jsx(Ym, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            V.activityVersionId
          );
        }) }) : null
      ] }, b.category);
    }) });
  }
  function Ad() {
    return Ne ? /* @__PURE__ */ a.jsxs("div", { className: "wf-inspector-content", children: [
      /* @__PURE__ */ a.jsx("h3", { children: E.find((b) => b.id === Ne.nodeId)?.data.label ?? Ne.nodeId }),
      /* @__PURE__ */ a.jsxs("dl", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Node ID" }),
        /* @__PURE__ */ a.jsx("dd", { children: Ne.nodeId }),
        /* @__PURE__ */ a.jsx("dt", { children: "Activity type" }),
        /* @__PURE__ */ a.jsx("dd", { children: St?.typeName ?? et.get(Ne.activityVersionId)?.activityTypeKey ?? "Unknown" }),
        /* @__PURE__ */ a.jsx("dt", { children: "Activity version" }),
        /* @__PURE__ */ a.jsx("dd", { children: Ne.activityVersionId })
      ] }),
      /* @__PURE__ */ a.jsx(
        Wx,
        {
          activity: Ne,
          descriptor: St,
          editors: o,
          expressionEditors: r,
          expressionDescriptors: v,
          descriptorStatus: m,
          onChange: $r
        }
      ),
      un.length > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-slot-list", children: [
        /* @__PURE__ */ a.jsx("span", { children: "Embedded slots" }),
        un.map((b) => /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => uo(Ne, b.id, `${E.find((M) => M.id === Ne.nodeId)?.data.label ?? Ne.nodeId} / ${b.label}`), children: [
          b.label,
          /* @__PURE__ */ a.jsxs("small", { children: [
            b.activities.length,
            " activit",
            b.activities.length === 1 ? "y" : "ies"
          ] })
        ] }, b.id))
      ] }) : /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
    ] }) : /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
  }
  return /* @__PURE__ */ a.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ a.jsx("button", { type: "button", className: "wf-link-button", onClick: s, children: "Definitions" }),
      /* @__PURE__ */ a.jsx(Xt, { size: 14 }),
      /* @__PURE__ */ a.jsx("strong", { children: c.definition.name }),
      /* @__PURE__ */ a.jsx("span", { className: "wf-chip", children: "Draft" }),
      ts ? /* @__PURE__ */ a.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ a.jsx(Vt, { size: 13 }),
        " ",
        ts
      ] }) : null,
      /* @__PURE__ */ a.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ a.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ a.jsx("input", { className: "wf-autosave-switch-input", type: "checkbox", checked: G, onChange: (b) => L(b.target.checked) }),
          /* @__PURE__ */ a.jsx("span", { children: "Autosave" })
        ] }),
        eo ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => xt(n, eo, { definition: c.definition, draft: l }), children: [
          /* @__PURE__ */ a.jsx(mt, { size: 15 }),
          " Risks"
        ] }) : null,
        to ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => xt(n, to, { definition: c.definition, draft: l }), children: [
          /* @__PURE__ */ a.jsx(mt, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ a.jsxs("button", { type: "button", disabled: ft, onClick: () => {
          br();
        }, children: [
          /* @__PURE__ */ a.jsx(Um, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ a.jsxs("button", { type: "button", disabled: ft, onClick: () => {
          Nr();
        }, children: [
          /* @__PURE__ */ a.jsx(Ll, { size: 15 }),
          " Publish"
        ] }),
        xo ? /* @__PURE__ */ a.jsx(
          ow,
          {
            testRun: xo,
            onOpen: () => {
              Me(!1), We((b) => b === "palette" ? null : b), Re("test-runs");
            }
          }
        ) : null,
        /* @__PURE__ */ a.jsxs(
          "button",
          {
            type: "button",
            disabled: !yr,
            title: l.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              Sr();
            },
            children: [
              /* @__PURE__ */ a.jsx(fr, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    P ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(Be, { size: 16 }),
      " ",
      P
    ] }) : null,
    /* @__PURE__ */ a.jsxs("div", { className: mo, style: jd, children: [
      /* @__PURE__ */ a.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ a.jsx(
            Ra,
            {
              label: "Activities panel tabs",
              tabs: Tr,
              activeTabId: os.id,
              onSelect: it
            }
          ),
          /* @__PURE__ */ a.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ a.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": fe ? "Expand activities panel" : "Collapse activities panel",
                title: fe ? "Expand" : "Collapse",
                onClick: () => ho("palette"),
                children: fe ? /* @__PURE__ */ a.jsx(Xt, { size: 14 }) : /* @__PURE__ */ a.jsx(Zo, { size: 14 })
              }
            ),
            fe ? null : /* @__PURE__ */ a.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": ye === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: ye === "palette" ? "Restore" : "Maximize",
                onClick: () => po("palette"),
                children: ye === "palette" ? /* @__PURE__ */ a.jsx(ga, { size: 14 }) : /* @__PURE__ */ a.jsx(Ko, { size: 14 })
              }
            )
          ] })
        ] }),
        Qi ? os.render() : null
      ] }),
      Qi && !ye ? /* @__PURE__ */ a.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": bn,
          "aria-valuemax": Nn,
          "aria-valuenow": ee,
          tabIndex: 0,
          onPointerDown: (b) => go("palette", b),
          onKeyDown: (b) => yo("palette", b)
        }
      ) : /* @__PURE__ */ a.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ a.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-breadcrumb", children: [
          /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
            w([]), R(null);
          }, children: "Root" }),
          g.map((b, M) => /* @__PURE__ */ a.jsxs(wt.Fragment, { children: [
            /* @__PURE__ */ a.jsx(Xt, { size: 13 }),
            /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
              w(g.slice(0, M + 1)), R(null);
            }, children: b.label })
          ] }, `${b.ownerNodeId}-${b.slotId}-${M}`))
        ] }),
        /* @__PURE__ */ a.jsxs("div", { className: "wf-canvas", ref: ke, onDragOver: ro, onDragLeave: io, onDrop: so, children: [
          /* @__PURE__ */ a.jsx(fd.Provider, { value: Pr, children: /* @__PURE__ */ a.jsxs(
            Il,
            {
              nodes: E,
              edges: j,
              nodeTypes: ld,
              edgeTypes: dd,
              onInit: D,
              onNodesChange: Er,
              onEdgesChange: kr,
              onNodesDelete: Ar,
              onEdgesDelete: Mr,
              onConnect: jr,
              onConnectStart: Ae ? ao : void 0,
              onConnectEnd: Ae ? Ir : void 0,
              onReconnect: Ae ? _r : void 0,
              isValidConnection: mn,
              onDragOver: ro,
              onDragLeave: io,
              onDrop: so,
              onPaneClick: () => R(null),
              onNodeClick: (b, M) => {
                R(M.id), Re("inspector");
              },
              onNodeDragStop: be ? void 0 : Cr,
              fitView: !0,
              minZoom: 0.2,
              maxZoom: 1.8,
              nodesConnectable: Ae,
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
          Ae && E.length === 0 ? /* @__PURE__ */ a.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => vr(), children: [
            /* @__PURE__ */ a.jsx(Oi, { size: 15 }),
            " Add activity"
          ] }) : null,
          F ? /* @__PURE__ */ a.jsx(
            tw,
            {
              clientX: F.clientX,
              clientY: F.clientY,
              activities: u,
              onPick: Dr,
              onClose: () => N(null)
            }
          ) : null
        ] }),
        /* @__PURE__ */ a.jsx(nw, { draft: l })
      ] }),
      es && !ye ? /* @__PURE__ */ a.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Sn,
          "aria-valuemax": En,
          "aria-valuenow": H,
          tabIndex: 0,
          onPointerDown: (b) => go("inspector", b),
          onKeyDown: (b) => yo("inspector", b)
        }
      ) : /* @__PURE__ */ a.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ a.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ a.jsx(
            Ra,
            {
              label: "Inspector panel tabs",
              tabs: zr,
              activeTabId: rs.id,
              onSelect: Re
            }
          ),
          /* @__PURE__ */ a.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ a.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": Ee ? "Expand inspector panel" : "Collapse inspector panel",
                title: Ee ? "Expand" : "Collapse",
                onClick: () => ho("inspector"),
                children: Ee ? /* @__PURE__ */ a.jsx(Zo, { size: 14 }) : /* @__PURE__ */ a.jsx(Xt, { size: 14 })
              }
            ),
            Ee ? null : /* @__PURE__ */ a.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": ye === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: ye === "inspector" ? "Restore" : "Maximize",
                onClick: () => po("inspector"),
                children: ye === "inspector" ? /* @__PURE__ */ a.jsx(ga, { size: 14 }) : /* @__PURE__ */ a.jsx(Ko, { size: 14 })
              }
            )
          ] })
        ] }),
        es ? rs.render() : null
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
function J0({ data: e, selected: t }) {
  const n = e, o = n.runtime, r = !n.suppressFlowPorts, i = r ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], s = Q0(n);
  return /* @__PURE__ */ a.jsxs(
    "div",
    {
      className: ["wf-node", t ? "selected" : "", o ? "wf-node-runtime" : "", o?.hasBlockingIncident ? "faulted" : ""].filter(Boolean).join(" "),
      "data-icon": n.icon ?? "activity",
      children: [
        r && n.acceptsInbound ? /* @__PURE__ */ a.jsx(rn, { type: "target", position: te.Left }) : null,
        /* @__PURE__ */ a.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ a.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: bd(n.icon) }),
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
          o.status ? /* @__PURE__ */ a.jsx(an, { status: o.status, subStatus: o.subStatus }) : null,
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
          return /* @__PURE__ */ a.jsxs(wt.Fragment, { children: [
            /* @__PURE__ */ a.jsx("span", { className: "wf-node-port-label", style: { top: l }, children: c.displayName }),
            /* @__PURE__ */ a.jsx(rn, { type: "source", position: te.Right, id: c.name, style: { top: l } })
          ] }, c.name);
        })
      ]
    }
  );
}
function Q0(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((r) => !!r).join(" · ");
}
function bd(e) {
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
      return /* @__PURE__ */ a.jsx(Zm, { size: 15 });
    default:
      return /* @__PURE__ */ a.jsx(zl, { size: 15 });
  }
}
function ew(e) {
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
    label: f,
    labelStyle: u
  } = e, h = wt.useContext(fd), [p, y] = Y(!1), [v, x, m] = qo({ sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: c }), S = h?.highlightedEdgeId === t;
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsx(
      Zn,
      {
        id: t,
        path: v,
        markerEnd: d,
        style: {
          ...l,
          strokeWidth: S ? 2.5 : l?.strokeWidth
        },
        label: f,
        labelX: x,
        labelY: m,
        labelStyle: u,
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1)
      }
    ),
    h ? /* @__PURE__ */ a.jsx(ym, { children: /* @__PURE__ */ a.jsxs(
      "div",
      {
        className: ["wf-edge-actions", p ? "visible" : "", S ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${x}px, ${m}px)` },
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1),
        children: [
          /* @__PURE__ */ a.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (g) => h.requestInsertActivity(t, g.clientX, g.clientY), children: /* @__PURE__ */ a.jsx(Oi, { size: 12 }) }),
          /* @__PURE__ */ a.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => h.deleteEdge(t), children: /* @__PURE__ */ a.jsx(On, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function tw({ clientX: e, clientY: t, activities: n, onPick: o, onClose: r }) {
  const [i, s] = Y(""), [c, d] = Y(0), l = ce(null), f = ce(null), u = ge(() => {
    const S = i.trim().toLowerCase(), g = n.filter(xd);
    return S ? g.filter((w) => Ie(w).toLowerCase().includes(S) || w.activityTypeKey.toLowerCase().includes(S) || (w.category ?? "").toLowerCase().includes(S) || (w.description ?? "").toLowerCase().includes(S)) : g;
  }, [n, i]), h = ge(() => gd(u), [u]), p = ge(() => h.flatMap((S) => S.activities), [h]);
  oe(() => {
    requestAnimationFrame(() => f.current?.focus());
  }, []), oe(() => {
    const S = (w) => {
      l.current?.contains(w.target) || r();
    }, g = (w) => {
      w.key === "Escape" && r();
    };
    return document.addEventListener("mousedown", S, !0), document.addEventListener("keydown", g), () => {
      document.removeEventListener("mousedown", S, !0), document.removeEventListener("keydown", g);
    };
  }, [r]);
  const y = (S) => {
    if (S.key === "ArrowDown")
      S.preventDefault(), d((g) => Math.min(g + 1, p.length - 1));
    else if (S.key === "ArrowUp")
      S.preventDefault(), d((g) => Math.max(g - 1, 0));
    else if (S.key === "Enter") {
      S.preventDefault();
      const g = p[c];
      g && o(g);
    }
  }, v = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), x = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let m = -1;
  return /* @__PURE__ */ a.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: v, top: x }, onMouseDown: (S) => S.stopPropagation(), onClick: (S) => S.stopPropagation(), children: [
    /* @__PURE__ */ a.jsx(
      "input",
      {
        ref: f,
        type: "search",
        value: i,
        placeholder: "Search activities...",
        "aria-label": "Search activities",
        onChange: (S) => {
          s(S.target.value), d(0);
        },
        onKeyDown: y
      }
    ),
    /* @__PURE__ */ a.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: h.length === 0 ? /* @__PURE__ */ a.jsx("p", { children: "No matching activities." }) : h.map((S) => /* @__PURE__ */ a.jsxs("section", { children: [
      /* @__PURE__ */ a.jsx("h4", { children: S.category }),
      S.activities.map((g) => {
        m += 1;
        const w = m, E = w === c;
        return /* @__PURE__ */ a.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": E,
            className: E ? "active" : "",
            onMouseEnter: () => d(w),
            onClick: () => o(g),
            children: [
              /* @__PURE__ */ a.jsx("strong", { children: Ie(g) }),
              /* @__PURE__ */ a.jsx("small", { children: g.category || g.activityTypeKey })
            ]
          },
          g.activityVersionId
        );
      })
    ] }, S.category)) })
  ] });
}
function nw({ draft: e }) {
  return e.validationErrors.length ? /* @__PURE__ */ a.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ a.jsx(Be, { size: 14 }),
    e.validationErrors.length,
    " validation issue",
    e.validationErrors.length === 1 ? "" : "s"
  ] }) : /* @__PURE__ */ a.jsxs("div", { className: "wf-validation ok", children: [
    /* @__PURE__ */ a.jsx(Vt, { size: 14 }),
    " No validation errors"
  ] });
}
function ow({ testRun: e, onOpen: t }) {
  const n = Ji(e);
  return /* @__PURE__ */ a.jsx("div", { className: "wf-test-run-status", "data-state": n ? "rejected" : "accepted", children: /* @__PURE__ */ a.jsxs(
    "button",
    {
      type: "button",
      className: "wf-test-run-trigger",
      "aria-controls": "wf-test-runs-panel",
      onClick: t,
      children: [
        n ? /* @__PURE__ */ a.jsx(Be, { size: 16 }) : /* @__PURE__ */ a.jsx(Vt, { size: 16 }),
        n ? "Test run rejected" : "Test run dispatched",
        /* @__PURE__ */ a.jsx(Rl, { size: 14 })
      ]
    }
  ) });
}
function Va(e) {
  return `${Ie(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function Ha(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function rw(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function Nd(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function iw(e) {
  const t = Nd(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function sw(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function at(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function aw(e) {
  return Ed(at(e));
}
function Sd(e, t, n = []) {
  if (!e) return n;
  const o = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: o?.activityTypeKey ?? e.activityVersionId,
    displayName: o ? Ie(o) : void 0
  });
  for (const r of Ge(e))
    for (const i of r.activities) Sd(i, t, n);
  return n;
}
function cw(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function lw(e) {
  return `${e.id}-${Ed(JSON.stringify(e.state))}`;
}
function Ed(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function Ji(e) {
  return e.status.toLowerCase() === "rejected";
}
function Je(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function dw(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function kd(e, t) {
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
  iw as isConnectEndOverExistingWorkflowNode,
  hw as register,
  sw as resolveConnectEndSource
};
