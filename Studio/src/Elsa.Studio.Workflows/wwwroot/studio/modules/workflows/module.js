import dt, { memo as be, forwardRef as Ro, useRef as ce, useEffect as se, useCallback as pe, useContext as An, useMemo as ge, useState as q, createContext as ri, useLayoutEffect as Sl, createElement as Hr, useId as sa } from "react";
import "@tanstack/react-query";
function Nl(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var Sr = { exports: {} }, sn = {};
var Ai;
function El() {
  if (Ai) return sn;
  Ai = 1;
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
  return sn.Fragment = t, sn.jsx = n, sn.jsxs = n, sn;
}
var Mi;
function kl() {
  return Mi || (Mi = 1, Sr.exports = El()), Sr.exports;
}
var c = kl();
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
var Cl = { value: () => {
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
function _l(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
mo.prototype = Ho.prototype = {
  constructor: mo,
  on: function(e, t) {
    var n = this._, o = _l(e + "", n), r, i = -1, s = o.length;
    if (arguments.length < 2) {
      for (; ++i < s; ) if ((r = (e = o[i]).type) && (r = Il(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++i < s; )
      if (r = (e = o[i]).type) n[r] = Di(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = Di(n[r], e.name, null);
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
function Il(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function Di(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = Cl, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var Vr = "http://www.w3.org/1999/xhtml";
const Pi = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: Vr,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function Vo(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Pi.hasOwnProperty(t) ? { space: Pi[t], local: e } : e;
}
function jl(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === Vr && t.documentElement.namespaceURI === Vr ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Al(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function aa(e) {
  var t = Vo(e);
  return (t.local ? Al : jl)(t);
}
function Ml() {
}
function ii(e) {
  return e == null ? Ml : function() {
    return this.querySelector(e);
  };
}
function Dl(e) {
  typeof e != "function" && (e = ii(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, l = o[r] = new Array(s), u, a, d = 0; d < s; ++d)
      (u = i[d]) && (a = e.call(u, u.__data__, d, i)) && ("__data__" in u && (a.__data__ = u.__data__), l[d] = a);
  return new Re(o, this._parents);
}
function Pl(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function $l() {
  return [];
}
function ca(e) {
  return e == null ? $l : function() {
    return this.querySelectorAll(e);
  };
}
function Tl(e) {
  return function() {
    return Pl(e.apply(this, arguments));
  };
}
function zl(e) {
  typeof e == "function" ? e = Tl(e) : e = ca(e);
  for (var t = this._groups, n = t.length, o = [], r = [], i = 0; i < n; ++i)
    for (var s = t[i], l = s.length, u, a = 0; a < l; ++a)
      (u = s[a]) && (o.push(e.call(u, u.__data__, a, s)), r.push(u));
  return new Re(o, r);
}
function la(e) {
  return function() {
    return this.matches(e);
  };
}
function ua(e) {
  return function(t) {
    return t.matches(e);
  };
}
var Ll = Array.prototype.find;
function Rl(e) {
  return function() {
    return Ll.call(this.children, e);
  };
}
function Hl() {
  return this.firstElementChild;
}
function Vl(e) {
  return this.select(e == null ? Hl : Rl(typeof e == "function" ? e : ua(e)));
}
var Ol = Array.prototype.filter;
function Bl() {
  return Array.from(this.children);
}
function Fl(e) {
  return function() {
    return Ol.call(this.children, e);
  };
}
function Wl(e) {
  return this.selectAll(e == null ? Bl : Fl(typeof e == "function" ? e : ua(e)));
}
function Xl(e) {
  typeof e != "function" && (e = la(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, l = o[r] = [], u, a = 0; a < s; ++a)
      (u = i[a]) && e.call(u, u.__data__, a, i) && l.push(u);
  return new Re(o, this._parents);
}
function da(e) {
  return new Array(e.length);
}
function Yl() {
  return new Re(this._enter || this._groups.map(da), this._parents);
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
function ql(e) {
  return function() {
    return e;
  };
}
function Zl(e, t, n, o, r, i) {
  for (var s = 0, l, u = t.length, a = i.length; s < a; ++s)
    (l = t[s]) ? (l.__data__ = i[s], o[s] = l) : n[s] = new Eo(e, i[s]);
  for (; s < u; ++s)
    (l = t[s]) && (r[s] = l);
}
function Kl(e, t, n, o, r, i, s) {
  var l, u, a = /* @__PURE__ */ new Map(), d = t.length, f = i.length, h = new Array(d), g;
  for (l = 0; l < d; ++l)
    (u = t[l]) && (h[l] = g = s.call(u, u.__data__, l, t) + "", a.has(g) ? r[l] = u : a.set(g, u));
  for (l = 0; l < f; ++l)
    g = s.call(e, i[l], l, i) + "", (u = a.get(g)) ? (o[l] = u, u.__data__ = i[l], a.delete(g)) : n[l] = new Eo(e, i[l]);
  for (l = 0; l < d; ++l)
    (u = t[l]) && a.get(h[l]) === u && (r[l] = u);
}
function Ul(e) {
  return e.__data__;
}
function Gl(e, t) {
  if (!arguments.length) return Array.from(this, Ul);
  var n = t ? Kl : Zl, o = this._parents, r = this._groups;
  typeof e != "function" && (e = ql(e));
  for (var i = r.length, s = new Array(i), l = new Array(i), u = new Array(i), a = 0; a < i; ++a) {
    var d = o[a], f = r[a], h = f.length, g = Ql(e.call(d, d && d.__data__, a, o)), m = g.length, x = l[a] = new Array(m), v = s[a] = new Array(m), y = u[a] = new Array(h);
    n(d, f, x, v, y, g, t);
    for (var b = 0, p = 0, w, E; b < m; ++b)
      if (w = x[b]) {
        for (b >= p && (p = b + 1); !(E = v[p]) && ++p < m; ) ;
        w._next = E || null;
      }
  }
  return s = new Re(s, o), s._enter = l, s._exit = u, s;
}
function Ql(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function Jl() {
  return new Re(this._exit || this._groups.map(da), this._parents);
}
function eu(e, t, n) {
  var o = this.enter(), r = this, i = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? i.remove() : n(i), o && r ? o.merge(r).order() : r;
}
function tu(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, i = o.length, s = Math.min(r, i), l = new Array(r), u = 0; u < s; ++u)
    for (var a = n[u], d = o[u], f = a.length, h = l[u] = new Array(f), g, m = 0; m < f; ++m)
      (g = a[m] || d[m]) && (h[m] = g);
  for (; u < r; ++u)
    l[u] = n[u];
  return new Re(l, this._parents);
}
function nu() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, i = o[r], s; --r >= 0; )
      (s = o[r]) && (i && s.compareDocumentPosition(i) ^ 4 && i.parentNode.insertBefore(s, i), i = s);
  return this;
}
function ou(e) {
  e || (e = ru);
  function t(f, h) {
    return f && h ? e(f.__data__, h.__data__) : !f - !h;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), i = 0; i < o; ++i) {
    for (var s = n[i], l = s.length, u = r[i] = new Array(l), a, d = 0; d < l; ++d)
      (a = s[d]) && (u[d] = a);
    u.sort(t);
  }
  return new Re(r, this._parents).order();
}
function ru(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function iu() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function su() {
  return Array.from(this);
}
function au() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length; r < i; ++r) {
      var s = o[r];
      if (s) return s;
    }
  return null;
}
function cu() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function lu() {
  return !this.node();
}
function uu(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], i = 0, s = r.length, l; i < s; ++i)
      (l = r[i]) && e.call(l, l.__data__, i, r);
  return this;
}
function du(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function fu(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function hu(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function pu(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function gu(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function mu(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function yu(e, t) {
  var n = Vo(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? fu : du : typeof t == "function" ? n.local ? mu : gu : n.local ? pu : hu)(n, t));
}
function fa(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function xu(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function wu(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function vu(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function bu(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? xu : typeof t == "function" ? vu : wu)(e, t, n ?? "")) : Tt(this.node(), e);
}
function Tt(e, t) {
  return e.style.getPropertyValue(t) || fa(e).getComputedStyle(e, null).getPropertyValue(t);
}
function Su(e) {
  return function() {
    delete this[e];
  };
}
function Nu(e, t) {
  return function() {
    this[e] = t;
  };
}
function Eu(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function ku(e, t) {
  return arguments.length > 1 ? this.each((t == null ? Su : typeof t == "function" ? Eu : Nu)(e, t)) : this.node()[e];
}
function ha(e) {
  return e.trim().split(/^|\s+/);
}
function si(e) {
  return e.classList || new pa(e);
}
function pa(e) {
  this._node = e, this._names = ha(e.getAttribute("class") || "");
}
pa.prototype = {
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
function ga(e, t) {
  for (var n = si(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function ma(e, t) {
  for (var n = si(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function Cu(e) {
  return function() {
    ga(this, e);
  };
}
function _u(e) {
  return function() {
    ma(this, e);
  };
}
function Iu(e, t) {
  return function() {
    (t.apply(this, arguments) ? ga : ma)(this, e);
  };
}
function ju(e, t) {
  var n = ha(e + "");
  if (arguments.length < 2) {
    for (var o = si(this.node()), r = -1, i = n.length; ++r < i; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? Iu : t ? Cu : _u)(n, t));
}
function Au() {
  this.textContent = "";
}
function Mu(e) {
  return function() {
    this.textContent = e;
  };
}
function Du(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function Pu(e) {
  return arguments.length ? this.each(e == null ? Au : (typeof e == "function" ? Du : Mu)(e)) : this.node().textContent;
}
function $u() {
  this.innerHTML = "";
}
function Tu(e) {
  return function() {
    this.innerHTML = e;
  };
}
function zu(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function Lu(e) {
  return arguments.length ? this.each(e == null ? $u : (typeof e == "function" ? zu : Tu)(e)) : this.node().innerHTML;
}
function Ru() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function Hu() {
  return this.each(Ru);
}
function Vu() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Ou() {
  return this.each(Vu);
}
function Bu(e) {
  var t = typeof e == "function" ? e : aa(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Fu() {
  return null;
}
function Wu(e, t) {
  var n = typeof e == "function" ? e : aa(e), o = t == null ? Fu : typeof t == "function" ? t : ii(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function Xu() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Yu() {
  return this.each(Xu);
}
function qu() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Zu() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Ku(e) {
  return this.select(e ? Zu : qu);
}
function Uu(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function Gu(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function Qu(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function Ju(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, i; n < r; ++n)
        i = t[n], (!e.type || i.type === e.type) && i.name === e.name ? this.removeEventListener(i.type, i.listener, i.options) : t[++o] = i;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function ed(e, t, n) {
  return function() {
    var o = this.__on, r, i = Gu(t);
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
function td(e, t, n) {
  var o = Qu(e + ""), r, i = o.length, s;
  if (arguments.length < 2) {
    var l = this.node().__on;
    if (l) {
      for (var u = 0, a = l.length, d; u < a; ++u)
        for (r = 0, d = l[u]; r < i; ++r)
          if ((s = o[r]).type === d.type && s.name === d.name)
            return d.value;
    }
    return;
  }
  for (l = t ? ed : Ju, r = 0; r < i; ++r) this.each(l(o[r], t, n));
  return this;
}
function ya(e, t, n) {
  var o = fa(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function nd(e, t) {
  return function() {
    return ya(this, e, t);
  };
}
function od(e, t) {
  return function() {
    return ya(this, e, t.apply(this, arguments));
  };
}
function rd(e, t) {
  return this.each((typeof t == "function" ? od : nd)(e, t));
}
function* id() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length, s; r < i; ++r)
      (s = o[r]) && (yield s);
}
var xa = [null];
function Re(e, t) {
  this._groups = e, this._parents = t;
}
function Mn() {
  return new Re([[document.documentElement]], xa);
}
function sd() {
  return this;
}
Re.prototype = Mn.prototype = {
  constructor: Re,
  select: Dl,
  selectAll: zl,
  selectChild: Vl,
  selectChildren: Wl,
  filter: Xl,
  data: Gl,
  enter: Yl,
  exit: Jl,
  join: eu,
  merge: tu,
  selection: sd,
  order: nu,
  sort: ou,
  call: iu,
  nodes: su,
  node: au,
  size: cu,
  empty: lu,
  each: uu,
  attr: yu,
  style: bu,
  property: ku,
  classed: ju,
  text: Pu,
  html: Lu,
  raise: Hu,
  lower: Ou,
  append: Bu,
  insert: Wu,
  remove: Yu,
  clone: Ku,
  datum: Uu,
  on: td,
  dispatch: rd,
  [Symbol.iterator]: id
};
function Le(e) {
  return typeof e == "string" ? new Re([[document.querySelector(e)]], [document.documentElement]) : new Re([[e]], xa);
}
function ad(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Xe(e, t) {
  if (e = ad(e), t === void 0 && (t = e.currentTarget), t) {
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
const cd = { passive: !1 }, vn = { capture: !0, passive: !1 };
function Nr(e) {
  e.stopImmediatePropagation();
}
function Pt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function wa(e) {
  var t = e.document.documentElement, n = Le(e).on("dragstart.drag", Pt, vn);
  "onselectstart" in t ? n.on("selectstart.drag", Pt, vn) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function va(e, t) {
  var n = e.document.documentElement, o = Le(e).on("dragstart.drag", null);
  t && (o.on("click.drag", Pt, vn), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const ro = (e) => () => e;
function Or(e, {
  sourceEvent: t,
  subject: n,
  target: o,
  identifier: r,
  active: i,
  x: s,
  y: l,
  dx: u,
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
    y: { value: l, enumerable: !0, configurable: !0 },
    dx: { value: u, enumerable: !0, configurable: !0 },
    dy: { value: a, enumerable: !0, configurable: !0 },
    _: { value: d }
  });
}
Or.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function ld(e) {
  return !e.ctrlKey && !e.button;
}
function ud() {
  return this.parentNode;
}
function dd(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function fd() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function ba() {
  var e = ld, t = ud, n = dd, o = fd, r = {}, i = Ho("start", "drag", "end"), s = 0, l, u, a, d, f = 0;
  function h(w) {
    w.on("mousedown.drag", g).filter(o).on("touchstart.drag", v).on("touchmove.drag", y, cd).on("touchend.drag touchcancel.drag", b).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function g(w, E) {
    if (!(d || !e.call(this, w, E))) {
      var k = p(this, t.call(this, w, E), w, E, "mouse");
      k && (Le(w.view).on("mousemove.drag", m, vn).on("mouseup.drag", x, vn), wa(w.view), Nr(w), a = !1, l = w.clientX, u = w.clientY, k("start", w));
    }
  }
  function m(w) {
    if (Pt(w), !a) {
      var E = w.clientX - l, k = w.clientY - u;
      a = E * E + k * k > f;
    }
    r.mouse("drag", w);
  }
  function x(w) {
    Le(w.view).on("mousemove.drag mouseup.drag", null), va(w.view, a), Pt(w), r.mouse("end", w);
  }
  function v(w, E) {
    if (e.call(this, w, E)) {
      var k = w.changedTouches, _ = t.call(this, w, E), D = k.length, $, W;
      for ($ = 0; $ < D; ++$)
        (W = p(this, _, w, E, k[$].identifier, k[$])) && (Nr(w), W("start", w, k[$]));
    }
  }
  function y(w) {
    var E = w.changedTouches, k = E.length, _, D;
    for (_ = 0; _ < k; ++_)
      (D = r[E[_].identifier]) && (Pt(w), D("drag", w, E[_]));
  }
  function b(w) {
    var E = w.changedTouches, k = E.length, _, D;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), _ = 0; _ < k; ++_)
      (D = r[E[_].identifier]) && (Nr(w), D("end", w, E[_]));
  }
  function p(w, E, k, _, D, $) {
    var W = i.copy(), j = Xe($ || k, E), z, H, S;
    if ((S = n.call(w, new Or("beforestart", {
      sourceEvent: k,
      target: h,
      identifier: D,
      active: s,
      x: j[0],
      y: j[1],
      dx: 0,
      dy: 0,
      dispatch: W
    }), _)) != null)
      return z = S.x - j[0] || 0, H = S.y - j[1] || 0, function I(C, A, T) {
        var P = j, V;
        switch (C) {
          case "start":
            r[D] = I, V = s++;
            break;
          case "end":
            delete r[D], --s;
          // falls through
          case "drag":
            j = Xe(T || A, E), V = s;
            break;
        }
        W.call(
          C,
          w,
          new Or(C, {
            sourceEvent: A,
            subject: S,
            target: h,
            identifier: D,
            active: V,
            x: j[0] + z,
            y: j[1] + H,
            dx: j[0] - P[0],
            dy: j[1] - P[1],
            dispatch: W
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
function ai(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function Sa(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function Dn() {
}
var bn = 0.7, ko = 1 / bn, $t = "\\s*([+-]?\\d+)\\s*", Sn = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", Qe = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", hd = /^#([0-9a-f]{3,8})$/, pd = new RegExp(`^rgb\\(${$t},${$t},${$t}\\)$`), gd = new RegExp(`^rgb\\(${Qe},${Qe},${Qe}\\)$`), md = new RegExp(`^rgba\\(${$t},${$t},${$t},${Sn}\\)$`), yd = new RegExp(`^rgba\\(${Qe},${Qe},${Qe},${Sn}\\)$`), xd = new RegExp(`^hsl\\(${Sn},${Qe},${Qe}\\)$`), wd = new RegExp(`^hsla\\(${Sn},${Qe},${Qe},${Sn}\\)$`), $i = {
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
ai(Dn, xt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Ti,
  // Deprecated! Use color.formatHex.
  formatHex: Ti,
  formatHex8: vd,
  formatHsl: bd,
  formatRgb: zi,
  toString: zi
});
function Ti() {
  return this.rgb().formatHex();
}
function vd() {
  return this.rgb().formatHex8();
}
function bd() {
  return Na(this).formatHsl();
}
function zi() {
  return this.rgb().formatRgb();
}
function xt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = hd.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? Li(t) : n === 3 ? new $e(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? io(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? io(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = pd.exec(e)) ? new $e(t[1], t[2], t[3], 1) : (t = gd.exec(e)) ? new $e(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = md.exec(e)) ? io(t[1], t[2], t[3], t[4]) : (t = yd.exec(e)) ? io(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = xd.exec(e)) ? Vi(t[1], t[2] / 100, t[3] / 100, 1) : (t = wd.exec(e)) ? Vi(t[1], t[2] / 100, t[3] / 100, t[4]) : $i.hasOwnProperty(e) ? Li($i[e]) : e === "transparent" ? new $e(NaN, NaN, NaN, 0) : null;
}
function Li(e) {
  return new $e(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function io(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new $e(e, t, n, o);
}
function Sd(e) {
  return e instanceof Dn || (e = xt(e)), e ? (e = e.rgb(), new $e(e.r, e.g, e.b, e.opacity)) : new $e();
}
function Br(e, t, n, o) {
  return arguments.length === 1 ? Sd(e) : new $e(e, t, n, o ?? 1);
}
function $e(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
ai($e, Br, Sa(Dn, {
  brighter(e) {
    return e = e == null ? ko : Math.pow(ko, e), new $e(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? bn : Math.pow(bn, e), new $e(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new $e(mt(this.r), mt(this.g), mt(this.b), Co(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: Ri,
  // Deprecated! Use color.formatHex.
  formatHex: Ri,
  formatHex8: Nd,
  formatRgb: Hi,
  toString: Hi
}));
function Ri() {
  return `#${gt(this.r)}${gt(this.g)}${gt(this.b)}`;
}
function Nd() {
  return `#${gt(this.r)}${gt(this.g)}${gt(this.b)}${gt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function Hi() {
  const e = Co(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${mt(this.r)}, ${mt(this.g)}, ${mt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Co(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function mt(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function gt(e) {
  return e = mt(e), (e < 16 ? "0" : "") + e.toString(16);
}
function Vi(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Ye(e, t, n, o);
}
function Na(e) {
  if (e instanceof Ye) return new Ye(e.h, e.s, e.l, e.opacity);
  if (e instanceof Dn || (e = xt(e)), !e) return new Ye();
  if (e instanceof Ye) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), i = Math.max(t, n, o), s = NaN, l = i - r, u = (i + r) / 2;
  return l ? (t === i ? s = (n - o) / l + (n < o) * 6 : n === i ? s = (o - t) / l + 2 : s = (t - n) / l + 4, l /= u < 0.5 ? i + r : 2 - i - r, s *= 60) : l = u > 0 && u < 1 ? 0 : s, new Ye(s, l, u, e.opacity);
}
function Ed(e, t, n, o) {
  return arguments.length === 1 ? Na(e) : new Ye(e, t, n, o ?? 1);
}
function Ye(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
ai(Ye, Ed, Sa(Dn, {
  brighter(e) {
    return e = e == null ? ko : Math.pow(ko, e), new Ye(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? bn : Math.pow(bn, e), new Ye(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new $e(
      Er(e >= 240 ? e - 240 : e + 120, r, o),
      Er(e, r, o),
      Er(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new Ye(Oi(this.h), so(this.s), so(this.l), Co(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Co(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${Oi(this.h)}, ${so(this.s) * 100}%, ${so(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function Oi(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function so(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function Er(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const ci = (e) => () => e;
function kd(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function Cd(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function _d(e) {
  return (e = +e) == 1 ? Ea : function(t, n) {
    return n - t ? Cd(t, n, e) : ci(isNaN(t) ? n : t);
  };
}
function Ea(e, t) {
  var n = t - e;
  return n ? kd(e, n) : ci(isNaN(e) ? t : e);
}
const _o = (function e(t) {
  var n = _d(t);
  function o(r, i) {
    var s = n((r = Br(r)).r, (i = Br(i)).r), l = n(r.g, i.g), u = n(r.b, i.b), a = Ea(r.opacity, i.opacity);
    return function(d) {
      return r.r = s(d), r.g = l(d), r.b = u(d), r.opacity = a(d), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function Id(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(i) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - i) + t[r] * i;
    return o;
  };
}
function jd(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Ad(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), i = new Array(n), s;
  for (s = 0; s < o; ++s) r[s] = xn(e[s], t[s]);
  for (; s < n; ++s) i[s] = t[s];
  return function(l) {
    for (s = 0; s < o; ++s) i[s] = r[s](l);
    return i;
  };
}
function Md(e, t) {
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
function Dd(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = xn(e[r], t[r]) : o[r] = t[r];
  return function(i) {
    for (r in n) o[r] = n[r](i);
    return o;
  };
}
var Fr = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, kr = new RegExp(Fr.source, "g");
function Pd(e) {
  return function() {
    return e;
  };
}
function $d(e) {
  return function(t) {
    return e(t) + "";
  };
}
function ka(e, t) {
  var n = Fr.lastIndex = kr.lastIndex = 0, o, r, i, s = -1, l = [], u = [];
  for (e = e + "", t = t + ""; (o = Fr.exec(e)) && (r = kr.exec(t)); )
    (i = r.index) > n && (i = t.slice(n, i), l[s] ? l[s] += i : l[++s] = i), (o = o[0]) === (r = r[0]) ? l[s] ? l[s] += r : l[++s] = r : (l[++s] = null, u.push({ i: s, x: Ge(o, r) })), n = kr.lastIndex;
  return n < t.length && (i = t.slice(n), l[s] ? l[s] += i : l[++s] = i), l.length < 2 ? u[0] ? $d(u[0].x) : Pd(t) : (t = u.length, function(a) {
    for (var d = 0, f; d < t; ++d) l[(f = u[d]).i] = f.x(a);
    return l.join("");
  });
}
function xn(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? ci(t) : (n === "number" ? Ge : n === "string" ? (o = xt(t)) ? (t = o, _o) : ka : t instanceof xt ? _o : t instanceof Date ? Md : jd(t) ? Id : Array.isArray(t) ? Ad : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Dd : Ge)(e, t);
}
var Bi = 180 / Math.PI, Wr = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function Ca(e, t, n, o, r, i) {
  var s, l, u;
  return (s = Math.sqrt(e * e + t * t)) && (e /= s, t /= s), (u = e * n + t * o) && (n -= e * u, o -= t * u), (l = Math.sqrt(n * n + o * o)) && (n /= l, o /= l, u /= l), e * o < t * n && (e = -e, t = -t, u = -u, s = -s), {
    translateX: r,
    translateY: i,
    rotate: Math.atan2(t, e) * Bi,
    skewX: Math.atan(u) * Bi,
    scaleX: s,
    scaleY: l
  };
}
var ao;
function Td(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Wr : Ca(t.a, t.b, t.c, t.d, t.e, t.f);
}
function zd(e) {
  return e == null || (ao || (ao = document.createElementNS("http://www.w3.org/2000/svg", "g")), ao.setAttribute("transform", e), !(e = ao.transform.baseVal.consolidate())) ? Wr : (e = e.matrix, Ca(e.a, e.b, e.c, e.d, e.e, e.f));
}
function _a(e, t, n, o) {
  function r(a) {
    return a.length ? a.pop() + " " : "";
  }
  function i(a, d, f, h, g, m) {
    if (a !== f || d !== h) {
      var x = g.push("translate(", null, t, null, n);
      m.push({ i: x - 4, x: Ge(a, f) }, { i: x - 2, x: Ge(d, h) });
    } else (f || h) && g.push("translate(" + f + t + h + n);
  }
  function s(a, d, f, h) {
    a !== d ? (a - d > 180 ? d += 360 : d - a > 180 && (a += 360), h.push({ i: f.push(r(f) + "rotate(", null, o) - 2, x: Ge(a, d) })) : d && f.push(r(f) + "rotate(" + d + o);
  }
  function l(a, d, f, h) {
    a !== d ? h.push({ i: f.push(r(f) + "skewX(", null, o) - 2, x: Ge(a, d) }) : d && f.push(r(f) + "skewX(" + d + o);
  }
  function u(a, d, f, h, g, m) {
    if (a !== f || d !== h) {
      var x = g.push(r(g) + "scale(", null, ",", null, ")");
      m.push({ i: x - 4, x: Ge(a, f) }, { i: x - 2, x: Ge(d, h) });
    } else (f !== 1 || h !== 1) && g.push(r(g) + "scale(" + f + "," + h + ")");
  }
  return function(a, d) {
    var f = [], h = [];
    return a = e(a), d = e(d), i(a.translateX, a.translateY, d.translateX, d.translateY, f, h), s(a.rotate, d.rotate, f, h), l(a.skewX, d.skewX, f, h), u(a.scaleX, a.scaleY, d.scaleX, d.scaleY, f, h), a = d = null, function(g) {
      for (var m = -1, x = h.length, v; ++m < x; ) f[(v = h[m]).i] = v.x(g);
      return f.join("");
    };
  };
}
var Ld = _a(Td, "px, ", "px)", "deg)"), Rd = _a(zd, ", ", ")", ")"), Hd = 1e-12;
function Fi(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Vd(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Od(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const yo = (function e(t, n, o) {
  function r(i, s) {
    var l = i[0], u = i[1], a = i[2], d = s[0], f = s[1], h = s[2], g = d - l, m = f - u, x = g * g + m * m, v, y;
    if (x < Hd)
      y = Math.log(h / a) / t, v = function(_) {
        return [
          l + _ * g,
          u + _ * m,
          a * Math.exp(t * _ * y)
        ];
      };
    else {
      var b = Math.sqrt(x), p = (h * h - a * a + o * x) / (2 * a * n * b), w = (h * h - a * a - o * x) / (2 * h * n * b), E = Math.log(Math.sqrt(p * p + 1) - p), k = Math.log(Math.sqrt(w * w + 1) - w);
      y = (k - E) / t, v = function(_) {
        var D = _ * y, $ = Fi(E), W = a / (n * b) * ($ * Od(t * D + E) - Vd(E));
        return [
          l + W * g,
          u + W * m,
          a * $ / Fi(t * D + E)
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
var zt = 0, gn = 0, an = 0, Ia = 1e3, Io, mn, jo = 0, wt = 0, Oo = 0, Nn = typeof performance == "object" && performance.now ? performance : Date, ja = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function li() {
  return wt || (ja(Bd), wt = Nn.now() + Oo);
}
function Bd() {
  wt = 0;
}
function Ao() {
  this._call = this._time = this._next = null;
}
Ao.prototype = Aa.prototype = {
  constructor: Ao,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? li() : +n) + (t == null ? 0 : +t), !this._next && mn !== this && (mn ? mn._next = this : Io = this, mn = this), this._call = e, this._time = n, Xr();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Xr());
  }
};
function Aa(e, t, n) {
  var o = new Ao();
  return o.restart(e, t, n), o;
}
function Fd() {
  li(), ++zt;
  for (var e = Io, t; e; )
    (t = wt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --zt;
}
function Wi() {
  wt = (jo = Nn.now()) + Oo, zt = gn = 0;
  try {
    Fd();
  } finally {
    zt = 0, Xd(), wt = 0;
  }
}
function Wd() {
  var e = Nn.now(), t = e - jo;
  t > Ia && (Oo -= t, jo = e);
}
function Xd() {
  for (var e, t = Io, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Io = n);
  mn = e, Xr(o);
}
function Xr(e) {
  if (!zt) {
    gn && (gn = clearTimeout(gn));
    var t = e - wt;
    t > 24 ? (e < 1 / 0 && (gn = setTimeout(Wi, e - Nn.now() - Oo)), an && (an = clearInterval(an))) : (an || (jo = Nn.now(), an = setInterval(Wd, Ia)), zt = 1, ja(Wi));
  }
}
function Xi(e, t, n) {
  var o = new Ao();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var Yd = Ho("start", "end", "cancel", "interrupt"), qd = [], Ma = 0, Yi = 1, Yr = 2, xo = 3, qi = 4, qr = 5, wo = 6;
function Bo(e, t, n, o, r, i) {
  var s = e.__transition;
  if (!s) e.__transition = {};
  else if (n in s) return;
  Zd(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: Yd,
    tween: qd,
    time: i.time,
    delay: i.delay,
    duration: i.duration,
    ease: i.ease,
    timer: null,
    state: Ma
  });
}
function ui(e, t) {
  var n = Ke(e, t);
  if (n.state > Ma) throw new Error("too late; already scheduled");
  return n;
}
function et(e, t) {
  var n = Ke(e, t);
  if (n.state > xo) throw new Error("too late; already running");
  return n;
}
function Ke(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function Zd(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = Aa(i, 0, n.time);
  function i(a) {
    n.state = Yi, n.timer.restart(s, n.delay, n.time), n.delay <= a && s(a - n.delay);
  }
  function s(a) {
    var d, f, h, g;
    if (n.state !== Yi) return u();
    for (d in o)
      if (g = o[d], g.name === n.name) {
        if (g.state === xo) return Xi(s);
        g.state === qi ? (g.state = wo, g.timer.stop(), g.on.call("interrupt", e, e.__data__, g.index, g.group), delete o[d]) : +d < t && (g.state = wo, g.timer.stop(), g.on.call("cancel", e, e.__data__, g.index, g.group), delete o[d]);
      }
    if (Xi(function() {
      n.state === xo && (n.state = qi, n.timer.restart(l, n.delay, n.time), l(a));
    }), n.state = Yr, n.on.call("start", e, e.__data__, n.index, n.group), n.state === Yr) {
      for (n.state = xo, r = new Array(h = n.tween.length), d = 0, f = -1; d < h; ++d)
        (g = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (r[++f] = g);
      r.length = f + 1;
    }
  }
  function l(a) {
    for (var d = a < n.duration ? n.ease.call(null, a / n.duration) : (n.timer.restart(u), n.state = qr, 1), f = -1, h = r.length; ++f < h; )
      r[f].call(e, d);
    n.state === qr && (n.on.call("end", e, e.__data__, n.index, n.group), u());
  }
  function u() {
    n.state = wo, n.timer.stop(), delete o[t];
    for (var a in o) return;
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
      r = o.state > Yr && o.state < qr, o.state = wo, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[s];
    }
    i && delete e.__transition;
  }
}
function Kd(e) {
  return this.each(function() {
    vo(this, e);
  });
}
function Ud(e, t) {
  var n, o;
  return function() {
    var r = et(this, e), i = r.tween;
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
function Gd(e, t, n) {
  var o, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var i = et(this, e), s = i.tween;
    if (s !== o) {
      r = (o = s).slice();
      for (var l = { name: t, value: n }, u = 0, a = r.length; u < a; ++u)
        if (r[u].name === t) {
          r[u] = l;
          break;
        }
      u === a && r.push(l);
    }
    i.tween = r;
  };
}
function Qd(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = Ke(this.node(), n).tween, r = 0, i = o.length, s; r < i; ++r)
      if ((s = o[r]).name === e)
        return s.value;
    return null;
  }
  return this.each((t == null ? Ud : Gd)(n, e, t));
}
function di(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = et(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return Ke(r, o).value[t];
  };
}
function Da(e, t) {
  var n;
  return (typeof t == "number" ? Ge : t instanceof xt ? _o : (n = xt(t)) ? (t = n, _o) : ka)(e, t);
}
function Jd(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function ef(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function tf(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttribute(e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function nf(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttributeNS(e.space, e.local);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function of(e, t, n) {
  var o, r, i;
  return function() {
    var s, l = n(this), u;
    return l == null ? void this.removeAttribute(e) : (s = this.getAttribute(e), u = l + "", s === u ? null : s === o && u === r ? i : (r = u, i = t(o = s, l)));
  };
}
function rf(e, t, n) {
  var o, r, i;
  return function() {
    var s, l = n(this), u;
    return l == null ? void this.removeAttributeNS(e.space, e.local) : (s = this.getAttributeNS(e.space, e.local), u = l + "", s === u ? null : s === o && u === r ? i : (r = u, i = t(o = s, l)));
  };
}
function sf(e, t) {
  var n = Vo(e), o = n === "transform" ? Rd : Da;
  return this.attrTween(e, typeof t == "function" ? (n.local ? rf : of)(n, o, di(this, "attr." + e, t)) : t == null ? (n.local ? ef : Jd)(n) : (n.local ? nf : tf)(n, o, t));
}
function af(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function cf(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function lf(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && cf(e, i)), n;
  }
  return r._value = t, r;
}
function uf(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && af(e, i)), n;
  }
  return r._value = t, r;
}
function df(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = Vo(e);
  return this.tween(n, (o.local ? lf : uf)(o, t));
}
function ff(e, t) {
  return function() {
    ui(this, e).delay = +t.apply(this, arguments);
  };
}
function hf(e, t) {
  return t = +t, function() {
    ui(this, e).delay = t;
  };
}
function pf(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? ff : hf)(t, e)) : Ke(this.node(), t).delay;
}
function gf(e, t) {
  return function() {
    et(this, e).duration = +t.apply(this, arguments);
  };
}
function mf(e, t) {
  return t = +t, function() {
    et(this, e).duration = t;
  };
}
function yf(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? gf : mf)(t, e)) : Ke(this.node(), t).duration;
}
function xf(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    et(this, e).ease = t;
  };
}
function wf(e) {
  var t = this._id;
  return arguments.length ? this.each(xf(t, e)) : Ke(this.node(), t).ease;
}
function vf(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    et(this, e).ease = n;
  };
}
function bf(e) {
  if (typeof e != "function") throw new Error();
  return this.each(vf(this._id, e));
}
function Sf(e) {
  typeof e != "function" && (e = la(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, l = o[r] = [], u, a = 0; a < s; ++a)
      (u = i[a]) && e.call(u, u.__data__, a, i) && l.push(u);
  return new rt(o, this._parents, this._name, this._id);
}
function Nf(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, i = Math.min(o, r), s = new Array(o), l = 0; l < i; ++l)
    for (var u = t[l], a = n[l], d = u.length, f = s[l] = new Array(d), h, g = 0; g < d; ++g)
      (h = u[g] || a[g]) && (f[g] = h);
  for (; l < o; ++l)
    s[l] = t[l];
  return new rt(s, this._parents, this._name, this._id);
}
function Ef(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function kf(e, t, n) {
  var o, r, i = Ef(t) ? ui : et;
  return function() {
    var s = i(this, e), l = s.on;
    l !== o && (r = (o = l).copy()).on(t, n), s.on = r;
  };
}
function Cf(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Ke(this.node(), n).on.on(e) : this.each(kf(n, e, t));
}
function _f(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function If() {
  return this.on("end.remove", _f(this._id));
}
function jf(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = ii(e));
  for (var o = this._groups, r = o.length, i = new Array(r), s = 0; s < r; ++s)
    for (var l = o[s], u = l.length, a = i[s] = new Array(u), d, f, h = 0; h < u; ++h)
      (d = l[h]) && (f = e.call(d, d.__data__, h, l)) && ("__data__" in d && (f.__data__ = d.__data__), a[h] = f, Bo(a[h], t, n, h, a, Ke(d, n)));
  return new rt(i, this._parents, t, n);
}
function Af(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = ca(e));
  for (var o = this._groups, r = o.length, i = [], s = [], l = 0; l < r; ++l)
    for (var u = o[l], a = u.length, d, f = 0; f < a; ++f)
      if (d = u[f]) {
        for (var h = e.call(d, d.__data__, f, u), g, m = Ke(d, n), x = 0, v = h.length; x < v; ++x)
          (g = h[x]) && Bo(g, t, n, x, h, m);
        i.push(h), s.push(d);
      }
  return new rt(i, s, t, n);
}
var Mf = Mn.prototype.constructor;
function Df() {
  return new Mf(this._groups, this._parents);
}
function Pf(e, t) {
  var n, o, r;
  return function() {
    var i = Tt(this, e), s = (this.style.removeProperty(e), Tt(this, e));
    return i === s ? null : i === n && s === o ? r : r = t(n = i, o = s);
  };
}
function Pa(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function $f(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = Tt(this, e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Tf(e, t, n) {
  var o, r, i;
  return function() {
    var s = Tt(this, e), l = n(this), u = l + "";
    return l == null && (u = l = (this.style.removeProperty(e), Tt(this, e))), s === u ? null : s === o && u === r ? i : (r = u, i = t(o = s, l));
  };
}
function zf(e, t) {
  var n, o, r, i = "style." + t, s = "end." + i, l;
  return function() {
    var u = et(this, e), a = u.on, d = u.value[i] == null ? l || (l = Pa(t)) : void 0;
    (a !== n || r !== d) && (o = (n = a).copy()).on(s, r = d), u.on = o;
  };
}
function Lf(e, t, n) {
  var o = (e += "") == "transform" ? Ld : Da;
  return t == null ? this.styleTween(e, Pf(e, o)).on("end.style." + e, Pa(e)) : typeof t == "function" ? this.styleTween(e, Tf(e, o, di(this, "style." + e, t))).each(zf(this._id, e)) : this.styleTween(e, $f(e, o, t), n).on("end.style." + e, null);
}
function Rf(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function Hf(e, t, n) {
  var o, r;
  function i() {
    var s = t.apply(this, arguments);
    return s !== r && (o = (r = s) && Rf(e, s, n)), o;
  }
  return i._value = t, i;
}
function Vf(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, Hf(e, t, n ?? ""));
}
function Of(e) {
  return function() {
    this.textContent = e;
  };
}
function Bf(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function Ff(e) {
  return this.tween("text", typeof e == "function" ? Bf(di(this, "text", e)) : Of(e == null ? "" : e + ""));
}
function Wf(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Xf(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Wf(r)), t;
  }
  return o._value = e, o;
}
function Yf(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, Xf(e));
}
function qf() {
  for (var e = this._name, t = this._id, n = $a(), o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], l = s.length, u, a = 0; a < l; ++a)
      if (u = s[a]) {
        var d = Ke(u, t);
        Bo(u, e, n, a, s, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new rt(o, this._parents, e, n);
}
function Zf() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(i, s) {
    var l = { value: s }, u = { value: function() {
      --r === 0 && i();
    } };
    n.each(function() {
      var a = et(this, o), d = a.on;
      d !== e && (t = (e = d).copy(), t._.cancel.push(l), t._.interrupt.push(l), t._.end.push(u)), a.on = t;
    }), r === 0 && i();
  });
}
var Kf = 0;
function rt(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function $a() {
  return ++Kf;
}
var nt = Mn.prototype;
rt.prototype = {
  constructor: rt,
  select: jf,
  selectAll: Af,
  selectChild: nt.selectChild,
  selectChildren: nt.selectChildren,
  filter: Sf,
  merge: Nf,
  selection: Df,
  transition: qf,
  call: nt.call,
  nodes: nt.nodes,
  node: nt.node,
  size: nt.size,
  empty: nt.empty,
  each: nt.each,
  on: Cf,
  attr: sf,
  attrTween: df,
  style: Lf,
  styleTween: Vf,
  text: Ff,
  textTween: Yf,
  remove: If,
  tween: Qd,
  delay: pf,
  duration: yf,
  ease: wf,
  easeVarying: bf,
  end: Zf,
  [Symbol.iterator]: nt[Symbol.iterator]
};
function Uf(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var Gf = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: Uf
};
function Qf(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function Jf(e) {
  var t, n;
  e instanceof rt ? (t = e._id, e = e._name) : (t = $a(), (n = Gf).time = li(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], l = s.length, u, a = 0; a < l; ++a)
      (u = s[a]) && Bo(u, e, t, a, s, n || Qf(u, t));
  return new rt(o, this._parents, e, t);
}
Mn.prototype.interrupt = Kd;
Mn.prototype.transition = Jf;
const co = (e) => () => e;
function eh(e, {
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
Ta.prototype = ot.prototype;
function Ta(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return Fo;
  return e.__zoom;
}
function Cr(e) {
  e.stopImmediatePropagation();
}
function cn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function th(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function nh() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function Zi() {
  return this.__zoom || Fo;
}
function oh(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function rh() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function ih(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], i = e.invertY(t[0][1]) - n[0][1], s = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    s > i ? (i + s) / 2 : Math.min(0, i) || Math.max(0, s)
  );
}
function za() {
  var e = th, t = nh, n = ih, o = oh, r = rh, i = [0, 1 / 0], s = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], l = 250, u = yo, a = Ho("start", "zoom", "end"), d, f, h, g = 500, m = 150, x = 0, v = 10;
  function y(S) {
    S.property("__zoom", Zi).on("wheel.zoom", D, { passive: !1 }).on("mousedown.zoom", $).on("dblclick.zoom", W).filter(r).on("touchstart.zoom", j).on("touchmove.zoom", z).on("touchend.zoom touchcancel.zoom", H).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  y.transform = function(S, I, C, A) {
    var T = S.selection ? S.selection() : S;
    T.property("__zoom", Zi), S !== T ? E(S, I, C, A) : T.interrupt().each(function() {
      k(this, arguments).event(A).start().zoom(null, typeof I == "function" ? I.apply(this, arguments) : I).end();
    });
  }, y.scaleBy = function(S, I, C, A) {
    y.scaleTo(S, function() {
      var T = this.__zoom.k, P = typeof I == "function" ? I.apply(this, arguments) : I;
      return T * P;
    }, C, A);
  }, y.scaleTo = function(S, I, C, A) {
    y.transform(S, function() {
      var T = t.apply(this, arguments), P = this.__zoom, V = C == null ? w(T) : typeof C == "function" ? C.apply(this, arguments) : C, F = P.invert(V), O = typeof I == "function" ? I.apply(this, arguments) : I;
      return n(p(b(P, O), V, F), T, s);
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
      var P = t.apply(this, arguments), V = this.__zoom, F = A == null ? w(P) : typeof A == "function" ? A.apply(this, arguments) : A;
      return n(Fo.translate(F[0], F[1]).scale(V.k).translate(
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
      var T = this, P = arguments, V = k(T, P).event(A), F = t.apply(T, P), O = C == null ? w(F) : typeof C == "function" ? C.apply(T, P) : C, G = Math.max(F[1][0] - F[0][0], F[1][1] - F[0][1]), K = T.__zoom, ne = typeof I == "function" ? I.apply(T, P) : I, ae = u(K.invert(O).concat(G / K.k), ne.invert(O).concat(G / ne.k));
      return function(U) {
        if (U === 1) U = ne;
        else {
          var L = ae(U), Y = G / L[2];
          U = new ot(Y, O[0] - L[0] * Y, O[1] - L[1] * Y);
        }
        V.zoom(null, U);
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
      var I = Le(this.that).datum();
      a.call(
        S,
        this.that,
        new eh(S, {
          sourceEvent: this.sourceEvent,
          target: y,
          transform: this.that.__zoom,
          dispatch: a
        }),
        I
      );
    }
  };
  function D(S, ...I) {
    if (!e.apply(this, arguments)) return;
    var C = k(this, I).event(S), A = this.__zoom, T = Math.max(i[0], Math.min(i[1], A.k * Math.pow(2, o.apply(this, arguments)))), P = Xe(S);
    if (C.wheel)
      (C.mouse[0][0] !== P[0] || C.mouse[0][1] !== P[1]) && (C.mouse[1] = A.invert(C.mouse[0] = P)), clearTimeout(C.wheel);
    else {
      if (A.k === T) return;
      C.mouse = [P, A.invert(P)], vo(this), C.start();
    }
    cn(S), C.wheel = setTimeout(V, m), C.zoom("mouse", n(p(b(A, T), C.mouse[0], C.mouse[1]), C.extent, s));
    function V() {
      C.wheel = null, C.end();
    }
  }
  function $(S, ...I) {
    if (h || !e.apply(this, arguments)) return;
    var C = S.currentTarget, A = k(this, I, !0).event(S), T = Le(S.view).on("mousemove.zoom", O, !0).on("mouseup.zoom", G, !0), P = Xe(S, C), V = S.clientX, F = S.clientY;
    wa(S.view), Cr(S), A.mouse = [P, this.__zoom.invert(P)], vo(this), A.start();
    function O(K) {
      if (cn(K), !A.moved) {
        var ne = K.clientX - V, ae = K.clientY - F;
        A.moved = ne * ne + ae * ae > x;
      }
      A.event(K).zoom("mouse", n(p(A.that.__zoom, A.mouse[0] = Xe(K, C), A.mouse[1]), A.extent, s));
    }
    function G(K) {
      T.on("mousemove.zoom mouseup.zoom", null), va(K.view, A.moved), cn(K), A.event(K).end();
    }
  }
  function W(S, ...I) {
    if (e.apply(this, arguments)) {
      var C = this.__zoom, A = Xe(S.changedTouches ? S.changedTouches[0] : S, this), T = C.invert(A), P = C.k * (S.shiftKey ? 0.5 : 2), V = n(p(b(C, P), A, T), t.apply(this, I), s);
      cn(S), l > 0 ? Le(this).transition().duration(l).call(E, V, A, S) : Le(this).call(y.transform, V, A, S);
    }
  }
  function j(S, ...I) {
    if (e.apply(this, arguments)) {
      var C = S.touches, A = C.length, T = k(this, I, S.changedTouches.length === A).event(S), P, V, F, O;
      for (Cr(S), V = 0; V < A; ++V)
        F = C[V], O = Xe(F, this), O = [O, this.__zoom.invert(O), F.identifier], T.touch0 ? !T.touch1 && T.touch0[2] !== O[2] && (T.touch1 = O, T.taps = 0) : (T.touch0 = O, P = !0, T.taps = 1 + !!d);
      d && (d = clearTimeout(d)), P && (T.taps < 2 && (f = O[0], d = setTimeout(function() {
        d = null;
      }, g)), vo(this), T.start());
    }
  }
  function z(S, ...I) {
    if (this.__zooming) {
      var C = k(this, I).event(S), A = S.changedTouches, T = A.length, P, V, F, O;
      for (cn(S), P = 0; P < T; ++P)
        V = A[P], F = Xe(V, this), C.touch0 && C.touch0[2] === V.identifier ? C.touch0[0] = F : C.touch1 && C.touch1[2] === V.identifier && (C.touch1[0] = F);
      if (V = C.that.__zoom, C.touch1) {
        var G = C.touch0[0], K = C.touch0[1], ne = C.touch1[0], ae = C.touch1[1], U = (U = ne[0] - G[0]) * U + (U = ne[1] - G[1]) * U, L = (L = ae[0] - K[0]) * L + (L = ae[1] - K[1]) * L;
        V = b(V, Math.sqrt(U / L)), F = [(G[0] + ne[0]) / 2, (G[1] + ne[1]) / 2], O = [(K[0] + ae[0]) / 2, (K[1] + ae[1]) / 2];
      } else if (C.touch0) F = C.touch0[0], O = C.touch0[1];
      else return;
      C.zoom("touch", n(p(V, F, O), C.extent, s));
    }
  }
  function H(S, ...I) {
    if (this.__zooming) {
      var C = k(this, I).event(S), A = S.changedTouches, T = A.length, P, V;
      for (Cr(S), h && clearTimeout(h), h = setTimeout(function() {
        h = null;
      }, g), P = 0; P < T; ++P)
        V = A[P], C.touch0 && C.touch0[2] === V.identifier ? delete C.touch0 : C.touch1 && C.touch1[2] === V.identifier && delete C.touch1;
      if (C.touch1 && !C.touch0 && (C.touch0 = C.touch1, delete C.touch1), C.touch0) C.touch0[1] = this.__zoom.invert(C.touch0[0]);
      else if (C.end(), C.taps === 2 && (V = Xe(V, this), Math.hypot(f[0] - V[0], f[1] - V[1]) < v)) {
        var F = Le(this).on("dblclick.zoom");
        F && F.apply(this, arguments);
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
    var S = a.on.apply(a, arguments);
    return S === a ? y : S;
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
}, En = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], La = ["Enter", " ", "Escape"], Ra = {
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
var Lt;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(Lt || (Lt = {}));
var yt;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(yt || (yt = {}));
var kn;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(kn || (kn = {}));
const Ha = {
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
var Mo;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(Mo || (Mo = {}));
var te;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(te || (te = {}));
const Ki = {
  [te.Left]: te.Right,
  [te.Right]: te.Left,
  [te.Top]: te.Bottom,
  [te.Bottom]: te.Top
};
function Va(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Oa = (e) => "id" in e && "source" in e && "target" in e, sh = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), fi = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), Pn = (e, t = [0, 0]) => {
  const { width: n, height: o } = it(e), r = e.origin ?? t, i = n * r[0], s = o * r[1];
  return {
    x: e.position.x - i,
    y: e.position.y - s
  };
}, ah = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const i = typeof r == "string";
    let s = !t.nodeLookup && !i ? r : void 0;
    t.nodeLookup && (s = i ? t.nodeLookup.get(r) : fi(r) ? r : t.nodeLookup.get(r.id));
    const l = s ? Do(s, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return Wo(o, l);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return Xo(n);
}, $n = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = Wo(n, Do(r)), o = !0);
  }), o ? Xo(n) : { x: 0, y: 0, width: 0, height: 0 };
}, hi = (e, t, [n, o, r] = [0, 0, 1], i = !1, s = !1) => {
  const l = {
    ...Yt(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, u = [];
  for (const a of e.values()) {
    const { measured: d, selectable: f = !0, hidden: h = !1 } = a;
    if (s && !f || h)
      continue;
    const g = d.width ?? a.width ?? a.initialWidth ?? null, m = d.height ?? a.height ?? a.initialHeight ?? null, x = Cn(l, Ht(a)), v = (g ?? 0) * (m ?? 0), y = i && x > 0;
    (!a.internals.handleBounds || y || x >= v || a.dragging) && u.push(a);
  }
  return u;
}, ch = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function lh(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function uh({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: i }, s) {
  if (e.size === 0)
    return !0;
  const l = lh(e, s), u = $n(l), a = gi(u, t, n, s?.minZoom ?? r, s?.maxZoom ?? i, s?.padding ?? 0.1);
  return await o.setViewport(a, {
    duration: s?.duration,
    ease: s?.ease,
    interpolate: s?.interpolate
  }), !0;
}
function Ba({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: i }) {
  const s = n.get(e), l = s.parentId ? n.get(s.parentId) : void 0, { x: u, y: a } = l ? l.internals.positionAbsolute : { x: 0, y: 0 }, d = s.origin ?? o;
  let f = s.extent || r;
  if (s.extent === "parent" && !s.expandParent)
    if (!l)
      i?.("005", He.error005());
    else {
      const g = l.measured.width, m = l.measured.height;
      g && m && (f = [
        [u, a],
        [u + g, a + m]
      ]);
    }
  else l && bt(s.extent) && (f = [
    [s.extent[0][0] + u, s.extent[0][1] + a],
    [s.extent[1][0] + u, s.extent[1][1] + a]
  ]);
  const h = bt(f) ? vt(t, f, s.measured) : t;
  return (s.measured.width === void 0 || s.measured.height === void 0) && i?.("015", He.error015()), {
    position: {
      x: h.x - u + (s.measured.width ?? 0) * d[0],
      y: h.y - a + (s.measured.height ?? 0) * d[1]
    },
    positionAbsolute: h
  };
}
async function dh({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const i = new Set(e.map((h) => h.id)), s = [];
  for (const h of n) {
    if (h.deletable === !1)
      continue;
    const g = i.has(h.id), m = !g && h.parentId && s.find((x) => x.id === h.parentId);
    (g || m) && s.push(h);
  }
  const l = new Set(t.map((h) => h.id)), u = o.filter((h) => h.deletable !== !1), d = ch(s, u);
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
const Rt = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), vt = (e = { x: 0, y: 0 }, t, n) => ({
  x: Rt(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: Rt(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Fa(e, t, n) {
  const { width: o, height: r } = it(n), { x: i, y: s } = n.internals.positionAbsolute;
  return vt(e, [
    [i, s],
    [i + o, s + r]
  ], t);
}
const Ui = (e, t, n) => e < t ? Rt(Math.abs(e - t), 1, t) / t : e > n ? -Rt(Math.abs(e - n), 1, t) / t : 0, pi = (e, t, n = 15, o = 40) => {
  const r = Ui(e.x, o, t.width - o) * n, i = Ui(e.y, o, t.height - o) * n;
  return [r, i];
}, Wo = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), Zr = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), Xo = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), Ht = (e, t = [0, 0]) => {
  const { x: n, y: o } = fi(e) ? e.internals.positionAbsolute : Pn(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, Do = (e, t = [0, 0]) => {
  const { x: n, y: o } = fi(e) ? e.internals.positionAbsolute : Pn(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, Wa = (e, t) => Xo(Wo(Zr(e), Zr(t))), Cn = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, Gi = (e) => qe(e.width) && qe(e.height) && qe(e.x) && qe(e.y), qe = (e) => !isNaN(e) && isFinite(e), Xa = (e, t) => (n, o) => {
}, Tn = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), Yt = ({ x: e, y: t }, [n, o, r], i = !1, s = [1, 1]) => {
  const l = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return i ? Tn(l, s) : l;
}, Vt = ({ x: e, y: t }, [n, o, r]) => ({
  x: e * r + n,
  y: t * r + o
});
function jt(e, t) {
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
function fh(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = jt(e, n), r = jt(e, t);
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
    const o = jt(e.top ?? e.y ?? 0, n), r = jt(e.bottom ?? e.y ?? 0, n), i = jt(e.left ?? e.x ?? 0, t), s = jt(e.right ?? e.x ?? 0, t);
    return { top: o, right: s, bottom: r, left: i, x: i + s, y: o + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function hh(e, t, n, o, r, i) {
  const { x: s, y: l } = Vt(e, [t, n, o]), { x: u, y: a } = Vt({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), d = r - u, f = i - a;
  return {
    left: Math.floor(s),
    top: Math.floor(l),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const gi = (e, t, n, o, r, i) => {
  const s = fh(i, t, n), l = (t - s.x) / e.width, u = (n - s.y) / e.height, a = Math.min(l, u), d = Rt(a, o, r), f = e.x + e.width / 2, h = e.y + e.height / 2, g = t / 2 - f * d, m = n / 2 - h * d, x = hh(e, g, m, d, t, n), v = {
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
}, _n = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function bt(e) {
  return e != null && e !== "parent";
}
function it(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function Ya(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function qa(e, t = { width: 0, height: 0 }, n, o, r) {
  const i = { ...e }, s = o.get(n);
  if (s) {
    const l = s.origin || r;
    i.x += s.internals.positionAbsolute.x - (t.width ?? 0) * l[0], i.y += s.internals.positionAbsolute.y - (t.height ?? 0) * l[1];
  }
  return i;
}
function Qi(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function ph() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function gh(e) {
  return { ...Ra, ...e || {} };
}
function wn(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: i, y: s } = Ze(e), l = Yt({ x: i - (r?.left ?? 0), y: s - (r?.top ?? 0) }, o), { x: u, y: a } = n ? Tn(l, t) : l;
  return {
    xSnapped: u,
    ySnapped: a,
    ...l
  };
}
const mi = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), Za = (e) => e?.getRootNode?.() || window?.document, mh = ["INPUT", "SELECT", "TEXTAREA"];
function Ka(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : mh.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const Ua = (e) => "clientX" in e, Ze = (e, t) => {
  const n = Ua(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, Ji = (e, t, n, o, r) => {
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
      ...mi(s)
    };
  });
};
function Ga({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: i, targetControlX: s, targetControlY: l }) {
  const u = e * 0.125 + r * 0.375 + s * 0.375 + n * 0.125, a = t * 0.125 + i * 0.375 + l * 0.375 + o * 0.125, d = Math.abs(u - e), f = Math.abs(a - t);
  return [u, a, d, f];
}
function lo(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function es({ pos: e, x1: t, y1: n, x2: o, y2: r, c: i }) {
  switch (e) {
    case te.Left:
      return [t - lo(t - o, i), n];
    case te.Right:
      return [t + lo(o - t, i), n];
    case te.Top:
      return [t, n - lo(n - r, i)];
    case te.Bottom:
      return [t, n + lo(r - n, i)];
  }
}
function Qa({ sourceX: e, sourceY: t, sourcePosition: n = te.Bottom, targetX: o, targetY: r, targetPosition: i = te.Top, curvature: s = 0.25 }) {
  const [l, u] = es({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: s
  }), [a, d] = es({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: s
  }), [f, h, g, m] = Ga({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: r,
    sourceControlX: l,
    sourceControlY: u,
    targetControlX: a,
    targetControlY: d
  });
  return [
    `M${e},${t} C${l},${u} ${a},${d} ${o},${r}`,
    f,
    h,
    g,
    m
  ];
}
function Ja({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, i = n < e ? n + r : n - r, s = Math.abs(o - t) / 2, l = o < t ? o + s : o - s;
  return [i, l, r, s];
}
function yh({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: i = "basic" }) {
  if (i === "manual")
    return o;
  const s = r && n ? o + 1e3 : o, l = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return s + l;
}
function xh({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const i = Wo(Do(e), Do(t));
  i.x === i.x2 && (i.x2 += 1), i.y === i.y2 && (i.y2 += 1);
  const s = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return Cn(s, Xo(i)) > 0;
}
const ec = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, wh = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), vh = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", He.error006()), t;
  const o = n.getEdgeId || ec;
  let r;
  return Oa(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, wh(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, bh = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: r, ...i } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", He.error006()), n;
  if (!n.find((a) => a.id === e.id))
    return o.onError?.("007", He.error007(r)), n;
  const l = o.getEdgeId || ec, u = {
    ...i,
    id: o.shouldReplaceId ? l(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((a) => a.id !== r).concat(u);
};
function tc({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, i, s, l] = Ja({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, i, s, l];
}
const ts = {
  [te.Left]: { x: -1, y: 0 },
  [te.Right]: { x: 1, y: 0 },
  [te.Top]: { x: 0, y: -1 },
  [te.Bottom]: { x: 0, y: 1 }
}, Sh = ({ source: e, sourcePosition: t = te.Bottom, target: n }) => t === te.Left || t === te.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, ns = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Nh({ source: e, sourcePosition: t = te.Bottom, target: n, targetPosition: o = te.Top, center: r, offset: i, stepPosition: s }) {
  const l = ts[t], u = ts[o], a = { x: e.x + l.x * i, y: e.y + l.y * i }, d = { x: n.x + u.x * i, y: n.y + u.y * i }, f = Sh({
    source: a,
    sourcePosition: t,
    target: d
  }), h = f.x !== 0 ? "x" : "y", g = f[h];
  let m = [], x, v;
  const y = { x: 0, y: 0 }, b = { x: 0, y: 0 }, [, , p, w] = Ja({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (l[h] * u[h] === -1) {
    h === "x" ? (x = r.x ?? a.x + (d.x - a.x) * s, v = r.y ?? (a.y + d.y) / 2) : (x = r.x ?? (a.x + d.x) / 2, v = r.y ?? a.y + (d.y - a.y) * s);
    const D = [
      { x, y: a.y },
      { x, y: d.y }
    ], $ = [
      { x: a.x, y: v },
      { x: d.x, y: v }
    ];
    l[h] === g ? m = h === "x" ? D : $ : m = h === "x" ? $ : D;
  } else {
    const D = [{ x: a.x, y: d.y }], $ = [{ x: d.x, y: a.y }];
    if (h === "x" ? m = l.x === g ? $ : D : m = l.y === g ? D : $, t === o) {
      const S = Math.abs(e[h] - n[h]);
      if (S <= i) {
        const I = Math.min(i - 1, i - S);
        l[h] === g ? y[h] = (a[h] > e[h] ? -1 : 1) * I : b[h] = (d[h] > n[h] ? -1 : 1) * I;
      }
    }
    if (t !== o) {
      const S = h === "x" ? "y" : "x", I = l[h] === u[S], C = a[S] > d[S], A = a[S] < d[S];
      (l[h] === 1 && (!I && C || I && A) || l[h] !== 1 && (!I && A || I && C)) && (m = h === "x" ? D : $);
    }
    const W = { x: a.x + y.x, y: a.y + y.y }, j = { x: d.x + b.x, y: d.y + b.y }, z = Math.max(Math.abs(W.x - m[0].x), Math.abs(j.x - m[0].x)), H = Math.max(Math.abs(W.y - m[0].y), Math.abs(j.y - m[0].y));
    z >= H ? (x = (W.x + j.x) / 2, v = m[0].y) : (x = m[0].x, v = (W.y + j.y) / 2);
  }
  const E = { x: a.x + y.x, y: a.y + y.y }, k = { x: d.x + b.x, y: d.y + b.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...E.x !== m[0].x || E.y !== m[0].y ? [E] : [],
    ...m,
    ...k.x !== m[m.length - 1].x || k.y !== m[m.length - 1].y ? [k] : [],
    n
  ], x, v, p, w];
}
function Eh(e, t, n, o) {
  const r = Math.min(ns(e, t) / 2, ns(t, n) / 2, o), { x: i, y: s } = t;
  if (e.x === i && i === n.x || e.y === s && s === n.y)
    return `L${i} ${s}`;
  if (e.y === s) {
    const a = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${i + r * a},${s}Q ${i},${s} ${i},${s + r * d}`;
  }
  const l = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${i},${s + r * u}Q ${i},${s} ${i + r * l},${s}`;
}
function Po({ sourceX: e, sourceY: t, sourcePosition: n = te.Bottom, targetX: o, targetY: r, targetPosition: i = te.Top, borderRadius: s = 5, centerX: l, centerY: u, offset: a = 20, stepPosition: d = 0.5 }) {
  const [f, h, g, m, x] = Nh({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: i,
    center: { x: l, y: u },
    offset: a,
    stepPosition: d
  });
  let v = `M${f[0].x} ${f[0].y}`;
  for (let y = 1; y < f.length - 1; y++)
    v += Eh(f[y - 1], f[y], f[y + 1], s);
  return v += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [v, h, g, m, x];
}
function os(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function kh(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!os(t) || !os(n))
    return null;
  const o = t.internals.handleBounds || rs(t.handles), r = n.internals.handleBounds || rs(n.handles), i = is(o?.source ?? [], e.sourceHandle), s = is(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === Lt.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!i || !s)
    return e.onError?.("008", He.error008(i ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const l = i?.position || te.Bottom, u = s?.position || te.Top, a = St(t, i, l), d = St(n, s, u);
  return {
    sourceX: a.x,
    sourceY: a.y,
    targetX: d.x,
    targetY: d.y,
    sourcePosition: l,
    targetPosition: u
  };
}
function rs(e) {
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
function St(e, t, n = te.Left, o = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, i = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: s, height: l } = t ?? it(e);
  if (o)
    return { x: r + s / 2, y: i + l / 2 };
  switch (t?.position ?? n) {
    case te.Top:
      return { x: r + s / 2, y: i };
    case te.Right:
      return { x: r + s, y: i + l / 2 };
    case te.Bottom:
      return { x: r + s / 2, y: i + l };
    case te.Left:
      return { x: r, y: i + l / 2 };
  }
}
function is(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Kr(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function Ch(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const i = /* @__PURE__ */ new Set();
  return e.reduce((s, l) => ([l.markerStart || o, l.markerEnd || r].forEach((u) => {
    if (u && typeof u == "object") {
      const a = Kr(u, t);
      i.has(a) || (s.push({ id: a, color: u.color || n, ...u }), i.add(a));
    }
  }), s), []).sort((s, l) => s.id.localeCompare(l.id));
}
const nc = 1e3, _h = 10, yi = {
  nodeOrigin: [0, 0],
  nodeExtent: En,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Ih = {
  ...yi,
  checkEquality: !0
};
function xi(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function jh(e, t, n) {
  const o = xi(yi, n);
  for (const r of e.values())
    if (r.parentId)
      vi(r, e, t, o);
    else {
      const i = Pn(r, o.nodeOrigin), s = bt(r.extent) ? r.extent : o.nodeExtent, l = vt(i, s, it(r));
      r.internals.positionAbsolute = l;
    }
}
function Ah(e, t) {
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
function wi(e) {
  return e === "manual";
}
function Ur(e, t, n, o = {}) {
  const r = xi(Ih, o), i = { i: 0 }, s = new Map(t), l = r?.elevateNodesOnSelect && !wi(r.zIndexMode) ? nc : 0;
  let u = e.length > 0, a = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = s.get(d.id);
    if (r.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const h = Pn(d, r.nodeOrigin), g = bt(d.extent) ? d.extent : r.nodeExtent, m = vt(h, g, it(d));
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
          handleBounds: Ah(d, f),
          z: oc(d, l, r.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (u = !1), d.parentId && vi(f, t, n, o, i), a ||= d.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: a };
}
function Mh(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function vi(e, t, n, o, r) {
  const { elevateNodesOnSelect: i, nodeOrigin: s, nodeExtent: l, zIndexMode: u } = xi(yi, o), a = e.parentId, d = t.get(a);
  if (!d) {
    console.warn(`Parent node ${a} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Mh(e, n), r && !d.parentId && d.internals.rootParentIndex === void 0 && u === "auto" && (d.internals.rootParentIndex = ++r.i, d.internals.z = d.internals.z + r.i * _h), r && d.internals.rootParentIndex !== void 0 && (r.i = d.internals.rootParentIndex);
  const f = i && !wi(u) ? nc : 0, { x: h, y: g, z: m } = Dh(e, d, s, l, f, u), { positionAbsolute: x } = e.internals, v = h !== x.x || g !== x.y;
  (v || m !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: v ? { x: h, y: g } : x,
      z: m
    }
  });
}
function oc(e, t, n) {
  const o = qe(e.zIndex) ? e.zIndex : 0;
  return wi(n) ? o : o + (e.selected ? t : 0);
}
function Dh(e, t, n, o, r, i) {
  const { x: s, y: l } = t.internals.positionAbsolute, u = it(e), a = Pn(e, n), d = bt(e.extent) ? vt(a, e.extent, u) : a;
  let f = vt({ x: s + d.x, y: l + d.y }, o, u);
  e.extent === "parent" && (f = Fa(f, u, t));
  const h = oc(e, r, i), g = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: g >= h ? g + 1 : h
  };
}
function bi(e, t, n, o = [0, 0]) {
  const r = [], i = /* @__PURE__ */ new Map();
  for (const s of e) {
    const l = t.get(s.parentId);
    if (!l)
      continue;
    const u = i.get(s.parentId)?.expandedRect ?? Ht(l), a = Wa(u, s.rect);
    i.set(s.parentId, { expandedRect: a, parent: l });
  }
  return i.size > 0 && i.forEach(({ expandedRect: s, parent: l }, u) => {
    const a = l.internals.positionAbsolute, d = it(l), f = l.origin ?? o, h = s.x < a.x ? Math.round(Math.abs(a.x - s.x)) : 0, g = s.y < a.y ? Math.round(Math.abs(a.y - s.y)) : 0, m = Math.max(d.width, Math.round(s.width)), x = Math.max(d.height, Math.round(s.height)), v = (m - d.width) * f[0], y = (x - d.height) * f[1];
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
function Ph(e, t, n, o, r, i, s) {
  const l = o?.querySelector(".xyflow__viewport");
  let u = !1;
  if (!l)
    return { changes: [], updatedInternals: u };
  const a = [], d = window.getComputedStyle(l), { m22: f } = new window.DOMMatrixReadOnly(d.transform), h = [];
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
    const x = mi(g.nodeElement), v = m.measured.width !== x.width || m.measured.height !== x.height;
    if (!!(x.width && x.height && (v || !m.internals.handleBounds || g.force))) {
      const b = g.nodeElement.getBoundingClientRect(), p = bt(m.extent) ? m.extent : i;
      let { positionAbsolute: w } = m.internals;
      m.parentId && m.extent === "parent" ? w = Fa(w, x, t.get(m.parentId)) : p && (w = vt(w, p, x));
      const E = {
        ...m,
        measured: x,
        internals: {
          ...m.internals,
          positionAbsolute: w,
          handleBounds: {
            source: Ji("source", g.nodeElement, b, f, m.id),
            target: Ji("target", g.nodeElement, b, f, m.id)
          }
        }
      };
      t.set(m.id, E), m.parentId && vi(E, t, n, { nodeOrigin: r, zIndexMode: s }), u = !0, v && (a.push({
        id: m.id,
        type: "dimensions",
        dimensions: x
      }), m.expandParent && m.parentId && h.push({
        id: m.id,
        parentId: m.parentId,
        rect: Ht(E, r)
      }));
    }
  }
  if (h.length > 0) {
    const g = bi(h, t, n, r);
    a.push(...g);
  }
  return { changes: a, updatedInternals: u };
}
async function $h({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: i }) {
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
function ss(e, t, n, o, r, i) {
  let s = r;
  const l = o.get(s) || /* @__PURE__ */ new Map();
  o.set(s, l.set(n, t)), s = `${r}-${e}`;
  const u = o.get(s) || /* @__PURE__ */ new Map();
  if (o.set(s, u.set(n, t)), i) {
    s = `${r}-${e}-${i}`;
    const a = o.get(s) || /* @__PURE__ */ new Map();
    o.set(s, a.set(n, t));
  }
}
function rc(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: i, sourceHandle: s = null, targetHandle: l = null } = o, u = { edgeId: o.id, source: r, target: i, sourceHandle: s, targetHandle: l }, a = `${r}-${s}--${i}-${l}`, d = `${i}-${l}--${r}-${s}`;
    ss("source", u, d, e, r, s), ss("target", u, a, e, i, l), t.set(o.id, o);
  }
}
function ic(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : ic(n, t) : !1;
}
function as(e, t, n) {
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
function Th(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [i, s] of e)
    if ((s.selected || s.id === o) && (!s.parentId || !ic(s, e)) && (s.draggable || t && typeof s.draggable > "u")) {
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
function _r({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
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
function zh({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const i = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, s = Tn(i, t);
  return {
    x: s.x - i.x,
    y: s.y - i.y
  };
}
function Lh({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let i = { x: null, y: null }, s = 0, l = /* @__PURE__ */ new Map(), u = !1, a = { x: 0, y: 0 }, d = null, f = !1, h = null, g = !1, m = !1, x = null;
  function v({ noDragClassName: b, handleSelector: p, domNode: w, isSelectable: E, nodeId: k, nodeClickDistance: _ = 0 }) {
    h = Le(w);
    function D({ x: z, y: H }) {
      const { nodeLookup: S, nodeExtent: I, snapGrid: C, snapToGrid: A, nodeOrigin: T, onNodeDrag: P, onSelectionDrag: V, onError: F, updateNodePositions: O } = t();
      i = { x: z, y: H };
      let G = !1;
      const K = l.size > 1, ne = K && I ? Zr($n(l)) : null, ae = K && A ? zh({
        dragItems: l,
        snapGrid: C,
        x: z,
        y: H
      }) : null;
      for (const [U, L] of l) {
        if (!S.has(U))
          continue;
        let Y = { x: z - L.distance.x, y: H - L.distance.y };
        A && (Y = ae ? {
          x: Math.round(Y.x + ae.x),
          y: Math.round(Y.y + ae.y)
        } : Tn(Y, C));
        let re = null;
        if (K && I && !L.extent && ne) {
          const { positionAbsolute: ee } = L.internals, oe = ee.x - ne.x + I[0][0], R = ee.x + L.measured.width - ne.x2 + I[1][0], J = ee.y - ne.y + I[0][1], fe = ee.y + L.measured.height - ne.y2 + I[1][1];
          re = [
            [oe, J],
            [R, fe]
          ];
        }
        const { position: ie, positionAbsolute: Z } = Ba({
          nodeId: U,
          nextPosition: Y,
          nodeLookup: S,
          nodeExtent: re || I,
          nodeOrigin: T,
          onError: F
        });
        G = G || L.position.x !== ie.x || L.position.y !== ie.y, L.position = ie, L.internals.positionAbsolute = Z;
      }
      if (m = m || G, !!G && (O(l, !0), x && (o || P || !k && V))) {
        const [U, L] = _r({
          nodeId: k,
          dragItems: l,
          nodeLookup: S
        });
        o?.(x, l, U, L), P?.(x, U, L), k || V?.(x, L);
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
      const [C, A] = pi(a, d, S);
      (C !== 0 || A !== 0) && (i.x = (i.x ?? 0) - C / z[2], i.y = (i.y ?? 0) - A / z[2], await H({ x: C, y: A }) && D(i)), s = requestAnimationFrame($);
    }
    function W(z) {
      const { nodeLookup: H, multiSelectionActive: S, nodesDraggable: I, transform: C, snapGrid: A, snapToGrid: T, selectNodesOnDrag: P, onNodeDragStart: V, onSelectionDragStart: F, unselectNodesAndEdges: O } = t();
      f = !0, (!P || !E) && !S && k && (H.get(k)?.selected || O()), E && P && k && e?.(k);
      const G = wn(z.sourceEvent, { transform: C, snapGrid: A, snapToGrid: T, containerBounds: d });
      if (i = G, l = Th(H, I, G, k), l.size > 0 && (n || V || !k && F)) {
        const [K, ne] = _r({
          nodeId: k,
          dragItems: l,
          nodeLookup: H
        });
        n?.(z.sourceEvent, l, K, ne), V?.(z.sourceEvent, K, ne), k || F?.(z.sourceEvent, ne);
      }
    }
    const j = ba().clickDistance(_).on("start", (z) => {
      const { domNode: H, nodeDragThreshold: S, transform: I, snapGrid: C, snapToGrid: A } = t();
      d = H?.getBoundingClientRect() || null, g = !1, m = !1, x = z.sourceEvent, S === 0 && W(z), i = wn(z.sourceEvent, { transform: I, snapGrid: C, snapToGrid: A, containerBounds: d }), a = Ze(z.sourceEvent, d);
    }).on("drag", (z) => {
      const { autoPanOnNodeDrag: H, transform: S, snapGrid: I, snapToGrid: C, nodeDragThreshold: A, nodeLookup: T } = t(), P = wn(z.sourceEvent, { transform: S, snapGrid: I, snapToGrid: C, containerBounds: d });
      if (x = z.sourceEvent, (z.sourceEvent.type === "touchmove" && z.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      k && !T.has(k)) && (g = !0), !g) {
        if (!u && H && f && (u = !0, $()), !f) {
          const V = Ze(z.sourceEvent, d), F = V.x - a.x, O = V.y - a.y;
          Math.sqrt(F * F + O * O) > A && W(z);
        }
        (i.x !== P.xSnapped || i.y !== P.ySnapped) && l && f && (a = Ze(z.sourceEvent, d), D(P));
      }
    }).on("end", (z) => {
      if (!f || g) {
        g && l.size > 0 && t().updateNodePositions(l, !1);
        return;
      }
      if (u = !1, f = !1, cancelAnimationFrame(s), l.size > 0) {
        const { nodeLookup: H, updateNodePositions: S, onNodeDragStop: I, onSelectionDragStop: C } = t();
        if (m && (S(l, !1), m = !1), r || I || !k && C) {
          const [A, T] = _r({
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
      return !z.button && (!b || !as(H, `.${b}`, w)) && (!p || as(H, p, w));
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
function Rh(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const i of t.values())
    Cn(r, Ht(i)) > 0 && o.push(i);
  return o;
}
const Hh = 250;
function Vh(e, t, n, o) {
  let r = [], i = 1 / 0;
  const s = Rh(e, n, t + Hh);
  for (const l of s) {
    const u = [...l.internals.handleBounds?.source ?? [], ...l.internals.handleBounds?.target ?? []];
    for (const a of u) {
      if (o.nodeId === a.nodeId && o.type === a.type && o.id === a.id)
        continue;
      const { x: d, y: f } = St(l, a, a.position, !0), h = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(f - e.y, 2));
      h > t || (h < i ? (r = [{ ...a, x: d, y: f }], i = h) : h === i && r.push({ ...a, x: d, y: f }));
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
function sc(e, t, n, o, r, i = !1) {
  const s = o.get(e);
  if (!s)
    return null;
  const l = r === "strict" ? s.internals.handleBounds?.[t] : [...s.internals.handleBounds?.source ?? [], ...s.internals.handleBounds?.target ?? []], u = (n ? l?.find((a) => a.id === n) : l?.[0]) ?? null;
  return u && i ? { ...u, ...St(s, u, u.position, !0) } : u;
}
function ac(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Oh(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const cc = () => !0;
function Bh(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: i, isTarget: s, domNode: l, nodeLookup: u, lib: a, autoPanOnConnect: d, flowId: f, panBy: h, cancelConnection: g, onConnectStart: m, onConnect: x, onConnectEnd: v, isValidConnection: y = cc, onReconnectEnd: b, updateConnection: p, getTransform: w, getFromHandle: E, autoPanSpeed: k, dragThreshold: _ = 1, handleDomNode: D }) {
  const $ = Za(e.target);
  let W = 0, j;
  const { x: z, y: H } = Ze(e), S = ac(i, D), I = l?.getBoundingClientRect();
  let C = !1;
  if (!I || !S)
    return;
  const A = sc(r, S, o, u, t);
  if (!A)
    return;
  let T = Ze(e, I), P = !1, V = null, F = !1, O = null;
  function G() {
    if (!d || !I)
      return;
    const [ie, Z] = pi(T, I, k);
    h({ x: ie, y: Z }), W = requestAnimationFrame(G);
  }
  const K = {
    ...A,
    nodeId: r,
    type: S,
    position: A.position
  }, ne = u.get(r);
  let U = {
    inProgress: !0,
    isValid: null,
    from: St(ne, K, te.Left, !0),
    fromHandle: K,
    fromPosition: K.position,
    fromNode: ne,
    to: T,
    toHandle: null,
    toPosition: Ki[K.position],
    toNode: null,
    pointer: T
  };
  function L() {
    C = !0, p(U), m?.(e, { nodeId: r, handleId: o, handleType: S });
  }
  _ === 0 && L();
  function Y(ie) {
    if (!C) {
      const { x: fe, y: we } = Ze(ie), Oe = fe - z, ve = we - H;
      if (!(Oe * Oe + ve * ve > _ * _))
        return;
      L();
    }
    if (!E() || !K) {
      re(ie);
      return;
    }
    const Z = w();
    T = Ze(ie, I), j = Vh(Yt(T, Z, !1, [1, 1]), n, u, K), P || (G(), P = !0);
    const ee = lc(ie, {
      handle: j,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: s ? "target" : "source",
      isValidConnection: y,
      doc: $,
      lib: a,
      flowId: f,
      nodeLookup: u
    });
    O = ee.handleDomNode, V = ee.connection, F = Oh(!!j, ee.isValid);
    const oe = u.get(r), R = oe ? St(oe, K, te.Left, !0) : U.from, J = {
      ...U,
      from: R,
      isValid: F,
      to: ee.toHandle && F ? Vt({ x: ee.toHandle.x, y: ee.toHandle.y }, Z) : T,
      toHandle: ee.toHandle,
      toPosition: F && ee.toHandle ? ee.toHandle.position : Ki[K.position],
      toNode: ee.toHandle ? u.get(ee.toHandle.nodeId) : null,
      pointer: T
    };
    p(J), U = J;
  }
  function re(ie) {
    if (!("touches" in ie && ie.touches.length > 0)) {
      if (C) {
        (j || O) && V && F && x?.(V);
        const { inProgress: Z, ...ee } = U, oe = {
          ...ee,
          toPosition: U.toHandle ? U.toPosition : null
        };
        v?.(ie, oe), i && b?.(ie, oe);
      }
      g(), cancelAnimationFrame(W), P = !1, F = !1, V = null, O = null, $.removeEventListener("mousemove", Y), $.removeEventListener("mouseup", re), $.removeEventListener("touchmove", Y), $.removeEventListener("touchend", re);
    }
  }
  $.addEventListener("mousemove", Y), $.addEventListener("mouseup", re), $.addEventListener("touchmove", Y), $.addEventListener("touchend", re);
}
function lc(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: i, doc: s, lib: l, flowId: u, isValidConnection: a = cc, nodeLookup: d }) {
  const f = i === "target", h = t ? s.querySelector(`.${l}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: g, y: m } = Ze(e), x = s.elementFromPoint(g, m), v = x?.classList.contains(`${l}-flow__handle`) ? x : h, y = {
    handleDomNode: v,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (v) {
    const b = ac(void 0, v), p = v.getAttribute("data-nodeid"), w = v.getAttribute("data-handleid"), E = v.classList.contains("connectable"), k = v.classList.contains("connectableend");
    if (!p || !b)
      return y;
    const _ = {
      source: f ? p : o,
      sourceHandle: f ? w : r,
      target: f ? o : p,
      targetHandle: f ? r : w
    };
    y.connection = _;
    const $ = E && k && (n === Lt.Strict ? f && b === "source" || !f && b === "target" : p !== o || w !== r);
    y.isValid = $ && a(_), y.toHandle = sc(p, b, w, d, n, !0);
  }
  return y;
}
const Gr = {
  onPointerDown: Bh,
  isValid: lc
};
function Fh({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = Le(e);
  function i({ translateExtent: l, width: u, height: a, zoomStep: d = 1, pannable: f = !0, zoomable: h = !0, inversePan: g = !1 }) {
    const m = (p) => {
      if (p.sourceEvent.type !== "wheel" || !t)
        return;
      const w = n(), E = p.sourceEvent.ctrlKey && _n() ? 10 : 1, k = -p.sourceEvent.deltaY * (p.sourceEvent.deltaMode === 1 ? 0.05 : p.sourceEvent.deltaMode ? 1 : 2e-3) * d, _ = w[2] * Math.pow(2, k * E);
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
        [u, a]
      ];
      t.setViewportConstrained({
        x: D.x,
        y: D.y,
        zoom: w[2]
      }, $, l);
    }, b = za().on("start", v).on("zoom", f ? y : null).on("zoom.wheel", h ? m : null);
    r.call(b, {});
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
const Yo = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), Ir = ({ x: e, y: t, zoom: n }) => Fo.translate(e, t).scale(n), Mt = (e, t) => e.target.closest(`.${t}`), uc = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Wh = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, jr = (e, t = 0, n = Wh, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, dc = (e) => {
  const t = e.ctrlKey && _n() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Xh({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: i, zoomOnPinch: s, onPanZoomStart: l, onPanZoom: u, onPanZoomEnd: a }) {
  return (d) => {
    if (Mt(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && s) {
      const v = Xe(d), y = dc(d), b = f * Math.pow(2, y);
      o.scaleTo(n, b, v, d);
      return;
    }
    const h = d.deltaMode === 1 ? 20 : 1;
    let g = r === yt.Vertical ? 0 : d.deltaX * h, m = r === yt.Horizontal ? 0 : d.deltaY * h;
    !_n() && d.shiftKey && r !== yt.Vertical && (g = d.deltaY * h, m = 0), o.translateBy(
      n,
      -(g / f) * i,
      -(m / f) * i,
      // @ts-ignore
      { internal: !0 }
    );
    const x = Yo(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(d, x), e.panScrollTimeout = setTimeout(() => {
      a?.(d, x), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, l?.(d, x));
  };
}
function Yh({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const i = o.type === "wheel", s = !t && i && !o.ctrlKey, l = Mt(o, e);
    if (o.ctrlKey && i && l && o.preventDefault(), s || l)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function qh({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = Yo(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function Zh({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (i) => {
    e.usedRightMouseButton = !!(n && uc(t, e.mouseButton ?? 0)), i.sourceEvent?.sync || o([i.transform.x, i.transform.y, i.transform.k]), r && !i.sourceEvent?.internal && r?.(i.sourceEvent, Yo(i.transform));
  };
}
function Kh({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: i }) {
  return (s) => {
    if (!s.sourceEvent?.internal && (e.isZoomingOrPanning = !1, i && uc(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && s.sourceEvent && i(s.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
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
function Uh({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: i, userSelectionActive: s, noWheelClassName: l, noPanClassName: u, lib: a, connectionInProgress: d }) {
  return (f) => {
    const h = e || t, g = n && f.ctrlKey, m = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (Mt(f, `${a}-flow__node`) || Mt(f, `${a}-flow__edge`)))
      return !0;
    if (!o && !h && !r && !i && !n || s || d && !m || Mt(f, l) && m || Mt(f, u) && (!m || r && m && !e) || !n && f.ctrlKey && m)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!h && !r && !g && m || !o && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(o) && !o.includes(f.button) && f.type === "mousedown")
      return !1;
    const x = Array.isArray(o) && o.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || m) && x;
  };
}
function Gh({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: i, onPanZoomStart: s, onPanZoomEnd: l, onDraggingChange: u }) {
  const a = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = za().scaleExtent([t, n]).translateExtent(o), h = Le(e).call(f);
  b({
    x: r.x,
    y: r.y,
    zoom: Rt(r.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], o);
  const g = h.on("wheel.zoom"), m = h.on("dblclick.zoom");
  f.wheelDelta(dc);
  async function x(j, z) {
    return h ? new Promise((H) => {
      f?.interpolate(z?.interpolate === "linear" ? xn : yo).transform(jr(h, z?.duration, z?.ease, () => H(!0)), j);
    }) : !1;
  }
  function v({ noWheelClassName: j, noPanClassName: z, onPaneContextMenu: H, userSelectionActive: S, panOnScroll: I, panOnDrag: C, panOnScrollMode: A, panOnScrollSpeed: T, preventScrolling: P, zoomOnPinch: V, zoomOnScroll: F, zoomOnDoubleClick: O, zoomActivationKeyPressed: G, lib: K, onTransformChange: ne, connectionInProgress: ae, paneClickDistance: U, selectionOnDrag: L }) {
    S && !a.isZoomingOrPanning && y();
    const Y = I && !G && !S;
    f.clickDistance(L ? 1 / 0 : !qe(U) || U < 0 ? 0 : U);
    const re = Y ? Xh({
      zoomPanValues: a,
      noWheelClassName: j,
      d3Selection: h,
      d3Zoom: f,
      panOnScrollMode: A,
      panOnScrollSpeed: T,
      zoomOnPinch: V,
      onPanZoomStart: s,
      onPanZoom: i,
      onPanZoomEnd: l
    }) : Yh({
      noWheelClassName: j,
      preventScrolling: P,
      d3ZoomHandler: g
    });
    h.on("wheel.zoom", re, { passive: !1 });
    const ie = qh({
      zoomPanValues: a,
      onDraggingChange: u,
      onPanZoomStart: s
    });
    f.on("start", ie);
    const Z = Zh({
      zoomPanValues: a,
      panOnDrag: C,
      onPaneContextMenu: !!H,
      onPanZoom: i,
      onTransformChange: ne
    });
    f.on("zoom", Z);
    const ee = Kh({
      zoomPanValues: a,
      panOnDrag: C,
      panOnScroll: I,
      onPaneContextMenu: H,
      onPanZoomEnd: l,
      onDraggingChange: u
    });
    f.on("end", ee);
    const oe = Uh({
      zoomActivationKeyPressed: G,
      panOnDrag: C,
      zoomOnScroll: F,
      panOnScroll: I,
      zoomOnDoubleClick: O,
      zoomOnPinch: V,
      userSelectionActive: S,
      noPanClassName: z,
      noWheelClassName: j,
      lib: K,
      connectionInProgress: ae
    });
    f.filter(oe), O ? h.on("dblclick.zoom", m) : h.on("dblclick.zoom", null);
  }
  function y() {
    f.on("zoom", null);
  }
  async function b(j, z, H) {
    const S = Ir(j), I = f?.constrain()(S, z, H);
    return I && await x(I), I;
  }
  async function p(j, z) {
    const H = Ir(j);
    return await x(H, z), H;
  }
  function w(j) {
    if (h) {
      const z = Ir(j), H = h.property("__zoom");
      (H.k !== j.zoom || H.x !== j.x || H.y !== j.y) && f?.transform(h, z, null, { sync: !0 });
    }
  }
  function E() {
    const j = h ? Ta(h.node()) : { x: 0, y: 0, k: 1 };
    return { x: j.x, y: j.y, zoom: j.k };
  }
  async function k(j, z) {
    return h ? new Promise((H) => {
      f?.interpolate(z?.interpolate === "linear" ? xn : yo).scaleTo(jr(h, z?.duration, z?.ease, () => H(!0)), j);
    }) : !1;
  }
  async function _(j, z) {
    return h ? new Promise((H) => {
      f?.interpolate(z?.interpolate === "linear" ? xn : yo).scaleBy(jr(h, z?.duration, z?.ease, () => H(!0)), j);
    }) : !1;
  }
  function D(j) {
    f?.scaleExtent(j);
  }
  function $(j) {
    f?.translateExtent(j);
  }
  function W(j) {
    const z = !qe(j) || j < 0 ? 0 : j;
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
    setClickDistance: W
  };
}
var Ot;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(Ot || (Ot = {}));
function Qh({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: i }) {
  const s = e - t, l = n - o, u = [s > 0 ? 1 : s < 0 ? -1 : 0, l > 0 ? 1 : l < 0 ? -1 : 0];
  return s && r && (u[0] = u[0] * -1), l && i && (u[1] = u[1] * -1), u;
}
function cs(e) {
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
function uo(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function ls(e, t) {
  return e ? !t : t;
}
function Jh(e, t, n, o, r, i, s, l) {
  let { affectsX: u, affectsY: a } = t;
  const { isHorizontal: d, isVertical: f } = t, h = d && f, { xSnapped: g, ySnapped: m } = n, { minWidth: x, maxWidth: v, minHeight: y, maxHeight: b } = o, { x: p, y: w, width: E, height: k, aspectRatio: _ } = e;
  let D = Math.floor(d ? g - e.pointerX : 0), $ = Math.floor(f ? m - e.pointerY : 0);
  const W = E + (u ? -D : D), j = k + (a ? -$ : $), z = -i[0] * E, H = -i[1] * k;
  let S = uo(W, x, v), I = uo(j, y, b);
  if (s) {
    let T = 0, P = 0;
    u && D < 0 ? T = st(p + D + z, s[0][0]) : !u && D > 0 && (T = at(p + W + z, s[1][0])), a && $ < 0 ? P = st(w + $ + H, s[0][1]) : !a && $ > 0 && (P = at(w + j + H, s[1][1])), S = Math.max(S, T), I = Math.max(I, P);
  }
  if (l) {
    let T = 0, P = 0;
    u && D > 0 ? T = at(p + D, l[0][0]) : !u && D < 0 && (T = st(p + W, l[1][0])), a && $ > 0 ? P = at(w + $, l[0][1]) : !a && $ < 0 && (P = st(w + j, l[1][1])), S = Math.max(S, T), I = Math.max(I, P);
  }
  if (r) {
    if (d) {
      const T = uo(W / _, y, b) * _;
      if (S = Math.max(S, T), s) {
        let P = 0;
        !u && !a || u && !a && h ? P = at(w + H + W / _, s[1][1]) * _ : P = st(w + H + (u ? D : -D) / _, s[0][1]) * _, S = Math.max(S, P);
      }
      if (l) {
        let P = 0;
        !u && !a || u && !a && h ? P = st(w + W / _, l[1][1]) * _ : P = at(w + (u ? D : -D) / _, l[0][1]) * _, S = Math.max(S, P);
      }
    }
    if (f) {
      const T = uo(j * _, x, v) / _;
      if (I = Math.max(I, T), s) {
        let P = 0;
        !u && !a || a && !u && h ? P = at(p + j * _ + z, s[1][0]) / _ : P = st(p + (a ? $ : -$) * _ + z, s[0][0]) / _, I = Math.max(I, P);
      }
      if (l) {
        let P = 0;
        !u && !a || a && !u && h ? P = st(p + j * _, l[1][0]) / _ : P = at(p + (a ? $ : -$) * _, l[0][0]) / _, I = Math.max(I, P);
      }
    }
  }
  $ = $ + ($ < 0 ? I : -I), D = D + (D < 0 ? S : -S), r && (h ? W > j * _ ? $ = (ls(u, a) ? -D : D) / _ : D = (ls(u, a) ? -$ : $) * _ : d ? ($ = D / _, a = u) : (D = $ * _, u = a));
  const C = u ? p + D : p, A = a ? w + $ : w;
  return {
    width: E + (u ? -D : D),
    height: k + (a ? -$ : $),
    x: i[0] * D * (u ? -1 : 1) + C,
    y: i[1] * $ * (a ? -1 : 1) + A
  };
}
const fc = { width: 0, height: 0, x: 0, y: 0 }, ep = {
  ...fc,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function tp(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, i = e.measured.width ?? 0, s = e.measured.height ?? 0, l = n[0] * i, u = n[1] * s;
  return [
    [o - l, r - u],
    [o + i - l, r + s - u]
  ];
}
function np({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const i = Le(e);
  let s = {
    controlDirection: cs("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function l({ controlPosition: a, boundaries: d, keepAspectRatio: f, resizeDirection: h, onResizeStart: g, onResize: m, onResizeEnd: x, shouldResize: v }) {
    let y = { ...fc }, b = { ...ep };
    s = {
      boundaries: d,
      resizeDirection: h,
      keepAspectRatio: f,
      controlDirection: cs(a)
    };
    let p, w = null, E = [], k, _, D, $ = !1;
    const W = ba().on("start", (j) => {
      const { nodeLookup: z, transform: H, snapGrid: S, snapToGrid: I, nodeOrigin: C, paneDomNode: A } = n();
      if (p = z.get(t), !p)
        return;
      w = A?.getBoundingClientRect() ?? null;
      const { xSnapped: T, ySnapped: P } = wn(j.sourceEvent, {
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
      }, k = void 0, _ = bt(p.extent) ? p.extent : void 0, p.parentId && (p.extent === "parent" || p.expandParent) && (k = z.get(p.parentId)), k && p.extent === "parent" && (_ = [
        [0, 0],
        [k.measured.width, k.measured.height]
      ]), E = [], D = void 0;
      for (const [V, F] of z)
        if (F.parentId === t && (E.push({
          id: V,
          position: { ...F.position },
          extent: F.extent
        }), F.extent === "parent" || F.expandParent)) {
          const O = tp(F, p, F.origin ?? C);
          D ? D = [
            [Math.min(O[0][0], D[0][0]), Math.min(O[0][1], D[0][1])],
            [Math.max(O[1][0], D[1][0]), Math.max(O[1][1], D[1][1])]
          ] : D = O;
        }
      g?.(j, { ...y });
    }).on("drag", (j) => {
      const { transform: z, snapGrid: H, snapToGrid: S, nodeOrigin: I } = n(), C = wn(j.sourceEvent, {
        transform: z,
        snapGrid: H,
        snapToGrid: S,
        containerBounds: w
      }), A = [];
      if (!p)
        return;
      const { x: T, y: P, width: V, height: F } = y, O = {}, G = p.origin ?? I, { width: K, height: ne, x: ae, y: U } = Jh(b, s.controlDirection, C, s.boundaries, s.keepAspectRatio, G, _, D), L = K !== V, Y = ne !== F, re = ae !== T && L, ie = U !== P && Y;
      if (!re && !ie && !L && !Y)
        return;
      if ((re || ie || G[0] === 1 || G[1] === 1) && (O.x = re ? ae : y.x, O.y = ie ? U : y.y, y.x = O.x, y.y = O.y, E.length > 0)) {
        const R = ae - T, J = U - P;
        for (const fe of E)
          fe.position = {
            x: fe.position.x - R + G[0] * (K - V),
            y: fe.position.y - J + G[1] * (ne - F)
          }, A.push(fe);
      }
      if ((L || Y) && (O.width = L && (!s.resizeDirection || s.resizeDirection === "horizontal") ? K : y.width, O.height = Y && (!s.resizeDirection || s.resizeDirection === "vertical") ? ne : y.height, y.width = O.width, y.height = O.height), k && p.expandParent) {
        const R = G[0] * (O.width ?? 0);
        O.x && O.x < R && (y.x = R, b.x = b.x - (O.x - R));
        const J = G[1] * (O.height ?? 0);
        O.y && O.y < J && (y.y = J, b.y = b.y - (O.y - J));
      }
      const Z = Qh({
        width: y.width,
        prevWidth: V,
        height: y.height,
        prevHeight: F,
        affectsX: s.controlDirection.affectsX,
        affectsY: s.controlDirection.affectsY
      }), ee = { ...y, direction: Z };
      v?.(j, ee) !== !1 && ($ = !0, m?.(j, ee), o(O, A));
    }).on("end", (j) => {
      $ && (x?.(j, { ...y }), r?.({ ...y }), $ = !1);
    });
    i.call(W);
  }
  function u() {
    i.on(".drag", null);
  }
  return {
    update: l,
    destroy: u
  };
}
var Ar = { exports: {} }, Mr = {}, Dr = { exports: {} }, Pr = {};
var us;
function op() {
  if (us) return Pr;
  us = 1;
  var e = dt;
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
  function a(f, h) {
    return h();
  }
  var d = typeof window > "u" || typeof window.document > "u" || typeof window.document.createElement > "u" ? a : l;
  return Pr.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : d, Pr;
}
var ds;
function rp() {
  return ds || (ds = 1, Dr.exports = op()), Dr.exports;
}
var fs;
function ip() {
  if (fs) return Mr;
  fs = 1;
  var e = dt, t = rp();
  function n(a, d) {
    return a === d && (a !== 0 || 1 / a === 1 / d) || a !== a && d !== d;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, i = e.useRef, s = e.useEffect, l = e.useMemo, u = e.useDebugValue;
  return Mr.useSyncExternalStoreWithSelector = function(a, d, f, h, g) {
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
    var v = r(a, m[0], m[1]);
    return s(
      function() {
        x.hasValue = !0, x.value = v;
      },
      [v]
    ), u(v), v;
  }, Mr;
}
var hs;
function sp() {
  return hs || (hs = 1, Ar.exports = ip()), Ar.exports;
}
var ap = sp();
const cp = /* @__PURE__ */ Nl(ap), lp = {}, ps = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (d, f) => {
    const h = typeof d == "function" ? d(t) : d;
    if (!Object.is(h, t)) {
      const g = t;
      t = f ?? (typeof h != "object" || h === null) ? h : Object.assign({}, t, h), n.forEach((m) => m(t, g));
    }
  }, r = () => t, u = { setState: o, getState: r, getInitialState: () => a, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (lp ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, a = t = e(o, r, u);
  return u;
}, up = (e) => e ? ps(e) : ps, { useDebugValue: dp } = dt, { useSyncExternalStoreWithSelector: fp } = cp, hp = (e) => e;
function hc(e, t = hp, n) {
  const o = fp(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return dp(o), o;
}
const gs = (e, t) => {
  const n = up(e), o = (r, i = t) => hc(n, r, i);
  return Object.assign(o, n), o;
}, pp = (e, t) => e ? gs(e, t) : gs;
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
var $r = { exports: {} }, je = {};
var ms;
function gp() {
  if (ms) return je;
  ms = 1;
  var e = dt;
  function t(u) {
    var a = "https://react.dev/errors/" + u;
    if (1 < arguments.length) {
      a += "?args[]=" + encodeURIComponent(arguments[1]);
      for (var d = 2; d < arguments.length; d++)
        a += "&args[]=" + encodeURIComponent(arguments[d]);
    }
    return "Minified React error #" + u + "; visit " + a + " for the full message or use the non-minified dev environment for full errors and additional helpful warnings.";
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
  function i(u, a, d) {
    var f = 3 < arguments.length && arguments[3] !== void 0 ? arguments[3] : null;
    return {
      $$typeof: r,
      key: f == null ? null : "" + f,
      children: u,
      containerInfo: a,
      implementation: d
    };
  }
  var s = e.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE;
  function l(u, a) {
    if (u === "font") return "";
    if (typeof a == "string")
      return a === "use-credentials" ? a : "";
  }
  return je.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, je.createPortal = function(u, a) {
    var d = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!a || a.nodeType !== 1 && a.nodeType !== 9 && a.nodeType !== 11)
      throw Error(t(299));
    return i(u, a, null, d);
  }, je.flushSync = function(u) {
    var a = s.T, d = o.p;
    try {
      if (s.T = null, o.p = 2, u) return u();
    } finally {
      s.T = a, o.p = d, o.d.f();
    }
  }, je.preconnect = function(u, a) {
    typeof u == "string" && (a ? (a = a.crossOrigin, a = typeof a == "string" ? a === "use-credentials" ? a : "" : void 0) : a = null, o.d.C(u, a));
  }, je.prefetchDNS = function(u) {
    typeof u == "string" && o.d.D(u);
  }, je.preinit = function(u, a) {
    if (typeof u == "string" && a && typeof a.as == "string") {
      var d = a.as, f = l(d, a.crossOrigin), h = typeof a.integrity == "string" ? a.integrity : void 0, g = typeof a.fetchPriority == "string" ? a.fetchPriority : void 0;
      d === "style" ? o.d.S(
        u,
        typeof a.precedence == "string" ? a.precedence : void 0,
        {
          crossOrigin: f,
          integrity: h,
          fetchPriority: g
        }
      ) : d === "script" && o.d.X(u, {
        crossOrigin: f,
        integrity: h,
        fetchPriority: g,
        nonce: typeof a.nonce == "string" ? a.nonce : void 0
      });
    }
  }, je.preinitModule = function(u, a) {
    if (typeof u == "string")
      if (typeof a == "object" && a !== null) {
        if (a.as == null || a.as === "script") {
          var d = l(
            a.as,
            a.crossOrigin
          );
          o.d.M(u, {
            crossOrigin: d,
            integrity: typeof a.integrity == "string" ? a.integrity : void 0,
            nonce: typeof a.nonce == "string" ? a.nonce : void 0
          });
        }
      } else a == null && o.d.M(u);
  }, je.preload = function(u, a) {
    if (typeof u == "string" && typeof a == "object" && a !== null && typeof a.as == "string") {
      var d = a.as, f = l(d, a.crossOrigin);
      o.d.L(u, d, {
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
  }, je.preloadModule = function(u, a) {
    if (typeof u == "string")
      if (a) {
        var d = l(a.as, a.crossOrigin);
        o.d.m(u, {
          as: typeof a.as == "string" && a.as !== "script" ? a.as : void 0,
          crossOrigin: d,
          integrity: typeof a.integrity == "string" ? a.integrity : void 0
        });
      } else o.d.m(u);
  }, je.requestFormReset = function(u) {
    o.d.r(u);
  }, je.unstable_batchedUpdates = function(u, a) {
    return u(a);
  }, je.useFormState = function(u, a, d) {
    return s.H.useFormState(u, a, d);
  }, je.useFormStatus = function() {
    return s.H.useHostTransitionStatus();
  }, je.version = "19.2.7", je;
}
var ys;
function mp() {
  if (ys) return $r.exports;
  ys = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), $r.exports = gp(), $r.exports;
}
var yp = mp();
const qo = ri(null), xp = qo.Provider, pc = He.error001("react");
function ue(e, t) {
  const n = An(qo);
  if (n === null)
    throw new Error(pc);
  return hc(n, e, t);
}
function ye() {
  const e = An(qo);
  if (e === null)
    throw new Error(pc);
  return ge(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const xs = { display: "none" }, wp = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, gc = "react-flow__node-desc", mc = "react-flow__edge-desc", vp = "react-flow__aria-live", bp = (e) => e.ariaLiveMessage, Sp = (e) => e.ariaLabelConfig;
function Np({ rfId: e }) {
  const t = ue(bp);
  return c.jsx("div", { id: `${vp}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: wp, children: t });
}
function Ep({ rfId: e, disableKeyboardA11y: t }) {
  const n = ue(Sp);
  return c.jsxs(c.Fragment, { children: [c.jsx("div", { id: `${gc}-${e}`, style: xs, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), c.jsx("div", { id: `${mc}-${e}`, style: xs, children: n["edge.a11yDescription.default"] }), !t && c.jsx(Np, { rfId: e })] });
}
const Zo = Ro(({ position: e = "top-left", children: t, className: n, style: o, ...r }, i) => {
  const s = `${e}`.split("-");
  return c.jsx("div", { className: Ne(["react-flow__panel", n, ...s]), style: o, ref: i, ...r, children: t });
});
Zo.displayName = "Panel";
function kp({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : c.jsx(Zo, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: c.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const Cp = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, fo = (e) => e.id;
function _p(e, t) {
  return me(e.selectedNodes.map(fo), t.selectedNodes.map(fo)) && me(e.selectedEdges.map(fo), t.selectedEdges.map(fo));
}
function Ip({ onSelectionChange: e }) {
  const t = ye(), { selectedNodes: n, selectedEdges: o } = ue(Cp, _p);
  return se(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((i) => i(r));
  }, [n, o, e]), null;
}
const jp = (e) => !!e.onSelectionChangeHandlers;
function Ap({ onSelectionChange: e }) {
  const t = ue(jp);
  return e || t ? c.jsx(Ip, { onSelectionChange: e }) : null;
}
const yc = [0, 0], Mp = { x: 0, y: 0, zoom: 1 }, Dp = [
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
], ws = [...Dp, "rfId"], Pp = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), vs = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: En,
  nodeOrigin: yc,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function $p(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: i, setNodeExtent: s, reset: l, setDefaultNodesAndEdges: u } = ue(Pp, me), a = ye();
  se(() => (u(e.defaultNodes, e.defaultEdges), () => {
    d.current = vs, l();
  }), []);
  const d = ce(vs);
  return se(
    () => {
      for (const f of ws) {
        const h = e[f], g = d.current[f];
        h !== g && (typeof e[f] > "u" || (f === "nodes" ? t(h) : f === "edges" ? n(h) : f === "minZoom" ? o(h) : f === "maxZoom" ? r(h) : f === "translateExtent" ? i(h) : f === "nodeExtent" ? s(h) : f === "ariaLabelConfig" ? a.setState({ ariaLabelConfig: gh(h) }) : f === "fitView" ? a.setState({ fitViewQueued: h }) : f === "fitViewOptions" ? a.setState({ fitViewOptions: h }) : a.setState({ [f]: h })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    ws.map((f) => e[f])
  ), null;
}
function bs() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function Tp(e) {
  const [t, n] = q(e === "system" ? null : e);
  return se(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = bs(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : bs()?.matches ? "dark" : "light";
}
const Ss = typeof document < "u" ? document : null;
function In(e = null, t = { target: Ss, actInsideInputWithModifier: !0 }) {
  const [n, o] = q(!1), r = ce(!1), i = ce(/* @__PURE__ */ new Set([])), [s, l] = ge(() => {
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
  return se(() => {
    const u = t?.target ?? Ss, a = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (g) => {
        if (r.current = g.ctrlKey || g.metaKey || g.shiftKey || g.altKey, (!r.current || r.current && !a) && Ka(g))
          return !1;
        const x = Es(g.code, l);
        if (i.current.add(g[x]), Ns(s, i.current, !1)) {
          const v = g.composedPath?.()?.[0] || g.target, y = v?.nodeName === "BUTTON" || v?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !y) && g.preventDefault(), o(!0);
        }
      }, f = (g) => {
        const m = Es(g.code, l);
        Ns(s, i.current, !0) ? (o(!1), i.current.clear()) : i.current.delete(g[m]), g.key === "Meta" && i.current.clear(), r.current = !1;
      }, h = () => {
        i.current.clear(), o(!1);
      };
      return u?.addEventListener("keydown", d), u?.addEventListener("keyup", f), window.addEventListener("blur", h), window.addEventListener("contextmenu", h), () => {
        u?.removeEventListener("keydown", d), u?.removeEventListener("keyup", f), window.removeEventListener("blur", h), window.removeEventListener("contextmenu", h);
      };
    }
  }, [e, o]), n;
}
function Ns(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function Es(e, t) {
  return t.includes(e) ? "code" : "key";
}
const zp = () => {
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
      const { width: o, height: r, minZoom: i, maxZoom: s, panZoom: l } = e.getState(), u = gi(t, o, r, i, s, n?.padding ?? 0.1);
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
      const { x: l, y: u } = s.getBoundingClientRect(), a = {
        x: t.x - l,
        y: t.y - u
      }, d = n.snapGrid ?? r, f = n.snapToGrid ?? i;
      return Yt(a, o, f, d);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: r, y: i } = o.getBoundingClientRect(), s = Vt(t, n);
      return {
        x: s.x + r,
        y: s.y + i
      };
    }
  }), []);
};
function xc(e, t) {
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
      Lp(u, l);
    n.push(l);
  }
  return r.length && r.forEach((i) => {
    i.index !== void 0 ? n.splice(i.index, 0, { ...i.item }) : n.push({ ...i.item });
  }), n;
}
function Lp(e, t) {
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
function wc(e, t) {
  return xc(e, t);
}
function vc(e, t) {
  return xc(e, t);
}
function pt(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function Dt(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [r, i] of e) {
    const s = t.has(r);
    !(i.selected === void 0 && !s) && i.selected !== s && (n && (i.selected = s), o.push(pt(i.id, s)));
  }
  return o;
}
function ks({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((r) => [r.id, r]));
  for (const [r, i] of e.entries()) {
    const s = t.get(i.id), l = s?.internals?.userNode ?? s;
    l !== void 0 && l !== i && n.push({ id: i.id, item: i, type: "replace" }), l === void 0 && n.push({ item: i, type: "add", index: r });
  }
  for (const [r] of t)
    o.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function Cs(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const bc = Xa();
function Sc(e, t, n = {}) {
  return vh(e, t, {
    ...n,
    onError: n.onError ?? bc
  });
}
function Rp(e, t, n, o = { shouldReplaceId: !0 }) {
  return bh(e, t, n, {
    ...o,
    onError: o.onError ?? bc
  });
}
const _s = (e) => sh(e), Hp = (e) => Oa(e);
function Nc(e) {
  return Ro(e);
}
const Vp = typeof window < "u" ? Sl : se;
function Is(e) {
  const [t, n] = q(BigInt(0)), [o] = q(() => Op(() => n((r) => r + BigInt(1))));
  return Vp(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function Op(e) {
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
const Ec = ri(null);
function Bp({ children: e }) {
  const t = ye(), n = pe((l) => {
    const { nodes: u = [], setNodes: a, hasDefaultNodes: d, onNodesChange: f, nodeLookup: h, fitViewQueued: g, onNodesChangeMiddlewareMap: m } = t.getState();
    let x = u;
    for (const y of l)
      x = typeof y == "function" ? y(x) : y;
    let v = ks({
      items: x,
      lookup: h
    });
    for (const y of m.values())
      v = y(v);
    d && a(x), v.length > 0 ? f?.(v) : g && window.requestAnimationFrame(() => {
      const { fitViewQueued: y, nodes: b, setNodes: p } = t.getState();
      y && p(b);
    });
  }, []), o = Is(n), r = pe((l) => {
    const { edges: u = [], setEdges: a, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: h } = t.getState();
    let g = u;
    for (const m of l)
      g = typeof m == "function" ? m(g) : m;
    d ? a(g) : f && f(ks({
      items: g,
      lookup: h
    }));
  }, []), i = Is(r), s = ge(() => ({ nodeQueue: o, edgeQueue: i }), []);
  return c.jsx(Ec.Provider, { value: s, children: e });
}
function Fp() {
  const e = An(Ec);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Wp = (e) => !!e.panZoom;
function Si() {
  const e = zp(), t = ye(), n = Fp(), o = ue(Wp), r = ge(() => {
    const i = (f) => t.getState().nodeLookup.get(f), s = (f) => {
      n.nodeQueue.push(f);
    }, l = (f) => {
      n.edgeQueue.push(f);
    }, u = (f) => {
      const { nodeLookup: h, nodeOrigin: g } = t.getState(), m = _s(f) ? f : h.get(f.id), x = m.parentId ? qa(m.position, m.measured, m.parentId, h, g) : m.position, v = {
        ...m,
        position: x,
        width: m.measured?.width ?? m.width,
        height: m.measured?.height ?? m.height
      };
      return Ht(v);
    }, a = (f, h, g = { replace: !1 }) => {
      s((m) => m.map((x) => {
        if (x.id === f) {
          const v = typeof h == "function" ? h(x) : h;
          return g.replace && _s(v) ? v : { ...x, ...v };
        }
        return x;
      }));
    }, d = (f, h, g = { replace: !1 }) => {
      l((m) => m.map((x) => {
        if (x.id === f) {
          const v = typeof h == "function" ? h(x) : h;
          return g.replace && Hp(v) ? v : { ...x, ...v };
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
        const { nodes: g, edges: m, onNodesDelete: x, onEdgesDelete: v, triggerNodeChanges: y, triggerEdgeChanges: b, onDelete: p, onBeforeDelete: w } = t.getState(), { nodes: E, edges: k } = await dh({
          nodesToRemove: f,
          edgesToRemove: h,
          nodes: g,
          edges: m,
          onBeforeDelete: w
        }), _ = k.length > 0, D = E.length > 0;
        if (_) {
          const $ = k.map(Cs);
          v?.(k), b($);
        }
        if (D) {
          const $ = E.map(Cs);
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
        const m = Gi(f), x = m ? f : u(f), v = g !== void 0;
        return x ? (g || t.getState().nodes).filter((y) => {
          const b = t.getState().nodeLookup.get(y.id);
          if (b && !m && (y.id === f.id || !b.internals.positionAbsolute))
            return !1;
          const p = Ht(v ? y : b), w = Cn(p, x);
          return h && w > 0 || w >= p.width * p.height || w >= x.width * x.height;
        }) : [];
      },
      isNodeIntersecting: (f, h, g = !0) => {
        const x = Gi(f) ? f : u(f);
        if (!x)
          return !1;
        const v = Cn(x, h);
        return g && v > 0 || v >= h.width * h.height || v >= x.width * x.height;
      },
      updateNode: a,
      updateNodeData: (f, h, g = { replace: !1 }) => {
        a(f, (m) => {
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
        return ah(f, { nodeLookup: h, nodeOrigin: g });
      },
      getHandleConnections: ({ type: f, id: h, nodeId: g }) => Array.from(t.getState().connectionLookup.get(`${g}-${f}${h ? `-${h}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: h, nodeId: g }) => Array.from(t.getState().connectionLookup.get(`${g}${f ? h ? `-${f}-${h}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const h = t.getState().fitViewResolver ?? ph();
        return t.setState({ fitViewQueued: !0, fitViewOptions: f, fitViewResolver: h }), n.nodeQueue.push((g) => [...g]), h.promise;
      }
    };
  }, []);
  return ge(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const js = (e) => e.selected, Xp = typeof window < "u" ? window : void 0;
function Yp({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = ye(), { deleteElements: o } = Si(), r = In(e, { actInsideInputWithModifier: !1 }), i = In(t, { target: Xp });
  se(() => {
    if (r) {
      const { edges: s, nodes: l } = n.getState();
      o({ nodes: l.filter(js), edges: s.filter(js) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), se(() => {
    n.setState({ multiSelectionActive: i });
  }, [i]);
}
function qp(e) {
  const t = ye();
  se(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = mi(e.current);
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
}, Zp = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function Kp({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: i = yt.Free, zoomOnDoubleClick: s = !0, panOnDrag: l = !0, defaultViewport: u, translateExtent: a, minZoom: d, maxZoom: f, zoomActivationKeyCode: h, preventScrolling: g = !0, children: m, noWheelClassName: x, noPanClassName: v, onViewportChange: y, isControlledViewport: b, paneClickDistance: p, selectionOnDrag: w }) {
  const E = ye(), k = ce(null), { userSelectionActive: _, lib: D, connectionInProgress: $ } = ue(Zp, me), W = In(h), j = ce();
  qp(k);
  const z = pe((H) => {
    y?.({ x: H[0], y: H[1], zoom: H[2] }), b || E.setState({ transform: H });
  }, [y, b]);
  return se(() => {
    if (k.current) {
      j.current = Gh({
        domNode: k.current,
        minZoom: d,
        maxZoom: f,
        translateExtent: a,
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
  }, []), se(() => {
    j.current?.update({
      onPaneContextMenu: e,
      zoomOnScroll: t,
      zoomOnPinch: n,
      panOnScroll: o,
      panOnScrollSpeed: r,
      panOnScrollMode: i,
      zoomOnDoubleClick: s,
      panOnDrag: l,
      zoomActivationKeyPressed: W,
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
    W,
    g,
    v,
    _,
    x,
    D,
    z,
    $,
    w,
    p
  ]), c.jsx("div", { className: "react-flow__renderer", ref: k, style: Ko, children: m });
}
const Up = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function Gp() {
  const { userSelectionActive: e, userSelectionRect: t } = ue(Up, me);
  return e && t ? c.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const Tr = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, Qp = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function Jp({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = kn.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: i, selectionOnDrag: s, onSelectionStart: l, onSelectionEnd: u, onPaneClick: a, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: h, onPaneMouseMove: g, onPaneMouseLeave: m, children: x }) {
  const v = ce(0), y = ye(), { userSelectionActive: b, elementsSelectable: p, dragging: w, connectionInProgress: E, panBy: k, autoPanSpeed: _ } = ue(Qp, me), D = p && (e || b), $ = ce(null), W = ce(), j = ce(/* @__PURE__ */ new Set()), z = ce(/* @__PURE__ */ new Set()), H = ce(!1), S = ce({ x: 0, y: 0 }), I = ce(!1), C = (L) => {
    if (H.current || E) {
      H.current = !1;
      return;
    }
    a?.(L), y.getState().resetSelectedElements(), y.setState({ nodesSelectionActive: !1 });
  }, A = (L) => {
    if (Array.isArray(o) && o?.includes(2)) {
      L.preventDefault();
      return;
    }
    d?.(L);
  }, T = f ? (L) => f(L) : void 0, P = (L) => {
    H.current && (L.stopPropagation(), H.current = !1);
  }, V = (L) => {
    const { domNode: Y, transform: re } = y.getState();
    if (W.current = Y?.getBoundingClientRect(), !W.current)
      return;
    const ie = L.target === $.current;
    if (!ie && !!L.target.closest(".nokey") || !e || !(s && ie || t) || L.button !== 0 || !L.isPrimary)
      return;
    L.target?.setPointerCapture?.(L.pointerId), H.current = !1;
    const { x: oe, y: R } = Ze(L.nativeEvent, W.current), J = Yt({ x: oe, y: R }, re);
    y.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: J.x,
        startY: J.y,
        x: oe,
        y: R
      }
    }), ie || (L.stopPropagation(), L.preventDefault());
  };
  function F(L, Y) {
    const { userSelectionRect: re } = y.getState();
    if (!re)
      return;
    const { transform: ie, nodeLookup: Z, edgeLookup: ee, connectionLookup: oe, triggerNodeChanges: R, triggerEdgeChanges: J, defaultEdgeOptions: fe } = y.getState(), we = { x: re.startX, y: re.startY }, { x: Oe, y: ve } = Vt(we, ie), De = {
      startX: we.x,
      startY: we.y,
      x: L < Oe ? L : Oe,
      y: Y < ve ? Y : ve,
      width: Math.abs(L - Oe),
      height: Math.abs(Y - ve)
    }, tt = j.current, Fe = z.current;
    j.current = new Set(hi(Z, De, ie, n === kn.Partial, !0).map((Ce) => Ce.id)), z.current = /* @__PURE__ */ new Set();
    const Be = fe?.selectable ?? !0;
    for (const Ce of j.current) {
      const Ae = oe.get(Ce);
      if (Ae)
        for (const { edgeId: Me } of Ae.values()) {
          const Ue = ee.get(Me);
          Ue && (Ue.selectable ?? Be) && z.current.add(Me);
        }
    }
    if (!Qi(tt, j.current)) {
      const Ce = Dt(Z, j.current, !0);
      R(Ce);
    }
    if (!Qi(Fe, z.current)) {
      const Ce = Dt(ee, z.current);
      J(Ce);
    }
    y.setState({
      userSelectionRect: De,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function O() {
    if (!r || !W.current)
      return;
    const [L, Y] = pi(S.current, W.current, _);
    k({ x: L, y: Y }).then((re) => {
      if (!H.current || !re) {
        v.current = requestAnimationFrame(O);
        return;
      }
      const { x: ie, y: Z } = S.current;
      F(ie, Z), v.current = requestAnimationFrame(O);
    });
  }
  const G = () => {
    cancelAnimationFrame(v.current), v.current = 0, I.current = !1;
  };
  se(() => () => G(), []);
  const K = (L) => {
    const { userSelectionRect: Y, transform: re, resetSelectedElements: ie } = y.getState();
    if (!W.current || !Y)
      return;
    const { x: Z, y: ee } = Ze(L.nativeEvent, W.current);
    S.current = { x: Z, y: ee };
    const oe = Vt({ x: Y.startX, y: Y.startY }, re);
    if (!H.current) {
      const R = t ? 0 : i;
      if (Math.hypot(Z - oe.x, ee - oe.y) <= R)
        return;
      ie(), l?.(L);
    }
    H.current = !0, I.current || (O(), I.current = !0), F(Z, ee);
  }, ne = (L) => {
    L.button === 0 && (L.target?.releasePointerCapture?.(L.pointerId), !b && L.target === $.current && y.getState().userSelectionRect && C?.(L), y.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), H.current && (u?.(L), y.setState({
      nodesSelectionActive: j.current.size > 0
    })), G());
  }, ae = (L) => {
    L.target?.releasePointerCapture?.(L.pointerId), G();
  }, U = o === !0 || Array.isArray(o) && o.includes(0);
  return c.jsxs("div", { className: Ne(["react-flow__pane", { draggable: U, dragging: w, selection: e }]), onClick: D ? void 0 : Tr(C, $), onContextMenu: Tr(A, $), onWheel: Tr(T, $), onPointerEnter: D ? void 0 : h, onPointerMove: D ? K : g, onPointerUp: D ? ne : void 0, onPointerCancel: D ? ae : void 0, onPointerDownCapture: D ? V : void 0, onClickCapture: D ? P : void 0, onPointerLeave: m, ref: $, style: Ko, children: [x, c.jsx(Gp, {})] });
}
function Qr({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: i, multiSelectionActive: s, nodeLookup: l, onError: u } = t.getState(), a = l.get(e);
  if (!a) {
    u?.("012", He.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), a.selected ? (n || a.selected && s) && (i({ nodes: [a], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function kc({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: i, nodeClickDistance: s }) {
  const l = ye(), [u, a] = q(!1), d = ce();
  return se(() => {
    d.current = Lh({
      getStoreItems: () => l.getState(),
      onNodeMouseDown: (f) => {
        Qr({
          id: f,
          store: l,
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
  }, []), se(() => {
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
const eg = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function Cc() {
  const e = ye();
  return pe((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: i, nodesDraggable: s, onError: l, updateNodePositions: u, nodeLookup: a, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), h = eg(s), g = r ? i[0] : 5, m = r ? i[1] : 5, x = n.direction.x * g * n.factor, v = n.direction.y * m * n.factor;
    for (const [, y] of a) {
      if (!h(y))
        continue;
      let b = {
        x: y.internals.positionAbsolute.x + x,
        y: y.internals.positionAbsolute.y + v
      };
      r && (b = Tn(b, i));
      const { position: p, positionAbsolute: w } = Ba({
        nodeId: y.id,
        nextPosition: b,
        nodeLookup: a,
        nodeExtent: o,
        nodeOrigin: d,
        onError: l
      });
      y.position = p, y.internals.positionAbsolute = w, f.set(y.id, y);
    }
    u(f);
  }, []);
}
const Ni = ri(null), tg = Ni.Provider;
Ni.Consumer;
const _c = () => An(Ni), ng = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), og = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: i, connection: s } = o, { fromHandle: l, toHandle: u, isValid: a } = s, d = u?.nodeId === e && u?.id === t && u?.type === n;
  return {
    connectingFrom: l?.nodeId === e && l?.id === t && l?.type === n,
    connectingTo: d,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: i === Lt.Strict ? l?.type !== n : e !== l?.nodeId || t !== l?.id,
    connectionInProcess: !!l,
    clickConnectionInProcess: !!r,
    valid: d && a
  };
};
function rg({ type: e = "source", position: t = te.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: i = !0, id: s, onConnect: l, children: u, className: a, onMouseDown: d, onTouchStart: f, ...h }, g) {
  const m = s || null, x = e === "target", v = ye(), y = _c(), { connectOnClick: b, noPanClassName: p, rfId: w } = ue(ng, me), { connectingFrom: E, connectingTo: k, clickConnecting: _, isPossibleEndHandle: D, connectionInProcess: $, clickConnectionInProcess: W, valid: j } = ue(og(y, m, e), me);
  y || v.getState().onError?.("010", He.error010());
  const z = (I) => {
    const { defaultEdgeOptions: C, onConnect: A, hasDefaultEdges: T } = v.getState(), P = {
      ...C,
      ...I
    };
    if (T) {
      const { edges: V, setEdges: F, onError: O } = v.getState();
      F(Sc(P, V, { onError: O }));
    }
    A?.(P), l?.(P);
  }, H = (I) => {
    if (!y)
      return;
    const C = Ua(I.nativeEvent);
    if (r && (C && I.button === 0 || !C)) {
      const A = v.getState();
      Gr.onPointerDown(I.nativeEvent, {
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
    const { onClickConnectStart: C, onClickConnectEnd: A, connectionClickStartHandle: T, connectionMode: P, isValidConnection: V, lib: F, rfId: O, nodeLookup: G, connection: K } = v.getState();
    if (!y || !T && !r)
      return;
    if (!T) {
      C?.(I.nativeEvent, { nodeId: y, handleId: m, handleType: e }), v.setState({ connectionClickStartHandle: { nodeId: y, type: e, id: m } });
      return;
    }
    const ne = Za(I.target), ae = n || V, { connection: U, isValid: L } = Gr.isValid(I.nativeEvent, {
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
      flowId: O,
      doc: ne,
      lib: F,
      nodeLookup: G
    });
    L && U && z(U);
    const Y = structuredClone(K);
    delete Y.inProgress, Y.toPosition = Y.toHandle ? Y.toHandle.position : null, A?.(I, Y), v.setState({ connectionClickStartHandle: null });
  };
  return c.jsx("div", { "data-handleid": m, "data-nodeid": y, "data-handlepos": t, "data-id": `${w}-${y}-${m}-${e}`, className: Ne([
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
      clickconnecting: _,
      connectingfrom: E,
      connectingto: k,
      valid: j,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!$ || D) && ($ || W ? i : r)
    }
  ]), onMouseDown: H, onTouchStart: H, onClick: b ? S : void 0, ref: g, ...h, children: u });
}
const Bt = be(Nc(rg));
function ig({ data: e, isConnectable: t, sourcePosition: n = te.Bottom }) {
  return c.jsxs(c.Fragment, { children: [e?.label, c.jsx(Bt, { type: "source", position: n, isConnectable: t })] });
}
function sg({ data: e, isConnectable: t, targetPosition: n = te.Top, sourcePosition: o = te.Bottom }) {
  return c.jsxs(c.Fragment, { children: [c.jsx(Bt, { type: "target", position: n, isConnectable: t }), e?.label, c.jsx(Bt, { type: "source", position: o, isConnectable: t })] });
}
function ag() {
  return null;
}
function cg({ data: e, isConnectable: t, targetPosition: n = te.Top }) {
  return c.jsxs(c.Fragment, { children: [c.jsx(Bt, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const $o = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, As = {
  input: ig,
  default: sg,
  output: cg,
  group: ag
};
function lg(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const ug = (e) => {
  const { width: t, height: n, x: o, y: r } = $n(e.nodeLookup, {
    filter: (i) => !!i.selected
  });
  return {
    width: qe(t) ? t : null,
    height: qe(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function dg({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = ye(), { width: r, height: i, transformString: s, userSelectionActive: l } = ue(ug, me), u = Cc(), a = ce(null);
  se(() => {
    n || a.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !l && r !== null && i !== null;
  if (kc({
    nodeRef: a,
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
  return c.jsx("div", { className: Ne(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: s
  }, children: c.jsx("div", { ref: a, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : h, style: {
    width: r,
    height: i
  } }) });
}
const Ms = typeof window < "u" ? window : void 0, fg = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function Ic({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, paneClickDistance: l, deleteKeyCode: u, selectionKeyCode: a, selectionOnDrag: d, selectionMode: f, onSelectionStart: h, onSelectionEnd: g, multiSelectionKeyCode: m, panActivationKeyCode: x, zoomActivationKeyCode: v, elementsSelectable: y, zoomOnScroll: b, zoomOnPinch: p, panOnScroll: w, panOnScrollSpeed: E, panOnScrollMode: k, zoomOnDoubleClick: _, panOnDrag: D, autoPanOnSelection: $, defaultViewport: W, translateExtent: j, minZoom: z, maxZoom: H, preventScrolling: S, onSelectionContextMenu: I, noWheelClassName: C, noPanClassName: A, disableKeyboardA11y: T, onViewportChange: P, isControlledViewport: V }) {
  const { nodesSelectionActive: F, userSelectionActive: O } = ue(fg, me), G = In(a, { target: Ms }), K = In(x, { target: Ms }), ne = K || D, ae = K || w, U = d && ne !== !0, L = G || O || U;
  return Yp({ deleteKeyCode: u, multiSelectionKeyCode: m }), c.jsx(Kp, { onPaneContextMenu: i, elementsSelectable: y, zoomOnScroll: b, zoomOnPinch: p, panOnScroll: ae, panOnScrollSpeed: E, panOnScrollMode: k, zoomOnDoubleClick: _, panOnDrag: !G && ne, defaultViewport: W, translateExtent: j, minZoom: z, maxZoom: H, zoomActivationKeyCode: v, preventScrolling: S, noWheelClassName: C, noPanClassName: A, onViewportChange: P, isControlledViewport: V, paneClickDistance: l, selectionOnDrag: U, children: c.jsxs(Jp, { onSelectionStart: h, onSelectionEnd: g, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, panOnDrag: ne, autoPanOnSelection: $, isSelecting: !!L, selectionMode: f, selectionKeyPressed: G, paneClickDistance: l, selectionOnDrag: U, children: [e, F && c.jsx(dg, { onSelectionContextMenu: I, noPanClassName: A, disableKeyboardA11y: T })] }) });
}
Ic.displayName = "FlowRenderer";
const hg = be(Ic), pg = (e) => (t) => e ? hi(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function gg(e) {
  return ue(pe(pg(e), [e]), me);
}
const mg = (e) => e.updateNodeInternals;
function yg() {
  const e = ue(mg), [t] = q(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
function xg({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = ye(), i = ce(null), s = ce(null), l = ce(e.sourcePosition), u = ce(e.targetPosition), a = ce(t), d = n && !!e.internals.handleBounds;
  return se(() => {
    i.current && !e.hidden && (!d || s.current !== i.current) && (s.current && o?.unobserve(s.current), o?.observe(i.current), s.current = i.current);
  }, [d, e.hidden]), se(() => () => {
    s.current && (o?.unobserve(s.current), s.current = null);
  }, []), se(() => {
    if (i.current) {
      const f = a.current !== t, h = l.current !== e.sourcePosition, g = u.current !== e.targetPosition;
      (f || h || g) && (a.current = t, l.current = e.sourcePosition, u.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: i.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), i;
}
function wg({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: i, onDoubleClick: s, nodesDraggable: l, elementsSelectable: u, nodesConnectable: a, nodesFocusable: d, resizeObserver: f, noDragClassName: h, noPanClassName: g, disableKeyboardA11y: m, rfId: x, nodeTypes: v, nodeClickDistance: y, onError: b }) {
  const { node: p, internals: w, isParent: E } = ue((L) => {
    const Y = L.nodeLookup.get(e), re = L.parentLookup.has(e);
    return {
      node: Y,
      internals: Y.internals,
      isParent: re
    };
  }, me);
  let k = p.type || "default", _ = v?.[k] || As[k];
  _ === void 0 && (b?.("003", He.error003(k)), k = "default", _ = v?.default || As.default);
  const D = !!(p.draggable || l && typeof p.draggable > "u"), $ = !!(p.selectable || u && typeof p.selectable > "u"), W = !!(p.connectable || a && typeof p.connectable > "u"), j = !!(p.focusable || d && typeof p.focusable > "u"), z = ye(), H = Ya(p), S = xg({ node: p, nodeType: k, hasDimensions: H, resizeObserver: f }), I = kc({
    nodeRef: S,
    disabled: p.hidden || !D,
    noDragClassName: h,
    handleSelector: p.dragHandle,
    nodeId: e,
    isSelectable: $,
    nodeClickDistance: y
  }), C = Cc();
  if (p.hidden)
    return null;
  const A = it(p), T = lg(p), P = $ || D || t || n || o || r, V = n ? (L) => n(L, { ...w.userNode }) : void 0, F = o ? (L) => o(L, { ...w.userNode }) : void 0, O = r ? (L) => r(L, { ...w.userNode }) : void 0, G = i ? (L) => i(L, { ...w.userNode }) : void 0, K = s ? (L) => s(L, { ...w.userNode }) : void 0, ne = (L) => {
    const { selectNodesOnDrag: Y, nodeDragThreshold: re } = z.getState();
    $ && (!Y || !D || re > 0) && Qr({
      id: e,
      store: z,
      nodeRef: S
    }), t && t(L, { ...w.userNode });
  }, ae = (L) => {
    if (!(Ka(L.nativeEvent) || m)) {
      if (La.includes(L.key) && $) {
        const Y = L.key === "Escape";
        Qr({
          id: e,
          store: z,
          unselect: Y,
          nodeRef: S
        });
      } else if (D && p.selected && Object.prototype.hasOwnProperty.call($o, L.key)) {
        L.preventDefault();
        const { ariaLabelConfig: Y } = z.getState();
        z.setState({
          ariaLiveMessage: Y["node.a11yDescription.ariaLiveMessage"]({
            direction: L.key.replace("Arrow", "").toLowerCase(),
            x: ~~w.positionAbsolute.x,
            y: ~~w.positionAbsolute.y
          })
        }), C({
          direction: $o[L.key],
          factor: L.shiftKey ? 4 : 1
        });
      }
    }
  }, U = () => {
    if (m || !S.current?.matches(":focus-visible"))
      return;
    const { transform: L, width: Y, height: re, autoPanOnNodeFocus: ie, setCenter: Z } = z.getState();
    if (!ie)
      return;
    hi(/* @__PURE__ */ new Map([[e, p]]), { x: 0, y: 0, width: Y, height: re }, L, !0).length > 0 || Z(p.position.x + A.width / 2, p.position.y + A.height / 2, {
      zoom: L[2]
    });
  };
  return c.jsx("div", { className: Ne([
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
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: V, onMouseMove: F, onMouseLeave: O, onContextMenu: G, onClick: ne, onDoubleClick: K, onKeyDown: j ? ae : void 0, tabIndex: j ? 0 : void 0, onFocus: j ? U : void 0, role: p.ariaRole ?? (j ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": m ? void 0 : `${gc}-${x}`, "aria-label": p.ariaLabel, ...p.domAttributes, children: c.jsx(tg, { value: e, children: c.jsx(_, { id: e, data: p.data, type: k, positionAbsoluteX: w.positionAbsolute.x, positionAbsoluteY: w.positionAbsolute.y, selected: p.selected ?? !1, selectable: $, draggable: D, deletable: p.deletable ?? !0, isConnectable: W, sourcePosition: p.sourcePosition, targetPosition: p.targetPosition, dragging: I, dragHandle: p.dragHandle, zIndex: w.z, parentId: p.parentId, ...A }) }) });
}
var vg = be(wg);
const bg = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function jc(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: i } = ue(bg, me), s = gg(e.onlyRenderVisibleElements), l = yg();
  return c.jsx("div", { className: "react-flow__nodes", style: Ko, children: s.map((u) => (
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
    c.jsx(vg, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: l, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: i }, u)
  )) });
}
jc.displayName = "NodeRenderer";
const Sg = be(jc);
function Ng(e) {
  return ue(pe((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const i = n.nodeLookup.get(r.source), s = n.nodeLookup.get(r.target);
        i && s && xh({
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
const Eg = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return c.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, kg = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return c.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Ds = {
  [Mo.Arrow]: Eg,
  [Mo.ArrowClosed]: kg
};
function Cg(e) {
  const t = ye();
  return ge(() => Object.prototype.hasOwnProperty.call(Ds, e) ? Ds[e] : (t.getState().onError?.("009", He.error009(e)), null), [e]);
}
const _g = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: i = "strokeWidth", strokeWidth: s, orient: l = "auto-start-reverse" }) => {
  const u = Cg(t);
  return u ? c.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: i, orient: l, refX: "0", refY: "0", children: c.jsx(u, { color: n, strokeWidth: s }) }) : null;
}, Ac = ({ defaultColor: e, rfId: t }) => {
  const n = ue((i) => i.edges), o = ue((i) => i.defaultEdgeOptions), r = ge(() => Ch(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? c.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: c.jsx("defs", { children: r.map((i) => c.jsx(_g, { id: i.id, type: i.type, color: i.color, width: i.width, height: i.height, markerUnits: i.markerUnits, strokeWidth: i.strokeWidth, orient: i.orient }, i.id)) }) }) : null;
};
Ac.displayName = "MarkerDefinitions";
var Ig = be(Ac);
function Mc({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: i, labelBgPadding: s = [2, 4], labelBgBorderRadius: l = 2, children: u, className: a, ...d }) {
  const [f, h] = q({ x: 1, y: 0, width: 0, height: 0 }), g = Ne(["react-flow__edge-textwrapper", a]), m = ce(null);
  return se(() => {
    if (m.current) {
      const x = m.current.getBBox();
      h({
        x: x.x,
        y: x.y,
        width: x.width,
        height: x.height
      });
    }
  }, [n]), n ? c.jsxs("g", { transform: `translate(${e - f.width / 2} ${t - f.height / 2})`, className: g, visibility: f.width ? "visible" : "hidden", ...d, children: [r && c.jsx("rect", { width: f.width + 2 * s[0], x: -s[0], y: -s[1], height: f.height + 2 * s[1], className: "react-flow__edge-textbg", style: i, rx: l, ry: l }), c.jsx("text", { className: "react-flow__edge-text", y: f.height / 2, dy: "0.3em", ref: m, style: o, children: n }), u] }) : null;
}
Mc.displayName = "EdgeText";
const jg = be(Mc);
function zn({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: l, labelBgBorderRadius: u, interactionWidth: a = 20, ...d }) {
  return c.jsxs(c.Fragment, { children: [c.jsx("path", { ...d, d: e, fill: "none", className: Ne(["react-flow__edge-path", d.className]) }), a ? c.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: a, className: "react-flow__edge-interaction" }) : null, o && qe(t) && qe(n) ? c.jsx(jg, { x: t, y: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: l, labelBgBorderRadius: u }) : null] });
}
function Ps({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === te.Left || e === te.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function Dc({ sourceX: e, sourceY: t, sourcePosition: n = te.Bottom, targetX: o, targetY: r, targetPosition: i = te.Top }) {
  const [s, l] = Ps({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [u, a] = Ps({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [d, f, h, g] = Ga({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: r,
    sourceControlX: s,
    sourceControlY: l,
    targetControlX: u,
    targetControlY: a
  });
  return [
    `M${e},${t} C${s},${l} ${u},${a} ${o},${r}`,
    d,
    f,
    h,
    g
  ];
}
function Pc(e) {
  return be(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: l, label: u, labelStyle: a, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: g, style: m, markerEnd: x, markerStart: v, interactionWidth: y }) => {
    const [b, p, w] = Dc({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: l
    }), E = e.isInternal ? void 0 : t;
    return c.jsx(zn, { id: E, path: b, labelX: p, labelY: w, label: u, labelStyle: a, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: g, style: m, markerEnd: x, markerStart: v, interactionWidth: y });
  });
}
const Ag = Pc({ isInternal: !1 }), $c = Pc({ isInternal: !0 });
Ag.displayName = "SimpleBezierEdge";
$c.displayName = "SimpleBezierEdgeInternal";
function Tc(e) {
  return be(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: l, labelShowBg: u, labelBgStyle: a, labelBgPadding: d, labelBgBorderRadius: f, style: h, sourcePosition: g = te.Bottom, targetPosition: m = te.Top, markerEnd: x, markerStart: v, pathOptions: y, interactionWidth: b }) => {
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
    return c.jsx(zn, { id: k, path: p, labelX: w, labelY: E, label: s, labelStyle: l, labelShowBg: u, labelBgStyle: a, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: x, markerStart: v, interactionWidth: b });
  });
}
const zc = Tc({ isInternal: !1 }), Lc = Tc({ isInternal: !0 });
zc.displayName = "SmoothStepEdge";
Lc.displayName = "SmoothStepEdgeInternal";
function Rc(e) {
  return be(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return c.jsx(zc, { ...n, id: o, pathOptions: ge(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Mg = Rc({ isInternal: !1 }), Hc = Rc({ isInternal: !0 });
Mg.displayName = "StepEdge";
Hc.displayName = "StepEdgeInternal";
function Vc(e) {
  return be(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: l, labelShowBg: u, labelBgStyle: a, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: g, markerStart: m, interactionWidth: x }) => {
    const [v, y, b] = tc({ sourceX: n, sourceY: o, targetX: r, targetY: i }), p = e.isInternal ? void 0 : t;
    return c.jsx(zn, { id: p, path: v, labelX: y, labelY: b, label: s, labelStyle: l, labelShowBg: u, labelBgStyle: a, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: g, markerStart: m, interactionWidth: x });
  });
}
const Dg = Vc({ isInternal: !1 }), Oc = Vc({ isInternal: !0 });
Dg.displayName = "StraightEdge";
Oc.displayName = "StraightEdgeInternal";
function Bc(e) {
  return be(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s = te.Bottom, targetPosition: l = te.Top, label: u, labelStyle: a, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: g, style: m, markerEnd: x, markerStart: v, pathOptions: y, interactionWidth: b }) => {
    const [p, w, E] = Qa({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: l,
      curvature: y?.curvature
    }), k = e.isInternal ? void 0 : t;
    return c.jsx(zn, { id: k, path: p, labelX: w, labelY: E, label: u, labelStyle: a, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: g, style: m, markerEnd: x, markerStart: v, interactionWidth: b });
  });
}
const Pg = Bc({ isInternal: !1 }), Fc = Bc({ isInternal: !0 });
Pg.displayName = "BezierEdge";
Fc.displayName = "BezierEdgeInternal";
const $s = {
  default: Fc,
  straight: Oc,
  step: Hc,
  smoothstep: Lc,
  simplebezier: $c
}, Ts = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, $g = (e, t, n) => n === te.Left ? e - t : n === te.Right ? e + t : e, Tg = (e, t, n) => n === te.Top ? e - t : n === te.Bottom ? e + t : e, zs = "react-flow__edgeupdater";
function Ls({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: i, onMouseOut: s, type: l }) {
  return c.jsx("circle", { onMouseDown: r, onMouseEnter: i, onMouseOut: s, className: Ne([zs, `${zs}-${l}`]), cx: $g(t, o, e), cy: Tg(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function zg({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: i, targetY: s, sourcePosition: l, targetPosition: u, onReconnect: a, onReconnectStart: d, onReconnectEnd: f, setReconnecting: h, setUpdateHover: g }) {
  const m = ye(), x = (w, E) => {
    if (w.button !== 0)
      return;
    const { autoPanOnConnect: k, domNode: _, connectionMode: D, connectionRadius: $, lib: W, onConnectStart: j, cancelConnection: z, nodeLookup: H, rfId: S, panBy: I, updateConnection: C } = m.getState(), A = E.type === "target", T = (F, O) => {
      h(!1), f?.(F, n, E.type, O);
    }, P = (F) => a?.(n, F), V = (F, O) => {
      h(!0), d?.(w, n, E.type), j?.(F, O);
    };
    Gr.onPointerDown(w.nativeEvent, {
      autoPanOnConnect: k,
      connectionMode: D,
      connectionRadius: $,
      domNode: _,
      handleId: E.id,
      nodeId: E.nodeId,
      nodeLookup: H,
      isTarget: A,
      edgeUpdaterType: E.type,
      lib: W,
      flowId: S,
      cancelConnection: z,
      panBy: I,
      isValidConnection: (...F) => m.getState().isValidConnection?.(...F) ?? !0,
      onConnect: P,
      onConnectStart: V,
      onConnectEnd: (...F) => m.getState().onConnectEnd?.(...F),
      onReconnectEnd: T,
      updateConnection: C,
      getTransform: () => m.getState().transform,
      getFromHandle: () => m.getState().connection.fromHandle,
      dragThreshold: m.getState().connectionDragThreshold,
      handleDomNode: w.currentTarget
    });
  }, v = (w) => x(w, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), y = (w) => x(w, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), b = () => g(!0), p = () => g(!1);
  return c.jsxs(c.Fragment, { children: [(e === !0 || e === "source") && c.jsx(Ls, { position: l, centerX: o, centerY: r, radius: t, onMouseDown: v, onMouseEnter: b, onMouseOut: p, type: "source" }), (e === !0 || e === "target") && c.jsx(Ls, { position: u, centerX: i, centerY: s, radius: t, onMouseDown: y, onMouseEnter: b, onMouseOut: p, type: "target" })] });
}
function Lg({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: i, onContextMenu: s, onMouseEnter: l, onMouseMove: u, onMouseLeave: a, reconnectRadius: d, onReconnect: f, onReconnectStart: h, onReconnectEnd: g, rfId: m, edgeTypes: x, noPanClassName: v, onError: y, disableKeyboardA11y: b }) {
  let p = ue((Z) => Z.edgeLookup.get(e));
  const w = ue((Z) => Z.defaultEdgeOptions);
  p = w ? { ...w, ...p } : p;
  let E = p.type || "default", k = x?.[E] || $s[E];
  k === void 0 && (y?.("011", He.error011(E)), E = "default", k = x?.default || $s.default);
  const _ = !!(p.focusable || t && typeof p.focusable > "u"), D = typeof f < "u" && (p.reconnectable || n && typeof p.reconnectable > "u"), $ = !!(p.selectable || o && typeof p.selectable > "u"), W = ce(null), [j, z] = q(!1), [H, S] = q(!1), I = ye(), { zIndex: C, sourceX: A, sourceY: T, targetX: P, targetY: V, sourcePosition: F, targetPosition: O } = ue(pe((Z) => {
    const ee = Z.nodeLookup.get(p.source), oe = Z.nodeLookup.get(p.target);
    if (!ee || !oe)
      return {
        zIndex: p.zIndex,
        ...Ts
      };
    const R = kh({
      id: e,
      sourceNode: ee,
      targetNode: oe,
      sourceHandle: p.sourceHandle || null,
      targetHandle: p.targetHandle || null,
      connectionMode: Z.connectionMode,
      onError: y
    });
    return {
      zIndex: yh({
        selected: p.selected,
        zIndex: p.zIndex,
        sourceNode: ee,
        targetNode: oe,
        elevateOnSelect: Z.elevateEdgesOnSelect,
        zIndexMode: Z.zIndexMode
      }),
      ...R || Ts
    };
  }, [p.source, p.target, p.sourceHandle, p.targetHandle, p.selected, p.zIndex]), me), G = ge(() => p.markerStart ? `url('#${Kr(p.markerStart, m)}')` : void 0, [p.markerStart, m]), K = ge(() => p.markerEnd ? `url('#${Kr(p.markerEnd, m)}')` : void 0, [p.markerEnd, m]);
  if (p.hidden || A === null || T === null || P === null || V === null)
    return null;
  const ne = (Z) => {
    const { addSelectedEdges: ee, unselectNodesAndEdges: oe, multiSelectionActive: R } = I.getState();
    $ && (I.setState({ nodesSelectionActive: !1 }), p.selected && R ? (oe({ nodes: [], edges: [p] }), W.current?.blur()) : ee([e])), r && r(Z, p);
  }, ae = i ? (Z) => {
    i(Z, { ...p });
  } : void 0, U = s ? (Z) => {
    s(Z, { ...p });
  } : void 0, L = l ? (Z) => {
    l(Z, { ...p });
  } : void 0, Y = u ? (Z) => {
    u(Z, { ...p });
  } : void 0, re = a ? (Z) => {
    a(Z, { ...p });
  } : void 0, ie = (Z) => {
    if (!b && La.includes(Z.key) && $) {
      const { unselectNodesAndEdges: ee, addSelectedEdges: oe } = I.getState();
      Z.key === "Escape" ? (W.current?.blur(), ee({ edges: [p] })) : oe([e]);
    }
  };
  return c.jsx("svg", { style: { zIndex: C }, children: c.jsxs("g", { className: Ne([
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
  ]), onClick: ne, onDoubleClick: ae, onContextMenu: U, onMouseEnter: L, onMouseMove: Y, onMouseLeave: re, onKeyDown: _ ? ie : void 0, tabIndex: _ ? 0 : void 0, role: p.ariaRole ?? (_ ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": p.ariaLabel === null ? void 0 : p.ariaLabel || `Edge from ${p.source} to ${p.target}`, "aria-describedby": _ ? `${mc}-${m}` : void 0, ref: W, ...p.domAttributes, children: [!H && c.jsx(k, { id: e, source: p.source, target: p.target, type: p.type, selected: p.selected, animated: p.animated, selectable: $, deletable: p.deletable ?? !0, label: p.label, labelStyle: p.labelStyle, labelShowBg: p.labelShowBg, labelBgStyle: p.labelBgStyle, labelBgPadding: p.labelBgPadding, labelBgBorderRadius: p.labelBgBorderRadius, sourceX: A, sourceY: T, targetX: P, targetY: V, sourcePosition: F, targetPosition: O, data: p.data, style: p.style, sourceHandleId: p.sourceHandle, targetHandleId: p.targetHandle, markerStart: G, markerEnd: K, pathOptions: "pathOptions" in p ? p.pathOptions : void 0, interactionWidth: p.interactionWidth }), D && c.jsx(zg, { edge: p, isReconnectable: D, reconnectRadius: d, onReconnect: f, onReconnectStart: h, onReconnectEnd: g, sourceX: A, sourceY: T, targetX: P, targetY: V, sourcePosition: F, targetPosition: O, setUpdateHover: z, setReconnecting: S })] }) });
}
var Rg = be(Lg);
const Hg = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function Wc({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: i, onEdgeContextMenu: s, onEdgeMouseEnter: l, onEdgeMouseMove: u, onEdgeMouseLeave: a, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: h, onReconnectStart: g, onReconnectEnd: m, disableKeyboardA11y: x }) {
  const { edgesFocusable: v, edgesReconnectable: y, elementsSelectable: b, onError: p } = ue(Hg, me), w = Ng(t);
  return c.jsxs("div", { className: "react-flow__edges", children: [c.jsx(Ig, { defaultColor: e, rfId: n }), w.map((E) => c.jsx(Rg, { id: E, edgesFocusable: v, edgesReconnectable: y, elementsSelectable: b, noPanClassName: r, onReconnect: i, onContextMenu: s, onMouseEnter: l, onMouseMove: u, onMouseLeave: a, onClick: d, reconnectRadius: f, onDoubleClick: h, onReconnectStart: g, onReconnectEnd: m, rfId: n, onError: p, edgeTypes: o, disableKeyboardA11y: x }, E))] });
}
Wc.displayName = "EdgeRenderer";
const Vg = be(Wc), Og = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function Bg({ children: e }) {
  const t = ue(Og);
  return c.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function Fg(e) {
  const t = Si(), n = ce(!1);
  se(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Wg = (e) => e.panZoom?.syncViewport;
function Xg(e) {
  const t = ue(Wg), n = ye();
  return se(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function Yg(e) {
  return e.connection.inProgress ? { ...e.connection, to: Yt(e.connection.to, e.transform) } : { ...e.connection };
}
function qg(e) {
  return Yg;
}
function Zg(e) {
  const t = qg();
  return ue(t, me);
}
const Kg = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function Ug({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: i, height: s, isValid: l, inProgress: u } = ue(Kg, me);
  return !(i && r && u) ? null : c.jsx("svg", { style: e, width: i, height: s, className: "react-flow__connectionline react-flow__container", children: c.jsx("g", { className: Ne(["react-flow__connection", Va(l)]), children: c.jsx(Xc, { style: t, type: n, CustomComponent: o, isValid: l }) }) });
}
const Xc = ({ style: e, type: t = ct.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: i, fromNode: s, fromHandle: l, fromPosition: u, to: a, toNode: d, toHandle: f, toPosition: h, pointer: g } = Zg();
  if (!r)
    return;
  if (n)
    return c.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: s, fromHandle: l, fromX: i.x, fromY: i.y, toX: a.x, toY: a.y, fromPosition: u, toPosition: h, connectionStatus: Va(o), toNode: d, toHandle: f, pointer: g });
  let m = "";
  const x = {
    sourceX: i.x,
    sourceY: i.y,
    sourcePosition: u,
    targetX: a.x,
    targetY: a.y,
    targetPosition: h
  };
  switch (t) {
    case ct.Bezier:
      [m] = Qa(x);
      break;
    case ct.SimpleBezier:
      [m] = Dc(x);
      break;
    case ct.Step:
      [m] = Po({
        ...x,
        borderRadius: 0
      });
      break;
    case ct.SmoothStep:
      [m] = Po(x);
      break;
    default:
      [m] = tc(x);
  }
  return c.jsx("path", { d: m, fill: "none", className: "react-flow__connection-path", style: e });
};
Xc.displayName = "ConnectionLine";
const Gg = {};
function Rs(e = Gg) {
  ce(e), ye(), se(() => {
  }, [e]);
}
function Qg() {
  ye(), ce(!1), se(() => {
  }, []);
}
function Yc({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: i, onEdgeDoubleClick: s, onNodeMouseEnter: l, onNodeMouseMove: u, onNodeMouseLeave: a, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: h, onSelectionEnd: g, connectionLineType: m, connectionLineStyle: x, connectionLineComponent: v, connectionLineContainerStyle: y, selectionKeyCode: b, selectionOnDrag: p, selectionMode: w, multiSelectionKeyCode: E, panActivationKeyCode: k, zoomActivationKeyCode: _, deleteKeyCode: D, onlyRenderVisibleElements: $, elementsSelectable: W, defaultViewport: j, translateExtent: z, minZoom: H, maxZoom: S, preventScrolling: I, defaultMarkerColor: C, zoomOnScroll: A, zoomOnPinch: T, panOnScroll: P, panOnScrollSpeed: V, panOnScrollMode: F, zoomOnDoubleClick: O, panOnDrag: G, autoPanOnSelection: K, onPaneClick: ne, onPaneMouseEnter: ae, onPaneMouseMove: U, onPaneMouseLeave: L, onPaneScroll: Y, onPaneContextMenu: re, paneClickDistance: ie, nodeClickDistance: Z, onEdgeContextMenu: ee, onEdgeMouseEnter: oe, onEdgeMouseMove: R, onEdgeMouseLeave: J, reconnectRadius: fe, onReconnect: we, onReconnectStart: Oe, onReconnectEnd: ve, noDragClassName: De, noWheelClassName: tt, noPanClassName: Fe, disableKeyboardA11y: Be, nodeExtent: Ce, rfId: Ae, viewport: Me, onViewportChange: Ue }) {
  return Rs(e), Rs(t), Qg(), Fg(n), Xg(Me), c.jsx(hg, { onPaneClick: ne, onPaneMouseEnter: ae, onPaneMouseMove: U, onPaneMouseLeave: L, onPaneContextMenu: re, onPaneScroll: Y, paneClickDistance: ie, deleteKeyCode: D, selectionKeyCode: b, selectionOnDrag: p, selectionMode: w, onSelectionStart: h, onSelectionEnd: g, multiSelectionKeyCode: E, panActivationKeyCode: k, zoomActivationKeyCode: _, elementsSelectable: W, zoomOnScroll: A, zoomOnPinch: T, zoomOnDoubleClick: O, panOnScroll: P, panOnScrollSpeed: V, panOnScrollMode: F, panOnDrag: G, autoPanOnSelection: K, defaultViewport: j, translateExtent: z, minZoom: H, maxZoom: S, onSelectionContextMenu: f, preventScrolling: I, noDragClassName: De, noWheelClassName: tt, noPanClassName: Fe, disableKeyboardA11y: Be, onViewportChange: Ue, isControlledViewport: !!Me, children: c.jsxs(Bg, { children: [c.jsx(Vg, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: s, onReconnect: we, onReconnectStart: Oe, onReconnectEnd: ve, onlyRenderVisibleElements: $, onEdgeContextMenu: ee, onEdgeMouseEnter: oe, onEdgeMouseMove: R, onEdgeMouseLeave: J, reconnectRadius: fe, defaultMarkerColor: C, noPanClassName: Fe, disableKeyboardA11y: Be, rfId: Ae }), c.jsx(Ug, { style: x, type: m, component: v, containerStyle: y }), c.jsx("div", { className: "react-flow__edgelabel-renderer" }), c.jsx(Sg, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: i, onNodeMouseEnter: l, onNodeMouseMove: u, onNodeMouseLeave: a, onNodeContextMenu: d, nodeClickDistance: Z, onlyRenderVisibleElements: $, noPanClassName: Fe, noDragClassName: De, disableKeyboardA11y: Be, nodeExtent: Ce, rfId: Ae }), c.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Yc.displayName = "GraphView";
const Jg = be(Yc), em = Xa(), Hs = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: l, minZoom: u = 0.5, maxZoom: a = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: h = "basic" } = {}) => {
  const g = /* @__PURE__ */ new Map(), m = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), v = /* @__PURE__ */ new Map(), y = o ?? t ?? [], b = n ?? e ?? [], p = d ?? [0, 0], w = f ?? En;
  rc(x, v, y);
  const { nodesInitialized: E } = Ur(b, g, m, {
    nodeOrigin: p,
    nodeExtent: w,
    zIndexMode: h
  });
  let k = [0, 0, 1];
  if (s && r && i) {
    const _ = $n(g, {
      filter: (j) => !!((j.width || j.initialWidth) && (j.height || j.initialHeight))
    }), { x: D, y: $, zoom: W } = gi(_, r, i, u, a, l?.padding ?? 0.1);
    k = [D, $, W];
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
    maxZoom: a,
    translateExtent: En,
    nodeExtent: w,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: Lt.Strict,
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
    connection: { ...Ha },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: em,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Ra,
    zIndexMode: h,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, tm = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: l, minZoom: u, maxZoom: a, nodeOrigin: d, nodeExtent: f, zIndexMode: h }) => pp((g, m) => {
  async function x() {
    const { nodeLookup: v, panZoom: y, fitViewOptions: b, fitViewResolver: p, width: w, height: E, minZoom: k, maxZoom: _ } = m();
    y && (await uh({
      nodes: v,
      width: w,
      height: E,
      panZoom: y,
      minZoom: k,
      maxZoom: _
    }, b), p?.resolve(!0), g({ fitViewResolver: null }));
  }
  return {
    ...Hs({
      nodes: e,
      edges: t,
      width: r,
      height: i,
      fitView: s,
      fitViewOptions: l,
      minZoom: u,
      maxZoom: a,
      nodeOrigin: d,
      nodeExtent: f,
      defaultNodes: n,
      defaultEdges: o,
      zIndexMode: h
    }),
    setNodes: (v) => {
      const { nodeLookup: y, parentLookup: b, nodeOrigin: p, elevateNodesOnSelect: w, fitViewQueued: E, zIndexMode: k, nodesSelectionActive: _ } = m(), { nodesInitialized: D, hasSelectedNodes: $ } = Ur(v, y, b, {
        nodeOrigin: p,
        nodeExtent: f,
        elevateNodesOnSelect: w,
        checkEquality: !0,
        zIndexMode: k
      }), W = _ && $;
      E && D ? (x(), g({
        nodes: v,
        nodesInitialized: D,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: W
      })) : g({ nodes: v, nodesInitialized: D, nodesSelectionActive: W });
    },
    setEdges: (v) => {
      const { connectionLookup: y, edgeLookup: b } = m();
      rc(y, b, v), g({ edges: v });
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
      const { triggerNodeChanges: y, nodeLookup: b, parentLookup: p, domNode: w, nodeOrigin: E, nodeExtent: k, debug: _, fitViewQueued: D, zIndexMode: $ } = m(), { changes: W, updatedInternals: j } = Ph(v, b, p, w, E, k, $);
      j && (jh(b, p, { nodeOrigin: E, nodeExtent: k, zIndexMode: $ }), D ? (x(), g({ fitViewQueued: !1, fitViewOptions: void 0 })) : g({}), W?.length > 0 && (_ && console.log("React Flow: trigger node changes", W), y?.(W)));
    },
    updateNodePositions: (v, y = !1) => {
      const b = [];
      let p = [];
      const { nodeLookup: w, triggerNodeChanges: E, connection: k, updateConnection: _, onNodesChangeMiddlewareMap: D } = m();
      for (const [$, W] of v) {
        const j = w.get($), z = !!(j?.expandParent && j?.parentId && W?.position), H = {
          id: $,
          type: "position",
          position: z ? {
            x: Math.max(0, W.position.x),
            y: Math.max(0, W.position.y)
          } : W.position,
          dragging: y
        };
        if (j && k.inProgress && k.fromNode.id === j.id) {
          const S = St(j, k.fromHandle, te.Left, !0);
          _({ ...k, from: S });
        }
        z && j.parentId && b.push({
          id: $,
          parentId: j.parentId,
          rect: {
            ...W.internals.positionAbsolute,
            width: W.measured.width ?? 0,
            height: W.measured.height ?? 0
          }
        }), p.push(H);
      }
      if (b.length > 0) {
        const { parentLookup: $, nodeOrigin: W } = m(), j = bi(b, w, $, W);
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
          const k = wc(v, p);
          b(k);
        }
        E && console.log("React Flow: trigger node changes", v), y?.(v);
      }
    },
    triggerEdgeChanges: (v) => {
      const { onEdgesChange: y, setEdges: b, edges: p, hasDefaultEdges: w, debug: E } = m();
      if (v?.length) {
        if (w) {
          const k = vc(v, p);
          b(k);
        }
        E && console.log("React Flow: trigger edge changes", v), y?.(v);
      }
    },
    addSelectedNodes: (v) => {
      const { multiSelectionActive: y, edgeLookup: b, nodeLookup: p, triggerNodeChanges: w, triggerEdgeChanges: E } = m();
      if (y) {
        const k = v.map((_) => pt(_, !0));
        w(k);
        return;
      }
      w(Dt(p, /* @__PURE__ */ new Set([...v]), !0)), E(Dt(b));
    },
    addSelectedEdges: (v) => {
      const { multiSelectionActive: y, edgeLookup: b, nodeLookup: p, triggerNodeChanges: w, triggerEdgeChanges: E } = m();
      if (y) {
        const k = v.map((_) => pt(_, !0));
        E(k);
        return;
      }
      E(Dt(b, /* @__PURE__ */ new Set([...v]))), w(Dt(p, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: v, edges: y } = {}) => {
      const { edges: b, nodes: p, nodeLookup: w, triggerNodeChanges: E, triggerEdgeChanges: k } = m(), _ = v || p, D = y || b, $ = [];
      for (const j of _) {
        if (!j.selected)
          continue;
        const z = w.get(j.id);
        z && (z.selected = !1), $.push(pt(j.id, !1));
      }
      const W = [];
      for (const j of D)
        j.selected && W.push(pt(j.id, !1));
      E($), k(W);
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
      const E = y.reduce((_, D) => D.selected ? [..._, pt(D.id, !1)] : _, []), k = v.reduce((_, D) => D.selected ? [..._, pt(D.id, !1)] : _, []);
      b(E), p(k);
    },
    setNodeExtent: (v) => {
      const { nodes: y, nodeLookup: b, parentLookup: p, nodeOrigin: w, elevateNodesOnSelect: E, nodeExtent: k, zIndexMode: _ } = m();
      v[0][0] === k[0][0] && v[0][1] === k[0][1] && v[1][0] === k[1][0] && v[1][1] === k[1][1] || (Ur(y, b, p, {
        nodeOrigin: w,
        nodeExtent: v,
        elevateNodesOnSelect: E,
        checkEquality: !1,
        zIndexMode: _
      }), g({ nodeExtent: v }));
    },
    panBy: (v) => {
      const { transform: y, width: b, height: p, panZoom: w, translateExtent: E } = m();
      return $h({ delta: v, panZoom: w, transform: y, translateExtent: E, width: b, height: p });
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
        connection: { ...Ha }
      });
    },
    updateConnection: (v) => {
      g({ connection: v });
    },
    reset: () => g({ ...Hs() })
  };
}, Object.is);
function nm({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: i, initialMinZoom: s, initialMaxZoom: l, initialFitViewOptions: u, fitView: a, nodeOrigin: d, nodeExtent: f, zIndexMode: h, children: g }) {
  const [m] = q(() => tm({
    nodes: e,
    edges: t,
    defaultNodes: n,
    defaultEdges: o,
    width: r,
    height: i,
    fitView: a,
    minZoom: s,
    maxZoom: l,
    fitViewOptions: u,
    nodeOrigin: d,
    nodeExtent: f,
    zIndexMode: h
  }));
  return c.jsx(xp, { value: m, children: c.jsx(Bp, { children: g }) });
}
function om({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: i, height: s, fitView: l, fitViewOptions: u, minZoom: a, maxZoom: d, nodeOrigin: f, nodeExtent: h, zIndexMode: g }) {
  return An(qo) ? c.jsx(c.Fragment, { children: e }) : c.jsx(nm, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: i, initialHeight: s, fitView: l, initialFitViewOptions: u, initialMinZoom: a, initialMaxZoom: d, nodeOrigin: f, nodeExtent: h, zIndexMode: g, children: e });
}
const rm = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function im({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: i, edgeTypes: s, onNodeClick: l, onEdgeClick: u, onInit: a, onMove: d, onMoveStart: f, onMoveEnd: h, onConnect: g, onConnectStart: m, onConnectEnd: x, onClickConnectStart: v, onClickConnectEnd: y, onNodeMouseEnter: b, onNodeMouseMove: p, onNodeMouseLeave: w, onNodeContextMenu: E, onNodeDoubleClick: k, onNodeDragStart: _, onNodeDrag: D, onNodeDragStop: $, onNodesDelete: W, onEdgesDelete: j, onDelete: z, onSelectionChange: H, onSelectionDragStart: S, onSelectionDrag: I, onSelectionDragStop: C, onSelectionContextMenu: A, onSelectionStart: T, onSelectionEnd: P, onBeforeDelete: V, connectionMode: F, connectionLineType: O = ct.Bezier, connectionLineStyle: G, connectionLineComponent: K, connectionLineContainerStyle: ne, deleteKeyCode: ae = "Backspace", selectionKeyCode: U = "Shift", selectionOnDrag: L = !1, selectionMode: Y = kn.Full, panActivationKeyCode: re = "Space", multiSelectionKeyCode: ie = _n() ? "Meta" : "Control", zoomActivationKeyCode: Z = _n() ? "Meta" : "Control", snapToGrid: ee, snapGrid: oe, onlyRenderVisibleElements: R = !1, selectNodesOnDrag: J, nodesDraggable: fe, autoPanOnNodeFocus: we, nodesConnectable: Oe, nodesFocusable: ve, nodeOrigin: De = yc, edgesFocusable: tt, edgesReconnectable: Fe, elementsSelectable: Be = !0, defaultViewport: Ce = Mp, minZoom: Ae = 0.5, maxZoom: Me = 2, translateExtent: Ue = En, preventScrolling: Te = !0, nodeExtent: Et, defaultMarkerColor: xe = "#b1b1b7", zoomOnScroll: We = !0, zoomOnPinch: qt = !0, panOnScroll: Ee = !1, panOnScrollSpeed: Zt = 0.5, panOnScrollMode: Kt = yt.Free, zoomOnDoubleClick: _e = !0, panOnDrag: ft = !0, onPaneClick: Rn, onPaneMouseEnter: Hn, onPaneMouseMove: Ut, onPaneMouseLeave: Jo, onPaneScroll: Gt, onPaneContextMenu: kt, paneClickDistance: ze = 1, nodeClickDistance: Qt = 0, children: Jt, onReconnect: en, onReconnectStart: Ct, onReconnectEnd: er, onEdgeContextMenu: tr, onEdgeDoubleClick: nr, onEdgeMouseEnter: or, onEdgeMouseMove: Vn, onEdgeMouseLeave: On, reconnectRadius: Bn = 10, onNodesChange: rr, onEdgesChange: tn, noDragClassName: ir = "nodrag", noWheelClassName: sr = "nowheel", noPanClassName: Fn = "nopan", fitView: Wn, fitViewOptions: Xn, connectOnClick: nn, attributionPosition: ar, proOptions: cr, defaultEdgeOptions: lr, elevateNodesOnSelect: ur = !0, elevateEdgesOnSelect: dr = !1, disableKeyboardA11y: Yn = !1, autoPanOnConnect: fr, autoPanOnNodeDrag: qn, autoPanOnSelection: Zn = !0, autoPanSpeed: hr, connectionRadius: pr, isValidConnection: gr, onError: mr, style: yr, id: on, nodeDragThreshold: Kn, connectionDragThreshold: Un, viewport: Gn, onViewportChange: Qn, width: xr, height: wr, colorMode: Jn = "light", debug: eo, onScroll: to, ariaLabelConfig: no, zIndexMode: _t = "basic", ...rn }, oo) {
  const It = on || "1", vr = Tp(Jn), br = pe((N) => {
    N.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), to?.(N);
  }, [to]);
  return c.jsx("div", { "data-testid": "rf__wrapper", ...rn, onScroll: br, style: { ...yr, ...rm }, ref: oo, className: Ne(["react-flow", r, vr]), id: on, role: "application", children: c.jsxs(om, { nodes: e, edges: t, width: xr, height: wr, fitView: Wn, fitViewOptions: Xn, minZoom: Ae, maxZoom: Me, nodeOrigin: De, nodeExtent: Et, zIndexMode: _t, children: [c.jsx($p, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: g, onConnectStart: m, onConnectEnd: x, onClickConnectStart: v, onClickConnectEnd: y, nodesDraggable: fe, autoPanOnNodeFocus: we, nodesConnectable: Oe, nodesFocusable: ve, edgesFocusable: tt, edgesReconnectable: Fe, elementsSelectable: Be, elevateNodesOnSelect: ur, elevateEdgesOnSelect: dr, minZoom: Ae, maxZoom: Me, nodeExtent: Et, onNodesChange: rr, onEdgesChange: tn, snapToGrid: ee, snapGrid: oe, connectionMode: F, translateExtent: Ue, connectOnClick: nn, defaultEdgeOptions: lr, fitView: Wn, fitViewOptions: Xn, onNodesDelete: W, onEdgesDelete: j, onDelete: z, onNodeDragStart: _, onNodeDrag: D, onNodeDragStop: $, onSelectionDrag: I, onSelectionDragStart: S, onSelectionDragStop: C, onMove: d, onMoveStart: f, onMoveEnd: h, noPanClassName: Fn, nodeOrigin: De, rfId: It, autoPanOnConnect: fr, autoPanOnNodeDrag: qn, autoPanSpeed: hr, onError: mr, connectionRadius: pr, isValidConnection: gr, selectNodesOnDrag: J, nodeDragThreshold: Kn, connectionDragThreshold: Un, onBeforeDelete: V, debug: eo, ariaLabelConfig: no, zIndexMode: _t }), c.jsx(Jg, { onInit: a, onNodeClick: l, onEdgeClick: u, onNodeMouseEnter: b, onNodeMouseMove: p, onNodeMouseLeave: w, onNodeContextMenu: E, onNodeDoubleClick: k, nodeTypes: i, edgeTypes: s, connectionLineType: O, connectionLineStyle: G, connectionLineComponent: K, connectionLineContainerStyle: ne, selectionKeyCode: U, selectionOnDrag: L, selectionMode: Y, deleteKeyCode: ae, multiSelectionKeyCode: ie, panActivationKeyCode: re, zoomActivationKeyCode: Z, onlyRenderVisibleElements: R, defaultViewport: Ce, translateExtent: Ue, minZoom: Ae, maxZoom: Me, preventScrolling: Te, zoomOnScroll: We, zoomOnPinch: qt, zoomOnDoubleClick: _e, panOnScroll: Ee, panOnScrollSpeed: Zt, panOnScrollMode: Kt, panOnDrag: ft, autoPanOnSelection: Zn, onPaneClick: Rn, onPaneMouseEnter: Hn, onPaneMouseMove: Ut, onPaneMouseLeave: Jo, onPaneScroll: Gt, onPaneContextMenu: kt, paneClickDistance: ze, nodeClickDistance: Qt, onSelectionContextMenu: A, onSelectionStart: T, onSelectionEnd: P, onReconnect: en, onReconnectStart: Ct, onReconnectEnd: er, onEdgeContextMenu: tr, onEdgeDoubleClick: nr, onEdgeMouseEnter: or, onEdgeMouseMove: Vn, onEdgeMouseLeave: On, reconnectRadius: Bn, defaultMarkerColor: xe, noDragClassName: ir, noWheelClassName: sr, noPanClassName: Fn, rfId: It, disableKeyboardA11y: Yn, nodeExtent: Et, viewport: Gn, onViewportChange: Qn }), c.jsx(Ap, { onSelectionChange: H }), Jt, c.jsx(kp, { proOptions: cr, position: ar }), c.jsx(Ep, { rfId: It, disableKeyboardA11y: Yn })] }) });
}
var sm = Nc(im);
const am = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function cm({ children: e }) {
  const t = ue(am);
  return t ? yp.createPortal(e, t) : null;
}
function lm({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return c.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ne(["react-flow__background-pattern", n, o]) });
}
function um({ radius: e, className: t }) {
  return c.jsx("circle", { cx: e, cy: e, r: e, className: Ne(["react-flow__background-pattern", "dots", t]) });
}
var lt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(lt || (lt = {}));
const dm = {
  [lt.Dots]: 1,
  [lt.Lines]: 1,
  [lt.Cross]: 6
}, fm = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function qc({
  id: e,
  variant: t = lt.Dots,
  // only used for dots and cross
  gap: n = 20,
  // only used for lines and cross
  size: o,
  lineWidth: r = 1,
  offset: i = 0,
  color: s,
  bgColor: l,
  style: u,
  className: a,
  patternClassName: d
}) {
  const f = ce(null), { transform: h, patternId: g } = ue(fm, me), m = o || dm[t], x = t === lt.Dots, v = t === lt.Cross, y = Array.isArray(n) ? n : [n, n], b = [y[0] * h[2] || 1, y[1] * h[2] || 1], p = m * h[2], w = Array.isArray(i) ? i : [i, i], E = v ? [p, p] : b, k = [
    w[0] * h[2] || 1 + E[0] / 2,
    w[1] * h[2] || 1 + E[1] / 2
  ], _ = `${g}${e || ""}`;
  return c.jsxs("svg", { className: Ne(["react-flow__background", a]), style: {
    ...u,
    ...Ko,
    "--xy-background-color-props": l,
    "--xy-background-pattern-color-props": s
  }, ref: f, "data-testid": "rf__background", children: [c.jsx("pattern", { id: _, x: h[0] % b[0], y: h[1] % b[1], width: b[0], height: b[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${k[0]},-${k[1]})`, children: x ? c.jsx(um, { radius: p / 2, className: d }) : c.jsx(lm, { dimensions: E, lineWidth: r, variant: t, className: d }) }), c.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${_})` })] });
}
qc.displayName = "Background";
const hm = be(qc);
function pm() {
  return c.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: c.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function gm() {
  return c.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: c.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function mm() {
  return c.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: c.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function ym() {
  return c.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: c.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function xm() {
  return c.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: c.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function ho({ children: e, className: t, ...n }) {
  return c.jsx("button", { type: "button", className: Ne(["react-flow__controls-button", t]), ...n, children: e });
}
const wm = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function Zc({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: i, onZoomOut: s, onFitView: l, onInteractiveChange: u, className: a, children: d, position: f = "bottom-left", orientation: h = "vertical", "aria-label": g }) {
  const m = ye(), { isInteractive: x, minZoomReached: v, maxZoomReached: y, ariaLabelConfig: b } = ue(wm, me), { zoomIn: p, zoomOut: w, fitView: E } = Si(), k = () => {
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
  }, W = h === "horizontal" ? "horizontal" : "vertical";
  return c.jsxs(Zo, { className: Ne(["react-flow__controls", W, a]), position: f, style: e, "data-testid": "rf__controls", "aria-label": g ?? b["controls.ariaLabel"], children: [t && c.jsxs(c.Fragment, { children: [c.jsx(ho, { onClick: k, className: "react-flow__controls-zoomin", title: b["controls.zoomIn.ariaLabel"], "aria-label": b["controls.zoomIn.ariaLabel"], disabled: y, children: c.jsx(pm, {}) }), c.jsx(ho, { onClick: _, className: "react-flow__controls-zoomout", title: b["controls.zoomOut.ariaLabel"], "aria-label": b["controls.zoomOut.ariaLabel"], disabled: v, children: c.jsx(gm, {}) })] }), n && c.jsx(ho, { className: "react-flow__controls-fitview", onClick: D, title: b["controls.fitView.ariaLabel"], "aria-label": b["controls.fitView.ariaLabel"], children: c.jsx(mm, {}) }), o && c.jsx(ho, { className: "react-flow__controls-interactive", onClick: $, title: b["controls.interactive.ariaLabel"], "aria-label": b["controls.interactive.ariaLabel"], children: x ? c.jsx(xm, {}) : c.jsx(ym, {}) }), d] });
}
Zc.displayName = "Controls";
const vm = be(Zc);
function bm({ id: e, x: t, y: n, width: o, height: r, style: i, color: s, strokeColor: l, strokeWidth: u, className: a, borderRadius: d, shapeRendering: f, selected: h, onClick: g }) {
  const { background: m, backgroundColor: x } = i || {}, v = s || m || x;
  return c.jsx("rect", { className: Ne(["react-flow__minimap-node", { selected: h }, a]), x: t, y: n, rx: d, ry: d, width: o, height: r, style: {
    fill: v,
    stroke: l,
    strokeWidth: u
  }, shapeRendering: f, onClick: g ? (y) => g(y, e) : void 0 });
}
const Sm = be(bm), Nm = (e) => e.nodes.map((t) => t.id), zr = (e) => e instanceof Function ? e : () => e;
function Em({
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
  const l = ue(Nm, me), u = zr(t), a = zr(e), d = zr(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return c.jsx(c.Fragment, { children: l.map((h) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    c.jsx(Cm, { id: h, nodeColorFunc: u, nodeStrokeColorFunc: a, nodeClassNameFunc: d, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: i, onClick: s, shapeRendering: f }, h)
  )) });
}
function km({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: i, shapeRendering: s, NodeComponent: l, onClick: u }) {
  const { node: a, x: d, y: f, width: h, height: g } = ue((m) => {
    const x = m.nodeLookup.get(e);
    if (!x)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const v = x.internals.userNode, { x: y, y: b } = x.internals.positionAbsolute, { width: p, height: w } = it(v);
    return {
      node: v,
      x: y,
      y: b,
      width: p,
      height: w
    };
  }, me);
  return !a || a.hidden || !Ya(a) ? null : c.jsx(l, { x: d, y: f, width: h, height: g, style: a.style, selected: !!a.selected, className: o(a), color: t(a), borderRadius: r, strokeColor: n(a), strokeWidth: i, shapeRendering: s, onClick: u, id: a.id });
}
const Cm = be(km);
var _m = be(Em);
const Im = 200, jm = 150, Am = (e) => !e.hidden, Mm = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? Wa($n(e.nodeLookup, { filter: Am }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, Dm = "react-flow__minimap-desc";
function Kc({
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
  maskColor: a,
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
  const E = ye(), k = ce(null), { boundingRect: _, viewBB: D, rfId: $, panZoom: W, translateExtent: j, flowWidth: z, flowHeight: H, ariaLabelConfig: S } = ue(Mm, me), I = e?.width ?? Im, C = e?.height ?? jm, A = _.width / I, T = _.height / C, P = Math.max(A, T), V = P * I, F = P * C, O = w * P, G = _.x - (V - _.width) / 2 - O, K = _.y - (F - _.height) / 2 - O, ne = V + O * 2, ae = F + O * 2, U = `${Dm}-${$}`, L = ce(0), Y = ce();
  L.current = P, se(() => {
    if (k.current && W)
      return Y.current = Fh({
        domNode: k.current,
        panZoom: W,
        getTransform: () => E.getState().transform,
        getViewScale: () => L.current
      }), () => {
        Y.current?.destroy();
      };
  }, [W]), se(() => {
    Y.current?.update({
      translateExtent: j,
      width: z,
      height: H,
      inversePan: b,
      pannable: x,
      zoomStep: p,
      zoomable: v
    });
  }, [x, v, b, p, j, z, H]);
  const re = g ? (ee) => {
    const [oe, R] = Y.current?.pointer(ee) || [0, 0];
    g(ee, { x: oe, y: R });
  } : void 0, ie = m ? pe((ee, oe) => {
    const R = E.getState().nodeLookup.get(oe).internals.userNode;
    m(ee, R);
  }, []) : void 0, Z = y ?? S["minimap.ariaLabel"];
  return c.jsx(Zo, { position: h, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof a == "string" ? a : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * P : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof s == "number" ? s : void 0
  }, className: Ne(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: c.jsxs("svg", { width: I, height: C, viewBox: `${G} ${K} ${ne} ${ae}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": U, ref: k, onClick: re, children: [Z && c.jsx("title", { id: U, children: Z }), c.jsx(_m, { onClick: ie, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: i, nodeClassName: r, nodeStrokeWidth: s, nodeComponent: l }), c.jsx("path", { className: "react-flow__minimap-mask", d: `M${G - O},${K - O}h${ne + O * 2}v${ae + O * 2}h${-ne - O * 2}z
        M${D.x},${D.y}h${D.width}v${D.height}h${-D.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
Kc.displayName = "MiniMap";
const Pm = be(Kc), $m = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, Tm = {
  [Ot.Line]: "right",
  [Ot.Handle]: "bottom-right"
};
function zm({ nodeId: e, position: t, variant: n = Ot.Handle, className: o, style: r = void 0, children: i, color: s, minWidth: l = 10, minHeight: u = 10, maxWidth: a = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: h, autoScale: g = !0, shouldResize: m, onResizeStart: x, onResize: v, onResizeEnd: y }) {
  const b = _c(), p = typeof e == "string" ? e : b, w = ye(), E = ce(null), k = n === Ot.Handle, _ = ue(pe($m(k && g), [k, g]), me), D = ce(null), $ = t ?? Tm[n];
  se(() => {
    if (!(!E.current || !p))
      return D.current || (D.current = np({
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
            const V = P.origin ?? C, F = j.width ?? P.measured.width ?? 0, O = j.height ?? P.measured.height ?? 0, G = {
              id: P.id,
              parentId: P.parentId,
              rect: {
                width: F,
                height: O,
                ...qa({
                  x: j.x ?? P.position.x,
                  y: j.y ?? P.position.y
                }, { width: F, height: O }, P.parentId, S, V)
              }
            }, K = bi([G], S, I, C);
            A.push(...K), T.x = j.x ? Math.max(V[0] * F, j.x) : void 0, T.y = j.y ? Math.max(V[1] * O, j.y) : void 0;
          }
          if (T.x !== void 0 && T.y !== void 0) {
            const V = {
              id: p,
              type: "position",
              position: { ...T }
            };
            A.push(V);
          }
          if (j.width !== void 0 && j.height !== void 0) {
            const F = {
              id: p,
              type: "dimensions",
              resizing: !0,
              setAttributes: h ? h === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: j.width,
                height: j.height
              }
            };
            A.push(F);
          }
          for (const V of z) {
            const F = {
              ...V,
              type: "position"
            };
            A.push(F);
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
    l,
    u,
    a,
    d,
    f,
    x,
    v,
    y,
    m
  ]);
  const W = $.split("-");
  return c.jsx("div", { className: Ne(["react-flow__resize-control", "nodrag", ...W, n, o]), ref: E, style: {
    ...r,
    scale: _,
    ...s && { [k ? "backgroundColor" : "borderColor"]: s }
  }, children: i });
}
be(zm);
const Lm = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), Uc = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var Rm = {
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
const Hm = Ro(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: o,
    className: r = "",
    children: i,
    iconNode: s,
    ...l
  }, u) => Hr(
    "svg",
    {
      ref: u,
      ...Rm,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: Uc("lucide", r),
      ...l
    },
    [
      ...s.map(([a, d]) => Hr(a, d)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const Se = (e, t) => {
  const n = Ro(
    ({ className: o, ...r }, i) => Hr(Hm, {
      ref: i,
      iconNode: t,
      className: Uc(`lucide-${Lm(e)}`, o),
      ...r
    })
  );
  return n.displayName = `${e}`, n;
};
const Gc = Se("Boxes", [
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
const Uo = Se("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const Vm = Se("ChevronDown", [
  ["path", { d: "m6 9 6 6 6-6", key: "qrunsl" }]
]);
const Jr = Se("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const At = Se("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const Nt = Se("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const Qc = Se("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const Om = Se("GripVertical", [
  ["circle", { cx: "9", cy: "12", r: "1", key: "1vctgf" }],
  ["circle", { cx: "9", cy: "5", r: "1", key: "hp0tcf" }],
  ["circle", { cx: "9", cy: "19", r: "1", key: "fkjjf6" }],
  ["circle", { cx: "15", cy: "12", r: "1", key: "1tmaij" }],
  ["circle", { cx: "15", cy: "5", r: "1", key: "19l28e" }],
  ["circle", { cx: "15", cy: "19", r: "1", key: "f4zoj3" }]
]);
const Jc = Se("ListTree", [
  ["path", { d: "M21 12h-8", key: "1bmf0i" }],
  ["path", { d: "M21 6H8", key: "1pqkrb" }],
  ["path", { d: "M21 18h-8", key: "1tm79t" }],
  ["path", { d: "M3 6v4c0 1.1.9 2 2 2h3", key: "1ywdgy" }],
  ["path", { d: "M3 10v6c0 1.1.9 2 2 2h3", key: "2wc746" }]
]);
const To = Se("Maximize2", [
  ["polyline", { points: "15 3 21 3 21 9", key: "mznyad" }],
  ["polyline", { points: "9 21 3 21 3 15", key: "1avn1i" }],
  ["line", { x1: "21", x2: "14", y1: "3", y2: "10", key: "ota7mn" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const Vs = Se("Minimize2", [
  ["polyline", { points: "4 14 10 14 10 20", key: "11kfnr" }],
  ["polyline", { points: "20 10 14 10 14 4", key: "rlmsce" }],
  ["line", { x1: "14", x2: "21", y1: "10", y2: "3", key: "o5lafz" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const Ei = Se("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const ki = Se("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const Bm = Se("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const Fm = Se("Save", [
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
const Wm = Se("Search", [
  ["circle", { cx: "11", cy: "11", r: "8", key: "4ej97u" }],
  ["path", { d: "m21 21-4.3-4.3", key: "1qie3q" }]
]);
const Ft = Se("Sparkles", [
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
const Xm = Se("Terminal", [
  ["polyline", { points: "4 17 10 11 4 5", key: "akl6gq" }],
  ["line", { x1: "12", x2: "20", y1: "19", y2: "19", key: "q2wloq" }]
]);
const ei = Se("Trash2", [
  ["path", { d: "M3 6h18", key: "d0wm0j" }],
  ["path", { d: "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", key: "4alrt4" }],
  ["path", { d: "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", key: "v07s0e" }],
  ["line", { x1: "10", x2: "10", y1: "11", y2: "17", key: "1uufr5" }],
  ["line", { x1: "14", x2: "14", y1: "11", y2: "17", key: "xtxkd" }]
]);
const Ym = Se("X", [
  ["path", { d: "M18 6 6 18", key: "1bl5f8" }],
  ["path", { d: "m6 6 12 12", key: "d8bk6v" }]
]);
const qm = Se("Zap", [
  [
    "path",
    {
      d: "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z",
      key: "1xq2db"
    }
  ]
]), Ve = "/_elsa/workflow-management";
async function Zm(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${Ve}/definitions?${n.toString()}`);
}
async function Km(e, t) {
  return e.http.getJson(`${Ve}/definitions/${encodeURIComponent(t)}`);
}
async function Um(e, t) {
  return e.http.postJson(`${Ve}/definitions`, t);
}
async function Gm(e, t) {
  await e.http.deleteJson(`${Ve}/definitions/${encodeURIComponent(t)}`);
}
async function Qm(e, t) {
  await e.http.postJson(`${Ve}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function Jm(e, t) {
  await e.http.deleteJson(`${Ve}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function ey(e, t) {
  return e.http.putJson(`${Ve}/drafts/${encodeURIComponent(t.id)}`, { state: t.state, layout: t.layout });
}
async function ty(e, t) {
  return e.http.postJson(`${Ve}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function ny(e, t) {
  return e.http.postJson(`${Ve}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function el(e, t) {
  return e.http.postJson(`${Ve}/executables/${encodeURIComponent(t)}/run`, {});
}
async function oy(e) {
  return e.http.getJson("/_demo/workflows/executables");
}
async function ry(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const o = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${o ? `?${o}` : ""}`);
}
async function iy(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function tl(e) {
  return e.http.getJson(`${Ve}/activities`);
}
async function sy(e) {
  const t = await nl(e, [
    `${Ve}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? Os(t) : Os(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function ay(e) {
  const t = await nl(e, [
    `${Ve}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : bo;
}
async function nl(e, t) {
  let n;
  for (const o of t)
    try {
      return await e.http.getJson(o);
    } catch (r) {
      n = r;
    }
  throw n;
}
function Os(e) {
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
const bo = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
], Go = "elsa.sequence.structure", Ln = "elsa.flowchart.structure";
function ol(e, t) {
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
function Lr(e, t) {
  const n = ol(e, t);
  if (!n) return null;
  let o = Je(n)[0];
  return o ? { owner: n, slot: o } : null;
}
function Je(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = Iy(t), r = Rr(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: jy(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, i]) => Rr(i)).map(([i, s]) => ({
    id: `${t.kind}:${i}`,
    label: My(i),
    property: i,
    mode: "generic",
    activities: Rr(s) ?? []
  }));
}
function cy(e, t, n) {
  const o = new Map(t.map((s) => [s.activityVersionId, s])), r = new Map(n.map((s) => [s.nodeId, s])), i = e.slot.activities.map((s, l) => {
    const u = o.get(s.activityVersionId), a = r.get(s.nodeId) ?? Ay(e.slot.mode, l);
    return sl(s, u, { x: a.x, y: a.y });
  });
  return {
    nodes: i,
    edges: e.slot.mode === "flowchart" ? by(e.owner) : vy(e.slot, i)
  };
}
function ly(e, t, n) {
  const o = t.find((i) => i.activityVersionId === e.activityVersionId), r = n.find((i) => i.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [sl(e, o, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function uy(e, t) {
  return e?.structure?.kind === Ln || gy(t) ? "flowchart" : e?.structure?.kind === Go || my(t) ? "sequence" : "unsupported";
}
function ti(e, t, n) {
  if (t.length === 0) {
    const l = Je(e)[0];
    return l ? jn(e, l, n) : e;
  }
  const [o, ...r] = t, i = Je(e).find((l) => l.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((l) => l.nodeId === o.ownerNodeId ? ti(l, r, n) : l);
  return jn(e, i, s);
}
function rl(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, i = Je(e).find((l) => l.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((l) => l.nodeId === o.ownerNodeId ? rl(l, r, n) : l);
  return jn(e, i, s);
}
function il(e, t, n) {
  if (e.nodeId === t) return n(e);
  const o = Je(e);
  if (o.length === 0) return e;
  let r = !1, i = e;
  for (const s of o) {
    const l = s.activities.map((u) => {
      const a = il(u, t, n);
      return a !== u && (r = !0), a;
    });
    r && (i = jn(i, s, l));
  }
  return r ? i : e;
}
function jn(e, t, n) {
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
function dy(e, t, n, o = []) {
  const r = new Map(e.slot.activities.map((s) => [s.nodeId, s]));
  for (const s of o)
    r.set(s.nodeId, s);
  const i = t.map((s) => r.get(s.id)).filter((s) => !!s);
  return e.slot.mode === "sequence" && i.sort((s, l) => {
    const u = t.find((d) => d.id === s.nodeId), a = t.find((d) => d.id === l.nodeId);
    return (u?.position.x ?? 0) - (a?.position.x ?? 0);
  }), jn(e.owner, e.slot, i);
}
function fy(e, t) {
  return {
    ...e,
    structure: wy(e.structure, t)
  };
}
function hy(e, t) {
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
function Bs(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: xy(e)
  };
}
function ke(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? yy(t) : n;
}
function sl(e, t, n, o = {}) {
  return {
    id: e.nodeId,
    type: "workflowActivity",
    position: n,
    connectable: o.connectable,
    deletable: o.deletable,
    draggable: o.draggable,
    data: {
      label: t ? ke(t) : e.activityVersionId,
      activityVersionId: e.activityVersionId,
      activityTypeKey: t?.activityTypeKey,
      category: t?.category,
      executionType: t?.executionType,
      icon: ni(t),
      childSlots: Je(e),
      acceptsInbound: Sy(e, t),
      sourcePorts: o.suppressFlowPorts ? [] : al(e, t),
      suppressFlowPorts: o.suppressFlowPorts
    }
  };
}
function ni(e) {
  if (!e) return "activity";
  const t = py(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), o = ke(e).toLowerCase(), r = e.category?.toLowerCase() ?? "", i = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || o === "flowchart" ? "flowchart" : n.endsWith(".sequence") || o === "sequence" ? "sequence" : n.includes("writeline") || o.includes("write line") ? "terminal" : r.includes("runtime") ? "runtime" : i === "trigger" ? "trigger" : "activity";
}
function py(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function gy(e) {
  return !!e && (ke(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function my(e) {
  return !!e && (ke(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function yy(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function xy(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: Go,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: Ln,
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
function wy(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], o = /* @__PURE__ */ new Map();
  for (const r of n) {
    if (!Ci(r)) continue;
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
          ...s?.length ? { vertices: s.map((a) => ({ x: Math.round(a.x), y: Math.round(a.y) })) } : {}
        };
      })
    }
  };
}
function vy(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function by(e) {
  if (e.structure?.kind !== Ln) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, i = n.target;
    if (!r?.nodeId || !i?.nodeId) return null;
    const s = Array.isArray(n.vertices) ? n.vertices.filter(Cy) : [];
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
function al(e, t) {
  const n = Fs(e.cases);
  if (Ey(e, t) && n.length > 0)
    return [...n.map((i) => ({ name: i, displayName: i })), { name: "Default", displayName: "Default" }];
  const o = [
    ...So(t?.designFacets),
    ...So(t?.ports),
    ...So(t?.outputs)
  ];
  if (o.length > 0) return ky(o);
  const r = Fs(e.outcomes);
  return r.length > 0 ? r.map((i) => ({ name: i, displayName: i })) : [{ name: "Done", displayName: "Done" }];
}
function Sy(e, t) {
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
function Ny(e, t, n) {
  const o = zo(t.source, n, t.sourceHandle ?? "Done", void 0), r = zo(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((i) => i.id !== t.id).concat(o, r);
}
function Rr(e) {
  return Array.isArray(e) ? e.filter(_y) : null;
}
function Ey(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function So(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!Ci(n)) continue;
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
function ky(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function Fs(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function Cy(e) {
  return Ci(e) && typeof e.x == "number" && typeof e.y == "number";
}
function Ci(e) {
  return typeof e == "object" && e !== null;
}
function _y(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Iy(e) {
  return e.kind === Go ? "sequence" : e.kind === Ln ? "flowchart" : "generic";
}
function jy(e) {
  return e.kind === Go || e.kind === Ln, "Activities";
}
function Ay(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function My(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Dy = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function Py(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function _i(e) {
  return Py(e.name);
}
function $y(e, t) {
  const n = _i(t), o = e[n];
  return t.isWrapped === !1 ? o ?? t.defaultValue ?? "" : ll(o, t);
}
function cl(e, t) {
  return ll(e[_i(t)], t);
}
function Ty(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function zy(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function Ws(e, t, n) {
  return {
    ...e,
    [_i(t)]: n
  };
}
function Ly(e, t) {
  return t.isWrapped === !1 ? $y(e, t) : cl(e, t).expression.value;
}
function ll(e, t) {
  return Ry(e) ? {
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
function Ry(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
const ul = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]);
function Hy({
  activity: e,
  descriptor: t,
  editors: n,
  expressionDescriptors: o,
  descriptorStatus: r,
  onChange: i
}) {
  if (r === "loading")
    return /* @__PURE__ */ c.jsx("p", { className: "wf-muted", children: "Loading activity properties..." });
  if (!t)
    return /* @__PURE__ */ c.jsx("p", { className: "wf-muted", children: "No activity descriptor is available for this activity." });
  const s = t.inputs.filter((a) => a.isBrowsable !== !1).sort((a, d) => (a.order ?? 0) - (d.order ?? 0) || a.name.localeCompare(d.name));
  if (s.length === 0)
    return /* @__PURE__ */ c.jsx("p", { className: "wf-muted", children: "This activity does not expose editable properties." });
  const l = Fy(s), u = o.length > 0 ? o : Dy;
  return /* @__PURE__ */ c.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ c.jsx("span", { className: "wf-section-label", children: "Properties" }),
    l.map((a) => /* @__PURE__ */ c.jsxs("section", { className: "wf-property-group", children: [
      l.length > 1 ? /* @__PURE__ */ c.jsx("h4", { children: a.category }) : null,
      a.inputs.map((d) => /* @__PURE__ */ c.jsx(
        Vy,
        {
          activity: e,
          input: d,
          editors: n,
          expressionDescriptors: u,
          onChange: i
        },
        d.name
      ))
    ] }, a.category))
  ] });
}
function Vy({
  activity: e,
  input: t,
  editors: n,
  expressionDescriptors: o,
  onChange: r
}) {
  const i = t.isReadOnly === !0, s = { activity: e, expressionDescriptors: o, readOnly: i }, l = By(n, t, s), u = l?.component, a = t.isWrapped !== !1 ? cl(e, t) : null, d = a?.expression.type ?? "Literal", f = Ly(e, t), h = !!(a && Wy(t, l?.id)), g = !!(a && Xy(t, l?.id)), [m, x] = q(!1), v = (b) => {
    const p = a ? Ty(a, b) : b;
    r(Ws(e, t, p));
  }, y = (b) => {
    a && r(Ws(e, t, zy(a, b)));
  };
  return /* @__PURE__ */ c.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ c.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ c.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ c.jsx("span", { children: dl(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ c.jsx("p", { children: t.description }) : null,
    a && !h ? /* @__PURE__ */ c.jsx(
      oi,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: d,
        descriptors: o,
        disabled: i,
        onChange: y
      }
    ) : null,
    h ? /* @__PURE__ */ c.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ c.jsx("div", { className: "wf-expression-editor", children: Xs(u, t, f, i, s, v) }),
      /* @__PURE__ */ c.jsx(
        oi,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: d,
          descriptors: o,
          disabled: i,
          variant: "inline",
          onChange: y
        }
      ),
      g ? /* @__PURE__ */ c.jsx(
        "button",
        {
          type: "button",
          className: "wf-expression-expand-button",
          "aria-label": `Open expanded ${t.displayName || t.name} editor`,
          title: "Open expanded editor",
          onClick: () => x(!0),
          children: /* @__PURE__ */ c.jsx(To, { size: 13 })
        }
      ) : null
    ] }) : Xs(u, t, f, i, s, v),
    g && !h ? /* @__PURE__ */ c.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => x(!0),
        children: [
          /* @__PURE__ */ c.jsx(To, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    m ? /* @__PURE__ */ c.jsx(
      Oy,
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
function Oy({
  input: e,
  value: t,
  syntax: n,
  descriptors: o,
  disabled: r,
  onChange: i,
  onSyntaxChange: s,
  onClose: l
}) {
  const u = sa(), a = e.displayName || e.name;
  return se(() => {
    const d = (f) => {
      f.key === "Escape" && l();
    };
    return window.addEventListener("keydown", d), () => window.removeEventListener("keydown", d);
  }, [l]), /* @__PURE__ */ c.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ c.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": u, children: [
    /* @__PURE__ */ c.jsxs("header", { children: [
      /* @__PURE__ */ c.jsxs("div", { children: [
        /* @__PURE__ */ c.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ c.jsx("h3", { id: u, children: a })
      ] }),
      /* @__PURE__ */ c.jsx("button", { type: "button", "aria-label": `Close ${a} editor`, onClick: l, children: /* @__PURE__ */ c.jsx(Ym, { size: 16 }) })
    ] }),
    /* @__PURE__ */ c.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ c.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ c.jsx(
          oi,
          {
            label: `${a} expression syntax`,
            value: n,
            descriptors: o,
            disabled: r,
            onChange: s
          }
        ),
        /* @__PURE__ */ c.jsx("span", { children: dl(e.typeName) })
      ] }),
      e.description ? /* @__PURE__ */ c.jsx("p", { children: e.description }) : null,
      /* @__PURE__ */ c.jsx(
        "textarea",
        {
          "aria-label": `${a} expanded value`,
          value: t == null ? "" : String(t),
          disabled: r,
          spellCheck: !1,
          onChange: (d) => i(d.target.value)
        }
      )
    ] }),
    /* @__PURE__ */ c.jsxs("footer", { children: [
      /* @__PURE__ */ c.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ c.jsx("button", { type: "button", onClick: l, children: "Close" })
    ] })
  ] }) });
}
function Xs(e, t, n, o, r, i) {
  return e ? /* @__PURE__ */ c.jsx(
    e,
    {
      descriptor: t,
      value: n,
      disabled: o,
      context: r,
      onChange: i
    }
  ) : /* @__PURE__ */ c.jsx("input", { type: "text", value: n == null ? "" : String(n), disabled: o, onChange: (s) => i(s.target.value) });
}
function oi({
  label: e,
  value: t,
  descriptors: n,
  disabled: o,
  variant: r = "block",
  onChange: i
}) {
  const [s, l] = q(!1), u = sa(), a = n.find((f) => f.type === t), d = [
    "wf-syntax-picker-trigger",
    r === "inline" ? "inline" : "",
    s ? "open" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ c.jsxs("div", { className: r === "inline" ? "wf-syntax-picker inline" : "wf-syntax-picker", onBlur: (f) => {
    f.currentTarget.contains(f.relatedTarget) || l(!1);
  }, children: [
    /* @__PURE__ */ c.jsx(
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
        children: /* @__PURE__ */ c.jsx("span", { children: a?.displayName || a?.type || t })
      }
    ),
    s ? /* @__PURE__ */ c.jsx("div", { id: u, role: "listbox", className: "wf-syntax-picker-menu", "aria-label": e, children: n.map((f) => {
      const h = f.displayName || f.type, g = f.type === t;
      return /* @__PURE__ */ c.jsx(
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
function By(e, t, n) {
  return [...e].sort((o, r) => (o.order ?? 500) - (r.order ?? 500)).find((o) => o.supports(t, n));
}
function Fy(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "General";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return [...t.entries()].map(([n, o]) => ({ category: n, inputs: o }));
}
function dl(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function Wy(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !ul.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function Xy(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !ul.has(t) && n !== "multiline") return !1;
  const o = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(o) || n === "singleline" || n === "multiline";
}
const Yy = { workflowActivity: N0 }, qy = { workflow: k0 }, Ys = "application/x-elsa-activity-version-id", Zy = 6, Ky = 1200, Uy = [10, 25, 50], Gy = 10, qs = "elsa-studio-workflow-palette-width", Zs = "elsa-studio-workflow-inspector-width", Ks = "elsa-studio-workflow-palette-collapsed", Us = "elsa-studio-workflow-inspector-collapsed", fl = "elsa-studio-workflow-side-panel-maximized", ln = 180, un = 460, Qy = 260, dn = 260, fn = 560, Jy = 320, Gs = 42, po = 16, hl = dt.createContext(null);
function P0(e) {
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
        component: () => /* @__PURE__ */ c.jsx(e0, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), workflowDesignerPanels: e.workflowDesigner.panels.list() })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ c.jsx(t0, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow instances",
        component: () => /* @__PURE__ */ c.jsx(n0, { context: e.backend, ai: e.ai })
      }
    ]
  });
}
function e0({
  context: e,
  ai: t,
  propertyEditors: n,
  workflowDesignerPanels: o
}) {
  const [r, i] = q(Qs);
  se(() => {
    const l = () => i(Qs());
    return window.addEventListener("popstate", l), () => window.removeEventListener("popstate", l);
  }, []);
  const s = (l) => {
    const u = l ? `/workflows/definitions?definition=${encodeURIComponent(l)}` : "/workflows/definitions";
    window.history.pushState({}, "", u), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return r ? /* @__PURE__ */ c.jsx(S0, { context: e, definitionId: r, ai: t, propertyEditors: n, workflowDesignerPanels: o, onBack: () => s(null) }) : /* @__PURE__ */ c.jsx(Ii, { activePath: "/workflows/definitions", title: "Definitions", children: /* @__PURE__ */ c.jsx(o0, { context: e, ai: t, onOpen: s }) });
}
function t0({ context: e, ai: t }) {
  const [n, o] = q(Js);
  return se(() => {
    const r = () => o(Js());
    return window.addEventListener("popstate", r), () => window.removeEventListener("popstate", r);
  }, []), /* @__PURE__ */ c.jsx(Ii, { activePath: "/workflows/executables", title: "Executables", children: /* @__PURE__ */ c.jsx(i0, { context: e, ai: t, definitionFilter: n }) });
}
function n0({ context: e, ai: t }) {
  return /* @__PURE__ */ c.jsx(Ii, { activePath: "/workflows/instances", title: "Instances", children: /* @__PURE__ */ c.jsx(s0, { context: e, ai: t }) });
}
function Ii({ activePath: e, title: t, children: n }) {
  const o = (r) => {
    window.history.pushState({}, "", r), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ c.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ c.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ c.jsxs("div", { children: [
      /* @__PURE__ */ c.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ c.jsx("h2", { children: t })
    ] }) }),
    /* @__PURE__ */ c.jsxs("nav", { className: "wf-section-tabs", "aria-label": "Workflow views", children: [
      /* @__PURE__ */ c.jsx("a", { className: e === "/workflows/definitions" ? "active" : "", href: "/workflows/definitions", onClick: (r) => {
        r.preventDefault(), o("/workflows/definitions");
      }, children: "Definitions" }),
      /* @__PURE__ */ c.jsx("a", { className: e === "/workflows/executables" ? "active" : "", href: "/workflows/executables", onClick: (r) => {
        r.preventDefault(), o("/workflows/executables");
      }, children: "Executables" }),
      /* @__PURE__ */ c.jsx("a", { className: e === "/workflows/instances" ? "active" : "", href: "/workflows/instances", onClick: (r) => {
        r.preventDefault(), o("/workflows/instances");
      }, children: "Instances" })
    ] }),
    n
  ] });
}
function Qs() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Js() {
  return new URLSearchParams(window.location.search).get("definition");
}
function o0({ context: e, ai: t, onOpen: n }) {
  const [o, r] = q(""), [i, s] = q("active"), [l, u] = q(1), [a, d] = q(Gy), [f, h] = q("loading"), [g, m] = q(""), [x, v] = q(""), [y, b] = q([]), [p, w] = q(0), [E, k] = q(() => /* @__PURE__ */ new Set()), [_, D] = q(null), [$, W] = q(!1), [j, z] = q([]), [H, S] = q("idle"), I = ce(null), C = ge(() => y.map((R) => R.id), [y]), A = Wt(t, "weaver.workflows.suggest-create-metadata"), T = Wt(t, "weaver.workflows.explain-definition"), P = C.filter((R) => E.has(R)).length, V = C.length > 0 && P === C.length, F = pe(async () => {
    h("loading"), m("");
    try {
      const R = await Zm(e, { search: o, state: i, page: l, pageSize: a }), J = typeof R.totalCount == "number", fe = R.totalCount ?? R.definitions.length, we = pl(fe, a);
      if (fe > 0 && l > we) {
        u(we);
        return;
      }
      b(J ? R.definitions : d0(R.definitions, l, a)), w(fe), h("ready");
    } catch (R) {
      m(R instanceof Error ? R.message : String(R)), h("failed");
    }
  }, [e, o, i, l, a]);
  se(() => {
    F();
  }, [F]), se(() => {
    I.current && (I.current.indeterminate = P > 0 && !V);
  }, [V, P]);
  const O = pe(async () => {
    if (!(H === "loading" || H === "ready")) {
      S("loading");
      try {
        const R = await tl(e);
        z(R.activities ?? []), S("ready");
      } catch (R) {
        S("failed"), m(R instanceof Error ? R.message : String(R));
      }
    }
  }, [H, e]), G = () => {
    m(""), v(""), D({ name: "", description: "", rootKind: "flowchart" }), O();
  }, K = async () => {
    if (_?.name.trim()) {
      W(!0), m(""), v("");
      try {
        const R = await Um(e, {
          name: _.name.trim(),
          description: _.description.trim() || null,
          rootKind: _.rootKind,
          rootActivityVersionId: p0(_, j)
        });
        D(null), n(R.definition.id);
      } catch (R) {
        m(R instanceof Error ? R.message : String(R));
      } finally {
        W(!1);
      }
    }
  }, ne = (R) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(R)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, ae = async () => {
    if (y.length === 1 && l > 1) {
      u(l - 1);
      return;
    }
    await F();
  }, U = () => k(/* @__PURE__ */ new Set()), L = (R, J) => {
    k((fe) => {
      const we = new Set(fe);
      return J ? we.add(R) : we.delete(R), we;
    });
  }, Y = (R) => {
    k((J) => {
      const fe = new Set(J);
      for (const we of C)
        R ? fe.add(we) : fe.delete(we);
      return fe;
    });
  }, re = (R) => {
    s(R), u(1), U();
  }, ie = (R) => {
    r(R), u(1), U();
  }, Z = async (R) => {
    if (window.confirm(`Delete workflow definition "${R.name}"? You can restore it from the Deleted view.`)) {
      v(""), m("");
      try {
        await Gm(e, R.id), L(R.id, !1), v(`Deleted ${R.name}`), await ae();
      } catch (J) {
        m(J instanceof Error ? J.message : String(J));
      }
    }
  }, ee = async (R) => {
    v(""), m("");
    try {
      await Qm(e, R.id), L(R.id, !1), v(`Restored ${R.name}`), await ae();
    } catch (J) {
      m(J instanceof Error ? J.message : String(J));
    }
  }, oe = async (R) => {
    if (window.confirm(`Permanently delete workflow definition "${R.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`)) {
      v(""), m("");
      try {
        await Jm(e, R.id), L(R.id, !1), v(`Permanently deleted ${R.name}`), await ae();
      } catch (J) {
        m(J instanceof Error ? J.message : String(J));
      }
    }
  };
  return /* @__PURE__ */ c.jsxs(c.Fragment, { children: [
    /* @__PURE__ */ c.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ c.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ c.jsx("button", { type: "button", className: i === "active" ? "active" : "", "aria-selected": i === "active", onClick: () => re("active"), children: "Active" }),
        /* @__PURE__ */ c.jsx("button", { type: "button", className: i === "deleted" ? "active" : "", "aria-selected": i === "deleted", onClick: () => re("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ c.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ c.jsx(Wm, { size: 15 }),
        /* @__PURE__ */ c.jsx("input", { value: o, onChange: (R) => ie(R.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ c.jsx("button", { type: "button", onClick: () => {
        F();
      }, children: "Refresh" }),
      /* @__PURE__ */ c.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ c.jsxs("button", { type: "button", title: "Create workflow", onClick: G, children: [
        /* @__PURE__ */ c.jsx(ki, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ c.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ c.jsx(Nt, { size: 16 }),
      " ",
      g
    ] }) : null,
    f !== "failed" && g ? /* @__PURE__ */ c.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ c.jsx(Nt, { size: 16 }),
      " ",
      g
    ] }) : null,
    x ? /* @__PURE__ */ c.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ c.jsx(Uo, { size: 14 }),
      " ",
      x
    ] }) : null,
    E.size > 0 ? /* @__PURE__ */ c.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ c.jsxs("span", { children: [
        E.size,
        " selected"
      ] }),
      /* @__PURE__ */ c.jsx("button", { type: "button", onClick: U, children: "Clear selection" })
    ] }) : null,
    f === "loading" ? /* @__PURE__ */ c.jsx("div", { className: "wf-empty", children: "Loading workflow definitions..." }) : null,
    f === "ready" && y.length === 0 ? /* @__PURE__ */ c.jsxs("div", { className: "wf-empty", children: [
      "No ",
      i,
      " workflow definitions found."
    ] }) : null,
    f === "ready" && y.length > 0 ? /* @__PURE__ */ c.jsxs(c.Fragment, { children: [
      /* @__PURE__ */ c.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ c.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ c.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ c.jsx(
            "input",
            {
              ref: I,
              type: "checkbox",
              checked: V,
              onChange: (R) => Y(R.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ c.jsx("span", { children: "Name" }),
          /* @__PURE__ */ c.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ c.jsx("span", { children: i === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ c.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ c.jsx("span", { children: "Actions" })
        ] }),
        y.map((R) => /* @__PURE__ */ c.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${R.name}`,
            "aria-selected": E.has(R.id),
            tabIndex: 0,
            onClick: () => n(R.id),
            onKeyDown: (J) => {
              J.currentTarget === J.target && (J.key !== "Enter" && J.key !== " " || (J.preventDefault(), n(R.id)));
            },
            children: [
              /* @__PURE__ */ c.jsx("label", { className: "wf-row-select", onClick: (J) => J.stopPropagation(), children: /* @__PURE__ */ c.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: E.has(R.id),
                  onChange: (J) => L(R.id, J.target.checked),
                  "aria-label": `Select workflow definition ${R.name}`
                }
              ) }),
              /* @__PURE__ */ c.jsxs("span", { children: [
                /* @__PURE__ */ c.jsx("strong", { children: R.name }),
                /* @__PURE__ */ c.jsx("small", { children: R.description || R.id })
              ] }),
              /* @__PURE__ */ c.jsx("span", { children: R.latestVersion ?? "No version" }),
              /* @__PURE__ */ c.jsx("span", { children: i === "deleted" ? ut(R.deletedAt) : R.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ c.jsx("span", { children: ut(R.lastModifiedAt) }),
              /* @__PURE__ */ c.jsx("span", { className: "wf-row-actions", onClick: (J) => J.stopPropagation(), children: i === "active" ? /* @__PURE__ */ c.jsxs(c.Fragment, { children: [
                /* @__PURE__ */ c.jsx("button", { type: "button", onClick: (J) => {
                  J.stopPropagation(), n(R.id);
                }, children: "Open" }),
                /* @__PURE__ */ c.jsx("button", { type: "button", onClick: (J) => {
                  J.stopPropagation(), ne(R.id);
                }, children: "Artifacts" }),
                T ? /* @__PURE__ */ c.jsxs("button", { type: "button", onClick: () => Xt(t, T, R), children: [
                  /* @__PURE__ */ c.jsx(Ft, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ c.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  Z(R);
                }, children: [
                  /* @__PURE__ */ c.jsx(ei, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ c.jsxs(c.Fragment, { children: [
                /* @__PURE__ */ c.jsxs("button", { type: "button", onClick: () => {
                  ee(R);
                }, children: [
                  /* @__PURE__ */ c.jsx(Bm, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ c.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  oe(R);
                }, children: [
                  /* @__PURE__ */ c.jsx(ei, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          R.id
        ))
      ] }),
      /* @__PURE__ */ c.jsx(
        u0,
        {
          page: l,
          pageSize: a,
          totalCount: p,
          onPageChange: u,
          onPageSizeChange: (R) => {
            d(R), u(1);
          }
        }
      )
    ] }) : null,
    _ ? /* @__PURE__ */ c.jsx(
      r0,
      {
        draft: _,
        activities: j,
        catalogState: H,
        creating: $,
        suggestMetadataAction: A,
        onSuggestMetadata: A ? () => Xt(t, A, { draft: _, activities: j }) : void 0,
        onChange: (R) => D(R),
        onClose: () => D(null),
        onSubmit: K
      }
    ) : null
  ] });
}
function r0({ draft: e, activities: t, catalogState: n, creating: o, suggestMetadataAction: r, onSuggestMetadata: i, onChange: s, onClose: l, onSubmit: u }) {
  const a = ge(() => f0(t), [t]), d = h0(e, t), f = (h) => {
    if (h.startsWith("kind:")) {
      s({ ...e, rootKind: h.slice(5), rootActivityVersionId: null });
      return;
    }
    const g = t.find((m) => m.activityVersionId === h);
    s({
      ...e,
      rootKind: gl(g) ?? e.rootKind,
      rootActivityVersionId: h
    });
  };
  return /* @__PURE__ */ c.jsx("div", { className: "wf-dialog-backdrop", role: "presentation", children: /* @__PURE__ */ c.jsx("section", { className: "wf-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "workflow-create-title", children: /* @__PURE__ */ c.jsxs(
    "form",
    {
      onSubmit: (h) => {
        h.preventDefault(), u();
      },
      children: [
        /* @__PURE__ */ c.jsxs("div", { className: "wf-dialog-heading", children: [
          /* @__PURE__ */ c.jsx("h3", { id: "workflow-create-title", children: "Create Workflow" }),
          r ? /* @__PURE__ */ c.jsxs("button", { type: "button", className: "wf-ai-action", onClick: i, title: r.description ?? r.label, children: [
            /* @__PURE__ */ c.jsx(Ft, { size: 13 }),
            " ",
            r.label
          ] }) : null
        ] }),
        /* @__PURE__ */ c.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ c.jsx("span", { children: "Display name" }),
          /* @__PURE__ */ c.jsx(
            "input",
            {
              autoFocus: !0,
              "aria-label": "Display name",
              value: e.name,
              onChange: (h) => s({ ...e, name: h.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ c.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ c.jsx("span", { children: "Description" }),
          /* @__PURE__ */ c.jsx(
            "textarea",
            {
              "aria-label": "Description",
              rows: 3,
              value: e.description,
              onChange: (h) => s({ ...e, description: h.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ c.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ c.jsx("span", { children: "Root activity" }),
          /* @__PURE__ */ c.jsxs(
            "select",
            {
              "aria-label": "Root activity",
              value: d,
              onChange: (h) => f(h.target.value),
              disabled: n === "loading",
              children: [
                /* @__PURE__ */ c.jsx("optgroup", { label: "Composite roots", children: a.compositeRoots.map((h) => /* @__PURE__ */ c.jsx("option", { value: h.value, children: h.label }, h.value)) }),
                a.otherCategories.map((h) => /* @__PURE__ */ c.jsx("optgroup", { label: h.name, children: h.activities.map((g) => /* @__PURE__ */ c.jsx("option", { value: g.activityVersionId, children: ke(g) }, g.activityVersionId)) }, h.name))
              ]
            }
          )
        ] }),
        n === "loading" ? /* @__PURE__ */ c.jsx("div", { className: "wf-dialog-note", children: "Loading activity catalog..." }) : null,
        n === "failed" ? /* @__PURE__ */ c.jsx("div", { className: "wf-dialog-note", children: "Activity catalog could not be loaded. Composite roots remain available." }) : null,
        /* @__PURE__ */ c.jsxs("div", { className: "wf-dialog-actions", children: [
          /* @__PURE__ */ c.jsx("button", { type: "button", onClick: l, disabled: o, children: "Cancel" }),
          /* @__PURE__ */ c.jsx("button", { type: "submit", disabled: o || !e.name.trim(), children: o ? "Creating..." : "Create" })
        ] })
      ]
    }
  ) }) });
}
function i0({ context: e, ai: t, definitionFilter: n }) {
  const [o, r] = q("loading"), [i, s] = q(""), [l, u] = q(""), [a, d] = q([]), f = ge(
    () => n ? a.filter((x) => x.definitionId === n || x.sourceId === n) : a,
    [n, a]
  ), h = Wt(t, "weaver.workflows.explain-executable"), g = pe(async () => {
    r("loading"), s("");
    try {
      d(await oy(e)), r("ready");
    } catch (x) {
      s(x instanceof Error ? x.message : String(x)), r("failed");
    }
  }, [e]);
  se(() => {
    g();
  }, [g]);
  const m = async (x) => {
    u(""), s("");
    try {
      await el(e, x.artifactId), u(`Started ${x.artifactId}`);
    } catch (v) {
      s(v instanceof Error ? v.message : String(v));
    }
  };
  return /* @__PURE__ */ c.jsxs(c.Fragment, { children: [
    /* @__PURE__ */ c.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ c.jsx("button", { type: "button", onClick: () => {
        g();
      }, children: "Refresh" }),
      n ? /* @__PURE__ */ c.jsxs("span", { className: "wf-filter-chip", children: [
        "Definition ",
        n
      ] }) : null
    ] }),
    o === "failed" ? /* @__PURE__ */ c.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ c.jsx(Nt, { size: 16 }),
      " ",
      i
    ] }) : null,
    l ? /* @__PURE__ */ c.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ c.jsx(Uo, { size: 14 }),
      " ",
      l
    ] }) : null,
    o === "loading" ? /* @__PURE__ */ c.jsx("div", { className: "wf-empty", children: "Loading workflow executables..." }) : null,
    o === "ready" && f.length === 0 ? /* @__PURE__ */ c.jsx("div", { className: "wf-empty", children: n ? "No workflow executables found for this definition." : "No workflow executables found. Publish a workflow definition to create one." }) : null,
    o === "ready" && f.length > 0 ? /* @__PURE__ */ c.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ c.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ c.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ c.jsx("span", { children: "Version" }),
        /* @__PURE__ */ c.jsx("span", { children: "Source" }),
        /* @__PURE__ */ c.jsx("span", { children: "Root" }),
        /* @__PURE__ */ c.jsx("span", { children: "Published" }),
        /* @__PURE__ */ c.jsx("span", { children: "Actions" })
      ] }),
      f.map((x) => /* @__PURE__ */ c.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ c.jsxs("span", { children: [
          /* @__PURE__ */ c.jsx("strong", { children: x.artifactId }),
          /* @__PURE__ */ c.jsx("small", { children: x.artifactHash })
        ] }),
        /* @__PURE__ */ c.jsx("span", { children: x.artifactVersion }),
        /* @__PURE__ */ c.jsx("span", { children: m0(x) }),
        /* @__PURE__ */ c.jsx("span", { children: y0(x) }),
        /* @__PURE__ */ c.jsx("span", { children: ut(x.publishedAt ?? x.createdAt) }),
        /* @__PURE__ */ c.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ c.jsxs("button", { type: "button", onClick: () => {
            m(x);
          }, children: [
            /* @__PURE__ */ c.jsx(Ei, { size: 13 }),
            " Run"
          ] }),
          h ? /* @__PURE__ */ c.jsxs("button", { type: "button", onClick: () => Xt(t, h, x), children: [
            /* @__PURE__ */ c.jsx(Ft, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, x.artifactId))
    ] }) : null
  ] });
}
function s0({ context: e, ai: t }) {
  const [n, o] = q("loading"), [r, i] = q(""), [s, l] = q(""), [u, a] = q([]), [d, f] = q(null), [h, g] = q(null), [m, x] = q("idle"), [v, y] = q(""), b = Wt(t, "weaver.workflows.explain-instance"), p = pe(async () => {
    o("loading"), i("");
    try {
      const E = await ry(e, { status: s || void 0, take: 100 });
      a(E), o("ready"), f((k) => k && E.some((_) => _.workflowExecutionId === k) ? k : E[0]?.workflowExecutionId ?? null);
    } catch (E) {
      i(E instanceof Error ? E.message : String(E)), a([]), o("failed");
    }
  }, [e, s]);
  se(() => {
    p();
  }, [p]), se(() => {
    let E = !1;
    return g(null), y(""), d ? (x("loading"), iy(e, d).then((k) => {
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
  return /* @__PURE__ */ c.jsxs(c.Fragment, { children: [
    /* @__PURE__ */ c.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ c.jsx("button", { type: "button", onClick: () => {
        p();
      }, children: "Refresh" }),
      /* @__PURE__ */ c.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ c.jsx("span", { children: "Status" }),
        /* @__PURE__ */ c.jsxs("select", { "aria-label": "Workflow instance status", value: s, onChange: (E) => l(E.target.value), children: [
          /* @__PURE__ */ c.jsx("option", { value: "", children: "All statuses" }),
          /* @__PURE__ */ c.jsx("option", { value: "Pending", children: "Pending" }),
          /* @__PURE__ */ c.jsx("option", { value: "Running", children: "Running" }),
          /* @__PURE__ */ c.jsx("option", { value: "Suspended", children: "Suspended" }),
          /* @__PURE__ */ c.jsx("option", { value: "Completed", children: "Completed" }),
          /* @__PURE__ */ c.jsx("option", { value: "Faulted", children: "Faulted" }),
          /* @__PURE__ */ c.jsx("option", { value: "Cancelled", children: "Cancelled" })
        ] })
      ] })
    ] }),
    n === "failed" ? /* @__PURE__ */ c.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ c.jsx(Nt, { size: 16 }),
      " ",
      r
    ] }) : null,
    n === "loading" ? /* @__PURE__ */ c.jsx("div", { className: "wf-empty", children: "Loading workflow instances..." }) : null,
    n === "ready" && u.length === 0 ? /* @__PURE__ */ c.jsx("div", { className: "wf-empty", children: "No workflow instances found. Run a published workflow executable to create instance history." }) : null,
    n === "ready" && u.length > 0 ? /* @__PURE__ */ c.jsxs("div", { className: "wf-instance-workbench", children: [
      /* @__PURE__ */ c.jsxs("div", { className: "wf-grid wf-instance-grid", role: "table", "aria-label": "Workflow instances", children: [
        /* @__PURE__ */ c.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ c.jsx("span", { children: "Instance" }),
          /* @__PURE__ */ c.jsx("span", { children: "Status" }),
          /* @__PURE__ */ c.jsx("span", { children: "Definition" }),
          /* @__PURE__ */ c.jsx("span", { children: "Activity" }),
          /* @__PURE__ */ c.jsx("span", { children: "Started" }),
          /* @__PURE__ */ c.jsx("span", { children: "Duration" })
        ] }),
        u.map((E) => /* @__PURE__ */ c.jsxs(
          "button",
          {
            type: "button",
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Inspect workflow instance ${E.workflowExecutionId}`,
            "aria-selected": E.workflowExecutionId === d,
            onClick: () => f(E.workflowExecutionId),
            children: [
              /* @__PURE__ */ c.jsxs("span", { children: [
                /* @__PURE__ */ c.jsx("strong", { children: E.workflowExecutionId }),
                /* @__PURE__ */ c.jsx("small", { children: E.artifactId })
              ] }),
              /* @__PURE__ */ c.jsx("span", { children: /* @__PURE__ */ c.jsx(ji, { status: E.status, subStatus: E.subStatus }) }),
              /* @__PURE__ */ c.jsxs("span", { children: [
                /* @__PURE__ */ c.jsx("strong", { children: E.definitionId }),
                /* @__PURE__ */ c.jsx("small", { children: E.definitionVersionId })
              ] }),
              /* @__PURE__ */ c.jsxs("span", { children: [
                /* @__PURE__ */ c.jsxs("strong", { children: [
                  E.activityCount,
                  " activities"
                ] }),
                /* @__PURE__ */ c.jsxs("small", { children: [
                  E.incidentCount,
                  " incidents"
                ] })
              ] }),
              /* @__PURE__ */ c.jsx("span", { children: ut(E.startedAt ?? E.createdAt) }),
              /* @__PURE__ */ c.jsx("span", { children: A0(E.startedAt ?? E.createdAt, E.completedAt ?? E.updatedAt) })
            ]
          },
          E.workflowExecutionId
        ))
      ] }),
      /* @__PURE__ */ c.jsx(
        a0,
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
function a0({ ai: e, action: t, summary: n, details: o, state: r, error: i }) {
  return n ? /* @__PURE__ */ c.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Workflow instance details", children: [
    /* @__PURE__ */ c.jsxs("header", { children: [
      /* @__PURE__ */ c.jsxs("div", { children: [
        /* @__PURE__ */ c.jsx("span", { children: "Workflow instance" }),
        /* @__PURE__ */ c.jsx("h3", { children: n.workflowExecutionId })
      ] }),
      t ? /* @__PURE__ */ c.jsxs("button", { type: "button", onClick: () => Xt(e, t, o ?? n), children: [
        /* @__PURE__ */ c.jsx(Ft, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    /* @__PURE__ */ c.jsxs("dl", { className: "wf-instance-meta", children: [
      /* @__PURE__ */ c.jsx("dt", { children: "Status" }),
      /* @__PURE__ */ c.jsx("dd", { children: /* @__PURE__ */ c.jsx(ji, { status: n.status, subStatus: n.subStatus }) }),
      /* @__PURE__ */ c.jsx("dt", { children: "Artifact" }),
      /* @__PURE__ */ c.jsxs("dd", { children: [
        n.artifactId,
        " ",
        /* @__PURE__ */ c.jsx("small", { children: n.artifactVersion })
      ] }),
      /* @__PURE__ */ c.jsx("dt", { children: "Definition" }),
      /* @__PURE__ */ c.jsxs("dd", { children: [
        n.definitionId,
        " ",
        /* @__PURE__ */ c.jsx("small", { children: n.definitionVersionId })
      ] }),
      /* @__PURE__ */ c.jsx("dt", { children: "Created" }),
      /* @__PURE__ */ c.jsx("dd", { children: ut(n.createdAt) }),
      /* @__PURE__ */ c.jsx("dt", { children: "Started" }),
      /* @__PURE__ */ c.jsx("dd", { children: ut(n.startedAt) }),
      /* @__PURE__ */ c.jsx("dt", { children: "Completed" }),
      /* @__PURE__ */ c.jsx("dd", { children: ut(n.completedAt) }),
      /* @__PURE__ */ c.jsx("dt", { children: "Correlation" }),
      /* @__PURE__ */ c.jsx("dd", { children: n.correlationId || "None" })
    ] }),
    r === "loading" ? /* @__PURE__ */ c.jsx("div", { className: "wf-empty", children: "Loading instance details..." }) : null,
    r === "failed" ? /* @__PURE__ */ c.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ c.jsx(Nt, { size: 16 }),
      " ",
      i
    ] }) : null,
    r === "ready" && o ? /* @__PURE__ */ c.jsxs(c.Fragment, { children: [
      /* @__PURE__ */ c.jsx(c0, { activities: o.activities }),
      /* @__PURE__ */ c.jsx(l0, { incidents: o.incidents })
    ] }) : null
  ] }) : /* @__PURE__ */ c.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ c.jsx("div", { className: "wf-empty", children: "Select a workflow instance to inspect activity history." }) });
}
function c0({ activities: e }) {
  return /* @__PURE__ */ c.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ c.jsx("h4", { children: "Activity history" }),
    e.length === 0 ? /* @__PURE__ */ c.jsx("p", { children: "No activity executions recorded yet." }) : null,
    e.length > 0 ? /* @__PURE__ */ c.jsx("div", { className: "wf-instance-activity-list", children: e.map((t) => /* @__PURE__ */ c.jsxs("div", { className: "wf-instance-activity", children: [
      /* @__PURE__ */ c.jsx("span", { children: /* @__PURE__ */ c.jsx(ji, { status: t.status, subStatus: t.subStatus }) }),
      /* @__PURE__ */ c.jsx("strong", { children: vl(t.activityType) ?? t.activityType }),
      /* @__PURE__ */ c.jsx("small", { children: t.activityExecutionId }),
      /* @__PURE__ */ c.jsx("time", { children: ut(t.scheduledAt) })
    ] }, t.activityExecutionId)) }) : null
  ] });
}
function l0({ incidents: e }) {
  return /* @__PURE__ */ c.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ c.jsx("h4", { children: "Incidents" }),
    e.length === 0 ? /* @__PURE__ */ c.jsx("p", { children: "No incidents recorded." }) : null,
    e.map((t) => /* @__PURE__ */ c.jsxs("div", { className: "wf-instance-incident", "data-severity": t.severity.toLowerCase(), children: [
      /* @__PURE__ */ c.jsx("strong", { children: t.failureType }),
      /* @__PURE__ */ c.jsxs("span", { children: [
        t.status,
        " · ",
        t.severity
      ] }),
      /* @__PURE__ */ c.jsx("p", { children: t.message })
    ] }, t.incidentId))
  ] });
}
function ji({ status: e, subStatus: t }) {
  return /* @__PURE__ */ c.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
function u0({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const i = pl(n, t), s = n === 0 ? 0 : (e - 1) * t + 1, l = Math.min(e * t, n);
  return /* @__PURE__ */ c.jsxs("div", { className: "wf-pagination", "aria-label": "Workflow definition pagination", children: [
    /* @__PURE__ */ c.jsxs("span", { className: "wf-pagination-summary", "aria-live": "polite", children: [
      "Showing ",
      s,
      "-",
      l,
      " of ",
      n
    ] }),
    /* @__PURE__ */ c.jsxs("label", { className: "wf-page-size", children: [
      "Rows",
      /* @__PURE__ */ c.jsx("select", { value: t, onChange: (u) => r(Number(u.target.value)), children: Uy.map((u) => /* @__PURE__ */ c.jsx("option", { value: u, children: u }, u)) })
    ] }),
    /* @__PURE__ */ c.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ c.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ c.jsx(Jr, { size: 14 }),
        " Previous"
      ] }),
      /* @__PURE__ */ c.jsxs("span", { children: [
        "Page ",
        e,
        " of ",
        i
      ] }),
      /* @__PURE__ */ c.jsxs("button", { type: "button", onClick: () => o(e + 1), disabled: e >= i, "aria-label": "Next page", title: "Next page", children: [
        "Next ",
        /* @__PURE__ */ c.jsx(At, { size: 14 })
      ] })
    ] })
  ] });
}
function d0(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function pl(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Wt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function Xt(e, t, n) {
  const o = t.createPrompt(n);
  o && e.dispatchPrompt(o);
}
function f0(e) {
  const t = Lo(e, "flowchart"), n = Lo(e, "sequence"), o = [
    { value: t?.activityVersionId ?? "kind:flowchart", label: "Flowchart" },
    { value: n?.activityVersionId ?? "kind:sequence", label: "Sequence" }
  ], r = /* @__PURE__ */ new Map();
  for (const s of e.filter(wl)) {
    if (g0(s)) continue;
    const l = s.category || "Uncategorized";
    r.set(l, [...r.get(l) ?? [], s]);
  }
  const i = Array.from(r.entries()).sort(([s], [l]) => s.localeCompare(l)).map(([s, l]) => ({
    name: s,
    activities: l.sort((u, a) => ke(u).localeCompare(ke(a)))
  }));
  return { compositeRoots: o, otherCategories: i };
}
function h0(e, t) {
  return e.rootActivityVersionId ?? Lo(t, e.rootKind)?.activityVersionId ?? `kind:${e.rootKind}`;
}
function p0(e, t) {
  return e.rootActivityVersionId ?? Lo(t, e.rootKind)?.activityVersionId ?? null;
}
function Lo(e, t) {
  return e.find((n) => gl(n) === t);
}
function gl(e) {
  return e ? yl(e) ? "flowchart" : xl(e) ? "sequence" : null : null;
}
function ml(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "Uncategorized";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [o]) => n.localeCompare(o)).map(([n, o]) => ({
    category: n,
    activities: o.sort((r, i) => ke(r).localeCompare(ke(i)))
  }));
}
function g0(e) {
  return yl(e) || xl(e);
}
function yl(e) {
  return ke(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function xl(e) {
  return ke(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function wl(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function m0(e) {
  return e.sourceKind || e.sourceId || e.sourceVersion ? [e.sourceKind, e.sourceId, e.sourceVersion].filter(Boolean).join(" / ") : e.definitionId;
}
function y0(e) {
  return x0(e.rootActivityType) || e.rootActivityType;
}
function x0(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function w0(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    go(t, n.typeName, n), go(t, n.name, n), go(t, n.displayName, n);
    const o = n.typeName.split(".").filter(Boolean).at(-1);
    go(t, o, n);
  }
  return t;
}
function v0(e, t, n) {
  const o = t.get(e.activityVersionId);
  return n.get(yn(o?.activityTypeKey)) ?? n.get(yn(vl(o?.activityTypeKey))) ?? n.get(yn(o?.displayName)) ?? n.get(yn(e.activityVersionId)) ?? null;
}
function go(e, t, n) {
  const o = yn(t);
  o && !e.has(o) && e.set(o, n);
}
function yn(e) {
  return e?.trim().toLowerCase() ?? "";
}
function vl(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function ea(e, t, n, o) {
  const r = Qo();
  if (!r) return t;
  const i = Number(r.getItem(e));
  return Number.isFinite(i) ? No(i, n, o) : t;
}
function ta(e, t) {
  const n = Qo();
  if (!n) return t;
  const o = n.getItem(e);
  return o === "true" ? !0 : o === "false" ? !1 : t;
}
function b0() {
  const e = Qo();
  if (!e) return null;
  const t = e.getItem(fl);
  return t === "palette" || t === "inspector" ? t : null;
}
function Qo() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function hn(e, t) {
  const n = Qo();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function No(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function S0({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: o,
  workflowDesignerPanels: r,
  onBack: i
}) {
  const [s, l] = q(null), [u, a] = q(null), [d, f] = q([]), [h, g] = q([]), [m, x] = q(bo), [v, y] = q("loading"), [b, p] = q([]), [w, E] = q([]), [k, _] = q([]), [D, $] = q(null), [W, j] = q(null), [z, H] = q(null), [S, I] = q(null), [C, A] = q(""), [T, P] = q(""), [V, F] = q(!1), [O, G] = q(null), [K, ne] = q(() => /* @__PURE__ */ new Set()), [ae, U] = q(() => ea(qs, Qy, ln, un)), [L, Y] = q(() => ea(Zs, Jy, dn, fn)), [re, ie] = q(() => ta(Ks, !1)), [Z, ee] = q(() => ta(Us, !1)), [oe, R] = q(b0), [J, fe] = q("activities"), [we, Oe] = q("inspector"), ve = ce(null), De = ce(null), tt = ce(""), Fe = ce(0), Be = ce(null), Ce = ce(!1), Ae = u?.state.rootActivity ?? null, Me = ge(() => new Map(d.map((N) => [N.activityVersionId, N])), [d]), Ue = ge(() => w0(h), [h]), Te = ge(() => ol(Ae, b), [Ae, b]), Et = uy(Te, Te ? Me.get(Te.activityVersionId) : void 0), xe = !!Te && Et === "unsupported", We = ge(() => xe ? null : Lr(Ae, b), [Ae, b, xe]), qt = ge(() => ml(d), [d]), Ee = ge(() => xe && Te?.nodeId === W ? Te : We?.slot.activities.find((N) => N.nodeId === W) ?? null, [xe, We, Te, W]), Zt = ge(
    () => Ee ? v0(Ee, Me, Ue) : null,
    [Me, Ue, Ee]
  ), Kt = Ee ? Je(Ee) : [], _e = Et === "flowchart" && We?.slot.mode === "flowchart", ft = !Ae || !xe, Rn = Wt(n, "weaver.workflows.find-draft-risks"), Hn = Wt(n, "weaver.workflows.propose-update");
  se(() => {
    hn(qs, String(ae));
  }, [ae]), se(() => {
    hn(Zs, String(L));
  }, [L]), se(() => {
    hn(Ks, String(re));
  }, [re]), se(() => {
    hn(Us, String(Z));
  }, [Z]), se(() => {
    hn(fl, oe);
  }, [oe]), se(() => {
    if (!oe) return;
    const N = (M) => {
      M.key === "Escape" && R(null);
    };
    return window.addEventListener("keydown", N), () => window.removeEventListener("keydown", N);
  }, [oe]);
  const Ut = pe(async () => {
    A(""), y("loading");
    const [N, M, B, X] = await Promise.all([
      Km(e, t),
      tl(e),
      sy(e).then(
        (de) => ({ ok: !0, descriptors: de }),
        () => ({ ok: !1, descriptors: [] })
      ),
      ay(e).then(
        (de) => ({ ok: !0, descriptors: de }),
        () => ({ ok: !1, descriptors: bo })
      )
    ]), Q = N.draft ?? null;
    l(N), tt.current = Q ? pn(Q) : "", a(Q), f(M.activities ?? []), g(B.descriptors), x(X.descriptors.length > 0 ? X.descriptors : bo), y(B.ok ? "ready" : "failed"), p([]), j(null);
  }, [e, t]);
  se(() => {
    Ut().catch((N) => A(N instanceof Error ? N.message : String(N)));
  }, [Ut]), se(() => {
    ne((N) => {
      let M = !1;
      const B = new Set(N);
      for (const X of qt)
        B.has(X.category) || (B.add(X.category), M = !0);
      return M ? B : N;
    });
  }, [qt]), se(() => {
    if (!Te) {
      E([]), _([]);
      return;
    }
    const N = xe ? ly(Te, d, u?.layout ?? []) : We ? cy(We, d, u?.layout ?? []) : { nodes: [], edges: [] };
    E(N.nodes), _(N.edges);
  }, [d, u?.layout, xe, We, Te]);
  const Jo = (N) => {
    a((M) => M && { ...M, state: { ...M.state, rootActivity: N } });
  }, Gt = pe((N, M) => {
    if (u?.state.rootActivity && xe)
      return;
    const B = Bs(N, ra(N));
    if (!u?.state.rootActivity) {
      Jo(B), j(B.nodeId);
      return;
    }
    if (!We) {
      if (!Je(B)[0]) {
        P(""), A("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      a((Q) => {
        if (!Q?.state.rootActivity) return Q;
        const de = Q.state.rootActivity, le = ti(B, [], [de]), he = M ? [
          ...Q.layout.filter((Pe) => Pe.nodeId !== de.nodeId),
          {
            nodeId: de.nodeId,
            x: Math.round(M.x),
            y: Math.round(M.y)
          }
        ] : Q.layout;
        return {
          ...Q,
          layout: he,
          state: {
            ...Q.state,
            rootActivity: le
          }
        };
      }), j(u.state.rootActivity.nodeId), A(""), P(`Wrapped root in ${ke(N)}`);
      return;
    }
    a((X) => {
      if (!X?.state.rootActivity) return X;
      const Q = Lr(X.state.rootActivity, b);
      if (!Q) return X;
      const de = ti(X.state.rootActivity, b, [...Q.slot.activities, B]), le = M ? [
        ...X.layout.filter((he) => he.nodeId !== B.nodeId),
        {
          nodeId: B.nodeId,
          x: Math.round(M.x),
          y: Math.round(M.y)
        }
      ] : X.layout;
      return {
        ...X,
        layout: le,
        state: {
          ...X.state,
          rootActivity: de
        }
      };
    }), j(B.nodeId);
  }, [u?.state.rootActivity, b, xe, We]), kt = pe((N, M) => {
    const B = Bs(N, ra(N)), X = {
      id: B.nodeId,
      type: "workflowActivity",
      position: M,
      selected: !0,
      data: {
        label: ke(N),
        activityVersionId: N.activityVersionId,
        activityTypeKey: N.activityTypeKey,
        category: N.category,
        executionType: N.executionType,
        icon: ni(N),
        childSlots: Je(B),
        acceptsInbound: String(N.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: al(B, N)
      }
    };
    return { activityNode: B, node: X };
  }, []), ze = pe((N, M, B = []) => {
    xe || a((X) => {
      if (!X) return X;
      const Q = hy(X.layout, N), de = X.state.rootActivity;
      if (!de) return { ...X, layout: Q };
      const le = Lr(de, b);
      if (!le) return { ...X, layout: Q };
      const he = dy(le, N, M, B), Pe = le.slot.mode === "flowchart" ? fy(he, M) : he;
      return {
        ...X,
        layout: Q,
        state: {
          ...X.state,
          rootActivity: rl(de, b, Pe)
        }
      };
    });
  }, [b, xe]), Qt = pe((N, M) => {
    if (!ve.current) return null;
    const B = ve.current.getBoundingClientRect();
    return D ? D.screenToFlowPosition({ x: N, y: M }) : {
      x: N - B.left,
      y: M - B.top
    };
  }, [D]), Jt = pe((N, M) => document.elementFromPoint(N, M)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), en = pe((N, M, B) => {
    const X = w.find((Ie) => Ie.id === M.source), Q = w.find((Ie) => Ie.id === M.target), de = X && Q ? I0(X, Q) : X ? ia(X) : B, le = kt(N, de), Pe = [...w.map((Ie) => Ie.selected ? { ...Ie, selected: !1 } : Ie), le.node], ht = Ny(k, M, le.node.id);
    E(Pe), _(ht), j(le.node.id), ze(Pe, ht, [le.activityNode]);
  }, [ze, kt, k, w]), Ct = pe((N, M, B) => {
    if (!ft || !ve.current) return !1;
    const X = ve.current.getBoundingClientRect();
    if (!(M >= X.left && M <= X.right && B >= X.top && B <= X.bottom)) return !1;
    const de = Qt(M, B);
    if (!de) return !1;
    if (_e) {
      const le = Jt(M, B), he = le ? k.find((Pe) => Pe.id === le) : void 0;
      if (he)
        return en(N, he, de), !0;
    }
    return Gt(N, de), !0;
  }, [Gt, ft, k, Jt, _e, en, Qt]);
  se(() => {
    const N = (B) => {
      const X = Be.current;
      if (!X) return;
      Math.hypot(B.clientX - X.startX, B.clientY - X.startY) >= Zy && (X.dragging = !0);
    }, M = (B) => {
      const X = Be.current;
      if (Be.current = null, !X?.dragging || !ve.current) return;
      const Q = ve.current.getBoundingClientRect();
      B.clientX >= Q.left && B.clientX <= Q.right && B.clientY >= Q.top && B.clientY <= Q.bottom && (Ce.current = !0, window.setTimeout(() => {
        Ce.current = !1;
      }, 0), Ct(X.activity, B.clientX, B.clientY));
    };
    return window.addEventListener("pointermove", N), window.addEventListener("pointerup", M), window.addEventListener("pointercancel", M), () => {
      window.removeEventListener("pointermove", N), window.removeEventListener("pointerup", M), window.removeEventListener("pointercancel", M);
    };
  }, [D, Ct]);
  const er = (N, M) => {
    N.dataTransfer.setData(Ys, M.activityVersionId), N.dataTransfer.setData("text/plain", M.activityVersionId), N.dataTransfer.effectAllowed = "copy";
  }, tr = (N, M) => {
    N.clientX === 0 && N.clientY === 0 || Ct(M, N.clientX, N.clientY) && (Ce.current = !0, window.setTimeout(() => {
      Ce.current = !1;
    }, 0));
  }, nr = (N, M) => {
    N.button === 0 && (Be.current = {
      activity: M,
      startX: N.clientX,
      startY: N.clientY,
      dragging: !1
    });
  }, or = (N) => {
    Ce.current || ft && Gt(N);
  }, Vn = (N) => {
    if (!ft) {
      N.dataTransfer.dropEffect = "none";
      return;
    }
    if (N.preventDefault(), N.dataTransfer.dropEffect = "copy", !_e) return;
    const M = Jt(N.clientX, N.clientY);
    I(M);
  }, On = (N) => {
    if (!ve.current) return;
    const M = N.relatedTarget;
    M && ve.current.contains(M) || I(null);
  }, Bn = (N) => {
    if (N.preventDefault(), I(null), !ft) return;
    const M = N.dataTransfer.getData(Ys) || N.dataTransfer.getData("text/plain"), B = Me.get(M);
    B && Ct(B, N.clientX, N.clientY);
  }, rr = () => {
    if (!_e) return;
    const N = ve.current?.getBoundingClientRect();
    N && H({
      kind: "fromEmpty",
      clientX: N.left + N.width / 2,
      clientY: N.top + N.height / 2
    });
  }, tn = pe(async (N, M) => {
    const B = ++Fe.current, X = pn(N);
    A("");
    try {
      const Q = await ey(e, N), de = pn(Q);
      tt.current = de, a((le) => !le || le.id !== Q.id ? le : pn(le) === X ? Q : { ...le, validationErrors: Q.validationErrors }), B === Fe.current && P(M);
    } catch (Q) {
      B === Fe.current && (P(""), A(Q instanceof Error ? Q.message : String(Q)));
    }
  }, [e]);
  se(() => {
    if (!V || !u || pn(u) === tt.current) return;
    P("Autosaving...");
    const M = window.setTimeout(() => {
      tn(u, "Autosaved");
    }, Ky);
    return () => window.clearTimeout(M);
  }, [V, u, tn]);
  const ir = async () => {
    u && (P("Saving..."), await tn(u, "Saved"));
  }, sr = async () => {
    if (u) {
      P("Promoting...");
      try {
        const N = await ty(e, u.id), M = await ny(e, N.versionId);
        G(M.artifactId), P(`Published ${M.artifactVersion}`), await Ut();
      } catch (N) {
        P(""), A(N instanceof Error ? N.message : String(N));
      }
    }
  }, Fn = async () => {
    if (O) {
      P("Running...");
      try {
        await el(e, O), P("Run dispatched");
      } catch (N) {
        P(""), A(N instanceof Error ? N.message : String(N));
      }
    }
  }, Wn = (N) => {
    const M = xe ? N.filter((B) => B.type === "select") : N;
    M.length !== 0 && E((B) => wc(M, B));
  }, Xn = (N) => {
    xe || _((M) => vc(N, M));
  }, nn = (N) => !N.source || !N.target || N.source === N.target || !_e ? !1 : !N.targetHandle, ar = (N) => {
    if (!u?.state.rootActivity || !We || !_e || !nn(N)) return;
    const M = zo(N.source, N.target, N.sourceHandle ?? "Done", N.targetHandle ?? void 0), B = Sc(M, k);
    _(B), ze(w, B);
  }, cr = () => {
    ze(w, k);
  }, lr = (N, M) => {
    if (!M.nodeId || M.handleType === "target") {
      De.current = null;
      return;
    }
    De.current = {
      nodeId: M.nodeId,
      handleId: M.handleId ?? null
    };
  }, ur = (N) => {
    const M = De.current;
    if (De.current = null, !M || !_e || N.target?.closest(".react-flow__handle, .react-flow__node")) return;
    const X = j0(N);
    H({
      kind: "fromPort",
      sourceNodeId: M.nodeId,
      sourceHandleId: M.handleId,
      clientX: X.x,
      clientY: X.y
    });
  }, dr = (N, M) => {
    if (!_e || !nn(M)) return;
    const B = Rp(N, {
      ...M,
      sourceHandle: M.sourceHandle ?? "Done",
      targetHandle: M.targetHandle ?? void 0
    }, k, { shouldReplaceId: !1 });
    _(B), ze(w, B);
  }, Yn = (N) => {
    if (xe || N.length === 0) return;
    const M = new Set(N.map((Q) => Q.id)), B = w.filter((Q) => !M.has(Q.id)), X = k.filter((Q) => !M.has(Q.source) && !M.has(Q.target));
    E(B), _(X), W && M.has(W) && j(null), ze(B, X);
  }, fr = (N) => {
    if (xe || N.length === 0) return;
    const M = new Set(N.map((X) => X.id)), B = k.filter((X) => !M.has(X.id));
    _(B), ze(w, B);
  }, qn = pe((N) => {
    if (xe) return;
    const M = k.filter((B) => B.id !== N);
    _(M), ze(w, M);
  }, [ze, k, xe, w]), Zn = pe((N, M, B) => {
    _e && H({ kind: "spliceEdge", edgeId: N, clientX: M, clientY: B });
  }, [_e]), hr = (N) => {
    const M = z;
    if (!M) return;
    H(null);
    const B = Qt(M.clientX, M.clientY) ?? { x: 0, y: 0 };
    if (M.kind === "fromEmpty") {
      const Q = kt(N, B), le = [...w.map((he) => he.selected ? { ...he, selected: !1 } : he), Q.node];
      E(le), j(Q.node.id), ze(le, k, [Q.activityNode]);
      return;
    }
    if (M.kind === "fromPort") {
      const Q = w.find((Ie) => Ie.id === M.sourceNodeId), de = Q ? ia(Q) : B, le = kt(N, de), Pe = [...w.map((Ie) => Ie.selected ? { ...Ie, selected: !1 } : Ie), le.node], ht = [...k, zo(M.sourceNodeId, le.node.id, M.sourceHandleId ?? "Done")];
      E(Pe), _(ht), j(le.node.id), ze(Pe, ht, [le.activityNode]);
      return;
    }
    const X = k.find((Q) => Q.id === M.edgeId);
    X && en(N, X, B);
  }, pr = ge(() => ({
    highlightedEdgeId: S,
    deleteEdge: qn,
    requestInsertActivity: Zn
  }), [qn, S, Zn]), gr = (N, M, B) => {
    p((X) => [...X, { ownerNodeId: N.nodeId, slotId: M, label: B }]), j(null);
  }, mr = pe((N) => {
    a((M) => {
      const B = M?.state.rootActivity;
      return !M || !B ? M : {
        ...M,
        state: {
          ...M.state,
          rootActivity: il(B, N.nodeId, () => N)
        }
      };
    });
  }, []), yr = (N) => {
    ne((M) => {
      const B = new Set(M);
      return B.has(N) ? B.delete(N) : B.add(N), B;
    });
  }, on = (N) => {
    R((M) => M === N ? null : M), N === "palette" ? ie((M) => !M) : ee((M) => !M);
  }, Kn = (N) => {
    N === "palette" ? ie(!1) : ee(!1), R((M) => M === N ? null : N);
  }, Un = (N, M) => {
    R(null), N === "palette" ? (ie(!1), U((B) => No(B + M, ln, un))) : (ee(!1), Y((B) => No(B + M, dn, fn)));
  }, Gn = (N, M) => {
    M.preventDefault(), R(null), N === "palette" ? ie(!1) : ee(!1);
    const B = M.clientX, X = N === "palette" ? ae : L, Q = N === "palette" ? ln : dn, de = N === "palette" ? un : fn;
    document.body.classList.add("wf-side-panel-resizing");
    const le = (Pe) => {
      const ht = N === "palette" ? Pe.clientX - B : B - Pe.clientX, Ie = No(X + ht, Q, de);
      N === "palette" ? U(Ie) : Y(Ie);
    }, he = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", le), window.removeEventListener("pointerup", he), window.removeEventListener("pointercancel", he);
    };
    window.addEventListener("pointermove", le), window.addEventListener("pointerup", he), window.addEventListener("pointercancel", he);
  }, Qn = (N, M) => {
    M.key === "ArrowLeft" ? (M.preventDefault(), Un(N, N === "palette" ? -po : po)) : M.key === "ArrowRight" ? (M.preventDefault(), Un(N, N === "palette" ? po : -po)) : M.key === "Home" ? (M.preventDefault(), N === "palette" ? U(ln) : Y(dn)) : M.key === "End" && (M.preventDefault(), N === "palette" ? U(un) : Y(fn));
  };
  if (!s || !u)
    return /* @__PURE__ */ c.jsx("div", { className: "wf-empty", children: C || "Loading workflow editor..." });
  const xr = [
    "wf-editor-body",
    re ? "palette-collapsed" : "",
    Z ? "inspector-collapsed" : "",
    oe === "palette" ? "palette-maximized" : "",
    oe === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), wr = {
    "--wf-palette-width": `${re ? Gs : ae}px`,
    "--wf-inspector-width": `${Z ? Gs : L}px`
  }, Jn = !re && oe !== "inspector", eo = !Z && oe !== "palette", to = {
    definition: s.definition,
    draft: u,
    selectedActivity: Ee,
    selectedActivityDescriptor: Zt,
    selectedActivitySlots: Kt,
    catalog: d,
    currentScopeOwner: Te,
    frames: b
  }, no = r.map((N) => {
    const M = N.component;
    return {
      id: N.id,
      title: N.title,
      side: N.side,
      order: N.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ c.jsx(M, { context: to })
    };
  }), _t = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ c.jsx(Gc, { size: 15 }),
      render: vr
    },
    ...no.filter((N) => N.side === "left")
  ].sort(oa), rn = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ c.jsx(Jc, { size: 15 }),
      render: br
    },
    ...no.filter((N) => N.side === "right")
  ].sort(oa), oo = _t.find((N) => N.id === J) ?? _t[0], It = rn.find((N) => N.id === we) ?? rn[0];
  function vr() {
    return /* @__PURE__ */ c.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: qt.map((N) => {
      const M = K.has(N.category);
      return /* @__PURE__ */ c.jsxs("div", { className: "wf-palette-category", children: [
        /* @__PURE__ */ c.jsxs(
          "button",
          {
            type: "button",
            className: "wf-palette-category-toggle",
            role: "treeitem",
            "aria-expanded": M,
            onClick: () => yr(N.category),
            children: [
              M ? /* @__PURE__ */ c.jsx(Vm, { size: 14 }) : /* @__PURE__ */ c.jsx(At, { size: 14 }),
              /* @__PURE__ */ c.jsx("span", { children: N.category }),
              /* @__PURE__ */ c.jsx("small", { children: N.activities.length })
            ]
          }
        ),
        M ? /* @__PURE__ */ c.jsx("div", { className: "wf-palette-activities", role: "group", children: N.activities.map((B) => {
          const X = B.description?.trim(), Q = X ? `wf-palette-description-${B.activityVersionId}` : void 0, de = ke(B), le = ni(B);
          return /* @__PURE__ */ c.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-activity",
              role: "treeitem",
              draggable: !0,
              title: X || ke(B),
              "aria-describedby": Q,
              onClick: () => or(B),
              onDragStart: (he) => er(he, B),
              onDragEnd: (he) => tr(he, B),
              onPointerDown: (he) => nr(he, B),
              children: [
                /* @__PURE__ */ c.jsx("span", { className: "wf-activity-icon", "data-icon": le, "aria-hidden": "true", children: bl(le) }),
                /* @__PURE__ */ c.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ c.jsx("strong", { children: de }),
                  X ? /* @__PURE__ */ c.jsx("small", { id: Q, children: X }) : null
                ] }),
                /* @__PURE__ */ c.jsx(Om, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            B.activityVersionId
          );
        }) }) : null
      ] }, N.category);
    }) });
  }
  function br() {
    return Ee ? /* @__PURE__ */ c.jsxs("div", { className: "wf-inspector-content", children: [
      /* @__PURE__ */ c.jsx("h3", { children: w.find((N) => N.id === Ee.nodeId)?.data.label ?? Ee.nodeId }),
      /* @__PURE__ */ c.jsxs("dl", { children: [
        /* @__PURE__ */ c.jsx("dt", { children: "Node ID" }),
        /* @__PURE__ */ c.jsx("dd", { children: Ee.nodeId }),
        /* @__PURE__ */ c.jsx("dt", { children: "Activity type" }),
        /* @__PURE__ */ c.jsx("dd", { children: Zt?.typeName ?? Me.get(Ee.activityVersionId)?.activityTypeKey ?? "Unknown" }),
        /* @__PURE__ */ c.jsx("dt", { children: "Activity version" }),
        /* @__PURE__ */ c.jsx("dd", { children: Ee.activityVersionId })
      ] }),
      /* @__PURE__ */ c.jsx(
        Hy,
        {
          activity: Ee,
          descriptor: Zt,
          editors: o,
          expressionDescriptors: m,
          descriptorStatus: v,
          onChange: mr
        }
      ),
      Kt.length > 0 ? /* @__PURE__ */ c.jsxs("div", { className: "wf-slot-list", children: [
        /* @__PURE__ */ c.jsx("span", { children: "Embedded slots" }),
        Kt.map((N) => /* @__PURE__ */ c.jsxs("button", { type: "button", onClick: () => gr(Ee, N.id, `${w.find((M) => M.id === Ee.nodeId)?.data.label ?? Ee.nodeId} / ${N.label}`), children: [
          N.label,
          /* @__PURE__ */ c.jsxs("small", { children: [
            N.activities.length,
            " activit",
            N.activities.length === 1 ? "y" : "ies"
          ] })
        ] }, N.id))
      ] }) : /* @__PURE__ */ c.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
    ] }) : /* @__PURE__ */ c.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
  }
  return /* @__PURE__ */ c.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ c.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ c.jsx("button", { type: "button", className: "wf-link-button", onClick: i, children: "Definitions" }),
      /* @__PURE__ */ c.jsx(At, { size: 14 }),
      /* @__PURE__ */ c.jsx("strong", { children: s.definition.name }),
      /* @__PURE__ */ c.jsx("span", { className: "wf-chip", children: "Draft" }),
      T ? /* @__PURE__ */ c.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ c.jsx(Uo, { size: 13 }),
        " ",
        T
      ] }) : null,
      /* @__PURE__ */ c.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ c.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ c.jsx("input", { type: "checkbox", checked: V, onChange: (N) => F(N.target.checked) }),
          /* @__PURE__ */ c.jsx("span", { children: "Autosave" })
        ] }),
        Rn ? /* @__PURE__ */ c.jsxs("button", { type: "button", onClick: () => Xt(n, Rn, { definition: s.definition, draft: u }), children: [
          /* @__PURE__ */ c.jsx(Ft, { size: 15 }),
          " Risks"
        ] }) : null,
        Hn ? /* @__PURE__ */ c.jsxs("button", { type: "button", onClick: () => Xt(n, Hn, { definition: s.definition, draft: u }), children: [
          /* @__PURE__ */ c.jsx(Ft, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ c.jsxs("button", { type: "button", onClick: () => {
          ir();
        }, children: [
          /* @__PURE__ */ c.jsx(Fm, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ c.jsxs("button", { type: "button", onClick: () => {
          sr();
        }, children: [
          /* @__PURE__ */ c.jsx(Qc, { size: 15 }),
          " Promote"
        ] }),
        /* @__PURE__ */ c.jsxs("button", { type: "button", disabled: !O, onClick: () => {
          Fn();
        }, children: [
          /* @__PURE__ */ c.jsx(Ei, { size: 15 }),
          " Run"
        ] })
      ] })
    ] }),
    C ? /* @__PURE__ */ c.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ c.jsx(Nt, { size: 16 }),
      " ",
      C
    ] }) : null,
    /* @__PURE__ */ c.jsxs("div", { className: xr, style: wr, children: [
      /* @__PURE__ */ c.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ c.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ c.jsx(
            na,
            {
              label: "Activities panel tabs",
              tabs: _t,
              activeTabId: oo.id,
              onSelect: fe
            }
          ),
          /* @__PURE__ */ c.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ c.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": re ? "Expand activities panel" : "Collapse activities panel",
                title: re ? "Expand" : "Collapse",
                onClick: () => on("palette"),
                children: re ? /* @__PURE__ */ c.jsx(At, { size: 14 }) : /* @__PURE__ */ c.jsx(Jr, { size: 14 })
              }
            ),
            re ? null : /* @__PURE__ */ c.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": oe === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: oe === "palette" ? "Restore" : "Maximize",
                onClick: () => Kn("palette"),
                children: oe === "palette" ? /* @__PURE__ */ c.jsx(Vs, { size: 14 }) : /* @__PURE__ */ c.jsx(To, { size: 14 })
              }
            )
          ] })
        ] }),
        Jn ? oo.render() : null
      ] }),
      Jn && !oe ? /* @__PURE__ */ c.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": ln,
          "aria-valuemax": un,
          "aria-valuenow": ae,
          tabIndex: 0,
          onPointerDown: (N) => Gn("palette", N),
          onKeyDown: (N) => Qn("palette", N)
        }
      ) : /* @__PURE__ */ c.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ c.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ c.jsxs("div", { className: "wf-breadcrumb", children: [
          /* @__PURE__ */ c.jsx("button", { type: "button", onClick: () => {
            p([]), j(null);
          }, children: "Root" }),
          b.map((N, M) => /* @__PURE__ */ c.jsxs(dt.Fragment, { children: [
            /* @__PURE__ */ c.jsx(At, { size: 13 }),
            /* @__PURE__ */ c.jsx("button", { type: "button", onClick: () => {
              p(b.slice(0, M + 1)), j(null);
            }, children: N.label })
          ] }, `${N.ownerNodeId}-${N.slotId}-${M}`))
        ] }),
        /* @__PURE__ */ c.jsxs("div", { className: "wf-canvas", ref: ve, onDragOver: Vn, onDragLeave: On, onDrop: Bn, children: [
          /* @__PURE__ */ c.jsx(hl.Provider, { value: pr, children: /* @__PURE__ */ c.jsxs(
            sm,
            {
              nodes: w,
              edges: k,
              nodeTypes: Yy,
              edgeTypes: qy,
              onInit: $,
              onNodesChange: Wn,
              onEdgesChange: Xn,
              onNodesDelete: Yn,
              onEdgesDelete: fr,
              onConnect: ar,
              onConnectStart: _e ? lr : void 0,
              onConnectEnd: _e ? ur : void 0,
              onReconnect: _e ? dr : void 0,
              isValidConnection: nn,
              onDragOver: Vn,
              onDragLeave: On,
              onDrop: Bn,
              onPaneClick: () => j(null),
              onNodeClick: (N, M) => j(M.id),
              onNodeDragStop: xe ? void 0 : cr,
              fitView: !0,
              minZoom: 0.2,
              maxZoom: 1.8,
              nodesConnectable: _e,
              nodesDraggable: !xe,
              selectionOnDrag: !0,
              multiSelectionKeyCode: ["Shift", "Meta", "Control"],
              deleteKeyCode: xe ? null : ["Backspace", "Delete"],
              panActivationKeyCode: null,
              defaultEdgeOptions: { type: "workflow" },
              children: [
                /* @__PURE__ */ c.jsx(hm, { gap: 18, size: 1 }),
                /* @__PURE__ */ c.jsx(vm, {}),
                /* @__PURE__ */ c.jsx(Pm, { pannable: !0, zoomable: !0 })
              ]
            }
          ) }),
          _e && w.length === 0 ? /* @__PURE__ */ c.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => rr(), children: [
            /* @__PURE__ */ c.jsx(ki, { size: 15 }),
            " Add activity"
          ] }) : null,
          z ? /* @__PURE__ */ c.jsx(
            C0,
            {
              clientX: z.clientX,
              clientY: z.clientY,
              activities: d,
              onPick: hr,
              onClose: () => H(null)
            }
          ) : null
        ] }),
        /* @__PURE__ */ c.jsx(_0, { draft: u })
      ] }),
      eo && !oe ? /* @__PURE__ */ c.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": dn,
          "aria-valuemax": fn,
          "aria-valuenow": L,
          tabIndex: 0,
          onPointerDown: (N) => Gn("inspector", N),
          onKeyDown: (N) => Qn("inspector", N)
        }
      ) : /* @__PURE__ */ c.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ c.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ c.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ c.jsx(
            na,
            {
              label: "Inspector panel tabs",
              tabs: rn,
              activeTabId: It.id,
              onSelect: Oe
            }
          ),
          /* @__PURE__ */ c.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ c.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": Z ? "Expand inspector panel" : "Collapse inspector panel",
                title: Z ? "Expand" : "Collapse",
                onClick: () => on("inspector"),
                children: Z ? /* @__PURE__ */ c.jsx(Jr, { size: 14 }) : /* @__PURE__ */ c.jsx(At, { size: 14 })
              }
            ),
            Z ? null : /* @__PURE__ */ c.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": oe === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: oe === "inspector" ? "Restore" : "Maximize",
                onClick: () => Kn("inspector"),
                children: oe === "inspector" ? /* @__PURE__ */ c.jsx(Vs, { size: 14 }) : /* @__PURE__ */ c.jsx(To, { size: 14 })
              }
            )
          ] })
        ] }),
        eo ? It.render() : null
      ] })
    ] })
  ] });
}
function na({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: o
}) {
  return /* @__PURE__ */ c.jsx("div", { className: "wf-panel-tab-list", role: "tablist", "aria-label": e, children: t.map((r) => /* @__PURE__ */ c.jsxs(
    "button",
    {
      type: "button",
      role: "tab",
      "aria-selected": r.id === n,
      className: r.id === n ? "active" : "",
      title: r.title,
      onClick: () => o(r.id),
      children: [
        r.icon ? /* @__PURE__ */ c.jsx("span", { className: "wf-panel-tab-icon", "aria-hidden": "true", children: r.icon }) : null,
        /* @__PURE__ */ c.jsx("span", { children: r.title })
      ]
    },
    r.id
  )) });
}
function oa(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
function N0({ data: e, selected: t }) {
  const n = e, o = !n.suppressFlowPorts, r = o ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], i = E0(n);
  return /* @__PURE__ */ c.jsxs("div", { className: t ? "wf-node selected" : "wf-node", "data-icon": n.icon ?? "activity", children: [
    o && n.acceptsInbound ? /* @__PURE__ */ c.jsx(Bt, { type: "target", position: te.Left }) : null,
    /* @__PURE__ */ c.jsxs("div", { className: "wf-node-content", children: [
      /* @__PURE__ */ c.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: bl(n.icon) }),
      /* @__PURE__ */ c.jsxs("span", { className: "wf-node-copy", children: [
        /* @__PURE__ */ c.jsx("strong", { children: n.label }),
        i ? /* @__PURE__ */ c.jsx("small", { children: i }) : null
      ] })
    ] }),
    n.childSlots.length > 0 ? /* @__PURE__ */ c.jsxs("span", { className: "wf-node-slot-badge", children: [
      n.childSlots.length,
      " slot",
      n.childSlots.length === 1 ? "" : "s"
    ] }) : null,
    r.map((s, l) => {
      const u = `${(l + 1) / (r.length + 1) * 100}%`;
      return /* @__PURE__ */ c.jsxs(dt.Fragment, { children: [
        /* @__PURE__ */ c.jsx("span", { className: "wf-node-port-label", style: { top: u }, children: s.displayName }),
        /* @__PURE__ */ c.jsx(Bt, { type: "source", position: te.Right, id: s.name, style: { top: u } })
      ] }, s.name);
    })
  ] });
}
function E0(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((r) => !!r).join(" · ");
}
function bl(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ c.jsx(Qc, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ c.jsx(Jc, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ c.jsx(Xm, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ c.jsx(Ei, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ c.jsx(qm, { size: 15 });
    default:
      return /* @__PURE__ */ c.jsx(Gc, { size: 15 });
  }
}
function k0(e) {
  const {
    id: t,
    sourceX: n,
    sourceY: o,
    targetX: r,
    targetY: i,
    sourcePosition: s,
    targetPosition: l,
    markerEnd: u,
    style: a,
    label: d,
    labelStyle: f
  } = e, h = dt.useContext(hl), [g, m] = q(!1), [x, v, y] = Po({ sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: l }), b = h?.highlightedEdgeId === t;
  return /* @__PURE__ */ c.jsxs(c.Fragment, { children: [
    /* @__PURE__ */ c.jsx(
      zn,
      {
        id: t,
        path: x,
        markerEnd: u,
        style: {
          ...a,
          strokeWidth: b ? 2.5 : a?.strokeWidth
        },
        label: d,
        labelX: v,
        labelY: y,
        labelStyle: f,
        onMouseEnter: () => m(!0),
        onMouseLeave: () => m(!1)
      }
    ),
    h ? /* @__PURE__ */ c.jsx(cm, { children: /* @__PURE__ */ c.jsxs(
      "div",
      {
        className: ["wf-edge-actions", g ? "visible" : "", b ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${v}px, ${y}px)` },
        onMouseEnter: () => m(!0),
        onMouseLeave: () => m(!1),
        children: [
          /* @__PURE__ */ c.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (p) => h.requestInsertActivity(t, p.clientX, p.clientY), children: /* @__PURE__ */ c.jsx(ki, { size: 12 }) }),
          /* @__PURE__ */ c.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => h.deleteEdge(t), children: /* @__PURE__ */ c.jsx(ei, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function C0({ clientX: e, clientY: t, activities: n, onPick: o, onClose: r }) {
  const [i, s] = q(""), [l, u] = q(0), a = ce(null), d = ce(null), f = ge(() => {
    const b = i.trim().toLowerCase(), p = n.filter(wl);
    return b ? p.filter((w) => ke(w).toLowerCase().includes(b) || w.activityTypeKey.toLowerCase().includes(b) || (w.category ?? "").toLowerCase().includes(b) || (w.description ?? "").toLowerCase().includes(b)) : p;
  }, [n, i]), h = ge(() => ml(f), [f]), g = ge(() => h.flatMap((b) => b.activities), [h]);
  se(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), se(() => {
    const b = (w) => {
      a.current?.contains(w.target) || r();
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
  return /* @__PURE__ */ c.jsxs("div", { ref: a, className: "wf-connect-menu", style: { left: x, top: v }, onMouseDown: (b) => b.stopPropagation(), onClick: (b) => b.stopPropagation(), children: [
    /* @__PURE__ */ c.jsx(
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
    /* @__PURE__ */ c.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: h.length === 0 ? /* @__PURE__ */ c.jsx("p", { children: "No matching activities." }) : h.map((b) => /* @__PURE__ */ c.jsxs("section", { children: [
      /* @__PURE__ */ c.jsx("h4", { children: b.category }),
      b.activities.map((p) => {
        y += 1;
        const w = y, E = w === l;
        return /* @__PURE__ */ c.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": E,
            className: E ? "active" : "",
            onMouseEnter: () => u(w),
            onClick: () => o(p),
            children: [
              /* @__PURE__ */ c.jsx("strong", { children: ke(p) }),
              /* @__PURE__ */ c.jsx("small", { children: p.category || p.activityTypeKey })
            ]
          },
          p.activityVersionId
        );
      })
    ] }, b.category)) })
  ] });
}
function _0({ draft: e }) {
  return e.validationErrors.length ? /* @__PURE__ */ c.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ c.jsx(Nt, { size: 14 }),
    e.validationErrors.length,
    " validation issue",
    e.validationErrors.length === 1 ? "" : "s"
  ] }) : /* @__PURE__ */ c.jsxs("div", { className: "wf-validation ok", children: [
    /* @__PURE__ */ c.jsx(Uo, { size: 14 }),
    " No validation errors"
  ] });
}
function ra(e) {
  return `${ke(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function ia(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function I0(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function j0(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function pn(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function ut(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function A0(e, t) {
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
  P0 as register
};
