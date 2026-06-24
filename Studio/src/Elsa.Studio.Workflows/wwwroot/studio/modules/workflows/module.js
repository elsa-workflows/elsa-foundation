import ut, { memo as ve, forwardRef as $o, useRef as ce, useEffect as ae, useCallback as he, useContext as Mn, useMemo as pe, useState as G, createContext as ti, useLayoutEffect as ml, createElement as Rr, useId as yl } from "react";
import "@tanstack/react-query";
function xl(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var vr = { exports: {} }, on = {};
var Ci;
function wl() {
  if (Ci) return on;
  Ci = 1;
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
  return on.Fragment = t, on.jsx = n, on.jsxs = n, on;
}
var ki;
function vl() {
  return ki || (ki = 1, vr.exports = wl()), vr.exports;
}
var u = vl();
function Se(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, o; n < e.length; n++)
      (o = Se(e[n])) !== "" && (t += (t && " ") + o);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var bl = { value: () => {
} };
function To() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new ho(n);
}
function ho(e) {
  this._ = e;
}
function Sl(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
ho.prototype = To.prototype = {
  constructor: ho,
  on: function(e, t) {
    var n = this._, o = Sl(e + "", n), r, i = -1, s = o.length;
    if (arguments.length < 2) {
      for (; ++i < s; ) if ((r = (e = o[i]).type) && (r = Nl(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++i < s; )
      if (r = (e = o[i]).type) n[r] = Ii(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = Ii(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new ho(e);
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
function Nl(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function Ii(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = bl, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var Lr = "http://www.w3.org/1999/xhtml";
const Mi = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: Lr,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function zo(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Mi.hasOwnProperty(t) ? { space: Mi[t], local: e } : e;
}
function El(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === Lr && t.documentElement.namespaceURI === Lr ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function _l(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function ra(e) {
  var t = zo(e);
  return (t.local ? _l : El)(t);
}
function Cl() {
}
function ni(e) {
  return e == null ? Cl : function() {
    return this.querySelector(e);
  };
}
function kl(e) {
  typeof e != "function" && (e = ni(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = new Array(s), l, a, d = 0; d < s; ++d)
      (l = i[d]) && (a = e.call(l, l.__data__, d, i)) && ("__data__" in l && (a.__data__ = l.__data__), c[d] = a);
  return new Le(o, this._parents);
}
function Il(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function Ml() {
  return [];
}
function ia(e) {
  return e == null ? Ml : function() {
    return this.querySelectorAll(e);
  };
}
function Al(e) {
  return function() {
    return Il(e.apply(this, arguments));
  };
}
function jl(e) {
  typeof e == "function" ? e = Al(e) : e = ia(e);
  for (var t = this._groups, n = t.length, o = [], r = [], i = 0; i < n; ++i)
    for (var s = t[i], c = s.length, l, a = 0; a < c; ++a)
      (l = s[a]) && (o.push(e.call(l, l.__data__, a, s)), r.push(l));
  return new Le(o, r);
}
function sa(e) {
  return function() {
    return this.matches(e);
  };
}
function aa(e) {
  return function(t) {
    return t.matches(e);
  };
}
var Dl = Array.prototype.find;
function Pl(e) {
  return function() {
    return Dl.call(this.children, e);
  };
}
function $l() {
  return this.firstElementChild;
}
function Tl(e) {
  return this.select(e == null ? $l : Pl(typeof e == "function" ? e : aa(e)));
}
var zl = Array.prototype.filter;
function Rl() {
  return Array.from(this.children);
}
function Ll(e) {
  return function() {
    return zl.call(this.children, e);
  };
}
function Hl(e) {
  return this.selectAll(e == null ? Rl : Ll(typeof e == "function" ? e : aa(e)));
}
function Vl(e) {
  typeof e != "function" && (e = sa(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = [], l, a = 0; a < s; ++a)
      (l = i[a]) && e.call(l, l.__data__, a, i) && c.push(l);
  return new Le(o, this._parents);
}
function ca(e) {
  return new Array(e.length);
}
function Ol() {
  return new Le(this._enter || this._groups.map(ca), this._parents);
}
function bo(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
bo.prototype = {
  constructor: bo,
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
function Bl(e) {
  return function() {
    return e;
  };
}
function Fl(e, t, n, o, r, i) {
  for (var s = 0, c, l = t.length, a = i.length; s < a; ++s)
    (c = t[s]) ? (c.__data__ = i[s], o[s] = c) : n[s] = new bo(e, i[s]);
  for (; s < l; ++s)
    (c = t[s]) && (r[s] = c);
}
function Wl(e, t, n, o, r, i, s) {
  var c, l, a = /* @__PURE__ */ new Map(), d = t.length, f = i.length, h = new Array(d), p;
  for (c = 0; c < d; ++c)
    (l = t[c]) && (h[c] = p = s.call(l, l.__data__, c, t) + "", a.has(p) ? r[c] = l : a.set(p, l));
  for (c = 0; c < f; ++c)
    p = s.call(e, i[c], c, i) + "", (l = a.get(p)) ? (o[c] = l, l.__data__ = i[c], a.delete(p)) : n[c] = new bo(e, i[c]);
  for (c = 0; c < d; ++c)
    (l = t[c]) && a.get(h[c]) === l && (r[c] = l);
}
function Xl(e) {
  return e.__data__;
}
function Yl(e, t) {
  if (!arguments.length) return Array.from(this, Xl);
  var n = t ? Wl : Fl, o = this._parents, r = this._groups;
  typeof e != "function" && (e = Bl(e));
  for (var i = r.length, s = new Array(i), c = new Array(i), l = new Array(i), a = 0; a < i; ++a) {
    var d = o[a], f = r[a], h = f.length, p = ql(e.call(d, d && d.__data__, a, o)), m = p.length, x = c[a] = new Array(m), v = s[a] = new Array(m), y = l[a] = new Array(h);
    n(d, f, x, v, y, p, t);
    for (var N = 0, g = 0, w, _; N < m; ++N)
      if (w = x[N]) {
        for (N >= g && (g = N + 1); !(_ = v[g]) && ++g < m; ) ;
        w._next = _ || null;
      }
  }
  return s = new Le(s, o), s._enter = c, s._exit = l, s;
}
function ql(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function Zl() {
  return new Le(this._exit || this._groups.map(ca), this._parents);
}
function Kl(e, t, n) {
  var o = this.enter(), r = this, i = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? i.remove() : n(i), o && r ? o.merge(r).order() : r;
}
function Ul(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, i = o.length, s = Math.min(r, i), c = new Array(r), l = 0; l < s; ++l)
    for (var a = n[l], d = o[l], f = a.length, h = c[l] = new Array(f), p, m = 0; m < f; ++m)
      (p = a[m] || d[m]) && (h[m] = p);
  for (; l < r; ++l)
    c[l] = n[l];
  return new Le(c, this._parents);
}
function Gl() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, i = o[r], s; --r >= 0; )
      (s = o[r]) && (i && s.compareDocumentPosition(i) ^ 4 && i.parentNode.insertBefore(s, i), i = s);
  return this;
}
function Ql(e) {
  e || (e = Jl);
  function t(f, h) {
    return f && h ? e(f.__data__, h.__data__) : !f - !h;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), i = 0; i < o; ++i) {
    for (var s = n[i], c = s.length, l = r[i] = new Array(c), a, d = 0; d < c; ++d)
      (a = s[d]) && (l[d] = a);
    l.sort(t);
  }
  return new Le(r, this._parents).order();
}
function Jl(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function eu() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function tu() {
  return Array.from(this);
}
function nu() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length; r < i; ++r) {
      var s = o[r];
      if (s) return s;
    }
  return null;
}
function ou() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function ru() {
  return !this.node();
}
function iu(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], i = 0, s = r.length, c; i < s; ++i)
      (c = r[i]) && e.call(c, c.__data__, i, r);
  return this;
}
function su(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function au(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function cu(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function lu(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function uu(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function du(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function fu(e, t) {
  var n = zo(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? au : su : typeof t == "function" ? n.local ? du : uu : n.local ? lu : cu)(n, t));
}
function la(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function hu(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function pu(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function gu(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function mu(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? hu : typeof t == "function" ? gu : pu)(e, t, n ?? "")) : Pt(this.node(), e);
}
function Pt(e, t) {
  return e.style.getPropertyValue(t) || la(e).getComputedStyle(e, null).getPropertyValue(t);
}
function yu(e) {
  return function() {
    delete this[e];
  };
}
function xu(e, t) {
  return function() {
    this[e] = t;
  };
}
function wu(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function vu(e, t) {
  return arguments.length > 1 ? this.each((t == null ? yu : typeof t == "function" ? wu : xu)(e, t)) : this.node()[e];
}
function ua(e) {
  return e.trim().split(/^|\s+/);
}
function oi(e) {
  return e.classList || new da(e);
}
function da(e) {
  this._node = e, this._names = ua(e.getAttribute("class") || "");
}
da.prototype = {
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
function fa(e, t) {
  for (var n = oi(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function ha(e, t) {
  for (var n = oi(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function bu(e) {
  return function() {
    fa(this, e);
  };
}
function Su(e) {
  return function() {
    ha(this, e);
  };
}
function Nu(e, t) {
  return function() {
    (t.apply(this, arguments) ? fa : ha)(this, e);
  };
}
function Eu(e, t) {
  var n = ua(e + "");
  if (arguments.length < 2) {
    for (var o = oi(this.node()), r = -1, i = n.length; ++r < i; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? Nu : t ? bu : Su)(n, t));
}
function _u() {
  this.textContent = "";
}
function Cu(e) {
  return function() {
    this.textContent = e;
  };
}
function ku(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function Iu(e) {
  return arguments.length ? this.each(e == null ? _u : (typeof e == "function" ? ku : Cu)(e)) : this.node().textContent;
}
function Mu() {
  this.innerHTML = "";
}
function Au(e) {
  return function() {
    this.innerHTML = e;
  };
}
function ju(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function Du(e) {
  return arguments.length ? this.each(e == null ? Mu : (typeof e == "function" ? ju : Au)(e)) : this.node().innerHTML;
}
function Pu() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function $u() {
  return this.each(Pu);
}
function Tu() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function zu() {
  return this.each(Tu);
}
function Ru(e) {
  var t = typeof e == "function" ? e : ra(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Lu() {
  return null;
}
function Hu(e, t) {
  var n = typeof e == "function" ? e : ra(e), o = t == null ? Lu : typeof t == "function" ? t : ni(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function Vu() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Ou() {
  return this.each(Vu);
}
function Bu() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Fu() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Wu(e) {
  return this.select(e ? Fu : Bu);
}
function Xu(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function Yu(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function qu(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function Zu(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, i; n < r; ++n)
        i = t[n], (!e.type || i.type === e.type) && i.name === e.name ? this.removeEventListener(i.type, i.listener, i.options) : t[++o] = i;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function Ku(e, t, n) {
  return function() {
    var o = this.__on, r, i = Yu(t);
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
function Uu(e, t, n) {
  var o = qu(e + ""), r, i = o.length, s;
  if (arguments.length < 2) {
    var c = this.node().__on;
    if (c) {
      for (var l = 0, a = c.length, d; l < a; ++l)
        for (r = 0, d = c[l]; r < i; ++r)
          if ((s = o[r]).type === d.type && s.name === d.name)
            return d.value;
    }
    return;
  }
  for (c = t ? Ku : Zu, r = 0; r < i; ++r) this.each(c(o[r], t, n));
  return this;
}
function pa(e, t, n) {
  var o = la(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function Gu(e, t) {
  return function() {
    return pa(this, e, t);
  };
}
function Qu(e, t) {
  return function() {
    return pa(this, e, t.apply(this, arguments));
  };
}
function Ju(e, t) {
  return this.each((typeof t == "function" ? Qu : Gu)(e, t));
}
function* ed() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length, s; r < i; ++r)
      (s = o[r]) && (yield s);
}
var ga = [null];
function Le(e, t) {
  this._groups = e, this._parents = t;
}
function An() {
  return new Le([[document.documentElement]], ga);
}
function td() {
  return this;
}
Le.prototype = An.prototype = {
  constructor: Le,
  select: kl,
  selectAll: jl,
  selectChild: Tl,
  selectChildren: Hl,
  filter: Vl,
  data: Yl,
  enter: Ol,
  exit: Zl,
  join: Kl,
  merge: Ul,
  selection: td,
  order: Gl,
  sort: Ql,
  call: eu,
  nodes: tu,
  node: nu,
  size: ou,
  empty: ru,
  each: iu,
  attr: fu,
  style: mu,
  property: vu,
  classed: Eu,
  text: Iu,
  html: Du,
  raise: $u,
  lower: zu,
  append: Ru,
  insert: Hu,
  remove: Ou,
  clone: Wu,
  datum: Xu,
  on: Uu,
  dispatch: Ju,
  [Symbol.iterator]: ed
};
function Re(e) {
  return typeof e == "string" ? new Le([[document.querySelector(e)]], [document.documentElement]) : new Le([[e]], ga);
}
function nd(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Xe(e, t) {
  if (e = nd(e), t === void 0 && (t = e.currentTarget), t) {
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
const od = { passive: !1 }, xn = { capture: !0, passive: !1 };
function br(e) {
  e.stopImmediatePropagation();
}
function jt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function ma(e) {
  var t = e.document.documentElement, n = Re(e).on("dragstart.drag", jt, xn);
  "onselectstart" in t ? n.on("selectstart.drag", jt, xn) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function ya(e, t) {
  var n = e.document.documentElement, o = Re(e).on("dragstart.drag", null);
  t && (o.on("click.drag", jt, xn), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const to = (e) => () => e;
function Hr(e, {
  sourceEvent: t,
  subject: n,
  target: o,
  identifier: r,
  active: i,
  x: s,
  y: c,
  dx: l,
  dy: a,
  dispatch: d
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
    _: { value: d }
  });
}
Hr.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function rd(e) {
  return !e.ctrlKey && !e.button;
}
function id() {
  return this.parentNode;
}
function sd(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function ad() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function xa() {
  var e = rd, t = id, n = sd, o = ad, r = {}, i = To("start", "drag", "end"), s = 0, c, l, a, d, f = 0;
  function h(w) {
    w.on("mousedown.drag", p).filter(o).on("touchstart.drag", v).on("touchmove.drag", y, od).on("touchend.drag touchcancel.drag", N).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function p(w, _) {
    if (!(d || !e.call(this, w, _))) {
      var E = g(this, t.call(this, w, _), w, _, "mouse");
      E && (Re(w.view).on("mousemove.drag", m, xn).on("mouseup.drag", x, xn), ma(w.view), br(w), a = !1, c = w.clientX, l = w.clientY, E("start", w));
    }
  }
  function m(w) {
    if (jt(w), !a) {
      var _ = w.clientX - c, E = w.clientY - l;
      a = _ * _ + E * E > f;
    }
    r.mouse("drag", w);
  }
  function x(w) {
    Re(w.view).on("mousemove.drag mouseup.drag", null), ya(w.view, a), jt(w), r.mouse("end", w);
  }
  function v(w, _) {
    if (e.call(this, w, _)) {
      var E = w.changedTouches, k = t.call(this, w, _), D = E.length, $, W;
      for ($ = 0; $ < D; ++$)
        (W = g(this, k, w, _, E[$].identifier, E[$])) && (br(w), W("start", w, E[$]));
    }
  }
  function y(w) {
    var _ = w.changedTouches, E = _.length, k, D;
    for (k = 0; k < E; ++k)
      (D = r[_[k].identifier]) && (jt(w), D("drag", w, _[k]));
  }
  function N(w) {
    var _ = w.changedTouches, E = _.length, k, D;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), k = 0; k < E; ++k)
      (D = r[_[k].identifier]) && (br(w), D("end", w, _[k]));
  }
  function g(w, _, E, k, D, $) {
    var W = i.copy(), M = Xe($ || E, _), z, H, b;
    if ((b = n.call(w, new Hr("beforestart", {
      sourceEvent: E,
      target: h,
      identifier: D,
      active: s,
      x: M[0],
      y: M[1],
      dx: 0,
      dy: 0,
      dispatch: W
    }), k)) != null)
      return z = b.x - M[0] || 0, H = b.y - M[1] || 0, function I(C, A, T) {
        var P = M, V;
        switch (C) {
          case "start":
            r[D] = I, V = s++;
            break;
          case "end":
            delete r[D], --s;
          // falls through
          case "drag":
            M = Xe(T || A, _), V = s;
            break;
        }
        W.call(
          C,
          w,
          new Hr(C, {
            sourceEvent: A,
            subject: b,
            target: h,
            identifier: D,
            active: V,
            x: M[0] + z,
            y: M[1] + H,
            dx: M[0] - P[0],
            dy: M[1] - P[1],
            dispatch: W
          }),
          k
        );
      };
  }
  return h.filter = function(w) {
    return arguments.length ? (e = typeof w == "function" ? w : to(!!w), h) : e;
  }, h.container = function(w) {
    return arguments.length ? (t = typeof w == "function" ? w : to(w), h) : t;
  }, h.subject = function(w) {
    return arguments.length ? (n = typeof w == "function" ? w : to(w), h) : n;
  }, h.touchable = function(w) {
    return arguments.length ? (o = typeof w == "function" ? w : to(!!w), h) : o;
  }, h.on = function() {
    var w = i.on.apply(i, arguments);
    return w === i ? h : w;
  }, h.clickDistance = function(w) {
    return arguments.length ? (f = (w = +w) * w, h) : Math.sqrt(f);
  }, h;
}
function ri(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function wa(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function jn() {
}
var wn = 0.7, So = 1 / wn, Dt = "\\s*([+-]?\\d+)\\s*", vn = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", Qe = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", cd = /^#([0-9a-f]{3,8})$/, ld = new RegExp(`^rgb\\(${Dt},${Dt},${Dt}\\)$`), ud = new RegExp(`^rgb\\(${Qe},${Qe},${Qe}\\)$`), dd = new RegExp(`^rgba\\(${Dt},${Dt},${Dt},${vn}\\)$`), fd = new RegExp(`^rgba\\(${Qe},${Qe},${Qe},${vn}\\)$`), hd = new RegExp(`^hsl\\(${vn},${Qe},${Qe}\\)$`), pd = new RegExp(`^hsla\\(${vn},${Qe},${Qe},${vn}\\)$`), Ai = {
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
ri(jn, yt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: ji,
  // Deprecated! Use color.formatHex.
  formatHex: ji,
  formatHex8: gd,
  formatHsl: md,
  formatRgb: Di,
  toString: Di
});
function ji() {
  return this.rgb().formatHex();
}
function gd() {
  return this.rgb().formatHex8();
}
function md() {
  return va(this).formatHsl();
}
function Di() {
  return this.rgb().formatRgb();
}
function yt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = cd.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? Pi(t) : n === 3 ? new $e(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? no(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? no(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = ld.exec(e)) ? new $e(t[1], t[2], t[3], 1) : (t = ud.exec(e)) ? new $e(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = dd.exec(e)) ? no(t[1], t[2], t[3], t[4]) : (t = fd.exec(e)) ? no(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = hd.exec(e)) ? zi(t[1], t[2] / 100, t[3] / 100, 1) : (t = pd.exec(e)) ? zi(t[1], t[2] / 100, t[3] / 100, t[4]) : Ai.hasOwnProperty(e) ? Pi(Ai[e]) : e === "transparent" ? new $e(NaN, NaN, NaN, 0) : null;
}
function Pi(e) {
  return new $e(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function no(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new $e(e, t, n, o);
}
function yd(e) {
  return e instanceof jn || (e = yt(e)), e ? (e = e.rgb(), new $e(e.r, e.g, e.b, e.opacity)) : new $e();
}
function Vr(e, t, n, o) {
  return arguments.length === 1 ? yd(e) : new $e(e, t, n, o ?? 1);
}
function $e(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
ri($e, Vr, wa(jn, {
  brighter(e) {
    return e = e == null ? So : Math.pow(So, e), new $e(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? wn : Math.pow(wn, e), new $e(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new $e(gt(this.r), gt(this.g), gt(this.b), No(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: $i,
  // Deprecated! Use color.formatHex.
  formatHex: $i,
  formatHex8: xd,
  formatRgb: Ti,
  toString: Ti
}));
function $i() {
  return `#${pt(this.r)}${pt(this.g)}${pt(this.b)}`;
}
function xd() {
  return `#${pt(this.r)}${pt(this.g)}${pt(this.b)}${pt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function Ti() {
  const e = No(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${gt(this.r)}, ${gt(this.g)}, ${gt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function No(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function gt(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function pt(e) {
  return e = gt(e), (e < 16 ? "0" : "") + e.toString(16);
}
function zi(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Ye(e, t, n, o);
}
function va(e) {
  if (e instanceof Ye) return new Ye(e.h, e.s, e.l, e.opacity);
  if (e instanceof jn || (e = yt(e)), !e) return new Ye();
  if (e instanceof Ye) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), i = Math.max(t, n, o), s = NaN, c = i - r, l = (i + r) / 2;
  return c ? (t === i ? s = (n - o) / c + (n < o) * 6 : n === i ? s = (o - t) / c + 2 : s = (t - n) / c + 4, c /= l < 0.5 ? i + r : 2 - i - r, s *= 60) : c = l > 0 && l < 1 ? 0 : s, new Ye(s, c, l, e.opacity);
}
function wd(e, t, n, o) {
  return arguments.length === 1 ? va(e) : new Ye(e, t, n, o ?? 1);
}
function Ye(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
ri(Ye, wd, wa(jn, {
  brighter(e) {
    return e = e == null ? So : Math.pow(So, e), new Ye(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? wn : Math.pow(wn, e), new Ye(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new $e(
      Sr(e >= 240 ? e - 240 : e + 120, r, o),
      Sr(e, r, o),
      Sr(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new Ye(Ri(this.h), oo(this.s), oo(this.l), No(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = No(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${Ri(this.h)}, ${oo(this.s) * 100}%, ${oo(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function Ri(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function oo(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function Sr(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const ii = (e) => () => e;
function vd(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function bd(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function Sd(e) {
  return (e = +e) == 1 ? ba : function(t, n) {
    return n - t ? bd(t, n, e) : ii(isNaN(t) ? n : t);
  };
}
function ba(e, t) {
  var n = t - e;
  return n ? vd(e, n) : ii(isNaN(e) ? t : e);
}
const Eo = (function e(t) {
  var n = Sd(t);
  function o(r, i) {
    var s = n((r = Vr(r)).r, (i = Vr(i)).r), c = n(r.g, i.g), l = n(r.b, i.b), a = ba(r.opacity, i.opacity);
    return function(d) {
      return r.r = s(d), r.g = c(d), r.b = l(d), r.opacity = a(d), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function Nd(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(i) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - i) + t[r] * i;
    return o;
  };
}
function Ed(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function _d(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), i = new Array(n), s;
  for (s = 0; s < o; ++s) r[s] = mn(e[s], t[s]);
  for (; s < n; ++s) i[s] = t[s];
  return function(c) {
    for (s = 0; s < o; ++s) i[s] = r[s](c);
    return i;
  };
}
function Cd(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(o) {
    return n.setTime(e * (1 - o) + t * o), n;
  };
}
function Ge(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function kd(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = mn(e[r], t[r]) : o[r] = t[r];
  return function(i) {
    for (r in n) o[r] = n[r](i);
    return o;
  };
}
var Or = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, Nr = new RegExp(Or.source, "g");
function Id(e) {
  return function() {
    return e;
  };
}
function Md(e) {
  return function(t) {
    return e(t) + "";
  };
}
function Sa(e, t) {
  var n = Or.lastIndex = Nr.lastIndex = 0, o, r, i, s = -1, c = [], l = [];
  for (e = e + "", t = t + ""; (o = Or.exec(e)) && (r = Nr.exec(t)); )
    (i = r.index) > n && (i = t.slice(n, i), c[s] ? c[s] += i : c[++s] = i), (o = o[0]) === (r = r[0]) ? c[s] ? c[s] += r : c[++s] = r : (c[++s] = null, l.push({ i: s, x: Ge(o, r) })), n = Nr.lastIndex;
  return n < t.length && (i = t.slice(n), c[s] ? c[s] += i : c[++s] = i), c.length < 2 ? l[0] ? Md(l[0].x) : Id(t) : (t = l.length, function(a) {
    for (var d = 0, f; d < t; ++d) c[(f = l[d]).i] = f.x(a);
    return c.join("");
  });
}
function mn(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? ii(t) : (n === "number" ? Ge : n === "string" ? (o = yt(t)) ? (t = o, Eo) : Sa : t instanceof yt ? Eo : t instanceof Date ? Cd : Ed(t) ? Nd : Array.isArray(t) ? _d : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? kd : Ge)(e, t);
}
var Li = 180 / Math.PI, Br = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function Na(e, t, n, o, r, i) {
  var s, c, l;
  return (s = Math.sqrt(e * e + t * t)) && (e /= s, t /= s), (l = e * n + t * o) && (n -= e * l, o -= t * l), (c = Math.sqrt(n * n + o * o)) && (n /= c, o /= c, l /= c), e * o < t * n && (e = -e, t = -t, l = -l, s = -s), {
    translateX: r,
    translateY: i,
    rotate: Math.atan2(t, e) * Li,
    skewX: Math.atan(l) * Li,
    scaleX: s,
    scaleY: c
  };
}
var ro;
function Ad(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Br : Na(t.a, t.b, t.c, t.d, t.e, t.f);
}
function jd(e) {
  return e == null || (ro || (ro = document.createElementNS("http://www.w3.org/2000/svg", "g")), ro.setAttribute("transform", e), !(e = ro.transform.baseVal.consolidate())) ? Br : (e = e.matrix, Na(e.a, e.b, e.c, e.d, e.e, e.f));
}
function Ea(e, t, n, o) {
  function r(a) {
    return a.length ? a.pop() + " " : "";
  }
  function i(a, d, f, h, p, m) {
    if (a !== f || d !== h) {
      var x = p.push("translate(", null, t, null, n);
      m.push({ i: x - 4, x: Ge(a, f) }, { i: x - 2, x: Ge(d, h) });
    } else (f || h) && p.push("translate(" + f + t + h + n);
  }
  function s(a, d, f, h) {
    a !== d ? (a - d > 180 ? d += 360 : d - a > 180 && (a += 360), h.push({ i: f.push(r(f) + "rotate(", null, o) - 2, x: Ge(a, d) })) : d && f.push(r(f) + "rotate(" + d + o);
  }
  function c(a, d, f, h) {
    a !== d ? h.push({ i: f.push(r(f) + "skewX(", null, o) - 2, x: Ge(a, d) }) : d && f.push(r(f) + "skewX(" + d + o);
  }
  function l(a, d, f, h, p, m) {
    if (a !== f || d !== h) {
      var x = p.push(r(p) + "scale(", null, ",", null, ")");
      m.push({ i: x - 4, x: Ge(a, f) }, { i: x - 2, x: Ge(d, h) });
    } else (f !== 1 || h !== 1) && p.push(r(p) + "scale(" + f + "," + h + ")");
  }
  return function(a, d) {
    var f = [], h = [];
    return a = e(a), d = e(d), i(a.translateX, a.translateY, d.translateX, d.translateY, f, h), s(a.rotate, d.rotate, f, h), c(a.skewX, d.skewX, f, h), l(a.scaleX, a.scaleY, d.scaleX, d.scaleY, f, h), a = d = null, function(p) {
      for (var m = -1, x = h.length, v; ++m < x; ) f[(v = h[m]).i] = v.x(p);
      return f.join("");
    };
  };
}
var Dd = Ea(Ad, "px, ", "px)", "deg)"), Pd = Ea(jd, ", ", ")", ")"), $d = 1e-12;
function Hi(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Td(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function zd(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const po = (function e(t, n, o) {
  function r(i, s) {
    var c = i[0], l = i[1], a = i[2], d = s[0], f = s[1], h = s[2], p = d - c, m = f - l, x = p * p + m * m, v, y;
    if (x < $d)
      y = Math.log(h / a) / t, v = function(k) {
        return [
          c + k * p,
          l + k * m,
          a * Math.exp(t * k * y)
        ];
      };
    else {
      var N = Math.sqrt(x), g = (h * h - a * a + o * x) / (2 * a * n * N), w = (h * h - a * a - o * x) / (2 * h * n * N), _ = Math.log(Math.sqrt(g * g + 1) - g), E = Math.log(Math.sqrt(w * w + 1) - w);
      y = (E - _) / t, v = function(k) {
        var D = k * y, $ = Hi(_), W = a / (n * N) * ($ * zd(t * D + _) - Td(_));
        return [
          c + W * p,
          l + W * m,
          a * $ / Hi(t * D + _)
        ];
      };
    }
    return v.duration = y * 1e3 * t / Math.SQRT2, v;
  }
  return r.rho = function(i) {
    var s = Math.max(1e-3, +i), c = s * s, l = c * c;
    return e(s, c, l);
  }, r;
})(Math.SQRT2, 2, 4);
var $t = 0, hn = 0, rn = 0, _a = 1e3, _o, pn, Co = 0, xt = 0, Ro = 0, bn = typeof performance == "object" && performance.now ? performance : Date, Ca = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function si() {
  return xt || (Ca(Rd), xt = bn.now() + Ro);
}
function Rd() {
  xt = 0;
}
function ko() {
  this._call = this._time = this._next = null;
}
ko.prototype = ka.prototype = {
  constructor: ko,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? si() : +n) + (t == null ? 0 : +t), !this._next && pn !== this && (pn ? pn._next = this : _o = this, pn = this), this._call = e, this._time = n, Fr();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Fr());
  }
};
function ka(e, t, n) {
  var o = new ko();
  return o.restart(e, t, n), o;
}
function Ld() {
  si(), ++$t;
  for (var e = _o, t; e; )
    (t = xt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --$t;
}
function Vi() {
  xt = (Co = bn.now()) + Ro, $t = hn = 0;
  try {
    Ld();
  } finally {
    $t = 0, Vd(), xt = 0;
  }
}
function Hd() {
  var e = bn.now(), t = e - Co;
  t > _a && (Ro -= t, Co = e);
}
function Vd() {
  for (var e, t = _o, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : _o = n);
  pn = e, Fr(o);
}
function Fr(e) {
  if (!$t) {
    hn && (hn = clearTimeout(hn));
    var t = e - xt;
    t > 24 ? (e < 1 / 0 && (hn = setTimeout(Vi, e - bn.now() - Ro)), rn && (rn = clearInterval(rn))) : (rn || (Co = bn.now(), rn = setInterval(Hd, _a)), $t = 1, Ca(Vi));
  }
}
function Oi(e, t, n) {
  var o = new ko();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var Od = To("start", "end", "cancel", "interrupt"), Bd = [], Ia = 0, Bi = 1, Wr = 2, go = 3, Fi = 4, Xr = 5, mo = 6;
function Lo(e, t, n, o, r, i) {
  var s = e.__transition;
  if (!s) e.__transition = {};
  else if (n in s) return;
  Fd(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: Od,
    tween: Bd,
    time: i.time,
    delay: i.delay,
    duration: i.duration,
    ease: i.ease,
    timer: null,
    state: Ia
  });
}
function ai(e, t) {
  var n = Ke(e, t);
  if (n.state > Ia) throw new Error("too late; already scheduled");
  return n;
}
function et(e, t) {
  var n = Ke(e, t);
  if (n.state > go) throw new Error("too late; already running");
  return n;
}
function Ke(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function Fd(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = ka(i, 0, n.time);
  function i(a) {
    n.state = Bi, n.timer.restart(s, n.delay, n.time), n.delay <= a && s(a - n.delay);
  }
  function s(a) {
    var d, f, h, p;
    if (n.state !== Bi) return l();
    for (d in o)
      if (p = o[d], p.name === n.name) {
        if (p.state === go) return Oi(s);
        p.state === Fi ? (p.state = mo, p.timer.stop(), p.on.call("interrupt", e, e.__data__, p.index, p.group), delete o[d]) : +d < t && (p.state = mo, p.timer.stop(), p.on.call("cancel", e, e.__data__, p.index, p.group), delete o[d]);
      }
    if (Oi(function() {
      n.state === go && (n.state = Fi, n.timer.restart(c, n.delay, n.time), c(a));
    }), n.state = Wr, n.on.call("start", e, e.__data__, n.index, n.group), n.state === Wr) {
      for (n.state = go, r = new Array(h = n.tween.length), d = 0, f = -1; d < h; ++d)
        (p = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (r[++f] = p);
      r.length = f + 1;
    }
  }
  function c(a) {
    for (var d = a < n.duration ? n.ease.call(null, a / n.duration) : (n.timer.restart(l), n.state = Xr, 1), f = -1, h = r.length; ++f < h; )
      r[f].call(e, d);
    n.state === Xr && (n.on.call("end", e, e.__data__, n.index, n.group), l());
  }
  function l() {
    n.state = mo, n.timer.stop(), delete o[t];
    for (var a in o) return;
    delete e.__transition;
  }
}
function yo(e, t) {
  var n = e.__transition, o, r, i = !0, s;
  if (n) {
    t = t == null ? null : t + "";
    for (s in n) {
      if ((o = n[s]).name !== t) {
        i = !1;
        continue;
      }
      r = o.state > Wr && o.state < Xr, o.state = mo, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[s];
    }
    i && delete e.__transition;
  }
}
function Wd(e) {
  return this.each(function() {
    yo(this, e);
  });
}
function Xd(e, t) {
  var n, o;
  return function() {
    var r = et(this, e), i = r.tween;
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
function Yd(e, t, n) {
  var o, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var i = et(this, e), s = i.tween;
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
function qd(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = Ke(this.node(), n).tween, r = 0, i = o.length, s; r < i; ++r)
      if ((s = o[r]).name === e)
        return s.value;
    return null;
  }
  return this.each((t == null ? Xd : Yd)(n, e, t));
}
function ci(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = et(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return Ke(r, o).value[t];
  };
}
function Ma(e, t) {
  var n;
  return (typeof t == "number" ? Ge : t instanceof yt ? Eo : (n = yt(t)) ? (t = n, Eo) : Sa)(e, t);
}
function Zd(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Kd(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Ud(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttribute(e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Gd(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttributeNS(e.space, e.local);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Qd(e, t, n) {
  var o, r, i;
  return function() {
    var s, c = n(this), l;
    return c == null ? void this.removeAttribute(e) : (s = this.getAttribute(e), l = c + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, c)));
  };
}
function Jd(e, t, n) {
  var o, r, i;
  return function() {
    var s, c = n(this), l;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (s = this.getAttributeNS(e.space, e.local), l = c + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, c)));
  };
}
function ef(e, t) {
  var n = zo(e), o = n === "transform" ? Pd : Ma;
  return this.attrTween(e, typeof t == "function" ? (n.local ? Jd : Qd)(n, o, ci(this, "attr." + e, t)) : t == null ? (n.local ? Kd : Zd)(n) : (n.local ? Gd : Ud)(n, o, t));
}
function tf(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function nf(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function of(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && nf(e, i)), n;
  }
  return r._value = t, r;
}
function rf(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && tf(e, i)), n;
  }
  return r._value = t, r;
}
function sf(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = zo(e);
  return this.tween(n, (o.local ? of : rf)(o, t));
}
function af(e, t) {
  return function() {
    ai(this, e).delay = +t.apply(this, arguments);
  };
}
function cf(e, t) {
  return t = +t, function() {
    ai(this, e).delay = t;
  };
}
function lf(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? af : cf)(t, e)) : Ke(this.node(), t).delay;
}
function uf(e, t) {
  return function() {
    et(this, e).duration = +t.apply(this, arguments);
  };
}
function df(e, t) {
  return t = +t, function() {
    et(this, e).duration = t;
  };
}
function ff(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? uf : df)(t, e)) : Ke(this.node(), t).duration;
}
function hf(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    et(this, e).ease = t;
  };
}
function pf(e) {
  var t = this._id;
  return arguments.length ? this.each(hf(t, e)) : Ke(this.node(), t).ease;
}
function gf(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    et(this, e).ease = n;
  };
}
function mf(e) {
  if (typeof e != "function") throw new Error();
  return this.each(gf(this._id, e));
}
function yf(e) {
  typeof e != "function" && (e = sa(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = [], l, a = 0; a < s; ++a)
      (l = i[a]) && e.call(l, l.__data__, a, i) && c.push(l);
  return new rt(o, this._parents, this._name, this._id);
}
function xf(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, i = Math.min(o, r), s = new Array(o), c = 0; c < i; ++c)
    for (var l = t[c], a = n[c], d = l.length, f = s[c] = new Array(d), h, p = 0; p < d; ++p)
      (h = l[p] || a[p]) && (f[p] = h);
  for (; c < o; ++c)
    s[c] = t[c];
  return new rt(s, this._parents, this._name, this._id);
}
function wf(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function vf(e, t, n) {
  var o, r, i = wf(t) ? ai : et;
  return function() {
    var s = i(this, e), c = s.on;
    c !== o && (r = (o = c).copy()).on(t, n), s.on = r;
  };
}
function bf(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Ke(this.node(), n).on.on(e) : this.each(vf(n, e, t));
}
function Sf(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function Nf() {
  return this.on("end.remove", Sf(this._id));
}
function Ef(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = ni(e));
  for (var o = this._groups, r = o.length, i = new Array(r), s = 0; s < r; ++s)
    for (var c = o[s], l = c.length, a = i[s] = new Array(l), d, f, h = 0; h < l; ++h)
      (d = c[h]) && (f = e.call(d, d.__data__, h, c)) && ("__data__" in d && (f.__data__ = d.__data__), a[h] = f, Lo(a[h], t, n, h, a, Ke(d, n)));
  return new rt(i, this._parents, t, n);
}
function _f(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = ia(e));
  for (var o = this._groups, r = o.length, i = [], s = [], c = 0; c < r; ++c)
    for (var l = o[c], a = l.length, d, f = 0; f < a; ++f)
      if (d = l[f]) {
        for (var h = e.call(d, d.__data__, f, l), p, m = Ke(d, n), x = 0, v = h.length; x < v; ++x)
          (p = h[x]) && Lo(p, t, n, x, h, m);
        i.push(h), s.push(d);
      }
  return new rt(i, s, t, n);
}
var Cf = An.prototype.constructor;
function kf() {
  return new Cf(this._groups, this._parents);
}
function If(e, t) {
  var n, o, r;
  return function() {
    var i = Pt(this, e), s = (this.style.removeProperty(e), Pt(this, e));
    return i === s ? null : i === n && s === o ? r : r = t(n = i, o = s);
  };
}
function Aa(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Mf(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = Pt(this, e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Af(e, t, n) {
  var o, r, i;
  return function() {
    var s = Pt(this, e), c = n(this), l = c + "";
    return c == null && (l = c = (this.style.removeProperty(e), Pt(this, e))), s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, c));
  };
}
function jf(e, t) {
  var n, o, r, i = "style." + t, s = "end." + i, c;
  return function() {
    var l = et(this, e), a = l.on, d = l.value[i] == null ? c || (c = Aa(t)) : void 0;
    (a !== n || r !== d) && (o = (n = a).copy()).on(s, r = d), l.on = o;
  };
}
function Df(e, t, n) {
  var o = (e += "") == "transform" ? Dd : Ma;
  return t == null ? this.styleTween(e, If(e, o)).on("end.style." + e, Aa(e)) : typeof t == "function" ? this.styleTween(e, Af(e, o, ci(this, "style." + e, t))).each(jf(this._id, e)) : this.styleTween(e, Mf(e, o, t), n).on("end.style." + e, null);
}
function Pf(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function $f(e, t, n) {
  var o, r;
  function i() {
    var s = t.apply(this, arguments);
    return s !== r && (o = (r = s) && Pf(e, s, n)), o;
  }
  return i._value = t, i;
}
function Tf(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, $f(e, t, n ?? ""));
}
function zf(e) {
  return function() {
    this.textContent = e;
  };
}
function Rf(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function Lf(e) {
  return this.tween("text", typeof e == "function" ? Rf(ci(this, "text", e)) : zf(e == null ? "" : e + ""));
}
function Hf(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Vf(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Hf(r)), t;
  }
  return o._value = e, o;
}
function Of(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, Vf(e));
}
function Bf() {
  for (var e = this._name, t = this._id, n = ja(), o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], c = s.length, l, a = 0; a < c; ++a)
      if (l = s[a]) {
        var d = Ke(l, t);
        Lo(l, e, n, a, s, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new rt(o, this._parents, e, n);
}
function Ff() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(i, s) {
    var c = { value: s }, l = { value: function() {
      --r === 0 && i();
    } };
    n.each(function() {
      var a = et(this, o), d = a.on;
      d !== e && (t = (e = d).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(l)), a.on = t;
    }), r === 0 && i();
  });
}
var Wf = 0;
function rt(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function ja() {
  return ++Wf;
}
var nt = An.prototype;
rt.prototype = {
  constructor: rt,
  select: Ef,
  selectAll: _f,
  selectChild: nt.selectChild,
  selectChildren: nt.selectChildren,
  filter: yf,
  merge: xf,
  selection: kf,
  transition: Bf,
  call: nt.call,
  nodes: nt.nodes,
  node: nt.node,
  size: nt.size,
  empty: nt.empty,
  each: nt.each,
  on: bf,
  attr: ef,
  attrTween: sf,
  style: Df,
  styleTween: Tf,
  text: Lf,
  textTween: Of,
  remove: Nf,
  tween: qd,
  delay: lf,
  duration: ff,
  ease: pf,
  easeVarying: mf,
  end: Ff,
  [Symbol.iterator]: nt[Symbol.iterator]
};
function Xf(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var Yf = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: Xf
};
function qf(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function Zf(e) {
  var t, n;
  e instanceof rt ? (t = e._id, e = e._name) : (t = ja(), (n = Yf).time = si(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], c = s.length, l, a = 0; a < c; ++a)
      (l = s[a]) && Lo(l, e, t, a, s, n || qf(l, t));
  return new rt(o, this._parents, e, t);
}
An.prototype.interrupt = Wd;
An.prototype.transition = Zf;
const io = (e) => () => e;
function Kf(e, {
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
function ot(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
ot.prototype = {
  constructor: ot,
  scale: function(e) {
    return e === 1 ? this : new ot(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new ot(this.k, this.x + this.k * e, this.y + this.k * t);
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
var Ho = new ot(1, 0, 0);
Da.prototype = ot.prototype;
function Da(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return Ho;
  return e.__zoom;
}
function Er(e) {
  e.stopImmediatePropagation();
}
function sn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Uf(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function Gf() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function Wi() {
  return this.__zoom || Ho;
}
function Qf(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function Jf() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function eh(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], i = e.invertY(t[0][1]) - n[0][1], s = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    s > i ? (i + s) / 2 : Math.min(0, i) || Math.max(0, s)
  );
}
function Pa() {
  var e = Uf, t = Gf, n = eh, o = Qf, r = Jf, i = [0, 1 / 0], s = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, l = po, a = To("start", "zoom", "end"), d, f, h, p = 500, m = 150, x = 0, v = 10;
  function y(b) {
    b.property("__zoom", Wi).on("wheel.zoom", D, { passive: !1 }).on("mousedown.zoom", $).on("dblclick.zoom", W).filter(r).on("touchstart.zoom", M).on("touchmove.zoom", z).on("touchend.zoom touchcancel.zoom", H).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  y.transform = function(b, I, C, A) {
    var T = b.selection ? b.selection() : b;
    T.property("__zoom", Wi), b !== T ? _(b, I, C, A) : T.interrupt().each(function() {
      E(this, arguments).event(A).start().zoom(null, typeof I == "function" ? I.apply(this, arguments) : I).end();
    });
  }, y.scaleBy = function(b, I, C, A) {
    y.scaleTo(b, function() {
      var T = this.__zoom.k, P = typeof I == "function" ? I.apply(this, arguments) : I;
      return T * P;
    }, C, A);
  }, y.scaleTo = function(b, I, C, A) {
    y.transform(b, function() {
      var T = t.apply(this, arguments), P = this.__zoom, V = C == null ? w(T) : typeof C == "function" ? C.apply(this, arguments) : C, B = P.invert(V), O = typeof I == "function" ? I.apply(this, arguments) : I;
      return n(g(N(P, O), V, B), T, s);
    }, C, A);
  }, y.translateBy = function(b, I, C, A) {
    y.transform(b, function() {
      return n(this.__zoom.translate(
        typeof I == "function" ? I.apply(this, arguments) : I,
        typeof C == "function" ? C.apply(this, arguments) : C
      ), t.apply(this, arguments), s);
    }, null, A);
  }, y.translateTo = function(b, I, C, A, T) {
    y.transform(b, function() {
      var P = t.apply(this, arguments), V = this.__zoom, B = A == null ? w(P) : typeof A == "function" ? A.apply(this, arguments) : A;
      return n(Ho.translate(B[0], B[1]).scale(V.k).translate(
        typeof I == "function" ? -I.apply(this, arguments) : -I,
        typeof C == "function" ? -C.apply(this, arguments) : -C
      ), P, s);
    }, A, T);
  };
  function N(b, I) {
    return I = Math.max(i[0], Math.min(i[1], I)), I === b.k ? b : new ot(I, b.x, b.y);
  }
  function g(b, I, C) {
    var A = I[0] - C[0] * b.k, T = I[1] - C[1] * b.k;
    return A === b.x && T === b.y ? b : new ot(b.k, A, T);
  }
  function w(b) {
    return [(+b[0][0] + +b[1][0]) / 2, (+b[0][1] + +b[1][1]) / 2];
  }
  function _(b, I, C, A) {
    b.on("start.zoom", function() {
      E(this, arguments).event(A).start();
    }).on("interrupt.zoom end.zoom", function() {
      E(this, arguments).event(A).end();
    }).tween("zoom", function() {
      var T = this, P = arguments, V = E(T, P).event(A), B = t.apply(T, P), O = C == null ? w(B) : typeof C == "function" ? C.apply(T, P) : C, U = Math.max(B[1][0] - B[0][0], B[1][1] - B[0][1]), Z = T.__zoom, ne = typeof I == "function" ? I.apply(T, P) : I, se = l(Z.invert(O).concat(U / Z.k), ne.invert(O).concat(U / ne.k));
      return function(K) {
        if (K === 1) K = ne;
        else {
          var R = se(K), Y = U / R[2];
          K = new ot(Y, O[0] - R[0] * Y, O[1] - R[1] * Y);
        }
        V.zoom(null, K);
      };
    });
  }
  function E(b, I, C) {
    return !C && b.__zooming || new k(b, I);
  }
  function k(b, I) {
    this.that = b, this.args = I, this.active = 0, this.sourceEvent = null, this.extent = t.apply(b, I), this.taps = 0;
  }
  k.prototype = {
    event: function(b) {
      return b && (this.sourceEvent = b), this;
    },
    start: function() {
      return ++this.active === 1 && (this.that.__zooming = this, this.emit("start")), this;
    },
    zoom: function(b, I) {
      return this.mouse && b !== "mouse" && (this.mouse[1] = I.invert(this.mouse[0])), this.touch0 && b !== "touch" && (this.touch0[1] = I.invert(this.touch0[0])), this.touch1 && b !== "touch" && (this.touch1[1] = I.invert(this.touch1[0])), this.that.__zoom = I, this.emit("zoom"), this;
    },
    end: function() {
      return --this.active === 0 && (delete this.that.__zooming, this.emit("end")), this;
    },
    emit: function(b) {
      var I = Re(this.that).datum();
      a.call(
        b,
        this.that,
        new Kf(b, {
          sourceEvent: this.sourceEvent,
          target: y,
          transform: this.that.__zoom,
          dispatch: a
        }),
        I
      );
    }
  };
  function D(b, ...I) {
    if (!e.apply(this, arguments)) return;
    var C = E(this, I).event(b), A = this.__zoom, T = Math.max(i[0], Math.min(i[1], A.k * Math.pow(2, o.apply(this, arguments)))), P = Xe(b);
    if (C.wheel)
      (C.mouse[0][0] !== P[0] || C.mouse[0][1] !== P[1]) && (C.mouse[1] = A.invert(C.mouse[0] = P)), clearTimeout(C.wheel);
    else {
      if (A.k === T) return;
      C.mouse = [P, A.invert(P)], yo(this), C.start();
    }
    sn(b), C.wheel = setTimeout(V, m), C.zoom("mouse", n(g(N(A, T), C.mouse[0], C.mouse[1]), C.extent, s));
    function V() {
      C.wheel = null, C.end();
    }
  }
  function $(b, ...I) {
    if (h || !e.apply(this, arguments)) return;
    var C = b.currentTarget, A = E(this, I, !0).event(b), T = Re(b.view).on("mousemove.zoom", O, !0).on("mouseup.zoom", U, !0), P = Xe(b, C), V = b.clientX, B = b.clientY;
    ma(b.view), Er(b), A.mouse = [P, this.__zoom.invert(P)], yo(this), A.start();
    function O(Z) {
      if (sn(Z), !A.moved) {
        var ne = Z.clientX - V, se = Z.clientY - B;
        A.moved = ne * ne + se * se > x;
      }
      A.event(Z).zoom("mouse", n(g(A.that.__zoom, A.mouse[0] = Xe(Z, C), A.mouse[1]), A.extent, s));
    }
    function U(Z) {
      T.on("mousemove.zoom mouseup.zoom", null), ya(Z.view, A.moved), sn(Z), A.event(Z).end();
    }
  }
  function W(b, ...I) {
    if (e.apply(this, arguments)) {
      var C = this.__zoom, A = Xe(b.changedTouches ? b.changedTouches[0] : b, this), T = C.invert(A), P = C.k * (b.shiftKey ? 0.5 : 2), V = n(g(N(C, P), A, T), t.apply(this, I), s);
      sn(b), c > 0 ? Re(this).transition().duration(c).call(_, V, A, b) : Re(this).call(y.transform, V, A, b);
    }
  }
  function M(b, ...I) {
    if (e.apply(this, arguments)) {
      var C = b.touches, A = C.length, T = E(this, I, b.changedTouches.length === A).event(b), P, V, B, O;
      for (Er(b), V = 0; V < A; ++V)
        B = C[V], O = Xe(B, this), O = [O, this.__zoom.invert(O), B.identifier], T.touch0 ? !T.touch1 && T.touch0[2] !== O[2] && (T.touch1 = O, T.taps = 0) : (T.touch0 = O, P = !0, T.taps = 1 + !!d);
      d && (d = clearTimeout(d)), P && (T.taps < 2 && (f = O[0], d = setTimeout(function() {
        d = null;
      }, p)), yo(this), T.start());
    }
  }
  function z(b, ...I) {
    if (this.__zooming) {
      var C = E(this, I).event(b), A = b.changedTouches, T = A.length, P, V, B, O;
      for (sn(b), P = 0; P < T; ++P)
        V = A[P], B = Xe(V, this), C.touch0 && C.touch0[2] === V.identifier ? C.touch0[0] = B : C.touch1 && C.touch1[2] === V.identifier && (C.touch1[0] = B);
      if (V = C.that.__zoom, C.touch1) {
        var U = C.touch0[0], Z = C.touch0[1], ne = C.touch1[0], se = C.touch1[1], K = (K = ne[0] - U[0]) * K + (K = ne[1] - U[1]) * K, R = (R = se[0] - Z[0]) * R + (R = se[1] - Z[1]) * R;
        V = N(V, Math.sqrt(K / R)), B = [(U[0] + ne[0]) / 2, (U[1] + ne[1]) / 2], O = [(Z[0] + se[0]) / 2, (Z[1] + se[1]) / 2];
      } else if (C.touch0) B = C.touch0[0], O = C.touch0[1];
      else return;
      C.zoom("touch", n(g(V, B, O), C.extent, s));
    }
  }
  function H(b, ...I) {
    if (this.__zooming) {
      var C = E(this, I).event(b), A = b.changedTouches, T = A.length, P, V;
      for (Er(b), h && clearTimeout(h), h = setTimeout(function() {
        h = null;
      }, p), P = 0; P < T; ++P)
        V = A[P], C.touch0 && C.touch0[2] === V.identifier ? delete C.touch0 : C.touch1 && C.touch1[2] === V.identifier && delete C.touch1;
      if (C.touch1 && !C.touch0 && (C.touch0 = C.touch1, delete C.touch1), C.touch0) C.touch0[1] = this.__zoom.invert(C.touch0[0]);
      else if (C.end(), C.taps === 2 && (V = Xe(V, this), Math.hypot(f[0] - V[0], f[1] - V[1]) < v)) {
        var B = Re(this).on("dblclick.zoom");
        B && B.apply(this, arguments);
      }
    }
  }
  return y.wheelDelta = function(b) {
    return arguments.length ? (o = typeof b == "function" ? b : io(+b), y) : o;
  }, y.filter = function(b) {
    return arguments.length ? (e = typeof b == "function" ? b : io(!!b), y) : e;
  }, y.touchable = function(b) {
    return arguments.length ? (r = typeof b == "function" ? b : io(!!b), y) : r;
  }, y.extent = function(b) {
    return arguments.length ? (t = typeof b == "function" ? b : io([[+b[0][0], +b[0][1]], [+b[1][0], +b[1][1]]]), y) : t;
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
    return arguments.length ? (v = +b, y) : v;
  }, y;
}
const He = {
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
}, Sn = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], $a = ["Enter", " ", "Escape"], Ta = {
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
var Tt;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(Tt || (Tt = {}));
var mt;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(mt || (mt = {}));
var Nn;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Nn || (Nn = {}));
const za = {
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
var ct;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(ct || (ct = {}));
var Io;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(Io || (Io = {}));
var te;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(te || (te = {}));
const Xi = {
  [te.Left]: te.Right,
  [te.Right]: te.Left,
  [te.Top]: te.Bottom,
  [te.Bottom]: te.Top
};
function Ra(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const La = (e) => "id" in e && "source" in e && "target" in e, th = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), li = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), Dn = (e, t = [0, 0]) => {
  const { width: n, height: o } = it(e), r = e.origin ?? t, i = n * r[0], s = o * r[1];
  return {
    x: e.position.x - i,
    y: e.position.y - s
  };
}, nh = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const i = typeof r == "string";
    let s = !t.nodeLookup && !i ? r : void 0;
    t.nodeLookup && (s = i ? t.nodeLookup.get(r) : li(r) ? r : t.nodeLookup.get(r.id));
    const c = s ? Mo(s, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return Vo(o, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return Oo(n);
}, Pn = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = Vo(n, Mo(r)), o = !0);
  }), o ? Oo(n) : { x: 0, y: 0, width: 0, height: 0 };
}, ui = (e, t, [n, o, r] = [0, 0, 1], i = !1, s = !1) => {
  const c = {
    ...Wt(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, l = [];
  for (const a of e.values()) {
    const { measured: d, selectable: f = !0, hidden: h = !1 } = a;
    if (s && !f || h)
      continue;
    const p = d.width ?? a.width ?? a.initialWidth ?? null, m = d.height ?? a.height ?? a.initialHeight ?? null, x = En(c, Rt(a)), v = (p ?? 0) * (m ?? 0), y = i && x > 0;
    (!a.internals.handleBounds || y || x >= v || a.dragging) && l.push(a);
  }
  return l;
}, oh = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function rh(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function ih({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: i }, s) {
  if (e.size === 0)
    return !0;
  const c = rh(e, s), l = Pn(c), a = fi(l, t, n, s?.minZoom ?? r, s?.maxZoom ?? i, s?.padding ?? 0.1);
  return await o.setViewport(a, {
    duration: s?.duration,
    ease: s?.ease,
    interpolate: s?.interpolate
  }), !0;
}
function Ha({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: i }) {
  const s = n.get(e), c = s.parentId ? n.get(s.parentId) : void 0, { x: l, y: a } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, d = s.origin ?? o;
  let f = s.extent || r;
  if (s.extent === "parent" && !s.expandParent)
    if (!c)
      i?.("005", He.error005());
    else {
      const p = c.measured.width, m = c.measured.height;
      p && m && (f = [
        [l, a],
        [l + p, a + m]
      ]);
    }
  else c && vt(s.extent) && (f = [
    [s.extent[0][0] + l, s.extent[0][1] + a],
    [s.extent[1][0] + l, s.extent[1][1] + a]
  ]);
  const h = vt(f) ? wt(t, f, s.measured) : t;
  return (s.measured.width === void 0 || s.measured.height === void 0) && i?.("015", He.error015()), {
    position: {
      x: h.x - l + (s.measured.width ?? 0) * d[0],
      y: h.y - a + (s.measured.height ?? 0) * d[1]
    },
    positionAbsolute: h
  };
}
async function sh({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const i = new Set(e.map((h) => h.id)), s = [];
  for (const h of n) {
    if (h.deletable === !1)
      continue;
    const p = i.has(h.id), m = !p && h.parentId && s.find((x) => x.id === h.parentId);
    (p || m) && s.push(h);
  }
  const c = new Set(t.map((h) => h.id)), l = o.filter((h) => h.deletable !== !1), d = oh(s, l);
  for (const h of l)
    c.has(h.id) && !d.find((m) => m.id === h.id) && d.push(h);
  if (!r)
    return {
      edges: d,
      nodes: s
    };
  const f = await r({
    nodes: s,
    edges: d
  });
  return typeof f == "boolean" ? f ? { edges: d, nodes: s } : { edges: [], nodes: [] } : f;
}
const zt = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), wt = (e = { x: 0, y: 0 }, t, n) => ({
  x: zt(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: zt(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Va(e, t, n) {
  const { width: o, height: r } = it(n), { x: i, y: s } = n.internals.positionAbsolute;
  return wt(e, [
    [i, s],
    [i + o, s + r]
  ], t);
}
const Yi = (e, t, n) => e < t ? zt(Math.abs(e - t), 1, t) / t : e > n ? -zt(Math.abs(e - n), 1, t) / t : 0, di = (e, t, n = 15, o = 40) => {
  const r = Yi(e.x, o, t.width - o) * n, i = Yi(e.y, o, t.height - o) * n;
  return [r, i];
}, Vo = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), Yr = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), Oo = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), Rt = (e, t = [0, 0]) => {
  const { x: n, y: o } = li(e) ? e.internals.positionAbsolute : Dn(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, Mo = (e, t = [0, 0]) => {
  const { x: n, y: o } = li(e) ? e.internals.positionAbsolute : Dn(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, Oa = (e, t) => Oo(Vo(Yr(e), Yr(t))), En = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, qi = (e) => qe(e.width) && qe(e.height) && qe(e.x) && qe(e.y), qe = (e) => !isNaN(e) && isFinite(e), Ba = (e, t) => (n, o) => {
}, $n = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), Wt = ({ x: e, y: t }, [n, o, r], i = !1, s = [1, 1]) => {
  const c = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return i ? $n(c, s) : c;
}, Lt = ({ x: e, y: t }, [n, o, r]) => ({
  x: e * r + n,
  y: t * r + o
});
function kt(e, t) {
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
function ah(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = kt(e, n), r = kt(e, t);
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
    const o = kt(e.top ?? e.y ?? 0, n), r = kt(e.bottom ?? e.y ?? 0, n), i = kt(e.left ?? e.x ?? 0, t), s = kt(e.right ?? e.x ?? 0, t);
    return { top: o, right: s, bottom: r, left: i, x: i + s, y: o + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function ch(e, t, n, o, r, i) {
  const { x: s, y: c } = Lt(e, [t, n, o]), { x: l, y: a } = Lt({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), d = r - l, f = i - a;
  return {
    left: Math.floor(s),
    top: Math.floor(c),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const fi = (e, t, n, o, r, i) => {
  const s = ah(i, t, n), c = (t - s.x) / e.width, l = (n - s.y) / e.height, a = Math.min(c, l), d = zt(a, o, r), f = e.x + e.width / 2, h = e.y + e.height / 2, p = t / 2 - f * d, m = n / 2 - h * d, x = ch(e, p, m, d, t, n), v = {
    left: Math.min(x.left - s.left, 0),
    top: Math.min(x.top - s.top, 0),
    right: Math.min(x.right - s.right, 0),
    bottom: Math.min(x.bottom - s.bottom, 0)
  };
  return {
    x: p - v.left + v.right,
    y: m - v.top + v.bottom,
    zoom: d
  };
}, _n = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function vt(e) {
  return e != null && e !== "parent";
}
function it(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function Fa(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function Wa(e, t = { width: 0, height: 0 }, n, o, r) {
  const i = { ...e }, s = o.get(n);
  if (s) {
    const c = s.origin || r;
    i.x += s.internals.positionAbsolute.x - (t.width ?? 0) * c[0], i.y += s.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return i;
}
function Zi(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function lh() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function uh(e) {
  return { ...Ta, ...e || {} };
}
function yn(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: i, y: s } = Ze(e), c = Wt({ x: i - (r?.left ?? 0), y: s - (r?.top ?? 0) }, o), { x: l, y: a } = n ? $n(c, t) : c;
  return {
    xSnapped: l,
    ySnapped: a,
    ...c
  };
}
const hi = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), Xa = (e) => e?.getRootNode?.() || window?.document, dh = ["INPUT", "SELECT", "TEXTAREA"];
function Ya(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : dh.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const qa = (e) => "clientX" in e, Ze = (e, t) => {
  const n = qa(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, Ki = (e, t, n, o, r) => {
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
      ...hi(s)
    };
  });
};
function Za({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: i, targetControlX: s, targetControlY: c }) {
  const l = e * 0.125 + r * 0.375 + s * 0.375 + n * 0.125, a = t * 0.125 + i * 0.375 + c * 0.375 + o * 0.125, d = Math.abs(l - e), f = Math.abs(a - t);
  return [l, a, d, f];
}
function so(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function Ui({ pos: e, x1: t, y1: n, x2: o, y2: r, c: i }) {
  switch (e) {
    case te.Left:
      return [t - so(t - o, i), n];
    case te.Right:
      return [t + so(o - t, i), n];
    case te.Top:
      return [t, n - so(n - r, i)];
    case te.Bottom:
      return [t, n + so(r - n, i)];
  }
}
function Ka({ sourceX: e, sourceY: t, sourcePosition: n = te.Bottom, targetX: o, targetY: r, targetPosition: i = te.Top, curvature: s = 0.25 }) {
  const [c, l] = Ui({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: s
  }), [a, d] = Ui({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: s
  }), [f, h, p, m] = Za({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: r,
    sourceControlX: c,
    sourceControlY: l,
    targetControlX: a,
    targetControlY: d
  });
  return [
    `M${e},${t} C${c},${l} ${a},${d} ${o},${r}`,
    f,
    h,
    p,
    m
  ];
}
function Ua({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, i = n < e ? n + r : n - r, s = Math.abs(o - t) / 2, c = o < t ? o + s : o - s;
  return [i, c, r, s];
}
function fh({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: i = "basic" }) {
  if (i === "manual")
    return o;
  const s = r && n ? o + 1e3 : o, c = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return s + c;
}
function hh({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const i = Vo(Mo(e), Mo(t));
  i.x === i.x2 && (i.x2 += 1), i.y === i.y2 && (i.y2 += 1);
  const s = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return En(s, Oo(i)) > 0;
}
const Ga = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, ph = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), gh = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", He.error006()), t;
  const o = n.getEdgeId || Ga;
  let r;
  return La(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, ph(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, mh = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: r, ...i } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", He.error006()), n;
  if (!n.find((a) => a.id === e.id))
    return o.onError?.("007", He.error007(r)), n;
  const c = o.getEdgeId || Ga, l = {
    ...i,
    id: o.shouldReplaceId ? c(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((a) => a.id !== r).concat(l);
};
function Qa({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, i, s, c] = Ua({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, i, s, c];
}
const Gi = {
  [te.Left]: { x: -1, y: 0 },
  [te.Right]: { x: 1, y: 0 },
  [te.Top]: { x: 0, y: -1 },
  [te.Bottom]: { x: 0, y: 1 }
}, yh = ({ source: e, sourcePosition: t = te.Bottom, target: n }) => t === te.Left || t === te.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, Qi = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function xh({ source: e, sourcePosition: t = te.Bottom, target: n, targetPosition: o = te.Top, center: r, offset: i, stepPosition: s }) {
  const c = Gi[t], l = Gi[o], a = { x: e.x + c.x * i, y: e.y + c.y * i }, d = { x: n.x + l.x * i, y: n.y + l.y * i }, f = yh({
    source: a,
    sourcePosition: t,
    target: d
  }), h = f.x !== 0 ? "x" : "y", p = f[h];
  let m = [], x, v;
  const y = { x: 0, y: 0 }, N = { x: 0, y: 0 }, [, , g, w] = Ua({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[h] * l[h] === -1) {
    h === "x" ? (x = r.x ?? a.x + (d.x - a.x) * s, v = r.y ?? (a.y + d.y) / 2) : (x = r.x ?? (a.x + d.x) / 2, v = r.y ?? a.y + (d.y - a.y) * s);
    const D = [
      { x, y: a.y },
      { x, y: d.y }
    ], $ = [
      { x: a.x, y: v },
      { x: d.x, y: v }
    ];
    c[h] === p ? m = h === "x" ? D : $ : m = h === "x" ? $ : D;
  } else {
    const D = [{ x: a.x, y: d.y }], $ = [{ x: d.x, y: a.y }];
    if (h === "x" ? m = c.x === p ? $ : D : m = c.y === p ? D : $, t === o) {
      const b = Math.abs(e[h] - n[h]);
      if (b <= i) {
        const I = Math.min(i - 1, i - b);
        c[h] === p ? y[h] = (a[h] > e[h] ? -1 : 1) * I : N[h] = (d[h] > n[h] ? -1 : 1) * I;
      }
    }
    if (t !== o) {
      const b = h === "x" ? "y" : "x", I = c[h] === l[b], C = a[b] > d[b], A = a[b] < d[b];
      (c[h] === 1 && (!I && C || I && A) || c[h] !== 1 && (!I && A || I && C)) && (m = h === "x" ? D : $);
    }
    const W = { x: a.x + y.x, y: a.y + y.y }, M = { x: d.x + N.x, y: d.y + N.y }, z = Math.max(Math.abs(W.x - m[0].x), Math.abs(M.x - m[0].x)), H = Math.max(Math.abs(W.y - m[0].y), Math.abs(M.y - m[0].y));
    z >= H ? (x = (W.x + M.x) / 2, v = m[0].y) : (x = m[0].x, v = (W.y + M.y) / 2);
  }
  const _ = { x: a.x + y.x, y: a.y + y.y }, E = { x: d.x + N.x, y: d.y + N.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ..._.x !== m[0].x || _.y !== m[0].y ? [_] : [],
    ...m,
    ...E.x !== m[m.length - 1].x || E.y !== m[m.length - 1].y ? [E] : [],
    n
  ], x, v, g, w];
}
function wh(e, t, n, o) {
  const r = Math.min(Qi(e, t) / 2, Qi(t, n) / 2, o), { x: i, y: s } = t;
  if (e.x === i && i === n.x || e.y === s && s === n.y)
    return `L${i} ${s}`;
  if (e.y === s) {
    const a = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${i + r * a},${s}Q ${i},${s} ${i},${s + r * d}`;
  }
  const c = e.x < n.x ? 1 : -1, l = e.y < n.y ? -1 : 1;
  return `L ${i},${s + r * l}Q ${i},${s} ${i + r * c},${s}`;
}
function Ao({ sourceX: e, sourceY: t, sourcePosition: n = te.Bottom, targetX: o, targetY: r, targetPosition: i = te.Top, borderRadius: s = 5, centerX: c, centerY: l, offset: a = 20, stepPosition: d = 0.5 }) {
  const [f, h, p, m, x] = xh({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: i,
    center: { x: c, y: l },
    offset: a,
    stepPosition: d
  });
  let v = `M${f[0].x} ${f[0].y}`;
  for (let y = 1; y < f.length - 1; y++)
    v += wh(f[y - 1], f[y], f[y + 1], s);
  return v += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [v, h, p, m, x];
}
function Ji(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function vh(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!Ji(t) || !Ji(n))
    return null;
  const o = t.internals.handleBounds || es(t.handles), r = n.internals.handleBounds || es(n.handles), i = ts(o?.source ?? [], e.sourceHandle), s = ts(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === Tt.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!i || !s)
    return e.onError?.("008", He.error008(i ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = i?.position || te.Bottom, l = s?.position || te.Top, a = bt(t, i, c), d = bt(n, s, l);
  return {
    sourceX: a.x,
    sourceY: a.y,
    targetX: d.x,
    targetY: d.y,
    sourcePosition: c,
    targetPosition: l
  };
}
function es(e) {
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
function bt(e, t, n = te.Left, o = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, i = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: s, height: c } = t ?? it(e);
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
function ts(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function qr(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function bh(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const i = /* @__PURE__ */ new Set();
  return e.reduce((s, c) => ([c.markerStart || o, c.markerEnd || r].forEach((l) => {
    if (l && typeof l == "object") {
      const a = qr(l, t);
      i.has(a) || (s.push({ id: a, color: l.color || n, ...l }), i.add(a));
    }
  }), s), []).sort((s, c) => s.id.localeCompare(c.id));
}
const Ja = 1e3, Sh = 10, pi = {
  nodeOrigin: [0, 0],
  nodeExtent: Sn,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Nh = {
  ...pi,
  checkEquality: !0
};
function gi(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function Eh(e, t, n) {
  const o = gi(pi, n);
  for (const r of e.values())
    if (r.parentId)
      yi(r, e, t, o);
    else {
      const i = Dn(r, o.nodeOrigin), s = vt(r.extent) ? r.extent : o.nodeExtent, c = wt(i, s, it(r));
      r.internals.positionAbsolute = c;
    }
}
function _h(e, t) {
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
function mi(e) {
  return e === "manual";
}
function Zr(e, t, n, o = {}) {
  const r = gi(Nh, o), i = { i: 0 }, s = new Map(t), c = r?.elevateNodesOnSelect && !mi(r.zIndexMode) ? Ja : 0;
  let l = e.length > 0, a = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = s.get(d.id);
    if (r.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const h = Dn(d, r.nodeOrigin), p = vt(d.extent) ? d.extent : r.nodeExtent, m = wt(h, p, it(d));
      f = {
        ...r.defaults,
        ...d,
        measured: {
          width: d.measured?.width,
          height: d.measured?.height
        },
        internals: {
          positionAbsolute: m,
          // if user re-initializes the node or removes `measured` for whatever reason, we reset the handleBounds so that the node gets re-measured
          handleBounds: _h(d, f),
          z: ec(d, c, r.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (l = !1), d.parentId && yi(f, t, n, o, i), a ||= d.selected ?? !1;
  }
  return { nodesInitialized: l, hasSelectedNodes: a };
}
function Ch(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function yi(e, t, n, o, r) {
  const { elevateNodesOnSelect: i, nodeOrigin: s, nodeExtent: c, zIndexMode: l } = gi(pi, o), a = e.parentId, d = t.get(a);
  if (!d) {
    console.warn(`Parent node ${a} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Ch(e, n), r && !d.parentId && d.internals.rootParentIndex === void 0 && l === "auto" && (d.internals.rootParentIndex = ++r.i, d.internals.z = d.internals.z + r.i * Sh), r && d.internals.rootParentIndex !== void 0 && (r.i = d.internals.rootParentIndex);
  const f = i && !mi(l) ? Ja : 0, { x: h, y: p, z: m } = kh(e, d, s, c, f, l), { positionAbsolute: x } = e.internals, v = h !== x.x || p !== x.y;
  (v || m !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: v ? { x: h, y: p } : x,
      z: m
    }
  });
}
function ec(e, t, n) {
  const o = qe(e.zIndex) ? e.zIndex : 0;
  return mi(n) ? o : o + (e.selected ? t : 0);
}
function kh(e, t, n, o, r, i) {
  const { x: s, y: c } = t.internals.positionAbsolute, l = it(e), a = Dn(e, n), d = vt(e.extent) ? wt(a, e.extent, l) : a;
  let f = wt({ x: s + d.x, y: c + d.y }, o, l);
  e.extent === "parent" && (f = Va(f, l, t));
  const h = ec(e, r, i), p = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: p >= h ? p + 1 : h
  };
}
function xi(e, t, n, o = [0, 0]) {
  const r = [], i = /* @__PURE__ */ new Map();
  for (const s of e) {
    const c = t.get(s.parentId);
    if (!c)
      continue;
    const l = i.get(s.parentId)?.expandedRect ?? Rt(c), a = Oa(l, s.rect);
    i.set(s.parentId, { expandedRect: a, parent: c });
  }
  return i.size > 0 && i.forEach(({ expandedRect: s, parent: c }, l) => {
    const a = c.internals.positionAbsolute, d = it(c), f = c.origin ?? o, h = s.x < a.x ? Math.round(Math.abs(a.x - s.x)) : 0, p = s.y < a.y ? Math.round(Math.abs(a.y - s.y)) : 0, m = Math.max(d.width, Math.round(s.width)), x = Math.max(d.height, Math.round(s.height)), v = (m - d.width) * f[0], y = (x - d.height) * f[1];
    (h > 0 || p > 0 || v || y) && (r.push({
      id: l,
      type: "position",
      position: {
        x: c.position.x - h + v,
        y: c.position.y - p + y
      }
    }), n.get(l)?.forEach((N) => {
      e.some((g) => g.id === N.id) || r.push({
        id: N.id,
        type: "position",
        position: {
          x: N.position.x + h,
          y: N.position.y + p
        }
      });
    })), (d.width < s.width || d.height < s.height || h || p) && r.push({
      id: l,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: m + (h ? f[0] * h - v : 0),
        height: x + (p ? f[1] * p - y : 0)
      }
    });
  }), r;
}
function Ih(e, t, n, o, r, i, s) {
  const c = o?.querySelector(".xyflow__viewport");
  let l = !1;
  if (!c)
    return { changes: [], updatedInternals: l };
  const a = [], d = window.getComputedStyle(c), { m22: f } = new window.DOMMatrixReadOnly(d.transform), h = [];
  for (const p of e.values()) {
    const m = t.get(p.id);
    if (!m)
      continue;
    if (m.hidden) {
      t.set(m.id, {
        ...m,
        internals: {
          ...m.internals,
          handleBounds: void 0
        }
      }), l = !0;
      continue;
    }
    const x = hi(p.nodeElement), v = m.measured.width !== x.width || m.measured.height !== x.height;
    if (!!(x.width && x.height && (v || !m.internals.handleBounds || p.force))) {
      const N = p.nodeElement.getBoundingClientRect(), g = vt(m.extent) ? m.extent : i;
      let { positionAbsolute: w } = m.internals;
      m.parentId && m.extent === "parent" ? w = Va(w, x, t.get(m.parentId)) : g && (w = wt(w, g, x));
      const _ = {
        ...m,
        measured: x,
        internals: {
          ...m.internals,
          positionAbsolute: w,
          handleBounds: {
            source: Ki("source", p.nodeElement, N, f, m.id),
            target: Ki("target", p.nodeElement, N, f, m.id)
          }
        }
      };
      t.set(m.id, _), m.parentId && yi(_, t, n, { nodeOrigin: r, zIndexMode: s }), l = !0, v && (a.push({
        id: m.id,
        type: "dimensions",
        dimensions: x
      }), m.expandParent && m.parentId && h.push({
        id: m.id,
        parentId: m.parentId,
        rect: Rt(_, r)
      }));
    }
  }
  if (h.length > 0) {
    const p = xi(h, t, n, r);
    a.push(...p);
  }
  return { changes: a, updatedInternals: l };
}
async function Mh({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: i }) {
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
function ns(e, t, n, o, r, i) {
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
function tc(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: i, sourceHandle: s = null, targetHandle: c = null } = o, l = { edgeId: o.id, source: r, target: i, sourceHandle: s, targetHandle: c }, a = `${r}-${s}--${i}-${c}`, d = `${i}-${c}--${r}-${s}`;
    ns("source", l, d, e, r, s), ns("target", l, a, e, i, c), t.set(o.id, o);
  }
}
function nc(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : nc(n, t) : !1;
}
function os(e, t, n) {
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
function Ah(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [i, s] of e)
    if ((s.selected || s.id === o) && (!s.parentId || !nc(s, e)) && (s.draggable || t && typeof s.draggable > "u")) {
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
function _r({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
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
function jh({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const i = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, s = $n(i, t);
  return {
    x: s.x - i.x,
    y: s.y - i.y
  };
}
function Dh({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let i = { x: null, y: null }, s = 0, c = /* @__PURE__ */ new Map(), l = !1, a = { x: 0, y: 0 }, d = null, f = !1, h = null, p = !1, m = !1, x = null;
  function v({ noDragClassName: N, handleSelector: g, domNode: w, isSelectable: _, nodeId: E, nodeClickDistance: k = 0 }) {
    h = Re(w);
    function D({ x: z, y: H }) {
      const { nodeLookup: b, nodeExtent: I, snapGrid: C, snapToGrid: A, nodeOrigin: T, onNodeDrag: P, onSelectionDrag: V, onError: B, updateNodePositions: O } = t();
      i = { x: z, y: H };
      let U = !1;
      const Z = c.size > 1, ne = Z && I ? Yr(Pn(c)) : null, se = Z && A ? jh({
        dragItems: c,
        snapGrid: C,
        x: z,
        y: H
      }) : null;
      for (const [K, R] of c) {
        if (!b.has(K))
          continue;
        let Y = { x: z - R.distance.x, y: H - R.distance.y };
        A && (Y = se ? {
          x: Math.round(Y.x + se.x),
          y: Math.round(Y.y + se.y)
        } : $n(Y, C));
        let re = null;
        if (Z && I && !R.extent && ne) {
          const { positionAbsolute: ee } = R.internals, oe = ee.x - ne.x + I[0][0], L = ee.x + R.measured.width - ne.x2 + I[1][0], J = ee.y - ne.y + I[0][1], fe = ee.y + R.measured.height - ne.y2 + I[1][1];
          re = [
            [oe, J],
            [L, fe]
          ];
        }
        const { position: ie, positionAbsolute: q } = Ha({
          nodeId: K,
          nextPosition: Y,
          nodeLookup: b,
          nodeExtent: re || I,
          nodeOrigin: T,
          onError: B
        });
        U = U || R.position.x !== ie.x || R.position.y !== ie.y, R.position = ie, R.internals.positionAbsolute = q;
      }
      if (m = m || U, !!U && (O(c, !0), x && (o || P || !E && V))) {
        const [K, R] = _r({
          nodeId: E,
          dragItems: c,
          nodeLookup: b
        });
        o?.(x, c, K, R), P?.(x, K, R), E || V?.(x, R);
      }
    }
    async function $() {
      if (!d)
        return;
      const { transform: z, panBy: H, autoPanSpeed: b, autoPanOnNodeDrag: I } = t();
      if (!I) {
        l = !1, cancelAnimationFrame(s);
        return;
      }
      const [C, A] = di(a, d, b);
      (C !== 0 || A !== 0) && (i.x = (i.x ?? 0) - C / z[2], i.y = (i.y ?? 0) - A / z[2], await H({ x: C, y: A }) && D(i)), s = requestAnimationFrame($);
    }
    function W(z) {
      const { nodeLookup: H, multiSelectionActive: b, nodesDraggable: I, transform: C, snapGrid: A, snapToGrid: T, selectNodesOnDrag: P, onNodeDragStart: V, onSelectionDragStart: B, unselectNodesAndEdges: O } = t();
      f = !0, (!P || !_) && !b && E && (H.get(E)?.selected || O()), _ && P && E && e?.(E);
      const U = yn(z.sourceEvent, { transform: C, snapGrid: A, snapToGrid: T, containerBounds: d });
      if (i = U, c = Ah(H, I, U, E), c.size > 0 && (n || V || !E && B)) {
        const [Z, ne] = _r({
          nodeId: E,
          dragItems: c,
          nodeLookup: H
        });
        n?.(z.sourceEvent, c, Z, ne), V?.(z.sourceEvent, Z, ne), E || B?.(z.sourceEvent, ne);
      }
    }
    const M = xa().clickDistance(k).on("start", (z) => {
      const { domNode: H, nodeDragThreshold: b, transform: I, snapGrid: C, snapToGrid: A } = t();
      d = H?.getBoundingClientRect() || null, p = !1, m = !1, x = z.sourceEvent, b === 0 && W(z), i = yn(z.sourceEvent, { transform: I, snapGrid: C, snapToGrid: A, containerBounds: d }), a = Ze(z.sourceEvent, d);
    }).on("drag", (z) => {
      const { autoPanOnNodeDrag: H, transform: b, snapGrid: I, snapToGrid: C, nodeDragThreshold: A, nodeLookup: T } = t(), P = yn(z.sourceEvent, { transform: b, snapGrid: I, snapToGrid: C, containerBounds: d });
      if (x = z.sourceEvent, (z.sourceEvent.type === "touchmove" && z.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      E && !T.has(E)) && (p = !0), !p) {
        if (!l && H && f && (l = !0, $()), !f) {
          const V = Ze(z.sourceEvent, d), B = V.x - a.x, O = V.y - a.y;
          Math.sqrt(B * B + O * O) > A && W(z);
        }
        (i.x !== P.xSnapped || i.y !== P.ySnapped) && c && f && (a = Ze(z.sourceEvent, d), D(P));
      }
    }).on("end", (z) => {
      if (!f || p) {
        p && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (l = !1, f = !1, cancelAnimationFrame(s), c.size > 0) {
        const { nodeLookup: H, updateNodePositions: b, onNodeDragStop: I, onSelectionDragStop: C } = t();
        if (m && (b(c, !1), m = !1), r || I || !E && C) {
          const [A, T] = _r({
            nodeId: E,
            dragItems: c,
            nodeLookup: H,
            dragging: !1
          });
          r?.(z.sourceEvent, c, A, T), I?.(z.sourceEvent, A, T), E || C?.(z.sourceEvent, T);
        }
      }
    }).filter((z) => {
      const H = z.target;
      return !z.button && (!N || !os(H, `.${N}`, w)) && (!g || os(H, g, w));
    });
    h.call(M);
  }
  function y() {
    h?.on(".drag", null);
  }
  return {
    update: v,
    destroy: y
  };
}
function Ph(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const i of t.values())
    En(r, Rt(i)) > 0 && o.push(i);
  return o;
}
const $h = 250;
function Th(e, t, n, o) {
  let r = [], i = 1 / 0;
  const s = Ph(e, n, t + $h);
  for (const c of s) {
    const l = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const a of l) {
      if (o.nodeId === a.nodeId && o.type === a.type && o.id === a.id)
        continue;
      const { x: d, y: f } = bt(c, a, a.position, !0), h = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(f - e.y, 2));
      h > t || (h < i ? (r = [{ ...a, x: d, y: f }], i = h) : h === i && r.push({ ...a, x: d, y: f }));
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
function oc(e, t, n, o, r, i = !1) {
  const s = o.get(e);
  if (!s)
    return null;
  const c = r === "strict" ? s.internals.handleBounds?.[t] : [...s.internals.handleBounds?.source ?? [], ...s.internals.handleBounds?.target ?? []], l = (n ? c?.find((a) => a.id === n) : c?.[0]) ?? null;
  return l && i ? { ...l, ...bt(s, l, l.position, !0) } : l;
}
function rc(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function zh(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const ic = () => !0;
function Rh(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: i, isTarget: s, domNode: c, nodeLookup: l, lib: a, autoPanOnConnect: d, flowId: f, panBy: h, cancelConnection: p, onConnectStart: m, onConnect: x, onConnectEnd: v, isValidConnection: y = ic, onReconnectEnd: N, updateConnection: g, getTransform: w, getFromHandle: _, autoPanSpeed: E, dragThreshold: k = 1, handleDomNode: D }) {
  const $ = Xa(e.target);
  let W = 0, M;
  const { x: z, y: H } = Ze(e), b = rc(i, D), I = c?.getBoundingClientRect();
  let C = !1;
  if (!I || !b)
    return;
  const A = oc(r, b, o, l, t);
  if (!A)
    return;
  let T = Ze(e, I), P = !1, V = null, B = !1, O = null;
  function U() {
    if (!d || !I)
      return;
    const [ie, q] = di(T, I, E);
    h({ x: ie, y: q }), W = requestAnimationFrame(U);
  }
  const Z = {
    ...A,
    nodeId: r,
    type: b,
    position: A.position
  }, ne = l.get(r);
  let K = {
    inProgress: !0,
    isValid: null,
    from: bt(ne, Z, te.Left, !0),
    fromHandle: Z,
    fromPosition: Z.position,
    fromNode: ne,
    to: T,
    toHandle: null,
    toPosition: Xi[Z.position],
    toNode: null,
    pointer: T
  };
  function R() {
    C = !0, g(K), m?.(e, { nodeId: r, handleId: o, handleType: b });
  }
  k === 0 && R();
  function Y(ie) {
    if (!C) {
      const { x: fe, y: xe } = Ze(ie), Oe = fe - z, we = xe - H;
      if (!(Oe * Oe + we * we > k * k))
        return;
      R();
    }
    if (!_() || !Z) {
      re(ie);
      return;
    }
    const q = w();
    T = Ze(ie, I), M = Th(Wt(T, q, !1, [1, 1]), n, l, Z), P || (U(), P = !0);
    const ee = sc(ie, {
      handle: M,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: s ? "target" : "source",
      isValidConnection: y,
      doc: $,
      lib: a,
      flowId: f,
      nodeLookup: l
    });
    O = ee.handleDomNode, V = ee.connection, B = zh(!!M, ee.isValid);
    const oe = l.get(r), L = oe ? bt(oe, Z, te.Left, !0) : K.from, J = {
      ...K,
      from: L,
      isValid: B,
      to: ee.toHandle && B ? Lt({ x: ee.toHandle.x, y: ee.toHandle.y }, q) : T,
      toHandle: ee.toHandle,
      toPosition: B && ee.toHandle ? ee.toHandle.position : Xi[Z.position],
      toNode: ee.toHandle ? l.get(ee.toHandle.nodeId) : null,
      pointer: T
    };
    g(J), K = J;
  }
  function re(ie) {
    if (!("touches" in ie && ie.touches.length > 0)) {
      if (C) {
        (M || O) && V && B && x?.(V);
        const { inProgress: q, ...ee } = K, oe = {
          ...ee,
          toPosition: K.toHandle ? K.toPosition : null
        };
        v?.(ie, oe), i && N?.(ie, oe);
      }
      p(), cancelAnimationFrame(W), P = !1, B = !1, V = null, O = null, $.removeEventListener("mousemove", Y), $.removeEventListener("mouseup", re), $.removeEventListener("touchmove", Y), $.removeEventListener("touchend", re);
    }
  }
  $.addEventListener("mousemove", Y), $.addEventListener("mouseup", re), $.addEventListener("touchmove", Y), $.addEventListener("touchend", re);
}
function sc(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: i, doc: s, lib: c, flowId: l, isValidConnection: a = ic, nodeLookup: d }) {
  const f = i === "target", h = t ? s.querySelector(`.${c}-flow__handle[data-id="${l}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: p, y: m } = Ze(e), x = s.elementFromPoint(p, m), v = x?.classList.contains(`${c}-flow__handle`) ? x : h, y = {
    handleDomNode: v,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (v) {
    const N = rc(void 0, v), g = v.getAttribute("data-nodeid"), w = v.getAttribute("data-handleid"), _ = v.classList.contains("connectable"), E = v.classList.contains("connectableend");
    if (!g || !N)
      return y;
    const k = {
      source: f ? g : o,
      sourceHandle: f ? w : r,
      target: f ? o : g,
      targetHandle: f ? r : w
    };
    y.connection = k;
    const $ = _ && E && (n === Tt.Strict ? f && N === "source" || !f && N === "target" : g !== o || w !== r);
    y.isValid = $ && a(k), y.toHandle = oc(g, N, w, d, n, !0);
  }
  return y;
}
const Kr = {
  onPointerDown: Rh,
  isValid: sc
};
function Lh({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = Re(e);
  function i({ translateExtent: c, width: l, height: a, zoomStep: d = 1, pannable: f = !0, zoomable: h = !0, inversePan: p = !1 }) {
    const m = (g) => {
      if (g.sourceEvent.type !== "wheel" || !t)
        return;
      const w = n(), _ = g.sourceEvent.ctrlKey && _n() ? 10 : 1, E = -g.sourceEvent.deltaY * (g.sourceEvent.deltaMode === 1 ? 0.05 : g.sourceEvent.deltaMode ? 1 : 2e-3) * d, k = w[2] * Math.pow(2, E * _);
      t.scaleTo(k);
    };
    let x = [0, 0];
    const v = (g) => {
      (g.sourceEvent.type === "mousedown" || g.sourceEvent.type === "touchstart") && (x = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ]);
    }, y = (g) => {
      const w = n();
      if (g.sourceEvent.type !== "mousemove" && g.sourceEvent.type !== "touchmove" || !t)
        return;
      const _ = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ], E = [_[0] - x[0], _[1] - x[1]];
      x = _;
      const k = o() * Math.max(w[2], Math.log(w[2])) * (p ? -1 : 1), D = {
        x: w[0] - E[0] * k,
        y: w[1] - E[1] * k
      }, $ = [
        [0, 0],
        [l, a]
      ];
      t.setViewportConstrained({
        x: D.x,
        y: D.y,
        zoom: w[2]
      }, $, c);
    }, N = Pa().on("start", v).on("zoom", f ? y : null).on("zoom.wheel", h ? m : null);
    r.call(N, {});
  }
  function s() {
    r.on("zoom", null);
  }
  return {
    update: i,
    destroy: s,
    pointer: Xe
  };
}
const Bo = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), Cr = ({ x: e, y: t, zoom: n }) => Ho.translate(e, t).scale(n), Mt = (e, t) => e.target.closest(`.${t}`), ac = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Hh = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, kr = (e, t = 0, n = Hh, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, cc = (e) => {
  const t = e.ctrlKey && _n() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Vh({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: i, zoomOnPinch: s, onPanZoomStart: c, onPanZoom: l, onPanZoomEnd: a }) {
  return (d) => {
    if (Mt(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && s) {
      const v = Xe(d), y = cc(d), N = f * Math.pow(2, y);
      o.scaleTo(n, N, v, d);
      return;
    }
    const h = d.deltaMode === 1 ? 20 : 1;
    let p = r === mt.Vertical ? 0 : d.deltaX * h, m = r === mt.Horizontal ? 0 : d.deltaY * h;
    !_n() && d.shiftKey && r !== mt.Vertical && (p = d.deltaY * h, m = 0), o.translateBy(
      n,
      -(p / f) * i,
      -(m / f) * i,
      // @ts-ignore
      { internal: !0 }
    );
    const x = Bo(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (l?.(d, x), e.panScrollTimeout = setTimeout(() => {
      a?.(d, x), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(d, x));
  };
}
function Oh({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const i = o.type === "wheel", s = !t && i && !o.ctrlKey, c = Mt(o, e);
    if (o.ctrlKey && i && c && o.preventDefault(), s || c)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function Bh({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = Bo(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function Fh({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (i) => {
    e.usedRightMouseButton = !!(n && ac(t, e.mouseButton ?? 0)), i.sourceEvent?.sync || o([i.transform.x, i.transform.y, i.transform.k]), r && !i.sourceEvent?.internal && r?.(i.sourceEvent, Bo(i.transform));
  };
}
function Wh({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: i }) {
  return (s) => {
    if (!s.sourceEvent?.internal && (e.isZoomingOrPanning = !1, i && ac(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && s.sourceEvent && i(s.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
      const c = Bo(s.transform);
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
function Xh({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: i, userSelectionActive: s, noWheelClassName: c, noPanClassName: l, lib: a, connectionInProgress: d }) {
  return (f) => {
    const h = e || t, p = n && f.ctrlKey, m = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (Mt(f, `${a}-flow__node`) || Mt(f, `${a}-flow__edge`)))
      return !0;
    if (!o && !h && !r && !i && !n || s || d && !m || Mt(f, c) && m || Mt(f, l) && (!m || r && m && !e) || !n && f.ctrlKey && m)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!h && !r && !p && m || !o && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(o) && !o.includes(f.button) && f.type === "mousedown")
      return !1;
    const x = Array.isArray(o) && o.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || m) && x;
  };
}
function Yh({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: i, onPanZoomStart: s, onPanZoomEnd: c, onDraggingChange: l }) {
  const a = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = Pa().scaleExtent([t, n]).translateExtent(o), h = Re(e).call(f);
  N({
    x: r.x,
    y: r.y,
    zoom: zt(r.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], o);
  const p = h.on("wheel.zoom"), m = h.on("dblclick.zoom");
  f.wheelDelta(cc);
  async function x(M, z) {
    return h ? new Promise((H) => {
      f?.interpolate(z?.interpolate === "linear" ? mn : po).transform(kr(h, z?.duration, z?.ease, () => H(!0)), M);
    }) : !1;
  }
  function v({ noWheelClassName: M, noPanClassName: z, onPaneContextMenu: H, userSelectionActive: b, panOnScroll: I, panOnDrag: C, panOnScrollMode: A, panOnScrollSpeed: T, preventScrolling: P, zoomOnPinch: V, zoomOnScroll: B, zoomOnDoubleClick: O, zoomActivationKeyPressed: U, lib: Z, onTransformChange: ne, connectionInProgress: se, paneClickDistance: K, selectionOnDrag: R }) {
    b && !a.isZoomingOrPanning && y();
    const Y = I && !U && !b;
    f.clickDistance(R ? 1 / 0 : !qe(K) || K < 0 ? 0 : K);
    const re = Y ? Vh({
      zoomPanValues: a,
      noWheelClassName: M,
      d3Selection: h,
      d3Zoom: f,
      panOnScrollMode: A,
      panOnScrollSpeed: T,
      zoomOnPinch: V,
      onPanZoomStart: s,
      onPanZoom: i,
      onPanZoomEnd: c
    }) : Oh({
      noWheelClassName: M,
      preventScrolling: P,
      d3ZoomHandler: p
    });
    h.on("wheel.zoom", re, { passive: !1 });
    const ie = Bh({
      zoomPanValues: a,
      onDraggingChange: l,
      onPanZoomStart: s
    });
    f.on("start", ie);
    const q = Fh({
      zoomPanValues: a,
      panOnDrag: C,
      onPaneContextMenu: !!H,
      onPanZoom: i,
      onTransformChange: ne
    });
    f.on("zoom", q);
    const ee = Wh({
      zoomPanValues: a,
      panOnDrag: C,
      panOnScroll: I,
      onPaneContextMenu: H,
      onPanZoomEnd: c,
      onDraggingChange: l
    });
    f.on("end", ee);
    const oe = Xh({
      zoomActivationKeyPressed: U,
      panOnDrag: C,
      zoomOnScroll: B,
      panOnScroll: I,
      zoomOnDoubleClick: O,
      zoomOnPinch: V,
      userSelectionActive: b,
      noPanClassName: z,
      noWheelClassName: M,
      lib: Z,
      connectionInProgress: se
    });
    f.filter(oe), O ? h.on("dblclick.zoom", m) : h.on("dblclick.zoom", null);
  }
  function y() {
    f.on("zoom", null);
  }
  async function N(M, z, H) {
    const b = Cr(M), I = f?.constrain()(b, z, H);
    return I && await x(I), I;
  }
  async function g(M, z) {
    const H = Cr(M);
    return await x(H, z), H;
  }
  function w(M) {
    if (h) {
      const z = Cr(M), H = h.property("__zoom");
      (H.k !== M.zoom || H.x !== M.x || H.y !== M.y) && f?.transform(h, z, null, { sync: !0 });
    }
  }
  function _() {
    const M = h ? Da(h.node()) : { x: 0, y: 0, k: 1 };
    return { x: M.x, y: M.y, zoom: M.k };
  }
  async function E(M, z) {
    return h ? new Promise((H) => {
      f?.interpolate(z?.interpolate === "linear" ? mn : po).scaleTo(kr(h, z?.duration, z?.ease, () => H(!0)), M);
    }) : !1;
  }
  async function k(M, z) {
    return h ? new Promise((H) => {
      f?.interpolate(z?.interpolate === "linear" ? mn : po).scaleBy(kr(h, z?.duration, z?.ease, () => H(!0)), M);
    }) : !1;
  }
  function D(M) {
    f?.scaleExtent(M);
  }
  function $(M) {
    f?.translateExtent(M);
  }
  function W(M) {
    const z = !qe(M) || M < 0 ? 0 : M;
    f?.clickDistance(z);
  }
  return {
    update: v,
    destroy: y,
    setViewport: g,
    setViewportConstrained: N,
    getViewport: _,
    scaleTo: E,
    scaleBy: k,
    setScaleExtent: D,
    setTranslateExtent: $,
    syncViewport: w,
    setClickDistance: W
  };
}
var Ht;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(Ht || (Ht = {}));
function qh({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: i }) {
  const s = e - t, c = n - o, l = [s > 0 ? 1 : s < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return s && r && (l[0] = l[0] * -1), c && i && (l[1] = l[1] * -1), l;
}
function rs(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: r
  };
}
function st(e, t) {
  return Math.max(0, t - e);
}
function at(e, t) {
  return Math.max(0, e - t);
}
function ao(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function is(e, t) {
  return e ? !t : t;
}
function Zh(e, t, n, o, r, i, s, c) {
  let { affectsX: l, affectsY: a } = t;
  const { isHorizontal: d, isVertical: f } = t, h = d && f, { xSnapped: p, ySnapped: m } = n, { minWidth: x, maxWidth: v, minHeight: y, maxHeight: N } = o, { x: g, y: w, width: _, height: E, aspectRatio: k } = e;
  let D = Math.floor(d ? p - e.pointerX : 0), $ = Math.floor(f ? m - e.pointerY : 0);
  const W = _ + (l ? -D : D), M = E + (a ? -$ : $), z = -i[0] * _, H = -i[1] * E;
  let b = ao(W, x, v), I = ao(M, y, N);
  if (s) {
    let T = 0, P = 0;
    l && D < 0 ? T = st(g + D + z, s[0][0]) : !l && D > 0 && (T = at(g + W + z, s[1][0])), a && $ < 0 ? P = st(w + $ + H, s[0][1]) : !a && $ > 0 && (P = at(w + M + H, s[1][1])), b = Math.max(b, T), I = Math.max(I, P);
  }
  if (c) {
    let T = 0, P = 0;
    l && D > 0 ? T = at(g + D, c[0][0]) : !l && D < 0 && (T = st(g + W, c[1][0])), a && $ > 0 ? P = at(w + $, c[0][1]) : !a && $ < 0 && (P = st(w + M, c[1][1])), b = Math.max(b, T), I = Math.max(I, P);
  }
  if (r) {
    if (d) {
      const T = ao(W / k, y, N) * k;
      if (b = Math.max(b, T), s) {
        let P = 0;
        !l && !a || l && !a && h ? P = at(w + H + W / k, s[1][1]) * k : P = st(w + H + (l ? D : -D) / k, s[0][1]) * k, b = Math.max(b, P);
      }
      if (c) {
        let P = 0;
        !l && !a || l && !a && h ? P = st(w + W / k, c[1][1]) * k : P = at(w + (l ? D : -D) / k, c[0][1]) * k, b = Math.max(b, P);
      }
    }
    if (f) {
      const T = ao(M * k, x, v) / k;
      if (I = Math.max(I, T), s) {
        let P = 0;
        !l && !a || a && !l && h ? P = at(g + M * k + z, s[1][0]) / k : P = st(g + (a ? $ : -$) * k + z, s[0][0]) / k, I = Math.max(I, P);
      }
      if (c) {
        let P = 0;
        !l && !a || a && !l && h ? P = st(g + M * k, c[1][0]) / k : P = at(g + (a ? $ : -$) * k, c[0][0]) / k, I = Math.max(I, P);
      }
    }
  }
  $ = $ + ($ < 0 ? I : -I), D = D + (D < 0 ? b : -b), r && (h ? W > M * k ? $ = (is(l, a) ? -D : D) / k : D = (is(l, a) ? -$ : $) * k : d ? ($ = D / k, a = l) : (D = $ * k, l = a));
  const C = l ? g + D : g, A = a ? w + $ : w;
  return {
    width: _ + (l ? -D : D),
    height: E + (a ? -$ : $),
    x: i[0] * D * (l ? -1 : 1) + C,
    y: i[1] * $ * (a ? -1 : 1) + A
  };
}
const lc = { width: 0, height: 0, x: 0, y: 0 }, Kh = {
  ...lc,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function Uh(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, i = e.measured.width ?? 0, s = e.measured.height ?? 0, c = n[0] * i, l = n[1] * s;
  return [
    [o - c, r - l],
    [o + i - c, r + s - l]
  ];
}
function Gh({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const i = Re(e);
  let s = {
    controlDirection: rs("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: a, boundaries: d, keepAspectRatio: f, resizeDirection: h, onResizeStart: p, onResize: m, onResizeEnd: x, shouldResize: v }) {
    let y = { ...lc }, N = { ...Kh };
    s = {
      boundaries: d,
      resizeDirection: h,
      keepAspectRatio: f,
      controlDirection: rs(a)
    };
    let g, w = null, _ = [], E, k, D, $ = !1;
    const W = xa().on("start", (M) => {
      const { nodeLookup: z, transform: H, snapGrid: b, snapToGrid: I, nodeOrigin: C, paneDomNode: A } = n();
      if (g = z.get(t), !g)
        return;
      w = A?.getBoundingClientRect() ?? null;
      const { xSnapped: T, ySnapped: P } = yn(M.sourceEvent, {
        transform: H,
        snapGrid: b,
        snapToGrid: I,
        containerBounds: w
      });
      y = {
        width: g.measured.width ?? 0,
        height: g.measured.height ?? 0,
        x: g.position.x ?? 0,
        y: g.position.y ?? 0
      }, N = {
        ...y,
        pointerX: T,
        pointerY: P,
        aspectRatio: y.width / y.height
      }, E = void 0, k = vt(g.extent) ? g.extent : void 0, g.parentId && (g.extent === "parent" || g.expandParent) && (E = z.get(g.parentId)), E && g.extent === "parent" && (k = [
        [0, 0],
        [E.measured.width, E.measured.height]
      ]), _ = [], D = void 0;
      for (const [V, B] of z)
        if (B.parentId === t && (_.push({
          id: V,
          position: { ...B.position },
          extent: B.extent
        }), B.extent === "parent" || B.expandParent)) {
          const O = Uh(B, g, B.origin ?? C);
          D ? D = [
            [Math.min(O[0][0], D[0][0]), Math.min(O[0][1], D[0][1])],
            [Math.max(O[1][0], D[1][0]), Math.max(O[1][1], D[1][1])]
          ] : D = O;
        }
      p?.(M, { ...y });
    }).on("drag", (M) => {
      const { transform: z, snapGrid: H, snapToGrid: b, nodeOrigin: I } = n(), C = yn(M.sourceEvent, {
        transform: z,
        snapGrid: H,
        snapToGrid: b,
        containerBounds: w
      }), A = [];
      if (!g)
        return;
      const { x: T, y: P, width: V, height: B } = y, O = {}, U = g.origin ?? I, { width: Z, height: ne, x: se, y: K } = Zh(N, s.controlDirection, C, s.boundaries, s.keepAspectRatio, U, k, D), R = Z !== V, Y = ne !== B, re = se !== T && R, ie = K !== P && Y;
      if (!re && !ie && !R && !Y)
        return;
      if ((re || ie || U[0] === 1 || U[1] === 1) && (O.x = re ? se : y.x, O.y = ie ? K : y.y, y.x = O.x, y.y = O.y, _.length > 0)) {
        const L = se - T, J = K - P;
        for (const fe of _)
          fe.position = {
            x: fe.position.x - L + U[0] * (Z - V),
            y: fe.position.y - J + U[1] * (ne - B)
          }, A.push(fe);
      }
      if ((R || Y) && (O.width = R && (!s.resizeDirection || s.resizeDirection === "horizontal") ? Z : y.width, O.height = Y && (!s.resizeDirection || s.resizeDirection === "vertical") ? ne : y.height, y.width = O.width, y.height = O.height), E && g.expandParent) {
        const L = U[0] * (O.width ?? 0);
        O.x && O.x < L && (y.x = L, N.x = N.x - (O.x - L));
        const J = U[1] * (O.height ?? 0);
        O.y && O.y < J && (y.y = J, N.y = N.y - (O.y - J));
      }
      const q = qh({
        width: y.width,
        prevWidth: V,
        height: y.height,
        prevHeight: B,
        affectsX: s.controlDirection.affectsX,
        affectsY: s.controlDirection.affectsY
      }), ee = { ...y, direction: q };
      v?.(M, ee) !== !1 && ($ = !0, m?.(M, ee), o(O, A));
    }).on("end", (M) => {
      $ && (x?.(M, { ...y }), r?.({ ...y }), $ = !1);
    });
    i.call(W);
  }
  function l() {
    i.on(".drag", null);
  }
  return {
    update: c,
    destroy: l
  };
}
var Ir = { exports: {} }, Mr = {}, Ar = { exports: {} }, jr = {};
var ss;
function Qh() {
  if (ss) return jr;
  ss = 1;
  var e = ut;
  function t(f, h) {
    return f === h && (f !== 0 || 1 / f === 1 / h) || f !== f && h !== h;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, i = e.useLayoutEffect, s = e.useDebugValue;
  function c(f, h) {
    var p = h(), m = o({ inst: { value: p, getSnapshot: h } }), x = m[0].inst, v = m[1];
    return i(
      function() {
        x.value = p, x.getSnapshot = h, l(x) && v({ inst: x });
      },
      [f, p, h]
    ), r(
      function() {
        return l(x) && v({ inst: x }), f(function() {
          l(x) && v({ inst: x });
        });
      },
      [f]
    ), s(p), p;
  }
  function l(f) {
    var h = f.getSnapshot;
    f = f.value;
    try {
      var p = h();
      return !n(f, p);
    } catch {
      return !0;
    }
  }
  function a(f, h) {
    return h();
  }
  var d = typeof window > "u" || typeof window.document > "u" || typeof window.document.createElement > "u" ? a : c;
  return jr.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : d, jr;
}
var as;
function Jh() {
  return as || (as = 1, Ar.exports = Qh()), Ar.exports;
}
var cs;
function ep() {
  if (cs) return Mr;
  cs = 1;
  var e = ut, t = Jh();
  function n(a, d) {
    return a === d && (a !== 0 || 1 / a === 1 / d) || a !== a && d !== d;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, i = e.useRef, s = e.useEffect, c = e.useMemo, l = e.useDebugValue;
  return Mr.useSyncExternalStoreWithSelector = function(a, d, f, h, p) {
    var m = i(null);
    if (m.current === null) {
      var x = { hasValue: !1, value: null };
      m.current = x;
    } else x = m.current;
    m = c(
      function() {
        function y(E) {
          if (!N) {
            if (N = !0, g = E, E = h(E), p !== void 0 && x.hasValue) {
              var k = x.value;
              if (p(k, E))
                return w = k;
            }
            return w = E;
          }
          if (k = w, o(g, E)) return k;
          var D = h(E);
          return p !== void 0 && p(k, D) ? (g = E, k) : (g = E, w = D);
        }
        var N = !1, g, w, _ = f === void 0 ? null : f;
        return [
          function() {
            return y(d());
          },
          _ === null ? void 0 : function() {
            return y(_());
          }
        ];
      },
      [d, f, h, p]
    );
    var v = r(a, m[0], m[1]);
    return s(
      function() {
        x.hasValue = !0, x.value = v;
      },
      [v]
    ), l(v), v;
  }, Mr;
}
var ls;
function tp() {
  return ls || (ls = 1, Ir.exports = ep()), Ir.exports;
}
var np = tp();
const op = /* @__PURE__ */ xl(np), rp = {}, us = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (d, f) => {
    const h = typeof d == "function" ? d(t) : d;
    if (!Object.is(h, t)) {
      const p = t;
      t = f ?? (typeof h != "object" || h === null) ? h : Object.assign({}, t, h), n.forEach((m) => m(t, p));
    }
  }, r = () => t, l = { setState: o, getState: r, getInitialState: () => a, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (rp ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, a = t = e(o, r, l);
  return l;
}, ip = (e) => e ? us(e) : us, { useDebugValue: sp } = ut, { useSyncExternalStoreWithSelector: ap } = op, cp = (e) => e;
function uc(e, t = cp, n) {
  const o = ap(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return sp(o), o;
}
const ds = (e, t) => {
  const n = ip(e), o = (r, i = t) => uc(n, r, i);
  return Object.assign(o, n), o;
}, lp = (e, t) => e ? ds(e, t) : ds;
function ge(e, t) {
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
var Dr = { exports: {} }, Me = {};
var fs;
function up() {
  if (fs) return Me;
  fs = 1;
  var e = ut;
  function t(l) {
    var a = "https://react.dev/errors/" + l;
    if (1 < arguments.length) {
      a += "?args[]=" + encodeURIComponent(arguments[1]);
      for (var d = 2; d < arguments.length; d++)
        a += "&args[]=" + encodeURIComponent(arguments[d]);
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
  function i(l, a, d) {
    var f = 3 < arguments.length && arguments[3] !== void 0 ? arguments[3] : null;
    return {
      $$typeof: r,
      key: f == null ? null : "" + f,
      children: l,
      containerInfo: a,
      implementation: d
    };
  }
  var s = e.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE;
  function c(l, a) {
    if (l === "font") return "";
    if (typeof a == "string")
      return a === "use-credentials" ? a : "";
  }
  return Me.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, Me.createPortal = function(l, a) {
    var d = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!a || a.nodeType !== 1 && a.nodeType !== 9 && a.nodeType !== 11)
      throw Error(t(299));
    return i(l, a, null, d);
  }, Me.flushSync = function(l) {
    var a = s.T, d = o.p;
    try {
      if (s.T = null, o.p = 2, l) return l();
    } finally {
      s.T = a, o.p = d, o.d.f();
    }
  }, Me.preconnect = function(l, a) {
    typeof l == "string" && (a ? (a = a.crossOrigin, a = typeof a == "string" ? a === "use-credentials" ? a : "" : void 0) : a = null, o.d.C(l, a));
  }, Me.prefetchDNS = function(l) {
    typeof l == "string" && o.d.D(l);
  }, Me.preinit = function(l, a) {
    if (typeof l == "string" && a && typeof a.as == "string") {
      var d = a.as, f = c(d, a.crossOrigin), h = typeof a.integrity == "string" ? a.integrity : void 0, p = typeof a.fetchPriority == "string" ? a.fetchPriority : void 0;
      d === "style" ? o.d.S(
        l,
        typeof a.precedence == "string" ? a.precedence : void 0,
        {
          crossOrigin: f,
          integrity: h,
          fetchPriority: p
        }
      ) : d === "script" && o.d.X(l, {
        crossOrigin: f,
        integrity: h,
        fetchPriority: p,
        nonce: typeof a.nonce == "string" ? a.nonce : void 0
      });
    }
  }, Me.preinitModule = function(l, a) {
    if (typeof l == "string")
      if (typeof a == "object" && a !== null) {
        if (a.as == null || a.as === "script") {
          var d = c(
            a.as,
            a.crossOrigin
          );
          o.d.M(l, {
            crossOrigin: d,
            integrity: typeof a.integrity == "string" ? a.integrity : void 0,
            nonce: typeof a.nonce == "string" ? a.nonce : void 0
          });
        }
      } else a == null && o.d.M(l);
  }, Me.preload = function(l, a) {
    if (typeof l == "string" && typeof a == "object" && a !== null && typeof a.as == "string") {
      var d = a.as, f = c(d, a.crossOrigin);
      o.d.L(l, d, {
        crossOrigin: f,
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
  }, Me.preloadModule = function(l, a) {
    if (typeof l == "string")
      if (a) {
        var d = c(a.as, a.crossOrigin);
        o.d.m(l, {
          as: typeof a.as == "string" && a.as !== "script" ? a.as : void 0,
          crossOrigin: d,
          integrity: typeof a.integrity == "string" ? a.integrity : void 0
        });
      } else o.d.m(l);
  }, Me.requestFormReset = function(l) {
    o.d.r(l);
  }, Me.unstable_batchedUpdates = function(l, a) {
    return l(a);
  }, Me.useFormState = function(l, a, d) {
    return s.H.useFormState(l, a, d);
  }, Me.useFormStatus = function() {
    return s.H.useHostTransitionStatus();
  }, Me.version = "19.2.7", Me;
}
var hs;
function dp() {
  if (hs) return Dr.exports;
  hs = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), Dr.exports = up(), Dr.exports;
}
var fp = dp();
const Fo = ti(null), hp = Fo.Provider, dc = He.error001("react");
function le(e, t) {
  const n = Mn(Fo);
  if (n === null)
    throw new Error(dc);
  return uc(n, e, t);
}
function me() {
  const e = Mn(Fo);
  if (e === null)
    throw new Error(dc);
  return pe(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const ps = { display: "none" }, pp = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, fc = "react-flow__node-desc", hc = "react-flow__edge-desc", gp = "react-flow__aria-live", mp = (e) => e.ariaLiveMessage, yp = (e) => e.ariaLabelConfig;
function xp({ rfId: e }) {
  const t = le(mp);
  return u.jsx("div", { id: `${gp}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: pp, children: t });
}
function wp({ rfId: e, disableKeyboardA11y: t }) {
  const n = le(yp);
  return u.jsxs(u.Fragment, { children: [u.jsx("div", { id: `${fc}-${e}`, style: ps, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), u.jsx("div", { id: `${hc}-${e}`, style: ps, children: n["edge.a11yDescription.default"] }), !t && u.jsx(xp, { rfId: e })] });
}
const Wo = $o(({ position: e = "top-left", children: t, className: n, style: o, ...r }, i) => {
  const s = `${e}`.split("-");
  return u.jsx("div", { className: Se(["react-flow__panel", n, ...s]), style: o, ref: i, ...r, children: t });
});
Wo.displayName = "Panel";
function vp({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : u.jsx(Wo, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: u.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const bp = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, co = (e) => e.id;
function Sp(e, t) {
  return ge(e.selectedNodes.map(co), t.selectedNodes.map(co)) && ge(e.selectedEdges.map(co), t.selectedEdges.map(co));
}
function Np({ onSelectionChange: e }) {
  const t = me(), { selectedNodes: n, selectedEdges: o } = le(bp, Sp);
  return ae(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((i) => i(r));
  }, [n, o, e]), null;
}
const Ep = (e) => !!e.onSelectionChangeHandlers;
function _p({ onSelectionChange: e }) {
  const t = le(Ep);
  return e || t ? u.jsx(Np, { onSelectionChange: e }) : null;
}
const pc = [0, 0], Cp = { x: 0, y: 0, zoom: 1 }, kp = [
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
], gs = [...kp, "rfId"], Ip = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), ms = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: Sn,
  nodeOrigin: pc,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function Mp(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: i, setNodeExtent: s, reset: c, setDefaultNodesAndEdges: l } = le(Ip, ge), a = me();
  ae(() => (l(e.defaultNodes, e.defaultEdges), () => {
    d.current = ms, c();
  }), []);
  const d = ce(ms);
  return ae(
    () => {
      for (const f of gs) {
        const h = e[f], p = d.current[f];
        h !== p && (typeof e[f] > "u" || (f === "nodes" ? t(h) : f === "edges" ? n(h) : f === "minZoom" ? o(h) : f === "maxZoom" ? r(h) : f === "translateExtent" ? i(h) : f === "nodeExtent" ? s(h) : f === "ariaLabelConfig" ? a.setState({ ariaLabelConfig: uh(h) }) : f === "fitView" ? a.setState({ fitViewQueued: h }) : f === "fitViewOptions" ? a.setState({ fitViewOptions: h }) : a.setState({ [f]: h })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    gs.map((f) => e[f])
  ), null;
}
function ys() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function Ap(e) {
  const [t, n] = G(e === "system" ? null : e);
  return ae(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = ys(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : ys()?.matches ? "dark" : "light";
}
const xs = typeof document < "u" ? document : null;
function Cn(e = null, t = { target: xs, actInsideInputWithModifier: !0 }) {
  const [n, o] = G(!1), r = ce(!1), i = ce(/* @__PURE__ */ new Set([])), [s, c] = pe(() => {
    if (e !== null) {
      const a = (Array.isArray(e) ? e : [e]).filter((f) => typeof f == "string").map((f) => f.replace("+", `
`).replace(`

`, `
+`).split(`
`)), d = a.reduce((f, h) => f.concat(...h), []);
      return [a, d];
    }
    return [[], []];
  }, [e]);
  return ae(() => {
    const l = t?.target ?? xs, a = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (p) => {
        if (r.current = p.ctrlKey || p.metaKey || p.shiftKey || p.altKey, (!r.current || r.current && !a) && Ya(p))
          return !1;
        const x = vs(p.code, c);
        if (i.current.add(p[x]), ws(s, i.current, !1)) {
          const v = p.composedPath?.()?.[0] || p.target, y = v?.nodeName === "BUTTON" || v?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !y) && p.preventDefault(), o(!0);
        }
      }, f = (p) => {
        const m = vs(p.code, c);
        ws(s, i.current, !0) ? (o(!1), i.current.clear()) : i.current.delete(p[m]), p.key === "Meta" && i.current.clear(), r.current = !1;
      }, h = () => {
        i.current.clear(), o(!1);
      };
      return l?.addEventListener("keydown", d), l?.addEventListener("keyup", f), window.addEventListener("blur", h), window.addEventListener("contextmenu", h), () => {
        l?.removeEventListener("keydown", d), l?.removeEventListener("keyup", f), window.removeEventListener("blur", h), window.removeEventListener("contextmenu", h);
      };
    }
  }, [e, o]), n;
}
function ws(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function vs(e, t) {
  return t.includes(e) ? "code" : "key";
}
const jp = () => {
  const e = me();
  return pe(() => ({
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
      const { width: o, height: r, minZoom: i, maxZoom: s, panZoom: c } = e.getState(), l = fi(t, o, r, i, s, n?.padding ?? 0.1);
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
      }, d = n.snapGrid ?? r, f = n.snapToGrid ?? i;
      return Wt(a, o, f, d);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: r, y: i } = o.getBoundingClientRect(), s = Lt(t, n);
      return {
        x: s.x + r,
        y: s.y + i
      };
    }
  }), []);
};
function gc(e, t) {
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
      Dp(l, c);
    n.push(c);
  }
  return r.length && r.forEach((i) => {
    i.index !== void 0 ? n.splice(i.index, 0, { ...i.item }) : n.push({ ...i.item });
  }), n;
}
function Dp(e, t) {
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
function mc(e, t) {
  return gc(e, t);
}
function yc(e, t) {
  return gc(e, t);
}
function ht(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function At(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [r, i] of e) {
    const s = t.has(r);
    !(i.selected === void 0 && !s) && i.selected !== s && (n && (i.selected = s), o.push(ht(i.id, s)));
  }
  return o;
}
function bs({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((r) => [r.id, r]));
  for (const [r, i] of e.entries()) {
    const s = t.get(i.id), c = s?.internals?.userNode ?? s;
    c !== void 0 && c !== i && n.push({ id: i.id, item: i, type: "replace" }), c === void 0 && n.push({ item: i, type: "add", index: r });
  }
  for (const [r] of t)
    o.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function Ss(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const xc = Ba();
function wc(e, t, n = {}) {
  return gh(e, t, {
    ...n,
    onError: n.onError ?? xc
  });
}
function Pp(e, t, n, o = { shouldReplaceId: !0 }) {
  return mh(e, t, n, {
    ...o,
    onError: o.onError ?? xc
  });
}
const Ns = (e) => th(e), $p = (e) => La(e);
function vc(e) {
  return $o(e);
}
const Tp = typeof window < "u" ? ml : ae;
function Es(e) {
  const [t, n] = G(BigInt(0)), [o] = G(() => zp(() => n((r) => r + BigInt(1))));
  return Tp(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function zp(e) {
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
const bc = ti(null);
function Rp({ children: e }) {
  const t = me(), n = he((c) => {
    const { nodes: l = [], setNodes: a, hasDefaultNodes: d, onNodesChange: f, nodeLookup: h, fitViewQueued: p, onNodesChangeMiddlewareMap: m } = t.getState();
    let x = l;
    for (const y of c)
      x = typeof y == "function" ? y(x) : y;
    let v = bs({
      items: x,
      lookup: h
    });
    for (const y of m.values())
      v = y(v);
    d && a(x), v.length > 0 ? f?.(v) : p && window.requestAnimationFrame(() => {
      const { fitViewQueued: y, nodes: N, setNodes: g } = t.getState();
      y && g(N);
    });
  }, []), o = Es(n), r = he((c) => {
    const { edges: l = [], setEdges: a, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: h } = t.getState();
    let p = l;
    for (const m of c)
      p = typeof m == "function" ? m(p) : m;
    d ? a(p) : f && f(bs({
      items: p,
      lookup: h
    }));
  }, []), i = Es(r), s = pe(() => ({ nodeQueue: o, edgeQueue: i }), []);
  return u.jsx(bc.Provider, { value: s, children: e });
}
function Lp() {
  const e = Mn(bc);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Hp = (e) => !!e.panZoom;
function wi() {
  const e = jp(), t = me(), n = Lp(), o = le(Hp), r = pe(() => {
    const i = (f) => t.getState().nodeLookup.get(f), s = (f) => {
      n.nodeQueue.push(f);
    }, c = (f) => {
      n.edgeQueue.push(f);
    }, l = (f) => {
      const { nodeLookup: h, nodeOrigin: p } = t.getState(), m = Ns(f) ? f : h.get(f.id), x = m.parentId ? Wa(m.position, m.measured, m.parentId, h, p) : m.position, v = {
        ...m,
        position: x,
        width: m.measured?.width ?? m.width,
        height: m.measured?.height ?? m.height
      };
      return Rt(v);
    }, a = (f, h, p = { replace: !1 }) => {
      s((m) => m.map((x) => {
        if (x.id === f) {
          const v = typeof h == "function" ? h(x) : h;
          return p.replace && Ns(v) ? v : { ...x, ...v };
        }
        return x;
      }));
    }, d = (f, h, p = { replace: !1 }) => {
      c((m) => m.map((x) => {
        if (x.id === f) {
          const v = typeof h == "function" ? h(x) : h;
          return p.replace && $p(v) ? v : { ...x, ...v };
        }
        return x;
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
        const { nodes: f = [], edges: h = [], transform: p } = t.getState(), [m, x, v] = p;
        return {
          nodes: f.map((y) => ({ ...y })),
          edges: h.map((y) => ({ ...y })),
          viewport: {
            x: m,
            y: x,
            zoom: v
          }
        };
      },
      deleteElements: async ({ nodes: f = [], edges: h = [] }) => {
        const { nodes: p, edges: m, onNodesDelete: x, onEdgesDelete: v, triggerNodeChanges: y, triggerEdgeChanges: N, onDelete: g, onBeforeDelete: w } = t.getState(), { nodes: _, edges: E } = await sh({
          nodesToRemove: f,
          edgesToRemove: h,
          nodes: p,
          edges: m,
          onBeforeDelete: w
        }), k = E.length > 0, D = _.length > 0;
        if (k) {
          const $ = E.map(Ss);
          v?.(E), N($);
        }
        if (D) {
          const $ = _.map(Ss);
          x?.(_), y($);
        }
        return (D || k) && g?.({ nodes: _, edges: E }), { deletedNodes: _, deletedEdges: E };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, h = !0, p) => {
        const m = qi(f), x = m ? f : l(f), v = p !== void 0;
        return x ? (p || t.getState().nodes).filter((y) => {
          const N = t.getState().nodeLookup.get(y.id);
          if (N && !m && (y.id === f.id || !N.internals.positionAbsolute))
            return !1;
          const g = Rt(v ? y : N), w = En(g, x);
          return h && w > 0 || w >= g.width * g.height || w >= x.width * x.height;
        }) : [];
      },
      isNodeIntersecting: (f, h, p = !0) => {
        const x = qi(f) ? f : l(f);
        if (!x)
          return !1;
        const v = En(x, h);
        return p && v > 0 || v >= h.width * h.height || v >= x.width * x.height;
      },
      updateNode: a,
      updateNodeData: (f, h, p = { replace: !1 }) => {
        a(f, (m) => {
          const x = typeof h == "function" ? h(m) : h;
          return p.replace ? { ...m, data: x } : { ...m, data: { ...m.data, ...x } };
        }, p);
      },
      updateEdge: d,
      updateEdgeData: (f, h, p = { replace: !1 }) => {
        d(f, (m) => {
          const x = typeof h == "function" ? h(m) : h;
          return p.replace ? { ...m, data: x } : { ...m, data: { ...m.data, ...x } };
        }, p);
      },
      getNodesBounds: (f) => {
        const { nodeLookup: h, nodeOrigin: p } = t.getState();
        return nh(f, { nodeLookup: h, nodeOrigin: p });
      },
      getHandleConnections: ({ type: f, id: h, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}-${f}${h ? `-${h}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: h, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}${f ? h ? `-${f}-${h}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const h = t.getState().fitViewResolver ?? lh();
        return t.setState({ fitViewQueued: !0, fitViewOptions: f, fitViewResolver: h }), n.nodeQueue.push((p) => [...p]), h.promise;
      }
    };
  }, []);
  return pe(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const _s = (e) => e.selected, Vp = typeof window < "u" ? window : void 0;
function Op({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = me(), { deleteElements: o } = wi(), r = Cn(e, { actInsideInputWithModifier: !1 }), i = Cn(t, { target: Vp });
  ae(() => {
    if (r) {
      const { edges: s, nodes: c } = n.getState();
      o({ nodes: c.filter(_s), edges: s.filter(_s) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), ae(() => {
    n.setState({ multiSelectionActive: i });
  }, [i]);
}
function Bp(e) {
  const t = me();
  ae(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = hi(e.current);
      (o.height === 0 || o.width === 0) && t.getState().onError?.("004", He.error004()), t.setState({ width: o.width || 500, height: o.height || 500 });
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
const Xo = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, Fp = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function Wp({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: i = mt.Free, zoomOnDoubleClick: s = !0, panOnDrag: c = !0, defaultViewport: l, translateExtent: a, minZoom: d, maxZoom: f, zoomActivationKeyCode: h, preventScrolling: p = !0, children: m, noWheelClassName: x, noPanClassName: v, onViewportChange: y, isControlledViewport: N, paneClickDistance: g, selectionOnDrag: w }) {
  const _ = me(), E = ce(null), { userSelectionActive: k, lib: D, connectionInProgress: $ } = le(Fp, ge), W = Cn(h), M = ce();
  Bp(E);
  const z = he((H) => {
    y?.({ x: H[0], y: H[1], zoom: H[2] }), N || _.setState({ transform: H });
  }, [y, N]);
  return ae(() => {
    if (E.current) {
      M.current = Yh({
        domNode: E.current,
        minZoom: d,
        maxZoom: f,
        translateExtent: a,
        viewport: l,
        onDraggingChange: (C) => _.setState((A) => A.paneDragging === C ? A : { paneDragging: C }),
        onPanZoomStart: (C, A) => {
          const { onViewportChangeStart: T, onMoveStart: P } = _.getState();
          P?.(C, A), T?.(A);
        },
        onPanZoom: (C, A) => {
          const { onViewportChange: T, onMove: P } = _.getState();
          P?.(C, A), T?.(A);
        },
        onPanZoomEnd: (C, A) => {
          const { onViewportChangeEnd: T, onMoveEnd: P } = _.getState();
          P?.(C, A), T?.(A);
        }
      });
      const { x: H, y: b, zoom: I } = M.current.getViewport();
      return _.setState({
        panZoom: M.current,
        transform: [H, b, I],
        domNode: E.current.closest(".react-flow")
      }), () => {
        M.current?.destroy();
      };
    }
  }, []), ae(() => {
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
      preventScrolling: p,
      noPanClassName: v,
      userSelectionActive: k,
      noWheelClassName: x,
      lib: D,
      onTransformChange: z,
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
    W,
    p,
    v,
    k,
    x,
    D,
    z,
    $,
    w,
    g
  ]), u.jsx("div", { className: "react-flow__renderer", ref: E, style: Xo, children: m });
}
const Xp = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function Yp() {
  const { userSelectionActive: e, userSelectionRect: t } = le(Xp, ge);
  return e && t ? u.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const Pr = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, qp = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function Zp({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Nn.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: i, selectionOnDrag: s, onSelectionStart: c, onSelectionEnd: l, onPaneClick: a, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: h, onPaneMouseMove: p, onPaneMouseLeave: m, children: x }) {
  const v = ce(0), y = me(), { userSelectionActive: N, elementsSelectable: g, dragging: w, connectionInProgress: _, panBy: E, autoPanSpeed: k } = le(qp, ge), D = g && (e || N), $ = ce(null), W = ce(), M = ce(/* @__PURE__ */ new Set()), z = ce(/* @__PURE__ */ new Set()), H = ce(!1), b = ce({ x: 0, y: 0 }), I = ce(!1), C = (R) => {
    if (H.current || _) {
      H.current = !1;
      return;
    }
    a?.(R), y.getState().resetSelectedElements(), y.setState({ nodesSelectionActive: !1 });
  }, A = (R) => {
    if (Array.isArray(o) && o?.includes(2)) {
      R.preventDefault();
      return;
    }
    d?.(R);
  }, T = f ? (R) => f(R) : void 0, P = (R) => {
    H.current && (R.stopPropagation(), H.current = !1);
  }, V = (R) => {
    const { domNode: Y, transform: re } = y.getState();
    if (W.current = Y?.getBoundingClientRect(), !W.current)
      return;
    const ie = R.target === $.current;
    if (!ie && !!R.target.closest(".nokey") || !e || !(s && ie || t) || R.button !== 0 || !R.isPrimary)
      return;
    R.target?.setPointerCapture?.(R.pointerId), H.current = !1;
    const { x: oe, y: L } = Ze(R.nativeEvent, W.current), J = Wt({ x: oe, y: L }, re);
    y.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: J.x,
        startY: J.y,
        x: oe,
        y: L
      }
    }), ie || (R.stopPropagation(), R.preventDefault());
  };
  function B(R, Y) {
    const { userSelectionRect: re } = y.getState();
    if (!re)
      return;
    const { transform: ie, nodeLookup: q, edgeLookup: ee, connectionLookup: oe, triggerNodeChanges: L, triggerEdgeChanges: J, defaultEdgeOptions: fe } = y.getState(), xe = { x: re.startX, y: re.startY }, { x: Oe, y: we } = Lt(xe, ie), je = {
      startX: xe.x,
      startY: xe.y,
      x: R < Oe ? R : Oe,
      y: Y < we ? Y : we,
      width: Math.abs(R - Oe),
      height: Math.abs(Y - we)
    }, tt = M.current, Fe = z.current;
    M.current = new Set(ui(q, je, ie, n === Nn.Partial, !0).map((Ce) => Ce.id)), z.current = /* @__PURE__ */ new Set();
    const Be = fe?.selectable ?? !0;
    for (const Ce of M.current) {
      const Ae = oe.get(Ce);
      if (Ae)
        for (const { edgeId: De } of Ae.values()) {
          const Ue = ee.get(De);
          Ue && (Ue.selectable ?? Be) && z.current.add(De);
        }
    }
    if (!Zi(tt, M.current)) {
      const Ce = At(q, M.current, !0);
      L(Ce);
    }
    if (!Zi(Fe, z.current)) {
      const Ce = At(ee, z.current);
      J(Ce);
    }
    y.setState({
      userSelectionRect: je,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function O() {
    if (!r || !W.current)
      return;
    const [R, Y] = di(b.current, W.current, k);
    E({ x: R, y: Y }).then((re) => {
      if (!H.current || !re) {
        v.current = requestAnimationFrame(O);
        return;
      }
      const { x: ie, y: q } = b.current;
      B(ie, q), v.current = requestAnimationFrame(O);
    });
  }
  const U = () => {
    cancelAnimationFrame(v.current), v.current = 0, I.current = !1;
  };
  ae(() => () => U(), []);
  const Z = (R) => {
    const { userSelectionRect: Y, transform: re, resetSelectedElements: ie } = y.getState();
    if (!W.current || !Y)
      return;
    const { x: q, y: ee } = Ze(R.nativeEvent, W.current);
    b.current = { x: q, y: ee };
    const oe = Lt({ x: Y.startX, y: Y.startY }, re);
    if (!H.current) {
      const L = t ? 0 : i;
      if (Math.hypot(q - oe.x, ee - oe.y) <= L)
        return;
      ie(), c?.(R);
    }
    H.current = !0, I.current || (O(), I.current = !0), B(q, ee);
  }, ne = (R) => {
    R.button === 0 && (R.target?.releasePointerCapture?.(R.pointerId), !N && R.target === $.current && y.getState().userSelectionRect && C?.(R), y.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), H.current && (l?.(R), y.setState({
      nodesSelectionActive: M.current.size > 0
    })), U());
  }, se = (R) => {
    R.target?.releasePointerCapture?.(R.pointerId), U();
  }, K = o === !0 || Array.isArray(o) && o.includes(0);
  return u.jsxs("div", { className: Se(["react-flow__pane", { draggable: K, dragging: w, selection: e }]), onClick: D ? void 0 : Pr(C, $), onContextMenu: Pr(A, $), onWheel: Pr(T, $), onPointerEnter: D ? void 0 : h, onPointerMove: D ? Z : p, onPointerUp: D ? ne : void 0, onPointerCancel: D ? se : void 0, onPointerDownCapture: D ? V : void 0, onClickCapture: D ? P : void 0, onPointerLeave: m, ref: $, style: Xo, children: [x, u.jsx(Yp, {})] });
}
function Ur({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: i, multiSelectionActive: s, nodeLookup: c, onError: l } = t.getState(), a = c.get(e);
  if (!a) {
    l?.("012", He.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), a.selected ? (n || a.selected && s) && (i({ nodes: [a], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function Sc({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: i, nodeClickDistance: s }) {
  const c = me(), [l, a] = G(!1), d = ce();
  return ae(() => {
    d.current = Dh({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (f) => {
        Ur({
          id: f,
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
  }, []), ae(() => {
    if (!(t || !e.current || !d.current))
      return d.current.update({
        noDragClassName: n,
        handleSelector: o,
        domNode: e.current,
        isSelectable: i,
        nodeId: r,
        nodeClickDistance: s
      }), () => {
        d.current?.destroy();
      };
  }, [n, o, t, i, e, r, s]), l;
}
const Kp = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function Nc() {
  const e = me();
  return he((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: i, nodesDraggable: s, onError: c, updateNodePositions: l, nodeLookup: a, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), h = Kp(s), p = r ? i[0] : 5, m = r ? i[1] : 5, x = n.direction.x * p * n.factor, v = n.direction.y * m * n.factor;
    for (const [, y] of a) {
      if (!h(y))
        continue;
      let N = {
        x: y.internals.positionAbsolute.x + x,
        y: y.internals.positionAbsolute.y + v
      };
      r && (N = $n(N, i));
      const { position: g, positionAbsolute: w } = Ha({
        nodeId: y.id,
        nextPosition: N,
        nodeLookup: a,
        nodeExtent: o,
        nodeOrigin: d,
        onError: c
      });
      y.position = g, y.internals.positionAbsolute = w, f.set(y.id, y);
    }
    l(f);
  }, []);
}
const vi = ti(null), Up = vi.Provider;
vi.Consumer;
const Ec = () => Mn(vi), Gp = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), Qp = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: i, connection: s } = o, { fromHandle: c, toHandle: l, isValid: a } = s, d = l?.nodeId === e && l?.id === t && l?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: d,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: i === Tt.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!r,
    valid: d && a
  };
};
function Jp({ type: e = "source", position: t = te.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: i = !0, id: s, onConnect: c, children: l, className: a, onMouseDown: d, onTouchStart: f, ...h }, p) {
  const m = s || null, x = e === "target", v = me(), y = Ec(), { connectOnClick: N, noPanClassName: g, rfId: w } = le(Gp, ge), { connectingFrom: _, connectingTo: E, clickConnecting: k, isPossibleEndHandle: D, connectionInProcess: $, clickConnectionInProcess: W, valid: M } = le(Qp(y, m, e), ge);
  y || v.getState().onError?.("010", He.error010());
  const z = (I) => {
    const { defaultEdgeOptions: C, onConnect: A, hasDefaultEdges: T } = v.getState(), P = {
      ...C,
      ...I
    };
    if (T) {
      const { edges: V, setEdges: B, onError: O } = v.getState();
      B(wc(P, V, { onError: O }));
    }
    A?.(P), c?.(P);
  }, H = (I) => {
    if (!y)
      return;
    const C = qa(I.nativeEvent);
    if (r && (C && I.button === 0 || !C)) {
      const A = v.getState();
      Kr.onPointerDown(I.nativeEvent, {
        handleDomNode: I.currentTarget,
        autoPanOnConnect: A.autoPanOnConnect,
        connectionMode: A.connectionMode,
        connectionRadius: A.connectionRadius,
        domNode: A.domNode,
        nodeLookup: A.nodeLookup,
        lib: A.lib,
        isTarget: x,
        handleId: m,
        nodeId: y,
        flowId: A.rfId,
        panBy: A.panBy,
        cancelConnection: A.cancelConnection,
        onConnectStart: A.onConnectStart,
        onConnectEnd: (...T) => v.getState().onConnectEnd?.(...T),
        updateConnection: A.updateConnection,
        onConnect: z,
        isValidConnection: n || ((...T) => v.getState().isValidConnection?.(...T) ?? !0),
        getTransform: () => v.getState().transform,
        getFromHandle: () => v.getState().connection.fromHandle,
        autoPanSpeed: A.autoPanSpeed,
        dragThreshold: A.connectionDragThreshold
      });
    }
    C ? d?.(I) : f?.(I);
  }, b = (I) => {
    const { onClickConnectStart: C, onClickConnectEnd: A, connectionClickStartHandle: T, connectionMode: P, isValidConnection: V, lib: B, rfId: O, nodeLookup: U, connection: Z } = v.getState();
    if (!y || !T && !r)
      return;
    if (!T) {
      C?.(I.nativeEvent, { nodeId: y, handleId: m, handleType: e }), v.setState({ connectionClickStartHandle: { nodeId: y, type: e, id: m } });
      return;
    }
    const ne = Xa(I.target), se = n || V, { connection: K, isValid: R } = Kr.isValid(I.nativeEvent, {
      handle: {
        nodeId: y,
        id: m,
        type: e
      },
      connectionMode: P,
      fromNodeId: T.nodeId,
      fromHandleId: T.id || null,
      fromType: T.type,
      isValidConnection: se,
      flowId: O,
      doc: ne,
      lib: B,
      nodeLookup: U
    });
    R && K && z(K);
    const Y = structuredClone(Z);
    delete Y.inProgress, Y.toPosition = Y.toHandle ? Y.toHandle.position : null, A?.(I, Y), v.setState({ connectionClickStartHandle: null });
  };
  return u.jsx("div", { "data-handleid": m, "data-nodeid": y, "data-handlepos": t, "data-id": `${w}-${y}-${m}-${e}`, className: Se([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    g,
    a,
    {
      source: !x,
      target: x,
      connectable: o,
      connectablestart: r,
      connectableend: i,
      clickconnecting: k,
      connectingfrom: _,
      connectingto: E,
      valid: M,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!$ || D) && ($ || W ? i : r)
    }
  ]), onMouseDown: H, onTouchStart: H, onClick: N ? b : void 0, ref: p, ...h, children: l });
}
const Vt = ve(vc(Jp));
function eg({ data: e, isConnectable: t, sourcePosition: n = te.Bottom }) {
  return u.jsxs(u.Fragment, { children: [e?.label, u.jsx(Vt, { type: "source", position: n, isConnectable: t })] });
}
function tg({ data: e, isConnectable: t, targetPosition: n = te.Top, sourcePosition: o = te.Bottom }) {
  return u.jsxs(u.Fragment, { children: [u.jsx(Vt, { type: "target", position: n, isConnectable: t }), e?.label, u.jsx(Vt, { type: "source", position: o, isConnectable: t })] });
}
function ng() {
  return null;
}
function og({ data: e, isConnectable: t, targetPosition: n = te.Top }) {
  return u.jsxs(u.Fragment, { children: [u.jsx(Vt, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const jo = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, Cs = {
  input: eg,
  default: tg,
  output: og,
  group: ng
};
function rg(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const ig = (e) => {
  const { width: t, height: n, x: o, y: r } = Pn(e.nodeLookup, {
    filter: (i) => !!i.selected
  });
  return {
    width: qe(t) ? t : null,
    height: qe(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function sg({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = me(), { width: r, height: i, transformString: s, userSelectionActive: c } = le(ig, ge), l = Nc(), a = ce(null);
  ae(() => {
    n || a.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !c && r !== null && i !== null;
  if (Sc({
    nodeRef: a,
    disabled: !d
  }), !d)
    return null;
  const f = e ? (p) => {
    const m = o.getState().nodes.filter((x) => x.selected);
    e(p, m);
  } : void 0, h = (p) => {
    Object.prototype.hasOwnProperty.call(jo, p.key) && (p.preventDefault(), l({
      direction: jo[p.key],
      factor: p.shiftKey ? 4 : 1
    }));
  };
  return u.jsx("div", { className: Se(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: s
  }, children: u.jsx("div", { ref: a, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : h, style: {
    width: r,
    height: i
  } }) });
}
const ks = typeof window < "u" ? window : void 0, ag = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function _c({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, paneClickDistance: c, deleteKeyCode: l, selectionKeyCode: a, selectionOnDrag: d, selectionMode: f, onSelectionStart: h, onSelectionEnd: p, multiSelectionKeyCode: m, panActivationKeyCode: x, zoomActivationKeyCode: v, elementsSelectable: y, zoomOnScroll: N, zoomOnPinch: g, panOnScroll: w, panOnScrollSpeed: _, panOnScrollMode: E, zoomOnDoubleClick: k, panOnDrag: D, autoPanOnSelection: $, defaultViewport: W, translateExtent: M, minZoom: z, maxZoom: H, preventScrolling: b, onSelectionContextMenu: I, noWheelClassName: C, noPanClassName: A, disableKeyboardA11y: T, onViewportChange: P, isControlledViewport: V }) {
  const { nodesSelectionActive: B, userSelectionActive: O } = le(ag, ge), U = Cn(a, { target: ks }), Z = Cn(x, { target: ks }), ne = Z || D, se = Z || w, K = d && ne !== !0, R = U || O || K;
  return Op({ deleteKeyCode: l, multiSelectionKeyCode: m }), u.jsx(Wp, { onPaneContextMenu: i, elementsSelectable: y, zoomOnScroll: N, zoomOnPinch: g, panOnScroll: se, panOnScrollSpeed: _, panOnScrollMode: E, zoomOnDoubleClick: k, panOnDrag: !U && ne, defaultViewport: W, translateExtent: M, minZoom: z, maxZoom: H, zoomActivationKeyCode: v, preventScrolling: b, noWheelClassName: C, noPanClassName: A, onViewportChange: P, isControlledViewport: V, paneClickDistance: c, selectionOnDrag: K, children: u.jsxs(Zp, { onSelectionStart: h, onSelectionEnd: p, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, panOnDrag: ne, autoPanOnSelection: $, isSelecting: !!R, selectionMode: f, selectionKeyPressed: U, paneClickDistance: c, selectionOnDrag: K, children: [e, B && u.jsx(sg, { onSelectionContextMenu: I, noPanClassName: A, disableKeyboardA11y: T })] }) });
}
_c.displayName = "FlowRenderer";
const cg = ve(_c), lg = (e) => (t) => e ? ui(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function ug(e) {
  return le(he(lg(e), [e]), ge);
}
const dg = (e) => e.updateNodeInternals;
function fg() {
  const e = le(dg), [t] = G(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
function hg({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = me(), i = ce(null), s = ce(null), c = ce(e.sourcePosition), l = ce(e.targetPosition), a = ce(t), d = n && !!e.internals.handleBounds;
  return ae(() => {
    i.current && !e.hidden && (!d || s.current !== i.current) && (s.current && o?.unobserve(s.current), o?.observe(i.current), s.current = i.current);
  }, [d, e.hidden]), ae(() => () => {
    s.current && (o?.unobserve(s.current), s.current = null);
  }, []), ae(() => {
    if (i.current) {
      const f = a.current !== t, h = c.current !== e.sourcePosition, p = l.current !== e.targetPosition;
      (f || h || p) && (a.current = t, c.current = e.sourcePosition, l.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: i.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), i;
}
function pg({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: i, onDoubleClick: s, nodesDraggable: c, elementsSelectable: l, nodesConnectable: a, nodesFocusable: d, resizeObserver: f, noDragClassName: h, noPanClassName: p, disableKeyboardA11y: m, rfId: x, nodeTypes: v, nodeClickDistance: y, onError: N }) {
  const { node: g, internals: w, isParent: _ } = le((R) => {
    const Y = R.nodeLookup.get(e), re = R.parentLookup.has(e);
    return {
      node: Y,
      internals: Y.internals,
      isParent: re
    };
  }, ge);
  let E = g.type || "default", k = v?.[E] || Cs[E];
  k === void 0 && (N?.("003", He.error003(E)), E = "default", k = v?.default || Cs.default);
  const D = !!(g.draggable || c && typeof g.draggable > "u"), $ = !!(g.selectable || l && typeof g.selectable > "u"), W = !!(g.connectable || a && typeof g.connectable > "u"), M = !!(g.focusable || d && typeof g.focusable > "u"), z = me(), H = Fa(g), b = hg({ node: g, nodeType: E, hasDimensions: H, resizeObserver: f }), I = Sc({
    nodeRef: b,
    disabled: g.hidden || !D,
    noDragClassName: h,
    handleSelector: g.dragHandle,
    nodeId: e,
    isSelectable: $,
    nodeClickDistance: y
  }), C = Nc();
  if (g.hidden)
    return null;
  const A = it(g), T = rg(g), P = $ || D || t || n || o || r, V = n ? (R) => n(R, { ...w.userNode }) : void 0, B = o ? (R) => o(R, { ...w.userNode }) : void 0, O = r ? (R) => r(R, { ...w.userNode }) : void 0, U = i ? (R) => i(R, { ...w.userNode }) : void 0, Z = s ? (R) => s(R, { ...w.userNode }) : void 0, ne = (R) => {
    const { selectNodesOnDrag: Y, nodeDragThreshold: re } = z.getState();
    $ && (!Y || !D || re > 0) && Ur({
      id: e,
      store: z,
      nodeRef: b
    }), t && t(R, { ...w.userNode });
  }, se = (R) => {
    if (!(Ya(R.nativeEvent) || m)) {
      if ($a.includes(R.key) && $) {
        const Y = R.key === "Escape";
        Ur({
          id: e,
          store: z,
          unselect: Y,
          nodeRef: b
        });
      } else if (D && g.selected && Object.prototype.hasOwnProperty.call(jo, R.key)) {
        R.preventDefault();
        const { ariaLabelConfig: Y } = z.getState();
        z.setState({
          ariaLiveMessage: Y["node.a11yDescription.ariaLiveMessage"]({
            direction: R.key.replace("Arrow", "").toLowerCase(),
            x: ~~w.positionAbsolute.x,
            y: ~~w.positionAbsolute.y
          })
        }), C({
          direction: jo[R.key],
          factor: R.shiftKey ? 4 : 1
        });
      }
    }
  }, K = () => {
    if (m || !b.current?.matches(":focus-visible"))
      return;
    const { transform: R, width: Y, height: re, autoPanOnNodeFocus: ie, setCenter: q } = z.getState();
    if (!ie)
      return;
    ui(/* @__PURE__ */ new Map([[e, g]]), { x: 0, y: 0, width: Y, height: re }, R, !0).length > 0 || q(g.position.x + A.width / 2, g.position.y + A.height / 2, {
      zoom: R[2]
    });
  };
  return u.jsx("div", { className: Se([
    "react-flow__node",
    `react-flow__node-${E}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [p]: D
    },
    g.className,
    {
      selected: g.selected,
      selectable: $,
      parent: _,
      draggable: D,
      dragging: I
    }
  ]), ref: b, style: {
    zIndex: w.z,
    transform: `translate(${w.positionAbsolute.x}px,${w.positionAbsolute.y}px)`,
    pointerEvents: P ? "all" : "none",
    visibility: H ? "visible" : "hidden",
    ...g.style,
    ...T
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: V, onMouseMove: B, onMouseLeave: O, onContextMenu: U, onClick: ne, onDoubleClick: Z, onKeyDown: M ? se : void 0, tabIndex: M ? 0 : void 0, onFocus: M ? K : void 0, role: g.ariaRole ?? (M ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": m ? void 0 : `${fc}-${x}`, "aria-label": g.ariaLabel, ...g.domAttributes, children: u.jsx(Up, { value: e, children: u.jsx(k, { id: e, data: g.data, type: E, positionAbsoluteX: w.positionAbsolute.x, positionAbsoluteY: w.positionAbsolute.y, selected: g.selected ?? !1, selectable: $, draggable: D, deletable: g.deletable ?? !0, isConnectable: W, sourcePosition: g.sourcePosition, targetPosition: g.targetPosition, dragging: I, dragHandle: g.dragHandle, zIndex: w.z, parentId: g.parentId, ...A }) }) });
}
var gg = ve(pg);
const mg = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function Cc(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: i } = le(mg, ge), s = ug(e.onlyRenderVisibleElements), c = fg();
  return u.jsx("div", { className: "react-flow__nodes", style: Xo, children: s.map((l) => (
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
    u.jsx(gg, { id: l, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: i }, l)
  )) });
}
Cc.displayName = "NodeRenderer";
const yg = ve(Cc);
function xg(e) {
  return le(he((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const i = n.nodeLookup.get(r.source), s = n.nodeLookup.get(r.target);
        i && s && hh({
          sourceNode: i,
          targetNode: s,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && o.push(r.id);
      }
    return o;
  }, [e]), ge);
}
const wg = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return u.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, vg = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return u.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Is = {
  [Io.Arrow]: wg,
  [Io.ArrowClosed]: vg
};
function bg(e) {
  const t = me();
  return pe(() => Object.prototype.hasOwnProperty.call(Is, e) ? Is[e] : (t.getState().onError?.("009", He.error009(e)), null), [e]);
}
const Sg = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: i = "strokeWidth", strokeWidth: s, orient: c = "auto-start-reverse" }) => {
  const l = bg(t);
  return l ? u.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: i, orient: c, refX: "0", refY: "0", children: u.jsx(l, { color: n, strokeWidth: s }) }) : null;
}, kc = ({ defaultColor: e, rfId: t }) => {
  const n = le((i) => i.edges), o = le((i) => i.defaultEdgeOptions), r = pe(() => bh(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? u.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: u.jsx("defs", { children: r.map((i) => u.jsx(Sg, { id: i.id, type: i.type, color: i.color, width: i.width, height: i.height, markerUnits: i.markerUnits, strokeWidth: i.strokeWidth, orient: i.orient }, i.id)) }) }) : null;
};
kc.displayName = "MarkerDefinitions";
var Ng = ve(kc);
function Ic({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: i, labelBgPadding: s = [2, 4], labelBgBorderRadius: c = 2, children: l, className: a, ...d }) {
  const [f, h] = G({ x: 1, y: 0, width: 0, height: 0 }), p = Se(["react-flow__edge-textwrapper", a]), m = ce(null);
  return ae(() => {
    if (m.current) {
      const x = m.current.getBBox();
      h({
        x: x.x,
        y: x.y,
        width: x.width,
        height: x.height
      });
    }
  }, [n]), n ? u.jsxs("g", { transform: `translate(${e - f.width / 2} ${t - f.height / 2})`, className: p, visibility: f.width ? "visible" : "hidden", ...d, children: [r && u.jsx("rect", { width: f.width + 2 * s[0], x: -s[0], y: -s[1], height: f.height + 2 * s[1], className: "react-flow__edge-textbg", style: i, rx: c, ry: c }), u.jsx("text", { className: "react-flow__edge-text", y: f.height / 2, dy: "0.3em", ref: m, style: o, children: n }), l] }) : null;
}
Ic.displayName = "EdgeText";
const Eg = ve(Ic);
function Tn({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: c, labelBgBorderRadius: l, interactionWidth: a = 20, ...d }) {
  return u.jsxs(u.Fragment, { children: [u.jsx("path", { ...d, d: e, fill: "none", className: Se(["react-flow__edge-path", d.className]) }), a ? u.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: a, className: "react-flow__edge-interaction" }) : null, o && qe(t) && qe(n) ? u.jsx(Eg, { x: t, y: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: c, labelBgBorderRadius: l }) : null] });
}
function Ms({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === te.Left || e === te.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function Mc({ sourceX: e, sourceY: t, sourcePosition: n = te.Bottom, targetX: o, targetY: r, targetPosition: i = te.Top }) {
  const [s, c] = Ms({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [l, a] = Ms({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [d, f, h, p] = Za({
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
    d,
    f,
    h,
    p
  ];
}
function Ac(e) {
  return ve(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: c, label: l, labelStyle: a, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: m, markerEnd: x, markerStart: v, interactionWidth: y }) => {
    const [N, g, w] = Mc({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: c
    }), _ = e.isInternal ? void 0 : t;
    return u.jsx(Tn, { id: _, path: N, labelX: g, labelY: w, label: l, labelStyle: a, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: m, markerEnd: x, markerStart: v, interactionWidth: y });
  });
}
const _g = Ac({ isInternal: !1 }), jc = Ac({ isInternal: !0 });
_g.displayName = "SimpleBezierEdge";
jc.displayName = "SimpleBezierEdgeInternal";
function Dc(e) {
  return ve(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: c, labelShowBg: l, labelBgStyle: a, labelBgPadding: d, labelBgBorderRadius: f, style: h, sourcePosition: p = te.Bottom, targetPosition: m = te.Top, markerEnd: x, markerStart: v, pathOptions: y, interactionWidth: N }) => {
    const [g, w, _] = Ao({
      sourceX: n,
      sourceY: o,
      sourcePosition: p,
      targetX: r,
      targetY: i,
      targetPosition: m,
      borderRadius: y?.borderRadius,
      offset: y?.offset,
      stepPosition: y?.stepPosition
    }), E = e.isInternal ? void 0 : t;
    return u.jsx(Tn, { id: E, path: g, labelX: w, labelY: _, label: s, labelStyle: c, labelShowBg: l, labelBgStyle: a, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: x, markerStart: v, interactionWidth: N });
  });
}
const Pc = Dc({ isInternal: !1 }), $c = Dc({ isInternal: !0 });
Pc.displayName = "SmoothStepEdge";
$c.displayName = "SmoothStepEdgeInternal";
function Tc(e) {
  return ve(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return u.jsx(Pc, { ...n, id: o, pathOptions: pe(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Cg = Tc({ isInternal: !1 }), zc = Tc({ isInternal: !0 });
Cg.displayName = "StepEdge";
zc.displayName = "StepEdgeInternal";
function Rc(e) {
  return ve(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: c, labelShowBg: l, labelBgStyle: a, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: p, markerStart: m, interactionWidth: x }) => {
    const [v, y, N] = Qa({ sourceX: n, sourceY: o, targetX: r, targetY: i }), g = e.isInternal ? void 0 : t;
    return u.jsx(Tn, { id: g, path: v, labelX: y, labelY: N, label: s, labelStyle: c, labelShowBg: l, labelBgStyle: a, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: p, markerStart: m, interactionWidth: x });
  });
}
const kg = Rc({ isInternal: !1 }), Lc = Rc({ isInternal: !0 });
kg.displayName = "StraightEdge";
Lc.displayName = "StraightEdgeInternal";
function Hc(e) {
  return ve(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s = te.Bottom, targetPosition: c = te.Top, label: l, labelStyle: a, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: m, markerEnd: x, markerStart: v, pathOptions: y, interactionWidth: N }) => {
    const [g, w, _] = Ka({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: c,
      curvature: y?.curvature
    }), E = e.isInternal ? void 0 : t;
    return u.jsx(Tn, { id: E, path: g, labelX: w, labelY: _, label: l, labelStyle: a, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: m, markerEnd: x, markerStart: v, interactionWidth: N });
  });
}
const Ig = Hc({ isInternal: !1 }), Vc = Hc({ isInternal: !0 });
Ig.displayName = "BezierEdge";
Vc.displayName = "BezierEdgeInternal";
const As = {
  default: Vc,
  straight: Lc,
  step: zc,
  smoothstep: $c,
  simplebezier: jc
}, js = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, Mg = (e, t, n) => n === te.Left ? e - t : n === te.Right ? e + t : e, Ag = (e, t, n) => n === te.Top ? e - t : n === te.Bottom ? e + t : e, Ds = "react-flow__edgeupdater";
function Ps({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: i, onMouseOut: s, type: c }) {
  return u.jsx("circle", { onMouseDown: r, onMouseEnter: i, onMouseOut: s, className: Se([Ds, `${Ds}-${c}`]), cx: Mg(t, o, e), cy: Ag(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function jg({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: i, targetY: s, sourcePosition: c, targetPosition: l, onReconnect: a, onReconnectStart: d, onReconnectEnd: f, setReconnecting: h, setUpdateHover: p }) {
  const m = me(), x = (w, _) => {
    if (w.button !== 0)
      return;
    const { autoPanOnConnect: E, domNode: k, connectionMode: D, connectionRadius: $, lib: W, onConnectStart: M, cancelConnection: z, nodeLookup: H, rfId: b, panBy: I, updateConnection: C } = m.getState(), A = _.type === "target", T = (B, O) => {
      h(!1), f?.(B, n, _.type, O);
    }, P = (B) => a?.(n, B), V = (B, O) => {
      h(!0), d?.(w, n, _.type), M?.(B, O);
    };
    Kr.onPointerDown(w.nativeEvent, {
      autoPanOnConnect: E,
      connectionMode: D,
      connectionRadius: $,
      domNode: k,
      handleId: _.id,
      nodeId: _.nodeId,
      nodeLookup: H,
      isTarget: A,
      edgeUpdaterType: _.type,
      lib: W,
      flowId: b,
      cancelConnection: z,
      panBy: I,
      isValidConnection: (...B) => m.getState().isValidConnection?.(...B) ?? !0,
      onConnect: P,
      onConnectStart: V,
      onConnectEnd: (...B) => m.getState().onConnectEnd?.(...B),
      onReconnectEnd: T,
      updateConnection: C,
      getTransform: () => m.getState().transform,
      getFromHandle: () => m.getState().connection.fromHandle,
      dragThreshold: m.getState().connectionDragThreshold,
      handleDomNode: w.currentTarget
    });
  }, v = (w) => x(w, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), y = (w) => x(w, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), N = () => p(!0), g = () => p(!1);
  return u.jsxs(u.Fragment, { children: [(e === !0 || e === "source") && u.jsx(Ps, { position: c, centerX: o, centerY: r, radius: t, onMouseDown: v, onMouseEnter: N, onMouseOut: g, type: "source" }), (e === !0 || e === "target") && u.jsx(Ps, { position: l, centerX: i, centerY: s, radius: t, onMouseDown: y, onMouseEnter: N, onMouseOut: g, type: "target" })] });
}
function Dg({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: i, onContextMenu: s, onMouseEnter: c, onMouseMove: l, onMouseLeave: a, reconnectRadius: d, onReconnect: f, onReconnectStart: h, onReconnectEnd: p, rfId: m, edgeTypes: x, noPanClassName: v, onError: y, disableKeyboardA11y: N }) {
  let g = le((q) => q.edgeLookup.get(e));
  const w = le((q) => q.defaultEdgeOptions);
  g = w ? { ...w, ...g } : g;
  let _ = g.type || "default", E = x?.[_] || As[_];
  E === void 0 && (y?.("011", He.error011(_)), _ = "default", E = x?.default || As.default);
  const k = !!(g.focusable || t && typeof g.focusable > "u"), D = typeof f < "u" && (g.reconnectable || n && typeof g.reconnectable > "u"), $ = !!(g.selectable || o && typeof g.selectable > "u"), W = ce(null), [M, z] = G(!1), [H, b] = G(!1), I = me(), { zIndex: C, sourceX: A, sourceY: T, targetX: P, targetY: V, sourcePosition: B, targetPosition: O } = le(he((q) => {
    const ee = q.nodeLookup.get(g.source), oe = q.nodeLookup.get(g.target);
    if (!ee || !oe)
      return {
        zIndex: g.zIndex,
        ...js
      };
    const L = vh({
      id: e,
      sourceNode: ee,
      targetNode: oe,
      sourceHandle: g.sourceHandle || null,
      targetHandle: g.targetHandle || null,
      connectionMode: q.connectionMode,
      onError: y
    });
    return {
      zIndex: fh({
        selected: g.selected,
        zIndex: g.zIndex,
        sourceNode: ee,
        targetNode: oe,
        elevateOnSelect: q.elevateEdgesOnSelect,
        zIndexMode: q.zIndexMode
      }),
      ...L || js
    };
  }, [g.source, g.target, g.sourceHandle, g.targetHandle, g.selected, g.zIndex]), ge), U = pe(() => g.markerStart ? `url('#${qr(g.markerStart, m)}')` : void 0, [g.markerStart, m]), Z = pe(() => g.markerEnd ? `url('#${qr(g.markerEnd, m)}')` : void 0, [g.markerEnd, m]);
  if (g.hidden || A === null || T === null || P === null || V === null)
    return null;
  const ne = (q) => {
    const { addSelectedEdges: ee, unselectNodesAndEdges: oe, multiSelectionActive: L } = I.getState();
    $ && (I.setState({ nodesSelectionActive: !1 }), g.selected && L ? (oe({ nodes: [], edges: [g] }), W.current?.blur()) : ee([e])), r && r(q, g);
  }, se = i ? (q) => {
    i(q, { ...g });
  } : void 0, K = s ? (q) => {
    s(q, { ...g });
  } : void 0, R = c ? (q) => {
    c(q, { ...g });
  } : void 0, Y = l ? (q) => {
    l(q, { ...g });
  } : void 0, re = a ? (q) => {
    a(q, { ...g });
  } : void 0, ie = (q) => {
    if (!N && $a.includes(q.key) && $) {
      const { unselectNodesAndEdges: ee, addSelectedEdges: oe } = I.getState();
      q.key === "Escape" ? (W.current?.blur(), ee({ edges: [g] })) : oe([e]);
    }
  };
  return u.jsx("svg", { style: { zIndex: C }, children: u.jsxs("g", { className: Se([
    "react-flow__edge",
    `react-flow__edge-${_}`,
    g.className,
    v,
    {
      selected: g.selected,
      animated: g.animated,
      inactive: !$ && !r,
      updating: M,
      selectable: $
    }
  ]), onClick: ne, onDoubleClick: se, onContextMenu: K, onMouseEnter: R, onMouseMove: Y, onMouseLeave: re, onKeyDown: k ? ie : void 0, tabIndex: k ? 0 : void 0, role: g.ariaRole ?? (k ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": g.ariaLabel === null ? void 0 : g.ariaLabel || `Edge from ${g.source} to ${g.target}`, "aria-describedby": k ? `${hc}-${m}` : void 0, ref: W, ...g.domAttributes, children: [!H && u.jsx(E, { id: e, source: g.source, target: g.target, type: g.type, selected: g.selected, animated: g.animated, selectable: $, deletable: g.deletable ?? !0, label: g.label, labelStyle: g.labelStyle, labelShowBg: g.labelShowBg, labelBgStyle: g.labelBgStyle, labelBgPadding: g.labelBgPadding, labelBgBorderRadius: g.labelBgBorderRadius, sourceX: A, sourceY: T, targetX: P, targetY: V, sourcePosition: B, targetPosition: O, data: g.data, style: g.style, sourceHandleId: g.sourceHandle, targetHandleId: g.targetHandle, markerStart: U, markerEnd: Z, pathOptions: "pathOptions" in g ? g.pathOptions : void 0, interactionWidth: g.interactionWidth }), D && u.jsx(jg, { edge: g, isReconnectable: D, reconnectRadius: d, onReconnect: f, onReconnectStart: h, onReconnectEnd: p, sourceX: A, sourceY: T, targetX: P, targetY: V, sourcePosition: B, targetPosition: O, setUpdateHover: z, setReconnecting: b })] }) });
}
var Pg = ve(Dg);
const $g = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function Oc({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: i, onEdgeContextMenu: s, onEdgeMouseEnter: c, onEdgeMouseMove: l, onEdgeMouseLeave: a, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: h, onReconnectStart: p, onReconnectEnd: m, disableKeyboardA11y: x }) {
  const { edgesFocusable: v, edgesReconnectable: y, elementsSelectable: N, onError: g } = le($g, ge), w = xg(t);
  return u.jsxs("div", { className: "react-flow__edges", children: [u.jsx(Ng, { defaultColor: e, rfId: n }), w.map((_) => u.jsx(Pg, { id: _, edgesFocusable: v, edgesReconnectable: y, elementsSelectable: N, noPanClassName: r, onReconnect: i, onContextMenu: s, onMouseEnter: c, onMouseMove: l, onMouseLeave: a, onClick: d, reconnectRadius: f, onDoubleClick: h, onReconnectStart: p, onReconnectEnd: m, rfId: n, onError: g, edgeTypes: o, disableKeyboardA11y: x }, _))] });
}
Oc.displayName = "EdgeRenderer";
const Tg = ve(Oc), zg = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function Rg({ children: e }) {
  const t = le(zg);
  return u.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function Lg(e) {
  const t = wi(), n = ce(!1);
  ae(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Hg = (e) => e.panZoom?.syncViewport;
function Vg(e) {
  const t = le(Hg), n = me();
  return ae(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function Og(e) {
  return e.connection.inProgress ? { ...e.connection, to: Wt(e.connection.to, e.transform) } : { ...e.connection };
}
function Bg(e) {
  return Og;
}
function Fg(e) {
  const t = Bg();
  return le(t, ge);
}
const Wg = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function Xg({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: i, height: s, isValid: c, inProgress: l } = le(Wg, ge);
  return !(i && r && l) ? null : u.jsx("svg", { style: e, width: i, height: s, className: "react-flow__connectionline react-flow__container", children: u.jsx("g", { className: Se(["react-flow__connection", Ra(c)]), children: u.jsx(Bc, { style: t, type: n, CustomComponent: o, isValid: c }) }) });
}
const Bc = ({ style: e, type: t = ct.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: i, fromNode: s, fromHandle: c, fromPosition: l, to: a, toNode: d, toHandle: f, toPosition: h, pointer: p } = Fg();
  if (!r)
    return;
  if (n)
    return u.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: s, fromHandle: c, fromX: i.x, fromY: i.y, toX: a.x, toY: a.y, fromPosition: l, toPosition: h, connectionStatus: Ra(o), toNode: d, toHandle: f, pointer: p });
  let m = "";
  const x = {
    sourceX: i.x,
    sourceY: i.y,
    sourcePosition: l,
    targetX: a.x,
    targetY: a.y,
    targetPosition: h
  };
  switch (t) {
    case ct.Bezier:
      [m] = Ka(x);
      break;
    case ct.SimpleBezier:
      [m] = Mc(x);
      break;
    case ct.Step:
      [m] = Ao({
        ...x,
        borderRadius: 0
      });
      break;
    case ct.SmoothStep:
      [m] = Ao(x);
      break;
    default:
      [m] = Qa(x);
  }
  return u.jsx("path", { d: m, fill: "none", className: "react-flow__connection-path", style: e });
};
Bc.displayName = "ConnectionLine";
const Yg = {};
function $s(e = Yg) {
  ce(e), me(), ae(() => {
  }, [e]);
}
function qg() {
  me(), ce(!1), ae(() => {
  }, []);
}
function Fc({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: i, onEdgeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: l, onNodeMouseLeave: a, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: h, onSelectionEnd: p, connectionLineType: m, connectionLineStyle: x, connectionLineComponent: v, connectionLineContainerStyle: y, selectionKeyCode: N, selectionOnDrag: g, selectionMode: w, multiSelectionKeyCode: _, panActivationKeyCode: E, zoomActivationKeyCode: k, deleteKeyCode: D, onlyRenderVisibleElements: $, elementsSelectable: W, defaultViewport: M, translateExtent: z, minZoom: H, maxZoom: b, preventScrolling: I, defaultMarkerColor: C, zoomOnScroll: A, zoomOnPinch: T, panOnScroll: P, panOnScrollSpeed: V, panOnScrollMode: B, zoomOnDoubleClick: O, panOnDrag: U, autoPanOnSelection: Z, onPaneClick: ne, onPaneMouseEnter: se, onPaneMouseMove: K, onPaneMouseLeave: R, onPaneScroll: Y, onPaneContextMenu: re, paneClickDistance: ie, nodeClickDistance: q, onEdgeContextMenu: ee, onEdgeMouseEnter: oe, onEdgeMouseMove: L, onEdgeMouseLeave: J, reconnectRadius: fe, onReconnect: xe, onReconnectStart: Oe, onReconnectEnd: we, noDragClassName: je, noWheelClassName: tt, noPanClassName: Fe, disableKeyboardA11y: Be, nodeExtent: Ce, rfId: Ae, viewport: De, onViewportChange: Ue }) {
  return $s(e), $s(t), qg(), Lg(n), Vg(De), u.jsx(cg, { onPaneClick: ne, onPaneMouseEnter: se, onPaneMouseMove: K, onPaneMouseLeave: R, onPaneContextMenu: re, onPaneScroll: Y, paneClickDistance: ie, deleteKeyCode: D, selectionKeyCode: N, selectionOnDrag: g, selectionMode: w, onSelectionStart: h, onSelectionEnd: p, multiSelectionKeyCode: _, panActivationKeyCode: E, zoomActivationKeyCode: k, elementsSelectable: W, zoomOnScroll: A, zoomOnPinch: T, zoomOnDoubleClick: O, panOnScroll: P, panOnScrollSpeed: V, panOnScrollMode: B, panOnDrag: U, autoPanOnSelection: Z, defaultViewport: M, translateExtent: z, minZoom: H, maxZoom: b, onSelectionContextMenu: f, preventScrolling: I, noDragClassName: je, noWheelClassName: tt, noPanClassName: Fe, disableKeyboardA11y: Be, onViewportChange: Ue, isControlledViewport: !!De, children: u.jsxs(Rg, { children: [u.jsx(Tg, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: s, onReconnect: xe, onReconnectStart: Oe, onReconnectEnd: we, onlyRenderVisibleElements: $, onEdgeContextMenu: ee, onEdgeMouseEnter: oe, onEdgeMouseMove: L, onEdgeMouseLeave: J, reconnectRadius: fe, defaultMarkerColor: C, noPanClassName: Fe, disableKeyboardA11y: Be, rfId: Ae }), u.jsx(Xg, { style: x, type: m, component: v, containerStyle: y }), u.jsx("div", { className: "react-flow__edgelabel-renderer" }), u.jsx(yg, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: i, onNodeMouseEnter: c, onNodeMouseMove: l, onNodeMouseLeave: a, onNodeContextMenu: d, nodeClickDistance: q, onlyRenderVisibleElements: $, noPanClassName: Fe, noDragClassName: je, disableKeyboardA11y: Be, nodeExtent: Ce, rfId: Ae }), u.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Fc.displayName = "GraphView";
const Zg = ve(Fc), Kg = Ba(), Ts = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: c, minZoom: l = 0.5, maxZoom: a = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: h = "basic" } = {}) => {
  const p = /* @__PURE__ */ new Map(), m = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), v = /* @__PURE__ */ new Map(), y = o ?? t ?? [], N = n ?? e ?? [], g = d ?? [0, 0], w = f ?? Sn;
  tc(x, v, y);
  const { nodesInitialized: _ } = Zr(N, p, m, {
    nodeOrigin: g,
    nodeExtent: w,
    zIndexMode: h
  });
  let E = [0, 0, 1];
  if (s && r && i) {
    const k = Pn(p, {
      filter: (M) => !!((M.width || M.initialWidth) && (M.height || M.initialHeight))
    }), { x: D, y: $, zoom: W } = fi(k, r, i, l, a, c?.padding ?? 0.1);
    E = [D, $, W];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: i ?? 0,
    transform: E,
    nodes: N,
    nodesInitialized: _,
    nodeLookup: p,
    parentLookup: m,
    edges: y,
    edgeLookup: v,
    connectionLookup: x,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: o !== void 0,
    panZoom: null,
    minZoom: l,
    maxZoom: a,
    translateExtent: Sn,
    nodeExtent: w,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: Tt.Strict,
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
    connection: { ...za },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: Kg,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Ta,
    zIndexMode: h,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, Ug = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: c, minZoom: l, maxZoom: a, nodeOrigin: d, nodeExtent: f, zIndexMode: h }) => lp((p, m) => {
  async function x() {
    const { nodeLookup: v, panZoom: y, fitViewOptions: N, fitViewResolver: g, width: w, height: _, minZoom: E, maxZoom: k } = m();
    y && (await ih({
      nodes: v,
      width: w,
      height: _,
      panZoom: y,
      minZoom: E,
      maxZoom: k
    }, N), g?.resolve(!0), p({ fitViewResolver: null }));
  }
  return {
    ...Ts({
      nodes: e,
      edges: t,
      width: r,
      height: i,
      fitView: s,
      fitViewOptions: c,
      minZoom: l,
      maxZoom: a,
      nodeOrigin: d,
      nodeExtent: f,
      defaultNodes: n,
      defaultEdges: o,
      zIndexMode: h
    }),
    setNodes: (v) => {
      const { nodeLookup: y, parentLookup: N, nodeOrigin: g, elevateNodesOnSelect: w, fitViewQueued: _, zIndexMode: E, nodesSelectionActive: k } = m(), { nodesInitialized: D, hasSelectedNodes: $ } = Zr(v, y, N, {
        nodeOrigin: g,
        nodeExtent: f,
        elevateNodesOnSelect: w,
        checkEquality: !0,
        zIndexMode: E
      }), W = k && $;
      _ && D ? (x(), p({
        nodes: v,
        nodesInitialized: D,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: W
      })) : p({ nodes: v, nodesInitialized: D, nodesSelectionActive: W });
    },
    setEdges: (v) => {
      const { connectionLookup: y, edgeLookup: N } = m();
      tc(y, N, v), p({ edges: v });
    },
    setDefaultNodesAndEdges: (v, y) => {
      if (v) {
        const { setNodes: N } = m();
        N(v), p({ hasDefaultNodes: !0 });
      }
      if (y) {
        const { setEdges: N } = m();
        N(y), p({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (v) => {
      const { triggerNodeChanges: y, nodeLookup: N, parentLookup: g, domNode: w, nodeOrigin: _, nodeExtent: E, debug: k, fitViewQueued: D, zIndexMode: $ } = m(), { changes: W, updatedInternals: M } = Ih(v, N, g, w, _, E, $);
      M && (Eh(N, g, { nodeOrigin: _, nodeExtent: E, zIndexMode: $ }), D ? (x(), p({ fitViewQueued: !1, fitViewOptions: void 0 })) : p({}), W?.length > 0 && (k && console.log("React Flow: trigger node changes", W), y?.(W)));
    },
    updateNodePositions: (v, y = !1) => {
      const N = [];
      let g = [];
      const { nodeLookup: w, triggerNodeChanges: _, connection: E, updateConnection: k, onNodesChangeMiddlewareMap: D } = m();
      for (const [$, W] of v) {
        const M = w.get($), z = !!(M?.expandParent && M?.parentId && W?.position), H = {
          id: $,
          type: "position",
          position: z ? {
            x: Math.max(0, W.position.x),
            y: Math.max(0, W.position.y)
          } : W.position,
          dragging: y
        };
        if (M && E.inProgress && E.fromNode.id === M.id) {
          const b = bt(M, E.fromHandle, te.Left, !0);
          k({ ...E, from: b });
        }
        z && M.parentId && N.push({
          id: $,
          parentId: M.parentId,
          rect: {
            ...W.internals.positionAbsolute,
            width: W.measured.width ?? 0,
            height: W.measured.height ?? 0
          }
        }), g.push(H);
      }
      if (N.length > 0) {
        const { parentLookup: $, nodeOrigin: W } = m(), M = xi(N, w, $, W);
        g.push(...M);
      }
      for (const $ of D.values())
        g = $(g);
      _(g);
    },
    triggerNodeChanges: (v) => {
      const { onNodesChange: y, setNodes: N, nodes: g, hasDefaultNodes: w, debug: _ } = m();
      if (v?.length) {
        if (w) {
          const E = mc(v, g);
          N(E);
        }
        _ && console.log("React Flow: trigger node changes", v), y?.(v);
      }
    },
    triggerEdgeChanges: (v) => {
      const { onEdgesChange: y, setEdges: N, edges: g, hasDefaultEdges: w, debug: _ } = m();
      if (v?.length) {
        if (w) {
          const E = yc(v, g);
          N(E);
        }
        _ && console.log("React Flow: trigger edge changes", v), y?.(v);
      }
    },
    addSelectedNodes: (v) => {
      const { multiSelectionActive: y, edgeLookup: N, nodeLookup: g, triggerNodeChanges: w, triggerEdgeChanges: _ } = m();
      if (y) {
        const E = v.map((k) => ht(k, !0));
        w(E);
        return;
      }
      w(At(g, /* @__PURE__ */ new Set([...v]), !0)), _(At(N));
    },
    addSelectedEdges: (v) => {
      const { multiSelectionActive: y, edgeLookup: N, nodeLookup: g, triggerNodeChanges: w, triggerEdgeChanges: _ } = m();
      if (y) {
        const E = v.map((k) => ht(k, !0));
        _(E);
        return;
      }
      _(At(N, /* @__PURE__ */ new Set([...v]))), w(At(g, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: v, edges: y } = {}) => {
      const { edges: N, nodes: g, nodeLookup: w, triggerNodeChanges: _, triggerEdgeChanges: E } = m(), k = v || g, D = y || N, $ = [];
      for (const M of k) {
        if (!M.selected)
          continue;
        const z = w.get(M.id);
        z && (z.selected = !1), $.push(ht(M.id, !1));
      }
      const W = [];
      for (const M of D)
        M.selected && W.push(ht(M.id, !1));
      _($), E(W);
    },
    setMinZoom: (v) => {
      const { panZoom: y, maxZoom: N } = m();
      y?.setScaleExtent([v, N]), p({ minZoom: v });
    },
    setMaxZoom: (v) => {
      const { panZoom: y, minZoom: N } = m();
      y?.setScaleExtent([N, v]), p({ maxZoom: v });
    },
    setTranslateExtent: (v) => {
      m().panZoom?.setTranslateExtent(v), p({ translateExtent: v });
    },
    resetSelectedElements: () => {
      const { edges: v, nodes: y, triggerNodeChanges: N, triggerEdgeChanges: g, elementsSelectable: w } = m();
      if (!w)
        return;
      const _ = y.reduce((k, D) => D.selected ? [...k, ht(D.id, !1)] : k, []), E = v.reduce((k, D) => D.selected ? [...k, ht(D.id, !1)] : k, []);
      N(_), g(E);
    },
    setNodeExtent: (v) => {
      const { nodes: y, nodeLookup: N, parentLookup: g, nodeOrigin: w, elevateNodesOnSelect: _, nodeExtent: E, zIndexMode: k } = m();
      v[0][0] === E[0][0] && v[0][1] === E[0][1] && v[1][0] === E[1][0] && v[1][1] === E[1][1] || (Zr(y, N, g, {
        nodeOrigin: w,
        nodeExtent: v,
        elevateNodesOnSelect: _,
        checkEquality: !1,
        zIndexMode: k
      }), p({ nodeExtent: v }));
    },
    panBy: (v) => {
      const { transform: y, width: N, height: g, panZoom: w, translateExtent: _ } = m();
      return Mh({ delta: v, panZoom: w, transform: y, translateExtent: _, width: N, height: g });
    },
    setCenter: async (v, y, N) => {
      const { width: g, height: w, maxZoom: _, panZoom: E } = m();
      if (!E)
        return !1;
      const k = typeof N?.zoom < "u" ? N.zoom : _;
      return await E.setViewport({
        x: g / 2 - v * k,
        y: w / 2 - y * k,
        zoom: k
      }, { duration: N?.duration, ease: N?.ease, interpolate: N?.interpolate }), !0;
    },
    cancelConnection: () => {
      p({
        connection: { ...za }
      });
    },
    updateConnection: (v) => {
      p({ connection: v });
    },
    reset: () => p({ ...Ts() })
  };
}, Object.is);
function Gg({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: i, initialMinZoom: s, initialMaxZoom: c, initialFitViewOptions: l, fitView: a, nodeOrigin: d, nodeExtent: f, zIndexMode: h, children: p }) {
  const [m] = G(() => Ug({
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
    nodeOrigin: d,
    nodeExtent: f,
    zIndexMode: h
  }));
  return u.jsx(hp, { value: m, children: u.jsx(Rp, { children: p }) });
}
function Qg({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: i, height: s, fitView: c, fitViewOptions: l, minZoom: a, maxZoom: d, nodeOrigin: f, nodeExtent: h, zIndexMode: p }) {
  return Mn(Fo) ? u.jsx(u.Fragment, { children: e }) : u.jsx(Gg, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: i, initialHeight: s, fitView: c, initialFitViewOptions: l, initialMinZoom: a, initialMaxZoom: d, nodeOrigin: f, nodeExtent: h, zIndexMode: p, children: e });
}
const Jg = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function em({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: i, edgeTypes: s, onNodeClick: c, onEdgeClick: l, onInit: a, onMove: d, onMoveStart: f, onMoveEnd: h, onConnect: p, onConnectStart: m, onConnectEnd: x, onClickConnectStart: v, onClickConnectEnd: y, onNodeMouseEnter: N, onNodeMouseMove: g, onNodeMouseLeave: w, onNodeContextMenu: _, onNodeDoubleClick: E, onNodeDragStart: k, onNodeDrag: D, onNodeDragStop: $, onNodesDelete: W, onEdgesDelete: M, onDelete: z, onSelectionChange: H, onSelectionDragStart: b, onSelectionDrag: I, onSelectionDragStop: C, onSelectionContextMenu: A, onSelectionStart: T, onSelectionEnd: P, onBeforeDelete: V, connectionMode: B, connectionLineType: O = ct.Bezier, connectionLineStyle: U, connectionLineComponent: Z, connectionLineContainerStyle: ne, deleteKeyCode: se = "Backspace", selectionKeyCode: K = "Shift", selectionOnDrag: R = !1, selectionMode: Y = Nn.Full, panActivationKeyCode: re = "Space", multiSelectionKeyCode: ie = _n() ? "Meta" : "Control", zoomActivationKeyCode: q = _n() ? "Meta" : "Control", snapToGrid: ee, snapGrid: oe, onlyRenderVisibleElements: L = !1, selectNodesOnDrag: J, nodesDraggable: fe, autoPanOnNodeFocus: xe, nodesConnectable: Oe, nodesFocusable: we, nodeOrigin: je = pc, edgesFocusable: tt, edgesReconnectable: Fe, elementsSelectable: Be = !0, defaultViewport: Ce = Cp, minZoom: Ae = 0.5, maxZoom: De = 2, translateExtent: Ue = Sn, preventScrolling: Te = !0, nodeExtent: St, defaultMarkerColor: ye = "#b1b1b7", zoomOnScroll: We = !0, zoomOnPinch: Xt = !0, panOnScroll: Ee = !1, panOnScrollSpeed: Yt = 0.5, panOnScrollMode: qt = mt.Free, zoomOnDoubleClick: ke = !0, panOnDrag: dt = !0, onPaneClick: Rn, onPaneMouseEnter: Ln, onPaneMouseMove: Zt, onPaneMouseLeave: Ko, onPaneScroll: Kt, onPaneContextMenu: Nt, paneClickDistance: ze = 1, nodeClickDistance: Ut = 0, children: Gt, onReconnect: Qt, onReconnectStart: Et, onReconnectEnd: Uo, onEdgeContextMenu: Go, onEdgeDoubleClick: Qo, onEdgeMouseEnter: Jo, onEdgeMouseMove: Hn, onEdgeMouseLeave: Vn, reconnectRadius: On = 10, onNodesChange: er, onEdgesChange: Jt, noDragClassName: tr = "nodrag", noWheelClassName: nr = "nowheel", noPanClassName: Bn = "nopan", fitView: Fn, fitViewOptions: Wn, connectOnClick: en, attributionPosition: or, proOptions: rr, defaultEdgeOptions: ir, elevateNodesOnSelect: sr = !0, elevateEdgesOnSelect: ar = !1, disableKeyboardA11y: Xn = !1, autoPanOnConnect: cr, autoPanOnNodeDrag: Yn, autoPanOnSelection: qn = !0, autoPanSpeed: lr, connectionRadius: ur, isValidConnection: dr, onError: fr, style: hr, id: tn, nodeDragThreshold: Zn, connectionDragThreshold: Kn, viewport: Un, onViewportChange: Gn, width: pr, height: gr, colorMode: mr = "light", debug: yr, onScroll: Qn, ariaLabelConfig: Jn, zIndexMode: _t = "basic", ...nn }, eo) {
  const Ct = tn || "1", xr = Ap(mr), wr = he((S) => {
    S.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), Qn?.(S);
  }, [Qn]);
  return u.jsx("div", { "data-testid": "rf__wrapper", ...nn, onScroll: wr, style: { ...hr, ...Jg }, ref: eo, className: Se(["react-flow", r, xr]), id: tn, role: "application", children: u.jsxs(Qg, { nodes: e, edges: t, width: pr, height: gr, fitView: Fn, fitViewOptions: Wn, minZoom: Ae, maxZoom: De, nodeOrigin: je, nodeExtent: St, zIndexMode: _t, children: [u.jsx(Mp, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: p, onConnectStart: m, onConnectEnd: x, onClickConnectStart: v, onClickConnectEnd: y, nodesDraggable: fe, autoPanOnNodeFocus: xe, nodesConnectable: Oe, nodesFocusable: we, edgesFocusable: tt, edgesReconnectable: Fe, elementsSelectable: Be, elevateNodesOnSelect: sr, elevateEdgesOnSelect: ar, minZoom: Ae, maxZoom: De, nodeExtent: St, onNodesChange: er, onEdgesChange: Jt, snapToGrid: ee, snapGrid: oe, connectionMode: B, translateExtent: Ue, connectOnClick: en, defaultEdgeOptions: ir, fitView: Fn, fitViewOptions: Wn, onNodesDelete: W, onEdgesDelete: M, onDelete: z, onNodeDragStart: k, onNodeDrag: D, onNodeDragStop: $, onSelectionDrag: I, onSelectionDragStart: b, onSelectionDragStop: C, onMove: d, onMoveStart: f, onMoveEnd: h, noPanClassName: Bn, nodeOrigin: je, rfId: Ct, autoPanOnConnect: cr, autoPanOnNodeDrag: Yn, autoPanSpeed: lr, onError: fr, connectionRadius: ur, isValidConnection: dr, selectNodesOnDrag: J, nodeDragThreshold: Zn, connectionDragThreshold: Kn, onBeforeDelete: V, debug: yr, ariaLabelConfig: Jn, zIndexMode: _t }), u.jsx(Zg, { onInit: a, onNodeClick: c, onEdgeClick: l, onNodeMouseEnter: N, onNodeMouseMove: g, onNodeMouseLeave: w, onNodeContextMenu: _, onNodeDoubleClick: E, nodeTypes: i, edgeTypes: s, connectionLineType: O, connectionLineStyle: U, connectionLineComponent: Z, connectionLineContainerStyle: ne, selectionKeyCode: K, selectionOnDrag: R, selectionMode: Y, deleteKeyCode: se, multiSelectionKeyCode: ie, panActivationKeyCode: re, zoomActivationKeyCode: q, onlyRenderVisibleElements: L, defaultViewport: Ce, translateExtent: Ue, minZoom: Ae, maxZoom: De, preventScrolling: Te, zoomOnScroll: We, zoomOnPinch: Xt, zoomOnDoubleClick: ke, panOnScroll: Ee, panOnScrollSpeed: Yt, panOnScrollMode: qt, panOnDrag: dt, autoPanOnSelection: qn, onPaneClick: Rn, onPaneMouseEnter: Ln, onPaneMouseMove: Zt, onPaneMouseLeave: Ko, onPaneScroll: Kt, onPaneContextMenu: Nt, paneClickDistance: ze, nodeClickDistance: Ut, onSelectionContextMenu: A, onSelectionStart: T, onSelectionEnd: P, onReconnect: Qt, onReconnectStart: Et, onReconnectEnd: Uo, onEdgeContextMenu: Go, onEdgeDoubleClick: Qo, onEdgeMouseEnter: Jo, onEdgeMouseMove: Hn, onEdgeMouseLeave: Vn, reconnectRadius: On, defaultMarkerColor: ye, noDragClassName: tr, noWheelClassName: nr, noPanClassName: Bn, rfId: Ct, disableKeyboardA11y: Xn, nodeExtent: St, viewport: Un, onViewportChange: Gn }), u.jsx(_p, { onSelectionChange: H }), Gt, u.jsx(vp, { proOptions: rr, position: or }), u.jsx(wp, { rfId: Ct, disableKeyboardA11y: Xn })] }) });
}
var tm = vc(em);
const nm = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function om({ children: e }) {
  const t = le(nm);
  return t ? fp.createPortal(e, t) : null;
}
function rm({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return u.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Se(["react-flow__background-pattern", n, o]) });
}
function im({ radius: e, className: t }) {
  return u.jsx("circle", { cx: e, cy: e, r: e, className: Se(["react-flow__background-pattern", "dots", t]) });
}
var lt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(lt || (lt = {}));
const sm = {
  [lt.Dots]: 1,
  [lt.Lines]: 1,
  [lt.Cross]: 6
}, am = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function Wc({
  id: e,
  variant: t = lt.Dots,
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
  patternClassName: d
}) {
  const f = ce(null), { transform: h, patternId: p } = le(am, ge), m = o || sm[t], x = t === lt.Dots, v = t === lt.Cross, y = Array.isArray(n) ? n : [n, n], N = [y[0] * h[2] || 1, y[1] * h[2] || 1], g = m * h[2], w = Array.isArray(i) ? i : [i, i], _ = v ? [g, g] : N, E = [
    w[0] * h[2] || 1 + _[0] / 2,
    w[1] * h[2] || 1 + _[1] / 2
  ], k = `${p}${e || ""}`;
  return u.jsxs("svg", { className: Se(["react-flow__background", a]), style: {
    ...l,
    ...Xo,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": s
  }, ref: f, "data-testid": "rf__background", children: [u.jsx("pattern", { id: k, x: h[0] % N[0], y: h[1] % N[1], width: N[0], height: N[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${E[0]},-${E[1]})`, children: x ? u.jsx(im, { radius: g / 2, className: d }) : u.jsx(rm, { dimensions: _, lineWidth: r, variant: t, className: d }) }), u.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${k})` })] });
}
Wc.displayName = "Background";
const cm = ve(Wc);
function lm() {
  return u.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: u.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function um() {
  return u.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: u.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function dm() {
  return u.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: u.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function fm() {
  return u.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: u.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function hm() {
  return u.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: u.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function lo({ children: e, className: t, ...n }) {
  return u.jsx("button", { type: "button", className: Se(["react-flow__controls-button", t]), ...n, children: e });
}
const pm = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function Xc({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: i, onZoomOut: s, onFitView: c, onInteractiveChange: l, className: a, children: d, position: f = "bottom-left", orientation: h = "vertical", "aria-label": p }) {
  const m = me(), { isInteractive: x, minZoomReached: v, maxZoomReached: y, ariaLabelConfig: N } = le(pm, ge), { zoomIn: g, zoomOut: w, fitView: _ } = wi(), E = () => {
    g(), i?.();
  }, k = () => {
    w(), s?.();
  }, D = () => {
    _(r), c?.();
  }, $ = () => {
    m.setState({
      nodesDraggable: !x,
      nodesConnectable: !x,
      elementsSelectable: !x
    }), l?.(!x);
  }, W = h === "horizontal" ? "horizontal" : "vertical";
  return u.jsxs(Wo, { className: Se(["react-flow__controls", W, a]), position: f, style: e, "data-testid": "rf__controls", "aria-label": p ?? N["controls.ariaLabel"], children: [t && u.jsxs(u.Fragment, { children: [u.jsx(lo, { onClick: E, className: "react-flow__controls-zoomin", title: N["controls.zoomIn.ariaLabel"], "aria-label": N["controls.zoomIn.ariaLabel"], disabled: y, children: u.jsx(lm, {}) }), u.jsx(lo, { onClick: k, className: "react-flow__controls-zoomout", title: N["controls.zoomOut.ariaLabel"], "aria-label": N["controls.zoomOut.ariaLabel"], disabled: v, children: u.jsx(um, {}) })] }), n && u.jsx(lo, { className: "react-flow__controls-fitview", onClick: D, title: N["controls.fitView.ariaLabel"], "aria-label": N["controls.fitView.ariaLabel"], children: u.jsx(dm, {}) }), o && u.jsx(lo, { className: "react-flow__controls-interactive", onClick: $, title: N["controls.interactive.ariaLabel"], "aria-label": N["controls.interactive.ariaLabel"], children: x ? u.jsx(hm, {}) : u.jsx(fm, {}) }), d] });
}
Xc.displayName = "Controls";
const gm = ve(Xc);
function mm({ id: e, x: t, y: n, width: o, height: r, style: i, color: s, strokeColor: c, strokeWidth: l, className: a, borderRadius: d, shapeRendering: f, selected: h, onClick: p }) {
  const { background: m, backgroundColor: x } = i || {}, v = s || m || x;
  return u.jsx("rect", { className: Se(["react-flow__minimap-node", { selected: h }, a]), x: t, y: n, rx: d, ry: d, width: o, height: r, style: {
    fill: v,
    stroke: c,
    strokeWidth: l
  }, shapeRendering: f, onClick: p ? (y) => p(y, e) : void 0 });
}
const ym = ve(mm), xm = (e) => e.nodes.map((t) => t.id), $r = (e) => e instanceof Function ? e : () => e;
function wm({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: i = ym,
  onClick: s
}) {
  const c = le(xm, ge), l = $r(t), a = $r(e), d = $r(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return u.jsx(u.Fragment, { children: c.map((h) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    u.jsx(bm, { id: h, nodeColorFunc: l, nodeStrokeColorFunc: a, nodeClassNameFunc: d, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: i, onClick: s, shapeRendering: f }, h)
  )) });
}
function vm({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: i, shapeRendering: s, NodeComponent: c, onClick: l }) {
  const { node: a, x: d, y: f, width: h, height: p } = le((m) => {
    const x = m.nodeLookup.get(e);
    if (!x)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const v = x.internals.userNode, { x: y, y: N } = x.internals.positionAbsolute, { width: g, height: w } = it(v);
    return {
      node: v,
      x: y,
      y: N,
      width: g,
      height: w
    };
  }, ge);
  return !a || a.hidden || !Fa(a) ? null : u.jsx(c, { x: d, y: f, width: h, height: p, style: a.style, selected: !!a.selected, className: o(a), color: t(a), borderRadius: r, strokeColor: n(a), strokeWidth: i, shapeRendering: s, onClick: l, id: a.id });
}
const bm = ve(vm);
var Sm = ve(wm);
const Nm = 200, Em = 150, _m = (e) => !e.hidden, Cm = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? Oa(Pn(e.nodeLookup, { filter: _m }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, km = "react-flow__minimap-desc";
function Yc({
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
  maskStrokeColor: d,
  maskStrokeWidth: f,
  position: h = "bottom-right",
  onClick: p,
  onNodeClick: m,
  pannable: x = !1,
  zoomable: v = !1,
  ariaLabel: y,
  inversePan: N,
  zoomStep: g = 1,
  offsetScale: w = 5
}) {
  const _ = me(), E = ce(null), { boundingRect: k, viewBB: D, rfId: $, panZoom: W, translateExtent: M, flowWidth: z, flowHeight: H, ariaLabelConfig: b } = le(Cm, ge), I = e?.width ?? Nm, C = e?.height ?? Em, A = k.width / I, T = k.height / C, P = Math.max(A, T), V = P * I, B = P * C, O = w * P, U = k.x - (V - k.width) / 2 - O, Z = k.y - (B - k.height) / 2 - O, ne = V + O * 2, se = B + O * 2, K = `${km}-${$}`, R = ce(0), Y = ce();
  R.current = P, ae(() => {
    if (E.current && W)
      return Y.current = Lh({
        domNode: E.current,
        panZoom: W,
        getTransform: () => _.getState().transform,
        getViewScale: () => R.current
      }), () => {
        Y.current?.destroy();
      };
  }, [W]), ae(() => {
    Y.current?.update({
      translateExtent: M,
      width: z,
      height: H,
      inversePan: N,
      pannable: x,
      zoomStep: g,
      zoomable: v
    });
  }, [x, v, N, g, M, z, H]);
  const re = p ? (ee) => {
    const [oe, L] = Y.current?.pointer(ee) || [0, 0];
    p(ee, { x: oe, y: L });
  } : void 0, ie = m ? he((ee, oe) => {
    const L = _.getState().nodeLookup.get(oe).internals.userNode;
    m(ee, L);
  }, []) : void 0, q = y ?? b["minimap.ariaLabel"];
  return u.jsx(Wo, { position: h, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-background-color-props": typeof a == "string" ? a : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * P : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof s == "number" ? s : void 0
  }, className: Se(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: u.jsxs("svg", { width: I, height: C, viewBox: `${U} ${Z} ${ne} ${se}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": K, ref: E, onClick: re, children: [q && u.jsx("title", { id: K, children: q }), u.jsx(Sm, { onClick: ie, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: i, nodeClassName: r, nodeStrokeWidth: s, nodeComponent: c }), u.jsx("path", { className: "react-flow__minimap-mask", d: `M${U - O},${Z - O}h${ne + O * 2}v${se + O * 2}h${-ne - O * 2}z
        M${D.x},${D.y}h${D.width}v${D.height}h${-D.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
Yc.displayName = "MiniMap";
const Im = ve(Yc), Mm = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, Am = {
  [Ht.Line]: "right",
  [Ht.Handle]: "bottom-right"
};
function jm({ nodeId: e, position: t, variant: n = Ht.Handle, className: o, style: r = void 0, children: i, color: s, minWidth: c = 10, minHeight: l = 10, maxWidth: a = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: h, autoScale: p = !0, shouldResize: m, onResizeStart: x, onResize: v, onResizeEnd: y }) {
  const N = Ec(), g = typeof e == "string" ? e : N, w = me(), _ = ce(null), E = n === Ht.Handle, k = le(he(Mm(E && p), [E, p]), ge), D = ce(null), $ = t ?? Am[n];
  ae(() => {
    if (!(!_.current || !g))
      return D.current || (D.current = Gh({
        domNode: _.current,
        nodeId: g,
        getStoreItems: () => {
          const { nodeLookup: M, transform: z, snapGrid: H, snapToGrid: b, nodeOrigin: I, domNode: C } = w.getState();
          return {
            nodeLookup: M,
            transform: z,
            snapGrid: H,
            snapToGrid: b,
            nodeOrigin: I,
            paneDomNode: C
          };
        },
        onChange: (M, z) => {
          const { triggerNodeChanges: H, nodeLookup: b, parentLookup: I, nodeOrigin: C } = w.getState(), A = [], T = { x: M.x, y: M.y }, P = b.get(g);
          if (P && P.expandParent && P.parentId) {
            const V = P.origin ?? C, B = M.width ?? P.measured.width ?? 0, O = M.height ?? P.measured.height ?? 0, U = {
              id: P.id,
              parentId: P.parentId,
              rect: {
                width: B,
                height: O,
                ...Wa({
                  x: M.x ?? P.position.x,
                  y: M.y ?? P.position.y
                }, { width: B, height: O }, P.parentId, b, V)
              }
            }, Z = xi([U], b, I, C);
            A.push(...Z), T.x = M.x ? Math.max(V[0] * B, M.x) : void 0, T.y = M.y ? Math.max(V[1] * O, M.y) : void 0;
          }
          if (T.x !== void 0 && T.y !== void 0) {
            const V = {
              id: g,
              type: "position",
              position: { ...T }
            };
            A.push(V);
          }
          if (M.width !== void 0 && M.height !== void 0) {
            const B = {
              id: g,
              type: "dimensions",
              resizing: !0,
              setAttributes: h ? h === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: M.width,
                height: M.height
              }
            };
            A.push(B);
          }
          for (const V of z) {
            const B = {
              ...V,
              type: "position"
            };
            A.push(B);
          }
          H(A);
        },
        onEnd: ({ width: M, height: z }) => {
          const H = {
            id: g,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: M,
              height: z
            }
          };
          w.getState().triggerNodeChanges([H]);
        }
      })), D.current.update({
        controlPosition: $,
        boundaries: {
          minWidth: c,
          minHeight: l,
          maxWidth: a,
          maxHeight: d
        },
        keepAspectRatio: f,
        resizeDirection: h,
        onResizeStart: x,
        onResize: v,
        onResizeEnd: y,
        shouldResize: m
      }), () => {
        D.current?.destroy();
      };
  }, [
    $,
    c,
    l,
    a,
    d,
    f,
    x,
    v,
    y,
    m
  ]);
  const W = $.split("-");
  return u.jsx("div", { className: Se(["react-flow__resize-control", "nodrag", ...W, n, o]), ref: _, style: {
    ...r,
    scale: k,
    ...s && { [E ? "backgroundColor" : "borderColor"]: s }
  }, children: i });
}
ve(jm);
const Dm = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), qc = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var Pm = {
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
const $m = $o(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: o,
    className: r = "",
    children: i,
    iconNode: s,
    ...c
  }, l) => Rr(
    "svg",
    {
      ref: l,
      ...Pm,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: qc("lucide", r),
      ...c
    },
    [
      ...s.map(([a, d]) => Rr(a, d)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const Ne = (e, t) => {
  const n = $o(
    ({ className: o, ...r }, i) => Rr($m, {
      ref: i,
      iconNode: t,
      className: qc(`lucide-${Dm(e)}`, o),
      ...r
    })
  );
  return n.displayName = `${e}`, n;
};
const Zc = Ne("Boxes", [
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
const Yo = Ne("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const Tm = Ne("ChevronDown", [
  ["path", { d: "m6 9 6 6 6-6", key: "qrunsl" }]
]);
const Gr = Ne("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const It = Ne("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const kn = Ne("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const Kc = Ne("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const Uc = Ne("ListTree", [
  ["path", { d: "M21 12h-8", key: "1bmf0i" }],
  ["path", { d: "M21 6H8", key: "1pqkrb" }],
  ["path", { d: "M21 18h-8", key: "1tm79t" }],
  ["path", { d: "M3 6v4c0 1.1.9 2 2 2h3", key: "1ywdgy" }],
  ["path", { d: "M3 10v6c0 1.1.9 2 2 2h3", key: "2wc746" }]
]);
const zs = Ne("Maximize2", [
  ["polyline", { points: "15 3 21 3 21 9", key: "mznyad" }],
  ["polyline", { points: "9 21 3 21 3 15", key: "1avn1i" }],
  ["line", { x1: "21", x2: "14", y1: "3", y2: "10", key: "ota7mn" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const Rs = Ne("Minimize2", [
  ["polyline", { points: "4 14 10 14 10 20", key: "11kfnr" }],
  ["polyline", { points: "20 10 14 10 14 4", key: "rlmsce" }],
  ["line", { x1: "14", x2: "21", y1: "10", y2: "3", key: "o5lafz" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const bi = Ne("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const Si = Ne("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const zm = Ne("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const Rm = Ne("Save", [
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
const Lm = Ne("Search", [
  ["circle", { cx: "11", cy: "11", r: "8", key: "4ej97u" }],
  ["path", { d: "m21 21-4.3-4.3", key: "1qie3q" }]
]);
const Ot = Ne("Sparkles", [
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
const Hm = Ne("Terminal", [
  ["polyline", { points: "4 17 10 11 4 5", key: "akl6gq" }],
  ["line", { x1: "12", x2: "20", y1: "19", y2: "19", key: "q2wloq" }]
]);
const Qr = Ne("Trash2", [
  ["path", { d: "M3 6h18", key: "d0wm0j" }],
  ["path", { d: "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", key: "4alrt4" }],
  ["path", { d: "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", key: "v07s0e" }],
  ["line", { x1: "10", x2: "10", y1: "11", y2: "17", key: "1uufr5" }],
  ["line", { x1: "14", x2: "14", y1: "11", y2: "17", key: "xtxkd" }]
]);
const Vm = Ne("Zap", [
  [
    "path",
    {
      d: "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z",
      key: "1xq2db"
    }
  ]
]), Ve = "/_elsa/workflow-management";
async function Om(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${Ve}/definitions?${n.toString()}`);
}
async function Bm(e, t) {
  return e.http.getJson(`${Ve}/definitions/${encodeURIComponent(t)}`);
}
async function Fm(e, t) {
  return e.http.postJson(`${Ve}/definitions`, t);
}
async function Wm(e, t) {
  await e.http.deleteJson(`${Ve}/definitions/${encodeURIComponent(t)}`);
}
async function Xm(e, t) {
  await e.http.postJson(`${Ve}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function Ym(e, t) {
  await e.http.deleteJson(`${Ve}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function qm(e, t) {
  return e.http.putJson(`${Ve}/drafts/${encodeURIComponent(t.id)}`, { state: t.state, layout: t.layout });
}
async function Zm(e, t) {
  return e.http.postJson(`${Ve}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function Km(e, t) {
  return e.http.postJson(`${Ve}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function Gc(e, t) {
  return e.http.postJson(`${Ve}/executables/${encodeURIComponent(t)}/run`, {});
}
async function Um(e) {
  return e.http.getJson("/_demo/workflows/executables");
}
async function Qc(e) {
  return e.http.getJson(`${Ve}/activities`);
}
async function Gm(e) {
  const t = await Jc(e, [
    `${Ve}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? Ls(t) : Ls(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function Qm(e) {
  const t = await Jc(e, [
    `${Ve}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : xo;
}
async function Jc(e, t) {
  let n;
  for (const o of t)
    try {
      return await e.http.getJson(o);
    } catch (r) {
      n = r;
    }
  throw n;
}
function Ls(e) {
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
const xo = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
], qo = "elsa.sequence.structure", zn = "elsa.flowchart.structure";
function el(e, t) {
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
function Tr(e, t) {
  const n = el(e, t);
  if (!n) return null;
  let o = Je(n)[0];
  return o ? { owner: n, slot: o } : null;
}
function Je(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = wy(t), r = zr(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: vy(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, i]) => zr(i)).map(([i, s]) => ({
    id: `${t.kind}:${i}`,
    label: Sy(i),
    property: i,
    mode: "generic",
    activities: zr(s) ?? []
  }));
}
function Jm(e, t, n) {
  const o = new Map(t.map((s) => [s.activityVersionId, s])), r = new Map(n.map((s) => [s.nodeId, s])), i = e.slot.activities.map((s, c) => {
    const l = o.get(s.activityVersionId), a = r.get(s.nodeId) ?? by(e.slot.mode, c);
    return ol(s, l, { x: a.x, y: a.y });
  });
  return {
    nodes: i,
    edges: e.slot.mode === "flowchart" ? fy(e.owner) : dy(e.slot, i)
  };
}
function ey(e, t, n) {
  const o = t.find((i) => i.activityVersionId === e.activityVersionId), r = n.find((i) => i.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [ol(e, o, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function ty(e, t) {
  return e?.structure?.kind === zn || sy(t) ? "flowchart" : e?.structure?.kind === qo || ay(t) ? "sequence" : "unsupported";
}
function Jr(e, t, n) {
  if (t.length === 0) {
    const c = Je(e)[0];
    return c ? In(e, c, n) : e;
  }
  const [o, ...r] = t, i = Je(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((c) => c.nodeId === o.ownerNodeId ? Jr(c, r, n) : c);
  return In(e, i, s);
}
function tl(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, i = Je(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((c) => c.nodeId === o.ownerNodeId ? tl(c, r, n) : c);
  return In(e, i, s);
}
function nl(e, t, n) {
  if (e.nodeId === t) return n(e);
  const o = Je(e);
  if (o.length === 0) return e;
  let r = !1, i = e;
  for (const s of o) {
    const c = s.activities.map((l) => {
      const a = nl(l, t, n);
      return a !== l && (r = !0), a;
    });
    r && (i = In(i, s, c));
  }
  return r ? i : e;
}
function In(e, t, n) {
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
function ny(e, t, n, o = []) {
  const r = new Map(e.slot.activities.map((s) => [s.nodeId, s]));
  for (const s of o)
    r.set(s.nodeId, s);
  const i = t.map((s) => r.get(s.id)).filter((s) => !!s);
  return e.slot.mode === "sequence" && i.sort((s, c) => {
    const l = t.find((d) => d.id === s.nodeId), a = t.find((d) => d.id === c.nodeId);
    return (l?.position.x ?? 0) - (a?.position.x ?? 0);
  }), In(e.owner, e.slot, i);
}
function oy(e, t) {
  return {
    ...e,
    structure: uy(e.structure, t)
  };
}
function ry(e, t) {
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
function Hs(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: ly(e)
  };
}
function _e(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? cy(t) : n;
}
function ol(e, t, n, o = {}) {
  return {
    id: e.nodeId,
    type: "workflowActivity",
    position: n,
    connectable: o.connectable,
    deletable: o.deletable,
    draggable: o.draggable,
    data: {
      label: t ? _e(t) : e.activityVersionId,
      activityVersionId: e.activityVersionId,
      activityTypeKey: t?.activityTypeKey,
      category: t?.category,
      executionType: t?.executionType,
      icon: rl(t),
      childSlots: Je(e),
      acceptsInbound: hy(e, t),
      sourcePorts: o.suppressFlowPorts ? [] : il(e, t),
      suppressFlowPorts: o.suppressFlowPorts
    }
  };
}
function rl(e) {
  if (!e) return "activity";
  const t = iy(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), o = _e(e).toLowerCase(), r = e.category?.toLowerCase() ?? "", i = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || o === "flowchart" ? "flowchart" : n.endsWith(".sequence") || o === "sequence" ? "sequence" : n.includes("writeline") || o.includes("write line") ? "terminal" : r.includes("runtime") ? "runtime" : i === "trigger" ? "trigger" : "activity";
}
function iy(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function sy(e) {
  return !!e && (_e(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function ay(e) {
  return !!e && (_e(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function cy(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function ly(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: qo,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: zn,
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
function uy(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], o = /* @__PURE__ */ new Map();
  for (const r of n) {
    if (!Ni(r)) continue;
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
function dy(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function fy(e) {
  if (e.structure?.kind !== zn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, i = n.target;
    if (!r?.nodeId || !i?.nodeId) return null;
    const s = Array.isArray(n.vertices) ? n.vertices.filter(yy) : [];
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
function il(e, t) {
  const n = Vs(e.cases);
  if (gy(e, t) && n.length > 0)
    return [...n.map((i) => ({ name: i, displayName: i })), { name: "Default", displayName: "Default" }];
  const o = [
    ...wo(t?.designFacets),
    ...wo(t?.ports),
    ...wo(t?.outputs)
  ];
  if (o.length > 0) return my(o);
  const r = Vs(e.outcomes);
  return r.length > 0 ? r.map((i) => ({ name: i, displayName: i })) : [{ name: "Done", displayName: "Done" }];
}
function hy(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function Do(e, t, n, o) {
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
function py(e, t, n) {
  const o = Do(t.source, n, t.sourceHandle ?? "Done", void 0), r = Do(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((i) => i.id !== t.id).concat(o, r);
}
function zr(e) {
  return Array.isArray(e) ? e.filter(xy) : null;
}
function gy(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function wo(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!Ni(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...wo(n.ports));
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
function my(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function Vs(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function yy(e) {
  return Ni(e) && typeof e.x == "number" && typeof e.y == "number";
}
function Ni(e) {
  return typeof e == "object" && e !== null;
}
function xy(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function wy(e) {
  return e.kind === qo ? "sequence" : e.kind === zn ? "flowchart" : "generic";
}
function vy(e) {
  return e.kind === qo || e.kind === zn, "Activities";
}
function by(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function Sy(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Ny = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function Ey(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function Ei(e) {
  return Ey(e.name);
}
function _y(e, t) {
  const n = Ei(t), o = e[n];
  return t.isWrapped === !1 ? o ?? t.defaultValue ?? "" : al(o, t);
}
function sl(e, t) {
  return al(e[Ei(t)], t);
}
function Cy(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function ky(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function Os(e, t, n) {
  return {
    ...e,
    [Ei(t)]: n
  };
}
function Iy(e, t) {
  return t.isWrapped === !1 ? _y(e, t) : sl(e, t).expression.value;
}
function al(e, t) {
  return My(e) ? {
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
function My(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
const Ay = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]);
function jy({
  activity: e,
  descriptor: t,
  editors: n,
  expressionDescriptors: o,
  descriptorStatus: r,
  onChange: i
}) {
  if (r === "loading")
    return /* @__PURE__ */ u.jsx("p", { className: "wf-muted", children: "Loading activity properties..." });
  if (!t)
    return /* @__PURE__ */ u.jsx("p", { className: "wf-muted", children: "No activity descriptor is available for this activity." });
  const s = t.inputs.filter((a) => a.isBrowsable !== !1).sort((a, d) => (a.order ?? 0) - (d.order ?? 0) || a.name.localeCompare(d.name));
  if (s.length === 0)
    return /* @__PURE__ */ u.jsx("p", { className: "wf-muted", children: "This activity does not expose editable properties." });
  const c = $y(s), l = o.length > 0 ? o : Ny;
  return /* @__PURE__ */ u.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ u.jsx("span", { className: "wf-section-label", children: "Properties" }),
    c.map((a) => /* @__PURE__ */ u.jsxs("section", { className: "wf-property-group", children: [
      c.length > 1 ? /* @__PURE__ */ u.jsx("h4", { children: a.category }) : null,
      a.inputs.map((d) => /* @__PURE__ */ u.jsx(
        Dy,
        {
          activity: e,
          input: d,
          editors: n,
          expressionDescriptors: l,
          onChange: i
        },
        d.name
      ))
    ] }, a.category))
  ] });
}
function Dy({
  activity: e,
  input: t,
  editors: n,
  expressionDescriptors: o,
  onChange: r
}) {
  const i = t.isReadOnly === !0, s = { activity: e, expressionDescriptors: o, readOnly: i }, c = Py(n, t, s), l = c?.component, a = t.isWrapped !== !1 ? sl(e, t) : null, d = a?.expression.type ?? "Literal", f = Iy(e, t), h = !!(a && zy(t, c?.id)), p = (x) => {
    const v = a ? Cy(a, x) : x;
    r(Os(e, t, v));
  }, m = (x) => {
    a && r(Os(e, t, ky(a, x)));
  };
  return /* @__PURE__ */ u.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ u.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ u.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ u.jsx("span", { children: Ty(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ u.jsx("p", { children: t.description }) : null,
    a && !h ? /* @__PURE__ */ u.jsx(
      Fs,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: d,
        descriptors: o,
        disabled: i,
        onChange: m
      }
    ) : null,
    h ? /* @__PURE__ */ u.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ u.jsx("div", { className: "wf-expression-editor", children: Bs(l, t, f, i, s, p) }),
      /* @__PURE__ */ u.jsx(
        Fs,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: d,
          descriptors: o,
          disabled: i,
          variant: "inline",
          onChange: m
        }
      )
    ] }) : Bs(l, t, f, i, s, p)
  ] });
}
function Bs(e, t, n, o, r, i) {
  return e ? /* @__PURE__ */ u.jsx(
    e,
    {
      descriptor: t,
      value: n,
      disabled: o,
      context: r,
      onChange: i
    }
  ) : /* @__PURE__ */ u.jsx("input", { type: "text", value: n == null ? "" : String(n), disabled: o, onChange: (s) => i(s.target.value) });
}
function Fs({
  label: e,
  value: t,
  descriptors: n,
  disabled: o,
  variant: r = "block",
  onChange: i
}) {
  const [s, c] = G(!1), l = yl(), a = n.find((f) => f.type === t), d = [
    "wf-syntax-picker-trigger",
    r === "inline" ? "inline" : "",
    s ? "open" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ u.jsxs("div", { className: r === "inline" ? "wf-syntax-picker inline" : "wf-syntax-picker", onBlur: (f) => {
    f.currentTarget.contains(f.relatedTarget) || c(!1);
  }, children: [
    /* @__PURE__ */ u.jsx(
      "button",
      {
        type: "button",
        className: d,
        "aria-label": e,
        "aria-haspopup": "listbox",
        "aria-expanded": s,
        "aria-controls": l,
        disabled: o,
        onClick: () => c((f) => !f),
        children: /* @__PURE__ */ u.jsx("span", { children: a?.displayName || a?.type || t })
      }
    ),
    s ? /* @__PURE__ */ u.jsx("div", { id: l, role: "listbox", className: "wf-syntax-picker-menu", "aria-label": e, children: n.map((f) => {
      const h = f.displayName || f.type, p = f.type === t;
      return /* @__PURE__ */ u.jsx(
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
function Py(e, t, n) {
  return [...e].sort((o, r) => (o.order ?? 500) - (r.order ?? 500)).find((o) => o.supports(t, n));
}
function $y(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "General";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return [...t.entries()].map(([n, o]) => ({ category: n, inputs: o }));
}
function Ty(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function zy(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !Ay.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
const Ry = { workflowActivity: d0 }, Ly = { workflow: p0 }, Ws = "application/x-elsa-activity-version-id", Hy = 6, Vy = 1200, Oy = [10, 25, 50], By = 10, Xs = "elsa-studio-workflow-palette-width", Ys = "elsa-studio-workflow-inspector-width", qs = "elsa-studio-workflow-palette-collapsed", Zs = "elsa-studio-workflow-inspector-collapsed", cl = "elsa-studio-workflow-side-panel-maximized", an = 180, cn = 460, Fy = 260, ln = 260, un = 560, Wy = 320, Ks = 42, uo = 16, ll = ut.createContext(null);
function b0(e) {
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
        component: () => /* @__PURE__ */ u.jsx(Xy, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), workflowDesignerPanels: e.workflowDesigner.panels.list() })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ u.jsx(Yy, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow instances",
        component: () => /* @__PURE__ */ u.jsx(qy, { ai: e.ai })
      }
    ]
  });
}
function Xy({
  context: e,
  ai: t,
  propertyEditors: n,
  workflowDesignerPanels: o
}) {
  const [r, i] = G(Us);
  ae(() => {
    const c = () => i(Us());
    return window.addEventListener("popstate", c), () => window.removeEventListener("popstate", c);
  }, []);
  const s = (c) => {
    const l = c ? `/workflows/definitions?definition=${encodeURIComponent(c)}` : "/workflows/definitions";
    window.history.pushState({}, "", l), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return r ? /* @__PURE__ */ u.jsx(u0, { context: e, definitionId: r, ai: t, propertyEditors: n, workflowDesignerPanels: o, onBack: () => s(null) }) : /* @__PURE__ */ u.jsx(_i, { activePath: "/workflows/definitions", title: "Definitions", children: /* @__PURE__ */ u.jsx(Zy, { context: e, ai: t, onOpen: s }) });
}
function Yy({ context: e, ai: t }) {
  const [n, o] = G(Gs);
  return ae(() => {
    const r = () => o(Gs());
    return window.addEventListener("popstate", r), () => window.removeEventListener("popstate", r);
  }, []), /* @__PURE__ */ u.jsx(_i, { activePath: "/workflows/executables", title: "Executables", children: /* @__PURE__ */ u.jsx(Uy, { context: e, ai: t, definitionFilter: n }) });
}
function qy({ ai: e }) {
  const t = Bt(e, "weaver.workflows.explain-instance");
  return /* @__PURE__ */ u.jsx(_i, { activePath: "/workflows/instances", title: "Instances", children: /* @__PURE__ */ u.jsxs("div", { className: "wf-empty", children: [
    "Workflow instance history will appear here when the runtime exposes an instance query endpoint.",
    t ? /* @__PURE__ */ u.jsxs("button", { type: "button", className: "wf-ai-inline-action", onClick: () => Ft(e, t, { scope: "workflow-instances" }), children: [
      /* @__PURE__ */ u.jsx(Ot, { size: 13 }),
      " Ask Weaver about instances"
    ] }) : null
  ] }) });
}
function _i({ activePath: e, title: t, children: n }) {
  const o = (r) => {
    window.history.pushState({}, "", r), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ u.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ u.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ u.jsxs("div", { children: [
      /* @__PURE__ */ u.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ u.jsx("h2", { children: t })
    ] }) }),
    /* @__PURE__ */ u.jsxs("nav", { className: "wf-section-tabs", "aria-label": "Workflow views", children: [
      /* @__PURE__ */ u.jsx("a", { className: e === "/workflows/definitions" ? "active" : "", href: "/workflows/definitions", onClick: (r) => {
        r.preventDefault(), o("/workflows/definitions");
      }, children: "Definitions" }),
      /* @__PURE__ */ u.jsx("a", { className: e === "/workflows/executables" ? "active" : "", href: "/workflows/executables", onClick: (r) => {
        r.preventDefault(), o("/workflows/executables");
      }, children: "Executables" }),
      /* @__PURE__ */ u.jsx("a", { className: e === "/workflows/instances" ? "active" : "", href: "/workflows/instances", onClick: (r) => {
        r.preventDefault(), o("/workflows/instances");
      }, children: "Instances" })
    ] }),
    n
  ] });
}
function Us() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Gs() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Zy({ context: e, ai: t, onOpen: n }) {
  const [o, r] = G(""), [i, s] = G("active"), [c, l] = G(1), [a, d] = G(By), [f, h] = G("loading"), [p, m] = G(""), [x, v] = G(""), [y, N] = G([]), [g, w] = G(0), [_, E] = G(() => /* @__PURE__ */ new Set()), [k, D] = G(null), [$, W] = G(!1), [M, z] = G([]), [H, b] = G("idle"), I = ce(null), C = pe(() => y.map((L) => L.id), [y]), A = Bt(t, "weaver.workflows.suggest-create-metadata"), T = Bt(t, "weaver.workflows.explain-definition"), P = C.filter((L) => _.has(L)).length, V = C.length > 0 && P === C.length, B = he(async () => {
    h("loading"), m("");
    try {
      const L = await Om(e, { search: o, state: i, page: c, pageSize: a }), J = typeof L.totalCount == "number", fe = L.totalCount ?? L.definitions.length, xe = ul(fe, a);
      if (fe > 0 && c > xe) {
        l(xe);
        return;
      }
      N(J ? L.definitions : Qy(L.definitions, c, a)), w(fe), h("ready");
    } catch (L) {
      m(L instanceof Error ? L.message : String(L)), h("failed");
    }
  }, [e, o, i, c, a]);
  ae(() => {
    B();
  }, [B]), ae(() => {
    I.current && (I.current.indeterminate = P > 0 && !V);
  }, [V, P]);
  const O = he(async () => {
    if (!(H === "loading" || H === "ready")) {
      b("loading");
      try {
        const L = await Qc(e);
        z(L.activities ?? []), b("ready");
      } catch (L) {
        b("failed"), m(L instanceof Error ? L.message : String(L));
      }
    }
  }, [H, e]), U = () => {
    m(""), v(""), D({ name: "", description: "", rootKind: "flowchart" }), O();
  }, Z = async () => {
    if (k?.name.trim()) {
      W(!0), m(""), v("");
      try {
        const L = await Fm(e, {
          name: k.name.trim(),
          description: k.description.trim() || null,
          rootKind: k.rootKind,
          rootActivityVersionId: t0(k, M)
        });
        D(null), n(L.definition.id);
      } catch (L) {
        m(L instanceof Error ? L.message : String(L));
      } finally {
        W(!1);
      }
    }
  }, ne = (L) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(L)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, se = async () => {
    if (y.length === 1 && c > 1) {
      l(c - 1);
      return;
    }
    await B();
  }, K = () => E(/* @__PURE__ */ new Set()), R = (L, J) => {
    E((fe) => {
      const xe = new Set(fe);
      return J ? xe.add(L) : xe.delete(L), xe;
    });
  }, Y = (L) => {
    E((J) => {
      const fe = new Set(J);
      for (const xe of C)
        L ? fe.add(xe) : fe.delete(xe);
      return fe;
    });
  }, re = (L) => {
    s(L), l(1), K();
  }, ie = (L) => {
    r(L), l(1), K();
  }, q = async (L) => {
    if (window.confirm(`Delete workflow definition "${L.name}"? You can restore it from the Deleted view.`)) {
      v(""), m("");
      try {
        await Wm(e, L.id), R(L.id, !1), v(`Deleted ${L.name}`), await se();
      } catch (J) {
        m(J instanceof Error ? J.message : String(J));
      }
    }
  }, ee = async (L) => {
    v(""), m("");
    try {
      await Xm(e, L.id), R(L.id, !1), v(`Restored ${L.name}`), await se();
    } catch (J) {
      m(J instanceof Error ? J.message : String(J));
    }
  }, oe = async (L) => {
    if (window.confirm(`Permanently delete workflow definition "${L.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`)) {
      v(""), m("");
      try {
        await Ym(e, L.id), R(L.id, !1), v(`Permanently deleted ${L.name}`), await se();
      } catch (J) {
        m(J instanceof Error ? J.message : String(J));
      }
    }
  };
  return /* @__PURE__ */ u.jsxs(u.Fragment, { children: [
    /* @__PURE__ */ u.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ u.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ u.jsx("button", { type: "button", className: i === "active" ? "active" : "", "aria-selected": i === "active", onClick: () => re("active"), children: "Active" }),
        /* @__PURE__ */ u.jsx("button", { type: "button", className: i === "deleted" ? "active" : "", "aria-selected": i === "deleted", onClick: () => re("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ u.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ u.jsx(Lm, { size: 15 }),
        /* @__PURE__ */ u.jsx("input", { value: o, onChange: (L) => ie(L.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ u.jsx("button", { type: "button", onClick: () => {
        B();
      }, children: "Refresh" }),
      /* @__PURE__ */ u.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ u.jsxs("button", { type: "button", title: "Create workflow", onClick: U, children: [
        /* @__PURE__ */ u.jsx(Si, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ u.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ u.jsx(kn, { size: 16 }),
      " ",
      p
    ] }) : null,
    f !== "failed" && p ? /* @__PURE__ */ u.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ u.jsx(kn, { size: 16 }),
      " ",
      p
    ] }) : null,
    x ? /* @__PURE__ */ u.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ u.jsx(Yo, { size: 14 }),
      " ",
      x
    ] }) : null,
    _.size > 0 ? /* @__PURE__ */ u.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ u.jsxs("span", { children: [
        _.size,
        " selected"
      ] }),
      /* @__PURE__ */ u.jsx("button", { type: "button", onClick: K, children: "Clear selection" })
    ] }) : null,
    f === "loading" ? /* @__PURE__ */ u.jsx("div", { className: "wf-empty", children: "Loading workflow definitions..." }) : null,
    f === "ready" && y.length === 0 ? /* @__PURE__ */ u.jsxs("div", { className: "wf-empty", children: [
      "No ",
      i,
      " workflow definitions found."
    ] }) : null,
    f === "ready" && y.length > 0 ? /* @__PURE__ */ u.jsxs(u.Fragment, { children: [
      /* @__PURE__ */ u.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ u.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ u.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ u.jsx(
            "input",
            {
              ref: I,
              type: "checkbox",
              checked: V,
              onChange: (L) => Y(L.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ u.jsx("span", { children: "Name" }),
          /* @__PURE__ */ u.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ u.jsx("span", { children: i === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ u.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ u.jsx("span", { children: "Actions" })
        ] }),
        y.map((L) => /* @__PURE__ */ u.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${L.name}`,
            "aria-selected": _.has(L.id),
            tabIndex: 0,
            onClick: () => n(L.id),
            onKeyDown: (J) => {
              J.currentTarget === J.target && (J.key !== "Enter" && J.key !== " " || (J.preventDefault(), n(L.id)));
            },
            children: [
              /* @__PURE__ */ u.jsx("label", { className: "wf-row-select", onClick: (J) => J.stopPropagation(), children: /* @__PURE__ */ u.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: _.has(L.id),
                  onChange: (J) => R(L.id, J.target.checked),
                  "aria-label": `Select workflow definition ${L.name}`
                }
              ) }),
              /* @__PURE__ */ u.jsxs("span", { children: [
                /* @__PURE__ */ u.jsx("strong", { children: L.name }),
                /* @__PURE__ */ u.jsx("small", { children: L.description || L.id })
              ] }),
              /* @__PURE__ */ u.jsx("span", { children: L.latestVersion ?? "No version" }),
              /* @__PURE__ */ u.jsx("span", { children: i === "deleted" ? ei(L.deletedAt) : L.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ u.jsx("span", { children: ei(L.lastModifiedAt) }),
              /* @__PURE__ */ u.jsx("span", { className: "wf-row-actions", onClick: (J) => J.stopPropagation(), children: i === "active" ? /* @__PURE__ */ u.jsxs(u.Fragment, { children: [
                /* @__PURE__ */ u.jsx("button", { type: "button", onClick: (J) => {
                  J.stopPropagation(), n(L.id);
                }, children: "Open" }),
                /* @__PURE__ */ u.jsx("button", { type: "button", onClick: (J) => {
                  J.stopPropagation(), ne(L.id);
                }, children: "Artifacts" }),
                T ? /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => Ft(t, T, L), children: [
                  /* @__PURE__ */ u.jsx(Ot, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ u.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  q(L);
                }, children: [
                  /* @__PURE__ */ u.jsx(Qr, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ u.jsxs(u.Fragment, { children: [
                /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => {
                  ee(L);
                }, children: [
                  /* @__PURE__ */ u.jsx(zm, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ u.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  oe(L);
                }, children: [
                  /* @__PURE__ */ u.jsx(Qr, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          L.id
        ))
      ] }),
      /* @__PURE__ */ u.jsx(
        Gy,
        {
          page: c,
          pageSize: a,
          totalCount: g,
          onPageChange: l,
          onPageSizeChange: (L) => {
            d(L), l(1);
          }
        }
      )
    ] }) : null,
    k ? /* @__PURE__ */ u.jsx(
      Ky,
      {
        draft: k,
        activities: M,
        catalogState: H,
        creating: $,
        suggestMetadataAction: A,
        onSuggestMetadata: A ? () => Ft(t, A, { draft: k, activities: M }) : void 0,
        onChange: (L) => D(L),
        onClose: () => D(null),
        onSubmit: Z
      }
    ) : null
  ] });
}
function Ky({ draft: e, activities: t, catalogState: n, creating: o, suggestMetadataAction: r, onSuggestMetadata: i, onChange: s, onClose: c, onSubmit: l }) {
  const a = pe(() => Jy(t), [t]), d = e0(e, t), f = (h) => {
    if (h.startsWith("kind:")) {
      s({ ...e, rootKind: h.slice(5), rootActivityVersionId: null });
      return;
    }
    const p = t.find((m) => m.activityVersionId === h);
    s({
      ...e,
      rootKind: dl(p) ?? e.rootKind,
      rootActivityVersionId: h
    });
  };
  return /* @__PURE__ */ u.jsx("div", { className: "wf-dialog-backdrop", role: "presentation", children: /* @__PURE__ */ u.jsx("section", { className: "wf-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "workflow-create-title", children: /* @__PURE__ */ u.jsxs(
    "form",
    {
      onSubmit: (h) => {
        h.preventDefault(), l();
      },
      children: [
        /* @__PURE__ */ u.jsxs("div", { className: "wf-dialog-heading", children: [
          /* @__PURE__ */ u.jsx("h3", { id: "workflow-create-title", children: "Create Workflow" }),
          r ? /* @__PURE__ */ u.jsxs("button", { type: "button", className: "wf-ai-action", onClick: i, title: r.description ?? r.label, children: [
            /* @__PURE__ */ u.jsx(Ot, { size: 13 }),
            " ",
            r.label
          ] }) : null
        ] }),
        /* @__PURE__ */ u.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ u.jsx("span", { children: "Display name" }),
          /* @__PURE__ */ u.jsx(
            "input",
            {
              autoFocus: !0,
              "aria-label": "Display name",
              value: e.name,
              onChange: (h) => s({ ...e, name: h.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ u.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ u.jsx("span", { children: "Description" }),
          /* @__PURE__ */ u.jsx(
            "textarea",
            {
              "aria-label": "Description",
              rows: 3,
              value: e.description,
              onChange: (h) => s({ ...e, description: h.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ u.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ u.jsx("span", { children: "Root activity" }),
          /* @__PURE__ */ u.jsxs(
            "select",
            {
              "aria-label": "Root activity",
              value: d,
              onChange: (h) => f(h.target.value),
              disabled: n === "loading",
              children: [
                /* @__PURE__ */ u.jsx("optgroup", { label: "Composite roots", children: a.compositeRoots.map((h) => /* @__PURE__ */ u.jsx("option", { value: h.value, children: h.label }, h.value)) }),
                a.otherCategories.map((h) => /* @__PURE__ */ u.jsx("optgroup", { label: h.name, children: h.activities.map((p) => /* @__PURE__ */ u.jsx("option", { value: p.activityVersionId, children: _e(p) }, p.activityVersionId)) }, h.name))
              ]
            }
          )
        ] }),
        n === "loading" ? /* @__PURE__ */ u.jsx("div", { className: "wf-dialog-note", children: "Loading activity catalog..." }) : null,
        n === "failed" ? /* @__PURE__ */ u.jsx("div", { className: "wf-dialog-note", children: "Activity catalog could not be loaded. Composite roots remain available." }) : null,
        /* @__PURE__ */ u.jsxs("div", { className: "wf-dialog-actions", children: [
          /* @__PURE__ */ u.jsx("button", { type: "button", onClick: c, disabled: o, children: "Cancel" }),
          /* @__PURE__ */ u.jsx("button", { type: "submit", disabled: o || !e.name.trim(), children: o ? "Creating..." : "Create" })
        ] })
      ]
    }
  ) }) });
}
function Uy({ context: e, ai: t, definitionFilter: n }) {
  const [o, r] = G("loading"), [i, s] = G(""), [c, l] = G(""), [a, d] = G([]), f = pe(
    () => n ? a.filter((x) => x.definitionId === n || x.sourceId === n) : a,
    [n, a]
  ), h = Bt(t, "weaver.workflows.explain-executable"), p = he(async () => {
    r("loading"), s("");
    try {
      d(await Um(e)), r("ready");
    } catch (x) {
      s(x instanceof Error ? x.message : String(x)), r("failed");
    }
  }, [e]);
  ae(() => {
    p();
  }, [p]);
  const m = async (x) => {
    l(""), s("");
    try {
      await Gc(e, x.artifactId), l(`Started ${x.artifactId}`);
    } catch (v) {
      s(v instanceof Error ? v.message : String(v));
    }
  };
  return /* @__PURE__ */ u.jsxs(u.Fragment, { children: [
    /* @__PURE__ */ u.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ u.jsx("button", { type: "button", onClick: () => {
        p();
      }, children: "Refresh" }),
      n ? /* @__PURE__ */ u.jsxs("span", { className: "wf-filter-chip", children: [
        "Definition ",
        n
      ] }) : null
    ] }),
    o === "failed" ? /* @__PURE__ */ u.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ u.jsx(kn, { size: 16 }),
      " ",
      i
    ] }) : null,
    c ? /* @__PURE__ */ u.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ u.jsx(Yo, { size: 14 }),
      " ",
      c
    ] }) : null,
    o === "loading" ? /* @__PURE__ */ u.jsx("div", { className: "wf-empty", children: "Loading workflow executables..." }) : null,
    o === "ready" && f.length === 0 ? /* @__PURE__ */ u.jsx("div", { className: "wf-empty", children: n ? "No workflow executables found for this definition." : "No workflow executables found. Publish a workflow definition to create one." }) : null,
    o === "ready" && f.length > 0 ? /* @__PURE__ */ u.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ u.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ u.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ u.jsx("span", { children: "Version" }),
        /* @__PURE__ */ u.jsx("span", { children: "Source" }),
        /* @__PURE__ */ u.jsx("span", { children: "Root" }),
        /* @__PURE__ */ u.jsx("span", { children: "Published" }),
        /* @__PURE__ */ u.jsx("span", { children: "Actions" })
      ] }),
      f.map((x) => /* @__PURE__ */ u.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ u.jsxs("span", { children: [
          /* @__PURE__ */ u.jsx("strong", { children: x.artifactId }),
          /* @__PURE__ */ u.jsx("small", { children: x.artifactHash })
        ] }),
        /* @__PURE__ */ u.jsx("span", { children: x.artifactVersion }),
        /* @__PURE__ */ u.jsx("span", { children: o0(x) }),
        /* @__PURE__ */ u.jsx("span", { children: r0(x) }),
        /* @__PURE__ */ u.jsx("span", { children: ei(x.publishedAt ?? x.createdAt) }),
        /* @__PURE__ */ u.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => {
            m(x);
          }, children: [
            /* @__PURE__ */ u.jsx(bi, { size: 13 }),
            " Run"
          ] }),
          h ? /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => Ft(t, h, x), children: [
            /* @__PURE__ */ u.jsx(Ot, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, x.artifactId))
    ] }) : null
  ] });
}
function Gy({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const i = ul(n, t), s = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
  return /* @__PURE__ */ u.jsxs("div", { className: "wf-pagination", "aria-label": "Workflow definition pagination", children: [
    /* @__PURE__ */ u.jsxs("span", { className: "wf-pagination-summary", "aria-live": "polite", children: [
      "Showing ",
      s,
      "-",
      c,
      " of ",
      n
    ] }),
    /* @__PURE__ */ u.jsxs("label", { className: "wf-page-size", children: [
      "Rows",
      /* @__PURE__ */ u.jsx("select", { value: t, onChange: (l) => r(Number(l.target.value)), children: Oy.map((l) => /* @__PURE__ */ u.jsx("option", { value: l, children: l }, l)) })
    ] }),
    /* @__PURE__ */ u.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ u.jsx(Gr, { size: 14 }),
        " Previous"
      ] }),
      /* @__PURE__ */ u.jsxs("span", { children: [
        "Page ",
        e,
        " of ",
        i
      ] }),
      /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => o(e + 1), disabled: e >= i, "aria-label": "Next page", title: "Next page", children: [
        "Next ",
        /* @__PURE__ */ u.jsx(It, { size: 14 })
      ] })
    ] })
  ] });
}
function Qy(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function ul(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Bt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function Ft(e, t, n) {
  const o = t.createPrompt(n);
  o && e.dispatchPrompt(o);
}
function Jy(e) {
  const t = Po(e, "flowchart"), n = Po(e, "sequence"), o = [
    { value: t?.activityVersionId ?? "kind:flowchart", label: "Flowchart" },
    { value: n?.activityVersionId ?? "kind:sequence", label: "Sequence" }
  ], r = /* @__PURE__ */ new Map();
  for (const s of e.filter(gl)) {
    if (n0(s)) continue;
    const c = s.category || "Uncategorized";
    r.set(c, [...r.get(c) ?? [], s]);
  }
  const i = Array.from(r.entries()).sort(([s], [c]) => s.localeCompare(c)).map(([s, c]) => ({
    name: s,
    activities: c.sort((l, a) => _e(l).localeCompare(_e(a)))
  }));
  return { compositeRoots: o, otherCategories: i };
}
function e0(e, t) {
  return e.rootActivityVersionId ?? Po(t, e.rootKind)?.activityVersionId ?? `kind:${e.rootKind}`;
}
function t0(e, t) {
  return e.rootActivityVersionId ?? Po(t, e.rootKind)?.activityVersionId ?? null;
}
function Po(e, t) {
  return e.find((n) => dl(n) === t);
}
function dl(e) {
  return e ? hl(e) ? "flowchart" : pl(e) ? "sequence" : null : null;
}
function fl(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "Uncategorized";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [o]) => n.localeCompare(o)).map(([n, o]) => ({
    category: n,
    activities: o.sort((r, i) => _e(r).localeCompare(_e(i)))
  }));
}
function n0(e) {
  return hl(e) || pl(e);
}
function hl(e) {
  return _e(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function pl(e) {
  return _e(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function gl(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function o0(e) {
  return e.sourceKind || e.sourceId || e.sourceVersion ? [e.sourceKind, e.sourceId, e.sourceVersion].filter(Boolean).join(" / ") : e.definitionId;
}
function r0(e) {
  return i0(e.rootActivityType) || e.rootActivityType;
}
function i0(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function s0(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    fo(t, n.typeName, n), fo(t, n.name, n), fo(t, n.displayName, n);
    const o = n.typeName.split(".").filter(Boolean).at(-1);
    fo(t, o, n);
  }
  return t;
}
function a0(e, t, n) {
  const o = t.get(e.activityVersionId);
  return n.get(gn(o?.activityTypeKey)) ?? n.get(gn(c0(o?.activityTypeKey))) ?? n.get(gn(o?.displayName)) ?? n.get(gn(e.activityVersionId)) ?? null;
}
function fo(e, t, n) {
  const o = gn(t);
  o && !e.has(o) && e.set(o, n);
}
function gn(e) {
  return e?.trim().toLowerCase() ?? "";
}
function c0(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function Qs(e, t, n, o) {
  const r = Zo();
  if (!r) return t;
  const i = Number(r.getItem(e));
  return Number.isFinite(i) ? vo(i, n, o) : t;
}
function Js(e, t) {
  const n = Zo();
  if (!n) return t;
  const o = n.getItem(e);
  return o === "true" ? !0 : o === "false" ? !1 : t;
}
function l0() {
  const e = Zo();
  if (!e) return null;
  const t = e.getItem(cl);
  return t === "palette" || t === "inspector" ? t : null;
}
function Zo() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function dn(e, t) {
  const n = Zo();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function vo(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function u0({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: o,
  workflowDesignerPanels: r,
  onBack: i
}) {
  const [s, c] = G(null), [l, a] = G(null), [d, f] = G([]), [h, p] = G([]), [m, x] = G(xo), [v, y] = G("loading"), [N, g] = G([]), [w, _] = G([]), [E, k] = G([]), [D, $] = G(null), [W, M] = G(null), [z, H] = G(null), [b, I] = G(null), [C, A] = G(""), [T, P] = G(""), [V, B] = G(!1), [O, U] = G(null), [Z, ne] = G(() => /* @__PURE__ */ new Set()), [se, K] = G(() => Qs(Xs, Fy, an, cn)), [R, Y] = G(() => Qs(Ys, Wy, ln, un)), [re, ie] = G(() => Js(qs, !1)), [q, ee] = G(() => Js(Zs, !1)), [oe, L] = G(l0), [J, fe] = G("activities"), [xe, Oe] = G("inspector"), we = ce(null), je = ce(null), tt = ce(""), Fe = ce(0), Be = ce(null), Ce = ce(!1), Ae = l?.state.rootActivity ?? null, De = pe(() => new Map(d.map((S) => [S.activityVersionId, S])), [d]), Ue = pe(() => s0(h), [h]), Te = pe(() => el(Ae, N), [Ae, N]), St = ty(Te, Te ? De.get(Te.activityVersionId) : void 0), ye = !!Te && St === "unsupported", We = pe(() => ye ? null : Tr(Ae, N), [Ae, N, ye]), Xt = pe(() => fl(d), [d]), Ee = pe(() => ye && Te?.nodeId === W ? Te : We?.slot.activities.find((S) => S.nodeId === W) ?? null, [ye, We, Te, W]), Yt = pe(
    () => Ee ? a0(Ee, De, Ue) : null,
    [De, Ue, Ee]
  ), qt = Ee ? Je(Ee) : [], ke = St === "flowchart" && We?.slot.mode === "flowchart", dt = !Ae || !ye, Rn = Bt(n, "weaver.workflows.find-draft-risks"), Ln = Bt(n, "weaver.workflows.propose-update");
  ae(() => {
    dn(Xs, String(se));
  }, [se]), ae(() => {
    dn(Ys, String(R));
  }, [R]), ae(() => {
    dn(qs, String(re));
  }, [re]), ae(() => {
    dn(Zs, String(q));
  }, [q]), ae(() => {
    dn(cl, oe);
  }, [oe]), ae(() => {
    if (!oe) return;
    const S = (j) => {
      j.key === "Escape" && L(null);
    };
    return window.addEventListener("keydown", S), () => window.removeEventListener("keydown", S);
  }, [oe]);
  const Zt = he(async () => {
    A(""), y("loading");
    const [S, j, F, X] = await Promise.all([
      Bm(e, t),
      Qc(e),
      Gm(e).then(
        (ue) => ({ ok: !0, descriptors: ue }),
        () => ({ ok: !1, descriptors: [] })
      ),
      Qm(e).then(
        (ue) => ({ ok: !0, descriptors: ue }),
        () => ({ ok: !1, descriptors: xo })
      )
    ]), Q = S.draft ?? null;
    c(S), tt.current = Q ? fn(Q) : "", a(Q), f(j.activities ?? []), p(F.descriptors), x(X.descriptors.length > 0 ? X.descriptors : xo), y(F.ok ? "ready" : "failed"), g([]), M(null);
  }, [e, t]);
  ae(() => {
    Zt().catch((S) => A(S instanceof Error ? S.message : String(S)));
  }, [Zt]), ae(() => {
    ne((S) => {
      let j = !1;
      const F = new Set(S);
      for (const X of Xt)
        F.has(X.category) || (F.add(X.category), j = !0);
      return j ? F : S;
    });
  }, [Xt]), ae(() => {
    if (!Te) {
      _([]), k([]);
      return;
    }
    const S = ye ? ey(Te, d, l?.layout ?? []) : We ? Jm(We, d, l?.layout ?? []) : { nodes: [], edges: [] };
    _(S.nodes), k(S.edges);
  }, [d, l?.layout, ye, We, Te]);
  const Ko = (S) => {
    a((j) => j && { ...j, state: { ...j.state, rootActivity: S } });
  }, Kt = he((S, j) => {
    if (l?.state.rootActivity && ye)
      return;
    const F = Hs(S, na(S));
    if (!l?.state.rootActivity) {
      Ko(F), M(F.nodeId);
      return;
    }
    if (!We) {
      if (!Je(F)[0]) {
        P(""), A("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      a((Q) => {
        if (!Q?.state.rootActivity) return Q;
        const ue = Q.state.rootActivity, de = Jr(F, [], [ue]), be = j ? [
          ...Q.layout.filter((Pe) => Pe.nodeId !== ue.nodeId),
          {
            nodeId: ue.nodeId,
            x: Math.round(j.x),
            y: Math.round(j.y)
          }
        ] : Q.layout;
        return {
          ...Q,
          layout: be,
          state: {
            ...Q.state,
            rootActivity: de
          }
        };
      }), M(l.state.rootActivity.nodeId), A(""), P(`Wrapped root in ${_e(S)}`);
      return;
    }
    a((X) => {
      if (!X?.state.rootActivity) return X;
      const Q = Tr(X.state.rootActivity, N);
      if (!Q) return X;
      const ue = Jr(X.state.rootActivity, N, [...Q.slot.activities, F]), de = j ? [
        ...X.layout.filter((be) => be.nodeId !== F.nodeId),
        {
          nodeId: F.nodeId,
          x: Math.round(j.x),
          y: Math.round(j.y)
        }
      ] : X.layout;
      return {
        ...X,
        layout: de,
        state: {
          ...X.state,
          rootActivity: ue
        }
      };
    }), M(F.nodeId);
  }, [l?.state.rootActivity, N, ye, We]), Nt = he((S, j) => {
    const F = Hs(S, na(S)), X = {
      id: F.nodeId,
      type: "workflowActivity",
      position: j,
      selected: !0,
      data: {
        label: _e(S),
        activityVersionId: S.activityVersionId,
        activityTypeKey: S.activityTypeKey,
        category: S.category,
        executionType: S.executionType,
        icon: rl(S),
        childSlots: Je(F),
        acceptsInbound: String(S.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: il(F, S)
      }
    };
    return { activityNode: F, node: X };
  }, []), ze = he((S, j, F = []) => {
    ye || a((X) => {
      if (!X) return X;
      const Q = ry(X.layout, S), ue = X.state.rootActivity;
      if (!ue) return { ...X, layout: Q };
      const de = Tr(ue, N);
      if (!de) return { ...X, layout: Q };
      const be = ny(de, S, j, F), Pe = de.slot.mode === "flowchart" ? oy(be, j) : be;
      return {
        ...X,
        layout: Q,
        state: {
          ...X.state,
          rootActivity: tl(ue, N, Pe)
        }
      };
    });
  }, [N, ye]), Ut = he((S, j) => {
    if (!we.current) return null;
    const F = we.current.getBoundingClientRect();
    return D ? D.screenToFlowPosition({ x: S, y: j }) : {
      x: S - F.left,
      y: j - F.top
    };
  }, [D]), Gt = he((S, j) => document.elementFromPoint(S, j)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), Qt = he((S, j, F) => {
    const X = w.find((Ie) => Ie.id === j.source), Q = w.find((Ie) => Ie.id === j.target), ue = X && Q ? y0(X, Q) : X ? oa(X) : F, de = Nt(S, ue), Pe = [...w.map((Ie) => Ie.selected ? { ...Ie, selected: !1 } : Ie), de.node], ft = py(E, j, de.node.id);
    _(Pe), k(ft), M(de.node.id), ze(Pe, ft, [de.activityNode]);
  }, [ze, Nt, E, w]), Et = he((S, j, F) => {
    if (!dt || !we.current) return !1;
    const X = we.current.getBoundingClientRect();
    if (!(j >= X.left && j <= X.right && F >= X.top && F <= X.bottom)) return !1;
    const ue = Ut(j, F);
    if (!ue) return !1;
    if (ke) {
      const de = Gt(j, F), be = de ? E.find((Pe) => Pe.id === de) : void 0;
      if (be)
        return Qt(S, be, ue), !0;
    }
    return Kt(S, ue), !0;
  }, [Kt, dt, E, Gt, ke, Qt, Ut]);
  ae(() => {
    const S = (F) => {
      const X = Be.current;
      if (!X) return;
      Math.hypot(F.clientX - X.startX, F.clientY - X.startY) >= Hy && (X.dragging = !0);
    }, j = (F) => {
      const X = Be.current;
      if (Be.current = null, !X?.dragging || !we.current) return;
      const Q = we.current.getBoundingClientRect();
      F.clientX >= Q.left && F.clientX <= Q.right && F.clientY >= Q.top && F.clientY <= Q.bottom && (Ce.current = !0, window.setTimeout(() => {
        Ce.current = !1;
      }, 0), Et(X.activity, F.clientX, F.clientY));
    };
    return window.addEventListener("pointermove", S), window.addEventListener("pointerup", j), window.addEventListener("pointercancel", j), () => {
      window.removeEventListener("pointermove", S), window.removeEventListener("pointerup", j), window.removeEventListener("pointercancel", j);
    };
  }, [D, Et]);
  const Uo = (S, j) => {
    S.dataTransfer.setData(Ws, j.activityVersionId), S.dataTransfer.setData("text/plain", j.activityVersionId), S.dataTransfer.effectAllowed = "copy";
  }, Go = (S, j) => {
    S.clientX === 0 && S.clientY === 0 || Et(j, S.clientX, S.clientY) && (Ce.current = !0, window.setTimeout(() => {
      Ce.current = !1;
    }, 0));
  }, Qo = (S, j) => {
    S.button === 0 && (Be.current = {
      activity: j,
      startX: S.clientX,
      startY: S.clientY,
      dragging: !1
    });
  }, Jo = (S) => {
    Ce.current || dt && Kt(S);
  }, Hn = (S) => {
    if (!dt) {
      S.dataTransfer.dropEffect = "none";
      return;
    }
    if (S.preventDefault(), S.dataTransfer.dropEffect = "copy", !ke) return;
    const j = Gt(S.clientX, S.clientY);
    I(j);
  }, Vn = (S) => {
    if (!we.current) return;
    const j = S.relatedTarget;
    j && we.current.contains(j) || I(null);
  }, On = (S) => {
    if (S.preventDefault(), I(null), !dt) return;
    const j = S.dataTransfer.getData(Ws) || S.dataTransfer.getData("text/plain"), F = De.get(j);
    F && Et(F, S.clientX, S.clientY);
  }, er = () => {
    if (!ke) return;
    const S = we.current?.getBoundingClientRect();
    S && H({
      kind: "fromEmpty",
      clientX: S.left + S.width / 2,
      clientY: S.top + S.height / 2
    });
  }, Jt = he(async (S, j) => {
    const F = ++Fe.current, X = fn(S);
    A("");
    try {
      const Q = await qm(e, S), ue = fn(Q);
      tt.current = ue, a((de) => !de || de.id !== Q.id ? de : fn(de) === X ? Q : { ...de, validationErrors: Q.validationErrors }), F === Fe.current && P(j);
    } catch (Q) {
      F === Fe.current && (P(""), A(Q instanceof Error ? Q.message : String(Q)));
    }
  }, [e]);
  ae(() => {
    if (!V || !l || fn(l) === tt.current) return;
    P("Autosaving...");
    const j = window.setTimeout(() => {
      Jt(l, "Autosaved");
    }, Vy);
    return () => window.clearTimeout(j);
  }, [V, l, Jt]);
  const tr = async () => {
    l && (P("Saving..."), await Jt(l, "Saved"));
  }, nr = async () => {
    if (l) {
      P("Promoting...");
      try {
        const S = await Zm(e, l.id), j = await Km(e, S.versionId);
        U(j.artifactId), P(`Published ${j.artifactVersion}`), await Zt();
      } catch (S) {
        P(""), A(S instanceof Error ? S.message : String(S));
      }
    }
  }, Bn = async () => {
    if (O) {
      P("Running...");
      try {
        await Gc(e, O), P("Run dispatched");
      } catch (S) {
        P(""), A(S instanceof Error ? S.message : String(S));
      }
    }
  }, Fn = (S) => {
    const j = ye ? S.filter((F) => F.type === "select") : S;
    j.length !== 0 && _((F) => mc(j, F));
  }, Wn = (S) => {
    ye || k((j) => yc(S, j));
  }, en = (S) => !S.source || !S.target || S.source === S.target || !ke ? !1 : !S.targetHandle, or = (S) => {
    if (!l?.state.rootActivity || !We || !ke || !en(S)) return;
    const j = Do(S.source, S.target, S.sourceHandle ?? "Done", S.targetHandle ?? void 0), F = wc(j, E);
    k(F), ze(w, F);
  }, rr = () => {
    ze(w, E);
  }, ir = (S, j) => {
    if (!j.nodeId || j.handleType === "target") {
      je.current = null;
      return;
    }
    je.current = {
      nodeId: j.nodeId,
      handleId: j.handleId ?? null
    };
  }, sr = (S) => {
    const j = je.current;
    if (je.current = null, !j || !ke || S.target?.closest(".react-flow__handle, .react-flow__node")) return;
    const X = x0(S);
    H({
      kind: "fromPort",
      sourceNodeId: j.nodeId,
      sourceHandleId: j.handleId,
      clientX: X.x,
      clientY: X.y
    });
  }, ar = (S, j) => {
    if (!ke || !en(j)) return;
    const F = Pp(S, {
      ...j,
      sourceHandle: j.sourceHandle ?? "Done",
      targetHandle: j.targetHandle ?? void 0
    }, E, { shouldReplaceId: !1 });
    k(F), ze(w, F);
  }, Xn = (S) => {
    if (ye || S.length === 0) return;
    const j = new Set(S.map((Q) => Q.id)), F = w.filter((Q) => !j.has(Q.id)), X = E.filter((Q) => !j.has(Q.source) && !j.has(Q.target));
    _(F), k(X), W && j.has(W) && M(null), ze(F, X);
  }, cr = (S) => {
    if (ye || S.length === 0) return;
    const j = new Set(S.map((X) => X.id)), F = E.filter((X) => !j.has(X.id));
    k(F), ze(w, F);
  }, Yn = he((S) => {
    if (ye) return;
    const j = E.filter((F) => F.id !== S);
    k(j), ze(w, j);
  }, [ze, E, ye, w]), qn = he((S, j, F) => {
    ke && H({ kind: "spliceEdge", edgeId: S, clientX: j, clientY: F });
  }, [ke]), lr = (S) => {
    const j = z;
    if (!j) return;
    H(null);
    const F = Ut(j.clientX, j.clientY) ?? { x: 0, y: 0 };
    if (j.kind === "fromEmpty") {
      const Q = Nt(S, F), de = [...w.map((be) => be.selected ? { ...be, selected: !1 } : be), Q.node];
      _(de), M(Q.node.id), ze(de, E, [Q.activityNode]);
      return;
    }
    if (j.kind === "fromPort") {
      const Q = w.find((Ie) => Ie.id === j.sourceNodeId), ue = Q ? oa(Q) : F, de = Nt(S, ue), Pe = [...w.map((Ie) => Ie.selected ? { ...Ie, selected: !1 } : Ie), de.node], ft = [...E, Do(j.sourceNodeId, de.node.id, j.sourceHandleId ?? "Done")];
      _(Pe), k(ft), M(de.node.id), ze(Pe, ft, [de.activityNode]);
      return;
    }
    const X = E.find((Q) => Q.id === j.edgeId);
    X && Qt(S, X, F);
  }, ur = pe(() => ({
    highlightedEdgeId: b,
    deleteEdge: Yn,
    requestInsertActivity: qn
  }), [Yn, b, qn]), dr = (S, j, F) => {
    g((X) => [...X, { ownerNodeId: S.nodeId, slotId: j, label: F }]), M(null);
  }, fr = he((S) => {
    a((j) => {
      const F = j?.state.rootActivity;
      return !j || !F ? j : {
        ...j,
        state: {
          ...j.state,
          rootActivity: nl(F, S.nodeId, () => S)
        }
      };
    });
  }, []), hr = (S) => {
    ne((j) => {
      const F = new Set(j);
      return F.has(S) ? F.delete(S) : F.add(S), F;
    });
  }, tn = (S) => {
    L((j) => j === S ? null : j), S === "palette" ? ie((j) => !j) : ee((j) => !j);
  }, Zn = (S) => {
    S === "palette" ? ie(!1) : ee(!1), L((j) => j === S ? null : S);
  }, Kn = (S, j) => {
    L(null), S === "palette" ? (ie(!1), K((F) => vo(F + j, an, cn))) : (ee(!1), Y((F) => vo(F + j, ln, un)));
  }, Un = (S, j) => {
    j.preventDefault(), L(null), S === "palette" ? ie(!1) : ee(!1);
    const F = j.clientX, X = S === "palette" ? se : R, Q = S === "palette" ? an : ln, ue = S === "palette" ? cn : un;
    document.body.classList.add("wf-side-panel-resizing");
    const de = (Pe) => {
      const ft = S === "palette" ? Pe.clientX - F : F - Pe.clientX, Ie = vo(X + ft, Q, ue);
      S === "palette" ? K(Ie) : Y(Ie);
    }, be = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", de), window.removeEventListener("pointerup", be), window.removeEventListener("pointercancel", be);
    };
    window.addEventListener("pointermove", de), window.addEventListener("pointerup", be), window.addEventListener("pointercancel", be);
  }, Gn = (S, j) => {
    j.key === "ArrowLeft" ? (j.preventDefault(), Kn(S, S === "palette" ? -uo : uo)) : j.key === "ArrowRight" ? (j.preventDefault(), Kn(S, S === "palette" ? uo : -uo)) : j.key === "Home" ? (j.preventDefault(), S === "palette" ? K(an) : Y(ln)) : j.key === "End" && (j.preventDefault(), S === "palette" ? K(cn) : Y(un));
  };
  if (!s || !l)
    return /* @__PURE__ */ u.jsx("div", { className: "wf-empty", children: C || "Loading workflow editor..." });
  const pr = [
    "wf-editor-body",
    re ? "palette-collapsed" : "",
    q ? "inspector-collapsed" : "",
    oe === "palette" ? "palette-maximized" : "",
    oe === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), gr = {
    "--wf-palette-width": `${re ? Ks : se}px`,
    "--wf-inspector-width": `${q ? Ks : R}px`
  }, mr = !re && oe !== "inspector", yr = !q && oe !== "palette", Qn = {
    definition: s.definition,
    draft: l,
    selectedActivity: Ee,
    selectedActivityDescriptor: Yt,
    selectedActivitySlots: qt,
    catalog: d,
    currentScopeOwner: Te,
    frames: N
  }, Jn = r.map((S) => {
    const j = S.component;
    return {
      id: S.id,
      title: S.title,
      side: S.side,
      order: S.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ u.jsx(j, { context: Qn })
    };
  }), _t = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ u.jsx(Zc, { size: 15 }),
      render: xr
    },
    ...Jn.filter((S) => S.side === "left")
  ].sort(ta), nn = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ u.jsx(Uc, { size: 15 }),
      render: wr
    },
    ...Jn.filter((S) => S.side === "right")
  ].sort(ta), eo = _t.find((S) => S.id === J) ?? _t[0], Ct = nn.find((S) => S.id === xe) ?? nn[0];
  function xr() {
    return /* @__PURE__ */ u.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: Xt.map((S) => {
      const j = Z.has(S.category);
      return /* @__PURE__ */ u.jsxs("div", { className: "wf-palette-category", children: [
        /* @__PURE__ */ u.jsxs(
          "button",
          {
            type: "button",
            className: "wf-palette-category-toggle",
            role: "treeitem",
            "aria-expanded": j,
            onClick: () => hr(S.category),
            children: [
              j ? /* @__PURE__ */ u.jsx(Tm, { size: 14 }) : /* @__PURE__ */ u.jsx(It, { size: 14 }),
              /* @__PURE__ */ u.jsx("span", { children: S.category }),
              /* @__PURE__ */ u.jsx("small", { children: S.activities.length })
            ]
          }
        ),
        j ? /* @__PURE__ */ u.jsx("div", { className: "wf-palette-activities", role: "group", children: S.activities.map((F) => {
          const X = F.description?.trim(), Q = X ? `wf-palette-description-${F.activityVersionId}` : void 0;
          return /* @__PURE__ */ u.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-activity",
              role: "treeitem",
              draggable: !0,
              title: X || _e(F),
              "aria-describedby": Q,
              onClick: () => Jo(F),
              onDragStart: (ue) => Uo(ue, F),
              onDragEnd: (ue) => Go(ue, F),
              onPointerDown: (ue) => Qo(ue, F),
              children: [
                /* @__PURE__ */ u.jsx("strong", { children: _e(F) }),
                X ? /* @__PURE__ */ u.jsx("small", { id: Q, children: X }) : null
              ]
            },
            F.activityVersionId
          );
        }) }) : null
      ] }, S.category);
    }) });
  }
  function wr() {
    return Ee ? /* @__PURE__ */ u.jsxs("div", { className: "wf-inspector-content", children: [
      /* @__PURE__ */ u.jsx("h3", { children: w.find((S) => S.id === Ee.nodeId)?.data.label ?? Ee.nodeId }),
      /* @__PURE__ */ u.jsxs("dl", { children: [
        /* @__PURE__ */ u.jsx("dt", { children: "Node ID" }),
        /* @__PURE__ */ u.jsx("dd", { children: Ee.nodeId }),
        /* @__PURE__ */ u.jsx("dt", { children: "Activity version" }),
        /* @__PURE__ */ u.jsx("dd", { children: Ee.activityVersionId }),
        /* @__PURE__ */ u.jsx("dt", { children: "Activity type" }),
        /* @__PURE__ */ u.jsx("dd", { children: Yt?.typeName ?? w.find((S) => S.id === Ee.nodeId)?.data.activityTypeKey ?? "Unknown" })
      ] }),
      /* @__PURE__ */ u.jsx(
        jy,
        {
          activity: Ee,
          descriptor: Yt,
          editors: o,
          expressionDescriptors: m,
          descriptorStatus: v,
          onChange: fr
        }
      ),
      qt.length > 0 ? /* @__PURE__ */ u.jsxs("div", { className: "wf-slot-list", children: [
        /* @__PURE__ */ u.jsx("span", { children: "Embedded slots" }),
        qt.map((S) => /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => dr(Ee, S.id, `${w.find((j) => j.id === Ee.nodeId)?.data.label ?? Ee.nodeId} / ${S.label}`), children: [
          S.label,
          /* @__PURE__ */ u.jsxs("small", { children: [
            S.activities.length,
            " activit",
            S.activities.length === 1 ? "y" : "ies"
          ] })
        ] }, S.id))
      ] }) : /* @__PURE__ */ u.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
    ] }) : /* @__PURE__ */ u.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
  }
  return /* @__PURE__ */ u.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ u.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ u.jsx("button", { type: "button", className: "wf-link-button", onClick: i, children: "Definitions" }),
      /* @__PURE__ */ u.jsx(It, { size: 14 }),
      /* @__PURE__ */ u.jsx("strong", { children: s.definition.name }),
      /* @__PURE__ */ u.jsx("span", { className: "wf-chip", children: "Draft" }),
      T ? /* @__PURE__ */ u.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ u.jsx(Yo, { size: 13 }),
        " ",
        T
      ] }) : null,
      /* @__PURE__ */ u.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ u.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ u.jsx("input", { type: "checkbox", checked: V, onChange: (S) => B(S.target.checked) }),
          /* @__PURE__ */ u.jsx("span", { children: "Autosave" })
        ] }),
        Rn ? /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => Ft(n, Rn, { definition: s.definition, draft: l }), children: [
          /* @__PURE__ */ u.jsx(Ot, { size: 15 }),
          " Risks"
        ] }) : null,
        Ln ? /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => Ft(n, Ln, { definition: s.definition, draft: l }), children: [
          /* @__PURE__ */ u.jsx(Ot, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => {
          tr();
        }, children: [
          /* @__PURE__ */ u.jsx(Rm, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => {
          nr();
        }, children: [
          /* @__PURE__ */ u.jsx(Kc, { size: 15 }),
          " Promote"
        ] }),
        /* @__PURE__ */ u.jsxs("button", { type: "button", disabled: !O, onClick: () => {
          Bn();
        }, children: [
          /* @__PURE__ */ u.jsx(bi, { size: 15 }),
          " Run"
        ] })
      ] })
    ] }),
    C ? /* @__PURE__ */ u.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ u.jsx(kn, { size: 16 }),
      " ",
      C
    ] }) : null,
    /* @__PURE__ */ u.jsxs("div", { className: pr, style: gr, children: [
      /* @__PURE__ */ u.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ u.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ u.jsx(
            ea,
            {
              label: "Activities panel tabs",
              tabs: _t,
              activeTabId: eo.id,
              onSelect: fe
            }
          ),
          /* @__PURE__ */ u.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ u.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": re ? "Expand activities panel" : "Collapse activities panel",
                title: re ? "Expand" : "Collapse",
                onClick: () => tn("palette"),
                children: re ? /* @__PURE__ */ u.jsx(It, { size: 14 }) : /* @__PURE__ */ u.jsx(Gr, { size: 14 })
              }
            ),
            re ? null : /* @__PURE__ */ u.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": oe === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: oe === "palette" ? "Restore" : "Maximize",
                onClick: () => Zn("palette"),
                children: oe === "palette" ? /* @__PURE__ */ u.jsx(Rs, { size: 14 }) : /* @__PURE__ */ u.jsx(zs, { size: 14 })
              }
            )
          ] })
        ] }),
        eo.render()
      ] }),
      mr && !oe ? /* @__PURE__ */ u.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": an,
          "aria-valuemax": cn,
          "aria-valuenow": se,
          tabIndex: 0,
          onPointerDown: (S) => Un("palette", S),
          onKeyDown: (S) => Gn("palette", S)
        }
      ) : /* @__PURE__ */ u.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ u.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ u.jsxs("div", { className: "wf-breadcrumb", children: [
          /* @__PURE__ */ u.jsx("button", { type: "button", onClick: () => {
            g([]), M(null);
          }, children: "Root" }),
          N.map((S, j) => /* @__PURE__ */ u.jsxs(ut.Fragment, { children: [
            /* @__PURE__ */ u.jsx(It, { size: 13 }),
            /* @__PURE__ */ u.jsx("button", { type: "button", onClick: () => {
              g(N.slice(0, j + 1)), M(null);
            }, children: S.label })
          ] }, `${S.ownerNodeId}-${S.slotId}-${j}`))
        ] }),
        /* @__PURE__ */ u.jsxs("div", { className: "wf-canvas", ref: we, onDragOver: Hn, onDragLeave: Vn, onDrop: On, children: [
          /* @__PURE__ */ u.jsx(ll.Provider, { value: ur, children: /* @__PURE__ */ u.jsxs(
            tm,
            {
              nodes: w,
              edges: E,
              nodeTypes: Ry,
              edgeTypes: Ly,
              onInit: $,
              onNodesChange: Fn,
              onEdgesChange: Wn,
              onNodesDelete: Xn,
              onEdgesDelete: cr,
              onConnect: or,
              onConnectStart: ke ? ir : void 0,
              onConnectEnd: ke ? sr : void 0,
              onReconnect: ke ? ar : void 0,
              isValidConnection: en,
              onDragOver: Hn,
              onDragLeave: Vn,
              onDrop: On,
              onPaneClick: () => M(null),
              onNodeClick: (S, j) => M(j.id),
              onNodeDragStop: ye ? void 0 : rr,
              fitView: !0,
              minZoom: 0.2,
              maxZoom: 1.8,
              nodesConnectable: ke,
              nodesDraggable: !ye,
              selectionOnDrag: !0,
              multiSelectionKeyCode: ["Shift", "Meta", "Control"],
              deleteKeyCode: ye ? null : ["Backspace", "Delete"],
              panActivationKeyCode: null,
              defaultEdgeOptions: { type: "workflow" },
              children: [
                /* @__PURE__ */ u.jsx(cm, { gap: 18, size: 1 }),
                /* @__PURE__ */ u.jsx(gm, {}),
                /* @__PURE__ */ u.jsx(Im, { pannable: !0, zoomable: !0 })
              ]
            }
          ) }),
          ke && w.length === 0 ? /* @__PURE__ */ u.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => er(), children: [
            /* @__PURE__ */ u.jsx(Si, { size: 15 }),
            " Add activity"
          ] }) : null,
          z ? /* @__PURE__ */ u.jsx(
            g0,
            {
              clientX: z.clientX,
              clientY: z.clientY,
              activities: d,
              onPick: lr,
              onClose: () => H(null)
            }
          ) : null
        ] }),
        /* @__PURE__ */ u.jsx(m0, { draft: l })
      ] }),
      yr && !oe ? /* @__PURE__ */ u.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": ln,
          "aria-valuemax": un,
          "aria-valuenow": R,
          tabIndex: 0,
          onPointerDown: (S) => Un("inspector", S),
          onKeyDown: (S) => Gn("inspector", S)
        }
      ) : /* @__PURE__ */ u.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ u.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ u.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ u.jsx(
            ea,
            {
              label: "Inspector panel tabs",
              tabs: nn,
              activeTabId: Ct.id,
              onSelect: Oe
            }
          ),
          /* @__PURE__ */ u.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ u.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": q ? "Expand inspector panel" : "Collapse inspector panel",
                title: q ? "Expand" : "Collapse",
                onClick: () => tn("inspector"),
                children: q ? /* @__PURE__ */ u.jsx(Gr, { size: 14 }) : /* @__PURE__ */ u.jsx(It, { size: 14 })
              }
            ),
            q ? null : /* @__PURE__ */ u.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": oe === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: oe === "inspector" ? "Restore" : "Maximize",
                onClick: () => Zn("inspector"),
                children: oe === "inspector" ? /* @__PURE__ */ u.jsx(Rs, { size: 14 }) : /* @__PURE__ */ u.jsx(zs, { size: 14 })
              }
            )
          ] })
        ] }),
        Ct.render()
      ] })
    ] })
  ] });
}
function ea({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: o
}) {
  return /* @__PURE__ */ u.jsx("div", { className: "wf-panel-tab-list", role: "tablist", "aria-label": e, children: t.map((r) => /* @__PURE__ */ u.jsxs(
    "button",
    {
      type: "button",
      role: "tab",
      "aria-selected": r.id === n,
      className: r.id === n ? "active" : "",
      title: r.title,
      onClick: () => o(r.id),
      children: [
        r.icon ? /* @__PURE__ */ u.jsx("span", { className: "wf-panel-tab-icon", "aria-hidden": "true", children: r.icon }) : null,
        /* @__PURE__ */ u.jsx("span", { children: r.title })
      ]
    },
    r.id
  )) });
}
function ta(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
function d0({ data: e, selected: t }) {
  const n = e, o = !n.suppressFlowPorts, r = o ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], i = f0(n);
  return /* @__PURE__ */ u.jsxs("div", { className: t ? "wf-node selected" : "wf-node", "data-icon": n.icon ?? "activity", children: [
    o && n.acceptsInbound ? /* @__PURE__ */ u.jsx(Vt, { type: "target", position: te.Left }) : null,
    /* @__PURE__ */ u.jsxs("div", { className: "wf-node-content", children: [
      /* @__PURE__ */ u.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: h0(n.icon) }),
      /* @__PURE__ */ u.jsxs("span", { className: "wf-node-copy", children: [
        /* @__PURE__ */ u.jsx("strong", { children: n.label }),
        i ? /* @__PURE__ */ u.jsx("small", { children: i }) : null
      ] })
    ] }),
    n.childSlots.length > 0 ? /* @__PURE__ */ u.jsxs("span", { className: "wf-node-slot-badge", children: [
      n.childSlots.length,
      " slot",
      n.childSlots.length === 1 ? "" : "s"
    ] }) : null,
    r.map((s, c) => {
      const l = `${(c + 1) / (r.length + 1) * 100}%`;
      return /* @__PURE__ */ u.jsxs(ut.Fragment, { children: [
        /* @__PURE__ */ u.jsx("span", { className: "wf-node-port-label", style: { top: l }, children: s.displayName }),
        /* @__PURE__ */ u.jsx(Vt, { type: "source", position: te.Right, id: s.name, style: { top: l } })
      ] }, s.name);
    })
  ] });
}
function f0(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((r) => !!r).join(" · ");
}
function h0(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ u.jsx(Kc, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ u.jsx(Uc, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ u.jsx(Hm, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ u.jsx(bi, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ u.jsx(Vm, { size: 15 });
    default:
      return /* @__PURE__ */ u.jsx(Zc, { size: 15 });
  }
}
function p0(e) {
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
    label: d,
    labelStyle: f
  } = e, h = ut.useContext(ll), [p, m] = G(!1), [x, v, y] = Ao({ sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: c }), N = h?.highlightedEdgeId === t;
  return /* @__PURE__ */ u.jsxs(u.Fragment, { children: [
    /* @__PURE__ */ u.jsx(
      Tn,
      {
        id: t,
        path: x,
        markerEnd: l,
        style: {
          ...a,
          strokeWidth: N ? 2.5 : a?.strokeWidth
        },
        label: d,
        labelX: v,
        labelY: y,
        labelStyle: f,
        onMouseEnter: () => m(!0),
        onMouseLeave: () => m(!1)
      }
    ),
    h ? /* @__PURE__ */ u.jsx(om, { children: /* @__PURE__ */ u.jsxs(
      "div",
      {
        className: ["wf-edge-actions", p ? "visible" : "", N ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${v}px, ${y}px)` },
        onMouseEnter: () => m(!0),
        onMouseLeave: () => m(!1),
        children: [
          /* @__PURE__ */ u.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (g) => h.requestInsertActivity(t, g.clientX, g.clientY), children: /* @__PURE__ */ u.jsx(Si, { size: 12 }) }),
          /* @__PURE__ */ u.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => h.deleteEdge(t), children: /* @__PURE__ */ u.jsx(Qr, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function g0({ clientX: e, clientY: t, activities: n, onPick: o, onClose: r }) {
  const [i, s] = G(""), [c, l] = G(0), a = ce(null), d = ce(null), f = pe(() => {
    const N = i.trim().toLowerCase(), g = n.filter(gl);
    return N ? g.filter((w) => _e(w).toLowerCase().includes(N) || w.activityTypeKey.toLowerCase().includes(N) || (w.category ?? "").toLowerCase().includes(N) || (w.description ?? "").toLowerCase().includes(N)) : g;
  }, [n, i]), h = pe(() => fl(f), [f]), p = pe(() => h.flatMap((N) => N.activities), [h]);
  ae(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), ae(() => {
    const N = (w) => {
      a.current?.contains(w.target) || r();
    }, g = (w) => {
      w.key === "Escape" && r();
    };
    return document.addEventListener("mousedown", N, !0), document.addEventListener("keydown", g), () => {
      document.removeEventListener("mousedown", N, !0), document.removeEventListener("keydown", g);
    };
  }, [r]);
  const m = (N) => {
    if (N.key === "ArrowDown")
      N.preventDefault(), l((g) => Math.min(g + 1, p.length - 1));
    else if (N.key === "ArrowUp")
      N.preventDefault(), l((g) => Math.max(g - 1, 0));
    else if (N.key === "Enter") {
      N.preventDefault();
      const g = p[c];
      g && o(g);
    }
  }, x = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), v = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let y = -1;
  return /* @__PURE__ */ u.jsxs("div", { ref: a, className: "wf-connect-menu", style: { left: x, top: v }, onMouseDown: (N) => N.stopPropagation(), onClick: (N) => N.stopPropagation(), children: [
    /* @__PURE__ */ u.jsx(
      "input",
      {
        ref: d,
        type: "search",
        value: i,
        placeholder: "Search activities...",
        "aria-label": "Search activities",
        onChange: (N) => {
          s(N.target.value), l(0);
        },
        onKeyDown: m
      }
    ),
    /* @__PURE__ */ u.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: h.length === 0 ? /* @__PURE__ */ u.jsx("p", { children: "No matching activities." }) : h.map((N) => /* @__PURE__ */ u.jsxs("section", { children: [
      /* @__PURE__ */ u.jsx("h4", { children: N.category }),
      N.activities.map((g) => {
        y += 1;
        const w = y, _ = w === c;
        return /* @__PURE__ */ u.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": _,
            className: _ ? "active" : "",
            onMouseEnter: () => l(w),
            onClick: () => o(g),
            children: [
              /* @__PURE__ */ u.jsx("strong", { children: _e(g) }),
              /* @__PURE__ */ u.jsx("small", { children: g.category || g.activityTypeKey })
            ]
          },
          g.activityVersionId
        );
      })
    ] }, N.category)) })
  ] });
}
function m0({ draft: e }) {
  return e.validationErrors.length ? /* @__PURE__ */ u.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ u.jsx(kn, { size: 14 }),
    e.validationErrors.length,
    " validation issue",
    e.validationErrors.length === 1 ? "" : "s"
  ] }) : /* @__PURE__ */ u.jsxs("div", { className: "wf-validation ok", children: [
    /* @__PURE__ */ u.jsx(Yo, { size: 14 }),
    " No validation errors"
  ] });
}
function na(e) {
  return `${_e(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function oa(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function y0(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function x0(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function fn(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function ei(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
export {
  b0 as register
};
