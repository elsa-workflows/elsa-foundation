import dt, { memo as ve, forwardRef as Lo, useRef as ce, useEffect as se, useCallback as ge, useContext as zn, useMemo as ye, useState as U, createContext as ai, useLayoutEffect as jl, createElement as Br, useId as fa } from "react";
import "@tanstack/react-query";
function Al(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var Cr = { exports: {} }, hn = {};
var $i;
function Ml() {
  if ($i) return hn;
  $i = 1;
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
  return hn.Fragment = t, hn.jsx = n, hn.jsxs = n, hn;
}
var zi;
function Dl() {
  return zi || (zi = 1, Cr.exports = Ml()), Cr.exports;
}
var u = Dl();
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
var Pl = { value: () => {
} };
function Ho() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new go(n);
}
function go(e) {
  this._ = e;
}
function Tl(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
go.prototype = Ho.prototype = {
  constructor: go,
  on: function(e, t) {
    var n = this._, o = Tl(e + "", n), r, i = -1, s = o.length;
    if (arguments.length < 2) {
      for (; ++i < s; ) if ((r = (e = o[i]).type) && (r = $l(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++i < s; )
      if (r = (e = o[i]).type) n[r] = Ri(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = Ri(n[r], e.name, null);
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
function $l(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function Ri(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = Pl, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var Fr = "http://www.w3.org/1999/xhtml";
const Li = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: Fr,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function Vo(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Li.hasOwnProperty(t) ? { space: Li[t], local: e } : e;
}
function zl(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === Fr && t.documentElement.namespaceURI === Fr ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Rl(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function ha(e) {
  var t = Vo(e);
  return (t.local ? Rl : zl)(t);
}
function Ll() {
}
function ci(e) {
  return e == null ? Ll : function() {
    return this.querySelector(e);
  };
}
function Hl(e) {
  typeof e != "function" && (e = ci(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = new Array(s), l, a, d = 0; d < s; ++d)
      (l = i[d]) && (a = e.call(l, l.__data__, d, i)) && ("__data__" in l && (a.__data__ = l.__data__), c[d] = a);
  return new Le(o, this._parents);
}
function Vl(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function Ol() {
  return [];
}
function pa(e) {
  return e == null ? Ol : function() {
    return this.querySelectorAll(e);
  };
}
function Bl(e) {
  return function() {
    return Vl(e.apply(this, arguments));
  };
}
function Fl(e) {
  typeof e == "function" ? e = Bl(e) : e = pa(e);
  for (var t = this._groups, n = t.length, o = [], r = [], i = 0; i < n; ++i)
    for (var s = t[i], c = s.length, l, a = 0; a < c; ++a)
      (l = s[a]) && (o.push(e.call(l, l.__data__, a, s)), r.push(l));
  return new Le(o, r);
}
function ga(e) {
  return function() {
    return this.matches(e);
  };
}
function ya(e) {
  return function(t) {
    return t.matches(e);
  };
}
var Wl = Array.prototype.find;
function Xl(e) {
  return function() {
    return Wl.call(this.children, e);
  };
}
function ql() {
  return this.firstElementChild;
}
function Yl(e) {
  return this.select(e == null ? ql : Xl(typeof e == "function" ? e : ya(e)));
}
var Zl = Array.prototype.filter;
function Kl() {
  return Array.from(this.children);
}
function Ul(e) {
  return function() {
    return Zl.call(this.children, e);
  };
}
function Gl(e) {
  return this.selectAll(e == null ? Kl : Ul(typeof e == "function" ? e : ya(e)));
}
function Ql(e) {
  typeof e != "function" && (e = ga(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = [], l, a = 0; a < s; ++a)
      (l = i[a]) && e.call(l, l.__data__, a, i) && c.push(l);
  return new Le(o, this._parents);
}
function ma(e) {
  return new Array(e.length);
}
function Jl() {
  return new Le(this._enter || this._groups.map(ma), this._parents);
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
function eu(e) {
  return function() {
    return e;
  };
}
function tu(e, t, n, o, r, i) {
  for (var s = 0, c, l = t.length, a = i.length; s < a; ++s)
    (c = t[s]) ? (c.__data__ = i[s], o[s] = c) : n[s] = new No(e, i[s]);
  for (; s < l; ++s)
    (c = t[s]) && (r[s] = c);
}
function nu(e, t, n, o, r, i, s) {
  var c, l, a = /* @__PURE__ */ new Map(), d = t.length, f = i.length, h = new Array(d), p;
  for (c = 0; c < d; ++c)
    (l = t[c]) && (h[c] = p = s.call(l, l.__data__, c, t) + "", a.has(p) ? r[c] = l : a.set(p, l));
  for (c = 0; c < f; ++c)
    p = s.call(e, i[c], c, i) + "", (l = a.get(p)) ? (o[c] = l, l.__data__ = i[c], a.delete(p)) : n[c] = new No(e, i[c]);
  for (c = 0; c < d; ++c)
    (l = t[c]) && a.get(h[c]) === l && (r[c] = l);
}
function ou(e) {
  return e.__data__;
}
function ru(e, t) {
  if (!arguments.length) return Array.from(this, ou);
  var n = t ? nu : tu, o = this._parents, r = this._groups;
  typeof e != "function" && (e = eu(e));
  for (var i = r.length, s = new Array(i), c = new Array(i), l = new Array(i), a = 0; a < i; ++a) {
    var d = o[a], f = r[a], h = f.length, p = iu(e.call(d, d && d.__data__, a, o)), y = p.length, x = c[a] = new Array(y), v = s[a] = new Array(y), m = l[a] = new Array(h);
    n(d, f, x, v, m, p, t);
    for (var b = 0, g = 0, w, C; b < y; ++b)
      if (w = x[b]) {
        for (b >= g && (g = b + 1); !(C = v[g]) && ++g < y; ) ;
        w._next = C || null;
      }
  }
  return s = new Le(s, o), s._enter = c, s._exit = l, s;
}
function iu(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function su() {
  return new Le(this._exit || this._groups.map(ma), this._parents);
}
function au(e, t, n) {
  var o = this.enter(), r = this, i = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? i.remove() : n(i), o && r ? o.merge(r).order() : r;
}
function cu(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, i = o.length, s = Math.min(r, i), c = new Array(r), l = 0; l < s; ++l)
    for (var a = n[l], d = o[l], f = a.length, h = c[l] = new Array(f), p, y = 0; y < f; ++y)
      (p = a[y] || d[y]) && (h[y] = p);
  for (; l < r; ++l)
    c[l] = n[l];
  return new Le(c, this._parents);
}
function lu() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, i = o[r], s; --r >= 0; )
      (s = o[r]) && (i && s.compareDocumentPosition(i) ^ 4 && i.parentNode.insertBefore(s, i), i = s);
  return this;
}
function uu(e) {
  e || (e = du);
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
function du(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function fu() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function hu() {
  return Array.from(this);
}
function pu() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length; r < i; ++r) {
      var s = o[r];
      if (s) return s;
    }
  return null;
}
function gu() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function yu() {
  return !this.node();
}
function mu(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], i = 0, s = r.length, c; i < s; ++i)
      (c = r[i]) && e.call(c, c.__data__, i, r);
  return this;
}
function xu(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function wu(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function vu(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function bu(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function Su(e, t) {
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
function Eu(e, t) {
  var n = Vo(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? wu : xu : typeof t == "function" ? n.local ? Nu : Su : n.local ? bu : vu)(n, t));
}
function xa(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function Cu(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function _u(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function ku(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function Iu(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? Cu : typeof t == "function" ? ku : _u)(e, t, n ?? "")) : Rt(this.node(), e);
}
function Rt(e, t) {
  return e.style.getPropertyValue(t) || xa(e).getComputedStyle(e, null).getPropertyValue(t);
}
function ju(e) {
  return function() {
    delete this[e];
  };
}
function Au(e, t) {
  return function() {
    this[e] = t;
  };
}
function Mu(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function Du(e, t) {
  return arguments.length > 1 ? this.each((t == null ? ju : typeof t == "function" ? Mu : Au)(e, t)) : this.node()[e];
}
function wa(e) {
  return e.trim().split(/^|\s+/);
}
function li(e) {
  return e.classList || new va(e);
}
function va(e) {
  this._node = e, this._names = wa(e.getAttribute("class") || "");
}
va.prototype = {
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
function ba(e, t) {
  for (var n = li(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function Sa(e, t) {
  for (var n = li(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function Pu(e) {
  return function() {
    ba(this, e);
  };
}
function Tu(e) {
  return function() {
    Sa(this, e);
  };
}
function $u(e, t) {
  return function() {
    (t.apply(this, arguments) ? ba : Sa)(this, e);
  };
}
function zu(e, t) {
  var n = wa(e + "");
  if (arguments.length < 2) {
    for (var o = li(this.node()), r = -1, i = n.length; ++r < i; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? $u : t ? Pu : Tu)(n, t));
}
function Ru() {
  this.textContent = "";
}
function Lu(e) {
  return function() {
    this.textContent = e;
  };
}
function Hu(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function Vu(e) {
  return arguments.length ? this.each(e == null ? Ru : (typeof e == "function" ? Hu : Lu)(e)) : this.node().textContent;
}
function Ou() {
  this.innerHTML = "";
}
function Bu(e) {
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
function Wu(e) {
  return arguments.length ? this.each(e == null ? Ou : (typeof e == "function" ? Fu : Bu)(e)) : this.node().innerHTML;
}
function Xu() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function qu() {
  return this.each(Xu);
}
function Yu() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Zu() {
  return this.each(Yu);
}
function Ku(e) {
  var t = typeof e == "function" ? e : ha(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Uu() {
  return null;
}
function Gu(e, t) {
  var n = typeof e == "function" ? e : ha(e), o = t == null ? Uu : typeof t == "function" ? t : ci(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function Qu() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Ju() {
  return this.each(Qu);
}
function ed() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function td() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function nd(e) {
  return this.select(e ? td : ed);
}
function od(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function rd(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function id(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function sd(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, i; n < r; ++n)
        i = t[n], (!e.type || i.type === e.type) && i.name === e.name ? this.removeEventListener(i.type, i.listener, i.options) : t[++o] = i;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function ad(e, t, n) {
  return function() {
    var o = this.__on, r, i = rd(t);
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
function cd(e, t, n) {
  var o = id(e + ""), r, i = o.length, s;
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
  for (c = t ? ad : sd, r = 0; r < i; ++r) this.each(c(o[r], t, n));
  return this;
}
function Na(e, t, n) {
  var o = xa(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function ld(e, t) {
  return function() {
    return Na(this, e, t);
  };
}
function ud(e, t) {
  return function() {
    return Na(this, e, t.apply(this, arguments));
  };
}
function dd(e, t) {
  return this.each((typeof t == "function" ? ud : ld)(e, t));
}
function* fd() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length, s; r < i; ++r)
      (s = o[r]) && (yield s);
}
var Ea = [null];
function Le(e, t) {
  this._groups = e, this._parents = t;
}
function Rn() {
  return new Le([[document.documentElement]], Ea);
}
function hd() {
  return this;
}
Le.prototype = Rn.prototype = {
  constructor: Le,
  select: Hl,
  selectAll: Fl,
  selectChild: Yl,
  selectChildren: Gl,
  filter: Ql,
  data: ru,
  enter: Jl,
  exit: su,
  join: au,
  merge: cu,
  selection: hd,
  order: lu,
  sort: uu,
  call: fu,
  nodes: hu,
  node: pu,
  size: gu,
  empty: yu,
  each: mu,
  attr: Eu,
  style: Iu,
  property: Du,
  classed: zu,
  text: Vu,
  html: Wu,
  raise: qu,
  lower: Zu,
  append: Ku,
  insert: Gu,
  remove: Ju,
  clone: nd,
  datum: od,
  on: cd,
  dispatch: dd,
  [Symbol.iterator]: fd
};
function Re(e) {
  return typeof e == "string" ? new Le([[document.querySelector(e)]], [document.documentElement]) : new Le([[e]], Ea);
}
function pd(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function We(e, t) {
  if (e = pd(e), t === void 0 && (t = e.currentTarget), t) {
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
const gd = { passive: !1 }, _n = { capture: !0, passive: !1 };
function _r(e) {
  e.stopImmediatePropagation();
}
function $t(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Ca(e) {
  var t = e.document.documentElement, n = Re(e).on("dragstart.drag", $t, _n);
  "onselectstart" in t ? n.on("selectstart.drag", $t, _n) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function _a(e, t) {
  var n = e.document.documentElement, o = Re(e).on("dragstart.drag", null);
  t && (o.on("click.drag", $t, _n), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const oo = (e) => () => e;
function Wr(e, {
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
Wr.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function yd(e) {
  return !e.ctrlKey && !e.button;
}
function md() {
  return this.parentNode;
}
function xd(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function wd() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function ka() {
  var e = yd, t = md, n = xd, o = wd, r = {}, i = Ho("start", "drag", "end"), s = 0, c, l, a, d, f = 0;
  function h(w) {
    w.on("mousedown.drag", p).filter(o).on("touchstart.drag", v).on("touchmove.drag", m, gd).on("touchend.drag touchcancel.drag", b).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function p(w, C) {
    if (!(d || !e.call(this, w, C))) {
      var E = g(this, t.call(this, w, C), w, C, "mouse");
      E && (Re(w.view).on("mousemove.drag", y, _n).on("mouseup.drag", x, _n), Ca(w.view), _r(w), a = !1, c = w.clientX, l = w.clientY, E("start", w));
    }
  }
  function y(w) {
    if ($t(w), !a) {
      var C = w.clientX - c, E = w.clientY - l;
      a = C * C + E * E > f;
    }
    r.mouse("drag", w);
  }
  function x(w) {
    Re(w.view).on("mousemove.drag mouseup.drag", null), _a(w.view, a), $t(w), r.mouse("end", w);
  }
  function v(w, C) {
    if (e.call(this, w, C)) {
      var E = w.changedTouches, k = t.call(this, w, C), D = E.length, T, F;
      for (T = 0; T < D; ++T)
        (F = g(this, k, w, C, E[T].identifier, E[T])) && (_r(w), F("start", w, E[T]));
    }
  }
  function m(w) {
    var C = w.changedTouches, E = C.length, k, D;
    for (k = 0; k < E; ++k)
      (D = r[C[k].identifier]) && ($t(w), D("drag", w, C[k]));
  }
  function b(w) {
    var C = w.changedTouches, E = C.length, k, D;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), k = 0; k < E; ++k)
      (D = r[C[k].identifier]) && (_r(w), D("end", w, C[k]));
  }
  function g(w, C, E, k, D, T) {
    var F = i.copy(), j = We(T || E, C), z, H, S;
    if ((S = n.call(w, new Wr("beforestart", {
      sourceEvent: E,
      target: h,
      identifier: D,
      active: s,
      x: j[0],
      y: j[1],
      dx: 0,
      dy: 0,
      dispatch: F
    }), k)) != null)
      return z = S.x - j[0] || 0, H = S.y - j[1] || 0, function I(_, A, $) {
        var P = j, W;
        switch (_) {
          case "start":
            r[D] = I, W = s++;
            break;
          case "end":
            delete r[D], --s;
          // falls through
          case "drag":
            j = We($ || A, C), W = s;
            break;
        }
        F.call(
          _,
          w,
          new Wr(_, {
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
function ui(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function Ia(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function Ln() {
}
var kn = 0.7, Eo = 1 / kn, zt = "\\s*([+-]?\\d+)\\s*", In = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", Ge = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", vd = /^#([0-9a-f]{3,8})$/, bd = new RegExp(`^rgb\\(${zt},${zt},${zt}\\)$`), Sd = new RegExp(`^rgb\\(${Ge},${Ge},${Ge}\\)$`), Nd = new RegExp(`^rgba\\(${zt},${zt},${zt},${In}\\)$`), Ed = new RegExp(`^rgba\\(${Ge},${Ge},${Ge},${In}\\)$`), Cd = new RegExp(`^hsl\\(${In},${Ge},${Ge}\\)$`), _d = new RegExp(`^hsla\\(${In},${Ge},${Ge},${In}\\)$`), Hi = {
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
ui(Ln, St, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Vi,
  // Deprecated! Use color.formatHex.
  formatHex: Vi,
  formatHex8: kd,
  formatHsl: Id,
  formatRgb: Oi,
  toString: Oi
});
function Vi() {
  return this.rgb().formatHex();
}
function kd() {
  return this.rgb().formatHex8();
}
function Id() {
  return ja(this).formatHsl();
}
function Oi() {
  return this.rgb().formatRgb();
}
function St(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = vd.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? Bi(t) : n === 3 ? new De(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? ro(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? ro(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = bd.exec(e)) ? new De(t[1], t[2], t[3], 1) : (t = Sd.exec(e)) ? new De(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = Nd.exec(e)) ? ro(t[1], t[2], t[3], t[4]) : (t = Ed.exec(e)) ? ro(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = Cd.exec(e)) ? Xi(t[1], t[2] / 100, t[3] / 100, 1) : (t = _d.exec(e)) ? Xi(t[1], t[2] / 100, t[3] / 100, t[4]) : Hi.hasOwnProperty(e) ? Bi(Hi[e]) : e === "transparent" ? new De(NaN, NaN, NaN, 0) : null;
}
function Bi(e) {
  return new De(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function ro(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new De(e, t, n, o);
}
function jd(e) {
  return e instanceof Ln || (e = St(e)), e ? (e = e.rgb(), new De(e.r, e.g, e.b, e.opacity)) : new De();
}
function Xr(e, t, n, o) {
  return arguments.length === 1 ? jd(e) : new De(e, t, n, o ?? 1);
}
function De(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
ui(De, Xr, Ia(Ln, {
  brighter(e) {
    return e = e == null ? Eo : Math.pow(Eo, e), new De(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? kn : Math.pow(kn, e), new De(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new De(vt(this.r), vt(this.g), vt(this.b), Co(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: Fi,
  // Deprecated! Use color.formatHex.
  formatHex: Fi,
  formatHex8: Ad,
  formatRgb: Wi,
  toString: Wi
}));
function Fi() {
  return `#${wt(this.r)}${wt(this.g)}${wt(this.b)}`;
}
function Ad() {
  return `#${wt(this.r)}${wt(this.g)}${wt(this.b)}${wt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function Wi() {
  const e = Co(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${vt(this.r)}, ${vt(this.g)}, ${vt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Co(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function vt(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function wt(e) {
  return e = vt(e), (e < 16 ? "0" : "") + e.toString(16);
}
function Xi(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Xe(e, t, n, o);
}
function ja(e) {
  if (e instanceof Xe) return new Xe(e.h, e.s, e.l, e.opacity);
  if (e instanceof Ln || (e = St(e)), !e) return new Xe();
  if (e instanceof Xe) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), i = Math.max(t, n, o), s = NaN, c = i - r, l = (i + r) / 2;
  return c ? (t === i ? s = (n - o) / c + (n < o) * 6 : n === i ? s = (o - t) / c + 2 : s = (t - n) / c + 4, c /= l < 0.5 ? i + r : 2 - i - r, s *= 60) : c = l > 0 && l < 1 ? 0 : s, new Xe(s, c, l, e.opacity);
}
function Md(e, t, n, o) {
  return arguments.length === 1 ? ja(e) : new Xe(e, t, n, o ?? 1);
}
function Xe(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
ui(Xe, Md, Ia(Ln, {
  brighter(e) {
    return e = e == null ? Eo : Math.pow(Eo, e), new Xe(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? kn : Math.pow(kn, e), new Xe(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new De(
      kr(e >= 240 ? e - 240 : e + 120, r, o),
      kr(e, r, o),
      kr(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new Xe(qi(this.h), io(this.s), io(this.l), Co(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Co(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${qi(this.h)}, ${io(this.s) * 100}%, ${io(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function qi(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function io(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function kr(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const di = (e) => () => e;
function Dd(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function Pd(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function Td(e) {
  return (e = +e) == 1 ? Aa : function(t, n) {
    return n - t ? Pd(t, n, e) : di(isNaN(t) ? n : t);
  };
}
function Aa(e, t) {
  var n = t - e;
  return n ? Dd(e, n) : di(isNaN(e) ? t : e);
}
const _o = (function e(t) {
  var n = Td(t);
  function o(r, i) {
    var s = n((r = Xr(r)).r, (i = Xr(i)).r), c = n(r.g, i.g), l = n(r.b, i.b), a = Aa(r.opacity, i.opacity);
    return function(d) {
      return r.r = s(d), r.g = c(d), r.b = l(d), r.opacity = a(d), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function $d(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(i) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - i) + t[r] * i;
    return o;
  };
}
function zd(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Rd(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), i = new Array(n), s;
  for (s = 0; s < o; ++s) r[s] = En(e[s], t[s]);
  for (; s < n; ++s) i[s] = t[s];
  return function(c) {
    for (s = 0; s < o; ++s) i[s] = r[s](c);
    return i;
  };
}
function Ld(e, t) {
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
function Hd(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = En(e[r], t[r]) : o[r] = t[r];
  return function(i) {
    for (r in n) o[r] = n[r](i);
    return o;
  };
}
var qr = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, Ir = new RegExp(qr.source, "g");
function Vd(e) {
  return function() {
    return e;
  };
}
function Od(e) {
  return function(t) {
    return e(t) + "";
  };
}
function Ma(e, t) {
  var n = qr.lastIndex = Ir.lastIndex = 0, o, r, i, s = -1, c = [], l = [];
  for (e = e + "", t = t + ""; (o = qr.exec(e)) && (r = Ir.exec(t)); )
    (i = r.index) > n && (i = t.slice(n, i), c[s] ? c[s] += i : c[++s] = i), (o = o[0]) === (r = r[0]) ? c[s] ? c[s] += r : c[++s] = r : (c[++s] = null, l.push({ i: s, x: Ue(o, r) })), n = Ir.lastIndex;
  return n < t.length && (i = t.slice(n), c[s] ? c[s] += i : c[++s] = i), c.length < 2 ? l[0] ? Od(l[0].x) : Vd(t) : (t = l.length, function(a) {
    for (var d = 0, f; d < t; ++d) c[(f = l[d]).i] = f.x(a);
    return c.join("");
  });
}
function En(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? di(t) : (n === "number" ? Ue : n === "string" ? (o = St(t)) ? (t = o, _o) : Ma : t instanceof St ? _o : t instanceof Date ? Ld : zd(t) ? $d : Array.isArray(t) ? Rd : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Hd : Ue)(e, t);
}
var Yi = 180 / Math.PI, Yr = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function Da(e, t, n, o, r, i) {
  var s, c, l;
  return (s = Math.sqrt(e * e + t * t)) && (e /= s, t /= s), (l = e * n + t * o) && (n -= e * l, o -= t * l), (c = Math.sqrt(n * n + o * o)) && (n /= c, o /= c, l /= c), e * o < t * n && (e = -e, t = -t, l = -l, s = -s), {
    translateX: r,
    translateY: i,
    rotate: Math.atan2(t, e) * Yi,
    skewX: Math.atan(l) * Yi,
    scaleX: s,
    scaleY: c
  };
}
var so;
function Bd(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Yr : Da(t.a, t.b, t.c, t.d, t.e, t.f);
}
function Fd(e) {
  return e == null || (so || (so = document.createElementNS("http://www.w3.org/2000/svg", "g")), so.setAttribute("transform", e), !(e = so.transform.baseVal.consolidate())) ? Yr : (e = e.matrix, Da(e.a, e.b, e.c, e.d, e.e, e.f));
}
function Pa(e, t, n, o) {
  function r(a) {
    return a.length ? a.pop() + " " : "";
  }
  function i(a, d, f, h, p, y) {
    if (a !== f || d !== h) {
      var x = p.push("translate(", null, t, null, n);
      y.push({ i: x - 4, x: Ue(a, f) }, { i: x - 2, x: Ue(d, h) });
    } else (f || h) && p.push("translate(" + f + t + h + n);
  }
  function s(a, d, f, h) {
    a !== d ? (a - d > 180 ? d += 360 : d - a > 180 && (a += 360), h.push({ i: f.push(r(f) + "rotate(", null, o) - 2, x: Ue(a, d) })) : d && f.push(r(f) + "rotate(" + d + o);
  }
  function c(a, d, f, h) {
    a !== d ? h.push({ i: f.push(r(f) + "skewX(", null, o) - 2, x: Ue(a, d) }) : d && f.push(r(f) + "skewX(" + d + o);
  }
  function l(a, d, f, h, p, y) {
    if (a !== f || d !== h) {
      var x = p.push(r(p) + "scale(", null, ",", null, ")");
      y.push({ i: x - 4, x: Ue(a, f) }, { i: x - 2, x: Ue(d, h) });
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
var Wd = Pa(Bd, "px, ", "px)", "deg)"), Xd = Pa(Fd, ", ", ")", ")"), qd = 1e-12;
function Zi(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Yd(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Zd(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const yo = (function e(t, n, o) {
  function r(i, s) {
    var c = i[0], l = i[1], a = i[2], d = s[0], f = s[1], h = s[2], p = d - c, y = f - l, x = p * p + y * y, v, m;
    if (x < qd)
      m = Math.log(h / a) / t, v = function(k) {
        return [
          c + k * p,
          l + k * y,
          a * Math.exp(t * k * m)
        ];
      };
    else {
      var b = Math.sqrt(x), g = (h * h - a * a + o * x) / (2 * a * n * b), w = (h * h - a * a - o * x) / (2 * h * n * b), C = Math.log(Math.sqrt(g * g + 1) - g), E = Math.log(Math.sqrt(w * w + 1) - w);
      m = (E - C) / t, v = function(k) {
        var D = k * m, T = Zi(C), F = a / (n * b) * (T * Zd(t * D + C) - Yd(C));
        return [
          c + F * p,
          l + F * y,
          a * T / Zi(t * D + C)
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
var Lt = 0, bn = 0, pn = 0, Ta = 1e3, ko, Sn, Io = 0, Nt = 0, Oo = 0, jn = typeof performance == "object" && performance.now ? performance : Date, $a = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function fi() {
  return Nt || ($a(Kd), Nt = jn.now() + Oo);
}
function Kd() {
  Nt = 0;
}
function jo() {
  this._call = this._time = this._next = null;
}
jo.prototype = za.prototype = {
  constructor: jo,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? fi() : +n) + (t == null ? 0 : +t), !this._next && Sn !== this && (Sn ? Sn._next = this : ko = this, Sn = this), this._call = e, this._time = n, Zr();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Zr());
  }
};
function za(e, t, n) {
  var o = new jo();
  return o.restart(e, t, n), o;
}
function Ud() {
  fi(), ++Lt;
  for (var e = ko, t; e; )
    (t = Nt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --Lt;
}
function Ki() {
  Nt = (Io = jn.now()) + Oo, Lt = bn = 0;
  try {
    Ud();
  } finally {
    Lt = 0, Qd(), Nt = 0;
  }
}
function Gd() {
  var e = jn.now(), t = e - Io;
  t > Ta && (Oo -= t, Io = e);
}
function Qd() {
  for (var e, t = ko, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : ko = n);
  Sn = e, Zr(o);
}
function Zr(e) {
  if (!Lt) {
    bn && (bn = clearTimeout(bn));
    var t = e - Nt;
    t > 24 ? (e < 1 / 0 && (bn = setTimeout(Ki, e - jn.now() - Oo)), pn && (pn = clearInterval(pn))) : (pn || (Io = jn.now(), pn = setInterval(Gd, Ta)), Lt = 1, $a(Ki));
  }
}
function Ui(e, t, n) {
  var o = new jo();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var Jd = Ho("start", "end", "cancel", "interrupt"), ef = [], Ra = 0, Gi = 1, Kr = 2, mo = 3, Qi = 4, Ur = 5, xo = 6;
function Bo(e, t, n, o, r, i) {
  var s = e.__transition;
  if (!s) e.__transition = {};
  else if (n in s) return;
  tf(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: Jd,
    tween: ef,
    time: i.time,
    delay: i.delay,
    duration: i.duration,
    ease: i.ease,
    timer: null,
    state: Ra
  });
}
function hi(e, t) {
  var n = Ze(e, t);
  if (n.state > Ra) throw new Error("too late; already scheduled");
  return n;
}
function Je(e, t) {
  var n = Ze(e, t);
  if (n.state > mo) throw new Error("too late; already running");
  return n;
}
function Ze(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function tf(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = za(i, 0, n.time);
  function i(a) {
    n.state = Gi, n.timer.restart(s, n.delay, n.time), n.delay <= a && s(a - n.delay);
  }
  function s(a) {
    var d, f, h, p;
    if (n.state !== Gi) return l();
    for (d in o)
      if (p = o[d], p.name === n.name) {
        if (p.state === mo) return Ui(s);
        p.state === Qi ? (p.state = xo, p.timer.stop(), p.on.call("interrupt", e, e.__data__, p.index, p.group), delete o[d]) : +d < t && (p.state = xo, p.timer.stop(), p.on.call("cancel", e, e.__data__, p.index, p.group), delete o[d]);
      }
    if (Ui(function() {
      n.state === mo && (n.state = Qi, n.timer.restart(c, n.delay, n.time), c(a));
    }), n.state = Kr, n.on.call("start", e, e.__data__, n.index, n.group), n.state === Kr) {
      for (n.state = mo, r = new Array(h = n.tween.length), d = 0, f = -1; d < h; ++d)
        (p = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (r[++f] = p);
      r.length = f + 1;
    }
  }
  function c(a) {
    for (var d = a < n.duration ? n.ease.call(null, a / n.duration) : (n.timer.restart(l), n.state = Ur, 1), f = -1, h = r.length; ++f < h; )
      r[f].call(e, d);
    n.state === Ur && (n.on.call("end", e, e.__data__, n.index, n.group), l());
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
      r = o.state > Kr && o.state < Ur, o.state = xo, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[s];
    }
    i && delete e.__transition;
  }
}
function nf(e) {
  return this.each(function() {
    wo(this, e);
  });
}
function of(e, t) {
  var n, o;
  return function() {
    var r = Je(this, e), i = r.tween;
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
function rf(e, t, n) {
  var o, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var i = Je(this, e), s = i.tween;
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
function sf(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = Ze(this.node(), n).tween, r = 0, i = o.length, s; r < i; ++r)
      if ((s = o[r]).name === e)
        return s.value;
    return null;
  }
  return this.each((t == null ? of : rf)(n, e, t));
}
function pi(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = Je(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return Ze(r, o).value[t];
  };
}
function La(e, t) {
  var n;
  return (typeof t == "number" ? Ue : t instanceof St ? _o : (n = St(t)) ? (t = n, _o) : Ma)(e, t);
}
function af(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function cf(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function lf(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttribute(e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function uf(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttributeNS(e.space, e.local);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function df(e, t, n) {
  var o, r, i;
  return function() {
    var s, c = n(this), l;
    return c == null ? void this.removeAttribute(e) : (s = this.getAttribute(e), l = c + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, c)));
  };
}
function ff(e, t, n) {
  var o, r, i;
  return function() {
    var s, c = n(this), l;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (s = this.getAttributeNS(e.space, e.local), l = c + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, c)));
  };
}
function hf(e, t) {
  var n = Vo(e), o = n === "transform" ? Xd : La;
  return this.attrTween(e, typeof t == "function" ? (n.local ? ff : df)(n, o, pi(this, "attr." + e, t)) : t == null ? (n.local ? cf : af)(n) : (n.local ? uf : lf)(n, o, t));
}
function pf(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function gf(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function yf(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && gf(e, i)), n;
  }
  return r._value = t, r;
}
function mf(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && pf(e, i)), n;
  }
  return r._value = t, r;
}
function xf(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = Vo(e);
  return this.tween(n, (o.local ? yf : mf)(o, t));
}
function wf(e, t) {
  return function() {
    hi(this, e).delay = +t.apply(this, arguments);
  };
}
function vf(e, t) {
  return t = +t, function() {
    hi(this, e).delay = t;
  };
}
function bf(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? wf : vf)(t, e)) : Ze(this.node(), t).delay;
}
function Sf(e, t) {
  return function() {
    Je(this, e).duration = +t.apply(this, arguments);
  };
}
function Nf(e, t) {
  return t = +t, function() {
    Je(this, e).duration = t;
  };
}
function Ef(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Sf : Nf)(t, e)) : Ze(this.node(), t).duration;
}
function Cf(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    Je(this, e).ease = t;
  };
}
function _f(e) {
  var t = this._id;
  return arguments.length ? this.each(Cf(t, e)) : Ze(this.node(), t).ease;
}
function kf(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    Je(this, e).ease = n;
  };
}
function If(e) {
  if (typeof e != "function") throw new Error();
  return this.each(kf(this._id, e));
}
function jf(e) {
  typeof e != "function" && (e = ga(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = [], l, a = 0; a < s; ++a)
      (l = i[a]) && e.call(l, l.__data__, a, i) && c.push(l);
  return new rt(o, this._parents, this._name, this._id);
}
function Af(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, i = Math.min(o, r), s = new Array(o), c = 0; c < i; ++c)
    for (var l = t[c], a = n[c], d = l.length, f = s[c] = new Array(d), h, p = 0; p < d; ++p)
      (h = l[p] || a[p]) && (f[p] = h);
  for (; c < o; ++c)
    s[c] = t[c];
  return new rt(s, this._parents, this._name, this._id);
}
function Mf(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function Df(e, t, n) {
  var o, r, i = Mf(t) ? hi : Je;
  return function() {
    var s = i(this, e), c = s.on;
    c !== o && (r = (o = c).copy()).on(t, n), s.on = r;
  };
}
function Pf(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Ze(this.node(), n).on.on(e) : this.each(Df(n, e, t));
}
function Tf(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function $f() {
  return this.on("end.remove", Tf(this._id));
}
function zf(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = ci(e));
  for (var o = this._groups, r = o.length, i = new Array(r), s = 0; s < r; ++s)
    for (var c = o[s], l = c.length, a = i[s] = new Array(l), d, f, h = 0; h < l; ++h)
      (d = c[h]) && (f = e.call(d, d.__data__, h, c)) && ("__data__" in d && (f.__data__ = d.__data__), a[h] = f, Bo(a[h], t, n, h, a, Ze(d, n)));
  return new rt(i, this._parents, t, n);
}
function Rf(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = pa(e));
  for (var o = this._groups, r = o.length, i = [], s = [], c = 0; c < r; ++c)
    for (var l = o[c], a = l.length, d, f = 0; f < a; ++f)
      if (d = l[f]) {
        for (var h = e.call(d, d.__data__, f, l), p, y = Ze(d, n), x = 0, v = h.length; x < v; ++x)
          (p = h[x]) && Bo(p, t, n, x, h, y);
        i.push(h), s.push(d);
      }
  return new rt(i, s, t, n);
}
var Lf = Rn.prototype.constructor;
function Hf() {
  return new Lf(this._groups, this._parents);
}
function Vf(e, t) {
  var n, o, r;
  return function() {
    var i = Rt(this, e), s = (this.style.removeProperty(e), Rt(this, e));
    return i === s ? null : i === n && s === o ? r : r = t(n = i, o = s);
  };
}
function Ha(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Of(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = Rt(this, e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Bf(e, t, n) {
  var o, r, i;
  return function() {
    var s = Rt(this, e), c = n(this), l = c + "";
    return c == null && (l = c = (this.style.removeProperty(e), Rt(this, e))), s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, c));
  };
}
function Ff(e, t) {
  var n, o, r, i = "style." + t, s = "end." + i, c;
  return function() {
    var l = Je(this, e), a = l.on, d = l.value[i] == null ? c || (c = Ha(t)) : void 0;
    (a !== n || r !== d) && (o = (n = a).copy()).on(s, r = d), l.on = o;
  };
}
function Wf(e, t, n) {
  var o = (e += "") == "transform" ? Wd : La;
  return t == null ? this.styleTween(e, Vf(e, o)).on("end.style." + e, Ha(e)) : typeof t == "function" ? this.styleTween(e, Bf(e, o, pi(this, "style." + e, t))).each(Ff(this._id, e)) : this.styleTween(e, Of(e, o, t), n).on("end.style." + e, null);
}
function Xf(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function qf(e, t, n) {
  var o, r;
  function i() {
    var s = t.apply(this, arguments);
    return s !== r && (o = (r = s) && Xf(e, s, n)), o;
  }
  return i._value = t, i;
}
function Yf(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, qf(e, t, n ?? ""));
}
function Zf(e) {
  return function() {
    this.textContent = e;
  };
}
function Kf(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function Uf(e) {
  return this.tween("text", typeof e == "function" ? Kf(pi(this, "text", e)) : Zf(e == null ? "" : e + ""));
}
function Gf(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Qf(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Gf(r)), t;
  }
  return o._value = e, o;
}
function Jf(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, Qf(e));
}
function eh() {
  for (var e = this._name, t = this._id, n = Va(), o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], c = s.length, l, a = 0; a < c; ++a)
      if (l = s[a]) {
        var d = Ze(l, t);
        Bo(l, e, n, a, s, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new rt(o, this._parents, e, n);
}
function th() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(i, s) {
    var c = { value: s }, l = { value: function() {
      --r === 0 && i();
    } };
    n.each(function() {
      var a = Je(this, o), d = a.on;
      d !== e && (t = (e = d).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(l)), a.on = t;
    }), r === 0 && i();
  });
}
var nh = 0;
function rt(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function Va() {
  return ++nh;
}
var nt = Rn.prototype;
rt.prototype = {
  constructor: rt,
  select: zf,
  selectAll: Rf,
  selectChild: nt.selectChild,
  selectChildren: nt.selectChildren,
  filter: jf,
  merge: Af,
  selection: Hf,
  transition: eh,
  call: nt.call,
  nodes: nt.nodes,
  node: nt.node,
  size: nt.size,
  empty: nt.empty,
  each: nt.each,
  on: Pf,
  attr: hf,
  attrTween: xf,
  style: Wf,
  styleTween: Yf,
  text: Uf,
  textTween: Jf,
  remove: $f,
  tween: sf,
  delay: bf,
  duration: Ef,
  ease: _f,
  easeVarying: If,
  end: th,
  [Symbol.iterator]: nt[Symbol.iterator]
};
function oh(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var rh = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: oh
};
function ih(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function sh(e) {
  var t, n;
  e instanceof rt ? (t = e._id, e = e._name) : (t = Va(), (n = rh).time = fi(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], c = s.length, l, a = 0; a < c; ++a)
      (l = s[a]) && Bo(l, e, t, a, s, n || ih(l, t));
  return new rt(o, this._parents, e, t);
}
Rn.prototype.interrupt = nf;
Rn.prototype.transition = sh;
const ao = (e) => () => e;
function ah(e, {
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
Oa.prototype = ot.prototype;
function Oa(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return Fo;
  return e.__zoom;
}
function jr(e) {
  e.stopImmediatePropagation();
}
function gn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function ch(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function lh() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function Ji() {
  return this.__zoom || Fo;
}
function uh(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function dh() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function fh(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], i = e.invertY(t[0][1]) - n[0][1], s = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    s > i ? (i + s) / 2 : Math.min(0, i) || Math.max(0, s)
  );
}
function Ba() {
  var e = ch, t = lh, n = fh, o = uh, r = dh, i = [0, 1 / 0], s = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, l = yo, a = Ho("start", "zoom", "end"), d, f, h, p = 500, y = 150, x = 0, v = 10;
  function m(S) {
    S.property("__zoom", Ji).on("wheel.zoom", D, { passive: !1 }).on("mousedown.zoom", T).on("dblclick.zoom", F).filter(r).on("touchstart.zoom", j).on("touchmove.zoom", z).on("touchend.zoom touchcancel.zoom", H).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  m.transform = function(S, I, _, A) {
    var $ = S.selection ? S.selection() : S;
    $.property("__zoom", Ji), S !== $ ? C(S, I, _, A) : $.interrupt().each(function() {
      E(this, arguments).event(A).start().zoom(null, typeof I == "function" ? I.apply(this, arguments) : I).end();
    });
  }, m.scaleBy = function(S, I, _, A) {
    m.scaleTo(S, function() {
      var $ = this.__zoom.k, P = typeof I == "function" ? I.apply(this, arguments) : I;
      return $ * P;
    }, _, A);
  }, m.scaleTo = function(S, I, _, A) {
    m.transform(S, function() {
      var $ = t.apply(this, arguments), P = this.__zoom, W = _ == null ? w($) : typeof _ == "function" ? _.apply(this, arguments) : _, O = P.invert(W), B = typeof I == "function" ? I.apply(this, arguments) : I;
      return n(g(b(P, B), W, O), $, s);
    }, _, A);
  }, m.translateBy = function(S, I, _, A) {
    m.transform(S, function() {
      return n(this.__zoom.translate(
        typeof I == "function" ? I.apply(this, arguments) : I,
        typeof _ == "function" ? _.apply(this, arguments) : _
      ), t.apply(this, arguments), s);
    }, null, A);
  }, m.translateTo = function(S, I, _, A, $) {
    m.transform(S, function() {
      var P = t.apply(this, arguments), W = this.__zoom, O = A == null ? w(P) : typeof A == "function" ? A.apply(this, arguments) : A;
      return n(Fo.translate(O[0], O[1]).scale(W.k).translate(
        typeof I == "function" ? -I.apply(this, arguments) : -I,
        typeof _ == "function" ? -_.apply(this, arguments) : -_
      ), P, s);
    }, A, $);
  };
  function b(S, I) {
    return I = Math.max(i[0], Math.min(i[1], I)), I === S.k ? S : new ot(I, S.x, S.y);
  }
  function g(S, I, _) {
    var A = I[0] - _[0] * S.k, $ = I[1] - _[1] * S.k;
    return A === S.x && $ === S.y ? S : new ot(S.k, A, $);
  }
  function w(S) {
    return [(+S[0][0] + +S[1][0]) / 2, (+S[0][1] + +S[1][1]) / 2];
  }
  function C(S, I, _, A) {
    S.on("start.zoom", function() {
      E(this, arguments).event(A).start();
    }).on("interrupt.zoom end.zoom", function() {
      E(this, arguments).event(A).end();
    }).tween("zoom", function() {
      var $ = this, P = arguments, W = E($, P).event(A), O = t.apply($, P), B = _ == null ? w(O) : typeof _ == "function" ? _.apply($, P) : _, G = Math.max(O[1][0] - O[0][0], O[1][1] - O[0][1]), Y = $.__zoom, te = typeof I == "function" ? I.apply($, P) : I, ae = l(Y.invert(B).concat(G / Y.k), te.invert(B).concat(G / te.k));
      return function(Q) {
        if (Q === 1) Q = te;
        else {
          var R = ae(Q), q = G / R[2];
          Q = new ot(q, B[0] - R[0] * q, B[1] - R[1] * q);
        }
        W.zoom(null, Q);
      };
    });
  }
  function E(S, I, _) {
    return !_ && S.__zooming || new k(S, I);
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
      var I = Re(this.that).datum();
      a.call(
        S,
        this.that,
        new ah(S, {
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
    var _ = E(this, I).event(S), A = this.__zoom, $ = Math.max(i[0], Math.min(i[1], A.k * Math.pow(2, o.apply(this, arguments)))), P = We(S);
    if (_.wheel)
      (_.mouse[0][0] !== P[0] || _.mouse[0][1] !== P[1]) && (_.mouse[1] = A.invert(_.mouse[0] = P)), clearTimeout(_.wheel);
    else {
      if (A.k === $) return;
      _.mouse = [P, A.invert(P)], wo(this), _.start();
    }
    gn(S), _.wheel = setTimeout(W, y), _.zoom("mouse", n(g(b(A, $), _.mouse[0], _.mouse[1]), _.extent, s));
    function W() {
      _.wheel = null, _.end();
    }
  }
  function T(S, ...I) {
    if (h || !e.apply(this, arguments)) return;
    var _ = S.currentTarget, A = E(this, I, !0).event(S), $ = Re(S.view).on("mousemove.zoom", B, !0).on("mouseup.zoom", G, !0), P = We(S, _), W = S.clientX, O = S.clientY;
    Ca(S.view), jr(S), A.mouse = [P, this.__zoom.invert(P)], wo(this), A.start();
    function B(Y) {
      if (gn(Y), !A.moved) {
        var te = Y.clientX - W, ae = Y.clientY - O;
        A.moved = te * te + ae * ae > x;
      }
      A.event(Y).zoom("mouse", n(g(A.that.__zoom, A.mouse[0] = We(Y, _), A.mouse[1]), A.extent, s));
    }
    function G(Y) {
      $.on("mousemove.zoom mouseup.zoom", null), _a(Y.view, A.moved), gn(Y), A.event(Y).end();
    }
  }
  function F(S, ...I) {
    if (e.apply(this, arguments)) {
      var _ = this.__zoom, A = We(S.changedTouches ? S.changedTouches[0] : S, this), $ = _.invert(A), P = _.k * (S.shiftKey ? 0.5 : 2), W = n(g(b(_, P), A, $), t.apply(this, I), s);
      gn(S), c > 0 ? Re(this).transition().duration(c).call(C, W, A, S) : Re(this).call(m.transform, W, A, S);
    }
  }
  function j(S, ...I) {
    if (e.apply(this, arguments)) {
      var _ = S.touches, A = _.length, $ = E(this, I, S.changedTouches.length === A).event(S), P, W, O, B;
      for (jr(S), W = 0; W < A; ++W)
        O = _[W], B = We(O, this), B = [B, this.__zoom.invert(B), O.identifier], $.touch0 ? !$.touch1 && $.touch0[2] !== B[2] && ($.touch1 = B, $.taps = 0) : ($.touch0 = B, P = !0, $.taps = 1 + !!d);
      d && (d = clearTimeout(d)), P && ($.taps < 2 && (f = B[0], d = setTimeout(function() {
        d = null;
      }, p)), wo(this), $.start());
    }
  }
  function z(S, ...I) {
    if (this.__zooming) {
      var _ = E(this, I).event(S), A = S.changedTouches, $ = A.length, P, W, O, B;
      for (gn(S), P = 0; P < $; ++P)
        W = A[P], O = We(W, this), _.touch0 && _.touch0[2] === W.identifier ? _.touch0[0] = O : _.touch1 && _.touch1[2] === W.identifier && (_.touch1[0] = O);
      if (W = _.that.__zoom, _.touch1) {
        var G = _.touch0[0], Y = _.touch0[1], te = _.touch1[0], ae = _.touch1[1], Q = (Q = te[0] - G[0]) * Q + (Q = te[1] - G[1]) * Q, R = (R = ae[0] - Y[0]) * R + (R = ae[1] - Y[1]) * R;
        W = b(W, Math.sqrt(Q / R)), O = [(G[0] + te[0]) / 2, (G[1] + te[1]) / 2], B = [(Y[0] + ae[0]) / 2, (Y[1] + ae[1]) / 2];
      } else if (_.touch0) O = _.touch0[0], B = _.touch0[1];
      else return;
      _.zoom("touch", n(g(W, O, B), _.extent, s));
    }
  }
  function H(S, ...I) {
    if (this.__zooming) {
      var _ = E(this, I).event(S), A = S.changedTouches, $ = A.length, P, W;
      for (jr(S), h && clearTimeout(h), h = setTimeout(function() {
        h = null;
      }, p), P = 0; P < $; ++P)
        W = A[P], _.touch0 && _.touch0[2] === W.identifier ? delete _.touch0 : _.touch1 && _.touch1[2] === W.identifier && delete _.touch1;
      if (_.touch1 && !_.touch0 && (_.touch0 = _.touch1, delete _.touch1), _.touch0) _.touch0[1] = this.__zoom.invert(_.touch0[0]);
      else if (_.end(), _.taps === 2 && (W = We(W, this), Math.hypot(f[0] - W[0], f[1] - W[1]) < v)) {
        var O = Re(this).on("dblclick.zoom");
        O && O.apply(this, arguments);
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
}, An = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], Fa = ["Enter", " ", "Escape"], Wa = {
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
var Ht;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(Ht || (Ht = {}));
var bt;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(bt || (bt = {}));
var Mn;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Mn || (Mn = {}));
const Xa = {
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
var lt;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(lt || (lt = {}));
var Ao;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(Ao || (Ao = {}));
var ee;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(ee || (ee = {}));
const es = {
  [ee.Left]: ee.Right,
  [ee.Right]: ee.Left,
  [ee.Top]: ee.Bottom,
  [ee.Bottom]: ee.Top
};
function qa(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Ya = (e) => "id" in e && "source" in e && "target" in e, hh = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), gi = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), Hn = (e, t = [0, 0]) => {
  const { width: n, height: o } = it(e), r = e.origin ?? t, i = n * r[0], s = o * r[1];
  return {
    x: e.position.x - i,
    y: e.position.y - s
  };
}, ph = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const i = typeof r == "string";
    let s = !t.nodeLookup && !i ? r : void 0;
    t.nodeLookup && (s = i ? t.nodeLookup.get(r) : gi(r) ? r : t.nodeLookup.get(r.id));
    const c = s ? Mo(s, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return Wo(o, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return Xo(n);
}, Vn = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = Wo(n, Mo(r)), o = !0);
  }), o ? Xo(n) : { x: 0, y: 0, width: 0, height: 0 };
}, yi = (e, t, [n, o, r] = [0, 0, 1], i = !1, s = !1) => {
  const c = {
    ...Kt(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, l = [];
  for (const a of e.values()) {
    const { measured: d, selectable: f = !0, hidden: h = !1 } = a;
    if (s && !f || h)
      continue;
    const p = d.width ?? a.width ?? a.initialWidth ?? null, y = d.height ?? a.height ?? a.initialHeight ?? null, x = Dn(c, Ot(a)), v = (p ?? 0) * (y ?? 0), m = i && x > 0;
    (!a.internals.handleBounds || m || x >= v || a.dragging) && l.push(a);
  }
  return l;
}, gh = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function yh(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function mh({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: i }, s) {
  if (e.size === 0)
    return !0;
  const c = yh(e, s), l = Vn(c), a = xi(l, t, n, s?.minZoom ?? r, s?.maxZoom ?? i, s?.padding ?? 0.1);
  return await o.setViewport(a, {
    duration: s?.duration,
    ease: s?.ease,
    interpolate: s?.interpolate
  }), !0;
}
function Za({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: i }) {
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
  else c && Ct(s.extent) && (f = [
    [s.extent[0][0] + l, s.extent[0][1] + a],
    [s.extent[1][0] + l, s.extent[1][1] + a]
  ]);
  const h = Ct(f) ? Et(t, f, s.measured) : t;
  return (s.measured.width === void 0 || s.measured.height === void 0) && i?.("015", He.error015()), {
    position: {
      x: h.x - l + (s.measured.width ?? 0) * d[0],
      y: h.y - a + (s.measured.height ?? 0) * d[1]
    },
    positionAbsolute: h
  };
}
async function xh({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const i = new Set(e.map((h) => h.id)), s = [];
  for (const h of n) {
    if (h.deletable === !1)
      continue;
    const p = i.has(h.id), y = !p && h.parentId && s.find((x) => x.id === h.parentId);
    (p || y) && s.push(h);
  }
  const c = new Set(t.map((h) => h.id)), l = o.filter((h) => h.deletable !== !1), d = gh(s, l);
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
const Vt = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), Et = (e = { x: 0, y: 0 }, t, n) => ({
  x: Vt(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: Vt(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Ka(e, t, n) {
  const { width: o, height: r } = it(n), { x: i, y: s } = n.internals.positionAbsolute;
  return Et(e, [
    [i, s],
    [i + o, s + r]
  ], t);
}
const ts = (e, t, n) => e < t ? Vt(Math.abs(e - t), 1, t) / t : e > n ? -Vt(Math.abs(e - n), 1, t) / t : 0, mi = (e, t, n = 15, o = 40) => {
  const r = ts(e.x, o, t.width - o) * n, i = ts(e.y, o, t.height - o) * n;
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
}), Ot = (e, t = [0, 0]) => {
  const { x: n, y: o } = gi(e) ? e.internals.positionAbsolute : Hn(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, Mo = (e, t = [0, 0]) => {
  const { x: n, y: o } = gi(e) ? e.internals.positionAbsolute : Hn(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, Ua = (e, t) => Xo(Wo(Gr(e), Gr(t))), Dn = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, ns = (e) => qe(e.width) && qe(e.height) && qe(e.x) && qe(e.y), qe = (e) => !isNaN(e) && isFinite(e), Ga = (e, t) => (n, o) => {
}, On = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), Kt = ({ x: e, y: t }, [n, o, r], i = !1, s = [1, 1]) => {
  const c = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return i ? On(c, s) : c;
}, Bt = ({ x: e, y: t }, [n, o, r]) => ({
  x: e * r + n,
  y: t * r + o
});
function Mt(e, t) {
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
function wh(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = Mt(e, n), r = Mt(e, t);
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
    const o = Mt(e.top ?? e.y ?? 0, n), r = Mt(e.bottom ?? e.y ?? 0, n), i = Mt(e.left ?? e.x ?? 0, t), s = Mt(e.right ?? e.x ?? 0, t);
    return { top: o, right: s, bottom: r, left: i, x: i + s, y: o + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function vh(e, t, n, o, r, i) {
  const { x: s, y: c } = Bt(e, [t, n, o]), { x: l, y: a } = Bt({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), d = r - l, f = i - a;
  return {
    left: Math.floor(s),
    top: Math.floor(c),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const xi = (e, t, n, o, r, i) => {
  const s = wh(i, t, n), c = (t - s.x) / e.width, l = (n - s.y) / e.height, a = Math.min(c, l), d = Vt(a, o, r), f = e.x + e.width / 2, h = e.y + e.height / 2, p = t / 2 - f * d, y = n / 2 - h * d, x = vh(e, p, y, d, t, n), v = {
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
}, Pn = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function Ct(e) {
  return e != null && e !== "parent";
}
function it(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function Qa(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function Ja(e, t = { width: 0, height: 0 }, n, o, r) {
  const i = { ...e }, s = o.get(n);
  if (s) {
    const c = s.origin || r;
    i.x += s.internals.positionAbsolute.x - (t.width ?? 0) * c[0], i.y += s.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return i;
}
function os(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function bh() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function Sh(e) {
  return { ...Wa, ...e || {} };
}
function Cn(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: i, y: s } = Ye(e), c = Kt({ x: i - (r?.left ?? 0), y: s - (r?.top ?? 0) }, o), { x: l, y: a } = n ? On(c, t) : c;
  return {
    xSnapped: l,
    ySnapped: a,
    ...c
  };
}
const wi = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), ec = (e) => e?.getRootNode?.() || window?.document, Nh = ["INPUT", "SELECT", "TEXTAREA"];
function tc(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : Nh.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const nc = (e) => "clientX" in e, Ye = (e, t) => {
  const n = nc(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, rs = (e, t, n, o, r) => {
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
      ...wi(s)
    };
  });
};
function oc({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: i, targetControlX: s, targetControlY: c }) {
  const l = e * 0.125 + r * 0.375 + s * 0.375 + n * 0.125, a = t * 0.125 + i * 0.375 + c * 0.375 + o * 0.125, d = Math.abs(l - e), f = Math.abs(a - t);
  return [l, a, d, f];
}
function co(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function is({ pos: e, x1: t, y1: n, x2: o, y2: r, c: i }) {
  switch (e) {
    case ee.Left:
      return [t - co(t - o, i), n];
    case ee.Right:
      return [t + co(o - t, i), n];
    case ee.Top:
      return [t, n - co(n - r, i)];
    case ee.Bottom:
      return [t, n + co(r - n, i)];
  }
}
function rc({ sourceX: e, sourceY: t, sourcePosition: n = ee.Bottom, targetX: o, targetY: r, targetPosition: i = ee.Top, curvature: s = 0.25 }) {
  const [c, l] = is({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: s
  }), [a, d] = is({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: s
  }), [f, h, p, y] = oc({
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
function ic({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, i = n < e ? n + r : n - r, s = Math.abs(o - t) / 2, c = o < t ? o + s : o - s;
  return [i, c, r, s];
}
function Eh({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: i = "basic" }) {
  if (i === "manual")
    return o;
  const s = r && n ? o + 1e3 : o, c = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return s + c;
}
function Ch({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const i = Wo(Mo(e), Mo(t));
  i.x === i.x2 && (i.x2 += 1), i.y === i.y2 && (i.y2 += 1);
  const s = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return Dn(s, Xo(i)) > 0;
}
const sc = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, _h = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), kh = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", He.error006()), t;
  const o = n.getEdgeId || sc;
  let r;
  return Ya(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, _h(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, Ih = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: r, ...i } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", He.error006()), n;
  if (!n.find((a) => a.id === e.id))
    return o.onError?.("007", He.error007(r)), n;
  const c = o.getEdgeId || sc, l = {
    ...i,
    id: o.shouldReplaceId ? c(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((a) => a.id !== r).concat(l);
};
function ac({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, i, s, c] = ic({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, i, s, c];
}
const ss = {
  [ee.Left]: { x: -1, y: 0 },
  [ee.Right]: { x: 1, y: 0 },
  [ee.Top]: { x: 0, y: -1 },
  [ee.Bottom]: { x: 0, y: 1 }
}, jh = ({ source: e, sourcePosition: t = ee.Bottom, target: n }) => t === ee.Left || t === ee.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, as = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Ah({ source: e, sourcePosition: t = ee.Bottom, target: n, targetPosition: o = ee.Top, center: r, offset: i, stepPosition: s }) {
  const c = ss[t], l = ss[o], a = { x: e.x + c.x * i, y: e.y + c.y * i }, d = { x: n.x + l.x * i, y: n.y + l.y * i }, f = jh({
    source: a,
    sourcePosition: t,
    target: d
  }), h = f.x !== 0 ? "x" : "y", p = f[h];
  let y = [], x, v;
  const m = { x: 0, y: 0 }, b = { x: 0, y: 0 }, [, , g, w] = ic({
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
    ], T = [
      { x: a.x, y: v },
      { x: d.x, y: v }
    ];
    c[h] === p ? y = h === "x" ? D : T : y = h === "x" ? T : D;
  } else {
    const D = [{ x: a.x, y: d.y }], T = [{ x: d.x, y: a.y }];
    if (h === "x" ? y = c.x === p ? T : D : y = c.y === p ? D : T, t === o) {
      const S = Math.abs(e[h] - n[h]);
      if (S <= i) {
        const I = Math.min(i - 1, i - S);
        c[h] === p ? m[h] = (a[h] > e[h] ? -1 : 1) * I : b[h] = (d[h] > n[h] ? -1 : 1) * I;
      }
    }
    if (t !== o) {
      const S = h === "x" ? "y" : "x", I = c[h] === l[S], _ = a[S] > d[S], A = a[S] < d[S];
      (c[h] === 1 && (!I && _ || I && A) || c[h] !== 1 && (!I && A || I && _)) && (y = h === "x" ? D : T);
    }
    const F = { x: a.x + m.x, y: a.y + m.y }, j = { x: d.x + b.x, y: d.y + b.y }, z = Math.max(Math.abs(F.x - y[0].x), Math.abs(j.x - y[0].x)), H = Math.max(Math.abs(F.y - y[0].y), Math.abs(j.y - y[0].y));
    z >= H ? (x = (F.x + j.x) / 2, v = y[0].y) : (x = y[0].x, v = (F.y + j.y) / 2);
  }
  const C = { x: a.x + m.x, y: a.y + m.y }, E = { x: d.x + b.x, y: d.y + b.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...C.x !== y[0].x || C.y !== y[0].y ? [C] : [],
    ...y,
    ...E.x !== y[y.length - 1].x || E.y !== y[y.length - 1].y ? [E] : [],
    n
  ], x, v, g, w];
}
function Mh(e, t, n, o) {
  const r = Math.min(as(e, t) / 2, as(t, n) / 2, o), { x: i, y: s } = t;
  if (e.x === i && i === n.x || e.y === s && s === n.y)
    return `L${i} ${s}`;
  if (e.y === s) {
    const a = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${i + r * a},${s}Q ${i},${s} ${i},${s + r * d}`;
  }
  const c = e.x < n.x ? 1 : -1, l = e.y < n.y ? -1 : 1;
  return `L ${i},${s + r * l}Q ${i},${s} ${i + r * c},${s}`;
}
function Do({ sourceX: e, sourceY: t, sourcePosition: n = ee.Bottom, targetX: o, targetY: r, targetPosition: i = ee.Top, borderRadius: s = 5, centerX: c, centerY: l, offset: a = 20, stepPosition: d = 0.5 }) {
  const [f, h, p, y, x] = Ah({
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
    v += Mh(f[m - 1], f[m], f[m + 1], s);
  return v += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [v, h, p, y, x];
}
function cs(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function Dh(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!cs(t) || !cs(n))
    return null;
  const o = t.internals.handleBounds || ls(t.handles), r = n.internals.handleBounds || ls(n.handles), i = us(o?.source ?? [], e.sourceHandle), s = us(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === Ht.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!i || !s)
    return e.onError?.("008", He.error008(i ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = i?.position || ee.Bottom, l = s?.position || ee.Top, a = _t(t, i, c), d = _t(n, s, l);
  return {
    sourceX: a.x,
    sourceY: a.y,
    targetX: d.x,
    targetY: d.y,
    sourcePosition: c,
    targetPosition: l
  };
}
function ls(e) {
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
function _t(e, t, n = ee.Left, o = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, i = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: s, height: c } = t ?? it(e);
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
function us(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Qr(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function Ph(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const i = /* @__PURE__ */ new Set();
  return e.reduce((s, c) => ([c.markerStart || o, c.markerEnd || r].forEach((l) => {
    if (l && typeof l == "object") {
      const a = Qr(l, t);
      i.has(a) || (s.push({ id: a, color: l.color || n, ...l }), i.add(a));
    }
  }), s), []).sort((s, c) => s.id.localeCompare(c.id));
}
const cc = 1e3, Th = 10, vi = {
  nodeOrigin: [0, 0],
  nodeExtent: An,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, $h = {
  ...vi,
  checkEquality: !0
};
function bi(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function zh(e, t, n) {
  const o = bi(vi, n);
  for (const r of e.values())
    if (r.parentId)
      Ni(r, e, t, o);
    else {
      const i = Hn(r, o.nodeOrigin), s = Ct(r.extent) ? r.extent : o.nodeExtent, c = Et(i, s, it(r));
      r.internals.positionAbsolute = c;
    }
}
function Rh(e, t) {
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
function Jr(e, t, n, o = {}) {
  const r = bi($h, o), i = { i: 0 }, s = new Map(t), c = r?.elevateNodesOnSelect && !Si(r.zIndexMode) ? cc : 0;
  let l = e.length > 0, a = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = s.get(d.id);
    if (r.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const h = Hn(d, r.nodeOrigin), p = Ct(d.extent) ? d.extent : r.nodeExtent, y = Et(h, p, it(d));
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
          handleBounds: Rh(d, f),
          z: lc(d, c, r.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (l = !1), d.parentId && Ni(f, t, n, o, i), a ||= d.selected ?? !1;
  }
  return { nodesInitialized: l, hasSelectedNodes: a };
}
function Lh(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function Ni(e, t, n, o, r) {
  const { elevateNodesOnSelect: i, nodeOrigin: s, nodeExtent: c, zIndexMode: l } = bi(vi, o), a = e.parentId, d = t.get(a);
  if (!d) {
    console.warn(`Parent node ${a} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Lh(e, n), r && !d.parentId && d.internals.rootParentIndex === void 0 && l === "auto" && (d.internals.rootParentIndex = ++r.i, d.internals.z = d.internals.z + r.i * Th), r && d.internals.rootParentIndex !== void 0 && (r.i = d.internals.rootParentIndex);
  const f = i && !Si(l) ? cc : 0, { x: h, y: p, z: y } = Hh(e, d, s, c, f, l), { positionAbsolute: x } = e.internals, v = h !== x.x || p !== x.y;
  (v || y !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: v ? { x: h, y: p } : x,
      z: y
    }
  });
}
function lc(e, t, n) {
  const o = qe(e.zIndex) ? e.zIndex : 0;
  return Si(n) ? o : o + (e.selected ? t : 0);
}
function Hh(e, t, n, o, r, i) {
  const { x: s, y: c } = t.internals.positionAbsolute, l = it(e), a = Hn(e, n), d = Ct(e.extent) ? Et(a, e.extent, l) : a;
  let f = Et({ x: s + d.x, y: c + d.y }, o, l);
  e.extent === "parent" && (f = Ka(f, l, t));
  const h = lc(e, r, i), p = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: p >= h ? p + 1 : h
  };
}
function Ei(e, t, n, o = [0, 0]) {
  const r = [], i = /* @__PURE__ */ new Map();
  for (const s of e) {
    const c = t.get(s.parentId);
    if (!c)
      continue;
    const l = i.get(s.parentId)?.expandedRect ?? Ot(c), a = Ua(l, s.rect);
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
function Vh(e, t, n, o, r, i, s) {
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
    const x = wi(p.nodeElement), v = y.measured.width !== x.width || y.measured.height !== x.height;
    if (!!(x.width && x.height && (v || !y.internals.handleBounds || p.force))) {
      const b = p.nodeElement.getBoundingClientRect(), g = Ct(y.extent) ? y.extent : i;
      let { positionAbsolute: w } = y.internals;
      y.parentId && y.extent === "parent" ? w = Ka(w, x, t.get(y.parentId)) : g && (w = Et(w, g, x));
      const C = {
        ...y,
        measured: x,
        internals: {
          ...y.internals,
          positionAbsolute: w,
          handleBounds: {
            source: rs("source", p.nodeElement, b, f, y.id),
            target: rs("target", p.nodeElement, b, f, y.id)
          }
        }
      };
      t.set(y.id, C), y.parentId && Ni(C, t, n, { nodeOrigin: r, zIndexMode: s }), l = !0, v && (a.push({
        id: y.id,
        type: "dimensions",
        dimensions: x
      }), y.expandParent && y.parentId && h.push({
        id: y.id,
        parentId: y.parentId,
        rect: Ot(C, r)
      }));
    }
  }
  if (h.length > 0) {
    const p = Ei(h, t, n, r);
    a.push(...p);
  }
  return { changes: a, updatedInternals: l };
}
async function Oh({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: i }) {
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
function ds(e, t, n, o, r, i) {
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
function uc(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: i, sourceHandle: s = null, targetHandle: c = null } = o, l = { edgeId: o.id, source: r, target: i, sourceHandle: s, targetHandle: c }, a = `${r}-${s}--${i}-${c}`, d = `${i}-${c}--${r}-${s}`;
    ds("source", l, d, e, r, s), ds("target", l, a, e, i, c), t.set(o.id, o);
  }
}
function dc(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : dc(n, t) : !1;
}
function fs(e, t, n) {
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
function Bh(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [i, s] of e)
    if ((s.selected || s.id === o) && (!s.parentId || !dc(s, e)) && (s.draggable || t && typeof s.draggable > "u")) {
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
function Ar({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
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
function Fh({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const i = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, s = On(i, t);
  return {
    x: s.x - i.x,
    y: s.y - i.y
  };
}
function Wh({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let i = { x: null, y: null }, s = 0, c = /* @__PURE__ */ new Map(), l = !1, a = { x: 0, y: 0 }, d = null, f = !1, h = null, p = !1, y = !1, x = null;
  function v({ noDragClassName: b, handleSelector: g, domNode: w, isSelectable: C, nodeId: E, nodeClickDistance: k = 0 }) {
    h = Re(w);
    function D({ x: z, y: H }) {
      const { nodeLookup: S, nodeExtent: I, snapGrid: _, snapToGrid: A, nodeOrigin: $, onNodeDrag: P, onSelectionDrag: W, onError: O, updateNodePositions: B } = t();
      i = { x: z, y: H };
      let G = !1;
      const Y = c.size > 1, te = Y && I ? Gr(Vn(c)) : null, ae = Y && A ? Fh({
        dragItems: c,
        snapGrid: _,
        x: z,
        y: H
      }) : null;
      for (const [Q, R] of c) {
        if (!S.has(Q))
          continue;
        let q = { x: z - R.distance.x, y: H - R.distance.y };
        A && (q = ae ? {
          x: Math.round(q.x + ae.x),
          y: Math.round(q.y + ae.y)
        } : On(q, _));
        let re = null;
        if (Y && I && !R.extent && te) {
          const { positionAbsolute: J } = R.internals, ie = J.x - te.x + I[0][0], L = J.x + R.measured.width - te.x2 + I[1][0], Z = J.y - te.y + I[0][1], fe = J.y + R.measured.height - te.y2 + I[1][1];
          re = [
            [ie, Z],
            [L, fe]
          ];
        }
        const { position: oe, positionAbsolute: K } = Za({
          nodeId: Q,
          nextPosition: q,
          nodeLookup: S,
          nodeExtent: re || I,
          nodeOrigin: $,
          onError: O
        });
        G = G || R.position.x !== oe.x || R.position.y !== oe.y, R.position = oe, R.internals.positionAbsolute = K;
      }
      if (y = y || G, !!G && (B(c, !0), x && (o || P || !E && W))) {
        const [Q, R] = Ar({
          nodeId: E,
          dragItems: c,
          nodeLookup: S
        });
        o?.(x, c, Q, R), P?.(x, Q, R), E || W?.(x, R);
      }
    }
    async function T() {
      if (!d)
        return;
      const { transform: z, panBy: H, autoPanSpeed: S, autoPanOnNodeDrag: I } = t();
      if (!I) {
        l = !1, cancelAnimationFrame(s);
        return;
      }
      const [_, A] = mi(a, d, S);
      (_ !== 0 || A !== 0) && (i.x = (i.x ?? 0) - _ / z[2], i.y = (i.y ?? 0) - A / z[2], await H({ x: _, y: A }) && D(i)), s = requestAnimationFrame(T);
    }
    function F(z) {
      const { nodeLookup: H, multiSelectionActive: S, nodesDraggable: I, transform: _, snapGrid: A, snapToGrid: $, selectNodesOnDrag: P, onNodeDragStart: W, onSelectionDragStart: O, unselectNodesAndEdges: B } = t();
      f = !0, (!P || !C) && !S && E && (H.get(E)?.selected || B()), C && P && E && e?.(E);
      const G = Cn(z.sourceEvent, { transform: _, snapGrid: A, snapToGrid: $, containerBounds: d });
      if (i = G, c = Bh(H, I, G, E), c.size > 0 && (n || W || !E && O)) {
        const [Y, te] = Ar({
          nodeId: E,
          dragItems: c,
          nodeLookup: H
        });
        n?.(z.sourceEvent, c, Y, te), W?.(z.sourceEvent, Y, te), E || O?.(z.sourceEvent, te);
      }
    }
    const j = ka().clickDistance(k).on("start", (z) => {
      const { domNode: H, nodeDragThreshold: S, transform: I, snapGrid: _, snapToGrid: A } = t();
      d = H?.getBoundingClientRect() || null, p = !1, y = !1, x = z.sourceEvent, S === 0 && F(z), i = Cn(z.sourceEvent, { transform: I, snapGrid: _, snapToGrid: A, containerBounds: d }), a = Ye(z.sourceEvent, d);
    }).on("drag", (z) => {
      const { autoPanOnNodeDrag: H, transform: S, snapGrid: I, snapToGrid: _, nodeDragThreshold: A, nodeLookup: $ } = t(), P = Cn(z.sourceEvent, { transform: S, snapGrid: I, snapToGrid: _, containerBounds: d });
      if (x = z.sourceEvent, (z.sourceEvent.type === "touchmove" && z.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      E && !$.has(E)) && (p = !0), !p) {
        if (!l && H && f && (l = !0, T()), !f) {
          const W = Ye(z.sourceEvent, d), O = W.x - a.x, B = W.y - a.y;
          Math.sqrt(O * O + B * B) > A && F(z);
        }
        (i.x !== P.xSnapped || i.y !== P.ySnapped) && c && f && (a = Ye(z.sourceEvent, d), D(P));
      }
    }).on("end", (z) => {
      if (!f || p) {
        p && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (l = !1, f = !1, cancelAnimationFrame(s), c.size > 0) {
        const { nodeLookup: H, updateNodePositions: S, onNodeDragStop: I, onSelectionDragStop: _ } = t();
        if (y && (S(c, !1), y = !1), r || I || !E && _) {
          const [A, $] = Ar({
            nodeId: E,
            dragItems: c,
            nodeLookup: H,
            dragging: !1
          });
          r?.(z.sourceEvent, c, A, $), I?.(z.sourceEvent, A, $), E || _?.(z.sourceEvent, $);
        }
      }
    }).filter((z) => {
      const H = z.target;
      return !z.button && (!b || !fs(H, `.${b}`, w)) && (!g || fs(H, g, w));
    });
    h.call(j);
  }
  function m() {
    h?.on(".drag", null);
  }
  return {
    update: v,
    destroy: m
  };
}
function Xh(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const i of t.values())
    Dn(r, Ot(i)) > 0 && o.push(i);
  return o;
}
const qh = 250;
function Yh(e, t, n, o) {
  let r = [], i = 1 / 0;
  const s = Xh(e, n, t + qh);
  for (const c of s) {
    const l = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const a of l) {
      if (o.nodeId === a.nodeId && o.type === a.type && o.id === a.id)
        continue;
      const { x: d, y: f } = _t(c, a, a.position, !0), h = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(f - e.y, 2));
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
function fc(e, t, n, o, r, i = !1) {
  const s = o.get(e);
  if (!s)
    return null;
  const c = r === "strict" ? s.internals.handleBounds?.[t] : [...s.internals.handleBounds?.source ?? [], ...s.internals.handleBounds?.target ?? []], l = (n ? c?.find((a) => a.id === n) : c?.[0]) ?? null;
  return l && i ? { ...l, ..._t(s, l, l.position, !0) } : l;
}
function hc(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Zh(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const pc = () => !0;
function Kh(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: i, isTarget: s, domNode: c, nodeLookup: l, lib: a, autoPanOnConnect: d, flowId: f, panBy: h, cancelConnection: p, onConnectStart: y, onConnect: x, onConnectEnd: v, isValidConnection: m = pc, onReconnectEnd: b, updateConnection: g, getTransform: w, getFromHandle: C, autoPanSpeed: E, dragThreshold: k = 1, handleDomNode: D }) {
  const T = ec(e.target);
  let F = 0, j;
  const { x: z, y: H } = Ye(e), S = hc(i, D), I = c?.getBoundingClientRect();
  let _ = !1;
  if (!I || !S)
    return;
  const A = fc(r, S, o, l, t);
  if (!A)
    return;
  let $ = Ye(e, I), P = !1, W = null, O = !1, B = null;
  function G() {
    if (!d || !I)
      return;
    const [oe, K] = mi($, I, E);
    h({ x: oe, y: K }), F = requestAnimationFrame(G);
  }
  const Y = {
    ...A,
    nodeId: r,
    type: S,
    position: A.position
  }, te = l.get(r);
  let Q = {
    inProgress: !0,
    isValid: null,
    from: _t(te, Y, ee.Left, !0),
    fromHandle: Y,
    fromPosition: Y.position,
    fromNode: te,
    to: $,
    toHandle: null,
    toPosition: es[Y.position],
    toNode: null,
    pointer: $
  };
  function R() {
    _ = !0, g(Q), y?.(e, { nodeId: r, handleId: o, handleType: S });
  }
  k === 0 && R();
  function q(oe) {
    if (!_) {
      const { x: fe, y: ue } = Ye(oe), Ie = fe - z, Oe = ue - H;
      if (!(Ie * Ie + Oe * Oe > k * k))
        return;
      R();
    }
    if (!C() || !Y) {
      re(oe);
      return;
    }
    const K = w();
    $ = Ye(oe, I), j = Yh(Kt($, K, !1, [1, 1]), n, l, Y), P || (G(), P = !0);
    const J = gc(oe, {
      handle: j,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: s ? "target" : "source",
      isValidConnection: m,
      doc: T,
      lib: a,
      flowId: f,
      nodeLookup: l
    });
    B = J.handleDomNode, W = J.connection, O = Zh(!!j, J.isValid);
    const ie = l.get(r), L = ie ? _t(ie, Y, ee.Left, !0) : Q.from, Z = {
      ...Q,
      from: L,
      isValid: O,
      to: J.toHandle && O ? Bt({ x: J.toHandle.x, y: J.toHandle.y }, K) : $,
      toHandle: J.toHandle,
      toPosition: O && J.toHandle ? J.toHandle.position : es[Y.position],
      toNode: J.toHandle ? l.get(J.toHandle.nodeId) : null,
      pointer: $
    };
    g(Z), Q = Z;
  }
  function re(oe) {
    if (!("touches" in oe && oe.touches.length > 0)) {
      if (_) {
        (j || B) && W && O && x?.(W);
        const { inProgress: K, ...J } = Q, ie = {
          ...J,
          toPosition: Q.toHandle ? Q.toPosition : null
        };
        v?.(oe, ie), i && b?.(oe, ie);
      }
      p(), cancelAnimationFrame(F), P = !1, O = !1, W = null, B = null, T.removeEventListener("mousemove", q), T.removeEventListener("mouseup", re), T.removeEventListener("touchmove", q), T.removeEventListener("touchend", re);
    }
  }
  T.addEventListener("mousemove", q), T.addEventListener("mouseup", re), T.addEventListener("touchmove", q), T.addEventListener("touchend", re);
}
function gc(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: i, doc: s, lib: c, flowId: l, isValidConnection: a = pc, nodeLookup: d }) {
  const f = i === "target", h = t ? s.querySelector(`.${c}-flow__handle[data-id="${l}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: p, y } = Ye(e), x = s.elementFromPoint(p, y), v = x?.classList.contains(`${c}-flow__handle`) ? x : h, m = {
    handleDomNode: v,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (v) {
    const b = hc(void 0, v), g = v.getAttribute("data-nodeid"), w = v.getAttribute("data-handleid"), C = v.classList.contains("connectable"), E = v.classList.contains("connectableend");
    if (!g || !b)
      return m;
    const k = {
      source: f ? g : o,
      sourceHandle: f ? w : r,
      target: f ? o : g,
      targetHandle: f ? r : w
    };
    m.connection = k;
    const T = C && E && (n === Ht.Strict ? f && b === "source" || !f && b === "target" : g !== o || w !== r);
    m.isValid = T && a(k), m.toHandle = fc(g, b, w, d, n, !0);
  }
  return m;
}
const ei = {
  onPointerDown: Kh,
  isValid: gc
};
function Uh({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = Re(e);
  function i({ translateExtent: c, width: l, height: a, zoomStep: d = 1, pannable: f = !0, zoomable: h = !0, inversePan: p = !1 }) {
    const y = (g) => {
      if (g.sourceEvent.type !== "wheel" || !t)
        return;
      const w = n(), C = g.sourceEvent.ctrlKey && Pn() ? 10 : 1, E = -g.sourceEvent.deltaY * (g.sourceEvent.deltaMode === 1 ? 0.05 : g.sourceEvent.deltaMode ? 1 : 2e-3) * d, k = w[2] * Math.pow(2, E * C);
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
      const C = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ], E = [C[0] - x[0], C[1] - x[1]];
      x = C;
      const k = o() * Math.max(w[2], Math.log(w[2])) * (p ? -1 : 1), D = {
        x: w[0] - E[0] * k,
        y: w[1] - E[1] * k
      }, T = [
        [0, 0],
        [l, a]
      ];
      t.setViewportConstrained({
        x: D.x,
        y: D.y,
        zoom: w[2]
      }, T, c);
    }, b = Ba().on("start", v).on("zoom", f ? m : null).on("zoom.wheel", h ? y : null);
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
const qo = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), Mr = ({ x: e, y: t, zoom: n }) => Fo.translate(e, t).scale(n), Pt = (e, t) => e.target.closest(`.${t}`), yc = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Gh = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, Dr = (e, t = 0, n = Gh, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, mc = (e) => {
  const t = e.ctrlKey && Pn() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Qh({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: i, zoomOnPinch: s, onPanZoomStart: c, onPanZoom: l, onPanZoomEnd: a }) {
  return (d) => {
    if (Pt(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && s) {
      const v = We(d), m = mc(d), b = f * Math.pow(2, m);
      o.scaleTo(n, b, v, d);
      return;
    }
    const h = d.deltaMode === 1 ? 20 : 1;
    let p = r === bt.Vertical ? 0 : d.deltaX * h, y = r === bt.Horizontal ? 0 : d.deltaY * h;
    !Pn() && d.shiftKey && r !== bt.Vertical && (p = d.deltaY * h, y = 0), o.translateBy(
      n,
      -(p / f) * i,
      -(y / f) * i,
      // @ts-ignore
      { internal: !0 }
    );
    const x = qo(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (l?.(d, x), e.panScrollTimeout = setTimeout(() => {
      a?.(d, x), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(d, x));
  };
}
function Jh({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const i = o.type === "wheel", s = !t && i && !o.ctrlKey, c = Pt(o, e);
    if (o.ctrlKey && i && c && o.preventDefault(), s || c)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function ep({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = qo(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function tp({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (i) => {
    e.usedRightMouseButton = !!(n && yc(t, e.mouseButton ?? 0)), i.sourceEvent?.sync || o([i.transform.x, i.transform.y, i.transform.k]), r && !i.sourceEvent?.internal && r?.(i.sourceEvent, qo(i.transform));
  };
}
function np({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: i }) {
  return (s) => {
    if (!s.sourceEvent?.internal && (e.isZoomingOrPanning = !1, i && yc(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && s.sourceEvent && i(s.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
      const c = qo(s.transform);
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
function op({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: i, userSelectionActive: s, noWheelClassName: c, noPanClassName: l, lib: a, connectionInProgress: d }) {
  return (f) => {
    const h = e || t, p = n && f.ctrlKey, y = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (Pt(f, `${a}-flow__node`) || Pt(f, `${a}-flow__edge`)))
      return !0;
    if (!o && !h && !r && !i && !n || s || d && !y || Pt(f, c) && y || Pt(f, l) && (!y || r && y && !e) || !n && f.ctrlKey && y)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!h && !r && !p && y || !o && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(o) && !o.includes(f.button) && f.type === "mousedown")
      return !1;
    const x = Array.isArray(o) && o.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || y) && x;
  };
}
function rp({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: i, onPanZoomStart: s, onPanZoomEnd: c, onDraggingChange: l }) {
  const a = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = Ba().scaleExtent([t, n]).translateExtent(o), h = Re(e).call(f);
  b({
    x: r.x,
    y: r.y,
    zoom: Vt(r.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], o);
  const p = h.on("wheel.zoom"), y = h.on("dblclick.zoom");
  f.wheelDelta(mc);
  async function x(j, z) {
    return h ? new Promise((H) => {
      f?.interpolate(z?.interpolate === "linear" ? En : yo).transform(Dr(h, z?.duration, z?.ease, () => H(!0)), j);
    }) : !1;
  }
  function v({ noWheelClassName: j, noPanClassName: z, onPaneContextMenu: H, userSelectionActive: S, panOnScroll: I, panOnDrag: _, panOnScrollMode: A, panOnScrollSpeed: $, preventScrolling: P, zoomOnPinch: W, zoomOnScroll: O, zoomOnDoubleClick: B, zoomActivationKeyPressed: G, lib: Y, onTransformChange: te, connectionInProgress: ae, paneClickDistance: Q, selectionOnDrag: R }) {
    S && !a.isZoomingOrPanning && m();
    const q = I && !G && !S;
    f.clickDistance(R ? 1 / 0 : !qe(Q) || Q < 0 ? 0 : Q);
    const re = q ? Qh({
      zoomPanValues: a,
      noWheelClassName: j,
      d3Selection: h,
      d3Zoom: f,
      panOnScrollMode: A,
      panOnScrollSpeed: $,
      zoomOnPinch: W,
      onPanZoomStart: s,
      onPanZoom: i,
      onPanZoomEnd: c
    }) : Jh({
      noWheelClassName: j,
      preventScrolling: P,
      d3ZoomHandler: p
    });
    h.on("wheel.zoom", re, { passive: !1 });
    const oe = ep({
      zoomPanValues: a,
      onDraggingChange: l,
      onPanZoomStart: s
    });
    f.on("start", oe);
    const K = tp({
      zoomPanValues: a,
      panOnDrag: _,
      onPaneContextMenu: !!H,
      onPanZoom: i,
      onTransformChange: te
    });
    f.on("zoom", K);
    const J = np({
      zoomPanValues: a,
      panOnDrag: _,
      panOnScroll: I,
      onPaneContextMenu: H,
      onPanZoomEnd: c,
      onDraggingChange: l
    });
    f.on("end", J);
    const ie = op({
      zoomActivationKeyPressed: G,
      panOnDrag: _,
      zoomOnScroll: O,
      panOnScroll: I,
      zoomOnDoubleClick: B,
      zoomOnPinch: W,
      userSelectionActive: S,
      noPanClassName: z,
      noWheelClassName: j,
      lib: Y,
      connectionInProgress: ae
    });
    f.filter(ie), B ? h.on("dblclick.zoom", y) : h.on("dblclick.zoom", null);
  }
  function m() {
    f.on("zoom", null);
  }
  async function b(j, z, H) {
    const S = Mr(j), I = f?.constrain()(S, z, H);
    return I && await x(I), I;
  }
  async function g(j, z) {
    const H = Mr(j);
    return await x(H, z), H;
  }
  function w(j) {
    if (h) {
      const z = Mr(j), H = h.property("__zoom");
      (H.k !== j.zoom || H.x !== j.x || H.y !== j.y) && f?.transform(h, z, null, { sync: !0 });
    }
  }
  function C() {
    const j = h ? Oa(h.node()) : { x: 0, y: 0, k: 1 };
    return { x: j.x, y: j.y, zoom: j.k };
  }
  async function E(j, z) {
    return h ? new Promise((H) => {
      f?.interpolate(z?.interpolate === "linear" ? En : yo).scaleTo(Dr(h, z?.duration, z?.ease, () => H(!0)), j);
    }) : !1;
  }
  async function k(j, z) {
    return h ? new Promise((H) => {
      f?.interpolate(z?.interpolate === "linear" ? En : yo).scaleBy(Dr(h, z?.duration, z?.ease, () => H(!0)), j);
    }) : !1;
  }
  function D(j) {
    f?.scaleExtent(j);
  }
  function T(j) {
    f?.translateExtent(j);
  }
  function F(j) {
    const z = !qe(j) || j < 0 ? 0 : j;
    f?.clickDistance(z);
  }
  return {
    update: v,
    destroy: m,
    setViewport: g,
    setViewportConstrained: b,
    getViewport: C,
    scaleTo: E,
    scaleBy: k,
    setScaleExtent: D,
    setTranslateExtent: T,
    syncViewport: w,
    setClickDistance: F
  };
}
var Ft;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(Ft || (Ft = {}));
function ip({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: i }) {
  const s = e - t, c = n - o, l = [s > 0 ? 1 : s < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return s && r && (l[0] = l[0] * -1), c && i && (l[1] = l[1] * -1), l;
}
function hs(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: r
  };
}
function at(e, t) {
  return Math.max(0, t - e);
}
function ct(e, t) {
  return Math.max(0, e - t);
}
function lo(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function ps(e, t) {
  return e ? !t : t;
}
function sp(e, t, n, o, r, i, s, c) {
  let { affectsX: l, affectsY: a } = t;
  const { isHorizontal: d, isVertical: f } = t, h = d && f, { xSnapped: p, ySnapped: y } = n, { minWidth: x, maxWidth: v, minHeight: m, maxHeight: b } = o, { x: g, y: w, width: C, height: E, aspectRatio: k } = e;
  let D = Math.floor(d ? p - e.pointerX : 0), T = Math.floor(f ? y - e.pointerY : 0);
  const F = C + (l ? -D : D), j = E + (a ? -T : T), z = -i[0] * C, H = -i[1] * E;
  let S = lo(F, x, v), I = lo(j, m, b);
  if (s) {
    let $ = 0, P = 0;
    l && D < 0 ? $ = at(g + D + z, s[0][0]) : !l && D > 0 && ($ = ct(g + F + z, s[1][0])), a && T < 0 ? P = at(w + T + H, s[0][1]) : !a && T > 0 && (P = ct(w + j + H, s[1][1])), S = Math.max(S, $), I = Math.max(I, P);
  }
  if (c) {
    let $ = 0, P = 0;
    l && D > 0 ? $ = ct(g + D, c[0][0]) : !l && D < 0 && ($ = at(g + F, c[1][0])), a && T > 0 ? P = ct(w + T, c[0][1]) : !a && T < 0 && (P = at(w + j, c[1][1])), S = Math.max(S, $), I = Math.max(I, P);
  }
  if (r) {
    if (d) {
      const $ = lo(F / k, m, b) * k;
      if (S = Math.max(S, $), s) {
        let P = 0;
        !l && !a || l && !a && h ? P = ct(w + H + F / k, s[1][1]) * k : P = at(w + H + (l ? D : -D) / k, s[0][1]) * k, S = Math.max(S, P);
      }
      if (c) {
        let P = 0;
        !l && !a || l && !a && h ? P = at(w + F / k, c[1][1]) * k : P = ct(w + (l ? D : -D) / k, c[0][1]) * k, S = Math.max(S, P);
      }
    }
    if (f) {
      const $ = lo(j * k, x, v) / k;
      if (I = Math.max(I, $), s) {
        let P = 0;
        !l && !a || a && !l && h ? P = ct(g + j * k + z, s[1][0]) / k : P = at(g + (a ? T : -T) * k + z, s[0][0]) / k, I = Math.max(I, P);
      }
      if (c) {
        let P = 0;
        !l && !a || a && !l && h ? P = at(g + j * k, c[1][0]) / k : P = ct(g + (a ? T : -T) * k, c[0][0]) / k, I = Math.max(I, P);
      }
    }
  }
  T = T + (T < 0 ? I : -I), D = D + (D < 0 ? S : -S), r && (h ? F > j * k ? T = (ps(l, a) ? -D : D) / k : D = (ps(l, a) ? -T : T) * k : d ? (T = D / k, a = l) : (D = T * k, l = a));
  const _ = l ? g + D : g, A = a ? w + T : w;
  return {
    width: C + (l ? -D : D),
    height: E + (a ? -T : T),
    x: i[0] * D * (l ? -1 : 1) + _,
    y: i[1] * T * (a ? -1 : 1) + A
  };
}
const xc = { width: 0, height: 0, x: 0, y: 0 }, ap = {
  ...xc,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function cp(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, i = e.measured.width ?? 0, s = e.measured.height ?? 0, c = n[0] * i, l = n[1] * s;
  return [
    [o - c, r - l],
    [o + i - c, r + s - l]
  ];
}
function lp({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const i = Re(e);
  let s = {
    controlDirection: hs("bottom-right"),
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
    let m = { ...xc }, b = { ...ap };
    s = {
      boundaries: d,
      resizeDirection: h,
      keepAspectRatio: f,
      controlDirection: hs(a)
    };
    let g, w = null, C = [], E, k, D, T = !1;
    const F = ka().on("start", (j) => {
      const { nodeLookup: z, transform: H, snapGrid: S, snapToGrid: I, nodeOrigin: _, paneDomNode: A } = n();
      if (g = z.get(t), !g)
        return;
      w = A?.getBoundingClientRect() ?? null;
      const { xSnapped: $, ySnapped: P } = Cn(j.sourceEvent, {
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
        pointerX: $,
        pointerY: P,
        aspectRatio: m.width / m.height
      }, E = void 0, k = Ct(g.extent) ? g.extent : void 0, g.parentId && (g.extent === "parent" || g.expandParent) && (E = z.get(g.parentId)), E && g.extent === "parent" && (k = [
        [0, 0],
        [E.measured.width, E.measured.height]
      ]), C = [], D = void 0;
      for (const [W, O] of z)
        if (O.parentId === t && (C.push({
          id: W,
          position: { ...O.position },
          extent: O.extent
        }), O.extent === "parent" || O.expandParent)) {
          const B = cp(O, g, O.origin ?? _);
          D ? D = [
            [Math.min(B[0][0], D[0][0]), Math.min(B[0][1], D[0][1])],
            [Math.max(B[1][0], D[1][0]), Math.max(B[1][1], D[1][1])]
          ] : D = B;
        }
      p?.(j, { ...m });
    }).on("drag", (j) => {
      const { transform: z, snapGrid: H, snapToGrid: S, nodeOrigin: I } = n(), _ = Cn(j.sourceEvent, {
        transform: z,
        snapGrid: H,
        snapToGrid: S,
        containerBounds: w
      }), A = [];
      if (!g)
        return;
      const { x: $, y: P, width: W, height: O } = m, B = {}, G = g.origin ?? I, { width: Y, height: te, x: ae, y: Q } = sp(b, s.controlDirection, _, s.boundaries, s.keepAspectRatio, G, k, D), R = Y !== W, q = te !== O, re = ae !== $ && R, oe = Q !== P && q;
      if (!re && !oe && !R && !q)
        return;
      if ((re || oe || G[0] === 1 || G[1] === 1) && (B.x = re ? ae : m.x, B.y = oe ? Q : m.y, m.x = B.x, m.y = B.y, C.length > 0)) {
        const L = ae - $, Z = Q - P;
        for (const fe of C)
          fe.position = {
            x: fe.position.x - L + G[0] * (Y - W),
            y: fe.position.y - Z + G[1] * (te - O)
          }, A.push(fe);
      }
      if ((R || q) && (B.width = R && (!s.resizeDirection || s.resizeDirection === "horizontal") ? Y : m.width, B.height = q && (!s.resizeDirection || s.resizeDirection === "vertical") ? te : m.height, m.width = B.width, m.height = B.height), E && g.expandParent) {
        const L = G[0] * (B.width ?? 0);
        B.x && B.x < L && (m.x = L, b.x = b.x - (B.x - L));
        const Z = G[1] * (B.height ?? 0);
        B.y && B.y < Z && (m.y = Z, b.y = b.y - (B.y - Z));
      }
      const K = ip({
        width: m.width,
        prevWidth: W,
        height: m.height,
        prevHeight: O,
        affectsX: s.controlDirection.affectsX,
        affectsY: s.controlDirection.affectsY
      }), J = { ...m, direction: K };
      v?.(j, J) !== !1 && (T = !0, y?.(j, J), o(B, A));
    }).on("end", (j) => {
      T && (x?.(j, { ...m }), r?.({ ...m }), T = !1);
    });
    i.call(F);
  }
  function l() {
    i.on(".drag", null);
  }
  return {
    update: c,
    destroy: l
  };
}
var Pr = { exports: {} }, Tr = {}, $r = { exports: {} }, zr = {};
var gs;
function up() {
  if (gs) return zr;
  gs = 1;
  var e = dt;
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
  return zr.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : d, zr;
}
var ys;
function dp() {
  return ys || (ys = 1, $r.exports = up()), $r.exports;
}
var ms;
function fp() {
  if (ms) return Tr;
  ms = 1;
  var e = dt, t = dp();
  function n(a, d) {
    return a === d && (a !== 0 || 1 / a === 1 / d) || a !== a && d !== d;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, i = e.useRef, s = e.useEffect, c = e.useMemo, l = e.useDebugValue;
  return Tr.useSyncExternalStoreWithSelector = function(a, d, f, h, p) {
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
        var b = !1, g, w, C = f === void 0 ? null : f;
        return [
          function() {
            return m(d());
          },
          C === null ? void 0 : function() {
            return m(C());
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
  }, Tr;
}
var xs;
function hp() {
  return xs || (xs = 1, Pr.exports = fp()), Pr.exports;
}
var pp = hp();
const gp = /* @__PURE__ */ Al(pp), yp = {}, ws = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (d, f) => {
    const h = typeof d == "function" ? d(t) : d;
    if (!Object.is(h, t)) {
      const p = t;
      t = f ?? (typeof h != "object" || h === null) ? h : Object.assign({}, t, h), n.forEach((y) => y(t, p));
    }
  }, r = () => t, l = { setState: o, getState: r, getInitialState: () => a, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (yp ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, a = t = e(o, r, l);
  return l;
}, mp = (e) => e ? ws(e) : ws, { useDebugValue: xp } = dt, { useSyncExternalStoreWithSelector: wp } = gp, vp = (e) => e;
function wc(e, t = vp, n) {
  const o = wp(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return xp(o), o;
}
const vs = (e, t) => {
  const n = mp(e), o = (r, i = t) => wc(n, r, i);
  return Object.assign(o, n), o;
}, bp = (e, t) => e ? vs(e, t) : vs;
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
var Rr = { exports: {} }, Me = {};
var bs;
function Sp() {
  if (bs) return Me;
  bs = 1;
  var e = dt;
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
var Ss;
function Np() {
  if (Ss) return Rr.exports;
  Ss = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), Rr.exports = Sp(), Rr.exports;
}
var Ep = Np();
const Yo = ai(null), Cp = Yo.Provider, vc = He.error001("react");
function de(e, t) {
  const n = zn(Yo);
  if (n === null)
    throw new Error(vc);
  return wc(n, e, t);
}
function xe() {
  const e = zn(Yo);
  if (e === null)
    throw new Error(vc);
  return ye(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const Ns = { display: "none" }, _p = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, bc = "react-flow__node-desc", Sc = "react-flow__edge-desc", kp = "react-flow__aria-live", Ip = (e) => e.ariaLiveMessage, jp = (e) => e.ariaLabelConfig;
function Ap({ rfId: e }) {
  const t = de(Ip);
  return u.jsx("div", { id: `${kp}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: _p, children: t });
}
function Mp({ rfId: e, disableKeyboardA11y: t }) {
  const n = de(jp);
  return u.jsxs(u.Fragment, { children: [u.jsx("div", { id: `${bc}-${e}`, style: Ns, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), u.jsx("div", { id: `${Sc}-${e}`, style: Ns, children: n["edge.a11yDescription.default"] }), !t && u.jsx(Ap, { rfId: e })] });
}
const Zo = Lo(({ position: e = "top-left", children: t, className: n, style: o, ...r }, i) => {
  const s = `${e}`.split("-");
  return u.jsx("div", { className: Ne(["react-flow__panel", n, ...s]), style: o, ref: i, ...r, children: t });
});
Zo.displayName = "Panel";
function Dp({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : u.jsx(Zo, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: u.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const Pp = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, uo = (e) => e.id;
function Tp(e, t) {
  return me(e.selectedNodes.map(uo), t.selectedNodes.map(uo)) && me(e.selectedEdges.map(uo), t.selectedEdges.map(uo));
}
function $p({ onSelectionChange: e }) {
  const t = xe(), { selectedNodes: n, selectedEdges: o } = de(Pp, Tp);
  return se(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((i) => i(r));
  }, [n, o, e]), null;
}
const zp = (e) => !!e.onSelectionChangeHandlers;
function Rp({ onSelectionChange: e }) {
  const t = de(zp);
  return e || t ? u.jsx($p, { onSelectionChange: e }) : null;
}
const Nc = [0, 0], Lp = { x: 0, y: 0, zoom: 1 }, Hp = [
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
], Es = [...Hp, "rfId"], Vp = (e) => ({
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
  translateExtent: An,
  nodeOrigin: Nc,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function Op(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: i, setNodeExtent: s, reset: c, setDefaultNodesAndEdges: l } = de(Vp, me), a = xe();
  se(() => (l(e.defaultNodes, e.defaultEdges), () => {
    d.current = Cs, c();
  }), []);
  const d = ce(Cs);
  return se(
    () => {
      for (const f of Es) {
        const h = e[f], p = d.current[f];
        h !== p && (typeof e[f] > "u" || (f === "nodes" ? t(h) : f === "edges" ? n(h) : f === "minZoom" ? o(h) : f === "maxZoom" ? r(h) : f === "translateExtent" ? i(h) : f === "nodeExtent" ? s(h) : f === "ariaLabelConfig" ? a.setState({ ariaLabelConfig: Sh(h) }) : f === "fitView" ? a.setState({ fitViewQueued: h }) : f === "fitViewOptions" ? a.setState({ fitViewOptions: h }) : a.setState({ [f]: h })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    Es.map((f) => e[f])
  ), null;
}
function _s() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function Bp(e) {
  const [t, n] = U(e === "system" ? null : e);
  return se(() => {
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
const ks = typeof document < "u" ? document : null;
function Tn(e = null, t = { target: ks, actInsideInputWithModifier: !0 }) {
  const [n, o] = U(!1), r = ce(!1), i = ce(/* @__PURE__ */ new Set([])), [s, c] = ye(() => {
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
    const l = t?.target ?? ks, a = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (p) => {
        if (r.current = p.ctrlKey || p.metaKey || p.shiftKey || p.altKey, (!r.current || r.current && !a) && tc(p))
          return !1;
        const x = js(p.code, c);
        if (i.current.add(p[x]), Is(s, i.current, !1)) {
          const v = p.composedPath?.()?.[0] || p.target, m = v?.nodeName === "BUTTON" || v?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !m) && p.preventDefault(), o(!0);
        }
      }, f = (p) => {
        const y = js(p.code, c);
        Is(s, i.current, !0) ? (o(!1), i.current.clear()) : i.current.delete(p[y]), p.key === "Meta" && i.current.clear(), r.current = !1;
      }, h = () => {
        i.current.clear(), o(!1);
      };
      return l?.addEventListener("keydown", d), l?.addEventListener("keyup", f), window.addEventListener("blur", h), window.addEventListener("contextmenu", h), () => {
        l?.removeEventListener("keydown", d), l?.removeEventListener("keyup", f), window.removeEventListener("blur", h), window.removeEventListener("contextmenu", h);
      };
    }
  }, [e, o]), n;
}
function Is(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function js(e, t) {
  return t.includes(e) ? "code" : "key";
}
const Fp = () => {
  const e = xe();
  return ye(() => ({
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
      const { width: o, height: r, minZoom: i, maxZoom: s, panZoom: c } = e.getState(), l = xi(t, o, r, i, s, n?.padding ?? 0.1);
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
      return Kt(a, o, f, d);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: r, y: i } = o.getBoundingClientRect(), s = Bt(t, n);
      return {
        x: s.x + r,
        y: s.y + i
      };
    }
  }), []);
};
function Ec(e, t) {
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
      Wp(l, c);
    n.push(c);
  }
  return r.length && r.forEach((i) => {
    i.index !== void 0 ? n.splice(i.index, 0, { ...i.item }) : n.push({ ...i.item });
  }), n;
}
function Wp(e, t) {
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
  return Ec(e, t);
}
function _c(e, t) {
  return Ec(e, t);
}
function xt(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function Tt(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [r, i] of e) {
    const s = t.has(r);
    !(i.selected === void 0 && !s) && i.selected !== s && (n && (i.selected = s), o.push(xt(i.id, s)));
  }
  return o;
}
function As({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((r) => [r.id, r]));
  for (const [r, i] of e.entries()) {
    const s = t.get(i.id), c = s?.internals?.userNode ?? s;
    c !== void 0 && c !== i && n.push({ id: i.id, item: i, type: "replace" }), c === void 0 && n.push({ item: i, type: "add", index: r });
  }
  for (const [r] of t)
    o.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function Ms(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const kc = Ga();
function Ic(e, t, n = {}) {
  return kh(e, t, {
    ...n,
    onError: n.onError ?? kc
  });
}
function Xp(e, t, n, o = { shouldReplaceId: !0 }) {
  return Ih(e, t, n, {
    ...o,
    onError: o.onError ?? kc
  });
}
const Ds = (e) => hh(e), qp = (e) => Ya(e);
function jc(e) {
  return Lo(e);
}
const Yp = typeof window < "u" ? jl : se;
function Ps(e) {
  const [t, n] = U(BigInt(0)), [o] = U(() => Zp(() => n((r) => r + BigInt(1))));
  return Yp(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function Zp(e) {
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
const Ac = ai(null);
function Kp({ children: e }) {
  const t = xe(), n = ge((c) => {
    const { nodes: l = [], setNodes: a, hasDefaultNodes: d, onNodesChange: f, nodeLookup: h, fitViewQueued: p, onNodesChangeMiddlewareMap: y } = t.getState();
    let x = l;
    for (const m of c)
      x = typeof m == "function" ? m(x) : m;
    let v = As({
      items: x,
      lookup: h
    });
    for (const m of y.values())
      v = m(v);
    d && a(x), v.length > 0 ? f?.(v) : p && window.requestAnimationFrame(() => {
      const { fitViewQueued: m, nodes: b, setNodes: g } = t.getState();
      m && g(b);
    });
  }, []), o = Ps(n), r = ge((c) => {
    const { edges: l = [], setEdges: a, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: h } = t.getState();
    let p = l;
    for (const y of c)
      p = typeof y == "function" ? y(p) : y;
    d ? a(p) : f && f(As({
      items: p,
      lookup: h
    }));
  }, []), i = Ps(r), s = ye(() => ({ nodeQueue: o, edgeQueue: i }), []);
  return u.jsx(Ac.Provider, { value: s, children: e });
}
function Up() {
  const e = zn(Ac);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Gp = (e) => !!e.panZoom;
function Ci() {
  const e = Fp(), t = xe(), n = Up(), o = de(Gp), r = ye(() => {
    const i = (f) => t.getState().nodeLookup.get(f), s = (f) => {
      n.nodeQueue.push(f);
    }, c = (f) => {
      n.edgeQueue.push(f);
    }, l = (f) => {
      const { nodeLookup: h, nodeOrigin: p } = t.getState(), y = Ds(f) ? f : h.get(f.id), x = y.parentId ? Ja(y.position, y.measured, y.parentId, h, p) : y.position, v = {
        ...y,
        position: x,
        width: y.measured?.width ?? y.width,
        height: y.measured?.height ?? y.height
      };
      return Ot(v);
    }, a = (f, h, p = { replace: !1 }) => {
      s((y) => y.map((x) => {
        if (x.id === f) {
          const v = typeof h == "function" ? h(x) : h;
          return p.replace && Ds(v) ? v : { ...x, ...v };
        }
        return x;
      }));
    }, d = (f, h, p = { replace: !1 }) => {
      c((y) => y.map((x) => {
        if (x.id === f) {
          const v = typeof h == "function" ? h(x) : h;
          return p.replace && qp(v) ? v : { ...x, ...v };
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
        const { nodes: p, edges: y, onNodesDelete: x, onEdgesDelete: v, triggerNodeChanges: m, triggerEdgeChanges: b, onDelete: g, onBeforeDelete: w } = t.getState(), { nodes: C, edges: E } = await xh({
          nodesToRemove: f,
          edgesToRemove: h,
          nodes: p,
          edges: y,
          onBeforeDelete: w
        }), k = E.length > 0, D = C.length > 0;
        if (k) {
          const T = E.map(Ms);
          v?.(E), b(T);
        }
        if (D) {
          const T = C.map(Ms);
          x?.(C), m(T);
        }
        return (D || k) && g?.({ nodes: C, edges: E }), { deletedNodes: C, deletedEdges: E };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, h = !0, p) => {
        const y = ns(f), x = y ? f : l(f), v = p !== void 0;
        return x ? (p || t.getState().nodes).filter((m) => {
          const b = t.getState().nodeLookup.get(m.id);
          if (b && !y && (m.id === f.id || !b.internals.positionAbsolute))
            return !1;
          const g = Ot(v ? m : b), w = Dn(g, x);
          return h && w > 0 || w >= g.width * g.height || w >= x.width * x.height;
        }) : [];
      },
      isNodeIntersecting: (f, h, p = !0) => {
        const x = ns(f) ? f : l(f);
        if (!x)
          return !1;
        const v = Dn(x, h);
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
        return ph(f, { nodeLookup: h, nodeOrigin: p });
      },
      getHandleConnections: ({ type: f, id: h, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}-${f}${h ? `-${h}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: h, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}${f ? h ? `-${f}-${h}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const h = t.getState().fitViewResolver ?? bh();
        return t.setState({ fitViewQueued: !0, fitViewOptions: f, fitViewResolver: h }), n.nodeQueue.push((p) => [...p]), h.promise;
      }
    };
  }, []);
  return ye(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const Ts = (e) => e.selected, Qp = typeof window < "u" ? window : void 0;
function Jp({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = xe(), { deleteElements: o } = Ci(), r = Tn(e, { actInsideInputWithModifier: !1 }), i = Tn(t, { target: Qp });
  se(() => {
    if (r) {
      const { edges: s, nodes: c } = n.getState();
      o({ nodes: c.filter(Ts), edges: s.filter(Ts) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), se(() => {
    n.setState({ multiSelectionActive: i });
  }, [i]);
}
function eg(e) {
  const t = xe();
  se(() => {
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
}, tg = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function ng({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: i = bt.Free, zoomOnDoubleClick: s = !0, panOnDrag: c = !0, defaultViewport: l, translateExtent: a, minZoom: d, maxZoom: f, zoomActivationKeyCode: h, preventScrolling: p = !0, children: y, noWheelClassName: x, noPanClassName: v, onViewportChange: m, isControlledViewport: b, paneClickDistance: g, selectionOnDrag: w }) {
  const C = xe(), E = ce(null), { userSelectionActive: k, lib: D, connectionInProgress: T } = de(tg, me), F = Tn(h), j = ce();
  eg(E);
  const z = ge((H) => {
    m?.({ x: H[0], y: H[1], zoom: H[2] }), b || C.setState({ transform: H });
  }, [m, b]);
  return se(() => {
    if (E.current) {
      j.current = rp({
        domNode: E.current,
        minZoom: d,
        maxZoom: f,
        translateExtent: a,
        viewport: l,
        onDraggingChange: (_) => C.setState((A) => A.paneDragging === _ ? A : { paneDragging: _ }),
        onPanZoomStart: (_, A) => {
          const { onViewportChangeStart: $, onMoveStart: P } = C.getState();
          P?.(_, A), $?.(A);
        },
        onPanZoom: (_, A) => {
          const { onViewportChange: $, onMove: P } = C.getState();
          P?.(_, A), $?.(A);
        },
        onPanZoomEnd: (_, A) => {
          const { onViewportChangeEnd: $, onMoveEnd: P } = C.getState();
          P?.(_, A), $?.(A);
        }
      });
      const { x: H, y: S, zoom: I } = j.current.getViewport();
      return C.setState({
        panZoom: j.current,
        transform: [H, S, I],
        domNode: E.current.closest(".react-flow")
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
      panOnDrag: c,
      zoomActivationKeyPressed: F,
      preventScrolling: p,
      noPanClassName: v,
      userSelectionActive: k,
      noWheelClassName: x,
      lib: D,
      onTransformChange: z,
      connectionInProgress: T,
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
    F,
    p,
    v,
    k,
    x,
    D,
    z,
    T,
    w,
    g
  ]), u.jsx("div", { className: "react-flow__renderer", ref: E, style: Ko, children: y });
}
const og = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function rg() {
  const { userSelectionActive: e, userSelectionRect: t } = de(og, me);
  return e && t ? u.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const Lr = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, ig = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function sg({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Mn.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: i, selectionOnDrag: s, onSelectionStart: c, onSelectionEnd: l, onPaneClick: a, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: h, onPaneMouseMove: p, onPaneMouseLeave: y, children: x }) {
  const v = ce(0), m = xe(), { userSelectionActive: b, elementsSelectable: g, dragging: w, connectionInProgress: C, panBy: E, autoPanSpeed: k } = de(ig, me), D = g && (e || b), T = ce(null), F = ce(), j = ce(/* @__PURE__ */ new Set()), z = ce(/* @__PURE__ */ new Set()), H = ce(!1), S = ce({ x: 0, y: 0 }), I = ce(!1), _ = (R) => {
    if (H.current || C) {
      H.current = !1;
      return;
    }
    a?.(R), m.getState().resetSelectedElements(), m.setState({ nodesSelectionActive: !1 });
  }, A = (R) => {
    if (Array.isArray(o) && o?.includes(2)) {
      R.preventDefault();
      return;
    }
    d?.(R);
  }, $ = f ? (R) => f(R) : void 0, P = (R) => {
    H.current && (R.stopPropagation(), H.current = !1);
  }, W = (R) => {
    const { domNode: q, transform: re } = m.getState();
    if (F.current = q?.getBoundingClientRect(), !F.current)
      return;
    const oe = R.target === T.current;
    if (!oe && !!R.target.closest(".nokey") || !e || !(s && oe || t) || R.button !== 0 || !R.isPrimary)
      return;
    R.target?.setPointerCapture?.(R.pointerId), H.current = !1;
    const { x: ie, y: L } = Ye(R.nativeEvent, F.current), Z = Kt({ x: ie, y: L }, re);
    m.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: Z.x,
        startY: Z.y,
        x: ie,
        y: L
      }
    }), oe || (R.stopPropagation(), R.preventDefault());
  };
  function O(R, q) {
    const { userSelectionRect: re } = m.getState();
    if (!re)
      return;
    const { transform: oe, nodeLookup: K, edgeLookup: J, connectionLookup: ie, triggerNodeChanges: L, triggerEdgeChanges: Z, defaultEdgeOptions: fe } = m.getState(), ue = { x: re.startX, y: re.startY }, { x: Ie, y: Oe } = Bt(ue, oe), Be = {
      startX: ue.x,
      startY: ue.y,
      x: R < Ie ? R : Ie,
      y: q < Oe ? q : Oe,
      width: Math.abs(R - Ie),
      height: Math.abs(q - Oe)
    }, ft = j.current, et = z.current;
    j.current = new Set(yi(K, Be, oe, n === Mn.Partial, !0).map((je) => je.id)), z.current = /* @__PURE__ */ new Set();
    const Ee = fe?.selectable ?? !0;
    for (const je of j.current) {
      const Pe = ie.get(je);
      if (Pe)
        for (const { edgeId: Te } of Pe.values()) {
          const Ke = J.get(Te);
          Ke && (Ke.selectable ?? Ee) && z.current.add(Te);
        }
    }
    if (!os(ft, j.current)) {
      const je = Tt(K, j.current, !0);
      L(je);
    }
    if (!os(et, z.current)) {
      const je = Tt(J, z.current);
      Z(je);
    }
    m.setState({
      userSelectionRect: Be,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function B() {
    if (!r || !F.current)
      return;
    const [R, q] = mi(S.current, F.current, k);
    E({ x: R, y: q }).then((re) => {
      if (!H.current || !re) {
        v.current = requestAnimationFrame(B);
        return;
      }
      const { x: oe, y: K } = S.current;
      O(oe, K), v.current = requestAnimationFrame(B);
    });
  }
  const G = () => {
    cancelAnimationFrame(v.current), v.current = 0, I.current = !1;
  };
  se(() => () => G(), []);
  const Y = (R) => {
    const { userSelectionRect: q, transform: re, resetSelectedElements: oe } = m.getState();
    if (!F.current || !q)
      return;
    const { x: K, y: J } = Ye(R.nativeEvent, F.current);
    S.current = { x: K, y: J };
    const ie = Bt({ x: q.startX, y: q.startY }, re);
    if (!H.current) {
      const L = t ? 0 : i;
      if (Math.hypot(K - ie.x, J - ie.y) <= L)
        return;
      oe(), c?.(R);
    }
    H.current = !0, I.current || (B(), I.current = !0), O(K, J);
  }, te = (R) => {
    R.button === 0 && (R.target?.releasePointerCapture?.(R.pointerId), !b && R.target === T.current && m.getState().userSelectionRect && _?.(R), m.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), H.current && (l?.(R), m.setState({
      nodesSelectionActive: j.current.size > 0
    })), G());
  }, ae = (R) => {
    R.target?.releasePointerCapture?.(R.pointerId), G();
  }, Q = o === !0 || Array.isArray(o) && o.includes(0);
  return u.jsxs("div", { className: Ne(["react-flow__pane", { draggable: Q, dragging: w, selection: e }]), onClick: D ? void 0 : Lr(_, T), onContextMenu: Lr(A, T), onWheel: Lr($, T), onPointerEnter: D ? void 0 : h, onPointerMove: D ? Y : p, onPointerUp: D ? te : void 0, onPointerCancel: D ? ae : void 0, onPointerDownCapture: D ? W : void 0, onClickCapture: D ? P : void 0, onPointerLeave: y, ref: T, style: Ko, children: [x, u.jsx(rg, {})] });
}
function ti({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: i, multiSelectionActive: s, nodeLookup: c, onError: l } = t.getState(), a = c.get(e);
  if (!a) {
    l?.("012", He.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), a.selected ? (n || a.selected && s) && (i({ nodes: [a], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function Mc({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: i, nodeClickDistance: s }) {
  const c = xe(), [l, a] = U(!1), d = ce();
  return se(() => {
    d.current = Wh({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (f) => {
        ti({
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
  }, [n, o, t, i, e, r, s]), l;
}
const ag = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function Dc() {
  const e = xe();
  return ge((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: i, nodesDraggable: s, onError: c, updateNodePositions: l, nodeLookup: a, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), h = ag(s), p = r ? i[0] : 5, y = r ? i[1] : 5, x = n.direction.x * p * n.factor, v = n.direction.y * y * n.factor;
    for (const [, m] of a) {
      if (!h(m))
        continue;
      let b = {
        x: m.internals.positionAbsolute.x + x,
        y: m.internals.positionAbsolute.y + v
      };
      r && (b = On(b, i));
      const { position: g, positionAbsolute: w } = Za({
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
const _i = ai(null), cg = _i.Provider;
_i.Consumer;
const Pc = () => zn(_i), lg = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), ug = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: i, connection: s } = o, { fromHandle: c, toHandle: l, isValid: a } = s, d = l?.nodeId === e && l?.id === t && l?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: d,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: i === Ht.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!r,
    valid: d && a
  };
};
function dg({ type: e = "source", position: t = ee.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: i = !0, id: s, onConnect: c, children: l, className: a, onMouseDown: d, onTouchStart: f, ...h }, p) {
  const y = s || null, x = e === "target", v = xe(), m = Pc(), { connectOnClick: b, noPanClassName: g, rfId: w } = de(lg, me), { connectingFrom: C, connectingTo: E, clickConnecting: k, isPossibleEndHandle: D, connectionInProcess: T, clickConnectionInProcess: F, valid: j } = de(ug(m, y, e), me);
  m || v.getState().onError?.("010", He.error010());
  const z = (I) => {
    const { defaultEdgeOptions: _, onConnect: A, hasDefaultEdges: $ } = v.getState(), P = {
      ..._,
      ...I
    };
    if ($) {
      const { edges: W, setEdges: O, onError: B } = v.getState();
      O(Ic(P, W, { onError: B }));
    }
    A?.(P), c?.(P);
  }, H = (I) => {
    if (!m)
      return;
    const _ = nc(I.nativeEvent);
    if (r && (_ && I.button === 0 || !_)) {
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
        handleId: y,
        nodeId: m,
        flowId: A.rfId,
        panBy: A.panBy,
        cancelConnection: A.cancelConnection,
        onConnectStart: A.onConnectStart,
        onConnectEnd: (...$) => v.getState().onConnectEnd?.(...$),
        updateConnection: A.updateConnection,
        onConnect: z,
        isValidConnection: n || ((...$) => v.getState().isValidConnection?.(...$) ?? !0),
        getTransform: () => v.getState().transform,
        getFromHandle: () => v.getState().connection.fromHandle,
        autoPanSpeed: A.autoPanSpeed,
        dragThreshold: A.connectionDragThreshold
      });
    }
    _ ? d?.(I) : f?.(I);
  }, S = (I) => {
    const { onClickConnectStart: _, onClickConnectEnd: A, connectionClickStartHandle: $, connectionMode: P, isValidConnection: W, lib: O, rfId: B, nodeLookup: G, connection: Y } = v.getState();
    if (!m || !$ && !r)
      return;
    if (!$) {
      _?.(I.nativeEvent, { nodeId: m, handleId: y, handleType: e }), v.setState({ connectionClickStartHandle: { nodeId: m, type: e, id: y } });
      return;
    }
    const te = ec(I.target), ae = n || W, { connection: Q, isValid: R } = ei.isValid(I.nativeEvent, {
      handle: {
        nodeId: m,
        id: y,
        type: e
      },
      connectionMode: P,
      fromNodeId: $.nodeId,
      fromHandleId: $.id || null,
      fromType: $.type,
      isValidConnection: ae,
      flowId: B,
      doc: te,
      lib: O,
      nodeLookup: G
    });
    R && Q && z(Q);
    const q = structuredClone(Y);
    delete q.inProgress, q.toPosition = q.toHandle ? q.toHandle.position : null, A?.(I, q), v.setState({ connectionClickStartHandle: null });
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
      connectingfrom: C,
      connectingto: E,
      valid: j,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!T || D) && (T || F ? i : r)
    }
  ]), onMouseDown: H, onTouchStart: H, onClick: b ? S : void 0, ref: p, ...h, children: l });
}
const Wt = ve(jc(dg));
function fg({ data: e, isConnectable: t, sourcePosition: n = ee.Bottom }) {
  return u.jsxs(u.Fragment, { children: [e?.label, u.jsx(Wt, { type: "source", position: n, isConnectable: t })] });
}
function hg({ data: e, isConnectable: t, targetPosition: n = ee.Top, sourcePosition: o = ee.Bottom }) {
  return u.jsxs(u.Fragment, { children: [u.jsx(Wt, { type: "target", position: n, isConnectable: t }), e?.label, u.jsx(Wt, { type: "source", position: o, isConnectable: t })] });
}
function pg() {
  return null;
}
function gg({ data: e, isConnectable: t, targetPosition: n = ee.Top }) {
  return u.jsxs(u.Fragment, { children: [u.jsx(Wt, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const Po = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, $s = {
  input: fg,
  default: hg,
  output: gg,
  group: pg
};
function yg(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const mg = (e) => {
  const { width: t, height: n, x: o, y: r } = Vn(e.nodeLookup, {
    filter: (i) => !!i.selected
  });
  return {
    width: qe(t) ? t : null,
    height: qe(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function xg({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = xe(), { width: r, height: i, transformString: s, userSelectionActive: c } = de(mg, me), l = Dc(), a = ce(null);
  se(() => {
    n || a.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !c && r !== null && i !== null;
  if (Mc({
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
const zs = typeof window < "u" ? window : void 0, wg = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function Tc({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, paneClickDistance: c, deleteKeyCode: l, selectionKeyCode: a, selectionOnDrag: d, selectionMode: f, onSelectionStart: h, onSelectionEnd: p, multiSelectionKeyCode: y, panActivationKeyCode: x, zoomActivationKeyCode: v, elementsSelectable: m, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: w, panOnScrollSpeed: C, panOnScrollMode: E, zoomOnDoubleClick: k, panOnDrag: D, autoPanOnSelection: T, defaultViewport: F, translateExtent: j, minZoom: z, maxZoom: H, preventScrolling: S, onSelectionContextMenu: I, noWheelClassName: _, noPanClassName: A, disableKeyboardA11y: $, onViewportChange: P, isControlledViewport: W }) {
  const { nodesSelectionActive: O, userSelectionActive: B } = de(wg, me), G = Tn(a, { target: zs }), Y = Tn(x, { target: zs }), te = Y || D, ae = Y || w, Q = d && te !== !0, R = G || B || Q;
  return Jp({ deleteKeyCode: l, multiSelectionKeyCode: y }), u.jsx(ng, { onPaneContextMenu: i, elementsSelectable: m, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: ae, panOnScrollSpeed: C, panOnScrollMode: E, zoomOnDoubleClick: k, panOnDrag: !G && te, defaultViewport: F, translateExtent: j, minZoom: z, maxZoom: H, zoomActivationKeyCode: v, preventScrolling: S, noWheelClassName: _, noPanClassName: A, onViewportChange: P, isControlledViewport: W, paneClickDistance: c, selectionOnDrag: Q, children: u.jsxs(sg, { onSelectionStart: h, onSelectionEnd: p, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, panOnDrag: te, autoPanOnSelection: T, isSelecting: !!R, selectionMode: f, selectionKeyPressed: G, paneClickDistance: c, selectionOnDrag: Q, children: [e, O && u.jsx(xg, { onSelectionContextMenu: I, noPanClassName: A, disableKeyboardA11y: $ })] }) });
}
Tc.displayName = "FlowRenderer";
const vg = ve(Tc), bg = (e) => (t) => e ? yi(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function Sg(e) {
  return de(ge(bg(e), [e]), me);
}
const Ng = (e) => e.updateNodeInternals;
function Eg() {
  const e = de(Ng), [t] = U(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
function Cg({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = xe(), i = ce(null), s = ce(null), c = ce(e.sourcePosition), l = ce(e.targetPosition), a = ce(t), d = n && !!e.internals.handleBounds;
  return se(() => {
    i.current && !e.hidden && (!d || s.current !== i.current) && (s.current && o?.unobserve(s.current), o?.observe(i.current), s.current = i.current);
  }, [d, e.hidden]), se(() => () => {
    s.current && (o?.unobserve(s.current), s.current = null);
  }, []), se(() => {
    if (i.current) {
      const f = a.current !== t, h = c.current !== e.sourcePosition, p = l.current !== e.targetPosition;
      (f || h || p) && (a.current = t, c.current = e.sourcePosition, l.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: i.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), i;
}
function _g({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: i, onDoubleClick: s, nodesDraggable: c, elementsSelectable: l, nodesConnectable: a, nodesFocusable: d, resizeObserver: f, noDragClassName: h, noPanClassName: p, disableKeyboardA11y: y, rfId: x, nodeTypes: v, nodeClickDistance: m, onError: b }) {
  const { node: g, internals: w, isParent: C } = de((R) => {
    const q = R.nodeLookup.get(e), re = R.parentLookup.has(e);
    return {
      node: q,
      internals: q.internals,
      isParent: re
    };
  }, me);
  let E = g.type || "default", k = v?.[E] || $s[E];
  k === void 0 && (b?.("003", He.error003(E)), E = "default", k = v?.default || $s.default);
  const D = !!(g.draggable || c && typeof g.draggable > "u"), T = !!(g.selectable || l && typeof g.selectable > "u"), F = !!(g.connectable || a && typeof g.connectable > "u"), j = !!(g.focusable || d && typeof g.focusable > "u"), z = xe(), H = Qa(g), S = Cg({ node: g, nodeType: E, hasDimensions: H, resizeObserver: f }), I = Mc({
    nodeRef: S,
    disabled: g.hidden || !D,
    noDragClassName: h,
    handleSelector: g.dragHandle,
    nodeId: e,
    isSelectable: T,
    nodeClickDistance: m
  }), _ = Dc();
  if (g.hidden)
    return null;
  const A = it(g), $ = yg(g), P = T || D || t || n || o || r, W = n ? (R) => n(R, { ...w.userNode }) : void 0, O = o ? (R) => o(R, { ...w.userNode }) : void 0, B = r ? (R) => r(R, { ...w.userNode }) : void 0, G = i ? (R) => i(R, { ...w.userNode }) : void 0, Y = s ? (R) => s(R, { ...w.userNode }) : void 0, te = (R) => {
    const { selectNodesOnDrag: q, nodeDragThreshold: re } = z.getState();
    T && (!q || !D || re > 0) && ti({
      id: e,
      store: z,
      nodeRef: S
    }), t && t(R, { ...w.userNode });
  }, ae = (R) => {
    if (!(tc(R.nativeEvent) || y)) {
      if (Fa.includes(R.key) && T) {
        const q = R.key === "Escape";
        ti({
          id: e,
          store: z,
          unselect: q,
          nodeRef: S
        });
      } else if (D && g.selected && Object.prototype.hasOwnProperty.call(Po, R.key)) {
        R.preventDefault();
        const { ariaLabelConfig: q } = z.getState();
        z.setState({
          ariaLiveMessage: q["node.a11yDescription.ariaLiveMessage"]({
            direction: R.key.replace("Arrow", "").toLowerCase(),
            x: ~~w.positionAbsolute.x,
            y: ~~w.positionAbsolute.y
          })
        }), _({
          direction: Po[R.key],
          factor: R.shiftKey ? 4 : 1
        });
      }
    }
  }, Q = () => {
    if (y || !S.current?.matches(":focus-visible"))
      return;
    const { transform: R, width: q, height: re, autoPanOnNodeFocus: oe, setCenter: K } = z.getState();
    if (!oe)
      return;
    yi(/* @__PURE__ */ new Map([[e, g]]), { x: 0, y: 0, width: q, height: re }, R, !0).length > 0 || K(g.position.x + A.width / 2, g.position.y + A.height / 2, {
      zoom: R[2]
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
      selectable: T,
      parent: C,
      draggable: D,
      dragging: I
    }
  ]), ref: S, style: {
    zIndex: w.z,
    transform: `translate(${w.positionAbsolute.x}px,${w.positionAbsolute.y}px)`,
    pointerEvents: P ? "all" : "none",
    visibility: H ? "visible" : "hidden",
    ...g.style,
    ...$
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: W, onMouseMove: O, onMouseLeave: B, onContextMenu: G, onClick: te, onDoubleClick: Y, onKeyDown: j ? ae : void 0, tabIndex: j ? 0 : void 0, onFocus: j ? Q : void 0, role: g.ariaRole ?? (j ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": y ? void 0 : `${bc}-${x}`, "aria-label": g.ariaLabel, ...g.domAttributes, children: u.jsx(cg, { value: e, children: u.jsx(k, { id: e, data: g.data, type: E, positionAbsoluteX: w.positionAbsolute.x, positionAbsoluteY: w.positionAbsolute.y, selected: g.selected ?? !1, selectable: T, draggable: D, deletable: g.deletable ?? !0, isConnectable: F, sourcePosition: g.sourcePosition, targetPosition: g.targetPosition, dragging: I, dragHandle: g.dragHandle, zIndex: w.z, parentId: g.parentId, ...A }) }) });
}
var kg = ve(_g);
const Ig = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function $c(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: i } = de(Ig, me), s = Sg(e.onlyRenderVisibleElements), c = Eg();
  return u.jsx("div", { className: "react-flow__nodes", style: Ko, children: s.map((l) => (
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
    u.jsx(kg, { id: l, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: i }, l)
  )) });
}
$c.displayName = "NodeRenderer";
const jg = ve($c);
function Ag(e) {
  return de(ge((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const i = n.nodeLookup.get(r.source), s = n.nodeLookup.get(r.target);
        i && s && Ch({
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
const Mg = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return u.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, Dg = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return u.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Rs = {
  [Ao.Arrow]: Mg,
  [Ao.ArrowClosed]: Dg
};
function Pg(e) {
  const t = xe();
  return ye(() => Object.prototype.hasOwnProperty.call(Rs, e) ? Rs[e] : (t.getState().onError?.("009", He.error009(e)), null), [e]);
}
const Tg = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: i = "strokeWidth", strokeWidth: s, orient: c = "auto-start-reverse" }) => {
  const l = Pg(t);
  return l ? u.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: i, orient: c, refX: "0", refY: "0", children: u.jsx(l, { color: n, strokeWidth: s }) }) : null;
}, zc = ({ defaultColor: e, rfId: t }) => {
  const n = de((i) => i.edges), o = de((i) => i.defaultEdgeOptions), r = ye(() => Ph(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? u.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: u.jsx("defs", { children: r.map((i) => u.jsx(Tg, { id: i.id, type: i.type, color: i.color, width: i.width, height: i.height, markerUnits: i.markerUnits, strokeWidth: i.strokeWidth, orient: i.orient }, i.id)) }) }) : null;
};
zc.displayName = "MarkerDefinitions";
var $g = ve(zc);
function Rc({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: i, labelBgPadding: s = [2, 4], labelBgBorderRadius: c = 2, children: l, className: a, ...d }) {
  const [f, h] = U({ x: 1, y: 0, width: 0, height: 0 }), p = Ne(["react-flow__edge-textwrapper", a]), y = ce(null);
  return se(() => {
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
Rc.displayName = "EdgeText";
const zg = ve(Rc);
function Bn({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: c, labelBgBorderRadius: l, interactionWidth: a = 20, ...d }) {
  return u.jsxs(u.Fragment, { children: [u.jsx("path", { ...d, d: e, fill: "none", className: Ne(["react-flow__edge-path", d.className]) }), a ? u.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: a, className: "react-flow__edge-interaction" }) : null, o && qe(t) && qe(n) ? u.jsx(zg, { x: t, y: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: c, labelBgBorderRadius: l }) : null] });
}
function Ls({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === ee.Left || e === ee.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function Lc({ sourceX: e, sourceY: t, sourcePosition: n = ee.Bottom, targetX: o, targetY: r, targetPosition: i = ee.Top }) {
  const [s, c] = Ls({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [l, a] = Ls({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [d, f, h, p] = oc({
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
function Hc(e) {
  return ve(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: c, label: l, labelStyle: a, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: y, markerEnd: x, markerStart: v, interactionWidth: m }) => {
    const [b, g, w] = Lc({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: c
    }), C = e.isInternal ? void 0 : t;
    return u.jsx(Bn, { id: C, path: b, labelX: g, labelY: w, label: l, labelStyle: a, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: y, markerEnd: x, markerStart: v, interactionWidth: m });
  });
}
const Rg = Hc({ isInternal: !1 }), Vc = Hc({ isInternal: !0 });
Rg.displayName = "SimpleBezierEdge";
Vc.displayName = "SimpleBezierEdgeInternal";
function Oc(e) {
  return ve(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: c, labelShowBg: l, labelBgStyle: a, labelBgPadding: d, labelBgBorderRadius: f, style: h, sourcePosition: p = ee.Bottom, targetPosition: y = ee.Top, markerEnd: x, markerStart: v, pathOptions: m, interactionWidth: b }) => {
    const [g, w, C] = Do({
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
    return u.jsx(Bn, { id: E, path: g, labelX: w, labelY: C, label: s, labelStyle: c, labelShowBg: l, labelBgStyle: a, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: x, markerStart: v, interactionWidth: b });
  });
}
const Bc = Oc({ isInternal: !1 }), Fc = Oc({ isInternal: !0 });
Bc.displayName = "SmoothStepEdge";
Fc.displayName = "SmoothStepEdgeInternal";
function Wc(e) {
  return ve(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return u.jsx(Bc, { ...n, id: o, pathOptions: ye(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Lg = Wc({ isInternal: !1 }), Xc = Wc({ isInternal: !0 });
Lg.displayName = "StepEdge";
Xc.displayName = "StepEdgeInternal";
function qc(e) {
  return ve(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: c, labelShowBg: l, labelBgStyle: a, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: p, markerStart: y, interactionWidth: x }) => {
    const [v, m, b] = ac({ sourceX: n, sourceY: o, targetX: r, targetY: i }), g = e.isInternal ? void 0 : t;
    return u.jsx(Bn, { id: g, path: v, labelX: m, labelY: b, label: s, labelStyle: c, labelShowBg: l, labelBgStyle: a, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: p, markerStart: y, interactionWidth: x });
  });
}
const Hg = qc({ isInternal: !1 }), Yc = qc({ isInternal: !0 });
Hg.displayName = "StraightEdge";
Yc.displayName = "StraightEdgeInternal";
function Zc(e) {
  return ve(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s = ee.Bottom, targetPosition: c = ee.Top, label: l, labelStyle: a, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: y, markerEnd: x, markerStart: v, pathOptions: m, interactionWidth: b }) => {
    const [g, w, C] = rc({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: c,
      curvature: m?.curvature
    }), E = e.isInternal ? void 0 : t;
    return u.jsx(Bn, { id: E, path: g, labelX: w, labelY: C, label: l, labelStyle: a, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: y, markerEnd: x, markerStart: v, interactionWidth: b });
  });
}
const Vg = Zc({ isInternal: !1 }), Kc = Zc({ isInternal: !0 });
Vg.displayName = "BezierEdge";
Kc.displayName = "BezierEdgeInternal";
const Hs = {
  default: Kc,
  straight: Yc,
  step: Xc,
  smoothstep: Fc,
  simplebezier: Vc
}, Vs = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, Og = (e, t, n) => n === ee.Left ? e - t : n === ee.Right ? e + t : e, Bg = (e, t, n) => n === ee.Top ? e - t : n === ee.Bottom ? e + t : e, Os = "react-flow__edgeupdater";
function Bs({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: i, onMouseOut: s, type: c }) {
  return u.jsx("circle", { onMouseDown: r, onMouseEnter: i, onMouseOut: s, className: Ne([Os, `${Os}-${c}`]), cx: Og(t, o, e), cy: Bg(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function Fg({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: i, targetY: s, sourcePosition: c, targetPosition: l, onReconnect: a, onReconnectStart: d, onReconnectEnd: f, setReconnecting: h, setUpdateHover: p }) {
  const y = xe(), x = (w, C) => {
    if (w.button !== 0)
      return;
    const { autoPanOnConnect: E, domNode: k, connectionMode: D, connectionRadius: T, lib: F, onConnectStart: j, cancelConnection: z, nodeLookup: H, rfId: S, panBy: I, updateConnection: _ } = y.getState(), A = C.type === "target", $ = (O, B) => {
      h(!1), f?.(O, n, C.type, B);
    }, P = (O) => a?.(n, O), W = (O, B) => {
      h(!0), d?.(w, n, C.type), j?.(O, B);
    };
    ei.onPointerDown(w.nativeEvent, {
      autoPanOnConnect: E,
      connectionMode: D,
      connectionRadius: T,
      domNode: k,
      handleId: C.id,
      nodeId: C.nodeId,
      nodeLookup: H,
      isTarget: A,
      edgeUpdaterType: C.type,
      lib: F,
      flowId: S,
      cancelConnection: z,
      panBy: I,
      isValidConnection: (...O) => y.getState().isValidConnection?.(...O) ?? !0,
      onConnect: P,
      onConnectStart: W,
      onConnectEnd: (...O) => y.getState().onConnectEnd?.(...O),
      onReconnectEnd: $,
      updateConnection: _,
      getTransform: () => y.getState().transform,
      getFromHandle: () => y.getState().connection.fromHandle,
      dragThreshold: y.getState().connectionDragThreshold,
      handleDomNode: w.currentTarget
    });
  }, v = (w) => x(w, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), m = (w) => x(w, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), b = () => p(!0), g = () => p(!1);
  return u.jsxs(u.Fragment, { children: [(e === !0 || e === "source") && u.jsx(Bs, { position: c, centerX: o, centerY: r, radius: t, onMouseDown: v, onMouseEnter: b, onMouseOut: g, type: "source" }), (e === !0 || e === "target") && u.jsx(Bs, { position: l, centerX: i, centerY: s, radius: t, onMouseDown: m, onMouseEnter: b, onMouseOut: g, type: "target" })] });
}
function Wg({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: i, onContextMenu: s, onMouseEnter: c, onMouseMove: l, onMouseLeave: a, reconnectRadius: d, onReconnect: f, onReconnectStart: h, onReconnectEnd: p, rfId: y, edgeTypes: x, noPanClassName: v, onError: m, disableKeyboardA11y: b }) {
  let g = de((K) => K.edgeLookup.get(e));
  const w = de((K) => K.defaultEdgeOptions);
  g = w ? { ...w, ...g } : g;
  let C = g.type || "default", E = x?.[C] || Hs[C];
  E === void 0 && (m?.("011", He.error011(C)), C = "default", E = x?.default || Hs.default);
  const k = !!(g.focusable || t && typeof g.focusable > "u"), D = typeof f < "u" && (g.reconnectable || n && typeof g.reconnectable > "u"), T = !!(g.selectable || o && typeof g.selectable > "u"), F = ce(null), [j, z] = U(!1), [H, S] = U(!1), I = xe(), { zIndex: _, sourceX: A, sourceY: $, targetX: P, targetY: W, sourcePosition: O, targetPosition: B } = de(ge((K) => {
    const J = K.nodeLookup.get(g.source), ie = K.nodeLookup.get(g.target);
    if (!J || !ie)
      return {
        zIndex: g.zIndex,
        ...Vs
      };
    const L = Dh({
      id: e,
      sourceNode: J,
      targetNode: ie,
      sourceHandle: g.sourceHandle || null,
      targetHandle: g.targetHandle || null,
      connectionMode: K.connectionMode,
      onError: m
    });
    return {
      zIndex: Eh({
        selected: g.selected,
        zIndex: g.zIndex,
        sourceNode: J,
        targetNode: ie,
        elevateOnSelect: K.elevateEdgesOnSelect,
        zIndexMode: K.zIndexMode
      }),
      ...L || Vs
    };
  }, [g.source, g.target, g.sourceHandle, g.targetHandle, g.selected, g.zIndex]), me), G = ye(() => g.markerStart ? `url('#${Qr(g.markerStart, y)}')` : void 0, [g.markerStart, y]), Y = ye(() => g.markerEnd ? `url('#${Qr(g.markerEnd, y)}')` : void 0, [g.markerEnd, y]);
  if (g.hidden || A === null || $ === null || P === null || W === null)
    return null;
  const te = (K) => {
    const { addSelectedEdges: J, unselectNodesAndEdges: ie, multiSelectionActive: L } = I.getState();
    T && (I.setState({ nodesSelectionActive: !1 }), g.selected && L ? (ie({ nodes: [], edges: [g] }), F.current?.blur()) : J([e])), r && r(K, g);
  }, ae = i ? (K) => {
    i(K, { ...g });
  } : void 0, Q = s ? (K) => {
    s(K, { ...g });
  } : void 0, R = c ? (K) => {
    c(K, { ...g });
  } : void 0, q = l ? (K) => {
    l(K, { ...g });
  } : void 0, re = a ? (K) => {
    a(K, { ...g });
  } : void 0, oe = (K) => {
    if (!b && Fa.includes(K.key) && T) {
      const { unselectNodesAndEdges: J, addSelectedEdges: ie } = I.getState();
      K.key === "Escape" ? (F.current?.blur(), J({ edges: [g] })) : ie([e]);
    }
  };
  return u.jsx("svg", { style: { zIndex: _ }, children: u.jsxs("g", { className: Ne([
    "react-flow__edge",
    `react-flow__edge-${C}`,
    g.className,
    v,
    {
      selected: g.selected,
      animated: g.animated,
      inactive: !T && !r,
      updating: j,
      selectable: T
    }
  ]), onClick: te, onDoubleClick: ae, onContextMenu: Q, onMouseEnter: R, onMouseMove: q, onMouseLeave: re, onKeyDown: k ? oe : void 0, tabIndex: k ? 0 : void 0, role: g.ariaRole ?? (k ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": g.ariaLabel === null ? void 0 : g.ariaLabel || `Edge from ${g.source} to ${g.target}`, "aria-describedby": k ? `${Sc}-${y}` : void 0, ref: F, ...g.domAttributes, children: [!H && u.jsx(E, { id: e, source: g.source, target: g.target, type: g.type, selected: g.selected, animated: g.animated, selectable: T, deletable: g.deletable ?? !0, label: g.label, labelStyle: g.labelStyle, labelShowBg: g.labelShowBg, labelBgStyle: g.labelBgStyle, labelBgPadding: g.labelBgPadding, labelBgBorderRadius: g.labelBgBorderRadius, sourceX: A, sourceY: $, targetX: P, targetY: W, sourcePosition: O, targetPosition: B, data: g.data, style: g.style, sourceHandleId: g.sourceHandle, targetHandleId: g.targetHandle, markerStart: G, markerEnd: Y, pathOptions: "pathOptions" in g ? g.pathOptions : void 0, interactionWidth: g.interactionWidth }), D && u.jsx(Fg, { edge: g, isReconnectable: D, reconnectRadius: d, onReconnect: f, onReconnectStart: h, onReconnectEnd: p, sourceX: A, sourceY: $, targetX: P, targetY: W, sourcePosition: O, targetPosition: B, setUpdateHover: z, setReconnecting: S })] }) });
}
var Xg = ve(Wg);
const qg = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function Uc({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: i, onEdgeContextMenu: s, onEdgeMouseEnter: c, onEdgeMouseMove: l, onEdgeMouseLeave: a, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: h, onReconnectStart: p, onReconnectEnd: y, disableKeyboardA11y: x }) {
  const { edgesFocusable: v, edgesReconnectable: m, elementsSelectable: b, onError: g } = de(qg, me), w = Ag(t);
  return u.jsxs("div", { className: "react-flow__edges", children: [u.jsx($g, { defaultColor: e, rfId: n }), w.map((C) => u.jsx(Xg, { id: C, edgesFocusable: v, edgesReconnectable: m, elementsSelectable: b, noPanClassName: r, onReconnect: i, onContextMenu: s, onMouseEnter: c, onMouseMove: l, onMouseLeave: a, onClick: d, reconnectRadius: f, onDoubleClick: h, onReconnectStart: p, onReconnectEnd: y, rfId: n, onError: g, edgeTypes: o, disableKeyboardA11y: x }, C))] });
}
Uc.displayName = "EdgeRenderer";
const Yg = ve(Uc), Zg = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function Kg({ children: e }) {
  const t = de(Zg);
  return u.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function Ug(e) {
  const t = Ci(), n = ce(!1);
  se(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Gg = (e) => e.panZoom?.syncViewport;
function Qg(e) {
  const t = de(Gg), n = xe();
  return se(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function Jg(e) {
  return e.connection.inProgress ? { ...e.connection, to: Kt(e.connection.to, e.transform) } : { ...e.connection };
}
function ey(e) {
  return Jg;
}
function ty(e) {
  const t = ey();
  return de(t, me);
}
const ny = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function oy({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: i, height: s, isValid: c, inProgress: l } = de(ny, me);
  return !(i && r && l) ? null : u.jsx("svg", { style: e, width: i, height: s, className: "react-flow__connectionline react-flow__container", children: u.jsx("g", { className: Ne(["react-flow__connection", qa(c)]), children: u.jsx(Gc, { style: t, type: n, CustomComponent: o, isValid: c }) }) });
}
const Gc = ({ style: e, type: t = lt.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: i, fromNode: s, fromHandle: c, fromPosition: l, to: a, toNode: d, toHandle: f, toPosition: h, pointer: p } = ty();
  if (!r)
    return;
  if (n)
    return u.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: s, fromHandle: c, fromX: i.x, fromY: i.y, toX: a.x, toY: a.y, fromPosition: l, toPosition: h, connectionStatus: qa(o), toNode: d, toHandle: f, pointer: p });
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
    case lt.Bezier:
      [y] = rc(x);
      break;
    case lt.SimpleBezier:
      [y] = Lc(x);
      break;
    case lt.Step:
      [y] = Do({
        ...x,
        borderRadius: 0
      });
      break;
    case lt.SmoothStep:
      [y] = Do(x);
      break;
    default:
      [y] = ac(x);
  }
  return u.jsx("path", { d: y, fill: "none", className: "react-flow__connection-path", style: e });
};
Gc.displayName = "ConnectionLine";
const ry = {};
function Fs(e = ry) {
  ce(e), xe(), se(() => {
  }, [e]);
}
function iy() {
  xe(), ce(!1), se(() => {
  }, []);
}
function Qc({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: i, onEdgeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: l, onNodeMouseLeave: a, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: h, onSelectionEnd: p, connectionLineType: y, connectionLineStyle: x, connectionLineComponent: v, connectionLineContainerStyle: m, selectionKeyCode: b, selectionOnDrag: g, selectionMode: w, multiSelectionKeyCode: C, panActivationKeyCode: E, zoomActivationKeyCode: k, deleteKeyCode: D, onlyRenderVisibleElements: T, elementsSelectable: F, defaultViewport: j, translateExtent: z, minZoom: H, maxZoom: S, preventScrolling: I, defaultMarkerColor: _, zoomOnScroll: A, zoomOnPinch: $, panOnScroll: P, panOnScrollSpeed: W, panOnScrollMode: O, zoomOnDoubleClick: B, panOnDrag: G, autoPanOnSelection: Y, onPaneClick: te, onPaneMouseEnter: ae, onPaneMouseMove: Q, onPaneMouseLeave: R, onPaneScroll: q, onPaneContextMenu: re, paneClickDistance: oe, nodeClickDistance: K, onEdgeContextMenu: J, onEdgeMouseEnter: ie, onEdgeMouseMove: L, onEdgeMouseLeave: Z, reconnectRadius: fe, onReconnect: ue, onReconnectStart: Ie, onReconnectEnd: Oe, noDragClassName: Be, noWheelClassName: ft, noPanClassName: et, disableKeyboardA11y: Ee, nodeExtent: je, rfId: Pe, viewport: Te, onViewportChange: Ke }) {
  return Fs(e), Fs(t), iy(), Ug(n), Qg(Te), u.jsx(vg, { onPaneClick: te, onPaneMouseEnter: ae, onPaneMouseMove: Q, onPaneMouseLeave: R, onPaneContextMenu: re, onPaneScroll: q, paneClickDistance: oe, deleteKeyCode: D, selectionKeyCode: b, selectionOnDrag: g, selectionMode: w, onSelectionStart: h, onSelectionEnd: p, multiSelectionKeyCode: C, panActivationKeyCode: E, zoomActivationKeyCode: k, elementsSelectable: F, zoomOnScroll: A, zoomOnPinch: $, zoomOnDoubleClick: B, panOnScroll: P, panOnScrollSpeed: W, panOnScrollMode: O, panOnDrag: G, autoPanOnSelection: Y, defaultViewport: j, translateExtent: z, minZoom: H, maxZoom: S, onSelectionContextMenu: f, preventScrolling: I, noDragClassName: Be, noWheelClassName: ft, noPanClassName: et, disableKeyboardA11y: Ee, onViewportChange: Ke, isControlledViewport: !!Te, children: u.jsxs(Kg, { children: [u.jsx(Yg, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: s, onReconnect: ue, onReconnectStart: Ie, onReconnectEnd: Oe, onlyRenderVisibleElements: T, onEdgeContextMenu: J, onEdgeMouseEnter: ie, onEdgeMouseMove: L, onEdgeMouseLeave: Z, reconnectRadius: fe, defaultMarkerColor: _, noPanClassName: et, disableKeyboardA11y: Ee, rfId: Pe }), u.jsx(oy, { style: x, type: y, component: v, containerStyle: m }), u.jsx("div", { className: "react-flow__edgelabel-renderer" }), u.jsx(jg, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: i, onNodeMouseEnter: c, onNodeMouseMove: l, onNodeMouseLeave: a, onNodeContextMenu: d, nodeClickDistance: K, onlyRenderVisibleElements: T, noPanClassName: et, noDragClassName: Be, disableKeyboardA11y: Ee, nodeExtent: je, rfId: Pe }), u.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Qc.displayName = "GraphView";
const sy = ve(Qc), ay = Ga(), Ws = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: c, minZoom: l = 0.5, maxZoom: a = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: h = "basic" } = {}) => {
  const p = /* @__PURE__ */ new Map(), y = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), v = /* @__PURE__ */ new Map(), m = o ?? t ?? [], b = n ?? e ?? [], g = d ?? [0, 0], w = f ?? An;
  uc(x, v, m);
  const { nodesInitialized: C } = Jr(b, p, y, {
    nodeOrigin: g,
    nodeExtent: w,
    zIndexMode: h
  });
  let E = [0, 0, 1];
  if (s && r && i) {
    const k = Vn(p, {
      filter: (j) => !!((j.width || j.initialWidth) && (j.height || j.initialHeight))
    }), { x: D, y: T, zoom: F } = xi(k, r, i, l, a, c?.padding ?? 0.1);
    E = [D, T, F];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: i ?? 0,
    transform: E,
    nodes: b,
    nodesInitialized: C,
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
    translateExtent: An,
    nodeExtent: w,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: Ht.Strict,
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
    connection: { ...Xa },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: ay,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Wa,
    zIndexMode: h,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, cy = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: c, minZoom: l, maxZoom: a, nodeOrigin: d, nodeExtent: f, zIndexMode: h }) => bp((p, y) => {
  async function x() {
    const { nodeLookup: v, panZoom: m, fitViewOptions: b, fitViewResolver: g, width: w, height: C, minZoom: E, maxZoom: k } = y();
    m && (await mh({
      nodes: v,
      width: w,
      height: C,
      panZoom: m,
      minZoom: E,
      maxZoom: k
    }, b), g?.resolve(!0), p({ fitViewResolver: null }));
  }
  return {
    ...Ws({
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
      const { nodeLookup: m, parentLookup: b, nodeOrigin: g, elevateNodesOnSelect: w, fitViewQueued: C, zIndexMode: E, nodesSelectionActive: k } = y(), { nodesInitialized: D, hasSelectedNodes: T } = Jr(v, m, b, {
        nodeOrigin: g,
        nodeExtent: f,
        elevateNodesOnSelect: w,
        checkEquality: !0,
        zIndexMode: E
      }), F = k && T;
      C && D ? (x(), p({
        nodes: v,
        nodesInitialized: D,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: F
      })) : p({ nodes: v, nodesInitialized: D, nodesSelectionActive: F });
    },
    setEdges: (v) => {
      const { connectionLookup: m, edgeLookup: b } = y();
      uc(m, b, v), p({ edges: v });
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
      const { triggerNodeChanges: m, nodeLookup: b, parentLookup: g, domNode: w, nodeOrigin: C, nodeExtent: E, debug: k, fitViewQueued: D, zIndexMode: T } = y(), { changes: F, updatedInternals: j } = Vh(v, b, g, w, C, E, T);
      j && (zh(b, g, { nodeOrigin: C, nodeExtent: E, zIndexMode: T }), D ? (x(), p({ fitViewQueued: !1, fitViewOptions: void 0 })) : p({}), F?.length > 0 && (k && console.log("React Flow: trigger node changes", F), m?.(F)));
    },
    updateNodePositions: (v, m = !1) => {
      const b = [];
      let g = [];
      const { nodeLookup: w, triggerNodeChanges: C, connection: E, updateConnection: k, onNodesChangeMiddlewareMap: D } = y();
      for (const [T, F] of v) {
        const j = w.get(T), z = !!(j?.expandParent && j?.parentId && F?.position), H = {
          id: T,
          type: "position",
          position: z ? {
            x: Math.max(0, F.position.x),
            y: Math.max(0, F.position.y)
          } : F.position,
          dragging: m
        };
        if (j && E.inProgress && E.fromNode.id === j.id) {
          const S = _t(j, E.fromHandle, ee.Left, !0);
          k({ ...E, from: S });
        }
        z && j.parentId && b.push({
          id: T,
          parentId: j.parentId,
          rect: {
            ...F.internals.positionAbsolute,
            width: F.measured.width ?? 0,
            height: F.measured.height ?? 0
          }
        }), g.push(H);
      }
      if (b.length > 0) {
        const { parentLookup: T, nodeOrigin: F } = y(), j = Ei(b, w, T, F);
        g.push(...j);
      }
      for (const T of D.values())
        g = T(g);
      C(g);
    },
    triggerNodeChanges: (v) => {
      const { onNodesChange: m, setNodes: b, nodes: g, hasDefaultNodes: w, debug: C } = y();
      if (v?.length) {
        if (w) {
          const E = Cc(v, g);
          b(E);
        }
        C && console.log("React Flow: trigger node changes", v), m?.(v);
      }
    },
    triggerEdgeChanges: (v) => {
      const { onEdgesChange: m, setEdges: b, edges: g, hasDefaultEdges: w, debug: C } = y();
      if (v?.length) {
        if (w) {
          const E = _c(v, g);
          b(E);
        }
        C && console.log("React Flow: trigger edge changes", v), m?.(v);
      }
    },
    addSelectedNodes: (v) => {
      const { multiSelectionActive: m, edgeLookup: b, nodeLookup: g, triggerNodeChanges: w, triggerEdgeChanges: C } = y();
      if (m) {
        const E = v.map((k) => xt(k, !0));
        w(E);
        return;
      }
      w(Tt(g, /* @__PURE__ */ new Set([...v]), !0)), C(Tt(b));
    },
    addSelectedEdges: (v) => {
      const { multiSelectionActive: m, edgeLookup: b, nodeLookup: g, triggerNodeChanges: w, triggerEdgeChanges: C } = y();
      if (m) {
        const E = v.map((k) => xt(k, !0));
        C(E);
        return;
      }
      C(Tt(b, /* @__PURE__ */ new Set([...v]))), w(Tt(g, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: v, edges: m } = {}) => {
      const { edges: b, nodes: g, nodeLookup: w, triggerNodeChanges: C, triggerEdgeChanges: E } = y(), k = v || g, D = m || b, T = [];
      for (const j of k) {
        if (!j.selected)
          continue;
        const z = w.get(j.id);
        z && (z.selected = !1), T.push(xt(j.id, !1));
      }
      const F = [];
      for (const j of D)
        j.selected && F.push(xt(j.id, !1));
      C(T), E(F);
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
      const C = m.reduce((k, D) => D.selected ? [...k, xt(D.id, !1)] : k, []), E = v.reduce((k, D) => D.selected ? [...k, xt(D.id, !1)] : k, []);
      b(C), g(E);
    },
    setNodeExtent: (v) => {
      const { nodes: m, nodeLookup: b, parentLookup: g, nodeOrigin: w, elevateNodesOnSelect: C, nodeExtent: E, zIndexMode: k } = y();
      v[0][0] === E[0][0] && v[0][1] === E[0][1] && v[1][0] === E[1][0] && v[1][1] === E[1][1] || (Jr(m, b, g, {
        nodeOrigin: w,
        nodeExtent: v,
        elevateNodesOnSelect: C,
        checkEquality: !1,
        zIndexMode: k
      }), p({ nodeExtent: v }));
    },
    panBy: (v) => {
      const { transform: m, width: b, height: g, panZoom: w, translateExtent: C } = y();
      return Oh({ delta: v, panZoom: w, transform: m, translateExtent: C, width: b, height: g });
    },
    setCenter: async (v, m, b) => {
      const { width: g, height: w, maxZoom: C, panZoom: E } = y();
      if (!E)
        return !1;
      const k = typeof b?.zoom < "u" ? b.zoom : C;
      return await E.setViewport({
        x: g / 2 - v * k,
        y: w / 2 - m * k,
        zoom: k
      }, { duration: b?.duration, ease: b?.ease, interpolate: b?.interpolate }), !0;
    },
    cancelConnection: () => {
      p({
        connection: { ...Xa }
      });
    },
    updateConnection: (v) => {
      p({ connection: v });
    },
    reset: () => p({ ...Ws() })
  };
}, Object.is);
function ly({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: i, initialMinZoom: s, initialMaxZoom: c, initialFitViewOptions: l, fitView: a, nodeOrigin: d, nodeExtent: f, zIndexMode: h, children: p }) {
  const [y] = U(() => cy({
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
  return u.jsx(Cp, { value: y, children: u.jsx(Kp, { children: p }) });
}
function uy({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: i, height: s, fitView: c, fitViewOptions: l, minZoom: a, maxZoom: d, nodeOrigin: f, nodeExtent: h, zIndexMode: p }) {
  return zn(Yo) ? u.jsx(u.Fragment, { children: e }) : u.jsx(ly, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: i, initialHeight: s, fitView: c, initialFitViewOptions: l, initialMinZoom: a, initialMaxZoom: d, nodeOrigin: f, nodeExtent: h, zIndexMode: p, children: e });
}
const dy = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function fy({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: i, edgeTypes: s, onNodeClick: c, onEdgeClick: l, onInit: a, onMove: d, onMoveStart: f, onMoveEnd: h, onConnect: p, onConnectStart: y, onConnectEnd: x, onClickConnectStart: v, onClickConnectEnd: m, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: w, onNodeContextMenu: C, onNodeDoubleClick: E, onNodeDragStart: k, onNodeDrag: D, onNodeDragStop: T, onNodesDelete: F, onEdgesDelete: j, onDelete: z, onSelectionChange: H, onSelectionDragStart: S, onSelectionDrag: I, onSelectionDragStop: _, onSelectionContextMenu: A, onSelectionStart: $, onSelectionEnd: P, onBeforeDelete: W, connectionMode: O, connectionLineType: B = lt.Bezier, connectionLineStyle: G, connectionLineComponent: Y, connectionLineContainerStyle: te, deleteKeyCode: ae = "Backspace", selectionKeyCode: Q = "Shift", selectionOnDrag: R = !1, selectionMode: q = Mn.Full, panActivationKeyCode: re = "Space", multiSelectionKeyCode: oe = Pn() ? "Meta" : "Control", zoomActivationKeyCode: K = Pn() ? "Meta" : "Control", snapToGrid: J, snapGrid: ie, onlyRenderVisibleElements: L = !1, selectNodesOnDrag: Z, nodesDraggable: fe, autoPanOnNodeFocus: ue, nodesConnectable: Ie, nodesFocusable: Oe, nodeOrigin: Be = Nc, edgesFocusable: ft, edgesReconnectable: et, elementsSelectable: Ee = !0, defaultViewport: je = Lp, minZoom: Pe = 0.5, maxZoom: Te = 2, translateExtent: Ke = An, preventScrolling: kt = !0, nodeExtent: tt, defaultMarkerColor: ht = "#b1b1b7", zoomOnScroll: pt = !0, zoomOnPinch: Xn = !0, panOnScroll: $e = !1, panOnScrollSpeed: qn = 0.5, panOnScrollMode: we = bt.Free, zoomOnDoubleClick: Fe = !0, panOnDrag: Ut = !0, onPaneClick: Ce, onPaneMouseEnter: Gt, onPaneMouseMove: Qt, onPaneMouseLeave: ke, onPaneScroll: gt, onPaneContextMenu: st, paneClickDistance: Qo = 1, nodeClickDistance: Yn = 0, children: Zn, onReconnect: Jt, onReconnectStart: Jo, onReconnectEnd: en, onEdgeContextMenu: It, onEdgeDoubleClick: ze, onEdgeMouseEnter: tn, onEdgeMouseMove: nn, onEdgeMouseLeave: on, reconnectRadius: jt = 10, onNodesChange: er, onEdgesChange: tr, noDragClassName: nr = "nodrag", noWheelClassName: or = "nowheel", noPanClassName: rn = "nopan", fitView: sn, fitViewOptions: an, connectOnClick: rr, attributionPosition: cn, proOptions: ir, defaultEdgeOptions: sr, elevateNodesOnSelect: ar = !0, elevateEdgesOnSelect: cr = !1, disableKeyboardA11y: Kn = !1, autoPanOnConnect: ln, autoPanOnNodeDrag: lr, autoPanOnSelection: ur = !0, autoPanSpeed: dr, connectionRadius: fr, isValidConnection: hr, onError: pr, style: gr, id: un, nodeDragThreshold: Un, connectionDragThreshold: yr, viewport: mr, onViewportChange: xr, width: wr, height: vr, colorMode: Gn = "light", debug: Qn, onScroll: dn, ariaLabelConfig: Jn, zIndexMode: fn = "basic", ...br }, Sr) {
  const At = un || "1", eo = Bp(Gn), to = ge((no) => {
    no.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), dn?.(no);
  }, [dn]);
  return u.jsx("div", { "data-testid": "rf__wrapper", ...br, onScroll: to, style: { ...gr, ...dy }, ref: Sr, className: Ne(["react-flow", r, eo]), id: un, role: "application", children: u.jsxs(uy, { nodes: e, edges: t, width: wr, height: vr, fitView: sn, fitViewOptions: an, minZoom: Pe, maxZoom: Te, nodeOrigin: Be, nodeExtent: tt, zIndexMode: fn, children: [u.jsx(Op, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: p, onConnectStart: y, onConnectEnd: x, onClickConnectStart: v, onClickConnectEnd: m, nodesDraggable: fe, autoPanOnNodeFocus: ue, nodesConnectable: Ie, nodesFocusable: Oe, edgesFocusable: ft, edgesReconnectable: et, elementsSelectable: Ee, elevateNodesOnSelect: ar, elevateEdgesOnSelect: cr, minZoom: Pe, maxZoom: Te, nodeExtent: tt, onNodesChange: er, onEdgesChange: tr, snapToGrid: J, snapGrid: ie, connectionMode: O, translateExtent: Ke, connectOnClick: rr, defaultEdgeOptions: sr, fitView: sn, fitViewOptions: an, onNodesDelete: F, onEdgesDelete: j, onDelete: z, onNodeDragStart: k, onNodeDrag: D, onNodeDragStop: T, onSelectionDrag: I, onSelectionDragStart: S, onSelectionDragStop: _, onMove: d, onMoveStart: f, onMoveEnd: h, noPanClassName: rn, nodeOrigin: Be, rfId: At, autoPanOnConnect: ln, autoPanOnNodeDrag: lr, autoPanSpeed: dr, onError: pr, connectionRadius: fr, isValidConnection: hr, selectNodesOnDrag: Z, nodeDragThreshold: Un, connectionDragThreshold: yr, onBeforeDelete: W, debug: Qn, ariaLabelConfig: Jn, zIndexMode: fn }), u.jsx(sy, { onInit: a, onNodeClick: c, onEdgeClick: l, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: w, onNodeContextMenu: C, onNodeDoubleClick: E, nodeTypes: i, edgeTypes: s, connectionLineType: B, connectionLineStyle: G, connectionLineComponent: Y, connectionLineContainerStyle: te, selectionKeyCode: Q, selectionOnDrag: R, selectionMode: q, deleteKeyCode: ae, multiSelectionKeyCode: oe, panActivationKeyCode: re, zoomActivationKeyCode: K, onlyRenderVisibleElements: L, defaultViewport: je, translateExtent: Ke, minZoom: Pe, maxZoom: Te, preventScrolling: kt, zoomOnScroll: pt, zoomOnPinch: Xn, zoomOnDoubleClick: Fe, panOnScroll: $e, panOnScrollSpeed: qn, panOnScrollMode: we, panOnDrag: Ut, autoPanOnSelection: ur, onPaneClick: Ce, onPaneMouseEnter: Gt, onPaneMouseMove: Qt, onPaneMouseLeave: ke, onPaneScroll: gt, onPaneContextMenu: st, paneClickDistance: Qo, nodeClickDistance: Yn, onSelectionContextMenu: A, onSelectionStart: $, onSelectionEnd: P, onReconnect: Jt, onReconnectStart: Jo, onReconnectEnd: en, onEdgeContextMenu: It, onEdgeDoubleClick: ze, onEdgeMouseEnter: tn, onEdgeMouseMove: nn, onEdgeMouseLeave: on, reconnectRadius: jt, defaultMarkerColor: ht, noDragClassName: nr, noWheelClassName: or, noPanClassName: rn, rfId: At, disableKeyboardA11y: Kn, nodeExtent: tt, viewport: mr, onViewportChange: xr }), u.jsx(Rp, { onSelectionChange: H }), Zn, u.jsx(Dp, { proOptions: ir, position: cn }), u.jsx(Mp, { rfId: At, disableKeyboardA11y: Kn })] }) });
}
var hy = jc(fy);
const py = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function gy({ children: e }) {
  const t = de(py);
  return t ? Ep.createPortal(e, t) : null;
}
function yy({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return u.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ne(["react-flow__background-pattern", n, o]) });
}
function my({ radius: e, className: t }) {
  return u.jsx("circle", { cx: e, cy: e, r: e, className: Ne(["react-flow__background-pattern", "dots", t]) });
}
var ut;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(ut || (ut = {}));
const xy = {
  [ut.Dots]: 1,
  [ut.Lines]: 1,
  [ut.Cross]: 6
}, wy = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function Jc({
  id: e,
  variant: t = ut.Dots,
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
  const f = ce(null), { transform: h, patternId: p } = de(wy, me), y = o || xy[t], x = t === ut.Dots, v = t === ut.Cross, m = Array.isArray(n) ? n : [n, n], b = [m[0] * h[2] || 1, m[1] * h[2] || 1], g = y * h[2], w = Array.isArray(i) ? i : [i, i], C = v ? [g, g] : b, E = [
    w[0] * h[2] || 1 + C[0] / 2,
    w[1] * h[2] || 1 + C[1] / 2
  ], k = `${p}${e || ""}`;
  return u.jsxs("svg", { className: Ne(["react-flow__background", a]), style: {
    ...l,
    ...Ko,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": s
  }, ref: f, "data-testid": "rf__background", children: [u.jsx("pattern", { id: k, x: h[0] % b[0], y: h[1] % b[1], width: b[0], height: b[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${E[0]},-${E[1]})`, children: x ? u.jsx(my, { radius: g / 2, className: d }) : u.jsx(yy, { dimensions: C, lineWidth: r, variant: t, className: d }) }), u.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${k})` })] });
}
Jc.displayName = "Background";
const vy = ve(Jc);
function by() {
  return u.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: u.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function Sy() {
  return u.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: u.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function Ny() {
  return u.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: u.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function Ey() {
  return u.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: u.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function Cy() {
  return u.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: u.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function fo({ children: e, className: t, ...n }) {
  return u.jsx("button", { type: "button", className: Ne(["react-flow__controls-button", t]), ...n, children: e });
}
const _y = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function el({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: i, onZoomOut: s, onFitView: c, onInteractiveChange: l, className: a, children: d, position: f = "bottom-left", orientation: h = "vertical", "aria-label": p }) {
  const y = xe(), { isInteractive: x, minZoomReached: v, maxZoomReached: m, ariaLabelConfig: b } = de(_y, me), { zoomIn: g, zoomOut: w, fitView: C } = Ci(), E = () => {
    g(), i?.();
  }, k = () => {
    w(), s?.();
  }, D = () => {
    C(r), c?.();
  }, T = () => {
    y.setState({
      nodesDraggable: !x,
      nodesConnectable: !x,
      elementsSelectable: !x
    }), l?.(!x);
  }, F = h === "horizontal" ? "horizontal" : "vertical";
  return u.jsxs(Zo, { className: Ne(["react-flow__controls", F, a]), position: f, style: e, "data-testid": "rf__controls", "aria-label": p ?? b["controls.ariaLabel"], children: [t && u.jsxs(u.Fragment, { children: [u.jsx(fo, { onClick: E, className: "react-flow__controls-zoomin", title: b["controls.zoomIn.ariaLabel"], "aria-label": b["controls.zoomIn.ariaLabel"], disabled: m, children: u.jsx(by, {}) }), u.jsx(fo, { onClick: k, className: "react-flow__controls-zoomout", title: b["controls.zoomOut.ariaLabel"], "aria-label": b["controls.zoomOut.ariaLabel"], disabled: v, children: u.jsx(Sy, {}) })] }), n && u.jsx(fo, { className: "react-flow__controls-fitview", onClick: D, title: b["controls.fitView.ariaLabel"], "aria-label": b["controls.fitView.ariaLabel"], children: u.jsx(Ny, {}) }), o && u.jsx(fo, { className: "react-flow__controls-interactive", onClick: T, title: b["controls.interactive.ariaLabel"], "aria-label": b["controls.interactive.ariaLabel"], children: x ? u.jsx(Cy, {}) : u.jsx(Ey, {}) }), d] });
}
el.displayName = "Controls";
const ky = ve(el);
function Iy({ id: e, x: t, y: n, width: o, height: r, style: i, color: s, strokeColor: c, strokeWidth: l, className: a, borderRadius: d, shapeRendering: f, selected: h, onClick: p }) {
  const { background: y, backgroundColor: x } = i || {}, v = s || y || x;
  return u.jsx("rect", { className: Ne(["react-flow__minimap-node", { selected: h }, a]), x: t, y: n, rx: d, ry: d, width: o, height: r, style: {
    fill: v,
    stroke: c,
    strokeWidth: l
  }, shapeRendering: f, onClick: p ? (m) => p(m, e) : void 0 });
}
const jy = ve(Iy), Ay = (e) => e.nodes.map((t) => t.id), Hr = (e) => e instanceof Function ? e : () => e;
function My({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: i = jy,
  onClick: s
}) {
  const c = de(Ay, me), l = Hr(t), a = Hr(e), d = Hr(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return u.jsx(u.Fragment, { children: c.map((h) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    u.jsx(Py, { id: h, nodeColorFunc: l, nodeStrokeColorFunc: a, nodeClassNameFunc: d, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: i, onClick: s, shapeRendering: f }, h)
  )) });
}
function Dy({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: i, shapeRendering: s, NodeComponent: c, onClick: l }) {
  const { node: a, x: d, y: f, width: h, height: p } = de((y) => {
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
  }, me);
  return !a || a.hidden || !Qa(a) ? null : u.jsx(c, { x: d, y: f, width: h, height: p, style: a.style, selected: !!a.selected, className: o(a), color: t(a), borderRadius: r, strokeColor: n(a), strokeWidth: i, shapeRendering: s, onClick: l, id: a.id });
}
const Py = ve(Dy);
var Ty = ve(My);
const $y = 200, zy = 150, Ry = (e) => !e.hidden, Ly = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? Ua(Vn(e.nodeLookup, { filter: Ry }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, Hy = "react-flow__minimap-desc";
function tl({
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
  const C = xe(), E = ce(null), { boundingRect: k, viewBB: D, rfId: T, panZoom: F, translateExtent: j, flowWidth: z, flowHeight: H, ariaLabelConfig: S } = de(Ly, me), I = e?.width ?? $y, _ = e?.height ?? zy, A = k.width / I, $ = k.height / _, P = Math.max(A, $), W = P * I, O = P * _, B = w * P, G = k.x - (W - k.width) / 2 - B, Y = k.y - (O - k.height) / 2 - B, te = W + B * 2, ae = O + B * 2, Q = `${Hy}-${T}`, R = ce(0), q = ce();
  R.current = P, se(() => {
    if (E.current && F)
      return q.current = Uh({
        domNode: E.current,
        panZoom: F,
        getTransform: () => C.getState().transform,
        getViewScale: () => R.current
      }), () => {
        q.current?.destroy();
      };
  }, [F]), se(() => {
    q.current?.update({
      translateExtent: j,
      width: z,
      height: H,
      inversePan: b,
      pannable: x,
      zoomStep: g,
      zoomable: v
    });
  }, [x, v, b, g, j, z, H]);
  const re = p ? (J) => {
    const [ie, L] = q.current?.pointer(J) || [0, 0];
    p(J, { x: ie, y: L });
  } : void 0, oe = y ? ge((J, ie) => {
    const L = C.getState().nodeLookup.get(ie).internals.userNode;
    y(J, L);
  }, []) : void 0, K = m ?? S["minimap.ariaLabel"];
  return u.jsx(Zo, { position: h, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-background-color-props": typeof a == "string" ? a : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * P : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof s == "number" ? s : void 0
  }, className: Ne(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: u.jsxs("svg", { width: I, height: _, viewBox: `${G} ${Y} ${te} ${ae}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": Q, ref: E, onClick: re, children: [K && u.jsx("title", { id: Q, children: K }), u.jsx(Ty, { onClick: oe, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: i, nodeClassName: r, nodeStrokeWidth: s, nodeComponent: c }), u.jsx("path", { className: "react-flow__minimap-mask", d: `M${G - B},${Y - B}h${te + B * 2}v${ae + B * 2}h${-te - B * 2}z
        M${D.x},${D.y}h${D.width}v${D.height}h${-D.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
tl.displayName = "MiniMap";
const Vy = ve(tl), Oy = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, By = {
  [Ft.Line]: "right",
  [Ft.Handle]: "bottom-right"
};
function Fy({ nodeId: e, position: t, variant: n = Ft.Handle, className: o, style: r = void 0, children: i, color: s, minWidth: c = 10, minHeight: l = 10, maxWidth: a = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: h, autoScale: p = !0, shouldResize: y, onResizeStart: x, onResize: v, onResizeEnd: m }) {
  const b = Pc(), g = typeof e == "string" ? e : b, w = xe(), C = ce(null), E = n === Ft.Handle, k = de(ge(Oy(E && p), [E, p]), me), D = ce(null), T = t ?? By[n];
  se(() => {
    if (!(!C.current || !g))
      return D.current || (D.current = lp({
        domNode: C.current,
        nodeId: g,
        getStoreItems: () => {
          const { nodeLookup: j, transform: z, snapGrid: H, snapToGrid: S, nodeOrigin: I, domNode: _ } = w.getState();
          return {
            nodeLookup: j,
            transform: z,
            snapGrid: H,
            snapToGrid: S,
            nodeOrigin: I,
            paneDomNode: _
          };
        },
        onChange: (j, z) => {
          const { triggerNodeChanges: H, nodeLookup: S, parentLookup: I, nodeOrigin: _ } = w.getState(), A = [], $ = { x: j.x, y: j.y }, P = S.get(g);
          if (P && P.expandParent && P.parentId) {
            const W = P.origin ?? _, O = j.width ?? P.measured.width ?? 0, B = j.height ?? P.measured.height ?? 0, G = {
              id: P.id,
              parentId: P.parentId,
              rect: {
                width: O,
                height: B,
                ...Ja({
                  x: j.x ?? P.position.x,
                  y: j.y ?? P.position.y
                }, { width: O, height: B }, P.parentId, S, W)
              }
            }, Y = Ei([G], S, I, _);
            A.push(...Y), $.x = j.x ? Math.max(W[0] * O, j.x) : void 0, $.y = j.y ? Math.max(W[1] * B, j.y) : void 0;
          }
          if ($.x !== void 0 && $.y !== void 0) {
            const W = {
              id: g,
              type: "position",
              position: { ...$ }
            };
            A.push(W);
          }
          if (j.width !== void 0 && j.height !== void 0) {
            const O = {
              id: g,
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
            id: g,
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
        controlPosition: T,
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
    T,
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
  const F = T.split("-");
  return u.jsx("div", { className: Ne(["react-flow__resize-control", "nodrag", ...F, n, o]), ref: C, style: {
    ...r,
    scale: k,
    ...s && { [E ? "backgroundColor" : "borderColor"]: s }
  }, children: i });
}
ve(Fy);
const Wy = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), nl = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var Xy = {
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
const qy = Lo(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: o,
    className: r = "",
    children: i,
    iconNode: s,
    ...c
  }, l) => Br(
    "svg",
    {
      ref: l,
      ...Xy,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: nl("lucide", r),
      ...c
    },
    [
      ...s.map(([a, d]) => Br(a, d)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const be = (e, t) => {
  const n = Lo(
    ({ className: o, ...r }, i) => Br(qy, {
      ref: i,
      iconNode: t,
      className: nl(`lucide-${Wy(e)}`, o),
      ...r
    })
  );
  return n.displayName = `${e}`, n;
};
const ol = be("Boxes", [
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
const Fn = be("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const Yy = be("ChevronDown", [
  ["path", { d: "m6 9 6 6 6-6", key: "qrunsl" }]
]);
const ni = be("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const Dt = be("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const Xt = be("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const rl = be("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const Zy = be("GripVertical", [
  ["circle", { cx: "9", cy: "12", r: "1", key: "1vctgf" }],
  ["circle", { cx: "9", cy: "5", r: "1", key: "hp0tcf" }],
  ["circle", { cx: "9", cy: "19", r: "1", key: "fkjjf6" }],
  ["circle", { cx: "15", cy: "12", r: "1", key: "1tmaij" }],
  ["circle", { cx: "15", cy: "5", r: "1", key: "19l28e" }],
  ["circle", { cx: "15", cy: "19", r: "1", key: "f4zoj3" }]
]);
const il = be("ListTree", [
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
const Xs = be("Minimize2", [
  ["polyline", { points: "4 14 10 14 10 20", key: "11kfnr" }],
  ["polyline", { points: "20 10 14 10 14 4", key: "rlmsce" }],
  ["line", { x1: "14", x2: "21", y1: "10", y2: "3", key: "o5lafz" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const ki = be("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const Ii = be("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const Ky = be("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const Uy = be("Save", [
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
const Gy = be("Search", [
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
const Qy = be("Terminal", [
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
const Jy = be("X", [
  ["path", { d: "M18 6 6 18", key: "1bl5f8" }],
  ["path", { d: "m6 6 12 12", key: "d8bk6v" }]
]);
const em = be("Zap", [
  [
    "path",
    {
      d: "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z",
      key: "1xq2db"
    }
  ]
]), Ve = "/_elsa/workflow-management", tm = "/_elsa/publishing";
async function nm(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${Ve}/definitions?${n.toString()}`);
}
async function om(e, t) {
  return e.http.getJson(`${Ve}/definitions/${encodeURIComponent(t)}`);
}
async function rm(e, t) {
  return e.http.postJson(`${Ve}/definitions`, t);
}
async function im(e, t) {
  await e.http.deleteJson(`${Ve}/definitions/${encodeURIComponent(t)}`);
}
async function sm(e, t) {
  await e.http.postJson(`${Ve}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function am(e, t) {
  await e.http.deleteJson(`${Ve}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function cm(e, t) {
  return e.http.putJson(`${Ve}/drafts/${encodeURIComponent(t.id)}`, { state: t.state, layout: t.layout });
}
async function lm(e, t) {
  return e.http.postJson(`${Ve}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function um(e, t) {
  return e.http.postJson(`${Ve}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function dm(e, t) {
  try {
    return await e.http.postJson(`${tm}/workflows/drafts/test-runs`, t);
  } catch (n) {
    const o = ym(n);
    if (o) return o;
    throw n;
  }
}
async function fm(e, t) {
  return e.http.postJson(`${Ve}/executables/${encodeURIComponent(t)}/run`, {});
}
async function hm(e) {
  return e.http.getJson("/_demo/workflows/executables");
}
async function sl(e) {
  return e.http.getJson(`${Ve}/activities`);
}
async function pm(e) {
  const t = await al(e, [
    `${Ve}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? qs(t) : qs(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function gm(e) {
  const t = await al(e, [
    `${Ve}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : vo;
}
async function al(e, t) {
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
function ym(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = Ys(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return Ys(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function Ys(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const vo = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
], Uo = "elsa.sequence.structure", Wn = "elsa.flowchart.structure";
function cl(e, t) {
  if (!e) return null;
  let n = e;
  for (const o of t) {
    const r = Qe(n).find((s) => s.id === o.slotId);
    if (!r) return null;
    const i = r.activities.find((s) => s.nodeId === o.ownerNodeId);
    if (!i) return null;
    n = i;
  }
  return n;
}
function Vr(e, t) {
  const n = cl(e, t);
  if (!n) return null;
  let o = Qe(n)[0];
  return o ? { owner: n, slot: o } : null;
}
function Qe(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = Rm(t), r = Or(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: Lm(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, i]) => Or(i)).map(([i, s]) => ({
    id: `${t.kind}:${i}`,
    label: Vm(i),
    property: i,
    mode: "generic",
    activities: Or(s) ?? []
  }));
}
function mm(e, t, n) {
  const o = new Map(t.map((s) => [s.activityVersionId, s])), r = new Map(n.map((s) => [s.nodeId, s])), i = e.slot.activities.map((s, c) => {
    const l = o.get(s.activityVersionId), a = r.get(s.nodeId) ?? Hm(e.slot.mode, c);
    return dl(s, l, { x: a.x, y: a.y });
  });
  return {
    nodes: i,
    edges: e.slot.mode === "flowchart" ? Am(e.owner) : jm(e.slot, i)
  };
}
function xm(e, t, n) {
  const o = t.find((i) => i.activityVersionId === e.activityVersionId), r = n.find((i) => i.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [dl(e, o, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function wm(e, t) {
  return e?.structure?.kind === Wn || Em(t) ? "flowchart" : e?.structure?.kind === Uo || Cm(t) ? "sequence" : "unsupported";
}
function ri(e, t, n) {
  if (t.length === 0) {
    const c = Qe(e)[0];
    return c ? $n(e, c, n) : e;
  }
  const [o, ...r] = t, i = Qe(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((c) => c.nodeId === o.ownerNodeId ? ri(c, r, n) : c);
  return $n(e, i, s);
}
function ll(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, i = Qe(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((c) => c.nodeId === o.ownerNodeId ? ll(c, r, n) : c);
  return $n(e, i, s);
}
function ul(e, t, n) {
  if (e.nodeId === t) return n(e);
  const o = Qe(e);
  if (o.length === 0) return e;
  let r = !1, i = e;
  for (const s of o) {
    const c = s.activities.map((l) => {
      const a = ul(l, t, n);
      return a !== l && (r = !0), a;
    });
    r && (i = $n(i, s, c));
  }
  return r ? i : e;
}
function $n(e, t, n) {
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
function vm(e, t, n, o = []) {
  const r = new Map(e.slot.activities.map((s) => [s.nodeId, s]));
  for (const s of o)
    r.set(s.nodeId, s);
  const i = t.map((s) => r.get(s.id)).filter((s) => !!s);
  return e.slot.mode === "sequence" && i.sort((s, c) => {
    const l = t.find((d) => d.id === s.nodeId), a = t.find((d) => d.id === c.nodeId);
    return (l?.position.x ?? 0) - (a?.position.x ?? 0);
  }), $n(e.owner, e.slot, i);
}
function bm(e, t) {
  return {
    ...e,
    structure: Im(e.structure, t)
  };
}
function Sm(e, t) {
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
function Zs(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: km(e)
  };
}
function _e(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? _m(t) : n;
}
function dl(e, t, n, o = {}) {
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
      icon: ii(t),
      childSlots: Qe(e),
      acceptsInbound: Mm(e, t),
      sourcePorts: o.suppressFlowPorts ? [] : fl(e, t),
      suppressFlowPorts: o.suppressFlowPorts
    }
  };
}
function ii(e) {
  if (!e) return "activity";
  const t = Nm(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), o = _e(e).toLowerCase(), r = e.category?.toLowerCase() ?? "", i = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || o === "flowchart" ? "flowchart" : n.endsWith(".sequence") || o === "sequence" ? "sequence" : n.includes("writeline") || o.includes("write line") ? "terminal" : r.includes("runtime") ? "runtime" : i === "trigger" ? "trigger" : "activity";
}
function Nm(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function Em(e) {
  return !!e && (_e(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function Cm(e) {
  return !!e && (_e(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function _m(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function km(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: Uo,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: Wn,
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
    if (!ji(r)) continue;
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
function jm(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function Am(e) {
  if (e.structure?.kind !== Wn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, i = n.target;
    if (!r?.nodeId || !i?.nodeId) return null;
    const s = Array.isArray(n.vertices) ? n.vertices.filter($m) : [];
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
function fl(e, t) {
  const n = Ks(e.cases);
  if (Pm(e, t) && n.length > 0)
    return [...n.map((i) => ({ name: i, displayName: i })), { name: "Default", displayName: "Default" }];
  const o = [
    ...bo(t?.designFacets),
    ...bo(t?.ports),
    ...bo(t?.outputs)
  ];
  if (o.length > 0) return Tm(o);
  const r = Ks(e.outcomes);
  return r.length > 0 ? r.map((i) => ({ name: i, displayName: i })) : [{ name: "Done", displayName: "Done" }];
}
function Mm(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function $o(e, t, n, o) {
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
function Dm(e, t, n) {
  const o = $o(t.source, n, t.sourceHandle ?? "Done", void 0), r = $o(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((i) => i.id !== t.id).concat(o, r);
}
function Or(e) {
  return Array.isArray(e) ? e.filter(zm) : null;
}
function Pm(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function bo(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!ji(n)) continue;
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
function Tm(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function Ks(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function $m(e) {
  return ji(e) && typeof e.x == "number" && typeof e.y == "number";
}
function ji(e) {
  return typeof e == "object" && e !== null;
}
function zm(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Rm(e) {
  return e.kind === Uo ? "sequence" : e.kind === Wn ? "flowchart" : "generic";
}
function Lm(e) {
  return e.kind === Uo || e.kind === Wn, "Activities";
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
function Ai(e) {
  return Bm(e.name);
}
function Fm(e, t) {
  const n = Ai(t), o = e[n];
  return t.isWrapped === !1 ? o ?? t.defaultValue ?? "" : pl(o, t);
}
function hl(e, t) {
  return pl(e[Ai(t)], t);
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
function Xm(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function Us(e, t, n) {
  return {
    ...e,
    [Ai(t)]: n
  };
}
function qm(e, t) {
  return t.isWrapped === !1 ? Fm(e, t) : hl(e, t).expression.value;
}
function pl(e, t) {
  return Ym(e) ? {
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
function Ym(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
const gl = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]);
function Zm({
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
  const c = Qm(s), l = o.length > 0 ? o : Om;
  return /* @__PURE__ */ u.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ u.jsx("span", { className: "wf-section-label", children: "Properties" }),
    c.map((a) => /* @__PURE__ */ u.jsxs("section", { className: "wf-property-group", children: [
      c.length > 1 ? /* @__PURE__ */ u.jsx("h4", { children: a.category }) : null,
      a.inputs.map((d) => /* @__PURE__ */ u.jsx(
        Km,
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
function Km({
  activity: e,
  input: t,
  editors: n,
  expressionDescriptors: o,
  onChange: r
}) {
  const i = t.isReadOnly === !0, s = { activity: e, expressionDescriptors: o, readOnly: i }, c = Gm(n, t, s), l = c?.component, a = t.isWrapped !== !1 ? hl(e, t) : null, d = a?.expression.type ?? "Literal", f = qm(e, t), h = !!(a && Jm(t, c?.id)), p = !!(a && e0(t, c?.id)), [y, x] = U(!1), v = (b) => {
    const g = a ? Wm(a, b) : b;
    r(Us(e, t, g));
  }, m = (b) => {
    a && r(Us(e, t, Xm(a, b)));
  };
  return /* @__PURE__ */ u.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ u.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ u.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ u.jsx("span", { children: yl(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ u.jsx("p", { children: t.description }) : null,
    a && !h ? /* @__PURE__ */ u.jsx(
      si,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: d,
        descriptors: o,
        disabled: i,
        onChange: m
      }
    ) : null,
    h ? /* @__PURE__ */ u.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ u.jsx("div", { className: "wf-expression-editor", children: Gs(l, t, f, i, s, v) }),
      /* @__PURE__ */ u.jsx(
        si,
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
          children: /* @__PURE__ */ u.jsx(To, { size: 13 })
        }
      ) : null
    ] }) : Gs(l, t, f, i, s, v),
    p && !h ? /* @__PURE__ */ u.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => x(!0),
        children: [
          /* @__PURE__ */ u.jsx(To, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    y ? /* @__PURE__ */ u.jsx(
      Um,
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
function Um({
  input: e,
  value: t,
  syntax: n,
  descriptors: o,
  disabled: r,
  onChange: i,
  onSyntaxChange: s,
  onClose: c
}) {
  const l = fa(), a = e.displayName || e.name;
  return se(() => {
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
      /* @__PURE__ */ u.jsx("button", { type: "button", "aria-label": `Close ${a} editor`, onClick: c, children: /* @__PURE__ */ u.jsx(Jy, { size: 16 }) })
    ] }),
    /* @__PURE__ */ u.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ u.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ u.jsx(
          si,
          {
            label: `${a} expression syntax`,
            value: n,
            descriptors: o,
            disabled: r,
            onChange: s
          }
        ),
        /* @__PURE__ */ u.jsx("span", { children: yl(e.typeName) })
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
function Gs(e, t, n, o, r, i) {
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
function si({
  label: e,
  value: t,
  descriptors: n,
  disabled: o,
  variant: r = "block",
  onChange: i
}) {
  const [s, c] = U(!1), l = fa(), a = n.find((f) => f.type === t), d = [
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
function yl(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function Jm(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !gl.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function e0(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !gl.has(t) && n !== "multiline") return !1;
  const o = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(o) || n === "singleline" || n === "multiline";
}
const t0 = { workflowActivity: j0 }, n0 = { workflow: M0 }, Qs = "application/x-elsa-activity-version-id", o0 = 6, r0 = 1200, i0 = [10, 25, 50], s0 = 10, Js = "elsa-studio-workflow-palette-width", ea = "elsa-studio-workflow-inspector-width", ta = "elsa-studio-workflow-palette-collapsed", na = "elsa-studio-workflow-inspector-collapsed", ml = "elsa-studio-workflow-side-panel-maximized", yn = 180, mn = 460, a0 = 260, xn = 260, wn = 560, c0 = 320, oa = 42, ho = 16, xl = dt.createContext(null);
function O0(e) {
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
        component: () => /* @__PURE__ */ u.jsx(l0, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), workflowDesignerPanels: e.workflowDesigner.panels.list() })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ u.jsx(u0, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow instances",
        component: () => /* @__PURE__ */ u.jsx(d0, { ai: e.ai })
      }
    ]
  });
}
function l0({
  context: e,
  ai: t,
  propertyEditors: n,
  workflowDesignerPanels: o
}) {
  const [r, i] = U(ra);
  se(() => {
    const c = () => i(ra());
    return window.addEventListener("popstate", c), () => window.removeEventListener("popstate", c);
  }, []);
  const s = (c) => {
    const l = c ? `/workflows/definitions?definition=${encodeURIComponent(c)}` : "/workflows/definitions";
    window.history.pushState({}, "", l), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return r ? /* @__PURE__ */ u.jsx(I0, { context: e, definitionId: r, ai: t, propertyEditors: n, workflowDesignerPanels: o, onBack: () => s(null) }) : /* @__PURE__ */ u.jsx(Mi, { activePath: "/workflows/definitions", title: "Definitions", children: /* @__PURE__ */ u.jsx(f0, { context: e, ai: t, onOpen: s }) });
}
function u0({ context: e, ai: t }) {
  const [n, o] = U(ia);
  return se(() => {
    const r = () => o(ia());
    return window.addEventListener("popstate", r), () => window.removeEventListener("popstate", r);
  }, []), /* @__PURE__ */ u.jsx(Mi, { activePath: "/workflows/executables", title: "Executables", children: /* @__PURE__ */ u.jsx(p0, { context: e, ai: t, definitionFilter: n }) });
}
function d0({ ai: e }) {
  const t = Yt(e, "weaver.workflows.explain-instance");
  return /* @__PURE__ */ u.jsx(Mi, { activePath: "/workflows/instances", title: "Instances", children: /* @__PURE__ */ u.jsxs("div", { className: "wf-empty", children: [
    "Workflow instance history will appear here when the runtime exposes an instance query endpoint.",
    t ? /* @__PURE__ */ u.jsxs("button", { type: "button", className: "wf-ai-inline-action", onClick: () => Zt(e, t, { scope: "workflow-instances" }), children: [
      /* @__PURE__ */ u.jsx(qt, { size: 13 }),
      " Ask Weaver about instances"
    ] }) : null
  ] }) });
}
function Mi({ activePath: e, title: t, children: n }) {
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
function ra() {
  return new URLSearchParams(window.location.search).get("definition");
}
function ia() {
  return new URLSearchParams(window.location.search).get("definition");
}
function f0({ context: e, ai: t, onOpen: n }) {
  const [o, r] = U(""), [i, s] = U("active"), [c, l] = U(1), [a, d] = U(s0), [f, h] = U("loading"), [p, y] = U(""), [x, v] = U(""), [m, b] = U([]), [g, w] = U(0), [C, E] = U(() => /* @__PURE__ */ new Set()), [k, D] = U(null), [T, F] = U(!1), [j, z] = U([]), [H, S] = U("idle"), I = ce(null), _ = ye(() => m.map((L) => L.id), [m]), A = Yt(t, "weaver.workflows.suggest-create-metadata"), $ = Yt(t, "weaver.workflows.explain-definition"), P = _.filter((L) => C.has(L)).length, W = _.length > 0 && P === _.length, O = ge(async () => {
    h("loading"), y("");
    try {
      const L = await nm(e, { search: o, state: i, page: c, pageSize: a }), Z = typeof L.totalCount == "number", fe = L.totalCount ?? L.definitions.length, ue = wl(fe, a);
      if (fe > 0 && c > ue) {
        l(ue);
        return;
      }
      b(Z ? L.definitions : y0(L.definitions, c, a)), w(fe), h("ready");
    } catch (L) {
      y(L instanceof Error ? L.message : String(L)), h("failed");
    }
  }, [e, o, i, c, a]);
  se(() => {
    O();
  }, [O]), se(() => {
    I.current && (I.current.indeterminate = P > 0 && !W);
  }, [W, P]);
  const B = ge(async () => {
    if (!(H === "loading" || H === "ready")) {
      S("loading");
      try {
        const L = await sl(e);
        z(L.activities ?? []), S("ready");
      } catch (L) {
        S("failed"), y(L instanceof Error ? L.message : String(L));
      }
    }
  }, [H, e]), G = () => {
    y(""), v(""), D({ name: "", description: "", rootKind: "flowchart" }), B();
  }, Y = async () => {
    if (k?.name.trim()) {
      F(!0), y(""), v("");
      try {
        const L = await rm(e, {
          name: k.name.trim(),
          description: k.description.trim() || null,
          rootKind: k.rootKind,
          rootActivityVersionId: w0(k, j)
        });
        D(null), n(L.definition.id);
      } catch (L) {
        y(L instanceof Error ? L.message : String(L));
      } finally {
        F(!1);
      }
    }
  }, te = (L) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(L)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, ae = async () => {
    if (m.length === 1 && c > 1) {
      l(c - 1);
      return;
    }
    await O();
  }, Q = () => E(/* @__PURE__ */ new Set()), R = (L, Z) => {
    E((fe) => {
      const ue = new Set(fe);
      return Z ? ue.add(L) : ue.delete(L), ue;
    });
  }, q = (L) => {
    E((Z) => {
      const fe = new Set(Z);
      for (const ue of _)
        L ? fe.add(ue) : fe.delete(ue);
      return fe;
    });
  }, re = (L) => {
    s(L), l(1), Q();
  }, oe = (L) => {
    r(L), l(1), Q();
  }, K = async (L) => {
    if (window.confirm(`Delete workflow definition "${L.name}"? You can restore it from the Deleted view.`)) {
      v(""), y("");
      try {
        await im(e, L.id), R(L.id, !1), v(`Deleted ${L.name}`), await ae();
      } catch (Z) {
        y(Z instanceof Error ? Z.message : String(Z));
      }
    }
  }, J = async (L) => {
    v(""), y("");
    try {
      await sm(e, L.id), R(L.id, !1), v(`Restored ${L.name}`), await ae();
    } catch (Z) {
      y(Z instanceof Error ? Z.message : String(Z));
    }
  }, ie = async (L) => {
    if (window.confirm(`Permanently delete workflow definition "${L.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`)) {
      v(""), y("");
      try {
        await am(e, L.id), R(L.id, !1), v(`Permanently deleted ${L.name}`), await ae();
      } catch (Z) {
        y(Z instanceof Error ? Z.message : String(Z));
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
        /* @__PURE__ */ u.jsx(Gy, { size: 15 }),
        /* @__PURE__ */ u.jsx("input", { value: o, onChange: (L) => oe(L.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ u.jsx("button", { type: "button", onClick: () => {
        O();
      }, children: "Refresh" }),
      /* @__PURE__ */ u.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ u.jsxs("button", { type: "button", title: "Create workflow", onClick: G, children: [
        /* @__PURE__ */ u.jsx(Ii, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ u.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ u.jsx(Xt, { size: 16 }),
      " ",
      p
    ] }) : null,
    f !== "failed" && p ? /* @__PURE__ */ u.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ u.jsx(Xt, { size: 16 }),
      " ",
      p
    ] }) : null,
    x ? /* @__PURE__ */ u.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ u.jsx(Fn, { size: 14 }),
      " ",
      x
    ] }) : null,
    C.size > 0 ? /* @__PURE__ */ u.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ u.jsxs("span", { children: [
        C.size,
        " selected"
      ] }),
      /* @__PURE__ */ u.jsx("button", { type: "button", onClick: Q, children: "Clear selection" })
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
              checked: W,
              onChange: (L) => q(L.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ u.jsx("span", { children: "Name" }),
          /* @__PURE__ */ u.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ u.jsx("span", { children: i === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ u.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ u.jsx("span", { children: "Actions" })
        ] }),
        m.map((L) => /* @__PURE__ */ u.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${L.name}`,
            "aria-selected": C.has(L.id),
            tabIndex: 0,
            onClick: () => n(L.id),
            onKeyDown: (Z) => {
              Z.currentTarget === Z.target && (Z.key !== "Enter" && Z.key !== " " || (Z.preventDefault(), n(L.id)));
            },
            children: [
              /* @__PURE__ */ u.jsx("label", { className: "wf-row-select", onClick: (Z) => Z.stopPropagation(), children: /* @__PURE__ */ u.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: C.has(L.id),
                  onChange: (Z) => R(L.id, Z.target.checked),
                  "aria-label": `Select workflow definition ${L.name}`
                }
              ) }),
              /* @__PURE__ */ u.jsxs("span", { children: [
                /* @__PURE__ */ u.jsx("strong", { children: L.name }),
                /* @__PURE__ */ u.jsx("small", { children: L.description || L.id })
              ] }),
              /* @__PURE__ */ u.jsx("span", { children: L.latestVersion ?? "No version" }),
              /* @__PURE__ */ u.jsx("span", { children: i === "deleted" ? Ro(L.deletedAt) : L.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ u.jsx("span", { children: Ro(L.lastModifiedAt) }),
              /* @__PURE__ */ u.jsx("span", { className: "wf-row-actions", onClick: (Z) => Z.stopPropagation(), children: i === "active" ? /* @__PURE__ */ u.jsxs(u.Fragment, { children: [
                /* @__PURE__ */ u.jsx("button", { type: "button", onClick: (Z) => {
                  Z.stopPropagation(), n(L.id);
                }, children: "Open" }),
                /* @__PURE__ */ u.jsx("button", { type: "button", onClick: (Z) => {
                  Z.stopPropagation(), te(L.id);
                }, children: "Artifacts" }),
                $ ? /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => Zt(t, $, L), children: [
                  /* @__PURE__ */ u.jsx(qt, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ u.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  K(L);
                }, children: [
                  /* @__PURE__ */ u.jsx(oi, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ u.jsxs(u.Fragment, { children: [
                /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => {
                  J(L);
                }, children: [
                  /* @__PURE__ */ u.jsx(Ky, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ u.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  ie(L);
                }, children: [
                  /* @__PURE__ */ u.jsx(oi, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          L.id
        ))
      ] }),
      /* @__PURE__ */ u.jsx(
        g0,
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
      h0,
      {
        draft: k,
        activities: j,
        catalogState: H,
        creating: T,
        suggestMetadataAction: A,
        onSuggestMetadata: A ? () => Zt(t, A, { draft: k, activities: j }) : void 0,
        onChange: (L) => D(L),
        onClose: () => D(null),
        onSubmit: Y
      }
    ) : null
  ] });
}
function h0({ draft: e, activities: t, catalogState: n, creating: o, suggestMetadataAction: r, onSuggestMetadata: i, onChange: s, onClose: c, onSubmit: l }) {
  const a = ye(() => m0(t), [t]), d = x0(e, t), f = (h) => {
    if (h.startsWith("kind:")) {
      s({ ...e, rootKind: h.slice(5), rootActivityVersionId: null });
      return;
    }
    const p = t.find((y) => y.activityVersionId === h);
    s({
      ...e,
      rootKind: vl(p) ?? e.rootKind,
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
            /* @__PURE__ */ u.jsx(qt, { size: 13 }),
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
function p0({ context: e, ai: t, definitionFilter: n }) {
  const [o, r] = U("loading"), [i, s] = U(""), [c, l] = U(""), [a, d] = U([]), f = ye(
    () => n ? a.filter((x) => x.definitionId === n || x.sourceId === n) : a,
    [n, a]
  ), h = Yt(t, "weaver.workflows.explain-executable"), p = ge(async () => {
    r("loading"), s("");
    try {
      d(await hm(e)), r("ready");
    } catch (x) {
      s(x instanceof Error ? x.message : String(x)), r("failed");
    }
  }, [e]);
  se(() => {
    p();
  }, [p]);
  const y = async (x) => {
    l(""), s("");
    try {
      await fm(e, x.artifactId), l(`Started ${x.artifactId}`);
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
      /* @__PURE__ */ u.jsx(Xt, { size: 16 }),
      " ",
      i
    ] }) : null,
    c ? /* @__PURE__ */ u.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ u.jsx(Fn, { size: 14 }),
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
        /* @__PURE__ */ u.jsx("span", { children: b0(x) }),
        /* @__PURE__ */ u.jsx("span", { children: S0(x) }),
        /* @__PURE__ */ u.jsx("span", { children: Ro(x.publishedAt ?? x.createdAt) }),
        /* @__PURE__ */ u.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => {
            y(x);
          }, children: [
            /* @__PURE__ */ u.jsx(ki, { size: 13 }),
            " Run"
          ] }),
          h ? /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => Zt(t, h, x), children: [
            /* @__PURE__ */ u.jsx(qt, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, x.artifactId))
    ] }) : null
  ] });
}
function g0({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const i = wl(n, t), s = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
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
      /* @__PURE__ */ u.jsx("select", { value: t, onChange: (l) => r(Number(l.target.value)), children: i0.map((l) => /* @__PURE__ */ u.jsx("option", { value: l, children: l }, l)) })
    ] }),
    /* @__PURE__ */ u.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ u.jsx(ni, { size: 14 }),
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
        /* @__PURE__ */ u.jsx(Dt, { size: 14 })
      ] })
    ] })
  ] });
}
function y0(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function wl(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Yt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function Zt(e, t, n) {
  const o = t.createPrompt(n);
  o && e.dispatchPrompt(o);
}
function m0(e) {
  const t = zo(e, "flowchart"), n = zo(e, "sequence"), o = [
    { value: t?.activityVersionId ?? "kind:flowchart", label: "Flowchart" },
    { value: n?.activityVersionId ?? "kind:sequence", label: "Sequence" }
  ], r = /* @__PURE__ */ new Map();
  for (const s of e.filter(El)) {
    if (v0(s)) continue;
    const c = s.category || "Uncategorized";
    r.set(c, [...r.get(c) ?? [], s]);
  }
  const i = Array.from(r.entries()).sort(([s], [c]) => s.localeCompare(c)).map(([s, c]) => ({
    name: s,
    activities: c.sort((l, a) => _e(l).localeCompare(_e(a)))
  }));
  return { compositeRoots: o, otherCategories: i };
}
function x0(e, t) {
  return e.rootActivityVersionId ?? zo(t, e.rootKind)?.activityVersionId ?? `kind:${e.rootKind}`;
}
function w0(e, t) {
  return e.rootActivityVersionId ?? zo(t, e.rootKind)?.activityVersionId ?? null;
}
function zo(e, t) {
  return e.find((n) => vl(n) === t);
}
function vl(e) {
  return e ? Sl(e) ? "flowchart" : Nl(e) ? "sequence" : null : null;
}
function bl(e) {
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
function v0(e) {
  return Sl(e) || Nl(e);
}
function Sl(e) {
  return _e(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function Nl(e) {
  return _e(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function El(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function b0(e) {
  return e.sourceKind || e.sourceId || e.sourceVersion ? [e.sourceKind, e.sourceId, e.sourceVersion].filter(Boolean).join(" / ") : e.definitionId;
}
function S0(e) {
  return N0(e.rootActivityType) || e.rootActivityType;
}
function N0(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function E0(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    po(t, n.typeName, n), po(t, n.name, n), po(t, n.displayName, n);
    const o = n.typeName.split(".").filter(Boolean).at(-1);
    po(t, o, n);
  }
  return t;
}
function C0(e, t, n) {
  const o = t.get(e.activityVersionId);
  return n.get(Nn(o?.activityTypeKey)) ?? n.get(Nn(_0(o?.activityTypeKey))) ?? n.get(Nn(o?.displayName)) ?? n.get(Nn(e.activityVersionId)) ?? null;
}
function po(e, t, n) {
  const o = Nn(t);
  o && !e.has(o) && e.set(o, n);
}
function Nn(e) {
  return e?.trim().toLowerCase() ?? "";
}
function _0(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function sa(e, t, n, o) {
  const r = Go();
  if (!r) return t;
  const i = r.getItem(e);
  if (i == null) return t;
  const s = Number(i);
  return Number.isFinite(s) ? So(s, n, o) : t;
}
function aa(e, t) {
  const n = Go();
  if (!n) return t;
  const o = n.getItem(e);
  return o === "true" ? !0 : o === "false" ? !1 : t;
}
function k0() {
  const e = Go();
  if (!e) return null;
  const t = e.getItem(ml);
  return t === "palette" || t === "inspector" ? t : null;
}
function Go() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function vn(e, t) {
  const n = Go();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function So(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function I0({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: o,
  workflowDesignerPanels: r,
  onBack: i
}) {
  const [s, c] = U(null), [l, a] = U(null), [d, f] = U([]), [h, p] = U([]), [y, x] = U(vo), [v, m] = U("loading"), [b, g] = U([]), [w, C] = U([]), [E, k] = U([]), [D, T] = U(null), [F, j] = U(null), [z, H] = U(null), [S, I] = U(null), [_, A] = U(""), [$, P] = U(""), [W, O] = U("idle"), [B, G] = U(null), [Y, te] = U(!1), [ae, Q] = U(null), [R, q] = U(() => /* @__PURE__ */ new Set()), [re, oe] = U(() => sa(Js, a0, yn, mn)), [K, J] = U(() => sa(ea, c0, xn, wn)), [ie, L] = U(() => aa(ta, !1)), [Z, fe] = U(() => aa(na, !1)), [ue, Ie] = U(k0), [Oe, Be] = U("activities"), [ft, et] = U("inspector"), Ee = ce(null), je = ce(null), Pe = ce(""), Te = ce(0), Ke = ce(Promise.resolve()), kt = ce(null), tt = ce(!1), ht = l?.state.rootActivity ?? null, pt = ye(() => new Map(d.map((N) => [N.activityVersionId, N])), [d]), Xn = ye(() => E0(h), [h]), $e = ye(() => cl(ht, b), [ht, b]), qn = wm($e, $e ? pt.get($e.activityVersionId) : void 0), we = !!$e && qn === "unsupported", Fe = ye(() => we ? null : Vr(ht, b), [ht, b, we]), Ut = ye(() => bl(d), [d]), Ce = ye(() => we && $e?.nodeId === F ? $e : Fe?.slot.activities.find((N) => N.nodeId === F) ?? null, [we, Fe, $e, F]), Gt = ye(
    () => Ce ? C0(Ce, pt, Xn) : null,
    [pt, Xn, Ce]
  ), Qt = Ce ? Qe(Ce) : [], ke = qn === "flowchart" && Fe?.slot.mode === "flowchart", gt = !ht || !we, st = W !== "idle", Qo = !!l?.state.rootActivity && !st, Yn = Yt(n, "weaver.workflows.find-draft-risks"), Zn = Yt(n, "weaver.workflows.propose-update");
  se(() => {
    vn(Js, String(re));
  }, [re]), se(() => {
    vn(ea, String(K));
  }, [K]), se(() => {
    vn(ta, String(ie));
  }, [ie]), se(() => {
    vn(na, String(Z));
  }, [Z]), se(() => {
    vn(ml, ue);
  }, [ue]), se(() => {
    if (!ue) return;
    const N = (M) => {
      M.key === "Escape" && Ie(null);
    };
    return window.addEventListener("keydown", N), () => window.removeEventListener("keydown", N);
  }, [ue]);
  const Jt = ge(async () => {
    A(""), m("loading");
    const [N, M, V, X] = await Promise.all([
      om(e, t),
      sl(e),
      pm(e).then(
        (he) => ({ ok: !0, descriptors: he }),
        () => ({ ok: !1, descriptors: [] })
      ),
      gm(e).then(
        (he) => ({ ok: !0, descriptors: he }),
        () => ({ ok: !1, descriptors: vo })
      )
    ]), ne = N.draft ?? null;
    c(N), Pe.current = ne ? mt(ne) : "", a(ne), f(M.activities ?? []), p(V.descriptors), x(X.descriptors.length > 0 ? X.descriptors : vo), m(V.ok ? "ready" : "failed"), g([]), j(null);
  }, [e, t]);
  se(() => {
    Jt().catch((N) => A(N instanceof Error ? N.message : String(N)));
  }, [Jt]), se(() => {
    q((N) => {
      let M = !1;
      const V = new Set(N);
      for (const X of Ut)
        V.has(X.category) || (V.add(X.category), M = !0);
      return M ? V : N;
    });
  }, [Ut]), se(() => {
    if (!$e) {
      C([]), k([]);
      return;
    }
    const N = we ? xm($e, d, l?.layout ?? []) : Fe ? mm(Fe, d, l?.layout ?? []) : { nodes: [], edges: [] };
    C(N.nodes), k(N.edges);
  }, [d, l?.layout, we, Fe, $e]);
  const Jo = (N) => {
    a((M) => M && { ...M, state: { ...M.state, rootActivity: N } });
  }, en = ge((N, M) => {
    if (l?.state.rootActivity && we)
      return;
    const V = Zs(N, ua(N));
    if (!l?.state.rootActivity) {
      Jo(V), j(V.nodeId);
      return;
    }
    if (!Fe) {
      if (!Qe(V)[0]) {
        P(""), A("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      a((ne) => {
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
      }), j(l.state.rootActivity.nodeId), A(""), P(`Wrapped root in ${_e(N)}`);
      return;
    }
    a((X) => {
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
  }, [l?.state.rootActivity, b, we, Fe]), It = ge((N, M) => {
    const V = Zs(N, ua(N)), X = {
      id: V.nodeId,
      type: "workflowActivity",
      position: M,
      selected: !0,
      data: {
        label: _e(N),
        activityVersionId: N.activityVersionId,
        activityTypeKey: N.activityTypeKey,
        category: N.category,
        executionType: N.executionType,
        icon: ii(N),
        childSlots: Qe(V),
        acceptsInbound: String(N.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: fl(V, N)
      }
    };
    return { activityNode: V, node: X };
  }, []), ze = ge((N, M, V = []) => {
    we || a((X) => {
      if (!X) return X;
      const ne = Sm(X.layout, N), he = X.state.rootActivity;
      if (!he) return { ...X, layout: ne };
      const le = Vr(he, b);
      if (!le) return { ...X, layout: ne };
      const pe = vm(le, N, M, V), Se = le.slot.mode === "flowchart" ? bm(pe, M) : pe;
      return {
        ...X,
        layout: ne,
        state: {
          ...X.state,
          rootActivity: ll(he, b, Se)
        }
      };
    });
  }, [b, we]), tn = ge((N, M) => {
    if (!Ee.current) return null;
    const V = Ee.current.getBoundingClientRect();
    return D ? D.screenToFlowPosition({ x: N, y: M }) : {
      x: N - V.left,
      y: M - V.top
    };
  }, [D]), nn = ge((N, M) => document.elementFromPoint(N, M)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), on = ge((N, M, V) => {
    const X = w.find((Ae) => Ae.id === M.source), ne = w.find((Ae) => Ae.id === M.target), he = X && ne ? $0(X, ne) : X ? da(X) : V, le = It(N, he), Se = [...w.map((Ae) => Ae.selected ? { ...Ae, selected: !1 } : Ae), le.node], yt = Dm(E, M, le.node.id);
    C(Se), k(yt), j(le.node.id), ze(Se, yt, [le.activityNode]);
  }, [ze, It, E, w]), jt = ge((N, M, V) => {
    if (!gt || !Ee.current) return !1;
    const X = Ee.current.getBoundingClientRect();
    if (!(M >= X.left && M <= X.right && V >= X.top && V <= X.bottom)) return !1;
    const he = tn(M, V);
    if (!he) return !1;
    if (ke) {
      const le = nn(M, V), pe = le ? E.find((Se) => Se.id === le) : void 0;
      if (pe)
        return on(N, pe, he), !0;
    }
    return en(N, he), !0;
  }, [en, gt, E, nn, ke, on, tn]);
  se(() => {
    const N = (V) => {
      const X = kt.current;
      if (!X) return;
      Math.hypot(V.clientX - X.startX, V.clientY - X.startY) >= o0 && (X.dragging = !0);
    }, M = (V) => {
      const X = kt.current;
      if (kt.current = null, !X?.dragging || !Ee.current) return;
      const ne = Ee.current.getBoundingClientRect();
      V.clientX >= ne.left && V.clientX <= ne.right && V.clientY >= ne.top && V.clientY <= ne.bottom && (tt.current = !0, window.setTimeout(() => {
        tt.current = !1;
      }, 0), jt(X.activity, V.clientX, V.clientY));
    };
    return window.addEventListener("pointermove", N), window.addEventListener("pointerup", M), window.addEventListener("pointercancel", M), () => {
      window.removeEventListener("pointermove", N), window.removeEventListener("pointerup", M), window.removeEventListener("pointercancel", M);
    };
  }, [D, jt]);
  const er = (N, M) => {
    N.dataTransfer.setData(Qs, M.activityVersionId), N.dataTransfer.setData("text/plain", M.activityVersionId), N.dataTransfer.effectAllowed = "copy";
  }, tr = (N, M) => {
    N.clientX === 0 && N.clientY === 0 || jt(M, N.clientX, N.clientY) && (tt.current = !0, window.setTimeout(() => {
      tt.current = !1;
    }, 0));
  }, nr = (N, M) => {
    N.button === 0 && (kt.current = {
      activity: M,
      startX: N.clientX,
      startY: N.clientY,
      dragging: !1
    });
  }, or = (N) => {
    tt.current || gt && en(N);
  }, rn = (N) => {
    if (!gt) {
      N.dataTransfer.dropEffect = "none";
      return;
    }
    if (N.preventDefault(), N.dataTransfer.dropEffect = "copy", !ke) return;
    const M = nn(N.clientX, N.clientY);
    I(M);
  }, sn = (N) => {
    if (!Ee.current) return;
    const M = N.relatedTarget;
    M && Ee.current.contains(M) || I(null);
  }, an = (N) => {
    if (N.preventDefault(), I(null), !gt) return;
    const M = N.dataTransfer.getData(Qs) || N.dataTransfer.getData("text/plain"), V = pt.get(M);
    V && jt(V, N.clientX, N.clientY);
  }, rr = () => {
    if (!ke) return;
    const N = Ee.current?.getBoundingClientRect();
    N && H({
      kind: "fromEmpty",
      clientX: N.left + N.width / 2,
      clientY: N.top + N.height / 2
    });
  }, cn = ge(async (N, M) => {
    const V = async () => {
      const ne = ++Te.current, he = mt(N);
      A("");
      try {
        const le = await cm(e, N), pe = mt(le);
        return Pe.current = pe, a((Se) => !Se || Se.id !== le.id ? Se : mt(Se) === he ? le : { ...Se, validationErrors: le.validationErrors }), ne === Te.current && P(M), le;
      } catch (le) {
        throw ne === Te.current && (P(""), A(le instanceof Error ? le.message : String(le))), le;
      }
    }, X = Ke.current.then(V, V);
    return Ke.current = X.catch(() => {
    }), X;
  }, [e]);
  se(() => {
    if (!Y || !l || mt(l) === Pe.current) return;
    P("Autosaving...");
    const M = window.setTimeout(() => {
      cn(l, "Autosaved").catch(() => {
      });
    }, r0);
    return () => window.clearTimeout(M);
  }, [Y, l, cn]);
  const ir = async () => {
    if (!(!l || st)) {
      O("saving"), P("Saving...");
      try {
        await cn(l, "Saved");
      } catch {
      } finally {
        O("idle");
      }
    }
  }, sr = async () => {
    if (!(!l || st)) {
      O("promoting"), P("Promoting...");
      try {
        const N = await lm(e, l.id), M = await um(e, N.versionId);
        Q(M.artifactId), P(`Published ${M.artifactVersion}`), await Jt();
      } catch (N) {
        P(""), A(N instanceof Error ? N.message : String(N));
      } finally {
        O("idle");
      }
    }
  }, ar = async () => {
    if (!l?.state.rootActivity || st) return;
    const N = l, M = mt(N);
    G(null), P("Preparing test run...");
    try {
      O("testRunPreparing"), P("Preparing test run...");
      const V = R0(N);
      O("testRunStarting"), P("Starting test run...");
      const X = await dm(e, {
        definitionId: N.definitionId,
        snapshotId: V,
        state: N.state
      });
      G({ draftSignature: M, view: X }), P(_l(X) ? "Test run rejected" : "Test run dispatched");
    } catch (V) {
      P(""), A(V instanceof Error ? V.message : String(V));
    } finally {
      O("idle");
    }
  }, cr = (N) => {
    const M = we ? N.filter((V) => V.type === "select") : N;
    M.length !== 0 && C((V) => Cc(M, V));
  }, Kn = (N) => {
    we || k((M) => _c(N, M));
  }, ln = (N) => !N.source || !N.target || N.source === N.target || !ke ? !1 : !N.targetHandle, lr = (N) => {
    if (!l?.state.rootActivity || !Fe || !ke || !ln(N)) return;
    const M = $o(N.source, N.target, N.sourceHandle ?? "Done", N.targetHandle ?? void 0), V = Ic(M, E);
    k(V), ze(w, V);
  }, ur = () => {
    ze(w, E);
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
    if (je.current = null, !M || !ke || N.target?.closest(".react-flow__handle, .react-flow__node")) return;
    const X = z0(N);
    H({
      kind: "fromPort",
      sourceNodeId: M.nodeId,
      sourceHandleId: M.handleId,
      clientX: X.x,
      clientY: X.y
    });
  }, hr = (N, M) => {
    if (!ke || !ln(M)) return;
    const V = Xp(N, {
      ...M,
      sourceHandle: M.sourceHandle ?? "Done",
      targetHandle: M.targetHandle ?? void 0
    }, E, { shouldReplaceId: !1 });
    k(V), ze(w, V);
  }, pr = (N) => {
    if (we || N.length === 0) return;
    const M = new Set(N.map((ne) => ne.id)), V = w.filter((ne) => !M.has(ne.id)), X = E.filter((ne) => !M.has(ne.source) && !M.has(ne.target));
    C(V), k(X), F && M.has(F) && j(null), ze(V, X);
  }, gr = (N) => {
    if (we || N.length === 0) return;
    const M = new Set(N.map((X) => X.id)), V = E.filter((X) => !M.has(X.id));
    k(V), ze(w, V);
  }, un = ge((N) => {
    if (we) return;
    const M = E.filter((V) => V.id !== N);
    k(M), ze(w, M);
  }, [ze, E, we, w]), Un = ge((N, M, V) => {
    ke && H({ kind: "spliceEdge", edgeId: N, clientX: M, clientY: V });
  }, [ke]), yr = (N) => {
    const M = z;
    if (!M) return;
    H(null);
    const V = tn(M.clientX, M.clientY) ?? { x: 0, y: 0 };
    if (M.kind === "fromEmpty") {
      const ne = It(N, V), le = [...w.map((pe) => pe.selected ? { ...pe, selected: !1 } : pe), ne.node];
      C(le), j(ne.node.id), ze(le, E, [ne.activityNode]);
      return;
    }
    if (M.kind === "fromPort") {
      const ne = w.find((Ae) => Ae.id === M.sourceNodeId), he = ne ? da(ne) : V, le = It(N, he), Se = [...w.map((Ae) => Ae.selected ? { ...Ae, selected: !1 } : Ae), le.node], yt = [...E, $o(M.sourceNodeId, le.node.id, M.sourceHandleId ?? "Done")];
      C(Se), k(yt), j(le.node.id), ze(Se, yt, [le.activityNode]);
      return;
    }
    const X = E.find((ne) => ne.id === M.edgeId);
    X && on(N, X, V);
  }, mr = ye(() => ({
    highlightedEdgeId: S,
    deleteEdge: un,
    requestInsertActivity: Un
  }), [un, S, Un]), xr = (N, M, V) => {
    g((X) => [...X, { ownerNodeId: N.nodeId, slotId: M, label: V }]), j(null);
  }, wr = ge((N) => {
    a((M) => {
      const V = M?.state.rootActivity;
      return !M || !V ? M : {
        ...M,
        state: {
          ...M.state,
          rootActivity: ul(V, N.nodeId, () => N)
        }
      };
    });
  }, []), vr = (N) => {
    q((M) => {
      const V = new Set(M);
      return V.has(N) ? V.delete(N) : V.add(N), V;
    });
  }, Gn = (N) => {
    Ie((M) => M === N ? null : M), N === "palette" ? L((M) => !M) : fe((M) => !M);
  }, Qn = (N) => {
    N === "palette" ? L(!1) : fe(!1), Ie((M) => M === N ? null : N);
  }, dn = (N, M) => {
    Ie(null), N === "palette" ? (L(!1), oe((V) => So(V + M, yn, mn))) : (fe(!1), J((V) => So(V + M, xn, wn)));
  }, Jn = (N, M) => {
    M.preventDefault(), Ie(null), N === "palette" ? L(!1) : fe(!1);
    const V = M.clientX, X = N === "palette" ? re : K, ne = N === "palette" ? yn : xn, he = N === "palette" ? mn : wn;
    document.body.classList.add("wf-side-panel-resizing");
    const le = (Se) => {
      const yt = N === "palette" ? Se.clientX - V : V - Se.clientX, Ae = So(X + yt, ne, he);
      N === "palette" ? oe(Ae) : J(Ae);
    }, pe = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", le), window.removeEventListener("pointerup", pe), window.removeEventListener("pointercancel", pe);
    };
    window.addEventListener("pointermove", le), window.addEventListener("pointerup", pe), window.addEventListener("pointercancel", pe);
  }, fn = (N, M) => {
    M.key === "ArrowLeft" ? (M.preventDefault(), dn(N, N === "palette" ? -ho : ho)) : M.key === "ArrowRight" ? (M.preventDefault(), dn(N, N === "palette" ? ho : -ho)) : M.key === "Home" ? (M.preventDefault(), N === "palette" ? oe(yn) : J(xn)) : M.key === "End" && (M.preventDefault(), N === "palette" ? oe(mn) : J(wn));
  };
  if (!s || !l)
    return /* @__PURE__ */ u.jsx("div", { className: "wf-empty", children: _ || "Loading workflow editor..." });
  const br = [
    "wf-editor-body",
    ie ? "palette-collapsed" : "",
    Z ? "inspector-collapsed" : "",
    ue === "palette" ? "palette-maximized" : "",
    ue === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), Sr = {
    "--wf-palette-width": `${ie ? oa : re}px`,
    "--wf-inspector-width": `${Z ? oa : K}px`
  }, At = !ie && ue !== "inspector", eo = !Z && ue !== "palette", to = B?.draftSignature === mt(l) ? B.view : null, no = {
    definition: s.definition,
    draft: l,
    selectedActivity: Ce,
    selectedActivityDescriptor: Gt,
    selectedActivitySlots: Qt,
    catalog: d,
    currentScopeOwner: $e,
    frames: b
  }, Di = r.map((N) => {
    const M = N.component;
    return {
      id: N.id,
      title: N.title,
      side: N.side,
      order: N.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ u.jsx(M, { context: no })
    };
  }), Nr = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ u.jsx(ol, { size: 15 }),
      render: kl
    },
    ...Di.filter((N) => N.side === "left")
  ].sort(la), Er = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ u.jsx(il, { size: 15 }),
      render: Il
    },
    ...Di.filter((N) => N.side === "right")
  ].sort(la), Pi = Nr.find((N) => N.id === Oe) ?? Nr[0], Ti = Er.find((N) => N.id === ft) ?? Er[0];
  function kl() {
    return /* @__PURE__ */ u.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: Ut.map((N) => {
      const M = R.has(N.category);
      return /* @__PURE__ */ u.jsxs("div", { className: "wf-palette-category", children: [
        /* @__PURE__ */ u.jsxs(
          "button",
          {
            type: "button",
            className: "wf-palette-category-toggle",
            role: "treeitem",
            "aria-expanded": M,
            onClick: () => vr(N.category),
            children: [
              M ? /* @__PURE__ */ u.jsx(Yy, { size: 14 }) : /* @__PURE__ */ u.jsx(Dt, { size: 14 }),
              /* @__PURE__ */ u.jsx("span", { children: N.category }),
              /* @__PURE__ */ u.jsx("small", { children: N.activities.length })
            ]
          }
        ),
        M ? /* @__PURE__ */ u.jsx("div", { className: "wf-palette-activities", role: "group", children: N.activities.map((V) => {
          const X = V.description?.trim(), ne = X ? `wf-palette-description-${V.activityVersionId}` : void 0, he = _e(V), le = ii(V);
          return /* @__PURE__ */ u.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-activity",
              role: "treeitem",
              draggable: !0,
              title: X || _e(V),
              "aria-describedby": ne,
              onClick: () => or(V),
              onDragStart: (pe) => er(pe, V),
              onDragEnd: (pe) => tr(pe, V),
              onPointerDown: (pe) => nr(pe, V),
              children: [
                /* @__PURE__ */ u.jsx("span", { className: "wf-activity-icon", "data-icon": le, "aria-hidden": "true", children: Cl(le) }),
                /* @__PURE__ */ u.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ u.jsx("strong", { children: he }),
                  X ? /* @__PURE__ */ u.jsx("small", { id: ne, children: X }) : null
                ] }),
                /* @__PURE__ */ u.jsx(Zy, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            V.activityVersionId
          );
        }) }) : null
      ] }, N.category);
    }) });
  }
  function Il() {
    return Ce ? /* @__PURE__ */ u.jsxs("div", { className: "wf-inspector-content", children: [
      /* @__PURE__ */ u.jsx("h3", { children: w.find((N) => N.id === Ce.nodeId)?.data.label ?? Ce.nodeId }),
      /* @__PURE__ */ u.jsxs("dl", { children: [
        /* @__PURE__ */ u.jsx("dt", { children: "Node ID" }),
        /* @__PURE__ */ u.jsx("dd", { children: Ce.nodeId }),
        /* @__PURE__ */ u.jsx("dt", { children: "Activity type" }),
        /* @__PURE__ */ u.jsx("dd", { children: Gt?.typeName ?? pt.get(Ce.activityVersionId)?.activityTypeKey ?? "Unknown" }),
        /* @__PURE__ */ u.jsx("dt", { children: "Activity version" }),
        /* @__PURE__ */ u.jsx("dd", { children: Ce.activityVersionId })
      ] }),
      /* @__PURE__ */ u.jsx(
        Zm,
        {
          activity: Ce,
          descriptor: Gt,
          editors: o,
          expressionDescriptors: y,
          descriptorStatus: v,
          onChange: wr
        }
      ),
      Qt.length > 0 ? /* @__PURE__ */ u.jsxs("div", { className: "wf-slot-list", children: [
        /* @__PURE__ */ u.jsx("span", { children: "Embedded slots" }),
        Qt.map((N) => /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => xr(Ce, N.id, `${w.find((M) => M.id === Ce.nodeId)?.data.label ?? Ce.nodeId} / ${N.label}`), children: [
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
      /* @__PURE__ */ u.jsx(Dt, { size: 14 }),
      /* @__PURE__ */ u.jsx("strong", { children: s.definition.name }),
      /* @__PURE__ */ u.jsx("span", { className: "wf-chip", children: "Draft" }),
      $ ? /* @__PURE__ */ u.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ u.jsx(Fn, { size: 13 }),
        " ",
        $
      ] }) : null,
      /* @__PURE__ */ u.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ u.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ u.jsx("input", { type: "checkbox", checked: Y, onChange: (N) => te(N.target.checked) }),
          /* @__PURE__ */ u.jsx("span", { children: "Autosave" })
        ] }),
        Yn ? /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => Zt(n, Yn, { definition: s.definition, draft: l }), children: [
          /* @__PURE__ */ u.jsx(qt, { size: 15 }),
          " Risks"
        ] }) : null,
        Zn ? /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => Zt(n, Zn, { definition: s.definition, draft: l }), children: [
          /* @__PURE__ */ u.jsx(qt, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ u.jsxs("button", { type: "button", disabled: st, onClick: () => {
          ir();
        }, children: [
          /* @__PURE__ */ u.jsx(Uy, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ u.jsxs("button", { type: "button", disabled: st, onClick: () => {
          sr();
        }, children: [
          /* @__PURE__ */ u.jsx(rl, { size: 15 }),
          " Promote"
        ] }),
        /* @__PURE__ */ u.jsxs(
          "button",
          {
            type: "button",
            disabled: !Qo,
            title: l.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              ar();
            },
            children: [
              /* @__PURE__ */ u.jsx(ki, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    _ ? /* @__PURE__ */ u.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ u.jsx(Xt, { size: 16 }),
      " ",
      _
    ] }) : null,
    to ? /* @__PURE__ */ u.jsx(T0, { testRun: to }) : null,
    /* @__PURE__ */ u.jsxs("div", { className: br, style: Sr, children: [
      /* @__PURE__ */ u.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ u.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ u.jsx(
            ca,
            {
              label: "Activities panel tabs",
              tabs: Nr,
              activeTabId: Pi.id,
              onSelect: Be
            }
          ),
          /* @__PURE__ */ u.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ u.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": ie ? "Expand activities panel" : "Collapse activities panel",
                title: ie ? "Expand" : "Collapse",
                onClick: () => Gn("palette"),
                children: ie ? /* @__PURE__ */ u.jsx(Dt, { size: 14 }) : /* @__PURE__ */ u.jsx(ni, { size: 14 })
              }
            ),
            ie ? null : /* @__PURE__ */ u.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": ue === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: ue === "palette" ? "Restore" : "Maximize",
                onClick: () => Qn("palette"),
                children: ue === "palette" ? /* @__PURE__ */ u.jsx(Xs, { size: 14 }) : /* @__PURE__ */ u.jsx(To, { size: 14 })
              }
            )
          ] })
        ] }),
        At ? Pi.render() : null
      ] }),
      At && !ue ? /* @__PURE__ */ u.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": yn,
          "aria-valuemax": mn,
          "aria-valuenow": re,
          tabIndex: 0,
          onPointerDown: (N) => Jn("palette", N),
          onKeyDown: (N) => fn("palette", N)
        }
      ) : /* @__PURE__ */ u.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ u.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ u.jsxs("div", { className: "wf-breadcrumb", children: [
          /* @__PURE__ */ u.jsx("button", { type: "button", onClick: () => {
            g([]), j(null);
          }, children: "Root" }),
          b.map((N, M) => /* @__PURE__ */ u.jsxs(dt.Fragment, { children: [
            /* @__PURE__ */ u.jsx(Dt, { size: 13 }),
            /* @__PURE__ */ u.jsx("button", { type: "button", onClick: () => {
              g(b.slice(0, M + 1)), j(null);
            }, children: N.label })
          ] }, `${N.ownerNodeId}-${N.slotId}-${M}`))
        ] }),
        /* @__PURE__ */ u.jsxs("div", { className: "wf-canvas", ref: Ee, onDragOver: rn, onDragLeave: sn, onDrop: an, children: [
          /* @__PURE__ */ u.jsx(xl.Provider, { value: mr, children: /* @__PURE__ */ u.jsxs(
            hy,
            {
              nodes: w,
              edges: E,
              nodeTypes: t0,
              edgeTypes: n0,
              onInit: T,
              onNodesChange: cr,
              onEdgesChange: Kn,
              onNodesDelete: pr,
              onEdgesDelete: gr,
              onConnect: lr,
              onConnectStart: ke ? dr : void 0,
              onConnectEnd: ke ? fr : void 0,
              onReconnect: ke ? hr : void 0,
              isValidConnection: ln,
              onDragOver: rn,
              onDragLeave: sn,
              onDrop: an,
              onPaneClick: () => j(null),
              onNodeClick: (N, M) => j(M.id),
              onNodeDragStop: we ? void 0 : ur,
              fitView: !0,
              minZoom: 0.2,
              maxZoom: 1.8,
              nodesConnectable: ke,
              nodesDraggable: !we,
              selectionOnDrag: !0,
              multiSelectionKeyCode: ["Shift", "Meta", "Control"],
              deleteKeyCode: we ? null : ["Backspace", "Delete"],
              panActivationKeyCode: null,
              defaultEdgeOptions: { type: "workflow" },
              children: [
                /* @__PURE__ */ u.jsx(vy, { gap: 18, size: 1 }),
                /* @__PURE__ */ u.jsx(ky, {}),
                /* @__PURE__ */ u.jsx(Vy, { pannable: !0, zoomable: !0 })
              ]
            }
          ) }),
          ke && w.length === 0 ? /* @__PURE__ */ u.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => rr(), children: [
            /* @__PURE__ */ u.jsx(Ii, { size: 15 }),
            " Add activity"
          ] }) : null,
          z ? /* @__PURE__ */ u.jsx(
            D0,
            {
              clientX: z.clientX,
              clientY: z.clientY,
              activities: d,
              onPick: yr,
              onClose: () => H(null)
            }
          ) : null
        ] }),
        /* @__PURE__ */ u.jsx(P0, { draft: l })
      ] }),
      eo && !ue ? /* @__PURE__ */ u.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": xn,
          "aria-valuemax": wn,
          "aria-valuenow": K,
          tabIndex: 0,
          onPointerDown: (N) => Jn("inspector", N),
          onKeyDown: (N) => fn("inspector", N)
        }
      ) : /* @__PURE__ */ u.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ u.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ u.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ u.jsx(
            ca,
            {
              label: "Inspector panel tabs",
              tabs: Er,
              activeTabId: Ti.id,
              onSelect: et
            }
          ),
          /* @__PURE__ */ u.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ u.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": Z ? "Expand inspector panel" : "Collapse inspector panel",
                title: Z ? "Expand" : "Collapse",
                onClick: () => Gn("inspector"),
                children: Z ? /* @__PURE__ */ u.jsx(ni, { size: 14 }) : /* @__PURE__ */ u.jsx(Dt, { size: 14 })
              }
            ),
            Z ? null : /* @__PURE__ */ u.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": ue === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: ue === "inspector" ? "Restore" : "Maximize",
                onClick: () => Qn("inspector"),
                children: ue === "inspector" ? /* @__PURE__ */ u.jsx(Xs, { size: 14 }) : /* @__PURE__ */ u.jsx(To, { size: 14 })
              }
            )
          ] })
        ] }),
        eo ? Ti.render() : null
      ] })
    ] })
  ] });
}
function ca({
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
function la(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
function j0({ data: e, selected: t }) {
  const n = e, o = !n.suppressFlowPorts, r = o ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], i = A0(n);
  return /* @__PURE__ */ u.jsxs("div", { className: t ? "wf-node selected" : "wf-node", "data-icon": n.icon ?? "activity", children: [
    o && n.acceptsInbound ? /* @__PURE__ */ u.jsx(Wt, { type: "target", position: ee.Left }) : null,
    /* @__PURE__ */ u.jsxs("div", { className: "wf-node-content", children: [
      /* @__PURE__ */ u.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: Cl(n.icon) }),
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
      return /* @__PURE__ */ u.jsxs(dt.Fragment, { children: [
        /* @__PURE__ */ u.jsx("span", { className: "wf-node-port-label", style: { top: l }, children: s.displayName }),
        /* @__PURE__ */ u.jsx(Wt, { type: "source", position: ee.Right, id: s.name, style: { top: l } })
      ] }, s.name);
    })
  ] });
}
function A0(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((r) => !!r).join(" · ");
}
function Cl(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ u.jsx(rl, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ u.jsx(il, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ u.jsx(Qy, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ u.jsx(ki, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ u.jsx(em, { size: 15 });
    default:
      return /* @__PURE__ */ u.jsx(ol, { size: 15 });
  }
}
function M0(e) {
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
  } = e, h = dt.useContext(xl), [p, y] = U(!1), [x, v, m] = Do({ sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: c }), b = h?.highlightedEdgeId === t;
  return /* @__PURE__ */ u.jsxs(u.Fragment, { children: [
    /* @__PURE__ */ u.jsx(
      Bn,
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
    h ? /* @__PURE__ */ u.jsx(gy, { children: /* @__PURE__ */ u.jsxs(
      "div",
      {
        className: ["wf-edge-actions", p ? "visible" : "", b ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${v}px, ${m}px)` },
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1),
        children: [
          /* @__PURE__ */ u.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (g) => h.requestInsertActivity(t, g.clientX, g.clientY), children: /* @__PURE__ */ u.jsx(Ii, { size: 12 }) }),
          /* @__PURE__ */ u.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => h.deleteEdge(t), children: /* @__PURE__ */ u.jsx(oi, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function D0({ clientX: e, clientY: t, activities: n, onPick: o, onClose: r }) {
  const [i, s] = U(""), [c, l] = U(0), a = ce(null), d = ce(null), f = ye(() => {
    const b = i.trim().toLowerCase(), g = n.filter(El);
    return b ? g.filter((w) => _e(w).toLowerCase().includes(b) || w.activityTypeKey.toLowerCase().includes(b) || (w.category ?? "").toLowerCase().includes(b) || (w.description ?? "").toLowerCase().includes(b)) : g;
  }, [n, i]), h = ye(() => bl(f), [f]), p = ye(() => h.flatMap((b) => b.activities), [h]);
  se(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), se(() => {
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
        const w = m, C = w === c;
        return /* @__PURE__ */ u.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": C,
            className: C ? "active" : "",
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
function P0({ draft: e }) {
  return e.validationErrors.length ? /* @__PURE__ */ u.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ u.jsx(Xt, { size: 14 }),
    e.validationErrors.length,
    " validation issue",
    e.validationErrors.length === 1 ? "" : "s"
  ] }) : /* @__PURE__ */ u.jsxs("div", { className: "wf-validation ok", children: [
    /* @__PURE__ */ u.jsx(Fn, { size: 14 }),
    " No validation errors"
  ] });
}
function T0({ testRun: e }) {
  const t = _l(e);
  return /* @__PURE__ */ u.jsxs("section", { className: "wf-test-run-capsule", "data-state": t ? "rejected" : "accepted", "aria-live": "polite", children: [
    /* @__PURE__ */ u.jsxs("div", { className: "wf-test-run-heading", children: [
      t ? /* @__PURE__ */ u.jsx(Xt, { size: 16 }) : /* @__PURE__ */ u.jsx(Fn, { size: 16 }),
      /* @__PURE__ */ u.jsxs("div", { children: [
        /* @__PURE__ */ u.jsx("strong", { children: t ? "Test run rejected" : "Test run dispatched" }),
        /* @__PURE__ */ u.jsx("span", { children: "Ephemeral - not promoted" })
      ] })
    ] }),
    t && e.reason ? /* @__PURE__ */ u.jsx("p", { children: e.reason }) : null,
    /* @__PURE__ */ u.jsxs("dl", { children: [
      /* @__PURE__ */ u.jsxs("div", { children: [
        /* @__PURE__ */ u.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ u.jsx("dd", { children: e.status })
      ] }),
      e.commandDispatchStatus ? /* @__PURE__ */ u.jsxs("div", { children: [
        /* @__PURE__ */ u.jsx("dt", { children: "Dispatch" }),
        /* @__PURE__ */ u.jsx("dd", { children: e.commandDispatchStatus })
      ] }) : null,
      /* @__PURE__ */ u.jsxs("div", { children: [
        /* @__PURE__ */ u.jsx("dt", { children: "Test run" }),
        /* @__PURE__ */ u.jsx("dd", { children: e.testRunId })
      ] }),
      e.artifactId ? /* @__PURE__ */ u.jsxs("div", { children: [
        /* @__PURE__ */ u.jsx("dt", { children: "Transient artifact" }),
        /* @__PURE__ */ u.jsx("dd", { children: e.artifactId })
      ] }) : null,
      e.workflowExecutionId ? /* @__PURE__ */ u.jsxs("div", { children: [
        /* @__PURE__ */ u.jsx("dt", { children: "Execution" }),
        /* @__PURE__ */ u.jsx("dd", { children: e.workflowExecutionId })
      ] }) : null,
      e.expiresAt ? /* @__PURE__ */ u.jsxs("div", { children: [
        /* @__PURE__ */ u.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ u.jsx("dd", { children: Ro(e.expiresAt) })
      ] }) : null
    ] })
  ] });
}
function ua(e) {
  return `${_e(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function da(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function $0(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function z0(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function mt(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function R0(e) {
  return `${e.id}-${L0(JSON.stringify(e.state))}`;
}
function L0(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function _l(e) {
  return e.status.toLowerCase() === "rejected";
}
function Ro(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
export {
  O0 as register
};
