import ut, { memo as be, forwardRef as Lo, useRef as ce, useEffect as ae, useCallback as pe, useContext as Mn, useMemo as ge, useState as G, createContext as ri, useLayoutEffect as vl, createElement as Rr, useId as ia } from "react";
import "@tanstack/react-query";
function bl(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var br = { exports: {} }, on = {};
var Mi;
function Sl() {
  if (Mi) return on;
  Mi = 1;
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
var Ai;
function Nl() {
  return Ai || (Ai = 1, br.exports = Sl()), br.exports;
}
var u = Nl();
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
var El = { value: () => {
} };
function Ro() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new go(n);
}
function go(e) {
  this._ = e;
}
function _l(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
go.prototype = Ro.prototype = {
  constructor: go,
  on: function(e, t) {
    var n = this._, o = _l(e + "", n), r, i = -1, s = o.length;
    if (arguments.length < 2) {
      for (; ++i < s; ) if ((r = (e = o[i]).type) && (r = Cl(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++i < s; )
      if (r = (e = o[i]).type) n[r] = ji(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = ji(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new go(e);
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
function Cl(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function ji(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = El, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var Hr = "http://www.w3.org/1999/xhtml";
const Di = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: Hr,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function Ho(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Di.hasOwnProperty(t) ? { space: Di[t], local: e } : e;
}
function kl(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === Hr && t.documentElement.namespaceURI === Hr ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Il(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function sa(e) {
  var t = Ho(e);
  return (t.local ? Il : kl)(t);
}
function Ml() {
}
function ii(e) {
  return e == null ? Ml : function() {
    return this.querySelector(e);
  };
}
function Al(e) {
  typeof e != "function" && (e = ii(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = new Array(s), l, a, d = 0; d < s; ++d)
      (l = i[d]) && (a = e.call(l, l.__data__, d, i)) && ("__data__" in l && (a.__data__ = l.__data__), c[d] = a);
  return new Re(o, this._parents);
}
function jl(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function Dl() {
  return [];
}
function aa(e) {
  return e == null ? Dl : function() {
    return this.querySelectorAll(e);
  };
}
function Pl(e) {
  return function() {
    return jl(e.apply(this, arguments));
  };
}
function $l(e) {
  typeof e == "function" ? e = Pl(e) : e = aa(e);
  for (var t = this._groups, n = t.length, o = [], r = [], i = 0; i < n; ++i)
    for (var s = t[i], c = s.length, l, a = 0; a < c; ++a)
      (l = s[a]) && (o.push(e.call(l, l.__data__, a, s)), r.push(l));
  return new Re(o, r);
}
function ca(e) {
  return function() {
    return this.matches(e);
  };
}
function la(e) {
  return function(t) {
    return t.matches(e);
  };
}
var Tl = Array.prototype.find;
function zl(e) {
  return function() {
    return Tl.call(this.children, e);
  };
}
function Ll() {
  return this.firstElementChild;
}
function Rl(e) {
  return this.select(e == null ? Ll : zl(typeof e == "function" ? e : la(e)));
}
var Hl = Array.prototype.filter;
function Vl() {
  return Array.from(this.children);
}
function Ol(e) {
  return function() {
    return Hl.call(this.children, e);
  };
}
function Bl(e) {
  return this.selectAll(e == null ? Vl : Ol(typeof e == "function" ? e : la(e)));
}
function Fl(e) {
  typeof e != "function" && (e = ca(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = [], l, a = 0; a < s; ++a)
      (l = i[a]) && e.call(l, l.__data__, a, i) && c.push(l);
  return new Re(o, this._parents);
}
function ua(e) {
  return new Array(e.length);
}
function Wl() {
  return new Re(this._enter || this._groups.map(ua), this._parents);
}
function No(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
No.prototype = {
  constructor: No,
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
function Xl(e) {
  return function() {
    return e;
  };
}
function Yl(e, t, n, o, r, i) {
  for (var s = 0, c, l = t.length, a = i.length; s < a; ++s)
    (c = t[s]) ? (c.__data__ = i[s], o[s] = c) : n[s] = new No(e, i[s]);
  for (; s < l; ++s)
    (c = t[s]) && (r[s] = c);
}
function ql(e, t, n, o, r, i, s) {
  var c, l, a = /* @__PURE__ */ new Map(), d = t.length, f = i.length, h = new Array(d), p;
  for (c = 0; c < d; ++c)
    (l = t[c]) && (h[c] = p = s.call(l, l.__data__, c, t) + "", a.has(p) ? r[c] = l : a.set(p, l));
  for (c = 0; c < f; ++c)
    p = s.call(e, i[c], c, i) + "", (l = a.get(p)) ? (o[c] = l, l.__data__ = i[c], a.delete(p)) : n[c] = new No(e, i[c]);
  for (c = 0; c < d; ++c)
    (l = t[c]) && a.get(h[c]) === l && (r[c] = l);
}
function Zl(e) {
  return e.__data__;
}
function Kl(e, t) {
  if (!arguments.length) return Array.from(this, Zl);
  var n = t ? ql : Yl, o = this._parents, r = this._groups;
  typeof e != "function" && (e = Xl(e));
  for (var i = r.length, s = new Array(i), c = new Array(i), l = new Array(i), a = 0; a < i; ++a) {
    var d = o[a], f = r[a], h = f.length, p = Ul(e.call(d, d && d.__data__, a, o)), y = p.length, x = c[a] = new Array(y), v = s[a] = new Array(y), m = l[a] = new Array(h);
    n(d, f, x, v, m, p, t);
    for (var b = 0, g = 0, w, _; b < y; ++b)
      if (w = x[b]) {
        for (b >= g && (g = b + 1); !(_ = v[g]) && ++g < y; ) ;
        w._next = _ || null;
      }
  }
  return s = new Re(s, o), s._enter = c, s._exit = l, s;
}
function Ul(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function Gl() {
  return new Re(this._exit || this._groups.map(ua), this._parents);
}
function Ql(e, t, n) {
  var o = this.enter(), r = this, i = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? i.remove() : n(i), o && r ? o.merge(r).order() : r;
}
function Jl(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, i = o.length, s = Math.min(r, i), c = new Array(r), l = 0; l < s; ++l)
    for (var a = n[l], d = o[l], f = a.length, h = c[l] = new Array(f), p, y = 0; y < f; ++y)
      (p = a[y] || d[y]) && (h[y] = p);
  for (; l < r; ++l)
    c[l] = n[l];
  return new Re(c, this._parents);
}
function eu() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, i = o[r], s; --r >= 0; )
      (s = o[r]) && (i && s.compareDocumentPosition(i) ^ 4 && i.parentNode.insertBefore(s, i), i = s);
  return this;
}
function tu(e) {
  e || (e = nu);
  function t(f, h) {
    return f && h ? e(f.__data__, h.__data__) : !f - !h;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), i = 0; i < o; ++i) {
    for (var s = n[i], c = s.length, l = r[i] = new Array(c), a, d = 0; d < c; ++d)
      (a = s[d]) && (l[d] = a);
    l.sort(t);
  }
  return new Re(r, this._parents).order();
}
function nu(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function ou() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function ru() {
  return Array.from(this);
}
function iu() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length; r < i; ++r) {
      var s = o[r];
      if (s) return s;
    }
  return null;
}
function su() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function au() {
  return !this.node();
}
function cu(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], i = 0, s = r.length, c; i < s; ++i)
      (c = r[i]) && e.call(c, c.__data__, i, r);
  return this;
}
function lu(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function uu(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function du(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function fu(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function hu(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function pu(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function gu(e, t) {
  var n = Ho(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? uu : lu : typeof t == "function" ? n.local ? pu : hu : n.local ? fu : du)(n, t));
}
function da(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function yu(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function mu(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function xu(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function wu(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? yu : typeof t == "function" ? xu : mu)(e, t, n ?? "")) : Pt(this.node(), e);
}
function Pt(e, t) {
  return e.style.getPropertyValue(t) || da(e).getComputedStyle(e, null).getPropertyValue(t);
}
function vu(e) {
  return function() {
    delete this[e];
  };
}
function bu(e, t) {
  return function() {
    this[e] = t;
  };
}
function Su(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function Nu(e, t) {
  return arguments.length > 1 ? this.each((t == null ? vu : typeof t == "function" ? Su : bu)(e, t)) : this.node()[e];
}
function fa(e) {
  return e.trim().split(/^|\s+/);
}
function si(e) {
  return e.classList || new ha(e);
}
function ha(e) {
  this._node = e, this._names = fa(e.getAttribute("class") || "");
}
ha.prototype = {
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
function pa(e, t) {
  for (var n = si(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function ga(e, t) {
  for (var n = si(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function Eu(e) {
  return function() {
    pa(this, e);
  };
}
function _u(e) {
  return function() {
    ga(this, e);
  };
}
function Cu(e, t) {
  return function() {
    (t.apply(this, arguments) ? pa : ga)(this, e);
  };
}
function ku(e, t) {
  var n = fa(e + "");
  if (arguments.length < 2) {
    for (var o = si(this.node()), r = -1, i = n.length; ++r < i; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? Cu : t ? Eu : _u)(n, t));
}
function Iu() {
  this.textContent = "";
}
function Mu(e) {
  return function() {
    this.textContent = e;
  };
}
function Au(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function ju(e) {
  return arguments.length ? this.each(e == null ? Iu : (typeof e == "function" ? Au : Mu)(e)) : this.node().textContent;
}
function Du() {
  this.innerHTML = "";
}
function Pu(e) {
  return function() {
    this.innerHTML = e;
  };
}
function $u(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function Tu(e) {
  return arguments.length ? this.each(e == null ? Du : (typeof e == "function" ? $u : Pu)(e)) : this.node().innerHTML;
}
function zu() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function Lu() {
  return this.each(zu);
}
function Ru() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Hu() {
  return this.each(Ru);
}
function Vu(e) {
  var t = typeof e == "function" ? e : sa(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Ou() {
  return null;
}
function Bu(e, t) {
  var n = typeof e == "function" ? e : sa(e), o = t == null ? Ou : typeof t == "function" ? t : ii(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function Fu() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Wu() {
  return this.each(Fu);
}
function Xu() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Yu() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function qu(e) {
  return this.select(e ? Yu : Xu);
}
function Zu(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function Ku(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function Uu(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function Gu(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, i; n < r; ++n)
        i = t[n], (!e.type || i.type === e.type) && i.name === e.name ? this.removeEventListener(i.type, i.listener, i.options) : t[++o] = i;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function Qu(e, t, n) {
  return function() {
    var o = this.__on, r, i = Ku(t);
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
function Ju(e, t, n) {
  var o = Uu(e + ""), r, i = o.length, s;
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
  for (c = t ? Qu : Gu, r = 0; r < i; ++r) this.each(c(o[r], t, n));
  return this;
}
function ya(e, t, n) {
  var o = da(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function ed(e, t) {
  return function() {
    return ya(this, e, t);
  };
}
function td(e, t) {
  return function() {
    return ya(this, e, t.apply(this, arguments));
  };
}
function nd(e, t) {
  return this.each((typeof t == "function" ? td : ed)(e, t));
}
function* od() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length, s; r < i; ++r)
      (s = o[r]) && (yield s);
}
var ma = [null];
function Re(e, t) {
  this._groups = e, this._parents = t;
}
function An() {
  return new Re([[document.documentElement]], ma);
}
function rd() {
  return this;
}
Re.prototype = An.prototype = {
  constructor: Re,
  select: Al,
  selectAll: $l,
  selectChild: Rl,
  selectChildren: Bl,
  filter: Fl,
  data: Kl,
  enter: Wl,
  exit: Gl,
  join: Ql,
  merge: Jl,
  selection: rd,
  order: eu,
  sort: tu,
  call: ou,
  nodes: ru,
  node: iu,
  size: su,
  empty: au,
  each: cu,
  attr: gu,
  style: wu,
  property: Nu,
  classed: ku,
  text: ju,
  html: Tu,
  raise: Lu,
  lower: Hu,
  append: Vu,
  insert: Bu,
  remove: Wu,
  clone: qu,
  datum: Zu,
  on: Ju,
  dispatch: nd,
  [Symbol.iterator]: od
};
function Le(e) {
  return typeof e == "string" ? new Re([[document.querySelector(e)]], [document.documentElement]) : new Re([[e]], ma);
}
function id(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Xe(e, t) {
  if (e = id(e), t === void 0 && (t = e.currentTarget), t) {
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
const sd = { passive: !1 }, xn = { capture: !0, passive: !1 };
function Sr(e) {
  e.stopImmediatePropagation();
}
function jt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function xa(e) {
  var t = e.document.documentElement, n = Le(e).on("dragstart.drag", jt, xn);
  "onselectstart" in t ? n.on("selectstart.drag", jt, xn) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function wa(e, t) {
  var n = e.document.documentElement, o = Le(e).on("dragstart.drag", null);
  t && (o.on("click.drag", jt, xn), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const oo = (e) => () => e;
function Vr(e, {
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
Vr.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function ad(e) {
  return !e.ctrlKey && !e.button;
}
function cd() {
  return this.parentNode;
}
function ld(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function ud() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function va() {
  var e = ad, t = cd, n = ld, o = ud, r = {}, i = Ro("start", "drag", "end"), s = 0, c, l, a, d, f = 0;
  function h(w) {
    w.on("mousedown.drag", p).filter(o).on("touchstart.drag", v).on("touchmove.drag", m, sd).on("touchend.drag touchcancel.drag", b).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function p(w, _) {
    if (!(d || !e.call(this, w, _))) {
      var E = g(this, t.call(this, w, _), w, _, "mouse");
      E && (Le(w.view).on("mousemove.drag", y, xn).on("mouseup.drag", x, xn), xa(w.view), Sr(w), a = !1, c = w.clientX, l = w.clientY, E("start", w));
    }
  }
  function y(w) {
    if (jt(w), !a) {
      var _ = w.clientX - c, E = w.clientY - l;
      a = _ * _ + E * E > f;
    }
    r.mouse("drag", w);
  }
  function x(w) {
    Le(w.view).on("mousemove.drag mouseup.drag", null), wa(w.view, a), jt(w), r.mouse("end", w);
  }
  function v(w, _) {
    if (e.call(this, w, _)) {
      var E = w.changedTouches, k = t.call(this, w, _), D = E.length, $, W;
      for ($ = 0; $ < D; ++$)
        (W = g(this, k, w, _, E[$].identifier, E[$])) && (Sr(w), W("start", w, E[$]));
    }
  }
  function m(w) {
    var _ = w.changedTouches, E = _.length, k, D;
    for (k = 0; k < E; ++k)
      (D = r[_[k].identifier]) && (jt(w), D("drag", w, _[k]));
  }
  function b(w) {
    var _ = w.changedTouches, E = _.length, k, D;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), k = 0; k < E; ++k)
      (D = r[_[k].identifier]) && (Sr(w), D("end", w, _[k]));
  }
  function g(w, _, E, k, D, $) {
    var W = i.copy(), M = Xe($ || E, _), z, H, S;
    if ((S = n.call(w, new Vr("beforestart", {
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
      return z = S.x - M[0] || 0, H = S.y - M[1] || 0, function I(C, A, T) {
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
          new Vr(C, {
            sourceEvent: A,
            subject: S,
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
    return arguments.length ? (e = typeof w == "function" ? w : oo(!!w), h) : e;
  }, h.container = function(w) {
    return arguments.length ? (t = typeof w == "function" ? w : oo(w), h) : t;
  }, h.subject = function(w) {
    return arguments.length ? (n = typeof w == "function" ? w : oo(w), h) : n;
  }, h.touchable = function(w) {
    return arguments.length ? (o = typeof w == "function" ? w : oo(!!w), h) : o;
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
function ba(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function jn() {
}
var wn = 0.7, Eo = 1 / wn, Dt = "\\s*([+-]?\\d+)\\s*", vn = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", Qe = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", dd = /^#([0-9a-f]{3,8})$/, fd = new RegExp(`^rgb\\(${Dt},${Dt},${Dt}\\)$`), hd = new RegExp(`^rgb\\(${Qe},${Qe},${Qe}\\)$`), pd = new RegExp(`^rgba\\(${Dt},${Dt},${Dt},${vn}\\)$`), gd = new RegExp(`^rgba\\(${Qe},${Qe},${Qe},${vn}\\)$`), yd = new RegExp(`^hsl\\(${vn},${Qe},${Qe}\\)$`), md = new RegExp(`^hsla\\(${vn},${Qe},${Qe},${vn}\\)$`), Pi = {
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
ai(jn, mt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: $i,
  // Deprecated! Use color.formatHex.
  formatHex: $i,
  formatHex8: xd,
  formatHsl: wd,
  formatRgb: Ti,
  toString: Ti
});
function $i() {
  return this.rgb().formatHex();
}
function xd() {
  return this.rgb().formatHex8();
}
function wd() {
  return Sa(this).formatHsl();
}
function Ti() {
  return this.rgb().formatRgb();
}
function mt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = dd.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? zi(t) : n === 3 ? new $e(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? ro(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? ro(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = fd.exec(e)) ? new $e(t[1], t[2], t[3], 1) : (t = hd.exec(e)) ? new $e(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = pd.exec(e)) ? ro(t[1], t[2], t[3], t[4]) : (t = gd.exec(e)) ? ro(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = yd.exec(e)) ? Hi(t[1], t[2] / 100, t[3] / 100, 1) : (t = md.exec(e)) ? Hi(t[1], t[2] / 100, t[3] / 100, t[4]) : Pi.hasOwnProperty(e) ? zi(Pi[e]) : e === "transparent" ? new $e(NaN, NaN, NaN, 0) : null;
}
function zi(e) {
  return new $e(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function ro(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new $e(e, t, n, o);
}
function vd(e) {
  return e instanceof jn || (e = mt(e)), e ? (e = e.rgb(), new $e(e.r, e.g, e.b, e.opacity)) : new $e();
}
function Or(e, t, n, o) {
  return arguments.length === 1 ? vd(e) : new $e(e, t, n, o ?? 1);
}
function $e(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
ai($e, Or, ba(jn, {
  brighter(e) {
    return e = e == null ? Eo : Math.pow(Eo, e), new $e(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? wn : Math.pow(wn, e), new $e(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new $e(gt(this.r), gt(this.g), gt(this.b), _o(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: Li,
  // Deprecated! Use color.formatHex.
  formatHex: Li,
  formatHex8: bd,
  formatRgb: Ri,
  toString: Ri
}));
function Li() {
  return `#${pt(this.r)}${pt(this.g)}${pt(this.b)}`;
}
function bd() {
  return `#${pt(this.r)}${pt(this.g)}${pt(this.b)}${pt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function Ri() {
  const e = _o(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${gt(this.r)}, ${gt(this.g)}, ${gt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function _o(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function gt(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function pt(e) {
  return e = gt(e), (e < 16 ? "0" : "") + e.toString(16);
}
function Hi(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Ye(e, t, n, o);
}
function Sa(e) {
  if (e instanceof Ye) return new Ye(e.h, e.s, e.l, e.opacity);
  if (e instanceof jn || (e = mt(e)), !e) return new Ye();
  if (e instanceof Ye) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), i = Math.max(t, n, o), s = NaN, c = i - r, l = (i + r) / 2;
  return c ? (t === i ? s = (n - o) / c + (n < o) * 6 : n === i ? s = (o - t) / c + 2 : s = (t - n) / c + 4, c /= l < 0.5 ? i + r : 2 - i - r, s *= 60) : c = l > 0 && l < 1 ? 0 : s, new Ye(s, c, l, e.opacity);
}
function Sd(e, t, n, o) {
  return arguments.length === 1 ? Sa(e) : new Ye(e, t, n, o ?? 1);
}
function Ye(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
ai(Ye, Sd, ba(jn, {
  brighter(e) {
    return e = e == null ? Eo : Math.pow(Eo, e), new Ye(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? wn : Math.pow(wn, e), new Ye(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new $e(
      Nr(e >= 240 ? e - 240 : e + 120, r, o),
      Nr(e, r, o),
      Nr(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new Ye(Vi(this.h), io(this.s), io(this.l), _o(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = _o(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${Vi(this.h)}, ${io(this.s) * 100}%, ${io(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function Vi(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function io(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function Nr(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const ci = (e) => () => e;
function Nd(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function Ed(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function _d(e) {
  return (e = +e) == 1 ? Na : function(t, n) {
    return n - t ? Ed(t, n, e) : ci(isNaN(t) ? n : t);
  };
}
function Na(e, t) {
  var n = t - e;
  return n ? Nd(e, n) : ci(isNaN(e) ? t : e);
}
const Co = (function e(t) {
  var n = _d(t);
  function o(r, i) {
    var s = n((r = Or(r)).r, (i = Or(i)).r), c = n(r.g, i.g), l = n(r.b, i.b), a = Na(r.opacity, i.opacity);
    return function(d) {
      return r.r = s(d), r.g = c(d), r.b = l(d), r.opacity = a(d), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function Cd(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(i) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - i) + t[r] * i;
    return o;
  };
}
function kd(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Id(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), i = new Array(n), s;
  for (s = 0; s < o; ++s) r[s] = yn(e[s], t[s]);
  for (; s < n; ++s) i[s] = t[s];
  return function(c) {
    for (s = 0; s < o; ++s) i[s] = r[s](c);
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
function Ad(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = yn(e[r], t[r]) : o[r] = t[r];
  return function(i) {
    for (r in n) o[r] = n[r](i);
    return o;
  };
}
var Br = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, Er = new RegExp(Br.source, "g");
function jd(e) {
  return function() {
    return e;
  };
}
function Dd(e) {
  return function(t) {
    return e(t) + "";
  };
}
function Ea(e, t) {
  var n = Br.lastIndex = Er.lastIndex = 0, o, r, i, s = -1, c = [], l = [];
  for (e = e + "", t = t + ""; (o = Br.exec(e)) && (r = Er.exec(t)); )
    (i = r.index) > n && (i = t.slice(n, i), c[s] ? c[s] += i : c[++s] = i), (o = o[0]) === (r = r[0]) ? c[s] ? c[s] += r : c[++s] = r : (c[++s] = null, l.push({ i: s, x: Ge(o, r) })), n = Er.lastIndex;
  return n < t.length && (i = t.slice(n), c[s] ? c[s] += i : c[++s] = i), c.length < 2 ? l[0] ? Dd(l[0].x) : jd(t) : (t = l.length, function(a) {
    for (var d = 0, f; d < t; ++d) c[(f = l[d]).i] = f.x(a);
    return c.join("");
  });
}
function yn(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? ci(t) : (n === "number" ? Ge : n === "string" ? (o = mt(t)) ? (t = o, Co) : Ea : t instanceof mt ? Co : t instanceof Date ? Md : kd(t) ? Cd : Array.isArray(t) ? Id : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Ad : Ge)(e, t);
}
var Oi = 180 / Math.PI, Fr = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function _a(e, t, n, o, r, i) {
  var s, c, l;
  return (s = Math.sqrt(e * e + t * t)) && (e /= s, t /= s), (l = e * n + t * o) && (n -= e * l, o -= t * l), (c = Math.sqrt(n * n + o * o)) && (n /= c, o /= c, l /= c), e * o < t * n && (e = -e, t = -t, l = -l, s = -s), {
    translateX: r,
    translateY: i,
    rotate: Math.atan2(t, e) * Oi,
    skewX: Math.atan(l) * Oi,
    scaleX: s,
    scaleY: c
  };
}
var so;
function Pd(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Fr : _a(t.a, t.b, t.c, t.d, t.e, t.f);
}
function $d(e) {
  return e == null || (so || (so = document.createElementNS("http://www.w3.org/2000/svg", "g")), so.setAttribute("transform", e), !(e = so.transform.baseVal.consolidate())) ? Fr : (e = e.matrix, _a(e.a, e.b, e.c, e.d, e.e, e.f));
}
function Ca(e, t, n, o) {
  function r(a) {
    return a.length ? a.pop() + " " : "";
  }
  function i(a, d, f, h, p, y) {
    if (a !== f || d !== h) {
      var x = p.push("translate(", null, t, null, n);
      y.push({ i: x - 4, x: Ge(a, f) }, { i: x - 2, x: Ge(d, h) });
    } else (f || h) && p.push("translate(" + f + t + h + n);
  }
  function s(a, d, f, h) {
    a !== d ? (a - d > 180 ? d += 360 : d - a > 180 && (a += 360), h.push({ i: f.push(r(f) + "rotate(", null, o) - 2, x: Ge(a, d) })) : d && f.push(r(f) + "rotate(" + d + o);
  }
  function c(a, d, f, h) {
    a !== d ? h.push({ i: f.push(r(f) + "skewX(", null, o) - 2, x: Ge(a, d) }) : d && f.push(r(f) + "skewX(" + d + o);
  }
  function l(a, d, f, h, p, y) {
    if (a !== f || d !== h) {
      var x = p.push(r(p) + "scale(", null, ",", null, ")");
      y.push({ i: x - 4, x: Ge(a, f) }, { i: x - 2, x: Ge(d, h) });
    } else (f !== 1 || h !== 1) && p.push(r(p) + "scale(" + f + "," + h + ")");
  }
  return function(a, d) {
    var f = [], h = [];
    return a = e(a), d = e(d), i(a.translateX, a.translateY, d.translateX, d.translateY, f, h), s(a.rotate, d.rotate, f, h), c(a.skewX, d.skewX, f, h), l(a.scaleX, a.scaleY, d.scaleX, d.scaleY, f, h), a = d = null, function(p) {
      for (var y = -1, x = h.length, v; ++y < x; ) f[(v = h[y]).i] = v.x(p);
      return f.join("");
    };
  };
}
var Td = Ca(Pd, "px, ", "px)", "deg)"), zd = Ca($d, ", ", ")", ")"), Ld = 1e-12;
function Bi(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Rd(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Hd(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const yo = (function e(t, n, o) {
  function r(i, s) {
    var c = i[0], l = i[1], a = i[2], d = s[0], f = s[1], h = s[2], p = d - c, y = f - l, x = p * p + y * y, v, m;
    if (x < Ld)
      m = Math.log(h / a) / t, v = function(k) {
        return [
          c + k * p,
          l + k * y,
          a * Math.exp(t * k * m)
        ];
      };
    else {
      var b = Math.sqrt(x), g = (h * h - a * a + o * x) / (2 * a * n * b), w = (h * h - a * a - o * x) / (2 * h * n * b), _ = Math.log(Math.sqrt(g * g + 1) - g), E = Math.log(Math.sqrt(w * w + 1) - w);
      m = (E - _) / t, v = function(k) {
        var D = k * m, $ = Bi(_), W = a / (n * b) * ($ * Hd(t * D + _) - Rd(_));
        return [
          c + W * p,
          l + W * y,
          a * $ / Bi(t * D + _)
        ];
      };
    }
    return v.duration = m * 1e3 * t / Math.SQRT2, v;
  }
  return r.rho = function(i) {
    var s = Math.max(1e-3, +i), c = s * s, l = c * c;
    return e(s, c, l);
  }, r;
})(Math.SQRT2, 2, 4);
var $t = 0, hn = 0, rn = 0, ka = 1e3, ko, pn, Io = 0, xt = 0, Vo = 0, bn = typeof performance == "object" && performance.now ? performance : Date, Ia = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function li() {
  return xt || (Ia(Vd), xt = bn.now() + Vo);
}
function Vd() {
  xt = 0;
}
function Mo() {
  this._call = this._time = this._next = null;
}
Mo.prototype = Ma.prototype = {
  constructor: Mo,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? li() : +n) + (t == null ? 0 : +t), !this._next && pn !== this && (pn ? pn._next = this : ko = this, pn = this), this._call = e, this._time = n, Wr();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Wr());
  }
};
function Ma(e, t, n) {
  var o = new Mo();
  return o.restart(e, t, n), o;
}
function Od() {
  li(), ++$t;
  for (var e = ko, t; e; )
    (t = xt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --$t;
}
function Fi() {
  xt = (Io = bn.now()) + Vo, $t = hn = 0;
  try {
    Od();
  } finally {
    $t = 0, Fd(), xt = 0;
  }
}
function Bd() {
  var e = bn.now(), t = e - Io;
  t > ka && (Vo -= t, Io = e);
}
function Fd() {
  for (var e, t = ko, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : ko = n);
  pn = e, Wr(o);
}
function Wr(e) {
  if (!$t) {
    hn && (hn = clearTimeout(hn));
    var t = e - xt;
    t > 24 ? (e < 1 / 0 && (hn = setTimeout(Fi, e - bn.now() - Vo)), rn && (rn = clearInterval(rn))) : (rn || (Io = bn.now(), rn = setInterval(Bd, ka)), $t = 1, Ia(Fi));
  }
}
function Wi(e, t, n) {
  var o = new Mo();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var Wd = Ro("start", "end", "cancel", "interrupt"), Xd = [], Aa = 0, Xi = 1, Xr = 2, mo = 3, Yi = 4, Yr = 5, xo = 6;
function Oo(e, t, n, o, r, i) {
  var s = e.__transition;
  if (!s) e.__transition = {};
  else if (n in s) return;
  Yd(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: Wd,
    tween: Xd,
    time: i.time,
    delay: i.delay,
    duration: i.duration,
    ease: i.ease,
    timer: null,
    state: Aa
  });
}
function ui(e, t) {
  var n = Ke(e, t);
  if (n.state > Aa) throw new Error("too late; already scheduled");
  return n;
}
function et(e, t) {
  var n = Ke(e, t);
  if (n.state > mo) throw new Error("too late; already running");
  return n;
}
function Ke(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function Yd(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = Ma(i, 0, n.time);
  function i(a) {
    n.state = Xi, n.timer.restart(s, n.delay, n.time), n.delay <= a && s(a - n.delay);
  }
  function s(a) {
    var d, f, h, p;
    if (n.state !== Xi) return l();
    for (d in o)
      if (p = o[d], p.name === n.name) {
        if (p.state === mo) return Wi(s);
        p.state === Yi ? (p.state = xo, p.timer.stop(), p.on.call("interrupt", e, e.__data__, p.index, p.group), delete o[d]) : +d < t && (p.state = xo, p.timer.stop(), p.on.call("cancel", e, e.__data__, p.index, p.group), delete o[d]);
      }
    if (Wi(function() {
      n.state === mo && (n.state = Yi, n.timer.restart(c, n.delay, n.time), c(a));
    }), n.state = Xr, n.on.call("start", e, e.__data__, n.index, n.group), n.state === Xr) {
      for (n.state = mo, r = new Array(h = n.tween.length), d = 0, f = -1; d < h; ++d)
        (p = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (r[++f] = p);
      r.length = f + 1;
    }
  }
  function c(a) {
    for (var d = a < n.duration ? n.ease.call(null, a / n.duration) : (n.timer.restart(l), n.state = Yr, 1), f = -1, h = r.length; ++f < h; )
      r[f].call(e, d);
    n.state === Yr && (n.on.call("end", e, e.__data__, n.index, n.group), l());
  }
  function l() {
    n.state = xo, n.timer.stop(), delete o[t];
    for (var a in o) return;
    delete e.__transition;
  }
}
function wo(e, t) {
  var n = e.__transition, o, r, i = !0, s;
  if (n) {
    t = t == null ? null : t + "";
    for (s in n) {
      if ((o = n[s]).name !== t) {
        i = !1;
        continue;
      }
      r = o.state > Xr && o.state < Yr, o.state = xo, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[s];
    }
    i && delete e.__transition;
  }
}
function qd(e) {
  return this.each(function() {
    wo(this, e);
  });
}
function Zd(e, t) {
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
function Kd(e, t, n) {
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
function Ud(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = Ke(this.node(), n).tween, r = 0, i = o.length, s; r < i; ++r)
      if ((s = o[r]).name === e)
        return s.value;
    return null;
  }
  return this.each((t == null ? Zd : Kd)(n, e, t));
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
function ja(e, t) {
  var n;
  return (typeof t == "number" ? Ge : t instanceof mt ? Co : (n = mt(t)) ? (t = n, Co) : Ea)(e, t);
}
function Gd(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Qd(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Jd(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttribute(e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function ef(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttributeNS(e.space, e.local);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function tf(e, t, n) {
  var o, r, i;
  return function() {
    var s, c = n(this), l;
    return c == null ? void this.removeAttribute(e) : (s = this.getAttribute(e), l = c + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, c)));
  };
}
function nf(e, t, n) {
  var o, r, i;
  return function() {
    var s, c = n(this), l;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (s = this.getAttributeNS(e.space, e.local), l = c + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, c)));
  };
}
function of(e, t) {
  var n = Ho(e), o = n === "transform" ? zd : ja;
  return this.attrTween(e, typeof t == "function" ? (n.local ? nf : tf)(n, o, di(this, "attr." + e, t)) : t == null ? (n.local ? Qd : Gd)(n) : (n.local ? ef : Jd)(n, o, t));
}
function rf(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function sf(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function af(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && sf(e, i)), n;
  }
  return r._value = t, r;
}
function cf(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && rf(e, i)), n;
  }
  return r._value = t, r;
}
function lf(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = Ho(e);
  return this.tween(n, (o.local ? af : cf)(o, t));
}
function uf(e, t) {
  return function() {
    ui(this, e).delay = +t.apply(this, arguments);
  };
}
function df(e, t) {
  return t = +t, function() {
    ui(this, e).delay = t;
  };
}
function ff(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? uf : df)(t, e)) : Ke(this.node(), t).delay;
}
function hf(e, t) {
  return function() {
    et(this, e).duration = +t.apply(this, arguments);
  };
}
function pf(e, t) {
  return t = +t, function() {
    et(this, e).duration = t;
  };
}
function gf(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? hf : pf)(t, e)) : Ke(this.node(), t).duration;
}
function yf(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    et(this, e).ease = t;
  };
}
function mf(e) {
  var t = this._id;
  return arguments.length ? this.each(yf(t, e)) : Ke(this.node(), t).ease;
}
function xf(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    et(this, e).ease = n;
  };
}
function wf(e) {
  if (typeof e != "function") throw new Error();
  return this.each(xf(this._id, e));
}
function vf(e) {
  typeof e != "function" && (e = ca(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = [], l, a = 0; a < s; ++a)
      (l = i[a]) && e.call(l, l.__data__, a, i) && c.push(l);
  return new rt(o, this._parents, this._name, this._id);
}
function bf(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, i = Math.min(o, r), s = new Array(o), c = 0; c < i; ++c)
    for (var l = t[c], a = n[c], d = l.length, f = s[c] = new Array(d), h, p = 0; p < d; ++p)
      (h = l[p] || a[p]) && (f[p] = h);
  for (; c < o; ++c)
    s[c] = t[c];
  return new rt(s, this._parents, this._name, this._id);
}
function Sf(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function Nf(e, t, n) {
  var o, r, i = Sf(t) ? ui : et;
  return function() {
    var s = i(this, e), c = s.on;
    c !== o && (r = (o = c).copy()).on(t, n), s.on = r;
  };
}
function Ef(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Ke(this.node(), n).on.on(e) : this.each(Nf(n, e, t));
}
function _f(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function Cf() {
  return this.on("end.remove", _f(this._id));
}
function kf(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = ii(e));
  for (var o = this._groups, r = o.length, i = new Array(r), s = 0; s < r; ++s)
    for (var c = o[s], l = c.length, a = i[s] = new Array(l), d, f, h = 0; h < l; ++h)
      (d = c[h]) && (f = e.call(d, d.__data__, h, c)) && ("__data__" in d && (f.__data__ = d.__data__), a[h] = f, Oo(a[h], t, n, h, a, Ke(d, n)));
  return new rt(i, this._parents, t, n);
}
function If(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = aa(e));
  for (var o = this._groups, r = o.length, i = [], s = [], c = 0; c < r; ++c)
    for (var l = o[c], a = l.length, d, f = 0; f < a; ++f)
      if (d = l[f]) {
        for (var h = e.call(d, d.__data__, f, l), p, y = Ke(d, n), x = 0, v = h.length; x < v; ++x)
          (p = h[x]) && Oo(p, t, n, x, h, y);
        i.push(h), s.push(d);
      }
  return new rt(i, s, t, n);
}
var Mf = An.prototype.constructor;
function Af() {
  return new Mf(this._groups, this._parents);
}
function jf(e, t) {
  var n, o, r;
  return function() {
    var i = Pt(this, e), s = (this.style.removeProperty(e), Pt(this, e));
    return i === s ? null : i === n && s === o ? r : r = t(n = i, o = s);
  };
}
function Da(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Df(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = Pt(this, e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Pf(e, t, n) {
  var o, r, i;
  return function() {
    var s = Pt(this, e), c = n(this), l = c + "";
    return c == null && (l = c = (this.style.removeProperty(e), Pt(this, e))), s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, c));
  };
}
function $f(e, t) {
  var n, o, r, i = "style." + t, s = "end." + i, c;
  return function() {
    var l = et(this, e), a = l.on, d = l.value[i] == null ? c || (c = Da(t)) : void 0;
    (a !== n || r !== d) && (o = (n = a).copy()).on(s, r = d), l.on = o;
  };
}
function Tf(e, t, n) {
  var o = (e += "") == "transform" ? Td : ja;
  return t == null ? this.styleTween(e, jf(e, o)).on("end.style." + e, Da(e)) : typeof t == "function" ? this.styleTween(e, Pf(e, o, di(this, "style." + e, t))).each($f(this._id, e)) : this.styleTween(e, Df(e, o, t), n).on("end.style." + e, null);
}
function zf(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function Lf(e, t, n) {
  var o, r;
  function i() {
    var s = t.apply(this, arguments);
    return s !== r && (o = (r = s) && zf(e, s, n)), o;
  }
  return i._value = t, i;
}
function Rf(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, Lf(e, t, n ?? ""));
}
function Hf(e) {
  return function() {
    this.textContent = e;
  };
}
function Vf(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function Of(e) {
  return this.tween("text", typeof e == "function" ? Vf(di(this, "text", e)) : Hf(e == null ? "" : e + ""));
}
function Bf(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Ff(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Bf(r)), t;
  }
  return o._value = e, o;
}
function Wf(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, Ff(e));
}
function Xf() {
  for (var e = this._name, t = this._id, n = Pa(), o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], c = s.length, l, a = 0; a < c; ++a)
      if (l = s[a]) {
        var d = Ke(l, t);
        Oo(l, e, n, a, s, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new rt(o, this._parents, e, n);
}
function Yf() {
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
var qf = 0;
function rt(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function Pa() {
  return ++qf;
}
var nt = An.prototype;
rt.prototype = {
  constructor: rt,
  select: kf,
  selectAll: If,
  selectChild: nt.selectChild,
  selectChildren: nt.selectChildren,
  filter: vf,
  merge: bf,
  selection: Af,
  transition: Xf,
  call: nt.call,
  nodes: nt.nodes,
  node: nt.node,
  size: nt.size,
  empty: nt.empty,
  each: nt.each,
  on: Ef,
  attr: of,
  attrTween: lf,
  style: Tf,
  styleTween: Rf,
  text: Of,
  textTween: Wf,
  remove: Cf,
  tween: Ud,
  delay: ff,
  duration: gf,
  ease: mf,
  easeVarying: wf,
  end: Yf,
  [Symbol.iterator]: nt[Symbol.iterator]
};
function Zf(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var Kf = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: Zf
};
function Uf(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function Gf(e) {
  var t, n;
  e instanceof rt ? (t = e._id, e = e._name) : (t = Pa(), (n = Kf).time = li(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], c = s.length, l, a = 0; a < c; ++a)
      (l = s[a]) && Oo(l, e, t, a, s, n || Uf(l, t));
  return new rt(o, this._parents, e, t);
}
An.prototype.interrupt = qd;
An.prototype.transition = Gf;
const ao = (e) => () => e;
function Qf(e, {
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
var Bo = new ot(1, 0, 0);
$a.prototype = ot.prototype;
function $a(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return Bo;
  return e.__zoom;
}
function _r(e) {
  e.stopImmediatePropagation();
}
function sn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Jf(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function eh() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function qi() {
  return this.__zoom || Bo;
}
function th(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function nh() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function oh(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], i = e.invertY(t[0][1]) - n[0][1], s = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    s > i ? (i + s) / 2 : Math.min(0, i) || Math.max(0, s)
  );
}
function Ta() {
  var e = Jf, t = eh, n = oh, o = th, r = nh, i = [0, 1 / 0], s = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, l = yo, a = Ro("start", "zoom", "end"), d, f, h, p = 500, y = 150, x = 0, v = 10;
  function m(S) {
    S.property("__zoom", qi).on("wheel.zoom", D, { passive: !1 }).on("mousedown.zoom", $).on("dblclick.zoom", W).filter(r).on("touchstart.zoom", M).on("touchmove.zoom", z).on("touchend.zoom touchcancel.zoom", H).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  m.transform = function(S, I, C, A) {
    var T = S.selection ? S.selection() : S;
    T.property("__zoom", qi), S !== T ? _(S, I, C, A) : T.interrupt().each(function() {
      E(this, arguments).event(A).start().zoom(null, typeof I == "function" ? I.apply(this, arguments) : I).end();
    });
  }, m.scaleBy = function(S, I, C, A) {
    m.scaleTo(S, function() {
      var T = this.__zoom.k, P = typeof I == "function" ? I.apply(this, arguments) : I;
      return T * P;
    }, C, A);
  }, m.scaleTo = function(S, I, C, A) {
    m.transform(S, function() {
      var T = t.apply(this, arguments), P = this.__zoom, V = C == null ? w(T) : typeof C == "function" ? C.apply(this, arguments) : C, F = P.invert(V), O = typeof I == "function" ? I.apply(this, arguments) : I;
      return n(g(b(P, O), V, F), T, s);
    }, C, A);
  }, m.translateBy = function(S, I, C, A) {
    m.transform(S, function() {
      return n(this.__zoom.translate(
        typeof I == "function" ? I.apply(this, arguments) : I,
        typeof C == "function" ? C.apply(this, arguments) : C
      ), t.apply(this, arguments), s);
    }, null, A);
  }, m.translateTo = function(S, I, C, A, T) {
    m.transform(S, function() {
      var P = t.apply(this, arguments), V = this.__zoom, F = A == null ? w(P) : typeof A == "function" ? A.apply(this, arguments) : A;
      return n(Bo.translate(F[0], F[1]).scale(V.k).translate(
        typeof I == "function" ? -I.apply(this, arguments) : -I,
        typeof C == "function" ? -C.apply(this, arguments) : -C
      ), P, s);
    }, A, T);
  };
  function b(S, I) {
    return I = Math.max(i[0], Math.min(i[1], I)), I === S.k ? S : new ot(I, S.x, S.y);
  }
  function g(S, I, C) {
    var A = I[0] - C[0] * S.k, T = I[1] - C[1] * S.k;
    return A === S.x && T === S.y ? S : new ot(S.k, A, T);
  }
  function w(S) {
    return [(+S[0][0] + +S[1][0]) / 2, (+S[0][1] + +S[1][1]) / 2];
  }
  function _(S, I, C, A) {
    S.on("start.zoom", function() {
      E(this, arguments).event(A).start();
    }).on("interrupt.zoom end.zoom", function() {
      E(this, arguments).event(A).end();
    }).tween("zoom", function() {
      var T = this, P = arguments, V = E(T, P).event(A), F = t.apply(T, P), O = C == null ? w(F) : typeof C == "function" ? C.apply(T, P) : C, U = Math.max(F[1][0] - F[0][0], F[1][1] - F[0][1]), Z = T.__zoom, ne = typeof I == "function" ? I.apply(T, P) : I, se = l(Z.invert(O).concat(U / Z.k), ne.invert(O).concat(U / ne.k));
      return function(K) {
        if (K === 1) K = ne;
        else {
          var L = se(K), Y = U / L[2];
          K = new ot(Y, O[0] - L[0] * Y, O[1] - L[1] * Y);
        }
        V.zoom(null, K);
      };
    });
  }
  function E(S, I, C) {
    return !C && S.__zooming || new k(S, I);
  }
  function k(S, I) {
    this.that = S, this.args = I, this.active = 0, this.sourceEvent = null, this.extent = t.apply(S, I), this.taps = 0;
  }
  k.prototype = {
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
        new Qf(S, {
          sourceEvent: this.sourceEvent,
          target: m,
          transform: this.that.__zoom,
          dispatch: a
        }),
        I
      );
    }
  };
  function D(S, ...I) {
    if (!e.apply(this, arguments)) return;
    var C = E(this, I).event(S), A = this.__zoom, T = Math.max(i[0], Math.min(i[1], A.k * Math.pow(2, o.apply(this, arguments)))), P = Xe(S);
    if (C.wheel)
      (C.mouse[0][0] !== P[0] || C.mouse[0][1] !== P[1]) && (C.mouse[1] = A.invert(C.mouse[0] = P)), clearTimeout(C.wheel);
    else {
      if (A.k === T) return;
      C.mouse = [P, A.invert(P)], wo(this), C.start();
    }
    sn(S), C.wheel = setTimeout(V, y), C.zoom("mouse", n(g(b(A, T), C.mouse[0], C.mouse[1]), C.extent, s));
    function V() {
      C.wheel = null, C.end();
    }
  }
  function $(S, ...I) {
    if (h || !e.apply(this, arguments)) return;
    var C = S.currentTarget, A = E(this, I, !0).event(S), T = Le(S.view).on("mousemove.zoom", O, !0).on("mouseup.zoom", U, !0), P = Xe(S, C), V = S.clientX, F = S.clientY;
    xa(S.view), _r(S), A.mouse = [P, this.__zoom.invert(P)], wo(this), A.start();
    function O(Z) {
      if (sn(Z), !A.moved) {
        var ne = Z.clientX - V, se = Z.clientY - F;
        A.moved = ne * ne + se * se > x;
      }
      A.event(Z).zoom("mouse", n(g(A.that.__zoom, A.mouse[0] = Xe(Z, C), A.mouse[1]), A.extent, s));
    }
    function U(Z) {
      T.on("mousemove.zoom mouseup.zoom", null), wa(Z.view, A.moved), sn(Z), A.event(Z).end();
    }
  }
  function W(S, ...I) {
    if (e.apply(this, arguments)) {
      var C = this.__zoom, A = Xe(S.changedTouches ? S.changedTouches[0] : S, this), T = C.invert(A), P = C.k * (S.shiftKey ? 0.5 : 2), V = n(g(b(C, P), A, T), t.apply(this, I), s);
      sn(S), c > 0 ? Le(this).transition().duration(c).call(_, V, A, S) : Le(this).call(m.transform, V, A, S);
    }
  }
  function M(S, ...I) {
    if (e.apply(this, arguments)) {
      var C = S.touches, A = C.length, T = E(this, I, S.changedTouches.length === A).event(S), P, V, F, O;
      for (_r(S), V = 0; V < A; ++V)
        F = C[V], O = Xe(F, this), O = [O, this.__zoom.invert(O), F.identifier], T.touch0 ? !T.touch1 && T.touch0[2] !== O[2] && (T.touch1 = O, T.taps = 0) : (T.touch0 = O, P = !0, T.taps = 1 + !!d);
      d && (d = clearTimeout(d)), P && (T.taps < 2 && (f = O[0], d = setTimeout(function() {
        d = null;
      }, p)), wo(this), T.start());
    }
  }
  function z(S, ...I) {
    if (this.__zooming) {
      var C = E(this, I).event(S), A = S.changedTouches, T = A.length, P, V, F, O;
      for (sn(S), P = 0; P < T; ++P)
        V = A[P], F = Xe(V, this), C.touch0 && C.touch0[2] === V.identifier ? C.touch0[0] = F : C.touch1 && C.touch1[2] === V.identifier && (C.touch1[0] = F);
      if (V = C.that.__zoom, C.touch1) {
        var U = C.touch0[0], Z = C.touch0[1], ne = C.touch1[0], se = C.touch1[1], K = (K = ne[0] - U[0]) * K + (K = ne[1] - U[1]) * K, L = (L = se[0] - Z[0]) * L + (L = se[1] - Z[1]) * L;
        V = b(V, Math.sqrt(K / L)), F = [(U[0] + ne[0]) / 2, (U[1] + ne[1]) / 2], O = [(Z[0] + se[0]) / 2, (Z[1] + se[1]) / 2];
      } else if (C.touch0) F = C.touch0[0], O = C.touch0[1];
      else return;
      C.zoom("touch", n(g(V, F, O), C.extent, s));
    }
  }
  function H(S, ...I) {
    if (this.__zooming) {
      var C = E(this, I).event(S), A = S.changedTouches, T = A.length, P, V;
      for (_r(S), h && clearTimeout(h), h = setTimeout(function() {
        h = null;
      }, p), P = 0; P < T; ++P)
        V = A[P], C.touch0 && C.touch0[2] === V.identifier ? delete C.touch0 : C.touch1 && C.touch1[2] === V.identifier && delete C.touch1;
      if (C.touch1 && !C.touch0 && (C.touch0 = C.touch1, delete C.touch1), C.touch0) C.touch0[1] = this.__zoom.invert(C.touch0[0]);
      else if (C.end(), C.taps === 2 && (V = Xe(V, this), Math.hypot(f[0] - V[0], f[1] - V[1]) < v)) {
        var F = Le(this).on("dblclick.zoom");
        F && F.apply(this, arguments);
      }
    }
  }
  return m.wheelDelta = function(S) {
    return arguments.length ? (o = typeof S == "function" ? S : ao(+S), m) : o;
  }, m.filter = function(S) {
    return arguments.length ? (e = typeof S == "function" ? S : ao(!!S), m) : e;
  }, m.touchable = function(S) {
    return arguments.length ? (r = typeof S == "function" ? S : ao(!!S), m) : r;
  }, m.extent = function(S) {
    return arguments.length ? (t = typeof S == "function" ? S : ao([[+S[0][0], +S[0][1]], [+S[1][0], +S[1][1]]]), m) : t;
  }, m.scaleExtent = function(S) {
    return arguments.length ? (i[0] = +S[0], i[1] = +S[1], m) : [i[0], i[1]];
  }, m.translateExtent = function(S) {
    return arguments.length ? (s[0][0] = +S[0][0], s[1][0] = +S[1][0], s[0][1] = +S[0][1], s[1][1] = +S[1][1], m) : [[s[0][0], s[0][1]], [s[1][0], s[1][1]]];
  }, m.constrain = function(S) {
    return arguments.length ? (n = S, m) : n;
  }, m.duration = function(S) {
    return arguments.length ? (c = +S, m) : c;
  }, m.interpolate = function(S) {
    return arguments.length ? (l = S, m) : l;
  }, m.on = function() {
    var S = a.on.apply(a, arguments);
    return S === a ? m : S;
  }, m.clickDistance = function(S) {
    return arguments.length ? (x = (S = +S) * S, m) : Math.sqrt(x);
  }, m.tapDistance = function(S) {
    return arguments.length ? (v = +S, m) : v;
  }, m;
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
], za = ["Enter", " ", "Escape"], La = {
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
var yt;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(yt || (yt = {}));
var Nn;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Nn || (Nn = {}));
const Ra = {
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
var Ao;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(Ao || (Ao = {}));
var te;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(te || (te = {}));
const Zi = {
  [te.Left]: te.Right,
  [te.Right]: te.Left,
  [te.Top]: te.Bottom,
  [te.Bottom]: te.Top
};
function Ha(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Va = (e) => "id" in e && "source" in e && "target" in e, rh = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), fi = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), Dn = (e, t = [0, 0]) => {
  const { width: n, height: o } = it(e), r = e.origin ?? t, i = n * r[0], s = o * r[1];
  return {
    x: e.position.x - i,
    y: e.position.y - s
  };
}, ih = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const i = typeof r == "string";
    let s = !t.nodeLookup && !i ? r : void 0;
    t.nodeLookup && (s = i ? t.nodeLookup.get(r) : fi(r) ? r : t.nodeLookup.get(r.id));
    const c = s ? jo(s, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return Fo(o, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return Wo(n);
}, Pn = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = Fo(n, jo(r)), o = !0);
  }), o ? Wo(n) : { x: 0, y: 0, width: 0, height: 0 };
}, hi = (e, t, [n, o, r] = [0, 0, 1], i = !1, s = !1) => {
  const c = {
    ...Wt(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, l = [];
  for (const a of e.values()) {
    const { measured: d, selectable: f = !0, hidden: h = !1 } = a;
    if (s && !f || h)
      continue;
    const p = d.width ?? a.width ?? a.initialWidth ?? null, y = d.height ?? a.height ?? a.initialHeight ?? null, x = En(c, Lt(a)), v = (p ?? 0) * (y ?? 0), m = i && x > 0;
    (!a.internals.handleBounds || m || x >= v || a.dragging) && l.push(a);
  }
  return l;
}, sh = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function ah(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function ch({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: i }, s) {
  if (e.size === 0)
    return !0;
  const c = ah(e, s), l = Pn(c), a = gi(l, t, n, s?.minZoom ?? r, s?.maxZoom ?? i, s?.padding ?? 0.1);
  return await o.setViewport(a, {
    duration: s?.duration,
    ease: s?.ease,
    interpolate: s?.interpolate
  }), !0;
}
function Oa({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: i }) {
  const s = n.get(e), c = s.parentId ? n.get(s.parentId) : void 0, { x: l, y: a } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, d = s.origin ?? o;
  let f = s.extent || r;
  if (s.extent === "parent" && !s.expandParent)
    if (!c)
      i?.("005", He.error005());
    else {
      const p = c.measured.width, y = c.measured.height;
      p && y && (f = [
        [l, a],
        [l + p, a + y]
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
async function lh({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const i = new Set(e.map((h) => h.id)), s = [];
  for (const h of n) {
    if (h.deletable === !1)
      continue;
    const p = i.has(h.id), y = !p && h.parentId && s.find((x) => x.id === h.parentId);
    (p || y) && s.push(h);
  }
  const c = new Set(t.map((h) => h.id)), l = o.filter((h) => h.deletable !== !1), d = sh(s, l);
  for (const h of l)
    c.has(h.id) && !d.find((y) => y.id === h.id) && d.push(h);
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
function Ba(e, t, n) {
  const { width: o, height: r } = it(n), { x: i, y: s } = n.internals.positionAbsolute;
  return wt(e, [
    [i, s],
    [i + o, s + r]
  ], t);
}
const Ki = (e, t, n) => e < t ? zt(Math.abs(e - t), 1, t) / t : e > n ? -zt(Math.abs(e - n), 1, t) / t : 0, pi = (e, t, n = 15, o = 40) => {
  const r = Ki(e.x, o, t.width - o) * n, i = Ki(e.y, o, t.height - o) * n;
  return [r, i];
}, Fo = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), qr = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), Wo = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), Lt = (e, t = [0, 0]) => {
  const { x: n, y: o } = fi(e) ? e.internals.positionAbsolute : Dn(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, jo = (e, t = [0, 0]) => {
  const { x: n, y: o } = fi(e) ? e.internals.positionAbsolute : Dn(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, Fa = (e, t) => Wo(Fo(qr(e), qr(t))), En = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, Ui = (e) => qe(e.width) && qe(e.height) && qe(e.x) && qe(e.y), qe = (e) => !isNaN(e) && isFinite(e), Wa = (e, t) => (n, o) => {
}, $n = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), Wt = ({ x: e, y: t }, [n, o, r], i = !1, s = [1, 1]) => {
  const c = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return i ? $n(c, s) : c;
}, Rt = ({ x: e, y: t }, [n, o, r]) => ({
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
function uh(e, t, n) {
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
function dh(e, t, n, o, r, i) {
  const { x: s, y: c } = Rt(e, [t, n, o]), { x: l, y: a } = Rt({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), d = r - l, f = i - a;
  return {
    left: Math.floor(s),
    top: Math.floor(c),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const gi = (e, t, n, o, r, i) => {
  const s = uh(i, t, n), c = (t - s.x) / e.width, l = (n - s.y) / e.height, a = Math.min(c, l), d = zt(a, o, r), f = e.x + e.width / 2, h = e.y + e.height / 2, p = t / 2 - f * d, y = n / 2 - h * d, x = dh(e, p, y, d, t, n), v = {
    left: Math.min(x.left - s.left, 0),
    top: Math.min(x.top - s.top, 0),
    right: Math.min(x.right - s.right, 0),
    bottom: Math.min(x.bottom - s.bottom, 0)
  };
  return {
    x: p - v.left + v.right,
    y: y - v.top + v.bottom,
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
function Xa(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function Ya(e, t = { width: 0, height: 0 }, n, o, r) {
  const i = { ...e }, s = o.get(n);
  if (s) {
    const c = s.origin || r;
    i.x += s.internals.positionAbsolute.x - (t.width ?? 0) * c[0], i.y += s.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return i;
}
function Gi(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function fh() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function hh(e) {
  return { ...La, ...e || {} };
}
function mn(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: i, y: s } = Ze(e), c = Wt({ x: i - (r?.left ?? 0), y: s - (r?.top ?? 0) }, o), { x: l, y: a } = n ? $n(c, t) : c;
  return {
    xSnapped: l,
    ySnapped: a,
    ...c
  };
}
const yi = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), qa = (e) => e?.getRootNode?.() || window?.document, ph = ["INPUT", "SELECT", "TEXTAREA"];
function Za(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : ph.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const Ka = (e) => "clientX" in e, Ze = (e, t) => {
  const n = Ka(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, Qi = (e, t, n, o, r) => {
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
      ...yi(s)
    };
  });
};
function Ua({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: i, targetControlX: s, targetControlY: c }) {
  const l = e * 0.125 + r * 0.375 + s * 0.375 + n * 0.125, a = t * 0.125 + i * 0.375 + c * 0.375 + o * 0.125, d = Math.abs(l - e), f = Math.abs(a - t);
  return [l, a, d, f];
}
function co(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function Ji({ pos: e, x1: t, y1: n, x2: o, y2: r, c: i }) {
  switch (e) {
    case te.Left:
      return [t - co(t - o, i), n];
    case te.Right:
      return [t + co(o - t, i), n];
    case te.Top:
      return [t, n - co(n - r, i)];
    case te.Bottom:
      return [t, n + co(r - n, i)];
  }
}
function Ga({ sourceX: e, sourceY: t, sourcePosition: n = te.Bottom, targetX: o, targetY: r, targetPosition: i = te.Top, curvature: s = 0.25 }) {
  const [c, l] = Ji({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: s
  }), [a, d] = Ji({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: s
  }), [f, h, p, y] = Ua({
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
    y
  ];
}
function Qa({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, i = n < e ? n + r : n - r, s = Math.abs(o - t) / 2, c = o < t ? o + s : o - s;
  return [i, c, r, s];
}
function gh({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: i = "basic" }) {
  if (i === "manual")
    return o;
  const s = r && n ? o + 1e3 : o, c = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return s + c;
}
function yh({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const i = Fo(jo(e), jo(t));
  i.x === i.x2 && (i.x2 += 1), i.y === i.y2 && (i.y2 += 1);
  const s = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return En(s, Wo(i)) > 0;
}
const Ja = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, mh = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), xh = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", He.error006()), t;
  const o = n.getEdgeId || Ja;
  let r;
  return Va(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, mh(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, wh = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: r, ...i } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", He.error006()), n;
  if (!n.find((a) => a.id === e.id))
    return o.onError?.("007", He.error007(r)), n;
  const c = o.getEdgeId || Ja, l = {
    ...i,
    id: o.shouldReplaceId ? c(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((a) => a.id !== r).concat(l);
};
function ec({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, i, s, c] = Qa({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, i, s, c];
}
const es = {
  [te.Left]: { x: -1, y: 0 },
  [te.Right]: { x: 1, y: 0 },
  [te.Top]: { x: 0, y: -1 },
  [te.Bottom]: { x: 0, y: 1 }
}, vh = ({ source: e, sourcePosition: t = te.Bottom, target: n }) => t === te.Left || t === te.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, ts = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function bh({ source: e, sourcePosition: t = te.Bottom, target: n, targetPosition: o = te.Top, center: r, offset: i, stepPosition: s }) {
  const c = es[t], l = es[o], a = { x: e.x + c.x * i, y: e.y + c.y * i }, d = { x: n.x + l.x * i, y: n.y + l.y * i }, f = vh({
    source: a,
    sourcePosition: t,
    target: d
  }), h = f.x !== 0 ? "x" : "y", p = f[h];
  let y = [], x, v;
  const m = { x: 0, y: 0 }, b = { x: 0, y: 0 }, [, , g, w] = Qa({
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
    c[h] === p ? y = h === "x" ? D : $ : y = h === "x" ? $ : D;
  } else {
    const D = [{ x: a.x, y: d.y }], $ = [{ x: d.x, y: a.y }];
    if (h === "x" ? y = c.x === p ? $ : D : y = c.y === p ? D : $, t === o) {
      const S = Math.abs(e[h] - n[h]);
      if (S <= i) {
        const I = Math.min(i - 1, i - S);
        c[h] === p ? m[h] = (a[h] > e[h] ? -1 : 1) * I : b[h] = (d[h] > n[h] ? -1 : 1) * I;
      }
    }
    if (t !== o) {
      const S = h === "x" ? "y" : "x", I = c[h] === l[S], C = a[S] > d[S], A = a[S] < d[S];
      (c[h] === 1 && (!I && C || I && A) || c[h] !== 1 && (!I && A || I && C)) && (y = h === "x" ? D : $);
    }
    const W = { x: a.x + m.x, y: a.y + m.y }, M = { x: d.x + b.x, y: d.y + b.y }, z = Math.max(Math.abs(W.x - y[0].x), Math.abs(M.x - y[0].x)), H = Math.max(Math.abs(W.y - y[0].y), Math.abs(M.y - y[0].y));
    z >= H ? (x = (W.x + M.x) / 2, v = y[0].y) : (x = y[0].x, v = (W.y + M.y) / 2);
  }
  const _ = { x: a.x + m.x, y: a.y + m.y }, E = { x: d.x + b.x, y: d.y + b.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ..._.x !== y[0].x || _.y !== y[0].y ? [_] : [],
    ...y,
    ...E.x !== y[y.length - 1].x || E.y !== y[y.length - 1].y ? [E] : [],
    n
  ], x, v, g, w];
}
function Sh(e, t, n, o) {
  const r = Math.min(ts(e, t) / 2, ts(t, n) / 2, o), { x: i, y: s } = t;
  if (e.x === i && i === n.x || e.y === s && s === n.y)
    return `L${i} ${s}`;
  if (e.y === s) {
    const a = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${i + r * a},${s}Q ${i},${s} ${i},${s + r * d}`;
  }
  const c = e.x < n.x ? 1 : -1, l = e.y < n.y ? -1 : 1;
  return `L ${i},${s + r * l}Q ${i},${s} ${i + r * c},${s}`;
}
function Do({ sourceX: e, sourceY: t, sourcePosition: n = te.Bottom, targetX: o, targetY: r, targetPosition: i = te.Top, borderRadius: s = 5, centerX: c, centerY: l, offset: a = 20, stepPosition: d = 0.5 }) {
  const [f, h, p, y, x] = bh({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: i,
    center: { x: c, y: l },
    offset: a,
    stepPosition: d
  });
  let v = `M${f[0].x} ${f[0].y}`;
  for (let m = 1; m < f.length - 1; m++)
    v += Sh(f[m - 1], f[m], f[m + 1], s);
  return v += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [v, h, p, y, x];
}
function ns(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function Nh(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!ns(t) || !ns(n))
    return null;
  const o = t.internals.handleBounds || os(t.handles), r = n.internals.handleBounds || os(n.handles), i = rs(o?.source ?? [], e.sourceHandle), s = rs(
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
function os(e) {
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
function rs(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Zr(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function Eh(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const i = /* @__PURE__ */ new Set();
  return e.reduce((s, c) => ([c.markerStart || o, c.markerEnd || r].forEach((l) => {
    if (l && typeof l == "object") {
      const a = Zr(l, t);
      i.has(a) || (s.push({ id: a, color: l.color || n, ...l }), i.add(a));
    }
  }), s), []).sort((s, c) => s.id.localeCompare(c.id));
}
const tc = 1e3, _h = 10, mi = {
  nodeOrigin: [0, 0],
  nodeExtent: Sn,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Ch = {
  ...mi,
  checkEquality: !0
};
function xi(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function kh(e, t, n) {
  const o = xi(mi, n);
  for (const r of e.values())
    if (r.parentId)
      vi(r, e, t, o);
    else {
      const i = Dn(r, o.nodeOrigin), s = vt(r.extent) ? r.extent : o.nodeExtent, c = wt(i, s, it(r));
      r.internals.positionAbsolute = c;
    }
}
function Ih(e, t) {
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
function Kr(e, t, n, o = {}) {
  const r = xi(Ch, o), i = { i: 0 }, s = new Map(t), c = r?.elevateNodesOnSelect && !wi(r.zIndexMode) ? tc : 0;
  let l = e.length > 0, a = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = s.get(d.id);
    if (r.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const h = Dn(d, r.nodeOrigin), p = vt(d.extent) ? d.extent : r.nodeExtent, y = wt(h, p, it(d));
      f = {
        ...r.defaults,
        ...d,
        measured: {
          width: d.measured?.width,
          height: d.measured?.height
        },
        internals: {
          positionAbsolute: y,
          // if user re-initializes the node or removes `measured` for whatever reason, we reset the handleBounds so that the node gets re-measured
          handleBounds: Ih(d, f),
          z: nc(d, c, r.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (l = !1), d.parentId && vi(f, t, n, o, i), a ||= d.selected ?? !1;
  }
  return { nodesInitialized: l, hasSelectedNodes: a };
}
function Mh(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function vi(e, t, n, o, r) {
  const { elevateNodesOnSelect: i, nodeOrigin: s, nodeExtent: c, zIndexMode: l } = xi(mi, o), a = e.parentId, d = t.get(a);
  if (!d) {
    console.warn(`Parent node ${a} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Mh(e, n), r && !d.parentId && d.internals.rootParentIndex === void 0 && l === "auto" && (d.internals.rootParentIndex = ++r.i, d.internals.z = d.internals.z + r.i * _h), r && d.internals.rootParentIndex !== void 0 && (r.i = d.internals.rootParentIndex);
  const f = i && !wi(l) ? tc : 0, { x: h, y: p, z: y } = Ah(e, d, s, c, f, l), { positionAbsolute: x } = e.internals, v = h !== x.x || p !== x.y;
  (v || y !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: v ? { x: h, y: p } : x,
      z: y
    }
  });
}
function nc(e, t, n) {
  const o = qe(e.zIndex) ? e.zIndex : 0;
  return wi(n) ? o : o + (e.selected ? t : 0);
}
function Ah(e, t, n, o, r, i) {
  const { x: s, y: c } = t.internals.positionAbsolute, l = it(e), a = Dn(e, n), d = vt(e.extent) ? wt(a, e.extent, l) : a;
  let f = wt({ x: s + d.x, y: c + d.y }, o, l);
  e.extent === "parent" && (f = Ba(f, l, t));
  const h = nc(e, r, i), p = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: p >= h ? p + 1 : h
  };
}
function bi(e, t, n, o = [0, 0]) {
  const r = [], i = /* @__PURE__ */ new Map();
  for (const s of e) {
    const c = t.get(s.parentId);
    if (!c)
      continue;
    const l = i.get(s.parentId)?.expandedRect ?? Lt(c), a = Fa(l, s.rect);
    i.set(s.parentId, { expandedRect: a, parent: c });
  }
  return i.size > 0 && i.forEach(({ expandedRect: s, parent: c }, l) => {
    const a = c.internals.positionAbsolute, d = it(c), f = c.origin ?? o, h = s.x < a.x ? Math.round(Math.abs(a.x - s.x)) : 0, p = s.y < a.y ? Math.round(Math.abs(a.y - s.y)) : 0, y = Math.max(d.width, Math.round(s.width)), x = Math.max(d.height, Math.round(s.height)), v = (y - d.width) * f[0], m = (x - d.height) * f[1];
    (h > 0 || p > 0 || v || m) && (r.push({
      id: l,
      type: "position",
      position: {
        x: c.position.x - h + v,
        y: c.position.y - p + m
      }
    }), n.get(l)?.forEach((b) => {
      e.some((g) => g.id === b.id) || r.push({
        id: b.id,
        type: "position",
        position: {
          x: b.position.x + h,
          y: b.position.y + p
        }
      });
    })), (d.width < s.width || d.height < s.height || h || p) && r.push({
      id: l,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: y + (h ? f[0] * h - v : 0),
        height: x + (p ? f[1] * p - m : 0)
      }
    });
  }), r;
}
function jh(e, t, n, o, r, i, s) {
  const c = o?.querySelector(".xyflow__viewport");
  let l = !1;
  if (!c)
    return { changes: [], updatedInternals: l };
  const a = [], d = window.getComputedStyle(c), { m22: f } = new window.DOMMatrixReadOnly(d.transform), h = [];
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
      }), l = !0;
      continue;
    }
    const x = yi(p.nodeElement), v = y.measured.width !== x.width || y.measured.height !== x.height;
    if (!!(x.width && x.height && (v || !y.internals.handleBounds || p.force))) {
      const b = p.nodeElement.getBoundingClientRect(), g = vt(y.extent) ? y.extent : i;
      let { positionAbsolute: w } = y.internals;
      y.parentId && y.extent === "parent" ? w = Ba(w, x, t.get(y.parentId)) : g && (w = wt(w, g, x));
      const _ = {
        ...y,
        measured: x,
        internals: {
          ...y.internals,
          positionAbsolute: w,
          handleBounds: {
            source: Qi("source", p.nodeElement, b, f, y.id),
            target: Qi("target", p.nodeElement, b, f, y.id)
          }
        }
      };
      t.set(y.id, _), y.parentId && vi(_, t, n, { nodeOrigin: r, zIndexMode: s }), l = !0, v && (a.push({
        id: y.id,
        type: "dimensions",
        dimensions: x
      }), y.expandParent && y.parentId && h.push({
        id: y.id,
        parentId: y.parentId,
        rect: Lt(_, r)
      }));
    }
  }
  if (h.length > 0) {
    const p = bi(h, t, n, r);
    a.push(...p);
  }
  return { changes: a, updatedInternals: l };
}
async function Dh({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: i }) {
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
function is(e, t, n, o, r, i) {
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
function oc(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: i, sourceHandle: s = null, targetHandle: c = null } = o, l = { edgeId: o.id, source: r, target: i, sourceHandle: s, targetHandle: c }, a = `${r}-${s}--${i}-${c}`, d = `${i}-${c}--${r}-${s}`;
    is("source", l, d, e, r, s), is("target", l, a, e, i, c), t.set(o.id, o);
  }
}
function rc(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : rc(n, t) : !1;
}
function ss(e, t, n) {
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
function Ph(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [i, s] of e)
    if ((s.selected || s.id === o) && (!s.parentId || !rc(s, e)) && (s.draggable || t && typeof s.draggable > "u")) {
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
function Cr({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
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
function $h({ dragItems: e, snapGrid: t, x: n, y: o }) {
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
function Th({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let i = { x: null, y: null }, s = 0, c = /* @__PURE__ */ new Map(), l = !1, a = { x: 0, y: 0 }, d = null, f = !1, h = null, p = !1, y = !1, x = null;
  function v({ noDragClassName: b, handleSelector: g, domNode: w, isSelectable: _, nodeId: E, nodeClickDistance: k = 0 }) {
    h = Le(w);
    function D({ x: z, y: H }) {
      const { nodeLookup: S, nodeExtent: I, snapGrid: C, snapToGrid: A, nodeOrigin: T, onNodeDrag: P, onSelectionDrag: V, onError: F, updateNodePositions: O } = t();
      i = { x: z, y: H };
      let U = !1;
      const Z = c.size > 1, ne = Z && I ? qr(Pn(c)) : null, se = Z && A ? $h({
        dragItems: c,
        snapGrid: C,
        x: z,
        y: H
      }) : null;
      for (const [K, L] of c) {
        if (!S.has(K))
          continue;
        let Y = { x: z - L.distance.x, y: H - L.distance.y };
        A && (Y = se ? {
          x: Math.round(Y.x + se.x),
          y: Math.round(Y.y + se.y)
        } : $n(Y, C));
        let re = null;
        if (Z && I && !L.extent && ne) {
          const { positionAbsolute: ee } = L.internals, oe = ee.x - ne.x + I[0][0], R = ee.x + L.measured.width - ne.x2 + I[1][0], J = ee.y - ne.y + I[0][1], fe = ee.y + L.measured.height - ne.y2 + I[1][1];
          re = [
            [oe, J],
            [R, fe]
          ];
        }
        const { position: ie, positionAbsolute: q } = Oa({
          nodeId: K,
          nextPosition: Y,
          nodeLookup: S,
          nodeExtent: re || I,
          nodeOrigin: T,
          onError: F
        });
        U = U || L.position.x !== ie.x || L.position.y !== ie.y, L.position = ie, L.internals.positionAbsolute = q;
      }
      if (y = y || U, !!U && (O(c, !0), x && (o || P || !E && V))) {
        const [K, L] = Cr({
          nodeId: E,
          dragItems: c,
          nodeLookup: S
        });
        o?.(x, c, K, L), P?.(x, K, L), E || V?.(x, L);
      }
    }
    async function $() {
      if (!d)
        return;
      const { transform: z, panBy: H, autoPanSpeed: S, autoPanOnNodeDrag: I } = t();
      if (!I) {
        l = !1, cancelAnimationFrame(s);
        return;
      }
      const [C, A] = pi(a, d, S);
      (C !== 0 || A !== 0) && (i.x = (i.x ?? 0) - C / z[2], i.y = (i.y ?? 0) - A / z[2], await H({ x: C, y: A }) && D(i)), s = requestAnimationFrame($);
    }
    function W(z) {
      const { nodeLookup: H, multiSelectionActive: S, nodesDraggable: I, transform: C, snapGrid: A, snapToGrid: T, selectNodesOnDrag: P, onNodeDragStart: V, onSelectionDragStart: F, unselectNodesAndEdges: O } = t();
      f = !0, (!P || !_) && !S && E && (H.get(E)?.selected || O()), _ && P && E && e?.(E);
      const U = mn(z.sourceEvent, { transform: C, snapGrid: A, snapToGrid: T, containerBounds: d });
      if (i = U, c = Ph(H, I, U, E), c.size > 0 && (n || V || !E && F)) {
        const [Z, ne] = Cr({
          nodeId: E,
          dragItems: c,
          nodeLookup: H
        });
        n?.(z.sourceEvent, c, Z, ne), V?.(z.sourceEvent, Z, ne), E || F?.(z.sourceEvent, ne);
      }
    }
    const M = va().clickDistance(k).on("start", (z) => {
      const { domNode: H, nodeDragThreshold: S, transform: I, snapGrid: C, snapToGrid: A } = t();
      d = H?.getBoundingClientRect() || null, p = !1, y = !1, x = z.sourceEvent, S === 0 && W(z), i = mn(z.sourceEvent, { transform: I, snapGrid: C, snapToGrid: A, containerBounds: d }), a = Ze(z.sourceEvent, d);
    }).on("drag", (z) => {
      const { autoPanOnNodeDrag: H, transform: S, snapGrid: I, snapToGrid: C, nodeDragThreshold: A, nodeLookup: T } = t(), P = mn(z.sourceEvent, { transform: S, snapGrid: I, snapToGrid: C, containerBounds: d });
      if (x = z.sourceEvent, (z.sourceEvent.type === "touchmove" && z.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      E && !T.has(E)) && (p = !0), !p) {
        if (!l && H && f && (l = !0, $()), !f) {
          const V = Ze(z.sourceEvent, d), F = V.x - a.x, O = V.y - a.y;
          Math.sqrt(F * F + O * O) > A && W(z);
        }
        (i.x !== P.xSnapped || i.y !== P.ySnapped) && c && f && (a = Ze(z.sourceEvent, d), D(P));
      }
    }).on("end", (z) => {
      if (!f || p) {
        p && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (l = !1, f = !1, cancelAnimationFrame(s), c.size > 0) {
        const { nodeLookup: H, updateNodePositions: S, onNodeDragStop: I, onSelectionDragStop: C } = t();
        if (y && (S(c, !1), y = !1), r || I || !E && C) {
          const [A, T] = Cr({
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
      return !z.button && (!b || !ss(H, `.${b}`, w)) && (!g || ss(H, g, w));
    });
    h.call(M);
  }
  function m() {
    h?.on(".drag", null);
  }
  return {
    update: v,
    destroy: m
  };
}
function zh(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const i of t.values())
    En(r, Lt(i)) > 0 && o.push(i);
  return o;
}
const Lh = 250;
function Rh(e, t, n, o) {
  let r = [], i = 1 / 0;
  const s = zh(e, n, t + Lh);
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
function ic(e, t, n, o, r, i = !1) {
  const s = o.get(e);
  if (!s)
    return null;
  const c = r === "strict" ? s.internals.handleBounds?.[t] : [...s.internals.handleBounds?.source ?? [], ...s.internals.handleBounds?.target ?? []], l = (n ? c?.find((a) => a.id === n) : c?.[0]) ?? null;
  return l && i ? { ...l, ...bt(s, l, l.position, !0) } : l;
}
function sc(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Hh(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const ac = () => !0;
function Vh(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: i, isTarget: s, domNode: c, nodeLookup: l, lib: a, autoPanOnConnect: d, flowId: f, panBy: h, cancelConnection: p, onConnectStart: y, onConnect: x, onConnectEnd: v, isValidConnection: m = ac, onReconnectEnd: b, updateConnection: g, getTransform: w, getFromHandle: _, autoPanSpeed: E, dragThreshold: k = 1, handleDomNode: D }) {
  const $ = qa(e.target);
  let W = 0, M;
  const { x: z, y: H } = Ze(e), S = sc(i, D), I = c?.getBoundingClientRect();
  let C = !1;
  if (!I || !S)
    return;
  const A = ic(r, S, o, l, t);
  if (!A)
    return;
  let T = Ze(e, I), P = !1, V = null, F = !1, O = null;
  function U() {
    if (!d || !I)
      return;
    const [ie, q] = pi(T, I, E);
    h({ x: ie, y: q }), W = requestAnimationFrame(U);
  }
  const Z = {
    ...A,
    nodeId: r,
    type: S,
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
    toPosition: Zi[Z.position],
    toNode: null,
    pointer: T
  };
  function L() {
    C = !0, g(K), y?.(e, { nodeId: r, handleId: o, handleType: S });
  }
  k === 0 && L();
  function Y(ie) {
    if (!C) {
      const { x: fe, y: we } = Ze(ie), Oe = fe - z, ve = we - H;
      if (!(Oe * Oe + ve * ve > k * k))
        return;
      L();
    }
    if (!_() || !Z) {
      re(ie);
      return;
    }
    const q = w();
    T = Ze(ie, I), M = Rh(Wt(T, q, !1, [1, 1]), n, l, Z), P || (U(), P = !0);
    const ee = cc(ie, {
      handle: M,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: s ? "target" : "source",
      isValidConnection: m,
      doc: $,
      lib: a,
      flowId: f,
      nodeLookup: l
    });
    O = ee.handleDomNode, V = ee.connection, F = Hh(!!M, ee.isValid);
    const oe = l.get(r), R = oe ? bt(oe, Z, te.Left, !0) : K.from, J = {
      ...K,
      from: R,
      isValid: F,
      to: ee.toHandle && F ? Rt({ x: ee.toHandle.x, y: ee.toHandle.y }, q) : T,
      toHandle: ee.toHandle,
      toPosition: F && ee.toHandle ? ee.toHandle.position : Zi[Z.position],
      toNode: ee.toHandle ? l.get(ee.toHandle.nodeId) : null,
      pointer: T
    };
    g(J), K = J;
  }
  function re(ie) {
    if (!("touches" in ie && ie.touches.length > 0)) {
      if (C) {
        (M || O) && V && F && x?.(V);
        const { inProgress: q, ...ee } = K, oe = {
          ...ee,
          toPosition: K.toHandle ? K.toPosition : null
        };
        v?.(ie, oe), i && b?.(ie, oe);
      }
      p(), cancelAnimationFrame(W), P = !1, F = !1, V = null, O = null, $.removeEventListener("mousemove", Y), $.removeEventListener("mouseup", re), $.removeEventListener("touchmove", Y), $.removeEventListener("touchend", re);
    }
  }
  $.addEventListener("mousemove", Y), $.addEventListener("mouseup", re), $.addEventListener("touchmove", Y), $.addEventListener("touchend", re);
}
function cc(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: i, doc: s, lib: c, flowId: l, isValidConnection: a = ac, nodeLookup: d }) {
  const f = i === "target", h = t ? s.querySelector(`.${c}-flow__handle[data-id="${l}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: p, y } = Ze(e), x = s.elementFromPoint(p, y), v = x?.classList.contains(`${c}-flow__handle`) ? x : h, m = {
    handleDomNode: v,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (v) {
    const b = sc(void 0, v), g = v.getAttribute("data-nodeid"), w = v.getAttribute("data-handleid"), _ = v.classList.contains("connectable"), E = v.classList.contains("connectableend");
    if (!g || !b)
      return m;
    const k = {
      source: f ? g : o,
      sourceHandle: f ? w : r,
      target: f ? o : g,
      targetHandle: f ? r : w
    };
    m.connection = k;
    const $ = _ && E && (n === Tt.Strict ? f && b === "source" || !f && b === "target" : g !== o || w !== r);
    m.isValid = $ && a(k), m.toHandle = ic(g, b, w, d, n, !0);
  }
  return m;
}
const Ur = {
  onPointerDown: Vh,
  isValid: cc
};
function Oh({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = Le(e);
  function i({ translateExtent: c, width: l, height: a, zoomStep: d = 1, pannable: f = !0, zoomable: h = !0, inversePan: p = !1 }) {
    const y = (g) => {
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
    }, m = (g) => {
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
    }, b = Ta().on("start", v).on("zoom", f ? m : null).on("zoom.wheel", h ? y : null);
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
const Xo = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), kr = ({ x: e, y: t, zoom: n }) => Bo.translate(e, t).scale(n), Mt = (e, t) => e.target.closest(`.${t}`), lc = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Bh = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, Ir = (e, t = 0, n = Bh, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, uc = (e) => {
  const t = e.ctrlKey && _n() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Fh({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: i, zoomOnPinch: s, onPanZoomStart: c, onPanZoom: l, onPanZoomEnd: a }) {
  return (d) => {
    if (Mt(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && s) {
      const v = Xe(d), m = uc(d), b = f * Math.pow(2, m);
      o.scaleTo(n, b, v, d);
      return;
    }
    const h = d.deltaMode === 1 ? 20 : 1;
    let p = r === yt.Vertical ? 0 : d.deltaX * h, y = r === yt.Horizontal ? 0 : d.deltaY * h;
    !_n() && d.shiftKey && r !== yt.Vertical && (p = d.deltaY * h, y = 0), o.translateBy(
      n,
      -(p / f) * i,
      -(y / f) * i,
      // @ts-ignore
      { internal: !0 }
    );
    const x = Xo(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (l?.(d, x), e.panScrollTimeout = setTimeout(() => {
      a?.(d, x), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(d, x));
  };
}
function Wh({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const i = o.type === "wheel", s = !t && i && !o.ctrlKey, c = Mt(o, e);
    if (o.ctrlKey && i && c && o.preventDefault(), s || c)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function Xh({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = Xo(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function Yh({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (i) => {
    e.usedRightMouseButton = !!(n && lc(t, e.mouseButton ?? 0)), i.sourceEvent?.sync || o([i.transform.x, i.transform.y, i.transform.k]), r && !i.sourceEvent?.internal && r?.(i.sourceEvent, Xo(i.transform));
  };
}
function qh({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: i }) {
  return (s) => {
    if (!s.sourceEvent?.internal && (e.isZoomingOrPanning = !1, i && lc(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && s.sourceEvent && i(s.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
      const c = Xo(s.transform);
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
function Zh({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: i, userSelectionActive: s, noWheelClassName: c, noPanClassName: l, lib: a, connectionInProgress: d }) {
  return (f) => {
    const h = e || t, p = n && f.ctrlKey, y = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (Mt(f, `${a}-flow__node`) || Mt(f, `${a}-flow__edge`)))
      return !0;
    if (!o && !h && !r && !i && !n || s || d && !y || Mt(f, c) && y || Mt(f, l) && (!y || r && y && !e) || !n && f.ctrlKey && y)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!h && !r && !p && y || !o && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(o) && !o.includes(f.button) && f.type === "mousedown")
      return !1;
    const x = Array.isArray(o) && o.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || y) && x;
  };
}
function Kh({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: i, onPanZoomStart: s, onPanZoomEnd: c, onDraggingChange: l }) {
  const a = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = Ta().scaleExtent([t, n]).translateExtent(o), h = Le(e).call(f);
  b({
    x: r.x,
    y: r.y,
    zoom: zt(r.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], o);
  const p = h.on("wheel.zoom"), y = h.on("dblclick.zoom");
  f.wheelDelta(uc);
  async function x(M, z) {
    return h ? new Promise((H) => {
      f?.interpolate(z?.interpolate === "linear" ? yn : yo).transform(Ir(h, z?.duration, z?.ease, () => H(!0)), M);
    }) : !1;
  }
  function v({ noWheelClassName: M, noPanClassName: z, onPaneContextMenu: H, userSelectionActive: S, panOnScroll: I, panOnDrag: C, panOnScrollMode: A, panOnScrollSpeed: T, preventScrolling: P, zoomOnPinch: V, zoomOnScroll: F, zoomOnDoubleClick: O, zoomActivationKeyPressed: U, lib: Z, onTransformChange: ne, connectionInProgress: se, paneClickDistance: K, selectionOnDrag: L }) {
    S && !a.isZoomingOrPanning && m();
    const Y = I && !U && !S;
    f.clickDistance(L ? 1 / 0 : !qe(K) || K < 0 ? 0 : K);
    const re = Y ? Fh({
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
    }) : Wh({
      noWheelClassName: M,
      preventScrolling: P,
      d3ZoomHandler: p
    });
    h.on("wheel.zoom", re, { passive: !1 });
    const ie = Xh({
      zoomPanValues: a,
      onDraggingChange: l,
      onPanZoomStart: s
    });
    f.on("start", ie);
    const q = Yh({
      zoomPanValues: a,
      panOnDrag: C,
      onPaneContextMenu: !!H,
      onPanZoom: i,
      onTransformChange: ne
    });
    f.on("zoom", q);
    const ee = qh({
      zoomPanValues: a,
      panOnDrag: C,
      panOnScroll: I,
      onPaneContextMenu: H,
      onPanZoomEnd: c,
      onDraggingChange: l
    });
    f.on("end", ee);
    const oe = Zh({
      zoomActivationKeyPressed: U,
      panOnDrag: C,
      zoomOnScroll: F,
      panOnScroll: I,
      zoomOnDoubleClick: O,
      zoomOnPinch: V,
      userSelectionActive: S,
      noPanClassName: z,
      noWheelClassName: M,
      lib: Z,
      connectionInProgress: se
    });
    f.filter(oe), O ? h.on("dblclick.zoom", y) : h.on("dblclick.zoom", null);
  }
  function m() {
    f.on("zoom", null);
  }
  async function b(M, z, H) {
    const S = kr(M), I = f?.constrain()(S, z, H);
    return I && await x(I), I;
  }
  async function g(M, z) {
    const H = kr(M);
    return await x(H, z), H;
  }
  function w(M) {
    if (h) {
      const z = kr(M), H = h.property("__zoom");
      (H.k !== M.zoom || H.x !== M.x || H.y !== M.y) && f?.transform(h, z, null, { sync: !0 });
    }
  }
  function _() {
    const M = h ? $a(h.node()) : { x: 0, y: 0, k: 1 };
    return { x: M.x, y: M.y, zoom: M.k };
  }
  async function E(M, z) {
    return h ? new Promise((H) => {
      f?.interpolate(z?.interpolate === "linear" ? yn : yo).scaleTo(Ir(h, z?.duration, z?.ease, () => H(!0)), M);
    }) : !1;
  }
  async function k(M, z) {
    return h ? new Promise((H) => {
      f?.interpolate(z?.interpolate === "linear" ? yn : yo).scaleBy(Ir(h, z?.duration, z?.ease, () => H(!0)), M);
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
    destroy: m,
    setViewport: g,
    setViewportConstrained: b,
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
function Uh({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: i }) {
  const s = e - t, c = n - o, l = [s > 0 ? 1 : s < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return s && r && (l[0] = l[0] * -1), c && i && (l[1] = l[1] * -1), l;
}
function as(e) {
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
function lo(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function cs(e, t) {
  return e ? !t : t;
}
function Gh(e, t, n, o, r, i, s, c) {
  let { affectsX: l, affectsY: a } = t;
  const { isHorizontal: d, isVertical: f } = t, h = d && f, { xSnapped: p, ySnapped: y } = n, { minWidth: x, maxWidth: v, minHeight: m, maxHeight: b } = o, { x: g, y: w, width: _, height: E, aspectRatio: k } = e;
  let D = Math.floor(d ? p - e.pointerX : 0), $ = Math.floor(f ? y - e.pointerY : 0);
  const W = _ + (l ? -D : D), M = E + (a ? -$ : $), z = -i[0] * _, H = -i[1] * E;
  let S = lo(W, x, v), I = lo(M, m, b);
  if (s) {
    let T = 0, P = 0;
    l && D < 0 ? T = st(g + D + z, s[0][0]) : !l && D > 0 && (T = at(g + W + z, s[1][0])), a && $ < 0 ? P = st(w + $ + H, s[0][1]) : !a && $ > 0 && (P = at(w + M + H, s[1][1])), S = Math.max(S, T), I = Math.max(I, P);
  }
  if (c) {
    let T = 0, P = 0;
    l && D > 0 ? T = at(g + D, c[0][0]) : !l && D < 0 && (T = st(g + W, c[1][0])), a && $ > 0 ? P = at(w + $, c[0][1]) : !a && $ < 0 && (P = st(w + M, c[1][1])), S = Math.max(S, T), I = Math.max(I, P);
  }
  if (r) {
    if (d) {
      const T = lo(W / k, m, b) * k;
      if (S = Math.max(S, T), s) {
        let P = 0;
        !l && !a || l && !a && h ? P = at(w + H + W / k, s[1][1]) * k : P = st(w + H + (l ? D : -D) / k, s[0][1]) * k, S = Math.max(S, P);
      }
      if (c) {
        let P = 0;
        !l && !a || l && !a && h ? P = st(w + W / k, c[1][1]) * k : P = at(w + (l ? D : -D) / k, c[0][1]) * k, S = Math.max(S, P);
      }
    }
    if (f) {
      const T = lo(M * k, x, v) / k;
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
  $ = $ + ($ < 0 ? I : -I), D = D + (D < 0 ? S : -S), r && (h ? W > M * k ? $ = (cs(l, a) ? -D : D) / k : D = (cs(l, a) ? -$ : $) * k : d ? ($ = D / k, a = l) : (D = $ * k, l = a));
  const C = l ? g + D : g, A = a ? w + $ : w;
  return {
    width: _ + (l ? -D : D),
    height: E + (a ? -$ : $),
    x: i[0] * D * (l ? -1 : 1) + C,
    y: i[1] * $ * (a ? -1 : 1) + A
  };
}
const dc = { width: 0, height: 0, x: 0, y: 0 }, Qh = {
  ...dc,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function Jh(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, i = e.measured.width ?? 0, s = e.measured.height ?? 0, c = n[0] * i, l = n[1] * s;
  return [
    [o - c, r - l],
    [o + i - c, r + s - l]
  ];
}
function ep({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const i = Le(e);
  let s = {
    controlDirection: as("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: a, boundaries: d, keepAspectRatio: f, resizeDirection: h, onResizeStart: p, onResize: y, onResizeEnd: x, shouldResize: v }) {
    let m = { ...dc }, b = { ...Qh };
    s = {
      boundaries: d,
      resizeDirection: h,
      keepAspectRatio: f,
      controlDirection: as(a)
    };
    let g, w = null, _ = [], E, k, D, $ = !1;
    const W = va().on("start", (M) => {
      const { nodeLookup: z, transform: H, snapGrid: S, snapToGrid: I, nodeOrigin: C, paneDomNode: A } = n();
      if (g = z.get(t), !g)
        return;
      w = A?.getBoundingClientRect() ?? null;
      const { xSnapped: T, ySnapped: P } = mn(M.sourceEvent, {
        transform: H,
        snapGrid: S,
        snapToGrid: I,
        containerBounds: w
      });
      m = {
        width: g.measured.width ?? 0,
        height: g.measured.height ?? 0,
        x: g.position.x ?? 0,
        y: g.position.y ?? 0
      }, b = {
        ...m,
        pointerX: T,
        pointerY: P,
        aspectRatio: m.width / m.height
      }, E = void 0, k = vt(g.extent) ? g.extent : void 0, g.parentId && (g.extent === "parent" || g.expandParent) && (E = z.get(g.parentId)), E && g.extent === "parent" && (k = [
        [0, 0],
        [E.measured.width, E.measured.height]
      ]), _ = [], D = void 0;
      for (const [V, F] of z)
        if (F.parentId === t && (_.push({
          id: V,
          position: { ...F.position },
          extent: F.extent
        }), F.extent === "parent" || F.expandParent)) {
          const O = Jh(F, g, F.origin ?? C);
          D ? D = [
            [Math.min(O[0][0], D[0][0]), Math.min(O[0][1], D[0][1])],
            [Math.max(O[1][0], D[1][0]), Math.max(O[1][1], D[1][1])]
          ] : D = O;
        }
      p?.(M, { ...m });
    }).on("drag", (M) => {
      const { transform: z, snapGrid: H, snapToGrid: S, nodeOrigin: I } = n(), C = mn(M.sourceEvent, {
        transform: z,
        snapGrid: H,
        snapToGrid: S,
        containerBounds: w
      }), A = [];
      if (!g)
        return;
      const { x: T, y: P, width: V, height: F } = m, O = {}, U = g.origin ?? I, { width: Z, height: ne, x: se, y: K } = Gh(b, s.controlDirection, C, s.boundaries, s.keepAspectRatio, U, k, D), L = Z !== V, Y = ne !== F, re = se !== T && L, ie = K !== P && Y;
      if (!re && !ie && !L && !Y)
        return;
      if ((re || ie || U[0] === 1 || U[1] === 1) && (O.x = re ? se : m.x, O.y = ie ? K : m.y, m.x = O.x, m.y = O.y, _.length > 0)) {
        const R = se - T, J = K - P;
        for (const fe of _)
          fe.position = {
            x: fe.position.x - R + U[0] * (Z - V),
            y: fe.position.y - J + U[1] * (ne - F)
          }, A.push(fe);
      }
      if ((L || Y) && (O.width = L && (!s.resizeDirection || s.resizeDirection === "horizontal") ? Z : m.width, O.height = Y && (!s.resizeDirection || s.resizeDirection === "vertical") ? ne : m.height, m.width = O.width, m.height = O.height), E && g.expandParent) {
        const R = U[0] * (O.width ?? 0);
        O.x && O.x < R && (m.x = R, b.x = b.x - (O.x - R));
        const J = U[1] * (O.height ?? 0);
        O.y && O.y < J && (m.y = J, b.y = b.y - (O.y - J));
      }
      const q = Uh({
        width: m.width,
        prevWidth: V,
        height: m.height,
        prevHeight: F,
        affectsX: s.controlDirection.affectsX,
        affectsY: s.controlDirection.affectsY
      }), ee = { ...m, direction: q };
      v?.(M, ee) !== !1 && ($ = !0, y?.(M, ee), o(O, A));
    }).on("end", (M) => {
      $ && (x?.(M, { ...m }), r?.({ ...m }), $ = !1);
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
var Mr = { exports: {} }, Ar = {}, jr = { exports: {} }, Dr = {};
var ls;
function tp() {
  if (ls) return Dr;
  ls = 1;
  var e = ut;
  function t(f, h) {
    return f === h && (f !== 0 || 1 / f === 1 / h) || f !== f && h !== h;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, i = e.useLayoutEffect, s = e.useDebugValue;
  function c(f, h) {
    var p = h(), y = o({ inst: { value: p, getSnapshot: h } }), x = y[0].inst, v = y[1];
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
  return Dr.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : d, Dr;
}
var us;
function np() {
  return us || (us = 1, jr.exports = tp()), jr.exports;
}
var ds;
function op() {
  if (ds) return Ar;
  ds = 1;
  var e = ut, t = np();
  function n(a, d) {
    return a === d && (a !== 0 || 1 / a === 1 / d) || a !== a && d !== d;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, i = e.useRef, s = e.useEffect, c = e.useMemo, l = e.useDebugValue;
  return Ar.useSyncExternalStoreWithSelector = function(a, d, f, h, p) {
    var y = i(null);
    if (y.current === null) {
      var x = { hasValue: !1, value: null };
      y.current = x;
    } else x = y.current;
    y = c(
      function() {
        function m(E) {
          if (!b) {
            if (b = !0, g = E, E = h(E), p !== void 0 && x.hasValue) {
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
        var b = !1, g, w, _ = f === void 0 ? null : f;
        return [
          function() {
            return m(d());
          },
          _ === null ? void 0 : function() {
            return m(_());
          }
        ];
      },
      [d, f, h, p]
    );
    var v = r(a, y[0], y[1]);
    return s(
      function() {
        x.hasValue = !0, x.value = v;
      },
      [v]
    ), l(v), v;
  }, Ar;
}
var fs;
function rp() {
  return fs || (fs = 1, Mr.exports = op()), Mr.exports;
}
var ip = rp();
const sp = /* @__PURE__ */ bl(ip), ap = {}, hs = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (d, f) => {
    const h = typeof d == "function" ? d(t) : d;
    if (!Object.is(h, t)) {
      const p = t;
      t = f ?? (typeof h != "object" || h === null) ? h : Object.assign({}, t, h), n.forEach((y) => y(t, p));
    }
  }, r = () => t, l = { setState: o, getState: r, getInitialState: () => a, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (ap ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, a = t = e(o, r, l);
  return l;
}, cp = (e) => e ? hs(e) : hs, { useDebugValue: lp } = ut, { useSyncExternalStoreWithSelector: up } = sp, dp = (e) => e;
function fc(e, t = dp, n) {
  const o = up(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return lp(o), o;
}
const ps = (e, t) => {
  const n = cp(e), o = (r, i = t) => fc(n, r, i);
  return Object.assign(o, n), o;
}, fp = (e, t) => e ? ps(e, t) : ps;
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
var Pr = { exports: {} }, Me = {};
var gs;
function hp() {
  if (gs) return Me;
  gs = 1;
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
var ys;
function pp() {
  if (ys) return Pr.exports;
  ys = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), Pr.exports = hp(), Pr.exports;
}
var gp = pp();
const Yo = ri(null), yp = Yo.Provider, hc = He.error001("react");
function ue(e, t) {
  const n = Mn(Yo);
  if (n === null)
    throw new Error(hc);
  return fc(n, e, t);
}
function me() {
  const e = Mn(Yo);
  if (e === null)
    throw new Error(hc);
  return ge(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const ms = { display: "none" }, mp = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, pc = "react-flow__node-desc", gc = "react-flow__edge-desc", xp = "react-flow__aria-live", wp = (e) => e.ariaLiveMessage, vp = (e) => e.ariaLabelConfig;
function bp({ rfId: e }) {
  const t = ue(wp);
  return u.jsx("div", { id: `${xp}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: mp, children: t });
}
function Sp({ rfId: e, disableKeyboardA11y: t }) {
  const n = ue(vp);
  return u.jsxs(u.Fragment, { children: [u.jsx("div", { id: `${pc}-${e}`, style: ms, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), u.jsx("div", { id: `${gc}-${e}`, style: ms, children: n["edge.a11yDescription.default"] }), !t && u.jsx(bp, { rfId: e })] });
}
const qo = Lo(({ position: e = "top-left", children: t, className: n, style: o, ...r }, i) => {
  const s = `${e}`.split("-");
  return u.jsx("div", { className: Ne(["react-flow__panel", n, ...s]), style: o, ref: i, ...r, children: t });
});
qo.displayName = "Panel";
function Np({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : u.jsx(qo, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: u.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const Ep = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, uo = (e) => e.id;
function _p(e, t) {
  return ye(e.selectedNodes.map(uo), t.selectedNodes.map(uo)) && ye(e.selectedEdges.map(uo), t.selectedEdges.map(uo));
}
function Cp({ onSelectionChange: e }) {
  const t = me(), { selectedNodes: n, selectedEdges: o } = ue(Ep, _p);
  return ae(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((i) => i(r));
  }, [n, o, e]), null;
}
const kp = (e) => !!e.onSelectionChangeHandlers;
function Ip({ onSelectionChange: e }) {
  const t = ue(kp);
  return e || t ? u.jsx(Cp, { onSelectionChange: e }) : null;
}
const yc = [0, 0], Mp = { x: 0, y: 0, zoom: 1 }, Ap = [
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
], xs = [...Ap, "rfId"], jp = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), ws = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: Sn,
  nodeOrigin: yc,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function Dp(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: i, setNodeExtent: s, reset: c, setDefaultNodesAndEdges: l } = ue(jp, ye), a = me();
  ae(() => (l(e.defaultNodes, e.defaultEdges), () => {
    d.current = ws, c();
  }), []);
  const d = ce(ws);
  return ae(
    () => {
      for (const f of xs) {
        const h = e[f], p = d.current[f];
        h !== p && (typeof e[f] > "u" || (f === "nodes" ? t(h) : f === "edges" ? n(h) : f === "minZoom" ? o(h) : f === "maxZoom" ? r(h) : f === "translateExtent" ? i(h) : f === "nodeExtent" ? s(h) : f === "ariaLabelConfig" ? a.setState({ ariaLabelConfig: hh(h) }) : f === "fitView" ? a.setState({ fitViewQueued: h }) : f === "fitViewOptions" ? a.setState({ fitViewOptions: h }) : a.setState({ [f]: h })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    xs.map((f) => e[f])
  ), null;
}
function vs() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function Pp(e) {
  const [t, n] = G(e === "system" ? null : e);
  return ae(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = vs(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : vs()?.matches ? "dark" : "light";
}
const bs = typeof document < "u" ? document : null;
function Cn(e = null, t = { target: bs, actInsideInputWithModifier: !0 }) {
  const [n, o] = G(!1), r = ce(!1), i = ce(/* @__PURE__ */ new Set([])), [s, c] = ge(() => {
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
    const l = t?.target ?? bs, a = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (p) => {
        if (r.current = p.ctrlKey || p.metaKey || p.shiftKey || p.altKey, (!r.current || r.current && !a) && Za(p))
          return !1;
        const x = Ns(p.code, c);
        if (i.current.add(p[x]), Ss(s, i.current, !1)) {
          const v = p.composedPath?.()?.[0] || p.target, m = v?.nodeName === "BUTTON" || v?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !m) && p.preventDefault(), o(!0);
        }
      }, f = (p) => {
        const y = Ns(p.code, c);
        Ss(s, i.current, !0) ? (o(!1), i.current.clear()) : i.current.delete(p[y]), p.key === "Meta" && i.current.clear(), r.current = !1;
      }, h = () => {
        i.current.clear(), o(!1);
      };
      return l?.addEventListener("keydown", d), l?.addEventListener("keyup", f), window.addEventListener("blur", h), window.addEventListener("contextmenu", h), () => {
        l?.removeEventListener("keydown", d), l?.removeEventListener("keyup", f), window.removeEventListener("blur", h), window.removeEventListener("contextmenu", h);
      };
    }
  }, [e, o]), n;
}
function Ss(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function Ns(e, t) {
  return t.includes(e) ? "code" : "key";
}
const $p = () => {
  const e = me();
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
      const { width: o, height: r, minZoom: i, maxZoom: s, panZoom: c } = e.getState(), l = gi(t, o, r, i, s, n?.padding ?? 0.1);
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
      const { x: r, y: i } = o.getBoundingClientRect(), s = Rt(t, n);
      return {
        x: s.x + r,
        y: s.y + i
      };
    }
  }), []);
};
function mc(e, t) {
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
      Tp(l, c);
    n.push(c);
  }
  return r.length && r.forEach((i) => {
    i.index !== void 0 ? n.splice(i.index, 0, { ...i.item }) : n.push({ ...i.item });
  }), n;
}
function Tp(e, t) {
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
function xc(e, t) {
  return mc(e, t);
}
function wc(e, t) {
  return mc(e, t);
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
function Es({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((r) => [r.id, r]));
  for (const [r, i] of e.entries()) {
    const s = t.get(i.id), c = s?.internals?.userNode ?? s;
    c !== void 0 && c !== i && n.push({ id: i.id, item: i, type: "replace" }), c === void 0 && n.push({ item: i, type: "add", index: r });
  }
  for (const [r] of t)
    o.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function _s(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const vc = Wa();
function bc(e, t, n = {}) {
  return xh(e, t, {
    ...n,
    onError: n.onError ?? vc
  });
}
function zp(e, t, n, o = { shouldReplaceId: !0 }) {
  return wh(e, t, n, {
    ...o,
    onError: o.onError ?? vc
  });
}
const Cs = (e) => rh(e), Lp = (e) => Va(e);
function Sc(e) {
  return Lo(e);
}
const Rp = typeof window < "u" ? vl : ae;
function ks(e) {
  const [t, n] = G(BigInt(0)), [o] = G(() => Hp(() => n((r) => r + BigInt(1))));
  return Rp(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function Hp(e) {
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
const Nc = ri(null);
function Vp({ children: e }) {
  const t = me(), n = pe((c) => {
    const { nodes: l = [], setNodes: a, hasDefaultNodes: d, onNodesChange: f, nodeLookup: h, fitViewQueued: p, onNodesChangeMiddlewareMap: y } = t.getState();
    let x = l;
    for (const m of c)
      x = typeof m == "function" ? m(x) : m;
    let v = Es({
      items: x,
      lookup: h
    });
    for (const m of y.values())
      v = m(v);
    d && a(x), v.length > 0 ? f?.(v) : p && window.requestAnimationFrame(() => {
      const { fitViewQueued: m, nodes: b, setNodes: g } = t.getState();
      m && g(b);
    });
  }, []), o = ks(n), r = pe((c) => {
    const { edges: l = [], setEdges: a, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: h } = t.getState();
    let p = l;
    for (const y of c)
      p = typeof y == "function" ? y(p) : y;
    d ? a(p) : f && f(Es({
      items: p,
      lookup: h
    }));
  }, []), i = ks(r), s = ge(() => ({ nodeQueue: o, edgeQueue: i }), []);
  return u.jsx(Nc.Provider, { value: s, children: e });
}
function Op() {
  const e = Mn(Nc);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Bp = (e) => !!e.panZoom;
function Si() {
  const e = $p(), t = me(), n = Op(), o = ue(Bp), r = ge(() => {
    const i = (f) => t.getState().nodeLookup.get(f), s = (f) => {
      n.nodeQueue.push(f);
    }, c = (f) => {
      n.edgeQueue.push(f);
    }, l = (f) => {
      const { nodeLookup: h, nodeOrigin: p } = t.getState(), y = Cs(f) ? f : h.get(f.id), x = y.parentId ? Ya(y.position, y.measured, y.parentId, h, p) : y.position, v = {
        ...y,
        position: x,
        width: y.measured?.width ?? y.width,
        height: y.measured?.height ?? y.height
      };
      return Lt(v);
    }, a = (f, h, p = { replace: !1 }) => {
      s((y) => y.map((x) => {
        if (x.id === f) {
          const v = typeof h == "function" ? h(x) : h;
          return p.replace && Cs(v) ? v : { ...x, ...v };
        }
        return x;
      }));
    }, d = (f, h, p = { replace: !1 }) => {
      c((y) => y.map((x) => {
        if (x.id === f) {
          const v = typeof h == "function" ? h(x) : h;
          return p.replace && Lp(v) ? v : { ...x, ...v };
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
        const { nodes: f = [], edges: h = [], transform: p } = t.getState(), [y, x, v] = p;
        return {
          nodes: f.map((m) => ({ ...m })),
          edges: h.map((m) => ({ ...m })),
          viewport: {
            x: y,
            y: x,
            zoom: v
          }
        };
      },
      deleteElements: async ({ nodes: f = [], edges: h = [] }) => {
        const { nodes: p, edges: y, onNodesDelete: x, onEdgesDelete: v, triggerNodeChanges: m, triggerEdgeChanges: b, onDelete: g, onBeforeDelete: w } = t.getState(), { nodes: _, edges: E } = await lh({
          nodesToRemove: f,
          edgesToRemove: h,
          nodes: p,
          edges: y,
          onBeforeDelete: w
        }), k = E.length > 0, D = _.length > 0;
        if (k) {
          const $ = E.map(_s);
          v?.(E), b($);
        }
        if (D) {
          const $ = _.map(_s);
          x?.(_), m($);
        }
        return (D || k) && g?.({ nodes: _, edges: E }), { deletedNodes: _, deletedEdges: E };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, h = !0, p) => {
        const y = Ui(f), x = y ? f : l(f), v = p !== void 0;
        return x ? (p || t.getState().nodes).filter((m) => {
          const b = t.getState().nodeLookup.get(m.id);
          if (b && !y && (m.id === f.id || !b.internals.positionAbsolute))
            return !1;
          const g = Lt(v ? m : b), w = En(g, x);
          return h && w > 0 || w >= g.width * g.height || w >= x.width * x.height;
        }) : [];
      },
      isNodeIntersecting: (f, h, p = !0) => {
        const x = Ui(f) ? f : l(f);
        if (!x)
          return !1;
        const v = En(x, h);
        return p && v > 0 || v >= h.width * h.height || v >= x.width * x.height;
      },
      updateNode: a,
      updateNodeData: (f, h, p = { replace: !1 }) => {
        a(f, (y) => {
          const x = typeof h == "function" ? h(y) : h;
          return p.replace ? { ...y, data: x } : { ...y, data: { ...y.data, ...x } };
        }, p);
      },
      updateEdge: d,
      updateEdgeData: (f, h, p = { replace: !1 }) => {
        d(f, (y) => {
          const x = typeof h == "function" ? h(y) : h;
          return p.replace ? { ...y, data: x } : { ...y, data: { ...y.data, ...x } };
        }, p);
      },
      getNodesBounds: (f) => {
        const { nodeLookup: h, nodeOrigin: p } = t.getState();
        return ih(f, { nodeLookup: h, nodeOrigin: p });
      },
      getHandleConnections: ({ type: f, id: h, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}-${f}${h ? `-${h}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: h, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}${f ? h ? `-${f}-${h}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const h = t.getState().fitViewResolver ?? fh();
        return t.setState({ fitViewQueued: !0, fitViewOptions: f, fitViewResolver: h }), n.nodeQueue.push((p) => [...p]), h.promise;
      }
    };
  }, []);
  return ge(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const Is = (e) => e.selected, Fp = typeof window < "u" ? window : void 0;
function Wp({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = me(), { deleteElements: o } = Si(), r = Cn(e, { actInsideInputWithModifier: !1 }), i = Cn(t, { target: Fp });
  ae(() => {
    if (r) {
      const { edges: s, nodes: c } = n.getState();
      o({ nodes: c.filter(Is), edges: s.filter(Is) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), ae(() => {
    n.setState({ multiSelectionActive: i });
  }, [i]);
}
function Xp(e) {
  const t = me();
  ae(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = yi(e.current);
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
const Zo = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, Yp = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function qp({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: i = yt.Free, zoomOnDoubleClick: s = !0, panOnDrag: c = !0, defaultViewport: l, translateExtent: a, minZoom: d, maxZoom: f, zoomActivationKeyCode: h, preventScrolling: p = !0, children: y, noWheelClassName: x, noPanClassName: v, onViewportChange: m, isControlledViewport: b, paneClickDistance: g, selectionOnDrag: w }) {
  const _ = me(), E = ce(null), { userSelectionActive: k, lib: D, connectionInProgress: $ } = ue(Yp, ye), W = Cn(h), M = ce();
  Xp(E);
  const z = pe((H) => {
    m?.({ x: H[0], y: H[1], zoom: H[2] }), b || _.setState({ transform: H });
  }, [m, b]);
  return ae(() => {
    if (E.current) {
      M.current = Kh({
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
      const { x: H, y: S, zoom: I } = M.current.getViewport();
      return _.setState({
        panZoom: M.current,
        transform: [H, S, I],
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
  ]), u.jsx("div", { className: "react-flow__renderer", ref: E, style: Zo, children: y });
}
const Zp = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function Kp() {
  const { userSelectionActive: e, userSelectionRect: t } = ue(Zp, ye);
  return e && t ? u.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const $r = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, Up = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function Gp({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Nn.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: i, selectionOnDrag: s, onSelectionStart: c, onSelectionEnd: l, onPaneClick: a, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: h, onPaneMouseMove: p, onPaneMouseLeave: y, children: x }) {
  const v = ce(0), m = me(), { userSelectionActive: b, elementsSelectable: g, dragging: w, connectionInProgress: _, panBy: E, autoPanSpeed: k } = ue(Up, ye), D = g && (e || b), $ = ce(null), W = ce(), M = ce(/* @__PURE__ */ new Set()), z = ce(/* @__PURE__ */ new Set()), H = ce(!1), S = ce({ x: 0, y: 0 }), I = ce(!1), C = (L) => {
    if (H.current || _) {
      H.current = !1;
      return;
    }
    a?.(L), m.getState().resetSelectedElements(), m.setState({ nodesSelectionActive: !1 });
  }, A = (L) => {
    if (Array.isArray(o) && o?.includes(2)) {
      L.preventDefault();
      return;
    }
    d?.(L);
  }, T = f ? (L) => f(L) : void 0, P = (L) => {
    H.current && (L.stopPropagation(), H.current = !1);
  }, V = (L) => {
    const { domNode: Y, transform: re } = m.getState();
    if (W.current = Y?.getBoundingClientRect(), !W.current)
      return;
    const ie = L.target === $.current;
    if (!ie && !!L.target.closest(".nokey") || !e || !(s && ie || t) || L.button !== 0 || !L.isPrimary)
      return;
    L.target?.setPointerCapture?.(L.pointerId), H.current = !1;
    const { x: oe, y: R } = Ze(L.nativeEvent, W.current), J = Wt({ x: oe, y: R }, re);
    m.setState({
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
    const { userSelectionRect: re } = m.getState();
    if (!re)
      return;
    const { transform: ie, nodeLookup: q, edgeLookup: ee, connectionLookup: oe, triggerNodeChanges: R, triggerEdgeChanges: J, defaultEdgeOptions: fe } = m.getState(), we = { x: re.startX, y: re.startY }, { x: Oe, y: ve } = Rt(we, ie), De = {
      startX: we.x,
      startY: we.y,
      x: L < Oe ? L : Oe,
      y: Y < ve ? Y : ve,
      width: Math.abs(L - Oe),
      height: Math.abs(Y - ve)
    }, tt = M.current, Fe = z.current;
    M.current = new Set(hi(q, De, ie, n === Nn.Partial, !0).map((Ce) => Ce.id)), z.current = /* @__PURE__ */ new Set();
    const Be = fe?.selectable ?? !0;
    for (const Ce of M.current) {
      const Ae = oe.get(Ce);
      if (Ae)
        for (const { edgeId: je } of Ae.values()) {
          const Ue = ee.get(je);
          Ue && (Ue.selectable ?? Be) && z.current.add(je);
        }
    }
    if (!Gi(tt, M.current)) {
      const Ce = At(q, M.current, !0);
      R(Ce);
    }
    if (!Gi(Fe, z.current)) {
      const Ce = At(ee, z.current);
      J(Ce);
    }
    m.setState({
      userSelectionRect: De,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function O() {
    if (!r || !W.current)
      return;
    const [L, Y] = pi(S.current, W.current, k);
    E({ x: L, y: Y }).then((re) => {
      if (!H.current || !re) {
        v.current = requestAnimationFrame(O);
        return;
      }
      const { x: ie, y: q } = S.current;
      F(ie, q), v.current = requestAnimationFrame(O);
    });
  }
  const U = () => {
    cancelAnimationFrame(v.current), v.current = 0, I.current = !1;
  };
  ae(() => () => U(), []);
  const Z = (L) => {
    const { userSelectionRect: Y, transform: re, resetSelectedElements: ie } = m.getState();
    if (!W.current || !Y)
      return;
    const { x: q, y: ee } = Ze(L.nativeEvent, W.current);
    S.current = { x: q, y: ee };
    const oe = Rt({ x: Y.startX, y: Y.startY }, re);
    if (!H.current) {
      const R = t ? 0 : i;
      if (Math.hypot(q - oe.x, ee - oe.y) <= R)
        return;
      ie(), c?.(L);
    }
    H.current = !0, I.current || (O(), I.current = !0), F(q, ee);
  }, ne = (L) => {
    L.button === 0 && (L.target?.releasePointerCapture?.(L.pointerId), !b && L.target === $.current && m.getState().userSelectionRect && C?.(L), m.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), H.current && (l?.(L), m.setState({
      nodesSelectionActive: M.current.size > 0
    })), U());
  }, se = (L) => {
    L.target?.releasePointerCapture?.(L.pointerId), U();
  }, K = o === !0 || Array.isArray(o) && o.includes(0);
  return u.jsxs("div", { className: Ne(["react-flow__pane", { draggable: K, dragging: w, selection: e }]), onClick: D ? void 0 : $r(C, $), onContextMenu: $r(A, $), onWheel: $r(T, $), onPointerEnter: D ? void 0 : h, onPointerMove: D ? Z : p, onPointerUp: D ? ne : void 0, onPointerCancel: D ? se : void 0, onPointerDownCapture: D ? V : void 0, onClickCapture: D ? P : void 0, onPointerLeave: y, ref: $, style: Zo, children: [x, u.jsx(Kp, {})] });
}
function Gr({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: i, multiSelectionActive: s, nodeLookup: c, onError: l } = t.getState(), a = c.get(e);
  if (!a) {
    l?.("012", He.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), a.selected ? (n || a.selected && s) && (i({ nodes: [a], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function Ec({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: i, nodeClickDistance: s }) {
  const c = me(), [l, a] = G(!1), d = ce();
  return ae(() => {
    d.current = Th({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (f) => {
        Gr({
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
const Qp = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function _c() {
  const e = me();
  return pe((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: i, nodesDraggable: s, onError: c, updateNodePositions: l, nodeLookup: a, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), h = Qp(s), p = r ? i[0] : 5, y = r ? i[1] : 5, x = n.direction.x * p * n.factor, v = n.direction.y * y * n.factor;
    for (const [, m] of a) {
      if (!h(m))
        continue;
      let b = {
        x: m.internals.positionAbsolute.x + x,
        y: m.internals.positionAbsolute.y + v
      };
      r && (b = $n(b, i));
      const { position: g, positionAbsolute: w } = Oa({
        nodeId: m.id,
        nextPosition: b,
        nodeLookup: a,
        nodeExtent: o,
        nodeOrigin: d,
        onError: c
      });
      m.position = g, m.internals.positionAbsolute = w, f.set(m.id, m);
    }
    l(f);
  }, []);
}
const Ni = ri(null), Jp = Ni.Provider;
Ni.Consumer;
const Cc = () => Mn(Ni), eg = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), tg = (e, t, n) => (o) => {
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
function ng({ type: e = "source", position: t = te.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: i = !0, id: s, onConnect: c, children: l, className: a, onMouseDown: d, onTouchStart: f, ...h }, p) {
  const y = s || null, x = e === "target", v = me(), m = Cc(), { connectOnClick: b, noPanClassName: g, rfId: w } = ue(eg, ye), { connectingFrom: _, connectingTo: E, clickConnecting: k, isPossibleEndHandle: D, connectionInProcess: $, clickConnectionInProcess: W, valid: M } = ue(tg(m, y, e), ye);
  m || v.getState().onError?.("010", He.error010());
  const z = (I) => {
    const { defaultEdgeOptions: C, onConnect: A, hasDefaultEdges: T } = v.getState(), P = {
      ...C,
      ...I
    };
    if (T) {
      const { edges: V, setEdges: F, onError: O } = v.getState();
      F(bc(P, V, { onError: O }));
    }
    A?.(P), c?.(P);
  }, H = (I) => {
    if (!m)
      return;
    const C = Ka(I.nativeEvent);
    if (r && (C && I.button === 0 || !C)) {
      const A = v.getState();
      Ur.onPointerDown(I.nativeEvent, {
        handleDomNode: I.currentTarget,
        autoPanOnConnect: A.autoPanOnConnect,
        connectionMode: A.connectionMode,
        connectionRadius: A.connectionRadius,
        domNode: A.domNode,
        nodeLookup: A.nodeLookup,
        lib: A.lib,
        isTarget: x,
        handleId: y,
        nodeId: m,
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
    const { onClickConnectStart: C, onClickConnectEnd: A, connectionClickStartHandle: T, connectionMode: P, isValidConnection: V, lib: F, rfId: O, nodeLookup: U, connection: Z } = v.getState();
    if (!m || !T && !r)
      return;
    if (!T) {
      C?.(I.nativeEvent, { nodeId: m, handleId: y, handleType: e }), v.setState({ connectionClickStartHandle: { nodeId: m, type: e, id: y } });
      return;
    }
    const ne = qa(I.target), se = n || V, { connection: K, isValid: L } = Ur.isValid(I.nativeEvent, {
      handle: {
        nodeId: m,
        id: y,
        type: e
      },
      connectionMode: P,
      fromNodeId: T.nodeId,
      fromHandleId: T.id || null,
      fromType: T.type,
      isValidConnection: se,
      flowId: O,
      doc: ne,
      lib: F,
      nodeLookup: U
    });
    L && K && z(K);
    const Y = structuredClone(Z);
    delete Y.inProgress, Y.toPosition = Y.toHandle ? Y.toHandle.position : null, A?.(I, Y), v.setState({ connectionClickStartHandle: null });
  };
  return u.jsx("div", { "data-handleid": y, "data-nodeid": m, "data-handlepos": t, "data-id": `${w}-${m}-${y}-${e}`, className: Ne([
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
  ]), onMouseDown: H, onTouchStart: H, onClick: b ? S : void 0, ref: p, ...h, children: l });
}
const Vt = be(Sc(ng));
function og({ data: e, isConnectable: t, sourcePosition: n = te.Bottom }) {
  return u.jsxs(u.Fragment, { children: [e?.label, u.jsx(Vt, { type: "source", position: n, isConnectable: t })] });
}
function rg({ data: e, isConnectable: t, targetPosition: n = te.Top, sourcePosition: o = te.Bottom }) {
  return u.jsxs(u.Fragment, { children: [u.jsx(Vt, { type: "target", position: n, isConnectable: t }), e?.label, u.jsx(Vt, { type: "source", position: o, isConnectable: t })] });
}
function ig() {
  return null;
}
function sg({ data: e, isConnectable: t, targetPosition: n = te.Top }) {
  return u.jsxs(u.Fragment, { children: [u.jsx(Vt, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const Po = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, Ms = {
  input: og,
  default: rg,
  output: sg,
  group: ig
};
function ag(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const cg = (e) => {
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
function lg({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = me(), { width: r, height: i, transformString: s, userSelectionActive: c } = ue(cg, ye), l = _c(), a = ce(null);
  ae(() => {
    n || a.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !c && r !== null && i !== null;
  if (Ec({
    nodeRef: a,
    disabled: !d
  }), !d)
    return null;
  const f = e ? (p) => {
    const y = o.getState().nodes.filter((x) => x.selected);
    e(p, y);
  } : void 0, h = (p) => {
    Object.prototype.hasOwnProperty.call(Po, p.key) && (p.preventDefault(), l({
      direction: Po[p.key],
      factor: p.shiftKey ? 4 : 1
    }));
  };
  return u.jsx("div", { className: Ne(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: s
  }, children: u.jsx("div", { ref: a, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : h, style: {
    width: r,
    height: i
  } }) });
}
const As = typeof window < "u" ? window : void 0, ug = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function kc({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, paneClickDistance: c, deleteKeyCode: l, selectionKeyCode: a, selectionOnDrag: d, selectionMode: f, onSelectionStart: h, onSelectionEnd: p, multiSelectionKeyCode: y, panActivationKeyCode: x, zoomActivationKeyCode: v, elementsSelectable: m, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: w, panOnScrollSpeed: _, panOnScrollMode: E, zoomOnDoubleClick: k, panOnDrag: D, autoPanOnSelection: $, defaultViewport: W, translateExtent: M, minZoom: z, maxZoom: H, preventScrolling: S, onSelectionContextMenu: I, noWheelClassName: C, noPanClassName: A, disableKeyboardA11y: T, onViewportChange: P, isControlledViewport: V }) {
  const { nodesSelectionActive: F, userSelectionActive: O } = ue(ug, ye), U = Cn(a, { target: As }), Z = Cn(x, { target: As }), ne = Z || D, se = Z || w, K = d && ne !== !0, L = U || O || K;
  return Wp({ deleteKeyCode: l, multiSelectionKeyCode: y }), u.jsx(qp, { onPaneContextMenu: i, elementsSelectable: m, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: se, panOnScrollSpeed: _, panOnScrollMode: E, zoomOnDoubleClick: k, panOnDrag: !U && ne, defaultViewport: W, translateExtent: M, minZoom: z, maxZoom: H, zoomActivationKeyCode: v, preventScrolling: S, noWheelClassName: C, noPanClassName: A, onViewportChange: P, isControlledViewport: V, paneClickDistance: c, selectionOnDrag: K, children: u.jsxs(Gp, { onSelectionStart: h, onSelectionEnd: p, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, panOnDrag: ne, autoPanOnSelection: $, isSelecting: !!L, selectionMode: f, selectionKeyPressed: U, paneClickDistance: c, selectionOnDrag: K, children: [e, F && u.jsx(lg, { onSelectionContextMenu: I, noPanClassName: A, disableKeyboardA11y: T })] }) });
}
kc.displayName = "FlowRenderer";
const dg = be(kc), fg = (e) => (t) => e ? hi(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function hg(e) {
  return ue(pe(fg(e), [e]), ye);
}
const pg = (e) => e.updateNodeInternals;
function gg() {
  const e = ue(pg), [t] = G(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
function yg({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
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
function mg({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: i, onDoubleClick: s, nodesDraggable: c, elementsSelectable: l, nodesConnectable: a, nodesFocusable: d, resizeObserver: f, noDragClassName: h, noPanClassName: p, disableKeyboardA11y: y, rfId: x, nodeTypes: v, nodeClickDistance: m, onError: b }) {
  const { node: g, internals: w, isParent: _ } = ue((L) => {
    const Y = L.nodeLookup.get(e), re = L.parentLookup.has(e);
    return {
      node: Y,
      internals: Y.internals,
      isParent: re
    };
  }, ye);
  let E = g.type || "default", k = v?.[E] || Ms[E];
  k === void 0 && (b?.("003", He.error003(E)), E = "default", k = v?.default || Ms.default);
  const D = !!(g.draggable || c && typeof g.draggable > "u"), $ = !!(g.selectable || l && typeof g.selectable > "u"), W = !!(g.connectable || a && typeof g.connectable > "u"), M = !!(g.focusable || d && typeof g.focusable > "u"), z = me(), H = Xa(g), S = yg({ node: g, nodeType: E, hasDimensions: H, resizeObserver: f }), I = Ec({
    nodeRef: S,
    disabled: g.hidden || !D,
    noDragClassName: h,
    handleSelector: g.dragHandle,
    nodeId: e,
    isSelectable: $,
    nodeClickDistance: m
  }), C = _c();
  if (g.hidden)
    return null;
  const A = it(g), T = ag(g), P = $ || D || t || n || o || r, V = n ? (L) => n(L, { ...w.userNode }) : void 0, F = o ? (L) => o(L, { ...w.userNode }) : void 0, O = r ? (L) => r(L, { ...w.userNode }) : void 0, U = i ? (L) => i(L, { ...w.userNode }) : void 0, Z = s ? (L) => s(L, { ...w.userNode }) : void 0, ne = (L) => {
    const { selectNodesOnDrag: Y, nodeDragThreshold: re } = z.getState();
    $ && (!Y || !D || re > 0) && Gr({
      id: e,
      store: z,
      nodeRef: S
    }), t && t(L, { ...w.userNode });
  }, se = (L) => {
    if (!(Za(L.nativeEvent) || y)) {
      if (za.includes(L.key) && $) {
        const Y = L.key === "Escape";
        Gr({
          id: e,
          store: z,
          unselect: Y,
          nodeRef: S
        });
      } else if (D && g.selected && Object.prototype.hasOwnProperty.call(Po, L.key)) {
        L.preventDefault();
        const { ariaLabelConfig: Y } = z.getState();
        z.setState({
          ariaLiveMessage: Y["node.a11yDescription.ariaLiveMessage"]({
            direction: L.key.replace("Arrow", "").toLowerCase(),
            x: ~~w.positionAbsolute.x,
            y: ~~w.positionAbsolute.y
          })
        }), C({
          direction: Po[L.key],
          factor: L.shiftKey ? 4 : 1
        });
      }
    }
  }, K = () => {
    if (y || !S.current?.matches(":focus-visible"))
      return;
    const { transform: L, width: Y, height: re, autoPanOnNodeFocus: ie, setCenter: q } = z.getState();
    if (!ie)
      return;
    hi(/* @__PURE__ */ new Map([[e, g]]), { x: 0, y: 0, width: Y, height: re }, L, !0).length > 0 || q(g.position.x + A.width / 2, g.position.y + A.height / 2, {
      zoom: L[2]
    });
  };
  return u.jsx("div", { className: Ne([
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
  ]), ref: S, style: {
    zIndex: w.z,
    transform: `translate(${w.positionAbsolute.x}px,${w.positionAbsolute.y}px)`,
    pointerEvents: P ? "all" : "none",
    visibility: H ? "visible" : "hidden",
    ...g.style,
    ...T
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: V, onMouseMove: F, onMouseLeave: O, onContextMenu: U, onClick: ne, onDoubleClick: Z, onKeyDown: M ? se : void 0, tabIndex: M ? 0 : void 0, onFocus: M ? K : void 0, role: g.ariaRole ?? (M ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": y ? void 0 : `${pc}-${x}`, "aria-label": g.ariaLabel, ...g.domAttributes, children: u.jsx(Jp, { value: e, children: u.jsx(k, { id: e, data: g.data, type: E, positionAbsoluteX: w.positionAbsolute.x, positionAbsoluteY: w.positionAbsolute.y, selected: g.selected ?? !1, selectable: $, draggable: D, deletable: g.deletable ?? !0, isConnectable: W, sourcePosition: g.sourcePosition, targetPosition: g.targetPosition, dragging: I, dragHandle: g.dragHandle, zIndex: w.z, parentId: g.parentId, ...A }) }) });
}
var xg = be(mg);
const wg = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function Ic(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: i } = ue(wg, ye), s = hg(e.onlyRenderVisibleElements), c = gg();
  return u.jsx("div", { className: "react-flow__nodes", style: Zo, children: s.map((l) => (
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
    u.jsx(xg, { id: l, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: i }, l)
  )) });
}
Ic.displayName = "NodeRenderer";
const vg = be(Ic);
function bg(e) {
  return ue(pe((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const i = n.nodeLookup.get(r.source), s = n.nodeLookup.get(r.target);
        i && s && yh({
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
const Sg = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return u.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, Ng = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return u.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, js = {
  [Ao.Arrow]: Sg,
  [Ao.ArrowClosed]: Ng
};
function Eg(e) {
  const t = me();
  return ge(() => Object.prototype.hasOwnProperty.call(js, e) ? js[e] : (t.getState().onError?.("009", He.error009(e)), null), [e]);
}
const _g = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: i = "strokeWidth", strokeWidth: s, orient: c = "auto-start-reverse" }) => {
  const l = Eg(t);
  return l ? u.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: i, orient: c, refX: "0", refY: "0", children: u.jsx(l, { color: n, strokeWidth: s }) }) : null;
}, Mc = ({ defaultColor: e, rfId: t }) => {
  const n = ue((i) => i.edges), o = ue((i) => i.defaultEdgeOptions), r = ge(() => Eh(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? u.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: u.jsx("defs", { children: r.map((i) => u.jsx(_g, { id: i.id, type: i.type, color: i.color, width: i.width, height: i.height, markerUnits: i.markerUnits, strokeWidth: i.strokeWidth, orient: i.orient }, i.id)) }) }) : null;
};
Mc.displayName = "MarkerDefinitions";
var Cg = be(Mc);
function Ac({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: i, labelBgPadding: s = [2, 4], labelBgBorderRadius: c = 2, children: l, className: a, ...d }) {
  const [f, h] = G({ x: 1, y: 0, width: 0, height: 0 }), p = Ne(["react-flow__edge-textwrapper", a]), y = ce(null);
  return ae(() => {
    if (y.current) {
      const x = y.current.getBBox();
      h({
        x: x.x,
        y: x.y,
        width: x.width,
        height: x.height
      });
    }
  }, [n]), n ? u.jsxs("g", { transform: `translate(${e - f.width / 2} ${t - f.height / 2})`, className: p, visibility: f.width ? "visible" : "hidden", ...d, children: [r && u.jsx("rect", { width: f.width + 2 * s[0], x: -s[0], y: -s[1], height: f.height + 2 * s[1], className: "react-flow__edge-textbg", style: i, rx: c, ry: c }), u.jsx("text", { className: "react-flow__edge-text", y: f.height / 2, dy: "0.3em", ref: y, style: o, children: n }), l] }) : null;
}
Ac.displayName = "EdgeText";
const kg = be(Ac);
function Tn({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: c, labelBgBorderRadius: l, interactionWidth: a = 20, ...d }) {
  return u.jsxs(u.Fragment, { children: [u.jsx("path", { ...d, d: e, fill: "none", className: Ne(["react-flow__edge-path", d.className]) }), a ? u.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: a, className: "react-flow__edge-interaction" }) : null, o && qe(t) && qe(n) ? u.jsx(kg, { x: t, y: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: c, labelBgBorderRadius: l }) : null] });
}
function Ds({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === te.Left || e === te.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function jc({ sourceX: e, sourceY: t, sourcePosition: n = te.Bottom, targetX: o, targetY: r, targetPosition: i = te.Top }) {
  const [s, c] = Ds({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [l, a] = Ds({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [d, f, h, p] = Ua({
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
function Dc(e) {
  return be(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: c, label: l, labelStyle: a, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: y, markerEnd: x, markerStart: v, interactionWidth: m }) => {
    const [b, g, w] = jc({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: c
    }), _ = e.isInternal ? void 0 : t;
    return u.jsx(Tn, { id: _, path: b, labelX: g, labelY: w, label: l, labelStyle: a, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: y, markerEnd: x, markerStart: v, interactionWidth: m });
  });
}
const Ig = Dc({ isInternal: !1 }), Pc = Dc({ isInternal: !0 });
Ig.displayName = "SimpleBezierEdge";
Pc.displayName = "SimpleBezierEdgeInternal";
function $c(e) {
  return be(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: c, labelShowBg: l, labelBgStyle: a, labelBgPadding: d, labelBgBorderRadius: f, style: h, sourcePosition: p = te.Bottom, targetPosition: y = te.Top, markerEnd: x, markerStart: v, pathOptions: m, interactionWidth: b }) => {
    const [g, w, _] = Do({
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
    return u.jsx(Tn, { id: E, path: g, labelX: w, labelY: _, label: s, labelStyle: c, labelShowBg: l, labelBgStyle: a, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: x, markerStart: v, interactionWidth: b });
  });
}
const Tc = $c({ isInternal: !1 }), zc = $c({ isInternal: !0 });
Tc.displayName = "SmoothStepEdge";
zc.displayName = "SmoothStepEdgeInternal";
function Lc(e) {
  return be(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return u.jsx(Tc, { ...n, id: o, pathOptions: ge(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Mg = Lc({ isInternal: !1 }), Rc = Lc({ isInternal: !0 });
Mg.displayName = "StepEdge";
Rc.displayName = "StepEdgeInternal";
function Hc(e) {
  return be(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: c, labelShowBg: l, labelBgStyle: a, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: p, markerStart: y, interactionWidth: x }) => {
    const [v, m, b] = ec({ sourceX: n, sourceY: o, targetX: r, targetY: i }), g = e.isInternal ? void 0 : t;
    return u.jsx(Tn, { id: g, path: v, labelX: m, labelY: b, label: s, labelStyle: c, labelShowBg: l, labelBgStyle: a, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: p, markerStart: y, interactionWidth: x });
  });
}
const Ag = Hc({ isInternal: !1 }), Vc = Hc({ isInternal: !0 });
Ag.displayName = "StraightEdge";
Vc.displayName = "StraightEdgeInternal";
function Oc(e) {
  return be(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s = te.Bottom, targetPosition: c = te.Top, label: l, labelStyle: a, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: y, markerEnd: x, markerStart: v, pathOptions: m, interactionWidth: b }) => {
    const [g, w, _] = Ga({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: c,
      curvature: m?.curvature
    }), E = e.isInternal ? void 0 : t;
    return u.jsx(Tn, { id: E, path: g, labelX: w, labelY: _, label: l, labelStyle: a, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: y, markerEnd: x, markerStart: v, interactionWidth: b });
  });
}
const jg = Oc({ isInternal: !1 }), Bc = Oc({ isInternal: !0 });
jg.displayName = "BezierEdge";
Bc.displayName = "BezierEdgeInternal";
const Ps = {
  default: Bc,
  straight: Vc,
  step: Rc,
  smoothstep: zc,
  simplebezier: Pc
}, $s = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, Dg = (e, t, n) => n === te.Left ? e - t : n === te.Right ? e + t : e, Pg = (e, t, n) => n === te.Top ? e - t : n === te.Bottom ? e + t : e, Ts = "react-flow__edgeupdater";
function zs({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: i, onMouseOut: s, type: c }) {
  return u.jsx("circle", { onMouseDown: r, onMouseEnter: i, onMouseOut: s, className: Ne([Ts, `${Ts}-${c}`]), cx: Dg(t, o, e), cy: Pg(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function $g({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: i, targetY: s, sourcePosition: c, targetPosition: l, onReconnect: a, onReconnectStart: d, onReconnectEnd: f, setReconnecting: h, setUpdateHover: p }) {
  const y = me(), x = (w, _) => {
    if (w.button !== 0)
      return;
    const { autoPanOnConnect: E, domNode: k, connectionMode: D, connectionRadius: $, lib: W, onConnectStart: M, cancelConnection: z, nodeLookup: H, rfId: S, panBy: I, updateConnection: C } = y.getState(), A = _.type === "target", T = (F, O) => {
      h(!1), f?.(F, n, _.type, O);
    }, P = (F) => a?.(n, F), V = (F, O) => {
      h(!0), d?.(w, n, _.type), M?.(F, O);
    };
    Ur.onPointerDown(w.nativeEvent, {
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
      flowId: S,
      cancelConnection: z,
      panBy: I,
      isValidConnection: (...F) => y.getState().isValidConnection?.(...F) ?? !0,
      onConnect: P,
      onConnectStart: V,
      onConnectEnd: (...F) => y.getState().onConnectEnd?.(...F),
      onReconnectEnd: T,
      updateConnection: C,
      getTransform: () => y.getState().transform,
      getFromHandle: () => y.getState().connection.fromHandle,
      dragThreshold: y.getState().connectionDragThreshold,
      handleDomNode: w.currentTarget
    });
  }, v = (w) => x(w, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), m = (w) => x(w, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), b = () => p(!0), g = () => p(!1);
  return u.jsxs(u.Fragment, { children: [(e === !0 || e === "source") && u.jsx(zs, { position: c, centerX: o, centerY: r, radius: t, onMouseDown: v, onMouseEnter: b, onMouseOut: g, type: "source" }), (e === !0 || e === "target") && u.jsx(zs, { position: l, centerX: i, centerY: s, radius: t, onMouseDown: m, onMouseEnter: b, onMouseOut: g, type: "target" })] });
}
function Tg({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: i, onContextMenu: s, onMouseEnter: c, onMouseMove: l, onMouseLeave: a, reconnectRadius: d, onReconnect: f, onReconnectStart: h, onReconnectEnd: p, rfId: y, edgeTypes: x, noPanClassName: v, onError: m, disableKeyboardA11y: b }) {
  let g = ue((q) => q.edgeLookup.get(e));
  const w = ue((q) => q.defaultEdgeOptions);
  g = w ? { ...w, ...g } : g;
  let _ = g.type || "default", E = x?.[_] || Ps[_];
  E === void 0 && (m?.("011", He.error011(_)), _ = "default", E = x?.default || Ps.default);
  const k = !!(g.focusable || t && typeof g.focusable > "u"), D = typeof f < "u" && (g.reconnectable || n && typeof g.reconnectable > "u"), $ = !!(g.selectable || o && typeof g.selectable > "u"), W = ce(null), [M, z] = G(!1), [H, S] = G(!1), I = me(), { zIndex: C, sourceX: A, sourceY: T, targetX: P, targetY: V, sourcePosition: F, targetPosition: O } = ue(pe((q) => {
    const ee = q.nodeLookup.get(g.source), oe = q.nodeLookup.get(g.target);
    if (!ee || !oe)
      return {
        zIndex: g.zIndex,
        ...$s
      };
    const R = Nh({
      id: e,
      sourceNode: ee,
      targetNode: oe,
      sourceHandle: g.sourceHandle || null,
      targetHandle: g.targetHandle || null,
      connectionMode: q.connectionMode,
      onError: m
    });
    return {
      zIndex: gh({
        selected: g.selected,
        zIndex: g.zIndex,
        sourceNode: ee,
        targetNode: oe,
        elevateOnSelect: q.elevateEdgesOnSelect,
        zIndexMode: q.zIndexMode
      }),
      ...R || $s
    };
  }, [g.source, g.target, g.sourceHandle, g.targetHandle, g.selected, g.zIndex]), ye), U = ge(() => g.markerStart ? `url('#${Zr(g.markerStart, y)}')` : void 0, [g.markerStart, y]), Z = ge(() => g.markerEnd ? `url('#${Zr(g.markerEnd, y)}')` : void 0, [g.markerEnd, y]);
  if (g.hidden || A === null || T === null || P === null || V === null)
    return null;
  const ne = (q) => {
    const { addSelectedEdges: ee, unselectNodesAndEdges: oe, multiSelectionActive: R } = I.getState();
    $ && (I.setState({ nodesSelectionActive: !1 }), g.selected && R ? (oe({ nodes: [], edges: [g] }), W.current?.blur()) : ee([e])), r && r(q, g);
  }, se = i ? (q) => {
    i(q, { ...g });
  } : void 0, K = s ? (q) => {
    s(q, { ...g });
  } : void 0, L = c ? (q) => {
    c(q, { ...g });
  } : void 0, Y = l ? (q) => {
    l(q, { ...g });
  } : void 0, re = a ? (q) => {
    a(q, { ...g });
  } : void 0, ie = (q) => {
    if (!b && za.includes(q.key) && $) {
      const { unselectNodesAndEdges: ee, addSelectedEdges: oe } = I.getState();
      q.key === "Escape" ? (W.current?.blur(), ee({ edges: [g] })) : oe([e]);
    }
  };
  return u.jsx("svg", { style: { zIndex: C }, children: u.jsxs("g", { className: Ne([
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
  ]), onClick: ne, onDoubleClick: se, onContextMenu: K, onMouseEnter: L, onMouseMove: Y, onMouseLeave: re, onKeyDown: k ? ie : void 0, tabIndex: k ? 0 : void 0, role: g.ariaRole ?? (k ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": g.ariaLabel === null ? void 0 : g.ariaLabel || `Edge from ${g.source} to ${g.target}`, "aria-describedby": k ? `${gc}-${y}` : void 0, ref: W, ...g.domAttributes, children: [!H && u.jsx(E, { id: e, source: g.source, target: g.target, type: g.type, selected: g.selected, animated: g.animated, selectable: $, deletable: g.deletable ?? !0, label: g.label, labelStyle: g.labelStyle, labelShowBg: g.labelShowBg, labelBgStyle: g.labelBgStyle, labelBgPadding: g.labelBgPadding, labelBgBorderRadius: g.labelBgBorderRadius, sourceX: A, sourceY: T, targetX: P, targetY: V, sourcePosition: F, targetPosition: O, data: g.data, style: g.style, sourceHandleId: g.sourceHandle, targetHandleId: g.targetHandle, markerStart: U, markerEnd: Z, pathOptions: "pathOptions" in g ? g.pathOptions : void 0, interactionWidth: g.interactionWidth }), D && u.jsx($g, { edge: g, isReconnectable: D, reconnectRadius: d, onReconnect: f, onReconnectStart: h, onReconnectEnd: p, sourceX: A, sourceY: T, targetX: P, targetY: V, sourcePosition: F, targetPosition: O, setUpdateHover: z, setReconnecting: S })] }) });
}
var zg = be(Tg);
const Lg = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function Fc({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: i, onEdgeContextMenu: s, onEdgeMouseEnter: c, onEdgeMouseMove: l, onEdgeMouseLeave: a, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: h, onReconnectStart: p, onReconnectEnd: y, disableKeyboardA11y: x }) {
  const { edgesFocusable: v, edgesReconnectable: m, elementsSelectable: b, onError: g } = ue(Lg, ye), w = bg(t);
  return u.jsxs("div", { className: "react-flow__edges", children: [u.jsx(Cg, { defaultColor: e, rfId: n }), w.map((_) => u.jsx(zg, { id: _, edgesFocusable: v, edgesReconnectable: m, elementsSelectable: b, noPanClassName: r, onReconnect: i, onContextMenu: s, onMouseEnter: c, onMouseMove: l, onMouseLeave: a, onClick: d, reconnectRadius: f, onDoubleClick: h, onReconnectStart: p, onReconnectEnd: y, rfId: n, onError: g, edgeTypes: o, disableKeyboardA11y: x }, _))] });
}
Fc.displayName = "EdgeRenderer";
const Rg = be(Fc), Hg = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function Vg({ children: e }) {
  const t = ue(Hg);
  return u.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function Og(e) {
  const t = Si(), n = ce(!1);
  ae(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Bg = (e) => e.panZoom?.syncViewport;
function Fg(e) {
  const t = ue(Bg), n = me();
  return ae(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function Wg(e) {
  return e.connection.inProgress ? { ...e.connection, to: Wt(e.connection.to, e.transform) } : { ...e.connection };
}
function Xg(e) {
  return Wg;
}
function Yg(e) {
  const t = Xg();
  return ue(t, ye);
}
const qg = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function Zg({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: i, height: s, isValid: c, inProgress: l } = ue(qg, ye);
  return !(i && r && l) ? null : u.jsx("svg", { style: e, width: i, height: s, className: "react-flow__connectionline react-flow__container", children: u.jsx("g", { className: Ne(["react-flow__connection", Ha(c)]), children: u.jsx(Wc, { style: t, type: n, CustomComponent: o, isValid: c }) }) });
}
const Wc = ({ style: e, type: t = ct.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: i, fromNode: s, fromHandle: c, fromPosition: l, to: a, toNode: d, toHandle: f, toPosition: h, pointer: p } = Yg();
  if (!r)
    return;
  if (n)
    return u.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: s, fromHandle: c, fromX: i.x, fromY: i.y, toX: a.x, toY: a.y, fromPosition: l, toPosition: h, connectionStatus: Ha(o), toNode: d, toHandle: f, pointer: p });
  let y = "";
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
      [y] = Ga(x);
      break;
    case ct.SimpleBezier:
      [y] = jc(x);
      break;
    case ct.Step:
      [y] = Do({
        ...x,
        borderRadius: 0
      });
      break;
    case ct.SmoothStep:
      [y] = Do(x);
      break;
    default:
      [y] = ec(x);
  }
  return u.jsx("path", { d: y, fill: "none", className: "react-flow__connection-path", style: e });
};
Wc.displayName = "ConnectionLine";
const Kg = {};
function Ls(e = Kg) {
  ce(e), me(), ae(() => {
  }, [e]);
}
function Ug() {
  me(), ce(!1), ae(() => {
  }, []);
}
function Xc({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: i, onEdgeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: l, onNodeMouseLeave: a, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: h, onSelectionEnd: p, connectionLineType: y, connectionLineStyle: x, connectionLineComponent: v, connectionLineContainerStyle: m, selectionKeyCode: b, selectionOnDrag: g, selectionMode: w, multiSelectionKeyCode: _, panActivationKeyCode: E, zoomActivationKeyCode: k, deleteKeyCode: D, onlyRenderVisibleElements: $, elementsSelectable: W, defaultViewport: M, translateExtent: z, minZoom: H, maxZoom: S, preventScrolling: I, defaultMarkerColor: C, zoomOnScroll: A, zoomOnPinch: T, panOnScroll: P, panOnScrollSpeed: V, panOnScrollMode: F, zoomOnDoubleClick: O, panOnDrag: U, autoPanOnSelection: Z, onPaneClick: ne, onPaneMouseEnter: se, onPaneMouseMove: K, onPaneMouseLeave: L, onPaneScroll: Y, onPaneContextMenu: re, paneClickDistance: ie, nodeClickDistance: q, onEdgeContextMenu: ee, onEdgeMouseEnter: oe, onEdgeMouseMove: R, onEdgeMouseLeave: J, reconnectRadius: fe, onReconnect: we, onReconnectStart: Oe, onReconnectEnd: ve, noDragClassName: De, noWheelClassName: tt, noPanClassName: Fe, disableKeyboardA11y: Be, nodeExtent: Ce, rfId: Ae, viewport: je, onViewportChange: Ue }) {
  return Ls(e), Ls(t), Ug(), Og(n), Fg(je), u.jsx(dg, { onPaneClick: ne, onPaneMouseEnter: se, onPaneMouseMove: K, onPaneMouseLeave: L, onPaneContextMenu: re, onPaneScroll: Y, paneClickDistance: ie, deleteKeyCode: D, selectionKeyCode: b, selectionOnDrag: g, selectionMode: w, onSelectionStart: h, onSelectionEnd: p, multiSelectionKeyCode: _, panActivationKeyCode: E, zoomActivationKeyCode: k, elementsSelectable: W, zoomOnScroll: A, zoomOnPinch: T, zoomOnDoubleClick: O, panOnScroll: P, panOnScrollSpeed: V, panOnScrollMode: F, panOnDrag: U, autoPanOnSelection: Z, defaultViewport: M, translateExtent: z, minZoom: H, maxZoom: S, onSelectionContextMenu: f, preventScrolling: I, noDragClassName: De, noWheelClassName: tt, noPanClassName: Fe, disableKeyboardA11y: Be, onViewportChange: Ue, isControlledViewport: !!je, children: u.jsxs(Vg, { children: [u.jsx(Rg, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: s, onReconnect: we, onReconnectStart: Oe, onReconnectEnd: ve, onlyRenderVisibleElements: $, onEdgeContextMenu: ee, onEdgeMouseEnter: oe, onEdgeMouseMove: R, onEdgeMouseLeave: J, reconnectRadius: fe, defaultMarkerColor: C, noPanClassName: Fe, disableKeyboardA11y: Be, rfId: Ae }), u.jsx(Zg, { style: x, type: y, component: v, containerStyle: m }), u.jsx("div", { className: "react-flow__edgelabel-renderer" }), u.jsx(vg, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: i, onNodeMouseEnter: c, onNodeMouseMove: l, onNodeMouseLeave: a, onNodeContextMenu: d, nodeClickDistance: q, onlyRenderVisibleElements: $, noPanClassName: Fe, noDragClassName: De, disableKeyboardA11y: Be, nodeExtent: Ce, rfId: Ae }), u.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Xc.displayName = "GraphView";
const Gg = be(Xc), Qg = Wa(), Rs = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: c, minZoom: l = 0.5, maxZoom: a = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: h = "basic" } = {}) => {
  const p = /* @__PURE__ */ new Map(), y = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), v = /* @__PURE__ */ new Map(), m = o ?? t ?? [], b = n ?? e ?? [], g = d ?? [0, 0], w = f ?? Sn;
  oc(x, v, m);
  const { nodesInitialized: _ } = Kr(b, p, y, {
    nodeOrigin: g,
    nodeExtent: w,
    zIndexMode: h
  });
  let E = [0, 0, 1];
  if (s && r && i) {
    const k = Pn(p, {
      filter: (M) => !!((M.width || M.initialWidth) && (M.height || M.initialHeight))
    }), { x: D, y: $, zoom: W } = gi(k, r, i, l, a, c?.padding ?? 0.1);
    E = [D, $, W];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: i ?? 0,
    transform: E,
    nodes: b,
    nodesInitialized: _,
    nodeLookup: p,
    parentLookup: y,
    edges: m,
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
    connection: { ...Ra },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: Qg,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: La,
    zIndexMode: h,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, Jg = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: c, minZoom: l, maxZoom: a, nodeOrigin: d, nodeExtent: f, zIndexMode: h }) => fp((p, y) => {
  async function x() {
    const { nodeLookup: v, panZoom: m, fitViewOptions: b, fitViewResolver: g, width: w, height: _, minZoom: E, maxZoom: k } = y();
    m && (await ch({
      nodes: v,
      width: w,
      height: _,
      panZoom: m,
      minZoom: E,
      maxZoom: k
    }, b), g?.resolve(!0), p({ fitViewResolver: null }));
  }
  return {
    ...Rs({
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
      const { nodeLookup: m, parentLookup: b, nodeOrigin: g, elevateNodesOnSelect: w, fitViewQueued: _, zIndexMode: E, nodesSelectionActive: k } = y(), { nodesInitialized: D, hasSelectedNodes: $ } = Kr(v, m, b, {
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
      const { connectionLookup: m, edgeLookup: b } = y();
      oc(m, b, v), p({ edges: v });
    },
    setDefaultNodesAndEdges: (v, m) => {
      if (v) {
        const { setNodes: b } = y();
        b(v), p({ hasDefaultNodes: !0 });
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
    updateNodeInternals: (v) => {
      const { triggerNodeChanges: m, nodeLookup: b, parentLookup: g, domNode: w, nodeOrigin: _, nodeExtent: E, debug: k, fitViewQueued: D, zIndexMode: $ } = y(), { changes: W, updatedInternals: M } = jh(v, b, g, w, _, E, $);
      M && (kh(b, g, { nodeOrigin: _, nodeExtent: E, zIndexMode: $ }), D ? (x(), p({ fitViewQueued: !1, fitViewOptions: void 0 })) : p({}), W?.length > 0 && (k && console.log("React Flow: trigger node changes", W), m?.(W)));
    },
    updateNodePositions: (v, m = !1) => {
      const b = [];
      let g = [];
      const { nodeLookup: w, triggerNodeChanges: _, connection: E, updateConnection: k, onNodesChangeMiddlewareMap: D } = y();
      for (const [$, W] of v) {
        const M = w.get($), z = !!(M?.expandParent && M?.parentId && W?.position), H = {
          id: $,
          type: "position",
          position: z ? {
            x: Math.max(0, W.position.x),
            y: Math.max(0, W.position.y)
          } : W.position,
          dragging: m
        };
        if (M && E.inProgress && E.fromNode.id === M.id) {
          const S = bt(M, E.fromHandle, te.Left, !0);
          k({ ...E, from: S });
        }
        z && M.parentId && b.push({
          id: $,
          parentId: M.parentId,
          rect: {
            ...W.internals.positionAbsolute,
            width: W.measured.width ?? 0,
            height: W.measured.height ?? 0
          }
        }), g.push(H);
      }
      if (b.length > 0) {
        const { parentLookup: $, nodeOrigin: W } = y(), M = bi(b, w, $, W);
        g.push(...M);
      }
      for (const $ of D.values())
        g = $(g);
      _(g);
    },
    triggerNodeChanges: (v) => {
      const { onNodesChange: m, setNodes: b, nodes: g, hasDefaultNodes: w, debug: _ } = y();
      if (v?.length) {
        if (w) {
          const E = xc(v, g);
          b(E);
        }
        _ && console.log("React Flow: trigger node changes", v), m?.(v);
      }
    },
    triggerEdgeChanges: (v) => {
      const { onEdgesChange: m, setEdges: b, edges: g, hasDefaultEdges: w, debug: _ } = y();
      if (v?.length) {
        if (w) {
          const E = wc(v, g);
          b(E);
        }
        _ && console.log("React Flow: trigger edge changes", v), m?.(v);
      }
    },
    addSelectedNodes: (v) => {
      const { multiSelectionActive: m, edgeLookup: b, nodeLookup: g, triggerNodeChanges: w, triggerEdgeChanges: _ } = y();
      if (m) {
        const E = v.map((k) => ht(k, !0));
        w(E);
        return;
      }
      w(At(g, /* @__PURE__ */ new Set([...v]), !0)), _(At(b));
    },
    addSelectedEdges: (v) => {
      const { multiSelectionActive: m, edgeLookup: b, nodeLookup: g, triggerNodeChanges: w, triggerEdgeChanges: _ } = y();
      if (m) {
        const E = v.map((k) => ht(k, !0));
        _(E);
        return;
      }
      _(At(b, /* @__PURE__ */ new Set([...v]))), w(At(g, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: v, edges: m } = {}) => {
      const { edges: b, nodes: g, nodeLookup: w, triggerNodeChanges: _, triggerEdgeChanges: E } = y(), k = v || g, D = m || b, $ = [];
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
      const { panZoom: m, maxZoom: b } = y();
      m?.setScaleExtent([v, b]), p({ minZoom: v });
    },
    setMaxZoom: (v) => {
      const { panZoom: m, minZoom: b } = y();
      m?.setScaleExtent([b, v]), p({ maxZoom: v });
    },
    setTranslateExtent: (v) => {
      y().panZoom?.setTranslateExtent(v), p({ translateExtent: v });
    },
    resetSelectedElements: () => {
      const { edges: v, nodes: m, triggerNodeChanges: b, triggerEdgeChanges: g, elementsSelectable: w } = y();
      if (!w)
        return;
      const _ = m.reduce((k, D) => D.selected ? [...k, ht(D.id, !1)] : k, []), E = v.reduce((k, D) => D.selected ? [...k, ht(D.id, !1)] : k, []);
      b(_), g(E);
    },
    setNodeExtent: (v) => {
      const { nodes: m, nodeLookup: b, parentLookup: g, nodeOrigin: w, elevateNodesOnSelect: _, nodeExtent: E, zIndexMode: k } = y();
      v[0][0] === E[0][0] && v[0][1] === E[0][1] && v[1][0] === E[1][0] && v[1][1] === E[1][1] || (Kr(m, b, g, {
        nodeOrigin: w,
        nodeExtent: v,
        elevateNodesOnSelect: _,
        checkEquality: !1,
        zIndexMode: k
      }), p({ nodeExtent: v }));
    },
    panBy: (v) => {
      const { transform: m, width: b, height: g, panZoom: w, translateExtent: _ } = y();
      return Dh({ delta: v, panZoom: w, transform: m, translateExtent: _, width: b, height: g });
    },
    setCenter: async (v, m, b) => {
      const { width: g, height: w, maxZoom: _, panZoom: E } = y();
      if (!E)
        return !1;
      const k = typeof b?.zoom < "u" ? b.zoom : _;
      return await E.setViewport({
        x: g / 2 - v * k,
        y: w / 2 - m * k,
        zoom: k
      }, { duration: b?.duration, ease: b?.ease, interpolate: b?.interpolate }), !0;
    },
    cancelConnection: () => {
      p({
        connection: { ...Ra }
      });
    },
    updateConnection: (v) => {
      p({ connection: v });
    },
    reset: () => p({ ...Rs() })
  };
}, Object.is);
function ey({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: i, initialMinZoom: s, initialMaxZoom: c, initialFitViewOptions: l, fitView: a, nodeOrigin: d, nodeExtent: f, zIndexMode: h, children: p }) {
  const [y] = G(() => Jg({
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
  return u.jsx(yp, { value: y, children: u.jsx(Vp, { children: p }) });
}
function ty({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: i, height: s, fitView: c, fitViewOptions: l, minZoom: a, maxZoom: d, nodeOrigin: f, nodeExtent: h, zIndexMode: p }) {
  return Mn(Yo) ? u.jsx(u.Fragment, { children: e }) : u.jsx(ey, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: i, initialHeight: s, fitView: c, initialFitViewOptions: l, initialMinZoom: a, initialMaxZoom: d, nodeOrigin: f, nodeExtent: h, zIndexMode: p, children: e });
}
const ny = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function oy({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: i, edgeTypes: s, onNodeClick: c, onEdgeClick: l, onInit: a, onMove: d, onMoveStart: f, onMoveEnd: h, onConnect: p, onConnectStart: y, onConnectEnd: x, onClickConnectStart: v, onClickConnectEnd: m, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: w, onNodeContextMenu: _, onNodeDoubleClick: E, onNodeDragStart: k, onNodeDrag: D, onNodeDragStop: $, onNodesDelete: W, onEdgesDelete: M, onDelete: z, onSelectionChange: H, onSelectionDragStart: S, onSelectionDrag: I, onSelectionDragStop: C, onSelectionContextMenu: A, onSelectionStart: T, onSelectionEnd: P, onBeforeDelete: V, connectionMode: F, connectionLineType: O = ct.Bezier, connectionLineStyle: U, connectionLineComponent: Z, connectionLineContainerStyle: ne, deleteKeyCode: se = "Backspace", selectionKeyCode: K = "Shift", selectionOnDrag: L = !1, selectionMode: Y = Nn.Full, panActivationKeyCode: re = "Space", multiSelectionKeyCode: ie = _n() ? "Meta" : "Control", zoomActivationKeyCode: q = _n() ? "Meta" : "Control", snapToGrid: ee, snapGrid: oe, onlyRenderVisibleElements: R = !1, selectNodesOnDrag: J, nodesDraggable: fe, autoPanOnNodeFocus: we, nodesConnectable: Oe, nodesFocusable: ve, nodeOrigin: De = yc, edgesFocusable: tt, edgesReconnectable: Fe, elementsSelectable: Be = !0, defaultViewport: Ce = Mp, minZoom: Ae = 0.5, maxZoom: je = 2, translateExtent: Ue = Sn, preventScrolling: Te = !0, nodeExtent: St, defaultMarkerColor: xe = "#b1b1b7", zoomOnScroll: We = !0, zoomOnPinch: Xt = !0, panOnScroll: Ee = !1, panOnScrollSpeed: Yt = 0.5, panOnScrollMode: qt = yt.Free, zoomOnDoubleClick: ke = !0, panOnDrag: dt = !0, onPaneClick: Ln, onPaneMouseEnter: Rn, onPaneMouseMove: Zt, onPaneMouseLeave: Qo, onPaneScroll: Kt, onPaneContextMenu: Nt, paneClickDistance: ze = 1, nodeClickDistance: Ut = 0, children: Gt, onReconnect: Qt, onReconnectStart: Et, onReconnectEnd: Jo, onEdgeContextMenu: er, onEdgeDoubleClick: tr, onEdgeMouseEnter: nr, onEdgeMouseMove: Hn, onEdgeMouseLeave: Vn, reconnectRadius: On = 10, onNodesChange: or, onEdgesChange: Jt, noDragClassName: rr = "nodrag", noWheelClassName: ir = "nowheel", noPanClassName: Bn = "nopan", fitView: Fn, fitViewOptions: Wn, connectOnClick: en, attributionPosition: sr, proOptions: ar, defaultEdgeOptions: cr, elevateNodesOnSelect: lr = !0, elevateEdgesOnSelect: ur = !1, disableKeyboardA11y: Xn = !1, autoPanOnConnect: dr, autoPanOnNodeDrag: Yn, autoPanOnSelection: qn = !0, autoPanSpeed: fr, connectionRadius: hr, isValidConnection: pr, onError: gr, style: yr, id: tn, nodeDragThreshold: Zn, connectionDragThreshold: Kn, viewport: Un, onViewportChange: Gn, width: mr, height: xr, colorMode: Qn = "light", debug: Jn, onScroll: eo, ariaLabelConfig: to, zIndexMode: _t = "basic", ...nn }, no) {
  const Ct = tn || "1", wr = Pp(Qn), vr = pe((N) => {
    N.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), eo?.(N);
  }, [eo]);
  return u.jsx("div", { "data-testid": "rf__wrapper", ...nn, onScroll: vr, style: { ...yr, ...ny }, ref: no, className: Ne(["react-flow", r, wr]), id: tn, role: "application", children: u.jsxs(ty, { nodes: e, edges: t, width: mr, height: xr, fitView: Fn, fitViewOptions: Wn, minZoom: Ae, maxZoom: je, nodeOrigin: De, nodeExtent: St, zIndexMode: _t, children: [u.jsx(Dp, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: p, onConnectStart: y, onConnectEnd: x, onClickConnectStart: v, onClickConnectEnd: m, nodesDraggable: fe, autoPanOnNodeFocus: we, nodesConnectable: Oe, nodesFocusable: ve, edgesFocusable: tt, edgesReconnectable: Fe, elementsSelectable: Be, elevateNodesOnSelect: lr, elevateEdgesOnSelect: ur, minZoom: Ae, maxZoom: je, nodeExtent: St, onNodesChange: or, onEdgesChange: Jt, snapToGrid: ee, snapGrid: oe, connectionMode: F, translateExtent: Ue, connectOnClick: en, defaultEdgeOptions: cr, fitView: Fn, fitViewOptions: Wn, onNodesDelete: W, onEdgesDelete: M, onDelete: z, onNodeDragStart: k, onNodeDrag: D, onNodeDragStop: $, onSelectionDrag: I, onSelectionDragStart: S, onSelectionDragStop: C, onMove: d, onMoveStart: f, onMoveEnd: h, noPanClassName: Bn, nodeOrigin: De, rfId: Ct, autoPanOnConnect: dr, autoPanOnNodeDrag: Yn, autoPanSpeed: fr, onError: gr, connectionRadius: hr, isValidConnection: pr, selectNodesOnDrag: J, nodeDragThreshold: Zn, connectionDragThreshold: Kn, onBeforeDelete: V, debug: Jn, ariaLabelConfig: to, zIndexMode: _t }), u.jsx(Gg, { onInit: a, onNodeClick: c, onEdgeClick: l, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: w, onNodeContextMenu: _, onNodeDoubleClick: E, nodeTypes: i, edgeTypes: s, connectionLineType: O, connectionLineStyle: U, connectionLineComponent: Z, connectionLineContainerStyle: ne, selectionKeyCode: K, selectionOnDrag: L, selectionMode: Y, deleteKeyCode: se, multiSelectionKeyCode: ie, panActivationKeyCode: re, zoomActivationKeyCode: q, onlyRenderVisibleElements: R, defaultViewport: Ce, translateExtent: Ue, minZoom: Ae, maxZoom: je, preventScrolling: Te, zoomOnScroll: We, zoomOnPinch: Xt, zoomOnDoubleClick: ke, panOnScroll: Ee, panOnScrollSpeed: Yt, panOnScrollMode: qt, panOnDrag: dt, autoPanOnSelection: qn, onPaneClick: Ln, onPaneMouseEnter: Rn, onPaneMouseMove: Zt, onPaneMouseLeave: Qo, onPaneScroll: Kt, onPaneContextMenu: Nt, paneClickDistance: ze, nodeClickDistance: Ut, onSelectionContextMenu: A, onSelectionStart: T, onSelectionEnd: P, onReconnect: Qt, onReconnectStart: Et, onReconnectEnd: Jo, onEdgeContextMenu: er, onEdgeDoubleClick: tr, onEdgeMouseEnter: nr, onEdgeMouseMove: Hn, onEdgeMouseLeave: Vn, reconnectRadius: On, defaultMarkerColor: xe, noDragClassName: rr, noWheelClassName: ir, noPanClassName: Bn, rfId: Ct, disableKeyboardA11y: Xn, nodeExtent: St, viewport: Un, onViewportChange: Gn }), u.jsx(Ip, { onSelectionChange: H }), Gt, u.jsx(Np, { proOptions: ar, position: sr }), u.jsx(Sp, { rfId: Ct, disableKeyboardA11y: Xn })] }) });
}
var ry = Sc(oy);
const iy = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function sy({ children: e }) {
  const t = ue(iy);
  return t ? gp.createPortal(e, t) : null;
}
function ay({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return u.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ne(["react-flow__background-pattern", n, o]) });
}
function cy({ radius: e, className: t }) {
  return u.jsx("circle", { cx: e, cy: e, r: e, className: Ne(["react-flow__background-pattern", "dots", t]) });
}
var lt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(lt || (lt = {}));
const ly = {
  [lt.Dots]: 1,
  [lt.Lines]: 1,
  [lt.Cross]: 6
}, uy = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function Yc({
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
  const f = ce(null), { transform: h, patternId: p } = ue(uy, ye), y = o || ly[t], x = t === lt.Dots, v = t === lt.Cross, m = Array.isArray(n) ? n : [n, n], b = [m[0] * h[2] || 1, m[1] * h[2] || 1], g = y * h[2], w = Array.isArray(i) ? i : [i, i], _ = v ? [g, g] : b, E = [
    w[0] * h[2] || 1 + _[0] / 2,
    w[1] * h[2] || 1 + _[1] / 2
  ], k = `${p}${e || ""}`;
  return u.jsxs("svg", { className: Ne(["react-flow__background", a]), style: {
    ...l,
    ...Zo,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": s
  }, ref: f, "data-testid": "rf__background", children: [u.jsx("pattern", { id: k, x: h[0] % b[0], y: h[1] % b[1], width: b[0], height: b[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${E[0]},-${E[1]})`, children: x ? u.jsx(cy, { radius: g / 2, className: d }) : u.jsx(ay, { dimensions: _, lineWidth: r, variant: t, className: d }) }), u.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${k})` })] });
}
Yc.displayName = "Background";
const dy = be(Yc);
function fy() {
  return u.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: u.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function hy() {
  return u.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: u.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function py() {
  return u.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: u.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function gy() {
  return u.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: u.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function yy() {
  return u.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: u.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function fo({ children: e, className: t, ...n }) {
  return u.jsx("button", { type: "button", className: Ne(["react-flow__controls-button", t]), ...n, children: e });
}
const my = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function qc({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: i, onZoomOut: s, onFitView: c, onInteractiveChange: l, className: a, children: d, position: f = "bottom-left", orientation: h = "vertical", "aria-label": p }) {
  const y = me(), { isInteractive: x, minZoomReached: v, maxZoomReached: m, ariaLabelConfig: b } = ue(my, ye), { zoomIn: g, zoomOut: w, fitView: _ } = Si(), E = () => {
    g(), i?.();
  }, k = () => {
    w(), s?.();
  }, D = () => {
    _(r), c?.();
  }, $ = () => {
    y.setState({
      nodesDraggable: !x,
      nodesConnectable: !x,
      elementsSelectable: !x
    }), l?.(!x);
  }, W = h === "horizontal" ? "horizontal" : "vertical";
  return u.jsxs(qo, { className: Ne(["react-flow__controls", W, a]), position: f, style: e, "data-testid": "rf__controls", "aria-label": p ?? b["controls.ariaLabel"], children: [t && u.jsxs(u.Fragment, { children: [u.jsx(fo, { onClick: E, className: "react-flow__controls-zoomin", title: b["controls.zoomIn.ariaLabel"], "aria-label": b["controls.zoomIn.ariaLabel"], disabled: m, children: u.jsx(fy, {}) }), u.jsx(fo, { onClick: k, className: "react-flow__controls-zoomout", title: b["controls.zoomOut.ariaLabel"], "aria-label": b["controls.zoomOut.ariaLabel"], disabled: v, children: u.jsx(hy, {}) })] }), n && u.jsx(fo, { className: "react-flow__controls-fitview", onClick: D, title: b["controls.fitView.ariaLabel"], "aria-label": b["controls.fitView.ariaLabel"], children: u.jsx(py, {}) }), o && u.jsx(fo, { className: "react-flow__controls-interactive", onClick: $, title: b["controls.interactive.ariaLabel"], "aria-label": b["controls.interactive.ariaLabel"], children: x ? u.jsx(yy, {}) : u.jsx(gy, {}) }), d] });
}
qc.displayName = "Controls";
const xy = be(qc);
function wy({ id: e, x: t, y: n, width: o, height: r, style: i, color: s, strokeColor: c, strokeWidth: l, className: a, borderRadius: d, shapeRendering: f, selected: h, onClick: p }) {
  const { background: y, backgroundColor: x } = i || {}, v = s || y || x;
  return u.jsx("rect", { className: Ne(["react-flow__minimap-node", { selected: h }, a]), x: t, y: n, rx: d, ry: d, width: o, height: r, style: {
    fill: v,
    stroke: c,
    strokeWidth: l
  }, shapeRendering: f, onClick: p ? (m) => p(m, e) : void 0 });
}
const vy = be(wy), by = (e) => e.nodes.map((t) => t.id), Tr = (e) => e instanceof Function ? e : () => e;
function Sy({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: i = vy,
  onClick: s
}) {
  const c = ue(by, ye), l = Tr(t), a = Tr(e), d = Tr(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return u.jsx(u.Fragment, { children: c.map((h) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    u.jsx(Ey, { id: h, nodeColorFunc: l, nodeStrokeColorFunc: a, nodeClassNameFunc: d, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: i, onClick: s, shapeRendering: f }, h)
  )) });
}
function Ny({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: i, shapeRendering: s, NodeComponent: c, onClick: l }) {
  const { node: a, x: d, y: f, width: h, height: p } = ue((y) => {
    const x = y.nodeLookup.get(e);
    if (!x)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const v = x.internals.userNode, { x: m, y: b } = x.internals.positionAbsolute, { width: g, height: w } = it(v);
    return {
      node: v,
      x: m,
      y: b,
      width: g,
      height: w
    };
  }, ye);
  return !a || a.hidden || !Xa(a) ? null : u.jsx(c, { x: d, y: f, width: h, height: p, style: a.style, selected: !!a.selected, className: o(a), color: t(a), borderRadius: r, strokeColor: n(a), strokeWidth: i, shapeRendering: s, onClick: l, id: a.id });
}
const Ey = be(Ny);
var _y = be(Sy);
const Cy = 200, ky = 150, Iy = (e) => !e.hidden, My = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? Fa(Pn(e.nodeLookup, { filter: Iy }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, Ay = "react-flow__minimap-desc";
function Zc({
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
  onNodeClick: y,
  pannable: x = !1,
  zoomable: v = !1,
  ariaLabel: m,
  inversePan: b,
  zoomStep: g = 1,
  offsetScale: w = 5
}) {
  const _ = me(), E = ce(null), { boundingRect: k, viewBB: D, rfId: $, panZoom: W, translateExtent: M, flowWidth: z, flowHeight: H, ariaLabelConfig: S } = ue(My, ye), I = e?.width ?? Cy, C = e?.height ?? ky, A = k.width / I, T = k.height / C, P = Math.max(A, T), V = P * I, F = P * C, O = w * P, U = k.x - (V - k.width) / 2 - O, Z = k.y - (F - k.height) / 2 - O, ne = V + O * 2, se = F + O * 2, K = `${Ay}-${$}`, L = ce(0), Y = ce();
  L.current = P, ae(() => {
    if (E.current && W)
      return Y.current = Oh({
        domNode: E.current,
        panZoom: W,
        getTransform: () => _.getState().transform,
        getViewScale: () => L.current
      }), () => {
        Y.current?.destroy();
      };
  }, [W]), ae(() => {
    Y.current?.update({
      translateExtent: M,
      width: z,
      height: H,
      inversePan: b,
      pannable: x,
      zoomStep: g,
      zoomable: v
    });
  }, [x, v, b, g, M, z, H]);
  const re = p ? (ee) => {
    const [oe, R] = Y.current?.pointer(ee) || [0, 0];
    p(ee, { x: oe, y: R });
  } : void 0, ie = y ? pe((ee, oe) => {
    const R = _.getState().nodeLookup.get(oe).internals.userNode;
    y(ee, R);
  }, []) : void 0, q = m ?? S["minimap.ariaLabel"];
  return u.jsx(qo, { position: h, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-background-color-props": typeof a == "string" ? a : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * P : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof s == "number" ? s : void 0
  }, className: Ne(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: u.jsxs("svg", { width: I, height: C, viewBox: `${U} ${Z} ${ne} ${se}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": K, ref: E, onClick: re, children: [q && u.jsx("title", { id: K, children: q }), u.jsx(_y, { onClick: ie, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: i, nodeClassName: r, nodeStrokeWidth: s, nodeComponent: c }), u.jsx("path", { className: "react-flow__minimap-mask", d: `M${U - O},${Z - O}h${ne + O * 2}v${se + O * 2}h${-ne - O * 2}z
        M${D.x},${D.y}h${D.width}v${D.height}h${-D.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
Zc.displayName = "MiniMap";
const jy = be(Zc), Dy = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, Py = {
  [Ht.Line]: "right",
  [Ht.Handle]: "bottom-right"
};
function $y({ nodeId: e, position: t, variant: n = Ht.Handle, className: o, style: r = void 0, children: i, color: s, minWidth: c = 10, minHeight: l = 10, maxWidth: a = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: h, autoScale: p = !0, shouldResize: y, onResizeStart: x, onResize: v, onResizeEnd: m }) {
  const b = Cc(), g = typeof e == "string" ? e : b, w = me(), _ = ce(null), E = n === Ht.Handle, k = ue(pe(Dy(E && p), [E, p]), ye), D = ce(null), $ = t ?? Py[n];
  ae(() => {
    if (!(!_.current || !g))
      return D.current || (D.current = ep({
        domNode: _.current,
        nodeId: g,
        getStoreItems: () => {
          const { nodeLookup: M, transform: z, snapGrid: H, snapToGrid: S, nodeOrigin: I, domNode: C } = w.getState();
          return {
            nodeLookup: M,
            transform: z,
            snapGrid: H,
            snapToGrid: S,
            nodeOrigin: I,
            paneDomNode: C
          };
        },
        onChange: (M, z) => {
          const { triggerNodeChanges: H, nodeLookup: S, parentLookup: I, nodeOrigin: C } = w.getState(), A = [], T = { x: M.x, y: M.y }, P = S.get(g);
          if (P && P.expandParent && P.parentId) {
            const V = P.origin ?? C, F = M.width ?? P.measured.width ?? 0, O = M.height ?? P.measured.height ?? 0, U = {
              id: P.id,
              parentId: P.parentId,
              rect: {
                width: F,
                height: O,
                ...Ya({
                  x: M.x ?? P.position.x,
                  y: M.y ?? P.position.y
                }, { width: F, height: O }, P.parentId, S, V)
              }
            }, Z = bi([U], S, I, C);
            A.push(...Z), T.x = M.x ? Math.max(V[0] * F, M.x) : void 0, T.y = M.y ? Math.max(V[1] * O, M.y) : void 0;
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
            const F = {
              id: g,
              type: "dimensions",
              resizing: !0,
              setAttributes: h ? h === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: M.width,
                height: M.height
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
        onResizeEnd: m,
        shouldResize: y
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
    m,
    y
  ]);
  const W = $.split("-");
  return u.jsx("div", { className: Ne(["react-flow__resize-control", "nodrag", ...W, n, o]), ref: _, style: {
    ...r,
    scale: k,
    ...s && { [E ? "backgroundColor" : "borderColor"]: s }
  }, children: i });
}
be($y);
const Ty = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), Kc = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var zy = {
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
const Ly = Lo(
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
      ...zy,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: Kc("lucide", r),
      ...c
    },
    [
      ...s.map(([a, d]) => Rr(a, d)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const Se = (e, t) => {
  const n = Lo(
    ({ className: o, ...r }, i) => Rr(Ly, {
      ref: i,
      iconNode: t,
      className: Kc(`lucide-${Ty(e)}`, o),
      ...r
    })
  );
  return n.displayName = `${e}`, n;
};
const Uc = Se("Boxes", [
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
const Ko = Se("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const Ry = Se("ChevronDown", [
  ["path", { d: "m6 9 6 6 6-6", key: "qrunsl" }]
]);
const Qr = Se("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const It = Se("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const kn = Se("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const Gc = Se("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const Hy = Se("GripVertical", [
  ["circle", { cx: "9", cy: "12", r: "1", key: "1vctgf" }],
  ["circle", { cx: "9", cy: "5", r: "1", key: "hp0tcf" }],
  ["circle", { cx: "9", cy: "19", r: "1", key: "fkjjf6" }],
  ["circle", { cx: "15", cy: "12", r: "1", key: "1tmaij" }],
  ["circle", { cx: "15", cy: "5", r: "1", key: "19l28e" }],
  ["circle", { cx: "15", cy: "19", r: "1", key: "f4zoj3" }]
]);
const Qc = Se("ListTree", [
  ["path", { d: "M21 12h-8", key: "1bmf0i" }],
  ["path", { d: "M21 6H8", key: "1pqkrb" }],
  ["path", { d: "M21 18h-8", key: "1tm79t" }],
  ["path", { d: "M3 6v4c0 1.1.9 2 2 2h3", key: "1ywdgy" }],
  ["path", { d: "M3 10v6c0 1.1.9 2 2 2h3", key: "2wc746" }]
]);
const $o = Se("Maximize2", [
  ["polyline", { points: "15 3 21 3 21 9", key: "mznyad" }],
  ["polyline", { points: "9 21 3 21 3 15", key: "1avn1i" }],
  ["line", { x1: "21", x2: "14", y1: "3", y2: "10", key: "ota7mn" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const Hs = Se("Minimize2", [
  ["polyline", { points: "4 14 10 14 10 20", key: "11kfnr" }],
  ["polyline", { points: "20 10 14 10 14 4", key: "rlmsce" }],
  ["line", { x1: "14", x2: "21", y1: "10", y2: "3", key: "o5lafz" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const Ei = Se("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const _i = Se("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const Vy = Se("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const Oy = Se("Save", [
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
const By = Se("Search", [
  ["circle", { cx: "11", cy: "11", r: "8", key: "4ej97u" }],
  ["path", { d: "m21 21-4.3-4.3", key: "1qie3q" }]
]);
const Ot = Se("Sparkles", [
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
const Fy = Se("Terminal", [
  ["polyline", { points: "4 17 10 11 4 5", key: "akl6gq" }],
  ["line", { x1: "12", x2: "20", y1: "19", y2: "19", key: "q2wloq" }]
]);
const Jr = Se("Trash2", [
  ["path", { d: "M3 6h18", key: "d0wm0j" }],
  ["path", { d: "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", key: "4alrt4" }],
  ["path", { d: "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", key: "v07s0e" }],
  ["line", { x1: "10", x2: "10", y1: "11", y2: "17", key: "1uufr5" }],
  ["line", { x1: "14", x2: "14", y1: "11", y2: "17", key: "xtxkd" }]
]);
const Wy = Se("X", [
  ["path", { d: "M18 6 6 18", key: "1bl5f8" }],
  ["path", { d: "m6 6 12 12", key: "d8bk6v" }]
]);
const Xy = Se("Zap", [
  [
    "path",
    {
      d: "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z",
      key: "1xq2db"
    }
  ]
]), Ve = "/_elsa/workflow-management";
async function Yy(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${Ve}/definitions?${n.toString()}`);
}
async function qy(e, t) {
  return e.http.getJson(`${Ve}/definitions/${encodeURIComponent(t)}`);
}
async function Zy(e, t) {
  return e.http.postJson(`${Ve}/definitions`, t);
}
async function Ky(e, t) {
  await e.http.deleteJson(`${Ve}/definitions/${encodeURIComponent(t)}`);
}
async function Uy(e, t) {
  await e.http.postJson(`${Ve}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function Gy(e, t) {
  await e.http.deleteJson(`${Ve}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function Qy(e, t) {
  return e.http.putJson(`${Ve}/drafts/${encodeURIComponent(t.id)}`, { state: t.state, layout: t.layout });
}
async function Jy(e, t) {
  return e.http.postJson(`${Ve}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function em(e, t) {
  return e.http.postJson(`${Ve}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function Jc(e, t) {
  return e.http.postJson(`${Ve}/executables/${encodeURIComponent(t)}/run`, {});
}
async function tm(e) {
  return e.http.getJson("/_demo/workflows/executables");
}
async function el(e) {
  return e.http.getJson(`${Ve}/activities`);
}
async function nm(e) {
  const t = await tl(e, [
    `${Ve}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? Vs(t) : Vs(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function om(e) {
  const t = await tl(e, [
    `${Ve}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : vo;
}
async function tl(e, t) {
  let n;
  for (const o of t)
    try {
      return await e.http.getJson(o);
    } catch (r) {
      n = r;
    }
  throw n;
}
function Vs(e) {
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
const vo = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
], Uo = "elsa.sequence.structure", zn = "elsa.flowchart.structure";
function nl(e, t) {
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
function zr(e, t) {
  const n = nl(e, t);
  if (!n) return null;
  let o = Je(n)[0];
  return o ? { owner: n, slot: o } : null;
}
function Je(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = Em(t), r = Lr(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: _m(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, i]) => Lr(i)).map(([i, s]) => ({
    id: `${t.kind}:${i}`,
    label: km(i),
    property: i,
    mode: "generic",
    activities: Lr(s) ?? []
  }));
}
function rm(e, t, n) {
  const o = new Map(t.map((s) => [s.activityVersionId, s])), r = new Map(n.map((s) => [s.nodeId, s])), i = e.slot.activities.map((s, c) => {
    const l = o.get(s.activityVersionId), a = r.get(s.nodeId) ?? Cm(e.slot.mode, c);
    return il(s, l, { x: a.x, y: a.y });
  });
  return {
    nodes: i,
    edges: e.slot.mode === "flowchart" ? mm(e.owner) : ym(e.slot, i)
  };
}
function im(e, t, n) {
  const o = t.find((i) => i.activityVersionId === e.activityVersionId), r = n.find((i) => i.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [il(e, o, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function sm(e, t) {
  return e?.structure?.kind === zn || dm(t) ? "flowchart" : e?.structure?.kind === Uo || fm(t) ? "sequence" : "unsupported";
}
function ei(e, t, n) {
  if (t.length === 0) {
    const c = Je(e)[0];
    return c ? In(e, c, n) : e;
  }
  const [o, ...r] = t, i = Je(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((c) => c.nodeId === o.ownerNodeId ? ei(c, r, n) : c);
  return In(e, i, s);
}
function ol(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, i = Je(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((c) => c.nodeId === o.ownerNodeId ? ol(c, r, n) : c);
  return In(e, i, s);
}
function rl(e, t, n) {
  if (e.nodeId === t) return n(e);
  const o = Je(e);
  if (o.length === 0) return e;
  let r = !1, i = e;
  for (const s of o) {
    const c = s.activities.map((l) => {
      const a = rl(l, t, n);
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
function am(e, t, n, o = []) {
  const r = new Map(e.slot.activities.map((s) => [s.nodeId, s]));
  for (const s of o)
    r.set(s.nodeId, s);
  const i = t.map((s) => r.get(s.id)).filter((s) => !!s);
  return e.slot.mode === "sequence" && i.sort((s, c) => {
    const l = t.find((d) => d.id === s.nodeId), a = t.find((d) => d.id === c.nodeId);
    return (l?.position.x ?? 0) - (a?.position.x ?? 0);
  }), In(e.owner, e.slot, i);
}
function cm(e, t) {
  return {
    ...e,
    structure: gm(e.structure, t)
  };
}
function lm(e, t) {
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
function Os(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: pm(e)
  };
}
function _e(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? hm(t) : n;
}
function il(e, t, n, o = {}) {
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
      icon: ti(t),
      childSlots: Je(e),
      acceptsInbound: xm(e, t),
      sourcePorts: o.suppressFlowPorts ? [] : sl(e, t),
      suppressFlowPorts: o.suppressFlowPorts
    }
  };
}
function ti(e) {
  if (!e) return "activity";
  const t = um(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), o = _e(e).toLowerCase(), r = e.category?.toLowerCase() ?? "", i = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || o === "flowchart" ? "flowchart" : n.endsWith(".sequence") || o === "sequence" ? "sequence" : n.includes("writeline") || o.includes("write line") ? "terminal" : r.includes("runtime") ? "runtime" : i === "trigger" ? "trigger" : "activity";
}
function um(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function dm(e) {
  return !!e && (_e(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function fm(e) {
  return !!e && (_e(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function hm(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function pm(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: Uo,
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
function gm(e, t) {
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
function ym(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function mm(e) {
  if (e.structure?.kind !== zn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, i = n.target;
    if (!r?.nodeId || !i?.nodeId) return null;
    const s = Array.isArray(n.vertices) ? n.vertices.filter(Sm) : [];
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
function sl(e, t) {
  const n = Bs(e.cases);
  if (vm(e, t) && n.length > 0)
    return [...n.map((i) => ({ name: i, displayName: i })), { name: "Default", displayName: "Default" }];
  const o = [
    ...bo(t?.designFacets),
    ...bo(t?.ports),
    ...bo(t?.outputs)
  ];
  if (o.length > 0) return bm(o);
  const r = Bs(e.outcomes);
  return r.length > 0 ? r.map((i) => ({ name: i, displayName: i })) : [{ name: "Done", displayName: "Done" }];
}
function xm(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function To(e, t, n, o) {
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
function wm(e, t, n) {
  const o = To(t.source, n, t.sourceHandle ?? "Done", void 0), r = To(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((i) => i.id !== t.id).concat(o, r);
}
function Lr(e) {
  return Array.isArray(e) ? e.filter(Nm) : null;
}
function vm(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function bo(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!Ci(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...bo(n.ports));
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
function bm(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function Bs(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function Sm(e) {
  return Ci(e) && typeof e.x == "number" && typeof e.y == "number";
}
function Ci(e) {
  return typeof e == "object" && e !== null;
}
function Nm(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Em(e) {
  return e.kind === Uo ? "sequence" : e.kind === zn ? "flowchart" : "generic";
}
function _m(e) {
  return e.kind === Uo || e.kind === zn, "Activities";
}
function Cm(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function km(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Im = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function Mm(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function ki(e) {
  return Mm(e.name);
}
function Am(e, t) {
  const n = ki(t), o = e[n];
  return t.isWrapped === !1 ? o ?? t.defaultValue ?? "" : cl(o, t);
}
function al(e, t) {
  return cl(e[ki(t)], t);
}
function jm(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function Dm(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function Fs(e, t, n) {
  return {
    ...e,
    [ki(t)]: n
  };
}
function Pm(e, t) {
  return t.isWrapped === !1 ? Am(e, t) : al(e, t).expression.value;
}
function cl(e, t) {
  return $m(e) ? {
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
function $m(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
const ll = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]);
function Tm({
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
  const c = Hm(s), l = o.length > 0 ? o : Im;
  return /* @__PURE__ */ u.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ u.jsx("span", { className: "wf-section-label", children: "Properties" }),
    c.map((a) => /* @__PURE__ */ u.jsxs("section", { className: "wf-property-group", children: [
      c.length > 1 ? /* @__PURE__ */ u.jsx("h4", { children: a.category }) : null,
      a.inputs.map((d) => /* @__PURE__ */ u.jsx(
        zm,
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
function zm({
  activity: e,
  input: t,
  editors: n,
  expressionDescriptors: o,
  onChange: r
}) {
  const i = t.isReadOnly === !0, s = { activity: e, expressionDescriptors: o, readOnly: i }, c = Rm(n, t, s), l = c?.component, a = t.isWrapped !== !1 ? al(e, t) : null, d = a?.expression.type ?? "Literal", f = Pm(e, t), h = !!(a && Vm(t, c?.id)), p = !!(a && Om(t, c?.id)), [y, x] = G(!1), v = (b) => {
    const g = a ? jm(a, b) : b;
    r(Fs(e, t, g));
  }, m = (b) => {
    a && r(Fs(e, t, Dm(a, b)));
  };
  return /* @__PURE__ */ u.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ u.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ u.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ u.jsx("span", { children: ul(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ u.jsx("p", { children: t.description }) : null,
    a && !h ? /* @__PURE__ */ u.jsx(
      ni,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: d,
        descriptors: o,
        disabled: i,
        onChange: m
      }
    ) : null,
    h ? /* @__PURE__ */ u.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ u.jsx("div", { className: "wf-expression-editor", children: Ws(l, t, f, i, s, v) }),
      /* @__PURE__ */ u.jsx(
        ni,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: d,
          descriptors: o,
          disabled: i,
          variant: "inline",
          onChange: m
        }
      ),
      p ? /* @__PURE__ */ u.jsx(
        "button",
        {
          type: "button",
          className: "wf-expression-expand-button",
          "aria-label": `Open expanded ${t.displayName || t.name} editor`,
          title: "Open expanded editor",
          onClick: () => x(!0),
          children: /* @__PURE__ */ u.jsx($o, { size: 13 })
        }
      ) : null
    ] }) : Ws(l, t, f, i, s, v),
    p && !h ? /* @__PURE__ */ u.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => x(!0),
        children: [
          /* @__PURE__ */ u.jsx($o, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    y ? /* @__PURE__ */ u.jsx(
      Lm,
      {
        input: t,
        value: f,
        syntax: d,
        descriptors: o,
        disabled: i,
        onChange: v,
        onSyntaxChange: m,
        onClose: () => x(!1)
      }
    ) : null
  ] });
}
function Lm({
  input: e,
  value: t,
  syntax: n,
  descriptors: o,
  disabled: r,
  onChange: i,
  onSyntaxChange: s,
  onClose: c
}) {
  const l = ia(), a = e.displayName || e.name;
  return ae(() => {
    const d = (f) => {
      f.key === "Escape" && c();
    };
    return window.addEventListener("keydown", d), () => window.removeEventListener("keydown", d);
  }, [c]), /* @__PURE__ */ u.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ u.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": l, children: [
    /* @__PURE__ */ u.jsxs("header", { children: [
      /* @__PURE__ */ u.jsxs("div", { children: [
        /* @__PURE__ */ u.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ u.jsx("h3", { id: l, children: a })
      ] }),
      /* @__PURE__ */ u.jsx("button", { type: "button", "aria-label": `Close ${a} editor`, onClick: c, children: /* @__PURE__ */ u.jsx(Wy, { size: 16 }) })
    ] }),
    /* @__PURE__ */ u.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ u.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ u.jsx(
          ni,
          {
            label: `${a} expression syntax`,
            value: n,
            descriptors: o,
            disabled: r,
            onChange: s
          }
        ),
        /* @__PURE__ */ u.jsx("span", { children: ul(e.typeName) })
      ] }),
      e.description ? /* @__PURE__ */ u.jsx("p", { children: e.description }) : null,
      /* @__PURE__ */ u.jsx(
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
    /* @__PURE__ */ u.jsxs("footer", { children: [
      /* @__PURE__ */ u.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ u.jsx("button", { type: "button", onClick: c, children: "Close" })
    ] })
  ] }) });
}
function Ws(e, t, n, o, r, i) {
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
function ni({
  label: e,
  value: t,
  descriptors: n,
  disabled: o,
  variant: r = "block",
  onChange: i
}) {
  const [s, c] = G(!1), l = ia(), a = n.find((f) => f.type === t), d = [
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
function Rm(e, t, n) {
  return [...e].sort((o, r) => (o.order ?? 500) - (r.order ?? 500)).find((o) => o.supports(t, n));
}
function Hm(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "General";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return [...t.entries()].map(([n, o]) => ({ category: n, inputs: o }));
}
function ul(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function Vm(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !ll.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function Om(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !ll.has(t) && n !== "multiline") return !1;
  const o = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(o) || n === "singleline" || n === "multiline";
}
const Bm = { workflowActivity: y0 }, Fm = { workflow: x0 }, Xs = "application/x-elsa-activity-version-id", Wm = 6, Xm = 1200, Ym = [10, 25, 50], qm = 10, Ys = "elsa-studio-workflow-palette-width", qs = "elsa-studio-workflow-inspector-width", Zs = "elsa-studio-workflow-palette-collapsed", Ks = "elsa-studio-workflow-inspector-collapsed", dl = "elsa-studio-workflow-side-panel-maximized", an = 180, cn = 460, Zm = 260, ln = 260, un = 560, Km = 320, Us = 42, ho = 16, fl = ut.createContext(null);
function _0(e) {
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
        component: () => /* @__PURE__ */ u.jsx(Um, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), workflowDesignerPanels: e.workflowDesigner.panels.list() })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ u.jsx(Gm, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow instances",
        component: () => /* @__PURE__ */ u.jsx(Qm, { ai: e.ai })
      }
    ]
  });
}
function Um({
  context: e,
  ai: t,
  propertyEditors: n,
  workflowDesignerPanels: o
}) {
  const [r, i] = G(Gs);
  ae(() => {
    const c = () => i(Gs());
    return window.addEventListener("popstate", c), () => window.removeEventListener("popstate", c);
  }, []);
  const s = (c) => {
    const l = c ? `/workflows/definitions?definition=${encodeURIComponent(c)}` : "/workflows/definitions";
    window.history.pushState({}, "", l), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return r ? /* @__PURE__ */ u.jsx(g0, { context: e, definitionId: r, ai: t, propertyEditors: n, workflowDesignerPanels: o, onBack: () => s(null) }) : /* @__PURE__ */ u.jsx(Ii, { activePath: "/workflows/definitions", title: "Definitions", children: /* @__PURE__ */ u.jsx(Jm, { context: e, ai: t, onOpen: s }) });
}
function Gm({ context: e, ai: t }) {
  const [n, o] = G(Qs);
  return ae(() => {
    const r = () => o(Qs());
    return window.addEventListener("popstate", r), () => window.removeEventListener("popstate", r);
  }, []), /* @__PURE__ */ u.jsx(Ii, { activePath: "/workflows/executables", title: "Executables", children: /* @__PURE__ */ u.jsx(t0, { context: e, ai: t, definitionFilter: n }) });
}
function Qm({ ai: e }) {
  const t = Bt(e, "weaver.workflows.explain-instance");
  return /* @__PURE__ */ u.jsx(Ii, { activePath: "/workflows/instances", title: "Instances", children: /* @__PURE__ */ u.jsxs("div", { className: "wf-empty", children: [
    "Workflow instance history will appear here when the runtime exposes an instance query endpoint.",
    t ? /* @__PURE__ */ u.jsxs("button", { type: "button", className: "wf-ai-inline-action", onClick: () => Ft(e, t, { scope: "workflow-instances" }), children: [
      /* @__PURE__ */ u.jsx(Ot, { size: 13 }),
      " Ask Weaver about instances"
    ] }) : null
  ] }) });
}
function Ii({ activePath: e, title: t, children: n }) {
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
function Gs() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Qs() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Jm({ context: e, ai: t, onOpen: n }) {
  const [o, r] = G(""), [i, s] = G("active"), [c, l] = G(1), [a, d] = G(qm), [f, h] = G("loading"), [p, y] = G(""), [x, v] = G(""), [m, b] = G([]), [g, w] = G(0), [_, E] = G(() => /* @__PURE__ */ new Set()), [k, D] = G(null), [$, W] = G(!1), [M, z] = G([]), [H, S] = G("idle"), I = ce(null), C = ge(() => m.map((R) => R.id), [m]), A = Bt(t, "weaver.workflows.suggest-create-metadata"), T = Bt(t, "weaver.workflows.explain-definition"), P = C.filter((R) => _.has(R)).length, V = C.length > 0 && P === C.length, F = pe(async () => {
    h("loading"), y("");
    try {
      const R = await Yy(e, { search: o, state: i, page: c, pageSize: a }), J = typeof R.totalCount == "number", fe = R.totalCount ?? R.definitions.length, we = hl(fe, a);
      if (fe > 0 && c > we) {
        l(we);
        return;
      }
      b(J ? R.definitions : o0(R.definitions, c, a)), w(fe), h("ready");
    } catch (R) {
      y(R instanceof Error ? R.message : String(R)), h("failed");
    }
  }, [e, o, i, c, a]);
  ae(() => {
    F();
  }, [F]), ae(() => {
    I.current && (I.current.indeterminate = P > 0 && !V);
  }, [V, P]);
  const O = pe(async () => {
    if (!(H === "loading" || H === "ready")) {
      S("loading");
      try {
        const R = await el(e);
        z(R.activities ?? []), S("ready");
      } catch (R) {
        S("failed"), y(R instanceof Error ? R.message : String(R));
      }
    }
  }, [H, e]), U = () => {
    y(""), v(""), D({ name: "", description: "", rootKind: "flowchart" }), O();
  }, Z = async () => {
    if (k?.name.trim()) {
      W(!0), y(""), v("");
      try {
        const R = await Zy(e, {
          name: k.name.trim(),
          description: k.description.trim() || null,
          rootKind: k.rootKind,
          rootActivityVersionId: s0(k, M)
        });
        D(null), n(R.definition.id);
      } catch (R) {
        y(R instanceof Error ? R.message : String(R));
      } finally {
        W(!1);
      }
    }
  }, ne = (R) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(R)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, se = async () => {
    if (m.length === 1 && c > 1) {
      l(c - 1);
      return;
    }
    await F();
  }, K = () => E(/* @__PURE__ */ new Set()), L = (R, J) => {
    E((fe) => {
      const we = new Set(fe);
      return J ? we.add(R) : we.delete(R), we;
    });
  }, Y = (R) => {
    E((J) => {
      const fe = new Set(J);
      for (const we of C)
        R ? fe.add(we) : fe.delete(we);
      return fe;
    });
  }, re = (R) => {
    s(R), l(1), K();
  }, ie = (R) => {
    r(R), l(1), K();
  }, q = async (R) => {
    if (window.confirm(`Delete workflow definition "${R.name}"? You can restore it from the Deleted view.`)) {
      v(""), y("");
      try {
        await Ky(e, R.id), L(R.id, !1), v(`Deleted ${R.name}`), await se();
      } catch (J) {
        y(J instanceof Error ? J.message : String(J));
      }
    }
  }, ee = async (R) => {
    v(""), y("");
    try {
      await Uy(e, R.id), L(R.id, !1), v(`Restored ${R.name}`), await se();
    } catch (J) {
      y(J instanceof Error ? J.message : String(J));
    }
  }, oe = async (R) => {
    if (window.confirm(`Permanently delete workflow definition "${R.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`)) {
      v(""), y("");
      try {
        await Gy(e, R.id), L(R.id, !1), v(`Permanently deleted ${R.name}`), await se();
      } catch (J) {
        y(J instanceof Error ? J.message : String(J));
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
        /* @__PURE__ */ u.jsx(By, { size: 15 }),
        /* @__PURE__ */ u.jsx("input", { value: o, onChange: (R) => ie(R.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ u.jsx("button", { type: "button", onClick: () => {
        F();
      }, children: "Refresh" }),
      /* @__PURE__ */ u.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ u.jsxs("button", { type: "button", title: "Create workflow", onClick: U, children: [
        /* @__PURE__ */ u.jsx(_i, { size: 15 }),
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
      /* @__PURE__ */ u.jsx(Ko, { size: 14 }),
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
    f === "ready" && m.length === 0 ? /* @__PURE__ */ u.jsxs("div", { className: "wf-empty", children: [
      "No ",
      i,
      " workflow definitions found."
    ] }) : null,
    f === "ready" && m.length > 0 ? /* @__PURE__ */ u.jsxs(u.Fragment, { children: [
      /* @__PURE__ */ u.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ u.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ u.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ u.jsx(
            "input",
            {
              ref: I,
              type: "checkbox",
              checked: V,
              onChange: (R) => Y(R.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ u.jsx("span", { children: "Name" }),
          /* @__PURE__ */ u.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ u.jsx("span", { children: i === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ u.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ u.jsx("span", { children: "Actions" })
        ] }),
        m.map((R) => /* @__PURE__ */ u.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${R.name}`,
            "aria-selected": _.has(R.id),
            tabIndex: 0,
            onClick: () => n(R.id),
            onKeyDown: (J) => {
              J.currentTarget === J.target && (J.key !== "Enter" && J.key !== " " || (J.preventDefault(), n(R.id)));
            },
            children: [
              /* @__PURE__ */ u.jsx("label", { className: "wf-row-select", onClick: (J) => J.stopPropagation(), children: /* @__PURE__ */ u.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: _.has(R.id),
                  onChange: (J) => L(R.id, J.target.checked),
                  "aria-label": `Select workflow definition ${R.name}`
                }
              ) }),
              /* @__PURE__ */ u.jsxs("span", { children: [
                /* @__PURE__ */ u.jsx("strong", { children: R.name }),
                /* @__PURE__ */ u.jsx("small", { children: R.description || R.id })
              ] }),
              /* @__PURE__ */ u.jsx("span", { children: R.latestVersion ?? "No version" }),
              /* @__PURE__ */ u.jsx("span", { children: i === "deleted" ? oi(R.deletedAt) : R.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ u.jsx("span", { children: oi(R.lastModifiedAt) }),
              /* @__PURE__ */ u.jsx("span", { className: "wf-row-actions", onClick: (J) => J.stopPropagation(), children: i === "active" ? /* @__PURE__ */ u.jsxs(u.Fragment, { children: [
                /* @__PURE__ */ u.jsx("button", { type: "button", onClick: (J) => {
                  J.stopPropagation(), n(R.id);
                }, children: "Open" }),
                /* @__PURE__ */ u.jsx("button", { type: "button", onClick: (J) => {
                  J.stopPropagation(), ne(R.id);
                }, children: "Artifacts" }),
                T ? /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => Ft(t, T, R), children: [
                  /* @__PURE__ */ u.jsx(Ot, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ u.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  q(R);
                }, children: [
                  /* @__PURE__ */ u.jsx(Jr, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ u.jsxs(u.Fragment, { children: [
                /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => {
                  ee(R);
                }, children: [
                  /* @__PURE__ */ u.jsx(Vy, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ u.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  oe(R);
                }, children: [
                  /* @__PURE__ */ u.jsx(Jr, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          R.id
        ))
      ] }),
      /* @__PURE__ */ u.jsx(
        n0,
        {
          page: c,
          pageSize: a,
          totalCount: g,
          onPageChange: l,
          onPageSizeChange: (R) => {
            d(R), l(1);
          }
        }
      )
    ] }) : null,
    k ? /* @__PURE__ */ u.jsx(
      e0,
      {
        draft: k,
        activities: M,
        catalogState: H,
        creating: $,
        suggestMetadataAction: A,
        onSuggestMetadata: A ? () => Ft(t, A, { draft: k, activities: M }) : void 0,
        onChange: (R) => D(R),
        onClose: () => D(null),
        onSubmit: Z
      }
    ) : null
  ] });
}
function e0({ draft: e, activities: t, catalogState: n, creating: o, suggestMetadataAction: r, onSuggestMetadata: i, onChange: s, onClose: c, onSubmit: l }) {
  const a = ge(() => r0(t), [t]), d = i0(e, t), f = (h) => {
    if (h.startsWith("kind:")) {
      s({ ...e, rootKind: h.slice(5), rootActivityVersionId: null });
      return;
    }
    const p = t.find((y) => y.activityVersionId === h);
    s({
      ...e,
      rootKind: pl(p) ?? e.rootKind,
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
function t0({ context: e, ai: t, definitionFilter: n }) {
  const [o, r] = G("loading"), [i, s] = G(""), [c, l] = G(""), [a, d] = G([]), f = ge(
    () => n ? a.filter((x) => x.definitionId === n || x.sourceId === n) : a,
    [n, a]
  ), h = Bt(t, "weaver.workflows.explain-executable"), p = pe(async () => {
    r("loading"), s("");
    try {
      d(await tm(e)), r("ready");
    } catch (x) {
      s(x instanceof Error ? x.message : String(x)), r("failed");
    }
  }, [e]);
  ae(() => {
    p();
  }, [p]);
  const y = async (x) => {
    l(""), s("");
    try {
      await Jc(e, x.artifactId), l(`Started ${x.artifactId}`);
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
      /* @__PURE__ */ u.jsx(Ko, { size: 14 }),
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
        /* @__PURE__ */ u.jsx("span", { children: c0(x) }),
        /* @__PURE__ */ u.jsx("span", { children: l0(x) }),
        /* @__PURE__ */ u.jsx("span", { children: oi(x.publishedAt ?? x.createdAt) }),
        /* @__PURE__ */ u.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => {
            y(x);
          }, children: [
            /* @__PURE__ */ u.jsx(Ei, { size: 13 }),
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
function n0({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const i = hl(n, t), s = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
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
      /* @__PURE__ */ u.jsx("select", { value: t, onChange: (l) => r(Number(l.target.value)), children: Ym.map((l) => /* @__PURE__ */ u.jsx("option", { value: l, children: l }, l)) })
    ] }),
    /* @__PURE__ */ u.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ u.jsx(Qr, { size: 14 }),
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
function o0(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function hl(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Bt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function Ft(e, t, n) {
  const o = t.createPrompt(n);
  o && e.dispatchPrompt(o);
}
function r0(e) {
  const t = zo(e, "flowchart"), n = zo(e, "sequence"), o = [
    { value: t?.activityVersionId ?? "kind:flowchart", label: "Flowchart" },
    { value: n?.activityVersionId ?? "kind:sequence", label: "Sequence" }
  ], r = /* @__PURE__ */ new Map();
  for (const s of e.filter(xl)) {
    if (a0(s)) continue;
    const c = s.category || "Uncategorized";
    r.set(c, [...r.get(c) ?? [], s]);
  }
  const i = Array.from(r.entries()).sort(([s], [c]) => s.localeCompare(c)).map(([s, c]) => ({
    name: s,
    activities: c.sort((l, a) => _e(l).localeCompare(_e(a)))
  }));
  return { compositeRoots: o, otherCategories: i };
}
function i0(e, t) {
  return e.rootActivityVersionId ?? zo(t, e.rootKind)?.activityVersionId ?? `kind:${e.rootKind}`;
}
function s0(e, t) {
  return e.rootActivityVersionId ?? zo(t, e.rootKind)?.activityVersionId ?? null;
}
function zo(e, t) {
  return e.find((n) => pl(n) === t);
}
function pl(e) {
  return e ? yl(e) ? "flowchart" : ml(e) ? "sequence" : null : null;
}
function gl(e) {
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
function a0(e) {
  return yl(e) || ml(e);
}
function yl(e) {
  return _e(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function ml(e) {
  return _e(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function xl(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function c0(e) {
  return e.sourceKind || e.sourceId || e.sourceVersion ? [e.sourceKind, e.sourceId, e.sourceVersion].filter(Boolean).join(" / ") : e.definitionId;
}
function l0(e) {
  return u0(e.rootActivityType) || e.rootActivityType;
}
function u0(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function d0(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    po(t, n.typeName, n), po(t, n.name, n), po(t, n.displayName, n);
    const o = n.typeName.split(".").filter(Boolean).at(-1);
    po(t, o, n);
  }
  return t;
}
function f0(e, t, n) {
  const o = t.get(e.activityVersionId);
  return n.get(gn(o?.activityTypeKey)) ?? n.get(gn(h0(o?.activityTypeKey))) ?? n.get(gn(o?.displayName)) ?? n.get(gn(e.activityVersionId)) ?? null;
}
function po(e, t, n) {
  const o = gn(t);
  o && !e.has(o) && e.set(o, n);
}
function gn(e) {
  return e?.trim().toLowerCase() ?? "";
}
function h0(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function Js(e, t, n, o) {
  const r = Go();
  if (!r) return t;
  const i = Number(r.getItem(e));
  return Number.isFinite(i) ? So(i, n, o) : t;
}
function ea(e, t) {
  const n = Go();
  if (!n) return t;
  const o = n.getItem(e);
  return o === "true" ? !0 : o === "false" ? !1 : t;
}
function p0() {
  const e = Go();
  if (!e) return null;
  const t = e.getItem(dl);
  return t === "palette" || t === "inspector" ? t : null;
}
function Go() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function dn(e, t) {
  const n = Go();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function So(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function g0({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: o,
  workflowDesignerPanels: r,
  onBack: i
}) {
  const [s, c] = G(null), [l, a] = G(null), [d, f] = G([]), [h, p] = G([]), [y, x] = G(vo), [v, m] = G("loading"), [b, g] = G([]), [w, _] = G([]), [E, k] = G([]), [D, $] = G(null), [W, M] = G(null), [z, H] = G(null), [S, I] = G(null), [C, A] = G(""), [T, P] = G(""), [V, F] = G(!1), [O, U] = G(null), [Z, ne] = G(() => /* @__PURE__ */ new Set()), [se, K] = G(() => Js(Ys, Zm, an, cn)), [L, Y] = G(() => Js(qs, Km, ln, un)), [re, ie] = G(() => ea(Zs, !1)), [q, ee] = G(() => ea(Ks, !1)), [oe, R] = G(p0), [J, fe] = G("activities"), [we, Oe] = G("inspector"), ve = ce(null), De = ce(null), tt = ce(""), Fe = ce(0), Be = ce(null), Ce = ce(!1), Ae = l?.state.rootActivity ?? null, je = ge(() => new Map(d.map((N) => [N.activityVersionId, N])), [d]), Ue = ge(() => d0(h), [h]), Te = ge(() => nl(Ae, b), [Ae, b]), St = sm(Te, Te ? je.get(Te.activityVersionId) : void 0), xe = !!Te && St === "unsupported", We = ge(() => xe ? null : zr(Ae, b), [Ae, b, xe]), Xt = ge(() => gl(d), [d]), Ee = ge(() => xe && Te?.nodeId === W ? Te : We?.slot.activities.find((N) => N.nodeId === W) ?? null, [xe, We, Te, W]), Yt = ge(
    () => Ee ? f0(Ee, je, Ue) : null,
    [je, Ue, Ee]
  ), qt = Ee ? Je(Ee) : [], ke = St === "flowchart" && We?.slot.mode === "flowchart", dt = !Ae || !xe, Ln = Bt(n, "weaver.workflows.find-draft-risks"), Rn = Bt(n, "weaver.workflows.propose-update");
  ae(() => {
    dn(Ys, String(se));
  }, [se]), ae(() => {
    dn(qs, String(L));
  }, [L]), ae(() => {
    dn(Zs, String(re));
  }, [re]), ae(() => {
    dn(Ks, String(q));
  }, [q]), ae(() => {
    dn(dl, oe);
  }, [oe]), ae(() => {
    if (!oe) return;
    const N = (j) => {
      j.key === "Escape" && R(null);
    };
    return window.addEventListener("keydown", N), () => window.removeEventListener("keydown", N);
  }, [oe]);
  const Zt = pe(async () => {
    A(""), m("loading");
    const [N, j, B, X] = await Promise.all([
      qy(e, t),
      el(e),
      nm(e).then(
        (de) => ({ ok: !0, descriptors: de }),
        () => ({ ok: !1, descriptors: [] })
      ),
      om(e).then(
        (de) => ({ ok: !0, descriptors: de }),
        () => ({ ok: !1, descriptors: vo })
      )
    ]), Q = N.draft ?? null;
    c(N), tt.current = Q ? fn(Q) : "", a(Q), f(j.activities ?? []), p(B.descriptors), x(X.descriptors.length > 0 ? X.descriptors : vo), m(B.ok ? "ready" : "failed"), g([]), M(null);
  }, [e, t]);
  ae(() => {
    Zt().catch((N) => A(N instanceof Error ? N.message : String(N)));
  }, [Zt]), ae(() => {
    ne((N) => {
      let j = !1;
      const B = new Set(N);
      for (const X of Xt)
        B.has(X.category) || (B.add(X.category), j = !0);
      return j ? B : N;
    });
  }, [Xt]), ae(() => {
    if (!Te) {
      _([]), k([]);
      return;
    }
    const N = xe ? im(Te, d, l?.layout ?? []) : We ? rm(We, d, l?.layout ?? []) : { nodes: [], edges: [] };
    _(N.nodes), k(N.edges);
  }, [d, l?.layout, xe, We, Te]);
  const Qo = (N) => {
    a((j) => j && { ...j, state: { ...j.state, rootActivity: N } });
  }, Kt = pe((N, j) => {
    if (l?.state.rootActivity && xe)
      return;
    const B = Os(N, oa(N));
    if (!l?.state.rootActivity) {
      Qo(B), M(B.nodeId);
      return;
    }
    if (!We) {
      if (!Je(B)[0]) {
        P(""), A("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      a((Q) => {
        if (!Q?.state.rootActivity) return Q;
        const de = Q.state.rootActivity, le = ei(B, [], [de]), he = j ? [
          ...Q.layout.filter((Pe) => Pe.nodeId !== de.nodeId),
          {
            nodeId: de.nodeId,
            x: Math.round(j.x),
            y: Math.round(j.y)
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
      }), M(l.state.rootActivity.nodeId), A(""), P(`Wrapped root in ${_e(N)}`);
      return;
    }
    a((X) => {
      if (!X?.state.rootActivity) return X;
      const Q = zr(X.state.rootActivity, b);
      if (!Q) return X;
      const de = ei(X.state.rootActivity, b, [...Q.slot.activities, B]), le = j ? [
        ...X.layout.filter((he) => he.nodeId !== B.nodeId),
        {
          nodeId: B.nodeId,
          x: Math.round(j.x),
          y: Math.round(j.y)
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
    }), M(B.nodeId);
  }, [l?.state.rootActivity, b, xe, We]), Nt = pe((N, j) => {
    const B = Os(N, oa(N)), X = {
      id: B.nodeId,
      type: "workflowActivity",
      position: j,
      selected: !0,
      data: {
        label: _e(N),
        activityVersionId: N.activityVersionId,
        activityTypeKey: N.activityTypeKey,
        category: N.category,
        executionType: N.executionType,
        icon: ti(N),
        childSlots: Je(B),
        acceptsInbound: String(N.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: sl(B, N)
      }
    };
    return { activityNode: B, node: X };
  }, []), ze = pe((N, j, B = []) => {
    xe || a((X) => {
      if (!X) return X;
      const Q = lm(X.layout, N), de = X.state.rootActivity;
      if (!de) return { ...X, layout: Q };
      const le = zr(de, b);
      if (!le) return { ...X, layout: Q };
      const he = am(le, N, j, B), Pe = le.slot.mode === "flowchart" ? cm(he, j) : he;
      return {
        ...X,
        layout: Q,
        state: {
          ...X.state,
          rootActivity: ol(de, b, Pe)
        }
      };
    });
  }, [b, xe]), Ut = pe((N, j) => {
    if (!ve.current) return null;
    const B = ve.current.getBoundingClientRect();
    return D ? D.screenToFlowPosition({ x: N, y: j }) : {
      x: N - B.left,
      y: j - B.top
    };
  }, [D]), Gt = pe((N, j) => document.elementFromPoint(N, j)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), Qt = pe((N, j, B) => {
    const X = w.find((Ie) => Ie.id === j.source), Q = w.find((Ie) => Ie.id === j.target), de = X && Q ? b0(X, Q) : X ? ra(X) : B, le = Nt(N, de), Pe = [...w.map((Ie) => Ie.selected ? { ...Ie, selected: !1 } : Ie), le.node], ft = wm(E, j, le.node.id);
    _(Pe), k(ft), M(le.node.id), ze(Pe, ft, [le.activityNode]);
  }, [ze, Nt, E, w]), Et = pe((N, j, B) => {
    if (!dt || !ve.current) return !1;
    const X = ve.current.getBoundingClientRect();
    if (!(j >= X.left && j <= X.right && B >= X.top && B <= X.bottom)) return !1;
    const de = Ut(j, B);
    if (!de) return !1;
    if (ke) {
      const le = Gt(j, B), he = le ? E.find((Pe) => Pe.id === le) : void 0;
      if (he)
        return Qt(N, he, de), !0;
    }
    return Kt(N, de), !0;
  }, [Kt, dt, E, Gt, ke, Qt, Ut]);
  ae(() => {
    const N = (B) => {
      const X = Be.current;
      if (!X) return;
      Math.hypot(B.clientX - X.startX, B.clientY - X.startY) >= Wm && (X.dragging = !0);
    }, j = (B) => {
      const X = Be.current;
      if (Be.current = null, !X?.dragging || !ve.current) return;
      const Q = ve.current.getBoundingClientRect();
      B.clientX >= Q.left && B.clientX <= Q.right && B.clientY >= Q.top && B.clientY <= Q.bottom && (Ce.current = !0, window.setTimeout(() => {
        Ce.current = !1;
      }, 0), Et(X.activity, B.clientX, B.clientY));
    };
    return window.addEventListener("pointermove", N), window.addEventListener("pointerup", j), window.addEventListener("pointercancel", j), () => {
      window.removeEventListener("pointermove", N), window.removeEventListener("pointerup", j), window.removeEventListener("pointercancel", j);
    };
  }, [D, Et]);
  const Jo = (N, j) => {
    N.dataTransfer.setData(Xs, j.activityVersionId), N.dataTransfer.setData("text/plain", j.activityVersionId), N.dataTransfer.effectAllowed = "copy";
  }, er = (N, j) => {
    N.clientX === 0 && N.clientY === 0 || Et(j, N.clientX, N.clientY) && (Ce.current = !0, window.setTimeout(() => {
      Ce.current = !1;
    }, 0));
  }, tr = (N, j) => {
    N.button === 0 && (Be.current = {
      activity: j,
      startX: N.clientX,
      startY: N.clientY,
      dragging: !1
    });
  }, nr = (N) => {
    Ce.current || dt && Kt(N);
  }, Hn = (N) => {
    if (!dt) {
      N.dataTransfer.dropEffect = "none";
      return;
    }
    if (N.preventDefault(), N.dataTransfer.dropEffect = "copy", !ke) return;
    const j = Gt(N.clientX, N.clientY);
    I(j);
  }, Vn = (N) => {
    if (!ve.current) return;
    const j = N.relatedTarget;
    j && ve.current.contains(j) || I(null);
  }, On = (N) => {
    if (N.preventDefault(), I(null), !dt) return;
    const j = N.dataTransfer.getData(Xs) || N.dataTransfer.getData("text/plain"), B = je.get(j);
    B && Et(B, N.clientX, N.clientY);
  }, or = () => {
    if (!ke) return;
    const N = ve.current?.getBoundingClientRect();
    N && H({
      kind: "fromEmpty",
      clientX: N.left + N.width / 2,
      clientY: N.top + N.height / 2
    });
  }, Jt = pe(async (N, j) => {
    const B = ++Fe.current, X = fn(N);
    A("");
    try {
      const Q = await Qy(e, N), de = fn(Q);
      tt.current = de, a((le) => !le || le.id !== Q.id ? le : fn(le) === X ? Q : { ...le, validationErrors: Q.validationErrors }), B === Fe.current && P(j);
    } catch (Q) {
      B === Fe.current && (P(""), A(Q instanceof Error ? Q.message : String(Q)));
    }
  }, [e]);
  ae(() => {
    if (!V || !l || fn(l) === tt.current) return;
    P("Autosaving...");
    const j = window.setTimeout(() => {
      Jt(l, "Autosaved");
    }, Xm);
    return () => window.clearTimeout(j);
  }, [V, l, Jt]);
  const rr = async () => {
    l && (P("Saving..."), await Jt(l, "Saved"));
  }, ir = async () => {
    if (l) {
      P("Promoting...");
      try {
        const N = await Jy(e, l.id), j = await em(e, N.versionId);
        U(j.artifactId), P(`Published ${j.artifactVersion}`), await Zt();
      } catch (N) {
        P(""), A(N instanceof Error ? N.message : String(N));
      }
    }
  }, Bn = async () => {
    if (O) {
      P("Running...");
      try {
        await Jc(e, O), P("Run dispatched");
      } catch (N) {
        P(""), A(N instanceof Error ? N.message : String(N));
      }
    }
  }, Fn = (N) => {
    const j = xe ? N.filter((B) => B.type === "select") : N;
    j.length !== 0 && _((B) => xc(j, B));
  }, Wn = (N) => {
    xe || k((j) => wc(N, j));
  }, en = (N) => !N.source || !N.target || N.source === N.target || !ke ? !1 : !N.targetHandle, sr = (N) => {
    if (!l?.state.rootActivity || !We || !ke || !en(N)) return;
    const j = To(N.source, N.target, N.sourceHandle ?? "Done", N.targetHandle ?? void 0), B = bc(j, E);
    k(B), ze(w, B);
  }, ar = () => {
    ze(w, E);
  }, cr = (N, j) => {
    if (!j.nodeId || j.handleType === "target") {
      De.current = null;
      return;
    }
    De.current = {
      nodeId: j.nodeId,
      handleId: j.handleId ?? null
    };
  }, lr = (N) => {
    const j = De.current;
    if (De.current = null, !j || !ke || N.target?.closest(".react-flow__handle, .react-flow__node")) return;
    const X = S0(N);
    H({
      kind: "fromPort",
      sourceNodeId: j.nodeId,
      sourceHandleId: j.handleId,
      clientX: X.x,
      clientY: X.y
    });
  }, ur = (N, j) => {
    if (!ke || !en(j)) return;
    const B = zp(N, {
      ...j,
      sourceHandle: j.sourceHandle ?? "Done",
      targetHandle: j.targetHandle ?? void 0
    }, E, { shouldReplaceId: !1 });
    k(B), ze(w, B);
  }, Xn = (N) => {
    if (xe || N.length === 0) return;
    const j = new Set(N.map((Q) => Q.id)), B = w.filter((Q) => !j.has(Q.id)), X = E.filter((Q) => !j.has(Q.source) && !j.has(Q.target));
    _(B), k(X), W && j.has(W) && M(null), ze(B, X);
  }, dr = (N) => {
    if (xe || N.length === 0) return;
    const j = new Set(N.map((X) => X.id)), B = E.filter((X) => !j.has(X.id));
    k(B), ze(w, B);
  }, Yn = pe((N) => {
    if (xe) return;
    const j = E.filter((B) => B.id !== N);
    k(j), ze(w, j);
  }, [ze, E, xe, w]), qn = pe((N, j, B) => {
    ke && H({ kind: "spliceEdge", edgeId: N, clientX: j, clientY: B });
  }, [ke]), fr = (N) => {
    const j = z;
    if (!j) return;
    H(null);
    const B = Ut(j.clientX, j.clientY) ?? { x: 0, y: 0 };
    if (j.kind === "fromEmpty") {
      const Q = Nt(N, B), le = [...w.map((he) => he.selected ? { ...he, selected: !1 } : he), Q.node];
      _(le), M(Q.node.id), ze(le, E, [Q.activityNode]);
      return;
    }
    if (j.kind === "fromPort") {
      const Q = w.find((Ie) => Ie.id === j.sourceNodeId), de = Q ? ra(Q) : B, le = Nt(N, de), Pe = [...w.map((Ie) => Ie.selected ? { ...Ie, selected: !1 } : Ie), le.node], ft = [...E, To(j.sourceNodeId, le.node.id, j.sourceHandleId ?? "Done")];
      _(Pe), k(ft), M(le.node.id), ze(Pe, ft, [le.activityNode]);
      return;
    }
    const X = E.find((Q) => Q.id === j.edgeId);
    X && Qt(N, X, B);
  }, hr = ge(() => ({
    highlightedEdgeId: S,
    deleteEdge: Yn,
    requestInsertActivity: qn
  }), [Yn, S, qn]), pr = (N, j, B) => {
    g((X) => [...X, { ownerNodeId: N.nodeId, slotId: j, label: B }]), M(null);
  }, gr = pe((N) => {
    a((j) => {
      const B = j?.state.rootActivity;
      return !j || !B ? j : {
        ...j,
        state: {
          ...j.state,
          rootActivity: rl(B, N.nodeId, () => N)
        }
      };
    });
  }, []), yr = (N) => {
    ne((j) => {
      const B = new Set(j);
      return B.has(N) ? B.delete(N) : B.add(N), B;
    });
  }, tn = (N) => {
    R((j) => j === N ? null : j), N === "palette" ? ie((j) => !j) : ee((j) => !j);
  }, Zn = (N) => {
    N === "palette" ? ie(!1) : ee(!1), R((j) => j === N ? null : N);
  }, Kn = (N, j) => {
    R(null), N === "palette" ? (ie(!1), K((B) => So(B + j, an, cn))) : (ee(!1), Y((B) => So(B + j, ln, un)));
  }, Un = (N, j) => {
    j.preventDefault(), R(null), N === "palette" ? ie(!1) : ee(!1);
    const B = j.clientX, X = N === "palette" ? se : L, Q = N === "palette" ? an : ln, de = N === "palette" ? cn : un;
    document.body.classList.add("wf-side-panel-resizing");
    const le = (Pe) => {
      const ft = N === "palette" ? Pe.clientX - B : B - Pe.clientX, Ie = So(X + ft, Q, de);
      N === "palette" ? K(Ie) : Y(Ie);
    }, he = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", le), window.removeEventListener("pointerup", he), window.removeEventListener("pointercancel", he);
    };
    window.addEventListener("pointermove", le), window.addEventListener("pointerup", he), window.addEventListener("pointercancel", he);
  }, Gn = (N, j) => {
    j.key === "ArrowLeft" ? (j.preventDefault(), Kn(N, N === "palette" ? -ho : ho)) : j.key === "ArrowRight" ? (j.preventDefault(), Kn(N, N === "palette" ? ho : -ho)) : j.key === "Home" ? (j.preventDefault(), N === "palette" ? K(an) : Y(ln)) : j.key === "End" && (j.preventDefault(), N === "palette" ? K(cn) : Y(un));
  };
  if (!s || !l)
    return /* @__PURE__ */ u.jsx("div", { className: "wf-empty", children: C || "Loading workflow editor..." });
  const mr = [
    "wf-editor-body",
    re ? "palette-collapsed" : "",
    q ? "inspector-collapsed" : "",
    oe === "palette" ? "palette-maximized" : "",
    oe === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), xr = {
    "--wf-palette-width": `${re ? Us : se}px`,
    "--wf-inspector-width": `${q ? Us : L}px`
  }, Qn = !re && oe !== "inspector", Jn = !q && oe !== "palette", eo = {
    definition: s.definition,
    draft: l,
    selectedActivity: Ee,
    selectedActivityDescriptor: Yt,
    selectedActivitySlots: qt,
    catalog: d,
    currentScopeOwner: Te,
    frames: b
  }, to = r.map((N) => {
    const j = N.component;
    return {
      id: N.id,
      title: N.title,
      side: N.side,
      order: N.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ u.jsx(j, { context: eo })
    };
  }), _t = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ u.jsx(Uc, { size: 15 }),
      render: wr
    },
    ...to.filter((N) => N.side === "left")
  ].sort(na), nn = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ u.jsx(Qc, { size: 15 }),
      render: vr
    },
    ...to.filter((N) => N.side === "right")
  ].sort(na), no = _t.find((N) => N.id === J) ?? _t[0], Ct = nn.find((N) => N.id === we) ?? nn[0];
  function wr() {
    return /* @__PURE__ */ u.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: Xt.map((N) => {
      const j = Z.has(N.category);
      return /* @__PURE__ */ u.jsxs("div", { className: "wf-palette-category", children: [
        /* @__PURE__ */ u.jsxs(
          "button",
          {
            type: "button",
            className: "wf-palette-category-toggle",
            role: "treeitem",
            "aria-expanded": j,
            onClick: () => yr(N.category),
            children: [
              j ? /* @__PURE__ */ u.jsx(Ry, { size: 14 }) : /* @__PURE__ */ u.jsx(It, { size: 14 }),
              /* @__PURE__ */ u.jsx("span", { children: N.category }),
              /* @__PURE__ */ u.jsx("small", { children: N.activities.length })
            ]
          }
        ),
        j ? /* @__PURE__ */ u.jsx("div", { className: "wf-palette-activities", role: "group", children: N.activities.map((B) => {
          const X = B.description?.trim(), Q = X ? `wf-palette-description-${B.activityVersionId}` : void 0, de = _e(B), le = ti(B);
          return /* @__PURE__ */ u.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-activity",
              role: "treeitem",
              draggable: !0,
              title: X || _e(B),
              "aria-describedby": Q,
              onClick: () => nr(B),
              onDragStart: (he) => Jo(he, B),
              onDragEnd: (he) => er(he, B),
              onPointerDown: (he) => tr(he, B),
              children: [
                /* @__PURE__ */ u.jsx("span", { className: "wf-activity-icon", "data-icon": le, "aria-hidden": "true", children: wl(le) }),
                /* @__PURE__ */ u.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ u.jsx("strong", { children: de }),
                  X ? /* @__PURE__ */ u.jsx("small", { id: Q, children: X }) : null
                ] }),
                /* @__PURE__ */ u.jsx(Hy, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            B.activityVersionId
          );
        }) }) : null
      ] }, N.category);
    }) });
  }
  function vr() {
    return Ee ? /* @__PURE__ */ u.jsxs("div", { className: "wf-inspector-content", children: [
      /* @__PURE__ */ u.jsx("h3", { children: w.find((N) => N.id === Ee.nodeId)?.data.label ?? Ee.nodeId }),
      /* @__PURE__ */ u.jsxs("dl", { children: [
        /* @__PURE__ */ u.jsx("dt", { children: "Node ID" }),
        /* @__PURE__ */ u.jsx("dd", { children: Ee.nodeId }),
        /* @__PURE__ */ u.jsx("dt", { children: "Activity type" }),
        /* @__PURE__ */ u.jsx("dd", { children: Yt?.typeName ?? je.get(Ee.activityVersionId)?.activityTypeKey ?? "Unknown" }),
        /* @__PURE__ */ u.jsx("dt", { children: "Activity version" }),
        /* @__PURE__ */ u.jsx("dd", { children: Ee.activityVersionId })
      ] }),
      /* @__PURE__ */ u.jsx(
        Tm,
        {
          activity: Ee,
          descriptor: Yt,
          editors: o,
          expressionDescriptors: y,
          descriptorStatus: v,
          onChange: gr
        }
      ),
      qt.length > 0 ? /* @__PURE__ */ u.jsxs("div", { className: "wf-slot-list", children: [
        /* @__PURE__ */ u.jsx("span", { children: "Embedded slots" }),
        qt.map((N) => /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => pr(Ee, N.id, `${w.find((j) => j.id === Ee.nodeId)?.data.label ?? Ee.nodeId} / ${N.label}`), children: [
          N.label,
          /* @__PURE__ */ u.jsxs("small", { children: [
            N.activities.length,
            " activit",
            N.activities.length === 1 ? "y" : "ies"
          ] })
        ] }, N.id))
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
        /* @__PURE__ */ u.jsx(Ko, { size: 13 }),
        " ",
        T
      ] }) : null,
      /* @__PURE__ */ u.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ u.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ u.jsx("input", { type: "checkbox", checked: V, onChange: (N) => F(N.target.checked) }),
          /* @__PURE__ */ u.jsx("span", { children: "Autosave" })
        ] }),
        Ln ? /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => Ft(n, Ln, { definition: s.definition, draft: l }), children: [
          /* @__PURE__ */ u.jsx(Ot, { size: 15 }),
          " Risks"
        ] }) : null,
        Rn ? /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => Ft(n, Rn, { definition: s.definition, draft: l }), children: [
          /* @__PURE__ */ u.jsx(Ot, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => {
          rr();
        }, children: [
          /* @__PURE__ */ u.jsx(Oy, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => {
          ir();
        }, children: [
          /* @__PURE__ */ u.jsx(Gc, { size: 15 }),
          " Promote"
        ] }),
        /* @__PURE__ */ u.jsxs("button", { type: "button", disabled: !O, onClick: () => {
          Bn();
        }, children: [
          /* @__PURE__ */ u.jsx(Ei, { size: 15 }),
          " Run"
        ] })
      ] })
    ] }),
    C ? /* @__PURE__ */ u.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ u.jsx(kn, { size: 16 }),
      " ",
      C
    ] }) : null,
    /* @__PURE__ */ u.jsxs("div", { className: mr, style: xr, children: [
      /* @__PURE__ */ u.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ u.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ u.jsx(
            ta,
            {
              label: "Activities panel tabs",
              tabs: _t,
              activeTabId: no.id,
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
                children: re ? /* @__PURE__ */ u.jsx(It, { size: 14 }) : /* @__PURE__ */ u.jsx(Qr, { size: 14 })
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
                children: oe === "palette" ? /* @__PURE__ */ u.jsx(Hs, { size: 14 }) : /* @__PURE__ */ u.jsx($o, { size: 14 })
              }
            )
          ] })
        ] }),
        Qn ? no.render() : null
      ] }),
      Qn && !oe ? /* @__PURE__ */ u.jsx(
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
          onPointerDown: (N) => Un("palette", N),
          onKeyDown: (N) => Gn("palette", N)
        }
      ) : /* @__PURE__ */ u.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ u.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ u.jsxs("div", { className: "wf-breadcrumb", children: [
          /* @__PURE__ */ u.jsx("button", { type: "button", onClick: () => {
            g([]), M(null);
          }, children: "Root" }),
          b.map((N, j) => /* @__PURE__ */ u.jsxs(ut.Fragment, { children: [
            /* @__PURE__ */ u.jsx(It, { size: 13 }),
            /* @__PURE__ */ u.jsx("button", { type: "button", onClick: () => {
              g(b.slice(0, j + 1)), M(null);
            }, children: N.label })
          ] }, `${N.ownerNodeId}-${N.slotId}-${j}`))
        ] }),
        /* @__PURE__ */ u.jsxs("div", { className: "wf-canvas", ref: ve, onDragOver: Hn, onDragLeave: Vn, onDrop: On, children: [
          /* @__PURE__ */ u.jsx(fl.Provider, { value: hr, children: /* @__PURE__ */ u.jsxs(
            ry,
            {
              nodes: w,
              edges: E,
              nodeTypes: Bm,
              edgeTypes: Fm,
              onInit: $,
              onNodesChange: Fn,
              onEdgesChange: Wn,
              onNodesDelete: Xn,
              onEdgesDelete: dr,
              onConnect: sr,
              onConnectStart: ke ? cr : void 0,
              onConnectEnd: ke ? lr : void 0,
              onReconnect: ke ? ur : void 0,
              isValidConnection: en,
              onDragOver: Hn,
              onDragLeave: Vn,
              onDrop: On,
              onPaneClick: () => M(null),
              onNodeClick: (N, j) => M(j.id),
              onNodeDragStop: xe ? void 0 : ar,
              fitView: !0,
              minZoom: 0.2,
              maxZoom: 1.8,
              nodesConnectable: ke,
              nodesDraggable: !xe,
              selectionOnDrag: !0,
              multiSelectionKeyCode: ["Shift", "Meta", "Control"],
              deleteKeyCode: xe ? null : ["Backspace", "Delete"],
              panActivationKeyCode: null,
              defaultEdgeOptions: { type: "workflow" },
              children: [
                /* @__PURE__ */ u.jsx(dy, { gap: 18, size: 1 }),
                /* @__PURE__ */ u.jsx(xy, {}),
                /* @__PURE__ */ u.jsx(jy, { pannable: !0, zoomable: !0 })
              ]
            }
          ) }),
          ke && w.length === 0 ? /* @__PURE__ */ u.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => or(), children: [
            /* @__PURE__ */ u.jsx(_i, { size: 15 }),
            " Add activity"
          ] }) : null,
          z ? /* @__PURE__ */ u.jsx(
            w0,
            {
              clientX: z.clientX,
              clientY: z.clientY,
              activities: d,
              onPick: fr,
              onClose: () => H(null)
            }
          ) : null
        ] }),
        /* @__PURE__ */ u.jsx(v0, { draft: l })
      ] }),
      Jn && !oe ? /* @__PURE__ */ u.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": ln,
          "aria-valuemax": un,
          "aria-valuenow": L,
          tabIndex: 0,
          onPointerDown: (N) => Un("inspector", N),
          onKeyDown: (N) => Gn("inspector", N)
        }
      ) : /* @__PURE__ */ u.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ u.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ u.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ u.jsx(
            ta,
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
                children: q ? /* @__PURE__ */ u.jsx(Qr, { size: 14 }) : /* @__PURE__ */ u.jsx(It, { size: 14 })
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
                children: oe === "inspector" ? /* @__PURE__ */ u.jsx(Hs, { size: 14 }) : /* @__PURE__ */ u.jsx($o, { size: 14 })
              }
            )
          ] })
        ] }),
        Jn ? Ct.render() : null
      ] })
    ] })
  ] });
}
function ta({
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
function na(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
function y0({ data: e, selected: t }) {
  const n = e, o = !n.suppressFlowPorts, r = o ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], i = m0(n);
  return /* @__PURE__ */ u.jsxs("div", { className: t ? "wf-node selected" : "wf-node", "data-icon": n.icon ?? "activity", children: [
    o && n.acceptsInbound ? /* @__PURE__ */ u.jsx(Vt, { type: "target", position: te.Left }) : null,
    /* @__PURE__ */ u.jsxs("div", { className: "wf-node-content", children: [
      /* @__PURE__ */ u.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: wl(n.icon) }),
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
function m0(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((r) => !!r).join(" · ");
}
function wl(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ u.jsx(Gc, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ u.jsx(Qc, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ u.jsx(Fy, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ u.jsx(Ei, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ u.jsx(Xy, { size: 15 });
    default:
      return /* @__PURE__ */ u.jsx(Uc, { size: 15 });
  }
}
function x0(e) {
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
  } = e, h = ut.useContext(fl), [p, y] = G(!1), [x, v, m] = Do({ sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: c }), b = h?.highlightedEdgeId === t;
  return /* @__PURE__ */ u.jsxs(u.Fragment, { children: [
    /* @__PURE__ */ u.jsx(
      Tn,
      {
        id: t,
        path: x,
        markerEnd: l,
        style: {
          ...a,
          strokeWidth: b ? 2.5 : a?.strokeWidth
        },
        label: d,
        labelX: v,
        labelY: m,
        labelStyle: f,
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1)
      }
    ),
    h ? /* @__PURE__ */ u.jsx(sy, { children: /* @__PURE__ */ u.jsxs(
      "div",
      {
        className: ["wf-edge-actions", p ? "visible" : "", b ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${v}px, ${m}px)` },
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1),
        children: [
          /* @__PURE__ */ u.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (g) => h.requestInsertActivity(t, g.clientX, g.clientY), children: /* @__PURE__ */ u.jsx(_i, { size: 12 }) }),
          /* @__PURE__ */ u.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => h.deleteEdge(t), children: /* @__PURE__ */ u.jsx(Jr, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function w0({ clientX: e, clientY: t, activities: n, onPick: o, onClose: r }) {
  const [i, s] = G(""), [c, l] = G(0), a = ce(null), d = ce(null), f = ge(() => {
    const b = i.trim().toLowerCase(), g = n.filter(xl);
    return b ? g.filter((w) => _e(w).toLowerCase().includes(b) || w.activityTypeKey.toLowerCase().includes(b) || (w.category ?? "").toLowerCase().includes(b) || (w.description ?? "").toLowerCase().includes(b)) : g;
  }, [n, i]), h = ge(() => gl(f), [f]), p = ge(() => h.flatMap((b) => b.activities), [h]);
  ae(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), ae(() => {
    const b = (w) => {
      a.current?.contains(w.target) || r();
    }, g = (w) => {
      w.key === "Escape" && r();
    };
    return document.addEventListener("mousedown", b, !0), document.addEventListener("keydown", g), () => {
      document.removeEventListener("mousedown", b, !0), document.removeEventListener("keydown", g);
    };
  }, [r]);
  const y = (b) => {
    if (b.key === "ArrowDown")
      b.preventDefault(), l((g) => Math.min(g + 1, p.length - 1));
    else if (b.key === "ArrowUp")
      b.preventDefault(), l((g) => Math.max(g - 1, 0));
    else if (b.key === "Enter") {
      b.preventDefault();
      const g = p[c];
      g && o(g);
    }
  }, x = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), v = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let m = -1;
  return /* @__PURE__ */ u.jsxs("div", { ref: a, className: "wf-connect-menu", style: { left: x, top: v }, onMouseDown: (b) => b.stopPropagation(), onClick: (b) => b.stopPropagation(), children: [
    /* @__PURE__ */ u.jsx(
      "input",
      {
        ref: d,
        type: "search",
        value: i,
        placeholder: "Search activities...",
        "aria-label": "Search activities",
        onChange: (b) => {
          s(b.target.value), l(0);
        },
        onKeyDown: y
      }
    ),
    /* @__PURE__ */ u.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: h.length === 0 ? /* @__PURE__ */ u.jsx("p", { children: "No matching activities." }) : h.map((b) => /* @__PURE__ */ u.jsxs("section", { children: [
      /* @__PURE__ */ u.jsx("h4", { children: b.category }),
      b.activities.map((g) => {
        m += 1;
        const w = m, _ = w === c;
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
    ] }, b.category)) })
  ] });
}
function v0({ draft: e }) {
  return e.validationErrors.length ? /* @__PURE__ */ u.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ u.jsx(kn, { size: 14 }),
    e.validationErrors.length,
    " validation issue",
    e.validationErrors.length === 1 ? "" : "s"
  ] }) : /* @__PURE__ */ u.jsxs("div", { className: "wf-validation ok", children: [
    /* @__PURE__ */ u.jsx(Ko, { size: 14 }),
    " No validation errors"
  ] });
}
function oa(e) {
  return `${_e(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function ra(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function b0(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function S0(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function fn(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function oi(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
export {
  _0 as register
};
