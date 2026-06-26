import wt, { memo as Ce, forwardRef as Qo, useRef as ce, useEffect as ne, useCallback as ye, useContext as Bn, useMemo as me, useState as Y, createContext as vi, useLayoutEffect as Id, createElement as ei, useId as Ha } from "react";
import "@tanstack/react-query";
function Cd(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var Rr = { exports: {} }, wn = {};
var ts;
function jd() {
  if (ts) return wn;
  ts = 1;
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
var ns;
function _d() {
  return ns || (ns = 1, Rr.exports = jd()), Rr.exports;
}
var a = _d();
function Ae(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, o; n < e.length; n++)
      (o = Ae(e[n])) !== "" && (t += (t && " ") + o);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var Ad = { value: () => {
} };
function er() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new Ao(n);
}
function Ao(e) {
  this._ = e;
}
function Md(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
Ao.prototype = er.prototype = {
  constructor: Ao,
  on: function(e, t) {
    var n = this._, o = Md(e + "", n), r, i = -1, s = o.length;
    if (arguments.length < 2) {
      for (; ++i < s; ) if ((r = (e = o[i]).type) && (r = Dd(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++i < s; )
      if (r = (e = o[i]).type) n[r] = os(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = os(n[r], e.name, null);
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
function Dd(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function os(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = Ad, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var ti = "http://www.w3.org/1999/xhtml";
const rs = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: ti,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function tr(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), rs.hasOwnProperty(t) ? { space: rs[t], local: e } : e;
}
function Pd(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === ti && t.documentElement.namespaceURI === ti ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function $d(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function Oa(e) {
  var t = tr(e);
  return (t.local ? $d : Pd)(t);
}
function Td() {
}
function bi(e) {
  return e == null ? Td : function() {
    return this.querySelector(e);
  };
}
function zd(e) {
  typeof e != "function" && (e = bi(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = new Array(s), d, l, f = 0; f < s; ++f)
      (d = i[f]) && (l = e.call(d, d.__data__, f, i)) && ("__data__" in d && (l.__data__ = d.__data__), c[f] = l);
  return new We(o, this._parents);
}
function Rd(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function Ld() {
  return [];
}
function Fa(e) {
  return e == null ? Ld : function() {
    return this.querySelectorAll(e);
  };
}
function Vd(e) {
  return function() {
    return Rd(e.apply(this, arguments));
  };
}
function Hd(e) {
  typeof e == "function" ? e = Vd(e) : e = Fa(e);
  for (var t = this._groups, n = t.length, o = [], r = [], i = 0; i < n; ++i)
    for (var s = t[i], c = s.length, d, l = 0; l < c; ++l)
      (d = s[l]) && (o.push(e.call(d, d.__data__, l, s)), r.push(d));
  return new We(o, r);
}
function Ba(e) {
  return function() {
    return this.matches(e);
  };
}
function Wa(e) {
  return function(t) {
    return t.matches(e);
  };
}
var Od = Array.prototype.find;
function Fd(e) {
  return function() {
    return Od.call(this.children, e);
  };
}
function Bd() {
  return this.firstElementChild;
}
function Wd(e) {
  return this.select(e == null ? Bd : Fd(typeof e == "function" ? e : Wa(e)));
}
var Xd = Array.prototype.filter;
function Yd() {
  return Array.from(this.children);
}
function qd(e) {
  return function() {
    return Xd.call(this.children, e);
  };
}
function Zd(e) {
  return this.selectAll(e == null ? Yd : qd(typeof e == "function" ? e : Wa(e)));
}
function Ud(e) {
  typeof e != "function" && (e = Ba(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = [], d, l = 0; l < s; ++l)
      (d = i[l]) && e.call(d, d.__data__, l, i) && c.push(d);
  return new We(o, this._parents);
}
function Xa(e) {
  return new Array(e.length);
}
function Kd() {
  return new We(this._enter || this._groups.map(Xa), this._parents);
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
function Gd(e) {
  return function() {
    return e;
  };
}
function Jd(e, t, n, o, r, i) {
  for (var s = 0, c, d = t.length, l = i.length; s < l; ++s)
    (c = t[s]) ? (c.__data__ = i[s], o[s] = c) : n[s] = new Lo(e, i[s]);
  for (; s < d; ++s)
    (c = t[s]) && (r[s] = c);
}
function Qd(e, t, n, o, r, i, s) {
  var c, d, l = /* @__PURE__ */ new Map(), f = t.length, u = i.length, h = new Array(f), g;
  for (c = 0; c < f; ++c)
    (d = t[c]) && (h[c] = g = s.call(d, d.__data__, c, t) + "", l.has(g) ? r[c] = d : l.set(g, d));
  for (c = 0; c < u; ++c)
    g = s.call(e, i[c], c, i) + "", (d = l.get(g)) ? (o[c] = d, d.__data__ = i[c], l.delete(g)) : n[c] = new Lo(e, i[c]);
  for (c = 0; c < f; ++c)
    (d = t[c]) && l.get(h[c]) === d && (r[c] = d);
}
function eu(e) {
  return e.__data__;
}
function tu(e, t) {
  if (!arguments.length) return Array.from(this, eu);
  var n = t ? Qd : Jd, o = this._parents, r = this._groups;
  typeof e != "function" && (e = Gd(e));
  for (var i = r.length, s = new Array(i), c = new Array(i), d = new Array(i), l = 0; l < i; ++l) {
    var f = o[l], u = r[l], h = u.length, g = nu(e.call(f, f && f.__data__, l, o)), y = g.length, v = c[l] = new Array(y), w = s[l] = new Array(y), m = d[l] = new Array(h);
    n(f, u, v, w, m, g, t);
    for (var b = 0, p = 0, x, I; b < y; ++b)
      if (x = v[b]) {
        for (b >= p && (p = b + 1); !(I = w[p]) && ++p < y; ) ;
        x._next = I || null;
      }
  }
  return s = new We(s, o), s._enter = c, s._exit = d, s;
}
function nu(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function ou() {
  return new We(this._exit || this._groups.map(Xa), this._parents);
}
function ru(e, t, n) {
  var o = this.enter(), r = this, i = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? i.remove() : n(i), o && r ? o.merge(r).order() : r;
}
function iu(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, i = o.length, s = Math.min(r, i), c = new Array(r), d = 0; d < s; ++d)
    for (var l = n[d], f = o[d], u = l.length, h = c[d] = new Array(u), g, y = 0; y < u; ++y)
      (g = l[y] || f[y]) && (h[y] = g);
  for (; d < r; ++d)
    c[d] = n[d];
  return new We(c, this._parents);
}
function su() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, i = o[r], s; --r >= 0; )
      (s = o[r]) && (i && s.compareDocumentPosition(i) ^ 4 && i.parentNode.insertBefore(s, i), i = s);
  return this;
}
function au(e) {
  e || (e = cu);
  function t(u, h) {
    return u && h ? e(u.__data__, h.__data__) : !u - !h;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), i = 0; i < o; ++i) {
    for (var s = n[i], c = s.length, d = r[i] = new Array(c), l, f = 0; f < c; ++f)
      (l = s[f]) && (d[f] = l);
    d.sort(t);
  }
  return new We(r, this._parents).order();
}
function cu(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function lu() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function du() {
  return Array.from(this);
}
function uu() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length; r < i; ++r) {
      var s = o[r];
      if (s) return s;
    }
  return null;
}
function fu() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function hu() {
  return !this.node();
}
function pu(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], i = 0, s = r.length, c; i < s; ++i)
      (c = r[i]) && e.call(c, c.__data__, i, r);
  return this;
}
function gu(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function yu(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function mu(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function xu(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function wu(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function vu(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function bu(e, t) {
  var n = tr(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? yu : gu : typeof t == "function" ? n.local ? vu : wu : n.local ? xu : mu)(n, t));
}
function Ya(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function Nu(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Su(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function Eu(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function ku(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? Nu : typeof t == "function" ? Eu : Su)(e, t, n ?? "")) : Kt(this.node(), e);
}
function Kt(e, t) {
  return e.style.getPropertyValue(t) || Ya(e).getComputedStyle(e, null).getPropertyValue(t);
}
function Iu(e) {
  return function() {
    delete this[e];
  };
}
function Cu(e, t) {
  return function() {
    this[e] = t;
  };
}
function ju(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function _u(e, t) {
  return arguments.length > 1 ? this.each((t == null ? Iu : typeof t == "function" ? ju : Cu)(e, t)) : this.node()[e];
}
function qa(e) {
  return e.trim().split(/^|\s+/);
}
function Ni(e) {
  return e.classList || new Za(e);
}
function Za(e) {
  this._node = e, this._names = qa(e.getAttribute("class") || "");
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
function Ua(e, t) {
  for (var n = Ni(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function Ka(e, t) {
  for (var n = Ni(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function Au(e) {
  return function() {
    Ua(this, e);
  };
}
function Mu(e) {
  return function() {
    Ka(this, e);
  };
}
function Du(e, t) {
  return function() {
    (t.apply(this, arguments) ? Ua : Ka)(this, e);
  };
}
function Pu(e, t) {
  var n = qa(e + "");
  if (arguments.length < 2) {
    for (var o = Ni(this.node()), r = -1, i = n.length; ++r < i; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? Du : t ? Au : Mu)(n, t));
}
function $u() {
  this.textContent = "";
}
function Tu(e) {
  return function() {
    this.textContent = e;
  };
}
function zu(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function Ru(e) {
  return arguments.length ? this.each(e == null ? $u : (typeof e == "function" ? zu : Tu)(e)) : this.node().textContent;
}
function Lu() {
  this.innerHTML = "";
}
function Vu(e) {
  return function() {
    this.innerHTML = e;
  };
}
function Hu(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function Ou(e) {
  return arguments.length ? this.each(e == null ? Lu : (typeof e == "function" ? Hu : Vu)(e)) : this.node().innerHTML;
}
function Fu() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function Bu() {
  return this.each(Fu);
}
function Wu() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Xu() {
  return this.each(Wu);
}
function Yu(e) {
  var t = typeof e == "function" ? e : Oa(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function qu() {
  return null;
}
function Zu(e, t) {
  var n = typeof e == "function" ? e : Oa(e), o = t == null ? qu : typeof t == "function" ? t : bi(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function Uu() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Ku() {
  return this.each(Uu);
}
function Gu() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Ju() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Qu(e) {
  return this.select(e ? Ju : Gu);
}
function ef(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function tf(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function nf(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function of(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, i; n < r; ++n)
        i = t[n], (!e.type || i.type === e.type) && i.name === e.name ? this.removeEventListener(i.type, i.listener, i.options) : t[++o] = i;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function rf(e, t, n) {
  return function() {
    var o = this.__on, r, i = tf(t);
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
function sf(e, t, n) {
  var o = nf(e + ""), r, i = o.length, s;
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
  for (c = t ? rf : of, r = 0; r < i; ++r) this.each(c(o[r], t, n));
  return this;
}
function Ga(e, t, n) {
  var o = Ya(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function af(e, t) {
  return function() {
    return Ga(this, e, t);
  };
}
function cf(e, t) {
  return function() {
    return Ga(this, e, t.apply(this, arguments));
  };
}
function lf(e, t) {
  return this.each((typeof t == "function" ? cf : af)(e, t));
}
function* df() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length, s; r < i; ++r)
      (s = o[r]) && (yield s);
}
var Ja = [null];
function We(e, t) {
  this._groups = e, this._parents = t;
}
function Wn() {
  return new We([[document.documentElement]], Ja);
}
function uf() {
  return this;
}
We.prototype = Wn.prototype = {
  constructor: We,
  select: zd,
  selectAll: Hd,
  selectChild: Wd,
  selectChildren: Zd,
  filter: Ud,
  data: tu,
  enter: Kd,
  exit: ou,
  join: ru,
  merge: iu,
  selection: uf,
  order: su,
  sort: au,
  call: lu,
  nodes: du,
  node: uu,
  size: fu,
  empty: hu,
  each: pu,
  attr: bu,
  style: ku,
  property: _u,
  classed: Pu,
  text: Ru,
  html: Ou,
  raise: Bu,
  lower: Xu,
  append: Yu,
  insert: Zu,
  remove: Ku,
  clone: Qu,
  datum: ef,
  on: sf,
  dispatch: lf,
  [Symbol.iterator]: df
};
function Be(e) {
  return typeof e == "string" ? new We([[document.querySelector(e)]], [document.documentElement]) : new We([[e]], Ja);
}
function ff(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Ze(e, t) {
  if (e = ff(e), t === void 0 && (t = e.currentTarget), t) {
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
const hf = { passive: !1 }, Pn = { capture: !0, passive: !1 };
function Lr(e) {
  e.stopImmediatePropagation();
}
function Yt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Qa(e) {
  var t = e.document.documentElement, n = Be(e).on("dragstart.drag", Yt, Pn);
  "onselectstart" in t ? n.on("selectstart.drag", Yt, Pn) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function ec(e, t) {
  var n = e.document.documentElement, o = Be(e).on("dragstart.drag", null);
  t && (o.on("click.drag", Yt, Pn), setTimeout(function() {
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
function pf(e) {
  return !e.ctrlKey && !e.button;
}
function gf() {
  return this.parentNode;
}
function yf(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function mf() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function tc() {
  var e = pf, t = gf, n = yf, o = mf, r = {}, i = er("start", "drag", "end"), s = 0, c, d, l, f, u = 0;
  function h(x) {
    x.on("mousedown.drag", g).filter(o).on("touchstart.drag", w).on("touchmove.drag", m, hf).on("touchend.drag touchcancel.drag", b).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function g(x, I) {
    if (!(f || !e.call(this, x, I))) {
      var E = p(this, t.call(this, x, I), x, I, "mouse");
      E && (Be(x.view).on("mousemove.drag", y, Pn).on("mouseup.drag", v, Pn), Qa(x.view), Lr(x), l = !1, c = x.clientX, d = x.clientY, E("start", x));
    }
  }
  function y(x) {
    if (Yt(x), !l) {
      var I = x.clientX - c, E = x.clientY - d;
      l = I * I + E * E > u;
    }
    r.mouse("drag", x);
  }
  function v(x) {
    Be(x.view).on("mousemove.drag mouseup.drag", null), ec(x.view, l), Yt(x), r.mouse("end", x);
  }
  function w(x, I) {
    if (e.call(this, x, I)) {
      var E = x.changedTouches, A = t.call(this, x, I), P = E.length, T, _;
      for (T = 0; T < P; ++T)
        (_ = p(this, A, x, I, E[T].identifier, E[T])) && (Lr(x), _("start", x, E[T]));
    }
  }
  function m(x) {
    var I = x.changedTouches, E = I.length, A, P;
    for (A = 0; A < E; ++A)
      (P = r[I[A].identifier]) && (Yt(x), P("drag", x, I[A]));
  }
  function b(x) {
    var I = x.changedTouches, E = I.length, A, P;
    for (f && clearTimeout(f), f = setTimeout(function() {
      f = null;
    }, 500), A = 0; A < E; ++A)
      (P = r[I[A].identifier]) && (Lr(x), P("end", x, I[A]));
  }
  function p(x, I, E, A, P, T) {
    var _ = i.copy(), k = Ze(T || E, I), R, F, S;
    if ((S = n.call(x, new ni("beforestart", {
      sourceEvent: E,
      target: h,
      identifier: P,
      active: s,
      x: k[0],
      y: k[1],
      dx: 0,
      dy: 0,
      dispatch: _
    }), A)) != null)
      return R = S.x - k[0] || 0, F = S.y - k[1] || 0, function j(C, M, z) {
        var $ = k, W;
        switch (C) {
          case "start":
            r[P] = j, W = s++;
            break;
          case "end":
            delete r[P], --s;
          // falls through
          case "drag":
            k = Ze(z || M, I), W = s;
            break;
        }
        _.call(
          C,
          x,
          new ni(C, {
            sourceEvent: M,
            subject: S,
            target: h,
            identifier: P,
            active: W,
            x: k[0] + R,
            y: k[1] + F,
            dx: k[0] - $[0],
            dy: k[1] - $[1],
            dispatch: _
          }),
          A
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
    return arguments.length ? (u = (x = +x) * x, h) : Math.sqrt(u);
  }, h;
}
function Si(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function nc(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function Xn() {
}
var $n = 0.7, Vo = 1 / $n, qt = "\\s*([+-]?\\d+)\\s*", Tn = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", rt = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", xf = /^#([0-9a-f]{3,8})$/, wf = new RegExp(`^rgb\\(${qt},${qt},${qt}\\)$`), vf = new RegExp(`^rgb\\(${rt},${rt},${rt}\\)$`), bf = new RegExp(`^rgba\\(${qt},${qt},${qt},${Tn}\\)$`), Nf = new RegExp(`^rgba\\(${rt},${rt},${rt},${Tn}\\)$`), Sf = new RegExp(`^hsl\\(${Tn},${rt},${rt}\\)$`), Ef = new RegExp(`^hsla\\(${Tn},${rt},${rt},${Tn}\\)$`), is = {
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
Si(Xn, At, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: ss,
  // Deprecated! Use color.formatHex.
  formatHex: ss,
  formatHex8: kf,
  formatHsl: If,
  formatRgb: as,
  toString: as
});
function ss() {
  return this.rgb().formatHex();
}
function kf() {
  return this.rgb().formatHex8();
}
function If() {
  return oc(this).formatHsl();
}
function as() {
  return this.rgb().formatRgb();
}
function At(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = xf.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? cs(t) : n === 3 ? new He(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? vo(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? vo(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = wf.exec(e)) ? new He(t[1], t[2], t[3], 1) : (t = vf.exec(e)) ? new He(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = bf.exec(e)) ? vo(t[1], t[2], t[3], t[4]) : (t = Nf.exec(e)) ? vo(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = Sf.exec(e)) ? us(t[1], t[2] / 100, t[3] / 100, 1) : (t = Ef.exec(e)) ? us(t[1], t[2] / 100, t[3] / 100, t[4]) : is.hasOwnProperty(e) ? cs(is[e]) : e === "transparent" ? new He(NaN, NaN, NaN, 0) : null;
}
function cs(e) {
  return new He(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function vo(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new He(e, t, n, o);
}
function Cf(e) {
  return e instanceof Xn || (e = At(e)), e ? (e = e.rgb(), new He(e.r, e.g, e.b, e.opacity)) : new He();
}
function oi(e, t, n, o) {
  return arguments.length === 1 ? Cf(e) : new He(e, t, n, o ?? 1);
}
function He(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
Si(He, oi, nc(Xn, {
  brighter(e) {
    return e = e == null ? Vo : Math.pow(Vo, e), new He(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? $n : Math.pow($n, e), new He(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new He(Ct(this.r), Ct(this.g), Ct(this.b), Ho(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: ls,
  // Deprecated! Use color.formatHex.
  formatHex: ls,
  formatHex8: jf,
  formatRgb: ds,
  toString: ds
}));
function ls() {
  return `#${It(this.r)}${It(this.g)}${It(this.b)}`;
}
function jf() {
  return `#${It(this.r)}${It(this.g)}${It(this.b)}${It((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function ds() {
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
function us(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Ue(e, t, n, o);
}
function oc(e) {
  if (e instanceof Ue) return new Ue(e.h, e.s, e.l, e.opacity);
  if (e instanceof Xn || (e = At(e)), !e) return new Ue();
  if (e instanceof Ue) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), i = Math.max(t, n, o), s = NaN, c = i - r, d = (i + r) / 2;
  return c ? (t === i ? s = (n - o) / c + (n < o) * 6 : n === i ? s = (o - t) / c + 2 : s = (t - n) / c + 4, c /= d < 0.5 ? i + r : 2 - i - r, s *= 60) : c = d > 0 && d < 1 ? 0 : s, new Ue(s, c, d, e.opacity);
}
function _f(e, t, n, o) {
  return arguments.length === 1 ? oc(e) : new Ue(e, t, n, o ?? 1);
}
function Ue(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
Si(Ue, _f, nc(Xn, {
  brighter(e) {
    return e = e == null ? Vo : Math.pow(Vo, e), new Ue(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? $n : Math.pow($n, e), new Ue(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new He(
      Vr(e >= 240 ? e - 240 : e + 120, r, o),
      Vr(e, r, o),
      Vr(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new Ue(fs(this.h), bo(this.s), bo(this.l), Ho(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Ho(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${fs(this.h)}, ${bo(this.s) * 100}%, ${bo(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function fs(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function bo(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function Vr(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const Ei = (e) => () => e;
function Af(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function Mf(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function Df(e) {
  return (e = +e) == 1 ? rc : function(t, n) {
    return n - t ? Mf(t, n, e) : Ei(isNaN(t) ? n : t);
  };
}
function rc(e, t) {
  var n = t - e;
  return n ? Af(e, n) : Ei(isNaN(e) ? t : e);
}
const Oo = (function e(t) {
  var n = Df(t);
  function o(r, i) {
    var s = n((r = oi(r)).r, (i = oi(i)).r), c = n(r.g, i.g), d = n(r.b, i.b), l = rc(r.opacity, i.opacity);
    return function(f) {
      return r.r = s(f), r.g = c(f), r.b = d(f), r.opacity = l(f), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function Pf(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(i) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - i) + t[r] * i;
    return o;
  };
}
function $f(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Tf(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), i = new Array(n), s;
  for (s = 0; s < o; ++s) r[s] = An(e[s], t[s]);
  for (; s < n; ++s) i[s] = t[s];
  return function(c) {
    for (s = 0; s < o; ++s) i[s] = r[s](c);
    return i;
  };
}
function zf(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(o) {
    return n.setTime(e * (1 - o) + t * o), n;
  };
}
function ot(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function Rf(e, t) {
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
function Lf(e) {
  return function() {
    return e;
  };
}
function Vf(e) {
  return function(t) {
    return e(t) + "";
  };
}
function ic(e, t) {
  var n = ri.lastIndex = Hr.lastIndex = 0, o, r, i, s = -1, c = [], d = [];
  for (e = e + "", t = t + ""; (o = ri.exec(e)) && (r = Hr.exec(t)); )
    (i = r.index) > n && (i = t.slice(n, i), c[s] ? c[s] += i : c[++s] = i), (o = o[0]) === (r = r[0]) ? c[s] ? c[s] += r : c[++s] = r : (c[++s] = null, d.push({ i: s, x: ot(o, r) })), n = Hr.lastIndex;
  return n < t.length && (i = t.slice(n), c[s] ? c[s] += i : c[++s] = i), c.length < 2 ? d[0] ? Vf(d[0].x) : Lf(t) : (t = d.length, function(l) {
    for (var f = 0, u; f < t; ++f) c[(u = d[f]).i] = u.x(l);
    return c.join("");
  });
}
function An(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? Ei(t) : (n === "number" ? ot : n === "string" ? (o = At(t)) ? (t = o, Oo) : ic : t instanceof At ? Oo : t instanceof Date ? zf : $f(t) ? Pf : Array.isArray(t) ? Tf : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Rf : ot)(e, t);
}
var hs = 180 / Math.PI, ii = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function sc(e, t, n, o, r, i) {
  var s, c, d;
  return (s = Math.sqrt(e * e + t * t)) && (e /= s, t /= s), (d = e * n + t * o) && (n -= e * d, o -= t * d), (c = Math.sqrt(n * n + o * o)) && (n /= c, o /= c, d /= c), e * o < t * n && (e = -e, t = -t, d = -d, s = -s), {
    translateX: r,
    translateY: i,
    rotate: Math.atan2(t, e) * hs,
    skewX: Math.atan(d) * hs,
    scaleX: s,
    scaleY: c
  };
}
var No;
function Hf(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? ii : sc(t.a, t.b, t.c, t.d, t.e, t.f);
}
function Of(e) {
  return e == null || (No || (No = document.createElementNS("http://www.w3.org/2000/svg", "g")), No.setAttribute("transform", e), !(e = No.transform.baseVal.consolidate())) ? ii : (e = e.matrix, sc(e.a, e.b, e.c, e.d, e.e, e.f));
}
function ac(e, t, n, o) {
  function r(l) {
    return l.length ? l.pop() + " " : "";
  }
  function i(l, f, u, h, g, y) {
    if (l !== u || f !== h) {
      var v = g.push("translate(", null, t, null, n);
      y.push({ i: v - 4, x: ot(l, u) }, { i: v - 2, x: ot(f, h) });
    } else (u || h) && g.push("translate(" + u + t + h + n);
  }
  function s(l, f, u, h) {
    l !== f ? (l - f > 180 ? f += 360 : f - l > 180 && (l += 360), h.push({ i: u.push(r(u) + "rotate(", null, o) - 2, x: ot(l, f) })) : f && u.push(r(u) + "rotate(" + f + o);
  }
  function c(l, f, u, h) {
    l !== f ? h.push({ i: u.push(r(u) + "skewX(", null, o) - 2, x: ot(l, f) }) : f && u.push(r(u) + "skewX(" + f + o);
  }
  function d(l, f, u, h, g, y) {
    if (l !== u || f !== h) {
      var v = g.push(r(g) + "scale(", null, ",", null, ")");
      y.push({ i: v - 4, x: ot(l, u) }, { i: v - 2, x: ot(f, h) });
    } else (u !== 1 || h !== 1) && g.push(r(g) + "scale(" + u + "," + h + ")");
  }
  return function(l, f) {
    var u = [], h = [];
    return l = e(l), f = e(f), i(l.translateX, l.translateY, f.translateX, f.translateY, u, h), s(l.rotate, f.rotate, u, h), c(l.skewX, f.skewX, u, h), d(l.scaleX, l.scaleY, f.scaleX, f.scaleY, u, h), l = f = null, function(g) {
      for (var y = -1, v = h.length, w; ++y < v; ) u[(w = h[y]).i] = w.x(g);
      return u.join("");
    };
  };
}
var Ff = ac(Hf, "px, ", "px)", "deg)"), Bf = ac(Of, ", ", ")", ")"), Wf = 1e-12;
function ps(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Xf(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Yf(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const Mo = (function e(t, n, o) {
  function r(i, s) {
    var c = i[0], d = i[1], l = i[2], f = s[0], u = s[1], h = s[2], g = f - c, y = u - d, v = g * g + y * y, w, m;
    if (v < Wf)
      m = Math.log(h / l) / t, w = function(A) {
        return [
          c + A * g,
          d + A * y,
          l * Math.exp(t * A * m)
        ];
      };
    else {
      var b = Math.sqrt(v), p = (h * h - l * l + o * v) / (2 * l * n * b), x = (h * h - l * l - o * v) / (2 * h * n * b), I = Math.log(Math.sqrt(p * p + 1) - p), E = Math.log(Math.sqrt(x * x + 1) - x);
      m = (E - I) / t, w = function(A) {
        var P = A * m, T = ps(I), _ = l / (n * b) * (T * Yf(t * P + I) - Xf(I));
        return [
          c + _ * g,
          d + _ * y,
          l * T / ps(t * P + I)
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
var Gt = 0, Cn = 0, vn = 0, cc = 1e3, Fo, jn, Bo = 0, Mt = 0, nr = 0, zn = typeof performance == "object" && performance.now ? performance : Date, lc = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function ki() {
  return Mt || (lc(qf), Mt = zn.now() + nr);
}
function qf() {
  Mt = 0;
}
function Wo() {
  this._call = this._time = this._next = null;
}
Wo.prototype = dc.prototype = {
  constructor: Wo,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? ki() : +n) + (t == null ? 0 : +t), !this._next && jn !== this && (jn ? jn._next = this : Fo = this, jn = this), this._call = e, this._time = n, si();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, si());
  }
};
function dc(e, t, n) {
  var o = new Wo();
  return o.restart(e, t, n), o;
}
function Zf() {
  ki(), ++Gt;
  for (var e = Fo, t; e; )
    (t = Mt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --Gt;
}
function gs() {
  Mt = (Bo = zn.now()) + nr, Gt = Cn = 0;
  try {
    Zf();
  } finally {
    Gt = 0, Kf(), Mt = 0;
  }
}
function Uf() {
  var e = zn.now(), t = e - Bo;
  t > cc && (nr -= t, Bo = e);
}
function Kf() {
  for (var e, t = Fo, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Fo = n);
  jn = e, si(o);
}
function si(e) {
  if (!Gt) {
    Cn && (Cn = clearTimeout(Cn));
    var t = e - Mt;
    t > 24 ? (e < 1 / 0 && (Cn = setTimeout(gs, e - zn.now() - nr)), vn && (vn = clearInterval(vn))) : (vn || (Bo = zn.now(), vn = setInterval(Uf, cc)), Gt = 1, lc(gs));
  }
}
function ys(e, t, n) {
  var o = new Wo();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var Gf = er("start", "end", "cancel", "interrupt"), Jf = [], uc = 0, ms = 1, ai = 2, Do = 3, xs = 4, ci = 5, Po = 6;
function or(e, t, n, o, r, i) {
  var s = e.__transition;
  if (!s) e.__transition = {};
  else if (n in s) return;
  Qf(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: Gf,
    tween: Jf,
    time: i.time,
    delay: i.delay,
    duration: i.duration,
    ease: i.ease,
    timer: null,
    state: uc
  });
}
function Ii(e, t) {
  var n = tt(e, t);
  if (n.state > uc) throw new Error("too late; already scheduled");
  return n;
}
function it(e, t) {
  var n = tt(e, t);
  if (n.state > Do) throw new Error("too late; already running");
  return n;
}
function tt(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function Qf(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = dc(i, 0, n.time);
  function i(l) {
    n.state = ms, n.timer.restart(s, n.delay, n.time), n.delay <= l && s(l - n.delay);
  }
  function s(l) {
    var f, u, h, g;
    if (n.state !== ms) return d();
    for (f in o)
      if (g = o[f], g.name === n.name) {
        if (g.state === Do) return ys(s);
        g.state === xs ? (g.state = Po, g.timer.stop(), g.on.call("interrupt", e, e.__data__, g.index, g.group), delete o[f]) : +f < t && (g.state = Po, g.timer.stop(), g.on.call("cancel", e, e.__data__, g.index, g.group), delete o[f]);
      }
    if (ys(function() {
      n.state === Do && (n.state = xs, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = ai, n.on.call("start", e, e.__data__, n.index, n.group), n.state === ai) {
      for (n.state = Do, r = new Array(h = n.tween.length), f = 0, u = -1; f < h; ++f)
        (g = n.tween[f].value.call(e, e.__data__, n.index, n.group)) && (r[++u] = g);
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
function eh(e) {
  return this.each(function() {
    $o(this, e);
  });
}
function th(e, t) {
  var n, o;
  return function() {
    var r = it(this, e), i = r.tween;
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
function nh(e, t, n) {
  var o, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var i = it(this, e), s = i.tween;
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
function oh(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = tt(this.node(), n).tween, r = 0, i = o.length, s; r < i; ++r)
      if ((s = o[r]).name === e)
        return s.value;
    return null;
  }
  return this.each((t == null ? th : nh)(n, e, t));
}
function Ci(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = it(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return tt(r, o).value[t];
  };
}
function fc(e, t) {
  var n;
  return (typeof t == "number" ? ot : t instanceof At ? Oo : (n = At(t)) ? (t = n, Oo) : ic)(e, t);
}
function rh(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function ih(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function sh(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttribute(e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function ah(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttributeNS(e.space, e.local);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function ch(e, t, n) {
  var o, r, i;
  return function() {
    var s, c = n(this), d;
    return c == null ? void this.removeAttribute(e) : (s = this.getAttribute(e), d = c + "", s === d ? null : s === o && d === r ? i : (r = d, i = t(o = s, c)));
  };
}
function lh(e, t, n) {
  var o, r, i;
  return function() {
    var s, c = n(this), d;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (s = this.getAttributeNS(e.space, e.local), d = c + "", s === d ? null : s === o && d === r ? i : (r = d, i = t(o = s, c)));
  };
}
function dh(e, t) {
  var n = tr(e), o = n === "transform" ? Bf : fc;
  return this.attrTween(e, typeof t == "function" ? (n.local ? lh : ch)(n, o, Ci(this, "attr." + e, t)) : t == null ? (n.local ? ih : rh)(n) : (n.local ? ah : sh)(n, o, t));
}
function uh(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function fh(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function hh(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && fh(e, i)), n;
  }
  return r._value = t, r;
}
function ph(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && uh(e, i)), n;
  }
  return r._value = t, r;
}
function gh(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = tr(e);
  return this.tween(n, (o.local ? hh : ph)(o, t));
}
function yh(e, t) {
  return function() {
    Ii(this, e).delay = +t.apply(this, arguments);
  };
}
function mh(e, t) {
  return t = +t, function() {
    Ii(this, e).delay = t;
  };
}
function xh(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? yh : mh)(t, e)) : tt(this.node(), t).delay;
}
function wh(e, t) {
  return function() {
    it(this, e).duration = +t.apply(this, arguments);
  };
}
function vh(e, t) {
  return t = +t, function() {
    it(this, e).duration = t;
  };
}
function bh(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? wh : vh)(t, e)) : tt(this.node(), t).duration;
}
function Nh(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    it(this, e).ease = t;
  };
}
function Sh(e) {
  var t = this._id;
  return arguments.length ? this.each(Nh(t, e)) : tt(this.node(), t).ease;
}
function Eh(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    it(this, e).ease = n;
  };
}
function kh(e) {
  if (typeof e != "function") throw new Error();
  return this.each(Eh(this._id, e));
}
function Ih(e) {
  typeof e != "function" && (e = Ba(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = [], d, l = 0; l < s; ++l)
      (d = i[l]) && e.call(d, d.__data__, l, i) && c.push(d);
  return new lt(o, this._parents, this._name, this._id);
}
function Ch(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, i = Math.min(o, r), s = new Array(o), c = 0; c < i; ++c)
    for (var d = t[c], l = n[c], f = d.length, u = s[c] = new Array(f), h, g = 0; g < f; ++g)
      (h = d[g] || l[g]) && (u[g] = h);
  for (; c < o; ++c)
    s[c] = t[c];
  return new lt(s, this._parents, this._name, this._id);
}
function jh(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function _h(e, t, n) {
  var o, r, i = jh(t) ? Ii : it;
  return function() {
    var s = i(this, e), c = s.on;
    c !== o && (r = (o = c).copy()).on(t, n), s.on = r;
  };
}
function Ah(e, t) {
  var n = this._id;
  return arguments.length < 2 ? tt(this.node(), n).on.on(e) : this.each(_h(n, e, t));
}
function Mh(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function Dh() {
  return this.on("end.remove", Mh(this._id));
}
function Ph(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = bi(e));
  for (var o = this._groups, r = o.length, i = new Array(r), s = 0; s < r; ++s)
    for (var c = o[s], d = c.length, l = i[s] = new Array(d), f, u, h = 0; h < d; ++h)
      (f = c[h]) && (u = e.call(f, f.__data__, h, c)) && ("__data__" in f && (u.__data__ = f.__data__), l[h] = u, or(l[h], t, n, h, l, tt(f, n)));
  return new lt(i, this._parents, t, n);
}
function $h(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Fa(e));
  for (var o = this._groups, r = o.length, i = [], s = [], c = 0; c < r; ++c)
    for (var d = o[c], l = d.length, f, u = 0; u < l; ++u)
      if (f = d[u]) {
        for (var h = e.call(f, f.__data__, u, d), g, y = tt(f, n), v = 0, w = h.length; v < w; ++v)
          (g = h[v]) && or(g, t, n, v, h, y);
        i.push(h), s.push(f);
      }
  return new lt(i, s, t, n);
}
var Th = Wn.prototype.constructor;
function zh() {
  return new Th(this._groups, this._parents);
}
function Rh(e, t) {
  var n, o, r;
  return function() {
    var i = Kt(this, e), s = (this.style.removeProperty(e), Kt(this, e));
    return i === s ? null : i === n && s === o ? r : r = t(n = i, o = s);
  };
}
function hc(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Lh(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = Kt(this, e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Vh(e, t, n) {
  var o, r, i;
  return function() {
    var s = Kt(this, e), c = n(this), d = c + "";
    return c == null && (d = c = (this.style.removeProperty(e), Kt(this, e))), s === d ? null : s === o && d === r ? i : (r = d, i = t(o = s, c));
  };
}
function Hh(e, t) {
  var n, o, r, i = "style." + t, s = "end." + i, c;
  return function() {
    var d = it(this, e), l = d.on, f = d.value[i] == null ? c || (c = hc(t)) : void 0;
    (l !== n || r !== f) && (o = (n = l).copy()).on(s, r = f), d.on = o;
  };
}
function Oh(e, t, n) {
  var o = (e += "") == "transform" ? Ff : fc;
  return t == null ? this.styleTween(e, Rh(e, o)).on("end.style." + e, hc(e)) : typeof t == "function" ? this.styleTween(e, Vh(e, o, Ci(this, "style." + e, t))).each(Hh(this._id, e)) : this.styleTween(e, Lh(e, o, t), n).on("end.style." + e, null);
}
function Fh(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function Bh(e, t, n) {
  var o, r;
  function i() {
    var s = t.apply(this, arguments);
    return s !== r && (o = (r = s) && Fh(e, s, n)), o;
  }
  return i._value = t, i;
}
function Wh(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, Bh(e, t, n ?? ""));
}
function Xh(e) {
  return function() {
    this.textContent = e;
  };
}
function Yh(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function qh(e) {
  return this.tween("text", typeof e == "function" ? Yh(Ci(this, "text", e)) : Xh(e == null ? "" : e + ""));
}
function Zh(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Uh(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Zh(r)), t;
  }
  return o._value = e, o;
}
function Kh(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, Uh(e));
}
function Gh() {
  for (var e = this._name, t = this._id, n = pc(), o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], c = s.length, d, l = 0; l < c; ++l)
      if (d = s[l]) {
        var f = tt(d, t);
        or(d, e, n, l, s, {
          time: f.time + f.delay + f.duration,
          delay: 0,
          duration: f.duration,
          ease: f.ease
        });
      }
  return new lt(o, this._parents, e, n);
}
function Jh() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(i, s) {
    var c = { value: s }, d = { value: function() {
      --r === 0 && i();
    } };
    n.each(function() {
      var l = it(this, o), f = l.on;
      f !== e && (t = (e = f).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(d)), l.on = t;
    }), r === 0 && i();
  });
}
var Qh = 0;
function lt(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function pc() {
  return ++Qh;
}
var st = Wn.prototype;
lt.prototype = {
  constructor: lt,
  select: Ph,
  selectAll: $h,
  selectChild: st.selectChild,
  selectChildren: st.selectChildren,
  filter: Ih,
  merge: Ch,
  selection: zh,
  transition: Gh,
  call: st.call,
  nodes: st.nodes,
  node: st.node,
  size: st.size,
  empty: st.empty,
  each: st.each,
  on: Ah,
  attr: dh,
  attrTween: gh,
  style: Oh,
  styleTween: Wh,
  text: qh,
  textTween: Kh,
  remove: Dh,
  tween: oh,
  delay: xh,
  duration: bh,
  ease: Sh,
  easeVarying: kh,
  end: Jh,
  [Symbol.iterator]: st[Symbol.iterator]
};
function ep(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var tp = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: ep
};
function np(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function op(e) {
  var t, n;
  e instanceof lt ? (t = e._id, e = e._name) : (t = pc(), (n = tp).time = ki(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], c = s.length, d, l = 0; l < c; ++l)
      (d = s[l]) && or(d, e, t, l, s, n || np(d, t));
  return new lt(o, this._parents, e, t);
}
Wn.prototype.interrupt = eh;
Wn.prototype.transition = op;
const So = (e) => () => e;
function rp(e, {
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
var rr = new ct(1, 0, 0);
gc.prototype = ct.prototype;
function gc(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return rr;
  return e.__zoom;
}
function Or(e) {
  e.stopImmediatePropagation();
}
function bn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function ip(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function sp() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function ws() {
  return this.__zoom || rr;
}
function ap(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function cp() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function lp(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], i = e.invertY(t[0][1]) - n[0][1], s = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    s > i ? (i + s) / 2 : Math.min(0, i) || Math.max(0, s)
  );
}
function yc() {
  var e = ip, t = sp, n = lp, o = ap, r = cp, i = [0, 1 / 0], s = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, d = Mo, l = er("start", "zoom", "end"), f, u, h, g = 500, y = 150, v = 0, w = 10;
  function m(S) {
    S.property("__zoom", ws).on("wheel.zoom", P, { passive: !1 }).on("mousedown.zoom", T).on("dblclick.zoom", _).filter(r).on("touchstart.zoom", k).on("touchmove.zoom", R).on("touchend.zoom touchcancel.zoom", F).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  m.transform = function(S, j, C, M) {
    var z = S.selection ? S.selection() : S;
    z.property("__zoom", ws), S !== z ? I(S, j, C, M) : z.interrupt().each(function() {
      E(this, arguments).event(M).start().zoom(null, typeof j == "function" ? j.apply(this, arguments) : j).end();
    });
  }, m.scaleBy = function(S, j, C, M) {
    m.scaleTo(S, function() {
      var z = this.__zoom.k, $ = typeof j == "function" ? j.apply(this, arguments) : j;
      return z * $;
    }, C, M);
  }, m.scaleTo = function(S, j, C, M) {
    m.transform(S, function() {
      var z = t.apply(this, arguments), $ = this.__zoom, W = C == null ? x(z) : typeof C == "function" ? C.apply(this, arguments) : C, L = $.invert(W), O = typeof j == "function" ? j.apply(this, arguments) : j;
      return n(p(b($, O), W, L), z, s);
    }, C, M);
  }, m.translateBy = function(S, j, C, M) {
    m.transform(S, function() {
      return n(this.__zoom.translate(
        typeof j == "function" ? j.apply(this, arguments) : j,
        typeof C == "function" ? C.apply(this, arguments) : C
      ), t.apply(this, arguments), s);
    }, null, M);
  }, m.translateTo = function(S, j, C, M, z) {
    m.transform(S, function() {
      var $ = t.apply(this, arguments), W = this.__zoom, L = M == null ? x($) : typeof M == "function" ? M.apply(this, arguments) : M;
      return n(rr.translate(L[0], L[1]).scale(W.k).translate(
        typeof j == "function" ? -j.apply(this, arguments) : -j,
        typeof C == "function" ? -C.apply(this, arguments) : -C
      ), $, s);
    }, M, z);
  };
  function b(S, j) {
    return j = Math.max(i[0], Math.min(i[1], j)), j === S.k ? S : new ct(j, S.x, S.y);
  }
  function p(S, j, C) {
    var M = j[0] - C[0] * S.k, z = j[1] - C[1] * S.k;
    return M === S.x && z === S.y ? S : new ct(S.k, M, z);
  }
  function x(S) {
    return [(+S[0][0] + +S[1][0]) / 2, (+S[0][1] + +S[1][1]) / 2];
  }
  function I(S, j, C, M) {
    S.on("start.zoom", function() {
      E(this, arguments).event(M).start();
    }).on("interrupt.zoom end.zoom", function() {
      E(this, arguments).event(M).end();
    }).tween("zoom", function() {
      var z = this, $ = arguments, W = E(z, $).event(M), L = t.apply(z, $), O = C == null ? x(L) : typeof C == "function" ? C.apply(z, $) : C, U = Math.max(L[1][0] - L[0][0], L[1][1] - L[0][1]), K = z.__zoom, G = typeof j == "function" ? j.apply(z, $) : j, se = d(K.invert(O).concat(U / K.k), G.invert(O).concat(U / G.k));
      return function(ee) {
        if (ee === 1) ee = G;
        else {
          var H = se(ee), Z = U / H[2];
          ee = new ct(Z, O[0] - H[0] * Z, O[1] - H[1] * Z);
        }
        W.zoom(null, ee);
      };
    });
  }
  function E(S, j, C) {
    return !C && S.__zooming || new A(S, j);
  }
  function A(S, j) {
    this.that = S, this.args = j, this.active = 0, this.sourceEvent = null, this.extent = t.apply(S, j), this.taps = 0;
  }
  A.prototype = {
    event: function(S) {
      return S && (this.sourceEvent = S), this;
    },
    start: function() {
      return ++this.active === 1 && (this.that.__zooming = this, this.emit("start")), this;
    },
    zoom: function(S, j) {
      return this.mouse && S !== "mouse" && (this.mouse[1] = j.invert(this.mouse[0])), this.touch0 && S !== "touch" && (this.touch0[1] = j.invert(this.touch0[0])), this.touch1 && S !== "touch" && (this.touch1[1] = j.invert(this.touch1[0])), this.that.__zoom = j, this.emit("zoom"), this;
    },
    end: function() {
      return --this.active === 0 && (delete this.that.__zooming, this.emit("end")), this;
    },
    emit: function(S) {
      var j = Be(this.that).datum();
      l.call(
        S,
        this.that,
        new rp(S, {
          sourceEvent: this.sourceEvent,
          target: m,
          transform: this.that.__zoom,
          dispatch: l
        }),
        j
      );
    }
  };
  function P(S, ...j) {
    if (!e.apply(this, arguments)) return;
    var C = E(this, j).event(S), M = this.__zoom, z = Math.max(i[0], Math.min(i[1], M.k * Math.pow(2, o.apply(this, arguments)))), $ = Ze(S);
    if (C.wheel)
      (C.mouse[0][0] !== $[0] || C.mouse[0][1] !== $[1]) && (C.mouse[1] = M.invert(C.mouse[0] = $)), clearTimeout(C.wheel);
    else {
      if (M.k === z) return;
      C.mouse = [$, M.invert($)], $o(this), C.start();
    }
    bn(S), C.wheel = setTimeout(W, y), C.zoom("mouse", n(p(b(M, z), C.mouse[0], C.mouse[1]), C.extent, s));
    function W() {
      C.wheel = null, C.end();
    }
  }
  function T(S, ...j) {
    if (h || !e.apply(this, arguments)) return;
    var C = S.currentTarget, M = E(this, j, !0).event(S), z = Be(S.view).on("mousemove.zoom", O, !0).on("mouseup.zoom", U, !0), $ = Ze(S, C), W = S.clientX, L = S.clientY;
    Qa(S.view), Or(S), M.mouse = [$, this.__zoom.invert($)], $o(this), M.start();
    function O(K) {
      if (bn(K), !M.moved) {
        var G = K.clientX - W, se = K.clientY - L;
        M.moved = G * G + se * se > v;
      }
      M.event(K).zoom("mouse", n(p(M.that.__zoom, M.mouse[0] = Ze(K, C), M.mouse[1]), M.extent, s));
    }
    function U(K) {
      z.on("mousemove.zoom mouseup.zoom", null), ec(K.view, M.moved), bn(K), M.event(K).end();
    }
  }
  function _(S, ...j) {
    if (e.apply(this, arguments)) {
      var C = this.__zoom, M = Ze(S.changedTouches ? S.changedTouches[0] : S, this), z = C.invert(M), $ = C.k * (S.shiftKey ? 0.5 : 2), W = n(p(b(C, $), M, z), t.apply(this, j), s);
      bn(S), c > 0 ? Be(this).transition().duration(c).call(I, W, M, S) : Be(this).call(m.transform, W, M, S);
    }
  }
  function k(S, ...j) {
    if (e.apply(this, arguments)) {
      var C = S.touches, M = C.length, z = E(this, j, S.changedTouches.length === M).event(S), $, W, L, O;
      for (Or(S), W = 0; W < M; ++W)
        L = C[W], O = Ze(L, this), O = [O, this.__zoom.invert(O), L.identifier], z.touch0 ? !z.touch1 && z.touch0[2] !== O[2] && (z.touch1 = O, z.taps = 0) : (z.touch0 = O, $ = !0, z.taps = 1 + !!f);
      f && (f = clearTimeout(f)), $ && (z.taps < 2 && (u = O[0], f = setTimeout(function() {
        f = null;
      }, g)), $o(this), z.start());
    }
  }
  function R(S, ...j) {
    if (this.__zooming) {
      var C = E(this, j).event(S), M = S.changedTouches, z = M.length, $, W, L, O;
      for (bn(S), $ = 0; $ < z; ++$)
        W = M[$], L = Ze(W, this), C.touch0 && C.touch0[2] === W.identifier ? C.touch0[0] = L : C.touch1 && C.touch1[2] === W.identifier && (C.touch1[0] = L);
      if (W = C.that.__zoom, C.touch1) {
        var U = C.touch0[0], K = C.touch0[1], G = C.touch1[0], se = C.touch1[1], ee = (ee = G[0] - U[0]) * ee + (ee = G[1] - U[1]) * ee, H = (H = se[0] - K[0]) * H + (H = se[1] - K[1]) * H;
        W = b(W, Math.sqrt(ee / H)), L = [(U[0] + G[0]) / 2, (U[1] + G[1]) / 2], O = [(K[0] + se[0]) / 2, (K[1] + se[1]) / 2];
      } else if (C.touch0) L = C.touch0[0], O = C.touch0[1];
      else return;
      C.zoom("touch", n(p(W, L, O), C.extent, s));
    }
  }
  function F(S, ...j) {
    if (this.__zooming) {
      var C = E(this, j).event(S), M = S.changedTouches, z = M.length, $, W;
      for (Or(S), h && clearTimeout(h), h = setTimeout(function() {
        h = null;
      }, g), $ = 0; $ < z; ++$)
        W = M[$], C.touch0 && C.touch0[2] === W.identifier ? delete C.touch0 : C.touch1 && C.touch1[2] === W.identifier && delete C.touch1;
      if (C.touch1 && !C.touch0 && (C.touch0 = C.touch1, delete C.touch1), C.touch0) C.touch0[1] = this.__zoom.invert(C.touch0[0]);
      else if (C.end(), C.taps === 2 && (W = Ze(W, this), Math.hypot(u[0] - W[0], u[1] - W[1]) < w)) {
        var L = Be(this).on("dblclick.zoom");
        L && L.apply(this, arguments);
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
const Xe = {
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
], mc = ["Enter", " ", "Escape"], xc = {
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
const wc = {
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
const vs = {
  [te.Left]: te.Right,
  [te.Right]: te.Left,
  [te.Top]: te.Bottom,
  [te.Bottom]: te.Top
};
function vc(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const bc = (e) => "id" in e && "source" in e && "target" in e, dp = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), ji = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), Yn = (e, t = [0, 0]) => {
  const { width: n, height: o } = dt(e), r = e.origin ?? t, i = n * r[0], s = o * r[1];
  return {
    x: e.position.x - i,
    y: e.position.y - s
  };
}, up = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const i = typeof r == "string";
    let s = !t.nodeLookup && !i ? r : void 0;
    t.nodeLookup && (s = i ? t.nodeLookup.get(r) : ji(r) ? r : t.nodeLookup.get(r.id));
    const c = s ? Yo(s, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return ir(o, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return sr(n);
}, qn = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = ir(n, Yo(r)), o = !0);
  }), o ? sr(n) : { x: 0, y: 0, width: 0, height: 0 };
}, _i = (e, t, [n, o, r] = [0, 0, 1], i = !1, s = !1) => {
  const c = {
    ...rn(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, d = [];
  for (const l of e.values()) {
    const { measured: f, selectable: u = !0, hidden: h = !1 } = l;
    if (s && !u || h)
      continue;
    const g = f.width ?? l.width ?? l.initialWidth ?? null, y = f.height ?? l.height ?? l.initialHeight ?? null, v = Vn(c, en(l)), w = (g ?? 0) * (y ?? 0), m = i && v > 0;
    (!l.internals.handleBounds || m || v >= w || l.dragging) && d.push(l);
  }
  return d;
}, fp = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function hp(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function pp({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: i }, s) {
  if (e.size === 0)
    return !0;
  const c = hp(e, s), d = qn(c), l = Mi(d, t, n, s?.minZoom ?? r, s?.maxZoom ?? i, s?.padding ?? 0.1);
  return await o.setViewport(l, {
    duration: s?.duration,
    ease: s?.ease,
    interpolate: s?.interpolate
  }), !0;
}
function Nc({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: i }) {
  const s = n.get(e), c = s.parentId ? n.get(s.parentId) : void 0, { x: d, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, f = s.origin ?? o;
  let u = s.extent || r;
  if (s.extent === "parent" && !s.expandParent)
    if (!c)
      i?.("005", Xe.error005());
    else {
      const g = c.measured.width, y = c.measured.height;
      g && y && (u = [
        [d, l],
        [d + g, l + y]
      ]);
    }
  else c && Pt(s.extent) && (u = [
    [s.extent[0][0] + d, s.extent[0][1] + l],
    [s.extent[1][0] + d, s.extent[1][1] + l]
  ]);
  const h = Pt(u) ? Dt(t, u, s.measured) : t;
  return (s.measured.width === void 0 || s.measured.height === void 0) && i?.("015", Xe.error015()), {
    position: {
      x: h.x - d + (s.measured.width ?? 0) * f[0],
      y: h.y - l + (s.measured.height ?? 0) * f[1]
    },
    positionAbsolute: h
  };
}
async function gp({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const i = new Set(e.map((h) => h.id)), s = [];
  for (const h of n) {
    if (h.deletable === !1)
      continue;
    const g = i.has(h.id), y = !g && h.parentId && s.find((v) => v.id === h.parentId);
    (g || y) && s.push(h);
  }
  const c = new Set(t.map((h) => h.id)), d = o.filter((h) => h.deletable !== !1), f = fp(s, d);
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
const Qt = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), Dt = (e = { x: 0, y: 0 }, t, n) => ({
  x: Qt(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: Qt(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Sc(e, t, n) {
  const { width: o, height: r } = dt(n), { x: i, y: s } = n.internals.positionAbsolute;
  return Dt(e, [
    [i, s],
    [i + o, s + r]
  ], t);
}
const bs = (e, t, n) => e < t ? Qt(Math.abs(e - t), 1, t) / t : e > n ? -Qt(Math.abs(e - n), 1, t) / t : 0, Ai = (e, t, n = 15, o = 40) => {
  const r = bs(e.x, o, t.width - o) * n, i = bs(e.y, o, t.height - o) * n;
  return [r, i];
}, ir = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), li = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), sr = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), en = (e, t = [0, 0]) => {
  const { x: n, y: o } = ji(e) ? e.internals.positionAbsolute : Yn(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, Yo = (e, t = [0, 0]) => {
  const { x: n, y: o } = ji(e) ? e.internals.positionAbsolute : Yn(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, Ec = (e, t) => sr(ir(li(e), li(t))), Vn = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, Ns = (e) => Ke(e.width) && Ke(e.height) && Ke(e.x) && Ke(e.y), Ke = (e) => !isNaN(e) && isFinite(e), kc = (e, t) => (n, o) => {
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
function Ft(e, t) {
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
function yp(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = Ft(e, n), r = Ft(e, t);
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
    const o = Ft(e.top ?? e.y ?? 0, n), r = Ft(e.bottom ?? e.y ?? 0, n), i = Ft(e.left ?? e.x ?? 0, t), s = Ft(e.right ?? e.x ?? 0, t);
    return { top: o, right: s, bottom: r, left: i, x: i + s, y: o + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function mp(e, t, n, o, r, i) {
  const { x: s, y: c } = tn(e, [t, n, o]), { x: d, y: l } = tn({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), f = r - d, u = i - l;
  return {
    left: Math.floor(s),
    top: Math.floor(c),
    right: Math.floor(f),
    bottom: Math.floor(u)
  };
}
const Mi = (e, t, n, o, r, i) => {
  const s = yp(i, t, n), c = (t - s.x) / e.width, d = (n - s.y) / e.height, l = Math.min(c, d), f = Qt(l, o, r), u = e.x + e.width / 2, h = e.y + e.height / 2, g = t / 2 - u * f, y = n / 2 - h * f, v = mp(e, g, y, f, t, n), w = {
    left: Math.min(v.left - s.left, 0),
    top: Math.min(v.top - s.top, 0),
    right: Math.min(v.right - s.right, 0),
    bottom: Math.min(v.bottom - s.bottom, 0)
  };
  return {
    x: g - w.left + w.right,
    y: y - w.top + w.bottom,
    zoom: f
  };
}, Hn = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function Pt(e) {
  return e != null && e !== "parent";
}
function dt(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function Ic(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function Cc(e, t = { width: 0, height: 0 }, n, o, r) {
  const i = { ...e }, s = o.get(n);
  if (s) {
    const c = s.origin || r;
    i.x += s.internals.positionAbsolute.x - (t.width ?? 0) * c[0], i.y += s.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return i;
}
function Ss(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function xp() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function wp(e) {
  return { ...xc, ...e || {} };
}
function Mn(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: i, y: s } = Ge(e), c = rn({ x: i - (r?.left ?? 0), y: s - (r?.top ?? 0) }, o), { x: d, y: l } = n ? Zn(c, t) : c;
  return {
    xSnapped: d,
    ySnapped: l,
    ...c
  };
}
const Di = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), jc = (e) => e?.getRootNode?.() || window?.document, vp = ["INPUT", "SELECT", "TEXTAREA"];
function _c(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : vp.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const Ac = (e) => "clientX" in e, Ge = (e, t) => {
  const n = Ac(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, Es = (e, t, n, o, r) => {
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
function Mc({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: i, targetControlX: s, targetControlY: c }) {
  const d = e * 0.125 + r * 0.375 + s * 0.375 + n * 0.125, l = t * 0.125 + i * 0.375 + c * 0.375 + o * 0.125, f = Math.abs(d - e), u = Math.abs(l - t);
  return [d, l, f, u];
}
function Eo(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function ks({ pos: e, x1: t, y1: n, x2: o, y2: r, c: i }) {
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
function Dc({ sourceX: e, sourceY: t, sourcePosition: n = te.Bottom, targetX: o, targetY: r, targetPosition: i = te.Top, curvature: s = 0.25 }) {
  const [c, d] = ks({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: s
  }), [l, f] = ks({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: s
  }), [u, h, g, y] = Mc({
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
    g,
    y
  ];
}
function Pc({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, i = n < e ? n + r : n - r, s = Math.abs(o - t) / 2, c = o < t ? o + s : o - s;
  return [i, c, r, s];
}
function bp({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: i = "basic" }) {
  if (i === "manual")
    return o;
  const s = r && n ? o + 1e3 : o, c = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return s + c;
}
function Np({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const i = ir(Yo(e), Yo(t));
  i.x === i.x2 && (i.x2 += 1), i.y === i.y2 && (i.y2 += 1);
  const s = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return Vn(s, sr(i)) > 0;
}
const $c = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, Sp = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), Ep = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Xe.error006()), t;
  const o = n.getEdgeId || $c;
  let r;
  return bc(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, Sp(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, kp = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: r, ...i } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", Xe.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return o.onError?.("007", Xe.error007(r)), n;
  const c = o.getEdgeId || $c, d = {
    ...i,
    id: o.shouldReplaceId ? c(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== r).concat(d);
};
function Tc({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, i, s, c] = Pc({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, i, s, c];
}
const Is = {
  [te.Left]: { x: -1, y: 0 },
  [te.Right]: { x: 1, y: 0 },
  [te.Top]: { x: 0, y: -1 },
  [te.Bottom]: { x: 0, y: 1 }
}, Ip = ({ source: e, sourcePosition: t = te.Bottom, target: n }) => t === te.Left || t === te.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, Cs = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Cp({ source: e, sourcePosition: t = te.Bottom, target: n, targetPosition: o = te.Top, center: r, offset: i, stepPosition: s }) {
  const c = Is[t], d = Is[o], l = { x: e.x + c.x * i, y: e.y + c.y * i }, f = { x: n.x + d.x * i, y: n.y + d.y * i }, u = Ip({
    source: l,
    sourcePosition: t,
    target: f
  }), h = u.x !== 0 ? "x" : "y", g = u[h];
  let y = [], v, w;
  const m = { x: 0, y: 0 }, b = { x: 0, y: 0 }, [, , p, x] = Pc({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[h] * d[h] === -1) {
    h === "x" ? (v = r.x ?? l.x + (f.x - l.x) * s, w = r.y ?? (l.y + f.y) / 2) : (v = r.x ?? (l.x + f.x) / 2, w = r.y ?? l.y + (f.y - l.y) * s);
    const P = [
      { x: v, y: l.y },
      { x: v, y: f.y }
    ], T = [
      { x: l.x, y: w },
      { x: f.x, y: w }
    ];
    c[h] === g ? y = h === "x" ? P : T : y = h === "x" ? T : P;
  } else {
    const P = [{ x: l.x, y: f.y }], T = [{ x: f.x, y: l.y }];
    if (h === "x" ? y = c.x === g ? T : P : y = c.y === g ? P : T, t === o) {
      const S = Math.abs(e[h] - n[h]);
      if (S <= i) {
        const j = Math.min(i - 1, i - S);
        c[h] === g ? m[h] = (l[h] > e[h] ? -1 : 1) * j : b[h] = (f[h] > n[h] ? -1 : 1) * j;
      }
    }
    if (t !== o) {
      const S = h === "x" ? "y" : "x", j = c[h] === d[S], C = l[S] > f[S], M = l[S] < f[S];
      (c[h] === 1 && (!j && C || j && M) || c[h] !== 1 && (!j && M || j && C)) && (y = h === "x" ? P : T);
    }
    const _ = { x: l.x + m.x, y: l.y + m.y }, k = { x: f.x + b.x, y: f.y + b.y }, R = Math.max(Math.abs(_.x - y[0].x), Math.abs(k.x - y[0].x)), F = Math.max(Math.abs(_.y - y[0].y), Math.abs(k.y - y[0].y));
    R >= F ? (v = (_.x + k.x) / 2, w = y[0].y) : (v = y[0].x, w = (_.y + k.y) / 2);
  }
  const I = { x: l.x + m.x, y: l.y + m.y }, E = { x: f.x + b.x, y: f.y + b.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...I.x !== y[0].x || I.y !== y[0].y ? [I] : [],
    ...y,
    ...E.x !== y[y.length - 1].x || E.y !== y[y.length - 1].y ? [E] : [],
    n
  ], v, w, p, x];
}
function jp(e, t, n, o) {
  const r = Math.min(Cs(e, t) / 2, Cs(t, n) / 2, o), { x: i, y: s } = t;
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
  const [u, h, g, y, v] = Cp({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: i,
    center: { x: c, y: d },
    offset: l,
    stepPosition: f
  });
  let w = `M${u[0].x} ${u[0].y}`;
  for (let m = 1; m < u.length - 1; m++)
    w += jp(u[m - 1], u[m], u[m + 1], s);
  return w += `L${u[u.length - 1].x} ${u[u.length - 1].y}`, [w, h, g, y, v];
}
function js(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function _p(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!js(t) || !js(n))
    return null;
  const o = t.internals.handleBounds || _s(t.handles), r = n.internals.handleBounds || _s(n.handles), i = As(o?.source ?? [], e.sourceHandle), s = As(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === Jt.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!i || !s)
    return e.onError?.("008", Xe.error008(i ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = i?.position || te.Bottom, d = s?.position || te.Top, l = $t(t, i, c), f = $t(n, s, d);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: f.x,
    targetY: f.y,
    sourcePosition: c,
    targetPosition: d
  };
}
function _s(e) {
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
function As(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function di(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function Ap(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const i = /* @__PURE__ */ new Set();
  return e.reduce((s, c) => ([c.markerStart || o, c.markerEnd || r].forEach((d) => {
    if (d && typeof d == "object") {
      const l = di(d, t);
      i.has(l) || (s.push({ id: l, color: d.color || n, ...d }), i.add(l));
    }
  }), s), []).sort((s, c) => s.id.localeCompare(c.id));
}
const zc = 1e3, Mp = 10, Pi = {
  nodeOrigin: [0, 0],
  nodeExtent: Rn,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Dp = {
  ...Pi,
  checkEquality: !0
};
function $i(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function Pp(e, t, n) {
  const o = $i(Pi, n);
  for (const r of e.values())
    if (r.parentId)
      zi(r, e, t, o);
    else {
      const i = Yn(r, o.nodeOrigin), s = Pt(r.extent) ? r.extent : o.nodeExtent, c = Dt(i, s, dt(r));
      r.internals.positionAbsolute = c;
    }
}
function $p(e, t) {
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
  const r = $i(Dp, o), i = { i: 0 }, s = new Map(t), c = r?.elevateNodesOnSelect && !Ti(r.zIndexMode) ? zc : 0;
  let d = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const f of e) {
    let u = s.get(f.id);
    if (r.checkEquality && f === u?.internals.userNode)
      t.set(f.id, u);
    else {
      const h = Yn(f, r.nodeOrigin), g = Pt(f.extent) ? f.extent : r.nodeExtent, y = Dt(h, g, dt(f));
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
          handleBounds: $p(f, u),
          z: Rc(f, c, r.zIndexMode),
          userNode: f
        }
      }, t.set(f.id, u);
    }
    (u.measured === void 0 || u.measured.width === void 0 || u.measured.height === void 0) && !u.hidden && (d = !1), f.parentId && zi(u, t, n, o, i), l ||= f.selected ?? !1;
  }
  return { nodesInitialized: d, hasSelectedNodes: l };
}
function Tp(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function zi(e, t, n, o, r) {
  const { elevateNodesOnSelect: i, nodeOrigin: s, nodeExtent: c, zIndexMode: d } = $i(Pi, o), l = e.parentId, f = t.get(l);
  if (!f) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Tp(e, n), r && !f.parentId && f.internals.rootParentIndex === void 0 && d === "auto" && (f.internals.rootParentIndex = ++r.i, f.internals.z = f.internals.z + r.i * Mp), r && f.internals.rootParentIndex !== void 0 && (r.i = f.internals.rootParentIndex);
  const u = i && !Ti(d) ? zc : 0, { x: h, y: g, z: y } = zp(e, f, s, c, u, d), { positionAbsolute: v } = e.internals, w = h !== v.x || g !== v.y;
  (w || y !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: w ? { x: h, y: g } : v,
      z: y
    }
  });
}
function Rc(e, t, n) {
  const o = Ke(e.zIndex) ? e.zIndex : 0;
  return Ti(n) ? o : o + (e.selected ? t : 0);
}
function zp(e, t, n, o, r, i) {
  const { x: s, y: c } = t.internals.positionAbsolute, d = dt(e), l = Yn(e, n), f = Pt(e.extent) ? Dt(l, e.extent, d) : l;
  let u = Dt({ x: s + f.x, y: c + f.y }, o, d);
  e.extent === "parent" && (u = Sc(u, d, t));
  const h = Rc(e, r, i), g = t.internals.z ?? 0;
  return {
    x: u.x,
    y: u.y,
    z: g >= h ? g + 1 : h
  };
}
function Ri(e, t, n, o = [0, 0]) {
  const r = [], i = /* @__PURE__ */ new Map();
  for (const s of e) {
    const c = t.get(s.parentId);
    if (!c)
      continue;
    const d = i.get(s.parentId)?.expandedRect ?? en(c), l = Ec(d, s.rect);
    i.set(s.parentId, { expandedRect: l, parent: c });
  }
  return i.size > 0 && i.forEach(({ expandedRect: s, parent: c }, d) => {
    const l = c.internals.positionAbsolute, f = dt(c), u = c.origin ?? o, h = s.x < l.x ? Math.round(Math.abs(l.x - s.x)) : 0, g = s.y < l.y ? Math.round(Math.abs(l.y - s.y)) : 0, y = Math.max(f.width, Math.round(s.width)), v = Math.max(f.height, Math.round(s.height)), w = (y - f.width) * u[0], m = (v - f.height) * u[1];
    (h > 0 || g > 0 || w || m) && (r.push({
      id: d,
      type: "position",
      position: {
        x: c.position.x - h + w,
        y: c.position.y - g + m
      }
    }), n.get(d)?.forEach((b) => {
      e.some((p) => p.id === b.id) || r.push({
        id: b.id,
        type: "position",
        position: {
          x: b.position.x + h,
          y: b.position.y + g
        }
      });
    })), (f.width < s.width || f.height < s.height || h || g) && r.push({
      id: d,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: y + (h ? u[0] * h - w : 0),
        height: v + (g ? u[1] * g - m : 0)
      }
    });
  }), r;
}
function Rp(e, t, n, o, r, i, s) {
  const c = o?.querySelector(".xyflow__viewport");
  let d = !1;
  if (!c)
    return { changes: [], updatedInternals: d };
  const l = [], f = window.getComputedStyle(c), { m22: u } = new window.DOMMatrixReadOnly(f.transform), h = [];
  for (const g of e.values()) {
    const y = t.get(g.id);
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
    const v = Di(g.nodeElement), w = y.measured.width !== v.width || y.measured.height !== v.height;
    if (!!(v.width && v.height && (w || !y.internals.handleBounds || g.force))) {
      const b = g.nodeElement.getBoundingClientRect(), p = Pt(y.extent) ? y.extent : i;
      let { positionAbsolute: x } = y.internals;
      y.parentId && y.extent === "parent" ? x = Sc(x, v, t.get(y.parentId)) : p && (x = Dt(x, p, v));
      const I = {
        ...y,
        measured: v,
        internals: {
          ...y.internals,
          positionAbsolute: x,
          handleBounds: {
            source: Es("source", g.nodeElement, b, u, y.id),
            target: Es("target", g.nodeElement, b, u, y.id)
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
    const g = Ri(h, t, n, r);
    l.push(...g);
  }
  return { changes: l, updatedInternals: d };
}
async function Lp({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: i }) {
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
function Ms(e, t, n, o, r, i) {
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
function Lc(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: i, sourceHandle: s = null, targetHandle: c = null } = o, d = { edgeId: o.id, source: r, target: i, sourceHandle: s, targetHandle: c }, l = `${r}-${s}--${i}-${c}`, f = `${i}-${c}--${r}-${s}`;
    Ms("source", d, f, e, r, s), Ms("target", d, l, e, i, c), t.set(o.id, o);
  }
}
function Vc(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : Vc(n, t) : !1;
}
function Ds(e, t, n) {
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
function Vp(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [i, s] of e)
    if ((s.selected || s.id === o) && (!s.parentId || !Vc(s, e)) && (s.draggable || t && typeof s.draggable > "u")) {
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
function Hp({ dragItems: e, snapGrid: t, x: n, y: o }) {
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
function Op({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let i = { x: null, y: null }, s = 0, c = /* @__PURE__ */ new Map(), d = !1, l = { x: 0, y: 0 }, f = null, u = !1, h = null, g = !1, y = !1, v = null;
  function w({ noDragClassName: b, handleSelector: p, domNode: x, isSelectable: I, nodeId: E, nodeClickDistance: A = 0 }) {
    h = Be(x);
    function P({ x: R, y: F }) {
      const { nodeLookup: S, nodeExtent: j, snapGrid: C, snapToGrid: M, nodeOrigin: z, onNodeDrag: $, onSelectionDrag: W, onError: L, updateNodePositions: O } = t();
      i = { x: R, y: F };
      let U = !1;
      const K = c.size > 1, G = K && j ? li(qn(c)) : null, se = K && M ? Hp({
        dragItems: c,
        snapGrid: C,
        x: R,
        y: F
      }) : null;
      for (const [ee, H] of c) {
        if (!S.has(ee))
          continue;
        let Z = { x: R - H.distance.x, y: F - H.distance.y };
        M && (Z = se ? {
          x: Math.round(Z.x + se.x),
          y: Math.round(Z.y + se.y)
        } : Zn(Z, C));
        let ae = null;
        if (K && j && !H.extent && G) {
          const { positionAbsolute: Q } = H.internals, le = Q.x - G.x + j[0][0], de = Q.x + H.measured.width - G.x2 + j[1][0], he = Q.y - G.y + j[0][1], ke = Q.y + H.measured.height - G.y2 + j[1][1];
          ae = [
            [le, he],
            [de, ke]
          ];
        }
        const { position: oe, positionAbsolute: q } = Nc({
          nodeId: ee,
          nextPosition: Z,
          nodeLookup: S,
          nodeExtent: ae || j,
          nodeOrigin: z,
          onError: L
        });
        U = U || H.position.x !== oe.x || H.position.y !== oe.y, H.position = oe, H.internals.positionAbsolute = q;
      }
      if (y = y || U, !!U && (O(c, !0), v && (o || $ || !E && W))) {
        const [ee, H] = Fr({
          nodeId: E,
          dragItems: c,
          nodeLookup: S
        });
        o?.(v, c, ee, H), $?.(v, ee, H), E || W?.(v, H);
      }
    }
    async function T() {
      if (!f)
        return;
      const { transform: R, panBy: F, autoPanSpeed: S, autoPanOnNodeDrag: j } = t();
      if (!j) {
        d = !1, cancelAnimationFrame(s);
        return;
      }
      const [C, M] = Ai(l, f, S);
      (C !== 0 || M !== 0) && (i.x = (i.x ?? 0) - C / R[2], i.y = (i.y ?? 0) - M / R[2], await F({ x: C, y: M }) && P(i)), s = requestAnimationFrame(T);
    }
    function _(R) {
      const { nodeLookup: F, multiSelectionActive: S, nodesDraggable: j, transform: C, snapGrid: M, snapToGrid: z, selectNodesOnDrag: $, onNodeDragStart: W, onSelectionDragStart: L, unselectNodesAndEdges: O } = t();
      u = !0, (!$ || !I) && !S && E && (F.get(E)?.selected || O()), I && $ && E && e?.(E);
      const U = Mn(R.sourceEvent, { transform: C, snapGrid: M, snapToGrid: z, containerBounds: f });
      if (i = U, c = Vp(F, j, U, E), c.size > 0 && (n || W || !E && L)) {
        const [K, G] = Fr({
          nodeId: E,
          dragItems: c,
          nodeLookup: F
        });
        n?.(R.sourceEvent, c, K, G), W?.(R.sourceEvent, K, G), E || L?.(R.sourceEvent, G);
      }
    }
    const k = tc().clickDistance(A).on("start", (R) => {
      const { domNode: F, nodeDragThreshold: S, transform: j, snapGrid: C, snapToGrid: M } = t();
      f = F?.getBoundingClientRect() || null, g = !1, y = !1, v = R.sourceEvent, S === 0 && _(R), i = Mn(R.sourceEvent, { transform: j, snapGrid: C, snapToGrid: M, containerBounds: f }), l = Ge(R.sourceEvent, f);
    }).on("drag", (R) => {
      const { autoPanOnNodeDrag: F, transform: S, snapGrid: j, snapToGrid: C, nodeDragThreshold: M, nodeLookup: z } = t(), $ = Mn(R.sourceEvent, { transform: S, snapGrid: j, snapToGrid: C, containerBounds: f });
      if (v = R.sourceEvent, (R.sourceEvent.type === "touchmove" && R.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      E && !z.has(E)) && (g = !0), !g) {
        if (!d && F && u && (d = !0, T()), !u) {
          const W = Ge(R.sourceEvent, f), L = W.x - l.x, O = W.y - l.y;
          Math.sqrt(L * L + O * O) > M && _(R);
        }
        (i.x !== $.xSnapped || i.y !== $.ySnapped) && c && u && (l = Ge(R.sourceEvent, f), P($));
      }
    }).on("end", (R) => {
      if (!u || g) {
        g && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (d = !1, u = !1, cancelAnimationFrame(s), c.size > 0) {
        const { nodeLookup: F, updateNodePositions: S, onNodeDragStop: j, onSelectionDragStop: C } = t();
        if (y && (S(c, !1), y = !1), r || j || !E && C) {
          const [M, z] = Fr({
            nodeId: E,
            dragItems: c,
            nodeLookup: F,
            dragging: !1
          });
          r?.(R.sourceEvent, c, M, z), j?.(R.sourceEvent, M, z), E || C?.(R.sourceEvent, z);
        }
      }
    }).filter((R) => {
      const F = R.target;
      return !R.button && (!b || !Ds(F, `.${b}`, x)) && (!p || Ds(F, p, x));
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
function Fp(e, t, n) {
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
const Bp = 250;
function Wp(e, t, n, o) {
  let r = [], i = 1 / 0;
  const s = Fp(e, n, t + Bp);
  for (const c of s) {
    const d = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of d) {
      if (o.nodeId === l.nodeId && o.type === l.type && o.id === l.id)
        continue;
      const { x: f, y: u } = $t(c, l, l.position, !0), h = Math.sqrt(Math.pow(f - e.x, 2) + Math.pow(u - e.y, 2));
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
function Hc(e, t, n, o, r, i = !1) {
  const s = o.get(e);
  if (!s)
    return null;
  const c = r === "strict" ? s.internals.handleBounds?.[t] : [...s.internals.handleBounds?.source ?? [], ...s.internals.handleBounds?.target ?? []], d = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return d && i ? { ...d, ...$t(s, d, d.position, !0) } : d;
}
function Oc(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Xp(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const Fc = () => !0;
function Yp(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: i, isTarget: s, domNode: c, nodeLookup: d, lib: l, autoPanOnConnect: f, flowId: u, panBy: h, cancelConnection: g, onConnectStart: y, onConnect: v, onConnectEnd: w, isValidConnection: m = Fc, onReconnectEnd: b, updateConnection: p, getTransform: x, getFromHandle: I, autoPanSpeed: E, dragThreshold: A = 1, handleDomNode: P }) {
  const T = jc(e.target);
  let _ = 0, k;
  const { x: R, y: F } = Ge(e), S = Oc(i, P), j = c?.getBoundingClientRect();
  let C = !1;
  if (!j || !S)
    return;
  const M = Hc(r, S, o, d, t);
  if (!M)
    return;
  let z = Ge(e, j), $ = !1, W = null, L = !1, O = null;
  function U() {
    if (!f || !j)
      return;
    const [oe, q] = Ai(z, j, E);
    h({ x: oe, y: q }), _ = requestAnimationFrame(U);
  }
  const K = {
    ...M,
    nodeId: r,
    type: S,
    position: M.position
  }, G = d.get(r);
  let ee = {
    inProgress: !0,
    isValid: null,
    from: $t(G, K, te.Left, !0),
    fromHandle: K,
    fromPosition: K.position,
    fromNode: G,
    to: z,
    toHandle: null,
    toPosition: vs[K.position],
    toNode: null,
    pointer: z
  };
  function H() {
    C = !0, p(ee), y?.(e, { nodeId: r, handleId: o, handleType: S });
  }
  A === 0 && H();
  function Z(oe) {
    if (!C) {
      const { x: ke, y: Ie } = Ge(oe), Pe = ke - R, ge = Ie - F;
      if (!(Pe * Pe + ge * ge > A * A))
        return;
      H();
    }
    if (!I() || !K) {
      ae(oe);
      return;
    }
    const q = x();
    z = Ge(oe, j), k = Wp(rn(z, q, !1, [1, 1]), n, d, K), $ || (U(), $ = !0);
    const Q = Bc(oe, {
      handle: k,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: s ? "target" : "source",
      isValidConnection: m,
      doc: T,
      lib: l,
      flowId: u,
      nodeLookup: d
    });
    O = Q.handleDomNode, W = Q.connection, L = Xp(!!k, Q.isValid);
    const le = d.get(r), de = le ? $t(le, K, te.Left, !0) : ee.from, he = {
      ...ee,
      from: de,
      isValid: L,
      to: Q.toHandle && L ? tn({ x: Q.toHandle.x, y: Q.toHandle.y }, q) : z,
      toHandle: Q.toHandle,
      toPosition: L && Q.toHandle ? Q.toHandle.position : vs[K.position],
      toNode: Q.toHandle ? d.get(Q.toHandle.nodeId) : null,
      pointer: z
    };
    p(he), ee = he;
  }
  function ae(oe) {
    if (!("touches" in oe && oe.touches.length > 0)) {
      if (C) {
        (k || O) && W && L && v?.(W);
        const { inProgress: q, ...Q } = ee, le = {
          ...Q,
          toPosition: ee.toHandle ? ee.toPosition : null
        };
        w?.(oe, le), i && b?.(oe, le);
      }
      g(), cancelAnimationFrame(_), $ = !1, L = !1, W = null, O = null, T.removeEventListener("mousemove", Z), T.removeEventListener("mouseup", ae), T.removeEventListener("touchmove", Z), T.removeEventListener("touchend", ae);
    }
  }
  T.addEventListener("mousemove", Z), T.addEventListener("mouseup", ae), T.addEventListener("touchmove", Z), T.addEventListener("touchend", ae);
}
function Bc(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: i, doc: s, lib: c, flowId: d, isValidConnection: l = Fc, nodeLookup: f }) {
  const u = i === "target", h = t ? s.querySelector(`.${c}-flow__handle[data-id="${d}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: g, y } = Ge(e), v = s.elementFromPoint(g, y), w = v?.classList.contains(`${c}-flow__handle`) ? v : h, m = {
    handleDomNode: w,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (w) {
    const b = Oc(void 0, w), p = w.getAttribute("data-nodeid"), x = w.getAttribute("data-handleid"), I = w.classList.contains("connectable"), E = w.classList.contains("connectableend");
    if (!p || !b)
      return m;
    const A = {
      source: u ? p : o,
      sourceHandle: u ? x : r,
      target: u ? o : p,
      targetHandle: u ? r : x
    };
    m.connection = A;
    const T = I && E && (n === Jt.Strict ? u && b === "source" || !u && b === "target" : p !== o || x !== r);
    m.isValid = T && l(A), m.toHandle = Hc(p, b, x, f, n, !0);
  }
  return m;
}
const fi = {
  onPointerDown: Yp,
  isValid: Bc
};
function qp({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = Be(e);
  function i({ translateExtent: c, width: d, height: l, zoomStep: f = 1, pannable: u = !0, zoomable: h = !0, inversePan: g = !1 }) {
    const y = (p) => {
      if (p.sourceEvent.type !== "wheel" || !t)
        return;
      const x = n(), I = p.sourceEvent.ctrlKey && Hn() ? 10 : 1, E = -p.sourceEvent.deltaY * (p.sourceEvent.deltaMode === 1 ? 0.05 : p.sourceEvent.deltaMode ? 1 : 2e-3) * f, A = x[2] * Math.pow(2, E * I);
      t.scaleTo(A);
    };
    let v = [0, 0];
    const w = (p) => {
      (p.sourceEvent.type === "mousedown" || p.sourceEvent.type === "touchstart") && (v = [
        p.sourceEvent.clientX ?? p.sourceEvent.touches[0].clientX,
        p.sourceEvent.clientY ?? p.sourceEvent.touches[0].clientY
      ]);
    }, m = (p) => {
      const x = n();
      if (p.sourceEvent.type !== "mousemove" && p.sourceEvent.type !== "touchmove" || !t)
        return;
      const I = [
        p.sourceEvent.clientX ?? p.sourceEvent.touches[0].clientX,
        p.sourceEvent.clientY ?? p.sourceEvent.touches[0].clientY
      ], E = [I[0] - v[0], I[1] - v[1]];
      v = I;
      const A = o() * Math.max(x[2], Math.log(x[2])) * (g ? -1 : 1), P = {
        x: x[0] - E[0] * A,
        y: x[1] - E[1] * A
      }, T = [
        [0, 0],
        [d, l]
      ];
      t.setViewportConstrained({
        x: P.x,
        y: P.y,
        zoom: x[2]
      }, T, c);
    }, b = yc().on("start", w).on("zoom", u ? m : null).on("zoom.wheel", h ? y : null);
    r.call(b, {});
  }
  function s() {
    r.on("zoom", null);
  }
  return {
    update: i,
    destroy: s,
    pointer: Ze
  };
}
const ar = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), Br = ({ x: e, y: t, zoom: n }) => rr.translate(e, t).scale(n), Wt = (e, t) => e.target.closest(`.${t}`), Wc = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Zp = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, Wr = (e, t = 0, n = Zp, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, Xc = (e) => {
  const t = e.ctrlKey && Hn() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Up({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: i, zoomOnPinch: s, onPanZoomStart: c, onPanZoom: d, onPanZoomEnd: l }) {
  return (f) => {
    if (Wt(f, t))
      return f.ctrlKey && f.preventDefault(), !1;
    f.preventDefault(), f.stopImmediatePropagation();
    const u = n.property("__zoom").k || 1;
    if (f.ctrlKey && s) {
      const w = Ze(f), m = Xc(f), b = u * Math.pow(2, m);
      o.scaleTo(n, b, w, f);
      return;
    }
    const h = f.deltaMode === 1 ? 20 : 1;
    let g = r === jt.Vertical ? 0 : f.deltaX * h, y = r === jt.Horizontal ? 0 : f.deltaY * h;
    !Hn() && f.shiftKey && r !== jt.Vertical && (g = f.deltaY * h, y = 0), o.translateBy(
      n,
      -(g / u) * i,
      -(y / u) * i,
      // @ts-ignore
      { internal: !0 }
    );
    const v = ar(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (d?.(f, v), e.panScrollTimeout = setTimeout(() => {
      l?.(f, v), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(f, v));
  };
}
function Kp({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const i = o.type === "wheel", s = !t && i && !o.ctrlKey, c = Wt(o, e);
    if (o.ctrlKey && i && c && o.preventDefault(), s || c)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function Gp({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = ar(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function Jp({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (i) => {
    e.usedRightMouseButton = !!(n && Wc(t, e.mouseButton ?? 0)), i.sourceEvent?.sync || o([i.transform.x, i.transform.y, i.transform.k]), r && !i.sourceEvent?.internal && r?.(i.sourceEvent, ar(i.transform));
  };
}
function Qp({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: i }) {
  return (s) => {
    if (!s.sourceEvent?.internal && (e.isZoomingOrPanning = !1, i && Wc(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && s.sourceEvent && i(s.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
      const c = ar(s.transform);
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
function eg({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: i, userSelectionActive: s, noWheelClassName: c, noPanClassName: d, lib: l, connectionInProgress: f }) {
  return (u) => {
    const h = e || t, g = n && u.ctrlKey, y = u.type === "wheel";
    if (u.button === 1 && u.type === "mousedown" && (Wt(u, `${l}-flow__node`) || Wt(u, `${l}-flow__edge`)))
      return !0;
    if (!o && !h && !r && !i && !n || s || f && !y || Wt(u, c) && y || Wt(u, d) && (!y || r && y && !e) || !n && u.ctrlKey && y)
      return !1;
    if (!n && u.type === "touchstart" && u.touches?.length > 1)
      return u.preventDefault(), !1;
    if (!h && !r && !g && y || !o && (u.type === "mousedown" || u.type === "touchstart") || Array.isArray(o) && !o.includes(u.button) && u.type === "mousedown")
      return !1;
    const v = Array.isArray(o) && o.includes(u.button) || !u.button || u.button <= 1;
    return (!u.ctrlKey || y) && v;
  };
}
function tg({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: i, onPanZoomStart: s, onPanZoomEnd: c, onDraggingChange: d }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, f = e.getBoundingClientRect(), u = yc().scaleExtent([t, n]).translateExtent(o), h = Be(e).call(u);
  b({
    x: r.x,
    y: r.y,
    zoom: Qt(r.zoom, t, n)
  }, [
    [0, 0],
    [f.width, f.height]
  ], o);
  const g = h.on("wheel.zoom"), y = h.on("dblclick.zoom");
  u.wheelDelta(Xc);
  async function v(k, R) {
    return h ? new Promise((F) => {
      u?.interpolate(R?.interpolate === "linear" ? An : Mo).transform(Wr(h, R?.duration, R?.ease, () => F(!0)), k);
    }) : !1;
  }
  function w({ noWheelClassName: k, noPanClassName: R, onPaneContextMenu: F, userSelectionActive: S, panOnScroll: j, panOnDrag: C, panOnScrollMode: M, panOnScrollSpeed: z, preventScrolling: $, zoomOnPinch: W, zoomOnScroll: L, zoomOnDoubleClick: O, zoomActivationKeyPressed: U, lib: K, onTransformChange: G, connectionInProgress: se, paneClickDistance: ee, selectionOnDrag: H }) {
    S && !l.isZoomingOrPanning && m();
    const Z = j && !U && !S;
    u.clickDistance(H ? 1 / 0 : !Ke(ee) || ee < 0 ? 0 : ee);
    const ae = Z ? Up({
      zoomPanValues: l,
      noWheelClassName: k,
      d3Selection: h,
      d3Zoom: u,
      panOnScrollMode: M,
      panOnScrollSpeed: z,
      zoomOnPinch: W,
      onPanZoomStart: s,
      onPanZoom: i,
      onPanZoomEnd: c
    }) : Kp({
      noWheelClassName: k,
      preventScrolling: $,
      d3ZoomHandler: g
    });
    h.on("wheel.zoom", ae, { passive: !1 });
    const oe = Gp({
      zoomPanValues: l,
      onDraggingChange: d,
      onPanZoomStart: s
    });
    u.on("start", oe);
    const q = Jp({
      zoomPanValues: l,
      panOnDrag: C,
      onPaneContextMenu: !!F,
      onPanZoom: i,
      onTransformChange: G
    });
    u.on("zoom", q);
    const Q = Qp({
      zoomPanValues: l,
      panOnDrag: C,
      panOnScroll: j,
      onPaneContextMenu: F,
      onPanZoomEnd: c,
      onDraggingChange: d
    });
    u.on("end", Q);
    const le = eg({
      zoomActivationKeyPressed: U,
      panOnDrag: C,
      zoomOnScroll: L,
      panOnScroll: j,
      zoomOnDoubleClick: O,
      zoomOnPinch: W,
      userSelectionActive: S,
      noPanClassName: R,
      noWheelClassName: k,
      lib: K,
      connectionInProgress: se
    });
    u.filter(le), O ? h.on("dblclick.zoom", y) : h.on("dblclick.zoom", null);
  }
  function m() {
    u.on("zoom", null);
  }
  async function b(k, R, F) {
    const S = Br(k), j = u?.constrain()(S, R, F);
    return j && await v(j), j;
  }
  async function p(k, R) {
    const F = Br(k);
    return await v(F, R), F;
  }
  function x(k) {
    if (h) {
      const R = Br(k), F = h.property("__zoom");
      (F.k !== k.zoom || F.x !== k.x || F.y !== k.y) && u?.transform(h, R, null, { sync: !0 });
    }
  }
  function I() {
    const k = h ? gc(h.node()) : { x: 0, y: 0, k: 1 };
    return { x: k.x, y: k.y, zoom: k.k };
  }
  async function E(k, R) {
    return h ? new Promise((F) => {
      u?.interpolate(R?.interpolate === "linear" ? An : Mo).scaleTo(Wr(h, R?.duration, R?.ease, () => F(!0)), k);
    }) : !1;
  }
  async function A(k, R) {
    return h ? new Promise((F) => {
      u?.interpolate(R?.interpolate === "linear" ? An : Mo).scaleBy(Wr(h, R?.duration, R?.ease, () => F(!0)), k);
    }) : !1;
  }
  function P(k) {
    u?.scaleExtent(k);
  }
  function T(k) {
    u?.translateExtent(k);
  }
  function _(k) {
    const R = !Ke(k) || k < 0 ? 0 : k;
    u?.clickDistance(R);
  }
  return {
    update: w,
    destroy: m,
    setViewport: p,
    setViewportConstrained: b,
    getViewport: I,
    scaleTo: E,
    scaleBy: A,
    setScaleExtent: P,
    setTranslateExtent: T,
    syncViewport: x,
    setClickDistance: _
  };
}
var nn;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(nn || (nn = {}));
function ng({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: i }) {
  const s = e - t, c = n - o, d = [s > 0 ? 1 : s < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return s && r && (d[0] = d[0] * -1), c && i && (d[1] = d[1] * -1), d;
}
function Ps(e) {
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
function $s(e, t) {
  return e ? !t : t;
}
function og(e, t, n, o, r, i, s, c) {
  let { affectsX: d, affectsY: l } = t;
  const { isHorizontal: f, isVertical: u } = t, h = f && u, { xSnapped: g, ySnapped: y } = n, { minWidth: v, maxWidth: w, minHeight: m, maxHeight: b } = o, { x: p, y: x, width: I, height: E, aspectRatio: A } = e;
  let P = Math.floor(f ? g - e.pointerX : 0), T = Math.floor(u ? y - e.pointerY : 0);
  const _ = I + (d ? -P : P), k = E + (l ? -T : T), R = -i[0] * I, F = -i[1] * E;
  let S = ko(_, v, w), j = ko(k, m, b);
  if (s) {
    let z = 0, $ = 0;
    d && P < 0 ? z = ht(p + P + R, s[0][0]) : !d && P > 0 && (z = pt(p + _ + R, s[1][0])), l && T < 0 ? $ = ht(x + T + F, s[0][1]) : !l && T > 0 && ($ = pt(x + k + F, s[1][1])), S = Math.max(S, z), j = Math.max(j, $);
  }
  if (c) {
    let z = 0, $ = 0;
    d && P > 0 ? z = pt(p + P, c[0][0]) : !d && P < 0 && (z = ht(p + _, c[1][0])), l && T > 0 ? $ = pt(x + T, c[0][1]) : !l && T < 0 && ($ = ht(x + k, c[1][1])), S = Math.max(S, z), j = Math.max(j, $);
  }
  if (r) {
    if (f) {
      const z = ko(_ / A, m, b) * A;
      if (S = Math.max(S, z), s) {
        let $ = 0;
        !d && !l || d && !l && h ? $ = pt(x + F + _ / A, s[1][1]) * A : $ = ht(x + F + (d ? P : -P) / A, s[0][1]) * A, S = Math.max(S, $);
      }
      if (c) {
        let $ = 0;
        !d && !l || d && !l && h ? $ = ht(x + _ / A, c[1][1]) * A : $ = pt(x + (d ? P : -P) / A, c[0][1]) * A, S = Math.max(S, $);
      }
    }
    if (u) {
      const z = ko(k * A, v, w) / A;
      if (j = Math.max(j, z), s) {
        let $ = 0;
        !d && !l || l && !d && h ? $ = pt(p + k * A + R, s[1][0]) / A : $ = ht(p + (l ? T : -T) * A + R, s[0][0]) / A, j = Math.max(j, $);
      }
      if (c) {
        let $ = 0;
        !d && !l || l && !d && h ? $ = ht(p + k * A, c[1][0]) / A : $ = pt(p + (l ? T : -T) * A, c[0][0]) / A, j = Math.max(j, $);
      }
    }
  }
  T = T + (T < 0 ? j : -j), P = P + (P < 0 ? S : -S), r && (h ? _ > k * A ? T = ($s(d, l) ? -P : P) / A : P = ($s(d, l) ? -T : T) * A : f ? (T = P / A, l = d) : (P = T * A, d = l));
  const C = d ? p + P : p, M = l ? x + T : x;
  return {
    width: I + (d ? -P : P),
    height: E + (l ? -T : T),
    x: i[0] * P * (d ? -1 : 1) + C,
    y: i[1] * T * (l ? -1 : 1) + M
  };
}
const Yc = { width: 0, height: 0, x: 0, y: 0 }, rg = {
  ...Yc,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function ig(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, i = e.measured.width ?? 0, s = e.measured.height ?? 0, c = n[0] * i, d = n[1] * s;
  return [
    [o - c, r - d],
    [o + i - c, r + s - d]
  ];
}
function sg({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const i = Be(e);
  let s = {
    controlDirection: Ps("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: l, boundaries: f, keepAspectRatio: u, resizeDirection: h, onResizeStart: g, onResize: y, onResizeEnd: v, shouldResize: w }) {
    let m = { ...Yc }, b = { ...rg };
    s = {
      boundaries: f,
      resizeDirection: h,
      keepAspectRatio: u,
      controlDirection: Ps(l)
    };
    let p, x = null, I = [], E, A, P, T = !1;
    const _ = tc().on("start", (k) => {
      const { nodeLookup: R, transform: F, snapGrid: S, snapToGrid: j, nodeOrigin: C, paneDomNode: M } = n();
      if (p = R.get(t), !p)
        return;
      x = M?.getBoundingClientRect() ?? null;
      const { xSnapped: z, ySnapped: $ } = Mn(k.sourceEvent, {
        transform: F,
        snapGrid: S,
        snapToGrid: j,
        containerBounds: x
      });
      m = {
        width: p.measured.width ?? 0,
        height: p.measured.height ?? 0,
        x: p.position.x ?? 0,
        y: p.position.y ?? 0
      }, b = {
        ...m,
        pointerX: z,
        pointerY: $,
        aspectRatio: m.width / m.height
      }, E = void 0, A = Pt(p.extent) ? p.extent : void 0, p.parentId && (p.extent === "parent" || p.expandParent) && (E = R.get(p.parentId)), E && p.extent === "parent" && (A = [
        [0, 0],
        [E.measured.width, E.measured.height]
      ]), I = [], P = void 0;
      for (const [W, L] of R)
        if (L.parentId === t && (I.push({
          id: W,
          position: { ...L.position },
          extent: L.extent
        }), L.extent === "parent" || L.expandParent)) {
          const O = ig(L, p, L.origin ?? C);
          P ? P = [
            [Math.min(O[0][0], P[0][0]), Math.min(O[0][1], P[0][1])],
            [Math.max(O[1][0], P[1][0]), Math.max(O[1][1], P[1][1])]
          ] : P = O;
        }
      g?.(k, { ...m });
    }).on("drag", (k) => {
      const { transform: R, snapGrid: F, snapToGrid: S, nodeOrigin: j } = n(), C = Mn(k.sourceEvent, {
        transform: R,
        snapGrid: F,
        snapToGrid: S,
        containerBounds: x
      }), M = [];
      if (!p)
        return;
      const { x: z, y: $, width: W, height: L } = m, O = {}, U = p.origin ?? j, { width: K, height: G, x: se, y: ee } = og(b, s.controlDirection, C, s.boundaries, s.keepAspectRatio, U, A, P), H = K !== W, Z = G !== L, ae = se !== z && H, oe = ee !== $ && Z;
      if (!ae && !oe && !H && !Z)
        return;
      if ((ae || oe || U[0] === 1 || U[1] === 1) && (O.x = ae ? se : m.x, O.y = oe ? ee : m.y, m.x = O.x, m.y = O.y, I.length > 0)) {
        const de = se - z, he = ee - $;
        for (const ke of I)
          ke.position = {
            x: ke.position.x - de + U[0] * (K - W),
            y: ke.position.y - he + U[1] * (G - L)
          }, M.push(ke);
      }
      if ((H || Z) && (O.width = H && (!s.resizeDirection || s.resizeDirection === "horizontal") ? K : m.width, O.height = Z && (!s.resizeDirection || s.resizeDirection === "vertical") ? G : m.height, m.width = O.width, m.height = O.height), E && p.expandParent) {
        const de = U[0] * (O.width ?? 0);
        O.x && O.x < de && (m.x = de, b.x = b.x - (O.x - de));
        const he = U[1] * (O.height ?? 0);
        O.y && O.y < he && (m.y = he, b.y = b.y - (O.y - he));
      }
      const q = ng({
        width: m.width,
        prevWidth: W,
        height: m.height,
        prevHeight: L,
        affectsX: s.controlDirection.affectsX,
        affectsY: s.controlDirection.affectsY
      }), Q = { ...m, direction: q };
      w?.(k, Q) !== !1 && (T = !0, y?.(k, Q), o(O, M));
    }).on("end", (k) => {
      T && (v?.(k, { ...m }), r?.({ ...m }), T = !1);
    });
    i.call(_);
  }
  function d() {
    i.on(".drag", null);
  }
  return {
    update: c,
    destroy: d
  };
}
var Xr = { exports: {} }, Yr = {}, qr = { exports: {} }, Zr = {};
var Ts;
function ag() {
  if (Ts) return Zr;
  Ts = 1;
  var e = wt;
  function t(u, h) {
    return u === h && (u !== 0 || 1 / u === 1 / h) || u !== u && h !== h;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, i = e.useLayoutEffect, s = e.useDebugValue;
  function c(u, h) {
    var g = h(), y = o({ inst: { value: g, getSnapshot: h } }), v = y[0].inst, w = y[1];
    return i(
      function() {
        v.value = g, v.getSnapshot = h, d(v) && w({ inst: v });
      },
      [u, g, h]
    ), r(
      function() {
        return d(v) && w({ inst: v }), u(function() {
          d(v) && w({ inst: v });
        });
      },
      [u]
    ), s(g), g;
  }
  function d(u) {
    var h = u.getSnapshot;
    u = u.value;
    try {
      var g = h();
      return !n(u, g);
    } catch {
      return !0;
    }
  }
  function l(u, h) {
    return h();
  }
  var f = typeof window > "u" || typeof window.document > "u" || typeof window.document.createElement > "u" ? l : c;
  return Zr.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : f, Zr;
}
var zs;
function cg() {
  return zs || (zs = 1, qr.exports = ag()), qr.exports;
}
var Rs;
function lg() {
  if (Rs) return Yr;
  Rs = 1;
  var e = wt, t = cg();
  function n(l, f) {
    return l === f && (l !== 0 || 1 / l === 1 / f) || l !== l && f !== f;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, i = e.useRef, s = e.useEffect, c = e.useMemo, d = e.useDebugValue;
  return Yr.useSyncExternalStoreWithSelector = function(l, f, u, h, g) {
    var y = i(null);
    if (y.current === null) {
      var v = { hasValue: !1, value: null };
      y.current = v;
    } else v = y.current;
    y = c(
      function() {
        function m(E) {
          if (!b) {
            if (b = !0, p = E, E = h(E), g !== void 0 && v.hasValue) {
              var A = v.value;
              if (g(A, E))
                return x = A;
            }
            return x = E;
          }
          if (A = x, o(p, E)) return A;
          var P = h(E);
          return g !== void 0 && g(A, P) ? (p = E, A) : (p = E, x = P);
        }
        var b = !1, p, x, I = u === void 0 ? null : u;
        return [
          function() {
            return m(f());
          },
          I === null ? void 0 : function() {
            return m(I());
          }
        ];
      },
      [f, u, h, g]
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
var Ls;
function dg() {
  return Ls || (Ls = 1, Xr.exports = lg()), Xr.exports;
}
var ug = dg();
const fg = /* @__PURE__ */ Cd(ug), hg = {}, Vs = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (f, u) => {
    const h = typeof f == "function" ? f(t) : f;
    if (!Object.is(h, t)) {
      const g = t;
      t = u ?? (typeof h != "object" || h === null) ? h : Object.assign({}, t, h), n.forEach((y) => y(t, g));
    }
  }, r = () => t, d = { setState: o, getState: r, getInitialState: () => l, subscribe: (f) => (n.add(f), () => n.delete(f)), destroy: () => {
    (hg ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(o, r, d);
  return d;
}, pg = (e) => e ? Vs(e) : Vs, { useDebugValue: gg } = wt, { useSyncExternalStoreWithSelector: yg } = fg, mg = (e) => e;
function qc(e, t = mg, n) {
  const o = yg(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return gg(o), o;
}
const Hs = (e, t) => {
  const n = pg(e), o = (r, i = t) => qc(n, r, i);
  return Object.assign(o, n), o;
}, xg = (e, t) => e ? Hs(e, t) : Hs;
function ve(e, t) {
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
var Ur = { exports: {} }, Re = {};
var Os;
function wg() {
  if (Os) return Re;
  Os = 1;
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
  return Re.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, Re.createPortal = function(d, l) {
    var f = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!l || l.nodeType !== 1 && l.nodeType !== 9 && l.nodeType !== 11)
      throw Error(t(299));
    return i(d, l, null, f);
  }, Re.flushSync = function(d) {
    var l = s.T, f = o.p;
    try {
      if (s.T = null, o.p = 2, d) return d();
    } finally {
      s.T = l, o.p = f, o.d.f();
    }
  }, Re.preconnect = function(d, l) {
    typeof d == "string" && (l ? (l = l.crossOrigin, l = typeof l == "string" ? l === "use-credentials" ? l : "" : void 0) : l = null, o.d.C(d, l));
  }, Re.prefetchDNS = function(d) {
    typeof d == "string" && o.d.D(d);
  }, Re.preinit = function(d, l) {
    if (typeof d == "string" && l && typeof l.as == "string") {
      var f = l.as, u = c(f, l.crossOrigin), h = typeof l.integrity == "string" ? l.integrity : void 0, g = typeof l.fetchPriority == "string" ? l.fetchPriority : void 0;
      f === "style" ? o.d.S(
        d,
        typeof l.precedence == "string" ? l.precedence : void 0,
        {
          crossOrigin: u,
          integrity: h,
          fetchPriority: g
        }
      ) : f === "script" && o.d.X(d, {
        crossOrigin: u,
        integrity: h,
        fetchPriority: g,
        nonce: typeof l.nonce == "string" ? l.nonce : void 0
      });
    }
  }, Re.preinitModule = function(d, l) {
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
  }, Re.preload = function(d, l) {
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
  }, Re.preloadModule = function(d, l) {
    if (typeof d == "string")
      if (l) {
        var f = c(l.as, l.crossOrigin);
        o.d.m(d, {
          as: typeof l.as == "string" && l.as !== "script" ? l.as : void 0,
          crossOrigin: f,
          integrity: typeof l.integrity == "string" ? l.integrity : void 0
        });
      } else o.d.m(d);
  }, Re.requestFormReset = function(d) {
    o.d.r(d);
  }, Re.unstable_batchedUpdates = function(d, l) {
    return d(l);
  }, Re.useFormState = function(d, l, f) {
    return s.H.useFormState(d, l, f);
  }, Re.useFormStatus = function() {
    return s.H.useHostTransitionStatus();
  }, Re.version = "19.2.7", Re;
}
var Fs;
function vg() {
  if (Fs) return Ur.exports;
  Fs = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), Ur.exports = wg(), Ur.exports;
}
var bg = vg();
const cr = vi(null), Ng = cr.Provider, Zc = Xe.error001("react");
function fe(e, t) {
  const n = Bn(cr);
  if (n === null)
    throw new Error(Zc);
  return qc(n, e, t);
}
function be() {
  const e = Bn(cr);
  if (e === null)
    throw new Error(Zc);
  return me(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const Bs = { display: "none" }, Sg = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, Uc = "react-flow__node-desc", Kc = "react-flow__edge-desc", Eg = "react-flow__aria-live", kg = (e) => e.ariaLiveMessage, Ig = (e) => e.ariaLabelConfig;
function Cg({ rfId: e }) {
  const t = fe(kg);
  return a.jsx("div", { id: `${Eg}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: Sg, children: t });
}
function jg({ rfId: e, disableKeyboardA11y: t }) {
  const n = fe(Ig);
  return a.jsxs(a.Fragment, { children: [a.jsx("div", { id: `${Uc}-${e}`, style: Bs, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), a.jsx("div", { id: `${Kc}-${e}`, style: Bs, children: n["edge.a11yDescription.default"] }), !t && a.jsx(Cg, { rfId: e })] });
}
const lr = Qo(({ position: e = "top-left", children: t, className: n, style: o, ...r }, i) => {
  const s = `${e}`.split("-");
  return a.jsx("div", { className: Ae(["react-flow__panel", n, ...s]), style: o, ref: i, ...r, children: t });
});
lr.displayName = "Panel";
function _g({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : a.jsx(lr, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: a.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const Ag = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, Io = (e) => e.id;
function Mg(e, t) {
  return ve(e.selectedNodes.map(Io), t.selectedNodes.map(Io)) && ve(e.selectedEdges.map(Io), t.selectedEdges.map(Io));
}
function Dg({ onSelectionChange: e }) {
  const t = be(), { selectedNodes: n, selectedEdges: o } = fe(Ag, Mg);
  return ne(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((i) => i(r));
  }, [n, o, e]), null;
}
const Pg = (e) => !!e.onSelectionChangeHandlers;
function $g({ onSelectionChange: e }) {
  const t = fe(Pg);
  return e || t ? a.jsx(Dg, { onSelectionChange: e }) : null;
}
const Gc = [0, 0], Tg = { x: 0, y: 0, zoom: 1 }, zg = [
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
], Ws = [...zg, "rfId"], Rg = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), Xs = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: Rn,
  nodeOrigin: Gc,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function Lg(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: i, setNodeExtent: s, reset: c, setDefaultNodesAndEdges: d } = fe(Rg, ve), l = be();
  ne(() => (d(e.defaultNodes, e.defaultEdges), () => {
    f.current = Xs, c();
  }), []);
  const f = ce(Xs);
  return ne(
    () => {
      for (const u of Ws) {
        const h = e[u], g = f.current[u];
        h !== g && (typeof e[u] > "u" || (u === "nodes" ? t(h) : u === "edges" ? n(h) : u === "minZoom" ? o(h) : u === "maxZoom" ? r(h) : u === "translateExtent" ? i(h) : u === "nodeExtent" ? s(h) : u === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: wp(h) }) : u === "fitView" ? l.setState({ fitViewQueued: h }) : u === "fitViewOptions" ? l.setState({ fitViewOptions: h }) : l.setState({ [u]: h })));
      }
      f.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    Ws.map((u) => e[u])
  ), null;
}
function Ys() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function Vg(e) {
  const [t, n] = Y(e === "system" ? null : e);
  return ne(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = Ys(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : Ys()?.matches ? "dark" : "light";
}
const qs = typeof document < "u" ? document : null;
function On(e = null, t = { target: qs, actInsideInputWithModifier: !0 }) {
  const [n, o] = Y(!1), r = ce(!1), i = ce(/* @__PURE__ */ new Set([])), [s, c] = me(() => {
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
  return ne(() => {
    const d = t?.target ?? qs, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const f = (g) => {
        if (r.current = g.ctrlKey || g.metaKey || g.shiftKey || g.altKey, (!r.current || r.current && !l) && _c(g))
          return !1;
        const v = Us(g.code, c);
        if (i.current.add(g[v]), Zs(s, i.current, !1)) {
          const w = g.composedPath?.()?.[0] || g.target, m = w?.nodeName === "BUTTON" || w?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !m) && g.preventDefault(), o(!0);
        }
      }, u = (g) => {
        const y = Us(g.code, c);
        Zs(s, i.current, !0) ? (o(!1), i.current.clear()) : i.current.delete(g[y]), g.key === "Meta" && i.current.clear(), r.current = !1;
      }, h = () => {
        i.current.clear(), o(!1);
      };
      return d?.addEventListener("keydown", f), d?.addEventListener("keyup", u), window.addEventListener("blur", h), window.addEventListener("contextmenu", h), () => {
        d?.removeEventListener("keydown", f), d?.removeEventListener("keyup", u), window.removeEventListener("blur", h), window.removeEventListener("contextmenu", h);
      };
    }
  }, [e, o]), n;
}
function Zs(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function Us(e, t) {
  return t.includes(e) ? "code" : "key";
}
const Hg = () => {
  const e = be();
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
      }, f = n.snapGrid ?? r, u = n.snapToGrid ?? i;
      return rn(l, o, u, f);
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
function Jc(e, t) {
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
      Og(d, c);
    n.push(c);
  }
  return r.length && r.forEach((i) => {
    i.index !== void 0 ? n.splice(i.index, 0, { ...i.item }) : n.push({ ...i.item });
  }), n;
}
function Og(e, t) {
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
function Qc(e, t) {
  return Jc(e, t);
}
function el(e, t) {
  return Jc(e, t);
}
function kt(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function Xt(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [r, i] of e) {
    const s = t.has(r);
    !(i.selected === void 0 && !s) && i.selected !== s && (n && (i.selected = s), o.push(kt(i.id, s)));
  }
  return o;
}
function Ks({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((r) => [r.id, r]));
  for (const [r, i] of e.entries()) {
    const s = t.get(i.id), c = s?.internals?.userNode ?? s;
    c !== void 0 && c !== i && n.push({ id: i.id, item: i, type: "replace" }), c === void 0 && n.push({ item: i, type: "add", index: r });
  }
  for (const [r] of t)
    o.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function Gs(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const tl = kc();
function nl(e, t, n = {}) {
  return Ep(e, t, {
    ...n,
    onError: n.onError ?? tl
  });
}
function Fg(e, t, n, o = { shouldReplaceId: !0 }) {
  return kp(e, t, n, {
    ...o,
    onError: o.onError ?? tl
  });
}
const Js = (e) => dp(e), Bg = (e) => bc(e);
function ol(e) {
  return Qo(e);
}
const Wg = typeof window < "u" ? Id : ne;
function Qs(e) {
  const [t, n] = Y(BigInt(0)), [o] = Y(() => Xg(() => n((r) => r + BigInt(1))));
  return Wg(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function Xg(e) {
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
const rl = vi(null);
function Yg({ children: e }) {
  const t = be(), n = ye((c) => {
    const { nodes: d = [], setNodes: l, hasDefaultNodes: f, onNodesChange: u, nodeLookup: h, fitViewQueued: g, onNodesChangeMiddlewareMap: y } = t.getState();
    let v = d;
    for (const m of c)
      v = typeof m == "function" ? m(v) : m;
    let w = Ks({
      items: v,
      lookup: h
    });
    for (const m of y.values())
      w = m(w);
    f && l(v), w.length > 0 ? u?.(w) : g && window.requestAnimationFrame(() => {
      const { fitViewQueued: m, nodes: b, setNodes: p } = t.getState();
      m && p(b);
    });
  }, []), o = Qs(n), r = ye((c) => {
    const { edges: d = [], setEdges: l, hasDefaultEdges: f, onEdgesChange: u, edgeLookup: h } = t.getState();
    let g = d;
    for (const y of c)
      g = typeof y == "function" ? y(g) : y;
    f ? l(g) : u && u(Ks({
      items: g,
      lookup: h
    }));
  }, []), i = Qs(r), s = me(() => ({ nodeQueue: o, edgeQueue: i }), []);
  return a.jsx(rl.Provider, { value: s, children: e });
}
function qg() {
  const e = Bn(rl);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Zg = (e) => !!e.panZoom;
function Li() {
  const e = Hg(), t = be(), n = qg(), o = fe(Zg), r = me(() => {
    const i = (u) => t.getState().nodeLookup.get(u), s = (u) => {
      n.nodeQueue.push(u);
    }, c = (u) => {
      n.edgeQueue.push(u);
    }, d = (u) => {
      const { nodeLookup: h, nodeOrigin: g } = t.getState(), y = Js(u) ? u : h.get(u.id), v = y.parentId ? Cc(y.position, y.measured, y.parentId, h, g) : y.position, w = {
        ...y,
        position: v,
        width: y.measured?.width ?? y.width,
        height: y.measured?.height ?? y.height
      };
      return en(w);
    }, l = (u, h, g = { replace: !1 }) => {
      s((y) => y.map((v) => {
        if (v.id === u) {
          const w = typeof h == "function" ? h(v) : h;
          return g.replace && Js(w) ? w : { ...v, ...w };
        }
        return v;
      }));
    }, f = (u, h, g = { replace: !1 }) => {
      c((y) => y.map((v) => {
        if (v.id === u) {
          const w = typeof h == "function" ? h(v) : h;
          return g.replace && Bg(w) ? w : { ...v, ...w };
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
        n.nodeQueue.push((g) => [...g, ...h]);
      },
      addEdges: (u) => {
        const h = Array.isArray(u) ? u : [u];
        n.edgeQueue.push((g) => [...g, ...h]);
      },
      toObject: () => {
        const { nodes: u = [], edges: h = [], transform: g } = t.getState(), [y, v, w] = g;
        return {
          nodes: u.map((m) => ({ ...m })),
          edges: h.map((m) => ({ ...m })),
          viewport: {
            x: y,
            y: v,
            zoom: w
          }
        };
      },
      deleteElements: async ({ nodes: u = [], edges: h = [] }) => {
        const { nodes: g, edges: y, onNodesDelete: v, onEdgesDelete: w, triggerNodeChanges: m, triggerEdgeChanges: b, onDelete: p, onBeforeDelete: x } = t.getState(), { nodes: I, edges: E } = await gp({
          nodesToRemove: u,
          edgesToRemove: h,
          nodes: g,
          edges: y,
          onBeforeDelete: x
        }), A = E.length > 0, P = I.length > 0;
        if (A) {
          const T = E.map(Gs);
          w?.(E), b(T);
        }
        if (P) {
          const T = I.map(Gs);
          v?.(I), m(T);
        }
        return (P || A) && p?.({ nodes: I, edges: E }), { deletedNodes: I, deletedEdges: E };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (u, h = !0, g) => {
        const y = Ns(u), v = y ? u : d(u), w = g !== void 0;
        return v ? (g || t.getState().nodes).filter((m) => {
          const b = t.getState().nodeLookup.get(m.id);
          if (b && !y && (m.id === u.id || !b.internals.positionAbsolute))
            return !1;
          const p = en(w ? m : b), x = Vn(p, v);
          return h && x > 0 || x >= p.width * p.height || x >= v.width * v.height;
        }) : [];
      },
      isNodeIntersecting: (u, h, g = !0) => {
        const v = Ns(u) ? u : d(u);
        if (!v)
          return !1;
        const w = Vn(v, h);
        return g && w > 0 || w >= h.width * h.height || w >= v.width * v.height;
      },
      updateNode: l,
      updateNodeData: (u, h, g = { replace: !1 }) => {
        l(u, (y) => {
          const v = typeof h == "function" ? h(y) : h;
          return g.replace ? { ...y, data: v } : { ...y, data: { ...y.data, ...v } };
        }, g);
      },
      updateEdge: f,
      updateEdgeData: (u, h, g = { replace: !1 }) => {
        f(u, (y) => {
          const v = typeof h == "function" ? h(y) : h;
          return g.replace ? { ...y, data: v } : { ...y, data: { ...y.data, ...v } };
        }, g);
      },
      getNodesBounds: (u) => {
        const { nodeLookup: h, nodeOrigin: g } = t.getState();
        return up(u, { nodeLookup: h, nodeOrigin: g });
      },
      getHandleConnections: ({ type: u, id: h, nodeId: g }) => Array.from(t.getState().connectionLookup.get(`${g}-${u}${h ? `-${h}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: u, handleId: h, nodeId: g }) => Array.from(t.getState().connectionLookup.get(`${g}${u ? h ? `-${u}-${h}` : `-${u}` : ""}`)?.values() ?? []),
      fitView: async (u) => {
        const h = t.getState().fitViewResolver ?? xp();
        return t.setState({ fitViewQueued: !0, fitViewOptions: u, fitViewResolver: h }), n.nodeQueue.push((g) => [...g]), h.promise;
      }
    };
  }, []);
  return me(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const ea = (e) => e.selected, Ug = typeof window < "u" ? window : void 0;
function Kg({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = be(), { deleteElements: o } = Li(), r = On(e, { actInsideInputWithModifier: !1 }), i = On(t, { target: Ug });
  ne(() => {
    if (r) {
      const { edges: s, nodes: c } = n.getState();
      o({ nodes: c.filter(ea), edges: s.filter(ea) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), ne(() => {
    n.setState({ multiSelectionActive: i });
  }, [i]);
}
function Gg(e) {
  const t = be();
  ne(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = Di(e.current);
      (o.height === 0 || o.width === 0) && t.getState().onError?.("004", Xe.error004()), t.setState({ width: o.width || 500, height: o.height || 500 });
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
const dr = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, Jg = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function Qg({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: i = jt.Free, zoomOnDoubleClick: s = !0, panOnDrag: c = !0, defaultViewport: d, translateExtent: l, minZoom: f, maxZoom: u, zoomActivationKeyCode: h, preventScrolling: g = !0, children: y, noWheelClassName: v, noPanClassName: w, onViewportChange: m, isControlledViewport: b, paneClickDistance: p, selectionOnDrag: x }) {
  const I = be(), E = ce(null), { userSelectionActive: A, lib: P, connectionInProgress: T } = fe(Jg, ve), _ = On(h), k = ce();
  Gg(E);
  const R = ye((F) => {
    m?.({ x: F[0], y: F[1], zoom: F[2] }), b || I.setState({ transform: F });
  }, [m, b]);
  return ne(() => {
    if (E.current) {
      k.current = tg({
        domNode: E.current,
        minZoom: f,
        maxZoom: u,
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
      const { x: F, y: S, zoom: j } = k.current.getViewport();
      return I.setState({
        panZoom: k.current,
        transform: [F, S, j],
        domNode: E.current.closest(".react-flow")
      }), () => {
        k.current?.destroy();
      };
    }
  }, []), ne(() => {
    k.current?.update({
      onPaneContextMenu: e,
      zoomOnScroll: t,
      zoomOnPinch: n,
      panOnScroll: o,
      panOnScrollSpeed: r,
      panOnScrollMode: i,
      zoomOnDoubleClick: s,
      panOnDrag: c,
      zoomActivationKeyPressed: _,
      preventScrolling: g,
      noPanClassName: w,
      userSelectionActive: A,
      noWheelClassName: v,
      lib: P,
      onTransformChange: R,
      connectionInProgress: T,
      selectionOnDrag: x,
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
    _,
    g,
    w,
    A,
    v,
    P,
    R,
    T,
    x,
    p
  ]), a.jsx("div", { className: "react-flow__renderer", ref: E, style: dr, children: y });
}
const ey = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function ty() {
  const { userSelectionActive: e, userSelectionRect: t } = fe(ey, ve);
  return e && t ? a.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const Kr = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, ny = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function oy({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Ln.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: i, selectionOnDrag: s, onSelectionStart: c, onSelectionEnd: d, onPaneClick: l, onPaneContextMenu: f, onPaneScroll: u, onPaneMouseEnter: h, onPaneMouseMove: g, onPaneMouseLeave: y, children: v }) {
  const w = ce(0), m = be(), { userSelectionActive: b, elementsSelectable: p, dragging: x, connectionInProgress: I, panBy: E, autoPanSpeed: A } = fe(ny, ve), P = p && (e || b), T = ce(null), _ = ce(), k = ce(/* @__PURE__ */ new Set()), R = ce(/* @__PURE__ */ new Set()), F = ce(!1), S = ce({ x: 0, y: 0 }), j = ce(!1), C = (H) => {
    if (F.current || I) {
      F.current = !1;
      return;
    }
    l?.(H), m.getState().resetSelectedElements(), m.setState({ nodesSelectionActive: !1 });
  }, M = (H) => {
    if (Array.isArray(o) && o?.includes(2)) {
      H.preventDefault();
      return;
    }
    f?.(H);
  }, z = u ? (H) => u(H) : void 0, $ = (H) => {
    F.current && (H.stopPropagation(), F.current = !1);
  }, W = (H) => {
    const { domNode: Z, transform: ae } = m.getState();
    if (_.current = Z?.getBoundingClientRect(), !_.current)
      return;
    const oe = H.target === T.current;
    if (!oe && !!H.target.closest(".nokey") || !e || !(s && oe || t) || H.button !== 0 || !H.isPrimary)
      return;
    H.target?.setPointerCapture?.(H.pointerId), F.current = !1;
    const { x: le, y: de } = Ge(H.nativeEvent, _.current), he = rn({ x: le, y: de }, ae);
    m.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: he.x,
        startY: he.y,
        x: le,
        y: de
      }
    }), oe || (H.stopPropagation(), H.preventDefault());
  };
  function L(H, Z) {
    const { userSelectionRect: ae } = m.getState();
    if (!ae)
      return;
    const { transform: oe, nodeLookup: q, edgeLookup: Q, connectionLookup: le, triggerNodeChanges: de, triggerEdgeChanges: he, defaultEdgeOptions: ke } = m.getState(), Ie = { x: ae.startX, y: ae.startY }, { x: Pe, y: ge } = tn(Ie, oe), $e = {
      startX: Ie.x,
      startY: Ie.y,
      x: H < Pe ? H : Pe,
      y: Z < ge ? Z : ge,
      width: Math.abs(H - Pe),
      height: Math.abs(Z - ge)
    }, B = k.current, re = R.current;
    k.current = new Set(_i(q, $e, oe, n === Ln.Partial, !0).map((we) => we.id)), R.current = /* @__PURE__ */ new Set();
    const xe = ke?.selectable ?? !0;
    for (const we of k.current) {
      const je = le.get(we);
      if (je)
        for (const { edgeId: Le } of je.values()) {
          const Ye = Q.get(Le);
          Ye && (Ye.selectable ?? xe) && R.current.add(Le);
        }
    }
    if (!Ss(B, k.current)) {
      const we = Xt(q, k.current, !0);
      de(we);
    }
    if (!Ss(re, R.current)) {
      const we = Xt(Q, R.current);
      he(we);
    }
    m.setState({
      userSelectionRect: $e,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function O() {
    if (!r || !_.current)
      return;
    const [H, Z] = Ai(S.current, _.current, A);
    E({ x: H, y: Z }).then((ae) => {
      if (!F.current || !ae) {
        w.current = requestAnimationFrame(O);
        return;
      }
      const { x: oe, y: q } = S.current;
      L(oe, q), w.current = requestAnimationFrame(O);
    });
  }
  const U = () => {
    cancelAnimationFrame(w.current), w.current = 0, j.current = !1;
  };
  ne(() => () => U(), []);
  const K = (H) => {
    const { userSelectionRect: Z, transform: ae, resetSelectedElements: oe } = m.getState();
    if (!_.current || !Z)
      return;
    const { x: q, y: Q } = Ge(H.nativeEvent, _.current);
    S.current = { x: q, y: Q };
    const le = tn({ x: Z.startX, y: Z.startY }, ae);
    if (!F.current) {
      const de = t ? 0 : i;
      if (Math.hypot(q - le.x, Q - le.y) <= de)
        return;
      oe(), c?.(H);
    }
    F.current = !0, j.current || (O(), j.current = !0), L(q, Q);
  }, G = (H) => {
    H.button === 0 && (H.target?.releasePointerCapture?.(H.pointerId), !b && H.target === T.current && m.getState().userSelectionRect && C?.(H), m.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), F.current && (d?.(H), m.setState({
      nodesSelectionActive: k.current.size > 0
    })), U());
  }, se = (H) => {
    H.target?.releasePointerCapture?.(H.pointerId), U();
  }, ee = o === !0 || Array.isArray(o) && o.includes(0);
  return a.jsxs("div", { className: Ae(["react-flow__pane", { draggable: ee, dragging: x, selection: e }]), onClick: P ? void 0 : Kr(C, T), onContextMenu: Kr(M, T), onWheel: Kr(z, T), onPointerEnter: P ? void 0 : h, onPointerMove: P ? K : g, onPointerUp: P ? G : void 0, onPointerCancel: P ? se : void 0, onPointerDownCapture: P ? W : void 0, onClickCapture: P ? $ : void 0, onPointerLeave: y, ref: T, style: dr, children: [v, a.jsx(ty, {})] });
}
function hi({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: i, multiSelectionActive: s, nodeLookup: c, onError: d } = t.getState(), l = c.get(e);
  if (!l) {
    d?.("012", Xe.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && s) && (i({ nodes: [l], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function il({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: i, nodeClickDistance: s }) {
  const c = be(), [d, l] = Y(!1), f = ce();
  return ne(() => {
    f.current = Op({
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
  }, []), ne(() => {
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
const ry = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function sl() {
  const e = be();
  return ye((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: i, nodesDraggable: s, onError: c, updateNodePositions: d, nodeLookup: l, nodeOrigin: f } = e.getState(), u = /* @__PURE__ */ new Map(), h = ry(s), g = r ? i[0] : 5, y = r ? i[1] : 5, v = n.direction.x * g * n.factor, w = n.direction.y * y * n.factor;
    for (const [, m] of l) {
      if (!h(m))
        continue;
      let b = {
        x: m.internals.positionAbsolute.x + v,
        y: m.internals.positionAbsolute.y + w
      };
      r && (b = Zn(b, i));
      const { position: p, positionAbsolute: x } = Nc({
        nodeId: m.id,
        nextPosition: b,
        nodeLookup: l,
        nodeExtent: o,
        nodeOrigin: f,
        onError: c
      });
      m.position = p, m.internals.positionAbsolute = x, u.set(m.id, m);
    }
    d(u);
  }, []);
}
const Vi = vi(null), iy = Vi.Provider;
Vi.Consumer;
const al = () => Bn(Vi), sy = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), ay = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: i, connection: s } = o, { fromHandle: c, toHandle: d, isValid: l } = s, f = d?.nodeId === e && d?.id === t && d?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: f,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: i === Jt.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!r,
    valid: f && l
  };
};
function cy({ type: e = "source", position: t = te.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: i = !0, id: s, onConnect: c, children: d, className: l, onMouseDown: f, onTouchStart: u, ...h }, g) {
  const y = s || null, v = e === "target", w = be(), m = al(), { connectOnClick: b, noPanClassName: p, rfId: x } = fe(sy, ve), { connectingFrom: I, connectingTo: E, clickConnecting: A, isPossibleEndHandle: P, connectionInProcess: T, clickConnectionInProcess: _, valid: k } = fe(ay(m, y, e), ve);
  m || w.getState().onError?.("010", Xe.error010());
  const R = (j) => {
    const { defaultEdgeOptions: C, onConnect: M, hasDefaultEdges: z } = w.getState(), $ = {
      ...C,
      ...j
    };
    if (z) {
      const { edges: W, setEdges: L, onError: O } = w.getState();
      L(nl($, W, { onError: O }));
    }
    M?.($), c?.($);
  }, F = (j) => {
    if (!m)
      return;
    const C = Ac(j.nativeEvent);
    if (r && (C && j.button === 0 || !C)) {
      const M = w.getState();
      fi.onPointerDown(j.nativeEvent, {
        handleDomNode: j.currentTarget,
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
    C ? f?.(j) : u?.(j);
  }, S = (j) => {
    const { onClickConnectStart: C, onClickConnectEnd: M, connectionClickStartHandle: z, connectionMode: $, isValidConnection: W, lib: L, rfId: O, nodeLookup: U, connection: K } = w.getState();
    if (!m || !z && !r)
      return;
    if (!z) {
      C?.(j.nativeEvent, { nodeId: m, handleId: y, handleType: e }), w.setState({ connectionClickStartHandle: { nodeId: m, type: e, id: y } });
      return;
    }
    const G = jc(j.target), se = n || W, { connection: ee, isValid: H } = fi.isValid(j.nativeEvent, {
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
      flowId: O,
      doc: G,
      lib: L,
      nodeLookup: U
    });
    H && ee && R(ee);
    const Z = structuredClone(K);
    delete Z.inProgress, Z.toPosition = Z.toHandle ? Z.toHandle.position : null, M?.(j, Z), w.setState({ connectionClickStartHandle: null });
  };
  return a.jsx("div", { "data-handleid": y, "data-nodeid": m, "data-handlepos": t, "data-id": `${x}-${m}-${y}-${e}`, className: Ae([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    p,
    l,
    {
      source: !v,
      target: v,
      connectable: o,
      connectablestart: r,
      connectableend: i,
      clickconnecting: A,
      connectingfrom: I,
      connectingto: E,
      valid: k,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!T || P) && (T || _ ? i : r)
    }
  ]), onMouseDown: F, onTouchStart: F, onClick: b ? S : void 0, ref: g, ...h, children: d });
}
const on = Ce(ol(cy));
function ly({ data: e, isConnectable: t, sourcePosition: n = te.Bottom }) {
  return a.jsxs(a.Fragment, { children: [e?.label, a.jsx(on, { type: "source", position: n, isConnectable: t })] });
}
function dy({ data: e, isConnectable: t, targetPosition: n = te.Top, sourcePosition: o = te.Bottom }) {
  return a.jsxs(a.Fragment, { children: [a.jsx(on, { type: "target", position: n, isConnectable: t }), e?.label, a.jsx(on, { type: "source", position: o, isConnectable: t })] });
}
function uy() {
  return null;
}
function fy({ data: e, isConnectable: t, targetPosition: n = te.Top }) {
  return a.jsxs(a.Fragment, { children: [a.jsx(on, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const Zo = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, ta = {
  input: ly,
  default: dy,
  output: fy,
  group: uy
};
function hy(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const py = (e) => {
  const { width: t, height: n, x: o, y: r } = qn(e.nodeLookup, {
    filter: (i) => !!i.selected
  });
  return {
    width: Ke(t) ? t : null,
    height: Ke(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function gy({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = be(), { width: r, height: i, transformString: s, userSelectionActive: c } = fe(py, ve), d = sl(), l = ce(null);
  ne(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const f = !c && r !== null && i !== null;
  if (il({
    nodeRef: l,
    disabled: !f
  }), !f)
    return null;
  const u = e ? (g) => {
    const y = o.getState().nodes.filter((v) => v.selected);
    e(g, y);
  } : void 0, h = (g) => {
    Object.prototype.hasOwnProperty.call(Zo, g.key) && (g.preventDefault(), d({
      direction: Zo[g.key],
      factor: g.shiftKey ? 4 : 1
    }));
  };
  return a.jsx("div", { className: Ae(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: s
  }, children: a.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: u, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : h, style: {
    width: r,
    height: i
  } }) });
}
const na = typeof window < "u" ? window : void 0, yy = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function cl({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, paneClickDistance: c, deleteKeyCode: d, selectionKeyCode: l, selectionOnDrag: f, selectionMode: u, onSelectionStart: h, onSelectionEnd: g, multiSelectionKeyCode: y, panActivationKeyCode: v, zoomActivationKeyCode: w, elementsSelectable: m, zoomOnScroll: b, zoomOnPinch: p, panOnScroll: x, panOnScrollSpeed: I, panOnScrollMode: E, zoomOnDoubleClick: A, panOnDrag: P, autoPanOnSelection: T, defaultViewport: _, translateExtent: k, minZoom: R, maxZoom: F, preventScrolling: S, onSelectionContextMenu: j, noWheelClassName: C, noPanClassName: M, disableKeyboardA11y: z, onViewportChange: $, isControlledViewport: W }) {
  const { nodesSelectionActive: L, userSelectionActive: O } = fe(yy, ve), U = On(l, { target: na }), K = On(v, { target: na }), G = K || P, se = K || x, ee = f && G !== !0, H = U || O || ee;
  return Kg({ deleteKeyCode: d, multiSelectionKeyCode: y }), a.jsx(Qg, { onPaneContextMenu: i, elementsSelectable: m, zoomOnScroll: b, zoomOnPinch: p, panOnScroll: se, panOnScrollSpeed: I, panOnScrollMode: E, zoomOnDoubleClick: A, panOnDrag: !U && G, defaultViewport: _, translateExtent: k, minZoom: R, maxZoom: F, zoomActivationKeyCode: w, preventScrolling: S, noWheelClassName: C, noPanClassName: M, onViewportChange: $, isControlledViewport: W, paneClickDistance: c, selectionOnDrag: ee, children: a.jsxs(oy, { onSelectionStart: h, onSelectionEnd: g, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, panOnDrag: G, autoPanOnSelection: T, isSelecting: !!H, selectionMode: u, selectionKeyPressed: U, paneClickDistance: c, selectionOnDrag: ee, children: [e, L && a.jsx(gy, { onSelectionContextMenu: j, noPanClassName: M, disableKeyboardA11y: z })] }) });
}
cl.displayName = "FlowRenderer";
const my = Ce(cl), xy = (e) => (t) => e ? _i(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function wy(e) {
  return fe(ye(xy(e), [e]), ve);
}
const vy = (e) => e.updateNodeInternals;
function by() {
  const e = fe(vy), [t] = Y(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
  return ne(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function Ny({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = be(), i = ce(null), s = ce(null), c = ce(e.sourcePosition), d = ce(e.targetPosition), l = ce(t), f = n && !!e.internals.handleBounds;
  return ne(() => {
    i.current && !e.hidden && (!f || s.current !== i.current) && (s.current && o?.unobserve(s.current), o?.observe(i.current), s.current = i.current);
  }, [f, e.hidden]), ne(() => () => {
    s.current && (o?.unobserve(s.current), s.current = null);
  }, []), ne(() => {
    if (i.current) {
      const u = l.current !== t, h = c.current !== e.sourcePosition, g = d.current !== e.targetPosition;
      (u || h || g) && (l.current = t, c.current = e.sourcePosition, d.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: i.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), i;
}
function Sy({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: i, onDoubleClick: s, nodesDraggable: c, elementsSelectable: d, nodesConnectable: l, nodesFocusable: f, resizeObserver: u, noDragClassName: h, noPanClassName: g, disableKeyboardA11y: y, rfId: v, nodeTypes: w, nodeClickDistance: m, onError: b }) {
  const { node: p, internals: x, isParent: I } = fe((H) => {
    const Z = H.nodeLookup.get(e), ae = H.parentLookup.has(e);
    return {
      node: Z,
      internals: Z.internals,
      isParent: ae
    };
  }, ve);
  let E = p.type || "default", A = w?.[E] || ta[E];
  A === void 0 && (b?.("003", Xe.error003(E)), E = "default", A = w?.default || ta.default);
  const P = !!(p.draggable || c && typeof p.draggable > "u"), T = !!(p.selectable || d && typeof p.selectable > "u"), _ = !!(p.connectable || l && typeof p.connectable > "u"), k = !!(p.focusable || f && typeof p.focusable > "u"), R = be(), F = Ic(p), S = Ny({ node: p, nodeType: E, hasDimensions: F, resizeObserver: u }), j = il({
    nodeRef: S,
    disabled: p.hidden || !P,
    noDragClassName: h,
    handleSelector: p.dragHandle,
    nodeId: e,
    isSelectable: T,
    nodeClickDistance: m
  }), C = sl();
  if (p.hidden)
    return null;
  const M = dt(p), z = hy(p), $ = T || P || t || n || o || r, W = n ? (H) => n(H, { ...x.userNode }) : void 0, L = o ? (H) => o(H, { ...x.userNode }) : void 0, O = r ? (H) => r(H, { ...x.userNode }) : void 0, U = i ? (H) => i(H, { ...x.userNode }) : void 0, K = s ? (H) => s(H, { ...x.userNode }) : void 0, G = (H) => {
    const { selectNodesOnDrag: Z, nodeDragThreshold: ae } = R.getState();
    T && (!Z || !P || ae > 0) && hi({
      id: e,
      store: R,
      nodeRef: S
    }), t && t(H, { ...x.userNode });
  }, se = (H) => {
    if (!(_c(H.nativeEvent) || y)) {
      if (mc.includes(H.key) && T) {
        const Z = H.key === "Escape";
        hi({
          id: e,
          store: R,
          unselect: Z,
          nodeRef: S
        });
      } else if (P && p.selected && Object.prototype.hasOwnProperty.call(Zo, H.key)) {
        H.preventDefault();
        const { ariaLabelConfig: Z } = R.getState();
        R.setState({
          ariaLiveMessage: Z["node.a11yDescription.ariaLiveMessage"]({
            direction: H.key.replace("Arrow", "").toLowerCase(),
            x: ~~x.positionAbsolute.x,
            y: ~~x.positionAbsolute.y
          })
        }), C({
          direction: Zo[H.key],
          factor: H.shiftKey ? 4 : 1
        });
      }
    }
  }, ee = () => {
    if (y || !S.current?.matches(":focus-visible"))
      return;
    const { transform: H, width: Z, height: ae, autoPanOnNodeFocus: oe, setCenter: q } = R.getState();
    if (!oe)
      return;
    _i(/* @__PURE__ */ new Map([[e, p]]), { x: 0, y: 0, width: Z, height: ae }, H, !0).length > 0 || q(p.position.x + M.width / 2, p.position.y + M.height / 2, {
      zoom: H[2]
    });
  };
  return a.jsx("div", { className: Ae([
    "react-flow__node",
    `react-flow__node-${E}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [g]: P
    },
    p.className,
    {
      selected: p.selected,
      selectable: T,
      parent: I,
      draggable: P,
      dragging: j
    }
  ]), ref: S, style: {
    zIndex: x.z,
    transform: `translate(${x.positionAbsolute.x}px,${x.positionAbsolute.y}px)`,
    pointerEvents: $ ? "all" : "none",
    visibility: F ? "visible" : "hidden",
    ...p.style,
    ...z
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: W, onMouseMove: L, onMouseLeave: O, onContextMenu: U, onClick: G, onDoubleClick: K, onKeyDown: k ? se : void 0, tabIndex: k ? 0 : void 0, onFocus: k ? ee : void 0, role: p.ariaRole ?? (k ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": y ? void 0 : `${Uc}-${v}`, "aria-label": p.ariaLabel, ...p.domAttributes, children: a.jsx(iy, { value: e, children: a.jsx(A, { id: e, data: p.data, type: E, positionAbsoluteX: x.positionAbsolute.x, positionAbsoluteY: x.positionAbsolute.y, selected: p.selected ?? !1, selectable: T, draggable: P, deletable: p.deletable ?? !0, isConnectable: _, sourcePosition: p.sourcePosition, targetPosition: p.targetPosition, dragging: j, dragHandle: p.dragHandle, zIndex: x.z, parentId: p.parentId, ...M }) }) });
}
var Ey = Ce(Sy);
const ky = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function ll(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: i } = fe(ky, ve), s = wy(e.onlyRenderVisibleElements), c = by();
  return a.jsx("div", { className: "react-flow__nodes", style: dr, children: s.map((d) => (
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
    a.jsx(Ey, { id: d, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: i }, d)
  )) });
}
ll.displayName = "NodeRenderer";
const Iy = Ce(ll);
function Cy(e) {
  return fe(ye((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const i = n.nodeLookup.get(r.source), s = n.nodeLookup.get(r.target);
        i && s && Np({
          sourceNode: i,
          targetNode: s,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && o.push(r.id);
      }
    return o;
  }, [e]), ve);
}
const jy = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return a.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, _y = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return a.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, oa = {
  [Xo.Arrow]: jy,
  [Xo.ArrowClosed]: _y
};
function Ay(e) {
  const t = be();
  return me(() => Object.prototype.hasOwnProperty.call(oa, e) ? oa[e] : (t.getState().onError?.("009", Xe.error009(e)), null), [e]);
}
const My = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: i = "strokeWidth", strokeWidth: s, orient: c = "auto-start-reverse" }) => {
  const d = Ay(t);
  return d ? a.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: i, orient: c, refX: "0", refY: "0", children: a.jsx(d, { color: n, strokeWidth: s }) }) : null;
}, dl = ({ defaultColor: e, rfId: t }) => {
  const n = fe((i) => i.edges), o = fe((i) => i.defaultEdgeOptions), r = me(() => Ap(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? a.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: a.jsx("defs", { children: r.map((i) => a.jsx(My, { id: i.id, type: i.type, color: i.color, width: i.width, height: i.height, markerUnits: i.markerUnits, strokeWidth: i.strokeWidth, orient: i.orient }, i.id)) }) }) : null;
};
dl.displayName = "MarkerDefinitions";
var Dy = Ce(dl);
function ul({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: i, labelBgPadding: s = [2, 4], labelBgBorderRadius: c = 2, children: d, className: l, ...f }) {
  const [u, h] = Y({ x: 1, y: 0, width: 0, height: 0 }), g = Ae(["react-flow__edge-textwrapper", l]), y = ce(null);
  return ne(() => {
    if (y.current) {
      const v = y.current.getBBox();
      h({
        x: v.x,
        y: v.y,
        width: v.width,
        height: v.height
      });
    }
  }, [n]), n ? a.jsxs("g", { transform: `translate(${e - u.width / 2} ${t - u.height / 2})`, className: g, visibility: u.width ? "visible" : "hidden", ...f, children: [r && a.jsx("rect", { width: u.width + 2 * s[0], x: -s[0], y: -s[1], height: u.height + 2 * s[1], className: "react-flow__edge-textbg", style: i, rx: c, ry: c }), a.jsx("text", { className: "react-flow__edge-text", y: u.height / 2, dy: "0.3em", ref: y, style: o, children: n }), d] }) : null;
}
ul.displayName = "EdgeText";
const Py = Ce(ul);
function Un({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: c, labelBgBorderRadius: d, interactionWidth: l = 20, ...f }) {
  return a.jsxs(a.Fragment, { children: [a.jsx("path", { ...f, d: e, fill: "none", className: Ae(["react-flow__edge-path", f.className]) }), l ? a.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, o && Ke(t) && Ke(n) ? a.jsx(Py, { x: t, y: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: c, labelBgBorderRadius: d }) : null] });
}
function ra({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === te.Left || e === te.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function fl({ sourceX: e, sourceY: t, sourcePosition: n = te.Bottom, targetX: o, targetY: r, targetPosition: i = te.Top }) {
  const [s, c] = ra({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [d, l] = ra({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [f, u, h, g] = Mc({
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
    g
  ];
}
function hl(e) {
  return Ce(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: c, label: d, labelStyle: l, labelShowBg: f, labelBgStyle: u, labelBgPadding: h, labelBgBorderRadius: g, style: y, markerEnd: v, markerStart: w, interactionWidth: m }) => {
    const [b, p, x] = fl({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: c
    }), I = e.isInternal ? void 0 : t;
    return a.jsx(Un, { id: I, path: b, labelX: p, labelY: x, label: d, labelStyle: l, labelShowBg: f, labelBgStyle: u, labelBgPadding: h, labelBgBorderRadius: g, style: y, markerEnd: v, markerStart: w, interactionWidth: m });
  });
}
const $y = hl({ isInternal: !1 }), pl = hl({ isInternal: !0 });
$y.displayName = "SimpleBezierEdge";
pl.displayName = "SimpleBezierEdgeInternal";
function gl(e) {
  return Ce(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: c, labelShowBg: d, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: u, style: h, sourcePosition: g = te.Bottom, targetPosition: y = te.Top, markerEnd: v, markerStart: w, pathOptions: m, interactionWidth: b }) => {
    const [p, x, I] = qo({
      sourceX: n,
      sourceY: o,
      sourcePosition: g,
      targetX: r,
      targetY: i,
      targetPosition: y,
      borderRadius: m?.borderRadius,
      offset: m?.offset,
      stepPosition: m?.stepPosition
    }), E = e.isInternal ? void 0 : t;
    return a.jsx(Un, { id: E, path: p, labelX: x, labelY: I, label: s, labelStyle: c, labelShowBg: d, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: u, style: h, markerEnd: v, markerStart: w, interactionWidth: b });
  });
}
const yl = gl({ isInternal: !1 }), ml = gl({ isInternal: !0 });
yl.displayName = "SmoothStepEdge";
ml.displayName = "SmoothStepEdgeInternal";
function xl(e) {
  return Ce(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return a.jsx(yl, { ...n, id: o, pathOptions: me(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Ty = xl({ isInternal: !1 }), wl = xl({ isInternal: !0 });
Ty.displayName = "StepEdge";
wl.displayName = "StepEdgeInternal";
function vl(e) {
  return Ce(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: c, labelShowBg: d, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: u, style: h, markerEnd: g, markerStart: y, interactionWidth: v }) => {
    const [w, m, b] = Tc({ sourceX: n, sourceY: o, targetX: r, targetY: i }), p = e.isInternal ? void 0 : t;
    return a.jsx(Un, { id: p, path: w, labelX: m, labelY: b, label: s, labelStyle: c, labelShowBg: d, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: u, style: h, markerEnd: g, markerStart: y, interactionWidth: v });
  });
}
const zy = vl({ isInternal: !1 }), bl = vl({ isInternal: !0 });
zy.displayName = "StraightEdge";
bl.displayName = "StraightEdgeInternal";
function Nl(e) {
  return Ce(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s = te.Bottom, targetPosition: c = te.Top, label: d, labelStyle: l, labelShowBg: f, labelBgStyle: u, labelBgPadding: h, labelBgBorderRadius: g, style: y, markerEnd: v, markerStart: w, pathOptions: m, interactionWidth: b }) => {
    const [p, x, I] = Dc({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: c,
      curvature: m?.curvature
    }), E = e.isInternal ? void 0 : t;
    return a.jsx(Un, { id: E, path: p, labelX: x, labelY: I, label: d, labelStyle: l, labelShowBg: f, labelBgStyle: u, labelBgPadding: h, labelBgBorderRadius: g, style: y, markerEnd: v, markerStart: w, interactionWidth: b });
  });
}
const Ry = Nl({ isInternal: !1 }), Sl = Nl({ isInternal: !0 });
Ry.displayName = "BezierEdge";
Sl.displayName = "BezierEdgeInternal";
const ia = {
  default: Sl,
  straight: bl,
  step: wl,
  smoothstep: ml,
  simplebezier: pl
}, sa = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, Ly = (e, t, n) => n === te.Left ? e - t : n === te.Right ? e + t : e, Vy = (e, t, n) => n === te.Top ? e - t : n === te.Bottom ? e + t : e, aa = "react-flow__edgeupdater";
function ca({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: i, onMouseOut: s, type: c }) {
  return a.jsx("circle", { onMouseDown: r, onMouseEnter: i, onMouseOut: s, className: Ae([aa, `${aa}-${c}`]), cx: Ly(t, o, e), cy: Vy(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function Hy({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: i, targetY: s, sourcePosition: c, targetPosition: d, onReconnect: l, onReconnectStart: f, onReconnectEnd: u, setReconnecting: h, setUpdateHover: g }) {
  const y = be(), v = (x, I) => {
    if (x.button !== 0)
      return;
    const { autoPanOnConnect: E, domNode: A, connectionMode: P, connectionRadius: T, lib: _, onConnectStart: k, cancelConnection: R, nodeLookup: F, rfId: S, panBy: j, updateConnection: C } = y.getState(), M = I.type === "target", z = (L, O) => {
      h(!1), u?.(L, n, I.type, O);
    }, $ = (L) => l?.(n, L), W = (L, O) => {
      h(!0), f?.(x, n, I.type), k?.(L, O);
    };
    fi.onPointerDown(x.nativeEvent, {
      autoPanOnConnect: E,
      connectionMode: P,
      connectionRadius: T,
      domNode: A,
      handleId: I.id,
      nodeId: I.nodeId,
      nodeLookup: F,
      isTarget: M,
      edgeUpdaterType: I.type,
      lib: _,
      flowId: S,
      cancelConnection: R,
      panBy: j,
      isValidConnection: (...L) => y.getState().isValidConnection?.(...L) ?? !0,
      onConnect: $,
      onConnectStart: W,
      onConnectEnd: (...L) => y.getState().onConnectEnd?.(...L),
      onReconnectEnd: z,
      updateConnection: C,
      getTransform: () => y.getState().transform,
      getFromHandle: () => y.getState().connection.fromHandle,
      dragThreshold: y.getState().connectionDragThreshold,
      handleDomNode: x.currentTarget
    });
  }, w = (x) => v(x, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), m = (x) => v(x, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), b = () => g(!0), p = () => g(!1);
  return a.jsxs(a.Fragment, { children: [(e === !0 || e === "source") && a.jsx(ca, { position: c, centerX: o, centerY: r, radius: t, onMouseDown: w, onMouseEnter: b, onMouseOut: p, type: "source" }), (e === !0 || e === "target") && a.jsx(ca, { position: d, centerX: i, centerY: s, radius: t, onMouseDown: m, onMouseEnter: b, onMouseOut: p, type: "target" })] });
}
function Oy({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: i, onContextMenu: s, onMouseEnter: c, onMouseMove: d, onMouseLeave: l, reconnectRadius: f, onReconnect: u, onReconnectStart: h, onReconnectEnd: g, rfId: y, edgeTypes: v, noPanClassName: w, onError: m, disableKeyboardA11y: b }) {
  let p = fe((q) => q.edgeLookup.get(e));
  const x = fe((q) => q.defaultEdgeOptions);
  p = x ? { ...x, ...p } : p;
  let I = p.type || "default", E = v?.[I] || ia[I];
  E === void 0 && (m?.("011", Xe.error011(I)), I = "default", E = v?.default || ia.default);
  const A = !!(p.focusable || t && typeof p.focusable > "u"), P = typeof u < "u" && (p.reconnectable || n && typeof p.reconnectable > "u"), T = !!(p.selectable || o && typeof p.selectable > "u"), _ = ce(null), [k, R] = Y(!1), [F, S] = Y(!1), j = be(), { zIndex: C, sourceX: M, sourceY: z, targetX: $, targetY: W, sourcePosition: L, targetPosition: O } = fe(ye((q) => {
    const Q = q.nodeLookup.get(p.source), le = q.nodeLookup.get(p.target);
    if (!Q || !le)
      return {
        zIndex: p.zIndex,
        ...sa
      };
    const de = _p({
      id: e,
      sourceNode: Q,
      targetNode: le,
      sourceHandle: p.sourceHandle || null,
      targetHandle: p.targetHandle || null,
      connectionMode: q.connectionMode,
      onError: m
    });
    return {
      zIndex: bp({
        selected: p.selected,
        zIndex: p.zIndex,
        sourceNode: Q,
        targetNode: le,
        elevateOnSelect: q.elevateEdgesOnSelect,
        zIndexMode: q.zIndexMode
      }),
      ...de || sa
    };
  }, [p.source, p.target, p.sourceHandle, p.targetHandle, p.selected, p.zIndex]), ve), U = me(() => p.markerStart ? `url('#${di(p.markerStart, y)}')` : void 0, [p.markerStart, y]), K = me(() => p.markerEnd ? `url('#${di(p.markerEnd, y)}')` : void 0, [p.markerEnd, y]);
  if (p.hidden || M === null || z === null || $ === null || W === null)
    return null;
  const G = (q) => {
    const { addSelectedEdges: Q, unselectNodesAndEdges: le, multiSelectionActive: de } = j.getState();
    T && (j.setState({ nodesSelectionActive: !1 }), p.selected && de ? (le({ nodes: [], edges: [p] }), _.current?.blur()) : Q([e])), r && r(q, p);
  }, se = i ? (q) => {
    i(q, { ...p });
  } : void 0, ee = s ? (q) => {
    s(q, { ...p });
  } : void 0, H = c ? (q) => {
    c(q, { ...p });
  } : void 0, Z = d ? (q) => {
    d(q, { ...p });
  } : void 0, ae = l ? (q) => {
    l(q, { ...p });
  } : void 0, oe = (q) => {
    if (!b && mc.includes(q.key) && T) {
      const { unselectNodesAndEdges: Q, addSelectedEdges: le } = j.getState();
      q.key === "Escape" ? (_.current?.blur(), Q({ edges: [p] })) : le([e]);
    }
  };
  return a.jsx("svg", { style: { zIndex: C }, children: a.jsxs("g", { className: Ae([
    "react-flow__edge",
    `react-flow__edge-${I}`,
    p.className,
    w,
    {
      selected: p.selected,
      animated: p.animated,
      inactive: !T && !r,
      updating: k,
      selectable: T
    }
  ]), onClick: G, onDoubleClick: se, onContextMenu: ee, onMouseEnter: H, onMouseMove: Z, onMouseLeave: ae, onKeyDown: A ? oe : void 0, tabIndex: A ? 0 : void 0, role: p.ariaRole ?? (A ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": p.ariaLabel === null ? void 0 : p.ariaLabel || `Edge from ${p.source} to ${p.target}`, "aria-describedby": A ? `${Kc}-${y}` : void 0, ref: _, ...p.domAttributes, children: [!F && a.jsx(E, { id: e, source: p.source, target: p.target, type: p.type, selected: p.selected, animated: p.animated, selectable: T, deletable: p.deletable ?? !0, label: p.label, labelStyle: p.labelStyle, labelShowBg: p.labelShowBg, labelBgStyle: p.labelBgStyle, labelBgPadding: p.labelBgPadding, labelBgBorderRadius: p.labelBgBorderRadius, sourceX: M, sourceY: z, targetX: $, targetY: W, sourcePosition: L, targetPosition: O, data: p.data, style: p.style, sourceHandleId: p.sourceHandle, targetHandleId: p.targetHandle, markerStart: U, markerEnd: K, pathOptions: "pathOptions" in p ? p.pathOptions : void 0, interactionWidth: p.interactionWidth }), P && a.jsx(Hy, { edge: p, isReconnectable: P, reconnectRadius: f, onReconnect: u, onReconnectStart: h, onReconnectEnd: g, sourceX: M, sourceY: z, targetX: $, targetY: W, sourcePosition: L, targetPosition: O, setUpdateHover: R, setReconnecting: S })] }) });
}
var Fy = Ce(Oy);
const By = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function El({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: i, onEdgeContextMenu: s, onEdgeMouseEnter: c, onEdgeMouseMove: d, onEdgeMouseLeave: l, onEdgeClick: f, reconnectRadius: u, onEdgeDoubleClick: h, onReconnectStart: g, onReconnectEnd: y, disableKeyboardA11y: v }) {
  const { edgesFocusable: w, edgesReconnectable: m, elementsSelectable: b, onError: p } = fe(By, ve), x = Cy(t);
  return a.jsxs("div", { className: "react-flow__edges", children: [a.jsx(Dy, { defaultColor: e, rfId: n }), x.map((I) => a.jsx(Fy, { id: I, edgesFocusable: w, edgesReconnectable: m, elementsSelectable: b, noPanClassName: r, onReconnect: i, onContextMenu: s, onMouseEnter: c, onMouseMove: d, onMouseLeave: l, onClick: f, reconnectRadius: u, onDoubleClick: h, onReconnectStart: g, onReconnectEnd: y, rfId: n, onError: p, edgeTypes: o, disableKeyboardA11y: v }, I))] });
}
El.displayName = "EdgeRenderer";
const Wy = Ce(El), Xy = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function Yy({ children: e }) {
  const t = fe(Xy);
  return a.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function qy(e) {
  const t = Li(), n = ce(!1);
  ne(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Zy = (e) => e.panZoom?.syncViewport;
function Uy(e) {
  const t = fe(Zy), n = be();
  return ne(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function Ky(e) {
  return e.connection.inProgress ? { ...e.connection, to: rn(e.connection.to, e.transform) } : { ...e.connection };
}
function Gy(e) {
  return Ky;
}
function Jy(e) {
  const t = Gy();
  return fe(t, ve);
}
const Qy = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function em({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: i, height: s, isValid: c, inProgress: d } = fe(Qy, ve);
  return !(i && r && d) ? null : a.jsx("svg", { style: e, width: i, height: s, className: "react-flow__connectionline react-flow__container", children: a.jsx("g", { className: Ae(["react-flow__connection", vc(c)]), children: a.jsx(kl, { style: t, type: n, CustomComponent: o, isValid: c }) }) });
}
const kl = ({ style: e, type: t = gt.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: i, fromNode: s, fromHandle: c, fromPosition: d, to: l, toNode: f, toHandle: u, toPosition: h, pointer: g } = Jy();
  if (!r)
    return;
  if (n)
    return a.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: s, fromHandle: c, fromX: i.x, fromY: i.y, toX: l.x, toY: l.y, fromPosition: d, toPosition: h, connectionStatus: vc(o), toNode: f, toHandle: u, pointer: g });
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
      [y] = Dc(v);
      break;
    case gt.SimpleBezier:
      [y] = fl(v);
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
      [y] = Tc(v);
  }
  return a.jsx("path", { d: y, fill: "none", className: "react-flow__connection-path", style: e });
};
kl.displayName = "ConnectionLine";
const tm = {};
function la(e = tm) {
  ce(e), be(), ne(() => {
  }, [e]);
}
function nm() {
  be(), ce(!1), ne(() => {
  }, []);
}
function Il({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: i, onEdgeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: d, onNodeMouseLeave: l, onNodeContextMenu: f, onSelectionContextMenu: u, onSelectionStart: h, onSelectionEnd: g, connectionLineType: y, connectionLineStyle: v, connectionLineComponent: w, connectionLineContainerStyle: m, selectionKeyCode: b, selectionOnDrag: p, selectionMode: x, multiSelectionKeyCode: I, panActivationKeyCode: E, zoomActivationKeyCode: A, deleteKeyCode: P, onlyRenderVisibleElements: T, elementsSelectable: _, defaultViewport: k, translateExtent: R, minZoom: F, maxZoom: S, preventScrolling: j, defaultMarkerColor: C, zoomOnScroll: M, zoomOnPinch: z, panOnScroll: $, panOnScrollSpeed: W, panOnScrollMode: L, zoomOnDoubleClick: O, panOnDrag: U, autoPanOnSelection: K, onPaneClick: G, onPaneMouseEnter: se, onPaneMouseMove: ee, onPaneMouseLeave: H, onPaneScroll: Z, onPaneContextMenu: ae, paneClickDistance: oe, nodeClickDistance: q, onEdgeContextMenu: Q, onEdgeMouseEnter: le, onEdgeMouseMove: de, onEdgeMouseLeave: he, reconnectRadius: ke, onReconnect: Ie, onReconnectStart: Pe, onReconnectEnd: ge, noDragClassName: $e, noWheelClassName: B, noPanClassName: re, disableKeyboardA11y: xe, nodeExtent: we, rfId: je, viewport: Le, onViewportChange: Ye }) {
  return la(e), la(t), nm(), qy(n), Uy(Le), a.jsx(my, { onPaneClick: G, onPaneMouseEnter: se, onPaneMouseMove: ee, onPaneMouseLeave: H, onPaneContextMenu: ae, onPaneScroll: Z, paneClickDistance: oe, deleteKeyCode: P, selectionKeyCode: b, selectionOnDrag: p, selectionMode: x, onSelectionStart: h, onSelectionEnd: g, multiSelectionKeyCode: I, panActivationKeyCode: E, zoomActivationKeyCode: A, elementsSelectable: _, zoomOnScroll: M, zoomOnPinch: z, zoomOnDoubleClick: O, panOnScroll: $, panOnScrollSpeed: W, panOnScrollMode: L, panOnDrag: U, autoPanOnSelection: K, defaultViewport: k, translateExtent: R, minZoom: F, maxZoom: S, onSelectionContextMenu: u, preventScrolling: j, noDragClassName: $e, noWheelClassName: B, noPanClassName: re, disableKeyboardA11y: xe, onViewportChange: Ye, isControlledViewport: !!Le, children: a.jsxs(Yy, { children: [a.jsx(Wy, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: s, onReconnect: Ie, onReconnectStart: Pe, onReconnectEnd: ge, onlyRenderVisibleElements: T, onEdgeContextMenu: Q, onEdgeMouseEnter: le, onEdgeMouseMove: de, onEdgeMouseLeave: he, reconnectRadius: ke, defaultMarkerColor: C, noPanClassName: re, disableKeyboardA11y: xe, rfId: je }), a.jsx(em, { style: v, type: y, component: w, containerStyle: m }), a.jsx("div", { className: "react-flow__edgelabel-renderer" }), a.jsx(Iy, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: i, onNodeMouseEnter: c, onNodeMouseMove: d, onNodeMouseLeave: l, onNodeContextMenu: f, nodeClickDistance: q, onlyRenderVisibleElements: T, noPanClassName: re, noDragClassName: $e, disableKeyboardA11y: xe, nodeExtent: we, rfId: je }), a.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Il.displayName = "GraphView";
const om = Ce(Il), rm = kc(), da = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: c, minZoom: d = 0.5, maxZoom: l = 2, nodeOrigin: f, nodeExtent: u, zIndexMode: h = "basic" } = {}) => {
  const g = /* @__PURE__ */ new Map(), y = /* @__PURE__ */ new Map(), v = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), m = o ?? t ?? [], b = n ?? e ?? [], p = f ?? [0, 0], x = u ?? Rn;
  Lc(v, w, m);
  const { nodesInitialized: I } = ui(b, g, y, {
    nodeOrigin: p,
    nodeExtent: x,
    zIndexMode: h
  });
  let E = [0, 0, 1];
  if (s && r && i) {
    const A = qn(g, {
      filter: (k) => !!((k.width || k.initialWidth) && (k.height || k.initialHeight))
    }), { x: P, y: T, zoom: _ } = Mi(A, r, i, d, l, c?.padding ?? 0.1);
    E = [P, T, _];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: i ?? 0,
    transform: E,
    nodes: b,
    nodesInitialized: I,
    nodeLookup: g,
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
    connection: { ...wc },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: rm,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: xc,
    zIndexMode: h,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, im = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: c, minZoom: d, maxZoom: l, nodeOrigin: f, nodeExtent: u, zIndexMode: h }) => xg((g, y) => {
  async function v() {
    const { nodeLookup: w, panZoom: m, fitViewOptions: b, fitViewResolver: p, width: x, height: I, minZoom: E, maxZoom: A } = y();
    m && (await pp({
      nodes: w,
      width: x,
      height: I,
      panZoom: m,
      minZoom: E,
      maxZoom: A
    }, b), p?.resolve(!0), g({ fitViewResolver: null }));
  }
  return {
    ...da({
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
    setNodes: (w) => {
      const { nodeLookup: m, parentLookup: b, nodeOrigin: p, elevateNodesOnSelect: x, fitViewQueued: I, zIndexMode: E, nodesSelectionActive: A } = y(), { nodesInitialized: P, hasSelectedNodes: T } = ui(w, m, b, {
        nodeOrigin: p,
        nodeExtent: u,
        elevateNodesOnSelect: x,
        checkEquality: !0,
        zIndexMode: E
      }), _ = A && T;
      I && P ? (v(), g({
        nodes: w,
        nodesInitialized: P,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: _
      })) : g({ nodes: w, nodesInitialized: P, nodesSelectionActive: _ });
    },
    setEdges: (w) => {
      const { connectionLookup: m, edgeLookup: b } = y();
      Lc(m, b, w), g({ edges: w });
    },
    setDefaultNodesAndEdges: (w, m) => {
      if (w) {
        const { setNodes: b } = y();
        b(w), g({ hasDefaultNodes: !0 });
      }
      if (m) {
        const { setEdges: b } = y();
        b(m), g({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (w) => {
      const { triggerNodeChanges: m, nodeLookup: b, parentLookup: p, domNode: x, nodeOrigin: I, nodeExtent: E, debug: A, fitViewQueued: P, zIndexMode: T } = y(), { changes: _, updatedInternals: k } = Rp(w, b, p, x, I, E, T);
      k && (Pp(b, p, { nodeOrigin: I, nodeExtent: E, zIndexMode: T }), P ? (v(), g({ fitViewQueued: !1, fitViewOptions: void 0 })) : g({}), _?.length > 0 && (A && console.log("React Flow: trigger node changes", _), m?.(_)));
    },
    updateNodePositions: (w, m = !1) => {
      const b = [];
      let p = [];
      const { nodeLookup: x, triggerNodeChanges: I, connection: E, updateConnection: A, onNodesChangeMiddlewareMap: P } = y();
      for (const [T, _] of w) {
        const k = x.get(T), R = !!(k?.expandParent && k?.parentId && _?.position), F = {
          id: T,
          type: "position",
          position: R ? {
            x: Math.max(0, _.position.x),
            y: Math.max(0, _.position.y)
          } : _.position,
          dragging: m
        };
        if (k && E.inProgress && E.fromNode.id === k.id) {
          const S = $t(k, E.fromHandle, te.Left, !0);
          A({ ...E, from: S });
        }
        R && k.parentId && b.push({
          id: T,
          parentId: k.parentId,
          rect: {
            ..._.internals.positionAbsolute,
            width: _.measured.width ?? 0,
            height: _.measured.height ?? 0
          }
        }), p.push(F);
      }
      if (b.length > 0) {
        const { parentLookup: T, nodeOrigin: _ } = y(), k = Ri(b, x, T, _);
        p.push(...k);
      }
      for (const T of P.values())
        p = T(p);
      I(p);
    },
    triggerNodeChanges: (w) => {
      const { onNodesChange: m, setNodes: b, nodes: p, hasDefaultNodes: x, debug: I } = y();
      if (w?.length) {
        if (x) {
          const E = Qc(w, p);
          b(E);
        }
        I && console.log("React Flow: trigger node changes", w), m?.(w);
      }
    },
    triggerEdgeChanges: (w) => {
      const { onEdgesChange: m, setEdges: b, edges: p, hasDefaultEdges: x, debug: I } = y();
      if (w?.length) {
        if (x) {
          const E = el(w, p);
          b(E);
        }
        I && console.log("React Flow: trigger edge changes", w), m?.(w);
      }
    },
    addSelectedNodes: (w) => {
      const { multiSelectionActive: m, edgeLookup: b, nodeLookup: p, triggerNodeChanges: x, triggerEdgeChanges: I } = y();
      if (m) {
        const E = w.map((A) => kt(A, !0));
        x(E);
        return;
      }
      x(Xt(p, /* @__PURE__ */ new Set([...w]), !0)), I(Xt(b));
    },
    addSelectedEdges: (w) => {
      const { multiSelectionActive: m, edgeLookup: b, nodeLookup: p, triggerNodeChanges: x, triggerEdgeChanges: I } = y();
      if (m) {
        const E = w.map((A) => kt(A, !0));
        I(E);
        return;
      }
      I(Xt(b, /* @__PURE__ */ new Set([...w]))), x(Xt(p, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: w, edges: m } = {}) => {
      const { edges: b, nodes: p, nodeLookup: x, triggerNodeChanges: I, triggerEdgeChanges: E } = y(), A = w || p, P = m || b, T = [];
      for (const k of A) {
        if (!k.selected)
          continue;
        const R = x.get(k.id);
        R && (R.selected = !1), T.push(kt(k.id, !1));
      }
      const _ = [];
      for (const k of P)
        k.selected && _.push(kt(k.id, !1));
      I(T), E(_);
    },
    setMinZoom: (w) => {
      const { panZoom: m, maxZoom: b } = y();
      m?.setScaleExtent([w, b]), g({ minZoom: w });
    },
    setMaxZoom: (w) => {
      const { panZoom: m, minZoom: b } = y();
      m?.setScaleExtent([b, w]), g({ maxZoom: w });
    },
    setTranslateExtent: (w) => {
      y().panZoom?.setTranslateExtent(w), g({ translateExtent: w });
    },
    resetSelectedElements: () => {
      const { edges: w, nodes: m, triggerNodeChanges: b, triggerEdgeChanges: p, elementsSelectable: x } = y();
      if (!x)
        return;
      const I = m.reduce((A, P) => P.selected ? [...A, kt(P.id, !1)] : A, []), E = w.reduce((A, P) => P.selected ? [...A, kt(P.id, !1)] : A, []);
      b(I), p(E);
    },
    setNodeExtent: (w) => {
      const { nodes: m, nodeLookup: b, parentLookup: p, nodeOrigin: x, elevateNodesOnSelect: I, nodeExtent: E, zIndexMode: A } = y();
      w[0][0] === E[0][0] && w[0][1] === E[0][1] && w[1][0] === E[1][0] && w[1][1] === E[1][1] || (ui(m, b, p, {
        nodeOrigin: x,
        nodeExtent: w,
        elevateNodesOnSelect: I,
        checkEquality: !1,
        zIndexMode: A
      }), g({ nodeExtent: w }));
    },
    panBy: (w) => {
      const { transform: m, width: b, height: p, panZoom: x, translateExtent: I } = y();
      return Lp({ delta: w, panZoom: x, transform: m, translateExtent: I, width: b, height: p });
    },
    setCenter: async (w, m, b) => {
      const { width: p, height: x, maxZoom: I, panZoom: E } = y();
      if (!E)
        return !1;
      const A = typeof b?.zoom < "u" ? b.zoom : I;
      return await E.setViewport({
        x: p / 2 - w * A,
        y: x / 2 - m * A,
        zoom: A
      }, { duration: b?.duration, ease: b?.ease, interpolate: b?.interpolate }), !0;
    },
    cancelConnection: () => {
      g({
        connection: { ...wc }
      });
    },
    updateConnection: (w) => {
      g({ connection: w });
    },
    reset: () => g({ ...da() })
  };
}, Object.is);
function sm({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: i, initialMinZoom: s, initialMaxZoom: c, initialFitViewOptions: d, fitView: l, nodeOrigin: f, nodeExtent: u, zIndexMode: h, children: g }) {
  const [y] = Y(() => im({
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
  return a.jsx(Ng, { value: y, children: a.jsx(Yg, { children: g }) });
}
function am({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: i, height: s, fitView: c, fitViewOptions: d, minZoom: l, maxZoom: f, nodeOrigin: u, nodeExtent: h, zIndexMode: g }) {
  return Bn(cr) ? a.jsx(a.Fragment, { children: e }) : a.jsx(sm, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: i, initialHeight: s, fitView: c, initialFitViewOptions: d, initialMinZoom: l, initialMaxZoom: f, nodeOrigin: u, nodeExtent: h, zIndexMode: g, children: e });
}
const cm = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function lm({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: i, edgeTypes: s, onNodeClick: c, onEdgeClick: d, onInit: l, onMove: f, onMoveStart: u, onMoveEnd: h, onConnect: g, onConnectStart: y, onConnectEnd: v, onClickConnectStart: w, onClickConnectEnd: m, onNodeMouseEnter: b, onNodeMouseMove: p, onNodeMouseLeave: x, onNodeContextMenu: I, onNodeDoubleClick: E, onNodeDragStart: A, onNodeDrag: P, onNodeDragStop: T, onNodesDelete: _, onEdgesDelete: k, onDelete: R, onSelectionChange: F, onSelectionDragStart: S, onSelectionDrag: j, onSelectionDragStop: C, onSelectionContextMenu: M, onSelectionStart: z, onSelectionEnd: $, onBeforeDelete: W, connectionMode: L, connectionLineType: O = gt.Bezier, connectionLineStyle: U, connectionLineComponent: K, connectionLineContainerStyle: G, deleteKeyCode: se = "Backspace", selectionKeyCode: ee = "Shift", selectionOnDrag: H = !1, selectionMode: Z = Ln.Full, panActivationKeyCode: ae = "Space", multiSelectionKeyCode: oe = Hn() ? "Meta" : "Control", zoomActivationKeyCode: q = Hn() ? "Meta" : "Control", snapToGrid: Q, snapGrid: le, onlyRenderVisibleElements: de = !1, selectNodesOnDrag: he, nodesDraggable: ke, autoPanOnNodeFocus: Ie, nodesConnectable: Pe, nodesFocusable: ge, nodeOrigin: $e = Gc, edgesFocusable: B, edgesReconnectable: re, elementsSelectable: xe = !0, defaultViewport: we = Tg, minZoom: je = 0.5, maxZoom: Le = 2, translateExtent: Ye = Rn, preventScrolling: an = !0, nodeExtent: Rt, defaultMarkerColor: cn = "#b1b1b7", zoomOnScroll: Lt = !0, zoomOnPinch: ut = !0, panOnScroll: vt = !1, panOnScrollSpeed: bt = 0.5, panOnScrollMode: nt = jt.Free, zoomOnDoubleClick: Qn = !0, panOnDrag: Oe = !0, onPaneClick: eo, onPaneMouseEnter: Se, onPaneMouseMove: qe, onPaneMouseLeave: ln, onPaneScroll: Ee, onPaneContextMenu: Nt, paneClickDistance: dn = 1, nodeClickDistance: Te = 0, children: St, onReconnect: ft, onReconnectStart: gr, onReconnectEnd: to, onEdgeContextMenu: no, onEdgeDoubleClick: un, onEdgeMouseEnter: yr, onEdgeMouseMove: fn, onEdgeMouseLeave: Vt, reconnectRadius: Fe = 10, onNodesChange: hn, onEdgesChange: pn, noDragClassName: gn = "nodrag", noWheelClassName: Ht = "nowheel", noPanClassName: oo = "nopan", fitView: ro, fitViewOptions: io, connectOnClick: mr, attributionPosition: so, proOptions: ao, defaultEdgeOptions: co, elevateNodesOnSelect: xr = !0, elevateEdgesOnSelect: yn = !1, disableKeyboardA11y: lo = !1, autoPanOnConnect: wr, autoPanOnNodeDrag: vr, autoPanOnSelection: br = !0, autoPanSpeed: Nr, connectionRadius: mn, isValidConnection: Sr, onError: Er, style: kr, id: uo, nodeDragThreshold: Ir, connectionDragThreshold: Cr, viewport: jr, onViewportChange: fo, width: ho, height: _r, colorMode: Ar = "light", debug: Mr, onScroll: po, ariaLabelConfig: Dr, zIndexMode: xn = "basic", ...go }, yo) {
  const Ot = uo || "1", mo = Vg(Ar), Pr = ye((xo) => {
    xo.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), po?.(xo);
  }, [po]);
  return a.jsx("div", { "data-testid": "rf__wrapper", ...go, onScroll: Pr, style: { ...kr, ...cm }, ref: yo, className: Ae(["react-flow", r, mo]), id: uo, role: "application", children: a.jsxs(am, { nodes: e, edges: t, width: ho, height: _r, fitView: ro, fitViewOptions: io, minZoom: je, maxZoom: Le, nodeOrigin: $e, nodeExtent: Rt, zIndexMode: xn, children: [a.jsx(Lg, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: g, onConnectStart: y, onConnectEnd: v, onClickConnectStart: w, onClickConnectEnd: m, nodesDraggable: ke, autoPanOnNodeFocus: Ie, nodesConnectable: Pe, nodesFocusable: ge, edgesFocusable: B, edgesReconnectable: re, elementsSelectable: xe, elevateNodesOnSelect: xr, elevateEdgesOnSelect: yn, minZoom: je, maxZoom: Le, nodeExtent: Rt, onNodesChange: hn, onEdgesChange: pn, snapToGrid: Q, snapGrid: le, connectionMode: L, translateExtent: Ye, connectOnClick: mr, defaultEdgeOptions: co, fitView: ro, fitViewOptions: io, onNodesDelete: _, onEdgesDelete: k, onDelete: R, onNodeDragStart: A, onNodeDrag: P, onNodeDragStop: T, onSelectionDrag: j, onSelectionDragStart: S, onSelectionDragStop: C, onMove: f, onMoveStart: u, onMoveEnd: h, noPanClassName: oo, nodeOrigin: $e, rfId: Ot, autoPanOnConnect: wr, autoPanOnNodeDrag: vr, autoPanSpeed: Nr, onError: Er, connectionRadius: mn, isValidConnection: Sr, selectNodesOnDrag: he, nodeDragThreshold: Ir, connectionDragThreshold: Cr, onBeforeDelete: W, debug: Mr, ariaLabelConfig: Dr, zIndexMode: xn }), a.jsx(om, { onInit: l, onNodeClick: c, onEdgeClick: d, onNodeMouseEnter: b, onNodeMouseMove: p, onNodeMouseLeave: x, onNodeContextMenu: I, onNodeDoubleClick: E, nodeTypes: i, edgeTypes: s, connectionLineType: O, connectionLineStyle: U, connectionLineComponent: K, connectionLineContainerStyle: G, selectionKeyCode: ee, selectionOnDrag: H, selectionMode: Z, deleteKeyCode: se, multiSelectionKeyCode: oe, panActivationKeyCode: ae, zoomActivationKeyCode: q, onlyRenderVisibleElements: de, defaultViewport: we, translateExtent: Ye, minZoom: je, maxZoom: Le, preventScrolling: an, zoomOnScroll: Lt, zoomOnPinch: ut, zoomOnDoubleClick: Qn, panOnScroll: vt, panOnScrollSpeed: bt, panOnScrollMode: nt, panOnDrag: Oe, autoPanOnSelection: br, onPaneClick: eo, onPaneMouseEnter: Se, onPaneMouseMove: qe, onPaneMouseLeave: ln, onPaneScroll: Ee, onPaneContextMenu: Nt, paneClickDistance: dn, nodeClickDistance: Te, onSelectionContextMenu: M, onSelectionStart: z, onSelectionEnd: $, onReconnect: ft, onReconnectStart: gr, onReconnectEnd: to, onEdgeContextMenu: no, onEdgeDoubleClick: un, onEdgeMouseEnter: yr, onEdgeMouseMove: fn, onEdgeMouseLeave: Vt, reconnectRadius: Fe, defaultMarkerColor: cn, noDragClassName: gn, noWheelClassName: Ht, noPanClassName: oo, rfId: Ot, disableKeyboardA11y: lo, nodeExtent: Rt, viewport: jr, onViewportChange: fo }), a.jsx($g, { onSelectionChange: F }), St, a.jsx(_g, { proOptions: ao, position: so }), a.jsx(jg, { rfId: Ot, disableKeyboardA11y: lo })] }) });
}
var Cl = ol(lm);
const dm = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function um({ children: e }) {
  const t = fe(dm);
  return t ? bg.createPortal(e, t) : null;
}
function fm({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return a.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ae(["react-flow__background-pattern", n, o]) });
}
function hm({ radius: e, className: t }) {
  return a.jsx("circle", { cx: e, cy: e, r: e, className: Ae(["react-flow__background-pattern", "dots", t]) });
}
var yt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(yt || (yt = {}));
const pm = {
  [yt.Dots]: 1,
  [yt.Lines]: 1,
  [yt.Cross]: 6
}, gm = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function jl({
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
  const u = ce(null), { transform: h, patternId: g } = fe(gm, ve), y = o || pm[t], v = t === yt.Dots, w = t === yt.Cross, m = Array.isArray(n) ? n : [n, n], b = [m[0] * h[2] || 1, m[1] * h[2] || 1], p = y * h[2], x = Array.isArray(i) ? i : [i, i], I = w ? [p, p] : b, E = [
    x[0] * h[2] || 1 + I[0] / 2,
    x[1] * h[2] || 1 + I[1] / 2
  ], A = `${g}${e || ""}`;
  return a.jsxs("svg", { className: Ae(["react-flow__background", l]), style: {
    ...d,
    ...dr,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": s
  }, ref: u, "data-testid": "rf__background", children: [a.jsx("pattern", { id: A, x: h[0] % b[0], y: h[1] % b[1], width: b[0], height: b[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${E[0]},-${E[1]})`, children: v ? a.jsx(hm, { radius: p / 2, className: f }) : a.jsx(fm, { dimensions: I, lineWidth: r, variant: t, className: f }) }), a.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${A})` })] });
}
jl.displayName = "Background";
const _l = Ce(jl);
function ym() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: a.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function mm() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: a.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function xm() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: a.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function wm() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: a.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function vm() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: a.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function Co({ children: e, className: t, ...n }) {
  return a.jsx("button", { type: "button", className: Ae(["react-flow__controls-button", t]), ...n, children: e });
}
const bm = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function Al({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: i, onZoomOut: s, onFitView: c, onInteractiveChange: d, className: l, children: f, position: u = "bottom-left", orientation: h = "vertical", "aria-label": g }) {
  const y = be(), { isInteractive: v, minZoomReached: w, maxZoomReached: m, ariaLabelConfig: b } = fe(bm, ve), { zoomIn: p, zoomOut: x, fitView: I } = Li(), E = () => {
    p(), i?.();
  }, A = () => {
    x(), s?.();
  }, P = () => {
    I(r), c?.();
  }, T = () => {
    y.setState({
      nodesDraggable: !v,
      nodesConnectable: !v,
      elementsSelectable: !v
    }), d?.(!v);
  }, _ = h === "horizontal" ? "horizontal" : "vertical";
  return a.jsxs(lr, { className: Ae(["react-flow__controls", _, l]), position: u, style: e, "data-testid": "rf__controls", "aria-label": g ?? b["controls.ariaLabel"], children: [t && a.jsxs(a.Fragment, { children: [a.jsx(Co, { onClick: E, className: "react-flow__controls-zoomin", title: b["controls.zoomIn.ariaLabel"], "aria-label": b["controls.zoomIn.ariaLabel"], disabled: m, children: a.jsx(ym, {}) }), a.jsx(Co, { onClick: A, className: "react-flow__controls-zoomout", title: b["controls.zoomOut.ariaLabel"], "aria-label": b["controls.zoomOut.ariaLabel"], disabled: w, children: a.jsx(mm, {}) })] }), n && a.jsx(Co, { className: "react-flow__controls-fitview", onClick: P, title: b["controls.fitView.ariaLabel"], "aria-label": b["controls.fitView.ariaLabel"], children: a.jsx(xm, {}) }), o && a.jsx(Co, { className: "react-flow__controls-interactive", onClick: T, title: b["controls.interactive.ariaLabel"], "aria-label": b["controls.interactive.ariaLabel"], children: v ? a.jsx(vm, {}) : a.jsx(wm, {}) }), f] });
}
Al.displayName = "Controls";
const Ml = Ce(Al);
function Nm({ id: e, x: t, y: n, width: o, height: r, style: i, color: s, strokeColor: c, strokeWidth: d, className: l, borderRadius: f, shapeRendering: u, selected: h, onClick: g }) {
  const { background: y, backgroundColor: v } = i || {}, w = s || y || v;
  return a.jsx("rect", { className: Ae(["react-flow__minimap-node", { selected: h }, l]), x: t, y: n, rx: f, ry: f, width: o, height: r, style: {
    fill: w,
    stroke: c,
    strokeWidth: d
  }, shapeRendering: u, onClick: g ? (m) => g(m, e) : void 0 });
}
const Sm = Ce(Nm), Em = (e) => e.nodes.map((t) => t.id), Gr = (e) => e instanceof Function ? e : () => e;
function km({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: i = Sm,
  onClick: s
}) {
  const c = fe(Em, ve), d = Gr(t), l = Gr(e), f = Gr(n), u = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return a.jsx(a.Fragment, { children: c.map((h) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    a.jsx(Cm, { id: h, nodeColorFunc: d, nodeStrokeColorFunc: l, nodeClassNameFunc: f, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: i, onClick: s, shapeRendering: u }, h)
  )) });
}
function Im({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: i, shapeRendering: s, NodeComponent: c, onClick: d }) {
  const { node: l, x: f, y: u, width: h, height: g } = fe((y) => {
    const v = y.nodeLookup.get(e);
    if (!v)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const w = v.internals.userNode, { x: m, y: b } = v.internals.positionAbsolute, { width: p, height: x } = dt(w);
    return {
      node: w,
      x: m,
      y: b,
      width: p,
      height: x
    };
  }, ve);
  return !l || l.hidden || !Ic(l) ? null : a.jsx(c, { x: f, y: u, width: h, height: g, style: l.style, selected: !!l.selected, className: o(l), color: t(l), borderRadius: r, strokeColor: n(l), strokeWidth: i, shapeRendering: s, onClick: d, id: l.id });
}
const Cm = Ce(Im);
var jm = Ce(km);
const _m = 200, Am = 150, Mm = (e) => !e.hidden, Dm = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? Ec(qn(e.nodeLookup, { filter: Mm }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, Pm = "react-flow__minimap-desc";
function Dl({
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
  onClick: g,
  onNodeClick: y,
  pannable: v = !1,
  zoomable: w = !1,
  ariaLabel: m,
  inversePan: b,
  zoomStep: p = 1,
  offsetScale: x = 5
}) {
  const I = be(), E = ce(null), { boundingRect: A, viewBB: P, rfId: T, panZoom: _, translateExtent: k, flowWidth: R, flowHeight: F, ariaLabelConfig: S } = fe(Dm, ve), j = e?.width ?? _m, C = e?.height ?? Am, M = A.width / j, z = A.height / C, $ = Math.max(M, z), W = $ * j, L = $ * C, O = x * $, U = A.x - (W - A.width) / 2 - O, K = A.y - (L - A.height) / 2 - O, G = W + O * 2, se = L + O * 2, ee = `${Pm}-${T}`, H = ce(0), Z = ce();
  H.current = $, ne(() => {
    if (E.current && _)
      return Z.current = qp({
        domNode: E.current,
        panZoom: _,
        getTransform: () => I.getState().transform,
        getViewScale: () => H.current
      }), () => {
        Z.current?.destroy();
      };
  }, [_]), ne(() => {
    Z.current?.update({
      translateExtent: k,
      width: R,
      height: F,
      inversePan: b,
      pannable: v,
      zoomStep: p,
      zoomable: w
    });
  }, [v, w, b, p, k, R, F]);
  const ae = g ? (Q) => {
    const [le, de] = Z.current?.pointer(Q) || [0, 0];
    g(Q, { x: le, y: de });
  } : void 0, oe = y ? ye((Q, le) => {
    const de = I.getState().nodeLookup.get(le).internals.userNode;
    y(Q, de);
  }, []) : void 0, q = m ?? S["minimap.ariaLabel"];
  return a.jsx(lr, { position: h, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof f == "string" ? f : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof u == "number" ? u * $ : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof s == "number" ? s : void 0
  }, className: Ae(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: a.jsxs("svg", { width: j, height: C, viewBox: `${U} ${K} ${G} ${se}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": ee, ref: E, onClick: ae, children: [q && a.jsx("title", { id: ee, children: q }), a.jsx(jm, { onClick: oe, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: i, nodeClassName: r, nodeStrokeWidth: s, nodeComponent: c }), a.jsx("path", { className: "react-flow__minimap-mask", d: `M${U - O},${K - O}h${G + O * 2}v${se + O * 2}h${-G - O * 2}z
        M${P.x},${P.y}h${P.width}v${P.height}h${-P.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
Dl.displayName = "MiniMap";
const Pl = Ce(Dl), $m = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, Tm = {
  [nn.Line]: "right",
  [nn.Handle]: "bottom-right"
};
function zm({ nodeId: e, position: t, variant: n = nn.Handle, className: o, style: r = void 0, children: i, color: s, minWidth: c = 10, minHeight: d = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: f = Number.MAX_VALUE, keepAspectRatio: u = !1, resizeDirection: h, autoScale: g = !0, shouldResize: y, onResizeStart: v, onResize: w, onResizeEnd: m }) {
  const b = al(), p = typeof e == "string" ? e : b, x = be(), I = ce(null), E = n === nn.Handle, A = fe(ye($m(E && g), [E, g]), ve), P = ce(null), T = t ?? Tm[n];
  ne(() => {
    if (!(!I.current || !p))
      return P.current || (P.current = sg({
        domNode: I.current,
        nodeId: p,
        getStoreItems: () => {
          const { nodeLookup: k, transform: R, snapGrid: F, snapToGrid: S, nodeOrigin: j, domNode: C } = x.getState();
          return {
            nodeLookup: k,
            transform: R,
            snapGrid: F,
            snapToGrid: S,
            nodeOrigin: j,
            paneDomNode: C
          };
        },
        onChange: (k, R) => {
          const { triggerNodeChanges: F, nodeLookup: S, parentLookup: j, nodeOrigin: C } = x.getState(), M = [], z = { x: k.x, y: k.y }, $ = S.get(p);
          if ($ && $.expandParent && $.parentId) {
            const W = $.origin ?? C, L = k.width ?? $.measured.width ?? 0, O = k.height ?? $.measured.height ?? 0, U = {
              id: $.id,
              parentId: $.parentId,
              rect: {
                width: L,
                height: O,
                ...Cc({
                  x: k.x ?? $.position.x,
                  y: k.y ?? $.position.y
                }, { width: L, height: O }, $.parentId, S, W)
              }
            }, K = Ri([U], S, j, C);
            M.push(...K), z.x = k.x ? Math.max(W[0] * L, k.x) : void 0, z.y = k.y ? Math.max(W[1] * O, k.y) : void 0;
          }
          if (z.x !== void 0 && z.y !== void 0) {
            const W = {
              id: p,
              type: "position",
              position: { ...z }
            };
            M.push(W);
          }
          if (k.width !== void 0 && k.height !== void 0) {
            const L = {
              id: p,
              type: "dimensions",
              resizing: !0,
              setAttributes: h ? h === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: k.width,
                height: k.height
              }
            };
            M.push(L);
          }
          for (const W of R) {
            const L = {
              ...W,
              type: "position"
            };
            M.push(L);
          }
          F(M);
        },
        onEnd: ({ width: k, height: R }) => {
          const F = {
            id: p,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: k,
              height: R
            }
          };
          x.getState().triggerNodeChanges([F]);
        }
      })), P.current.update({
        controlPosition: T,
        boundaries: {
          minWidth: c,
          minHeight: d,
          maxWidth: l,
          maxHeight: f
        },
        keepAspectRatio: u,
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
    f,
    u,
    v,
    w,
    m,
    y
  ]);
  const _ = T.split("-");
  return a.jsx("div", { className: Ae(["react-flow__resize-control", "nodrag", ..._, n, o]), ref: I, style: {
    ...r,
    scale: A,
    ...s && { [E ? "backgroundColor" : "borderColor"]: s }
  }, children: i });
}
Ce(zm);
const Rm = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), $l = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var Lm = {
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
const Vm = Qo(
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
      ...Lm,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: $l("lucide", r),
      ...c
    },
    [
      ...s.map(([l, f]) => ei(l, f)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const Ne = (e, t) => {
  const n = Qo(
    ({ className: o, ...r }, i) => ei(Vm, {
      ref: i,
      iconNode: t,
      className: $l(`lucide-${Rm(e)}`, o),
      ...r
    })
  );
  return n.displayName = `${e}`, n;
};
const Tl = Ne("Boxes", [
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
const sn = Ne("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const zl = Ne("ChevronDown", [
  ["path", { d: "m6 9 6 6 6-6", key: "qrunsl" }]
]);
const Uo = Ne("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const Bt = Ne("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const et = Ne("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const Hm = Ne("Copy", [
  ["rect", { width: "14", height: "14", x: "8", y: "8", rx: "2", ry: "2", key: "17jyea" }],
  ["path", { d: "M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2", key: "zix9uf" }]
]);
const Hi = Ne("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const Om = Ne("GripVertical", [
  ["circle", { cx: "9", cy: "12", r: "1", key: "1vctgf" }],
  ["circle", { cx: "9", cy: "5", r: "1", key: "hp0tcf" }],
  ["circle", { cx: "9", cy: "19", r: "1", key: "fkjjf6" }],
  ["circle", { cx: "15", cy: "12", r: "1", key: "1tmaij" }],
  ["circle", { cx: "15", cy: "5", r: "1", key: "19l28e" }],
  ["circle", { cx: "15", cy: "19", r: "1", key: "f4zoj3" }]
]);
const Rl = Ne("ListTree", [
  ["path", { d: "M21 12h-8", key: "1bmf0i" }],
  ["path", { d: "M21 6H8", key: "1pqkrb" }],
  ["path", { d: "M21 18h-8", key: "1tm79t" }],
  ["path", { d: "M3 6v4c0 1.1.9 2 2 2h3", key: "1ywdgy" }],
  ["path", { d: "M3 10v6c0 1.1.9 2 2 2h3", key: "2wc746" }]
]);
const Ko = Ne("Maximize2", [
  ["polyline", { points: "15 3 21 3 21 9", key: "mznyad" }],
  ["polyline", { points: "9 21 3 21 3 15", key: "1avn1i" }],
  ["line", { x1: "21", x2: "14", y1: "3", y2: "10", key: "ota7mn" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const ua = Ne("Minimize2", [
  ["polyline", { points: "4 14 10 14 10 20", key: "11kfnr" }],
  ["polyline", { points: "20 10 14 10 14 4", key: "rlmsce" }],
  ["line", { x1: "14", x2: "21", y1: "10", y2: "3", key: "o5lafz" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const Fm = Ne("Package", [
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
const ur = Ne("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const Oi = Ne("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const Kn = Ne("RefreshCcw", [
  ["path", { d: "M21 12a9 9 0 0 0-9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "14sxne" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }],
  ["path", { d: "M3 12a9 9 0 0 0 9 9 9.75 9.75 0 0 0 6.74-2.74L21 16", key: "1hlbsb" }],
  ["path", { d: "M16 16h5v5", key: "ccwih5" }]
]);
const Ll = Ne("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const Bm = Ne("Save", [
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
const Vl = Ne("Search", [
  ["circle", { cx: "11", cy: "11", r: "8", key: "4ej97u" }],
  ["path", { d: "m21 21-4.3-4.3", key: "1qie3q" }]
]);
const mt = Ne("Sparkles", [
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
const Wm = Ne("Terminal", [
  ["polyline", { points: "4 17 10 11 4 5", key: "akl6gq" }],
  ["line", { x1: "12", x2: "20", y1: "19", y2: "19", key: "q2wloq" }]
]);
const Zt = Ne("Trash2", [
  ["path", { d: "M3 6h18", key: "d0wm0j" }],
  ["path", { d: "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", key: "4alrt4" }],
  ["path", { d: "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", key: "v07s0e" }],
  ["line", { x1: "10", x2: "10", y1: "11", y2: "17", key: "1uufr5" }],
  ["line", { x1: "14", x2: "14", y1: "11", y2: "17", key: "xtxkd" }]
]);
const Hl = Ne("X", [
  ["path", { d: "M18 6 6 18", key: "1bl5f8" }],
  ["path", { d: "m6 6 12 12", key: "d8bk6v" }]
]);
const Xm = Ne("Zap", [
  [
    "path",
    {
      d: "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z",
      key: "1xq2db"
    }
  ]
]), De = "/_elsa/workflow-management", Ym = "/publishing";
async function qm(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${De}/definitions?${n.toString()}`);
}
async function Zm(e, t) {
  return e.http.getJson(`${De}/definitions/${encodeURIComponent(t)}`);
}
async function Um(e, t) {
  return e.http.getJson(`${De}/versions/${encodeURIComponent(t)}`);
}
async function Km(e, t) {
  return e.http.postJson(`${De}/definitions`, t);
}
async function fa(e, t) {
  await e.http.deleteJson(`${De}/definitions/${encodeURIComponent(t)}`);
}
async function Gm(e, t) {
  await e.http.postJson(`${De}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function ha(e, t) {
  await e.http.deleteJson(`${De}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function Jm(e, t) {
  return e.http.putJson(`${De}/drafts/${encodeURIComponent(t.id)}`, { state: t.state, layout: t.layout });
}
async function Ol(e, t) {
  return e.http.postJson(`${De}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function Fl(e, t) {
  return e.http.postJson(`${De}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function Qm(e, t) {
  try {
    return await e.http.postJson(`${Ym}/workflows/drafts/test-runs`, t);
  } catch (n) {
    const o = ax(n);
    if (o) return o;
    throw n;
  }
}
async function Bl(e, t) {
  return e.http.postJson(`${De}/executables/${encodeURIComponent(t)}/run`, {});
}
async function Wl(e, t = "active") {
  const n = await e.http.getJson(`${De}/executables?state=${encodeURIComponent(t)}`);
  return Array.isArray(n) ? n : n.executables;
}
async function ex(e, t) {
  await e.http.deleteJson(`${De}/executables/${encodeURIComponent(t)}`);
}
async function tx(e, t) {
  await e.http.postJson(`${De}/executables/${encodeURIComponent(t)}/restore`, {});
}
async function nx(e, t) {
  await e.http.deleteJson(`${De}/executables/${encodeURIComponent(t)}/permanent`);
}
async function ox(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const o = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${o ? `?${o}` : ""}`);
}
async function rx(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function Fi(e) {
  return e.http.getJson(`${De}/activities`);
}
async function ix(e) {
  const t = await Xl(e, [
    `${De}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? pa(t) : pa(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function sx(e) {
  const t = await Xl(e, [
    `${De}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : To;
}
async function Xl(e, t) {
  let n;
  for (const o of t)
    try {
      return await e.http.getJson(o);
    } catch (r) {
      n = r;
    }
  throw n;
}
function pa(e) {
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
function ax(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = ga(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return ga(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function ga(e) {
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
], fr = "elsa.sequence.structure", Gn = "elsa.flowchart.structure";
function Yl(e, t) {
  if (!e) return null;
  let n = e;
  for (const o of t) {
    const r = Je(n).find((s) => s.id === o.slotId);
    if (!r) return null;
    const i = r.activities.find((s) => s.nodeId === o.ownerNodeId);
    if (!i) return null;
    n = i;
  }
  return n;
}
function Dn(e, t) {
  const n = Yl(e, t);
  if (!n) return null;
  let o = Je(n)[0];
  return o ? { owner: n, slot: o } : null;
}
function Je(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = Cx(t), r = Jr(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: jx(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, i]) => Jr(i)).map(([i, s]) => ({
    id: `${t.kind}:${i}`,
    label: Ax(i),
    property: i,
    mode: "generic",
    activities: Jr(s) ?? []
  }));
}
function ql(e, t, n) {
  const o = new Map(t.map((s) => [s.activityVersionId, s])), r = new Map(n.map((s) => [s.nodeId, s])), i = e.slot.activities.map((s, c) => {
    const d = o.get(s.activityVersionId), l = r.get(s.nodeId) ?? _x(e.slot.mode, c);
    return Kl(s, d, { x: l.x, y: l.y });
  });
  return {
    nodes: i,
    edges: e.slot.mode === "flowchart" ? wx(e.owner) : xx(e.slot, i)
  };
}
function pi(e, t, n) {
  const o = t.find((i) => i.activityVersionId === e.activityVersionId), r = n.find((i) => i.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [Kl(e, o, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function cx(e, t, n, o = null) {
  const r = new Map(t.map((c) => [c.activityExecutionId, c])), i = ma(t, (c) => c.authoredActivityId || c.executableNodeId), s = ma(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? r.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const d = i.get(c.id) ?? [], l = s.get(c.id) ?? [];
    if (d.length === 0 && l.length === 0) return c;
    const f = Ex(d), u = o === c.id || d.some((g) => g.activityExecutionId === o) || l.some((g) => g.incidentId === o), h = {
      status: f?.status,
      subStatus: f?.subStatus,
      activityExecutionId: f?.activityExecutionId,
      faultCount: d.reduce((g, y) => g + y.faultCount + y.aggregateFaultCount, 0),
      incidentCount: l.length,
      hasBlockingIncident: l.some((g) => g.isBlocking),
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
function Bi(e, t) {
  return e?.structure?.kind === Gn || hx(t) ? "flowchart" : e?.structure?.kind === fr || px(t) ? "sequence" : "unsupported";
}
function gi(e, t, n) {
  if (t.length === 0) {
    const c = Je(e)[0];
    return c ? Fn(e, c, n) : e;
  }
  const [o, ...r] = t, i = Je(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((c) => c.nodeId === o.ownerNodeId ? gi(c, r, n) : c);
  return Fn(e, i, s);
}
function Zl(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, i = Je(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((c) => c.nodeId === o.ownerNodeId ? Zl(c, r, n) : c);
  return Fn(e, i, s);
}
function Ul(e, t, n) {
  if (e.nodeId === t) return n(e);
  const o = Je(e);
  if (o.length === 0) return e;
  let r = !1, i = e;
  for (const s of o) {
    const c = s.activities.map((d) => {
      const l = Ul(d, t, n);
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
function lx(e, t, n, o = []) {
  const r = new Map(e.slot.activities.map((s) => [s.nodeId, s]));
  for (const s of o)
    r.set(s.nodeId, s);
  const i = t.map((s) => r.get(s.id)).filter((s) => !!s);
  return e.slot.mode === "sequence" && i.sort((s, c) => {
    const d = t.find((f) => f.id === s.nodeId), l = t.find((f) => f.id === c.nodeId);
    return (d?.position.x ?? 0) - (l?.position.x ?? 0);
  }), Fn(e.owner, e.slot, i);
}
function dx(e, t) {
  return {
    ...e,
    structure: mx(e.structure, t)
  };
}
function ux(e, t) {
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
    structure: yx(e)
  };
}
function Me(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? gx(t) : n;
}
function Kl(e, t, n, o = {}) {
  return {
    id: e.nodeId,
    type: "workflowActivity",
    position: n,
    connectable: o.connectable,
    deletable: o.deletable,
    draggable: o.draggable,
    data: {
      label: t ? Me(t) : e.activityVersionId,
      activityVersionId: e.activityVersionId,
      activityTypeKey: t?.activityTypeKey,
      category: t?.category,
      executionType: t?.executionType,
      icon: mi(t),
      childSlots: Je(e),
      acceptsInbound: vx(e, t),
      sourcePorts: o.suppressFlowPorts ? [] : Gl(e, t),
      suppressFlowPorts: o.suppressFlowPorts
    }
  };
}
function mi(e) {
  if (!e) return "activity";
  const t = fx(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), o = Me(e).toLowerCase(), r = e.category?.toLowerCase() ?? "", i = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || o === "flowchart" ? "flowchart" : n.endsWith(".sequence") || o === "sequence" ? "sequence" : n.includes("writeline") || o.includes("write line") ? "terminal" : r.includes("runtime") ? "runtime" : i === "trigger" ? "trigger" : "activity";
}
function fx(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function hx(e) {
  return !!e && (Me(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function px(e) {
  return !!e && (Me(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function gx(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function yx(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: fr,
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
function mx(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], o = /* @__PURE__ */ new Map();
  for (const r of n) {
    if (!Wi(r)) continue;
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
function xx(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function wx(e) {
  if (e.structure?.kind !== Gn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, i = n.target;
    if (!r?.nodeId || !i?.nodeId) return null;
    const s = Array.isArray(n.vertices) ? n.vertices.filter(kx) : [];
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
function Gl(e, t) {
  const n = ya(e.cases);
  if (Nx(e, t) && n.length > 0)
    return [...n.map((i) => ({ name: i, displayName: i })), { name: "Default", displayName: "Default" }];
  const o = [
    ...zo(t?.designFacets),
    ...zo(t?.ports),
    ...zo(t?.outputs)
  ];
  if (o.length > 0) return Sx(o);
  const r = ya(e.outcomes);
  return r.length > 0 ? r.map((i) => ({ name: i, displayName: i })) : [{ name: "Done", displayName: "Done" }];
}
function vx(e, t) {
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
function bx(e, t, n) {
  const o = Go(t.source, n, t.sourceHandle ?? "Done", void 0), r = Go(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((i) => i.id !== t.id).concat(o, r);
}
function Jr(e) {
  return Array.isArray(e) ? e.filter(Ix) : null;
}
function Nx(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function zo(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!Wi(n)) continue;
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
function Sx(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function ya(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function ma(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const o of e) {
    const r = t(o);
    r && n.set(r, [...n.get(r) ?? [], o]);
  }
  return n;
}
function Ex(e) {
  return [...e].sort((t, n) => xa(n).localeCompare(xa(t)))[0];
}
function xa(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function kx(e) {
  return Wi(e) && typeof e.x == "number" && typeof e.y == "number";
}
function Wi(e) {
  return typeof e == "object" && e !== null;
}
function Ix(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Cx(e) {
  return e.kind === fr ? "sequence" : e.kind === Gn ? "flowchart" : "generic";
}
function jx(e) {
  return e.kind === fr || e.kind === Gn, "Activities";
}
function _x(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function Ax(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Mx = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function Jl(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function Xi(e) {
  return Jl(e.name);
}
function Dx(e, t) {
  const n = Xi(t), o = e[n];
  return t.isWrapped === !1 ? o ?? t.defaultValue ?? "" : ed(o, t);
}
function Ql(e, t) {
  return ed(e[Xi(t)], t);
}
function Px(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function $x(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function wa(e, t, n) {
  return {
    ...e,
    [Xi(t)]: n
  };
}
function Tx(e, t) {
  return t.isWrapped === !1 ? Dx(e, t) : Ql(e, t).expression.value;
}
function ed(e, t) {
  return zx(e) ? {
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
function zx(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
const td = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]);
function Rx({
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
  const s = t.inputs.filter((l) => l.isBrowsable !== !1).sort((l, f) => (l.order ?? 0) - (f.order ?? 0) || l.name.localeCompare(f.name));
  if (s.length === 0)
    return /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "This activity does not expose editable properties." });
  const c = Ox(s), d = o.length > 0 ? o : Mx;
  return /* @__PURE__ */ a.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ a.jsx("span", { className: "wf-section-label", children: "Properties" }),
    c.map((l) => /* @__PURE__ */ a.jsxs("section", { className: "wf-property-group", children: [
      c.length > 1 ? /* @__PURE__ */ a.jsx("h4", { children: l.category }) : null,
      l.inputs.map((f) => /* @__PURE__ */ a.jsx(
        Lx,
        {
          activity: e,
          input: f,
          editors: n,
          expressionDescriptors: d,
          onChange: i
        },
        f.name
      ))
    ] }, l.category))
  ] });
}
function Lx({
  activity: e,
  input: t,
  editors: n,
  expressionDescriptors: o,
  onChange: r
}) {
  const i = t.isReadOnly === !0, s = { activity: e, expressionDescriptors: o, readOnly: i }, c = Hx(n, t, s), d = c?.component, l = t.isWrapped !== !1 ? Ql(e, t) : null, f = l?.expression.type ?? "Literal", u = Tx(e, t), h = !!(l && Fx(t, c?.id)), g = !!(l && Bx(t, c?.id)), [y, v] = Y(!1), w = (b) => {
    const p = l ? Px(l, b) : b;
    r(wa(e, t, p));
  }, m = (b) => {
    l && r(wa(e, t, $x(l, b)));
  };
  return /* @__PURE__ */ a.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ a.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ a.jsx("span", { children: nd(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ a.jsx("p", { children: t.description }) : null,
    l && !h ? /* @__PURE__ */ a.jsx(
      xi,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: f,
        descriptors: o,
        disabled: i,
        onChange: m
      }
    ) : null,
    h ? /* @__PURE__ */ a.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ a.jsx("div", { className: "wf-expression-editor", children: va(d, t, u, i, s, w) }),
      /* @__PURE__ */ a.jsx(
        xi,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: f,
          descriptors: o,
          disabled: i,
          variant: "inline",
          onChange: m
        }
      ),
      g ? /* @__PURE__ */ a.jsx(
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
    ] }) : va(d, t, u, i, s, w),
    g && !h ? /* @__PURE__ */ a.jsxs(
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
      Vx,
      {
        input: t,
        value: u,
        syntax: f,
        descriptors: o,
        disabled: i,
        onChange: w,
        onSyntaxChange: m,
        onClose: () => v(!1)
      }
    ) : null
  ] });
}
function Vx({
  input: e,
  value: t,
  syntax: n,
  descriptors: o,
  disabled: r,
  onChange: i,
  onSyntaxChange: s,
  onClose: c
}) {
  const d = Ha(), l = e.displayName || e.name;
  return ne(() => {
    const f = (u) => {
      u.key === "Escape" && c();
    };
    return window.addEventListener("keydown", f), () => window.removeEventListener("keydown", f);
  }, [c]), /* @__PURE__ */ a.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ a.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": d, children: [
    /* @__PURE__ */ a.jsxs("header", { children: [
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ a.jsx("h3", { id: d, children: l })
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", "aria-label": `Close ${l} editor`, onClick: c, children: /* @__PURE__ */ a.jsx(Hl, { size: 16 }) })
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
        /* @__PURE__ */ a.jsx("span", { children: nd(e.typeName) })
      ] }),
      e.description ? /* @__PURE__ */ a.jsx("p", { children: e.description }) : null,
      /* @__PURE__ */ a.jsx(
        "textarea",
        {
          "aria-label": `${l} expanded value`,
          value: t == null ? "" : String(t),
          disabled: r,
          spellCheck: !1,
          onChange: (f) => i(f.target.value)
        }
      )
    ] }),
    /* @__PURE__ */ a.jsxs("footer", { children: [
      /* @__PURE__ */ a.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: c, children: "Close" })
    ] })
  ] }) });
}
function va(e, t, n, o, r, i) {
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
  const [s, c] = Y(!1), d = Ha(), l = n.find((u) => u.type === t), f = [
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
      const h = u.displayName || u.type, g = u.type === t;
      return /* @__PURE__ */ a.jsx(
        "button",
        {
          type: "button",
          role: "option",
          "aria-selected": g,
          className: g ? "selected" : "",
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
function Hx(e, t, n) {
  return [...e].sort((o, r) => (o.order ?? 500) - (r.order ?? 500)).find((o) => o.supports(t, n));
}
function Ox(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "General";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return [...t.entries()].map(([n, o]) => ({ category: n, inputs: o }));
}
function nd(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function Fx(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !td.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function Bx(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !td.has(t) && n !== "multiline") return !1;
  const o = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(o) || n === "singleline" || n === "multiline";
}
const ba = "elsa-studio:apply-workflow-graph-operation-batch", Na = "elsa-studio:undo-workflow-graph-operation-batch", Wx = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function Xx(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const o = Qx(e), r = rd(o.state.rootActivity), i = /* @__PURE__ */ new Map(), s = /* @__PURE__ */ new Map(), c = [];
  for (const d of t.operations) {
    const l = Jx(d.kind), f = d.parameters ?? {};
    if (l === "add-activity") {
      const u = Ve(f.activityId) ?? d.temporaryReferences?.[0], h = Gx(u ?? Ve(f.displayName) ?? Ve(f.activityType) ?? "weaver-activity", r), g = Yx(d, h, n);
      s.set(h, g), c.push(h), u && i.set(u, h), o.state.rootActivity && qx(o.state.rootActivity, g);
      const y = _t(f.position) ? wi(f.position, { x: 280, y: 160 }) : null;
      y && (o.layout = Sa(o.layout, h, y));
      continue;
    }
    if (l === "set-root") {
      const u = Qr(o, f.activityId, i, s);
      if (!u) throw new Error("Weaver batch referenced an unknown root activity.");
      o.state.rootActivity = u;
      continue;
    }
    if (l === "set-designer-position") {
      const u = Tt(f.activityId, i);
      if (!u || !Yi(o.state.rootActivity, u)) throw new Error("Weaver batch referenced an unknown activity position.");
      o.layout = Sa(o.layout, u, wi(f, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const u = Qr(o, f.activityId, i, s);
      if (!u) throw new Error("Weaver batch referenced an unknown activity property target.");
      Kx(u, Ve(f.propertyName) ?? "Value", f.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const u = Qr(o, f.activityId, i, s);
      if (!u) throw new Error("Weaver batch referenced an unknown activity update target.");
      const h = _t(f.patch) ? f.patch : f;
      Object.assign(u, h);
      continue;
    }
    if (l === "remove-activity") {
      const u = Tt(f.activityId, i);
      if (!u) throw new Error("Weaver batch referenced an unknown activity remove target.");
      o.state.rootActivity = od(o.state.rootActivity, u), o.layout = o.layout.filter((h) => h.nodeId !== u);
      continue;
    }
    if (l === "connect-activities") {
      Zx(o, f, i);
      continue;
    }
    if (l === "disconnect-activities") {
      Ux(o, f, i);
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
function Yx(e, t, n) {
  const o = e.parameters ?? {}, r = Ve(o.activityVersionId) ?? Ve(o.activityType) ?? "Elsa.Workflows.Activity", i = n.find((s) => s.activityVersionId === r || s.activityTypeKey === r || s.displayName === Ve(o.displayName));
  return i ? yi(i, t) : {
    nodeId: t,
    activityVersionId: i?.activityVersionId ?? r,
    inputs: [],
    outputs: [],
    ...Ve(o.displayName) ? { displayName: Ve(o.displayName) } : {},
    designer: { position: wi(o.position, { x: 280, y: 160 }) }
  };
}
function qx(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = qi(e);
  n && !n.some((o) => o.nodeId === t.nodeId) && n.push(t);
}
function Zx(e, t, n) {
  const o = e.state.rootActivity;
  if (!o?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const r = Tt(t.sourceActivityId ?? t.sourceId ?? t.from, n), i = Tt(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!r || !i) throw new Error("Weaver batch connection is missing source or target activity.");
  const s = o.structure.payload, c = Array.isArray(s.connections) ? s.connections : [], d = Ve(t.connectionId) ?? `flow-${r}-${i}`;
  s.connections = [
    ...c.filter((l) => !_t(l) || l.id !== d),
    {
      id: d,
      source: { nodeId: r, port: Ve(t.outcome) ?? Ve(t.sourcePort) ?? "Done" },
      target: { nodeId: i }
    }
  ];
}
function Ux(e, t, n) {
  const o = e.state.rootActivity, r = o?.structure?.payload.connections;
  if (!Array.isArray(r)) return;
  const i = Ve(t.connectionId), s = Tt(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = Tt(t.targetActivityId ?? t.targetId ?? t.to, n);
  o.structure.payload.connections = r.filter((d) => {
    if (!_t(d)) return !0;
    if (i && d.id === i) return !1;
    const l = _t(d.source) ? d.source.nodeId : void 0, f = _t(d.target) ? d.target.nodeId : void 0;
    return l !== s || f !== c;
  });
}
function Kx(e, t, n) {
  e[Jl(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: "Literal", value: n }
  };
}
function Qr(e, t, n, o) {
  const r = Tt(t, n);
  return r ? Yi(e.state.rootActivity, r) ?? o.get(r) ?? null : null;
}
function Tt(e, t) {
  const n = Ve(e);
  return n ? t.get(n) ?? n : null;
}
function Yi(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of id(e)) {
    const o = Yi(n, t);
    if (o) return o;
  }
  return null;
}
function od(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = qi(e);
  if (n) {
    const o = n.map((r) => od(r, t)).filter((r) => !!r);
    n.splice(0, n.length, ...o);
  }
  return e;
}
function rd(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of id(e)) rd(n, t);
  return t;
}
function id(e) {
  return qi(e) ?? [];
}
function qi(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function Sa(e, t, n) {
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
function Gx(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let o = n, r = 2;
  for (; t.has(o); )
    o = `${n}-${r}`, r += 1;
  return t.add(o), o;
}
function Jx(e) {
  return typeof e == "number" ? Wx[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Ve(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function Qx(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function _t(e) {
  return typeof e == "object" && e !== null;
}
const sd = { workflowActivity: H0 }, ad = { workflow: F0 }, Ea = "application/x-elsa-activity-version-id", e0 = 6, t0 = 1200, n0 = [10, 25, 50], o0 = 10, ka = "elsa-studio-workflow-palette-width", Ia = "elsa-studio-workflow-inspector-width", Ca = "elsa-studio-workflow-palette-collapsed", ja = "elsa-studio-workflow-inspector-collapsed", cd = "elsa-studio-workflow-side-panel-maximized", Nn = 180, Sn = 460, r0 = 260, En = 260, kn = 560, i0 = 320, _a = 42, jo = 16, ld = wt.createContext(null);
function nw(e) {
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
        component: () => /* @__PURE__ */ a.jsx(s0, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), workflowDesignerPanels: e.workflowDesigner.panels.list() })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ a.jsx(a0, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow instances",
        component: () => /* @__PURE__ */ a.jsx(c0, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow instance",
        component: () => /* @__PURE__ */ a.jsx(l0, { context: e.backend, ai: e.ai })
      }
    ]
  });
}
function s0({
  context: e,
  ai: t,
  propertyEditors: n,
  workflowDesignerPanels: o
}) {
  const [r, i] = Y(Aa);
  ne(() => {
    const c = () => i(Aa());
    return window.addEventListener("popstate", c), () => window.removeEventListener("popstate", c);
  }, []);
  const s = (c) => {
    const d = c ? `/workflows/definitions?definition=${encodeURIComponent(c)}` : "/workflows/definitions";
    window.history.pushState({}, "", d), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return r ? /* @__PURE__ */ a.jsx(V0, { context: e, definitionId: r, ai: t, propertyEditors: n, workflowDesignerPanels: o, onBack: () => s(null) }) : /* @__PURE__ */ a.jsx(hr, { title: "Definitions", children: /* @__PURE__ */ a.jsx(u0, { context: e, ai: t, onOpen: s }) });
}
function a0({ context: e, ai: t }) {
  const [n, o] = Y(Ma);
  ne(() => {
    const i = () => o(Ma());
    return window.addEventListener("popstate", i), () => window.removeEventListener("popstate", i);
  }, []);
  const r = ye((i) => {
    const s = i?.trim() ?? "", c = new URL(window.location.href);
    s ? c.searchParams.set("definition", s) : c.searchParams.delete("definition"), o(s || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ a.jsx(hr, { title: "Executables", children: /* @__PURE__ */ a.jsx(h0, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: r }) });
}
function c0({ context: e, ai: t }) {
  return /* @__PURE__ */ a.jsx(hr, { title: "Instances", children: /* @__PURE__ */ a.jsx(y0, { context: e, ai: t }) });
}
function l0({ context: e, ai: t }) {
  const n = d0();
  return /* @__PURE__ */ a.jsx(hr, { title: "Instance", children: /* @__PURE__ */ a.jsx(m0, { context: e, ai: t, workflowExecutionId: n }) });
}
function hr({ title: e, children: t }) {
  return /* @__PURE__ */ a.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ a.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ a.jsxs("div", { children: [
      /* @__PURE__ */ a.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ a.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function Aa() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Ma() {
  return new URLSearchParams(window.location.search).get("definition");
}
function d0() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function u0({ context: e, ai: t, onOpen: n }) {
  const [o, r] = Y(""), [i, s] = Y("active"), [c, d] = Y(1), [l, f] = Y(o0), [u, h] = Y("loading"), [g, y] = Y(""), [v, w] = Y(""), [m, b] = Y([]), [p, x] = Y(0), [I, E] = Y(() => /* @__PURE__ */ new Set()), [A, P] = Y("idle"), [T, _] = Y(null), [k, R] = Y(!1), [F, S] = Y([]), [j, C] = Y("idle"), M = ce(null), z = me(() => m.map((B) => B.id), [m]), $ = zt(t, "weaver.workflows.suggest-create-metadata"), W = zt(t, "weaver.workflows.explain-definition"), L = me(() => m.filter((B) => I.has(B.id)), [m, I]), O = me(() => L.filter((B) => !!B.draftId), [L]), U = z.filter((B) => I.has(B)).length, K = z.length > 0 && U === z.length, G = A !== "idle", se = ye(async () => {
    h("loading"), y("");
    try {
      const B = await qm(e, { search: o, state: i, page: c, pageSize: l }), re = typeof B.totalCount == "number", xe = B.totalCount ?? B.definitions.length, we = dd(xe, l);
      if (xe > 0 && c > we) {
        d(we);
        return;
      }
      b(re ? B.definitions : k0(B.definitions, c, l)), x(xe), h("ready");
    } catch (B) {
      y(B instanceof Error ? B.message : String(B)), h("failed");
    }
  }, [e, o, i, c, l]);
  ne(() => {
    se();
  }, [se]), ne(() => {
    M.current && (M.current.indeterminate = U > 0 && !K);
  }, [K, U]);
  const ee = ye(async () => {
    if (!(j === "loading" || j === "ready")) {
      C("loading");
      try {
        const B = await Fi(e);
        S(B.activities ?? []), C("ready");
      } catch (B) {
        C("failed"), y(B instanceof Error ? B.message : String(B));
      }
    }
  }, [j, e]), H = () => {
    y(""), w(""), _({ name: "", description: "", rootKind: "flowchart" }), ee();
  }, Z = async () => {
    if (T?.name.trim()) {
      R(!0), y(""), w("");
      try {
        const B = await Km(e, {
          name: T.name.trim(),
          description: T.description.trim() || null,
          rootKind: T.rootKind,
          rootActivityVersionId: j0(T, F)
        });
        _(null), n(B.definition.id);
      } catch (B) {
        y(B instanceof Error ? B.message : String(B));
      } finally {
        R(!1);
      }
    }
  }, ae = (B) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(B)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, oe = async () => {
    if (m.length === 1 && c > 1) {
      d(c - 1);
      return;
    }
    await se();
  }, q = () => E(/* @__PURE__ */ new Set()), Q = (B, re) => {
    E((xe) => {
      const we = new Set(xe);
      return re ? we.add(B) : we.delete(B), we;
    });
  }, le = (B) => {
    E((re) => {
      const xe = new Set(re);
      for (const we of z)
        B ? xe.add(we) : xe.delete(we);
      return xe;
    });
  }, de = (B) => {
    s(B), d(1), q();
  }, he = (B) => {
    r(B), d(1), q();
  }, ke = async () => {
    if (L.length === 0 || G) return;
    const B = i === "deleted", re = B ? `Permanently delete ${L.length} selected workflow definition${L.length === 1 ? "" : "s"}? This cannot be undone.` : `Delete ${L.length} selected workflow definition${L.length === 1 ? "" : "s"}? You can restore them from the Deleted view.`;
    if (window.confirm(re)) {
      P("deleting"), w(B ? "Deleting selected definitions..." : "Moving selected definitions to Deleted..."), y("");
      try {
        await Promise.all(L.map((xe) => B ? ha(e, xe.id) : fa(e, xe.id))), q(), w(B ? `Deleted ${L.length} workflow definition${L.length === 1 ? "" : "s"} permanently` : `Deleted ${L.length} workflow definition${L.length === 1 ? "" : "s"}`), await oe();
      } catch (xe) {
        w(""), y(xe instanceof Error ? xe.message : String(xe));
      } finally {
        P("idle");
      }
    }
  }, Ie = async () => {
    if (!(O.length === 0 || G) && window.confirm(`Publish drafts for ${O.length} selected workflow definition${O.length === 1 ? "" : "s"}?`)) {
      P("publishing"), w("Publishing selected definitions..."), y("");
      try {
        const B = await Promise.all(O.map(async (xe) => {
          const we = await Ol(e, xe.draftId);
          return Fl(e, we.versionId);
        }));
        q();
        const re = L.length - O.length;
        w(`Published ${B.length} workflow definition${B.length === 1 ? "" : "s"}${re > 0 ? `; skipped ${re} without drafts` : ""}`), await se();
      } catch (B) {
        w(""), y(B instanceof Error ? B.message : String(B));
      } finally {
        P("idle");
      }
    }
  }, Pe = async (B) => {
    if (window.confirm(`Delete workflow definition "${B.name}"? You can restore it from the Deleted view.`)) {
      w(""), y("");
      try {
        await fa(e, B.id), Q(B.id, !1), w(`Deleted ${B.name}`), await oe();
      } catch (re) {
        y(re instanceof Error ? re.message : String(re));
      }
    }
  }, ge = async (B) => {
    w(""), y("");
    try {
      await Gm(e, B.id), Q(B.id, !1), w(`Restored ${B.name}`), await oe();
    } catch (re) {
      y(re instanceof Error ? re.message : String(re));
    }
  }, $e = async (B) => {
    if (window.confirm(`Permanently delete workflow definition "${B.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`)) {
      w(""), y("");
      try {
        await ha(e, B.id), Q(B.id, !1), w(`Permanently deleted ${B.name}`), await oe();
      } catch (re) {
        y(re instanceof Error ? re.message : String(re));
      }
    }
  };
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ a.jsx("button", { type: "button", className: i === "active" ? "active" : "", "aria-selected": i === "active", onClick: () => de("active"), children: "Active" }),
        /* @__PURE__ */ a.jsx("button", { type: "button", className: i === "deleted" ? "active" : "", "aria-selected": i === "deleted", onClick: () => de("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ a.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ a.jsx(Vl, { size: 15 }),
        /* @__PURE__ */ a.jsx("input", { value: o, onChange: (B) => he(B.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", className: "wf-icon-button", "aria-label": "Refresh workflow definitions", title: "Refresh", onClick: () => {
        se();
      }, children: /* @__PURE__ */ a.jsx(Kn, { size: 15 }) }),
      /* @__PURE__ */ a.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ a.jsxs("button", { type: "button", title: "Create workflow", onClick: H, children: [
        /* @__PURE__ */ a.jsx(Oi, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    u === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(et, { size: 16 }),
      " ",
      g
    ] }) : null,
    u !== "failed" && g ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(et, { size: 16 }),
      " ",
      g
    ] }) : null,
    v ? /* @__PURE__ */ a.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ a.jsx(sn, { size: 14 }),
      " ",
      v
    ] }) : null,
    I.size > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ a.jsxs("span", { children: [
        I.size,
        " selected"
      ] }),
      /* @__PURE__ */ a.jsxs("div", { className: "wf-selection-actions", children: [
        /* @__PURE__ */ a.jsxs("button", { type: "button", className: "danger", disabled: G || L.length === 0, onClick: () => {
          ke();
        }, children: [
          /* @__PURE__ */ a.jsx(Zt, { size: 13 }),
          " ",
          i === "deleted" ? "Delete permanently" : "Delete"
        ] }),
        i === "active" ? /* @__PURE__ */ a.jsxs("button", { type: "button", disabled: G || O.length === 0, onClick: () => {
          Ie();
        }, title: O.length === 0 ? "Selected definitions do not have drafts to publish." : "Publish selected workflow drafts", children: [
          /* @__PURE__ */ a.jsx(Hi, { size: 13 }),
          " Publish"
        ] }) : null,
        /* @__PURE__ */ a.jsx("button", { type: "button", disabled: G, onClick: q, children: "Clear selection" })
      ] })
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
              ref: M,
              type: "checkbox",
              checked: K,
              onChange: (B) => le(B.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ a.jsx("span", { children: "Name" }),
          /* @__PURE__ */ a.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ a.jsx("span", { children: i === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ a.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ a.jsx("span", { children: "Actions" })
        ] }),
        m.map((B) => /* @__PURE__ */ a.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${B.name}`,
            "aria-selected": I.has(B.id),
            tabIndex: 0,
            onClick: () => n(B.id),
            onKeyDown: (re) => {
              re.currentTarget === re.target && (re.key !== "Enter" && re.key !== " " || (re.preventDefault(), n(B.id)));
            },
            children: [
              /* @__PURE__ */ a.jsx("label", { className: "wf-row-select", onClick: (re) => re.stopPropagation(), children: /* @__PURE__ */ a.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: I.has(B.id),
                  onChange: (re) => Q(B.id, re.target.checked),
                  "aria-label": `Select workflow definition ${B.name}`
                }
              ) }),
              /* @__PURE__ */ a.jsxs("span", { children: [
                /* @__PURE__ */ a.jsx("strong", { children: B.name }),
                /* @__PURE__ */ a.jsx("small", { children: B.description || B.id })
              ] }),
              /* @__PURE__ */ a.jsx("span", { children: B.latestVersion ?? "No version" }),
              /* @__PURE__ */ a.jsx("span", { children: i === "deleted" ? Qe(B.deletedAt) : B.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ a.jsx("span", { children: Qe(B.lastModifiedAt) }),
              /* @__PURE__ */ a.jsx("span", { className: "wf-row-actions", onClick: (re) => re.stopPropagation(), children: i === "active" ? /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
                /* @__PURE__ */ a.jsx("button", { type: "button", onClick: (re) => {
                  re.stopPropagation(), n(B.id);
                }, children: "Open" }),
                /* @__PURE__ */ a.jsx("button", { type: "button", onClick: (re) => {
                  re.stopPropagation(), ae(B.id);
                }, children: "Artifacts" }),
                W ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => xt(t, W, B), children: [
                  /* @__PURE__ */ a.jsx(mt, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ a.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  Pe(B);
                }, children: [
                  /* @__PURE__ */ a.jsx(Zt, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
                /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
                  ge(B);
                }, children: [
                  /* @__PURE__ */ a.jsx(Ll, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ a.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  $e(B);
                }, children: [
                  /* @__PURE__ */ a.jsx(Zt, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          B.id
        ))
      ] }),
      /* @__PURE__ */ a.jsx(
        E0,
        {
          page: c,
          pageSize: l,
          totalCount: p,
          onPageChange: (B) => {
            d(B), q();
          },
          onPageSizeChange: (B) => {
            f(B), d(1), q();
          }
        }
      )
    ] }) : null,
    T ? /* @__PURE__ */ a.jsx(
      f0,
      {
        draft: T,
        activities: F,
        catalogState: j,
        creating: k,
        suggestMetadataAction: $,
        onSuggestMetadata: $ ? () => xt(t, $, { draft: T, activities: F }) : void 0,
        onChange: (B) => _(B),
        onClose: () => _(null),
        onSubmit: Z
      }
    ) : null
  ] });
}
function f0({ draft: e, activities: t, catalogState: n, creating: o, suggestMetadataAction: r, onSuggestMetadata: i, onChange: s, onClose: c, onSubmit: d }) {
  const l = me(() => I0(t), [t]), f = C0(e, t), u = (h) => {
    if (h.startsWith("kind:")) {
      s({ ...e, rootKind: h.slice(5), rootActivityVersionId: null });
      return;
    }
    const g = t.find((y) => y.activityVersionId === h);
    s({
      ...e,
      rootKind: ud(g) ?? e.rootKind,
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
                l.otherCategories.map((h) => /* @__PURE__ */ a.jsx("optgroup", { label: h.name, children: h.activities.map((g) => /* @__PURE__ */ a.jsx("option", { value: g.activityVersionId, children: Me(g) }, g.activityVersionId)) }, h.name))
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
function h0({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: o }) {
  const [r, i] = Y("active"), [s, c] = Y("loading"), [d, l] = Y(""), [f, u] = Y(""), [h, g] = Y([]), y = n?.trim().toLowerCase() ?? "", v = me(
    () => y ? h.filter((_) => A0(_, y)) : h,
    [y, h]
  ), w = me(
    () => Array.from(new Set(h.flatMap((_) => [
      _.definitionId,
      _.definitionVersionId,
      _.sourceId
    ]).filter((_) => !!_))).sort((_, k) => _.localeCompare(k)),
    [h]
  ), m = zt(t, "weaver.workflows.explain-executable"), b = ye(async () => {
    c("loading"), l("");
    try {
      g(await Wl(e, r)), c("ready");
    } catch (_) {
      l(_ instanceof Error ? _.message : String(_)), c("failed");
    }
  }, [e, r]);
  ne(() => {
    b();
  }, [b]);
  const p = async (_) => {
    u(""), l("");
    try {
      await Bl(e, _.artifactId), u(`Started ${_.artifactId}`);
    } catch (k) {
      l(k instanceof Error ? k.message : String(k));
    }
  }, x = async (_) => {
    if (window.confirm(`Delete executable artifact "${_.artifactId}"? You can restore it from the Deleted view.`)) {
      u(""), l("");
      try {
        await ex(e, _.artifactId), u(`Deleted ${_.artifactId}`), await b();
      } catch (k) {
        l(k instanceof Error ? k.message : String(k));
      }
    }
  }, I = async (_) => {
    u(""), l("");
    try {
      await tx(e, _.artifactId), u(`Restored ${_.artifactId}`), await b();
    } catch (k) {
      l(k instanceof Error ? k.message : String(k));
    }
  }, E = async (_) => {
    if (window.confirm(`Permanently delete executable artifact "${_.artifactId}"? This cannot be undone.`)) {
      u(""), l("");
      try {
        await nx(e, _.artifactId), u(`Permanently deleted ${_.artifactId}`), await b();
      } catch (k) {
        l(k instanceof Error ? k.message : String(k));
      }
    }
  }, A = (_) => {
    m && xt(t, m, _) && (l(""), u(`Sent ${_.artifactId} to Weaver`));
  }, P = (_) => {
    l(""), u(`Copied ${_}`);
  }, T = (_) => {
    u(""), l(`Could not copy ${_}.`);
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
        b();
      }, children: /* @__PURE__ */ a.jsx(Kn, { size: 15 }) }),
      /* @__PURE__ */ a.jsxs("label", { className: "wf-search wf-executable-definition-filter", children: [
        /* @__PURE__ */ a.jsx(Vl, { size: 14 }),
        /* @__PURE__ */ a.jsx(
          "input",
          {
            "aria-label": "Filter executables by workflow definition",
            list: "wf-executable-definition-options",
            placeholder: "Filter by definition ID",
            value: n ?? "",
            onChange: (_) => o(_.currentTarget.value || null)
          }
        )
      ] }),
      /* @__PURE__ */ a.jsx("datalist", { id: "wf-executable-definition-options", children: w.map((_) => /* @__PURE__ */ a.jsx("option", { value: _ }, _)) }),
      n ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => o(null), children: [
        /* @__PURE__ */ a.jsx(Hl, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    s === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(et, { size: 16 }),
      " ",
      d
    ] }) : null,
    s !== "failed" && d ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(et, { size: 16 }),
      " ",
      d
    ] }) : null,
    f ? /* @__PURE__ */ a.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ a.jsx(sn, { size: 14 }),
      " ",
      f
    ] }) : null,
    s === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading workflow executables..." }) : null,
    s === "ready" && v.length === 0 ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: M0(r, !!n) }) : null,
    s === "ready" && v.length > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ a.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ a.jsx("span", { children: "Version" }),
        /* @__PURE__ */ a.jsx("span", { children: "Source" }),
        /* @__PURE__ */ a.jsx("span", { children: "Root" }),
        /* @__PURE__ */ a.jsx("span", { children: r === "deleted" ? "Deleted" : "Published" }),
        /* @__PURE__ */ a.jsx("span", { children: "Actions" })
      ] }),
      v.map((_) => /* @__PURE__ */ a.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ a.jsxs("span", { className: "wf-artifact-cell", children: [
          /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line", children: [
            /* @__PURE__ */ a.jsx("strong", { title: _.artifactId, children: _.artifactId }),
            /* @__PURE__ */ a.jsx(Ut, { value: _.artifactId, ariaLabel: `Copy artifact ID ${_.artifactId}`, copiedLabel: "artifact ID", onCopied: P, onCopyFailed: T })
          ] }),
          /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
            /* @__PURE__ */ a.jsx("small", { title: _.artifactHash, children: _.artifactHash }),
            /* @__PURE__ */ a.jsx(Ut, { value: _.artifactHash, ariaLabel: `Copy artifact hash ${_.artifactHash}`, copiedLabel: "artifact hash", onCopied: P, onCopyFailed: T })
          ] })
        ] }),
        /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line wf-version-cell", children: [
          /* @__PURE__ */ a.jsx("span", { children: _.artifactVersion }),
          /* @__PURE__ */ a.jsx(Ut, { value: _.artifactVersion, ariaLabel: `Copy artifact version ${_.artifactVersion}`, copiedLabel: "artifact version", onCopied: P, onCopyFailed: T })
        ] }),
        /* @__PURE__ */ a.jsx(p0, { executable: _, onCopied: P, onCopyFailed: T }),
        /* @__PURE__ */ a.jsx("span", { children: yd(_) }),
        /* @__PURE__ */ a.jsx("span", { children: Qe(r === "deleted" ? _.deletedAt : _.publishedAt ?? _.createdAt) }),
        /* @__PURE__ */ a.jsx("span", { className: "wf-row-actions", children: r === "active" ? /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
          /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
            p(_);
          }, children: [
            /* @__PURE__ */ a.jsx(ur, { size: 13 }),
            " Run"
          ] }),
          m ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => A(_), children: [
            /* @__PURE__ */ a.jsx(mt, { size: 13 }),
            " Explain"
          ] }) : null,
          /* @__PURE__ */ a.jsxs("button", { type: "button", className: "danger", onClick: () => {
            x(_);
          }, children: [
            /* @__PURE__ */ a.jsx(Zt, { size: 13 }),
            " Delete"
          ] })
        ] }) : /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
          /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
            I(_);
          }, children: [
            /* @__PURE__ */ a.jsx(Ll, { size: 13 }),
            " Restore"
          ] }),
          /* @__PURE__ */ a.jsxs("button", { type: "button", className: "danger", onClick: () => {
            E(_);
          }, children: [
            /* @__PURE__ */ a.jsx(Zt, { size: 13 }),
            " Delete permanently"
          ] })
        ] }) })
      ] }, _.artifactId))
    ] }) : null
  ] });
}
function p0({ executable: e, onCopied: t, onCopyFailed: n }) {
  const o = e.sourceId || e.definitionVersionId || e.definitionId, r = e.sourceVersion;
  return /* @__PURE__ */ a.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ a.jsx("span", { className: "wf-source-kind", children: md(e.sourceKind) }),
    o ? /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line", children: [
      /* @__PURE__ */ a.jsx("code", { title: o, children: o }),
      /* @__PURE__ */ a.jsx(Ut, { value: o, ariaLabel: `Copy source ID ${o}`, copiedLabel: "source ID", onCopied: t, onCopyFailed: n })
    ] }) : null,
    r ? /* @__PURE__ */ a.jsxs("small", { children: [
      "Version ",
      r
    ] }) : null
  ] });
}
function Ut({ value: e, ariaLabel: t, copiedLabel: n, onCopied: o, onCopyFailed: r }) {
  if (!e) return null;
  const i = async (s) => {
    s.preventDefault(), s.stopPropagation();
    try {
      await $0(e), o(n);
    } catch {
      r(n);
    }
  };
  return /* @__PURE__ */ a.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (s) => {
    i(s);
  }, children: /* @__PURE__ */ a.jsx(Hm, { size: 12 }) });
}
function g0({ context: e, ai: t, definitionId: n, publishedArtifactId: o }) {
  const [r, i] = Y("loading"), [s, c] = Y(""), [d, l] = Y(""), [f, u] = Y([]), h = zt(t, "weaver.workflows.explain-executable"), g = ye(async () => {
    i("loading"), c("");
    try {
      const p = await Wl(e);
      u(p.filter((x) => D0(x, n)).sort(P0)), i("ready");
    } catch (p) {
      c(p instanceof Error ? p.message : String(p)), u([]), i("failed");
    }
  }, [e, n]);
  ne(() => {
    g();
  }, [g, o]);
  const y = async (p) => {
    l(""), c("");
    try {
      await Bl(e, p.artifactId), l(`Started ${p.artifactId}`);
    } catch (x) {
      c(x instanceof Error ? x.message : String(x));
    }
  }, v = (p) => {
    h && xt(t, h, p) && (c(""), l(`Sent ${p.artifactId} to Weaver`));
  }, w = () => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(n)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, m = (p) => {
    c(""), l(`Copied ${p}`);
  }, b = (p) => {
    l(""), c(`Could not copy ${p}.`);
  };
  return /* @__PURE__ */ a.jsxs("div", { className: "wf-artifacts-panel", children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-artifacts-toolbar", children: [
      /* @__PURE__ */ a.jsxs("span", { children: [
        f.length,
        " artifact",
        f.length === 1 ? "" : "s"
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", className: "wf-icon-button", "aria-label": "Refresh workflow artifacts", title: "Refresh", onClick: () => {
        g();
      }, children: /* @__PURE__ */ a.jsx(Kn, { size: 13 }) }),
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: w, children: "Open list" })
    ] }),
    r === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ a.jsx(et, { size: 14 }),
      " ",
      s
    ] }) : null,
    d ? /* @__PURE__ */ a.jsxs("div", { className: "wf-status-line compact", children: [
      /* @__PURE__ */ a.jsx(sn, { size: 13 }),
      " ",
      d
    ] }) : null,
    r === "loading" ? /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "Loading artifacts..." }) : null,
    r === "ready" && f.length === 0 ? /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "No published artifacts for this workflow yet." }) : null,
    r === "ready" && f.length > 0 ? /* @__PURE__ */ a.jsx("div", { className: "wf-artifact-list", role: "list", "aria-label": "Workflow artifacts", children: f.map((p) => /* @__PURE__ */ a.jsxs("article", { className: "wf-artifact-card", role: "listitem", "data-active": p.artifactId === o ? "true" : void 0, children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-artifact-card-heading", children: [
        /* @__PURE__ */ a.jsxs("div", { children: [
          /* @__PURE__ */ a.jsxs("span", { className: "wf-artifact-version", children: [
            "Version ",
            p.artifactVersion
          ] }),
          p.artifactId === o ? /* @__PURE__ */ a.jsx("span", { className: "wf-chip", children: "Latest publish" }) : null
        ] }),
        /* @__PURE__ */ a.jsx("span", { children: Qe(p.publishedAt ?? p.createdAt) })
      ] }),
      /* @__PURE__ */ a.jsxs("div", { className: "wf-artifact-card-values", children: [
        /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line", children: [
          /* @__PURE__ */ a.jsx("code", { title: p.artifactId, children: p.artifactId }),
          /* @__PURE__ */ a.jsx(Ut, { value: p.artifactId, ariaLabel: `Copy artifact ID ${p.artifactId}`, copiedLabel: "artifact ID", onCopied: m, onCopyFailed: b })
        ] }),
        /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ a.jsx("code", { title: p.artifactHash, children: p.artifactHash }),
          /* @__PURE__ */ a.jsx(Ut, { value: p.artifactHash, ariaLabel: `Copy artifact hash ${p.artifactHash}`, copiedLabel: "artifact hash", onCopied: m, onCopyFailed: b })
        ] })
      ] }),
      /* @__PURE__ */ a.jsxs("dl", { children: [
        /* @__PURE__ */ a.jsxs("div", { children: [
          /* @__PURE__ */ a.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ a.jsxs("dd", { children: [
            md(p.sourceKind),
            " ",
            p.sourceVersion ? `v${p.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ a.jsxs("div", { children: [
          /* @__PURE__ */ a.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ a.jsx("dd", { children: yd(p) })
        ] })
      ] }),
      /* @__PURE__ */ a.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
          y(p);
        }, children: [
          /* @__PURE__ */ a.jsx(ur, { size: 13 }),
          " Run"
        ] }),
        h ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => v(p), children: [
          /* @__PURE__ */ a.jsx(mt, { size: 13 }),
          " Explain"
        ] }) : null
      ] })
    ] }, p.artifactId)) }) : null
  ] });
}
function y0({ context: e }) {
  const [t, n] = Y("loading"), [o, r] = Y(""), [i, s] = Y(""), [c, d] = Y([]), l = ye(async () => {
    n("loading"), r("");
    try {
      const u = await ox(e, { status: i || void 0, take: 100 });
      d(u), n("ready");
    } catch (u) {
      r(u instanceof Error ? u.message : String(u)), d([]), n("failed");
    }
  }, [e, i]);
  ne(() => {
    l();
  }, [l]);
  const f = (u) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(u)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ a.jsx("button", { type: "button", className: "wf-icon-button", "aria-label": "Refresh workflow instances", title: "Refresh", onClick: () => {
        l();
      }, children: /* @__PURE__ */ a.jsx(Kn, { size: 15 }) }),
      /* @__PURE__ */ a.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ a.jsx("span", { children: "Status" }),
        /* @__PURE__ */ a.jsxs("select", { "aria-label": "Workflow instance status", value: i, onChange: (u) => s(u.target.value), children: [
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
    t === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(et, { size: 16 }),
      " ",
      o
    ] }) : null,
    t === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading workflow instances..." }) : null,
    t === "ready" && c.length === 0 ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "No workflow instances found. Run a published workflow executable to create instance history." }) : null,
    t === "ready" && c.length > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-grid wf-instance-grid", role: "table", "aria-label": "Workflow instances", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ a.jsx("span", { children: "Instance" }),
        /* @__PURE__ */ a.jsx("span", { children: "Status" }),
        /* @__PURE__ */ a.jsx("span", { children: "Definition" }),
        /* @__PURE__ */ a.jsx("span", { children: "Activity" }),
        /* @__PURE__ */ a.jsx("span", { children: "Started" }),
        /* @__PURE__ */ a.jsx("span", { children: "Duration" })
      ] }),
      c.map((u) => /* @__PURE__ */ a.jsxs(
        "button",
        {
          type: "button",
          className: "wf-grid-row",
          role: "row",
          "aria-label": `Inspect workflow instance ${u.workflowExecutionId}`,
          onClick: () => f(u.workflowExecutionId),
          children: [
            /* @__PURE__ */ a.jsxs("span", { children: [
              /* @__PURE__ */ a.jsx("strong", { children: u.workflowExecutionId }),
              /* @__PURE__ */ a.jsx("small", { children: u.artifactId })
            ] }),
            /* @__PURE__ */ a.jsx("span", { children: /* @__PURE__ */ a.jsx(Jn, { status: u.status, subStatus: u.subStatus }) }),
            /* @__PURE__ */ a.jsxs("span", { children: [
              /* @__PURE__ */ a.jsx("strong", { children: u.definitionId }),
              /* @__PURE__ */ a.jsx("small", { children: u.definitionVersionId })
            ] }),
            /* @__PURE__ */ a.jsxs("span", { children: [
              /* @__PURE__ */ a.jsxs("strong", { children: [
                u.activityCount,
                " activities"
              ] }),
              /* @__PURE__ */ a.jsxs("small", { children: [
                u.incidentCount,
                " incidents"
              ] })
            ] }),
            /* @__PURE__ */ a.jsx("span", { children: Qe(u.startedAt ?? u.createdAt) }),
            /* @__PURE__ */ a.jsx("span", { children: Q0(u.startedAt ?? u.createdAt, u.completedAt ?? u.updatedAt) })
          ]
        },
        u.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function m0({ context: e, ai: t, workflowExecutionId: n }) {
  const [o, r] = Y("loading"), [i, s] = Y(""), [c, d] = Y(null), [l, f] = Y(null), u = zt(t, "weaver.workflows.explain-instance"), h = ye(async () => {
    if (!n) {
      s("No workflow execution id was provided."), r("failed");
      return;
    }
    r("loading"), s("");
    try {
      const y = await rx(e, n), [v, w] = await Promise.all([
        Um(e, y.instance.definitionVersionId).then(
          (m) => ({ definitionVersion: m, error: "" }),
          (m) => ({ definitionVersion: null, error: m instanceof Error ? m.message : String(m) })
        ),
        Fi(e)
      ]);
      d({
        details: y,
        definitionVersion: v.definitionVersion,
        definitionVersionError: v.error,
        activityCatalog: w.activities
      }), f(null), r("ready");
    } catch (y) {
      d(null), s(y instanceof Error ? y.message : String(y)), r("failed");
    }
  }, [e, n]);
  ne(() => {
    h();
  }, [h]);
  const g = () => {
    window.history.pushState({}, "", "/workflows/instances"), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: g, children: [
        /* @__PURE__ */ a.jsx(Uo, { size: 14 }),
        " Instances"
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", className: "wf-icon-button", "aria-label": "Refresh workflow instance", title: "Refresh", onClick: () => {
        h();
      }, children: /* @__PURE__ */ a.jsx(Kn, { size: 14 }) }),
      c && u ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => xt(t, u, c.details), children: [
        /* @__PURE__ */ a.jsx(mt, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    o === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading workflow instance..." }) : null,
    o === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(et, { size: 16 }),
      " ",
      i
    ] }) : null,
    o === "ready" && c ? /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-detail-workbench", children: [
      /* @__PURE__ */ a.jsx(
        x0,
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
        w0,
        {
          ai: t,
          action: u,
          summary: c.details.instance,
          details: c.details,
          state: "ready",
          error: "",
          selectedEvidenceId: l,
          onSelectEvidence: f,
          graphNodeIds: c.definitionVersion ? S0(c.definitionVersion, c.activityCatalog) : void 0
        }
      )
    ] }) : null
  ] });
}
function x0({ definitionVersion: e, definitionVersionError: t, activityCatalog: n, details: o, selectedEvidenceId: r, onSelectEvidence: i }) {
  const s = me(() => {
    if (!e) return { nodes: [], edges: [] };
    const c = e.state.rootActivity;
    if (!c) return { nodes: [], edges: [] };
    const d = n.find((g) => g.activityVersionId === c.activityVersionId), l = Bi(c, d), f = l === "unsupported" ? null : Dn(c, []), u = l === "unsupported" ? pi(c, n, e.layout) : f ? ql(f, n, e.layout) : pi(c, n, e.layout), h = u.nodes.map((g) => ({
      ...g,
      draggable: !1,
      connectable: !1,
      deletable: !1
    }));
    return {
      nodes: cx(h, o.activities, o.incidents, r),
      edges: u.edges.map((g) => ({ ...g, deletable: !1 }))
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
      /* @__PURE__ */ a.jsx(Jn, { status: o.instance.status, subStatus: o.instance.subStatus })
    ] }),
    /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ a.jsxs("div", { className: "wf-empty", children: [
        "The workflow instance loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ a.jsx("small", { children: J0(t) }) : null
      ] }),
      e && s.nodes.length === 0 ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      s.nodes.length > 0 ? /* @__PURE__ */ a.jsxs(
        Cl,
        {
          nodes: s.nodes,
          edges: s.edges,
          nodeTypes: sd,
          edgeTypes: ad,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (c, d) => i(d.id),
          onPaneClick: () => i(null),
          children: [
            /* @__PURE__ */ a.jsx(_l, {}),
            /* @__PURE__ */ a.jsx(Pl, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ a.jsx(Ml, {})
          ]
        }
      ) : null
    ] })
  ] });
}
function w0({ ai: e, action: t, summary: n, details: o, state: r, error: i, selectedEvidenceId: s = null, onSelectEvidence: c, graphNodeIds: d }) {
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
      /* @__PURE__ */ a.jsx("dd", { children: /* @__PURE__ */ a.jsx(Jn, { status: n.status, subStatus: n.subStatus }) }),
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
      /* @__PURE__ */ a.jsx("dd", { children: Qe(n.createdAt) }),
      /* @__PURE__ */ a.jsx("dt", { children: "Started" }),
      /* @__PURE__ */ a.jsx("dd", { children: Qe(n.startedAt) }),
      /* @__PURE__ */ a.jsx("dt", { children: "Completed" }),
      /* @__PURE__ */ a.jsx("dd", { children: Qe(n.completedAt) }),
      /* @__PURE__ */ a.jsx("dt", { children: "Correlation" }),
      /* @__PURE__ */ a.jsx("dd", { children: n.correlationId || "None" })
    ] }),
    r === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading instance details..." }) : null,
    r === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(et, { size: 16 }),
      " ",
      i
    ] }) : null,
    r === "ready" && o ? /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
      /* @__PURE__ */ a.jsx(v0, { activities: o.activities, selectedEvidenceId: s, onSelectEvidence: c }),
      /* @__PURE__ */ a.jsx(b0, { incidents: o.incidents, selectedEvidenceId: s, onSelectEvidence: c }),
      /* @__PURE__ */ a.jsx(N0, { details: o, graphNodeIds: d })
    ] }) : null
  ] }) : /* @__PURE__ */ a.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Select a workflow instance to inspect activity history." }) });
}
function v0({ activities: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
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
          /* @__PURE__ */ a.jsx("span", { children: /* @__PURE__ */ a.jsx(Jn, { status: o.status, subStatus: o.subStatus }) }),
          /* @__PURE__ */ a.jsx("strong", { children: Zi(o.activityType) ?? o.activityType }),
          /* @__PURE__ */ a.jsx("small", { children: o.activityExecutionId }),
          /* @__PURE__ */ a.jsx("time", { children: Qe(o.scheduledAt) })
        ]
      },
      o.activityExecutionId
    )) }) : null
  ] });
}
function b0({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
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
function N0({ details: e, graphNodeIds: t }) {
  if (!t) return null;
  const n = new Map(e.activities.map((i) => [i.activityExecutionId, i])), o = e.activities.filter((i) => !t.has(Da(i))), r = e.incidents.filter((i) => {
    const s = i.activityExecutionId ? n.get(i.activityExecutionId) : null, c = i.executableNodeId ?? (s ? Da(s) : "");
    return !c || !t.has(c);
  });
  return o.length === 0 && r.length === 0 ? null : /* @__PURE__ */ a.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ a.jsx("h4", { children: "Unmatched runtime evidence" }),
    /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-unmatched-list", children: [
      o.map((i) => /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ a.jsx("strong", { children: Zi(i.activityType) ?? i.activityType }),
        /* @__PURE__ */ a.jsx("small", { children: i.activityExecutionId })
      ] }, `activity-${i.activityExecutionId}`)),
      r.map((i) => /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ a.jsx("strong", { children: i.failureType }),
        /* @__PURE__ */ a.jsx("small", { children: i.incidentId })
      ] }, `incident-${i.incidentId}`))
    ] })
  ] });
}
function Jn({ status: e, subStatus: t }) {
  return /* @__PURE__ */ a.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
function S0(e, t) {
  const n = e.state.rootActivity;
  if (!n) return /* @__PURE__ */ new Set();
  const o = t.find((i) => i.activityVersionId === n.activityVersionId);
  if (Bi(n, o) === "unsupported") return /* @__PURE__ */ new Set([n.nodeId]);
  const r = Dn(n, []);
  return new Set(r?.slot.activities.map((i) => i.nodeId) ?? [n.nodeId]);
}
function Da(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function E0({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const i = dd(n, t), s = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
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
      /* @__PURE__ */ a.jsx("select", { value: t, onChange: (d) => r(Number(d.target.value)), children: n0.map((d) => /* @__PURE__ */ a.jsx("option", { value: d, children: d }, d)) })
    ] }),
    /* @__PURE__ */ a.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ a.jsx(Uo, { size: 14 }),
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
        /* @__PURE__ */ a.jsx(Bt, { size: 14 })
      ] })
    ] })
  ] });
}
function k0(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function dd(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function zt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function xt(e, t, n) {
  const o = t.createPrompt(n);
  return o ? (e.dispatchPrompt(o), !0) : !1;
}
function I0(e) {
  const t = Jo(e, "flowchart"), n = Jo(e, "sequence"), o = [
    { value: t?.activityVersionId ?? "kind:flowchart", label: "Flowchart" },
    { value: n?.activityVersionId ?? "kind:sequence", label: "Sequence" }
  ], r = /* @__PURE__ */ new Map();
  for (const s of e.filter(gd)) {
    if (_0(s)) continue;
    const c = s.category || "Uncategorized";
    r.set(c, [...r.get(c) ?? [], s]);
  }
  const i = Array.from(r.entries()).sort(([s], [c]) => s.localeCompare(c)).map(([s, c]) => ({
    name: s,
    activities: c.sort((d, l) => Me(d).localeCompare(Me(l)))
  }));
  return { compositeRoots: o, otherCategories: i };
}
function C0(e, t) {
  return e.rootActivityVersionId ?? Jo(t, e.rootKind)?.activityVersionId ?? `kind:${e.rootKind}`;
}
function j0(e, t) {
  return e.rootActivityVersionId ?? Jo(t, e.rootKind)?.activityVersionId ?? null;
}
function Jo(e, t) {
  return e.find((n) => ud(n) === t);
}
function ud(e) {
  return e ? hd(e) ? "flowchart" : pd(e) ? "sequence" : null : null;
}
function fd(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "Uncategorized";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [o]) => n.localeCompare(o)).map(([n, o]) => ({
    category: n,
    activities: o.sort((r, i) => Me(r).localeCompare(Me(i)))
  }));
}
function _0(e) {
  return hd(e) || pd(e);
}
function hd(e) {
  return Me(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function pd(e) {
  return Me(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function gd(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function yd(e) {
  return T0(e.rootActivityType) || e.rootActivityType;
}
function A0(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function M0(e, t) {
  return t ? "No workflow executables match this definition filter." : e === "deleted" ? "No deleted workflow executables found." : "No workflow executables found. Publish a workflow definition to create one.";
}
function D0(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function P0(e, t) {
  return Pa(t) - Pa(e);
}
function Pa(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function md(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
async function $0(e) {
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
function T0(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function z0(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    _o(t, n.typeName, n), _o(t, n.name, n), _o(t, n.displayName, n);
    const o = n.typeName.split(".").filter(Boolean).at(-1);
    _o(t, o, n);
  }
  return t;
}
function R0(e, t, n) {
  const o = t.get(e.activityVersionId);
  return n.get(_n(o?.activityTypeKey)) ?? n.get(_n(Zi(o?.activityTypeKey))) ?? n.get(_n(o?.displayName)) ?? n.get(_n(e.activityVersionId)) ?? null;
}
function _o(e, t, n) {
  const o = _n(t);
  o && !e.has(o) && e.set(o, n);
}
function _n(e) {
  return e?.trim().toLowerCase() ?? "";
}
function Zi(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function $a(e, t, n, o) {
  const r = pr();
  if (!r) return t;
  const i = r.getItem(e);
  if (i == null) return t;
  const s = Number(i);
  return Number.isFinite(s) ? Ro(s, n, o) : t;
}
function Ta(e, t) {
  const n = pr();
  if (!n) return t;
  const o = n.getItem(e);
  return o === "true" ? !0 : o === "false" ? !1 : t;
}
function L0() {
  const e = pr();
  if (!e) return null;
  const t = e.getItem(cd);
  return t === "palette" || t === "inspector" ? t : null;
}
function pr() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function In(e, t) {
  const n = pr();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function Ro(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function V0({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: o,
  workflowDesignerPanels: r,
  onBack: i
}) {
  const [s, c] = Y(null), [d, l] = Y(null), [f, u] = Y([]), [h, g] = Y([]), [y, v] = Y(To), [w, m] = Y("loading"), [b, p] = Y([]), [x, I] = Y([]), [E, A] = Y([]), [P, T] = Y(null), [_, k] = Y(null), [R, F] = Y(null), [S, j] = Y(null), [C, M] = Y(""), [z, $] = Y(""), [W, L] = Y("idle"), [O, U] = Y(null), [K, G] = Y(!1), [se, ee] = Y(!1), [H, Z] = Y(null), [ae, oe] = Y(() => /* @__PURE__ */ new Set()), [q, Q] = Y(() => $a(ka, r0, Nn, Sn)), [le, de] = Y(() => $a(Ia, i0, En, kn)), [he, ke] = Y(() => Ta(Ca, !1)), [Ie, Pe] = Y(() => Ta(ja, !1)), [ge, $e] = Y(L0), [B, re] = Y("activities"), [xe, we] = Y("inspector"), je = ce(null), Le = ce(null), Ye = ce(""), an = ce(0), Rt = ce(Promise.resolve()), cn = ce(/* @__PURE__ */ new Map()), Lt = ce(null), ut = ce(null), vt = ce(!1), bt = d?.state.rootActivity ?? null, nt = me(() => new Map(f.map((N) => [N.activityVersionId, N])), [f]), Qn = me(() => z0(h), [h]), Oe = me(() => Yl(bt, b), [bt, b]), eo = Bi(Oe, Oe ? nt.get(Oe.activityVersionId) : void 0), Se = !!Oe && eo === "unsupported", qe = me(() => Se ? null : Dn(bt, b), [bt, b, Se]), ln = me(() => fd(f), [f]), Ee = me(() => Se && Oe?.nodeId === _ ? Oe : qe?.slot.activities.find((N) => N.nodeId === _) ?? null, [Se, qe, Oe, _]), Nt = me(
    () => Ee ? R0(Ee, nt, Qn) : null,
    [nt, Qn, Ee]
  ), dn = Ee ? Je(Ee) : [], Te = eo === "flowchart" && qe?.slot.mode === "flowchart", St = !bt || !Se, ft = W !== "idle", gr = !!d?.state.rootActivity && !ft, to = zt(n, "weaver.workflows.find-draft-risks"), no = zt(n, "weaver.workflows.propose-update");
  ne(() => {
    if (!(!s || !d))
      return window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
        workflowId: s.definition.id,
        workflowDefinitionId: s.definition.id,
        workflowVersionId: d.sourceVersionId ?? null,
        draftId: d.id,
        revision: U0(d),
        selectedNodeId: _,
        selectedActivityType: Nt?.typeName ?? (Ee ? nt.get(Ee.activityVersionId)?.activityTypeKey ?? Ee.activityVersionId : null),
        summary: s.definition.name,
        activities: vd(d.state.rootActivity, nt),
        diagnostics: d.validationErrors.map((N) => ({ severity: N.code ?? "warning", message: N.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === s.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [nt, s, d, Nt, Ee, _]), ne(() => {
    !d || !O || O.draftSignature !== at(d) && G(!1);
  }, [d, O]), ne(() => {
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
        const ue = K0(d), ie = Xx(d, X.batch, f), pe = `weaver-batch-${Date.now()}`;
        cn.current.set(pe, ue), l(ie.draft), p([]), k(ie.finalActivityIds.at(-1) ?? null), Z(null), U(null), $(ie.summary), M(""), X.respond({ ok: !0, result: { ...ie, undoToken: pe } });
      } catch (ue) {
        const ie = ue instanceof Error ? ue.message : String(ue);
        M(ie), X.respond({ ok: !1, message: ie });
      }
    }, D = (V) => {
      const X = V.detail;
      if (!X?.undoToken || !X.respond) return;
      const J = cn.current.get(X.undoToken);
      if (!J) {
        X.respond({ ok: !1, message: "The Weaver batch undo point is no longer available." });
        return;
      }
      cn.current.delete(X.undoToken), l(J), p([]), k(null), Z(null), U(null), $("Restored workflow draft before Weaver batch."), M(""), X.respond({ ok: !0, summary: "Restored workflow draft before Weaver batch." });
    };
    return window.addEventListener(ba, N), window.addEventListener(Na, D), () => {
      window.removeEventListener(ba, N), window.removeEventListener(Na, D);
    };
  }, [f, s, d]), ne(() => {
    In(ka, String(q));
  }, [q]), ne(() => {
    In(Ia, String(le));
  }, [le]), ne(() => {
    In(Ca, String(he));
  }, [he]), ne(() => {
    In(ja, String(Ie));
  }, [Ie]), ne(() => {
    In(cd, ge);
  }, [ge]), ne(() => {
    if (!ge) return;
    const N = (D) => {
      D.key === "Escape" && $e(null);
    };
    return window.addEventListener("keydown", N), () => window.removeEventListener("keydown", N);
  }, [ge]);
  const un = ye(async () => {
    M(""), m("loading");
    const [N, D, V, X] = await Promise.all([
      Zm(e, t),
      Fi(e),
      ix(e).then(
        (ue) => ({ ok: !0, descriptors: ue }),
        () => ({ ok: !1, descriptors: [] })
      ),
      sx(e).then(
        (ue) => ({ ok: !0, descriptors: ue }),
        () => ({ ok: !1, descriptors: To })
      )
    ]), J = N.draft ?? null;
    c(N), Ye.current = J ? at(J) : "", l(J), u(D.activities ?? []), g(V.descriptors), v(X.descriptors.length > 0 ? X.descriptors : To), m(V.ok ? "ready" : "failed"), p([]), k(null);
  }, [e, t]);
  ne(() => {
    un().catch((N) => M(N instanceof Error ? N.message : String(N)));
  }, [un]), ne(() => {
    oe((N) => {
      let D = !1;
      const V = new Set(N);
      for (const X of ln)
        V.has(X.category) || (V.add(X.category), D = !0);
      return D ? V : N;
    });
  }, [ln]), ne(() => {
    if (!Oe) {
      I([]), A([]);
      return;
    }
    const N = Se ? pi(Oe, f, d?.layout ?? []) : qe ? ql(qe, f, d?.layout ?? []) : { nodes: [], edges: [] };
    I(N.nodes), A(N.edges);
  }, [f, d?.layout, Se, qe, Oe]);
  const yr = (N) => {
    l((D) => D && { ...D, state: { ...D.state, rootActivity: N } });
  }, fn = ye((N, D) => {
    if (d?.state.rootActivity && Se)
      return;
    const V = yi(N, La(N));
    if (!d?.state.rootActivity) {
      yr(V), k(V.nodeId);
      return;
    }
    if (!qe) {
      if (!Je(V)[0]) {
        $(""), M("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      l((J) => {
        if (!J?.state.rootActivity) return J;
        const ue = J.state.rootActivity, ie = gi(V, [], [ue]), pe = D ? [
          ...J.layout.filter((_e) => _e.nodeId !== ue.nodeId),
          {
            nodeId: ue.nodeId,
            x: Math.round(D.x),
            y: Math.round(D.y)
          }
        ] : J.layout;
        return {
          ...J,
          layout: pe,
          state: {
            ...J.state,
            rootActivity: ie
          }
        };
      }), k(d.state.rootActivity.nodeId), M(""), $(`Wrapped root in ${Me(N)}`);
      return;
    }
    l((X) => {
      if (!X?.state.rootActivity) return X;
      const J = Dn(X.state.rootActivity, b);
      if (!J) return X;
      const ue = gi(X.state.rootActivity, b, [...J.slot.activities, V]), ie = D ? [
        ...X.layout.filter((pe) => pe.nodeId !== V.nodeId),
        {
          nodeId: V.nodeId,
          x: Math.round(D.x),
          y: Math.round(D.y)
        }
      ] : X.layout;
      return {
        ...X,
        layout: ie,
        state: {
          ...X.state,
          rootActivity: ue
        }
      };
    }), k(V.nodeId);
  }, [d?.state.rootActivity, b, Se, qe]), Vt = ye((N, D) => {
    const V = yi(N, La(N)), X = {
      id: V.nodeId,
      type: "workflowActivity",
      position: D,
      selected: !0,
      data: {
        label: Me(N),
        activityVersionId: N.activityVersionId,
        activityTypeKey: N.activityTypeKey,
        category: N.category,
        executionType: N.executionType,
        icon: mi(N),
        childSlots: Je(V),
        acceptsInbound: String(N.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: Gl(V, N)
      }
    };
    return { activityNode: V, node: X };
  }, []), Fe = ye((N, D, V = []) => {
    Se || l((X) => {
      if (!X) return X;
      const J = ux(X.layout, N), ue = X.state.rootActivity;
      if (!ue) return { ...X, layout: J };
      const ie = Dn(ue, b);
      if (!ie) return { ...X, layout: J };
      const pe = lx(ie, N, D, V), _e = ie.slot.mode === "flowchart" ? dx(pe, D) : pe;
      return {
        ...X,
        layout: J,
        state: {
          ...X.state,
          rootActivity: Zl(ue, b, _e)
        }
      };
    });
  }, [b, Se]), hn = ye((N, D) => {
    if (!je.current) return null;
    const V = je.current.getBoundingClientRect();
    return P ? P.screenToFlowPosition({ x: N, y: D }) : {
      x: N - V.left,
      y: D - V.top
    };
  }, [P]), pn = ye((N, D) => document.elementFromPoint(N, D)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), gn = ye((N, D, V) => {
    const X = x.find((ze) => ze.id === D.source), J = x.find((ze) => ze.id === D.target), ue = X && J ? Y0(X, J) : X ? Va(X) : V, ie = Vt(N, ue), _e = [...x.map((ze) => ze.selected ? { ...ze, selected: !1 } : ze), ie.node], Et = bx(E, D, ie.node.id);
    I(_e), A(Et), k(ie.node.id), Fe(_e, Et, [ie.activityNode]);
  }, [Fe, Vt, E, x]), Ht = ye((N, D, V) => {
    if (!St || !je.current) return !1;
    const X = je.current.getBoundingClientRect();
    if (!(D >= X.left && D <= X.right && V >= X.top && V <= X.bottom)) return !1;
    const ue = hn(D, V);
    if (!ue) return !1;
    if (Te) {
      const ie = pn(D, V), pe = ie ? E.find((_e) => _e.id === ie) : void 0;
      if (pe)
        return gn(N, pe, ue), !0;
    }
    return fn(N, ue), !0;
  }, [fn, St, E, pn, Te, gn, hn]);
  ne(() => {
    const N = (V) => {
      const X = Lt.current;
      if (!X) return;
      Math.hypot(V.clientX - X.startX, V.clientY - X.startY) >= e0 && (X.dragging = !0);
    }, D = (V) => {
      const X = Lt.current;
      if (Lt.current = null, !X?.dragging || !je.current || ut.current) return;
      const J = je.current.getBoundingClientRect();
      V.clientX >= J.left && V.clientX <= J.right && V.clientY >= J.top && V.clientY <= J.bottom && (vt.current = !0, window.setTimeout(() => {
        vt.current = !1;
      }, 0), Ht(X.activity, V.clientX, V.clientY));
    };
    return window.addEventListener("pointermove", N), window.addEventListener("pointerup", D), window.addEventListener("pointercancel", D), () => {
      window.removeEventListener("pointermove", N), window.removeEventListener("pointerup", D), window.removeEventListener("pointercancel", D);
    };
  }, [P, Ht]);
  const oo = (N, D) => {
    ut.current = { activityVersionId: D.activityVersionId, handledDrop: !1 }, N.dataTransfer.setData(Ea, D.activityVersionId), N.dataTransfer.setData("text/plain", D.activityVersionId), N.dataTransfer.effectAllowed = "copy";
  }, ro = (N, D) => {
    const V = ut.current;
    ut.current = null, !V?.handledDrop && (N.clientX === 0 && N.clientY === 0 || Ht(D, N.clientX, N.clientY) && (vt.current = !0, window.setTimeout(() => {
      vt.current = !1;
    }, 0)));
  }, io = (N, D) => {
    N.button === 0 && (Lt.current = {
      activity: D,
      startX: N.clientX,
      startY: N.clientY,
      dragging: !1
    });
  }, mr = (N) => {
    vt.current || St && fn(N);
  }, so = (N) => {
    if (!St) {
      N.dataTransfer.dropEffect = "none";
      return;
    }
    if (N.preventDefault(), N.dataTransfer.dropEffect = "copy", !Te) return;
    const D = pn(N.clientX, N.clientY);
    j(D);
  }, ao = (N) => {
    if (!je.current) return;
    const D = N.relatedTarget;
    D && je.current.contains(D) || j(null);
  }, co = (N) => {
    N.preventDefault(), j(null);
    const D = N.dataTransfer.getData(Ea) || N.dataTransfer.getData("text/plain");
    if (!D || (N.stopPropagation(), ut.current?.activityVersionId === D && (ut.current.handledDrop = !0), !St)) return;
    const V = nt.get(D);
    V && Ht(V, N.clientX, N.clientY);
  }, xr = () => {
    if (!Te) return;
    const N = je.current?.getBoundingClientRect();
    N && F({
      kind: "fromEmpty",
      clientX: N.left + N.width / 2,
      clientY: N.top + N.height / 2
    });
  }, yn = ye(async (N, D) => {
    const V = async () => {
      const J = ++an.current, ue = at(N);
      M("");
      try {
        const ie = await Jm(e, N), pe = at(ie);
        return Ye.current = pe, l((_e) => !_e || _e.id !== ie.id ? _e : at(_e) === ue ? ie : { ..._e, validationErrors: ie.validationErrors }), J === an.current && $(D), ie;
      } catch (ie) {
        throw J === an.current && ($(""), M(ie instanceof Error ? ie.message : String(ie))), ie;
      }
    }, X = Rt.current.then(V, V);
    return Rt.current = X.catch(() => {
    }), X;
  }, [e]);
  ne(() => {
    if (!se || !d || at(d) === Ye.current) return;
    $("Autosaving...");
    const D = window.setTimeout(() => {
      yn(d, "Autosaved").catch(() => {
      });
    }, t0);
    return () => window.clearTimeout(D);
  }, [se, d, yn]);
  const lo = async () => {
    if (!(!d || ft)) {
      L("saving"), $("Saving...");
      try {
        await yn(d, "Saved");
      } catch {
      } finally {
        L("idle");
      }
    }
  }, wr = async () => {
    if (!(!d || ft)) {
      L("promoting"), $("Publishing...");
      try {
        const N = await Ol(e, d.id), D = await Fl(e, N.versionId);
        Z(D.artifactId), $(`Published ${D.artifactVersion}`), await un();
      } catch (N) {
        $(""), M(N instanceof Error ? N.message : String(N));
      } finally {
        L("idle");
      }
    }
  }, vr = async () => {
    if (!d?.state.rootActivity || ft) return;
    const N = d, D = at(N);
    U(null), G(!1), $("Preparing test run...");
    try {
      L("testRunPreparing"), $("Preparing test run...");
      const V = G0(N);
      L("testRunStarting"), $("Starting test run...");
      const X = await Qm(e, {
        definitionId: N.definitionId,
        snapshotId: V,
        state: N.state
      });
      U({ draftSignature: D, view: X }), $(Nd(X) ? "Test run rejected" : "Test run dispatched");
    } catch (V) {
      $(""), M(V instanceof Error ? V.message : String(V));
    } finally {
      L("idle");
    }
  }, br = (N) => {
    const D = Se ? N.filter((V) => V.type === "select") : N;
    D.length !== 0 && I((V) => Qc(D, V));
  }, Nr = (N) => {
    Se || A((D) => el(N, D));
  }, mn = (N) => !N.source || !N.target || N.source === N.target || !Te ? !1 : !N.targetHandle, Sr = (N) => {
    if (!d?.state.rootActivity || !qe || !Te || !mn(N)) return;
    const D = Go(N.source, N.target, N.sourceHandle ?? "Done", N.targetHandle ?? void 0), V = nl(D, E);
    A(V), Fe(x, V);
  }, Er = () => {
    Fe(x, E);
  }, kr = (N, D) => {
    if (!D.nodeId || D.handleType === "target") {
      Le.current = null;
      return;
    }
    Le.current = {
      nodeId: D.nodeId,
      handleId: D.handleId ?? null
    };
  }, uo = (N, D) => {
    const V = Z0(Le.current, D);
    if (Le.current = null, !V || !Te || D.toNode || D.toHandle || q0(N)) return;
    const X = wd(N);
    F({
      kind: "fromPort",
      sourceNodeId: V.nodeId,
      sourceHandleId: V.handleId,
      clientX: X.x,
      clientY: X.y
    });
  }, Ir = (N, D) => {
    if (!Te || !mn(D)) return;
    const V = Fg(N, {
      ...D,
      sourceHandle: D.sourceHandle ?? "Done",
      targetHandle: D.targetHandle ?? void 0
    }, E, { shouldReplaceId: !1 });
    A(V), Fe(x, V);
  }, Cr = (N) => {
    if (Se || N.length === 0) return;
    const D = new Set(N.map((J) => J.id)), V = x.filter((J) => !D.has(J.id)), X = E.filter((J) => !D.has(J.source) && !D.has(J.target));
    I(V), A(X), _ && D.has(_) && k(null), Fe(V, X);
  }, jr = (N) => {
    if (Se || N.length === 0) return;
    const D = new Set(N.map((X) => X.id)), V = E.filter((X) => !D.has(X.id));
    A(V), Fe(x, V);
  }, fo = ye((N) => {
    if (Se) return;
    const D = E.filter((V) => V.id !== N);
    A(D), Fe(x, D);
  }, [Fe, E, Se, x]), ho = ye((N, D, V) => {
    Te && F({ kind: "spliceEdge", edgeId: N, clientX: D, clientY: V });
  }, [Te]), _r = (N) => {
    const D = R;
    if (!D) return;
    F(null);
    const V = hn(D.clientX, D.clientY) ?? { x: 0, y: 0 };
    if (D.kind === "fromEmpty") {
      const J = Vt(N, V), ie = [...x.map((pe) => pe.selected ? { ...pe, selected: !1 } : pe), J.node];
      I(ie), k(J.node.id), Fe(ie, E, [J.activityNode]);
      return;
    }
    if (D.kind === "fromPort") {
      const J = x.find((ze) => ze.id === D.sourceNodeId), ue = J ? Va(J) : V, ie = Vt(N, ue), _e = [...x.map((ze) => ze.selected ? { ...ze, selected: !1 } : ze), ie.node], Et = [...E, Go(D.sourceNodeId, ie.node.id, D.sourceHandleId ?? "Done")];
      I(_e), A(Et), k(ie.node.id), Fe(_e, Et, [ie.activityNode]);
      return;
    }
    const X = E.find((J) => J.id === D.edgeId);
    X && gn(N, X, V);
  }, Ar = me(() => ({
    highlightedEdgeId: S,
    deleteEdge: fo,
    requestInsertActivity: ho
  }), [fo, S, ho]), Mr = (N, D, V) => {
    p((X) => [...X, { ownerNodeId: N.nodeId, slotId: D, label: V }]), k(null);
  }, po = ye((N) => {
    l((D) => {
      const V = D?.state.rootActivity;
      return !D || !V ? D : {
        ...D,
        state: {
          ...D.state,
          rootActivity: Ul(V, N.nodeId, () => N)
        }
      };
    });
  }, []), Dr = (N) => {
    oe((D) => {
      const V = new Set(D);
      return V.has(N) ? V.delete(N) : V.add(N), V;
    });
  }, xn = (N) => {
    $e((D) => D === N ? null : D), N === "palette" ? ke((D) => !D) : Pe((D) => !D);
  }, go = (N) => {
    N === "palette" ? ke(!1) : Pe(!1), $e((D) => D === N ? null : N);
  }, yo = (N, D) => {
    $e(null), N === "palette" ? (ke(!1), Q((V) => Ro(V + D, Nn, Sn))) : (Pe(!1), de((V) => Ro(V + D, En, kn)));
  }, Ot = (N, D) => {
    D.preventDefault(), $e(null), N === "palette" ? ke(!1) : Pe(!1);
    const V = D.clientX, X = N === "palette" ? q : le, J = N === "palette" ? Nn : En, ue = N === "palette" ? Sn : kn;
    document.body.classList.add("wf-side-panel-resizing");
    const ie = (_e) => {
      const Et = N === "palette" ? _e.clientX - V : V - _e.clientX, ze = Ro(X + Et, J, ue);
      N === "palette" ? Q(ze) : de(ze);
    }, pe = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", ie), window.removeEventListener("pointerup", pe), window.removeEventListener("pointercancel", pe);
    };
    window.addEventListener("pointermove", ie), window.addEventListener("pointerup", pe), window.addEventListener("pointercancel", pe);
  }, mo = (N, D) => {
    D.key === "ArrowLeft" ? (D.preventDefault(), yo(N, N === "palette" ? -jo : jo)) : D.key === "ArrowRight" ? (D.preventDefault(), yo(N, N === "palette" ? jo : -jo)) : D.key === "Home" ? (D.preventDefault(), N === "palette" ? Q(Nn) : de(En)) : D.key === "End" && (D.preventDefault(), N === "palette" ? Q(Sn) : de(kn));
  };
  if (!s || !d)
    return /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: C || "Loading workflow editor..." });
  const Pr = [
    "wf-editor-body",
    he ? "palette-collapsed" : "",
    Ie ? "inspector-collapsed" : "",
    ge === "palette" ? "palette-maximized" : "",
    ge === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), xo = {
    "--wf-palette-width": `${he ? _a : q}px`,
    "--wf-inspector-width": `${Ie ? _a : le}px`
  }, Ui = !he && ge !== "inspector", Ki = !Ie && ge !== "palette", $r = O?.draftSignature === at(d) ? O.view : null, Gi = $r && z.startsWith("Test run") ? "" : z, Sd = {
    definition: s.definition,
    draft: d,
    selectedActivity: Ee,
    selectedActivityDescriptor: Nt,
    selectedActivitySlots: dn,
    catalog: f,
    currentScopeOwner: Oe,
    frames: b
  }, Ji = r.map((N) => {
    const D = N.component;
    return {
      id: N.id,
      title: N.title,
      side: N.side,
      order: N.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ a.jsx(D, { context: Sd })
    };
  }), Tr = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ a.jsx(Tl, { size: 15 }),
      render: Ed
    },
    ...Ji.filter((N) => N.side === "left")
  ].sort(Ra), zr = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ a.jsx(Rl, { size: 15 }),
      render: kd
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ a.jsx(Fm, { size: 15 }),
      render: () => /* @__PURE__ */ a.jsx(
        g0,
        {
          context: e,
          ai: n,
          definitionId: s.definition.id,
          publishedArtifactId: H
        }
      )
    },
    ...Ji.filter((N) => N.side === "right")
  ].sort(Ra), Qi = Tr.find((N) => N.id === B) ?? Tr[0], es = zr.find((N) => N.id === xe) ?? zr[0];
  function Ed() {
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
            onClick: () => Dr(N.category),
            children: [
              D ? /* @__PURE__ */ a.jsx(zl, { size: 14 }) : /* @__PURE__ */ a.jsx(Bt, { size: 14 }),
              /* @__PURE__ */ a.jsx("span", { children: N.category }),
              /* @__PURE__ */ a.jsx("small", { children: N.activities.length })
            ]
          }
        ),
        D ? /* @__PURE__ */ a.jsx("div", { className: "wf-palette-activities", role: "group", children: N.activities.map((V) => {
          const X = V.description?.trim(), J = X ? `wf-palette-description-${V.activityVersionId}` : void 0, ue = Me(V), ie = mi(V);
          return /* @__PURE__ */ a.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-activity",
              role: "treeitem",
              draggable: !0,
              title: X || Me(V),
              "aria-describedby": J,
              onClick: () => mr(V),
              onDragStart: (pe) => oo(pe, V),
              onDragEnd: (pe) => ro(pe, V),
              onPointerDown: (pe) => io(pe, V),
              children: [
                /* @__PURE__ */ a.jsx("span", { className: "wf-activity-icon", "data-icon": ie, "aria-hidden": "true", children: xd(ie) }),
                /* @__PURE__ */ a.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ a.jsx("strong", { children: ue }),
                  X ? /* @__PURE__ */ a.jsx("small", { id: J, children: X }) : null
                ] }),
                /* @__PURE__ */ a.jsx(Om, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            V.activityVersionId
          );
        }) }) : null
      ] }, N.category);
    }) });
  }
  function kd() {
    return Ee ? /* @__PURE__ */ a.jsxs("div", { className: "wf-inspector-content", children: [
      /* @__PURE__ */ a.jsx("h3", { children: x.find((N) => N.id === Ee.nodeId)?.data.label ?? Ee.nodeId }),
      /* @__PURE__ */ a.jsxs("dl", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Node ID" }),
        /* @__PURE__ */ a.jsx("dd", { children: Ee.nodeId }),
        /* @__PURE__ */ a.jsx("dt", { children: "Activity type" }),
        /* @__PURE__ */ a.jsx("dd", { children: Nt?.typeName ?? nt.get(Ee.activityVersionId)?.activityTypeKey ?? "Unknown" }),
        /* @__PURE__ */ a.jsx("dt", { children: "Activity version" }),
        /* @__PURE__ */ a.jsx("dd", { children: Ee.activityVersionId })
      ] }),
      /* @__PURE__ */ a.jsx(
        Rx,
        {
          activity: Ee,
          descriptor: Nt,
          editors: o,
          expressionDescriptors: y,
          descriptorStatus: w,
          onChange: po
        }
      ),
      dn.length > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-slot-list", children: [
        /* @__PURE__ */ a.jsx("span", { children: "Embedded slots" }),
        dn.map((N) => /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => Mr(Ee, N.id, `${x.find((D) => D.id === Ee.nodeId)?.data.label ?? Ee.nodeId} / ${N.label}`), children: [
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
      /* @__PURE__ */ a.jsx(Bt, { size: 14 }),
      /* @__PURE__ */ a.jsx("strong", { children: s.definition.name }),
      /* @__PURE__ */ a.jsx("span", { className: "wf-chip", children: "Draft" }),
      Gi ? /* @__PURE__ */ a.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ a.jsx(sn, { size: 13 }),
        " ",
        Gi
      ] }) : null,
      /* @__PURE__ */ a.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ a.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ a.jsx("input", { className: "wf-autosave-switch-input", type: "checkbox", checked: se, onChange: (N) => ee(N.target.checked) }),
          /* @__PURE__ */ a.jsx("span", { children: "Autosave" })
        ] }),
        to ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => xt(n, to, { definition: s.definition, draft: d }), children: [
          /* @__PURE__ */ a.jsx(mt, { size: 15 }),
          " Risks"
        ] }) : null,
        no ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => xt(n, no, { definition: s.definition, draft: d }), children: [
          /* @__PURE__ */ a.jsx(mt, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ a.jsxs("button", { type: "button", disabled: ft, onClick: () => {
          lo();
        }, children: [
          /* @__PURE__ */ a.jsx(Bm, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ a.jsxs("button", { type: "button", disabled: ft, onClick: () => {
          wr();
        }, children: [
          /* @__PURE__ */ a.jsx(Hi, { size: 15 }),
          " Publish"
        ] }),
        $r ? /* @__PURE__ */ a.jsx(
          X0,
          {
            testRun: $r,
            open: K,
            onToggle: () => G((N) => !N)
          }
        ) : null,
        /* @__PURE__ */ a.jsxs(
          "button",
          {
            type: "button",
            disabled: !gr,
            title: d.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              vr();
            },
            children: [
              /* @__PURE__ */ a.jsx(ur, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    C ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(et, { size: 16 }),
      " ",
      C
    ] }) : null,
    /* @__PURE__ */ a.jsxs("div", { className: Pr, style: xo, children: [
      /* @__PURE__ */ a.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ a.jsx(
            za,
            {
              label: "Activities panel tabs",
              tabs: Tr,
              activeTabId: Qi.id,
              onSelect: re
            }
          ),
          /* @__PURE__ */ a.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ a.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": he ? "Expand activities panel" : "Collapse activities panel",
                title: he ? "Expand" : "Collapse",
                onClick: () => xn("palette"),
                children: he ? /* @__PURE__ */ a.jsx(Bt, { size: 14 }) : /* @__PURE__ */ a.jsx(Uo, { size: 14 })
              }
            ),
            he ? null : /* @__PURE__ */ a.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": ge === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: ge === "palette" ? "Restore" : "Maximize",
                onClick: () => go("palette"),
                children: ge === "palette" ? /* @__PURE__ */ a.jsx(ua, { size: 14 }) : /* @__PURE__ */ a.jsx(Ko, { size: 14 })
              }
            )
          ] })
        ] }),
        Ui ? Qi.render() : null
      ] }),
      Ui && !ge ? /* @__PURE__ */ a.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Nn,
          "aria-valuemax": Sn,
          "aria-valuenow": q,
          tabIndex: 0,
          onPointerDown: (N) => Ot("palette", N),
          onKeyDown: (N) => mo("palette", N)
        }
      ) : /* @__PURE__ */ a.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ a.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-breadcrumb", children: [
          /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
            p([]), k(null);
          }, children: "Root" }),
          b.map((N, D) => /* @__PURE__ */ a.jsxs(wt.Fragment, { children: [
            /* @__PURE__ */ a.jsx(Bt, { size: 13 }),
            /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
              p(b.slice(0, D + 1)), k(null);
            }, children: N.label })
          ] }, `${N.ownerNodeId}-${N.slotId}-${D}`))
        ] }),
        /* @__PURE__ */ a.jsxs("div", { className: "wf-canvas", ref: je, onDragOver: so, onDragLeave: ao, onDrop: co, children: [
          /* @__PURE__ */ a.jsx(ld.Provider, { value: Ar, children: /* @__PURE__ */ a.jsxs(
            Cl,
            {
              nodes: x,
              edges: E,
              nodeTypes: sd,
              edgeTypes: ad,
              onInit: T,
              onNodesChange: br,
              onEdgesChange: Nr,
              onNodesDelete: Cr,
              onEdgesDelete: jr,
              onConnect: Sr,
              onConnectStart: Te ? kr : void 0,
              onConnectEnd: Te ? uo : void 0,
              onReconnect: Te ? Ir : void 0,
              isValidConnection: mn,
              onDragOver: so,
              onDragLeave: ao,
              onDrop: co,
              onPaneClick: () => k(null),
              onNodeClick: (N, D) => k(D.id),
              onNodeDragStop: Se ? void 0 : Er,
              fitView: !0,
              minZoom: 0.2,
              maxZoom: 1.8,
              nodesConnectable: Te,
              nodesDraggable: !Se,
              selectionOnDrag: !0,
              multiSelectionKeyCode: ["Shift", "Meta", "Control"],
              deleteKeyCode: Se ? null : ["Backspace", "Delete"],
              panActivationKeyCode: null,
              defaultEdgeOptions: { type: "workflow" },
              children: [
                /* @__PURE__ */ a.jsx(_l, { gap: 18, size: 1 }),
                /* @__PURE__ */ a.jsx(Ml, {}),
                /* @__PURE__ */ a.jsx(Pl, { pannable: !0, zoomable: !0 })
              ]
            }
          ) }),
          Te && x.length === 0 ? /* @__PURE__ */ a.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => xr(), children: [
            /* @__PURE__ */ a.jsx(Oi, { size: 15 }),
            " Add activity"
          ] }) : null,
          R ? /* @__PURE__ */ a.jsx(
            B0,
            {
              clientX: R.clientX,
              clientY: R.clientY,
              activities: f,
              onPick: _r,
              onClose: () => F(null)
            }
          ) : null
        ] }),
        /* @__PURE__ */ a.jsx(W0, { draft: d })
      ] }),
      Ki && !ge ? /* @__PURE__ */ a.jsx(
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
          onPointerDown: (N) => Ot("inspector", N),
          onKeyDown: (N) => mo("inspector", N)
        }
      ) : /* @__PURE__ */ a.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ a.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ a.jsx(
            za,
            {
              label: "Inspector panel tabs",
              tabs: zr,
              activeTabId: es.id,
              onSelect: we
            }
          ),
          /* @__PURE__ */ a.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ a.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": Ie ? "Expand inspector panel" : "Collapse inspector panel",
                title: Ie ? "Expand" : "Collapse",
                onClick: () => xn("inspector"),
                children: Ie ? /* @__PURE__ */ a.jsx(Uo, { size: 14 }) : /* @__PURE__ */ a.jsx(Bt, { size: 14 })
              }
            ),
            Ie ? null : /* @__PURE__ */ a.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": ge === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: ge === "inspector" ? "Restore" : "Maximize",
                onClick: () => go("inspector"),
                children: ge === "inspector" ? /* @__PURE__ */ a.jsx(ua, { size: 14 }) : /* @__PURE__ */ a.jsx(Ko, { size: 14 })
              }
            )
          ] })
        ] }),
        Ki ? es.render() : null
      ] })
    ] })
  ] });
}
function za({
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
function Ra(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
function H0({ data: e, selected: t }) {
  const n = e, o = n.runtime, r = !n.suppressFlowPorts, i = r ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], s = O0(n);
  return /* @__PURE__ */ a.jsxs(
    "div",
    {
      className: ["wf-node", t ? "selected" : "", o ? "wf-node-runtime" : "", o?.hasBlockingIncident ? "faulted" : ""].filter(Boolean).join(" "),
      "data-icon": n.icon ?? "activity",
      children: [
        r && n.acceptsInbound ? /* @__PURE__ */ a.jsx(on, { type: "target", position: te.Left }) : null,
        /* @__PURE__ */ a.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ a.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: xd(n.icon) }),
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
          o.status ? /* @__PURE__ */ a.jsx(Jn, { status: o.status, subStatus: o.subStatus }) : null,
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
            /* @__PURE__ */ a.jsx(on, { type: "source", position: te.Right, id: c.name, style: { top: l } })
          ] }, c.name);
        })
      ]
    }
  );
}
function O0(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((r) => !!r).join(" · ");
}
function xd(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ a.jsx(Hi, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ a.jsx(Rl, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ a.jsx(Wm, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ a.jsx(ur, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ a.jsx(Xm, { size: 15 });
    default:
      return /* @__PURE__ */ a.jsx(Tl, { size: 15 });
  }
}
function F0(e) {
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
  } = e, h = wt.useContext(ld), [g, y] = Y(!1), [v, w, m] = qo({ sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: c }), b = h?.highlightedEdgeId === t;
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsx(
      Un,
      {
        id: t,
        path: v,
        markerEnd: d,
        style: {
          ...l,
          strokeWidth: b ? 2.5 : l?.strokeWidth
        },
        label: f,
        labelX: w,
        labelY: m,
        labelStyle: u,
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1)
      }
    ),
    h ? /* @__PURE__ */ a.jsx(um, { children: /* @__PURE__ */ a.jsxs(
      "div",
      {
        className: ["wf-edge-actions", g ? "visible" : "", b ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${w}px, ${m}px)` },
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1),
        children: [
          /* @__PURE__ */ a.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (p) => h.requestInsertActivity(t, p.clientX, p.clientY), children: /* @__PURE__ */ a.jsx(Oi, { size: 12 }) }),
          /* @__PURE__ */ a.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => h.deleteEdge(t), children: /* @__PURE__ */ a.jsx(Zt, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function B0({ clientX: e, clientY: t, activities: n, onPick: o, onClose: r }) {
  const [i, s] = Y(""), [c, d] = Y(0), l = ce(null), f = ce(null), u = me(() => {
    const b = i.trim().toLowerCase(), p = n.filter(gd);
    return b ? p.filter((x) => Me(x).toLowerCase().includes(b) || x.activityTypeKey.toLowerCase().includes(b) || (x.category ?? "").toLowerCase().includes(b) || (x.description ?? "").toLowerCase().includes(b)) : p;
  }, [n, i]), h = me(() => fd(u), [u]), g = me(() => h.flatMap((b) => b.activities), [h]);
  ne(() => {
    requestAnimationFrame(() => f.current?.focus());
  }, []), ne(() => {
    const b = (x) => {
      l.current?.contains(x.target) || r();
    }, p = (x) => {
      x.key === "Escape" && r();
    };
    return document.addEventListener("mousedown", b, !0), document.addEventListener("keydown", p), () => {
      document.removeEventListener("mousedown", b, !0), document.removeEventListener("keydown", p);
    };
  }, [r]);
  const y = (b) => {
    if (b.key === "ArrowDown")
      b.preventDefault(), d((p) => Math.min(p + 1, g.length - 1));
    else if (b.key === "ArrowUp")
      b.preventDefault(), d((p) => Math.max(p - 1, 0));
    else if (b.key === "Enter") {
      b.preventDefault();
      const p = g[c];
      p && o(p);
    }
  }, v = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), w = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let m = -1;
  return /* @__PURE__ */ a.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: v, top: w }, onMouseDown: (b) => b.stopPropagation(), onClick: (b) => b.stopPropagation(), children: [
    /* @__PURE__ */ a.jsx(
      "input",
      {
        ref: f,
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
      b.activities.map((p) => {
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
            onClick: () => o(p),
            children: [
              /* @__PURE__ */ a.jsx("strong", { children: Me(p) }),
              /* @__PURE__ */ a.jsx("small", { children: p.category || p.activityTypeKey })
            ]
          },
          p.activityVersionId
        );
      })
    ] }, b.category)) })
  ] });
}
function W0({ draft: e }) {
  return e.validationErrors.length ? /* @__PURE__ */ a.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ a.jsx(et, { size: 14 }),
    e.validationErrors.length,
    " validation issue",
    e.validationErrors.length === 1 ? "" : "s"
  ] }) : /* @__PURE__ */ a.jsxs("div", { className: "wf-validation ok", children: [
    /* @__PURE__ */ a.jsx(sn, { size: 14 }),
    " No validation errors"
  ] });
}
function X0({
  testRun: e,
  open: t,
  onToggle: n
}) {
  const o = Nd(e);
  return /* @__PURE__ */ a.jsxs("div", { className: "wf-test-run-status", "data-state": o ? "rejected" : "accepted", children: [
    /* @__PURE__ */ a.jsxs(
      "button",
      {
        type: "button",
        className: "wf-test-run-trigger",
        "aria-expanded": t,
        "aria-haspopup": "dialog",
        onClick: n,
        children: [
          o ? /* @__PURE__ */ a.jsx(et, { size: 16 }) : /* @__PURE__ */ a.jsx(sn, { size: 16 }),
          o ? "Test run rejected" : "Test run dispatched",
          /* @__PURE__ */ a.jsx(zl, { size: 14 })
        ]
      }
    ),
    t ? /* @__PURE__ */ a.jsxs("section", { className: "wf-test-run-popover", role: "dialog", "aria-label": "Test run details", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-test-run-popover-heading", children: [
        /* @__PURE__ */ a.jsx("strong", { children: o ? "Rejected by the server" : "Transient run accepted" }),
        /* @__PURE__ */ a.jsx("span", { children: "Ephemeral - not promoted" })
      ] }),
      o && e.reason ? /* @__PURE__ */ a.jsx("p", { children: e.reason }) : null,
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
        e.workflowExecutionId ? /* @__PURE__ */ a.jsxs("div", { children: [
          /* @__PURE__ */ a.jsx("dt", { children: "Execution" }),
          /* @__PURE__ */ a.jsx("dd", { title: e.workflowExecutionId, children: e.workflowExecutionId })
        ] }) : null,
        e.expiresAt ? /* @__PURE__ */ a.jsxs("div", { children: [
          /* @__PURE__ */ a.jsx("dt", { children: "Expires" }),
          /* @__PURE__ */ a.jsx("dd", { title: Qe(e.expiresAt), children: Qe(e.expiresAt) })
        ] }) : null
      ] })
    ] }) : null
  ] });
}
function La(e) {
  return `${Me(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function Va(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function Y0(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function wd(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function q0(e) {
  const t = wd(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function Z0(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function at(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function U0(e) {
  return bd(at(e));
}
function vd(e, t, n = []) {
  if (!e) return n;
  const o = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: o?.activityTypeKey ?? e.activityVersionId,
    displayName: o ? Me(o) : void 0
  });
  for (const r of Je(e))
    for (const i of r.activities) vd(i, t, n);
  return n;
}
function K0(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function G0(e) {
  return `${e.id}-${bd(JSON.stringify(e.state))}`;
}
function bd(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function Nd(e) {
  return e.status.toLowerCase() === "rejected";
}
function Qe(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function J0(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function Q0(e, t) {
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
  q0 as isConnectEndOverExistingWorkflowNode,
  nw as register,
  Z0 as resolveConnectEndSource
};
