import ht, { memo as ve, forwardRef as Lo, useRef as ce, useEffect as ie, useCallback as ge, useContext as Rn, useMemo as me, useState as Y, createContext as ai, useLayoutEffect as Ml, createElement as Br, useId as ha } from "react";
import "@tanstack/react-query";
function Dl(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var kr = { exports: {} }, pn = {};
var zi;
function Pl() {
  if (zi) return pn;
  zi = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function n(o, r, i) {
    var s = null;
    if (i !== void 0 && (s = "" + i), r.key !== void 0 && (s = "" + r.key), "key" in r) {
      i = {};
      for (var l in r)
        l !== "key" && (i[l] = r[l]);
    } else i = r;
    return r = i.ref, {
      $$typeof: e,
      type: o,
      key: s,
      ref: r !== void 0 ? r : null,
      props: i
    };
  }
  return pn.Fragment = t, pn.jsx = n, pn.jsxs = n, pn;
}
var Ri;
function $l() {
  return Ri || (Ri = 1, kr.exports = Pl()), kr.exports;
}
var a = $l();
function Ne(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, o; n < e.length; n++)
      (o = Ne(e[n])) !== "" && (t += (t && " ") + o);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var Tl = { value: () => {
} };
function Ho() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new mo(n);
}
function mo(e) {
  this._ = e;
}
function zl(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
mo.prototype = Ho.prototype = {
  constructor: mo,
  on: function(e, t) {
    var n = this._, o = zl(e + "", n), r, i = -1, s = o.length;
    if (arguments.length < 2) {
      for (; ++i < s; ) if ((r = (e = o[i]).type) && (r = Rl(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++i < s; )
      if (r = (e = o[i]).type) n[r] = Li(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = Li(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new mo(e);
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
function Rl(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function Li(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = Tl, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var Fr = "http://www.w3.org/1999/xhtml";
const Hi = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: Fr,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function Vo(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Hi.hasOwnProperty(t) ? { space: Hi[t], local: e } : e;
}
function Ll(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === Fr && t.documentElement.namespaceURI === Fr ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Hl(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function pa(e) {
  var t = Vo(e);
  return (t.local ? Hl : Ll)(t);
}
function Vl() {
}
function ci(e) {
  return e == null ? Vl : function() {
    return this.querySelector(e);
  };
}
function Ol(e) {
  typeof e != "function" && (e = ci(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, l = o[r] = new Array(s), u, c, d = 0; d < s; ++d)
      (u = i[d]) && (c = e.call(u, u.__data__, d, i)) && ("__data__" in u && (c.__data__ = u.__data__), l[d] = c);
  return new Le(o, this._parents);
}
function Bl(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function Fl() {
  return [];
}
function ga(e) {
  return e == null ? Fl : function() {
    return this.querySelectorAll(e);
  };
}
function Wl(e) {
  return function() {
    return Bl(e.apply(this, arguments));
  };
}
function Xl(e) {
  typeof e == "function" ? e = Wl(e) : e = ga(e);
  for (var t = this._groups, n = t.length, o = [], r = [], i = 0; i < n; ++i)
    for (var s = t[i], l = s.length, u, c = 0; c < l; ++c)
      (u = s[c]) && (o.push(e.call(u, u.__data__, c, s)), r.push(u));
  return new Le(o, r);
}
function ma(e) {
  return function() {
    return this.matches(e);
  };
}
function ya(e) {
  return function(t) {
    return t.matches(e);
  };
}
var Yl = Array.prototype.find;
function ql(e) {
  return function() {
    return Yl.call(this.children, e);
  };
}
function Zl() {
  return this.firstElementChild;
}
function Kl(e) {
  return this.select(e == null ? Zl : ql(typeof e == "function" ? e : ya(e)));
}
var Ul = Array.prototype.filter;
function Gl() {
  return Array.from(this.children);
}
function Jl(e) {
  return function() {
    return Ul.call(this.children, e);
  };
}
function Ql(e) {
  return this.selectAll(e == null ? Gl : Jl(typeof e == "function" ? e : ya(e)));
}
function eu(e) {
  typeof e != "function" && (e = ma(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, l = o[r] = [], u, c = 0; c < s; ++c)
      (u = i[c]) && e.call(u, u.__data__, c, i) && l.push(u);
  return new Le(o, this._parents);
}
function xa(e) {
  return new Array(e.length);
}
function tu() {
  return new Le(this._enter || this._groups.map(xa), this._parents);
}
function Eo(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
Eo.prototype = {
  constructor: Eo,
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
  for (var s = 0, l, u = t.length, c = i.length; s < c; ++s)
    (l = t[s]) ? (l.__data__ = i[s], o[s] = l) : n[s] = new Eo(e, i[s]);
  for (; s < u; ++s)
    (l = t[s]) && (r[s] = l);
}
function ru(e, t, n, o, r, i, s) {
  var l, u, c = /* @__PURE__ */ new Map(), d = t.length, f = i.length, h = new Array(d), g;
  for (l = 0; l < d; ++l)
    (u = t[l]) && (h[l] = g = s.call(u, u.__data__, l, t) + "", c.has(g) ? r[l] = u : c.set(g, u));
  for (l = 0; l < f; ++l)
    g = s.call(e, i[l], l, i) + "", (u = c.get(g)) ? (o[l] = u, u.__data__ = i[l], c.delete(g)) : n[l] = new Eo(e, i[l]);
  for (l = 0; l < d; ++l)
    (u = t[l]) && c.get(h[l]) === u && (r[l] = u);
}
function iu(e) {
  return e.__data__;
}
function su(e, t) {
  if (!arguments.length) return Array.from(this, iu);
  var n = t ? ru : ou, o = this._parents, r = this._groups;
  typeof e != "function" && (e = nu(e));
  for (var i = r.length, s = new Array(i), l = new Array(i), u = new Array(i), c = 0; c < i; ++c) {
    var d = o[c], f = r[c], h = f.length, g = au(e.call(d, d && d.__data__, c, o)), m = g.length, x = l[c] = new Array(m), v = s[c] = new Array(m), y = u[c] = new Array(h);
    n(d, f, x, v, y, g, t);
    for (var b = 0, p = 0, w, E; b < m; ++b)
      if (w = x[b]) {
        for (b >= p && (p = b + 1); !(E = v[p]) && ++p < m; ) ;
        w._next = E || null;
      }
  }
  return s = new Le(s, o), s._enter = l, s._exit = u, s;
}
function au(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function cu() {
  return new Le(this._exit || this._groups.map(xa), this._parents);
}
function lu(e, t, n) {
  var o = this.enter(), r = this, i = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? i.remove() : n(i), o && r ? o.merge(r).order() : r;
}
function uu(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, i = o.length, s = Math.min(r, i), l = new Array(r), u = 0; u < s; ++u)
    for (var c = n[u], d = o[u], f = c.length, h = l[u] = new Array(f), g, m = 0; m < f; ++m)
      (g = c[m] || d[m]) && (h[m] = g);
  for (; u < r; ++u)
    l[u] = n[u];
  return new Le(l, this._parents);
}
function du() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, i = o[r], s; --r >= 0; )
      (s = o[r]) && (i && s.compareDocumentPosition(i) ^ 4 && i.parentNode.insertBefore(s, i), i = s);
  return this;
}
function fu(e) {
  e || (e = hu);
  function t(f, h) {
    return f && h ? e(f.__data__, h.__data__) : !f - !h;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), i = 0; i < o; ++i) {
    for (var s = n[i], l = s.length, u = r[i] = new Array(l), c, d = 0; d < l; ++d)
      (c = s[d]) && (u[d] = c);
    u.sort(t);
  }
  return new Le(r, this._parents).order();
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
function mu() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length; r < i; ++r) {
      var s = o[r];
      if (s) return s;
    }
  return null;
}
function yu() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function xu() {
  return !this.node();
}
function wu(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], i = 0, s = r.length, l; i < s; ++i)
      (l = r[i]) && e.call(l, l.__data__, i, r);
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
function Su(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function Nu(e, t) {
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
function Cu(e, t) {
  var n = Vo(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? bu : vu : typeof t == "function" ? n.local ? ku : Eu : n.local ? Nu : Su)(n, t));
}
function wa(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function _u(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Iu(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function ju(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function Au(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? _u : typeof t == "function" ? ju : Iu)(e, t, n ?? "")) : Ht(this.node(), e);
}
function Ht(e, t) {
  return e.style.getPropertyValue(t) || wa(e).getComputedStyle(e, null).getPropertyValue(t);
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
function va(e) {
  return e.trim().split(/^|\s+/);
}
function li(e) {
  return e.classList || new ba(e);
}
function ba(e) {
  this._node = e, this._names = va(e.getAttribute("class") || "");
}
ba.prototype = {
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
function Sa(e, t) {
  for (var n = li(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function Na(e, t) {
  for (var n = li(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function Tu(e) {
  return function() {
    Sa(this, e);
  };
}
function zu(e) {
  return function() {
    Na(this, e);
  };
}
function Ru(e, t) {
  return function() {
    (t.apply(this, arguments) ? Sa : Na)(this, e);
  };
}
function Lu(e, t) {
  var n = va(e + "");
  if (arguments.length < 2) {
    for (var o = li(this.node()), r = -1, i = n.length; ++r < i; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? Ru : t ? Tu : zu)(n, t));
}
function Hu() {
  this.textContent = "";
}
function Vu(e) {
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
function Bu(e) {
  return arguments.length ? this.each(e == null ? Hu : (typeof e == "function" ? Ou : Vu)(e)) : this.node().textContent;
}
function Fu() {
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
  return arguments.length ? this.each(e == null ? Fu : (typeof e == "function" ? Xu : Wu)(e)) : this.node().innerHTML;
}
function qu() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function Zu() {
  return this.each(qu);
}
function Ku() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Uu() {
  return this.each(Ku);
}
function Gu(e) {
  var t = typeof e == "function" ? e : pa(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Ju() {
  return null;
}
function Qu(e, t) {
  var n = typeof e == "function" ? e : pa(e), o = t == null ? Ju : typeof t == "function" ? t : ci(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function ed() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function td() {
  return this.each(ed);
}
function nd() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function od() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function rd(e) {
  return this.select(e ? od : nd);
}
function id(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function sd(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function ad(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function cd(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, i; n < r; ++n)
        i = t[n], (!e.type || i.type === e.type) && i.name === e.name ? this.removeEventListener(i.type, i.listener, i.options) : t[++o] = i;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function ld(e, t, n) {
  return function() {
    var o = this.__on, r, i = sd(t);
    if (o) {
      for (var s = 0, l = o.length; s < l; ++s)
        if ((r = o[s]).type === e.type && r.name === e.name) {
          this.removeEventListener(r.type, r.listener, r.options), this.addEventListener(r.type, r.listener = i, r.options = n), r.value = t;
          return;
        }
    }
    this.addEventListener(e.type, i, n), r = { type: e.type, name: e.name, value: t, listener: i, options: n }, o ? o.push(r) : this.__on = [r];
  };
}
function ud(e, t, n) {
  var o = ad(e + ""), r, i = o.length, s;
  if (arguments.length < 2) {
    var l = this.node().__on;
    if (l) {
      for (var u = 0, c = l.length, d; u < c; ++u)
        for (r = 0, d = l[u]; r < i; ++r)
          if ((s = o[r]).type === d.type && s.name === d.name)
            return d.value;
    }
    return;
  }
  for (l = t ? ld : cd, r = 0; r < i; ++r) this.each(l(o[r], t, n));
  return this;
}
function Ea(e, t, n) {
  var o = wa(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function dd(e, t) {
  return function() {
    return Ea(this, e, t);
  };
}
function fd(e, t) {
  return function() {
    return Ea(this, e, t.apply(this, arguments));
  };
}
function hd(e, t) {
  return this.each((typeof t == "function" ? fd : dd)(e, t));
}
function* pd() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length, s; r < i; ++r)
      (s = o[r]) && (yield s);
}
var ka = [null];
function Le(e, t) {
  this._groups = e, this._parents = t;
}
function Ln() {
  return new Le([[document.documentElement]], ka);
}
function gd() {
  return this;
}
Le.prototype = Ln.prototype = {
  constructor: Le,
  select: Ol,
  selectAll: Xl,
  selectChild: Kl,
  selectChildren: Ql,
  filter: eu,
  data: su,
  enter: tu,
  exit: cu,
  join: lu,
  merge: uu,
  selection: gd,
  order: du,
  sort: fu,
  call: pu,
  nodes: gu,
  node: mu,
  size: yu,
  empty: xu,
  each: wu,
  attr: Cu,
  style: Au,
  property: $u,
  classed: Lu,
  text: Bu,
  html: Yu,
  raise: Zu,
  lower: Uu,
  append: Gu,
  insert: Qu,
  remove: td,
  clone: rd,
  datum: id,
  on: ud,
  dispatch: hd,
  [Symbol.iterator]: pd
};
function Re(e) {
  return typeof e == "string" ? new Le([[document.querySelector(e)]], [document.documentElement]) : new Le([[e]], ka);
}
function md(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function We(e, t) {
  if (e = md(e), t === void 0 && (t = e.currentTarget), t) {
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
const yd = { passive: !1 }, _n = { capture: !0, passive: !1 };
function Cr(e) {
  e.stopImmediatePropagation();
}
function Rt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Ca(e) {
  var t = e.document.documentElement, n = Re(e).on("dragstart.drag", Rt, _n);
  "onselectstart" in t ? n.on("selectstart.drag", Rt, _n) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function _a(e, t) {
  var n = e.document.documentElement, o = Re(e).on("dragstart.drag", null);
  t && (o.on("click.drag", Rt, _n), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const ro = (e) => () => e;
function Wr(e, {
  sourceEvent: t,
  subject: n,
  target: o,
  identifier: r,
  active: i,
  x: s,
  y: l,
  dx: u,
  dy: c,
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
    y: { value: l, enumerable: !0, configurable: !0 },
    dx: { value: u, enumerable: !0, configurable: !0 },
    dy: { value: c, enumerable: !0, configurable: !0 },
    _: { value: d }
  });
}
Wr.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function xd(e) {
  return !e.ctrlKey && !e.button;
}
function wd() {
  return this.parentNode;
}
function vd(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function bd() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Ia() {
  var e = xd, t = wd, n = vd, o = bd, r = {}, i = Ho("start", "drag", "end"), s = 0, l, u, c, d, f = 0;
  function h(w) {
    w.on("mousedown.drag", g).filter(o).on("touchstart.drag", v).on("touchmove.drag", y, yd).on("touchend.drag touchcancel.drag", b).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function g(w, E) {
    if (!(d || !e.call(this, w, E))) {
      var k = p(this, t.call(this, w, E), w, E, "mouse");
      k && (Re(w.view).on("mousemove.drag", m, _n).on("mouseup.drag", x, _n), Ca(w.view), Cr(w), c = !1, l = w.clientX, u = w.clientY, k("start", w));
    }
  }
  function m(w) {
    if (Rt(w), !c) {
      var E = w.clientX - l, k = w.clientY - u;
      c = E * E + k * k > f;
    }
    r.mouse("drag", w);
  }
  function x(w) {
    Re(w.view).on("mousemove.drag mouseup.drag", null), _a(w.view, c), Rt(w), r.mouse("end", w);
  }
  function v(w, E) {
    if (e.call(this, w, E)) {
      var k = w.changedTouches, _ = t.call(this, w, E), D = k.length, $, F;
      for ($ = 0; $ < D; ++$)
        (F = p(this, _, w, E, k[$].identifier, k[$])) && (Cr(w), F("start", w, k[$]));
    }
  }
  function y(w) {
    var E = w.changedTouches, k = E.length, _, D;
    for (_ = 0; _ < k; ++_)
      (D = r[E[_].identifier]) && (Rt(w), D("drag", w, E[_]));
  }
  function b(w) {
    var E = w.changedTouches, k = E.length, _, D;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), _ = 0; _ < k; ++_)
      (D = r[E[_].identifier]) && (Cr(w), D("end", w, E[_]));
  }
  function p(w, E, k, _, D, $) {
    var F = i.copy(), j = We($ || k, E), z, H, S;
    if ((S = n.call(w, new Wr("beforestart", {
      sourceEvent: k,
      target: h,
      identifier: D,
      active: s,
      x: j[0],
      y: j[1],
      dx: 0,
      dy: 0,
      dispatch: F
    }), _)) != null)
      return z = S.x - j[0] || 0, H = S.y - j[1] || 0, function I(C, A, T) {
        var P = j, W;
        switch (C) {
          case "start":
            r[D] = I, W = s++;
            break;
          case "end":
            delete r[D], --s;
          // falls through
          case "drag":
            j = We(T || A, E), W = s;
            break;
        }
        F.call(
          C,
          w,
          new Wr(C, {
            sourceEvent: A,
            subject: S,
            target: h,
            identifier: D,
            active: W,
            x: j[0] + z,
            y: j[1] + H,
            dx: j[0] - P[0],
            dy: j[1] - P[1],
            dispatch: F
          }),
          _
        );
      };
  }
  return h.filter = function(w) {
    return arguments.length ? (e = typeof w == "function" ? w : ro(!!w), h) : e;
  }, h.container = function(w) {
    return arguments.length ? (t = typeof w == "function" ? w : ro(w), h) : t;
  }, h.subject = function(w) {
    return arguments.length ? (n = typeof w == "function" ? w : ro(w), h) : n;
  }, h.touchable = function(w) {
    return arguments.length ? (o = typeof w == "function" ? w : ro(!!w), h) : o;
  }, h.on = function() {
    var w = i.on.apply(i, arguments);
    return w === i ? h : w;
  }, h.clickDistance = function(w) {
    return arguments.length ? (f = (w = +w) * w, h) : Math.sqrt(f);
  }, h;
}
function ui(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function ja(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function Hn() {
}
var In = 0.7, ko = 1 / In, Lt = "\\s*([+-]?\\d+)\\s*", jn = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", Ge = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", Sd = /^#([0-9a-f]{3,8})$/, Nd = new RegExp(`^rgb\\(${Lt},${Lt},${Lt}\\)$`), Ed = new RegExp(`^rgb\\(${Ge},${Ge},${Ge}\\)$`), kd = new RegExp(`^rgba\\(${Lt},${Lt},${Lt},${jn}\\)$`), Cd = new RegExp(`^rgba\\(${Ge},${Ge},${Ge},${jn}\\)$`), _d = new RegExp(`^hsl\\(${jn},${Ge},${Ge}\\)$`), Id = new RegExp(`^hsla\\(${jn},${Ge},${Ge},${jn}\\)$`), Vi = {
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
ui(Hn, Et, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Oi,
  // Deprecated! Use color.formatHex.
  formatHex: Oi,
  formatHex8: jd,
  formatHsl: Ad,
  formatRgb: Bi,
  toString: Bi
});
function Oi() {
  return this.rgb().formatHex();
}
function jd() {
  return this.rgb().formatHex8();
}
function Ad() {
  return Aa(this).formatHsl();
}
function Bi() {
  return this.rgb().formatRgb();
}
function Et(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = Sd.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? Fi(t) : n === 3 ? new De(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? io(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? io(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = Nd.exec(e)) ? new De(t[1], t[2], t[3], 1) : (t = Ed.exec(e)) ? new De(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = kd.exec(e)) ? io(t[1], t[2], t[3], t[4]) : (t = Cd.exec(e)) ? io(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = _d.exec(e)) ? Yi(t[1], t[2] / 100, t[3] / 100, 1) : (t = Id.exec(e)) ? Yi(t[1], t[2] / 100, t[3] / 100, t[4]) : Vi.hasOwnProperty(e) ? Fi(Vi[e]) : e === "transparent" ? new De(NaN, NaN, NaN, 0) : null;
}
function Fi(e) {
  return new De(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function io(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new De(e, t, n, o);
}
function Md(e) {
  return e instanceof Hn || (e = Et(e)), e ? (e = e.rgb(), new De(e.r, e.g, e.b, e.opacity)) : new De();
}
function Xr(e, t, n, o) {
  return arguments.length === 1 ? Md(e) : new De(e, t, n, o ?? 1);
}
function De(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
ui(De, Xr, ja(Hn, {
  brighter(e) {
    return e = e == null ? ko : Math.pow(ko, e), new De(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? In : Math.pow(In, e), new De(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new De(St(this.r), St(this.g), St(this.b), Co(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: Wi,
  // Deprecated! Use color.formatHex.
  formatHex: Wi,
  formatHex8: Dd,
  formatRgb: Xi,
  toString: Xi
}));
function Wi() {
  return `#${bt(this.r)}${bt(this.g)}${bt(this.b)}`;
}
function Dd() {
  return `#${bt(this.r)}${bt(this.g)}${bt(this.b)}${bt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function Xi() {
  const e = Co(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${St(this.r)}, ${St(this.g)}, ${St(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Co(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function St(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function bt(e) {
  return e = St(e), (e < 16 ? "0" : "") + e.toString(16);
}
function Yi(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Xe(e, t, n, o);
}
function Aa(e) {
  if (e instanceof Xe) return new Xe(e.h, e.s, e.l, e.opacity);
  if (e instanceof Hn || (e = Et(e)), !e) return new Xe();
  if (e instanceof Xe) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), i = Math.max(t, n, o), s = NaN, l = i - r, u = (i + r) / 2;
  return l ? (t === i ? s = (n - o) / l + (n < o) * 6 : n === i ? s = (o - t) / l + 2 : s = (t - n) / l + 4, l /= u < 0.5 ? i + r : 2 - i - r, s *= 60) : l = u > 0 && u < 1 ? 0 : s, new Xe(s, l, u, e.opacity);
}
function Pd(e, t, n, o) {
  return arguments.length === 1 ? Aa(e) : new Xe(e, t, n, o ?? 1);
}
function Xe(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
ui(Xe, Pd, ja(Hn, {
  brighter(e) {
    return e = e == null ? ko : Math.pow(ko, e), new Xe(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? In : Math.pow(In, e), new Xe(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new De(
      _r(e >= 240 ? e - 240 : e + 120, r, o),
      _r(e, r, o),
      _r(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new Xe(qi(this.h), so(this.s), so(this.l), Co(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Co(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${qi(this.h)}, ${so(this.s) * 100}%, ${so(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function qi(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function so(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function _r(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const di = (e) => () => e;
function $d(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function Td(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function zd(e) {
  return (e = +e) == 1 ? Ma : function(t, n) {
    return n - t ? Td(t, n, e) : di(isNaN(t) ? n : t);
  };
}
function Ma(e, t) {
  var n = t - e;
  return n ? $d(e, n) : di(isNaN(e) ? t : e);
}
const _o = (function e(t) {
  var n = zd(t);
  function o(r, i) {
    var s = n((r = Xr(r)).r, (i = Xr(i)).r), l = n(r.g, i.g), u = n(r.b, i.b), c = Ma(r.opacity, i.opacity);
    return function(d) {
      return r.r = s(d), r.g = l(d), r.b = u(d), r.opacity = c(d), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function Rd(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(i) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - i) + t[r] * i;
    return o;
  };
}
function Ld(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Hd(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), i = new Array(n), s;
  for (s = 0; s < o; ++s) r[s] = kn(e[s], t[s]);
  for (; s < n; ++s) i[s] = t[s];
  return function(l) {
    for (s = 0; s < o; ++s) i[s] = r[s](l);
    return i;
  };
}
function Vd(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(o) {
    return n.setTime(e * (1 - o) + t * o), n;
  };
}
function Ue(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function Od(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = kn(e[r], t[r]) : o[r] = t[r];
  return function(i) {
    for (r in n) o[r] = n[r](i);
    return o;
  };
}
var Yr = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, Ir = new RegExp(Yr.source, "g");
function Bd(e) {
  return function() {
    return e;
  };
}
function Fd(e) {
  return function(t) {
    return e(t) + "";
  };
}
function Da(e, t) {
  var n = Yr.lastIndex = Ir.lastIndex = 0, o, r, i, s = -1, l = [], u = [];
  for (e = e + "", t = t + ""; (o = Yr.exec(e)) && (r = Ir.exec(t)); )
    (i = r.index) > n && (i = t.slice(n, i), l[s] ? l[s] += i : l[++s] = i), (o = o[0]) === (r = r[0]) ? l[s] ? l[s] += r : l[++s] = r : (l[++s] = null, u.push({ i: s, x: Ue(o, r) })), n = Ir.lastIndex;
  return n < t.length && (i = t.slice(n), l[s] ? l[s] += i : l[++s] = i), l.length < 2 ? u[0] ? Fd(u[0].x) : Bd(t) : (t = u.length, function(c) {
    for (var d = 0, f; d < t; ++d) l[(f = u[d]).i] = f.x(c);
    return l.join("");
  });
}
function kn(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? di(t) : (n === "number" ? Ue : n === "string" ? (o = Et(t)) ? (t = o, _o) : Da : t instanceof Et ? _o : t instanceof Date ? Vd : Ld(t) ? Rd : Array.isArray(t) ? Hd : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Od : Ue)(e, t);
}
var Zi = 180 / Math.PI, qr = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function Pa(e, t, n, o, r, i) {
  var s, l, u;
  return (s = Math.sqrt(e * e + t * t)) && (e /= s, t /= s), (u = e * n + t * o) && (n -= e * u, o -= t * u), (l = Math.sqrt(n * n + o * o)) && (n /= l, o /= l, u /= l), e * o < t * n && (e = -e, t = -t, u = -u, s = -s), {
    translateX: r,
    translateY: i,
    rotate: Math.atan2(t, e) * Zi,
    skewX: Math.atan(u) * Zi,
    scaleX: s,
    scaleY: l
  };
}
var ao;
function Wd(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? qr : Pa(t.a, t.b, t.c, t.d, t.e, t.f);
}
function Xd(e) {
  return e == null || (ao || (ao = document.createElementNS("http://www.w3.org/2000/svg", "g")), ao.setAttribute("transform", e), !(e = ao.transform.baseVal.consolidate())) ? qr : (e = e.matrix, Pa(e.a, e.b, e.c, e.d, e.e, e.f));
}
function $a(e, t, n, o) {
  function r(c) {
    return c.length ? c.pop() + " " : "";
  }
  function i(c, d, f, h, g, m) {
    if (c !== f || d !== h) {
      var x = g.push("translate(", null, t, null, n);
      m.push({ i: x - 4, x: Ue(c, f) }, { i: x - 2, x: Ue(d, h) });
    } else (f || h) && g.push("translate(" + f + t + h + n);
  }
  function s(c, d, f, h) {
    c !== d ? (c - d > 180 ? d += 360 : d - c > 180 && (c += 360), h.push({ i: f.push(r(f) + "rotate(", null, o) - 2, x: Ue(c, d) })) : d && f.push(r(f) + "rotate(" + d + o);
  }
  function l(c, d, f, h) {
    c !== d ? h.push({ i: f.push(r(f) + "skewX(", null, o) - 2, x: Ue(c, d) }) : d && f.push(r(f) + "skewX(" + d + o);
  }
  function u(c, d, f, h, g, m) {
    if (c !== f || d !== h) {
      var x = g.push(r(g) + "scale(", null, ",", null, ")");
      m.push({ i: x - 4, x: Ue(c, f) }, { i: x - 2, x: Ue(d, h) });
    } else (f !== 1 || h !== 1) && g.push(r(g) + "scale(" + f + "," + h + ")");
  }
  return function(c, d) {
    var f = [], h = [];
    return c = e(c), d = e(d), i(c.translateX, c.translateY, d.translateX, d.translateY, f, h), s(c.rotate, d.rotate, f, h), l(c.skewX, d.skewX, f, h), u(c.scaleX, c.scaleY, d.scaleX, d.scaleY, f, h), c = d = null, function(g) {
      for (var m = -1, x = h.length, v; ++m < x; ) f[(v = h[m]).i] = v.x(g);
      return f.join("");
    };
  };
}
var Yd = $a(Wd, "px, ", "px)", "deg)"), qd = $a(Xd, ", ", ")", ")"), Zd = 1e-12;
function Ki(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Kd(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Ud(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const yo = (function e(t, n, o) {
  function r(i, s) {
    var l = i[0], u = i[1], c = i[2], d = s[0], f = s[1], h = s[2], g = d - l, m = f - u, x = g * g + m * m, v, y;
    if (x < Zd)
      y = Math.log(h / c) / t, v = function(_) {
        return [
          l + _ * g,
          u + _ * m,
          c * Math.exp(t * _ * y)
        ];
      };
    else {
      var b = Math.sqrt(x), p = (h * h - c * c + o * x) / (2 * c * n * b), w = (h * h - c * c - o * x) / (2 * h * n * b), E = Math.log(Math.sqrt(p * p + 1) - p), k = Math.log(Math.sqrt(w * w + 1) - w);
      y = (k - E) / t, v = function(_) {
        var D = _ * y, $ = Ki(E), F = c / (n * b) * ($ * Ud(t * D + E) - Kd(E));
        return [
          l + F * g,
          u + F * m,
          c * $ / Ki(t * D + E)
        ];
      };
    }
    return v.duration = y * 1e3 * t / Math.SQRT2, v;
  }
  return r.rho = function(i) {
    var s = Math.max(1e-3, +i), l = s * s, u = l * l;
    return e(s, l, u);
  }, r;
})(Math.SQRT2, 2, 4);
var Vt = 0, Sn = 0, gn = 0, Ta = 1e3, Io, Nn, jo = 0, kt = 0, Oo = 0, An = typeof performance == "object" && performance.now ? performance : Date, za = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function fi() {
  return kt || (za(Gd), kt = An.now() + Oo);
}
function Gd() {
  kt = 0;
}
function Ao() {
  this._call = this._time = this._next = null;
}
Ao.prototype = Ra.prototype = {
  constructor: Ao,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? fi() : +n) + (t == null ? 0 : +t), !this._next && Nn !== this && (Nn ? Nn._next = this : Io = this, Nn = this), this._call = e, this._time = n, Zr();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Zr());
  }
};
function Ra(e, t, n) {
  var o = new Ao();
  return o.restart(e, t, n), o;
}
function Jd() {
  fi(), ++Vt;
  for (var e = Io, t; e; )
    (t = kt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --Vt;
}
function Ui() {
  kt = (jo = An.now()) + Oo, Vt = Sn = 0;
  try {
    Jd();
  } finally {
    Vt = 0, ef(), kt = 0;
  }
}
function Qd() {
  var e = An.now(), t = e - jo;
  t > Ta && (Oo -= t, jo = e);
}
function ef() {
  for (var e, t = Io, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Io = n);
  Nn = e, Zr(o);
}
function Zr(e) {
  if (!Vt) {
    Sn && (Sn = clearTimeout(Sn));
    var t = e - kt;
    t > 24 ? (e < 1 / 0 && (Sn = setTimeout(Ui, e - An.now() - Oo)), gn && (gn = clearInterval(gn))) : (gn || (jo = An.now(), gn = setInterval(Qd, Ta)), Vt = 1, za(Ui));
  }
}
function Gi(e, t, n) {
  var o = new Ao();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var tf = Ho("start", "end", "cancel", "interrupt"), nf = [], La = 0, Ji = 1, Kr = 2, xo = 3, Qi = 4, Ur = 5, wo = 6;
function Bo(e, t, n, o, r, i) {
  var s = e.__transition;
  if (!s) e.__transition = {};
  else if (n in s) return;
  of(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: tf,
    tween: nf,
    time: i.time,
    delay: i.delay,
    duration: i.duration,
    ease: i.ease,
    timer: null,
    state: La
  });
}
function hi(e, t) {
  var n = Ze(e, t);
  if (n.state > La) throw new Error("too late; already scheduled");
  return n;
}
function Qe(e, t) {
  var n = Ze(e, t);
  if (n.state > xo) throw new Error("too late; already running");
  return n;
}
function Ze(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function of(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = Ra(i, 0, n.time);
  function i(c) {
    n.state = Ji, n.timer.restart(s, n.delay, n.time), n.delay <= c && s(c - n.delay);
  }
  function s(c) {
    var d, f, h, g;
    if (n.state !== Ji) return u();
    for (d in o)
      if (g = o[d], g.name === n.name) {
        if (g.state === xo) return Gi(s);
        g.state === Qi ? (g.state = wo, g.timer.stop(), g.on.call("interrupt", e, e.__data__, g.index, g.group), delete o[d]) : +d < t && (g.state = wo, g.timer.stop(), g.on.call("cancel", e, e.__data__, g.index, g.group), delete o[d]);
      }
    if (Gi(function() {
      n.state === xo && (n.state = Qi, n.timer.restart(l, n.delay, n.time), l(c));
    }), n.state = Kr, n.on.call("start", e, e.__data__, n.index, n.group), n.state === Kr) {
      for (n.state = xo, r = new Array(h = n.tween.length), d = 0, f = -1; d < h; ++d)
        (g = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (r[++f] = g);
      r.length = f + 1;
    }
  }
  function l(c) {
    for (var d = c < n.duration ? n.ease.call(null, c / n.duration) : (n.timer.restart(u), n.state = Ur, 1), f = -1, h = r.length; ++f < h; )
      r[f].call(e, d);
    n.state === Ur && (n.on.call("end", e, e.__data__, n.index, n.group), u());
  }
  function u() {
    n.state = wo, n.timer.stop(), delete o[t];
    for (var c in o) return;
    delete e.__transition;
  }
}
function vo(e, t) {
  var n = e.__transition, o, r, i = !0, s;
  if (n) {
    t = t == null ? null : t + "";
    for (s in n) {
      if ((o = n[s]).name !== t) {
        i = !1;
        continue;
      }
      r = o.state > Kr && o.state < Ur, o.state = wo, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[s];
    }
    i && delete e.__transition;
  }
}
function rf(e) {
  return this.each(function() {
    vo(this, e);
  });
}
function sf(e, t) {
  var n, o;
  return function() {
    var r = Qe(this, e), i = r.tween;
    if (i !== n) {
      o = n = i;
      for (var s = 0, l = o.length; s < l; ++s)
        if (o[s].name === t) {
          o = o.slice(), o.splice(s, 1);
          break;
        }
    }
    r.tween = o;
  };
}
function af(e, t, n) {
  var o, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var i = Qe(this, e), s = i.tween;
    if (s !== o) {
      r = (o = s).slice();
      for (var l = { name: t, value: n }, u = 0, c = r.length; u < c; ++u)
        if (r[u].name === t) {
          r[u] = l;
          break;
        }
      u === c && r.push(l);
    }
    i.tween = r;
  };
}
function cf(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = Ze(this.node(), n).tween, r = 0, i = o.length, s; r < i; ++r)
      if ((s = o[r]).name === e)
        return s.value;
    return null;
  }
  return this.each((t == null ? sf : af)(n, e, t));
}
function pi(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = Qe(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return Ze(r, o).value[t];
  };
}
function Ha(e, t) {
  var n;
  return (typeof t == "number" ? Ue : t instanceof Et ? _o : (n = Et(t)) ? (t = n, _o) : Da)(e, t);
}
function lf(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function uf(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function df(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttribute(e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function ff(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttributeNS(e.space, e.local);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function hf(e, t, n) {
  var o, r, i;
  return function() {
    var s, l = n(this), u;
    return l == null ? void this.removeAttribute(e) : (s = this.getAttribute(e), u = l + "", s === u ? null : s === o && u === r ? i : (r = u, i = t(o = s, l)));
  };
}
function pf(e, t, n) {
  var o, r, i;
  return function() {
    var s, l = n(this), u;
    return l == null ? void this.removeAttributeNS(e.space, e.local) : (s = this.getAttributeNS(e.space, e.local), u = l + "", s === u ? null : s === o && u === r ? i : (r = u, i = t(o = s, l)));
  };
}
function gf(e, t) {
  var n = Vo(e), o = n === "transform" ? qd : Ha;
  return this.attrTween(e, typeof t == "function" ? (n.local ? pf : hf)(n, o, pi(this, "attr." + e, t)) : t == null ? (n.local ? uf : lf)(n) : (n.local ? ff : df)(n, o, t));
}
function mf(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function yf(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function xf(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && yf(e, i)), n;
  }
  return r._value = t, r;
}
function wf(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && mf(e, i)), n;
  }
  return r._value = t, r;
}
function vf(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = Vo(e);
  return this.tween(n, (o.local ? xf : wf)(o, t));
}
function bf(e, t) {
  return function() {
    hi(this, e).delay = +t.apply(this, arguments);
  };
}
function Sf(e, t) {
  return t = +t, function() {
    hi(this, e).delay = t;
  };
}
function Nf(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? bf : Sf)(t, e)) : Ze(this.node(), t).delay;
}
function Ef(e, t) {
  return function() {
    Qe(this, e).duration = +t.apply(this, arguments);
  };
}
function kf(e, t) {
  return t = +t, function() {
    Qe(this, e).duration = t;
  };
}
function Cf(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Ef : kf)(t, e)) : Ze(this.node(), t).duration;
}
function _f(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    Qe(this, e).ease = t;
  };
}
function If(e) {
  var t = this._id;
  return arguments.length ? this.each(_f(t, e)) : Ze(this.node(), t).ease;
}
function jf(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    Qe(this, e).ease = n;
  };
}
function Af(e) {
  if (typeof e != "function") throw new Error();
  return this.each(jf(this._id, e));
}
function Mf(e) {
  typeof e != "function" && (e = ma(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, l = o[r] = [], u, c = 0; c < s; ++c)
      (u = i[c]) && e.call(u, u.__data__, c, i) && l.push(u);
  return new it(o, this._parents, this._name, this._id);
}
function Df(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, i = Math.min(o, r), s = new Array(o), l = 0; l < i; ++l)
    for (var u = t[l], c = n[l], d = u.length, f = s[l] = new Array(d), h, g = 0; g < d; ++g)
      (h = u[g] || c[g]) && (f[g] = h);
  for (; l < o; ++l)
    s[l] = t[l];
  return new it(s, this._parents, this._name, this._id);
}
function Pf(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function $f(e, t, n) {
  var o, r, i = Pf(t) ? hi : Qe;
  return function() {
    var s = i(this, e), l = s.on;
    l !== o && (r = (o = l).copy()).on(t, n), s.on = r;
  };
}
function Tf(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Ze(this.node(), n).on.on(e) : this.each($f(n, e, t));
}
function zf(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function Rf() {
  return this.on("end.remove", zf(this._id));
}
function Lf(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = ci(e));
  for (var o = this._groups, r = o.length, i = new Array(r), s = 0; s < r; ++s)
    for (var l = o[s], u = l.length, c = i[s] = new Array(u), d, f, h = 0; h < u; ++h)
      (d = l[h]) && (f = e.call(d, d.__data__, h, l)) && ("__data__" in d && (f.__data__ = d.__data__), c[h] = f, Bo(c[h], t, n, h, c, Ze(d, n)));
  return new it(i, this._parents, t, n);
}
function Hf(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = ga(e));
  for (var o = this._groups, r = o.length, i = [], s = [], l = 0; l < r; ++l)
    for (var u = o[l], c = u.length, d, f = 0; f < c; ++f)
      if (d = u[f]) {
        for (var h = e.call(d, d.__data__, f, u), g, m = Ze(d, n), x = 0, v = h.length; x < v; ++x)
          (g = h[x]) && Bo(g, t, n, x, h, m);
        i.push(h), s.push(d);
      }
  return new it(i, s, t, n);
}
var Vf = Ln.prototype.constructor;
function Of() {
  return new Vf(this._groups, this._parents);
}
function Bf(e, t) {
  var n, o, r;
  return function() {
    var i = Ht(this, e), s = (this.style.removeProperty(e), Ht(this, e));
    return i === s ? null : i === n && s === o ? r : r = t(n = i, o = s);
  };
}
function Va(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Ff(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = Ht(this, e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Wf(e, t, n) {
  var o, r, i;
  return function() {
    var s = Ht(this, e), l = n(this), u = l + "";
    return l == null && (u = l = (this.style.removeProperty(e), Ht(this, e))), s === u ? null : s === o && u === r ? i : (r = u, i = t(o = s, l));
  };
}
function Xf(e, t) {
  var n, o, r, i = "style." + t, s = "end." + i, l;
  return function() {
    var u = Qe(this, e), c = u.on, d = u.value[i] == null ? l || (l = Va(t)) : void 0;
    (c !== n || r !== d) && (o = (n = c).copy()).on(s, r = d), u.on = o;
  };
}
function Yf(e, t, n) {
  var o = (e += "") == "transform" ? Yd : Ha;
  return t == null ? this.styleTween(e, Bf(e, o)).on("end.style." + e, Va(e)) : typeof t == "function" ? this.styleTween(e, Wf(e, o, pi(this, "style." + e, t))).each(Xf(this._id, e)) : this.styleTween(e, Ff(e, o, t), n).on("end.style." + e, null);
}
function qf(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function Zf(e, t, n) {
  var o, r;
  function i() {
    var s = t.apply(this, arguments);
    return s !== r && (o = (r = s) && qf(e, s, n)), o;
  }
  return i._value = t, i;
}
function Kf(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, Zf(e, t, n ?? ""));
}
function Uf(e) {
  return function() {
    this.textContent = e;
  };
}
function Gf(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function Jf(e) {
  return this.tween("text", typeof e == "function" ? Gf(pi(this, "text", e)) : Uf(e == null ? "" : e + ""));
}
function Qf(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function eh(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Qf(r)), t;
  }
  return o._value = e, o;
}
function th(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, eh(e));
}
function nh() {
  for (var e = this._name, t = this._id, n = Oa(), o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], l = s.length, u, c = 0; c < l; ++c)
      if (u = s[c]) {
        var d = Ze(u, t);
        Bo(u, e, n, c, s, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new it(o, this._parents, e, n);
}
function oh() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(i, s) {
    var l = { value: s }, u = { value: function() {
      --r === 0 && i();
    } };
    n.each(function() {
      var c = Qe(this, o), d = c.on;
      d !== e && (t = (e = d).copy(), t._.cancel.push(l), t._.interrupt.push(l), t._.end.push(u)), c.on = t;
    }), r === 0 && i();
  });
}
var rh = 0;
function it(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function Oa() {
  return ++rh;
}
var nt = Ln.prototype;
it.prototype = {
  constructor: it,
  select: Lf,
  selectAll: Hf,
  selectChild: nt.selectChild,
  selectChildren: nt.selectChildren,
  filter: Mf,
  merge: Df,
  selection: Of,
  transition: nh,
  call: nt.call,
  nodes: nt.nodes,
  node: nt.node,
  size: nt.size,
  empty: nt.empty,
  each: nt.each,
  on: Tf,
  attr: gf,
  attrTween: vf,
  style: Yf,
  styleTween: Kf,
  text: Jf,
  textTween: th,
  remove: Rf,
  tween: cf,
  delay: Nf,
  duration: Cf,
  ease: If,
  easeVarying: Af,
  end: oh,
  [Symbol.iterator]: nt[Symbol.iterator]
};
function ih(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var sh = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: ih
};
function ah(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function ch(e) {
  var t, n;
  e instanceof it ? (t = e._id, e = e._name) : (t = Oa(), (n = sh).time = fi(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], l = s.length, u, c = 0; c < l; ++c)
      (u = s[c]) && Bo(u, e, t, c, s, n || ah(u, t));
  return new it(o, this._parents, e, t);
}
Ln.prototype.interrupt = rf;
Ln.prototype.transition = ch;
const co = (e) => () => e;
function lh(e, {
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
var Fo = new ot(1, 0, 0);
Ba.prototype = ot.prototype;
function Ba(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return Fo;
  return e.__zoom;
}
function jr(e) {
  e.stopImmediatePropagation();
}
function mn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function uh(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function dh() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function es() {
  return this.__zoom || Fo;
}
function fh(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function hh() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function ph(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], i = e.invertY(t[0][1]) - n[0][1], s = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    s > i ? (i + s) / 2 : Math.min(0, i) || Math.max(0, s)
  );
}
function Fa() {
  var e = uh, t = dh, n = ph, o = fh, r = hh, i = [0, 1 / 0], s = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], l = 250, u = yo, c = Ho("start", "zoom", "end"), d, f, h, g = 500, m = 150, x = 0, v = 10;
  function y(S) {
    S.property("__zoom", es).on("wheel.zoom", D, { passive: !1 }).on("mousedown.zoom", $).on("dblclick.zoom", F).filter(r).on("touchstart.zoom", j).on("touchmove.zoom", z).on("touchend.zoom touchcancel.zoom", H).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  y.transform = function(S, I, C, A) {
    var T = S.selection ? S.selection() : S;
    T.property("__zoom", es), S !== T ? E(S, I, C, A) : T.interrupt().each(function() {
      k(this, arguments).event(A).start().zoom(null, typeof I == "function" ? I.apply(this, arguments) : I).end();
    });
  }, y.scaleBy = function(S, I, C, A) {
    y.scaleTo(S, function() {
      var T = this.__zoom.k, P = typeof I == "function" ? I.apply(this, arguments) : I;
      return T * P;
    }, C, A);
  }, y.scaleTo = function(S, I, C, A) {
    y.transform(S, function() {
      var T = t.apply(this, arguments), P = this.__zoom, W = C == null ? w(T) : typeof C == "function" ? C.apply(this, arguments) : C, O = P.invert(W), B = typeof I == "function" ? I.apply(this, arguments) : I;
      return n(p(b(P, B), W, O), T, s);
    }, C, A);
  }, y.translateBy = function(S, I, C, A) {
    y.transform(S, function() {
      return n(this.__zoom.translate(
        typeof I == "function" ? I.apply(this, arguments) : I,
        typeof C == "function" ? C.apply(this, arguments) : C
      ), t.apply(this, arguments), s);
    }, null, A);
  }, y.translateTo = function(S, I, C, A, T) {
    y.transform(S, function() {
      var P = t.apply(this, arguments), W = this.__zoom, O = A == null ? w(P) : typeof A == "function" ? A.apply(this, arguments) : A;
      return n(Fo.translate(O[0], O[1]).scale(W.k).translate(
        typeof I == "function" ? -I.apply(this, arguments) : -I,
        typeof C == "function" ? -C.apply(this, arguments) : -C
      ), P, s);
    }, A, T);
  };
  function b(S, I) {
    return I = Math.max(i[0], Math.min(i[1], I)), I === S.k ? S : new ot(I, S.x, S.y);
  }
  function p(S, I, C) {
    var A = I[0] - C[0] * S.k, T = I[1] - C[1] * S.k;
    return A === S.x && T === S.y ? S : new ot(S.k, A, T);
  }
  function w(S) {
    return [(+S[0][0] + +S[1][0]) / 2, (+S[0][1] + +S[1][1]) / 2];
  }
  function E(S, I, C, A) {
    S.on("start.zoom", function() {
      k(this, arguments).event(A).start();
    }).on("interrupt.zoom end.zoom", function() {
      k(this, arguments).event(A).end();
    }).tween("zoom", function() {
      var T = this, P = arguments, W = k(T, P).event(A), O = t.apply(T, P), B = C == null ? w(O) : typeof C == "function" ? C.apply(T, P) : C, G = Math.max(O[1][0] - O[0][0], O[1][1] - O[0][1]), Z = T.__zoom, te = typeof I == "function" ? I.apply(T, P) : I, ae = u(Z.invert(B).concat(G / Z.k), te.invert(B).concat(G / te.k));
      return function(J) {
        if (J === 1) J = te;
        else {
          var R = ae(J), q = G / R[2];
          J = new ot(q, B[0] - R[0] * q, B[1] - R[1] * q);
        }
        W.zoom(null, J);
      };
    });
  }
  function k(S, I, C) {
    return !C && S.__zooming || new _(S, I);
  }
  function _(S, I) {
    this.that = S, this.args = I, this.active = 0, this.sourceEvent = null, this.extent = t.apply(S, I), this.taps = 0;
  }
  _.prototype = {
    event: function(S) {
      return S && (this.sourceEvent = S), this;
    },
    start: function() {
      return ++this.active === 1 && (this.that.__zooming = this, this.emit("start")), this;
    },
    zoom: function(S, I) {
      return this.mouse && S !== "mouse" && (this.mouse[1] = I.invert(this.mouse[0])), this.touch0 && S !== "touch" && (this.touch0[1] = I.invert(this.touch0[0])), this.touch1 && S !== "touch" && (this.touch1[1] = I.invert(this.touch1[0])), this.that.__zoom = I, this.emit("zoom"), this;
    },
    end: function() {
      return --this.active === 0 && (delete this.that.__zooming, this.emit("end")), this;
    },
    emit: function(S) {
      var I = Re(this.that).datum();
      c.call(
        S,
        this.that,
        new lh(S, {
          sourceEvent: this.sourceEvent,
          target: y,
          transform: this.that.__zoom,
          dispatch: c
        }),
        I
      );
    }
  };
  function D(S, ...I) {
    if (!e.apply(this, arguments)) return;
    var C = k(this, I).event(S), A = this.__zoom, T = Math.max(i[0], Math.min(i[1], A.k * Math.pow(2, o.apply(this, arguments)))), P = We(S);
    if (C.wheel)
      (C.mouse[0][0] !== P[0] || C.mouse[0][1] !== P[1]) && (C.mouse[1] = A.invert(C.mouse[0] = P)), clearTimeout(C.wheel);
    else {
      if (A.k === T) return;
      C.mouse = [P, A.invert(P)], vo(this), C.start();
    }
    mn(S), C.wheel = setTimeout(W, m), C.zoom("mouse", n(p(b(A, T), C.mouse[0], C.mouse[1]), C.extent, s));
    function W() {
      C.wheel = null, C.end();
    }
  }
  function $(S, ...I) {
    if (h || !e.apply(this, arguments)) return;
    var C = S.currentTarget, A = k(this, I, !0).event(S), T = Re(S.view).on("mousemove.zoom", B, !0).on("mouseup.zoom", G, !0), P = We(S, C), W = S.clientX, O = S.clientY;
    Ca(S.view), jr(S), A.mouse = [P, this.__zoom.invert(P)], vo(this), A.start();
    function B(Z) {
      if (mn(Z), !A.moved) {
        var te = Z.clientX - W, ae = Z.clientY - O;
        A.moved = te * te + ae * ae > x;
      }
      A.event(Z).zoom("mouse", n(p(A.that.__zoom, A.mouse[0] = We(Z, C), A.mouse[1]), A.extent, s));
    }
    function G(Z) {
      T.on("mousemove.zoom mouseup.zoom", null), _a(Z.view, A.moved), mn(Z), A.event(Z).end();
    }
  }
  function F(S, ...I) {
    if (e.apply(this, arguments)) {
      var C = this.__zoom, A = We(S.changedTouches ? S.changedTouches[0] : S, this), T = C.invert(A), P = C.k * (S.shiftKey ? 0.5 : 2), W = n(p(b(C, P), A, T), t.apply(this, I), s);
      mn(S), l > 0 ? Re(this).transition().duration(l).call(E, W, A, S) : Re(this).call(y.transform, W, A, S);
    }
  }
  function j(S, ...I) {
    if (e.apply(this, arguments)) {
      var C = S.touches, A = C.length, T = k(this, I, S.changedTouches.length === A).event(S), P, W, O, B;
      for (jr(S), W = 0; W < A; ++W)
        O = C[W], B = We(O, this), B = [B, this.__zoom.invert(B), O.identifier], T.touch0 ? !T.touch1 && T.touch0[2] !== B[2] && (T.touch1 = B, T.taps = 0) : (T.touch0 = B, P = !0, T.taps = 1 + !!d);
      d && (d = clearTimeout(d)), P && (T.taps < 2 && (f = B[0], d = setTimeout(function() {
        d = null;
      }, g)), vo(this), T.start());
    }
  }
  function z(S, ...I) {
    if (this.__zooming) {
      var C = k(this, I).event(S), A = S.changedTouches, T = A.length, P, W, O, B;
      for (mn(S), P = 0; P < T; ++P)
        W = A[P], O = We(W, this), C.touch0 && C.touch0[2] === W.identifier ? C.touch0[0] = O : C.touch1 && C.touch1[2] === W.identifier && (C.touch1[0] = O);
      if (W = C.that.__zoom, C.touch1) {
        var G = C.touch0[0], Z = C.touch0[1], te = C.touch1[0], ae = C.touch1[1], J = (J = te[0] - G[0]) * J + (J = te[1] - G[1]) * J, R = (R = ae[0] - Z[0]) * R + (R = ae[1] - Z[1]) * R;
        W = b(W, Math.sqrt(J / R)), O = [(G[0] + te[0]) / 2, (G[1] + te[1]) / 2], B = [(Z[0] + ae[0]) / 2, (Z[1] + ae[1]) / 2];
      } else if (C.touch0) O = C.touch0[0], B = C.touch0[1];
      else return;
      C.zoom("touch", n(p(W, O, B), C.extent, s));
    }
  }
  function H(S, ...I) {
    if (this.__zooming) {
      var C = k(this, I).event(S), A = S.changedTouches, T = A.length, P, W;
      for (jr(S), h && clearTimeout(h), h = setTimeout(function() {
        h = null;
      }, g), P = 0; P < T; ++P)
        W = A[P], C.touch0 && C.touch0[2] === W.identifier ? delete C.touch0 : C.touch1 && C.touch1[2] === W.identifier && delete C.touch1;
      if (C.touch1 && !C.touch0 && (C.touch0 = C.touch1, delete C.touch1), C.touch0) C.touch0[1] = this.__zoom.invert(C.touch0[0]);
      else if (C.end(), C.taps === 2 && (W = We(W, this), Math.hypot(f[0] - W[0], f[1] - W[1]) < v)) {
        var O = Re(this).on("dblclick.zoom");
        O && O.apply(this, arguments);
      }
    }
  }
  return y.wheelDelta = function(S) {
    return arguments.length ? (o = typeof S == "function" ? S : co(+S), y) : o;
  }, y.filter = function(S) {
    return arguments.length ? (e = typeof S == "function" ? S : co(!!S), y) : e;
  }, y.touchable = function(S) {
    return arguments.length ? (r = typeof S == "function" ? S : co(!!S), y) : r;
  }, y.extent = function(S) {
    return arguments.length ? (t = typeof S == "function" ? S : co([[+S[0][0], +S[0][1]], [+S[1][0], +S[1][1]]]), y) : t;
  }, y.scaleExtent = function(S) {
    return arguments.length ? (i[0] = +S[0], i[1] = +S[1], y) : [i[0], i[1]];
  }, y.translateExtent = function(S) {
    return arguments.length ? (s[0][0] = +S[0][0], s[1][0] = +S[1][0], s[0][1] = +S[0][1], s[1][1] = +S[1][1], y) : [[s[0][0], s[0][1]], [s[1][0], s[1][1]]];
  }, y.constrain = function(S) {
    return arguments.length ? (n = S, y) : n;
  }, y.duration = function(S) {
    return arguments.length ? (l = +S, y) : l;
  }, y.interpolate = function(S) {
    return arguments.length ? (u = S, y) : u;
  }, y.on = function() {
    var S = c.on.apply(c, arguments);
    return S === c ? y : S;
  }, y.clickDistance = function(S) {
    return arguments.length ? (x = (S = +S) * S, y) : Math.sqrt(x);
  }, y.tapDistance = function(S) {
    return arguments.length ? (v = +S, y) : v;
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
}, Mn = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], Wa = ["Enter", " ", "Escape"], Xa = {
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
var Ot;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(Ot || (Ot = {}));
var Nt;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(Nt || (Nt = {}));
var Dn;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Dn || (Dn = {}));
const Ya = {
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
var ut;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(ut || (ut = {}));
var Mo;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(Mo || (Mo = {}));
var ee;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(ee || (ee = {}));
const ts = {
  [ee.Left]: ee.Right,
  [ee.Right]: ee.Left,
  [ee.Top]: ee.Bottom,
  [ee.Bottom]: ee.Top
};
function qa(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Za = (e) => "id" in e && "source" in e && "target" in e, gh = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), gi = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), Vn = (e, t = [0, 0]) => {
  const { width: n, height: o } = st(e), r = e.origin ?? t, i = n * r[0], s = o * r[1];
  return {
    x: e.position.x - i,
    y: e.position.y - s
  };
}, mh = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const i = typeof r == "string";
    let s = !t.nodeLookup && !i ? r : void 0;
    t.nodeLookup && (s = i ? t.nodeLookup.get(r) : gi(r) ? r : t.nodeLookup.get(r.id));
    const l = s ? Do(s, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return Wo(o, l);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return Xo(n);
}, On = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = Wo(n, Do(r)), o = !0);
  }), o ? Xo(n) : { x: 0, y: 0, width: 0, height: 0 };
}, mi = (e, t, [n, o, r] = [0, 0, 1], i = !1, s = !1) => {
  const l = {
    ...Ut(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, u = [];
  for (const c of e.values()) {
    const { measured: d, selectable: f = !0, hidden: h = !1 } = c;
    if (s && !f || h)
      continue;
    const g = d.width ?? c.width ?? c.initialWidth ?? null, m = d.height ?? c.height ?? c.initialHeight ?? null, x = Pn(l, Ft(c)), v = (g ?? 0) * (m ?? 0), y = i && x > 0;
    (!c.internals.handleBounds || y || x >= v || c.dragging) && u.push(c);
  }
  return u;
}, yh = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function xh(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function wh({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: i }, s) {
  if (e.size === 0)
    return !0;
  const l = xh(e, s), u = On(l), c = xi(u, t, n, s?.minZoom ?? r, s?.maxZoom ?? i, s?.padding ?? 0.1);
  return await o.setViewport(c, {
    duration: s?.duration,
    ease: s?.ease,
    interpolate: s?.interpolate
  }), !0;
}
function Ka({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: i }) {
  const s = n.get(e), l = s.parentId ? n.get(s.parentId) : void 0, { x: u, y: c } = l ? l.internals.positionAbsolute : { x: 0, y: 0 }, d = s.origin ?? o;
  let f = s.extent || r;
  if (s.extent === "parent" && !s.expandParent)
    if (!l)
      i?.("005", He.error005());
    else {
      const g = l.measured.width, m = l.measured.height;
      g && m && (f = [
        [u, c],
        [u + g, c + m]
      ]);
    }
  else l && _t(s.extent) && (f = [
    [s.extent[0][0] + u, s.extent[0][1] + c],
    [s.extent[1][0] + u, s.extent[1][1] + c]
  ]);
  const h = _t(f) ? Ct(t, f, s.measured) : t;
  return (s.measured.width === void 0 || s.measured.height === void 0) && i?.("015", He.error015()), {
    position: {
      x: h.x - u + (s.measured.width ?? 0) * d[0],
      y: h.y - c + (s.measured.height ?? 0) * d[1]
    },
    positionAbsolute: h
  };
}
async function vh({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const i = new Set(e.map((h) => h.id)), s = [];
  for (const h of n) {
    if (h.deletable === !1)
      continue;
    const g = i.has(h.id), m = !g && h.parentId && s.find((x) => x.id === h.parentId);
    (g || m) && s.push(h);
  }
  const l = new Set(t.map((h) => h.id)), u = o.filter((h) => h.deletable !== !1), d = yh(s, u);
  for (const h of u)
    l.has(h.id) && !d.find((m) => m.id === h.id) && d.push(h);
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
const Bt = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), Ct = (e = { x: 0, y: 0 }, t, n) => ({
  x: Bt(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: Bt(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Ua(e, t, n) {
  const { width: o, height: r } = st(n), { x: i, y: s } = n.internals.positionAbsolute;
  return Ct(e, [
    [i, s],
    [i + o, s + r]
  ], t);
}
const ns = (e, t, n) => e < t ? Bt(Math.abs(e - t), 1, t) / t : e > n ? -Bt(Math.abs(e - n), 1, t) / t : 0, yi = (e, t, n = 15, o = 40) => {
  const r = ns(e.x, o, t.width - o) * n, i = ns(e.y, o, t.height - o) * n;
  return [r, i];
}, Wo = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), Gr = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), Xo = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), Ft = (e, t = [0, 0]) => {
  const { x: n, y: o } = gi(e) ? e.internals.positionAbsolute : Vn(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, Do = (e, t = [0, 0]) => {
  const { x: n, y: o } = gi(e) ? e.internals.positionAbsolute : Vn(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, Ga = (e, t) => Xo(Wo(Gr(e), Gr(t))), Pn = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, os = (e) => Ye(e.width) && Ye(e.height) && Ye(e.x) && Ye(e.y), Ye = (e) => !isNaN(e) && isFinite(e), Ja = (e, t) => (n, o) => {
}, Bn = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), Ut = ({ x: e, y: t }, [n, o, r], i = !1, s = [1, 1]) => {
  const l = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return i ? Bn(l, s) : l;
}, Wt = ({ x: e, y: t }, [n, o, r]) => ({
  x: e * r + n,
  y: t * r + o
});
function Pt(e, t) {
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
function bh(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = Pt(e, n), r = Pt(e, t);
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
    const o = Pt(e.top ?? e.y ?? 0, n), r = Pt(e.bottom ?? e.y ?? 0, n), i = Pt(e.left ?? e.x ?? 0, t), s = Pt(e.right ?? e.x ?? 0, t);
    return { top: o, right: s, bottom: r, left: i, x: i + s, y: o + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function Sh(e, t, n, o, r, i) {
  const { x: s, y: l } = Wt(e, [t, n, o]), { x: u, y: c } = Wt({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), d = r - u, f = i - c;
  return {
    left: Math.floor(s),
    top: Math.floor(l),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const xi = (e, t, n, o, r, i) => {
  const s = bh(i, t, n), l = (t - s.x) / e.width, u = (n - s.y) / e.height, c = Math.min(l, u), d = Bt(c, o, r), f = e.x + e.width / 2, h = e.y + e.height / 2, g = t / 2 - f * d, m = n / 2 - h * d, x = Sh(e, g, m, d, t, n), v = {
    left: Math.min(x.left - s.left, 0),
    top: Math.min(x.top - s.top, 0),
    right: Math.min(x.right - s.right, 0),
    bottom: Math.min(x.bottom - s.bottom, 0)
  };
  return {
    x: g - v.left + v.right,
    y: m - v.top + v.bottom,
    zoom: d
  };
}, $n = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function _t(e) {
  return e != null && e !== "parent";
}
function st(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function Qa(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function ec(e, t = { width: 0, height: 0 }, n, o, r) {
  const i = { ...e }, s = o.get(n);
  if (s) {
    const l = s.origin || r;
    i.x += s.internals.positionAbsolute.x - (t.width ?? 0) * l[0], i.y += s.internals.positionAbsolute.y - (t.height ?? 0) * l[1];
  }
  return i;
}
function rs(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function Nh() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function Eh(e) {
  return { ...Xa, ...e || {} };
}
function Cn(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: i, y: s } = qe(e), l = Ut({ x: i - (r?.left ?? 0), y: s - (r?.top ?? 0) }, o), { x: u, y: c } = n ? Bn(l, t) : l;
  return {
    xSnapped: u,
    ySnapped: c,
    ...l
  };
}
const wi = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), tc = (e) => e?.getRootNode?.() || window?.document, kh = ["INPUT", "SELECT", "TEXTAREA"];
function nc(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : kh.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const oc = (e) => "clientX" in e, qe = (e, t) => {
  const n = oc(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, is = (e, t, n, o, r) => {
  const i = t.querySelectorAll(`.${e}`);
  return !i || !i.length ? null : Array.from(i).map((s) => {
    const l = s.getBoundingClientRect();
    return {
      id: s.getAttribute("data-handleid"),
      type: e,
      nodeId: r,
      position: s.getAttribute("data-handlepos"),
      x: (l.left - n.left) / o,
      y: (l.top - n.top) / o,
      ...wi(s)
    };
  });
};
function rc({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: i, targetControlX: s, targetControlY: l }) {
  const u = e * 0.125 + r * 0.375 + s * 0.375 + n * 0.125, c = t * 0.125 + i * 0.375 + l * 0.375 + o * 0.125, d = Math.abs(u - e), f = Math.abs(c - t);
  return [u, c, d, f];
}
function lo(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function ss({ pos: e, x1: t, y1: n, x2: o, y2: r, c: i }) {
  switch (e) {
    case ee.Left:
      return [t - lo(t - o, i), n];
    case ee.Right:
      return [t + lo(o - t, i), n];
    case ee.Top:
      return [t, n - lo(n - r, i)];
    case ee.Bottom:
      return [t, n + lo(r - n, i)];
  }
}
function ic({ sourceX: e, sourceY: t, sourcePosition: n = ee.Bottom, targetX: o, targetY: r, targetPosition: i = ee.Top, curvature: s = 0.25 }) {
  const [l, u] = ss({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: s
  }), [c, d] = ss({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: s
  }), [f, h, g, m] = rc({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: r,
    sourceControlX: l,
    sourceControlY: u,
    targetControlX: c,
    targetControlY: d
  });
  return [
    `M${e},${t} C${l},${u} ${c},${d} ${o},${r}`,
    f,
    h,
    g,
    m
  ];
}
function sc({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, i = n < e ? n + r : n - r, s = Math.abs(o - t) / 2, l = o < t ? o + s : o - s;
  return [i, l, r, s];
}
function Ch({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: i = "basic" }) {
  if (i === "manual")
    return o;
  const s = r && n ? o + 1e3 : o, l = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return s + l;
}
function _h({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const i = Wo(Do(e), Do(t));
  i.x === i.x2 && (i.x2 += 1), i.y === i.y2 && (i.y2 += 1);
  const s = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return Pn(s, Xo(i)) > 0;
}
const ac = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, Ih = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), jh = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", He.error006()), t;
  const o = n.getEdgeId || ac;
  let r;
  return Za(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, Ih(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, Ah = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: r, ...i } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", He.error006()), n;
  if (!n.find((c) => c.id === e.id))
    return o.onError?.("007", He.error007(r)), n;
  const l = o.getEdgeId || ac, u = {
    ...i,
    id: o.shouldReplaceId ? l(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((c) => c.id !== r).concat(u);
};
function cc({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, i, s, l] = sc({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, i, s, l];
}
const as = {
  [ee.Left]: { x: -1, y: 0 },
  [ee.Right]: { x: 1, y: 0 },
  [ee.Top]: { x: 0, y: -1 },
  [ee.Bottom]: { x: 0, y: 1 }
}, Mh = ({ source: e, sourcePosition: t = ee.Bottom, target: n }) => t === ee.Left || t === ee.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, cs = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Dh({ source: e, sourcePosition: t = ee.Bottom, target: n, targetPosition: o = ee.Top, center: r, offset: i, stepPosition: s }) {
  const l = as[t], u = as[o], c = { x: e.x + l.x * i, y: e.y + l.y * i }, d = { x: n.x + u.x * i, y: n.y + u.y * i }, f = Mh({
    source: c,
    sourcePosition: t,
    target: d
  }), h = f.x !== 0 ? "x" : "y", g = f[h];
  let m = [], x, v;
  const y = { x: 0, y: 0 }, b = { x: 0, y: 0 }, [, , p, w] = sc({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (l[h] * u[h] === -1) {
    h === "x" ? (x = r.x ?? c.x + (d.x - c.x) * s, v = r.y ?? (c.y + d.y) / 2) : (x = r.x ?? (c.x + d.x) / 2, v = r.y ?? c.y + (d.y - c.y) * s);
    const D = [
      { x, y: c.y },
      { x, y: d.y }
    ], $ = [
      { x: c.x, y: v },
      { x: d.x, y: v }
    ];
    l[h] === g ? m = h === "x" ? D : $ : m = h === "x" ? $ : D;
  } else {
    const D = [{ x: c.x, y: d.y }], $ = [{ x: d.x, y: c.y }];
    if (h === "x" ? m = l.x === g ? $ : D : m = l.y === g ? D : $, t === o) {
      const S = Math.abs(e[h] - n[h]);
      if (S <= i) {
        const I = Math.min(i - 1, i - S);
        l[h] === g ? y[h] = (c[h] > e[h] ? -1 : 1) * I : b[h] = (d[h] > n[h] ? -1 : 1) * I;
      }
    }
    if (t !== o) {
      const S = h === "x" ? "y" : "x", I = l[h] === u[S], C = c[S] > d[S], A = c[S] < d[S];
      (l[h] === 1 && (!I && C || I && A) || l[h] !== 1 && (!I && A || I && C)) && (m = h === "x" ? D : $);
    }
    const F = { x: c.x + y.x, y: c.y + y.y }, j = { x: d.x + b.x, y: d.y + b.y }, z = Math.max(Math.abs(F.x - m[0].x), Math.abs(j.x - m[0].x)), H = Math.max(Math.abs(F.y - m[0].y), Math.abs(j.y - m[0].y));
    z >= H ? (x = (F.x + j.x) / 2, v = m[0].y) : (x = m[0].x, v = (F.y + j.y) / 2);
  }
  const E = { x: c.x + y.x, y: c.y + y.y }, k = { x: d.x + b.x, y: d.y + b.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...E.x !== m[0].x || E.y !== m[0].y ? [E] : [],
    ...m,
    ...k.x !== m[m.length - 1].x || k.y !== m[m.length - 1].y ? [k] : [],
    n
  ], x, v, p, w];
}
function Ph(e, t, n, o) {
  const r = Math.min(cs(e, t) / 2, cs(t, n) / 2, o), { x: i, y: s } = t;
  if (e.x === i && i === n.x || e.y === s && s === n.y)
    return `L${i} ${s}`;
  if (e.y === s) {
    const c = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${i + r * c},${s}Q ${i},${s} ${i},${s + r * d}`;
  }
  const l = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${i},${s + r * u}Q ${i},${s} ${i + r * l},${s}`;
}
function Po({ sourceX: e, sourceY: t, sourcePosition: n = ee.Bottom, targetX: o, targetY: r, targetPosition: i = ee.Top, borderRadius: s = 5, centerX: l, centerY: u, offset: c = 20, stepPosition: d = 0.5 }) {
  const [f, h, g, m, x] = Dh({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: i,
    center: { x: l, y: u },
    offset: c,
    stepPosition: d
  });
  let v = `M${f[0].x} ${f[0].y}`;
  for (let y = 1; y < f.length - 1; y++)
    v += Ph(f[y - 1], f[y], f[y + 1], s);
  return v += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [v, h, g, m, x];
}
function ls(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function $h(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!ls(t) || !ls(n))
    return null;
  const o = t.internals.handleBounds || us(t.handles), r = n.internals.handleBounds || us(n.handles), i = ds(o?.source ?? [], e.sourceHandle), s = ds(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === Ot.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!i || !s)
    return e.onError?.("008", He.error008(i ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const l = i?.position || ee.Bottom, u = s?.position || ee.Top, c = It(t, i, l), d = It(n, s, u);
  return {
    sourceX: c.x,
    sourceY: c.y,
    targetX: d.x,
    targetY: d.y,
    sourcePosition: l,
    targetPosition: u
  };
}
function us(e) {
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
function It(e, t, n = ee.Left, o = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, i = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: s, height: l } = t ?? st(e);
  if (o)
    return { x: r + s / 2, y: i + l / 2 };
  switch (t?.position ?? n) {
    case ee.Top:
      return { x: r + s / 2, y: i };
    case ee.Right:
      return { x: r + s, y: i + l / 2 };
    case ee.Bottom:
      return { x: r + s / 2, y: i + l };
    case ee.Left:
      return { x: r, y: i + l / 2 };
  }
}
function ds(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Jr(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function Th(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const i = /* @__PURE__ */ new Set();
  return e.reduce((s, l) => ([l.markerStart || o, l.markerEnd || r].forEach((u) => {
    if (u && typeof u == "object") {
      const c = Jr(u, t);
      i.has(c) || (s.push({ id: c, color: u.color || n, ...u }), i.add(c));
    }
  }), s), []).sort((s, l) => s.id.localeCompare(l.id));
}
const lc = 1e3, zh = 10, vi = {
  nodeOrigin: [0, 0],
  nodeExtent: Mn,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Rh = {
  ...vi,
  checkEquality: !0
};
function bi(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function Lh(e, t, n) {
  const o = bi(vi, n);
  for (const r of e.values())
    if (r.parentId)
      Ni(r, e, t, o);
    else {
      const i = Vn(r, o.nodeOrigin), s = _t(r.extent) ? r.extent : o.nodeExtent, l = Ct(i, s, st(r));
      r.internals.positionAbsolute = l;
    }
}
function Hh(e, t) {
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
function Si(e) {
  return e === "manual";
}
function Qr(e, t, n, o = {}) {
  const r = bi(Rh, o), i = { i: 0 }, s = new Map(t), l = r?.elevateNodesOnSelect && !Si(r.zIndexMode) ? lc : 0;
  let u = e.length > 0, c = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = s.get(d.id);
    if (r.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const h = Vn(d, r.nodeOrigin), g = _t(d.extent) ? d.extent : r.nodeExtent, m = Ct(h, g, st(d));
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
          handleBounds: Hh(d, f),
          z: uc(d, l, r.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (u = !1), d.parentId && Ni(f, t, n, o, i), c ||= d.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: c };
}
function Vh(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function Ni(e, t, n, o, r) {
  const { elevateNodesOnSelect: i, nodeOrigin: s, nodeExtent: l, zIndexMode: u } = bi(vi, o), c = e.parentId, d = t.get(c);
  if (!d) {
    console.warn(`Parent node ${c} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Vh(e, n), r && !d.parentId && d.internals.rootParentIndex === void 0 && u === "auto" && (d.internals.rootParentIndex = ++r.i, d.internals.z = d.internals.z + r.i * zh), r && d.internals.rootParentIndex !== void 0 && (r.i = d.internals.rootParentIndex);
  const f = i && !Si(u) ? lc : 0, { x: h, y: g, z: m } = Oh(e, d, s, l, f, u), { positionAbsolute: x } = e.internals, v = h !== x.x || g !== x.y;
  (v || m !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: v ? { x: h, y: g } : x,
      z: m
    }
  });
}
function uc(e, t, n) {
  const o = Ye(e.zIndex) ? e.zIndex : 0;
  return Si(n) ? o : o + (e.selected ? t : 0);
}
function Oh(e, t, n, o, r, i) {
  const { x: s, y: l } = t.internals.positionAbsolute, u = st(e), c = Vn(e, n), d = _t(e.extent) ? Ct(c, e.extent, u) : c;
  let f = Ct({ x: s + d.x, y: l + d.y }, o, u);
  e.extent === "parent" && (f = Ua(f, u, t));
  const h = uc(e, r, i), g = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: g >= h ? g + 1 : h
  };
}
function Ei(e, t, n, o = [0, 0]) {
  const r = [], i = /* @__PURE__ */ new Map();
  for (const s of e) {
    const l = t.get(s.parentId);
    if (!l)
      continue;
    const u = i.get(s.parentId)?.expandedRect ?? Ft(l), c = Ga(u, s.rect);
    i.set(s.parentId, { expandedRect: c, parent: l });
  }
  return i.size > 0 && i.forEach(({ expandedRect: s, parent: l }, u) => {
    const c = l.internals.positionAbsolute, d = st(l), f = l.origin ?? o, h = s.x < c.x ? Math.round(Math.abs(c.x - s.x)) : 0, g = s.y < c.y ? Math.round(Math.abs(c.y - s.y)) : 0, m = Math.max(d.width, Math.round(s.width)), x = Math.max(d.height, Math.round(s.height)), v = (m - d.width) * f[0], y = (x - d.height) * f[1];
    (h > 0 || g > 0 || v || y) && (r.push({
      id: u,
      type: "position",
      position: {
        x: l.position.x - h + v,
        y: l.position.y - g + y
      }
    }), n.get(u)?.forEach((b) => {
      e.some((p) => p.id === b.id) || r.push({
        id: b.id,
        type: "position",
        position: {
          x: b.position.x + h,
          y: b.position.y + g
        }
      });
    })), (d.width < s.width || d.height < s.height || h || g) && r.push({
      id: u,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: m + (h ? f[0] * h - v : 0),
        height: x + (g ? f[1] * g - y : 0)
      }
    });
  }), r;
}
function Bh(e, t, n, o, r, i, s) {
  const l = o?.querySelector(".xyflow__viewport");
  let u = !1;
  if (!l)
    return { changes: [], updatedInternals: u };
  const c = [], d = window.getComputedStyle(l), { m22: f } = new window.DOMMatrixReadOnly(d.transform), h = [];
  for (const g of e.values()) {
    const m = t.get(g.id);
    if (!m)
      continue;
    if (m.hidden) {
      t.set(m.id, {
        ...m,
        internals: {
          ...m.internals,
          handleBounds: void 0
        }
      }), u = !0;
      continue;
    }
    const x = wi(g.nodeElement), v = m.measured.width !== x.width || m.measured.height !== x.height;
    if (!!(x.width && x.height && (v || !m.internals.handleBounds || g.force))) {
      const b = g.nodeElement.getBoundingClientRect(), p = _t(m.extent) ? m.extent : i;
      let { positionAbsolute: w } = m.internals;
      m.parentId && m.extent === "parent" ? w = Ua(w, x, t.get(m.parentId)) : p && (w = Ct(w, p, x));
      const E = {
        ...m,
        measured: x,
        internals: {
          ...m.internals,
          positionAbsolute: w,
          handleBounds: {
            source: is("source", g.nodeElement, b, f, m.id),
            target: is("target", g.nodeElement, b, f, m.id)
          }
        }
      };
      t.set(m.id, E), m.parentId && Ni(E, t, n, { nodeOrigin: r, zIndexMode: s }), u = !0, v && (c.push({
        id: m.id,
        type: "dimensions",
        dimensions: x
      }), m.expandParent && m.parentId && h.push({
        id: m.id,
        parentId: m.parentId,
        rect: Ft(E, r)
      }));
    }
  }
  if (h.length > 0) {
    const g = Ei(h, t, n, r);
    c.push(...g);
  }
  return { changes: c, updatedInternals: u };
}
async function Fh({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: i }) {
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
function fs(e, t, n, o, r, i) {
  let s = r;
  const l = o.get(s) || /* @__PURE__ */ new Map();
  o.set(s, l.set(n, t)), s = `${r}-${e}`;
  const u = o.get(s) || /* @__PURE__ */ new Map();
  if (o.set(s, u.set(n, t)), i) {
    s = `${r}-${e}-${i}`;
    const c = o.get(s) || /* @__PURE__ */ new Map();
    o.set(s, c.set(n, t));
  }
}
function dc(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: i, sourceHandle: s = null, targetHandle: l = null } = o, u = { edgeId: o.id, source: r, target: i, sourceHandle: s, targetHandle: l }, c = `${r}-${s}--${i}-${l}`, d = `${i}-${l}--${r}-${s}`;
    fs("source", u, d, e, r, s), fs("target", u, c, e, i, l), t.set(o.id, o);
  }
}
function fc(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : fc(n, t) : !1;
}
function hs(e, t, n) {
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
function Wh(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [i, s] of e)
    if ((s.selected || s.id === o) && (!s.parentId || !fc(s, e)) && (s.draggable || t && typeof s.draggable > "u")) {
      const l = e.get(i);
      l && r.set(i, {
        id: i,
        position: l.position || { x: 0, y: 0 },
        distance: {
          x: n.x - l.internals.positionAbsolute.x,
          y: n.y - l.internals.positionAbsolute.y
        },
        extent: l.extent,
        parentId: l.parentId,
        origin: l.origin,
        expandParent: l.expandParent,
        internals: {
          positionAbsolute: l.internals.positionAbsolute || { x: 0, y: 0 }
        },
        measured: {
          width: l.measured.width ?? 0,
          height: l.measured.height ?? 0
        }
      });
    }
  return r;
}
function Ar({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
  const r = [];
  for (const [s, l] of t) {
    const u = n.get(s)?.internals.userNode;
    u && r.push({
      ...u,
      position: l.position,
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
function Xh({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const i = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, s = Bn(i, t);
  return {
    x: s.x - i.x,
    y: s.y - i.y
  };
}
function Yh({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let i = { x: null, y: null }, s = 0, l = /* @__PURE__ */ new Map(), u = !1, c = { x: 0, y: 0 }, d = null, f = !1, h = null, g = !1, m = !1, x = null;
  function v({ noDragClassName: b, handleSelector: p, domNode: w, isSelectable: E, nodeId: k, nodeClickDistance: _ = 0 }) {
    h = Re(w);
    function D({ x: z, y: H }) {
      const { nodeLookup: S, nodeExtent: I, snapGrid: C, snapToGrid: A, nodeOrigin: T, onNodeDrag: P, onSelectionDrag: W, onError: O, updateNodePositions: B } = t();
      i = { x: z, y: H };
      let G = !1;
      const Z = l.size > 1, te = Z && I ? Gr(On(l)) : null, ae = Z && A ? Xh({
        dragItems: l,
        snapGrid: C,
        x: z,
        y: H
      }) : null;
      for (const [J, R] of l) {
        if (!S.has(J))
          continue;
        let q = { x: z - R.distance.x, y: H - R.distance.y };
        A && (q = ae ? {
          x: Math.round(q.x + ae.x),
          y: Math.round(q.y + ae.y)
        } : Bn(q, C));
        let re = null;
        if (Z && I && !R.extent && te) {
          const { positionAbsolute: Q } = R.internals, se = Q.x - te.x + I[0][0], L = Q.x + R.measured.width - te.x2 + I[1][0], K = Q.y - te.y + I[0][1], fe = Q.y + R.measured.height - te.y2 + I[1][1];
          re = [
            [se, K],
            [L, fe]
          ];
        }
        const { position: oe, positionAbsolute: U } = Ka({
          nodeId: J,
          nextPosition: q,
          nodeLookup: S,
          nodeExtent: re || I,
          nodeOrigin: T,
          onError: O
        });
        G = G || R.position.x !== oe.x || R.position.y !== oe.y, R.position = oe, R.internals.positionAbsolute = U;
      }
      if (m = m || G, !!G && (B(l, !0), x && (o || P || !k && W))) {
        const [J, R] = Ar({
          nodeId: k,
          dragItems: l,
          nodeLookup: S
        });
        o?.(x, l, J, R), P?.(x, J, R), k || W?.(x, R);
      }
    }
    async function $() {
      if (!d)
        return;
      const { transform: z, panBy: H, autoPanSpeed: S, autoPanOnNodeDrag: I } = t();
      if (!I) {
        u = !1, cancelAnimationFrame(s);
        return;
      }
      const [C, A] = yi(c, d, S);
      (C !== 0 || A !== 0) && (i.x = (i.x ?? 0) - C / z[2], i.y = (i.y ?? 0) - A / z[2], await H({ x: C, y: A }) && D(i)), s = requestAnimationFrame($);
    }
    function F(z) {
      const { nodeLookup: H, multiSelectionActive: S, nodesDraggable: I, transform: C, snapGrid: A, snapToGrid: T, selectNodesOnDrag: P, onNodeDragStart: W, onSelectionDragStart: O, unselectNodesAndEdges: B } = t();
      f = !0, (!P || !E) && !S && k && (H.get(k)?.selected || B()), E && P && k && e?.(k);
      const G = Cn(z.sourceEvent, { transform: C, snapGrid: A, snapToGrid: T, containerBounds: d });
      if (i = G, l = Wh(H, I, G, k), l.size > 0 && (n || W || !k && O)) {
        const [Z, te] = Ar({
          nodeId: k,
          dragItems: l,
          nodeLookup: H
        });
        n?.(z.sourceEvent, l, Z, te), W?.(z.sourceEvent, Z, te), k || O?.(z.sourceEvent, te);
      }
    }
    const j = Ia().clickDistance(_).on("start", (z) => {
      const { domNode: H, nodeDragThreshold: S, transform: I, snapGrid: C, snapToGrid: A } = t();
      d = H?.getBoundingClientRect() || null, g = !1, m = !1, x = z.sourceEvent, S === 0 && F(z), i = Cn(z.sourceEvent, { transform: I, snapGrid: C, snapToGrid: A, containerBounds: d }), c = qe(z.sourceEvent, d);
    }).on("drag", (z) => {
      const { autoPanOnNodeDrag: H, transform: S, snapGrid: I, snapToGrid: C, nodeDragThreshold: A, nodeLookup: T } = t(), P = Cn(z.sourceEvent, { transform: S, snapGrid: I, snapToGrid: C, containerBounds: d });
      if (x = z.sourceEvent, (z.sourceEvent.type === "touchmove" && z.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      k && !T.has(k)) && (g = !0), !g) {
        if (!u && H && f && (u = !0, $()), !f) {
          const W = qe(z.sourceEvent, d), O = W.x - c.x, B = W.y - c.y;
          Math.sqrt(O * O + B * B) > A && F(z);
        }
        (i.x !== P.xSnapped || i.y !== P.ySnapped) && l && f && (c = qe(z.sourceEvent, d), D(P));
      }
    }).on("end", (z) => {
      if (!f || g) {
        g && l.size > 0 && t().updateNodePositions(l, !1);
        return;
      }
      if (u = !1, f = !1, cancelAnimationFrame(s), l.size > 0) {
        const { nodeLookup: H, updateNodePositions: S, onNodeDragStop: I, onSelectionDragStop: C } = t();
        if (m && (S(l, !1), m = !1), r || I || !k && C) {
          const [A, T] = Ar({
            nodeId: k,
            dragItems: l,
            nodeLookup: H,
            dragging: !1
          });
          r?.(z.sourceEvent, l, A, T), I?.(z.sourceEvent, A, T), k || C?.(z.sourceEvent, T);
        }
      }
    }).filter((z) => {
      const H = z.target;
      return !z.button && (!b || !hs(H, `.${b}`, w)) && (!p || hs(H, p, w));
    });
    h.call(j);
  }
  function y() {
    h?.on(".drag", null);
  }
  return {
    update: v,
    destroy: y
  };
}
function qh(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const i of t.values())
    Pn(r, Ft(i)) > 0 && o.push(i);
  return o;
}
const Zh = 250;
function Kh(e, t, n, o) {
  let r = [], i = 1 / 0;
  const s = qh(e, n, t + Zh);
  for (const l of s) {
    const u = [...l.internals.handleBounds?.source ?? [], ...l.internals.handleBounds?.target ?? []];
    for (const c of u) {
      if (o.nodeId === c.nodeId && o.type === c.type && o.id === c.id)
        continue;
      const { x: d, y: f } = It(l, c, c.position, !0), h = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(f - e.y, 2));
      h > t || (h < i ? (r = [{ ...c, x: d, y: f }], i = h) : h === i && r.push({ ...c, x: d, y: f }));
    }
  }
  if (!r.length)
    return null;
  if (r.length > 1) {
    const l = o.type === "source" ? "target" : "source";
    return r.find((u) => u.type === l) ?? r[0];
  }
  return r[0];
}
function hc(e, t, n, o, r, i = !1) {
  const s = o.get(e);
  if (!s)
    return null;
  const l = r === "strict" ? s.internals.handleBounds?.[t] : [...s.internals.handleBounds?.source ?? [], ...s.internals.handleBounds?.target ?? []], u = (n ? l?.find((c) => c.id === n) : l?.[0]) ?? null;
  return u && i ? { ...u, ...It(s, u, u.position, !0) } : u;
}
function pc(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Uh(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const gc = () => !0;
function Gh(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: i, isTarget: s, domNode: l, nodeLookup: u, lib: c, autoPanOnConnect: d, flowId: f, panBy: h, cancelConnection: g, onConnectStart: m, onConnect: x, onConnectEnd: v, isValidConnection: y = gc, onReconnectEnd: b, updateConnection: p, getTransform: w, getFromHandle: E, autoPanSpeed: k, dragThreshold: _ = 1, handleDomNode: D }) {
  const $ = tc(e.target);
  let F = 0, j;
  const { x: z, y: H } = qe(e), S = pc(i, D), I = l?.getBoundingClientRect();
  let C = !1;
  if (!I || !S)
    return;
  const A = hc(r, S, o, u, t);
  if (!A)
    return;
  let T = qe(e, I), P = !1, W = null, O = !1, B = null;
  function G() {
    if (!d || !I)
      return;
    const [oe, U] = yi(T, I, k);
    h({ x: oe, y: U }), F = requestAnimationFrame(G);
  }
  const Z = {
    ...A,
    nodeId: r,
    type: S,
    position: A.position
  }, te = u.get(r);
  let J = {
    inProgress: !0,
    isValid: null,
    from: It(te, Z, ee.Left, !0),
    fromHandle: Z,
    fromPosition: Z.position,
    fromNode: te,
    to: T,
    toHandle: null,
    toPosition: ts[Z.position],
    toNode: null,
    pointer: T
  };
  function R() {
    C = !0, p(J), m?.(e, { nodeId: r, handleId: o, handleType: S });
  }
  _ === 0 && R();
  function q(oe) {
    if (!C) {
      const { x: fe, y: ue } = qe(oe), Ie = fe - z, Oe = ue - H;
      if (!(Ie * Ie + Oe * Oe > _ * _))
        return;
      R();
    }
    if (!E() || !Z) {
      re(oe);
      return;
    }
    const U = w();
    T = qe(oe, I), j = Kh(Ut(T, U, !1, [1, 1]), n, u, Z), P || (G(), P = !0);
    const Q = mc(oe, {
      handle: j,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: s ? "target" : "source",
      isValidConnection: y,
      doc: $,
      lib: c,
      flowId: f,
      nodeLookup: u
    });
    B = Q.handleDomNode, W = Q.connection, O = Uh(!!j, Q.isValid);
    const se = u.get(r), L = se ? It(se, Z, ee.Left, !0) : J.from, K = {
      ...J,
      from: L,
      isValid: O,
      to: Q.toHandle && O ? Wt({ x: Q.toHandle.x, y: Q.toHandle.y }, U) : T,
      toHandle: Q.toHandle,
      toPosition: O && Q.toHandle ? Q.toHandle.position : ts[Z.position],
      toNode: Q.toHandle ? u.get(Q.toHandle.nodeId) : null,
      pointer: T
    };
    p(K), J = K;
  }
  function re(oe) {
    if (!("touches" in oe && oe.touches.length > 0)) {
      if (C) {
        (j || B) && W && O && x?.(W);
        const { inProgress: U, ...Q } = J, se = {
          ...Q,
          toPosition: J.toHandle ? J.toPosition : null
        };
        v?.(oe, se), i && b?.(oe, se);
      }
      g(), cancelAnimationFrame(F), P = !1, O = !1, W = null, B = null, $.removeEventListener("mousemove", q), $.removeEventListener("mouseup", re), $.removeEventListener("touchmove", q), $.removeEventListener("touchend", re);
    }
  }
  $.addEventListener("mousemove", q), $.addEventListener("mouseup", re), $.addEventListener("touchmove", q), $.addEventListener("touchend", re);
}
function mc(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: i, doc: s, lib: l, flowId: u, isValidConnection: c = gc, nodeLookup: d }) {
  const f = i === "target", h = t ? s.querySelector(`.${l}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: g, y: m } = qe(e), x = s.elementFromPoint(g, m), v = x?.classList.contains(`${l}-flow__handle`) ? x : h, y = {
    handleDomNode: v,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (v) {
    const b = pc(void 0, v), p = v.getAttribute("data-nodeid"), w = v.getAttribute("data-handleid"), E = v.classList.contains("connectable"), k = v.classList.contains("connectableend");
    if (!p || !b)
      return y;
    const _ = {
      source: f ? p : o,
      sourceHandle: f ? w : r,
      target: f ? o : p,
      targetHandle: f ? r : w
    };
    y.connection = _;
    const $ = E && k && (n === Ot.Strict ? f && b === "source" || !f && b === "target" : p !== o || w !== r);
    y.isValid = $ && c(_), y.toHandle = hc(p, b, w, d, n, !0);
  }
  return y;
}
const ei = {
  onPointerDown: Gh,
  isValid: mc
};
function Jh({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = Re(e);
  function i({ translateExtent: l, width: u, height: c, zoomStep: d = 1, pannable: f = !0, zoomable: h = !0, inversePan: g = !1 }) {
    const m = (p) => {
      if (p.sourceEvent.type !== "wheel" || !t)
        return;
      const w = n(), E = p.sourceEvent.ctrlKey && $n() ? 10 : 1, k = -p.sourceEvent.deltaY * (p.sourceEvent.deltaMode === 1 ? 0.05 : p.sourceEvent.deltaMode ? 1 : 2e-3) * d, _ = w[2] * Math.pow(2, k * E);
      t.scaleTo(_);
    };
    let x = [0, 0];
    const v = (p) => {
      (p.sourceEvent.type === "mousedown" || p.sourceEvent.type === "touchstart") && (x = [
        p.sourceEvent.clientX ?? p.sourceEvent.touches[0].clientX,
        p.sourceEvent.clientY ?? p.sourceEvent.touches[0].clientY
      ]);
    }, y = (p) => {
      const w = n();
      if (p.sourceEvent.type !== "mousemove" && p.sourceEvent.type !== "touchmove" || !t)
        return;
      const E = [
        p.sourceEvent.clientX ?? p.sourceEvent.touches[0].clientX,
        p.sourceEvent.clientY ?? p.sourceEvent.touches[0].clientY
      ], k = [E[0] - x[0], E[1] - x[1]];
      x = E;
      const _ = o() * Math.max(w[2], Math.log(w[2])) * (g ? -1 : 1), D = {
        x: w[0] - k[0] * _,
        y: w[1] - k[1] * _
      }, $ = [
        [0, 0],
        [u, c]
      ];
      t.setViewportConstrained({
        x: D.x,
        y: D.y,
        zoom: w[2]
      }, $, l);
    }, b = Fa().on("start", v).on("zoom", f ? y : null).on("zoom.wheel", h ? m : null);
    r.call(b, {});
  }
  function s() {
    r.on("zoom", null);
  }
  return {
    update: i,
    destroy: s,
    pointer: We
  };
}
const Yo = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), Mr = ({ x: e, y: t, zoom: n }) => Fo.translate(e, t).scale(n), Tt = (e, t) => e.target.closest(`.${t}`), yc = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Qh = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, Dr = (e, t = 0, n = Qh, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, xc = (e) => {
  const t = e.ctrlKey && $n() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function ep({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: i, zoomOnPinch: s, onPanZoomStart: l, onPanZoom: u, onPanZoomEnd: c }) {
  return (d) => {
    if (Tt(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && s) {
      const v = We(d), y = xc(d), b = f * Math.pow(2, y);
      o.scaleTo(n, b, v, d);
      return;
    }
    const h = d.deltaMode === 1 ? 20 : 1;
    let g = r === Nt.Vertical ? 0 : d.deltaX * h, m = r === Nt.Horizontal ? 0 : d.deltaY * h;
    !$n() && d.shiftKey && r !== Nt.Vertical && (g = d.deltaY * h, m = 0), o.translateBy(
      n,
      -(g / f) * i,
      -(m / f) * i,
      // @ts-ignore
      { internal: !0 }
    );
    const x = Yo(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(d, x), e.panScrollTimeout = setTimeout(() => {
      c?.(d, x), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, l?.(d, x));
  };
}
function tp({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const i = o.type === "wheel", s = !t && i && !o.ctrlKey, l = Tt(o, e);
    if (o.ctrlKey && i && l && o.preventDefault(), s || l)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function np({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = Yo(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function op({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (i) => {
    e.usedRightMouseButton = !!(n && yc(t, e.mouseButton ?? 0)), i.sourceEvent?.sync || o([i.transform.x, i.transform.y, i.transform.k]), r && !i.sourceEvent?.internal && r?.(i.sourceEvent, Yo(i.transform));
  };
}
function rp({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: i }) {
  return (s) => {
    if (!s.sourceEvent?.internal && (e.isZoomingOrPanning = !1, i && yc(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && s.sourceEvent && i(s.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
      const l = Yo(s.transform);
      e.prevViewport = l, clearTimeout(e.timerId), e.timerId = setTimeout(
        () => {
          r?.(s.sourceEvent, l);
        },
        // we need a setTimeout for panOnScroll to suppress multiple end events fired during scroll
        n ? 150 : 0
      );
    }
  };
}
function ip({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: i, userSelectionActive: s, noWheelClassName: l, noPanClassName: u, lib: c, connectionInProgress: d }) {
  return (f) => {
    const h = e || t, g = n && f.ctrlKey, m = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (Tt(f, `${c}-flow__node`) || Tt(f, `${c}-flow__edge`)))
      return !0;
    if (!o && !h && !r && !i && !n || s || d && !m || Tt(f, l) && m || Tt(f, u) && (!m || r && m && !e) || !n && f.ctrlKey && m)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!h && !r && !g && m || !o && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(o) && !o.includes(f.button) && f.type === "mousedown")
      return !1;
    const x = Array.isArray(o) && o.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || m) && x;
  };
}
function sp({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: i, onPanZoomStart: s, onPanZoomEnd: l, onDraggingChange: u }) {
  const c = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = Fa().scaleExtent([t, n]).translateExtent(o), h = Re(e).call(f);
  b({
    x: r.x,
    y: r.y,
    zoom: Bt(r.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], o);
  const g = h.on("wheel.zoom"), m = h.on("dblclick.zoom");
  f.wheelDelta(xc);
  async function x(j, z) {
    return h ? new Promise((H) => {
      f?.interpolate(z?.interpolate === "linear" ? kn : yo).transform(Dr(h, z?.duration, z?.ease, () => H(!0)), j);
    }) : !1;
  }
  function v({ noWheelClassName: j, noPanClassName: z, onPaneContextMenu: H, userSelectionActive: S, panOnScroll: I, panOnDrag: C, panOnScrollMode: A, panOnScrollSpeed: T, preventScrolling: P, zoomOnPinch: W, zoomOnScroll: O, zoomOnDoubleClick: B, zoomActivationKeyPressed: G, lib: Z, onTransformChange: te, connectionInProgress: ae, paneClickDistance: J, selectionOnDrag: R }) {
    S && !c.isZoomingOrPanning && y();
    const q = I && !G && !S;
    f.clickDistance(R ? 1 / 0 : !Ye(J) || J < 0 ? 0 : J);
    const re = q ? ep({
      zoomPanValues: c,
      noWheelClassName: j,
      d3Selection: h,
      d3Zoom: f,
      panOnScrollMode: A,
      panOnScrollSpeed: T,
      zoomOnPinch: W,
      onPanZoomStart: s,
      onPanZoom: i,
      onPanZoomEnd: l
    }) : tp({
      noWheelClassName: j,
      preventScrolling: P,
      d3ZoomHandler: g
    });
    h.on("wheel.zoom", re, { passive: !1 });
    const oe = np({
      zoomPanValues: c,
      onDraggingChange: u,
      onPanZoomStart: s
    });
    f.on("start", oe);
    const U = op({
      zoomPanValues: c,
      panOnDrag: C,
      onPaneContextMenu: !!H,
      onPanZoom: i,
      onTransformChange: te
    });
    f.on("zoom", U);
    const Q = rp({
      zoomPanValues: c,
      panOnDrag: C,
      panOnScroll: I,
      onPaneContextMenu: H,
      onPanZoomEnd: l,
      onDraggingChange: u
    });
    f.on("end", Q);
    const se = ip({
      zoomActivationKeyPressed: G,
      panOnDrag: C,
      zoomOnScroll: O,
      panOnScroll: I,
      zoomOnDoubleClick: B,
      zoomOnPinch: W,
      userSelectionActive: S,
      noPanClassName: z,
      noWheelClassName: j,
      lib: Z,
      connectionInProgress: ae
    });
    f.filter(se), B ? h.on("dblclick.zoom", m) : h.on("dblclick.zoom", null);
  }
  function y() {
    f.on("zoom", null);
  }
  async function b(j, z, H) {
    const S = Mr(j), I = f?.constrain()(S, z, H);
    return I && await x(I), I;
  }
  async function p(j, z) {
    const H = Mr(j);
    return await x(H, z), H;
  }
  function w(j) {
    if (h) {
      const z = Mr(j), H = h.property("__zoom");
      (H.k !== j.zoom || H.x !== j.x || H.y !== j.y) && f?.transform(h, z, null, { sync: !0 });
    }
  }
  function E() {
    const j = h ? Ba(h.node()) : { x: 0, y: 0, k: 1 };
    return { x: j.x, y: j.y, zoom: j.k };
  }
  async function k(j, z) {
    return h ? new Promise((H) => {
      f?.interpolate(z?.interpolate === "linear" ? kn : yo).scaleTo(Dr(h, z?.duration, z?.ease, () => H(!0)), j);
    }) : !1;
  }
  async function _(j, z) {
    return h ? new Promise((H) => {
      f?.interpolate(z?.interpolate === "linear" ? kn : yo).scaleBy(Dr(h, z?.duration, z?.ease, () => H(!0)), j);
    }) : !1;
  }
  function D(j) {
    f?.scaleExtent(j);
  }
  function $(j) {
    f?.translateExtent(j);
  }
  function F(j) {
    const z = !Ye(j) || j < 0 ? 0 : j;
    f?.clickDistance(z);
  }
  return {
    update: v,
    destroy: y,
    setViewport: p,
    setViewportConstrained: b,
    getViewport: E,
    scaleTo: k,
    scaleBy: _,
    setScaleExtent: D,
    setTranslateExtent: $,
    syncViewport: w,
    setClickDistance: F
  };
}
var Xt;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(Xt || (Xt = {}));
function ap({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: i }) {
  const s = e - t, l = n - o, u = [s > 0 ? 1 : s < 0 ? -1 : 0, l > 0 ? 1 : l < 0 ? -1 : 0];
  return s && r && (u[0] = u[0] * -1), l && i && (u[1] = u[1] * -1), u;
}
function ps(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: r
  };
}
function ct(e, t) {
  return Math.max(0, t - e);
}
function lt(e, t) {
  return Math.max(0, e - t);
}
function uo(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function gs(e, t) {
  return e ? !t : t;
}
function cp(e, t, n, o, r, i, s, l) {
  let { affectsX: u, affectsY: c } = t;
  const { isHorizontal: d, isVertical: f } = t, h = d && f, { xSnapped: g, ySnapped: m } = n, { minWidth: x, maxWidth: v, minHeight: y, maxHeight: b } = o, { x: p, y: w, width: E, height: k, aspectRatio: _ } = e;
  let D = Math.floor(d ? g - e.pointerX : 0), $ = Math.floor(f ? m - e.pointerY : 0);
  const F = E + (u ? -D : D), j = k + (c ? -$ : $), z = -i[0] * E, H = -i[1] * k;
  let S = uo(F, x, v), I = uo(j, y, b);
  if (s) {
    let T = 0, P = 0;
    u && D < 0 ? T = ct(p + D + z, s[0][0]) : !u && D > 0 && (T = lt(p + F + z, s[1][0])), c && $ < 0 ? P = ct(w + $ + H, s[0][1]) : !c && $ > 0 && (P = lt(w + j + H, s[1][1])), S = Math.max(S, T), I = Math.max(I, P);
  }
  if (l) {
    let T = 0, P = 0;
    u && D > 0 ? T = lt(p + D, l[0][0]) : !u && D < 0 && (T = ct(p + F, l[1][0])), c && $ > 0 ? P = lt(w + $, l[0][1]) : !c && $ < 0 && (P = ct(w + j, l[1][1])), S = Math.max(S, T), I = Math.max(I, P);
  }
  if (r) {
    if (d) {
      const T = uo(F / _, y, b) * _;
      if (S = Math.max(S, T), s) {
        let P = 0;
        !u && !c || u && !c && h ? P = lt(w + H + F / _, s[1][1]) * _ : P = ct(w + H + (u ? D : -D) / _, s[0][1]) * _, S = Math.max(S, P);
      }
      if (l) {
        let P = 0;
        !u && !c || u && !c && h ? P = ct(w + F / _, l[1][1]) * _ : P = lt(w + (u ? D : -D) / _, l[0][1]) * _, S = Math.max(S, P);
      }
    }
    if (f) {
      const T = uo(j * _, x, v) / _;
      if (I = Math.max(I, T), s) {
        let P = 0;
        !u && !c || c && !u && h ? P = lt(p + j * _ + z, s[1][0]) / _ : P = ct(p + (c ? $ : -$) * _ + z, s[0][0]) / _, I = Math.max(I, P);
      }
      if (l) {
        let P = 0;
        !u && !c || c && !u && h ? P = ct(p + j * _, l[1][0]) / _ : P = lt(p + (c ? $ : -$) * _, l[0][0]) / _, I = Math.max(I, P);
      }
    }
  }
  $ = $ + ($ < 0 ? I : -I), D = D + (D < 0 ? S : -S), r && (h ? F > j * _ ? $ = (gs(u, c) ? -D : D) / _ : D = (gs(u, c) ? -$ : $) * _ : d ? ($ = D / _, c = u) : (D = $ * _, u = c));
  const C = u ? p + D : p, A = c ? w + $ : w;
  return {
    width: E + (u ? -D : D),
    height: k + (c ? -$ : $),
    x: i[0] * D * (u ? -1 : 1) + C,
    y: i[1] * $ * (c ? -1 : 1) + A
  };
}
const wc = { width: 0, height: 0, x: 0, y: 0 }, lp = {
  ...wc,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function up(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, i = e.measured.width ?? 0, s = e.measured.height ?? 0, l = n[0] * i, u = n[1] * s;
  return [
    [o - l, r - u],
    [o + i - l, r + s - u]
  ];
}
function dp({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const i = Re(e);
  let s = {
    controlDirection: ps("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function l({ controlPosition: c, boundaries: d, keepAspectRatio: f, resizeDirection: h, onResizeStart: g, onResize: m, onResizeEnd: x, shouldResize: v }) {
    let y = { ...wc }, b = { ...lp };
    s = {
      boundaries: d,
      resizeDirection: h,
      keepAspectRatio: f,
      controlDirection: ps(c)
    };
    let p, w = null, E = [], k, _, D, $ = !1;
    const F = Ia().on("start", (j) => {
      const { nodeLookup: z, transform: H, snapGrid: S, snapToGrid: I, nodeOrigin: C, paneDomNode: A } = n();
      if (p = z.get(t), !p)
        return;
      w = A?.getBoundingClientRect() ?? null;
      const { xSnapped: T, ySnapped: P } = Cn(j.sourceEvent, {
        transform: H,
        snapGrid: S,
        snapToGrid: I,
        containerBounds: w
      });
      y = {
        width: p.measured.width ?? 0,
        height: p.measured.height ?? 0,
        x: p.position.x ?? 0,
        y: p.position.y ?? 0
      }, b = {
        ...y,
        pointerX: T,
        pointerY: P,
        aspectRatio: y.width / y.height
      }, k = void 0, _ = _t(p.extent) ? p.extent : void 0, p.parentId && (p.extent === "parent" || p.expandParent) && (k = z.get(p.parentId)), k && p.extent === "parent" && (_ = [
        [0, 0],
        [k.measured.width, k.measured.height]
      ]), E = [], D = void 0;
      for (const [W, O] of z)
        if (O.parentId === t && (E.push({
          id: W,
          position: { ...O.position },
          extent: O.extent
        }), O.extent === "parent" || O.expandParent)) {
          const B = up(O, p, O.origin ?? C);
          D ? D = [
            [Math.min(B[0][0], D[0][0]), Math.min(B[0][1], D[0][1])],
            [Math.max(B[1][0], D[1][0]), Math.max(B[1][1], D[1][1])]
          ] : D = B;
        }
      g?.(j, { ...y });
    }).on("drag", (j) => {
      const { transform: z, snapGrid: H, snapToGrid: S, nodeOrigin: I } = n(), C = Cn(j.sourceEvent, {
        transform: z,
        snapGrid: H,
        snapToGrid: S,
        containerBounds: w
      }), A = [];
      if (!p)
        return;
      const { x: T, y: P, width: W, height: O } = y, B = {}, G = p.origin ?? I, { width: Z, height: te, x: ae, y: J } = cp(b, s.controlDirection, C, s.boundaries, s.keepAspectRatio, G, _, D), R = Z !== W, q = te !== O, re = ae !== T && R, oe = J !== P && q;
      if (!re && !oe && !R && !q)
        return;
      if ((re || oe || G[0] === 1 || G[1] === 1) && (B.x = re ? ae : y.x, B.y = oe ? J : y.y, y.x = B.x, y.y = B.y, E.length > 0)) {
        const L = ae - T, K = J - P;
        for (const fe of E)
          fe.position = {
            x: fe.position.x - L + G[0] * (Z - W),
            y: fe.position.y - K + G[1] * (te - O)
          }, A.push(fe);
      }
      if ((R || q) && (B.width = R && (!s.resizeDirection || s.resizeDirection === "horizontal") ? Z : y.width, B.height = q && (!s.resizeDirection || s.resizeDirection === "vertical") ? te : y.height, y.width = B.width, y.height = B.height), k && p.expandParent) {
        const L = G[0] * (B.width ?? 0);
        B.x && B.x < L && (y.x = L, b.x = b.x - (B.x - L));
        const K = G[1] * (B.height ?? 0);
        B.y && B.y < K && (y.y = K, b.y = b.y - (B.y - K));
      }
      const U = ap({
        width: y.width,
        prevWidth: W,
        height: y.height,
        prevHeight: O,
        affectsX: s.controlDirection.affectsX,
        affectsY: s.controlDirection.affectsY
      }), Q = { ...y, direction: U };
      v?.(j, Q) !== !1 && ($ = !0, m?.(j, Q), o(B, A));
    }).on("end", (j) => {
      $ && (x?.(j, { ...y }), r?.({ ...y }), $ = !1);
    });
    i.call(F);
  }
  function u() {
    i.on(".drag", null);
  }
  return {
    update: l,
    destroy: u
  };
}
var Pr = { exports: {} }, $r = {}, Tr = { exports: {} }, zr = {};
var ms;
function fp() {
  if (ms) return zr;
  ms = 1;
  var e = ht;
  function t(f, h) {
    return f === h && (f !== 0 || 1 / f === 1 / h) || f !== f && h !== h;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, i = e.useLayoutEffect, s = e.useDebugValue;
  function l(f, h) {
    var g = h(), m = o({ inst: { value: g, getSnapshot: h } }), x = m[0].inst, v = m[1];
    return i(
      function() {
        x.value = g, x.getSnapshot = h, u(x) && v({ inst: x });
      },
      [f, g, h]
    ), r(
      function() {
        return u(x) && v({ inst: x }), f(function() {
          u(x) && v({ inst: x });
        });
      },
      [f]
    ), s(g), g;
  }
  function u(f) {
    var h = f.getSnapshot;
    f = f.value;
    try {
      var g = h();
      return !n(f, g);
    } catch {
      return !0;
    }
  }
  function c(f, h) {
    return h();
  }
  var d = typeof window > "u" || typeof window.document > "u" || typeof window.document.createElement > "u" ? c : l;
  return zr.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : d, zr;
}
var ys;
function hp() {
  return ys || (ys = 1, Tr.exports = fp()), Tr.exports;
}
var xs;
function pp() {
  if (xs) return $r;
  xs = 1;
  var e = ht, t = hp();
  function n(c, d) {
    return c === d && (c !== 0 || 1 / c === 1 / d) || c !== c && d !== d;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, i = e.useRef, s = e.useEffect, l = e.useMemo, u = e.useDebugValue;
  return $r.useSyncExternalStoreWithSelector = function(c, d, f, h, g) {
    var m = i(null);
    if (m.current === null) {
      var x = { hasValue: !1, value: null };
      m.current = x;
    } else x = m.current;
    m = l(
      function() {
        function y(k) {
          if (!b) {
            if (b = !0, p = k, k = h(k), g !== void 0 && x.hasValue) {
              var _ = x.value;
              if (g(_, k))
                return w = _;
            }
            return w = k;
          }
          if (_ = w, o(p, k)) return _;
          var D = h(k);
          return g !== void 0 && g(_, D) ? (p = k, _) : (p = k, w = D);
        }
        var b = !1, p, w, E = f === void 0 ? null : f;
        return [
          function() {
            return y(d());
          },
          E === null ? void 0 : function() {
            return y(E());
          }
        ];
      },
      [d, f, h, g]
    );
    var v = r(c, m[0], m[1]);
    return s(
      function() {
        x.hasValue = !0, x.value = v;
      },
      [v]
    ), u(v), v;
  }, $r;
}
var ws;
function gp() {
  return ws || (ws = 1, Pr.exports = pp()), Pr.exports;
}
var mp = gp();
const yp = /* @__PURE__ */ Dl(mp), xp = {}, vs = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (d, f) => {
    const h = typeof d == "function" ? d(t) : d;
    if (!Object.is(h, t)) {
      const g = t;
      t = f ?? (typeof h != "object" || h === null) ? h : Object.assign({}, t, h), n.forEach((m) => m(t, g));
    }
  }, r = () => t, u = { setState: o, getState: r, getInitialState: () => c, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (xp ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, c = t = e(o, r, u);
  return u;
}, wp = (e) => e ? vs(e) : vs, { useDebugValue: vp } = ht, { useSyncExternalStoreWithSelector: bp } = yp, Sp = (e) => e;
function vc(e, t = Sp, n) {
  const o = bp(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return vp(o), o;
}
const bs = (e, t) => {
  const n = wp(e), o = (r, i = t) => vc(n, r, i);
  return Object.assign(o, n), o;
}, Np = (e, t) => e ? bs(e, t) : bs;
function ye(e, t) {
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
var Rr = { exports: {} }, Me = {};
var Ss;
function Ep() {
  if (Ss) return Me;
  Ss = 1;
  var e = ht;
  function t(u) {
    var c = "https://react.dev/errors/" + u;
    if (1 < arguments.length) {
      c += "?args[]=" + encodeURIComponent(arguments[1]);
      for (var d = 2; d < arguments.length; d++)
        c += "&args[]=" + encodeURIComponent(arguments[d]);
    }
    return "Minified React error #" + u + "; visit " + c + " for the full message or use the non-minified dev environment for full errors and additional helpful warnings.";
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
  function i(u, c, d) {
    var f = 3 < arguments.length && arguments[3] !== void 0 ? arguments[3] : null;
    return {
      $$typeof: r,
      key: f == null ? null : "" + f,
      children: u,
      containerInfo: c,
      implementation: d
    };
  }
  var s = e.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE;
  function l(u, c) {
    if (u === "font") return "";
    if (typeof c == "string")
      return c === "use-credentials" ? c : "";
  }
  return Me.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, Me.createPortal = function(u, c) {
    var d = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!c || c.nodeType !== 1 && c.nodeType !== 9 && c.nodeType !== 11)
      throw Error(t(299));
    return i(u, c, null, d);
  }, Me.flushSync = function(u) {
    var c = s.T, d = o.p;
    try {
      if (s.T = null, o.p = 2, u) return u();
    } finally {
      s.T = c, o.p = d, o.d.f();
    }
  }, Me.preconnect = function(u, c) {
    typeof u == "string" && (c ? (c = c.crossOrigin, c = typeof c == "string" ? c === "use-credentials" ? c : "" : void 0) : c = null, o.d.C(u, c));
  }, Me.prefetchDNS = function(u) {
    typeof u == "string" && o.d.D(u);
  }, Me.preinit = function(u, c) {
    if (typeof u == "string" && c && typeof c.as == "string") {
      var d = c.as, f = l(d, c.crossOrigin), h = typeof c.integrity == "string" ? c.integrity : void 0, g = typeof c.fetchPriority == "string" ? c.fetchPriority : void 0;
      d === "style" ? o.d.S(
        u,
        typeof c.precedence == "string" ? c.precedence : void 0,
        {
          crossOrigin: f,
          integrity: h,
          fetchPriority: g
        }
      ) : d === "script" && o.d.X(u, {
        crossOrigin: f,
        integrity: h,
        fetchPriority: g,
        nonce: typeof c.nonce == "string" ? c.nonce : void 0
      });
    }
  }, Me.preinitModule = function(u, c) {
    if (typeof u == "string")
      if (typeof c == "object" && c !== null) {
        if (c.as == null || c.as === "script") {
          var d = l(
            c.as,
            c.crossOrigin
          );
          o.d.M(u, {
            crossOrigin: d,
            integrity: typeof c.integrity == "string" ? c.integrity : void 0,
            nonce: typeof c.nonce == "string" ? c.nonce : void 0
          });
        }
      } else c == null && o.d.M(u);
  }, Me.preload = function(u, c) {
    if (typeof u == "string" && typeof c == "object" && c !== null && typeof c.as == "string") {
      var d = c.as, f = l(d, c.crossOrigin);
      o.d.L(u, d, {
        crossOrigin: f,
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
  }, Me.preloadModule = function(u, c) {
    if (typeof u == "string")
      if (c) {
        var d = l(c.as, c.crossOrigin);
        o.d.m(u, {
          as: typeof c.as == "string" && c.as !== "script" ? c.as : void 0,
          crossOrigin: d,
          integrity: typeof c.integrity == "string" ? c.integrity : void 0
        });
      } else o.d.m(u);
  }, Me.requestFormReset = function(u) {
    o.d.r(u);
  }, Me.unstable_batchedUpdates = function(u, c) {
    return u(c);
  }, Me.useFormState = function(u, c, d) {
    return s.H.useFormState(u, c, d);
  }, Me.useFormStatus = function() {
    return s.H.useHostTransitionStatus();
  }, Me.version = "19.2.7", Me;
}
var Ns;
function kp() {
  if (Ns) return Rr.exports;
  Ns = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), Rr.exports = Ep(), Rr.exports;
}
var Cp = kp();
const qo = ai(null), _p = qo.Provider, bc = He.error001("react");
function de(e, t) {
  const n = Rn(qo);
  if (n === null)
    throw new Error(bc);
  return vc(n, e, t);
}
function xe() {
  const e = Rn(qo);
  if (e === null)
    throw new Error(bc);
  return me(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const Es = { display: "none" }, Ip = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, Sc = "react-flow__node-desc", Nc = "react-flow__edge-desc", jp = "react-flow__aria-live", Ap = (e) => e.ariaLiveMessage, Mp = (e) => e.ariaLabelConfig;
function Dp({ rfId: e }) {
  const t = de(Ap);
  return a.jsx("div", { id: `${jp}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: Ip, children: t });
}
function Pp({ rfId: e, disableKeyboardA11y: t }) {
  const n = de(Mp);
  return a.jsxs(a.Fragment, { children: [a.jsx("div", { id: `${Sc}-${e}`, style: Es, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), a.jsx("div", { id: `${Nc}-${e}`, style: Es, children: n["edge.a11yDescription.default"] }), !t && a.jsx(Dp, { rfId: e })] });
}
const Zo = Lo(({ position: e = "top-left", children: t, className: n, style: o, ...r }, i) => {
  const s = `${e}`.split("-");
  return a.jsx("div", { className: Ne(["react-flow__panel", n, ...s]), style: o, ref: i, ...r, children: t });
});
Zo.displayName = "Panel";
function $p({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : a.jsx(Zo, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: a.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const Tp = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, fo = (e) => e.id;
function zp(e, t) {
  return ye(e.selectedNodes.map(fo), t.selectedNodes.map(fo)) && ye(e.selectedEdges.map(fo), t.selectedEdges.map(fo));
}
function Rp({ onSelectionChange: e }) {
  const t = xe(), { selectedNodes: n, selectedEdges: o } = de(Tp, zp);
  return ie(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((i) => i(r));
  }, [n, o, e]), null;
}
const Lp = (e) => !!e.onSelectionChangeHandlers;
function Hp({ onSelectionChange: e }) {
  const t = de(Lp);
  return e || t ? a.jsx(Rp, { onSelectionChange: e }) : null;
}
const Ec = [0, 0], Vp = { x: 0, y: 0, zoom: 1 }, Op = [
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
], ks = [...Op, "rfId"], Bp = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), Cs = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: Mn,
  nodeOrigin: Ec,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function Fp(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: i, setNodeExtent: s, reset: l, setDefaultNodesAndEdges: u } = de(Bp, ye), c = xe();
  ie(() => (u(e.defaultNodes, e.defaultEdges), () => {
    d.current = Cs, l();
  }), []);
  const d = ce(Cs);
  return ie(
    () => {
      for (const f of ks) {
        const h = e[f], g = d.current[f];
        h !== g && (typeof e[f] > "u" || (f === "nodes" ? t(h) : f === "edges" ? n(h) : f === "minZoom" ? o(h) : f === "maxZoom" ? r(h) : f === "translateExtent" ? i(h) : f === "nodeExtent" ? s(h) : f === "ariaLabelConfig" ? c.setState({ ariaLabelConfig: Eh(h) }) : f === "fitView" ? c.setState({ fitViewQueued: h }) : f === "fitViewOptions" ? c.setState({ fitViewOptions: h }) : c.setState({ [f]: h })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    ks.map((f) => e[f])
  ), null;
}
function _s() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function Wp(e) {
  const [t, n] = Y(e === "system" ? null : e);
  return ie(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = _s(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : _s()?.matches ? "dark" : "light";
}
const Is = typeof document < "u" ? document : null;
function Tn(e = null, t = { target: Is, actInsideInputWithModifier: !0 }) {
  const [n, o] = Y(!1), r = ce(!1), i = ce(/* @__PURE__ */ new Set([])), [s, l] = me(() => {
    if (e !== null) {
      const c = (Array.isArray(e) ? e : [e]).filter((f) => typeof f == "string").map((f) => f.replace("+", `
`).replace(`

`, `
+`).split(`
`)), d = c.reduce((f, h) => f.concat(...h), []);
      return [c, d];
    }
    return [[], []];
  }, [e]);
  return ie(() => {
    const u = t?.target ?? Is, c = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (g) => {
        if (r.current = g.ctrlKey || g.metaKey || g.shiftKey || g.altKey, (!r.current || r.current && !c) && nc(g))
          return !1;
        const x = As(g.code, l);
        if (i.current.add(g[x]), js(s, i.current, !1)) {
          const v = g.composedPath?.()?.[0] || g.target, y = v?.nodeName === "BUTTON" || v?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !y) && g.preventDefault(), o(!0);
        }
      }, f = (g) => {
        const m = As(g.code, l);
        js(s, i.current, !0) ? (o(!1), i.current.clear()) : i.current.delete(g[m]), g.key === "Meta" && i.current.clear(), r.current = !1;
      }, h = () => {
        i.current.clear(), o(!1);
      };
      return u?.addEventListener("keydown", d), u?.addEventListener("keyup", f), window.addEventListener("blur", h), window.addEventListener("contextmenu", h), () => {
        u?.removeEventListener("keydown", d), u?.removeEventListener("keyup", f), window.removeEventListener("blur", h), window.removeEventListener("contextmenu", h);
      };
    }
  }, [e, o]), n;
}
function js(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function As(e, t) {
  return t.includes(e) ? "code" : "key";
}
const Xp = () => {
  const e = xe();
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
      const { width: o, height: r, minZoom: i, maxZoom: s, panZoom: l } = e.getState(), u = xi(t, o, r, i, s, n?.padding ?? 0.1);
      return l ? (await l.setViewport(u, {
        duration: n?.duration,
        ease: n?.ease,
        interpolate: n?.interpolate
      }), !0) : !1;
    },
    screenToFlowPosition: (t, n = {}) => {
      const { transform: o, snapGrid: r, snapToGrid: i, domNode: s } = e.getState();
      if (!s)
        return t;
      const { x: l, y: u } = s.getBoundingClientRect(), c = {
        x: t.x - l,
        y: t.y - u
      }, d = n.snapGrid ?? r, f = n.snapToGrid ?? i;
      return Ut(c, o, f, d);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: r, y: i } = o.getBoundingClientRect(), s = Wt(t, n);
      return {
        x: s.x + r,
        y: s.y + i
      };
    }
  }), []);
};
function kc(e, t) {
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
    const l = { ...i };
    for (const u of s)
      Yp(u, l);
    n.push(l);
  }
  return r.length && r.forEach((i) => {
    i.index !== void 0 ? n.splice(i.index, 0, { ...i.item }) : n.push({ ...i.item });
  }), n;
}
function Yp(e, t) {
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
function Cc(e, t) {
  return kc(e, t);
}
function _c(e, t) {
  return kc(e, t);
}
function vt(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function zt(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [r, i] of e) {
    const s = t.has(r);
    !(i.selected === void 0 && !s) && i.selected !== s && (n && (i.selected = s), o.push(vt(i.id, s)));
  }
  return o;
}
function Ms({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((r) => [r.id, r]));
  for (const [r, i] of e.entries()) {
    const s = t.get(i.id), l = s?.internals?.userNode ?? s;
    l !== void 0 && l !== i && n.push({ id: i.id, item: i, type: "replace" }), l === void 0 && n.push({ item: i, type: "add", index: r });
  }
  for (const [r] of t)
    o.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function Ds(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const Ic = Ja();
function jc(e, t, n = {}) {
  return jh(e, t, {
    ...n,
    onError: n.onError ?? Ic
  });
}
function qp(e, t, n, o = { shouldReplaceId: !0 }) {
  return Ah(e, t, n, {
    ...o,
    onError: o.onError ?? Ic
  });
}
const Ps = (e) => gh(e), Zp = (e) => Za(e);
function Ac(e) {
  return Lo(e);
}
const Kp = typeof window < "u" ? Ml : ie;
function $s(e) {
  const [t, n] = Y(BigInt(0)), [o] = Y(() => Up(() => n((r) => r + BigInt(1))));
  return Kp(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function Up(e) {
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
const Mc = ai(null);
function Gp({ children: e }) {
  const t = xe(), n = ge((l) => {
    const { nodes: u = [], setNodes: c, hasDefaultNodes: d, onNodesChange: f, nodeLookup: h, fitViewQueued: g, onNodesChangeMiddlewareMap: m } = t.getState();
    let x = u;
    for (const y of l)
      x = typeof y == "function" ? y(x) : y;
    let v = Ms({
      items: x,
      lookup: h
    });
    for (const y of m.values())
      v = y(v);
    d && c(x), v.length > 0 ? f?.(v) : g && window.requestAnimationFrame(() => {
      const { fitViewQueued: y, nodes: b, setNodes: p } = t.getState();
      y && p(b);
    });
  }, []), o = $s(n), r = ge((l) => {
    const { edges: u = [], setEdges: c, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: h } = t.getState();
    let g = u;
    for (const m of l)
      g = typeof m == "function" ? m(g) : m;
    d ? c(g) : f && f(Ms({
      items: g,
      lookup: h
    }));
  }, []), i = $s(r), s = me(() => ({ nodeQueue: o, edgeQueue: i }), []);
  return a.jsx(Mc.Provider, { value: s, children: e });
}
function Jp() {
  const e = Rn(Mc);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Qp = (e) => !!e.panZoom;
function ki() {
  const e = Xp(), t = xe(), n = Jp(), o = de(Qp), r = me(() => {
    const i = (f) => t.getState().nodeLookup.get(f), s = (f) => {
      n.nodeQueue.push(f);
    }, l = (f) => {
      n.edgeQueue.push(f);
    }, u = (f) => {
      const { nodeLookup: h, nodeOrigin: g } = t.getState(), m = Ps(f) ? f : h.get(f.id), x = m.parentId ? ec(m.position, m.measured, m.parentId, h, g) : m.position, v = {
        ...m,
        position: x,
        width: m.measured?.width ?? m.width,
        height: m.measured?.height ?? m.height
      };
      return Ft(v);
    }, c = (f, h, g = { replace: !1 }) => {
      s((m) => m.map((x) => {
        if (x.id === f) {
          const v = typeof h == "function" ? h(x) : h;
          return g.replace && Ps(v) ? v : { ...x, ...v };
        }
        return x;
      }));
    }, d = (f, h, g = { replace: !1 }) => {
      l((m) => m.map((x) => {
        if (x.id === f) {
          const v = typeof h == "function" ? h(x) : h;
          return g.replace && Zp(v) ? v : { ...x, ...v };
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
      setEdges: l,
      addNodes: (f) => {
        const h = Array.isArray(f) ? f : [f];
        n.nodeQueue.push((g) => [...g, ...h]);
      },
      addEdges: (f) => {
        const h = Array.isArray(f) ? f : [f];
        n.edgeQueue.push((g) => [...g, ...h]);
      },
      toObject: () => {
        const { nodes: f = [], edges: h = [], transform: g } = t.getState(), [m, x, v] = g;
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
        const { nodes: g, edges: m, onNodesDelete: x, onEdgesDelete: v, triggerNodeChanges: y, triggerEdgeChanges: b, onDelete: p, onBeforeDelete: w } = t.getState(), { nodes: E, edges: k } = await vh({
          nodesToRemove: f,
          edgesToRemove: h,
          nodes: g,
          edges: m,
          onBeforeDelete: w
        }), _ = k.length > 0, D = E.length > 0;
        if (_) {
          const $ = k.map(Ds);
          v?.(k), b($);
        }
        if (D) {
          const $ = E.map(Ds);
          x?.(E), y($);
        }
        return (D || _) && p?.({ nodes: E, edges: k }), { deletedNodes: E, deletedEdges: k };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, h = !0, g) => {
        const m = os(f), x = m ? f : u(f), v = g !== void 0;
        return x ? (g || t.getState().nodes).filter((y) => {
          const b = t.getState().nodeLookup.get(y.id);
          if (b && !m && (y.id === f.id || !b.internals.positionAbsolute))
            return !1;
          const p = Ft(v ? y : b), w = Pn(p, x);
          return h && w > 0 || w >= p.width * p.height || w >= x.width * x.height;
        }) : [];
      },
      isNodeIntersecting: (f, h, g = !0) => {
        const x = os(f) ? f : u(f);
        if (!x)
          return !1;
        const v = Pn(x, h);
        return g && v > 0 || v >= h.width * h.height || v >= x.width * x.height;
      },
      updateNode: c,
      updateNodeData: (f, h, g = { replace: !1 }) => {
        c(f, (m) => {
          const x = typeof h == "function" ? h(m) : h;
          return g.replace ? { ...m, data: x } : { ...m, data: { ...m.data, ...x } };
        }, g);
      },
      updateEdge: d,
      updateEdgeData: (f, h, g = { replace: !1 }) => {
        d(f, (m) => {
          const x = typeof h == "function" ? h(m) : h;
          return g.replace ? { ...m, data: x } : { ...m, data: { ...m.data, ...x } };
        }, g);
      },
      getNodesBounds: (f) => {
        const { nodeLookup: h, nodeOrigin: g } = t.getState();
        return mh(f, { nodeLookup: h, nodeOrigin: g });
      },
      getHandleConnections: ({ type: f, id: h, nodeId: g }) => Array.from(t.getState().connectionLookup.get(`${g}-${f}${h ? `-${h}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: h, nodeId: g }) => Array.from(t.getState().connectionLookup.get(`${g}${f ? h ? `-${f}-${h}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const h = t.getState().fitViewResolver ?? Nh();
        return t.setState({ fitViewQueued: !0, fitViewOptions: f, fitViewResolver: h }), n.nodeQueue.push((g) => [...g]), h.promise;
      }
    };
  }, []);
  return me(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const Ts = (e) => e.selected, eg = typeof window < "u" ? window : void 0;
function tg({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = xe(), { deleteElements: o } = ki(), r = Tn(e, { actInsideInputWithModifier: !1 }), i = Tn(t, { target: eg });
  ie(() => {
    if (r) {
      const { edges: s, nodes: l } = n.getState();
      o({ nodes: l.filter(Ts), edges: s.filter(Ts) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), ie(() => {
    n.setState({ multiSelectionActive: i });
  }, [i]);
}
function ng(e) {
  const t = xe();
  ie(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = wi(e.current);
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
const Ko = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, og = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function rg({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: i = Nt.Free, zoomOnDoubleClick: s = !0, panOnDrag: l = !0, defaultViewport: u, translateExtent: c, minZoom: d, maxZoom: f, zoomActivationKeyCode: h, preventScrolling: g = !0, children: m, noWheelClassName: x, noPanClassName: v, onViewportChange: y, isControlledViewport: b, paneClickDistance: p, selectionOnDrag: w }) {
  const E = xe(), k = ce(null), { userSelectionActive: _, lib: D, connectionInProgress: $ } = de(og, ye), F = Tn(h), j = ce();
  ng(k);
  const z = ge((H) => {
    y?.({ x: H[0], y: H[1], zoom: H[2] }), b || E.setState({ transform: H });
  }, [y, b]);
  return ie(() => {
    if (k.current) {
      j.current = sp({
        domNode: k.current,
        minZoom: d,
        maxZoom: f,
        translateExtent: c,
        viewport: u,
        onDraggingChange: (C) => E.setState((A) => A.paneDragging === C ? A : { paneDragging: C }),
        onPanZoomStart: (C, A) => {
          const { onViewportChangeStart: T, onMoveStart: P } = E.getState();
          P?.(C, A), T?.(A);
        },
        onPanZoom: (C, A) => {
          const { onViewportChange: T, onMove: P } = E.getState();
          P?.(C, A), T?.(A);
        },
        onPanZoomEnd: (C, A) => {
          const { onViewportChangeEnd: T, onMoveEnd: P } = E.getState();
          P?.(C, A), T?.(A);
        }
      });
      const { x: H, y: S, zoom: I } = j.current.getViewport();
      return E.setState({
        panZoom: j.current,
        transform: [H, S, I],
        domNode: k.current.closest(".react-flow")
      }), () => {
        j.current?.destroy();
      };
    }
  }, []), ie(() => {
    j.current?.update({
      onPaneContextMenu: e,
      zoomOnScroll: t,
      zoomOnPinch: n,
      panOnScroll: o,
      panOnScrollSpeed: r,
      panOnScrollMode: i,
      zoomOnDoubleClick: s,
      panOnDrag: l,
      zoomActivationKeyPressed: F,
      preventScrolling: g,
      noPanClassName: v,
      userSelectionActive: _,
      noWheelClassName: x,
      lib: D,
      onTransformChange: z,
      connectionInProgress: $,
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
    l,
    F,
    g,
    v,
    _,
    x,
    D,
    z,
    $,
    w,
    p
  ]), a.jsx("div", { className: "react-flow__renderer", ref: k, style: Ko, children: m });
}
const ig = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function sg() {
  const { userSelectionActive: e, userSelectionRect: t } = de(ig, ye);
  return e && t ? a.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const Lr = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, ag = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function cg({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Dn.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: i, selectionOnDrag: s, onSelectionStart: l, onSelectionEnd: u, onPaneClick: c, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: h, onPaneMouseMove: g, onPaneMouseLeave: m, children: x }) {
  const v = ce(0), y = xe(), { userSelectionActive: b, elementsSelectable: p, dragging: w, connectionInProgress: E, panBy: k, autoPanSpeed: _ } = de(ag, ye), D = p && (e || b), $ = ce(null), F = ce(), j = ce(/* @__PURE__ */ new Set()), z = ce(/* @__PURE__ */ new Set()), H = ce(!1), S = ce({ x: 0, y: 0 }), I = ce(!1), C = (R) => {
    if (H.current || E) {
      H.current = !1;
      return;
    }
    c?.(R), y.getState().resetSelectedElements(), y.setState({ nodesSelectionActive: !1 });
  }, A = (R) => {
    if (Array.isArray(o) && o?.includes(2)) {
      R.preventDefault();
      return;
    }
    d?.(R);
  }, T = f ? (R) => f(R) : void 0, P = (R) => {
    H.current && (R.stopPropagation(), H.current = !1);
  }, W = (R) => {
    const { domNode: q, transform: re } = y.getState();
    if (F.current = q?.getBoundingClientRect(), !F.current)
      return;
    const oe = R.target === $.current;
    if (!oe && !!R.target.closest(".nokey") || !e || !(s && oe || t) || R.button !== 0 || !R.isPrimary)
      return;
    R.target?.setPointerCapture?.(R.pointerId), H.current = !1;
    const { x: se, y: L } = qe(R.nativeEvent, F.current), K = Ut({ x: se, y: L }, re);
    y.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: K.x,
        startY: K.y,
        x: se,
        y: L
      }
    }), oe || (R.stopPropagation(), R.preventDefault());
  };
  function O(R, q) {
    const { userSelectionRect: re } = y.getState();
    if (!re)
      return;
    const { transform: oe, nodeLookup: U, edgeLookup: Q, connectionLookup: se, triggerNodeChanges: L, triggerEdgeChanges: K, defaultEdgeOptions: fe } = y.getState(), ue = { x: re.startX, y: re.startY }, { x: Ie, y: Oe } = Wt(ue, oe), Be = {
      startX: ue.x,
      startY: ue.y,
      x: R < Ie ? R : Ie,
      y: q < Oe ? q : Oe,
      width: Math.abs(R - Ie),
      height: Math.abs(q - Oe)
    }, pt = j.current, et = z.current;
    j.current = new Set(mi(U, Be, oe, n === Dn.Partial, !0).map((je) => je.id)), z.current = /* @__PURE__ */ new Set();
    const Ee = fe?.selectable ?? !0;
    for (const je of j.current) {
      const Pe = se.get(je);
      if (Pe)
        for (const { edgeId: $e } of Pe.values()) {
          const Ke = Q.get($e);
          Ke && (Ke.selectable ?? Ee) && z.current.add($e);
        }
    }
    if (!rs(pt, j.current)) {
      const je = zt(U, j.current, !0);
      L(je);
    }
    if (!rs(et, z.current)) {
      const je = zt(Q, z.current);
      K(je);
    }
    y.setState({
      userSelectionRect: Be,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function B() {
    if (!r || !F.current)
      return;
    const [R, q] = yi(S.current, F.current, _);
    k({ x: R, y: q }).then((re) => {
      if (!H.current || !re) {
        v.current = requestAnimationFrame(B);
        return;
      }
      const { x: oe, y: U } = S.current;
      O(oe, U), v.current = requestAnimationFrame(B);
    });
  }
  const G = () => {
    cancelAnimationFrame(v.current), v.current = 0, I.current = !1;
  };
  ie(() => () => G(), []);
  const Z = (R) => {
    const { userSelectionRect: q, transform: re, resetSelectedElements: oe } = y.getState();
    if (!F.current || !q)
      return;
    const { x: U, y: Q } = qe(R.nativeEvent, F.current);
    S.current = { x: U, y: Q };
    const se = Wt({ x: q.startX, y: q.startY }, re);
    if (!H.current) {
      const L = t ? 0 : i;
      if (Math.hypot(U - se.x, Q - se.y) <= L)
        return;
      oe(), l?.(R);
    }
    H.current = !0, I.current || (B(), I.current = !0), O(U, Q);
  }, te = (R) => {
    R.button === 0 && (R.target?.releasePointerCapture?.(R.pointerId), !b && R.target === $.current && y.getState().userSelectionRect && C?.(R), y.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), H.current && (u?.(R), y.setState({
      nodesSelectionActive: j.current.size > 0
    })), G());
  }, ae = (R) => {
    R.target?.releasePointerCapture?.(R.pointerId), G();
  }, J = o === !0 || Array.isArray(o) && o.includes(0);
  return a.jsxs("div", { className: Ne(["react-flow__pane", { draggable: J, dragging: w, selection: e }]), onClick: D ? void 0 : Lr(C, $), onContextMenu: Lr(A, $), onWheel: Lr(T, $), onPointerEnter: D ? void 0 : h, onPointerMove: D ? Z : g, onPointerUp: D ? te : void 0, onPointerCancel: D ? ae : void 0, onPointerDownCapture: D ? W : void 0, onClickCapture: D ? P : void 0, onPointerLeave: m, ref: $, style: Ko, children: [x, a.jsx(sg, {})] });
}
function ti({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: i, multiSelectionActive: s, nodeLookup: l, onError: u } = t.getState(), c = l.get(e);
  if (!c) {
    u?.("012", He.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), c.selected ? (n || c.selected && s) && (i({ nodes: [c], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function Dc({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: i, nodeClickDistance: s }) {
  const l = xe(), [u, c] = Y(!1), d = ce();
  return ie(() => {
    d.current = Yh({
      getStoreItems: () => l.getState(),
      onNodeMouseDown: (f) => {
        ti({
          id: f,
          store: l,
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
  }, []), ie(() => {
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
  }, [n, o, t, i, e, r, s]), u;
}
const lg = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function Pc() {
  const e = xe();
  return ge((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: i, nodesDraggable: s, onError: l, updateNodePositions: u, nodeLookup: c, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), h = lg(s), g = r ? i[0] : 5, m = r ? i[1] : 5, x = n.direction.x * g * n.factor, v = n.direction.y * m * n.factor;
    for (const [, y] of c) {
      if (!h(y))
        continue;
      let b = {
        x: y.internals.positionAbsolute.x + x,
        y: y.internals.positionAbsolute.y + v
      };
      r && (b = Bn(b, i));
      const { position: p, positionAbsolute: w } = Ka({
        nodeId: y.id,
        nextPosition: b,
        nodeLookup: c,
        nodeExtent: o,
        nodeOrigin: d,
        onError: l
      });
      y.position = p, y.internals.positionAbsolute = w, f.set(y.id, y);
    }
    u(f);
  }, []);
}
const Ci = ai(null), ug = Ci.Provider;
Ci.Consumer;
const $c = () => Rn(Ci), dg = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), fg = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: i, connection: s } = o, { fromHandle: l, toHandle: u, isValid: c } = s, d = u?.nodeId === e && u?.id === t && u?.type === n;
  return {
    connectingFrom: l?.nodeId === e && l?.id === t && l?.type === n,
    connectingTo: d,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: i === Ot.Strict ? l?.type !== n : e !== l?.nodeId || t !== l?.id,
    connectionInProcess: !!l,
    clickConnectionInProcess: !!r,
    valid: d && c
  };
};
function hg({ type: e = "source", position: t = ee.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: i = !0, id: s, onConnect: l, children: u, className: c, onMouseDown: d, onTouchStart: f, ...h }, g) {
  const m = s || null, x = e === "target", v = xe(), y = $c(), { connectOnClick: b, noPanClassName: p, rfId: w } = de(dg, ye), { connectingFrom: E, connectingTo: k, clickConnecting: _, isPossibleEndHandle: D, connectionInProcess: $, clickConnectionInProcess: F, valid: j } = de(fg(y, m, e), ye);
  y || v.getState().onError?.("010", He.error010());
  const z = (I) => {
    const { defaultEdgeOptions: C, onConnect: A, hasDefaultEdges: T } = v.getState(), P = {
      ...C,
      ...I
    };
    if (T) {
      const { edges: W, setEdges: O, onError: B } = v.getState();
      O(jc(P, W, { onError: B }));
    }
    A?.(P), l?.(P);
  }, H = (I) => {
    if (!y)
      return;
    const C = oc(I.nativeEvent);
    if (r && (C && I.button === 0 || !C)) {
      const A = v.getState();
      ei.onPointerDown(I.nativeEvent, {
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
  }, S = (I) => {
    const { onClickConnectStart: C, onClickConnectEnd: A, connectionClickStartHandle: T, connectionMode: P, isValidConnection: W, lib: O, rfId: B, nodeLookup: G, connection: Z } = v.getState();
    if (!y || !T && !r)
      return;
    if (!T) {
      C?.(I.nativeEvent, { nodeId: y, handleId: m, handleType: e }), v.setState({ connectionClickStartHandle: { nodeId: y, type: e, id: m } });
      return;
    }
    const te = tc(I.target), ae = n || W, { connection: J, isValid: R } = ei.isValid(I.nativeEvent, {
      handle: {
        nodeId: y,
        id: m,
        type: e
      },
      connectionMode: P,
      fromNodeId: T.nodeId,
      fromHandleId: T.id || null,
      fromType: T.type,
      isValidConnection: ae,
      flowId: B,
      doc: te,
      lib: O,
      nodeLookup: G
    });
    R && J && z(J);
    const q = structuredClone(Z);
    delete q.inProgress, q.toPosition = q.toHandle ? q.toHandle.position : null, A?.(I, q), v.setState({ connectionClickStartHandle: null });
  };
  return a.jsx("div", { "data-handleid": m, "data-nodeid": y, "data-handlepos": t, "data-id": `${w}-${y}-${m}-${e}`, className: Ne([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    p,
    c,
    {
      source: !x,
      target: x,
      connectable: o,
      connectablestart: r,
      connectableend: i,
      clickconnecting: _,
      connectingfrom: E,
      connectingto: k,
      valid: j,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!$ || D) && ($ || F ? i : r)
    }
  ]), onMouseDown: H, onTouchStart: H, onClick: b ? S : void 0, ref: g, ...h, children: u });
}
const Yt = ve(Ac(hg));
function pg({ data: e, isConnectable: t, sourcePosition: n = ee.Bottom }) {
  return a.jsxs(a.Fragment, { children: [e?.label, a.jsx(Yt, { type: "source", position: n, isConnectable: t })] });
}
function gg({ data: e, isConnectable: t, targetPosition: n = ee.Top, sourcePosition: o = ee.Bottom }) {
  return a.jsxs(a.Fragment, { children: [a.jsx(Yt, { type: "target", position: n, isConnectable: t }), e?.label, a.jsx(Yt, { type: "source", position: o, isConnectable: t })] });
}
function mg() {
  return null;
}
function yg({ data: e, isConnectable: t, targetPosition: n = ee.Top }) {
  return a.jsxs(a.Fragment, { children: [a.jsx(Yt, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const $o = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, zs = {
  input: pg,
  default: gg,
  output: yg,
  group: mg
};
function xg(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const wg = (e) => {
  const { width: t, height: n, x: o, y: r } = On(e.nodeLookup, {
    filter: (i) => !!i.selected
  });
  return {
    width: Ye(t) ? t : null,
    height: Ye(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function vg({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = xe(), { width: r, height: i, transformString: s, userSelectionActive: l } = de(wg, ye), u = Pc(), c = ce(null);
  ie(() => {
    n || c.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !l && r !== null && i !== null;
  if (Dc({
    nodeRef: c,
    disabled: !d
  }), !d)
    return null;
  const f = e ? (g) => {
    const m = o.getState().nodes.filter((x) => x.selected);
    e(g, m);
  } : void 0, h = (g) => {
    Object.prototype.hasOwnProperty.call($o, g.key) && (g.preventDefault(), u({
      direction: $o[g.key],
      factor: g.shiftKey ? 4 : 1
    }));
  };
  return a.jsx("div", { className: Ne(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: s
  }, children: a.jsx("div", { ref: c, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : h, style: {
    width: r,
    height: i
  } }) });
}
const Rs = typeof window < "u" ? window : void 0, bg = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function Tc({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, paneClickDistance: l, deleteKeyCode: u, selectionKeyCode: c, selectionOnDrag: d, selectionMode: f, onSelectionStart: h, onSelectionEnd: g, multiSelectionKeyCode: m, panActivationKeyCode: x, zoomActivationKeyCode: v, elementsSelectable: y, zoomOnScroll: b, zoomOnPinch: p, panOnScroll: w, panOnScrollSpeed: E, panOnScrollMode: k, zoomOnDoubleClick: _, panOnDrag: D, autoPanOnSelection: $, defaultViewport: F, translateExtent: j, minZoom: z, maxZoom: H, preventScrolling: S, onSelectionContextMenu: I, noWheelClassName: C, noPanClassName: A, disableKeyboardA11y: T, onViewportChange: P, isControlledViewport: W }) {
  const { nodesSelectionActive: O, userSelectionActive: B } = de(bg, ye), G = Tn(c, { target: Rs }), Z = Tn(x, { target: Rs }), te = Z || D, ae = Z || w, J = d && te !== !0, R = G || B || J;
  return tg({ deleteKeyCode: u, multiSelectionKeyCode: m }), a.jsx(rg, { onPaneContextMenu: i, elementsSelectable: y, zoomOnScroll: b, zoomOnPinch: p, panOnScroll: ae, panOnScrollSpeed: E, panOnScrollMode: k, zoomOnDoubleClick: _, panOnDrag: !G && te, defaultViewport: F, translateExtent: j, minZoom: z, maxZoom: H, zoomActivationKeyCode: v, preventScrolling: S, noWheelClassName: C, noPanClassName: A, onViewportChange: P, isControlledViewport: W, paneClickDistance: l, selectionOnDrag: J, children: a.jsxs(cg, { onSelectionStart: h, onSelectionEnd: g, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, panOnDrag: te, autoPanOnSelection: $, isSelecting: !!R, selectionMode: f, selectionKeyPressed: G, paneClickDistance: l, selectionOnDrag: J, children: [e, O && a.jsx(vg, { onSelectionContextMenu: I, noPanClassName: A, disableKeyboardA11y: T })] }) });
}
Tc.displayName = "FlowRenderer";
const Sg = ve(Tc), Ng = (e) => (t) => e ? mi(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function Eg(e) {
  return de(ge(Ng(e), [e]), ye);
}
const kg = (e) => e.updateNodeInternals;
function Cg() {
  const e = de(kg), [t] = Y(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
  return ie(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function _g({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = xe(), i = ce(null), s = ce(null), l = ce(e.sourcePosition), u = ce(e.targetPosition), c = ce(t), d = n && !!e.internals.handleBounds;
  return ie(() => {
    i.current && !e.hidden && (!d || s.current !== i.current) && (s.current && o?.unobserve(s.current), o?.observe(i.current), s.current = i.current);
  }, [d, e.hidden]), ie(() => () => {
    s.current && (o?.unobserve(s.current), s.current = null);
  }, []), ie(() => {
    if (i.current) {
      const f = c.current !== t, h = l.current !== e.sourcePosition, g = u.current !== e.targetPosition;
      (f || h || g) && (c.current = t, l.current = e.sourcePosition, u.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: i.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), i;
}
function Ig({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: i, onDoubleClick: s, nodesDraggable: l, elementsSelectable: u, nodesConnectable: c, nodesFocusable: d, resizeObserver: f, noDragClassName: h, noPanClassName: g, disableKeyboardA11y: m, rfId: x, nodeTypes: v, nodeClickDistance: y, onError: b }) {
  const { node: p, internals: w, isParent: E } = de((R) => {
    const q = R.nodeLookup.get(e), re = R.parentLookup.has(e);
    return {
      node: q,
      internals: q.internals,
      isParent: re
    };
  }, ye);
  let k = p.type || "default", _ = v?.[k] || zs[k];
  _ === void 0 && (b?.("003", He.error003(k)), k = "default", _ = v?.default || zs.default);
  const D = !!(p.draggable || l && typeof p.draggable > "u"), $ = !!(p.selectable || u && typeof p.selectable > "u"), F = !!(p.connectable || c && typeof p.connectable > "u"), j = !!(p.focusable || d && typeof p.focusable > "u"), z = xe(), H = Qa(p), S = _g({ node: p, nodeType: k, hasDimensions: H, resizeObserver: f }), I = Dc({
    nodeRef: S,
    disabled: p.hidden || !D,
    noDragClassName: h,
    handleSelector: p.dragHandle,
    nodeId: e,
    isSelectable: $,
    nodeClickDistance: y
  }), C = Pc();
  if (p.hidden)
    return null;
  const A = st(p), T = xg(p), P = $ || D || t || n || o || r, W = n ? (R) => n(R, { ...w.userNode }) : void 0, O = o ? (R) => o(R, { ...w.userNode }) : void 0, B = r ? (R) => r(R, { ...w.userNode }) : void 0, G = i ? (R) => i(R, { ...w.userNode }) : void 0, Z = s ? (R) => s(R, { ...w.userNode }) : void 0, te = (R) => {
    const { selectNodesOnDrag: q, nodeDragThreshold: re } = z.getState();
    $ && (!q || !D || re > 0) && ti({
      id: e,
      store: z,
      nodeRef: S
    }), t && t(R, { ...w.userNode });
  }, ae = (R) => {
    if (!(nc(R.nativeEvent) || m)) {
      if (Wa.includes(R.key) && $) {
        const q = R.key === "Escape";
        ti({
          id: e,
          store: z,
          unselect: q,
          nodeRef: S
        });
      } else if (D && p.selected && Object.prototype.hasOwnProperty.call($o, R.key)) {
        R.preventDefault();
        const { ariaLabelConfig: q } = z.getState();
        z.setState({
          ariaLiveMessage: q["node.a11yDescription.ariaLiveMessage"]({
            direction: R.key.replace("Arrow", "").toLowerCase(),
            x: ~~w.positionAbsolute.x,
            y: ~~w.positionAbsolute.y
          })
        }), C({
          direction: $o[R.key],
          factor: R.shiftKey ? 4 : 1
        });
      }
    }
  }, J = () => {
    if (m || !S.current?.matches(":focus-visible"))
      return;
    const { transform: R, width: q, height: re, autoPanOnNodeFocus: oe, setCenter: U } = z.getState();
    if (!oe)
      return;
    mi(/* @__PURE__ */ new Map([[e, p]]), { x: 0, y: 0, width: q, height: re }, R, !0).length > 0 || U(p.position.x + A.width / 2, p.position.y + A.height / 2, {
      zoom: R[2]
    });
  };
  return a.jsx("div", { className: Ne([
    "react-flow__node",
    `react-flow__node-${k}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [g]: D
    },
    p.className,
    {
      selected: p.selected,
      selectable: $,
      parent: E,
      draggable: D,
      dragging: I
    }
  ]), ref: S, style: {
    zIndex: w.z,
    transform: `translate(${w.positionAbsolute.x}px,${w.positionAbsolute.y}px)`,
    pointerEvents: P ? "all" : "none",
    visibility: H ? "visible" : "hidden",
    ...p.style,
    ...T
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: W, onMouseMove: O, onMouseLeave: B, onContextMenu: G, onClick: te, onDoubleClick: Z, onKeyDown: j ? ae : void 0, tabIndex: j ? 0 : void 0, onFocus: j ? J : void 0, role: p.ariaRole ?? (j ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": m ? void 0 : `${Sc}-${x}`, "aria-label": p.ariaLabel, ...p.domAttributes, children: a.jsx(ug, { value: e, children: a.jsx(_, { id: e, data: p.data, type: k, positionAbsoluteX: w.positionAbsolute.x, positionAbsoluteY: w.positionAbsolute.y, selected: p.selected ?? !1, selectable: $, draggable: D, deletable: p.deletable ?? !0, isConnectable: F, sourcePosition: p.sourcePosition, targetPosition: p.targetPosition, dragging: I, dragHandle: p.dragHandle, zIndex: w.z, parentId: p.parentId, ...A }) }) });
}
var jg = ve(Ig);
const Ag = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function zc(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: i } = de(Ag, ye), s = Eg(e.onlyRenderVisibleElements), l = Cg();
  return a.jsx("div", { className: "react-flow__nodes", style: Ko, children: s.map((u) => (
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
    a.jsx(jg, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: l, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: i }, u)
  )) });
}
zc.displayName = "NodeRenderer";
const Mg = ve(zc);
function Dg(e) {
  return de(ge((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const i = n.nodeLookup.get(r.source), s = n.nodeLookup.get(r.target);
        i && s && _h({
          sourceNode: i,
          targetNode: s,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && o.push(r.id);
      }
    return o;
  }, [e]), ye);
}
const Pg = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return a.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, $g = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return a.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Ls = {
  [Mo.Arrow]: Pg,
  [Mo.ArrowClosed]: $g
};
function Tg(e) {
  const t = xe();
  return me(() => Object.prototype.hasOwnProperty.call(Ls, e) ? Ls[e] : (t.getState().onError?.("009", He.error009(e)), null), [e]);
}
const zg = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: i = "strokeWidth", strokeWidth: s, orient: l = "auto-start-reverse" }) => {
  const u = Tg(t);
  return u ? a.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: i, orient: l, refX: "0", refY: "0", children: a.jsx(u, { color: n, strokeWidth: s }) }) : null;
}, Rc = ({ defaultColor: e, rfId: t }) => {
  const n = de((i) => i.edges), o = de((i) => i.defaultEdgeOptions), r = me(() => Th(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? a.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: a.jsx("defs", { children: r.map((i) => a.jsx(zg, { id: i.id, type: i.type, color: i.color, width: i.width, height: i.height, markerUnits: i.markerUnits, strokeWidth: i.strokeWidth, orient: i.orient }, i.id)) }) }) : null;
};
Rc.displayName = "MarkerDefinitions";
var Rg = ve(Rc);
function Lc({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: i, labelBgPadding: s = [2, 4], labelBgBorderRadius: l = 2, children: u, className: c, ...d }) {
  const [f, h] = Y({ x: 1, y: 0, width: 0, height: 0 }), g = Ne(["react-flow__edge-textwrapper", c]), m = ce(null);
  return ie(() => {
    if (m.current) {
      const x = m.current.getBBox();
      h({
        x: x.x,
        y: x.y,
        width: x.width,
        height: x.height
      });
    }
  }, [n]), n ? a.jsxs("g", { transform: `translate(${e - f.width / 2} ${t - f.height / 2})`, className: g, visibility: f.width ? "visible" : "hidden", ...d, children: [r && a.jsx("rect", { width: f.width + 2 * s[0], x: -s[0], y: -s[1], height: f.height + 2 * s[1], className: "react-flow__edge-textbg", style: i, rx: l, ry: l }), a.jsx("text", { className: "react-flow__edge-text", y: f.height / 2, dy: "0.3em", ref: m, style: o, children: n }), u] }) : null;
}
Lc.displayName = "EdgeText";
const Lg = ve(Lc);
function Fn({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: l, labelBgBorderRadius: u, interactionWidth: c = 20, ...d }) {
  return a.jsxs(a.Fragment, { children: [a.jsx("path", { ...d, d: e, fill: "none", className: Ne(["react-flow__edge-path", d.className]) }), c ? a.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: c, className: "react-flow__edge-interaction" }) : null, o && Ye(t) && Ye(n) ? a.jsx(Lg, { x: t, y: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: l, labelBgBorderRadius: u }) : null] });
}
function Hs({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === ee.Left || e === ee.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function Hc({ sourceX: e, sourceY: t, sourcePosition: n = ee.Bottom, targetX: o, targetY: r, targetPosition: i = ee.Top }) {
  const [s, l] = Hs({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [u, c] = Hs({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [d, f, h, g] = rc({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: r,
    sourceControlX: s,
    sourceControlY: l,
    targetControlX: u,
    targetControlY: c
  });
  return [
    `M${e},${t} C${s},${l} ${u},${c} ${o},${r}`,
    d,
    f,
    h,
    g
  ];
}
function Vc(e) {
  return ve(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: l, label: u, labelStyle: c, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: g, style: m, markerEnd: x, markerStart: v, interactionWidth: y }) => {
    const [b, p, w] = Hc({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: l
    }), E = e.isInternal ? void 0 : t;
    return a.jsx(Fn, { id: E, path: b, labelX: p, labelY: w, label: u, labelStyle: c, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: g, style: m, markerEnd: x, markerStart: v, interactionWidth: y });
  });
}
const Hg = Vc({ isInternal: !1 }), Oc = Vc({ isInternal: !0 });
Hg.displayName = "SimpleBezierEdge";
Oc.displayName = "SimpleBezierEdgeInternal";
function Bc(e) {
  return ve(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: l, labelShowBg: u, labelBgStyle: c, labelBgPadding: d, labelBgBorderRadius: f, style: h, sourcePosition: g = ee.Bottom, targetPosition: m = ee.Top, markerEnd: x, markerStart: v, pathOptions: y, interactionWidth: b }) => {
    const [p, w, E] = Po({
      sourceX: n,
      sourceY: o,
      sourcePosition: g,
      targetX: r,
      targetY: i,
      targetPosition: m,
      borderRadius: y?.borderRadius,
      offset: y?.offset,
      stepPosition: y?.stepPosition
    }), k = e.isInternal ? void 0 : t;
    return a.jsx(Fn, { id: k, path: p, labelX: w, labelY: E, label: s, labelStyle: l, labelShowBg: u, labelBgStyle: c, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: x, markerStart: v, interactionWidth: b });
  });
}
const Fc = Bc({ isInternal: !1 }), Wc = Bc({ isInternal: !0 });
Fc.displayName = "SmoothStepEdge";
Wc.displayName = "SmoothStepEdgeInternal";
function Xc(e) {
  return ve(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return a.jsx(Fc, { ...n, id: o, pathOptions: me(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Vg = Xc({ isInternal: !1 }), Yc = Xc({ isInternal: !0 });
Vg.displayName = "StepEdge";
Yc.displayName = "StepEdgeInternal";
function qc(e) {
  return ve(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: l, labelShowBg: u, labelBgStyle: c, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: g, markerStart: m, interactionWidth: x }) => {
    const [v, y, b] = cc({ sourceX: n, sourceY: o, targetX: r, targetY: i }), p = e.isInternal ? void 0 : t;
    return a.jsx(Fn, { id: p, path: v, labelX: y, labelY: b, label: s, labelStyle: l, labelShowBg: u, labelBgStyle: c, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: g, markerStart: m, interactionWidth: x });
  });
}
const Og = qc({ isInternal: !1 }), Zc = qc({ isInternal: !0 });
Og.displayName = "StraightEdge";
Zc.displayName = "StraightEdgeInternal";
function Kc(e) {
  return ve(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s = ee.Bottom, targetPosition: l = ee.Top, label: u, labelStyle: c, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: g, style: m, markerEnd: x, markerStart: v, pathOptions: y, interactionWidth: b }) => {
    const [p, w, E] = ic({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: l,
      curvature: y?.curvature
    }), k = e.isInternal ? void 0 : t;
    return a.jsx(Fn, { id: k, path: p, labelX: w, labelY: E, label: u, labelStyle: c, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: g, style: m, markerEnd: x, markerStart: v, interactionWidth: b });
  });
}
const Bg = Kc({ isInternal: !1 }), Uc = Kc({ isInternal: !0 });
Bg.displayName = "BezierEdge";
Uc.displayName = "BezierEdgeInternal";
const Vs = {
  default: Uc,
  straight: Zc,
  step: Yc,
  smoothstep: Wc,
  simplebezier: Oc
}, Os = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, Fg = (e, t, n) => n === ee.Left ? e - t : n === ee.Right ? e + t : e, Wg = (e, t, n) => n === ee.Top ? e - t : n === ee.Bottom ? e + t : e, Bs = "react-flow__edgeupdater";
function Fs({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: i, onMouseOut: s, type: l }) {
  return a.jsx("circle", { onMouseDown: r, onMouseEnter: i, onMouseOut: s, className: Ne([Bs, `${Bs}-${l}`]), cx: Fg(t, o, e), cy: Wg(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function Xg({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: i, targetY: s, sourcePosition: l, targetPosition: u, onReconnect: c, onReconnectStart: d, onReconnectEnd: f, setReconnecting: h, setUpdateHover: g }) {
  const m = xe(), x = (w, E) => {
    if (w.button !== 0)
      return;
    const { autoPanOnConnect: k, domNode: _, connectionMode: D, connectionRadius: $, lib: F, onConnectStart: j, cancelConnection: z, nodeLookup: H, rfId: S, panBy: I, updateConnection: C } = m.getState(), A = E.type === "target", T = (O, B) => {
      h(!1), f?.(O, n, E.type, B);
    }, P = (O) => c?.(n, O), W = (O, B) => {
      h(!0), d?.(w, n, E.type), j?.(O, B);
    };
    ei.onPointerDown(w.nativeEvent, {
      autoPanOnConnect: k,
      connectionMode: D,
      connectionRadius: $,
      domNode: _,
      handleId: E.id,
      nodeId: E.nodeId,
      nodeLookup: H,
      isTarget: A,
      edgeUpdaterType: E.type,
      lib: F,
      flowId: S,
      cancelConnection: z,
      panBy: I,
      isValidConnection: (...O) => m.getState().isValidConnection?.(...O) ?? !0,
      onConnect: P,
      onConnectStart: W,
      onConnectEnd: (...O) => m.getState().onConnectEnd?.(...O),
      onReconnectEnd: T,
      updateConnection: C,
      getTransform: () => m.getState().transform,
      getFromHandle: () => m.getState().connection.fromHandle,
      dragThreshold: m.getState().connectionDragThreshold,
      handleDomNode: w.currentTarget
    });
  }, v = (w) => x(w, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), y = (w) => x(w, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), b = () => g(!0), p = () => g(!1);
  return a.jsxs(a.Fragment, { children: [(e === !0 || e === "source") && a.jsx(Fs, { position: l, centerX: o, centerY: r, radius: t, onMouseDown: v, onMouseEnter: b, onMouseOut: p, type: "source" }), (e === !0 || e === "target") && a.jsx(Fs, { position: u, centerX: i, centerY: s, radius: t, onMouseDown: y, onMouseEnter: b, onMouseOut: p, type: "target" })] });
}
function Yg({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: i, onContextMenu: s, onMouseEnter: l, onMouseMove: u, onMouseLeave: c, reconnectRadius: d, onReconnect: f, onReconnectStart: h, onReconnectEnd: g, rfId: m, edgeTypes: x, noPanClassName: v, onError: y, disableKeyboardA11y: b }) {
  let p = de((U) => U.edgeLookup.get(e));
  const w = de((U) => U.defaultEdgeOptions);
  p = w ? { ...w, ...p } : p;
  let E = p.type || "default", k = x?.[E] || Vs[E];
  k === void 0 && (y?.("011", He.error011(E)), E = "default", k = x?.default || Vs.default);
  const _ = !!(p.focusable || t && typeof p.focusable > "u"), D = typeof f < "u" && (p.reconnectable || n && typeof p.reconnectable > "u"), $ = !!(p.selectable || o && typeof p.selectable > "u"), F = ce(null), [j, z] = Y(!1), [H, S] = Y(!1), I = xe(), { zIndex: C, sourceX: A, sourceY: T, targetX: P, targetY: W, sourcePosition: O, targetPosition: B } = de(ge((U) => {
    const Q = U.nodeLookup.get(p.source), se = U.nodeLookup.get(p.target);
    if (!Q || !se)
      return {
        zIndex: p.zIndex,
        ...Os
      };
    const L = $h({
      id: e,
      sourceNode: Q,
      targetNode: se,
      sourceHandle: p.sourceHandle || null,
      targetHandle: p.targetHandle || null,
      connectionMode: U.connectionMode,
      onError: y
    });
    return {
      zIndex: Ch({
        selected: p.selected,
        zIndex: p.zIndex,
        sourceNode: Q,
        targetNode: se,
        elevateOnSelect: U.elevateEdgesOnSelect,
        zIndexMode: U.zIndexMode
      }),
      ...L || Os
    };
  }, [p.source, p.target, p.sourceHandle, p.targetHandle, p.selected, p.zIndex]), ye), G = me(() => p.markerStart ? `url('#${Jr(p.markerStart, m)}')` : void 0, [p.markerStart, m]), Z = me(() => p.markerEnd ? `url('#${Jr(p.markerEnd, m)}')` : void 0, [p.markerEnd, m]);
  if (p.hidden || A === null || T === null || P === null || W === null)
    return null;
  const te = (U) => {
    const { addSelectedEdges: Q, unselectNodesAndEdges: se, multiSelectionActive: L } = I.getState();
    $ && (I.setState({ nodesSelectionActive: !1 }), p.selected && L ? (se({ nodes: [], edges: [p] }), F.current?.blur()) : Q([e])), r && r(U, p);
  }, ae = i ? (U) => {
    i(U, { ...p });
  } : void 0, J = s ? (U) => {
    s(U, { ...p });
  } : void 0, R = l ? (U) => {
    l(U, { ...p });
  } : void 0, q = u ? (U) => {
    u(U, { ...p });
  } : void 0, re = c ? (U) => {
    c(U, { ...p });
  } : void 0, oe = (U) => {
    if (!b && Wa.includes(U.key) && $) {
      const { unselectNodesAndEdges: Q, addSelectedEdges: se } = I.getState();
      U.key === "Escape" ? (F.current?.blur(), Q({ edges: [p] })) : se([e]);
    }
  };
  return a.jsx("svg", { style: { zIndex: C }, children: a.jsxs("g", { className: Ne([
    "react-flow__edge",
    `react-flow__edge-${E}`,
    p.className,
    v,
    {
      selected: p.selected,
      animated: p.animated,
      inactive: !$ && !r,
      updating: j,
      selectable: $
    }
  ]), onClick: te, onDoubleClick: ae, onContextMenu: J, onMouseEnter: R, onMouseMove: q, onMouseLeave: re, onKeyDown: _ ? oe : void 0, tabIndex: _ ? 0 : void 0, role: p.ariaRole ?? (_ ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": p.ariaLabel === null ? void 0 : p.ariaLabel || `Edge from ${p.source} to ${p.target}`, "aria-describedby": _ ? `${Nc}-${m}` : void 0, ref: F, ...p.domAttributes, children: [!H && a.jsx(k, { id: e, source: p.source, target: p.target, type: p.type, selected: p.selected, animated: p.animated, selectable: $, deletable: p.deletable ?? !0, label: p.label, labelStyle: p.labelStyle, labelShowBg: p.labelShowBg, labelBgStyle: p.labelBgStyle, labelBgPadding: p.labelBgPadding, labelBgBorderRadius: p.labelBgBorderRadius, sourceX: A, sourceY: T, targetX: P, targetY: W, sourcePosition: O, targetPosition: B, data: p.data, style: p.style, sourceHandleId: p.sourceHandle, targetHandleId: p.targetHandle, markerStart: G, markerEnd: Z, pathOptions: "pathOptions" in p ? p.pathOptions : void 0, interactionWidth: p.interactionWidth }), D && a.jsx(Xg, { edge: p, isReconnectable: D, reconnectRadius: d, onReconnect: f, onReconnectStart: h, onReconnectEnd: g, sourceX: A, sourceY: T, targetX: P, targetY: W, sourcePosition: O, targetPosition: B, setUpdateHover: z, setReconnecting: S })] }) });
}
var qg = ve(Yg);
const Zg = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function Gc({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: i, onEdgeContextMenu: s, onEdgeMouseEnter: l, onEdgeMouseMove: u, onEdgeMouseLeave: c, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: h, onReconnectStart: g, onReconnectEnd: m, disableKeyboardA11y: x }) {
  const { edgesFocusable: v, edgesReconnectable: y, elementsSelectable: b, onError: p } = de(Zg, ye), w = Dg(t);
  return a.jsxs("div", { className: "react-flow__edges", children: [a.jsx(Rg, { defaultColor: e, rfId: n }), w.map((E) => a.jsx(qg, { id: E, edgesFocusable: v, edgesReconnectable: y, elementsSelectable: b, noPanClassName: r, onReconnect: i, onContextMenu: s, onMouseEnter: l, onMouseMove: u, onMouseLeave: c, onClick: d, reconnectRadius: f, onDoubleClick: h, onReconnectStart: g, onReconnectEnd: m, rfId: n, onError: p, edgeTypes: o, disableKeyboardA11y: x }, E))] });
}
Gc.displayName = "EdgeRenderer";
const Kg = ve(Gc), Ug = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function Gg({ children: e }) {
  const t = de(Ug);
  return a.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function Jg(e) {
  const t = ki(), n = ce(!1);
  ie(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Qg = (e) => e.panZoom?.syncViewport;
function em(e) {
  const t = de(Qg), n = xe();
  return ie(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function tm(e) {
  return e.connection.inProgress ? { ...e.connection, to: Ut(e.connection.to, e.transform) } : { ...e.connection };
}
function nm(e) {
  return tm;
}
function om(e) {
  const t = nm();
  return de(t, ye);
}
const rm = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function im({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: i, height: s, isValid: l, inProgress: u } = de(rm, ye);
  return !(i && r && u) ? null : a.jsx("svg", { style: e, width: i, height: s, className: "react-flow__connectionline react-flow__container", children: a.jsx("g", { className: Ne(["react-flow__connection", qa(l)]), children: a.jsx(Jc, { style: t, type: n, CustomComponent: o, isValid: l }) }) });
}
const Jc = ({ style: e, type: t = ut.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: i, fromNode: s, fromHandle: l, fromPosition: u, to: c, toNode: d, toHandle: f, toPosition: h, pointer: g } = om();
  if (!r)
    return;
  if (n)
    return a.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: s, fromHandle: l, fromX: i.x, fromY: i.y, toX: c.x, toY: c.y, fromPosition: u, toPosition: h, connectionStatus: qa(o), toNode: d, toHandle: f, pointer: g });
  let m = "";
  const x = {
    sourceX: i.x,
    sourceY: i.y,
    sourcePosition: u,
    targetX: c.x,
    targetY: c.y,
    targetPosition: h
  };
  switch (t) {
    case ut.Bezier:
      [m] = ic(x);
      break;
    case ut.SimpleBezier:
      [m] = Hc(x);
      break;
    case ut.Step:
      [m] = Po({
        ...x,
        borderRadius: 0
      });
      break;
    case ut.SmoothStep:
      [m] = Po(x);
      break;
    default:
      [m] = cc(x);
  }
  return a.jsx("path", { d: m, fill: "none", className: "react-flow__connection-path", style: e });
};
Jc.displayName = "ConnectionLine";
const sm = {};
function Ws(e = sm) {
  ce(e), xe(), ie(() => {
  }, [e]);
}
function am() {
  xe(), ce(!1), ie(() => {
  }, []);
}
function Qc({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: i, onEdgeDoubleClick: s, onNodeMouseEnter: l, onNodeMouseMove: u, onNodeMouseLeave: c, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: h, onSelectionEnd: g, connectionLineType: m, connectionLineStyle: x, connectionLineComponent: v, connectionLineContainerStyle: y, selectionKeyCode: b, selectionOnDrag: p, selectionMode: w, multiSelectionKeyCode: E, panActivationKeyCode: k, zoomActivationKeyCode: _, deleteKeyCode: D, onlyRenderVisibleElements: $, elementsSelectable: F, defaultViewport: j, translateExtent: z, minZoom: H, maxZoom: S, preventScrolling: I, defaultMarkerColor: C, zoomOnScroll: A, zoomOnPinch: T, panOnScroll: P, panOnScrollSpeed: W, panOnScrollMode: O, zoomOnDoubleClick: B, panOnDrag: G, autoPanOnSelection: Z, onPaneClick: te, onPaneMouseEnter: ae, onPaneMouseMove: J, onPaneMouseLeave: R, onPaneScroll: q, onPaneContextMenu: re, paneClickDistance: oe, nodeClickDistance: U, onEdgeContextMenu: Q, onEdgeMouseEnter: se, onEdgeMouseMove: L, onEdgeMouseLeave: K, reconnectRadius: fe, onReconnect: ue, onReconnectStart: Ie, onReconnectEnd: Oe, noDragClassName: Be, noWheelClassName: pt, noPanClassName: et, disableKeyboardA11y: Ee, nodeExtent: je, rfId: Pe, viewport: $e, onViewportChange: Ke }) {
  return Ws(e), Ws(t), am(), Jg(n), em($e), a.jsx(Sg, { onPaneClick: te, onPaneMouseEnter: ae, onPaneMouseMove: J, onPaneMouseLeave: R, onPaneContextMenu: re, onPaneScroll: q, paneClickDistance: oe, deleteKeyCode: D, selectionKeyCode: b, selectionOnDrag: p, selectionMode: w, onSelectionStart: h, onSelectionEnd: g, multiSelectionKeyCode: E, panActivationKeyCode: k, zoomActivationKeyCode: _, elementsSelectable: F, zoomOnScroll: A, zoomOnPinch: T, zoomOnDoubleClick: B, panOnScroll: P, panOnScrollSpeed: W, panOnScrollMode: O, panOnDrag: G, autoPanOnSelection: Z, defaultViewport: j, translateExtent: z, minZoom: H, maxZoom: S, onSelectionContextMenu: f, preventScrolling: I, noDragClassName: Be, noWheelClassName: pt, noPanClassName: et, disableKeyboardA11y: Ee, onViewportChange: Ke, isControlledViewport: !!$e, children: a.jsxs(Gg, { children: [a.jsx(Kg, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: s, onReconnect: ue, onReconnectStart: Ie, onReconnectEnd: Oe, onlyRenderVisibleElements: $, onEdgeContextMenu: Q, onEdgeMouseEnter: se, onEdgeMouseMove: L, onEdgeMouseLeave: K, reconnectRadius: fe, defaultMarkerColor: C, noPanClassName: et, disableKeyboardA11y: Ee, rfId: Pe }), a.jsx(im, { style: x, type: m, component: v, containerStyle: y }), a.jsx("div", { className: "react-flow__edgelabel-renderer" }), a.jsx(Mg, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: i, onNodeMouseEnter: l, onNodeMouseMove: u, onNodeMouseLeave: c, onNodeContextMenu: d, nodeClickDistance: U, onlyRenderVisibleElements: $, noPanClassName: et, noDragClassName: Be, disableKeyboardA11y: Ee, nodeExtent: je, rfId: Pe }), a.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Qc.displayName = "GraphView";
const cm = ve(Qc), lm = Ja(), Xs = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: l, minZoom: u = 0.5, maxZoom: c = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: h = "basic" } = {}) => {
  const g = /* @__PURE__ */ new Map(), m = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), v = /* @__PURE__ */ new Map(), y = o ?? t ?? [], b = n ?? e ?? [], p = d ?? [0, 0], w = f ?? Mn;
  dc(x, v, y);
  const { nodesInitialized: E } = Qr(b, g, m, {
    nodeOrigin: p,
    nodeExtent: w,
    zIndexMode: h
  });
  let k = [0, 0, 1];
  if (s && r && i) {
    const _ = On(g, {
      filter: (j) => !!((j.width || j.initialWidth) && (j.height || j.initialHeight))
    }), { x: D, y: $, zoom: F } = xi(_, r, i, u, c, l?.padding ?? 0.1);
    k = [D, $, F];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: i ?? 0,
    transform: k,
    nodes: b,
    nodesInitialized: E,
    nodeLookup: g,
    parentLookup: m,
    edges: y,
    edgeLookup: v,
    connectionLookup: x,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: o !== void 0,
    panZoom: null,
    minZoom: u,
    maxZoom: c,
    translateExtent: Mn,
    nodeExtent: w,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: Ot.Strict,
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
    fitViewOptions: l,
    fitViewResolver: null,
    connection: { ...Ya },
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
    ariaLabelConfig: Xa,
    zIndexMode: h,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, um = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: l, minZoom: u, maxZoom: c, nodeOrigin: d, nodeExtent: f, zIndexMode: h }) => Np((g, m) => {
  async function x() {
    const { nodeLookup: v, panZoom: y, fitViewOptions: b, fitViewResolver: p, width: w, height: E, minZoom: k, maxZoom: _ } = m();
    y && (await wh({
      nodes: v,
      width: w,
      height: E,
      panZoom: y,
      minZoom: k,
      maxZoom: _
    }, b), p?.resolve(!0), g({ fitViewResolver: null }));
  }
  return {
    ...Xs({
      nodes: e,
      edges: t,
      width: r,
      height: i,
      fitView: s,
      fitViewOptions: l,
      minZoom: u,
      maxZoom: c,
      nodeOrigin: d,
      nodeExtent: f,
      defaultNodes: n,
      defaultEdges: o,
      zIndexMode: h
    }),
    setNodes: (v) => {
      const { nodeLookup: y, parentLookup: b, nodeOrigin: p, elevateNodesOnSelect: w, fitViewQueued: E, zIndexMode: k, nodesSelectionActive: _ } = m(), { nodesInitialized: D, hasSelectedNodes: $ } = Qr(v, y, b, {
        nodeOrigin: p,
        nodeExtent: f,
        elevateNodesOnSelect: w,
        checkEquality: !0,
        zIndexMode: k
      }), F = _ && $;
      E && D ? (x(), g({
        nodes: v,
        nodesInitialized: D,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: F
      })) : g({ nodes: v, nodesInitialized: D, nodesSelectionActive: F });
    },
    setEdges: (v) => {
      const { connectionLookup: y, edgeLookup: b } = m();
      dc(y, b, v), g({ edges: v });
    },
    setDefaultNodesAndEdges: (v, y) => {
      if (v) {
        const { setNodes: b } = m();
        b(v), g({ hasDefaultNodes: !0 });
      }
      if (y) {
        const { setEdges: b } = m();
        b(y), g({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (v) => {
      const { triggerNodeChanges: y, nodeLookup: b, parentLookup: p, domNode: w, nodeOrigin: E, nodeExtent: k, debug: _, fitViewQueued: D, zIndexMode: $ } = m(), { changes: F, updatedInternals: j } = Bh(v, b, p, w, E, k, $);
      j && (Lh(b, p, { nodeOrigin: E, nodeExtent: k, zIndexMode: $ }), D ? (x(), g({ fitViewQueued: !1, fitViewOptions: void 0 })) : g({}), F?.length > 0 && (_ && console.log("React Flow: trigger node changes", F), y?.(F)));
    },
    updateNodePositions: (v, y = !1) => {
      const b = [];
      let p = [];
      const { nodeLookup: w, triggerNodeChanges: E, connection: k, updateConnection: _, onNodesChangeMiddlewareMap: D } = m();
      for (const [$, F] of v) {
        const j = w.get($), z = !!(j?.expandParent && j?.parentId && F?.position), H = {
          id: $,
          type: "position",
          position: z ? {
            x: Math.max(0, F.position.x),
            y: Math.max(0, F.position.y)
          } : F.position,
          dragging: y
        };
        if (j && k.inProgress && k.fromNode.id === j.id) {
          const S = It(j, k.fromHandle, ee.Left, !0);
          _({ ...k, from: S });
        }
        z && j.parentId && b.push({
          id: $,
          parentId: j.parentId,
          rect: {
            ...F.internals.positionAbsolute,
            width: F.measured.width ?? 0,
            height: F.measured.height ?? 0
          }
        }), p.push(H);
      }
      if (b.length > 0) {
        const { parentLookup: $, nodeOrigin: F } = m(), j = Ei(b, w, $, F);
        p.push(...j);
      }
      for (const $ of D.values())
        p = $(p);
      E(p);
    },
    triggerNodeChanges: (v) => {
      const { onNodesChange: y, setNodes: b, nodes: p, hasDefaultNodes: w, debug: E } = m();
      if (v?.length) {
        if (w) {
          const k = Cc(v, p);
          b(k);
        }
        E && console.log("React Flow: trigger node changes", v), y?.(v);
      }
    },
    triggerEdgeChanges: (v) => {
      const { onEdgesChange: y, setEdges: b, edges: p, hasDefaultEdges: w, debug: E } = m();
      if (v?.length) {
        if (w) {
          const k = _c(v, p);
          b(k);
        }
        E && console.log("React Flow: trigger edge changes", v), y?.(v);
      }
    },
    addSelectedNodes: (v) => {
      const { multiSelectionActive: y, edgeLookup: b, nodeLookup: p, triggerNodeChanges: w, triggerEdgeChanges: E } = m();
      if (y) {
        const k = v.map((_) => vt(_, !0));
        w(k);
        return;
      }
      w(zt(p, /* @__PURE__ */ new Set([...v]), !0)), E(zt(b));
    },
    addSelectedEdges: (v) => {
      const { multiSelectionActive: y, edgeLookup: b, nodeLookup: p, triggerNodeChanges: w, triggerEdgeChanges: E } = m();
      if (y) {
        const k = v.map((_) => vt(_, !0));
        E(k);
        return;
      }
      E(zt(b, /* @__PURE__ */ new Set([...v]))), w(zt(p, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: v, edges: y } = {}) => {
      const { edges: b, nodes: p, nodeLookup: w, triggerNodeChanges: E, triggerEdgeChanges: k } = m(), _ = v || p, D = y || b, $ = [];
      for (const j of _) {
        if (!j.selected)
          continue;
        const z = w.get(j.id);
        z && (z.selected = !1), $.push(vt(j.id, !1));
      }
      const F = [];
      for (const j of D)
        j.selected && F.push(vt(j.id, !1));
      E($), k(F);
    },
    setMinZoom: (v) => {
      const { panZoom: y, maxZoom: b } = m();
      y?.setScaleExtent([v, b]), g({ minZoom: v });
    },
    setMaxZoom: (v) => {
      const { panZoom: y, minZoom: b } = m();
      y?.setScaleExtent([b, v]), g({ maxZoom: v });
    },
    setTranslateExtent: (v) => {
      m().panZoom?.setTranslateExtent(v), g({ translateExtent: v });
    },
    resetSelectedElements: () => {
      const { edges: v, nodes: y, triggerNodeChanges: b, triggerEdgeChanges: p, elementsSelectable: w } = m();
      if (!w)
        return;
      const E = y.reduce((_, D) => D.selected ? [..._, vt(D.id, !1)] : _, []), k = v.reduce((_, D) => D.selected ? [..._, vt(D.id, !1)] : _, []);
      b(E), p(k);
    },
    setNodeExtent: (v) => {
      const { nodes: y, nodeLookup: b, parentLookup: p, nodeOrigin: w, elevateNodesOnSelect: E, nodeExtent: k, zIndexMode: _ } = m();
      v[0][0] === k[0][0] && v[0][1] === k[0][1] && v[1][0] === k[1][0] && v[1][1] === k[1][1] || (Qr(y, b, p, {
        nodeOrigin: w,
        nodeExtent: v,
        elevateNodesOnSelect: E,
        checkEquality: !1,
        zIndexMode: _
      }), g({ nodeExtent: v }));
    },
    panBy: (v) => {
      const { transform: y, width: b, height: p, panZoom: w, translateExtent: E } = m();
      return Fh({ delta: v, panZoom: w, transform: y, translateExtent: E, width: b, height: p });
    },
    setCenter: async (v, y, b) => {
      const { width: p, height: w, maxZoom: E, panZoom: k } = m();
      if (!k)
        return !1;
      const _ = typeof b?.zoom < "u" ? b.zoom : E;
      return await k.setViewport({
        x: p / 2 - v * _,
        y: w / 2 - y * _,
        zoom: _
      }, { duration: b?.duration, ease: b?.ease, interpolate: b?.interpolate }), !0;
    },
    cancelConnection: () => {
      g({
        connection: { ...Ya }
      });
    },
    updateConnection: (v) => {
      g({ connection: v });
    },
    reset: () => g({ ...Xs() })
  };
}, Object.is);
function dm({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: i, initialMinZoom: s, initialMaxZoom: l, initialFitViewOptions: u, fitView: c, nodeOrigin: d, nodeExtent: f, zIndexMode: h, children: g }) {
  const [m] = Y(() => um({
    nodes: e,
    edges: t,
    defaultNodes: n,
    defaultEdges: o,
    width: r,
    height: i,
    fitView: c,
    minZoom: s,
    maxZoom: l,
    fitViewOptions: u,
    nodeOrigin: d,
    nodeExtent: f,
    zIndexMode: h
  }));
  return a.jsx(_p, { value: m, children: a.jsx(Gp, { children: g }) });
}
function fm({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: i, height: s, fitView: l, fitViewOptions: u, minZoom: c, maxZoom: d, nodeOrigin: f, nodeExtent: h, zIndexMode: g }) {
  return Rn(qo) ? a.jsx(a.Fragment, { children: e }) : a.jsx(dm, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: i, initialHeight: s, fitView: l, initialFitViewOptions: u, initialMinZoom: c, initialMaxZoom: d, nodeOrigin: f, nodeExtent: h, zIndexMode: g, children: e });
}
const hm = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function pm({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: i, edgeTypes: s, onNodeClick: l, onEdgeClick: u, onInit: c, onMove: d, onMoveStart: f, onMoveEnd: h, onConnect: g, onConnectStart: m, onConnectEnd: x, onClickConnectStart: v, onClickConnectEnd: y, onNodeMouseEnter: b, onNodeMouseMove: p, onNodeMouseLeave: w, onNodeContextMenu: E, onNodeDoubleClick: k, onNodeDragStart: _, onNodeDrag: D, onNodeDragStop: $, onNodesDelete: F, onEdgesDelete: j, onDelete: z, onSelectionChange: H, onSelectionDragStart: S, onSelectionDrag: I, onSelectionDragStop: C, onSelectionContextMenu: A, onSelectionStart: T, onSelectionEnd: P, onBeforeDelete: W, connectionMode: O, connectionLineType: B = ut.Bezier, connectionLineStyle: G, connectionLineComponent: Z, connectionLineContainerStyle: te, deleteKeyCode: ae = "Backspace", selectionKeyCode: J = "Shift", selectionOnDrag: R = !1, selectionMode: q = Dn.Full, panActivationKeyCode: re = "Space", multiSelectionKeyCode: oe = $n() ? "Meta" : "Control", zoomActivationKeyCode: U = $n() ? "Meta" : "Control", snapToGrid: Q, snapGrid: se, onlyRenderVisibleElements: L = !1, selectNodesOnDrag: K, nodesDraggable: fe, autoPanOnNodeFocus: ue, nodesConnectable: Ie, nodesFocusable: Oe, nodeOrigin: Be = Ec, edgesFocusable: pt, edgesReconnectable: et, elementsSelectable: Ee = !0, defaultViewport: je = Vp, minZoom: Pe = 0.5, maxZoom: $e = 2, translateExtent: Ke = Mn, preventScrolling: jt = !0, nodeExtent: tt, defaultMarkerColor: gt = "#b1b1b7", zoomOnScroll: mt = !0, zoomOnPinch: Yn = !0, panOnScroll: Te = !1, panOnScrollSpeed: qn = 0.5, panOnScrollMode: we = Nt.Free, zoomOnDoubleClick: Fe = !0, panOnDrag: Gt = !0, onPaneClick: ke, onPaneMouseEnter: Jt, onPaneMouseMove: Qt, onPaneMouseLeave: _e, onPaneScroll: yt, onPaneContextMenu: at, paneClickDistance: Jo = 1, nodeClickDistance: Zn = 0, children: Kn, onReconnect: en, onReconnectStart: Qo, onReconnectEnd: tn, onEdgeContextMenu: At, onEdgeDoubleClick: ze, onEdgeMouseEnter: nn, onEdgeMouseMove: on, onEdgeMouseLeave: rn, reconnectRadius: Mt = 10, onNodesChange: er, onEdgesChange: tr, noDragClassName: nr = "nodrag", noWheelClassName: or = "nowheel", noPanClassName: sn = "nopan", fitView: an, fitViewOptions: cn, connectOnClick: rr, attributionPosition: ln, proOptions: ir, defaultEdgeOptions: sr, elevateNodesOnSelect: ar = !0, elevateEdgesOnSelect: cr = !1, disableKeyboardA11y: Un = !1, autoPanOnConnect: un, autoPanOnNodeDrag: lr, autoPanOnSelection: ur = !0, autoPanSpeed: dr, connectionRadius: fr, isValidConnection: hr, onError: pr, style: gr, id: dn, nodeDragThreshold: Gn, connectionDragThreshold: mr, viewport: yr, onViewportChange: xr, width: wr, height: vr, colorMode: Jn = "light", debug: Qn, onScroll: fn, ariaLabelConfig: eo, zIndexMode: hn = "basic", ...br }, Sr) {
  const Dt = dn || "1", to = Wp(Jn), no = ge((oo) => {
    oo.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), fn?.(oo);
  }, [fn]);
  return a.jsx("div", { "data-testid": "rf__wrapper", ...br, onScroll: no, style: { ...gr, ...hm }, ref: Sr, className: Ne(["react-flow", r, to]), id: dn, role: "application", children: a.jsxs(fm, { nodes: e, edges: t, width: wr, height: vr, fitView: an, fitViewOptions: cn, minZoom: Pe, maxZoom: $e, nodeOrigin: Be, nodeExtent: tt, zIndexMode: hn, children: [a.jsx(Fp, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: g, onConnectStart: m, onConnectEnd: x, onClickConnectStart: v, onClickConnectEnd: y, nodesDraggable: fe, autoPanOnNodeFocus: ue, nodesConnectable: Ie, nodesFocusable: Oe, edgesFocusable: pt, edgesReconnectable: et, elementsSelectable: Ee, elevateNodesOnSelect: ar, elevateEdgesOnSelect: cr, minZoom: Pe, maxZoom: $e, nodeExtent: tt, onNodesChange: er, onEdgesChange: tr, snapToGrid: Q, snapGrid: se, connectionMode: O, translateExtent: Ke, connectOnClick: rr, defaultEdgeOptions: sr, fitView: an, fitViewOptions: cn, onNodesDelete: F, onEdgesDelete: j, onDelete: z, onNodeDragStart: _, onNodeDrag: D, onNodeDragStop: $, onSelectionDrag: I, onSelectionDragStart: S, onSelectionDragStop: C, onMove: d, onMoveStart: f, onMoveEnd: h, noPanClassName: sn, nodeOrigin: Be, rfId: Dt, autoPanOnConnect: un, autoPanOnNodeDrag: lr, autoPanSpeed: dr, onError: pr, connectionRadius: fr, isValidConnection: hr, selectNodesOnDrag: K, nodeDragThreshold: Gn, connectionDragThreshold: mr, onBeforeDelete: W, debug: Qn, ariaLabelConfig: eo, zIndexMode: hn }), a.jsx(cm, { onInit: c, onNodeClick: l, onEdgeClick: u, onNodeMouseEnter: b, onNodeMouseMove: p, onNodeMouseLeave: w, onNodeContextMenu: E, onNodeDoubleClick: k, nodeTypes: i, edgeTypes: s, connectionLineType: B, connectionLineStyle: G, connectionLineComponent: Z, connectionLineContainerStyle: te, selectionKeyCode: J, selectionOnDrag: R, selectionMode: q, deleteKeyCode: ae, multiSelectionKeyCode: oe, panActivationKeyCode: re, zoomActivationKeyCode: U, onlyRenderVisibleElements: L, defaultViewport: je, translateExtent: Ke, minZoom: Pe, maxZoom: $e, preventScrolling: jt, zoomOnScroll: mt, zoomOnPinch: Yn, zoomOnDoubleClick: Fe, panOnScroll: Te, panOnScrollSpeed: qn, panOnScrollMode: we, panOnDrag: Gt, autoPanOnSelection: ur, onPaneClick: ke, onPaneMouseEnter: Jt, onPaneMouseMove: Qt, onPaneMouseLeave: _e, onPaneScroll: yt, onPaneContextMenu: at, paneClickDistance: Jo, nodeClickDistance: Zn, onSelectionContextMenu: A, onSelectionStart: T, onSelectionEnd: P, onReconnect: en, onReconnectStart: Qo, onReconnectEnd: tn, onEdgeContextMenu: At, onEdgeDoubleClick: ze, onEdgeMouseEnter: nn, onEdgeMouseMove: on, onEdgeMouseLeave: rn, reconnectRadius: Mt, defaultMarkerColor: gt, noDragClassName: nr, noWheelClassName: or, noPanClassName: sn, rfId: Dt, disableKeyboardA11y: Un, nodeExtent: tt, viewport: yr, onViewportChange: xr }), a.jsx(Hp, { onSelectionChange: H }), Kn, a.jsx($p, { proOptions: ir, position: ln }), a.jsx(Pp, { rfId: Dt, disableKeyboardA11y: Un })] }) });
}
var gm = Ac(pm);
const mm = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function ym({ children: e }) {
  const t = de(mm);
  return t ? Cp.createPortal(e, t) : null;
}
function xm({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return a.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ne(["react-flow__background-pattern", n, o]) });
}
function wm({ radius: e, className: t }) {
  return a.jsx("circle", { cx: e, cy: e, r: e, className: Ne(["react-flow__background-pattern", "dots", t]) });
}
var dt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(dt || (dt = {}));
const vm = {
  [dt.Dots]: 1,
  [dt.Lines]: 1,
  [dt.Cross]: 6
}, bm = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function el({
  id: e,
  variant: t = dt.Dots,
  // only used for dots and cross
  gap: n = 20,
  // only used for lines and cross
  size: o,
  lineWidth: r = 1,
  offset: i = 0,
  color: s,
  bgColor: l,
  style: u,
  className: c,
  patternClassName: d
}) {
  const f = ce(null), { transform: h, patternId: g } = de(bm, ye), m = o || vm[t], x = t === dt.Dots, v = t === dt.Cross, y = Array.isArray(n) ? n : [n, n], b = [y[0] * h[2] || 1, y[1] * h[2] || 1], p = m * h[2], w = Array.isArray(i) ? i : [i, i], E = v ? [p, p] : b, k = [
    w[0] * h[2] || 1 + E[0] / 2,
    w[1] * h[2] || 1 + E[1] / 2
  ], _ = `${g}${e || ""}`;
  return a.jsxs("svg", { className: Ne(["react-flow__background", c]), style: {
    ...u,
    ...Ko,
    "--xy-background-color-props": l,
    "--xy-background-pattern-color-props": s
  }, ref: f, "data-testid": "rf__background", children: [a.jsx("pattern", { id: _, x: h[0] % b[0], y: h[1] % b[1], width: b[0], height: b[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${k[0]},-${k[1]})`, children: x ? a.jsx(wm, { radius: p / 2, className: d }) : a.jsx(xm, { dimensions: E, lineWidth: r, variant: t, className: d }) }), a.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${_})` })] });
}
el.displayName = "Background";
const Sm = ve(el);
function Nm() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: a.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function Em() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: a.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function km() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: a.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function Cm() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: a.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function _m() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: a.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function ho({ children: e, className: t, ...n }) {
  return a.jsx("button", { type: "button", className: Ne(["react-flow__controls-button", t]), ...n, children: e });
}
const Im = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function tl({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: i, onZoomOut: s, onFitView: l, onInteractiveChange: u, className: c, children: d, position: f = "bottom-left", orientation: h = "vertical", "aria-label": g }) {
  const m = xe(), { isInteractive: x, minZoomReached: v, maxZoomReached: y, ariaLabelConfig: b } = de(Im, ye), { zoomIn: p, zoomOut: w, fitView: E } = ki(), k = () => {
    p(), i?.();
  }, _ = () => {
    w(), s?.();
  }, D = () => {
    E(r), l?.();
  }, $ = () => {
    m.setState({
      nodesDraggable: !x,
      nodesConnectable: !x,
      elementsSelectable: !x
    }), u?.(!x);
  }, F = h === "horizontal" ? "horizontal" : "vertical";
  return a.jsxs(Zo, { className: Ne(["react-flow__controls", F, c]), position: f, style: e, "data-testid": "rf__controls", "aria-label": g ?? b["controls.ariaLabel"], children: [t && a.jsxs(a.Fragment, { children: [a.jsx(ho, { onClick: k, className: "react-flow__controls-zoomin", title: b["controls.zoomIn.ariaLabel"], "aria-label": b["controls.zoomIn.ariaLabel"], disabled: y, children: a.jsx(Nm, {}) }), a.jsx(ho, { onClick: _, className: "react-flow__controls-zoomout", title: b["controls.zoomOut.ariaLabel"], "aria-label": b["controls.zoomOut.ariaLabel"], disabled: v, children: a.jsx(Em, {}) })] }), n && a.jsx(ho, { className: "react-flow__controls-fitview", onClick: D, title: b["controls.fitView.ariaLabel"], "aria-label": b["controls.fitView.ariaLabel"], children: a.jsx(km, {}) }), o && a.jsx(ho, { className: "react-flow__controls-interactive", onClick: $, title: b["controls.interactive.ariaLabel"], "aria-label": b["controls.interactive.ariaLabel"], children: x ? a.jsx(_m, {}) : a.jsx(Cm, {}) }), d] });
}
tl.displayName = "Controls";
const jm = ve(tl);
function Am({ id: e, x: t, y: n, width: o, height: r, style: i, color: s, strokeColor: l, strokeWidth: u, className: c, borderRadius: d, shapeRendering: f, selected: h, onClick: g }) {
  const { background: m, backgroundColor: x } = i || {}, v = s || m || x;
  return a.jsx("rect", { className: Ne(["react-flow__minimap-node", { selected: h }, c]), x: t, y: n, rx: d, ry: d, width: o, height: r, style: {
    fill: v,
    stroke: l,
    strokeWidth: u
  }, shapeRendering: f, onClick: g ? (y) => g(y, e) : void 0 });
}
const Mm = ve(Am), Dm = (e) => e.nodes.map((t) => t.id), Hr = (e) => e instanceof Function ? e : () => e;
function Pm({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: i = Mm,
  onClick: s
}) {
  const l = de(Dm, ye), u = Hr(t), c = Hr(e), d = Hr(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return a.jsx(a.Fragment, { children: l.map((h) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    a.jsx(Tm, { id: h, nodeColorFunc: u, nodeStrokeColorFunc: c, nodeClassNameFunc: d, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: i, onClick: s, shapeRendering: f }, h)
  )) });
}
function $m({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: i, shapeRendering: s, NodeComponent: l, onClick: u }) {
  const { node: c, x: d, y: f, width: h, height: g } = de((m) => {
    const x = m.nodeLookup.get(e);
    if (!x)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const v = x.internals.userNode, { x: y, y: b } = x.internals.positionAbsolute, { width: p, height: w } = st(v);
    return {
      node: v,
      x: y,
      y: b,
      width: p,
      height: w
    };
  }, ye);
  return !c || c.hidden || !Qa(c) ? null : a.jsx(l, { x: d, y: f, width: h, height: g, style: c.style, selected: !!c.selected, className: o(c), color: t(c), borderRadius: r, strokeColor: n(c), strokeWidth: i, shapeRendering: s, onClick: u, id: c.id });
}
const Tm = ve($m);
var zm = ve(Pm);
const Rm = 200, Lm = 150, Hm = (e) => !e.hidden, Vm = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? Ga(On(e.nodeLookup, { filter: Hm }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, Om = "react-flow__minimap-desc";
function nl({
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
  nodeComponent: l,
  bgColor: u,
  maskColor: c,
  maskStrokeColor: d,
  maskStrokeWidth: f,
  position: h = "bottom-right",
  onClick: g,
  onNodeClick: m,
  pannable: x = !1,
  zoomable: v = !1,
  ariaLabel: y,
  inversePan: b,
  zoomStep: p = 1,
  offsetScale: w = 5
}) {
  const E = xe(), k = ce(null), { boundingRect: _, viewBB: D, rfId: $, panZoom: F, translateExtent: j, flowWidth: z, flowHeight: H, ariaLabelConfig: S } = de(Vm, ye), I = e?.width ?? Rm, C = e?.height ?? Lm, A = _.width / I, T = _.height / C, P = Math.max(A, T), W = P * I, O = P * C, B = w * P, G = _.x - (W - _.width) / 2 - B, Z = _.y - (O - _.height) / 2 - B, te = W + B * 2, ae = O + B * 2, J = `${Om}-${$}`, R = ce(0), q = ce();
  R.current = P, ie(() => {
    if (k.current && F)
      return q.current = Jh({
        domNode: k.current,
        panZoom: F,
        getTransform: () => E.getState().transform,
        getViewScale: () => R.current
      }), () => {
        q.current?.destroy();
      };
  }, [F]), ie(() => {
    q.current?.update({
      translateExtent: j,
      width: z,
      height: H,
      inversePan: b,
      pannable: x,
      zoomStep: p,
      zoomable: v
    });
  }, [x, v, b, p, j, z, H]);
  const re = g ? (Q) => {
    const [se, L] = q.current?.pointer(Q) || [0, 0];
    g(Q, { x: se, y: L });
  } : void 0, oe = m ? ge((Q, se) => {
    const L = E.getState().nodeLookup.get(se).internals.userNode;
    m(Q, L);
  }, []) : void 0, U = y ?? S["minimap.ariaLabel"];
  return a.jsx(Zo, { position: h, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof c == "string" ? c : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * P : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof s == "number" ? s : void 0
  }, className: Ne(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: a.jsxs("svg", { width: I, height: C, viewBox: `${G} ${Z} ${te} ${ae}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": J, ref: k, onClick: re, children: [U && a.jsx("title", { id: J, children: U }), a.jsx(zm, { onClick: oe, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: i, nodeClassName: r, nodeStrokeWidth: s, nodeComponent: l }), a.jsx("path", { className: "react-flow__minimap-mask", d: `M${G - B},${Z - B}h${te + B * 2}v${ae + B * 2}h${-te - B * 2}z
        M${D.x},${D.y}h${D.width}v${D.height}h${-D.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
nl.displayName = "MiniMap";
const Bm = ve(nl), Fm = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, Wm = {
  [Xt.Line]: "right",
  [Xt.Handle]: "bottom-right"
};
function Xm({ nodeId: e, position: t, variant: n = Xt.Handle, className: o, style: r = void 0, children: i, color: s, minWidth: l = 10, minHeight: u = 10, maxWidth: c = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: h, autoScale: g = !0, shouldResize: m, onResizeStart: x, onResize: v, onResizeEnd: y }) {
  const b = $c(), p = typeof e == "string" ? e : b, w = xe(), E = ce(null), k = n === Xt.Handle, _ = de(ge(Fm(k && g), [k, g]), ye), D = ce(null), $ = t ?? Wm[n];
  ie(() => {
    if (!(!E.current || !p))
      return D.current || (D.current = dp({
        domNode: E.current,
        nodeId: p,
        getStoreItems: () => {
          const { nodeLookup: j, transform: z, snapGrid: H, snapToGrid: S, nodeOrigin: I, domNode: C } = w.getState();
          return {
            nodeLookup: j,
            transform: z,
            snapGrid: H,
            snapToGrid: S,
            nodeOrigin: I,
            paneDomNode: C
          };
        },
        onChange: (j, z) => {
          const { triggerNodeChanges: H, nodeLookup: S, parentLookup: I, nodeOrigin: C } = w.getState(), A = [], T = { x: j.x, y: j.y }, P = S.get(p);
          if (P && P.expandParent && P.parentId) {
            const W = P.origin ?? C, O = j.width ?? P.measured.width ?? 0, B = j.height ?? P.measured.height ?? 0, G = {
              id: P.id,
              parentId: P.parentId,
              rect: {
                width: O,
                height: B,
                ...ec({
                  x: j.x ?? P.position.x,
                  y: j.y ?? P.position.y
                }, { width: O, height: B }, P.parentId, S, W)
              }
            }, Z = Ei([G], S, I, C);
            A.push(...Z), T.x = j.x ? Math.max(W[0] * O, j.x) : void 0, T.y = j.y ? Math.max(W[1] * B, j.y) : void 0;
          }
          if (T.x !== void 0 && T.y !== void 0) {
            const W = {
              id: p,
              type: "position",
              position: { ...T }
            };
            A.push(W);
          }
          if (j.width !== void 0 && j.height !== void 0) {
            const O = {
              id: p,
              type: "dimensions",
              resizing: !0,
              setAttributes: h ? h === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: j.width,
                height: j.height
              }
            };
            A.push(O);
          }
          for (const W of z) {
            const O = {
              ...W,
              type: "position"
            };
            A.push(O);
          }
          H(A);
        },
        onEnd: ({ width: j, height: z }) => {
          const H = {
            id: p,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: j,
              height: z
            }
          };
          w.getState().triggerNodeChanges([H]);
        }
      })), D.current.update({
        controlPosition: $,
        boundaries: {
          minWidth: l,
          minHeight: u,
          maxWidth: c,
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
    l,
    u,
    c,
    d,
    f,
    x,
    v,
    y,
    m
  ]);
  const F = $.split("-");
  return a.jsx("div", { className: Ne(["react-flow__resize-control", "nodrag", ...F, n, o]), ref: E, style: {
    ...r,
    scale: _,
    ...s && { [k ? "backgroundColor" : "borderColor"]: s }
  }, children: i });
}
ve(Xm);
const Ym = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), ol = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var qm = {
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
const Zm = Lo(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: o,
    className: r = "",
    children: i,
    iconNode: s,
    ...l
  }, u) => Br(
    "svg",
    {
      ref: u,
      ...qm,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: ol("lucide", r),
      ...l
    },
    [
      ...s.map(([c, d]) => Br(c, d)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const be = (e, t) => {
  const n = Lo(
    ({ className: o, ...r }, i) => Br(Zm, {
      ref: i,
      iconNode: t,
      className: ol(`lucide-${Ym(e)}`, o),
      ...r
    })
  );
  return n.displayName = `${e}`, n;
};
const rl = be("Boxes", [
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
const Wn = be("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const Km = be("ChevronDown", [
  ["path", { d: "m6 9 6 6 6-6", key: "qrunsl" }]
]);
const ni = be("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const $t = be("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const ft = be("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const il = be("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const Um = be("GripVertical", [
  ["circle", { cx: "9", cy: "12", r: "1", key: "1vctgf" }],
  ["circle", { cx: "9", cy: "5", r: "1", key: "hp0tcf" }],
  ["circle", { cx: "9", cy: "19", r: "1", key: "fkjjf6" }],
  ["circle", { cx: "15", cy: "12", r: "1", key: "1tmaij" }],
  ["circle", { cx: "15", cy: "5", r: "1", key: "19l28e" }],
  ["circle", { cx: "15", cy: "19", r: "1", key: "f4zoj3" }]
]);
const sl = be("ListTree", [
  ["path", { d: "M21 12h-8", key: "1bmf0i" }],
  ["path", { d: "M21 6H8", key: "1pqkrb" }],
  ["path", { d: "M21 18h-8", key: "1tm79t" }],
  ["path", { d: "M3 6v4c0 1.1.9 2 2 2h3", key: "1ywdgy" }],
  ["path", { d: "M3 10v6c0 1.1.9 2 2 2h3", key: "2wc746" }]
]);
const To = be("Maximize2", [
  ["polyline", { points: "15 3 21 3 21 9", key: "mznyad" }],
  ["polyline", { points: "9 21 3 21 3 15", key: "1avn1i" }],
  ["line", { x1: "21", x2: "14", y1: "3", y2: "10", key: "ota7mn" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const Ys = be("Minimize2", [
  ["polyline", { points: "4 14 10 14 10 20", key: "11kfnr" }],
  ["polyline", { points: "20 10 14 10 14 4", key: "rlmsce" }],
  ["line", { x1: "14", x2: "21", y1: "10", y2: "3", key: "o5lafz" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const _i = be("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const Ii = be("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const Gm = be("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const Jm = be("Save", [
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
const Qm = be("Search", [
  ["circle", { cx: "11", cy: "11", r: "8", key: "4ej97u" }],
  ["path", { d: "m21 21-4.3-4.3", key: "1qie3q" }]
]);
const qt = be("Sparkles", [
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
const ey = be("Terminal", [
  ["polyline", { points: "4 17 10 11 4 5", key: "akl6gq" }],
  ["line", { x1: "12", x2: "20", y1: "19", y2: "19", key: "q2wloq" }]
]);
const oi = be("Trash2", [
  ["path", { d: "M3 6h18", key: "d0wm0j" }],
  ["path", { d: "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", key: "4alrt4" }],
  ["path", { d: "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", key: "v07s0e" }],
  ["line", { x1: "10", x2: "10", y1: "11", y2: "17", key: "1uufr5" }],
  ["line", { x1: "14", x2: "14", y1: "11", y2: "17", key: "xtxkd" }]
]);
const ty = be("X", [
  ["path", { d: "M18 6 6 18", key: "1bl5f8" }],
  ["path", { d: "m6 6 12 12", key: "d8bk6v" }]
]);
const ny = be("Zap", [
  [
    "path",
    {
      d: "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z",
      key: "1xq2db"
    }
  ]
]), Ve = "/_elsa/workflow-management", oy = "/_elsa/publishing";
async function ry(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${Ve}/definitions?${n.toString()}`);
}
async function iy(e, t) {
  return e.http.getJson(`${Ve}/definitions/${encodeURIComponent(t)}`);
}
async function sy(e, t) {
  return e.http.postJson(`${Ve}/definitions`, t);
}
async function ay(e, t) {
  await e.http.deleteJson(`${Ve}/definitions/${encodeURIComponent(t)}`);
}
async function cy(e, t) {
  await e.http.postJson(`${Ve}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function ly(e, t) {
  await e.http.deleteJson(`${Ve}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function uy(e, t) {
  return e.http.putJson(`${Ve}/drafts/${encodeURIComponent(t.id)}`, { state: t.state, layout: t.layout });
}
async function dy(e, t) {
  return e.http.postJson(`${Ve}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function fy(e, t) {
  return e.http.postJson(`${Ve}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function hy(e, t) {
  try {
    return await e.http.postJson(`${oy}/workflows/drafts/test-runs`, t);
  } catch (n) {
    const o = vy(n);
    if (o) return o;
    throw n;
  }
}
async function py(e, t) {
  return e.http.postJson(`${Ve}/executables/${encodeURIComponent(t)}/run`, {});
}
async function gy(e) {
  return e.http.getJson("/_demo/workflows/executables");
}
async function my(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const o = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${o ? `?${o}` : ""}`);
}
async function yy(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function al(e) {
  return e.http.getJson(`${Ve}/activities`);
}
async function xy(e) {
  const t = await cl(e, [
    `${Ve}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? qs(t) : qs(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function wy(e) {
  const t = await cl(e, [
    `${Ve}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : bo;
}
async function cl(e, t) {
  let n;
  for (const o of t)
    try {
      return await e.http.getJson(o);
    } catch (r) {
      n = r;
    }
  throw n;
}
function qs(e) {
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
function vy(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = Zs(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return Zs(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function Zs(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const bo = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
], Uo = "elsa.sequence.structure", Xn = "elsa.flowchart.structure";
function ll(e, t) {
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
function Vr(e, t) {
  const n = ll(e, t);
  if (!n) return null;
  let o = Je(n)[0];
  return o ? { owner: n, slot: o } : null;
}
function Je(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = Oy(t), r = Or(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: By(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, i]) => Or(i)).map(([i, s]) => ({
    id: `${t.kind}:${i}`,
    label: Wy(i),
    property: i,
    mode: "generic",
    activities: Or(s) ?? []
  }));
}
function by(e, t, n) {
  const o = new Map(t.map((s) => [s.activityVersionId, s])), r = new Map(n.map((s) => [s.nodeId, s])), i = e.slot.activities.map((s, l) => {
    const u = o.get(s.activityVersionId), c = r.get(s.nodeId) ?? Fy(e.slot.mode, l);
    return fl(s, u, { x: c.x, y: c.y });
  });
  return {
    nodes: i,
    edges: e.slot.mode === "flowchart" ? $y(e.owner) : Py(e.slot, i)
  };
}
function Sy(e, t, n) {
  const o = t.find((i) => i.activityVersionId === e.activityVersionId), r = n.find((i) => i.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [fl(e, o, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function Ny(e, t) {
  return e?.structure?.kind === Xn || Iy(t) ? "flowchart" : e?.structure?.kind === Uo || jy(t) ? "sequence" : "unsupported";
}
function ri(e, t, n) {
  if (t.length === 0) {
    const l = Je(e)[0];
    return l ? zn(e, l, n) : e;
  }
  const [o, ...r] = t, i = Je(e).find((l) => l.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((l) => l.nodeId === o.ownerNodeId ? ri(l, r, n) : l);
  return zn(e, i, s);
}
function ul(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, i = Je(e).find((l) => l.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((l) => l.nodeId === o.ownerNodeId ? ul(l, r, n) : l);
  return zn(e, i, s);
}
function dl(e, t, n) {
  if (e.nodeId === t) return n(e);
  const o = Je(e);
  if (o.length === 0) return e;
  let r = !1, i = e;
  for (const s of o) {
    const l = s.activities.map((u) => {
      const c = dl(u, t, n);
      return c !== u && (r = !0), c;
    });
    r && (i = zn(i, s, l));
  }
  return r ? i : e;
}
function zn(e, t, n) {
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
function Ey(e, t, n, o = []) {
  const r = new Map(e.slot.activities.map((s) => [s.nodeId, s]));
  for (const s of o)
    r.set(s.nodeId, s);
  const i = t.map((s) => r.get(s.id)).filter((s) => !!s);
  return e.slot.mode === "sequence" && i.sort((s, l) => {
    const u = t.find((d) => d.id === s.nodeId), c = t.find((d) => d.id === l.nodeId);
    return (u?.position.x ?? 0) - (c?.position.x ?? 0);
  }), zn(e.owner, e.slot, i);
}
function ky(e, t) {
  return {
    ...e,
    structure: Dy(e.structure, t)
  };
}
function Cy(e, t) {
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
function Ks(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: My(e)
  };
}
function Ce(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? Ay(t) : n;
}
function fl(e, t, n, o = {}) {
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
      icon: ii(t),
      childSlots: Je(e),
      acceptsInbound: Ty(e, t),
      sourcePorts: o.suppressFlowPorts ? [] : hl(e, t),
      suppressFlowPorts: o.suppressFlowPorts
    }
  };
}
function ii(e) {
  if (!e) return "activity";
  const t = _y(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), o = Ce(e).toLowerCase(), r = e.category?.toLowerCase() ?? "", i = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || o === "flowchart" ? "flowchart" : n.endsWith(".sequence") || o === "sequence" ? "sequence" : n.includes("writeline") || o.includes("write line") ? "terminal" : r.includes("runtime") ? "runtime" : i === "trigger" ? "trigger" : "activity";
}
function _y(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function Iy(e) {
  return !!e && (Ce(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function jy(e) {
  return !!e && (Ce(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function Ay(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function My(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: Uo,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: Xn,
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
function Dy(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], o = /* @__PURE__ */ new Map();
  for (const r of n) {
    if (!ji(r)) continue;
    const i = r.id;
    typeof i == "string" && o.set(i, r);
  }
  return {
    ...e,
    payload: {
      ...e.payload,
      connections: t.map((r) => {
        const i = o.get(r.id) ?? {}, s = r.data?.vertices, { vertices: l, ...u } = i;
        return {
          ...u,
          id: r.id,
          source: { nodeId: r.source, port: r.sourceHandle ?? "Done" },
          target: r.targetHandle ? { nodeId: r.target, port: r.targetHandle } : { nodeId: r.target },
          ...s?.length ? { vertices: s.map((c) => ({ x: Math.round(c.x), y: Math.round(c.y) })) } : {}
        };
      })
    }
  };
}
function Py(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function $y(e) {
  if (e.structure?.kind !== Xn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, i = n.target;
    if (!r?.nodeId || !i?.nodeId) return null;
    const s = Array.isArray(n.vertices) ? n.vertices.filter(Hy) : [];
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
function hl(e, t) {
  const n = Us(e.cases);
  if (Ry(e, t) && n.length > 0)
    return [...n.map((i) => ({ name: i, displayName: i })), { name: "Default", displayName: "Default" }];
  const o = [
    ...So(t?.designFacets),
    ...So(t?.ports),
    ...So(t?.outputs)
  ];
  if (o.length > 0) return Ly(o);
  const r = Us(e.outcomes);
  return r.length > 0 ? r.map((i) => ({ name: i, displayName: i })) : [{ name: "Done", displayName: "Done" }];
}
function Ty(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function zo(e, t, n, o) {
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
function zy(e, t, n) {
  const o = zo(t.source, n, t.sourceHandle ?? "Done", void 0), r = zo(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((i) => i.id !== t.id).concat(o, r);
}
function Or(e) {
  return Array.isArray(e) ? e.filter(Vy) : null;
}
function Ry(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function So(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!ji(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...So(n.ports));
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
function Ly(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function Us(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function Hy(e) {
  return ji(e) && typeof e.x == "number" && typeof e.y == "number";
}
function ji(e) {
  return typeof e == "object" && e !== null;
}
function Vy(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Oy(e) {
  return e.kind === Uo ? "sequence" : e.kind === Xn ? "flowchart" : "generic";
}
function By(e) {
  return e.kind === Uo || e.kind === Xn, "Activities";
}
function Fy(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function Wy(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Xy = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function Yy(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function Ai(e) {
  return Yy(e.name);
}
function qy(e, t) {
  const n = Ai(t), o = e[n];
  return t.isWrapped === !1 ? o ?? t.defaultValue ?? "" : gl(o, t);
}
function pl(e, t) {
  return gl(e[Ai(t)], t);
}
function Zy(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function Ky(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function Gs(e, t, n) {
  return {
    ...e,
    [Ai(t)]: n
  };
}
function Uy(e, t) {
  return t.isWrapped === !1 ? qy(e, t) : pl(e, t).expression.value;
}
function gl(e, t) {
  return Gy(e) ? {
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
function Gy(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
const ml = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]);
function Jy({
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
  const s = t.inputs.filter((c) => c.isBrowsable !== !1).sort((c, d) => (c.order ?? 0) - (d.order ?? 0) || c.name.localeCompare(d.name));
  if (s.length === 0)
    return /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "This activity does not expose editable properties." });
  const l = n0(s), u = o.length > 0 ? o : Xy;
  return /* @__PURE__ */ a.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ a.jsx("span", { className: "wf-section-label", children: "Properties" }),
    l.map((c) => /* @__PURE__ */ a.jsxs("section", { className: "wf-property-group", children: [
      l.length > 1 ? /* @__PURE__ */ a.jsx("h4", { children: c.category }) : null,
      c.inputs.map((d) => /* @__PURE__ */ a.jsx(
        Qy,
        {
          activity: e,
          input: d,
          editors: n,
          expressionDescriptors: u,
          onChange: i
        },
        d.name
      ))
    ] }, c.category))
  ] });
}
function Qy({
  activity: e,
  input: t,
  editors: n,
  expressionDescriptors: o,
  onChange: r
}) {
  const i = t.isReadOnly === !0, s = { activity: e, expressionDescriptors: o, readOnly: i }, l = t0(n, t, s), u = l?.component, c = t.isWrapped !== !1 ? pl(e, t) : null, d = c?.expression.type ?? "Literal", f = Uy(e, t), h = !!(c && o0(t, l?.id)), g = !!(c && r0(t, l?.id)), [m, x] = Y(!1), v = (b) => {
    const p = c ? Zy(c, b) : b;
    r(Gs(e, t, p));
  }, y = (b) => {
    c && r(Gs(e, t, Ky(c, b)));
  };
  return /* @__PURE__ */ a.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ a.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ a.jsx("span", { children: yl(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ a.jsx("p", { children: t.description }) : null,
    c && !h ? /* @__PURE__ */ a.jsx(
      si,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: d,
        descriptors: o,
        disabled: i,
        onChange: y
      }
    ) : null,
    h ? /* @__PURE__ */ a.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ a.jsx("div", { className: "wf-expression-editor", children: Js(u, t, f, i, s, v) }),
      /* @__PURE__ */ a.jsx(
        si,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: d,
          descriptors: o,
          disabled: i,
          variant: "inline",
          onChange: y
        }
      ),
      g ? /* @__PURE__ */ a.jsx(
        "button",
        {
          type: "button",
          className: "wf-expression-expand-button",
          "aria-label": `Open expanded ${t.displayName || t.name} editor`,
          title: "Open expanded editor",
          onClick: () => x(!0),
          children: /* @__PURE__ */ a.jsx(To, { size: 13 })
        }
      ) : null
    ] }) : Js(u, t, f, i, s, v),
    g && !h ? /* @__PURE__ */ a.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => x(!0),
        children: [
          /* @__PURE__ */ a.jsx(To, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    m ? /* @__PURE__ */ a.jsx(
      e0,
      {
        input: t,
        value: f,
        syntax: d,
        descriptors: o,
        disabled: i,
        onChange: v,
        onSyntaxChange: y,
        onClose: () => x(!1)
      }
    ) : null
  ] });
}
function e0({
  input: e,
  value: t,
  syntax: n,
  descriptors: o,
  disabled: r,
  onChange: i,
  onSyntaxChange: s,
  onClose: l
}) {
  const u = ha(), c = e.displayName || e.name;
  return ie(() => {
    const d = (f) => {
      f.key === "Escape" && l();
    };
    return window.addEventListener("keydown", d), () => window.removeEventListener("keydown", d);
  }, [l]), /* @__PURE__ */ a.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ a.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": u, children: [
    /* @__PURE__ */ a.jsxs("header", { children: [
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ a.jsx("h3", { id: u, children: c })
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", "aria-label": `Close ${c} editor`, onClick: l, children: /* @__PURE__ */ a.jsx(ty, { size: 16 }) })
    ] }),
    /* @__PURE__ */ a.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ a.jsx(
          si,
          {
            label: `${c} expression syntax`,
            value: n,
            descriptors: o,
            disabled: r,
            onChange: s
          }
        ),
        /* @__PURE__ */ a.jsx("span", { children: yl(e.typeName) })
      ] }),
      e.description ? /* @__PURE__ */ a.jsx("p", { children: e.description }) : null,
      /* @__PURE__ */ a.jsx(
        "textarea",
        {
          "aria-label": `${c} expanded value`,
          value: t == null ? "" : String(t),
          disabled: r,
          spellCheck: !1,
          onChange: (d) => i(d.target.value)
        }
      )
    ] }),
    /* @__PURE__ */ a.jsxs("footer", { children: [
      /* @__PURE__ */ a.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: l, children: "Close" })
    ] })
  ] }) });
}
function Js(e, t, n, o, r, i) {
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
function si({
  label: e,
  value: t,
  descriptors: n,
  disabled: o,
  variant: r = "block",
  onChange: i
}) {
  const [s, l] = Y(!1), u = ha(), c = n.find((f) => f.type === t), d = [
    "wf-syntax-picker-trigger",
    r === "inline" ? "inline" : "",
    s ? "open" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ a.jsxs("div", { className: r === "inline" ? "wf-syntax-picker inline" : "wf-syntax-picker", onBlur: (f) => {
    f.currentTarget.contains(f.relatedTarget) || l(!1);
  }, children: [
    /* @__PURE__ */ a.jsx(
      "button",
      {
        type: "button",
        className: d,
        "aria-label": e,
        "aria-haspopup": "listbox",
        "aria-expanded": s,
        "aria-controls": u,
        disabled: o,
        onClick: () => l((f) => !f),
        children: /* @__PURE__ */ a.jsx("span", { children: c?.displayName || c?.type || t })
      }
    ),
    s ? /* @__PURE__ */ a.jsx("div", { id: u, role: "listbox", className: "wf-syntax-picker-menu", "aria-label": e, children: n.map((f) => {
      const h = f.displayName || f.type, g = f.type === t;
      return /* @__PURE__ */ a.jsx(
        "button",
        {
          type: "button",
          role: "option",
          "aria-selected": g,
          className: g ? "selected" : "",
          onClick: () => {
            i(f.type), l(!1);
          },
          children: h
        },
        f.type
      );
    }) }) : null
  ] });
}
function t0(e, t, n) {
  return [...e].sort((o, r) => (o.order ?? 500) - (r.order ?? 500)).find((o) => o.supports(t, n));
}
function n0(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "General";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return [...t.entries()].map(([n, o]) => ({ category: n, inputs: o }));
}
function yl(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function o0(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !ml.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function r0(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !ml.has(t) && n !== "multiline") return !1;
  const o = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(o) || n === "singleline" || n === "multiline";
}
const i0 = { workflowActivity: z0 }, s0 = { workflow: L0 }, Qs = "application/x-elsa-activity-version-id", a0 = 6, c0 = 1200, l0 = [10, 25, 50], u0 = 10, ea = "elsa-studio-workflow-palette-width", ta = "elsa-studio-workflow-inspector-width", na = "elsa-studio-workflow-palette-collapsed", oa = "elsa-studio-workflow-inspector-collapsed", xl = "elsa-studio-workflow-side-panel-maximized", yn = 180, xn = 460, d0 = 260, wn = 260, vn = 560, f0 = 320, ra = 42, po = 16, wl = ht.createContext(null);
function K0(e) {
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
        component: () => /* @__PURE__ */ a.jsx(h0, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), workflowDesignerPanels: e.workflowDesigner.panels.list() })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ a.jsx(p0, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow instances",
        component: () => /* @__PURE__ */ a.jsx(g0, { context: e.backend, ai: e.ai })
      }
    ]
  });
}
function h0({
  context: e,
  ai: t,
  propertyEditors: n,
  workflowDesignerPanels: o
}) {
  const [r, i] = Y(ia);
  ie(() => {
    const l = () => i(ia());
    return window.addEventListener("popstate", l), () => window.removeEventListener("popstate", l);
  }, []);
  const s = (l) => {
    const u = l ? `/workflows/definitions?definition=${encodeURIComponent(l)}` : "/workflows/definitions";
    window.history.pushState({}, "", u), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return r ? /* @__PURE__ */ a.jsx(T0, { context: e, definitionId: r, ai: t, propertyEditors: n, workflowDesignerPanels: o, onBack: () => s(null) }) : /* @__PURE__ */ a.jsx(Mi, { activePath: "/workflows/definitions", title: "Definitions", children: /* @__PURE__ */ a.jsx(m0, { context: e, ai: t, onOpen: s }) });
}
function p0({ context: e, ai: t }) {
  const [n, o] = Y(sa);
  return ie(() => {
    const r = () => o(sa());
    return window.addEventListener("popstate", r), () => window.removeEventListener("popstate", r);
  }, []), /* @__PURE__ */ a.jsx(Mi, { activePath: "/workflows/executables", title: "Executables", children: /* @__PURE__ */ a.jsx(x0, { context: e, ai: t, definitionFilter: n }) });
}
function g0({ context: e, ai: t }) {
  return /* @__PURE__ */ a.jsx(Mi, { activePath: "/workflows/instances", title: "Instances", children: /* @__PURE__ */ a.jsx(w0, { context: e, ai: t }) });
}
function Mi({ activePath: e, title: t, children: n }) {
  const o = (r) => {
    window.history.pushState({}, "", r), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ a.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ a.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ a.jsxs("div", { children: [
      /* @__PURE__ */ a.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ a.jsx("h2", { children: t })
    ] }) }),
    /* @__PURE__ */ a.jsxs("nav", { className: "wf-section-tabs", "aria-label": "Workflow views", children: [
      /* @__PURE__ */ a.jsx("a", { className: e === "/workflows/definitions" ? "active" : "", href: "/workflows/definitions", onClick: (r) => {
        r.preventDefault(), o("/workflows/definitions");
      }, children: "Definitions" }),
      /* @__PURE__ */ a.jsx("a", { className: e === "/workflows/executables" ? "active" : "", href: "/workflows/executables", onClick: (r) => {
        r.preventDefault(), o("/workflows/executables");
      }, children: "Executables" }),
      /* @__PURE__ */ a.jsx("a", { className: e === "/workflows/instances" ? "active" : "", href: "/workflows/instances", onClick: (r) => {
        r.preventDefault(), o("/workflows/instances");
      }, children: "Instances" })
    ] }),
    n
  ] });
}
function ia() {
  return new URLSearchParams(window.location.search).get("definition");
}
function sa() {
  return new URLSearchParams(window.location.search).get("definition");
}
function m0({ context: e, ai: t, onOpen: n }) {
  const [o, r] = Y(""), [i, s] = Y("active"), [l, u] = Y(1), [c, d] = Y(u0), [f, h] = Y("loading"), [g, m] = Y(""), [x, v] = Y(""), [y, b] = Y([]), [p, w] = Y(0), [E, k] = Y(() => /* @__PURE__ */ new Set()), [_, D] = Y(null), [$, F] = Y(!1), [j, z] = Y([]), [H, S] = Y("idle"), I = ce(null), C = me(() => y.map((L) => L.id), [y]), A = Zt(t, "weaver.workflows.suggest-create-metadata"), T = Zt(t, "weaver.workflows.explain-definition"), P = C.filter((L) => E.has(L)).length, W = C.length > 0 && P === C.length, O = ge(async () => {
    h("loading"), m("");
    try {
      const L = await ry(e, { search: o, state: i, page: l, pageSize: c }), K = typeof L.totalCount == "number", fe = L.totalCount ?? L.definitions.length, ue = vl(fe, c);
      if (fe > 0 && l > ue) {
        u(ue);
        return;
      }
      b(K ? L.definitions : E0(L.definitions, l, c)), w(fe), h("ready");
    } catch (L) {
      m(L instanceof Error ? L.message : String(L)), h("failed");
    }
  }, [e, o, i, l, c]);
  ie(() => {
    O();
  }, [O]), ie(() => {
    I.current && (I.current.indeterminate = P > 0 && !W);
  }, [W, P]);
  const B = ge(async () => {
    if (!(H === "loading" || H === "ready")) {
      S("loading");
      try {
        const L = await al(e);
        z(L.activities ?? []), S("ready");
      } catch (L) {
        S("failed"), m(L instanceof Error ? L.message : String(L));
      }
    }
  }, [H, e]), G = () => {
    m(""), v(""), D({ name: "", description: "", rootKind: "flowchart" }), B();
  }, Z = async () => {
    if (_?.name.trim()) {
      F(!0), m(""), v("");
      try {
        const L = await sy(e, {
          name: _.name.trim(),
          description: _.description.trim() || null,
          rootKind: _.rootKind,
          rootActivityVersionId: _0(_, j)
        });
        D(null), n(L.definition.id);
      } catch (L) {
        m(L instanceof Error ? L.message : String(L));
      } finally {
        F(!1);
      }
    }
  }, te = (L) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(L)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, ae = async () => {
    if (y.length === 1 && l > 1) {
      u(l - 1);
      return;
    }
    await O();
  }, J = () => k(/* @__PURE__ */ new Set()), R = (L, K) => {
    k((fe) => {
      const ue = new Set(fe);
      return K ? ue.add(L) : ue.delete(L), ue;
    });
  }, q = (L) => {
    k((K) => {
      const fe = new Set(K);
      for (const ue of C)
        L ? fe.add(ue) : fe.delete(ue);
      return fe;
    });
  }, re = (L) => {
    s(L), u(1), J();
  }, oe = (L) => {
    r(L), u(1), J();
  }, U = async (L) => {
    if (window.confirm(`Delete workflow definition "${L.name}"? You can restore it from the Deleted view.`)) {
      v(""), m("");
      try {
        await ay(e, L.id), R(L.id, !1), v(`Deleted ${L.name}`), await ae();
      } catch (K) {
        m(K instanceof Error ? K.message : String(K));
      }
    }
  }, Q = async (L) => {
    v(""), m("");
    try {
      await cy(e, L.id), R(L.id, !1), v(`Restored ${L.name}`), await ae();
    } catch (K) {
      m(K instanceof Error ? K.message : String(K));
    }
  }, se = async (L) => {
    if (window.confirm(`Permanently delete workflow definition "${L.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`)) {
      v(""), m("");
      try {
        await ly(e, L.id), R(L.id, !1), v(`Permanently deleted ${L.name}`), await ae();
      } catch (K) {
        m(K instanceof Error ? K.message : String(K));
      }
    }
  };
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ a.jsx("button", { type: "button", className: i === "active" ? "active" : "", "aria-selected": i === "active", onClick: () => re("active"), children: "Active" }),
        /* @__PURE__ */ a.jsx("button", { type: "button", className: i === "deleted" ? "active" : "", "aria-selected": i === "deleted", onClick: () => re("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ a.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ a.jsx(Qm, { size: 15 }),
        /* @__PURE__ */ a.jsx("input", { value: o, onChange: (L) => oe(L.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
        O();
      }, children: "Refresh" }),
      /* @__PURE__ */ a.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ a.jsxs("button", { type: "button", title: "Create workflow", onClick: G, children: [
        /* @__PURE__ */ a.jsx(Ii, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(ft, { size: 16 }),
      " ",
      g
    ] }) : null,
    f !== "failed" && g ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(ft, { size: 16 }),
      " ",
      g
    ] }) : null,
    x ? /* @__PURE__ */ a.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ a.jsx(Wn, { size: 14 }),
      " ",
      x
    ] }) : null,
    E.size > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ a.jsxs("span", { children: [
        E.size,
        " selected"
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: J, children: "Clear selection" })
    ] }) : null,
    f === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading workflow definitions..." }) : null,
    f === "ready" && y.length === 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-empty", children: [
      "No ",
      i,
      " workflow definitions found."
    ] }) : null,
    f === "ready" && y.length > 0 ? /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ a.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ a.jsx(
            "input",
            {
              ref: I,
              type: "checkbox",
              checked: W,
              onChange: (L) => q(L.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ a.jsx("span", { children: "Name" }),
          /* @__PURE__ */ a.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ a.jsx("span", { children: i === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ a.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ a.jsx("span", { children: "Actions" })
        ] }),
        y.map((L) => /* @__PURE__ */ a.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${L.name}`,
            "aria-selected": E.has(L.id),
            tabIndex: 0,
            onClick: () => n(L.id),
            onKeyDown: (K) => {
              K.currentTarget === K.target && (K.key !== "Enter" && K.key !== " " || (K.preventDefault(), n(L.id)));
            },
            children: [
              /* @__PURE__ */ a.jsx("label", { className: "wf-row-select", onClick: (K) => K.stopPropagation(), children: /* @__PURE__ */ a.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: E.has(L.id),
                  onChange: (K) => R(L.id, K.target.checked),
                  "aria-label": `Select workflow definition ${L.name}`
                }
              ) }),
              /* @__PURE__ */ a.jsxs("span", { children: [
                /* @__PURE__ */ a.jsx("strong", { children: L.name }),
                /* @__PURE__ */ a.jsx("small", { children: L.description || L.id })
              ] }),
              /* @__PURE__ */ a.jsx("span", { children: L.latestVersion ?? "No version" }),
              /* @__PURE__ */ a.jsx("span", { children: i === "deleted" ? rt(L.deletedAt) : L.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ a.jsx("span", { children: rt(L.lastModifiedAt) }),
              /* @__PURE__ */ a.jsx("span", { className: "wf-row-actions", onClick: (K) => K.stopPropagation(), children: i === "active" ? /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
                /* @__PURE__ */ a.jsx("button", { type: "button", onClick: (K) => {
                  K.stopPropagation(), n(L.id);
                }, children: "Open" }),
                /* @__PURE__ */ a.jsx("button", { type: "button", onClick: (K) => {
                  K.stopPropagation(), te(L.id);
                }, children: "Artifacts" }),
                T ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => Kt(t, T, L), children: [
                  /* @__PURE__ */ a.jsx(qt, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ a.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  U(L);
                }, children: [
                  /* @__PURE__ */ a.jsx(oi, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
                /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
                  Q(L);
                }, children: [
                  /* @__PURE__ */ a.jsx(Gm, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ a.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  se(L);
                }, children: [
                  /* @__PURE__ */ a.jsx(oi, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          L.id
        ))
      ] }),
      /* @__PURE__ */ a.jsx(
        N0,
        {
          page: l,
          pageSize: c,
          totalCount: p,
          onPageChange: u,
          onPageSizeChange: (L) => {
            d(L), u(1);
          }
        }
      )
    ] }) : null,
    _ ? /* @__PURE__ */ a.jsx(
      y0,
      {
        draft: _,
        activities: j,
        catalogState: H,
        creating: $,
        suggestMetadataAction: A,
        onSuggestMetadata: A ? () => Kt(t, A, { draft: _, activities: j }) : void 0,
        onChange: (L) => D(L),
        onClose: () => D(null),
        onSubmit: Z
      }
    ) : null
  ] });
}
function y0({ draft: e, activities: t, catalogState: n, creating: o, suggestMetadataAction: r, onSuggestMetadata: i, onChange: s, onClose: l, onSubmit: u }) {
  const c = me(() => k0(t), [t]), d = C0(e, t), f = (h) => {
    if (h.startsWith("kind:")) {
      s({ ...e, rootKind: h.slice(5), rootActivityVersionId: null });
      return;
    }
    const g = t.find((m) => m.activityVersionId === h);
    s({
      ...e,
      rootKind: bl(g) ?? e.rootKind,
      rootActivityVersionId: h
    });
  };
  return /* @__PURE__ */ a.jsx("div", { className: "wf-dialog-backdrop", role: "presentation", children: /* @__PURE__ */ a.jsx("section", { className: "wf-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "workflow-create-title", children: /* @__PURE__ */ a.jsxs(
    "form",
    {
      onSubmit: (h) => {
        h.preventDefault(), u();
      },
      children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-dialog-heading", children: [
          /* @__PURE__ */ a.jsx("h3", { id: "workflow-create-title", children: "Create Workflow" }),
          r ? /* @__PURE__ */ a.jsxs("button", { type: "button", className: "wf-ai-action", onClick: i, title: r.description ?? r.label, children: [
            /* @__PURE__ */ a.jsx(qt, { size: 13 }),
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
              value: d,
              onChange: (h) => f(h.target.value),
              disabled: n === "loading",
              children: [
                /* @__PURE__ */ a.jsx("optgroup", { label: "Composite roots", children: c.compositeRoots.map((h) => /* @__PURE__ */ a.jsx("option", { value: h.value, children: h.label }, h.value)) }),
                c.otherCategories.map((h) => /* @__PURE__ */ a.jsx("optgroup", { label: h.name, children: h.activities.map((g) => /* @__PURE__ */ a.jsx("option", { value: g.activityVersionId, children: Ce(g) }, g.activityVersionId)) }, h.name))
              ]
            }
          )
        ] }),
        n === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-dialog-note", children: "Loading activity catalog..." }) : null,
        n === "failed" ? /* @__PURE__ */ a.jsx("div", { className: "wf-dialog-note", children: "Activity catalog could not be loaded. Composite roots remain available." }) : null,
        /* @__PURE__ */ a.jsxs("div", { className: "wf-dialog-actions", children: [
          /* @__PURE__ */ a.jsx("button", { type: "button", onClick: l, disabled: o, children: "Cancel" }),
          /* @__PURE__ */ a.jsx("button", { type: "submit", disabled: o || !e.name.trim(), children: o ? "Creating..." : "Create" })
        ] })
      ]
    }
  ) }) });
}
function x0({ context: e, ai: t, definitionFilter: n }) {
  const [o, r] = Y("loading"), [i, s] = Y(""), [l, u] = Y(""), [c, d] = Y([]), f = me(
    () => n ? c.filter((x) => x.definitionId === n || x.sourceId === n) : c,
    [n, c]
  ), h = Zt(t, "weaver.workflows.explain-executable"), g = ge(async () => {
    r("loading"), s("");
    try {
      d(await gy(e)), r("ready");
    } catch (x) {
      s(x instanceof Error ? x.message : String(x)), r("failed");
    }
  }, [e]);
  ie(() => {
    g();
  }, [g]);
  const m = async (x) => {
    u(""), s("");
    try {
      await py(e, x.artifactId), u(`Started ${x.artifactId}`);
    } catch (v) {
      s(v instanceof Error ? v.message : String(v));
    }
  };
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
        g();
      }, children: "Refresh" }),
      n ? /* @__PURE__ */ a.jsxs("span", { className: "wf-filter-chip", children: [
        "Definition ",
        n
      ] }) : null
    ] }),
    o === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(ft, { size: 16 }),
      " ",
      i
    ] }) : null,
    l ? /* @__PURE__ */ a.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ a.jsx(Wn, { size: 14 }),
      " ",
      l
    ] }) : null,
    o === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading workflow executables..." }) : null,
    o === "ready" && f.length === 0 ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: n ? "No workflow executables found for this definition." : "No workflow executables found. Publish a workflow definition to create one." }) : null,
    o === "ready" && f.length > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ a.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ a.jsx("span", { children: "Version" }),
        /* @__PURE__ */ a.jsx("span", { children: "Source" }),
        /* @__PURE__ */ a.jsx("span", { children: "Root" }),
        /* @__PURE__ */ a.jsx("span", { children: "Published" }),
        /* @__PURE__ */ a.jsx("span", { children: "Actions" })
      ] }),
      f.map((x) => /* @__PURE__ */ a.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ a.jsxs("span", { children: [
          /* @__PURE__ */ a.jsx("strong", { children: x.artifactId }),
          /* @__PURE__ */ a.jsx("small", { children: x.artifactHash })
        ] }),
        /* @__PURE__ */ a.jsx("span", { children: x.artifactVersion }),
        /* @__PURE__ */ a.jsx("span", { children: j0(x) }),
        /* @__PURE__ */ a.jsx("span", { children: A0(x) }),
        /* @__PURE__ */ a.jsx("span", { children: rt(x.publishedAt ?? x.createdAt) }),
        /* @__PURE__ */ a.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
            m(x);
          }, children: [
            /* @__PURE__ */ a.jsx(_i, { size: 13 }),
            " Run"
          ] }),
          h ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => Kt(t, h, x), children: [
            /* @__PURE__ */ a.jsx(qt, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, x.artifactId))
    ] }) : null
  ] });
}
function w0({ context: e, ai: t }) {
  const [n, o] = Y("loading"), [r, i] = Y(""), [s, l] = Y(""), [u, c] = Y([]), [d, f] = Y(null), [h, g] = Y(null), [m, x] = Y("idle"), [v, y] = Y(""), b = Zt(t, "weaver.workflows.explain-instance"), p = ge(async () => {
    o("loading"), i("");
    try {
      const E = await my(e, { status: s || void 0, take: 100 });
      c(E), o("ready"), f((k) => k && E.some((_) => _.workflowExecutionId === k) ? k : E[0]?.workflowExecutionId ?? null);
    } catch (E) {
      i(E instanceof Error ? E.message : String(E)), c([]), o("failed");
    }
  }, [e, s]);
  ie(() => {
    p();
  }, [p]), ie(() => {
    let E = !1;
    return g(null), y(""), d ? (x("loading"), yy(e, d).then((k) => {
      E || (g(k), x("ready"));
    }).catch((k) => {
      E || (y(k instanceof Error ? k.message : String(k)), x("failed"));
    }), () => {
      E = !0;
    }) : (x("idle"), () => {
      E = !0;
    });
  }, [e, d]);
  const w = u.find((E) => E.workflowExecutionId === d) ?? null;
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
        p();
      }, children: "Refresh" }),
      /* @__PURE__ */ a.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ a.jsx("span", { children: "Status" }),
        /* @__PURE__ */ a.jsxs("select", { "aria-label": "Workflow instance status", value: s, onChange: (E) => l(E.target.value), children: [
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
    n === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(ft, { size: 16 }),
      " ",
      r
    ] }) : null,
    n === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading workflow instances..." }) : null,
    n === "ready" && u.length === 0 ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "No workflow instances found. Run a published workflow executable to create instance history." }) : null,
    n === "ready" && u.length > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-workbench", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-grid wf-instance-grid", role: "table", "aria-label": "Workflow instances", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ a.jsx("span", { children: "Instance" }),
          /* @__PURE__ */ a.jsx("span", { children: "Status" }),
          /* @__PURE__ */ a.jsx("span", { children: "Definition" }),
          /* @__PURE__ */ a.jsx("span", { children: "Activity" }),
          /* @__PURE__ */ a.jsx("span", { children: "Started" }),
          /* @__PURE__ */ a.jsx("span", { children: "Duration" })
        ] }),
        u.map((E) => /* @__PURE__ */ a.jsxs(
          "button",
          {
            type: "button",
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Inspect workflow instance ${E.workflowExecutionId}`,
            "aria-selected": E.workflowExecutionId === d,
            onClick: () => f(E.workflowExecutionId),
            children: [
              /* @__PURE__ */ a.jsxs("span", { children: [
                /* @__PURE__ */ a.jsx("strong", { children: E.workflowExecutionId }),
                /* @__PURE__ */ a.jsx("small", { children: E.artifactId })
              ] }),
              /* @__PURE__ */ a.jsx("span", { children: /* @__PURE__ */ a.jsx(Di, { status: E.status, subStatus: E.subStatus }) }),
              /* @__PURE__ */ a.jsxs("span", { children: [
                /* @__PURE__ */ a.jsx("strong", { children: E.definitionId }),
                /* @__PURE__ */ a.jsx("small", { children: E.definitionVersionId })
              ] }),
              /* @__PURE__ */ a.jsxs("span", { children: [
                /* @__PURE__ */ a.jsxs("strong", { children: [
                  E.activityCount,
                  " activities"
                ] }),
                /* @__PURE__ */ a.jsxs("small", { children: [
                  E.incidentCount,
                  " incidents"
                ] })
              ] }),
              /* @__PURE__ */ a.jsx("span", { children: rt(E.startedAt ?? E.createdAt) }),
              /* @__PURE__ */ a.jsx("span", { children: Y0(E.startedAt ?? E.createdAt, E.completedAt ?? E.updatedAt) })
            ]
          },
          E.workflowExecutionId
        ))
      ] }),
      /* @__PURE__ */ a.jsx(
        v0,
        {
          ai: t,
          action: b,
          summary: w,
          details: h,
          state: m,
          error: v
        }
      )
    ] }) : null
  ] });
}
function v0({ ai: e, action: t, summary: n, details: o, state: r, error: i }) {
  return n ? /* @__PURE__ */ a.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Workflow instance details", children: [
    /* @__PURE__ */ a.jsxs("header", { children: [
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("span", { children: "Workflow instance" }),
        /* @__PURE__ */ a.jsx("h3", { children: n.workflowExecutionId })
      ] }),
      t ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => Kt(e, t, o ?? n), children: [
        /* @__PURE__ */ a.jsx(qt, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    /* @__PURE__ */ a.jsxs("dl", { className: "wf-instance-meta", children: [
      /* @__PURE__ */ a.jsx("dt", { children: "Status" }),
      /* @__PURE__ */ a.jsx("dd", { children: /* @__PURE__ */ a.jsx(Di, { status: n.status, subStatus: n.subStatus }) }),
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
      /* @__PURE__ */ a.jsx("dd", { children: rt(n.createdAt) }),
      /* @__PURE__ */ a.jsx("dt", { children: "Started" }),
      /* @__PURE__ */ a.jsx("dd", { children: rt(n.startedAt) }),
      /* @__PURE__ */ a.jsx("dt", { children: "Completed" }),
      /* @__PURE__ */ a.jsx("dd", { children: rt(n.completedAt) }),
      /* @__PURE__ */ a.jsx("dt", { children: "Correlation" }),
      /* @__PURE__ */ a.jsx("dd", { children: n.correlationId || "None" })
    ] }),
    r === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading instance details..." }) : null,
    r === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(ft, { size: 16 }),
      " ",
      i
    ] }) : null,
    r === "ready" && o ? /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
      /* @__PURE__ */ a.jsx(b0, { activities: o.activities }),
      /* @__PURE__ */ a.jsx(S0, { incidents: o.incidents })
    ] }) : null
  ] }) : /* @__PURE__ */ a.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Select a workflow instance to inspect activity history." }) });
}
function b0({ activities: e }) {
  return /* @__PURE__ */ a.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ a.jsx("h4", { children: "Activity history" }),
    e.length === 0 ? /* @__PURE__ */ a.jsx("p", { children: "No activity executions recorded yet." }) : null,
    e.length > 0 ? /* @__PURE__ */ a.jsx("div", { className: "wf-instance-activity-list", children: e.map((t) => /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-activity", children: [
      /* @__PURE__ */ a.jsx("span", { children: /* @__PURE__ */ a.jsx(Di, { status: t.status, subStatus: t.subStatus }) }),
      /* @__PURE__ */ a.jsx("strong", { children: Cl(t.activityType) ?? t.activityType }),
      /* @__PURE__ */ a.jsx("small", { children: t.activityExecutionId }),
      /* @__PURE__ */ a.jsx("time", { children: rt(t.scheduledAt) })
    ] }, t.activityExecutionId)) }) : null
  ] });
}
function S0({ incidents: e }) {
  return /* @__PURE__ */ a.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ a.jsx("h4", { children: "Incidents" }),
    e.length === 0 ? /* @__PURE__ */ a.jsx("p", { children: "No incidents recorded." }) : null,
    e.map((t) => /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-incident", "data-severity": t.severity.toLowerCase(), children: [
      /* @__PURE__ */ a.jsx("strong", { children: t.failureType }),
      /* @__PURE__ */ a.jsxs("span", { children: [
        t.status,
        " · ",
        t.severity
      ] }),
      /* @__PURE__ */ a.jsx("p", { children: t.message })
    ] }, t.incidentId))
  ] });
}
function Di({ status: e, subStatus: t }) {
  return /* @__PURE__ */ a.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
function N0({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const i = vl(n, t), s = n === 0 ? 0 : (e - 1) * t + 1, l = Math.min(e * t, n);
  return /* @__PURE__ */ a.jsxs("div", { className: "wf-pagination", "aria-label": "Workflow definition pagination", children: [
    /* @__PURE__ */ a.jsxs("span", { className: "wf-pagination-summary", "aria-live": "polite", children: [
      "Showing ",
      s,
      "-",
      l,
      " of ",
      n
    ] }),
    /* @__PURE__ */ a.jsxs("label", { className: "wf-page-size", children: [
      "Rows",
      /* @__PURE__ */ a.jsx("select", { value: t, onChange: (u) => r(Number(u.target.value)), children: l0.map((u) => /* @__PURE__ */ a.jsx("option", { value: u, children: u }, u)) })
    ] }),
    /* @__PURE__ */ a.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ a.jsx(ni, { size: 14 }),
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
        /* @__PURE__ */ a.jsx($t, { size: 14 })
      ] })
    ] })
  ] });
}
function E0(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function vl(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Zt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function Kt(e, t, n) {
  const o = t.createPrompt(n);
  o && e.dispatchPrompt(o);
}
function k0(e) {
  const t = Ro(e, "flowchart"), n = Ro(e, "sequence"), o = [
    { value: t?.activityVersionId ?? "kind:flowchart", label: "Flowchart" },
    { value: n?.activityVersionId ?? "kind:sequence", label: "Sequence" }
  ], r = /* @__PURE__ */ new Map();
  for (const s of e.filter(kl)) {
    if (I0(s)) continue;
    const l = s.category || "Uncategorized";
    r.set(l, [...r.get(l) ?? [], s]);
  }
  const i = Array.from(r.entries()).sort(([s], [l]) => s.localeCompare(l)).map(([s, l]) => ({
    name: s,
    activities: l.sort((u, c) => Ce(u).localeCompare(Ce(c)))
  }));
  return { compositeRoots: o, otherCategories: i };
}
function C0(e, t) {
  return e.rootActivityVersionId ?? Ro(t, e.rootKind)?.activityVersionId ?? `kind:${e.rootKind}`;
}
function _0(e, t) {
  return e.rootActivityVersionId ?? Ro(t, e.rootKind)?.activityVersionId ?? null;
}
function Ro(e, t) {
  return e.find((n) => bl(n) === t);
}
function bl(e) {
  return e ? Nl(e) ? "flowchart" : El(e) ? "sequence" : null : null;
}
function Sl(e) {
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
function I0(e) {
  return Nl(e) || El(e);
}
function Nl(e) {
  return Ce(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function El(e) {
  return Ce(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function kl(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function j0(e) {
  return e.sourceKind || e.sourceId || e.sourceVersion ? [e.sourceKind, e.sourceId, e.sourceVersion].filter(Boolean).join(" / ") : e.definitionId;
}
function A0(e) {
  return M0(e.rootActivityType) || e.rootActivityType;
}
function M0(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function D0(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    go(t, n.typeName, n), go(t, n.name, n), go(t, n.displayName, n);
    const o = n.typeName.split(".").filter(Boolean).at(-1);
    go(t, o, n);
  }
  return t;
}
function P0(e, t, n) {
  const o = t.get(e.activityVersionId);
  return n.get(En(o?.activityTypeKey)) ?? n.get(En(Cl(o?.activityTypeKey))) ?? n.get(En(o?.displayName)) ?? n.get(En(e.activityVersionId)) ?? null;
}
function go(e, t, n) {
  const o = En(t);
  o && !e.has(o) && e.set(o, n);
}
function En(e) {
  return e?.trim().toLowerCase() ?? "";
}
function Cl(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function aa(e, t, n, o) {
  const r = Go();
  if (!r) return t;
  const i = r.getItem(e);
  if (i == null) return t;
  const s = Number(i);
  return Number.isFinite(s) ? No(s, n, o) : t;
}
function ca(e, t) {
  const n = Go();
  if (!n) return t;
  const o = n.getItem(e);
  return o === "true" ? !0 : o === "false" ? !1 : t;
}
function $0() {
  const e = Go();
  if (!e) return null;
  const t = e.getItem(xl);
  return t === "palette" || t === "inspector" ? t : null;
}
function Go() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function bn(e, t) {
  const n = Go();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function No(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function T0({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: o,
  workflowDesignerPanels: r,
  onBack: i
}) {
  const [s, l] = Y(null), [u, c] = Y(null), [d, f] = Y([]), [h, g] = Y([]), [m, x] = Y(bo), [v, y] = Y("loading"), [b, p] = Y([]), [w, E] = Y([]), [k, _] = Y([]), [D, $] = Y(null), [F, j] = Y(null), [z, H] = Y(null), [S, I] = Y(null), [C, A] = Y(""), [T, P] = Y(""), [W, O] = Y("idle"), [B, G] = Y(null), [Z, te] = Y(!1), [ae, J] = Y(null), [R, q] = Y(() => /* @__PURE__ */ new Set()), [re, oe] = Y(() => aa(ea, d0, yn, xn)), [U, Q] = Y(() => aa(ta, f0, wn, vn)), [se, L] = Y(() => ca(na, !1)), [K, fe] = Y(() => ca(oa, !1)), [ue, Ie] = Y($0), [Oe, Be] = Y("activities"), [pt, et] = Y("inspector"), Ee = ce(null), je = ce(null), Pe = ce(""), $e = ce(0), Ke = ce(Promise.resolve()), jt = ce(null), tt = ce(!1), gt = u?.state.rootActivity ?? null, mt = me(() => new Map(d.map((N) => [N.activityVersionId, N])), [d]), Yn = me(() => D0(h), [h]), Te = me(() => ll(gt, b), [gt, b]), qn = Ny(Te, Te ? mt.get(Te.activityVersionId) : void 0), we = !!Te && qn === "unsupported", Fe = me(() => we ? null : Vr(gt, b), [gt, b, we]), Gt = me(() => Sl(d), [d]), ke = me(() => we && Te?.nodeId === F ? Te : Fe?.slot.activities.find((N) => N.nodeId === F) ?? null, [we, Fe, Te, F]), Jt = me(
    () => ke ? P0(ke, mt, Yn) : null,
    [mt, Yn, ke]
  ), Qt = ke ? Je(ke) : [], _e = qn === "flowchart" && Fe?.slot.mode === "flowchart", yt = !gt || !we, at = W !== "idle", Jo = !!u?.state.rootActivity && !at, Zn = Zt(n, "weaver.workflows.find-draft-risks"), Kn = Zt(n, "weaver.workflows.propose-update");
  ie(() => {
    bn(ea, String(re));
  }, [re]), ie(() => {
    bn(ta, String(U));
  }, [U]), ie(() => {
    bn(na, String(se));
  }, [se]), ie(() => {
    bn(oa, String(K));
  }, [K]), ie(() => {
    bn(xl, ue);
  }, [ue]), ie(() => {
    if (!ue) return;
    const N = (M) => {
      M.key === "Escape" && Ie(null);
    };
    return window.addEventListener("keydown", N), () => window.removeEventListener("keydown", N);
  }, [ue]);
  const en = ge(async () => {
    A(""), y("loading");
    const [N, M, V, X] = await Promise.all([
      iy(e, t),
      al(e),
      xy(e).then(
        (he) => ({ ok: !0, descriptors: he }),
        () => ({ ok: !1, descriptors: [] })
      ),
      wy(e).then(
        (he) => ({ ok: !0, descriptors: he }),
        () => ({ ok: !1, descriptors: bo })
      )
    ]), ne = N.draft ?? null;
    l(N), Pe.current = ne ? wt(ne) : "", c(ne), f(M.activities ?? []), g(V.descriptors), x(X.descriptors.length > 0 ? X.descriptors : bo), y(V.ok ? "ready" : "failed"), p([]), j(null);
  }, [e, t]);
  ie(() => {
    en().catch((N) => A(N instanceof Error ? N.message : String(N)));
  }, [en]), ie(() => {
    q((N) => {
      let M = !1;
      const V = new Set(N);
      for (const X of Gt)
        V.has(X.category) || (V.add(X.category), M = !0);
      return M ? V : N;
    });
  }, [Gt]), ie(() => {
    if (!Te) {
      E([]), _([]);
      return;
    }
    const N = we ? Sy(Te, d, u?.layout ?? []) : Fe ? by(Fe, d, u?.layout ?? []) : { nodes: [], edges: [] };
    E(N.nodes), _(N.edges);
  }, [d, u?.layout, we, Fe, Te]);
  const Qo = (N) => {
    c((M) => M && { ...M, state: { ...M.state, rootActivity: N } });
  }, tn = ge((N, M) => {
    if (u?.state.rootActivity && we)
      return;
    const V = Ks(N, da(N));
    if (!u?.state.rootActivity) {
      Qo(V), j(V.nodeId);
      return;
    }
    if (!Fe) {
      if (!Je(V)[0]) {
        P(""), A("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      c((ne) => {
        if (!ne?.state.rootActivity) return ne;
        const he = ne.state.rootActivity, le = ri(V, [], [he]), pe = M ? [
          ...ne.layout.filter((Se) => Se.nodeId !== he.nodeId),
          {
            nodeId: he.nodeId,
            x: Math.round(M.x),
            y: Math.round(M.y)
          }
        ] : ne.layout;
        return {
          ...ne,
          layout: pe,
          state: {
            ...ne.state,
            rootActivity: le
          }
        };
      }), j(u.state.rootActivity.nodeId), A(""), P(`Wrapped root in ${Ce(N)}`);
      return;
    }
    c((X) => {
      if (!X?.state.rootActivity) return X;
      const ne = Vr(X.state.rootActivity, b);
      if (!ne) return X;
      const he = ri(X.state.rootActivity, b, [...ne.slot.activities, V]), le = M ? [
        ...X.layout.filter((pe) => pe.nodeId !== V.nodeId),
        {
          nodeId: V.nodeId,
          x: Math.round(M.x),
          y: Math.round(M.y)
        }
      ] : X.layout;
      return {
        ...X,
        layout: le,
        state: {
          ...X.state,
          rootActivity: he
        }
      };
    }), j(V.nodeId);
  }, [u?.state.rootActivity, b, we, Fe]), At = ge((N, M) => {
    const V = Ks(N, da(N)), X = {
      id: V.nodeId,
      type: "workflowActivity",
      position: M,
      selected: !0,
      data: {
        label: Ce(N),
        activityVersionId: N.activityVersionId,
        activityTypeKey: N.activityTypeKey,
        category: N.category,
        executionType: N.executionType,
        icon: ii(N),
        childSlots: Je(V),
        acceptsInbound: String(N.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: hl(V, N)
      }
    };
    return { activityNode: V, node: X };
  }, []), ze = ge((N, M, V = []) => {
    we || c((X) => {
      if (!X) return X;
      const ne = Cy(X.layout, N), he = X.state.rootActivity;
      if (!he) return { ...X, layout: ne };
      const le = Vr(he, b);
      if (!le) return { ...X, layout: ne };
      const pe = Ey(le, N, M, V), Se = le.slot.mode === "flowchart" ? ky(pe, M) : pe;
      return {
        ...X,
        layout: ne,
        state: {
          ...X.state,
          rootActivity: ul(he, b, Se)
        }
      };
    });
  }, [b, we]), nn = ge((N, M) => {
    if (!Ee.current) return null;
    const V = Ee.current.getBoundingClientRect();
    return D ? D.screenToFlowPosition({ x: N, y: M }) : {
      x: N - V.left,
      y: M - V.top
    };
  }, [D]), on = ge((N, M) => document.elementFromPoint(N, M)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), rn = ge((N, M, V) => {
    const X = w.find((Ae) => Ae.id === M.source), ne = w.find((Ae) => Ae.id === M.target), he = X && ne ? B0(X, ne) : X ? fa(X) : V, le = At(N, he), Se = [...w.map((Ae) => Ae.selected ? { ...Ae, selected: !1 } : Ae), le.node], xt = zy(k, M, le.node.id);
    E(Se), _(xt), j(le.node.id), ze(Se, xt, [le.activityNode]);
  }, [ze, At, k, w]), Mt = ge((N, M, V) => {
    if (!yt || !Ee.current) return !1;
    const X = Ee.current.getBoundingClientRect();
    if (!(M >= X.left && M <= X.right && V >= X.top && V <= X.bottom)) return !1;
    const he = nn(M, V);
    if (!he) return !1;
    if (_e) {
      const le = on(M, V), pe = le ? k.find((Se) => Se.id === le) : void 0;
      if (pe)
        return rn(N, pe, he), !0;
    }
    return tn(N, he), !0;
  }, [tn, yt, k, on, _e, rn, nn]);
  ie(() => {
    const N = (V) => {
      const X = jt.current;
      if (!X) return;
      Math.hypot(V.clientX - X.startX, V.clientY - X.startY) >= a0 && (X.dragging = !0);
    }, M = (V) => {
      const X = jt.current;
      if (jt.current = null, !X?.dragging || !Ee.current) return;
      const ne = Ee.current.getBoundingClientRect();
      V.clientX >= ne.left && V.clientX <= ne.right && V.clientY >= ne.top && V.clientY <= ne.bottom && (tt.current = !0, window.setTimeout(() => {
        tt.current = !1;
      }, 0), Mt(X.activity, V.clientX, V.clientY));
    };
    return window.addEventListener("pointermove", N), window.addEventListener("pointerup", M), window.addEventListener("pointercancel", M), () => {
      window.removeEventListener("pointermove", N), window.removeEventListener("pointerup", M), window.removeEventListener("pointercancel", M);
    };
  }, [D, Mt]);
  const er = (N, M) => {
    N.dataTransfer.setData(Qs, M.activityVersionId), N.dataTransfer.setData("text/plain", M.activityVersionId), N.dataTransfer.effectAllowed = "copy";
  }, tr = (N, M) => {
    N.clientX === 0 && N.clientY === 0 || Mt(M, N.clientX, N.clientY) && (tt.current = !0, window.setTimeout(() => {
      tt.current = !1;
    }, 0));
  }, nr = (N, M) => {
    N.button === 0 && (jt.current = {
      activity: M,
      startX: N.clientX,
      startY: N.clientY,
      dragging: !1
    });
  }, or = (N) => {
    tt.current || yt && tn(N);
  }, sn = (N) => {
    if (!yt) {
      N.dataTransfer.dropEffect = "none";
      return;
    }
    if (N.preventDefault(), N.dataTransfer.dropEffect = "copy", !_e) return;
    const M = on(N.clientX, N.clientY);
    I(M);
  }, an = (N) => {
    if (!Ee.current) return;
    const M = N.relatedTarget;
    M && Ee.current.contains(M) || I(null);
  }, cn = (N) => {
    if (N.preventDefault(), I(null), !yt) return;
    const M = N.dataTransfer.getData(Qs) || N.dataTransfer.getData("text/plain"), V = mt.get(M);
    V && Mt(V, N.clientX, N.clientY);
  }, rr = () => {
    if (!_e) return;
    const N = Ee.current?.getBoundingClientRect();
    N && H({
      kind: "fromEmpty",
      clientX: N.left + N.width / 2,
      clientY: N.top + N.height / 2
    });
  }, ln = ge(async (N, M) => {
    const V = async () => {
      const ne = ++$e.current, he = wt(N);
      A("");
      try {
        const le = await uy(e, N), pe = wt(le);
        return Pe.current = pe, c((Se) => !Se || Se.id !== le.id ? Se : wt(Se) === he ? le : { ...Se, validationErrors: le.validationErrors }), ne === $e.current && P(M), le;
      } catch (le) {
        throw ne === $e.current && (P(""), A(le instanceof Error ? le.message : String(le))), le;
      }
    }, X = Ke.current.then(V, V);
    return Ke.current = X.catch(() => {
    }), X;
  }, [e]);
  ie(() => {
    if (!Z || !u || wt(u) === Pe.current) return;
    P("Autosaving...");
    const M = window.setTimeout(() => {
      ln(u, "Autosaved").catch(() => {
      });
    }, c0);
    return () => window.clearTimeout(M);
  }, [Z, u, ln]);
  const ir = async () => {
    if (!(!u || at)) {
      O("saving"), P("Saving...");
      try {
        await ln(u, "Saved");
      } catch {
      } finally {
        O("idle");
      }
    }
  }, sr = async () => {
    if (!(!u || at)) {
      O("promoting"), P("Promoting...");
      try {
        const N = await dy(e, u.id), M = await fy(e, N.versionId);
        J(M.artifactId), P(`Published ${M.artifactVersion}`), await en();
      } catch (N) {
        P(""), A(N instanceof Error ? N.message : String(N));
      } finally {
        O("idle");
      }
    }
  }, ar = async () => {
    if (!u?.state.rootActivity || at) return;
    const N = u, M = wt(N);
    G(null), P("Preparing test run...");
    try {
      O("testRunPreparing"), P("Preparing test run...");
      const V = W0(N);
      O("testRunStarting"), P("Starting test run...");
      const X = await hy(e, {
        definitionId: N.definitionId,
        snapshotId: V,
        state: N.state
      });
      G({ draftSignature: M, view: X }), P(Il(X) ? "Test run rejected" : "Test run dispatched");
    } catch (V) {
      P(""), A(V instanceof Error ? V.message : String(V));
    } finally {
      O("idle");
    }
  }, cr = (N) => {
    const M = we ? N.filter((V) => V.type === "select") : N;
    M.length !== 0 && E((V) => Cc(M, V));
  }, Un = (N) => {
    we || _((M) => _c(N, M));
  }, un = (N) => !N.source || !N.target || N.source === N.target || !_e ? !1 : !N.targetHandle, lr = (N) => {
    if (!u?.state.rootActivity || !Fe || !_e || !un(N)) return;
    const M = zo(N.source, N.target, N.sourceHandle ?? "Done", N.targetHandle ?? void 0), V = jc(M, k);
    _(V), ze(w, V);
  }, ur = () => {
    ze(w, k);
  }, dr = (N, M) => {
    if (!M.nodeId || M.handleType === "target") {
      je.current = null;
      return;
    }
    je.current = {
      nodeId: M.nodeId,
      handleId: M.handleId ?? null
    };
  }, fr = (N) => {
    const M = je.current;
    if (je.current = null, !M || !_e || N.target?.closest(".react-flow__handle, .react-flow__node")) return;
    const X = F0(N);
    H({
      kind: "fromPort",
      sourceNodeId: M.nodeId,
      sourceHandleId: M.handleId,
      clientX: X.x,
      clientY: X.y
    });
  }, hr = (N, M) => {
    if (!_e || !un(M)) return;
    const V = qp(N, {
      ...M,
      sourceHandle: M.sourceHandle ?? "Done",
      targetHandle: M.targetHandle ?? void 0
    }, k, { shouldReplaceId: !1 });
    _(V), ze(w, V);
  }, pr = (N) => {
    if (we || N.length === 0) return;
    const M = new Set(N.map((ne) => ne.id)), V = w.filter((ne) => !M.has(ne.id)), X = k.filter((ne) => !M.has(ne.source) && !M.has(ne.target));
    E(V), _(X), F && M.has(F) && j(null), ze(V, X);
  }, gr = (N) => {
    if (we || N.length === 0) return;
    const M = new Set(N.map((X) => X.id)), V = k.filter((X) => !M.has(X.id));
    _(V), ze(w, V);
  }, dn = ge((N) => {
    if (we) return;
    const M = k.filter((V) => V.id !== N);
    _(M), ze(w, M);
  }, [ze, k, we, w]), Gn = ge((N, M, V) => {
    _e && H({ kind: "spliceEdge", edgeId: N, clientX: M, clientY: V });
  }, [_e]), mr = (N) => {
    const M = z;
    if (!M) return;
    H(null);
    const V = nn(M.clientX, M.clientY) ?? { x: 0, y: 0 };
    if (M.kind === "fromEmpty") {
      const ne = At(N, V), le = [...w.map((pe) => pe.selected ? { ...pe, selected: !1 } : pe), ne.node];
      E(le), j(ne.node.id), ze(le, k, [ne.activityNode]);
      return;
    }
    if (M.kind === "fromPort") {
      const ne = w.find((Ae) => Ae.id === M.sourceNodeId), he = ne ? fa(ne) : V, le = At(N, he), Se = [...w.map((Ae) => Ae.selected ? { ...Ae, selected: !1 } : Ae), le.node], xt = [...k, zo(M.sourceNodeId, le.node.id, M.sourceHandleId ?? "Done")];
      E(Se), _(xt), j(le.node.id), ze(Se, xt, [le.activityNode]);
      return;
    }
    const X = k.find((ne) => ne.id === M.edgeId);
    X && rn(N, X, V);
  }, yr = me(() => ({
    highlightedEdgeId: S,
    deleteEdge: dn,
    requestInsertActivity: Gn
  }), [dn, S, Gn]), xr = (N, M, V) => {
    p((X) => [...X, { ownerNodeId: N.nodeId, slotId: M, label: V }]), j(null);
  }, wr = ge((N) => {
    c((M) => {
      const V = M?.state.rootActivity;
      return !M || !V ? M : {
        ...M,
        state: {
          ...M.state,
          rootActivity: dl(V, N.nodeId, () => N)
        }
      };
    });
  }, []), vr = (N) => {
    q((M) => {
      const V = new Set(M);
      return V.has(N) ? V.delete(N) : V.add(N), V;
    });
  }, Jn = (N) => {
    Ie((M) => M === N ? null : M), N === "palette" ? L((M) => !M) : fe((M) => !M);
  }, Qn = (N) => {
    N === "palette" ? L(!1) : fe(!1), Ie((M) => M === N ? null : N);
  }, fn = (N, M) => {
    Ie(null), N === "palette" ? (L(!1), oe((V) => No(V + M, yn, xn))) : (fe(!1), Q((V) => No(V + M, wn, vn)));
  }, eo = (N, M) => {
    M.preventDefault(), Ie(null), N === "palette" ? L(!1) : fe(!1);
    const V = M.clientX, X = N === "palette" ? re : U, ne = N === "palette" ? yn : wn, he = N === "palette" ? xn : vn;
    document.body.classList.add("wf-side-panel-resizing");
    const le = (Se) => {
      const xt = N === "palette" ? Se.clientX - V : V - Se.clientX, Ae = No(X + xt, ne, he);
      N === "palette" ? oe(Ae) : Q(Ae);
    }, pe = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", le), window.removeEventListener("pointerup", pe), window.removeEventListener("pointercancel", pe);
    };
    window.addEventListener("pointermove", le), window.addEventListener("pointerup", pe), window.addEventListener("pointercancel", pe);
  }, hn = (N, M) => {
    M.key === "ArrowLeft" ? (M.preventDefault(), fn(N, N === "palette" ? -po : po)) : M.key === "ArrowRight" ? (M.preventDefault(), fn(N, N === "palette" ? po : -po)) : M.key === "Home" ? (M.preventDefault(), N === "palette" ? oe(yn) : Q(wn)) : M.key === "End" && (M.preventDefault(), N === "palette" ? oe(xn) : Q(vn));
  };
  if (!s || !u)
    return /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: C || "Loading workflow editor..." });
  const br = [
    "wf-editor-body",
    se ? "palette-collapsed" : "",
    K ? "inspector-collapsed" : "",
    ue === "palette" ? "palette-maximized" : "",
    ue === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), Sr = {
    "--wf-palette-width": `${se ? ra : re}px`,
    "--wf-inspector-width": `${K ? ra : U}px`
  }, Dt = !se && ue !== "inspector", to = !K && ue !== "palette", no = B?.draftSignature === wt(u) ? B.view : null, oo = {
    definition: s.definition,
    draft: u,
    selectedActivity: ke,
    selectedActivityDescriptor: Jt,
    selectedActivitySlots: Qt,
    catalog: d,
    currentScopeOwner: Te,
    frames: b
  }, Pi = r.map((N) => {
    const M = N.component;
    return {
      id: N.id,
      title: N.title,
      side: N.side,
      order: N.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ a.jsx(M, { context: oo })
    };
  }), Nr = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ a.jsx(rl, { size: 15 }),
      render: jl
    },
    ...Pi.filter((N) => N.side === "left")
  ].sort(ua), Er = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ a.jsx(sl, { size: 15 }),
      render: Al
    },
    ...Pi.filter((N) => N.side === "right")
  ].sort(ua), $i = Nr.find((N) => N.id === Oe) ?? Nr[0], Ti = Er.find((N) => N.id === pt) ?? Er[0];
  function jl() {
    return /* @__PURE__ */ a.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: Gt.map((N) => {
      const M = R.has(N.category);
      return /* @__PURE__ */ a.jsxs("div", { className: "wf-palette-category", children: [
        /* @__PURE__ */ a.jsxs(
          "button",
          {
            type: "button",
            className: "wf-palette-category-toggle",
            role: "treeitem",
            "aria-expanded": M,
            onClick: () => vr(N.category),
            children: [
              M ? /* @__PURE__ */ a.jsx(Km, { size: 14 }) : /* @__PURE__ */ a.jsx($t, { size: 14 }),
              /* @__PURE__ */ a.jsx("span", { children: N.category }),
              /* @__PURE__ */ a.jsx("small", { children: N.activities.length })
            ]
          }
        ),
        M ? /* @__PURE__ */ a.jsx("div", { className: "wf-palette-activities", role: "group", children: N.activities.map((V) => {
          const X = V.description?.trim(), ne = X ? `wf-palette-description-${V.activityVersionId}` : void 0, he = Ce(V), le = ii(V);
          return /* @__PURE__ */ a.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-activity",
              role: "treeitem",
              draggable: !0,
              title: X || Ce(V),
              "aria-describedby": ne,
              onClick: () => or(V),
              onDragStart: (pe) => er(pe, V),
              onDragEnd: (pe) => tr(pe, V),
              onPointerDown: (pe) => nr(pe, V),
              children: [
                /* @__PURE__ */ a.jsx("span", { className: "wf-activity-icon", "data-icon": le, "aria-hidden": "true", children: _l(le) }),
                /* @__PURE__ */ a.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ a.jsx("strong", { children: he }),
                  X ? /* @__PURE__ */ a.jsx("small", { id: ne, children: X }) : null
                ] }),
                /* @__PURE__ */ a.jsx(Um, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            V.activityVersionId
          );
        }) }) : null
      ] }, N.category);
    }) });
  }
  function Al() {
    return ke ? /* @__PURE__ */ a.jsxs("div", { className: "wf-inspector-content", children: [
      /* @__PURE__ */ a.jsx("h3", { children: w.find((N) => N.id === ke.nodeId)?.data.label ?? ke.nodeId }),
      /* @__PURE__ */ a.jsxs("dl", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Node ID" }),
        /* @__PURE__ */ a.jsx("dd", { children: ke.nodeId }),
        /* @__PURE__ */ a.jsx("dt", { children: "Activity type" }),
        /* @__PURE__ */ a.jsx("dd", { children: Jt?.typeName ?? mt.get(ke.activityVersionId)?.activityTypeKey ?? "Unknown" }),
        /* @__PURE__ */ a.jsx("dt", { children: "Activity version" }),
        /* @__PURE__ */ a.jsx("dd", { children: ke.activityVersionId })
      ] }),
      /* @__PURE__ */ a.jsx(
        Jy,
        {
          activity: ke,
          descriptor: Jt,
          editors: o,
          expressionDescriptors: m,
          descriptorStatus: v,
          onChange: wr
        }
      ),
      Qt.length > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-slot-list", children: [
        /* @__PURE__ */ a.jsx("span", { children: "Embedded slots" }),
        Qt.map((N) => /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => xr(ke, N.id, `${w.find((M) => M.id === ke.nodeId)?.data.label ?? ke.nodeId} / ${N.label}`), children: [
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
      /* @__PURE__ */ a.jsx($t, { size: 14 }),
      /* @__PURE__ */ a.jsx("strong", { children: s.definition.name }),
      /* @__PURE__ */ a.jsx("span", { className: "wf-chip", children: "Draft" }),
      T ? /* @__PURE__ */ a.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ a.jsx(Wn, { size: 13 }),
        " ",
        T
      ] }) : null,
      /* @__PURE__ */ a.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ a.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ a.jsx("input", { type: "checkbox", checked: Z, onChange: (N) => te(N.target.checked) }),
          /* @__PURE__ */ a.jsx("span", { children: "Autosave" })
        ] }),
        Zn ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => Kt(n, Zn, { definition: s.definition, draft: u }), children: [
          /* @__PURE__ */ a.jsx(qt, { size: 15 }),
          " Risks"
        ] }) : null,
        Kn ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => Kt(n, Kn, { definition: s.definition, draft: u }), children: [
          /* @__PURE__ */ a.jsx(qt, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ a.jsxs("button", { type: "button", disabled: at, onClick: () => {
          ir();
        }, children: [
          /* @__PURE__ */ a.jsx(Jm, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ a.jsxs("button", { type: "button", disabled: at, onClick: () => {
          sr();
        }, children: [
          /* @__PURE__ */ a.jsx(il, { size: 15 }),
          " Promote"
        ] }),
        /* @__PURE__ */ a.jsxs(
          "button",
          {
            type: "button",
            disabled: !Jo,
            title: u.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              ar();
            },
            children: [
              /* @__PURE__ */ a.jsx(_i, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    C ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(ft, { size: 16 }),
      " ",
      C
    ] }) : null,
    no ? /* @__PURE__ */ a.jsx(O0, { testRun: no }) : null,
    /* @__PURE__ */ a.jsxs("div", { className: br, style: Sr, children: [
      /* @__PURE__ */ a.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ a.jsx(
            la,
            {
              label: "Activities panel tabs",
              tabs: Nr,
              activeTabId: $i.id,
              onSelect: Be
            }
          ),
          /* @__PURE__ */ a.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ a.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": se ? "Expand activities panel" : "Collapse activities panel",
                title: se ? "Expand" : "Collapse",
                onClick: () => Jn("palette"),
                children: se ? /* @__PURE__ */ a.jsx($t, { size: 14 }) : /* @__PURE__ */ a.jsx(ni, { size: 14 })
              }
            ),
            se ? null : /* @__PURE__ */ a.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": ue === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: ue === "palette" ? "Restore" : "Maximize",
                onClick: () => Qn("palette"),
                children: ue === "palette" ? /* @__PURE__ */ a.jsx(Ys, { size: 14 }) : /* @__PURE__ */ a.jsx(To, { size: 14 })
              }
            )
          ] })
        ] }),
        Dt ? $i.render() : null
      ] }),
      Dt && !ue ? /* @__PURE__ */ a.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": yn,
          "aria-valuemax": xn,
          "aria-valuenow": re,
          tabIndex: 0,
          onPointerDown: (N) => eo("palette", N),
          onKeyDown: (N) => hn("palette", N)
        }
      ) : /* @__PURE__ */ a.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ a.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-breadcrumb", children: [
          /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
            p([]), j(null);
          }, children: "Root" }),
          b.map((N, M) => /* @__PURE__ */ a.jsxs(ht.Fragment, { children: [
            /* @__PURE__ */ a.jsx($t, { size: 13 }),
            /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
              p(b.slice(0, M + 1)), j(null);
            }, children: N.label })
          ] }, `${N.ownerNodeId}-${N.slotId}-${M}`))
        ] }),
        /* @__PURE__ */ a.jsxs("div", { className: "wf-canvas", ref: Ee, onDragOver: sn, onDragLeave: an, onDrop: cn, children: [
          /* @__PURE__ */ a.jsx(wl.Provider, { value: yr, children: /* @__PURE__ */ a.jsxs(
            gm,
            {
              nodes: w,
              edges: k,
              nodeTypes: i0,
              edgeTypes: s0,
              onInit: $,
              onNodesChange: cr,
              onEdgesChange: Un,
              onNodesDelete: pr,
              onEdgesDelete: gr,
              onConnect: lr,
              onConnectStart: _e ? dr : void 0,
              onConnectEnd: _e ? fr : void 0,
              onReconnect: _e ? hr : void 0,
              isValidConnection: un,
              onDragOver: sn,
              onDragLeave: an,
              onDrop: cn,
              onPaneClick: () => j(null),
              onNodeClick: (N, M) => j(M.id),
              onNodeDragStop: we ? void 0 : ur,
              fitView: !0,
              minZoom: 0.2,
              maxZoom: 1.8,
              nodesConnectable: _e,
              nodesDraggable: !we,
              selectionOnDrag: !0,
              multiSelectionKeyCode: ["Shift", "Meta", "Control"],
              deleteKeyCode: we ? null : ["Backspace", "Delete"],
              panActivationKeyCode: null,
              defaultEdgeOptions: { type: "workflow" },
              children: [
                /* @__PURE__ */ a.jsx(Sm, { gap: 18, size: 1 }),
                /* @__PURE__ */ a.jsx(jm, {}),
                /* @__PURE__ */ a.jsx(Bm, { pannable: !0, zoomable: !0 })
              ]
            }
          ) }),
          _e && w.length === 0 ? /* @__PURE__ */ a.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => rr(), children: [
            /* @__PURE__ */ a.jsx(Ii, { size: 15 }),
            " Add activity"
          ] }) : null,
          z ? /* @__PURE__ */ a.jsx(
            H0,
            {
              clientX: z.clientX,
              clientY: z.clientY,
              activities: d,
              onPick: mr,
              onClose: () => H(null)
            }
          ) : null
        ] }),
        /* @__PURE__ */ a.jsx(V0, { draft: u })
      ] }),
      to && !ue ? /* @__PURE__ */ a.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": wn,
          "aria-valuemax": vn,
          "aria-valuenow": U,
          tabIndex: 0,
          onPointerDown: (N) => eo("inspector", N),
          onKeyDown: (N) => hn("inspector", N)
        }
      ) : /* @__PURE__ */ a.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ a.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ a.jsx(
            la,
            {
              label: "Inspector panel tabs",
              tabs: Er,
              activeTabId: Ti.id,
              onSelect: et
            }
          ),
          /* @__PURE__ */ a.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ a.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": K ? "Expand inspector panel" : "Collapse inspector panel",
                title: K ? "Expand" : "Collapse",
                onClick: () => Jn("inspector"),
                children: K ? /* @__PURE__ */ a.jsx(ni, { size: 14 }) : /* @__PURE__ */ a.jsx($t, { size: 14 })
              }
            ),
            K ? null : /* @__PURE__ */ a.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": ue === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: ue === "inspector" ? "Restore" : "Maximize",
                onClick: () => Qn("inspector"),
                children: ue === "inspector" ? /* @__PURE__ */ a.jsx(Ys, { size: 14 }) : /* @__PURE__ */ a.jsx(To, { size: 14 })
              }
            )
          ] })
        ] }),
        to ? Ti.render() : null
      ] })
    ] })
  ] });
}
function la({
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
function ua(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
function z0({ data: e, selected: t }) {
  const n = e, o = !n.suppressFlowPorts, r = o ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], i = R0(n);
  return /* @__PURE__ */ a.jsxs("div", { className: t ? "wf-node selected" : "wf-node", "data-icon": n.icon ?? "activity", children: [
    o && n.acceptsInbound ? /* @__PURE__ */ a.jsx(Yt, { type: "target", position: ee.Left }) : null,
    /* @__PURE__ */ a.jsxs("div", { className: "wf-node-content", children: [
      /* @__PURE__ */ a.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: _l(n.icon) }),
      /* @__PURE__ */ a.jsxs("span", { className: "wf-node-copy", children: [
        /* @__PURE__ */ a.jsx("strong", { children: n.label }),
        i ? /* @__PURE__ */ a.jsx("small", { children: i }) : null
      ] })
    ] }),
    n.childSlots.length > 0 ? /* @__PURE__ */ a.jsxs("span", { className: "wf-node-slot-badge", children: [
      n.childSlots.length,
      " slot",
      n.childSlots.length === 1 ? "" : "s"
    ] }) : null,
    r.map((s, l) => {
      const u = `${(l + 1) / (r.length + 1) * 100}%`;
      return /* @__PURE__ */ a.jsxs(ht.Fragment, { children: [
        /* @__PURE__ */ a.jsx("span", { className: "wf-node-port-label", style: { top: u }, children: s.displayName }),
        /* @__PURE__ */ a.jsx(Yt, { type: "source", position: ee.Right, id: s.name, style: { top: u } })
      ] }, s.name);
    })
  ] });
}
function R0(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((r) => !!r).join(" · ");
}
function _l(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ a.jsx(il, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ a.jsx(sl, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ a.jsx(ey, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ a.jsx(_i, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ a.jsx(ny, { size: 15 });
    default:
      return /* @__PURE__ */ a.jsx(rl, { size: 15 });
  }
}
function L0(e) {
  const {
    id: t,
    sourceX: n,
    sourceY: o,
    targetX: r,
    targetY: i,
    sourcePosition: s,
    targetPosition: l,
    markerEnd: u,
    style: c,
    label: d,
    labelStyle: f
  } = e, h = ht.useContext(wl), [g, m] = Y(!1), [x, v, y] = Po({ sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: l }), b = h?.highlightedEdgeId === t;
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsx(
      Fn,
      {
        id: t,
        path: x,
        markerEnd: u,
        style: {
          ...c,
          strokeWidth: b ? 2.5 : c?.strokeWidth
        },
        label: d,
        labelX: v,
        labelY: y,
        labelStyle: f,
        onMouseEnter: () => m(!0),
        onMouseLeave: () => m(!1)
      }
    ),
    h ? /* @__PURE__ */ a.jsx(ym, { children: /* @__PURE__ */ a.jsxs(
      "div",
      {
        className: ["wf-edge-actions", g ? "visible" : "", b ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${v}px, ${y}px)` },
        onMouseEnter: () => m(!0),
        onMouseLeave: () => m(!1),
        children: [
          /* @__PURE__ */ a.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (p) => h.requestInsertActivity(t, p.clientX, p.clientY), children: /* @__PURE__ */ a.jsx(Ii, { size: 12 }) }),
          /* @__PURE__ */ a.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => h.deleteEdge(t), children: /* @__PURE__ */ a.jsx(oi, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function H0({ clientX: e, clientY: t, activities: n, onPick: o, onClose: r }) {
  const [i, s] = Y(""), [l, u] = Y(0), c = ce(null), d = ce(null), f = me(() => {
    const b = i.trim().toLowerCase(), p = n.filter(kl);
    return b ? p.filter((w) => Ce(w).toLowerCase().includes(b) || w.activityTypeKey.toLowerCase().includes(b) || (w.category ?? "").toLowerCase().includes(b) || (w.description ?? "").toLowerCase().includes(b)) : p;
  }, [n, i]), h = me(() => Sl(f), [f]), g = me(() => h.flatMap((b) => b.activities), [h]);
  ie(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), ie(() => {
    const b = (w) => {
      c.current?.contains(w.target) || r();
    }, p = (w) => {
      w.key === "Escape" && r();
    };
    return document.addEventListener("mousedown", b, !0), document.addEventListener("keydown", p), () => {
      document.removeEventListener("mousedown", b, !0), document.removeEventListener("keydown", p);
    };
  }, [r]);
  const m = (b) => {
    if (b.key === "ArrowDown")
      b.preventDefault(), u((p) => Math.min(p + 1, g.length - 1));
    else if (b.key === "ArrowUp")
      b.preventDefault(), u((p) => Math.max(p - 1, 0));
    else if (b.key === "Enter") {
      b.preventDefault();
      const p = g[l];
      p && o(p);
    }
  }, x = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), v = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let y = -1;
  return /* @__PURE__ */ a.jsxs("div", { ref: c, className: "wf-connect-menu", style: { left: x, top: v }, onMouseDown: (b) => b.stopPropagation(), onClick: (b) => b.stopPropagation(), children: [
    /* @__PURE__ */ a.jsx(
      "input",
      {
        ref: d,
        type: "search",
        value: i,
        placeholder: "Search activities...",
        "aria-label": "Search activities",
        onChange: (b) => {
          s(b.target.value), u(0);
        },
        onKeyDown: m
      }
    ),
    /* @__PURE__ */ a.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: h.length === 0 ? /* @__PURE__ */ a.jsx("p", { children: "No matching activities." }) : h.map((b) => /* @__PURE__ */ a.jsxs("section", { children: [
      /* @__PURE__ */ a.jsx("h4", { children: b.category }),
      b.activities.map((p) => {
        y += 1;
        const w = y, E = w === l;
        return /* @__PURE__ */ a.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": E,
            className: E ? "active" : "",
            onMouseEnter: () => u(w),
            onClick: () => o(p),
            children: [
              /* @__PURE__ */ a.jsx("strong", { children: Ce(p) }),
              /* @__PURE__ */ a.jsx("small", { children: p.category || p.activityTypeKey })
            ]
          },
          p.activityVersionId
        );
      })
    ] }, b.category)) })
  ] });
}
function V0({ draft: e }) {
  return e.validationErrors.length ? /* @__PURE__ */ a.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ a.jsx(ft, { size: 14 }),
    e.validationErrors.length,
    " validation issue",
    e.validationErrors.length === 1 ? "" : "s"
  ] }) : /* @__PURE__ */ a.jsxs("div", { className: "wf-validation ok", children: [
    /* @__PURE__ */ a.jsx(Wn, { size: 14 }),
    " No validation errors"
  ] });
}
function O0({ testRun: e }) {
  const t = Il(e);
  return /* @__PURE__ */ a.jsxs("section", { className: "wf-test-run-capsule", "data-state": t ? "rejected" : "accepted", "aria-live": "polite", children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-test-run-heading", children: [
      t ? /* @__PURE__ */ a.jsx(ft, { size: 16 }) : /* @__PURE__ */ a.jsx(Wn, { size: 16 }),
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("strong", { children: t ? "Test run rejected" : "Test run dispatched" }),
        /* @__PURE__ */ a.jsx("span", { children: "Ephemeral - not promoted" })
      ] })
    ] }),
    t && e.reason ? /* @__PURE__ */ a.jsx("p", { children: e.reason }) : null,
    /* @__PURE__ */ a.jsxs("dl", { children: [
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ a.jsx("dd", { children: e.status })
      ] }),
      e.commandDispatchStatus ? /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Dispatch" }),
        /* @__PURE__ */ a.jsx("dd", { children: e.commandDispatchStatus })
      ] }) : null,
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Test run" }),
        /* @__PURE__ */ a.jsx("dd", { children: e.testRunId })
      ] }),
      e.artifactId ? /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Transient artifact" }),
        /* @__PURE__ */ a.jsx("dd", { children: e.artifactId })
      ] }) : null,
      e.workflowExecutionId ? /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Execution" }),
        /* @__PURE__ */ a.jsx("dd", { children: e.workflowExecutionId })
      ] }) : null,
      e.expiresAt ? /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ a.jsx("dd", { children: rt(e.expiresAt) })
      ] }) : null
    ] })
  ] });
}
function da(e) {
  return `${Ce(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function fa(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function B0(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function F0(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function wt(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function W0(e) {
  return `${e.id}-${X0(JSON.stringify(e.state))}`;
}
function X0(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function Il(e) {
  return e.status.toLowerCase() === "rejected";
}
function rt(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function Y0(e, t) {
  if (!e || !t) return "";
  const n = Date.parse(e), o = Date.parse(t);
  if (Number.isNaN(n) || Number.isNaN(o) || o < n) return "";
  const r = Math.round((o - n) / 1e3);
  if (r < 60) return `${r}s`;
  const i = Math.floor(r / 60), s = r % 60;
  if (i < 60) return s ? `${i}m ${s}s` : `${i}m`;
  const l = Math.floor(i / 60), u = i % 60;
  return u ? `${l}h ${u}m` : `${l}h`;
}
export {
  K0 as register
};
