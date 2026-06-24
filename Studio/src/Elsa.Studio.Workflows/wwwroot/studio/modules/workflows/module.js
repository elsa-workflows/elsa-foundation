import ht, { memo as ve, forwardRef as Oo, useRef as ce, useEffect as ie, useCallback as ge, useContext as Ln, useMemo as me, useState as Y, createContext as ui, useLayoutEffect as Wl, createElement as Xr, useId as va } from "react";
import "@tanstack/react-query";
function Xl(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var _r = { exports: {} }, pn = {};
var Hi;
function Yl() {
  if (Hi) return pn;
  Hi = 1;
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
  return pn.Fragment = t, pn.jsx = n, pn.jsxs = n, pn;
}
var Oi;
function ql() {
  return Oi || (Oi = 1, _r.exports = Yl()), _r.exports;
}
var a = ql();
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
var Zl = { value: () => {
} };
function Bo() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new xo(n);
}
function xo(e) {
  this._ = e;
}
function Kl(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
xo.prototype = Bo.prototype = {
  constructor: xo,
  on: function(e, t) {
    var n = this._, o = Kl(e + "", n), r, i = -1, s = o.length;
    if (arguments.length < 2) {
      for (; ++i < s; ) if ((r = (e = o[i]).type) && (r = Ul(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++i < s; )
      if (r = (e = o[i]).type) n[r] = Bi(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = Bi(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new xo(e);
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
function Ul(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function Bi(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = Zl, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var Yr = "http://www.w3.org/1999/xhtml";
const Fi = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: Yr,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function Fo(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Fi.hasOwnProperty(t) ? { space: Fi[t], local: e } : e;
}
function Gl(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === Yr && t.documentElement.namespaceURI === Yr ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Jl(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function ba(e) {
  var t = Fo(e);
  return (t.local ? Jl : Gl)(t);
}
function Ql() {
}
function di(e) {
  return e == null ? Ql : function() {
    return this.querySelector(e);
  };
}
function eu(e) {
  typeof e != "function" && (e = di(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = new Array(s), u, l, f = 0; f < s; ++f)
      (u = i[f]) && (l = e.call(u, u.__data__, f, i)) && ("__data__" in u && (l.__data__ = u.__data__), c[f] = l);
  return new Ve(o, this._parents);
}
function tu(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function nu() {
  return [];
}
function Sa(e) {
  return e == null ? nu : function() {
    return this.querySelectorAll(e);
  };
}
function ou(e) {
  return function() {
    return tu(e.apply(this, arguments));
  };
}
function ru(e) {
  typeof e == "function" ? e = ou(e) : e = Sa(e);
  for (var t = this._groups, n = t.length, o = [], r = [], i = 0; i < n; ++i)
    for (var s = t[i], c = s.length, u, l = 0; l < c; ++l)
      (u = s[l]) && (o.push(e.call(u, u.__data__, l, s)), r.push(u));
  return new Ve(o, r);
}
function Na(e) {
  return function() {
    return this.matches(e);
  };
}
function Ea(e) {
  return function(t) {
    return t.matches(e);
  };
}
var iu = Array.prototype.find;
function su(e) {
  return function() {
    return iu.call(this.children, e);
  };
}
function au() {
  return this.firstElementChild;
}
function cu(e) {
  return this.select(e == null ? au : su(typeof e == "function" ? e : Ea(e)));
}
var lu = Array.prototype.filter;
function uu() {
  return Array.from(this.children);
}
function du(e) {
  return function() {
    return lu.call(this.children, e);
  };
}
function fu(e) {
  return this.selectAll(e == null ? uu : du(typeof e == "function" ? e : Ea(e)));
}
function hu(e) {
  typeof e != "function" && (e = Na(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = [], u, l = 0; l < s; ++l)
      (u = i[l]) && e.call(u, u.__data__, l, i) && c.push(u);
  return new Ve(o, this._parents);
}
function ka(e) {
  return new Array(e.length);
}
function pu() {
  return new Ve(this._enter || this._groups.map(ka), this._parents);
}
function Co(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
Co.prototype = {
  constructor: Co,
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
function gu(e) {
  return function() {
    return e;
  };
}
function mu(e, t, n, o, r, i) {
  for (var s = 0, c, u = t.length, l = i.length; s < l; ++s)
    (c = t[s]) ? (c.__data__ = i[s], o[s] = c) : n[s] = new Co(e, i[s]);
  for (; s < u; ++s)
    (c = t[s]) && (r[s] = c);
}
function yu(e, t, n, o, r, i, s) {
  var c, u, l = /* @__PURE__ */ new Map(), f = t.length, d = i.length, h = new Array(f), p;
  for (c = 0; c < f; ++c)
    (u = t[c]) && (h[c] = p = s.call(u, u.__data__, c, t) + "", l.has(p) ? r[c] = u : l.set(p, u));
  for (c = 0; c < d; ++c)
    p = s.call(e, i[c], c, i) + "", (u = l.get(p)) ? (o[c] = u, u.__data__ = i[c], l.delete(p)) : n[c] = new Co(e, i[c]);
  for (c = 0; c < f; ++c)
    (u = t[c]) && l.get(h[c]) === u && (r[c] = u);
}
function xu(e) {
  return e.__data__;
}
function wu(e, t) {
  if (!arguments.length) return Array.from(this, xu);
  var n = t ? yu : mu, o = this._parents, r = this._groups;
  typeof e != "function" && (e = gu(e));
  for (var i = r.length, s = new Array(i), c = new Array(i), u = new Array(i), l = 0; l < i; ++l) {
    var f = o[l], d = r[l], h = d.length, p = vu(e.call(f, f && f.__data__, l, o)), g = p.length, x = c[l] = new Array(g), w = s[l] = new Array(g), y = u[l] = new Array(h);
    n(f, d, x, w, y, p, t);
    for (var b = 0, m = 0, v, k; b < g; ++b)
      if (v = x[b]) {
        for (b >= m && (m = b + 1); !(k = w[m]) && ++m < g; ) ;
        v._next = k || null;
      }
  }
  return s = new Ve(s, o), s._enter = c, s._exit = u, s;
}
function vu(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function bu() {
  return new Ve(this._exit || this._groups.map(ka), this._parents);
}
function Su(e, t, n) {
  var o = this.enter(), r = this, i = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? i.remove() : n(i), o && r ? o.merge(r).order() : r;
}
function Nu(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, i = o.length, s = Math.min(r, i), c = new Array(r), u = 0; u < s; ++u)
    for (var l = n[u], f = o[u], d = l.length, h = c[u] = new Array(d), p, g = 0; g < d; ++g)
      (p = l[g] || f[g]) && (h[g] = p);
  for (; u < r; ++u)
    c[u] = n[u];
  return new Ve(c, this._parents);
}
function Eu() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, i = o[r], s; --r >= 0; )
      (s = o[r]) && (i && s.compareDocumentPosition(i) ^ 4 && i.parentNode.insertBefore(s, i), i = s);
  return this;
}
function ku(e) {
  e || (e = Cu);
  function t(d, h) {
    return d && h ? e(d.__data__, h.__data__) : !d - !h;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), i = 0; i < o; ++i) {
    for (var s = n[i], c = s.length, u = r[i] = new Array(c), l, f = 0; f < c; ++f)
      (l = s[f]) && (u[f] = l);
    u.sort(t);
  }
  return new Ve(r, this._parents).order();
}
function Cu(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function Iu() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function ju() {
  return Array.from(this);
}
function _u() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length; r < i; ++r) {
      var s = o[r];
      if (s) return s;
    }
  return null;
}
function Au() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function Mu() {
  return !this.node();
}
function Du(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], i = 0, s = r.length, c; i < s; ++i)
      (c = r[i]) && e.call(c, c.__data__, i, r);
  return this;
}
function Pu(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function $u(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Tu(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function zu(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function Ru(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function Lu(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function Vu(e, t) {
  var n = Fo(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? $u : Pu : typeof t == "function" ? n.local ? Lu : Ru : n.local ? zu : Tu)(n, t));
}
function Ca(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function Hu(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Ou(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function Bu(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function Fu(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? Hu : typeof t == "function" ? Bu : Ou)(e, t, n ?? "")) : Ot(this.node(), e);
}
function Ot(e, t) {
  return e.style.getPropertyValue(t) || Ca(e).getComputedStyle(e, null).getPropertyValue(t);
}
function Wu(e) {
  return function() {
    delete this[e];
  };
}
function Xu(e, t) {
  return function() {
    this[e] = t;
  };
}
function Yu(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function qu(e, t) {
  return arguments.length > 1 ? this.each((t == null ? Wu : typeof t == "function" ? Yu : Xu)(e, t)) : this.node()[e];
}
function Ia(e) {
  return e.trim().split(/^|\s+/);
}
function fi(e) {
  return e.classList || new ja(e);
}
function ja(e) {
  this._node = e, this._names = Ia(e.getAttribute("class") || "");
}
ja.prototype = {
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
function _a(e, t) {
  for (var n = fi(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function Aa(e, t) {
  for (var n = fi(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function Zu(e) {
  return function() {
    _a(this, e);
  };
}
function Ku(e) {
  return function() {
    Aa(this, e);
  };
}
function Uu(e, t) {
  return function() {
    (t.apply(this, arguments) ? _a : Aa)(this, e);
  };
}
function Gu(e, t) {
  var n = Ia(e + "");
  if (arguments.length < 2) {
    for (var o = fi(this.node()), r = -1, i = n.length; ++r < i; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? Uu : t ? Zu : Ku)(n, t));
}
function Ju() {
  this.textContent = "";
}
function Qu(e) {
  return function() {
    this.textContent = e;
  };
}
function ed(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function td(e) {
  return arguments.length ? this.each(e == null ? Ju : (typeof e == "function" ? ed : Qu)(e)) : this.node().textContent;
}
function nd() {
  this.innerHTML = "";
}
function od(e) {
  return function() {
    this.innerHTML = e;
  };
}
function rd(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function id(e) {
  return arguments.length ? this.each(e == null ? nd : (typeof e == "function" ? rd : od)(e)) : this.node().innerHTML;
}
function sd() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function ad() {
  return this.each(sd);
}
function cd() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function ld() {
  return this.each(cd);
}
function ud(e) {
  var t = typeof e == "function" ? e : ba(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function dd() {
  return null;
}
function fd(e, t) {
  var n = typeof e == "function" ? e : ba(e), o = t == null ? dd : typeof t == "function" ? t : di(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function hd() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function pd() {
  return this.each(hd);
}
function gd() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function md() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function yd(e) {
  return this.select(e ? md : gd);
}
function xd(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function wd(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function vd(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function bd(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, i; n < r; ++n)
        i = t[n], (!e.type || i.type === e.type) && i.name === e.name ? this.removeEventListener(i.type, i.listener, i.options) : t[++o] = i;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function Sd(e, t, n) {
  return function() {
    var o = this.__on, r, i = wd(t);
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
function Nd(e, t, n) {
  var o = vd(e + ""), r, i = o.length, s;
  if (arguments.length < 2) {
    var c = this.node().__on;
    if (c) {
      for (var u = 0, l = c.length, f; u < l; ++u)
        for (r = 0, f = c[u]; r < i; ++r)
          if ((s = o[r]).type === f.type && s.name === f.name)
            return f.value;
    }
    return;
  }
  for (c = t ? Sd : bd, r = 0; r < i; ++r) this.each(c(o[r], t, n));
  return this;
}
function Ma(e, t, n) {
  var o = Ca(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function Ed(e, t) {
  return function() {
    return Ma(this, e, t);
  };
}
function kd(e, t) {
  return function() {
    return Ma(this, e, t.apply(this, arguments));
  };
}
function Cd(e, t) {
  return this.each((typeof t == "function" ? kd : Ed)(e, t));
}
function* Id() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length, s; r < i; ++r)
      (s = o[r]) && (yield s);
}
var Da = [null];
function Ve(e, t) {
  this._groups = e, this._parents = t;
}
function Vn() {
  return new Ve([[document.documentElement]], Da);
}
function jd() {
  return this;
}
Ve.prototype = Vn.prototype = {
  constructor: Ve,
  select: eu,
  selectAll: ru,
  selectChild: cu,
  selectChildren: fu,
  filter: hu,
  data: wu,
  enter: pu,
  exit: bu,
  join: Su,
  merge: Nu,
  selection: jd,
  order: Eu,
  sort: ku,
  call: Iu,
  nodes: ju,
  node: _u,
  size: Au,
  empty: Mu,
  each: Du,
  attr: Vu,
  style: Fu,
  property: qu,
  classed: Gu,
  text: td,
  html: id,
  raise: ad,
  lower: ld,
  append: ud,
  insert: fd,
  remove: pd,
  clone: yd,
  datum: xd,
  on: Nd,
  dispatch: Cd,
  [Symbol.iterator]: Id
};
function Le(e) {
  return typeof e == "string" ? new Ve([[document.querySelector(e)]], [document.documentElement]) : new Ve([[e]], Da);
}
function _d(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function We(e, t) {
  if (e = _d(e), t === void 0 && (t = e.currentTarget), t) {
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
const Ad = { passive: !1 }, jn = { capture: !0, passive: !1 };
function Ar(e) {
  e.stopImmediatePropagation();
}
function Vt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Pa(e) {
  var t = e.document.documentElement, n = Le(e).on("dragstart.drag", Vt, jn);
  "onselectstart" in t ? n.on("selectstart.drag", Vt, jn) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function $a(e, t) {
  var n = e.document.documentElement, o = Le(e).on("dragstart.drag", null);
  t && (o.on("click.drag", Vt, jn), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const so = (e) => () => e;
function qr(e, {
  sourceEvent: t,
  subject: n,
  target: o,
  identifier: r,
  active: i,
  x: s,
  y: c,
  dx: u,
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
    dx: { value: u, enumerable: !0, configurable: !0 },
    dy: { value: l, enumerable: !0, configurable: !0 },
    _: { value: f }
  });
}
qr.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function Md(e) {
  return !e.ctrlKey && !e.button;
}
function Dd() {
  return this.parentNode;
}
function Pd(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function $d() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Ta() {
  var e = Md, t = Dd, n = Pd, o = $d, r = {}, i = Bo("start", "drag", "end"), s = 0, c, u, l, f, d = 0;
  function h(v) {
    v.on("mousedown.drag", p).filter(o).on("touchstart.drag", w).on("touchmove.drag", y, Ad).on("touchend.drag touchcancel.drag", b).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function p(v, k) {
    if (!(f || !e.call(this, v, k))) {
      var E = m(this, t.call(this, v, k), v, k, "mouse");
      E && (Le(v.view).on("mousemove.drag", g, jn).on("mouseup.drag", x, jn), Pa(v.view), Ar(v), l = !1, c = v.clientX, u = v.clientY, E("start", v));
    }
  }
  function g(v) {
    if (Vt(v), !l) {
      var k = v.clientX - c, E = v.clientY - u;
      l = k * k + E * E > d;
    }
    r.mouse("drag", v);
  }
  function x(v) {
    Le(v.view).on("mousemove.drag mouseup.drag", null), $a(v.view, l), Vt(v), r.mouse("end", v);
  }
  function w(v, k) {
    if (e.call(this, v, k)) {
      var E = v.changedTouches, I = t.call(this, v, k), D = E.length, $, F;
      for ($ = 0; $ < D; ++$)
        (F = m(this, I, v, k, E[$].identifier, E[$])) && (Ar(v), F("start", v, E[$]));
    }
  }
  function y(v) {
    var k = v.changedTouches, E = k.length, I, D;
    for (I = 0; I < E; ++I)
      (D = r[k[I].identifier]) && (Vt(v), D("drag", v, k[I]));
  }
  function b(v) {
    var k = v.changedTouches, E = k.length, I, D;
    for (f && clearTimeout(f), f = setTimeout(function() {
      f = null;
    }, 500), I = 0; I < E; ++I)
      (D = r[k[I].identifier]) && (Ar(v), D("end", v, k[I]));
  }
  function m(v, k, E, I, D, $) {
    var F = i.copy(), _ = We($ || E, k), z, V, S;
    if ((S = n.call(v, new qr("beforestart", {
      sourceEvent: E,
      target: h,
      identifier: D,
      active: s,
      x: _[0],
      y: _[1],
      dx: 0,
      dy: 0,
      dispatch: F
    }), I)) != null)
      return z = S.x - _[0] || 0, V = S.y - _[1] || 0, function j(C, A, T) {
        var P = _, W;
        switch (C) {
          case "start":
            r[D] = j, W = s++;
            break;
          case "end":
            delete r[D], --s;
          // falls through
          case "drag":
            _ = We(T || A, k), W = s;
            break;
        }
        F.call(
          C,
          v,
          new qr(C, {
            sourceEvent: A,
            subject: S,
            target: h,
            identifier: D,
            active: W,
            x: _[0] + z,
            y: _[1] + V,
            dx: _[0] - P[0],
            dy: _[1] - P[1],
            dispatch: F
          }),
          I
        );
      };
  }
  return h.filter = function(v) {
    return arguments.length ? (e = typeof v == "function" ? v : so(!!v), h) : e;
  }, h.container = function(v) {
    return arguments.length ? (t = typeof v == "function" ? v : so(v), h) : t;
  }, h.subject = function(v) {
    return arguments.length ? (n = typeof v == "function" ? v : so(v), h) : n;
  }, h.touchable = function(v) {
    return arguments.length ? (o = typeof v == "function" ? v : so(!!v), h) : o;
  }, h.on = function() {
    var v = i.on.apply(i, arguments);
    return v === i ? h : v;
  }, h.clickDistance = function(v) {
    return arguments.length ? (d = (v = +v) * v, h) : Math.sqrt(d);
  }, h;
}
function hi(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function za(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function Hn() {
}
var _n = 0.7, Io = 1 / _n, Ht = "\\s*([+-]?\\d+)\\s*", An = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", Ge = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", Td = /^#([0-9a-f]{3,8})$/, zd = new RegExp(`^rgb\\(${Ht},${Ht},${Ht}\\)$`), Rd = new RegExp(`^rgb\\(${Ge},${Ge},${Ge}\\)$`), Ld = new RegExp(`^rgba\\(${Ht},${Ht},${Ht},${An}\\)$`), Vd = new RegExp(`^rgba\\(${Ge},${Ge},${Ge},${An}\\)$`), Hd = new RegExp(`^hsl\\(${An},${Ge},${Ge}\\)$`), Od = new RegExp(`^hsla\\(${An},${Ge},${Ge},${An}\\)$`), Wi = {
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
hi(Hn, Et, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Xi,
  // Deprecated! Use color.formatHex.
  formatHex: Xi,
  formatHex8: Bd,
  formatHsl: Fd,
  formatRgb: Yi,
  toString: Yi
});
function Xi() {
  return this.rgb().formatHex();
}
function Bd() {
  return this.rgb().formatHex8();
}
function Fd() {
  return Ra(this).formatHsl();
}
function Yi() {
  return this.rgb().formatRgb();
}
function Et(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = Td.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? qi(t) : n === 3 ? new De(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? ao(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? ao(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = zd.exec(e)) ? new De(t[1], t[2], t[3], 1) : (t = Rd.exec(e)) ? new De(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = Ld.exec(e)) ? ao(t[1], t[2], t[3], t[4]) : (t = Vd.exec(e)) ? ao(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = Hd.exec(e)) ? Ui(t[1], t[2] / 100, t[3] / 100, 1) : (t = Od.exec(e)) ? Ui(t[1], t[2] / 100, t[3] / 100, t[4]) : Wi.hasOwnProperty(e) ? qi(Wi[e]) : e === "transparent" ? new De(NaN, NaN, NaN, 0) : null;
}
function qi(e) {
  return new De(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function ao(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new De(e, t, n, o);
}
function Wd(e) {
  return e instanceof Hn || (e = Et(e)), e ? (e = e.rgb(), new De(e.r, e.g, e.b, e.opacity)) : new De();
}
function Zr(e, t, n, o) {
  return arguments.length === 1 ? Wd(e) : new De(e, t, n, o ?? 1);
}
function De(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
hi(De, Zr, za(Hn, {
  brighter(e) {
    return e = e == null ? Io : Math.pow(Io, e), new De(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? _n : Math.pow(_n, e), new De(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new De(St(this.r), St(this.g), St(this.b), jo(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: Zi,
  // Deprecated! Use color.formatHex.
  formatHex: Zi,
  formatHex8: Xd,
  formatRgb: Ki,
  toString: Ki
}));
function Zi() {
  return `#${bt(this.r)}${bt(this.g)}${bt(this.b)}`;
}
function Xd() {
  return `#${bt(this.r)}${bt(this.g)}${bt(this.b)}${bt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function Ki() {
  const e = jo(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${St(this.r)}, ${St(this.g)}, ${St(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function jo(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function St(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function bt(e) {
  return e = St(e), (e < 16 ? "0" : "") + e.toString(16);
}
function Ui(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Xe(e, t, n, o);
}
function Ra(e) {
  if (e instanceof Xe) return new Xe(e.h, e.s, e.l, e.opacity);
  if (e instanceof Hn || (e = Et(e)), !e) return new Xe();
  if (e instanceof Xe) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), i = Math.max(t, n, o), s = NaN, c = i - r, u = (i + r) / 2;
  return c ? (t === i ? s = (n - o) / c + (n < o) * 6 : n === i ? s = (o - t) / c + 2 : s = (t - n) / c + 4, c /= u < 0.5 ? i + r : 2 - i - r, s *= 60) : c = u > 0 && u < 1 ? 0 : s, new Xe(s, c, u, e.opacity);
}
function Yd(e, t, n, o) {
  return arguments.length === 1 ? Ra(e) : new Xe(e, t, n, o ?? 1);
}
function Xe(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
hi(Xe, Yd, za(Hn, {
  brighter(e) {
    return e = e == null ? Io : Math.pow(Io, e), new Xe(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? _n : Math.pow(_n, e), new Xe(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new De(
      Mr(e >= 240 ? e - 240 : e + 120, r, o),
      Mr(e, r, o),
      Mr(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new Xe(Gi(this.h), co(this.s), co(this.l), jo(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = jo(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${Gi(this.h)}, ${co(this.s) * 100}%, ${co(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function Gi(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function co(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function Mr(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const pi = (e) => () => e;
function qd(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function Zd(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function Kd(e) {
  return (e = +e) == 1 ? La : function(t, n) {
    return n - t ? Zd(t, n, e) : pi(isNaN(t) ? n : t);
  };
}
function La(e, t) {
  var n = t - e;
  return n ? qd(e, n) : pi(isNaN(e) ? t : e);
}
const _o = (function e(t) {
  var n = Kd(t);
  function o(r, i) {
    var s = n((r = Zr(r)).r, (i = Zr(i)).r), c = n(r.g, i.g), u = n(r.b, i.b), l = La(r.opacity, i.opacity);
    return function(f) {
      return r.r = s(f), r.g = c(f), r.b = u(f), r.opacity = l(f), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function Ud(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(i) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - i) + t[r] * i;
    return o;
  };
}
function Gd(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Jd(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), i = new Array(n), s;
  for (s = 0; s < o; ++s) r[s] = kn(e[s], t[s]);
  for (; s < n; ++s) i[s] = t[s];
  return function(c) {
    for (s = 0; s < o; ++s) i[s] = r[s](c);
    return i;
  };
}
function Qd(e, t) {
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
function ef(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = kn(e[r], t[r]) : o[r] = t[r];
  return function(i) {
    for (r in n) o[r] = n[r](i);
    return o;
  };
}
var Kr = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, Dr = new RegExp(Kr.source, "g");
function tf(e) {
  return function() {
    return e;
  };
}
function nf(e) {
  return function(t) {
    return e(t) + "";
  };
}
function Va(e, t) {
  var n = Kr.lastIndex = Dr.lastIndex = 0, o, r, i, s = -1, c = [], u = [];
  for (e = e + "", t = t + ""; (o = Kr.exec(e)) && (r = Dr.exec(t)); )
    (i = r.index) > n && (i = t.slice(n, i), c[s] ? c[s] += i : c[++s] = i), (o = o[0]) === (r = r[0]) ? c[s] ? c[s] += r : c[++s] = r : (c[++s] = null, u.push({ i: s, x: Ue(o, r) })), n = Dr.lastIndex;
  return n < t.length && (i = t.slice(n), c[s] ? c[s] += i : c[++s] = i), c.length < 2 ? u[0] ? nf(u[0].x) : tf(t) : (t = u.length, function(l) {
    for (var f = 0, d; f < t; ++f) c[(d = u[f]).i] = d.x(l);
    return c.join("");
  });
}
function kn(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? pi(t) : (n === "number" ? Ue : n === "string" ? (o = Et(t)) ? (t = o, _o) : Va : t instanceof Et ? _o : t instanceof Date ? Qd : Gd(t) ? Ud : Array.isArray(t) ? Jd : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? ef : Ue)(e, t);
}
var Ji = 180 / Math.PI, Ur = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function Ha(e, t, n, o, r, i) {
  var s, c, u;
  return (s = Math.sqrt(e * e + t * t)) && (e /= s, t /= s), (u = e * n + t * o) && (n -= e * u, o -= t * u), (c = Math.sqrt(n * n + o * o)) && (n /= c, o /= c, u /= c), e * o < t * n && (e = -e, t = -t, u = -u, s = -s), {
    translateX: r,
    translateY: i,
    rotate: Math.atan2(t, e) * Ji,
    skewX: Math.atan(u) * Ji,
    scaleX: s,
    scaleY: c
  };
}
var lo;
function of(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Ur : Ha(t.a, t.b, t.c, t.d, t.e, t.f);
}
function rf(e) {
  return e == null || (lo || (lo = document.createElementNS("http://www.w3.org/2000/svg", "g")), lo.setAttribute("transform", e), !(e = lo.transform.baseVal.consolidate())) ? Ur : (e = e.matrix, Ha(e.a, e.b, e.c, e.d, e.e, e.f));
}
function Oa(e, t, n, o) {
  function r(l) {
    return l.length ? l.pop() + " " : "";
  }
  function i(l, f, d, h, p, g) {
    if (l !== d || f !== h) {
      var x = p.push("translate(", null, t, null, n);
      g.push({ i: x - 4, x: Ue(l, d) }, { i: x - 2, x: Ue(f, h) });
    } else (d || h) && p.push("translate(" + d + t + h + n);
  }
  function s(l, f, d, h) {
    l !== f ? (l - f > 180 ? f += 360 : f - l > 180 && (l += 360), h.push({ i: d.push(r(d) + "rotate(", null, o) - 2, x: Ue(l, f) })) : f && d.push(r(d) + "rotate(" + f + o);
  }
  function c(l, f, d, h) {
    l !== f ? h.push({ i: d.push(r(d) + "skewX(", null, o) - 2, x: Ue(l, f) }) : f && d.push(r(d) + "skewX(" + f + o);
  }
  function u(l, f, d, h, p, g) {
    if (l !== d || f !== h) {
      var x = p.push(r(p) + "scale(", null, ",", null, ")");
      g.push({ i: x - 4, x: Ue(l, d) }, { i: x - 2, x: Ue(f, h) });
    } else (d !== 1 || h !== 1) && p.push(r(p) + "scale(" + d + "," + h + ")");
  }
  return function(l, f) {
    var d = [], h = [];
    return l = e(l), f = e(f), i(l.translateX, l.translateY, f.translateX, f.translateY, d, h), s(l.rotate, f.rotate, d, h), c(l.skewX, f.skewX, d, h), u(l.scaleX, l.scaleY, f.scaleX, f.scaleY, d, h), l = f = null, function(p) {
      for (var g = -1, x = h.length, w; ++g < x; ) d[(w = h[g]).i] = w.x(p);
      return d.join("");
    };
  };
}
var sf = Oa(of, "px, ", "px)", "deg)"), af = Oa(rf, ", ", ")", ")"), cf = 1e-12;
function Qi(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function lf(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function uf(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const wo = (function e(t, n, o) {
  function r(i, s) {
    var c = i[0], u = i[1], l = i[2], f = s[0], d = s[1], h = s[2], p = f - c, g = d - u, x = p * p + g * g, w, y;
    if (x < cf)
      y = Math.log(h / l) / t, w = function(I) {
        return [
          c + I * p,
          u + I * g,
          l * Math.exp(t * I * y)
        ];
      };
    else {
      var b = Math.sqrt(x), m = (h * h - l * l + o * x) / (2 * l * n * b), v = (h * h - l * l - o * x) / (2 * h * n * b), k = Math.log(Math.sqrt(m * m + 1) - m), E = Math.log(Math.sqrt(v * v + 1) - v);
      y = (E - k) / t, w = function(I) {
        var D = I * y, $ = Qi(k), F = l / (n * b) * ($ * uf(t * D + k) - lf(k));
        return [
          c + F * p,
          u + F * g,
          l * $ / Qi(t * D + k)
        ];
      };
    }
    return w.duration = y * 1e3 * t / Math.SQRT2, w;
  }
  return r.rho = function(i) {
    var s = Math.max(1e-3, +i), c = s * s, u = c * c;
    return e(s, c, u);
  }, r;
})(Math.SQRT2, 2, 4);
var Bt = 0, Sn = 0, gn = 0, Ba = 1e3, Ao, Nn, Mo = 0, kt = 0, Wo = 0, Mn = typeof performance == "object" && performance.now ? performance : Date, Fa = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function gi() {
  return kt || (Fa(df), kt = Mn.now() + Wo);
}
function df() {
  kt = 0;
}
function Do() {
  this._call = this._time = this._next = null;
}
Do.prototype = Wa.prototype = {
  constructor: Do,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? gi() : +n) + (t == null ? 0 : +t), !this._next && Nn !== this && (Nn ? Nn._next = this : Ao = this, Nn = this), this._call = e, this._time = n, Gr();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Gr());
  }
};
function Wa(e, t, n) {
  var o = new Do();
  return o.restart(e, t, n), o;
}
function ff() {
  gi(), ++Bt;
  for (var e = Ao, t; e; )
    (t = kt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --Bt;
}
function es() {
  kt = (Mo = Mn.now()) + Wo, Bt = Sn = 0;
  try {
    ff();
  } finally {
    Bt = 0, pf(), kt = 0;
  }
}
function hf() {
  var e = Mn.now(), t = e - Mo;
  t > Ba && (Wo -= t, Mo = e);
}
function pf() {
  for (var e, t = Ao, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Ao = n);
  Nn = e, Gr(o);
}
function Gr(e) {
  if (!Bt) {
    Sn && (Sn = clearTimeout(Sn));
    var t = e - kt;
    t > 24 ? (e < 1 / 0 && (Sn = setTimeout(es, e - Mn.now() - Wo)), gn && (gn = clearInterval(gn))) : (gn || (Mo = Mn.now(), gn = setInterval(hf, Ba)), Bt = 1, Fa(es));
  }
}
function ts(e, t, n) {
  var o = new Do();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var gf = Bo("start", "end", "cancel", "interrupt"), mf = [], Xa = 0, ns = 1, Jr = 2, vo = 3, os = 4, Qr = 5, bo = 6;
function Xo(e, t, n, o, r, i) {
  var s = e.__transition;
  if (!s) e.__transition = {};
  else if (n in s) return;
  yf(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: gf,
    tween: mf,
    time: i.time,
    delay: i.delay,
    duration: i.duration,
    ease: i.ease,
    timer: null,
    state: Xa
  });
}
function mi(e, t) {
  var n = Ze(e, t);
  if (n.state > Xa) throw new Error("too late; already scheduled");
  return n;
}
function Qe(e, t) {
  var n = Ze(e, t);
  if (n.state > vo) throw new Error("too late; already running");
  return n;
}
function Ze(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function yf(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = Wa(i, 0, n.time);
  function i(l) {
    n.state = ns, n.timer.restart(s, n.delay, n.time), n.delay <= l && s(l - n.delay);
  }
  function s(l) {
    var f, d, h, p;
    if (n.state !== ns) return u();
    for (f in o)
      if (p = o[f], p.name === n.name) {
        if (p.state === vo) return ts(s);
        p.state === os ? (p.state = bo, p.timer.stop(), p.on.call("interrupt", e, e.__data__, p.index, p.group), delete o[f]) : +f < t && (p.state = bo, p.timer.stop(), p.on.call("cancel", e, e.__data__, p.index, p.group), delete o[f]);
      }
    if (ts(function() {
      n.state === vo && (n.state = os, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = Jr, n.on.call("start", e, e.__data__, n.index, n.group), n.state === Jr) {
      for (n.state = vo, r = new Array(h = n.tween.length), f = 0, d = -1; f < h; ++f)
        (p = n.tween[f].value.call(e, e.__data__, n.index, n.group)) && (r[++d] = p);
      r.length = d + 1;
    }
  }
  function c(l) {
    for (var f = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(u), n.state = Qr, 1), d = -1, h = r.length; ++d < h; )
      r[d].call(e, f);
    n.state === Qr && (n.on.call("end", e, e.__data__, n.index, n.group), u());
  }
  function u() {
    n.state = bo, n.timer.stop(), delete o[t];
    for (var l in o) return;
    delete e.__transition;
  }
}
function So(e, t) {
  var n = e.__transition, o, r, i = !0, s;
  if (n) {
    t = t == null ? null : t + "";
    for (s in n) {
      if ((o = n[s]).name !== t) {
        i = !1;
        continue;
      }
      r = o.state > Jr && o.state < Qr, o.state = bo, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[s];
    }
    i && delete e.__transition;
  }
}
function xf(e) {
  return this.each(function() {
    So(this, e);
  });
}
function wf(e, t) {
  var n, o;
  return function() {
    var r = Qe(this, e), i = r.tween;
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
function vf(e, t, n) {
  var o, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var i = Qe(this, e), s = i.tween;
    if (s !== o) {
      r = (o = s).slice();
      for (var c = { name: t, value: n }, u = 0, l = r.length; u < l; ++u)
        if (r[u].name === t) {
          r[u] = c;
          break;
        }
      u === l && r.push(c);
    }
    i.tween = r;
  };
}
function bf(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = Ze(this.node(), n).tween, r = 0, i = o.length, s; r < i; ++r)
      if ((s = o[r]).name === e)
        return s.value;
    return null;
  }
  return this.each((t == null ? wf : vf)(n, e, t));
}
function yi(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = Qe(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return Ze(r, o).value[t];
  };
}
function Ya(e, t) {
  var n;
  return (typeof t == "number" ? Ue : t instanceof Et ? _o : (n = Et(t)) ? (t = n, _o) : Va)(e, t);
}
function Sf(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Nf(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Ef(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttribute(e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function kf(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttributeNS(e.space, e.local);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Cf(e, t, n) {
  var o, r, i;
  return function() {
    var s, c = n(this), u;
    return c == null ? void this.removeAttribute(e) : (s = this.getAttribute(e), u = c + "", s === u ? null : s === o && u === r ? i : (r = u, i = t(o = s, c)));
  };
}
function If(e, t, n) {
  var o, r, i;
  return function() {
    var s, c = n(this), u;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (s = this.getAttributeNS(e.space, e.local), u = c + "", s === u ? null : s === o && u === r ? i : (r = u, i = t(o = s, c)));
  };
}
function jf(e, t) {
  var n = Fo(e), o = n === "transform" ? af : Ya;
  return this.attrTween(e, typeof t == "function" ? (n.local ? If : Cf)(n, o, yi(this, "attr." + e, t)) : t == null ? (n.local ? Nf : Sf)(n) : (n.local ? kf : Ef)(n, o, t));
}
function _f(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function Af(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function Mf(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && Af(e, i)), n;
  }
  return r._value = t, r;
}
function Df(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && _f(e, i)), n;
  }
  return r._value = t, r;
}
function Pf(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = Fo(e);
  return this.tween(n, (o.local ? Mf : Df)(o, t));
}
function $f(e, t) {
  return function() {
    mi(this, e).delay = +t.apply(this, arguments);
  };
}
function Tf(e, t) {
  return t = +t, function() {
    mi(this, e).delay = t;
  };
}
function zf(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? $f : Tf)(t, e)) : Ze(this.node(), t).delay;
}
function Rf(e, t) {
  return function() {
    Qe(this, e).duration = +t.apply(this, arguments);
  };
}
function Lf(e, t) {
  return t = +t, function() {
    Qe(this, e).duration = t;
  };
}
function Vf(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Rf : Lf)(t, e)) : Ze(this.node(), t).duration;
}
function Hf(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    Qe(this, e).ease = t;
  };
}
function Of(e) {
  var t = this._id;
  return arguments.length ? this.each(Hf(t, e)) : Ze(this.node(), t).ease;
}
function Bf(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    Qe(this, e).ease = n;
  };
}
function Ff(e) {
  if (typeof e != "function") throw new Error();
  return this.each(Bf(this._id, e));
}
function Wf(e) {
  typeof e != "function" && (e = Na(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = [], u, l = 0; l < s; ++l)
      (u = i[l]) && e.call(u, u.__data__, l, i) && c.push(u);
  return new it(o, this._parents, this._name, this._id);
}
function Xf(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, i = Math.min(o, r), s = new Array(o), c = 0; c < i; ++c)
    for (var u = t[c], l = n[c], f = u.length, d = s[c] = new Array(f), h, p = 0; p < f; ++p)
      (h = u[p] || l[p]) && (d[p] = h);
  for (; c < o; ++c)
    s[c] = t[c];
  return new it(s, this._parents, this._name, this._id);
}
function Yf(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function qf(e, t, n) {
  var o, r, i = Yf(t) ? mi : Qe;
  return function() {
    var s = i(this, e), c = s.on;
    c !== o && (r = (o = c).copy()).on(t, n), s.on = r;
  };
}
function Zf(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Ze(this.node(), n).on.on(e) : this.each(qf(n, e, t));
}
function Kf(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function Uf() {
  return this.on("end.remove", Kf(this._id));
}
function Gf(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = di(e));
  for (var o = this._groups, r = o.length, i = new Array(r), s = 0; s < r; ++s)
    for (var c = o[s], u = c.length, l = i[s] = new Array(u), f, d, h = 0; h < u; ++h)
      (f = c[h]) && (d = e.call(f, f.__data__, h, c)) && ("__data__" in f && (d.__data__ = f.__data__), l[h] = d, Xo(l[h], t, n, h, l, Ze(f, n)));
  return new it(i, this._parents, t, n);
}
function Jf(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Sa(e));
  for (var o = this._groups, r = o.length, i = [], s = [], c = 0; c < r; ++c)
    for (var u = o[c], l = u.length, f, d = 0; d < l; ++d)
      if (f = u[d]) {
        for (var h = e.call(f, f.__data__, d, u), p, g = Ze(f, n), x = 0, w = h.length; x < w; ++x)
          (p = h[x]) && Xo(p, t, n, x, h, g);
        i.push(h), s.push(f);
      }
  return new it(i, s, t, n);
}
var Qf = Vn.prototype.constructor;
function eh() {
  return new Qf(this._groups, this._parents);
}
function th(e, t) {
  var n, o, r;
  return function() {
    var i = Ot(this, e), s = (this.style.removeProperty(e), Ot(this, e));
    return i === s ? null : i === n && s === o ? r : r = t(n = i, o = s);
  };
}
function qa(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function nh(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = Ot(this, e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function oh(e, t, n) {
  var o, r, i;
  return function() {
    var s = Ot(this, e), c = n(this), u = c + "";
    return c == null && (u = c = (this.style.removeProperty(e), Ot(this, e))), s === u ? null : s === o && u === r ? i : (r = u, i = t(o = s, c));
  };
}
function rh(e, t) {
  var n, o, r, i = "style." + t, s = "end." + i, c;
  return function() {
    var u = Qe(this, e), l = u.on, f = u.value[i] == null ? c || (c = qa(t)) : void 0;
    (l !== n || r !== f) && (o = (n = l).copy()).on(s, r = f), u.on = o;
  };
}
function ih(e, t, n) {
  var o = (e += "") == "transform" ? sf : Ya;
  return t == null ? this.styleTween(e, th(e, o)).on("end.style." + e, qa(e)) : typeof t == "function" ? this.styleTween(e, oh(e, o, yi(this, "style." + e, t))).each(rh(this._id, e)) : this.styleTween(e, nh(e, o, t), n).on("end.style." + e, null);
}
function sh(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function ah(e, t, n) {
  var o, r;
  function i() {
    var s = t.apply(this, arguments);
    return s !== r && (o = (r = s) && sh(e, s, n)), o;
  }
  return i._value = t, i;
}
function ch(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, ah(e, t, n ?? ""));
}
function lh(e) {
  return function() {
    this.textContent = e;
  };
}
function uh(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function dh(e) {
  return this.tween("text", typeof e == "function" ? uh(yi(this, "text", e)) : lh(e == null ? "" : e + ""));
}
function fh(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function hh(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && fh(r)), t;
  }
  return o._value = e, o;
}
function ph(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, hh(e));
}
function gh() {
  for (var e = this._name, t = this._id, n = Za(), o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], c = s.length, u, l = 0; l < c; ++l)
      if (u = s[l]) {
        var f = Ze(u, t);
        Xo(u, e, n, l, s, {
          time: f.time + f.delay + f.duration,
          delay: 0,
          duration: f.duration,
          ease: f.ease
        });
      }
  return new it(o, this._parents, e, n);
}
function mh() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(i, s) {
    var c = { value: s }, u = { value: function() {
      --r === 0 && i();
    } };
    n.each(function() {
      var l = Qe(this, o), f = l.on;
      f !== e && (t = (e = f).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(u)), l.on = t;
    }), r === 0 && i();
  });
}
var yh = 0;
function it(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function Za() {
  return ++yh;
}
var nt = Vn.prototype;
it.prototype = {
  constructor: it,
  select: Gf,
  selectAll: Jf,
  selectChild: nt.selectChild,
  selectChildren: nt.selectChildren,
  filter: Wf,
  merge: Xf,
  selection: eh,
  transition: gh,
  call: nt.call,
  nodes: nt.nodes,
  node: nt.node,
  size: nt.size,
  empty: nt.empty,
  each: nt.each,
  on: Zf,
  attr: jf,
  attrTween: Pf,
  style: ih,
  styleTween: ch,
  text: dh,
  textTween: ph,
  remove: Uf,
  tween: bf,
  delay: zf,
  duration: Vf,
  ease: Of,
  easeVarying: Ff,
  end: mh,
  [Symbol.iterator]: nt[Symbol.iterator]
};
function xh(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var wh = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: xh
};
function vh(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function bh(e) {
  var t, n;
  e instanceof it ? (t = e._id, e = e._name) : (t = Za(), (n = wh).time = gi(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], c = s.length, u, l = 0; l < c; ++l)
      (u = s[l]) && Xo(u, e, t, l, s, n || vh(u, t));
  return new it(o, this._parents, e, t);
}
Vn.prototype.interrupt = xf;
Vn.prototype.transition = bh;
const uo = (e) => () => e;
function Sh(e, {
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
var Yo = new ot(1, 0, 0);
Ka.prototype = ot.prototype;
function Ka(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return Yo;
  return e.__zoom;
}
function Pr(e) {
  e.stopImmediatePropagation();
}
function mn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Nh(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function Eh() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function rs() {
  return this.__zoom || Yo;
}
function kh(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function Ch() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Ih(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], i = e.invertY(t[0][1]) - n[0][1], s = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    s > i ? (i + s) / 2 : Math.min(0, i) || Math.max(0, s)
  );
}
function Ua() {
  var e = Nh, t = Eh, n = Ih, o = kh, r = Ch, i = [0, 1 / 0], s = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, u = wo, l = Bo("start", "zoom", "end"), f, d, h, p = 500, g = 150, x = 0, w = 10;
  function y(S) {
    S.property("__zoom", rs).on("wheel.zoom", D, { passive: !1 }).on("mousedown.zoom", $).on("dblclick.zoom", F).filter(r).on("touchstart.zoom", _).on("touchmove.zoom", z).on("touchend.zoom touchcancel.zoom", V).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  y.transform = function(S, j, C, A) {
    var T = S.selection ? S.selection() : S;
    T.property("__zoom", rs), S !== T ? k(S, j, C, A) : T.interrupt().each(function() {
      E(this, arguments).event(A).start().zoom(null, typeof j == "function" ? j.apply(this, arguments) : j).end();
    });
  }, y.scaleBy = function(S, j, C, A) {
    y.scaleTo(S, function() {
      var T = this.__zoom.k, P = typeof j == "function" ? j.apply(this, arguments) : j;
      return T * P;
    }, C, A);
  }, y.scaleTo = function(S, j, C, A) {
    y.transform(S, function() {
      var T = t.apply(this, arguments), P = this.__zoom, W = C == null ? v(T) : typeof C == "function" ? C.apply(this, arguments) : C, O = P.invert(W), B = typeof j == "function" ? j.apply(this, arguments) : j;
      return n(m(b(P, B), W, O), T, s);
    }, C, A);
  }, y.translateBy = function(S, j, C, A) {
    y.transform(S, function() {
      return n(this.__zoom.translate(
        typeof j == "function" ? j.apply(this, arguments) : j,
        typeof C == "function" ? C.apply(this, arguments) : C
      ), t.apply(this, arguments), s);
    }, null, A);
  }, y.translateTo = function(S, j, C, A, T) {
    y.transform(S, function() {
      var P = t.apply(this, arguments), W = this.__zoom, O = A == null ? v(P) : typeof A == "function" ? A.apply(this, arguments) : A;
      return n(Yo.translate(O[0], O[1]).scale(W.k).translate(
        typeof j == "function" ? -j.apply(this, arguments) : -j,
        typeof C == "function" ? -C.apply(this, arguments) : -C
      ), P, s);
    }, A, T);
  };
  function b(S, j) {
    return j = Math.max(i[0], Math.min(i[1], j)), j === S.k ? S : new ot(j, S.x, S.y);
  }
  function m(S, j, C) {
    var A = j[0] - C[0] * S.k, T = j[1] - C[1] * S.k;
    return A === S.x && T === S.y ? S : new ot(S.k, A, T);
  }
  function v(S) {
    return [(+S[0][0] + +S[1][0]) / 2, (+S[0][1] + +S[1][1]) / 2];
  }
  function k(S, j, C, A) {
    S.on("start.zoom", function() {
      E(this, arguments).event(A).start();
    }).on("interrupt.zoom end.zoom", function() {
      E(this, arguments).event(A).end();
    }).tween("zoom", function() {
      var T = this, P = arguments, W = E(T, P).event(A), O = t.apply(T, P), B = C == null ? v(O) : typeof C == "function" ? C.apply(T, P) : C, G = Math.max(O[1][0] - O[0][0], O[1][1] - O[0][1]), Z = T.__zoom, te = typeof j == "function" ? j.apply(T, P) : j, ae = u(Z.invert(B).concat(G / Z.k), te.invert(B).concat(G / te.k));
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
  function E(S, j, C) {
    return !C && S.__zooming || new I(S, j);
  }
  function I(S, j) {
    this.that = S, this.args = j, this.active = 0, this.sourceEvent = null, this.extent = t.apply(S, j), this.taps = 0;
  }
  I.prototype = {
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
      var j = Le(this.that).datum();
      l.call(
        S,
        this.that,
        new Sh(S, {
          sourceEvent: this.sourceEvent,
          target: y,
          transform: this.that.__zoom,
          dispatch: l
        }),
        j
      );
    }
  };
  function D(S, ...j) {
    if (!e.apply(this, arguments)) return;
    var C = E(this, j).event(S), A = this.__zoom, T = Math.max(i[0], Math.min(i[1], A.k * Math.pow(2, o.apply(this, arguments)))), P = We(S);
    if (C.wheel)
      (C.mouse[0][0] !== P[0] || C.mouse[0][1] !== P[1]) && (C.mouse[1] = A.invert(C.mouse[0] = P)), clearTimeout(C.wheel);
    else {
      if (A.k === T) return;
      C.mouse = [P, A.invert(P)], So(this), C.start();
    }
    mn(S), C.wheel = setTimeout(W, g), C.zoom("mouse", n(m(b(A, T), C.mouse[0], C.mouse[1]), C.extent, s));
    function W() {
      C.wheel = null, C.end();
    }
  }
  function $(S, ...j) {
    if (h || !e.apply(this, arguments)) return;
    var C = S.currentTarget, A = E(this, j, !0).event(S), T = Le(S.view).on("mousemove.zoom", B, !0).on("mouseup.zoom", G, !0), P = We(S, C), W = S.clientX, O = S.clientY;
    Pa(S.view), Pr(S), A.mouse = [P, this.__zoom.invert(P)], So(this), A.start();
    function B(Z) {
      if (mn(Z), !A.moved) {
        var te = Z.clientX - W, ae = Z.clientY - O;
        A.moved = te * te + ae * ae > x;
      }
      A.event(Z).zoom("mouse", n(m(A.that.__zoom, A.mouse[0] = We(Z, C), A.mouse[1]), A.extent, s));
    }
    function G(Z) {
      T.on("mousemove.zoom mouseup.zoom", null), $a(Z.view, A.moved), mn(Z), A.event(Z).end();
    }
  }
  function F(S, ...j) {
    if (e.apply(this, arguments)) {
      var C = this.__zoom, A = We(S.changedTouches ? S.changedTouches[0] : S, this), T = C.invert(A), P = C.k * (S.shiftKey ? 0.5 : 2), W = n(m(b(C, P), A, T), t.apply(this, j), s);
      mn(S), c > 0 ? Le(this).transition().duration(c).call(k, W, A, S) : Le(this).call(y.transform, W, A, S);
    }
  }
  function _(S, ...j) {
    if (e.apply(this, arguments)) {
      var C = S.touches, A = C.length, T = E(this, j, S.changedTouches.length === A).event(S), P, W, O, B;
      for (Pr(S), W = 0; W < A; ++W)
        O = C[W], B = We(O, this), B = [B, this.__zoom.invert(B), O.identifier], T.touch0 ? !T.touch1 && T.touch0[2] !== B[2] && (T.touch1 = B, T.taps = 0) : (T.touch0 = B, P = !0, T.taps = 1 + !!f);
      f && (f = clearTimeout(f)), P && (T.taps < 2 && (d = B[0], f = setTimeout(function() {
        f = null;
      }, p)), So(this), T.start());
    }
  }
  function z(S, ...j) {
    if (this.__zooming) {
      var C = E(this, j).event(S), A = S.changedTouches, T = A.length, P, W, O, B;
      for (mn(S), P = 0; P < T; ++P)
        W = A[P], O = We(W, this), C.touch0 && C.touch0[2] === W.identifier ? C.touch0[0] = O : C.touch1 && C.touch1[2] === W.identifier && (C.touch1[0] = O);
      if (W = C.that.__zoom, C.touch1) {
        var G = C.touch0[0], Z = C.touch0[1], te = C.touch1[0], ae = C.touch1[1], J = (J = te[0] - G[0]) * J + (J = te[1] - G[1]) * J, R = (R = ae[0] - Z[0]) * R + (R = ae[1] - Z[1]) * R;
        W = b(W, Math.sqrt(J / R)), O = [(G[0] + te[0]) / 2, (G[1] + te[1]) / 2], B = [(Z[0] + ae[0]) / 2, (Z[1] + ae[1]) / 2];
      } else if (C.touch0) O = C.touch0[0], B = C.touch0[1];
      else return;
      C.zoom("touch", n(m(W, O, B), C.extent, s));
    }
  }
  function V(S, ...j) {
    if (this.__zooming) {
      var C = E(this, j).event(S), A = S.changedTouches, T = A.length, P, W;
      for (Pr(S), h && clearTimeout(h), h = setTimeout(function() {
        h = null;
      }, p), P = 0; P < T; ++P)
        W = A[P], C.touch0 && C.touch0[2] === W.identifier ? delete C.touch0 : C.touch1 && C.touch1[2] === W.identifier && delete C.touch1;
      if (C.touch1 && !C.touch0 && (C.touch0 = C.touch1, delete C.touch1), C.touch0) C.touch0[1] = this.__zoom.invert(C.touch0[0]);
      else if (C.end(), C.taps === 2 && (W = We(W, this), Math.hypot(d[0] - W[0], d[1] - W[1]) < w)) {
        var O = Le(this).on("dblclick.zoom");
        O && O.apply(this, arguments);
      }
    }
  }
  return y.wheelDelta = function(S) {
    return arguments.length ? (o = typeof S == "function" ? S : uo(+S), y) : o;
  }, y.filter = function(S) {
    return arguments.length ? (e = typeof S == "function" ? S : uo(!!S), y) : e;
  }, y.touchable = function(S) {
    return arguments.length ? (r = typeof S == "function" ? S : uo(!!S), y) : r;
  }, y.extent = function(S) {
    return arguments.length ? (t = typeof S == "function" ? S : uo([[+S[0][0], +S[0][1]], [+S[1][0], +S[1][1]]]), y) : t;
  }, y.scaleExtent = function(S) {
    return arguments.length ? (i[0] = +S[0], i[1] = +S[1], y) : [i[0], i[1]];
  }, y.translateExtent = function(S) {
    return arguments.length ? (s[0][0] = +S[0][0], s[1][0] = +S[1][0], s[0][1] = +S[0][1], s[1][1] = +S[1][1], y) : [[s[0][0], s[0][1]], [s[1][0], s[1][1]]];
  }, y.constrain = function(S) {
    return arguments.length ? (n = S, y) : n;
  }, y.duration = function(S) {
    return arguments.length ? (c = +S, y) : c;
  }, y.interpolate = function(S) {
    return arguments.length ? (u = S, y) : u;
  }, y.on = function() {
    var S = l.on.apply(l, arguments);
    return S === l ? y : S;
  }, y.clickDistance = function(S) {
    return arguments.length ? (x = (S = +S) * S, y) : Math.sqrt(x);
  }, y.tapDistance = function(S) {
    return arguments.length ? (w = +S, y) : w;
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
}, Dn = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], Ga = ["Enter", " ", "Escape"], Ja = {
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
var Ft;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(Ft || (Ft = {}));
var Nt;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(Nt || (Nt = {}));
var Pn;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Pn || (Pn = {}));
const Qa = {
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
var dt;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(dt || (dt = {}));
var Po;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(Po || (Po = {}));
var ee;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(ee || (ee = {}));
const is = {
  [ee.Left]: ee.Right,
  [ee.Right]: ee.Left,
  [ee.Top]: ee.Bottom,
  [ee.Bottom]: ee.Top
};
function ec(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const tc = (e) => "id" in e && "source" in e && "target" in e, jh = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), xi = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), On = (e, t = [0, 0]) => {
  const { width: n, height: o } = at(e), r = e.origin ?? t, i = n * r[0], s = o * r[1];
  return {
    x: e.position.x - i,
    y: e.position.y - s
  };
}, _h = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const i = typeof r == "string";
    let s = !t.nodeLookup && !i ? r : void 0;
    t.nodeLookup && (s = i ? t.nodeLookup.get(r) : xi(r) ? r : t.nodeLookup.get(r.id));
    const c = s ? $o(s, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return qo(o, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return Zo(n);
}, Bn = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = qo(n, $o(r)), o = !0);
  }), o ? Zo(n) : { x: 0, y: 0, width: 0, height: 0 };
}, wi = (e, t, [n, o, r] = [0, 0, 1], i = !1, s = !1) => {
  const c = {
    ...Ut(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, u = [];
  for (const l of e.values()) {
    const { measured: f, selectable: d = !0, hidden: h = !1 } = l;
    if (s && !d || h)
      continue;
    const p = f.width ?? l.width ?? l.initialWidth ?? null, g = f.height ?? l.height ?? l.initialHeight ?? null, x = $n(c, Xt(l)), w = (p ?? 0) * (g ?? 0), y = i && x > 0;
    (!l.internals.handleBounds || y || x >= w || l.dragging) && u.push(l);
  }
  return u;
}, Ah = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function Mh(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function Dh({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: i }, s) {
  if (e.size === 0)
    return !0;
  const c = Mh(e, s), u = Bn(c), l = bi(u, t, n, s?.minZoom ?? r, s?.maxZoom ?? i, s?.padding ?? 0.1);
  return await o.setViewport(l, {
    duration: s?.duration,
    ease: s?.ease,
    interpolate: s?.interpolate
  }), !0;
}
function nc({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: i }) {
  const s = n.get(e), c = s.parentId ? n.get(s.parentId) : void 0, { x: u, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, f = s.origin ?? o;
  let d = s.extent || r;
  if (s.extent === "parent" && !s.expandParent)
    if (!c)
      i?.("005", He.error005());
    else {
      const p = c.measured.width, g = c.measured.height;
      p && g && (d = [
        [u, l],
        [u + p, l + g]
      ]);
    }
  else c && It(s.extent) && (d = [
    [s.extent[0][0] + u, s.extent[0][1] + l],
    [s.extent[1][0] + u, s.extent[1][1] + l]
  ]);
  const h = It(d) ? Ct(t, d, s.measured) : t;
  return (s.measured.width === void 0 || s.measured.height === void 0) && i?.("015", He.error015()), {
    position: {
      x: h.x - u + (s.measured.width ?? 0) * f[0],
      y: h.y - l + (s.measured.height ?? 0) * f[1]
    },
    positionAbsolute: h
  };
}
async function Ph({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const i = new Set(e.map((h) => h.id)), s = [];
  for (const h of n) {
    if (h.deletable === !1)
      continue;
    const p = i.has(h.id), g = !p && h.parentId && s.find((x) => x.id === h.parentId);
    (p || g) && s.push(h);
  }
  const c = new Set(t.map((h) => h.id)), u = o.filter((h) => h.deletable !== !1), f = Ah(s, u);
  for (const h of u)
    c.has(h.id) && !f.find((g) => g.id === h.id) && f.push(h);
  if (!r)
    return {
      edges: f,
      nodes: s
    };
  const d = await r({
    nodes: s,
    edges: f
  });
  return typeof d == "boolean" ? d ? { edges: f, nodes: s } : { edges: [], nodes: [] } : d;
}
const Wt = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), Ct = (e = { x: 0, y: 0 }, t, n) => ({
  x: Wt(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: Wt(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function oc(e, t, n) {
  const { width: o, height: r } = at(n), { x: i, y: s } = n.internals.positionAbsolute;
  return Ct(e, [
    [i, s],
    [i + o, s + r]
  ], t);
}
const ss = (e, t, n) => e < t ? Wt(Math.abs(e - t), 1, t) / t : e > n ? -Wt(Math.abs(e - n), 1, t) / t : 0, vi = (e, t, n = 15, o = 40) => {
  const r = ss(e.x, o, t.width - o) * n, i = ss(e.y, o, t.height - o) * n;
  return [r, i];
}, qo = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), ei = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), Zo = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), Xt = (e, t = [0, 0]) => {
  const { x: n, y: o } = xi(e) ? e.internals.positionAbsolute : On(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, $o = (e, t = [0, 0]) => {
  const { x: n, y: o } = xi(e) ? e.internals.positionAbsolute : On(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, rc = (e, t) => Zo(qo(ei(e), ei(t))), $n = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, as = (e) => Ye(e.width) && Ye(e.height) && Ye(e.x) && Ye(e.y), Ye = (e) => !isNaN(e) && isFinite(e), ic = (e, t) => (n, o) => {
}, Fn = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), Ut = ({ x: e, y: t }, [n, o, r], i = !1, s = [1, 1]) => {
  const c = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return i ? Fn(c, s) : c;
}, Yt = ({ x: e, y: t }, [n, o, r]) => ({
  x: e * r + n,
  y: t * r + o
});
function Tt(e, t) {
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
function $h(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = Tt(e, n), r = Tt(e, t);
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
    const o = Tt(e.top ?? e.y ?? 0, n), r = Tt(e.bottom ?? e.y ?? 0, n), i = Tt(e.left ?? e.x ?? 0, t), s = Tt(e.right ?? e.x ?? 0, t);
    return { top: o, right: s, bottom: r, left: i, x: i + s, y: o + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function Th(e, t, n, o, r, i) {
  const { x: s, y: c } = Yt(e, [t, n, o]), { x: u, y: l } = Yt({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), f = r - u, d = i - l;
  return {
    left: Math.floor(s),
    top: Math.floor(c),
    right: Math.floor(f),
    bottom: Math.floor(d)
  };
}
const bi = (e, t, n, o, r, i) => {
  const s = $h(i, t, n), c = (t - s.x) / e.width, u = (n - s.y) / e.height, l = Math.min(c, u), f = Wt(l, o, r), d = e.x + e.width / 2, h = e.y + e.height / 2, p = t / 2 - d * f, g = n / 2 - h * f, x = Th(e, p, g, f, t, n), w = {
    left: Math.min(x.left - s.left, 0),
    top: Math.min(x.top - s.top, 0),
    right: Math.min(x.right - s.right, 0),
    bottom: Math.min(x.bottom - s.bottom, 0)
  };
  return {
    x: p - w.left + w.right,
    y: g - w.top + w.bottom,
    zoom: f
  };
}, Tn = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function It(e) {
  return e != null && e !== "parent";
}
function at(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function sc(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function ac(e, t = { width: 0, height: 0 }, n, o, r) {
  const i = { ...e }, s = o.get(n);
  if (s) {
    const c = s.origin || r;
    i.x += s.internals.positionAbsolute.x - (t.width ?? 0) * c[0], i.y += s.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return i;
}
function cs(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function zh() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function Rh(e) {
  return { ...Ja, ...e || {} };
}
function Cn(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: i, y: s } = qe(e), c = Ut({ x: i - (r?.left ?? 0), y: s - (r?.top ?? 0) }, o), { x: u, y: l } = n ? Fn(c, t) : c;
  return {
    xSnapped: u,
    ySnapped: l,
    ...c
  };
}
const Si = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), cc = (e) => e?.getRootNode?.() || window?.document, Lh = ["INPUT", "SELECT", "TEXTAREA"];
function lc(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : Lh.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const uc = (e) => "clientX" in e, qe = (e, t) => {
  const n = uc(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, ls = (e, t, n, o, r) => {
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
      ...Si(s)
    };
  });
};
function dc({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: i, targetControlX: s, targetControlY: c }) {
  const u = e * 0.125 + r * 0.375 + s * 0.375 + n * 0.125, l = t * 0.125 + i * 0.375 + c * 0.375 + o * 0.125, f = Math.abs(u - e), d = Math.abs(l - t);
  return [u, l, f, d];
}
function fo(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function us({ pos: e, x1: t, y1: n, x2: o, y2: r, c: i }) {
  switch (e) {
    case ee.Left:
      return [t - fo(t - o, i), n];
    case ee.Right:
      return [t + fo(o - t, i), n];
    case ee.Top:
      return [t, n - fo(n - r, i)];
    case ee.Bottom:
      return [t, n + fo(r - n, i)];
  }
}
function fc({ sourceX: e, sourceY: t, sourcePosition: n = ee.Bottom, targetX: o, targetY: r, targetPosition: i = ee.Top, curvature: s = 0.25 }) {
  const [c, u] = us({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: s
  }), [l, f] = us({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: s
  }), [d, h, p, g] = dc({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: r,
    sourceControlX: c,
    sourceControlY: u,
    targetControlX: l,
    targetControlY: f
  });
  return [
    `M${e},${t} C${c},${u} ${l},${f} ${o},${r}`,
    d,
    h,
    p,
    g
  ];
}
function hc({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, i = n < e ? n + r : n - r, s = Math.abs(o - t) / 2, c = o < t ? o + s : o - s;
  return [i, c, r, s];
}
function Vh({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: i = "basic" }) {
  if (i === "manual")
    return o;
  const s = r && n ? o + 1e3 : o, c = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return s + c;
}
function Hh({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const i = qo($o(e), $o(t));
  i.x === i.x2 && (i.x2 += 1), i.y === i.y2 && (i.y2 += 1);
  const s = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return $n(s, Zo(i)) > 0;
}
const pc = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, Oh = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), Bh = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", He.error006()), t;
  const o = n.getEdgeId || pc;
  let r;
  return tc(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, Oh(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, Fh = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: r, ...i } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", He.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return o.onError?.("007", He.error007(r)), n;
  const c = o.getEdgeId || pc, u = {
    ...i,
    id: o.shouldReplaceId ? c(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== r).concat(u);
};
function gc({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, i, s, c] = hc({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, i, s, c];
}
const ds = {
  [ee.Left]: { x: -1, y: 0 },
  [ee.Right]: { x: 1, y: 0 },
  [ee.Top]: { x: 0, y: -1 },
  [ee.Bottom]: { x: 0, y: 1 }
}, Wh = ({ source: e, sourcePosition: t = ee.Bottom, target: n }) => t === ee.Left || t === ee.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, fs = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Xh({ source: e, sourcePosition: t = ee.Bottom, target: n, targetPosition: o = ee.Top, center: r, offset: i, stepPosition: s }) {
  const c = ds[t], u = ds[o], l = { x: e.x + c.x * i, y: e.y + c.y * i }, f = { x: n.x + u.x * i, y: n.y + u.y * i }, d = Wh({
    source: l,
    sourcePosition: t,
    target: f
  }), h = d.x !== 0 ? "x" : "y", p = d[h];
  let g = [], x, w;
  const y = { x: 0, y: 0 }, b = { x: 0, y: 0 }, [, , m, v] = hc({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[h] * u[h] === -1) {
    h === "x" ? (x = r.x ?? l.x + (f.x - l.x) * s, w = r.y ?? (l.y + f.y) / 2) : (x = r.x ?? (l.x + f.x) / 2, w = r.y ?? l.y + (f.y - l.y) * s);
    const D = [
      { x, y: l.y },
      { x, y: f.y }
    ], $ = [
      { x: l.x, y: w },
      { x: f.x, y: w }
    ];
    c[h] === p ? g = h === "x" ? D : $ : g = h === "x" ? $ : D;
  } else {
    const D = [{ x: l.x, y: f.y }], $ = [{ x: f.x, y: l.y }];
    if (h === "x" ? g = c.x === p ? $ : D : g = c.y === p ? D : $, t === o) {
      const S = Math.abs(e[h] - n[h]);
      if (S <= i) {
        const j = Math.min(i - 1, i - S);
        c[h] === p ? y[h] = (l[h] > e[h] ? -1 : 1) * j : b[h] = (f[h] > n[h] ? -1 : 1) * j;
      }
    }
    if (t !== o) {
      const S = h === "x" ? "y" : "x", j = c[h] === u[S], C = l[S] > f[S], A = l[S] < f[S];
      (c[h] === 1 && (!j && C || j && A) || c[h] !== 1 && (!j && A || j && C)) && (g = h === "x" ? D : $);
    }
    const F = { x: l.x + y.x, y: l.y + y.y }, _ = { x: f.x + b.x, y: f.y + b.y }, z = Math.max(Math.abs(F.x - g[0].x), Math.abs(_.x - g[0].x)), V = Math.max(Math.abs(F.y - g[0].y), Math.abs(_.y - g[0].y));
    z >= V ? (x = (F.x + _.x) / 2, w = g[0].y) : (x = g[0].x, w = (F.y + _.y) / 2);
  }
  const k = { x: l.x + y.x, y: l.y + y.y }, E = { x: f.x + b.x, y: f.y + b.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...k.x !== g[0].x || k.y !== g[0].y ? [k] : [],
    ...g,
    ...E.x !== g[g.length - 1].x || E.y !== g[g.length - 1].y ? [E] : [],
    n
  ], x, w, m, v];
}
function Yh(e, t, n, o) {
  const r = Math.min(fs(e, t) / 2, fs(t, n) / 2, o), { x: i, y: s } = t;
  if (e.x === i && i === n.x || e.y === s && s === n.y)
    return `L${i} ${s}`;
  if (e.y === s) {
    const l = e.x < n.x ? -1 : 1, f = e.y < n.y ? 1 : -1;
    return `L ${i + r * l},${s}Q ${i},${s} ${i},${s + r * f}`;
  }
  const c = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${i},${s + r * u}Q ${i},${s} ${i + r * c},${s}`;
}
function To({ sourceX: e, sourceY: t, sourcePosition: n = ee.Bottom, targetX: o, targetY: r, targetPosition: i = ee.Top, borderRadius: s = 5, centerX: c, centerY: u, offset: l = 20, stepPosition: f = 0.5 }) {
  const [d, h, p, g, x] = Xh({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: i,
    center: { x: c, y: u },
    offset: l,
    stepPosition: f
  });
  let w = `M${d[0].x} ${d[0].y}`;
  for (let y = 1; y < d.length - 1; y++)
    w += Yh(d[y - 1], d[y], d[y + 1], s);
  return w += `L${d[d.length - 1].x} ${d[d.length - 1].y}`, [w, h, p, g, x];
}
function hs(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function qh(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!hs(t) || !hs(n))
    return null;
  const o = t.internals.handleBounds || ps(t.handles), r = n.internals.handleBounds || ps(n.handles), i = gs(o?.source ?? [], e.sourceHandle), s = gs(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === Ft.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!i || !s)
    return e.onError?.("008", He.error008(i ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = i?.position || ee.Bottom, u = s?.position || ee.Top, l = jt(t, i, c), f = jt(n, s, u);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: f.x,
    targetY: f.y,
    sourcePosition: c,
    targetPosition: u
  };
}
function ps(e) {
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
function jt(e, t, n = ee.Left, o = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, i = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: s, height: c } = t ?? at(e);
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
function gs(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function ti(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function Zh(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const i = /* @__PURE__ */ new Set();
  return e.reduce((s, c) => ([c.markerStart || o, c.markerEnd || r].forEach((u) => {
    if (u && typeof u == "object") {
      const l = ti(u, t);
      i.has(l) || (s.push({ id: l, color: u.color || n, ...u }), i.add(l));
    }
  }), s), []).sort((s, c) => s.id.localeCompare(c.id));
}
const mc = 1e3, Kh = 10, Ni = {
  nodeOrigin: [0, 0],
  nodeExtent: Dn,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Uh = {
  ...Ni,
  checkEquality: !0
};
function Ei(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function Gh(e, t, n) {
  const o = Ei(Ni, n);
  for (const r of e.values())
    if (r.parentId)
      Ci(r, e, t, o);
    else {
      const i = On(r, o.nodeOrigin), s = It(r.extent) ? r.extent : o.nodeExtent, c = Ct(i, s, at(r));
      r.internals.positionAbsolute = c;
    }
}
function Jh(e, t) {
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
function ki(e) {
  return e === "manual";
}
function ni(e, t, n, o = {}) {
  const r = Ei(Uh, o), i = { i: 0 }, s = new Map(t), c = r?.elevateNodesOnSelect && !ki(r.zIndexMode) ? mc : 0;
  let u = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const f of e) {
    let d = s.get(f.id);
    if (r.checkEquality && f === d?.internals.userNode)
      t.set(f.id, d);
    else {
      const h = On(f, r.nodeOrigin), p = It(f.extent) ? f.extent : r.nodeExtent, g = Ct(h, p, at(f));
      d = {
        ...r.defaults,
        ...f,
        measured: {
          width: f.measured?.width,
          height: f.measured?.height
        },
        internals: {
          positionAbsolute: g,
          // if user re-initializes the node or removes `measured` for whatever reason, we reset the handleBounds so that the node gets re-measured
          handleBounds: Jh(f, d),
          z: yc(f, c, r.zIndexMode),
          userNode: f
        }
      }, t.set(f.id, d);
    }
    (d.measured === void 0 || d.measured.width === void 0 || d.measured.height === void 0) && !d.hidden && (u = !1), f.parentId && Ci(d, t, n, o, i), l ||= f.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: l };
}
function Qh(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function Ci(e, t, n, o, r) {
  const { elevateNodesOnSelect: i, nodeOrigin: s, nodeExtent: c, zIndexMode: u } = Ei(Ni, o), l = e.parentId, f = t.get(l);
  if (!f) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Qh(e, n), r && !f.parentId && f.internals.rootParentIndex === void 0 && u === "auto" && (f.internals.rootParentIndex = ++r.i, f.internals.z = f.internals.z + r.i * Kh), r && f.internals.rootParentIndex !== void 0 && (r.i = f.internals.rootParentIndex);
  const d = i && !ki(u) ? mc : 0, { x: h, y: p, z: g } = ep(e, f, s, c, d, u), { positionAbsolute: x } = e.internals, w = h !== x.x || p !== x.y;
  (w || g !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: w ? { x: h, y: p } : x,
      z: g
    }
  });
}
function yc(e, t, n) {
  const o = Ye(e.zIndex) ? e.zIndex : 0;
  return ki(n) ? o : o + (e.selected ? t : 0);
}
function ep(e, t, n, o, r, i) {
  const { x: s, y: c } = t.internals.positionAbsolute, u = at(e), l = On(e, n), f = It(e.extent) ? Ct(l, e.extent, u) : l;
  let d = Ct({ x: s + f.x, y: c + f.y }, o, u);
  e.extent === "parent" && (d = oc(d, u, t));
  const h = yc(e, r, i), p = t.internals.z ?? 0;
  return {
    x: d.x,
    y: d.y,
    z: p >= h ? p + 1 : h
  };
}
function Ii(e, t, n, o = [0, 0]) {
  const r = [], i = /* @__PURE__ */ new Map();
  for (const s of e) {
    const c = t.get(s.parentId);
    if (!c)
      continue;
    const u = i.get(s.parentId)?.expandedRect ?? Xt(c), l = rc(u, s.rect);
    i.set(s.parentId, { expandedRect: l, parent: c });
  }
  return i.size > 0 && i.forEach(({ expandedRect: s, parent: c }, u) => {
    const l = c.internals.positionAbsolute, f = at(c), d = c.origin ?? o, h = s.x < l.x ? Math.round(Math.abs(l.x - s.x)) : 0, p = s.y < l.y ? Math.round(Math.abs(l.y - s.y)) : 0, g = Math.max(f.width, Math.round(s.width)), x = Math.max(f.height, Math.round(s.height)), w = (g - f.width) * d[0], y = (x - f.height) * d[1];
    (h > 0 || p > 0 || w || y) && (r.push({
      id: u,
      type: "position",
      position: {
        x: c.position.x - h + w,
        y: c.position.y - p + y
      }
    }), n.get(u)?.forEach((b) => {
      e.some((m) => m.id === b.id) || r.push({
        id: b.id,
        type: "position",
        position: {
          x: b.position.x + h,
          y: b.position.y + p
        }
      });
    })), (f.width < s.width || f.height < s.height || h || p) && r.push({
      id: u,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: g + (h ? d[0] * h - w : 0),
        height: x + (p ? d[1] * p - y : 0)
      }
    });
  }), r;
}
function tp(e, t, n, o, r, i, s) {
  const c = o?.querySelector(".xyflow__viewport");
  let u = !1;
  if (!c)
    return { changes: [], updatedInternals: u };
  const l = [], f = window.getComputedStyle(c), { m22: d } = new window.DOMMatrixReadOnly(f.transform), h = [];
  for (const p of e.values()) {
    const g = t.get(p.id);
    if (!g)
      continue;
    if (g.hidden) {
      t.set(g.id, {
        ...g,
        internals: {
          ...g.internals,
          handleBounds: void 0
        }
      }), u = !0;
      continue;
    }
    const x = Si(p.nodeElement), w = g.measured.width !== x.width || g.measured.height !== x.height;
    if (!!(x.width && x.height && (w || !g.internals.handleBounds || p.force))) {
      const b = p.nodeElement.getBoundingClientRect(), m = It(g.extent) ? g.extent : i;
      let { positionAbsolute: v } = g.internals;
      g.parentId && g.extent === "parent" ? v = oc(v, x, t.get(g.parentId)) : m && (v = Ct(v, m, x));
      const k = {
        ...g,
        measured: x,
        internals: {
          ...g.internals,
          positionAbsolute: v,
          handleBounds: {
            source: ls("source", p.nodeElement, b, d, g.id),
            target: ls("target", p.nodeElement, b, d, g.id)
          }
        }
      };
      t.set(g.id, k), g.parentId && Ci(k, t, n, { nodeOrigin: r, zIndexMode: s }), u = !0, w && (l.push({
        id: g.id,
        type: "dimensions",
        dimensions: x
      }), g.expandParent && g.parentId && h.push({
        id: g.id,
        parentId: g.parentId,
        rect: Xt(k, r)
      }));
    }
  }
  if (h.length > 0) {
    const p = Ii(h, t, n, r);
    l.push(...p);
  }
  return { changes: l, updatedInternals: u };
}
async function np({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: i }) {
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
function ms(e, t, n, o, r, i) {
  let s = r;
  const c = o.get(s) || /* @__PURE__ */ new Map();
  o.set(s, c.set(n, t)), s = `${r}-${e}`;
  const u = o.get(s) || /* @__PURE__ */ new Map();
  if (o.set(s, u.set(n, t)), i) {
    s = `${r}-${e}-${i}`;
    const l = o.get(s) || /* @__PURE__ */ new Map();
    o.set(s, l.set(n, t));
  }
}
function xc(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: i, sourceHandle: s = null, targetHandle: c = null } = o, u = { edgeId: o.id, source: r, target: i, sourceHandle: s, targetHandle: c }, l = `${r}-${s}--${i}-${c}`, f = `${i}-${c}--${r}-${s}`;
    ms("source", u, f, e, r, s), ms("target", u, l, e, i, c), t.set(o.id, o);
  }
}
function wc(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : wc(n, t) : !1;
}
function ys(e, t, n) {
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
function op(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [i, s] of e)
    if ((s.selected || s.id === o) && (!s.parentId || !wc(s, e)) && (s.draggable || t && typeof s.draggable > "u")) {
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
function $r({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
  const r = [];
  for (const [s, c] of t) {
    const u = n.get(s)?.internals.userNode;
    u && r.push({
      ...u,
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
function rp({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const i = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, s = Fn(i, t);
  return {
    x: s.x - i.x,
    y: s.y - i.y
  };
}
function ip({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let i = { x: null, y: null }, s = 0, c = /* @__PURE__ */ new Map(), u = !1, l = { x: 0, y: 0 }, f = null, d = !1, h = null, p = !1, g = !1, x = null;
  function w({ noDragClassName: b, handleSelector: m, domNode: v, isSelectable: k, nodeId: E, nodeClickDistance: I = 0 }) {
    h = Le(v);
    function D({ x: z, y: V }) {
      const { nodeLookup: S, nodeExtent: j, snapGrid: C, snapToGrid: A, nodeOrigin: T, onNodeDrag: P, onSelectionDrag: W, onError: O, updateNodePositions: B } = t();
      i = { x: z, y: V };
      let G = !1;
      const Z = c.size > 1, te = Z && j ? ei(Bn(c)) : null, ae = Z && A ? rp({
        dragItems: c,
        snapGrid: C,
        x: z,
        y: V
      }) : null;
      for (const [J, R] of c) {
        if (!S.has(J))
          continue;
        let q = { x: z - R.distance.x, y: V - R.distance.y };
        A && (q = ae ? {
          x: Math.round(q.x + ae.x),
          y: Math.round(q.y + ae.y)
        } : Fn(q, C));
        let re = null;
        if (Z && j && !R.extent && te) {
          const { positionAbsolute: Q } = R.internals, se = Q.x - te.x + j[0][0], L = Q.x + R.measured.width - te.x2 + j[1][0], K = Q.y - te.y + j[0][1], fe = Q.y + R.measured.height - te.y2 + j[1][1];
          re = [
            [se, K],
            [L, fe]
          ];
        }
        const { position: oe, positionAbsolute: U } = nc({
          nodeId: J,
          nextPosition: q,
          nodeLookup: S,
          nodeExtent: re || j,
          nodeOrigin: T,
          onError: O
        });
        G = G || R.position.x !== oe.x || R.position.y !== oe.y, R.position = oe, R.internals.positionAbsolute = U;
      }
      if (g = g || G, !!G && (B(c, !0), x && (o || P || !E && W))) {
        const [J, R] = $r({
          nodeId: E,
          dragItems: c,
          nodeLookup: S
        });
        o?.(x, c, J, R), P?.(x, J, R), E || W?.(x, R);
      }
    }
    async function $() {
      if (!f)
        return;
      const { transform: z, panBy: V, autoPanSpeed: S, autoPanOnNodeDrag: j } = t();
      if (!j) {
        u = !1, cancelAnimationFrame(s);
        return;
      }
      const [C, A] = vi(l, f, S);
      (C !== 0 || A !== 0) && (i.x = (i.x ?? 0) - C / z[2], i.y = (i.y ?? 0) - A / z[2], await V({ x: C, y: A }) && D(i)), s = requestAnimationFrame($);
    }
    function F(z) {
      const { nodeLookup: V, multiSelectionActive: S, nodesDraggable: j, transform: C, snapGrid: A, snapToGrid: T, selectNodesOnDrag: P, onNodeDragStart: W, onSelectionDragStart: O, unselectNodesAndEdges: B } = t();
      d = !0, (!P || !k) && !S && E && (V.get(E)?.selected || B()), k && P && E && e?.(E);
      const G = Cn(z.sourceEvent, { transform: C, snapGrid: A, snapToGrid: T, containerBounds: f });
      if (i = G, c = op(V, j, G, E), c.size > 0 && (n || W || !E && O)) {
        const [Z, te] = $r({
          nodeId: E,
          dragItems: c,
          nodeLookup: V
        });
        n?.(z.sourceEvent, c, Z, te), W?.(z.sourceEvent, Z, te), E || O?.(z.sourceEvent, te);
      }
    }
    const _ = Ta().clickDistance(I).on("start", (z) => {
      const { domNode: V, nodeDragThreshold: S, transform: j, snapGrid: C, snapToGrid: A } = t();
      f = V?.getBoundingClientRect() || null, p = !1, g = !1, x = z.sourceEvent, S === 0 && F(z), i = Cn(z.sourceEvent, { transform: j, snapGrid: C, snapToGrid: A, containerBounds: f }), l = qe(z.sourceEvent, f);
    }).on("drag", (z) => {
      const { autoPanOnNodeDrag: V, transform: S, snapGrid: j, snapToGrid: C, nodeDragThreshold: A, nodeLookup: T } = t(), P = Cn(z.sourceEvent, { transform: S, snapGrid: j, snapToGrid: C, containerBounds: f });
      if (x = z.sourceEvent, (z.sourceEvent.type === "touchmove" && z.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      E && !T.has(E)) && (p = !0), !p) {
        if (!u && V && d && (u = !0, $()), !d) {
          const W = qe(z.sourceEvent, f), O = W.x - l.x, B = W.y - l.y;
          Math.sqrt(O * O + B * B) > A && F(z);
        }
        (i.x !== P.xSnapped || i.y !== P.ySnapped) && c && d && (l = qe(z.sourceEvent, f), D(P));
      }
    }).on("end", (z) => {
      if (!d || p) {
        p && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (u = !1, d = !1, cancelAnimationFrame(s), c.size > 0) {
        const { nodeLookup: V, updateNodePositions: S, onNodeDragStop: j, onSelectionDragStop: C } = t();
        if (g && (S(c, !1), g = !1), r || j || !E && C) {
          const [A, T] = $r({
            nodeId: E,
            dragItems: c,
            nodeLookup: V,
            dragging: !1
          });
          r?.(z.sourceEvent, c, A, T), j?.(z.sourceEvent, A, T), E || C?.(z.sourceEvent, T);
        }
      }
    }).filter((z) => {
      const V = z.target;
      return !z.button && (!b || !ys(V, `.${b}`, v)) && (!m || ys(V, m, v));
    });
    h.call(_);
  }
  function y() {
    h?.on(".drag", null);
  }
  return {
    update: w,
    destroy: y
  };
}
function sp(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const i of t.values())
    $n(r, Xt(i)) > 0 && o.push(i);
  return o;
}
const ap = 250;
function cp(e, t, n, o) {
  let r = [], i = 1 / 0;
  const s = sp(e, n, t + ap);
  for (const c of s) {
    const u = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of u) {
      if (o.nodeId === l.nodeId && o.type === l.type && o.id === l.id)
        continue;
      const { x: f, y: d } = jt(c, l, l.position, !0), h = Math.sqrt(Math.pow(f - e.x, 2) + Math.pow(d - e.y, 2));
      h > t || (h < i ? (r = [{ ...l, x: f, y: d }], i = h) : h === i && r.push({ ...l, x: f, y: d }));
    }
  }
  if (!r.length)
    return null;
  if (r.length > 1) {
    const c = o.type === "source" ? "target" : "source";
    return r.find((u) => u.type === c) ?? r[0];
  }
  return r[0];
}
function vc(e, t, n, o, r, i = !1) {
  const s = o.get(e);
  if (!s)
    return null;
  const c = r === "strict" ? s.internals.handleBounds?.[t] : [...s.internals.handleBounds?.source ?? [], ...s.internals.handleBounds?.target ?? []], u = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return u && i ? { ...u, ...jt(s, u, u.position, !0) } : u;
}
function bc(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function lp(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const Sc = () => !0;
function up(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: i, isTarget: s, domNode: c, nodeLookup: u, lib: l, autoPanOnConnect: f, flowId: d, panBy: h, cancelConnection: p, onConnectStart: g, onConnect: x, onConnectEnd: w, isValidConnection: y = Sc, onReconnectEnd: b, updateConnection: m, getTransform: v, getFromHandle: k, autoPanSpeed: E, dragThreshold: I = 1, handleDomNode: D }) {
  const $ = cc(e.target);
  let F = 0, _;
  const { x: z, y: V } = qe(e), S = bc(i, D), j = c?.getBoundingClientRect();
  let C = !1;
  if (!j || !S)
    return;
  const A = vc(r, S, o, u, t);
  if (!A)
    return;
  let T = qe(e, j), P = !1, W = null, O = !1, B = null;
  function G() {
    if (!f || !j)
      return;
    const [oe, U] = vi(T, j, E);
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
    from: jt(te, Z, ee.Left, !0),
    fromHandle: Z,
    fromPosition: Z.position,
    fromNode: te,
    to: T,
    toHandle: null,
    toPosition: is[Z.position],
    toNode: null,
    pointer: T
  };
  function R() {
    C = !0, m(J), g?.(e, { nodeId: r, handleId: o, handleType: S });
  }
  I === 0 && R();
  function q(oe) {
    if (!C) {
      const { x: fe, y: ue } = qe(oe), je = fe - z, Oe = ue - V;
      if (!(je * je + Oe * Oe > I * I))
        return;
      R();
    }
    if (!k() || !Z) {
      re(oe);
      return;
    }
    const U = v();
    T = qe(oe, j), _ = cp(Ut(T, U, !1, [1, 1]), n, u, Z), P || (G(), P = !0);
    const Q = Nc(oe, {
      handle: _,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: s ? "target" : "source",
      isValidConnection: y,
      doc: $,
      lib: l,
      flowId: d,
      nodeLookup: u
    });
    B = Q.handleDomNode, W = Q.connection, O = lp(!!_, Q.isValid);
    const se = u.get(r), L = se ? jt(se, Z, ee.Left, !0) : J.from, K = {
      ...J,
      from: L,
      isValid: O,
      to: Q.toHandle && O ? Yt({ x: Q.toHandle.x, y: Q.toHandle.y }, U) : T,
      toHandle: Q.toHandle,
      toPosition: O && Q.toHandle ? Q.toHandle.position : is[Z.position],
      toNode: Q.toHandle ? u.get(Q.toHandle.nodeId) : null,
      pointer: T
    };
    m(K), J = K;
  }
  function re(oe) {
    if (!("touches" in oe && oe.touches.length > 0)) {
      if (C) {
        (_ || B) && W && O && x?.(W);
        const { inProgress: U, ...Q } = J, se = {
          ...Q,
          toPosition: J.toHandle ? J.toPosition : null
        };
        w?.(oe, se), i && b?.(oe, se);
      }
      p(), cancelAnimationFrame(F), P = !1, O = !1, W = null, B = null, $.removeEventListener("mousemove", q), $.removeEventListener("mouseup", re), $.removeEventListener("touchmove", q), $.removeEventListener("touchend", re);
    }
  }
  $.addEventListener("mousemove", q), $.addEventListener("mouseup", re), $.addEventListener("touchmove", q), $.addEventListener("touchend", re);
}
function Nc(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: i, doc: s, lib: c, flowId: u, isValidConnection: l = Sc, nodeLookup: f }) {
  const d = i === "target", h = t ? s.querySelector(`.${c}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: p, y: g } = qe(e), x = s.elementFromPoint(p, g), w = x?.classList.contains(`${c}-flow__handle`) ? x : h, y = {
    handleDomNode: w,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (w) {
    const b = bc(void 0, w), m = w.getAttribute("data-nodeid"), v = w.getAttribute("data-handleid"), k = w.classList.contains("connectable"), E = w.classList.contains("connectableend");
    if (!m || !b)
      return y;
    const I = {
      source: d ? m : o,
      sourceHandle: d ? v : r,
      target: d ? o : m,
      targetHandle: d ? r : v
    };
    y.connection = I;
    const $ = k && E && (n === Ft.Strict ? d && b === "source" || !d && b === "target" : m !== o || v !== r);
    y.isValid = $ && l(I), y.toHandle = vc(m, b, v, f, n, !0);
  }
  return y;
}
const oi = {
  onPointerDown: up,
  isValid: Nc
};
function dp({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = Le(e);
  function i({ translateExtent: c, width: u, height: l, zoomStep: f = 1, pannable: d = !0, zoomable: h = !0, inversePan: p = !1 }) {
    const g = (m) => {
      if (m.sourceEvent.type !== "wheel" || !t)
        return;
      const v = n(), k = m.sourceEvent.ctrlKey && Tn() ? 10 : 1, E = -m.sourceEvent.deltaY * (m.sourceEvent.deltaMode === 1 ? 0.05 : m.sourceEvent.deltaMode ? 1 : 2e-3) * f, I = v[2] * Math.pow(2, E * k);
      t.scaleTo(I);
    };
    let x = [0, 0];
    const w = (m) => {
      (m.sourceEvent.type === "mousedown" || m.sourceEvent.type === "touchstart") && (x = [
        m.sourceEvent.clientX ?? m.sourceEvent.touches[0].clientX,
        m.sourceEvent.clientY ?? m.sourceEvent.touches[0].clientY
      ]);
    }, y = (m) => {
      const v = n();
      if (m.sourceEvent.type !== "mousemove" && m.sourceEvent.type !== "touchmove" || !t)
        return;
      const k = [
        m.sourceEvent.clientX ?? m.sourceEvent.touches[0].clientX,
        m.sourceEvent.clientY ?? m.sourceEvent.touches[0].clientY
      ], E = [k[0] - x[0], k[1] - x[1]];
      x = k;
      const I = o() * Math.max(v[2], Math.log(v[2])) * (p ? -1 : 1), D = {
        x: v[0] - E[0] * I,
        y: v[1] - E[1] * I
      }, $ = [
        [0, 0],
        [u, l]
      ];
      t.setViewportConstrained({
        x: D.x,
        y: D.y,
        zoom: v[2]
      }, $, c);
    }, b = Ua().on("start", w).on("zoom", d ? y : null).on("zoom.wheel", h ? g : null);
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
const Ko = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), Tr = ({ x: e, y: t, zoom: n }) => Yo.translate(e, t).scale(n), Rt = (e, t) => e.target.closest(`.${t}`), Ec = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), fp = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, zr = (e, t = 0, n = fp, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, kc = (e) => {
  const t = e.ctrlKey && Tn() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function hp({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: i, zoomOnPinch: s, onPanZoomStart: c, onPanZoom: u, onPanZoomEnd: l }) {
  return (f) => {
    if (Rt(f, t))
      return f.ctrlKey && f.preventDefault(), !1;
    f.preventDefault(), f.stopImmediatePropagation();
    const d = n.property("__zoom").k || 1;
    if (f.ctrlKey && s) {
      const w = We(f), y = kc(f), b = d * Math.pow(2, y);
      o.scaleTo(n, b, w, f);
      return;
    }
    const h = f.deltaMode === 1 ? 20 : 1;
    let p = r === Nt.Vertical ? 0 : f.deltaX * h, g = r === Nt.Horizontal ? 0 : f.deltaY * h;
    !Tn() && f.shiftKey && r !== Nt.Vertical && (p = f.deltaY * h, g = 0), o.translateBy(
      n,
      -(p / d) * i,
      -(g / d) * i,
      // @ts-ignore
      { internal: !0 }
    );
    const x = Ko(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(f, x), e.panScrollTimeout = setTimeout(() => {
      l?.(f, x), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(f, x));
  };
}
function pp({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const i = o.type === "wheel", s = !t && i && !o.ctrlKey, c = Rt(o, e);
    if (o.ctrlKey && i && c && o.preventDefault(), s || c)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function gp({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = Ko(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function mp({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (i) => {
    e.usedRightMouseButton = !!(n && Ec(t, e.mouseButton ?? 0)), i.sourceEvent?.sync || o([i.transform.x, i.transform.y, i.transform.k]), r && !i.sourceEvent?.internal && r?.(i.sourceEvent, Ko(i.transform));
  };
}
function yp({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: i }) {
  return (s) => {
    if (!s.sourceEvent?.internal && (e.isZoomingOrPanning = !1, i && Ec(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && s.sourceEvent && i(s.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
      const c = Ko(s.transform);
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
function xp({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: i, userSelectionActive: s, noWheelClassName: c, noPanClassName: u, lib: l, connectionInProgress: f }) {
  return (d) => {
    const h = e || t, p = n && d.ctrlKey, g = d.type === "wheel";
    if (d.button === 1 && d.type === "mousedown" && (Rt(d, `${l}-flow__node`) || Rt(d, `${l}-flow__edge`)))
      return !0;
    if (!o && !h && !r && !i && !n || s || f && !g || Rt(d, c) && g || Rt(d, u) && (!g || r && g && !e) || !n && d.ctrlKey && g)
      return !1;
    if (!n && d.type === "touchstart" && d.touches?.length > 1)
      return d.preventDefault(), !1;
    if (!h && !r && !p && g || !o && (d.type === "mousedown" || d.type === "touchstart") || Array.isArray(o) && !o.includes(d.button) && d.type === "mousedown")
      return !1;
    const x = Array.isArray(o) && o.includes(d.button) || !d.button || d.button <= 1;
    return (!d.ctrlKey || g) && x;
  };
}
function wp({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: i, onPanZoomStart: s, onPanZoomEnd: c, onDraggingChange: u }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, f = e.getBoundingClientRect(), d = Ua().scaleExtent([t, n]).translateExtent(o), h = Le(e).call(d);
  b({
    x: r.x,
    y: r.y,
    zoom: Wt(r.zoom, t, n)
  }, [
    [0, 0],
    [f.width, f.height]
  ], o);
  const p = h.on("wheel.zoom"), g = h.on("dblclick.zoom");
  d.wheelDelta(kc);
  async function x(_, z) {
    return h ? new Promise((V) => {
      d?.interpolate(z?.interpolate === "linear" ? kn : wo).transform(zr(h, z?.duration, z?.ease, () => V(!0)), _);
    }) : !1;
  }
  function w({ noWheelClassName: _, noPanClassName: z, onPaneContextMenu: V, userSelectionActive: S, panOnScroll: j, panOnDrag: C, panOnScrollMode: A, panOnScrollSpeed: T, preventScrolling: P, zoomOnPinch: W, zoomOnScroll: O, zoomOnDoubleClick: B, zoomActivationKeyPressed: G, lib: Z, onTransformChange: te, connectionInProgress: ae, paneClickDistance: J, selectionOnDrag: R }) {
    S && !l.isZoomingOrPanning && y();
    const q = j && !G && !S;
    d.clickDistance(R ? 1 / 0 : !Ye(J) || J < 0 ? 0 : J);
    const re = q ? hp({
      zoomPanValues: l,
      noWheelClassName: _,
      d3Selection: h,
      d3Zoom: d,
      panOnScrollMode: A,
      panOnScrollSpeed: T,
      zoomOnPinch: W,
      onPanZoomStart: s,
      onPanZoom: i,
      onPanZoomEnd: c
    }) : pp({
      noWheelClassName: _,
      preventScrolling: P,
      d3ZoomHandler: p
    });
    h.on("wheel.zoom", re, { passive: !1 });
    const oe = gp({
      zoomPanValues: l,
      onDraggingChange: u,
      onPanZoomStart: s
    });
    d.on("start", oe);
    const U = mp({
      zoomPanValues: l,
      panOnDrag: C,
      onPaneContextMenu: !!V,
      onPanZoom: i,
      onTransformChange: te
    });
    d.on("zoom", U);
    const Q = yp({
      zoomPanValues: l,
      panOnDrag: C,
      panOnScroll: j,
      onPaneContextMenu: V,
      onPanZoomEnd: c,
      onDraggingChange: u
    });
    d.on("end", Q);
    const se = xp({
      zoomActivationKeyPressed: G,
      panOnDrag: C,
      zoomOnScroll: O,
      panOnScroll: j,
      zoomOnDoubleClick: B,
      zoomOnPinch: W,
      userSelectionActive: S,
      noPanClassName: z,
      noWheelClassName: _,
      lib: Z,
      connectionInProgress: ae
    });
    d.filter(se), B ? h.on("dblclick.zoom", g) : h.on("dblclick.zoom", null);
  }
  function y() {
    d.on("zoom", null);
  }
  async function b(_, z, V) {
    const S = Tr(_), j = d?.constrain()(S, z, V);
    return j && await x(j), j;
  }
  async function m(_, z) {
    const V = Tr(_);
    return await x(V, z), V;
  }
  function v(_) {
    if (h) {
      const z = Tr(_), V = h.property("__zoom");
      (V.k !== _.zoom || V.x !== _.x || V.y !== _.y) && d?.transform(h, z, null, { sync: !0 });
    }
  }
  function k() {
    const _ = h ? Ka(h.node()) : { x: 0, y: 0, k: 1 };
    return { x: _.x, y: _.y, zoom: _.k };
  }
  async function E(_, z) {
    return h ? new Promise((V) => {
      d?.interpolate(z?.interpolate === "linear" ? kn : wo).scaleTo(zr(h, z?.duration, z?.ease, () => V(!0)), _);
    }) : !1;
  }
  async function I(_, z) {
    return h ? new Promise((V) => {
      d?.interpolate(z?.interpolate === "linear" ? kn : wo).scaleBy(zr(h, z?.duration, z?.ease, () => V(!0)), _);
    }) : !1;
  }
  function D(_) {
    d?.scaleExtent(_);
  }
  function $(_) {
    d?.translateExtent(_);
  }
  function F(_) {
    const z = !Ye(_) || _ < 0 ? 0 : _;
    d?.clickDistance(z);
  }
  return {
    update: w,
    destroy: y,
    setViewport: m,
    setViewportConstrained: b,
    getViewport: k,
    scaleTo: E,
    scaleBy: I,
    setScaleExtent: D,
    setTranslateExtent: $,
    syncViewport: v,
    setClickDistance: F
  };
}
var qt;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(qt || (qt = {}));
function vp({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: i }) {
  const s = e - t, c = n - o, u = [s > 0 ? 1 : s < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return s && r && (u[0] = u[0] * -1), c && i && (u[1] = u[1] * -1), u;
}
function xs(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: r
  };
}
function lt(e, t) {
  return Math.max(0, t - e);
}
function ut(e, t) {
  return Math.max(0, e - t);
}
function ho(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function ws(e, t) {
  return e ? !t : t;
}
function bp(e, t, n, o, r, i, s, c) {
  let { affectsX: u, affectsY: l } = t;
  const { isHorizontal: f, isVertical: d } = t, h = f && d, { xSnapped: p, ySnapped: g } = n, { minWidth: x, maxWidth: w, minHeight: y, maxHeight: b } = o, { x: m, y: v, width: k, height: E, aspectRatio: I } = e;
  let D = Math.floor(f ? p - e.pointerX : 0), $ = Math.floor(d ? g - e.pointerY : 0);
  const F = k + (u ? -D : D), _ = E + (l ? -$ : $), z = -i[0] * k, V = -i[1] * E;
  let S = ho(F, x, w), j = ho(_, y, b);
  if (s) {
    let T = 0, P = 0;
    u && D < 0 ? T = lt(m + D + z, s[0][0]) : !u && D > 0 && (T = ut(m + F + z, s[1][0])), l && $ < 0 ? P = lt(v + $ + V, s[0][1]) : !l && $ > 0 && (P = ut(v + _ + V, s[1][1])), S = Math.max(S, T), j = Math.max(j, P);
  }
  if (c) {
    let T = 0, P = 0;
    u && D > 0 ? T = ut(m + D, c[0][0]) : !u && D < 0 && (T = lt(m + F, c[1][0])), l && $ > 0 ? P = ut(v + $, c[0][1]) : !l && $ < 0 && (P = lt(v + _, c[1][1])), S = Math.max(S, T), j = Math.max(j, P);
  }
  if (r) {
    if (f) {
      const T = ho(F / I, y, b) * I;
      if (S = Math.max(S, T), s) {
        let P = 0;
        !u && !l || u && !l && h ? P = ut(v + V + F / I, s[1][1]) * I : P = lt(v + V + (u ? D : -D) / I, s[0][1]) * I, S = Math.max(S, P);
      }
      if (c) {
        let P = 0;
        !u && !l || u && !l && h ? P = lt(v + F / I, c[1][1]) * I : P = ut(v + (u ? D : -D) / I, c[0][1]) * I, S = Math.max(S, P);
      }
    }
    if (d) {
      const T = ho(_ * I, x, w) / I;
      if (j = Math.max(j, T), s) {
        let P = 0;
        !u && !l || l && !u && h ? P = ut(m + _ * I + z, s[1][0]) / I : P = lt(m + (l ? $ : -$) * I + z, s[0][0]) / I, j = Math.max(j, P);
      }
      if (c) {
        let P = 0;
        !u && !l || l && !u && h ? P = lt(m + _ * I, c[1][0]) / I : P = ut(m + (l ? $ : -$) * I, c[0][0]) / I, j = Math.max(j, P);
      }
    }
  }
  $ = $ + ($ < 0 ? j : -j), D = D + (D < 0 ? S : -S), r && (h ? F > _ * I ? $ = (ws(u, l) ? -D : D) / I : D = (ws(u, l) ? -$ : $) * I : f ? ($ = D / I, l = u) : (D = $ * I, u = l));
  const C = u ? m + D : m, A = l ? v + $ : v;
  return {
    width: k + (u ? -D : D),
    height: E + (l ? -$ : $),
    x: i[0] * D * (u ? -1 : 1) + C,
    y: i[1] * $ * (l ? -1 : 1) + A
  };
}
const Cc = { width: 0, height: 0, x: 0, y: 0 }, Sp = {
  ...Cc,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function Np(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, i = e.measured.width ?? 0, s = e.measured.height ?? 0, c = n[0] * i, u = n[1] * s;
  return [
    [o - c, r - u],
    [o + i - c, r + s - u]
  ];
}
function Ep({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const i = Le(e);
  let s = {
    controlDirection: xs("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: l, boundaries: f, keepAspectRatio: d, resizeDirection: h, onResizeStart: p, onResize: g, onResizeEnd: x, shouldResize: w }) {
    let y = { ...Cc }, b = { ...Sp };
    s = {
      boundaries: f,
      resizeDirection: h,
      keepAspectRatio: d,
      controlDirection: xs(l)
    };
    let m, v = null, k = [], E, I, D, $ = !1;
    const F = Ta().on("start", (_) => {
      const { nodeLookup: z, transform: V, snapGrid: S, snapToGrid: j, nodeOrigin: C, paneDomNode: A } = n();
      if (m = z.get(t), !m)
        return;
      v = A?.getBoundingClientRect() ?? null;
      const { xSnapped: T, ySnapped: P } = Cn(_.sourceEvent, {
        transform: V,
        snapGrid: S,
        snapToGrid: j,
        containerBounds: v
      });
      y = {
        width: m.measured.width ?? 0,
        height: m.measured.height ?? 0,
        x: m.position.x ?? 0,
        y: m.position.y ?? 0
      }, b = {
        ...y,
        pointerX: T,
        pointerY: P,
        aspectRatio: y.width / y.height
      }, E = void 0, I = It(m.extent) ? m.extent : void 0, m.parentId && (m.extent === "parent" || m.expandParent) && (E = z.get(m.parentId)), E && m.extent === "parent" && (I = [
        [0, 0],
        [E.measured.width, E.measured.height]
      ]), k = [], D = void 0;
      for (const [W, O] of z)
        if (O.parentId === t && (k.push({
          id: W,
          position: { ...O.position },
          extent: O.extent
        }), O.extent === "parent" || O.expandParent)) {
          const B = Np(O, m, O.origin ?? C);
          D ? D = [
            [Math.min(B[0][0], D[0][0]), Math.min(B[0][1], D[0][1])],
            [Math.max(B[1][0], D[1][0]), Math.max(B[1][1], D[1][1])]
          ] : D = B;
        }
      p?.(_, { ...y });
    }).on("drag", (_) => {
      const { transform: z, snapGrid: V, snapToGrid: S, nodeOrigin: j } = n(), C = Cn(_.sourceEvent, {
        transform: z,
        snapGrid: V,
        snapToGrid: S,
        containerBounds: v
      }), A = [];
      if (!m)
        return;
      const { x: T, y: P, width: W, height: O } = y, B = {}, G = m.origin ?? j, { width: Z, height: te, x: ae, y: J } = bp(b, s.controlDirection, C, s.boundaries, s.keepAspectRatio, G, I, D), R = Z !== W, q = te !== O, re = ae !== T && R, oe = J !== P && q;
      if (!re && !oe && !R && !q)
        return;
      if ((re || oe || G[0] === 1 || G[1] === 1) && (B.x = re ? ae : y.x, B.y = oe ? J : y.y, y.x = B.x, y.y = B.y, k.length > 0)) {
        const L = ae - T, K = J - P;
        for (const fe of k)
          fe.position = {
            x: fe.position.x - L + G[0] * (Z - W),
            y: fe.position.y - K + G[1] * (te - O)
          }, A.push(fe);
      }
      if ((R || q) && (B.width = R && (!s.resizeDirection || s.resizeDirection === "horizontal") ? Z : y.width, B.height = q && (!s.resizeDirection || s.resizeDirection === "vertical") ? te : y.height, y.width = B.width, y.height = B.height), E && m.expandParent) {
        const L = G[0] * (B.width ?? 0);
        B.x && B.x < L && (y.x = L, b.x = b.x - (B.x - L));
        const K = G[1] * (B.height ?? 0);
        B.y && B.y < K && (y.y = K, b.y = b.y - (B.y - K));
      }
      const U = vp({
        width: y.width,
        prevWidth: W,
        height: y.height,
        prevHeight: O,
        affectsX: s.controlDirection.affectsX,
        affectsY: s.controlDirection.affectsY
      }), Q = { ...y, direction: U };
      w?.(_, Q) !== !1 && ($ = !0, g?.(_, Q), o(B, A));
    }).on("end", (_) => {
      $ && (x?.(_, { ...y }), r?.({ ...y }), $ = !1);
    });
    i.call(F);
  }
  function u() {
    i.on(".drag", null);
  }
  return {
    update: c,
    destroy: u
  };
}
var Rr = { exports: {} }, Lr = {}, Vr = { exports: {} }, Hr = {};
var vs;
function kp() {
  if (vs) return Hr;
  vs = 1;
  var e = ht;
  function t(d, h) {
    return d === h && (d !== 0 || 1 / d === 1 / h) || d !== d && h !== h;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, i = e.useLayoutEffect, s = e.useDebugValue;
  function c(d, h) {
    var p = h(), g = o({ inst: { value: p, getSnapshot: h } }), x = g[0].inst, w = g[1];
    return i(
      function() {
        x.value = p, x.getSnapshot = h, u(x) && w({ inst: x });
      },
      [d, p, h]
    ), r(
      function() {
        return u(x) && w({ inst: x }), d(function() {
          u(x) && w({ inst: x });
        });
      },
      [d]
    ), s(p), p;
  }
  function u(d) {
    var h = d.getSnapshot;
    d = d.value;
    try {
      var p = h();
      return !n(d, p);
    } catch {
      return !0;
    }
  }
  function l(d, h) {
    return h();
  }
  var f = typeof window > "u" || typeof window.document > "u" || typeof window.document.createElement > "u" ? l : c;
  return Hr.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : f, Hr;
}
var bs;
function Cp() {
  return bs || (bs = 1, Vr.exports = kp()), Vr.exports;
}
var Ss;
function Ip() {
  if (Ss) return Lr;
  Ss = 1;
  var e = ht, t = Cp();
  function n(l, f) {
    return l === f && (l !== 0 || 1 / l === 1 / f) || l !== l && f !== f;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, i = e.useRef, s = e.useEffect, c = e.useMemo, u = e.useDebugValue;
  return Lr.useSyncExternalStoreWithSelector = function(l, f, d, h, p) {
    var g = i(null);
    if (g.current === null) {
      var x = { hasValue: !1, value: null };
      g.current = x;
    } else x = g.current;
    g = c(
      function() {
        function y(E) {
          if (!b) {
            if (b = !0, m = E, E = h(E), p !== void 0 && x.hasValue) {
              var I = x.value;
              if (p(I, E))
                return v = I;
            }
            return v = E;
          }
          if (I = v, o(m, E)) return I;
          var D = h(E);
          return p !== void 0 && p(I, D) ? (m = E, I) : (m = E, v = D);
        }
        var b = !1, m, v, k = d === void 0 ? null : d;
        return [
          function() {
            return y(f());
          },
          k === null ? void 0 : function() {
            return y(k());
          }
        ];
      },
      [f, d, h, p]
    );
    var w = r(l, g[0], g[1]);
    return s(
      function() {
        x.hasValue = !0, x.value = w;
      },
      [w]
    ), u(w), w;
  }, Lr;
}
var Ns;
function jp() {
  return Ns || (Ns = 1, Rr.exports = Ip()), Rr.exports;
}
var _p = jp();
const Ap = /* @__PURE__ */ Xl(_p), Mp = {}, Es = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (f, d) => {
    const h = typeof f == "function" ? f(t) : f;
    if (!Object.is(h, t)) {
      const p = t;
      t = d ?? (typeof h != "object" || h === null) ? h : Object.assign({}, t, h), n.forEach((g) => g(t, p));
    }
  }, r = () => t, u = { setState: o, getState: r, getInitialState: () => l, subscribe: (f) => (n.add(f), () => n.delete(f)), destroy: () => {
    (Mp ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(o, r, u);
  return u;
}, Dp = (e) => e ? Es(e) : Es, { useDebugValue: Pp } = ht, { useSyncExternalStoreWithSelector: $p } = Ap, Tp = (e) => e;
function Ic(e, t = Tp, n) {
  const o = $p(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return Pp(o), o;
}
const ks = (e, t) => {
  const n = Dp(e), o = (r, i = t) => Ic(n, r, i);
  return Object.assign(o, n), o;
}, zp = (e, t) => e ? ks(e, t) : ks;
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
var Or = { exports: {} }, Me = {};
var Cs;
function Rp() {
  if (Cs) return Me;
  Cs = 1;
  var e = ht;
  function t(u) {
    var l = "https://react.dev/errors/" + u;
    if (1 < arguments.length) {
      l += "?args[]=" + encodeURIComponent(arguments[1]);
      for (var f = 2; f < arguments.length; f++)
        l += "&args[]=" + encodeURIComponent(arguments[f]);
    }
    return "Minified React error #" + u + "; visit " + l + " for the full message or use the non-minified dev environment for full errors and additional helpful warnings.";
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
  function i(u, l, f) {
    var d = 3 < arguments.length && arguments[3] !== void 0 ? arguments[3] : null;
    return {
      $$typeof: r,
      key: d == null ? null : "" + d,
      children: u,
      containerInfo: l,
      implementation: f
    };
  }
  var s = e.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE;
  function c(u, l) {
    if (u === "font") return "";
    if (typeof l == "string")
      return l === "use-credentials" ? l : "";
  }
  return Me.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, Me.createPortal = function(u, l) {
    var f = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!l || l.nodeType !== 1 && l.nodeType !== 9 && l.nodeType !== 11)
      throw Error(t(299));
    return i(u, l, null, f);
  }, Me.flushSync = function(u) {
    var l = s.T, f = o.p;
    try {
      if (s.T = null, o.p = 2, u) return u();
    } finally {
      s.T = l, o.p = f, o.d.f();
    }
  }, Me.preconnect = function(u, l) {
    typeof u == "string" && (l ? (l = l.crossOrigin, l = typeof l == "string" ? l === "use-credentials" ? l : "" : void 0) : l = null, o.d.C(u, l));
  }, Me.prefetchDNS = function(u) {
    typeof u == "string" && o.d.D(u);
  }, Me.preinit = function(u, l) {
    if (typeof u == "string" && l && typeof l.as == "string") {
      var f = l.as, d = c(f, l.crossOrigin), h = typeof l.integrity == "string" ? l.integrity : void 0, p = typeof l.fetchPriority == "string" ? l.fetchPriority : void 0;
      f === "style" ? o.d.S(
        u,
        typeof l.precedence == "string" ? l.precedence : void 0,
        {
          crossOrigin: d,
          integrity: h,
          fetchPriority: p
        }
      ) : f === "script" && o.d.X(u, {
        crossOrigin: d,
        integrity: h,
        fetchPriority: p,
        nonce: typeof l.nonce == "string" ? l.nonce : void 0
      });
    }
  }, Me.preinitModule = function(u, l) {
    if (typeof u == "string")
      if (typeof l == "object" && l !== null) {
        if (l.as == null || l.as === "script") {
          var f = c(
            l.as,
            l.crossOrigin
          );
          o.d.M(u, {
            crossOrigin: f,
            integrity: typeof l.integrity == "string" ? l.integrity : void 0,
            nonce: typeof l.nonce == "string" ? l.nonce : void 0
          });
        }
      } else l == null && o.d.M(u);
  }, Me.preload = function(u, l) {
    if (typeof u == "string" && typeof l == "object" && l !== null && typeof l.as == "string") {
      var f = l.as, d = c(f, l.crossOrigin);
      o.d.L(u, f, {
        crossOrigin: d,
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
  }, Me.preloadModule = function(u, l) {
    if (typeof u == "string")
      if (l) {
        var f = c(l.as, l.crossOrigin);
        o.d.m(u, {
          as: typeof l.as == "string" && l.as !== "script" ? l.as : void 0,
          crossOrigin: f,
          integrity: typeof l.integrity == "string" ? l.integrity : void 0
        });
      } else o.d.m(u);
  }, Me.requestFormReset = function(u) {
    o.d.r(u);
  }, Me.unstable_batchedUpdates = function(u, l) {
    return u(l);
  }, Me.useFormState = function(u, l, f) {
    return s.H.useFormState(u, l, f);
  }, Me.useFormStatus = function() {
    return s.H.useHostTransitionStatus();
  }, Me.version = "19.2.7", Me;
}
var Is;
function Lp() {
  if (Is) return Or.exports;
  Is = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), Or.exports = Rp(), Or.exports;
}
var Vp = Lp();
const Uo = ui(null), Hp = Uo.Provider, jc = He.error001("react");
function de(e, t) {
  const n = Ln(Uo);
  if (n === null)
    throw new Error(jc);
  return Ic(n, e, t);
}
function xe() {
  const e = Ln(Uo);
  if (e === null)
    throw new Error(jc);
  return me(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const js = { display: "none" }, Op = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, _c = "react-flow__node-desc", Ac = "react-flow__edge-desc", Bp = "react-flow__aria-live", Fp = (e) => e.ariaLiveMessage, Wp = (e) => e.ariaLabelConfig;
function Xp({ rfId: e }) {
  const t = de(Fp);
  return a.jsx("div", { id: `${Bp}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: Op, children: t });
}
function Yp({ rfId: e, disableKeyboardA11y: t }) {
  const n = de(Wp);
  return a.jsxs(a.Fragment, { children: [a.jsx("div", { id: `${_c}-${e}`, style: js, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), a.jsx("div", { id: `${Ac}-${e}`, style: js, children: n["edge.a11yDescription.default"] }), !t && a.jsx(Xp, { rfId: e })] });
}
const Go = Oo(({ position: e = "top-left", children: t, className: n, style: o, ...r }, i) => {
  const s = `${e}`.split("-");
  return a.jsx("div", { className: Ne(["react-flow__panel", n, ...s]), style: o, ref: i, ...r, children: t });
});
Go.displayName = "Panel";
function qp({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : a.jsx(Go, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: a.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const Zp = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, po = (e) => e.id;
function Kp(e, t) {
  return ye(e.selectedNodes.map(po), t.selectedNodes.map(po)) && ye(e.selectedEdges.map(po), t.selectedEdges.map(po));
}
function Up({ onSelectionChange: e }) {
  const t = xe(), { selectedNodes: n, selectedEdges: o } = de(Zp, Kp);
  return ie(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((i) => i(r));
  }, [n, o, e]), null;
}
const Gp = (e) => !!e.onSelectionChangeHandlers;
function Jp({ onSelectionChange: e }) {
  const t = de(Gp);
  return e || t ? a.jsx(Up, { onSelectionChange: e }) : null;
}
const Mc = [0, 0], Qp = { x: 0, y: 0, zoom: 1 }, eg = [
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
], _s = [...eg, "rfId"], tg = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), As = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: Dn,
  nodeOrigin: Mc,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function ng(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: i, setNodeExtent: s, reset: c, setDefaultNodesAndEdges: u } = de(tg, ye), l = xe();
  ie(() => (u(e.defaultNodes, e.defaultEdges), () => {
    f.current = As, c();
  }), []);
  const f = ce(As);
  return ie(
    () => {
      for (const d of _s) {
        const h = e[d], p = f.current[d];
        h !== p && (typeof e[d] > "u" || (d === "nodes" ? t(h) : d === "edges" ? n(h) : d === "minZoom" ? o(h) : d === "maxZoom" ? r(h) : d === "translateExtent" ? i(h) : d === "nodeExtent" ? s(h) : d === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: Rh(h) }) : d === "fitView" ? l.setState({ fitViewQueued: h }) : d === "fitViewOptions" ? l.setState({ fitViewOptions: h }) : l.setState({ [d]: h })));
      }
      f.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    _s.map((d) => e[d])
  ), null;
}
function Ms() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function og(e) {
  const [t, n] = Y(e === "system" ? null : e);
  return ie(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = Ms(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : Ms()?.matches ? "dark" : "light";
}
const Ds = typeof document < "u" ? document : null;
function zn(e = null, t = { target: Ds, actInsideInputWithModifier: !0 }) {
  const [n, o] = Y(!1), r = ce(!1), i = ce(/* @__PURE__ */ new Set([])), [s, c] = me(() => {
    if (e !== null) {
      const l = (Array.isArray(e) ? e : [e]).filter((d) => typeof d == "string").map((d) => d.replace("+", `
`).replace(`

`, `
+`).split(`
`)), f = l.reduce((d, h) => d.concat(...h), []);
      return [l, f];
    }
    return [[], []];
  }, [e]);
  return ie(() => {
    const u = t?.target ?? Ds, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const f = (p) => {
        if (r.current = p.ctrlKey || p.metaKey || p.shiftKey || p.altKey, (!r.current || r.current && !l) && lc(p))
          return !1;
        const x = $s(p.code, c);
        if (i.current.add(p[x]), Ps(s, i.current, !1)) {
          const w = p.composedPath?.()?.[0] || p.target, y = w?.nodeName === "BUTTON" || w?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !y) && p.preventDefault(), o(!0);
        }
      }, d = (p) => {
        const g = $s(p.code, c);
        Ps(s, i.current, !0) ? (o(!1), i.current.clear()) : i.current.delete(p[g]), p.key === "Meta" && i.current.clear(), r.current = !1;
      }, h = () => {
        i.current.clear(), o(!1);
      };
      return u?.addEventListener("keydown", f), u?.addEventListener("keyup", d), window.addEventListener("blur", h), window.addEventListener("contextmenu", h), () => {
        u?.removeEventListener("keydown", f), u?.removeEventListener("keyup", d), window.removeEventListener("blur", h), window.removeEventListener("contextmenu", h);
      };
    }
  }, [e, o]), n;
}
function Ps(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function $s(e, t) {
  return t.includes(e) ? "code" : "key";
}
const rg = () => {
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
      const { width: o, height: r, minZoom: i, maxZoom: s, panZoom: c } = e.getState(), u = bi(t, o, r, i, s, n?.padding ?? 0.1);
      return c ? (await c.setViewport(u, {
        duration: n?.duration,
        ease: n?.ease,
        interpolate: n?.interpolate
      }), !0) : !1;
    },
    screenToFlowPosition: (t, n = {}) => {
      const { transform: o, snapGrid: r, snapToGrid: i, domNode: s } = e.getState();
      if (!s)
        return t;
      const { x: c, y: u } = s.getBoundingClientRect(), l = {
        x: t.x - c,
        y: t.y - u
      }, f = n.snapGrid ?? r, d = n.snapToGrid ?? i;
      return Ut(l, o, d, f);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: r, y: i } = o.getBoundingClientRect(), s = Yt(t, n);
      return {
        x: s.x + r,
        y: s.y + i
      };
    }
  }), []);
};
function Dc(e, t) {
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
    for (const u of s)
      ig(u, c);
    n.push(c);
  }
  return r.length && r.forEach((i) => {
    i.index !== void 0 ? n.splice(i.index, 0, { ...i.item }) : n.push({ ...i.item });
  }), n;
}
function ig(e, t) {
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
function Pc(e, t) {
  return Dc(e, t);
}
function $c(e, t) {
  return Dc(e, t);
}
function vt(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function Lt(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [r, i] of e) {
    const s = t.has(r);
    !(i.selected === void 0 && !s) && i.selected !== s && (n && (i.selected = s), o.push(vt(i.id, s)));
  }
  return o;
}
function Ts({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((r) => [r.id, r]));
  for (const [r, i] of e.entries()) {
    const s = t.get(i.id), c = s?.internals?.userNode ?? s;
    c !== void 0 && c !== i && n.push({ id: i.id, item: i, type: "replace" }), c === void 0 && n.push({ item: i, type: "add", index: r });
  }
  for (const [r] of t)
    o.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function zs(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const Tc = ic();
function zc(e, t, n = {}) {
  return Bh(e, t, {
    ...n,
    onError: n.onError ?? Tc
  });
}
function sg(e, t, n, o = { shouldReplaceId: !0 }) {
  return Fh(e, t, n, {
    ...o,
    onError: o.onError ?? Tc
  });
}
const Rs = (e) => jh(e), ag = (e) => tc(e);
function Rc(e) {
  return Oo(e);
}
const cg = typeof window < "u" ? Wl : ie;
function Ls(e) {
  const [t, n] = Y(BigInt(0)), [o] = Y(() => lg(() => n((r) => r + BigInt(1))));
  return cg(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function lg(e) {
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
const Lc = ui(null);
function ug({ children: e }) {
  const t = xe(), n = ge((c) => {
    const { nodes: u = [], setNodes: l, hasDefaultNodes: f, onNodesChange: d, nodeLookup: h, fitViewQueued: p, onNodesChangeMiddlewareMap: g } = t.getState();
    let x = u;
    for (const y of c)
      x = typeof y == "function" ? y(x) : y;
    let w = Ts({
      items: x,
      lookup: h
    });
    for (const y of g.values())
      w = y(w);
    f && l(x), w.length > 0 ? d?.(w) : p && window.requestAnimationFrame(() => {
      const { fitViewQueued: y, nodes: b, setNodes: m } = t.getState();
      y && m(b);
    });
  }, []), o = Ls(n), r = ge((c) => {
    const { edges: u = [], setEdges: l, hasDefaultEdges: f, onEdgesChange: d, edgeLookup: h } = t.getState();
    let p = u;
    for (const g of c)
      p = typeof g == "function" ? g(p) : g;
    f ? l(p) : d && d(Ts({
      items: p,
      lookup: h
    }));
  }, []), i = Ls(r), s = me(() => ({ nodeQueue: o, edgeQueue: i }), []);
  return a.jsx(Lc.Provider, { value: s, children: e });
}
function dg() {
  const e = Ln(Lc);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const fg = (e) => !!e.panZoom;
function ji() {
  const e = rg(), t = xe(), n = dg(), o = de(fg), r = me(() => {
    const i = (d) => t.getState().nodeLookup.get(d), s = (d) => {
      n.nodeQueue.push(d);
    }, c = (d) => {
      n.edgeQueue.push(d);
    }, u = (d) => {
      const { nodeLookup: h, nodeOrigin: p } = t.getState(), g = Rs(d) ? d : h.get(d.id), x = g.parentId ? ac(g.position, g.measured, g.parentId, h, p) : g.position, w = {
        ...g,
        position: x,
        width: g.measured?.width ?? g.width,
        height: g.measured?.height ?? g.height
      };
      return Xt(w);
    }, l = (d, h, p = { replace: !1 }) => {
      s((g) => g.map((x) => {
        if (x.id === d) {
          const w = typeof h == "function" ? h(x) : h;
          return p.replace && Rs(w) ? w : { ...x, ...w };
        }
        return x;
      }));
    }, f = (d, h, p = { replace: !1 }) => {
      c((g) => g.map((x) => {
        if (x.id === d) {
          const w = typeof h == "function" ? h(x) : h;
          return p.replace && ag(w) ? w : { ...x, ...w };
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
        return d.map((h) => ({ ...h }));
      },
      getEdge: (d) => t.getState().edgeLookup.get(d),
      setNodes: s,
      setEdges: c,
      addNodes: (d) => {
        const h = Array.isArray(d) ? d : [d];
        n.nodeQueue.push((p) => [...p, ...h]);
      },
      addEdges: (d) => {
        const h = Array.isArray(d) ? d : [d];
        n.edgeQueue.push((p) => [...p, ...h]);
      },
      toObject: () => {
        const { nodes: d = [], edges: h = [], transform: p } = t.getState(), [g, x, w] = p;
        return {
          nodes: d.map((y) => ({ ...y })),
          edges: h.map((y) => ({ ...y })),
          viewport: {
            x: g,
            y: x,
            zoom: w
          }
        };
      },
      deleteElements: async ({ nodes: d = [], edges: h = [] }) => {
        const { nodes: p, edges: g, onNodesDelete: x, onEdgesDelete: w, triggerNodeChanges: y, triggerEdgeChanges: b, onDelete: m, onBeforeDelete: v } = t.getState(), { nodes: k, edges: E } = await Ph({
          nodesToRemove: d,
          edgesToRemove: h,
          nodes: p,
          edges: g,
          onBeforeDelete: v
        }), I = E.length > 0, D = k.length > 0;
        if (I) {
          const $ = E.map(zs);
          w?.(E), b($);
        }
        if (D) {
          const $ = k.map(zs);
          x?.(k), y($);
        }
        return (D || I) && m?.({ nodes: k, edges: E }), { deletedNodes: k, deletedEdges: E };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (d, h = !0, p) => {
        const g = as(d), x = g ? d : u(d), w = p !== void 0;
        return x ? (p || t.getState().nodes).filter((y) => {
          const b = t.getState().nodeLookup.get(y.id);
          if (b && !g && (y.id === d.id || !b.internals.positionAbsolute))
            return !1;
          const m = Xt(w ? y : b), v = $n(m, x);
          return h && v > 0 || v >= m.width * m.height || v >= x.width * x.height;
        }) : [];
      },
      isNodeIntersecting: (d, h, p = !0) => {
        const x = as(d) ? d : u(d);
        if (!x)
          return !1;
        const w = $n(x, h);
        return p && w > 0 || w >= h.width * h.height || w >= x.width * x.height;
      },
      updateNode: l,
      updateNodeData: (d, h, p = { replace: !1 }) => {
        l(d, (g) => {
          const x = typeof h == "function" ? h(g) : h;
          return p.replace ? { ...g, data: x } : { ...g, data: { ...g.data, ...x } };
        }, p);
      },
      updateEdge: f,
      updateEdgeData: (d, h, p = { replace: !1 }) => {
        f(d, (g) => {
          const x = typeof h == "function" ? h(g) : h;
          return p.replace ? { ...g, data: x } : { ...g, data: { ...g.data, ...x } };
        }, p);
      },
      getNodesBounds: (d) => {
        const { nodeLookup: h, nodeOrigin: p } = t.getState();
        return _h(d, { nodeLookup: h, nodeOrigin: p });
      },
      getHandleConnections: ({ type: d, id: h, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}-${d}${h ? `-${h}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: d, handleId: h, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}${d ? h ? `-${d}-${h}` : `-${d}` : ""}`)?.values() ?? []),
      fitView: async (d) => {
        const h = t.getState().fitViewResolver ?? zh();
        return t.setState({ fitViewQueued: !0, fitViewOptions: d, fitViewResolver: h }), n.nodeQueue.push((p) => [...p]), h.promise;
      }
    };
  }, []);
  return me(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const Vs = (e) => e.selected, hg = typeof window < "u" ? window : void 0;
function pg({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = xe(), { deleteElements: o } = ji(), r = zn(e, { actInsideInputWithModifier: !1 }), i = zn(t, { target: hg });
  ie(() => {
    if (r) {
      const { edges: s, nodes: c } = n.getState();
      o({ nodes: c.filter(Vs), edges: s.filter(Vs) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), ie(() => {
    n.setState({ multiSelectionActive: i });
  }, [i]);
}
function gg(e) {
  const t = xe();
  ie(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = Si(e.current);
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
const Jo = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, mg = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function yg({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: i = Nt.Free, zoomOnDoubleClick: s = !0, panOnDrag: c = !0, defaultViewport: u, translateExtent: l, minZoom: f, maxZoom: d, zoomActivationKeyCode: h, preventScrolling: p = !0, children: g, noWheelClassName: x, noPanClassName: w, onViewportChange: y, isControlledViewport: b, paneClickDistance: m, selectionOnDrag: v }) {
  const k = xe(), E = ce(null), { userSelectionActive: I, lib: D, connectionInProgress: $ } = de(mg, ye), F = zn(h), _ = ce();
  gg(E);
  const z = ge((V) => {
    y?.({ x: V[0], y: V[1], zoom: V[2] }), b || k.setState({ transform: V });
  }, [y, b]);
  return ie(() => {
    if (E.current) {
      _.current = wp({
        domNode: E.current,
        minZoom: f,
        maxZoom: d,
        translateExtent: l,
        viewport: u,
        onDraggingChange: (C) => k.setState((A) => A.paneDragging === C ? A : { paneDragging: C }),
        onPanZoomStart: (C, A) => {
          const { onViewportChangeStart: T, onMoveStart: P } = k.getState();
          P?.(C, A), T?.(A);
        },
        onPanZoom: (C, A) => {
          const { onViewportChange: T, onMove: P } = k.getState();
          P?.(C, A), T?.(A);
        },
        onPanZoomEnd: (C, A) => {
          const { onViewportChangeEnd: T, onMoveEnd: P } = k.getState();
          P?.(C, A), T?.(A);
        }
      });
      const { x: V, y: S, zoom: j } = _.current.getViewport();
      return k.setState({
        panZoom: _.current,
        transform: [V, S, j],
        domNode: E.current.closest(".react-flow")
      }), () => {
        _.current?.destroy();
      };
    }
  }, []), ie(() => {
    _.current?.update({
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
      noPanClassName: w,
      userSelectionActive: I,
      noWheelClassName: x,
      lib: D,
      onTransformChange: z,
      connectionInProgress: $,
      selectionOnDrag: v,
      paneClickDistance: m
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
    w,
    I,
    x,
    D,
    z,
    $,
    v,
    m
  ]), a.jsx("div", { className: "react-flow__renderer", ref: E, style: Jo, children: g });
}
const xg = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function wg() {
  const { userSelectionActive: e, userSelectionRect: t } = de(xg, ye);
  return e && t ? a.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const Br = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, vg = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function bg({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Pn.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: i, selectionOnDrag: s, onSelectionStart: c, onSelectionEnd: u, onPaneClick: l, onPaneContextMenu: f, onPaneScroll: d, onPaneMouseEnter: h, onPaneMouseMove: p, onPaneMouseLeave: g, children: x }) {
  const w = ce(0), y = xe(), { userSelectionActive: b, elementsSelectable: m, dragging: v, connectionInProgress: k, panBy: E, autoPanSpeed: I } = de(vg, ye), D = m && (e || b), $ = ce(null), F = ce(), _ = ce(/* @__PURE__ */ new Set()), z = ce(/* @__PURE__ */ new Set()), V = ce(!1), S = ce({ x: 0, y: 0 }), j = ce(!1), C = (R) => {
    if (V.current || k) {
      V.current = !1;
      return;
    }
    l?.(R), y.getState().resetSelectedElements(), y.setState({ nodesSelectionActive: !1 });
  }, A = (R) => {
    if (Array.isArray(o) && o?.includes(2)) {
      R.preventDefault();
      return;
    }
    f?.(R);
  }, T = d ? (R) => d(R) : void 0, P = (R) => {
    V.current && (R.stopPropagation(), V.current = !1);
  }, W = (R) => {
    const { domNode: q, transform: re } = y.getState();
    if (F.current = q?.getBoundingClientRect(), !F.current)
      return;
    const oe = R.target === $.current;
    if (!oe && !!R.target.closest(".nokey") || !e || !(s && oe || t) || R.button !== 0 || !R.isPrimary)
      return;
    R.target?.setPointerCapture?.(R.pointerId), V.current = !1;
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
    const { transform: oe, nodeLookup: U, edgeLookup: Q, connectionLookup: se, triggerNodeChanges: L, triggerEdgeChanges: K, defaultEdgeOptions: fe } = y.getState(), ue = { x: re.startX, y: re.startY }, { x: je, y: Oe } = Yt(ue, oe), Be = {
      startX: ue.x,
      startY: ue.y,
      x: R < je ? R : je,
      y: q < Oe ? q : Oe,
      width: Math.abs(R - je),
      height: Math.abs(q - Oe)
    }, pt = _.current, et = z.current;
    _.current = new Set(wi(U, Be, oe, n === Pn.Partial, !0).map((_e) => _e.id)), z.current = /* @__PURE__ */ new Set();
    const Ee = fe?.selectable ?? !0;
    for (const _e of _.current) {
      const $e = se.get(_e);
      if ($e)
        for (const { edgeId: Te } of $e.values()) {
          const Ke = Q.get(Te);
          Ke && (Ke.selectable ?? Ee) && z.current.add(Te);
        }
    }
    if (!cs(pt, _.current)) {
      const _e = Lt(U, _.current, !0);
      L(_e);
    }
    if (!cs(et, z.current)) {
      const _e = Lt(Q, z.current);
      K(_e);
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
    const [R, q] = vi(S.current, F.current, I);
    E({ x: R, y: q }).then((re) => {
      if (!V.current || !re) {
        w.current = requestAnimationFrame(B);
        return;
      }
      const { x: oe, y: U } = S.current;
      O(oe, U), w.current = requestAnimationFrame(B);
    });
  }
  const G = () => {
    cancelAnimationFrame(w.current), w.current = 0, j.current = !1;
  };
  ie(() => () => G(), []);
  const Z = (R) => {
    const { userSelectionRect: q, transform: re, resetSelectedElements: oe } = y.getState();
    if (!F.current || !q)
      return;
    const { x: U, y: Q } = qe(R.nativeEvent, F.current);
    S.current = { x: U, y: Q };
    const se = Yt({ x: q.startX, y: q.startY }, re);
    if (!V.current) {
      const L = t ? 0 : i;
      if (Math.hypot(U - se.x, Q - se.y) <= L)
        return;
      oe(), c?.(R);
    }
    V.current = !0, j.current || (B(), j.current = !0), O(U, Q);
  }, te = (R) => {
    R.button === 0 && (R.target?.releasePointerCapture?.(R.pointerId), !b && R.target === $.current && y.getState().userSelectionRect && C?.(R), y.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), V.current && (u?.(R), y.setState({
      nodesSelectionActive: _.current.size > 0
    })), G());
  }, ae = (R) => {
    R.target?.releasePointerCapture?.(R.pointerId), G();
  }, J = o === !0 || Array.isArray(o) && o.includes(0);
  return a.jsxs("div", { className: Ne(["react-flow__pane", { draggable: J, dragging: v, selection: e }]), onClick: D ? void 0 : Br(C, $), onContextMenu: Br(A, $), onWheel: Br(T, $), onPointerEnter: D ? void 0 : h, onPointerMove: D ? Z : p, onPointerUp: D ? te : void 0, onPointerCancel: D ? ae : void 0, onPointerDownCapture: D ? W : void 0, onClickCapture: D ? P : void 0, onPointerLeave: g, ref: $, style: Jo, children: [x, a.jsx(wg, {})] });
}
function ri({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: i, multiSelectionActive: s, nodeLookup: c, onError: u } = t.getState(), l = c.get(e);
  if (!l) {
    u?.("012", He.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && s) && (i({ nodes: [l], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function Vc({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: i, nodeClickDistance: s }) {
  const c = xe(), [u, l] = Y(!1), f = ce();
  return ie(() => {
    f.current = ip({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (d) => {
        ri({
          id: d,
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
  }, []), ie(() => {
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
  }, [n, o, t, i, e, r, s]), u;
}
const Sg = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function Hc() {
  const e = xe();
  return ge((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: i, nodesDraggable: s, onError: c, updateNodePositions: u, nodeLookup: l, nodeOrigin: f } = e.getState(), d = /* @__PURE__ */ new Map(), h = Sg(s), p = r ? i[0] : 5, g = r ? i[1] : 5, x = n.direction.x * p * n.factor, w = n.direction.y * g * n.factor;
    for (const [, y] of l) {
      if (!h(y))
        continue;
      let b = {
        x: y.internals.positionAbsolute.x + x,
        y: y.internals.positionAbsolute.y + w
      };
      r && (b = Fn(b, i));
      const { position: m, positionAbsolute: v } = nc({
        nodeId: y.id,
        nextPosition: b,
        nodeLookup: l,
        nodeExtent: o,
        nodeOrigin: f,
        onError: c
      });
      y.position = m, y.internals.positionAbsolute = v, d.set(y.id, y);
    }
    u(d);
  }, []);
}
const _i = ui(null), Ng = _i.Provider;
_i.Consumer;
const Oc = () => Ln(_i), Eg = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), kg = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: i, connection: s } = o, { fromHandle: c, toHandle: u, isValid: l } = s, f = u?.nodeId === e && u?.id === t && u?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: f,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: i === Ft.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!r,
    valid: f && l
  };
};
function Cg({ type: e = "source", position: t = ee.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: i = !0, id: s, onConnect: c, children: u, className: l, onMouseDown: f, onTouchStart: d, ...h }, p) {
  const g = s || null, x = e === "target", w = xe(), y = Oc(), { connectOnClick: b, noPanClassName: m, rfId: v } = de(Eg, ye), { connectingFrom: k, connectingTo: E, clickConnecting: I, isPossibleEndHandle: D, connectionInProcess: $, clickConnectionInProcess: F, valid: _ } = de(kg(y, g, e), ye);
  y || w.getState().onError?.("010", He.error010());
  const z = (j) => {
    const { defaultEdgeOptions: C, onConnect: A, hasDefaultEdges: T } = w.getState(), P = {
      ...C,
      ...j
    };
    if (T) {
      const { edges: W, setEdges: O, onError: B } = w.getState();
      O(zc(P, W, { onError: B }));
    }
    A?.(P), c?.(P);
  }, V = (j) => {
    if (!y)
      return;
    const C = uc(j.nativeEvent);
    if (r && (C && j.button === 0 || !C)) {
      const A = w.getState();
      oi.onPointerDown(j.nativeEvent, {
        handleDomNode: j.currentTarget,
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
        onConnectEnd: (...T) => w.getState().onConnectEnd?.(...T),
        updateConnection: A.updateConnection,
        onConnect: z,
        isValidConnection: n || ((...T) => w.getState().isValidConnection?.(...T) ?? !0),
        getTransform: () => w.getState().transform,
        getFromHandle: () => w.getState().connection.fromHandle,
        autoPanSpeed: A.autoPanSpeed,
        dragThreshold: A.connectionDragThreshold
      });
    }
    C ? f?.(j) : d?.(j);
  }, S = (j) => {
    const { onClickConnectStart: C, onClickConnectEnd: A, connectionClickStartHandle: T, connectionMode: P, isValidConnection: W, lib: O, rfId: B, nodeLookup: G, connection: Z } = w.getState();
    if (!y || !T && !r)
      return;
    if (!T) {
      C?.(j.nativeEvent, { nodeId: y, handleId: g, handleType: e }), w.setState({ connectionClickStartHandle: { nodeId: y, type: e, id: g } });
      return;
    }
    const te = cc(j.target), ae = n || W, { connection: J, isValid: R } = oi.isValid(j.nativeEvent, {
      handle: {
        nodeId: y,
        id: g,
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
    delete q.inProgress, q.toPosition = q.toHandle ? q.toHandle.position : null, A?.(j, q), w.setState({ connectionClickStartHandle: null });
  };
  return a.jsx("div", { "data-handleid": g, "data-nodeid": y, "data-handlepos": t, "data-id": `${v}-${y}-${g}-${e}`, className: Ne([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    m,
    l,
    {
      source: !x,
      target: x,
      connectable: o,
      connectablestart: r,
      connectableend: i,
      clickconnecting: I,
      connectingfrom: k,
      connectingto: E,
      valid: _,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!$ || D) && ($ || F ? i : r)
    }
  ]), onMouseDown: V, onTouchStart: V, onClick: b ? S : void 0, ref: p, ...h, children: u });
}
const Zt = ve(Rc(Cg));
function Ig({ data: e, isConnectable: t, sourcePosition: n = ee.Bottom }) {
  return a.jsxs(a.Fragment, { children: [e?.label, a.jsx(Zt, { type: "source", position: n, isConnectable: t })] });
}
function jg({ data: e, isConnectable: t, targetPosition: n = ee.Top, sourcePosition: o = ee.Bottom }) {
  return a.jsxs(a.Fragment, { children: [a.jsx(Zt, { type: "target", position: n, isConnectable: t }), e?.label, a.jsx(Zt, { type: "source", position: o, isConnectable: t })] });
}
function _g() {
  return null;
}
function Ag({ data: e, isConnectable: t, targetPosition: n = ee.Top }) {
  return a.jsxs(a.Fragment, { children: [a.jsx(Zt, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const zo = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, Hs = {
  input: Ig,
  default: jg,
  output: Ag,
  group: _g
};
function Mg(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const Dg = (e) => {
  const { width: t, height: n, x: o, y: r } = Bn(e.nodeLookup, {
    filter: (i) => !!i.selected
  });
  return {
    width: Ye(t) ? t : null,
    height: Ye(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function Pg({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = xe(), { width: r, height: i, transformString: s, userSelectionActive: c } = de(Dg, ye), u = Hc(), l = ce(null);
  ie(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const f = !c && r !== null && i !== null;
  if (Vc({
    nodeRef: l,
    disabled: !f
  }), !f)
    return null;
  const d = e ? (p) => {
    const g = o.getState().nodes.filter((x) => x.selected);
    e(p, g);
  } : void 0, h = (p) => {
    Object.prototype.hasOwnProperty.call(zo, p.key) && (p.preventDefault(), u({
      direction: zo[p.key],
      factor: p.shiftKey ? 4 : 1
    }));
  };
  return a.jsx("div", { className: Ne(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: s
  }, children: a.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: d, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : h, style: {
    width: r,
    height: i
  } }) });
}
const Os = typeof window < "u" ? window : void 0, $g = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function Bc({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, paneClickDistance: c, deleteKeyCode: u, selectionKeyCode: l, selectionOnDrag: f, selectionMode: d, onSelectionStart: h, onSelectionEnd: p, multiSelectionKeyCode: g, panActivationKeyCode: x, zoomActivationKeyCode: w, elementsSelectable: y, zoomOnScroll: b, zoomOnPinch: m, panOnScroll: v, panOnScrollSpeed: k, panOnScrollMode: E, zoomOnDoubleClick: I, panOnDrag: D, autoPanOnSelection: $, defaultViewport: F, translateExtent: _, minZoom: z, maxZoom: V, preventScrolling: S, onSelectionContextMenu: j, noWheelClassName: C, noPanClassName: A, disableKeyboardA11y: T, onViewportChange: P, isControlledViewport: W }) {
  const { nodesSelectionActive: O, userSelectionActive: B } = de($g, ye), G = zn(l, { target: Os }), Z = zn(x, { target: Os }), te = Z || D, ae = Z || v, J = f && te !== !0, R = G || B || J;
  return pg({ deleteKeyCode: u, multiSelectionKeyCode: g }), a.jsx(yg, { onPaneContextMenu: i, elementsSelectable: y, zoomOnScroll: b, zoomOnPinch: m, panOnScroll: ae, panOnScrollSpeed: k, panOnScrollMode: E, zoomOnDoubleClick: I, panOnDrag: !G && te, defaultViewport: F, translateExtent: _, minZoom: z, maxZoom: V, zoomActivationKeyCode: w, preventScrolling: S, noWheelClassName: C, noPanClassName: A, onViewportChange: P, isControlledViewport: W, paneClickDistance: c, selectionOnDrag: J, children: a.jsxs(bg, { onSelectionStart: h, onSelectionEnd: p, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, panOnDrag: te, autoPanOnSelection: $, isSelecting: !!R, selectionMode: d, selectionKeyPressed: G, paneClickDistance: c, selectionOnDrag: J, children: [e, O && a.jsx(Pg, { onSelectionContextMenu: j, noPanClassName: A, disableKeyboardA11y: T })] }) });
}
Bc.displayName = "FlowRenderer";
const Tg = ve(Bc), zg = (e) => (t) => e ? wi(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function Rg(e) {
  return de(ge(zg(e), [e]), ye);
}
const Lg = (e) => e.updateNodeInternals;
function Vg() {
  const e = de(Lg), [t] = Y(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
function Hg({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = xe(), i = ce(null), s = ce(null), c = ce(e.sourcePosition), u = ce(e.targetPosition), l = ce(t), f = n && !!e.internals.handleBounds;
  return ie(() => {
    i.current && !e.hidden && (!f || s.current !== i.current) && (s.current && o?.unobserve(s.current), o?.observe(i.current), s.current = i.current);
  }, [f, e.hidden]), ie(() => () => {
    s.current && (o?.unobserve(s.current), s.current = null);
  }, []), ie(() => {
    if (i.current) {
      const d = l.current !== t, h = c.current !== e.sourcePosition, p = u.current !== e.targetPosition;
      (d || h || p) && (l.current = t, c.current = e.sourcePosition, u.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: i.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), i;
}
function Og({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: i, onDoubleClick: s, nodesDraggable: c, elementsSelectable: u, nodesConnectable: l, nodesFocusable: f, resizeObserver: d, noDragClassName: h, noPanClassName: p, disableKeyboardA11y: g, rfId: x, nodeTypes: w, nodeClickDistance: y, onError: b }) {
  const { node: m, internals: v, isParent: k } = de((R) => {
    const q = R.nodeLookup.get(e), re = R.parentLookup.has(e);
    return {
      node: q,
      internals: q.internals,
      isParent: re
    };
  }, ye);
  let E = m.type || "default", I = w?.[E] || Hs[E];
  I === void 0 && (b?.("003", He.error003(E)), E = "default", I = w?.default || Hs.default);
  const D = !!(m.draggable || c && typeof m.draggable > "u"), $ = !!(m.selectable || u && typeof m.selectable > "u"), F = !!(m.connectable || l && typeof m.connectable > "u"), _ = !!(m.focusable || f && typeof m.focusable > "u"), z = xe(), V = sc(m), S = Hg({ node: m, nodeType: E, hasDimensions: V, resizeObserver: d }), j = Vc({
    nodeRef: S,
    disabled: m.hidden || !D,
    noDragClassName: h,
    handleSelector: m.dragHandle,
    nodeId: e,
    isSelectable: $,
    nodeClickDistance: y
  }), C = Hc();
  if (m.hidden)
    return null;
  const A = at(m), T = Mg(m), P = $ || D || t || n || o || r, W = n ? (R) => n(R, { ...v.userNode }) : void 0, O = o ? (R) => o(R, { ...v.userNode }) : void 0, B = r ? (R) => r(R, { ...v.userNode }) : void 0, G = i ? (R) => i(R, { ...v.userNode }) : void 0, Z = s ? (R) => s(R, { ...v.userNode }) : void 0, te = (R) => {
    const { selectNodesOnDrag: q, nodeDragThreshold: re } = z.getState();
    $ && (!q || !D || re > 0) && ri({
      id: e,
      store: z,
      nodeRef: S
    }), t && t(R, { ...v.userNode });
  }, ae = (R) => {
    if (!(lc(R.nativeEvent) || g)) {
      if (Ga.includes(R.key) && $) {
        const q = R.key === "Escape";
        ri({
          id: e,
          store: z,
          unselect: q,
          nodeRef: S
        });
      } else if (D && m.selected && Object.prototype.hasOwnProperty.call(zo, R.key)) {
        R.preventDefault();
        const { ariaLabelConfig: q } = z.getState();
        z.setState({
          ariaLiveMessage: q["node.a11yDescription.ariaLiveMessage"]({
            direction: R.key.replace("Arrow", "").toLowerCase(),
            x: ~~v.positionAbsolute.x,
            y: ~~v.positionAbsolute.y
          })
        }), C({
          direction: zo[R.key],
          factor: R.shiftKey ? 4 : 1
        });
      }
    }
  }, J = () => {
    if (g || !S.current?.matches(":focus-visible"))
      return;
    const { transform: R, width: q, height: re, autoPanOnNodeFocus: oe, setCenter: U } = z.getState();
    if (!oe)
      return;
    wi(/* @__PURE__ */ new Map([[e, m]]), { x: 0, y: 0, width: q, height: re }, R, !0).length > 0 || U(m.position.x + A.width / 2, m.position.y + A.height / 2, {
      zoom: R[2]
    });
  };
  return a.jsx("div", { className: Ne([
    "react-flow__node",
    `react-flow__node-${E}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [p]: D
    },
    m.className,
    {
      selected: m.selected,
      selectable: $,
      parent: k,
      draggable: D,
      dragging: j
    }
  ]), ref: S, style: {
    zIndex: v.z,
    transform: `translate(${v.positionAbsolute.x}px,${v.positionAbsolute.y}px)`,
    pointerEvents: P ? "all" : "none",
    visibility: V ? "visible" : "hidden",
    ...m.style,
    ...T
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: W, onMouseMove: O, onMouseLeave: B, onContextMenu: G, onClick: te, onDoubleClick: Z, onKeyDown: _ ? ae : void 0, tabIndex: _ ? 0 : void 0, onFocus: _ ? J : void 0, role: m.ariaRole ?? (_ ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": g ? void 0 : `${_c}-${x}`, "aria-label": m.ariaLabel, ...m.domAttributes, children: a.jsx(Ng, { value: e, children: a.jsx(I, { id: e, data: m.data, type: E, positionAbsoluteX: v.positionAbsolute.x, positionAbsoluteY: v.positionAbsolute.y, selected: m.selected ?? !1, selectable: $, draggable: D, deletable: m.deletable ?? !0, isConnectable: F, sourcePosition: m.sourcePosition, targetPosition: m.targetPosition, dragging: j, dragHandle: m.dragHandle, zIndex: v.z, parentId: m.parentId, ...A }) }) });
}
var Bg = ve(Og);
const Fg = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function Fc(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: i } = de(Fg, ye), s = Rg(e.onlyRenderVisibleElements), c = Vg();
  return a.jsx("div", { className: "react-flow__nodes", style: Jo, children: s.map((u) => (
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
    a.jsx(Bg, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: i }, u)
  )) });
}
Fc.displayName = "NodeRenderer";
const Wg = ve(Fc);
function Xg(e) {
  return de(ge((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const i = n.nodeLookup.get(r.source), s = n.nodeLookup.get(r.target);
        i && s && Hh({
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
const Yg = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return a.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, qg = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return a.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Bs = {
  [Po.Arrow]: Yg,
  [Po.ArrowClosed]: qg
};
function Zg(e) {
  const t = xe();
  return me(() => Object.prototype.hasOwnProperty.call(Bs, e) ? Bs[e] : (t.getState().onError?.("009", He.error009(e)), null), [e]);
}
const Kg = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: i = "strokeWidth", strokeWidth: s, orient: c = "auto-start-reverse" }) => {
  const u = Zg(t);
  return u ? a.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: i, orient: c, refX: "0", refY: "0", children: a.jsx(u, { color: n, strokeWidth: s }) }) : null;
}, Wc = ({ defaultColor: e, rfId: t }) => {
  const n = de((i) => i.edges), o = de((i) => i.defaultEdgeOptions), r = me(() => Zh(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? a.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: a.jsx("defs", { children: r.map((i) => a.jsx(Kg, { id: i.id, type: i.type, color: i.color, width: i.width, height: i.height, markerUnits: i.markerUnits, strokeWidth: i.strokeWidth, orient: i.orient }, i.id)) }) }) : null;
};
Wc.displayName = "MarkerDefinitions";
var Ug = ve(Wc);
function Xc({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: i, labelBgPadding: s = [2, 4], labelBgBorderRadius: c = 2, children: u, className: l, ...f }) {
  const [d, h] = Y({ x: 1, y: 0, width: 0, height: 0 }), p = Ne(["react-flow__edge-textwrapper", l]), g = ce(null);
  return ie(() => {
    if (g.current) {
      const x = g.current.getBBox();
      h({
        x: x.x,
        y: x.y,
        width: x.width,
        height: x.height
      });
    }
  }, [n]), n ? a.jsxs("g", { transform: `translate(${e - d.width / 2} ${t - d.height / 2})`, className: p, visibility: d.width ? "visible" : "hidden", ...f, children: [r && a.jsx("rect", { width: d.width + 2 * s[0], x: -s[0], y: -s[1], height: d.height + 2 * s[1], className: "react-flow__edge-textbg", style: i, rx: c, ry: c }), a.jsx("text", { className: "react-flow__edge-text", y: d.height / 2, dy: "0.3em", ref: g, style: o, children: n }), u] }) : null;
}
Xc.displayName = "EdgeText";
const Gg = ve(Xc);
function Wn({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: c, labelBgBorderRadius: u, interactionWidth: l = 20, ...f }) {
  return a.jsxs(a.Fragment, { children: [a.jsx("path", { ...f, d: e, fill: "none", className: Ne(["react-flow__edge-path", f.className]) }), l ? a.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, o && Ye(t) && Ye(n) ? a.jsx(Gg, { x: t, y: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: c, labelBgBorderRadius: u }) : null] });
}
function Fs({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === ee.Left || e === ee.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function Yc({ sourceX: e, sourceY: t, sourcePosition: n = ee.Bottom, targetX: o, targetY: r, targetPosition: i = ee.Top }) {
  const [s, c] = Fs({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [u, l] = Fs({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [f, d, h, p] = dc({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: r,
    sourceControlX: s,
    sourceControlY: c,
    targetControlX: u,
    targetControlY: l
  });
  return [
    `M${e},${t} C${s},${c} ${u},${l} ${o},${r}`,
    f,
    d,
    h,
    p
  ];
}
function qc(e) {
  return ve(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: c, label: u, labelStyle: l, labelShowBg: f, labelBgStyle: d, labelBgPadding: h, labelBgBorderRadius: p, style: g, markerEnd: x, markerStart: w, interactionWidth: y }) => {
    const [b, m, v] = Yc({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: c
    }), k = e.isInternal ? void 0 : t;
    return a.jsx(Wn, { id: k, path: b, labelX: m, labelY: v, label: u, labelStyle: l, labelShowBg: f, labelBgStyle: d, labelBgPadding: h, labelBgBorderRadius: p, style: g, markerEnd: x, markerStart: w, interactionWidth: y });
  });
}
const Jg = qc({ isInternal: !1 }), Zc = qc({ isInternal: !0 });
Jg.displayName = "SimpleBezierEdge";
Zc.displayName = "SimpleBezierEdgeInternal";
function Kc(e) {
  return ve(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: d, style: h, sourcePosition: p = ee.Bottom, targetPosition: g = ee.Top, markerEnd: x, markerStart: w, pathOptions: y, interactionWidth: b }) => {
    const [m, v, k] = To({
      sourceX: n,
      sourceY: o,
      sourcePosition: p,
      targetX: r,
      targetY: i,
      targetPosition: g,
      borderRadius: y?.borderRadius,
      offset: y?.offset,
      stepPosition: y?.stepPosition
    }), E = e.isInternal ? void 0 : t;
    return a.jsx(Wn, { id: E, path: m, labelX: v, labelY: k, label: s, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: d, style: h, markerEnd: x, markerStart: w, interactionWidth: b });
  });
}
const Uc = Kc({ isInternal: !1 }), Gc = Kc({ isInternal: !0 });
Uc.displayName = "SmoothStepEdge";
Gc.displayName = "SmoothStepEdgeInternal";
function Jc(e) {
  return ve(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return a.jsx(Uc, { ...n, id: o, pathOptions: me(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Qg = Jc({ isInternal: !1 }), Qc = Jc({ isInternal: !0 });
Qg.displayName = "StepEdge";
Qc.displayName = "StepEdgeInternal";
function el(e) {
  return ve(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: d, style: h, markerEnd: p, markerStart: g, interactionWidth: x }) => {
    const [w, y, b] = gc({ sourceX: n, sourceY: o, targetX: r, targetY: i }), m = e.isInternal ? void 0 : t;
    return a.jsx(Wn, { id: m, path: w, labelX: y, labelY: b, label: s, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: d, style: h, markerEnd: p, markerStart: g, interactionWidth: x });
  });
}
const em = el({ isInternal: !1 }), tl = el({ isInternal: !0 });
em.displayName = "StraightEdge";
tl.displayName = "StraightEdgeInternal";
function nl(e) {
  return ve(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s = ee.Bottom, targetPosition: c = ee.Top, label: u, labelStyle: l, labelShowBg: f, labelBgStyle: d, labelBgPadding: h, labelBgBorderRadius: p, style: g, markerEnd: x, markerStart: w, pathOptions: y, interactionWidth: b }) => {
    const [m, v, k] = fc({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: c,
      curvature: y?.curvature
    }), E = e.isInternal ? void 0 : t;
    return a.jsx(Wn, { id: E, path: m, labelX: v, labelY: k, label: u, labelStyle: l, labelShowBg: f, labelBgStyle: d, labelBgPadding: h, labelBgBorderRadius: p, style: g, markerEnd: x, markerStart: w, interactionWidth: b });
  });
}
const tm = nl({ isInternal: !1 }), ol = nl({ isInternal: !0 });
tm.displayName = "BezierEdge";
ol.displayName = "BezierEdgeInternal";
const Ws = {
  default: ol,
  straight: tl,
  step: Qc,
  smoothstep: Gc,
  simplebezier: Zc
}, Xs = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, nm = (e, t, n) => n === ee.Left ? e - t : n === ee.Right ? e + t : e, om = (e, t, n) => n === ee.Top ? e - t : n === ee.Bottom ? e + t : e, Ys = "react-flow__edgeupdater";
function qs({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: i, onMouseOut: s, type: c }) {
  return a.jsx("circle", { onMouseDown: r, onMouseEnter: i, onMouseOut: s, className: Ne([Ys, `${Ys}-${c}`]), cx: nm(t, o, e), cy: om(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function rm({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: i, targetY: s, sourcePosition: c, targetPosition: u, onReconnect: l, onReconnectStart: f, onReconnectEnd: d, setReconnecting: h, setUpdateHover: p }) {
  const g = xe(), x = (v, k) => {
    if (v.button !== 0)
      return;
    const { autoPanOnConnect: E, domNode: I, connectionMode: D, connectionRadius: $, lib: F, onConnectStart: _, cancelConnection: z, nodeLookup: V, rfId: S, panBy: j, updateConnection: C } = g.getState(), A = k.type === "target", T = (O, B) => {
      h(!1), d?.(O, n, k.type, B);
    }, P = (O) => l?.(n, O), W = (O, B) => {
      h(!0), f?.(v, n, k.type), _?.(O, B);
    };
    oi.onPointerDown(v.nativeEvent, {
      autoPanOnConnect: E,
      connectionMode: D,
      connectionRadius: $,
      domNode: I,
      handleId: k.id,
      nodeId: k.nodeId,
      nodeLookup: V,
      isTarget: A,
      edgeUpdaterType: k.type,
      lib: F,
      flowId: S,
      cancelConnection: z,
      panBy: j,
      isValidConnection: (...O) => g.getState().isValidConnection?.(...O) ?? !0,
      onConnect: P,
      onConnectStart: W,
      onConnectEnd: (...O) => g.getState().onConnectEnd?.(...O),
      onReconnectEnd: T,
      updateConnection: C,
      getTransform: () => g.getState().transform,
      getFromHandle: () => g.getState().connection.fromHandle,
      dragThreshold: g.getState().connectionDragThreshold,
      handleDomNode: v.currentTarget
    });
  }, w = (v) => x(v, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), y = (v) => x(v, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), b = () => p(!0), m = () => p(!1);
  return a.jsxs(a.Fragment, { children: [(e === !0 || e === "source") && a.jsx(qs, { position: c, centerX: o, centerY: r, radius: t, onMouseDown: w, onMouseEnter: b, onMouseOut: m, type: "source" }), (e === !0 || e === "target") && a.jsx(qs, { position: u, centerX: i, centerY: s, radius: t, onMouseDown: y, onMouseEnter: b, onMouseOut: m, type: "target" })] });
}
function im({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: i, onContextMenu: s, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, reconnectRadius: f, onReconnect: d, onReconnectStart: h, onReconnectEnd: p, rfId: g, edgeTypes: x, noPanClassName: w, onError: y, disableKeyboardA11y: b }) {
  let m = de((U) => U.edgeLookup.get(e));
  const v = de((U) => U.defaultEdgeOptions);
  m = v ? { ...v, ...m } : m;
  let k = m.type || "default", E = x?.[k] || Ws[k];
  E === void 0 && (y?.("011", He.error011(k)), k = "default", E = x?.default || Ws.default);
  const I = !!(m.focusable || t && typeof m.focusable > "u"), D = typeof d < "u" && (m.reconnectable || n && typeof m.reconnectable > "u"), $ = !!(m.selectable || o && typeof m.selectable > "u"), F = ce(null), [_, z] = Y(!1), [V, S] = Y(!1), j = xe(), { zIndex: C, sourceX: A, sourceY: T, targetX: P, targetY: W, sourcePosition: O, targetPosition: B } = de(ge((U) => {
    const Q = U.nodeLookup.get(m.source), se = U.nodeLookup.get(m.target);
    if (!Q || !se)
      return {
        zIndex: m.zIndex,
        ...Xs
      };
    const L = qh({
      id: e,
      sourceNode: Q,
      targetNode: se,
      sourceHandle: m.sourceHandle || null,
      targetHandle: m.targetHandle || null,
      connectionMode: U.connectionMode,
      onError: y
    });
    return {
      zIndex: Vh({
        selected: m.selected,
        zIndex: m.zIndex,
        sourceNode: Q,
        targetNode: se,
        elevateOnSelect: U.elevateEdgesOnSelect,
        zIndexMode: U.zIndexMode
      }),
      ...L || Xs
    };
  }, [m.source, m.target, m.sourceHandle, m.targetHandle, m.selected, m.zIndex]), ye), G = me(() => m.markerStart ? `url('#${ti(m.markerStart, g)}')` : void 0, [m.markerStart, g]), Z = me(() => m.markerEnd ? `url('#${ti(m.markerEnd, g)}')` : void 0, [m.markerEnd, g]);
  if (m.hidden || A === null || T === null || P === null || W === null)
    return null;
  const te = (U) => {
    const { addSelectedEdges: Q, unselectNodesAndEdges: se, multiSelectionActive: L } = j.getState();
    $ && (j.setState({ nodesSelectionActive: !1 }), m.selected && L ? (se({ nodes: [], edges: [m] }), F.current?.blur()) : Q([e])), r && r(U, m);
  }, ae = i ? (U) => {
    i(U, { ...m });
  } : void 0, J = s ? (U) => {
    s(U, { ...m });
  } : void 0, R = c ? (U) => {
    c(U, { ...m });
  } : void 0, q = u ? (U) => {
    u(U, { ...m });
  } : void 0, re = l ? (U) => {
    l(U, { ...m });
  } : void 0, oe = (U) => {
    if (!b && Ga.includes(U.key) && $) {
      const { unselectNodesAndEdges: Q, addSelectedEdges: se } = j.getState();
      U.key === "Escape" ? (F.current?.blur(), Q({ edges: [m] })) : se([e]);
    }
  };
  return a.jsx("svg", { style: { zIndex: C }, children: a.jsxs("g", { className: Ne([
    "react-flow__edge",
    `react-flow__edge-${k}`,
    m.className,
    w,
    {
      selected: m.selected,
      animated: m.animated,
      inactive: !$ && !r,
      updating: _,
      selectable: $
    }
  ]), onClick: te, onDoubleClick: ae, onContextMenu: J, onMouseEnter: R, onMouseMove: q, onMouseLeave: re, onKeyDown: I ? oe : void 0, tabIndex: I ? 0 : void 0, role: m.ariaRole ?? (I ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": m.ariaLabel === null ? void 0 : m.ariaLabel || `Edge from ${m.source} to ${m.target}`, "aria-describedby": I ? `${Ac}-${g}` : void 0, ref: F, ...m.domAttributes, children: [!V && a.jsx(E, { id: e, source: m.source, target: m.target, type: m.type, selected: m.selected, animated: m.animated, selectable: $, deletable: m.deletable ?? !0, label: m.label, labelStyle: m.labelStyle, labelShowBg: m.labelShowBg, labelBgStyle: m.labelBgStyle, labelBgPadding: m.labelBgPadding, labelBgBorderRadius: m.labelBgBorderRadius, sourceX: A, sourceY: T, targetX: P, targetY: W, sourcePosition: O, targetPosition: B, data: m.data, style: m.style, sourceHandleId: m.sourceHandle, targetHandleId: m.targetHandle, markerStart: G, markerEnd: Z, pathOptions: "pathOptions" in m ? m.pathOptions : void 0, interactionWidth: m.interactionWidth }), D && a.jsx(rm, { edge: m, isReconnectable: D, reconnectRadius: f, onReconnect: d, onReconnectStart: h, onReconnectEnd: p, sourceX: A, sourceY: T, targetX: P, targetY: W, sourcePosition: O, targetPosition: B, setUpdateHover: z, setReconnecting: S })] }) });
}
var sm = ve(im);
const am = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function rl({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: i, onEdgeContextMenu: s, onEdgeMouseEnter: c, onEdgeMouseMove: u, onEdgeMouseLeave: l, onEdgeClick: f, reconnectRadius: d, onEdgeDoubleClick: h, onReconnectStart: p, onReconnectEnd: g, disableKeyboardA11y: x }) {
  const { edgesFocusable: w, edgesReconnectable: y, elementsSelectable: b, onError: m } = de(am, ye), v = Xg(t);
  return a.jsxs("div", { className: "react-flow__edges", children: [a.jsx(Ug, { defaultColor: e, rfId: n }), v.map((k) => a.jsx(sm, { id: k, edgesFocusable: w, edgesReconnectable: y, elementsSelectable: b, noPanClassName: r, onReconnect: i, onContextMenu: s, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, onClick: f, reconnectRadius: d, onDoubleClick: h, onReconnectStart: p, onReconnectEnd: g, rfId: n, onError: m, edgeTypes: o, disableKeyboardA11y: x }, k))] });
}
rl.displayName = "EdgeRenderer";
const cm = ve(rl), lm = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function um({ children: e }) {
  const t = de(lm);
  return a.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function dm(e) {
  const t = ji(), n = ce(!1);
  ie(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const fm = (e) => e.panZoom?.syncViewport;
function hm(e) {
  const t = de(fm), n = xe();
  return ie(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function pm(e) {
  return e.connection.inProgress ? { ...e.connection, to: Ut(e.connection.to, e.transform) } : { ...e.connection };
}
function gm(e) {
  return pm;
}
function mm(e) {
  const t = gm();
  return de(t, ye);
}
const ym = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function xm({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: i, height: s, isValid: c, inProgress: u } = de(ym, ye);
  return !(i && r && u) ? null : a.jsx("svg", { style: e, width: i, height: s, className: "react-flow__connectionline react-flow__container", children: a.jsx("g", { className: Ne(["react-flow__connection", ec(c)]), children: a.jsx(il, { style: t, type: n, CustomComponent: o, isValid: c }) }) });
}
const il = ({ style: e, type: t = dt.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: i, fromNode: s, fromHandle: c, fromPosition: u, to: l, toNode: f, toHandle: d, toPosition: h, pointer: p } = mm();
  if (!r)
    return;
  if (n)
    return a.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: s, fromHandle: c, fromX: i.x, fromY: i.y, toX: l.x, toY: l.y, fromPosition: u, toPosition: h, connectionStatus: ec(o), toNode: f, toHandle: d, pointer: p });
  let g = "";
  const x = {
    sourceX: i.x,
    sourceY: i.y,
    sourcePosition: u,
    targetX: l.x,
    targetY: l.y,
    targetPosition: h
  };
  switch (t) {
    case dt.Bezier:
      [g] = fc(x);
      break;
    case dt.SimpleBezier:
      [g] = Yc(x);
      break;
    case dt.Step:
      [g] = To({
        ...x,
        borderRadius: 0
      });
      break;
    case dt.SmoothStep:
      [g] = To(x);
      break;
    default:
      [g] = gc(x);
  }
  return a.jsx("path", { d: g, fill: "none", className: "react-flow__connection-path", style: e });
};
il.displayName = "ConnectionLine";
const wm = {};
function Zs(e = wm) {
  ce(e), xe(), ie(() => {
  }, [e]);
}
function vm() {
  xe(), ce(!1), ie(() => {
  }, []);
}
function sl({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: i, onEdgeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: f, onSelectionContextMenu: d, onSelectionStart: h, onSelectionEnd: p, connectionLineType: g, connectionLineStyle: x, connectionLineComponent: w, connectionLineContainerStyle: y, selectionKeyCode: b, selectionOnDrag: m, selectionMode: v, multiSelectionKeyCode: k, panActivationKeyCode: E, zoomActivationKeyCode: I, deleteKeyCode: D, onlyRenderVisibleElements: $, elementsSelectable: F, defaultViewport: _, translateExtent: z, minZoom: V, maxZoom: S, preventScrolling: j, defaultMarkerColor: C, zoomOnScroll: A, zoomOnPinch: T, panOnScroll: P, panOnScrollSpeed: W, panOnScrollMode: O, zoomOnDoubleClick: B, panOnDrag: G, autoPanOnSelection: Z, onPaneClick: te, onPaneMouseEnter: ae, onPaneMouseMove: J, onPaneMouseLeave: R, onPaneScroll: q, onPaneContextMenu: re, paneClickDistance: oe, nodeClickDistance: U, onEdgeContextMenu: Q, onEdgeMouseEnter: se, onEdgeMouseMove: L, onEdgeMouseLeave: K, reconnectRadius: fe, onReconnect: ue, onReconnectStart: je, onReconnectEnd: Oe, noDragClassName: Be, noWheelClassName: pt, noPanClassName: et, disableKeyboardA11y: Ee, nodeExtent: _e, rfId: $e, viewport: Te, onViewportChange: Ke }) {
  return Zs(e), Zs(t), vm(), dm(n), hm(Te), a.jsx(Tg, { onPaneClick: te, onPaneMouseEnter: ae, onPaneMouseMove: J, onPaneMouseLeave: R, onPaneContextMenu: re, onPaneScroll: q, paneClickDistance: oe, deleteKeyCode: D, selectionKeyCode: b, selectionOnDrag: m, selectionMode: v, onSelectionStart: h, onSelectionEnd: p, multiSelectionKeyCode: k, panActivationKeyCode: E, zoomActivationKeyCode: I, elementsSelectable: F, zoomOnScroll: A, zoomOnPinch: T, zoomOnDoubleClick: B, panOnScroll: P, panOnScrollSpeed: W, panOnScrollMode: O, panOnDrag: G, autoPanOnSelection: Z, defaultViewport: _, translateExtent: z, minZoom: V, maxZoom: S, onSelectionContextMenu: d, preventScrolling: j, noDragClassName: Be, noWheelClassName: pt, noPanClassName: et, disableKeyboardA11y: Ee, onViewportChange: Ke, isControlledViewport: !!Te, children: a.jsxs(um, { children: [a.jsx(cm, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: s, onReconnect: ue, onReconnectStart: je, onReconnectEnd: Oe, onlyRenderVisibleElements: $, onEdgeContextMenu: Q, onEdgeMouseEnter: se, onEdgeMouseMove: L, onEdgeMouseLeave: K, reconnectRadius: fe, defaultMarkerColor: C, noPanClassName: et, disableKeyboardA11y: Ee, rfId: $e }), a.jsx(xm, { style: x, type: g, component: w, containerStyle: y }), a.jsx("div", { className: "react-flow__edgelabel-renderer" }), a.jsx(Wg, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: i, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: f, nodeClickDistance: U, onlyRenderVisibleElements: $, noPanClassName: et, noDragClassName: Be, disableKeyboardA11y: Ee, nodeExtent: _e, rfId: $e }), a.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
sl.displayName = "GraphView";
const bm = ve(sl), Sm = ic(), Ks = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: c, minZoom: u = 0.5, maxZoom: l = 2, nodeOrigin: f, nodeExtent: d, zIndexMode: h = "basic" } = {}) => {
  const p = /* @__PURE__ */ new Map(), g = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), y = o ?? t ?? [], b = n ?? e ?? [], m = f ?? [0, 0], v = d ?? Dn;
  xc(x, w, y);
  const { nodesInitialized: k } = ni(b, p, g, {
    nodeOrigin: m,
    nodeExtent: v,
    zIndexMode: h
  });
  let E = [0, 0, 1];
  if (s && r && i) {
    const I = Bn(p, {
      filter: (_) => !!((_.width || _.initialWidth) && (_.height || _.initialHeight))
    }), { x: D, y: $, zoom: F } = bi(I, r, i, u, l, c?.padding ?? 0.1);
    E = [D, $, F];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: i ?? 0,
    transform: E,
    nodes: b,
    nodesInitialized: k,
    nodeLookup: p,
    parentLookup: g,
    edges: y,
    edgeLookup: w,
    connectionLookup: x,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: o !== void 0,
    panZoom: null,
    minZoom: u,
    maxZoom: l,
    translateExtent: Dn,
    nodeExtent: v,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: Ft.Strict,
    domNode: null,
    paneDragging: !1,
    noPanClassName: "nopan",
    nodeOrigin: m,
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
    connection: { ...Qa },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: Sm,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Ja,
    zIndexMode: h,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, Nm = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: c, minZoom: u, maxZoom: l, nodeOrigin: f, nodeExtent: d, zIndexMode: h }) => zp((p, g) => {
  async function x() {
    const { nodeLookup: w, panZoom: y, fitViewOptions: b, fitViewResolver: m, width: v, height: k, minZoom: E, maxZoom: I } = g();
    y && (await Dh({
      nodes: w,
      width: v,
      height: k,
      panZoom: y,
      minZoom: E,
      maxZoom: I
    }, b), m?.resolve(!0), p({ fitViewResolver: null }));
  }
  return {
    ...Ks({
      nodes: e,
      edges: t,
      width: r,
      height: i,
      fitView: s,
      fitViewOptions: c,
      minZoom: u,
      maxZoom: l,
      nodeOrigin: f,
      nodeExtent: d,
      defaultNodes: n,
      defaultEdges: o,
      zIndexMode: h
    }),
    setNodes: (w) => {
      const { nodeLookup: y, parentLookup: b, nodeOrigin: m, elevateNodesOnSelect: v, fitViewQueued: k, zIndexMode: E, nodesSelectionActive: I } = g(), { nodesInitialized: D, hasSelectedNodes: $ } = ni(w, y, b, {
        nodeOrigin: m,
        nodeExtent: d,
        elevateNodesOnSelect: v,
        checkEquality: !0,
        zIndexMode: E
      }), F = I && $;
      k && D ? (x(), p({
        nodes: w,
        nodesInitialized: D,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: F
      })) : p({ nodes: w, nodesInitialized: D, nodesSelectionActive: F });
    },
    setEdges: (w) => {
      const { connectionLookup: y, edgeLookup: b } = g();
      xc(y, b, w), p({ edges: w });
    },
    setDefaultNodesAndEdges: (w, y) => {
      if (w) {
        const { setNodes: b } = g();
        b(w), p({ hasDefaultNodes: !0 });
      }
      if (y) {
        const { setEdges: b } = g();
        b(y), p({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (w) => {
      const { triggerNodeChanges: y, nodeLookup: b, parentLookup: m, domNode: v, nodeOrigin: k, nodeExtent: E, debug: I, fitViewQueued: D, zIndexMode: $ } = g(), { changes: F, updatedInternals: _ } = tp(w, b, m, v, k, E, $);
      _ && (Gh(b, m, { nodeOrigin: k, nodeExtent: E, zIndexMode: $ }), D ? (x(), p({ fitViewQueued: !1, fitViewOptions: void 0 })) : p({}), F?.length > 0 && (I && console.log("React Flow: trigger node changes", F), y?.(F)));
    },
    updateNodePositions: (w, y = !1) => {
      const b = [];
      let m = [];
      const { nodeLookup: v, triggerNodeChanges: k, connection: E, updateConnection: I, onNodesChangeMiddlewareMap: D } = g();
      for (const [$, F] of w) {
        const _ = v.get($), z = !!(_?.expandParent && _?.parentId && F?.position), V = {
          id: $,
          type: "position",
          position: z ? {
            x: Math.max(0, F.position.x),
            y: Math.max(0, F.position.y)
          } : F.position,
          dragging: y
        };
        if (_ && E.inProgress && E.fromNode.id === _.id) {
          const S = jt(_, E.fromHandle, ee.Left, !0);
          I({ ...E, from: S });
        }
        z && _.parentId && b.push({
          id: $,
          parentId: _.parentId,
          rect: {
            ...F.internals.positionAbsolute,
            width: F.measured.width ?? 0,
            height: F.measured.height ?? 0
          }
        }), m.push(V);
      }
      if (b.length > 0) {
        const { parentLookup: $, nodeOrigin: F } = g(), _ = Ii(b, v, $, F);
        m.push(..._);
      }
      for (const $ of D.values())
        m = $(m);
      k(m);
    },
    triggerNodeChanges: (w) => {
      const { onNodesChange: y, setNodes: b, nodes: m, hasDefaultNodes: v, debug: k } = g();
      if (w?.length) {
        if (v) {
          const E = Pc(w, m);
          b(E);
        }
        k && console.log("React Flow: trigger node changes", w), y?.(w);
      }
    },
    triggerEdgeChanges: (w) => {
      const { onEdgesChange: y, setEdges: b, edges: m, hasDefaultEdges: v, debug: k } = g();
      if (w?.length) {
        if (v) {
          const E = $c(w, m);
          b(E);
        }
        k && console.log("React Flow: trigger edge changes", w), y?.(w);
      }
    },
    addSelectedNodes: (w) => {
      const { multiSelectionActive: y, edgeLookup: b, nodeLookup: m, triggerNodeChanges: v, triggerEdgeChanges: k } = g();
      if (y) {
        const E = w.map((I) => vt(I, !0));
        v(E);
        return;
      }
      v(Lt(m, /* @__PURE__ */ new Set([...w]), !0)), k(Lt(b));
    },
    addSelectedEdges: (w) => {
      const { multiSelectionActive: y, edgeLookup: b, nodeLookup: m, triggerNodeChanges: v, triggerEdgeChanges: k } = g();
      if (y) {
        const E = w.map((I) => vt(I, !0));
        k(E);
        return;
      }
      k(Lt(b, /* @__PURE__ */ new Set([...w]))), v(Lt(m, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: w, edges: y } = {}) => {
      const { edges: b, nodes: m, nodeLookup: v, triggerNodeChanges: k, triggerEdgeChanges: E } = g(), I = w || m, D = y || b, $ = [];
      for (const _ of I) {
        if (!_.selected)
          continue;
        const z = v.get(_.id);
        z && (z.selected = !1), $.push(vt(_.id, !1));
      }
      const F = [];
      for (const _ of D)
        _.selected && F.push(vt(_.id, !1));
      k($), E(F);
    },
    setMinZoom: (w) => {
      const { panZoom: y, maxZoom: b } = g();
      y?.setScaleExtent([w, b]), p({ minZoom: w });
    },
    setMaxZoom: (w) => {
      const { panZoom: y, minZoom: b } = g();
      y?.setScaleExtent([b, w]), p({ maxZoom: w });
    },
    setTranslateExtent: (w) => {
      g().panZoom?.setTranslateExtent(w), p({ translateExtent: w });
    },
    resetSelectedElements: () => {
      const { edges: w, nodes: y, triggerNodeChanges: b, triggerEdgeChanges: m, elementsSelectable: v } = g();
      if (!v)
        return;
      const k = y.reduce((I, D) => D.selected ? [...I, vt(D.id, !1)] : I, []), E = w.reduce((I, D) => D.selected ? [...I, vt(D.id, !1)] : I, []);
      b(k), m(E);
    },
    setNodeExtent: (w) => {
      const { nodes: y, nodeLookup: b, parentLookup: m, nodeOrigin: v, elevateNodesOnSelect: k, nodeExtent: E, zIndexMode: I } = g();
      w[0][0] === E[0][0] && w[0][1] === E[0][1] && w[1][0] === E[1][0] && w[1][1] === E[1][1] || (ni(y, b, m, {
        nodeOrigin: v,
        nodeExtent: w,
        elevateNodesOnSelect: k,
        checkEquality: !1,
        zIndexMode: I
      }), p({ nodeExtent: w }));
    },
    panBy: (w) => {
      const { transform: y, width: b, height: m, panZoom: v, translateExtent: k } = g();
      return np({ delta: w, panZoom: v, transform: y, translateExtent: k, width: b, height: m });
    },
    setCenter: async (w, y, b) => {
      const { width: m, height: v, maxZoom: k, panZoom: E } = g();
      if (!E)
        return !1;
      const I = typeof b?.zoom < "u" ? b.zoom : k;
      return await E.setViewport({
        x: m / 2 - w * I,
        y: v / 2 - y * I,
        zoom: I
      }, { duration: b?.duration, ease: b?.ease, interpolate: b?.interpolate }), !0;
    },
    cancelConnection: () => {
      p({
        connection: { ...Qa }
      });
    },
    updateConnection: (w) => {
      p({ connection: w });
    },
    reset: () => p({ ...Ks() })
  };
}, Object.is);
function Em({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: i, initialMinZoom: s, initialMaxZoom: c, initialFitViewOptions: u, fitView: l, nodeOrigin: f, nodeExtent: d, zIndexMode: h, children: p }) {
  const [g] = Y(() => Nm({
    nodes: e,
    edges: t,
    defaultNodes: n,
    defaultEdges: o,
    width: r,
    height: i,
    fitView: l,
    minZoom: s,
    maxZoom: c,
    fitViewOptions: u,
    nodeOrigin: f,
    nodeExtent: d,
    zIndexMode: h
  }));
  return a.jsx(Hp, { value: g, children: a.jsx(ug, { children: p }) });
}
function km({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: i, height: s, fitView: c, fitViewOptions: u, minZoom: l, maxZoom: f, nodeOrigin: d, nodeExtent: h, zIndexMode: p }) {
  return Ln(Uo) ? a.jsx(a.Fragment, { children: e }) : a.jsx(Em, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: i, initialHeight: s, fitView: c, initialFitViewOptions: u, initialMinZoom: l, initialMaxZoom: f, nodeOrigin: d, nodeExtent: h, zIndexMode: p, children: e });
}
const Cm = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function Im({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: i, edgeTypes: s, onNodeClick: c, onEdgeClick: u, onInit: l, onMove: f, onMoveStart: d, onMoveEnd: h, onConnect: p, onConnectStart: g, onConnectEnd: x, onClickConnectStart: w, onClickConnectEnd: y, onNodeMouseEnter: b, onNodeMouseMove: m, onNodeMouseLeave: v, onNodeContextMenu: k, onNodeDoubleClick: E, onNodeDragStart: I, onNodeDrag: D, onNodeDragStop: $, onNodesDelete: F, onEdgesDelete: _, onDelete: z, onSelectionChange: V, onSelectionDragStart: S, onSelectionDrag: j, onSelectionDragStop: C, onSelectionContextMenu: A, onSelectionStart: T, onSelectionEnd: P, onBeforeDelete: W, connectionMode: O, connectionLineType: B = dt.Bezier, connectionLineStyle: G, connectionLineComponent: Z, connectionLineContainerStyle: te, deleteKeyCode: ae = "Backspace", selectionKeyCode: J = "Shift", selectionOnDrag: R = !1, selectionMode: q = Pn.Full, panActivationKeyCode: re = "Space", multiSelectionKeyCode: oe = Tn() ? "Meta" : "Control", zoomActivationKeyCode: U = Tn() ? "Meta" : "Control", snapToGrid: Q, snapGrid: se, onlyRenderVisibleElements: L = !1, selectNodesOnDrag: K, nodesDraggable: fe, autoPanOnNodeFocus: ue, nodesConnectable: je, nodesFocusable: Oe, nodeOrigin: Be = Mc, edgesFocusable: pt, edgesReconnectable: et, elementsSelectable: Ee = !0, defaultViewport: _e = Qp, minZoom: $e = 0.5, maxZoom: Te = 2, translateExtent: Ke = Dn, preventScrolling: Mt = !0, nodeExtent: tt, defaultMarkerColor: gt = "#b1b1b7", zoomOnScroll: mt = !0, zoomOnPinch: Zn = !0, panOnScroll: ze = !1, panOnScrollSpeed: Kn = 0.5, panOnScrollMode: we = Nt.Free, zoomOnDoubleClick: Fe = !0, panOnDrag: Gt = !0, onPaneClick: ke, onPaneMouseEnter: Jt, onPaneMouseMove: Qt, onPaneMouseLeave: Ie, onPaneScroll: yt, onPaneContextMenu: ct, paneClickDistance: nr = 1, nodeClickDistance: Un = 0, children: Gn, onReconnect: en, onReconnectStart: or, onReconnectEnd: tn, onEdgeContextMenu: Dt, onEdgeDoubleClick: Re, onEdgeMouseEnter: nn, onEdgeMouseMove: on, onEdgeMouseLeave: rn, reconnectRadius: Pt = 10, onNodesChange: rr, onEdgesChange: ir, noDragClassName: sr = "nodrag", noWheelClassName: ar = "nowheel", noPanClassName: sn = "nopan", fitView: an, fitViewOptions: cn, connectOnClick: cr, attributionPosition: ln, proOptions: lr, defaultEdgeOptions: ur, elevateNodesOnSelect: dr = !0, elevateEdgesOnSelect: fr = !1, disableKeyboardA11y: Jn = !1, autoPanOnConnect: un, autoPanOnNodeDrag: hr, autoPanOnSelection: pr = !0, autoPanSpeed: gr, connectionRadius: mr, isValidConnection: yr, onError: xr, style: wr, id: dn, nodeDragThreshold: Qn, connectionDragThreshold: vr, viewport: br, onViewportChange: Sr, width: Nr, height: Er, colorMode: eo = "light", debug: to, onScroll: fn, ariaLabelConfig: no, zIndexMode: hn = "basic", ...kr }, Cr) {
  const $t = dn || "1", oo = og(eo), ro = ge((io) => {
    io.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), fn?.(io);
  }, [fn]);
  return a.jsx("div", { "data-testid": "rf__wrapper", ...kr, onScroll: ro, style: { ...wr, ...Cm }, ref: Cr, className: Ne(["react-flow", r, oo]), id: dn, role: "application", children: a.jsxs(km, { nodes: e, edges: t, width: Nr, height: Er, fitView: an, fitViewOptions: cn, minZoom: $e, maxZoom: Te, nodeOrigin: Be, nodeExtent: tt, zIndexMode: hn, children: [a.jsx(ng, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: p, onConnectStart: g, onConnectEnd: x, onClickConnectStart: w, onClickConnectEnd: y, nodesDraggable: fe, autoPanOnNodeFocus: ue, nodesConnectable: je, nodesFocusable: Oe, edgesFocusable: pt, edgesReconnectable: et, elementsSelectable: Ee, elevateNodesOnSelect: dr, elevateEdgesOnSelect: fr, minZoom: $e, maxZoom: Te, nodeExtent: tt, onNodesChange: rr, onEdgesChange: ir, snapToGrid: Q, snapGrid: se, connectionMode: O, translateExtent: Ke, connectOnClick: cr, defaultEdgeOptions: ur, fitView: an, fitViewOptions: cn, onNodesDelete: F, onEdgesDelete: _, onDelete: z, onNodeDragStart: I, onNodeDrag: D, onNodeDragStop: $, onSelectionDrag: j, onSelectionDragStart: S, onSelectionDragStop: C, onMove: f, onMoveStart: d, onMoveEnd: h, noPanClassName: sn, nodeOrigin: Be, rfId: $t, autoPanOnConnect: un, autoPanOnNodeDrag: hr, autoPanSpeed: gr, onError: xr, connectionRadius: mr, isValidConnection: yr, selectNodesOnDrag: K, nodeDragThreshold: Qn, connectionDragThreshold: vr, onBeforeDelete: W, debug: to, ariaLabelConfig: no, zIndexMode: hn }), a.jsx(bm, { onInit: l, onNodeClick: c, onEdgeClick: u, onNodeMouseEnter: b, onNodeMouseMove: m, onNodeMouseLeave: v, onNodeContextMenu: k, onNodeDoubleClick: E, nodeTypes: i, edgeTypes: s, connectionLineType: B, connectionLineStyle: G, connectionLineComponent: Z, connectionLineContainerStyle: te, selectionKeyCode: J, selectionOnDrag: R, selectionMode: q, deleteKeyCode: ae, multiSelectionKeyCode: oe, panActivationKeyCode: re, zoomActivationKeyCode: U, onlyRenderVisibleElements: L, defaultViewport: _e, translateExtent: Ke, minZoom: $e, maxZoom: Te, preventScrolling: Mt, zoomOnScroll: mt, zoomOnPinch: Zn, zoomOnDoubleClick: Fe, panOnScroll: ze, panOnScrollSpeed: Kn, panOnScrollMode: we, panOnDrag: Gt, autoPanOnSelection: pr, onPaneClick: ke, onPaneMouseEnter: Jt, onPaneMouseMove: Qt, onPaneMouseLeave: Ie, onPaneScroll: yt, onPaneContextMenu: ct, paneClickDistance: nr, nodeClickDistance: Un, onSelectionContextMenu: A, onSelectionStart: T, onSelectionEnd: P, onReconnect: en, onReconnectStart: or, onReconnectEnd: tn, onEdgeContextMenu: Dt, onEdgeDoubleClick: Re, onEdgeMouseEnter: nn, onEdgeMouseMove: on, onEdgeMouseLeave: rn, reconnectRadius: Pt, defaultMarkerColor: gt, noDragClassName: sr, noWheelClassName: ar, noPanClassName: sn, rfId: $t, disableKeyboardA11y: Jn, nodeExtent: tt, viewport: br, onViewportChange: Sr }), a.jsx(Jp, { onSelectionChange: V }), Gn, a.jsx(qp, { proOptions: lr, position: ln }), a.jsx(Yp, { rfId: $t, disableKeyboardA11y: Jn })] }) });
}
var al = Rc(Im);
const jm = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function _m({ children: e }) {
  const t = de(jm);
  return t ? Vp.createPortal(e, t) : null;
}
function Am({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return a.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ne(["react-flow__background-pattern", n, o]) });
}
function Mm({ radius: e, className: t }) {
  return a.jsx("circle", { cx: e, cy: e, r: e, className: Ne(["react-flow__background-pattern", "dots", t]) });
}
var ft;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(ft || (ft = {}));
const Dm = {
  [ft.Dots]: 1,
  [ft.Lines]: 1,
  [ft.Cross]: 6
}, Pm = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function cl({
  id: e,
  variant: t = ft.Dots,
  // only used for dots and cross
  gap: n = 20,
  // only used for lines and cross
  size: o,
  lineWidth: r = 1,
  offset: i = 0,
  color: s,
  bgColor: c,
  style: u,
  className: l,
  patternClassName: f
}) {
  const d = ce(null), { transform: h, patternId: p } = de(Pm, ye), g = o || Dm[t], x = t === ft.Dots, w = t === ft.Cross, y = Array.isArray(n) ? n : [n, n], b = [y[0] * h[2] || 1, y[1] * h[2] || 1], m = g * h[2], v = Array.isArray(i) ? i : [i, i], k = w ? [m, m] : b, E = [
    v[0] * h[2] || 1 + k[0] / 2,
    v[1] * h[2] || 1 + k[1] / 2
  ], I = `${p}${e || ""}`;
  return a.jsxs("svg", { className: Ne(["react-flow__background", l]), style: {
    ...u,
    ...Jo,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": s
  }, ref: d, "data-testid": "rf__background", children: [a.jsx("pattern", { id: I, x: h[0] % b[0], y: h[1] % b[1], width: b[0], height: b[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${E[0]},-${E[1]})`, children: x ? a.jsx(Mm, { radius: m / 2, className: f }) : a.jsx(Am, { dimensions: k, lineWidth: r, variant: t, className: f }) }), a.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${I})` })] });
}
cl.displayName = "Background";
const ll = ve(cl);
function $m() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: a.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function Tm() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: a.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function zm() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: a.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function Rm() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: a.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function Lm() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: a.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function go({ children: e, className: t, ...n }) {
  return a.jsx("button", { type: "button", className: Ne(["react-flow__controls-button", t]), ...n, children: e });
}
const Vm = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function ul({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: i, onZoomOut: s, onFitView: c, onInteractiveChange: u, className: l, children: f, position: d = "bottom-left", orientation: h = "vertical", "aria-label": p }) {
  const g = xe(), { isInteractive: x, minZoomReached: w, maxZoomReached: y, ariaLabelConfig: b } = de(Vm, ye), { zoomIn: m, zoomOut: v, fitView: k } = ji(), E = () => {
    m(), i?.();
  }, I = () => {
    v(), s?.();
  }, D = () => {
    k(r), c?.();
  }, $ = () => {
    g.setState({
      nodesDraggable: !x,
      nodesConnectable: !x,
      elementsSelectable: !x
    }), u?.(!x);
  }, F = h === "horizontal" ? "horizontal" : "vertical";
  return a.jsxs(Go, { className: Ne(["react-flow__controls", F, l]), position: d, style: e, "data-testid": "rf__controls", "aria-label": p ?? b["controls.ariaLabel"], children: [t && a.jsxs(a.Fragment, { children: [a.jsx(go, { onClick: E, className: "react-flow__controls-zoomin", title: b["controls.zoomIn.ariaLabel"], "aria-label": b["controls.zoomIn.ariaLabel"], disabled: y, children: a.jsx($m, {}) }), a.jsx(go, { onClick: I, className: "react-flow__controls-zoomout", title: b["controls.zoomOut.ariaLabel"], "aria-label": b["controls.zoomOut.ariaLabel"], disabled: w, children: a.jsx(Tm, {}) })] }), n && a.jsx(go, { className: "react-flow__controls-fitview", onClick: D, title: b["controls.fitView.ariaLabel"], "aria-label": b["controls.fitView.ariaLabel"], children: a.jsx(zm, {}) }), o && a.jsx(go, { className: "react-flow__controls-interactive", onClick: $, title: b["controls.interactive.ariaLabel"], "aria-label": b["controls.interactive.ariaLabel"], children: x ? a.jsx(Lm, {}) : a.jsx(Rm, {}) }), f] });
}
ul.displayName = "Controls";
const dl = ve(ul);
function Hm({ id: e, x: t, y: n, width: o, height: r, style: i, color: s, strokeColor: c, strokeWidth: u, className: l, borderRadius: f, shapeRendering: d, selected: h, onClick: p }) {
  const { background: g, backgroundColor: x } = i || {}, w = s || g || x;
  return a.jsx("rect", { className: Ne(["react-flow__minimap-node", { selected: h }, l]), x: t, y: n, rx: f, ry: f, width: o, height: r, style: {
    fill: w,
    stroke: c,
    strokeWidth: u
  }, shapeRendering: d, onClick: p ? (y) => p(y, e) : void 0 });
}
const Om = ve(Hm), Bm = (e) => e.nodes.map((t) => t.id), Fr = (e) => e instanceof Function ? e : () => e;
function Fm({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: i = Om,
  onClick: s
}) {
  const c = de(Bm, ye), u = Fr(t), l = Fr(e), f = Fr(n), d = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return a.jsx(a.Fragment, { children: c.map((h) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    a.jsx(Xm, { id: h, nodeColorFunc: u, nodeStrokeColorFunc: l, nodeClassNameFunc: f, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: i, onClick: s, shapeRendering: d }, h)
  )) });
}
function Wm({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: i, shapeRendering: s, NodeComponent: c, onClick: u }) {
  const { node: l, x: f, y: d, width: h, height: p } = de((g) => {
    const x = g.nodeLookup.get(e);
    if (!x)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const w = x.internals.userNode, { x: y, y: b } = x.internals.positionAbsolute, { width: m, height: v } = at(w);
    return {
      node: w,
      x: y,
      y: b,
      width: m,
      height: v
    };
  }, ye);
  return !l || l.hidden || !sc(l) ? null : a.jsx(c, { x: f, y: d, width: h, height: p, style: l.style, selected: !!l.selected, className: o(l), color: t(l), borderRadius: r, strokeColor: n(l), strokeWidth: i, shapeRendering: s, onClick: u, id: l.id });
}
const Xm = ve(Wm);
var Ym = ve(Fm);
const qm = 200, Zm = 150, Km = (e) => !e.hidden, Um = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? rc(Bn(e.nodeLookup, { filter: Km }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, Gm = "react-flow__minimap-desc";
function fl({
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
  bgColor: u,
  maskColor: l,
  maskStrokeColor: f,
  maskStrokeWidth: d,
  position: h = "bottom-right",
  onClick: p,
  onNodeClick: g,
  pannable: x = !1,
  zoomable: w = !1,
  ariaLabel: y,
  inversePan: b,
  zoomStep: m = 1,
  offsetScale: v = 5
}) {
  const k = xe(), E = ce(null), { boundingRect: I, viewBB: D, rfId: $, panZoom: F, translateExtent: _, flowWidth: z, flowHeight: V, ariaLabelConfig: S } = de(Um, ye), j = e?.width ?? qm, C = e?.height ?? Zm, A = I.width / j, T = I.height / C, P = Math.max(A, T), W = P * j, O = P * C, B = v * P, G = I.x - (W - I.width) / 2 - B, Z = I.y - (O - I.height) / 2 - B, te = W + B * 2, ae = O + B * 2, J = `${Gm}-${$}`, R = ce(0), q = ce();
  R.current = P, ie(() => {
    if (E.current && F)
      return q.current = dp({
        domNode: E.current,
        panZoom: F,
        getTransform: () => k.getState().transform,
        getViewScale: () => R.current
      }), () => {
        q.current?.destroy();
      };
  }, [F]), ie(() => {
    q.current?.update({
      translateExtent: _,
      width: z,
      height: V,
      inversePan: b,
      pannable: x,
      zoomStep: m,
      zoomable: w
    });
  }, [x, w, b, m, _, z, V]);
  const re = p ? (Q) => {
    const [se, L] = q.current?.pointer(Q) || [0, 0];
    p(Q, { x: se, y: L });
  } : void 0, oe = g ? ge((Q, se) => {
    const L = k.getState().nodeLookup.get(se).internals.userNode;
    g(Q, L);
  }, []) : void 0, U = y ?? S["minimap.ariaLabel"];
  return a.jsx(Go, { position: h, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof f == "string" ? f : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof d == "number" ? d * P : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof s == "number" ? s : void 0
  }, className: Ne(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: a.jsxs("svg", { width: j, height: C, viewBox: `${G} ${Z} ${te} ${ae}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": J, ref: E, onClick: re, children: [U && a.jsx("title", { id: J, children: U }), a.jsx(Ym, { onClick: oe, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: i, nodeClassName: r, nodeStrokeWidth: s, nodeComponent: c }), a.jsx("path", { className: "react-flow__minimap-mask", d: `M${G - B},${Z - B}h${te + B * 2}v${ae + B * 2}h${-te - B * 2}z
        M${D.x},${D.y}h${D.width}v${D.height}h${-D.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
fl.displayName = "MiniMap";
const hl = ve(fl), Jm = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, Qm = {
  [qt.Line]: "right",
  [qt.Handle]: "bottom-right"
};
function ey({ nodeId: e, position: t, variant: n = qt.Handle, className: o, style: r = void 0, children: i, color: s, minWidth: c = 10, minHeight: u = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: f = Number.MAX_VALUE, keepAspectRatio: d = !1, resizeDirection: h, autoScale: p = !0, shouldResize: g, onResizeStart: x, onResize: w, onResizeEnd: y }) {
  const b = Oc(), m = typeof e == "string" ? e : b, v = xe(), k = ce(null), E = n === qt.Handle, I = de(ge(Jm(E && p), [E, p]), ye), D = ce(null), $ = t ?? Qm[n];
  ie(() => {
    if (!(!k.current || !m))
      return D.current || (D.current = Ep({
        domNode: k.current,
        nodeId: m,
        getStoreItems: () => {
          const { nodeLookup: _, transform: z, snapGrid: V, snapToGrid: S, nodeOrigin: j, domNode: C } = v.getState();
          return {
            nodeLookup: _,
            transform: z,
            snapGrid: V,
            snapToGrid: S,
            nodeOrigin: j,
            paneDomNode: C
          };
        },
        onChange: (_, z) => {
          const { triggerNodeChanges: V, nodeLookup: S, parentLookup: j, nodeOrigin: C } = v.getState(), A = [], T = { x: _.x, y: _.y }, P = S.get(m);
          if (P && P.expandParent && P.parentId) {
            const W = P.origin ?? C, O = _.width ?? P.measured.width ?? 0, B = _.height ?? P.measured.height ?? 0, G = {
              id: P.id,
              parentId: P.parentId,
              rect: {
                width: O,
                height: B,
                ...ac({
                  x: _.x ?? P.position.x,
                  y: _.y ?? P.position.y
                }, { width: O, height: B }, P.parentId, S, W)
              }
            }, Z = Ii([G], S, j, C);
            A.push(...Z), T.x = _.x ? Math.max(W[0] * O, _.x) : void 0, T.y = _.y ? Math.max(W[1] * B, _.y) : void 0;
          }
          if (T.x !== void 0 && T.y !== void 0) {
            const W = {
              id: m,
              type: "position",
              position: { ...T }
            };
            A.push(W);
          }
          if (_.width !== void 0 && _.height !== void 0) {
            const O = {
              id: m,
              type: "dimensions",
              resizing: !0,
              setAttributes: h ? h === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: _.width,
                height: _.height
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
          V(A);
        },
        onEnd: ({ width: _, height: z }) => {
          const V = {
            id: m,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: _,
              height: z
            }
          };
          v.getState().triggerNodeChanges([V]);
        }
      })), D.current.update({
        controlPosition: $,
        boundaries: {
          minWidth: c,
          minHeight: u,
          maxWidth: l,
          maxHeight: f
        },
        keepAspectRatio: d,
        resizeDirection: h,
        onResizeStart: x,
        onResize: w,
        onResizeEnd: y,
        shouldResize: g
      }), () => {
        D.current?.destroy();
      };
  }, [
    $,
    c,
    u,
    l,
    f,
    d,
    x,
    w,
    y,
    g
  ]);
  const F = $.split("-");
  return a.jsx("div", { className: Ne(["react-flow__resize-control", "nodrag", ...F, n, o]), ref: k, style: {
    ...r,
    scale: I,
    ...s && { [E ? "backgroundColor" : "borderColor"]: s }
  }, children: i });
}
ve(ey);
const ty = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), pl = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var ny = {
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
const oy = Oo(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: o,
    className: r = "",
    children: i,
    iconNode: s,
    ...c
  }, u) => Xr(
    "svg",
    {
      ref: u,
      ...ny,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: pl("lucide", r),
      ...c
    },
    [
      ...s.map(([l, f]) => Xr(l, f)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const be = (e, t) => {
  const n = Oo(
    ({ className: o, ...r }, i) => Xr(oy, {
      ref: i,
      iconNode: t,
      className: pl(`lucide-${ty(e)}`, o),
      ...r
    })
  );
  return n.displayName = `${e}`, n;
};
const gl = be("Boxes", [
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
const Xn = be("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const ry = be("ChevronDown", [
  ["path", { d: "m6 9 6 6 6-6", key: "qrunsl" }]
]);
const Ro = be("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const zt = be("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const st = be("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const ml = be("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const iy = be("GripVertical", [
  ["circle", { cx: "9", cy: "12", r: "1", key: "1vctgf" }],
  ["circle", { cx: "9", cy: "5", r: "1", key: "hp0tcf" }],
  ["circle", { cx: "9", cy: "19", r: "1", key: "fkjjf6" }],
  ["circle", { cx: "15", cy: "12", r: "1", key: "1tmaij" }],
  ["circle", { cx: "15", cy: "5", r: "1", key: "19l28e" }],
  ["circle", { cx: "15", cy: "19", r: "1", key: "f4zoj3" }]
]);
const yl = be("ListTree", [
  ["path", { d: "M21 12h-8", key: "1bmf0i" }],
  ["path", { d: "M21 6H8", key: "1pqkrb" }],
  ["path", { d: "M21 18h-8", key: "1tm79t" }],
  ["path", { d: "M3 6v4c0 1.1.9 2 2 2h3", key: "1ywdgy" }],
  ["path", { d: "M3 10v6c0 1.1.9 2 2 2h3", key: "2wc746" }]
]);
const Lo = be("Maximize2", [
  ["polyline", { points: "15 3 21 3 21 9", key: "mznyad" }],
  ["polyline", { points: "9 21 3 21 3 15", key: "1avn1i" }],
  ["line", { x1: "21", x2: "14", y1: "3", y2: "10", key: "ota7mn" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const Us = be("Minimize2", [
  ["polyline", { points: "4 14 10 14 10 20", key: "11kfnr" }],
  ["polyline", { points: "20 10 14 10 14 4", key: "rlmsce" }],
  ["line", { x1: "14", x2: "21", y1: "10", y2: "3", key: "o5lafz" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const Ai = be("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const Mi = be("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const xl = be("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const sy = be("Save", [
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
const ay = be("Search", [
  ["circle", { cx: "11", cy: "11", r: "8", key: "4ej97u" }],
  ["path", { d: "m21 21-4.3-4.3", key: "1qie3q" }]
]);
const _t = be("Sparkles", [
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
const cy = be("Terminal", [
  ["polyline", { points: "4 17 10 11 4 5", key: "akl6gq" }],
  ["line", { x1: "12", x2: "20", y1: "19", y2: "19", key: "q2wloq" }]
]);
const ii = be("Trash2", [
  ["path", { d: "M3 6h18", key: "d0wm0j" }],
  ["path", { d: "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", key: "4alrt4" }],
  ["path", { d: "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", key: "v07s0e" }],
  ["line", { x1: "10", x2: "10", y1: "11", y2: "17", key: "1uufr5" }],
  ["line", { x1: "14", x2: "14", y1: "11", y2: "17", key: "xtxkd" }]
]);
const ly = be("X", [
  ["path", { d: "M18 6 6 18", key: "1bl5f8" }],
  ["path", { d: "m6 6 12 12", key: "d8bk6v" }]
]);
const uy = be("Zap", [
  [
    "path",
    {
      d: "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z",
      key: "1xq2db"
    }
  ]
]), Pe = "/_elsa/workflow-management", dy = "/_elsa/publishing";
async function fy(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${Pe}/definitions?${n.toString()}`);
}
async function hy(e, t) {
  return e.http.getJson(`${Pe}/definitions/${encodeURIComponent(t)}`);
}
async function py(e, t) {
  return e.http.getJson(`${Pe}/versions/${encodeURIComponent(t)}`);
}
async function gy(e, t) {
  return e.http.postJson(`${Pe}/definitions`, t);
}
async function my(e, t) {
  await e.http.deleteJson(`${Pe}/definitions/${encodeURIComponent(t)}`);
}
async function yy(e, t) {
  await e.http.postJson(`${Pe}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function xy(e, t) {
  await e.http.deleteJson(`${Pe}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function wy(e, t) {
  return e.http.putJson(`${Pe}/drafts/${encodeURIComponent(t.id)}`, { state: t.state, layout: t.layout });
}
async function vy(e, t) {
  return e.http.postJson(`${Pe}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function by(e, t) {
  return e.http.postJson(`${Pe}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function Sy(e, t) {
  try {
    return await e.http.postJson(`${dy}/workflows/drafts/test-runs`, t);
  } catch (n) {
    const o = _y(n);
    if (o) return o;
    throw n;
  }
}
async function Ny(e, t) {
  return e.http.postJson(`${Pe}/executables/${encodeURIComponent(t)}/run`, {});
}
async function Ey(e) {
  return e.http.getJson("/_demo/workflows/executables");
}
async function ky(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const o = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${o ? `?${o}` : ""}`);
}
async function Cy(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function Di(e) {
  return e.http.getJson(`${Pe}/activities`);
}
async function Iy(e) {
  const t = await wl(e, [
    `${Pe}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? Gs(t) : Gs(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function jy(e) {
  const t = await wl(e, [
    `${Pe}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : No;
}
async function wl(e, t) {
  let n;
  for (const o of t)
    try {
      return await e.http.getJson(o);
    } catch (r) {
      n = r;
    }
  throw n;
}
function Gs(e) {
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
function _y(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = Js(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return Js(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function Js(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const No = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
], Qo = "elsa.sequence.structure", Yn = "elsa.flowchart.structure";
function vl(e, t) {
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
function In(e, t) {
  const n = vl(e, t);
  if (!n) return null;
  let o = Je(n)[0];
  return o ? { owner: n, slot: o } : null;
}
function Je(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = Ky(t), r = Wr(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: Uy(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, i]) => Wr(i)).map(([i, s]) => ({
    id: `${t.kind}:${i}`,
    label: Jy(i),
    property: i,
    mode: "generic",
    activities: Wr(s) ?? []
  }));
}
function bl(e, t, n) {
  const o = new Map(t.map((s) => [s.activityVersionId, s])), r = new Map(n.map((s) => [s.nodeId, s])), i = e.slot.activities.map((s, c) => {
    const u = o.get(s.activityVersionId), l = r.get(s.nodeId) ?? Gy(e.slot.mode, c);
    return El(s, u, { x: l.x, y: l.y });
  });
  return {
    nodes: i,
    edges: e.slot.mode === "flowchart" ? Oy(e.owner) : Hy(e.slot, i)
  };
}
function si(e, t, n) {
  const o = t.find((i) => i.activityVersionId === e.activityVersionId), r = n.find((i) => i.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [El(e, o, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function Ay(e, t, n, o = null) {
  const r = new Map(t.map((c) => [c.activityExecutionId, c])), i = ta(t, (c) => c.authoredActivityId || c.executableNodeId), s = ta(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? r.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const u = i.get(c.id) ?? [], l = s.get(c.id) ?? [];
    if (u.length === 0 && l.length === 0) return c;
    const f = Yy(u), d = o === c.id || u.some((p) => p.activityExecutionId === o) || l.some((p) => p.incidentId === o), h = {
      status: f?.status,
      subStatus: f?.subStatus,
      activityExecutionId: f?.activityExecutionId,
      faultCount: u.reduce((p, g) => p + g.faultCount + g.aggregateFaultCount, 0),
      incidentCount: l.length,
      hasBlockingIncident: l.some((p) => p.isBlocking),
      selected: d
    };
    return {
      ...c,
      selected: d,
      className: d ? "wf-runtime-node-selected" : c.className,
      data: {
        ...c.data,
        runtime: h
      }
    };
  });
}
function Pi(e, t) {
  return e?.structure?.kind === Yn || Ty(t) ? "flowchart" : e?.structure?.kind === Qo || zy(t) ? "sequence" : "unsupported";
}
function ai(e, t, n) {
  if (t.length === 0) {
    const c = Je(e)[0];
    return c ? Rn(e, c, n) : e;
  }
  const [o, ...r] = t, i = Je(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((c) => c.nodeId === o.ownerNodeId ? ai(c, r, n) : c);
  return Rn(e, i, s);
}
function Sl(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, i = Je(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((c) => c.nodeId === o.ownerNodeId ? Sl(c, r, n) : c);
  return Rn(e, i, s);
}
function Nl(e, t, n) {
  if (e.nodeId === t) return n(e);
  const o = Je(e);
  if (o.length === 0) return e;
  let r = !1, i = e;
  for (const s of o) {
    const c = s.activities.map((u) => {
      const l = Nl(u, t, n);
      return l !== u && (r = !0), l;
    });
    r && (i = Rn(i, s, c));
  }
  return r ? i : e;
}
function Rn(e, t, n) {
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
function My(e, t, n, o = []) {
  const r = new Map(e.slot.activities.map((s) => [s.nodeId, s]));
  for (const s of o)
    r.set(s.nodeId, s);
  const i = t.map((s) => r.get(s.id)).filter((s) => !!s);
  return e.slot.mode === "sequence" && i.sort((s, c) => {
    const u = t.find((f) => f.id === s.nodeId), l = t.find((f) => f.id === c.nodeId);
    return (u?.position.x ?? 0) - (l?.position.x ?? 0);
  }), Rn(e.owner, e.slot, i);
}
function Dy(e, t) {
  return {
    ...e,
    structure: Vy(e.structure, t)
  };
}
function Py(e, t) {
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
function Qs(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: Ly(e)
  };
}
function Ce(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? Ry(t) : n;
}
function El(e, t, n, o = {}) {
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
      icon: ci(t),
      childSlots: Je(e),
      acceptsInbound: By(e, t),
      sourcePorts: o.suppressFlowPorts ? [] : kl(e, t),
      suppressFlowPorts: o.suppressFlowPorts
    }
  };
}
function ci(e) {
  if (!e) return "activity";
  const t = $y(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), o = Ce(e).toLowerCase(), r = e.category?.toLowerCase() ?? "", i = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || o === "flowchart" ? "flowchart" : n.endsWith(".sequence") || o === "sequence" ? "sequence" : n.includes("writeline") || o.includes("write line") ? "terminal" : r.includes("runtime") ? "runtime" : i === "trigger" ? "trigger" : "activity";
}
function $y(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function Ty(e) {
  return !!e && (Ce(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function zy(e) {
  return !!e && (Ce(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function Ry(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function Ly(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: Qo,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: Yn,
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
function Vy(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], o = /* @__PURE__ */ new Map();
  for (const r of n) {
    if (!$i(r)) continue;
    const i = r.id;
    typeof i == "string" && o.set(i, r);
  }
  return {
    ...e,
    payload: {
      ...e.payload,
      connections: t.map((r) => {
        const i = o.get(r.id) ?? {}, s = r.data?.vertices, { vertices: c, ...u } = i;
        return {
          ...u,
          id: r.id,
          source: { nodeId: r.source, port: r.sourceHandle ?? "Done" },
          target: r.targetHandle ? { nodeId: r.target, port: r.targetHandle } : { nodeId: r.target },
          ...s?.length ? { vertices: s.map((l) => ({ x: Math.round(l.x), y: Math.round(l.y) })) } : {}
        };
      })
    }
  };
}
function Hy(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function Oy(e) {
  if (e.structure?.kind !== Yn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, i = n.target;
    if (!r?.nodeId || !i?.nodeId) return null;
    const s = Array.isArray(n.vertices) ? n.vertices.filter(qy) : [];
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
function kl(e, t) {
  const n = ea(e.cases);
  if (Wy(e, t) && n.length > 0)
    return [...n.map((i) => ({ name: i, displayName: i })), { name: "Default", displayName: "Default" }];
  const o = [
    ...Eo(t?.designFacets),
    ...Eo(t?.ports),
    ...Eo(t?.outputs)
  ];
  if (o.length > 0) return Xy(o);
  const r = ea(e.outcomes);
  return r.length > 0 ? r.map((i) => ({ name: i, displayName: i })) : [{ name: "Done", displayName: "Done" }];
}
function By(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function Vo(e, t, n, o) {
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
function Fy(e, t, n) {
  const o = Vo(t.source, n, t.sourceHandle ?? "Done", void 0), r = Vo(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((i) => i.id !== t.id).concat(o, r);
}
function Wr(e) {
  return Array.isArray(e) ? e.filter(Zy) : null;
}
function Wy(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function Eo(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!$i(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...Eo(n.ports));
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
function Xy(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function ea(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function ta(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const o of e) {
    const r = t(o);
    r && n.set(r, [...n.get(r) ?? [], o]);
  }
  return n;
}
function Yy(e) {
  return [...e].sort((t, n) => na(n).localeCompare(na(t)))[0];
}
function na(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function qy(e) {
  return $i(e) && typeof e.x == "number" && typeof e.y == "number";
}
function $i(e) {
  return typeof e == "object" && e !== null;
}
function Zy(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Ky(e) {
  return e.kind === Qo ? "sequence" : e.kind === Yn ? "flowchart" : "generic";
}
function Uy(e) {
  return e.kind === Qo || e.kind === Yn, "Activities";
}
function Gy(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function Jy(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Qy = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function ex(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function Ti(e) {
  return ex(e.name);
}
function tx(e, t) {
  const n = Ti(t), o = e[n];
  return t.isWrapped === !1 ? o ?? t.defaultValue ?? "" : Il(o, t);
}
function Cl(e, t) {
  return Il(e[Ti(t)], t);
}
function nx(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function ox(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function oa(e, t, n) {
  return {
    ...e,
    [Ti(t)]: n
  };
}
function rx(e, t) {
  return t.isWrapped === !1 ? tx(e, t) : Cl(e, t).expression.value;
}
function Il(e, t) {
  return ix(e) ? {
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
function ix(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
const jl = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]);
function sx({
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
  const c = ux(s), u = o.length > 0 ? o : Qy;
  return /* @__PURE__ */ a.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ a.jsx("span", { className: "wf-section-label", children: "Properties" }),
    c.map((l) => /* @__PURE__ */ a.jsxs("section", { className: "wf-property-group", children: [
      c.length > 1 ? /* @__PURE__ */ a.jsx("h4", { children: l.category }) : null,
      l.inputs.map((f) => /* @__PURE__ */ a.jsx(
        ax,
        {
          activity: e,
          input: f,
          editors: n,
          expressionDescriptors: u,
          onChange: i
        },
        f.name
      ))
    ] }, l.category))
  ] });
}
function ax({
  activity: e,
  input: t,
  editors: n,
  expressionDescriptors: o,
  onChange: r
}) {
  const i = t.isReadOnly === !0, s = { activity: e, expressionDescriptors: o, readOnly: i }, c = lx(n, t, s), u = c?.component, l = t.isWrapped !== !1 ? Cl(e, t) : null, f = l?.expression.type ?? "Literal", d = rx(e, t), h = !!(l && dx(t, c?.id)), p = !!(l && fx(t, c?.id)), [g, x] = Y(!1), w = (b) => {
    const m = l ? nx(l, b) : b;
    r(oa(e, t, m));
  }, y = (b) => {
    l && r(oa(e, t, ox(l, b)));
  };
  return /* @__PURE__ */ a.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ a.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ a.jsx("span", { children: _l(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ a.jsx("p", { children: t.description }) : null,
    l && !h ? /* @__PURE__ */ a.jsx(
      li,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: f,
        descriptors: o,
        disabled: i,
        onChange: y
      }
    ) : null,
    h ? /* @__PURE__ */ a.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ a.jsx("div", { className: "wf-expression-editor", children: ra(u, t, d, i, s, w) }),
      /* @__PURE__ */ a.jsx(
        li,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: f,
          descriptors: o,
          disabled: i,
          variant: "inline",
          onChange: y
        }
      ),
      p ? /* @__PURE__ */ a.jsx(
        "button",
        {
          type: "button",
          className: "wf-expression-expand-button",
          "aria-label": `Open expanded ${t.displayName || t.name} editor`,
          title: "Open expanded editor",
          onClick: () => x(!0),
          children: /* @__PURE__ */ a.jsx(Lo, { size: 13 })
        }
      ) : null
    ] }) : ra(u, t, d, i, s, w),
    p && !h ? /* @__PURE__ */ a.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => x(!0),
        children: [
          /* @__PURE__ */ a.jsx(Lo, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    g ? /* @__PURE__ */ a.jsx(
      cx,
      {
        input: t,
        value: d,
        syntax: f,
        descriptors: o,
        disabled: i,
        onChange: w,
        onSyntaxChange: y,
        onClose: () => x(!1)
      }
    ) : null
  ] });
}
function cx({
  input: e,
  value: t,
  syntax: n,
  descriptors: o,
  disabled: r,
  onChange: i,
  onSyntaxChange: s,
  onClose: c
}) {
  const u = va(), l = e.displayName || e.name;
  return ie(() => {
    const f = (d) => {
      d.key === "Escape" && c();
    };
    return window.addEventListener("keydown", f), () => window.removeEventListener("keydown", f);
  }, [c]), /* @__PURE__ */ a.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ a.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": u, children: [
    /* @__PURE__ */ a.jsxs("header", { children: [
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ a.jsx("h3", { id: u, children: l })
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", "aria-label": `Close ${l} editor`, onClick: c, children: /* @__PURE__ */ a.jsx(ly, { size: 16 }) })
    ] }),
    /* @__PURE__ */ a.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ a.jsx(
          li,
          {
            label: `${l} expression syntax`,
            value: n,
            descriptors: o,
            disabled: r,
            onChange: s
          }
        ),
        /* @__PURE__ */ a.jsx("span", { children: _l(e.typeName) })
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
function ra(e, t, n, o, r, i) {
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
function li({
  label: e,
  value: t,
  descriptors: n,
  disabled: o,
  variant: r = "block",
  onChange: i
}) {
  const [s, c] = Y(!1), u = va(), l = n.find((d) => d.type === t), f = [
    "wf-syntax-picker-trigger",
    r === "inline" ? "inline" : "",
    s ? "open" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ a.jsxs("div", { className: r === "inline" ? "wf-syntax-picker inline" : "wf-syntax-picker", onBlur: (d) => {
    d.currentTarget.contains(d.relatedTarget) || c(!1);
  }, children: [
    /* @__PURE__ */ a.jsx(
      "button",
      {
        type: "button",
        className: f,
        "aria-label": e,
        "aria-haspopup": "listbox",
        "aria-expanded": s,
        "aria-controls": u,
        disabled: o,
        onClick: () => c((d) => !d),
        children: /* @__PURE__ */ a.jsx("span", { children: l?.displayName || l?.type || t })
      }
    ),
    s ? /* @__PURE__ */ a.jsx("div", { id: u, role: "listbox", className: "wf-syntax-picker-menu", "aria-label": e, children: n.map((d) => {
      const h = d.displayName || d.type, p = d.type === t;
      return /* @__PURE__ */ a.jsx(
        "button",
        {
          type: "button",
          role: "option",
          "aria-selected": p,
          className: p ? "selected" : "",
          onClick: () => {
            i(d.type), c(!1);
          },
          children: h
        },
        d.type
      );
    }) }) : null
  ] });
}
function lx(e, t, n) {
  return [...e].sort((o, r) => (o.order ?? 500) - (r.order ?? 500)).find((o) => o.supports(t, n));
}
function ux(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "General";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return [...t.entries()].map(([n, o]) => ({ category: n, inputs: o }));
}
function _l(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function dx(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !jl.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function fx(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !jl.has(t) && n !== "multiline") return !1;
  const o = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(o) || n === "singleline" || n === "multiline";
}
const Al = { workflowActivity: Zx }, Ml = { workflow: Ux }, ia = "application/x-elsa-activity-version-id", hx = 6, px = 1200, gx = [10, 25, 50], mx = 10, sa = "elsa-studio-workflow-palette-width", aa = "elsa-studio-workflow-inspector-width", ca = "elsa-studio-workflow-palette-collapsed", la = "elsa-studio-workflow-inspector-collapsed", Dl = "elsa-studio-workflow-side-panel-maximized", yn = 180, xn = 460, yx = 260, wn = 260, vn = 560, xx = 320, ua = 42, mo = 16, Pl = ht.createContext(null);
function a0(e) {
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
        component: () => /* @__PURE__ */ a.jsx(wx, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), workflowDesignerPanels: e.workflowDesigner.panels.list() })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ a.jsx(vx, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow instances",
        component: () => /* @__PURE__ */ a.jsx(bx, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow instance",
        component: () => /* @__PURE__ */ a.jsx(Sx, { context: e.backend, ai: e.ai })
      }
    ]
  });
}
function wx({
  context: e,
  ai: t,
  propertyEditors: n,
  workflowDesignerPanels: o
}) {
  const [r, i] = Y(da);
  ie(() => {
    const c = () => i(da());
    return window.addEventListener("popstate", c), () => window.removeEventListener("popstate", c);
  }, []);
  const s = (c) => {
    const u = c ? `/workflows/definitions?definition=${encodeURIComponent(c)}` : "/workflows/definitions";
    window.history.pushState({}, "", u), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return r ? /* @__PURE__ */ a.jsx(qx, { context: e, definitionId: r, ai: t, propertyEditors: n, workflowDesignerPanels: o, onBack: () => s(null) }) : /* @__PURE__ */ a.jsx(er, { activePath: "/workflows/definitions", title: "Definitions", children: /* @__PURE__ */ a.jsx(Ex, { context: e, ai: t, onOpen: s }) });
}
function vx({ context: e, ai: t }) {
  const [n, o] = Y(fa);
  return ie(() => {
    const r = () => o(fa());
    return window.addEventListener("popstate", r), () => window.removeEventListener("popstate", r);
  }, []), /* @__PURE__ */ a.jsx(er, { activePath: "/workflows/executables", title: "Executables", children: /* @__PURE__ */ a.jsx(Cx, { context: e, ai: t, definitionFilter: n }) });
}
function bx({ context: e, ai: t }) {
  return /* @__PURE__ */ a.jsx(er, { activePath: "/workflows/instances", title: "Instances", children: /* @__PURE__ */ a.jsx(Ix, { context: e, ai: t }) });
}
function Sx({ context: e, ai: t }) {
  const n = Nx();
  return /* @__PURE__ */ a.jsx(er, { activePath: "/workflows/instances", title: "Instance", children: /* @__PURE__ */ a.jsx(jx, { context: e, ai: t, workflowExecutionId: n }) });
}
function er({ activePath: e, title: t, children: n }) {
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
function da() {
  return new URLSearchParams(window.location.search).get("definition");
}
function fa() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Nx() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function Ex({ context: e, ai: t, onOpen: n }) {
  const [o, r] = Y(""), [i, s] = Y("active"), [c, u] = Y(1), [l, f] = Y(mx), [d, h] = Y("loading"), [p, g] = Y(""), [x, w] = Y(""), [y, b] = Y([]), [m, v] = Y(0), [k, E] = Y(() => /* @__PURE__ */ new Set()), [I, D] = Y(null), [$, F] = Y(!1), [_, z] = Y([]), [V, S] = Y("idle"), j = ce(null), C = me(() => y.map((L) => L.id), [y]), A = Kt(t, "weaver.workflows.suggest-create-metadata"), T = Kt(t, "weaver.workflows.explain-definition"), P = C.filter((L) => k.has(L)).length, W = C.length > 0 && P === C.length, O = ge(async () => {
    h("loading"), g("");
    try {
      const L = await fy(e, { search: o, state: i, page: c, pageSize: l }), K = typeof L.totalCount == "number", fe = L.totalCount ?? L.definitions.length, ue = $l(fe, l);
      if (fe > 0 && c > ue) {
        u(ue);
        return;
      }
      b(K ? L.definitions : zx(L.definitions, c, l)), v(fe), h("ready");
    } catch (L) {
      g(L instanceof Error ? L.message : String(L)), h("failed");
    }
  }, [e, o, i, c, l]);
  ie(() => {
    O();
  }, [O]), ie(() => {
    j.current && (j.current.indeterminate = P > 0 && !W);
  }, [W, P]);
  const B = ge(async () => {
    if (!(V === "loading" || V === "ready")) {
      S("loading");
      try {
        const L = await Di(e);
        z(L.activities ?? []), S("ready");
      } catch (L) {
        S("failed"), g(L instanceof Error ? L.message : String(L));
      }
    }
  }, [V, e]), G = () => {
    g(""), w(""), D({ name: "", description: "", rootKind: "flowchart" }), B();
  }, Z = async () => {
    if (I?.name.trim()) {
      F(!0), g(""), w("");
      try {
        const L = await gy(e, {
          name: I.name.trim(),
          description: I.description.trim() || null,
          rootKind: I.rootKind,
          rootActivityVersionId: Vx(I, _)
        });
        D(null), n(L.definition.id);
      } catch (L) {
        g(L instanceof Error ? L.message : String(L));
      } finally {
        F(!1);
      }
    }
  }, te = (L) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(L)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, ae = async () => {
    if (y.length === 1 && c > 1) {
      u(c - 1);
      return;
    }
    await O();
  }, J = () => E(/* @__PURE__ */ new Set()), R = (L, K) => {
    E((fe) => {
      const ue = new Set(fe);
      return K ? ue.add(L) : ue.delete(L), ue;
    });
  }, q = (L) => {
    E((K) => {
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
      w(""), g("");
      try {
        await my(e, L.id), R(L.id, !1), w(`Deleted ${L.name}`), await ae();
      } catch (K) {
        g(K instanceof Error ? K.message : String(K));
      }
    }
  }, Q = async (L) => {
    w(""), g("");
    try {
      await yy(e, L.id), R(L.id, !1), w(`Restored ${L.name}`), await ae();
    } catch (K) {
      g(K instanceof Error ? K.message : String(K));
    }
  }, se = async (L) => {
    if (window.confirm(`Permanently delete workflow definition "${L.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`)) {
      w(""), g("");
      try {
        await xy(e, L.id), R(L.id, !1), w(`Permanently deleted ${L.name}`), await ae();
      } catch (K) {
        g(K instanceof Error ? K.message : String(K));
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
        /* @__PURE__ */ a.jsx(ay, { size: 15 }),
        /* @__PURE__ */ a.jsx("input", { value: o, onChange: (L) => oe(L.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
        O();
      }, children: "Refresh" }),
      /* @__PURE__ */ a.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ a.jsxs("button", { type: "button", title: "Create workflow", onClick: G, children: [
        /* @__PURE__ */ a.jsx(Mi, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    d === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(st, { size: 16 }),
      " ",
      p
    ] }) : null,
    d !== "failed" && p ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(st, { size: 16 }),
      " ",
      p
    ] }) : null,
    x ? /* @__PURE__ */ a.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ a.jsx(Xn, { size: 14 }),
      " ",
      x
    ] }) : null,
    k.size > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ a.jsxs("span", { children: [
        k.size,
        " selected"
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: J, children: "Clear selection" })
    ] }) : null,
    d === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading workflow definitions..." }) : null,
    d === "ready" && y.length === 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-empty", children: [
      "No ",
      i,
      " workflow definitions found."
    ] }) : null,
    d === "ready" && y.length > 0 ? /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ a.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ a.jsx(
            "input",
            {
              ref: j,
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
            "aria-selected": k.has(L.id),
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
                  checked: k.has(L.id),
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
                T ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => At(t, T, L), children: [
                  /* @__PURE__ */ a.jsx(_t, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ a.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  U(L);
                }, children: [
                  /* @__PURE__ */ a.jsx(ii, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
                /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
                  Q(L);
                }, children: [
                  /* @__PURE__ */ a.jsx(xl, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ a.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  se(L);
                }, children: [
                  /* @__PURE__ */ a.jsx(ii, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          L.id
        ))
      ] }),
      /* @__PURE__ */ a.jsx(
        Tx,
        {
          page: c,
          pageSize: l,
          totalCount: m,
          onPageChange: u,
          onPageSizeChange: (L) => {
            f(L), u(1);
          }
        }
      )
    ] }) : null,
    I ? /* @__PURE__ */ a.jsx(
      kx,
      {
        draft: I,
        activities: _,
        catalogState: V,
        creating: $,
        suggestMetadataAction: A,
        onSuggestMetadata: A ? () => At(t, A, { draft: I, activities: _ }) : void 0,
        onChange: (L) => D(L),
        onClose: () => D(null),
        onSubmit: Z
      }
    ) : null
  ] });
}
function kx({ draft: e, activities: t, catalogState: n, creating: o, suggestMetadataAction: r, onSuggestMetadata: i, onChange: s, onClose: c, onSubmit: u }) {
  const l = me(() => Rx(t), [t]), f = Lx(e, t), d = (h) => {
    if (h.startsWith("kind:")) {
      s({ ...e, rootKind: h.slice(5), rootActivityVersionId: null });
      return;
    }
    const p = t.find((g) => g.activityVersionId === h);
    s({
      ...e,
      rootKind: Tl(p) ?? e.rootKind,
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
            /* @__PURE__ */ a.jsx(_t, { size: 13 }),
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
              onChange: (h) => d(h.target.value),
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
function Cx({ context: e, ai: t, definitionFilter: n }) {
  const [o, r] = Y("loading"), [i, s] = Y(""), [c, u] = Y(""), [l, f] = Y([]), d = me(
    () => n ? l.filter((x) => x.definitionId === n || x.sourceId === n) : l,
    [n, l]
  ), h = Kt(t, "weaver.workflows.explain-executable"), p = ge(async () => {
    r("loading"), s("");
    try {
      f(await Ey(e)), r("ready");
    } catch (x) {
      s(x instanceof Error ? x.message : String(x)), r("failed");
    }
  }, [e]);
  ie(() => {
    p();
  }, [p]);
  const g = async (x) => {
    u(""), s("");
    try {
      await Ny(e, x.artifactId), u(`Started ${x.artifactId}`);
    } catch (w) {
      s(w instanceof Error ? w.message : String(w));
    }
  };
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
        p();
      }, children: "Refresh" }),
      n ? /* @__PURE__ */ a.jsxs("span", { className: "wf-filter-chip", children: [
        "Definition ",
        n
      ] }) : null
    ] }),
    o === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(st, { size: 16 }),
      " ",
      i
    ] }) : null,
    c ? /* @__PURE__ */ a.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ a.jsx(Xn, { size: 14 }),
      " ",
      c
    ] }) : null,
    o === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading workflow executables..." }) : null,
    o === "ready" && d.length === 0 ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: n ? "No workflow executables found for this definition." : "No workflow executables found. Publish a workflow definition to create one." }) : null,
    o === "ready" && d.length > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ a.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ a.jsx("span", { children: "Version" }),
        /* @__PURE__ */ a.jsx("span", { children: "Source" }),
        /* @__PURE__ */ a.jsx("span", { children: "Root" }),
        /* @__PURE__ */ a.jsx("span", { children: "Published" }),
        /* @__PURE__ */ a.jsx("span", { children: "Actions" })
      ] }),
      d.map((x) => /* @__PURE__ */ a.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ a.jsxs("span", { children: [
          /* @__PURE__ */ a.jsx("strong", { children: x.artifactId }),
          /* @__PURE__ */ a.jsx("small", { children: x.artifactHash })
        ] }),
        /* @__PURE__ */ a.jsx("span", { children: x.artifactVersion }),
        /* @__PURE__ */ a.jsx("span", { children: Ox(x) }),
        /* @__PURE__ */ a.jsx("span", { children: Bx(x) }),
        /* @__PURE__ */ a.jsx("span", { children: rt(x.publishedAt ?? x.createdAt) }),
        /* @__PURE__ */ a.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
            g(x);
          }, children: [
            /* @__PURE__ */ a.jsx(Ai, { size: 13 }),
            " Run"
          ] }),
          h ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => At(t, h, x), children: [
            /* @__PURE__ */ a.jsx(_t, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, x.artifactId))
    ] }) : null
  ] });
}
function Ix({ context: e }) {
  const [t, n] = Y("loading"), [o, r] = Y(""), [i, s] = Y(""), [c, u] = Y([]), l = ge(async () => {
    n("loading"), r("");
    try {
      const d = await ky(e, { status: i || void 0, take: 100 });
      u(d), n("ready");
    } catch (d) {
      r(d instanceof Error ? d.message : String(d)), u([]), n("failed");
    }
  }, [e, i]);
  ie(() => {
    l();
  }, [l]);
  const f = (d) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(d)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
        l();
      }, children: "Refresh" }),
      /* @__PURE__ */ a.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ a.jsx("span", { children: "Status" }),
        /* @__PURE__ */ a.jsxs("select", { "aria-label": "Workflow instance status", value: i, onChange: (d) => s(d.target.value), children: [
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
      /* @__PURE__ */ a.jsx(st, { size: 16 }),
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
      c.map((d) => /* @__PURE__ */ a.jsxs(
        "button",
        {
          type: "button",
          className: "wf-grid-row",
          role: "row",
          "aria-label": `Inspect workflow instance ${d.workflowExecutionId}`,
          onClick: () => f(d.workflowExecutionId),
          children: [
            /* @__PURE__ */ a.jsxs("span", { children: [
              /* @__PURE__ */ a.jsx("strong", { children: d.workflowExecutionId }),
              /* @__PURE__ */ a.jsx("small", { children: d.artifactId })
            ] }),
            /* @__PURE__ */ a.jsx("span", { children: /* @__PURE__ */ a.jsx(qn, { status: d.status, subStatus: d.subStatus }) }),
            /* @__PURE__ */ a.jsxs("span", { children: [
              /* @__PURE__ */ a.jsx("strong", { children: d.definitionId }),
              /* @__PURE__ */ a.jsx("small", { children: d.definitionVersionId })
            ] }),
            /* @__PURE__ */ a.jsxs("span", { children: [
              /* @__PURE__ */ a.jsxs("strong", { children: [
                d.activityCount,
                " activities"
              ] }),
              /* @__PURE__ */ a.jsxs("small", { children: [
                d.incidentCount,
                " incidents"
              ] })
            ] }),
            /* @__PURE__ */ a.jsx("span", { children: rt(d.startedAt ?? d.createdAt) }),
            /* @__PURE__ */ a.jsx("span", { children: r0(d.startedAt ?? d.createdAt, d.completedAt ?? d.updatedAt) })
          ]
        },
        d.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function jx({ context: e, ai: t, workflowExecutionId: n }) {
  const [o, r] = Y("loading"), [i, s] = Y(""), [c, u] = Y(null), [l, f] = Y(null), d = Kt(t, "weaver.workflows.explain-instance"), h = ge(async () => {
    if (!n) {
      s("No workflow execution id was provided."), r("failed");
      return;
    }
    r("loading"), s("");
    try {
      const g = await Cy(e, n), [x, w] = await Promise.all([
        py(e, g.instance.definitionVersionId),
        Di(e)
      ]);
      u({ details: g, definitionVersion: x, activityCatalog: w.activities }), f(null), r("ready");
    } catch (g) {
      u(null), s(g instanceof Error ? g.message : String(g)), r("failed");
    }
  }, [e, n]);
  ie(() => {
    h();
  }, [h]);
  const p = () => {
    window.history.pushState({}, "", "/workflows/instances"), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: p, children: [
        /* @__PURE__ */ a.jsx(Ro, { size: 14 }),
        " Instances"
      ] }),
      /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
        h();
      }, children: [
        /* @__PURE__ */ a.jsx(xl, { size: 14 }),
        " Refresh"
      ] }),
      c && d ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => At(t, d, c.details), children: [
        /* @__PURE__ */ a.jsx(_t, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    o === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading workflow instance..." }) : null,
    o === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(st, { size: 16 }),
      " ",
      i
    ] }) : null,
    o === "ready" && c ? /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-detail-workbench", children: [
      /* @__PURE__ */ a.jsx(
        _x,
        {
          definitionVersion: c.definitionVersion,
          activityCatalog: c.activityCatalog,
          details: c.details,
          selectedEvidenceId: l,
          onSelectEvidence: f
        }
      ),
      /* @__PURE__ */ a.jsx(
        Ax,
        {
          ai: t,
          action: d,
          summary: c.details.instance,
          details: c.details,
          state: "ready",
          error: "",
          selectedEvidenceId: l,
          onSelectEvidence: f,
          graphNodeIds: $x(c.definitionVersion, c.activityCatalog)
        }
      )
    ] }) : null
  ] });
}
function _x({ definitionVersion: e, activityCatalog: t, details: n, selectedEvidenceId: o, onSelectEvidence: r }) {
  const i = me(() => {
    const s = e.state.rootActivity;
    if (!s) return { nodes: [], edges: [] };
    const c = t.find((h) => h.activityVersionId === s.activityVersionId), u = Pi(s, c), l = u === "unsupported" ? null : In(s, []), f = u === "unsupported" ? si(s, t, e.layout) : l ? bl(l, t, e.layout) : si(s, t, e.layout), d = f.nodes.map((h) => ({
      ...h,
      draggable: !1,
      connectable: !1,
      deletable: !1
    }));
    return {
      nodes: Ay(d, n.activities, n.incidents, o),
      edges: f.edges.map((h) => ({ ...h, deletable: !1 }))
    };
  }, [t, e, n, o]);
  return /* @__PURE__ */ a.jsxs("section", { className: "wf-instance-canvas-shell", "aria-label": "Workflow instance canvas", children: [
    /* @__PURE__ */ a.jsxs("header", { children: [
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("span", { children: "Definition version" }),
        /* @__PURE__ */ a.jsxs("h3", { children: [
          e.definition.name,
          " ",
          /* @__PURE__ */ a.jsx("small", { children: e.version })
        ] })
      ] }),
      /* @__PURE__ */ a.jsx(qn, { status: n.instance.status, subStatus: n.instance.subStatus })
    ] }),
    /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-canvas", children: [
      i.nodes.length === 0 ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      i.nodes.length > 0 ? /* @__PURE__ */ a.jsxs(
        al,
        {
          nodes: i.nodes,
          edges: i.edges,
          nodeTypes: Al,
          edgeTypes: Ml,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (s, c) => r(c.id),
          onPaneClick: () => r(null),
          children: [
            /* @__PURE__ */ a.jsx(ll, {}),
            /* @__PURE__ */ a.jsx(hl, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ a.jsx(dl, {})
          ]
        }
      ) : null
    ] })
  ] });
}
function Ax({ ai: e, action: t, summary: n, details: o, state: r, error: i, selectedEvidenceId: s = null, onSelectEvidence: c, graphNodeIds: u }) {
  return n ? /* @__PURE__ */ a.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Workflow instance details", children: [
    /* @__PURE__ */ a.jsxs("header", { children: [
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("span", { children: "Workflow instance" }),
        /* @__PURE__ */ a.jsx("h3", { children: n.workflowExecutionId })
      ] }),
      t ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => At(e, t, o ?? n), children: [
        /* @__PURE__ */ a.jsx(_t, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    /* @__PURE__ */ a.jsxs("dl", { className: "wf-instance-meta", children: [
      /* @__PURE__ */ a.jsx("dt", { children: "Status" }),
      /* @__PURE__ */ a.jsx("dd", { children: /* @__PURE__ */ a.jsx(qn, { status: n.status, subStatus: n.subStatus }) }),
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
      /* @__PURE__ */ a.jsx(st, { size: 16 }),
      " ",
      i
    ] }) : null,
    r === "ready" && o ? /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
      /* @__PURE__ */ a.jsx(Mx, { activities: o.activities, selectedEvidenceId: s, onSelectEvidence: c }),
      /* @__PURE__ */ a.jsx(Dx, { incidents: o.incidents, selectedEvidenceId: s, onSelectEvidence: c }),
      /* @__PURE__ */ a.jsx(Px, { details: o, graphNodeIds: u })
    ] }) : null
  ] }) : /* @__PURE__ */ a.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Select a workflow instance to inspect activity history." }) });
}
function Mx({ activities: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
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
          /* @__PURE__ */ a.jsx("span", { children: /* @__PURE__ */ a.jsx(qn, { status: o.status, subStatus: o.subStatus }) }),
          /* @__PURE__ */ a.jsx("strong", { children: zi(o.activityType) ?? o.activityType }),
          /* @__PURE__ */ a.jsx("small", { children: o.activityExecutionId }),
          /* @__PURE__ */ a.jsx("time", { children: rt(o.scheduledAt) })
        ]
      },
      o.activityExecutionId
    )) }) : null
  ] });
}
function Dx({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
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
function Px({ details: e, graphNodeIds: t }) {
  if (!t) return null;
  const n = new Map(e.activities.map((i) => [i.activityExecutionId, i])), o = e.activities.filter((i) => !t.has(ha(i))), r = e.incidents.filter((i) => {
    const s = i.activityExecutionId ? n.get(i.activityExecutionId) : null, c = i.executableNodeId ?? (s ? ha(s) : "");
    return !c || !t.has(c);
  });
  return o.length === 0 && r.length === 0 ? null : /* @__PURE__ */ a.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ a.jsx("h4", { children: "Unmatched runtime evidence" }),
    /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-unmatched-list", children: [
      o.map((i) => /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ a.jsx("strong", { children: zi(i.activityType) ?? i.activityType }),
        /* @__PURE__ */ a.jsx("small", { children: i.activityExecutionId })
      ] }, `activity-${i.activityExecutionId}`)),
      r.map((i) => /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ a.jsx("strong", { children: i.failureType }),
        /* @__PURE__ */ a.jsx("small", { children: i.incidentId })
      ] }, `incident-${i.incidentId}`))
    ] })
  ] });
}
function qn({ status: e, subStatus: t }) {
  return /* @__PURE__ */ a.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
function $x(e, t) {
  const n = e.state.rootActivity;
  if (!n) return /* @__PURE__ */ new Set();
  const o = t.find((i) => i.activityVersionId === n.activityVersionId);
  if (Pi(n, o) === "unsupported") return /* @__PURE__ */ new Set([n.nodeId]);
  const r = In(n, []);
  return new Set(r?.slot.activities.map((i) => i.nodeId) ?? [n.nodeId]);
}
function ha(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function Tx({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const i = $l(n, t), s = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
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
      /* @__PURE__ */ a.jsx("select", { value: t, onChange: (u) => r(Number(u.target.value)), children: gx.map((u) => /* @__PURE__ */ a.jsx("option", { value: u, children: u }, u)) })
    ] }),
    /* @__PURE__ */ a.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ a.jsx(Ro, { size: 14 }),
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
        /* @__PURE__ */ a.jsx(zt, { size: 14 })
      ] })
    ] })
  ] });
}
function zx(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function $l(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Kt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function At(e, t, n) {
  const o = t.createPrompt(n);
  o && e.dispatchPrompt(o);
}
function Rx(e) {
  const t = Ho(e, "flowchart"), n = Ho(e, "sequence"), o = [
    { value: t?.activityVersionId ?? "kind:flowchart", label: "Flowchart" },
    { value: n?.activityVersionId ?? "kind:sequence", label: "Sequence" }
  ], r = /* @__PURE__ */ new Map();
  for (const s of e.filter(Vl)) {
    if (Hx(s)) continue;
    const c = s.category || "Uncategorized";
    r.set(c, [...r.get(c) ?? [], s]);
  }
  const i = Array.from(r.entries()).sort(([s], [c]) => s.localeCompare(c)).map(([s, c]) => ({
    name: s,
    activities: c.sort((u, l) => Ce(u).localeCompare(Ce(l)))
  }));
  return { compositeRoots: o, otherCategories: i };
}
function Lx(e, t) {
  return e.rootActivityVersionId ?? Ho(t, e.rootKind)?.activityVersionId ?? `kind:${e.rootKind}`;
}
function Vx(e, t) {
  return e.rootActivityVersionId ?? Ho(t, e.rootKind)?.activityVersionId ?? null;
}
function Ho(e, t) {
  return e.find((n) => Tl(n) === t);
}
function Tl(e) {
  return e ? Rl(e) ? "flowchart" : Ll(e) ? "sequence" : null : null;
}
function zl(e) {
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
function Hx(e) {
  return Rl(e) || Ll(e);
}
function Rl(e) {
  return Ce(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function Ll(e) {
  return Ce(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function Vl(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function Ox(e) {
  return e.sourceKind || e.sourceId || e.sourceVersion ? [e.sourceKind, e.sourceId, e.sourceVersion].filter(Boolean).join(" / ") : e.definitionId;
}
function Bx(e) {
  return Fx(e.rootActivityType) || e.rootActivityType;
}
function Fx(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function Wx(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    yo(t, n.typeName, n), yo(t, n.name, n), yo(t, n.displayName, n);
    const o = n.typeName.split(".").filter(Boolean).at(-1);
    yo(t, o, n);
  }
  return t;
}
function Xx(e, t, n) {
  const o = t.get(e.activityVersionId);
  return n.get(En(o?.activityTypeKey)) ?? n.get(En(zi(o?.activityTypeKey))) ?? n.get(En(o?.displayName)) ?? n.get(En(e.activityVersionId)) ?? null;
}
function yo(e, t, n) {
  const o = En(t);
  o && !e.has(o) && e.set(o, n);
}
function En(e) {
  return e?.trim().toLowerCase() ?? "";
}
function zi(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function pa(e, t, n, o) {
  const r = tr();
  if (!r) return t;
  const i = r.getItem(e);
  if (i == null) return t;
  const s = Number(i);
  return Number.isFinite(s) ? ko(s, n, o) : t;
}
function ga(e, t) {
  const n = tr();
  if (!n) return t;
  const o = n.getItem(e);
  return o === "true" ? !0 : o === "false" ? !1 : t;
}
function Yx() {
  const e = tr();
  if (!e) return null;
  const t = e.getItem(Dl);
  return t === "palette" || t === "inspector" ? t : null;
}
function tr() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function bn(e, t) {
  const n = tr();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function ko(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function qx({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: o,
  workflowDesignerPanels: r,
  onBack: i
}) {
  const [s, c] = Y(null), [u, l] = Y(null), [f, d] = Y([]), [h, p] = Y([]), [g, x] = Y(No), [w, y] = Y("loading"), [b, m] = Y([]), [v, k] = Y([]), [E, I] = Y([]), [D, $] = Y(null), [F, _] = Y(null), [z, V] = Y(null), [S, j] = Y(null), [C, A] = Y(""), [T, P] = Y(""), [W, O] = Y("idle"), [B, G] = Y(null), [Z, te] = Y(!1), [ae, J] = Y(null), [R, q] = Y(() => /* @__PURE__ */ new Set()), [re, oe] = Y(() => pa(sa, yx, yn, xn)), [U, Q] = Y(() => pa(aa, xx, wn, vn)), [se, L] = Y(() => ga(ca, !1)), [K, fe] = Y(() => ga(la, !1)), [ue, je] = Y(Yx), [Oe, Be] = Y("activities"), [pt, et] = Y("inspector"), Ee = ce(null), _e = ce(null), $e = ce(""), Te = ce(0), Ke = ce(Promise.resolve()), Mt = ce(null), tt = ce(!1), gt = u?.state.rootActivity ?? null, mt = me(() => new Map(f.map((N) => [N.activityVersionId, N])), [f]), Zn = me(() => Wx(h), [h]), ze = me(() => vl(gt, b), [gt, b]), Kn = Pi(ze, ze ? mt.get(ze.activityVersionId) : void 0), we = !!ze && Kn === "unsupported", Fe = me(() => we ? null : In(gt, b), [gt, b, we]), Gt = me(() => zl(f), [f]), ke = me(() => we && ze?.nodeId === F ? ze : Fe?.slot.activities.find((N) => N.nodeId === F) ?? null, [we, Fe, ze, F]), Jt = me(
    () => ke ? Xx(ke, mt, Zn) : null,
    [mt, Zn, ke]
  ), Qt = ke ? Je(ke) : [], Ie = Kn === "flowchart" && Fe?.slot.mode === "flowchart", yt = !gt || !we, ct = W !== "idle", nr = !!u?.state.rootActivity && !ct, Un = Kt(n, "weaver.workflows.find-draft-risks"), Gn = Kt(n, "weaver.workflows.propose-update");
  ie(() => {
    bn(sa, String(re));
  }, [re]), ie(() => {
    bn(aa, String(U));
  }, [U]), ie(() => {
    bn(ca, String(se));
  }, [se]), ie(() => {
    bn(la, String(K));
  }, [K]), ie(() => {
    bn(Dl, ue);
  }, [ue]), ie(() => {
    if (!ue) return;
    const N = (M) => {
      M.key === "Escape" && je(null);
    };
    return window.addEventListener("keydown", N), () => window.removeEventListener("keydown", N);
  }, [ue]);
  const en = ge(async () => {
    A(""), y("loading");
    const [N, M, H, X] = await Promise.all([
      hy(e, t),
      Di(e),
      Iy(e).then(
        (he) => ({ ok: !0, descriptors: he }),
        () => ({ ok: !1, descriptors: [] })
      ),
      jy(e).then(
        (he) => ({ ok: !0, descriptors: he }),
        () => ({ ok: !1, descriptors: No })
      )
    ]), ne = N.draft ?? null;
    c(N), $e.current = ne ? wt(ne) : "", l(ne), d(M.activities ?? []), p(H.descriptors), x(X.descriptors.length > 0 ? X.descriptors : No), y(H.ok ? "ready" : "failed"), m([]), _(null);
  }, [e, t]);
  ie(() => {
    en().catch((N) => A(N instanceof Error ? N.message : String(N)));
  }, [en]), ie(() => {
    q((N) => {
      let M = !1;
      const H = new Set(N);
      for (const X of Gt)
        H.has(X.category) || (H.add(X.category), M = !0);
      return M ? H : N;
    });
  }, [Gt]), ie(() => {
    if (!ze) {
      k([]), I([]);
      return;
    }
    const N = we ? si(ze, f, u?.layout ?? []) : Fe ? bl(Fe, f, u?.layout ?? []) : { nodes: [], edges: [] };
    k(N.nodes), I(N.edges);
  }, [f, u?.layout, we, Fe, ze]);
  const or = (N) => {
    l((M) => M && { ...M, state: { ...M.state, rootActivity: N } });
  }, tn = ge((N, M) => {
    if (u?.state.rootActivity && we)
      return;
    const H = Qs(N, xa(N));
    if (!u?.state.rootActivity) {
      or(H), _(H.nodeId);
      return;
    }
    if (!Fe) {
      if (!Je(H)[0]) {
        P(""), A("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      l((ne) => {
        if (!ne?.state.rootActivity) return ne;
        const he = ne.state.rootActivity, le = ai(H, [], [he]), pe = M ? [
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
      }), _(u.state.rootActivity.nodeId), A(""), P(`Wrapped root in ${Ce(N)}`);
      return;
    }
    l((X) => {
      if (!X?.state.rootActivity) return X;
      const ne = In(X.state.rootActivity, b);
      if (!ne) return X;
      const he = ai(X.state.rootActivity, b, [...ne.slot.activities, H]), le = M ? [
        ...X.layout.filter((pe) => pe.nodeId !== H.nodeId),
        {
          nodeId: H.nodeId,
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
    }), _(H.nodeId);
  }, [u?.state.rootActivity, b, we, Fe]), Dt = ge((N, M) => {
    const H = Qs(N, xa(N)), X = {
      id: H.nodeId,
      type: "workflowActivity",
      position: M,
      selected: !0,
      data: {
        label: Ce(N),
        activityVersionId: N.activityVersionId,
        activityTypeKey: N.activityTypeKey,
        category: N.category,
        executionType: N.executionType,
        icon: ci(N),
        childSlots: Je(H),
        acceptsInbound: String(N.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: kl(H, N)
      }
    };
    return { activityNode: H, node: X };
  }, []), Re = ge((N, M, H = []) => {
    we || l((X) => {
      if (!X) return X;
      const ne = Py(X.layout, N), he = X.state.rootActivity;
      if (!he) return { ...X, layout: ne };
      const le = In(he, b);
      if (!le) return { ...X, layout: ne };
      const pe = My(le, N, M, H), Se = le.slot.mode === "flowchart" ? Dy(pe, M) : pe;
      return {
        ...X,
        layout: ne,
        state: {
          ...X.state,
          rootActivity: Sl(he, b, Se)
        }
      };
    });
  }, [b, we]), nn = ge((N, M) => {
    if (!Ee.current) return null;
    const H = Ee.current.getBoundingClientRect();
    return D ? D.screenToFlowPosition({ x: N, y: M }) : {
      x: N - H.left,
      y: M - H.top
    };
  }, [D]), on = ge((N, M) => document.elementFromPoint(N, M)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), rn = ge((N, M, H) => {
    const X = v.find((Ae) => Ae.id === M.source), ne = v.find((Ae) => Ae.id === M.target), he = X && ne ? e0(X, ne) : X ? wa(X) : H, le = Dt(N, he), Se = [...v.map((Ae) => Ae.selected ? { ...Ae, selected: !1 } : Ae), le.node], xt = Fy(E, M, le.node.id);
    k(Se), I(xt), _(le.node.id), Re(Se, xt, [le.activityNode]);
  }, [Re, Dt, E, v]), Pt = ge((N, M, H) => {
    if (!yt || !Ee.current) return !1;
    const X = Ee.current.getBoundingClientRect();
    if (!(M >= X.left && M <= X.right && H >= X.top && H <= X.bottom)) return !1;
    const he = nn(M, H);
    if (!he) return !1;
    if (Ie) {
      const le = on(M, H), pe = le ? E.find((Se) => Se.id === le) : void 0;
      if (pe)
        return rn(N, pe, he), !0;
    }
    return tn(N, he), !0;
  }, [tn, yt, E, on, Ie, rn, nn]);
  ie(() => {
    const N = (H) => {
      const X = Mt.current;
      if (!X) return;
      Math.hypot(H.clientX - X.startX, H.clientY - X.startY) >= hx && (X.dragging = !0);
    }, M = (H) => {
      const X = Mt.current;
      if (Mt.current = null, !X?.dragging || !Ee.current) return;
      const ne = Ee.current.getBoundingClientRect();
      H.clientX >= ne.left && H.clientX <= ne.right && H.clientY >= ne.top && H.clientY <= ne.bottom && (tt.current = !0, window.setTimeout(() => {
        tt.current = !1;
      }, 0), Pt(X.activity, H.clientX, H.clientY));
    };
    return window.addEventListener("pointermove", N), window.addEventListener("pointerup", M), window.addEventListener("pointercancel", M), () => {
      window.removeEventListener("pointermove", N), window.removeEventListener("pointerup", M), window.removeEventListener("pointercancel", M);
    };
  }, [D, Pt]);
  const rr = (N, M) => {
    N.dataTransfer.setData(ia, M.activityVersionId), N.dataTransfer.setData("text/plain", M.activityVersionId), N.dataTransfer.effectAllowed = "copy";
  }, ir = (N, M) => {
    N.clientX === 0 && N.clientY === 0 || Pt(M, N.clientX, N.clientY) && (tt.current = !0, window.setTimeout(() => {
      tt.current = !1;
    }, 0));
  }, sr = (N, M) => {
    N.button === 0 && (Mt.current = {
      activity: M,
      startX: N.clientX,
      startY: N.clientY,
      dragging: !1
    });
  }, ar = (N) => {
    tt.current || yt && tn(N);
  }, sn = (N) => {
    if (!yt) {
      N.dataTransfer.dropEffect = "none";
      return;
    }
    if (N.preventDefault(), N.dataTransfer.dropEffect = "copy", !Ie) return;
    const M = on(N.clientX, N.clientY);
    j(M);
  }, an = (N) => {
    if (!Ee.current) return;
    const M = N.relatedTarget;
    M && Ee.current.contains(M) || j(null);
  }, cn = (N) => {
    if (N.preventDefault(), j(null), !yt) return;
    const M = N.dataTransfer.getData(ia) || N.dataTransfer.getData("text/plain"), H = mt.get(M);
    H && Pt(H, N.clientX, N.clientY);
  }, cr = () => {
    if (!Ie) return;
    const N = Ee.current?.getBoundingClientRect();
    N && V({
      kind: "fromEmpty",
      clientX: N.left + N.width / 2,
      clientY: N.top + N.height / 2
    });
  }, ln = ge(async (N, M) => {
    const H = async () => {
      const ne = ++Te.current, he = wt(N);
      A("");
      try {
        const le = await wy(e, N), pe = wt(le);
        return $e.current = pe, l((Se) => !Se || Se.id !== le.id ? Se : wt(Se) === he ? le : { ...Se, validationErrors: le.validationErrors }), ne === Te.current && P(M), le;
      } catch (le) {
        throw ne === Te.current && (P(""), A(le instanceof Error ? le.message : String(le))), le;
      }
    }, X = Ke.current.then(H, H);
    return Ke.current = X.catch(() => {
    }), X;
  }, [e]);
  ie(() => {
    if (!Z || !u || wt(u) === $e.current) return;
    P("Autosaving...");
    const M = window.setTimeout(() => {
      ln(u, "Autosaved").catch(() => {
      });
    }, px);
    return () => window.clearTimeout(M);
  }, [Z, u, ln]);
  const lr = async () => {
    if (!(!u || ct)) {
      O("saving"), P("Saving...");
      try {
        await ln(u, "Saved");
      } catch {
      } finally {
        O("idle");
      }
    }
  }, ur = async () => {
    if (!(!u || ct)) {
      O("promoting"), P("Promoting...");
      try {
        const N = await vy(e, u.id), M = await by(e, N.versionId);
        J(M.artifactId), P(`Published ${M.artifactVersion}`), await en();
      } catch (N) {
        P(""), A(N instanceof Error ? N.message : String(N));
      } finally {
        O("idle");
      }
    }
  }, dr = async () => {
    if (!u?.state.rootActivity || ct) return;
    const N = u, M = wt(N);
    G(null), P("Preparing test run...");
    try {
      O("testRunPreparing"), P("Preparing test run...");
      const H = n0(N);
      O("testRunStarting"), P("Starting test run...");
      const X = await Sy(e, {
        definitionId: N.definitionId,
        snapshotId: H,
        state: N.state
      });
      G({ draftSignature: M, view: X }), P(Ol(X) ? "Test run rejected" : "Test run dispatched");
    } catch (H) {
      P(""), A(H instanceof Error ? H.message : String(H));
    } finally {
      O("idle");
    }
  }, fr = (N) => {
    const M = we ? N.filter((H) => H.type === "select") : N;
    M.length !== 0 && k((H) => Pc(M, H));
  }, Jn = (N) => {
    we || I((M) => $c(N, M));
  }, un = (N) => !N.source || !N.target || N.source === N.target || !Ie ? !1 : !N.targetHandle, hr = (N) => {
    if (!u?.state.rootActivity || !Fe || !Ie || !un(N)) return;
    const M = Vo(N.source, N.target, N.sourceHandle ?? "Done", N.targetHandle ?? void 0), H = zc(M, E);
    I(H), Re(v, H);
  }, pr = () => {
    Re(v, E);
  }, gr = (N, M) => {
    if (!M.nodeId || M.handleType === "target") {
      _e.current = null;
      return;
    }
    _e.current = {
      nodeId: M.nodeId,
      handleId: M.handleId ?? null
    };
  }, mr = (N) => {
    const M = _e.current;
    if (_e.current = null, !M || !Ie || N.target?.closest(".react-flow__handle, .react-flow__node")) return;
    const X = t0(N);
    V({
      kind: "fromPort",
      sourceNodeId: M.nodeId,
      sourceHandleId: M.handleId,
      clientX: X.x,
      clientY: X.y
    });
  }, yr = (N, M) => {
    if (!Ie || !un(M)) return;
    const H = sg(N, {
      ...M,
      sourceHandle: M.sourceHandle ?? "Done",
      targetHandle: M.targetHandle ?? void 0
    }, E, { shouldReplaceId: !1 });
    I(H), Re(v, H);
  }, xr = (N) => {
    if (we || N.length === 0) return;
    const M = new Set(N.map((ne) => ne.id)), H = v.filter((ne) => !M.has(ne.id)), X = E.filter((ne) => !M.has(ne.source) && !M.has(ne.target));
    k(H), I(X), F && M.has(F) && _(null), Re(H, X);
  }, wr = (N) => {
    if (we || N.length === 0) return;
    const M = new Set(N.map((X) => X.id)), H = E.filter((X) => !M.has(X.id));
    I(H), Re(v, H);
  }, dn = ge((N) => {
    if (we) return;
    const M = E.filter((H) => H.id !== N);
    I(M), Re(v, M);
  }, [Re, E, we, v]), Qn = ge((N, M, H) => {
    Ie && V({ kind: "spliceEdge", edgeId: N, clientX: M, clientY: H });
  }, [Ie]), vr = (N) => {
    const M = z;
    if (!M) return;
    V(null);
    const H = nn(M.clientX, M.clientY) ?? { x: 0, y: 0 };
    if (M.kind === "fromEmpty") {
      const ne = Dt(N, H), le = [...v.map((pe) => pe.selected ? { ...pe, selected: !1 } : pe), ne.node];
      k(le), _(ne.node.id), Re(le, E, [ne.activityNode]);
      return;
    }
    if (M.kind === "fromPort") {
      const ne = v.find((Ae) => Ae.id === M.sourceNodeId), he = ne ? wa(ne) : H, le = Dt(N, he), Se = [...v.map((Ae) => Ae.selected ? { ...Ae, selected: !1 } : Ae), le.node], xt = [...E, Vo(M.sourceNodeId, le.node.id, M.sourceHandleId ?? "Done")];
      k(Se), I(xt), _(le.node.id), Re(Se, xt, [le.activityNode]);
      return;
    }
    const X = E.find((ne) => ne.id === M.edgeId);
    X && rn(N, X, H);
  }, br = me(() => ({
    highlightedEdgeId: S,
    deleteEdge: dn,
    requestInsertActivity: Qn
  }), [dn, S, Qn]), Sr = (N, M, H) => {
    m((X) => [...X, { ownerNodeId: N.nodeId, slotId: M, label: H }]), _(null);
  }, Nr = ge((N) => {
    l((M) => {
      const H = M?.state.rootActivity;
      return !M || !H ? M : {
        ...M,
        state: {
          ...M.state,
          rootActivity: Nl(H, N.nodeId, () => N)
        }
      };
    });
  }, []), Er = (N) => {
    q((M) => {
      const H = new Set(M);
      return H.has(N) ? H.delete(N) : H.add(N), H;
    });
  }, eo = (N) => {
    je((M) => M === N ? null : M), N === "palette" ? L((M) => !M) : fe((M) => !M);
  }, to = (N) => {
    N === "palette" ? L(!1) : fe(!1), je((M) => M === N ? null : N);
  }, fn = (N, M) => {
    je(null), N === "palette" ? (L(!1), oe((H) => ko(H + M, yn, xn))) : (fe(!1), Q((H) => ko(H + M, wn, vn)));
  }, no = (N, M) => {
    M.preventDefault(), je(null), N === "palette" ? L(!1) : fe(!1);
    const H = M.clientX, X = N === "palette" ? re : U, ne = N === "palette" ? yn : wn, he = N === "palette" ? xn : vn;
    document.body.classList.add("wf-side-panel-resizing");
    const le = (Se) => {
      const xt = N === "palette" ? Se.clientX - H : H - Se.clientX, Ae = ko(X + xt, ne, he);
      N === "palette" ? oe(Ae) : Q(Ae);
    }, pe = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", le), window.removeEventListener("pointerup", pe), window.removeEventListener("pointercancel", pe);
    };
    window.addEventListener("pointermove", le), window.addEventListener("pointerup", pe), window.addEventListener("pointercancel", pe);
  }, hn = (N, M) => {
    M.key === "ArrowLeft" ? (M.preventDefault(), fn(N, N === "palette" ? -mo : mo)) : M.key === "ArrowRight" ? (M.preventDefault(), fn(N, N === "palette" ? mo : -mo)) : M.key === "Home" ? (M.preventDefault(), N === "palette" ? oe(yn) : Q(wn)) : M.key === "End" && (M.preventDefault(), N === "palette" ? oe(xn) : Q(vn));
  };
  if (!s || !u)
    return /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: C || "Loading workflow editor..." });
  const kr = [
    "wf-editor-body",
    se ? "palette-collapsed" : "",
    K ? "inspector-collapsed" : "",
    ue === "palette" ? "palette-maximized" : "",
    ue === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), Cr = {
    "--wf-palette-width": `${se ? ua : re}px`,
    "--wf-inspector-width": `${K ? ua : U}px`
  }, $t = !se && ue !== "inspector", oo = !K && ue !== "palette", ro = B?.draftSignature === wt(u) ? B.view : null, io = {
    definition: s.definition,
    draft: u,
    selectedActivity: ke,
    selectedActivityDescriptor: Jt,
    selectedActivitySlots: Qt,
    catalog: f,
    currentScopeOwner: ze,
    frames: b
  }, Ri = r.map((N) => {
    const M = N.component;
    return {
      id: N.id,
      title: N.title,
      side: N.side,
      order: N.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ a.jsx(M, { context: io })
    };
  }), Ir = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ a.jsx(gl, { size: 15 }),
      render: Bl
    },
    ...Ri.filter((N) => N.side === "left")
  ].sort(ya), jr = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ a.jsx(yl, { size: 15 }),
      render: Fl
    },
    ...Ri.filter((N) => N.side === "right")
  ].sort(ya), Li = Ir.find((N) => N.id === Oe) ?? Ir[0], Vi = jr.find((N) => N.id === pt) ?? jr[0];
  function Bl() {
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
            onClick: () => Er(N.category),
            children: [
              M ? /* @__PURE__ */ a.jsx(ry, { size: 14 }) : /* @__PURE__ */ a.jsx(zt, { size: 14 }),
              /* @__PURE__ */ a.jsx("span", { children: N.category }),
              /* @__PURE__ */ a.jsx("small", { children: N.activities.length })
            ]
          }
        ),
        M ? /* @__PURE__ */ a.jsx("div", { className: "wf-palette-activities", role: "group", children: N.activities.map((H) => {
          const X = H.description?.trim(), ne = X ? `wf-palette-description-${H.activityVersionId}` : void 0, he = Ce(H), le = ci(H);
          return /* @__PURE__ */ a.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-activity",
              role: "treeitem",
              draggable: !0,
              title: X || Ce(H),
              "aria-describedby": ne,
              onClick: () => ar(H),
              onDragStart: (pe) => rr(pe, H),
              onDragEnd: (pe) => ir(pe, H),
              onPointerDown: (pe) => sr(pe, H),
              children: [
                /* @__PURE__ */ a.jsx("span", { className: "wf-activity-icon", "data-icon": le, "aria-hidden": "true", children: Hl(le) }),
                /* @__PURE__ */ a.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ a.jsx("strong", { children: he }),
                  X ? /* @__PURE__ */ a.jsx("small", { id: ne, children: X }) : null
                ] }),
                /* @__PURE__ */ a.jsx(iy, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            H.activityVersionId
          );
        }) }) : null
      ] }, N.category);
    }) });
  }
  function Fl() {
    return ke ? /* @__PURE__ */ a.jsxs("div", { className: "wf-inspector-content", children: [
      /* @__PURE__ */ a.jsx("h3", { children: v.find((N) => N.id === ke.nodeId)?.data.label ?? ke.nodeId }),
      /* @__PURE__ */ a.jsxs("dl", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Node ID" }),
        /* @__PURE__ */ a.jsx("dd", { children: ke.nodeId }),
        /* @__PURE__ */ a.jsx("dt", { children: "Activity type" }),
        /* @__PURE__ */ a.jsx("dd", { children: Jt?.typeName ?? mt.get(ke.activityVersionId)?.activityTypeKey ?? "Unknown" }),
        /* @__PURE__ */ a.jsx("dt", { children: "Activity version" }),
        /* @__PURE__ */ a.jsx("dd", { children: ke.activityVersionId })
      ] }),
      /* @__PURE__ */ a.jsx(
        sx,
        {
          activity: ke,
          descriptor: Jt,
          editors: o,
          expressionDescriptors: g,
          descriptorStatus: w,
          onChange: Nr
        }
      ),
      Qt.length > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-slot-list", children: [
        /* @__PURE__ */ a.jsx("span", { children: "Embedded slots" }),
        Qt.map((N) => /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => Sr(ke, N.id, `${v.find((M) => M.id === ke.nodeId)?.data.label ?? ke.nodeId} / ${N.label}`), children: [
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
      /* @__PURE__ */ a.jsx(zt, { size: 14 }),
      /* @__PURE__ */ a.jsx("strong", { children: s.definition.name }),
      /* @__PURE__ */ a.jsx("span", { className: "wf-chip", children: "Draft" }),
      T ? /* @__PURE__ */ a.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ a.jsx(Xn, { size: 13 }),
        " ",
        T
      ] }) : null,
      /* @__PURE__ */ a.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ a.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ a.jsx("input", { type: "checkbox", checked: Z, onChange: (N) => te(N.target.checked) }),
          /* @__PURE__ */ a.jsx("span", { children: "Autosave" })
        ] }),
        Un ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => At(n, Un, { definition: s.definition, draft: u }), children: [
          /* @__PURE__ */ a.jsx(_t, { size: 15 }),
          " Risks"
        ] }) : null,
        Gn ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => At(n, Gn, { definition: s.definition, draft: u }), children: [
          /* @__PURE__ */ a.jsx(_t, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ a.jsxs("button", { type: "button", disabled: ct, onClick: () => {
          lr();
        }, children: [
          /* @__PURE__ */ a.jsx(sy, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ a.jsxs("button", { type: "button", disabled: ct, onClick: () => {
          ur();
        }, children: [
          /* @__PURE__ */ a.jsx(ml, { size: 15 }),
          " Promote"
        ] }),
        /* @__PURE__ */ a.jsxs(
          "button",
          {
            type: "button",
            disabled: !nr,
            title: u.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              dr();
            },
            children: [
              /* @__PURE__ */ a.jsx(Ai, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    C ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(st, { size: 16 }),
      " ",
      C
    ] }) : null,
    ro ? /* @__PURE__ */ a.jsx(Qx, { testRun: ro }) : null,
    /* @__PURE__ */ a.jsxs("div", { className: kr, style: Cr, children: [
      /* @__PURE__ */ a.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ a.jsx(
            ma,
            {
              label: "Activities panel tabs",
              tabs: Ir,
              activeTabId: Li.id,
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
                onClick: () => eo("palette"),
                children: se ? /* @__PURE__ */ a.jsx(zt, { size: 14 }) : /* @__PURE__ */ a.jsx(Ro, { size: 14 })
              }
            ),
            se ? null : /* @__PURE__ */ a.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": ue === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: ue === "palette" ? "Restore" : "Maximize",
                onClick: () => to("palette"),
                children: ue === "palette" ? /* @__PURE__ */ a.jsx(Us, { size: 14 }) : /* @__PURE__ */ a.jsx(Lo, { size: 14 })
              }
            )
          ] })
        ] }),
        $t ? Li.render() : null
      ] }),
      $t && !ue ? /* @__PURE__ */ a.jsx(
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
          onPointerDown: (N) => no("palette", N),
          onKeyDown: (N) => hn("palette", N)
        }
      ) : /* @__PURE__ */ a.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ a.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-breadcrumb", children: [
          /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
            m([]), _(null);
          }, children: "Root" }),
          b.map((N, M) => /* @__PURE__ */ a.jsxs(ht.Fragment, { children: [
            /* @__PURE__ */ a.jsx(zt, { size: 13 }),
            /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
              m(b.slice(0, M + 1)), _(null);
            }, children: N.label })
          ] }, `${N.ownerNodeId}-${N.slotId}-${M}`))
        ] }),
        /* @__PURE__ */ a.jsxs("div", { className: "wf-canvas", ref: Ee, onDragOver: sn, onDragLeave: an, onDrop: cn, children: [
          /* @__PURE__ */ a.jsx(Pl.Provider, { value: br, children: /* @__PURE__ */ a.jsxs(
            al,
            {
              nodes: v,
              edges: E,
              nodeTypes: Al,
              edgeTypes: Ml,
              onInit: $,
              onNodesChange: fr,
              onEdgesChange: Jn,
              onNodesDelete: xr,
              onEdgesDelete: wr,
              onConnect: hr,
              onConnectStart: Ie ? gr : void 0,
              onConnectEnd: Ie ? mr : void 0,
              onReconnect: Ie ? yr : void 0,
              isValidConnection: un,
              onDragOver: sn,
              onDragLeave: an,
              onDrop: cn,
              onPaneClick: () => _(null),
              onNodeClick: (N, M) => _(M.id),
              onNodeDragStop: we ? void 0 : pr,
              fitView: !0,
              minZoom: 0.2,
              maxZoom: 1.8,
              nodesConnectable: Ie,
              nodesDraggable: !we,
              selectionOnDrag: !0,
              multiSelectionKeyCode: ["Shift", "Meta", "Control"],
              deleteKeyCode: we ? null : ["Backspace", "Delete"],
              panActivationKeyCode: null,
              defaultEdgeOptions: { type: "workflow" },
              children: [
                /* @__PURE__ */ a.jsx(ll, { gap: 18, size: 1 }),
                /* @__PURE__ */ a.jsx(dl, {}),
                /* @__PURE__ */ a.jsx(hl, { pannable: !0, zoomable: !0 })
              ]
            }
          ) }),
          Ie && v.length === 0 ? /* @__PURE__ */ a.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => cr(), children: [
            /* @__PURE__ */ a.jsx(Mi, { size: 15 }),
            " Add activity"
          ] }) : null,
          z ? /* @__PURE__ */ a.jsx(
            Gx,
            {
              clientX: z.clientX,
              clientY: z.clientY,
              activities: f,
              onPick: vr,
              onClose: () => V(null)
            }
          ) : null
        ] }),
        /* @__PURE__ */ a.jsx(Jx, { draft: u })
      ] }),
      oo && !ue ? /* @__PURE__ */ a.jsx(
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
          onPointerDown: (N) => no("inspector", N),
          onKeyDown: (N) => hn("inspector", N)
        }
      ) : /* @__PURE__ */ a.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ a.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ a.jsx(
            ma,
            {
              label: "Inspector panel tabs",
              tabs: jr,
              activeTabId: Vi.id,
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
                onClick: () => eo("inspector"),
                children: K ? /* @__PURE__ */ a.jsx(Ro, { size: 14 }) : /* @__PURE__ */ a.jsx(zt, { size: 14 })
              }
            ),
            K ? null : /* @__PURE__ */ a.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": ue === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: ue === "inspector" ? "Restore" : "Maximize",
                onClick: () => to("inspector"),
                children: ue === "inspector" ? /* @__PURE__ */ a.jsx(Us, { size: 14 }) : /* @__PURE__ */ a.jsx(Lo, { size: 14 })
              }
            )
          ] })
        ] }),
        oo ? Vi.render() : null
      ] })
    ] })
  ] });
}
function ma({
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
function ya(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
function Zx({ data: e, selected: t }) {
  const n = e, o = n.runtime, r = !n.suppressFlowPorts, i = r ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], s = Kx(n);
  return /* @__PURE__ */ a.jsxs(
    "div",
    {
      className: ["wf-node", t ? "selected" : "", o ? "wf-node-runtime" : "", o?.hasBlockingIncident ? "faulted" : ""].filter(Boolean).join(" "),
      "data-icon": n.icon ?? "activity",
      children: [
        r && n.acceptsInbound ? /* @__PURE__ */ a.jsx(Zt, { type: "target", position: ee.Left }) : null,
        /* @__PURE__ */ a.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ a.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: Hl(n.icon) }),
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
          o.status ? /* @__PURE__ */ a.jsx(qn, { status: o.status, subStatus: o.subStatus }) : null,
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
        i.map((c, u) => {
          const l = `${(u + 1) / (i.length + 1) * 100}%`;
          return /* @__PURE__ */ a.jsxs(ht.Fragment, { children: [
            /* @__PURE__ */ a.jsx("span", { className: "wf-node-port-label", style: { top: l }, children: c.displayName }),
            /* @__PURE__ */ a.jsx(Zt, { type: "source", position: ee.Right, id: c.name, style: { top: l } })
          ] }, c.name);
        })
      ]
    }
  );
}
function Kx(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((r) => !!r).join(" · ");
}
function Hl(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ a.jsx(ml, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ a.jsx(yl, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ a.jsx(cy, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ a.jsx(Ai, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ a.jsx(uy, { size: 15 });
    default:
      return /* @__PURE__ */ a.jsx(gl, { size: 15 });
  }
}
function Ux(e) {
  const {
    id: t,
    sourceX: n,
    sourceY: o,
    targetX: r,
    targetY: i,
    sourcePosition: s,
    targetPosition: c,
    markerEnd: u,
    style: l,
    label: f,
    labelStyle: d
  } = e, h = ht.useContext(Pl), [p, g] = Y(!1), [x, w, y] = To({ sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: c }), b = h?.highlightedEdgeId === t;
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsx(
      Wn,
      {
        id: t,
        path: x,
        markerEnd: u,
        style: {
          ...l,
          strokeWidth: b ? 2.5 : l?.strokeWidth
        },
        label: f,
        labelX: w,
        labelY: y,
        labelStyle: d,
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1)
      }
    ),
    h ? /* @__PURE__ */ a.jsx(_m, { children: /* @__PURE__ */ a.jsxs(
      "div",
      {
        className: ["wf-edge-actions", p ? "visible" : "", b ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${w}px, ${y}px)` },
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1),
        children: [
          /* @__PURE__ */ a.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (m) => h.requestInsertActivity(t, m.clientX, m.clientY), children: /* @__PURE__ */ a.jsx(Mi, { size: 12 }) }),
          /* @__PURE__ */ a.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => h.deleteEdge(t), children: /* @__PURE__ */ a.jsx(ii, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function Gx({ clientX: e, clientY: t, activities: n, onPick: o, onClose: r }) {
  const [i, s] = Y(""), [c, u] = Y(0), l = ce(null), f = ce(null), d = me(() => {
    const b = i.trim().toLowerCase(), m = n.filter(Vl);
    return b ? m.filter((v) => Ce(v).toLowerCase().includes(b) || v.activityTypeKey.toLowerCase().includes(b) || (v.category ?? "").toLowerCase().includes(b) || (v.description ?? "").toLowerCase().includes(b)) : m;
  }, [n, i]), h = me(() => zl(d), [d]), p = me(() => h.flatMap((b) => b.activities), [h]);
  ie(() => {
    requestAnimationFrame(() => f.current?.focus());
  }, []), ie(() => {
    const b = (v) => {
      l.current?.contains(v.target) || r();
    }, m = (v) => {
      v.key === "Escape" && r();
    };
    return document.addEventListener("mousedown", b, !0), document.addEventListener("keydown", m), () => {
      document.removeEventListener("mousedown", b, !0), document.removeEventListener("keydown", m);
    };
  }, [r]);
  const g = (b) => {
    if (b.key === "ArrowDown")
      b.preventDefault(), u((m) => Math.min(m + 1, p.length - 1));
    else if (b.key === "ArrowUp")
      b.preventDefault(), u((m) => Math.max(m - 1, 0));
    else if (b.key === "Enter") {
      b.preventDefault();
      const m = p[c];
      m && o(m);
    }
  }, x = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), w = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let y = -1;
  return /* @__PURE__ */ a.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: x, top: w }, onMouseDown: (b) => b.stopPropagation(), onClick: (b) => b.stopPropagation(), children: [
    /* @__PURE__ */ a.jsx(
      "input",
      {
        ref: f,
        type: "search",
        value: i,
        placeholder: "Search activities...",
        "aria-label": "Search activities",
        onChange: (b) => {
          s(b.target.value), u(0);
        },
        onKeyDown: g
      }
    ),
    /* @__PURE__ */ a.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: h.length === 0 ? /* @__PURE__ */ a.jsx("p", { children: "No matching activities." }) : h.map((b) => /* @__PURE__ */ a.jsxs("section", { children: [
      /* @__PURE__ */ a.jsx("h4", { children: b.category }),
      b.activities.map((m) => {
        y += 1;
        const v = y, k = v === c;
        return /* @__PURE__ */ a.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": k,
            className: k ? "active" : "",
            onMouseEnter: () => u(v),
            onClick: () => o(m),
            children: [
              /* @__PURE__ */ a.jsx("strong", { children: Ce(m) }),
              /* @__PURE__ */ a.jsx("small", { children: m.category || m.activityTypeKey })
            ]
          },
          m.activityVersionId
        );
      })
    ] }, b.category)) })
  ] });
}
function Jx({ draft: e }) {
  return e.validationErrors.length ? /* @__PURE__ */ a.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ a.jsx(st, { size: 14 }),
    e.validationErrors.length,
    " validation issue",
    e.validationErrors.length === 1 ? "" : "s"
  ] }) : /* @__PURE__ */ a.jsxs("div", { className: "wf-validation ok", children: [
    /* @__PURE__ */ a.jsx(Xn, { size: 14 }),
    " No validation errors"
  ] });
}
function Qx({ testRun: e }) {
  const t = Ol(e);
  return /* @__PURE__ */ a.jsxs("section", { className: "wf-test-run-capsule", "data-state": t ? "rejected" : "accepted", "aria-live": "polite", children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-test-run-heading", children: [
      t ? /* @__PURE__ */ a.jsx(st, { size: 16 }) : /* @__PURE__ */ a.jsx(Xn, { size: 16 }),
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
function xa(e) {
  return `${Ce(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function wa(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function e0(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function t0(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function wt(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function n0(e) {
  return `${e.id}-${o0(JSON.stringify(e.state))}`;
}
function o0(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function Ol(e) {
  return e.status.toLowerCase() === "rejected";
}
function rt(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function r0(e, t) {
  if (!e || !t) return "";
  const n = Date.parse(e), o = Date.parse(t);
  if (Number.isNaN(n) || Number.isNaN(o) || o < n) return "";
  const r = Math.round((o - n) / 1e3);
  if (r < 60) return `${r}s`;
  const i = Math.floor(r / 60), s = r % 60;
  if (i < 60) return s ? `${i}m ${s}s` : `${i}m`;
  const c = Math.floor(i / 60), u = i % 60;
  return u ? `${c}h ${u}m` : `${c}h`;
}
export {
  a0 as register
};
