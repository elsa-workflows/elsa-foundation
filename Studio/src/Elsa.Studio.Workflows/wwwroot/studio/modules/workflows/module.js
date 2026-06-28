import xt, { memo as Ee, forwardRef as Qo, useRef as ce, useEffect as oe, useCallback as pe, useContext as Bn, useMemo as ye, useState as Y, createContext as Ni, useLayoutEffect as Du, createElement as Qr, useId as Oa } from "react";
import "@tanstack/react-query";
function Pu(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var Rr = { exports: {} }, xn = {};
var rs;
function $u() {
  if (rs) return xn;
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
  return xn.Fragment = t, xn.jsx = n, xn.jsxs = n, xn;
}
var is;
function Tu() {
  return is || (is = 1, Rr.exports = $u()), Rr.exports;
}
var a = Tu();
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
var Ru = { value: () => {
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
function zu(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
Ao.prototype = er.prototype = {
  constructor: Ao,
  on: function(e, t) {
    var n = this._, o = zu(e + "", n), r, i = -1, s = o.length;
    if (arguments.length < 2) {
      for (; ++i < s; ) if ((r = (e = o[i]).type) && (r = Lu(n[r], e.name))) return r;
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
function Lu(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function ss(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = Ru, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var ei = "http://www.w3.org/1999/xhtml";
const as = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: ei,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function tr(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), as.hasOwnProperty(t) ? { space: as[t], local: e } : e;
}
function Vu(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === ei && t.documentElement.namespaceURI === ei ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Hu(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function Fa(e) {
  var t = tr(e);
  return (t.local ? Hu : Vu)(t);
}
function Ou() {
}
function Si(e) {
  return e == null ? Ou : function() {
    return this.querySelector(e);
  };
}
function Fu(e) {
  typeof e != "function" && (e = Si(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = new Array(s), u, l, f = 0; f < s; ++f)
      (u = i[f]) && (l = e.call(u, u.__data__, f, i)) && ("__data__" in u && (l.__data__ = u.__data__), c[f] = l);
  return new He(o, this._parents);
}
function Bu(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function Wu() {
  return [];
}
function Ba(e) {
  return e == null ? Wu : function() {
    return this.querySelectorAll(e);
  };
}
function Xu(e) {
  return function() {
    return Bu(e.apply(this, arguments));
  };
}
function Yu(e) {
  typeof e == "function" ? e = Xu(e) : e = Ba(e);
  for (var t = this._groups, n = t.length, o = [], r = [], i = 0; i < n; ++i)
    for (var s = t[i], c = s.length, u, l = 0; l < c; ++l)
      (u = s[l]) && (o.push(e.call(u, u.__data__, l, s)), r.push(u));
  return new He(o, r);
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
var qu = Array.prototype.find;
function Ku(e) {
  return function() {
    return qu.call(this.children, e);
  };
}
function Uu() {
  return this.firstElementChild;
}
function Zu(e) {
  return this.select(e == null ? Uu : Ku(typeof e == "function" ? e : Xa(e)));
}
var Gu = Array.prototype.filter;
function Ju() {
  return Array.from(this.children);
}
function Qu(e) {
  return function() {
    return Gu.call(this.children, e);
  };
}
function ed(e) {
  return this.selectAll(e == null ? Ju : Qu(typeof e == "function" ? e : Xa(e)));
}
function td(e) {
  typeof e != "function" && (e = Wa(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = [], u, l = 0; l < s; ++l)
      (u = i[l]) && e.call(u, u.__data__, l, i) && c.push(u);
  return new He(o, this._parents);
}
function Ya(e) {
  return new Array(e.length);
}
function nd() {
  return new He(this._enter || this._groups.map(Ya), this._parents);
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
function od(e) {
  return function() {
    return e;
  };
}
function rd(e, t, n, o, r, i) {
  for (var s = 0, c, u = t.length, l = i.length; s < l; ++s)
    (c = t[s]) ? (c.__data__ = i[s], o[s] = c) : n[s] = new Lo(e, i[s]);
  for (; s < u; ++s)
    (c = t[s]) && (r[s] = c);
}
function id(e, t, n, o, r, i, s) {
  var c, u, l = /* @__PURE__ */ new Map(), f = t.length, d = i.length, h = new Array(f), p;
  for (c = 0; c < f; ++c)
    (u = t[c]) && (h[c] = p = s.call(u, u.__data__, c, t) + "", l.has(p) ? r[c] = u : l.set(p, u));
  for (c = 0; c < d; ++c)
    p = s.call(e, i[c], c, i) + "", (u = l.get(p)) ? (o[c] = u, u.__data__ = i[c], l.delete(p)) : n[c] = new Lo(e, i[c]);
  for (c = 0; c < f; ++c)
    (u = t[c]) && l.get(h[c]) === u && (r[c] = u);
}
function sd(e) {
  return e.__data__;
}
function ad(e, t) {
  if (!arguments.length) return Array.from(this, sd);
  var n = t ? id : rd, o = this._parents, r = this._groups;
  typeof e != "function" && (e = od(e));
  for (var i = r.length, s = new Array(i), c = new Array(i), u = new Array(i), l = 0; l < i; ++l) {
    var f = o[l], d = r[l], h = d.length, p = cd(e.call(f, f && f.__data__, l, o)), g = p.length, v = c[l] = new Array(g), x = s[l] = new Array(g), m = u[l] = new Array(h);
    n(f, d, v, x, m, p, t);
    for (var k = 0, y = 0, w, b; k < g; ++k)
      if (w = v[k]) {
        for (k >= y && (y = k + 1); !(b = x[y]) && ++y < g; ) ;
        w._next = b || null;
      }
  }
  return s = new He(s, o), s._enter = c, s._exit = u, s;
}
function cd(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function ld() {
  return new He(this._exit || this._groups.map(Ya), this._parents);
}
function ud(e, t, n) {
  var o = this.enter(), r = this, i = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? i.remove() : n(i), o && r ? o.merge(r).order() : r;
}
function dd(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, i = o.length, s = Math.min(r, i), c = new Array(r), u = 0; u < s; ++u)
    for (var l = n[u], f = o[u], d = l.length, h = c[u] = new Array(d), p, g = 0; g < d; ++g)
      (p = l[g] || f[g]) && (h[g] = p);
  for (; u < r; ++u)
    c[u] = n[u];
  return new He(c, this._parents);
}
function fd() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, i = o[r], s; --r >= 0; )
      (s = o[r]) && (i && s.compareDocumentPosition(i) ^ 4 && i.parentNode.insertBefore(s, i), i = s);
  return this;
}
function hd(e) {
  e || (e = pd);
  function t(d, h) {
    return d && h ? e(d.__data__, h.__data__) : !d - !h;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), i = 0; i < o; ++i) {
    for (var s = n[i], c = s.length, u = r[i] = new Array(c), l, f = 0; f < c; ++f)
      (l = s[f]) && (u[f] = l);
    u.sort(t);
  }
  return new He(r, this._parents).order();
}
function pd(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function gd() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function yd() {
  return Array.from(this);
}
function md() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length; r < i; ++r) {
      var s = o[r];
      if (s) return s;
    }
  return null;
}
function xd() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function wd() {
  return !this.node();
}
function vd(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], i = 0, s = r.length, c; i < s; ++i)
      (c = r[i]) && e.call(c, c.__data__, i, r);
  return this;
}
function bd(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Nd(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Sd(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function Ed(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function kd(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function Id(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function Cd(e, t) {
  var n = tr(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? Nd : bd : typeof t == "function" ? n.local ? Id : kd : n.local ? Ed : Sd)(n, t));
}
function qa(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function jd(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function _d(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function Ad(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function Md(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? jd : typeof t == "function" ? Ad : _d)(e, t, n ?? "")) : Ut(this.node(), e);
}
function Ut(e, t) {
  return e.style.getPropertyValue(t) || qa(e).getComputedStyle(e, null).getPropertyValue(t);
}
function Dd(e) {
  return function() {
    delete this[e];
  };
}
function Pd(e, t) {
  return function() {
    this[e] = t;
  };
}
function $d(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function Td(e, t) {
  return arguments.length > 1 ? this.each((t == null ? Dd : typeof t == "function" ? $d : Pd)(e, t)) : this.node()[e];
}
function Ka(e) {
  return e.trim().split(/^|\s+/);
}
function Ei(e) {
  return e.classList || new Ua(e);
}
function Ua(e) {
  this._node = e, this._names = Ka(e.getAttribute("class") || "");
}
Ua.prototype = {
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
function Za(e, t) {
  for (var n = Ei(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function Ga(e, t) {
  for (var n = Ei(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function Rd(e) {
  return function() {
    Za(this, e);
  };
}
function zd(e) {
  return function() {
    Ga(this, e);
  };
}
function Ld(e, t) {
  return function() {
    (t.apply(this, arguments) ? Za : Ga)(this, e);
  };
}
function Vd(e, t) {
  var n = Ka(e + "");
  if (arguments.length < 2) {
    for (var o = Ei(this.node()), r = -1, i = n.length; ++r < i; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? Ld : t ? Rd : zd)(n, t));
}
function Hd() {
  this.textContent = "";
}
function Od(e) {
  return function() {
    this.textContent = e;
  };
}
function Fd(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function Bd(e) {
  return arguments.length ? this.each(e == null ? Hd : (typeof e == "function" ? Fd : Od)(e)) : this.node().textContent;
}
function Wd() {
  this.innerHTML = "";
}
function Xd(e) {
  return function() {
    this.innerHTML = e;
  };
}
function Yd(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function qd(e) {
  return arguments.length ? this.each(e == null ? Wd : (typeof e == "function" ? Yd : Xd)(e)) : this.node().innerHTML;
}
function Kd() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function Ud() {
  return this.each(Kd);
}
function Zd() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Gd() {
  return this.each(Zd);
}
function Jd(e) {
  var t = typeof e == "function" ? e : Fa(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Qd() {
  return null;
}
function ef(e, t) {
  var n = typeof e == "function" ? e : Fa(e), o = t == null ? Qd : typeof t == "function" ? t : Si(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function tf() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function nf() {
  return this.each(tf);
}
function of() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function rf() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function sf(e) {
  return this.select(e ? rf : of);
}
function af(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function cf(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function lf(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function uf(e) {
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
    var o = this.__on, r, i = cf(t);
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
function ff(e, t, n) {
  var o = lf(e + ""), r, i = o.length, s;
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
  for (c = t ? df : uf, r = 0; r < i; ++r) this.each(c(o[r], t, n));
  return this;
}
function Ja(e, t, n) {
  var o = qa(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function hf(e, t) {
  return function() {
    return Ja(this, e, t);
  };
}
function pf(e, t) {
  return function() {
    return Ja(this, e, t.apply(this, arguments));
  };
}
function gf(e, t) {
  return this.each((typeof t == "function" ? pf : hf)(e, t));
}
function* yf() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length, s; r < i; ++r)
      (s = o[r]) && (yield s);
}
var Qa = [null];
function He(e, t) {
  this._groups = e, this._parents = t;
}
function Wn() {
  return new He([[document.documentElement]], Qa);
}
function mf() {
  return this;
}
He.prototype = Wn.prototype = {
  constructor: He,
  select: Fu,
  selectAll: Yu,
  selectChild: Zu,
  selectChildren: ed,
  filter: td,
  data: ad,
  enter: nd,
  exit: ld,
  join: ud,
  merge: dd,
  selection: mf,
  order: fd,
  sort: hd,
  call: gd,
  nodes: yd,
  node: md,
  size: xd,
  empty: wd,
  each: vd,
  attr: Cd,
  style: Md,
  property: Td,
  classed: Vd,
  text: Bd,
  html: qd,
  raise: Ud,
  lower: Gd,
  append: Jd,
  insert: ef,
  remove: nf,
  clone: sf,
  datum: af,
  on: ff,
  dispatch: gf,
  [Symbol.iterator]: yf
};
function Ve(e) {
  return typeof e == "string" ? new He([[document.querySelector(e)]], [document.documentElement]) : new He([[e]], Qa);
}
function xf(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Ye(e, t) {
  if (e = xf(e), t === void 0 && (t = e.currentTarget), t) {
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
const wf = { passive: !1 }, Dn = { capture: !0, passive: !1 };
function zr(e) {
  e.stopImmediatePropagation();
}
function Yt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function ec(e) {
  var t = e.document.documentElement, n = Ve(e).on("dragstart.drag", Yt, Dn);
  "onselectstart" in t ? n.on("selectstart.drag", Yt, Dn) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function tc(e, t) {
  var n = e.document.documentElement, o = Ve(e).on("dragstart.drag", null);
  t && (o.on("click.drag", Yt, Dn), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const wo = (e) => () => e;
function ti(e, {
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
ti.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function vf(e) {
  return !e.ctrlKey && !e.button;
}
function bf() {
  return this.parentNode;
}
function Nf(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function Sf() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function nc() {
  var e = vf, t = bf, n = Nf, o = Sf, r = {}, i = er("start", "drag", "end"), s = 0, c, u, l, f, d = 0;
  function h(w) {
    w.on("mousedown.drag", p).filter(o).on("touchstart.drag", x).on("touchmove.drag", m, wf).on("touchend.drag touchcancel.drag", k).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function p(w, b) {
    if (!(f || !e.call(this, w, b))) {
      var I = y(this, t.call(this, w, b), w, b, "mouse");
      I && (Ve(w.view).on("mousemove.drag", g, Dn).on("mouseup.drag", v, Dn), ec(w.view), zr(w), l = !1, c = w.clientX, u = w.clientY, I("start", w));
    }
  }
  function g(w) {
    if (Yt(w), !l) {
      var b = w.clientX - c, I = w.clientY - u;
      l = b * b + I * I > d;
    }
    r.mouse("drag", w);
  }
  function v(w) {
    Ve(w.view).on("mousemove.drag mouseup.drag", null), tc(w.view, l), Yt(w), r.mouse("end", w);
  }
  function x(w, b) {
    if (e.call(this, w, b)) {
      var I = w.changedTouches, E = t.call(this, w, b), j = I.length, P, W;
      for (P = 0; P < j; ++P)
        (W = y(this, E, w, b, I[P].identifier, I[P])) && (zr(w), W("start", w, I[P]));
    }
  }
  function m(w) {
    var b = w.changedTouches, I = b.length, E, j;
    for (E = 0; E < I; ++E)
      (j = r[b[E].identifier]) && (Yt(w), j("drag", w, b[E]));
  }
  function k(w) {
    var b = w.changedTouches, I = b.length, E, j;
    for (f && clearTimeout(f), f = setTimeout(function() {
      f = null;
    }, 500), E = 0; E < I; ++E)
      (j = r[b[E].identifier]) && (zr(w), j("end", w, b[E]));
  }
  function y(w, b, I, E, j, P) {
    var W = i.copy(), M = Ye(P || I, b), R, O, N;
    if ((N = n.call(w, new ti("beforestart", {
      sourceEvent: I,
      target: h,
      identifier: j,
      active: s,
      x: M[0],
      y: M[1],
      dx: 0,
      dy: 0,
      dispatch: W
    }), E)) != null)
      return R = N.x - M[0] || 0, O = N.y - M[1] || 0, function _(C, D, $) {
        var T = M, H;
        switch (C) {
          case "start":
            r[j] = _, H = s++;
            break;
          case "end":
            delete r[j], --s;
          // falls through
          case "drag":
            M = Ye($ || D, b), H = s;
            break;
        }
        W.call(
          C,
          w,
          new ti(C, {
            sourceEvent: D,
            subject: N,
            target: h,
            identifier: j,
            active: H,
            x: M[0] + R,
            y: M[1] + O,
            dx: M[0] - T[0],
            dy: M[1] - T[1],
            dispatch: W
          }),
          E
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
    return arguments.length ? (d = (w = +w) * w, h) : Math.sqrt(d);
  }, h;
}
function ki(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function oc(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function Xn() {
}
var Pn = 0.7, Vo = 1 / Pn, qt = "\\s*([+-]?\\d+)\\s*", $n = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", nt = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", Ef = /^#([0-9a-f]{3,8})$/, kf = new RegExp(`^rgb\\(${qt},${qt},${qt}\\)$`), If = new RegExp(`^rgb\\(${nt},${nt},${nt}\\)$`), Cf = new RegExp(`^rgba\\(${qt},${qt},${qt},${$n}\\)$`), jf = new RegExp(`^rgba\\(${nt},${nt},${nt},${$n}\\)$`), _f = new RegExp(`^hsl\\(${$n},${nt},${nt}\\)$`), Af = new RegExp(`^hsla\\(${$n},${nt},${nt},${$n}\\)$`), cs = {
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
ki(Xn, Mt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: ls,
  // Deprecated! Use color.formatHex.
  formatHex: ls,
  formatHex8: Mf,
  formatHsl: Df,
  formatRgb: us,
  toString: us
});
function ls() {
  return this.rgb().formatHex();
}
function Mf() {
  return this.rgb().formatHex8();
}
function Df() {
  return rc(this).formatHsl();
}
function us() {
  return this.rgb().formatRgb();
}
function Mt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = Ef.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? ds(t) : n === 3 ? new $e(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? vo(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? vo(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = kf.exec(e)) ? new $e(t[1], t[2], t[3], 1) : (t = If.exec(e)) ? new $e(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = Cf.exec(e)) ? vo(t[1], t[2], t[3], t[4]) : (t = jf.exec(e)) ? vo(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = _f.exec(e)) ? ps(t[1], t[2] / 100, t[3] / 100, 1) : (t = Af.exec(e)) ? ps(t[1], t[2] / 100, t[3] / 100, t[4]) : cs.hasOwnProperty(e) ? ds(cs[e]) : e === "transparent" ? new $e(NaN, NaN, NaN, 0) : null;
}
function ds(e) {
  return new $e(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function vo(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new $e(e, t, n, o);
}
function Pf(e) {
  return e instanceof Xn || (e = Mt(e)), e ? (e = e.rgb(), new $e(e.r, e.g, e.b, e.opacity)) : new $e();
}
function ni(e, t, n, o) {
  return arguments.length === 1 ? Pf(e) : new $e(e, t, n, o ?? 1);
}
function $e(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
ki($e, ni, oc(Xn, {
  brighter(e) {
    return e = e == null ? Vo : Math.pow(Vo, e), new $e(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Pn : Math.pow(Pn, e), new $e(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new $e(jt(this.r), jt(this.g), jt(this.b), Ho(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: fs,
  // Deprecated! Use color.formatHex.
  formatHex: fs,
  formatHex8: $f,
  formatRgb: hs,
  toString: hs
}));
function fs() {
  return `#${Ct(this.r)}${Ct(this.g)}${Ct(this.b)}`;
}
function $f() {
  return `#${Ct(this.r)}${Ct(this.g)}${Ct(this.b)}${Ct((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function hs() {
  const e = Ho(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${jt(this.r)}, ${jt(this.g)}, ${jt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Ho(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function jt(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function Ct(e) {
  return e = jt(e), (e < 16 ? "0" : "") + e.toString(16);
}
function ps(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new qe(e, t, n, o);
}
function rc(e) {
  if (e instanceof qe) return new qe(e.h, e.s, e.l, e.opacity);
  if (e instanceof Xn || (e = Mt(e)), !e) return new qe();
  if (e instanceof qe) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), i = Math.max(t, n, o), s = NaN, c = i - r, u = (i + r) / 2;
  return c ? (t === i ? s = (n - o) / c + (n < o) * 6 : n === i ? s = (o - t) / c + 2 : s = (t - n) / c + 4, c /= u < 0.5 ? i + r : 2 - i - r, s *= 60) : c = u > 0 && u < 1 ? 0 : s, new qe(s, c, u, e.opacity);
}
function Tf(e, t, n, o) {
  return arguments.length === 1 ? rc(e) : new qe(e, t, n, o ?? 1);
}
function qe(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
ki(qe, Tf, oc(Xn, {
  brighter(e) {
    return e = e == null ? Vo : Math.pow(Vo, e), new qe(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Pn : Math.pow(Pn, e), new qe(this.h, this.s, this.l * e, this.opacity);
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
function Lr(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const Ii = (e) => () => e;
function Rf(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function zf(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function Lf(e) {
  return (e = +e) == 1 ? ic : function(t, n) {
    return n - t ? zf(t, n, e) : Ii(isNaN(t) ? n : t);
  };
}
function ic(e, t) {
  var n = t - e;
  return n ? Rf(e, n) : Ii(isNaN(e) ? t : e);
}
const Oo = (function e(t) {
  var n = Lf(t);
  function o(r, i) {
    var s = n((r = ni(r)).r, (i = ni(i)).r), c = n(r.g, i.g), u = n(r.b, i.b), l = ic(r.opacity, i.opacity);
    return function(f) {
      return r.r = s(f), r.g = c(f), r.b = u(f), r.opacity = l(f), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function Vf(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(i) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - i) + t[r] * i;
    return o;
  };
}
function Hf(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Of(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), i = new Array(n), s;
  for (s = 0; s < o; ++s) r[s] = _n(e[s], t[s]);
  for (; s < n; ++s) i[s] = t[s];
  return function(c) {
    for (s = 0; s < o; ++s) i[s] = r[s](c);
    return i;
  };
}
function Ff(e, t) {
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
function Bf(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = _n(e[r], t[r]) : o[r] = t[r];
  return function(i) {
    for (r in n) o[r] = n[r](i);
    return o;
  };
}
var oi = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, Vr = new RegExp(oi.source, "g");
function Wf(e) {
  return function() {
    return e;
  };
}
function Xf(e) {
  return function(t) {
    return e(t) + "";
  };
}
function sc(e, t) {
  var n = oi.lastIndex = Vr.lastIndex = 0, o, r, i, s = -1, c = [], u = [];
  for (e = e + "", t = t + ""; (o = oi.exec(e)) && (r = Vr.exec(t)); )
    (i = r.index) > n && (i = t.slice(n, i), c[s] ? c[s] += i : c[++s] = i), (o = o[0]) === (r = r[0]) ? c[s] ? c[s] += r : c[++s] = r : (c[++s] = null, u.push({ i: s, x: tt(o, r) })), n = Vr.lastIndex;
  return n < t.length && (i = t.slice(n), c[s] ? c[s] += i : c[++s] = i), c.length < 2 ? u[0] ? Xf(u[0].x) : Wf(t) : (t = u.length, function(l) {
    for (var f = 0, d; f < t; ++f) c[(d = u[f]).i] = d.x(l);
    return c.join("");
  });
}
function _n(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? Ii(t) : (n === "number" ? tt : n === "string" ? (o = Mt(t)) ? (t = o, Oo) : sc : t instanceof Mt ? Oo : t instanceof Date ? Ff : Hf(t) ? Vf : Array.isArray(t) ? Of : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Bf : tt)(e, t);
}
var ys = 180 / Math.PI, ri = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function ac(e, t, n, o, r, i) {
  var s, c, u;
  return (s = Math.sqrt(e * e + t * t)) && (e /= s, t /= s), (u = e * n + t * o) && (n -= e * u, o -= t * u), (c = Math.sqrt(n * n + o * o)) && (n /= c, o /= c, u /= c), e * o < t * n && (e = -e, t = -t, u = -u, s = -s), {
    translateX: r,
    translateY: i,
    rotate: Math.atan2(t, e) * ys,
    skewX: Math.atan(u) * ys,
    scaleX: s,
    scaleY: c
  };
}
var No;
function Yf(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? ri : ac(t.a, t.b, t.c, t.d, t.e, t.f);
}
function qf(e) {
  return e == null || (No || (No = document.createElementNS("http://www.w3.org/2000/svg", "g")), No.setAttribute("transform", e), !(e = No.transform.baseVal.consolidate())) ? ri : (e = e.matrix, ac(e.a, e.b, e.c, e.d, e.e, e.f));
}
function cc(e, t, n, o) {
  function r(l) {
    return l.length ? l.pop() + " " : "";
  }
  function i(l, f, d, h, p, g) {
    if (l !== d || f !== h) {
      var v = p.push("translate(", null, t, null, n);
      g.push({ i: v - 4, x: tt(l, d) }, { i: v - 2, x: tt(f, h) });
    } else (d || h) && p.push("translate(" + d + t + h + n);
  }
  function s(l, f, d, h) {
    l !== f ? (l - f > 180 ? f += 360 : f - l > 180 && (l += 360), h.push({ i: d.push(r(d) + "rotate(", null, o) - 2, x: tt(l, f) })) : f && d.push(r(d) + "rotate(" + f + o);
  }
  function c(l, f, d, h) {
    l !== f ? h.push({ i: d.push(r(d) + "skewX(", null, o) - 2, x: tt(l, f) }) : f && d.push(r(d) + "skewX(" + f + o);
  }
  function u(l, f, d, h, p, g) {
    if (l !== d || f !== h) {
      var v = p.push(r(p) + "scale(", null, ",", null, ")");
      g.push({ i: v - 4, x: tt(l, d) }, { i: v - 2, x: tt(f, h) });
    } else (d !== 1 || h !== 1) && p.push(r(p) + "scale(" + d + "," + h + ")");
  }
  return function(l, f) {
    var d = [], h = [];
    return l = e(l), f = e(f), i(l.translateX, l.translateY, f.translateX, f.translateY, d, h), s(l.rotate, f.rotate, d, h), c(l.skewX, f.skewX, d, h), u(l.scaleX, l.scaleY, f.scaleX, f.scaleY, d, h), l = f = null, function(p) {
      for (var g = -1, v = h.length, x; ++g < v; ) d[(x = h[g]).i] = x.x(p);
      return d.join("");
    };
  };
}
var Kf = cc(Yf, "px, ", "px)", "deg)"), Uf = cc(qf, ", ", ")", ")"), Zf = 1e-12;
function ms(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Gf(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Jf(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const Mo = (function e(t, n, o) {
  function r(i, s) {
    var c = i[0], u = i[1], l = i[2], f = s[0], d = s[1], h = s[2], p = f - c, g = d - u, v = p * p + g * g, x, m;
    if (v < Zf)
      m = Math.log(h / l) / t, x = function(E) {
        return [
          c + E * p,
          u + E * g,
          l * Math.exp(t * E * m)
        ];
      };
    else {
      var k = Math.sqrt(v), y = (h * h - l * l + o * v) / (2 * l * n * k), w = (h * h - l * l - o * v) / (2 * h * n * k), b = Math.log(Math.sqrt(y * y + 1) - y), I = Math.log(Math.sqrt(w * w + 1) - w);
      m = (I - b) / t, x = function(E) {
        var j = E * m, P = ms(b), W = l / (n * k) * (P * Jf(t * j + b) - Gf(b));
        return [
          c + W * p,
          u + W * g,
          l * P / ms(t * j + b)
        ];
      };
    }
    return x.duration = m * 1e3 * t / Math.SQRT2, x;
  }
  return r.rho = function(i) {
    var s = Math.max(1e-3, +i), c = s * s, u = c * c;
    return e(s, c, u);
  }, r;
})(Math.SQRT2, 2, 4);
var Zt = 0, In = 0, wn = 0, lc = 1e3, Fo, Cn, Bo = 0, Dt = 0, nr = 0, Tn = typeof performance == "object" && performance.now ? performance : Date, uc = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function Ci() {
  return Dt || (uc(Qf), Dt = Tn.now() + nr);
}
function Qf() {
  Dt = 0;
}
function Wo() {
  this._call = this._time = this._next = null;
}
Wo.prototype = dc.prototype = {
  constructor: Wo,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? Ci() : +n) + (t == null ? 0 : +t), !this._next && Cn !== this && (Cn ? Cn._next = this : Fo = this, Cn = this), this._call = e, this._time = n, ii();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, ii());
  }
};
function dc(e, t, n) {
  var o = new Wo();
  return o.restart(e, t, n), o;
}
function eh() {
  Ci(), ++Zt;
  for (var e = Fo, t; e; )
    (t = Dt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --Zt;
}
function xs() {
  Dt = (Bo = Tn.now()) + nr, Zt = In = 0;
  try {
    eh();
  } finally {
    Zt = 0, nh(), Dt = 0;
  }
}
function th() {
  var e = Tn.now(), t = e - Bo;
  t > lc && (nr -= t, Bo = e);
}
function nh() {
  for (var e, t = Fo, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Fo = n);
  Cn = e, ii(o);
}
function ii(e) {
  if (!Zt) {
    In && (In = clearTimeout(In));
    var t = e - Dt;
    t > 24 ? (e < 1 / 0 && (In = setTimeout(xs, e - Tn.now() - nr)), wn && (wn = clearInterval(wn))) : (wn || (Bo = Tn.now(), wn = setInterval(th, lc)), Zt = 1, uc(xs));
  }
}
function ws(e, t, n) {
  var o = new Wo();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var oh = er("start", "end", "cancel", "interrupt"), rh = [], fc = 0, vs = 1, si = 2, Do = 3, bs = 4, ai = 5, Po = 6;
function or(e, t, n, o, r, i) {
  var s = e.__transition;
  if (!s) e.__transition = {};
  else if (n in s) return;
  ih(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: oh,
    tween: rh,
    time: i.time,
    delay: i.delay,
    duration: i.duration,
    ease: i.ease,
    timer: null,
    state: fc
  });
}
function ji(e, t) {
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
function ih(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = dc(i, 0, n.time);
  function i(l) {
    n.state = vs, n.timer.restart(s, n.delay, n.time), n.delay <= l && s(l - n.delay);
  }
  function s(l) {
    var f, d, h, p;
    if (n.state !== vs) return u();
    for (f in o)
      if (p = o[f], p.name === n.name) {
        if (p.state === Do) return ws(s);
        p.state === bs ? (p.state = Po, p.timer.stop(), p.on.call("interrupt", e, e.__data__, p.index, p.group), delete o[f]) : +f < t && (p.state = Po, p.timer.stop(), p.on.call("cancel", e, e.__data__, p.index, p.group), delete o[f]);
      }
    if (ws(function() {
      n.state === Do && (n.state = bs, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = si, n.on.call("start", e, e.__data__, n.index, n.group), n.state === si) {
      for (n.state = Do, r = new Array(h = n.tween.length), f = 0, d = -1; f < h; ++f)
        (p = n.tween[f].value.call(e, e.__data__, n.index, n.group)) && (r[++d] = p);
      r.length = d + 1;
    }
  }
  function c(l) {
    for (var f = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(u), n.state = ai, 1), d = -1, h = r.length; ++d < h; )
      r[d].call(e, f);
    n.state === ai && (n.on.call("end", e, e.__data__, n.index, n.group), u());
  }
  function u() {
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
      r = o.state > si && o.state < ai, o.state = Po, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[s];
    }
    i && delete e.__transition;
  }
}
function sh(e) {
  return this.each(function() {
    $o(this, e);
  });
}
function ah(e, t) {
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
function ch(e, t, n) {
  var o, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var i = ot(this, e), s = i.tween;
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
function lh(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = Qe(this.node(), n).tween, r = 0, i = o.length, s; r < i; ++r)
      if ((s = o[r]).name === e)
        return s.value;
    return null;
  }
  return this.each((t == null ? ah : ch)(n, e, t));
}
function _i(e, t, n) {
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
  return (typeof t == "number" ? tt : t instanceof Mt ? Oo : (n = Mt(t)) ? (t = n, Oo) : sc)(e, t);
}
function uh(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function dh(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function fh(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttribute(e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function hh(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttributeNS(e.space, e.local);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function ph(e, t, n) {
  var o, r, i;
  return function() {
    var s, c = n(this), u;
    return c == null ? void this.removeAttribute(e) : (s = this.getAttribute(e), u = c + "", s === u ? null : s === o && u === r ? i : (r = u, i = t(o = s, c)));
  };
}
function gh(e, t, n) {
  var o, r, i;
  return function() {
    var s, c = n(this), u;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (s = this.getAttributeNS(e.space, e.local), u = c + "", s === u ? null : s === o && u === r ? i : (r = u, i = t(o = s, c)));
  };
}
function yh(e, t) {
  var n = tr(e), o = n === "transform" ? Uf : hc;
  return this.attrTween(e, typeof t == "function" ? (n.local ? gh : ph)(n, o, _i(this, "attr." + e, t)) : t == null ? (n.local ? dh : uh)(n) : (n.local ? hh : fh)(n, o, t));
}
function mh(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function xh(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function wh(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && xh(e, i)), n;
  }
  return r._value = t, r;
}
function vh(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && mh(e, i)), n;
  }
  return r._value = t, r;
}
function bh(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = tr(e);
  return this.tween(n, (o.local ? wh : vh)(o, t));
}
function Nh(e, t) {
  return function() {
    ji(this, e).delay = +t.apply(this, arguments);
  };
}
function Sh(e, t) {
  return t = +t, function() {
    ji(this, e).delay = t;
  };
}
function Eh(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Nh : Sh)(t, e)) : Qe(this.node(), t).delay;
}
function kh(e, t) {
  return function() {
    ot(this, e).duration = +t.apply(this, arguments);
  };
}
function Ih(e, t) {
  return t = +t, function() {
    ot(this, e).duration = t;
  };
}
function Ch(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? kh : Ih)(t, e)) : Qe(this.node(), t).duration;
}
function jh(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    ot(this, e).ease = t;
  };
}
function _h(e) {
  var t = this._id;
  return arguments.length ? this.each(jh(t, e)) : Qe(this.node(), t).ease;
}
function Ah(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    ot(this, e).ease = n;
  };
}
function Mh(e) {
  if (typeof e != "function") throw new Error();
  return this.each(Ah(this._id, e));
}
function Dh(e) {
  typeof e != "function" && (e = Wa(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = [], u, l = 0; l < s; ++l)
      (u = i[l]) && e.call(u, u.__data__, l, i) && c.push(u);
  return new at(o, this._parents, this._name, this._id);
}
function Ph(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, i = Math.min(o, r), s = new Array(o), c = 0; c < i; ++c)
    for (var u = t[c], l = n[c], f = u.length, d = s[c] = new Array(f), h, p = 0; p < f; ++p)
      (h = u[p] || l[p]) && (d[p] = h);
  for (; c < o; ++c)
    s[c] = t[c];
  return new at(s, this._parents, this._name, this._id);
}
function $h(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function Th(e, t, n) {
  var o, r, i = $h(t) ? ji : ot;
  return function() {
    var s = i(this, e), c = s.on;
    c !== o && (r = (o = c).copy()).on(t, n), s.on = r;
  };
}
function Rh(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Qe(this.node(), n).on.on(e) : this.each(Th(n, e, t));
}
function zh(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function Lh() {
  return this.on("end.remove", zh(this._id));
}
function Vh(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Si(e));
  for (var o = this._groups, r = o.length, i = new Array(r), s = 0; s < r; ++s)
    for (var c = o[s], u = c.length, l = i[s] = new Array(u), f, d, h = 0; h < u; ++h)
      (f = c[h]) && (d = e.call(f, f.__data__, h, c)) && ("__data__" in f && (d.__data__ = f.__data__), l[h] = d, or(l[h], t, n, h, l, Qe(f, n)));
  return new at(i, this._parents, t, n);
}
function Hh(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Ba(e));
  for (var o = this._groups, r = o.length, i = [], s = [], c = 0; c < r; ++c)
    for (var u = o[c], l = u.length, f, d = 0; d < l; ++d)
      if (f = u[d]) {
        for (var h = e.call(f, f.__data__, d, u), p, g = Qe(f, n), v = 0, x = h.length; v < x; ++v)
          (p = h[v]) && or(p, t, n, v, h, g);
        i.push(h), s.push(f);
      }
  return new at(i, s, t, n);
}
var Oh = Wn.prototype.constructor;
function Fh() {
  return new Oh(this._groups, this._parents);
}
function Bh(e, t) {
  var n, o, r;
  return function() {
    var i = Ut(this, e), s = (this.style.removeProperty(e), Ut(this, e));
    return i === s ? null : i === n && s === o ? r : r = t(n = i, o = s);
  };
}
function pc(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Wh(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = Ut(this, e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Xh(e, t, n) {
  var o, r, i;
  return function() {
    var s = Ut(this, e), c = n(this), u = c + "";
    return c == null && (u = c = (this.style.removeProperty(e), Ut(this, e))), s === u ? null : s === o && u === r ? i : (r = u, i = t(o = s, c));
  };
}
function Yh(e, t) {
  var n, o, r, i = "style." + t, s = "end." + i, c;
  return function() {
    var u = ot(this, e), l = u.on, f = u.value[i] == null ? c || (c = pc(t)) : void 0;
    (l !== n || r !== f) && (o = (n = l).copy()).on(s, r = f), u.on = o;
  };
}
function qh(e, t, n) {
  var o = (e += "") == "transform" ? Kf : hc;
  return t == null ? this.styleTween(e, Bh(e, o)).on("end.style." + e, pc(e)) : typeof t == "function" ? this.styleTween(e, Xh(e, o, _i(this, "style." + e, t))).each(Yh(this._id, e)) : this.styleTween(e, Wh(e, o, t), n).on("end.style." + e, null);
}
function Kh(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function Uh(e, t, n) {
  var o, r;
  function i() {
    var s = t.apply(this, arguments);
    return s !== r && (o = (r = s) && Kh(e, s, n)), o;
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
function Gh(e) {
  return function() {
    this.textContent = e;
  };
}
function Jh(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function Qh(e) {
  return this.tween("text", typeof e == "function" ? Jh(_i(this, "text", e)) : Gh(e == null ? "" : e + ""));
}
function ep(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function tp(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && ep(r)), t;
  }
  return o._value = e, o;
}
function np(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, tp(e));
}
function op() {
  for (var e = this._name, t = this._id, n = gc(), o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], c = s.length, u, l = 0; l < c; ++l)
      if (u = s[l]) {
        var f = Qe(u, t);
        or(u, e, n, l, s, {
          time: f.time + f.delay + f.duration,
          delay: 0,
          duration: f.duration,
          ease: f.ease
        });
      }
  return new at(o, this._parents, e, n);
}
function rp() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(i, s) {
    var c = { value: s }, u = { value: function() {
      --r === 0 && i();
    } };
    n.each(function() {
      var l = ot(this, o), f = l.on;
      f !== e && (t = (e = f).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(u)), l.on = t;
    }), r === 0 && i();
  });
}
var ip = 0;
function at(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function gc() {
  return ++ip;
}
var it = Wn.prototype;
at.prototype = {
  constructor: at,
  select: Vh,
  selectAll: Hh,
  selectChild: it.selectChild,
  selectChildren: it.selectChildren,
  filter: Dh,
  merge: Ph,
  selection: Fh,
  transition: op,
  call: it.call,
  nodes: it.nodes,
  node: it.node,
  size: it.size,
  empty: it.empty,
  each: it.each,
  on: Rh,
  attr: yh,
  attrTween: bh,
  style: qh,
  styleTween: Zh,
  text: Qh,
  textTween: np,
  remove: Lh,
  tween: lh,
  delay: Eh,
  duration: Ch,
  ease: _h,
  easeVarying: Mh,
  end: rp,
  [Symbol.iterator]: it[Symbol.iterator]
};
function sp(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var ap = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: sp
};
function cp(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function lp(e) {
  var t, n;
  e instanceof at ? (t = e._id, e = e._name) : (t = gc(), (n = ap).time = Ci(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], c = s.length, u, l = 0; l < c; ++l)
      (u = s[l]) && or(u, e, t, l, s, n || cp(u, t));
  return new at(o, this._parents, e, t);
}
Wn.prototype.interrupt = sh;
Wn.prototype.transition = lp;
const So = (e) => () => e;
function up(e, {
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
function st(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
st.prototype = {
  constructor: st,
  scale: function(e) {
    return e === 1 ? this : new st(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new st(this.k, this.x + this.k * e, this.y + this.k * t);
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
var rr = new st(1, 0, 0);
yc.prototype = st.prototype;
function yc(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return rr;
  return e.__zoom;
}
function Hr(e) {
  e.stopImmediatePropagation();
}
function vn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function dp(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function fp() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function Ns() {
  return this.__zoom || rr;
}
function hp(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function pp() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function gp(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], i = e.invertY(t[0][1]) - n[0][1], s = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    s > i ? (i + s) / 2 : Math.min(0, i) || Math.max(0, s)
  );
}
function mc() {
  var e = dp, t = fp, n = gp, o = hp, r = pp, i = [0, 1 / 0], s = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, u = Mo, l = er("start", "zoom", "end"), f, d, h, p = 500, g = 150, v = 0, x = 10;
  function m(N) {
    N.property("__zoom", Ns).on("wheel.zoom", j, { passive: !1 }).on("mousedown.zoom", P).on("dblclick.zoom", W).filter(r).on("touchstart.zoom", M).on("touchmove.zoom", R).on("touchend.zoom touchcancel.zoom", O).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  m.transform = function(N, _, C, D) {
    var $ = N.selection ? N.selection() : N;
    $.property("__zoom", Ns), N !== $ ? b(N, _, C, D) : $.interrupt().each(function() {
      I(this, arguments).event(D).start().zoom(null, typeof _ == "function" ? _.apply(this, arguments) : _).end();
    });
  }, m.scaleBy = function(N, _, C, D) {
    m.scaleTo(N, function() {
      var $ = this.__zoom.k, T = typeof _ == "function" ? _.apply(this, arguments) : _;
      return $ * T;
    }, C, D);
  }, m.scaleTo = function(N, _, C, D) {
    m.transform(N, function() {
      var $ = t.apply(this, arguments), T = this.__zoom, H = C == null ? w($) : typeof C == "function" ? C.apply(this, arguments) : C, B = T.invert(H), F = typeof _ == "function" ? _.apply(this, arguments) : _;
      return n(y(k(T, F), H, B), $, s);
    }, C, D);
  }, m.translateBy = function(N, _, C, D) {
    m.transform(N, function() {
      return n(this.__zoom.translate(
        typeof _ == "function" ? _.apply(this, arguments) : _,
        typeof C == "function" ? C.apply(this, arguments) : C
      ), t.apply(this, arguments), s);
    }, null, D);
  }, m.translateTo = function(N, _, C, D, $) {
    m.transform(N, function() {
      var T = t.apply(this, arguments), H = this.__zoom, B = D == null ? w(T) : typeof D == "function" ? D.apply(this, arguments) : D;
      return n(rr.translate(B[0], B[1]).scale(H.k).translate(
        typeof _ == "function" ? -_.apply(this, arguments) : -_,
        typeof C == "function" ? -C.apply(this, arguments) : -C
      ), T, s);
    }, D, $);
  };
  function k(N, _) {
    return _ = Math.max(i[0], Math.min(i[1], _)), _ === N.k ? N : new st(_, N.x, N.y);
  }
  function y(N, _, C) {
    var D = _[0] - C[0] * N.k, $ = _[1] - C[1] * N.k;
    return D === N.x && $ === N.y ? N : new st(N.k, D, $);
  }
  function w(N) {
    return [(+N[0][0] + +N[1][0]) / 2, (+N[0][1] + +N[1][1]) / 2];
  }
  function b(N, _, C, D) {
    N.on("start.zoom", function() {
      I(this, arguments).event(D).start();
    }).on("interrupt.zoom end.zoom", function() {
      I(this, arguments).event(D).end();
    }).tween("zoom", function() {
      var $ = this, T = arguments, H = I($, T).event(D), B = t.apply($, T), F = C == null ? w(B) : typeof C == "function" ? C.apply($, T) : C, Z = Math.max(B[1][0] - B[0][0], B[1][1] - B[0][1]), q = $.__zoom, te = typeof _ == "function" ? _.apply($, T) : _, ae = u(q.invert(F).concat(Z / q.k), te.invert(F).concat(Z / te.k));
      return function(Q) {
        if (Q === 1) Q = te;
        else {
          var z = ae(Q), K = Z / z[2];
          Q = new st(K, F[0] - z[0] * K, F[1] - z[1] * K);
        }
        H.zoom(null, Q);
      };
    });
  }
  function I(N, _, C) {
    return !C && N.__zooming || new E(N, _);
  }
  function E(N, _) {
    this.that = N, this.args = _, this.active = 0, this.sourceEvent = null, this.extent = t.apply(N, _), this.taps = 0;
  }
  E.prototype = {
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
      var _ = Ve(this.that).datum();
      l.call(
        N,
        this.that,
        new up(N, {
          sourceEvent: this.sourceEvent,
          target: m,
          transform: this.that.__zoom,
          dispatch: l
        }),
        _
      );
    }
  };
  function j(N, ..._) {
    if (!e.apply(this, arguments)) return;
    var C = I(this, _).event(N), D = this.__zoom, $ = Math.max(i[0], Math.min(i[1], D.k * Math.pow(2, o.apply(this, arguments)))), T = Ye(N);
    if (C.wheel)
      (C.mouse[0][0] !== T[0] || C.mouse[0][1] !== T[1]) && (C.mouse[1] = D.invert(C.mouse[0] = T)), clearTimeout(C.wheel);
    else {
      if (D.k === $) return;
      C.mouse = [T, D.invert(T)], $o(this), C.start();
    }
    vn(N), C.wheel = setTimeout(H, g), C.zoom("mouse", n(y(k(D, $), C.mouse[0], C.mouse[1]), C.extent, s));
    function H() {
      C.wheel = null, C.end();
    }
  }
  function P(N, ..._) {
    if (h || !e.apply(this, arguments)) return;
    var C = N.currentTarget, D = I(this, _, !0).event(N), $ = Ve(N.view).on("mousemove.zoom", F, !0).on("mouseup.zoom", Z, !0), T = Ye(N, C), H = N.clientX, B = N.clientY;
    ec(N.view), Hr(N), D.mouse = [T, this.__zoom.invert(T)], $o(this), D.start();
    function F(q) {
      if (vn(q), !D.moved) {
        var te = q.clientX - H, ae = q.clientY - B;
        D.moved = te * te + ae * ae > v;
      }
      D.event(q).zoom("mouse", n(y(D.that.__zoom, D.mouse[0] = Ye(q, C), D.mouse[1]), D.extent, s));
    }
    function Z(q) {
      $.on("mousemove.zoom mouseup.zoom", null), tc(q.view, D.moved), vn(q), D.event(q).end();
    }
  }
  function W(N, ..._) {
    if (e.apply(this, arguments)) {
      var C = this.__zoom, D = Ye(N.changedTouches ? N.changedTouches[0] : N, this), $ = C.invert(D), T = C.k * (N.shiftKey ? 0.5 : 2), H = n(y(k(C, T), D, $), t.apply(this, _), s);
      vn(N), c > 0 ? Ve(this).transition().duration(c).call(b, H, D, N) : Ve(this).call(m.transform, H, D, N);
    }
  }
  function M(N, ..._) {
    if (e.apply(this, arguments)) {
      var C = N.touches, D = C.length, $ = I(this, _, N.changedTouches.length === D).event(N), T, H, B, F;
      for (Hr(N), H = 0; H < D; ++H)
        B = C[H], F = Ye(B, this), F = [F, this.__zoom.invert(F), B.identifier], $.touch0 ? !$.touch1 && $.touch0[2] !== F[2] && ($.touch1 = F, $.taps = 0) : ($.touch0 = F, T = !0, $.taps = 1 + !!f);
      f && (f = clearTimeout(f)), T && ($.taps < 2 && (d = F[0], f = setTimeout(function() {
        f = null;
      }, p)), $o(this), $.start());
    }
  }
  function R(N, ..._) {
    if (this.__zooming) {
      var C = I(this, _).event(N), D = N.changedTouches, $ = D.length, T, H, B, F;
      for (vn(N), T = 0; T < $; ++T)
        H = D[T], B = Ye(H, this), C.touch0 && C.touch0[2] === H.identifier ? C.touch0[0] = B : C.touch1 && C.touch1[2] === H.identifier && (C.touch1[0] = B);
      if (H = C.that.__zoom, C.touch1) {
        var Z = C.touch0[0], q = C.touch0[1], te = C.touch1[0], ae = C.touch1[1], Q = (Q = te[0] - Z[0]) * Q + (Q = te[1] - Z[1]) * Q, z = (z = ae[0] - q[0]) * z + (z = ae[1] - q[1]) * z;
        H = k(H, Math.sqrt(Q / z)), B = [(Z[0] + te[0]) / 2, (Z[1] + te[1]) / 2], F = [(q[0] + ae[0]) / 2, (q[1] + ae[1]) / 2];
      } else if (C.touch0) B = C.touch0[0], F = C.touch0[1];
      else return;
      C.zoom("touch", n(y(H, B, F), C.extent, s));
    }
  }
  function O(N, ..._) {
    if (this.__zooming) {
      var C = I(this, _).event(N), D = N.changedTouches, $ = D.length, T, H;
      for (Hr(N), h && clearTimeout(h), h = setTimeout(function() {
        h = null;
      }, p), T = 0; T < $; ++T)
        H = D[T], C.touch0 && C.touch0[2] === H.identifier ? delete C.touch0 : C.touch1 && C.touch1[2] === H.identifier && delete C.touch1;
      if (C.touch1 && !C.touch0 && (C.touch0 = C.touch1, delete C.touch1), C.touch0) C.touch0[1] = this.__zoom.invert(C.touch0[0]);
      else if (C.end(), C.taps === 2 && (H = Ye(H, this), Math.hypot(d[0] - H[0], d[1] - H[1]) < x)) {
        var B = Ve(this).on("dblclick.zoom");
        B && B.apply(this, arguments);
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
    return arguments.length ? (u = N, m) : u;
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
var Gt;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(Gt || (Gt = {}));
var _t;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(_t || (_t = {}));
var zn;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(zn || (zn = {}));
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
var ne;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(ne || (ne = {}));
const Ss = {
  [ne.Left]: ne.Right,
  [ne.Right]: ne.Left,
  [ne.Top]: ne.Bottom,
  [ne.Bottom]: ne.Top
};
function bc(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Nc = (e) => "id" in e && "source" in e && "target" in e, yp = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), Ai = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), Yn = (e, t = [0, 0]) => {
  const { width: n, height: o } = ct(e), r = e.origin ?? t, i = n * r[0], s = o * r[1];
  return {
    x: e.position.x - i,
    y: e.position.y - s
  };
}, mp = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const i = typeof r == "string";
    let s = !t.nodeLookup && !i ? r : void 0;
    t.nodeLookup && (s = i ? t.nodeLookup.get(r) : Ai(r) ? r : t.nodeLookup.get(r.id));
    const c = s ? Yo(s, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return ir(o, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return sr(n);
}, qn = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = ir(n, Yo(r)), o = !0);
  }), o ? sr(n) : { x: 0, y: 0, width: 0, height: 0 };
}, Mi = (e, t, [n, o, r] = [0, 0, 1], i = !1, s = !1) => {
  const c = {
    ...rn(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, u = [];
  for (const l of e.values()) {
    const { measured: f, selectable: d = !0, hidden: h = !1 } = l;
    if (s && !d || h)
      continue;
    const p = f.width ?? l.width ?? l.initialWidth ?? null, g = f.height ?? l.height ?? l.initialHeight ?? null, v = Ln(c, Qt(l)), x = (p ?? 0) * (g ?? 0), m = i && v > 0;
    (!l.internals.handleBounds || m || v >= x || l.dragging) && u.push(l);
  }
  return u;
}, xp = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function wp(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function vp({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: i }, s) {
  if (e.size === 0)
    return !0;
  const c = wp(e, s), u = qn(c), l = Pi(u, t, n, s?.minZoom ?? r, s?.maxZoom ?? i, s?.padding ?? 0.1);
  return await o.setViewport(l, {
    duration: s?.duration,
    ease: s?.ease,
    interpolate: s?.interpolate
  }), !0;
}
function Sc({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: i }) {
  const s = n.get(e), c = s.parentId ? n.get(s.parentId) : void 0, { x: u, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, f = s.origin ?? o;
  let d = s.extent || r;
  if (s.extent === "parent" && !s.expandParent)
    if (!c)
      i?.("005", Oe.error005());
    else {
      const p = c.measured.width, g = c.measured.height;
      p && g && (d = [
        [u, l],
        [u + p, l + g]
      ]);
    }
  else c && $t(s.extent) && (d = [
    [s.extent[0][0] + u, s.extent[0][1] + l],
    [s.extent[1][0] + u, s.extent[1][1] + l]
  ]);
  const h = $t(d) ? Pt(t, d, s.measured) : t;
  return (s.measured.width === void 0 || s.measured.height === void 0) && i?.("015", Oe.error015()), {
    position: {
      x: h.x - u + (s.measured.width ?? 0) * f[0],
      y: h.y - l + (s.measured.height ?? 0) * f[1]
    },
    positionAbsolute: h
  };
}
async function bp({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const i = new Set(e.map((h) => h.id)), s = [];
  for (const h of n) {
    if (h.deletable === !1)
      continue;
    const p = i.has(h.id), g = !p && h.parentId && s.find((v) => v.id === h.parentId);
    (p || g) && s.push(h);
  }
  const c = new Set(t.map((h) => h.id)), u = o.filter((h) => h.deletable !== !1), f = xp(s, u);
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
const Jt = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), Pt = (e = { x: 0, y: 0 }, t, n) => ({
  x: Jt(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: Jt(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Ec(e, t, n) {
  const { width: o, height: r } = ct(n), { x: i, y: s } = n.internals.positionAbsolute;
  return Pt(e, [
    [i, s],
    [i + o, s + r]
  ], t);
}
const Es = (e, t, n) => e < t ? Jt(Math.abs(e - t), 1, t) / t : e > n ? -Jt(Math.abs(e - n), 1, t) / t : 0, Di = (e, t, n = 15, o = 40) => {
  const r = Es(e.x, o, t.width - o) * n, i = Es(e.y, o, t.height - o) * n;
  return [r, i];
}, ir = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), ci = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), sr = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), Qt = (e, t = [0, 0]) => {
  const { x: n, y: o } = Ai(e) ? e.internals.positionAbsolute : Yn(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, Yo = (e, t = [0, 0]) => {
  const { x: n, y: o } = Ai(e) ? e.internals.positionAbsolute : Yn(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, kc = (e, t) => sr(ir(ci(e), ci(t))), Ln = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, ks = (e) => Ke(e.width) && Ke(e.height) && Ke(e.x) && Ke(e.y), Ke = (e) => !isNaN(e) && isFinite(e), Ic = (e, t) => (n, o) => {
}, Kn = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), rn = ({ x: e, y: t }, [n, o, r], i = !1, s = [1, 1]) => {
  const c = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return i ? Kn(c, s) : c;
}, en = ({ x: e, y: t }, [n, o, r]) => ({
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
function Np(e, t, n) {
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
function Sp(e, t, n, o, r, i) {
  const { x: s, y: c } = en(e, [t, n, o]), { x: u, y: l } = en({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), f = r - u, d = i - l;
  return {
    left: Math.floor(s),
    top: Math.floor(c),
    right: Math.floor(f),
    bottom: Math.floor(d)
  };
}
const Pi = (e, t, n, o, r, i) => {
  const s = Np(i, t, n), c = (t - s.x) / e.width, u = (n - s.y) / e.height, l = Math.min(c, u), f = Jt(l, o, r), d = e.x + e.width / 2, h = e.y + e.height / 2, p = t / 2 - d * f, g = n / 2 - h * f, v = Sp(e, p, g, f, t, n), x = {
    left: Math.min(v.left - s.left, 0),
    top: Math.min(v.top - s.top, 0),
    right: Math.min(v.right - s.right, 0),
    bottom: Math.min(v.bottom - s.bottom, 0)
  };
  return {
    x: p - x.left + x.right,
    y: g - x.top + x.bottom,
    zoom: f
  };
}, Vn = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function $t(e) {
  return e != null && e !== "parent";
}
function ct(e) {
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
function Ep() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function kp(e) {
  return { ...wc, ...e || {} };
}
function An(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: i, y: s } = Ue(e), c = rn({ x: i - (r?.left ?? 0), y: s - (r?.top ?? 0) }, o), { x: u, y: l } = n ? Kn(c, t) : c;
  return {
    xSnapped: u,
    ySnapped: l,
    ...c
  };
}
const $i = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), _c = (e) => e?.getRootNode?.() || window?.document, Ip = ["INPUT", "SELECT", "TEXTAREA"];
function Ac(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : Ip.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const Mc = (e) => "clientX" in e, Ue = (e, t) => {
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
      ...$i(s)
    };
  });
};
function Dc({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: i, targetControlX: s, targetControlY: c }) {
  const u = e * 0.125 + r * 0.375 + s * 0.375 + n * 0.125, l = t * 0.125 + i * 0.375 + c * 0.375 + o * 0.125, f = Math.abs(u - e), d = Math.abs(l - t);
  return [u, l, f, d];
}
function Eo(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function js({ pos: e, x1: t, y1: n, x2: o, y2: r, c: i }) {
  switch (e) {
    case ne.Left:
      return [t - Eo(t - o, i), n];
    case ne.Right:
      return [t + Eo(o - t, i), n];
    case ne.Top:
      return [t, n - Eo(n - r, i)];
    case ne.Bottom:
      return [t, n + Eo(r - n, i)];
  }
}
function Pc({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: r, targetPosition: i = ne.Top, curvature: s = 0.25 }) {
  const [c, u] = js({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: s
  }), [l, f] = js({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: s
  }), [d, h, p, g] = Dc({
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
function $c({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, i = n < e ? n + r : n - r, s = Math.abs(o - t) / 2, c = o < t ? o + s : o - s;
  return [i, c, r, s];
}
function Cp({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: i = "basic" }) {
  if (i === "manual")
    return o;
  const s = r && n ? o + 1e3 : o, c = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return s + c;
}
function jp({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const i = ir(Yo(e), Yo(t));
  i.x === i.x2 && (i.x2 += 1), i.y === i.y2 && (i.y2 += 1);
  const s = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return Ln(s, sr(i)) > 0;
}
const Tc = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, _p = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), Ap = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Oe.error006()), t;
  const o = n.getEdgeId || Tc;
  let r;
  return Nc(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, _p(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, Mp = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: r, ...i } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", Oe.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return o.onError?.("007", Oe.error007(r)), n;
  const c = o.getEdgeId || Tc, u = {
    ...i,
    id: o.shouldReplaceId ? c(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== r).concat(u);
};
function Rc({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, i, s, c] = $c({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, i, s, c];
}
const _s = {
  [ne.Left]: { x: -1, y: 0 },
  [ne.Right]: { x: 1, y: 0 },
  [ne.Top]: { x: 0, y: -1 },
  [ne.Bottom]: { x: 0, y: 1 }
}, Dp = ({ source: e, sourcePosition: t = ne.Bottom, target: n }) => t === ne.Left || t === ne.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, As = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Pp({ source: e, sourcePosition: t = ne.Bottom, target: n, targetPosition: o = ne.Top, center: r, offset: i, stepPosition: s }) {
  const c = _s[t], u = _s[o], l = { x: e.x + c.x * i, y: e.y + c.y * i }, f = { x: n.x + u.x * i, y: n.y + u.y * i }, d = Dp({
    source: l,
    sourcePosition: t,
    target: f
  }), h = d.x !== 0 ? "x" : "y", p = d[h];
  let g = [], v, x;
  const m = { x: 0, y: 0 }, k = { x: 0, y: 0 }, [, , y, w] = $c({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[h] * u[h] === -1) {
    h === "x" ? (v = r.x ?? l.x + (f.x - l.x) * s, x = r.y ?? (l.y + f.y) / 2) : (v = r.x ?? (l.x + f.x) / 2, x = r.y ?? l.y + (f.y - l.y) * s);
    const j = [
      { x: v, y: l.y },
      { x: v, y: f.y }
    ], P = [
      { x: l.x, y: x },
      { x: f.x, y: x }
    ];
    c[h] === p ? g = h === "x" ? j : P : g = h === "x" ? P : j;
  } else {
    const j = [{ x: l.x, y: f.y }], P = [{ x: f.x, y: l.y }];
    if (h === "x" ? g = c.x === p ? P : j : g = c.y === p ? j : P, t === o) {
      const N = Math.abs(e[h] - n[h]);
      if (N <= i) {
        const _ = Math.min(i - 1, i - N);
        c[h] === p ? m[h] = (l[h] > e[h] ? -1 : 1) * _ : k[h] = (f[h] > n[h] ? -1 : 1) * _;
      }
    }
    if (t !== o) {
      const N = h === "x" ? "y" : "x", _ = c[h] === u[N], C = l[N] > f[N], D = l[N] < f[N];
      (c[h] === 1 && (!_ && C || _ && D) || c[h] !== 1 && (!_ && D || _ && C)) && (g = h === "x" ? j : P);
    }
    const W = { x: l.x + m.x, y: l.y + m.y }, M = { x: f.x + k.x, y: f.y + k.y }, R = Math.max(Math.abs(W.x - g[0].x), Math.abs(M.x - g[0].x)), O = Math.max(Math.abs(W.y - g[0].y), Math.abs(M.y - g[0].y));
    R >= O ? (v = (W.x + M.x) / 2, x = g[0].y) : (v = g[0].x, x = (W.y + M.y) / 2);
  }
  const b = { x: l.x + m.x, y: l.y + m.y }, I = { x: f.x + k.x, y: f.y + k.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...b.x !== g[0].x || b.y !== g[0].y ? [b] : [],
    ...g,
    ...I.x !== g[g.length - 1].x || I.y !== g[g.length - 1].y ? [I] : [],
    n
  ], v, x, y, w];
}
function $p(e, t, n, o) {
  const r = Math.min(As(e, t) / 2, As(t, n) / 2, o), { x: i, y: s } = t;
  if (e.x === i && i === n.x || e.y === s && s === n.y)
    return `L${i} ${s}`;
  if (e.y === s) {
    const l = e.x < n.x ? -1 : 1, f = e.y < n.y ? 1 : -1;
    return `L ${i + r * l},${s}Q ${i},${s} ${i},${s + r * f}`;
  }
  const c = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${i},${s + r * u}Q ${i},${s} ${i + r * c},${s}`;
}
function qo({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: r, targetPosition: i = ne.Top, borderRadius: s = 5, centerX: c, centerY: u, offset: l = 20, stepPosition: f = 0.5 }) {
  const [d, h, p, g, v] = Pp({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: i,
    center: { x: c, y: u },
    offset: l,
    stepPosition: f
  });
  let x = `M${d[0].x} ${d[0].y}`;
  for (let m = 1; m < d.length - 1; m++)
    x += $p(d[m - 1], d[m], d[m + 1], s);
  return x += `L${d[d.length - 1].x} ${d[d.length - 1].y}`, [x, h, p, g, v];
}
function Ms(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function Tp(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!Ms(t) || !Ms(n))
    return null;
  const o = t.internals.handleBounds || Ds(t.handles), r = n.internals.handleBounds || Ds(n.handles), i = Ps(o?.source ?? [], e.sourceHandle), s = Ps(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === Gt.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!i || !s)
    return e.onError?.("008", Oe.error008(i ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = i?.position || ne.Bottom, u = s?.position || ne.Top, l = Tt(t, i, c), f = Tt(n, s, u);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: f.x,
    targetY: f.y,
    sourcePosition: c,
    targetPosition: u
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
function Tt(e, t, n = ne.Left, o = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, i = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: s, height: c } = t ?? ct(e);
  if (o)
    return { x: r + s / 2, y: i + c / 2 };
  switch (t?.position ?? n) {
    case ne.Top:
      return { x: r + s / 2, y: i };
    case ne.Right:
      return { x: r + s, y: i + c / 2 };
    case ne.Bottom:
      return { x: r + s / 2, y: i + c };
    case ne.Left:
      return { x: r, y: i + c / 2 };
  }
}
function Ps(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function li(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function Rp(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const i = /* @__PURE__ */ new Set();
  return e.reduce((s, c) => ([c.markerStart || o, c.markerEnd || r].forEach((u) => {
    if (u && typeof u == "object") {
      const l = li(u, t);
      i.has(l) || (s.push({ id: l, color: u.color || n, ...u }), i.add(l));
    }
  }), s), []).sort((s, c) => s.id.localeCompare(c.id));
}
const zc = 1e3, zp = 10, Ti = {
  nodeOrigin: [0, 0],
  nodeExtent: Rn,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Lp = {
  ...Ti,
  checkEquality: !0
};
function Ri(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function Vp(e, t, n) {
  const o = Ri(Ti, n);
  for (const r of e.values())
    if (r.parentId)
      Li(r, e, t, o);
    else {
      const i = Yn(r, o.nodeOrigin), s = $t(r.extent) ? r.extent : o.nodeExtent, c = Pt(i, s, ct(r));
      r.internals.positionAbsolute = c;
    }
}
function Hp(e, t) {
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
  const r = Ri(Lp, o), i = { i: 0 }, s = new Map(t), c = r?.elevateNodesOnSelect && !zi(r.zIndexMode) ? zc : 0;
  let u = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const f of e) {
    let d = s.get(f.id);
    if (r.checkEquality && f === d?.internals.userNode)
      t.set(f.id, d);
    else {
      const h = Yn(f, r.nodeOrigin), p = $t(f.extent) ? f.extent : r.nodeExtent, g = Pt(h, p, ct(f));
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
          handleBounds: Hp(f, d),
          z: Lc(f, c, r.zIndexMode),
          userNode: f
        }
      }, t.set(f.id, d);
    }
    (d.measured === void 0 || d.measured.width === void 0 || d.measured.height === void 0) && !d.hidden && (u = !1), f.parentId && Li(d, t, n, o, i), l ||= f.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: l };
}
function Op(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function Li(e, t, n, o, r) {
  const { elevateNodesOnSelect: i, nodeOrigin: s, nodeExtent: c, zIndexMode: u } = Ri(Ti, o), l = e.parentId, f = t.get(l);
  if (!f) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Op(e, n), r && !f.parentId && f.internals.rootParentIndex === void 0 && u === "auto" && (f.internals.rootParentIndex = ++r.i, f.internals.z = f.internals.z + r.i * zp), r && f.internals.rootParentIndex !== void 0 && (r.i = f.internals.rootParentIndex);
  const d = i && !zi(u) ? zc : 0, { x: h, y: p, z: g } = Fp(e, f, s, c, d, u), { positionAbsolute: v } = e.internals, x = h !== v.x || p !== v.y;
  (x || g !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: x ? { x: h, y: p } : v,
      z: g
    }
  });
}
function Lc(e, t, n) {
  const o = Ke(e.zIndex) ? e.zIndex : 0;
  return zi(n) ? o : o + (e.selected ? t : 0);
}
function Fp(e, t, n, o, r, i) {
  const { x: s, y: c } = t.internals.positionAbsolute, u = ct(e), l = Yn(e, n), f = $t(e.extent) ? Pt(l, e.extent, u) : l;
  let d = Pt({ x: s + f.x, y: c + f.y }, o, u);
  e.extent === "parent" && (d = Ec(d, u, t));
  const h = Lc(e, r, i), p = t.internals.z ?? 0;
  return {
    x: d.x,
    y: d.y,
    z: p >= h ? p + 1 : h
  };
}
function Vi(e, t, n, o = [0, 0]) {
  const r = [], i = /* @__PURE__ */ new Map();
  for (const s of e) {
    const c = t.get(s.parentId);
    if (!c)
      continue;
    const u = i.get(s.parentId)?.expandedRect ?? Qt(c), l = kc(u, s.rect);
    i.set(s.parentId, { expandedRect: l, parent: c });
  }
  return i.size > 0 && i.forEach(({ expandedRect: s, parent: c }, u) => {
    const l = c.internals.positionAbsolute, f = ct(c), d = c.origin ?? o, h = s.x < l.x ? Math.round(Math.abs(l.x - s.x)) : 0, p = s.y < l.y ? Math.round(Math.abs(l.y - s.y)) : 0, g = Math.max(f.width, Math.round(s.width)), v = Math.max(f.height, Math.round(s.height)), x = (g - f.width) * d[0], m = (v - f.height) * d[1];
    (h > 0 || p > 0 || x || m) && (r.push({
      id: u,
      type: "position",
      position: {
        x: c.position.x - h + x,
        y: c.position.y - p + m
      }
    }), n.get(u)?.forEach((k) => {
      e.some((y) => y.id === k.id) || r.push({
        id: k.id,
        type: "position",
        position: {
          x: k.position.x + h,
          y: k.position.y + p
        }
      });
    })), (f.width < s.width || f.height < s.height || h || p) && r.push({
      id: u,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: g + (h ? d[0] * h - x : 0),
        height: v + (p ? d[1] * p - m : 0)
      }
    });
  }), r;
}
function Bp(e, t, n, o, r, i, s) {
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
    const v = $i(p.nodeElement), x = g.measured.width !== v.width || g.measured.height !== v.height;
    if (!!(v.width && v.height && (x || !g.internals.handleBounds || p.force))) {
      const k = p.nodeElement.getBoundingClientRect(), y = $t(g.extent) ? g.extent : i;
      let { positionAbsolute: w } = g.internals;
      g.parentId && g.extent === "parent" ? w = Ec(w, v, t.get(g.parentId)) : y && (w = Pt(w, y, v));
      const b = {
        ...g,
        measured: v,
        internals: {
          ...g.internals,
          positionAbsolute: w,
          handleBounds: {
            source: Cs("source", p.nodeElement, k, d, g.id),
            target: Cs("target", p.nodeElement, k, d, g.id)
          }
        }
      };
      t.set(g.id, b), g.parentId && Li(b, t, n, { nodeOrigin: r, zIndexMode: s }), u = !0, x && (l.push({
        id: g.id,
        type: "dimensions",
        dimensions: v
      }), g.expandParent && g.parentId && h.push({
        id: g.id,
        parentId: g.parentId,
        rect: Qt(b, r)
      }));
    }
  }
  if (h.length > 0) {
    const p = Vi(h, t, n, r);
    l.push(...p);
  }
  return { changes: l, updatedInternals: u };
}
async function Wp({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: i }) {
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
  const u = o.get(s) || /* @__PURE__ */ new Map();
  if (o.set(s, u.set(n, t)), i) {
    s = `${r}-${e}-${i}`;
    const l = o.get(s) || /* @__PURE__ */ new Map();
    o.set(s, l.set(n, t));
  }
}
function Vc(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: i, sourceHandle: s = null, targetHandle: c = null } = o, u = { edgeId: o.id, source: r, target: i, sourceHandle: s, targetHandle: c }, l = `${r}-${s}--${i}-${c}`, f = `${i}-${c}--${r}-${s}`;
    $s("source", u, f, e, r, s), $s("target", u, l, e, i, c), t.set(o.id, o);
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
function Xp(e, t, n, o) {
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
function Or({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
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
function Yp({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const i = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, s = Kn(i, t);
  return {
    x: s.x - i.x,
    y: s.y - i.y
  };
}
function qp({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let i = { x: null, y: null }, s = 0, c = /* @__PURE__ */ new Map(), u = !1, l = { x: 0, y: 0 }, f = null, d = !1, h = null, p = !1, g = !1, v = null;
  function x({ noDragClassName: k, handleSelector: y, domNode: w, isSelectable: b, nodeId: I, nodeClickDistance: E = 0 }) {
    h = Ve(w);
    function j({ x: R, y: O }) {
      const { nodeLookup: N, nodeExtent: _, snapGrid: C, snapToGrid: D, nodeOrigin: $, onNodeDrag: T, onSelectionDrag: H, onError: B, updateNodePositions: F } = t();
      i = { x: R, y: O };
      let Z = !1;
      const q = c.size > 1, te = q && _ ? ci(qn(c)) : null, ae = q && D ? Yp({
        dragItems: c,
        snapGrid: C,
        x: R,
        y: O
      }) : null;
      for (const [Q, z] of c) {
        if (!N.has(Q))
          continue;
        let K = { x: R - z.distance.x, y: O - z.distance.y };
        D && (K = ae ? {
          x: Math.round(K.x + ae.x),
          y: Math.round(K.y + ae.y)
        } : Kn(K, C));
        let se = null;
        if (q && _ && !z.extent && te) {
          const { positionAbsolute: ee } = z.internals, le = ee.x - te.x + _[0][0], L = ee.x + z.measured.width - te.x2 + _[1][0], G = ee.y - te.y + _[0][1], fe = ee.y + z.measured.height - te.y2 + _[1][1];
          se = [
            [le, G],
            [L, fe]
          ];
        }
        const { position: re, positionAbsolute: U } = Sc({
          nodeId: Q,
          nextPosition: K,
          nodeLookup: N,
          nodeExtent: se || _,
          nodeOrigin: $,
          onError: B
        });
        Z = Z || z.position.x !== re.x || z.position.y !== re.y, z.position = re, z.internals.positionAbsolute = U;
      }
      if (g = g || Z, !!Z && (F(c, !0), v && (o || T || !I && H))) {
        const [Q, z] = Or({
          nodeId: I,
          dragItems: c,
          nodeLookup: N
        });
        o?.(v, c, Q, z), T?.(v, Q, z), I || H?.(v, z);
      }
    }
    async function P() {
      if (!f)
        return;
      const { transform: R, panBy: O, autoPanSpeed: N, autoPanOnNodeDrag: _ } = t();
      if (!_) {
        u = !1, cancelAnimationFrame(s);
        return;
      }
      const [C, D] = Di(l, f, N);
      (C !== 0 || D !== 0) && (i.x = (i.x ?? 0) - C / R[2], i.y = (i.y ?? 0) - D / R[2], await O({ x: C, y: D }) && j(i)), s = requestAnimationFrame(P);
    }
    function W(R) {
      const { nodeLookup: O, multiSelectionActive: N, nodesDraggable: _, transform: C, snapGrid: D, snapToGrid: $, selectNodesOnDrag: T, onNodeDragStart: H, onSelectionDragStart: B, unselectNodesAndEdges: F } = t();
      d = !0, (!T || !b) && !N && I && (O.get(I)?.selected || F()), b && T && I && e?.(I);
      const Z = An(R.sourceEvent, { transform: C, snapGrid: D, snapToGrid: $, containerBounds: f });
      if (i = Z, c = Xp(O, _, Z, I), c.size > 0 && (n || H || !I && B)) {
        const [q, te] = Or({
          nodeId: I,
          dragItems: c,
          nodeLookup: O
        });
        n?.(R.sourceEvent, c, q, te), H?.(R.sourceEvent, q, te), I || B?.(R.sourceEvent, te);
      }
    }
    const M = nc().clickDistance(E).on("start", (R) => {
      const { domNode: O, nodeDragThreshold: N, transform: _, snapGrid: C, snapToGrid: D } = t();
      f = O?.getBoundingClientRect() || null, p = !1, g = !1, v = R.sourceEvent, N === 0 && W(R), i = An(R.sourceEvent, { transform: _, snapGrid: C, snapToGrid: D, containerBounds: f }), l = Ue(R.sourceEvent, f);
    }).on("drag", (R) => {
      const { autoPanOnNodeDrag: O, transform: N, snapGrid: _, snapToGrid: C, nodeDragThreshold: D, nodeLookup: $ } = t(), T = An(R.sourceEvent, { transform: N, snapGrid: _, snapToGrid: C, containerBounds: f });
      if (v = R.sourceEvent, (R.sourceEvent.type === "touchmove" && R.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      I && !$.has(I)) && (p = !0), !p) {
        if (!u && O && d && (u = !0, P()), !d) {
          const H = Ue(R.sourceEvent, f), B = H.x - l.x, F = H.y - l.y;
          Math.sqrt(B * B + F * F) > D && W(R);
        }
        (i.x !== T.xSnapped || i.y !== T.ySnapped) && c && d && (l = Ue(R.sourceEvent, f), j(T));
      }
    }).on("end", (R) => {
      if (!d || p) {
        p && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (u = !1, d = !1, cancelAnimationFrame(s), c.size > 0) {
        const { nodeLookup: O, updateNodePositions: N, onNodeDragStop: _, onSelectionDragStop: C } = t();
        if (g && (N(c, !1), g = !1), r || _ || !I && C) {
          const [D, $] = Or({
            nodeId: I,
            dragItems: c,
            nodeLookup: O,
            dragging: !1
          });
          r?.(R.sourceEvent, c, D, $), _?.(R.sourceEvent, D, $), I || C?.(R.sourceEvent, $);
        }
      }
    }).filter((R) => {
      const O = R.target;
      return !R.button && (!k || !Ts(O, `.${k}`, w)) && (!y || Ts(O, y, w));
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
function Kp(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const i of t.values())
    Ln(r, Qt(i)) > 0 && o.push(i);
  return o;
}
const Up = 250;
function Zp(e, t, n, o) {
  let r = [], i = 1 / 0;
  const s = Kp(e, n, t + Up);
  for (const c of s) {
    const u = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of u) {
      if (o.nodeId === l.nodeId && o.type === l.type && o.id === l.id)
        continue;
      const { x: f, y: d } = Tt(c, l, l.position, !0), h = Math.sqrt(Math.pow(f - e.x, 2) + Math.pow(d - e.y, 2));
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
function Oc(e, t, n, o, r, i = !1) {
  const s = o.get(e);
  if (!s)
    return null;
  const c = r === "strict" ? s.internals.handleBounds?.[t] : [...s.internals.handleBounds?.source ?? [], ...s.internals.handleBounds?.target ?? []], u = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return u && i ? { ...u, ...Tt(s, u, u.position, !0) } : u;
}
function Fc(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Gp(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const Bc = () => !0;
function Jp(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: i, isTarget: s, domNode: c, nodeLookup: u, lib: l, autoPanOnConnect: f, flowId: d, panBy: h, cancelConnection: p, onConnectStart: g, onConnect: v, onConnectEnd: x, isValidConnection: m = Bc, onReconnectEnd: k, updateConnection: y, getTransform: w, getFromHandle: b, autoPanSpeed: I, dragThreshold: E = 1, handleDomNode: j }) {
  const P = _c(e.target);
  let W = 0, M;
  const { x: R, y: O } = Ue(e), N = Fc(i, j), _ = c?.getBoundingClientRect();
  let C = !1;
  if (!_ || !N)
    return;
  const D = Oc(r, N, o, u, t);
  if (!D)
    return;
  let $ = Ue(e, _), T = !1, H = null, B = !1, F = null;
  function Z() {
    if (!f || !_)
      return;
    const [re, U] = Di($, _, I);
    h({ x: re, y: U }), W = requestAnimationFrame(Z);
  }
  const q = {
    ...D,
    nodeId: r,
    type: N,
    position: D.position
  }, te = u.get(r);
  let Q = {
    inProgress: !0,
    isValid: null,
    from: Tt(te, q, ne.Left, !0),
    fromHandle: q,
    fromPosition: q.position,
    fromNode: te,
    to: $,
    toHandle: null,
    toPosition: Ss[q.position],
    toNode: null,
    pointer: $
  };
  function z() {
    C = !0, y(Q), g?.(e, { nodeId: r, handleId: o, handleType: N });
  }
  E === 0 && z();
  function K(re) {
    if (!C) {
      const { x: fe, y: me } = Ue(re), ge = fe - R, _e = me - O;
      if (!(ge * ge + _e * _e > E * E))
        return;
      z();
    }
    if (!b() || !q) {
      se(re);
      return;
    }
    const U = w();
    $ = Ue(re, _), M = Zp(rn($, U, !1, [1, 1]), n, u, q), T || (Z(), T = !0);
    const ee = Wc(re, {
      handle: M,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: s ? "target" : "source",
      isValidConnection: m,
      doc: P,
      lib: l,
      flowId: d,
      nodeLookup: u
    });
    F = ee.handleDomNode, H = ee.connection, B = Gp(!!M, ee.isValid);
    const le = u.get(r), L = le ? Tt(le, q, ne.Left, !0) : Q.from, G = {
      ...Q,
      from: L,
      isValid: B,
      to: ee.toHandle && B ? en({ x: ee.toHandle.x, y: ee.toHandle.y }, U) : $,
      toHandle: ee.toHandle,
      toPosition: B && ee.toHandle ? ee.toHandle.position : Ss[q.position],
      toNode: ee.toHandle ? u.get(ee.toHandle.nodeId) : null,
      pointer: $
    };
    y(G), Q = G;
  }
  function se(re) {
    if (!("touches" in re && re.touches.length > 0)) {
      if (C) {
        (M || F) && H && B && v?.(H);
        const { inProgress: U, ...ee } = Q, le = {
          ...ee,
          toPosition: Q.toHandle ? Q.toPosition : null
        };
        x?.(re, le), i && k?.(re, le);
      }
      p(), cancelAnimationFrame(W), T = !1, B = !1, H = null, F = null, P.removeEventListener("mousemove", K), P.removeEventListener("mouseup", se), P.removeEventListener("touchmove", K), P.removeEventListener("touchend", se);
    }
  }
  P.addEventListener("mousemove", K), P.addEventListener("mouseup", se), P.addEventListener("touchmove", K), P.addEventListener("touchend", se);
}
function Wc(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: i, doc: s, lib: c, flowId: u, isValidConnection: l = Bc, nodeLookup: f }) {
  const d = i === "target", h = t ? s.querySelector(`.${c}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: p, y: g } = Ue(e), v = s.elementFromPoint(p, g), x = v?.classList.contains(`${c}-flow__handle`) ? v : h, m = {
    handleDomNode: x,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (x) {
    const k = Fc(void 0, x), y = x.getAttribute("data-nodeid"), w = x.getAttribute("data-handleid"), b = x.classList.contains("connectable"), I = x.classList.contains("connectableend");
    if (!y || !k)
      return m;
    const E = {
      source: d ? y : o,
      sourceHandle: d ? w : r,
      target: d ? o : y,
      targetHandle: d ? r : w
    };
    m.connection = E;
    const P = b && I && (n === Gt.Strict ? d && k === "source" || !d && k === "target" : y !== o || w !== r);
    m.isValid = P && l(E), m.toHandle = Oc(y, k, w, f, n, !0);
  }
  return m;
}
const di = {
  onPointerDown: Jp,
  isValid: Wc
};
function Qp({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = Ve(e);
  function i({ translateExtent: c, width: u, height: l, zoomStep: f = 1, pannable: d = !0, zoomable: h = !0, inversePan: p = !1 }) {
    const g = (y) => {
      if (y.sourceEvent.type !== "wheel" || !t)
        return;
      const w = n(), b = y.sourceEvent.ctrlKey && Vn() ? 10 : 1, I = -y.sourceEvent.deltaY * (y.sourceEvent.deltaMode === 1 ? 0.05 : y.sourceEvent.deltaMode ? 1 : 2e-3) * f, E = w[2] * Math.pow(2, I * b);
      t.scaleTo(E);
    };
    let v = [0, 0];
    const x = (y) => {
      (y.sourceEvent.type === "mousedown" || y.sourceEvent.type === "touchstart") && (v = [
        y.sourceEvent.clientX ?? y.sourceEvent.touches[0].clientX,
        y.sourceEvent.clientY ?? y.sourceEvent.touches[0].clientY
      ]);
    }, m = (y) => {
      const w = n();
      if (y.sourceEvent.type !== "mousemove" && y.sourceEvent.type !== "touchmove" || !t)
        return;
      const b = [
        y.sourceEvent.clientX ?? y.sourceEvent.touches[0].clientX,
        y.sourceEvent.clientY ?? y.sourceEvent.touches[0].clientY
      ], I = [b[0] - v[0], b[1] - v[1]];
      v = b;
      const E = o() * Math.max(w[2], Math.log(w[2])) * (p ? -1 : 1), j = {
        x: w[0] - I[0] * E,
        y: w[1] - I[1] * E
      }, P = [
        [0, 0],
        [u, l]
      ];
      t.setViewportConstrained({
        x: j.x,
        y: j.y,
        zoom: w[2]
      }, P, c);
    }, k = mc().on("start", x).on("zoom", d ? m : null).on("zoom.wheel", h ? g : null);
    r.call(k, {});
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
const ar = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), Fr = ({ x: e, y: t, zoom: n }) => rr.translate(e, t).scale(n), Wt = (e, t) => e.target.closest(`.${t}`), Xc = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), eg = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, Br = (e, t = 0, n = eg, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, Yc = (e) => {
  const t = e.ctrlKey && Vn() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function tg({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: i, zoomOnPinch: s, onPanZoomStart: c, onPanZoom: u, onPanZoomEnd: l }) {
  return (f) => {
    if (Wt(f, t))
      return f.ctrlKey && f.preventDefault(), !1;
    f.preventDefault(), f.stopImmediatePropagation();
    const d = n.property("__zoom").k || 1;
    if (f.ctrlKey && s) {
      const x = Ye(f), m = Yc(f), k = d * Math.pow(2, m);
      o.scaleTo(n, k, x, f);
      return;
    }
    const h = f.deltaMode === 1 ? 20 : 1;
    let p = r === _t.Vertical ? 0 : f.deltaX * h, g = r === _t.Horizontal ? 0 : f.deltaY * h;
    !Vn() && f.shiftKey && r !== _t.Vertical && (p = f.deltaY * h, g = 0), o.translateBy(
      n,
      -(p / d) * i,
      -(g / d) * i,
      // @ts-ignore
      { internal: !0 }
    );
    const v = ar(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(f, v), e.panScrollTimeout = setTimeout(() => {
      l?.(f, v), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(f, v));
  };
}
function ng({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const i = o.type === "wheel", s = !t && i && !o.ctrlKey, c = Wt(o, e);
    if (o.ctrlKey && i && c && o.preventDefault(), s || c)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function og({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = ar(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function rg({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (i) => {
    e.usedRightMouseButton = !!(n && Xc(t, e.mouseButton ?? 0)), i.sourceEvent?.sync || o([i.transform.x, i.transform.y, i.transform.k]), r && !i.sourceEvent?.internal && r?.(i.sourceEvent, ar(i.transform));
  };
}
function ig({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: i }) {
  return (s) => {
    if (!s.sourceEvent?.internal && (e.isZoomingOrPanning = !1, i && Xc(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && s.sourceEvent && i(s.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
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
function sg({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: i, userSelectionActive: s, noWheelClassName: c, noPanClassName: u, lib: l, connectionInProgress: f }) {
  return (d) => {
    const h = e || t, p = n && d.ctrlKey, g = d.type === "wheel";
    if (d.button === 1 && d.type === "mousedown" && (Wt(d, `${l}-flow__node`) || Wt(d, `${l}-flow__edge`)))
      return !0;
    if (!o && !h && !r && !i && !n || s || f && !g || Wt(d, c) && g || Wt(d, u) && (!g || r && g && !e) || !n && d.ctrlKey && g)
      return !1;
    if (!n && d.type === "touchstart" && d.touches?.length > 1)
      return d.preventDefault(), !1;
    if (!h && !r && !p && g || !o && (d.type === "mousedown" || d.type === "touchstart") || Array.isArray(o) && !o.includes(d.button) && d.type === "mousedown")
      return !1;
    const v = Array.isArray(o) && o.includes(d.button) || !d.button || d.button <= 1;
    return (!d.ctrlKey || g) && v;
  };
}
function ag({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: i, onPanZoomStart: s, onPanZoomEnd: c, onDraggingChange: u }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, f = e.getBoundingClientRect(), d = mc().scaleExtent([t, n]).translateExtent(o), h = Ve(e).call(d);
  k({
    x: r.x,
    y: r.y,
    zoom: Jt(r.zoom, t, n)
  }, [
    [0, 0],
    [f.width, f.height]
  ], o);
  const p = h.on("wheel.zoom"), g = h.on("dblclick.zoom");
  d.wheelDelta(Yc);
  async function v(M, R) {
    return h ? new Promise((O) => {
      d?.interpolate(R?.interpolate === "linear" ? _n : Mo).transform(Br(h, R?.duration, R?.ease, () => O(!0)), M);
    }) : !1;
  }
  function x({ noWheelClassName: M, noPanClassName: R, onPaneContextMenu: O, userSelectionActive: N, panOnScroll: _, panOnDrag: C, panOnScrollMode: D, panOnScrollSpeed: $, preventScrolling: T, zoomOnPinch: H, zoomOnScroll: B, zoomOnDoubleClick: F, zoomActivationKeyPressed: Z, lib: q, onTransformChange: te, connectionInProgress: ae, paneClickDistance: Q, selectionOnDrag: z }) {
    N && !l.isZoomingOrPanning && m();
    const K = _ && !Z && !N;
    d.clickDistance(z ? 1 / 0 : !Ke(Q) || Q < 0 ? 0 : Q);
    const se = K ? tg({
      zoomPanValues: l,
      noWheelClassName: M,
      d3Selection: h,
      d3Zoom: d,
      panOnScrollMode: D,
      panOnScrollSpeed: $,
      zoomOnPinch: H,
      onPanZoomStart: s,
      onPanZoom: i,
      onPanZoomEnd: c
    }) : ng({
      noWheelClassName: M,
      preventScrolling: T,
      d3ZoomHandler: p
    });
    h.on("wheel.zoom", se, { passive: !1 });
    const re = og({
      zoomPanValues: l,
      onDraggingChange: u,
      onPanZoomStart: s
    });
    d.on("start", re);
    const U = rg({
      zoomPanValues: l,
      panOnDrag: C,
      onPaneContextMenu: !!O,
      onPanZoom: i,
      onTransformChange: te
    });
    d.on("zoom", U);
    const ee = ig({
      zoomPanValues: l,
      panOnDrag: C,
      panOnScroll: _,
      onPaneContextMenu: O,
      onPanZoomEnd: c,
      onDraggingChange: u
    });
    d.on("end", ee);
    const le = sg({
      zoomActivationKeyPressed: Z,
      panOnDrag: C,
      zoomOnScroll: B,
      panOnScroll: _,
      zoomOnDoubleClick: F,
      zoomOnPinch: H,
      userSelectionActive: N,
      noPanClassName: R,
      noWheelClassName: M,
      lib: q,
      connectionInProgress: ae
    });
    d.filter(le), F ? h.on("dblclick.zoom", g) : h.on("dblclick.zoom", null);
  }
  function m() {
    d.on("zoom", null);
  }
  async function k(M, R, O) {
    const N = Fr(M), _ = d?.constrain()(N, R, O);
    return _ && await v(_), _;
  }
  async function y(M, R) {
    const O = Fr(M);
    return await v(O, R), O;
  }
  function w(M) {
    if (h) {
      const R = Fr(M), O = h.property("__zoom");
      (O.k !== M.zoom || O.x !== M.x || O.y !== M.y) && d?.transform(h, R, null, { sync: !0 });
    }
  }
  function b() {
    const M = h ? yc(h.node()) : { x: 0, y: 0, k: 1 };
    return { x: M.x, y: M.y, zoom: M.k };
  }
  async function I(M, R) {
    return h ? new Promise((O) => {
      d?.interpolate(R?.interpolate === "linear" ? _n : Mo).scaleTo(Br(h, R?.duration, R?.ease, () => O(!0)), M);
    }) : !1;
  }
  async function E(M, R) {
    return h ? new Promise((O) => {
      d?.interpolate(R?.interpolate === "linear" ? _n : Mo).scaleBy(Br(h, R?.duration, R?.ease, () => O(!0)), M);
    }) : !1;
  }
  function j(M) {
    d?.scaleExtent(M);
  }
  function P(M) {
    d?.translateExtent(M);
  }
  function W(M) {
    const R = !Ke(M) || M < 0 ? 0 : M;
    d?.clickDistance(R);
  }
  return {
    update: x,
    destroy: m,
    setViewport: y,
    setViewportConstrained: k,
    getViewport: b,
    scaleTo: I,
    scaleBy: E,
    setScaleExtent: j,
    setTranslateExtent: P,
    syncViewport: w,
    setClickDistance: W
  };
}
var tn;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(tn || (tn = {}));
function cg({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: i }) {
  const s = e - t, c = n - o, u = [s > 0 ? 1 : s < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return s && r && (u[0] = u[0] * -1), c && i && (u[1] = u[1] * -1), u;
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
function dt(e, t) {
  return Math.max(0, t - e);
}
function ft(e, t) {
  return Math.max(0, e - t);
}
function ko(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function zs(e, t) {
  return e ? !t : t;
}
function lg(e, t, n, o, r, i, s, c) {
  let { affectsX: u, affectsY: l } = t;
  const { isHorizontal: f, isVertical: d } = t, h = f && d, { xSnapped: p, ySnapped: g } = n, { minWidth: v, maxWidth: x, minHeight: m, maxHeight: k } = o, { x: y, y: w, width: b, height: I, aspectRatio: E } = e;
  let j = Math.floor(f ? p - e.pointerX : 0), P = Math.floor(d ? g - e.pointerY : 0);
  const W = b + (u ? -j : j), M = I + (l ? -P : P), R = -i[0] * b, O = -i[1] * I;
  let N = ko(W, v, x), _ = ko(M, m, k);
  if (s) {
    let $ = 0, T = 0;
    u && j < 0 ? $ = dt(y + j + R, s[0][0]) : !u && j > 0 && ($ = ft(y + W + R, s[1][0])), l && P < 0 ? T = dt(w + P + O, s[0][1]) : !l && P > 0 && (T = ft(w + M + O, s[1][1])), N = Math.max(N, $), _ = Math.max(_, T);
  }
  if (c) {
    let $ = 0, T = 0;
    u && j > 0 ? $ = ft(y + j, c[0][0]) : !u && j < 0 && ($ = dt(y + W, c[1][0])), l && P > 0 ? T = ft(w + P, c[0][1]) : !l && P < 0 && (T = dt(w + M, c[1][1])), N = Math.max(N, $), _ = Math.max(_, T);
  }
  if (r) {
    if (f) {
      const $ = ko(W / E, m, k) * E;
      if (N = Math.max(N, $), s) {
        let T = 0;
        !u && !l || u && !l && h ? T = ft(w + O + W / E, s[1][1]) * E : T = dt(w + O + (u ? j : -j) / E, s[0][1]) * E, N = Math.max(N, T);
      }
      if (c) {
        let T = 0;
        !u && !l || u && !l && h ? T = dt(w + W / E, c[1][1]) * E : T = ft(w + (u ? j : -j) / E, c[0][1]) * E, N = Math.max(N, T);
      }
    }
    if (d) {
      const $ = ko(M * E, v, x) / E;
      if (_ = Math.max(_, $), s) {
        let T = 0;
        !u && !l || l && !u && h ? T = ft(y + M * E + R, s[1][0]) / E : T = dt(y + (l ? P : -P) * E + R, s[0][0]) / E, _ = Math.max(_, T);
      }
      if (c) {
        let T = 0;
        !u && !l || l && !u && h ? T = dt(y + M * E, c[1][0]) / E : T = ft(y + (l ? P : -P) * E, c[0][0]) / E, _ = Math.max(_, T);
      }
    }
  }
  P = P + (P < 0 ? _ : -_), j = j + (j < 0 ? N : -N), r && (h ? W > M * E ? P = (zs(u, l) ? -j : j) / E : j = (zs(u, l) ? -P : P) * E : f ? (P = j / E, l = u) : (j = P * E, u = l));
  const C = u ? y + j : y, D = l ? w + P : w;
  return {
    width: b + (u ? -j : j),
    height: I + (l ? -P : P),
    x: i[0] * j * (u ? -1 : 1) + C,
    y: i[1] * P * (l ? -1 : 1) + D
  };
}
const qc = { width: 0, height: 0, x: 0, y: 0 }, ug = {
  ...qc,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function dg(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, i = e.measured.width ?? 0, s = e.measured.height ?? 0, c = n[0] * i, u = n[1] * s;
  return [
    [o - c, r - u],
    [o + i - c, r + s - u]
  ];
}
function fg({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const i = Ve(e);
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
  function c({ controlPosition: l, boundaries: f, keepAspectRatio: d, resizeDirection: h, onResizeStart: p, onResize: g, onResizeEnd: v, shouldResize: x }) {
    let m = { ...qc }, k = { ...ug };
    s = {
      boundaries: f,
      resizeDirection: h,
      keepAspectRatio: d,
      controlDirection: Rs(l)
    };
    let y, w = null, b = [], I, E, j, P = !1;
    const W = nc().on("start", (M) => {
      const { nodeLookup: R, transform: O, snapGrid: N, snapToGrid: _, nodeOrigin: C, paneDomNode: D } = n();
      if (y = R.get(t), !y)
        return;
      w = D?.getBoundingClientRect() ?? null;
      const { xSnapped: $, ySnapped: T } = An(M.sourceEvent, {
        transform: O,
        snapGrid: N,
        snapToGrid: _,
        containerBounds: w
      });
      m = {
        width: y.measured.width ?? 0,
        height: y.measured.height ?? 0,
        x: y.position.x ?? 0,
        y: y.position.y ?? 0
      }, k = {
        ...m,
        pointerX: $,
        pointerY: T,
        aspectRatio: m.width / m.height
      }, I = void 0, E = $t(y.extent) ? y.extent : void 0, y.parentId && (y.extent === "parent" || y.expandParent) && (I = R.get(y.parentId)), I && y.extent === "parent" && (E = [
        [0, 0],
        [I.measured.width, I.measured.height]
      ]), b = [], j = void 0;
      for (const [H, B] of R)
        if (B.parentId === t && (b.push({
          id: H,
          position: { ...B.position },
          extent: B.extent
        }), B.extent === "parent" || B.expandParent)) {
          const F = dg(B, y, B.origin ?? C);
          j ? j = [
            [Math.min(F[0][0], j[0][0]), Math.min(F[0][1], j[0][1])],
            [Math.max(F[1][0], j[1][0]), Math.max(F[1][1], j[1][1])]
          ] : j = F;
        }
      p?.(M, { ...m });
    }).on("drag", (M) => {
      const { transform: R, snapGrid: O, snapToGrid: N, nodeOrigin: _ } = n(), C = An(M.sourceEvent, {
        transform: R,
        snapGrid: O,
        snapToGrid: N,
        containerBounds: w
      }), D = [];
      if (!y)
        return;
      const { x: $, y: T, width: H, height: B } = m, F = {}, Z = y.origin ?? _, { width: q, height: te, x: ae, y: Q } = lg(k, s.controlDirection, C, s.boundaries, s.keepAspectRatio, Z, E, j), z = q !== H, K = te !== B, se = ae !== $ && z, re = Q !== T && K;
      if (!se && !re && !z && !K)
        return;
      if ((se || re || Z[0] === 1 || Z[1] === 1) && (F.x = se ? ae : m.x, F.y = re ? Q : m.y, m.x = F.x, m.y = F.y, b.length > 0)) {
        const L = ae - $, G = Q - T;
        for (const fe of b)
          fe.position = {
            x: fe.position.x - L + Z[0] * (q - H),
            y: fe.position.y - G + Z[1] * (te - B)
          }, D.push(fe);
      }
      if ((z || K) && (F.width = z && (!s.resizeDirection || s.resizeDirection === "horizontal") ? q : m.width, F.height = K && (!s.resizeDirection || s.resizeDirection === "vertical") ? te : m.height, m.width = F.width, m.height = F.height), I && y.expandParent) {
        const L = Z[0] * (F.width ?? 0);
        F.x && F.x < L && (m.x = L, k.x = k.x - (F.x - L));
        const G = Z[1] * (F.height ?? 0);
        F.y && F.y < G && (m.y = G, k.y = k.y - (F.y - G));
      }
      const U = cg({
        width: m.width,
        prevWidth: H,
        height: m.height,
        prevHeight: B,
        affectsX: s.controlDirection.affectsX,
        affectsY: s.controlDirection.affectsY
      }), ee = { ...m, direction: U };
      x?.(M, ee) !== !1 && (P = !0, g?.(M, ee), o(F, D));
    }).on("end", (M) => {
      P && (v?.(M, { ...m }), r?.({ ...m }), P = !1);
    });
    i.call(W);
  }
  function u() {
    i.on(".drag", null);
  }
  return {
    update: c,
    destroy: u
  };
}
var Wr = { exports: {} }, Xr = {}, Yr = { exports: {} }, qr = {};
var Ls;
function hg() {
  if (Ls) return qr;
  Ls = 1;
  var e = xt;
  function t(d, h) {
    return d === h && (d !== 0 || 1 / d === 1 / h) || d !== d && h !== h;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, i = e.useLayoutEffect, s = e.useDebugValue;
  function c(d, h) {
    var p = h(), g = o({ inst: { value: p, getSnapshot: h } }), v = g[0].inst, x = g[1];
    return i(
      function() {
        v.value = p, v.getSnapshot = h, u(v) && x({ inst: v });
      },
      [d, p, h]
    ), r(
      function() {
        return u(v) && x({ inst: v }), d(function() {
          u(v) && x({ inst: v });
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
  return qr.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : f, qr;
}
var Vs;
function pg() {
  return Vs || (Vs = 1, Yr.exports = hg()), Yr.exports;
}
var Hs;
function gg() {
  if (Hs) return Xr;
  Hs = 1;
  var e = xt, t = pg();
  function n(l, f) {
    return l === f && (l !== 0 || 1 / l === 1 / f) || l !== l && f !== f;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, i = e.useRef, s = e.useEffect, c = e.useMemo, u = e.useDebugValue;
  return Xr.useSyncExternalStoreWithSelector = function(l, f, d, h, p) {
    var g = i(null);
    if (g.current === null) {
      var v = { hasValue: !1, value: null };
      g.current = v;
    } else v = g.current;
    g = c(
      function() {
        function m(I) {
          if (!k) {
            if (k = !0, y = I, I = h(I), p !== void 0 && v.hasValue) {
              var E = v.value;
              if (p(E, I))
                return w = E;
            }
            return w = I;
          }
          if (E = w, o(y, I)) return E;
          var j = h(I);
          return p !== void 0 && p(E, j) ? (y = I, E) : (y = I, w = j);
        }
        var k = !1, y, w, b = d === void 0 ? null : d;
        return [
          function() {
            return m(f());
          },
          b === null ? void 0 : function() {
            return m(b());
          }
        ];
      },
      [f, d, h, p]
    );
    var x = r(l, g[0], g[1]);
    return s(
      function() {
        v.hasValue = !0, v.value = x;
      },
      [x]
    ), u(x), x;
  }, Xr;
}
var Os;
function yg() {
  return Os || (Os = 1, Wr.exports = gg()), Wr.exports;
}
var mg = yg();
const xg = /* @__PURE__ */ Pu(mg), wg = {}, Fs = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (f, d) => {
    const h = typeof f == "function" ? f(t) : f;
    if (!Object.is(h, t)) {
      const p = t;
      t = d ?? (typeof h != "object" || h === null) ? h : Object.assign({}, t, h), n.forEach((g) => g(t, p));
    }
  }, r = () => t, u = { setState: o, getState: r, getInitialState: () => l, subscribe: (f) => (n.add(f), () => n.delete(f)), destroy: () => {
    (wg ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(o, r, u);
  return u;
}, vg = (e) => e ? Fs(e) : Fs, { useDebugValue: bg } = xt, { useSyncExternalStoreWithSelector: Ng } = xg, Sg = (e) => e;
function Kc(e, t = Sg, n) {
  const o = Ng(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return bg(o), o;
}
const Bs = (e, t) => {
  const n = vg(e), o = (r, i = t) => Kc(n, r, i);
  return Object.assign(o, n), o;
}, Eg = (e, t) => e ? Bs(e, t) : Bs;
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
var Kr = { exports: {} }, Me = {};
var Ws;
function kg() {
  if (Ws) return Me;
  Ws = 1;
  var e = xt;
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
var Xs;
function Ig() {
  if (Xs) return Kr.exports;
  Xs = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), Kr.exports = kg(), Kr.exports;
}
var Cg = Ig();
const cr = Ni(null), jg = cr.Provider, Uc = Oe.error001("react");
function de(e, t) {
  const n = Bn(cr);
  if (n === null)
    throw new Error(Uc);
  return Kc(n, e, t);
}
function we() {
  const e = Bn(cr);
  if (e === null)
    throw new Error(Uc);
  return ye(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const Ys = { display: "none" }, _g = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, Zc = "react-flow__node-desc", Gc = "react-flow__edge-desc", Ag = "react-flow__aria-live", Mg = (e) => e.ariaLiveMessage, Dg = (e) => e.ariaLabelConfig;
function Pg({ rfId: e }) {
  const t = de(Mg);
  return a.jsx("div", { id: `${Ag}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: _g, children: t });
}
function $g({ rfId: e, disableKeyboardA11y: t }) {
  const n = de(Dg);
  return a.jsxs(a.Fragment, { children: [a.jsx("div", { id: `${Zc}-${e}`, style: Ys, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), a.jsx("div", { id: `${Gc}-${e}`, style: Ys, children: n["edge.a11yDescription.default"] }), !t && a.jsx(Pg, { rfId: e })] });
}
const lr = Qo(({ position: e = "top-left", children: t, className: n, style: o, ...r }, i) => {
  const s = `${e}`.split("-");
  return a.jsx("div", { className: Ie(["react-flow__panel", n, ...s]), style: o, ref: i, ...r, children: t });
});
lr.displayName = "Panel";
function Tg({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : a.jsx(lr, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: a.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const Rg = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, Io = (e) => e.id;
function zg(e, t) {
  return xe(e.selectedNodes.map(Io), t.selectedNodes.map(Io)) && xe(e.selectedEdges.map(Io), t.selectedEdges.map(Io));
}
function Lg({ onSelectionChange: e }) {
  const t = we(), { selectedNodes: n, selectedEdges: o } = de(Rg, zg);
  return oe(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((i) => i(r));
  }, [n, o, e]), null;
}
const Vg = (e) => !!e.onSelectionChangeHandlers;
function Hg({ onSelectionChange: e }) {
  const t = de(Vg);
  return e || t ? a.jsx(Lg, { onSelectionChange: e }) : null;
}
const Jc = [0, 0], Og = { x: 0, y: 0, zoom: 1 }, Fg = [
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
], qs = [...Fg, "rfId"], Bg = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), Ks = {
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
function Wg(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: i, setNodeExtent: s, reset: c, setDefaultNodesAndEdges: u } = de(Bg, xe), l = we();
  oe(() => (u(e.defaultNodes, e.defaultEdges), () => {
    f.current = Ks, c();
  }), []);
  const f = ce(Ks);
  return oe(
    () => {
      for (const d of qs) {
        const h = e[d], p = f.current[d];
        h !== p && (typeof e[d] > "u" || (d === "nodes" ? t(h) : d === "edges" ? n(h) : d === "minZoom" ? o(h) : d === "maxZoom" ? r(h) : d === "translateExtent" ? i(h) : d === "nodeExtent" ? s(h) : d === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: kp(h) }) : d === "fitView" ? l.setState({ fitViewQueued: h }) : d === "fitViewOptions" ? l.setState({ fitViewOptions: h }) : l.setState({ [d]: h })));
      }
      f.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    qs.map((d) => e[d])
  ), null;
}
function Us() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function Xg(e) {
  const [t, n] = Y(e === "system" ? null : e);
  return oe(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = Us(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : Us()?.matches ? "dark" : "light";
}
const Zs = typeof document < "u" ? document : null;
function Hn(e = null, t = { target: Zs, actInsideInputWithModifier: !0 }) {
  const [n, o] = Y(!1), r = ce(!1), i = ce(/* @__PURE__ */ new Set([])), [s, c] = ye(() => {
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
  return oe(() => {
    const u = t?.target ?? Zs, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const f = (p) => {
        if (r.current = p.ctrlKey || p.metaKey || p.shiftKey || p.altKey, (!r.current || r.current && !l) && Ac(p))
          return !1;
        const v = Js(p.code, c);
        if (i.current.add(p[v]), Gs(s, i.current, !1)) {
          const x = p.composedPath?.()?.[0] || p.target, m = x?.nodeName === "BUTTON" || x?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !m) && p.preventDefault(), o(!0);
        }
      }, d = (p) => {
        const g = Js(p.code, c);
        Gs(s, i.current, !0) ? (o(!1), i.current.clear()) : i.current.delete(p[g]), p.key === "Meta" && i.current.clear(), r.current = !1;
      }, h = () => {
        i.current.clear(), o(!1);
      };
      return u?.addEventListener("keydown", f), u?.addEventListener("keyup", d), window.addEventListener("blur", h), window.addEventListener("contextmenu", h), () => {
        u?.removeEventListener("keydown", f), u?.removeEventListener("keyup", d), window.removeEventListener("blur", h), window.removeEventListener("contextmenu", h);
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
const Yg = () => {
  const e = we();
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
      const { width: o, height: r, minZoom: i, maxZoom: s, panZoom: c } = e.getState(), u = Pi(t, o, r, i, s, n?.padding ?? 0.1);
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
      return rn(l, o, d, f);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: r, y: i } = o.getBoundingClientRect(), s = en(t, n);
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
    for (const u of s)
      qg(u, c);
    n.push(c);
  }
  return r.length && r.forEach((i) => {
    i.index !== void 0 ? n.splice(i.index, 0, { ...i.item }) : n.push({ ...i.item });
  }), n;
}
function qg(e, t) {
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
function It(e, t) {
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
    !(i.selected === void 0 && !s) && i.selected !== s && (n && (i.selected = s), o.push(It(i.id, s)));
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
  return Ap(e, t, {
    ...n,
    onError: n.onError ?? nl
  });
}
function Kg(e, t, n, o = { shouldReplaceId: !0 }) {
  return Mp(e, t, n, {
    ...o,
    onError: o.onError ?? nl
  });
}
const ta = (e) => yp(e), Ug = (e) => Nc(e);
function rl(e) {
  return Qo(e);
}
const Zg = typeof window < "u" ? Du : oe;
function na(e) {
  const [t, n] = Y(BigInt(0)), [o] = Y(() => Gg(() => n((r) => r + BigInt(1))));
  return Zg(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function Gg(e) {
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
const il = Ni(null);
function Jg({ children: e }) {
  const t = we(), n = pe((c) => {
    const { nodes: u = [], setNodes: l, hasDefaultNodes: f, onNodesChange: d, nodeLookup: h, fitViewQueued: p, onNodesChangeMiddlewareMap: g } = t.getState();
    let v = u;
    for (const m of c)
      v = typeof m == "function" ? m(v) : m;
    let x = Qs({
      items: v,
      lookup: h
    });
    for (const m of g.values())
      x = m(x);
    f && l(v), x.length > 0 ? d?.(x) : p && window.requestAnimationFrame(() => {
      const { fitViewQueued: m, nodes: k, setNodes: y } = t.getState();
      m && y(k);
    });
  }, []), o = na(n), r = pe((c) => {
    const { edges: u = [], setEdges: l, hasDefaultEdges: f, onEdgesChange: d, edgeLookup: h } = t.getState();
    let p = u;
    for (const g of c)
      p = typeof g == "function" ? g(p) : g;
    f ? l(p) : d && d(Qs({
      items: p,
      lookup: h
    }));
  }, []), i = na(r), s = ye(() => ({ nodeQueue: o, edgeQueue: i }), []);
  return a.jsx(il.Provider, { value: s, children: e });
}
function Qg() {
  const e = Bn(il);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const ey = (e) => !!e.panZoom;
function Hi() {
  const e = Yg(), t = we(), n = Qg(), o = de(ey), r = ye(() => {
    const i = (d) => t.getState().nodeLookup.get(d), s = (d) => {
      n.nodeQueue.push(d);
    }, c = (d) => {
      n.edgeQueue.push(d);
    }, u = (d) => {
      const { nodeLookup: h, nodeOrigin: p } = t.getState(), g = ta(d) ? d : h.get(d.id), v = g.parentId ? jc(g.position, g.measured, g.parentId, h, p) : g.position, x = {
        ...g,
        position: v,
        width: g.measured?.width ?? g.width,
        height: g.measured?.height ?? g.height
      };
      return Qt(x);
    }, l = (d, h, p = { replace: !1 }) => {
      s((g) => g.map((v) => {
        if (v.id === d) {
          const x = typeof h == "function" ? h(v) : h;
          return p.replace && ta(x) ? x : { ...v, ...x };
        }
        return v;
      }));
    }, f = (d, h, p = { replace: !1 }) => {
      c((g) => g.map((v) => {
        if (v.id === d) {
          const x = typeof h == "function" ? h(v) : h;
          return p.replace && Ug(x) ? x : { ...v, ...x };
        }
        return v;
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
        const { nodes: d = [], edges: h = [], transform: p } = t.getState(), [g, v, x] = p;
        return {
          nodes: d.map((m) => ({ ...m })),
          edges: h.map((m) => ({ ...m })),
          viewport: {
            x: g,
            y: v,
            zoom: x
          }
        };
      },
      deleteElements: async ({ nodes: d = [], edges: h = [] }) => {
        const { nodes: p, edges: g, onNodesDelete: v, onEdgesDelete: x, triggerNodeChanges: m, triggerEdgeChanges: k, onDelete: y, onBeforeDelete: w } = t.getState(), { nodes: b, edges: I } = await bp({
          nodesToRemove: d,
          edgesToRemove: h,
          nodes: p,
          edges: g,
          onBeforeDelete: w
        }), E = I.length > 0, j = b.length > 0;
        if (E) {
          const P = I.map(ea);
          x?.(I), k(P);
        }
        if (j) {
          const P = b.map(ea);
          v?.(b), m(P);
        }
        return (j || E) && y?.({ nodes: b, edges: I }), { deletedNodes: b, deletedEdges: I };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (d, h = !0, p) => {
        const g = ks(d), v = g ? d : u(d), x = p !== void 0;
        return v ? (p || t.getState().nodes).filter((m) => {
          const k = t.getState().nodeLookup.get(m.id);
          if (k && !g && (m.id === d.id || !k.internals.positionAbsolute))
            return !1;
          const y = Qt(x ? m : k), w = Ln(y, v);
          return h && w > 0 || w >= y.width * y.height || w >= v.width * v.height;
        }) : [];
      },
      isNodeIntersecting: (d, h, p = !0) => {
        const v = ks(d) ? d : u(d);
        if (!v)
          return !1;
        const x = Ln(v, h);
        return p && x > 0 || x >= h.width * h.height || x >= v.width * v.height;
      },
      updateNode: l,
      updateNodeData: (d, h, p = { replace: !1 }) => {
        l(d, (g) => {
          const v = typeof h == "function" ? h(g) : h;
          return p.replace ? { ...g, data: v } : { ...g, data: { ...g.data, ...v } };
        }, p);
      },
      updateEdge: f,
      updateEdgeData: (d, h, p = { replace: !1 }) => {
        f(d, (g) => {
          const v = typeof h == "function" ? h(g) : h;
          return p.replace ? { ...g, data: v } : { ...g, data: { ...g.data, ...v } };
        }, p);
      },
      getNodesBounds: (d) => {
        const { nodeLookup: h, nodeOrigin: p } = t.getState();
        return mp(d, { nodeLookup: h, nodeOrigin: p });
      },
      getHandleConnections: ({ type: d, id: h, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}-${d}${h ? `-${h}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: d, handleId: h, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}${d ? h ? `-${d}-${h}` : `-${d}` : ""}`)?.values() ?? []),
      fitView: async (d) => {
        const h = t.getState().fitViewResolver ?? Ep();
        return t.setState({ fitViewQueued: !0, fitViewOptions: d, fitViewResolver: h }), n.nodeQueue.push((p) => [...p]), h.promise;
      }
    };
  }, []);
  return ye(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const oa = (e) => e.selected, ty = typeof window < "u" ? window : void 0;
function ny({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = we(), { deleteElements: o } = Hi(), r = Hn(e, { actInsideInputWithModifier: !1 }), i = Hn(t, { target: ty });
  oe(() => {
    if (r) {
      const { edges: s, nodes: c } = n.getState();
      o({ nodes: c.filter(oa), edges: s.filter(oa) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), oe(() => {
    n.setState({ multiSelectionActive: i });
  }, [i]);
}
function oy(e) {
  const t = we();
  oe(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = $i(e.current);
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
const ur = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, ry = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function iy({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: i = _t.Free, zoomOnDoubleClick: s = !0, panOnDrag: c = !0, defaultViewport: u, translateExtent: l, minZoom: f, maxZoom: d, zoomActivationKeyCode: h, preventScrolling: p = !0, children: g, noWheelClassName: v, noPanClassName: x, onViewportChange: m, isControlledViewport: k, paneClickDistance: y, selectionOnDrag: w }) {
  const b = we(), I = ce(null), { userSelectionActive: E, lib: j, connectionInProgress: P } = de(ry, xe), W = Hn(h), M = ce();
  oy(I);
  const R = pe((O) => {
    m?.({ x: O[0], y: O[1], zoom: O[2] }), k || b.setState({ transform: O });
  }, [m, k]);
  return oe(() => {
    if (I.current) {
      M.current = ag({
        domNode: I.current,
        minZoom: f,
        maxZoom: d,
        translateExtent: l,
        viewport: u,
        onDraggingChange: (C) => b.setState((D) => D.paneDragging === C ? D : { paneDragging: C }),
        onPanZoomStart: (C, D) => {
          const { onViewportChangeStart: $, onMoveStart: T } = b.getState();
          T?.(C, D), $?.(D);
        },
        onPanZoom: (C, D) => {
          const { onViewportChange: $, onMove: T } = b.getState();
          T?.(C, D), $?.(D);
        },
        onPanZoomEnd: (C, D) => {
          const { onViewportChangeEnd: $, onMoveEnd: T } = b.getState();
          T?.(C, D), $?.(D);
        }
      });
      const { x: O, y: N, zoom: _ } = M.current.getViewport();
      return b.setState({
        panZoom: M.current,
        transform: [O, N, _],
        domNode: I.current.closest(".react-flow")
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
      preventScrolling: p,
      noPanClassName: x,
      userSelectionActive: E,
      noWheelClassName: v,
      lib: j,
      onTransformChange: R,
      connectionInProgress: P,
      selectionOnDrag: w,
      paneClickDistance: y
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
    x,
    E,
    v,
    j,
    R,
    P,
    w,
    y
  ]), a.jsx("div", { className: "react-flow__renderer", ref: I, style: ur, children: g });
}
const sy = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function ay() {
  const { userSelectionActive: e, userSelectionRect: t } = de(sy, xe);
  return e && t ? a.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const Ur = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, cy = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function ly({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = zn.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: i, selectionOnDrag: s, onSelectionStart: c, onSelectionEnd: u, onPaneClick: l, onPaneContextMenu: f, onPaneScroll: d, onPaneMouseEnter: h, onPaneMouseMove: p, onPaneMouseLeave: g, children: v }) {
  const x = ce(0), m = we(), { userSelectionActive: k, elementsSelectable: y, dragging: w, connectionInProgress: b, panBy: I, autoPanSpeed: E } = de(cy, xe), j = y && (e || k), P = ce(null), W = ce(), M = ce(/* @__PURE__ */ new Set()), R = ce(/* @__PURE__ */ new Set()), O = ce(!1), N = ce({ x: 0, y: 0 }), _ = ce(!1), C = (z) => {
    if (O.current || b) {
      O.current = !1;
      return;
    }
    l?.(z), m.getState().resetSelectedElements(), m.setState({ nodesSelectionActive: !1 });
  }, D = (z) => {
    if (Array.isArray(o) && o?.includes(2)) {
      z.preventDefault();
      return;
    }
    f?.(z);
  }, $ = d ? (z) => d(z) : void 0, T = (z) => {
    O.current && (z.stopPropagation(), O.current = !1);
  }, H = (z) => {
    const { domNode: K, transform: se } = m.getState();
    if (W.current = K?.getBoundingClientRect(), !W.current)
      return;
    const re = z.target === P.current;
    if (!re && !!z.target.closest(".nokey") || !e || !(s && re || t) || z.button !== 0 || !z.isPrimary)
      return;
    z.target?.setPointerCapture?.(z.pointerId), O.current = !1;
    const { x: le, y: L } = Ue(z.nativeEvent, W.current), G = rn({ x: le, y: L }, se);
    m.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: G.x,
        startY: G.y,
        x: le,
        y: L
      }
    }), re || (z.stopPropagation(), z.preventDefault());
  };
  function B(z, K) {
    const { userSelectionRect: se } = m.getState();
    if (!se)
      return;
    const { transform: re, nodeLookup: U, edgeLookup: ee, connectionLookup: le, triggerNodeChanges: L, triggerEdgeChanges: G, defaultEdgeOptions: fe } = m.getState(), me = { x: se.startX, y: se.startY }, { x: ge, y: _e } = en(me, re), Fe = {
      startX: me.x,
      startY: me.y,
      x: z < ge ? z : ge,
      y: K < _e ? K : _e,
      width: Math.abs(z - ge),
      height: Math.abs(K - _e)
    }, wt = M.current, rt = R.current;
    M.current = new Set(Mi(U, Fe, re, n === zn.Partial, !0).map((be) => be.id)), R.current = /* @__PURE__ */ new Set();
    const Be = fe?.selectable ?? !0;
    for (const be of M.current) {
      const De = le.get(be);
      if (De)
        for (const { edgeId: Re } of De.values()) {
          const We = ee.get(Re);
          We && (We.selectable ?? Be) && R.current.add(Re);
        }
    }
    if (!Is(wt, M.current)) {
      const be = Xt(U, M.current, !0);
      L(be);
    }
    if (!Is(rt, R.current)) {
      const be = Xt(ee, R.current);
      G(be);
    }
    m.setState({
      userSelectionRect: Fe,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function F() {
    if (!r || !W.current)
      return;
    const [z, K] = Di(N.current, W.current, E);
    I({ x: z, y: K }).then((se) => {
      if (!O.current || !se) {
        x.current = requestAnimationFrame(F);
        return;
      }
      const { x: re, y: U } = N.current;
      B(re, U), x.current = requestAnimationFrame(F);
    });
  }
  const Z = () => {
    cancelAnimationFrame(x.current), x.current = 0, _.current = !1;
  };
  oe(() => () => Z(), []);
  const q = (z) => {
    const { userSelectionRect: K, transform: se, resetSelectedElements: re } = m.getState();
    if (!W.current || !K)
      return;
    const { x: U, y: ee } = Ue(z.nativeEvent, W.current);
    N.current = { x: U, y: ee };
    const le = en({ x: K.startX, y: K.startY }, se);
    if (!O.current) {
      const L = t ? 0 : i;
      if (Math.hypot(U - le.x, ee - le.y) <= L)
        return;
      re(), c?.(z);
    }
    O.current = !0, _.current || (F(), _.current = !0), B(U, ee);
  }, te = (z) => {
    z.button === 0 && (z.target?.releasePointerCapture?.(z.pointerId), !k && z.target === P.current && m.getState().userSelectionRect && C?.(z), m.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), O.current && (u?.(z), m.setState({
      nodesSelectionActive: M.current.size > 0
    })), Z());
  }, ae = (z) => {
    z.target?.releasePointerCapture?.(z.pointerId), Z();
  }, Q = o === !0 || Array.isArray(o) && o.includes(0);
  return a.jsxs("div", { className: Ie(["react-flow__pane", { draggable: Q, dragging: w, selection: e }]), onClick: j ? void 0 : Ur(C, P), onContextMenu: Ur(D, P), onWheel: Ur($, P), onPointerEnter: j ? void 0 : h, onPointerMove: j ? q : p, onPointerUp: j ? te : void 0, onPointerCancel: j ? ae : void 0, onPointerDownCapture: j ? H : void 0, onClickCapture: j ? T : void 0, onPointerLeave: g, ref: P, style: ur, children: [v, a.jsx(ay, {})] });
}
function fi({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: i, multiSelectionActive: s, nodeLookup: c, onError: u } = t.getState(), l = c.get(e);
  if (!l) {
    u?.("012", Oe.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && s) && (i({ nodes: [l], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function sl({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: i, nodeClickDistance: s }) {
  const c = we(), [u, l] = Y(!1), f = ce();
  return oe(() => {
    f.current = qp({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (d) => {
        fi({
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
  }, [n, o, t, i, e, r, s]), u;
}
const uy = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function al() {
  const e = we();
  return pe((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: i, nodesDraggable: s, onError: c, updateNodePositions: u, nodeLookup: l, nodeOrigin: f } = e.getState(), d = /* @__PURE__ */ new Map(), h = uy(s), p = r ? i[0] : 5, g = r ? i[1] : 5, v = n.direction.x * p * n.factor, x = n.direction.y * g * n.factor;
    for (const [, m] of l) {
      if (!h(m))
        continue;
      let k = {
        x: m.internals.positionAbsolute.x + v,
        y: m.internals.positionAbsolute.y + x
      };
      r && (k = Kn(k, i));
      const { position: y, positionAbsolute: w } = Sc({
        nodeId: m.id,
        nextPosition: k,
        nodeLookup: l,
        nodeExtent: o,
        nodeOrigin: f,
        onError: c
      });
      m.position = y, m.internals.positionAbsolute = w, d.set(m.id, m);
    }
    u(d);
  }, []);
}
const Oi = Ni(null), dy = Oi.Provider;
Oi.Consumer;
const cl = () => Bn(Oi), fy = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), hy = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: i, connection: s } = o, { fromHandle: c, toHandle: u, isValid: l } = s, f = u?.nodeId === e && u?.id === t && u?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: f,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: i === Gt.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!r,
    valid: f && l
  };
};
function py({ type: e = "source", position: t = ne.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: i = !0, id: s, onConnect: c, children: u, className: l, onMouseDown: f, onTouchStart: d, ...h }, p) {
  const g = s || null, v = e === "target", x = we(), m = cl(), { connectOnClick: k, noPanClassName: y, rfId: w } = de(fy, xe), { connectingFrom: b, connectingTo: I, clickConnecting: E, isPossibleEndHandle: j, connectionInProcess: P, clickConnectionInProcess: W, valid: M } = de(hy(m, g, e), xe);
  m || x.getState().onError?.("010", Oe.error010());
  const R = (_) => {
    const { defaultEdgeOptions: C, onConnect: D, hasDefaultEdges: $ } = x.getState(), T = {
      ...C,
      ..._
    };
    if ($) {
      const { edges: H, setEdges: B, onError: F } = x.getState();
      B(ol(T, H, { onError: F }));
    }
    D?.(T), c?.(T);
  }, O = (_) => {
    if (!m)
      return;
    const C = Mc(_.nativeEvent);
    if (r && (C && _.button === 0 || !C)) {
      const D = x.getState();
      di.onPointerDown(_.nativeEvent, {
        handleDomNode: _.currentTarget,
        autoPanOnConnect: D.autoPanOnConnect,
        connectionMode: D.connectionMode,
        connectionRadius: D.connectionRadius,
        domNode: D.domNode,
        nodeLookup: D.nodeLookup,
        lib: D.lib,
        isTarget: v,
        handleId: g,
        nodeId: m,
        flowId: D.rfId,
        panBy: D.panBy,
        cancelConnection: D.cancelConnection,
        onConnectStart: D.onConnectStart,
        onConnectEnd: (...$) => x.getState().onConnectEnd?.(...$),
        updateConnection: D.updateConnection,
        onConnect: R,
        isValidConnection: n || ((...$) => x.getState().isValidConnection?.(...$) ?? !0),
        getTransform: () => x.getState().transform,
        getFromHandle: () => x.getState().connection.fromHandle,
        autoPanSpeed: D.autoPanSpeed,
        dragThreshold: D.connectionDragThreshold
      });
    }
    C ? f?.(_) : d?.(_);
  }, N = (_) => {
    const { onClickConnectStart: C, onClickConnectEnd: D, connectionClickStartHandle: $, connectionMode: T, isValidConnection: H, lib: B, rfId: F, nodeLookup: Z, connection: q } = x.getState();
    if (!m || !$ && !r)
      return;
    if (!$) {
      C?.(_.nativeEvent, { nodeId: m, handleId: g, handleType: e }), x.setState({ connectionClickStartHandle: { nodeId: m, type: e, id: g } });
      return;
    }
    const te = _c(_.target), ae = n || H, { connection: Q, isValid: z } = di.isValid(_.nativeEvent, {
      handle: {
        nodeId: m,
        id: g,
        type: e
      },
      connectionMode: T,
      fromNodeId: $.nodeId,
      fromHandleId: $.id || null,
      fromType: $.type,
      isValidConnection: ae,
      flowId: F,
      doc: te,
      lib: B,
      nodeLookup: Z
    });
    z && Q && R(Q);
    const K = structuredClone(q);
    delete K.inProgress, K.toPosition = K.toHandle ? K.toHandle.position : null, D?.(_, K), x.setState({ connectionClickStartHandle: null });
  };
  return a.jsx("div", { "data-handleid": g, "data-nodeid": m, "data-handlepos": t, "data-id": `${w}-${m}-${g}-${e}`, className: Ie([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    y,
    l,
    {
      source: !v,
      target: v,
      connectable: o,
      connectablestart: r,
      connectableend: i,
      clickconnecting: E,
      connectingfrom: b,
      connectingto: I,
      valid: M,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!P || j) && (P || W ? i : r)
    }
  ]), onMouseDown: O, onTouchStart: O, onClick: k ? N : void 0, ref: p, ...h, children: u });
}
const nn = Ee(rl(py));
function gy({ data: e, isConnectable: t, sourcePosition: n = ne.Bottom }) {
  return a.jsxs(a.Fragment, { children: [e?.label, a.jsx(nn, { type: "source", position: n, isConnectable: t })] });
}
function yy({ data: e, isConnectable: t, targetPosition: n = ne.Top, sourcePosition: o = ne.Bottom }) {
  return a.jsxs(a.Fragment, { children: [a.jsx(nn, { type: "target", position: n, isConnectable: t }), e?.label, a.jsx(nn, { type: "source", position: o, isConnectable: t })] });
}
function my() {
  return null;
}
function xy({ data: e, isConnectable: t, targetPosition: n = ne.Top }) {
  return a.jsxs(a.Fragment, { children: [a.jsx(nn, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const Ko = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, ra = {
  input: gy,
  default: yy,
  output: xy,
  group: my
};
function wy(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const vy = (e) => {
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
function by({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = we(), { width: r, height: i, transformString: s, userSelectionActive: c } = de(vy, xe), u = al(), l = ce(null);
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
  const d = e ? (p) => {
    const g = o.getState().nodes.filter((v) => v.selected);
    e(p, g);
  } : void 0, h = (p) => {
    Object.prototype.hasOwnProperty.call(Ko, p.key) && (p.preventDefault(), u({
      direction: Ko[p.key],
      factor: p.shiftKey ? 4 : 1
    }));
  };
  return a.jsx("div", { className: Ie(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: s
  }, children: a.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: d, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : h, style: {
    width: r,
    height: i
  } }) });
}
const ia = typeof window < "u" ? window : void 0, Ny = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function ll({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, paneClickDistance: c, deleteKeyCode: u, selectionKeyCode: l, selectionOnDrag: f, selectionMode: d, onSelectionStart: h, onSelectionEnd: p, multiSelectionKeyCode: g, panActivationKeyCode: v, zoomActivationKeyCode: x, elementsSelectable: m, zoomOnScroll: k, zoomOnPinch: y, panOnScroll: w, panOnScrollSpeed: b, panOnScrollMode: I, zoomOnDoubleClick: E, panOnDrag: j, autoPanOnSelection: P, defaultViewport: W, translateExtent: M, minZoom: R, maxZoom: O, preventScrolling: N, onSelectionContextMenu: _, noWheelClassName: C, noPanClassName: D, disableKeyboardA11y: $, onViewportChange: T, isControlledViewport: H }) {
  const { nodesSelectionActive: B, userSelectionActive: F } = de(Ny, xe), Z = Hn(l, { target: ia }), q = Hn(v, { target: ia }), te = q || j, ae = q || w, Q = f && te !== !0, z = Z || F || Q;
  return ny({ deleteKeyCode: u, multiSelectionKeyCode: g }), a.jsx(iy, { onPaneContextMenu: i, elementsSelectable: m, zoomOnScroll: k, zoomOnPinch: y, panOnScroll: ae, panOnScrollSpeed: b, panOnScrollMode: I, zoomOnDoubleClick: E, panOnDrag: !Z && te, defaultViewport: W, translateExtent: M, minZoom: R, maxZoom: O, zoomActivationKeyCode: x, preventScrolling: N, noWheelClassName: C, noPanClassName: D, onViewportChange: T, isControlledViewport: H, paneClickDistance: c, selectionOnDrag: Q, children: a.jsxs(ly, { onSelectionStart: h, onSelectionEnd: p, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, panOnDrag: te, autoPanOnSelection: P, isSelecting: !!z, selectionMode: d, selectionKeyPressed: Z, paneClickDistance: c, selectionOnDrag: Q, children: [e, B && a.jsx(by, { onSelectionContextMenu: _, noPanClassName: D, disableKeyboardA11y: $ })] }) });
}
ll.displayName = "FlowRenderer";
const Sy = Ee(ll), Ey = (e) => (t) => e ? Mi(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function ky(e) {
  return de(pe(Ey(e), [e]), xe);
}
const Iy = (e) => e.updateNodeInternals;
function Cy() {
  const e = de(Iy), [t] = Y(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
function jy({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = we(), i = ce(null), s = ce(null), c = ce(e.sourcePosition), u = ce(e.targetPosition), l = ce(t), f = n && !!e.internals.handleBounds;
  return oe(() => {
    i.current && !e.hidden && (!f || s.current !== i.current) && (s.current && o?.unobserve(s.current), o?.observe(i.current), s.current = i.current);
  }, [f, e.hidden]), oe(() => () => {
    s.current && (o?.unobserve(s.current), s.current = null);
  }, []), oe(() => {
    if (i.current) {
      const d = l.current !== t, h = c.current !== e.sourcePosition, p = u.current !== e.targetPosition;
      (d || h || p) && (l.current = t, c.current = e.sourcePosition, u.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: i.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), i;
}
function _y({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: i, onDoubleClick: s, nodesDraggable: c, elementsSelectable: u, nodesConnectable: l, nodesFocusable: f, resizeObserver: d, noDragClassName: h, noPanClassName: p, disableKeyboardA11y: g, rfId: v, nodeTypes: x, nodeClickDistance: m, onError: k }) {
  const { node: y, internals: w, isParent: b } = de((z) => {
    const K = z.nodeLookup.get(e), se = z.parentLookup.has(e);
    return {
      node: K,
      internals: K.internals,
      isParent: se
    };
  }, xe);
  let I = y.type || "default", E = x?.[I] || ra[I];
  E === void 0 && (k?.("003", Oe.error003(I)), I = "default", E = x?.default || ra.default);
  const j = !!(y.draggable || c && typeof y.draggable > "u"), P = !!(y.selectable || u && typeof y.selectable > "u"), W = !!(y.connectable || l && typeof y.connectable > "u"), M = !!(y.focusable || f && typeof y.focusable > "u"), R = we(), O = Cc(y), N = jy({ node: y, nodeType: I, hasDimensions: O, resizeObserver: d }), _ = sl({
    nodeRef: N,
    disabled: y.hidden || !j,
    noDragClassName: h,
    handleSelector: y.dragHandle,
    nodeId: e,
    isSelectable: P,
    nodeClickDistance: m
  }), C = al();
  if (y.hidden)
    return null;
  const D = ct(y), $ = wy(y), T = P || j || t || n || o || r, H = n ? (z) => n(z, { ...w.userNode }) : void 0, B = o ? (z) => o(z, { ...w.userNode }) : void 0, F = r ? (z) => r(z, { ...w.userNode }) : void 0, Z = i ? (z) => i(z, { ...w.userNode }) : void 0, q = s ? (z) => s(z, { ...w.userNode }) : void 0, te = (z) => {
    const { selectNodesOnDrag: K, nodeDragThreshold: se } = R.getState();
    P && (!K || !j || se > 0) && fi({
      id: e,
      store: R,
      nodeRef: N
    }), t && t(z, { ...w.userNode });
  }, ae = (z) => {
    if (!(Ac(z.nativeEvent) || g)) {
      if (xc.includes(z.key) && P) {
        const K = z.key === "Escape";
        fi({
          id: e,
          store: R,
          unselect: K,
          nodeRef: N
        });
      } else if (j && y.selected && Object.prototype.hasOwnProperty.call(Ko, z.key)) {
        z.preventDefault();
        const { ariaLabelConfig: K } = R.getState();
        R.setState({
          ariaLiveMessage: K["node.a11yDescription.ariaLiveMessage"]({
            direction: z.key.replace("Arrow", "").toLowerCase(),
            x: ~~w.positionAbsolute.x,
            y: ~~w.positionAbsolute.y
          })
        }), C({
          direction: Ko[z.key],
          factor: z.shiftKey ? 4 : 1
        });
      }
    }
  }, Q = () => {
    if (g || !N.current?.matches(":focus-visible"))
      return;
    const { transform: z, width: K, height: se, autoPanOnNodeFocus: re, setCenter: U } = R.getState();
    if (!re)
      return;
    Mi(/* @__PURE__ */ new Map([[e, y]]), { x: 0, y: 0, width: K, height: se }, z, !0).length > 0 || U(y.position.x + D.width / 2, y.position.y + D.height / 2, {
      zoom: z[2]
    });
  };
  return a.jsx("div", { className: Ie([
    "react-flow__node",
    `react-flow__node-${I}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [p]: j
    },
    y.className,
    {
      selected: y.selected,
      selectable: P,
      parent: b,
      draggable: j,
      dragging: _
    }
  ]), ref: N, style: {
    zIndex: w.z,
    transform: `translate(${w.positionAbsolute.x}px,${w.positionAbsolute.y}px)`,
    pointerEvents: T ? "all" : "none",
    visibility: O ? "visible" : "hidden",
    ...y.style,
    ...$
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: H, onMouseMove: B, onMouseLeave: F, onContextMenu: Z, onClick: te, onDoubleClick: q, onKeyDown: M ? ae : void 0, tabIndex: M ? 0 : void 0, onFocus: M ? Q : void 0, role: y.ariaRole ?? (M ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": g ? void 0 : `${Zc}-${v}`, "aria-label": y.ariaLabel, ...y.domAttributes, children: a.jsx(dy, { value: e, children: a.jsx(E, { id: e, data: y.data, type: I, positionAbsoluteX: w.positionAbsolute.x, positionAbsoluteY: w.positionAbsolute.y, selected: y.selected ?? !1, selectable: P, draggable: j, deletable: y.deletable ?? !0, isConnectable: W, sourcePosition: y.sourcePosition, targetPosition: y.targetPosition, dragging: _, dragHandle: y.dragHandle, zIndex: w.z, parentId: y.parentId, ...D }) }) });
}
var Ay = Ee(_y);
const My = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function ul(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: i } = de(My, xe), s = ky(e.onlyRenderVisibleElements), c = Cy();
  return a.jsx("div", { className: "react-flow__nodes", style: ur, children: s.map((u) => (
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
    a.jsx(Ay, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: i }, u)
  )) });
}
ul.displayName = "NodeRenderer";
const Dy = Ee(ul);
function Py(e) {
  return de(pe((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const i = n.nodeLookup.get(r.source), s = n.nodeLookup.get(r.target);
        i && s && jp({
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
const $y = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return a.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, Ty = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return a.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, sa = {
  [Xo.Arrow]: $y,
  [Xo.ArrowClosed]: Ty
};
function Ry(e) {
  const t = we();
  return ye(() => Object.prototype.hasOwnProperty.call(sa, e) ? sa[e] : (t.getState().onError?.("009", Oe.error009(e)), null), [e]);
}
const zy = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: i = "strokeWidth", strokeWidth: s, orient: c = "auto-start-reverse" }) => {
  const u = Ry(t);
  return u ? a.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: i, orient: c, refX: "0", refY: "0", children: a.jsx(u, { color: n, strokeWidth: s }) }) : null;
}, dl = ({ defaultColor: e, rfId: t }) => {
  const n = de((i) => i.edges), o = de((i) => i.defaultEdgeOptions), r = ye(() => Rp(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? a.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: a.jsx("defs", { children: r.map((i) => a.jsx(zy, { id: i.id, type: i.type, color: i.color, width: i.width, height: i.height, markerUnits: i.markerUnits, strokeWidth: i.strokeWidth, orient: i.orient }, i.id)) }) }) : null;
};
dl.displayName = "MarkerDefinitions";
var Ly = Ee(dl);
function fl({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: i, labelBgPadding: s = [2, 4], labelBgBorderRadius: c = 2, children: u, className: l, ...f }) {
  const [d, h] = Y({ x: 1, y: 0, width: 0, height: 0 }), p = Ie(["react-flow__edge-textwrapper", l]), g = ce(null);
  return oe(() => {
    if (g.current) {
      const v = g.current.getBBox();
      h({
        x: v.x,
        y: v.y,
        width: v.width,
        height: v.height
      });
    }
  }, [n]), n ? a.jsxs("g", { transform: `translate(${e - d.width / 2} ${t - d.height / 2})`, className: p, visibility: d.width ? "visible" : "hidden", ...f, children: [r && a.jsx("rect", { width: d.width + 2 * s[0], x: -s[0], y: -s[1], height: d.height + 2 * s[1], className: "react-flow__edge-textbg", style: i, rx: c, ry: c }), a.jsx("text", { className: "react-flow__edge-text", y: d.height / 2, dy: "0.3em", ref: g, style: o, children: n }), u] }) : null;
}
fl.displayName = "EdgeText";
const Vy = Ee(fl);
function Un({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: c, labelBgBorderRadius: u, interactionWidth: l = 20, ...f }) {
  return a.jsxs(a.Fragment, { children: [a.jsx("path", { ...f, d: e, fill: "none", className: Ie(["react-flow__edge-path", f.className]) }), l ? a.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, o && Ke(t) && Ke(n) ? a.jsx(Vy, { x: t, y: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: c, labelBgBorderRadius: u }) : null] });
}
function aa({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === ne.Left || e === ne.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function hl({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: r, targetPosition: i = ne.Top }) {
  const [s, c] = aa({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [u, l] = aa({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [f, d, h, p] = Dc({
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
function pl(e) {
  return Ee(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: c, label: u, labelStyle: l, labelShowBg: f, labelBgStyle: d, labelBgPadding: h, labelBgBorderRadius: p, style: g, markerEnd: v, markerStart: x, interactionWidth: m }) => {
    const [k, y, w] = hl({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: c
    }), b = e.isInternal ? void 0 : t;
    return a.jsx(Un, { id: b, path: k, labelX: y, labelY: w, label: u, labelStyle: l, labelShowBg: f, labelBgStyle: d, labelBgPadding: h, labelBgBorderRadius: p, style: g, markerEnd: v, markerStart: x, interactionWidth: m });
  });
}
const Hy = pl({ isInternal: !1 }), gl = pl({ isInternal: !0 });
Hy.displayName = "SimpleBezierEdge";
gl.displayName = "SimpleBezierEdgeInternal";
function yl(e) {
  return Ee(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: d, style: h, sourcePosition: p = ne.Bottom, targetPosition: g = ne.Top, markerEnd: v, markerStart: x, pathOptions: m, interactionWidth: k }) => {
    const [y, w, b] = qo({
      sourceX: n,
      sourceY: o,
      sourcePosition: p,
      targetX: r,
      targetY: i,
      targetPosition: g,
      borderRadius: m?.borderRadius,
      offset: m?.offset,
      stepPosition: m?.stepPosition
    }), I = e.isInternal ? void 0 : t;
    return a.jsx(Un, { id: I, path: y, labelX: w, labelY: b, label: s, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: d, style: h, markerEnd: v, markerStart: x, interactionWidth: k });
  });
}
const ml = yl({ isInternal: !1 }), xl = yl({ isInternal: !0 });
ml.displayName = "SmoothStepEdge";
xl.displayName = "SmoothStepEdgeInternal";
function wl(e) {
  return Ee(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return a.jsx(ml, { ...n, id: o, pathOptions: ye(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Oy = wl({ isInternal: !1 }), vl = wl({ isInternal: !0 });
Oy.displayName = "StepEdge";
vl.displayName = "StepEdgeInternal";
function bl(e) {
  return Ee(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: d, style: h, markerEnd: p, markerStart: g, interactionWidth: v }) => {
    const [x, m, k] = Rc({ sourceX: n, sourceY: o, targetX: r, targetY: i }), y = e.isInternal ? void 0 : t;
    return a.jsx(Un, { id: y, path: x, labelX: m, labelY: k, label: s, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: f, labelBgBorderRadius: d, style: h, markerEnd: p, markerStart: g, interactionWidth: v });
  });
}
const Fy = bl({ isInternal: !1 }), Nl = bl({ isInternal: !0 });
Fy.displayName = "StraightEdge";
Nl.displayName = "StraightEdgeInternal";
function Sl(e) {
  return Ee(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s = ne.Bottom, targetPosition: c = ne.Top, label: u, labelStyle: l, labelShowBg: f, labelBgStyle: d, labelBgPadding: h, labelBgBorderRadius: p, style: g, markerEnd: v, markerStart: x, pathOptions: m, interactionWidth: k }) => {
    const [y, w, b] = Pc({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: c,
      curvature: m?.curvature
    }), I = e.isInternal ? void 0 : t;
    return a.jsx(Un, { id: I, path: y, labelX: w, labelY: b, label: u, labelStyle: l, labelShowBg: f, labelBgStyle: d, labelBgPadding: h, labelBgBorderRadius: p, style: g, markerEnd: v, markerStart: x, interactionWidth: k });
  });
}
const By = Sl({ isInternal: !1 }), El = Sl({ isInternal: !0 });
By.displayName = "BezierEdge";
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
}, Wy = (e, t, n) => n === ne.Left ? e - t : n === ne.Right ? e + t : e, Xy = (e, t, n) => n === ne.Top ? e - t : n === ne.Bottom ? e + t : e, ua = "react-flow__edgeupdater";
function da({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: i, onMouseOut: s, type: c }) {
  return a.jsx("circle", { onMouseDown: r, onMouseEnter: i, onMouseOut: s, className: Ie([ua, `${ua}-${c}`]), cx: Wy(t, o, e), cy: Xy(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function Yy({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: i, targetY: s, sourcePosition: c, targetPosition: u, onReconnect: l, onReconnectStart: f, onReconnectEnd: d, setReconnecting: h, setUpdateHover: p }) {
  const g = we(), v = (w, b) => {
    if (w.button !== 0)
      return;
    const { autoPanOnConnect: I, domNode: E, connectionMode: j, connectionRadius: P, lib: W, onConnectStart: M, cancelConnection: R, nodeLookup: O, rfId: N, panBy: _, updateConnection: C } = g.getState(), D = b.type === "target", $ = (B, F) => {
      h(!1), d?.(B, n, b.type, F);
    }, T = (B) => l?.(n, B), H = (B, F) => {
      h(!0), f?.(w, n, b.type), M?.(B, F);
    };
    di.onPointerDown(w.nativeEvent, {
      autoPanOnConnect: I,
      connectionMode: j,
      connectionRadius: P,
      domNode: E,
      handleId: b.id,
      nodeId: b.nodeId,
      nodeLookup: O,
      isTarget: D,
      edgeUpdaterType: b.type,
      lib: W,
      flowId: N,
      cancelConnection: R,
      panBy: _,
      isValidConnection: (...B) => g.getState().isValidConnection?.(...B) ?? !0,
      onConnect: T,
      onConnectStart: H,
      onConnectEnd: (...B) => g.getState().onConnectEnd?.(...B),
      onReconnectEnd: $,
      updateConnection: C,
      getTransform: () => g.getState().transform,
      getFromHandle: () => g.getState().connection.fromHandle,
      dragThreshold: g.getState().connectionDragThreshold,
      handleDomNode: w.currentTarget
    });
  }, x = (w) => v(w, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), m = (w) => v(w, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), k = () => p(!0), y = () => p(!1);
  return a.jsxs(a.Fragment, { children: [(e === !0 || e === "source") && a.jsx(da, { position: c, centerX: o, centerY: r, radius: t, onMouseDown: x, onMouseEnter: k, onMouseOut: y, type: "source" }), (e === !0 || e === "target") && a.jsx(da, { position: u, centerX: i, centerY: s, radius: t, onMouseDown: m, onMouseEnter: k, onMouseOut: y, type: "target" })] });
}
function qy({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: i, onContextMenu: s, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, reconnectRadius: f, onReconnect: d, onReconnectStart: h, onReconnectEnd: p, rfId: g, edgeTypes: v, noPanClassName: x, onError: m, disableKeyboardA11y: k }) {
  let y = de((U) => U.edgeLookup.get(e));
  const w = de((U) => U.defaultEdgeOptions);
  y = w ? { ...w, ...y } : y;
  let b = y.type || "default", I = v?.[b] || ca[b];
  I === void 0 && (m?.("011", Oe.error011(b)), b = "default", I = v?.default || ca.default);
  const E = !!(y.focusable || t && typeof y.focusable > "u"), j = typeof d < "u" && (y.reconnectable || n && typeof y.reconnectable > "u"), P = !!(y.selectable || o && typeof y.selectable > "u"), W = ce(null), [M, R] = Y(!1), [O, N] = Y(!1), _ = we(), { zIndex: C, sourceX: D, sourceY: $, targetX: T, targetY: H, sourcePosition: B, targetPosition: F } = de(pe((U) => {
    const ee = U.nodeLookup.get(y.source), le = U.nodeLookup.get(y.target);
    if (!ee || !le)
      return {
        zIndex: y.zIndex,
        ...la
      };
    const L = Tp({
      id: e,
      sourceNode: ee,
      targetNode: le,
      sourceHandle: y.sourceHandle || null,
      targetHandle: y.targetHandle || null,
      connectionMode: U.connectionMode,
      onError: m
    });
    return {
      zIndex: Cp({
        selected: y.selected,
        zIndex: y.zIndex,
        sourceNode: ee,
        targetNode: le,
        elevateOnSelect: U.elevateEdgesOnSelect,
        zIndexMode: U.zIndexMode
      }),
      ...L || la
    };
  }, [y.source, y.target, y.sourceHandle, y.targetHandle, y.selected, y.zIndex]), xe), Z = ye(() => y.markerStart ? `url('#${li(y.markerStart, g)}')` : void 0, [y.markerStart, g]), q = ye(() => y.markerEnd ? `url('#${li(y.markerEnd, g)}')` : void 0, [y.markerEnd, g]);
  if (y.hidden || D === null || $ === null || T === null || H === null)
    return null;
  const te = (U) => {
    const { addSelectedEdges: ee, unselectNodesAndEdges: le, multiSelectionActive: L } = _.getState();
    P && (_.setState({ nodesSelectionActive: !1 }), y.selected && L ? (le({ nodes: [], edges: [y] }), W.current?.blur()) : ee([e])), r && r(U, y);
  }, ae = i ? (U) => {
    i(U, { ...y });
  } : void 0, Q = s ? (U) => {
    s(U, { ...y });
  } : void 0, z = c ? (U) => {
    c(U, { ...y });
  } : void 0, K = u ? (U) => {
    u(U, { ...y });
  } : void 0, se = l ? (U) => {
    l(U, { ...y });
  } : void 0, re = (U) => {
    if (!k && xc.includes(U.key) && P) {
      const { unselectNodesAndEdges: ee, addSelectedEdges: le } = _.getState();
      U.key === "Escape" ? (W.current?.blur(), ee({ edges: [y] })) : le([e]);
    }
  };
  return a.jsx("svg", { style: { zIndex: C }, children: a.jsxs("g", { className: Ie([
    "react-flow__edge",
    `react-flow__edge-${b}`,
    y.className,
    x,
    {
      selected: y.selected,
      animated: y.animated,
      inactive: !P && !r,
      updating: M,
      selectable: P
    }
  ]), onClick: te, onDoubleClick: ae, onContextMenu: Q, onMouseEnter: z, onMouseMove: K, onMouseLeave: se, onKeyDown: E ? re : void 0, tabIndex: E ? 0 : void 0, role: y.ariaRole ?? (E ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": y.ariaLabel === null ? void 0 : y.ariaLabel || `Edge from ${y.source} to ${y.target}`, "aria-describedby": E ? `${Gc}-${g}` : void 0, ref: W, ...y.domAttributes, children: [!O && a.jsx(I, { id: e, source: y.source, target: y.target, type: y.type, selected: y.selected, animated: y.animated, selectable: P, deletable: y.deletable ?? !0, label: y.label, labelStyle: y.labelStyle, labelShowBg: y.labelShowBg, labelBgStyle: y.labelBgStyle, labelBgPadding: y.labelBgPadding, labelBgBorderRadius: y.labelBgBorderRadius, sourceX: D, sourceY: $, targetX: T, targetY: H, sourcePosition: B, targetPosition: F, data: y.data, style: y.style, sourceHandleId: y.sourceHandle, targetHandleId: y.targetHandle, markerStart: Z, markerEnd: q, pathOptions: "pathOptions" in y ? y.pathOptions : void 0, interactionWidth: y.interactionWidth }), j && a.jsx(Yy, { edge: y, isReconnectable: j, reconnectRadius: f, onReconnect: d, onReconnectStart: h, onReconnectEnd: p, sourceX: D, sourceY: $, targetX: T, targetY: H, sourcePosition: B, targetPosition: F, setUpdateHover: R, setReconnecting: N })] }) });
}
var Ky = Ee(qy);
const Uy = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function kl({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: i, onEdgeContextMenu: s, onEdgeMouseEnter: c, onEdgeMouseMove: u, onEdgeMouseLeave: l, onEdgeClick: f, reconnectRadius: d, onEdgeDoubleClick: h, onReconnectStart: p, onReconnectEnd: g, disableKeyboardA11y: v }) {
  const { edgesFocusable: x, edgesReconnectable: m, elementsSelectable: k, onError: y } = de(Uy, xe), w = Py(t);
  return a.jsxs("div", { className: "react-flow__edges", children: [a.jsx(Ly, { defaultColor: e, rfId: n }), w.map((b) => a.jsx(Ky, { id: b, edgesFocusable: x, edgesReconnectable: m, elementsSelectable: k, noPanClassName: r, onReconnect: i, onContextMenu: s, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, onClick: f, reconnectRadius: d, onDoubleClick: h, onReconnectStart: p, onReconnectEnd: g, rfId: n, onError: y, edgeTypes: o, disableKeyboardA11y: v }, b))] });
}
kl.displayName = "EdgeRenderer";
const Zy = Ee(kl), Gy = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function Jy({ children: e }) {
  const t = de(Gy);
  return a.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function Qy(e) {
  const t = Hi(), n = ce(!1);
  oe(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const em = (e) => e.panZoom?.syncViewport;
function tm(e) {
  const t = de(em), n = we();
  return oe(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function nm(e) {
  return e.connection.inProgress ? { ...e.connection, to: rn(e.connection.to, e.transform) } : { ...e.connection };
}
function om(e) {
  return nm;
}
function rm(e) {
  const t = om();
  return de(t, xe);
}
const im = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function sm({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: i, height: s, isValid: c, inProgress: u } = de(im, xe);
  return !(i && r && u) ? null : a.jsx("svg", { style: e, width: i, height: s, className: "react-flow__connectionline react-flow__container", children: a.jsx("g", { className: Ie(["react-flow__connection", bc(c)]), children: a.jsx(Il, { style: t, type: n, CustomComponent: o, isValid: c }) }) });
}
const Il = ({ style: e, type: t = pt.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: i, fromNode: s, fromHandle: c, fromPosition: u, to: l, toNode: f, toHandle: d, toPosition: h, pointer: p } = rm();
  if (!r)
    return;
  if (n)
    return a.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: s, fromHandle: c, fromX: i.x, fromY: i.y, toX: l.x, toY: l.y, fromPosition: u, toPosition: h, connectionStatus: bc(o), toNode: f, toHandle: d, pointer: p });
  let g = "";
  const v = {
    sourceX: i.x,
    sourceY: i.y,
    sourcePosition: u,
    targetX: l.x,
    targetY: l.y,
    targetPosition: h
  };
  switch (t) {
    case pt.Bezier:
      [g] = Pc(v);
      break;
    case pt.SimpleBezier:
      [g] = hl(v);
      break;
    case pt.Step:
      [g] = qo({
        ...v,
        borderRadius: 0
      });
      break;
    case pt.SmoothStep:
      [g] = qo(v);
      break;
    default:
      [g] = Rc(v);
  }
  return a.jsx("path", { d: g, fill: "none", className: "react-flow__connection-path", style: e });
};
Il.displayName = "ConnectionLine";
const am = {};
function fa(e = am) {
  ce(e), we(), oe(() => {
  }, [e]);
}
function cm() {
  we(), ce(!1), oe(() => {
  }, []);
}
function Cl({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: i, onEdgeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: f, onSelectionContextMenu: d, onSelectionStart: h, onSelectionEnd: p, connectionLineType: g, connectionLineStyle: v, connectionLineComponent: x, connectionLineContainerStyle: m, selectionKeyCode: k, selectionOnDrag: y, selectionMode: w, multiSelectionKeyCode: b, panActivationKeyCode: I, zoomActivationKeyCode: E, deleteKeyCode: j, onlyRenderVisibleElements: P, elementsSelectable: W, defaultViewport: M, translateExtent: R, minZoom: O, maxZoom: N, preventScrolling: _, defaultMarkerColor: C, zoomOnScroll: D, zoomOnPinch: $, panOnScroll: T, panOnScrollSpeed: H, panOnScrollMode: B, zoomOnDoubleClick: F, panOnDrag: Z, autoPanOnSelection: q, onPaneClick: te, onPaneMouseEnter: ae, onPaneMouseMove: Q, onPaneMouseLeave: z, onPaneScroll: K, onPaneContextMenu: se, paneClickDistance: re, nodeClickDistance: U, onEdgeContextMenu: ee, onEdgeMouseEnter: le, onEdgeMouseMove: L, onEdgeMouseLeave: G, reconnectRadius: fe, onReconnect: me, onReconnectStart: ge, onReconnectEnd: _e, noDragClassName: Fe, noWheelClassName: wt, noPanClassName: rt, disableKeyboardA11y: Be, nodeExtent: be, rfId: De, viewport: Re, onViewportChange: We }) {
  return fa(e), fa(t), cm(), Qy(n), tm(Re), a.jsx(Sy, { onPaneClick: te, onPaneMouseEnter: ae, onPaneMouseMove: Q, onPaneMouseLeave: z, onPaneContextMenu: se, onPaneScroll: K, paneClickDistance: re, deleteKeyCode: j, selectionKeyCode: k, selectionOnDrag: y, selectionMode: w, onSelectionStart: h, onSelectionEnd: p, multiSelectionKeyCode: b, panActivationKeyCode: I, zoomActivationKeyCode: E, elementsSelectable: W, zoomOnScroll: D, zoomOnPinch: $, zoomOnDoubleClick: F, panOnScroll: T, panOnScrollSpeed: H, panOnScrollMode: B, panOnDrag: Z, autoPanOnSelection: q, defaultViewport: M, translateExtent: R, minZoom: O, maxZoom: N, onSelectionContextMenu: d, preventScrolling: _, noDragClassName: Fe, noWheelClassName: wt, noPanClassName: rt, disableKeyboardA11y: Be, onViewportChange: We, isControlledViewport: !!Re, children: a.jsxs(Jy, { children: [a.jsx(Zy, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: s, onReconnect: me, onReconnectStart: ge, onReconnectEnd: _e, onlyRenderVisibleElements: P, onEdgeContextMenu: ee, onEdgeMouseEnter: le, onEdgeMouseMove: L, onEdgeMouseLeave: G, reconnectRadius: fe, defaultMarkerColor: C, noPanClassName: rt, disableKeyboardA11y: Be, rfId: De }), a.jsx(sm, { style: v, type: g, component: x, containerStyle: m }), a.jsx("div", { className: "react-flow__edgelabel-renderer" }), a.jsx(Dy, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: i, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: f, nodeClickDistance: U, onlyRenderVisibleElements: P, noPanClassName: rt, noDragClassName: Fe, disableKeyboardA11y: Be, nodeExtent: be, rfId: De }), a.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Cl.displayName = "GraphView";
const lm = Ee(Cl), um = Ic(), ha = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: c, minZoom: u = 0.5, maxZoom: l = 2, nodeOrigin: f, nodeExtent: d, zIndexMode: h = "basic" } = {}) => {
  const p = /* @__PURE__ */ new Map(), g = /* @__PURE__ */ new Map(), v = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), m = o ?? t ?? [], k = n ?? e ?? [], y = f ?? [0, 0], w = d ?? Rn;
  Vc(v, x, m);
  const { nodesInitialized: b } = ui(k, p, g, {
    nodeOrigin: y,
    nodeExtent: w,
    zIndexMode: h
  });
  let I = [0, 0, 1];
  if (s && r && i) {
    const E = qn(p, {
      filter: (M) => !!((M.width || M.initialWidth) && (M.height || M.initialHeight))
    }), { x: j, y: P, zoom: W } = Pi(E, r, i, u, l, c?.padding ?? 0.1);
    I = [j, P, W];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: i ?? 0,
    transform: I,
    nodes: k,
    nodesInitialized: b,
    nodeLookup: p,
    parentLookup: g,
    edges: m,
    edgeLookup: x,
    connectionLookup: v,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: o !== void 0,
    panZoom: null,
    minZoom: u,
    maxZoom: l,
    translateExtent: Rn,
    nodeExtent: w,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: Gt.Strict,
    domNode: null,
    paneDragging: !1,
    noPanClassName: "nopan",
    nodeOrigin: y,
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
    onError: um,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: wc,
    zIndexMode: h,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, dm = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: c, minZoom: u, maxZoom: l, nodeOrigin: f, nodeExtent: d, zIndexMode: h }) => Eg((p, g) => {
  async function v() {
    const { nodeLookup: x, panZoom: m, fitViewOptions: k, fitViewResolver: y, width: w, height: b, minZoom: I, maxZoom: E } = g();
    m && (await vp({
      nodes: x,
      width: w,
      height: b,
      panZoom: m,
      minZoom: I,
      maxZoom: E
    }, k), y?.resolve(!0), p({ fitViewResolver: null }));
  }
  return {
    ...ha({
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
    setNodes: (x) => {
      const { nodeLookup: m, parentLookup: k, nodeOrigin: y, elevateNodesOnSelect: w, fitViewQueued: b, zIndexMode: I, nodesSelectionActive: E } = g(), { nodesInitialized: j, hasSelectedNodes: P } = ui(x, m, k, {
        nodeOrigin: y,
        nodeExtent: d,
        elevateNodesOnSelect: w,
        checkEquality: !0,
        zIndexMode: I
      }), W = E && P;
      b && j ? (v(), p({
        nodes: x,
        nodesInitialized: j,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: W
      })) : p({ nodes: x, nodesInitialized: j, nodesSelectionActive: W });
    },
    setEdges: (x) => {
      const { connectionLookup: m, edgeLookup: k } = g();
      Vc(m, k, x), p({ edges: x });
    },
    setDefaultNodesAndEdges: (x, m) => {
      if (x) {
        const { setNodes: k } = g();
        k(x), p({ hasDefaultNodes: !0 });
      }
      if (m) {
        const { setEdges: k } = g();
        k(m), p({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (x) => {
      const { triggerNodeChanges: m, nodeLookup: k, parentLookup: y, domNode: w, nodeOrigin: b, nodeExtent: I, debug: E, fitViewQueued: j, zIndexMode: P } = g(), { changes: W, updatedInternals: M } = Bp(x, k, y, w, b, I, P);
      M && (Vp(k, y, { nodeOrigin: b, nodeExtent: I, zIndexMode: P }), j ? (v(), p({ fitViewQueued: !1, fitViewOptions: void 0 })) : p({}), W?.length > 0 && (E && console.log("React Flow: trigger node changes", W), m?.(W)));
    },
    updateNodePositions: (x, m = !1) => {
      const k = [];
      let y = [];
      const { nodeLookup: w, triggerNodeChanges: b, connection: I, updateConnection: E, onNodesChangeMiddlewareMap: j } = g();
      for (const [P, W] of x) {
        const M = w.get(P), R = !!(M?.expandParent && M?.parentId && W?.position), O = {
          id: P,
          type: "position",
          position: R ? {
            x: Math.max(0, W.position.x),
            y: Math.max(0, W.position.y)
          } : W.position,
          dragging: m
        };
        if (M && I.inProgress && I.fromNode.id === M.id) {
          const N = Tt(M, I.fromHandle, ne.Left, !0);
          E({ ...I, from: N });
        }
        R && M.parentId && k.push({
          id: P,
          parentId: M.parentId,
          rect: {
            ...W.internals.positionAbsolute,
            width: W.measured.width ?? 0,
            height: W.measured.height ?? 0
          }
        }), y.push(O);
      }
      if (k.length > 0) {
        const { parentLookup: P, nodeOrigin: W } = g(), M = Vi(k, w, P, W);
        y.push(...M);
      }
      for (const P of j.values())
        y = P(y);
      b(y);
    },
    triggerNodeChanges: (x) => {
      const { onNodesChange: m, setNodes: k, nodes: y, hasDefaultNodes: w, debug: b } = g();
      if (x?.length) {
        if (w) {
          const I = el(x, y);
          k(I);
        }
        b && console.log("React Flow: trigger node changes", x), m?.(x);
      }
    },
    triggerEdgeChanges: (x) => {
      const { onEdgesChange: m, setEdges: k, edges: y, hasDefaultEdges: w, debug: b } = g();
      if (x?.length) {
        if (w) {
          const I = tl(x, y);
          k(I);
        }
        b && console.log("React Flow: trigger edge changes", x), m?.(x);
      }
    },
    addSelectedNodes: (x) => {
      const { multiSelectionActive: m, edgeLookup: k, nodeLookup: y, triggerNodeChanges: w, triggerEdgeChanges: b } = g();
      if (m) {
        const I = x.map((E) => It(E, !0));
        w(I);
        return;
      }
      w(Xt(y, /* @__PURE__ */ new Set([...x]), !0)), b(Xt(k));
    },
    addSelectedEdges: (x) => {
      const { multiSelectionActive: m, edgeLookup: k, nodeLookup: y, triggerNodeChanges: w, triggerEdgeChanges: b } = g();
      if (m) {
        const I = x.map((E) => It(E, !0));
        b(I);
        return;
      }
      b(Xt(k, /* @__PURE__ */ new Set([...x]))), w(Xt(y, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: x, edges: m } = {}) => {
      const { edges: k, nodes: y, nodeLookup: w, triggerNodeChanges: b, triggerEdgeChanges: I } = g(), E = x || y, j = m || k, P = [];
      for (const M of E) {
        if (!M.selected)
          continue;
        const R = w.get(M.id);
        R && (R.selected = !1), P.push(It(M.id, !1));
      }
      const W = [];
      for (const M of j)
        M.selected && W.push(It(M.id, !1));
      b(P), I(W);
    },
    setMinZoom: (x) => {
      const { panZoom: m, maxZoom: k } = g();
      m?.setScaleExtent([x, k]), p({ minZoom: x });
    },
    setMaxZoom: (x) => {
      const { panZoom: m, minZoom: k } = g();
      m?.setScaleExtent([k, x]), p({ maxZoom: x });
    },
    setTranslateExtent: (x) => {
      g().panZoom?.setTranslateExtent(x), p({ translateExtent: x });
    },
    resetSelectedElements: () => {
      const { edges: x, nodes: m, triggerNodeChanges: k, triggerEdgeChanges: y, elementsSelectable: w } = g();
      if (!w)
        return;
      const b = m.reduce((E, j) => j.selected ? [...E, It(j.id, !1)] : E, []), I = x.reduce((E, j) => j.selected ? [...E, It(j.id, !1)] : E, []);
      k(b), y(I);
    },
    setNodeExtent: (x) => {
      const { nodes: m, nodeLookup: k, parentLookup: y, nodeOrigin: w, elevateNodesOnSelect: b, nodeExtent: I, zIndexMode: E } = g();
      x[0][0] === I[0][0] && x[0][1] === I[0][1] && x[1][0] === I[1][0] && x[1][1] === I[1][1] || (ui(m, k, y, {
        nodeOrigin: w,
        nodeExtent: x,
        elevateNodesOnSelect: b,
        checkEquality: !1,
        zIndexMode: E
      }), p({ nodeExtent: x }));
    },
    panBy: (x) => {
      const { transform: m, width: k, height: y, panZoom: w, translateExtent: b } = g();
      return Wp({ delta: x, panZoom: w, transform: m, translateExtent: b, width: k, height: y });
    },
    setCenter: async (x, m, k) => {
      const { width: y, height: w, maxZoom: b, panZoom: I } = g();
      if (!I)
        return !1;
      const E = typeof k?.zoom < "u" ? k.zoom : b;
      return await I.setViewport({
        x: y / 2 - x * E,
        y: w / 2 - m * E,
        zoom: E
      }, { duration: k?.duration, ease: k?.ease, interpolate: k?.interpolate }), !0;
    },
    cancelConnection: () => {
      p({
        connection: { ...vc }
      });
    },
    updateConnection: (x) => {
      p({ connection: x });
    },
    reset: () => p({ ...ha() })
  };
}, Object.is);
function fm({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: i, initialMinZoom: s, initialMaxZoom: c, initialFitViewOptions: u, fitView: l, nodeOrigin: f, nodeExtent: d, zIndexMode: h, children: p }) {
  const [g] = Y(() => dm({
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
  return a.jsx(jg, { value: g, children: a.jsx(Jg, { children: p }) });
}
function hm({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: i, height: s, fitView: c, fitViewOptions: u, minZoom: l, maxZoom: f, nodeOrigin: d, nodeExtent: h, zIndexMode: p }) {
  return Bn(cr) ? a.jsx(a.Fragment, { children: e }) : a.jsx(fm, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: i, initialHeight: s, fitView: c, initialFitViewOptions: u, initialMinZoom: l, initialMaxZoom: f, nodeOrigin: d, nodeExtent: h, zIndexMode: p, children: e });
}
const pm = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function gm({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: i, edgeTypes: s, onNodeClick: c, onEdgeClick: u, onInit: l, onMove: f, onMoveStart: d, onMoveEnd: h, onConnect: p, onConnectStart: g, onConnectEnd: v, onClickConnectStart: x, onClickConnectEnd: m, onNodeMouseEnter: k, onNodeMouseMove: y, onNodeMouseLeave: w, onNodeContextMenu: b, onNodeDoubleClick: I, onNodeDragStart: E, onNodeDrag: j, onNodeDragStop: P, onNodesDelete: W, onEdgesDelete: M, onDelete: R, onSelectionChange: O, onSelectionDragStart: N, onSelectionDrag: _, onSelectionDragStop: C, onSelectionContextMenu: D, onSelectionStart: $, onSelectionEnd: T, onBeforeDelete: H, connectionMode: B, connectionLineType: F = pt.Bezier, connectionLineStyle: Z, connectionLineComponent: q, connectionLineContainerStyle: te, deleteKeyCode: ae = "Backspace", selectionKeyCode: Q = "Shift", selectionOnDrag: z = !1, selectionMode: K = zn.Full, panActivationKeyCode: se = "Space", multiSelectionKeyCode: re = Vn() ? "Meta" : "Control", zoomActivationKeyCode: U = Vn() ? "Meta" : "Control", snapToGrid: ee, snapGrid: le, onlyRenderVisibleElements: L = !1, selectNodesOnDrag: G, nodesDraggable: fe, autoPanOnNodeFocus: me, nodesConnectable: ge, nodesFocusable: _e, nodeOrigin: Fe = Jc, edgesFocusable: wt, edgesReconnectable: rt, elementsSelectable: Be = !0, defaultViewport: be = Og, minZoom: De = 0.5, maxZoom: Re = 2, translateExtent: We = Rn, preventScrolling: Jn = !0, nodeExtent: vt, defaultMarkerColor: Lt = "#b1b1b7", zoomOnScroll: lt = !0, zoomOnPinch: bt = !0, panOnScroll: Nt = !1, panOnScrollSpeed: et = 0.5, panOnScrollMode: Qn = _t.Free, zoomOnDoubleClick: ze = !0, panOnDrag: eo = !0, onPaneClick: Ne, onPaneMouseEnter: Xe, onPaneMouseMove: an, onPaneMouseLeave: Se, onPaneScroll: St, onPaneContextMenu: cn, paneClickDistance: je = 1, nodeClickDistance: Et = 0, children: ut, onReconnect: pr, onReconnectStart: to, onReconnectEnd: no, onEdgeContextMenu: ln, onEdgeDoubleClick: gr, onEdgeMouseEnter: un, onEdgeMouseMove: Vt, onEdgeMouseLeave: Le, reconnectRadius: dn = 10, onNodesChange: fn, onEdgesChange: hn, noDragClassName: Ht = "nodrag", noWheelClassName: yr = "nowheel", noPanClassName: oo = "nopan", fitView: ro, fitViewOptions: io, connectOnClick: so, attributionPosition: ao, proOptions: co, defaultEdgeOptions: mr, elevateNodesOnSelect: pn = !0, elevateEdgesOnSelect: xr = !1, disableKeyboardA11y: lo = !1, autoPanOnConnect: wr, autoPanOnNodeDrag: vr, autoPanOnSelection: br = !0, autoPanSpeed: gn, connectionRadius: Nr, isValidConnection: Sr, onError: Er, style: kr, id: uo, nodeDragThreshold: Ir, connectionDragThreshold: Cr, viewport: fo, onViewportChange: ho, width: jr, height: _r, colorMode: Ar = "light", debug: Mr, onScroll: po, ariaLabelConfig: go, zIndexMode: yn = "basic", ...yo }, mo) {
  const Ot = uo || "1", Dr = Xg(Ar), Pr = pe((mn) => {
    mn.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), po?.(mn);
  }, [po]);
  return a.jsx("div", { "data-testid": "rf__wrapper", ...yo, onScroll: Pr, style: { ...kr, ...pm }, ref: mo, className: Ie(["react-flow", r, Dr]), id: uo, role: "application", children: a.jsxs(hm, { nodes: e, edges: t, width: jr, height: _r, fitView: ro, fitViewOptions: io, minZoom: De, maxZoom: Re, nodeOrigin: Fe, nodeExtent: vt, zIndexMode: yn, children: [a.jsx(Wg, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: p, onConnectStart: g, onConnectEnd: v, onClickConnectStart: x, onClickConnectEnd: m, nodesDraggable: fe, autoPanOnNodeFocus: me, nodesConnectable: ge, nodesFocusable: _e, edgesFocusable: wt, edgesReconnectable: rt, elementsSelectable: Be, elevateNodesOnSelect: pn, elevateEdgesOnSelect: xr, minZoom: De, maxZoom: Re, nodeExtent: vt, onNodesChange: fn, onEdgesChange: hn, snapToGrid: ee, snapGrid: le, connectionMode: B, translateExtent: We, connectOnClick: so, defaultEdgeOptions: mr, fitView: ro, fitViewOptions: io, onNodesDelete: W, onEdgesDelete: M, onDelete: R, onNodeDragStart: E, onNodeDrag: j, onNodeDragStop: P, onSelectionDrag: _, onSelectionDragStart: N, onSelectionDragStop: C, onMove: f, onMoveStart: d, onMoveEnd: h, noPanClassName: oo, nodeOrigin: Fe, rfId: Ot, autoPanOnConnect: wr, autoPanOnNodeDrag: vr, autoPanSpeed: gn, onError: Er, connectionRadius: Nr, isValidConnection: Sr, selectNodesOnDrag: G, nodeDragThreshold: Ir, connectionDragThreshold: Cr, onBeforeDelete: H, debug: Mr, ariaLabelConfig: go, zIndexMode: yn }), a.jsx(lm, { onInit: l, onNodeClick: c, onEdgeClick: u, onNodeMouseEnter: k, onNodeMouseMove: y, onNodeMouseLeave: w, onNodeContextMenu: b, onNodeDoubleClick: I, nodeTypes: i, edgeTypes: s, connectionLineType: F, connectionLineStyle: Z, connectionLineComponent: q, connectionLineContainerStyle: te, selectionKeyCode: Q, selectionOnDrag: z, selectionMode: K, deleteKeyCode: ae, multiSelectionKeyCode: re, panActivationKeyCode: se, zoomActivationKeyCode: U, onlyRenderVisibleElements: L, defaultViewport: be, translateExtent: We, minZoom: De, maxZoom: Re, preventScrolling: Jn, zoomOnScroll: lt, zoomOnPinch: bt, zoomOnDoubleClick: ze, panOnScroll: Nt, panOnScrollSpeed: et, panOnScrollMode: Qn, panOnDrag: eo, autoPanOnSelection: br, onPaneClick: Ne, onPaneMouseEnter: Xe, onPaneMouseMove: an, onPaneMouseLeave: Se, onPaneScroll: St, onPaneContextMenu: cn, paneClickDistance: je, nodeClickDistance: Et, onSelectionContextMenu: D, onSelectionStart: $, onSelectionEnd: T, onReconnect: pr, onReconnectStart: to, onReconnectEnd: no, onEdgeContextMenu: ln, onEdgeDoubleClick: gr, onEdgeMouseEnter: un, onEdgeMouseMove: Vt, onEdgeMouseLeave: Le, reconnectRadius: dn, defaultMarkerColor: Lt, noDragClassName: Ht, noWheelClassName: yr, noPanClassName: oo, rfId: Ot, disableKeyboardA11y: lo, nodeExtent: vt, viewport: fo, onViewportChange: ho }), a.jsx(Hg, { onSelectionChange: O }), ut, a.jsx(Tg, { proOptions: co, position: ao }), a.jsx($g, { rfId: Ot, disableKeyboardA11y: lo })] }) });
}
var jl = rl(gm);
const ym = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function mm({ children: e }) {
  const t = de(ym);
  return t ? Cg.createPortal(e, t) : null;
}
function xm({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return a.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ie(["react-flow__background-pattern", n, o]) });
}
function wm({ radius: e, className: t }) {
  return a.jsx("circle", { cx: e, cy: e, r: e, className: Ie(["react-flow__background-pattern", "dots", t]) });
}
var gt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(gt || (gt = {}));
const vm = {
  [gt.Dots]: 1,
  [gt.Lines]: 1,
  [gt.Cross]: 6
}, bm = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
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
  style: u,
  className: l,
  patternClassName: f
}) {
  const d = ce(null), { transform: h, patternId: p } = de(bm, xe), g = o || vm[t], v = t === gt.Dots, x = t === gt.Cross, m = Array.isArray(n) ? n : [n, n], k = [m[0] * h[2] || 1, m[1] * h[2] || 1], y = g * h[2], w = Array.isArray(i) ? i : [i, i], b = x ? [y, y] : k, I = [
    w[0] * h[2] || 1 + b[0] / 2,
    w[1] * h[2] || 1 + b[1] / 2
  ], E = `${p}${e || ""}`;
  return a.jsxs("svg", { className: Ie(["react-flow__background", l]), style: {
    ...u,
    ...ur,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": s
  }, ref: d, "data-testid": "rf__background", children: [a.jsx("pattern", { id: E, x: h[0] % k[0], y: h[1] % k[1], width: k[0], height: k[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${I[0]},-${I[1]})`, children: v ? a.jsx(wm, { radius: y / 2, className: f }) : a.jsx(xm, { dimensions: b, lineWidth: r, variant: t, className: f }) }), a.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${E})` })] });
}
_l.displayName = "Background";
const Al = Ee(_l);
function Nm() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: a.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function Sm() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: a.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function Em() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: a.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function km() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: a.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function Im() {
  return a.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: a.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function Co({ children: e, className: t, ...n }) {
  return a.jsx("button", { type: "button", className: Ie(["react-flow__controls-button", t]), ...n, children: e });
}
const Cm = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function Ml({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: i, onZoomOut: s, onFitView: c, onInteractiveChange: u, className: l, children: f, position: d = "bottom-left", orientation: h = "vertical", "aria-label": p }) {
  const g = we(), { isInteractive: v, minZoomReached: x, maxZoomReached: m, ariaLabelConfig: k } = de(Cm, xe), { zoomIn: y, zoomOut: w, fitView: b } = Hi(), I = () => {
    y(), i?.();
  }, E = () => {
    w(), s?.();
  }, j = () => {
    b(r), c?.();
  }, P = () => {
    g.setState({
      nodesDraggable: !v,
      nodesConnectable: !v,
      elementsSelectable: !v
    }), u?.(!v);
  }, W = h === "horizontal" ? "horizontal" : "vertical";
  return a.jsxs(lr, { className: Ie(["react-flow__controls", W, l]), position: d, style: e, "data-testid": "rf__controls", "aria-label": p ?? k["controls.ariaLabel"], children: [t && a.jsxs(a.Fragment, { children: [a.jsx(Co, { onClick: I, className: "react-flow__controls-zoomin", title: k["controls.zoomIn.ariaLabel"], "aria-label": k["controls.zoomIn.ariaLabel"], disabled: m, children: a.jsx(Nm, {}) }), a.jsx(Co, { onClick: E, className: "react-flow__controls-zoomout", title: k["controls.zoomOut.ariaLabel"], "aria-label": k["controls.zoomOut.ariaLabel"], disabled: x, children: a.jsx(Sm, {}) })] }), n && a.jsx(Co, { className: "react-flow__controls-fitview", onClick: j, title: k["controls.fitView.ariaLabel"], "aria-label": k["controls.fitView.ariaLabel"], children: a.jsx(Em, {}) }), o && a.jsx(Co, { className: "react-flow__controls-interactive", onClick: P, title: k["controls.interactive.ariaLabel"], "aria-label": k["controls.interactive.ariaLabel"], children: v ? a.jsx(Im, {}) : a.jsx(km, {}) }), f] });
}
Ml.displayName = "Controls";
const Dl = Ee(Ml);
function jm({ id: e, x: t, y: n, width: o, height: r, style: i, color: s, strokeColor: c, strokeWidth: u, className: l, borderRadius: f, shapeRendering: d, selected: h, onClick: p }) {
  const { background: g, backgroundColor: v } = i || {}, x = s || g || v;
  return a.jsx("rect", { className: Ie(["react-flow__minimap-node", { selected: h }, l]), x: t, y: n, rx: f, ry: f, width: o, height: r, style: {
    fill: x,
    stroke: c,
    strokeWidth: u
  }, shapeRendering: d, onClick: p ? (m) => p(m, e) : void 0 });
}
const _m = Ee(jm), Am = (e) => e.nodes.map((t) => t.id), Zr = (e) => e instanceof Function ? e : () => e;
function Mm({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: i = _m,
  onClick: s
}) {
  const c = de(Am, xe), u = Zr(t), l = Zr(e), f = Zr(n), d = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return a.jsx(a.Fragment, { children: c.map((h) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    a.jsx(Pm, { id: h, nodeColorFunc: u, nodeStrokeColorFunc: l, nodeClassNameFunc: f, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: i, onClick: s, shapeRendering: d }, h)
  )) });
}
function Dm({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: i, shapeRendering: s, NodeComponent: c, onClick: u }) {
  const { node: l, x: f, y: d, width: h, height: p } = de((g) => {
    const v = g.nodeLookup.get(e);
    if (!v)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const x = v.internals.userNode, { x: m, y: k } = v.internals.positionAbsolute, { width: y, height: w } = ct(x);
    return {
      node: x,
      x: m,
      y: k,
      width: y,
      height: w
    };
  }, xe);
  return !l || l.hidden || !Cc(l) ? null : a.jsx(c, { x: f, y: d, width: h, height: p, style: l.style, selected: !!l.selected, className: o(l), color: t(l), borderRadius: r, strokeColor: n(l), strokeWidth: i, shapeRendering: s, onClick: u, id: l.id });
}
const Pm = Ee(Dm);
var $m = Ee(Mm);
const Tm = 200, Rm = 150, zm = (e) => !e.hidden, Lm = (e) => {
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
}, Vm = "react-flow__minimap-desc";
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
  bgColor: u,
  maskColor: l,
  maskStrokeColor: f,
  maskStrokeWidth: d,
  position: h = "bottom-right",
  onClick: p,
  onNodeClick: g,
  pannable: v = !1,
  zoomable: x = !1,
  ariaLabel: m,
  inversePan: k,
  zoomStep: y = 1,
  offsetScale: w = 5
}) {
  const b = we(), I = ce(null), { boundingRect: E, viewBB: j, rfId: P, panZoom: W, translateExtent: M, flowWidth: R, flowHeight: O, ariaLabelConfig: N } = de(Lm, xe), _ = e?.width ?? Tm, C = e?.height ?? Rm, D = E.width / _, $ = E.height / C, T = Math.max(D, $), H = T * _, B = T * C, F = w * T, Z = E.x - (H - E.width) / 2 - F, q = E.y - (B - E.height) / 2 - F, te = H + F * 2, ae = B + F * 2, Q = `${Vm}-${P}`, z = ce(0), K = ce();
  z.current = T, oe(() => {
    if (I.current && W)
      return K.current = Qp({
        domNode: I.current,
        panZoom: W,
        getTransform: () => b.getState().transform,
        getViewScale: () => z.current
      }), () => {
        K.current?.destroy();
      };
  }, [W]), oe(() => {
    K.current?.update({
      translateExtent: M,
      width: R,
      height: O,
      inversePan: k,
      pannable: v,
      zoomStep: y,
      zoomable: x
    });
  }, [v, x, k, y, M, R, O]);
  const se = p ? (ee) => {
    const [le, L] = K.current?.pointer(ee) || [0, 0];
    p(ee, { x: le, y: L });
  } : void 0, re = g ? pe((ee, le) => {
    const L = b.getState().nodeLookup.get(le).internals.userNode;
    g(ee, L);
  }, []) : void 0, U = m ?? N["minimap.ariaLabel"];
  return a.jsx(lr, { position: h, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof f == "string" ? f : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof d == "number" ? d * T : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof s == "number" ? s : void 0
  }, className: Ie(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: a.jsxs("svg", { width: _, height: C, viewBox: `${Z} ${q} ${te} ${ae}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": Q, ref: I, onClick: se, children: [U && a.jsx("title", { id: Q, children: U }), a.jsx($m, { onClick: re, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: i, nodeClassName: r, nodeStrokeWidth: s, nodeComponent: c }), a.jsx("path", { className: "react-flow__minimap-mask", d: `M${Z - F},${q - F}h${te + F * 2}v${ae + F * 2}h${-te - F * 2}z
        M${j.x},${j.y}h${j.width}v${j.height}h${-j.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
Pl.displayName = "MiniMap";
const $l = Ee(Pl), Hm = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, Om = {
  [tn.Line]: "right",
  [tn.Handle]: "bottom-right"
};
function Fm({ nodeId: e, position: t, variant: n = tn.Handle, className: o, style: r = void 0, children: i, color: s, minWidth: c = 10, minHeight: u = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: f = Number.MAX_VALUE, keepAspectRatio: d = !1, resizeDirection: h, autoScale: p = !0, shouldResize: g, onResizeStart: v, onResize: x, onResizeEnd: m }) {
  const k = cl(), y = typeof e == "string" ? e : k, w = we(), b = ce(null), I = n === tn.Handle, E = de(pe(Hm(I && p), [I, p]), xe), j = ce(null), P = t ?? Om[n];
  oe(() => {
    if (!(!b.current || !y))
      return j.current || (j.current = fg({
        domNode: b.current,
        nodeId: y,
        getStoreItems: () => {
          const { nodeLookup: M, transform: R, snapGrid: O, snapToGrid: N, nodeOrigin: _, domNode: C } = w.getState();
          return {
            nodeLookup: M,
            transform: R,
            snapGrid: O,
            snapToGrid: N,
            nodeOrigin: _,
            paneDomNode: C
          };
        },
        onChange: (M, R) => {
          const { triggerNodeChanges: O, nodeLookup: N, parentLookup: _, nodeOrigin: C } = w.getState(), D = [], $ = { x: M.x, y: M.y }, T = N.get(y);
          if (T && T.expandParent && T.parentId) {
            const H = T.origin ?? C, B = M.width ?? T.measured.width ?? 0, F = M.height ?? T.measured.height ?? 0, Z = {
              id: T.id,
              parentId: T.parentId,
              rect: {
                width: B,
                height: F,
                ...jc({
                  x: M.x ?? T.position.x,
                  y: M.y ?? T.position.y
                }, { width: B, height: F }, T.parentId, N, H)
              }
            }, q = Vi([Z], N, _, C);
            D.push(...q), $.x = M.x ? Math.max(H[0] * B, M.x) : void 0, $.y = M.y ? Math.max(H[1] * F, M.y) : void 0;
          }
          if ($.x !== void 0 && $.y !== void 0) {
            const H = {
              id: y,
              type: "position",
              position: { ...$ }
            };
            D.push(H);
          }
          if (M.width !== void 0 && M.height !== void 0) {
            const B = {
              id: y,
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
          for (const H of R) {
            const B = {
              ...H,
              type: "position"
            };
            D.push(B);
          }
          O(D);
        },
        onEnd: ({ width: M, height: R }) => {
          const O = {
            id: y,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: M,
              height: R
            }
          };
          w.getState().triggerNodeChanges([O]);
        }
      })), j.current.update({
        controlPosition: P,
        boundaries: {
          minWidth: c,
          minHeight: u,
          maxWidth: l,
          maxHeight: f
        },
        keepAspectRatio: d,
        resizeDirection: h,
        onResizeStart: v,
        onResize: x,
        onResizeEnd: m,
        shouldResize: g
      }), () => {
        j.current?.destroy();
      };
  }, [
    P,
    c,
    u,
    l,
    f,
    d,
    v,
    x,
    m,
    g
  ]);
  const W = P.split("-");
  return a.jsx("div", { className: Ie(["react-flow__resize-control", "nodrag", ...W, n, o]), ref: b, style: {
    ...r,
    scale: E,
    ...s && { [I ? "backgroundColor" : "borderColor"]: s }
  }, children: i });
}
Ee(Fm);
const Bm = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), Tl = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var Wm = {
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
const Xm = Qo(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: o,
    className: r = "",
    children: i,
    iconNode: s,
    ...c
  }, u) => Qr(
    "svg",
    {
      ref: u,
      ...Wm,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: Tl("lucide", r),
      ...c
    },
    [
      ...s.map(([l, f]) => Qr(l, f)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const ve = (e, t) => {
  const n = Qo(
    ({ className: o, ...r }, i) => Qr(Xm, {
      ref: i,
      iconNode: t,
      className: Tl(`lucide-${Bm(e)}`, o),
      ...r
    })
  );
  return n.displayName = `${e}`, n;
};
const Rl = ve("Boxes", [
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
const Zn = ve("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const Ym = ve("ChevronDown", [
  ["path", { d: "m6 9 6 6 6-6", key: "qrunsl" }]
]);
const Uo = ve("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const Bt = ve("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const Je = ve("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const qm = ve("Copy", [
  ["rect", { width: "14", height: "14", x: "8", y: "8", rx: "2", ry: "2", key: "17jyea" }],
  ["path", { d: "M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2", key: "zix9uf" }]
]);
const zl = ve("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const Km = ve("GripVertical", [
  ["circle", { cx: "9", cy: "12", r: "1", key: "1vctgf" }],
  ["circle", { cx: "9", cy: "5", r: "1", key: "hp0tcf" }],
  ["circle", { cx: "9", cy: "19", r: "1", key: "fkjjf6" }],
  ["circle", { cx: "15", cy: "12", r: "1", key: "1tmaij" }],
  ["circle", { cx: "15", cy: "5", r: "1", key: "19l28e" }],
  ["circle", { cx: "15", cy: "19", r: "1", key: "f4zoj3" }]
]);
const Ll = ve("ListTree", [
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
const pa = ve("Minimize2", [
  ["polyline", { points: "4 14 10 14 10 20", key: "11kfnr" }],
  ["polyline", { points: "20 10 14 10 14 4", key: "rlmsce" }],
  ["line", { x1: "14", x2: "21", y1: "10", y2: "3", key: "o5lafz" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const Um = ve("Package", [
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
const On = ve("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const Fi = ve("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const Bi = ve("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const Zm = ve("Save", [
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
const Vl = ve("Search", [
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
const Gm = ve("Terminal", [
  ["polyline", { points: "4 17 10 11 4 5", key: "akl6gq" }],
  ["line", { x1: "12", x2: "20", y1: "19", y2: "19", key: "q2wloq" }]
]);
const hi = ve("Trash2", [
  ["path", { d: "M3 6h18", key: "d0wm0j" }],
  ["path", { d: "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", key: "4alrt4" }],
  ["path", { d: "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", key: "v07s0e" }],
  ["line", { x1: "10", x2: "10", y1: "11", y2: "17", key: "1uufr5" }],
  ["line", { x1: "14", x2: "14", y1: "11", y2: "17", key: "xtxkd" }]
]);
const Hl = ve("X", [
  ["path", { d: "M18 6 6 18", key: "1bl5f8" }],
  ["path", { d: "m6 6 12 12", key: "d8bk6v" }]
]);
const Jm = ve("Zap", [
  [
    "path",
    {
      d: "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z",
      key: "1xq2db"
    }
  ]
]), Qm = /* @__PURE__ */ new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);
function Ol(e) {
  return Fl(e, ex);
}
function Wi(e) {
  return Fl(e, tx);
}
function Fl(e, t) {
  return !e || !e.rootActivity ? e : { ...e, rootActivity: Bl(e.rootActivity, t) };
}
function Bl(e, t) {
  const n = t(e), o = n.structure;
  if (!o || !on(o.payload)) return n;
  let r = !1;
  const i = { ...o.payload };
  for (const [s, c] of Object.entries(o.payload))
    Array.isArray(c) && c.length > 0 && c.every(sx) && (i[s] = c.map((u) => Bl(u, t)), r = !0);
  return r ? { ...n, structure: { ...o, payload: i } } : n;
}
function ex(e) {
  const t = [], n = {};
  for (const [r, i] of Object.entries(e))
    Qm.has(r) || (ix(i) ? t.push({
      referenceKey: nx(r),
      value: { value: rx(i.expression.value), expressionType: i.expression.type || "Literal" }
    }) : n[r] = i);
  const o = Array.isArray(e.inputs) ? e.inputs : [];
  return {
    ...n,
    nodeId: e.nodeId,
    activityVersionId: e.activityVersionId,
    inputs: [...o, ...t],
    outputs: Array.isArray(e.outputs) ? e.outputs : [],
    structure: e.structure
  };
}
function tx(e) {
  const t = Array.isArray(e.inputs) ? e.inputs : [], n = {};
  for (const o of t) {
    if (!on(o) || typeof o.referenceKey != "string") continue;
    const r = on(o.value) ? o.value : {};
    n[ox(o.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof r.expressionType == "string" ? r.expressionType : "Literal",
        value: r.value ?? ""
      }
    };
  }
  return { ...e, ...n, inputs: [] };
}
function nx(e) {
  return e && e.charAt(0).toUpperCase() + e.slice(1);
}
function ox(e) {
  return e && e.charAt(0).toLowerCase() + e.slice(1);
}
function rx(e) {
  return e == null ? null : typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function ix(e) {
  if (!on(e) || Array.isArray(e)) return !1;
  const t = e.expression;
  return on(t) && typeof t.type == "string";
}
function sx(e) {
  return on(e) && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function on(e) {
  return typeof e == "object" && e !== null;
}
const Te = "/_elsa/workflow-management", ax = "/publishing";
async function cx(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${Te}/definitions?${n.toString()}`);
}
async function lx(e, t) {
  const n = await e.http.getJson(`${Te}/definitions/${encodeURIComponent(t)}`);
  return n.draft ? { ...n, draft: { ...n.draft, state: Wi(n.draft.state) } } : n;
}
async function ux(e, t) {
  const n = await e.http.getJson(`${Te}/versions/${encodeURIComponent(t)}`);
  return { ...n, state: Wi(n.state) };
}
async function dx(e, t) {
  return e.http.postJson(`${Te}/definitions`, t);
}
async function fx(e, t) {
  await e.http.deleteJson(`${Te}/definitions/${encodeURIComponent(t)}`);
}
async function hx(e, t) {
  await e.http.postJson(`${Te}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function px(e, t) {
  await e.http.deleteJson(`${Te}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function gx(e, t) {
  const n = await e.http.putJson(
    `${Te}/drafts/${encodeURIComponent(t.id)}`,
    { state: Ol(t.state), layout: t.layout }
  );
  return { ...n, state: Wi(n.state) };
}
async function yx(e, t) {
  return e.http.postJson(`${Te}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function mx(e, t) {
  return e.http.postJson(`${Te}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function xx(e, t) {
  const n = { ...t, state: Ol(t.state) };
  try {
    return await e.http.postJson(`${ax}/workflows/drafts/test-runs`, n);
  } catch (o) {
    const r = Sx(o);
    if (r) return r;
    throw o;
  }
}
async function Wl(e, t) {
  return e.http.postJson(`${Te}/executables/${encodeURIComponent(t)}/run`, {});
}
async function Xl(e) {
  return e.http.getJson("/_demo/workflows/executables");
}
async function wx(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.runKind && n.set("runKind", t.runKind), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const o = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${o ? `?${o}` : ""}`);
}
async function vx(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function Xi(e) {
  return e.http.getJson(`${Te}/activities`);
}
async function bx(e) {
  const t = await Yl(e, [
    `${Te}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? ga(t) : ga(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function Nx(e) {
  const t = await Yl(e, [
    `${Te}/descriptors/expression-descriptors`,
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
function Sx(e) {
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
], dr = "elsa.sequence.structure", Gn = "elsa.flowchart.structure";
function ql(e, t) {
  if (!e) return null;
  let n = e;
  for (const o of t) {
    const r = Ze(n).find((s) => s.id === o.slotId);
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
  let o = Ze(n)[0];
  return o ? { owner: n, slot: o } : null;
}
function Ze(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = Bx(t), r = Gr(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: Wx(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, i]) => Gr(i)).map(([i, s]) => ({
    id: `${t.kind}:${i}`,
    label: Yx(i),
    property: i,
    mode: "generic",
    activities: Gr(s) ?? []
  }));
}
function Kl(e, t, n) {
  const o = new Map(t.map((s) => [s.activityVersionId, s])), r = new Map(n.map((s) => [s.nodeId, s])), i = e.slot.activities.map((s, c) => {
    const u = o.get(s.activityVersionId), l = r.get(s.nodeId) ?? Xx(e.slot.mode, c);
    return Gl(s, u, { x: l.x, y: l.y });
  });
  return {
    nodes: i,
    edges: e.slot.mode === "flowchart" ? Tx(e.owner) : $x(e.slot, i)
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
function Ex(e, t, n, o = null) {
  const r = new Map(t.map((c) => [c.activityExecutionId, c])), i = xa(t, (c) => c.authoredActivityId || c.executableNodeId), s = xa(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? r.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const u = i.get(c.id) ?? [], l = s.get(c.id) ?? [];
    if (u.length === 0 && l.length === 0) return c;
    const f = Hx(u), d = o === c.id || u.some((p) => p.activityExecutionId === o) || l.some((p) => p.incidentId === o), h = {
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
function Yi(e, t) {
  return e?.structure?.kind === Gn || _x(t) ? "flowchart" : e?.structure?.kind === dr || Ax(t) ? "sequence" : "unsupported";
}
function gi(e, t, n) {
  if (t.length === 0) {
    const c = Ze(e)[0];
    return c ? Fn(e, c, n) : e;
  }
  const [o, ...r] = t, i = Ze(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((c) => c.nodeId === o.ownerNodeId ? gi(c, r, n) : c);
  return Fn(e, i, s);
}
function Ul(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, i = Ze(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((c) => c.nodeId === o.ownerNodeId ? Ul(c, r, n) : c);
  return Fn(e, i, s);
}
function Zl(e, t, n) {
  if (e.nodeId === t) return n(e);
  const o = Ze(e);
  if (o.length === 0) return e;
  let r = !1, i = e;
  for (const s of o) {
    const c = s.activities.map((u) => {
      const l = Zl(u, t, n);
      return l !== u && (r = !0), l;
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
function kx(e, t, n, o = []) {
  const r = new Map(e.slot.activities.map((s) => [s.nodeId, s]));
  for (const s of o)
    r.set(s.nodeId, s);
  const i = t.map((s) => r.get(s.id)).filter((s) => !!s);
  return e.slot.mode === "sequence" && i.sort((s, c) => {
    const u = t.find((f) => f.id === s.nodeId), l = t.find((f) => f.id === c.nodeId);
    return (u?.position.x ?? 0) - (l?.position.x ?? 0);
  }), Fn(e.owner, e.slot, i);
}
function Ix(e, t) {
  return {
    ...e,
    structure: Px(e.structure, t)
  };
}
function Cx(e, t) {
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
    structure: Dx(e)
  };
}
function Ce(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? Mx(t) : n;
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
      childSlots: Ze(e),
      acceptsInbound: Rx(e, t),
      sourcePorts: o.suppressFlowPorts ? [] : Jl(e, t),
      suppressFlowPorts: o.suppressFlowPorts
    }
  };
}
function mi(e) {
  if (!e) return "activity";
  const t = jx(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), o = Ce(e).toLowerCase(), r = e.category?.toLowerCase() ?? "", i = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || o === "flowchart" ? "flowchart" : n.endsWith(".sequence") || o === "sequence" ? "sequence" : n.includes("writeline") || o.includes("write line") ? "terminal" : r.includes("runtime") ? "runtime" : i === "trigger" ? "trigger" : "activity";
}
function jx(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function _x(e) {
  return !!e && (Ce(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function Ax(e) {
  return !!e && (Ce(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function Mx(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function Dx(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: dr,
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
function Px(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], o = /* @__PURE__ */ new Map();
  for (const r of n) {
    if (!qi(r)) continue;
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
function $x(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function Tx(e) {
  if (e.structure?.kind !== Gn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, i = n.target;
    if (!r?.nodeId || !i?.nodeId) return null;
    const s = Array.isArray(n.vertices) ? n.vertices.filter(Ox) : [];
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
  if (Lx(e, t) && n.length > 0)
    return [...n.map((i) => ({ name: i, displayName: i })), { name: "Default", displayName: "Default" }];
  const o = [
    ...Ro(t?.designFacets),
    ...Ro(t?.ports),
    ...Ro(t?.outputs)
  ];
  if (o.length > 0) return Vx(o);
  const r = ma(e.outcomes);
  return r.length > 0 ? r.map((i) => ({ name: i, displayName: i })) : [{ name: "Done", displayName: "Done" }];
}
function Rx(e, t) {
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
function zx(e, t, n) {
  const o = Go(t.source, n, t.sourceHandle ?? "Done", void 0), r = Go(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((i) => i.id !== t.id).concat(o, r);
}
function Gr(e) {
  return Array.isArray(e) ? e.filter(Fx) : null;
}
function Lx(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function Ro(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!qi(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...Ro(n.ports));
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
function Vx(e) {
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
function Hx(e) {
  return [...e].sort((t, n) => wa(n).localeCompare(wa(t)))[0];
}
function wa(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function Ox(e) {
  return qi(e) && typeof e.x == "number" && typeof e.y == "number";
}
function qi(e) {
  return typeof e == "object" && e !== null;
}
function Fx(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Bx(e) {
  return e.kind === dr ? "sequence" : e.kind === Gn ? "flowchart" : "generic";
}
function Wx(e) {
  return e.kind === dr || e.kind === Gn, "Activities";
}
function Xx(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function Yx(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const qx = [
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
function Ki(e) {
  return Ql(e.name);
}
function Kx(e, t) {
  const n = Ki(t), o = e[n];
  return t.isWrapped === !1 ? o ?? t.defaultValue ?? "" : tu(o, t);
}
function eu(e, t) {
  return tu(e[Ki(t)], t);
}
function Ux(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function Zx(e, t) {
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
    [Ki(t)]: n
  };
}
function Gx(e, t) {
  return t.isWrapped === !1 ? Kx(e, t) : eu(e, t).expression.value;
}
function tu(e, t) {
  return Jx(e) ? {
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
function Jx(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
const nu = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]);
function Qx({
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
  const c = t.inputs.filter((f) => f.isBrowsable !== !1).sort((f, d) => (f.order ?? 0) - (d.order ?? 0) || f.name.localeCompare(d.name));
  if (c.length === 0)
    return /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "This activity does not expose editable properties." });
  const u = i0(c), l = r.length > 0 ? r : qx;
  return /* @__PURE__ */ a.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ a.jsx("span", { className: "wf-section-label", children: "Properties" }),
    u.map((f) => /* @__PURE__ */ a.jsxs("section", { className: "wf-property-group", children: [
      u.length > 1 ? /* @__PURE__ */ a.jsx("h4", { children: f.category }) : null,
      f.inputs.map((d) => /* @__PURE__ */ a.jsx(
        e0,
        {
          activity: e,
          input: d,
          editors: n,
          expressionEditors: o,
          expressionDescriptors: l,
          onChange: s
        },
        d.name
      ))
    ] }, f.category))
  ] });
}
function e0({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: o,
  expressionDescriptors: r,
  onChange: i
}) {
  const s = t.isReadOnly === !0, c = { activity: e, expressionDescriptors: r, readOnly: s }, u = o0(n, t, c), l = u?.component, f = t.isWrapped !== !1 ? eu(e, t) : null, d = f?.expression.type ?? "Literal", h = Gx(e, t), p = f ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: r,
    readOnly: s,
    surface: "inline",
    syntax: d
  } : null, g = p ? ou(o, p) : null, v = g?.surfaces.inline, x = g && p ? ru(g, p, h) : [], m = !!(f && s0(t, u?.id)), k = !!(f && a0(t, u?.id)), [y, w] = Y(!1), b = (j) => {
    const P = f ? Ux(f, j) : j;
    i(va(e, t, P));
  }, I = (j) => {
    f && i(va(e, t, Zx(f, j)));
  }, E = v && p ? /* @__PURE__ */ a.jsx(
    v,
    {
      descriptor: t,
      syntax: d,
      value: h,
      disabled: s,
      context: p,
      onChange: b
    }
  ) : n0(l, t, h, s, c, b);
  return /* @__PURE__ */ a.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ a.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ a.jsx("span", { children: iu(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ a.jsx("p", { children: t.description }) : null,
    f && !m ? /* @__PURE__ */ a.jsx(
      xi,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: d,
        descriptors: r,
        disabled: s,
        onChange: I
      }
    ) : null,
    m ? /* @__PURE__ */ a.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-expression-editor", children: [
        E,
        wi(x)
      ] }),
      /* @__PURE__ */ a.jsx(
        xi,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: d,
          descriptors: r,
          disabled: s,
          variant: "inline",
          onChange: I
        }
      ),
      k ? /* @__PURE__ */ a.jsx(
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
      E,
      wi(x)
    ] }),
    k && !m ? /* @__PURE__ */ a.jsxs(
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
    y ? /* @__PURE__ */ a.jsx(
      t0,
      {
        input: t,
        value: h,
        syntax: d,
        descriptors: r,
        activity: e,
        expressionEditors: o,
        disabled: s,
        onChange: b,
        onSyntaxChange: I,
        onClose: () => w(!1)
      }
    ) : null
  ] });
}
function t0({
  input: e,
  value: t,
  syntax: n,
  descriptors: o,
  activity: r,
  expressionEditors: i,
  disabled: s,
  onChange: c,
  onSyntaxChange: u,
  onClose: l
}) {
  const f = Oa(), d = e.displayName || e.name, h = {
    activity: r,
    descriptor: e,
    expressionDescriptors: o,
    readOnly: s,
    surface: "expanded",
    syntax: n
  }, p = ou(i, h), g = p?.surfaces.expanded, v = p ? ru(p, h, t) : [], x = g ? null : r0(i, h);
  return oe(() => {
    const m = (k) => {
      k.key === "Escape" && l();
    };
    return window.addEventListener("keydown", m), () => window.removeEventListener("keydown", m);
  }, [l]), /* @__PURE__ */ a.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ a.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": f, children: [
    /* @__PURE__ */ a.jsxs("header", { children: [
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ a.jsx("h3", { id: f, children: d })
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", "aria-label": `Close ${d} editor`, onClick: l, children: /* @__PURE__ */ a.jsx(Hl, { size: 16 }) })
    ] }),
    /* @__PURE__ */ a.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ a.jsx(
          xi,
          {
            label: `${d} expression syntax`,
            value: n,
            descriptors: o,
            disabled: s,
            onChange: u
          }
        ),
        /* @__PURE__ */ a.jsx("span", { children: iu(e.typeName) })
      ] }),
      e.description ? /* @__PURE__ */ a.jsx("p", { children: e.description }) : null,
      g ? /* @__PURE__ */ a.jsx(
        g,
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
            "aria-label": `${d} expanded value`,
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
function n0(e, t, n, o, r, i) {
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
  const [s, c] = Y(!1), u = Oa(), l = n.find((d) => d.type === t), f = [
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
function o0(e, t, n) {
  return [...e].sort((o, r) => (o.order ?? 500) - (r.order ?? 500)).find((o) => o.supports(t, n));
}
function ou(e, t) {
  return [...e].sort((n, o) => (n.order ?? 500) - (o.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function ru(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function r0(e, t) {
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
function i0(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "General";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return [...t.entries()].map(([n, o]) => ({ category: n, inputs: o }));
}
function iu(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function s0(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !nu.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function a0(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !nu.has(t) && n !== "multiline") return !1;
  const o = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(o) || n === "singleline" || n === "multiline";
}
const ba = "elsa-studio:apply-workflow-graph-operation-batch", Na = "elsa-studio:undo-workflow-graph-operation-batch", c0 = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function l0(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const o = m0(e), r = au(o.state.rootActivity), i = /* @__PURE__ */ new Map(), s = /* @__PURE__ */ new Map(), c = [];
  for (const u of t.operations) {
    const l = y0(u.kind), f = u.parameters ?? {};
    if (l === "add-activity") {
      const d = Pe(f.activityId) ?? u.temporaryReferences?.[0], h = g0(d ?? Pe(f.displayName) ?? Pe(f.activityType) ?? "weaver-activity", r), p = u0(u, h, n);
      s.set(h, p), c.push(h), d && i.set(d, h), o.state.rootActivity && d0(o.state.rootActivity, p);
      const g = At(f.position) ? vi(f.position, { x: 280, y: 160 }) : null;
      g && (o.layout = Sa(o.layout, h, g));
      continue;
    }
    if (l === "set-root") {
      const d = Jr(o, f.activityId, i, s);
      if (!d) throw new Error("Weaver batch referenced an unknown root activity.");
      o.state.rootActivity = d;
      continue;
    }
    if (l === "set-designer-position") {
      const d = Rt(f.activityId, i);
      if (!d || !Ui(o.state.rootActivity, d)) throw new Error("Weaver batch referenced an unknown activity position.");
      o.layout = Sa(o.layout, d, vi(f, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const d = Jr(o, f.activityId, i, s);
      if (!d) throw new Error("Weaver batch referenced an unknown activity property target.");
      p0(d, Pe(f.propertyName) ?? "Value", f.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const d = Jr(o, f.activityId, i, s);
      if (!d) throw new Error("Weaver batch referenced an unknown activity update target.");
      const h = At(f.patch) ? f.patch : f;
      Object.assign(d, h);
      continue;
    }
    if (l === "remove-activity") {
      const d = Rt(f.activityId, i);
      if (!d) throw new Error("Weaver batch referenced an unknown activity remove target.");
      o.state.rootActivity = su(o.state.rootActivity, d), o.layout = o.layout.filter((h) => h.nodeId !== d);
      continue;
    }
    if (l === "connect-activities") {
      f0(o, f, i);
      continue;
    }
    if (l === "disconnect-activities") {
      h0(o, f, i);
      continue;
    }
    throw new Error(`Weaver batch operation '${String(u.kind || "unknown")}' is not supported by this designer apply path.`);
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
function u0(e, t, n) {
  const o = e.parameters ?? {}, r = Pe(o.activityVersionId) ?? Pe(o.activityType) ?? "Elsa.Workflows.Activity", i = n.find((s) => s.activityVersionId === r || s.activityTypeKey === r || s.displayName === Pe(o.displayName));
  return i ? yi(i, t) : {
    nodeId: t,
    activityVersionId: i?.activityVersionId ?? r,
    inputs: [],
    outputs: [],
    ...Pe(o.displayName) ? { displayName: Pe(o.displayName) } : {},
    designer: { position: vi(o.position, { x: 280, y: 160 }) }
  };
}
function d0(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = Zi(e);
  n && !n.some((o) => o.nodeId === t.nodeId) && n.push(t);
}
function f0(e, t, n) {
  const o = e.state.rootActivity;
  if (!o?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const r = Rt(t.sourceActivityId ?? t.sourceId ?? t.from, n), i = Rt(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!r || !i) throw new Error("Weaver batch connection is missing source or target activity.");
  const s = o.structure.payload, c = Array.isArray(s.connections) ? s.connections : [], u = Pe(t.connectionId) ?? `flow-${r}-${i}`;
  s.connections = [
    ...c.filter((l) => !At(l) || l.id !== u),
    {
      id: u,
      source: { nodeId: r, port: Pe(t.outcome) ?? Pe(t.sourcePort) ?? "Done" },
      target: { nodeId: i }
    }
  ];
}
function h0(e, t, n) {
  const o = e.state.rootActivity, r = o?.structure?.payload.connections;
  if (!Array.isArray(r)) return;
  const i = Pe(t.connectionId), s = Rt(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = Rt(t.targetActivityId ?? t.targetId ?? t.to, n);
  o.structure.payload.connections = r.filter((u) => {
    if (!At(u)) return !0;
    if (i && u.id === i) return !1;
    const l = At(u.source) ? u.source.nodeId : void 0, f = At(u.target) ? u.target.nodeId : void 0;
    return l !== s || f !== c;
  });
}
function p0(e, t, n) {
  e[Ql(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: "Literal", value: n }
  };
}
function Jr(e, t, n, o) {
  const r = Rt(t, n);
  return r ? Ui(e.state.rootActivity, r) ?? o.get(r) ?? null : null;
}
function Rt(e, t) {
  const n = Pe(e);
  return n ? t.get(n) ?? n : null;
}
function Ui(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of cu(e)) {
    const o = Ui(n, t);
    if (o) return o;
  }
  return null;
}
function su(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = Zi(e);
  if (n) {
    const o = n.map((r) => su(r, t)).filter((r) => !!r);
    n.splice(0, n.length, ...o);
  }
  return e;
}
function au(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of cu(e)) au(n, t);
  return t;
}
function cu(e) {
  return Zi(e) ?? [];
}
function Zi(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function Sa(e, t, n) {
  return [
    ...e.filter((o) => o.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function vi(e, t) {
  const n = At(e) ? e : {}, o = Number(n.x), r = Number(n.y);
  return {
    x: Number.isFinite(o) ? Math.max(40, Math.round(o)) : t.x,
    y: Number.isFinite(r) ? Math.max(40, Math.round(r)) : t.y
  };
}
function g0(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let o = n, r = 2;
  for (; t.has(o); )
    o = `${n}-${r}`, r += 1;
  return t.add(o), o;
}
function y0(e) {
  return typeof e == "number" ? c0[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Pe(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function m0(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function At(e) {
  return typeof e == "object" && e !== null;
}
const lu = { workflowActivity: rw }, uu = { workflow: sw }, Ea = "application/x-elsa-activity-version-id", x0 = 6, w0 = 1200, v0 = [10, 25, 50], b0 = 10, ka = "elsa-studio-workflow-palette-width", Ia = "elsa-studio-workflow-inspector-width", Ca = "elsa-studio-workflow-palette-collapsed", ja = "elsa-studio-workflow-inspector-collapsed", du = "elsa-studio-workflow-side-panel-maximized", bn = 180, Nn = 460, N0 = 260, Sn = 260, En = 560, S0 = 320, _a = 42, jo = 16, fu = xt.createContext(null);
let bi;
function Sw(e) {
  bi = e.dialogs, e.featureAreas.add({
    id: "workflows",
    title: "Workflows",
    description: "Design, publish and run workflow definitions and inspect runs.",
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
        { title: "Runs", path: "/workflows/instances", iconColor: "#0ea5e9" }
      ]
    },
    routes: [
      {
        id: "workflows-definitions",
        path: "/workflows/definitions",
        label: "Workflow definitions",
        component: () => /* @__PURE__ */ a.jsx(E0, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list() })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ a.jsx(k0, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => /* @__PURE__ */ a.jsx(I0, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => /* @__PURE__ */ a.jsx(C0, { context: e.backend, ai: e.ai })
      }
    ]
  });
}
function E0({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: o,
  workflowDesignerPanels: r
}) {
  const [i, s] = Y(Aa);
  oe(() => {
    const u = () => s(Aa());
    return window.addEventListener("popstate", u), () => window.removeEventListener("popstate", u);
  }, []);
  const c = (u) => {
    const l = u ? `/workflows/definitions?definition=${encodeURIComponent(u)}` : "/workflows/definitions";
    window.history.pushState({}, "", l), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return i ? /* @__PURE__ */ a.jsx(ow, { context: e, definitionId: i, ai: t, propertyEditors: n, expressionEditors: o, workflowDesignerPanels: r, onBack: () => c(null) }) : /* @__PURE__ */ a.jsx(fr, { title: "Definitions", children: /* @__PURE__ */ a.jsx(_0, { context: e, ai: t, onOpen: c }) });
}
function k0({ context: e, ai: t }) {
  const [n, o] = Y(Ma);
  oe(() => {
    const i = () => o(Ma());
    return window.addEventListener("popstate", i), () => window.removeEventListener("popstate", i);
  }, []);
  const r = pe((i) => {
    const s = i?.trim() ?? "", c = new URL(window.location.href);
    s ? c.searchParams.set("definition", s) : c.searchParams.delete("definition"), o(s || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ a.jsx(fr, { title: "Executables", children: /* @__PURE__ */ a.jsx(M0, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: r }) });
}
function I0({ context: e, ai: t }) {
  return /* @__PURE__ */ a.jsx(fr, { title: "Runs", children: /* @__PURE__ */ a.jsx($0, { context: e, ai: t }) });
}
function C0({ context: e, ai: t }) {
  const n = j0();
  return /* @__PURE__ */ a.jsx(fr, { title: "Run", children: /* @__PURE__ */ a.jsx(T0, { context: e, ai: t, workflowExecutionId: n }) });
}
function fr({ title: e, children: t }) {
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
function j0() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function _0({ context: e, ai: t, onOpen: n }) {
  const [o, r] = Y(""), [i, s] = Y("active"), [c, u] = Y(1), [l, f] = Y(b0), [d, h] = Y("loading"), [p, g] = Y(""), [v, x] = Y(""), [m, k] = Y([]), [y, w] = Y(0), [b, I] = Y(() => /* @__PURE__ */ new Set()), [E, j] = Y(null), [P, W] = Y(!1), [M, R] = Y([]), [O, N] = Y("idle"), _ = ce(null), C = ye(() => m.map((L) => L.id), [m]), D = zt(t, "weaver.workflows.suggest-create-metadata"), $ = zt(t, "weaver.workflows.explain-definition"), T = C.filter((L) => b.has(L)).length, H = C.length > 0 && T === C.length, B = pe(async () => {
    h("loading"), g("");
    try {
      const L = await cx(e, { search: o, state: i, page: c, pageSize: l }), G = typeof L.totalCount == "number", fe = L.totalCount ?? L.definitions.length, me = gu(fe, l);
      if (fe > 0 && c > me) {
        u(me);
        return;
      }
      k(G ? L.definitions : W0(L.definitions, c, l)), w(fe), h("ready");
    } catch (L) {
      g(L instanceof Error ? L.message : String(L)), h("failed");
    }
  }, [e, o, i, c, l]);
  oe(() => {
    B();
  }, [B]), oe(() => {
    _.current && (_.current.indeterminate = T > 0 && !H);
  }, [H, T]);
  const F = pe(async () => {
    if (!(O === "loading" || O === "ready")) {
      N("loading");
      try {
        const L = await Xi(e);
        R(L.activities ?? []), N("ready");
      } catch (L) {
        N("failed"), g(L instanceof Error ? L.message : String(L));
      }
    }
  }, [O, e]), Z = () => {
    g(""), x(""), j({ name: "", description: "", rootKind: "flowchart" }), F();
  }, q = async () => {
    if (E?.name.trim()) {
      W(!0), g(""), x("");
      try {
        const L = await dx(e, {
          name: E.name.trim(),
          description: E.description.trim() || null,
          rootKind: E.rootKind,
          rootActivityVersionId: q0(E, M)
        });
        j(null), n(L.definition.id);
      } catch (L) {
        g(L instanceof Error ? L.message : String(L));
      } finally {
        W(!1);
      }
    }
  }, te = (L) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(L)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, ae = async () => {
    if (m.length === 1 && c > 1) {
      u(c - 1);
      return;
    }
    await B();
  }, Q = () => I(/* @__PURE__ */ new Set()), z = (L, G) => {
    I((fe) => {
      const me = new Set(fe);
      return G ? me.add(L) : me.delete(L), me;
    });
  }, K = (L) => {
    I((G) => {
      const fe = new Set(G);
      for (const me of C)
        L ? fe.add(me) : fe.delete(me);
      return fe;
    });
  }, se = (L) => {
    s(L), u(1), Q();
  }, re = (L) => {
    r(L), u(1), Q();
  }, U = async (L) => {
    if (await bi.confirm({ message: `Delete workflow definition "${L.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" })) {
      x(""), g("");
      try {
        await fx(e, L.id), z(L.id, !1), x(`Deleted ${L.name}`), await ae();
      } catch (G) {
        g(G instanceof Error ? G.message : String(G));
      }
    }
  }, ee = async (L) => {
    x(""), g("");
    try {
      await hx(e, L.id), z(L.id, !1), x(`Restored ${L.name}`), await ae();
    } catch (G) {
      g(G instanceof Error ? G.message : String(G));
    }
  }, le = async (L) => {
    if (await bi.confirm({ message: `Permanently delete workflow definition "${L.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) {
      x(""), g("");
      try {
        await px(e, L.id), z(L.id, !1), x(`Permanently deleted ${L.name}`), await ae();
      } catch (G) {
        g(G instanceof Error ? G.message : String(G));
      }
    }
  };
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ a.jsx("button", { type: "button", className: i === "active" ? "active" : "", "aria-selected": i === "active", onClick: () => se("active"), children: "Active" }),
        /* @__PURE__ */ a.jsx("button", { type: "button", className: i === "deleted" ? "active" : "", "aria-selected": i === "deleted", onClick: () => se("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ a.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ a.jsx(Vl, { size: 15 }),
        /* @__PURE__ */ a.jsx("input", { value: o, onChange: (L) => re(L.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
        B();
      }, children: "Refresh" }),
      /* @__PURE__ */ a.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ a.jsxs("button", { type: "button", title: "Create workflow", onClick: Z, children: [
        /* @__PURE__ */ a.jsx(Fi, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    d === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(Je, { size: 16 }),
      " ",
      p
    ] }) : null,
    d !== "failed" && p ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(Je, { size: 16 }),
      " ",
      p
    ] }) : null,
    v ? /* @__PURE__ */ a.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ a.jsx(Zn, { size: 14 }),
      " ",
      v
    ] }) : null,
    b.size > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ a.jsxs("span", { children: [
        b.size,
        " selected"
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: Q, children: "Clear selection" })
    ] }) : null,
    d === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading workflow definitions..." }) : null,
    d === "ready" && m.length === 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-empty", children: [
      "No ",
      i,
      " workflow definitions found."
    ] }) : null,
    d === "ready" && m.length > 0 ? /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ a.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ a.jsx(
            "input",
            {
              ref: _,
              type: "checkbox",
              checked: H,
              onChange: (L) => K(L.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ a.jsx("span", { children: "Name" }),
          /* @__PURE__ */ a.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ a.jsx("span", { children: i === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ a.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ a.jsx("span", { children: "Actions" })
        ] }),
        m.map((L) => /* @__PURE__ */ a.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${L.name}`,
            "aria-selected": b.has(L.id),
            tabIndex: 0,
            onClick: () => n(L.id),
            onKeyDown: (G) => {
              G.currentTarget === G.target && (G.key !== "Enter" && G.key !== " " || (G.preventDefault(), n(L.id)));
            },
            children: [
              /* @__PURE__ */ a.jsx("label", { className: "wf-row-select", onClick: (G) => G.stopPropagation(), children: /* @__PURE__ */ a.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: b.has(L.id),
                  onChange: (G) => z(L.id, G.target.checked),
                  "aria-label": `Select workflow definition ${L.name}`
                }
              ) }),
              /* @__PURE__ */ a.jsxs("span", { children: [
                /* @__PURE__ */ a.jsx("strong", { children: L.name }),
                /* @__PURE__ */ a.jsx("small", { children: L.description || L.id })
              ] }),
              /* @__PURE__ */ a.jsx("span", { children: L.latestVersion ?? "No version" }),
              /* @__PURE__ */ a.jsx("span", { children: i === "deleted" ? Ge(L.deletedAt) : L.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ a.jsx("span", { children: Ge(L.lastModifiedAt) }),
              /* @__PURE__ */ a.jsx("span", { className: "wf-row-actions", onClick: (G) => G.stopPropagation(), children: i === "active" ? /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
                /* @__PURE__ */ a.jsx("button", { type: "button", onClick: (G) => {
                  G.stopPropagation(), n(L.id);
                }, children: "Open" }),
                /* @__PURE__ */ a.jsx("button", { type: "button", onClick: (G) => {
                  G.stopPropagation(), te(L.id);
                }, children: "Artifacts" }),
                $ ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => mt(t, $, L), children: [
                  /* @__PURE__ */ a.jsx(yt, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ a.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  U(L);
                }, children: [
                  /* @__PURE__ */ a.jsx(hi, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
                /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
                  ee(L);
                }, children: [
                  /* @__PURE__ */ a.jsx(Bi, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ a.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  le(L);
                }, children: [
                  /* @__PURE__ */ a.jsx(hi, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          L.id
        ))
      ] }),
      /* @__PURE__ */ a.jsx(
        B0,
        {
          page: c,
          pageSize: l,
          totalCount: y,
          onPageChange: u,
          onPageSizeChange: (L) => {
            f(L), u(1);
          }
        }
      )
    ] }) : null,
    E ? /* @__PURE__ */ a.jsx(
      A0,
      {
        draft: E,
        activities: M,
        catalogState: O,
        creating: P,
        suggestMetadataAction: D,
        onSuggestMetadata: D ? () => mt(t, D, { draft: E, activities: M }) : void 0,
        onChange: (L) => j(L),
        onClose: () => j(null),
        onSubmit: q
      }
    ) : null
  ] });
}
function A0({ draft: e, activities: t, catalogState: n, creating: o, suggestMetadataAction: r, onSuggestMetadata: i, onChange: s, onClose: c, onSubmit: u }) {
  const l = ye(() => X0(t), [t]), f = Y0(e, t), d = (h) => {
    if (h.startsWith("kind:")) {
      s({ ...e, rootKind: h.slice(5), rootActivityVersionId: null });
      return;
    }
    const p = t.find((g) => g.activityVersionId === h);
    s({
      ...e,
      rootKind: yu(p) ?? e.rootKind,
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
function M0({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: o }) {
  const [r, i] = Y("loading"), [s, c] = Y(""), [u, l] = Y(""), [f, d] = Y(null), [h, p] = Y([]), g = n?.trim().toLowerCase() ?? "", v = ye(
    () => g ? h.filter((E) => U0(E, g)) : h,
    [g, h]
  ), x = ye(
    () => Array.from(new Set(h.flatMap((E) => [
      E.definitionId,
      E.definitionVersionId,
      E.sourceId
    ]).filter((E) => !!E))).sort((E, j) => E.localeCompare(j)),
    [h]
  ), m = zt(t, "weaver.workflows.explain-executable"), k = pe(async () => {
    i("loading"), c("");
    try {
      p(await Xl(e)), i("ready");
    } catch (E) {
      c(E instanceof Error ? E.message : String(E)), i("failed");
    }
  }, [e]);
  oe(() => {
    k();
  }, [k]);
  const y = async (E) => {
    l(""), d(null), c("");
    try {
      const j = await Wl(e, E.artifactId), P = Su(j);
      d({ artifactId: E.artifactId, workflowExecutionId: P }), l(`Started ${E.artifactId}`);
    } catch (j) {
      c(j instanceof Error ? j.message : String(j));
    }
  }, w = (E) => {
    m && mt(t, m, E) && (c(""), d(null), l(`Sent ${E.artifactId} to Weaver`));
  }, b = (E) => {
    c(""), d(null), l(`Copied ${E}`);
  }, I = (E) => {
    l(""), d(null), c(`Could not copy ${E}.`);
  };
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
        k();
      }, children: "Refresh" }),
      /* @__PURE__ */ a.jsxs("label", { className: "wf-search wf-executable-definition-filter", children: [
        /* @__PURE__ */ a.jsx(Vl, { size: 14 }),
        /* @__PURE__ */ a.jsx(
          "input",
          {
            "aria-label": "Filter executables by workflow definition",
            list: "wf-executable-definition-options",
            placeholder: "Filter by definition ID",
            value: n ?? "",
            onChange: (E) => o(E.currentTarget.value || null)
          }
        )
      ] }),
      /* @__PURE__ */ a.jsx("datalist", { id: "wf-executable-definition-options", children: x.map((E) => /* @__PURE__ */ a.jsx("option", { value: E }, E)) }),
      n ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => o(null), children: [
        /* @__PURE__ */ a.jsx(Hl, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    r === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(Je, { size: 16 }),
      " ",
      s
    ] }) : null,
    u ? /* @__PURE__ */ a.jsx(hu, { status: u, run: f }) : null,
    r === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading workflow executables..." }) : null,
    r === "ready" && v.length === 0 ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: n ? "No workflow executables match this definition filter." : "No workflow executables found. Publish a workflow definition to create one." }) : null,
    r === "ready" && v.length > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ a.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ a.jsx("span", { children: "Version" }),
        /* @__PURE__ */ a.jsx("span", { children: "Source" }),
        /* @__PURE__ */ a.jsx("span", { children: "Root" }),
        /* @__PURE__ */ a.jsx("span", { children: "Published" }),
        /* @__PURE__ */ a.jsx("span", { children: "Actions" })
      ] }),
      v.map((E) => /* @__PURE__ */ a.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ a.jsxs("span", { className: "wf-artifact-cell", children: [
          /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line", children: [
            /* @__PURE__ */ a.jsx("strong", { title: E.artifactId, children: E.artifactId }),
            /* @__PURE__ */ a.jsx(Kt, { value: E.artifactId, ariaLabel: `Copy artifact ID ${E.artifactId}`, copiedLabel: "artifact ID", onCopied: b, onCopyFailed: I })
          ] }),
          /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
            /* @__PURE__ */ a.jsx("small", { title: E.artifactHash, children: E.artifactHash }),
            /* @__PURE__ */ a.jsx(Kt, { value: E.artifactHash, ariaLabel: `Copy artifact hash ${E.artifactHash}`, copiedLabel: "artifact hash", onCopied: b, onCopyFailed: I })
          ] })
        ] }),
        /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line wf-version-cell", children: [
          /* @__PURE__ */ a.jsx("span", { children: E.artifactVersion }),
          /* @__PURE__ */ a.jsx(Kt, { value: E.artifactVersion, ariaLabel: `Copy artifact version ${E.artifactVersion}`, copiedLabel: "artifact version", onCopied: b, onCopyFailed: I })
        ] }),
        /* @__PURE__ */ a.jsx(D0, { executable: E, onCopied: b, onCopyFailed: I }),
        /* @__PURE__ */ a.jsx("span", { children: bu(E) }),
        /* @__PURE__ */ a.jsx("span", { children: Ge(E.publishedAt ?? E.createdAt) }),
        /* @__PURE__ */ a.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
            y(E);
          }, children: [
            /* @__PURE__ */ a.jsx(On, { size: 13 }),
            " Run"
          ] }),
          m ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => w(E), children: [
            /* @__PURE__ */ a.jsx(yt, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, E.artifactId))
    ] }) : null
  ] });
}
function D0({ executable: e, onCopied: t, onCopyFailed: n }) {
  const o = e.sourceId || e.definitionVersionId || e.definitionId, r = e.sourceVersion;
  return /* @__PURE__ */ a.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ a.jsx("span", { className: "wf-source-kind", children: Nu(e.sourceKind) }),
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
function hu({ status: e, run: t, compact: n = !1 }) {
  const o = () => {
    t?.workflowExecutionId && (window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(t.workflowExecutionId)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  };
  return /* @__PURE__ */ a.jsxs("div", { className: `wf-status-line${n ? " compact" : ""}`, children: [
    /* @__PURE__ */ a.jsx(Zn, { size: n ? 13 : 14 }),
    /* @__PURE__ */ a.jsx("span", { children: e }),
    t?.workflowExecutionId ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: o, children: [
      "Open Run ",
      t.workflowExecutionId
    ] }) : null
  ] });
}
function Kt({ value: e, ariaLabel: t, copiedLabel: n, onCopied: o, onCopyFailed: r }) {
  if (!e) return null;
  const i = async (s) => {
    s.preventDefault(), s.stopPropagation();
    try {
      await J0(e), o(n);
    } catch {
      r(n);
    }
  };
  return /* @__PURE__ */ a.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (s) => {
    i(s);
  }, children: /* @__PURE__ */ a.jsx(qm, { size: 12 }) });
}
function P0({ context: e, ai: t, definitionId: n, publishedArtifactId: o }) {
  const [r, i] = Y("loading"), [s, c] = Y(""), [u, l] = Y(""), [f, d] = Y(null), [h, p] = Y([]), g = zt(t, "weaver.workflows.explain-executable"), v = pe(async () => {
    i("loading"), c("");
    try {
      const b = await Xl(e);
      p(b.filter((I) => Z0(I, n)).sort(G0)), i("ready");
    } catch (b) {
      c(b instanceof Error ? b.message : String(b)), p([]), i("failed");
    }
  }, [e, n]);
  oe(() => {
    v();
  }, [v, o]);
  const x = async (b) => {
    l(""), d(null), c("");
    try {
      const I = await Wl(e, b.artifactId);
      d({ artifactId: b.artifactId, workflowExecutionId: Su(I) }), l(`Started ${b.artifactId}`);
    } catch (I) {
      c(I instanceof Error ? I.message : String(I));
    }
  }, m = (b) => {
    g && mt(t, g, b) && (c(""), d(null), l(`Sent ${b.artifactId} to Weaver`));
  }, k = () => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(n)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, y = (b) => {
    c(""), d(null), l(`Copied ${b}`);
  }, w = (b) => {
    l(""), d(null), c(`Could not copy ${b}.`);
  };
  return /* @__PURE__ */ a.jsxs("div", { className: "wf-artifacts-panel", children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-artifacts-toolbar", children: [
      /* @__PURE__ */ a.jsxs("span", { children: [
        h.length,
        " artifact",
        h.length === 1 ? "" : "s"
      ] }),
      /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
        v();
      }, children: [
        /* @__PURE__ */ a.jsx(Bi, { size: 13 }),
        " Refresh"
      ] }),
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: k, children: "Open list" })
    ] }),
    r === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ a.jsx(Je, { size: 14 }),
      " ",
      s
    ] }) : null,
    u ? /* @__PURE__ */ a.jsx(hu, { status: u, run: f, compact: !0 }) : null,
    r === "loading" ? /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "Loading artifacts..." }) : null,
    r === "ready" && h.length === 0 ? /* @__PURE__ */ a.jsx("p", { className: "wf-muted", children: "No published artifacts for this workflow yet." }) : null,
    r === "ready" && h.length > 0 ? /* @__PURE__ */ a.jsx("div", { className: "wf-artifact-list", role: "list", "aria-label": "Workflow artifacts", children: h.map((b) => /* @__PURE__ */ a.jsxs("article", { className: "wf-artifact-card", role: "listitem", "data-active": b.artifactId === o ? "true" : void 0, children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-artifact-card-heading", children: [
        /* @__PURE__ */ a.jsxs("div", { children: [
          /* @__PURE__ */ a.jsxs("span", { className: "wf-artifact-version", children: [
            "Version ",
            b.artifactVersion
          ] }),
          b.artifactId === o ? /* @__PURE__ */ a.jsx("span", { className: "wf-chip", children: "Latest publish" }) : null
        ] }),
        /* @__PURE__ */ a.jsx("span", { children: Ge(b.publishedAt ?? b.createdAt) })
      ] }),
      /* @__PURE__ */ a.jsxs("div", { className: "wf-artifact-card-values", children: [
        /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line", children: [
          /* @__PURE__ */ a.jsx("code", { title: b.artifactId, children: b.artifactId }),
          /* @__PURE__ */ a.jsx(Kt, { value: b.artifactId, ariaLabel: `Copy artifact ID ${b.artifactId}`, copiedLabel: "artifact ID", onCopied: y, onCopyFailed: w })
        ] }),
        /* @__PURE__ */ a.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ a.jsx("code", { title: b.artifactHash, children: b.artifactHash }),
          /* @__PURE__ */ a.jsx(Kt, { value: b.artifactHash, ariaLabel: `Copy artifact hash ${b.artifactHash}`, copiedLabel: "artifact hash", onCopied: y, onCopyFailed: w })
        ] })
      ] }),
      /* @__PURE__ */ a.jsxs("dl", { children: [
        /* @__PURE__ */ a.jsxs("div", { children: [
          /* @__PURE__ */ a.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ a.jsxs("dd", { children: [
            Nu(b.sourceKind),
            " ",
            b.sourceVersion ? `v${b.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ a.jsxs("div", { children: [
          /* @__PURE__ */ a.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ a.jsx("dd", { children: bu(b) })
        ] })
      ] }),
      /* @__PURE__ */ a.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
          x(b);
        }, children: [
          /* @__PURE__ */ a.jsx(On, { size: 13 }),
          " Run"
        ] }),
        g ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => m(b), children: [
          /* @__PURE__ */ a.jsx(yt, { size: 13 }),
          " Explain"
        ] }) : null
      ] })
    ] }, b.artifactId)) }) : null
  ] });
}
function $0({ context: e }) {
  const [t, n] = Y("loading"), [o, r] = Y(""), [i, s] = Y(""), [c, u] = Y(""), [l, f] = Y([]), d = pe(async () => {
    n("loading"), r("");
    try {
      const p = await wx(e, {
        status: i || void 0,
        runKind: c || void 0,
        take: 100
      });
      f(p), n("ready");
    } catch (p) {
      r(p instanceof Error ? p.message : String(p)), f([]), n("failed");
    }
  }, [e, c, i]);
  oe(() => {
    d();
  }, [d]);
  const h = (p) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(p)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
        d();
      }, children: "Refresh" }),
      /* @__PURE__ */ a.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ a.jsx("span", { children: "Status" }),
        /* @__PURE__ */ a.jsxs("select", { "aria-label": "Workflow run status", value: i, onChange: (p) => s(p.target.value), children: [
          /* @__PURE__ */ a.jsx("option", { value: "", children: "All statuses" }),
          /* @__PURE__ */ a.jsx("option", { value: "Pending", children: "Pending" }),
          /* @__PURE__ */ a.jsx("option", { value: "Running", children: "Running" }),
          /* @__PURE__ */ a.jsx("option", { value: "Suspended", children: "Suspended" }),
          /* @__PURE__ */ a.jsx("option", { value: "Completed", children: "Completed" }),
          /* @__PURE__ */ a.jsx("option", { value: "Faulted", children: "Faulted" }),
          /* @__PURE__ */ a.jsx("option", { value: "Cancelled", children: "Cancelled" })
        ] })
      ] }),
      /* @__PURE__ */ a.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ a.jsx("span", { children: "Kind" }),
        /* @__PURE__ */ a.jsxs("select", { "aria-label": "Run Kind", value: c, onChange: (p) => u(p.target.value), children: [
          /* @__PURE__ */ a.jsx("option", { value: "", children: "All kinds" }),
          /* @__PURE__ */ a.jsx("option", { value: "TestRun", children: "Test Run" }),
          /* @__PURE__ */ a.jsx("option", { value: "PublishedRun", children: "Published Run" }),
          /* @__PURE__ */ a.jsx("option", { value: "BackgroundWeaverRun", children: "Background Weaver Run" }),
          /* @__PURE__ */ a.jsx("option", { value: "Unknown", children: "Unknown / legacy" })
        ] })
      ] })
    ] }),
    t === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(Je, { size: 16 }),
      " ",
      o
    ] }) : null,
    t === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading workflow runs..." }) : null,
    t === "ready" && l.length === 0 ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "No workflow runs found. Run a published workflow executable to create history." }) : null,
    t === "ready" && l.length > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-grid wf-instance-grid", role: "table", "aria-label": "Workflow runs", children: [
      /* @__PURE__ */ a.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ a.jsx("span", { children: "Run" }),
        /* @__PURE__ */ a.jsx("span", { children: "Kind" }),
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
          "aria-label": `Inspect workflow run ${p.workflowExecutionId}`,
          onClick: () => h(p.workflowExecutionId),
          children: [
            /* @__PURE__ */ a.jsxs("span", { children: [
              /* @__PURE__ */ a.jsx("strong", { children: p.workflowExecutionId }),
              /* @__PURE__ */ a.jsx("small", { children: p.artifactId })
            ] }),
            /* @__PURE__ */ a.jsx("span", { children: pu(p.runKind) }),
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
            /* @__PURE__ */ a.jsx("span", { children: vw(p.startedAt ?? p.createdAt, p.completedAt ?? p.updatedAt) })
          ]
        },
        p.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function T0({ context: e, ai: t, workflowExecutionId: n }) {
  const [o, r] = Y("loading"), [i, s] = Y(""), [c, u] = Y(null), [l, f] = Y(null), d = zt(t, "weaver.workflows.explain-instance"), h = pe(async () => {
    if (!n) {
      s("No workflow execution id was provided."), r("failed");
      return;
    }
    r("loading"), s("");
    try {
      const g = await vx(e, n), [v, x] = await Promise.all([
        ux(e, g.instance.definitionVersionId).then(
          (m) => ({ definitionVersion: m, error: "" }),
          (m) => ({ definitionVersion: null, error: m instanceof Error ? m.message : String(m) })
        ),
        Xi(e)
      ]);
      u({
        details: g,
        definitionVersion: v.definitionVersion,
        definitionVersionError: v.error,
        activityCatalog: x.activities
      }), f(null), r("ready");
    } catch (g) {
      u(null), s(xw(g, n)), r("failed");
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
        /* @__PURE__ */ a.jsx(Uo, { size: 14 }),
        " Runs"
      ] }),
      /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => {
        h();
      }, children: [
        /* @__PURE__ */ a.jsx(Bi, { size: 14 }),
        " Refresh"
      ] }),
      c && d ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => mt(t, d, c.details), children: [
        /* @__PURE__ */ a.jsx(yt, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    o === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading workflow run..." }) : null,
    o === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(Je, { size: 16 }),
      " ",
      i
    ] }) : null,
    o === "ready" && c ? /* @__PURE__ */ a.jsxs("div", { className: "wf-instance-detail-workbench", children: [
      /* @__PURE__ */ a.jsx(
        R0,
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
        z0,
        {
          ai: t,
          action: d,
          summary: c.details.instance,
          details: c.details,
          state: "ready",
          error: "",
          selectedEvidenceId: l,
          onSelectEvidence: f,
          graphNodeIds: c.definitionVersion ? F0(c.definitionVersion, c.activityCatalog) : void 0
        }
      )
    ] }) : null
  ] });
}
function R0({ definitionVersion: e, definitionVersionError: t, activityCatalog: n, details: o, selectedEvidenceId: r, onSelectEvidence: i }) {
  const s = ye(() => {
    if (!e) return { nodes: [], edges: [] };
    const c = e.state.rootActivity;
    if (!c) return { nodes: [], edges: [] };
    const u = n.find((p) => p.activityVersionId === c.activityVersionId), l = Yi(c, u), f = l === "unsupported" ? null : Mn(c, []), d = l === "unsupported" ? pi(c, n, e.layout) : f ? Kl(f, n, e.layout) : pi(c, n, e.layout), h = d.nodes.map((p) => ({
      ...p,
      draggable: !1,
      connectable: !1,
      deletable: !1
    }));
    return {
      nodes: Ex(h, o.activities, o.incidents, r),
      edges: d.edges.map((p) => ({ ...p, deletable: !1 }))
    };
  }, [n, e, o, r]);
  return /* @__PURE__ */ a.jsxs("section", { className: "wf-instance-canvas-shell", "aria-label": "Workflow run canvas", children: [
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
        "The workflow run loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ a.jsx("small", { children: mw(t) }) : null
      ] }),
      e && s.nodes.length === 0 ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      s.nodes.length > 0 ? /* @__PURE__ */ a.jsxs(
        jl,
        {
          nodes: s.nodes,
          edges: s.edges,
          nodeTypes: lu,
          edgeTypes: uu,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (c, u) => i(u.id),
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
function z0({ ai: e, action: t, summary: n, details: o, state: r, error: i, selectedEvidenceId: s = null, onSelectEvidence: c, graphNodeIds: u }) {
  return n ? /* @__PURE__ */ a.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Workflow run details", children: [
    /* @__PURE__ */ a.jsxs("header", { children: [
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("span", { children: "Workflow Instance ID" }),
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
      /* @__PURE__ */ a.jsx("dt", { children: "Run Kind" }),
      /* @__PURE__ */ a.jsx("dd", { children: pu(n.runKind) }),
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
    r === "loading" ? /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Loading run details..." }) : null,
    r === "failed" ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(Je, { size: 16 }),
      " ",
      i
    ] }) : null,
    r === "ready" && o ? /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
      /* @__PURE__ */ a.jsx(L0, { activities: o.activities, selectedEvidenceId: s, onSelectEvidence: c }),
      /* @__PURE__ */ a.jsx(V0, { incidents: o.incidents, selectedEvidenceId: s, onSelectEvidence: c }),
      /* @__PURE__ */ a.jsx(H0, { details: o, graphNodeIds: u })
    ] }) : null
  ] }) : /* @__PURE__ */ a.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Select a workflow run to inspect activity history." }) });
}
function L0({ activities: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
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
          /* @__PURE__ */ a.jsx("strong", { children: Gi(o.activityType) ?? o.activityType }),
          /* @__PURE__ */ a.jsx("small", { children: o.activityExecutionId }),
          /* @__PURE__ */ a.jsx("time", { children: Ge(o.scheduledAt) })
        ]
      },
      o.activityExecutionId
    )) }) : null
  ] });
}
function V0({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
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
function H0({ details: e, graphNodeIds: t }) {
  if (!t) return null;
  const n = new Map(e.activities.map((i) => [i.activityExecutionId, i])), o = e.activities.filter((i) => !t.has(Da(i))), r = e.incidents.filter((i) => {
    const s = i.activityExecutionId ? n.get(i.activityExecutionId) : null, c = i.executableNodeId ?? (s ? Da(s) : "");
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
function sn({ status: e, subStatus: t }) {
  return /* @__PURE__ */ a.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
function pu(e) {
  switch (O0(e)) {
    case "testrun":
      return "Test Run";
    case "publishedrun":
      return "Published Run";
    case "backgroundweaverrun":
      return "Background Weaver Run";
    default:
      return "Unknown / legacy";
  }
}
function O0(e) {
  return (e ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}
function F0(e, t) {
  const n = e.state.rootActivity;
  if (!n) return /* @__PURE__ */ new Set();
  const o = t.find((i) => i.activityVersionId === n.activityVersionId);
  if (Yi(n, o) === "unsupported") return /* @__PURE__ */ new Set([n.nodeId]);
  const r = Mn(n, []);
  return new Set(r?.slot.activities.map((i) => i.nodeId) ?? [n.nodeId]);
}
function Da(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function B0({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const i = gu(n, t), s = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
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
      /* @__PURE__ */ a.jsx("select", { value: t, onChange: (u) => r(Number(u.target.value)), children: v0.map((u) => /* @__PURE__ */ a.jsx("option", { value: u, children: u }, u)) })
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
function W0(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function gu(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function zt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function mt(e, t, n) {
  const o = t.createPrompt(n);
  return o ? (e.dispatchPrompt(o), !0) : !1;
}
function X0(e) {
  const t = Jo(e, "flowchart"), n = Jo(e, "sequence"), o = [
    { value: t?.activityVersionId ?? "kind:flowchart", label: "Flowchart" },
    { value: n?.activityVersionId ?? "kind:sequence", label: "Sequence" }
  ], r = /* @__PURE__ */ new Map();
  for (const s of e.filter(vu)) {
    if (K0(s)) continue;
    const c = s.category || "Uncategorized";
    r.set(c, [...r.get(c) ?? [], s]);
  }
  const i = Array.from(r.entries()).sort(([s], [c]) => s.localeCompare(c)).map(([s, c]) => ({
    name: s,
    activities: c.sort((u, l) => Ce(u).localeCompare(Ce(l)))
  }));
  return { compositeRoots: o, otherCategories: i };
}
function Y0(e, t) {
  return e.rootActivityVersionId ?? Jo(t, e.rootKind)?.activityVersionId ?? `kind:${e.rootKind}`;
}
function q0(e, t) {
  return e.rootActivityVersionId ?? Jo(t, e.rootKind)?.activityVersionId ?? null;
}
function Jo(e, t) {
  return e.find((n) => yu(n) === t);
}
function yu(e) {
  return e ? xu(e) ? "flowchart" : wu(e) ? "sequence" : null : null;
}
function mu(e) {
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
function K0(e) {
  return xu(e) || wu(e);
}
function xu(e) {
  return Ce(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function wu(e) {
  return Ce(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function vu(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function bu(e) {
  return Q0(e.rootActivityType) || e.rootActivityType;
}
function U0(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function Z0(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function G0(e, t) {
  return Pa(t) - Pa(e);
}
function Pa(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function Nu(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
function Su(e) {
  const t = e?.workflowExecutionId ?? e?.runId ?? e?.executionId;
  return typeof t == "string" && t.trim() ? t : null;
}
async function J0(e) {
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
function Q0(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function ew(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    _o(t, n.typeName, n), _o(t, n.name, n), _o(t, n.displayName, n);
    const o = n.typeName.split(".").filter(Boolean).at(-1);
    _o(t, o, n);
  }
  return t;
}
function tw(e, t, n) {
  const o = t.get(e.activityVersionId);
  return n.get(jn(o?.activityTypeKey)) ?? n.get(jn(Gi(o?.activityTypeKey))) ?? n.get(jn(o?.displayName)) ?? n.get(jn(e.activityVersionId)) ?? null;
}
function _o(e, t, n) {
  const o = jn(t);
  o && !e.has(o) && e.set(o, n);
}
function jn(e) {
  return e?.trim().toLowerCase() ?? "";
}
function Gi(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function $a(e, t, n, o) {
  const r = hr();
  if (!r) return t;
  const i = r.getItem(e);
  if (i == null) return t;
  const s = Number(i);
  return Number.isFinite(s) ? zo(s, n, o) : t;
}
function Ta(e, t) {
  const n = hr();
  if (!n) return t;
  const o = n.getItem(e);
  return o === "true" ? !0 : o === "false" ? !1 : t;
}
function nw() {
  const e = hr();
  if (!e) return null;
  const t = e.getItem(du);
  return t === "palette" || t === "inspector" ? t : null;
}
function hr() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function kn(e, t) {
  const n = hr();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function zo(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function ow({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: o,
  expressionEditors: r,
  workflowDesignerPanels: i,
  onBack: s
}) {
  const [c, u] = Y(null), [l, f] = Y(null), [d, h] = Y([]), [p, g] = Y([]), [v, x] = Y(To), [m, k] = Y("loading"), [y, w] = Y([]), [b, I] = Y([]), [E, j] = Y([]), [P, W] = Y(null), [M, R] = Y(null), [O, N] = Y(null), [_, C] = Y(null), [D, $] = Y(""), [T, H] = Y(""), [B, F] = Y("idle"), [Z, q] = Y(null), [te, ae] = Y(!1), [Q, z] = Y(null), [K, se] = Y(() => /* @__PURE__ */ new Set()), [re, U] = Y(() => $a(ka, N0, bn, Nn)), [ee, le] = Y(() => $a(Ia, S0, Sn, En)), [L, G] = Y(() => Ta(Ca, !1)), [fe, me] = Y(() => Ta(ja, !1)), [ge, _e] = Y(nw), [Fe, wt] = Y("activities"), [rt, Be] = Y("inspector"), be = ce(null), De = ce(null), Re = ce(""), We = ce(0), Jn = ce(Promise.resolve()), vt = ce(/* @__PURE__ */ new Map()), Lt = ce(null), lt = ce(null), bt = ce(!1), Nt = l?.state.rootActivity ?? null, et = ye(() => new Map(d.map((S) => [S.activityVersionId, S])), [d]), Qn = ye(() => ew(p), [p]), ze = ye(() => ql(Nt, y), [Nt, y]), eo = Yi(ze, ze ? et.get(ze.activityVersionId) : void 0), Ne = !!ze && eo === "unsupported", Xe = ye(() => Ne ? null : Mn(Nt, y), [Nt, y, Ne]), an = ye(() => mu(d), [d]), Se = ye(() => Ne && ze?.nodeId === M ? ze : Xe?.slot.activities.find((S) => S.nodeId === M) ?? null, [Ne, Xe, ze, M]), St = ye(
    () => Se ? tw(Se, et, Qn) : null,
    [et, Qn, Se]
  ), cn = Se ? Ze(Se) : [], je = eo === "flowchart" && Xe?.slot.mode === "flowchart", Et = !Nt || !Ne, ut = B !== "idle", pr = !!l?.state.rootActivity && !ut, to = zt(n, "weaver.workflows.find-draft-risks"), no = zt(n, "weaver.workflows.propose-update");
  oe(() => {
    if (!(!c || !l))
      return window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
        workflowId: c.definition.id,
        workflowDefinitionId: c.definition.id,
        workflowVersionId: l.sourceVersionId ?? null,
        draftId: l.id,
        revision: pw(l),
        selectedNodeId: M,
        selectedActivityType: St?.typeName ?? (Se ? et.get(Se.activityVersionId)?.activityTypeKey ?? Se.activityVersionId : null),
        summary: c.definition.name,
        activities: Iu(l.state.rootActivity, et),
        diagnostics: l.validationErrors.map((S) => ({ severity: S.code ?? "warning", message: S.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === c.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [et, c, l, St, Se, M]), oe(() => {
    const S = (V) => {
      const X = V.detail;
      if (!X?.batch || !X.respond) return;
      if (!l || !c) {
        X.respond({ ok: !1, message: "No active workflow draft is open." });
        return;
      }
      const J = X.batch.workflowDefinitionId;
      if (J && J !== "active-draft" && J !== c.definition.id) {
        X.respond({ ok: !1, message: `Batch targets workflow '${J}', but '${c.definition.id}' is active.` });
        return;
      }
      try {
        const ue = gw(l), ie = l0(l, X.batch, d), he = `weaver-batch-${Date.now()}`;
        vt.current.set(he, ue), f(ie.draft), w([]), R(ie.finalActivityIds.at(-1) ?? null), z(null), q(null), H(ie.summary), $(""), X.respond({ ok: !0, result: { ...ie, undoToken: he } });
      } catch (ue) {
        const ie = ue instanceof Error ? ue.message : String(ue);
        $(ie), X.respond({ ok: !1, message: ie });
      }
    }, A = (V) => {
      const X = V.detail;
      if (!X?.undoToken || !X.respond) return;
      const J = vt.current.get(X.undoToken);
      if (!J) {
        X.respond({ ok: !1, message: "The Weaver batch undo point is no longer available." });
        return;
      }
      vt.current.delete(X.undoToken), f(J), w([]), R(null), z(null), q(null), H("Restored workflow draft before Weaver batch."), $(""), X.respond({ ok: !0, summary: "Restored workflow draft before Weaver batch." });
    };
    return window.addEventListener(ba, S), window.addEventListener(Na, A), () => {
      window.removeEventListener(ba, S), window.removeEventListener(Na, A);
    };
  }, [d, c, l]), oe(() => {
    kn(ka, String(re));
  }, [re]), oe(() => {
    kn(Ia, String(ee));
  }, [ee]), oe(() => {
    kn(Ca, String(L));
  }, [L]), oe(() => {
    kn(ja, String(fe));
  }, [fe]), oe(() => {
    kn(du, ge);
  }, [ge]), oe(() => {
    if (!ge) return;
    const S = (A) => {
      A.key === "Escape" && _e(null);
    };
    return window.addEventListener("keydown", S), () => window.removeEventListener("keydown", S);
  }, [ge]);
  const ln = pe(async () => {
    $(""), k("loading");
    const [S, A, V, X] = await Promise.all([
      lx(e, t),
      Xi(e),
      bx(e).then(
        (ue) => ({ ok: !0, descriptors: ue }),
        () => ({ ok: !1, descriptors: [] })
      ),
      Nx(e).then(
        (ue) => ({ ok: !0, descriptors: ue }),
        () => ({ ok: !1, descriptors: To })
      )
    ]), J = S.draft ?? null;
    u(S), Re.current = J ? ht(J) : "", f(J), h(A.activities ?? []), g(V.descriptors), x(X.descriptors.length > 0 ? X.descriptors : To), k(V.ok ? "ready" : "failed"), w([]), R(null);
  }, [e, t]);
  oe(() => {
    ln().catch((S) => $(S instanceof Error ? S.message : String(S)));
  }, [ln]), oe(() => {
    se((S) => {
      let A = !1;
      const V = new Set(S);
      for (const X of an)
        V.has(X.category) || (V.add(X.category), A = !0);
      return A ? V : S;
    });
  }, [an]), oe(() => {
    if (!ze) {
      I([]), j([]);
      return;
    }
    const S = Ne ? pi(ze, d, l?.layout ?? []) : Xe ? Kl(Xe, d, l?.layout ?? []) : { nodes: [], edges: [] };
    I(S.nodes), j(S.edges);
  }, [d, l?.layout, Ne, Xe, ze]);
  const gr = (S) => {
    f((A) => A && { ...A, state: { ...A.state, rootActivity: S } });
  }, un = pe((S, A) => {
    if (l?.state.rootActivity && Ne)
      return;
    const V = yi(S, Va(S));
    if (!l?.state.rootActivity) {
      gr(V), R(V.nodeId);
      return;
    }
    if (!Xe) {
      if (!Ze(V)[0]) {
        H(""), $("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      f((J) => {
        if (!J?.state.rootActivity) return J;
        const ue = J.state.rootActivity, ie = gi(V, [], [ue]), he = A ? [
          ...J.layout.filter((ke) => ke.nodeId !== ue.nodeId),
          {
            nodeId: ue.nodeId,
            x: Math.round(A.x),
            y: Math.round(A.y)
          }
        ] : J.layout;
        return {
          ...J,
          layout: he,
          state: {
            ...J.state,
            rootActivity: ie
          }
        };
      }), R(l.state.rootActivity.nodeId), $(""), H(`Wrapped root in ${Ce(S)}`);
      return;
    }
    f((X) => {
      if (!X?.state.rootActivity) return X;
      const J = Mn(X.state.rootActivity, y);
      if (!J) return X;
      const ue = gi(X.state.rootActivity, y, [...J.slot.activities, V]), ie = A ? [
        ...X.layout.filter((he) => he.nodeId !== V.nodeId),
        {
          nodeId: V.nodeId,
          x: Math.round(A.x),
          y: Math.round(A.y)
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
    }), R(V.nodeId);
  }, [l?.state.rootActivity, y, Ne, Xe]), Vt = pe((S, A) => {
    const V = yi(S, Va(S)), X = {
      id: V.nodeId,
      type: "workflowActivity",
      position: A,
      selected: !0,
      data: {
        label: Ce(S),
        activityVersionId: S.activityVersionId,
        activityTypeKey: S.activityTypeKey,
        category: S.category,
        executionType: S.executionType,
        icon: mi(S),
        childSlots: Ze(V),
        acceptsInbound: String(S.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: Jl(V, S)
      }
    };
    return { activityNode: V, node: X };
  }, []), Le = pe((S, A, V = []) => {
    Ne || f((X) => {
      if (!X) return X;
      const J = Cx(X.layout, S), ue = X.state.rootActivity;
      if (!ue) return { ...X, layout: J };
      const ie = Mn(ue, y);
      if (!ie) return { ...X, layout: J };
      const he = kx(ie, S, A, V), ke = ie.slot.mode === "flowchart" ? Ix(he, A) : he;
      return {
        ...X,
        layout: J,
        state: {
          ...X.state,
          rootActivity: Ul(ue, y, ke)
        }
      };
    });
  }, [y, Ne]), dn = pe((S, A) => {
    if (!be.current) return null;
    const V = be.current.getBoundingClientRect();
    return P ? P.screenToFlowPosition({ x: S, y: A }) : {
      x: S - V.left,
      y: A - V.top
    };
  }, [P]), fn = pe((S, A) => document.elementFromPoint(S, A)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), hn = pe((S, A, V) => {
    const X = b.find((Ae) => Ae.id === A.source), J = b.find((Ae) => Ae.id === A.target), ue = X && J ? dw(X, J) : X ? Ha(X) : V, ie = Vt(S, ue), ke = [...b.map((Ae) => Ae.selected ? { ...Ae, selected: !1 } : Ae), ie.node], kt = zx(E, A, ie.node.id);
    I(ke), j(kt), R(ie.node.id), Le(ke, kt, [ie.activityNode]);
  }, [Le, Vt, E, b]), Ht = pe((S, A, V) => {
    if (!Et || !be.current) return !1;
    const X = be.current.getBoundingClientRect();
    if (!(A >= X.left && A <= X.right && V >= X.top && V <= X.bottom)) return !1;
    const ue = dn(A, V);
    if (!ue) return !1;
    if (je) {
      const ie = fn(A, V), he = ie ? E.find((ke) => ke.id === ie) : void 0;
      if (he)
        return hn(S, he, ue), !0;
    }
    return un(S, ue), !0;
  }, [un, Et, E, fn, je, hn, dn]);
  oe(() => {
    const S = (V) => {
      const X = Lt.current;
      if (!X) return;
      Math.hypot(V.clientX - X.startX, V.clientY - X.startY) >= x0 && (X.dragging = !0);
    }, A = (V) => {
      const X = Lt.current;
      if (Lt.current = null, !X?.dragging || !be.current || lt.current) return;
      const J = be.current.getBoundingClientRect();
      V.clientX >= J.left && V.clientX <= J.right && V.clientY >= J.top && V.clientY <= J.bottom && (bt.current = !0, window.setTimeout(() => {
        bt.current = !1;
      }, 0), Ht(X.activity, V.clientX, V.clientY));
    };
    return window.addEventListener("pointermove", S), window.addEventListener("pointerup", A), window.addEventListener("pointercancel", A), () => {
      window.removeEventListener("pointermove", S), window.removeEventListener("pointerup", A), window.removeEventListener("pointercancel", A);
    };
  }, [P, Ht]);
  const yr = (S, A) => {
    lt.current = { activityVersionId: A.activityVersionId, handledDrop: !1 }, S.dataTransfer.setData(Ea, A.activityVersionId), S.dataTransfer.setData("text/plain", A.activityVersionId), S.dataTransfer.effectAllowed = "copy";
  }, oo = (S, A) => {
    const V = lt.current;
    lt.current = null, !V?.handledDrop && (S.clientX === 0 && S.clientY === 0 || Ht(A, S.clientX, S.clientY) && (bt.current = !0, window.setTimeout(() => {
      bt.current = !1;
    }, 0)));
  }, ro = (S, A) => {
    S.button === 0 && (Lt.current = {
      activity: A,
      startX: S.clientX,
      startY: S.clientY,
      dragging: !1
    });
  }, io = (S) => {
    bt.current || Et && un(S);
  }, so = (S) => {
    if (!Et) {
      S.dataTransfer.dropEffect = "none";
      return;
    }
    if (S.preventDefault(), S.dataTransfer.dropEffect = "copy", !je) return;
    const A = fn(S.clientX, S.clientY);
    C(A);
  }, ao = (S) => {
    if (!be.current) return;
    const A = S.relatedTarget;
    A && be.current.contains(A) || C(null);
  }, co = (S) => {
    S.preventDefault(), C(null);
    const A = S.dataTransfer.getData(Ea) || S.dataTransfer.getData("text/plain");
    if (!A || (S.stopPropagation(), lt.current?.activityVersionId === A && (lt.current.handledDrop = !0), !Et)) return;
    const V = et.get(A);
    V && Ht(V, S.clientX, S.clientY);
  }, mr = () => {
    if (!je) return;
    const S = be.current?.getBoundingClientRect();
    S && N({
      kind: "fromEmpty",
      clientX: S.left + S.width / 2,
      clientY: S.top + S.height / 2
    });
  }, pn = pe(async (S, A) => {
    const V = async () => {
      const J = ++We.current, ue = ht(S);
      $("");
      try {
        const ie = await gx(e, S), he = ht(ie);
        return Re.current = he, f((ke) => !ke || ke.id !== ie.id ? ke : ht(ke) === ue ? ie : { ...ke, validationErrors: ie.validationErrors }), J === We.current && H(A), ie;
      } catch (ie) {
        throw J === We.current && (H(""), $(ie instanceof Error ? ie.message : String(ie))), ie;
      }
    }, X = Jn.current.then(V, V);
    return Jn.current = X.catch(() => {
    }), X;
  }, [e]);
  oe(() => {
    if (!te || !l || ht(l) === Re.current) return;
    H("Autosaving...");
    const A = window.setTimeout(() => {
      pn(l, "Autosaved").catch(() => {
      });
    }, w0);
    return () => window.clearTimeout(A);
  }, [te, l, pn]);
  const xr = async () => {
    if (!(!l || ut)) {
      F("saving"), H("Saving...");
      try {
        await pn(l, "Saved");
      } catch {
      } finally {
        F("idle");
      }
    }
  }, lo = async () => {
    if (!(!l || ut)) {
      F("promoting"), H("Promoting...");
      try {
        const S = await yx(e, l.id), A = await mx(e, S.versionId);
        z(A.artifactId), H(`Published ${A.artifactVersion}`), await ln();
      } catch (S) {
        H(""), $(S instanceof Error ? S.message : String(S));
      } finally {
        F("idle");
      }
    }
  }, wr = async () => {
    if (!l?.state.rootActivity || ut) return;
    const S = l, A = ht(S);
    q(null), H("Preparing test run...");
    try {
      F("testRunPreparing"), H("Preparing test run...");
      const V = yw(S);
      F("testRunStarting"), H("Starting test run...");
      const X = await xx(e, {
        definitionId: S.definitionId,
        snapshotId: V,
        state: S.state
      });
      q({ draftSignature: A, view: X }), Be("runtime"), me(!1), H(Ji(X) ? "Test run rejected" : "Test run dispatched");
    } catch (V) {
      H(""), $(V instanceof Error ? V.message : String(V));
    } finally {
      F("idle");
    }
  }, vr = (S) => {
    const A = Ne ? S.filter((V) => V.type === "select") : S;
    A.length !== 0 && I((V) => el(A, V));
  }, br = (S) => {
    Ne || j((A) => tl(S, A));
  }, gn = (S) => !S.source || !S.target || S.source === S.target || !je ? !1 : !S.targetHandle, Nr = (S) => {
    if (!l?.state.rootActivity || !Xe || !je || !gn(S)) return;
    const A = Go(S.source, S.target, S.sourceHandle ?? "Done", S.targetHandle ?? void 0), V = ol(A, E);
    j(V), Le(b, V);
  }, Sr = () => {
    Le(b, E);
  }, Er = (S, A) => {
    if (!A.nodeId || A.handleType === "target") {
      De.current = null;
      return;
    }
    De.current = {
      nodeId: A.nodeId,
      handleId: A.handleId ?? null
    };
  }, kr = (S, A) => {
    const V = hw(De.current, A);
    if (De.current = null, !V || !je || A.toNode || A.toHandle || fw(S)) return;
    const X = ku(S);
    N({
      kind: "fromPort",
      sourceNodeId: V.nodeId,
      sourceHandleId: V.handleId,
      clientX: X.x,
      clientY: X.y
    });
  }, uo = (S, A) => {
    if (!je || !gn(A)) return;
    const V = Kg(S, {
      ...A,
      sourceHandle: A.sourceHandle ?? "Done",
      targetHandle: A.targetHandle ?? void 0
    }, E, { shouldReplaceId: !1 });
    j(V), Le(b, V);
  }, Ir = (S) => {
    if (Ne || S.length === 0) return;
    const A = new Set(S.map((J) => J.id)), V = b.filter((J) => !A.has(J.id)), X = E.filter((J) => !A.has(J.source) && !A.has(J.target));
    I(V), j(X), M && A.has(M) && R(null), Le(V, X);
  }, Cr = (S) => {
    if (Ne || S.length === 0) return;
    const A = new Set(S.map((X) => X.id)), V = E.filter((X) => !A.has(X.id));
    j(V), Le(b, V);
  }, fo = pe((S) => {
    if (Ne) return;
    const A = E.filter((V) => V.id !== S);
    j(A), Le(b, A);
  }, [Le, E, Ne, b]), ho = pe((S, A, V) => {
    je && N({ kind: "spliceEdge", edgeId: S, clientX: A, clientY: V });
  }, [je]), jr = (S) => {
    const A = O;
    if (!A) return;
    N(null);
    const V = dn(A.clientX, A.clientY) ?? { x: 0, y: 0 };
    if (A.kind === "fromEmpty") {
      const J = Vt(S, V), ie = [...b.map((he) => he.selected ? { ...he, selected: !1 } : he), J.node];
      I(ie), R(J.node.id), Le(ie, E, [J.activityNode]);
      return;
    }
    if (A.kind === "fromPort") {
      const J = b.find((Ae) => Ae.id === A.sourceNodeId), ue = J ? Ha(J) : V, ie = Vt(S, ue), ke = [...b.map((Ae) => Ae.selected ? { ...Ae, selected: !1 } : Ae), ie.node], kt = [...E, Go(A.sourceNodeId, ie.node.id, A.sourceHandleId ?? "Done")];
      I(ke), j(kt), R(ie.node.id), Le(ke, kt, [ie.activityNode]);
      return;
    }
    const X = E.find((J) => J.id === A.edgeId);
    X && hn(S, X, V);
  }, _r = ye(() => ({
    highlightedEdgeId: _,
    deleteEdge: fo,
    requestInsertActivity: ho
  }), [fo, _, ho]), Ar = (S, A, V) => {
    w((X) => [...X, { ownerNodeId: S.nodeId, slotId: A, label: V }]), R(null);
  }, Mr = pe((S) => {
    f((A) => {
      const V = A?.state.rootActivity;
      return !A || !V ? A : {
        ...A,
        state: {
          ...A.state,
          rootActivity: Zl(V, S.nodeId, () => S)
        }
      };
    });
  }, []), po = (S) => {
    se((A) => {
      const V = new Set(A);
      return V.has(S) ? V.delete(S) : V.add(S), V;
    });
  }, go = (S) => {
    _e((A) => A === S ? null : A), S === "palette" ? G((A) => !A) : me((A) => !A);
  }, yn = (S) => {
    S === "palette" ? G(!1) : me(!1), _e((A) => A === S ? null : S);
  }, yo = (S, A) => {
    _e(null), S === "palette" ? (G(!1), U((V) => zo(V + A, bn, Nn))) : (me(!1), le((V) => zo(V + A, Sn, En)));
  }, mo = (S, A) => {
    A.preventDefault(), _e(null), S === "palette" ? G(!1) : me(!1);
    const V = A.clientX, X = S === "palette" ? re : ee, J = S === "palette" ? bn : Sn, ue = S === "palette" ? Nn : En;
    document.body.classList.add("wf-side-panel-resizing");
    const ie = (ke) => {
      const kt = S === "palette" ? ke.clientX - V : V - ke.clientX, Ae = zo(X + kt, J, ue);
      S === "palette" ? U(Ae) : le(Ae);
    }, he = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", ie), window.removeEventListener("pointerup", he), window.removeEventListener("pointercancel", he);
    };
    window.addEventListener("pointermove", ie), window.addEventListener("pointerup", he), window.addEventListener("pointercancel", he);
  }, Ot = (S, A) => {
    A.key === "ArrowLeft" ? (A.preventDefault(), yo(S, S === "palette" ? -jo : jo)) : A.key === "ArrowRight" ? (A.preventDefault(), yo(S, S === "palette" ? jo : -jo)) : A.key === "Home" ? (A.preventDefault(), S === "palette" ? U(bn) : le(Sn)) : A.key === "End" && (A.preventDefault(), S === "palette" ? U(Nn) : le(En));
  };
  if (!c || !l)
    return /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: D || "Loading workflow editor..." });
  const Dr = [
    "wf-editor-body",
    L ? "palette-collapsed" : "",
    fe ? "inspector-collapsed" : "",
    ge === "palette" ? "palette-maximized" : "",
    ge === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), Pr = {
    "--wf-palette-width": `${L ? _a : re}px`,
    "--wf-inspector-width": `${fe ? _a : ee}px`
  }, mn = !L && ge !== "inspector", Qi = !fe && ge !== "palette", xo = Z?.draftSignature === ht(l) ? Z.view : null, es = xo && T.startsWith("Test run") ? "" : T, ju = (S) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(S)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, _u = {
    definition: c.definition,
    draft: l,
    selectedActivity: Se,
    selectedActivityDescriptor: St,
    selectedActivitySlots: cn,
    catalog: d,
    currentScopeOwner: ze,
    frames: y
  }, ts = i.map((S) => {
    const A = S.component;
    return {
      id: S.id,
      title: S.title,
      side: S.side,
      order: S.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ a.jsx(A, { context: _u })
    };
  }), $r = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ a.jsx(Rl, { size: 15 }),
      render: Au
    },
    ...ts.filter((S) => S.side === "left")
  ].sort(za), Tr = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ a.jsx(Ll, { size: 15 }),
      render: Mu
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: /* @__PURE__ */ a.jsx(On, { size: 15 }),
      render: () => /* @__PURE__ */ a.jsx(uw, { testRun: xo, onOpenRun: ju })
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ a.jsx(Um, { size: 15 }),
      render: () => /* @__PURE__ */ a.jsx(
        P0,
        {
          context: e,
          ai: n,
          definitionId: c.definition.id,
          publishedArtifactId: Q
        }
      )
    },
    ...ts.filter((S) => S.side === "right")
  ].sort(za), ns = $r.find((S) => S.id === Fe) ?? $r[0], os = Tr.find((S) => S.id === rt) ?? Tr[0];
  function Au() {
    return /* @__PURE__ */ a.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: an.map((S) => {
      const A = K.has(S.category);
      return /* @__PURE__ */ a.jsxs("div", { className: "wf-palette-category", children: [
        /* @__PURE__ */ a.jsxs(
          "button",
          {
            type: "button",
            className: "wf-palette-category-toggle",
            role: "treeitem",
            "aria-expanded": A,
            onClick: () => po(S.category),
            children: [
              A ? /* @__PURE__ */ a.jsx(Ym, { size: 14 }) : /* @__PURE__ */ a.jsx(Bt, { size: 14 }),
              /* @__PURE__ */ a.jsx("span", { children: S.category }),
              /* @__PURE__ */ a.jsx("small", { children: S.activities.length })
            ]
          }
        ),
        A ? /* @__PURE__ */ a.jsx("div", { className: "wf-palette-activities", role: "group", children: S.activities.map((V) => {
          const X = V.description?.trim(), J = X ? `wf-palette-description-${V.activityVersionId}` : void 0, ue = Ce(V), ie = mi(V);
          return /* @__PURE__ */ a.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-activity",
              role: "treeitem",
              draggable: !0,
              title: X || Ce(V),
              "aria-describedby": J,
              onClick: () => io(V),
              onDragStart: (he) => yr(he, V),
              onDragEnd: (he) => oo(he, V),
              onPointerDown: (he) => ro(he, V),
              children: [
                /* @__PURE__ */ a.jsx("span", { className: "wf-activity-icon", "data-icon": ie, "aria-hidden": "true", children: Eu(ie) }),
                /* @__PURE__ */ a.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ a.jsx("strong", { children: ue }),
                  X ? /* @__PURE__ */ a.jsx("small", { id: J, children: X }) : null
                ] }),
                /* @__PURE__ */ a.jsx(Km, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            V.activityVersionId
          );
        }) }) : null
      ] }, S.category);
    }) });
  }
  function Mu() {
    return Se ? /* @__PURE__ */ a.jsxs("div", { className: "wf-inspector-content", children: [
      /* @__PURE__ */ a.jsx("h3", { children: b.find((S) => S.id === Se.nodeId)?.data.label ?? Se.nodeId }),
      /* @__PURE__ */ a.jsxs("dl", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Node ID" }),
        /* @__PURE__ */ a.jsx("dd", { children: Se.nodeId }),
        /* @__PURE__ */ a.jsx("dt", { children: "Activity type" }),
        /* @__PURE__ */ a.jsx("dd", { children: St?.typeName ?? et.get(Se.activityVersionId)?.activityTypeKey ?? "Unknown" }),
        /* @__PURE__ */ a.jsx("dt", { children: "Activity version" }),
        /* @__PURE__ */ a.jsx("dd", { children: Se.activityVersionId })
      ] }),
      /* @__PURE__ */ a.jsx(
        Qx,
        {
          activity: Se,
          descriptor: St,
          editors: o,
          expressionEditors: r,
          expressionDescriptors: v,
          descriptorStatus: m,
          onChange: Mr
        }
      ),
      cn.length > 0 ? /* @__PURE__ */ a.jsxs("div", { className: "wf-slot-list", children: [
        /* @__PURE__ */ a.jsx("span", { children: "Embedded slots" }),
        cn.map((S) => /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => Ar(Se, S.id, `${b.find((A) => A.id === Se.nodeId)?.data.label ?? Se.nodeId} / ${S.label}`), children: [
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
      /* @__PURE__ */ a.jsx(Bt, { size: 14 }),
      /* @__PURE__ */ a.jsx("strong", { children: c.definition.name }),
      /* @__PURE__ */ a.jsx("span", { className: "wf-chip", children: "Draft" }),
      es ? /* @__PURE__ */ a.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ a.jsx(Zn, { size: 13 }),
        " ",
        es
      ] }) : null,
      /* @__PURE__ */ a.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ a.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ a.jsx("input", { className: "wf-autosave-switch-input", type: "checkbox", checked: te, onChange: (S) => ae(S.target.checked) }),
          /* @__PURE__ */ a.jsx("span", { children: "Autosave" })
        ] }),
        to ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => mt(n, to, { definition: c.definition, draft: l }), children: [
          /* @__PURE__ */ a.jsx(yt, { size: 15 }),
          " Risks"
        ] }) : null,
        no ? /* @__PURE__ */ a.jsxs("button", { type: "button", onClick: () => mt(n, no, { definition: c.definition, draft: l }), children: [
          /* @__PURE__ */ a.jsx(yt, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ a.jsxs("button", { type: "button", disabled: ut, onClick: () => {
          xr();
        }, children: [
          /* @__PURE__ */ a.jsx(Zm, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ a.jsxs("button", { type: "button", disabled: ut, onClick: () => {
          lo();
        }, children: [
          /* @__PURE__ */ a.jsx(zl, { size: 15 }),
          " Promote"
        ] }),
        xo ? /* @__PURE__ */ a.jsx(
          lw,
          {
            testRun: xo,
            onOpenDetails: () => {
              Be("runtime"), me(!1);
            }
          }
        ) : null,
        /* @__PURE__ */ a.jsxs(
          "button",
          {
            type: "button",
            disabled: !pr,
            title: l.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              wr();
            },
            children: [
              /* @__PURE__ */ a.jsx(On, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    D ? /* @__PURE__ */ a.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ a.jsx(Je, { size: 16 }),
      " ",
      D
    ] }) : null,
    /* @__PURE__ */ a.jsxs("div", { className: Dr, style: Pr, children: [
      /* @__PURE__ */ a.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ a.jsx(
            Ra,
            {
              label: "Activities panel tabs",
              tabs: $r,
              activeTabId: ns.id,
              onSelect: wt
            }
          ),
          /* @__PURE__ */ a.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ a.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": L ? "Expand activities panel" : "Collapse activities panel",
                title: L ? "Expand" : "Collapse",
                onClick: () => go("palette"),
                children: L ? /* @__PURE__ */ a.jsx(Bt, { size: 14 }) : /* @__PURE__ */ a.jsx(Uo, { size: 14 })
              }
            ),
            L ? null : /* @__PURE__ */ a.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": ge === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: ge === "palette" ? "Restore" : "Maximize",
                onClick: () => yn("palette"),
                children: ge === "palette" ? /* @__PURE__ */ a.jsx(pa, { size: 14 }) : /* @__PURE__ */ a.jsx(Zo, { size: 14 })
              }
            )
          ] })
        ] }),
        mn ? ns.render() : null
      ] }),
      mn && !ge ? /* @__PURE__ */ a.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": bn,
          "aria-valuemax": Nn,
          "aria-valuenow": re,
          tabIndex: 0,
          onPointerDown: (S) => mo("palette", S),
          onKeyDown: (S) => Ot("palette", S)
        }
      ) : /* @__PURE__ */ a.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ a.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-breadcrumb", children: [
          /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
            w([]), R(null);
          }, children: "Root" }),
          y.map((S, A) => /* @__PURE__ */ a.jsxs(xt.Fragment, { children: [
            /* @__PURE__ */ a.jsx(Bt, { size: 13 }),
            /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => {
              w(y.slice(0, A + 1)), R(null);
            }, children: S.label })
          ] }, `${S.ownerNodeId}-${S.slotId}-${A}`))
        ] }),
        /* @__PURE__ */ a.jsxs("div", { className: "wf-canvas", ref: be, onDragOver: so, onDragLeave: ao, onDrop: co, children: [
          /* @__PURE__ */ a.jsx(fu.Provider, { value: _r, children: /* @__PURE__ */ a.jsxs(
            jl,
            {
              nodes: b,
              edges: E,
              nodeTypes: lu,
              edgeTypes: uu,
              onInit: W,
              onNodesChange: vr,
              onEdgesChange: br,
              onNodesDelete: Ir,
              onEdgesDelete: Cr,
              onConnect: Nr,
              onConnectStart: je ? Er : void 0,
              onConnectEnd: je ? kr : void 0,
              onReconnect: je ? uo : void 0,
              isValidConnection: gn,
              onDragOver: so,
              onDragLeave: ao,
              onDrop: co,
              onPaneClick: () => R(null),
              onNodeClick: (S, A) => R(A.id),
              onNodeDragStop: Ne ? void 0 : Sr,
              fitView: !0,
              minZoom: 0.2,
              maxZoom: 1.8,
              nodesConnectable: je,
              nodesDraggable: !Ne,
              selectionOnDrag: !0,
              multiSelectionKeyCode: ["Shift", "Meta", "Control"],
              deleteKeyCode: Ne ? null : ["Backspace", "Delete"],
              panActivationKeyCode: null,
              defaultEdgeOptions: { type: "workflow" },
              children: [
                /* @__PURE__ */ a.jsx(Al, { gap: 18, size: 1 }),
                /* @__PURE__ */ a.jsx(Dl, {}),
                /* @__PURE__ */ a.jsx($l, { pannable: !0, zoomable: !0 })
              ]
            }
          ) }),
          je && b.length === 0 ? /* @__PURE__ */ a.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => mr(), children: [
            /* @__PURE__ */ a.jsx(Fi, { size: 15 }),
            " Add activity"
          ] }) : null,
          O ? /* @__PURE__ */ a.jsx(
            aw,
            {
              clientX: O.clientX,
              clientY: O.clientY,
              activities: d,
              onPick: jr,
              onClose: () => N(null)
            }
          ) : null
        ] }),
        /* @__PURE__ */ a.jsx(cw, { draft: l })
      ] }),
      Qi && !ge ? /* @__PURE__ */ a.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Sn,
          "aria-valuemax": En,
          "aria-valuenow": ee,
          tabIndex: 0,
          onPointerDown: (S) => mo("inspector", S),
          onKeyDown: (S) => Ot("inspector", S)
        }
      ) : /* @__PURE__ */ a.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ a.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ a.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ a.jsx(
            Ra,
            {
              label: "Inspector panel tabs",
              tabs: Tr,
              activeTabId: os.id,
              onSelect: Be
            }
          ),
          /* @__PURE__ */ a.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ a.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": fe ? "Expand inspector panel" : "Collapse inspector panel",
                title: fe ? "Expand" : "Collapse",
                onClick: () => go("inspector"),
                children: fe ? /* @__PURE__ */ a.jsx(Uo, { size: 14 }) : /* @__PURE__ */ a.jsx(Bt, { size: 14 })
              }
            ),
            fe ? null : /* @__PURE__ */ a.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": ge === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: ge === "inspector" ? "Restore" : "Maximize",
                onClick: () => yn("inspector"),
                children: ge === "inspector" ? /* @__PURE__ */ a.jsx(pa, { size: 14 }) : /* @__PURE__ */ a.jsx(Zo, { size: 14 })
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
function za(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
function rw({ data: e, selected: t }) {
  const n = e, o = n.runtime, r = !n.suppressFlowPorts, i = r ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], s = iw(n);
  return /* @__PURE__ */ a.jsxs(
    "div",
    {
      className: ["wf-node", t ? "selected" : "", o ? "wf-node-runtime" : "", o?.hasBlockingIncident ? "faulted" : ""].filter(Boolean).join(" "),
      "data-icon": n.icon ?? "activity",
      children: [
        r && n.acceptsInbound ? /* @__PURE__ */ a.jsx(nn, { type: "target", position: ne.Left }) : null,
        /* @__PURE__ */ a.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ a.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: Eu(n.icon) }),
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
        i.map((c, u) => {
          const l = `${(u + 1) / (i.length + 1) * 100}%`;
          return /* @__PURE__ */ a.jsxs(xt.Fragment, { children: [
            /* @__PURE__ */ a.jsx("span", { className: "wf-node-port-label", style: { top: l }, children: c.displayName }),
            /* @__PURE__ */ a.jsx(nn, { type: "source", position: ne.Right, id: c.name, style: { top: l } })
          ] }, c.name);
        })
      ]
    }
  );
}
function iw(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((r) => !!r).join(" · ");
}
function Eu(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ a.jsx(zl, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ a.jsx(Ll, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ a.jsx(Gm, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ a.jsx(On, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ a.jsx(Jm, { size: 15 });
    default:
      return /* @__PURE__ */ a.jsx(Rl, { size: 15 });
  }
}
function sw(e) {
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
  } = e, h = xt.useContext(fu), [p, g] = Y(!1), [v, x, m] = qo({ sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: c }), k = h?.highlightedEdgeId === t;
  return /* @__PURE__ */ a.jsxs(a.Fragment, { children: [
    /* @__PURE__ */ a.jsx(
      Un,
      {
        id: t,
        path: v,
        markerEnd: u,
        style: {
          ...l,
          strokeWidth: k ? 2.5 : l?.strokeWidth
        },
        label: f,
        labelX: x,
        labelY: m,
        labelStyle: d,
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1)
      }
    ),
    h ? /* @__PURE__ */ a.jsx(mm, { children: /* @__PURE__ */ a.jsxs(
      "div",
      {
        className: ["wf-edge-actions", p ? "visible" : "", k ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${x}px, ${m}px)` },
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1),
        children: [
          /* @__PURE__ */ a.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (y) => h.requestInsertActivity(t, y.clientX, y.clientY), children: /* @__PURE__ */ a.jsx(Fi, { size: 12 }) }),
          /* @__PURE__ */ a.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => h.deleteEdge(t), children: /* @__PURE__ */ a.jsx(hi, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function aw({ clientX: e, clientY: t, activities: n, onPick: o, onClose: r }) {
  const [i, s] = Y(""), [c, u] = Y(0), l = ce(null), f = ce(null), d = ye(() => {
    const k = i.trim().toLowerCase(), y = n.filter(vu);
    return k ? y.filter((w) => Ce(w).toLowerCase().includes(k) || w.activityTypeKey.toLowerCase().includes(k) || (w.category ?? "").toLowerCase().includes(k) || (w.description ?? "").toLowerCase().includes(k)) : y;
  }, [n, i]), h = ye(() => mu(d), [d]), p = ye(() => h.flatMap((k) => k.activities), [h]);
  oe(() => {
    requestAnimationFrame(() => f.current?.focus());
  }, []), oe(() => {
    const k = (w) => {
      l.current?.contains(w.target) || r();
    }, y = (w) => {
      w.key === "Escape" && r();
    };
    return document.addEventListener("mousedown", k, !0), document.addEventListener("keydown", y), () => {
      document.removeEventListener("mousedown", k, !0), document.removeEventListener("keydown", y);
    };
  }, [r]);
  const g = (k) => {
    if (k.key === "ArrowDown")
      k.preventDefault(), u((y) => Math.min(y + 1, p.length - 1));
    else if (k.key === "ArrowUp")
      k.preventDefault(), u((y) => Math.max(y - 1, 0));
    else if (k.key === "Enter") {
      k.preventDefault();
      const y = p[c];
      y && o(y);
    }
  }, v = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), x = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let m = -1;
  return /* @__PURE__ */ a.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: v, top: x }, onMouseDown: (k) => k.stopPropagation(), onClick: (k) => k.stopPropagation(), children: [
    /* @__PURE__ */ a.jsx(
      "input",
      {
        ref: f,
        type: "search",
        value: i,
        placeholder: "Search activities...",
        "aria-label": "Search activities",
        onChange: (k) => {
          s(k.target.value), u(0);
        },
        onKeyDown: g
      }
    ),
    /* @__PURE__ */ a.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: h.length === 0 ? /* @__PURE__ */ a.jsx("p", { children: "No matching activities." }) : h.map((k) => /* @__PURE__ */ a.jsxs("section", { children: [
      /* @__PURE__ */ a.jsx("h4", { children: k.category }),
      k.activities.map((y) => {
        m += 1;
        const w = m, b = w === c;
        return /* @__PURE__ */ a.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": b,
            className: b ? "active" : "",
            onMouseEnter: () => u(w),
            onClick: () => o(y),
            children: [
              /* @__PURE__ */ a.jsx("strong", { children: Ce(y) }),
              /* @__PURE__ */ a.jsx("small", { children: y.category || y.activityTypeKey })
            ]
          },
          y.activityVersionId
        );
      })
    ] }, k.category)) })
  ] });
}
function cw({ draft: e }) {
  return e.validationErrors.length ? /* @__PURE__ */ a.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ a.jsx(Je, { size: 14 }),
    e.validationErrors.length,
    " validation issue",
    e.validationErrors.length === 1 ? "" : "s"
  ] }) : /* @__PURE__ */ a.jsxs("div", { className: "wf-validation ok", children: [
    /* @__PURE__ */ a.jsx(Zn, { size: 14 }),
    " No validation errors"
  ] });
}
function lw({
  testRun: e,
  onOpenDetails: t
}) {
  const n = Ji(e);
  return /* @__PURE__ */ a.jsx("div", { className: "wf-test-run-status", "data-state": n ? "rejected" : "accepted", children: /* @__PURE__ */ a.jsxs(
    "button",
    {
      type: "button",
      className: "wf-test-run-trigger",
      onClick: t,
      children: [
        n ? /* @__PURE__ */ a.jsx(Je, { size: 16 }) : /* @__PURE__ */ a.jsx(Zn, { size: 16 }),
        n ? "Test run rejected" : "Test run dispatched"
      ]
    }
  ) });
}
function uw({ testRun: e, onOpenRun: t }) {
  if (!e)
    return /* @__PURE__ */ a.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ a.jsx("div", { className: "wf-empty", children: "Run the draft to see Runtime Evidence." }) });
  const n = Ji(e), o = e.workflowExecutionId;
  return /* @__PURE__ */ a.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ a.jsxs("section", { className: "wf-runtime-card", "data-state": n ? "rejected" : "accepted", children: [
    /* @__PURE__ */ a.jsxs("header", { children: [
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("span", { children: "Latest Test Run" }),
        /* @__PURE__ */ a.jsx("h3", { children: n ? "Rejected by the server" : "Transient run accepted" })
      ] }),
      /* @__PURE__ */ a.jsx(sn, { status: e.status, subStatus: e.commandDispatchStatus ?? void 0 })
    ] }),
    /* @__PURE__ */ a.jsx("p", { children: "Ephemeral - not saved, promoted, or published." }),
    n && e.reason ? /* @__PURE__ */ a.jsxs("div", { className: "wf-runtime-reason", children: [
      /* @__PURE__ */ a.jsx(Je, { size: 14 }),
      " ",
      e.reason
    ] }) : null,
    /* @__PURE__ */ a.jsxs("dl", { className: "wf-runtime-meta", children: [
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Dispatch" }),
        /* @__PURE__ */ a.jsx("dd", { title: e.commandDispatchStatus ?? e.status, children: e.commandDispatchStatus ?? e.status })
      ] }),
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Test Run" }),
        /* @__PURE__ */ a.jsx("dd", { title: e.testRunId, children: e.testRunId })
      ] }),
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Artifact" }),
        /* @__PURE__ */ a.jsx("dd", { title: e.artifactId ?? "None", children: e.artifactId ?? "None" })
      ] }),
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Run / Instance" }),
        /* @__PURE__ */ a.jsx("dd", { title: o ?? "None", children: o ? /* @__PURE__ */ a.jsx("button", { type: "button", onClick: () => t(o), children: o }) : "None" })
      ] }),
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Activities" }),
        /* @__PURE__ */ a.jsx("dd", { children: La(e.activityCount, "activity") })
      ] }),
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ a.jsx("dd", { children: La(e.incidentCount, "incident") })
      ] }),
      /* @__PURE__ */ a.jsxs("div", { children: [
        /* @__PURE__ */ a.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ a.jsx("dd", { title: e.expiresAt ? Ge(e.expiresAt) : "None", children: e.expiresAt ? Ge(e.expiresAt) : "None" })
      ] })
    ] })
  ] }) });
}
function La(e, t) {
  return typeof e != "number" ? "Available on linked Run" : `${e} ${t}${e === 1 ? "" : "s"}`;
}
function Va(e) {
  return `${Ce(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function Ha(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function dw(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function ku(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function fw(e) {
  const t = ku(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function hw(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function ht(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function pw(e) {
  return Cu(ht(e));
}
function Iu(e, t, n = []) {
  if (!e) return n;
  const o = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: o?.activityTypeKey ?? e.activityVersionId,
    displayName: o ? Ce(o) : void 0
  });
  for (const r of Ze(e))
    for (const i of r.activities) Iu(i, t, n);
  return n;
}
function gw(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function yw(e) {
  return `${e.id}-${Cu(JSON.stringify(e.state))}`;
}
function Cu(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function Ji(e) {
  return e.status.toLowerCase() === "rejected";
}
function Ge(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function mw(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function xw(e, t) {
  const n = e instanceof Error ? e.message : String(e);
  return ww(e, n) ? `Run ${t} was not found.` : n;
}
function ww(e, t) {
  if ((typeof e == "object" && e ? e.response?.status ?? e.status : void 0) === 404 || /\b404\b/.test(t)) return !0;
  try {
    const o = JSON.parse(t);
    return [o.error, o.title, o.detail].some((r) => typeof r == "string" && /not found/i.test(r));
  } catch {
    return /not found/i.test(t);
  }
}
function vw(e, t) {
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
  fw as isConnectEndOverExistingWorkflowNode,
  Sw as register,
  hw as resolveConnectEndSource
};
