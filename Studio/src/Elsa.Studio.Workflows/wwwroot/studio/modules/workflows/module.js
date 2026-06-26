import wt, { memo as Se, forwardRef as Go, useRef as ce, useEffect as oe, useCallback as pe, useContext as On, useMemo as ge, useState as Y, createContext as vi, useLayoutEffect as Ed, createElement as Jr, useId as La } from "react";
import "@tanstack/react-query";
function Cd(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var Tr = { exports: {} }, mn = {};
var ts;
function kd() {
  if (ts) return mn;
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
  return mn.Fragment = t, mn.jsx = n, mn.jsxs = n, mn;
}
var ns;
function Id() {
  return ns || (ns = 1, Tr.exports = kd()), Tr.exports;
}
var a = Id();
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
var jd = { value: () => {
} };
function Jo() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new jo(n);
}
function jo(e) {
  this._ = e;
}
function _d(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
jo.prototype = Jo.prototype = {
  constructor: jo,
  on: function(e, t) {
    var n = this._, o = _d(e + "", n), r, i = -1, s = o.length;
    if (arguments.length < 2) {
      for (; ++i < s; ) if ((r = (e = o[i]).type) && (r = Ad(n[r], e.name))) return r;
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
    return new jo(e);
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
function Ad(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function os(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = jd, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var Qr = "http://www.w3.org/1999/xhtml";
const rs = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: Qr,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function Qo(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), rs.hasOwnProperty(t) ? { space: rs[t], local: e } : e;
}
function Md(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === Qr && t.documentElement.namespaceURI === Qr ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Dd(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function Ra(e) {
  var t = Qo(e);
  return (t.local ? Dd : Md)(t);
}
function Pd() {
}
function bi(e) {
  return e == null ? Pd : function() {
    return this.querySelector(e);
  };
}
function $d(e) {
  typeof e != "function" && (e = bi(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = new Array(s), d, l, f = 0; f < s; ++f)
      (d = i[f]) && (l = e.call(d, d.__data__, f, i)) && ("__data__" in d && (l.__data__ = d.__data__), c[f] = l);
  return new He(o, this._parents);
}
function Td(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function zd() {
  return [];
}
function Va(e) {
  return e == null ? zd : function() {
    return this.querySelectorAll(e);
  };
}
function Ld(e) {
  return function() {
    return Td(e.apply(this, arguments));
  };
}
function Rd(e) {
  typeof e == "function" ? e = Ld(e) : e = Va(e);
  for (var t = this._groups, n = t.length, o = [], r = [], i = 0; i < n; ++i)
    for (var s = t[i], c = s.length, d, l = 0; l < c; ++l)
      (d = s[l]) && (o.push(e.call(d, d.__data__, l, s)), r.push(d));
  return new He(o, r);
}
function Ha(e) {
  return function() {
    return this.matches(e);
  };
}
function Oa(e) {
  return function(t) {
    return t.matches(e);
  };
}
var Vd = Array.prototype.find;
function Hd(e) {
  return function() {
    return Vd.call(this.children, e);
  };
}
function Od() {
  return this.firstElementChild;
}
function Fd(e) {
  return this.select(e == null ? Od : Hd(typeof e == "function" ? e : Oa(e)));
}
var Bd = Array.prototype.filter;
function Wd() {
  return Array.from(this.children);
}
function Xd(e) {
  return function() {
    return Bd.call(this.children, e);
  };
}
function Yd(e) {
  return this.selectAll(e == null ? Wd : Xd(typeof e == "function" ? e : Oa(e)));
}
function qd(e) {
  typeof e != "function" && (e = Ha(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = [], d, l = 0; l < s; ++l)
      (d = i[l]) && e.call(d, d.__data__, l, i) && c.push(d);
  return new He(o, this._parents);
}
function Fa(e) {
  return new Array(e.length);
}
function Zd() {
  return new He(this._enter || this._groups.map(Fa), this._parents);
}
function zo(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
zo.prototype = {
  constructor: zo,
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
function Ud(e) {
  return function() {
    return e;
  };
}
function Kd(e, t, n, o, r, i) {
  for (var s = 0, c, d = t.length, l = i.length; s < l; ++s)
    (c = t[s]) ? (c.__data__ = i[s], o[s] = c) : n[s] = new zo(e, i[s]);
  for (; s < d; ++s)
    (c = t[s]) && (r[s] = c);
}
function Gd(e, t, n, o, r, i, s) {
  var c, d, l = /* @__PURE__ */ new Map(), f = t.length, u = i.length, h = new Array(f), g;
  for (c = 0; c < f; ++c)
    (d = t[c]) && (h[c] = g = s.call(d, d.__data__, c, t) + "", l.has(g) ? r[c] = d : l.set(g, d));
  for (c = 0; c < u; ++c)
    g = s.call(e, i[c], c, i) + "", (d = l.get(g)) ? (o[c] = d, d.__data__ = i[c], l.delete(g)) : n[c] = new zo(e, i[c]);
  for (c = 0; c < f; ++c)
    (d = t[c]) && l.get(h[c]) === d && (r[c] = d);
}
function Jd(e) {
  return e.__data__;
}
function Qd(e, t) {
  if (!arguments.length) return Array.from(this, Jd);
  var n = t ? Gd : Kd, o = this._parents, r = this._groups;
  typeof e != "function" && (e = Ud(e));
  for (var i = r.length, s = new Array(i), c = new Array(i), d = new Array(i), l = 0; l < i; ++l) {
    var f = o[l], u = r[l], h = u.length, g = eu(e.call(f, f && f.__data__, l, o)), y = g.length, v = c[l] = new Array(y), x = s[l] = new Array(y), m = d[l] = new Array(h);
    n(f, u, v, x, m, g, t);
    for (var E = 0, p = 0, w, b; E < y; ++E)
      if (w = v[E]) {
        for (E >= p && (p = E + 1); !(b = x[p]) && ++p < y; ) ;
        w._next = b || null;
      }
  }
  return s = new He(s, o), s._enter = c, s._exit = d, s;
}
function eu(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function tu() {
  return new He(this._exit || this._groups.map(Fa), this._parents);
}
function nu(e, t, n) {
  var o = this.enter(), r = this, i = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? i.remove() : n(i), o && r ? o.merge(r).order() : r;
}
function ou(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, i = o.length, s = Math.min(r, i), c = new Array(r), d = 0; d < s; ++d)
    for (var l = n[d], f = o[d], u = l.length, h = c[d] = new Array(u), g, y = 0; y < u; ++y)
      (g = l[y] || f[y]) && (h[y] = g);
  for (; d < r; ++d)
    c[d] = n[d];
  return new He(c, this._parents);
}
function ru() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, i = o[r], s; --r >= 0; )
      (s = o[r]) && (i && s.compareDocumentPosition(i) ^ 4 && i.parentNode.insertBefore(s, i), i = s);
  return this;
}
function iu(e) {
  e || (e = su);
  function t(u, h) {
    return u && h ? e(u.__data__, h.__data__) : !u - !h;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), i = 0; i < o; ++i) {
    for (var s = n[i], c = s.length, d = r[i] = new Array(c), l, f = 0; f < c; ++f)
      (l = s[f]) && (d[f] = l);
    d.sort(t);
  }
  return new He(r, this._parents).order();
}
function su(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function au() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function cu() {
  return Array.from(this);
}
function lu() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length; r < i; ++r) {
      var s = o[r];
      if (s) return s;
    }
  return null;
}
function du() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function uu() {
  return !this.node();
}
function fu(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], i = 0, s = r.length, c; i < s; ++i)
      (c = r[i]) && e.call(c, c.__data__, i, r);
  return this;
}
function hu(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function pu(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function gu(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function yu(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function mu(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function xu(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function wu(e, t) {
  var n = Qo(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? pu : hu : typeof t == "function" ? n.local ? xu : mu : n.local ? yu : gu)(n, t));
}
function Ba(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function vu(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function bu(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function Nu(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function Su(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? vu : typeof t == "function" ? Nu : bu)(e, t, n ?? "")) : Kt(this.node(), e);
}
function Kt(e, t) {
  return e.style.getPropertyValue(t) || Ba(e).getComputedStyle(e, null).getPropertyValue(t);
}
function Eu(e) {
  return function() {
    delete this[e];
  };
}
function Cu(e, t) {
  return function() {
    this[e] = t;
  };
}
function ku(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function Iu(e, t) {
  return arguments.length > 1 ? this.each((t == null ? Eu : typeof t == "function" ? ku : Cu)(e, t)) : this.node()[e];
}
function Wa(e) {
  return e.trim().split(/^|\s+/);
}
function Ni(e) {
  return e.classList || new Xa(e);
}
function Xa(e) {
  this._node = e, this._names = Wa(e.getAttribute("class") || "");
}
Xa.prototype = {
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
function Ya(e, t) {
  for (var n = Ni(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function qa(e, t) {
  for (var n = Ni(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function ju(e) {
  return function() {
    Ya(this, e);
  };
}
function _u(e) {
  return function() {
    qa(this, e);
  };
}
function Au(e, t) {
  return function() {
    (t.apply(this, arguments) ? Ya : qa)(this, e);
  };
}
function Mu(e, t) {
  var n = Wa(e + "");
  if (arguments.length < 2) {
    for (var o = Ni(this.node()), r = -1, i = n.length; ++r < i; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? Au : t ? ju : _u)(n, t));
}
function Du() {
  this.textContent = "";
}
function Pu(e) {
  return function() {
    this.textContent = e;
  };
}
function $u(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function Tu(e) {
  return arguments.length ? this.each(e == null ? Du : (typeof e == "function" ? $u : Pu)(e)) : this.node().textContent;
}
function zu() {
  this.innerHTML = "";
}
function Lu(e) {
  return function() {
    this.innerHTML = e;
  };
}
function Ru(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function Vu(e) {
  return arguments.length ? this.each(e == null ? zu : (typeof e == "function" ? Ru : Lu)(e)) : this.node().innerHTML;
}
function Hu() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function Ou() {
  return this.each(Hu);
}
function Fu() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Bu() {
  return this.each(Fu);
}
function Wu(e) {
  var t = typeof e == "function" ? e : Ra(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Xu() {
  return null;
}
function Yu(e, t) {
  var n = typeof e == "function" ? e : Ra(e), o = t == null ? Xu : typeof t == "function" ? t : bi(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function qu() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Zu() {
  return this.each(qu);
}
function Uu() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Ku() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Gu(e) {
  return this.select(e ? Ku : Uu);
}
function Ju(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function Qu(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function ef(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function tf(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, i; n < r; ++n)
        i = t[n], (!e.type || i.type === e.type) && i.name === e.name ? this.removeEventListener(i.type, i.listener, i.options) : t[++o] = i;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function nf(e, t, n) {
  return function() {
    var o = this.__on, r, i = Qu(t);
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
function of(e, t, n) {
  var o = ef(e + ""), r, i = o.length, s;
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
  for (c = t ? nf : tf, r = 0; r < i; ++r) this.each(c(o[r], t, n));
  return this;
}
function Za(e, t, n) {
  var o = Ba(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function rf(e, t) {
  return function() {
    return Za(this, e, t);
  };
}
function sf(e, t) {
  return function() {
    return Za(this, e, t.apply(this, arguments));
  };
}
function af(e, t) {
  return this.each((typeof t == "function" ? sf : rf)(e, t));
}
function* cf() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length, s; r < i; ++r)
      (s = o[r]) && (yield s);
}
var Ua = [null];
function He(e, t) {
  this._groups = e, this._parents = t;
}
function Fn() {
  return new He([[document.documentElement]], Ua);
}
function lf() {
  return this;
}
He.prototype = Fn.prototype = {
  constructor: He,
  select: $d,
  selectAll: Rd,
  selectChild: Fd,
  selectChildren: Yd,
  filter: qd,
  data: Qd,
  enter: Zd,
  exit: tu,
  join: nu,
  merge: ou,
  selection: lf,
  order: ru,
  sort: iu,
  call: au,
  nodes: cu,
  node: lu,
  size: du,
  empty: uu,
  each: fu,
  attr: wu,
  style: Su,
  property: Iu,
  classed: Mu,
  text: Tu,
  html: Vu,
  raise: Ou,
  lower: Bu,
  append: Wu,
  insert: Yu,
  remove: Zu,
  clone: Gu,
  datum: Ju,
  on: of,
  dispatch: af,
  [Symbol.iterator]: cf
};
function Ve(e) {
  return typeof e == "string" ? new He([[document.querySelector(e)]], [document.documentElement]) : new He([[e]], Ua);
}
function df(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Ye(e, t) {
  if (e = df(e), t === void 0 && (t = e.currentTarget), t) {
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
const uf = { passive: !1 }, Mn = { capture: !0, passive: !1 };
function zr(e) {
  e.stopImmediatePropagation();
}
function qt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Ka(e) {
  var t = e.document.documentElement, n = Ve(e).on("dragstart.drag", qt, Mn);
  "onselectstart" in t ? n.on("selectstart.drag", qt, Mn) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function Ga(e, t) {
  var n = e.document.documentElement, o = Ve(e).on("dragstart.drag", null);
  t && (o.on("click.drag", qt, Mn), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const mo = (e) => () => e;
function ei(e, {
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
ei.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function ff(e) {
  return !e.ctrlKey && !e.button;
}
function hf() {
  return this.parentNode;
}
function pf(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function gf() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Ja() {
  var e = ff, t = hf, n = pf, o = gf, r = {}, i = Jo("start", "drag", "end"), s = 0, c, d, l, f, u = 0;
  function h(w) {
    w.on("mousedown.drag", g).filter(o).on("touchstart.drag", x).on("touchmove.drag", m, uf).on("touchend.drag touchcancel.drag", E).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function g(w, b) {
    if (!(f || !e.call(this, w, b))) {
      var C = p(this, t.call(this, w, b), w, b, "mouse");
      C && (Ve(w.view).on("mousemove.drag", y, Mn).on("mouseup.drag", v, Mn), Ka(w.view), zr(w), l = !1, c = w.clientX, d = w.clientY, C("start", w));
    }
  }
  function y(w) {
    if (qt(w), !l) {
      var b = w.clientX - c, C = w.clientY - d;
      l = b * b + C * C > u;
    }
    r.mouse("drag", w);
  }
  function v(w) {
    Ve(w.view).on("mousemove.drag mouseup.drag", null), Ga(w.view, l), qt(w), r.mouse("end", w);
  }
  function x(w, b) {
    if (e.call(this, w, b)) {
      var C = w.changedTouches, k = t.call(this, w, b), A = C.length, P, W;
      for (P = 0; P < A; ++P)
        (W = p(this, k, w, b, C[P].identifier, C[P])) && (zr(w), W("start", w, C[P]));
    }
  }
  function m(w) {
    var b = w.changedTouches, C = b.length, k, A;
    for (k = 0; k < C; ++k)
      (A = r[b[k].identifier]) && (qt(w), A("drag", w, b[k]));
  }
  function E(w) {
    var b = w.changedTouches, C = b.length, k, A;
    for (f && clearTimeout(f), f = setTimeout(function() {
      f = null;
    }, 500), k = 0; k < C; ++k)
      (A = r[b[k].identifier]) && (zr(w), A("end", w, b[k]));
  }
  function p(w, b, C, k, A, P) {
    var W = i.copy(), M = Ye(P || C, b), z, O, N;
    if ((N = n.call(w, new ei("beforestart", {
      sourceEvent: C,
      target: h,
      identifier: A,
      active: s,
      x: M[0],
      y: M[1],
      dx: 0,
      dy: 0,
      dispatch: W
    }), k)) != null)
      return z = N.x - M[0] || 0, O = N.y - M[1] || 0, function j(I, D, $) {
        var T = M, H;
        switch (I) {
          case "start":
            r[A] = j, H = s++;
            break;
          case "end":
            delete r[A], --s;
          // falls through
          case "drag":
            M = Ye($ || D, b), H = s;
            break;
        }
        W.call(
          I,
          w,
          new ei(I, {
            sourceEvent: D,
            subject: N,
            target: h,
            identifier: A,
            active: H,
            x: M[0] + z,
            y: M[1] + O,
            dx: M[0] - T[0],
            dy: M[1] - T[1],
            dispatch: W
          }),
          k
        );
      };
  }
  return h.filter = function(w) {
    return arguments.length ? (e = typeof w == "function" ? w : mo(!!w), h) : e;
  }, h.container = function(w) {
    return arguments.length ? (t = typeof w == "function" ? w : mo(w), h) : t;
  }, h.subject = function(w) {
    return arguments.length ? (n = typeof w == "function" ? w : mo(w), h) : n;
  }, h.touchable = function(w) {
    return arguments.length ? (o = typeof w == "function" ? w : mo(!!w), h) : o;
  }, h.on = function() {
    var w = i.on.apply(i, arguments);
    return w === i ? h : w;
  }, h.clickDistance = function(w) {
    return arguments.length ? (u = (w = +w) * w, h) : Math.sqrt(u);
  }, h;
}
function Si(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function Qa(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function Bn() {
}
var Dn = 0.7, Lo = 1 / Dn, Zt = "\\s*([+-]?\\d+)\\s*", Pn = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", tt = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", yf = /^#([0-9a-f]{3,8})$/, mf = new RegExp(`^rgb\\(${Zt},${Zt},${Zt}\\)$`), xf = new RegExp(`^rgb\\(${tt},${tt},${tt}\\)$`), wf = new RegExp(`^rgba\\(${Zt},${Zt},${Zt},${Pn}\\)$`), vf = new RegExp(`^rgba\\(${tt},${tt},${tt},${Pn}\\)$`), bf = new RegExp(`^hsl\\(${Pn},${tt},${tt}\\)$`), Nf = new RegExp(`^hsla\\(${Pn},${tt},${tt},${Pn}\\)$`), is = {
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
Si(Bn, Dt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: ss,
  // Deprecated! Use color.formatHex.
  formatHex: ss,
  formatHex8: Sf,
  formatHsl: Ef,
  formatRgb: as,
  toString: as
});
function ss() {
  return this.rgb().formatHex();
}
function Sf() {
  return this.rgb().formatHex8();
}
function Ef() {
  return ec(this).formatHsl();
}
function as() {
  return this.rgb().formatRgb();
}
function Dt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = yf.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? cs(t) : n === 3 ? new $e(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? xo(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? xo(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = mf.exec(e)) ? new $e(t[1], t[2], t[3], 1) : (t = xf.exec(e)) ? new $e(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = wf.exec(e)) ? xo(t[1], t[2], t[3], t[4]) : (t = vf.exec(e)) ? xo(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = bf.exec(e)) ? us(t[1], t[2] / 100, t[3] / 100, 1) : (t = Nf.exec(e)) ? us(t[1], t[2] / 100, t[3] / 100, t[4]) : is.hasOwnProperty(e) ? cs(is[e]) : e === "transparent" ? new $e(NaN, NaN, NaN, 0) : null;
}
function cs(e) {
  return new $e(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function xo(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new $e(e, t, n, o);
}
function Cf(e) {
  return e instanceof Bn || (e = Dt(e)), e ? (e = e.rgb(), new $e(e.r, e.g, e.b, e.opacity)) : new $e();
}
function ti(e, t, n, o) {
  return arguments.length === 1 ? Cf(e) : new $e(e, t, n, o ?? 1);
}
function $e(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
Si($e, ti, Qa(Bn, {
  brighter(e) {
    return e = e == null ? Lo : Math.pow(Lo, e), new $e(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Dn : Math.pow(Dn, e), new $e(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new $e(_t(this.r), _t(this.g), _t(this.b), Ro(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: ls,
  // Deprecated! Use color.formatHex.
  formatHex: ls,
  formatHex8: kf,
  formatRgb: ds,
  toString: ds
}));
function ls() {
  return `#${jt(this.r)}${jt(this.g)}${jt(this.b)}`;
}
function kf() {
  return `#${jt(this.r)}${jt(this.g)}${jt(this.b)}${jt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function ds() {
  const e = Ro(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${_t(this.r)}, ${_t(this.g)}, ${_t(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Ro(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function _t(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function jt(e) {
  return e = _t(e), (e < 16 ? "0" : "") + e.toString(16);
}
function us(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new qe(e, t, n, o);
}
function ec(e) {
  if (e instanceof qe) return new qe(e.h, e.s, e.l, e.opacity);
  if (e instanceof Bn || (e = Dt(e)), !e) return new qe();
  if (e instanceof qe) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), i = Math.max(t, n, o), s = NaN, c = i - r, d = (i + r) / 2;
  return c ? (t === i ? s = (n - o) / c + (n < o) * 6 : n === i ? s = (o - t) / c + 2 : s = (t - n) / c + 4, c /= d < 0.5 ? i + r : 2 - i - r, s *= 60) : c = d > 0 && d < 1 ? 0 : s, new qe(s, c, d, e.opacity);
}
function If(e, t, n, o) {
  return arguments.length === 1 ? ec(e) : new qe(e, t, n, o ?? 1);
}
function qe(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
Si(qe, If, Qa(Bn, {
  brighter(e) {
    return e = e == null ? Lo : Math.pow(Lo, e), new qe(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Dn : Math.pow(Dn, e), new qe(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new $e(
      Lr(e >= 240 ? e - 240 : e + 120, r, o),
      Lr(e, r, o),
      Lr(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new qe(fs(this.h), wo(this.s), wo(this.l), Ro(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Ro(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${fs(this.h)}, ${wo(this.s) * 100}%, ${wo(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function fs(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function wo(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function Lr(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const Ei = (e) => () => e;
function jf(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function _f(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function Af(e) {
  return (e = +e) == 1 ? tc : function(t, n) {
    return n - t ? _f(t, n, e) : Ei(isNaN(t) ? n : t);
  };
}
function tc(e, t) {
  var n = t - e;
  return n ? jf(e, n) : Ei(isNaN(e) ? t : e);
}
const Vo = (function e(t) {
  var n = Af(t);
  function o(r, i) {
    var s = n((r = ti(r)).r, (i = ti(i)).r), c = n(r.g, i.g), d = n(r.b, i.b), l = tc(r.opacity, i.opacity);
    return function(f) {
      return r.r = s(f), r.g = c(f), r.b = d(f), r.opacity = l(f), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function Mf(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(i) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - i) + t[r] * i;
    return o;
  };
}
function Df(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Pf(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), i = new Array(n), s;
  for (s = 0; s < o; ++s) r[s] = jn(e[s], t[s]);
  for (; s < n; ++s) i[s] = t[s];
  return function(c) {
    for (s = 0; s < o; ++s) i[s] = r[s](c);
    return i;
  };
}
function $f(e, t) {
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
function Tf(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = jn(e[r], t[r]) : o[r] = t[r];
  return function(i) {
    for (r in n) o[r] = n[r](i);
    return o;
  };
}
var ni = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, Rr = new RegExp(ni.source, "g");
function zf(e) {
  return function() {
    return e;
  };
}
function Lf(e) {
  return function(t) {
    return e(t) + "";
  };
}
function nc(e, t) {
  var n = ni.lastIndex = Rr.lastIndex = 0, o, r, i, s = -1, c = [], d = [];
  for (e = e + "", t = t + ""; (o = ni.exec(e)) && (r = Rr.exec(t)); )
    (i = r.index) > n && (i = t.slice(n, i), c[s] ? c[s] += i : c[++s] = i), (o = o[0]) === (r = r[0]) ? c[s] ? c[s] += r : c[++s] = r : (c[++s] = null, d.push({ i: s, x: et(o, r) })), n = Rr.lastIndex;
  return n < t.length && (i = t.slice(n), c[s] ? c[s] += i : c[++s] = i), c.length < 2 ? d[0] ? Lf(d[0].x) : zf(t) : (t = d.length, function(l) {
    for (var f = 0, u; f < t; ++f) c[(u = d[f]).i] = u.x(l);
    return c.join("");
  });
}
function jn(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? Ei(t) : (n === "number" ? et : n === "string" ? (o = Dt(t)) ? (t = o, Vo) : nc : t instanceof Dt ? Vo : t instanceof Date ? $f : Df(t) ? Mf : Array.isArray(t) ? Pf : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Tf : et)(e, t);
}
var hs = 180 / Math.PI, oi = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function oc(e, t, n, o, r, i) {
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
var vo;
function Rf(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? oi : oc(t.a, t.b, t.c, t.d, t.e, t.f);
}
function Vf(e) {
  return e == null || (vo || (vo = document.createElementNS("http://www.w3.org/2000/svg", "g")), vo.setAttribute("transform", e), !(e = vo.transform.baseVal.consolidate())) ? oi : (e = e.matrix, oc(e.a, e.b, e.c, e.d, e.e, e.f));
}
function rc(e, t, n, o) {
  function r(l) {
    return l.length ? l.pop() + " " : "";
  }
  function i(l, f, u, h, g, y) {
    if (l !== u || f !== h) {
      var v = g.push("translate(", null, t, null, n);
      y.push({ i: v - 4, x: et(l, u) }, { i: v - 2, x: et(f, h) });
    } else (u || h) && g.push("translate(" + u + t + h + n);
  }
  function s(l, f, u, h) {
    l !== f ? (l - f > 180 ? f += 360 : f - l > 180 && (l += 360), h.push({ i: u.push(r(u) + "rotate(", null, o) - 2, x: et(l, f) })) : f && u.push(r(u) + "rotate(" + f + o);
  }
  function c(l, f, u, h) {
    l !== f ? h.push({ i: u.push(r(u) + "skewX(", null, o) - 2, x: et(l, f) }) : f && u.push(r(u) + "skewX(" + f + o);
  }
  function d(l, f, u, h, g, y) {
    if (l !== u || f !== h) {
      var v = g.push(r(g) + "scale(", null, ",", null, ")");
      y.push({ i: v - 4, x: et(l, u) }, { i: v - 2, x: et(f, h) });
    } else (u !== 1 || h !== 1) && g.push(r(g) + "scale(" + u + "," + h + ")");
  }
  return function(l, f) {
    var u = [], h = [];
    return l = e(l), f = e(f), i(l.translateX, l.translateY, f.translateX, f.translateY, u, h), s(l.rotate, f.rotate, u, h), c(l.skewX, f.skewX, u, h), d(l.scaleX, l.scaleY, f.scaleX, f.scaleY, u, h), l = f = null, function(g) {
      for (var y = -1, v = h.length, x; ++y < v; ) u[(x = h[y]).i] = x.x(g);
      return u.join("");
    };
  };
}
var Hf = rc(Rf, "px, ", "px)", "deg)"), Of = rc(Vf, ", ", ")", ")"), Ff = 1e-12;
function ps(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Bf(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Wf(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const _o = (function e(t, n, o) {
  function r(i, s) {
    var c = i[0], d = i[1], l = i[2], f = s[0], u = s[1], h = s[2], g = f - c, y = u - d, v = g * g + y * y, x, m;
    if (v < Ff)
      m = Math.log(h / l) / t, x = function(k) {
        return [
          c + k * g,
          d + k * y,
          l * Math.exp(t * k * m)
        ];
      };
    else {
      var E = Math.sqrt(v), p = (h * h - l * l + o * v) / (2 * l * n * E), w = (h * h - l * l - o * v) / (2 * h * n * E), b = Math.log(Math.sqrt(p * p + 1) - p), C = Math.log(Math.sqrt(w * w + 1) - w);
      m = (C - b) / t, x = function(k) {
        var A = k * m, P = ps(b), W = l / (n * E) * (P * Wf(t * A + b) - Bf(b));
        return [
          c + W * g,
          d + W * y,
          l * P / ps(t * A + b)
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
var Gt = 0, Cn = 0, xn = 0, ic = 1e3, Ho, kn, Oo = 0, Pt = 0, er = 0, $n = typeof performance == "object" && performance.now ? performance : Date, sc = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function Ci() {
  return Pt || (sc(Xf), Pt = $n.now() + er);
}
function Xf() {
  Pt = 0;
}
function Fo() {
  this._call = this._time = this._next = null;
}
Fo.prototype = ac.prototype = {
  constructor: Fo,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? Ci() : +n) + (t == null ? 0 : +t), !this._next && kn !== this && (kn ? kn._next = this : Ho = this, kn = this), this._call = e, this._time = n, ri();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, ri());
  }
};
function ac(e, t, n) {
  var o = new Fo();
  return o.restart(e, t, n), o;
}
function Yf() {
  Ci(), ++Gt;
  for (var e = Ho, t; e; )
    (t = Pt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --Gt;
}
function gs() {
  Pt = (Oo = $n.now()) + er, Gt = Cn = 0;
  try {
    Yf();
  } finally {
    Gt = 0, Zf(), Pt = 0;
  }
}
function qf() {
  var e = $n.now(), t = e - Oo;
  t > ic && (er -= t, Oo = e);
}
function Zf() {
  for (var e, t = Ho, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Ho = n);
  kn = e, ri(o);
}
function ri(e) {
  if (!Gt) {
    Cn && (Cn = clearTimeout(Cn));
    var t = e - Pt;
    t > 24 ? (e < 1 / 0 && (Cn = setTimeout(gs, e - $n.now() - er)), xn && (xn = clearInterval(xn))) : (xn || (Oo = $n.now(), xn = setInterval(qf, ic)), Gt = 1, sc(gs));
  }
}
function ys(e, t, n) {
  var o = new Fo();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var Uf = Jo("start", "end", "cancel", "interrupt"), Kf = [], cc = 0, ms = 1, ii = 2, Ao = 3, xs = 4, si = 5, Mo = 6;
function tr(e, t, n, o, r, i) {
  var s = e.__transition;
  if (!s) e.__transition = {};
  else if (n in s) return;
  Gf(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: Uf,
    tween: Kf,
    time: i.time,
    delay: i.delay,
    duration: i.duration,
    ease: i.ease,
    timer: null,
    state: cc
  });
}
function ki(e, t) {
  var n = Je(e, t);
  if (n.state > cc) throw new Error("too late; already scheduled");
  return n;
}
function ot(e, t) {
  var n = Je(e, t);
  if (n.state > Ao) throw new Error("too late; already running");
  return n;
}
function Je(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function Gf(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = ac(i, 0, n.time);
  function i(l) {
    n.state = ms, n.timer.restart(s, n.delay, n.time), n.delay <= l && s(l - n.delay);
  }
  function s(l) {
    var f, u, h, g;
    if (n.state !== ms) return d();
    for (f in o)
      if (g = o[f], g.name === n.name) {
        if (g.state === Ao) return ys(s);
        g.state === xs ? (g.state = Mo, g.timer.stop(), g.on.call("interrupt", e, e.__data__, g.index, g.group), delete o[f]) : +f < t && (g.state = Mo, g.timer.stop(), g.on.call("cancel", e, e.__data__, g.index, g.group), delete o[f]);
      }
    if (ys(function() {
      n.state === Ao && (n.state = xs, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = ii, n.on.call("start", e, e.__data__, n.index, n.group), n.state === ii) {
      for (n.state = Ao, r = new Array(h = n.tween.length), f = 0, u = -1; f < h; ++f)
        (g = n.tween[f].value.call(e, e.__data__, n.index, n.group)) && (r[++u] = g);
      r.length = u + 1;
    }
  }
  function c(l) {
    for (var f = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(d), n.state = si, 1), u = -1, h = r.length; ++u < h; )
      r[u].call(e, f);
    n.state === si && (n.on.call("end", e, e.__data__, n.index, n.group), d());
  }
  function d() {
    n.state = Mo, n.timer.stop(), delete o[t];
    for (var l in o) return;
    delete e.__transition;
  }
}
function Do(e, t) {
  var n = e.__transition, o, r, i = !0, s;
  if (n) {
    t = t == null ? null : t + "";
    for (s in n) {
      if ((o = n[s]).name !== t) {
        i = !1;
        continue;
      }
      r = o.state > ii && o.state < si, o.state = Mo, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[s];
    }
    i && delete e.__transition;
  }
}
function Jf(e) {
  return this.each(function() {
    Do(this, e);
  });
}
function Qf(e, t) {
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
function eh(e, t, n) {
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
function th(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = Je(this.node(), n).tween, r = 0, i = o.length, s; r < i; ++r)
      if ((s = o[r]).name === e)
        return s.value;
    return null;
  }
  return this.each((t == null ? Qf : eh)(n, e, t));
}
function Ii(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = ot(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return Je(r, o).value[t];
  };
}
function lc(e, t) {
  var n;
  return (typeof t == "number" ? et : t instanceof Dt ? Vo : (n = Dt(t)) ? (t = n, Vo) : nc)(e, t);
}
function nh(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function oh(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function rh(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttribute(e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function ih(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttributeNS(e.space, e.local);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function sh(e, t, n) {
  var o, r, i;
  return function() {
    var s, c = n(this), d;
    return c == null ? void this.removeAttribute(e) : (s = this.getAttribute(e), d = c + "", s === d ? null : s === o && d === r ? i : (r = d, i = t(o = s, c)));
  };
}
function ah(e, t, n) {
  var o, r, i;
  return function() {
    var s, c = n(this), d;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (s = this.getAttributeNS(e.space, e.local), d = c + "", s === d ? null : s === o && d === r ? i : (r = d, i = t(o = s, c)));
  };
}
function ch(e, t) {
  var n = Qo(e), o = n === "transform" ? Of : lc;
  return this.attrTween(e, typeof t == "function" ? (n.local ? ah : sh)(n, o, Ii(this, "attr." + e, t)) : t == null ? (n.local ? oh : nh)(n) : (n.local ? ih : rh)(n, o, t));
}
function lh(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function dh(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function uh(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && dh(e, i)), n;
  }
  return r._value = t, r;
}
function fh(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && lh(e, i)), n;
  }
  return r._value = t, r;
}
function hh(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = Qo(e);
  return this.tween(n, (o.local ? uh : fh)(o, t));
}
function ph(e, t) {
  return function() {
    ki(this, e).delay = +t.apply(this, arguments);
  };
}
function gh(e, t) {
  return t = +t, function() {
    ki(this, e).delay = t;
  };
}
function yh(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? ph : gh)(t, e)) : Je(this.node(), t).delay;
}
function mh(e, t) {
  return function() {
    ot(this, e).duration = +t.apply(this, arguments);
  };
}
function xh(e, t) {
  return t = +t, function() {
    ot(this, e).duration = t;
  };
}
function wh(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? mh : xh)(t, e)) : Je(this.node(), t).duration;
}
function vh(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    ot(this, e).ease = t;
  };
}
function bh(e) {
  var t = this._id;
  return arguments.length ? this.each(vh(t, e)) : Je(this.node(), t).ease;
}
function Nh(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    ot(this, e).ease = n;
  };
}
function Sh(e) {
  if (typeof e != "function") throw new Error();
  return this.each(Nh(this._id, e));
}
function Eh(e) {
  typeof e != "function" && (e = Ha(e));
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
function kh(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function Ih(e, t, n) {
  var o, r, i = kh(t) ? ki : ot;
  return function() {
    var s = i(this, e), c = s.on;
    c !== o && (r = (o = c).copy()).on(t, n), s.on = r;
  };
}
function jh(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Je(this.node(), n).on.on(e) : this.each(Ih(n, e, t));
}
function _h(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function Ah() {
  return this.on("end.remove", _h(this._id));
}
function Mh(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = bi(e));
  for (var o = this._groups, r = o.length, i = new Array(r), s = 0; s < r; ++s)
    for (var c = o[s], d = c.length, l = i[s] = new Array(d), f, u, h = 0; h < d; ++h)
      (f = c[h]) && (u = e.call(f, f.__data__, h, c)) && ("__data__" in f && (u.__data__ = f.__data__), l[h] = u, tr(l[h], t, n, h, l, Je(f, n)));
  return new lt(i, this._parents, t, n);
}
function Dh(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Va(e));
  for (var o = this._groups, r = o.length, i = [], s = [], c = 0; c < r; ++c)
    for (var d = o[c], l = d.length, f, u = 0; u < l; ++u)
      if (f = d[u]) {
        for (var h = e.call(f, f.__data__, u, d), g, y = Je(f, n), v = 0, x = h.length; v < x; ++v)
          (g = h[v]) && tr(g, t, n, v, h, y);
        i.push(h), s.push(f);
      }
  return new lt(i, s, t, n);
}
var Ph = Fn.prototype.constructor;
function $h() {
  return new Ph(this._groups, this._parents);
}
function Th(e, t) {
  var n, o, r;
  return function() {
    var i = Kt(this, e), s = (this.style.removeProperty(e), Kt(this, e));
    return i === s ? null : i === n && s === o ? r : r = t(n = i, o = s);
  };
}
function dc(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function zh(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = Kt(this, e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Lh(e, t, n) {
  var o, r, i;
  return function() {
    var s = Kt(this, e), c = n(this), d = c + "";
    return c == null && (d = c = (this.style.removeProperty(e), Kt(this, e))), s === d ? null : s === o && d === r ? i : (r = d, i = t(o = s, c));
  };
}
function Rh(e, t) {
  var n, o, r, i = "style." + t, s = "end." + i, c;
  return function() {
    var d = ot(this, e), l = d.on, f = d.value[i] == null ? c || (c = dc(t)) : void 0;
    (l !== n || r !== f) && (o = (n = l).copy()).on(s, r = f), d.on = o;
  };
}
function Vh(e, t, n) {
  var o = (e += "") == "transform" ? Hf : lc;
  return t == null ? this.styleTween(e, Th(e, o)).on("end.style." + e, dc(e)) : typeof t == "function" ? this.styleTween(e, Lh(e, o, Ii(this, "style." + e, t))).each(Rh(this._id, e)) : this.styleTween(e, zh(e, o, t), n).on("end.style." + e, null);
}
function Hh(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function Oh(e, t, n) {
  var o, r;
  function i() {
    var s = t.apply(this, arguments);
    return s !== r && (o = (r = s) && Hh(e, s, n)), o;
  }
  return i._value = t, i;
}
function Fh(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, Oh(e, t, n ?? ""));
}
function Bh(e) {
  return function() {
    this.textContent = e;
  };
}
function Wh(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function Xh(e) {
  return this.tween("text", typeof e == "function" ? Wh(Ii(this, "text", e)) : Bh(e == null ? "" : e + ""));
}
function Yh(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function qh(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Yh(r)), t;
  }
  return o._value = e, o;
}
function Zh(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, qh(e));
}
function Uh() {
  for (var e = this._name, t = this._id, n = uc(), o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], c = s.length, d, l = 0; l < c; ++l)
      if (d = s[l]) {
        var f = Je(d, t);
        tr(d, e, n, l, s, {
          time: f.time + f.delay + f.duration,
          delay: 0,
          duration: f.duration,
          ease: f.ease
        });
      }
  return new lt(o, this._parents, e, n);
}
function Kh() {
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
var Gh = 0;
function lt(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function uc() {
  return ++Gh;
}
var st = Fn.prototype;
lt.prototype = {
  constructor: lt,
  select: Mh,
  selectAll: Dh,
  selectChild: st.selectChild,
  selectChildren: st.selectChildren,
  filter: Eh,
  merge: Ch,
  selection: $h,
  transition: Uh,
  call: st.call,
  nodes: st.nodes,
  node: st.node,
  size: st.size,
  empty: st.empty,
  each: st.each,
  on: jh,
  attr: ch,
  attrTween: hh,
  style: Vh,
  styleTween: Fh,
  text: Xh,
  textTween: Zh,
  remove: Ah,
  tween: th,
  delay: yh,
  duration: wh,
  ease: bh,
  easeVarying: Sh,
  end: Kh,
  [Symbol.iterator]: st[Symbol.iterator]
};
function Jh(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var Qh = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: Jh
};
function ep(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function tp(e) {
  var t, n;
  e instanceof lt ? (t = e._id, e = e._name) : (t = uc(), (n = Qh).time = Ci(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], c = s.length, d, l = 0; l < c; ++l)
      (d = s[l]) && tr(d, e, t, l, s, n || ep(d, t));
  return new lt(o, this._parents, e, t);
}
Fn.prototype.interrupt = Jf;
Fn.prototype.transition = tp;
const bo = (e) => () => e;
function np(e, {
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
var nr = new ct(1, 0, 0);
fc.prototype = ct.prototype;
function fc(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return nr;
  return e.__zoom;
}
function Vr(e) {
  e.stopImmediatePropagation();
}
function wn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function op(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function rp() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function ws() {
  return this.__zoom || nr;
}
function ip(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function sp() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function ap(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], i = e.invertY(t[0][1]) - n[0][1], s = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    s > i ? (i + s) / 2 : Math.min(0, i) || Math.max(0, s)
  );
}
function hc() {
  var e = op, t = rp, n = ap, o = ip, r = sp, i = [0, 1 / 0], s = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, d = _o, l = Jo("start", "zoom", "end"), f, u, h, g = 500, y = 150, v = 0, x = 10;
  function m(N) {
    N.property("__zoom", ws).on("wheel.zoom", A, { passive: !1 }).on("mousedown.zoom", P).on("dblclick.zoom", W).filter(r).on("touchstart.zoom", M).on("touchmove.zoom", z).on("touchend.zoom touchcancel.zoom", O).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  m.transform = function(N, j, I, D) {
    var $ = N.selection ? N.selection() : N;
    $.property("__zoom", ws), N !== $ ? b(N, j, I, D) : $.interrupt().each(function() {
      C(this, arguments).event(D).start().zoom(null, typeof j == "function" ? j.apply(this, arguments) : j).end();
    });
  }, m.scaleBy = function(N, j, I, D) {
    m.scaleTo(N, function() {
      var $ = this.__zoom.k, T = typeof j == "function" ? j.apply(this, arguments) : j;
      return $ * T;
    }, I, D);
  }, m.scaleTo = function(N, j, I, D) {
    m.transform(N, function() {
      var $ = t.apply(this, arguments), T = this.__zoom, H = I == null ? w($) : typeof I == "function" ? I.apply(this, arguments) : I, B = T.invert(H), F = typeof j == "function" ? j.apply(this, arguments) : j;
      return n(p(E(T, F), H, B), $, s);
    }, I, D);
  }, m.translateBy = function(N, j, I, D) {
    m.transform(N, function() {
      return n(this.__zoom.translate(
        typeof j == "function" ? j.apply(this, arguments) : j,
        typeof I == "function" ? I.apply(this, arguments) : I
      ), t.apply(this, arguments), s);
    }, null, D);
  }, m.translateTo = function(N, j, I, D, $) {
    m.transform(N, function() {
      var T = t.apply(this, arguments), H = this.__zoom, B = D == null ? w(T) : typeof D == "function" ? D.apply(this, arguments) : D;
      return n(nr.translate(B[0], B[1]).scale(H.k).translate(
        typeof j == "function" ? -j.apply(this, arguments) : -j,
        typeof I == "function" ? -I.apply(this, arguments) : -I
      ), T, s);
    }, D, $);
  };
  function E(N, j) {
    return j = Math.max(i[0], Math.min(i[1], j)), j === N.k ? N : new ct(j, N.x, N.y);
  }
  function p(N, j, I) {
    var D = j[0] - I[0] * N.k, $ = j[1] - I[1] * N.k;
    return D === N.x && $ === N.y ? N : new ct(N.k, D, $);
  }
  function w(N) {
    return [(+N[0][0] + +N[1][0]) / 2, (+N[0][1] + +N[1][1]) / 2];
  }
  function b(N, j, I, D) {
    N.on("start.zoom", function() {
      C(this, arguments).event(D).start();
    }).on("interrupt.zoom end.zoom", function() {
      C(this, arguments).event(D).end();
    }).tween("zoom", function() {
      var $ = this, T = arguments, H = C($, T).event(D), B = t.apply($, T), F = I == null ? w(B) : typeof I == "function" ? I.apply($, T) : I, U = Math.max(B[1][0] - B[0][0], B[1][1] - B[0][1]), q = $.__zoom, ne = typeof j == "function" ? j.apply($, T) : j, ae = d(q.invert(F).concat(U / q.k), ne.invert(F).concat(U / ne.k));
      return function(K) {
        if (K === 1) K = ne;
        else {
          var L = ae(K), Z = U / L[2];
          K = new ct(Z, F[0] - L[0] * Z, F[1] - L[1] * Z);
        }
        H.zoom(null, K);
      };
    });
  }
  function C(N, j, I) {
    return !I && N.__zooming || new k(N, j);
  }
  function k(N, j) {
    this.that = N, this.args = j, this.active = 0, this.sourceEvent = null, this.extent = t.apply(N, j), this.taps = 0;
  }
  k.prototype = {
    event: function(N) {
      return N && (this.sourceEvent = N), this;
    },
    start: function() {
      return ++this.active === 1 && (this.that.__zooming = this, this.emit("start")), this;
    },
    zoom: function(N, j) {
      return this.mouse && N !== "mouse" && (this.mouse[1] = j.invert(this.mouse[0])), this.touch0 && N !== "touch" && (this.touch0[1] = j.invert(this.touch0[0])), this.touch1 && N !== "touch" && (this.touch1[1] = j.invert(this.touch1[0])), this.that.__zoom = j, this.emit("zoom"), this;
    },
    end: function() {
      return --this.active === 0 && (delete this.that.__zooming, this.emit("end")), this;
    },
    emit: function(N) {
      var j = Ve(this.that).datum();
      l.call(
        N,
        this.that,
        new np(N, {
          sourceEvent: this.sourceEvent,
          target: m,
          transform: this.that.__zoom,
          dispatch: l
        }),
        j
      );
    }
  };
  function A(N, ...j) {
    if (!e.apply(this, arguments)) return;
    var I = C(this, j).event(N), D = this.__zoom, $ = Math.max(i[0], Math.min(i[1], D.k * Math.pow(2, o.apply(this, arguments)))), T = Ye(N);
    if (I.wheel)
      (I.mouse[0][0] !== T[0] || I.mouse[0][1] !== T[1]) && (I.mouse[1] = D.invert(I.mouse[0] = T)), clearTimeout(I.wheel);
    else {
      if (D.k === $) return;
      I.mouse = [T, D.invert(T)], Do(this), I.start();
    }
    wn(N), I.wheel = setTimeout(H, y), I.zoom("mouse", n(p(E(D, $), I.mouse[0], I.mouse[1]), I.extent, s));
    function H() {
      I.wheel = null, I.end();
    }
  }
  function P(N, ...j) {
    if (h || !e.apply(this, arguments)) return;
    var I = N.currentTarget, D = C(this, j, !0).event(N), $ = Ve(N.view).on("mousemove.zoom", F, !0).on("mouseup.zoom", U, !0), T = Ye(N, I), H = N.clientX, B = N.clientY;
    Ka(N.view), Vr(N), D.mouse = [T, this.__zoom.invert(T)], Do(this), D.start();
    function F(q) {
      if (wn(q), !D.moved) {
        var ne = q.clientX - H, ae = q.clientY - B;
        D.moved = ne * ne + ae * ae > v;
      }
      D.event(q).zoom("mouse", n(p(D.that.__zoom, D.mouse[0] = Ye(q, I), D.mouse[1]), D.extent, s));
    }
    function U(q) {
      $.on("mousemove.zoom mouseup.zoom", null), Ga(q.view, D.moved), wn(q), D.event(q).end();
    }
  }
  function W(N, ...j) {
    if (e.apply(this, arguments)) {
      var I = this.__zoom, D = Ye(N.changedTouches ? N.changedTouches[0] : N, this), $ = I.invert(D), T = I.k * (N.shiftKey ? 0.5 : 2), H = n(p(E(I, T), D, $), t.apply(this, j), s);
      wn(N), c > 0 ? Ve(this).transition().duration(c).call(b, H, D, N) : Ve(this).call(m.transform, H, D, N);
    }
  }
  function M(N, ...j) {
    if (e.apply(this, arguments)) {
      var I = N.touches, D = I.length, $ = C(this, j, N.changedTouches.length === D).event(N), T, H, B, F;
      for (Vr(N), H = 0; H < D; ++H)
        B = I[H], F = Ye(B, this), F = [F, this.__zoom.invert(F), B.identifier], $.touch0 ? !$.touch1 && $.touch0[2] !== F[2] && ($.touch1 = F, $.taps = 0) : ($.touch0 = F, T = !0, $.taps = 1 + !!f);
      f && (f = clearTimeout(f)), T && ($.taps < 2 && (u = F[0], f = setTimeout(function() {
        f = null;
      }, g)), Do(this), $.start());
    }
  }
  function z(N, ...j) {
    if (this.__zooming) {
      var I = C(this, j).event(N), D = N.changedTouches, $ = D.length, T, H, B, F;
      for (wn(N), T = 0; T < $; ++T)
        H = D[T], B = Ye(H, this), I.touch0 && I.touch0[2] === H.identifier ? I.touch0[0] = B : I.touch1 && I.touch1[2] === H.identifier && (I.touch1[0] = B);
      if (H = I.that.__zoom, I.touch1) {
        var U = I.touch0[0], q = I.touch0[1], ne = I.touch1[0], ae = I.touch1[1], K = (K = ne[0] - U[0]) * K + (K = ne[1] - U[1]) * K, L = (L = ae[0] - q[0]) * L + (L = ae[1] - q[1]) * L;
        H = E(H, Math.sqrt(K / L)), B = [(U[0] + ne[0]) / 2, (U[1] + ne[1]) / 2], F = [(q[0] + ae[0]) / 2, (q[1] + ae[1]) / 2];
      } else if (I.touch0) B = I.touch0[0], F = I.touch0[1];
      else return;
      I.zoom("touch", n(p(H, B, F), I.extent, s));
    }
  }
  function O(N, ...j) {
    if (this.__zooming) {
      var I = C(this, j).event(N), D = N.changedTouches, $ = D.length, T, H;
      for (Vr(N), h && clearTimeout(h), h = setTimeout(function() {
        h = null;
      }, g), T = 0; T < $; ++T)
        H = D[T], I.touch0 && I.touch0[2] === H.identifier ? delete I.touch0 : I.touch1 && I.touch1[2] === H.identifier && delete I.touch1;
      if (I.touch1 && !I.touch0 && (I.touch0 = I.touch1, delete I.touch1), I.touch0) I.touch0[1] = this.__zoom.invert(I.touch0[0]);
      else if (I.end(), I.taps === 2 && (H = Ye(H, this), Math.hypot(u[0] - H[0], u[1] - H[1]) < x)) {
        var B = Ve(this).on("dblclick.zoom");
        B && B.apply(this, arguments);
      }
    }
  }
  return m.wheelDelta = function(N) {
    return arguments.length ? (o = typeof N == "function" ? N : bo(+N), m) : o;
  }, m.filter = function(N) {
    return arguments.length ? (e = typeof N == "function" ? N : bo(!!N), m) : e;
  }, m.touchable = function(N) {
    return arguments.length ? (r = typeof N == "function" ? N : bo(!!N), m) : r;
  }, m.extent = function(N) {
    return arguments.length ? (t = typeof N == "function" ? N : bo([[+N[0][0], +N[0][1]], [+N[1][0], +N[1][1]]]), m) : t;
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
const Oe = {
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
}, Tn = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], pc = ["Enter", " ", "Escape"], gc = {
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
var At;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(At || (At = {}));
var zn;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(zn || (zn = {}));
const yc = {
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
var Bo;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(Bo || (Bo = {}));
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
function mc(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const xc = (e) => "id" in e && "source" in e && "target" in e, cp = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), ji = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), Wn = (e, t = [0, 0]) => {
  const { width: n, height: o } = dt(e), r = e.origin ?? t, i = n * r[0], s = o * r[1];
  return {
    x: e.position.x - i,
    y: e.position.y - s
  };
}, lp = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const i = typeof r == "string";
    let s = !t.nodeLookup && !i ? r : void 0;
    t.nodeLookup && (s = i ? t.nodeLookup.get(r) : ji(r) ? r : t.nodeLookup.get(r.id));
    const c = s ? Wo(s, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return or(o, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return rr(n);
}, Xn = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = or(n, Wo(r)), o = !0);
  }), o ? rr(n) : { x: 0, y: 0, width: 0, height: 0 };
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
    const g = f.width ?? l.width ?? l.initialWidth ?? null, y = f.height ?? l.height ?? l.initialHeight ?? null, v = Ln(c, en(l)), x = (g ?? 0) * (y ?? 0), m = i && v > 0;
    (!l.internals.handleBounds || m || v >= x || l.dragging) && d.push(l);
  }
  return d;
}, dp = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function up(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function fp({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: i }, s) {
  if (e.size === 0)
    return !0;
  const c = up(e, s), d = Xn(c), l = Mi(d, t, n, s?.minZoom ?? r, s?.maxZoom ?? i, s?.padding ?? 0.1);
  return await o.setViewport(l, {
    duration: s?.duration,
    ease: s?.ease,
    interpolate: s?.interpolate
  }), !0;
}
function wc({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: i }) {
  const s = n.get(e), c = s.parentId ? n.get(s.parentId) : void 0, { x: d, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, f = s.origin ?? o;
  let u = s.extent || r;
  if (s.extent === "parent" && !s.expandParent)
    if (!c)
      i?.("005", Oe.error005());
    else {
      const g = c.measured.width, y = c.measured.height;
      g && y && (u = [
        [d, l],
        [d + g, l + y]
      ]);
    }
  else c && Tt(s.extent) && (u = [
    [s.extent[0][0] + d, s.extent[0][1] + l],
    [s.extent[1][0] + d, s.extent[1][1] + l]
  ]);
  const h = Tt(u) ? $t(t, u, s.measured) : t;
  return (s.measured.width === void 0 || s.measured.height === void 0) && i?.("015", Oe.error015()), {
    position: {
      x: h.x - d + (s.measured.width ?? 0) * f[0],
      y: h.y - l + (s.measured.height ?? 0) * f[1]
    },
    positionAbsolute: h
  };
}
async function hp({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const i = new Set(e.map((h) => h.id)), s = [];
  for (const h of n) {
    if (h.deletable === !1)
      continue;
    const g = i.has(h.id), y = !g && h.parentId && s.find((v) => v.id === h.parentId);
    (g || y) && s.push(h);
  }
  const c = new Set(t.map((h) => h.id)), d = o.filter((h) => h.deletable !== !1), f = dp(s, d);
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
const Qt = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), $t = (e = { x: 0, y: 0 }, t, n) => ({
  x: Qt(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: Qt(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function vc(e, t, n) {
  const { width: o, height: r } = dt(n), { x: i, y: s } = n.internals.positionAbsolute;
  return $t(e, [
    [i, s],
    [i + o, s + r]
  ], t);
}
const bs = (e, t, n) => e < t ? Qt(Math.abs(e - t), 1, t) / t : e > n ? -Qt(Math.abs(e - n), 1, t) / t : 0, Ai = (e, t, n = 15, o = 40) => {
  const r = bs(e.x, o, t.width - o) * n, i = bs(e.y, o, t.height - o) * n;
  return [r, i];
}, or = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), ai = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), rr = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), en = (e, t = [0, 0]) => {
  const { x: n, y: o } = ji(e) ? e.internals.positionAbsolute : Wn(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, Wo = (e, t = [0, 0]) => {
  const { x: n, y: o } = ji(e) ? e.internals.positionAbsolute : Wn(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, bc = (e, t) => rr(or(ai(e), ai(t))), Ln = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, Ns = (e) => Ze(e.width) && Ze(e.height) && Ze(e.x) && Ze(e.y), Ze = (e) => !isNaN(e) && isFinite(e), Nc = (e, t) => (n, o) => {
}, Yn = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), rn = ({ x: e, y: t }, [n, o, r], i = !1, s = [1, 1]) => {
  const c = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return i ? Yn(c, s) : c;
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
function pp(e, t, n) {
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
function gp(e, t, n, o, r, i) {
  const { x: s, y: c } = tn(e, [t, n, o]), { x: d, y: l } = tn({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), f = r - d, u = i - l;
  return {
    left: Math.floor(s),
    top: Math.floor(c),
    right: Math.floor(f),
    bottom: Math.floor(u)
  };
}
const Mi = (e, t, n, o, r, i) => {
  const s = pp(i, t, n), c = (t - s.x) / e.width, d = (n - s.y) / e.height, l = Math.min(c, d), f = Qt(l, o, r), u = e.x + e.width / 2, h = e.y + e.height / 2, g = t / 2 - u * f, y = n / 2 - h * f, v = gp(e, g, y, f, t, n), x = {
    left: Math.min(v.left - s.left, 0),
    top: Math.min(v.top - s.top, 0),
    right: Math.min(v.right - s.right, 0),
    bottom: Math.min(v.bottom - s.bottom, 0)
  };
  return {
    x: g - x.left + x.right,
    y: y - x.top + x.bottom,
    zoom: f
  };
}, Rn = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function Tt(e) {
  return e != null && e !== "parent";
}
function dt(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function Sc(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function Ec(e, t = { width: 0, height: 0 }, n, o, r) {
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
function yp() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function mp(e) {
  return { ...gc, ...e || {} };
}
function _n(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: i, y: s } = Ue(e), c = rn({ x: i - (r?.left ?? 0), y: s - (r?.top ?? 0) }, o), { x: d, y: l } = n ? Yn(c, t) : c;
  return {
    xSnapped: d,
    ySnapped: l,
    ...c
  };
}
const Di = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), Cc = (e) => e?.getRootNode?.() || window?.document, xp = ["INPUT", "SELECT", "TEXTAREA"];
function kc(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : xp.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const Ic = (e) => "clientX" in e, Ue = (e, t) => {
  const n = Ic(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
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
function jc({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: i, targetControlX: s, targetControlY: c }) {
  const d = e * 0.125 + r * 0.375 + s * 0.375 + n * 0.125, l = t * 0.125 + i * 0.375 + c * 0.375 + o * 0.125, f = Math.abs(d - e), u = Math.abs(l - t);
  return [d, l, f, u];
}
function No(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function Cs({ pos: e, x1: t, y1: n, x2: o, y2: r, c: i }) {
  switch (e) {
    case te.Left:
      return [t - No(t - o, i), n];
    case te.Right:
      return [t + No(o - t, i), n];
    case te.Top:
      return [t, n - No(n - r, i)];
    case te.Bottom:
      return [t, n + No(r - n, i)];
  }
}
function _c({ sourceX: e, sourceY: t, sourcePosition: n = te.Bottom, targetX: o, targetY: r, targetPosition: i = te.Top, curvature: s = 0.25 }) {
  const [c, d] = Cs({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: s
  }), [l, f] = Cs({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: s
  }), [u, h, g, y] = jc({
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
function Ac({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, i = n < e ? n + r : n - r, s = Math.abs(o - t) / 2, c = o < t ? o + s : o - s;
  return [i, c, r, s];
}
function wp({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: i = "basic" }) {
  if (i === "manual")
    return o;
  const s = r && n ? o + 1e3 : o, c = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return s + c;
}
function vp({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const i = or(Wo(e), Wo(t));
  i.x === i.x2 && (i.x2 += 1), i.y === i.y2 && (i.y2 += 1);
  const s = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return Ln(s, rr(i)) > 0;
}
const Mc = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, bp = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), Np = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Oe.error006()), t;
  const o = n.getEdgeId || Mc;
  let r;
  return xc(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, bp(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, Sp = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: r, ...i } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", Oe.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return o.onError?.("007", Oe.error007(r)), n;
  const c = o.getEdgeId || Mc, d = {
    ...i,
    id: o.shouldReplaceId ? c(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== r).concat(d);
};
function Dc({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, i, s, c] = Ac({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, i, s, c];
}
const ks = {
  [te.Left]: { x: -1, y: 0 },
  [te.Right]: { x: 1, y: 0 },
  [te.Top]: { x: 0, y: -1 },
  [te.Bottom]: { x: 0, y: 1 }
}, Ep = ({ source: e, sourcePosition: t = te.Bottom, target: n }) => t === te.Left || t === te.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, Is = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Cp({ source: e, sourcePosition: t = te.Bottom, target: n, targetPosition: o = te.Top, center: r, offset: i, stepPosition: s }) {
  const c = ks[t], d = ks[o], l = { x: e.x + c.x * i, y: e.y + c.y * i }, f = { x: n.x + d.x * i, y: n.y + d.y * i }, u = Ep({
    source: l,
    sourcePosition: t,
    target: f
  }), h = u.x !== 0 ? "x" : "y", g = u[h];
  let y = [], v, x;
  const m = { x: 0, y: 0 }, E = { x: 0, y: 0 }, [, , p, w] = Ac({
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
    ], P = [
      { x: l.x, y: x },
      { x: f.x, y: x }
    ];
    c[h] === g ? y = h === "x" ? A : P : y = h === "x" ? P : A;
  } else {
    const A = [{ x: l.x, y: f.y }], P = [{ x: f.x, y: l.y }];
    if (h === "x" ? y = c.x === g ? P : A : y = c.y === g ? A : P, t === o) {
      const N = Math.abs(e[h] - n[h]);
      if (N <= i) {
        const j = Math.min(i - 1, i - N);
        c[h] === g ? m[h] = (l[h] > e[h] ? -1 : 1) * j : E[h] = (f[h] > n[h] ? -1 : 1) * j;
      }
    }
    if (t !== o) {
      const N = h === "x" ? "y" : "x", j = c[h] === d[N], I = l[N] > f[N], D = l[N] < f[N];
      (c[h] === 1 && (!j && I || j && D) || c[h] !== 1 && (!j && D || j && I)) && (y = h === "x" ? A : P);
    }
    const W = { x: l.x + m.x, y: l.y + m.y }, M = { x: f.x + E.x, y: f.y + E.y }, z = Math.max(Math.abs(W.x - y[0].x), Math.abs(M.x - y[0].x)), O = Math.max(Math.abs(W.y - y[0].y), Math.abs(M.y - y[0].y));
    z >= O ? (v = (W.x + M.x) / 2, x = y[0].y) : (v = y[0].x, x = (W.y + M.y) / 2);
  }
  const b = { x: l.x + m.x, y: l.y + m.y }, C = { x: f.x + E.x, y: f.y + E.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...b.x !== y[0].x || b.y !== y[0].y ? [b] : [],
    ...y,
    ...C.x !== y[y.length - 1].x || C.y !== y[y.length - 1].y ? [C] : [],
    n
  ], v, x, p, w];
}
function kp(e, t, n, o) {
  const r = Math.min(Is(e, t) / 2, Is(t, n) / 2, o), { x: i, y: s } = t;
  if (e.x === i && i === n.x || e.y === s && s === n.y)
    return `L${i} ${s}`;
  if (e.y === s) {
    const l = e.x < n.x ? -1 : 1, f = e.y < n.y ? 1 : -1;
    return `L ${i + r * l},${s}Q ${i},${s} ${i},${s + r * f}`;
  }
  const c = e.x < n.x ? 1 : -1, d = e.y < n.y ? -1 : 1;
  return `L ${i},${s + r * d}Q ${i},${s} ${i + r * c},${s}`;
}
function Xo({ sourceX: e, sourceY: t, sourcePosition: n = te.Bottom, targetX: o, targetY: r, targetPosition: i = te.Top, borderRadius: s = 5, centerX: c, centerY: d, offset: l = 20, stepPosition: f = 0.5 }) {
  const [u, h, g, y, v] = Cp({
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
    x += kp(u[m - 1], u[m], u[m + 1], s);
  return x += `L${u[u.length - 1].x} ${u[u.length - 1].y}`, [x, h, g, y, v];
}
function js(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function Ip(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!js(t) || !js(n))
    return null;
  const o = t.internals.handleBounds || _s(t.handles), r = n.internals.handleBounds || _s(n.handles), i = As(o?.source ?? [], e.sourceHandle), s = As(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === Jt.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!i || !s)
    return e.onError?.("008", Oe.error008(i ? "target" : "source", {
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
function As(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function ci(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function jp(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const i = /* @__PURE__ */ new Set();
  return e.reduce((s, c) => ([c.markerStart || o, c.markerEnd || r].forEach((d) => {
    if (d && typeof d == "object") {
      const l = ci(d, t);
      i.has(l) || (s.push({ id: l, color: d.color || n, ...d }), i.add(l));
    }
  }), s), []).sort((s, c) => s.id.localeCompare(c.id));
}
const Pc = 1e3, _p = 10, Pi = {
  nodeOrigin: [0, 0],
  nodeExtent: Tn,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Ap = {
  ...Pi,
  checkEquality: !0
};
function $i(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function Mp(e, t, n) {
  const o = $i(Pi, n);
  for (const r of e.values())
    if (r.parentId)
      zi(r, e, t, o);
    else {
      const i = Wn(r, o.nodeOrigin), s = Tt(r.extent) ? r.extent : o.nodeExtent, c = $t(i, s, dt(r));
      r.internals.positionAbsolute = c;
    }
}
function Dp(e, t) {
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
function li(e, t, n, o = {}) {
  const r = $i(Ap, o), i = { i: 0 }, s = new Map(t), c = r?.elevateNodesOnSelect && !Ti(r.zIndexMode) ? Pc : 0;
  let d = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const f of e) {
    let u = s.get(f.id);
    if (r.checkEquality && f === u?.internals.userNode)
      t.set(f.id, u);
    else {
      const h = Wn(f, r.nodeOrigin), g = Tt(f.extent) ? f.extent : r.nodeExtent, y = $t(h, g, dt(f));
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
          handleBounds: Dp(f, u),
          z: $c(f, c, r.zIndexMode),
          userNode: f
        }
      }, t.set(f.id, u);
    }
    (u.measured === void 0 || u.measured.width === void 0 || u.measured.height === void 0) && !u.hidden && (d = !1), f.parentId && zi(u, t, n, o, i), l ||= f.selected ?? !1;
  }
  return { nodesInitialized: d, hasSelectedNodes: l };
}
function Pp(e, t) {
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
  Pp(e, n), r && !f.parentId && f.internals.rootParentIndex === void 0 && d === "auto" && (f.internals.rootParentIndex = ++r.i, f.internals.z = f.internals.z + r.i * _p), r && f.internals.rootParentIndex !== void 0 && (r.i = f.internals.rootParentIndex);
  const u = i && !Ti(d) ? Pc : 0, { x: h, y: g, z: y } = $p(e, f, s, c, u, d), { positionAbsolute: v } = e.internals, x = h !== v.x || g !== v.y;
  (x || y !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: x ? { x: h, y: g } : v,
      z: y
    }
  });
}
function $c(e, t, n) {
  const o = Ze(e.zIndex) ? e.zIndex : 0;
  return Ti(n) ? o : o + (e.selected ? t : 0);
}
function $p(e, t, n, o, r, i) {
  const { x: s, y: c } = t.internals.positionAbsolute, d = dt(e), l = Wn(e, n), f = Tt(e.extent) ? $t(l, e.extent, d) : l;
  let u = $t({ x: s + f.x, y: c + f.y }, o, d);
  e.extent === "parent" && (u = vc(u, d, t));
  const h = $c(e, r, i), g = t.internals.z ?? 0;
  return {
    x: u.x,
    y: u.y,
    z: g >= h ? g + 1 : h
  };
}
function Li(e, t, n, o = [0, 0]) {
  const r = [], i = /* @__PURE__ */ new Map();
  for (const s of e) {
    const c = t.get(s.parentId);
    if (!c)
      continue;
    const d = i.get(s.parentId)?.expandedRect ?? en(c), l = bc(d, s.rect);
    i.set(s.parentId, { expandedRect: l, parent: c });
  }
  return i.size > 0 && i.forEach(({ expandedRect: s, parent: c }, d) => {
    const l = c.internals.positionAbsolute, f = dt(c), u = c.origin ?? o, h = s.x < l.x ? Math.round(Math.abs(l.x - s.x)) : 0, g = s.y < l.y ? Math.round(Math.abs(l.y - s.y)) : 0, y = Math.max(f.width, Math.round(s.width)), v = Math.max(f.height, Math.round(s.height)), x = (y - f.width) * u[0], m = (v - f.height) * u[1];
    (h > 0 || g > 0 || x || m) && (r.push({
      id: d,
      type: "position",
      position: {
        x: c.position.x - h + x,
        y: c.position.y - g + m
      }
    }), n.get(d)?.forEach((E) => {
      e.some((p) => p.id === E.id) || r.push({
        id: E.id,
        type: "position",
        position: {
          x: E.position.x + h,
          y: E.position.y + g
        }
      });
    })), (f.width < s.width || f.height < s.height || h || g) && r.push({
      id: d,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: y + (h ? u[0] * h - x : 0),
        height: v + (g ? u[1] * g - m : 0)
      }
    });
  }), r;
}
function Tp(e, t, n, o, r, i, s) {
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
    const v = Di(g.nodeElement), x = y.measured.width !== v.width || y.measured.height !== v.height;
    if (!!(v.width && v.height && (x || !y.internals.handleBounds || g.force))) {
      const E = g.nodeElement.getBoundingClientRect(), p = Tt(y.extent) ? y.extent : i;
      let { positionAbsolute: w } = y.internals;
      y.parentId && y.extent === "parent" ? w = vc(w, v, t.get(y.parentId)) : p && (w = $t(w, p, v));
      const b = {
        ...y,
        measured: v,
        internals: {
          ...y.internals,
          positionAbsolute: w,
          handleBounds: {
            source: Es("source", g.nodeElement, E, u, y.id),
            target: Es("target", g.nodeElement, E, u, y.id)
          }
        }
      };
      t.set(y.id, b), y.parentId && zi(b, t, n, { nodeOrigin: r, zIndexMode: s }), d = !0, x && (l.push({
        id: y.id,
        type: "dimensions",
        dimensions: v
      }), y.expandParent && y.parentId && h.push({
        id: y.id,
        parentId: y.parentId,
        rect: en(b, r)
      }));
    }
  }
  if (h.length > 0) {
    const g = Li(h, t, n, r);
    l.push(...g);
  }
  return { changes: l, updatedInternals: d };
}
async function zp({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: i }) {
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
function Tc(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: i, sourceHandle: s = null, targetHandle: c = null } = o, d = { edgeId: o.id, source: r, target: i, sourceHandle: s, targetHandle: c }, l = `${r}-${s}--${i}-${c}`, f = `${i}-${c}--${r}-${s}`;
    Ms("source", d, f, e, r, s), Ms("target", d, l, e, i, c), t.set(o.id, o);
  }
}
function zc(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : zc(n, t) : !1;
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
function Lp(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [i, s] of e)
    if ((s.selected || s.id === o) && (!s.parentId || !zc(s, e)) && (s.draggable || t && typeof s.draggable > "u")) {
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
function Hr({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
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
function Rp({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const i = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, s = Yn(i, t);
  return {
    x: s.x - i.x,
    y: s.y - i.y
  };
}
function Vp({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let i = { x: null, y: null }, s = 0, c = /* @__PURE__ */ new Map(), d = !1, l = { x: 0, y: 0 }, f = null, u = !1, h = null, g = !1, y = !1, v = null;
  function x({ noDragClassName: E, handleSelector: p, domNode: w, isSelectable: b, nodeId: C, nodeClickDistance: k = 0 }) {
    h = Ve(w);
    function A({ x: z, y: O }) {
      const { nodeLookup: N, nodeExtent: j, snapGrid: I, snapToGrid: D, nodeOrigin: $, onNodeDrag: T, onSelectionDrag: H, onError: B, updateNodePositions: F } = t();
      i = { x: z, y: O };
      let U = !1;
      const q = c.size > 1, ne = q && j ? ai(Xn(c)) : null, ae = q && D ? Rp({
        dragItems: c,
        snapGrid: I,
        x: z,
        y: O
      }) : null;
      for (const [K, L] of c) {
        if (!N.has(K))
          continue;
        let Z = { x: z - L.distance.x, y: O - L.distance.y };
        D && (Z = ae ? {
          x: Math.round(Z.x + ae.x),
          y: Math.round(Z.y + ae.y)
        } : Yn(Z, I));
        let ie = null;
        if (q && j && !L.extent && ne) {
          const { positionAbsolute: ee } = L.internals, le = ee.x - ne.x + j[0][0], V = ee.x + L.measured.width - ne.x2 + j[1][0], J = ee.y - ne.y + j[0][1], fe = ee.y + L.measured.height - ne.y2 + j[1][1];
          ie = [
            [le, J],
            [V, fe]
          ];
        }
        const { position: se, positionAbsolute: G } = wc({
          nodeId: K,
          nextPosition: Z,
          nodeLookup: N,
          nodeExtent: ie || j,
          nodeOrigin: $,
          onError: B
        });
        U = U || L.position.x !== se.x || L.position.y !== se.y, L.position = se, L.internals.positionAbsolute = G;
      }
      if (y = y || U, !!U && (F(c, !0), v && (o || T || !C && H))) {
        const [K, L] = Hr({
          nodeId: C,
          dragItems: c,
          nodeLookup: N
        });
        o?.(v, c, K, L), T?.(v, K, L), C || H?.(v, L);
      }
    }
    async function P() {
      if (!f)
        return;
      const { transform: z, panBy: O, autoPanSpeed: N, autoPanOnNodeDrag: j } = t();
      if (!j) {
        d = !1, cancelAnimationFrame(s);
        return;
      }
      const [I, D] = Ai(l, f, N);
      (I !== 0 || D !== 0) && (i.x = (i.x ?? 0) - I / z[2], i.y = (i.y ?? 0) - D / z[2], await O({ x: I, y: D }) && A(i)), s = requestAnimationFrame(P);
    }
    function W(z) {
      const { nodeLookup: O, multiSelectionActive: N, nodesDraggable: j, transform: I, snapGrid: D, snapToGrid: $, selectNodesOnDrag: T, onNodeDragStart: H, onSelectionDragStart: B, unselectNodesAndEdges: F } = t();
      u = !0, (!T || !b) && !N && C && (O.get(C)?.selected || F()), b && T && C && e?.(C);
      const U = _n(z.sourceEvent, { transform: I, snapGrid: D, snapToGrid: $, containerBounds: f });
      if (i = U, c = Lp(O, j, U, C), c.size > 0 && (n || H || !C && B)) {
        const [q, ne] = Hr({
          nodeId: C,
          dragItems: c,
          nodeLookup: O
        });
        n?.(z.sourceEvent, c, q, ne), H?.(z.sourceEvent, q, ne), C || B?.(z.sourceEvent, ne);
      }
    }
    const M = Ja().clickDistance(k).on("start", (z) => {
      const { domNode: O, nodeDragThreshold: N, transform: j, snapGrid: I, snapToGrid: D } = t();
      f = O?.getBoundingClientRect() || null, g = !1, y = !1, v = z.sourceEvent, N === 0 && W(z), i = _n(z.sourceEvent, { transform: j, snapGrid: I, snapToGrid: D, containerBounds: f }), l = Ue(z.sourceEvent, f);
    }).on("drag", (z) => {
      const { autoPanOnNodeDrag: O, transform: N, snapGrid: j, snapToGrid: I, nodeDragThreshold: D, nodeLookup: $ } = t(), T = _n(z.sourceEvent, { transform: N, snapGrid: j, snapToGrid: I, containerBounds: f });
      if (v = z.sourceEvent, (z.sourceEvent.type === "touchmove" && z.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      C && !$.has(C)) && (g = !0), !g) {
        if (!d && O && u && (d = !0, P()), !u) {
          const H = Ue(z.sourceEvent, f), B = H.x - l.x, F = H.y - l.y;
          Math.sqrt(B * B + F * F) > D && W(z);
        }
        (i.x !== T.xSnapped || i.y !== T.ySnapped) && c && u && (l = Ue(z.sourceEvent, f), A(T));
      }
    }).on("end", (z) => {
      if (!u || g) {
        g && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (d = !1, u = !1, cancelAnimationFrame(s), c.size > 0) {
        const { nodeLookup: O, updateNodePositions: N, onNodeDragStop: j, onSelectionDragStop: I } = t();
        if (y && (N(c, !1), y = !1), r || j || !C && I) {
          const [D, $] = Hr({
            nodeId: C,
            dragItems: c,
            nodeLookup: O,
            dragging: !1
          });
          r?.(z.sourceEvent, c, D, $), j?.(z.sourceEvent, D, $), C || I?.(z.sourceEvent, $);
        }
      }
    }).filter((z) => {
      const O = z.target;
      return !z.button && (!E || !Ds(O, `.${E}`, w)) && (!p || Ds(O, p, w));
    });
    h.call(M);
  }
  function m() {
    h?.on(".drag", null);
  }
  return {
    update: x,
    destroy: m
  };
}
function Hp(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const i of t.values())
    Ln(r, en(i)) > 0 && o.push(i);
  return o;
}
const Op = 250;
function Fp(e, t, n, o) {
  let r = [], i = 1 / 0;
  const s = Hp(e, n, t + Op);
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
function Lc(e, t, n, o, r, i = !1) {
  const s = o.get(e);
  if (!s)
    return null;
  const c = r === "strict" ? s.internals.handleBounds?.[t] : [...s.internals.handleBounds?.source ?? [], ...s.internals.handleBounds?.target ?? []], d = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return d && i ? { ...d, ...zt(s, d, d.position, !0) } : d;
}
function Rc(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Bp(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const Vc = () => !0;
function Wp(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: i, isTarget: s, domNode: c, nodeLookup: d, lib: l, autoPanOnConnect: f, flowId: u, panBy: h, cancelConnection: g, onConnectStart: y, onConnect: v, onConnectEnd: x, isValidConnection: m = Vc, onReconnectEnd: E, updateConnection: p, getTransform: w, getFromHandle: b, autoPanSpeed: C, dragThreshold: k = 1, handleDomNode: A }) {
  const P = Cc(e.target);
  let W = 0, M;
  const { x: z, y: O } = Ue(e), N = Rc(i, A), j = c?.getBoundingClientRect();
  let I = !1;
  if (!j || !N)
    return;
  const D = Lc(r, N, o, d, t);
  if (!D)
    return;
  let $ = Ue(e, j), T = !1, H = null, B = !1, F = null;
  function U() {
    if (!f || !j)
      return;
    const [se, G] = Ai($, j, C);
    h({ x: se, y: G }), W = requestAnimationFrame(U);
  }
  const q = {
    ...D,
    nodeId: r,
    type: N,
    position: D.position
  }, ne = d.get(r);
  let K = {
    inProgress: !0,
    isValid: null,
    from: zt(ne, q, te.Left, !0),
    fromHandle: q,
    fromPosition: q.position,
    fromNode: ne,
    to: $,
    toHandle: null,
    toPosition: vs[q.position],
    toNode: null,
    pointer: $
  };
  function L() {
    I = !0, p(K), y?.(e, { nodeId: r, handleId: o, handleType: N });
  }
  k === 0 && L();
  function Z(se) {
    if (!I) {
      const { x: fe, y: me } = Ue(se), Ee = fe - z, De = me - O;
      if (!(Ee * Ee + De * De > k * k))
        return;
      L();
    }
    if (!b() || !q) {
      ie(se);
      return;
    }
    const G = w();
    $ = Ue(se, j), M = Fp(rn($, G, !1, [1, 1]), n, d, q), T || (U(), T = !0);
    const ee = Hc(se, {
      handle: M,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: s ? "target" : "source",
      isValidConnection: m,
      doc: P,
      lib: l,
      flowId: u,
      nodeLookup: d
    });
    F = ee.handleDomNode, H = ee.connection, B = Bp(!!M, ee.isValid);
    const le = d.get(r), V = le ? zt(le, q, te.Left, !0) : K.from, J = {
      ...K,
      from: V,
      isValid: B,
      to: ee.toHandle && B ? tn({ x: ee.toHandle.x, y: ee.toHandle.y }, G) : $,
      toHandle: ee.toHandle,
      toPosition: B && ee.toHandle ? ee.toHandle.position : vs[q.position],
      toNode: ee.toHandle ? d.get(ee.toHandle.nodeId) : null,
      pointer: $
    };
    p(J), K = J;
  }
  function ie(se) {
    if (!("touches" in se && se.touches.length > 0)) {
      if (I) {
        (M || F) && H && B && v?.(H);
        const { inProgress: G, ...ee } = K, le = {
          ...ee,
          toPosition: K.toHandle ? K.toPosition : null
        };
        x?.(se, le), i && E?.(se, le);
      }
      g(), cancelAnimationFrame(W), T = !1, B = !1, H = null, F = null, P.removeEventListener("mousemove", Z), P.removeEventListener("mouseup", ie), P.removeEventListener("touchmove", Z), P.removeEventListener("touchend", ie);
    }
  }
  P.addEventListener("mousemove", Z), P.addEventListener("mouseup", ie), P.addEventListener("touchmove", Z), P.addEventListener("touchend", ie);
}
function Hc(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: i, doc: s, lib: c, flowId: d, isValidConnection: l = Vc, nodeLookup: f }) {
  const u = i === "target", h = t ? s.querySelector(`.${c}-flow__handle[data-id="${d}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: g, y } = Ue(e), v = s.elementFromPoint(g, y), x = v?.classList.contains(`${c}-flow__handle`) ? v : h, m = {
    handleDomNode: x,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (x) {
    const E = Rc(void 0, x), p = x.getAttribute("data-nodeid"), w = x.getAttribute("data-handleid"), b = x.classList.contains("connectable"), C = x.classList.contains("connectableend");
    if (!p || !E)
      return m;
    const k = {
      source: u ? p : o,
      sourceHandle: u ? w : r,
      target: u ? o : p,
      targetHandle: u ? r : w
    };
    m.connection = k;
    const P = b && C && (n === Jt.Strict ? u && E === "source" || !u && E === "target" : p !== o || w !== r);
    m.isValid = P && l(k), m.toHandle = Lc(p, E, w, f, n, !0);
  }
  return m;
}
const di = {
  onPointerDown: Wp,
  isValid: Hc
};
function Xp({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = Ve(e);
  function i({ translateExtent: c, width: d, height: l, zoomStep: f = 1, pannable: u = !0, zoomable: h = !0, inversePan: g = !1 }) {
    const y = (p) => {
      if (p.sourceEvent.type !== "wheel" || !t)
        return;
      const w = n(), b = p.sourceEvent.ctrlKey && Rn() ? 10 : 1, C = -p.sourceEvent.deltaY * (p.sourceEvent.deltaMode === 1 ? 0.05 : p.sourceEvent.deltaMode ? 1 : 2e-3) * f, k = w[2] * Math.pow(2, C * b);
      t.scaleTo(k);
    };
    let v = [0, 0];
    const x = (p) => {
      (p.sourceEvent.type === "mousedown" || p.sourceEvent.type === "touchstart") && (v = [
        p.sourceEvent.clientX ?? p.sourceEvent.touches[0].clientX,
        p.sourceEvent.clientY ?? p.sourceEvent.touches[0].clientY
      ]);
    }, m = (p) => {
      const w = n();
      if (p.sourceEvent.type !== "mousemove" && p.sourceEvent.type !== "touchmove" || !t)
        return;
      const b = [
        p.sourceEvent.clientX ?? p.sourceEvent.touches[0].clientX,
        p.sourceEvent.clientY ?? p.sourceEvent.touches[0].clientY
      ], C = [b[0] - v[0], b[1] - v[1]];
      v = b;
      const k = o() * Math.max(w[2], Math.log(w[2])) * (g ? -1 : 1), A = {
        x: w[0] - C[0] * k,
        y: w[1] - C[1] * k
      }, P = [
        [0, 0],
        [d, l]
      ];
      t.setViewportConstrained({
        x: A.x,
        y: A.y,
        zoom: w[2]
      }, P, c);
    }, E = hc().on("start", x).on("zoom", u ? m : null).on("zoom.wheel", h ? y : null);
    r.call(E, {});
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
const ir = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), Or = ({ x: e, y: t, zoom: n }) => nr.translate(e, t).scale(n), Xt = (e, t) => e.target.closest(`.${t}`), Oc = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Yp = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, Fr = (e, t = 0, n = Yp, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, Fc = (e) => {
  const t = e.ctrlKey && Rn() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function qp({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: i, zoomOnPinch: s, onPanZoomStart: c, onPanZoom: d, onPanZoomEnd: l }) {
  return (f) => {
    if (Xt(f, t))
      return f.ctrlKey && f.preventDefault(), !1;
    f.preventDefault(), f.stopImmediatePropagation();
    const u = n.property("__zoom").k || 1;
    if (f.ctrlKey && s) {
      const x = Ye(f), m = Fc(f), E = u * Math.pow(2, m);
      o.scaleTo(n, E, x, f);
      return;
    }
    const h = f.deltaMode === 1 ? 20 : 1;
    let g = r === At.Vertical ? 0 : f.deltaX * h, y = r === At.Horizontal ? 0 : f.deltaY * h;
    !Rn() && f.shiftKey && r !== At.Vertical && (g = f.deltaY * h, y = 0), o.translateBy(
      n,
      -(g / u) * i,
      -(y / u) * i,
      // @ts-ignore
      { internal: !0 }
    );
    const v = ir(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (d?.(f, v), e.panScrollTimeout = setTimeout(() => {
      l?.(f, v), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(f, v));
  };
}
function Zp({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const i = o.type === "wheel", s = !t && i && !o.ctrlKey, c = Xt(o, e);
    if (o.ctrlKey && i && c && o.preventDefault(), s || c)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function Up({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = ir(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function Kp({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (i) => {
    e.usedRightMouseButton = !!(n && Oc(t, e.mouseButton ?? 0)), i.sourceEvent?.sync || o([i.transform.x, i.transform.y, i.transform.k]), r && !i.sourceEvent?.internal && r?.(i.sourceEvent, ir(i.transform));
  };
}
function Gp({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: i }) {
  return (s) => {
    if (!s.sourceEvent?.internal && (e.isZoomingOrPanning = !1, i && Oc(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && s.sourceEvent && i(s.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
      const c = ir(s.transform);
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
function Jp({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: i, userSelectionActive: s, noWheelClassName: c, noPanClassName: d, lib: l, connectionInProgress: f }) {
  return (u) => {
    const h = e || t, g = n && u.ctrlKey, y = u.type === "wheel";
    if (u.button === 1 && u.type === "mousedown" && (Xt(u, `${l}-flow__node`) || Xt(u, `${l}-flow__edge`)))
      return !0;
    if (!o && !h && !r && !i && !n || s || f && !y || Xt(u, c) && y || Xt(u, d) && (!y || r && y && !e) || !n && u.ctrlKey && y)
      return !1;
    if (!n && u.type === "touchstart" && u.touches?.length > 1)
      return u.preventDefault(), !1;
    if (!h && !r && !g && y || !o && (u.type === "mousedown" || u.type === "touchstart") || Array.isArray(o) && !o.includes(u.button) && u.type === "mousedown")
      return !1;
    const v = Array.isArray(o) && o.includes(u.button) || !u.button || u.button <= 1;
    return (!u.ctrlKey || y) && v;
  };
}
function Qp({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: i, onPanZoomStart: s, onPanZoomEnd: c, onDraggingChange: d }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, f = e.getBoundingClientRect(), u = hc().scaleExtent([t, n]).translateExtent(o), h = Ve(e).call(u);
  E({
    x: r.x,
    y: r.y,
    zoom: Qt(r.zoom, t, n)
  }, [
    [0, 0],
    [f.width, f.height]
  ], o);
  const g = h.on("wheel.zoom"), y = h.on("dblclick.zoom");
  u.wheelDelta(Fc);
  async function v(M, z) {
    return h ? new Promise((O) => {
      u?.interpolate(z?.interpolate === "linear" ? jn : _o).transform(Fr(h, z?.duration, z?.ease, () => O(!0)), M);
    }) : !1;
  }
  function x({ noWheelClassName: M, noPanClassName: z, onPaneContextMenu: O, userSelectionActive: N, panOnScroll: j, panOnDrag: I, panOnScrollMode: D, panOnScrollSpeed: $, preventScrolling: T, zoomOnPinch: H, zoomOnScroll: B, zoomOnDoubleClick: F, zoomActivationKeyPressed: U, lib: q, onTransformChange: ne, connectionInProgress: ae, paneClickDistance: K, selectionOnDrag: L }) {
    N && !l.isZoomingOrPanning && m();
    const Z = j && !U && !N;
    u.clickDistance(L ? 1 / 0 : !Ze(K) || K < 0 ? 0 : K);
    const ie = Z ? qp({
      zoomPanValues: l,
      noWheelClassName: M,
      d3Selection: h,
      d3Zoom: u,
      panOnScrollMode: D,
      panOnScrollSpeed: $,
      zoomOnPinch: H,
      onPanZoomStart: s,
      onPanZoom: i,
      onPanZoomEnd: c
    }) : Zp({
      noWheelClassName: M,
      preventScrolling: T,
      d3ZoomHandler: g
    });
    h.on("wheel.zoom", ie, { passive: !1 });
    const se = Up({
      zoomPanValues: l,
      onDraggingChange: d,
      onPanZoomStart: s
    });
    u.on("start", se);
    const G = Kp({
      zoomPanValues: l,
      panOnDrag: I,
      onPaneContextMenu: !!O,
      onPanZoom: i,
      onTransformChange: ne
    });
    u.on("zoom", G);
    const ee = Gp({
      zoomPanValues: l,
      panOnDrag: I,
      panOnScroll: j,
      onPaneContextMenu: O,
      onPanZoomEnd: c,
      onDraggingChange: d
    });
    u.on("end", ee);
    const le = Jp({
      zoomActivationKeyPressed: U,
      panOnDrag: I,
      zoomOnScroll: B,
      panOnScroll: j,
      zoomOnDoubleClick: F,
      zoomOnPinch: H,
      userSelectionActive: N,
      noPanClassName: z,
      noWheelClassName: M,
      lib: q,
      connectionInProgress: ae
    });
    u.filter(le), F ? h.on("dblclick.zoom", y) : h.on("dblclick.zoom", null);
  }
  function m() {
    u.on("zoom", null);
  }
  async function E(M, z, O) {
    const N = Or(M), j = u?.constrain()(N, z, O);
    return j && await v(j), j;
  }
  async function p(M, z) {
    const O = Or(M);
    return await v(O, z), O;
  }
  function w(M) {
    if (h) {
      const z = Or(M), O = h.property("__zoom");
      (O.k !== M.zoom || O.x !== M.x || O.y !== M.y) && u?.transform(h, z, null, { sync: !0 });
    }
  }
  function b() {
    const M = h ? fc(h.node()) : { x: 0, y: 0, k: 1 };
    return { x: M.x, y: M.y, zoom: M.k };
  }
  async function C(M, z) {
    return h ? new Promise((O) => {
      u?.interpolate(z?.interpolate === "linear" ? jn : _o).scaleTo(Fr(h, z?.duration, z?.ease, () => O(!0)), M);
    }) : !1;
  }
  async function k(M, z) {
    return h ? new Promise((O) => {
      u?.interpolate(z?.interpolate === "linear" ? jn : _o).scaleBy(Fr(h, z?.duration, z?.ease, () => O(!0)), M);
    }) : !1;
  }
  function A(M) {
    u?.scaleExtent(M);
  }
  function P(M) {
    u?.translateExtent(M);
  }
  function W(M) {
    const z = !Ze(M) || M < 0 ? 0 : M;
    u?.clickDistance(z);
  }
  return {
    update: x,
    destroy: m,
    setViewport: p,
    setViewportConstrained: E,
    getViewport: b,
    scaleTo: C,
    scaleBy: k,
    setScaleExtent: A,
    setTranslateExtent: P,
    syncViewport: w,
    setClickDistance: W
  };
}
var nn;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(nn || (nn = {}));
function eg({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: i }) {
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
function So(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function $s(e, t) {
  return e ? !t : t;
}
function tg(e, t, n, o, r, i, s, c) {
  let { affectsX: d, affectsY: l } = t;
  const { isHorizontal: f, isVertical: u } = t, h = f && u, { xSnapped: g, ySnapped: y } = n, { minWidth: v, maxWidth: x, minHeight: m, maxHeight: E } = o, { x: p, y: w, width: b, height: C, aspectRatio: k } = e;
  let A = Math.floor(f ? g - e.pointerX : 0), P = Math.floor(u ? y - e.pointerY : 0);
  const W = b + (d ? -A : A), M = C + (l ? -P : P), z = -i[0] * b, O = -i[1] * C;
  let N = So(W, v, x), j = So(M, m, E);
  if (s) {
    let $ = 0, T = 0;
    d && A < 0 ? $ = ht(p + A + z, s[0][0]) : !d && A > 0 && ($ = pt(p + W + z, s[1][0])), l && P < 0 ? T = ht(w + P + O, s[0][1]) : !l && P > 0 && (T = pt(w + M + O, s[1][1])), N = Math.max(N, $), j = Math.max(j, T);
  }
  if (c) {
    let $ = 0, T = 0;
    d && A > 0 ? $ = pt(p + A, c[0][0]) : !d && A < 0 && ($ = ht(p + W, c[1][0])), l && P > 0 ? T = pt(w + P, c[0][1]) : !l && P < 0 && (T = ht(w + M, c[1][1])), N = Math.max(N, $), j = Math.max(j, T);
  }
  if (r) {
    if (f) {
      const $ = So(W / k, m, E) * k;
      if (N = Math.max(N, $), s) {
        let T = 0;
        !d && !l || d && !l && h ? T = pt(w + O + W / k, s[1][1]) * k : T = ht(w + O + (d ? A : -A) / k, s[0][1]) * k, N = Math.max(N, T);
      }
      if (c) {
        let T = 0;
        !d && !l || d && !l && h ? T = ht(w + W / k, c[1][1]) * k : T = pt(w + (d ? A : -A) / k, c[0][1]) * k, N = Math.max(N, T);
      }
    }
    if (u) {
      const $ = So(M * k, v, x) / k;
      if (j = Math.max(j, $), s) {
        let T = 0;
        !d && !l || l && !d && h ? T = pt(p + M * k + z, s[1][0]) / k : T = ht(p + (l ? P : -P) * k + z, s[0][0]) / k, j = Math.max(j, T);
      }
      if (c) {
        let T = 0;
        !d && !l || l && !d && h ? T = ht(p + M * k, c[1][0]) / k : T = pt(p + (l ? P : -P) * k, c[0][0]) / k, j = Math.max(j, T);
      }
    }
  }
  P = P + (P < 0 ? j : -j), A = A + (A < 0 ? N : -N), r && (h ? W > M * k ? P = ($s(d, l) ? -A : A) / k : A = ($s(d, l) ? -P : P) * k : f ? (P = A / k, l = d) : (A = P * k, d = l));
  const I = d ? p + A : p, D = l ? w + P : w;
  return {
    width: b + (d ? -A : A),
    height: C + (l ? -P : P),
    x: i[0] * A * (d ? -1 : 1) + I,
    y: i[1] * P * (l ? -1 : 1) + D
  };
}
const Bc = { width: 0, height: 0, x: 0, y: 0 }, ng = {
  ...Bc,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function og(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, i = e.measured.width ?? 0, s = e.measured.height ?? 0, c = n[0] * i, d = n[1] * s;
  return [
    [o - c, r - d],
    [o + i - c, r + s - d]
  ];
}
function rg({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const i = Ve(e);
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
  function c({ controlPosition: l, boundaries: f, keepAspectRatio: u, resizeDirection: h, onResizeStart: g, onResize: y, onResizeEnd: v, shouldResize: x }) {
    let m = { ...Bc }, E = { ...ng };
    s = {
      boundaries: f,
      resizeDirection: h,
      keepAspectRatio: u,
      controlDirection: Ps(l)
    };
    let p, w = null, b = [], C, k, A, P = !1;
    const W = Ja().on("start", (M) => {
      const { nodeLookup: z, transform: O, snapGrid: N, snapToGrid: j, nodeOrigin: I, paneDomNode: D } = n();
      if (p = z.get(t), !p)
        return;
      w = D?.getBoundingClientRect() ?? null;
      const { xSnapped: $, ySnapped: T } = _n(M.sourceEvent, {
        transform: O,
        snapGrid: N,
        snapToGrid: j,
        containerBounds: w
      });
      m = {
        width: p.measured.width ?? 0,
        height: p.measured.height ?? 0,
        x: p.position.x ?? 0,
        y: p.position.y ?? 0
      }, E = {
        ...m,
        pointerX: $,
        pointerY: T,
        aspectRatio: m.width / m.height
      }, C = void 0, k = Tt(p.extent) ? p.extent : void 0, p.parentId && (p.extent === "parent" || p.expandParent) && (C = z.get(p.parentId)), C && p.extent === "parent" && (k = [
        [0, 0],
        [C.measured.width, C.measured.height]
      ]), b = [], A = void 0;
      for (const [H, B] of z)
        if (B.parentId === t && (b.push({
          id: H,
          position: { ...B.position },
          extent: B.extent
        }), B.extent === "parent" || B.expandParent)) {
          const F = og(B, p, B.origin ?? I);
          A ? A = [
            [Math.min(F[0][0], A[0][0]), Math.min(F[0][1], A[0][1])],
            [Math.max(F[1][0], A[1][0]), Math.max(F[1][1], A[1][1])]
          ] : A = F;
        }
      g?.(M, { ...m });
    }).on("drag", (M) => {
      const { transform: z, snapGrid: O, snapToGrid: N, nodeOrigin: j } = n(), I = _n(M.sourceEvent, {
        transform: z,
        snapGrid: O,
        snapToGrid: N,
        containerBounds: w
      }), D = [];
      if (!p)
        return;
      const { x: $, y: T, width: H, height: B } = m, F = {}, U = p.origin ?? j, { width: q, height: ne, x: ae, y: K } = tg(E, s.controlDirection, I, s.boundaries, s.keepAspectRatio, U, k, A), L = q !== H, Z = ne !== B, ie = ae !== $ && L, se = K !== T && Z;
      if (!ie && !se && !L && !Z)
        return;
      if ((ie || se || U[0] === 1 || U[1] === 1) && (F.x = ie ? ae : m.x, F.y = se ? K : m.y, m.x = F.x, m.y = F.y, b.length > 0)) {
        const V = ae - $, J = K - T;
        for (const fe of b)
          fe.position = {
            x: fe.position.x - V + U[0] * (q - H),
            y: fe.position.y - J + U[1] * (ne - B)
          }, D.push(fe);
      }
      if ((L || Z) && (F.width = L && (!s.resizeDirection || s.resizeDirection === "horizontal") ? q : m.width, F.height = Z && (!s.resizeDirection || s.resizeDirection === "vertical") ? ne : m.height, m.width = F.width, m.height = F.height), C && p.expandParent) {
        const V = U[0] * (F.width ?? 0);
        F.x && F.x < V && (m.x = V, E.x = E.x - (F.x - V));
        const J = U[1] * (F.height ?? 0);
        F.y && F.y < J && (m.y = J, E.y = E.y - (F.y - J));
      }
      const G = eg({
        width: m.width,
        prevWidth: H,
        height: m.height,
        prevHeight: B,
        affectsX: s.controlDirection.affectsX,
        affectsY: s.controlDirection.affectsY
      }), ee = { ...m, direction: G };
      x?.(M, ee) !== !1 && (P = !0, y?.(M, ee), o(F, D));
    }).on("end", (M) => {
      P && (v?.(M, { ...m }), r?.({ ...m }), P = !1);
    });
    i.call(W);
  }
  function d() {
    i.on(".drag", null);
  }
  return {
    update: c,
    destroy: d
  };
}
var Br = { exports: {} }, Wr = {}, Xr = { exports: {} }, Yr = {};
var Ts;
function ig() {
  if (Ts) return Yr;
  Ts = 1;
  var e = wt;
  function t(u, h) {
    return u === h && (u !== 0 || 1 / u === 1 / h) || u !== u && h !== h;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, i = e.useLayoutEffect, s = e.useDebugValue;
  function c(u, h) {
    var g = h(), y = o({ inst: { value: g, getSnapshot: h } }), v = y[0].inst, x = y[1];
    return i(
      function() {
        v.value = g, v.getSnapshot = h, d(v) && x({ inst: v });
      },
      [u, g, h]
    ), r(
      function() {
        return d(v) && x({ inst: v }), u(function() {
          d(v) && x({ inst: v });
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
  return Yr.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : f, Yr;
}
var zs;
function sg() {
  return zs || (zs = 1, Xr.exports = ig()), Xr.exports;
}
var Ls;
function ag() {
  if (Ls) return Wr;
  Ls = 1;
  var e = wt, t = sg();
  function n(l, f) {
    return l === f && (l !== 0 || 1 / l === 1 / f) || l !== l && f !== f;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, i = e.useRef, s = e.useEffect, c = e.useMemo, d = e.useDebugValue;
  return Wr.useSyncExternalStoreWithSelector = function(l, f, u, h, g) {
    var y = i(null);
    if (y.current === null) {
      var v = { hasValue: !1, value: null };
      y.current = v;
    } else v = y.current;
    y = c(
      function() {
        function m(C) {
          if (!E) {
            if (E = !0, p = C, C = h(C), g !== void 0 && v.hasValue) {
              var k = v.value;
              if (g(k, C))
                return w = k;
            }
            return w = C;
          }
          if (k = w, o(p, C)) return k;
          var A = h(C);
          return g !== void 0 && g(k, A) ? (p = C, k) : (p = C, w = A);
        }
        var E = !1, p, w, b = u === void 0 ? null : u;
        return [
          function() {
            return m(f());
          },
          b === null ? void 0 : function() {
            return m(b());
          }
        ];
      },
      [f, u, h, g]
    );
    var x = r(l, y[0], y[1]);
    return s(
      function() {
        v.hasValue = !0, v.value = x;
      },
      [x]
    ), d(x), x;
  }, Wr;
}
var Rs;
function cg() {
  return Rs || (Rs = 1, Br.exports = ag()), Br.exports;
}
var lg = cg();
const dg = /* @__PURE__ */ Cd(lg), ug = {}, Vs = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (f, u) => {
    const h = typeof f == "function" ? f(t) : f;
    if (!Object.is(h, t)) {
      const g = t;
      t = u ?? (typeof h != "object" || h === null) ? h : Object.assign({}, t, h), n.forEach((y) => y(t, g));
    }
  }, r = () => t, d = { setState: o, getState: r, getInitialState: () => l, subscribe: (f) => (n.add(f), () => n.delete(f)), destroy: () => {
    (ug ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(o, r, d);
  return d;
}, fg = (e) => e ? Vs(e) : Vs, { useDebugValue: hg } = wt, { useSyncExternalStoreWithSelector: pg } = dg, gg = (e) => e;
function Wc(e, t = gg, n) {
  const o = pg(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return hg(o), o;
}
const Hs = (e, t) => {
  const n = fg(e), o = (r, i = t) => Wc(n, r, i);
  return Object.assign(o, n), o;
}, yg = (e, t) => e ? Hs(e, t) : Hs;
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
var qr = { exports: {} }, Me = {};
var Os;
function mg() {
  if (Os) return Me;
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
  return Me.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, Me.createPortal = function(d, l) {
    var f = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!l || l.nodeType !== 1 && l.nodeType !== 9 && l.nodeType !== 11)
      throw Error(t(299));
    return i(d, l, null, f);
  }, Me.flushSync = function(d) {
    var l = s.T, f = o.p;
    try {
      if (s.T = null, o.p = 2, d) return d();
    } finally {
      s.T = l, o.p = f, o.d.f();
    }
  }, Me.preconnect = function(d, l) {
    typeof d == "string" && (l ? (l = l.crossOrigin, l = typeof l == "string" ? l === "use-credentials" ? l : "" : void 0) : l = null, o.d.C(d, l));
  }, Me.prefetchDNS = function(d) {
    typeof d == "string" && o.d.D(d);
  }, Me.preinit = function(d, l) {
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
  }, Me.preinitModule = function(d, l) {
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
  }, Me.preload = function(d, l) {
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
  }, Me.preloadModule = function(d, l) {
    if (typeof d == "string")
      if (l) {
        var f = c(l.as, l.crossOrigin);
        o.d.m(d, {
          as: typeof l.as == "string" && l.as !== "script" ? l.as : void 0,
          crossOrigin: f,
          integrity: typeof l.integrity == "string" ? l.integrity : void 0
        });
      } else o.d.m(d);
  }, Me.requestFormReset = function(d) {
    o.d.r(d);
  }, Me.unstable_batchedUpdates = function(d, l) {
    return d(l);
  }, Me.useFormState = function(d, l, f) {
    return s.H.useFormState(d, l, f);
  }, Me.useFormStatus = function() {
    return s.H.useHostTransitionStatus();
  }, Me.version = "19.2.7", Me;
}
var Fs;
function xg() {
  if (Fs) return qr.exports;
  Fs = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), qr.exports = mg(), qr.exports;
}
var wg = xg();
const sr = vi(null), vg = sr.Provider, Xc = Oe.error001("react");
function ue(e, t) {
  const n = On(sr);
  if (n === null)
    throw new Error(Xc);
  return Wc(n, e, t);
}
function we() {
  const e = On(sr);
  if (e === null)
    throw new Error(Xc);
  return ge(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const Bs = { display: "none" }, bg = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, Yc = "react-flow__node-desc", qc = "react-flow__edge-desc", Ng = "react-flow__aria-live", Sg = (e) => e.ariaLiveMessage, Eg = (e) => e.ariaLabelConfig;
function Cg({ rfId: e }) {
  const t = ue(Sg);
  return a.jsx("div", { id: `${Ng}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: bg, children: t });
}
function kg({ rfId: e, disableKeyboardA11y: t }) {
  const n = ue(Eg);
  return a.jsxs(a.Fragment, { children: [a.jsx("div", { id: `${Yc}-${e}`, style: Bs, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), a.jsx("div", { id: `${qc}-${e}`, style: Bs, children: n["edge.a11yDescription.default"] }), !t && a.jsx(Cg, { rfId: e })] });
}
const ar = Go(({ position: e = "top-left", children: t, className: n, style: o, ...r }, i) => {
  const s = `${e}`.split("-");
  return a.jsx("div", { className: Ie(["react-flow__panel", n, ...s]), style: o, ref: i, ...r, children: t });
});
ar.displayName = "Panel";
function Ig({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : a.jsx(ar, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: a.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const jg = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, Eo = (e) => e.id;
function _g(e, t) {
  return xe(e.selectedNodes.map(Eo), t.selectedNodes.map(Eo)) && xe(e.selectedEdges.map(Eo), t.selectedEdges.map(Eo));
}
function Ag({ onSelectionChange: e }) {
  const t = we(), { selectedNodes: n, selectedEdges: o } = ue(jg, _g);
  return oe(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((i) => i(r));
  }, [n, o, e]), null;
}
const Mg = (e) => !!e.onSelectionChangeHandlers;
function Dg({ onSelectionChange: e }) {
  const t = ue(Mg);
  return e || t ? a.jsx(Ag, { onSelectionChange: e }) : null;
}
const Zc = [0, 0], Pg = { x: 0, y: 0, zoom: 1 }, $g = [
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
], Ws = [...$g, "rfId"], Tg = (e) => ({
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
  translateExtent: Tn,
  nodeOrigin: Zc,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function zg(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: i, setNodeExtent: s, reset: c, setDefaultNodesAndEdges: d } = ue(Tg, xe), l = we();
  oe(() => (d(e.defaultNodes, e.defaultEdges), () => {
    f.current = Xs, c();
  }), []);
  const f = ce(Xs);
  return oe(
    () => {
      for (const u of Ws) {
        const h = e[u], g = f.current[u];
        h !== g && (typeof e[u] > "u" || (u === "nodes" ? t(h) : u === "edges" ? n(h) : u === "minZoom" ? o(h) : u === "maxZoom" ? r(h) : u === "translateExtent" ? i(h) : u === "nodeExtent" ? s(h) : u === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: mp(h) }) : u === "fitView" ? l.setState({ fitViewQueued: h }) : u === "fitViewOptions" ? l.setState({ fitViewOptions: h }) : l.setState({ [u]: h })));
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
function Lg(e) {
  const [t, n] = Y(e === "system" ? null : e);
  return oe(() => {
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
function Vn(e = null, t = { target: qs, actInsideInputWithModifier: !0 }) {
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
    const d = t?.target ?? qs, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const f = (g) => {
        if (r.current = g.ctrlKey || g.metaKey || g.shiftKey || g.altKey, (!r.current || r.current && !l) && kc(g))
          return !1;
        const v = Us(g.code, c);
        if (i.current.add(g[v]), Zs(s, i.current, !1)) {
          const x = g.composedPath?.()?.[0] || g.target, m = x?.nodeName === "BUTTON" || x?.nodeName === "A";
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
const Rg = () => {
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
function Uc(e, t) {
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
      Vg(d, c);
    n.push(c);
  }
  return r.length && r.forEach((i) => {
    i.index !== void 0 ? n.splice(i.index, 0, { ...i.item }) : n.push({ ...i.item });
  }), n;
}
function Vg(e, t) {
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
function Kc(e, t) {
  return Uc(e, t);
}
function Gc(e, t) {
  return Uc(e, t);
}
function It(e, t) {
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
    !(i.selected === void 0 && !s) && i.selected !== s && (n && (i.selected = s), o.push(It(i.id, s)));
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
const Jc = Nc();
function Qc(e, t, n = {}) {
  return Np(e, t, {
    ...n,
    onError: n.onError ?? Jc
  });
}
function Hg(e, t, n, o = { shouldReplaceId: !0 }) {
  return Sp(e, t, n, {
    ...o,
    onError: o.onError ?? Jc
  });
}
const Js = (e) => cp(e), Og = (e) => xc(e);
function el(e) {
  return Go(e);
}
const Fg = typeof window < "u" ? Ed : oe;
function Qs(e) {
  const [t, n] = Y(BigInt(0)), [o] = Y(() => Bg(() => n((r) => r + BigInt(1))));
  return Fg(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function Bg(e) {
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
const tl = vi(null);
function Wg({ children: e }) {
  const t = we(), n = pe((c) => {
    const { nodes: d = [], setNodes: l, hasDefaultNodes: f, onNodesChange: u, nodeLookup: h, fitViewQueued: g, onNodesChangeMiddlewareMap: y } = t.getState();
    let v = d;
    for (const m of c)
      v = typeof m == "function" ? m(v) : m;
    let x = Ks({
      items: v,
      lookup: h
    });
    for (const m of y.values())
      x = m(x);
    f && l(v), x.length > 0 ? u?.(x) : g && window.requestAnimationFrame(() => {
      const { fitViewQueued: m, nodes: E, setNodes: p } = t.getState();
      m && p(E);
    });
  }, []), o = Qs(n), r = pe((c) => {
    const { edges: d = [], setEdges: l, hasDefaultEdges: f, onEdgesChange: u, edgeLookup: h } = t.getState();
    let g = d;
    for (const y of c)
      g = typeof y == "function" ? y(g) : y;
    f ? l(g) : u && u(Ks({
      items: g,
      lookup: h
    }));
  }, []), i = Qs(r), s = ge(() => ({ nodeQueue: o, edgeQueue: i }), []);
  return a.jsx(tl.Provider, { value: s, children: e });
}
function Xg() {
  const e = On(tl);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Yg = (e) => !!e.panZoom;
function Ri() {
  const e = Rg(), t = we(), n = Xg(), o = ue(Yg), r = ge(() => {
    const i = (u) => t.getState().nodeLookup.get(u), s = (u) => {
      n.nodeQueue.push(u);
    }, c = (u) => {
      n.edgeQueue.push(u);
    }, d = (u) => {
      const { nodeLookup: h, nodeOrigin: g } = t.getState(), y = Js(u) ? u : h.get(u.id), v = y.parentId ? Ec(y.position, y.measured, y.parentId, h, g) : y.position, x = {
        ...y,
        position: v,
        width: y.measured?.width ?? y.width,
        height: y.measured?.height ?? y.height
      };
      return en(x);
    }, l = (u, h, g = { replace: !1 }) => {
      s((y) => y.map((v) => {
        if (v.id === u) {
          const x = typeof h == "function" ? h(v) : h;
          return g.replace && Js(x) ? x : { ...v, ...x };
        }
        return v;
      }));
    }, f = (u, h, g = { replace: !1 }) => {
      c((y) => y.map((v) => {
        if (v.id === u) {
          const x = typeof h == "function" ? h(v) : h;
          return g.replace && Og(x) ? x : { ...v, ...x };
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
        const { nodes: u = [], edges: h = [], transform: g } = t.getState(), [y, v, x] = g;
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
        const { nodes: g, edges: y, onNodesDelete: v, onEdgesDelete: x, triggerNodeChanges: m, triggerEdgeChanges: E, onDelete: p, onBeforeDelete: w } = t.getState(), { nodes: b, edges: C } = await hp({
          nodesToRemove: u,
          edgesToRemove: h,
          nodes: g,
          edges: y,
          onBeforeDelete: w
        }), k = C.length > 0, A = b.length > 0;
        if (k) {
          const P = C.map(Gs);
          x?.(C), E(P);
        }
        if (A) {
          const P = b.map(Gs);
          v?.(b), m(P);
        }
        return (A || k) && p?.({ nodes: b, edges: C }), { deletedNodes: b, deletedEdges: C };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (u, h = !0, g) => {
        const y = Ns(u), v = y ? u : d(u), x = g !== void 0;
        return v ? (g || t.getState().nodes).filter((m) => {
          const E = t.getState().nodeLookup.get(m.id);
          if (E && !y && (m.id === u.id || !E.internals.positionAbsolute))
            return !1;
          const p = en(x ? m : E), w = Ln(p, v);
          return h && w > 0 || w >= p.width * p.height || w >= v.width * v.height;
        }) : [];
      },
      isNodeIntersecting: (u, h, g = !0) => {
        const v = Ns(u) ? u : d(u);
        if (!v)
          return !1;
        const x = Ln(v, h);
        return g && x > 0 || x >= h.width * h.height || x >= v.width * v.height;
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
        return lp(u, { nodeLookup: h, nodeOrigin: g });
      },
      getHandleConnections: ({ type: u, id: h, nodeId: g }) => Array.from(t.getState().connectionLookup.get(`${g}-${u}${h ? `-${h}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: u, handleId: h, nodeId: g }) => Array.from(t.getState().connectionLookup.get(`${g}${u ? h ? `-${u}-${h}` : `-${u}` : ""}`)?.values() ?? []),
      fitView: async (u) => {
        const h = t.getState().fitViewResolver ?? yp();
        return t.setState({ fitViewQueued: !0, fitViewOptions: u, fitViewResolver: h }), n.nodeQueue.push((g) => [...g]), h.promise;
      }
    };
  }, []);
  return ge(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const ea = (e) => e.selected, qg = typeof window < "u" ? window : void 0;
function Zg({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = we(), { deleteElements: o } = Ri(), r = Vn(e, { actInsideInputWithModifier: !1 }), i = Vn(t, { target: qg });
  oe(() => {
    if (r) {
      const { edges: s, nodes: c } = n.getState();
      o({ nodes: c.filter(ea), edges: s.filter(ea) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), oe(() => {
    n.setState({ multiSelectionActive: i });
  }, [i]);
}
function Ug(e) {
  const t = we();
  oe(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = Di(e.current);
      (o.height === 0 || o.width === 0) && t.getState().onError?.("004", Oe.error004()), t.setState({ width: o.width || 500, height: o.height || 500 });
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
const cr = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, Kg = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function Gg({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: i = At.Free, zoomOnDoubleClick: s = !0, panOnDrag: c = !0, defaultViewport: d, translateExtent: l, minZoom: f, maxZoom: u, zoomActivationKeyCode: h, preventScrolling: g = !0, children: y, noWheelClassName: v, noPanClassName: x, onViewportChange: m, isControlledViewport: E, paneClickDistance: p, selectionOnDrag: w }) {
  const b = we(), C = ce(null), { userSelectionActive: k, lib: A, connectionInProgress: P } = ue(Kg, xe), W = Vn(h), M = ce();
  Ug(C);
  const z = pe((O) => {
    m?.({ x: O[0], y: O[1], zoom: O[2] }), E || b.setState({ transform: O });
  }, [m, E]);
  return oe(() => {
    if (C.current) {
      M.current = Qp({
        domNode: C.current,
        minZoom: f,
        maxZoom: u,
        translateExtent: l,
        viewport: d,
        onDraggingChange: (I) => b.setState((D) => D.paneDragging === I ? D : { paneDragging: I }),
        onPanZoomStart: (I, D) => {
          const { onViewportChangeStart: $, onMoveStart: T } = b.getState();
          T?.(I, D), $?.(D);
        },
        onPanZoom: (I, D) => {
          const { onViewportChange: $, onMove: T } = b.getState();
          T?.(I, D), $?.(D);
        },
        onPanZoomEnd: (I, D) => {
          const { onViewportChangeEnd: $, onMoveEnd: T } = b.getState();
          T?.(I, D), $?.(D);
        }
      });
      const { x: O, y: N, zoom: j } = M.current.getViewport();
      return b.setState({
        panZoom: M.current,
        transform: [O, N, j],
        domNode: C.current.closest(".react-flow")
      }), () => {
        M.current?.destroy();
      };
    }
  }, []), oe(() => {
    M.current?.update({
      onPaneContextMenu: e,
      zoomOnScroll: t,
      zoomOnPinch: n,
      panOnScroll: o,
      panOnScrollSpeed: r,
      panOnScrollMode: i,
      zoomOnDoubleClick: s,
      panOnDrag: c,
      zoomActivationKeyPressed: W,
      preventScrolling: g,
      noPanClassName: x,
      userSelectionActive: k,
      noWheelClassName: v,
      lib: A,
      onTransformChange: z,
      connectionInProgress: P,
      selectionOnDrag: w,
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
    W,
    g,
    x,
    k,
    v,
    A,
    z,
    P,
    w,
    p
  ]), a.jsx("div", { className: "react-flow__renderer", ref: C, style: cr, children: y });
}
const Jg = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function Qg() {
  const { userSelectionActive: e, userSelectionRect: t } = ue(Jg, xe);
  return e && t ? a.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const Zr = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, ey = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function ty({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = zn.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: i, selectionOnDrag: s, onSelectionStart: c, onSelectionEnd: d, onPaneClick: l, onPaneContextMenu: f, onPaneScroll: u, onPaneMouseEnter: h, onPaneMouseMove: g, onPaneMouseLeave: y, children: v }) {
  const x = ce(0), m = we(), { userSelectionActive: E, elementsSelectable: p, dragging: w, connectionInProgress: b, panBy: C, autoPanSpeed: k } = ue(ey, xe), A = p && (e || E), P = ce(null), W = ce(), M = ce(/* @__PURE__ */ new Set()), z = ce(/* @__PURE__ */ new Set()), O = ce(!1), N = ce({ x: 0, y: 0 }), j = ce(!1), I = (L) => {
    if (O.current || b) {
      O.current = !1;
      return;
    }
    l?.(L), m.getState().resetSelectedElements(), m.setState({ nodesSelectionActive: !1 });
  }, D = (L) => {
    if (Array.isArray(o) && o?.includes(2)) {
      L.preventDefault();
      return;
    }
    f?.(L);
  }, $ = u ? (L) => u(L) : void 0, T = (L) => {
    O.current && (L.stopPropagation(), O.current = !1);
  }, H = (L) => {
    const { domNode: Z, transform: ie } = m.getState();
    if (W.current = Z?.getBoundingClientRect(), !W.current)
      return;
    const se = L.target === P.current;
    if (!se && !!L.target.closest(".nokey") || !e || !(s && se || t) || L.button !== 0 || !L.isPrimary)
      return;
    L.target?.setPointerCapture?.(L.pointerId), O.current = !1;
    const { x: le, y: V } = Ue(L.nativeEvent, W.current), J = rn({ x: le, y: V }, ie);
    m.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: J.x,
        startY: J.y,
        x: le,
        y: V
      }
    }), se || (L.stopPropagation(), L.preventDefault());
  };
  function B(L, Z) {
    const { userSelectionRect: ie } = m.getState();
    if (!ie)
      return;
    const { transform: se, nodeLookup: G, edgeLookup: ee, connectionLookup: le, triggerNodeChanges: V, triggerEdgeChanges: J, defaultEdgeOptions: fe } = m.getState(), me = { x: ie.startX, y: ie.startY }, { x: Ee, y: De } = tn(me, se), ye = {
      startX: me.x,
      startY: me.y,
      x: L < Ee ? L : Ee,
      y: Z < De ? Z : De,
      width: Math.abs(L - Ee),
      height: Math.abs(Z - De)
    }, Be = M.current, rt = z.current;
    M.current = new Set(_i(G, ye, se, n === zn.Partial, !0).map((ze) => ze.id)), z.current = /* @__PURE__ */ new Set();
    const it = fe?.selectable ?? !0;
    for (const ze of M.current) {
      const We = le.get(ze);
      if (We)
        for (const { edgeId: Ce } of We.values()) {
          const Fe = ee.get(Ce);
          Fe && (Fe.selectable ?? it) && z.current.add(Ce);
        }
    }
    if (!Ss(Be, M.current)) {
      const ze = Yt(G, M.current, !0);
      V(ze);
    }
    if (!Ss(rt, z.current)) {
      const ze = Yt(ee, z.current);
      J(ze);
    }
    m.setState({
      userSelectionRect: ye,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function F() {
    if (!r || !W.current)
      return;
    const [L, Z] = Ai(N.current, W.current, k);
    C({ x: L, y: Z }).then((ie) => {
      if (!O.current || !ie) {
        x.current = requestAnimationFrame(F);
        return;
      }
      const { x: se, y: G } = N.current;
      B(se, G), x.current = requestAnimationFrame(F);
    });
  }
  const U = () => {
    cancelAnimationFrame(x.current), x.current = 0, j.current = !1;
  };
  oe(() => () => U(), []);
  const q = (L) => {
    const { userSelectionRect: Z, transform: ie, resetSelectedElements: se } = m.getState();
    if (!W.current || !Z)
      return;
    const { x: G, y: ee } = Ue(L.nativeEvent, W.current);
    N.current = { x: G, y: ee };
    const le = tn({ x: Z.startX, y: Z.startY }, ie);
    if (!O.current) {
      const V = t ? 0 : i;
      if (Math.hypot(G - le.x, ee - le.y) <= V)
        return;
      se(), c?.(L);
    }
    O.current = !0, j.current || (F(), j.current = !0), B(G, ee);
  }, ne = (L) => {
    L.button === 0 && (L.target?.releasePointerCapture?.(L.pointerId), !E && L.target === P.current && m.getState().userSelectionRect && I?.(L), m.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), O.current && (d?.(L), m.setState({
      nodesSelectionActive: M.current.size > 0
    })), U());
  }, ae = (L) => {
    L.target?.releasePointerCapture?.(L.pointerId), U();
  }, K = o === !0 || Array.isArray(o) && o.includes(0);
  return a.jsxs("div", { className: Ie(["react-flow__pane", { draggable: K, dragging: w, selection: e }]), onClick: A ? void 0 : Zr(I, P), onContextMenu: Zr(D, P), onWheel: Zr($, P), onPointerEnter: A ? void 0 : h, onPointerMove: A ? q : g, onPointerUp: A ? ne : void 0, onPointerCancel: A ? ae : void 0, onPointerDownCapture: A ? H : void 0, onClickCapture: A ? T : void 0, onPointerLeave: y, ref: P, style: cr, children: [v, a.jsx(Qg, {})] });
}
function ui({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: i, multiSelectionActive: s, nodeLookup: c, onError: d } = t.getState(), l = c.get(e);
  if (!l) {
    d?.("012", Oe.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && s) && (i({ nodes: [l], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function nl({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: i, nodeClickDistance: s }) {
  const c = we(), [d, l] = Y(!1), f = ce();
  return oe(() => {
    f.current = Vp({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (u) => {
        ui({
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
const ny = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function ol() {
  const e = we();
  return pe((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: i, nodesDraggable: s, onError: c, updateNodePositions: d, nodeLookup: l, nodeOrigin: f } = e.getState(), u = /* @__PURE__ */ new Map(), h = ny(s), g = r ? i[0] : 5, y = r ? i[1] : 5, v = n.direction.x * g * n.factor, x = n.direction.y * y * n.factor;
    for (const [, m] of l) {
      if (!h(m))
        continue;
      let E = {
        x: m.internals.positionAbsolute.x + v,
        y: m.internals.positionAbsolute.y + x
      };
      r && (E = Yn(E, i));
      const { position: p, positionAbsolute: w } = wc({
        nodeId: m.id,
        nextPosition: E,
        nodeLookup: l,
        nodeExtent: o,
        nodeOrigin: f,
        onError: c
      });
      m.position = p, m.internals.positionAbsolute = w, u.set(m.id, m);
    }
    d(u);
  }, []);
}
const Vi = vi(null), oy = Vi.Provider;
Vi.Consumer;
const rl = () => On(Vi), ry = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), iy = (e, t, n) => (o) => {
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
function sy({ type: e = "source", position: t = te.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: i = !0, id: s, onConnect: c, children: d, className: l, onMouseDown: f, onTouchStart: u, ...h }, g) {
  const y = s || null, v = e === "target", x = we(), m = rl(), { connectOnClick: E, noPanClassName: p, rfId: w } = ue(ry, xe), { connectingFrom: b, connectingTo: C, clickConnecting: k, isPossibleEndHandle: A, connectionInProcess: P, clickConnectionInProcess: W, valid: M } = ue(iy(m, y, e), xe);
  m || x.getState().onError?.("010", Oe.error010());
  const z = (j) => {
    const { defaultEdgeOptions: I, onConnect: D, hasDefaultEdges: $ } = x.getState(), T = {
      ...I,
      ...j
    };
    if ($) {
      const { edges: H, setEdges: B, onError: F } = x.getState();
      B(Qc(T, H, { onError: F }));
    }
    D?.(T), c?.(T);
  }, O = (j) => {
    if (!m)
      return;
    const I = Ic(j.nativeEvent);
    if (r && (I && j.button === 0 || !I)) {
      const D = x.getState();
      di.onPointerDown(j.nativeEvent, {
        handleDomNode: j.currentTarget,
        autoPanOnConnect: D.autoPanOnConnect,
        connectionMode: D.connectionMode,
        connectionRadius: D.connectionRadius,
        domNode: D.domNode,
        nodeLookup: D.nodeLookup,
        lib: D.lib,
        isTarget: v,
        handleId: y,
        nodeId: m,
        flowId: D.rfId,
        panBy: D.panBy,
        cancelConnection: D.cancelConnection,
        onConnectStart: D.onConnectStart,
        onConnectEnd: (...$) => x.getState().onConnectEnd?.(...$),
        updateConnection: D.updateConnection,
        onConnect: z,
        isValidConnection: n || ((...$) => x.getState().isValidConnection?.(...$) ?? !0),
        getTransform: () => x.getState().transform,
        getFromHandle: () => x.getState().connection.fromHandle,
        autoPanSpeed: D.autoPanSpeed,
        dragThreshold: D.connectionDragThreshold
      });
    }
    I ? f?.(j) : u?.(j);
  }, N = (j) => {
    const { onClickConnectStart: I, onClickConnectEnd: D, connectionClickStartHandle: $, connectionMode: T, isValidConnection: H, lib: B, rfId: F, nodeLookup: U, connection: q } = x.getState();
    if (!m || !$ && !r)
      return;
    if (!$) {
      I?.(j.nativeEvent, { nodeId: m, handleId: y, handleType: e }), x.setState({ connectionClickStartHandle: { nodeId: m, type: e, id: y } });
      return;
    }
    const ne = Cc(j.target), ae = n || H, { connection: K, isValid: L } = di.isValid(j.nativeEvent, {
      handle: {
        nodeId: m,
        id: y,
        type: e
      },
      connectionMode: T,
      fromNodeId: $.nodeId,
      fromHandleId: $.id || null,
      fromType: $.type,
      isValidConnection: ae,
      flowId: F,
      doc: ne,
      lib: B,
      nodeLookup: U
    });
    L && K && z(K);
    const Z = structuredClone(q);
    delete Z.inProgress, Z.toPosition = Z.toHandle ? Z.toHandle.position : null, D?.(j, Z), x.setState({ connectionClickStartHandle: null });
  };
  return a.jsx("div", { "data-handleid": y, "data-nodeid": m, "data-handlepos": t, "data-id": `${w}-${m}-${y}-${e}`, className: Ie([
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
      clickconnecting: k,
      connectingfrom: b,
      connectingto: C,
      valid: M,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!P || A) && (P || W ? i : r)
    }
  ]), onMouseDown: O, onTouchStart: O, onClick: E ? N : void 0, ref: g, ...h, children: d });
}
const on = Se(el(sy));
function ay({ data: e, isConnectable: t, sourcePosition: n = te.Bottom }) {
  return a.jsxs(a.Fragment, { children: [e?.label, a.jsx(on, { type: "source", position: n, isConnectable: t })] });
}
function cy({ data: e, isConnectable: t, targetPosition: n = te.Top, sourcePosition: o = te.Bottom }) {
  return a.jsxs(a.Fragment, { children: [a.jsx(on, { type: "target", position: n, isConnectable: t }), e?.label, a.jsx(on, { type: "source", position: o, isConnectable: t })] });
}
function ly() {
  return null;
}
function dy({ data: e, isConnectable: t, targetPosition: n = te.Top }) {
  return a.jsxs(a.Fragment, { children: [a.jsx(on, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const Yo = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, ta = {
  input: ay,
  default: cy,
  output: dy,
  group: ly
};
function uy(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const fy = (e) => {
  const { width: t, height: n, x: o, y: r } = Xn(e.nodeLookup, {
    filter: (i) => !!i.selected
  });
  return {
    width: Ze(t) ? t : null,
    height: Ze(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function hy({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = we(), { width: r, height: i, transformString: s, userSelectionActive: c } = ue(fy, xe), d = ol(), l = ce(null);
  oe(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const f = !c && r !== null && i !== null;
  if (nl({
    nodeRef: l,
    disabled: !f
  }), !f)
    return null;
  const u = e ? (g) => {
    const y = o.getState().nodes.filter((v) => v.selected);
    e(g, y);
  } : void 0, h = (g) => {
    Object.prototype.hasOwnProperty.call(Yo, g.key) && (g.preventDefault(), d({
      direction: Yo[g.key],
      factor: g.shiftKey ? 4 : 1
    }));
  };
  return a.jsx("div", { className: Ie(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: s
  }, children: a.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: u, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : h, style: {
    width: r,
    height: i
  } }) });
}
const na = typeof window < "u" ? window : void 0, py = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function il({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, paneClickDistance: c, deleteKeyCode: d, selectionKeyCode: l, selectionOnDrag: f, selectionMode: u, onSelectionStart: h, onSelectionEnd: g, multiSelectionKeyCode: y, panActivationKeyCode: v, zoomActivationKeyCode: x, elementsSelectable: m, zoomOnScroll: E, zoomOnPinch: p, panOnScroll: w, panOnScrollSpeed: b, panOnScrollMode: C, zoomOnDoubleClick: k, panOnDrag: A, autoPanOnSelection: P, defaultViewport: W, translateExtent: M, minZoom: z, maxZoom: O, preventScrolling: N, onSelectionContextMenu: j, noWheelClassName: I, noPanClassName: D, disableKeyboardA11y: $, onViewportChange: T, isControlledViewport: H }) {
  const { nodesSelectionActive: B, userSelectionActive: F } = ue(py, xe), U = Vn(l, { target: na }), q = Vn(v, { target: na }), ne = q || A, ae = q || w, K = f && ne !== !0, L = U || F || K;
  return Zg({ deleteKeyCode: d, multiSelectionKeyCode: y }), a.jsx(Gg, { onPaneContextMenu: i, elementsSelectable: m, zoomOnScroll: E, zoomOnPinch: p, panOnScroll: ae, panOnScrollSpeed: b, panOnScrollMode: C, zoomOnDoubleClick: k, panOnDrag: !U && ne, defaultViewport: W, translateExtent: M, minZoom: z, maxZoom: O, zoomActivationKeyCode: x, preventScrolling: N, noWheelClassName: I, noPanClassName: D, onViewportChange: T, isControlledViewport: H, paneClickDistance: c, selectionOnDrag: K, children: a.jsxs(ty, { onSelectionStart: h, onSelectionEnd: g, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, panOnDrag: ne, autoPanOnSelection: P, isSelecting: !!L, selectionMode: u, selectionKeyPressed: U, paneClickDistance: c, selectionOnDrag: K, children: [e, B && a.jsx(hy, { onSelectionContextMenu: j, noPanClassName: D, disableKeyboardA11y: $ })] }) });
}
il.displayName = "FlowRenderer";
const gy = Se(il), yy = (e) => (t) => e ? _i(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function my(e) {
  return ue(pe(yy(e), [e]), xe);
}
const xy = (e) => e.updateNodeInternals;
function wy() {
  const e = ue(xy), [t] = Y(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
function vy({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = we(), i = ce(null), s = ce(null), c = ce(e.sourcePosition), d = ce(e.targetPosition), l = ce(t), f = n && !!e.internals.handleBounds;
  return oe(() => {
    i.current && !e.hidden && (!f || s.current !== i.current) && (s.current && o?.unobserve(s.current), o?.observe(i.current), s.current = i.current);
  }, [f, e.hidden]), oe(() => () => {
    s.current && (o?.unobserve(s.current), s.current = null);
  }, []), oe(() => {
    if (i.current) {
      const u = l.current !== t, h = c.current !== e.sourcePosition, g = d.current !== e.targetPosition;
      (u || h || g) && (l.current = t, c.current = e.sourcePosition, d.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: i.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), i;
}
function by({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: i, onDoubleClick: s, nodesDraggable: c, elementsSelectable: d, nodesConnectable: l, nodesFocusable: f, resizeObserver: u, noDragClassName: h, noPanClassName: g, disableKeyboardA11y: y, rfId: v, nodeTypes: x, nodeClickDistance: m, onError: E }) {
  const { node: p, internals: w, isParent: b } = ue((L) => {
    const Z = L.nodeLookup.get(e), ie = L.parentLookup.has(e);
    return {
      node: Z,
      internals: Z.internals,
      isParent: ie
    };
  }, xe);
  let C = p.type || "default", k = x?.[C] || ta[C];
  k === void 0 && (E?.("003", Oe.error003(C)), C = "default", k = x?.default || ta.default);
  const A = !!(p.draggable || c && typeof p.draggable > "u"), P = !!(p.selectable || d && typeof p.selectable > "u"), W = !!(p.connectable || l && typeof p.connectable > "u"), M = !!(p.focusable || f && typeof p.focusable > "u"), z = we(), O = Sc(p), N = vy({ node: p, nodeType: C, hasDimensions: O, resizeObserver: u }), j = nl({
    nodeRef: N,
    disabled: p.hidden || !A,
    noDragClassName: h,
    handleSelector: p.dragHandle,
    nodeId: e,
    isSelectable: P,
    nodeClickDistance: m
  }), I = ol();
  if (p.hidden)
    return null;
  const D = dt(p), $ = uy(p), T = P || A || t || n || o || r, H = n ? (L) => n(L, { ...w.userNode }) : void 0, B = o ? (L) => o(L, { ...w.userNode }) : void 0, F = r ? (L) => r(L, { ...w.userNode }) : void 0, U = i ? (L) => i(L, { ...w.userNode }) : void 0, q = s ? (L) => s(L, { ...w.userNode }) : void 0, ne = (L) => {
    const { selectNodesOnDrag: Z, nodeDragThreshold: ie } = z.getState();
    P && (!Z || !A || ie > 0) && ui({
      id: e,
      store: z,
      nodeRef: N
    }), t && t(L, { ...w.userNode });
  }, ae = (L) => {
    if (!(kc(L.nativeEvent) || y)) {
      if (pc.includes(L.key) && P) {
        const Z = L.key === "Escape";
        ui({
          id: e,
          store: z,
          unselect: Z,
          nodeRef: N
        });
      } else if (A && p.selected && Object.prototype.hasOwnProperty.call(Yo, L.key)) {
        L.preventDefault();
        const { ariaLabelConfig: Z } = z.getState();
        z.setState({
          ariaLiveMessage: Z["node.a11yDescription.ariaLiveMessage"]({
            direction: L.key.replace("Arrow", "").toLowerCase(),
            x: ~~w.positionAbsolute.x,
            y: ~~w.positionAbsolute.y
          })
        }), I({
          direction: Yo[L.key],
          factor: L.shiftKey ? 4 : 1
        });
      }
    }
  }, K = () => {
    if (y || !N.current?.matches(":focus-visible"))
      return;
    const { transform: L, width: Z, height: ie, autoPanOnNodeFocus: se, setCenter: G } = z.getState();
    if (!se)
      return;
    _i(/* @__PURE__ */ new Map([[e, p]]), { x: 0, y: 0, width: Z, height: ie }, L, !0).length > 0 || G(p.position.x + D.width / 2, p.position.y + D.height / 2, {
      zoom: L[2]
    });
  };
  return a.jsx("div", { className: Ie([
    "react-flow__node",
    `react-flow__node-${C}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [g]: A
    },
    p.className,
    {
      selected: p.selected,
      selectable: P,
      parent: b,
      draggable: A,
      dragging: j
    }
  ]), ref: N, style: {
    zIndex: w.z,
    transform: `translate(${w.positionAbsolute.x}px,${w.positionAbsolute.y}px)`,
    pointerEvents: T ? "all" : "none",
    visibility: O ? "visible" : "hidden",
    ...p.style,
    ...$
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: H, onMouseMove: B, onMouseLeave: F, onContextMenu: U, onClick: ne, onDoubleClick: q, onKeyDown: M ? ae : void 0, tabIndex: M ? 0 : void 0, onFocus: M ? K : void 0, role: p.ariaRole ?? (M ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": y ? void 0 : `${Yc}-${v}`, "aria-label": p.ariaLabel, ...p.domAttributes, children: a.jsx(oy, { value: e, children: a.jsx(k, { id: e, data: p.data, type: C, positionAbsoluteX: w.positionAbsolute.x, positionAbsoluteY: w.positionAbsolute.y, selected: p.selected ?? !1, selectable: P, draggable: A, deletable: p.deletable ?? !0, isConnectable: W, sourcePosition: p.sourcePosition, targetPosition: p.targetPosition, dragging: j, dragHandle: p.dragHandle, zIndex: w.z, parentId: p.parentId, ...D }) }) });
}
var Ny = Se(by);
const Sy = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function sl(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: i } = ue(Sy, xe), s = my(e.onlyRenderVisibleElements), c = wy();
  return a.jsx("div", { className: "react-flow__nodes", style: cr, children: s.map((d) => (
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
    a.jsx(Ny, { id: d, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: i }, d)
  )) });
}
sl.displayName = "NodeRenderer";
const Ey = Se(sl);
function Cy(e) {
  return ue(pe((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const i = n.nodeLookup.get(r.source), s = n.nodeLookup.get(r.target);
        i && s && vp({
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
const ky = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return a.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, Iy = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return a.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, oa = {
  [Bo.Arrow]: ky,
  [Bo.ArrowClosed]: Iy
};
function jy(e) {
  const t = we();
  return ge(() => Object.prototype.hasOwnProperty.call(oa, e) ? oa[e] : (t.getState().onError?.("009", Oe.error009(e)), null), [e]);
}
const _y = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: i = "strokeWidth", strokeWidth: s, orient: c = "auto-start-reverse" }) => {
  const d = jy(t);
  return d ? a.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: i, orient: c, refX: "0", refY: "0", children: a.jsx(d, { color: n, strokeWidth: s }) }) : null;
}, al = ({ defaultColor: e, rfId: t }) => {
  const n = ue((i) => i.edges), o = ue((i) => i.defaultEdgeOptions), r = ge(() => jp(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? a.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: a.jsx("defs", { children: r.map((i) => a.jsx(_y, { id: i.id, type: i.type, color: i.color, width: i.width, height: i.height, markerUnits: i.markerUnits, strokeWidth: i.strokeWidth, orient: i.orient }, i.id)) }) }) : null;
};
al.displayName = "MarkerDefinitions";
var Ay = Se(al);
function cl({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: i, labelBgPadding: s = [2, 4], labelBgBorderRadius: c = 2, children: d, className: l, ...f }) {
  const [u, h] = Y({ x: 1, y: 0, width: 0, height: 0 }), g = Ie(["react-flow__edge-textwrapper", l]), y = ce(null);
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
  }, [n]), n ? a.jsxs("g", { transform: `translate(${e - u.width / 2} ${t - u.height / 2})`, className: g, visibility: u.width ? "visible" : "hidden", ...f, children: [r && a.jsx("rect", { width: u.width + 2 * s[0], x: -s[0], y: -s[1], height: u.height + 2 * s[1], className: "react-flow__edge-textbg", style: i, rx: c, ry: c }), a.jsx("text", { className: "react-flow__edge-text", y: u.height / 2, dy: "0.3em", ref: y, style: o, children: n }), d] }) : null;
}
cl.displayName = "EdgeText";
const My = Se(cl);
function qn({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: c, labelBgBorderRadius: d, interactionWidth: l = 20, ...f }) {
  return a.jsxs(a.Fragment, { children: [a.jsx("path", { ...f, d: e, fill: "none", className: Ie(["react-flow__edge-path", f.className]) }), l ? a.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, o && Ze(t) && Ze(n) ? a.jsx(My, { x: t, y: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: c, labelBgBorderRadius: d }) : null] });
}
function ra({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === te.Left || e === te.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function ll({ sourceX: e, sourceY: t, sourcePosition: n = te.Bottom, targetX: o, targetY: r, targetPosition: i = te.Top }) {
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
  }), [f, u, h, g] = jc({
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
function dl(e) {
  return Se(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: c, label: d, labelStyle: l, labelShowBg: f, labelBgStyle: u, labelBgPadding: h, labelBgBorderRadius: g, style: y, markerEnd: v, markerStart: x, interactionWidth: m }) => {
    const [E, p, w] = ll({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: c
    }), b = e.isInternal ? void 0 : t;
    return a.jsx(qn, { id: b, path: E, labelX: p, labelY: w, label: d, labelStyle: l, labelShowBg: f, labelBgStyle: u, labelBgPadding: h, labelBgBorderRadius: g, style: y, markerEnd: v, markerStart: x, interactionWidth: m });
  });
}
const Dy = dl({ isInternal: !1 }), ul = dl({ isInternal: !0 });
Dy.displayName = "SimpleBezierEdge";
ul.displayName = "SimpleBezierEdgeInternal";
function fl(e) {
  return Se(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: c, labelShowBg: d, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: u, style: h, sourcePosition: g = te.Bottom, targetPosition: y = te.Top, markerEnd: v, markerStart: x, pathOptions: m, interactionWidth: E }) => {
    const [p, w, b] = Xo({
      sourceX: n,
      sourceY: o,
      sourcePosition: g,
      targetX: r,
      targetY: i,
      targetPosition: y,
      borderRadius: m?.borderRadius,
      offset: m?.offset,
      stepPosition: m?.stepPosition
    }), C = e.isInternal ? void 0 : t;
    return a.jsx(qn, { id: C, path: p, labelX: w, labelY: b, label: s, labelStyle: c, labelShowBg: d, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: u, style: h, markerEnd: v, markerStart: x, interactionWidth: E });
  });
}
const hl = fl({ isInternal: !1 }), pl = fl({ isInternal: !0 });
hl.displayName = "SmoothStepEdge";
pl.displayName = "SmoothStepEdgeInternal";
function gl(e) {
  return Se(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return a.jsx(hl, { ...n, id: o, pathOptions: ge(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Py = gl({ isInternal: !1 }), yl = gl({ isInternal: !0 });
Py.displayName = "StepEdge";
yl.displayName = "StepEdgeInternal";
function ml(e) {
  return Se(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: c, labelShowBg: d, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: u, style: h, markerEnd: g, markerStart: y, interactionWidth: v }) => {
    const [x, m, E] = Dc({ sourceX: n, sourceY: o, targetX: r, targetY: i }), p = e.isInternal ? void 0 : t;
    return a.jsx(qn, { id: p, path: x, labelX: m, labelY: E, label: s, labelStyle: c, labelShowBg: d, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: u, style: h, markerEnd: g, markerStart: y, interactionWidth: v });
  });
}
const $y = ml({ isInternal: !1 }), xl = ml({ isInternal: !0 });
$y.displayName = "StraightEdge";
xl.displayName = "StraightEdgeInternal";
function wl(e) {
  return Se(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s = te.Bottom, targetPosition: c = te.Top, label: d, labelStyle: l, labelShowBg: f, labelBgStyle: u, labelBgPadding: h, labelBgBorderRadius: g, style: y, markerEnd: v, markerStart: x, pathOptions: m, interactionWidth: E }) => {
    const [p, w, b] = _c({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: c,
      curvature: m?.curvature
    }), C = e.isInternal ? void 0 : t;
    return a.jsx(qn, { id: C, path: p, labelX: w, labelY: b, label: d, labelStyle: l, labelShowBg: f, labelBgStyle: u, labelBgPadding: h, labelBgBorderRadius: g, style: y, markerEnd: v, markerStart: x, interactionWidth: E });
  });
}
const Ty = wl({ isInternal: !1 }), vl = wl({ isInternal: !0 });
Ty.displayName = "BezierEdge";
vl.displayName = "BezierEdgeInternal";
const ia = {
  default: vl,
  straight: xl,
  step: yl,
  smoothstep: pl,
  simplebezier: ul
}, sa = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, zy = (e, t, n) => n === te.Left ? e - t : n === te.Right ? e + t : e, Ly = (e, t, n) => n === te.Top ? e - t : n === te.Bottom ? e + t : e, aa = "react-flow__edgeupdater";
function ca({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: i, onMouseOut: s, type: c }) {
  return a.jsx("circle", { onMouseDown: r, onMouseEnter: i, onMouseOut: s, className: Ie([aa, `${aa}-${c}`]), cx: zy(t, o, e), cy: Ly(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function Ry({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: i, targetY: s, sourcePosition: c, targetPosition: d, onReconnect: l, onReconnectStart: f, onReconnectEnd: u, setReconnecting: h, setUpdateHover: g }) {
  const y = we(), v = (w, b) => {
    if (w.button !== 0)
      return;
    const { autoPanOnConnect: C, domNode: k, connectionMode: A, connectionRadius: P, lib: W, onConnectStart: M, cancelConnection: z, nodeLookup: O, rfId: N, panBy: j, updateConnection: I } = y.getState(), D = b.type === "target", $ = (B, F) => {
      h(!1), u?.(B, n, b.type, F);
    }, T = (B) => l?.(n, B), H = (B, F) => {
      h(!0), f?.(w, n, b.type), M?.(B, F);
    };
    di.onPointerDown(w.nativeEvent, {
      autoPanOnConnect: C,
      connectionMode: A,
      connectionRadius: P,
      domNode: k,
      handleId: b.id,
      nodeId: b.nodeId,
      nodeLookup: O,
      isTarget: D,
      edgeUpdaterType: b.type,
      lib: W,
      flowId: N,
      cancelConnection: z,
      panBy: j,
      isValidConnection: (...B) => y.getState().isValidConnection?.(...B) ?? !0,
      onConnect: T,
      onConnectStart: H,
      onConnectEnd: (...B) => y.getState().onConnectEnd?.(...B),
      onReconnectEnd: $,
      updateConnection: I,
      getTransform: () => y.getState().transform,
      getFromHandle: () => y.getState().connection.fromHandle,
      dragThreshold: y.getState().connectionDragThreshold,
      handleDomNode: w.currentTarget
    });
  }, x = (w) => v(w, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), m = (w) => v(w, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), E = () => g(!0), p = () => g(!1);
  return a.jsxs(a.Fragment, { children: [(e === !0 || e === "source") && a.jsx(ca, { position: c, centerX: o, centerY: r, radius: t, onMouseDown: x, onMouseEnter: E, onMouseOut: p, type: "source" }), (e === !0 || e === "target") && a.jsx(ca, { position: d, centerX: i, centerY: s, radius: t, onMouseDown: m, onMouseEnter: E, onMouseOut: p, type: "target" })] });
}
function Vy({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: i, onContextMenu: s, onMouseEnter: c, onMouseMove: d, onMouseLeave: l, reconnectRadius: f, onReconnect: u, onReconnectStart: h, onReconnectEnd: g, rfId: y, edgeTypes: v, noPanClassName: x, onError: m, disableKeyboardA11y: E }) {
  let p = ue((G) => G.edgeLookup.get(e));
  const w = ue((G) => G.defaultEdgeOptions);
  p = w ? { ...w, ...p } : p;
  let b = p.type || "default", C = v?.[b] || ia[b];
  C === void 0 && (m?.("011", Oe.error011(b)), b = "default", C = v?.default || ia.default);
  const k = !!(p.focusable || t && typeof p.focusable > "u"), A = typeof u < "u" && (p.reconnectable || n && typeof p.reconnectable > "u"), P = !!(p.selectable || o && typeof p.selectable > "u"), W = ce(null), [M, z] = Y(!1), [O, N] = Y(!1), j = we(), { zIndex: I, sourceX: D, sourceY: $, targetX: T, targetY: H, sourcePosition: B, targetPosition: F } = ue(pe((G) => {
    const ee = G.nodeLookup.get(p.source), le = G.nodeLookup.get(p.target);
    if (!ee || !le)
      return {
        zIndex: p.zIndex,
        ...sa
      };
    const V = Ip({
      id: e,
      sourceNode: ee,
      targetNode: le,
      sourceHandle: p.sourceHandle || null,
      targetHandle: p.targetHandle || null,
      connectionMode: G.connectionMode,
      onError: m
    });
    return {
      zIndex: wp({
        selected: p.selected,
        zIndex: p.zIndex,
        sourceNode: ee,
        targetNode: le,
        elevateOnSelect: G.elevateEdgesOnSelect,
        zIndexMode: G.zIndexMode
      }),
      ...V || sa
    };
  }, [p.source, p.target, p.sourceHandle, p.targetHandle, p.selected, p.zIndex]), xe), U = ge(() => p.markerStart ? `url('#${ci(p.markerStart, y)}')` : void 0, [p.markerStart, y]), q = ge(() => p.markerEnd ? `url('#${ci(p.markerEnd, y)}')` : void 0, [p.markerEnd, y]);
  if (p.hidden || D === null || $ === null || T === null || H === null)
    return null;
  const ne = (G) => {
    const { addSelectedEdges: ee, unselectNodesAndEdges: le, multiSelectionActive: V } = j.getState();
    P && (j.setState({ nodesSelectionActive: !1 }), p.selected && V ? (le({ nodes: [], edges: [p] }), W.current?.blur()) : ee([e])), r && r(G, p);
  }, ae = i ? (G) => {
    i(G, { ...p });
  } : void 0, K = s ? (G) => {
    s(G, { ...p });
  } : void 0, L = c ? (G) => {
    c(G, { ...p });
  } : void 0, Z = d ? (G) => {
    d(G, { ...p });
  } : void 0, ie = l ? (G) => {
    l(G, { ...p });
  } : void 0, se = (G) => {
    if (!E && pc.includes(G.key) && P) {
      const { unselectNodesAndEdges: ee, addSelectedEdges: le } = j.getState();
      G.key === "Escape" ? (W.current?.blur(), ee({ edges: [p] })) : le([e]);
    }
  };
  return a.jsx("svg", { style: { zIndex: I }, children: a.jsxs("g", { className: Ie([
    "react-flow__edge",
    `react-flow__edge-${b}`,
    p.className,
    x,
    {
      selected: p.selected,
      animated: p.animated,
      inactive: !P && !r,
      updating: M,
      selectable: P
    }
  ]), onClick: ne, onDoubleClick: ae, onContextMenu: K, onMouseEnter: L, onMouseMove: Z, onMouseLeave: ie, onKeyDown: k ? se : void 0, tabIndex: k ? 0 : void 0, role: p.ariaRole ?? (k ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": p.ariaLabel === null ? void 0 : p.ariaLabel || `Edge from ${p.source} to ${p.target}`, "aria-describedby": k ? `${qc}-${y}` : void 0, ref: W, ...p.domAttributes, children: [!O && a.jsx(C, { id: e, source: p.source, target: p.target, type: p.type, selected: p.selected, animated: p.animated, selectable: P, deletable: p.deletable ?? !0, label: p.label, labelStyle: p.labelStyle, labelShowBg: p.labelShowBg, labelBgStyle: p.labelBgStyle, labelBgPadding: p.labelBgPadding, labelBgBorderRadius: p.labelBgBorderRadius, sourceX: D, sourceY: $, targetX: T, targetY: H, sourcePosition: B, targetPosition: F, data: p.data, style: p.style, sourceHandleId: p.sourceHandle, targetHandleId: p.targetHandle, markerStart: U, markerEnd: q, pathOptions: "pathOptions" in p ? p.pathOptions : void 0, interactionWidth: p.interactionWidth }), A && a.jsx(Ry, { edge: p, isReconnectable: A, reconnectRadius: f, onReconnect: u, onReconnectStart: h, onReconnectEnd: g, sourceX: D, sourceY: $, targetX: T, targetY: H, sourcePosition: B, targetPosition: F, setUpdateHover: z, setReconnecting: N })] }) });
}
var Hy = Se(Vy);
const Oy = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function bl({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: i, onEdgeContextMenu: s, onEdgeMouseEnter: c, onEdgeMouseMove: d, onEdgeMouseLeave: l, onEdgeClick: f, reconnectRadius: u, onEdgeDoubleClick: h, onReconnectStart: g, onReconnectEnd: y, disableKeyboardA11y: v }) {
  const { edgesFocusable: x, edgesReconnectable: m, elementsSelectable: E, onError: p } = ue(Oy, xe), w = Cy(t);
  return a.jsxs("div", { className: "react-flow__edges", children: [a.jsx(Ay, { defaultColor: e, rfId: n }), w.map((b) => a.jsx(Hy, { id: b, edgesFocusable: x, edgesReconnectable: m, elementsSelectable: E, noPanClassName: r, onReconnect: i, onContextMenu: s, onMouseEnter: c, onMouseMove: d, onMouseLeave: l, onClick: f, reconnectRadius: u, onDoubleClick: h, onReconnectStart: g, onReconnectEnd: y, rfId: n, onError: p, edgeTypes: o, disableKeyboardA11y: v }, b))] });
}
bl.displayName = "EdgeRenderer";
const Fy = Se(bl), By = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function Wy({ children: e }) {
  const t = ue(By);
  return a.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function Xy(e) {
  const t = Ri(), n = ce(!1);
  oe(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Yy = (e) => e.panZoom?.syncViewport;
function qy(e) {
  const t = ue(Yy), n = we();
  return oe(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function Zy(e) {
  return e.connection.inProgress ? { ...e.connection, to: rn(e.connection.to, e.transform) } : { ...e.connection };
}
function Uy(e) {
  return Zy;
}
function Ky(e) {
  const t = Uy();
  return ue(t, xe);
}
const Gy = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function Jy({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: i, height: s, isValid: c, inProgress: d } = ue(Gy, xe);
  return !(i && r && d) ? null : a.jsx("svg", { style: e, width: i, height: s, className: "react-flow__connectionline react-flow__container", children: a.jsx("g", { className: Ie(["react-flow__connection", mc(c)]), children: a.jsx(Nl, { style: t, type: n, CustomComponent: o, isValid: c }) }) });
}
const Nl = ({ style: e, type: t = gt.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: i, fromNode: s, fromHandle: c, fromPosition: d, to: l, toNode: f, toHandle: u, toPosition: h, pointer: g } = Ky();
  if (!r)
    return;
  if (n)
    return a.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: s, fromHandle: c, fromX: i.x, fromY: i.y, toX: l.x, toY: l.y, fromPosition: d, toPosition: h, connectionStatus: mc(o), toNode: f, toHandle: u, pointer: g });
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
      [y] = _c(v);
      break;
    case gt.SimpleBezier:
      [y] = ll(v);
      break;
    case gt.Step:
      [y] = Xo({
        ...v,
        borderRadius: 0
      });
      break;
    case gt.SmoothStep:
      [y] = Xo(v);
      break;
    default:
      [y] = Dc(v);
  }
  return a.jsx("path", { d: y, fill: "none", className: "react-flow__connection-path", style: e });
};
Nl.displayName = "ConnectionLine";
const Qy = {};
function la(e = Qy) {
  ce(e), we(), oe(() => {
  }, [e]);
}
function em() {
  we(), ce(!1), oe(() => {
  }, []);
}
function Sl({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: i, onEdgeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: d, onNodeMouseLeave: l, onNodeContextMenu: f, onSelectionContextMenu: u, onSelectionStart: h, onSelectionEnd: g, connectionLineType: y, connectionLineStyle: v, connectionLineComponent: x, connectionLineContainerStyle: m, selectionKeyCode: E, selectionOnDrag: p, selectionMode: w, multiSelectionKeyCode: b, panActivationKeyCode: C, zoomActivationKeyCode: k, deleteKeyCode: A, onlyRenderVisibleElements: P, elementsSelectable: W, defaultViewport: M, translateExtent: z, minZoom: O, maxZoom: N, preventScrolling: j, defaultMarkerColor: I, zoomOnScroll: D, zoomOnPinch: $, panOnScroll: T, panOnScrollSpeed: H, panOnScrollMode: B, zoomOnDoubleClick: F, panOnDrag: U, autoPanOnSelection: q, onPaneClick: ne, onPaneMouseEnter: ae, onPaneMouseMove: K, onPaneMouseLeave: L, onPaneScroll: Z, onPaneContextMenu: ie, paneClickDistance: se, nodeClickDistance: G, onEdgeContextMenu: ee, onEdgeMouseEnter: le, onEdgeMouseMove: V, onEdgeMouseLeave: J, reconnectRadius: fe, onReconnect: me, onReconnectStart: Ee, onReconnectEnd: De, noDragClassName: ye, noWheelClassName: Be, noPanClassName: rt, disableKeyboardA11y: it, nodeExtent: ze, rfId: We, viewport: Ce, onViewportChange: Fe }) {
  return la(e), la(t), em(), Xy(n), qy(Ce), a.jsx(gy, { onPaneClick: ne, onPaneMouseEnter: ae, onPaneMouseMove: K, onPaneMouseLeave: L, onPaneContextMenu: ie, onPaneScroll: Z, paneClickDistance: se, deleteKeyCode: A, selectionKeyCode: E, selectionOnDrag: p, selectionMode: w, onSelectionStart: h, onSelectionEnd: g, multiSelectionKeyCode: b, panActivationKeyCode: C, zoomActivationKeyCode: k, elementsSelectable: W, zoomOnScroll: D, zoomOnPinch: $, zoomOnDoubleClick: F, panOnScroll: T, panOnScrollSpeed: H, panOnScrollMode: B, panOnDrag: U, autoPanOnSelection: q, defaultViewport: M, translateExtent: z, minZoom: O, maxZoom: N, onSelectionContextMenu: u, preventScrolling: j, noDragClassName: ye, noWheelClassName: Be, noPanClassName: rt, disableKeyboardA11y: it, onViewportChange: Fe, isControlledViewport: !!Ce, children: a.jsxs(Wy, { children: [a.jsx(Fy, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: s, onReconnect: me, onReconnectStart: Ee, onReconnectEnd: De, onlyRenderVisibleElements: P, onEdgeContextMenu: ee, onEdgeMouseEnter: le, onEdgeMouseMove: V, onEdgeMouseLeave: J, reconnectRadius: fe, defaultMarkerColor: I, noPanClassName: rt, disableKeyboardA11y: it, rfId: We }), a.jsx(Jy, { style: v, type: y, component: x, containerStyle: m }), a.jsx("div", { className: "react-flow__edgelabel-renderer" }), a.jsx(Ey, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: i, onNodeMouseEnter: c, onNodeMouseMove: d, onNodeMouseLeave: l, onNodeContextMenu: f, nodeClickDistance: G, onlyRenderVisibleElements: P, noPanClassName: rt, noDragClassName: ye, disableKeyboardA11y: it, nodeExtent: ze, rfId: We }), a.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Sl.displayName = "GraphView";
const tm = Se(Sl), nm = Nc(), da = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: c, minZoom: d = 0.5, maxZoom: l = 2, nodeOrigin: f, nodeExtent: u, zIndexMode: h = "basic" } = {}) => {
  const g = /* @__PURE__ */ new Map(), y = /* @__PURE__ */ new Map(), v = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), m = o ?? t ?? [], E = n ?? e ?? [], p = f ?? [0, 0], w = u ?? Tn;
  Tc(v, x, m);
  const { nodesInitialized: b } = li(E, g, y, {
    nodeOrigin: p,
    nodeExtent: w,
    zIndexMode: h
  });
  let C = [0, 0, 1];
  if (s && r && i) {
    const k = Xn(g, {
      filter: (M) => !!((M.width || M.initialWidth) && (M.height || M.initialHeight))
    }), { x: A, y: P, zoom: W } = Mi(k, r, i, d, l, c?.padding ?? 0.1);
    C = [A, P, W];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: i ?? 0,
    transform: C,
    nodes: E,
    nodesInitialized: b,
    nodeLookup: g,
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
    translateExtent: Tn,
    nodeExtent: w,
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
    connection: { ...yc },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: nm,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: gc,
    zIndexMode: h,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, om = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: c, minZoom: d, maxZoom: l, nodeOrigin: f, nodeExtent: u, zIndexMode: h }) => yg((g, y) => {
  async function v() {
    const { nodeLookup: x, panZoom: m, fitViewOptions: E, fitViewResolver: p, width: w, height: b, minZoom: C, maxZoom: k } = y();
    m && (await fp({
      nodes: x,
      width: w,
      height: b,
      panZoom: m,
      minZoom: C,
      maxZoom: k
    }, E), p?.resolve(!0), g({ fitViewResolver: null }));
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
    setNodes: (x) => {
      const { nodeLookup: m, parentLookup: E, nodeOrigin: p, elevateNodesOnSelect: w, fitViewQueued: b, zIndexMode: C, nodesSelectionActive: k } = y(), { nodesInitialized: A, hasSelectedNodes: P } = li(x, m, E, {
        nodeOrigin: p,
        nodeExtent: u,
        elevateNodesOnSelect: w,
        checkEquality: !0,
        zIndexMode: C
      }), W = k && P;
      b && A ? (v(), g({
        nodes: x,
        nodesInitialized: A,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: W
      })) : g({ nodes: x, nodesInitialized: A, nodesSelectionActive: W });
    },
    setEdges: (x) => {
      const { connectionLookup: m, edgeLookup: E } = y();
      Tc(m, E, x), g({ edges: x });
    },
    setDefaultNodesAndEdges: (x, m) => {
      if (x) {
        const { setNodes: E } = y();
        E(x), g({ hasDefaultNodes: !0 });
      }
      if (m) {
        const { setEdges: E } = y();
        E(m), g({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (x) => {
      const { triggerNodeChanges: m, nodeLookup: E, parentLookup: p, domNode: w, nodeOrigin: b, nodeExtent: C, debug: k, fitViewQueued: A, zIndexMode: P } = y(), { changes: W, updatedInternals: M } = Tp(x, E, p, w, b, C, P);
      M && (Mp(E, p, { nodeOrigin: b, nodeExtent: C, zIndexMode: P }), A ? (v(), g({ fitViewQueued: !1, fitViewOptions: void 0 })) : g({}), W?.length > 0 && (k && console.log("React Flow: trigger node changes", W), m?.(W)));
    },
    updateNodePositions: (x, m = !1) => {
      const E = [];
      let p = [];
      const { nodeLookup: w, triggerNodeChanges: b, connection: C, updateConnection: k, onNodesChangeMiddlewareMap: A } = y();
      for (const [P, W] of x) {
        const M = w.get(P), z = !!(M?.expandParent && M?.parentId && W?.position), O = {
          id: P,
          type: "position",
          position: z ? {
            x: Math.max(0, W.position.x),
            y: Math.max(0, W.position.y)
          } : W.position,
          dragging: m
        };
        if (M && C.inProgress && C.fromNode.id === M.id) {
          const N = zt(M, C.fromHandle, te.Left, !0);
          k({ ...C, from: N });
        }
        z && M.parentId && E.push({
          id: P,
          parentId: M.parentId,
          rect: {
            ...W.internals.positionAbsolute,
            width: W.measured.width ?? 0,
            height: W.measured.height ?? 0
          }
        }), p.push(O);
      }
      if (E.length > 0) {
        const { parentLookup: P, nodeOrigin: W } = y(), M = Li(E, w, P, W);
        p.push(...M);
      }
      for (const P of A.values())
        p = P(p);
      b(p);
    },
    triggerNodeChanges: (x) => {
      const { onNodesChange: m, setNodes: E, nodes: p, hasDefaultNodes: w, debug: b } = y();
      if (x?.length) {
        if (w) {
          const C = Kc(x, p);
          E(C);
        }
        b && console.log("React Flow: trigger node changes", x), m?.(x);
      }
    },
    triggerEdgeChanges: (x) => {
      const { onEdgesChange: m, setEdges: E, edges: p, hasDefaultEdges: w, debug: b } = y();
      if (x?.length) {
        if (w) {
          const C = Gc(x, p);
          E(C);
        }
        b && console.log("React Flow: trigger edge changes", x), m?.(x);
      }
    },
    addSelectedNodes: (x) => {
      const { multiSelectionActive: m, edgeLookup: E, nodeLookup: p, triggerNodeChanges: w, triggerEdgeChanges: b } = y();
      if (m) {
        const C = x.map((k) => It(k, !0));
        w(C);
        return;
      }
      w(Yt(p, /* @__PURE__ */ new Set([...x]), !0)), b(Yt(E));
    },
    addSelectedEdges: (x) => {
      const { multiSelectionActive: m, edgeLookup: E, nodeLookup: p, triggerNodeChanges: w, triggerEdgeChanges: b } = y();
      if (m) {
        const C = x.map((k) => It(k, !0));
        b(C);
        return;
      }
      b(Yt(E, /* @__PURE__ */ new Set([...x]))), w(Yt(p, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: x, edges: m } = {}) => {
      const { edges: E, nodes: p, nodeLookup: w, triggerNodeChanges: b, triggerEdgeChanges: C } = y(), k = x || p, A = m || E, P = [];
      for (const M of k) {
        if (!M.selected)
          continue;
        const z = w.get(M.id);
        z && (z.selected = !1), P.push(It(M.id, !1));
      }
      const W = [];
      for (const M of A)
        M.selected && W.push(It(M.id, !1));
      b(P), C(W);
    },
    setMinZoom: (x) => {
      const { panZoom: m, maxZoom: E } = y();
      m?.setScaleExtent([x, E]), g({ minZoom: x });
    },
    setMaxZoom: (x) => {
      const { panZoom: m, minZoom: E } = y();
      m?.setScaleExtent([E, x]), g({ maxZoom: x });
    },
    setTranslateExtent: (x) => {
      y().panZoom?.setTranslateExtent(x), g({ translateExtent: x });
    },
    resetSelectedElements: () => {
      const { edges: x, nodes: m, triggerNodeChanges: E, triggerEdgeChanges: p, elementsSelectable: w } = y();
      if (!w)
        return;
      const b = m.reduce((k, A) => A.selected ? [...k, It(A.id, !1)] : k, []), C = x.reduce((k, A) => A.selected ? [...k, It(A.id, !1)] : k, []);
      E(b), p(C);
    },
    setNodeExtent: (x) => {
      const { nodes: m, nodeLookup: E, parentLookup: p, nodeOrigin: w, elevateNodesOnSelect: b, nodeExtent: C, zIndexMode: k } = y();
      x[0][0] === C[0][0] && x[0][1] === C[0][1] && x[1][0] === C[1][0] && x[1][1] === C[1][1] || (li(m, E, p, {
        nodeOrigin: w,
        nodeExtent: x,
        elevateNodesOnSelect: b,
        checkEquality: !1,
        zIndexMode: k
      }), g({ nodeExtent: x }));
    },
    panBy: (x) => {
      const { transform: m, width: E, height: p, panZoom: w, translateExtent: b } = y();
      return zp({ delta: x, panZoom: w, transform: m, translateExtent: b, width: E, height: p });
    },
    setCenter: async (x, m, E) => {
      const { width: p, height: w, maxZoom: b, panZoom: C } = y();
      if (!C)
        return !1;
      const k = typeof E?.zoom < "u" ? E.zoom : b;
      return await C.setViewport({
        x: p / 2 - x * k,
        y: w / 2 - m * k,
        zoom: k
      }, { duration: E?.duration, ease: E?.ease, interpolate: E?.interpolate }), !0;
    },
    cancelConnection: () => {
      g({
        connection: { ...yc }
      });
    },
    updateConnection: (x) => {
      g({ connection: x });
    },
    reset: () => g({ ...da() })
  };
}, Object.is);
function rm({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: i, initialMinZoom: s, initialMaxZoom: c, initialFitViewOptions: d, fitView: l, nodeOrigin: f, nodeExtent: u, zIndexMode: h, children: g }) {
  const [y] = Y(() => om({
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
  return a.jsx(vg, { value: y, children: a.jsx(Wg, { children: g }) });
}
function im({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: i, height: s, fitView: c, fitViewOptions: d, minZoom: l, maxZoom: f, nodeOrigin: u, nodeExtent: h, zIndexMode: g }) {
  return On(sr) ? a.jsx(a.Fragment, { children: e }) : a.jsx(rm, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: i, initialHeight: s, fitView: c, initialFitViewOptions: d, initialMinZoom: l, initialMaxZoom: f, nodeOrigin: u, nodeExtent: h, zIndexMode: g, children: e });
}
const sm = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function am({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: i, edgeTypes: s, onNodeClick: c, onEdgeClick: d, onInit: l, onMove: f, onMoveStart: u, onMoveEnd: h, onConnect: g, onConnectStart: y, onConnectEnd: v, onClickConnectStart: x, onClickConnectEnd: m, onNodeMouseEnter: E, onNodeMouseMove: p, onNodeMouseLeave: w, onNodeContextMenu: b, onNodeDoubleClick: C, onNodeDragStart: k, onNodeDrag: A, onNodeDragStop: P, onNodesDelete: W, onEdgesDelete: M, onDelete: z, onSelectionChange: O, onSelectionDragStart: N, onSelectionDrag: j, onSelectionDragStop: I, onSelectionContextMenu: D, onSelectionStart: $, onSelectionEnd: T, onBeforeDelete: H, connectionMode: B, connectionLineType: F = gt.Bezier, connectionLineStyle: U, connectionLineComponent: q, connectionLineContainerStyle: ne, deleteKeyCode: ae = "Backspace", selectionKeyCode: K = "Shift", selectionOnDrag: L = !1, selectionMode: Z = zn.Full, panActivationKeyCode: ie = "Space", multiSelectionKeyCode: se = Rn() ? "Meta" : "Control", zoomActivationKeyCode: G = Rn() ? "Meta" : "Control", snapToGrid: ee, snapGrid: le, onlyRenderVisibleElements: V = !1, selectNodesOnDrag: J, nodesDraggable: fe, autoPanOnNodeFocus: me, nodesConnectable: Ee, nodesFocusable: De, nodeOrigin: ye = Zc, edgesFocusable: Be, edgesReconnectable: rt, elementsSelectable: it = !0, defaultViewport: ze = Pg, minZoom: We = 0.5, maxZoom: Ce = 2, translateExtent: Fe = Tn, preventScrolling: an = !0, nodeExtent: vt, defaultMarkerColor: Kn = "#b1b1b7", zoomOnScroll: cn = !0, zoomOnPinch: Vt = !0, panOnScroll: ut = !1, panOnScrollSpeed: bt = 0.5, panOnScrollMode: Nt = At.Free, zoomOnDoubleClick: Qe = !0, panOnDrag: Gn = !0, onPaneClick: Le, onPaneMouseEnter: Jn, onPaneMouseMove: be, onPaneMouseLeave: Xe, onPaneScroll: ln, onPaneContextMenu: Ne, paneClickDistance: St = 1, nodeClickDistance: dn = 0, children: _e, onReconnect: Et, onReconnectStart: ft, onReconnectEnd: hr, onEdgeContextMenu: Qn, onEdgeDoubleClick: eo, onEdgeMouseEnter: un, onEdgeMouseMove: pr, onEdgeMouseLeave: fn, reconnectRadius: Ht = 10, onNodesChange: Re, onEdgesChange: hn, noDragClassName: pn = "nodrag", noWheelClassName: gn = "nowheel", noPanClassName: Ct = "nopan", fitView: to, fitViewOptions: no, connectOnClick: gr, attributionPosition: yr, proOptions: oo, defaultEdgeOptions: ro, elevateNodesOnSelect: io = !0, elevateEdgesOnSelect: mr = !1, disableKeyboardA11y: Ot = !1, autoPanOnConnect: xr, autoPanOnNodeDrag: wr, autoPanOnSelection: vr = !0, autoPanSpeed: br, connectionRadius: Nr, isValidConnection: yn, onError: Sr, style: Er, id: so, nodeDragThreshold: Cr, connectionDragThreshold: kr, viewport: Ir, onViewportChange: jr, width: ao, height: co, colorMode: _r = "light", debug: Ar, onScroll: lo, ariaLabelConfig: Mr, zIndexMode: uo = "basic", ...fo }, ho) {
  const Ft = so || "1", po = Lg(_r), go = pe((yo) => {
    yo.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), lo?.(yo);
  }, [lo]);
  return a.jsx("div", { "data-testid": "rf__wrapper", ...fo, onScroll: go, style: { ...Er, ...sm }, ref: ho, className: Ie(["react-flow", r, po]), id: so, role: "application", children: a.jsxs(im, { nodes: e, edges: t, width: ao, height: co, fitView: to, fitViewOptions: no, minZoom: We, maxZoom: Ce, nodeOrigin: ye, nodeExtent: vt, zIndexMode: uo, children: [a.jsx(zg, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: g, onConnectStart: y, onConnectEnd: v, onClickConnectStart: x, onClickConnectEnd: m, nodesDraggable: fe, autoPanOnNodeFocus: me, nodesConnectable: Ee, nodesFocusable: De, edgesFocusable: Be, edgesReconnectable: rt, elementsSelectable: it, elevateNodesOnSelect: io, elevateEdgesOnSelect: mr, minZoom: We, maxZoom: Ce, nodeExtent: vt, onNodesChange: Re, onEdgesChange: hn, snapToGrid: ee, snapGrid: le, connectionMode: B, translateExtent: Fe, connectOnClick: gr, defaultEdgeOptions: ro, fitView: to, fitViewOptions: no, onNodesDelete: W, onEdgesDelete: M, onDelete: z, onNodeDragStart: k, onNodeDrag: A, onNodeDragStop: P, onSelectionDrag: j, onSelectionDragStart: N, onSelectionDragStop: I, onMove: f, onMoveStart: u, onMoveEnd: h, noPanClassName: Ct, nodeOrigin: ye, rfId: Ft, autoPanOnConnect: xr, autoPanOnNodeDrag: wr, autoPanSpeed: br, onError: Sr, connectionRadius: Nr, isValidConnection: yn, selectNodesOnDrag: J, nodeDragThreshold: Cr, connectionDragThreshold: kr, onBeforeDelete: H, debug: Ar, ariaLabelConfig: Mr, zIndexMode: uo }), a.jsx(tm, { onInit: l, onNodeClick: c, onEdgeClick: d, onNodeMouseEnter: E, onNodeMouseMove: p, onNodeMouseLeave: w, onNodeContextMenu: b, onNodeDoubleClick: C, nodeTypes: i, edgeTypes: s, connectionLineType: F, connectionLineStyle: U, connectionLineComponent: q, connectionLineContainerStyle: ne, selectionKeyCode: K, selectionOnDrag: L, selectionMode: Z, deleteKeyCode: ae, multiSelectionKeyCode: se, panActivationKeyCode: ie, zoomActivationKeyCode: G, onlyRenderVisibleElements: V, defaultViewport: ze, translateExtent: Fe, minZoom: We, maxZoom: Ce, preventScrolling: an, zoomOnScroll: cn, zoomOnPinch: Vt, zoomOnDoubleClick: Qe, panOnScroll: ut, panOnScrollSpeed: bt, panOnScrollMode: Nt, panOnDrag: Gn, autoPanOnSelection: vr, onPaneClick: Le, onPaneMouseEnter: Jn, onPaneMouseMove: be, onPaneMouseLeave: Xe, onPaneScroll: ln, onPaneContextMenu: Ne, paneClickDistance: St, nodeClickDistance: dn, onSelectionContextMenu: D, onSelectionStart: $, onSelectionEnd: T, onReconnect: Et, onReconnectStart: ft, onReconnectEnd: hr, onEdgeContextMenu: Qn, onEdgeDoubleClick: eo, onEdgeMouseEnter: un, onEdgeMouseMove: pr, onEdgeMouseLeave: fn, reconnectRadius: Ht, defaultMarkerColor: Kn, noDragClassName: pn, noWheelClassName: gn, noPanClassName: Ct, rfId: Ft, disableKeyboardA11y: Ot, nodeExtent: vt, viewport: Ir, onViewportChange: jr }), a.jsx(Dg, { onSelectionChange: O }), _e, a.jsx(Ig, { proOptions: oo, position: yr }), a.jsx(kg, { rfId: Ft, disableKeyboardA11y: Ot })] }) });
}
var El = el(am);
const cm = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function lm({ children: e }) {
  const t = ue(cm);
  return t ? wg.createPortal(e, t) : null;
}
function dm({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return a.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ie(["react-flow__background-pattern", n, o]) });
}
function um({ radius: e, className: t }) {
  return a.jsx("circle", { cx: e, cy: e, r: e, className: Ie(["react-flow__background-pattern", "dots", t]) });
}
var yt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(yt || (yt = {}));
const fm = {
  [yt.Dots]: 1,
  [yt.Lines]: 1,
  [yt.Cross]: 6
}, hm = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function Cl({
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
  const u = ce(null), { transform: h, patternId: g } = ue(hm, xe), y = o || fm[t], v = t === yt.Dots, x = t === yt.Cross, m = Array.isArray(n) ? n : [n, n], E = [m[0] * h[2] || 1, m[1] * h[2] || 1], p = y * h[2], w = Array.isArray(i) ? i : [i, i], b = x ? [p, p] : E, C = [
    w[0] * h[2] || 1 + b[0] / 2,
    w[1] * h[2] || 1 + b[1] / 2
  ], k = `${g}${e || ""}`;
  return a.jsxs("svg", { className: Ie(["react-flow__background", l]), style: {
    ...d,
    ...cr,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": s
  }, ref: u, "data-testid": "rf__background", children: [a.jsx("pattern", { id: k, x: h[0] % E[0], y: h[1] % E[1], width: E[0], height: E[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${C[0]},-${C[1]})`, children: v ? a.jsx(um, { radius: p / 2, className: f }) : a.jsx(dm, { dimensions: b, lineWidth: r, variant: t, className: f }) }), a.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${k})` })] });
}
Cl.displayName = "Background";
const kl = Se(Cl);
function pm() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: a.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function gm() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: a.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function ym() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: a.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function mm() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: a.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function xm() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: a.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function Co({ children: e, className: t, ...n }) {
  return a.jsx("button", { type: "button", className: Ie(["react-flow__controls-button", t]), ...n, children: e });
}
const wm = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function Il({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: i, onZoomOut: s, onFitView: c, onInteractiveChange: d, className: l, children: f, position: u = "bottom-left", orientation: h = "vertical", "aria-label": g }) {
  const y = we(), { isInteractive: v, minZoomReached: x, maxZoomReached: m, ariaLabelConfig: E } = ue(wm, xe), { zoomIn: p, zoomOut: w, fitView: b } = Ri(), C = () => {
    p(), i?.();
  }, k = () => {
    w(), s?.();
  }, A = () => {
    b(r), c?.();
  }, P = () => {
    y.setState({
      nodesDraggable: !v,
      nodesConnectable: !v,
      elementsSelectable: !v
    }), d?.(!v);
  }, W = h === "horizontal" ? "horizontal" : "vertical";
  return a.jsxs(ar, { className: Ie(["react-flow__controls", W, l]), position: u, style: e, "data-testid": "rf__controls", "aria-label": g ?? E["controls.ariaLabel"], children: [t && a.jsxs(a.Fragment, { children: [a.jsx(Co, { onClick: C, className: "react-flow__controls-zoomin", title: E["controls.zoomIn.ariaLabel"], "aria-label": E["controls.zoomIn.ariaLabel"], disabled: m, children: a.jsx(pm, {}) }), a.jsx(Co, { onClick: k, className: "react-flow__controls-zoomout", title: E["controls.zoomOut.ariaLabel"], "aria-label": E["controls.zoomOut.ariaLabel"], disabled: x, children: a.jsx(gm, {}) })] }), n && a.jsx(Co, { className: "react-flow__controls-fitview", onClick: A, title: E["controls.fitView.ariaLabel"], "aria-label": E["controls.fitView.ariaLabel"], children: a.jsx(ym, {}) }), o && a.jsx(Co, { className: "react-flow__controls-interactive", onClick: P, title: E["controls.interactive.ariaLabel"], "aria-label": E["controls.interactive.ariaLabel"], children: v ? a.jsx(xm, {}) : a.jsx(mm, {}) }), f] });
}
Il.displayName = "Controls";
const jl = Se(Il);
function vm({ id: e, x: t, y: n, width: o, height: r, style: i, color: s, strokeColor: c, strokeWidth: d, className: l, borderRadius: f, shapeRendering: u, selected: h, onClick: g }) {
  const { background: y, backgroundColor: v } = i || {}, x = s || y || v;
  return a.jsx("rect", { className: Ie(["react-flow__minimap-node", { selected: h }, l]), x: t, y: n, rx: f, ry: f, width: o, height: r, style: {
    fill: x,
    stroke: c,
    strokeWidth: d
  }, shapeRendering: u, onClick: g ? (m) => g(m, e) : void 0 });
}
const bm = Se(vm), Nm = (e) => e.nodes.map((t) => t.id), Ur = (e) => e instanceof Function ? e : () => e;
function Sm({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: i = bm,
  onClick: s
}) {
  const c = ue(Nm, xe), d = Ur(t), l = Ur(e), f = Ur(n), u = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
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
function Em({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: i, shapeRendering: s, NodeComponent: c, onClick: d }) {
  const { node: l, x: f, y: u, width: h, height: g } = ue((y) => {
    const v = y.nodeLookup.get(e);
    if (!v)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const x = v.internals.userNode, { x: m, y: E } = v.internals.positionAbsolute, { width: p, height: w } = dt(x);
    return {
      node: x,
      x: m,
      y: E,
      width: p,
      height: w
    };
  }, xe);
  return !l || l.hidden || !Sc(l) ? null : a.jsx(c, { x: f, y: u, width: h, height: g, style: l.style, selected: !!l.selected, className: o(l), color: t(l), borderRadius: r, strokeColor: n(l), strokeWidth: i, shapeRendering: s, onClick: d, id: l.id });
}
const Cm = Se(Em);
var km = Se(Sm);
const Im = 200, jm = 150, _m = (e) => !e.hidden, Am = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? bc(Xn(e.nodeLookup, { filter: _m }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, Mm = "react-flow__minimap-desc";
function _l({
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
  zoomable: x = !1,
  ariaLabel: m,
  inversePan: E,
  zoomStep: p = 1,
  offsetScale: w = 5
}) {
  const b = we(), C = ce(null), { boundingRect: k, viewBB: A, rfId: P, panZoom: W, translateExtent: M, flowWidth: z, flowHeight: O, ariaLabelConfig: N } = ue(Am, xe), j = e?.width ?? Im, I = e?.height ?? jm, D = k.width / j, $ = k.height / I, T = Math.max(D, $), H = T * j, B = T * I, F = w * T, U = k.x - (H - k.width) / 2 - F, q = k.y - (B - k.height) / 2 - F, ne = H + F * 2, ae = B + F * 2, K = `${Mm}-${P}`, L = ce(0), Z = ce();
  L.current = T, oe(() => {
    if (C.current && W)
      return Z.current = Xp({
        domNode: C.current,
        panZoom: W,
        getTransform: () => b.getState().transform,
        getViewScale: () => L.current
      }), () => {
        Z.current?.destroy();
      };
  }, [W]), oe(() => {
    Z.current?.update({
      translateExtent: M,
      width: z,
      height: O,
      inversePan: E,
      pannable: v,
      zoomStep: p,
      zoomable: x
    });
  }, [v, x, E, p, M, z, O]);
  const ie = g ? (ee) => {
    const [le, V] = Z.current?.pointer(ee) || [0, 0];
    g(ee, { x: le, y: V });
  } : void 0, se = y ? pe((ee, le) => {
    const V = b.getState().nodeLookup.get(le).internals.userNode;
    y(ee, V);
  }, []) : void 0, G = m ?? N["minimap.ariaLabel"];
  return a.jsx(ar, { position: h, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof f == "string" ? f : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof u == "number" ? u * T : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof s == "number" ? s : void 0
  }, className: Ie(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: a.jsxs("svg", { width: j, height: I, viewBox: `${U} ${q} ${ne} ${ae}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": K, ref: C, onClick: ie, children: [G && a.jsx("title", { id: K, children: G }), a.jsx(km, { onClick: se, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: i, nodeClassName: r, nodeStrokeWidth: s, nodeComponent: c }), a.jsx("path", { className: "react-flow__minimap-mask", d: `M${U - F},${q - F}h${ne + F * 2}v${ae + F * 2}h${-ne - F * 2}z
        M${A.x},${A.y}h${A.width}v${A.height}h${-A.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
_l.displayName = "MiniMap";
const Al = Se(_l), Dm = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, Pm = {
  [nn.Line]: "right",
  [nn.Handle]: "bottom-right"
};
function $m({ nodeId: e, position: t, variant: n = nn.Handle, className: o, style: r = void 0, children: i, color: s, minWidth: c = 10, minHeight: d = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: f = Number.MAX_VALUE, keepAspectRatio: u = !1, resizeDirection: h, autoScale: g = !0, shouldResize: y, onResizeStart: v, onResize: x, onResizeEnd: m }) {
  const E = rl(), p = typeof e == "string" ? e : E, w = we(), b = ce(null), C = n === nn.Handle, k = ue(pe(Dm(C && g), [C, g]), xe), A = ce(null), P = t ?? Pm[n];
  oe(() => {
    if (!(!b.current || !p))
      return A.current || (A.current = rg({
        domNode: b.current,
        nodeId: p,
        getStoreItems: () => {
          const { nodeLookup: M, transform: z, snapGrid: O, snapToGrid: N, nodeOrigin: j, domNode: I } = w.getState();
          return {
            nodeLookup: M,
            transform: z,
            snapGrid: O,
            snapToGrid: N,
            nodeOrigin: j,
            paneDomNode: I
          };
        },
        onChange: (M, z) => {
          const { triggerNodeChanges: O, nodeLookup: N, parentLookup: j, nodeOrigin: I } = w.getState(), D = [], $ = { x: M.x, y: M.y }, T = N.get(p);
          if (T && T.expandParent && T.parentId) {
            const H = T.origin ?? I, B = M.width ?? T.measured.width ?? 0, F = M.height ?? T.measured.height ?? 0, U = {
              id: T.id,
              parentId: T.parentId,
              rect: {
                width: B,
                height: F,
                ...Ec({
                  x: M.x ?? T.position.x,
                  y: M.y ?? T.position.y
                }, { width: B, height: F }, T.parentId, N, H)
              }
            }, q = Li([U], N, j, I);
            D.push(...q), $.x = M.x ? Math.max(H[0] * B, M.x) : void 0, $.y = M.y ? Math.max(H[1] * F, M.y) : void 0;
          }
          if ($.x !== void 0 && $.y !== void 0) {
            const H = {
              id: p,
              type: "position",
              position: { ...$ }
            };
            D.push(H);
          }
          if (M.width !== void 0 && M.height !== void 0) {
            const B = {
              id: p,
              type: "dimensions",
              resizing: !0,
              setAttributes: h ? h === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: M.width,
                height: M.height
              }
            };
            D.push(B);
          }
          for (const H of z) {
            const B = {
              ...H,
              type: "position"
            };
            D.push(B);
          }
          O(D);
        },
        onEnd: ({ width: M, height: z }) => {
          const O = {
            id: p,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: M,
              height: z
            }
          };
          w.getState().triggerNodeChanges([O]);
        }
      })), A.current.update({
        controlPosition: P,
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
    P,
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
  const W = P.split("-");
  return a.jsx("div", { className: Ie(["react-flow__resize-control", "nodrag", ...W, n, o]), ref: b, style: {
    ...r,
    scale: k,
    ...s && { [C ? "backgroundColor" : "borderColor"]: s }
  }, children: i });
}
Se($m);
const Tm = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), Ml = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var zm = {
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
const Lm = Go(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: o,
    className: r = "",
    children: i,
    iconNode: s,
    ...c
  }, d) => Jr(
    "svg",
    {
      ref: d,
      ...zm,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: Ml("lucide", r),
      ...c
    },
    [
      ...s.map(([l, f]) => Jr(l, f)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const ve = (e, t) => {
  const n = Go(
    ({ className: o, ...r }, i) => Jr(Lm, {
      ref: i,
      iconNode: t,
      className: Ml(`lucide-${Tm(e)}`, o),
      ...r
    })
  );
  return n.displayName = `${e}`, n;
};
const Dl = ve("Boxes", [
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
const sn = ve("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const Pl = ve("ChevronDown", [
  ["path", { d: "m6 9 6 6 6-6", key: "qrunsl" }]
]);
const qo = ve("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const Wt = ve("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const nt = ve("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const Rm = ve("Copy", [
  ["rect", { width: "14", height: "14", x: "8", y: "8", rx: "2", ry: "2", key: "17jyea" }],
  ["path", { d: "M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2", key: "zix9uf" }]
]);
const $l = ve("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const Vm = ve("GripVertical", [
  ["circle", { cx: "9", cy: "12", r: "1", key: "1vctgf" }],
  ["circle", { cx: "9", cy: "5", r: "1", key: "hp0tcf" }],
  ["circle", { cx: "9", cy: "19", r: "1", key: "fkjjf6" }],
  ["circle", { cx: "15", cy: "12", r: "1", key: "1tmaij" }],
  ["circle", { cx: "15", cy: "5", r: "1", key: "19l28e" }],
  ["circle", { cx: "15", cy: "19", r: "1", key: "f4zoj3" }]
]);
const Tl = ve("ListTree", [
  ["path", { d: "M21 12h-8", key: "1bmf0i" }],
  ["path", { d: "M21 6H8", key: "1pqkrb" }],
  ["path", { d: "M21 18h-8", key: "1tm79t" }],
  ["path", { d: "M3 6v4c0 1.1.9 2 2 2h3", key: "1ywdgy" }],
  ["path", { d: "M3 10v6c0 1.1.9 2 2 2h3", key: "2wc746" }]
]);
const Zo = ve("Maximize2", [
  ["polyline", { points: "15 3 21 3 21 9", key: "mznyad" }],
  ["polyline", { points: "9 21 3 21 3 15", key: "1avn1i" }],
  ["line", { x1: "21", x2: "14", y1: "3", y2: "10", key: "ota7mn" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const ua = ve("Minimize2", [
  ["polyline", { points: "4 14 10 14 10 20", key: "11kfnr" }],
  ["polyline", { points: "20 10 14 10 14 4", key: "rlmsce" }],
  ["line", { x1: "14", x2: "21", y1: "10", y2: "3", key: "o5lafz" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const Hm = ve("Package", [
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
const lr = ve("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const Hi = ve("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const Oi = ve("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const Om = ve("Save", [
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
const zl = ve("Search", [
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
const Fm = ve("Terminal", [
  ["polyline", { points: "4 17 10 11 4 5", key: "akl6gq" }],
  ["line", { x1: "12", x2: "20", y1: "19", y2: "19", key: "q2wloq" }]
]);
const fi = ve("Trash2", [
  ["path", { d: "M3 6h18", key: "d0wm0j" }],
  ["path", { d: "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", key: "4alrt4" }],
  ["path", { d: "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", key: "v07s0e" }],
  ["line", { x1: "10", x2: "10", y1: "11", y2: "17", key: "1uufr5" }],
  ["line", { x1: "14", x2: "14", y1: "11", y2: "17", key: "xtxkd" }]
]);
const Ll = ve("X", [
  ["path", { d: "M18 6 6 18", key: "1bl5f8" }],
  ["path", { d: "m6 6 12 12", key: "d8bk6v" }]
]);
const Bm = ve("Zap", [
  [
    "path",
    {
      d: "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z",
      key: "1xq2db"
    }
  ]
]), Te = "/_elsa/workflow-management", Wm = "/publishing";
async function Xm(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${Te}/definitions?${n.toString()}`);
}
async function Ym(e, t) {
  return e.http.getJson(`${Te}/definitions/${encodeURIComponent(t)}`);
}
async function qm(e, t) {
  return e.http.getJson(`${Te}/versions/${encodeURIComponent(t)}`);
}
async function Zm(e, t) {
  return e.http.postJson(`${Te}/definitions`, t);
}
async function Um(e, t) {
  await e.http.deleteJson(`${Te}/definitions/${encodeURIComponent(t)}`);
}
async function Km(e, t) {
  await e.http.postJson(`${Te}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function Gm(e, t) {
  await e.http.deleteJson(`${Te}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function Jm(e, t) {
  return e.http.putJson(`${Te}/drafts/${encodeURIComponent(t.id)}`, { state: t.state, layout: t.layout });
}
async function Qm(e, t) {
  return e.http.postJson(`${Te}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function ex(e, t) {
  return e.http.postJson(`${Te}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function tx(e, t) {
  try {
    return await e.http.postJson(`${Wm}/workflows/drafts/test-runs`, t);
  } catch (n) {
    const o = sx(n);
    if (o) return o;
    throw n;
  }
}
async function Rl(e, t) {
  return e.http.postJson(`${Te}/executables/${encodeURIComponent(t)}/run`, {});
}
async function Vl(e) {
  return e.http.getJson("/_demo/workflows/executables");
}
async function nx(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const o = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${o ? `?${o}` : ""}`);
}
async function ox(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function Fi(e) {
  return e.http.getJson(`${Te}/activities`);
}
async function rx(e) {
  const t = await Hl(e, [
    `${Te}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? fa(t) : fa(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function ix(e) {
  const t = await Hl(e, [
    `${Te}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : Po;
}
async function Hl(e, t) {
  let n;
  for (const o of t)
    try {
      return await e.http.getJson(o);
    } catch (r) {
      n = r;
    }
  throw n;
}
function fa(e) {
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
function sx(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = ha(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return ha(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function ha(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const Po = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
], dr = "elsa.sequence.structure", Zn = "elsa.flowchart.structure";
function Ol(e, t) {
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
function An(e, t) {
  const n = Ol(e, t);
  if (!n) return null;
  let o = Ke(n)[0];
  return o ? { owner: n, slot: o } : null;
}
function Ke(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = kx(t), r = Kr(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: Ix(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, i]) => Kr(i)).map(([i, s]) => ({
    id: `${t.kind}:${i}`,
    label: _x(i),
    property: i,
    mode: "generic",
    activities: Kr(s) ?? []
  }));
}
function Fl(e, t, n) {
  const o = new Map(t.map((s) => [s.activityVersionId, s])), r = new Map(n.map((s) => [s.nodeId, s])), i = e.slot.activities.map((s, c) => {
    const d = o.get(s.activityVersionId), l = r.get(s.nodeId) ?? jx(e.slot.mode, c);
    return Xl(s, d, { x: l.x, y: l.y });
  });
  return {
    nodes: i,
    edges: e.slot.mode === "flowchart" ? xx(e.owner) : mx(e.slot, i)
  };
}
function hi(e, t, n) {
  const o = t.find((i) => i.activityVersionId === e.activityVersionId), r = n.find((i) => i.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [Xl(e, o, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function ax(e, t, n, o = null) {
  const r = new Map(t.map((c) => [c.activityExecutionId, c])), i = ga(t, (c) => c.authoredActivityId || c.executableNodeId), s = ga(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? r.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const d = i.get(c.id) ?? [], l = s.get(c.id) ?? [];
    if (d.length === 0 && l.length === 0) return c;
    const f = Sx(d), u = o === c.id || d.some((g) => g.activityExecutionId === o) || l.some((g) => g.incidentId === o), h = {
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
  return e?.structure?.kind === Zn || fx(t) ? "flowchart" : e?.structure?.kind === dr || hx(t) ? "sequence" : "unsupported";
}
function pi(e, t, n) {
  if (t.length === 0) {
    const c = Ke(e)[0];
    return c ? Hn(e, c, n) : e;
  }
  const [o, ...r] = t, i = Ke(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((c) => c.nodeId === o.ownerNodeId ? pi(c, r, n) : c);
  return Hn(e, i, s);
}
function Bl(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, i = Ke(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((c) => c.nodeId === o.ownerNodeId ? Bl(c, r, n) : c);
  return Hn(e, i, s);
}
function Wl(e, t, n) {
  if (e.nodeId === t) return n(e);
  const o = Ke(e);
  if (o.length === 0) return e;
  let r = !1, i = e;
  for (const s of o) {
    const c = s.activities.map((d) => {
      const l = Wl(d, t, n);
      return l !== d && (r = !0), l;
    });
    r && (i = Hn(i, s, c));
  }
  return r ? i : e;
}
function Hn(e, t, n) {
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
function cx(e, t, n, o = []) {
  const r = new Map(e.slot.activities.map((s) => [s.nodeId, s]));
  for (const s of o)
    r.set(s.nodeId, s);
  const i = t.map((s) => r.get(s.id)).filter((s) => !!s);
  return e.slot.mode === "sequence" && i.sort((s, c) => {
    const d = t.find((f) => f.id === s.nodeId), l = t.find((f) => f.id === c.nodeId);
    return (d?.position.x ?? 0) - (l?.position.x ?? 0);
  }), Hn(e.owner, e.slot, i);
}
function lx(e, t) {
  return {
    ...e,
    structure: yx(e.structure, t)
  };
}
function dx(e, t) {
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
function gi(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: gx(e)
  };
}
function je(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? px(t) : n;
}
function Xl(e, t, n, o = {}) {
  return {
    id: e.nodeId,
    type: "workflowActivity",
    position: n,
    connectable: o.connectable,
    deletable: o.deletable,
    draggable: o.draggable,
    data: {
      label: t ? je(t) : e.activityVersionId,
      activityVersionId: e.activityVersionId,
      activityTypeKey: t?.activityTypeKey,
      category: t?.category,
      executionType: t?.executionType,
      icon: yi(t),
      childSlots: Ke(e),
      acceptsInbound: wx(e, t),
      sourcePorts: o.suppressFlowPorts ? [] : Yl(e, t),
      suppressFlowPorts: o.suppressFlowPorts
    }
  };
}
function yi(e) {
  if (!e) return "activity";
  const t = ux(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), o = je(e).toLowerCase(), r = e.category?.toLowerCase() ?? "", i = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || o === "flowchart" ? "flowchart" : n.endsWith(".sequence") || o === "sequence" ? "sequence" : n.includes("writeline") || o.includes("write line") ? "terminal" : r.includes("runtime") ? "runtime" : i === "trigger" ? "trigger" : "activity";
}
function ux(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function fx(e) {
  return !!e && (je(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function hx(e) {
  return !!e && (je(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function px(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function gx(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: dr,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: Zn,
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
function yx(e, t) {
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
function mx(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function xx(e) {
  if (e.structure?.kind !== Zn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, i = n.target;
    if (!r?.nodeId || !i?.nodeId) return null;
    const s = Array.isArray(n.vertices) ? n.vertices.filter(Ex) : [];
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
function Yl(e, t) {
  const n = pa(e.cases);
  if (bx(e, t) && n.length > 0)
    return [...n.map((i) => ({ name: i, displayName: i })), { name: "Default", displayName: "Default" }];
  const o = [
    ...$o(t?.designFacets),
    ...$o(t?.ports),
    ...$o(t?.outputs)
  ];
  if (o.length > 0) return Nx(o);
  const r = pa(e.outcomes);
  return r.length > 0 ? r.map((i) => ({ name: i, displayName: i })) : [{ name: "Done", displayName: "Done" }];
}
function wx(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function Uo(e, t, n, o) {
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
function vx(e, t, n) {
  const o = Uo(t.source, n, t.sourceHandle ?? "Done", void 0), r = Uo(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((i) => i.id !== t.id).concat(o, r);
}
function Kr(e) {
  return Array.isArray(e) ? e.filter(Cx) : null;
}
function bx(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function $o(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!Wi(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...$o(n.ports));
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
function Nx(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function pa(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function ga(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const o of e) {
    const r = t(o);
    r && n.set(r, [...n.get(r) ?? [], o]);
  }
  return n;
}
function Sx(e) {
  return [...e].sort((t, n) => ya(n).localeCompare(ya(t)))[0];
}
function ya(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function Ex(e) {
  return Wi(e) && typeof e.x == "number" && typeof e.y == "number";
}
function Wi(e) {
  return typeof e == "object" && e !== null;
}
function Cx(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function kx(e) {
  return e.kind === dr ? "sequence" : e.kind === Zn ? "flowchart" : "generic";
}
function Ix(e) {
  return e.kind === dr || e.kind === Zn, "Activities";
}
function jx(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function _x(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Ax = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function ql(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function Xi(e) {
  return ql(e.name);
}
function Mx(e, t) {
  const n = Xi(t), o = e[n];
  return t.isWrapped === !1 ? o ?? t.defaultValue ?? "" : Ul(o, t);
}
function Zl(e, t) {
  return Ul(e[Xi(t)], t);
}
function Dx(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function Px(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function ma(e, t, n) {
  return {
    ...e,
    [Xi(t)]: n
  };
}
function $x(e, t) {
  return t.isWrapped === !1 ? Mx(e, t) : Zl(e, t).expression.value;
}
function Ul(e, t) {
  return Tx(e) ? {
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
function Tx(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
const Kl = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]);
function zx({
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
  const d = Fx(c), l = r.length > 0 ? r : Ax;
  return /* @__PURE__ */ a.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ a.jsx("span", { className: "wf-section-label", children: "Properties" }),
    d.map((f) => /* @__PURE__ */ a.jsxs("section", { className: "wf-property-group", children: [
      d.length > 1 ? /* @__PURE__ */ a.jsx("h4", { children: f.category }) : null,
      f.inputs.map((u) => /* @__PURE__ */ a.jsx(
        Lx,
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
function Lx({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: o,
  expressionDescriptors: r,
  onChange: i
}) {
  const s = t.isReadOnly === !0, c = { activity: e, expressionDescriptors: r, readOnly: s }, d = Hx(n, t, c), l = d?.component, f = t.isWrapped !== !1 ? Zl(e, t) : null, u = f?.expression.type ?? "Literal", h = $x(e, t), g = f ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: r,
    readOnly: s,
    surface: "inline",
    syntax: u
  } : null, y = g ? Gl(o, g) : null, v = y?.surfaces.inline, x = y && g ? Jl(y, g, h) : [], m = !!(f && Bx(t, d?.id)), E = !!(f && Wx(t, d?.id)), [p, w] = Y(!1), b = (A) => {
    const P = f ? Dx(f, A) : A;
    i(ma(e, t, P));
  }, C = (A) => {
    f && i(ma(e, t, Px(f, A)));
  }, k = v && g ? /* @__PURE__ */ a.jsx(
    v,
    {
      descriptor: t,
      syntax: u,
      value: h,
      disabled: s,
      context: g,
      onChange: b
    }
  ) : Vx(l, t, h, s, c, b);
  return /* @__PURE__ */ a.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ a.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ a.jsx("span", { children: Ql(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ a.jsx("p", { children: t.description }) : null,
    f && !m ? /* @__PURE__ */ a.jsx(
      mi,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: u,
        descriptors: r,
        disabled: s,
        onChange: C
      }
    ) : null,
    m ? /* @__PURE__ */ a.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-expression-editor", children: [
        k,
        xi(x)
      ] }),
      /* @__PURE__ */ a.jsx(
        mi,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: u,
          descriptors: r,
          disabled: s,
          variant: "inline",
          onChange: C
        }
      ),
      E ? /* @__PURE__ */ a.jsx(
        "button",
        {
          type: "button",
          className: "wf-expression-expand-button",
          "aria-label": `Open expanded ${t.displayName || t.name} editor`,
          title: "Open expanded editor",
          onClick: () => w(!0),
          children: /* @__PURE__ */ a.jsx(Zo, { size: 13 })
        }
      ) : null
    ] }) : /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
      k,
      xi(x)
    ] }),
    E && !m ? /* @__PURE__ */ a.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => w(!0),
        children: [
          /* @__PURE__ */ a.jsx(Zo, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    p ? /* @__PURE__ */ a.jsx(
      Rx,
      {
        input: t,
        value: h,
        syntax: u,
        descriptors: r,
        activity: e,
        expressionEditors: o,
        disabled: s,
        onChange: b,
        onSyntaxChange: C,
        onClose: () => w(!1)
      }
    ) : null
  ] });
}
function Rx({
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
  const f = La(), u = e.displayName || e.name, h = {
    activity: r,
    descriptor: e,
    expressionDescriptors: o,
    readOnly: s,
    surface: "expanded",
    syntax: n
  }, g = Gl(i, h), y = g?.surfaces.expanded, v = g ? Jl(g, h, t) : [], x = y ? null : Ox(i, h);
  return oe(() => {
    const m = (E) => {
      E.key === "Escape" && l();
    };
    return window.addEventListener("keydown", m), () => window.removeEventListener("keydown", m);
  }, [l]), /* @__PURE__ */ a.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ a.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": f, children: [
    /* @__PURE__ */ a.jsxs("header", { children: [
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ a.jsx("h3", { id: f, children: u })
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", "aria-label": `Close ${u} editor`, onClick: l, children: /* @__PURE__ */ a.jsx(Ll, { size: 16 }) })
    ] }),
    /* @__PURE__ */ a.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ a.jsx(
          mi,
          {
            label: `${u} expression syntax`,
            value: n,
            descriptors: o,
            disabled: s,
            onChange: d
          }
        ),
        /* @__PURE__ */ a.jsx("span", { children: Ql(e.typeName) })
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
      xi(v)
    ] }),
    /* @__PURE__ */ a.jsxs("footer", { children: [
      /* @__PURE__ */ a.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: l, children: "Close" })
    ] })
  ] }) });
}
function Vx(e, t, n, o, r, i) {
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
function mi({
  label: e,
  value: t,
  descriptors: n,
  disabled: o,
  variant: r = "block",
  onChange: i
}) {
  const [s, c] = Y(!1), d = La(), l = n.find((u) => u.type === t), f = [
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
function Gl(e, t) {
  return [...e].sort((n, o) => (n.order ?? 500) - (o.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function Jl(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function Ox(e, t) {
  if (t.syntax.toLowerCase() === "literal") return null;
  const n = [...e].sort((s, c) => (s.order ?? 500) - (c.order ?? 500)).find((s) => s.supports(t) && s.metadata)?.metadata;
  if (!n) return `No enhanced editor is registered for ${t.syntax}. Using the generic text editor.`;
  const o = n.displayName?.trim() || "enhanced editor", r = n.installHint?.trim(), i = `No ${o} is registered for ${t.syntax}. Using the generic text editor.`;
  return r ? `${i} ${r}` : i;
}
function xi(e) {
  return e.length === 0 ? null : /* @__PURE__ */ a.jsx("div", { className: "wf-expression-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const o = t.severity ?? "info";
    return /* @__PURE__ */ a.jsxs("p", { className: `wf-expression-editor-diagnostic ${o}`, children: [
      t.code ? /* @__PURE__ */ a.jsx("span", { children: t.code }) : null,
      t.message
    ] }, `${t.code ?? "diagnostic"}-${n}`);
  }) });
}
function Fx(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "General";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return [...t.entries()].map(([n, o]) => ({ category: n, inputs: o }));
}
function Ql(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function Bx(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !Kl.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function Wx(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !Kl.has(t) && n !== "multiline") return !1;
  const o = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(o) || n === "singleline" || n === "multiline";
}
const xa = "elsa-studio:apply-workflow-graph-operation-batch", wa = "elsa-studio:undo-workflow-graph-operation-batch", Xx = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function Yx(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const o = e0(e), r = td(o.state.rootActivity), i = /* @__PURE__ */ new Map(), s = /* @__PURE__ */ new Map(), c = [];
  for (const d of t.operations) {
    const l = Qx(d.kind), f = d.parameters ?? {};
    if (l === "add-activity") {
      const u = Pe(f.activityId) ?? d.temporaryReferences?.[0], h = Jx(u ?? Pe(f.displayName) ?? Pe(f.activityType) ?? "weaver-activity", r), g = qx(d, h, n);
      s.set(h, g), c.push(h), u && i.set(u, h), o.state.rootActivity && Zx(o.state.rootActivity, g);
      const y = Mt(f.position) ? wi(f.position, { x: 280, y: 160 }) : null;
      y && (o.layout = va(o.layout, h, y));
      continue;
    }
    if (l === "set-root") {
      const u = Gr(o, f.activityId, i, s);
      if (!u) throw new Error("Weaver batch referenced an unknown root activity.");
      o.state.rootActivity = u;
      continue;
    }
    if (l === "set-designer-position") {
      const u = Lt(f.activityId, i);
      if (!u || !Yi(o.state.rootActivity, u)) throw new Error("Weaver batch referenced an unknown activity position.");
      o.layout = va(o.layout, u, wi(f, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const u = Gr(o, f.activityId, i, s);
      if (!u) throw new Error("Weaver batch referenced an unknown activity property target.");
      Gx(u, Pe(f.propertyName) ?? "Value", f.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const u = Gr(o, f.activityId, i, s);
      if (!u) throw new Error("Weaver batch referenced an unknown activity update target.");
      const h = Mt(f.patch) ? f.patch : f;
      Object.assign(u, h);
      continue;
    }
    if (l === "remove-activity") {
      const u = Lt(f.activityId, i);
      if (!u) throw new Error("Weaver batch referenced an unknown activity remove target.");
      o.state.rootActivity = ed(o.state.rootActivity, u), o.layout = o.layout.filter((h) => h.nodeId !== u);
      continue;
    }
    if (l === "connect-activities") {
      Ux(o, f, i);
      continue;
    }
    if (l === "disconnect-activities") {
      Kx(o, f, i);
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
function qx(e, t, n) {
  const o = e.parameters ?? {}, r = Pe(o.activityVersionId) ?? Pe(o.activityType) ?? "Elsa.Workflows.Activity", i = n.find((s) => s.activityVersionId === r || s.activityTypeKey === r || s.displayName === Pe(o.displayName));
  return i ? gi(i, t) : {
    nodeId: t,
    activityVersionId: i?.activityVersionId ?? r,
    inputs: [],
    outputs: [],
    ...Pe(o.displayName) ? { displayName: Pe(o.displayName) } : {},
    designer: { position: wi(o.position, { x: 280, y: 160 }) }
  };
}
function Zx(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = qi(e);
  n && !n.some((o) => o.nodeId === t.nodeId) && n.push(t);
}
function Ux(e, t, n) {
  const o = e.state.rootActivity;
  if (!o?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const r = Lt(t.sourceActivityId ?? t.sourceId ?? t.from, n), i = Lt(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!r || !i) throw new Error("Weaver batch connection is missing source or target activity.");
  const s = o.structure.payload, c = Array.isArray(s.connections) ? s.connections : [], d = Pe(t.connectionId) ?? `flow-${r}-${i}`;
  s.connections = [
    ...c.filter((l) => !Mt(l) || l.id !== d),
    {
      id: d,
      source: { nodeId: r, port: Pe(t.outcome) ?? Pe(t.sourcePort) ?? "Done" },
      target: { nodeId: i }
    }
  ];
}
function Kx(e, t, n) {
  const o = e.state.rootActivity, r = o?.structure?.payload.connections;
  if (!Array.isArray(r)) return;
  const i = Pe(t.connectionId), s = Lt(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = Lt(t.targetActivityId ?? t.targetId ?? t.to, n);
  o.structure.payload.connections = r.filter((d) => {
    if (!Mt(d)) return !0;
    if (i && d.id === i) return !1;
    const l = Mt(d.source) ? d.source.nodeId : void 0, f = Mt(d.target) ? d.target.nodeId : void 0;
    return l !== s || f !== c;
  });
}
function Gx(e, t, n) {
  e[ql(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: "Literal", value: n }
  };
}
function Gr(e, t, n, o) {
  const r = Lt(t, n);
  return r ? Yi(e.state.rootActivity, r) ?? o.get(r) ?? null : null;
}
function Lt(e, t) {
  const n = Pe(e);
  return n ? t.get(n) ?? n : null;
}
function Yi(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of nd(e)) {
    const o = Yi(n, t);
    if (o) return o;
  }
  return null;
}
function ed(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = qi(e);
  if (n) {
    const o = n.map((r) => ed(r, t)).filter((r) => !!r);
    n.splice(0, n.length, ...o);
  }
  return e;
}
function td(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of nd(e)) td(n, t);
  return t;
}
function nd(e) {
  return qi(e) ?? [];
}
function qi(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function va(e, t, n) {
  return [
    ...e.filter((o) => o.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function wi(e, t) {
  const n = Mt(e) ? e : {}, o = Number(n.x), r = Number(n.y);
  return {
    x: Number.isFinite(o) ? Math.max(40, Math.round(o)) : t.x,
    y: Number.isFinite(r) ? Math.max(40, Math.round(r)) : t.y
  };
}
function Jx(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let o = n, r = 2;
  for (; t.has(o); )
    o = `${n}-${r}`, r += 1;
  return t.add(o), o;
}
function Qx(e) {
  return typeof e == "number" ? Xx[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Pe(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function e0(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function Mt(e) {
  return typeof e == "object" && e !== null;
}
const od = { workflowActivity: H0 }, rd = { workflow: F0 }, ba = "application/x-elsa-activity-version-id", t0 = 6, n0 = 1200, o0 = [10, 25, 50], r0 = 10, Na = "elsa-studio-workflow-palette-width", Sa = "elsa-studio-workflow-inspector-width", Ea = "elsa-studio-workflow-palette-collapsed", Ca = "elsa-studio-workflow-inspector-collapsed", id = "elsa-studio-workflow-side-panel-maximized", vn = 180, bn = 460, i0 = 260, Nn = 260, Sn = 560, s0 = 320, ka = 42, ko = 16, sd = wt.createContext(null);
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
        component: () => /* @__PURE__ */ a.jsx(a0, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list() })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ a.jsx(c0, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow instances",
        component: () => /* @__PURE__ */ a.jsx(l0, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow instance",
        component: () => /* @__PURE__ */ a.jsx(d0, { context: e.backend, ai: e.ai })
      }
    ]
  });
}
function a0({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: o,
  workflowDesignerPanels: r
}) {
  const [i, s] = Y(Ia);
  oe(() => {
    const d = () => s(Ia());
    return window.addEventListener("popstate", d), () => window.removeEventListener("popstate", d);
  }, []);
  const c = (d) => {
    const l = d ? `/workflows/definitions?definition=${encodeURIComponent(d)}` : "/workflows/definitions";
    window.history.pushState({}, "", l), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return i ? /* @__PURE__ */ a.jsx(V0, { context: e, definitionId: i, ai: t, propertyEditors: n, expressionEditors: o, workflowDesignerPanels: r, onBack: () => c(null) }) : /* @__PURE__ */ a.jsx(ur, { title: "Definitions", children: /* @__PURE__ */ a.jsx(f0, { context: e, ai: t, onOpen: c }) });
}
function c0({ context: e, ai: t }) {
  const [n, o] = Y(ja);
  oe(() => {
    const i = () => o(ja());
    return window.addEventListener("popstate", i), () => window.removeEventListener("popstate", i);
  }, []);
  const r = pe((i) => {
    const s = i?.trim() ?? "", c = new URL(window.location.href);
    s ? c.searchParams.set("definition", s) : c.searchParams.delete("definition"), o(s || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ a.jsx(ur, { title: "Executables", children: /* @__PURE__ */ a.jsx(p0, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: r }) });
}
function l0({ context: e, ai: t }) {
  return /* @__PURE__ */ a.jsx(ur, { title: "Instances", children: /* @__PURE__ */ a.jsx(m0, { context: e, ai: t }) });
}
function d0({ context: e, ai: t }) {
  const n = u0();
  return /* @__PURE__ */ a.jsx(ur, { title: "Instance", children: /* @__PURE__ */ a.jsx(x0, { context: e, ai: t, workflowExecutionId: n }) });
}
function ur({ title: e, children: t }) {
  return /* @__PURE__ */ a.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ a.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ a.jsxs("div", { children: [
      /* @__PURE__ */ a.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ a.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function Ia() {
  return new URLSearchParams(window.location.search).get("definition");
}
function ja() {
  return new URLSearchParams(window.location.search).get("definition");
}
function u0() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function f0({ context: e, ai: t, onOpen: n }) {
  const [o, r] = Y(""), [i, s] = Y("active"), [c, d] = Y(1), [l, f] = Y(r0), [u, h] = Y("loading"), [g, y] = Y(""), [v, x] = Y(""), [m, E] = Y([]), [p, w] = Y(0), [b, C] = Y(() => /* @__PURE__ */ new Set()), [k, A] = Y(null), [P, W] = Y(!1), [M, z] = Y([]), [O, N] = Y("idle"), j = ce(null), I = ge(() => m.map((V) => V.id), [m]), D = Rt(t, "weaver.workflows.suggest-create-metadata"), $ = Rt(t, "weaver.workflows.explain-definition"), T = I.filter((V) => b.has(V)).length, H = I.length > 0 && T === I.length, B = pe(async () => {
    h("loading"), y("");
    try {
      const V = await Xm(e, { search: o, state: i, page: c, pageSize: l }), J = typeof V.totalCount == "number", fe = V.totalCount ?? V.definitions.length, me = ad(fe, l);
      if (fe > 0 && c > me) {
        d(me);
        return;
      }
      E(J ? V.definitions : k0(V.definitions, c, l)), w(fe), h("ready");
    } catch (V) {
      y(V instanceof Error ? V.message : String(V)), h("failed");
    }
  }, [e, o, i, c, l]);
  oe(() => {
    B();
  }, [B]), oe(() => {
    j.current && (j.current.indeterminate = T > 0 && !H);
  }, [H, T]);
  const F = pe(async () => {
    if (!(O === "loading" || O === "ready")) {
      N("loading");
      try {
        const V = await Fi(e);
        z(V.activities ?? []), N("ready");
      } catch (V) {
        N("failed"), y(V instanceof Error ? V.message : String(V));
      }
    }
  }, [O, e]), U = () => {
    y(""), x(""), A({ name: "", description: "", rootKind: "flowchart" }), F();
  }, q = async () => {
    if (k?.name.trim()) {
      W(!0), y(""), x("");
      try {
        const V = await Zm(e, {
          name: k.name.trim(),
          description: k.description.trim() || null,
          rootKind: k.rootKind,
          rootActivityVersionId: _0(k, M)
        });
        A(null), n(V.definition.id);
      } catch (V) {
        y(V instanceof Error ? V.message : String(V));
      } finally {
        W(!1);
      }
    }
  }, ne = (V) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(V)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, ae = async () => {
    if (m.length === 1 && c > 1) {
      d(c - 1);
      return;
    }
    await B();
  }, K = () => C(/* @__PURE__ */ new Set()), L = (V, J) => {
    C((fe) => {
      const me = new Set(fe);
      return J ? me.add(V) : me.delete(V), me;
    });
  }, Z = (V) => {
    C((J) => {
      const fe = new Set(J);
      for (const me of I)
        V ? fe.add(me) : fe.delete(me);
      return fe;
    });
  }, ie = (V) => {
    s(V), d(1), K();
  }, se = (V) => {
    r(V), d(1), K();
  }, G = async (V) => {
    if (window.confirm(`Delete workflow definition "${V.name}"? You can restore it from the Deleted view.`)) {
      x(""), y("");
      try {
        await Um(e, V.id), L(V.id, !1), x(`Deleted ${V.name}`), await ae();
      } catch (J) {
        y(J instanceof Error ? J.message : String(J));
      }
    }
  }, ee = async (V) => {
    x(""), y("");
    try {
      await Km(e, V.id), L(V.id, !1), x(`Restored ${V.name}`), await ae();
    } catch (J) {
      y(J instanceof Error ? J.message : String(J));
    }
  }, le = async (V) => {
    if (window.confirm(`Permanently delete workflow definition "${V.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`)) {
      x(""), y("");
      try {
        await Gm(e, V.id), L(V.id, !1), x(`Permanently deleted ${V.name}`), await ae();
      } catch (J) {
        y(J instanceof Error ? J.message : String(J));
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
        /* @__PURE__ */ a.jsx(zl, { size: 15 }),
        /* @__PURE__ */ a.jsx("input", { value: o, onChange: (V) => se(V.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
        B();
      }, children: "Refresh" }),
      /* @__PURE__ */ a.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ a.jsxs("button", { type: "button", title: "Create workflow", onClick: U, children: [
        /* @__PURE__ */ a.jsx(Hi, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    u === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(nt, { size: 16 }),
      " ",
      g
    ] }) : null,
    u !== "failed" && g ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(nt, { size: 16 }),
      " ",
      g
    ] }) : null,
    v ? /* @__PURE__ */ a.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ a.jsx(sn, { size: 14 }),
      " ",
      v
    ] }) : null,
    b.size > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ a.jsxs("span", { children: [
        b.size,
        " selected"
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: K, children: "Clear selection" })
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
              ref: j,
              type: "checkbox",
              checked: H,
              onChange: (V) => Z(V.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ a.jsx("span", { children: "Name" }),
          /* @__PURE__ */ a.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ a.jsx("span", { children: i === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ a.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ a.jsx("span", { children: "Actions" })
        ] }),
        m.map((V) => /* @__PURE__ */ a.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${V.name}`,
            "aria-selected": b.has(V.id),
            tabIndex: 0,
            onClick: () => n(V.id),
            onKeyDown: (J) => {
              J.currentTarget === J.target && (J.key !== "Enter" && J.key !== " " || (J.preventDefault(), n(V.id)));
            },
            children: [
              /* @__PURE__ */ a.jsx("label", { className: "wf-row-select", onClick: (J) => J.stopPropagation(), children: /* @__PURE__ */ a.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: b.has(V.id),
                  onChange: (J) => L(V.id, J.target.checked),
                  "aria-label": `Select workflow definition ${V.name}`
                }
              ) }),
              /* @__PURE__ */ a.jsxs("span", { children: [
                /* @__PURE__ */ a.jsx("strong", { children: V.name }),
                /* @__PURE__ */ a.jsx("small", { children: V.description || V.id })
              ] }),
              /* @__PURE__ */ a.jsx("span", { children: V.latestVersion ?? "No version" }),
              /* @__PURE__ */ a.jsx("span", { children: i === "deleted" ? Ge(V.deletedAt) : V.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ a.jsx("span", { children: Ge(V.lastModifiedAt) }),
              /* @__PURE__ */ a.jsx("span", { className: "wf-row-actions", onClick: (J) => J.stopPropagation(), children: i === "active" ? /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
                /* @__PURE__ */ a.jsx("button", { type: "button", onClick: (J) => {
                  J.stopPropagation(), n(V.id);
                }, children: "Open" }),
                /* @__PURE__ */ a.jsx("button", { type: "button", onClick: (J) => {
                  J.stopPropagation(), ne(V.id);
                }, children: "Artifacts" }),
                $ ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => xt(t, $, V), children: [
                  /* @__PURE__ */ a.jsx(mt, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ a.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  G(V);
                }, children: [
                  /* @__PURE__ */ a.jsx(fi, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
                /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
                  ee(V);
                }, children: [
                  /* @__PURE__ */ a.jsx(Oi, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ a.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  le(V);
                }, children: [
                  /* @__PURE__ */ a.jsx(fi, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          V.id
        ))
      ] }),
      /* @__PURE__ */ a.jsx(
        C0,
        {
          page: c,
          pageSize: l,
          totalCount: p,
          onPageChange: d,
          onPageSizeChange: (V) => {
            f(V), d(1);
          }
        }
      )
    ] }) : null,
    k ? /* @__PURE__ */ a.jsx(
      h0,
      {
        draft: k,
        activities: M,
        catalogState: O,
        creating: P,
        suggestMetadataAction: D,
        onSuggestMetadata: D ? () => xt(t, D, { draft: k, activities: M }) : void 0,
        onChange: (V) => A(V),
        onClose: () => A(null),
        onSubmit: q
      }
    ) : null
  ] });
}
function h0({ draft: e, activities: t, catalogState: n, creating: o, suggestMetadataAction: r, onSuggestMetadata: i, onChange: s, onClose: c, onSubmit: d }) {
  const l = ge(() => I0(t), [t]), f = j0(e, t), u = (h) => {
    if (h.startsWith("kind:")) {
      s({ ...e, rootKind: h.slice(5), rootActivityVersionId: null });
      return;
    }
    const g = t.find((y) => y.activityVersionId === h);
    s({
      ...e,
      rootKind: cd(g) ?? e.rootKind,
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
                l.otherCategories.map((h) => /* @__PURE__ */ a.jsx("optgroup", { label: h.name, children: h.activities.map((g) => /* @__PURE__ */ a.jsx("option", { value: g.activityVersionId, children: je(g) }, g.activityVersionId)) }, h.name))
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
function p0({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: o }) {
  const [r, i] = Y("loading"), [s, c] = Y(""), [d, l] = Y(""), [f, u] = Y([]), h = n?.trim().toLowerCase() ?? "", g = ge(
    () => h ? f.filter((b) => M0(b, h)) : f,
    [h, f]
  ), y = ge(
    () => Array.from(new Set(f.flatMap((b) => [
      b.definitionId,
      b.definitionVersionId,
      b.sourceId
    ]).filter((b) => !!b))).sort((b, C) => b.localeCompare(C)),
    [f]
  ), v = Rt(t, "weaver.workflows.explain-executable"), x = pe(async () => {
    i("loading"), c("");
    try {
      u(await Vl(e)), i("ready");
    } catch (b) {
      c(b instanceof Error ? b.message : String(b)), i("failed");
    }
  }, [e]);
  oe(() => {
    x();
  }, [x]);
  const m = async (b) => {
    l(""), c("");
    try {
      await Rl(e, b.artifactId), l(`Started ${b.artifactId}`);
    } catch (C) {
      c(C instanceof Error ? C.message : String(C));
    }
  }, E = (b) => {
    v && xt(t, v, b) && (c(""), l(`Sent ${b.artifactId} to Weaver`));
  }, p = (b) => {
    c(""), l(`Copied ${b}`);
  }, w = (b) => {
    l(""), c(`Could not copy ${b}.`);
  };
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
        x();
      }, children: "Refresh" }),
      /* @__PURE__ */ a.jsxs("label", { className: "wf-search wf-executable-definition-filter", children: [
        /* @__PURE__ */ a.jsx(zl, { size: 14 }),
        /* @__PURE__ */ a.jsx(
          "input",
          {
            "aria-label": "Filter executables by workflow definition",
            list: "wf-executable-definition-options",
            placeholder: "Filter by definition ID",
            value: n ?? "",
            onChange: (b) => o(b.currentTarget.value || null)
          }
        )
      ] }),
      /* @__PURE__ */ a.jsx("datalist", { id: "wf-executable-definition-options", children: y.map((b) => /* @__PURE__ */ a.jsx("option", { value: b }, b)) }),
      n ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => o(null), children: [
        /* @__PURE__ */ a.jsx(Ll, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    r === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(nt, { size: 16 }),
      " ",
      s
    ] }) : null,
    d ? /* @__PURE__ */ a.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ a.jsx(sn, { size: 14 }),
      " ",
      d
    ] }) : null,
    r === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading workflow executables..." }) : null,
    r === "ready" && g.length === 0 ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: n ? "No workflow executables match this definition filter." : "No workflow executables found. Publish a workflow definition to create one." }) : null,
    r === "ready" && g.length > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ a.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ a.jsx("span", { children: "Version" }),
        /* @__PURE__ */ a.jsx("span", { children: "Source" }),
        /* @__PURE__ */ a.jsx("span", { children: "Root" }),
        /* @__PURE__ */ a.jsx("span", { children: "Published" }),
        /* @__PURE__ */ a.jsx("span", { children: "Actions" })
      ] }),
      g.map((b) => /* @__PURE__ */ a.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ a.jsxs("span", { className: "wf-artifact-cell", children: [
          /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line", children: [
            /* @__PURE__ */ a.jsx("strong", { title: b.artifactId, children: b.artifactId }),
            /* @__PURE__ */ a.jsx(Ut, { value: b.artifactId, ariaLabel: `Copy artifact ID ${b.artifactId}`, copiedLabel: "artifact ID", onCopied: p, onCopyFailed: w })
          ] }),
          /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
            /* @__PURE__ */ a.jsx("small", { title: b.artifactHash, children: b.artifactHash }),
            /* @__PURE__ */ a.jsx(Ut, { value: b.artifactHash, ariaLabel: `Copy artifact hash ${b.artifactHash}`, copiedLabel: "artifact hash", onCopied: p, onCopyFailed: w })
          ] })
        ] }),
        /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line wf-version-cell", children: [
          /* @__PURE__ */ a.jsx("span", { children: b.artifactVersion }),
          /* @__PURE__ */ a.jsx(Ut, { value: b.artifactVersion, ariaLabel: `Copy artifact version ${b.artifactVersion}`, copiedLabel: "artifact version", onCopied: p, onCopyFailed: w })
        ] }),
        /* @__PURE__ */ a.jsx(g0, { executable: b, onCopied: p, onCopyFailed: w }),
        /* @__PURE__ */ a.jsx("span", { children: hd(b) }),
        /* @__PURE__ */ a.jsx("span", { children: Ge(b.publishedAt ?? b.createdAt) }),
        /* @__PURE__ */ a.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
            m(b);
          }, children: [
            /* @__PURE__ */ a.jsx(lr, { size: 13 }),
            " Run"
          ] }),
          v ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => E(b), children: [
            /* @__PURE__ */ a.jsx(mt, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, b.artifactId))
    ] }) : null
  ] });
}
function g0({ executable: e, onCopied: t, onCopyFailed: n }) {
  const o = e.sourceId || e.definitionVersionId || e.definitionId, r = e.sourceVersion;
  return /* @__PURE__ */ a.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ a.jsx("span", { className: "wf-source-kind", children: pd(e.sourceKind) }),
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
  }, children: /* @__PURE__ */ a.jsx(Rm, { size: 12 }) });
}
function y0({ context: e, ai: t, definitionId: n, publishedArtifactId: o }) {
  const [r, i] = Y("loading"), [s, c] = Y(""), [d, l] = Y(""), [f, u] = Y([]), h = Rt(t, "weaver.workflows.explain-executable"), g = pe(async () => {
    i("loading"), c("");
    try {
      const p = await Vl(e);
      u(p.filter((w) => D0(w, n)).sort(P0)), i("ready");
    } catch (p) {
      c(p instanceof Error ? p.message : String(p)), u([]), i("failed");
    }
  }, [e, n]);
  oe(() => {
    g();
  }, [g, o]);
  const y = async (p) => {
    l(""), c("");
    try {
      await Rl(e, p.artifactId), l(`Started ${p.artifactId}`);
    } catch (w) {
      c(w instanceof Error ? w.message : String(w));
    }
  }, v = (p) => {
    h && xt(t, h, p) && (c(""), l(`Sent ${p.artifactId} to Weaver`));
  }, x = () => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(n)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, m = (p) => {
    c(""), l(`Copied ${p}`);
  }, E = (p) => {
    l(""), c(`Could not copy ${p}.`);
  };
  return /* @__PURE__ */ a.jsxs("div", { className: "wf-artifacts-panel", children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-artifacts-toolbar", children: [
      /* @__PURE__ */ a.jsxs("span", { children: [
        f.length,
        " artifact",
        f.length === 1 ? "" : "s"
      ] }),
      /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
        g();
      }, children: [
        /* @__PURE__ */ a.jsx(Oi, { size: 13 }),
        " Refresh"
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: x, children: "Open list" })
    ] }),
    r === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ a.jsx(nt, { size: 14 }),
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
        /* @__PURE__ */ a.jsx("span", { children: Ge(p.publishedAt ?? p.createdAt) })
      ] }),
      /* @__PURE__ */ a.jsxs("div", { className: "wf-artifact-card-values", children: [
        /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line", children: [
          /* @__PURE__ */ a.jsx("code", { title: p.artifactId, children: p.artifactId }),
          /* @__PURE__ */ a.jsx(Ut, { value: p.artifactId, ariaLabel: `Copy artifact ID ${p.artifactId}`, copiedLabel: "artifact ID", onCopied: m, onCopyFailed: E })
        ] }),
        /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ a.jsx("code", { title: p.artifactHash, children: p.artifactHash }),
          /* @__PURE__ */ a.jsx(Ut, { value: p.artifactHash, ariaLabel: `Copy artifact hash ${p.artifactHash}`, copiedLabel: "artifact hash", onCopied: m, onCopyFailed: E })
        ] })
      ] }),
      /* @__PURE__ */ a.jsxs("dl", { children: [
        /* @__PURE__ */ a.jsxs("div", { children: [
          /* @__PURE__ */ a.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ a.jsxs("dd", { children: [
            pd(p.sourceKind),
            " ",
            p.sourceVersion ? `v${p.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ a.jsxs("div", { children: [
          /* @__PURE__ */ a.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ a.jsx("dd", { children: hd(p) })
        ] })
      ] }),
      /* @__PURE__ */ a.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
          y(p);
        }, children: [
          /* @__PURE__ */ a.jsx(lr, { size: 13 }),
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
function m0({ context: e }) {
  const [t, n] = Y("loading"), [o, r] = Y(""), [i, s] = Y(""), [c, d] = Y([]), l = pe(async () => {
    n("loading"), r("");
    try {
      const u = await nx(e, { status: i || void 0, take: 100 });
      d(u), n("ready");
    } catch (u) {
      r(u instanceof Error ? u.message : String(u)), d([]), n("failed");
    }
  }, [e, i]);
  oe(() => {
    l();
  }, [l]);
  const f = (u) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(u)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
        l();
      }, children: "Refresh" }),
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
      /* @__PURE__ */ a.jsx(nt, { size: 16 }),
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
            /* @__PURE__ */ a.jsx("span", { children: /* @__PURE__ */ a.jsx(Un, { status: u.status, subStatus: u.subStatus }) }),
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
            /* @__PURE__ */ a.jsx("span", { children: Ge(u.startedAt ?? u.createdAt) }),
            /* @__PURE__ */ a.jsx("span", { children: Q0(u.startedAt ?? u.createdAt, u.completedAt ?? u.updatedAt) })
          ]
        },
        u.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function x0({ context: e, ai: t, workflowExecutionId: n }) {
  const [o, r] = Y("loading"), [i, s] = Y(""), [c, d] = Y(null), [l, f] = Y(null), u = Rt(t, "weaver.workflows.explain-instance"), h = pe(async () => {
    if (!n) {
      s("No workflow execution id was provided."), r("failed");
      return;
    }
    r("loading"), s("");
    try {
      const y = await ox(e, n), [v, x] = await Promise.all([
        qm(e, y.instance.definitionVersionId).then(
          (m) => ({ definitionVersion: m, error: "" }),
          (m) => ({ definitionVersion: null, error: m instanceof Error ? m.message : String(m) })
        ),
        Fi(e)
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
  const g = () => {
    window.history.pushState({}, "", "/workflows/instances"), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: g, children: [
        /* @__PURE__ */ a.jsx(qo, { size: 14 }),
        " Instances"
      ] }),
      /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
        h();
      }, children: [
        /* @__PURE__ */ a.jsx(Oi, { size: 14 }),
        " Refresh"
      ] }),
      c && u ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => xt(t, u, c.details), children: [
        /* @__PURE__ */ a.jsx(mt, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    o === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading workflow instance..." }) : null,
    o === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(nt, { size: 16 }),
      " ",
      i
    ] }) : null,
    o === "ready" && c ? /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-detail-workbench", children: [
      /* @__PURE__ */ a.jsx(
        w0,
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
        v0,
        {
          ai: t,
          action: u,
          summary: c.details.instance,
          details: c.details,
          state: "ready",
          error: "",
          selectedEvidenceId: l,
          onSelectEvidence: f,
          graphNodeIds: c.definitionVersion ? E0(c.definitionVersion, c.activityCatalog) : void 0
        }
      )
    ] }) : null
  ] });
}
function w0({ definitionVersion: e, definitionVersionError: t, activityCatalog: n, details: o, selectedEvidenceId: r, onSelectEvidence: i }) {
  const s = ge(() => {
    if (!e) return { nodes: [], edges: [] };
    const c = e.state.rootActivity;
    if (!c) return { nodes: [], edges: [] };
    const d = n.find((g) => g.activityVersionId === c.activityVersionId), l = Bi(c, d), f = l === "unsupported" ? null : An(c, []), u = l === "unsupported" ? hi(c, n, e.layout) : f ? Fl(f, n, e.layout) : hi(c, n, e.layout), h = u.nodes.map((g) => ({
      ...g,
      draggable: !1,
      connectable: !1,
      deletable: !1
    }));
    return {
      nodes: ax(h, o.activities, o.incidents, r),
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
      /* @__PURE__ */ a.jsx(Un, { status: o.instance.status, subStatus: o.instance.subStatus })
    ] }),
    /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ a.jsxs("div", { className: "wf-empty", children: [
        "The workflow instance loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ a.jsx("small", { children: J0(t) }) : null
      ] }),
      e && s.nodes.length === 0 ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      s.nodes.length > 0 ? /* @__PURE__ */ a.jsxs(
        El,
        {
          nodes: s.nodes,
          edges: s.edges,
          nodeTypes: od,
          edgeTypes: rd,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (c, d) => i(d.id),
          onPaneClick: () => i(null),
          children: [
            /* @__PURE__ */ a.jsx(kl, {}),
            /* @__PURE__ */ a.jsx(Al, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ a.jsx(jl, {})
          ]
        }
      ) : null
    ] })
  ] });
}
function v0({ ai: e, action: t, summary: n, details: o, state: r, error: i, selectedEvidenceId: s = null, onSelectEvidence: c, graphNodeIds: d }) {
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
      /* @__PURE__ */ a.jsx("dd", { children: /* @__PURE__ */ a.jsx(Un, { status: n.status, subStatus: n.subStatus }) }),
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
      /* @__PURE__ */ a.jsx(nt, { size: 16 }),
      " ",
      i
    ] }) : null,
    r === "ready" && o ? /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
      /* @__PURE__ */ a.jsx(b0, { activities: o.activities, selectedEvidenceId: s, onSelectEvidence: c }),
      /* @__PURE__ */ a.jsx(N0, { incidents: o.incidents, selectedEvidenceId: s, onSelectEvidence: c }),
      /* @__PURE__ */ a.jsx(S0, { details: o, graphNodeIds: d })
    ] }) : null
  ] }) : /* @__PURE__ */ a.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Select a workflow instance to inspect activity history." }) });
}
function b0({ activities: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
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
          /* @__PURE__ */ a.jsx("span", { children: /* @__PURE__ */ a.jsx(Un, { status: o.status, subStatus: o.subStatus }) }),
          /* @__PURE__ */ a.jsx("strong", { children: Zi(o.activityType) ?? o.activityType }),
          /* @__PURE__ */ a.jsx("small", { children: o.activityExecutionId }),
          /* @__PURE__ */ a.jsx("time", { children: Ge(o.scheduledAt) })
        ]
      },
      o.activityExecutionId
    )) }) : null
  ] });
}
function N0({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
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
function S0({ details: e, graphNodeIds: t }) {
  if (!t) return null;
  const n = new Map(e.activities.map((i) => [i.activityExecutionId, i])), o = e.activities.filter((i) => !t.has(_a(i))), r = e.incidents.filter((i) => {
    const s = i.activityExecutionId ? n.get(i.activityExecutionId) : null, c = i.executableNodeId ?? (s ? _a(s) : "");
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
function Un({ status: e, subStatus: t }) {
  return /* @__PURE__ */ a.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
function E0(e, t) {
  const n = e.state.rootActivity;
  if (!n) return /* @__PURE__ */ new Set();
  const o = t.find((i) => i.activityVersionId === n.activityVersionId);
  if (Bi(n, o) === "unsupported") return /* @__PURE__ */ new Set([n.nodeId]);
  const r = An(n, []);
  return new Set(r?.slot.activities.map((i) => i.nodeId) ?? [n.nodeId]);
}
function _a(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function C0({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const i = ad(n, t), s = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
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
      /* @__PURE__ */ a.jsx("select", { value: t, onChange: (d) => r(Number(d.target.value)), children: o0.map((d) => /* @__PURE__ */ a.jsx("option", { value: d, children: d }, d)) })
    ] }),
    /* @__PURE__ */ a.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ a.jsx(qo, { size: 14 }),
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
function k0(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function ad(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Rt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function xt(e, t, n) {
  const o = t.createPrompt(n);
  return o ? (e.dispatchPrompt(o), !0) : !1;
}
function I0(e) {
  const t = Ko(e, "flowchart"), n = Ko(e, "sequence"), o = [
    { value: t?.activityVersionId ?? "kind:flowchart", label: "Flowchart" },
    { value: n?.activityVersionId ?? "kind:sequence", label: "Sequence" }
  ], r = /* @__PURE__ */ new Map();
  for (const s of e.filter(fd)) {
    if (A0(s)) continue;
    const c = s.category || "Uncategorized";
    r.set(c, [...r.get(c) ?? [], s]);
  }
  const i = Array.from(r.entries()).sort(([s], [c]) => s.localeCompare(c)).map(([s, c]) => ({
    name: s,
    activities: c.sort((d, l) => je(d).localeCompare(je(l)))
  }));
  return { compositeRoots: o, otherCategories: i };
}
function j0(e, t) {
  return e.rootActivityVersionId ?? Ko(t, e.rootKind)?.activityVersionId ?? `kind:${e.rootKind}`;
}
function _0(e, t) {
  return e.rootActivityVersionId ?? Ko(t, e.rootKind)?.activityVersionId ?? null;
}
function Ko(e, t) {
  return e.find((n) => cd(n) === t);
}
function cd(e) {
  return e ? dd(e) ? "flowchart" : ud(e) ? "sequence" : null : null;
}
function ld(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "Uncategorized";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [o]) => n.localeCompare(o)).map(([n, o]) => ({
    category: n,
    activities: o.sort((r, i) => je(r).localeCompare(je(i)))
  }));
}
function A0(e) {
  return dd(e) || ud(e);
}
function dd(e) {
  return je(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function ud(e) {
  return je(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function fd(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function hd(e) {
  return T0(e.rootActivityType) || e.rootActivityType;
}
function M0(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function D0(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function P0(e, t) {
  return Aa(t) - Aa(e);
}
function Aa(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function pd(e) {
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
    Io(t, n.typeName, n), Io(t, n.name, n), Io(t, n.displayName, n);
    const o = n.typeName.split(".").filter(Boolean).at(-1);
    Io(t, o, n);
  }
  return t;
}
function L0(e, t, n) {
  const o = t.get(e.activityVersionId);
  return n.get(In(o?.activityTypeKey)) ?? n.get(In(Zi(o?.activityTypeKey))) ?? n.get(In(o?.displayName)) ?? n.get(In(e.activityVersionId)) ?? null;
}
function Io(e, t, n) {
  const o = In(t);
  o && !e.has(o) && e.set(o, n);
}
function In(e) {
  return e?.trim().toLowerCase() ?? "";
}
function Zi(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function Ma(e, t, n, o) {
  const r = fr();
  if (!r) return t;
  const i = r.getItem(e);
  if (i == null) return t;
  const s = Number(i);
  return Number.isFinite(s) ? To(s, n, o) : t;
}
function Da(e, t) {
  const n = fr();
  if (!n) return t;
  const o = n.getItem(e);
  return o === "true" ? !0 : o === "false" ? !1 : t;
}
function R0() {
  const e = fr();
  if (!e) return null;
  const t = e.getItem(id);
  return t === "palette" || t === "inspector" ? t : null;
}
function fr() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function En(e, t) {
  const n = fr();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function To(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function V0({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: o,
  expressionEditors: r,
  workflowDesignerPanels: i,
  onBack: s
}) {
  const [c, d] = Y(null), [l, f] = Y(null), [u, h] = Y([]), [g, y] = Y([]), [v, x] = Y(Po), [m, E] = Y("loading"), [p, w] = Y([]), [b, C] = Y([]), [k, A] = Y([]), [P, W] = Y(null), [M, z] = Y(null), [O, N] = Y(null), [j, I] = Y(null), [D, $] = Y(""), [T, H] = Y(""), [B, F] = Y("idle"), [U, q] = Y(null), [ne, ae] = Y(!1), [K, L] = Y(!1), [Z, ie] = Y(null), [se, G] = Y(() => /* @__PURE__ */ new Set()), [ee, le] = Y(() => Ma(Na, i0, vn, bn)), [V, J] = Y(() => Ma(Sa, s0, Nn, Sn)), [fe, me] = Y(() => Da(Ea, !1)), [Ee, De] = Y(() => Da(Ca, !1)), [ye, Be] = Y(R0), [rt, it] = Y("activities"), [ze, We] = Y("inspector"), Ce = ce(null), Fe = ce(null), an = ce(""), vt = ce(0), Kn = ce(Promise.resolve()), cn = ce(/* @__PURE__ */ new Map()), Vt = ce(null), ut = ce(null), bt = ce(!1), Nt = l?.state.rootActivity ?? null, Qe = ge(() => new Map(u.map((S) => [S.activityVersionId, S])), [u]), Gn = ge(() => z0(g), [g]), Le = ge(() => Ol(Nt, p), [Nt, p]), Jn = Bi(Le, Le ? Qe.get(Le.activityVersionId) : void 0), be = !!Le && Jn === "unsupported", Xe = ge(() => be ? null : An(Nt, p), [Nt, p, be]), ln = ge(() => ld(u), [u]), Ne = ge(() => be && Le?.nodeId === M ? Le : Xe?.slot.activities.find((S) => S.nodeId === M) ?? null, [be, Xe, Le, M]), St = ge(
    () => Ne ? L0(Ne, Qe, Gn) : null,
    [Qe, Gn, Ne]
  ), dn = Ne ? Ke(Ne) : [], _e = Jn === "flowchart" && Xe?.slot.mode === "flowchart", Et = !Nt || !be, ft = B !== "idle", hr = !!l?.state.rootActivity && !ft, Qn = Rt(n, "weaver.workflows.find-draft-risks"), eo = Rt(n, "weaver.workflows.propose-update");
  oe(() => {
    if (!(!c || !l))
      return window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
        workflowId: c.definition.id,
        workflowDefinitionId: c.definition.id,
        workflowVersionId: l.sourceVersionId ?? null,
        draftId: l.id,
        revision: U0(l),
        selectedNodeId: M,
        selectedActivityType: St?.typeName ?? (Ne ? Qe.get(Ne.activityVersionId)?.activityTypeKey ?? Ne.activityVersionId : null),
        summary: c.definition.name,
        activities: md(l.state.rootActivity, Qe),
        diagnostics: l.validationErrors.map((S) => ({ severity: S.code ?? "warning", message: S.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === c.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [Qe, c, l, St, Ne, M]), oe(() => {
    !l || !U || U.draftSignature !== at(l) && ae(!1);
  }, [l, U]), oe(() => {
    const S = (R) => {
      const X = R.detail;
      if (!X?.batch || !X.respond) return;
      if (!l || !c) {
        X.respond({ ok: !1, message: "No active workflow draft is open." });
        return;
      }
      const Q = X.batch.workflowDefinitionId;
      if (Q && Q !== "active-draft" && Q !== c.definition.id) {
        X.respond({ ok: !1, message: `Batch targets workflow '${Q}', but '${c.definition.id}' is active.` });
        return;
      }
      try {
        const de = K0(l), re = Yx(l, X.batch, u), he = `weaver-batch-${Date.now()}`;
        cn.current.set(he, de), f(re.draft), w([]), z(re.finalActivityIds.at(-1) ?? null), ie(null), q(null), H(re.summary), $(""), X.respond({ ok: !0, result: { ...re, undoToken: he } });
      } catch (de) {
        const re = de instanceof Error ? de.message : String(de);
        $(re), X.respond({ ok: !1, message: re });
      }
    }, _ = (R) => {
      const X = R.detail;
      if (!X?.undoToken || !X.respond) return;
      const Q = cn.current.get(X.undoToken);
      if (!Q) {
        X.respond({ ok: !1, message: "The Weaver batch undo point is no longer available." });
        return;
      }
      cn.current.delete(X.undoToken), f(Q), w([]), z(null), ie(null), q(null), H("Restored workflow draft before Weaver batch."), $(""), X.respond({ ok: !0, summary: "Restored workflow draft before Weaver batch." });
    };
    return window.addEventListener(xa, S), window.addEventListener(wa, _), () => {
      window.removeEventListener(xa, S), window.removeEventListener(wa, _);
    };
  }, [u, c, l]), oe(() => {
    En(Na, String(ee));
  }, [ee]), oe(() => {
    En(Sa, String(V));
  }, [V]), oe(() => {
    En(Ea, String(fe));
  }, [fe]), oe(() => {
    En(Ca, String(Ee));
  }, [Ee]), oe(() => {
    En(id, ye);
  }, [ye]), oe(() => {
    if (!ye) return;
    const S = (_) => {
      _.key === "Escape" && Be(null);
    };
    return window.addEventListener("keydown", S), () => window.removeEventListener("keydown", S);
  }, [ye]);
  const un = pe(async () => {
    $(""), E("loading");
    const [S, _, R, X] = await Promise.all([
      Ym(e, t),
      Fi(e),
      rx(e).then(
        (de) => ({ ok: !0, descriptors: de }),
        () => ({ ok: !1, descriptors: [] })
      ),
      ix(e).then(
        (de) => ({ ok: !0, descriptors: de }),
        () => ({ ok: !1, descriptors: Po })
      )
    ]), Q = S.draft ?? null;
    d(S), an.current = Q ? at(Q) : "", f(Q), h(_.activities ?? []), y(R.descriptors), x(X.descriptors.length > 0 ? X.descriptors : Po), E(R.ok ? "ready" : "failed"), w([]), z(null);
  }, [e, t]);
  oe(() => {
    un().catch((S) => $(S instanceof Error ? S.message : String(S)));
  }, [un]), oe(() => {
    G((S) => {
      let _ = !1;
      const R = new Set(S);
      for (const X of ln)
        R.has(X.category) || (R.add(X.category), _ = !0);
      return _ ? R : S;
    });
  }, [ln]), oe(() => {
    if (!Le) {
      C([]), A([]);
      return;
    }
    const S = be ? hi(Le, u, l?.layout ?? []) : Xe ? Fl(Xe, u, l?.layout ?? []) : { nodes: [], edges: [] };
    C(S.nodes), A(S.edges);
  }, [u, l?.layout, be, Xe, Le]);
  const pr = (S) => {
    f((_) => _ && { ..._, state: { ..._.state, rootActivity: S } });
  }, fn = pe((S, _) => {
    if (l?.state.rootActivity && be)
      return;
    const R = gi(S, Ta(S));
    if (!l?.state.rootActivity) {
      pr(R), z(R.nodeId);
      return;
    }
    if (!Xe) {
      if (!Ke(R)[0]) {
        H(""), $("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      f((Q) => {
        if (!Q?.state.rootActivity) return Q;
        const de = Q.state.rootActivity, re = pi(R, [], [de]), he = _ ? [
          ...Q.layout.filter((ke) => ke.nodeId !== de.nodeId),
          {
            nodeId: de.nodeId,
            x: Math.round(_.x),
            y: Math.round(_.y)
          }
        ] : Q.layout;
        return {
          ...Q,
          layout: he,
          state: {
            ...Q.state,
            rootActivity: re
          }
        };
      }), z(l.state.rootActivity.nodeId), $(""), H(`Wrapped root in ${je(S)}`);
      return;
    }
    f((X) => {
      if (!X?.state.rootActivity) return X;
      const Q = An(X.state.rootActivity, p);
      if (!Q) return X;
      const de = pi(X.state.rootActivity, p, [...Q.slot.activities, R]), re = _ ? [
        ...X.layout.filter((he) => he.nodeId !== R.nodeId),
        {
          nodeId: R.nodeId,
          x: Math.round(_.x),
          y: Math.round(_.y)
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
    }), z(R.nodeId);
  }, [l?.state.rootActivity, p, be, Xe]), Ht = pe((S, _) => {
    const R = gi(S, Ta(S)), X = {
      id: R.nodeId,
      type: "workflowActivity",
      position: _,
      selected: !0,
      data: {
        label: je(S),
        activityVersionId: S.activityVersionId,
        activityTypeKey: S.activityTypeKey,
        category: S.category,
        executionType: S.executionType,
        icon: yi(S),
        childSlots: Ke(R),
        acceptsInbound: String(S.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: Yl(R, S)
      }
    };
    return { activityNode: R, node: X };
  }, []), Re = pe((S, _, R = []) => {
    be || f((X) => {
      if (!X) return X;
      const Q = dx(X.layout, S), de = X.state.rootActivity;
      if (!de) return { ...X, layout: Q };
      const re = An(de, p);
      if (!re) return { ...X, layout: Q };
      const he = cx(re, S, _, R), ke = re.slot.mode === "flowchart" ? lx(he, _) : he;
      return {
        ...X,
        layout: Q,
        state: {
          ...X.state,
          rootActivity: Bl(de, p, ke)
        }
      };
    });
  }, [p, be]), hn = pe((S, _) => {
    if (!Ce.current) return null;
    const R = Ce.current.getBoundingClientRect();
    return P ? P.screenToFlowPosition({ x: S, y: _ }) : {
      x: S - R.left,
      y: _ - R.top
    };
  }, [P]), pn = pe((S, _) => document.elementFromPoint(S, _)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), gn = pe((S, _, R) => {
    const X = b.find((Ae) => Ae.id === _.source), Q = b.find((Ae) => Ae.id === _.target), de = X && Q ? Y0(X, Q) : X ? za(X) : R, re = Ht(S, de), ke = [...b.map((Ae) => Ae.selected ? { ...Ae, selected: !1 } : Ae), re.node], kt = vx(k, _, re.node.id);
    C(ke), A(kt), z(re.node.id), Re(ke, kt, [re.activityNode]);
  }, [Re, Ht, k, b]), Ct = pe((S, _, R) => {
    if (!Et || !Ce.current) return !1;
    const X = Ce.current.getBoundingClientRect();
    if (!(_ >= X.left && _ <= X.right && R >= X.top && R <= X.bottom)) return !1;
    const de = hn(_, R);
    if (!de) return !1;
    if (_e) {
      const re = pn(_, R), he = re ? k.find((ke) => ke.id === re) : void 0;
      if (he)
        return gn(S, he, de), !0;
    }
    return fn(S, de), !0;
  }, [fn, Et, k, pn, _e, gn, hn]);
  oe(() => {
    const S = (R) => {
      const X = Vt.current;
      if (!X) return;
      Math.hypot(R.clientX - X.startX, R.clientY - X.startY) >= t0 && (X.dragging = !0);
    }, _ = (R) => {
      const X = Vt.current;
      if (Vt.current = null, !X?.dragging || !Ce.current || ut.current) return;
      const Q = Ce.current.getBoundingClientRect();
      R.clientX >= Q.left && R.clientX <= Q.right && R.clientY >= Q.top && R.clientY <= Q.bottom && (bt.current = !0, window.setTimeout(() => {
        bt.current = !1;
      }, 0), Ct(X.activity, R.clientX, R.clientY));
    };
    return window.addEventListener("pointermove", S), window.addEventListener("pointerup", _), window.addEventListener("pointercancel", _), () => {
      window.removeEventListener("pointermove", S), window.removeEventListener("pointerup", _), window.removeEventListener("pointercancel", _);
    };
  }, [P, Ct]);
  const to = (S, _) => {
    ut.current = { activityVersionId: _.activityVersionId, handledDrop: !1 }, S.dataTransfer.setData(ba, _.activityVersionId), S.dataTransfer.setData("text/plain", _.activityVersionId), S.dataTransfer.effectAllowed = "copy";
  }, no = (S, _) => {
    const R = ut.current;
    ut.current = null, !R?.handledDrop && (S.clientX === 0 && S.clientY === 0 || Ct(_, S.clientX, S.clientY) && (bt.current = !0, window.setTimeout(() => {
      bt.current = !1;
    }, 0)));
  }, gr = (S, _) => {
    S.button === 0 && (Vt.current = {
      activity: _,
      startX: S.clientX,
      startY: S.clientY,
      dragging: !1
    });
  }, yr = (S) => {
    bt.current || Et && fn(S);
  }, oo = (S) => {
    if (!Et) {
      S.dataTransfer.dropEffect = "none";
      return;
    }
    if (S.preventDefault(), S.dataTransfer.dropEffect = "copy", !_e) return;
    const _ = pn(S.clientX, S.clientY);
    I(_);
  }, ro = (S) => {
    if (!Ce.current) return;
    const _ = S.relatedTarget;
    _ && Ce.current.contains(_) || I(null);
  }, io = (S) => {
    S.preventDefault(), I(null);
    const _ = S.dataTransfer.getData(ba) || S.dataTransfer.getData("text/plain");
    if (!_ || (S.stopPropagation(), ut.current?.activityVersionId === _ && (ut.current.handledDrop = !0), !Et)) return;
    const R = Qe.get(_);
    R && Ct(R, S.clientX, S.clientY);
  }, mr = () => {
    if (!_e) return;
    const S = Ce.current?.getBoundingClientRect();
    S && N({
      kind: "fromEmpty",
      clientX: S.left + S.width / 2,
      clientY: S.top + S.height / 2
    });
  }, Ot = pe(async (S, _) => {
    const R = async () => {
      const Q = ++vt.current, de = at(S);
      $("");
      try {
        const re = await Jm(e, S), he = at(re);
        return an.current = he, f((ke) => !ke || ke.id !== re.id ? ke : at(ke) === de ? re : { ...ke, validationErrors: re.validationErrors }), Q === vt.current && H(_), re;
      } catch (re) {
        throw Q === vt.current && (H(""), $(re instanceof Error ? re.message : String(re))), re;
      }
    }, X = Kn.current.then(R, R);
    return Kn.current = X.catch(() => {
    }), X;
  }, [e]);
  oe(() => {
    if (!K || !l || at(l) === an.current) return;
    H("Autosaving...");
    const _ = window.setTimeout(() => {
      Ot(l, "Autosaved").catch(() => {
      });
    }, n0);
    return () => window.clearTimeout(_);
  }, [K, l, Ot]);
  const xr = async () => {
    if (!(!l || ft)) {
      F("saving"), H("Saving...");
      try {
        await Ot(l, "Saved");
      } catch {
      } finally {
        F("idle");
      }
    }
  }, wr = async () => {
    if (!(!l || ft)) {
      F("promoting"), H("Promoting...");
      try {
        const S = await Qm(e, l.id), _ = await ex(e, S.versionId);
        ie(_.artifactId), H(`Published ${_.artifactVersion}`), await un();
      } catch (S) {
        H(""), $(S instanceof Error ? S.message : String(S));
      } finally {
        F("idle");
      }
    }
  }, vr = async () => {
    if (!l?.state.rootActivity || ft) return;
    const S = l, _ = at(S);
    q(null), ae(!1), H("Preparing test run...");
    try {
      F("testRunPreparing"), H("Preparing test run...");
      const R = G0(S);
      F("testRunStarting"), H("Starting test run...");
      const X = await tx(e, {
        definitionId: S.definitionId,
        snapshotId: R,
        state: S.state
      });
      q({ draftSignature: _, view: X }), H(wd(X) ? "Test run rejected" : "Test run dispatched");
    } catch (R) {
      H(""), $(R instanceof Error ? R.message : String(R));
    } finally {
      F("idle");
    }
  }, br = (S) => {
    const _ = be ? S.filter((R) => R.type === "select") : S;
    _.length !== 0 && C((R) => Kc(_, R));
  }, Nr = (S) => {
    be || A((_) => Gc(S, _));
  }, yn = (S) => !S.source || !S.target || S.source === S.target || !_e ? !1 : !S.targetHandle, Sr = (S) => {
    if (!l?.state.rootActivity || !Xe || !_e || !yn(S)) return;
    const _ = Uo(S.source, S.target, S.sourceHandle ?? "Done", S.targetHandle ?? void 0), R = Qc(_, k);
    A(R), Re(b, R);
  }, Er = () => {
    Re(b, k);
  }, so = (S, _) => {
    if (!_.nodeId || _.handleType === "target") {
      Fe.current = null;
      return;
    }
    Fe.current = {
      nodeId: _.nodeId,
      handleId: _.handleId ?? null
    };
  }, Cr = (S, _) => {
    const R = Z0(Fe.current, _);
    if (Fe.current = null, !R || !_e || _.toNode || _.toHandle || q0(S)) return;
    const X = yd(S);
    N({
      kind: "fromPort",
      sourceNodeId: R.nodeId,
      sourceHandleId: R.handleId,
      clientX: X.x,
      clientY: X.y
    });
  }, kr = (S, _) => {
    if (!_e || !yn(_)) return;
    const R = Hg(S, {
      ..._,
      sourceHandle: _.sourceHandle ?? "Done",
      targetHandle: _.targetHandle ?? void 0
    }, k, { shouldReplaceId: !1 });
    A(R), Re(b, R);
  }, Ir = (S) => {
    if (be || S.length === 0) return;
    const _ = new Set(S.map((Q) => Q.id)), R = b.filter((Q) => !_.has(Q.id)), X = k.filter((Q) => !_.has(Q.source) && !_.has(Q.target));
    C(R), A(X), M && _.has(M) && z(null), Re(R, X);
  }, jr = (S) => {
    if (be || S.length === 0) return;
    const _ = new Set(S.map((X) => X.id)), R = k.filter((X) => !_.has(X.id));
    A(R), Re(b, R);
  }, ao = pe((S) => {
    if (be) return;
    const _ = k.filter((R) => R.id !== S);
    A(_), Re(b, _);
  }, [Re, k, be, b]), co = pe((S, _, R) => {
    _e && N({ kind: "spliceEdge", edgeId: S, clientX: _, clientY: R });
  }, [_e]), _r = (S) => {
    const _ = O;
    if (!_) return;
    N(null);
    const R = hn(_.clientX, _.clientY) ?? { x: 0, y: 0 };
    if (_.kind === "fromEmpty") {
      const Q = Ht(S, R), re = [...b.map((he) => he.selected ? { ...he, selected: !1 } : he), Q.node];
      C(re), z(Q.node.id), Re(re, k, [Q.activityNode]);
      return;
    }
    if (_.kind === "fromPort") {
      const Q = b.find((Ae) => Ae.id === _.sourceNodeId), de = Q ? za(Q) : R, re = Ht(S, de), ke = [...b.map((Ae) => Ae.selected ? { ...Ae, selected: !1 } : Ae), re.node], kt = [...k, Uo(_.sourceNodeId, re.node.id, _.sourceHandleId ?? "Done")];
      C(ke), A(kt), z(re.node.id), Re(ke, kt, [re.activityNode]);
      return;
    }
    const X = k.find((Q) => Q.id === _.edgeId);
    X && gn(S, X, R);
  }, Ar = ge(() => ({
    highlightedEdgeId: j,
    deleteEdge: ao,
    requestInsertActivity: co
  }), [ao, j, co]), lo = (S, _, R) => {
    w((X) => [...X, { ownerNodeId: S.nodeId, slotId: _, label: R }]), z(null);
  }, Mr = pe((S) => {
    f((_) => {
      const R = _?.state.rootActivity;
      return !_ || !R ? _ : {
        ..._,
        state: {
          ..._.state,
          rootActivity: Wl(R, S.nodeId, () => S)
        }
      };
    });
  }, []), uo = (S) => {
    G((_) => {
      const R = new Set(_);
      return R.has(S) ? R.delete(S) : R.add(S), R;
    });
  }, fo = (S) => {
    Be((_) => _ === S ? null : _), S === "palette" ? me((_) => !_) : De((_) => !_);
  }, ho = (S) => {
    S === "palette" ? me(!1) : De(!1), Be((_) => _ === S ? null : S);
  }, Ft = (S, _) => {
    Be(null), S === "palette" ? (me(!1), le((R) => To(R + _, vn, bn))) : (De(!1), J((R) => To(R + _, Nn, Sn)));
  }, po = (S, _) => {
    _.preventDefault(), Be(null), S === "palette" ? me(!1) : De(!1);
    const R = _.clientX, X = S === "palette" ? ee : V, Q = S === "palette" ? vn : Nn, de = S === "palette" ? bn : Sn;
    document.body.classList.add("wf-side-panel-resizing");
    const re = (ke) => {
      const kt = S === "palette" ? ke.clientX - R : R - ke.clientX, Ae = To(X + kt, Q, de);
      S === "palette" ? le(Ae) : J(Ae);
    }, he = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", re), window.removeEventListener("pointerup", he), window.removeEventListener("pointercancel", he);
    };
    window.addEventListener("pointermove", re), window.addEventListener("pointerup", he), window.addEventListener("pointercancel", he);
  }, go = (S, _) => {
    _.key === "ArrowLeft" ? (_.preventDefault(), Ft(S, S === "palette" ? -ko : ko)) : _.key === "ArrowRight" ? (_.preventDefault(), Ft(S, S === "palette" ? ko : -ko)) : _.key === "Home" ? (_.preventDefault(), S === "palette" ? le(vn) : J(Nn)) : _.key === "End" && (_.preventDefault(), S === "palette" ? le(bn) : J(Sn));
  };
  if (!c || !l)
    return /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: D || "Loading workflow editor..." });
  const yo = [
    "wf-editor-body",
    fe ? "palette-collapsed" : "",
    Ee ? "inspector-collapsed" : "",
    ye === "palette" ? "palette-maximized" : "",
    ye === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), vd = {
    "--wf-palette-width": `${fe ? ka : ee}px`,
    "--wf-inspector-width": `${Ee ? ka : V}px`
  }, Ui = !fe && ye !== "inspector", Ki = !Ee && ye !== "palette", Dr = U?.draftSignature === at(l) ? U.view : null, Gi = Dr && T.startsWith("Test run") ? "" : T, bd = {
    definition: c.definition,
    draft: l,
    selectedActivity: Ne,
    selectedActivityDescriptor: St,
    selectedActivitySlots: dn,
    catalog: u,
    currentScopeOwner: Le,
    frames: p
  }, Ji = i.map((S) => {
    const _ = S.component;
    return {
      id: S.id,
      title: S.title,
      side: S.side,
      order: S.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ a.jsx(_, { context: bd })
    };
  }), Pr = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ a.jsx(Dl, { size: 15 }),
      render: Nd
    },
    ...Ji.filter((S) => S.side === "left")
  ].sort($a), $r = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ a.jsx(Tl, { size: 15 }),
      render: Sd
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ a.jsx(Hm, { size: 15 }),
      render: () => /* @__PURE__ */ a.jsx(
        y0,
        {
          context: e,
          ai: n,
          definitionId: c.definition.id,
          publishedArtifactId: Z
        }
      )
    },
    ...Ji.filter((S) => S.side === "right")
  ].sort($a), Qi = Pr.find((S) => S.id === rt) ?? Pr[0], es = $r.find((S) => S.id === ze) ?? $r[0];
  function Nd() {
    return /* @__PURE__ */ a.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: ln.map((S) => {
      const _ = se.has(S.category);
      return /* @__PURE__ */ a.jsxs("div", { className: "wf-palette-category", children: [
        /* @__PURE__ */ a.jsxs(
          "button",
          {
            type: "button",
            className: "wf-palette-category-toggle",
            role: "treeitem",
            "aria-expanded": _,
            onClick: () => uo(S.category),
            children: [
              _ ? /* @__PURE__ */ a.jsx(Pl, { size: 14 }) : /* @__PURE__ */ a.jsx(Wt, { size: 14 }),
              /* @__PURE__ */ a.jsx("span", { children: S.category }),
              /* @__PURE__ */ a.jsx("small", { children: S.activities.length })
            ]
          }
        ),
        _ ? /* @__PURE__ */ a.jsx("div", { className: "wf-palette-activities", role: "group", children: S.activities.map((R) => {
          const X = R.description?.trim(), Q = X ? `wf-palette-description-${R.activityVersionId}` : void 0, de = je(R), re = yi(R);
          return /* @__PURE__ */ a.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-activity",
              role: "treeitem",
              draggable: !0,
              title: X || je(R),
              "aria-describedby": Q,
              onClick: () => yr(R),
              onDragStart: (he) => to(he, R),
              onDragEnd: (he) => no(he, R),
              onPointerDown: (he) => gr(he, R),
              children: [
                /* @__PURE__ */ a.jsx("span", { className: "wf-activity-icon", "data-icon": re, "aria-hidden": "true", children: gd(re) }),
                /* @__PURE__ */ a.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ a.jsx("strong", { children: de }),
                  X ? /* @__PURE__ */ a.jsx("small", { id: Q, children: X }) : null
                ] }),
                /* @__PURE__ */ a.jsx(Vm, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            R.activityVersionId
          );
        }) }) : null
      ] }, S.category);
    }) });
  }
  function Sd() {
    return Ne ? /* @__PURE__ */ a.jsxs("div", { className: "wf-inspector-content", children: [
      /* @__PURE__ */ a.jsx("h3", { children: b.find((S) => S.id === Ne.nodeId)?.data.label ?? Ne.nodeId }),
      /* @__PURE__ */ a.jsxs("dl", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Node ID" }),
        /* @__PURE__ */ a.jsx("dd", { children: Ne.nodeId }),
        /* @__PURE__ */ a.jsx("dt", { children: "Activity type" }),
        /* @__PURE__ */ a.jsx("dd", { children: St?.typeName ?? Qe.get(Ne.activityVersionId)?.activityTypeKey ?? "Unknown" }),
        /* @__PURE__ */ a.jsx("dt", { children: "Activity version" }),
        /* @__PURE__ */ a.jsx("dd", { children: Ne.activityVersionId })
      ] }),
      /* @__PURE__ */ a.jsx(
        zx,
        {
          activity: Ne,
          descriptor: St,
          editors: o,
          expressionEditors: r,
          expressionDescriptors: v,
          descriptorStatus: m,
          onChange: Mr
        }
      ),
      dn.length > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-slot-list", children: [
        /* @__PURE__ */ a.jsx("span", { children: "Embedded slots" }),
        dn.map((S) => /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => lo(Ne, S.id, `${b.find((_) => _.id === Ne.nodeId)?.data.label ?? Ne.nodeId} / ${S.label}`), children: [
          S.label,
          /* @__PURE__ */ a.jsxs("small", { children: [
            S.activities.length,
            " activit",
            S.activities.length === 1 ? "y" : "ies"
          ] })
        ] }, S.id))
      ] }) : /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
    ] }) : /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
  }
  return /* @__PURE__ */ a.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ a.jsx("button", { type: "button", className: "wf-link-button", onClick: s, children: "Definitions" }),
      /* @__PURE__ */ a.jsx(Wt, { size: 14 }),
      /* @__PURE__ */ a.jsx("strong", { children: c.definition.name }),
      /* @__PURE__ */ a.jsx("span", { className: "wf-chip", children: "Draft" }),
      Gi ? /* @__PURE__ */ a.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ a.jsx(sn, { size: 13 }),
        " ",
        Gi
      ] }) : null,
      /* @__PURE__ */ a.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ a.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ a.jsx("input", { className: "wf-autosave-switch-input", type: "checkbox", checked: K, onChange: (S) => L(S.target.checked) }),
          /* @__PURE__ */ a.jsx("span", { children: "Autosave" })
        ] }),
        Qn ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => xt(n, Qn, { definition: c.definition, draft: l }), children: [
          /* @__PURE__ */ a.jsx(mt, { size: 15 }),
          " Risks"
        ] }) : null,
        eo ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => xt(n, eo, { definition: c.definition, draft: l }), children: [
          /* @__PURE__ */ a.jsx(mt, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ a.jsxs("button", { type: "button", disabled: ft, onClick: () => {
          xr();
        }, children: [
          /* @__PURE__ */ a.jsx(Om, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ a.jsxs("button", { type: "button", disabled: ft, onClick: () => {
          wr();
        }, children: [
          /* @__PURE__ */ a.jsx($l, { size: 15 }),
          " Promote"
        ] }),
        Dr ? /* @__PURE__ */ a.jsx(
          X0,
          {
            testRun: Dr,
            open: ne,
            onToggle: () => ae((S) => !S)
          }
        ) : null,
        /* @__PURE__ */ a.jsxs(
          "button",
          {
            type: "button",
            disabled: !hr,
            title: l.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              vr();
            },
            children: [
              /* @__PURE__ */ a.jsx(lr, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    D ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(nt, { size: 16 }),
      " ",
      D
    ] }) : null,
    /* @__PURE__ */ a.jsxs("div", { className: yo, style: vd, children: [
      /* @__PURE__ */ a.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ a.jsx(
            Pa,
            {
              label: "Activities panel tabs",
              tabs: Pr,
              activeTabId: Qi.id,
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
                onClick: () => fo("palette"),
                children: fe ? /* @__PURE__ */ a.jsx(Wt, { size: 14 }) : /* @__PURE__ */ a.jsx(qo, { size: 14 })
              }
            ),
            fe ? null : /* @__PURE__ */ a.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": ye === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: ye === "palette" ? "Restore" : "Maximize",
                onClick: () => ho("palette"),
                children: ye === "palette" ? /* @__PURE__ */ a.jsx(ua, { size: 14 }) : /* @__PURE__ */ a.jsx(Zo, { size: 14 })
              }
            )
          ] })
        ] }),
        Ui ? Qi.render() : null
      ] }),
      Ui && !ye ? /* @__PURE__ */ a.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": vn,
          "aria-valuemax": bn,
          "aria-valuenow": ee,
          tabIndex: 0,
          onPointerDown: (S) => po("palette", S),
          onKeyDown: (S) => go("palette", S)
        }
      ) : /* @__PURE__ */ a.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ a.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-breadcrumb", children: [
          /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
            w([]), z(null);
          }, children: "Root" }),
          p.map((S, _) => /* @__PURE__ */ a.jsxs(wt.Fragment, { children: [
            /* @__PURE__ */ a.jsx(Wt, { size: 13 }),
            /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
              w(p.slice(0, _ + 1)), z(null);
            }, children: S.label })
          ] }, `${S.ownerNodeId}-${S.slotId}-${_}`))
        ] }),
        /* @__PURE__ */ a.jsxs("div", { className: "wf-canvas", ref: Ce, onDragOver: oo, onDragLeave: ro, onDrop: io, children: [
          /* @__PURE__ */ a.jsx(sd.Provider, { value: Ar, children: /* @__PURE__ */ a.jsxs(
            El,
            {
              nodes: b,
              edges: k,
              nodeTypes: od,
              edgeTypes: rd,
              onInit: W,
              onNodesChange: br,
              onEdgesChange: Nr,
              onNodesDelete: Ir,
              onEdgesDelete: jr,
              onConnect: Sr,
              onConnectStart: _e ? so : void 0,
              onConnectEnd: _e ? Cr : void 0,
              onReconnect: _e ? kr : void 0,
              isValidConnection: yn,
              onDragOver: oo,
              onDragLeave: ro,
              onDrop: io,
              onPaneClick: () => z(null),
              onNodeClick: (S, _) => z(_.id),
              onNodeDragStop: be ? void 0 : Er,
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
                /* @__PURE__ */ a.jsx(kl, { gap: 18, size: 1 }),
                /* @__PURE__ */ a.jsx(jl, {}),
                /* @__PURE__ */ a.jsx(Al, { pannable: !0, zoomable: !0 })
              ]
            }
          ) }),
          _e && b.length === 0 ? /* @__PURE__ */ a.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => mr(), children: [
            /* @__PURE__ */ a.jsx(Hi, { size: 15 }),
            " Add activity"
          ] }) : null,
          O ? /* @__PURE__ */ a.jsx(
            B0,
            {
              clientX: O.clientX,
              clientY: O.clientY,
              activities: u,
              onPick: _r,
              onClose: () => N(null)
            }
          ) : null
        ] }),
        /* @__PURE__ */ a.jsx(W0, { draft: l })
      ] }),
      Ki && !ye ? /* @__PURE__ */ a.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Nn,
          "aria-valuemax": Sn,
          "aria-valuenow": V,
          tabIndex: 0,
          onPointerDown: (S) => po("inspector", S),
          onKeyDown: (S) => go("inspector", S)
        }
      ) : /* @__PURE__ */ a.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ a.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ a.jsx(
            Pa,
            {
              label: "Inspector panel tabs",
              tabs: $r,
              activeTabId: es.id,
              onSelect: We
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
                onClick: () => fo("inspector"),
                children: Ee ? /* @__PURE__ */ a.jsx(qo, { size: 14 }) : /* @__PURE__ */ a.jsx(Wt, { size: 14 })
              }
            ),
            Ee ? null : /* @__PURE__ */ a.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": ye === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: ye === "inspector" ? "Restore" : "Maximize",
                onClick: () => ho("inspector"),
                children: ye === "inspector" ? /* @__PURE__ */ a.jsx(ua, { size: 14 }) : /* @__PURE__ */ a.jsx(Zo, { size: 14 })
              }
            )
          ] })
        ] }),
        Ki ? es.render() : null
      ] })
    ] })
  ] });
}
function Pa({
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
function $a(e, t) {
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
          /* @__PURE__ */ a.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: gd(n.icon) }),
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
          o.status ? /* @__PURE__ */ a.jsx(Un, { status: o.status, subStatus: o.subStatus }) : null,
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
function gd(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ a.jsx($l, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ a.jsx(Tl, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ a.jsx(Fm, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ a.jsx(lr, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ a.jsx(Bm, { size: 15 });
    default:
      return /* @__PURE__ */ a.jsx(Dl, { size: 15 });
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
  } = e, h = wt.useContext(sd), [g, y] = Y(!1), [v, x, m] = Xo({ sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: c }), E = h?.highlightedEdgeId === t;
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsx(
      qn,
      {
        id: t,
        path: v,
        markerEnd: d,
        style: {
          ...l,
          strokeWidth: E ? 2.5 : l?.strokeWidth
        },
        label: f,
        labelX: x,
        labelY: m,
        labelStyle: u,
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1)
      }
    ),
    h ? /* @__PURE__ */ a.jsx(lm, { children: /* @__PURE__ */ a.jsxs(
      "div",
      {
        className: ["wf-edge-actions", g ? "visible" : "", E ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${x}px, ${m}px)` },
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1),
        children: [
          /* @__PURE__ */ a.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (p) => h.requestInsertActivity(t, p.clientX, p.clientY), children: /* @__PURE__ */ a.jsx(Hi, { size: 12 }) }),
          /* @__PURE__ */ a.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => h.deleteEdge(t), children: /* @__PURE__ */ a.jsx(fi, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function B0({ clientX: e, clientY: t, activities: n, onPick: o, onClose: r }) {
  const [i, s] = Y(""), [c, d] = Y(0), l = ce(null), f = ce(null), u = ge(() => {
    const E = i.trim().toLowerCase(), p = n.filter(fd);
    return E ? p.filter((w) => je(w).toLowerCase().includes(E) || w.activityTypeKey.toLowerCase().includes(E) || (w.category ?? "").toLowerCase().includes(E) || (w.description ?? "").toLowerCase().includes(E)) : p;
  }, [n, i]), h = ge(() => ld(u), [u]), g = ge(() => h.flatMap((E) => E.activities), [h]);
  oe(() => {
    requestAnimationFrame(() => f.current?.focus());
  }, []), oe(() => {
    const E = (w) => {
      l.current?.contains(w.target) || r();
    }, p = (w) => {
      w.key === "Escape" && r();
    };
    return document.addEventListener("mousedown", E, !0), document.addEventListener("keydown", p), () => {
      document.removeEventListener("mousedown", E, !0), document.removeEventListener("keydown", p);
    };
  }, [r]);
  const y = (E) => {
    if (E.key === "ArrowDown")
      E.preventDefault(), d((p) => Math.min(p + 1, g.length - 1));
    else if (E.key === "ArrowUp")
      E.preventDefault(), d((p) => Math.max(p - 1, 0));
    else if (E.key === "Enter") {
      E.preventDefault();
      const p = g[c];
      p && o(p);
    }
  }, v = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), x = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let m = -1;
  return /* @__PURE__ */ a.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: v, top: x }, onMouseDown: (E) => E.stopPropagation(), onClick: (E) => E.stopPropagation(), children: [
    /* @__PURE__ */ a.jsx(
      "input",
      {
        ref: f,
        type: "search",
        value: i,
        placeholder: "Search activities...",
        "aria-label": "Search activities",
        onChange: (E) => {
          s(E.target.value), d(0);
        },
        onKeyDown: y
      }
    ),
    /* @__PURE__ */ a.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: h.length === 0 ? /* @__PURE__ */ a.jsx("p", { children: "No matching activities." }) : h.map((E) => /* @__PURE__ */ a.jsxs("section", { children: [
      /* @__PURE__ */ a.jsx("h4", { children: E.category }),
      E.activities.map((p) => {
        m += 1;
        const w = m, b = w === c;
        return /* @__PURE__ */ a.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": b,
            className: b ? "active" : "",
            onMouseEnter: () => d(w),
            onClick: () => o(p),
            children: [
              /* @__PURE__ */ a.jsx("strong", { children: je(p) }),
              /* @__PURE__ */ a.jsx("small", { children: p.category || p.activityTypeKey })
            ]
          },
          p.activityVersionId
        );
      })
    ] }, E.category)) })
  ] });
}
function W0({ draft: e }) {
  return e.validationErrors.length ? /* @__PURE__ */ a.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ a.jsx(nt, { size: 14 }),
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
  const o = wd(e);
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
          o ? /* @__PURE__ */ a.jsx(nt, { size: 16 }) : /* @__PURE__ */ a.jsx(sn, { size: 16 }),
          o ? "Test run rejected" : "Test run dispatched",
          /* @__PURE__ */ a.jsx(Pl, { size: 14 })
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
          /* @__PURE__ */ a.jsx("dd", { title: Ge(e.expiresAt), children: Ge(e.expiresAt) })
        ] }) : null
      ] })
    ] }) : null
  ] });
}
function Ta(e) {
  return `${je(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function za(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function Y0(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function yd(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function q0(e) {
  const t = yd(e);
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
  return xd(at(e));
}
function md(e, t, n = []) {
  if (!e) return n;
  const o = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: o?.activityTypeKey ?? e.activityVersionId,
    displayName: o ? je(o) : void 0
  });
  for (const r of Ke(e))
    for (const i of r.activities) md(i, t, n);
  return n;
}
function K0(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function G0(e) {
  return `${e.id}-${xd(JSON.stringify(e.state))}`;
}
function xd(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function wd(e) {
  return e.status.toLowerCase() === "rejected";
}
function Ge(e) {
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
