import ct, { memo as ke, forwardRef as wi, useRef as re, useEffect as oe, useCallback as ue, useContext as co, useMemo as fe, useState as q, createContext as Br, useLayoutEffect as Pu, createElement as vr, useId as Cc, lazy as zu, Suspense as Ru } from "react";
import { useQuery as Ic, useQueryClient as Lu, useMutation as Vu } from "@tanstack/react-query";
function Hu(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var er = { exports: {} }, Pn = {};
var Ps;
function Ou() {
  if (Ps) return Pn;
  Ps = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function n(o, i, s) {
    var a = null;
    if (s !== void 0 && (a = "" + s), i.key !== void 0 && (a = "" + i.key), "key" in i) {
      s = {};
      for (var c in i)
        c !== "key" && (s[c] = i[c]);
    } else s = i;
    return i = s.ref, {
      $$typeof: e,
      type: o,
      key: a,
      ref: i !== void 0 ? i : null,
      props: s
    };
  }
  return Pn.Fragment = t, Pn.jsx = n, Pn.jsxs = n, Pn;
}
var zs;
function Fu() {
  return zs || (zs = 1, er.exports = Ou()), er.exports;
}
var r = Fu();
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
var Bu = { value: () => {
} };
function vi() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new Bo(n);
}
function Bo(e) {
  this._ = e;
}
function Wu(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", i = n.indexOf(".");
    if (i >= 0 && (o = n.slice(i + 1), n = n.slice(0, i)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
Bo.prototype = vi.prototype = {
  constructor: Bo,
  on: function(e, t) {
    var n = this._, o = Wu(e + "", n), i, s = -1, a = o.length;
    if (arguments.length < 2) {
      for (; ++s < a; ) if ((i = (e = o[s]).type) && (i = Ku(n[i], e.name))) return i;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++s < a; )
      if (i = (e = o[s]).type) n[i] = Rs(n[i], e.name, t);
      else if (t == null) for (i in n) n[i] = Rs(n[i], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new Bo(e);
  },
  call: function(e, t) {
    if ((i = arguments.length - 2) > 0) for (var n = new Array(i), o = 0, i, s; o < i; ++o) n[o] = arguments[o + 2];
    if (!this._.hasOwnProperty(e)) throw new Error("unknown type: " + e);
    for (s = this._[e], o = 0, i = s.length; o < i; ++o) s[o].value.apply(t, n);
  },
  apply: function(e, t, n) {
    if (!this._.hasOwnProperty(e)) throw new Error("unknown type: " + e);
    for (var o = this._[e], i = 0, s = o.length; i < s; ++i) o[i].value.apply(t, n);
  }
};
function Ku(e, t) {
  for (var n = 0, o = e.length, i; n < o; ++n)
    if ((i = e[n]).name === t)
      return i.value;
}
function Rs(e, t, n) {
  for (var o = 0, i = e.length; o < i; ++o)
    if (e[o].name === t) {
      e[o] = Bu, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var br = "http://www.w3.org/1999/xhtml";
const Ls = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: br,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function bi(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Ls.hasOwnProperty(t) ? { space: Ls[t], local: e } : e;
}
function qu(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === br && t.documentElement.namespaceURI === br ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Xu(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function Ac(e) {
  var t = bi(e);
  return (t.local ? Xu : qu)(t);
}
function Yu() {
}
function Wr(e) {
  return e == null ? Yu : function() {
    return this.querySelector(e);
  };
}
function Uu(e) {
  typeof e != "function" && (e = Wr(e));
  for (var t = this._groups, n = t.length, o = new Array(n), i = 0; i < n; ++i)
    for (var s = t[i], a = s.length, c = o[i] = new Array(a), d, l, u = 0; u < a; ++u)
      (d = s[u]) && (l = e.call(d, d.__data__, u, s)) && ("__data__" in d && (l.__data__ = d.__data__), c[u] = l);
  return new Ke(o, this._parents);
}
function Zu(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function Gu() {
  return [];
}
function _c(e) {
  return e == null ? Gu : function() {
    return this.querySelectorAll(e);
  };
}
function Ju(e) {
  return function() {
    return Zu(e.apply(this, arguments));
  };
}
function Qu(e) {
  typeof e == "function" ? e = Ju(e) : e = _c(e);
  for (var t = this._groups, n = t.length, o = [], i = [], s = 0; s < n; ++s)
    for (var a = t[s], c = a.length, d, l = 0; l < c; ++l)
      (d = a[l]) && (o.push(e.call(d, d.__data__, l, a)), i.push(d));
  return new Ke(o, i);
}
function Mc(e) {
  return function() {
    return this.matches(e);
  };
}
function Dc(e) {
  return function(t) {
    return t.matches(e);
  };
}
var ef = Array.prototype.find;
function tf(e) {
  return function() {
    return ef.call(this.children, e);
  };
}
function nf() {
  return this.firstElementChild;
}
function of(e) {
  return this.select(e == null ? nf : tf(typeof e == "function" ? e : Dc(e)));
}
var rf = Array.prototype.filter;
function sf() {
  return Array.from(this.children);
}
function af(e) {
  return function() {
    return rf.call(this.children, e);
  };
}
function cf(e) {
  return this.selectAll(e == null ? sf : af(typeof e == "function" ? e : Dc(e)));
}
function lf(e) {
  typeof e != "function" && (e = Mc(e));
  for (var t = this._groups, n = t.length, o = new Array(n), i = 0; i < n; ++i)
    for (var s = t[i], a = s.length, c = o[i] = [], d, l = 0; l < a; ++l)
      (d = s[l]) && e.call(d, d.__data__, l, s) && c.push(d);
  return new Ke(o, this._parents);
}
function Tc(e) {
  return new Array(e.length);
}
function df() {
  return new Ke(this._enter || this._groups.map(Tc), this._parents);
}
function Jo(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
Jo.prototype = {
  constructor: Jo,
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
function uf(e) {
  return function() {
    return e;
  };
}
function ff(e, t, n, o, i, s) {
  for (var a = 0, c, d = t.length, l = s.length; a < l; ++a)
    (c = t[a]) ? (c.__data__ = s[a], o[a] = c) : n[a] = new Jo(e, s[a]);
  for (; a < d; ++a)
    (c = t[a]) && (i[a] = c);
}
function hf(e, t, n, o, i, s, a) {
  var c, d, l = /* @__PURE__ */ new Map(), u = t.length, f = s.length, h = new Array(u), p;
  for (c = 0; c < u; ++c)
    (d = t[c]) && (h[c] = p = a.call(d, d.__data__, c, t) + "", l.has(p) ? i[c] = d : l.set(p, d));
  for (c = 0; c < f; ++c)
    p = a.call(e, s[c], c, s) + "", (d = l.get(p)) ? (o[c] = d, d.__data__ = s[c], l.delete(p)) : n[c] = new Jo(e, s[c]);
  for (c = 0; c < u; ++c)
    (d = t[c]) && l.get(h[c]) === d && (i[c] = d);
}
function pf(e) {
  return e.__data__;
}
function gf(e, t) {
  if (!arguments.length) return Array.from(this, pf);
  var n = t ? hf : ff, o = this._parents, i = this._groups;
  typeof e != "function" && (e = uf(e));
  for (var s = i.length, a = new Array(s), c = new Array(s), d = new Array(s), l = 0; l < s; ++l) {
    var u = o[l], f = i[l], h = f.length, p = yf(e.call(u, u && u.__data__, l, o)), g = p.length, v = c[l] = new Array(g), w = a[l] = new Array(g), m = d[l] = new Array(h);
    n(u, f, v, w, m, p, t);
    for (var k = 0, y = 0, b, S; k < g; ++k)
      if (b = v[k]) {
        for (k >= y && (y = k + 1); !(S = w[y]) && ++y < g; ) ;
        b._next = S || null;
      }
  }
  return a = new Ke(a, o), a._enter = c, a._exit = d, a;
}
function yf(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function mf() {
  return new Ke(this._exit || this._groups.map(Tc), this._parents);
}
function xf(e, t, n) {
  var o = this.enter(), i = this, s = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (i = t(i), i && (i = i.selection())), n == null ? s.remove() : n(s), o && i ? o.merge(i).order() : i;
}
function wf(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, i = n.length, s = o.length, a = Math.min(i, s), c = new Array(i), d = 0; d < a; ++d)
    for (var l = n[d], u = o[d], f = l.length, h = c[d] = new Array(f), p, g = 0; g < f; ++g)
      (p = l[g] || u[g]) && (h[g] = p);
  for (; d < i; ++d)
    c[d] = n[d];
  return new Ke(c, this._parents);
}
function vf() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], i = o.length - 1, s = o[i], a; --i >= 0; )
      (a = o[i]) && (s && a.compareDocumentPosition(s) ^ 4 && s.parentNode.insertBefore(a, s), s = a);
  return this;
}
function bf(e) {
  e || (e = Nf);
  function t(f, h) {
    return f && h ? e(f.__data__, h.__data__) : !f - !h;
  }
  for (var n = this._groups, o = n.length, i = new Array(o), s = 0; s < o; ++s) {
    for (var a = n[s], c = a.length, d = i[s] = new Array(c), l, u = 0; u < c; ++u)
      (l = a[u]) && (d[u] = l);
    d.sort(t);
  }
  return new Ke(i, this._parents).order();
}
function Nf(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function jf() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function Sf() {
  return Array.from(this);
}
function kf() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], i = 0, s = o.length; i < s; ++i) {
      var a = o[i];
      if (a) return a;
    }
  return null;
}
function Ef() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function Cf() {
  return !this.node();
}
function If(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var i = t[n], s = 0, a = i.length, c; s < a; ++s)
      (c = i[s]) && e.call(c, c.__data__, s, i);
  return this;
}
function Af(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function _f(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Mf(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function Df(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function Tf(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function $f(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function Pf(e, t) {
  var n = bi(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? _f : Af : typeof t == "function" ? n.local ? $f : Tf : n.local ? Df : Mf)(n, t));
}
function $c(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function zf(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Rf(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function Lf(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function Vf(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? zf : typeof t == "function" ? Lf : Rf)(e, t, n ?? "")) : an(this.node(), e);
}
function an(e, t) {
  return e.style.getPropertyValue(t) || $c(e).getComputedStyle(e, null).getPropertyValue(t);
}
function Hf(e) {
  return function() {
    delete this[e];
  };
}
function Of(e, t) {
  return function() {
    this[e] = t;
  };
}
function Ff(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function Bf(e, t) {
  return arguments.length > 1 ? this.each((t == null ? Hf : typeof t == "function" ? Ff : Of)(e, t)) : this.node()[e];
}
function Pc(e) {
  return e.trim().split(/^|\s+/);
}
function Kr(e) {
  return e.classList || new zc(e);
}
function zc(e) {
  this._node = e, this._names = Pc(e.getAttribute("class") || "");
}
zc.prototype = {
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
function Rc(e, t) {
  for (var n = Kr(e), o = -1, i = t.length; ++o < i; ) n.add(t[o]);
}
function Lc(e, t) {
  for (var n = Kr(e), o = -1, i = t.length; ++o < i; ) n.remove(t[o]);
}
function Wf(e) {
  return function() {
    Rc(this, e);
  };
}
function Kf(e) {
  return function() {
    Lc(this, e);
  };
}
function qf(e, t) {
  return function() {
    (t.apply(this, arguments) ? Rc : Lc)(this, e);
  };
}
function Xf(e, t) {
  var n = Pc(e + "");
  if (arguments.length < 2) {
    for (var o = Kr(this.node()), i = -1, s = n.length; ++i < s; ) if (!o.contains(n[i])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? qf : t ? Wf : Kf)(n, t));
}
function Yf() {
  this.textContent = "";
}
function Uf(e) {
  return function() {
    this.textContent = e;
  };
}
function Zf(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function Gf(e) {
  return arguments.length ? this.each(e == null ? Yf : (typeof e == "function" ? Zf : Uf)(e)) : this.node().textContent;
}
function Jf() {
  this.innerHTML = "";
}
function Qf(e) {
  return function() {
    this.innerHTML = e;
  };
}
function eh(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function th(e) {
  return arguments.length ? this.each(e == null ? Jf : (typeof e == "function" ? eh : Qf)(e)) : this.node().innerHTML;
}
function nh() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function oh() {
  return this.each(nh);
}
function ih() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function rh() {
  return this.each(ih);
}
function sh(e) {
  var t = typeof e == "function" ? e : Ac(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function ah() {
  return null;
}
function ch(e, t) {
  var n = typeof e == "function" ? e : Ac(e), o = t == null ? ah : typeof t == "function" ? t : Wr(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function lh() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function dh() {
  return this.each(lh);
}
function uh() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function fh() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function hh(e) {
  return this.select(e ? fh : uh);
}
function ph(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function gh(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function yh(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function mh(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, i = t.length, s; n < i; ++n)
        s = t[n], (!e.type || s.type === e.type) && s.name === e.name ? this.removeEventListener(s.type, s.listener, s.options) : t[++o] = s;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function xh(e, t, n) {
  return function() {
    var o = this.__on, i, s = gh(t);
    if (o) {
      for (var a = 0, c = o.length; a < c; ++a)
        if ((i = o[a]).type === e.type && i.name === e.name) {
          this.removeEventListener(i.type, i.listener, i.options), this.addEventListener(i.type, i.listener = s, i.options = n), i.value = t;
          return;
        }
    }
    this.addEventListener(e.type, s, n), i = { type: e.type, name: e.name, value: t, listener: s, options: n }, o ? o.push(i) : this.__on = [i];
  };
}
function wh(e, t, n) {
  var o = yh(e + ""), i, s = o.length, a;
  if (arguments.length < 2) {
    var c = this.node().__on;
    if (c) {
      for (var d = 0, l = c.length, u; d < l; ++d)
        for (i = 0, u = c[d]; i < s; ++i)
          if ((a = o[i]).type === u.type && a.name === u.name)
            return u.value;
    }
    return;
  }
  for (c = t ? xh : mh, i = 0; i < s; ++i) this.each(c(o[i], t, n));
  return this;
}
function Vc(e, t, n) {
  var o = $c(e), i = o.CustomEvent;
  typeof i == "function" ? i = new i(t, n) : (i = o.document.createEvent("Event"), n ? (i.initEvent(t, n.bubbles, n.cancelable), i.detail = n.detail) : i.initEvent(t, !1, !1)), e.dispatchEvent(i);
}
function vh(e, t) {
  return function() {
    return Vc(this, e, t);
  };
}
function bh(e, t) {
  return function() {
    return Vc(this, e, t.apply(this, arguments));
  };
}
function Nh(e, t) {
  return this.each((typeof t == "function" ? bh : vh)(e, t));
}
function* jh() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], i = 0, s = o.length, a; i < s; ++i)
      (a = o[i]) && (yield a);
}
var Hc = [null];
function Ke(e, t) {
  this._groups = e, this._parents = t;
}
function lo() {
  return new Ke([[document.documentElement]], Hc);
}
function Sh() {
  return this;
}
Ke.prototype = lo.prototype = {
  constructor: Ke,
  select: Uu,
  selectAll: Qu,
  selectChild: of,
  selectChildren: cf,
  filter: lf,
  data: gf,
  enter: df,
  exit: mf,
  join: xf,
  merge: wf,
  selection: Sh,
  order: vf,
  sort: bf,
  call: jf,
  nodes: Sf,
  node: kf,
  size: Ef,
  empty: Cf,
  each: If,
  attr: Pf,
  style: Vf,
  property: Bf,
  classed: Xf,
  text: Gf,
  html: th,
  raise: oh,
  lower: rh,
  append: sh,
  insert: ch,
  remove: dh,
  clone: hh,
  datum: ph,
  on: wh,
  dispatch: Nh,
  [Symbol.iterator]: jh
};
function We(e) {
  return typeof e == "string" ? new Ke([[document.querySelector(e)]], [document.documentElement]) : new Ke([[e]], Hc);
}
function kh(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Qe(e, t) {
  if (e = kh(e), t === void 0 && (t = e.currentTarget), t) {
    var n = t.ownerSVGElement || t;
    if (n.createSVGPoint) {
      var o = n.createSVGPoint();
      return o.x = e.clientX, o.y = e.clientY, o = o.matrixTransform(t.getScreenCTM().inverse()), [o.x, o.y];
    }
    if (t.getBoundingClientRect) {
      var i = t.getBoundingClientRect();
      return [e.clientX - i.left - t.clientLeft, e.clientY - i.top - t.clientTop];
    }
  }
  return [e.pageX, e.pageY];
}
const Eh = { passive: !1 }, Jn = { capture: !0, passive: !1 };
function tr(e) {
  e.stopImmediatePropagation();
}
function on(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Oc(e) {
  var t = e.document.documentElement, n = We(e).on("dragstart.drag", on, Jn);
  "onselectstart" in t ? n.on("selectstart.drag", on, Jn) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function Fc(e, t) {
  var n = e.document.documentElement, o = We(e).on("dragstart.drag", null);
  t && (o.on("click.drag", on, Jn), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const Do = (e) => () => e;
function Nr(e, {
  sourceEvent: t,
  subject: n,
  target: o,
  identifier: i,
  active: s,
  x: a,
  y: c,
  dx: d,
  dy: l,
  dispatch: u
}) {
  Object.defineProperties(this, {
    type: { value: e, enumerable: !0, configurable: !0 },
    sourceEvent: { value: t, enumerable: !0, configurable: !0 },
    subject: { value: n, enumerable: !0, configurable: !0 },
    target: { value: o, enumerable: !0, configurable: !0 },
    identifier: { value: i, enumerable: !0, configurable: !0 },
    active: { value: s, enumerable: !0, configurable: !0 },
    x: { value: a, enumerable: !0, configurable: !0 },
    y: { value: c, enumerable: !0, configurable: !0 },
    dx: { value: d, enumerable: !0, configurable: !0 },
    dy: { value: l, enumerable: !0, configurable: !0 },
    _: { value: u }
  });
}
Nr.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function Ch(e) {
  return !e.ctrlKey && !e.button;
}
function Ih() {
  return this.parentNode;
}
function Ah(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function _h() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Bc() {
  var e = Ch, t = Ih, n = Ah, o = _h, i = {}, s = vi("start", "drag", "end"), a = 0, c, d, l, u, f = 0;
  function h(b) {
    b.on("mousedown.drag", p).filter(o).on("touchstart.drag", w).on("touchmove.drag", m, Eh).on("touchend.drag touchcancel.drag", k).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function p(b, S) {
    if (!(u || !e.call(this, b, S))) {
      var E = y(this, t.call(this, b, S), b, S, "mouse");
      E && (We(b.view).on("mousemove.drag", g, Jn).on("mouseup.drag", v, Jn), Oc(b.view), tr(b), l = !1, c = b.clientX, d = b.clientY, E("start", b));
    }
  }
  function g(b) {
    if (on(b), !l) {
      var S = b.clientX - c, E = b.clientY - d;
      l = S * S + E * E > f;
    }
    i.mouse("drag", b);
  }
  function v(b) {
    We(b.view).on("mousemove.drag mouseup.drag", null), Fc(b.view, l), on(b), i.mouse("end", b);
  }
  function w(b, S) {
    if (e.call(this, b, S)) {
      var E = b.changedTouches, j = t.call(this, b, S), _ = E.length, D, B;
      for (D = 0; D < _; ++D)
        (B = y(this, j, b, S, E[D].identifier, E[D])) && (tr(b), B("start", b, E[D]));
    }
  }
  function m(b) {
    var S = b.changedTouches, E = S.length, j, _;
    for (j = 0; j < E; ++j)
      (_ = i[S[j].identifier]) && (on(b), _("drag", b, S[j]));
  }
  function k(b) {
    var S = b.changedTouches, E = S.length, j, _;
    for (u && clearTimeout(u), u = setTimeout(function() {
      u = null;
    }, 500), j = 0; j < E; ++j)
      (_ = i[S[j].identifier]) && (tr(b), _("end", b, S[j]));
  }
  function y(b, S, E, j, _, D) {
    var B = s.copy(), M = Qe(D || E, S), T, L, N;
    if ((N = n.call(b, new Nr("beforestart", {
      sourceEvent: E,
      target: h,
      identifier: _,
      active: a,
      x: M[0],
      y: M[1],
      dx: 0,
      dy: 0,
      dispatch: B
    }), j)) != null)
      return T = N.x - M[0] || 0, L = N.y - M[1] || 0, function A(I, $, z) {
        var P = M, F;
        switch (I) {
          case "start":
            i[_] = A, F = a++;
            break;
          case "end":
            delete i[_], --a;
          // falls through
          case "drag":
            M = Qe(z || $, S), F = a;
            break;
        }
        B.call(
          I,
          b,
          new Nr(I, {
            sourceEvent: $,
            subject: N,
            target: h,
            identifier: _,
            active: F,
            x: M[0] + T,
            y: M[1] + L,
            dx: M[0] - P[0],
            dy: M[1] - P[1],
            dispatch: B
          }),
          j
        );
      };
  }
  return h.filter = function(b) {
    return arguments.length ? (e = typeof b == "function" ? b : Do(!!b), h) : e;
  }, h.container = function(b) {
    return arguments.length ? (t = typeof b == "function" ? b : Do(b), h) : t;
  }, h.subject = function(b) {
    return arguments.length ? (n = typeof b == "function" ? b : Do(b), h) : n;
  }, h.touchable = function(b) {
    return arguments.length ? (o = typeof b == "function" ? b : Do(!!b), h) : o;
  }, h.on = function() {
    var b = s.on.apply(s, arguments);
    return b === s ? h : b;
  }, h.clickDistance = function(b) {
    return arguments.length ? (f = (b = +b) * b, h) : Math.sqrt(f);
  }, h;
}
function qr(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function Wc(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function uo() {
}
var Qn = 0.7, Qo = 1 / Qn, rn = "\\s*([+-]?\\d+)\\s*", eo = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", at = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", Mh = /^#([0-9a-f]{3,8})$/, Dh = new RegExp(`^rgb\\(${rn},${rn},${rn}\\)$`), Th = new RegExp(`^rgb\\(${at},${at},${at}\\)$`), $h = new RegExp(`^rgba\\(${rn},${rn},${rn},${eo}\\)$`), Ph = new RegExp(`^rgba\\(${at},${at},${at},${eo}\\)$`), zh = new RegExp(`^hsl\\(${eo},${at},${at}\\)$`), Rh = new RegExp(`^hsla\\(${eo},${at},${at},${eo}\\)$`), Vs = {
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
qr(uo, Rt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Hs,
  // Deprecated! Use color.formatHex.
  formatHex: Hs,
  formatHex8: Lh,
  formatHsl: Vh,
  formatRgb: Os,
  toString: Os
});
function Hs() {
  return this.rgb().formatHex();
}
function Lh() {
  return this.rgb().formatHex8();
}
function Vh() {
  return Kc(this).formatHsl();
}
function Os() {
  return this.rgb().formatRgb();
}
function Rt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = Mh.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? Fs(t) : n === 3 ? new Re(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? To(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? To(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = Dh.exec(e)) ? new Re(t[1], t[2], t[3], 1) : (t = Th.exec(e)) ? new Re(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = $h.exec(e)) ? To(t[1], t[2], t[3], t[4]) : (t = Ph.exec(e)) ? To(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = zh.exec(e)) ? Ks(t[1], t[2] / 100, t[3] / 100, 1) : (t = Rh.exec(e)) ? Ks(t[1], t[2] / 100, t[3] / 100, t[4]) : Vs.hasOwnProperty(e) ? Fs(Vs[e]) : e === "transparent" ? new Re(NaN, NaN, NaN, 0) : null;
}
function Fs(e) {
  return new Re(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function To(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new Re(e, t, n, o);
}
function Hh(e) {
  return e instanceof uo || (e = Rt(e)), e ? (e = e.rgb(), new Re(e.r, e.g, e.b, e.opacity)) : new Re();
}
function jr(e, t, n, o) {
  return arguments.length === 1 ? Hh(e) : new Re(e, t, n, o ?? 1);
}
function Re(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
qr(Re, jr, Wc(uo, {
  brighter(e) {
    return e = e == null ? Qo : Math.pow(Qo, e), new Re(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Qn : Math.pow(Qn, e), new Re(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new Re(Tt(this.r), Tt(this.g), Tt(this.b), ei(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: Bs,
  // Deprecated! Use color.formatHex.
  formatHex: Bs,
  formatHex8: Oh,
  formatRgb: Ws,
  toString: Ws
}));
function Bs() {
  return `#${Dt(this.r)}${Dt(this.g)}${Dt(this.b)}`;
}
function Oh() {
  return `#${Dt(this.r)}${Dt(this.g)}${Dt(this.b)}${Dt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function Ws() {
  const e = ei(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${Tt(this.r)}, ${Tt(this.g)}, ${Tt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function ei(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function Tt(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function Dt(e) {
  return e = Tt(e), (e < 16 ? "0" : "") + e.toString(16);
}
function Ks(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new et(e, t, n, o);
}
function Kc(e) {
  if (e instanceof et) return new et(e.h, e.s, e.l, e.opacity);
  if (e instanceof uo || (e = Rt(e)), !e) return new et();
  if (e instanceof et) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, i = Math.min(t, n, o), s = Math.max(t, n, o), a = NaN, c = s - i, d = (s + i) / 2;
  return c ? (t === s ? a = (n - o) / c + (n < o) * 6 : n === s ? a = (o - t) / c + 2 : a = (t - n) / c + 4, c /= d < 0.5 ? s + i : 2 - s - i, a *= 60) : c = d > 0 && d < 1 ? 0 : a, new et(a, c, d, e.opacity);
}
function Fh(e, t, n, o) {
  return arguments.length === 1 ? Kc(e) : new et(e, t, n, o ?? 1);
}
function et(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
qr(et, Fh, Wc(uo, {
  brighter(e) {
    return e = e == null ? Qo : Math.pow(Qo, e), new et(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Qn : Math.pow(Qn, e), new et(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, i = 2 * n - o;
    return new Re(
      nr(e >= 240 ? e - 240 : e + 120, i, o),
      nr(e, i, o),
      nr(e < 120 ? e + 240 : e - 120, i, o),
      this.opacity
    );
  },
  clamp() {
    return new et(qs(this.h), $o(this.s), $o(this.l), ei(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = ei(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${qs(this.h)}, ${$o(this.s) * 100}%, ${$o(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function qs(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function $o(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function nr(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const Xr = (e) => () => e;
function Bh(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function Wh(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function Kh(e) {
  return (e = +e) == 1 ? qc : function(t, n) {
    return n - t ? Wh(t, n, e) : Xr(isNaN(t) ? n : t);
  };
}
function qc(e, t) {
  var n = t - e;
  return n ? Bh(e, n) : Xr(isNaN(e) ? t : e);
}
const ti = (function e(t) {
  var n = Kh(t);
  function o(i, s) {
    var a = n((i = jr(i)).r, (s = jr(s)).r), c = n(i.g, s.g), d = n(i.b, s.b), l = qc(i.opacity, s.opacity);
    return function(u) {
      return i.r = a(u), i.g = c(u), i.b = d(u), i.opacity = l(u), i + "";
    };
  }
  return o.gamma = e, o;
})(1);
function qh(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), i;
  return function(s) {
    for (i = 0; i < n; ++i) o[i] = e[i] * (1 - s) + t[i] * s;
    return o;
  };
}
function Xh(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Yh(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, i = new Array(o), s = new Array(n), a;
  for (a = 0; a < o; ++a) i[a] = Yn(e[a], t[a]);
  for (; a < n; ++a) s[a] = t[a];
  return function(c) {
    for (a = 0; a < o; ++a) s[a] = i[a](c);
    return s;
  };
}
function Uh(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(o) {
    return n.setTime(e * (1 - o) + t * o), n;
  };
}
function st(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function Zh(e, t) {
  var n = {}, o = {}, i;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (i in t)
    i in e ? n[i] = Yn(e[i], t[i]) : o[i] = t[i];
  return function(s) {
    for (i in n) o[i] = n[i](s);
    return o;
  };
}
var Sr = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, or = new RegExp(Sr.source, "g");
function Gh(e) {
  return function() {
    return e;
  };
}
function Jh(e) {
  return function(t) {
    return e(t) + "";
  };
}
function Xc(e, t) {
  var n = Sr.lastIndex = or.lastIndex = 0, o, i, s, a = -1, c = [], d = [];
  for (e = e + "", t = t + ""; (o = Sr.exec(e)) && (i = or.exec(t)); )
    (s = i.index) > n && (s = t.slice(n, s), c[a] ? c[a] += s : c[++a] = s), (o = o[0]) === (i = i[0]) ? c[a] ? c[a] += i : c[++a] = i : (c[++a] = null, d.push({ i: a, x: st(o, i) })), n = or.lastIndex;
  return n < t.length && (s = t.slice(n), c[a] ? c[a] += s : c[++a] = s), c.length < 2 ? d[0] ? Jh(d[0].x) : Gh(t) : (t = d.length, function(l) {
    for (var u = 0, f; u < t; ++u) c[(f = d[u]).i] = f.x(l);
    return c.join("");
  });
}
function Yn(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? Xr(t) : (n === "number" ? st : n === "string" ? (o = Rt(t)) ? (t = o, ti) : Xc : t instanceof Rt ? ti : t instanceof Date ? Uh : Xh(t) ? qh : Array.isArray(t) ? Yh : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Zh : st)(e, t);
}
var Xs = 180 / Math.PI, kr = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function Yc(e, t, n, o, i, s) {
  var a, c, d;
  return (a = Math.sqrt(e * e + t * t)) && (e /= a, t /= a), (d = e * n + t * o) && (n -= e * d, o -= t * d), (c = Math.sqrt(n * n + o * o)) && (n /= c, o /= c, d /= c), e * o < t * n && (e = -e, t = -t, d = -d, a = -a), {
    translateX: i,
    translateY: s,
    rotate: Math.atan2(t, e) * Xs,
    skewX: Math.atan(d) * Xs,
    scaleX: a,
    scaleY: c
  };
}
var Po;
function Qh(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? kr : Yc(t.a, t.b, t.c, t.d, t.e, t.f);
}
function ep(e) {
  return e == null || (Po || (Po = document.createElementNS("http://www.w3.org/2000/svg", "g")), Po.setAttribute("transform", e), !(e = Po.transform.baseVal.consolidate())) ? kr : (e = e.matrix, Yc(e.a, e.b, e.c, e.d, e.e, e.f));
}
function Uc(e, t, n, o) {
  function i(l) {
    return l.length ? l.pop() + " " : "";
  }
  function s(l, u, f, h, p, g) {
    if (l !== f || u !== h) {
      var v = p.push("translate(", null, t, null, n);
      g.push({ i: v - 4, x: st(l, f) }, { i: v - 2, x: st(u, h) });
    } else (f || h) && p.push("translate(" + f + t + h + n);
  }
  function a(l, u, f, h) {
    l !== u ? (l - u > 180 ? u += 360 : u - l > 180 && (l += 360), h.push({ i: f.push(i(f) + "rotate(", null, o) - 2, x: st(l, u) })) : u && f.push(i(f) + "rotate(" + u + o);
  }
  function c(l, u, f, h) {
    l !== u ? h.push({ i: f.push(i(f) + "skewX(", null, o) - 2, x: st(l, u) }) : u && f.push(i(f) + "skewX(" + u + o);
  }
  function d(l, u, f, h, p, g) {
    if (l !== f || u !== h) {
      var v = p.push(i(p) + "scale(", null, ",", null, ")");
      g.push({ i: v - 4, x: st(l, f) }, { i: v - 2, x: st(u, h) });
    } else (f !== 1 || h !== 1) && p.push(i(p) + "scale(" + f + "," + h + ")");
  }
  return function(l, u) {
    var f = [], h = [];
    return l = e(l), u = e(u), s(l.translateX, l.translateY, u.translateX, u.translateY, f, h), a(l.rotate, u.rotate, f, h), c(l.skewX, u.skewX, f, h), d(l.scaleX, l.scaleY, u.scaleX, u.scaleY, f, h), l = u = null, function(p) {
      for (var g = -1, v = h.length, w; ++g < v; ) f[(w = h[g]).i] = w.x(p);
      return f.join("");
    };
  };
}
var tp = Uc(Qh, "px, ", "px)", "deg)"), np = Uc(ep, ", ", ")", ")"), op = 1e-12;
function Ys(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function ip(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function rp(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const Wo = (function e(t, n, o) {
  function i(s, a) {
    var c = s[0], d = s[1], l = s[2], u = a[0], f = a[1], h = a[2], p = u - c, g = f - d, v = p * p + g * g, w, m;
    if (v < op)
      m = Math.log(h / l) / t, w = function(j) {
        return [
          c + j * p,
          d + j * g,
          l * Math.exp(t * j * m)
        ];
      };
    else {
      var k = Math.sqrt(v), y = (h * h - l * l + o * v) / (2 * l * n * k), b = (h * h - l * l - o * v) / (2 * h * n * k), S = Math.log(Math.sqrt(y * y + 1) - y), E = Math.log(Math.sqrt(b * b + 1) - b);
      m = (E - S) / t, w = function(j) {
        var _ = j * m, D = Ys(S), B = l / (n * k) * (D * rp(t * _ + S) - ip(S));
        return [
          c + B * p,
          d + B * g,
          l * D / Ys(t * _ + S)
        ];
      };
    }
    return w.duration = m * 1e3 * t / Math.SQRT2, w;
  }
  return i.rho = function(s) {
    var a = Math.max(1e-3, +s), c = a * a, d = c * c;
    return e(a, c, d);
  }, i;
})(Math.SQRT2, 2, 4);
var cn = 0, Kn = 0, zn = 0, Zc = 1e3, ni, qn, oi = 0, Lt = 0, Ni = 0, to = typeof performance == "object" && performance.now ? performance : Date, Gc = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function Yr() {
  return Lt || (Gc(sp), Lt = to.now() + Ni);
}
function sp() {
  Lt = 0;
}
function ii() {
  this._call = this._time = this._next = null;
}
ii.prototype = Jc.prototype = {
  constructor: ii,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? Yr() : +n) + (t == null ? 0 : +t), !this._next && qn !== this && (qn ? qn._next = this : ni = this, qn = this), this._call = e, this._time = n, Er();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Er());
  }
};
function Jc(e, t, n) {
  var o = new ii();
  return o.restart(e, t, n), o;
}
function ap() {
  Yr(), ++cn;
  for (var e = ni, t; e; )
    (t = Lt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --cn;
}
function Us() {
  Lt = (oi = to.now()) + Ni, cn = Kn = 0;
  try {
    ap();
  } finally {
    cn = 0, lp(), Lt = 0;
  }
}
function cp() {
  var e = to.now(), t = e - oi;
  t > Zc && (Ni -= t, oi = e);
}
function lp() {
  for (var e, t = ni, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : ni = n);
  qn = e, Er(o);
}
function Er(e) {
  if (!cn) {
    Kn && (Kn = clearTimeout(Kn));
    var t = e - Lt;
    t > 24 ? (e < 1 / 0 && (Kn = setTimeout(Us, e - to.now() - Ni)), zn && (zn = clearInterval(zn))) : (zn || (oi = to.now(), zn = setInterval(cp, Zc)), cn = 1, Gc(Us));
  }
}
function Zs(e, t, n) {
  var o = new ii();
  return t = t == null ? 0 : +t, o.restart((i) => {
    o.stop(), e(i + t);
  }, t, n), o;
}
var dp = vi("start", "end", "cancel", "interrupt"), up = [], Qc = 0, Gs = 1, Cr = 2, Ko = 3, Js = 4, Ir = 5, qo = 6;
function ji(e, t, n, o, i, s) {
  var a = e.__transition;
  if (!a) e.__transition = {};
  else if (n in a) return;
  fp(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: i,
    // For context during callback.
    on: dp,
    tween: up,
    time: s.time,
    delay: s.delay,
    duration: s.duration,
    ease: s.ease,
    timer: null,
    state: Qc
  });
}
function Ur(e, t) {
  var n = rt(e, t);
  if (n.state > Qc) throw new Error("too late; already scheduled");
  return n;
}
function lt(e, t) {
  var n = rt(e, t);
  if (n.state > Ko) throw new Error("too late; already running");
  return n;
}
function rt(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function fp(e, t, n) {
  var o = e.__transition, i;
  o[t] = n, n.timer = Jc(s, 0, n.time);
  function s(l) {
    n.state = Gs, n.timer.restart(a, n.delay, n.time), n.delay <= l && a(l - n.delay);
  }
  function a(l) {
    var u, f, h, p;
    if (n.state !== Gs) return d();
    for (u in o)
      if (p = o[u], p.name === n.name) {
        if (p.state === Ko) return Zs(a);
        p.state === Js ? (p.state = qo, p.timer.stop(), p.on.call("interrupt", e, e.__data__, p.index, p.group), delete o[u]) : +u < t && (p.state = qo, p.timer.stop(), p.on.call("cancel", e, e.__data__, p.index, p.group), delete o[u]);
      }
    if (Zs(function() {
      n.state === Ko && (n.state = Js, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = Cr, n.on.call("start", e, e.__data__, n.index, n.group), n.state === Cr) {
      for (n.state = Ko, i = new Array(h = n.tween.length), u = 0, f = -1; u < h; ++u)
        (p = n.tween[u].value.call(e, e.__data__, n.index, n.group)) && (i[++f] = p);
      i.length = f + 1;
    }
  }
  function c(l) {
    for (var u = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(d), n.state = Ir, 1), f = -1, h = i.length; ++f < h; )
      i[f].call(e, u);
    n.state === Ir && (n.on.call("end", e, e.__data__, n.index, n.group), d());
  }
  function d() {
    n.state = qo, n.timer.stop(), delete o[t];
    for (var l in o) return;
    delete e.__transition;
  }
}
function Xo(e, t) {
  var n = e.__transition, o, i, s = !0, a;
  if (n) {
    t = t == null ? null : t + "";
    for (a in n) {
      if ((o = n[a]).name !== t) {
        s = !1;
        continue;
      }
      i = o.state > Cr && o.state < Ir, o.state = qo, o.timer.stop(), o.on.call(i ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[a];
    }
    s && delete e.__transition;
  }
}
function hp(e) {
  return this.each(function() {
    Xo(this, e);
  });
}
function pp(e, t) {
  var n, o;
  return function() {
    var i = lt(this, e), s = i.tween;
    if (s !== n) {
      o = n = s;
      for (var a = 0, c = o.length; a < c; ++a)
        if (o[a].name === t) {
          o = o.slice(), o.splice(a, 1);
          break;
        }
    }
    i.tween = o;
  };
}
function gp(e, t, n) {
  var o, i;
  if (typeof n != "function") throw new Error();
  return function() {
    var s = lt(this, e), a = s.tween;
    if (a !== o) {
      i = (o = a).slice();
      for (var c = { name: t, value: n }, d = 0, l = i.length; d < l; ++d)
        if (i[d].name === t) {
          i[d] = c;
          break;
        }
      d === l && i.push(c);
    }
    s.tween = i;
  };
}
function yp(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = rt(this.node(), n).tween, i = 0, s = o.length, a; i < s; ++i)
      if ((a = o[i]).name === e)
        return a.value;
    return null;
  }
  return this.each((t == null ? pp : gp)(n, e, t));
}
function Zr(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var i = lt(this, o);
    (i.value || (i.value = {}))[t] = n.apply(this, arguments);
  }), function(i) {
    return rt(i, o).value[t];
  };
}
function el(e, t) {
  var n;
  return (typeof t == "number" ? st : t instanceof Rt ? ti : (n = Rt(t)) ? (t = n, ti) : Xc)(e, t);
}
function mp(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function xp(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function wp(e, t, n) {
  var o, i = n + "", s;
  return function() {
    var a = this.getAttribute(e);
    return a === i ? null : a === o ? s : s = t(o = a, n);
  };
}
function vp(e, t, n) {
  var o, i = n + "", s;
  return function() {
    var a = this.getAttributeNS(e.space, e.local);
    return a === i ? null : a === o ? s : s = t(o = a, n);
  };
}
function bp(e, t, n) {
  var o, i, s;
  return function() {
    var a, c = n(this), d;
    return c == null ? void this.removeAttribute(e) : (a = this.getAttribute(e), d = c + "", a === d ? null : a === o && d === i ? s : (i = d, s = t(o = a, c)));
  };
}
function Np(e, t, n) {
  var o, i, s;
  return function() {
    var a, c = n(this), d;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (a = this.getAttributeNS(e.space, e.local), d = c + "", a === d ? null : a === o && d === i ? s : (i = d, s = t(o = a, c)));
  };
}
function jp(e, t) {
  var n = bi(e), o = n === "transform" ? np : el;
  return this.attrTween(e, typeof t == "function" ? (n.local ? Np : bp)(n, o, Zr(this, "attr." + e, t)) : t == null ? (n.local ? xp : mp)(n) : (n.local ? vp : wp)(n, o, t));
}
function Sp(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function kp(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function Ep(e, t) {
  var n, o;
  function i() {
    var s = t.apply(this, arguments);
    return s !== o && (n = (o = s) && kp(e, s)), n;
  }
  return i._value = t, i;
}
function Cp(e, t) {
  var n, o;
  function i() {
    var s = t.apply(this, arguments);
    return s !== o && (n = (o = s) && Sp(e, s)), n;
  }
  return i._value = t, i;
}
function Ip(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = bi(e);
  return this.tween(n, (o.local ? Ep : Cp)(o, t));
}
function Ap(e, t) {
  return function() {
    Ur(this, e).delay = +t.apply(this, arguments);
  };
}
function _p(e, t) {
  return t = +t, function() {
    Ur(this, e).delay = t;
  };
}
function Mp(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Ap : _p)(t, e)) : rt(this.node(), t).delay;
}
function Dp(e, t) {
  return function() {
    lt(this, e).duration = +t.apply(this, arguments);
  };
}
function Tp(e, t) {
  return t = +t, function() {
    lt(this, e).duration = t;
  };
}
function $p(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Dp : Tp)(t, e)) : rt(this.node(), t).duration;
}
function Pp(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    lt(this, e).ease = t;
  };
}
function zp(e) {
  var t = this._id;
  return arguments.length ? this.each(Pp(t, e)) : rt(this.node(), t).ease;
}
function Rp(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    lt(this, e).ease = n;
  };
}
function Lp(e) {
  if (typeof e != "function") throw new Error();
  return this.each(Rp(this._id, e));
}
function Vp(e) {
  typeof e != "function" && (e = Mc(e));
  for (var t = this._groups, n = t.length, o = new Array(n), i = 0; i < n; ++i)
    for (var s = t[i], a = s.length, c = o[i] = [], d, l = 0; l < a; ++l)
      (d = s[l]) && e.call(d, d.__data__, l, s) && c.push(d);
  return new gt(o, this._parents, this._name, this._id);
}
function Hp(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, i = n.length, s = Math.min(o, i), a = new Array(o), c = 0; c < s; ++c)
    for (var d = t[c], l = n[c], u = d.length, f = a[c] = new Array(u), h, p = 0; p < u; ++p)
      (h = d[p] || l[p]) && (f[p] = h);
  for (; c < o; ++c)
    a[c] = t[c];
  return new gt(a, this._parents, this._name, this._id);
}
function Op(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function Fp(e, t, n) {
  var o, i, s = Op(t) ? Ur : lt;
  return function() {
    var a = s(this, e), c = a.on;
    c !== o && (i = (o = c).copy()).on(t, n), a.on = i;
  };
}
function Bp(e, t) {
  var n = this._id;
  return arguments.length < 2 ? rt(this.node(), n).on.on(e) : this.each(Fp(n, e, t));
}
function Wp(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function Kp() {
  return this.on("end.remove", Wp(this._id));
}
function qp(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Wr(e));
  for (var o = this._groups, i = o.length, s = new Array(i), a = 0; a < i; ++a)
    for (var c = o[a], d = c.length, l = s[a] = new Array(d), u, f, h = 0; h < d; ++h)
      (u = c[h]) && (f = e.call(u, u.__data__, h, c)) && ("__data__" in u && (f.__data__ = u.__data__), l[h] = f, ji(l[h], t, n, h, l, rt(u, n)));
  return new gt(s, this._parents, t, n);
}
function Xp(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = _c(e));
  for (var o = this._groups, i = o.length, s = [], a = [], c = 0; c < i; ++c)
    for (var d = o[c], l = d.length, u, f = 0; f < l; ++f)
      if (u = d[f]) {
        for (var h = e.call(u, u.__data__, f, d), p, g = rt(u, n), v = 0, w = h.length; v < w; ++v)
          (p = h[v]) && ji(p, t, n, v, h, g);
        s.push(h), a.push(u);
      }
  return new gt(s, a, t, n);
}
var Yp = lo.prototype.constructor;
function Up() {
  return new Yp(this._groups, this._parents);
}
function Zp(e, t) {
  var n, o, i;
  return function() {
    var s = an(this, e), a = (this.style.removeProperty(e), an(this, e));
    return s === a ? null : s === n && a === o ? i : i = t(n = s, o = a);
  };
}
function tl(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Gp(e, t, n) {
  var o, i = n + "", s;
  return function() {
    var a = an(this, e);
    return a === i ? null : a === o ? s : s = t(o = a, n);
  };
}
function Jp(e, t, n) {
  var o, i, s;
  return function() {
    var a = an(this, e), c = n(this), d = c + "";
    return c == null && (d = c = (this.style.removeProperty(e), an(this, e))), a === d ? null : a === o && d === i ? s : (i = d, s = t(o = a, c));
  };
}
function Qp(e, t) {
  var n, o, i, s = "style." + t, a = "end." + s, c;
  return function() {
    var d = lt(this, e), l = d.on, u = d.value[s] == null ? c || (c = tl(t)) : void 0;
    (l !== n || i !== u) && (o = (n = l).copy()).on(a, i = u), d.on = o;
  };
}
function eg(e, t, n) {
  var o = (e += "") == "transform" ? tp : el;
  return t == null ? this.styleTween(e, Zp(e, o)).on("end.style." + e, tl(e)) : typeof t == "function" ? this.styleTween(e, Jp(e, o, Zr(this, "style." + e, t))).each(Qp(this._id, e)) : this.styleTween(e, Gp(e, o, t), n).on("end.style." + e, null);
}
function tg(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function ng(e, t, n) {
  var o, i;
  function s() {
    var a = t.apply(this, arguments);
    return a !== i && (o = (i = a) && tg(e, a, n)), o;
  }
  return s._value = t, s;
}
function og(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, ng(e, t, n ?? ""));
}
function ig(e) {
  return function() {
    this.textContent = e;
  };
}
function rg(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function sg(e) {
  return this.tween("text", typeof e == "function" ? rg(Zr(this, "text", e)) : ig(e == null ? "" : e + ""));
}
function ag(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function cg(e) {
  var t, n;
  function o() {
    var i = e.apply(this, arguments);
    return i !== n && (t = (n = i) && ag(i)), t;
  }
  return o._value = e, o;
}
function lg(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, cg(e));
}
function dg() {
  for (var e = this._name, t = this._id, n = nl(), o = this._groups, i = o.length, s = 0; s < i; ++s)
    for (var a = o[s], c = a.length, d, l = 0; l < c; ++l)
      if (d = a[l]) {
        var u = rt(d, t);
        ji(d, e, n, l, a, {
          time: u.time + u.delay + u.duration,
          delay: 0,
          duration: u.duration,
          ease: u.ease
        });
      }
  return new gt(o, this._parents, e, n);
}
function ug() {
  var e, t, n = this, o = n._id, i = n.size();
  return new Promise(function(s, a) {
    var c = { value: a }, d = { value: function() {
      --i === 0 && s();
    } };
    n.each(function() {
      var l = lt(this, o), u = l.on;
      u !== e && (t = (e = u).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(d)), l.on = t;
    }), i === 0 && s();
  });
}
var fg = 0;
function gt(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function nl() {
  return ++fg;
}
var ht = lo.prototype;
gt.prototype = {
  constructor: gt,
  select: qp,
  selectAll: Xp,
  selectChild: ht.selectChild,
  selectChildren: ht.selectChildren,
  filter: Vp,
  merge: Hp,
  selection: Up,
  transition: dg,
  call: ht.call,
  nodes: ht.nodes,
  node: ht.node,
  size: ht.size,
  empty: ht.empty,
  each: ht.each,
  on: Bp,
  attr: jp,
  attrTween: Ip,
  style: eg,
  styleTween: og,
  text: sg,
  textTween: lg,
  remove: Kp,
  tween: yp,
  delay: Mp,
  duration: $p,
  ease: zp,
  easeVarying: Lp,
  end: ug,
  [Symbol.iterator]: ht[Symbol.iterator]
};
function hg(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var pg = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: hg
};
function gg(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function yg(e) {
  var t, n;
  e instanceof gt ? (t = e._id, e = e._name) : (t = nl(), (n = pg).time = Yr(), e = e == null ? null : e + "");
  for (var o = this._groups, i = o.length, s = 0; s < i; ++s)
    for (var a = o[s], c = a.length, d, l = 0; l < c; ++l)
      (d = a[l]) && ji(d, e, t, l, a, n || gg(d, t));
  return new gt(o, this._parents, e, t);
}
lo.prototype.interrupt = hp;
lo.prototype.transition = yg;
const zo = (e) => () => e;
function mg(e, {
  sourceEvent: t,
  target: n,
  transform: o,
  dispatch: i
}) {
  Object.defineProperties(this, {
    type: { value: e, enumerable: !0, configurable: !0 },
    sourceEvent: { value: t, enumerable: !0, configurable: !0 },
    target: { value: n, enumerable: !0, configurable: !0 },
    transform: { value: o, enumerable: !0, configurable: !0 },
    _: { value: i }
  });
}
function pt(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
pt.prototype = {
  constructor: pt,
  scale: function(e) {
    return e === 1 ? this : new pt(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new pt(this.k, this.x + this.k * e, this.y + this.k * t);
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
var Si = new pt(1, 0, 0);
ol.prototype = pt.prototype;
function ol(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return Si;
  return e.__zoom;
}
function ir(e) {
  e.stopImmediatePropagation();
}
function Rn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function xg(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function wg() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function Qs() {
  return this.__zoom || Si;
}
function vg(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function bg() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Ng(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], i = e.invertX(t[1][0]) - n[1][0], s = e.invertY(t[0][1]) - n[0][1], a = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    i > o ? (o + i) / 2 : Math.min(0, o) || Math.max(0, i),
    a > s ? (s + a) / 2 : Math.min(0, s) || Math.max(0, a)
  );
}
function il() {
  var e = xg, t = wg, n = Ng, o = vg, i = bg, s = [0, 1 / 0], a = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, d = Wo, l = vi("start", "zoom", "end"), u, f, h, p = 500, g = 150, v = 0, w = 10;
  function m(N) {
    N.property("__zoom", Qs).on("wheel.zoom", _, { passive: !1 }).on("mousedown.zoom", D).on("dblclick.zoom", B).filter(i).on("touchstart.zoom", M).on("touchmove.zoom", T).on("touchend.zoom touchcancel.zoom", L).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  m.transform = function(N, A, I, $) {
    var z = N.selection ? N.selection() : N;
    z.property("__zoom", Qs), N !== z ? S(N, A, I, $) : z.interrupt().each(function() {
      E(this, arguments).event($).start().zoom(null, typeof A == "function" ? A.apply(this, arguments) : A).end();
    });
  }, m.scaleBy = function(N, A, I, $) {
    m.scaleTo(N, function() {
      var z = this.__zoom.k, P = typeof A == "function" ? A.apply(this, arguments) : A;
      return z * P;
    }, I, $);
  }, m.scaleTo = function(N, A, I, $) {
    m.transform(N, function() {
      var z = t.apply(this, arguments), P = this.__zoom, F = I == null ? b(z) : typeof I == "function" ? I.apply(this, arguments) : I, K = P.invert(F), H = typeof A == "function" ? A.apply(this, arguments) : A;
      return n(y(k(P, H), F, K), z, a);
    }, I, $);
  }, m.translateBy = function(N, A, I, $) {
    m.transform(N, function() {
      return n(this.__zoom.translate(
        typeof A == "function" ? A.apply(this, arguments) : A,
        typeof I == "function" ? I.apply(this, arguments) : I
      ), t.apply(this, arguments), a);
    }, null, $);
  }, m.translateTo = function(N, A, I, $, z) {
    m.transform(N, function() {
      var P = t.apply(this, arguments), F = this.__zoom, K = $ == null ? b(P) : typeof $ == "function" ? $.apply(this, arguments) : $;
      return n(Si.translate(K[0], K[1]).scale(F.k).translate(
        typeof A == "function" ? -A.apply(this, arguments) : -A,
        typeof I == "function" ? -I.apply(this, arguments) : -I
      ), P, a);
    }, $, z);
  };
  function k(N, A) {
    return A = Math.max(s[0], Math.min(s[1], A)), A === N.k ? N : new pt(A, N.x, N.y);
  }
  function y(N, A, I) {
    var $ = A[0] - I[0] * N.k, z = A[1] - I[1] * N.k;
    return $ === N.x && z === N.y ? N : new pt(N.k, $, z);
  }
  function b(N) {
    return [(+N[0][0] + +N[1][0]) / 2, (+N[0][1] + +N[1][1]) / 2];
  }
  function S(N, A, I, $) {
    N.on("start.zoom", function() {
      E(this, arguments).event($).start();
    }).on("interrupt.zoom end.zoom", function() {
      E(this, arguments).event($).end();
    }).tween("zoom", function() {
      var z = this, P = arguments, F = E(z, P).event($), K = t.apply(z, P), H = I == null ? b(K) : typeof I == "function" ? I.apply(z, P) : I, Z = Math.max(K[1][0] - K[0][0], K[1][1] - K[0][1]), X = z.__zoom, ne = typeof A == "function" ? A.apply(z, P) : A, se = d(X.invert(H).concat(Z / X.k), ne.invert(H).concat(Z / ne.k));
      return function(G) {
        if (G === 1) G = ne;
        else {
          var V = se(G), Y = Z / V[2];
          G = new pt(Y, H[0] - V[0] * Y, H[1] - V[1] * Y);
        }
        F.zoom(null, G);
      };
    });
  }
  function E(N, A, I) {
    return !I && N.__zooming || new j(N, A);
  }
  function j(N, A) {
    this.that = N, this.args = A, this.active = 0, this.sourceEvent = null, this.extent = t.apply(N, A), this.taps = 0;
  }
  j.prototype = {
    event: function(N) {
      return N && (this.sourceEvent = N), this;
    },
    start: function() {
      return ++this.active === 1 && (this.that.__zooming = this, this.emit("start")), this;
    },
    zoom: function(N, A) {
      return this.mouse && N !== "mouse" && (this.mouse[1] = A.invert(this.mouse[0])), this.touch0 && N !== "touch" && (this.touch0[1] = A.invert(this.touch0[0])), this.touch1 && N !== "touch" && (this.touch1[1] = A.invert(this.touch1[0])), this.that.__zoom = A, this.emit("zoom"), this;
    },
    end: function() {
      return --this.active === 0 && (delete this.that.__zooming, this.emit("end")), this;
    },
    emit: function(N) {
      var A = We(this.that).datum();
      l.call(
        N,
        this.that,
        new mg(N, {
          sourceEvent: this.sourceEvent,
          target: m,
          transform: this.that.__zoom,
          dispatch: l
        }),
        A
      );
    }
  };
  function _(N, ...A) {
    if (!e.apply(this, arguments)) return;
    var I = E(this, A).event(N), $ = this.__zoom, z = Math.max(s[0], Math.min(s[1], $.k * Math.pow(2, o.apply(this, arguments)))), P = Qe(N);
    if (I.wheel)
      (I.mouse[0][0] !== P[0] || I.mouse[0][1] !== P[1]) && (I.mouse[1] = $.invert(I.mouse[0] = P)), clearTimeout(I.wheel);
    else {
      if ($.k === z) return;
      I.mouse = [P, $.invert(P)], Xo(this), I.start();
    }
    Rn(N), I.wheel = setTimeout(F, g), I.zoom("mouse", n(y(k($, z), I.mouse[0], I.mouse[1]), I.extent, a));
    function F() {
      I.wheel = null, I.end();
    }
  }
  function D(N, ...A) {
    if (h || !e.apply(this, arguments)) return;
    var I = N.currentTarget, $ = E(this, A, !0).event(N), z = We(N.view).on("mousemove.zoom", H, !0).on("mouseup.zoom", Z, !0), P = Qe(N, I), F = N.clientX, K = N.clientY;
    Oc(N.view), ir(N), $.mouse = [P, this.__zoom.invert(P)], Xo(this), $.start();
    function H(X) {
      if (Rn(X), !$.moved) {
        var ne = X.clientX - F, se = X.clientY - K;
        $.moved = ne * ne + se * se > v;
      }
      $.event(X).zoom("mouse", n(y($.that.__zoom, $.mouse[0] = Qe(X, I), $.mouse[1]), $.extent, a));
    }
    function Z(X) {
      z.on("mousemove.zoom mouseup.zoom", null), Fc(X.view, $.moved), Rn(X), $.event(X).end();
    }
  }
  function B(N, ...A) {
    if (e.apply(this, arguments)) {
      var I = this.__zoom, $ = Qe(N.changedTouches ? N.changedTouches[0] : N, this), z = I.invert($), P = I.k * (N.shiftKey ? 0.5 : 2), F = n(y(k(I, P), $, z), t.apply(this, A), a);
      Rn(N), c > 0 ? We(this).transition().duration(c).call(S, F, $, N) : We(this).call(m.transform, F, $, N);
    }
  }
  function M(N, ...A) {
    if (e.apply(this, arguments)) {
      var I = N.touches, $ = I.length, z = E(this, A, N.changedTouches.length === $).event(N), P, F, K, H;
      for (ir(N), F = 0; F < $; ++F)
        K = I[F], H = Qe(K, this), H = [H, this.__zoom.invert(H), K.identifier], z.touch0 ? !z.touch1 && z.touch0[2] !== H[2] && (z.touch1 = H, z.taps = 0) : (z.touch0 = H, P = !0, z.taps = 1 + !!u);
      u && (u = clearTimeout(u)), P && (z.taps < 2 && (f = H[0], u = setTimeout(function() {
        u = null;
      }, p)), Xo(this), z.start());
    }
  }
  function T(N, ...A) {
    if (this.__zooming) {
      var I = E(this, A).event(N), $ = N.changedTouches, z = $.length, P, F, K, H;
      for (Rn(N), P = 0; P < z; ++P)
        F = $[P], K = Qe(F, this), I.touch0 && I.touch0[2] === F.identifier ? I.touch0[0] = K : I.touch1 && I.touch1[2] === F.identifier && (I.touch1[0] = K);
      if (F = I.that.__zoom, I.touch1) {
        var Z = I.touch0[0], X = I.touch0[1], ne = I.touch1[0], se = I.touch1[1], G = (G = ne[0] - Z[0]) * G + (G = ne[1] - Z[1]) * G, V = (V = se[0] - X[0]) * V + (V = se[1] - X[1]) * V;
        F = k(F, Math.sqrt(G / V)), K = [(Z[0] + ne[0]) / 2, (Z[1] + ne[1]) / 2], H = [(X[0] + se[0]) / 2, (X[1] + se[1]) / 2];
      } else if (I.touch0) K = I.touch0[0], H = I.touch0[1];
      else return;
      I.zoom("touch", n(y(F, K, H), I.extent, a));
    }
  }
  function L(N, ...A) {
    if (this.__zooming) {
      var I = E(this, A).event(N), $ = N.changedTouches, z = $.length, P, F;
      for (ir(N), h && clearTimeout(h), h = setTimeout(function() {
        h = null;
      }, p), P = 0; P < z; ++P)
        F = $[P], I.touch0 && I.touch0[2] === F.identifier ? delete I.touch0 : I.touch1 && I.touch1[2] === F.identifier && delete I.touch1;
      if (I.touch1 && !I.touch0 && (I.touch0 = I.touch1, delete I.touch1), I.touch0) I.touch0[1] = this.__zoom.invert(I.touch0[0]);
      else if (I.end(), I.taps === 2 && (F = Qe(F, this), Math.hypot(f[0] - F[0], f[1] - F[1]) < w)) {
        var K = We(this).on("dblclick.zoom");
        K && K.apply(this, arguments);
      }
    }
  }
  return m.wheelDelta = function(N) {
    return arguments.length ? (o = typeof N == "function" ? N : zo(+N), m) : o;
  }, m.filter = function(N) {
    return arguments.length ? (e = typeof N == "function" ? N : zo(!!N), m) : e;
  }, m.touchable = function(N) {
    return arguments.length ? (i = typeof N == "function" ? N : zo(!!N), m) : i;
  }, m.extent = function(N) {
    return arguments.length ? (t = typeof N == "function" ? N : zo([[+N[0][0], +N[0][1]], [+N[1][0], +N[1][1]]]), m) : t;
  }, m.scaleExtent = function(N) {
    return arguments.length ? (s[0] = +N[0], s[1] = +N[1], m) : [s[0], s[1]];
  }, m.translateExtent = function(N) {
    return arguments.length ? (a[0][0] = +N[0][0], a[1][0] = +N[1][0], a[0][1] = +N[0][1], a[1][1] = +N[1][1], m) : [[a[0][0], a[0][1]], [a[1][0], a[1][1]]];
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
    return arguments.length ? (w = +N, m) : w;
  }, m;
}
const qe = {
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
}, no = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], rl = ["Enter", " ", "Escape"], sl = {
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
var ln;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(ln || (ln = {}));
var $t;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})($t || ($t = {}));
var oo;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(oo || (oo = {}));
const al = {
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
var bt;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(bt || (bt = {}));
var ri;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(ri || (ri = {}));
var te;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(te || (te = {}));
const ea = {
  [te.Left]: te.Right,
  [te.Right]: te.Left,
  [te.Top]: te.Bottom,
  [te.Bottom]: te.Top
};
function cl(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const ll = (e) => "id" in e && "source" in e && "target" in e, jg = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), Gr = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), fo = (e, t = [0, 0]) => {
  const { width: n, height: o } = yt(e), i = e.origin ?? t, s = n * i[0], a = o * i[1];
  return {
    x: e.position.x - s,
    y: e.position.y - a
  };
}, Sg = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, i) => {
    const s = typeof i == "string";
    let a = !t.nodeLookup && !s ? i : void 0;
    t.nodeLookup && (a = s ? t.nodeLookup.get(i) : Gr(i) ? i : t.nodeLookup.get(i.id));
    const c = a ? si(a, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return ki(o, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return Ei(n);
}, ho = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((i) => {
    (t.filter === void 0 || t.filter(i)) && (n = ki(n, si(i)), o = !0);
  }), o ? Ei(n) : { x: 0, y: 0, width: 0, height: 0 };
}, Jr = (e, t, [n, o, i] = [0, 0, 1], s = !1, a = !1) => {
  const c = {
    ...mn(t, [n, o, i]),
    width: t.width / i,
    height: t.height / i
  }, d = [];
  for (const l of e.values()) {
    const { measured: u, selectable: f = !0, hidden: h = !1 } = l;
    if (a && !f || h)
      continue;
    const p = u.width ?? l.width ?? l.initialWidth ?? null, g = u.height ?? l.height ?? l.initialHeight ?? null, v = io(c, un(l)), w = (p ?? 0) * (g ?? 0), m = s && v > 0;
    (!l.internals.handleBounds || m || v >= w || l.dragging) && d.push(l);
  }
  return d;
}, kg = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function Eg(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((i) => i.id)) : null;
  return e.forEach((i) => {
    i.measured.width && i.measured.height && (t?.includeHiddenNodes || !i.hidden) && (!o || o.has(i.id)) && n.set(i.id, i);
  }), n;
}
async function Cg({ nodes: e, width: t, height: n, panZoom: o, minZoom: i, maxZoom: s }, a) {
  if (e.size === 0)
    return !0;
  const c = Eg(e, a), d = ho(c), l = es(d, t, n, a?.minZoom ?? i, a?.maxZoom ?? s, a?.padding ?? 0.1);
  return await o.setViewport(l, {
    duration: a?.duration,
    ease: a?.ease,
    interpolate: a?.interpolate
  }), !0;
}
function dl({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: i, onError: s }) {
  const a = n.get(e), c = a.parentId ? n.get(a.parentId) : void 0, { x: d, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, u = a.origin ?? o;
  let f = a.extent || i;
  if (a.extent === "parent" && !a.expandParent)
    if (!c)
      s?.("005", qe.error005());
    else {
      const p = c.measured.width, g = c.measured.height;
      p && g && (f = [
        [d, l],
        [d + p, l + g]
      ]);
    }
  else c && Ht(a.extent) && (f = [
    [a.extent[0][0] + d, a.extent[0][1] + l],
    [a.extent[1][0] + d, a.extent[1][1] + l]
  ]);
  const h = Ht(f) ? Vt(t, f, a.measured) : t;
  return (a.measured.width === void 0 || a.measured.height === void 0) && s?.("015", qe.error015()), {
    position: {
      x: h.x - d + (a.measured.width ?? 0) * u[0],
      y: h.y - l + (a.measured.height ?? 0) * u[1]
    },
    positionAbsolute: h
  };
}
async function Ig({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: i }) {
  const s = new Set(e.map((h) => h.id)), a = [];
  for (const h of n) {
    if (h.deletable === !1)
      continue;
    const p = s.has(h.id), g = !p && h.parentId && a.find((v) => v.id === h.parentId);
    (p || g) && a.push(h);
  }
  const c = new Set(t.map((h) => h.id)), d = o.filter((h) => h.deletable !== !1), u = kg(a, d);
  for (const h of d)
    c.has(h.id) && !u.find((g) => g.id === h.id) && u.push(h);
  if (!i)
    return {
      edges: u,
      nodes: a
    };
  const f = await i({
    nodes: a,
    edges: u
  });
  return typeof f == "boolean" ? f ? { edges: u, nodes: a } : { edges: [], nodes: [] } : f;
}
const dn = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), Vt = (e = { x: 0, y: 0 }, t, n) => ({
  x: dn(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: dn(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function ul(e, t, n) {
  const { width: o, height: i } = yt(n), { x: s, y: a } = n.internals.positionAbsolute;
  return Vt(e, [
    [s, a],
    [s + o, a + i]
  ], t);
}
const ta = (e, t, n) => e < t ? dn(Math.abs(e - t), 1, t) / t : e > n ? -dn(Math.abs(e - n), 1, t) / t : 0, Qr = (e, t, n = 15, o = 40) => {
  const i = ta(e.x, o, t.width - o) * n, s = ta(e.y, o, t.height - o) * n;
  return [i, s];
}, ki = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), Ar = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), Ei = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), un = (e, t = [0, 0]) => {
  const { x: n, y: o } = Gr(e) ? e.internals.positionAbsolute : fo(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, si = (e, t = [0, 0]) => {
  const { x: n, y: o } = Gr(e) ? e.internals.positionAbsolute : fo(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, fl = (e, t) => Ei(ki(Ar(e), Ar(t))), io = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, na = (e) => tt(e.width) && tt(e.height) && tt(e.x) && tt(e.y), tt = (e) => !isNaN(e) && isFinite(e), hl = (e, t) => (n, o) => {
}, po = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), mn = ({ x: e, y: t }, [n, o, i], s = !1, a = [1, 1]) => {
  const c = {
    x: (e - n) / i,
    y: (t - o) / i
  };
  return s ? po(c, a) : c;
}, fn = ({ x: e, y: t }, [n, o, i]) => ({
  x: e * i + n,
  y: t * i + o
});
function Qt(e, t) {
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
function Ag(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = Qt(e, n), i = Qt(e, t);
    return {
      top: o,
      right: i,
      bottom: o,
      left: i,
      x: i * 2,
      y: o * 2
    };
  }
  if (typeof e == "object") {
    const o = Qt(e.top ?? e.y ?? 0, n), i = Qt(e.bottom ?? e.y ?? 0, n), s = Qt(e.left ?? e.x ?? 0, t), a = Qt(e.right ?? e.x ?? 0, t);
    return { top: o, right: a, bottom: i, left: s, x: s + a, y: o + i };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function _g(e, t, n, o, i, s) {
  const { x: a, y: c } = fn(e, [t, n, o]), { x: d, y: l } = fn({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), u = i - d, f = s - l;
  return {
    left: Math.floor(a),
    top: Math.floor(c),
    right: Math.floor(u),
    bottom: Math.floor(f)
  };
}
const es = (e, t, n, o, i, s) => {
  const a = Ag(s, t, n), c = (t - a.x) / e.width, d = (n - a.y) / e.height, l = Math.min(c, d), u = dn(l, o, i), f = e.x + e.width / 2, h = e.y + e.height / 2, p = t / 2 - f * u, g = n / 2 - h * u, v = _g(e, p, g, u, t, n), w = {
    left: Math.min(v.left - a.left, 0),
    top: Math.min(v.top - a.top, 0),
    right: Math.min(v.right - a.right, 0),
    bottom: Math.min(v.bottom - a.bottom, 0)
  };
  return {
    x: p - w.left + w.right,
    y: g - w.top + w.bottom,
    zoom: u
  };
}, ro = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function Ht(e) {
  return e != null && e !== "parent";
}
function yt(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function pl(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function gl(e, t = { width: 0, height: 0 }, n, o, i) {
  const s = { ...e }, a = o.get(n);
  if (a) {
    const c = a.origin || i;
    s.x += a.internals.positionAbsolute.x - (t.width ?? 0) * c[0], s.y += a.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return s;
}
function oa(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function Mg() {
  let e, t;
  return { promise: new Promise((o, i) => {
    e = o, t = i;
  }), resolve: e, reject: t };
}
function Dg(e) {
  return { ...sl, ...e || {} };
}
function Un(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: i }) {
  const { x: s, y: a } = nt(e), c = mn({ x: s - (i?.left ?? 0), y: a - (i?.top ?? 0) }, o), { x: d, y: l } = n ? po(c, t) : c;
  return {
    xSnapped: d,
    ySnapped: l,
    ...c
  };
}
const ts = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), yl = (e) => e?.getRootNode?.() || window?.document, Tg = ["INPUT", "SELECT", "TEXTAREA"];
function ml(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : Tg.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const xl = (e) => "clientX" in e, nt = (e, t) => {
  const n = xl(e), o = n ? e.clientX : e.touches?.[0].clientX, i = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: i - (t?.top ?? 0)
  };
}, ia = (e, t, n, o, i) => {
  const s = t.querySelectorAll(`.${e}`);
  return !s || !s.length ? null : Array.from(s).map((a) => {
    const c = a.getBoundingClientRect();
    return {
      id: a.getAttribute("data-handleid"),
      type: e,
      nodeId: i,
      position: a.getAttribute("data-handlepos"),
      x: (c.left - n.left) / o,
      y: (c.top - n.top) / o,
      ...ts(a)
    };
  });
};
function wl({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: i, sourceControlY: s, targetControlX: a, targetControlY: c }) {
  const d = e * 0.125 + i * 0.375 + a * 0.375 + n * 0.125, l = t * 0.125 + s * 0.375 + c * 0.375 + o * 0.125, u = Math.abs(d - e), f = Math.abs(l - t);
  return [d, l, u, f];
}
function Ro(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function ra({ pos: e, x1: t, y1: n, x2: o, y2: i, c: s }) {
  switch (e) {
    case te.Left:
      return [t - Ro(t - o, s), n];
    case te.Right:
      return [t + Ro(o - t, s), n];
    case te.Top:
      return [t, n - Ro(n - i, s)];
    case te.Bottom:
      return [t, n + Ro(i - n, s)];
  }
}
function vl({ sourceX: e, sourceY: t, sourcePosition: n = te.Bottom, targetX: o, targetY: i, targetPosition: s = te.Top, curvature: a = 0.25 }) {
  const [c, d] = ra({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: i,
    c: a
  }), [l, u] = ra({
    pos: s,
    x1: o,
    y1: i,
    x2: e,
    y2: t,
    c: a
  }), [f, h, p, g] = wl({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: i,
    sourceControlX: c,
    sourceControlY: d,
    targetControlX: l,
    targetControlY: u
  });
  return [
    `M${e},${t} C${c},${d} ${l},${u} ${o},${i}`,
    f,
    h,
    p,
    g
  ];
}
function bl({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const i = Math.abs(n - e) / 2, s = n < e ? n + i : n - i, a = Math.abs(o - t) / 2, c = o < t ? o + a : o - a;
  return [s, c, i, a];
}
function $g({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: i = !1, zIndexMode: s = "basic" }) {
  if (s === "manual")
    return o;
  const a = i && n ? o + 1e3 : o, c = Math.max(e.parentId || i && e.selected ? e.internals.z : 0, t.parentId || i && t.selected ? t.internals.z : 0);
  return a + c;
}
function Pg({ sourceNode: e, targetNode: t, width: n, height: o, transform: i }) {
  const s = ki(si(e), si(t));
  s.x === s.x2 && (s.x2 += 1), s.y === s.y2 && (s.y2 += 1);
  const a = {
    x: -i[0] / i[2],
    y: -i[1] / i[2],
    width: n / i[2],
    height: o / i[2]
  };
  return io(a, Ei(s)) > 0;
}
const Nl = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, zg = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), Rg = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", qe.error006()), t;
  const o = n.getEdgeId || Nl;
  let i;
  return ll(e) ? i = { ...e } : i = {
    ...e,
    id: o(e)
  }, zg(i, t) ? t : (i.sourceHandle === null && delete i.sourceHandle, i.targetHandle === null && delete i.targetHandle, t.concat(i));
}, Lg = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: i, ...s } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", qe.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return o.onError?.("007", qe.error007(i)), n;
  const c = o.getEdgeId || Nl, d = {
    ...s,
    id: o.shouldReplaceId ? c(t) : i,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== i).concat(d);
};
function jl({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [i, s, a, c] = bl({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, i, s, a, c];
}
const sa = {
  [te.Left]: { x: -1, y: 0 },
  [te.Right]: { x: 1, y: 0 },
  [te.Top]: { x: 0, y: -1 },
  [te.Bottom]: { x: 0, y: 1 }
}, Vg = ({ source: e, sourcePosition: t = te.Bottom, target: n }) => t === te.Left || t === te.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, aa = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Hg({ source: e, sourcePosition: t = te.Bottom, target: n, targetPosition: o = te.Top, center: i, offset: s, stepPosition: a }) {
  const c = sa[t], d = sa[o], l = { x: e.x + c.x * s, y: e.y + c.y * s }, u = { x: n.x + d.x * s, y: n.y + d.y * s }, f = Vg({
    source: l,
    sourcePosition: t,
    target: u
  }), h = f.x !== 0 ? "x" : "y", p = f[h];
  let g = [], v, w;
  const m = { x: 0, y: 0 }, k = { x: 0, y: 0 }, [, , y, b] = bl({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[h] * d[h] === -1) {
    h === "x" ? (v = i.x ?? l.x + (u.x - l.x) * a, w = i.y ?? (l.y + u.y) / 2) : (v = i.x ?? (l.x + u.x) / 2, w = i.y ?? l.y + (u.y - l.y) * a);
    const _ = [
      { x: v, y: l.y },
      { x: v, y: u.y }
    ], D = [
      { x: l.x, y: w },
      { x: u.x, y: w }
    ];
    c[h] === p ? g = h === "x" ? _ : D : g = h === "x" ? D : _;
  } else {
    const _ = [{ x: l.x, y: u.y }], D = [{ x: u.x, y: l.y }];
    if (h === "x" ? g = c.x === p ? D : _ : g = c.y === p ? _ : D, t === o) {
      const N = Math.abs(e[h] - n[h]);
      if (N <= s) {
        const A = Math.min(s - 1, s - N);
        c[h] === p ? m[h] = (l[h] > e[h] ? -1 : 1) * A : k[h] = (u[h] > n[h] ? -1 : 1) * A;
      }
    }
    if (t !== o) {
      const N = h === "x" ? "y" : "x", A = c[h] === d[N], I = l[N] > u[N], $ = l[N] < u[N];
      (c[h] === 1 && (!A && I || A && $) || c[h] !== 1 && (!A && $ || A && I)) && (g = h === "x" ? _ : D);
    }
    const B = { x: l.x + m.x, y: l.y + m.y }, M = { x: u.x + k.x, y: u.y + k.y }, T = Math.max(Math.abs(B.x - g[0].x), Math.abs(M.x - g[0].x)), L = Math.max(Math.abs(B.y - g[0].y), Math.abs(M.y - g[0].y));
    T >= L ? (v = (B.x + M.x) / 2, w = g[0].y) : (v = g[0].x, w = (B.y + M.y) / 2);
  }
  const S = { x: l.x + m.x, y: l.y + m.y }, E = { x: u.x + k.x, y: u.y + k.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...S.x !== g[0].x || S.y !== g[0].y ? [S] : [],
    ...g,
    ...E.x !== g[g.length - 1].x || E.y !== g[g.length - 1].y ? [E] : [],
    n
  ], v, w, y, b];
}
function Og(e, t, n, o) {
  const i = Math.min(aa(e, t) / 2, aa(t, n) / 2, o), { x: s, y: a } = t;
  if (e.x === s && s === n.x || e.y === a && a === n.y)
    return `L${s} ${a}`;
  if (e.y === a) {
    const l = e.x < n.x ? -1 : 1, u = e.y < n.y ? 1 : -1;
    return `L ${s + i * l},${a}Q ${s},${a} ${s},${a + i * u}`;
  }
  const c = e.x < n.x ? 1 : -1, d = e.y < n.y ? -1 : 1;
  return `L ${s},${a + i * d}Q ${s},${a} ${s + i * c},${a}`;
}
function ai({ sourceX: e, sourceY: t, sourcePosition: n = te.Bottom, targetX: o, targetY: i, targetPosition: s = te.Top, borderRadius: a = 5, centerX: c, centerY: d, offset: l = 20, stepPosition: u = 0.5 }) {
  const [f, h, p, g, v] = Hg({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: i },
    targetPosition: s,
    center: { x: c, y: d },
    offset: l,
    stepPosition: u
  });
  let w = `M${f[0].x} ${f[0].y}`;
  for (let m = 1; m < f.length - 1; m++)
    w += Og(f[m - 1], f[m], f[m + 1], a);
  return w += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [w, h, p, g, v];
}
function ca(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function Fg(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!ca(t) || !ca(n))
    return null;
  const o = t.internals.handleBounds || la(t.handles), i = n.internals.handleBounds || la(n.handles), s = da(o?.source ?? [], e.sourceHandle), a = da(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === ln.Strict ? i?.target ?? [] : (i?.target ?? []).concat(i?.source ?? []),
    e.targetHandle
  );
  if (!s || !a)
    return e.onError?.("008", qe.error008(s ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = s?.position || te.Bottom, d = a?.position || te.Top, l = Ot(t, s, c), u = Ot(n, a, d);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: u.x,
    targetY: u.y,
    sourcePosition: c,
    targetPosition: d
  };
}
function la(e) {
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
function Ot(e, t, n = te.Left, o = !1) {
  const i = (t?.x ?? 0) + e.internals.positionAbsolute.x, s = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: a, height: c } = t ?? yt(e);
  if (o)
    return { x: i + a / 2, y: s + c / 2 };
  switch (t?.position ?? n) {
    case te.Top:
      return { x: i + a / 2, y: s };
    case te.Right:
      return { x: i + a, y: s + c / 2 };
    case te.Bottom:
      return { x: i + a / 2, y: s + c };
    case te.Left:
      return { x: i, y: s + c / 2 };
  }
}
function da(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function _r(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function Bg(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: i }) {
  const s = /* @__PURE__ */ new Set();
  return e.reduce((a, c) => ([c.markerStart || o, c.markerEnd || i].forEach((d) => {
    if (d && typeof d == "object") {
      const l = _r(d, t);
      s.has(l) || (a.push({ id: l, color: d.color || n, ...d }), s.add(l));
    }
  }), a), []).sort((a, c) => a.id.localeCompare(c.id));
}
const Sl = 1e3, Wg = 10, ns = {
  nodeOrigin: [0, 0],
  nodeExtent: no,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Kg = {
  ...ns,
  checkEquality: !0
};
function os(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function qg(e, t, n) {
  const o = os(ns, n);
  for (const i of e.values())
    if (i.parentId)
      rs(i, e, t, o);
    else {
      const s = fo(i, o.nodeOrigin), a = Ht(i.extent) ? i.extent : o.nodeExtent, c = Vt(s, a, yt(i));
      i.internals.positionAbsolute = c;
    }
}
function Xg(e, t) {
  if (!e.handles)
    return e.measured ? t?.internals.handleBounds : void 0;
  const n = [], o = [];
  for (const i of e.handles) {
    const s = {
      id: i.id,
      width: i.width ?? 1,
      height: i.height ?? 1,
      nodeId: e.id,
      x: i.x,
      y: i.y,
      position: i.position,
      type: i.type
    };
    i.type === "source" ? n.push(s) : i.type === "target" && o.push(s);
  }
  return {
    source: n,
    target: o
  };
}
function is(e) {
  return e === "manual";
}
function Mr(e, t, n, o = {}) {
  const i = os(Kg, o), s = { i: 0 }, a = new Map(t), c = i?.elevateNodesOnSelect && !is(i.zIndexMode) ? Sl : 0;
  let d = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const u of e) {
    let f = a.get(u.id);
    if (i.checkEquality && u === f?.internals.userNode)
      t.set(u.id, f);
    else {
      const h = fo(u, i.nodeOrigin), p = Ht(u.extent) ? u.extent : i.nodeExtent, g = Vt(h, p, yt(u));
      f = {
        ...i.defaults,
        ...u,
        measured: {
          width: u.measured?.width,
          height: u.measured?.height
        },
        internals: {
          positionAbsolute: g,
          // if user re-initializes the node or removes `measured` for whatever reason, we reset the handleBounds so that the node gets re-measured
          handleBounds: Xg(u, f),
          z: kl(u, c, i.zIndexMode),
          userNode: u
        }
      }, t.set(u.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (d = !1), u.parentId && rs(f, t, n, o, s), l ||= u.selected ?? !1;
  }
  return { nodesInitialized: d, hasSelectedNodes: l };
}
function Yg(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function rs(e, t, n, o, i) {
  const { elevateNodesOnSelect: s, nodeOrigin: a, nodeExtent: c, zIndexMode: d } = os(ns, o), l = e.parentId, u = t.get(l);
  if (!u) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Yg(e, n), i && !u.parentId && u.internals.rootParentIndex === void 0 && d === "auto" && (u.internals.rootParentIndex = ++i.i, u.internals.z = u.internals.z + i.i * Wg), i && u.internals.rootParentIndex !== void 0 && (i.i = u.internals.rootParentIndex);
  const f = s && !is(d) ? Sl : 0, { x: h, y: p, z: g } = Ug(e, u, a, c, f, d), { positionAbsolute: v } = e.internals, w = h !== v.x || p !== v.y;
  (w || g !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: w ? { x: h, y: p } : v,
      z: g
    }
  });
}
function kl(e, t, n) {
  const o = tt(e.zIndex) ? e.zIndex : 0;
  return is(n) ? o : o + (e.selected ? t : 0);
}
function Ug(e, t, n, o, i, s) {
  const { x: a, y: c } = t.internals.positionAbsolute, d = yt(e), l = fo(e, n), u = Ht(e.extent) ? Vt(l, e.extent, d) : l;
  let f = Vt({ x: a + u.x, y: c + u.y }, o, d);
  e.extent === "parent" && (f = ul(f, d, t));
  const h = kl(e, i, s), p = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: p >= h ? p + 1 : h
  };
}
function ss(e, t, n, o = [0, 0]) {
  const i = [], s = /* @__PURE__ */ new Map();
  for (const a of e) {
    const c = t.get(a.parentId);
    if (!c)
      continue;
    const d = s.get(a.parentId)?.expandedRect ?? un(c), l = fl(d, a.rect);
    s.set(a.parentId, { expandedRect: l, parent: c });
  }
  return s.size > 0 && s.forEach(({ expandedRect: a, parent: c }, d) => {
    const l = c.internals.positionAbsolute, u = yt(c), f = c.origin ?? o, h = a.x < l.x ? Math.round(Math.abs(l.x - a.x)) : 0, p = a.y < l.y ? Math.round(Math.abs(l.y - a.y)) : 0, g = Math.max(u.width, Math.round(a.width)), v = Math.max(u.height, Math.round(a.height)), w = (g - u.width) * f[0], m = (v - u.height) * f[1];
    (h > 0 || p > 0 || w || m) && (i.push({
      id: d,
      type: "position",
      position: {
        x: c.position.x - h + w,
        y: c.position.y - p + m
      }
    }), n.get(d)?.forEach((k) => {
      e.some((y) => y.id === k.id) || i.push({
        id: k.id,
        type: "position",
        position: {
          x: k.position.x + h,
          y: k.position.y + p
        }
      });
    })), (u.width < a.width || u.height < a.height || h || p) && i.push({
      id: d,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: g + (h ? f[0] * h - w : 0),
        height: v + (p ? f[1] * p - m : 0)
      }
    });
  }), i;
}
function Zg(e, t, n, o, i, s, a) {
  const c = o?.querySelector(".xyflow__viewport");
  let d = !1;
  if (!c)
    return { changes: [], updatedInternals: d };
  const l = [], u = window.getComputedStyle(c), { m22: f } = new window.DOMMatrixReadOnly(u.transform), h = [];
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
      }), d = !0;
      continue;
    }
    const v = ts(p.nodeElement), w = g.measured.width !== v.width || g.measured.height !== v.height;
    if (!!(v.width && v.height && (w || !g.internals.handleBounds || p.force))) {
      const k = p.nodeElement.getBoundingClientRect(), y = Ht(g.extent) ? g.extent : s;
      let { positionAbsolute: b } = g.internals;
      g.parentId && g.extent === "parent" ? b = ul(b, v, t.get(g.parentId)) : y && (b = Vt(b, y, v));
      const S = {
        ...g,
        measured: v,
        internals: {
          ...g.internals,
          positionAbsolute: b,
          handleBounds: {
            source: ia("source", p.nodeElement, k, f, g.id),
            target: ia("target", p.nodeElement, k, f, g.id)
          }
        }
      };
      t.set(g.id, S), g.parentId && rs(S, t, n, { nodeOrigin: i, zIndexMode: a }), d = !0, w && (l.push({
        id: g.id,
        type: "dimensions",
        dimensions: v
      }), g.expandParent && g.parentId && h.push({
        id: g.id,
        parentId: g.parentId,
        rect: un(S, i)
      }));
    }
  }
  if (h.length > 0) {
    const p = ss(h, t, n, i);
    l.push(...p);
  }
  return { changes: l, updatedInternals: d };
}
async function Gg({ delta: e, panZoom: t, transform: n, translateExtent: o, width: i, height: s }) {
  if (!t || !e.x && !e.y)
    return !1;
  const a = await t.setViewportConstrained({
    x: n[0] + e.x,
    y: n[1] + e.y,
    zoom: n[2]
  }, [
    [0, 0],
    [i, s]
  ], o);
  return !!a && (a.x !== n[0] || a.y !== n[1] || a.k !== n[2]);
}
function ua(e, t, n, o, i, s) {
  let a = i;
  const c = o.get(a) || /* @__PURE__ */ new Map();
  o.set(a, c.set(n, t)), a = `${i}-${e}`;
  const d = o.get(a) || /* @__PURE__ */ new Map();
  if (o.set(a, d.set(n, t)), s) {
    a = `${i}-${e}-${s}`;
    const l = o.get(a) || /* @__PURE__ */ new Map();
    o.set(a, l.set(n, t));
  }
}
function El(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: i, target: s, sourceHandle: a = null, targetHandle: c = null } = o, d = { edgeId: o.id, source: i, target: s, sourceHandle: a, targetHandle: c }, l = `${i}-${a}--${s}-${c}`, u = `${s}-${c}--${i}-${a}`;
    ua("source", d, u, e, i, a), ua("target", d, l, e, s, c), t.set(o.id, o);
  }
}
function Cl(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : Cl(n, t) : !1;
}
function fa(e, t, n) {
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
function Jg(e, t, n, o) {
  const i = /* @__PURE__ */ new Map();
  for (const [s, a] of e)
    if ((a.selected || a.id === o) && (!a.parentId || !Cl(a, e)) && (a.draggable || t && typeof a.draggable > "u")) {
      const c = e.get(s);
      c && i.set(s, {
        id: s,
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
  return i;
}
function rr({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
  const i = [];
  for (const [a, c] of t) {
    const d = n.get(a)?.internals.userNode;
    d && i.push({
      ...d,
      position: c.position,
      dragging: o
    });
  }
  if (!e)
    return [i[0], i];
  const s = n.get(e)?.internals.userNode;
  return [
    s ? {
      ...s,
      position: t.get(e)?.position || s.position,
      dragging: o
    } : i[0],
    i
  ];
}
function Qg({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const i = e.values().next().value;
  if (!i)
    return null;
  const s = {
    x: n - i.distance.x,
    y: o - i.distance.y
  }, a = po(s, t);
  return {
    x: a.x - s.x,
    y: a.y - s.y
  };
}
function ey({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: i }) {
  let s = { x: null, y: null }, a = 0, c = /* @__PURE__ */ new Map(), d = !1, l = { x: 0, y: 0 }, u = null, f = !1, h = null, p = !1, g = !1, v = null;
  function w({ noDragClassName: k, handleSelector: y, domNode: b, isSelectable: S, nodeId: E, nodeClickDistance: j = 0 }) {
    h = We(b);
    function _({ x: T, y: L }) {
      const { nodeLookup: N, nodeExtent: A, snapGrid: I, snapToGrid: $, nodeOrigin: z, onNodeDrag: P, onSelectionDrag: F, onError: K, updateNodePositions: H } = t();
      s = { x: T, y: L };
      let Z = !1;
      const X = c.size > 1, ne = X && A ? Ar(ho(c)) : null, se = X && $ ? Qg({
        dragItems: c,
        snapGrid: I,
        x: T,
        y: L
      }) : null;
      for (const [G, V] of c) {
        if (!N.has(G))
          continue;
        let Y = { x: T - V.distance.x, y: L - V.distance.y };
        $ && (Y = se ? {
          x: Math.round(Y.x + se.x),
          y: Math.round(Y.y + se.y)
        } : po(Y, I));
        let ae = null;
        if (X && A && !V.extent && ne) {
          const { positionAbsolute: ee } = V.internals, de = ee.x - ne.x + A[0][0], O = ee.x + V.measured.width - ne.x2 + A[1][0], Q = ee.y - ne.y + A[0][1], ge = ee.y + V.measured.height - ne.y2 + A[1][1];
          ae = [
            [de, Q],
            [O, ge]
          ];
        }
        const { position: ce, positionAbsolute: J } = dl({
          nodeId: G,
          nextPosition: Y,
          nodeLookup: N,
          nodeExtent: ae || A,
          nodeOrigin: z,
          onError: K
        });
        Z = Z || V.position.x !== ce.x || V.position.y !== ce.y, V.position = ce, V.internals.positionAbsolute = J;
      }
      if (g = g || Z, !!Z && (H(c, !0), v && (o || P || !E && F))) {
        const [G, V] = rr({
          nodeId: E,
          dragItems: c,
          nodeLookup: N
        });
        o?.(v, c, G, V), P?.(v, G, V), E || F?.(v, V);
      }
    }
    async function D() {
      if (!u)
        return;
      const { transform: T, panBy: L, autoPanSpeed: N, autoPanOnNodeDrag: A } = t();
      if (!A) {
        d = !1, cancelAnimationFrame(a);
        return;
      }
      const [I, $] = Qr(l, u, N);
      (I !== 0 || $ !== 0) && (s.x = (s.x ?? 0) - I / T[2], s.y = (s.y ?? 0) - $ / T[2], await L({ x: I, y: $ }) && _(s)), a = requestAnimationFrame(D);
    }
    function B(T) {
      const { nodeLookup: L, multiSelectionActive: N, nodesDraggable: A, transform: I, snapGrid: $, snapToGrid: z, selectNodesOnDrag: P, onNodeDragStart: F, onSelectionDragStart: K, unselectNodesAndEdges: H } = t();
      f = !0, (!P || !S) && !N && E && (L.get(E)?.selected || H()), S && P && E && e?.(E);
      const Z = Un(T.sourceEvent, { transform: I, snapGrid: $, snapToGrid: z, containerBounds: u });
      if (s = Z, c = Jg(L, A, Z, E), c.size > 0 && (n || F || !E && K)) {
        const [X, ne] = rr({
          nodeId: E,
          dragItems: c,
          nodeLookup: L
        });
        n?.(T.sourceEvent, c, X, ne), F?.(T.sourceEvent, X, ne), E || K?.(T.sourceEvent, ne);
      }
    }
    const M = Bc().clickDistance(j).on("start", (T) => {
      const { domNode: L, nodeDragThreshold: N, transform: A, snapGrid: I, snapToGrid: $ } = t();
      u = L?.getBoundingClientRect() || null, p = !1, g = !1, v = T.sourceEvent, N === 0 && B(T), s = Un(T.sourceEvent, { transform: A, snapGrid: I, snapToGrid: $, containerBounds: u }), l = nt(T.sourceEvent, u);
    }).on("drag", (T) => {
      const { autoPanOnNodeDrag: L, transform: N, snapGrid: A, snapToGrid: I, nodeDragThreshold: $, nodeLookup: z } = t(), P = Un(T.sourceEvent, { transform: N, snapGrid: A, snapToGrid: I, containerBounds: u });
      if (v = T.sourceEvent, (T.sourceEvent.type === "touchmove" && T.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      E && !z.has(E)) && (p = !0), !p) {
        if (!d && L && f && (d = !0, D()), !f) {
          const F = nt(T.sourceEvent, u), K = F.x - l.x, H = F.y - l.y;
          Math.sqrt(K * K + H * H) > $ && B(T);
        }
        (s.x !== P.xSnapped || s.y !== P.ySnapped) && c && f && (l = nt(T.sourceEvent, u), _(P));
      }
    }).on("end", (T) => {
      if (!f || p) {
        p && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (d = !1, f = !1, cancelAnimationFrame(a), c.size > 0) {
        const { nodeLookup: L, updateNodePositions: N, onNodeDragStop: A, onSelectionDragStop: I } = t();
        if (g && (N(c, !1), g = !1), i || A || !E && I) {
          const [$, z] = rr({
            nodeId: E,
            dragItems: c,
            nodeLookup: L,
            dragging: !1
          });
          i?.(T.sourceEvent, c, $, z), A?.(T.sourceEvent, $, z), E || I?.(T.sourceEvent, z);
        }
      }
    }).filter((T) => {
      const L = T.target;
      return !T.button && (!k || !fa(L, `.${k}`, b)) && (!y || fa(L, y, b));
    });
    h.call(M);
  }
  function m() {
    h?.on(".drag", null);
  }
  return {
    update: w,
    destroy: m
  };
}
function ty(e, t, n) {
  const o = [], i = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const s of t.values())
    io(i, un(s)) > 0 && o.push(s);
  return o;
}
const ny = 250;
function oy(e, t, n, o) {
  let i = [], s = 1 / 0;
  const a = ty(e, n, t + ny);
  for (const c of a) {
    const d = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of d) {
      if (o.nodeId === l.nodeId && o.type === l.type && o.id === l.id)
        continue;
      const { x: u, y: f } = Ot(c, l, l.position, !0), h = Math.sqrt(Math.pow(u - e.x, 2) + Math.pow(f - e.y, 2));
      h > t || (h < s ? (i = [{ ...l, x: u, y: f }], s = h) : h === s && i.push({ ...l, x: u, y: f }));
    }
  }
  if (!i.length)
    return null;
  if (i.length > 1) {
    const c = o.type === "source" ? "target" : "source";
    return i.find((d) => d.type === c) ?? i[0];
  }
  return i[0];
}
function Il(e, t, n, o, i, s = !1) {
  const a = o.get(e);
  if (!a)
    return null;
  const c = i === "strict" ? a.internals.handleBounds?.[t] : [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []], d = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return d && s ? { ...d, ...Ot(a, d, d.position, !0) } : d;
}
function Al(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function iy(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const _l = () => !0;
function ry(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: i, edgeUpdaterType: s, isTarget: a, domNode: c, nodeLookup: d, lib: l, autoPanOnConnect: u, flowId: f, panBy: h, cancelConnection: p, onConnectStart: g, onConnect: v, onConnectEnd: w, isValidConnection: m = _l, onReconnectEnd: k, updateConnection: y, getTransform: b, getFromHandle: S, autoPanSpeed: E, dragThreshold: j = 1, handleDomNode: _ }) {
  const D = yl(e.target);
  let B = 0, M;
  const { x: T, y: L } = nt(e), N = Al(s, _), A = c?.getBoundingClientRect();
  let I = !1;
  if (!A || !N)
    return;
  const $ = Il(i, N, o, d, t);
  if (!$)
    return;
  let z = nt(e, A), P = !1, F = null, K = !1, H = null;
  function Z() {
    if (!u || !A)
      return;
    const [ce, J] = Qr(z, A, E);
    h({ x: ce, y: J }), B = requestAnimationFrame(Z);
  }
  const X = {
    ...$,
    nodeId: i,
    type: N,
    position: $.position
  }, ne = d.get(i);
  let G = {
    inProgress: !0,
    isValid: null,
    from: Ot(ne, X, te.Left, !0),
    fromHandle: X,
    fromPosition: X.position,
    fromNode: ne,
    to: z,
    toHandle: null,
    toPosition: ea[X.position],
    toNode: null,
    pointer: z
  };
  function V() {
    I = !0, y(G), g?.(e, { nodeId: i, handleId: o, handleType: N });
  }
  j === 0 && V();
  function Y(ce) {
    if (!I) {
      const { x: ge, y: ve } = nt(ce), Ee = ge - T, $e = ve - L;
      if (!(Ee * Ee + $e * $e > j * j))
        return;
      V();
    }
    if (!S() || !X) {
      ae(ce);
      return;
    }
    const J = b();
    z = nt(ce, A), M = oy(mn(z, J, !1, [1, 1]), n, d, X), P || (Z(), P = !0);
    const ee = Ml(ce, {
      handle: M,
      connectionMode: t,
      fromNodeId: i,
      fromHandleId: o,
      fromType: a ? "target" : "source",
      isValidConnection: m,
      doc: D,
      lib: l,
      flowId: f,
      nodeLookup: d
    });
    H = ee.handleDomNode, F = ee.connection, K = iy(!!M, ee.isValid);
    const de = d.get(i), O = de ? Ot(de, X, te.Left, !0) : G.from, Q = {
      ...G,
      from: O,
      isValid: K,
      to: ee.toHandle && K ? fn({ x: ee.toHandle.x, y: ee.toHandle.y }, J) : z,
      toHandle: ee.toHandle,
      toPosition: K && ee.toHandle ? ee.toHandle.position : ea[X.position],
      toNode: ee.toHandle ? d.get(ee.toHandle.nodeId) : null,
      pointer: z
    };
    y(Q), G = Q;
  }
  function ae(ce) {
    if (!("touches" in ce && ce.touches.length > 0)) {
      if (I) {
        (M || H) && F && K && v?.(F);
        const { inProgress: J, ...ee } = G, de = {
          ...ee,
          toPosition: G.toHandle ? G.toPosition : null
        };
        w?.(ce, de), s && k?.(ce, de);
      }
      p(), cancelAnimationFrame(B), P = !1, K = !1, F = null, H = null, D.removeEventListener("mousemove", Y), D.removeEventListener("mouseup", ae), D.removeEventListener("touchmove", Y), D.removeEventListener("touchend", ae);
    }
  }
  D.addEventListener("mousemove", Y), D.addEventListener("mouseup", ae), D.addEventListener("touchmove", Y), D.addEventListener("touchend", ae);
}
function Ml(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: i, fromType: s, doc: a, lib: c, flowId: d, isValidConnection: l = _l, nodeLookup: u }) {
  const f = s === "target", h = t ? a.querySelector(`.${c}-flow__handle[data-id="${d}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: p, y: g } = nt(e), v = a.elementFromPoint(p, g), w = v?.classList.contains(`${c}-flow__handle`) ? v : h, m = {
    handleDomNode: w,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (w) {
    const k = Al(void 0, w), y = w.getAttribute("data-nodeid"), b = w.getAttribute("data-handleid"), S = w.classList.contains("connectable"), E = w.classList.contains("connectableend");
    if (!y || !k)
      return m;
    const j = {
      source: f ? y : o,
      sourceHandle: f ? b : i,
      target: f ? o : y,
      targetHandle: f ? i : b
    };
    m.connection = j;
    const D = S && E && (n === ln.Strict ? f && k === "source" || !f && k === "target" : y !== o || b !== i);
    m.isValid = D && l(j), m.toHandle = Il(y, k, b, u, n, !0);
  }
  return m;
}
const Dr = {
  onPointerDown: ry,
  isValid: Ml
};
function sy({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const i = We(e);
  function s({ translateExtent: c, width: d, height: l, zoomStep: u = 1, pannable: f = !0, zoomable: h = !0, inversePan: p = !1 }) {
    const g = (y) => {
      if (y.sourceEvent.type !== "wheel" || !t)
        return;
      const b = n(), S = y.sourceEvent.ctrlKey && ro() ? 10 : 1, E = -y.sourceEvent.deltaY * (y.sourceEvent.deltaMode === 1 ? 0.05 : y.sourceEvent.deltaMode ? 1 : 2e-3) * u, j = b[2] * Math.pow(2, E * S);
      t.scaleTo(j);
    };
    let v = [0, 0];
    const w = (y) => {
      (y.sourceEvent.type === "mousedown" || y.sourceEvent.type === "touchstart") && (v = [
        y.sourceEvent.clientX ?? y.sourceEvent.touches[0].clientX,
        y.sourceEvent.clientY ?? y.sourceEvent.touches[0].clientY
      ]);
    }, m = (y) => {
      const b = n();
      if (y.sourceEvent.type !== "mousemove" && y.sourceEvent.type !== "touchmove" || !t)
        return;
      const S = [
        y.sourceEvent.clientX ?? y.sourceEvent.touches[0].clientX,
        y.sourceEvent.clientY ?? y.sourceEvent.touches[0].clientY
      ], E = [S[0] - v[0], S[1] - v[1]];
      v = S;
      const j = o() * Math.max(b[2], Math.log(b[2])) * (p ? -1 : 1), _ = {
        x: b[0] - E[0] * j,
        y: b[1] - E[1] * j
      }, D = [
        [0, 0],
        [d, l]
      ];
      t.setViewportConstrained({
        x: _.x,
        y: _.y,
        zoom: b[2]
      }, D, c);
    }, k = il().on("start", w).on("zoom", f ? m : null).on("zoom.wheel", h ? g : null);
    i.call(k, {});
  }
  function a() {
    i.on("zoom", null);
  }
  return {
    update: s,
    destroy: a,
    pointer: Qe
  };
}
const Ci = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), sr = ({ x: e, y: t, zoom: n }) => Si.translate(e, t).scale(n), tn = (e, t) => e.target.closest(`.${t}`), Dl = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), ay = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, ar = (e, t = 0, n = ay, o = () => {
}) => {
  const i = typeof t == "number" && t > 0;
  return i || o(), i ? e.transition().duration(t).ease(n).on("end", o) : e;
}, Tl = (e) => {
  const t = e.ctrlKey && ro() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function cy({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: i, panOnScrollSpeed: s, zoomOnPinch: a, onPanZoomStart: c, onPanZoom: d, onPanZoomEnd: l }) {
  return (u) => {
    if (tn(u, t))
      return u.ctrlKey && u.preventDefault(), !1;
    u.preventDefault(), u.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (u.ctrlKey && a) {
      const w = Qe(u), m = Tl(u), k = f * Math.pow(2, m);
      o.scaleTo(n, k, w, u);
      return;
    }
    const h = u.deltaMode === 1 ? 20 : 1;
    let p = i === $t.Vertical ? 0 : u.deltaX * h, g = i === $t.Horizontal ? 0 : u.deltaY * h;
    !ro() && u.shiftKey && i !== $t.Vertical && (p = u.deltaY * h, g = 0), o.translateBy(
      n,
      -(p / f) * s,
      -(g / f) * s,
      // @ts-ignore
      { internal: !0 }
    );
    const v = Ci(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (d?.(u, v), e.panScrollTimeout = setTimeout(() => {
      l?.(u, v), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(u, v));
  };
}
function ly({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, i) {
    const s = o.type === "wheel", a = !t && s && !o.ctrlKey, c = tn(o, e);
    if (o.ctrlKey && s && c && o.preventDefault(), a || c)
      return null;
    o.preventDefault(), n.call(this, o, i);
  };
}
function dy({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const i = Ci(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = i, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, i);
  };
}
function uy({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: i }) {
  return (s) => {
    e.usedRightMouseButton = !!(n && Dl(t, e.mouseButton ?? 0)), s.sourceEvent?.sync || o([s.transform.x, s.transform.y, s.transform.k]), i && !s.sourceEvent?.internal && i?.(s.sourceEvent, Ci(s.transform));
  };
}
function fy({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: i, onPaneContextMenu: s }) {
  return (a) => {
    if (!a.sourceEvent?.internal && (e.isZoomingOrPanning = !1, s && Dl(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && a.sourceEvent && s(a.sourceEvent), e.usedRightMouseButton = !1, o(!1), i)) {
      const c = Ci(a.transform);
      e.prevViewport = c, clearTimeout(e.timerId), e.timerId = setTimeout(
        () => {
          i?.(a.sourceEvent, c);
        },
        // we need a setTimeout for panOnScroll to suppress multiple end events fired during scroll
        n ? 150 : 0
      );
    }
  };
}
function hy({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: i, zoomOnDoubleClick: s, userSelectionActive: a, noWheelClassName: c, noPanClassName: d, lib: l, connectionInProgress: u }) {
  return (f) => {
    const h = e || t, p = n && f.ctrlKey, g = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (tn(f, `${l}-flow__node`) || tn(f, `${l}-flow__edge`)))
      return !0;
    if (!o && !h && !i && !s && !n || a || u && !g || tn(f, c) && g || tn(f, d) && (!g || i && g && !e) || !n && f.ctrlKey && g)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!h && !i && !p && g || !o && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(o) && !o.includes(f.button) && f.type === "mousedown")
      return !1;
    const v = Array.isArray(o) && o.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || g) && v;
  };
}
function py({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: i, onPanZoom: s, onPanZoomStart: a, onPanZoomEnd: c, onDraggingChange: d }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, u = e.getBoundingClientRect(), f = il().scaleExtent([t, n]).translateExtent(o), h = We(e).call(f);
  k({
    x: i.x,
    y: i.y,
    zoom: dn(i.zoom, t, n)
  }, [
    [0, 0],
    [u.width, u.height]
  ], o);
  const p = h.on("wheel.zoom"), g = h.on("dblclick.zoom");
  f.wheelDelta(Tl);
  async function v(M, T) {
    return h ? new Promise((L) => {
      f?.interpolate(T?.interpolate === "linear" ? Yn : Wo).transform(ar(h, T?.duration, T?.ease, () => L(!0)), M);
    }) : !1;
  }
  function w({ noWheelClassName: M, noPanClassName: T, onPaneContextMenu: L, userSelectionActive: N, panOnScroll: A, panOnDrag: I, panOnScrollMode: $, panOnScrollSpeed: z, preventScrolling: P, zoomOnPinch: F, zoomOnScroll: K, zoomOnDoubleClick: H, zoomActivationKeyPressed: Z, lib: X, onTransformChange: ne, connectionInProgress: se, paneClickDistance: G, selectionOnDrag: V }) {
    N && !l.isZoomingOrPanning && m();
    const Y = A && !Z && !N;
    f.clickDistance(V ? 1 / 0 : !tt(G) || G < 0 ? 0 : G);
    const ae = Y ? cy({
      zoomPanValues: l,
      noWheelClassName: M,
      d3Selection: h,
      d3Zoom: f,
      panOnScrollMode: $,
      panOnScrollSpeed: z,
      zoomOnPinch: F,
      onPanZoomStart: a,
      onPanZoom: s,
      onPanZoomEnd: c
    }) : ly({
      noWheelClassName: M,
      preventScrolling: P,
      d3ZoomHandler: p
    });
    h.on("wheel.zoom", ae, { passive: !1 });
    const ce = dy({
      zoomPanValues: l,
      onDraggingChange: d,
      onPanZoomStart: a
    });
    f.on("start", ce);
    const J = uy({
      zoomPanValues: l,
      panOnDrag: I,
      onPaneContextMenu: !!L,
      onPanZoom: s,
      onTransformChange: ne
    });
    f.on("zoom", J);
    const ee = fy({
      zoomPanValues: l,
      panOnDrag: I,
      panOnScroll: A,
      onPaneContextMenu: L,
      onPanZoomEnd: c,
      onDraggingChange: d
    });
    f.on("end", ee);
    const de = hy({
      zoomActivationKeyPressed: Z,
      panOnDrag: I,
      zoomOnScroll: K,
      panOnScroll: A,
      zoomOnDoubleClick: H,
      zoomOnPinch: F,
      userSelectionActive: N,
      noPanClassName: T,
      noWheelClassName: M,
      lib: X,
      connectionInProgress: se
    });
    f.filter(de), H ? h.on("dblclick.zoom", g) : h.on("dblclick.zoom", null);
  }
  function m() {
    f.on("zoom", null);
  }
  async function k(M, T, L) {
    const N = sr(M), A = f?.constrain()(N, T, L);
    return A && await v(A), A;
  }
  async function y(M, T) {
    const L = sr(M);
    return await v(L, T), L;
  }
  function b(M) {
    if (h) {
      const T = sr(M), L = h.property("__zoom");
      (L.k !== M.zoom || L.x !== M.x || L.y !== M.y) && f?.transform(h, T, null, { sync: !0 });
    }
  }
  function S() {
    const M = h ? ol(h.node()) : { x: 0, y: 0, k: 1 };
    return { x: M.x, y: M.y, zoom: M.k };
  }
  async function E(M, T) {
    return h ? new Promise((L) => {
      f?.interpolate(T?.interpolate === "linear" ? Yn : Wo).scaleTo(ar(h, T?.duration, T?.ease, () => L(!0)), M);
    }) : !1;
  }
  async function j(M, T) {
    return h ? new Promise((L) => {
      f?.interpolate(T?.interpolate === "linear" ? Yn : Wo).scaleBy(ar(h, T?.duration, T?.ease, () => L(!0)), M);
    }) : !1;
  }
  function _(M) {
    f?.scaleExtent(M);
  }
  function D(M) {
    f?.translateExtent(M);
  }
  function B(M) {
    const T = !tt(M) || M < 0 ? 0 : M;
    f?.clickDistance(T);
  }
  return {
    update: w,
    destroy: m,
    setViewport: y,
    setViewportConstrained: k,
    getViewport: S,
    scaleTo: E,
    scaleBy: j,
    setScaleExtent: _,
    setTranslateExtent: D,
    syncViewport: b,
    setClickDistance: B
  };
}
var hn;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(hn || (hn = {}));
function gy({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: i, affectsY: s }) {
  const a = e - t, c = n - o, d = [a > 0 ? 1 : a < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return a && i && (d[0] = d[0] * -1), c && s && (d[1] = d[1] * -1), d;
}
function ha(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), i = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: i
  };
}
function wt(e, t) {
  return Math.max(0, t - e);
}
function vt(e, t) {
  return Math.max(0, e - t);
}
function Lo(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function pa(e, t) {
  return e ? !t : t;
}
function yy(e, t, n, o, i, s, a, c) {
  let { affectsX: d, affectsY: l } = t;
  const { isHorizontal: u, isVertical: f } = t, h = u && f, { xSnapped: p, ySnapped: g } = n, { minWidth: v, maxWidth: w, minHeight: m, maxHeight: k } = o, { x: y, y: b, width: S, height: E, aspectRatio: j } = e;
  let _ = Math.floor(u ? p - e.pointerX : 0), D = Math.floor(f ? g - e.pointerY : 0);
  const B = S + (d ? -_ : _), M = E + (l ? -D : D), T = -s[0] * S, L = -s[1] * E;
  let N = Lo(B, v, w), A = Lo(M, m, k);
  if (a) {
    let z = 0, P = 0;
    d && _ < 0 ? z = wt(y + _ + T, a[0][0]) : !d && _ > 0 && (z = vt(y + B + T, a[1][0])), l && D < 0 ? P = wt(b + D + L, a[0][1]) : !l && D > 0 && (P = vt(b + M + L, a[1][1])), N = Math.max(N, z), A = Math.max(A, P);
  }
  if (c) {
    let z = 0, P = 0;
    d && _ > 0 ? z = vt(y + _, c[0][0]) : !d && _ < 0 && (z = wt(y + B, c[1][0])), l && D > 0 ? P = vt(b + D, c[0][1]) : !l && D < 0 && (P = wt(b + M, c[1][1])), N = Math.max(N, z), A = Math.max(A, P);
  }
  if (i) {
    if (u) {
      const z = Lo(B / j, m, k) * j;
      if (N = Math.max(N, z), a) {
        let P = 0;
        !d && !l || d && !l && h ? P = vt(b + L + B / j, a[1][1]) * j : P = wt(b + L + (d ? _ : -_) / j, a[0][1]) * j, N = Math.max(N, P);
      }
      if (c) {
        let P = 0;
        !d && !l || d && !l && h ? P = wt(b + B / j, c[1][1]) * j : P = vt(b + (d ? _ : -_) / j, c[0][1]) * j, N = Math.max(N, P);
      }
    }
    if (f) {
      const z = Lo(M * j, v, w) / j;
      if (A = Math.max(A, z), a) {
        let P = 0;
        !d && !l || l && !d && h ? P = vt(y + M * j + T, a[1][0]) / j : P = wt(y + (l ? D : -D) * j + T, a[0][0]) / j, A = Math.max(A, P);
      }
      if (c) {
        let P = 0;
        !d && !l || l && !d && h ? P = wt(y + M * j, c[1][0]) / j : P = vt(y + (l ? D : -D) * j, c[0][0]) / j, A = Math.max(A, P);
      }
    }
  }
  D = D + (D < 0 ? A : -A), _ = _ + (_ < 0 ? N : -N), i && (h ? B > M * j ? D = (pa(d, l) ? -_ : _) / j : _ = (pa(d, l) ? -D : D) * j : u ? (D = _ / j, l = d) : (_ = D * j, d = l));
  const I = d ? y + _ : y, $ = l ? b + D : b;
  return {
    width: S + (d ? -_ : _),
    height: E + (l ? -D : D),
    x: s[0] * _ * (d ? -1 : 1) + I,
    y: s[1] * D * (l ? -1 : 1) + $
  };
}
const $l = { width: 0, height: 0, x: 0, y: 0 }, my = {
  ...$l,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function xy(e, t, n) {
  const o = t.position.x + e.position.x, i = t.position.y + e.position.y, s = e.measured.width ?? 0, a = e.measured.height ?? 0, c = n[0] * s, d = n[1] * a;
  return [
    [o - c, i - d],
    [o + s - c, i + a - d]
  ];
}
function wy({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: i }) {
  const s = We(e);
  let a = {
    controlDirection: ha("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: l, boundaries: u, keepAspectRatio: f, resizeDirection: h, onResizeStart: p, onResize: g, onResizeEnd: v, shouldResize: w }) {
    let m = { ...$l }, k = { ...my };
    a = {
      boundaries: u,
      resizeDirection: h,
      keepAspectRatio: f,
      controlDirection: ha(l)
    };
    let y, b = null, S = [], E, j, _, D = !1;
    const B = Bc().on("start", (M) => {
      const { nodeLookup: T, transform: L, snapGrid: N, snapToGrid: A, nodeOrigin: I, paneDomNode: $ } = n();
      if (y = T.get(t), !y)
        return;
      b = $?.getBoundingClientRect() ?? null;
      const { xSnapped: z, ySnapped: P } = Un(M.sourceEvent, {
        transform: L,
        snapGrid: N,
        snapToGrid: A,
        containerBounds: b
      });
      m = {
        width: y.measured.width ?? 0,
        height: y.measured.height ?? 0,
        x: y.position.x ?? 0,
        y: y.position.y ?? 0
      }, k = {
        ...m,
        pointerX: z,
        pointerY: P,
        aspectRatio: m.width / m.height
      }, E = void 0, j = Ht(y.extent) ? y.extent : void 0, y.parentId && (y.extent === "parent" || y.expandParent) && (E = T.get(y.parentId)), E && y.extent === "parent" && (j = [
        [0, 0],
        [E.measured.width, E.measured.height]
      ]), S = [], _ = void 0;
      for (const [F, K] of T)
        if (K.parentId === t && (S.push({
          id: F,
          position: { ...K.position },
          extent: K.extent
        }), K.extent === "parent" || K.expandParent)) {
          const H = xy(K, y, K.origin ?? I);
          _ ? _ = [
            [Math.min(H[0][0], _[0][0]), Math.min(H[0][1], _[0][1])],
            [Math.max(H[1][0], _[1][0]), Math.max(H[1][1], _[1][1])]
          ] : _ = H;
        }
      p?.(M, { ...m });
    }).on("drag", (M) => {
      const { transform: T, snapGrid: L, snapToGrid: N, nodeOrigin: A } = n(), I = Un(M.sourceEvent, {
        transform: T,
        snapGrid: L,
        snapToGrid: N,
        containerBounds: b
      }), $ = [];
      if (!y)
        return;
      const { x: z, y: P, width: F, height: K } = m, H = {}, Z = y.origin ?? A, { width: X, height: ne, x: se, y: G } = yy(k, a.controlDirection, I, a.boundaries, a.keepAspectRatio, Z, j, _), V = X !== F, Y = ne !== K, ae = se !== z && V, ce = G !== P && Y;
      if (!ae && !ce && !V && !Y)
        return;
      if ((ae || ce || Z[0] === 1 || Z[1] === 1) && (H.x = ae ? se : m.x, H.y = ce ? G : m.y, m.x = H.x, m.y = H.y, S.length > 0)) {
        const O = se - z, Q = G - P;
        for (const ge of S)
          ge.position = {
            x: ge.position.x - O + Z[0] * (X - F),
            y: ge.position.y - Q + Z[1] * (ne - K)
          }, $.push(ge);
      }
      if ((V || Y) && (H.width = V && (!a.resizeDirection || a.resizeDirection === "horizontal") ? X : m.width, H.height = Y && (!a.resizeDirection || a.resizeDirection === "vertical") ? ne : m.height, m.width = H.width, m.height = H.height), E && y.expandParent) {
        const O = Z[0] * (H.width ?? 0);
        H.x && H.x < O && (m.x = O, k.x = k.x - (H.x - O));
        const Q = Z[1] * (H.height ?? 0);
        H.y && H.y < Q && (m.y = Q, k.y = k.y - (H.y - Q));
      }
      const J = gy({
        width: m.width,
        prevWidth: F,
        height: m.height,
        prevHeight: K,
        affectsX: a.controlDirection.affectsX,
        affectsY: a.controlDirection.affectsY
      }), ee = { ...m, direction: J };
      w?.(M, ee) !== !1 && (D = !0, g?.(M, ee), o(H, $));
    }).on("end", (M) => {
      D && (v?.(M, { ...m }), i?.({ ...m }), D = !1);
    });
    s.call(B);
  }
  function d() {
    s.on(".drag", null);
  }
  return {
    update: c,
    destroy: d
  };
}
var cr = { exports: {} }, lr = {}, dr = { exports: {} }, ur = {};
var ga;
function vy() {
  if (ga) return ur;
  ga = 1;
  var e = ct;
  function t(f, h) {
    return f === h && (f !== 0 || 1 / f === 1 / h) || f !== f && h !== h;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, i = e.useEffect, s = e.useLayoutEffect, a = e.useDebugValue;
  function c(f, h) {
    var p = h(), g = o({ inst: { value: p, getSnapshot: h } }), v = g[0].inst, w = g[1];
    return s(
      function() {
        v.value = p, v.getSnapshot = h, d(v) && w({ inst: v });
      },
      [f, p, h]
    ), i(
      function() {
        return d(v) && w({ inst: v }), f(function() {
          d(v) && w({ inst: v });
        });
      },
      [f]
    ), a(p), p;
  }
  function d(f) {
    var h = f.getSnapshot;
    f = f.value;
    try {
      var p = h();
      return !n(f, p);
    } catch {
      return !0;
    }
  }
  function l(f, h) {
    return h();
  }
  var u = typeof window > "u" || typeof window.document > "u" || typeof window.document.createElement > "u" ? l : c;
  return ur.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : u, ur;
}
var ya;
function by() {
  return ya || (ya = 1, dr.exports = vy()), dr.exports;
}
var ma;
function Ny() {
  if (ma) return lr;
  ma = 1;
  var e = ct, t = by();
  function n(l, u) {
    return l === u && (l !== 0 || 1 / l === 1 / u) || l !== l && u !== u;
  }
  var o = typeof Object.is == "function" ? Object.is : n, i = t.useSyncExternalStore, s = e.useRef, a = e.useEffect, c = e.useMemo, d = e.useDebugValue;
  return lr.useSyncExternalStoreWithSelector = function(l, u, f, h, p) {
    var g = s(null);
    if (g.current === null) {
      var v = { hasValue: !1, value: null };
      g.current = v;
    } else v = g.current;
    g = c(
      function() {
        function m(E) {
          if (!k) {
            if (k = !0, y = E, E = h(E), p !== void 0 && v.hasValue) {
              var j = v.value;
              if (p(j, E))
                return b = j;
            }
            return b = E;
          }
          if (j = b, o(y, E)) return j;
          var _ = h(E);
          return p !== void 0 && p(j, _) ? (y = E, j) : (y = E, b = _);
        }
        var k = !1, y, b, S = f === void 0 ? null : f;
        return [
          function() {
            return m(u());
          },
          S === null ? void 0 : function() {
            return m(S());
          }
        ];
      },
      [u, f, h, p]
    );
    var w = i(l, g[0], g[1]);
    return a(
      function() {
        v.hasValue = !0, v.value = w;
      },
      [w]
    ), d(w), w;
  }, lr;
}
var xa;
function jy() {
  return xa || (xa = 1, cr.exports = Ny()), cr.exports;
}
var Sy = jy();
const ky = /* @__PURE__ */ Hu(Sy), Ey = {}, wa = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (u, f) => {
    const h = typeof u == "function" ? u(t) : u;
    if (!Object.is(h, t)) {
      const p = t;
      t = f ?? (typeof h != "object" || h === null) ? h : Object.assign({}, t, h), n.forEach((g) => g(t, p));
    }
  }, i = () => t, d = { setState: o, getState: i, getInitialState: () => l, subscribe: (u) => (n.add(u), () => n.delete(u)), destroy: () => {
    (Ey ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(o, i, d);
  return d;
}, Cy = (e) => e ? wa(e) : wa, { useDebugValue: Iy } = ct, { useSyncExternalStoreWithSelector: Ay } = ky, _y = (e) => e;
function Pl(e, t = _y, n) {
  const o = Ay(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return Iy(o), o;
}
const va = (e, t) => {
  const n = Cy(e), o = (i, s = t) => Pl(n, i, s);
  return Object.assign(o, n), o;
}, My = (e, t) => e ? va(e, t) : va;
function Ne(e, t) {
  if (Object.is(e, t))
    return !0;
  if (typeof e != "object" || e === null || typeof t != "object" || t === null)
    return !1;
  if (e instanceof Map && t instanceof Map) {
    if (e.size !== t.size) return !1;
    for (const [o, i] of e)
      if (!Object.is(i, t.get(o)))
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
var fr = { exports: {} }, Te = {};
var ba;
function Dy() {
  if (ba) return Te;
  ba = 1;
  var e = ct;
  function t(d) {
    var l = "https://react.dev/errors/" + d;
    if (1 < arguments.length) {
      l += "?args[]=" + encodeURIComponent(arguments[1]);
      for (var u = 2; u < arguments.length; u++)
        l += "&args[]=" + encodeURIComponent(arguments[u]);
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
  }, i = /* @__PURE__ */ Symbol.for("react.portal");
  function s(d, l, u) {
    var f = 3 < arguments.length && arguments[3] !== void 0 ? arguments[3] : null;
    return {
      $$typeof: i,
      key: f == null ? null : "" + f,
      children: d,
      containerInfo: l,
      implementation: u
    };
  }
  var a = e.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE;
  function c(d, l) {
    if (d === "font") return "";
    if (typeof l == "string")
      return l === "use-credentials" ? l : "";
  }
  return Te.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, Te.createPortal = function(d, l) {
    var u = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!l || l.nodeType !== 1 && l.nodeType !== 9 && l.nodeType !== 11)
      throw Error(t(299));
    return s(d, l, null, u);
  }, Te.flushSync = function(d) {
    var l = a.T, u = o.p;
    try {
      if (a.T = null, o.p = 2, d) return d();
    } finally {
      a.T = l, o.p = u, o.d.f();
    }
  }, Te.preconnect = function(d, l) {
    typeof d == "string" && (l ? (l = l.crossOrigin, l = typeof l == "string" ? l === "use-credentials" ? l : "" : void 0) : l = null, o.d.C(d, l));
  }, Te.prefetchDNS = function(d) {
    typeof d == "string" && o.d.D(d);
  }, Te.preinit = function(d, l) {
    if (typeof d == "string" && l && typeof l.as == "string") {
      var u = l.as, f = c(u, l.crossOrigin), h = typeof l.integrity == "string" ? l.integrity : void 0, p = typeof l.fetchPriority == "string" ? l.fetchPriority : void 0;
      u === "style" ? o.d.S(
        d,
        typeof l.precedence == "string" ? l.precedence : void 0,
        {
          crossOrigin: f,
          integrity: h,
          fetchPriority: p
        }
      ) : u === "script" && o.d.X(d, {
        crossOrigin: f,
        integrity: h,
        fetchPriority: p,
        nonce: typeof l.nonce == "string" ? l.nonce : void 0
      });
    }
  }, Te.preinitModule = function(d, l) {
    if (typeof d == "string")
      if (typeof l == "object" && l !== null) {
        if (l.as == null || l.as === "script") {
          var u = c(
            l.as,
            l.crossOrigin
          );
          o.d.M(d, {
            crossOrigin: u,
            integrity: typeof l.integrity == "string" ? l.integrity : void 0,
            nonce: typeof l.nonce == "string" ? l.nonce : void 0
          });
        }
      } else l == null && o.d.M(d);
  }, Te.preload = function(d, l) {
    if (typeof d == "string" && typeof l == "object" && l !== null && typeof l.as == "string") {
      var u = l.as, f = c(u, l.crossOrigin);
      o.d.L(d, u, {
        crossOrigin: f,
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
  }, Te.preloadModule = function(d, l) {
    if (typeof d == "string")
      if (l) {
        var u = c(l.as, l.crossOrigin);
        o.d.m(d, {
          as: typeof l.as == "string" && l.as !== "script" ? l.as : void 0,
          crossOrigin: u,
          integrity: typeof l.integrity == "string" ? l.integrity : void 0
        });
      } else o.d.m(d);
  }, Te.requestFormReset = function(d) {
    o.d.r(d);
  }, Te.unstable_batchedUpdates = function(d, l) {
    return d(l);
  }, Te.useFormState = function(d, l, u) {
    return a.H.useFormState(d, l, u);
  }, Te.useFormStatus = function() {
    return a.H.useHostTransitionStatus();
  }, Te.version = "19.2.7", Te;
}
var Na;
function Ty() {
  if (Na) return fr.exports;
  Na = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), fr.exports = Dy(), fr.exports;
}
var $y = Ty();
const Ii = Br(null), Py = Ii.Provider, zl = qe.error001("react");
function he(e, t) {
  const n = co(Ii);
  if (n === null)
    throw new Error(zl);
  return Pl(n, e, t);
}
function je() {
  const e = co(Ii);
  if (e === null)
    throw new Error(zl);
  return fe(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const ja = { display: "none" }, zy = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, Rl = "react-flow__node-desc", Ll = "react-flow__edge-desc", Ry = "react-flow__aria-live", Ly = (e) => e.ariaLiveMessage, Vy = (e) => e.ariaLabelConfig;
function Hy({ rfId: e }) {
  const t = he(Ly);
  return r.jsx("div", { id: `${Ry}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: zy, children: t });
}
function Oy({ rfId: e, disableKeyboardA11y: t }) {
  const n = he(Vy);
  return r.jsxs(r.Fragment, { children: [r.jsx("div", { id: `${Rl}-${e}`, style: ja, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), r.jsx("div", { id: `${Ll}-${e}`, style: ja, children: n["edge.a11yDescription.default"] }), !t && r.jsx(Hy, { rfId: e })] });
}
const Ai = wi(({ position: e = "top-left", children: t, className: n, style: o, ...i }, s) => {
  const a = `${e}`.split("-");
  return r.jsx("div", { className: Ce(["react-flow__panel", n, ...a]), style: o, ref: s, ...i, children: t });
});
Ai.displayName = "Panel";
function Fy({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : r.jsx(Ai, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: r.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const By = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, Vo = (e) => e.id;
function Wy(e, t) {
  return Ne(e.selectedNodes.map(Vo), t.selectedNodes.map(Vo)) && Ne(e.selectedEdges.map(Vo), t.selectedEdges.map(Vo));
}
function Ky({ onSelectionChange: e }) {
  const t = je(), { selectedNodes: n, selectedEdges: o } = he(By, Wy);
  return oe(() => {
    const i = { nodes: n, edges: o };
    e?.(i), t.getState().onSelectionChangeHandlers.forEach((s) => s(i));
  }, [n, o, e]), null;
}
const qy = (e) => !!e.onSelectionChangeHandlers;
function Xy({ onSelectionChange: e }) {
  const t = he(qy);
  return e || t ? r.jsx(Ky, { onSelectionChange: e }) : null;
}
const Vl = [0, 0], Yy = { x: 0, y: 0, zoom: 1 }, Uy = [
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
], Sa = [...Uy, "rfId"], Zy = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), ka = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: no,
  nodeOrigin: Vl,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function Gy(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: i, setTranslateExtent: s, setNodeExtent: a, reset: c, setDefaultNodesAndEdges: d } = he(Zy, Ne), l = je();
  oe(() => (d(e.defaultNodes, e.defaultEdges), () => {
    u.current = ka, c();
  }), []);
  const u = re(ka);
  return oe(
    () => {
      for (const f of Sa) {
        const h = e[f], p = u.current[f];
        h !== p && (typeof e[f] > "u" || (f === "nodes" ? t(h) : f === "edges" ? n(h) : f === "minZoom" ? o(h) : f === "maxZoom" ? i(h) : f === "translateExtent" ? s(h) : f === "nodeExtent" ? a(h) : f === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: Dg(h) }) : f === "fitView" ? l.setState({ fitViewQueued: h }) : f === "fitViewOptions" ? l.setState({ fitViewOptions: h }) : l.setState({ [f]: h })));
      }
      u.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    Sa.map((f) => e[f])
  ), null;
}
function Ea() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function Jy(e) {
  const [t, n] = q(e === "system" ? null : e);
  return oe(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = Ea(), i = () => n(o?.matches ? "dark" : "light");
    return i(), o?.addEventListener("change", i), () => {
      o?.removeEventListener("change", i);
    };
  }, [e]), t !== null ? t : Ea()?.matches ? "dark" : "light";
}
const Ca = typeof document < "u" ? document : null;
function so(e = null, t = { target: Ca, actInsideInputWithModifier: !0 }) {
  const [n, o] = q(!1), i = re(!1), s = re(/* @__PURE__ */ new Set([])), [a, c] = fe(() => {
    if (e !== null) {
      const l = (Array.isArray(e) ? e : [e]).filter((f) => typeof f == "string").map((f) => f.replace("+", `
`).replace(`

`, `
+`).split(`
`)), u = l.reduce((f, h) => f.concat(...h), []);
      return [l, u];
    }
    return [[], []];
  }, [e]);
  return oe(() => {
    const d = t?.target ?? Ca, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const u = (p) => {
        if (i.current = p.ctrlKey || p.metaKey || p.shiftKey || p.altKey, (!i.current || i.current && !l) && ml(p))
          return !1;
        const v = Aa(p.code, c);
        if (s.current.add(p[v]), Ia(a, s.current, !1)) {
          const w = p.composedPath?.()?.[0] || p.target, m = w?.nodeName === "BUTTON" || w?.nodeName === "A";
          t.preventDefault !== !1 && (i.current || !m) && p.preventDefault(), o(!0);
        }
      }, f = (p) => {
        const g = Aa(p.code, c);
        Ia(a, s.current, !0) ? (o(!1), s.current.clear()) : s.current.delete(p[g]), p.key === "Meta" && s.current.clear(), i.current = !1;
      }, h = () => {
        s.current.clear(), o(!1);
      };
      return d?.addEventListener("keydown", u), d?.addEventListener("keyup", f), window.addEventListener("blur", h), window.addEventListener("contextmenu", h), () => {
        d?.removeEventListener("keydown", u), d?.removeEventListener("keyup", f), window.removeEventListener("blur", h), window.removeEventListener("contextmenu", h);
      };
    }
  }, [e, o]), n;
}
function Ia(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((i) => t.has(i)));
}
function Aa(e, t) {
  return t.includes(e) ? "code" : "key";
}
const Qy = () => {
  const e = je();
  return fe(() => ({
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
      const { transform: [o, i, s], panZoom: a } = e.getState();
      return a ? (await a.setViewport({
        x: t.x ?? o,
        y: t.y ?? i,
        zoom: t.zoom ?? s
      }, n), !0) : !1;
    },
    getViewport: () => {
      const [t, n, o] = e.getState().transform;
      return { x: t, y: n, zoom: o };
    },
    setCenter: async (t, n, o) => e.getState().setCenter(t, n, o),
    fitBounds: async (t, n) => {
      const { width: o, height: i, minZoom: s, maxZoom: a, panZoom: c } = e.getState(), d = es(t, o, i, s, a, n?.padding ?? 0.1);
      return c ? (await c.setViewport(d, {
        duration: n?.duration,
        ease: n?.ease,
        interpolate: n?.interpolate
      }), !0) : !1;
    },
    screenToFlowPosition: (t, n = {}) => {
      const { transform: o, snapGrid: i, snapToGrid: s, domNode: a } = e.getState();
      if (!a)
        return t;
      const { x: c, y: d } = a.getBoundingClientRect(), l = {
        x: t.x - c,
        y: t.y - d
      }, u = n.snapGrid ?? i, f = n.snapToGrid ?? s;
      return mn(l, o, f, u);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: i, y: s } = o.getBoundingClientRect(), a = fn(t, n);
      return {
        x: a.x + i,
        y: a.y + s
      };
    }
  }), []);
};
function Hl(e, t) {
  const n = [], o = /* @__PURE__ */ new Map(), i = [];
  for (const s of e)
    if (s.type === "add") {
      i.push(s);
      continue;
    } else if (s.type === "remove" || s.type === "replace")
      o.set(s.id, [s]);
    else {
      const a = o.get(s.id);
      a ? a.push(s) : o.set(s.id, [s]);
    }
  for (const s of t) {
    const a = o.get(s.id);
    if (!a) {
      n.push(s);
      continue;
    }
    if (a[0].type === "remove")
      continue;
    if (a[0].type === "replace") {
      n.push({ ...a[0].item });
      continue;
    }
    const c = { ...s };
    for (const d of a)
      em(d, c);
    n.push(c);
  }
  return i.length && i.forEach((s) => {
    s.index !== void 0 ? n.splice(s.index, 0, { ...s.item }) : n.push({ ...s.item });
  }), n;
}
function em(e, t) {
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
function Ol(e, t) {
  return Hl(e, t);
}
function Fl(e, t) {
  return Hl(e, t);
}
function Mt(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function nn(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [i, s] of e) {
    const a = t.has(i);
    !(s.selected === void 0 && !a) && s.selected !== a && (n && (s.selected = a), o.push(Mt(s.id, a)));
  }
  return o;
}
function _a({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((i) => [i.id, i]));
  for (const [i, s] of e.entries()) {
    const a = t.get(s.id), c = a?.internals?.userNode ?? a;
    c !== void 0 && c !== s && n.push({ id: s.id, item: s, type: "replace" }), c === void 0 && n.push({ item: s, type: "add", index: i });
  }
  for (const [i] of t)
    o.get(i) === void 0 && n.push({ id: i, type: "remove" });
  return n;
}
function Ma(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const Bl = hl();
function Wl(e, t, n = {}) {
  return Rg(e, t, {
    ...n,
    onError: n.onError ?? Bl
  });
}
function tm(e, t, n, o = { shouldReplaceId: !0 }) {
  return Lg(e, t, n, {
    ...o,
    onError: o.onError ?? Bl
  });
}
const Da = (e) => jg(e), nm = (e) => ll(e);
function Kl(e) {
  return wi(e);
}
const om = typeof window < "u" ? Pu : oe;
function Ta(e) {
  const [t, n] = q(BigInt(0)), [o] = q(() => im(() => n((i) => i + BigInt(1))));
  return om(() => {
    const i = o.get();
    i.length && (e(i), o.reset());
  }, [t]), o;
}
function im(e) {
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
const ql = Br(null);
function rm({ children: e }) {
  const t = je(), n = ue((c) => {
    const { nodes: d = [], setNodes: l, hasDefaultNodes: u, onNodesChange: f, nodeLookup: h, fitViewQueued: p, onNodesChangeMiddlewareMap: g } = t.getState();
    let v = d;
    for (const m of c)
      v = typeof m == "function" ? m(v) : m;
    let w = _a({
      items: v,
      lookup: h
    });
    for (const m of g.values())
      w = m(w);
    u && l(v), w.length > 0 ? f?.(w) : p && window.requestAnimationFrame(() => {
      const { fitViewQueued: m, nodes: k, setNodes: y } = t.getState();
      m && y(k);
    });
  }, []), o = Ta(n), i = ue((c) => {
    const { edges: d = [], setEdges: l, hasDefaultEdges: u, onEdgesChange: f, edgeLookup: h } = t.getState();
    let p = d;
    for (const g of c)
      p = typeof g == "function" ? g(p) : g;
    u ? l(p) : f && f(_a({
      items: p,
      lookup: h
    }));
  }, []), s = Ta(i), a = fe(() => ({ nodeQueue: o, edgeQueue: s }), []);
  return r.jsx(ql.Provider, { value: a, children: e });
}
function sm() {
  const e = co(ql);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const am = (e) => !!e.panZoom;
function as() {
  const e = Qy(), t = je(), n = sm(), o = he(am), i = fe(() => {
    const s = (f) => t.getState().nodeLookup.get(f), a = (f) => {
      n.nodeQueue.push(f);
    }, c = (f) => {
      n.edgeQueue.push(f);
    }, d = (f) => {
      const { nodeLookup: h, nodeOrigin: p } = t.getState(), g = Da(f) ? f : h.get(f.id), v = g.parentId ? gl(g.position, g.measured, g.parentId, h, p) : g.position, w = {
        ...g,
        position: v,
        width: g.measured?.width ?? g.width,
        height: g.measured?.height ?? g.height
      };
      return un(w);
    }, l = (f, h, p = { replace: !1 }) => {
      a((g) => g.map((v) => {
        if (v.id === f) {
          const w = typeof h == "function" ? h(v) : h;
          return p.replace && Da(w) ? w : { ...v, ...w };
        }
        return v;
      }));
    }, u = (f, h, p = { replace: !1 }) => {
      c((g) => g.map((v) => {
        if (v.id === f) {
          const w = typeof h == "function" ? h(v) : h;
          return p.replace && nm(w) ? w : { ...v, ...w };
        }
        return v;
      }));
    };
    return {
      getNodes: () => t.getState().nodes.map((f) => ({ ...f })),
      getNode: (f) => s(f)?.internals.userNode,
      getInternalNode: s,
      getEdges: () => {
        const { edges: f = [] } = t.getState();
        return f.map((h) => ({ ...h }));
      },
      getEdge: (f) => t.getState().edgeLookup.get(f),
      setNodes: a,
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
        const { nodes: f = [], edges: h = [], transform: p } = t.getState(), [g, v, w] = p;
        return {
          nodes: f.map((m) => ({ ...m })),
          edges: h.map((m) => ({ ...m })),
          viewport: {
            x: g,
            y: v,
            zoom: w
          }
        };
      },
      deleteElements: async ({ nodes: f = [], edges: h = [] }) => {
        const { nodes: p, edges: g, onNodesDelete: v, onEdgesDelete: w, triggerNodeChanges: m, triggerEdgeChanges: k, onDelete: y, onBeforeDelete: b } = t.getState(), { nodes: S, edges: E } = await Ig({
          nodesToRemove: f,
          edgesToRemove: h,
          nodes: p,
          edges: g,
          onBeforeDelete: b
        }), j = E.length > 0, _ = S.length > 0;
        if (j) {
          const D = E.map(Ma);
          w?.(E), k(D);
        }
        if (_) {
          const D = S.map(Ma);
          v?.(S), m(D);
        }
        return (_ || j) && y?.({ nodes: S, edges: E }), { deletedNodes: S, deletedEdges: E };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, h = !0, p) => {
        const g = na(f), v = g ? f : d(f), w = p !== void 0;
        return v ? (p || t.getState().nodes).filter((m) => {
          const k = t.getState().nodeLookup.get(m.id);
          if (k && !g && (m.id === f.id || !k.internals.positionAbsolute))
            return !1;
          const y = un(w ? m : k), b = io(y, v);
          return h && b > 0 || b >= y.width * y.height || b >= v.width * v.height;
        }) : [];
      },
      isNodeIntersecting: (f, h, p = !0) => {
        const v = na(f) ? f : d(f);
        if (!v)
          return !1;
        const w = io(v, h);
        return p && w > 0 || w >= h.width * h.height || w >= v.width * v.height;
      },
      updateNode: l,
      updateNodeData: (f, h, p = { replace: !1 }) => {
        l(f, (g) => {
          const v = typeof h == "function" ? h(g) : h;
          return p.replace ? { ...g, data: v } : { ...g, data: { ...g.data, ...v } };
        }, p);
      },
      updateEdge: u,
      updateEdgeData: (f, h, p = { replace: !1 }) => {
        u(f, (g) => {
          const v = typeof h == "function" ? h(g) : h;
          return p.replace ? { ...g, data: v } : { ...g, data: { ...g.data, ...v } };
        }, p);
      },
      getNodesBounds: (f) => {
        const { nodeLookup: h, nodeOrigin: p } = t.getState();
        return Sg(f, { nodeLookup: h, nodeOrigin: p });
      },
      getHandleConnections: ({ type: f, id: h, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}-${f}${h ? `-${h}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: h, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}${f ? h ? `-${f}-${h}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const h = t.getState().fitViewResolver ?? Mg();
        return t.setState({ fitViewQueued: !0, fitViewOptions: f, fitViewResolver: h }), n.nodeQueue.push((p) => [...p]), h.promise;
      }
    };
  }, []);
  return fe(() => ({
    ...i,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const $a = (e) => e.selected, cm = typeof window < "u" ? window : void 0;
function lm({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = je(), { deleteElements: o } = as(), i = so(e, { actInsideInputWithModifier: !1 }), s = so(t, { target: cm });
  oe(() => {
    if (i) {
      const { edges: a, nodes: c } = n.getState();
      o({ nodes: c.filter($a), edges: a.filter($a) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [i]), oe(() => {
    n.setState({ multiSelectionActive: s });
  }, [s]);
}
function dm(e) {
  const t = je();
  oe(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = ts(e.current);
      (o.height === 0 || o.width === 0) && t.getState().onError?.("004", qe.error004()), t.setState({ width: o.width || 500, height: o.height || 500 });
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
const _i = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, um = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function fm({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: i = 0.5, panOnScrollMode: s = $t.Free, zoomOnDoubleClick: a = !0, panOnDrag: c = !0, defaultViewport: d, translateExtent: l, minZoom: u, maxZoom: f, zoomActivationKeyCode: h, preventScrolling: p = !0, children: g, noWheelClassName: v, noPanClassName: w, onViewportChange: m, isControlledViewport: k, paneClickDistance: y, selectionOnDrag: b }) {
  const S = je(), E = re(null), { userSelectionActive: j, lib: _, connectionInProgress: D } = he(um, Ne), B = so(h), M = re();
  dm(E);
  const T = ue((L) => {
    m?.({ x: L[0], y: L[1], zoom: L[2] }), k || S.setState({ transform: L });
  }, [m, k]);
  return oe(() => {
    if (E.current) {
      M.current = py({
        domNode: E.current,
        minZoom: u,
        maxZoom: f,
        translateExtent: l,
        viewport: d,
        onDraggingChange: (I) => S.setState(($) => $.paneDragging === I ? $ : { paneDragging: I }),
        onPanZoomStart: (I, $) => {
          const { onViewportChangeStart: z, onMoveStart: P } = S.getState();
          P?.(I, $), z?.($);
        },
        onPanZoom: (I, $) => {
          const { onViewportChange: z, onMove: P } = S.getState();
          P?.(I, $), z?.($);
        },
        onPanZoomEnd: (I, $) => {
          const { onViewportChangeEnd: z, onMoveEnd: P } = S.getState();
          P?.(I, $), z?.($);
        }
      });
      const { x: L, y: N, zoom: A } = M.current.getViewport();
      return S.setState({
        panZoom: M.current,
        transform: [L, N, A],
        domNode: E.current.closest(".react-flow")
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
      panOnScrollSpeed: i,
      panOnScrollMode: s,
      zoomOnDoubleClick: a,
      panOnDrag: c,
      zoomActivationKeyPressed: B,
      preventScrolling: p,
      noPanClassName: w,
      userSelectionActive: j,
      noWheelClassName: v,
      lib: _,
      onTransformChange: T,
      connectionInProgress: D,
      selectionOnDrag: b,
      paneClickDistance: y
    });
  }, [
    e,
    t,
    n,
    o,
    i,
    s,
    a,
    c,
    B,
    p,
    w,
    j,
    v,
    _,
    T,
    D,
    b,
    y
  ]), r.jsx("div", { className: "react-flow__renderer", ref: E, style: _i, children: g });
}
const hm = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function pm() {
  const { userSelectionActive: e, userSelectionRect: t } = he(hm, Ne);
  return e && t ? r.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const hr = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, gm = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function ym({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = oo.Full, panOnDrag: o, autoPanOnSelection: i, paneClickDistance: s, selectionOnDrag: a, onSelectionStart: c, onSelectionEnd: d, onPaneClick: l, onPaneContextMenu: u, onPaneScroll: f, onPaneMouseEnter: h, onPaneMouseMove: p, onPaneMouseLeave: g, children: v }) {
  const w = re(0), m = je(), { userSelectionActive: k, elementsSelectable: y, dragging: b, connectionInProgress: S, panBy: E, autoPanSpeed: j } = he(gm, Ne), _ = y && (e || k), D = re(null), B = re(), M = re(/* @__PURE__ */ new Set()), T = re(/* @__PURE__ */ new Set()), L = re(!1), N = re({ x: 0, y: 0 }), A = re(!1), I = (V) => {
    if (L.current || S) {
      L.current = !1;
      return;
    }
    l?.(V), m.getState().resetSelectedElements(), m.setState({ nodesSelectionActive: !1 });
  }, $ = (V) => {
    if (Array.isArray(o) && o?.includes(2)) {
      V.preventDefault();
      return;
    }
    u?.(V);
  }, z = f ? (V) => f(V) : void 0, P = (V) => {
    L.current && (V.stopPropagation(), L.current = !1);
  }, F = (V) => {
    const { domNode: Y, transform: ae } = m.getState();
    if (B.current = Y?.getBoundingClientRect(), !B.current)
      return;
    const ce = V.target === D.current;
    if (!ce && !!V.target.closest(".nokey") || !e || !(a && ce || t) || V.button !== 0 || !V.isPrimary)
      return;
    V.target?.setPointerCapture?.(V.pointerId), L.current = !1;
    const { x: de, y: O } = nt(V.nativeEvent, B.current), Q = mn({ x: de, y: O }, ae);
    m.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: Q.x,
        startY: Q.y,
        x: de,
        y: O
      }
    }), ce || (V.stopPropagation(), V.preventDefault());
  };
  function K(V, Y) {
    const { userSelectionRect: ae } = m.getState();
    if (!ae)
      return;
    const { transform: ce, nodeLookup: J, edgeLookup: ee, connectionLookup: de, triggerNodeChanges: O, triggerEdgeChanges: Q, defaultEdgeOptions: ge } = m.getState(), ve = { x: ae.startX, y: ae.startY }, { x: Ee, y: $e } = fn(ve, ce), Ie = {
      startX: ve.x,
      startY: ve.y,
      x: V < Ee ? V : Ee,
      y: Y < $e ? Y : $e,
      width: Math.abs(V - Ee),
      height: Math.abs(Y - $e)
    }, Xe = M.current, be = T.current;
    M.current = new Set(Jr(J, Ie, ce, n === oo.Partial, !0).map((Ve) => Ve.id)), T.current = /* @__PURE__ */ new Set();
    const Le = ge?.selectable ?? !0;
    for (const Ve of M.current) {
      const Ze = de.get(Ve);
      if (Ze)
        for (const { edgeId: Ge } of Ze.values()) {
          const Je = ee.get(Ge);
          Je && (Je.selectable ?? Le) && T.current.add(Ge);
        }
    }
    if (!oa(Xe, M.current)) {
      const Ve = nn(J, M.current, !0);
      O(Ve);
    }
    if (!oa(be, T.current)) {
      const Ve = nn(ee, T.current);
      Q(Ve);
    }
    m.setState({
      userSelectionRect: Ie,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function H() {
    if (!i || !B.current)
      return;
    const [V, Y] = Qr(N.current, B.current, j);
    E({ x: V, y: Y }).then((ae) => {
      if (!L.current || !ae) {
        w.current = requestAnimationFrame(H);
        return;
      }
      const { x: ce, y: J } = N.current;
      K(ce, J), w.current = requestAnimationFrame(H);
    });
  }
  const Z = () => {
    cancelAnimationFrame(w.current), w.current = 0, A.current = !1;
  };
  oe(() => () => Z(), []);
  const X = (V) => {
    const { userSelectionRect: Y, transform: ae, resetSelectedElements: ce } = m.getState();
    if (!B.current || !Y)
      return;
    const { x: J, y: ee } = nt(V.nativeEvent, B.current);
    N.current = { x: J, y: ee };
    const de = fn({ x: Y.startX, y: Y.startY }, ae);
    if (!L.current) {
      const O = t ? 0 : s;
      if (Math.hypot(J - de.x, ee - de.y) <= O)
        return;
      ce(), c?.(V);
    }
    L.current = !0, A.current || (H(), A.current = !0), K(J, ee);
  }, ne = (V) => {
    V.button === 0 && (V.target?.releasePointerCapture?.(V.pointerId), !k && V.target === D.current && m.getState().userSelectionRect && I?.(V), m.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), L.current && (d?.(V), m.setState({
      nodesSelectionActive: M.current.size > 0
    })), Z());
  }, se = (V) => {
    V.target?.releasePointerCapture?.(V.pointerId), Z();
  }, G = o === !0 || Array.isArray(o) && o.includes(0);
  return r.jsxs("div", { className: Ce(["react-flow__pane", { draggable: G, dragging: b, selection: e }]), onClick: _ ? void 0 : hr(I, D), onContextMenu: hr($, D), onWheel: hr(z, D), onPointerEnter: _ ? void 0 : h, onPointerMove: _ ? X : p, onPointerUp: _ ? ne : void 0, onPointerCancel: _ ? se : void 0, onPointerDownCapture: _ ? F : void 0, onClickCapture: _ ? P : void 0, onPointerLeave: g, ref: D, style: _i, children: [v, r.jsx(pm, {})] });
}
function Tr({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: i, unselectNodesAndEdges: s, multiSelectionActive: a, nodeLookup: c, onError: d } = t.getState(), l = c.get(e);
  if (!l) {
    d?.("012", qe.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && a) && (s({ nodes: [l], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : i([e]);
}
function Xl({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: i, isSelectable: s, nodeClickDistance: a }) {
  const c = je(), [d, l] = q(!1), u = re();
  return oe(() => {
    u.current = ey({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (f) => {
        Tr({
          id: f,
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
    if (!(t || !e.current || !u.current))
      return u.current.update({
        noDragClassName: n,
        handleSelector: o,
        domNode: e.current,
        isSelectable: s,
        nodeId: i,
        nodeClickDistance: a
      }), () => {
        u.current?.destroy();
      };
  }, [n, o, t, s, e, i, a]), d;
}
const mm = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function Yl() {
  const e = je();
  return ue((n) => {
    const { nodeExtent: o, snapToGrid: i, snapGrid: s, nodesDraggable: a, onError: c, updateNodePositions: d, nodeLookup: l, nodeOrigin: u } = e.getState(), f = /* @__PURE__ */ new Map(), h = mm(a), p = i ? s[0] : 5, g = i ? s[1] : 5, v = n.direction.x * p * n.factor, w = n.direction.y * g * n.factor;
    for (const [, m] of l) {
      if (!h(m))
        continue;
      let k = {
        x: m.internals.positionAbsolute.x + v,
        y: m.internals.positionAbsolute.y + w
      };
      i && (k = po(k, s));
      const { position: y, positionAbsolute: b } = dl({
        nodeId: m.id,
        nextPosition: k,
        nodeLookup: l,
        nodeExtent: o,
        nodeOrigin: u,
        onError: c
      });
      m.position = y, m.internals.positionAbsolute = b, f.set(m.id, m);
    }
    d(f);
  }, []);
}
const cs = Br(null), xm = cs.Provider;
cs.Consumer;
const Ul = () => co(cs), wm = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), vm = (e, t, n) => (o) => {
  const { connectionClickStartHandle: i, connectionMode: s, connection: a } = o, { fromHandle: c, toHandle: d, isValid: l } = a, u = d?.nodeId === e && d?.id === t && d?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: u,
    clickConnecting: i?.nodeId === e && i?.id === t && i?.type === n,
    isPossibleEndHandle: s === ln.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!i,
    valid: u && l
  };
};
function bm({ type: e = "source", position: t = te.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: i = !0, isConnectableEnd: s = !0, id: a, onConnect: c, children: d, className: l, onMouseDown: u, onTouchStart: f, ...h }, p) {
  const g = a || null, v = e === "target", w = je(), m = Ul(), { connectOnClick: k, noPanClassName: y, rfId: b } = he(wm, Ne), { connectingFrom: S, connectingTo: E, clickConnecting: j, isPossibleEndHandle: _, connectionInProcess: D, clickConnectionInProcess: B, valid: M } = he(vm(m, g, e), Ne);
  m || w.getState().onError?.("010", qe.error010());
  const T = (A) => {
    const { defaultEdgeOptions: I, onConnect: $, hasDefaultEdges: z } = w.getState(), P = {
      ...I,
      ...A
    };
    if (z) {
      const { edges: F, setEdges: K, onError: H } = w.getState();
      K(Wl(P, F, { onError: H }));
    }
    $?.(P), c?.(P);
  }, L = (A) => {
    if (!m)
      return;
    const I = xl(A.nativeEvent);
    if (i && (I && A.button === 0 || !I)) {
      const $ = w.getState();
      Dr.onPointerDown(A.nativeEvent, {
        handleDomNode: A.currentTarget,
        autoPanOnConnect: $.autoPanOnConnect,
        connectionMode: $.connectionMode,
        connectionRadius: $.connectionRadius,
        domNode: $.domNode,
        nodeLookup: $.nodeLookup,
        lib: $.lib,
        isTarget: v,
        handleId: g,
        nodeId: m,
        flowId: $.rfId,
        panBy: $.panBy,
        cancelConnection: $.cancelConnection,
        onConnectStart: $.onConnectStart,
        onConnectEnd: (...z) => w.getState().onConnectEnd?.(...z),
        updateConnection: $.updateConnection,
        onConnect: T,
        isValidConnection: n || ((...z) => w.getState().isValidConnection?.(...z) ?? !0),
        getTransform: () => w.getState().transform,
        getFromHandle: () => w.getState().connection.fromHandle,
        autoPanSpeed: $.autoPanSpeed,
        dragThreshold: $.connectionDragThreshold
      });
    }
    I ? u?.(A) : f?.(A);
  }, N = (A) => {
    const { onClickConnectStart: I, onClickConnectEnd: $, connectionClickStartHandle: z, connectionMode: P, isValidConnection: F, lib: K, rfId: H, nodeLookup: Z, connection: X } = w.getState();
    if (!m || !z && !i)
      return;
    if (!z) {
      I?.(A.nativeEvent, { nodeId: m, handleId: g, handleType: e }), w.setState({ connectionClickStartHandle: { nodeId: m, type: e, id: g } });
      return;
    }
    const ne = yl(A.target), se = n || F, { connection: G, isValid: V } = Dr.isValid(A.nativeEvent, {
      handle: {
        nodeId: m,
        id: g,
        type: e
      },
      connectionMode: P,
      fromNodeId: z.nodeId,
      fromHandleId: z.id || null,
      fromType: z.type,
      isValidConnection: se,
      flowId: H,
      doc: ne,
      lib: K,
      nodeLookup: Z
    });
    V && G && T(G);
    const Y = structuredClone(X);
    delete Y.inProgress, Y.toPosition = Y.toHandle ? Y.toHandle.position : null, $?.(A, Y), w.setState({ connectionClickStartHandle: null });
  };
  return r.jsx("div", { "data-handleid": g, "data-nodeid": m, "data-handlepos": t, "data-id": `${b}-${m}-${g}-${e}`, className: Ce([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    y,
    l,
    {
      source: !v,
      target: v,
      connectable: o,
      connectablestart: i,
      connectableend: s,
      clickconnecting: j,
      connectingfrom: S,
      connectingto: E,
      valid: M,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!D || _) && (D || B ? s : i)
    }
  ]), onMouseDown: L, onTouchStart: L, onClick: k ? N : void 0, ref: p, ...h, children: d });
}
const pn = ke(Kl(bm));
function Nm({ data: e, isConnectable: t, sourcePosition: n = te.Bottom }) {
  return r.jsxs(r.Fragment, { children: [e?.label, r.jsx(pn, { type: "source", position: n, isConnectable: t })] });
}
function jm({ data: e, isConnectable: t, targetPosition: n = te.Top, sourcePosition: o = te.Bottom }) {
  return r.jsxs(r.Fragment, { children: [r.jsx(pn, { type: "target", position: n, isConnectable: t }), e?.label, r.jsx(pn, { type: "source", position: o, isConnectable: t })] });
}
function Sm() {
  return null;
}
function km({ data: e, isConnectable: t, targetPosition: n = te.Top }) {
  return r.jsxs(r.Fragment, { children: [r.jsx(pn, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const ci = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, Pa = {
  input: Nm,
  default: jm,
  output: km,
  group: Sm
};
function Em(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const Cm = (e) => {
  const { width: t, height: n, x: o, y: i } = ho(e.nodeLookup, {
    filter: (s) => !!s.selected
  });
  return {
    width: tt(t) ? t : null,
    height: tt(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${i}px)`
  };
};
function Im({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = je(), { width: i, height: s, transformString: a, userSelectionActive: c } = he(Cm, Ne), d = Yl(), l = re(null);
  oe(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const u = !c && i !== null && s !== null;
  if (Xl({
    nodeRef: l,
    disabled: !u
  }), !u)
    return null;
  const f = e ? (p) => {
    const g = o.getState().nodes.filter((v) => v.selected);
    e(p, g);
  } : void 0, h = (p) => {
    Object.prototype.hasOwnProperty.call(ci, p.key) && (p.preventDefault(), d({
      direction: ci[p.key],
      factor: p.shiftKey ? 4 : 1
    }));
  };
  return r.jsx("div", { className: Ce(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: a
  }, children: r.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : h, style: {
    width: i,
    height: s
  } }) });
}
const za = typeof window < "u" ? window : void 0, Am = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function Zl({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: i, onPaneContextMenu: s, onPaneScroll: a, paneClickDistance: c, deleteKeyCode: d, selectionKeyCode: l, selectionOnDrag: u, selectionMode: f, onSelectionStart: h, onSelectionEnd: p, multiSelectionKeyCode: g, panActivationKeyCode: v, zoomActivationKeyCode: w, elementsSelectable: m, zoomOnScroll: k, zoomOnPinch: y, panOnScroll: b, panOnScrollSpeed: S, panOnScrollMode: E, zoomOnDoubleClick: j, panOnDrag: _, autoPanOnSelection: D, defaultViewport: B, translateExtent: M, minZoom: T, maxZoom: L, preventScrolling: N, onSelectionContextMenu: A, noWheelClassName: I, noPanClassName: $, disableKeyboardA11y: z, onViewportChange: P, isControlledViewport: F }) {
  const { nodesSelectionActive: K, userSelectionActive: H } = he(Am, Ne), Z = so(l, { target: za }), X = so(v, { target: za }), ne = X || _, se = X || b, G = u && ne !== !0, V = Z || H || G;
  return lm({ deleteKeyCode: d, multiSelectionKeyCode: g }), r.jsx(fm, { onPaneContextMenu: s, elementsSelectable: m, zoomOnScroll: k, zoomOnPinch: y, panOnScroll: se, panOnScrollSpeed: S, panOnScrollMode: E, zoomOnDoubleClick: j, panOnDrag: !Z && ne, defaultViewport: B, translateExtent: M, minZoom: T, maxZoom: L, zoomActivationKeyCode: w, preventScrolling: N, noWheelClassName: I, noPanClassName: $, onViewportChange: P, isControlledViewport: F, paneClickDistance: c, selectionOnDrag: G, children: r.jsxs(ym, { onSelectionStart: h, onSelectionEnd: p, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: i, onPaneContextMenu: s, onPaneScroll: a, panOnDrag: ne, autoPanOnSelection: D, isSelecting: !!V, selectionMode: f, selectionKeyPressed: Z, paneClickDistance: c, selectionOnDrag: G, children: [e, K && r.jsx(Im, { onSelectionContextMenu: A, noPanClassName: $, disableKeyboardA11y: z })] }) });
}
Zl.displayName = "FlowRenderer";
const _m = ke(Zl), Mm = (e) => (t) => e ? Jr(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function Dm(e) {
  return he(ue(Mm(e), [e]), Ne);
}
const Tm = (e) => e.updateNodeInternals;
function $m() {
  const e = he(Tm), [t] = q(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
    const o = /* @__PURE__ */ new Map();
    n.forEach((i) => {
      const s = i.target.getAttribute("data-id");
      o.set(s, {
        id: s,
        nodeElement: i.target,
        force: !0
      });
    }), e(o);
  }));
  return oe(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function Pm({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const i = je(), s = re(null), a = re(null), c = re(e.sourcePosition), d = re(e.targetPosition), l = re(t), u = n && !!e.internals.handleBounds;
  return oe(() => {
    s.current && !e.hidden && (!u || a.current !== s.current) && (a.current && o?.unobserve(a.current), o?.observe(s.current), a.current = s.current);
  }, [u, e.hidden]), oe(() => () => {
    a.current && (o?.unobserve(a.current), a.current = null);
  }, []), oe(() => {
    if (s.current) {
      const f = l.current !== t, h = c.current !== e.sourcePosition, p = d.current !== e.targetPosition;
      (f || h || p) && (l.current = t, c.current = e.sourcePosition, d.current = e.targetPosition, i.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: s.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), s;
}
function zm({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: i, onContextMenu: s, onDoubleClick: a, nodesDraggable: c, elementsSelectable: d, nodesConnectable: l, nodesFocusable: u, resizeObserver: f, noDragClassName: h, noPanClassName: p, disableKeyboardA11y: g, rfId: v, nodeTypes: w, nodeClickDistance: m, onError: k }) {
  const { node: y, internals: b, isParent: S } = he((V) => {
    const Y = V.nodeLookup.get(e), ae = V.parentLookup.has(e);
    return {
      node: Y,
      internals: Y.internals,
      isParent: ae
    };
  }, Ne);
  let E = y.type || "default", j = w?.[E] || Pa[E];
  j === void 0 && (k?.("003", qe.error003(E)), E = "default", j = w?.default || Pa.default);
  const _ = !!(y.draggable || c && typeof y.draggable > "u"), D = !!(y.selectable || d && typeof y.selectable > "u"), B = !!(y.connectable || l && typeof y.connectable > "u"), M = !!(y.focusable || u && typeof y.focusable > "u"), T = je(), L = pl(y), N = Pm({ node: y, nodeType: E, hasDimensions: L, resizeObserver: f }), A = Xl({
    nodeRef: N,
    disabled: y.hidden || !_,
    noDragClassName: h,
    handleSelector: y.dragHandle,
    nodeId: e,
    isSelectable: D,
    nodeClickDistance: m
  }), I = Yl();
  if (y.hidden)
    return null;
  const $ = yt(y), z = Em(y), P = D || _ || t || n || o || i, F = n ? (V) => n(V, { ...b.userNode }) : void 0, K = o ? (V) => o(V, { ...b.userNode }) : void 0, H = i ? (V) => i(V, { ...b.userNode }) : void 0, Z = s ? (V) => s(V, { ...b.userNode }) : void 0, X = a ? (V) => a(V, { ...b.userNode }) : void 0, ne = (V) => {
    const { selectNodesOnDrag: Y, nodeDragThreshold: ae } = T.getState();
    D && (!Y || !_ || ae > 0) && Tr({
      id: e,
      store: T,
      nodeRef: N
    }), t && t(V, { ...b.userNode });
  }, se = (V) => {
    if (!(ml(V.nativeEvent) || g)) {
      if (rl.includes(V.key) && D) {
        const Y = V.key === "Escape";
        Tr({
          id: e,
          store: T,
          unselect: Y,
          nodeRef: N
        });
      } else if (_ && y.selected && Object.prototype.hasOwnProperty.call(ci, V.key)) {
        V.preventDefault();
        const { ariaLabelConfig: Y } = T.getState();
        T.setState({
          ariaLiveMessage: Y["node.a11yDescription.ariaLiveMessage"]({
            direction: V.key.replace("Arrow", "").toLowerCase(),
            x: ~~b.positionAbsolute.x,
            y: ~~b.positionAbsolute.y
          })
        }), I({
          direction: ci[V.key],
          factor: V.shiftKey ? 4 : 1
        });
      }
    }
  }, G = () => {
    if (g || !N.current?.matches(":focus-visible"))
      return;
    const { transform: V, width: Y, height: ae, autoPanOnNodeFocus: ce, setCenter: J } = T.getState();
    if (!ce)
      return;
    Jr(/* @__PURE__ */ new Map([[e, y]]), { x: 0, y: 0, width: Y, height: ae }, V, !0).length > 0 || J(y.position.x + $.width / 2, y.position.y + $.height / 2, {
      zoom: V[2]
    });
  };
  return r.jsx("div", { className: Ce([
    "react-flow__node",
    `react-flow__node-${E}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [p]: _
    },
    y.className,
    {
      selected: y.selected,
      selectable: D,
      parent: S,
      draggable: _,
      dragging: A
    }
  ]), ref: N, style: {
    zIndex: b.z,
    transform: `translate(${b.positionAbsolute.x}px,${b.positionAbsolute.y}px)`,
    pointerEvents: P ? "all" : "none",
    visibility: L ? "visible" : "hidden",
    ...y.style,
    ...z
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: F, onMouseMove: K, onMouseLeave: H, onContextMenu: Z, onClick: ne, onDoubleClick: X, onKeyDown: M ? se : void 0, tabIndex: M ? 0 : void 0, onFocus: M ? G : void 0, role: y.ariaRole ?? (M ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": g ? void 0 : `${Rl}-${v}`, "aria-label": y.ariaLabel, ...y.domAttributes, children: r.jsx(xm, { value: e, children: r.jsx(j, { id: e, data: y.data, type: E, positionAbsoluteX: b.positionAbsolute.x, positionAbsoluteY: b.positionAbsolute.y, selected: y.selected ?? !1, selectable: D, draggable: _, deletable: y.deletable ?? !0, isConnectable: B, sourcePosition: y.sourcePosition, targetPosition: y.targetPosition, dragging: A, dragHandle: y.dragHandle, zIndex: b.z, parentId: y.parentId, ...$ }) }) });
}
var Rm = ke(zm);
const Lm = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function Gl(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: i, onError: s } = he(Lm, Ne), a = Dm(e.onlyRenderVisibleElements), c = $m();
  return r.jsx("div", { className: "react-flow__nodes", style: _i, children: a.map((d) => (
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
    r.jsx(Rm, { id: d, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: i, nodeClickDistance: e.nodeClickDistance, onError: s }, d)
  )) });
}
Gl.displayName = "NodeRenderer";
const Vm = ke(Gl);
function Hm(e) {
  return he(ue((n) => {
    if (!e)
      return n.edges.map((i) => i.id);
    const o = [];
    if (n.width && n.height)
      for (const i of n.edges) {
        const s = n.nodeLookup.get(i.source), a = n.nodeLookup.get(i.target);
        s && a && Pg({
          sourceNode: s,
          targetNode: a,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && o.push(i.id);
      }
    return o;
  }, [e]), Ne);
}
const Om = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return r.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, Fm = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return r.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Ra = {
  [ri.Arrow]: Om,
  [ri.ArrowClosed]: Fm
};
function Bm(e) {
  const t = je();
  return fe(() => Object.prototype.hasOwnProperty.call(Ra, e) ? Ra[e] : (t.getState().onError?.("009", qe.error009(e)), null), [e]);
}
const Wm = ({ id: e, type: t, color: n, width: o = 12.5, height: i = 12.5, markerUnits: s = "strokeWidth", strokeWidth: a, orient: c = "auto-start-reverse" }) => {
  const d = Bm(t);
  return d ? r.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${i}`, viewBox: "-10 -10 20 20", markerUnits: s, orient: c, refX: "0", refY: "0", children: r.jsx(d, { color: n, strokeWidth: a }) }) : null;
}, Jl = ({ defaultColor: e, rfId: t }) => {
  const n = he((s) => s.edges), o = he((s) => s.defaultEdgeOptions), i = fe(() => Bg(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return i.length ? r.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: r.jsx("defs", { children: i.map((s) => r.jsx(Wm, { id: s.id, type: s.type, color: s.color, width: s.width, height: s.height, markerUnits: s.markerUnits, strokeWidth: s.strokeWidth, orient: s.orient }, s.id)) }) }) : null;
};
Jl.displayName = "MarkerDefinitions";
var Km = ke(Jl);
function Ql({ x: e, y: t, label: n, labelStyle: o, labelShowBg: i = !0, labelBgStyle: s, labelBgPadding: a = [2, 4], labelBgBorderRadius: c = 2, children: d, className: l, ...u }) {
  const [f, h] = q({ x: 1, y: 0, width: 0, height: 0 }), p = Ce(["react-flow__edge-textwrapper", l]), g = re(null);
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
  }, [n]), n ? r.jsxs("g", { transform: `translate(${e - f.width / 2} ${t - f.height / 2})`, className: p, visibility: f.width ? "visible" : "hidden", ...u, children: [i && r.jsx("rect", { width: f.width + 2 * a[0], x: -a[0], y: -a[1], height: f.height + 2 * a[1], className: "react-flow__edge-textbg", style: s, rx: c, ry: c }), r.jsx("text", { className: "react-flow__edge-text", y: f.height / 2, dy: "0.3em", ref: g, style: o, children: n }), d] }) : null;
}
Ql.displayName = "EdgeText";
const qm = ke(Ql);
function go({ path: e, labelX: t, labelY: n, label: o, labelStyle: i, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: d, interactionWidth: l = 20, ...u }) {
  return r.jsxs(r.Fragment, { children: [r.jsx("path", { ...u, d: e, fill: "none", className: Ce(["react-flow__edge-path", u.className]) }), l ? r.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, o && tt(t) && tt(n) ? r.jsx(qm, { x: t, y: n, label: o, labelStyle: i, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: d }) : null] });
}
function La({ pos: e, x1: t, y1: n, x2: o, y2: i }) {
  return e === te.Left || e === te.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + i)];
}
function ed({ sourceX: e, sourceY: t, sourcePosition: n = te.Bottom, targetX: o, targetY: i, targetPosition: s = te.Top }) {
  const [a, c] = La({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: i
  }), [d, l] = La({
    pos: s,
    x1: o,
    y1: i,
    x2: e,
    y2: t
  }), [u, f, h, p] = wl({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: i,
    sourceControlX: a,
    sourceControlY: c,
    targetControlX: d,
    targetControlY: l
  });
  return [
    `M${e},${t} C${a},${c} ${d},${l} ${o},${i}`,
    u,
    f,
    h,
    p
  ];
}
function td(e) {
  return ke(({ id: t, sourceX: n, sourceY: o, targetX: i, targetY: s, sourcePosition: a, targetPosition: c, label: d, labelStyle: l, labelShowBg: u, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: g, markerEnd: v, markerStart: w, interactionWidth: m }) => {
    const [k, y, b] = ed({
      sourceX: n,
      sourceY: o,
      sourcePosition: a,
      targetX: i,
      targetY: s,
      targetPosition: c
    }), S = e.isInternal ? void 0 : t;
    return r.jsx(go, { id: S, path: k, labelX: y, labelY: b, label: d, labelStyle: l, labelShowBg: u, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: g, markerEnd: v, markerStart: w, interactionWidth: m });
  });
}
const Xm = td({ isInternal: !1 }), nd = td({ isInternal: !0 });
Xm.displayName = "SimpleBezierEdge";
nd.displayName = "SimpleBezierEdgeInternal";
function od(e) {
  return ke(({ id: t, sourceX: n, sourceY: o, targetX: i, targetY: s, label: a, labelStyle: c, labelShowBg: d, labelBgStyle: l, labelBgPadding: u, labelBgBorderRadius: f, style: h, sourcePosition: p = te.Bottom, targetPosition: g = te.Top, markerEnd: v, markerStart: w, pathOptions: m, interactionWidth: k }) => {
    const [y, b, S] = ai({
      sourceX: n,
      sourceY: o,
      sourcePosition: p,
      targetX: i,
      targetY: s,
      targetPosition: g,
      borderRadius: m?.borderRadius,
      offset: m?.offset,
      stepPosition: m?.stepPosition
    }), E = e.isInternal ? void 0 : t;
    return r.jsx(go, { id: E, path: y, labelX: b, labelY: S, label: a, labelStyle: c, labelShowBg: d, labelBgStyle: l, labelBgPadding: u, labelBgBorderRadius: f, style: h, markerEnd: v, markerStart: w, interactionWidth: k });
  });
}
const id = od({ isInternal: !1 }), rd = od({ isInternal: !0 });
id.displayName = "SmoothStepEdge";
rd.displayName = "SmoothStepEdgeInternal";
function sd(e) {
  return ke(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return r.jsx(id, { ...n, id: o, pathOptions: fe(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Ym = sd({ isInternal: !1 }), ad = sd({ isInternal: !0 });
Ym.displayName = "StepEdge";
ad.displayName = "StepEdgeInternal";
function cd(e) {
  return ke(({ id: t, sourceX: n, sourceY: o, targetX: i, targetY: s, label: a, labelStyle: c, labelShowBg: d, labelBgStyle: l, labelBgPadding: u, labelBgBorderRadius: f, style: h, markerEnd: p, markerStart: g, interactionWidth: v }) => {
    const [w, m, k] = jl({ sourceX: n, sourceY: o, targetX: i, targetY: s }), y = e.isInternal ? void 0 : t;
    return r.jsx(go, { id: y, path: w, labelX: m, labelY: k, label: a, labelStyle: c, labelShowBg: d, labelBgStyle: l, labelBgPadding: u, labelBgBorderRadius: f, style: h, markerEnd: p, markerStart: g, interactionWidth: v });
  });
}
const Um = cd({ isInternal: !1 }), ld = cd({ isInternal: !0 });
Um.displayName = "StraightEdge";
ld.displayName = "StraightEdgeInternal";
function dd(e) {
  return ke(({ id: t, sourceX: n, sourceY: o, targetX: i, targetY: s, sourcePosition: a = te.Bottom, targetPosition: c = te.Top, label: d, labelStyle: l, labelShowBg: u, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: g, markerEnd: v, markerStart: w, pathOptions: m, interactionWidth: k }) => {
    const [y, b, S] = vl({
      sourceX: n,
      sourceY: o,
      sourcePosition: a,
      targetX: i,
      targetY: s,
      targetPosition: c,
      curvature: m?.curvature
    }), E = e.isInternal ? void 0 : t;
    return r.jsx(go, { id: E, path: y, labelX: b, labelY: S, label: d, labelStyle: l, labelShowBg: u, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: g, markerEnd: v, markerStart: w, interactionWidth: k });
  });
}
const Zm = dd({ isInternal: !1 }), ud = dd({ isInternal: !0 });
Zm.displayName = "BezierEdge";
ud.displayName = "BezierEdgeInternal";
const Va = {
  default: ud,
  straight: ld,
  step: ad,
  smoothstep: rd,
  simplebezier: nd
}, Ha = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, Gm = (e, t, n) => n === te.Left ? e - t : n === te.Right ? e + t : e, Jm = (e, t, n) => n === te.Top ? e - t : n === te.Bottom ? e + t : e, Oa = "react-flow__edgeupdater";
function Fa({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: i, onMouseEnter: s, onMouseOut: a, type: c }) {
  return r.jsx("circle", { onMouseDown: i, onMouseEnter: s, onMouseOut: a, className: Ce([Oa, `${Oa}-${c}`]), cx: Gm(t, o, e), cy: Jm(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function Qm({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: i, targetX: s, targetY: a, sourcePosition: c, targetPosition: d, onReconnect: l, onReconnectStart: u, onReconnectEnd: f, setReconnecting: h, setUpdateHover: p }) {
  const g = je(), v = (b, S) => {
    if (b.button !== 0)
      return;
    const { autoPanOnConnect: E, domNode: j, connectionMode: _, connectionRadius: D, lib: B, onConnectStart: M, cancelConnection: T, nodeLookup: L, rfId: N, panBy: A, updateConnection: I } = g.getState(), $ = S.type === "target", z = (K, H) => {
      h(!1), f?.(K, n, S.type, H);
    }, P = (K) => l?.(n, K), F = (K, H) => {
      h(!0), u?.(b, n, S.type), M?.(K, H);
    };
    Dr.onPointerDown(b.nativeEvent, {
      autoPanOnConnect: E,
      connectionMode: _,
      connectionRadius: D,
      domNode: j,
      handleId: S.id,
      nodeId: S.nodeId,
      nodeLookup: L,
      isTarget: $,
      edgeUpdaterType: S.type,
      lib: B,
      flowId: N,
      cancelConnection: T,
      panBy: A,
      isValidConnection: (...K) => g.getState().isValidConnection?.(...K) ?? !0,
      onConnect: P,
      onConnectStart: F,
      onConnectEnd: (...K) => g.getState().onConnectEnd?.(...K),
      onReconnectEnd: z,
      updateConnection: I,
      getTransform: () => g.getState().transform,
      getFromHandle: () => g.getState().connection.fromHandle,
      dragThreshold: g.getState().connectionDragThreshold,
      handleDomNode: b.currentTarget
    });
  }, w = (b) => v(b, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), m = (b) => v(b, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), k = () => p(!0), y = () => p(!1);
  return r.jsxs(r.Fragment, { children: [(e === !0 || e === "source") && r.jsx(Fa, { position: c, centerX: o, centerY: i, radius: t, onMouseDown: w, onMouseEnter: k, onMouseOut: y, type: "source" }), (e === !0 || e === "target") && r.jsx(Fa, { position: d, centerX: s, centerY: a, radius: t, onMouseDown: m, onMouseEnter: k, onMouseOut: y, type: "target" })] });
}
function ex({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: i, onDoubleClick: s, onContextMenu: a, onMouseEnter: c, onMouseMove: d, onMouseLeave: l, reconnectRadius: u, onReconnect: f, onReconnectStart: h, onReconnectEnd: p, rfId: g, edgeTypes: v, noPanClassName: w, onError: m, disableKeyboardA11y: k }) {
  let y = he((J) => J.edgeLookup.get(e));
  const b = he((J) => J.defaultEdgeOptions);
  y = b ? { ...b, ...y } : y;
  let S = y.type || "default", E = v?.[S] || Va[S];
  E === void 0 && (m?.("011", qe.error011(S)), S = "default", E = v?.default || Va.default);
  const j = !!(y.focusable || t && typeof y.focusable > "u"), _ = typeof f < "u" && (y.reconnectable || n && typeof y.reconnectable > "u"), D = !!(y.selectable || o && typeof y.selectable > "u"), B = re(null), [M, T] = q(!1), [L, N] = q(!1), A = je(), { zIndex: I, sourceX: $, sourceY: z, targetX: P, targetY: F, sourcePosition: K, targetPosition: H } = he(ue((J) => {
    const ee = J.nodeLookup.get(y.source), de = J.nodeLookup.get(y.target);
    if (!ee || !de)
      return {
        zIndex: y.zIndex,
        ...Ha
      };
    const O = Fg({
      id: e,
      sourceNode: ee,
      targetNode: de,
      sourceHandle: y.sourceHandle || null,
      targetHandle: y.targetHandle || null,
      connectionMode: J.connectionMode,
      onError: m
    });
    return {
      zIndex: $g({
        selected: y.selected,
        zIndex: y.zIndex,
        sourceNode: ee,
        targetNode: de,
        elevateOnSelect: J.elevateEdgesOnSelect,
        zIndexMode: J.zIndexMode
      }),
      ...O || Ha
    };
  }, [y.source, y.target, y.sourceHandle, y.targetHandle, y.selected, y.zIndex]), Ne), Z = fe(() => y.markerStart ? `url('#${_r(y.markerStart, g)}')` : void 0, [y.markerStart, g]), X = fe(() => y.markerEnd ? `url('#${_r(y.markerEnd, g)}')` : void 0, [y.markerEnd, g]);
  if (y.hidden || $ === null || z === null || P === null || F === null)
    return null;
  const ne = (J) => {
    const { addSelectedEdges: ee, unselectNodesAndEdges: de, multiSelectionActive: O } = A.getState();
    D && (A.setState({ nodesSelectionActive: !1 }), y.selected && O ? (de({ nodes: [], edges: [y] }), B.current?.blur()) : ee([e])), i && i(J, y);
  }, se = s ? (J) => {
    s(J, { ...y });
  } : void 0, G = a ? (J) => {
    a(J, { ...y });
  } : void 0, V = c ? (J) => {
    c(J, { ...y });
  } : void 0, Y = d ? (J) => {
    d(J, { ...y });
  } : void 0, ae = l ? (J) => {
    l(J, { ...y });
  } : void 0, ce = (J) => {
    if (!k && rl.includes(J.key) && D) {
      const { unselectNodesAndEdges: ee, addSelectedEdges: de } = A.getState();
      J.key === "Escape" ? (B.current?.blur(), ee({ edges: [y] })) : de([e]);
    }
  };
  return r.jsx("svg", { style: { zIndex: I }, children: r.jsxs("g", { className: Ce([
    "react-flow__edge",
    `react-flow__edge-${S}`,
    y.className,
    w,
    {
      selected: y.selected,
      animated: y.animated,
      inactive: !D && !i,
      updating: M,
      selectable: D
    }
  ]), onClick: ne, onDoubleClick: se, onContextMenu: G, onMouseEnter: V, onMouseMove: Y, onMouseLeave: ae, onKeyDown: j ? ce : void 0, tabIndex: j ? 0 : void 0, role: y.ariaRole ?? (j ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": y.ariaLabel === null ? void 0 : y.ariaLabel || `Edge from ${y.source} to ${y.target}`, "aria-describedby": j ? `${Ll}-${g}` : void 0, ref: B, ...y.domAttributes, children: [!L && r.jsx(E, { id: e, source: y.source, target: y.target, type: y.type, selected: y.selected, animated: y.animated, selectable: D, deletable: y.deletable ?? !0, label: y.label, labelStyle: y.labelStyle, labelShowBg: y.labelShowBg, labelBgStyle: y.labelBgStyle, labelBgPadding: y.labelBgPadding, labelBgBorderRadius: y.labelBgBorderRadius, sourceX: $, sourceY: z, targetX: P, targetY: F, sourcePosition: K, targetPosition: H, data: y.data, style: y.style, sourceHandleId: y.sourceHandle, targetHandleId: y.targetHandle, markerStart: Z, markerEnd: X, pathOptions: "pathOptions" in y ? y.pathOptions : void 0, interactionWidth: y.interactionWidth }), _ && r.jsx(Qm, { edge: y, isReconnectable: _, reconnectRadius: u, onReconnect: f, onReconnectStart: h, onReconnectEnd: p, sourceX: $, sourceY: z, targetX: P, targetY: F, sourcePosition: K, targetPosition: H, setUpdateHover: T, setReconnecting: N })] }) });
}
var tx = ke(ex);
const nx = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function fd({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: i, onReconnect: s, onEdgeContextMenu: a, onEdgeMouseEnter: c, onEdgeMouseMove: d, onEdgeMouseLeave: l, onEdgeClick: u, reconnectRadius: f, onEdgeDoubleClick: h, onReconnectStart: p, onReconnectEnd: g, disableKeyboardA11y: v }) {
  const { edgesFocusable: w, edgesReconnectable: m, elementsSelectable: k, onError: y } = he(nx, Ne), b = Hm(t);
  return r.jsxs("div", { className: "react-flow__edges", children: [r.jsx(Km, { defaultColor: e, rfId: n }), b.map((S) => r.jsx(tx, { id: S, edgesFocusable: w, edgesReconnectable: m, elementsSelectable: k, noPanClassName: i, onReconnect: s, onContextMenu: a, onMouseEnter: c, onMouseMove: d, onMouseLeave: l, onClick: u, reconnectRadius: f, onDoubleClick: h, onReconnectStart: p, onReconnectEnd: g, rfId: n, onError: y, edgeTypes: o, disableKeyboardA11y: v }, S))] });
}
fd.displayName = "EdgeRenderer";
const ox = ke(fd), ix = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function rx({ children: e }) {
  const t = he(ix);
  return r.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function sx(e) {
  const t = as(), n = re(!1);
  oe(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const ax = (e) => e.panZoom?.syncViewport;
function cx(e) {
  const t = he(ax), n = je();
  return oe(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function lx(e) {
  return e.connection.inProgress ? { ...e.connection, to: mn(e.connection.to, e.transform) } : { ...e.connection };
}
function dx(e) {
  return lx;
}
function ux(e) {
  const t = dx();
  return he(t, Ne);
}
const fx = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function hx({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: i, width: s, height: a, isValid: c, inProgress: d } = he(fx, Ne);
  return !(s && i && d) ? null : r.jsx("svg", { style: e, width: s, height: a, className: "react-flow__connectionline react-flow__container", children: r.jsx("g", { className: Ce(["react-flow__connection", cl(c)]), children: r.jsx(hd, { style: t, type: n, CustomComponent: o, isValid: c }) }) });
}
const hd = ({ style: e, type: t = bt.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: i, from: s, fromNode: a, fromHandle: c, fromPosition: d, to: l, toNode: u, toHandle: f, toPosition: h, pointer: p } = ux();
  if (!i)
    return;
  if (n)
    return r.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: a, fromHandle: c, fromX: s.x, fromY: s.y, toX: l.x, toY: l.y, fromPosition: d, toPosition: h, connectionStatus: cl(o), toNode: u, toHandle: f, pointer: p });
  let g = "";
  const v = {
    sourceX: s.x,
    sourceY: s.y,
    sourcePosition: d,
    targetX: l.x,
    targetY: l.y,
    targetPosition: h
  };
  switch (t) {
    case bt.Bezier:
      [g] = vl(v);
      break;
    case bt.SimpleBezier:
      [g] = ed(v);
      break;
    case bt.Step:
      [g] = ai({
        ...v,
        borderRadius: 0
      });
      break;
    case bt.SmoothStep:
      [g] = ai(v);
      break;
    default:
      [g] = jl(v);
  }
  return r.jsx("path", { d: g, fill: "none", className: "react-flow__connection-path", style: e });
};
hd.displayName = "ConnectionLine";
const px = {};
function Ba(e = px) {
  re(e), je(), oe(() => {
  }, [e]);
}
function gx() {
  je(), re(!1), oe(() => {
  }, []);
}
function pd({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: i, onNodeDoubleClick: s, onEdgeDoubleClick: a, onNodeMouseEnter: c, onNodeMouseMove: d, onNodeMouseLeave: l, onNodeContextMenu: u, onSelectionContextMenu: f, onSelectionStart: h, onSelectionEnd: p, connectionLineType: g, connectionLineStyle: v, connectionLineComponent: w, connectionLineContainerStyle: m, selectionKeyCode: k, selectionOnDrag: y, selectionMode: b, multiSelectionKeyCode: S, panActivationKeyCode: E, zoomActivationKeyCode: j, deleteKeyCode: _, onlyRenderVisibleElements: D, elementsSelectable: B, defaultViewport: M, translateExtent: T, minZoom: L, maxZoom: N, preventScrolling: A, defaultMarkerColor: I, zoomOnScroll: $, zoomOnPinch: z, panOnScroll: P, panOnScrollSpeed: F, panOnScrollMode: K, zoomOnDoubleClick: H, panOnDrag: Z, autoPanOnSelection: X, onPaneClick: ne, onPaneMouseEnter: se, onPaneMouseMove: G, onPaneMouseLeave: V, onPaneScroll: Y, onPaneContextMenu: ae, paneClickDistance: ce, nodeClickDistance: J, onEdgeContextMenu: ee, onEdgeMouseEnter: de, onEdgeMouseMove: O, onEdgeMouseLeave: Q, reconnectRadius: ge, onReconnect: ve, onReconnectStart: Ee, onReconnectEnd: $e, noDragClassName: Ie, noWheelClassName: Xe, noPanClassName: be, disableKeyboardA11y: Le, nodeExtent: Ve, rfId: Ze, viewport: Ge, onViewportChange: Je }) {
  return Ba(e), Ba(t), gx(), sx(n), cx(Ge), r.jsx(_m, { onPaneClick: ne, onPaneMouseEnter: se, onPaneMouseMove: G, onPaneMouseLeave: V, onPaneContextMenu: ae, onPaneScroll: Y, paneClickDistance: ce, deleteKeyCode: _, selectionKeyCode: k, selectionOnDrag: y, selectionMode: b, onSelectionStart: h, onSelectionEnd: p, multiSelectionKeyCode: S, panActivationKeyCode: E, zoomActivationKeyCode: j, elementsSelectable: B, zoomOnScroll: $, zoomOnPinch: z, zoomOnDoubleClick: H, panOnScroll: P, panOnScrollSpeed: F, panOnScrollMode: K, panOnDrag: Z, autoPanOnSelection: X, defaultViewport: M, translateExtent: T, minZoom: L, maxZoom: N, onSelectionContextMenu: f, preventScrolling: A, noDragClassName: Ie, noWheelClassName: Xe, noPanClassName: be, disableKeyboardA11y: Le, onViewportChange: Je, isControlledViewport: !!Ge, children: r.jsxs(rx, { children: [r.jsx(ox, { edgeTypes: t, onEdgeClick: i, onEdgeDoubleClick: a, onReconnect: ve, onReconnectStart: Ee, onReconnectEnd: $e, onlyRenderVisibleElements: D, onEdgeContextMenu: ee, onEdgeMouseEnter: de, onEdgeMouseMove: O, onEdgeMouseLeave: Q, reconnectRadius: ge, defaultMarkerColor: I, noPanClassName: be, disableKeyboardA11y: Le, rfId: Ze }), r.jsx(hx, { style: v, type: g, component: w, containerStyle: m }), r.jsx("div", { className: "react-flow__edgelabel-renderer" }), r.jsx(Vm, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: d, onNodeMouseLeave: l, onNodeContextMenu: u, nodeClickDistance: J, onlyRenderVisibleElements: D, noPanClassName: be, noDragClassName: Ie, disableKeyboardA11y: Le, nodeExtent: Ve, rfId: Ze }), r.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
pd.displayName = "GraphView";
const yx = ke(pd), mx = hl(), Wa = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: i, height: s, fitView: a, fitViewOptions: c, minZoom: d = 0.5, maxZoom: l = 2, nodeOrigin: u, nodeExtent: f, zIndexMode: h = "basic" } = {}) => {
  const p = /* @__PURE__ */ new Map(), g = /* @__PURE__ */ new Map(), v = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), m = o ?? t ?? [], k = n ?? e ?? [], y = u ?? [0, 0], b = f ?? no;
  El(v, w, m);
  const { nodesInitialized: S } = Mr(k, p, g, {
    nodeOrigin: y,
    nodeExtent: b,
    zIndexMode: h
  });
  let E = [0, 0, 1];
  if (a && i && s) {
    const j = ho(p, {
      filter: (M) => !!((M.width || M.initialWidth) && (M.height || M.initialHeight))
    }), { x: _, y: D, zoom: B } = es(j, i, s, d, l, c?.padding ?? 0.1);
    E = [_, D, B];
  }
  return {
    rfId: "1",
    width: i ?? 0,
    height: s ?? 0,
    transform: E,
    nodes: k,
    nodesInitialized: S,
    nodeLookup: p,
    parentLookup: g,
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
    translateExtent: no,
    nodeExtent: b,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: ln.Strict,
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
    fitViewQueued: a ?? !1,
    fitViewOptions: c,
    fitViewResolver: null,
    connection: { ...al },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: mx,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: sl,
    zIndexMode: h,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, xx = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: i, height: s, fitView: a, fitViewOptions: c, minZoom: d, maxZoom: l, nodeOrigin: u, nodeExtent: f, zIndexMode: h }) => My((p, g) => {
  async function v() {
    const { nodeLookup: w, panZoom: m, fitViewOptions: k, fitViewResolver: y, width: b, height: S, minZoom: E, maxZoom: j } = g();
    m && (await Cg({
      nodes: w,
      width: b,
      height: S,
      panZoom: m,
      minZoom: E,
      maxZoom: j
    }, k), y?.resolve(!0), p({ fitViewResolver: null }));
  }
  return {
    ...Wa({
      nodes: e,
      edges: t,
      width: i,
      height: s,
      fitView: a,
      fitViewOptions: c,
      minZoom: d,
      maxZoom: l,
      nodeOrigin: u,
      nodeExtent: f,
      defaultNodes: n,
      defaultEdges: o,
      zIndexMode: h
    }),
    setNodes: (w) => {
      const { nodeLookup: m, parentLookup: k, nodeOrigin: y, elevateNodesOnSelect: b, fitViewQueued: S, zIndexMode: E, nodesSelectionActive: j } = g(), { nodesInitialized: _, hasSelectedNodes: D } = Mr(w, m, k, {
        nodeOrigin: y,
        nodeExtent: f,
        elevateNodesOnSelect: b,
        checkEquality: !0,
        zIndexMode: E
      }), B = j && D;
      S && _ ? (v(), p({
        nodes: w,
        nodesInitialized: _,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: B
      })) : p({ nodes: w, nodesInitialized: _, nodesSelectionActive: B });
    },
    setEdges: (w) => {
      const { connectionLookup: m, edgeLookup: k } = g();
      El(m, k, w), p({ edges: w });
    },
    setDefaultNodesAndEdges: (w, m) => {
      if (w) {
        const { setNodes: k } = g();
        k(w), p({ hasDefaultNodes: !0 });
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
    updateNodeInternals: (w) => {
      const { triggerNodeChanges: m, nodeLookup: k, parentLookup: y, domNode: b, nodeOrigin: S, nodeExtent: E, debug: j, fitViewQueued: _, zIndexMode: D } = g(), { changes: B, updatedInternals: M } = Zg(w, k, y, b, S, E, D);
      M && (qg(k, y, { nodeOrigin: S, nodeExtent: E, zIndexMode: D }), _ ? (v(), p({ fitViewQueued: !1, fitViewOptions: void 0 })) : p({}), B?.length > 0 && (j && console.log("React Flow: trigger node changes", B), m?.(B)));
    },
    updateNodePositions: (w, m = !1) => {
      const k = [];
      let y = [];
      const { nodeLookup: b, triggerNodeChanges: S, connection: E, updateConnection: j, onNodesChangeMiddlewareMap: _ } = g();
      for (const [D, B] of w) {
        const M = b.get(D), T = !!(M?.expandParent && M?.parentId && B?.position), L = {
          id: D,
          type: "position",
          position: T ? {
            x: Math.max(0, B.position.x),
            y: Math.max(0, B.position.y)
          } : B.position,
          dragging: m
        };
        if (M && E.inProgress && E.fromNode.id === M.id) {
          const N = Ot(M, E.fromHandle, te.Left, !0);
          j({ ...E, from: N });
        }
        T && M.parentId && k.push({
          id: D,
          parentId: M.parentId,
          rect: {
            ...B.internals.positionAbsolute,
            width: B.measured.width ?? 0,
            height: B.measured.height ?? 0
          }
        }), y.push(L);
      }
      if (k.length > 0) {
        const { parentLookup: D, nodeOrigin: B } = g(), M = ss(k, b, D, B);
        y.push(...M);
      }
      for (const D of _.values())
        y = D(y);
      S(y);
    },
    triggerNodeChanges: (w) => {
      const { onNodesChange: m, setNodes: k, nodes: y, hasDefaultNodes: b, debug: S } = g();
      if (w?.length) {
        if (b) {
          const E = Ol(w, y);
          k(E);
        }
        S && console.log("React Flow: trigger node changes", w), m?.(w);
      }
    },
    triggerEdgeChanges: (w) => {
      const { onEdgesChange: m, setEdges: k, edges: y, hasDefaultEdges: b, debug: S } = g();
      if (w?.length) {
        if (b) {
          const E = Fl(w, y);
          k(E);
        }
        S && console.log("React Flow: trigger edge changes", w), m?.(w);
      }
    },
    addSelectedNodes: (w) => {
      const { multiSelectionActive: m, edgeLookup: k, nodeLookup: y, triggerNodeChanges: b, triggerEdgeChanges: S } = g();
      if (m) {
        const E = w.map((j) => Mt(j, !0));
        b(E);
        return;
      }
      b(nn(y, /* @__PURE__ */ new Set([...w]), !0)), S(nn(k));
    },
    addSelectedEdges: (w) => {
      const { multiSelectionActive: m, edgeLookup: k, nodeLookup: y, triggerNodeChanges: b, triggerEdgeChanges: S } = g();
      if (m) {
        const E = w.map((j) => Mt(j, !0));
        S(E);
        return;
      }
      S(nn(k, /* @__PURE__ */ new Set([...w]))), b(nn(y, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: w, edges: m } = {}) => {
      const { edges: k, nodes: y, nodeLookup: b, triggerNodeChanges: S, triggerEdgeChanges: E } = g(), j = w || y, _ = m || k, D = [];
      for (const M of j) {
        if (!M.selected)
          continue;
        const T = b.get(M.id);
        T && (T.selected = !1), D.push(Mt(M.id, !1));
      }
      const B = [];
      for (const M of _)
        M.selected && B.push(Mt(M.id, !1));
      S(D), E(B);
    },
    setMinZoom: (w) => {
      const { panZoom: m, maxZoom: k } = g();
      m?.setScaleExtent([w, k]), p({ minZoom: w });
    },
    setMaxZoom: (w) => {
      const { panZoom: m, minZoom: k } = g();
      m?.setScaleExtent([k, w]), p({ maxZoom: w });
    },
    setTranslateExtent: (w) => {
      g().panZoom?.setTranslateExtent(w), p({ translateExtent: w });
    },
    resetSelectedElements: () => {
      const { edges: w, nodes: m, triggerNodeChanges: k, triggerEdgeChanges: y, elementsSelectable: b } = g();
      if (!b)
        return;
      const S = m.reduce((j, _) => _.selected ? [...j, Mt(_.id, !1)] : j, []), E = w.reduce((j, _) => _.selected ? [...j, Mt(_.id, !1)] : j, []);
      k(S), y(E);
    },
    setNodeExtent: (w) => {
      const { nodes: m, nodeLookup: k, parentLookup: y, nodeOrigin: b, elevateNodesOnSelect: S, nodeExtent: E, zIndexMode: j } = g();
      w[0][0] === E[0][0] && w[0][1] === E[0][1] && w[1][0] === E[1][0] && w[1][1] === E[1][1] || (Mr(m, k, y, {
        nodeOrigin: b,
        nodeExtent: w,
        elevateNodesOnSelect: S,
        checkEquality: !1,
        zIndexMode: j
      }), p({ nodeExtent: w }));
    },
    panBy: (w) => {
      const { transform: m, width: k, height: y, panZoom: b, translateExtent: S } = g();
      return Gg({ delta: w, panZoom: b, transform: m, translateExtent: S, width: k, height: y });
    },
    setCenter: async (w, m, k) => {
      const { width: y, height: b, maxZoom: S, panZoom: E } = g();
      if (!E)
        return !1;
      const j = typeof k?.zoom < "u" ? k.zoom : S;
      return await E.setViewport({
        x: y / 2 - w * j,
        y: b / 2 - m * j,
        zoom: j
      }, { duration: k?.duration, ease: k?.ease, interpolate: k?.interpolate }), !0;
    },
    cancelConnection: () => {
      p({
        connection: { ...al }
      });
    },
    updateConnection: (w) => {
      p({ connection: w });
    },
    reset: () => p({ ...Wa() })
  };
}, Object.is);
function wx({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: i, initialHeight: s, initialMinZoom: a, initialMaxZoom: c, initialFitViewOptions: d, fitView: l, nodeOrigin: u, nodeExtent: f, zIndexMode: h, children: p }) {
  const [g] = q(() => xx({
    nodes: e,
    edges: t,
    defaultNodes: n,
    defaultEdges: o,
    width: i,
    height: s,
    fitView: l,
    minZoom: a,
    maxZoom: c,
    fitViewOptions: d,
    nodeOrigin: u,
    nodeExtent: f,
    zIndexMode: h
  }));
  return r.jsx(Py, { value: g, children: r.jsx(rm, { children: p }) });
}
function vx({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: i, width: s, height: a, fitView: c, fitViewOptions: d, minZoom: l, maxZoom: u, nodeOrigin: f, nodeExtent: h, zIndexMode: p }) {
  return co(Ii) ? r.jsx(r.Fragment, { children: e }) : r.jsx(wx, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: i, initialWidth: s, initialHeight: a, fitView: c, initialFitViewOptions: d, initialMinZoom: l, initialMaxZoom: u, nodeOrigin: f, nodeExtent: h, zIndexMode: p, children: e });
}
const bx = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function Nx({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: i, nodeTypes: s, edgeTypes: a, onNodeClick: c, onEdgeClick: d, onInit: l, onMove: u, onMoveStart: f, onMoveEnd: h, onConnect: p, onConnectStart: g, onConnectEnd: v, onClickConnectStart: w, onClickConnectEnd: m, onNodeMouseEnter: k, onNodeMouseMove: y, onNodeMouseLeave: b, onNodeContextMenu: S, onNodeDoubleClick: E, onNodeDragStart: j, onNodeDrag: _, onNodeDragStop: D, onNodesDelete: B, onEdgesDelete: M, onDelete: T, onSelectionChange: L, onSelectionDragStart: N, onSelectionDrag: A, onSelectionDragStop: I, onSelectionContextMenu: $, onSelectionStart: z, onSelectionEnd: P, onBeforeDelete: F, connectionMode: K, connectionLineType: H = bt.Bezier, connectionLineStyle: Z, connectionLineComponent: X, connectionLineContainerStyle: ne, deleteKeyCode: se = "Backspace", selectionKeyCode: G = "Shift", selectionOnDrag: V = !1, selectionMode: Y = oo.Full, panActivationKeyCode: ae = "Space", multiSelectionKeyCode: ce = ro() ? "Meta" : "Control", zoomActivationKeyCode: J = ro() ? "Meta" : "Control", snapToGrid: ee, snapGrid: de, onlyRenderVisibleElements: O = !1, selectNodesOnDrag: Q, nodesDraggable: ge, autoPanOnNodeFocus: ve, nodesConnectable: Ee, nodesFocusable: $e, nodeOrigin: Ie = Vl, edgesFocusable: Xe, edgesReconnectable: be, elementsSelectable: Le = !0, defaultViewport: Ve = Yy, minZoom: Ze = 0.5, maxZoom: Ge = 2, translateExtent: Je = no, preventScrolling: Et = !0, nodeExtent: wn, defaultMarkerColor: Ye = "#b1b1b7", zoomOnScroll: Wt = !0, zoomOnPinch: vn = !0, panOnScroll: bn = !1, panOnScrollSpeed: wo = 0.5, panOnScrollMode: Nn = $t.Free, zoomOnDoubleClick: He = !0, panOnDrag: dt = !0, onPaneClick: ut, onPaneMouseEnter: Kt, onPaneMouseMove: Li, onPaneMouseLeave: jn, onPaneScroll: qt, onPaneContextMenu: mt, paneClickDistance: Ct = 1, nodeClickDistance: It = 0, children: Ue, onReconnect: Sn, onReconnectStart: vo, onReconnectEnd: Oe, onEdgeContextMenu: bo, onEdgeDoubleClick: we, onEdgeMouseEnter: Fe, onEdgeMouseMove: Xt, onEdgeMouseLeave: No, reconnectRadius: me = 10, onNodesChange: At, onEdgesChange: jo, noDragClassName: kn = "nodrag", noWheelClassName: Ae = "nowheel", noPanClassName: xt = "nopan", fitView: ft, fitViewOptions: So, connectOnClick: ko, attributionPosition: Eo, proOptions: En, defaultEdgeOptions: Vi, elevateNodesOnSelect: Cn = !0, elevateEdgesOnSelect: Yt = !1, disableKeyboardA11y: Me = !1, autoPanOnConnect: In, autoPanOnNodeDrag: An, autoPanOnSelection: _n = !0, autoPanSpeed: Ut, connectionRadius: Hi, isValidConnection: Oi, onError: Fi, style: Bi, id: Mn, nodeDragThreshold: Co, connectionDragThreshold: Io, viewport: Wi, onViewportChange: Dn, width: Ki, height: qi, colorMode: Zt = "light", debug: Gt, onScroll: Jt, ariaLabelConfig: Tn, zIndexMode: Ao = "basic", ...Xi }, Yi) {
  const $n = Mn || "1", Ui = Jy(Zt), Zi = ue((_o) => {
    _o.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), Jt?.(_o);
  }, [Jt]);
  return r.jsx("div", { "data-testid": "rf__wrapper", ...Xi, onScroll: Zi, style: { ...Bi, ...bx }, ref: Yi, className: Ce(["react-flow", i, Ui]), id: Mn, role: "application", children: r.jsxs(vx, { nodes: e, edges: t, width: Ki, height: qi, fitView: ft, fitViewOptions: So, minZoom: Ze, maxZoom: Ge, nodeOrigin: Ie, nodeExtent: wn, zIndexMode: Ao, children: [r.jsx(Gy, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: p, onConnectStart: g, onConnectEnd: v, onClickConnectStart: w, onClickConnectEnd: m, nodesDraggable: ge, autoPanOnNodeFocus: ve, nodesConnectable: Ee, nodesFocusable: $e, edgesFocusable: Xe, edgesReconnectable: be, elementsSelectable: Le, elevateNodesOnSelect: Cn, elevateEdgesOnSelect: Yt, minZoom: Ze, maxZoom: Ge, nodeExtent: wn, onNodesChange: At, onEdgesChange: jo, snapToGrid: ee, snapGrid: de, connectionMode: K, translateExtent: Je, connectOnClick: ko, defaultEdgeOptions: Vi, fitView: ft, fitViewOptions: So, onNodesDelete: B, onEdgesDelete: M, onDelete: T, onNodeDragStart: j, onNodeDrag: _, onNodeDragStop: D, onSelectionDrag: A, onSelectionDragStart: N, onSelectionDragStop: I, onMove: u, onMoveStart: f, onMoveEnd: h, noPanClassName: xt, nodeOrigin: Ie, rfId: $n, autoPanOnConnect: In, autoPanOnNodeDrag: An, autoPanSpeed: Ut, onError: Fi, connectionRadius: Hi, isValidConnection: Oi, selectNodesOnDrag: Q, nodeDragThreshold: Co, connectionDragThreshold: Io, onBeforeDelete: F, debug: Gt, ariaLabelConfig: Tn, zIndexMode: Ao }), r.jsx(yx, { onInit: l, onNodeClick: c, onEdgeClick: d, onNodeMouseEnter: k, onNodeMouseMove: y, onNodeMouseLeave: b, onNodeContextMenu: S, onNodeDoubleClick: E, nodeTypes: s, edgeTypes: a, connectionLineType: H, connectionLineStyle: Z, connectionLineComponent: X, connectionLineContainerStyle: ne, selectionKeyCode: G, selectionOnDrag: V, selectionMode: Y, deleteKeyCode: se, multiSelectionKeyCode: ce, panActivationKeyCode: ae, zoomActivationKeyCode: J, onlyRenderVisibleElements: O, defaultViewport: Ve, translateExtent: Je, minZoom: Ze, maxZoom: Ge, preventScrolling: Et, zoomOnScroll: Wt, zoomOnPinch: vn, zoomOnDoubleClick: He, panOnScroll: bn, panOnScrollSpeed: wo, panOnScrollMode: Nn, panOnDrag: dt, autoPanOnSelection: _n, onPaneClick: ut, onPaneMouseEnter: Kt, onPaneMouseMove: Li, onPaneMouseLeave: jn, onPaneScroll: qt, onPaneContextMenu: mt, paneClickDistance: Ct, nodeClickDistance: It, onSelectionContextMenu: $, onSelectionStart: z, onSelectionEnd: P, onReconnect: Sn, onReconnectStart: vo, onReconnectEnd: Oe, onEdgeContextMenu: bo, onEdgeDoubleClick: we, onEdgeMouseEnter: Fe, onEdgeMouseMove: Xt, onEdgeMouseLeave: No, reconnectRadius: me, defaultMarkerColor: Ye, noDragClassName: kn, noWheelClassName: Ae, noPanClassName: xt, rfId: $n, disableKeyboardA11y: Me, nodeExtent: wn, viewport: Wi, onViewportChange: Dn }), r.jsx(Xy, { onSelectionChange: L }), Ue, r.jsx(Fy, { proOptions: En, position: Eo }), r.jsx(Oy, { rfId: $n, disableKeyboardA11y: Me })] }) });
}
var gd = Kl(Nx);
const jx = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function Sx({ children: e }) {
  const t = he(jx);
  return t ? $y.createPortal(e, t) : null;
}
function kx({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return r.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ce(["react-flow__background-pattern", n, o]) });
}
function Ex({ radius: e, className: t }) {
  return r.jsx("circle", { cx: e, cy: e, r: e, className: Ce(["react-flow__background-pattern", "dots", t]) });
}
var Nt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(Nt || (Nt = {}));
const Cx = {
  [Nt.Dots]: 1,
  [Nt.Lines]: 1,
  [Nt.Cross]: 6
}, Ix = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function yd({
  id: e,
  variant: t = Nt.Dots,
  // only used for dots and cross
  gap: n = 20,
  // only used for lines and cross
  size: o,
  lineWidth: i = 1,
  offset: s = 0,
  color: a,
  bgColor: c,
  style: d,
  className: l,
  patternClassName: u
}) {
  const f = re(null), { transform: h, patternId: p } = he(Ix, Ne), g = o || Cx[t], v = t === Nt.Dots, w = t === Nt.Cross, m = Array.isArray(n) ? n : [n, n], k = [m[0] * h[2] || 1, m[1] * h[2] || 1], y = g * h[2], b = Array.isArray(s) ? s : [s, s], S = w ? [y, y] : k, E = [
    b[0] * h[2] || 1 + S[0] / 2,
    b[1] * h[2] || 1 + S[1] / 2
  ], j = `${p}${e || ""}`;
  return r.jsxs("svg", { className: Ce(["react-flow__background", l]), style: {
    ...d,
    ..._i,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": a
  }, ref: f, "data-testid": "rf__background", children: [r.jsx("pattern", { id: j, x: h[0] % k[0], y: h[1] % k[1], width: k[0], height: k[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${E[0]},-${E[1]})`, children: v ? r.jsx(Ex, { radius: y / 2, className: u }) : r.jsx(kx, { dimensions: S, lineWidth: i, variant: t, className: u }) }), r.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${j})` })] });
}
yd.displayName = "Background";
const md = ke(yd);
function Ax() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: r.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function _x() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: r.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function Mx() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: r.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function Dx() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: r.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function Tx() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: r.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function Ho({ children: e, className: t, ...n }) {
  return r.jsx("button", { type: "button", className: Ce(["react-flow__controls-button", t]), ...n, children: e });
}
const $x = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function xd({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: i, onZoomIn: s, onZoomOut: a, onFitView: c, onInteractiveChange: d, className: l, children: u, position: f = "bottom-left", orientation: h = "vertical", "aria-label": p }) {
  const g = je(), { isInteractive: v, minZoomReached: w, maxZoomReached: m, ariaLabelConfig: k } = he($x, Ne), { zoomIn: y, zoomOut: b, fitView: S } = as(), E = () => {
    y(), s?.();
  }, j = () => {
    b(), a?.();
  }, _ = () => {
    S(i), c?.();
  }, D = () => {
    g.setState({
      nodesDraggable: !v,
      nodesConnectable: !v,
      elementsSelectable: !v
    }), d?.(!v);
  }, B = h === "horizontal" ? "horizontal" : "vertical";
  return r.jsxs(Ai, { className: Ce(["react-flow__controls", B, l]), position: f, style: e, "data-testid": "rf__controls", "aria-label": p ?? k["controls.ariaLabel"], children: [t && r.jsxs(r.Fragment, { children: [r.jsx(Ho, { onClick: E, className: "react-flow__controls-zoomin", title: k["controls.zoomIn.ariaLabel"], "aria-label": k["controls.zoomIn.ariaLabel"], disabled: m, children: r.jsx(Ax, {}) }), r.jsx(Ho, { onClick: j, className: "react-flow__controls-zoomout", title: k["controls.zoomOut.ariaLabel"], "aria-label": k["controls.zoomOut.ariaLabel"], disabled: w, children: r.jsx(_x, {}) })] }), n && r.jsx(Ho, { className: "react-flow__controls-fitview", onClick: _, title: k["controls.fitView.ariaLabel"], "aria-label": k["controls.fitView.ariaLabel"], children: r.jsx(Mx, {}) }), o && r.jsx(Ho, { className: "react-flow__controls-interactive", onClick: D, title: k["controls.interactive.ariaLabel"], "aria-label": k["controls.interactive.ariaLabel"], children: v ? r.jsx(Tx, {}) : r.jsx(Dx, {}) }), u] });
}
xd.displayName = "Controls";
const wd = ke(xd);
function Px({ id: e, x: t, y: n, width: o, height: i, style: s, color: a, strokeColor: c, strokeWidth: d, className: l, borderRadius: u, shapeRendering: f, selected: h, onClick: p }) {
  const { background: g, backgroundColor: v } = s || {}, w = a || g || v;
  return r.jsx("rect", { className: Ce(["react-flow__minimap-node", { selected: h }, l]), x: t, y: n, rx: u, ry: u, width: o, height: i, style: {
    fill: w,
    stroke: c,
    strokeWidth: d
  }, shapeRendering: f, onClick: p ? (m) => p(m, e) : void 0 });
}
const zx = ke(Px), Rx = (e) => e.nodes.map((t) => t.id), pr = (e) => e instanceof Function ? e : () => e;
function Lx({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: i,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: s = zx,
  onClick: a
}) {
  const c = he(Rx, Ne), d = pr(t), l = pr(e), u = pr(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return r.jsx(r.Fragment, { children: c.map((h) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    r.jsx(Hx, { id: h, nodeColorFunc: d, nodeStrokeColorFunc: l, nodeClassNameFunc: u, nodeBorderRadius: o, nodeStrokeWidth: i, NodeComponent: s, onClick: a, shapeRendering: f }, h)
  )) });
}
function Vx({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: i, nodeStrokeWidth: s, shapeRendering: a, NodeComponent: c, onClick: d }) {
  const { node: l, x: u, y: f, width: h, height: p } = he((g) => {
    const v = g.nodeLookup.get(e);
    if (!v)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const w = v.internals.userNode, { x: m, y: k } = v.internals.positionAbsolute, { width: y, height: b } = yt(w);
    return {
      node: w,
      x: m,
      y: k,
      width: y,
      height: b
    };
  }, Ne);
  return !l || l.hidden || !pl(l) ? null : r.jsx(c, { x: u, y: f, width: h, height: p, style: l.style, selected: !!l.selected, className: o(l), color: t(l), borderRadius: i, strokeColor: n(l), strokeWidth: s, shapeRendering: a, onClick: d, id: l.id });
}
const Hx = ke(Vx);
var Ox = ke(Lx);
const Fx = 200, Bx = 150, Wx = (e) => !e.hidden, Kx = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? fl(ho(e.nodeLookup, { filter: Wx }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, qx = "react-flow__minimap-desc";
function vd({
  style: e,
  className: t,
  nodeStrokeColor: n,
  nodeColor: o,
  nodeClassName: i = "",
  nodeBorderRadius: s = 5,
  nodeStrokeWidth: a,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: c,
  bgColor: d,
  maskColor: l,
  maskStrokeColor: u,
  maskStrokeWidth: f,
  position: h = "bottom-right",
  onClick: p,
  onNodeClick: g,
  pannable: v = !1,
  zoomable: w = !1,
  ariaLabel: m,
  inversePan: k,
  zoomStep: y = 1,
  offsetScale: b = 5
}) {
  const S = je(), E = re(null), { boundingRect: j, viewBB: _, rfId: D, panZoom: B, translateExtent: M, flowWidth: T, flowHeight: L, ariaLabelConfig: N } = he(Kx, Ne), A = e?.width ?? Fx, I = e?.height ?? Bx, $ = j.width / A, z = j.height / I, P = Math.max($, z), F = P * A, K = P * I, H = b * P, Z = j.x - (F - j.width) / 2 - H, X = j.y - (K - j.height) / 2 - H, ne = F + H * 2, se = K + H * 2, G = `${qx}-${D}`, V = re(0), Y = re();
  V.current = P, oe(() => {
    if (E.current && B)
      return Y.current = sy({
        domNode: E.current,
        panZoom: B,
        getTransform: () => S.getState().transform,
        getViewScale: () => V.current
      }), () => {
        Y.current?.destroy();
      };
  }, [B]), oe(() => {
    Y.current?.update({
      translateExtent: M,
      width: T,
      height: L,
      inversePan: k,
      pannable: v,
      zoomStep: y,
      zoomable: w
    });
  }, [v, w, k, y, M, T, L]);
  const ae = p ? (ee) => {
    const [de, O] = Y.current?.pointer(ee) || [0, 0];
    p(ee, { x: de, y: O });
  } : void 0, ce = g ? ue((ee, de) => {
    const O = S.getState().nodeLookup.get(de).internals.userNode;
    g(ee, O);
  }, []) : void 0, J = m ?? N["minimap.ariaLabel"];
  return r.jsx(Ai, { position: h, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * P : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof a == "number" ? a : void 0
  }, className: Ce(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: r.jsxs("svg", { width: A, height: I, viewBox: `${Z} ${X} ${ne} ${se}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": G, ref: E, onClick: ae, children: [J && r.jsx("title", { id: G, children: J }), r.jsx(Ox, { onClick: ce, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: s, nodeClassName: i, nodeStrokeWidth: a, nodeComponent: c }), r.jsx("path", { className: "react-flow__minimap-mask", d: `M${Z - H},${X - H}h${ne + H * 2}v${se + H * 2}h${-ne - H * 2}z
        M${_.x},${_.y}h${_.width}v${_.height}h${-_.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
vd.displayName = "MiniMap";
const bd = ke(vd), Xx = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, Yx = {
  [hn.Line]: "right",
  [hn.Handle]: "bottom-right"
};
function Ux({ nodeId: e, position: t, variant: n = hn.Handle, className: o, style: i = void 0, children: s, color: a, minWidth: c = 10, minHeight: d = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: u = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: h, autoScale: p = !0, shouldResize: g, onResizeStart: v, onResize: w, onResizeEnd: m }) {
  const k = Ul(), y = typeof e == "string" ? e : k, b = je(), S = re(null), E = n === hn.Handle, j = he(ue(Xx(E && p), [E, p]), Ne), _ = re(null), D = t ?? Yx[n];
  oe(() => {
    if (!(!S.current || !y))
      return _.current || (_.current = wy({
        domNode: S.current,
        nodeId: y,
        getStoreItems: () => {
          const { nodeLookup: M, transform: T, snapGrid: L, snapToGrid: N, nodeOrigin: A, domNode: I } = b.getState();
          return {
            nodeLookup: M,
            transform: T,
            snapGrid: L,
            snapToGrid: N,
            nodeOrigin: A,
            paneDomNode: I
          };
        },
        onChange: (M, T) => {
          const { triggerNodeChanges: L, nodeLookup: N, parentLookup: A, nodeOrigin: I } = b.getState(), $ = [], z = { x: M.x, y: M.y }, P = N.get(y);
          if (P && P.expandParent && P.parentId) {
            const F = P.origin ?? I, K = M.width ?? P.measured.width ?? 0, H = M.height ?? P.measured.height ?? 0, Z = {
              id: P.id,
              parentId: P.parentId,
              rect: {
                width: K,
                height: H,
                ...gl({
                  x: M.x ?? P.position.x,
                  y: M.y ?? P.position.y
                }, { width: K, height: H }, P.parentId, N, F)
              }
            }, X = ss([Z], N, A, I);
            $.push(...X), z.x = M.x ? Math.max(F[0] * K, M.x) : void 0, z.y = M.y ? Math.max(F[1] * H, M.y) : void 0;
          }
          if (z.x !== void 0 && z.y !== void 0) {
            const F = {
              id: y,
              type: "position",
              position: { ...z }
            };
            $.push(F);
          }
          if (M.width !== void 0 && M.height !== void 0) {
            const K = {
              id: y,
              type: "dimensions",
              resizing: !0,
              setAttributes: h ? h === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: M.width,
                height: M.height
              }
            };
            $.push(K);
          }
          for (const F of T) {
            const K = {
              ...F,
              type: "position"
            };
            $.push(K);
          }
          L($);
        },
        onEnd: ({ width: M, height: T }) => {
          const L = {
            id: y,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: M,
              height: T
            }
          };
          b.getState().triggerNodeChanges([L]);
        }
      })), _.current.update({
        controlPosition: D,
        boundaries: {
          minWidth: c,
          minHeight: d,
          maxWidth: l,
          maxHeight: u
        },
        keepAspectRatio: f,
        resizeDirection: h,
        onResizeStart: v,
        onResize: w,
        onResizeEnd: m,
        shouldResize: g
      }), () => {
        _.current?.destroy();
      };
  }, [
    D,
    c,
    d,
    l,
    u,
    f,
    v,
    w,
    m,
    g
  ]);
  const B = D.split("-");
  return r.jsx("div", { className: Ce(["react-flow__resize-control", "nodrag", ...B, n, o]), ref: S, style: {
    ...i,
    scale: j,
    ...a && { [E ? "backgroundColor" : "borderColor"]: a }
  }, children: s });
}
ke(Ux);
const Zx = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), Nd = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var Gx = {
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
const Jx = wi(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: o,
    className: i = "",
    children: s,
    iconNode: a,
    ...c
  }, d) => vr(
    "svg",
    {
      ref: d,
      ...Gx,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: Nd("lucide", i),
      ...c
    },
    [
      ...a.map(([l, u]) => vr(l, u)),
      ...Array.isArray(s) ? s : [s]
    ]
  )
);
const pe = (e, t) => {
  const n = wi(
    ({ className: o, ...i }, s) => vr(Jx, {
      ref: s,
      iconNode: t,
      className: Nd(`lucide-${Zx(e)}`, o),
      ...i
    })
  );
  return n.displayName = `${e}`, n;
};
const Qx = pe("Activity", [
  [
    "path",
    {
      d: "M22 12h-2.48a2 2 0 0 0-1.93 1.46l-2.35 8.36a.25.25 0 0 1-.48 0L9.24 2.18a.25.25 0 0 0-.48 0l-2.35 8.36A2 2 0 0 1 4.49 12H2",
      key: "169zse"
    }
  ]
]);
const Mi = pe("Boxes", [
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
const xn = pe("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const e0 = pe("ChevronDown", [
  ["path", { d: "m6 9 6 6 6-6", key: "qrunsl" }]
]);
const li = pe("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const en = pe("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const kt = pe("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const t0 = pe("CodeXml", [
  ["path", { d: "m18 16 4-4-4-4", key: "1inbqp" }],
  ["path", { d: "m6 8-4 4 4 4", key: "15zrgr" }],
  ["path", { d: "m14.5 4-5 16", key: "e7oirm" }]
]);
const n0 = pe("Copy", [
  ["rect", { width: "14", height: "14", x: "8", y: "8", rx: "2", ry: "2", key: "17jyea" }],
  ["path", { d: "M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2", key: "zix9uf" }]
]);
const o0 = pe("Download", [
  ["path", { d: "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4", key: "ih7n3h" }],
  ["polyline", { points: "7 10 12 15 17 10", key: "2ggqvy" }],
  ["line", { x1: "12", x2: "12", y1: "15", y2: "3", key: "1vk2je" }]
]);
const Ka = pe("EyeOff", [
  [
    "path",
    {
      d: "M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49",
      key: "ct8e1f"
    }
  ],
  ["path", { d: "M14.084 14.158a3 3 0 0 1-4.242-4.242", key: "151rxh" }],
  [
    "path",
    {
      d: "M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143",
      key: "13bj9a"
    }
  ],
  ["path", { d: "m2 2 20 20", key: "1ooewy" }]
]);
const jd = pe("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const i0 = pe("GripVertical", [
  ["circle", { cx: "9", cy: "12", r: "1", key: "1vctgf" }],
  ["circle", { cx: "9", cy: "5", r: "1", key: "hp0tcf" }],
  ["circle", { cx: "9", cy: "19", r: "1", key: "fkjjf6" }],
  ["circle", { cx: "15", cy: "12", r: "1", key: "1tmaij" }],
  ["circle", { cx: "15", cy: "5", r: "1", key: "19l28e" }],
  ["circle", { cx: "15", cy: "19", r: "1", key: "f4zoj3" }]
]);
const r0 = pe("ListChecks", [
  ["path", { d: "m3 17 2 2 4-4", key: "1jhpwq" }],
  ["path", { d: "m3 7 2 2 4-4", key: "1obspn" }],
  ["path", { d: "M13 6h8", key: "15sg57" }],
  ["path", { d: "M13 12h8", key: "h98zly" }],
  ["path", { d: "M13 18h8", key: "oe0vm4" }]
]);
const ls = pe("ListTree", [
  ["path", { d: "M21 12h-8", key: "1bmf0i" }],
  ["path", { d: "M21 6H8", key: "1pqkrb" }],
  ["path", { d: "M21 18h-8", key: "1tm79t" }],
  ["path", { d: "M3 6v4c0 1.1.9 2 2 2h3", key: "1ywdgy" }],
  ["path", { d: "M3 10v6c0 1.1.9 2 2 2h3", key: "2wc746" }]
]);
const di = pe("Maximize2", [
  ["polyline", { points: "15 3 21 3 21 9", key: "mznyad" }],
  ["polyline", { points: "9 21 3 21 3 15", key: "1avn1i" }],
  ["line", { x1: "21", x2: "14", y1: "3", y2: "10", key: "ota7mn" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const qa = pe("Minimize2", [
  ["polyline", { points: "4 14 10 14 10 20", key: "11kfnr" }],
  ["polyline", { points: "20 10 14 10 14 4", key: "rlmsce" }],
  ["line", { x1: "14", x2: "21", y1: "10", y2: "3", key: "o5lafz" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const s0 = pe("Network", [
  ["rect", { x: "16", y: "16", width: "6", height: "6", rx: "1", key: "4q2zg0" }],
  ["rect", { x: "2", y: "16", width: "6", height: "6", rx: "1", key: "8cvhb9" }],
  ["rect", { x: "9", y: "2", width: "6", height: "6", rx: "1", key: "1egb70" }],
  ["path", { d: "M5 16v-3a1 1 0 0 1 1-1h12a1 1 0 0 1 1 1v3", key: "1jsf9p" }],
  ["path", { d: "M12 12V8", key: "2874zd" }]
]);
const Sd = pe("Package", [
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
const gn = pe("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const ui = pe("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const a0 = pe("Redo2", [
  ["path", { d: "m15 14 5-5-5-5", key: "12vg1m" }],
  ["path", { d: "M20 9H9.5A5.5 5.5 0 0 0 4 14.5A5.5 5.5 0 0 0 9.5 20H13", key: "6uklza" }]
]);
const ds = pe("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const kd = pe("Save", [
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
const Di = pe("Search", [
  ["circle", { cx: "11", cy: "11", r: "8", key: "4ej97u" }],
  ["path", { d: "m21 21-4.3-4.3", key: "1qie3q" }]
]);
const Xa = pe("Shield", [
  [
    "path",
    {
      d: "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z",
      key: "oel41y"
    }
  ]
]);
const us = pe("SlidersHorizontal", [
  ["line", { x1: "21", x2: "14", y1: "4", y2: "4", key: "obuewd" }],
  ["line", { x1: "10", x2: "3", y1: "4", y2: "4", key: "1q6298" }],
  ["line", { x1: "21", x2: "12", y1: "12", y2: "12", key: "1iu8h1" }],
  ["line", { x1: "8", x2: "3", y1: "12", y2: "12", key: "ntss68" }],
  ["line", { x1: "21", x2: "16", y1: "20", y2: "20", key: "14d8ph" }],
  ["line", { x1: "12", x2: "3", y1: "20", y2: "20", key: "m0wm8r" }],
  ["line", { x1: "14", x2: "14", y1: "2", y2: "6", key: "14e1ph" }],
  ["line", { x1: "8", x2: "8", y1: "10", y2: "14", key: "1i6ji0" }],
  ["line", { x1: "16", x2: "16", y1: "18", y2: "22", key: "1lctlv" }]
]);
const jt = pe("Sparkles", [
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
const c0 = pe("Terminal", [
  ["polyline", { points: "4 17 10 11 4 5", key: "akl6gq" }],
  ["line", { x1: "12", x2: "20", y1: "19", y2: "19", key: "q2wloq" }]
]);
const $r = pe("Trash2", [
  ["path", { d: "M3 6h18", key: "d0wm0j" }],
  ["path", { d: "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", key: "4alrt4" }],
  ["path", { d: "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", key: "v07s0e" }],
  ["line", { x1: "10", x2: "10", y1: "11", y2: "17", key: "1uufr5" }],
  ["line", { x1: "14", x2: "14", y1: "11", y2: "17", key: "xtxkd" }]
]);
const fi = pe("TriangleAlert", [
  [
    "path",
    {
      d: "m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3",
      key: "wmoenq"
    }
  ],
  ["path", { d: "M12 9v4", key: "juzpu7" }],
  ["path", { d: "M12 17h.01", key: "p32p05" }]
]);
const l0 = pe("Undo2", [
  ["path", { d: "M9 14 4 9l5-5", key: "102s5s" }],
  ["path", { d: "M4 9h10.5a5.5 5.5 0 0 1 5.5 5.5a5.5 5.5 0 0 1-5.5 5.5H11", key: "f3b9sd" }]
]);
const d0 = pe("Workflow", [
  ["rect", { width: "8", height: "8", x: "3", y: "3", rx: "2", key: "by2w9f" }],
  ["path", { d: "M7 11v4a2 2 0 0 0 2 2h4", key: "xkn7yn" }],
  ["rect", { width: "8", height: "8", x: "13", y: "13", rx: "2", key: "1cgmvn" }]
]);
const Ed = pe("X", [
  ["path", { d: "M18 6 6 18", key: "1bl5f8" }],
  ["path", { d: "m6 6 12 12", key: "d8bk6v" }]
]);
const u0 = pe("Zap", [
  [
    "path",
    {
      d: "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z",
      key: "1xq2db"
    }
  ]
]), f0 = /* @__PURE__ */ new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);
function Ti(e) {
  return Cd(e, h0);
}
function $i(e) {
  return Cd(e, p0);
}
function Cd(e, t) {
  return !e || !e.rootActivity ? e : { ...e, rootActivity: Id(e.rootActivity, t) };
}
function Id(e, t) {
  const n = t(e), o = n.structure;
  if (!o || !yn(o.payload)) return n;
  let i = !1;
  const s = { ...o.payload };
  for (const [a, c] of Object.entries(o.payload))
    Array.isArray(c) && c.length > 0 && c.every(w0) && (s[a] = c.map((d) => Id(d, t)), i = !0);
  return i ? { ...n, structure: { ...o, payload: s } } : n;
}
function h0(e) {
  const t = [], n = {};
  for (const [i, s] of Object.entries(e))
    f0.has(i) || (x0(s) ? t.push({
      referenceKey: g0(i),
      value: { value: m0(s.expression.value), expressionType: s.expression.type || "Literal" }
    }) : n[i] = s);
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
function p0(e) {
  const t = Array.isArray(e.inputs) ? e.inputs : [], n = {};
  for (const o of t) {
    if (!yn(o) || typeof o.referenceKey != "string") continue;
    const i = yn(o.value) ? o.value : {};
    n[y0(o.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof i.expressionType == "string" ? i.expressionType : "Literal",
        value: i.value ?? ""
      }
    };
  }
  return { ...e, ...n, inputs: [] };
}
function g0(e) {
  return e && e.charAt(0).toUpperCase() + e.slice(1);
}
function y0(e) {
  return e && e.charAt(0).toLowerCase() + e.slice(1);
}
function m0(e) {
  return e == null ? null : typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function x0(e) {
  if (!yn(e) || Array.isArray(e)) return !1;
  const t = e.expression;
  return yn(t) && typeof t.type == "string";
}
function w0(e) {
  return yn(e) && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function yn(e) {
  return typeof e == "object" && e !== null;
}
const _e = "/_elsa/workflow-management", v0 = "/publishing", Zn = {
  activities: ["workflows", "activities"],
  activityAvailabilitySettings: ["workflows", "activity-availability", "settings"],
  activityAvailabilityDiagnostics: ["workflows", "activity-availability", "diagnostics"]
};
function b0(e) {
  return Ic({
    queryKey: Zn.activityAvailabilitySettings,
    queryFn: () => R0(e)
  });
}
function N0(e) {
  return Ic({
    queryKey: Zn.activityAvailabilityDiagnostics,
    queryFn: () => Md(e)
  });
}
function j0(e) {
  const t = Lu();
  return Vu({
    mutationFn: (n) => L0(e, n),
    onSuccess: () => {
      t.invalidateQueries({ queryKey: Zn.activityAvailabilitySettings }), t.invalidateQueries({ queryKey: Zn.activityAvailabilityDiagnostics }), t.invalidateQueries({ queryKey: Zn.activities });
    }
  });
}
async function S0(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${_e}/definitions?${n.toString()}`);
}
async function k0(e, t) {
  const n = await e.http.getJson(`${_e}/definitions/${encodeURIComponent(t)}`);
  return n.draft ? { ...n, draft: { ...n.draft, state: $i(n.draft.state) } } : n;
}
async function E0(e, t) {
  const n = await e.http.getJson(`${_e}/versions/${encodeURIComponent(t)}`);
  return { ...n, state: $i(n.state) };
}
async function C0(e, t) {
  return e.http.postJson(`${_e}/definitions`, t);
}
async function I0(e, t) {
  await e.http.deleteJson(`${_e}/definitions/${encodeURIComponent(t)}`);
}
async function A0(e, t) {
  await e.http.postJson(`${_e}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function _0(e, t) {
  await e.http.deleteJson(`${_e}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function M0(e, t) {
  const n = await e.http.putJson(
    `${_e}/drafts/${encodeURIComponent(t.id)}`,
    { state: Ti(t.state), layout: t.layout }
  );
  return { ...n, state: $i(n.state) };
}
async function D0(e, t) {
  return e.http.postJson(`${_e}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function T0(e, t) {
  return e.http.postJson(`${_e}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function $0(e, t) {
  const n = { ...t, state: Ti(t.state) };
  try {
    return await e.http.postJson(`${v0}/workflows/drafts/test-runs`, n);
  } catch (o) {
    const i = O0(o);
    if (i) return i;
    throw o;
  }
}
async function Ad(e, t) {
  return e.http.postJson(`${_e}/executables/${encodeURIComponent(t)}/run`, {});
}
async function _d(e) {
  return e.http.getJson("/_demo/workflows/executables");
}
async function P0(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.runKind && n.set("runKind", t.runKind), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const o = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${o ? `?${o}` : ""}`);
}
async function z0(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function fs(e) {
  return e.http.getJson(`${_e}/activities`);
}
async function R0(e) {
  return e.http.getJson(`${_e}/activities/availability/settings`);
}
async function L0(e, t) {
  return e.http.putJson(`${_e}/activities/availability/settings`, t);
}
async function Md(e) {
  return e.http.getJson(`${_e}/activities/availability/diagnostics`);
}
async function V0(e) {
  const t = await Dd(e, [
    `${_e}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? Ya(t) : Ya(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function H0(e) {
  const t = await Dd(e, [
    `${_e}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : Yo;
}
async function Dd(e, t) {
  let n;
  for (const o of t)
    try {
      return await e.http.getJson(o);
    } catch (i) {
      n = i;
    }
  throw n;
}
function Ya(e) {
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
function O0(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = Ua(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return Ua(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function Ua(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const Yo = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
], F0 = [
  "Available",
  "BlockedByHostBaseline",
  "HiddenByManagementSettings",
  "RemovedFromCatalog",
  "UnresolvedReference"
], B0 = {
  Available: "Available",
  BlockedByHostBaseline: "Host blocked",
  HiddenByManagementSettings: "Management hidden",
  RemovedFromCatalog: "Removed",
  UnresolvedReference: "Unresolved"
};
function Pt(e) {
  return typeof e == "string" ? e : typeof e == "number" ? F0[e] ?? "Available" : "Available";
}
function hi(e) {
  const t = Pt(e);
  return B0[t] ?? t;
}
function W0(e) {
  return Pt(e).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function K0(e) {
  return Pt(e) !== "Available";
}
function q0(e) {
  return e === "Only" || e === 1 ? "Only" : "AllExcept";
}
function X0(e) {
  return e === "Only" ? 1 : 0;
}
function Za(e) {
  const t = e?.rules;
  return {
    mode: q0(e?.mode),
    activityTypes: t?.activityTypes ?? [],
    sets: t?.sets ?? []
  };
}
function Y0(e) {
  return e.referenceKind === 0 || e.referenceKind === "ActivityType";
}
function U0(e) {
  return [...e?.items ?? []].filter(Y0).filter((t) => t.activityTypeKey && t.activityDefinitionId).sort((t, n) => pi(t).localeCompare(pi(n)));
}
function Z0(e) {
  return [...e?.items ?? []].filter((t) => {
    const n = Pt(t.state);
    return n === "RemovedFromCatalog" || n === "UnresolvedReference";
  }).sort((t, n) => (t.referenceName ?? "").localeCompare(n.referenceName ?? ""));
}
function Ga(e, t) {
  return e.includes(t) ? e.filter((n) => n !== t) : [...e, t].sort((n, o) => n.localeCompare(o));
}
function pi(e) {
  const t = e?.displayName?.trim();
  if (t) return t;
  const n = e?.activityTypeKey?.split(".").filter(Boolean).at(-1) ?? "";
  return G0(n) || e?.activityTypeKey || "Activity";
}
function G0(e) {
  return e.replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").replace(/([a-z0-9])([A-Z])/g, "$1 $2").trim();
}
function J0(e, t) {
  const n = new Set(e.filter((o) => !!o));
  return (t?.items ?? []).find((o) => K0(o.state) ? [o.activityDefinitionId, o.activityTypeKey, o.referenceName].some((i) => i && n.has(i)) : !1) ?? null;
}
const Pi = "elsa.sequence.structure", yo = "elsa.flowchart.structure";
function Td(e, t) {
  if (!e) return null;
  let n = e;
  for (const o of t) {
    const i = ot(n).find((a) => a.id === o.slotId);
    if (!i) return null;
    const s = i.activities.find((a) => a.nodeId === o.ownerNodeId);
    if (!s) return null;
    n = s;
  }
  return n;
}
function Gn(e, t) {
  const n = Td(e, t);
  if (!n) return null;
  let o = ot(n)[0];
  return o ? { owner: n, slot: o } : null;
}
function ot(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = xw(t), i = gr(n.activities);
  return i ? [{
    id: `${t.kind}:activities`,
    label: ww(t),
    property: "activities",
    mode: o,
    activities: i
  }] : Object.entries(n).filter(([, s]) => gr(s)).map(([s, a]) => ({
    id: `${t.kind}:${s}`,
    label: bw(s),
    property: s,
    mode: "generic",
    activities: gr(a) ?? []
  }));
}
function $d(e, t, n) {
  const o = new Map(t.map((a) => [a.activityVersionId, a])), i = new Map(n.map((a) => [a.nodeId, a])), s = e.slot.activities.map((a, c) => {
    const d = o.get(a.activityVersionId), l = i.get(a.nodeId) ?? vw(e.slot.mode, c);
    return Rd(a, d, { x: l.x, y: l.y });
  });
  return {
    nodes: s,
    edges: e.slot.mode === "flowchart" ? dw(e.owner) : lw(e.slot, s)
  };
}
function Pr(e, t, n) {
  const o = t.find((s) => s.activityVersionId === e.activityVersionId), i = n.find((s) => s.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [Rd(e, o, { x: i.x, y: i.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function Q0(e, t, n, o = null) {
  const i = new Map(t.map((c) => [c.activityExecutionId, c])), s = Qa(t, (c) => c.authoredActivityId || c.executableNodeId), a = Qa(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? i.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const d = s.get(c.id) ?? [], l = a.get(c.id) ?? [];
    if (d.length === 0 && l.length === 0) return c;
    const u = gw(d), f = o === c.id || d.some((p) => p.activityExecutionId === o) || l.some((p) => p.incidentId === o), h = {
      status: u?.status,
      subStatus: u?.subStatus,
      activityExecutionId: u?.activityExecutionId,
      faultCount: d.reduce((p, g) => p + g.faultCount + g.aggregateFaultCount, 0),
      incidentCount: l.length,
      hasBlockingIncident: l.some((p) => p.isBlocking),
      selected: f
    };
    return {
      ...c,
      selected: f,
      className: f ? "wf-runtime-node-selected" : c.className,
      data: {
        ...c.data,
        runtime: h
      }
    };
  });
}
function hs(e, t) {
  return e?.structure?.kind === yo || iw(t) ? "flowchart" : e?.structure?.kind === Pi || rw(t) ? "sequence" : "unsupported";
}
function zr(e, t, n) {
  if (t.length === 0) {
    const c = ot(e)[0];
    return c ? ao(e, c, n) : e;
  }
  const [o, ...i] = t, s = ot(e).find((c) => c.id === o.slotId);
  if (!s) return e;
  const a = s.activities.map((c) => c.nodeId === o.ownerNodeId ? zr(c, i, n) : c);
  return ao(e, s, a);
}
function Pd(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...i] = t, s = ot(e).find((c) => c.id === o.slotId);
  if (!s) return e;
  const a = s.activities.map((c) => c.nodeId === o.ownerNodeId ? Pd(c, i, n) : c);
  return ao(e, s, a);
}
function zd(e, t, n) {
  if (e.nodeId === t) return n(e);
  const o = ot(e);
  if (o.length === 0) return e;
  let i = !1, s = e;
  for (const a of o) {
    const c = a.activities.map((d) => {
      const l = zd(d, t, n);
      return l !== d && (i = !0), l;
    });
    i && (s = ao(s, a, c));
  }
  return i ? s : e;
}
function ao(e, t, n) {
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
function ew(e, t, n, o = []) {
  const i = new Map(e.slot.activities.map((a) => [a.nodeId, a]));
  for (const a of o)
    i.set(a.nodeId, a);
  const s = t.map((a) => i.get(a.id)).filter((a) => !!a);
  return e.slot.mode === "sequence" && s.sort((a, c) => {
    const d = t.find((u) => u.id === a.nodeId), l = t.find((u) => u.id === c.nodeId);
    return (d?.position.x ?? 0) - (l?.position.x ?? 0);
  }), ao(e.owner, e.slot, s);
}
function tw(e, t) {
  return {
    ...e,
    structure: cw(e.structure, t)
  };
}
function nw(e, t) {
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
function Rr(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: aw(e)
  };
}
function Se(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? sw(t) : n;
}
function Rd(e, t, n, o = {}) {
  return {
    id: e.nodeId,
    type: "workflowActivity",
    position: n,
    connectable: o.connectable,
    deletable: o.deletable,
    draggable: o.draggable,
    data: {
      label: t ? Se(t) : e.activityVersionId,
      activityVersionId: e.activityVersionId,
      activityTypeKey: t?.activityTypeKey,
      category: t?.category,
      executionType: t?.executionType,
      icon: gi(t),
      childSlots: ot(e),
      acceptsInbound: uw(e, t),
      sourcePorts: o.suppressFlowPorts ? [] : Ld(e, t),
      suppressFlowPorts: o.suppressFlowPorts
    }
  };
}
function gi(e) {
  if (!e) return "activity";
  const t = ow(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), o = Se(e).toLowerCase(), i = e.category?.toLowerCase() ?? "", s = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || o === "flowchart" ? "flowchart" : n.endsWith(".sequence") || o === "sequence" ? "sequence" : n.includes("writeline") || o.includes("write line") ? "terminal" : i.includes("runtime") ? "runtime" : s === "trigger" ? "trigger" : "activity";
}
function ow(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function iw(e) {
  return !!e && (Se(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function rw(e) {
  return !!e && (Se(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function sw(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function aw(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: Pi,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: yo,
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
function cw(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], o = /* @__PURE__ */ new Map();
  for (const i of n) {
    if (!ps(i)) continue;
    const s = i.id;
    typeof s == "string" && o.set(s, i);
  }
  return {
    ...e,
    payload: {
      ...e.payload,
      connections: t.map((i) => {
        const s = o.get(i.id) ?? {}, a = i.data?.vertices, { vertices: c, ...d } = s;
        return {
          ...d,
          id: i.id,
          source: { nodeId: i.source, port: i.sourceHandle ?? "Done" },
          target: i.targetHandle ? { nodeId: i.target, port: i.targetHandle } : { nodeId: i.target },
          ...a?.length ? { vertices: a.map((l) => ({ x: Math.round(l.x), y: Math.round(l.y) })) } : {}
        };
      })
    }
  };
}
function lw(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function dw(e) {
  if (e.structure?.kind !== yo) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const i = n.source, s = n.target;
    if (!i?.nodeId || !s?.nodeId) return null;
    const a = Array.isArray(n.vertices) ? n.vertices.filter(yw) : [];
    return {
      id: typeof n.id == "string" ? String(n.id) : `flow-${o}-${i.nodeId}-${s.nodeId}`,
      source: i.nodeId,
      target: s.nodeId,
      sourceHandle: i.port,
      targetHandle: s.port && s.port !== "Done" ? s.port : void 0,
      type: "workflow",
      label: i.port && i.port !== "Done" ? i.port : void 0,
      data: a.length ? { vertices: a } : void 0
    };
  }).filter((n) => !!n) : [];
}
function Ld(e, t) {
  const n = Ja(e.cases);
  if (hw(e, t) && n.length > 0)
    return [...n.map((s) => ({ name: s, displayName: s })), { name: "Default", displayName: "Default" }];
  const o = [
    ...Uo(t?.designFacets),
    ...Uo(t?.ports),
    ...Uo(t?.outputs)
  ];
  if (o.length > 0) return pw(o);
  const i = Ja(e.outcomes);
  return i.length > 0 ? i.map((s) => ({ name: s, displayName: s })) : [{ name: "Done", displayName: "Done" }];
}
function uw(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function yi(e, t, n, o) {
  const i = n ?? "Done";
  return {
    id: `flow-${e}-${t}-${i}-${crypto.randomUUID().slice(0, 8)}`,
    source: e,
    target: t,
    sourceHandle: i,
    targetHandle: o ?? void 0,
    type: "workflow",
    label: i !== "Done" ? i : void 0
  };
}
function fw(e, t, n) {
  const o = yi(t.source, n, t.sourceHandle ?? "Done", void 0), i = yi(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((s) => s.id !== t.id).concat(o, i);
}
function gr(e) {
  return Array.isArray(e) ? e.filter(mw) : null;
}
function hw(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function Uo(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!ps(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...Uo(n.ports));
      continue;
    }
    const o = typeof n.type == "string" ? n.type : typeof n.portType == "string" ? n.portType : "", i = n.isBrowsable !== !1 && n.browsable !== !1, s = typeof n.name == "string" ? n.name : typeof n.id == "string" ? n.id : "";
    if (i && o.toLowerCase() === "flow" && s) {
      const a = typeof n.displayName == "string" ? n.displayName : s;
      t.push({ name: s, displayName: a });
    }
  }
  return t;
}
function pw(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function Ja(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function Qa(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const o of e) {
    const i = t(o);
    i && n.set(i, [...n.get(i) ?? [], o]);
  }
  return n;
}
function gw(e) {
  return [...e].sort((t, n) => ec(n).localeCompare(ec(t)))[0];
}
function ec(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function yw(e) {
  return ps(e) && typeof e.x == "number" && typeof e.y == "number";
}
function ps(e) {
  return typeof e == "object" && e !== null;
}
function mw(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function xw(e) {
  return e.kind === Pi ? "sequence" : e.kind === yo ? "flowchart" : "generic";
}
function ww(e) {
  return e.kind === Pi || e.kind === yo, "Activities";
}
function vw(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function bw(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Nw = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function Vd(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function gs(e) {
  return Vd(e.name);
}
function jw(e, t) {
  const n = gs(t), o = e[n];
  return t.isWrapped === !1 ? o ?? t.defaultValue ?? "" : Od(o, t);
}
function Hd(e, t) {
  return Od(e[gs(t)], t);
}
function Sw(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function kw(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function tc(e, t, n) {
  return {
    ...e,
    [gs(t)]: n
  };
}
function Ew(e, t) {
  return t.isWrapped === !1 ? jw(e, t) : Hd(e, t).expression.value;
}
function Od(e, t) {
  return Cw(e) ? {
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
function Cw(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
const Fd = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]);
function Iw({
  activity: e,
  descriptor: t,
  editors: n,
  expressionEditors: o,
  expressionDescriptors: i,
  descriptorStatus: s,
  onChange: a
}) {
  if (s === "loading")
    return /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Loading activity properties..." });
  if (!t)
    return /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No activity descriptor is available for this activity." });
  const c = t.inputs.filter((u) => u.isBrowsable !== !1).sort((u, f) => (u.order ?? 0) - (f.order ?? 0) || u.name.localeCompare(f.name));
  if (c.length === 0)
    return /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "This activity does not expose editable properties." });
  const d = $w(c), l = i.length > 0 ? i : Nw;
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ r.jsx("span", { className: "wf-section-label", children: "Properties" }),
    d.map((u) => /* @__PURE__ */ r.jsxs("section", { className: "wf-property-group", children: [
      d.length > 1 ? /* @__PURE__ */ r.jsx("h4", { children: u.category }) : null,
      u.inputs.map((f) => /* @__PURE__ */ r.jsx(
        Aw,
        {
          activity: e,
          input: f,
          editors: n,
          expressionEditors: o,
          expressionDescriptors: l,
          onChange: a
        },
        f.name
      ))
    ] }, u.category))
  ] });
}
function Aw({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: o,
  expressionDescriptors: i,
  onChange: s
}) {
  const a = t.isReadOnly === !0, c = { activity: e, expressionDescriptors: i, readOnly: a }, d = Dw(n, t, c), l = d?.component, u = t.isWrapped !== !1 ? Hd(e, t) : null, f = u?.expression.type ?? "Literal", h = Ew(e, t), p = u ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: i,
    readOnly: a,
    surface: "inline",
    syntax: f
  } : null, g = p ? Bd(o, p) : null, v = g?.surfaces.inline, w = g && p ? Wd(g, p, h) : [], m = !!(u && Pw(t, d?.id)), k = !!(u && zw(t, d?.id)), [y, b] = q(!1), S = (_) => {
    const D = u ? Sw(u, _) : _;
    s(tc(e, t, D));
  }, E = (_) => {
    u && s(tc(e, t, kw(u, _)));
  }, j = v && p ? /* @__PURE__ */ r.jsx(
    v,
    {
      descriptor: t,
      syntax: f,
      value: h,
      disabled: a,
      context: p,
      onChange: S
    }
  ) : Mw(l, t, h, a, c, S);
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ r.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ r.jsx("span", { children: Kd(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ r.jsx("p", { children: t.description }) : null,
    u && !m ? /* @__PURE__ */ r.jsx(
      Lr,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: f,
        descriptors: i,
        disabled: a,
        onChange: E
      }
    ) : null,
    m ? /* @__PURE__ */ r.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-expression-editor", children: [
        j,
        Vr(w)
      ] }),
      /* @__PURE__ */ r.jsx(
        Lr,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: f,
          descriptors: i,
          disabled: a,
          variant: "inline",
          onChange: E
        }
      ),
      k ? /* @__PURE__ */ r.jsx(
        "button",
        {
          type: "button",
          className: "wf-expression-expand-button",
          "aria-label": `Open expanded ${t.displayName || t.name} editor`,
          title: "Open expanded editor",
          onClick: () => b(!0),
          children: /* @__PURE__ */ r.jsx(di, { size: 13 })
        }
      ) : null
    ] }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
      j,
      Vr(w)
    ] }),
    k && !m ? /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => b(!0),
        children: [
          /* @__PURE__ */ r.jsx(di, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    y ? /* @__PURE__ */ r.jsx(
      _w,
      {
        input: t,
        value: h,
        syntax: f,
        descriptors: i,
        activity: e,
        expressionEditors: o,
        disabled: a,
        onChange: S,
        onSyntaxChange: E,
        onClose: () => b(!1)
      }
    ) : null
  ] });
}
function _w({
  input: e,
  value: t,
  syntax: n,
  descriptors: o,
  activity: i,
  expressionEditors: s,
  disabled: a,
  onChange: c,
  onSyntaxChange: d,
  onClose: l
}) {
  const u = Cc(), f = e.displayName || e.name, h = {
    activity: i,
    descriptor: e,
    expressionDescriptors: o,
    readOnly: a,
    surface: "expanded",
    syntax: n
  }, p = Bd(s, h), g = p?.surfaces.expanded, v = p ? Wd(p, h, t) : [], w = g ? null : Tw(s, h);
  return oe(() => {
    const m = (k) => {
      k.key === "Escape" && l();
    };
    return window.addEventListener("keydown", m), () => window.removeEventListener("keydown", m);
  }, [l]), /* @__PURE__ */ r.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ r.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": u, children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ r.jsx("h3", { id: u, children: f })
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": `Close ${f} editor`, onClick: l, children: /* @__PURE__ */ r.jsx(Ed, { size: 16 }) })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ r.jsx(
          Lr,
          {
            label: `${f} expression syntax`,
            value: n,
            descriptors: o,
            disabled: a,
            onChange: d
          }
        ),
        /* @__PURE__ */ r.jsx("span", { children: Kd(e.typeName) })
      ] }),
      e.description ? /* @__PURE__ */ r.jsx("p", { children: e.description }) : null,
      g ? /* @__PURE__ */ r.jsx(
        g,
        {
          descriptor: e,
          syntax: n,
          value: t,
          disabled: a,
          context: h,
          onChange: c
        }
      ) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
        w ? /* @__PURE__ */ r.jsx("p", { className: "wf-expression-editor-hint", children: w }) : null,
        /* @__PURE__ */ r.jsx(
          "textarea",
          {
            "aria-label": `${f} expanded value`,
            value: t == null ? "" : String(t),
            disabled: a,
            spellCheck: !1,
            onChange: (m) => c(m.target.value)
          }
        )
      ] }),
      Vr(v)
    ] }),
    /* @__PURE__ */ r.jsxs("footer", { children: [
      /* @__PURE__ */ r.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: l, children: "Close" })
    ] })
  ] }) });
}
function Mw(e, t, n, o, i, s) {
  return e ? /* @__PURE__ */ r.jsx(
    e,
    {
      descriptor: t,
      value: n,
      disabled: o,
      context: i,
      onChange: s
    }
  ) : /* @__PURE__ */ r.jsx("input", { type: "text", value: n == null ? "" : String(n), disabled: o, onChange: (a) => s(a.target.value) });
}
function Lr({
  label: e,
  value: t,
  descriptors: n,
  disabled: o,
  variant: i = "block",
  onChange: s
}) {
  const [a, c] = q(!1), d = Cc(), l = n.find((f) => f.type === t), u = [
    "wf-syntax-picker-trigger",
    i === "inline" ? "inline" : "",
    a ? "open" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ r.jsxs("div", { className: i === "inline" ? "wf-syntax-picker inline" : "wf-syntax-picker", onBlur: (f) => {
    f.currentTarget.contains(f.relatedTarget) || c(!1);
  }, children: [
    /* @__PURE__ */ r.jsx(
      "button",
      {
        type: "button",
        className: u,
        "aria-label": e,
        "aria-haspopup": "listbox",
        "aria-expanded": a,
        "aria-controls": d,
        disabled: o,
        onClick: () => c((f) => !f),
        children: /* @__PURE__ */ r.jsx("span", { children: l?.displayName || l?.type || t })
      }
    ),
    a ? /* @__PURE__ */ r.jsx("div", { id: d, role: "listbox", className: "wf-syntax-picker-menu", "aria-label": e, children: n.map((f) => {
      const h = f.displayName || f.type, p = f.type === t;
      return /* @__PURE__ */ r.jsx(
        "button",
        {
          type: "button",
          role: "option",
          "aria-selected": p,
          className: p ? "selected" : "",
          onClick: () => {
            s(f.type), c(!1);
          },
          children: h
        },
        f.type
      );
    }) }) : null
  ] });
}
function Dw(e, t, n) {
  return [...e].sort((o, i) => (o.order ?? 500) - (i.order ?? 500)).find((o) => o.supports(t, n));
}
function Bd(e, t) {
  return [...e].sort((n, o) => (n.order ?? 500) - (o.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function Wd(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function Tw(e, t) {
  if (t.syntax.toLowerCase() === "literal") return null;
  const n = [...e].sort((a, c) => (a.order ?? 500) - (c.order ?? 500)).find((a) => a.supports(t) && a.metadata)?.metadata;
  if (!n) return `No enhanced editor is registered for ${t.syntax}. Using the generic text editor.`;
  const o = n.displayName?.trim() || "enhanced editor", i = n.installHint?.trim(), s = `No ${o} is registered for ${t.syntax}. Using the generic text editor.`;
  return i ? `${s} ${i}` : s;
}
function Vr(e) {
  return e.length === 0 ? null : /* @__PURE__ */ r.jsx("div", { className: "wf-expression-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const o = t.severity ?? "info";
    return /* @__PURE__ */ r.jsxs("p", { className: `wf-expression-editor-diagnostic ${o}`, children: [
      t.code ? /* @__PURE__ */ r.jsx("span", { children: t.code }) : null,
      t.message
    ] }, `${t.code ?? "diagnostic"}-${n}`);
  }) });
}
function $w(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "General";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return [...t.entries()].map(([n, o]) => ({ category: n, inputs: o }));
}
function Kd(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function Pw(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !Fd.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function zw(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !Fd.has(t) && n !== "multiline") return !1;
  const o = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(o) || n === "singleline" || n === "multiline";
}
function Rw(e, t) {
  return {
    ...t ? { name: t } : {},
    definitionId: e.definitionId,
    state: Ti(e.state),
    layout: e.layout
  };
}
function Lw(e) {
  return JSON.stringify(
    {
      state: Ti(e.state),
      layout: e.layout
    },
    null,
    2
  );
}
function Vw(e, t) {
  let n;
  try {
    n = JSON.parse(e);
  } catch (i) {
    return { ok: !1, error: i instanceof Error ? i.message : String(i) };
  }
  if (!n || typeof n != "object")
    return { ok: !1, error: "Workflow JSON must be an object with a 'state' property." };
  const o = n;
  return !o.state || typeof o.state != "object" ? { ok: !1, error: "Workflow JSON is missing a valid 'state' object." } : o.layout !== void 0 && !Array.isArray(o.layout) ? { ok: !1, error: "'layout' must be an array when present." } : {
    ok: !0,
    draft: {
      ...t,
      state: $i(o.state),
      layout: o.layout ?? t.layout
    }
  };
}
function Hw(e, t) {
  const n = (t ?? "workflow").trim().replace(/[^\w.-]+/g, "-") || "workflow", o = new Blob([JSON.stringify(e, null, 2)], { type: "application/json" }), i = URL.createObjectURL(o), s = document.createElement("a");
  s.href = i, s.download = `${n}.json`, document.body.appendChild(s), s.click(), s.remove(), URL.revokeObjectURL(i);
}
const Ow = 320, Fw = 140;
function Bw(e, t, n) {
  return n === "sequence" ? Ww(e) : Kw(e, t);
}
function Ww(e) {
  const t = /* @__PURE__ */ new Map();
  return e.forEach((n, o) => {
    t.set(n.id, { x: o * 280, y: 0 });
  }), t;
}
function Kw(e, t) {
  const n = /* @__PURE__ */ new Map();
  if (e.length === 0) return n;
  const o = new Set(e.map((u) => u.id)), i = t.filter((u) => o.has(u.source) && o.has(u.target)), s = /* @__PURE__ */ new Set();
  for (const u of i)
    s.add(u.source), s.add(u.target);
  const a = /* @__PURE__ */ new Map();
  for (const u of e) a.set(u.id, 0);
  for (let u = 0; u < e.length; u += 1) {
    let f = !1;
    for (const h of i) {
      const p = (a.get(h.source) ?? 0) + 1;
      p > (a.get(h.target) ?? 0) && p <= e.length && (a.set(h.target, p), f = !0);
    }
    if (!f) break;
  }
  const c = Math.max(0, ...e.filter((u) => s.has(u.id)).map((u) => a.get(u.id) ?? 0)), d = s.size > 0 ? c + 1 : 0, l = /* @__PURE__ */ new Map();
  for (const u of e) {
    const f = s.has(u.id) ? a.get(u.id) ?? 0 : d, h = l.get(f);
    h ? h.push(u.id) : l.set(f, [u.id]);
  }
  for (const [u, f] of l)
    f.forEach((h, p) => {
      n.set(h, { x: u * Ow, y: p * Fw });
    });
  return n;
}
const qw = 50;
function nc() {
  return { past: [], future: [] };
}
function Xw(e) {
  return e.past.length > 0;
}
function Yw(e) {
  return e.future.length > 0;
}
function oc(e, t, n = qw) {
  const o = [...e.past, t];
  return o.length > n && o.splice(0, o.length - n), { past: o, future: [] };
}
function Uw(e, t) {
  if (e.past.length === 0) return null;
  const n = e.past.slice(), o = n.pop();
  return { history: { past: n, future: [...e.future, t] }, snapshot: o };
}
function Zw(e, t) {
  if (e.future.length === 0) return null;
  const n = e.future.slice(), o = n.pop();
  return { history: { past: [...e.past, t], future: n }, snapshot: o };
}
function it(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function qd(e, t) {
  if (!e || !t) return "";
  const n = Date.parse(e), o = Date.parse(t);
  if (Number.isNaN(n) || Number.isNaN(o) || o < n) return "";
  const i = Math.round((o - n) / 1e3);
  if (i < 60) return `${i}s`;
  const s = Math.floor(i / 60), a = i % 60;
  if (s < 60) return a ? `${s}m ${a}s` : `${s}m`;
  const c = Math.floor(s / 60), d = s % 60;
  return d ? `${c}h ${d}m` : `${c}h`;
}
function mi(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function ys(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ r.jsx(jd, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ r.jsx(ls, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ r.jsx(c0, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ r.jsx(gn, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ r.jsx(u0, { size: 15 });
    default:
      return /* @__PURE__ */ r.jsx(Mi, { size: 15 });
  }
}
var yr = { exports: {} }, Ln = {};
var ic;
function Gw() {
  if (ic) return Ln;
  ic = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function n(o, i, s) {
    var a = null;
    if (s !== void 0 && (a = "" + s), i.key !== void 0 && (a = "" + i.key), "key" in i) {
      s = {};
      for (var c in i)
        c !== "key" && (s[c] = i[c]);
    } else s = i;
    return i = s.ref, {
      $$typeof: e,
      type: o,
      key: a,
      ref: i !== void 0 ? i : null,
      props: s
    };
  }
  return Ln.Fragment = t, Ln.jsx = n, Ln.jsxs = n, Ln;
}
var rc;
function Jw() {
  return rc || (rc = 1, yr.exports = Gw()), yr.exports;
}
var Pe = Jw();
function sc({
  document: e,
  readOnly: t,
  minHeight: n,
  ariaLabel: o,
  onChange: i
}) {
  const s = (a) => {
    t || i({ ...e, value: a.target.value });
  };
  return /* @__PURE__ */ Pe.jsx(
    "textarea",
    {
      "aria-label": o,
      "aria-readonly": t,
      className: "studio-code-editor-input",
      readOnly: t,
      spellCheck: !1,
      autoCapitalize: "off",
      autoCorrect: "off",
      value: e.value,
      style: { minHeight: n },
      onChange: s
    }
  );
}
function Qw({
  document: e,
  diagnostics: t = [],
  readOnly: n = !1,
  theme: o = "studio",
  minHeight: i = "220px",
  ariaLabel: s,
  languageAdapter: a,
  onChange: c
}) {
  const d = t.filter((h) => !h.uri || h.uri === e.uri), l = a?.displayName ?? e.language, u = a?.loadEditor, f = fe(
    () => u ? zu(u) : null,
    [u]
  );
  return /* @__PURE__ */ Pe.jsxs(
    "section",
    {
      className: "studio-code-editor",
      "data-language": e.language,
      "data-theme": o,
      "data-readonly": n,
      children: [
        /* @__PURE__ */ Pe.jsxs("div", { className: "studio-code-editor-header", children: [
          /* @__PURE__ */ Pe.jsx("span", { children: l }),
          /* @__PURE__ */ Pe.jsx("code", { children: e.uri })
        ] }),
        f ? /* @__PURE__ */ Pe.jsx(Ru, { fallback: /* @__PURE__ */ Pe.jsx(
          sc,
          {
            document: e,
            readOnly: n,
            minHeight: i,
            ariaLabel: s,
            onChange: c
          }
        ), children: /* @__PURE__ */ Pe.jsx(
          f,
          {
            document: e,
            readOnly: n,
            theme: o,
            minHeight: i,
            ariaLabel: s,
            onChange: c
          }
        ) }) : /* @__PURE__ */ Pe.jsx(
          sc,
          {
            document: e,
            readOnly: n,
            minHeight: i,
            ariaLabel: s,
            onChange: c
          }
        ),
        /* @__PURE__ */ Pe.jsx(ev, { diagnostics: d })
      ]
    }
  );
}
function ev({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ Pe.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const o = t.severity ?? "info", i = tv(t);
    return /* @__PURE__ */ Pe.jsxs(
      "p",
      {
        className: `studio-code-editor-diagnostic ${o}`,
        children: [
          t.code ? /* @__PURE__ */ Pe.jsx("span", { children: t.code }) : null,
          i ? /* @__PURE__ */ Pe.jsx("small", { children: i }) : null,
          t.message
        ]
      },
      `${t.uri ?? "document"}-${t.code ?? "diagnostic"}-${n}`
    );
  }) });
}
function tv(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const nv = { language: "json", displayName: "JSON" };
function ov({ draft: e, onApply: t }) {
  const n = fe(() => Lw(e), [e]), [o, i] = q(n), [s, a] = q(n), [c, d] = q(null);
  oe(() => {
    i(n), a(n), d(null);
  }, [n]);
  const l = o !== s, u = c ? [{ severity: "error", message: c }] : [], f = () => d(t(o));
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-code-view", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-code-view-toolbar", children: [
      /* @__PURE__ */ r.jsx("span", { className: "wf-muted", children: "Edit the workflow definition as JSON. Changes apply to the draft when you click Apply." }),
      /* @__PURE__ */ r.jsxs("span", { className: "wf-code-view-actions", children: [
        /* @__PURE__ */ r.jsx("button", { type: "button", disabled: !l, onClick: () => {
          i(s), d(null);
        }, children: "Reset" }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: !l, onClick: f, children: [
          /* @__PURE__ */ r.jsx(xn, { size: 14 }),
          " Apply"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ r.jsx("div", { className: "wf-code-view-editor", children: /* @__PURE__ */ r.jsx(
      Qw,
      {
        ariaLabel: "Workflow JSON",
        document: { uri: "elsa://workflows/definition.json", language: "json", value: o },
        languageAdapter: nv,
        diagnostics: u,
        minHeight: "100%",
        theme: "studio",
        onChange: (h) => {
          i(h.value), c && d(null);
        }
      }
    ) })
  ] });
}
const iv = ["name", "Name"], rv = ["typeName", "TypeName", "type", "Type"], sv = ["value", "Value", "defaultValue", "DefaultValue"];
function av(e) {
  return !!e && typeof e == "object" && !Array.isArray(e);
}
function mr(e, t) {
  for (const n of t)
    if (n in e && e[n] != null) return String(e[n]);
  return "";
}
function xr({ title: e, emptyLabel: t, items: n }) {
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ r.jsx("h3", { children: e }),
    n.length === 0 ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: t }) : /* @__PURE__ */ r.jsxs("table", { className: "wf-properties-table", children: [
      /* @__PURE__ */ r.jsx("thead", { children: /* @__PURE__ */ r.jsxs("tr", { children: [
        /* @__PURE__ */ r.jsx("th", { children: "Name" }),
        /* @__PURE__ */ r.jsx("th", { children: "Type" }),
        /* @__PURE__ */ r.jsx("th", { children: "Default" })
      ] }) }),
      /* @__PURE__ */ r.jsx("tbody", { children: n.map((o, i) => {
        const s = av(o) ? o : null;
        if (!s)
          return /* @__PURE__ */ r.jsx("tr", { children: /* @__PURE__ */ r.jsx("td", { colSpan: 3, children: /* @__PURE__ */ r.jsx("code", { children: JSON.stringify(o) }) }) }, i);
        const a = mr(s, iv), c = mr(s, rv), d = mr(s, sv);
        return /* @__PURE__ */ r.jsxs("tr", { children: [
          /* @__PURE__ */ r.jsx("td", { children: a || /* @__PURE__ */ r.jsx("span", { className: "wf-muted", children: "—" }) }),
          /* @__PURE__ */ r.jsx("td", { children: c || /* @__PURE__ */ r.jsx("span", { className: "wf-muted", children: "—" }) }),
          /* @__PURE__ */ r.jsx("td", { children: d || /* @__PURE__ */ r.jsx("span", { className: "wf-muted", children: "—" }) })
        ] }, i);
      }) })
    ] })
  ] });
}
function cv({ details: e, draft: t }) {
  const n = t.state.variables ?? [], o = t.state.inputs ?? [], i = t.state.outputs ?? [], s = e?.versions ?? [], a = e?.definition.description?.trim();
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-properties-view", children: [
    /* @__PURE__ */ r.jsxs("section", { className: "wf-properties-section", children: [
      /* @__PURE__ */ r.jsx("h3", { children: "Information" }),
      /* @__PURE__ */ r.jsxs("dl", { className: "wf-properties-info", children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Name" }),
        /* @__PURE__ */ r.jsx("dd", { children: e?.definition.name ?? "—" }),
        /* @__PURE__ */ r.jsx("dt", { children: "Description" }),
        /* @__PURE__ */ r.jsx("dd", { children: a || /* @__PURE__ */ r.jsx("span", { className: "wf-muted", children: "No description" }) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Definition ID" }),
        /* @__PURE__ */ r.jsx("dd", { children: /* @__PURE__ */ r.jsx("code", { children: t.definitionId }) })
      ] }),
      /* @__PURE__ */ r.jsx("p", { className: "wf-muted wf-properties-hint", children: "Workflow metadata is read-only here for now. Editing variables, inputs and outputs is coming soon." })
    ] }),
    /* @__PURE__ */ r.jsx(xr, { title: "Variables", emptyLabel: "No variables defined.", items: n }),
    /* @__PURE__ */ r.jsx(xr, { title: "Inputs", emptyLabel: "No inputs defined.", items: o }),
    /* @__PURE__ */ r.jsx(xr, { title: "Outputs", emptyLabel: "No outputs defined.", items: i }),
    /* @__PURE__ */ r.jsxs("section", { className: "wf-properties-section", children: [
      /* @__PURE__ */ r.jsx("h3", { children: "Versions" }),
      s.length === 0 ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No published versions yet." }) : /* @__PURE__ */ r.jsx("ul", { className: "wf-properties-versions", children: s.map((c) => /* @__PURE__ */ r.jsxs("li", { children: [
        /* @__PURE__ */ r.jsxs("span", { className: "wf-properties-version-tag", children: [
          "v",
          c.version
        ] }),
        /* @__PURE__ */ r.jsx("time", { children: it(c.createdAt) })
      ] }, c.id)) })
    ] })
  ] });
}
function lv({ activities: e, activityCatalog: t, selectedEvidenceId: n = null, onSelectEvidence: o }) {
  const i = fe(
    () => new Map(t.map((a) => [a.activityTypeKey, a])),
    [t]
  ), s = fe(() => uv(e), [e]);
  return s.length === 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty wf-timeline-empty", children: "No activity executions recorded yet." }) : /* @__PURE__ */ r.jsx("ol", { className: "wf-timeline", "aria-label": "Execution timeline", children: s.map((a) => {
    const c = i.get(a.activityType), d = gi(c), l = c ? Se(c) : mi(a.activityType) ?? a.activityType, u = mi(a.activityType) ?? a.activityType, f = fv(a.startedAt ?? a.scheduledAt), h = qd(a.startedAt, a.completedAt);
    return /* @__PURE__ */ r.jsx("li", { children: /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-timeline-entry",
        "data-selected": a.activityExecutionId === n,
        onClick: () => o?.(a.activityExecutionId),
        children: [
          /* @__PURE__ */ r.jsx("span", { className: "wf-timeline-icon wf-activity-icon", "data-icon": d, "aria-hidden": "true", children: ys(d) }),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-timeline-body", children: [
            /* @__PURE__ */ r.jsx("strong", { title: l, children: l }),
            /* @__PURE__ */ r.jsx("small", { title: u, children: u })
          ] }),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-timeline-meta", children: [
            f ? /* @__PURE__ */ r.jsx("time", { children: f }) : null,
            h ? /* @__PURE__ */ r.jsxs("small", { children: [
              "took ",
              h
            ] }) : null
          ] }),
          /* @__PURE__ */ r.jsx(dv, { status: a.status })
        ]
      }
    ) }, a.activityExecutionId);
  }) });
}
function dv({ status: e }) {
  const t = e.toLowerCase(), n = t === "completed" ? "OK" : e;
  return /* @__PURE__ */ r.jsx("span", { className: "wf-status-badge wf-timeline-pill", "data-status": t, children: n });
}
function uv(e) {
  return e.map((t, n) => ({ activity: t, index: n })).sort((t, n) => ac(t.activity) - ac(n.activity) || t.index - n.index).map((t) => t.activity);
}
function ac(e) {
  const t = e.startedAt ?? e.scheduledAt, n = t ? Date.parse(t) : Number.NaN;
  return Number.isNaN(n) ? Number.MAX_SAFE_INTEGER : n;
}
function fv(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? "" : t.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: !1 });
}
function hv({ context: e }) {
  const t = b0(e), n = N0(e), o = j0(e), i = t.data ?? null, s = n.data ?? null, a = t.isLoading || n.isLoading, c = o.isPending, [d, l] = q(() => Za(i)), [u, f] = q(""), [h, p] = q(null);
  oe(() => {
    l(Za(i));
  }, [i]);
  const g = fe(() => U0(s), [s]), v = fe(() => Z0(s), [s]), w = s?.sets ?? [], m = fe(() => {
    const T = u.trim().toLowerCase();
    return T ? g.filter(
      (L) => pi(L).toLowerCase().includes(T) || (L.activityTypeKey ?? "").toLowerCase().includes(T)
    ) : g;
  }, [g, u]), k = new Set(d.activityTypes), y = new Set(d.sets), b = g.filter((T) => Pt(T.state) === "BlockedByHostBaseline").length, S = g.filter((T) => Pt(T.state) === "HiddenByManagementSettings").length, E = o.error ?? t.error ?? n.error, j = E instanceof Error ? E.message : E ? "Activity availability could not be loaded." : null, _ = (T) => l((L) => ({ ...L, mode: T })), D = (T) => l((L) => ({ ...L, activityTypes: Ga(L.activityTypes, T) })), B = (T) => l((L) => ({ ...L, sets: Ga(L.sets, T) })), M = () => {
    p(null), o.mutate(
      {
        scope: i?.scope ?? "host-default",
        mode: X0(d.mode),
        rules: { activityTypes: d.activityTypes, sets: d.sets }
      },
      { onSuccess: () => p("Activity availability saved.") }
    );
  };
  return /* @__PURE__ */ r.jsxs("div", { className: "availability-shell", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "availability-header", children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsxs("h2", { children: [
          /* @__PURE__ */ r.jsx(r0, { size: 18 }),
          " Activity availability"
        ] }),
        /* @__PURE__ */ r.jsx("p", { children: "Control which activities can be added to new workflows. Host baseline rules always take precedence." })
      ] }),
      /* @__PURE__ */ r.jsxs("button", { type: "button", className: "availability-save", onClick: M, disabled: a || c, children: [
        /* @__PURE__ */ r.jsx(kd, { size: 15 }),
        c ? "Saving…" : "Save"
      ] })
    ] }),
    j && /* @__PURE__ */ r.jsx("div", { className: "availability-error", children: j }),
    h && !j && /* @__PURE__ */ r.jsx("div", { className: "availability-status", children: h }),
    /* @__PURE__ */ r.jsxs("div", { className: "availability-mode", role: "group", "aria-label": "Activity availability mode", children: [
      /* @__PURE__ */ r.jsxs("button", { type: "button", className: d.mode === "AllExcept" ? "active" : "", onClick: () => _("AllExcept"), disabled: a || c, children: [
        /* @__PURE__ */ r.jsx(Ka, { size: 15 }),
        /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx("strong", { children: "All except" }),
          /* @__PURE__ */ r.jsx("em", { children: "Show everything except the selected activities" })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("button", { type: "button", className: d.mode === "Only" ? "active" : "", onClick: () => _("Only"), disabled: a || c, children: [
        /* @__PURE__ */ r.jsx(Xa, { size: 15 }),
        /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx("strong", { children: "Only" }),
          /* @__PURE__ */ r.jsx("em", { children: "Show only the selected activities" })
        ] })
      ] })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "availability-counts", children: [
      /* @__PURE__ */ r.jsxs("span", { children: [
        /* @__PURE__ */ r.jsx(Xa, { size: 14 }),
        " ",
        b,
        " host blocked"
      ] }),
      /* @__PURE__ */ r.jsxs("span", { children: [
        /* @__PURE__ */ r.jsx(Ka, { size: 14 }),
        " ",
        S,
        " management hidden"
      ] }),
      /* @__PURE__ */ r.jsxs("span", { children: [
        /* @__PURE__ */ r.jsx(fi, { size: 14 }),
        " ",
        v.length,
        " unresolved"
      ] })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "availability-body", children: [
      w.length > 0 && /* @__PURE__ */ r.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ r.jsxs("h3", { children: [
          /* @__PURE__ */ r.jsx(us, { size: 15 }),
          " Sets"
        ] }),
        /* @__PURE__ */ r.jsx("div", { className: "availability-set-list", children: w.map((T) => /* @__PURE__ */ r.jsxs("label", { className: "availability-set-option", children: [
          /* @__PURE__ */ r.jsx("input", { type: "checkbox", checked: y.has(T.name), disabled: a || c, onChange: () => B(T.name) }),
          /* @__PURE__ */ r.jsx("span", { children: T.name }),
          /* @__PURE__ */ r.jsx("code", { children: (T.activityTypeKeys ?? []).length })
        ] }, T.name)) })
      ] }),
      /* @__PURE__ */ r.jsxs("section", { className: "availability-section availability-section-grow", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "availability-section-head", children: [
          /* @__PURE__ */ r.jsxs("h3", { children: [
            /* @__PURE__ */ r.jsx(Qx, { size: 15 }),
            " Activities"
          ] }),
          /* @__PURE__ */ r.jsxs("div", { className: "availability-search", children: [
            /* @__PURE__ */ r.jsx(Di, { size: 14 }),
            /* @__PURE__ */ r.jsx("input", { type: "search", value: u, placeholder: "Filter activities…", onChange: (T) => f(T.target.value) })
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("div", { className: "availability-activity-list", children: [
          a && g.length === 0 && /* @__PURE__ */ r.jsx("p", { className: "availability-muted", children: "Loading availability…" }),
          !a && g.length === 0 && /* @__PURE__ */ r.jsx("p", { className: "availability-muted", children: "No availability diagnostics reported." }),
          !a && g.length > 0 && m.length === 0 && /* @__PURE__ */ r.jsx("p", { className: "availability-muted", children: "No activities match the filter." }),
          m.map((T) => {
            const N = Pt(T.state) === "BlockedByHostBaseline", A = T.activityTypeKey ?? T.activityDefinitionId ?? "";
            return /* @__PURE__ */ r.jsxs("label", { className: `availability-activity-option ${N ? "disabled" : ""}`, children: [
              /* @__PURE__ */ r.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: k.has(A),
                  disabled: a || c || N,
                  onChange: () => D(A)
                }
              ),
              /* @__PURE__ */ r.jsxs("span", { className: "availability-activity-main", children: [
                /* @__PURE__ */ r.jsx("strong", { children: pi(T) }),
                /* @__PURE__ */ r.jsx("code", { children: T.activityTypeKey })
              ] }),
              /* @__PURE__ */ r.jsx("em", { className: `availability-state ${W0(T.state)}`, children: hi(T.state) })
            ] }, A);
          })
        ] })
      ] }),
      v.length > 0 && /* @__PURE__ */ r.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ r.jsxs("h3", { children: [
          /* @__PURE__ */ r.jsx(fi, { size: 15 }),
          " Unresolved references"
        ] }),
        /* @__PURE__ */ r.jsx("div", { className: "availability-unresolved-list", children: v.map((T) => /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx("strong", { children: T.referenceName }),
          /* @__PURE__ */ r.jsx("em", { children: hi(T.state) })
        ] }, `${T.layer}-${T.referenceKind}-${T.referenceName}`)) })
      ] })
    ] })
  ] });
}
const cc = "elsa-studio:apply-workflow-graph-operation-batch", lc = "elsa-studio:undo-workflow-graph-operation-batch", pv = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function gv(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const o = jv(e), i = Yd(o.state.rootActivity), s = /* @__PURE__ */ new Map(), a = /* @__PURE__ */ new Map(), c = [];
  for (const d of t.operations) {
    const l = Nv(d.kind), u = d.parameters ?? {};
    if (l === "add-activity") {
      const f = ze(u.activityId) ?? d.temporaryReferences?.[0], h = bv(f ?? ze(u.displayName) ?? ze(u.activityType) ?? "weaver-activity", i), p = yv(d, h, n);
      a.set(h, p), c.push(h), f && s.set(f, h), o.state.rootActivity && mv(o.state.rootActivity, p);
      const g = zt(u.position) ? Hr(u.position, { x: 280, y: 160 }) : null;
      g && (o.layout = dc(o.layout, h, g));
      continue;
    }
    if (l === "set-root") {
      const f = wr(o, u.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown root activity.");
      o.state.rootActivity = f;
      continue;
    }
    if (l === "set-designer-position") {
      const f = Ft(u.activityId, s);
      if (!f || !ms(o.state.rootActivity, f)) throw new Error("Weaver batch referenced an unknown activity position.");
      o.layout = dc(o.layout, f, Hr(u, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const f = wr(o, u.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity property target.");
      vv(f, ze(u.propertyName) ?? "Value", u.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const f = wr(o, u.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity update target.");
      const h = zt(u.patch) ? u.patch : u;
      Object.assign(f, h);
      continue;
    }
    if (l === "remove-activity") {
      const f = Ft(u.activityId, s);
      if (!f) throw new Error("Weaver batch referenced an unknown activity remove target.");
      o.state.rootActivity = Xd(o.state.rootActivity, f), o.layout = o.layout.filter((h) => h.nodeId !== f);
      continue;
    }
    if (l === "connect-activities") {
      xv(o, u, s);
      continue;
    }
    if (l === "disconnect-activities") {
      wv(o, u, s);
      continue;
    }
    throw new Error(`Weaver batch operation '${String(d.kind || "unknown")}' is not supported by this designer apply path.`);
  }
  if (!o.state.rootActivity) throw new Error("Weaver batch did not produce a root activity.");
  return o.sourceVersionId = null, {
    draft: o,
    appliedCount: t.operations.length,
    finalActivityIds: c,
    temporaryReferences: Object.fromEntries(s),
    summary: `Applied ${t.operations.length} workflow operation${t.operations.length === 1 ? "" : "s"} to the working draft.`
  };
}
function yv(e, t, n) {
  const o = e.parameters ?? {}, i = ze(o.activityVersionId) ?? ze(o.activityType) ?? "Elsa.Workflows.Activity", s = n.find((a) => a.activityVersionId === i || a.activityTypeKey === i || a.displayName === ze(o.displayName));
  return s ? Rr(s, t) : {
    nodeId: t,
    activityVersionId: s?.activityVersionId ?? i,
    inputs: [],
    outputs: [],
    ...ze(o.displayName) ? { displayName: ze(o.displayName) } : {},
    designer: { position: Hr(o.position, { x: 280, y: 160 }) }
  };
}
function mv(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = xs(e);
  n && !n.some((o) => o.nodeId === t.nodeId) && n.push(t);
}
function xv(e, t, n) {
  const o = e.state.rootActivity;
  if (!o?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const i = Ft(t.sourceActivityId ?? t.sourceId ?? t.from, n), s = Ft(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!i || !s) throw new Error("Weaver batch connection is missing source or target activity.");
  const a = o.structure.payload, c = Array.isArray(a.connections) ? a.connections : [], d = ze(t.connectionId) ?? `flow-${i}-${s}`;
  a.connections = [
    ...c.filter((l) => !zt(l) || l.id !== d),
    {
      id: d,
      source: { nodeId: i, port: ze(t.outcome) ?? ze(t.sourcePort) ?? "Done" },
      target: { nodeId: s }
    }
  ];
}
function wv(e, t, n) {
  const o = e.state.rootActivity, i = o?.structure?.payload.connections;
  if (!Array.isArray(i)) return;
  const s = ze(t.connectionId), a = Ft(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = Ft(t.targetActivityId ?? t.targetId ?? t.to, n);
  o.structure.payload.connections = i.filter((d) => {
    if (!zt(d)) return !0;
    if (s && d.id === s) return !1;
    const l = zt(d.source) ? d.source.nodeId : void 0, u = zt(d.target) ? d.target.nodeId : void 0;
    return l !== a || u !== c;
  });
}
function vv(e, t, n) {
  e[Vd(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: "Literal", value: n }
  };
}
function wr(e, t, n, o) {
  const i = Ft(t, n);
  return i ? ms(e.state.rootActivity, i) ?? o.get(i) ?? null : null;
}
function Ft(e, t) {
  const n = ze(e);
  return n ? t.get(n) ?? n : null;
}
function ms(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of Ud(e)) {
    const o = ms(n, t);
    if (o) return o;
  }
  return null;
}
function Xd(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = xs(e);
  if (n) {
    const o = n.map((i) => Xd(i, t)).filter((i) => !!i);
    n.splice(0, n.length, ...o);
  }
  return e;
}
function Yd(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of Ud(e)) Yd(n, t);
  return t;
}
function Ud(e) {
  return xs(e) ?? [];
}
function xs(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function dc(e, t, n) {
  return [
    ...e.filter((o) => o.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function Hr(e, t) {
  const n = zt(e) ? e : {}, o = Number(n.x), i = Number(n.y);
  return {
    x: Number.isFinite(o) ? Math.max(40, Math.round(o)) : t.x,
    y: Number.isFinite(i) ? Math.max(40, Math.round(i)) : t.y
  };
}
function bv(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let o = n, i = 2;
  for (; t.has(o); )
    o = `${n}-${i}`, i += 1;
  return t.add(o), o;
}
function Nv(e) {
  return typeof e == "number" ? pv[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function ze(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function jv(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function zt(e) {
  return typeof e == "object" && e !== null;
}
function ws({ rows: e = 5 }) {
  return /* @__PURE__ */ r.jsx("div", { className: "wf-grid", "aria-busy": "true", "aria-label": "Loading", children: Array.from({ length: e }).map((t, n) => /* @__PURE__ */ r.jsx("div", { className: "wf-skeleton wf-skeleton-row", style: { width: `${90 - n % 3 * 12}%` } }, n)) });
}
function vs({ icon: e, title: t, description: n, action: o }) {
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-empty-state", role: "status", children: [
    /* @__PURE__ */ r.jsx("div", { className: "wf-empty-state-icon", "aria-hidden": !0, children: e ?? /* @__PURE__ */ r.jsx(Mi, { size: 22 }) }),
    /* @__PURE__ */ r.jsx("h3", { children: t }),
    n ? /* @__PURE__ */ r.jsx("p", { children: n }) : null,
    o ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty-state-action", children: o }) : null
  ] });
}
function mo({ message: e, title: t = "Something went wrong" }) {
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-error-card", role: "alert", children: [
    /* @__PURE__ */ r.jsx(kt, { size: 18 }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-error-card-body", children: [
      /* @__PURE__ */ r.jsx("strong", { children: t }),
      /* @__PURE__ */ r.jsx("span", { children: e || "Please try again, or check that the Elsa server is reachable." })
    ] })
  ] });
}
const Zd = { workflowActivity: d1 }, Gd = { workflow: f1 }, uc = "application/x-elsa-activity-version-id", Sv = 6, kv = 1200, Ev = 250, Cv = [10, 25, 50], Iv = 10, fc = "elsa-studio-workflow-palette-width", hc = "elsa-studio-workflow-inspector-width", pc = "elsa-studio-workflow-palette-collapsed", gc = "elsa-studio-workflow-inspector-collapsed", Jd = "elsa-studio-workflow-side-panel-maximized", Vn = 180, Hn = 460, Av = 260, On = 260, Fn = 560, _v = 320, yc = 42, Oo = 16, Qd = ct.createContext(null), eu = ct.createContext(null);
let Or;
function C1(e) {
  Or = e.dialogs, e.featureAreas.add({
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
        { title: "Runs", path: "/workflows/instances", iconColor: "#0ea5e9" },
        { title: "Activity Availability", path: "/workflows/activity-availability", iconColor: "#0ea5e9" }
      ]
    },
    routes: [
      {
        id: "workflows-definitions",
        path: "/workflows/definitions",
        label: "Workflow definitions",
        component: () => /* @__PURE__ */ r.jsx(Mv, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list() })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ r.jsx(Dv, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => /* @__PURE__ */ r.jsx(Tv, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => /* @__PURE__ */ r.jsx($v, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => /* @__PURE__ */ r.jsx(hv, { context: e.backend })
      }
    ]
  });
}
function Mv({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: o,
  workflowDesignerPanels: i
}) {
  const [s, a] = q(mc);
  oe(() => {
    const d = () => a(mc());
    return window.addEventListener("popstate", d), () => window.removeEventListener("popstate", d);
  }, []);
  const c = (d) => {
    const l = d ? `/workflows/definitions?definition=${encodeURIComponent(d)}` : "/workflows/definitions";
    window.history.pushState({}, "", l), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return s ? /* @__PURE__ */ r.jsx(l1, { context: e, definitionId: s, ai: t, propertyEditors: n, expressionEditors: o, workflowDesignerPanels: i, onBack: () => c(null) }) : /* @__PURE__ */ r.jsx(zi, { title: "Definitions", children: /* @__PURE__ */ r.jsx(zv, { context: e, ai: t, onOpen: c }) });
}
function Dv({ context: e, ai: t }) {
  const [n, o] = q(xc);
  oe(() => {
    const s = () => o(xc());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const i = ue((s) => {
    const a = s?.trim() ?? "", c = new URL(window.location.href);
    a ? c.searchParams.set("definition", a) : c.searchParams.delete("definition"), o(a || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ r.jsx(zi, { title: "Executables", children: /* @__PURE__ */ r.jsx(Lv, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: i }) });
}
function Tv({ context: e, ai: t }) {
  return /* @__PURE__ */ r.jsx(zi, { title: "Runs", children: /* @__PURE__ */ r.jsx(Ov, { context: e, ai: t }) });
}
function $v({ context: e, ai: t }) {
  const n = Pv();
  return /* @__PURE__ */ r.jsx(zi, { title: "Run", children: /* @__PURE__ */ r.jsx(Fv, { context: e, ai: t, workflowExecutionId: n }) });
}
function zi({ title: e, children: t }) {
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ r.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ r.jsxs("div", { children: [
      /* @__PURE__ */ r.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ r.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function mc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function xc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Pv() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function zv({ context: e, ai: t, onOpen: n }) {
  const [o, i] = q(""), [s, a] = q("active"), [c, d] = q(1), [l, u] = q(Iv), [f, h] = q("loading"), [p, g] = q(""), [v, w] = q(""), [m, k] = q([]), [y, b] = q(0), [S, E] = q(() => /* @__PURE__ */ new Set()), [j, _] = q(null), [D, B] = q(!1), [M, T] = q([]), [L, N] = q("idle"), A = re(null), I = fe(() => m.map((O) => O.id), [m]), $ = Bt(t, "weaver.workflows.suggest-create-metadata"), z = Bt(t, "weaver.workflows.explain-definition"), P = I.filter((O) => S.has(O)).length, F = I.length > 0 && P === I.length, K = ue(async () => {
    h("loading"), g("");
    try {
      const O = await S0(e, { search: o, state: s, page: c, pageSize: l }), Q = typeof O.totalCount == "number", ge = O.totalCount ?? O.definitions.length, ve = ou(ge, l);
      if (ge > 0 && c > ve) {
        d(ve);
        return;
      }
      k(Q ? O.definitions : Zv(O.definitions, c, l)), b(ge), h("ready");
    } catch (O) {
      g(O instanceof Error ? O.message : String(O)), h("failed");
    }
  }, [e, o, s, c, l]);
  oe(() => {
    K();
  }, [K]), oe(() => {
    A.current && (A.current.indeterminate = P > 0 && !F);
  }, [F, P]);
  const H = ue(async () => {
    if (!(L === "loading" || L === "ready")) {
      N("loading");
      try {
        const O = await fs(e);
        T(O.activities ?? []), N("ready");
      } catch (O) {
        N("failed"), g(O instanceof Error ? O.message : String(O));
      }
    }
  }, [L, e]), Z = () => {
    g(""), w(""), _({ name: "", description: "", rootKind: "flowchart" }), H();
  }, X = async () => {
    if (j?.name.trim()) {
      B(!0), g(""), w("");
      try {
        const O = await C0(e, {
          name: j.name.trim(),
          description: j.description.trim() || null,
          rootKind: j.rootKind,
          rootActivityVersionId: Qv(j, M)
        });
        _(null), n(O.definition.id);
      } catch (O) {
        g(O instanceof Error ? O.message : String(O));
      } finally {
        B(!1);
      }
    }
  }, ne = (O) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(O)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, se = async () => {
    if (m.length === 1 && c > 1) {
      d(c - 1);
      return;
    }
    await K();
  }, G = () => E(/* @__PURE__ */ new Set()), V = (O, Q) => {
    E((ge) => {
      const ve = new Set(ge);
      return Q ? ve.add(O) : ve.delete(O), ve;
    });
  }, Y = (O) => {
    E((Q) => {
      const ge = new Set(Q);
      for (const ve of I)
        O ? ge.add(ve) : ge.delete(ve);
      return ge;
    });
  }, ae = (O) => {
    a(O), d(1), G();
  }, ce = (O) => {
    i(O), d(1), G();
  }, J = async (O) => {
    if (await Or.confirm({ message: `Delete workflow definition "${O.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" })) {
      w(""), g("");
      try {
        await I0(e, O.id), V(O.id, !1), w(`Deleted ${O.name}`), await se();
      } catch (Q) {
        g(Q instanceof Error ? Q.message : String(Q));
      }
    }
  }, ee = async (O) => {
    w(""), g("");
    try {
      await A0(e, O.id), V(O.id, !1), w(`Restored ${O.name}`), await se();
    } catch (Q) {
      g(Q instanceof Error ? Q.message : String(Q));
    }
  }, de = async (O) => {
    if (await Or.confirm({ message: `Permanently delete workflow definition "${O.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) {
      w(""), g("");
      try {
        await _0(e, O.id), V(O.id, !1), w(`Permanently deleted ${O.name}`), await se();
      } catch (Q) {
        g(Q instanceof Error ? Q.message : String(Q));
      }
    }
  };
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ r.jsx("button", { type: "button", className: s === "active" ? "active" : "", "aria-selected": s === "active", onClick: () => ae("active"), children: "Active" }),
        /* @__PURE__ */ r.jsx("button", { type: "button", className: s === "deleted" ? "active" : "", "aria-selected": s === "deleted", onClick: () => ae("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ r.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ r.jsx(Di, { size: 15 }),
        /* @__PURE__ */ r.jsx("input", { value: o, onChange: (O) => ce(O.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => {
        K();
      }, children: "Refresh" }),
      /* @__PURE__ */ r.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ r.jsxs("button", { type: "button", title: "Create workflow", onClick: Z, children: [
        /* @__PURE__ */ r.jsx(ui, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ r.jsx(mo, { message: p, title: "Couldn't load workflow definitions" }) : null,
    f !== "failed" && p ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ r.jsx(kt, { size: 16 }),
      " ",
      p
    ] }) : null,
    v ? /* @__PURE__ */ r.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ r.jsx(xn, { size: 14 }),
      " ",
      v
    ] }) : null,
    S.size > 0 ? /* @__PURE__ */ r.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ r.jsxs("span", { children: [
        S.size,
        " selected"
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: G, children: "Clear selection" })
    ] }) : null,
    f === "loading" ? /* @__PURE__ */ r.jsx(ws, {}) : null,
    f === "ready" && m.length === 0 ? /* @__PURE__ */ r.jsx(
      vs,
      {
        icon: /* @__PURE__ */ r.jsx(Sd, { size: 22 }),
        title: `No ${s} workflow definitions`,
        description: "Create a workflow to start designing automation, or adjust your filters to see more.",
        action: /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-link-button", onClick: Z, children: [
          /* @__PURE__ */ r.jsx(ui, { size: 15 }),
          " Create workflow"
        ] })
      }
    ) : null,
    f === "ready" && m.length > 0 ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ r.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ r.jsx(
            "input",
            {
              ref: A,
              type: "checkbox",
              checked: F,
              onChange: (O) => Y(O.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ r.jsx("span", { children: "Name" }),
          /* @__PURE__ */ r.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ r.jsx("span", { children: s === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ r.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ r.jsx("span", { children: "Actions" })
        ] }),
        m.map((O) => /* @__PURE__ */ r.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${O.name}`,
            "aria-selected": S.has(O.id),
            tabIndex: 0,
            onClick: () => n(O.id),
            onKeyDown: (Q) => {
              Q.currentTarget === Q.target && (Q.key !== "Enter" && Q.key !== " " || (Q.preventDefault(), n(O.id)));
            },
            children: [
              /* @__PURE__ */ r.jsx("label", { className: "wf-row-select", onClick: (Q) => Q.stopPropagation(), children: /* @__PURE__ */ r.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: S.has(O.id),
                  onChange: (Q) => V(O.id, Q.target.checked),
                  "aria-label": `Select workflow definition ${O.name}`
                }
              ) }),
              /* @__PURE__ */ r.jsxs("span", { children: [
                /* @__PURE__ */ r.jsx("strong", { children: O.name }),
                /* @__PURE__ */ r.jsx("small", { children: O.description || O.id })
              ] }),
              /* @__PURE__ */ r.jsx("span", { children: O.latestVersion ?? "No version" }),
              /* @__PURE__ */ r.jsx("span", { children: s === "deleted" ? it(O.deletedAt) : O.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ r.jsx("span", { children: it(O.lastModifiedAt) }),
              /* @__PURE__ */ r.jsx("span", { className: "wf-row-actions", onClick: (Q) => Q.stopPropagation(), children: s === "active" ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
                /* @__PURE__ */ r.jsx("button", { type: "button", onClick: (Q) => {
                  Q.stopPropagation(), n(O.id);
                }, children: "Open" }),
                /* @__PURE__ */ r.jsx("button", { type: "button", onClick: (Q) => {
                  Q.stopPropagation(), ne(O.id);
                }, children: "Artifacts" }),
                z ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => St(t, z, O), children: [
                  /* @__PURE__ */ r.jsx(jt, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ r.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  J(O);
                }, children: [
                  /* @__PURE__ */ r.jsx($r, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
                /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
                  ee(O);
                }, children: [
                  /* @__PURE__ */ r.jsx(ds, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ r.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  de(O);
                }, children: [
                  /* @__PURE__ */ r.jsx($r, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          O.id
        ))
      ] }),
      /* @__PURE__ */ r.jsx(
        Uv,
        {
          page: c,
          pageSize: l,
          totalCount: y,
          onPageChange: d,
          onPageSizeChange: (O) => {
            u(O), d(1);
          }
        }
      )
    ] }) : null,
    j ? /* @__PURE__ */ r.jsx(
      Rv,
      {
        draft: j,
        activities: M,
        catalogState: L,
        creating: D,
        suggestMetadataAction: $,
        onSuggestMetadata: $ ? () => St(t, $, { draft: j, activities: M }) : void 0,
        onChange: (O) => _(O),
        onClose: () => _(null),
        onSubmit: X
      }
    ) : null
  ] });
}
function Rv({ draft: e, activities: t, catalogState: n, creating: o, suggestMetadataAction: i, onSuggestMetadata: s, onChange: a, onClose: c, onSubmit: d }) {
  const l = fe(() => Gv(t), [t]), u = Jv(e, t), f = (h) => {
    if (h.startsWith("kind:")) {
      a({ ...e, rootKind: h.slice(5), rootActivityVersionId: null });
      return;
    }
    const p = t.find((g) => g.activityVersionId === h);
    a({
      ...e,
      rootKind: iu(p) ?? e.rootKind,
      rootActivityVersionId: h
    });
  };
  return /* @__PURE__ */ r.jsx("div", { className: "wf-dialog-backdrop", role: "presentation", children: /* @__PURE__ */ r.jsx("section", { className: "wf-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "workflow-create-title", children: /* @__PURE__ */ r.jsxs(
    "form",
    {
      onSubmit: (h) => {
        h.preventDefault(), d();
      },
      children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-dialog-heading", children: [
          /* @__PURE__ */ r.jsx("h3", { id: "workflow-create-title", children: "Create Workflow" }),
          i ? /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-ai-action", onClick: s, title: i.description ?? i.label, children: [
            /* @__PURE__ */ r.jsx(jt, { size: 13 }),
            " ",
            i.label
          ] }) : null
        ] }),
        /* @__PURE__ */ r.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ r.jsx("span", { children: "Display name" }),
          /* @__PURE__ */ r.jsx(
            "input",
            {
              autoFocus: !0,
              "aria-label": "Display name",
              value: e.name,
              onChange: (h) => a({ ...e, name: h.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ r.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ r.jsx("span", { children: "Description" }),
          /* @__PURE__ */ r.jsx(
            "textarea",
            {
              "aria-label": "Description",
              rows: 3,
              value: e.description,
              onChange: (h) => a({ ...e, description: h.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ r.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ r.jsx("span", { children: "Root activity" }),
          /* @__PURE__ */ r.jsxs(
            "select",
            {
              "aria-label": "Root activity",
              value: u,
              onChange: (h) => f(h.target.value),
              disabled: n === "loading",
              children: [
                /* @__PURE__ */ r.jsx("optgroup", { label: "Composite roots", children: l.compositeRoots.map((h) => /* @__PURE__ */ r.jsx("option", { value: h.value, children: h.label }, h.value)) }),
                l.otherCategories.map((h) => /* @__PURE__ */ r.jsx("optgroup", { label: h.name, children: h.activities.map((p) => /* @__PURE__ */ r.jsx("option", { value: p.activityVersionId, children: Se(p) }, p.activityVersionId)) }, h.name))
              ]
            }
          )
        ] }),
        n === "loading" ? /* @__PURE__ */ r.jsx("div", { className: "wf-dialog-note", children: "Loading activity catalog..." }) : null,
        n === "failed" ? /* @__PURE__ */ r.jsx("div", { className: "wf-dialog-note", children: "Activity catalog could not be loaded. Composite roots remain available." }) : null,
        /* @__PURE__ */ r.jsxs("div", { className: "wf-dialog-actions", children: [
          /* @__PURE__ */ r.jsx("button", { type: "button", onClick: c, disabled: o, children: "Cancel" }),
          /* @__PURE__ */ r.jsx("button", { type: "submit", disabled: o || !e.name.trim(), children: o ? "Creating..." : "Create" })
        ] })
      ]
    }
  ) }) });
}
function Lv({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: o }) {
  const [i, s] = q("loading"), [a, c] = q(""), [d, l] = q(""), [u, f] = q(null), [h, p] = q([]), g = n?.trim().toLowerCase() ?? "", v = fe(
    () => g ? h.filter((j) => t1(j, g)) : h,
    [g, h]
  ), w = fe(
    () => Array.from(new Set(h.flatMap((j) => [
      j.definitionId,
      j.definitionVersionId,
      j.sourceId
    ]).filter((j) => !!j))).sort((j, _) => j.localeCompare(_)),
    [h]
  ), m = Bt(t, "weaver.workflows.explain-executable"), k = ue(async () => {
    s("loading"), c("");
    try {
      p(await _d(e)), s("ready");
    } catch (j) {
      c(j instanceof Error ? j.message : String(j)), s("failed");
    }
  }, [e]);
  oe(() => {
    k();
  }, [k]);
  const y = async (j) => {
    l(""), f(null), c("");
    try {
      const _ = await Ad(e, j.artifactId), D = du(_);
      f({ artifactId: j.artifactId, workflowExecutionId: D }), l(`Started ${j.artifactId}`);
    } catch (_) {
      c(_ instanceof Error ? _.message : String(_));
    }
  }, b = (j) => {
    m && St(t, m, j) && (c(""), f(null), l(`Sent ${j.artifactId} to Weaver`));
  }, S = (j) => {
    c(""), f(null), l(`Copied ${j}`);
  }, E = (j) => {
    l(""), f(null), c(`Could not copy ${j}.`);
  };
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => {
        k();
      }, children: "Refresh" }),
      /* @__PURE__ */ r.jsxs("label", { className: "wf-search wf-executable-definition-filter", children: [
        /* @__PURE__ */ r.jsx(Di, { size: 14 }),
        /* @__PURE__ */ r.jsx(
          "input",
          {
            "aria-label": "Filter executables by workflow definition",
            list: "wf-executable-definition-options",
            placeholder: "Filter by definition ID",
            value: n ?? "",
            onChange: (j) => o(j.currentTarget.value || null)
          }
        )
      ] }),
      /* @__PURE__ */ r.jsx("datalist", { id: "wf-executable-definition-options", children: w.map((j) => /* @__PURE__ */ r.jsx("option", { value: j }, j)) }),
      n ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => o(null), children: [
        /* @__PURE__ */ r.jsx(Ed, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    i === "failed" ? /* @__PURE__ */ r.jsx(mo, { message: a }) : null,
    d ? /* @__PURE__ */ r.jsx(tu, { status: d, run: u }) : null,
    i === "loading" ? /* @__PURE__ */ r.jsx(ws, {}) : null,
    i === "ready" && v.length === 0 ? /* @__PURE__ */ r.jsx(
      vs,
      {
        icon: /* @__PURE__ */ r.jsx(gn, { size: 22 }),
        title: "No workflow executables",
        description: n ? "No executables match this definition filter." : "Publish a workflow definition to make it executable."
      }
    ) : null,
    i === "ready" && v.length > 0 ? /* @__PURE__ */ r.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ r.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ r.jsx("span", { children: "Version" }),
        /* @__PURE__ */ r.jsx("span", { children: "Source" }),
        /* @__PURE__ */ r.jsx("span", { children: "Root" }),
        /* @__PURE__ */ r.jsx("span", { children: "Published" }),
        /* @__PURE__ */ r.jsx("span", { children: "Actions" })
      ] }),
      v.map((j) => /* @__PURE__ */ r.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ r.jsxs("span", { className: "wf-artifact-cell", children: [
          /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line", children: [
            /* @__PURE__ */ r.jsx("strong", { title: j.artifactId, children: j.artifactId }),
            /* @__PURE__ */ r.jsx(sn, { value: j.artifactId, ariaLabel: `Copy artifact ID ${j.artifactId}`, copiedLabel: "artifact ID", onCopied: S, onCopyFailed: E })
          ] }),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
            /* @__PURE__ */ r.jsx("small", { title: j.artifactHash, children: j.artifactHash }),
            /* @__PURE__ */ r.jsx(sn, { value: j.artifactHash, ariaLabel: `Copy artifact hash ${j.artifactHash}`, copiedLabel: "artifact hash", onCopied: S, onCopyFailed: E })
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line wf-version-cell", children: [
          /* @__PURE__ */ r.jsx("span", { children: j.artifactVersion }),
          /* @__PURE__ */ r.jsx(sn, { value: j.artifactVersion, ariaLabel: `Copy artifact version ${j.artifactVersion}`, copiedLabel: "artifact version", onCopied: S, onCopyFailed: E })
        ] }),
        /* @__PURE__ */ r.jsx(Vv, { executable: j, onCopied: S, onCopyFailed: E }),
        /* @__PURE__ */ r.jsx("span", { children: cu(j) }),
        /* @__PURE__ */ r.jsx("span", { children: it(j.publishedAt ?? j.createdAt) }),
        /* @__PURE__ */ r.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
            y(j);
          }, children: [
            /* @__PURE__ */ r.jsx(gn, { size: 13 }),
            " Run"
          ] }),
          m ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => b(j), children: [
            /* @__PURE__ */ r.jsx(jt, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, j.artifactId))
    ] }) : null
  ] });
}
function Vv({ executable: e, onCopied: t, onCopyFailed: n }) {
  const o = e.sourceId || e.definitionVersionId || e.definitionId, i = e.sourceVersion;
  return /* @__PURE__ */ r.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ r.jsx("span", { className: "wf-source-kind", children: lu(e.sourceKind) }),
    o ? /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line", children: [
      /* @__PURE__ */ r.jsx("code", { title: o, children: o }),
      /* @__PURE__ */ r.jsx(sn, { value: o, ariaLabel: `Copy source ID ${o}`, copiedLabel: "source ID", onCopied: t, onCopyFailed: n })
    ] }) : null,
    i ? /* @__PURE__ */ r.jsxs("small", { children: [
      "Version ",
      i
    ] }) : null
  ] });
}
function tu({ status: e, run: t, compact: n = !1 }) {
  const o = () => {
    t?.workflowExecutionId && (window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(t.workflowExecutionId)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  };
  return /* @__PURE__ */ r.jsxs("div", { className: `wf-status-line${n ? " compact" : ""}`, children: [
    /* @__PURE__ */ r.jsx(xn, { size: n ? 13 : 14 }),
    /* @__PURE__ */ r.jsx("span", { children: e }),
    t?.workflowExecutionId ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: o, children: [
      "Open Run ",
      t.workflowExecutionId
    ] }) : null
  ] });
}
function sn({ value: e, ariaLabel: t, copiedLabel: n, onCopied: o, onCopyFailed: i }) {
  if (!e) return null;
  const s = async (a) => {
    a.preventDefault(), a.stopPropagation();
    try {
      await i1(e), o(n);
    } catch {
      i(n);
    }
  };
  return /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (a) => {
    s(a);
  }, children: /* @__PURE__ */ r.jsx(n0, { size: 12 }) });
}
function Hv({ context: e, ai: t, definitionId: n, publishedArtifactId: o }) {
  const [i, s] = q("loading"), [a, c] = q(""), [d, l] = q(""), [u, f] = q(null), [h, p] = q([]), g = Bt(t, "weaver.workflows.explain-executable"), v = ue(async () => {
    s("loading"), c("");
    try {
      const S = await _d(e);
      p(S.filter((E) => n1(E, n)).sort(o1)), s("ready");
    } catch (S) {
      c(S instanceof Error ? S.message : String(S)), p([]), s("failed");
    }
  }, [e, n]);
  oe(() => {
    v();
  }, [v, o]);
  const w = async (S) => {
    l(""), f(null), c("");
    try {
      const E = await Ad(e, S.artifactId);
      f({ artifactId: S.artifactId, workflowExecutionId: du(E) }), l(`Started ${S.artifactId}`);
    } catch (E) {
      c(E instanceof Error ? E.message : String(E));
    }
  }, m = (S) => {
    g && St(t, g, S) && (c(""), f(null), l(`Sent ${S.artifactId} to Weaver`));
  }, k = () => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(n)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, y = (S) => {
    c(""), f(null), l(`Copied ${S}`);
  }, b = (S) => {
    l(""), f(null), c(`Could not copy ${S}.`);
  };
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-artifacts-panel", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-artifacts-toolbar", children: [
      /* @__PURE__ */ r.jsxs("span", { children: [
        h.length,
        " artifact",
        h.length === 1 ? "" : "s"
      ] }),
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
        v();
      }, children: [
        /* @__PURE__ */ r.jsx(ds, { size: 13 }),
        " Refresh"
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: k, children: "Open list" })
    ] }),
    i === "failed" ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ r.jsx(kt, { size: 14 }),
      " ",
      a
    ] }) : null,
    d ? /* @__PURE__ */ r.jsx(tu, { status: d, run: u, compact: !0 }) : null,
    i === "loading" ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Loading artifacts..." }) : null,
    i === "ready" && h.length === 0 ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No published artifacts for this workflow yet." }) : null,
    i === "ready" && h.length > 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-artifact-list", role: "list", "aria-label": "Workflow artifacts", children: h.map((S) => /* @__PURE__ */ r.jsxs("article", { className: "wf-artifact-card", role: "listitem", "data-active": S.artifactId === o ? "true" : void 0, children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-artifact-card-heading", children: [
        /* @__PURE__ */ r.jsxs("div", { children: [
          /* @__PURE__ */ r.jsxs("span", { className: "wf-artifact-version", children: [
            "Version ",
            S.artifactVersion
          ] }),
          S.artifactId === o ? /* @__PURE__ */ r.jsx("span", { className: "wf-chip", children: "Latest publish" }) : null
        ] }),
        /* @__PURE__ */ r.jsx("span", { children: it(S.publishedAt ?? S.createdAt) })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "wf-artifact-card-values", children: [
        /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line", children: [
          /* @__PURE__ */ r.jsx("code", { title: S.artifactId, children: S.artifactId }),
          /* @__PURE__ */ r.jsx(sn, { value: S.artifactId, ariaLabel: `Copy artifact ID ${S.artifactId}`, copiedLabel: "artifact ID", onCopied: y, onCopyFailed: b })
        ] }),
        /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ r.jsx("code", { title: S.artifactHash, children: S.artifactHash }),
          /* @__PURE__ */ r.jsx(sn, { value: S.artifactHash, ariaLabel: `Copy artifact hash ${S.artifactHash}`, copiedLabel: "artifact hash", onCopied: y, onCopyFailed: b })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("dl", { children: [
        /* @__PURE__ */ r.jsxs("div", { children: [
          /* @__PURE__ */ r.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ r.jsxs("dd", { children: [
            lu(S.sourceKind),
            " ",
            S.sourceVersion ? `v${S.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("div", { children: [
          /* @__PURE__ */ r.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ r.jsx("dd", { children: cu(S) })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
          w(S);
        }, children: [
          /* @__PURE__ */ r.jsx(gn, { size: 13 }),
          " Run"
        ] }),
        g ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => m(S), children: [
          /* @__PURE__ */ r.jsx(jt, { size: 13 }),
          " Explain"
        ] }) : null
      ] })
    ] }, S.artifactId)) }) : null
  ] });
}
function Ov({ context: e }) {
  const [t, n] = q("loading"), [o, i] = q(""), [s, a] = q(""), [c, d] = q(""), [l, u] = q([]), f = ue(async () => {
    n("loading"), i("");
    try {
      const p = await P0(e, {
        status: s || void 0,
        runKind: c || void 0,
        take: 100
      });
      u(p), n("ready");
    } catch (p) {
      i(p instanceof Error ? p.message : String(p)), u([]), n("failed");
    }
  }, [e, c, s]);
  oe(() => {
    f();
  }, [f]);
  const h = (p) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(p)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => {
        f();
      }, children: "Refresh" }),
      /* @__PURE__ */ r.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ r.jsx("span", { children: "Status" }),
        /* @__PURE__ */ r.jsxs("select", { "aria-label": "Workflow run status", value: s, onChange: (p) => a(p.target.value), children: [
          /* @__PURE__ */ r.jsx("option", { value: "", children: "All statuses" }),
          /* @__PURE__ */ r.jsx("option", { value: "Pending", children: "Pending" }),
          /* @__PURE__ */ r.jsx("option", { value: "Running", children: "Running" }),
          /* @__PURE__ */ r.jsx("option", { value: "Suspended", children: "Suspended" }),
          /* @__PURE__ */ r.jsx("option", { value: "Completed", children: "Completed" }),
          /* @__PURE__ */ r.jsx("option", { value: "Faulted", children: "Faulted" }),
          /* @__PURE__ */ r.jsx("option", { value: "Cancelled", children: "Cancelled" })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ r.jsx("span", { children: "Kind" }),
        /* @__PURE__ */ r.jsxs("select", { "aria-label": "Run Kind", value: c, onChange: (p) => d(p.target.value), children: [
          /* @__PURE__ */ r.jsx("option", { value: "", children: "All kinds" }),
          /* @__PURE__ */ r.jsx("option", { value: "TestRun", children: "Test Run" }),
          /* @__PURE__ */ r.jsx("option", { value: "PublishedRun", children: "Published Run" }),
          /* @__PURE__ */ r.jsx("option", { value: "BackgroundWeaverRun", children: "Background Weaver Run" }),
          /* @__PURE__ */ r.jsx("option", { value: "Unknown", children: "Unknown / legacy" })
        ] })
      ] })
    ] }),
    t === "failed" ? /* @__PURE__ */ r.jsx(mo, { message: o }) : null,
    t === "loading" ? /* @__PURE__ */ r.jsx(ws, {}) : null,
    t === "ready" && l.length === 0 ? /* @__PURE__ */ r.jsx(
      vs,
      {
        icon: /* @__PURE__ */ r.jsx(Mi, { size: 22 }),
        title: "No workflow runs yet",
        description: "Run a published workflow executable to create execution history here."
      }
    ) : null,
    t === "ready" && l.length > 0 ? /* @__PURE__ */ r.jsxs("div", { className: "wf-grid wf-instance-grid", role: "table", "aria-label": "Workflow runs", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ r.jsx("span", { children: "Run" }),
        /* @__PURE__ */ r.jsx("span", { children: "Kind" }),
        /* @__PURE__ */ r.jsx("span", { children: "Status" }),
        /* @__PURE__ */ r.jsx("span", { children: "Definition" }),
        /* @__PURE__ */ r.jsx("span", { children: "Activity" }),
        /* @__PURE__ */ r.jsx("span", { children: "Started" }),
        /* @__PURE__ */ r.jsx("span", { children: "Duration" })
      ] }),
      l.map((p) => /* @__PURE__ */ r.jsxs(
        "button",
        {
          type: "button",
          className: "wf-grid-row",
          role: "row",
          "aria-label": `Inspect workflow run ${p.workflowExecutionId}`,
          onClick: () => h(p.workflowExecutionId),
          children: [
            /* @__PURE__ */ r.jsxs("span", { children: [
              /* @__PURE__ */ r.jsx("strong", { children: p.workflowExecutionId }),
              /* @__PURE__ */ r.jsx("small", { children: p.artifactId })
            ] }),
            /* @__PURE__ */ r.jsx("span", { children: nu(p.runKind) }),
            /* @__PURE__ */ r.jsx("span", { children: /* @__PURE__ */ r.jsx(xo, { status: p.status, subStatus: p.subStatus }) }),
            /* @__PURE__ */ r.jsxs("span", { children: [
              /* @__PURE__ */ r.jsx("strong", { children: p.definitionId }),
              /* @__PURE__ */ r.jsx("small", { children: p.definitionVersionId })
            ] }),
            /* @__PURE__ */ r.jsxs("span", { children: [
              /* @__PURE__ */ r.jsxs("strong", { children: [
                p.activityCount,
                " activities"
              ] }),
              /* @__PURE__ */ r.jsxs("small", { children: [
                p.incidentCount,
                " incidents"
              ] })
            ] }),
            /* @__PURE__ */ r.jsx("span", { children: it(p.startedAt ?? p.createdAt) }),
            /* @__PURE__ */ r.jsx("span", { children: qd(p.startedAt ?? p.createdAt, p.completedAt ?? p.updatedAt) })
          ]
        },
        p.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function Fv({ context: e, ai: t, workflowExecutionId: n }) {
  const [o, i] = q("loading"), [s, a] = q(""), [c, d] = q(null), [l, u] = q(null), f = Bt(t, "weaver.workflows.explain-instance"), h = ue(async () => {
    if (!n) {
      a("No workflow execution id was provided."), i("failed");
      return;
    }
    i("loading"), a("");
    try {
      const g = await z0(e, n), [v, w] = await Promise.all([
        E0(e, g.instance.definitionVersionId).then(
          (m) => ({ definitionVersion: m, error: "" }),
          (m) => ({ definitionVersion: null, error: m instanceof Error ? m.message : String(m) })
        ),
        fs(e)
      ]);
      d({
        details: g,
        definitionVersion: v.definitionVersion,
        definitionVersionError: v.error,
        activityCatalog: w.activities
      }), u(null), i("ready");
    } catch (g) {
      d(null), a(j1(g, n)), i("failed");
    }
  }, [e, n]);
  oe(() => {
    h();
  }, [h]);
  const p = () => {
    window.history.pushState({}, "", "/workflows/instances"), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: p, children: [
        /* @__PURE__ */ r.jsx(li, { size: 14 }),
        " Runs"
      ] }),
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
        h();
      }, children: [
        /* @__PURE__ */ r.jsx(ds, { size: 14 }),
        " Refresh"
      ] }),
      c && f ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => St(t, f, c.details), children: [
        /* @__PURE__ */ r.jsx(jt, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    o === "loading" ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Loading workflow run..." }) : null,
    o === "failed" ? /* @__PURE__ */ r.jsx(mo, { message: s }) : null,
    o === "ready" && c ? /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-detail-workbench", children: [
      /* @__PURE__ */ r.jsx(
        Bv,
        {
          definitionVersion: c.definitionVersion,
          definitionVersionError: c.definitionVersionError,
          activityCatalog: c.activityCatalog,
          details: c.details,
          selectedEvidenceId: l,
          onSelectEvidence: u
        }
      ),
      /* @__PURE__ */ r.jsx(
        Wv,
        {
          ai: t,
          action: f,
          summary: c.details.instance,
          details: c.details,
          state: "ready",
          error: "",
          selectedEvidenceId: l,
          onSelectEvidence: u,
          activityCatalog: c.activityCatalog,
          graphNodeIds: c.definitionVersion ? Yv(c.definitionVersion, c.activityCatalog) : void 0
        }
      )
    ] }) : null
  ] });
}
function Bv({ definitionVersion: e, definitionVersionError: t, activityCatalog: n, details: o, selectedEvidenceId: i, onSelectEvidence: s }) {
  const a = fe(() => {
    if (!e) return { nodes: [], edges: [] };
    const c = e.state.rootActivity;
    if (!c) return { nodes: [], edges: [] };
    const d = n.find((p) => p.activityVersionId === c.activityVersionId), l = hs(c, d), u = l === "unsupported" ? null : Gn(c, []), f = l === "unsupported" ? Pr(c, n, e.layout) : u ? $d(u, n, e.layout) : Pr(c, n, e.layout), h = f.nodes.map((p) => ({
      ...p,
      draggable: !1,
      connectable: !1,
      deletable: !1
    }));
    return {
      nodes: Q0(h, o.activities, o.incidents, i),
      edges: f.edges.map((p) => ({ ...p, deletable: !1 }))
    };
  }, [n, e, o, i]);
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-canvas-shell", "aria-label": "Workflow run canvas", children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("span", { children: "Definition version" }),
        /* @__PURE__ */ r.jsx("h3", { children: e ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
          e.definition.name,
          " ",
          /* @__PURE__ */ r.jsx("small", { children: e.version })
        ] }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
          "Definition graph unavailable ",
          /* @__PURE__ */ r.jsx("small", { children: o.instance.definitionVersionId })
        ] }) })
      ] }),
      /* @__PURE__ */ r.jsx(xo, { status: o.instance.status, subStatus: o.instance.subStatus })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ r.jsxs("div", { className: "wf-empty", children: [
        "The workflow run loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ r.jsx("small", { children: N1(t) }) : null
      ] }),
      e && a.nodes.length === 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      a.nodes.length > 0 ? /* @__PURE__ */ r.jsxs(
        gd,
        {
          nodes: a.nodes,
          edges: a.edges,
          nodeTypes: Zd,
          edgeTypes: Gd,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (c, d) => s(d.id),
          onPaneClick: () => s(null),
          children: [
            /* @__PURE__ */ r.jsx(md, {}),
            /* @__PURE__ */ r.jsx(bd, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ r.jsx(wd, {})
          ]
        }
      ) : null
    ] })
  ] });
}
function Wv({ ai: e, action: t, summary: n, details: o, state: i, error: s, selectedEvidenceId: a = null, onSelectEvidence: c, graphNodeIds: d, activityCatalog: l = [] }) {
  const [u, f] = q("timeline");
  if (!n)
    return /* @__PURE__ */ r.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Select a workflow run to inspect its timeline." }) });
  const h = o?.incidents.length ?? 0, p = [
    { id: "timeline", title: "Timeline", order: 0, icon: /* @__PURE__ */ r.jsx(ls, { size: 14 }), render: () => null },
    { id: "issues", title: h > 0 ? `Issues (${h})` : "Issues", order: 1, icon: /* @__PURE__ */ r.jsx(kt, { size: 14 }), render: () => null },
    { id: "details", title: "Details", order: 2, icon: /* @__PURE__ */ r.jsx(us, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ r.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Workflow run details", children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("span", { children: "Workflow Instance ID" }),
        /* @__PURE__ */ r.jsx("h3", { children: n.workflowExecutionId })
      ] }),
      t ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => St(e, t, o ?? n), children: [
        /* @__PURE__ */ r.jsx(jt, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    /* @__PURE__ */ r.jsx("div", { className: "wf-instance-tabs", children: /* @__PURE__ */ r.jsx(Go, { label: "Workflow run tabs", tabs: p, activeTabId: u, onSelect: (g) => f(g) }) }),
    i === "loading" ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Loading run details..." }) : null,
    i === "failed" ? /* @__PURE__ */ r.jsx(mo, { message: s }) : null,
    i === "ready" && o ? /* @__PURE__ */ r.jsx("div", { className: "wf-instance-tab-content", children: u === "timeline" ? /* @__PURE__ */ r.jsx(
      lv,
      {
        activities: o.activities,
        activityCatalog: l,
        selectedEvidenceId: a,
        onSelectEvidence: c
      }
    ) : u === "issues" ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
      /* @__PURE__ */ r.jsx(Kv, { incidents: o.incidents, selectedEvidenceId: a, onSelectEvidence: c }),
      /* @__PURE__ */ r.jsx(qv, { details: o, graphNodeIds: d })
    ] }) : /* @__PURE__ */ r.jsxs("dl", { className: "wf-instance-meta", children: [
      /* @__PURE__ */ r.jsx("dt", { children: "Status" }),
      /* @__PURE__ */ r.jsx("dd", { children: /* @__PURE__ */ r.jsx(xo, { status: n.status, subStatus: n.subStatus }) }),
      /* @__PURE__ */ r.jsx("dt", { children: "Run Kind" }),
      /* @__PURE__ */ r.jsx("dd", { children: nu(n.runKind) }),
      /* @__PURE__ */ r.jsx("dt", { children: "Artifact" }),
      /* @__PURE__ */ r.jsxs("dd", { children: [
        n.artifactId,
        " ",
        /* @__PURE__ */ r.jsx("small", { children: n.artifactVersion })
      ] }),
      /* @__PURE__ */ r.jsx("dt", { children: "Definition" }),
      /* @__PURE__ */ r.jsxs("dd", { children: [
        n.definitionId,
        " ",
        /* @__PURE__ */ r.jsx("small", { children: n.definitionVersionId })
      ] }),
      /* @__PURE__ */ r.jsx("dt", { children: "Created" }),
      /* @__PURE__ */ r.jsx("dd", { children: it(n.createdAt) }),
      /* @__PURE__ */ r.jsx("dt", { children: "Started" }),
      /* @__PURE__ */ r.jsx("dd", { children: it(n.startedAt) }),
      /* @__PURE__ */ r.jsx("dt", { children: "Completed" }),
      /* @__PURE__ */ r.jsx("dd", { children: it(n.completedAt) }),
      /* @__PURE__ */ r.jsx("dt", { children: "Correlation" }),
      /* @__PURE__ */ r.jsx("dd", { children: n.correlationId || "None" })
    ] }) }) : null
  ] });
}
function Kv({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ r.jsx("h4", { children: "Incidents" }),
    e.length === 0 ? /* @__PURE__ */ r.jsx("p", { children: "No incidents recorded." }) : null,
    e.map((o) => /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-instance-incident",
        "data-severity": o.severity.toLowerCase(),
        "data-selected": o.incidentId === t,
        onClick: () => n?.(o.incidentId),
        children: [
          /* @__PURE__ */ r.jsx("strong", { children: o.failureType }),
          /* @__PURE__ */ r.jsxs("span", { children: [
            o.status,
            " · ",
            o.severity
          ] }),
          /* @__PURE__ */ r.jsx("p", { children: o.message })
        ]
      },
      o.incidentId
    ))
  ] });
}
function qv({ details: e, graphNodeIds: t }) {
  if (!t) return null;
  const n = new Map(e.activities.map((s) => [s.activityExecutionId, s])), o = e.activities.filter((s) => !t.has(wc(s))), i = e.incidents.filter((s) => {
    const a = s.activityExecutionId ? n.get(s.activityExecutionId) : null, c = s.executableNodeId ?? (a ? wc(a) : "");
    return !c || !t.has(c);
  });
  return o.length === 0 && i.length === 0 ? null : /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ r.jsx("h4", { children: "Unmatched runtime evidence" }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-unmatched-list", children: [
      o.map((s) => /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ r.jsx("strong", { children: mi(s.activityType) ?? s.activityType }),
        /* @__PURE__ */ r.jsx("small", { children: s.activityExecutionId })
      ] }, `activity-${s.activityExecutionId}`)),
      i.map((s) => /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ r.jsx("strong", { children: s.failureType }),
        /* @__PURE__ */ r.jsx("small", { children: s.incidentId })
      ] }, `incident-${s.incidentId}`))
    ] })
  ] });
}
function xo({ status: e, subStatus: t }) {
  return /* @__PURE__ */ r.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
function nu(e) {
  switch (Xv(e)) {
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
function Xv(e) {
  return (e ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}
function Yv(e, t) {
  const n = e.state.rootActivity;
  if (!n) return /* @__PURE__ */ new Set();
  const o = t.find((s) => s.activityVersionId === n.activityVersionId);
  if (hs(n, o) === "unsupported") return /* @__PURE__ */ new Set([n.nodeId]);
  const i = Gn(n, []);
  return new Set(i?.slot.activities.map((s) => s.nodeId) ?? [n.nodeId]);
}
function wc(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function Uv({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: i }) {
  const s = ou(n, t), a = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-pagination", "aria-label": "Workflow definition pagination", children: [
    /* @__PURE__ */ r.jsxs("span", { className: "wf-pagination-summary", "aria-live": "polite", children: [
      "Showing ",
      a,
      "-",
      c,
      " of ",
      n
    ] }),
    /* @__PURE__ */ r.jsxs("label", { className: "wf-page-size", children: [
      "Rows",
      /* @__PURE__ */ r.jsx("select", { value: t, onChange: (d) => i(Number(d.target.value)), children: Cv.map((d) => /* @__PURE__ */ r.jsx("option", { value: d, children: d }, d)) })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ r.jsx(li, { size: 14 }),
        " Previous"
      ] }),
      /* @__PURE__ */ r.jsxs("span", { children: [
        "Page ",
        e,
        " of ",
        s
      ] }),
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => o(e + 1), disabled: e >= s, "aria-label": "Next page", title: "Next page", children: [
        "Next ",
        /* @__PURE__ */ r.jsx(en, { size: 14 })
      ] })
    ] })
  ] });
}
function Zv(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function ou(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Bt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function St(e, t, n) {
  const o = t.createPrompt(n);
  return o ? (e.dispatchPrompt(o), !0) : !1;
}
function Gv(e) {
  const t = xi(e, "flowchart"), n = xi(e, "sequence"), o = [
    { value: t?.activityVersionId ?? "kind:flowchart", label: "Flowchart" },
    { value: n?.activityVersionId ?? "kind:sequence", label: "Sequence" }
  ], i = /* @__PURE__ */ new Map();
  for (const a of e.filter(au)) {
    if (e1(a)) continue;
    const c = a.category || "Uncategorized";
    i.set(c, [...i.get(c) ?? [], a]);
  }
  const s = Array.from(i.entries()).sort(([a], [c]) => a.localeCompare(c)).map(([a, c]) => ({
    name: a,
    activities: c.sort((d, l) => Se(d).localeCompare(Se(l)))
  }));
  return { compositeRoots: o, otherCategories: s };
}
function Jv(e, t) {
  return e.rootActivityVersionId ?? xi(t, e.rootKind)?.activityVersionId ?? `kind:${e.rootKind}`;
}
function Qv(e, t) {
  return e.rootActivityVersionId ?? xi(t, e.rootKind)?.activityVersionId ?? null;
}
function xi(e, t) {
  return e.find((n) => iu(n) === t);
}
function iu(e) {
  return e ? ru(e) ? "flowchart" : su(e) ? "sequence" : null : null;
}
function Fr(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "Uncategorized";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [o]) => n.localeCompare(o)).map(([n, o]) => ({
    category: n,
    activities: o.sort((i, s) => Se(i).localeCompare(Se(s)))
  }));
}
function e1(e) {
  return ru(e) || su(e);
}
function ru(e) {
  return Se(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function su(e) {
  return Se(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function au(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function cu(e) {
  return r1(e.rootActivityType) || e.rootActivityType;
}
function t1(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function n1(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function o1(e, t) {
  return vc(t) - vc(e);
}
function vc(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function lu(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
function du(e) {
  const t = e?.workflowExecutionId ?? e?.runId ?? e?.executionId;
  return typeof t == "string" && t.trim() ? t : null;
}
async function i1(e) {
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
function r1(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function s1(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    Fo(t, n.typeName, n), Fo(t, n.name, n), Fo(t, n.displayName, n);
    const o = n.typeName.split(".").filter(Boolean).at(-1);
    Fo(t, o, n);
  }
  return t;
}
function a1(e, t, n) {
  const o = t.get(e.activityVersionId);
  return n.get(Xn(o?.activityTypeKey)) ?? n.get(Xn(mi(o?.activityTypeKey))) ?? n.get(Xn(o?.displayName)) ?? n.get(Xn(e.activityVersionId)) ?? null;
}
function Fo(e, t, n) {
  const o = Xn(t);
  o && !e.has(o) && e.set(o, n);
}
function Xn(e) {
  return e?.trim().toLowerCase() ?? "";
}
function bc(e, t, n, o) {
  const i = Ri();
  if (!i) return t;
  const s = i.getItem(e);
  if (s == null) return t;
  const a = Number(s);
  return Number.isFinite(a) ? Zo(a, n, o) : t;
}
function Nc(e, t) {
  const n = Ri();
  if (!n) return t;
  const o = n.getItem(e);
  return o === "true" ? !0 : o === "false" ? !1 : t;
}
function c1() {
  const e = Ri();
  if (!e) return null;
  const t = e.getItem(Jd);
  return t === "palette" || t === "inspector" ? t : null;
}
function Ri() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function Bn(e, t) {
  const n = Ri();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function Zo(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function l1({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: o,
  expressionEditors: i,
  workflowDesignerPanels: s,
  onBack: a
}) {
  const [c, d] = q(null), [l, u] = q(null), [f, h] = q([]), [p, g] = q([]), [v, w] = q(null), [m, k] = q(Yo), [y, b] = q("loading"), [S, E] = q([]), [j, _] = q([]), [D, B] = q([]), [M, T] = q(null), [L, N] = q(null), [A, I] = q(null), [$, z] = q(null), [P, F] = q(""), [K, H] = q(""), [Z, X] = q("idle"), [ne, se] = q(null), [G, V] = q(!1), [Y, ae] = q(null), [ce, J] = q(() => /* @__PURE__ */ new Set()), [ee, de] = q(""), [O, Q] = q(() => bc(fc, Av, Vn, Hn)), [ge, ve] = q(() => bc(hc, _v, On, Fn)), [Ee, $e] = q(() => Nc(pc, !1)), [Ie, Xe] = q(() => Nc(gc, !1)), [be, Le] = q(c1), [Ve, Ze] = q("activities"), [Ge, Je] = q("inspector"), [Et, wn] = q("designer"), Ye = re(null), Wt = re(null), vn = re(""), bn = re(0), wo = re(Promise.resolve()), Nn = re(/* @__PURE__ */ new Map()), He = re(nc()), dt = re(null), ut = re(""), Kt = re(!1), [Li, jn] = q(0), qt = re(null), mt = re(null), Ct = re(!1), It = l?.state.rootActivity ?? null, Ue = fe(() => new Map(f.map((x) => [x.activityVersionId, x])), [f]), Sn = ue(
    (x) => J0([x.activityVersionId, x.activityTypeKey], v),
    [v]
  ), vo = fe(() => s1(p), [p]), Oe = fe(() => Td(It, S), [It, S]), bo = hs(Oe, Oe ? Ue.get(Oe.activityVersionId) : void 0), we = !!Oe && bo === "unsupported", Fe = fe(() => we ? null : Gn(It, S), [It, S, we]), Xt = fe(() => Fr(f), [f]), No = fe(() => {
    const x = ee.trim().toLowerCase();
    if (!x) return Xt;
    const C = f.filter((R) => Se(R).toLowerCase().includes(x) || R.activityTypeKey.toLowerCase().includes(x) || (R.category ?? "").toLowerCase().includes(x) || (R.description ?? "").toLowerCase().includes(x));
    return Fr(C);
  }, [f, ee, Xt]), me = fe(() => we && Oe?.nodeId === L ? Oe : Fe?.slot.activities.find((x) => x.nodeId === L) ?? null, [we, Fe, Oe, L]), At = fe(
    () => me ? a1(me, Ue, vo) : null,
    [Ue, vo, me]
  ), jo = fe(
    () => me ? Sn({ activityVersionId: me.activityVersionId, activityTypeKey: Ue.get(me.activityVersionId)?.activityTypeKey }) : null,
    [Sn, Ue, me]
  ), kn = me ? ot(me) : [], Ae = bo === "flowchart" && Fe?.slot.mode === "flowchart", xt = !It || !we, ft = Z !== "idle", So = !!l?.state.rootActivity && !ft, ko = Bt(n, "weaver.workflows.find-draft-risks"), Eo = Bt(n, "weaver.workflows.propose-update");
  oe(() => {
    if (!(!c || !l))
      return window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
        workflowId: c.definition.id,
        workflowDefinitionId: c.definition.id,
        workflowVersionId: l.sourceVersionId ?? null,
        draftId: l.id,
        revision: v1(l),
        selectedNodeId: L,
        selectedActivityType: At?.typeName ?? (me ? Ue.get(me.activityVersionId)?.activityTypeKey ?? me.activityVersionId : null),
        summary: c.definition.name,
        activities: fu(l.state.rootActivity, Ue),
        diagnostics: l.validationErrors.map((x) => ({ severity: x.code ?? "warning", message: x.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === c.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [Ue, c, l, At, me, L]), oe(() => {
    const x = (R) => {
      const W = R.detail;
      if (!W?.batch || !W.respond) return;
      if (!l || !c) {
        W.respond({ ok: !1, message: "No active workflow draft is open." });
        return;
      }
      const U = W.batch.workflowDefinitionId;
      if (U && U !== "active-draft" && U !== c.definition.id) {
        W.respond({ ok: !1, message: `Batch targets workflow '${U}', but '${c.definition.id}' is active.` });
        return;
      }
      try {
        const le = Wn(l), ie = gv(l, W.batch, f), ye = `weaver-batch-${Date.now()}`;
        Nn.current.set(ye, le), u(ie.draft), E([]), N(ie.finalActivityIds.at(-1) ?? null), ae(null), se(null), H(ie.summary), F(""), W.respond({ ok: !0, result: { ...ie, undoToken: ye } });
      } catch (le) {
        const ie = le instanceof Error ? le.message : String(le);
        F(ie), W.respond({ ok: !1, message: ie });
      }
    }, C = (R) => {
      const W = R.detail;
      if (!W?.undoToken || !W.respond) return;
      const U = Nn.current.get(W.undoToken);
      if (!U) {
        W.respond({ ok: !1, message: "The Weaver batch undo point is no longer available." });
        return;
      }
      Nn.current.delete(W.undoToken), u(U), E([]), N(null), ae(null), se(null), H("Restored workflow draft before Weaver batch."), F(""), W.respond({ ok: !0, summary: "Restored workflow draft before Weaver batch." });
    };
    return window.addEventListener(cc, x), window.addEventListener(lc, C), () => {
      window.removeEventListener(cc, x), window.removeEventListener(lc, C);
    };
  }, [f, c, l]), oe(() => {
    Bn(fc, String(O));
  }, [O]), oe(() => {
    Bn(hc, String(ge));
  }, [ge]), oe(() => {
    Bn(pc, String(Ee));
  }, [Ee]), oe(() => {
    Bn(gc, String(Ie));
  }, [Ie]), oe(() => {
    Bn(Jd, be);
  }, [be]), oe(() => {
    if (!be) return;
    const x = (C) => {
      C.key === "Escape" && Le(null);
    };
    return window.addEventListener("keydown", x), () => window.removeEventListener("keydown", x);
  }, [be]);
  const En = ue(async () => {
    F(""), b("loading");
    const [x, C, R, W, U] = await Promise.all([
      k0(e, t),
      fs(e),
      V0(e).then(
        (ie) => ({ ok: !0, descriptors: ie }),
        () => ({ ok: !1, descriptors: [] })
      ),
      H0(e).then(
        (ie) => ({ ok: !0, descriptors: ie }),
        () => ({ ok: !1, descriptors: Yo })
      ),
      // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
      Md(e).then(
        (ie) => ie,
        () => null
      )
    ]), le = x.draft ?? null;
    d(x), vn.current = le ? Be(le) : "", He.current = nc(), dt.current = le ? Wn(le) : null, ut.current = le ? Be(le) : "", Kt.current = !1, jn(0), u(le), h(C.activities ?? []), g(R.descriptors), w(U), k(W.descriptors.length > 0 ? W.descriptors : Yo), b(R.ok ? "ready" : "failed"), E([]), N(null);
  }, [e, t]);
  oe(() => {
    En().catch((x) => F(x instanceof Error ? x.message : String(x)));
  }, [En]), oe(() => {
    J((x) => {
      let C = !1;
      const R = new Set(x);
      for (const W of Xt)
        R.has(W.category) || (R.add(W.category), C = !0);
      return C ? R : x;
    });
  }, [Xt]), oe(() => {
    if (!Oe) {
      _([]), B([]);
      return;
    }
    const x = we ? Pr(Oe, f, l?.layout ?? []) : Fe ? $d(Fe, f, l?.layout ?? []) : { nodes: [], edges: [] };
    _(x.nodes), B(x.edges);
  }, [f, l?.layout, we, Fe, Oe]);
  const Vi = (x) => {
    u((C) => C && { ...C, state: { ...C.state, rootActivity: x } });
  }, Cn = ue((x, C) => {
    if (l?.state.rootActivity && we)
      return;
    const R = Rr(x, kc(x));
    if (!l?.state.rootActivity) {
      Vi(R), N(R.nodeId);
      return;
    }
    if (!Fe) {
      if (!ot(R)[0]) {
        H(""), F("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      u((U) => {
        if (!U?.state.rootActivity) return U;
        const le = U.state.rootActivity, ie = zr(R, [], [le]), ye = C ? [
          ...U.layout.filter((xe) => xe.nodeId !== le.nodeId),
          {
            nodeId: le.nodeId,
            x: Math.round(C.x),
            y: Math.round(C.y)
          }
        ] : U.layout;
        return {
          ...U,
          layout: ye,
          state: {
            ...U.state,
            rootActivity: ie
          }
        };
      }), N(l.state.rootActivity.nodeId), F(""), H(`Wrapped root in ${Se(x)}`);
      return;
    }
    u((W) => {
      if (!W?.state.rootActivity) return W;
      const U = Gn(W.state.rootActivity, S);
      if (!U) return W;
      const le = zr(W.state.rootActivity, S, [...U.slot.activities, R]), ie = C ? [
        ...W.layout.filter((ye) => ye.nodeId !== R.nodeId),
        {
          nodeId: R.nodeId,
          x: Math.round(C.x),
          y: Math.round(C.y)
        }
      ] : W.layout;
      return {
        ...W,
        layout: ie,
        state: {
          ...W.state,
          rootActivity: le
        }
      };
    }), N(R.nodeId);
  }, [l?.state.rootActivity, S, we, Fe]), Yt = ue((x, C) => {
    const R = Rr(x, kc(x)), W = {
      id: R.nodeId,
      type: "workflowActivity",
      position: C,
      selected: !0,
      data: {
        label: Se(x),
        activityVersionId: x.activityVersionId,
        activityTypeKey: x.activityTypeKey,
        category: x.category,
        executionType: x.executionType,
        icon: gi(x),
        childSlots: ot(R),
        acceptsInbound: String(x.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: Ld(R, x)
      }
    };
    return { activityNode: R, node: W };
  }, []), Me = ue((x, C, R = []) => {
    we || u((W) => {
      if (!W) return W;
      const U = nw(W.layout, x), le = W.state.rootActivity;
      if (!le) return { ...W, layout: U };
      const ie = Gn(le, S);
      if (!ie) return { ...W, layout: U };
      const ye = ew(ie, x, C, R), xe = ie.slot.mode === "flowchart" ? tw(ye, C) : ye;
      return {
        ...W,
        layout: U,
        state: {
          ...W.state,
          rootActivity: Pd(le, S, xe)
        }
      };
    });
  }, [S, we]), In = ue((x, C) => {
    if (!Ye.current) return null;
    const R = Ye.current.getBoundingClientRect();
    return M ? M.screenToFlowPosition({ x, y: C }) : {
      x: x - R.left,
      y: C - R.top
    };
  }, [M]), An = ue((x, C) => document.elementFromPoint(x, C)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), _n = ue((x, C, R) => {
    const W = j.find((De) => De.id === C.source), U = j.find((De) => De.id === C.target), le = W && U ? m1(W, U) : W ? Ec(W) : R, ie = Yt(x, le), xe = [...j.map((De) => De.selected ? { ...De, selected: !1 } : De), ie.node], _t = fw(D, C, ie.node.id);
    _(xe), B(_t), N(ie.node.id), Me(xe, _t, [ie.activityNode]);
  }, [Me, Yt, D, j]), Ut = ue((x, C, R) => {
    if (!xt || !Ye.current) return !1;
    const W = Ye.current.getBoundingClientRect();
    if (!(C >= W.left && C <= W.right && R >= W.top && R <= W.bottom)) return !1;
    const le = In(C, R);
    if (!le) return !1;
    if (Ae) {
      const ie = An(C, R), ye = ie ? D.find((xe) => xe.id === ie) : void 0;
      if (ye)
        return _n(x, ye, le), !0;
    }
    return Cn(x, le), !0;
  }, [Cn, xt, D, An, Ae, _n, In]);
  oe(() => {
    const x = (R) => {
      const W = qt.current;
      if (!W) return;
      Math.hypot(R.clientX - W.startX, R.clientY - W.startY) >= Sv && (W.dragging = !0);
    }, C = (R) => {
      const W = qt.current;
      if (qt.current = null, !W?.dragging || !Ye.current || mt.current) return;
      const U = Ye.current.getBoundingClientRect();
      R.clientX >= U.left && R.clientX <= U.right && R.clientY >= U.top && R.clientY <= U.bottom && (Ct.current = !0, window.setTimeout(() => {
        Ct.current = !1;
      }, 0), Ut(W.activity, R.clientX, R.clientY));
    };
    return window.addEventListener("pointermove", x), window.addEventListener("pointerup", C), window.addEventListener("pointercancel", C), () => {
      window.removeEventListener("pointermove", x), window.removeEventListener("pointerup", C), window.removeEventListener("pointercancel", C);
    };
  }, [M, Ut]);
  const Hi = (x, C) => {
    mt.current = { activityVersionId: C.activityVersionId, handledDrop: !1 }, x.dataTransfer.setData(uc, C.activityVersionId), x.dataTransfer.setData("text/plain", C.activityVersionId), x.dataTransfer.effectAllowed = "copy";
  }, Oi = (x, C) => {
    const R = mt.current;
    mt.current = null, !R?.handledDrop && (x.clientX === 0 && x.clientY === 0 || Ut(C, x.clientX, x.clientY) && (Ct.current = !0, window.setTimeout(() => {
      Ct.current = !1;
    }, 0)));
  }, Fi = (x, C) => {
    x.button === 0 && (qt.current = {
      activity: C,
      startX: x.clientX,
      startY: x.clientY,
      dragging: !1
    });
  }, Bi = (x) => {
    Ct.current || xt && Cn(x);
  }, Mn = (x) => {
    if (!xt) {
      x.dataTransfer.dropEffect = "none";
      return;
    }
    if (x.preventDefault(), x.dataTransfer.dropEffect = "copy", !Ae) return;
    const C = An(x.clientX, x.clientY);
    z(C);
  }, Co = (x) => {
    if (!Ye.current) return;
    const C = x.relatedTarget;
    C && Ye.current.contains(C) || z(null);
  }, Io = (x) => {
    x.preventDefault(), z(null);
    const C = x.dataTransfer.getData(uc) || x.dataTransfer.getData("text/plain");
    if (!C || (x.stopPropagation(), mt.current?.activityVersionId === C && (mt.current.handledDrop = !0), !xt)) return;
    const R = Ue.get(C);
    R && Ut(R, x.clientX, x.clientY);
  }, Wi = () => {
    if (!Ae) return;
    const x = Ye.current?.getBoundingClientRect();
    x && I({
      kind: "fromEmpty",
      clientX: x.left + x.width / 2,
      clientY: x.top + x.height / 2
    });
  }, Dn = ue(async (x, C) => {
    const R = async () => {
      const U = ++bn.current, le = Be(x);
      F("");
      try {
        const ie = await M0(e, x), ye = Be(ie);
        return vn.current = ye, u((xe) => !xe || xe.id !== ie.id ? xe : Be(xe) === le ? ie : { ...xe, validationErrors: ie.validationErrors }), U === bn.current && H(C), ie;
      } catch (ie) {
        throw U === bn.current && (H(""), F(ie instanceof Error ? ie.message : String(ie))), ie;
      }
    }, W = wo.current.then(R, R);
    return wo.current = W.catch(() => {
    }), W;
  }, [e]);
  oe(() => {
    if (!G || !l || Be(l) === vn.current) return;
    H("Autosaving...");
    const C = window.setTimeout(() => {
      Dn(l, "Autosaved").catch(() => {
      });
    }, kv);
    return () => window.clearTimeout(C);
  }, [G, l, Dn]), oe(() => {
    if (!l) return;
    if (Kt.current) {
      Kt.current = !1;
      return;
    }
    const x = Be(l);
    if (x === ut.current) return;
    const C = window.setTimeout(() => {
      const R = dt.current;
      R && (He.current = oc(He.current, R), jn((W) => W + 1)), dt.current = Wn(l), ut.current = x;
    }, Ev);
    return () => window.clearTimeout(C);
  }, [l]);
  const Ki = ue(() => {
    if (!l) return;
    const x = c?.definition.name;
    Hw(Rw(l, x), x), H("Exported workflow as JSON.");
  }, [l, c]), qi = ue((x) => {
    if (!l) return "No draft is loaded.";
    const C = Vw(x, l);
    return C.ok ? (u(C.draft), N(null), E([]), H("Applied workflow JSON."), null) : C.error;
  }, [l]), Zt = ue(() => {
    if (!l) return;
    const x = Be(l);
    if (x === ut.current) return;
    const C = dt.current;
    C && (He.current = oc(He.current, C)), dt.current = Wn(l), ut.current = x;
  }, [l]), Gt = ue((x) => {
    Kt.current = !0, dt.current = Wn(x), ut.current = Be(x), u(x), N(null), E([]), jn((C) => C + 1);
  }, []), Jt = ue(() => {
    if (!l) return;
    Zt();
    const x = Uw(He.current, l);
    x && (He.current = x.history, Gt(x.snapshot));
  }, [l, Zt, Gt]), Tn = ue(() => {
    if (!l) return;
    Zt();
    const x = Zw(He.current, l);
    x && (He.current = x.history, Gt(x.snapshot));
  }, [l, Zt, Gt]), { canUndoNow: Ao, canRedoNow: Xi } = fe(() => {
    const x = !!l && !!dt.current && Be(l) !== ut.current;
    return {
      canUndoNow: Xw(He.current) || x,
      canRedoNow: Yw(He.current) && !x
    };
  }, [l, Li]);
  oe(() => {
    const x = (C) => {
      if (Et !== "designer" || !(C.metaKey || C.ctrlKey)) return;
      const R = C.target;
      if (R && (R.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(R.tagName))) return;
      const W = C.key.toLowerCase();
      W === "z" && !C.shiftKey ? (C.preventDefault(), Jt()) : (W === "z" && C.shiftKey || W === "y") && (C.preventDefault(), Tn());
    };
    return window.addEventListener("keydown", x), () => window.removeEventListener("keydown", x);
  }, [Et, Jt, Tn]);
  const Yi = async () => {
    if (!(!l || ft)) {
      X("saving"), H("Saving...");
      try {
        await Dn(l, "Saved");
      } catch {
      } finally {
        X("idle");
      }
    }
  }, $n = async () => {
    if (!(!l || ft)) {
      X("promoting"), H("Promoting...");
      try {
        const x = await D0(e, l.id), C = await T0(e, x.versionId);
        ae(C.artifactId), H(`Published ${C.artifactVersion}`), await En();
      } catch (x) {
        H(""), F(x instanceof Error ? x.message : String(x));
      } finally {
        X("idle");
      }
    }
  }, Ui = async () => {
    if (!l?.state.rootActivity || ft) return;
    const x = l, C = Be(x);
    se(null), H("Preparing test run...");
    try {
      X("testRunPreparing"), H("Preparing test run...");
      const R = b1(x);
      X("testRunStarting"), H("Starting test run...");
      const W = await $0(e, {
        definitionId: x.definitionId,
        snapshotId: R,
        state: x.state
      });
      se({ draftSignature: C, view: W }), Je("runtime"), Xe(!1), H(bs(W) ? "Test run rejected" : "Test run dispatched");
    } catch (R) {
      H(""), F(R instanceof Error ? R.message : String(R));
    } finally {
      X("idle");
    }
  }, Zi = (x) => {
    const C = we ? x.filter((R) => R.type === "select") : x;
    C.length !== 0 && _((R) => Ol(C, R));
  }, _o = (x) => {
    we || B((C) => Fl(x, C));
  }, Gi = (x) => !x.source || !x.target || x.source === x.target || !Ae ? !1 : !x.targetHandle, pu = (x) => {
    if (!l?.state.rootActivity || !Fe || !Ae || !Gi(x)) return;
    const C = yi(x.source, x.target, x.sourceHandle ?? "Done", x.targetHandle ?? void 0), R = Wl(C, D);
    B(R), Me(j, R);
  }, gu = () => {
    Me(j, D);
  }, yu = !we && j.length > 0, mu = ue(() => {
    if (we || j.length === 0) return;
    const x = Fe?.slot.mode === "sequence" ? "sequence" : "flowchart", C = Bw(j, D, x), R = j.map((W) => {
      const U = C.get(W.id);
      return U ? { ...W, position: U } : W;
    });
    _(R), Me(R, D), window.requestAnimationFrame(() => M?.fitView({ padding: 0.2 })), H("Rearranged the canvas.");
  }, [D, j, Fe, we, Me, M]), xu = (x, C) => {
    if (!C.nodeId || C.handleType === "target") {
      Wt.current = null;
      return;
    }
    Wt.current = {
      nodeId: C.nodeId,
      handleId: C.handleId ?? null
    };
  }, wu = (x, C) => {
    const R = w1(Wt.current, C);
    if (Wt.current = null, !R || !Ae || C.toNode || C.toHandle || x1(x)) return;
    const W = uu(x);
    I({
      kind: "fromPort",
      sourceNodeId: R.nodeId,
      sourceHandleId: R.handleId,
      clientX: W.x,
      clientY: W.y
    });
  }, vu = (x, C) => {
    if (!Ae || !Gi(C)) return;
    const R = tm(x, {
      ...C,
      sourceHandle: C.sourceHandle ?? "Done",
      targetHandle: C.targetHandle ?? void 0
    }, D, { shouldReplaceId: !1 });
    B(R), Me(j, R);
  }, bu = (x) => {
    if (we || x.length === 0) return;
    const C = new Set(x.map((U) => U.id)), R = j.filter((U) => !C.has(U.id)), W = D.filter((U) => !C.has(U.source) && !C.has(U.target));
    _(R), B(W), L && C.has(L) && N(null), Me(R, W);
  }, Nu = (x) => {
    if (we || x.length === 0) return;
    const C = new Set(x.map((W) => W.id)), R = D.filter((W) => !C.has(W.id));
    B(R), Me(j, R);
  }, Ns = ue((x) => {
    if (we) return;
    const C = D.filter((R) => R.id !== x);
    B(C), Me(j, C);
  }, [Me, D, we, j]), js = ue((x, C, R) => {
    Ae && I({ kind: "spliceEdge", edgeId: x, clientX: C, clientY: R });
  }, [Ae]), ju = (x) => {
    const C = A;
    if (!C) return;
    I(null);
    const R = In(C.clientX, C.clientY) ?? { x: 0, y: 0 };
    if (C.kind === "fromEmpty") {
      const U = Yt(x, R), ie = [...j.map((ye) => ye.selected ? { ...ye, selected: !1 } : ye), U.node];
      _(ie), N(U.node.id), Me(ie, D, [U.activityNode]);
      return;
    }
    if (C.kind === "fromPort") {
      const U = j.find((De) => De.id === C.sourceNodeId), le = U ? Ec(U) : R, ie = Yt(x, le), xe = [...j.map((De) => De.selected ? { ...De, selected: !1 } : De), ie.node], _t = [...D, yi(C.sourceNodeId, ie.node.id, C.sourceHandleId ?? "Done")];
      _(xe), B(_t), N(ie.node.id), Me(xe, _t, [ie.activityNode]);
      return;
    }
    const W = D.find((U) => U.id === C.edgeId);
    W && _n(x, W, R);
  }, Su = fe(() => ({
    highlightedEdgeId: $,
    deleteEdge: Ns,
    requestInsertActivity: js
  }), [Ns, $, js]), ku = (x, C, R) => {
    E((W) => [...W, { ownerNodeId: x.nodeId, slotId: C, label: R }]), N(null);
  }, Eu = ue((x) => {
    u((C) => {
      const R = C?.state.rootActivity;
      return !C || !R ? C : {
        ...C,
        state: {
          ...C.state,
          rootActivity: zd(R, x.nodeId, () => x)
        }
      };
    });
  }, []), Cu = (x) => {
    J((C) => {
      const R = new Set(C);
      return R.has(x) ? R.delete(x) : R.add(x), R;
    });
  }, Ss = (x) => {
    Le((C) => C === x ? null : C), x === "palette" ? $e((C) => !C) : Xe((C) => !C);
  }, ks = (x) => {
    x === "palette" ? $e(!1) : Xe(!1), Le((C) => C === x ? null : x);
  }, Es = (x, C) => {
    Le(null), x === "palette" ? ($e(!1), Q((R) => Zo(R + C, Vn, Hn))) : (Xe(!1), ve((R) => Zo(R + C, On, Fn)));
  }, Cs = (x, C) => {
    C.preventDefault(), Le(null), x === "palette" ? $e(!1) : Xe(!1);
    const R = C.clientX, W = x === "palette" ? O : ge, U = x === "palette" ? Vn : On, le = x === "palette" ? Hn : Fn;
    document.body.classList.add("wf-side-panel-resizing");
    const ie = (xe) => {
      const _t = x === "palette" ? xe.clientX - R : R - xe.clientX, De = Zo(W + _t, U, le);
      x === "palette" ? Q(De) : ve(De);
    }, ye = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", ie), window.removeEventListener("pointerup", ye), window.removeEventListener("pointercancel", ye);
    };
    window.addEventListener("pointermove", ie), window.addEventListener("pointerup", ye), window.addEventListener("pointercancel", ye);
  }, Is = (x, C) => {
    C.key === "ArrowLeft" ? (C.preventDefault(), Es(x, x === "palette" ? -Oo : Oo)) : C.key === "ArrowRight" ? (C.preventDefault(), Es(x, x === "palette" ? Oo : -Oo)) : C.key === "Home" ? (C.preventDefault(), x === "palette" ? Q(Vn) : ve(On)) : C.key === "End" && (C.preventDefault(), x === "palette" ? Q(Hn) : ve(Fn));
  };
  if (!c || !l)
    return /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: P || "Loading workflow editor..." });
  const Iu = [
    "wf-editor-body",
    Ee ? "palette-collapsed" : "",
    Ie ? "inspector-collapsed" : "",
    be === "palette" ? "palette-maximized" : "",
    be === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), Au = {
    "--wf-palette-width": `${Ee ? yc : O}px`,
    "--wf-inspector-width": `${Ie ? yc : ge}px`
  }, As = !Ee && be !== "inspector", _s = !Ie && be !== "palette", Mo = ne?.draftSignature === Be(l) ? ne.view : null, Ms = Mo && K.startsWith("Test run") ? "" : K, _u = (x) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(x)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, Mu = {
    definition: c.definition,
    draft: l,
    selectedActivity: me,
    selectedActivityDescriptor: At,
    selectedActivitySlots: kn,
    catalog: f,
    currentScopeOwner: Oe,
    frames: S
  }, Ds = s.map((x) => {
    const C = x.component;
    return {
      id: x.id,
      title: x.title,
      side: x.side,
      order: x.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ r.jsx(C, { context: Mu })
    };
  }), Ji = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ r.jsx(Mi, { size: 15 }),
      render: Tu
    },
    ...Ds.filter((x) => x.side === "left")
  ].sort(jc), Qi = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ r.jsx(ls, { size: 15 }),
      render: $u
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: /* @__PURE__ */ r.jsx(gn, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(y1, { testRun: Mo, onOpenRun: _u })
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ r.jsx(Sd, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(
        Hv,
        {
          context: e,
          ai: n,
          definitionId: c.definition.id,
          publishedArtifactId: Y
        }
      )
    },
    ...Ds.filter((x) => x.side === "right")
  ].sort(jc), Ts = Ji.find((x) => x.id === Ve) ?? Ji[0], $s = Qi.find((x) => x.id === Ge) ?? Qi[0], Du = [
    { id: "designer", title: "Designer", order: 0, icon: /* @__PURE__ */ r.jsx(d0, { size: 14 }), render: () => null },
    { id: "code", title: "Code", order: 1, icon: /* @__PURE__ */ r.jsx(t0, { size: 14 }), render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: /* @__PURE__ */ r.jsx(us, { size: 14 }), render: () => null }
  ];
  function Tu() {
    const x = ee.trim().length > 0;
    return /* @__PURE__ */ r.jsxs("div", { className: "wf-palette-body", children: [
      /* @__PURE__ */ r.jsxs("label", { className: "wf-palette-search", children: [
        /* @__PURE__ */ r.jsx(Di, { size: 14, "aria-hidden": "true" }),
        /* @__PURE__ */ r.jsx(
          "input",
          {
            type: "search",
            value: ee,
            placeholder: "Search activities",
            "aria-label": "Search activity palette",
            onChange: (C) => de(C.target.value)
          }
        )
      ] }),
      /* @__PURE__ */ r.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: No.length === 0 ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted wf-palette-empty", children: "No matching activities." }) : No.map((C) => {
        const R = x || ce.has(C.category);
        return /* @__PURE__ */ r.jsxs("div", { className: "wf-palette-category", children: [
          /* @__PURE__ */ r.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-category-toggle",
              role: "treeitem",
              "aria-expanded": R,
              onClick: () => Cu(C.category),
              children: [
                R ? /* @__PURE__ */ r.jsx(e0, { size: 14 }) : /* @__PURE__ */ r.jsx(en, { size: 14 }),
                /* @__PURE__ */ r.jsx("span", { children: C.category }),
                /* @__PURE__ */ r.jsx("small", { children: C.activities.length })
              ]
            }
          ),
          R ? /* @__PURE__ */ r.jsx("div", { className: "wf-palette-activities", role: "group", children: C.activities.map((W) => {
            const U = W.description?.trim(), le = U ? `wf-palette-description-${W.activityVersionId}` : void 0, ie = Se(W), ye = gi(W);
            return /* @__PURE__ */ r.jsxs(
              "button",
              {
                type: "button",
                className: "wf-palette-activity",
                role: "treeitem",
                draggable: !0,
                title: U || Se(W),
                "aria-describedby": le,
                onClick: () => Bi(W),
                onDragStart: (xe) => Hi(xe, W),
                onDragEnd: (xe) => Oi(xe, W),
                onPointerDown: (xe) => Fi(xe, W),
                children: [
                  /* @__PURE__ */ r.jsx("span", { className: "wf-activity-icon", "data-icon": ye, "aria-hidden": "true", children: ys(ye) }),
                  /* @__PURE__ */ r.jsxs("span", { className: "wf-palette-activity-text", children: [
                    /* @__PURE__ */ r.jsx("strong", { children: ie }),
                    U ? /* @__PURE__ */ r.jsx("small", { id: le, children: U }) : null
                  ] }),
                  /* @__PURE__ */ r.jsx(i0, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
                ]
              },
              W.activityVersionId
            );
          }) }) : null
        ] }, C.category);
      }) })
    ] });
  }
  function $u() {
    return me ? /* @__PURE__ */ r.jsxs("div", { className: "wf-inspector-content", children: [
      /* @__PURE__ */ r.jsx("h3", { children: j.find((x) => x.id === me.nodeId)?.data.label ?? me.nodeId }),
      /* @__PURE__ */ r.jsxs("dl", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Node ID" }),
        /* @__PURE__ */ r.jsx("dd", { children: me.nodeId }),
        /* @__PURE__ */ r.jsx("dt", { children: "Activity type" }),
        /* @__PURE__ */ r.jsx("dd", { children: At?.typeName ?? Ue.get(me.activityVersionId)?.activityTypeKey ?? "Unknown" }),
        /* @__PURE__ */ r.jsx("dt", { children: "Activity version" }),
        /* @__PURE__ */ r.jsx("dd", { children: me.activityVersionId })
      ] }),
      jo ? /* @__PURE__ */ r.jsxs("div", { className: "wf-availability-notice", children: [
        /* @__PURE__ */ r.jsx(fi, { size: 14 }),
        /* @__PURE__ */ r.jsxs("span", { children: [
          "No longer available for new use · ",
          hi(jo.state)
        ] })
      ] }) : null,
      /* @__PURE__ */ r.jsx(
        Iw,
        {
          activity: me,
          descriptor: At,
          editors: o,
          expressionEditors: i,
          expressionDescriptors: m,
          descriptorStatus: y,
          onChange: Eu
        }
      ),
      kn.length > 0 ? /* @__PURE__ */ r.jsxs("div", { className: "wf-slot-list", children: [
        /* @__PURE__ */ r.jsx("span", { children: "Embedded slots" }),
        kn.map((x) => /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => ku(me, x.id, `${j.find((C) => C.id === me.nodeId)?.data.label ?? me.nodeId} / ${x.label}`), children: [
          x.label,
          /* @__PURE__ */ r.jsxs("small", { children: [
            x.activities.length,
            " activit",
            x.activities.length === 1 ? "y" : "ies"
          ] })
        ] }, x.id))
      ] }) : /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
    ] }) : /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
  }
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-link-button", onClick: a, children: "Definitions" }),
      /* @__PURE__ */ r.jsx(en, { size: 14 }),
      /* @__PURE__ */ r.jsx("strong", { children: c.definition.name }),
      /* @__PURE__ */ r.jsx("span", { className: "wf-chip", children: "Draft" }),
      Ms ? /* @__PURE__ */ r.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ r.jsx(xn, { size: 13 }),
        " ",
        Ms
      ] }) : null,
      /* @__PURE__ */ r.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-canvas-tools", role: "group", "aria-label": "Canvas tools", children: [
          /* @__PURE__ */ r.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Undo",
              title: "Undo (Ctrl+Z)",
              disabled: !Ao,
              onClick: Jt,
              children: /* @__PURE__ */ r.jsx(l0, { size: 16 })
            }
          ),
          /* @__PURE__ */ r.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Redo",
              title: "Redo (Ctrl+Shift+Z)",
              disabled: !Xi,
              onClick: Tn,
              children: /* @__PURE__ */ r.jsx(a0, { size: 16 })
            }
          ),
          /* @__PURE__ */ r.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Auto-layout",
              title: "Auto-layout the canvas",
              disabled: !yu,
              onClick: mu,
              children: /* @__PURE__ */ r.jsx(s0, { size: 16 })
            }
          )
        ] }),
        /* @__PURE__ */ r.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ r.jsx("input", { className: "wf-autosave-switch-input", type: "checkbox", checked: G, onChange: (x) => V(x.target.checked) }),
          /* @__PURE__ */ r.jsx("span", { children: "Autosave" })
        ] }),
        ko ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => St(n, ko, { definition: c.definition, draft: l }), children: [
          /* @__PURE__ */ r.jsx(jt, { size: 15 }),
          " Risks"
        ] }) : null,
        Eo ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => St(n, Eo, { definition: c.definition, draft: l }), children: [
          /* @__PURE__ */ r.jsx(jt, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ r.jsxs("button", { type: "button", title: "Export workflow as JSON", onClick: Ki, children: [
          /* @__PURE__ */ r.jsx(o0, { size: 15 }),
          " Export"
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: ft, onClick: () => {
          Yi();
        }, children: [
          /* @__PURE__ */ r.jsx(kd, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: ft, onClick: () => {
          $n();
        }, children: [
          /* @__PURE__ */ r.jsx(jd, { size: 15 }),
          " Promote"
        ] }),
        Mo ? /* @__PURE__ */ r.jsx(
          g1,
          {
            testRun: Mo,
            onOpenDetails: () => {
              Je("runtime"), Xe(!1);
            }
          }
        ) : null,
        /* @__PURE__ */ r.jsxs(
          "button",
          {
            type: "button",
            disabled: !So,
            title: l.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              Ui();
            },
            children: [
              /* @__PURE__ */ r.jsx(gn, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    P ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ r.jsx(kt, { size: 16 }),
      " ",
      P
    ] }) : null,
    /* @__PURE__ */ r.jsxs("div", { className: Iu, style: Au, children: [
      /* @__PURE__ */ r.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ r.jsx(
            Go,
            {
              label: "Activities panel tabs",
              tabs: Ji,
              activeTabId: Ts.id,
              onSelect: Ze
            }
          ),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": Ee ? "Expand activities panel" : "Collapse activities panel",
                title: Ee ? "Expand" : "Collapse",
                onClick: () => Ss("palette"),
                children: Ee ? /* @__PURE__ */ r.jsx(en, { size: 14 }) : /* @__PURE__ */ r.jsx(li, { size: 14 })
              }
            ),
            Ee ? null : /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": be === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: be === "palette" ? "Restore" : "Maximize",
                onClick: () => ks("palette"),
                children: be === "palette" ? /* @__PURE__ */ r.jsx(qa, { size: 14 }) : /* @__PURE__ */ r.jsx(di, { size: 14 })
              }
            )
          ] })
        ] }),
        As ? Ts.render() : null
      ] }),
      As && !be ? /* @__PURE__ */ r.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Vn,
          "aria-valuemax": Hn,
          "aria-valuenow": O,
          tabIndex: 0,
          onPointerDown: (x) => Cs("palette", x),
          onKeyDown: (x) => Is("palette", x)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ r.jsx("div", { className: "wf-canvas-tabs", children: /* @__PURE__ */ r.jsx(
          Go,
          {
            label: "Editor view tabs",
            tabs: Du,
            activeTabId: Et,
            onSelect: (x) => wn(x)
          }
        ) }),
        Et === "code" ? /* @__PURE__ */ r.jsx(ov, { draft: l, onApply: qi }) : Et === "properties" ? /* @__PURE__ */ r.jsx(cv, { details: c, draft: l }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
          /* @__PURE__ */ r.jsxs("div", { className: "wf-breadcrumb", children: [
            /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => {
              E([]), N(null);
            }, children: "Root" }),
            S.map((x, C) => /* @__PURE__ */ r.jsxs(ct.Fragment, { children: [
              /* @__PURE__ */ r.jsx(en, { size: 13 }),
              /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => {
                E(S.slice(0, C + 1)), N(null);
              }, children: x.label })
            ] }, `${x.ownerNodeId}-${x.slotId}-${C}`))
          ] }),
          /* @__PURE__ */ r.jsxs("div", { className: "wf-canvas", ref: Ye, onDragOver: Mn, onDragLeave: Co, onDrop: Io, children: [
            /* @__PURE__ */ r.jsx(Qd.Provider, { value: Su, children: /* @__PURE__ */ r.jsx(eu.Provider, { value: Sn, children: /* @__PURE__ */ r.jsxs(
              gd,
              {
                nodes: j,
                edges: D,
                nodeTypes: Zd,
                edgeTypes: Gd,
                onInit: T,
                onNodesChange: Zi,
                onEdgesChange: _o,
                onNodesDelete: bu,
                onEdgesDelete: Nu,
                onConnect: pu,
                onConnectStart: Ae ? xu : void 0,
                onConnectEnd: Ae ? wu : void 0,
                onReconnect: Ae ? vu : void 0,
                isValidConnection: Gi,
                onDragOver: Mn,
                onDragLeave: Co,
                onDrop: Io,
                onPaneClick: () => N(null),
                onNodeClick: (x, C) => N(C.id),
                onNodeDragStop: we ? void 0 : gu,
                fitView: !0,
                minZoom: 0.2,
                maxZoom: 1.8,
                nodesConnectable: Ae,
                nodesDraggable: !we,
                selectionOnDrag: !0,
                multiSelectionKeyCode: ["Shift", "Meta", "Control"],
                deleteKeyCode: we ? null : ["Backspace", "Delete"],
                panActivationKeyCode: null,
                defaultEdgeOptions: { type: "workflow" },
                children: [
                  /* @__PURE__ */ r.jsx(md, { gap: 18, size: 1 }),
                  /* @__PURE__ */ r.jsx(wd, {}),
                  /* @__PURE__ */ r.jsx(bd, { pannable: !0, zoomable: !0 })
                ]
              }
            ) }) }),
            Ae && j.length === 0 ? /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => Wi(), children: [
              /* @__PURE__ */ r.jsx(ui, { size: 15 }),
              " Add activity"
            ] }) : null,
            A ? /* @__PURE__ */ r.jsx(
              h1,
              {
                clientX: A.clientX,
                clientY: A.clientY,
                activities: f,
                onPick: ju,
                onClose: () => I(null)
              }
            ) : null
          ] }),
          /* @__PURE__ */ r.jsx(p1, { draft: l })
        ] })
      ] }),
      _s && !be ? /* @__PURE__ */ r.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": On,
          "aria-valuemax": Fn,
          "aria-valuenow": ge,
          tabIndex: 0,
          onPointerDown: (x) => Cs("inspector", x),
          onKeyDown: (x) => Is("inspector", x)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ r.jsx(
            Go,
            {
              label: "Inspector panel tabs",
              tabs: Qi,
              activeTabId: $s.id,
              onSelect: Je
            }
          ),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": Ie ? "Expand inspector panel" : "Collapse inspector panel",
                title: Ie ? "Expand" : "Collapse",
                onClick: () => Ss("inspector"),
                children: Ie ? /* @__PURE__ */ r.jsx(li, { size: 14 }) : /* @__PURE__ */ r.jsx(en, { size: 14 })
              }
            ),
            Ie ? null : /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": be === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: be === "inspector" ? "Restore" : "Maximize",
                onClick: () => ks("inspector"),
                children: be === "inspector" ? /* @__PURE__ */ r.jsx(qa, { size: 14 }) : /* @__PURE__ */ r.jsx(di, { size: 14 })
              }
            )
          ] })
        ] }),
        _s ? $s.render() : null
      ] })
    ] })
  ] });
}
function Go({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: o
}) {
  return /* @__PURE__ */ r.jsx("div", { className: "wf-panel-tab-list", role: "tablist", "aria-label": e, children: t.map((i) => /* @__PURE__ */ r.jsxs(
    "button",
    {
      type: "button",
      role: "tab",
      "aria-selected": i.id === n,
      className: i.id === n ? "active" : "",
      title: i.title,
      onClick: () => o(i.id),
      children: [
        i.icon ? /* @__PURE__ */ r.jsx("span", { className: "wf-panel-tab-icon", "aria-hidden": "true", children: i.icon }) : null,
        /* @__PURE__ */ r.jsx("span", { children: i.title })
      ]
    },
    i.id
  )) });
}
function jc(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
function d1({ data: e, selected: t }) {
  const n = e, o = n.runtime, i = !n.suppressFlowPorts, s = i ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], a = u1(n), d = ct.useContext(eu)?.({ activityVersionId: n.activityVersionId, activityTypeKey: n.activityTypeKey }) ?? null;
  return /* @__PURE__ */ r.jsxs(
    "div",
    {
      className: ["wf-node", t ? "selected" : "", o ? "wf-node-runtime" : "", o?.hasBlockingIncident ? "faulted" : "", d ? "wf-node-unavailable" : ""].filter(Boolean).join(" "),
      "data-icon": n.icon ?? "activity",
      children: [
        i && n.acceptsInbound ? /* @__PURE__ */ r.jsx(pn, { type: "target", position: te.Left }) : null,
        d ? /* @__PURE__ */ r.jsx("span", { className: "wf-node-availability", title: `No longer available for new use · ${hi(d.state)}`, children: /* @__PURE__ */ r.jsx(fi, { size: 13 }) }) : null,
        /* @__PURE__ */ r.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ r.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: ys(n.icon) }),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-node-copy", children: [
            /* @__PURE__ */ r.jsx("strong", { children: n.label }),
            a ? /* @__PURE__ */ r.jsx("small", { children: a }) : null
          ] })
        ] }),
        n.childSlots.length > 0 ? /* @__PURE__ */ r.jsxs("span", { className: "wf-node-slot-badge", children: [
          n.childSlots.length,
          " slot",
          n.childSlots.length === 1 ? "" : "s"
        ] }) : null,
        o ? /* @__PURE__ */ r.jsxs("div", { className: "wf-node-runtime-strip", children: [
          o.status ? /* @__PURE__ */ r.jsx(xo, { status: o.status, subStatus: o.subStatus }) : null,
          o.incidentCount > 0 ? /* @__PURE__ */ r.jsxs("span", { className: "wf-node-runtime-count", children: [
            o.incidentCount,
            " incident",
            o.incidentCount === 1 ? "" : "s"
          ] }) : null,
          o.faultCount > 0 ? /* @__PURE__ */ r.jsxs("span", { className: "wf-node-runtime-count", children: [
            o.faultCount,
            " faults"
          ] }) : null
        ] }) : null,
        s.map((l, u) => {
          const f = `${(u + 1) / (s.length + 1) * 100}%`;
          return /* @__PURE__ */ r.jsxs(ct.Fragment, { children: [
            /* @__PURE__ */ r.jsx("span", { className: "wf-node-port-label", style: { top: f }, children: l.displayName }),
            /* @__PURE__ */ r.jsx(pn, { type: "source", position: te.Right, id: l.name, style: { top: f } })
          ] }, l.name);
        })
      ]
    }
  );
}
function u1(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((i) => !!i).join(" · ");
}
function f1(e) {
  const {
    id: t,
    sourceX: n,
    sourceY: o,
    targetX: i,
    targetY: s,
    sourcePosition: a,
    targetPosition: c,
    markerEnd: d,
    style: l,
    label: u,
    labelStyle: f
  } = e, h = ct.useContext(Qd), [p, g] = q(!1), [v, w, m] = ai({ sourceX: n, sourceY: o, targetX: i, targetY: s, sourcePosition: a, targetPosition: c }), k = h?.highlightedEdgeId === t;
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsx(
      go,
      {
        id: t,
        path: v,
        markerEnd: d,
        style: {
          ...l,
          strokeWidth: k ? 2.5 : l?.strokeWidth
        },
        label: u,
        labelX: w,
        labelY: m,
        labelStyle: f,
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1)
      }
    ),
    h ? /* @__PURE__ */ r.jsx(Sx, { children: /* @__PURE__ */ r.jsxs(
      "div",
      {
        className: ["wf-edge-actions", p ? "visible" : "", k ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${w}px, ${m}px)` },
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1),
        children: [
          /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (y) => h.requestInsertActivity(t, y.clientX, y.clientY), children: /* @__PURE__ */ r.jsx(ui, { size: 12 }) }),
          /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => h.deleteEdge(t), children: /* @__PURE__ */ r.jsx($r, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function h1({ clientX: e, clientY: t, activities: n, onPick: o, onClose: i }) {
  const [s, a] = q(""), [c, d] = q(0), l = re(null), u = re(null), f = fe(() => {
    const k = s.trim().toLowerCase(), y = n.filter(au);
    return k ? y.filter((b) => Se(b).toLowerCase().includes(k) || b.activityTypeKey.toLowerCase().includes(k) || (b.category ?? "").toLowerCase().includes(k) || (b.description ?? "").toLowerCase().includes(k)) : y;
  }, [n, s]), h = fe(() => Fr(f), [f]), p = fe(() => h.flatMap((k) => k.activities), [h]);
  oe(() => {
    requestAnimationFrame(() => u.current?.focus());
  }, []), oe(() => {
    const k = (b) => {
      l.current?.contains(b.target) || i();
    }, y = (b) => {
      b.key === "Escape" && i();
    };
    return document.addEventListener("mousedown", k, !0), document.addEventListener("keydown", y), () => {
      document.removeEventListener("mousedown", k, !0), document.removeEventListener("keydown", y);
    };
  }, [i]);
  const g = (k) => {
    if (k.key === "ArrowDown")
      k.preventDefault(), d((y) => Math.min(y + 1, p.length - 1));
    else if (k.key === "ArrowUp")
      k.preventDefault(), d((y) => Math.max(y - 1, 0));
    else if (k.key === "Enter") {
      k.preventDefault();
      const y = p[c];
      y && o(y);
    }
  }, v = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), w = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let m = -1;
  return /* @__PURE__ */ r.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: v, top: w }, onMouseDown: (k) => k.stopPropagation(), onClick: (k) => k.stopPropagation(), children: [
    /* @__PURE__ */ r.jsx(
      "input",
      {
        ref: u,
        type: "search",
        value: s,
        placeholder: "Search activities...",
        "aria-label": "Search activities",
        onChange: (k) => {
          a(k.target.value), d(0);
        },
        onKeyDown: g
      }
    ),
    /* @__PURE__ */ r.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: h.length === 0 ? /* @__PURE__ */ r.jsx("p", { children: "No matching activities." }) : h.map((k) => /* @__PURE__ */ r.jsxs("section", { children: [
      /* @__PURE__ */ r.jsx("h4", { children: k.category }),
      k.activities.map((y) => {
        m += 1;
        const b = m, S = b === c;
        return /* @__PURE__ */ r.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": S,
            className: S ? "active" : "",
            onMouseEnter: () => d(b),
            onClick: () => o(y),
            children: [
              /* @__PURE__ */ r.jsx("strong", { children: Se(y) }),
              /* @__PURE__ */ r.jsx("small", { children: y.category || y.activityTypeKey })
            ]
          },
          y.activityVersionId
        );
      })
    ] }, k.category)) })
  ] });
}
function p1({ draft: e }) {
  return e.validationErrors.length ? /* @__PURE__ */ r.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ r.jsx(kt, { size: 14 }),
    e.validationErrors.length,
    " validation issue",
    e.validationErrors.length === 1 ? "" : "s"
  ] }) : /* @__PURE__ */ r.jsxs("div", { className: "wf-validation ok", children: [
    /* @__PURE__ */ r.jsx(xn, { size: 14 }),
    " No validation errors"
  ] });
}
function g1({
  testRun: e,
  onOpenDetails: t
}) {
  const n = bs(e);
  return /* @__PURE__ */ r.jsx("div", { className: "wf-test-run-status", "data-state": n ? "rejected" : "accepted", children: /* @__PURE__ */ r.jsxs(
    "button",
    {
      type: "button",
      className: "wf-test-run-trigger",
      onClick: t,
      children: [
        n ? /* @__PURE__ */ r.jsx(kt, { size: 16 }) : /* @__PURE__ */ r.jsx(xn, { size: 16 }),
        n ? "Test run rejected" : "Test run dispatched"
      ]
    }
  ) });
}
function y1({ testRun: e, onOpenRun: t }) {
  if (!e)
    return /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Run the draft to see Runtime Evidence." }) });
  const n = bs(e), o = e.workflowExecutionId;
  return /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ r.jsxs("section", { className: "wf-runtime-card", "data-state": n ? "rejected" : "accepted", children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("span", { children: "Latest Test Run" }),
        /* @__PURE__ */ r.jsx("h3", { children: n ? "Rejected by the server" : "Transient run accepted" })
      ] }),
      /* @__PURE__ */ r.jsx(xo, { status: e.status, subStatus: e.commandDispatchStatus ?? void 0 })
    ] }),
    /* @__PURE__ */ r.jsx("p", { children: "Ephemeral - not saved, promoted, or published." }),
    n && e.reason ? /* @__PURE__ */ r.jsxs("div", { className: "wf-runtime-reason", children: [
      /* @__PURE__ */ r.jsx(kt, { size: 14 }),
      " ",
      e.reason
    ] }) : null,
    /* @__PURE__ */ r.jsxs("dl", { className: "wf-runtime-meta", children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Dispatch" }),
        /* @__PURE__ */ r.jsx("dd", { title: e.commandDispatchStatus ?? e.status, children: e.commandDispatchStatus ?? e.status })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Test Run" }),
        /* @__PURE__ */ r.jsx("dd", { title: e.testRunId, children: e.testRunId })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Artifact" }),
        /* @__PURE__ */ r.jsx("dd", { title: e.artifactId ?? "None", children: e.artifactId ?? "None" })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Run / Instance" }),
        /* @__PURE__ */ r.jsx("dd", { title: o ?? "None", children: o ? /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => t(o), children: o }) : "None" })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Activities" }),
        /* @__PURE__ */ r.jsx("dd", { children: Sc(e.activityCount, "activity") })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ r.jsx("dd", { children: Sc(e.incidentCount, "incident") })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ r.jsx("dd", { title: e.expiresAt ? it(e.expiresAt) : "None", children: e.expiresAt ? it(e.expiresAt) : "None" })
      ] })
    ] })
  ] }) });
}
function Sc(e, t) {
  return typeof e != "number" ? "Available on linked Run" : `${e} ${t}${e === 1 ? "" : "s"}`;
}
function kc(e) {
  return `${Se(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function Ec(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function m1(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function uu(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function x1(e) {
  const t = uu(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function w1(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function Be(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function v1(e) {
  return hu(Be(e));
}
function fu(e, t, n = []) {
  if (!e) return n;
  const o = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: o?.activityTypeKey ?? e.activityVersionId,
    displayName: o ? Se(o) : void 0
  });
  for (const i of ot(e))
    for (const s of i.activities) fu(s, t, n);
  return n;
}
function Wn(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function b1(e) {
  return `${e.id}-${hu(JSON.stringify(e.state))}`;
}
function hu(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function bs(e) {
  return e.status.toLowerCase() === "rejected";
}
function N1(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function j1(e, t) {
  const n = e instanceof Error ? e.message : String(e);
  return S1(e, n) ? `Run ${t} was not found.` : n;
}
function S1(e, t) {
  if ((typeof e == "object" && e ? e.response?.status ?? e.status : void 0) === 404 || /\b404\b/.test(t)) return !0;
  try {
    const o = JSON.parse(t);
    return [o.error, o.title, o.detail].some((i) => typeof i == "string" && /not found/i.test(i));
  } catch {
    return /not found/i.test(t);
  }
}
export {
  x1 as isConnectEndOverExistingWorkflowNode,
  C1 as register,
  w1 as resolveConnectEndSource
};
