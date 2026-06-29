import ct, { memo as ke, forwardRef as vi, useRef as re, useEffect as te, useCallback as fe, useContext as lo, useMemo as ue, useState as q, createContext as Fr, useLayoutEffect as Wd, createElement as br, useId as _c, lazy as Kd, Suspense as qd } from "react";
import { useQuery as Dc, useQueryClient as Xd, useMutation as Ud } from "@tanstack/react-query";
function Yd(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var or = { exports: {} }, Pn = {};
var Vs;
function Zd() {
  if (Vs) return Pn;
  Vs = 1;
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
var Os;
function Gd() {
  return Os || (Os = 1, or.exports = Zd()), or.exports;
}
var r = Gd();
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
var Jd = { value: () => {
} };
function bi() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new Fo(n);
}
function Fo(e) {
  this._ = e;
}
function Qd(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", i = n.indexOf(".");
    if (i >= 0 && (o = n.slice(i + 1), n = n.slice(0, i)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
Fo.prototype = bi.prototype = {
  constructor: Fo,
  on: function(e, t) {
    var n = this._, o = Qd(e + "", n), i, s = -1, a = o.length;
    if (arguments.length < 2) {
      for (; ++s < a; ) if ((i = (e = o[s]).type) && (i = ef(n[i], e.name))) return i;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++s < a; )
      if (i = (e = o[s]).type) n[i] = Hs(n[i], e.name, t);
      else if (t == null) for (i in n) n[i] = Hs(n[i], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new Fo(e);
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
function ef(e, t) {
  for (var n = 0, o = e.length, i; n < o; ++n)
    if ((i = e[n]).name === t)
      return i.value;
}
function Hs(e, t, n) {
  for (var o = 0, i = e.length; o < i; ++o)
    if (e[o].name === t) {
      e[o] = Jd, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var Nr = "http://www.w3.org/1999/xhtml";
const Bs = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: Nr,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function Ni(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Bs.hasOwnProperty(t) ? { space: Bs[t], local: e } : e;
}
function tf(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === Nr && t.documentElement.namespaceURI === Nr ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function nf(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function Mc(e) {
  var t = Ni(e);
  return (t.local ? nf : tf)(t);
}
function of() {
}
function Wr(e) {
  return e == null ? of : function() {
    return this.querySelector(e);
  };
}
function rf(e) {
  typeof e != "function" && (e = Wr(e));
  for (var t = this._groups, n = t.length, o = new Array(n), i = 0; i < n; ++i)
    for (var s = t[i], a = s.length, c = o[i] = new Array(a), u, l, d = 0; d < a; ++d)
      (u = s[d]) && (l = e.call(u, u.__data__, d, s)) && ("__data__" in u && (l.__data__ = u.__data__), c[d] = l);
  return new We(o, this._parents);
}
function sf(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function af() {
  return [];
}
function Tc(e) {
  return e == null ? af : function() {
    return this.querySelectorAll(e);
  };
}
function cf(e) {
  return function() {
    return sf(e.apply(this, arguments));
  };
}
function lf(e) {
  typeof e == "function" ? e = cf(e) : e = Tc(e);
  for (var t = this._groups, n = t.length, o = [], i = [], s = 0; s < n; ++s)
    for (var a = t[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && (o.push(e.call(u, u.__data__, l, a)), i.push(u));
  return new We(o, i);
}
function $c(e) {
  return function() {
    return this.matches(e);
  };
}
function Pc(e) {
  return function(t) {
    return t.matches(e);
  };
}
var uf = Array.prototype.find;
function df(e) {
  return function() {
    return uf.call(this.children, e);
  };
}
function ff() {
  return this.firstElementChild;
}
function hf(e) {
  return this.select(e == null ? ff : df(typeof e == "function" ? e : Pc(e)));
}
var pf = Array.prototype.filter;
function gf() {
  return Array.from(this.children);
}
function yf(e) {
  return function() {
    return pf.call(this.children, e);
  };
}
function mf(e) {
  return this.selectAll(e == null ? gf : yf(typeof e == "function" ? e : Pc(e)));
}
function xf(e) {
  typeof e != "function" && (e = $c(e));
  for (var t = this._groups, n = t.length, o = new Array(n), i = 0; i < n; ++i)
    for (var s = t[i], a = s.length, c = o[i] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new We(o, this._parents);
}
function zc(e) {
  return new Array(e.length);
}
function wf() {
  return new We(this._enter || this._groups.map(zc), this._parents);
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
function vf(e) {
  return function() {
    return e;
  };
}
function bf(e, t, n, o, i, s) {
  for (var a = 0, c, u = t.length, l = s.length; a < l; ++a)
    (c = t[a]) ? (c.__data__ = s[a], o[a] = c) : n[a] = new Jo(e, s[a]);
  for (; a < u; ++a)
    (c = t[a]) && (i[a] = c);
}
function Nf(e, t, n, o, i, s, a) {
  var c, u, l = /* @__PURE__ */ new Map(), d = t.length, f = s.length, h = new Array(d), p;
  for (c = 0; c < d; ++c)
    (u = t[c]) && (h[c] = p = a.call(u, u.__data__, c, t) + "", l.has(p) ? i[c] = u : l.set(p, u));
  for (c = 0; c < f; ++c)
    p = a.call(e, s[c], c, s) + "", (u = l.get(p)) ? (o[c] = u, u.__data__ = s[c], l.delete(p)) : n[c] = new Jo(e, s[c]);
  for (c = 0; c < d; ++c)
    (u = t[c]) && l.get(h[c]) === u && (i[c] = u);
}
function jf(e) {
  return e.__data__;
}
function Sf(e, t) {
  if (!arguments.length) return Array.from(this, jf);
  var n = t ? Nf : bf, o = this._parents, i = this._groups;
  typeof e != "function" && (e = vf(e));
  for (var s = i.length, a = new Array(s), c = new Array(s), u = new Array(s), l = 0; l < s; ++l) {
    var d = o[l], f = i[l], h = f.length, p = kf(e.call(d, d && d.__data__, l, o)), g = p.length, v = c[l] = new Array(g), w = a[l] = new Array(g), m = u[l] = new Array(h);
    n(d, f, v, w, m, p, t);
    for (var k = 0, y = 0, b, S; k < g; ++k)
      if (b = v[k]) {
        for (k >= y && (y = k + 1); !(S = w[y]) && ++y < g; ) ;
        b._next = S || null;
      }
  }
  return a = new We(a, o), a._enter = c, a._exit = u, a;
}
function kf(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function Ef() {
  return new We(this._exit || this._groups.map(zc), this._parents);
}
function Cf(e, t, n) {
  var o = this.enter(), i = this, s = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (i = t(i), i && (i = i.selection())), n == null ? s.remove() : n(s), o && i ? o.merge(i).order() : i;
}
function If(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, i = n.length, s = o.length, a = Math.min(i, s), c = new Array(i), u = 0; u < a; ++u)
    for (var l = n[u], d = o[u], f = l.length, h = c[u] = new Array(f), p, g = 0; g < f; ++g)
      (p = l[g] || d[g]) && (h[g] = p);
  for (; u < i; ++u)
    c[u] = n[u];
  return new We(c, this._parents);
}
function Af() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], i = o.length - 1, s = o[i], a; --i >= 0; )
      (a = o[i]) && (s && a.compareDocumentPosition(s) ^ 4 && s.parentNode.insertBefore(a, s), s = a);
  return this;
}
function _f(e) {
  e || (e = Df);
  function t(f, h) {
    return f && h ? e(f.__data__, h.__data__) : !f - !h;
  }
  for (var n = this._groups, o = n.length, i = new Array(o), s = 0; s < o; ++s) {
    for (var a = n[s], c = a.length, u = i[s] = new Array(c), l, d = 0; d < c; ++d)
      (l = a[d]) && (u[d] = l);
    u.sort(t);
  }
  return new We(i, this._parents).order();
}
function Df(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function Mf() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function Tf() {
  return Array.from(this);
}
function $f() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], i = 0, s = o.length; i < s; ++i) {
      var a = o[i];
      if (a) return a;
    }
  return null;
}
function Pf() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function zf() {
  return !this.node();
}
function Rf(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var i = t[n], s = 0, a = i.length, c; s < a; ++s)
      (c = i[s]) && e.call(c, c.__data__, s, i);
  return this;
}
function Lf(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Vf(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Of(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function Hf(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function Bf(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function Ff(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function Wf(e, t) {
  var n = Ni(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? Vf : Lf : typeof t == "function" ? n.local ? Ff : Bf : n.local ? Hf : Of)(n, t));
}
function Rc(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function Kf(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function qf(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function Xf(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function Uf(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? Kf : typeof t == "function" ? Xf : qf)(e, t, n ?? "")) : an(this.node(), e);
}
function an(e, t) {
  return e.style.getPropertyValue(t) || Rc(e).getComputedStyle(e, null).getPropertyValue(t);
}
function Yf(e) {
  return function() {
    delete this[e];
  };
}
function Zf(e, t) {
  return function() {
    this[e] = t;
  };
}
function Gf(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function Jf(e, t) {
  return arguments.length > 1 ? this.each((t == null ? Yf : typeof t == "function" ? Gf : Zf)(e, t)) : this.node()[e];
}
function Lc(e) {
  return e.trim().split(/^|\s+/);
}
function Kr(e) {
  return e.classList || new Vc(e);
}
function Vc(e) {
  this._node = e, this._names = Lc(e.getAttribute("class") || "");
}
Vc.prototype = {
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
function Oc(e, t) {
  for (var n = Kr(e), o = -1, i = t.length; ++o < i; ) n.add(t[o]);
}
function Hc(e, t) {
  for (var n = Kr(e), o = -1, i = t.length; ++o < i; ) n.remove(t[o]);
}
function Qf(e) {
  return function() {
    Oc(this, e);
  };
}
function eh(e) {
  return function() {
    Hc(this, e);
  };
}
function th(e, t) {
  return function() {
    (t.apply(this, arguments) ? Oc : Hc)(this, e);
  };
}
function nh(e, t) {
  var n = Lc(e + "");
  if (arguments.length < 2) {
    for (var o = Kr(this.node()), i = -1, s = n.length; ++i < s; ) if (!o.contains(n[i])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? th : t ? Qf : eh)(n, t));
}
function oh() {
  this.textContent = "";
}
function ih(e) {
  return function() {
    this.textContent = e;
  };
}
function rh(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function sh(e) {
  return arguments.length ? this.each(e == null ? oh : (typeof e == "function" ? rh : ih)(e)) : this.node().textContent;
}
function ah() {
  this.innerHTML = "";
}
function ch(e) {
  return function() {
    this.innerHTML = e;
  };
}
function lh(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function uh(e) {
  return arguments.length ? this.each(e == null ? ah : (typeof e == "function" ? lh : ch)(e)) : this.node().innerHTML;
}
function dh() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function fh() {
  return this.each(dh);
}
function hh() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function ph() {
  return this.each(hh);
}
function gh(e) {
  var t = typeof e == "function" ? e : Mc(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function yh() {
  return null;
}
function mh(e, t) {
  var n = typeof e == "function" ? e : Mc(e), o = t == null ? yh : typeof t == "function" ? t : Wr(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function xh() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function wh() {
  return this.each(xh);
}
function vh() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function bh() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Nh(e) {
  return this.select(e ? bh : vh);
}
function jh(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function Sh(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function kh(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function Eh(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, i = t.length, s; n < i; ++n)
        s = t[n], (!e.type || s.type === e.type) && s.name === e.name ? this.removeEventListener(s.type, s.listener, s.options) : t[++o] = s;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function Ch(e, t, n) {
  return function() {
    var o = this.__on, i, s = Sh(t);
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
function Ih(e, t, n) {
  var o = kh(e + ""), i, s = o.length, a;
  if (arguments.length < 2) {
    var c = this.node().__on;
    if (c) {
      for (var u = 0, l = c.length, d; u < l; ++u)
        for (i = 0, d = c[u]; i < s; ++i)
          if ((a = o[i]).type === d.type && a.name === d.name)
            return d.value;
    }
    return;
  }
  for (c = t ? Ch : Eh, i = 0; i < s; ++i) this.each(c(o[i], t, n));
  return this;
}
function Bc(e, t, n) {
  var o = Rc(e), i = o.CustomEvent;
  typeof i == "function" ? i = new i(t, n) : (i = o.document.createEvent("Event"), n ? (i.initEvent(t, n.bubbles, n.cancelable), i.detail = n.detail) : i.initEvent(t, !1, !1)), e.dispatchEvent(i);
}
function Ah(e, t) {
  return function() {
    return Bc(this, e, t);
  };
}
function _h(e, t) {
  return function() {
    return Bc(this, e, t.apply(this, arguments));
  };
}
function Dh(e, t) {
  return this.each((typeof t == "function" ? _h : Ah)(e, t));
}
function* Mh() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], i = 0, s = o.length, a; i < s; ++i)
      (a = o[i]) && (yield a);
}
var Fc = [null];
function We(e, t) {
  this._groups = e, this._parents = t;
}
function uo() {
  return new We([[document.documentElement]], Fc);
}
function Th() {
  return this;
}
We.prototype = uo.prototype = {
  constructor: We,
  select: rf,
  selectAll: lf,
  selectChild: hf,
  selectChildren: mf,
  filter: xf,
  data: Sf,
  enter: wf,
  exit: Ef,
  join: Cf,
  merge: If,
  selection: Th,
  order: Af,
  sort: _f,
  call: Mf,
  nodes: Tf,
  node: $f,
  size: Pf,
  empty: zf,
  each: Rf,
  attr: Wf,
  style: Uf,
  property: Jf,
  classed: nh,
  text: sh,
  html: uh,
  raise: fh,
  lower: ph,
  append: gh,
  insert: mh,
  remove: wh,
  clone: Nh,
  datum: jh,
  on: Ih,
  dispatch: Dh,
  [Symbol.iterator]: Mh
};
function Fe(e) {
  return typeof e == "string" ? new We([[document.querySelector(e)]], [document.documentElement]) : new We([[e]], Fc);
}
function $h(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Je(e, t) {
  if (e = $h(e), t === void 0 && (t = e.currentTarget), t) {
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
const Ph = { passive: !1 }, Gn = { capture: !0, passive: !1 };
function ir(e) {
  e.stopImmediatePropagation();
}
function on(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Wc(e) {
  var t = e.document.documentElement, n = Fe(e).on("dragstart.drag", on, Gn);
  "onselectstart" in t ? n.on("selectstart.drag", on, Gn) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function Kc(e, t) {
  var n = e.document.documentElement, o = Fe(e).on("dragstart.drag", null);
  t && (o.on("click.drag", on, Gn), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const Mo = (e) => () => e;
function jr(e, {
  sourceEvent: t,
  subject: n,
  target: o,
  identifier: i,
  active: s,
  x: a,
  y: c,
  dx: u,
  dy: l,
  dispatch: d
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
    dx: { value: u, enumerable: !0, configurable: !0 },
    dy: { value: l, enumerable: !0, configurable: !0 },
    _: { value: d }
  });
}
jr.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function zh(e) {
  return !e.ctrlKey && !e.button;
}
function Rh() {
  return this.parentNode;
}
function Lh(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function Vh() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function qc() {
  var e = zh, t = Rh, n = Lh, o = Vh, i = {}, s = bi("start", "drag", "end"), a = 0, c, u, l, d, f = 0;
  function h(b) {
    b.on("mousedown.drag", p).filter(o).on("touchstart.drag", w).on("touchmove.drag", m, Ph).on("touchend.drag touchcancel.drag", k).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function p(b, S) {
    if (!(d || !e.call(this, b, S))) {
      var E = y(this, t.call(this, b, S), b, S, "mouse");
      E && (Fe(b.view).on("mousemove.drag", g, Gn).on("mouseup.drag", v, Gn), Wc(b.view), ir(b), l = !1, c = b.clientX, u = b.clientY, E("start", b));
    }
  }
  function g(b) {
    if (on(b), !l) {
      var S = b.clientX - c, E = b.clientY - u;
      l = S * S + E * E > f;
    }
    i.mouse("drag", b);
  }
  function v(b) {
    Fe(b.view).on("mousemove.drag mouseup.drag", null), Kc(b.view, l), on(b), i.mouse("end", b);
  }
  function w(b, S) {
    if (e.call(this, b, S)) {
      var E = b.changedTouches, j = t.call(this, b, S), _ = E.length, M, F;
      for (M = 0; M < _; ++M)
        (F = y(this, j, b, S, E[M].identifier, E[M])) && (ir(b), F("start", b, E[M]));
    }
  }
  function m(b) {
    var S = b.changedTouches, E = S.length, j, _;
    for (j = 0; j < E; ++j)
      (_ = i[S[j].identifier]) && (on(b), _("drag", b, S[j]));
  }
  function k(b) {
    var S = b.changedTouches, E = S.length, j, _;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), j = 0; j < E; ++j)
      (_ = i[S[j].identifier]) && (ir(b), _("end", b, S[j]));
  }
  function y(b, S, E, j, _, M) {
    var F = s.copy(), D = Je(M || E, S), T, L, N;
    if ((N = n.call(b, new jr("beforestart", {
      sourceEvent: E,
      target: h,
      identifier: _,
      active: a,
      x: D[0],
      y: D[1],
      dx: 0,
      dy: 0,
      dispatch: F
    }), j)) != null)
      return T = N.x - D[0] || 0, L = N.y - D[1] || 0, function A(I, $, z) {
        var P = D, B;
        switch (I) {
          case "start":
            i[_] = A, B = a++;
            break;
          case "end":
            delete i[_], --a;
          // falls through
          case "drag":
            D = Je(z || $, S), B = a;
            break;
        }
        F.call(
          I,
          b,
          new jr(I, {
            sourceEvent: $,
            subject: N,
            target: h,
            identifier: _,
            active: B,
            x: D[0] + T,
            y: D[1] + L,
            dx: D[0] - P[0],
            dy: D[1] - P[1],
            dispatch: F
          }),
          j
        );
      };
  }
  return h.filter = function(b) {
    return arguments.length ? (e = typeof b == "function" ? b : Mo(!!b), h) : e;
  }, h.container = function(b) {
    return arguments.length ? (t = typeof b == "function" ? b : Mo(b), h) : t;
  }, h.subject = function(b) {
    return arguments.length ? (n = typeof b == "function" ? b : Mo(b), h) : n;
  }, h.touchable = function(b) {
    return arguments.length ? (o = typeof b == "function" ? b : Mo(!!b), h) : o;
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
function Xc(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function fo() {
}
var Jn = 0.7, Qo = 1 / Jn, rn = "\\s*([+-]?\\d+)\\s*", Qn = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", at = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", Oh = /^#([0-9a-f]{3,8})$/, Hh = new RegExp(`^rgb\\(${rn},${rn},${rn}\\)$`), Bh = new RegExp(`^rgb\\(${at},${at},${at}\\)$`), Fh = new RegExp(`^rgba\\(${rn},${rn},${rn},${Qn}\\)$`), Wh = new RegExp(`^rgba\\(${at},${at},${at},${Qn}\\)$`), Kh = new RegExp(`^hsl\\(${Qn},${at},${at}\\)$`), qh = new RegExp(`^hsla\\(${Qn},${at},${at},${Qn}\\)$`), Fs = {
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
qr(fo, Lt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Ws,
  // Deprecated! Use color.formatHex.
  formatHex: Ws,
  formatHex8: Xh,
  formatHsl: Uh,
  formatRgb: Ks,
  toString: Ks
});
function Ws() {
  return this.rgb().formatHex();
}
function Xh() {
  return this.rgb().formatHex8();
}
function Uh() {
  return Uc(this).formatHsl();
}
function Ks() {
  return this.rgb().formatRgb();
}
function Lt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = Oh.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? qs(t) : n === 3 ? new ze(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? To(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? To(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = Hh.exec(e)) ? new ze(t[1], t[2], t[3], 1) : (t = Bh.exec(e)) ? new ze(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = Fh.exec(e)) ? To(t[1], t[2], t[3], t[4]) : (t = Wh.exec(e)) ? To(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = Kh.exec(e)) ? Ys(t[1], t[2] / 100, t[3] / 100, 1) : (t = qh.exec(e)) ? Ys(t[1], t[2] / 100, t[3] / 100, t[4]) : Fs.hasOwnProperty(e) ? qs(Fs[e]) : e === "transparent" ? new ze(NaN, NaN, NaN, 0) : null;
}
function qs(e) {
  return new ze(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function To(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new ze(e, t, n, o);
}
function Yh(e) {
  return e instanceof fo || (e = Lt(e)), e ? (e = e.rgb(), new ze(e.r, e.g, e.b, e.opacity)) : new ze();
}
function Sr(e, t, n, o) {
  return arguments.length === 1 ? Yh(e) : new ze(e, t, n, o ?? 1);
}
function ze(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
qr(ze, Sr, Xc(fo, {
  brighter(e) {
    return e = e == null ? Qo : Math.pow(Qo, e), new ze(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Jn : Math.pow(Jn, e), new ze(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new ze($t(this.r), $t(this.g), $t(this.b), ei(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: Xs,
  // Deprecated! Use color.formatHex.
  formatHex: Xs,
  formatHex8: Zh,
  formatRgb: Us,
  toString: Us
}));
function Xs() {
  return `#${Tt(this.r)}${Tt(this.g)}${Tt(this.b)}`;
}
function Zh() {
  return `#${Tt(this.r)}${Tt(this.g)}${Tt(this.b)}${Tt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function Us() {
  const e = ei(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${$t(this.r)}, ${$t(this.g)}, ${$t(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function ei(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function $t(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function Tt(e) {
  return e = $t(e), (e < 16 ? "0" : "") + e.toString(16);
}
function Ys(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Qe(e, t, n, o);
}
function Uc(e) {
  if (e instanceof Qe) return new Qe(e.h, e.s, e.l, e.opacity);
  if (e instanceof fo || (e = Lt(e)), !e) return new Qe();
  if (e instanceof Qe) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, i = Math.min(t, n, o), s = Math.max(t, n, o), a = NaN, c = s - i, u = (s + i) / 2;
  return c ? (t === s ? a = (n - o) / c + (n < o) * 6 : n === s ? a = (o - t) / c + 2 : a = (t - n) / c + 4, c /= u < 0.5 ? s + i : 2 - s - i, a *= 60) : c = u > 0 && u < 1 ? 0 : a, new Qe(a, c, u, e.opacity);
}
function Gh(e, t, n, o) {
  return arguments.length === 1 ? Uc(e) : new Qe(e, t, n, o ?? 1);
}
function Qe(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
qr(Qe, Gh, Xc(fo, {
  brighter(e) {
    return e = e == null ? Qo : Math.pow(Qo, e), new Qe(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Jn : Math.pow(Jn, e), new Qe(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, i = 2 * n - o;
    return new ze(
      rr(e >= 240 ? e - 240 : e + 120, i, o),
      rr(e, i, o),
      rr(e < 120 ? e + 240 : e - 120, i, o),
      this.opacity
    );
  },
  clamp() {
    return new Qe(Zs(this.h), $o(this.s), $o(this.l), ei(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = ei(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${Zs(this.h)}, ${$o(this.s) * 100}%, ${$o(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function Zs(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function $o(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function rr(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const Xr = (e) => () => e;
function Jh(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function Qh(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function ep(e) {
  return (e = +e) == 1 ? Yc : function(t, n) {
    return n - t ? Qh(t, n, e) : Xr(isNaN(t) ? n : t);
  };
}
function Yc(e, t) {
  var n = t - e;
  return n ? Jh(e, n) : Xr(isNaN(e) ? t : e);
}
const ti = (function e(t) {
  var n = ep(t);
  function o(i, s) {
    var a = n((i = Sr(i)).r, (s = Sr(s)).r), c = n(i.g, s.g), u = n(i.b, s.b), l = Yc(i.opacity, s.opacity);
    return function(d) {
      return i.r = a(d), i.g = c(d), i.b = u(d), i.opacity = l(d), i + "";
    };
  }
  return o.gamma = e, o;
})(1);
function tp(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), i;
  return function(s) {
    for (i = 0; i < n; ++i) o[i] = e[i] * (1 - s) + t[i] * s;
    return o;
  };
}
function np(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function op(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, i = new Array(o), s = new Array(n), a;
  for (a = 0; a < o; ++a) i[a] = Xn(e[a], t[a]);
  for (; a < n; ++a) s[a] = t[a];
  return function(c) {
    for (a = 0; a < o; ++a) s[a] = i[a](c);
    return s;
  };
}
function ip(e, t) {
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
function rp(e, t) {
  var n = {}, o = {}, i;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (i in t)
    i in e ? n[i] = Xn(e[i], t[i]) : o[i] = t[i];
  return function(s) {
    for (i in n) o[i] = n[i](s);
    return o;
  };
}
var kr = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, sr = new RegExp(kr.source, "g");
function sp(e) {
  return function() {
    return e;
  };
}
function ap(e) {
  return function(t) {
    return e(t) + "";
  };
}
function Zc(e, t) {
  var n = kr.lastIndex = sr.lastIndex = 0, o, i, s, a = -1, c = [], u = [];
  for (e = e + "", t = t + ""; (o = kr.exec(e)) && (i = sr.exec(t)); )
    (s = i.index) > n && (s = t.slice(n, s), c[a] ? c[a] += s : c[++a] = s), (o = o[0]) === (i = i[0]) ? c[a] ? c[a] += i : c[++a] = i : (c[++a] = null, u.push({ i: a, x: st(o, i) })), n = sr.lastIndex;
  return n < t.length && (s = t.slice(n), c[a] ? c[a] += s : c[++a] = s), c.length < 2 ? u[0] ? ap(u[0].x) : sp(t) : (t = u.length, function(l) {
    for (var d = 0, f; d < t; ++d) c[(f = u[d]).i] = f.x(l);
    return c.join("");
  });
}
function Xn(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? Xr(t) : (n === "number" ? st : n === "string" ? (o = Lt(t)) ? (t = o, ti) : Zc : t instanceof Lt ? ti : t instanceof Date ? ip : np(t) ? tp : Array.isArray(t) ? op : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? rp : st)(e, t);
}
var Gs = 180 / Math.PI, Er = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function Gc(e, t, n, o, i, s) {
  var a, c, u;
  return (a = Math.sqrt(e * e + t * t)) && (e /= a, t /= a), (u = e * n + t * o) && (n -= e * u, o -= t * u), (c = Math.sqrt(n * n + o * o)) && (n /= c, o /= c, u /= c), e * o < t * n && (e = -e, t = -t, u = -u, a = -a), {
    translateX: i,
    translateY: s,
    rotate: Math.atan2(t, e) * Gs,
    skewX: Math.atan(u) * Gs,
    scaleX: a,
    scaleY: c
  };
}
var Po;
function cp(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Er : Gc(t.a, t.b, t.c, t.d, t.e, t.f);
}
function lp(e) {
  return e == null || (Po || (Po = document.createElementNS("http://www.w3.org/2000/svg", "g")), Po.setAttribute("transform", e), !(e = Po.transform.baseVal.consolidate())) ? Er : (e = e.matrix, Gc(e.a, e.b, e.c, e.d, e.e, e.f));
}
function Jc(e, t, n, o) {
  function i(l) {
    return l.length ? l.pop() + " " : "";
  }
  function s(l, d, f, h, p, g) {
    if (l !== f || d !== h) {
      var v = p.push("translate(", null, t, null, n);
      g.push({ i: v - 4, x: st(l, f) }, { i: v - 2, x: st(d, h) });
    } else (f || h) && p.push("translate(" + f + t + h + n);
  }
  function a(l, d, f, h) {
    l !== d ? (l - d > 180 ? d += 360 : d - l > 180 && (l += 360), h.push({ i: f.push(i(f) + "rotate(", null, o) - 2, x: st(l, d) })) : d && f.push(i(f) + "rotate(" + d + o);
  }
  function c(l, d, f, h) {
    l !== d ? h.push({ i: f.push(i(f) + "skewX(", null, o) - 2, x: st(l, d) }) : d && f.push(i(f) + "skewX(" + d + o);
  }
  function u(l, d, f, h, p, g) {
    if (l !== f || d !== h) {
      var v = p.push(i(p) + "scale(", null, ",", null, ")");
      g.push({ i: v - 4, x: st(l, f) }, { i: v - 2, x: st(d, h) });
    } else (f !== 1 || h !== 1) && p.push(i(p) + "scale(" + f + "," + h + ")");
  }
  return function(l, d) {
    var f = [], h = [];
    return l = e(l), d = e(d), s(l.translateX, l.translateY, d.translateX, d.translateY, f, h), a(l.rotate, d.rotate, f, h), c(l.skewX, d.skewX, f, h), u(l.scaleX, l.scaleY, d.scaleX, d.scaleY, f, h), l = d = null, function(p) {
      for (var g = -1, v = h.length, w; ++g < v; ) f[(w = h[g]).i] = w.x(p);
      return f.join("");
    };
  };
}
var up = Jc(cp, "px, ", "px)", "deg)"), dp = Jc(lp, ", ", ")", ")"), fp = 1e-12;
function Js(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function hp(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function pp(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const Wo = (function e(t, n, o) {
  function i(s, a) {
    var c = s[0], u = s[1], l = s[2], d = a[0], f = a[1], h = a[2], p = d - c, g = f - u, v = p * p + g * g, w, m;
    if (v < fp)
      m = Math.log(h / l) / t, w = function(j) {
        return [
          c + j * p,
          u + j * g,
          l * Math.exp(t * j * m)
        ];
      };
    else {
      var k = Math.sqrt(v), y = (h * h - l * l + o * v) / (2 * l * n * k), b = (h * h - l * l - o * v) / (2 * h * n * k), S = Math.log(Math.sqrt(y * y + 1) - y), E = Math.log(Math.sqrt(b * b + 1) - b);
      m = (E - S) / t, w = function(j) {
        var _ = j * m, M = Js(S), F = l / (n * k) * (M * pp(t * _ + S) - hp(S));
        return [
          c + F * p,
          u + F * g,
          l * M / Js(t * _ + S)
        ];
      };
    }
    return w.duration = m * 1e3 * t / Math.SQRT2, w;
  }
  return i.rho = function(s) {
    var a = Math.max(1e-3, +s), c = a * a, u = c * c;
    return e(a, c, u);
  }, i;
})(Math.SQRT2, 2, 4);
var cn = 0, Wn = 0, zn = 0, Qc = 1e3, ni, Kn, oi = 0, Vt = 0, ji = 0, eo = typeof performance == "object" && performance.now ? performance : Date, el = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function Ur() {
  return Vt || (el(gp), Vt = eo.now() + ji);
}
function gp() {
  Vt = 0;
}
function ii() {
  this._call = this._time = this._next = null;
}
ii.prototype = tl.prototype = {
  constructor: ii,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? Ur() : +n) + (t == null ? 0 : +t), !this._next && Kn !== this && (Kn ? Kn._next = this : ni = this, Kn = this), this._call = e, this._time = n, Cr();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Cr());
  }
};
function tl(e, t, n) {
  var o = new ii();
  return o.restart(e, t, n), o;
}
function yp() {
  Ur(), ++cn;
  for (var e = ni, t; e; )
    (t = Vt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --cn;
}
function Qs() {
  Vt = (oi = eo.now()) + ji, cn = Wn = 0;
  try {
    yp();
  } finally {
    cn = 0, xp(), Vt = 0;
  }
}
function mp() {
  var e = eo.now(), t = e - oi;
  t > Qc && (ji -= t, oi = e);
}
function xp() {
  for (var e, t = ni, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : ni = n);
  Kn = e, Cr(o);
}
function Cr(e) {
  if (!cn) {
    Wn && (Wn = clearTimeout(Wn));
    var t = e - Vt;
    t > 24 ? (e < 1 / 0 && (Wn = setTimeout(Qs, e - eo.now() - ji)), zn && (zn = clearInterval(zn))) : (zn || (oi = eo.now(), zn = setInterval(mp, Qc)), cn = 1, el(Qs));
  }
}
function ea(e, t, n) {
  var o = new ii();
  return t = t == null ? 0 : +t, o.restart((i) => {
    o.stop(), e(i + t);
  }, t, n), o;
}
var wp = bi("start", "end", "cancel", "interrupt"), vp = [], nl = 0, ta = 1, Ir = 2, Ko = 3, na = 4, Ar = 5, qo = 6;
function Si(e, t, n, o, i, s) {
  var a = e.__transition;
  if (!a) e.__transition = {};
  else if (n in a) return;
  bp(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: i,
    // For context during callback.
    on: wp,
    tween: vp,
    time: s.time,
    delay: s.delay,
    duration: s.duration,
    ease: s.ease,
    timer: null,
    state: nl
  });
}
function Yr(e, t) {
  var n = rt(e, t);
  if (n.state > nl) throw new Error("too late; already scheduled");
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
function bp(e, t, n) {
  var o = e.__transition, i;
  o[t] = n, n.timer = tl(s, 0, n.time);
  function s(l) {
    n.state = ta, n.timer.restart(a, n.delay, n.time), n.delay <= l && a(l - n.delay);
  }
  function a(l) {
    var d, f, h, p;
    if (n.state !== ta) return u();
    for (d in o)
      if (p = o[d], p.name === n.name) {
        if (p.state === Ko) return ea(a);
        p.state === na ? (p.state = qo, p.timer.stop(), p.on.call("interrupt", e, e.__data__, p.index, p.group), delete o[d]) : +d < t && (p.state = qo, p.timer.stop(), p.on.call("cancel", e, e.__data__, p.index, p.group), delete o[d]);
      }
    if (ea(function() {
      n.state === Ko && (n.state = na, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = Ir, n.on.call("start", e, e.__data__, n.index, n.group), n.state === Ir) {
      for (n.state = Ko, i = new Array(h = n.tween.length), d = 0, f = -1; d < h; ++d)
        (p = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (i[++f] = p);
      i.length = f + 1;
    }
  }
  function c(l) {
    for (var d = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(u), n.state = Ar, 1), f = -1, h = i.length; ++f < h; )
      i[f].call(e, d);
    n.state === Ar && (n.on.call("end", e, e.__data__, n.index, n.group), u());
  }
  function u() {
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
      i = o.state > Ir && o.state < Ar, o.state = qo, o.timer.stop(), o.on.call(i ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[a];
    }
    s && delete e.__transition;
  }
}
function Np(e) {
  return this.each(function() {
    Xo(this, e);
  });
}
function jp(e, t) {
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
function Sp(e, t, n) {
  var o, i;
  if (typeof n != "function") throw new Error();
  return function() {
    var s = lt(this, e), a = s.tween;
    if (a !== o) {
      i = (o = a).slice();
      for (var c = { name: t, value: n }, u = 0, l = i.length; u < l; ++u)
        if (i[u].name === t) {
          i[u] = c;
          break;
        }
      u === l && i.push(c);
    }
    s.tween = i;
  };
}
function kp(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = rt(this.node(), n).tween, i = 0, s = o.length, a; i < s; ++i)
      if ((a = o[i]).name === e)
        return a.value;
    return null;
  }
  return this.each((t == null ? jp : Sp)(n, e, t));
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
function ol(e, t) {
  var n;
  return (typeof t == "number" ? st : t instanceof Lt ? ti : (n = Lt(t)) ? (t = n, ti) : Zc)(e, t);
}
function Ep(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Cp(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Ip(e, t, n) {
  var o, i = n + "", s;
  return function() {
    var a = this.getAttribute(e);
    return a === i ? null : a === o ? s : s = t(o = a, n);
  };
}
function Ap(e, t, n) {
  var o, i = n + "", s;
  return function() {
    var a = this.getAttributeNS(e.space, e.local);
    return a === i ? null : a === o ? s : s = t(o = a, n);
  };
}
function _p(e, t, n) {
  var o, i, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttribute(e) : (a = this.getAttribute(e), u = c + "", a === u ? null : a === o && u === i ? s : (i = u, s = t(o = a, c)));
  };
}
function Dp(e, t, n) {
  var o, i, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (a = this.getAttributeNS(e.space, e.local), u = c + "", a === u ? null : a === o && u === i ? s : (i = u, s = t(o = a, c)));
  };
}
function Mp(e, t) {
  var n = Ni(e), o = n === "transform" ? dp : ol;
  return this.attrTween(e, typeof t == "function" ? (n.local ? Dp : _p)(n, o, Zr(this, "attr." + e, t)) : t == null ? (n.local ? Cp : Ep)(n) : (n.local ? Ap : Ip)(n, o, t));
}
function Tp(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function $p(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function Pp(e, t) {
  var n, o;
  function i() {
    var s = t.apply(this, arguments);
    return s !== o && (n = (o = s) && $p(e, s)), n;
  }
  return i._value = t, i;
}
function zp(e, t) {
  var n, o;
  function i() {
    var s = t.apply(this, arguments);
    return s !== o && (n = (o = s) && Tp(e, s)), n;
  }
  return i._value = t, i;
}
function Rp(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = Ni(e);
  return this.tween(n, (o.local ? Pp : zp)(o, t));
}
function Lp(e, t) {
  return function() {
    Yr(this, e).delay = +t.apply(this, arguments);
  };
}
function Vp(e, t) {
  return t = +t, function() {
    Yr(this, e).delay = t;
  };
}
function Op(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Lp : Vp)(t, e)) : rt(this.node(), t).delay;
}
function Hp(e, t) {
  return function() {
    lt(this, e).duration = +t.apply(this, arguments);
  };
}
function Bp(e, t) {
  return t = +t, function() {
    lt(this, e).duration = t;
  };
}
function Fp(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Hp : Bp)(t, e)) : rt(this.node(), t).duration;
}
function Wp(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    lt(this, e).ease = t;
  };
}
function Kp(e) {
  var t = this._id;
  return arguments.length ? this.each(Wp(t, e)) : rt(this.node(), t).ease;
}
function qp(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    lt(this, e).ease = n;
  };
}
function Xp(e) {
  if (typeof e != "function") throw new Error();
  return this.each(qp(this._id, e));
}
function Up(e) {
  typeof e != "function" && (e = $c(e));
  for (var t = this._groups, n = t.length, o = new Array(n), i = 0; i < n; ++i)
    for (var s = t[i], a = s.length, c = o[i] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new gt(o, this._parents, this._name, this._id);
}
function Yp(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, i = n.length, s = Math.min(o, i), a = new Array(o), c = 0; c < s; ++c)
    for (var u = t[c], l = n[c], d = u.length, f = a[c] = new Array(d), h, p = 0; p < d; ++p)
      (h = u[p] || l[p]) && (f[p] = h);
  for (; c < o; ++c)
    a[c] = t[c];
  return new gt(a, this._parents, this._name, this._id);
}
function Zp(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function Gp(e, t, n) {
  var o, i, s = Zp(t) ? Yr : lt;
  return function() {
    var a = s(this, e), c = a.on;
    c !== o && (i = (o = c).copy()).on(t, n), a.on = i;
  };
}
function Jp(e, t) {
  var n = this._id;
  return arguments.length < 2 ? rt(this.node(), n).on.on(e) : this.each(Gp(n, e, t));
}
function Qp(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function eg() {
  return this.on("end.remove", Qp(this._id));
}
function tg(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Wr(e));
  for (var o = this._groups, i = o.length, s = new Array(i), a = 0; a < i; ++a)
    for (var c = o[a], u = c.length, l = s[a] = new Array(u), d, f, h = 0; h < u; ++h)
      (d = c[h]) && (f = e.call(d, d.__data__, h, c)) && ("__data__" in d && (f.__data__ = d.__data__), l[h] = f, Si(l[h], t, n, h, l, rt(d, n)));
  return new gt(s, this._parents, t, n);
}
function ng(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Tc(e));
  for (var o = this._groups, i = o.length, s = [], a = [], c = 0; c < i; ++c)
    for (var u = o[c], l = u.length, d, f = 0; f < l; ++f)
      if (d = u[f]) {
        for (var h = e.call(d, d.__data__, f, u), p, g = rt(d, n), v = 0, w = h.length; v < w; ++v)
          (p = h[v]) && Si(p, t, n, v, h, g);
        s.push(h), a.push(d);
      }
  return new gt(s, a, t, n);
}
var og = uo.prototype.constructor;
function ig() {
  return new og(this._groups, this._parents);
}
function rg(e, t) {
  var n, o, i;
  return function() {
    var s = an(this, e), a = (this.style.removeProperty(e), an(this, e));
    return s === a ? null : s === n && a === o ? i : i = t(n = s, o = a);
  };
}
function il(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function sg(e, t, n) {
  var o, i = n + "", s;
  return function() {
    var a = an(this, e);
    return a === i ? null : a === o ? s : s = t(o = a, n);
  };
}
function ag(e, t, n) {
  var o, i, s;
  return function() {
    var a = an(this, e), c = n(this), u = c + "";
    return c == null && (u = c = (this.style.removeProperty(e), an(this, e))), a === u ? null : a === o && u === i ? s : (i = u, s = t(o = a, c));
  };
}
function cg(e, t) {
  var n, o, i, s = "style." + t, a = "end." + s, c;
  return function() {
    var u = lt(this, e), l = u.on, d = u.value[s] == null ? c || (c = il(t)) : void 0;
    (l !== n || i !== d) && (o = (n = l).copy()).on(a, i = d), u.on = o;
  };
}
function lg(e, t, n) {
  var o = (e += "") == "transform" ? up : ol;
  return t == null ? this.styleTween(e, rg(e, o)).on("end.style." + e, il(e)) : typeof t == "function" ? this.styleTween(e, ag(e, o, Zr(this, "style." + e, t))).each(cg(this._id, e)) : this.styleTween(e, sg(e, o, t), n).on("end.style." + e, null);
}
function ug(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function dg(e, t, n) {
  var o, i;
  function s() {
    var a = t.apply(this, arguments);
    return a !== i && (o = (i = a) && ug(e, a, n)), o;
  }
  return s._value = t, s;
}
function fg(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, dg(e, t, n ?? ""));
}
function hg(e) {
  return function() {
    this.textContent = e;
  };
}
function pg(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function gg(e) {
  return this.tween("text", typeof e == "function" ? pg(Zr(this, "text", e)) : hg(e == null ? "" : e + ""));
}
function yg(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function mg(e) {
  var t, n;
  function o() {
    var i = e.apply(this, arguments);
    return i !== n && (t = (n = i) && yg(i)), t;
  }
  return o._value = e, o;
}
function xg(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, mg(e));
}
function wg() {
  for (var e = this._name, t = this._id, n = rl(), o = this._groups, i = o.length, s = 0; s < i; ++s)
    for (var a = o[s], c = a.length, u, l = 0; l < c; ++l)
      if (u = a[l]) {
        var d = rt(u, t);
        Si(u, e, n, l, a, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new gt(o, this._parents, e, n);
}
function vg() {
  var e, t, n = this, o = n._id, i = n.size();
  return new Promise(function(s, a) {
    var c = { value: a }, u = { value: function() {
      --i === 0 && s();
    } };
    n.each(function() {
      var l = lt(this, o), d = l.on;
      d !== e && (t = (e = d).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(u)), l.on = t;
    }), i === 0 && s();
  });
}
var bg = 0;
function gt(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function rl() {
  return ++bg;
}
var ht = uo.prototype;
gt.prototype = {
  constructor: gt,
  select: tg,
  selectAll: ng,
  selectChild: ht.selectChild,
  selectChildren: ht.selectChildren,
  filter: Up,
  merge: Yp,
  selection: ig,
  transition: wg,
  call: ht.call,
  nodes: ht.nodes,
  node: ht.node,
  size: ht.size,
  empty: ht.empty,
  each: ht.each,
  on: Jp,
  attr: Mp,
  attrTween: Rp,
  style: lg,
  styleTween: fg,
  text: gg,
  textTween: xg,
  remove: eg,
  tween: kp,
  delay: Op,
  duration: Fp,
  ease: Kp,
  easeVarying: Xp,
  end: vg,
  [Symbol.iterator]: ht[Symbol.iterator]
};
function Ng(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var jg = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: Ng
};
function Sg(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function kg(e) {
  var t, n;
  e instanceof gt ? (t = e._id, e = e._name) : (t = rl(), (n = jg).time = Ur(), e = e == null ? null : e + "");
  for (var o = this._groups, i = o.length, s = 0; s < i; ++s)
    for (var a = o[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && Si(u, e, t, l, a, n || Sg(u, t));
  return new gt(o, this._parents, e, t);
}
uo.prototype.interrupt = Np;
uo.prototype.transition = kg;
const zo = (e) => () => e;
function Eg(e, {
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
var ki = new pt(1, 0, 0);
sl.prototype = pt.prototype;
function sl(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return ki;
  return e.__zoom;
}
function ar(e) {
  e.stopImmediatePropagation();
}
function Rn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Cg(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function Ig() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function oa() {
  return this.__zoom || ki;
}
function Ag(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function _g() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Dg(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], i = e.invertX(t[1][0]) - n[1][0], s = e.invertY(t[0][1]) - n[0][1], a = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    i > o ? (o + i) / 2 : Math.min(0, o) || Math.max(0, i),
    a > s ? (s + a) / 2 : Math.min(0, s) || Math.max(0, a)
  );
}
function al() {
  var e = Cg, t = Ig, n = Dg, o = Ag, i = _g, s = [0, 1 / 0], a = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, u = Wo, l = bi("start", "zoom", "end"), d, f, h, p = 500, g = 150, v = 0, w = 10;
  function m(N) {
    N.property("__zoom", oa).on("wheel.zoom", _, { passive: !1 }).on("mousedown.zoom", M).on("dblclick.zoom", F).filter(i).on("touchstart.zoom", D).on("touchmove.zoom", T).on("touchend.zoom touchcancel.zoom", L).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  m.transform = function(N, A, I, $) {
    var z = N.selection ? N.selection() : N;
    z.property("__zoom", oa), N !== z ? S(N, A, I, $) : z.interrupt().each(function() {
      E(this, arguments).event($).start().zoom(null, typeof A == "function" ? A.apply(this, arguments) : A).end();
    });
  }, m.scaleBy = function(N, A, I, $) {
    m.scaleTo(N, function() {
      var z = this.__zoom.k, P = typeof A == "function" ? A.apply(this, arguments) : A;
      return z * P;
    }, I, $);
  }, m.scaleTo = function(N, A, I, $) {
    m.transform(N, function() {
      var z = t.apply(this, arguments), P = this.__zoom, B = I == null ? b(z) : typeof I == "function" ? I.apply(this, arguments) : I, K = P.invert(B), O = typeof A == "function" ? A.apply(this, arguments) : A;
      return n(y(k(P, O), B, K), z, a);
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
      var P = t.apply(this, arguments), B = this.__zoom, K = $ == null ? b(P) : typeof $ == "function" ? $.apply(this, arguments) : $;
      return n(ki.translate(K[0], K[1]).scale(B.k).translate(
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
      var z = this, P = arguments, B = E(z, P).event($), K = t.apply(z, P), O = I == null ? b(K) : typeof I == "function" ? I.apply(z, P) : I, Z = Math.max(K[1][0] - K[0][0], K[1][1] - K[0][1]), X = z.__zoom, oe = typeof A == "function" ? A.apply(z, P) : A, se = u(X.invert(O).concat(Z / X.k), oe.invert(O).concat(Z / oe.k));
      return function(G) {
        if (G === 1) G = oe;
        else {
          var V = se(G), U = Z / V[2];
          G = new pt(U, O[0] - V[0] * U, O[1] - V[1] * U);
        }
        B.zoom(null, G);
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
      var A = Fe(this.that).datum();
      l.call(
        N,
        this.that,
        new Eg(N, {
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
    var I = E(this, A).event(N), $ = this.__zoom, z = Math.max(s[0], Math.min(s[1], $.k * Math.pow(2, o.apply(this, arguments)))), P = Je(N);
    if (I.wheel)
      (I.mouse[0][0] !== P[0] || I.mouse[0][1] !== P[1]) && (I.mouse[1] = $.invert(I.mouse[0] = P)), clearTimeout(I.wheel);
    else {
      if ($.k === z) return;
      I.mouse = [P, $.invert(P)], Xo(this), I.start();
    }
    Rn(N), I.wheel = setTimeout(B, g), I.zoom("mouse", n(y(k($, z), I.mouse[0], I.mouse[1]), I.extent, a));
    function B() {
      I.wheel = null, I.end();
    }
  }
  function M(N, ...A) {
    if (h || !e.apply(this, arguments)) return;
    var I = N.currentTarget, $ = E(this, A, !0).event(N), z = Fe(N.view).on("mousemove.zoom", O, !0).on("mouseup.zoom", Z, !0), P = Je(N, I), B = N.clientX, K = N.clientY;
    Wc(N.view), ar(N), $.mouse = [P, this.__zoom.invert(P)], Xo(this), $.start();
    function O(X) {
      if (Rn(X), !$.moved) {
        var oe = X.clientX - B, se = X.clientY - K;
        $.moved = oe * oe + se * se > v;
      }
      $.event(X).zoom("mouse", n(y($.that.__zoom, $.mouse[0] = Je(X, I), $.mouse[1]), $.extent, a));
    }
    function Z(X) {
      z.on("mousemove.zoom mouseup.zoom", null), Kc(X.view, $.moved), Rn(X), $.event(X).end();
    }
  }
  function F(N, ...A) {
    if (e.apply(this, arguments)) {
      var I = this.__zoom, $ = Je(N.changedTouches ? N.changedTouches[0] : N, this), z = I.invert($), P = I.k * (N.shiftKey ? 0.5 : 2), B = n(y(k(I, P), $, z), t.apply(this, A), a);
      Rn(N), c > 0 ? Fe(this).transition().duration(c).call(S, B, $, N) : Fe(this).call(m.transform, B, $, N);
    }
  }
  function D(N, ...A) {
    if (e.apply(this, arguments)) {
      var I = N.touches, $ = I.length, z = E(this, A, N.changedTouches.length === $).event(N), P, B, K, O;
      for (ar(N), B = 0; B < $; ++B)
        K = I[B], O = Je(K, this), O = [O, this.__zoom.invert(O), K.identifier], z.touch0 ? !z.touch1 && z.touch0[2] !== O[2] && (z.touch1 = O, z.taps = 0) : (z.touch0 = O, P = !0, z.taps = 1 + !!d);
      d && (d = clearTimeout(d)), P && (z.taps < 2 && (f = O[0], d = setTimeout(function() {
        d = null;
      }, p)), Xo(this), z.start());
    }
  }
  function T(N, ...A) {
    if (this.__zooming) {
      var I = E(this, A).event(N), $ = N.changedTouches, z = $.length, P, B, K, O;
      for (Rn(N), P = 0; P < z; ++P)
        B = $[P], K = Je(B, this), I.touch0 && I.touch0[2] === B.identifier ? I.touch0[0] = K : I.touch1 && I.touch1[2] === B.identifier && (I.touch1[0] = K);
      if (B = I.that.__zoom, I.touch1) {
        var Z = I.touch0[0], X = I.touch0[1], oe = I.touch1[0], se = I.touch1[1], G = (G = oe[0] - Z[0]) * G + (G = oe[1] - Z[1]) * G, V = (V = se[0] - X[0]) * V + (V = se[1] - X[1]) * V;
        B = k(B, Math.sqrt(G / V)), K = [(Z[0] + oe[0]) / 2, (Z[1] + oe[1]) / 2], O = [(X[0] + se[0]) / 2, (X[1] + se[1]) / 2];
      } else if (I.touch0) K = I.touch0[0], O = I.touch0[1];
      else return;
      I.zoom("touch", n(y(B, K, O), I.extent, a));
    }
  }
  function L(N, ...A) {
    if (this.__zooming) {
      var I = E(this, A).event(N), $ = N.changedTouches, z = $.length, P, B;
      for (ar(N), h && clearTimeout(h), h = setTimeout(function() {
        h = null;
      }, p), P = 0; P < z; ++P)
        B = $[P], I.touch0 && I.touch0[2] === B.identifier ? delete I.touch0 : I.touch1 && I.touch1[2] === B.identifier && delete I.touch1;
      if (I.touch1 && !I.touch0 && (I.touch0 = I.touch1, delete I.touch1), I.touch0) I.touch0[1] = this.__zoom.invert(I.touch0[0]);
      else if (I.end(), I.taps === 2 && (B = Je(B, this), Math.hypot(f[0] - B[0], f[1] - B[1]) < w)) {
        var K = Fe(this).on("dblclick.zoom");
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
    return arguments.length ? (u = N, m) : u;
  }, m.on = function() {
    var N = l.on.apply(l, arguments);
    return N === l ? m : N;
  }, m.clickDistance = function(N) {
    return arguments.length ? (v = (N = +N) * N, m) : Math.sqrt(v);
  }, m.tapDistance = function(N) {
    return arguments.length ? (w = +N, m) : w;
  }, m;
}
const Ke = {
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
}, to = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], cl = ["Enter", " ", "Escape"], ll = {
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
var Pt;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(Pt || (Pt = {}));
var no;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(no || (no = {}));
const ul = {
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
var ne;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(ne || (ne = {}));
const ia = {
  [ne.Left]: ne.Right,
  [ne.Right]: ne.Left,
  [ne.Top]: ne.Bottom,
  [ne.Bottom]: ne.Top
};
function dl(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const fl = (e) => "id" in e && "source" in e && "target" in e, Mg = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), Gr = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), ho = (e, t = [0, 0]) => {
  const { width: n, height: o } = yt(e), i = e.origin ?? t, s = n * i[0], a = o * i[1];
  return {
    x: e.position.x - s,
    y: e.position.y - a
  };
}, Tg = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, i) => {
    const s = typeof i == "string";
    let a = !t.nodeLookup && !s ? i : void 0;
    t.nodeLookup && (a = s ? t.nodeLookup.get(i) : Gr(i) ? i : t.nodeLookup.get(i.id));
    const c = a ? si(a, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return Ei(o, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return Ci(n);
}, po = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((i) => {
    (t.filter === void 0 || t.filter(i)) && (n = Ei(n, si(i)), o = !0);
  }), o ? Ci(n) : { x: 0, y: 0, width: 0, height: 0 };
}, Jr = (e, t, [n, o, i] = [0, 0, 1], s = !1, a = !1) => {
  const c = {
    ...mn(t, [n, o, i]),
    width: t.width / i,
    height: t.height / i
  }, u = [];
  for (const l of e.values()) {
    const { measured: d, selectable: f = !0, hidden: h = !1 } = l;
    if (a && !f || h)
      continue;
    const p = d.width ?? l.width ?? l.initialWidth ?? null, g = d.height ?? l.height ?? l.initialHeight ?? null, v = oo(c, dn(l)), w = (p ?? 0) * (g ?? 0), m = s && v > 0;
    (!l.internals.handleBounds || m || v >= w || l.dragging) && u.push(l);
  }
  return u;
}, $g = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function Pg(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((i) => i.id)) : null;
  return e.forEach((i) => {
    i.measured.width && i.measured.height && (t?.includeHiddenNodes || !i.hidden) && (!o || o.has(i.id)) && n.set(i.id, i);
  }), n;
}
async function zg({ nodes: e, width: t, height: n, panZoom: o, minZoom: i, maxZoom: s }, a) {
  if (e.size === 0)
    return !0;
  const c = Pg(e, a), u = po(c), l = es(u, t, n, a?.minZoom ?? i, a?.maxZoom ?? s, a?.padding ?? 0.1);
  return await o.setViewport(l, {
    duration: a?.duration,
    ease: a?.ease,
    interpolate: a?.interpolate
  }), !0;
}
function hl({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: i, onError: s }) {
  const a = n.get(e), c = a.parentId ? n.get(a.parentId) : void 0, { x: u, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, d = a.origin ?? o;
  let f = a.extent || i;
  if (a.extent === "parent" && !a.expandParent)
    if (!c)
      s?.("005", Ke.error005());
    else {
      const p = c.measured.width, g = c.measured.height;
      p && g && (f = [
        [u, l],
        [u + p, l + g]
      ]);
    }
  else c && Ht(a.extent) && (f = [
    [a.extent[0][0] + u, a.extent[0][1] + l],
    [a.extent[1][0] + u, a.extent[1][1] + l]
  ]);
  const h = Ht(f) ? Ot(t, f, a.measured) : t;
  return (a.measured.width === void 0 || a.measured.height === void 0) && s?.("015", Ke.error015()), {
    position: {
      x: h.x - u + (a.measured.width ?? 0) * d[0],
      y: h.y - l + (a.measured.height ?? 0) * d[1]
    },
    positionAbsolute: h
  };
}
async function Rg({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: i }) {
  const s = new Set(e.map((h) => h.id)), a = [];
  for (const h of n) {
    if (h.deletable === !1)
      continue;
    const p = s.has(h.id), g = !p && h.parentId && a.find((v) => v.id === h.parentId);
    (p || g) && a.push(h);
  }
  const c = new Set(t.map((h) => h.id)), u = o.filter((h) => h.deletable !== !1), d = $g(a, u);
  for (const h of u)
    c.has(h.id) && !d.find((g) => g.id === h.id) && d.push(h);
  if (!i)
    return {
      edges: d,
      nodes: a
    };
  const f = await i({
    nodes: a,
    edges: d
  });
  return typeof f == "boolean" ? f ? { edges: d, nodes: a } : { edges: [], nodes: [] } : f;
}
const un = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), Ot = (e = { x: 0, y: 0 }, t, n) => ({
  x: un(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: un(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function pl(e, t, n) {
  const { width: o, height: i } = yt(n), { x: s, y: a } = n.internals.positionAbsolute;
  return Ot(e, [
    [s, a],
    [s + o, a + i]
  ], t);
}
const ra = (e, t, n) => e < t ? un(Math.abs(e - t), 1, t) / t : e > n ? -un(Math.abs(e - n), 1, t) / t : 0, Qr = (e, t, n = 15, o = 40) => {
  const i = ra(e.x, o, t.width - o) * n, s = ra(e.y, o, t.height - o) * n;
  return [i, s];
}, Ei = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), _r = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), Ci = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), dn = (e, t = [0, 0]) => {
  const { x: n, y: o } = Gr(e) ? e.internals.positionAbsolute : ho(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, si = (e, t = [0, 0]) => {
  const { x: n, y: o } = Gr(e) ? e.internals.positionAbsolute : ho(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, gl = (e, t) => Ci(Ei(_r(e), _r(t))), oo = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, sa = (e) => et(e.width) && et(e.height) && et(e.x) && et(e.y), et = (e) => !isNaN(e) && isFinite(e), yl = (e, t) => (n, o) => {
}, go = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), mn = ({ x: e, y: t }, [n, o, i], s = !1, a = [1, 1]) => {
  const c = {
    x: (e - n) / i,
    y: (t - o) / i
  };
  return s ? go(c, a) : c;
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
function Lg(e, t, n) {
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
function Vg(e, t, n, o, i, s) {
  const { x: a, y: c } = fn(e, [t, n, o]), { x: u, y: l } = fn({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), d = i - u, f = s - l;
  return {
    left: Math.floor(a),
    top: Math.floor(c),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const es = (e, t, n, o, i, s) => {
  const a = Lg(s, t, n), c = (t - a.x) / e.width, u = (n - a.y) / e.height, l = Math.min(c, u), d = un(l, o, i), f = e.x + e.width / 2, h = e.y + e.height / 2, p = t / 2 - f * d, g = n / 2 - h * d, v = Vg(e, p, g, d, t, n), w = {
    left: Math.min(v.left - a.left, 0),
    top: Math.min(v.top - a.top, 0),
    right: Math.min(v.right - a.right, 0),
    bottom: Math.min(v.bottom - a.bottom, 0)
  };
  return {
    x: p - w.left + w.right,
    y: g - w.top + w.bottom,
    zoom: d
  };
}, io = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function Ht(e) {
  return e != null && e !== "parent";
}
function yt(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function ml(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function xl(e, t = { width: 0, height: 0 }, n, o, i) {
  const s = { ...e }, a = o.get(n);
  if (a) {
    const c = a.origin || i;
    s.x += a.internals.positionAbsolute.x - (t.width ?? 0) * c[0], s.y += a.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return s;
}
function aa(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function Og() {
  let e, t;
  return { promise: new Promise((o, i) => {
    e = o, t = i;
  }), resolve: e, reject: t };
}
function Hg(e) {
  return { ...ll, ...e || {} };
}
function Un(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: i }) {
  const { x: s, y: a } = tt(e), c = mn({ x: s - (i?.left ?? 0), y: a - (i?.top ?? 0) }, o), { x: u, y: l } = n ? go(c, t) : c;
  return {
    xSnapped: u,
    ySnapped: l,
    ...c
  };
}
const ts = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), wl = (e) => e?.getRootNode?.() || window?.document, Bg = ["INPUT", "SELECT", "TEXTAREA"];
function vl(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : Bg.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const bl = (e) => "clientX" in e, tt = (e, t) => {
  const n = bl(e), o = n ? e.clientX : e.touches?.[0].clientX, i = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: i - (t?.top ?? 0)
  };
}, ca = (e, t, n, o, i) => {
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
function Nl({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: i, sourceControlY: s, targetControlX: a, targetControlY: c }) {
  const u = e * 0.125 + i * 0.375 + a * 0.375 + n * 0.125, l = t * 0.125 + s * 0.375 + c * 0.375 + o * 0.125, d = Math.abs(u - e), f = Math.abs(l - t);
  return [u, l, d, f];
}
function Ro(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function la({ pos: e, x1: t, y1: n, x2: o, y2: i, c: s }) {
  switch (e) {
    case ne.Left:
      return [t - Ro(t - o, s), n];
    case ne.Right:
      return [t + Ro(o - t, s), n];
    case ne.Top:
      return [t, n - Ro(n - i, s)];
    case ne.Bottom:
      return [t, n + Ro(i - n, s)];
  }
}
function jl({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: i, targetPosition: s = ne.Top, curvature: a = 0.25 }) {
  const [c, u] = la({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: i,
    c: a
  }), [l, d] = la({
    pos: s,
    x1: o,
    y1: i,
    x2: e,
    y2: t,
    c: a
  }), [f, h, p, g] = Nl({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: i,
    sourceControlX: c,
    sourceControlY: u,
    targetControlX: l,
    targetControlY: d
  });
  return [
    `M${e},${t} C${c},${u} ${l},${d} ${o},${i}`,
    f,
    h,
    p,
    g
  ];
}
function Sl({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const i = Math.abs(n - e) / 2, s = n < e ? n + i : n - i, a = Math.abs(o - t) / 2, c = o < t ? o + a : o - a;
  return [s, c, i, a];
}
function Fg({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: i = !1, zIndexMode: s = "basic" }) {
  if (s === "manual")
    return o;
  const a = i && n ? o + 1e3 : o, c = Math.max(e.parentId || i && e.selected ? e.internals.z : 0, t.parentId || i && t.selected ? t.internals.z : 0);
  return a + c;
}
function Wg({ sourceNode: e, targetNode: t, width: n, height: o, transform: i }) {
  const s = Ei(si(e), si(t));
  s.x === s.x2 && (s.x2 += 1), s.y === s.y2 && (s.y2 += 1);
  const a = {
    x: -i[0] / i[2],
    y: -i[1] / i[2],
    width: n / i[2],
    height: o / i[2]
  };
  return oo(a, Ci(s)) > 0;
}
const kl = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, Kg = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), qg = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Ke.error006()), t;
  const o = n.getEdgeId || kl;
  let i;
  return fl(e) ? i = { ...e } : i = {
    ...e,
    id: o(e)
  }, Kg(i, t) ? t : (i.sourceHandle === null && delete i.sourceHandle, i.targetHandle === null && delete i.targetHandle, t.concat(i));
}, Xg = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: i, ...s } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", Ke.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return o.onError?.("007", Ke.error007(i)), n;
  const c = o.getEdgeId || kl, u = {
    ...s,
    id: o.shouldReplaceId ? c(t) : i,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== i).concat(u);
};
function El({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [i, s, a, c] = Sl({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, i, s, a, c];
}
const ua = {
  [ne.Left]: { x: -1, y: 0 },
  [ne.Right]: { x: 1, y: 0 },
  [ne.Top]: { x: 0, y: -1 },
  [ne.Bottom]: { x: 0, y: 1 }
}, Ug = ({ source: e, sourcePosition: t = ne.Bottom, target: n }) => t === ne.Left || t === ne.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, da = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Yg({ source: e, sourcePosition: t = ne.Bottom, target: n, targetPosition: o = ne.Top, center: i, offset: s, stepPosition: a }) {
  const c = ua[t], u = ua[o], l = { x: e.x + c.x * s, y: e.y + c.y * s }, d = { x: n.x + u.x * s, y: n.y + u.y * s }, f = Ug({
    source: l,
    sourcePosition: t,
    target: d
  }), h = f.x !== 0 ? "x" : "y", p = f[h];
  let g = [], v, w;
  const m = { x: 0, y: 0 }, k = { x: 0, y: 0 }, [, , y, b] = Sl({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[h] * u[h] === -1) {
    h === "x" ? (v = i.x ?? l.x + (d.x - l.x) * a, w = i.y ?? (l.y + d.y) / 2) : (v = i.x ?? (l.x + d.x) / 2, w = i.y ?? l.y + (d.y - l.y) * a);
    const _ = [
      { x: v, y: l.y },
      { x: v, y: d.y }
    ], M = [
      { x: l.x, y: w },
      { x: d.x, y: w }
    ];
    c[h] === p ? g = h === "x" ? _ : M : g = h === "x" ? M : _;
  } else {
    const _ = [{ x: l.x, y: d.y }], M = [{ x: d.x, y: l.y }];
    if (h === "x" ? g = c.x === p ? M : _ : g = c.y === p ? _ : M, t === o) {
      const N = Math.abs(e[h] - n[h]);
      if (N <= s) {
        const A = Math.min(s - 1, s - N);
        c[h] === p ? m[h] = (l[h] > e[h] ? -1 : 1) * A : k[h] = (d[h] > n[h] ? -1 : 1) * A;
      }
    }
    if (t !== o) {
      const N = h === "x" ? "y" : "x", A = c[h] === u[N], I = l[N] > d[N], $ = l[N] < d[N];
      (c[h] === 1 && (!A && I || A && $) || c[h] !== 1 && (!A && $ || A && I)) && (g = h === "x" ? _ : M);
    }
    const F = { x: l.x + m.x, y: l.y + m.y }, D = { x: d.x + k.x, y: d.y + k.y }, T = Math.max(Math.abs(F.x - g[0].x), Math.abs(D.x - g[0].x)), L = Math.max(Math.abs(F.y - g[0].y), Math.abs(D.y - g[0].y));
    T >= L ? (v = (F.x + D.x) / 2, w = g[0].y) : (v = g[0].x, w = (F.y + D.y) / 2);
  }
  const S = { x: l.x + m.x, y: l.y + m.y }, E = { x: d.x + k.x, y: d.y + k.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...S.x !== g[0].x || S.y !== g[0].y ? [S] : [],
    ...g,
    ...E.x !== g[g.length - 1].x || E.y !== g[g.length - 1].y ? [E] : [],
    n
  ], v, w, y, b];
}
function Zg(e, t, n, o) {
  const i = Math.min(da(e, t) / 2, da(t, n) / 2, o), { x: s, y: a } = t;
  if (e.x === s && s === n.x || e.y === a && a === n.y)
    return `L${s} ${a}`;
  if (e.y === a) {
    const l = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${s + i * l},${a}Q ${s},${a} ${s},${a + i * d}`;
  }
  const c = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${s},${a + i * u}Q ${s},${a} ${s + i * c},${a}`;
}
function ai({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: i, targetPosition: s = ne.Top, borderRadius: a = 5, centerX: c, centerY: u, offset: l = 20, stepPosition: d = 0.5 }) {
  const [f, h, p, g, v] = Yg({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: i },
    targetPosition: s,
    center: { x: c, y: u },
    offset: l,
    stepPosition: d
  });
  let w = `M${f[0].x} ${f[0].y}`;
  for (let m = 1; m < f.length - 1; m++)
    w += Zg(f[m - 1], f[m], f[m + 1], a);
  return w += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [w, h, p, g, v];
}
function fa(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function Gg(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!fa(t) || !fa(n))
    return null;
  const o = t.internals.handleBounds || ha(t.handles), i = n.internals.handleBounds || ha(n.handles), s = pa(o?.source ?? [], e.sourceHandle), a = pa(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === ln.Strict ? i?.target ?? [] : (i?.target ?? []).concat(i?.source ?? []),
    e.targetHandle
  );
  if (!s || !a)
    return e.onError?.("008", Ke.error008(s ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = s?.position || ne.Bottom, u = a?.position || ne.Top, l = Bt(t, s, c), d = Bt(n, a, u);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: d.x,
    targetY: d.y,
    sourcePosition: c,
    targetPosition: u
  };
}
function ha(e) {
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
function Bt(e, t, n = ne.Left, o = !1) {
  const i = (t?.x ?? 0) + e.internals.positionAbsolute.x, s = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: a, height: c } = t ?? yt(e);
  if (o)
    return { x: i + a / 2, y: s + c / 2 };
  switch (t?.position ?? n) {
    case ne.Top:
      return { x: i + a / 2, y: s };
    case ne.Right:
      return { x: i + a, y: s + c / 2 };
    case ne.Bottom:
      return { x: i + a / 2, y: s + c };
    case ne.Left:
      return { x: i, y: s + c / 2 };
  }
}
function pa(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Dr(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function Jg(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: i }) {
  const s = /* @__PURE__ */ new Set();
  return e.reduce((a, c) => ([c.markerStart || o, c.markerEnd || i].forEach((u) => {
    if (u && typeof u == "object") {
      const l = Dr(u, t);
      s.has(l) || (a.push({ id: l, color: u.color || n, ...u }), s.add(l));
    }
  }), a), []).sort((a, c) => a.id.localeCompare(c.id));
}
const Cl = 1e3, Qg = 10, ns = {
  nodeOrigin: [0, 0],
  nodeExtent: to,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, ey = {
  ...ns,
  checkEquality: !0
};
function os(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function ty(e, t, n) {
  const o = os(ns, n);
  for (const i of e.values())
    if (i.parentId)
      rs(i, e, t, o);
    else {
      const s = ho(i, o.nodeOrigin), a = Ht(i.extent) ? i.extent : o.nodeExtent, c = Ot(s, a, yt(i));
      i.internals.positionAbsolute = c;
    }
}
function ny(e, t) {
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
  const i = os(ey, o), s = { i: 0 }, a = new Map(t), c = i?.elevateNodesOnSelect && !is(i.zIndexMode) ? Cl : 0;
  let u = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = a.get(d.id);
    if (i.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const h = ho(d, i.nodeOrigin), p = Ht(d.extent) ? d.extent : i.nodeExtent, g = Ot(h, p, yt(d));
      f = {
        ...i.defaults,
        ...d,
        measured: {
          width: d.measured?.width,
          height: d.measured?.height
        },
        internals: {
          positionAbsolute: g,
          // if user re-initializes the node or removes `measured` for whatever reason, we reset the handleBounds so that the node gets re-measured
          handleBounds: ny(d, f),
          z: Il(d, c, i.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (u = !1), d.parentId && rs(f, t, n, o, s), l ||= d.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: l };
}
function oy(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function rs(e, t, n, o, i) {
  const { elevateNodesOnSelect: s, nodeOrigin: a, nodeExtent: c, zIndexMode: u } = os(ns, o), l = e.parentId, d = t.get(l);
  if (!d) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  oy(e, n), i && !d.parentId && d.internals.rootParentIndex === void 0 && u === "auto" && (d.internals.rootParentIndex = ++i.i, d.internals.z = d.internals.z + i.i * Qg), i && d.internals.rootParentIndex !== void 0 && (i.i = d.internals.rootParentIndex);
  const f = s && !is(u) ? Cl : 0, { x: h, y: p, z: g } = iy(e, d, a, c, f, u), { positionAbsolute: v } = e.internals, w = h !== v.x || p !== v.y;
  (w || g !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: w ? { x: h, y: p } : v,
      z: g
    }
  });
}
function Il(e, t, n) {
  const o = et(e.zIndex) ? e.zIndex : 0;
  return is(n) ? o : o + (e.selected ? t : 0);
}
function iy(e, t, n, o, i, s) {
  const { x: a, y: c } = t.internals.positionAbsolute, u = yt(e), l = ho(e, n), d = Ht(e.extent) ? Ot(l, e.extent, u) : l;
  let f = Ot({ x: a + d.x, y: c + d.y }, o, u);
  e.extent === "parent" && (f = pl(f, u, t));
  const h = Il(e, i, s), p = t.internals.z ?? 0;
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
    const u = s.get(a.parentId)?.expandedRect ?? dn(c), l = gl(u, a.rect);
    s.set(a.parentId, { expandedRect: l, parent: c });
  }
  return s.size > 0 && s.forEach(({ expandedRect: a, parent: c }, u) => {
    const l = c.internals.positionAbsolute, d = yt(c), f = c.origin ?? o, h = a.x < l.x ? Math.round(Math.abs(l.x - a.x)) : 0, p = a.y < l.y ? Math.round(Math.abs(l.y - a.y)) : 0, g = Math.max(d.width, Math.round(a.width)), v = Math.max(d.height, Math.round(a.height)), w = (g - d.width) * f[0], m = (v - d.height) * f[1];
    (h > 0 || p > 0 || w || m) && (i.push({
      id: u,
      type: "position",
      position: {
        x: c.position.x - h + w,
        y: c.position.y - p + m
      }
    }), n.get(u)?.forEach((k) => {
      e.some((y) => y.id === k.id) || i.push({
        id: k.id,
        type: "position",
        position: {
          x: k.position.x + h,
          y: k.position.y + p
        }
      });
    })), (d.width < a.width || d.height < a.height || h || p) && i.push({
      id: u,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: g + (h ? f[0] * h - w : 0),
        height: v + (p ? f[1] * p - m : 0)
      }
    });
  }), i;
}
function ry(e, t, n, o, i, s, a) {
  const c = o?.querySelector(".xyflow__viewport");
  let u = !1;
  if (!c)
    return { changes: [], updatedInternals: u };
  const l = [], d = window.getComputedStyle(c), { m22: f } = new window.DOMMatrixReadOnly(d.transform), h = [];
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
    const v = ts(p.nodeElement), w = g.measured.width !== v.width || g.measured.height !== v.height;
    if (!!(v.width && v.height && (w || !g.internals.handleBounds || p.force))) {
      const k = p.nodeElement.getBoundingClientRect(), y = Ht(g.extent) ? g.extent : s;
      let { positionAbsolute: b } = g.internals;
      g.parentId && g.extent === "parent" ? b = pl(b, v, t.get(g.parentId)) : y && (b = Ot(b, y, v));
      const S = {
        ...g,
        measured: v,
        internals: {
          ...g.internals,
          positionAbsolute: b,
          handleBounds: {
            source: ca("source", p.nodeElement, k, f, g.id),
            target: ca("target", p.nodeElement, k, f, g.id)
          }
        }
      };
      t.set(g.id, S), g.parentId && rs(S, t, n, { nodeOrigin: i, zIndexMode: a }), u = !0, w && (l.push({
        id: g.id,
        type: "dimensions",
        dimensions: v
      }), g.expandParent && g.parentId && h.push({
        id: g.id,
        parentId: g.parentId,
        rect: dn(S, i)
      }));
    }
  }
  if (h.length > 0) {
    const p = ss(h, t, n, i);
    l.push(...p);
  }
  return { changes: l, updatedInternals: u };
}
async function sy({ delta: e, panZoom: t, transform: n, translateExtent: o, width: i, height: s }) {
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
function ga(e, t, n, o, i, s) {
  let a = i;
  const c = o.get(a) || /* @__PURE__ */ new Map();
  o.set(a, c.set(n, t)), a = `${i}-${e}`;
  const u = o.get(a) || /* @__PURE__ */ new Map();
  if (o.set(a, u.set(n, t)), s) {
    a = `${i}-${e}-${s}`;
    const l = o.get(a) || /* @__PURE__ */ new Map();
    o.set(a, l.set(n, t));
  }
}
function Al(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: i, target: s, sourceHandle: a = null, targetHandle: c = null } = o, u = { edgeId: o.id, source: i, target: s, sourceHandle: a, targetHandle: c }, l = `${i}-${a}--${s}-${c}`, d = `${s}-${c}--${i}-${a}`;
    ga("source", u, d, e, i, a), ga("target", u, l, e, s, c), t.set(o.id, o);
  }
}
function _l(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : _l(n, t) : !1;
}
function ya(e, t, n) {
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
function ay(e, t, n, o) {
  const i = /* @__PURE__ */ new Map();
  for (const [s, a] of e)
    if ((a.selected || a.id === o) && (!a.parentId || !_l(a, e)) && (a.draggable || t && typeof a.draggable > "u")) {
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
function cr({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
  const i = [];
  for (const [a, c] of t) {
    const u = n.get(a)?.internals.userNode;
    u && i.push({
      ...u,
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
function cy({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const i = e.values().next().value;
  if (!i)
    return null;
  const s = {
    x: n - i.distance.x,
    y: o - i.distance.y
  }, a = go(s, t);
  return {
    x: a.x - s.x,
    y: a.y - s.y
  };
}
function ly({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: i }) {
  let s = { x: null, y: null }, a = 0, c = /* @__PURE__ */ new Map(), u = !1, l = { x: 0, y: 0 }, d = null, f = !1, h = null, p = !1, g = !1, v = null;
  function w({ noDragClassName: k, handleSelector: y, domNode: b, isSelectable: S, nodeId: E, nodeClickDistance: j = 0 }) {
    h = Fe(b);
    function _({ x: T, y: L }) {
      const { nodeLookup: N, nodeExtent: A, snapGrid: I, snapToGrid: $, nodeOrigin: z, onNodeDrag: P, onSelectionDrag: B, onError: K, updateNodePositions: O } = t();
      s = { x: T, y: L };
      let Z = !1;
      const X = c.size > 1, oe = X && A ? _r(po(c)) : null, se = X && $ ? cy({
        dragItems: c,
        snapGrid: I,
        x: T,
        y: L
      }) : null;
      for (const [G, V] of c) {
        if (!N.has(G))
          continue;
        let U = { x: T - V.distance.x, y: L - V.distance.y };
        $ && (U = se ? {
          x: Math.round(U.x + se.x),
          y: Math.round(U.y + se.y)
        } : go(U, I));
        let ae = null;
        if (X && A && !V.extent && oe) {
          const { positionAbsolute: ee } = V.internals, de = ee.x - oe.x + A[0][0], H = ee.x + V.measured.width - oe.x2 + A[1][0], Q = ee.y - oe.y + A[0][1], ge = ee.y + V.measured.height - oe.y2 + A[1][1];
          ae = [
            [de, Q],
            [H, ge]
          ];
        }
        const { position: ce, positionAbsolute: J } = hl({
          nodeId: G,
          nextPosition: U,
          nodeLookup: N,
          nodeExtent: ae || A,
          nodeOrigin: z,
          onError: K
        });
        Z = Z || V.position.x !== ce.x || V.position.y !== ce.y, V.position = ce, V.internals.positionAbsolute = J;
      }
      if (g = g || Z, !!Z && (O(c, !0), v && (o || P || !E && B))) {
        const [G, V] = cr({
          nodeId: E,
          dragItems: c,
          nodeLookup: N
        });
        o?.(v, c, G, V), P?.(v, G, V), E || B?.(v, V);
      }
    }
    async function M() {
      if (!d)
        return;
      const { transform: T, panBy: L, autoPanSpeed: N, autoPanOnNodeDrag: A } = t();
      if (!A) {
        u = !1, cancelAnimationFrame(a);
        return;
      }
      const [I, $] = Qr(l, d, N);
      (I !== 0 || $ !== 0) && (s.x = (s.x ?? 0) - I / T[2], s.y = (s.y ?? 0) - $ / T[2], await L({ x: I, y: $ }) && _(s)), a = requestAnimationFrame(M);
    }
    function F(T) {
      const { nodeLookup: L, multiSelectionActive: N, nodesDraggable: A, transform: I, snapGrid: $, snapToGrid: z, selectNodesOnDrag: P, onNodeDragStart: B, onSelectionDragStart: K, unselectNodesAndEdges: O } = t();
      f = !0, (!P || !S) && !N && E && (L.get(E)?.selected || O()), S && P && E && e?.(E);
      const Z = Un(T.sourceEvent, { transform: I, snapGrid: $, snapToGrid: z, containerBounds: d });
      if (s = Z, c = ay(L, A, Z, E), c.size > 0 && (n || B || !E && K)) {
        const [X, oe] = cr({
          nodeId: E,
          dragItems: c,
          nodeLookup: L
        });
        n?.(T.sourceEvent, c, X, oe), B?.(T.sourceEvent, X, oe), E || K?.(T.sourceEvent, oe);
      }
    }
    const D = qc().clickDistance(j).on("start", (T) => {
      const { domNode: L, nodeDragThreshold: N, transform: A, snapGrid: I, snapToGrid: $ } = t();
      d = L?.getBoundingClientRect() || null, p = !1, g = !1, v = T.sourceEvent, N === 0 && F(T), s = Un(T.sourceEvent, { transform: A, snapGrid: I, snapToGrid: $, containerBounds: d }), l = tt(T.sourceEvent, d);
    }).on("drag", (T) => {
      const { autoPanOnNodeDrag: L, transform: N, snapGrid: A, snapToGrid: I, nodeDragThreshold: $, nodeLookup: z } = t(), P = Un(T.sourceEvent, { transform: N, snapGrid: A, snapToGrid: I, containerBounds: d });
      if (v = T.sourceEvent, (T.sourceEvent.type === "touchmove" && T.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      E && !z.has(E)) && (p = !0), !p) {
        if (!u && L && f && (u = !0, M()), !f) {
          const B = tt(T.sourceEvent, d), K = B.x - l.x, O = B.y - l.y;
          Math.sqrt(K * K + O * O) > $ && F(T);
        }
        (s.x !== P.xSnapped || s.y !== P.ySnapped) && c && f && (l = tt(T.sourceEvent, d), _(P));
      }
    }).on("end", (T) => {
      if (!f || p) {
        p && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (u = !1, f = !1, cancelAnimationFrame(a), c.size > 0) {
        const { nodeLookup: L, updateNodePositions: N, onNodeDragStop: A, onSelectionDragStop: I } = t();
        if (g && (N(c, !1), g = !1), i || A || !E && I) {
          const [$, z] = cr({
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
      return !T.button && (!k || !ya(L, `.${k}`, b)) && (!y || ya(L, y, b));
    });
    h.call(D);
  }
  function m() {
    h?.on(".drag", null);
  }
  return {
    update: w,
    destroy: m
  };
}
function uy(e, t, n) {
  const o = [], i = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const s of t.values())
    oo(i, dn(s)) > 0 && o.push(s);
  return o;
}
const dy = 250;
function fy(e, t, n, o) {
  let i = [], s = 1 / 0;
  const a = uy(e, n, t + dy);
  for (const c of a) {
    const u = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of u) {
      if (o.nodeId === l.nodeId && o.type === l.type && o.id === l.id)
        continue;
      const { x: d, y: f } = Bt(c, l, l.position, !0), h = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(f - e.y, 2));
      h > t || (h < s ? (i = [{ ...l, x: d, y: f }], s = h) : h === s && i.push({ ...l, x: d, y: f }));
    }
  }
  if (!i.length)
    return null;
  if (i.length > 1) {
    const c = o.type === "source" ? "target" : "source";
    return i.find((u) => u.type === c) ?? i[0];
  }
  return i[0];
}
function Dl(e, t, n, o, i, s = !1) {
  const a = o.get(e);
  if (!a)
    return null;
  const c = i === "strict" ? a.internals.handleBounds?.[t] : [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []], u = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return u && s ? { ...u, ...Bt(a, u, u.position, !0) } : u;
}
function Ml(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function hy(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const Tl = () => !0;
function py(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: i, edgeUpdaterType: s, isTarget: a, domNode: c, nodeLookup: u, lib: l, autoPanOnConnect: d, flowId: f, panBy: h, cancelConnection: p, onConnectStart: g, onConnect: v, onConnectEnd: w, isValidConnection: m = Tl, onReconnectEnd: k, updateConnection: y, getTransform: b, getFromHandle: S, autoPanSpeed: E, dragThreshold: j = 1, handleDomNode: _ }) {
  const M = wl(e.target);
  let F = 0, D;
  const { x: T, y: L } = tt(e), N = Ml(s, _), A = c?.getBoundingClientRect();
  let I = !1;
  if (!A || !N)
    return;
  const $ = Dl(i, N, o, u, t);
  if (!$)
    return;
  let z = tt(e, A), P = !1, B = null, K = !1, O = null;
  function Z() {
    if (!d || !A)
      return;
    const [ce, J] = Qr(z, A, E);
    h({ x: ce, y: J }), F = requestAnimationFrame(Z);
  }
  const X = {
    ...$,
    nodeId: i,
    type: N,
    position: $.position
  }, oe = u.get(i);
  let G = {
    inProgress: !0,
    isValid: null,
    from: Bt(oe, X, ne.Left, !0),
    fromHandle: X,
    fromPosition: X.position,
    fromNode: oe,
    to: z,
    toHandle: null,
    toPosition: ia[X.position],
    toNode: null,
    pointer: z
  };
  function V() {
    I = !0, y(G), g?.(e, { nodeId: i, handleId: o, handleType: N });
  }
  j === 0 && V();
  function U(ce) {
    if (!I) {
      const { x: ge, y: ve } = tt(ce), Ee = ge - T, $e = ve - L;
      if (!(Ee * Ee + $e * $e > j * j))
        return;
      V();
    }
    if (!S() || !X) {
      ae(ce);
      return;
    }
    const J = b();
    z = tt(ce, A), D = fy(mn(z, J, !1, [1, 1]), n, u, X), P || (Z(), P = !0);
    const ee = $l(ce, {
      handle: D,
      connectionMode: t,
      fromNodeId: i,
      fromHandleId: o,
      fromType: a ? "target" : "source",
      isValidConnection: m,
      doc: M,
      lib: l,
      flowId: f,
      nodeLookup: u
    });
    O = ee.handleDomNode, B = ee.connection, K = hy(!!D, ee.isValid);
    const de = u.get(i), H = de ? Bt(de, X, ne.Left, !0) : G.from, Q = {
      ...G,
      from: H,
      isValid: K,
      to: ee.toHandle && K ? fn({ x: ee.toHandle.x, y: ee.toHandle.y }, J) : z,
      toHandle: ee.toHandle,
      toPosition: K && ee.toHandle ? ee.toHandle.position : ia[X.position],
      toNode: ee.toHandle ? u.get(ee.toHandle.nodeId) : null,
      pointer: z
    };
    y(Q), G = Q;
  }
  function ae(ce) {
    if (!("touches" in ce && ce.touches.length > 0)) {
      if (I) {
        (D || O) && B && K && v?.(B);
        const { inProgress: J, ...ee } = G, de = {
          ...ee,
          toPosition: G.toHandle ? G.toPosition : null
        };
        w?.(ce, de), s && k?.(ce, de);
      }
      p(), cancelAnimationFrame(F), P = !1, K = !1, B = null, O = null, M.removeEventListener("mousemove", U), M.removeEventListener("mouseup", ae), M.removeEventListener("touchmove", U), M.removeEventListener("touchend", ae);
    }
  }
  M.addEventListener("mousemove", U), M.addEventListener("mouseup", ae), M.addEventListener("touchmove", U), M.addEventListener("touchend", ae);
}
function $l(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: i, fromType: s, doc: a, lib: c, flowId: u, isValidConnection: l = Tl, nodeLookup: d }) {
  const f = s === "target", h = t ? a.querySelector(`.${c}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: p, y: g } = tt(e), v = a.elementFromPoint(p, g), w = v?.classList.contains(`${c}-flow__handle`) ? v : h, m = {
    handleDomNode: w,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (w) {
    const k = Ml(void 0, w), y = w.getAttribute("data-nodeid"), b = w.getAttribute("data-handleid"), S = w.classList.contains("connectable"), E = w.classList.contains("connectableend");
    if (!y || !k)
      return m;
    const j = {
      source: f ? y : o,
      sourceHandle: f ? b : i,
      target: f ? o : y,
      targetHandle: f ? i : b
    };
    m.connection = j;
    const M = S && E && (n === ln.Strict ? f && k === "source" || !f && k === "target" : y !== o || b !== i);
    m.isValid = M && l(j), m.toHandle = Dl(y, k, b, d, n, !0);
  }
  return m;
}
const Tr = {
  onPointerDown: py,
  isValid: $l
};
function gy({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const i = Fe(e);
  function s({ translateExtent: c, width: u, height: l, zoomStep: d = 1, pannable: f = !0, zoomable: h = !0, inversePan: p = !1 }) {
    const g = (y) => {
      if (y.sourceEvent.type !== "wheel" || !t)
        return;
      const b = n(), S = y.sourceEvent.ctrlKey && io() ? 10 : 1, E = -y.sourceEvent.deltaY * (y.sourceEvent.deltaMode === 1 ? 0.05 : y.sourceEvent.deltaMode ? 1 : 2e-3) * d, j = b[2] * Math.pow(2, E * S);
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
      }, M = [
        [0, 0],
        [u, l]
      ];
      t.setViewportConstrained({
        x: _.x,
        y: _.y,
        zoom: b[2]
      }, M, c);
    }, k = al().on("start", w).on("zoom", f ? m : null).on("zoom.wheel", h ? g : null);
    i.call(k, {});
  }
  function a() {
    i.on("zoom", null);
  }
  return {
    update: s,
    destroy: a,
    pointer: Je
  };
}
const Ii = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), lr = ({ x: e, y: t, zoom: n }) => ki.translate(e, t).scale(n), tn = (e, t) => e.target.closest(`.${t}`), Pl = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), yy = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, ur = (e, t = 0, n = yy, o = () => {
}) => {
  const i = typeof t == "number" && t > 0;
  return i || o(), i ? e.transition().duration(t).ease(n).on("end", o) : e;
}, zl = (e) => {
  const t = e.ctrlKey && io() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function my({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: i, panOnScrollSpeed: s, zoomOnPinch: a, onPanZoomStart: c, onPanZoom: u, onPanZoomEnd: l }) {
  return (d) => {
    if (tn(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && a) {
      const w = Je(d), m = zl(d), k = f * Math.pow(2, m);
      o.scaleTo(n, k, w, d);
      return;
    }
    const h = d.deltaMode === 1 ? 20 : 1;
    let p = i === Pt.Vertical ? 0 : d.deltaX * h, g = i === Pt.Horizontal ? 0 : d.deltaY * h;
    !io() && d.shiftKey && i !== Pt.Vertical && (p = d.deltaY * h, g = 0), o.translateBy(
      n,
      -(p / f) * s,
      -(g / f) * s,
      // @ts-ignore
      { internal: !0 }
    );
    const v = Ii(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(d, v), e.panScrollTimeout = setTimeout(() => {
      l?.(d, v), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(d, v));
  };
}
function xy({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, i) {
    const s = o.type === "wheel", a = !t && s && !o.ctrlKey, c = tn(o, e);
    if (o.ctrlKey && s && c && o.preventDefault(), a || c)
      return null;
    o.preventDefault(), n.call(this, o, i);
  };
}
function wy({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const i = Ii(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = i, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, i);
  };
}
function vy({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: i }) {
  return (s) => {
    e.usedRightMouseButton = !!(n && Pl(t, e.mouseButton ?? 0)), s.sourceEvent?.sync || o([s.transform.x, s.transform.y, s.transform.k]), i && !s.sourceEvent?.internal && i?.(s.sourceEvent, Ii(s.transform));
  };
}
function by({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: i, onPaneContextMenu: s }) {
  return (a) => {
    if (!a.sourceEvent?.internal && (e.isZoomingOrPanning = !1, s && Pl(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && a.sourceEvent && s(a.sourceEvent), e.usedRightMouseButton = !1, o(!1), i)) {
      const c = Ii(a.transform);
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
function Ny({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: i, zoomOnDoubleClick: s, userSelectionActive: a, noWheelClassName: c, noPanClassName: u, lib: l, connectionInProgress: d }) {
  return (f) => {
    const h = e || t, p = n && f.ctrlKey, g = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (tn(f, `${l}-flow__node`) || tn(f, `${l}-flow__edge`)))
      return !0;
    if (!o && !h && !i && !s && !n || a || d && !g || tn(f, c) && g || tn(f, u) && (!g || i && g && !e) || !n && f.ctrlKey && g)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!h && !i && !p && g || !o && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(o) && !o.includes(f.button) && f.type === "mousedown")
      return !1;
    const v = Array.isArray(o) && o.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || g) && v;
  };
}
function jy({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: i, onPanZoom: s, onPanZoomStart: a, onPanZoomEnd: c, onDraggingChange: u }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = al().scaleExtent([t, n]).translateExtent(o), h = Fe(e).call(f);
  k({
    x: i.x,
    y: i.y,
    zoom: un(i.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], o);
  const p = h.on("wheel.zoom"), g = h.on("dblclick.zoom");
  f.wheelDelta(zl);
  async function v(D, T) {
    return h ? new Promise((L) => {
      f?.interpolate(T?.interpolate === "linear" ? Xn : Wo).transform(ur(h, T?.duration, T?.ease, () => L(!0)), D);
    }) : !1;
  }
  function w({ noWheelClassName: D, noPanClassName: T, onPaneContextMenu: L, userSelectionActive: N, panOnScroll: A, panOnDrag: I, panOnScrollMode: $, panOnScrollSpeed: z, preventScrolling: P, zoomOnPinch: B, zoomOnScroll: K, zoomOnDoubleClick: O, zoomActivationKeyPressed: Z, lib: X, onTransformChange: oe, connectionInProgress: se, paneClickDistance: G, selectionOnDrag: V }) {
    N && !l.isZoomingOrPanning && m();
    const U = A && !Z && !N;
    f.clickDistance(V ? 1 / 0 : !et(G) || G < 0 ? 0 : G);
    const ae = U ? my({
      zoomPanValues: l,
      noWheelClassName: D,
      d3Selection: h,
      d3Zoom: f,
      panOnScrollMode: $,
      panOnScrollSpeed: z,
      zoomOnPinch: B,
      onPanZoomStart: a,
      onPanZoom: s,
      onPanZoomEnd: c
    }) : xy({
      noWheelClassName: D,
      preventScrolling: P,
      d3ZoomHandler: p
    });
    h.on("wheel.zoom", ae, { passive: !1 });
    const ce = wy({
      zoomPanValues: l,
      onDraggingChange: u,
      onPanZoomStart: a
    });
    f.on("start", ce);
    const J = vy({
      zoomPanValues: l,
      panOnDrag: I,
      onPaneContextMenu: !!L,
      onPanZoom: s,
      onTransformChange: oe
    });
    f.on("zoom", J);
    const ee = by({
      zoomPanValues: l,
      panOnDrag: I,
      panOnScroll: A,
      onPaneContextMenu: L,
      onPanZoomEnd: c,
      onDraggingChange: u
    });
    f.on("end", ee);
    const de = Ny({
      zoomActivationKeyPressed: Z,
      panOnDrag: I,
      zoomOnScroll: K,
      panOnScroll: A,
      zoomOnDoubleClick: O,
      zoomOnPinch: B,
      userSelectionActive: N,
      noPanClassName: T,
      noWheelClassName: D,
      lib: X,
      connectionInProgress: se
    });
    f.filter(de), O ? h.on("dblclick.zoom", g) : h.on("dblclick.zoom", null);
  }
  function m() {
    f.on("zoom", null);
  }
  async function k(D, T, L) {
    const N = lr(D), A = f?.constrain()(N, T, L);
    return A && await v(A), A;
  }
  async function y(D, T) {
    const L = lr(D);
    return await v(L, T), L;
  }
  function b(D) {
    if (h) {
      const T = lr(D), L = h.property("__zoom");
      (L.k !== D.zoom || L.x !== D.x || L.y !== D.y) && f?.transform(h, T, null, { sync: !0 });
    }
  }
  function S() {
    const D = h ? sl(h.node()) : { x: 0, y: 0, k: 1 };
    return { x: D.x, y: D.y, zoom: D.k };
  }
  async function E(D, T) {
    return h ? new Promise((L) => {
      f?.interpolate(T?.interpolate === "linear" ? Xn : Wo).scaleTo(ur(h, T?.duration, T?.ease, () => L(!0)), D);
    }) : !1;
  }
  async function j(D, T) {
    return h ? new Promise((L) => {
      f?.interpolate(T?.interpolate === "linear" ? Xn : Wo).scaleBy(ur(h, T?.duration, T?.ease, () => L(!0)), D);
    }) : !1;
  }
  function _(D) {
    f?.scaleExtent(D);
  }
  function M(D) {
    f?.translateExtent(D);
  }
  function F(D) {
    const T = !et(D) || D < 0 ? 0 : D;
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
    setTranslateExtent: M,
    syncViewport: b,
    setClickDistance: F
  };
}
var hn;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(hn || (hn = {}));
function Sy({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: i, affectsY: s }) {
  const a = e - t, c = n - o, u = [a > 0 ? 1 : a < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return a && i && (u[0] = u[0] * -1), c && s && (u[1] = u[1] * -1), u;
}
function ma(e) {
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
function xa(e, t) {
  return e ? !t : t;
}
function ky(e, t, n, o, i, s, a, c) {
  let { affectsX: u, affectsY: l } = t;
  const { isHorizontal: d, isVertical: f } = t, h = d && f, { xSnapped: p, ySnapped: g } = n, { minWidth: v, maxWidth: w, minHeight: m, maxHeight: k } = o, { x: y, y: b, width: S, height: E, aspectRatio: j } = e;
  let _ = Math.floor(d ? p - e.pointerX : 0), M = Math.floor(f ? g - e.pointerY : 0);
  const F = S + (u ? -_ : _), D = E + (l ? -M : M), T = -s[0] * S, L = -s[1] * E;
  let N = Lo(F, v, w), A = Lo(D, m, k);
  if (a) {
    let z = 0, P = 0;
    u && _ < 0 ? z = wt(y + _ + T, a[0][0]) : !u && _ > 0 && (z = vt(y + F + T, a[1][0])), l && M < 0 ? P = wt(b + M + L, a[0][1]) : !l && M > 0 && (P = vt(b + D + L, a[1][1])), N = Math.max(N, z), A = Math.max(A, P);
  }
  if (c) {
    let z = 0, P = 0;
    u && _ > 0 ? z = vt(y + _, c[0][0]) : !u && _ < 0 && (z = wt(y + F, c[1][0])), l && M > 0 ? P = vt(b + M, c[0][1]) : !l && M < 0 && (P = wt(b + D, c[1][1])), N = Math.max(N, z), A = Math.max(A, P);
  }
  if (i) {
    if (d) {
      const z = Lo(F / j, m, k) * j;
      if (N = Math.max(N, z), a) {
        let P = 0;
        !u && !l || u && !l && h ? P = vt(b + L + F / j, a[1][1]) * j : P = wt(b + L + (u ? _ : -_) / j, a[0][1]) * j, N = Math.max(N, P);
      }
      if (c) {
        let P = 0;
        !u && !l || u && !l && h ? P = wt(b + F / j, c[1][1]) * j : P = vt(b + (u ? _ : -_) / j, c[0][1]) * j, N = Math.max(N, P);
      }
    }
    if (f) {
      const z = Lo(D * j, v, w) / j;
      if (A = Math.max(A, z), a) {
        let P = 0;
        !u && !l || l && !u && h ? P = vt(y + D * j + T, a[1][0]) / j : P = wt(y + (l ? M : -M) * j + T, a[0][0]) / j, A = Math.max(A, P);
      }
      if (c) {
        let P = 0;
        !u && !l || l && !u && h ? P = wt(y + D * j, c[1][0]) / j : P = vt(y + (l ? M : -M) * j, c[0][0]) / j, A = Math.max(A, P);
      }
    }
  }
  M = M + (M < 0 ? A : -A), _ = _ + (_ < 0 ? N : -N), i && (h ? F > D * j ? M = (xa(u, l) ? -_ : _) / j : _ = (xa(u, l) ? -M : M) * j : d ? (M = _ / j, l = u) : (_ = M * j, u = l));
  const I = u ? y + _ : y, $ = l ? b + M : b;
  return {
    width: S + (u ? -_ : _),
    height: E + (l ? -M : M),
    x: s[0] * _ * (u ? -1 : 1) + I,
    y: s[1] * M * (l ? -1 : 1) + $
  };
}
const Rl = { width: 0, height: 0, x: 0, y: 0 }, Ey = {
  ...Rl,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function Cy(e, t, n) {
  const o = t.position.x + e.position.x, i = t.position.y + e.position.y, s = e.measured.width ?? 0, a = e.measured.height ?? 0, c = n[0] * s, u = n[1] * a;
  return [
    [o - c, i - u],
    [o + s - c, i + a - u]
  ];
}
function Iy({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: i }) {
  const s = Fe(e);
  let a = {
    controlDirection: ma("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: l, boundaries: d, keepAspectRatio: f, resizeDirection: h, onResizeStart: p, onResize: g, onResizeEnd: v, shouldResize: w }) {
    let m = { ...Rl }, k = { ...Ey };
    a = {
      boundaries: d,
      resizeDirection: h,
      keepAspectRatio: f,
      controlDirection: ma(l)
    };
    let y, b = null, S = [], E, j, _, M = !1;
    const F = qc().on("start", (D) => {
      const { nodeLookup: T, transform: L, snapGrid: N, snapToGrid: A, nodeOrigin: I, paneDomNode: $ } = n();
      if (y = T.get(t), !y)
        return;
      b = $?.getBoundingClientRect() ?? null;
      const { xSnapped: z, ySnapped: P } = Un(D.sourceEvent, {
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
      for (const [B, K] of T)
        if (K.parentId === t && (S.push({
          id: B,
          position: { ...K.position },
          extent: K.extent
        }), K.extent === "parent" || K.expandParent)) {
          const O = Cy(K, y, K.origin ?? I);
          _ ? _ = [
            [Math.min(O[0][0], _[0][0]), Math.min(O[0][1], _[0][1])],
            [Math.max(O[1][0], _[1][0]), Math.max(O[1][1], _[1][1])]
          ] : _ = O;
        }
      p?.(D, { ...m });
    }).on("drag", (D) => {
      const { transform: T, snapGrid: L, snapToGrid: N, nodeOrigin: A } = n(), I = Un(D.sourceEvent, {
        transform: T,
        snapGrid: L,
        snapToGrid: N,
        containerBounds: b
      }), $ = [];
      if (!y)
        return;
      const { x: z, y: P, width: B, height: K } = m, O = {}, Z = y.origin ?? A, { width: X, height: oe, x: se, y: G } = ky(k, a.controlDirection, I, a.boundaries, a.keepAspectRatio, Z, j, _), V = X !== B, U = oe !== K, ae = se !== z && V, ce = G !== P && U;
      if (!ae && !ce && !V && !U)
        return;
      if ((ae || ce || Z[0] === 1 || Z[1] === 1) && (O.x = ae ? se : m.x, O.y = ce ? G : m.y, m.x = O.x, m.y = O.y, S.length > 0)) {
        const H = se - z, Q = G - P;
        for (const ge of S)
          ge.position = {
            x: ge.position.x - H + Z[0] * (X - B),
            y: ge.position.y - Q + Z[1] * (oe - K)
          }, $.push(ge);
      }
      if ((V || U) && (O.width = V && (!a.resizeDirection || a.resizeDirection === "horizontal") ? X : m.width, O.height = U && (!a.resizeDirection || a.resizeDirection === "vertical") ? oe : m.height, m.width = O.width, m.height = O.height), E && y.expandParent) {
        const H = Z[0] * (O.width ?? 0);
        O.x && O.x < H && (m.x = H, k.x = k.x - (O.x - H));
        const Q = Z[1] * (O.height ?? 0);
        O.y && O.y < Q && (m.y = Q, k.y = k.y - (O.y - Q));
      }
      const J = Sy({
        width: m.width,
        prevWidth: B,
        height: m.height,
        prevHeight: K,
        affectsX: a.controlDirection.affectsX,
        affectsY: a.controlDirection.affectsY
      }), ee = { ...m, direction: J };
      w?.(D, ee) !== !1 && (M = !0, g?.(D, ee), o(O, $));
    }).on("end", (D) => {
      M && (v?.(D, { ...m }), i?.({ ...m }), M = !1);
    });
    s.call(F);
  }
  function u() {
    s.on(".drag", null);
  }
  return {
    update: c,
    destroy: u
  };
}
var dr = { exports: {} }, fr = {}, hr = { exports: {} }, pr = {};
var wa;
function Ay() {
  if (wa) return pr;
  wa = 1;
  var e = ct;
  function t(f, h) {
    return f === h && (f !== 0 || 1 / f === 1 / h) || f !== f && h !== h;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, i = e.useEffect, s = e.useLayoutEffect, a = e.useDebugValue;
  function c(f, h) {
    var p = h(), g = o({ inst: { value: p, getSnapshot: h } }), v = g[0].inst, w = g[1];
    return s(
      function() {
        v.value = p, v.getSnapshot = h, u(v) && w({ inst: v });
      },
      [f, p, h]
    ), i(
      function() {
        return u(v) && w({ inst: v }), f(function() {
          u(v) && w({ inst: v });
        });
      },
      [f]
    ), a(p), p;
  }
  function u(f) {
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
  var d = typeof window > "u" || typeof window.document > "u" || typeof window.document.createElement > "u" ? l : c;
  return pr.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : d, pr;
}
var va;
function _y() {
  return va || (va = 1, hr.exports = Ay()), hr.exports;
}
var ba;
function Dy() {
  if (ba) return fr;
  ba = 1;
  var e = ct, t = _y();
  function n(l, d) {
    return l === d && (l !== 0 || 1 / l === 1 / d) || l !== l && d !== d;
  }
  var o = typeof Object.is == "function" ? Object.is : n, i = t.useSyncExternalStore, s = e.useRef, a = e.useEffect, c = e.useMemo, u = e.useDebugValue;
  return fr.useSyncExternalStoreWithSelector = function(l, d, f, h, p) {
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
            return m(d());
          },
          S === null ? void 0 : function() {
            return m(S());
          }
        ];
      },
      [d, f, h, p]
    );
    var w = i(l, g[0], g[1]);
    return a(
      function() {
        v.hasValue = !0, v.value = w;
      },
      [w]
    ), u(w), w;
  }, fr;
}
var Na;
function My() {
  return Na || (Na = 1, dr.exports = Dy()), dr.exports;
}
var Ty = My();
const $y = /* @__PURE__ */ Yd(Ty), Py = {}, ja = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (d, f) => {
    const h = typeof d == "function" ? d(t) : d;
    if (!Object.is(h, t)) {
      const p = t;
      t = f ?? (typeof h != "object" || h === null) ? h : Object.assign({}, t, h), n.forEach((g) => g(t, p));
    }
  }, i = () => t, u = { setState: o, getState: i, getInitialState: () => l, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (Py ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(o, i, u);
  return u;
}, zy = (e) => e ? ja(e) : ja, { useDebugValue: Ry } = ct, { useSyncExternalStoreWithSelector: Ly } = $y, Vy = (e) => e;
function Ll(e, t = Vy, n) {
  const o = Ly(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return Ry(o), o;
}
const Sa = (e, t) => {
  const n = zy(e), o = (i, s = t) => Ll(n, i, s);
  return Object.assign(o, n), o;
}, Oy = (e, t) => e ? Sa(e, t) : Sa;
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
var gr = { exports: {} }, Te = {};
var ka;
function Hy() {
  if (ka) return Te;
  ka = 1;
  var e = ct;
  function t(u) {
    var l = "https://react.dev/errors/" + u;
    if (1 < arguments.length) {
      l += "?args[]=" + encodeURIComponent(arguments[1]);
      for (var d = 2; d < arguments.length; d++)
        l += "&args[]=" + encodeURIComponent(arguments[d]);
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
  }, i = /* @__PURE__ */ Symbol.for("react.portal");
  function s(u, l, d) {
    var f = 3 < arguments.length && arguments[3] !== void 0 ? arguments[3] : null;
    return {
      $$typeof: i,
      key: f == null ? null : "" + f,
      children: u,
      containerInfo: l,
      implementation: d
    };
  }
  var a = e.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE;
  function c(u, l) {
    if (u === "font") return "";
    if (typeof l == "string")
      return l === "use-credentials" ? l : "";
  }
  return Te.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, Te.createPortal = function(u, l) {
    var d = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!l || l.nodeType !== 1 && l.nodeType !== 9 && l.nodeType !== 11)
      throw Error(t(299));
    return s(u, l, null, d);
  }, Te.flushSync = function(u) {
    var l = a.T, d = o.p;
    try {
      if (a.T = null, o.p = 2, u) return u();
    } finally {
      a.T = l, o.p = d, o.d.f();
    }
  }, Te.preconnect = function(u, l) {
    typeof u == "string" && (l ? (l = l.crossOrigin, l = typeof l == "string" ? l === "use-credentials" ? l : "" : void 0) : l = null, o.d.C(u, l));
  }, Te.prefetchDNS = function(u) {
    typeof u == "string" && o.d.D(u);
  }, Te.preinit = function(u, l) {
    if (typeof u == "string" && l && typeof l.as == "string") {
      var d = l.as, f = c(d, l.crossOrigin), h = typeof l.integrity == "string" ? l.integrity : void 0, p = typeof l.fetchPriority == "string" ? l.fetchPriority : void 0;
      d === "style" ? o.d.S(
        u,
        typeof l.precedence == "string" ? l.precedence : void 0,
        {
          crossOrigin: f,
          integrity: h,
          fetchPriority: p
        }
      ) : d === "script" && o.d.X(u, {
        crossOrigin: f,
        integrity: h,
        fetchPriority: p,
        nonce: typeof l.nonce == "string" ? l.nonce : void 0
      });
    }
  }, Te.preinitModule = function(u, l) {
    if (typeof u == "string")
      if (typeof l == "object" && l !== null) {
        if (l.as == null || l.as === "script") {
          var d = c(
            l.as,
            l.crossOrigin
          );
          o.d.M(u, {
            crossOrigin: d,
            integrity: typeof l.integrity == "string" ? l.integrity : void 0,
            nonce: typeof l.nonce == "string" ? l.nonce : void 0
          });
        }
      } else l == null && o.d.M(u);
  }, Te.preload = function(u, l) {
    if (typeof u == "string" && typeof l == "object" && l !== null && typeof l.as == "string") {
      var d = l.as, f = c(d, l.crossOrigin);
      o.d.L(u, d, {
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
  }, Te.preloadModule = function(u, l) {
    if (typeof u == "string")
      if (l) {
        var d = c(l.as, l.crossOrigin);
        o.d.m(u, {
          as: typeof l.as == "string" && l.as !== "script" ? l.as : void 0,
          crossOrigin: d,
          integrity: typeof l.integrity == "string" ? l.integrity : void 0
        });
      } else o.d.m(u);
  }, Te.requestFormReset = function(u) {
    o.d.r(u);
  }, Te.unstable_batchedUpdates = function(u, l) {
    return u(l);
  }, Te.useFormState = function(u, l, d) {
    return a.H.useFormState(u, l, d);
  }, Te.useFormStatus = function() {
    return a.H.useHostTransitionStatus();
  }, Te.version = "19.2.7", Te;
}
var Ea;
function By() {
  if (Ea) return gr.exports;
  Ea = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), gr.exports = Hy(), gr.exports;
}
var Fy = By();
const Ai = Fr(null), Wy = Ai.Provider, Vl = Ke.error001("react");
function he(e, t) {
  const n = lo(Ai);
  if (n === null)
    throw new Error(Vl);
  return Ll(n, e, t);
}
function je() {
  const e = lo(Ai);
  if (e === null)
    throw new Error(Vl);
  return ue(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const Ca = { display: "none" }, Ky = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, Ol = "react-flow__node-desc", Hl = "react-flow__edge-desc", qy = "react-flow__aria-live", Xy = (e) => e.ariaLiveMessage, Uy = (e) => e.ariaLabelConfig;
function Yy({ rfId: e }) {
  const t = he(Xy);
  return r.jsx("div", { id: `${qy}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: Ky, children: t });
}
function Zy({ rfId: e, disableKeyboardA11y: t }) {
  const n = he(Uy);
  return r.jsxs(r.Fragment, { children: [r.jsx("div", { id: `${Ol}-${e}`, style: Ca, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), r.jsx("div", { id: `${Hl}-${e}`, style: Ca, children: n["edge.a11yDescription.default"] }), !t && r.jsx(Yy, { rfId: e })] });
}
const _i = vi(({ position: e = "top-left", children: t, className: n, style: o, ...i }, s) => {
  const a = `${e}`.split("-");
  return r.jsx("div", { className: Ce(["react-flow__panel", n, ...a]), style: o, ref: s, ...i, children: t });
});
_i.displayName = "Panel";
function Gy({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : r.jsx(_i, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: r.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const Jy = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, Vo = (e) => e.id;
function Qy(e, t) {
  return Ne(e.selectedNodes.map(Vo), t.selectedNodes.map(Vo)) && Ne(e.selectedEdges.map(Vo), t.selectedEdges.map(Vo));
}
function em({ onSelectionChange: e }) {
  const t = je(), { selectedNodes: n, selectedEdges: o } = he(Jy, Qy);
  return te(() => {
    const i = { nodes: n, edges: o };
    e?.(i), t.getState().onSelectionChangeHandlers.forEach((s) => s(i));
  }, [n, o, e]), null;
}
const tm = (e) => !!e.onSelectionChangeHandlers;
function nm({ onSelectionChange: e }) {
  const t = he(tm);
  return e || t ? r.jsx(em, { onSelectionChange: e }) : null;
}
const Bl = [0, 0], om = { x: 0, y: 0, zoom: 1 }, im = [
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
], Ia = [...im, "rfId"], rm = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), Aa = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: to,
  nodeOrigin: Bl,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function sm(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: i, setTranslateExtent: s, setNodeExtent: a, reset: c, setDefaultNodesAndEdges: u } = he(rm, Ne), l = je();
  te(() => (u(e.defaultNodes, e.defaultEdges), () => {
    d.current = Aa, c();
  }), []);
  const d = re(Aa);
  return te(
    () => {
      for (const f of Ia) {
        const h = e[f], p = d.current[f];
        h !== p && (typeof e[f] > "u" || (f === "nodes" ? t(h) : f === "edges" ? n(h) : f === "minZoom" ? o(h) : f === "maxZoom" ? i(h) : f === "translateExtent" ? s(h) : f === "nodeExtent" ? a(h) : f === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: Hg(h) }) : f === "fitView" ? l.setState({ fitViewQueued: h }) : f === "fitViewOptions" ? l.setState({ fitViewOptions: h }) : l.setState({ [f]: h })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    Ia.map((f) => e[f])
  ), null;
}
function _a() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function am(e) {
  const [t, n] = q(e === "system" ? null : e);
  return te(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = _a(), i = () => n(o?.matches ? "dark" : "light");
    return i(), o?.addEventListener("change", i), () => {
      o?.removeEventListener("change", i);
    };
  }, [e]), t !== null ? t : _a()?.matches ? "dark" : "light";
}
const Da = typeof document < "u" ? document : null;
function ro(e = null, t = { target: Da, actInsideInputWithModifier: !0 }) {
  const [n, o] = q(!1), i = re(!1), s = re(/* @__PURE__ */ new Set([])), [a, c] = ue(() => {
    if (e !== null) {
      const l = (Array.isArray(e) ? e : [e]).filter((f) => typeof f == "string").map((f) => f.replace("+", `
`).replace(`

`, `
+`).split(`
`)), d = l.reduce((f, h) => f.concat(...h), []);
      return [l, d];
    }
    return [[], []];
  }, [e]);
  return te(() => {
    const u = t?.target ?? Da, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (p) => {
        if (i.current = p.ctrlKey || p.metaKey || p.shiftKey || p.altKey, (!i.current || i.current && !l) && vl(p))
          return !1;
        const v = Ta(p.code, c);
        if (s.current.add(p[v]), Ma(a, s.current, !1)) {
          const w = p.composedPath?.()?.[0] || p.target, m = w?.nodeName === "BUTTON" || w?.nodeName === "A";
          t.preventDefault !== !1 && (i.current || !m) && p.preventDefault(), o(!0);
        }
      }, f = (p) => {
        const g = Ta(p.code, c);
        Ma(a, s.current, !0) ? (o(!1), s.current.clear()) : s.current.delete(p[g]), p.key === "Meta" && s.current.clear(), i.current = !1;
      }, h = () => {
        s.current.clear(), o(!1);
      };
      return u?.addEventListener("keydown", d), u?.addEventListener("keyup", f), window.addEventListener("blur", h), window.addEventListener("contextmenu", h), () => {
        u?.removeEventListener("keydown", d), u?.removeEventListener("keyup", f), window.removeEventListener("blur", h), window.removeEventListener("contextmenu", h);
      };
    }
  }, [e, o]), n;
}
function Ma(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((i) => t.has(i)));
}
function Ta(e, t) {
  return t.includes(e) ? "code" : "key";
}
const cm = () => {
  const e = je();
  return ue(() => ({
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
      const { width: o, height: i, minZoom: s, maxZoom: a, panZoom: c } = e.getState(), u = es(t, o, i, s, a, n?.padding ?? 0.1);
      return c ? (await c.setViewport(u, {
        duration: n?.duration,
        ease: n?.ease,
        interpolate: n?.interpolate
      }), !0) : !1;
    },
    screenToFlowPosition: (t, n = {}) => {
      const { transform: o, snapGrid: i, snapToGrid: s, domNode: a } = e.getState();
      if (!a)
        return t;
      const { x: c, y: u } = a.getBoundingClientRect(), l = {
        x: t.x - c,
        y: t.y - u
      }, d = n.snapGrid ?? i, f = n.snapToGrid ?? s;
      return mn(l, o, f, d);
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
function Fl(e, t) {
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
    for (const u of a)
      lm(u, c);
    n.push(c);
  }
  return i.length && i.forEach((s) => {
    s.index !== void 0 ? n.splice(s.index, 0, { ...s.item }) : n.push({ ...s.item });
  }), n;
}
function lm(e, t) {
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
function Wl(e, t) {
  return Fl(e, t);
}
function Kl(e, t) {
  return Fl(e, t);
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
function $a({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((i) => [i.id, i]));
  for (const [i, s] of e.entries()) {
    const a = t.get(s.id), c = a?.internals?.userNode ?? a;
    c !== void 0 && c !== s && n.push({ id: s.id, item: s, type: "replace" }), c === void 0 && n.push({ item: s, type: "add", index: i });
  }
  for (const [i] of t)
    o.get(i) === void 0 && n.push({ id: i, type: "remove" });
  return n;
}
function Pa(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const ql = yl();
function Xl(e, t, n = {}) {
  return qg(e, t, {
    ...n,
    onError: n.onError ?? ql
  });
}
function um(e, t, n, o = { shouldReplaceId: !0 }) {
  return Xg(e, t, n, {
    ...o,
    onError: o.onError ?? ql
  });
}
const za = (e) => Mg(e), dm = (e) => fl(e);
function Ul(e) {
  return vi(e);
}
const fm = typeof window < "u" ? Wd : te;
function Ra(e) {
  const [t, n] = q(BigInt(0)), [o] = q(() => hm(() => n((i) => i + BigInt(1))));
  return fm(() => {
    const i = o.get();
    i.length && (e(i), o.reset());
  }, [t]), o;
}
function hm(e) {
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
const Yl = Fr(null);
function pm({ children: e }) {
  const t = je(), n = fe((c) => {
    const { nodes: u = [], setNodes: l, hasDefaultNodes: d, onNodesChange: f, nodeLookup: h, fitViewQueued: p, onNodesChangeMiddlewareMap: g } = t.getState();
    let v = u;
    for (const m of c)
      v = typeof m == "function" ? m(v) : m;
    let w = $a({
      items: v,
      lookup: h
    });
    for (const m of g.values())
      w = m(w);
    d && l(v), w.length > 0 ? f?.(w) : p && window.requestAnimationFrame(() => {
      const { fitViewQueued: m, nodes: k, setNodes: y } = t.getState();
      m && y(k);
    });
  }, []), o = Ra(n), i = fe((c) => {
    const { edges: u = [], setEdges: l, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: h } = t.getState();
    let p = u;
    for (const g of c)
      p = typeof g == "function" ? g(p) : g;
    d ? l(p) : f && f($a({
      items: p,
      lookup: h
    }));
  }, []), s = Ra(i), a = ue(() => ({ nodeQueue: o, edgeQueue: s }), []);
  return r.jsx(Yl.Provider, { value: a, children: e });
}
function gm() {
  const e = lo(Yl);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const ym = (e) => !!e.panZoom;
function as() {
  const e = cm(), t = je(), n = gm(), o = he(ym), i = ue(() => {
    const s = (f) => t.getState().nodeLookup.get(f), a = (f) => {
      n.nodeQueue.push(f);
    }, c = (f) => {
      n.edgeQueue.push(f);
    }, u = (f) => {
      const { nodeLookup: h, nodeOrigin: p } = t.getState(), g = za(f) ? f : h.get(f.id), v = g.parentId ? xl(g.position, g.measured, g.parentId, h, p) : g.position, w = {
        ...g,
        position: v,
        width: g.measured?.width ?? g.width,
        height: g.measured?.height ?? g.height
      };
      return dn(w);
    }, l = (f, h, p = { replace: !1 }) => {
      a((g) => g.map((v) => {
        if (v.id === f) {
          const w = typeof h == "function" ? h(v) : h;
          return p.replace && za(w) ? w : { ...v, ...w };
        }
        return v;
      }));
    }, d = (f, h, p = { replace: !1 }) => {
      c((g) => g.map((v) => {
        if (v.id === f) {
          const w = typeof h == "function" ? h(v) : h;
          return p.replace && dm(w) ? w : { ...v, ...w };
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
        const { nodes: p, edges: g, onNodesDelete: v, onEdgesDelete: w, triggerNodeChanges: m, triggerEdgeChanges: k, onDelete: y, onBeforeDelete: b } = t.getState(), { nodes: S, edges: E } = await Rg({
          nodesToRemove: f,
          edgesToRemove: h,
          nodes: p,
          edges: g,
          onBeforeDelete: b
        }), j = E.length > 0, _ = S.length > 0;
        if (j) {
          const M = E.map(Pa);
          w?.(E), k(M);
        }
        if (_) {
          const M = S.map(Pa);
          v?.(S), m(M);
        }
        return (_ || j) && y?.({ nodes: S, edges: E }), { deletedNodes: S, deletedEdges: E };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, h = !0, p) => {
        const g = sa(f), v = g ? f : u(f), w = p !== void 0;
        return v ? (p || t.getState().nodes).filter((m) => {
          const k = t.getState().nodeLookup.get(m.id);
          if (k && !g && (m.id === f.id || !k.internals.positionAbsolute))
            return !1;
          const y = dn(w ? m : k), b = oo(y, v);
          return h && b > 0 || b >= y.width * y.height || b >= v.width * v.height;
        }) : [];
      },
      isNodeIntersecting: (f, h, p = !0) => {
        const v = sa(f) ? f : u(f);
        if (!v)
          return !1;
        const w = oo(v, h);
        return p && w > 0 || w >= h.width * h.height || w >= v.width * v.height;
      },
      updateNode: l,
      updateNodeData: (f, h, p = { replace: !1 }) => {
        l(f, (g) => {
          const v = typeof h == "function" ? h(g) : h;
          return p.replace ? { ...g, data: v } : { ...g, data: { ...g.data, ...v } };
        }, p);
      },
      updateEdge: d,
      updateEdgeData: (f, h, p = { replace: !1 }) => {
        d(f, (g) => {
          const v = typeof h == "function" ? h(g) : h;
          return p.replace ? { ...g, data: v } : { ...g, data: { ...g.data, ...v } };
        }, p);
      },
      getNodesBounds: (f) => {
        const { nodeLookup: h, nodeOrigin: p } = t.getState();
        return Tg(f, { nodeLookup: h, nodeOrigin: p });
      },
      getHandleConnections: ({ type: f, id: h, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}-${f}${h ? `-${h}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: h, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}${f ? h ? `-${f}-${h}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const h = t.getState().fitViewResolver ?? Og();
        return t.setState({ fitViewQueued: !0, fitViewOptions: f, fitViewResolver: h }), n.nodeQueue.push((p) => [...p]), h.promise;
      }
    };
  }, []);
  return ue(() => ({
    ...i,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const La = (e) => e.selected, mm = typeof window < "u" ? window : void 0;
function xm({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = je(), { deleteElements: o } = as(), i = ro(e, { actInsideInputWithModifier: !1 }), s = ro(t, { target: mm });
  te(() => {
    if (i) {
      const { edges: a, nodes: c } = n.getState();
      o({ nodes: c.filter(La), edges: a.filter(La) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [i]), te(() => {
    n.setState({ multiSelectionActive: s });
  }, [s]);
}
function wm(e) {
  const t = je();
  te(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = ts(e.current);
      (o.height === 0 || o.width === 0) && t.getState().onError?.("004", Ke.error004()), t.setState({ width: o.width || 500, height: o.height || 500 });
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
const Di = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, vm = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function bm({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: i = 0.5, panOnScrollMode: s = Pt.Free, zoomOnDoubleClick: a = !0, panOnDrag: c = !0, defaultViewport: u, translateExtent: l, minZoom: d, maxZoom: f, zoomActivationKeyCode: h, preventScrolling: p = !0, children: g, noWheelClassName: v, noPanClassName: w, onViewportChange: m, isControlledViewport: k, paneClickDistance: y, selectionOnDrag: b }) {
  const S = je(), E = re(null), { userSelectionActive: j, lib: _, connectionInProgress: M } = he(vm, Ne), F = ro(h), D = re();
  wm(E);
  const T = fe((L) => {
    m?.({ x: L[0], y: L[1], zoom: L[2] }), k || S.setState({ transform: L });
  }, [m, k]);
  return te(() => {
    if (E.current) {
      D.current = jy({
        domNode: E.current,
        minZoom: d,
        maxZoom: f,
        translateExtent: l,
        viewport: u,
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
      const { x: L, y: N, zoom: A } = D.current.getViewport();
      return S.setState({
        panZoom: D.current,
        transform: [L, N, A],
        domNode: E.current.closest(".react-flow")
      }), () => {
        D.current?.destroy();
      };
    }
  }, []), te(() => {
    D.current?.update({
      onPaneContextMenu: e,
      zoomOnScroll: t,
      zoomOnPinch: n,
      panOnScroll: o,
      panOnScrollSpeed: i,
      panOnScrollMode: s,
      zoomOnDoubleClick: a,
      panOnDrag: c,
      zoomActivationKeyPressed: F,
      preventScrolling: p,
      noPanClassName: w,
      userSelectionActive: j,
      noWheelClassName: v,
      lib: _,
      onTransformChange: T,
      connectionInProgress: M,
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
    F,
    p,
    w,
    j,
    v,
    _,
    T,
    M,
    b,
    y
  ]), r.jsx("div", { className: "react-flow__renderer", ref: E, style: Di, children: g });
}
const Nm = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function jm() {
  const { userSelectionActive: e, userSelectionRect: t } = he(Nm, Ne);
  return e && t ? r.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const yr = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, Sm = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function km({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = no.Full, panOnDrag: o, autoPanOnSelection: i, paneClickDistance: s, selectionOnDrag: a, onSelectionStart: c, onSelectionEnd: u, onPaneClick: l, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: h, onPaneMouseMove: p, onPaneMouseLeave: g, children: v }) {
  const w = re(0), m = je(), { userSelectionActive: k, elementsSelectable: y, dragging: b, connectionInProgress: S, panBy: E, autoPanSpeed: j } = he(Sm, Ne), _ = y && (e || k), M = re(null), F = re(), D = re(/* @__PURE__ */ new Set()), T = re(/* @__PURE__ */ new Set()), L = re(!1), N = re({ x: 0, y: 0 }), A = re(!1), I = (V) => {
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
    d?.(V);
  }, z = f ? (V) => f(V) : void 0, P = (V) => {
    L.current && (V.stopPropagation(), L.current = !1);
  }, B = (V) => {
    const { domNode: U, transform: ae } = m.getState();
    if (F.current = U?.getBoundingClientRect(), !F.current)
      return;
    const ce = V.target === M.current;
    if (!ce && !!V.target.closest(".nokey") || !e || !(a && ce || t) || V.button !== 0 || !V.isPrimary)
      return;
    V.target?.setPointerCapture?.(V.pointerId), L.current = !1;
    const { x: de, y: H } = tt(V.nativeEvent, F.current), Q = mn({ x: de, y: H }, ae);
    m.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: Q.x,
        startY: Q.y,
        x: de,
        y: H
      }
    }), ce || (V.stopPropagation(), V.preventDefault());
  };
  function K(V, U) {
    const { userSelectionRect: ae } = m.getState();
    if (!ae)
      return;
    const { transform: ce, nodeLookup: J, edgeLookup: ee, connectionLookup: de, triggerNodeChanges: H, triggerEdgeChanges: Q, defaultEdgeOptions: ge } = m.getState(), ve = { x: ae.startX, y: ae.startY }, { x: Ee, y: $e } = fn(ve, ce), Ie = {
      startX: ve.x,
      startY: ve.y,
      x: V < Ee ? V : Ee,
      y: U < $e ? U : $e,
      width: Math.abs(V - Ee),
      height: Math.abs(U - $e)
    }, qe = D.current, be = T.current;
    D.current = new Set(Jr(J, Ie, ce, n === no.Partial, !0).map((Le) => Le.id)), T.current = /* @__PURE__ */ new Set();
    const Re = ge?.selectable ?? !0;
    for (const Le of D.current) {
      const Ye = de.get(Le);
      if (Ye)
        for (const { edgeId: Ze } of Ye.values()) {
          const Ge = ee.get(Ze);
          Ge && (Ge.selectable ?? Re) && T.current.add(Ze);
        }
    }
    if (!aa(qe, D.current)) {
      const Le = nn(J, D.current, !0);
      H(Le);
    }
    if (!aa(be, T.current)) {
      const Le = nn(ee, T.current);
      Q(Le);
    }
    m.setState({
      userSelectionRect: Ie,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function O() {
    if (!i || !F.current)
      return;
    const [V, U] = Qr(N.current, F.current, j);
    E({ x: V, y: U }).then((ae) => {
      if (!L.current || !ae) {
        w.current = requestAnimationFrame(O);
        return;
      }
      const { x: ce, y: J } = N.current;
      K(ce, J), w.current = requestAnimationFrame(O);
    });
  }
  const Z = () => {
    cancelAnimationFrame(w.current), w.current = 0, A.current = !1;
  };
  te(() => () => Z(), []);
  const X = (V) => {
    const { userSelectionRect: U, transform: ae, resetSelectedElements: ce } = m.getState();
    if (!F.current || !U)
      return;
    const { x: J, y: ee } = tt(V.nativeEvent, F.current);
    N.current = { x: J, y: ee };
    const de = fn({ x: U.startX, y: U.startY }, ae);
    if (!L.current) {
      const H = t ? 0 : s;
      if (Math.hypot(J - de.x, ee - de.y) <= H)
        return;
      ce(), c?.(V);
    }
    L.current = !0, A.current || (O(), A.current = !0), K(J, ee);
  }, oe = (V) => {
    V.button === 0 && (V.target?.releasePointerCapture?.(V.pointerId), !k && V.target === M.current && m.getState().userSelectionRect && I?.(V), m.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), L.current && (u?.(V), m.setState({
      nodesSelectionActive: D.current.size > 0
    })), Z());
  }, se = (V) => {
    V.target?.releasePointerCapture?.(V.pointerId), Z();
  }, G = o === !0 || Array.isArray(o) && o.includes(0);
  return r.jsxs("div", { className: Ce(["react-flow__pane", { draggable: G, dragging: b, selection: e }]), onClick: _ ? void 0 : yr(I, M), onContextMenu: yr($, M), onWheel: yr(z, M), onPointerEnter: _ ? void 0 : h, onPointerMove: _ ? X : p, onPointerUp: _ ? oe : void 0, onPointerCancel: _ ? se : void 0, onPointerDownCapture: _ ? B : void 0, onClickCapture: _ ? P : void 0, onPointerLeave: g, ref: M, style: Di, children: [v, r.jsx(jm, {})] });
}
function $r({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: i, unselectNodesAndEdges: s, multiSelectionActive: a, nodeLookup: c, onError: u } = t.getState(), l = c.get(e);
  if (!l) {
    u?.("012", Ke.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && a) && (s({ nodes: [l], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : i([e]);
}
function Zl({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: i, isSelectable: s, nodeClickDistance: a }) {
  const c = je(), [u, l] = q(!1), d = re();
  return te(() => {
    d.current = ly({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (f) => {
        $r({
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
  }, []), te(() => {
    if (!(t || !e.current || !d.current))
      return d.current.update({
        noDragClassName: n,
        handleSelector: o,
        domNode: e.current,
        isSelectable: s,
        nodeId: i,
        nodeClickDistance: a
      }), () => {
        d.current?.destroy();
      };
  }, [n, o, t, s, e, i, a]), u;
}
const Em = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function Gl() {
  const e = je();
  return fe((n) => {
    const { nodeExtent: o, snapToGrid: i, snapGrid: s, nodesDraggable: a, onError: c, updateNodePositions: u, nodeLookup: l, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), h = Em(a), p = i ? s[0] : 5, g = i ? s[1] : 5, v = n.direction.x * p * n.factor, w = n.direction.y * g * n.factor;
    for (const [, m] of l) {
      if (!h(m))
        continue;
      let k = {
        x: m.internals.positionAbsolute.x + v,
        y: m.internals.positionAbsolute.y + w
      };
      i && (k = go(k, s));
      const { position: y, positionAbsolute: b } = hl({
        nodeId: m.id,
        nextPosition: k,
        nodeLookup: l,
        nodeExtent: o,
        nodeOrigin: d,
        onError: c
      });
      m.position = y, m.internals.positionAbsolute = b, f.set(m.id, m);
    }
    u(f);
  }, []);
}
const cs = Fr(null), Cm = cs.Provider;
cs.Consumer;
const Jl = () => lo(cs), Im = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), Am = (e, t, n) => (o) => {
  const { connectionClickStartHandle: i, connectionMode: s, connection: a } = o, { fromHandle: c, toHandle: u, isValid: l } = a, d = u?.nodeId === e && u?.id === t && u?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: d,
    clickConnecting: i?.nodeId === e && i?.id === t && i?.type === n,
    isPossibleEndHandle: s === ln.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!i,
    valid: d && l
  };
};
function _m({ type: e = "source", position: t = ne.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: i = !0, isConnectableEnd: s = !0, id: a, onConnect: c, children: u, className: l, onMouseDown: d, onTouchStart: f, ...h }, p) {
  const g = a || null, v = e === "target", w = je(), m = Jl(), { connectOnClick: k, noPanClassName: y, rfId: b } = he(Im, Ne), { connectingFrom: S, connectingTo: E, clickConnecting: j, isPossibleEndHandle: _, connectionInProcess: M, clickConnectionInProcess: F, valid: D } = he(Am(m, g, e), Ne);
  m || w.getState().onError?.("010", Ke.error010());
  const T = (A) => {
    const { defaultEdgeOptions: I, onConnect: $, hasDefaultEdges: z } = w.getState(), P = {
      ...I,
      ...A
    };
    if (z) {
      const { edges: B, setEdges: K, onError: O } = w.getState();
      K(Xl(P, B, { onError: O }));
    }
    $?.(P), c?.(P);
  }, L = (A) => {
    if (!m)
      return;
    const I = bl(A.nativeEvent);
    if (i && (I && A.button === 0 || !I)) {
      const $ = w.getState();
      Tr.onPointerDown(A.nativeEvent, {
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
    I ? d?.(A) : f?.(A);
  }, N = (A) => {
    const { onClickConnectStart: I, onClickConnectEnd: $, connectionClickStartHandle: z, connectionMode: P, isValidConnection: B, lib: K, rfId: O, nodeLookup: Z, connection: X } = w.getState();
    if (!m || !z && !i)
      return;
    if (!z) {
      I?.(A.nativeEvent, { nodeId: m, handleId: g, handleType: e }), w.setState({ connectionClickStartHandle: { nodeId: m, type: e, id: g } });
      return;
    }
    const oe = wl(A.target), se = n || B, { connection: G, isValid: V } = Tr.isValid(A.nativeEvent, {
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
      flowId: O,
      doc: oe,
      lib: K,
      nodeLookup: Z
    });
    V && G && T(G);
    const U = structuredClone(X);
    delete U.inProgress, U.toPosition = U.toHandle ? U.toHandle.position : null, $?.(A, U), w.setState({ connectionClickStartHandle: null });
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
      valid: D,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!M || _) && (M || F ? s : i)
    }
  ]), onMouseDown: L, onTouchStart: L, onClick: k ? N : void 0, ref: p, ...h, children: u });
}
const pn = ke(Ul(_m));
function Dm({ data: e, isConnectable: t, sourcePosition: n = ne.Bottom }) {
  return r.jsxs(r.Fragment, { children: [e?.label, r.jsx(pn, { type: "source", position: n, isConnectable: t })] });
}
function Mm({ data: e, isConnectable: t, targetPosition: n = ne.Top, sourcePosition: o = ne.Bottom }) {
  return r.jsxs(r.Fragment, { children: [r.jsx(pn, { type: "target", position: n, isConnectable: t }), e?.label, r.jsx(pn, { type: "source", position: o, isConnectable: t })] });
}
function Tm() {
  return null;
}
function $m({ data: e, isConnectable: t, targetPosition: n = ne.Top }) {
  return r.jsxs(r.Fragment, { children: [r.jsx(pn, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const ci = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, Va = {
  input: Dm,
  default: Mm,
  output: $m,
  group: Tm
};
function Pm(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const zm = (e) => {
  const { width: t, height: n, x: o, y: i } = po(e.nodeLookup, {
    filter: (s) => !!s.selected
  });
  return {
    width: et(t) ? t : null,
    height: et(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${i}px)`
  };
};
function Rm({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = je(), { width: i, height: s, transformString: a, userSelectionActive: c } = he(zm, Ne), u = Gl(), l = re(null);
  te(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !c && i !== null && s !== null;
  if (Zl({
    nodeRef: l,
    disabled: !d
  }), !d)
    return null;
  const f = e ? (p) => {
    const g = o.getState().nodes.filter((v) => v.selected);
    e(p, g);
  } : void 0, h = (p) => {
    Object.prototype.hasOwnProperty.call(ci, p.key) && (p.preventDefault(), u({
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
const Oa = typeof window < "u" ? window : void 0, Lm = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function Ql({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: i, onPaneContextMenu: s, onPaneScroll: a, paneClickDistance: c, deleteKeyCode: u, selectionKeyCode: l, selectionOnDrag: d, selectionMode: f, onSelectionStart: h, onSelectionEnd: p, multiSelectionKeyCode: g, panActivationKeyCode: v, zoomActivationKeyCode: w, elementsSelectable: m, zoomOnScroll: k, zoomOnPinch: y, panOnScroll: b, panOnScrollSpeed: S, panOnScrollMode: E, zoomOnDoubleClick: j, panOnDrag: _, autoPanOnSelection: M, defaultViewport: F, translateExtent: D, minZoom: T, maxZoom: L, preventScrolling: N, onSelectionContextMenu: A, noWheelClassName: I, noPanClassName: $, disableKeyboardA11y: z, onViewportChange: P, isControlledViewport: B }) {
  const { nodesSelectionActive: K, userSelectionActive: O } = he(Lm, Ne), Z = ro(l, { target: Oa }), X = ro(v, { target: Oa }), oe = X || _, se = X || b, G = d && oe !== !0, V = Z || O || G;
  return xm({ deleteKeyCode: u, multiSelectionKeyCode: g }), r.jsx(bm, { onPaneContextMenu: s, elementsSelectable: m, zoomOnScroll: k, zoomOnPinch: y, panOnScroll: se, panOnScrollSpeed: S, panOnScrollMode: E, zoomOnDoubleClick: j, panOnDrag: !Z && oe, defaultViewport: F, translateExtent: D, minZoom: T, maxZoom: L, zoomActivationKeyCode: w, preventScrolling: N, noWheelClassName: I, noPanClassName: $, onViewportChange: P, isControlledViewport: B, paneClickDistance: c, selectionOnDrag: G, children: r.jsxs(km, { onSelectionStart: h, onSelectionEnd: p, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: i, onPaneContextMenu: s, onPaneScroll: a, panOnDrag: oe, autoPanOnSelection: M, isSelecting: !!V, selectionMode: f, selectionKeyPressed: Z, paneClickDistance: c, selectionOnDrag: G, children: [e, K && r.jsx(Rm, { onSelectionContextMenu: A, noPanClassName: $, disableKeyboardA11y: z })] }) });
}
Ql.displayName = "FlowRenderer";
const Vm = ke(Ql), Om = (e) => (t) => e ? Jr(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function Hm(e) {
  return he(fe(Om(e), [e]), Ne);
}
const Bm = (e) => e.updateNodeInternals;
function Fm() {
  const e = he(Bm), [t] = q(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
  return te(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function Wm({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const i = je(), s = re(null), a = re(null), c = re(e.sourcePosition), u = re(e.targetPosition), l = re(t), d = n && !!e.internals.handleBounds;
  return te(() => {
    s.current && !e.hidden && (!d || a.current !== s.current) && (a.current && o?.unobserve(a.current), o?.observe(s.current), a.current = s.current);
  }, [d, e.hidden]), te(() => () => {
    a.current && (o?.unobserve(a.current), a.current = null);
  }, []), te(() => {
    if (s.current) {
      const f = l.current !== t, h = c.current !== e.sourcePosition, p = u.current !== e.targetPosition;
      (f || h || p) && (l.current = t, c.current = e.sourcePosition, u.current = e.targetPosition, i.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: s.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), s;
}
function Km({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: i, onContextMenu: s, onDoubleClick: a, nodesDraggable: c, elementsSelectable: u, nodesConnectable: l, nodesFocusable: d, resizeObserver: f, noDragClassName: h, noPanClassName: p, disableKeyboardA11y: g, rfId: v, nodeTypes: w, nodeClickDistance: m, onError: k }) {
  const { node: y, internals: b, isParent: S } = he((V) => {
    const U = V.nodeLookup.get(e), ae = V.parentLookup.has(e);
    return {
      node: U,
      internals: U.internals,
      isParent: ae
    };
  }, Ne);
  let E = y.type || "default", j = w?.[E] || Va[E];
  j === void 0 && (k?.("003", Ke.error003(E)), E = "default", j = w?.default || Va.default);
  const _ = !!(y.draggable || c && typeof y.draggable > "u"), M = !!(y.selectable || u && typeof y.selectable > "u"), F = !!(y.connectable || l && typeof y.connectable > "u"), D = !!(y.focusable || d && typeof y.focusable > "u"), T = je(), L = ml(y), N = Wm({ node: y, nodeType: E, hasDimensions: L, resizeObserver: f }), A = Zl({
    nodeRef: N,
    disabled: y.hidden || !_,
    noDragClassName: h,
    handleSelector: y.dragHandle,
    nodeId: e,
    isSelectable: M,
    nodeClickDistance: m
  }), I = Gl();
  if (y.hidden)
    return null;
  const $ = yt(y), z = Pm(y), P = M || _ || t || n || o || i, B = n ? (V) => n(V, { ...b.userNode }) : void 0, K = o ? (V) => o(V, { ...b.userNode }) : void 0, O = i ? (V) => i(V, { ...b.userNode }) : void 0, Z = s ? (V) => s(V, { ...b.userNode }) : void 0, X = a ? (V) => a(V, { ...b.userNode }) : void 0, oe = (V) => {
    const { selectNodesOnDrag: U, nodeDragThreshold: ae } = T.getState();
    M && (!U || !_ || ae > 0) && $r({
      id: e,
      store: T,
      nodeRef: N
    }), t && t(V, { ...b.userNode });
  }, se = (V) => {
    if (!(vl(V.nativeEvent) || g)) {
      if (cl.includes(V.key) && M) {
        const U = V.key === "Escape";
        $r({
          id: e,
          store: T,
          unselect: U,
          nodeRef: N
        });
      } else if (_ && y.selected && Object.prototype.hasOwnProperty.call(ci, V.key)) {
        V.preventDefault();
        const { ariaLabelConfig: U } = T.getState();
        T.setState({
          ariaLiveMessage: U["node.a11yDescription.ariaLiveMessage"]({
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
    const { transform: V, width: U, height: ae, autoPanOnNodeFocus: ce, setCenter: J } = T.getState();
    if (!ce)
      return;
    Jr(/* @__PURE__ */ new Map([[e, y]]), { x: 0, y: 0, width: U, height: ae }, V, !0).length > 0 || J(y.position.x + $.width / 2, y.position.y + $.height / 2, {
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
      selectable: M,
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
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: B, onMouseMove: K, onMouseLeave: O, onContextMenu: Z, onClick: oe, onDoubleClick: X, onKeyDown: D ? se : void 0, tabIndex: D ? 0 : void 0, onFocus: D ? G : void 0, role: y.ariaRole ?? (D ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": g ? void 0 : `${Ol}-${v}`, "aria-label": y.ariaLabel, ...y.domAttributes, children: r.jsx(Cm, { value: e, children: r.jsx(j, { id: e, data: y.data, type: E, positionAbsoluteX: b.positionAbsolute.x, positionAbsoluteY: b.positionAbsolute.y, selected: y.selected ?? !1, selectable: M, draggable: _, deletable: y.deletable ?? !0, isConnectable: F, sourcePosition: y.sourcePosition, targetPosition: y.targetPosition, dragging: A, dragHandle: y.dragHandle, zIndex: b.z, parentId: y.parentId, ...$ }) }) });
}
var qm = ke(Km);
const Xm = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function eu(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: i, onError: s } = he(Xm, Ne), a = Hm(e.onlyRenderVisibleElements), c = Fm();
  return r.jsx("div", { className: "react-flow__nodes", style: Di, children: a.map((u) => (
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
    r.jsx(qm, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: i, nodeClickDistance: e.nodeClickDistance, onError: s }, u)
  )) });
}
eu.displayName = "NodeRenderer";
const Um = ke(eu);
function Ym(e) {
  return he(fe((n) => {
    if (!e)
      return n.edges.map((i) => i.id);
    const o = [];
    if (n.width && n.height)
      for (const i of n.edges) {
        const s = n.nodeLookup.get(i.source), a = n.nodeLookup.get(i.target);
        s && a && Wg({
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
const Zm = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return r.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, Gm = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return r.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Ha = {
  [ri.Arrow]: Zm,
  [ri.ArrowClosed]: Gm
};
function Jm(e) {
  const t = je();
  return ue(() => Object.prototype.hasOwnProperty.call(Ha, e) ? Ha[e] : (t.getState().onError?.("009", Ke.error009(e)), null), [e]);
}
const Qm = ({ id: e, type: t, color: n, width: o = 12.5, height: i = 12.5, markerUnits: s = "strokeWidth", strokeWidth: a, orient: c = "auto-start-reverse" }) => {
  const u = Jm(t);
  return u ? r.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${i}`, viewBox: "-10 -10 20 20", markerUnits: s, orient: c, refX: "0", refY: "0", children: r.jsx(u, { color: n, strokeWidth: a }) }) : null;
}, tu = ({ defaultColor: e, rfId: t }) => {
  const n = he((s) => s.edges), o = he((s) => s.defaultEdgeOptions), i = ue(() => Jg(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return i.length ? r.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: r.jsx("defs", { children: i.map((s) => r.jsx(Qm, { id: s.id, type: s.type, color: s.color, width: s.width, height: s.height, markerUnits: s.markerUnits, strokeWidth: s.strokeWidth, orient: s.orient }, s.id)) }) }) : null;
};
tu.displayName = "MarkerDefinitions";
var ex = ke(tu);
function nu({ x: e, y: t, label: n, labelStyle: o, labelShowBg: i = !0, labelBgStyle: s, labelBgPadding: a = [2, 4], labelBgBorderRadius: c = 2, children: u, className: l, ...d }) {
  const [f, h] = q({ x: 1, y: 0, width: 0, height: 0 }), p = Ce(["react-flow__edge-textwrapper", l]), g = re(null);
  return te(() => {
    if (g.current) {
      const v = g.current.getBBox();
      h({
        x: v.x,
        y: v.y,
        width: v.width,
        height: v.height
      });
    }
  }, [n]), n ? r.jsxs("g", { transform: `translate(${e - f.width / 2} ${t - f.height / 2})`, className: p, visibility: f.width ? "visible" : "hidden", ...d, children: [i && r.jsx("rect", { width: f.width + 2 * a[0], x: -a[0], y: -a[1], height: f.height + 2 * a[1], className: "react-flow__edge-textbg", style: s, rx: c, ry: c }), r.jsx("text", { className: "react-flow__edge-text", y: f.height / 2, dy: "0.3em", ref: g, style: o, children: n }), u] }) : null;
}
nu.displayName = "EdgeText";
const tx = ke(nu);
function yo({ path: e, labelX: t, labelY: n, label: o, labelStyle: i, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u, interactionWidth: l = 20, ...d }) {
  return r.jsxs(r.Fragment, { children: [r.jsx("path", { ...d, d: e, fill: "none", className: Ce(["react-flow__edge-path", d.className]) }), l ? r.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, o && et(t) && et(n) ? r.jsx(tx, { x: t, y: n, label: o, labelStyle: i, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u }) : null] });
}
function Ba({ pos: e, x1: t, y1: n, x2: o, y2: i }) {
  return e === ne.Left || e === ne.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + i)];
}
function ou({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: i, targetPosition: s = ne.Top }) {
  const [a, c] = Ba({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: i
  }), [u, l] = Ba({
    pos: s,
    x1: o,
    y1: i,
    x2: e,
    y2: t
  }), [d, f, h, p] = Nl({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: i,
    sourceControlX: a,
    sourceControlY: c,
    targetControlX: u,
    targetControlY: l
  });
  return [
    `M${e},${t} C${a},${c} ${u},${l} ${o},${i}`,
    d,
    f,
    h,
    p
  ];
}
function iu(e) {
  return ke(({ id: t, sourceX: n, sourceY: o, targetX: i, targetY: s, sourcePosition: a, targetPosition: c, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: g, markerEnd: v, markerStart: w, interactionWidth: m }) => {
    const [k, y, b] = ou({
      sourceX: n,
      sourceY: o,
      sourcePosition: a,
      targetX: i,
      targetY: s,
      targetPosition: c
    }), S = e.isInternal ? void 0 : t;
    return r.jsx(yo, { id: S, path: k, labelX: y, labelY: b, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: g, markerEnd: v, markerStart: w, interactionWidth: m });
  });
}
const nx = iu({ isInternal: !1 }), ru = iu({ isInternal: !0 });
nx.displayName = "SimpleBezierEdge";
ru.displayName = "SimpleBezierEdgeInternal";
function su(e) {
  return ke(({ id: t, sourceX: n, sourceY: o, targetX: i, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: h, sourcePosition: p = ne.Bottom, targetPosition: g = ne.Top, markerEnd: v, markerStart: w, pathOptions: m, interactionWidth: k }) => {
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
    return r.jsx(yo, { id: E, path: y, labelX: b, labelY: S, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: v, markerStart: w, interactionWidth: k });
  });
}
const au = su({ isInternal: !1 }), cu = su({ isInternal: !0 });
au.displayName = "SmoothStepEdge";
cu.displayName = "SmoothStepEdgeInternal";
function lu(e) {
  return ke(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return r.jsx(au, { ...n, id: o, pathOptions: ue(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const ox = lu({ isInternal: !1 }), uu = lu({ isInternal: !0 });
ox.displayName = "StepEdge";
uu.displayName = "StepEdgeInternal";
function du(e) {
  return ke(({ id: t, sourceX: n, sourceY: o, targetX: i, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: p, markerStart: g, interactionWidth: v }) => {
    const [w, m, k] = El({ sourceX: n, sourceY: o, targetX: i, targetY: s }), y = e.isInternal ? void 0 : t;
    return r.jsx(yo, { id: y, path: w, labelX: m, labelY: k, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: p, markerStart: g, interactionWidth: v });
  });
}
const ix = du({ isInternal: !1 }), fu = du({ isInternal: !0 });
ix.displayName = "StraightEdge";
fu.displayName = "StraightEdgeInternal";
function hu(e) {
  return ke(({ id: t, sourceX: n, sourceY: o, targetX: i, targetY: s, sourcePosition: a = ne.Bottom, targetPosition: c = ne.Top, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: g, markerEnd: v, markerStart: w, pathOptions: m, interactionWidth: k }) => {
    const [y, b, S] = jl({
      sourceX: n,
      sourceY: o,
      sourcePosition: a,
      targetX: i,
      targetY: s,
      targetPosition: c,
      curvature: m?.curvature
    }), E = e.isInternal ? void 0 : t;
    return r.jsx(yo, { id: E, path: y, labelX: b, labelY: S, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: g, markerEnd: v, markerStart: w, interactionWidth: k });
  });
}
const rx = hu({ isInternal: !1 }), pu = hu({ isInternal: !0 });
rx.displayName = "BezierEdge";
pu.displayName = "BezierEdgeInternal";
const Fa = {
  default: pu,
  straight: fu,
  step: uu,
  smoothstep: cu,
  simplebezier: ru
}, Wa = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, sx = (e, t, n) => n === ne.Left ? e - t : n === ne.Right ? e + t : e, ax = (e, t, n) => n === ne.Top ? e - t : n === ne.Bottom ? e + t : e, Ka = "react-flow__edgeupdater";
function qa({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: i, onMouseEnter: s, onMouseOut: a, type: c }) {
  return r.jsx("circle", { onMouseDown: i, onMouseEnter: s, onMouseOut: a, className: Ce([Ka, `${Ka}-${c}`]), cx: sx(t, o, e), cy: ax(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function cx({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: i, targetX: s, targetY: a, sourcePosition: c, targetPosition: u, onReconnect: l, onReconnectStart: d, onReconnectEnd: f, setReconnecting: h, setUpdateHover: p }) {
  const g = je(), v = (b, S) => {
    if (b.button !== 0)
      return;
    const { autoPanOnConnect: E, domNode: j, connectionMode: _, connectionRadius: M, lib: F, onConnectStart: D, cancelConnection: T, nodeLookup: L, rfId: N, panBy: A, updateConnection: I } = g.getState(), $ = S.type === "target", z = (K, O) => {
      h(!1), f?.(K, n, S.type, O);
    }, P = (K) => l?.(n, K), B = (K, O) => {
      h(!0), d?.(b, n, S.type), D?.(K, O);
    };
    Tr.onPointerDown(b.nativeEvent, {
      autoPanOnConnect: E,
      connectionMode: _,
      connectionRadius: M,
      domNode: j,
      handleId: S.id,
      nodeId: S.nodeId,
      nodeLookup: L,
      isTarget: $,
      edgeUpdaterType: S.type,
      lib: F,
      flowId: N,
      cancelConnection: T,
      panBy: A,
      isValidConnection: (...K) => g.getState().isValidConnection?.(...K) ?? !0,
      onConnect: P,
      onConnectStart: B,
      onConnectEnd: (...K) => g.getState().onConnectEnd?.(...K),
      onReconnectEnd: z,
      updateConnection: I,
      getTransform: () => g.getState().transform,
      getFromHandle: () => g.getState().connection.fromHandle,
      dragThreshold: g.getState().connectionDragThreshold,
      handleDomNode: b.currentTarget
    });
  }, w = (b) => v(b, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), m = (b) => v(b, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), k = () => p(!0), y = () => p(!1);
  return r.jsxs(r.Fragment, { children: [(e === !0 || e === "source") && r.jsx(qa, { position: c, centerX: o, centerY: i, radius: t, onMouseDown: w, onMouseEnter: k, onMouseOut: y, type: "source" }), (e === !0 || e === "target") && r.jsx(qa, { position: u, centerX: s, centerY: a, radius: t, onMouseDown: m, onMouseEnter: k, onMouseOut: y, type: "target" })] });
}
function lx({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: i, onDoubleClick: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, reconnectRadius: d, onReconnect: f, onReconnectStart: h, onReconnectEnd: p, rfId: g, edgeTypes: v, noPanClassName: w, onError: m, disableKeyboardA11y: k }) {
  let y = he((J) => J.edgeLookup.get(e));
  const b = he((J) => J.defaultEdgeOptions);
  y = b ? { ...b, ...y } : y;
  let S = y.type || "default", E = v?.[S] || Fa[S];
  E === void 0 && (m?.("011", Ke.error011(S)), S = "default", E = v?.default || Fa.default);
  const j = !!(y.focusable || t && typeof y.focusable > "u"), _ = typeof f < "u" && (y.reconnectable || n && typeof y.reconnectable > "u"), M = !!(y.selectable || o && typeof y.selectable > "u"), F = re(null), [D, T] = q(!1), [L, N] = q(!1), A = je(), { zIndex: I, sourceX: $, sourceY: z, targetX: P, targetY: B, sourcePosition: K, targetPosition: O } = he(fe((J) => {
    const ee = J.nodeLookup.get(y.source), de = J.nodeLookup.get(y.target);
    if (!ee || !de)
      return {
        zIndex: y.zIndex,
        ...Wa
      };
    const H = Gg({
      id: e,
      sourceNode: ee,
      targetNode: de,
      sourceHandle: y.sourceHandle || null,
      targetHandle: y.targetHandle || null,
      connectionMode: J.connectionMode,
      onError: m
    });
    return {
      zIndex: Fg({
        selected: y.selected,
        zIndex: y.zIndex,
        sourceNode: ee,
        targetNode: de,
        elevateOnSelect: J.elevateEdgesOnSelect,
        zIndexMode: J.zIndexMode
      }),
      ...H || Wa
    };
  }, [y.source, y.target, y.sourceHandle, y.targetHandle, y.selected, y.zIndex]), Ne), Z = ue(() => y.markerStart ? `url('#${Dr(y.markerStart, g)}')` : void 0, [y.markerStart, g]), X = ue(() => y.markerEnd ? `url('#${Dr(y.markerEnd, g)}')` : void 0, [y.markerEnd, g]);
  if (y.hidden || $ === null || z === null || P === null || B === null)
    return null;
  const oe = (J) => {
    const { addSelectedEdges: ee, unselectNodesAndEdges: de, multiSelectionActive: H } = A.getState();
    M && (A.setState({ nodesSelectionActive: !1 }), y.selected && H ? (de({ nodes: [], edges: [y] }), F.current?.blur()) : ee([e])), i && i(J, y);
  }, se = s ? (J) => {
    s(J, { ...y });
  } : void 0, G = a ? (J) => {
    a(J, { ...y });
  } : void 0, V = c ? (J) => {
    c(J, { ...y });
  } : void 0, U = u ? (J) => {
    u(J, { ...y });
  } : void 0, ae = l ? (J) => {
    l(J, { ...y });
  } : void 0, ce = (J) => {
    if (!k && cl.includes(J.key) && M) {
      const { unselectNodesAndEdges: ee, addSelectedEdges: de } = A.getState();
      J.key === "Escape" ? (F.current?.blur(), ee({ edges: [y] })) : de([e]);
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
      inactive: !M && !i,
      updating: D,
      selectable: M
    }
  ]), onClick: oe, onDoubleClick: se, onContextMenu: G, onMouseEnter: V, onMouseMove: U, onMouseLeave: ae, onKeyDown: j ? ce : void 0, tabIndex: j ? 0 : void 0, role: y.ariaRole ?? (j ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": y.ariaLabel === null ? void 0 : y.ariaLabel || `Edge from ${y.source} to ${y.target}`, "aria-describedby": j ? `${Hl}-${g}` : void 0, ref: F, ...y.domAttributes, children: [!L && r.jsx(E, { id: e, source: y.source, target: y.target, type: y.type, selected: y.selected, animated: y.animated, selectable: M, deletable: y.deletable ?? !0, label: y.label, labelStyle: y.labelStyle, labelShowBg: y.labelShowBg, labelBgStyle: y.labelBgStyle, labelBgPadding: y.labelBgPadding, labelBgBorderRadius: y.labelBgBorderRadius, sourceX: $, sourceY: z, targetX: P, targetY: B, sourcePosition: K, targetPosition: O, data: y.data, style: y.style, sourceHandleId: y.sourceHandle, targetHandleId: y.targetHandle, markerStart: Z, markerEnd: X, pathOptions: "pathOptions" in y ? y.pathOptions : void 0, interactionWidth: y.interactionWidth }), _ && r.jsx(cx, { edge: y, isReconnectable: _, reconnectRadius: d, onReconnect: f, onReconnectStart: h, onReconnectEnd: p, sourceX: $, sourceY: z, targetX: P, targetY: B, sourcePosition: K, targetPosition: O, setUpdateHover: T, setReconnecting: N })] }) });
}
var ux = ke(lx);
const dx = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function gu({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: i, onReconnect: s, onEdgeContextMenu: a, onEdgeMouseEnter: c, onEdgeMouseMove: u, onEdgeMouseLeave: l, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: h, onReconnectStart: p, onReconnectEnd: g, disableKeyboardA11y: v }) {
  const { edgesFocusable: w, edgesReconnectable: m, elementsSelectable: k, onError: y } = he(dx, Ne), b = Ym(t);
  return r.jsxs("div", { className: "react-flow__edges", children: [r.jsx(ex, { defaultColor: e, rfId: n }), b.map((S) => r.jsx(ux, { id: S, edgesFocusable: w, edgesReconnectable: m, elementsSelectable: k, noPanClassName: i, onReconnect: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, onClick: d, reconnectRadius: f, onDoubleClick: h, onReconnectStart: p, onReconnectEnd: g, rfId: n, onError: y, edgeTypes: o, disableKeyboardA11y: v }, S))] });
}
gu.displayName = "EdgeRenderer";
const fx = ke(gu), hx = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function px({ children: e }) {
  const t = he(hx);
  return r.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function gx(e) {
  const t = as(), n = re(!1);
  te(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const yx = (e) => e.panZoom?.syncViewport;
function mx(e) {
  const t = he(yx), n = je();
  return te(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function xx(e) {
  return e.connection.inProgress ? { ...e.connection, to: mn(e.connection.to, e.transform) } : { ...e.connection };
}
function wx(e) {
  return xx;
}
function vx(e) {
  const t = wx();
  return he(t, Ne);
}
const bx = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function Nx({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: i, width: s, height: a, isValid: c, inProgress: u } = he(bx, Ne);
  return !(s && i && u) ? null : r.jsx("svg", { style: e, width: s, height: a, className: "react-flow__connectionline react-flow__container", children: r.jsx("g", { className: Ce(["react-flow__connection", dl(c)]), children: r.jsx(yu, { style: t, type: n, CustomComponent: o, isValid: c }) }) });
}
const yu = ({ style: e, type: t = bt.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: i, from: s, fromNode: a, fromHandle: c, fromPosition: u, to: l, toNode: d, toHandle: f, toPosition: h, pointer: p } = vx();
  if (!i)
    return;
  if (n)
    return r.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: a, fromHandle: c, fromX: s.x, fromY: s.y, toX: l.x, toY: l.y, fromPosition: u, toPosition: h, connectionStatus: dl(o), toNode: d, toHandle: f, pointer: p });
  let g = "";
  const v = {
    sourceX: s.x,
    sourceY: s.y,
    sourcePosition: u,
    targetX: l.x,
    targetY: l.y,
    targetPosition: h
  };
  switch (t) {
    case bt.Bezier:
      [g] = jl(v);
      break;
    case bt.SimpleBezier:
      [g] = ou(v);
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
      [g] = El(v);
  }
  return r.jsx("path", { d: g, fill: "none", className: "react-flow__connection-path", style: e });
};
yu.displayName = "ConnectionLine";
const jx = {};
function Xa(e = jx) {
  re(e), je(), te(() => {
  }, [e]);
}
function Sx() {
  je(), re(!1), te(() => {
  }, []);
}
function mu({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: i, onNodeDoubleClick: s, onEdgeDoubleClick: a, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: h, onSelectionEnd: p, connectionLineType: g, connectionLineStyle: v, connectionLineComponent: w, connectionLineContainerStyle: m, selectionKeyCode: k, selectionOnDrag: y, selectionMode: b, multiSelectionKeyCode: S, panActivationKeyCode: E, zoomActivationKeyCode: j, deleteKeyCode: _, onlyRenderVisibleElements: M, elementsSelectable: F, defaultViewport: D, translateExtent: T, minZoom: L, maxZoom: N, preventScrolling: A, defaultMarkerColor: I, zoomOnScroll: $, zoomOnPinch: z, panOnScroll: P, panOnScrollSpeed: B, panOnScrollMode: K, zoomOnDoubleClick: O, panOnDrag: Z, autoPanOnSelection: X, onPaneClick: oe, onPaneMouseEnter: se, onPaneMouseMove: G, onPaneMouseLeave: V, onPaneScroll: U, onPaneContextMenu: ae, paneClickDistance: ce, nodeClickDistance: J, onEdgeContextMenu: ee, onEdgeMouseEnter: de, onEdgeMouseMove: H, onEdgeMouseLeave: Q, reconnectRadius: ge, onReconnect: ve, onReconnectStart: Ee, onReconnectEnd: $e, noDragClassName: Ie, noWheelClassName: qe, noPanClassName: be, disableKeyboardA11y: Re, nodeExtent: Le, rfId: Ye, viewport: Ze, onViewportChange: Ge }) {
  return Xa(e), Xa(t), Sx(), gx(n), mx(Ze), r.jsx(Vm, { onPaneClick: oe, onPaneMouseEnter: se, onPaneMouseMove: G, onPaneMouseLeave: V, onPaneContextMenu: ae, onPaneScroll: U, paneClickDistance: ce, deleteKeyCode: _, selectionKeyCode: k, selectionOnDrag: y, selectionMode: b, onSelectionStart: h, onSelectionEnd: p, multiSelectionKeyCode: S, panActivationKeyCode: E, zoomActivationKeyCode: j, elementsSelectable: F, zoomOnScroll: $, zoomOnPinch: z, zoomOnDoubleClick: O, panOnScroll: P, panOnScrollSpeed: B, panOnScrollMode: K, panOnDrag: Z, autoPanOnSelection: X, defaultViewport: D, translateExtent: T, minZoom: L, maxZoom: N, onSelectionContextMenu: f, preventScrolling: A, noDragClassName: Ie, noWheelClassName: qe, noPanClassName: be, disableKeyboardA11y: Re, onViewportChange: Ge, isControlledViewport: !!Ze, children: r.jsxs(px, { children: [r.jsx(fx, { edgeTypes: t, onEdgeClick: i, onEdgeDoubleClick: a, onReconnect: ve, onReconnectStart: Ee, onReconnectEnd: $e, onlyRenderVisibleElements: M, onEdgeContextMenu: ee, onEdgeMouseEnter: de, onEdgeMouseMove: H, onEdgeMouseLeave: Q, reconnectRadius: ge, defaultMarkerColor: I, noPanClassName: be, disableKeyboardA11y: Re, rfId: Ye }), r.jsx(Nx, { style: v, type: g, component: w, containerStyle: m }), r.jsx("div", { className: "react-flow__edgelabel-renderer" }), r.jsx(Um, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, nodeClickDistance: J, onlyRenderVisibleElements: M, noPanClassName: be, noDragClassName: Ie, disableKeyboardA11y: Re, nodeExtent: Le, rfId: Ye }), r.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
mu.displayName = "GraphView";
const kx = ke(mu), Ex = yl(), Ua = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: i, height: s, fitView: a, fitViewOptions: c, minZoom: u = 0.5, maxZoom: l = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: h = "basic" } = {}) => {
  const p = /* @__PURE__ */ new Map(), g = /* @__PURE__ */ new Map(), v = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), m = o ?? t ?? [], k = n ?? e ?? [], y = d ?? [0, 0], b = f ?? to;
  Al(v, w, m);
  const { nodesInitialized: S } = Mr(k, p, g, {
    nodeOrigin: y,
    nodeExtent: b,
    zIndexMode: h
  });
  let E = [0, 0, 1];
  if (a && i && s) {
    const j = po(p, {
      filter: (D) => !!((D.width || D.initialWidth) && (D.height || D.initialHeight))
    }), { x: _, y: M, zoom: F } = es(j, i, s, u, l, c?.padding ?? 0.1);
    E = [_, M, F];
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
    minZoom: u,
    maxZoom: l,
    translateExtent: to,
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
    connection: { ...ul },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: Ex,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: ll,
    zIndexMode: h,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, Cx = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: i, height: s, fitView: a, fitViewOptions: c, minZoom: u, maxZoom: l, nodeOrigin: d, nodeExtent: f, zIndexMode: h }) => Oy((p, g) => {
  async function v() {
    const { nodeLookup: w, panZoom: m, fitViewOptions: k, fitViewResolver: y, width: b, height: S, minZoom: E, maxZoom: j } = g();
    m && (await zg({
      nodes: w,
      width: b,
      height: S,
      panZoom: m,
      minZoom: E,
      maxZoom: j
    }, k), y?.resolve(!0), p({ fitViewResolver: null }));
  }
  return {
    ...Ua({
      nodes: e,
      edges: t,
      width: i,
      height: s,
      fitView: a,
      fitViewOptions: c,
      minZoom: u,
      maxZoom: l,
      nodeOrigin: d,
      nodeExtent: f,
      defaultNodes: n,
      defaultEdges: o,
      zIndexMode: h
    }),
    setNodes: (w) => {
      const { nodeLookup: m, parentLookup: k, nodeOrigin: y, elevateNodesOnSelect: b, fitViewQueued: S, zIndexMode: E, nodesSelectionActive: j } = g(), { nodesInitialized: _, hasSelectedNodes: M } = Mr(w, m, k, {
        nodeOrigin: y,
        nodeExtent: f,
        elevateNodesOnSelect: b,
        checkEquality: !0,
        zIndexMode: E
      }), F = j && M;
      S && _ ? (v(), p({
        nodes: w,
        nodesInitialized: _,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: F
      })) : p({ nodes: w, nodesInitialized: _, nodesSelectionActive: F });
    },
    setEdges: (w) => {
      const { connectionLookup: m, edgeLookup: k } = g();
      Al(m, k, w), p({ edges: w });
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
      const { triggerNodeChanges: m, nodeLookup: k, parentLookup: y, domNode: b, nodeOrigin: S, nodeExtent: E, debug: j, fitViewQueued: _, zIndexMode: M } = g(), { changes: F, updatedInternals: D } = ry(w, k, y, b, S, E, M);
      D && (ty(k, y, { nodeOrigin: S, nodeExtent: E, zIndexMode: M }), _ ? (v(), p({ fitViewQueued: !1, fitViewOptions: void 0 })) : p({}), F?.length > 0 && (j && console.log("React Flow: trigger node changes", F), m?.(F)));
    },
    updateNodePositions: (w, m = !1) => {
      const k = [];
      let y = [];
      const { nodeLookup: b, triggerNodeChanges: S, connection: E, updateConnection: j, onNodesChangeMiddlewareMap: _ } = g();
      for (const [M, F] of w) {
        const D = b.get(M), T = !!(D?.expandParent && D?.parentId && F?.position), L = {
          id: M,
          type: "position",
          position: T ? {
            x: Math.max(0, F.position.x),
            y: Math.max(0, F.position.y)
          } : F.position,
          dragging: m
        };
        if (D && E.inProgress && E.fromNode.id === D.id) {
          const N = Bt(D, E.fromHandle, ne.Left, !0);
          j({ ...E, from: N });
        }
        T && D.parentId && k.push({
          id: M,
          parentId: D.parentId,
          rect: {
            ...F.internals.positionAbsolute,
            width: F.measured.width ?? 0,
            height: F.measured.height ?? 0
          }
        }), y.push(L);
      }
      if (k.length > 0) {
        const { parentLookup: M, nodeOrigin: F } = g(), D = ss(k, b, M, F);
        y.push(...D);
      }
      for (const M of _.values())
        y = M(y);
      S(y);
    },
    triggerNodeChanges: (w) => {
      const { onNodesChange: m, setNodes: k, nodes: y, hasDefaultNodes: b, debug: S } = g();
      if (w?.length) {
        if (b) {
          const E = Wl(w, y);
          k(E);
        }
        S && console.log("React Flow: trigger node changes", w), m?.(w);
      }
    },
    triggerEdgeChanges: (w) => {
      const { onEdgesChange: m, setEdges: k, edges: y, hasDefaultEdges: b, debug: S } = g();
      if (w?.length) {
        if (b) {
          const E = Kl(w, y);
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
      const { edges: k, nodes: y, nodeLookup: b, triggerNodeChanges: S, triggerEdgeChanges: E } = g(), j = w || y, _ = m || k, M = [];
      for (const D of j) {
        if (!D.selected)
          continue;
        const T = b.get(D.id);
        T && (T.selected = !1), M.push(Mt(D.id, !1));
      }
      const F = [];
      for (const D of _)
        D.selected && F.push(Mt(D.id, !1));
      S(M), E(F);
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
      return sy({ delta: w, panZoom: b, transform: m, translateExtent: S, width: k, height: y });
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
        connection: { ...ul }
      });
    },
    updateConnection: (w) => {
      p({ connection: w });
    },
    reset: () => p({ ...Ua() })
  };
}, Object.is);
function Ix({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: i, initialHeight: s, initialMinZoom: a, initialMaxZoom: c, initialFitViewOptions: u, fitView: l, nodeOrigin: d, nodeExtent: f, zIndexMode: h, children: p }) {
  const [g] = q(() => Cx({
    nodes: e,
    edges: t,
    defaultNodes: n,
    defaultEdges: o,
    width: i,
    height: s,
    fitView: l,
    minZoom: a,
    maxZoom: c,
    fitViewOptions: u,
    nodeOrigin: d,
    nodeExtent: f,
    zIndexMode: h
  }));
  return r.jsx(Wy, { value: g, children: r.jsx(pm, { children: p }) });
}
function Ax({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: i, width: s, height: a, fitView: c, fitViewOptions: u, minZoom: l, maxZoom: d, nodeOrigin: f, nodeExtent: h, zIndexMode: p }) {
  return lo(Ai) ? r.jsx(r.Fragment, { children: e }) : r.jsx(Ix, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: i, initialWidth: s, initialHeight: a, fitView: c, initialFitViewOptions: u, initialMinZoom: l, initialMaxZoom: d, nodeOrigin: f, nodeExtent: h, zIndexMode: p, children: e });
}
const _x = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function Dx({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: i, nodeTypes: s, edgeTypes: a, onNodeClick: c, onEdgeClick: u, onInit: l, onMove: d, onMoveStart: f, onMoveEnd: h, onConnect: p, onConnectStart: g, onConnectEnd: v, onClickConnectStart: w, onClickConnectEnd: m, onNodeMouseEnter: k, onNodeMouseMove: y, onNodeMouseLeave: b, onNodeContextMenu: S, onNodeDoubleClick: E, onNodeDragStart: j, onNodeDrag: _, onNodeDragStop: M, onNodesDelete: F, onEdgesDelete: D, onDelete: T, onSelectionChange: L, onSelectionDragStart: N, onSelectionDrag: A, onSelectionDragStop: I, onSelectionContextMenu: $, onSelectionStart: z, onSelectionEnd: P, onBeforeDelete: B, connectionMode: K, connectionLineType: O = bt.Bezier, connectionLineStyle: Z, connectionLineComponent: X, connectionLineContainerStyle: oe, deleteKeyCode: se = "Backspace", selectionKeyCode: G = "Shift", selectionOnDrag: V = !1, selectionMode: U = no.Full, panActivationKeyCode: ae = "Space", multiSelectionKeyCode: ce = io() ? "Meta" : "Control", zoomActivationKeyCode: J = io() ? "Meta" : "Control", snapToGrid: ee, snapGrid: de, onlyRenderVisibleElements: H = !1, selectNodesOnDrag: Q, nodesDraggable: ge, autoPanOnNodeFocus: ve, nodesConnectable: Ee, nodesFocusable: $e, nodeOrigin: Ie = Bl, edgesFocusable: qe, edgesReconnectable: be, elementsSelectable: Re = !0, defaultViewport: Le = om, minZoom: Ye = 0.5, maxZoom: Ze = 2, translateExtent: Ge = to, preventScrolling: Et = !0, nodeExtent: wn, defaultMarkerColor: Xe = "#b1b1b7", zoomOnScroll: Kt = !0, zoomOnPinch: vn = !0, panOnScroll: bn = !1, panOnScrollSpeed: vo = 0.5, panOnScrollMode: Nn = Pt.Free, zoomOnDoubleClick: Ve = !0, panOnDrag: ut = !0, onPaneClick: dt, onPaneMouseEnter: qt, onPaneMouseMove: Oi, onPaneMouseLeave: jn, onPaneScroll: Xt, onPaneContextMenu: mt, paneClickDistance: Ct = 1, nodeClickDistance: It = 0, children: Ue, onReconnect: Sn, onReconnectStart: bo, onReconnectEnd: Oe, onEdgeContextMenu: No, onEdgeDoubleClick: we, onEdgeMouseEnter: He, onEdgeMouseMove: Ut, onEdgeMouseLeave: jo, reconnectRadius: me = 10, onNodesChange: At, onEdgesChange: So, noDragClassName: kn = "nodrag", noWheelClassName: _e = "nowheel", noPanClassName: xt = "nopan", fitView: ft, fitViewOptions: ko, connectOnClick: Eo, attributionPosition: Co, proOptions: En, defaultEdgeOptions: Hi, elevateNodesOnSelect: Cn = !0, elevateEdgesOnSelect: Yt = !1, disableKeyboardA11y: De = !1, autoPanOnConnect: In, autoPanOnNodeDrag: An, autoPanOnSelection: _n = !0, autoPanSpeed: Zt, connectionRadius: Bi, isValidConnection: Fi, onError: Wi, style: Ki, id: Dn, nodeDragThreshold: Io, connectionDragThreshold: Ao, viewport: qi, onViewportChange: Mn, width: Xi, height: Ui, colorMode: Yi = "light", debug: Gt, onScroll: _t, ariaLabelConfig: Tn, zIndexMode: Jt = "basic", ...Zi }, Gi) {
  const $n = Dn || "1", Ji = am(Yi), Qi = fe((_o) => {
    _o.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), _t?.(_o);
  }, [_t]);
  return r.jsx("div", { "data-testid": "rf__wrapper", ...Zi, onScroll: Qi, style: { ...Ki, ..._x }, ref: Gi, className: Ce(["react-flow", i, Ji]), id: Dn, role: "application", children: r.jsxs(Ax, { nodes: e, edges: t, width: Xi, height: Ui, fitView: ft, fitViewOptions: ko, minZoom: Ye, maxZoom: Ze, nodeOrigin: Ie, nodeExtent: wn, zIndexMode: Jt, children: [r.jsx(sm, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: p, onConnectStart: g, onConnectEnd: v, onClickConnectStart: w, onClickConnectEnd: m, nodesDraggable: ge, autoPanOnNodeFocus: ve, nodesConnectable: Ee, nodesFocusable: $e, edgesFocusable: qe, edgesReconnectable: be, elementsSelectable: Re, elevateNodesOnSelect: Cn, elevateEdgesOnSelect: Yt, minZoom: Ye, maxZoom: Ze, nodeExtent: wn, onNodesChange: At, onEdgesChange: So, snapToGrid: ee, snapGrid: de, connectionMode: K, translateExtent: Ge, connectOnClick: Eo, defaultEdgeOptions: Hi, fitView: ft, fitViewOptions: ko, onNodesDelete: F, onEdgesDelete: D, onDelete: T, onNodeDragStart: j, onNodeDrag: _, onNodeDragStop: M, onSelectionDrag: A, onSelectionDragStart: N, onSelectionDragStop: I, onMove: d, onMoveStart: f, onMoveEnd: h, noPanClassName: xt, nodeOrigin: Ie, rfId: $n, autoPanOnConnect: In, autoPanOnNodeDrag: An, autoPanSpeed: Zt, onError: Wi, connectionRadius: Bi, isValidConnection: Fi, selectNodesOnDrag: Q, nodeDragThreshold: Io, connectionDragThreshold: Ao, onBeforeDelete: B, debug: Gt, ariaLabelConfig: Tn, zIndexMode: Jt }), r.jsx(kx, { onInit: l, onNodeClick: c, onEdgeClick: u, onNodeMouseEnter: k, onNodeMouseMove: y, onNodeMouseLeave: b, onNodeContextMenu: S, onNodeDoubleClick: E, nodeTypes: s, edgeTypes: a, connectionLineType: O, connectionLineStyle: Z, connectionLineComponent: X, connectionLineContainerStyle: oe, selectionKeyCode: G, selectionOnDrag: V, selectionMode: U, deleteKeyCode: se, multiSelectionKeyCode: ce, panActivationKeyCode: ae, zoomActivationKeyCode: J, onlyRenderVisibleElements: H, defaultViewport: Le, translateExtent: Ge, minZoom: Ye, maxZoom: Ze, preventScrolling: Et, zoomOnScroll: Kt, zoomOnPinch: vn, zoomOnDoubleClick: Ve, panOnScroll: bn, panOnScrollSpeed: vo, panOnScrollMode: Nn, panOnDrag: ut, autoPanOnSelection: _n, onPaneClick: dt, onPaneMouseEnter: qt, onPaneMouseMove: Oi, onPaneMouseLeave: jn, onPaneScroll: Xt, onPaneContextMenu: mt, paneClickDistance: Ct, nodeClickDistance: It, onSelectionContextMenu: $, onSelectionStart: z, onSelectionEnd: P, onReconnect: Sn, onReconnectStart: bo, onReconnectEnd: Oe, onEdgeContextMenu: No, onEdgeDoubleClick: we, onEdgeMouseEnter: He, onEdgeMouseMove: Ut, onEdgeMouseLeave: jo, reconnectRadius: me, defaultMarkerColor: Xe, noDragClassName: kn, noWheelClassName: _e, noPanClassName: xt, rfId: $n, disableKeyboardA11y: De, nodeExtent: wn, viewport: qi, onViewportChange: Mn }), r.jsx(nm, { onSelectionChange: L }), Ue, r.jsx(Gy, { proOptions: En, position: Co }), r.jsx(Zy, { rfId: $n, disableKeyboardA11y: De })] }) });
}
var xu = Ul(Dx);
const Mx = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function Tx({ children: e }) {
  const t = he(Mx);
  return t ? Fy.createPortal(e, t) : null;
}
function $x({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return r.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ce(["react-flow__background-pattern", n, o]) });
}
function Px({ radius: e, className: t }) {
  return r.jsx("circle", { cx: e, cy: e, r: e, className: Ce(["react-flow__background-pattern", "dots", t]) });
}
var Nt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(Nt || (Nt = {}));
const zx = {
  [Nt.Dots]: 1,
  [Nt.Lines]: 1,
  [Nt.Cross]: 6
}, Rx = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function wu({
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
  style: u,
  className: l,
  patternClassName: d
}) {
  const f = re(null), { transform: h, patternId: p } = he(Rx, Ne), g = o || zx[t], v = t === Nt.Dots, w = t === Nt.Cross, m = Array.isArray(n) ? n : [n, n], k = [m[0] * h[2] || 1, m[1] * h[2] || 1], y = g * h[2], b = Array.isArray(s) ? s : [s, s], S = w ? [y, y] : k, E = [
    b[0] * h[2] || 1 + S[0] / 2,
    b[1] * h[2] || 1 + S[1] / 2
  ], j = `${p}${e || ""}`;
  return r.jsxs("svg", { className: Ce(["react-flow__background", l]), style: {
    ...u,
    ...Di,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": a
  }, ref: f, "data-testid": "rf__background", children: [r.jsx("pattern", { id: j, x: h[0] % k[0], y: h[1] % k[1], width: k[0], height: k[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${E[0]},-${E[1]})`, children: v ? r.jsx(Px, { radius: y / 2, className: d }) : r.jsx($x, { dimensions: S, lineWidth: i, variant: t, className: d }) }), r.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${j})` })] });
}
wu.displayName = "Background";
const vu = ke(wu);
function Lx() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: r.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function Vx() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: r.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function Ox() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: r.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function Hx() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: r.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function Bx() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: r.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function Oo({ children: e, className: t, ...n }) {
  return r.jsx("button", { type: "button", className: Ce(["react-flow__controls-button", t]), ...n, children: e });
}
const Fx = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function bu({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: i, onZoomIn: s, onZoomOut: a, onFitView: c, onInteractiveChange: u, className: l, children: d, position: f = "bottom-left", orientation: h = "vertical", "aria-label": p }) {
  const g = je(), { isInteractive: v, minZoomReached: w, maxZoomReached: m, ariaLabelConfig: k } = he(Fx, Ne), { zoomIn: y, zoomOut: b, fitView: S } = as(), E = () => {
    y(), s?.();
  }, j = () => {
    b(), a?.();
  }, _ = () => {
    S(i), c?.();
  }, M = () => {
    g.setState({
      nodesDraggable: !v,
      nodesConnectable: !v,
      elementsSelectable: !v
    }), u?.(!v);
  }, F = h === "horizontal" ? "horizontal" : "vertical";
  return r.jsxs(_i, { className: Ce(["react-flow__controls", F, l]), position: f, style: e, "data-testid": "rf__controls", "aria-label": p ?? k["controls.ariaLabel"], children: [t && r.jsxs(r.Fragment, { children: [r.jsx(Oo, { onClick: E, className: "react-flow__controls-zoomin", title: k["controls.zoomIn.ariaLabel"], "aria-label": k["controls.zoomIn.ariaLabel"], disabled: m, children: r.jsx(Lx, {}) }), r.jsx(Oo, { onClick: j, className: "react-flow__controls-zoomout", title: k["controls.zoomOut.ariaLabel"], "aria-label": k["controls.zoomOut.ariaLabel"], disabled: w, children: r.jsx(Vx, {}) })] }), n && r.jsx(Oo, { className: "react-flow__controls-fitview", onClick: _, title: k["controls.fitView.ariaLabel"], "aria-label": k["controls.fitView.ariaLabel"], children: r.jsx(Ox, {}) }), o && r.jsx(Oo, { className: "react-flow__controls-interactive", onClick: M, title: k["controls.interactive.ariaLabel"], "aria-label": k["controls.interactive.ariaLabel"], children: v ? r.jsx(Bx, {}) : r.jsx(Hx, {}) }), d] });
}
bu.displayName = "Controls";
const Nu = ke(bu);
function Wx({ id: e, x: t, y: n, width: o, height: i, style: s, color: a, strokeColor: c, strokeWidth: u, className: l, borderRadius: d, shapeRendering: f, selected: h, onClick: p }) {
  const { background: g, backgroundColor: v } = s || {}, w = a || g || v;
  return r.jsx("rect", { className: Ce(["react-flow__minimap-node", { selected: h }, l]), x: t, y: n, rx: d, ry: d, width: o, height: i, style: {
    fill: w,
    stroke: c,
    strokeWidth: u
  }, shapeRendering: f, onClick: p ? (m) => p(m, e) : void 0 });
}
const Kx = ke(Wx), qx = (e) => e.nodes.map((t) => t.id), mr = (e) => e instanceof Function ? e : () => e;
function Xx({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: i,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: s = Kx,
  onClick: a
}) {
  const c = he(qx, Ne), u = mr(t), l = mr(e), d = mr(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return r.jsx(r.Fragment, { children: c.map((h) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    r.jsx(Yx, { id: h, nodeColorFunc: u, nodeStrokeColorFunc: l, nodeClassNameFunc: d, nodeBorderRadius: o, nodeStrokeWidth: i, NodeComponent: s, onClick: a, shapeRendering: f }, h)
  )) });
}
function Ux({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: i, nodeStrokeWidth: s, shapeRendering: a, NodeComponent: c, onClick: u }) {
  const { node: l, x: d, y: f, width: h, height: p } = he((g) => {
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
  return !l || l.hidden || !ml(l) ? null : r.jsx(c, { x: d, y: f, width: h, height: p, style: l.style, selected: !!l.selected, className: o(l), color: t(l), borderRadius: i, strokeColor: n(l), strokeWidth: s, shapeRendering: a, onClick: u, id: l.id });
}
const Yx = ke(Ux);
var Zx = ke(Xx);
const Gx = 200, Jx = 150, Qx = (e) => !e.hidden, e0 = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? gl(po(e.nodeLookup, { filter: Qx }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, t0 = "react-flow__minimap-desc";
function ju({
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
  bgColor: u,
  maskColor: l,
  maskStrokeColor: d,
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
  const S = je(), E = re(null), { boundingRect: j, viewBB: _, rfId: M, panZoom: F, translateExtent: D, flowWidth: T, flowHeight: L, ariaLabelConfig: N } = he(e0, Ne), A = e?.width ?? Gx, I = e?.height ?? Jx, $ = j.width / A, z = j.height / I, P = Math.max($, z), B = P * A, K = P * I, O = b * P, Z = j.x - (B - j.width) / 2 - O, X = j.y - (K - j.height) / 2 - O, oe = B + O * 2, se = K + O * 2, G = `${t0}-${M}`, V = re(0), U = re();
  V.current = P, te(() => {
    if (E.current && F)
      return U.current = gy({
        domNode: E.current,
        panZoom: F,
        getTransform: () => S.getState().transform,
        getViewScale: () => V.current
      }), () => {
        U.current?.destroy();
      };
  }, [F]), te(() => {
    U.current?.update({
      translateExtent: D,
      width: T,
      height: L,
      inversePan: k,
      pannable: v,
      zoomStep: y,
      zoomable: w
    });
  }, [v, w, k, y, D, T, L]);
  const ae = p ? (ee) => {
    const [de, H] = U.current?.pointer(ee) || [0, 0];
    p(ee, { x: de, y: H });
  } : void 0, ce = g ? fe((ee, de) => {
    const H = S.getState().nodeLookup.get(de).internals.userNode;
    g(ee, H);
  }, []) : void 0, J = m ?? N["minimap.ariaLabel"];
  return r.jsx(_i, { position: h, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * P : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof a == "number" ? a : void 0
  }, className: Ce(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: r.jsxs("svg", { width: A, height: I, viewBox: `${Z} ${X} ${oe} ${se}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": G, ref: E, onClick: ae, children: [J && r.jsx("title", { id: G, children: J }), r.jsx(Zx, { onClick: ce, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: s, nodeClassName: i, nodeStrokeWidth: a, nodeComponent: c }), r.jsx("path", { className: "react-flow__minimap-mask", d: `M${Z - O},${X - O}h${oe + O * 2}v${se + O * 2}h${-oe - O * 2}z
        M${_.x},${_.y}h${_.width}v${_.height}h${-_.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
ju.displayName = "MiniMap";
const Su = ke(ju), n0 = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, o0 = {
  [hn.Line]: "right",
  [hn.Handle]: "bottom-right"
};
function i0({ nodeId: e, position: t, variant: n = hn.Handle, className: o, style: i = void 0, children: s, color: a, minWidth: c = 10, minHeight: u = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: h, autoScale: p = !0, shouldResize: g, onResizeStart: v, onResize: w, onResizeEnd: m }) {
  const k = Jl(), y = typeof e == "string" ? e : k, b = je(), S = re(null), E = n === hn.Handle, j = he(fe(n0(E && p), [E, p]), Ne), _ = re(null), M = t ?? o0[n];
  te(() => {
    if (!(!S.current || !y))
      return _.current || (_.current = Iy({
        domNode: S.current,
        nodeId: y,
        getStoreItems: () => {
          const { nodeLookup: D, transform: T, snapGrid: L, snapToGrid: N, nodeOrigin: A, domNode: I } = b.getState();
          return {
            nodeLookup: D,
            transform: T,
            snapGrid: L,
            snapToGrid: N,
            nodeOrigin: A,
            paneDomNode: I
          };
        },
        onChange: (D, T) => {
          const { triggerNodeChanges: L, nodeLookup: N, parentLookup: A, nodeOrigin: I } = b.getState(), $ = [], z = { x: D.x, y: D.y }, P = N.get(y);
          if (P && P.expandParent && P.parentId) {
            const B = P.origin ?? I, K = D.width ?? P.measured.width ?? 0, O = D.height ?? P.measured.height ?? 0, Z = {
              id: P.id,
              parentId: P.parentId,
              rect: {
                width: K,
                height: O,
                ...xl({
                  x: D.x ?? P.position.x,
                  y: D.y ?? P.position.y
                }, { width: K, height: O }, P.parentId, N, B)
              }
            }, X = ss([Z], N, A, I);
            $.push(...X), z.x = D.x ? Math.max(B[0] * K, D.x) : void 0, z.y = D.y ? Math.max(B[1] * O, D.y) : void 0;
          }
          if (z.x !== void 0 && z.y !== void 0) {
            const B = {
              id: y,
              type: "position",
              position: { ...z }
            };
            $.push(B);
          }
          if (D.width !== void 0 && D.height !== void 0) {
            const K = {
              id: y,
              type: "dimensions",
              resizing: !0,
              setAttributes: h ? h === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: D.width,
                height: D.height
              }
            };
            $.push(K);
          }
          for (const B of T) {
            const K = {
              ...B,
              type: "position"
            };
            $.push(K);
          }
          L($);
        },
        onEnd: ({ width: D, height: T }) => {
          const L = {
            id: y,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: D,
              height: T
            }
          };
          b.getState().triggerNodeChanges([L]);
        }
      })), _.current.update({
        controlPosition: M,
        boundaries: {
          minWidth: c,
          minHeight: u,
          maxWidth: l,
          maxHeight: d
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
    M,
    c,
    u,
    l,
    d,
    f,
    v,
    w,
    m,
    g
  ]);
  const F = M.split("-");
  return r.jsx("div", { className: Ce(["react-flow__resize-control", "nodrag", ...F, n, o]), ref: S, style: {
    ...i,
    scale: j,
    ...a && { [E ? "backgroundColor" : "borderColor"]: a }
  }, children: s });
}
ke(i0);
const r0 = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), ku = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var s0 = {
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
const a0 = vi(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: o,
    className: i = "",
    children: s,
    iconNode: a,
    ...c
  }, u) => br(
    "svg",
    {
      ref: u,
      ...s0,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: ku("lucide", i),
      ...c
    },
    [
      ...a.map(([l, d]) => br(l, d)),
      ...Array.isArray(s) ? s : [s]
    ]
  )
);
const pe = (e, t) => {
  const n = vi(
    ({ className: o, ...i }, s) => br(a0, {
      ref: s,
      iconNode: t,
      className: ku(`lucide-${r0(e)}`, o),
      ...i
    })
  );
  return n.displayName = `${e}`, n;
};
const c0 = pe("Activity", [
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
const l0 = pe("ChevronDown", [
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
const u0 = pe("CodeXml", [
  ["path", { d: "m18 16 4-4-4-4", key: "1inbqp" }],
  ["path", { d: "m6 8-4 4 4 4", key: "15zrgr" }],
  ["path", { d: "m14.5 4-5 16", key: "e7oirm" }]
]);
const d0 = pe("Copy", [
  ["rect", { width: "14", height: "14", x: "8", y: "8", rx: "2", ry: "2", key: "17jyea" }],
  ["path", { d: "M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2", key: "zix9uf" }]
]);
const f0 = pe("Download", [
  ["path", { d: "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4", key: "ih7n3h" }],
  ["polyline", { points: "7 10 12 15 17 10", key: "2ggqvy" }],
  ["line", { x1: "12", x2: "12", y1: "15", y2: "3", key: "1vk2je" }]
]);
const Ya = pe("EyeOff", [
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
const Eu = pe("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const h0 = pe("GripVertical", [
  ["circle", { cx: "9", cy: "12", r: "1", key: "1vctgf" }],
  ["circle", { cx: "9", cy: "5", r: "1", key: "hp0tcf" }],
  ["circle", { cx: "9", cy: "19", r: "1", key: "fkjjf6" }],
  ["circle", { cx: "15", cy: "12", r: "1", key: "1tmaij" }],
  ["circle", { cx: "15", cy: "5", r: "1", key: "19l28e" }],
  ["circle", { cx: "15", cy: "19", r: "1", key: "f4zoj3" }]
]);
const p0 = pe("ListChecks", [
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
const ui = pe("Maximize2", [
  ["polyline", { points: "15 3 21 3 21 9", key: "mznyad" }],
  ["polyline", { points: "9 21 3 21 3 15", key: "1avn1i" }],
  ["line", { x1: "21", x2: "14", y1: "3", y2: "10", key: "ota7mn" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const Za = pe("Minimize2", [
  ["polyline", { points: "4 14 10 14 10 20", key: "11kfnr" }],
  ["polyline", { points: "20 10 14 10 14 4", key: "rlmsce" }],
  ["line", { x1: "14", x2: "21", y1: "10", y2: "3", key: "o5lafz" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const g0 = pe("Network", [
  ["rect", { x: "16", y: "16", width: "6", height: "6", rx: "1", key: "4q2zg0" }],
  ["rect", { x: "2", y: "16", width: "6", height: "6", rx: "1", key: "8cvhb9" }],
  ["rect", { x: "9", y: "2", width: "6", height: "6", rx: "1", key: "1egb70" }],
  ["path", { d: "M5 16v-3a1 1 0 0 1 1-1h12a1 1 0 0 1 1 1v3", key: "1jsf9p" }],
  ["path", { d: "M12 12V8", key: "2874zd" }]
]);
const Cu = pe("Package", [
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
const so = pe("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const y0 = pe("Redo2", [
  ["path", { d: "m15 14 5-5-5-5", key: "12vg1m" }],
  ["path", { d: "M20 9H9.5A5.5 5.5 0 0 0 4 14.5A5.5 5.5 0 0 0 9.5 20H13", key: "6uklza" }]
]);
const us = pe("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const Iu = pe("Save", [
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
const Ti = pe("Search", [
  ["circle", { cx: "11", cy: "11", r: "8", key: "4ej97u" }],
  ["path", { d: "m21 21-4.3-4.3", key: "1qie3q" }]
]);
const Ga = pe("Shield", [
  [
    "path",
    {
      d: "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z",
      key: "oel41y"
    }
  ]
]);
const ds = pe("SlidersHorizontal", [
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
const m0 = pe("Terminal", [
  ["polyline", { points: "4 17 10 11 4 5", key: "akl6gq" }],
  ["line", { x1: "12", x2: "20", y1: "19", y2: "19", key: "q2wloq" }]
]);
const di = pe("Trash2", [
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
const x0 = pe("Undo2", [
  ["path", { d: "M9 14 4 9l5-5", key: "102s5s" }],
  ["path", { d: "M4 9h10.5a5.5 5.5 0 0 1 5.5 5.5a5.5 5.5 0 0 1-5.5 5.5H11", key: "f3b9sd" }]
]);
const w0 = pe("Workflow", [
  ["rect", { width: "8", height: "8", x: "3", y: "3", rx: "2", key: "by2w9f" }],
  ["path", { d: "M7 11v4a2 2 0 0 0 2 2h4", key: "xkn7yn" }],
  ["rect", { width: "8", height: "8", x: "13", y: "13", rx: "2", key: "1cgmvn" }]
]);
const Au = pe("X", [
  ["path", { d: "M18 6 6 18", key: "1bl5f8" }],
  ["path", { d: "m6 6 12 12", key: "d8bk6v" }]
]);
const v0 = pe("Zap", [
  [
    "path",
    {
      d: "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z",
      key: "1xq2db"
    }
  ]
]), b0 = /* @__PURE__ */ new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);
function $i(e) {
  return _u(e, N0);
}
function Pi(e) {
  return _u(e, j0);
}
function _u(e, t) {
  return !e || !e.rootActivity ? e : { ...e, rootActivity: Du(e.rootActivity, t) };
}
function Du(e, t) {
  const n = t(e), o = n.structure;
  if (!o || !yn(o.payload)) return n;
  let i = !1;
  const s = { ...o.payload };
  for (const [a, c] of Object.entries(o.payload))
    Array.isArray(c) && c.length > 0 && c.every(I0) && (s[a] = c.map((u) => Du(u, t)), i = !0);
  return i ? { ...n, structure: { ...o, payload: s } } : n;
}
function N0(e) {
  const t = [], n = {};
  for (const [i, s] of Object.entries(e))
    b0.has(i) || (C0(s) ? t.push({
      referenceKey: S0(i),
      value: { value: E0(s.expression.value), expressionType: s.expression.type || "Literal" }
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
function j0(e) {
  const t = Array.isArray(e.inputs) ? e.inputs : [], n = {};
  for (const o of t) {
    if (!yn(o) || typeof o.referenceKey != "string") continue;
    const i = yn(o.value) ? o.value : {};
    n[k0(o.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof i.expressionType == "string" ? i.expressionType : "Literal",
        value: i.value ?? ""
      }
    };
  }
  return { ...e, ...n, inputs: [] };
}
function S0(e) {
  return e && e.charAt(0).toUpperCase() + e.slice(1);
}
function k0(e) {
  return e && e.charAt(0).toLowerCase() + e.slice(1);
}
function E0(e) {
  return e == null ? null : typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function C0(e) {
  if (!yn(e) || Array.isArray(e)) return !1;
  const t = e.expression;
  return yn(t) && typeof t.type == "string";
}
function I0(e) {
  return yn(e) && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function yn(e) {
  return typeof e == "object" && e !== null;
}
const Ae = "/_elsa/workflow-management", A0 = "/publishing", Yn = {
  activities: ["workflows", "activities"],
  activityAvailabilitySettings: ["workflows", "activity-availability", "settings"],
  activityAvailabilityDiagnostics: ["workflows", "activity-availability", "diagnostics"]
};
function _0(e) {
  return Dc({
    queryKey: Yn.activityAvailabilitySettings,
    queryFn: () => q0(e)
  });
}
function D0(e) {
  return Dc({
    queryKey: Yn.activityAvailabilityDiagnostics,
    queryFn: () => $u(e)
  });
}
function M0(e) {
  const t = Xd();
  return Ud({
    mutationFn: (n) => X0(e, n),
    onSuccess: () => {
      t.invalidateQueries({ queryKey: Yn.activityAvailabilitySettings }), t.invalidateQueries({ queryKey: Yn.activityAvailabilityDiagnostics }), t.invalidateQueries({ queryKey: Yn.activities });
    }
  });
}
async function T0(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${Ae}/definitions?${n.toString()}`);
}
async function $0(e, t) {
  const n = await e.http.getJson(`${Ae}/definitions/${encodeURIComponent(t)}`);
  return n.draft ? { ...n, draft: { ...n.draft, state: Pi(n.draft.state) } } : n;
}
async function P0(e, t) {
  const n = await e.http.getJson(`${Ae}/versions/${encodeURIComponent(t)}`);
  return { ...n, state: Pi(n.state) };
}
async function z0(e, t) {
  return e.http.postJson(`${Ae}/definitions`, t);
}
async function R0(e, t) {
  await e.http.deleteJson(`${Ae}/definitions/${encodeURIComponent(t)}`);
}
async function L0(e, t) {
  await e.http.postJson(`${Ae}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function V0(e, t) {
  await e.http.deleteJson(`${Ae}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function O0(e, t) {
  const n = await e.http.putJson(
    `${Ae}/drafts/${encodeURIComponent(t.id)}`,
    { state: $i(t.state), layout: t.layout }
  );
  return { ...n, state: Pi(n.state) };
}
async function H0(e, t) {
  return e.http.postJson(`${Ae}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function B0(e, t) {
  return e.http.postJson(`${Ae}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function F0(e, t) {
  const n = { ...t, state: $i(t.state) };
  try {
    return await e.http.postJson(`${A0}/workflows/drafts/test-runs`, n);
  } catch (o) {
    const i = J0(o);
    if (i) return i;
    throw o;
  }
}
async function Mu(e, t) {
  return e.http.postJson(`${Ae}/executables/${encodeURIComponent(t)}/run`, {});
}
async function Tu(e) {
  return e.http.getJson("/_demo/workflows/executables");
}
async function W0(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.runKind && n.set("runKind", t.runKind), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const o = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${o ? `?${o}` : ""}`);
}
async function K0(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function fs(e) {
  return e.http.getJson(`${Ae}/activities`);
}
async function q0(e) {
  return e.http.getJson(`${Ae}/activities/availability/settings`);
}
async function X0(e, t) {
  return e.http.putJson(`${Ae}/activities/availability/settings`, t);
}
async function $u(e) {
  return e.http.getJson(`${Ae}/activities/availability/diagnostics`);
}
async function U0(e) {
  const t = await zi(e, [
    `${Ae}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? Ja(t) : Ja(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function Y0(e) {
  const t = await zi(e, [
    `${Ae}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : Uo;
}
async function Z0(e) {
  const t = await zi(e, [
    `${Ae}/descriptors/variables`,
    "/descriptors/variables"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((o) => Pu(o));
}
async function G0(e) {
  const t = await zi(e, [
    `${Ae}/descriptors/storage-drivers`,
    "/descriptors/storage-drivers"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((o) => Pu(o));
}
function Pu(e) {
  return !!e && typeof e == "object" && typeof e.typeName == "string" && e.typeName.length > 0;
}
async function zi(e, t) {
  let n;
  for (const o of t)
    try {
      return await e.http.getJson(o);
    } catch (i) {
      n = i;
    }
  throw n;
}
function Ja(e) {
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
function J0(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = Qa(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return Qa(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function Qa(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const Uo = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
], Q0 = [
  "Available",
  "BlockedByHostBaseline",
  "HiddenByManagementSettings",
  "RemovedFromCatalog",
  "UnresolvedReference"
], ew = {
  Available: "Available",
  BlockedByHostBaseline: "Host blocked",
  HiddenByManagementSettings: "Management hidden",
  RemovedFromCatalog: "Removed",
  UnresolvedReference: "Unresolved"
};
function zt(e) {
  return typeof e == "string" ? e : typeof e == "number" ? Q0[e] ?? "Available" : "Available";
}
function hi(e) {
  const t = zt(e);
  return ew[t] ?? t;
}
function tw(e) {
  return zt(e).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function nw(e) {
  return zt(e) !== "Available";
}
function ow(e) {
  return e === "Only" || e === 1 ? "Only" : "AllExcept";
}
function iw(e) {
  return e === "Only" ? 1 : 0;
}
function ec(e) {
  const t = e?.rules;
  return {
    mode: ow(e?.mode),
    activityTypes: t?.activityTypes ?? [],
    sets: t?.sets ?? []
  };
}
function rw(e) {
  return e.referenceKind === 0 || e.referenceKind === "ActivityType";
}
function sw(e) {
  return [...e?.items ?? []].filter(rw).filter((t) => t.activityTypeKey && t.activityDefinitionId).sort((t, n) => pi(t).localeCompare(pi(n)));
}
function aw(e) {
  return [...e?.items ?? []].filter((t) => {
    const n = zt(t.state);
    return n === "RemovedFromCatalog" || n === "UnresolvedReference";
  }).sort((t, n) => (t.referenceName ?? "").localeCompare(n.referenceName ?? ""));
}
function tc(e, t) {
  return e.includes(t) ? e.filter((n) => n !== t) : [...e, t].sort((n, o) => n.localeCompare(o));
}
function pi(e) {
  const t = e?.displayName?.trim();
  if (t) return t;
  const n = e?.activityTypeKey?.split(".").filter(Boolean).at(-1) ?? "";
  return cw(n) || e?.activityTypeKey || "Activity";
}
function cw(e) {
  return e.replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").replace(/([a-z0-9])([A-Z])/g, "$1 $2").trim();
}
function lw(e, t) {
  const n = new Set(e.filter((o) => !!o));
  return (t?.items ?? []).find((o) => nw(o.state) ? [o.activityDefinitionId, o.activityTypeKey, o.referenceName].some((i) => i && n.has(i)) : !1) ?? null;
}
const Ri = "elsa.sequence.structure", mo = "elsa.flowchart.structure";
function zu(e, t) {
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
function Zn(e, t) {
  const n = zu(e, t);
  if (!n) return null;
  let o = ot(n)[0];
  return o ? { owner: n, slot: o } : null;
}
function ot(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = Aw(t), i = xr(n.activities);
  return i ? [{
    id: `${t.kind}:activities`,
    label: _w(t),
    property: "activities",
    mode: o,
    activities: i
  }] : Object.entries(n).filter(([, s]) => xr(s)).map(([s, a]) => ({
    id: `${t.kind}:${s}`,
    label: Mw(s),
    property: s,
    mode: "generic",
    activities: xr(a) ?? []
  }));
}
function Ru(e, t, n) {
  const o = new Map(t.map((a) => [a.activityVersionId, a])), i = new Map(n.map((a) => [a.nodeId, a])), s = e.slot.activities.map((a, c) => {
    const u = o.get(a.activityVersionId), l = i.get(a.nodeId) ?? Dw(e.slot.mode, c);
    return Ou(a, u, { x: l.x, y: l.y });
  });
  return {
    nodes: s,
    edges: e.slot.mode === "flowchart" ? bw(e.owner) : vw(e.slot, s)
  };
}
function Pr(e, t, n) {
  const o = t.find((s) => s.activityVersionId === e.activityVersionId), i = n.find((s) => s.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [Ou(e, o, { x: i.x, y: i.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function uw(e, t, n, o = null) {
  const i = new Map(t.map((c) => [c.activityExecutionId, c])), s = oc(t, (c) => c.authoredActivityId || c.executableNodeId), a = oc(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? i.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const u = s.get(c.id) ?? [], l = a.get(c.id) ?? [];
    if (u.length === 0 && l.length === 0) return c;
    const d = Ew(u), f = o === c.id || u.some((p) => p.activityExecutionId === o) || l.some((p) => p.incidentId === o), h = {
      status: d?.status,
      subStatus: d?.subStatus,
      activityExecutionId: d?.activityExecutionId,
      faultCount: u.reduce((p, g) => p + g.faultCount + g.aggregateFaultCount, 0),
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
  return e?.structure?.kind === mo || gw(t) ? "flowchart" : e?.structure?.kind === Ri || yw(t) ? "sequence" : "unsupported";
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
function Lu(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...i] = t, s = ot(e).find((c) => c.id === o.slotId);
  if (!s) return e;
  const a = s.activities.map((c) => c.nodeId === o.ownerNodeId ? Lu(c, i, n) : c);
  return ao(e, s, a);
}
function Vu(e, t, n) {
  if (e.nodeId === t) return n(e);
  const o = ot(e);
  if (o.length === 0) return e;
  let i = !1, s = e;
  for (const a of o) {
    const c = a.activities.map((u) => {
      const l = Vu(u, t, n);
      return l !== u && (i = !0), l;
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
function dw(e, t, n, o = []) {
  const i = new Map(e.slot.activities.map((a) => [a.nodeId, a]));
  for (const a of o)
    i.set(a.nodeId, a);
  const s = t.map((a) => i.get(a.id)).filter((a) => !!a);
  return e.slot.mode === "sequence" && s.sort((a, c) => {
    const u = t.find((d) => d.id === a.nodeId), l = t.find((d) => d.id === c.nodeId);
    return (u?.position.x ?? 0) - (l?.position.x ?? 0);
  }), ao(e.owner, e.slot, s);
}
function fw(e, t) {
  return {
    ...e,
    structure: ww(e.structure, t)
  };
}
function hw(e, t) {
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
    structure: xw(e)
  };
}
function Se(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? mw(t) : n;
}
function Ou(e, t, n, o = {}) {
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
      acceptsInbound: Nw(e, t),
      sourcePorts: o.suppressFlowPorts ? [] : Hu(e, t),
      suppressFlowPorts: o.suppressFlowPorts
    }
  };
}
function gi(e) {
  if (!e) return "activity";
  const t = pw(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), o = Se(e).toLowerCase(), i = e.category?.toLowerCase() ?? "", s = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || o === "flowchart" ? "flowchart" : n.endsWith(".sequence") || o === "sequence" ? "sequence" : n.includes("writeline") || o.includes("write line") ? "terminal" : i.includes("runtime") ? "runtime" : s === "trigger" ? "trigger" : "activity";
}
function pw(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function gw(e) {
  return !!e && (Se(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function yw(e) {
  return !!e && (Se(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function mw(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function xw(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: Ri,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: mo,
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
function ww(e, t) {
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
        const s = o.get(i.id) ?? {}, a = i.data?.vertices, { vertices: c, ...u } = s;
        return {
          ...u,
          id: i.id,
          source: { nodeId: i.source, port: i.sourceHandle ?? "Done" },
          target: i.targetHandle ? { nodeId: i.target, port: i.targetHandle } : { nodeId: i.target },
          ...a?.length ? { vertices: a.map((l) => ({ x: Math.round(l.x), y: Math.round(l.y) })) } : {}
        };
      })
    }
  };
}
function vw(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function bw(e) {
  if (e.structure?.kind !== mo) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const i = n.source, s = n.target;
    if (!i?.nodeId || !s?.nodeId) return null;
    const a = Array.isArray(n.vertices) ? n.vertices.filter(Cw) : [];
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
function Hu(e, t) {
  const n = nc(e.cases);
  if (Sw(e, t) && n.length > 0)
    return [...n.map((s) => ({ name: s, displayName: s })), { name: "Default", displayName: "Default" }];
  const o = [
    ...Yo(t?.designFacets),
    ...Yo(t?.ports),
    ...Yo(t?.outputs)
  ];
  if (o.length > 0) return kw(o);
  const i = nc(e.outcomes);
  return i.length > 0 ? i.map((s) => ({ name: s, displayName: s })) : [{ name: "Done", displayName: "Done" }];
}
function Nw(e, t) {
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
function jw(e, t, n) {
  const o = yi(t.source, n, t.sourceHandle ?? "Done", void 0), i = yi(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((s) => s.id !== t.id).concat(o, i);
}
function xr(e) {
  return Array.isArray(e) ? e.filter(Iw) : null;
}
function Sw(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function Yo(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!ps(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...Yo(n.ports));
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
function kw(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function nc(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function oc(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const o of e) {
    const i = t(o);
    i && n.set(i, [...n.get(i) ?? [], o]);
  }
  return n;
}
function Ew(e) {
  return [...e].sort((t, n) => ic(n).localeCompare(ic(t)))[0];
}
function ic(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function Cw(e) {
  return ps(e) && typeof e.x == "number" && typeof e.y == "number";
}
function ps(e) {
  return typeof e == "object" && e !== null;
}
function Iw(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Aw(e) {
  return e.kind === Ri ? "sequence" : e.kind === mo ? "flowchart" : "generic";
}
function _w(e) {
  return e.kind === Ri || e.kind === mo, "Activities";
}
function Dw(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function Mw(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Tw = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function Bu(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function gs(e) {
  return Bu(e.name);
}
function $w(e, t) {
  const n = gs(t), o = e[n];
  return t.isWrapped === !1 ? o ?? t.defaultValue ?? "" : Wu(o, t);
}
function Fu(e, t) {
  return Wu(e[gs(t)], t);
}
function Pw(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function zw(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function rc(e, t, n) {
  return {
    ...e,
    [gs(t)]: n
  };
}
function Rw(e, t) {
  return t.isWrapped === !1 ? $w(e, t) : Fu(e, t).expression.value;
}
function Wu(e, t) {
  return Lw(e) ? {
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
function Lw(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
const Ku = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]);
function Vw({
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
  const c = t.inputs.filter((d) => d.isBrowsable !== !1).sort((d, f) => (d.order ?? 0) - (f.order ?? 0) || d.name.localeCompare(f.name));
  if (c.length === 0)
    return /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "This activity does not expose editable properties." });
  const u = Kw(c), l = i.length > 0 ? i : Tw;
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ r.jsx("span", { className: "wf-section-label", children: "Properties" }),
    u.map((d) => /* @__PURE__ */ r.jsxs("section", { className: "wf-property-group", children: [
      u.length > 1 ? /* @__PURE__ */ r.jsx("h4", { children: d.category }) : null,
      d.inputs.map((f) => /* @__PURE__ */ r.jsx(
        Ow,
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
    ] }, d.category))
  ] });
}
function Ow({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: o,
  expressionDescriptors: i,
  onChange: s
}) {
  const a = t.isReadOnly === !0, c = { activity: e, expressionDescriptors: i, readOnly: a }, u = Fw(n, t, c), l = u?.component, d = t.isWrapped !== !1 ? Fu(e, t) : null, f = d?.expression.type ?? "Literal", h = Rw(e, t), p = d ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: i,
    readOnly: a,
    surface: "inline",
    syntax: f
  } : null, g = p ? qu(o, p) : null, v = g?.surfaces.inline, w = g && p ? Xu(g, p, h) : [], m = !!(d && qw(t, u?.id)), k = !!(d && Xw(t, u?.id)), [y, b] = q(!1), S = (_) => {
    const M = d ? Pw(d, _) : _;
    s(rc(e, t, M));
  }, E = (_) => {
    d && s(rc(e, t, zw(d, _)));
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
  ) : Bw(l, t, h, a, c, S);
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ r.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ r.jsx("span", { children: Uu(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ r.jsx("p", { children: t.description }) : null,
    d && !m ? /* @__PURE__ */ r.jsx(
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
          children: /* @__PURE__ */ r.jsx(ui, { size: 13 })
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
          /* @__PURE__ */ r.jsx(ui, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    y ? /* @__PURE__ */ r.jsx(
      Hw,
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
function Hw({
  input: e,
  value: t,
  syntax: n,
  descriptors: o,
  activity: i,
  expressionEditors: s,
  disabled: a,
  onChange: c,
  onSyntaxChange: u,
  onClose: l
}) {
  const d = _c(), f = e.displayName || e.name, h = {
    activity: i,
    descriptor: e,
    expressionDescriptors: o,
    readOnly: a,
    surface: "expanded",
    syntax: n
  }, p = qu(s, h), g = p?.surfaces.expanded, v = p ? Xu(p, h, t) : [], w = g ? null : Ww(s, h);
  return te(() => {
    const m = (k) => {
      k.key === "Escape" && l();
    };
    return window.addEventListener("keydown", m), () => window.removeEventListener("keydown", m);
  }, [l]), /* @__PURE__ */ r.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ r.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": d, children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ r.jsx("h3", { id: d, children: f })
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": `Close ${f} editor`, onClick: l, children: /* @__PURE__ */ r.jsx(Au, { size: 16 }) })
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
            onChange: u
          }
        ),
        /* @__PURE__ */ r.jsx("span", { children: Uu(e.typeName) })
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
function Bw(e, t, n, o, i, s) {
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
  const [a, c] = q(!1), u = _c(), l = n.find((f) => f.type === t), d = [
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
        className: d,
        "aria-label": e,
        "aria-haspopup": "listbox",
        "aria-expanded": a,
        "aria-controls": u,
        disabled: o,
        onClick: () => c((f) => !f),
        children: /* @__PURE__ */ r.jsx("span", { children: l?.displayName || l?.type || t })
      }
    ),
    a ? /* @__PURE__ */ r.jsx("div", { id: u, role: "listbox", className: "wf-syntax-picker-menu", "aria-label": e, children: n.map((f) => {
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
function Fw(e, t, n) {
  return [...e].sort((o, i) => (o.order ?? 500) - (i.order ?? 500)).find((o) => o.supports(t, n));
}
function qu(e, t) {
  return [...e].sort((n, o) => (n.order ?? 500) - (o.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function Xu(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function Ww(e, t) {
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
function Kw(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "General";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return [...t.entries()].map(([n, o]) => ({ category: n, inputs: o }));
}
function Uu(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function qw(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !Ku.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function Xw(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !Ku.has(t) && n !== "multiline") return !1;
  const o = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(o) || n === "singleline" || n === "multiline";
}
function Uw(e, t) {
  return {
    ...t ? { name: t } : {},
    definitionId: e.definitionId,
    state: $i(e.state),
    layout: e.layout
  };
}
function Yw(e) {
  return JSON.stringify(
    {
      state: $i(e.state),
      layout: e.layout
    },
    null,
    2
  );
}
function Zw(e, t) {
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
      state: Pi(o.state),
      layout: o.layout ?? t.layout
    }
  };
}
function Gw(e, t) {
  const n = (t ?? "workflow").trim().replace(/[^\w.-]+/g, "-") || "workflow", o = new Blob([JSON.stringify(e, null, 2)], { type: "application/json" }), i = URL.createObjectURL(o), s = document.createElement("a");
  s.href = i, s.download = `${n}.json`, document.body.appendChild(s), s.click(), s.remove(), URL.revokeObjectURL(i);
}
const Jw = 320, Qw = 140;
function ev(e, t, n) {
  return n === "sequence" ? tv(e) : nv(e, t);
}
function tv(e) {
  const t = /* @__PURE__ */ new Map();
  return e.forEach((n, o) => {
    t.set(n.id, { x: o * 280, y: 0 });
  }), t;
}
function nv(e, t) {
  const n = /* @__PURE__ */ new Map();
  if (e.length === 0) return n;
  const o = new Set(e.map((d) => d.id)), i = t.filter((d) => o.has(d.source) && o.has(d.target)), s = /* @__PURE__ */ new Set();
  for (const d of i)
    s.add(d.source), s.add(d.target);
  const a = /* @__PURE__ */ new Map();
  for (const d of e) a.set(d.id, 0);
  for (let d = 0; d < e.length; d += 1) {
    let f = !1;
    for (const h of i) {
      const p = (a.get(h.source) ?? 0) + 1;
      p > (a.get(h.target) ?? 0) && p <= e.length && (a.set(h.target, p), f = !0);
    }
    if (!f) break;
  }
  const c = Math.max(0, ...e.filter((d) => s.has(d.id)).map((d) => a.get(d.id) ?? 0)), u = s.size > 0 ? c + 1 : 0, l = /* @__PURE__ */ new Map();
  for (const d of e) {
    const f = s.has(d.id) ? a.get(d.id) ?? 0 : u, h = l.get(f);
    h ? h.push(d.id) : l.set(f, [d.id]);
  }
  for (const [d, f] of l)
    f.forEach((h, p) => {
      n.set(h, { x: d * Jw, y: p * Qw });
    });
  return n;
}
const ov = 50;
function sc() {
  return { past: [], future: [] };
}
function iv(e) {
  return e.past.length > 0;
}
function rv(e) {
  return e.future.length > 0;
}
function ac(e, t, n = ov) {
  const o = [...e.past, t];
  return o.length > n && o.splice(0, o.length - n), { past: o, future: [] };
}
function sv(e, t) {
  if (e.past.length === 0) return null;
  const n = e.past.slice(), o = n.pop();
  return { history: { past: n, future: [...e.future, t] }, snapshot: o };
}
function av(e, t) {
  if (e.future.length === 0) return null;
  const n = e.future.slice(), o = n.pop();
  return { history: { past: [...e.past, t], future: n }, snapshot: o };
}
function it(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function Yu(e, t) {
  if (!e || !t) return "";
  const n = Date.parse(e), o = Date.parse(t);
  if (Number.isNaN(n) || Number.isNaN(o) || o < n) return "";
  const i = Math.round((o - n) / 1e3);
  if (i < 60) return `${i}s`;
  const s = Math.floor(i / 60), a = i % 60;
  if (s < 60) return a ? `${s}m ${a}s` : `${s}m`;
  const c = Math.floor(s / 60), u = s % 60;
  return u ? `${c}h ${u}m` : `${c}h`;
}
function mi(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function ys(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ r.jsx(Eu, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ r.jsx(ls, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ r.jsx(m0, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ r.jsx(gn, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ r.jsx(v0, { size: 15 });
    default:
      return /* @__PURE__ */ r.jsx(Mi, { size: 15 });
  }
}
function cc({
  document: e,
  readOnly: t,
  minHeight: n,
  ariaLabel: o,
  onChange: i
}) {
  const s = (a) => {
    t || i({ ...e, value: a.target.value });
  };
  return /* @__PURE__ */ r.jsx(
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
function cv({
  document: e,
  diagnostics: t = [],
  readOnly: n = !1,
  theme: o = "studio",
  minHeight: i = "220px",
  ariaLabel: s,
  languageAdapter: a,
  onChange: c
}) {
  const u = t.filter((h) => !h.uri || h.uri === e.uri), l = a?.displayName ?? e.language, d = a?.loadEditor, f = ue(
    () => d ? Kd(d) : null,
    [d]
  );
  return /* @__PURE__ */ r.jsxs(
    "section",
    {
      className: "studio-code-editor",
      "data-language": e.language,
      "data-theme": o,
      "data-readonly": n,
      children: [
        /* @__PURE__ */ r.jsxs("div", { className: "studio-code-editor-header", children: [
          /* @__PURE__ */ r.jsx("span", { children: l }),
          /* @__PURE__ */ r.jsx("code", { children: e.uri })
        ] }),
        f ? /* @__PURE__ */ r.jsx(qd, { fallback: /* @__PURE__ */ r.jsx(
          cc,
          {
            document: e,
            readOnly: n,
            minHeight: i,
            ariaLabel: s,
            onChange: c
          }
        ), children: /* @__PURE__ */ r.jsx(
          f,
          {
            document: e,
            readOnly: n,
            theme: o,
            minHeight: i,
            ariaLabel: s,
            onChange: c
          }
        ) }) : /* @__PURE__ */ r.jsx(
          cc,
          {
            document: e,
            readOnly: n,
            minHeight: i,
            ariaLabel: s,
            onChange: c
          }
        ),
        /* @__PURE__ */ r.jsx(lv, { diagnostics: u })
      ]
    }
  );
}
function lv({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ r.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const o = t.severity ?? "info", i = uv(t);
    return /* @__PURE__ */ r.jsxs(
      "p",
      {
        className: `studio-code-editor-diagnostic ${o}`,
        children: [
          t.code ? /* @__PURE__ */ r.jsx("span", { children: t.code }) : null,
          i ? /* @__PURE__ */ r.jsx("small", { children: i }) : null,
          t.message
        ]
      },
      `${t.uri ?? "document"}-${t.code ?? "diagnostic"}-${n}`
    );
  }) });
}
function uv(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const dv = { language: "json", displayName: "JSON" };
function fv({ draft: e, onApply: t }) {
  const n = ue(() => Yw(e), [e]), [o, i] = q(n), [s, a] = q(n), [c, u] = q(null);
  te(() => {
    i(n), a(n), u(null);
  }, [n]);
  const l = o !== s, d = c ? [{ severity: "error", message: c }] : [], f = () => u(t(o));
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-code-view", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-code-view-toolbar", children: [
      /* @__PURE__ */ r.jsx("span", { className: "wf-muted", children: "Edit the workflow definition as JSON. Changes apply to the draft when you click Apply." }),
      /* @__PURE__ */ r.jsxs("span", { className: "wf-code-view-actions", children: [
        /* @__PURE__ */ r.jsx("button", { type: "button", disabled: !l, onClick: () => {
          i(s), u(null);
        }, children: "Reset" }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: !l, onClick: f, children: [
          /* @__PURE__ */ r.jsx(xn, { size: 14 }),
          " Apply"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ r.jsx("div", { className: "wf-code-view-editor", children: /* @__PURE__ */ r.jsx(
      cv,
      {
        ariaLabel: "Workflow JSON",
        document: { uri: "elsa://workflows/definition.json", language: "json", value: o },
        languageAdapter: dv,
        diagnostics: d,
        minHeight: "100%",
        theme: "studio",
        onChange: (h) => {
          i(h.value), c && u(null);
        }
      }
    ) })
  ] });
}
const hv = "String", pv = "WorkflowInstanceStorageDriver", Zu = "String", gv = "singleline";
function yv() {
  return typeof crypto < "u" && typeof crypto.randomUUID == "function" ? crypto.randomUUID() : `id-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
}
function mv(e, t) {
  const n = new Set(t);
  let o = 1, i = `${e}${o}`;
  for (; n.has(i); )
    o += 1, i = `${e}${o}`;
  return i;
}
function xv(e) {
  return {
    id: yv(),
    name: e.name,
    typeName: e.typeName?.trim() || hv,
    isArray: !1,
    value: null,
    storageDriverTypeName: e.storageDriverTypeName ?? pv
  };
}
function wv(e, t) {
  return { ...e, ...t };
}
function vv(e) {
  return {
    name: e.name,
    type: e.type?.trim() || Zu,
    displayName: e.name,
    description: "",
    category: "",
    isArray: !1,
    uiHint: gv,
    storageDriverType: e.storageDriverType ?? null,
    defaultValue: null,
    defaultSyntax: null,
    isReadOnly: null
  };
}
function bv(e, t) {
  return { ...e, ...t };
}
function Nv(e) {
  return {
    name: e.name,
    type: e.type?.trim() || Zu,
    displayName: e.name,
    description: "",
    category: "",
    isArray: !1
  };
}
function jv(e, t) {
  return { ...e, ...t };
}
function Sv(e) {
  const t = e.split(",")[0].trim();
  return (t.split(".").pop() ?? t).split("`")[0];
}
function Gu(e, t) {
  return e && !e.includes(",") && !e.includes(".") ? e : Sv(e || t);
}
function kv(e, t) {
  return Gu(e, t).replace(/StorageDriver$/, "");
}
function Ev(e) {
  return !!e && typeof e == "object" && !Array.isArray(e);
}
function nt(e, t) {
  for (const n of t) {
    const o = e[n];
    if (o != null && typeof o != "object") return String(o);
  }
  return "";
}
function Ju(e, t) {
  for (const n of t)
    if (n in e && e[n] != null) return e[n] === !0 || e[n] === "true";
  return !1;
}
const lc = ["name", "Name"], Cv = ["typeName", "TypeName"], Iv = ["value", "Value"], Av = ["storageDriverTypeName", "StorageDriverTypeName"], xi = ["name", "Name"], Qu = ["type", "Type", "typeName", "TypeName"], ed = ["isArray", "IsArray"], _v = ["storageDriverType", "StorageDriverType"], Dv = ["defaultValue", "DefaultValue"];
function Mv(e) {
  const [t, n] = q(null), [o, i] = q(null);
  te(() => {
    let c = !1;
    return Z0(e).then(
      (u) => {
        c || n(u);
      },
      () => {
        c || n([]);
      }
    ), G0(e).then(
      (u) => {
        c || i(u);
      },
      () => {
        c || i([]);
      }
    ), () => {
      c = !0;
    };
  }, [e]);
  const s = ue(
    () => t && t.length > 0 ? t.map((c) => ({
      value: c.typeName,
      label: Gu(c.displayName, c.typeName),
      group: c.category?.trim() || "Other"
    })) : null,
    [t]
  ), a = ue(
    () => o && o.length > 0 ? o.filter((c) => !c.deprecated).map((c) => ({
      value: c.typeName,
      label: kv(c.displayName, c.typeName)
    })) : null,
    [o]
  );
  return { typeOptions: s, storageOptions: a };
}
function ms(e) {
  return !e || e.length === 0 ? void 0 : (e.find((n) => /(^|\.)String$/i.test(n.value) || n.label.toLowerCase() === "string") ?? e[0]).value;
}
function xs(e, t, n) {
  return {
    add: () => {
      const o = mv(n.namePrefix, e.map((i) => nt(i, n.nameKeys)));
      t([...e, n.create(o)]);
    },
    update: (o, i) => t(e.map((s, a) => a === o ? n.patch(s, i) : s)),
    remove: (o) => t(e.filter((i, s) => s !== o))
  };
}
function co({ value: e, options: t, placeholder: n, allowEmpty: o, ariaLabel: i, onChange: s }) {
  if (!t)
    return /* @__PURE__ */ r.jsx(
      "input",
      {
        type: "text",
        "aria-label": i,
        value: e,
        placeholder: n,
        onChange: (l) => s(l.target.value)
      }
    );
  const a = e === "" || t.some((l) => l.value === e), c = Array.from(new Set(t.map((l) => l.group).filter((l) => !!l))), u = c.length > 0;
  return /* @__PURE__ */ r.jsxs("select", { "aria-label": i, value: e, onChange: (l) => s(l.target.value), children: [
    o ? /* @__PURE__ */ r.jsx("option", { value: "", children: n ?? "—" }) : null,
    a ? null : /* @__PURE__ */ r.jsx("option", { value: e, children: e }),
    u ? c.map((l) => /* @__PURE__ */ r.jsx("optgroup", { label: l, children: t.filter((d) => d.group === l).map((d) => /* @__PURE__ */ r.jsx("option", { value: d.value, children: d.label }, d.value)) }, l)) : t.map((l) => /* @__PURE__ */ r.jsx("option", { value: l.value, children: l.label }, l.value))
  ] });
}
function ws({ title: e, addLabel: t, emptyLabel: n, headers: o, isEmpty: i, onAdd: s, children: a }) {
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-properties-section-head", children: [
      /* @__PURE__ */ r.jsx("h3", { children: e }),
      /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-properties-add", onClick: s, children: [
        /* @__PURE__ */ r.jsx(so, { size: 14 }),
        " ",
        t
      ] })
    ] }),
    i ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: n }) : /* @__PURE__ */ r.jsxs("table", { className: "wf-properties-table", children: [
      /* @__PURE__ */ r.jsx("thead", { children: /* @__PURE__ */ r.jsxs("tr", { children: [
        o.map((c) => /* @__PURE__ */ r.jsx("th", { children: c }, c)),
        /* @__PURE__ */ r.jsx("th", { "aria-label": "Actions" })
      ] }) }),
      /* @__PURE__ */ r.jsx("tbody", { children: a })
    ] })
  ] });
}
function vs({ label: e, onRemove: t }) {
  return /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-properties-remove", "aria-label": e, title: e, onClick: t, children: /* @__PURE__ */ r.jsx(di, { size: 14 }) }) });
}
function Tv({ items: e, typeOptions: t, storageOptions: n, onChange: o }) {
  const { add: i, update: s, remove: a } = xs(e, o, {
    namePrefix: "Variable",
    nameKeys: lc,
    create: (c) => xv({ name: c, typeName: ms(t) }),
    patch: wv
  });
  return /* @__PURE__ */ r.jsx(
    ws,
    {
      title: "Variables",
      addLabel: "Add variable",
      emptyLabel: "No variables defined.",
      headers: ["Name", "Type", "Default", "Storage"],
      isEmpty: e.length === 0,
      onAdd: i,
      children: e.map((c, u) => {
        const l = nt(c, lc);
        return /* @__PURE__ */ r.jsxs("tr", { children: [
          /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx("input", { type: "text", "aria-label": "Variable name", value: l, onChange: (d) => s(u, { name: d.target.value }) }) }),
          /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            co,
            {
              ariaLabel: "Variable type",
              value: nt(c, Cv),
              options: t,
              placeholder: "Type",
              onChange: (d) => s(u, { typeName: d })
            }
          ) }),
          /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            "input",
            {
              type: "text",
              "aria-label": "Variable default value",
              value: nt(c, Iv),
              placeholder: "(empty)",
              onChange: (d) => s(u, { value: d.target.value === "" ? null : d.target.value })
            }
          ) }),
          /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            co,
            {
              ariaLabel: "Variable storage driver",
              value: nt(c, Av),
              options: n,
              placeholder: "—",
              allowEmpty: !0,
              onChange: (d) => s(u, { storageDriverTypeName: d || null })
            }
          ) }),
          /* @__PURE__ */ r.jsx(vs, { label: `Remove variable ${l || u + 1}`, onRemove: () => a(u) })
        ] }, u);
      })
    }
  );
}
function $v({ items: e, typeOptions: t, storageOptions: n, onChange: o }) {
  const { add: i, update: s, remove: a } = xs(e, o, {
    namePrefix: "Input",
    nameKeys: xi,
    create: (c) => vv({ name: c, type: ms(t) }),
    patch: bv
  });
  return /* @__PURE__ */ r.jsx(
    ws,
    {
      title: "Inputs",
      addLabel: "Add input",
      emptyLabel: "No inputs defined.",
      headers: ["Name", "Type", "Array", "Default", "Storage"],
      isEmpty: e.length === 0,
      onAdd: i,
      children: e.map((c, u) => {
        const l = nt(c, xi);
        return /* @__PURE__ */ r.jsxs("tr", { children: [
          /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx("input", { type: "text", "aria-label": "Input name", value: l, onChange: (d) => s(u, { name: d.target.value }) }) }),
          /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            co,
            {
              ariaLabel: "Input type",
              value: nt(c, Qu),
              options: t,
              placeholder: "Type",
              onChange: (d) => s(u, { type: d })
            }
          ) }),
          /* @__PURE__ */ r.jsx("td", { className: "wf-properties-cell-center", children: /* @__PURE__ */ r.jsx(
            "input",
            {
              type: "checkbox",
              "aria-label": "Input is array",
              checked: Ju(c, ed),
              onChange: (d) => s(u, { isArray: d.target.checked })
            }
          ) }),
          /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            "input",
            {
              type: "text",
              "aria-label": "Input default value",
              value: nt(c, Dv),
              placeholder: "(empty)",
              onChange: (d) => s(u, { defaultValue: d.target.value === "" ? null : d.target.value })
            }
          ) }),
          /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            co,
            {
              ariaLabel: "Input storage driver",
              value: nt(c, _v),
              options: n,
              placeholder: "—",
              allowEmpty: !0,
              onChange: (d) => s(u, { storageDriverType: d || null })
            }
          ) }),
          /* @__PURE__ */ r.jsx(vs, { label: `Remove input ${l || u + 1}`, onRemove: () => a(u) })
        ] }, u);
      })
    }
  );
}
function Pv({ items: e, typeOptions: t, onChange: n }) {
  const { add: o, update: i, remove: s } = xs(e, n, {
    namePrefix: "Output",
    nameKeys: xi,
    create: (a) => Nv({ name: a, type: ms(t) }),
    patch: jv
  });
  return /* @__PURE__ */ r.jsx(
    ws,
    {
      title: "Outputs",
      addLabel: "Add output",
      emptyLabel: "No outputs defined.",
      headers: ["Name", "Type", "Array"],
      isEmpty: e.length === 0,
      onAdd: o,
      children: e.map((a, c) => {
        const u = nt(a, xi);
        return /* @__PURE__ */ r.jsxs("tr", { children: [
          /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx("input", { type: "text", "aria-label": "Output name", value: u, onChange: (l) => i(c, { name: l.target.value }) }) }),
          /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            co,
            {
              ariaLabel: "Output type",
              value: nt(a, Qu),
              options: t,
              placeholder: "Type",
              onChange: (l) => i(c, { type: l })
            }
          ) }),
          /* @__PURE__ */ r.jsx("td", { className: "wf-properties-cell-center", children: /* @__PURE__ */ r.jsx(
            "input",
            {
              type: "checkbox",
              "aria-label": "Output is array",
              checked: Ju(a, ed),
              onChange: (l) => i(c, { isArray: l.target.checked })
            }
          ) }),
          /* @__PURE__ */ r.jsx(vs, { label: `Remove output ${u || c + 1}`, onRemove: () => s(c) })
        ] }, c);
      })
    }
  );
}
function wr(e) {
  return (e ?? []).filter(Ev);
}
function zv({ details: e, draft: t, context: n, onStateChange: o }) {
  const { typeOptions: i, storageOptions: s } = Mv(n), a = wr(t.state.variables), c = wr(t.state.inputs), u = wr(t.state.outputs), l = e?.versions ?? [], d = e?.definition.description?.trim();
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-properties-view", children: [
    /* @__PURE__ */ r.jsxs("section", { className: "wf-properties-section", children: [
      /* @__PURE__ */ r.jsx("h3", { children: "Information" }),
      /* @__PURE__ */ r.jsxs("dl", { className: "wf-properties-info", children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Name" }),
        /* @__PURE__ */ r.jsx("dd", { children: e?.definition.name ?? "—" }),
        /* @__PURE__ */ r.jsx("dt", { children: "Description" }),
        /* @__PURE__ */ r.jsx("dd", { children: d || /* @__PURE__ */ r.jsx("span", { className: "wf-muted", children: "No description" }) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Definition ID" }),
        /* @__PURE__ */ r.jsx("dd", { children: /* @__PURE__ */ r.jsx("code", { children: t.definitionId }) })
      ] })
    ] }),
    /* @__PURE__ */ r.jsx(
      Tv,
      {
        items: a,
        typeOptions: i,
        storageOptions: s,
        onChange: (f) => o((h) => ({ ...h, variables: f }))
      }
    ),
    /* @__PURE__ */ r.jsx(
      $v,
      {
        items: c,
        typeOptions: i,
        storageOptions: s,
        onChange: (f) => o((h) => ({ ...h, inputs: f }))
      }
    ),
    /* @__PURE__ */ r.jsx(
      Pv,
      {
        items: u,
        typeOptions: i,
        onChange: (f) => o((h) => ({ ...h, outputs: f }))
      }
    ),
    /* @__PURE__ */ r.jsxs("section", { className: "wf-properties-section", children: [
      /* @__PURE__ */ r.jsx("h3", { children: "Versions" }),
      l.length === 0 ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No published versions yet." }) : /* @__PURE__ */ r.jsx("ul", { className: "wf-properties-versions", children: l.map((f) => /* @__PURE__ */ r.jsxs("li", { children: [
        /* @__PURE__ */ r.jsxs("span", { className: "wf-properties-version-tag", children: [
          "v",
          f.version
        ] }),
        /* @__PURE__ */ r.jsx("time", { children: it(f.createdAt) })
      ] }, f.id)) })
    ] })
  ] });
}
function Rv({ activities: e, activityCatalog: t, selectedEvidenceId: n = null, onSelectEvidence: o }) {
  const i = ue(
    () => new Map(t.map((a) => [a.activityTypeKey, a])),
    [t]
  ), s = ue(() => Vv(e), [e]);
  return s.length === 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty wf-timeline-empty", children: "No activity executions recorded yet." }) : /* @__PURE__ */ r.jsx("ol", { className: "wf-timeline", "aria-label": "Execution timeline", children: s.map((a) => {
    const c = i.get(a.activityType), u = gi(c), l = c ? Se(c) : mi(a.activityType) ?? a.activityType, d = mi(a.activityType) ?? a.activityType, f = Ov(a.startedAt ?? a.scheduledAt), h = Yu(a.startedAt, a.completedAt);
    return /* @__PURE__ */ r.jsx("li", { children: /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-timeline-entry",
        "data-selected": a.activityExecutionId === n,
        onClick: () => o?.(a.activityExecutionId),
        children: [
          /* @__PURE__ */ r.jsx("span", { className: "wf-timeline-icon wf-activity-icon", "data-icon": u, "aria-hidden": "true", children: ys(u) }),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-timeline-body", children: [
            /* @__PURE__ */ r.jsx("strong", { title: l, children: l }),
            /* @__PURE__ */ r.jsx("small", { title: d, children: d })
          ] }),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-timeline-meta", children: [
            f ? /* @__PURE__ */ r.jsx("time", { children: f }) : null,
            h ? /* @__PURE__ */ r.jsxs("small", { children: [
              "took ",
              h
            ] }) : null
          ] }),
          /* @__PURE__ */ r.jsx(Lv, { status: a.status })
        ]
      }
    ) }, a.activityExecutionId);
  }) });
}
function Lv({ status: e }) {
  const t = e.toLowerCase(), n = t === "completed" ? "OK" : e;
  return /* @__PURE__ */ r.jsx("span", { className: "wf-status-badge wf-timeline-pill", "data-status": t, children: n });
}
function Vv(e) {
  return e.map((t, n) => ({ activity: t, index: n })).sort((t, n) => uc(t.activity) - uc(n.activity) || t.index - n.index).map((t) => t.activity);
}
function uc(e) {
  const t = e.startedAt ?? e.scheduledAt, n = t ? Date.parse(t) : Number.NaN;
  return Number.isNaN(n) ? Number.MAX_SAFE_INTEGER : n;
}
function Ov(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? "" : t.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: !1 });
}
function Hv({ context: e }) {
  const t = _0(e), n = D0(e), o = M0(e), i = t.data ?? null, s = n.data ?? null, a = t.isLoading || n.isLoading, c = o.isPending, [u, l] = q(() => ec(i)), [d, f] = q(""), [h, p] = q(null);
  te(() => {
    l(ec(i));
  }, [i]);
  const g = ue(() => sw(s), [s]), v = ue(() => aw(s), [s]), w = s?.sets ?? [], m = ue(() => {
    const T = d.trim().toLowerCase();
    return T ? g.filter(
      (L) => pi(L).toLowerCase().includes(T) || (L.activityTypeKey ?? "").toLowerCase().includes(T)
    ) : g;
  }, [g, d]), k = new Set(u.activityTypes), y = new Set(u.sets), b = g.filter((T) => zt(T.state) === "BlockedByHostBaseline").length, S = g.filter((T) => zt(T.state) === "HiddenByManagementSettings").length, E = o.error ?? t.error ?? n.error, j = E instanceof Error ? E.message : E ? "Activity availability could not be loaded." : null, _ = (T) => l((L) => ({ ...L, mode: T })), M = (T) => l((L) => ({ ...L, activityTypes: tc(L.activityTypes, T) })), F = (T) => l((L) => ({ ...L, sets: tc(L.sets, T) })), D = () => {
    p(null), o.mutate(
      {
        scope: i?.scope ?? "host-default",
        mode: iw(u.mode),
        rules: { activityTypes: u.activityTypes, sets: u.sets }
      },
      { onSuccess: () => p("Activity availability saved.") }
    );
  };
  return /* @__PURE__ */ r.jsxs("div", { className: "availability-shell", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "availability-header", children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsxs("h2", { children: [
          /* @__PURE__ */ r.jsx(p0, { size: 18 }),
          " Activity availability"
        ] }),
        /* @__PURE__ */ r.jsx("p", { children: "Control which activities can be added to new workflows. Host baseline rules always take precedence." })
      ] }),
      /* @__PURE__ */ r.jsxs("button", { type: "button", className: "availability-save", onClick: D, disabled: a || c, children: [
        /* @__PURE__ */ r.jsx(Iu, { size: 15 }),
        c ? "Saving…" : "Save"
      ] })
    ] }),
    j && /* @__PURE__ */ r.jsx("div", { className: "availability-error", children: j }),
    h && !j && /* @__PURE__ */ r.jsx("div", { className: "availability-status", children: h }),
    /* @__PURE__ */ r.jsxs("div", { className: "availability-mode", role: "group", "aria-label": "Activity availability mode", children: [
      /* @__PURE__ */ r.jsxs("button", { type: "button", className: u.mode === "AllExcept" ? "active" : "", onClick: () => _("AllExcept"), disabled: a || c, children: [
        /* @__PURE__ */ r.jsx(Ya, { size: 15 }),
        /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx("strong", { children: "All except" }),
          /* @__PURE__ */ r.jsx("em", { children: "Show everything except the selected activities" })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("button", { type: "button", className: u.mode === "Only" ? "active" : "", onClick: () => _("Only"), disabled: a || c, children: [
        /* @__PURE__ */ r.jsx(Ga, { size: 15 }),
        /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx("strong", { children: "Only" }),
          /* @__PURE__ */ r.jsx("em", { children: "Show only the selected activities" })
        ] })
      ] })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "availability-counts", children: [
      /* @__PURE__ */ r.jsxs("span", { children: [
        /* @__PURE__ */ r.jsx(Ga, { size: 14 }),
        " ",
        b,
        " host blocked"
      ] }),
      /* @__PURE__ */ r.jsxs("span", { children: [
        /* @__PURE__ */ r.jsx(Ya, { size: 14 }),
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
          /* @__PURE__ */ r.jsx(ds, { size: 15 }),
          " Sets"
        ] }),
        /* @__PURE__ */ r.jsx("div", { className: "availability-set-list", children: w.map((T) => /* @__PURE__ */ r.jsxs("label", { className: "availability-set-option", children: [
          /* @__PURE__ */ r.jsx("input", { type: "checkbox", checked: y.has(T.name), disabled: a || c, onChange: () => F(T.name) }),
          /* @__PURE__ */ r.jsx("span", { children: T.name }),
          /* @__PURE__ */ r.jsx("code", { children: (T.activityTypeKeys ?? []).length })
        ] }, T.name)) })
      ] }),
      /* @__PURE__ */ r.jsxs("section", { className: "availability-section availability-section-grow", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "availability-section-head", children: [
          /* @__PURE__ */ r.jsxs("h3", { children: [
            /* @__PURE__ */ r.jsx(c0, { size: 15 }),
            " Activities"
          ] }),
          /* @__PURE__ */ r.jsxs("div", { className: "availability-search", children: [
            /* @__PURE__ */ r.jsx(Ti, { size: 14 }),
            /* @__PURE__ */ r.jsx("input", { type: "search", value: d, placeholder: "Filter activities…", onChange: (T) => f(T.target.value) })
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("div", { className: "availability-activity-list", children: [
          a && g.length === 0 && /* @__PURE__ */ r.jsx("p", { className: "availability-muted", children: "Loading availability…" }),
          !a && g.length === 0 && /* @__PURE__ */ r.jsx("p", { className: "availability-muted", children: "No availability diagnostics reported." }),
          !a && g.length > 0 && m.length === 0 && /* @__PURE__ */ r.jsx("p", { className: "availability-muted", children: "No activities match the filter." }),
          m.map((T) => {
            const N = zt(T.state) === "BlockedByHostBaseline", A = T.activityTypeKey ?? T.activityDefinitionId ?? "";
            return /* @__PURE__ */ r.jsxs("label", { className: `availability-activity-option ${N ? "disabled" : ""}`, children: [
              /* @__PURE__ */ r.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: k.has(A),
                  disabled: a || c || N,
                  onChange: () => M(A)
                }
              ),
              /* @__PURE__ */ r.jsxs("span", { className: "availability-activity-main", children: [
                /* @__PURE__ */ r.jsx("strong", { children: pi(T) }),
                /* @__PURE__ */ r.jsx("code", { children: T.activityTypeKey })
              ] }),
              /* @__PURE__ */ r.jsx("em", { className: `availability-state ${tw(T.state)}`, children: hi(T.state) })
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
const dc = "elsa-studio:apply-workflow-graph-operation-batch", fc = "elsa-studio:undo-workflow-graph-operation-batch", Bv = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function Fv(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const o = Gv(e), i = nd(o.state.rootActivity), s = /* @__PURE__ */ new Map(), a = /* @__PURE__ */ new Map(), c = [];
  for (const u of t.operations) {
    const l = Zv(u.kind), d = u.parameters ?? {};
    if (l === "add-activity") {
      const f = Pe(d.activityId) ?? u.temporaryReferences?.[0], h = Yv(f ?? Pe(d.displayName) ?? Pe(d.activityType) ?? "weaver-activity", i), p = Wv(u, h, n);
      a.set(h, p), c.push(h), f && s.set(f, h), o.state.rootActivity && Kv(o.state.rootActivity, p);
      const g = Rt(d.position) ? Or(d.position, { x: 280, y: 160 }) : null;
      g && (o.layout = hc(o.layout, h, g));
      continue;
    }
    if (l === "set-root") {
      const f = vr(o, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown root activity.");
      o.state.rootActivity = f;
      continue;
    }
    if (l === "set-designer-position") {
      const f = Ft(d.activityId, s);
      if (!f || !bs(o.state.rootActivity, f)) throw new Error("Weaver batch referenced an unknown activity position.");
      o.layout = hc(o.layout, f, Or(d, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const f = vr(o, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity property target.");
      Uv(f, Pe(d.propertyName) ?? "Value", d.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const f = vr(o, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity update target.");
      const h = Rt(d.patch) ? d.patch : d;
      Object.assign(f, h);
      continue;
    }
    if (l === "remove-activity") {
      const f = Ft(d.activityId, s);
      if (!f) throw new Error("Weaver batch referenced an unknown activity remove target.");
      o.state.rootActivity = td(o.state.rootActivity, f), o.layout = o.layout.filter((h) => h.nodeId !== f);
      continue;
    }
    if (l === "connect-activities") {
      qv(o, d, s);
      continue;
    }
    if (l === "disconnect-activities") {
      Xv(o, d, s);
      continue;
    }
    throw new Error(`Weaver batch operation '${String(u.kind || "unknown")}' is not supported by this designer apply path.`);
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
function Wv(e, t, n) {
  const o = e.parameters ?? {}, i = Pe(o.activityVersionId) ?? Pe(o.activityType) ?? "Elsa.Workflows.Activity", s = n.find((a) => a.activityVersionId === i || a.activityTypeKey === i || a.displayName === Pe(o.displayName));
  return s ? Rr(s, t) : {
    nodeId: t,
    activityVersionId: s?.activityVersionId ?? i,
    inputs: [],
    outputs: [],
    ...Pe(o.displayName) ? { displayName: Pe(o.displayName) } : {},
    designer: { position: Or(o.position, { x: 280, y: 160 }) }
  };
}
function Kv(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = Ns(e);
  n && !n.some((o) => o.nodeId === t.nodeId) && n.push(t);
}
function qv(e, t, n) {
  const o = e.state.rootActivity;
  if (!o?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const i = Ft(t.sourceActivityId ?? t.sourceId ?? t.from, n), s = Ft(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!i || !s) throw new Error("Weaver batch connection is missing source or target activity.");
  const a = o.structure.payload, c = Array.isArray(a.connections) ? a.connections : [], u = Pe(t.connectionId) ?? `flow-${i}-${s}`;
  a.connections = [
    ...c.filter((l) => !Rt(l) || l.id !== u),
    {
      id: u,
      source: { nodeId: i, port: Pe(t.outcome) ?? Pe(t.sourcePort) ?? "Done" },
      target: { nodeId: s }
    }
  ];
}
function Xv(e, t, n) {
  const o = e.state.rootActivity, i = o?.structure?.payload.connections;
  if (!Array.isArray(i)) return;
  const s = Pe(t.connectionId), a = Ft(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = Ft(t.targetActivityId ?? t.targetId ?? t.to, n);
  o.structure.payload.connections = i.filter((u) => {
    if (!Rt(u)) return !0;
    if (s && u.id === s) return !1;
    const l = Rt(u.source) ? u.source.nodeId : void 0, d = Rt(u.target) ? u.target.nodeId : void 0;
    return l !== a || d !== c;
  });
}
function Uv(e, t, n) {
  e[Bu(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: "Literal", value: n }
  };
}
function vr(e, t, n, o) {
  const i = Ft(t, n);
  return i ? bs(e.state.rootActivity, i) ?? o.get(i) ?? null : null;
}
function Ft(e, t) {
  const n = Pe(e);
  return n ? t.get(n) ?? n : null;
}
function bs(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of od(e)) {
    const o = bs(n, t);
    if (o) return o;
  }
  return null;
}
function td(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = Ns(e);
  if (n) {
    const o = n.map((i) => td(i, t)).filter((i) => !!i);
    n.splice(0, n.length, ...o);
  }
  return e;
}
function nd(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of od(e)) nd(n, t);
  return t;
}
function od(e) {
  return Ns(e) ?? [];
}
function Ns(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function hc(e, t, n) {
  return [
    ...e.filter((o) => o.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function Or(e, t) {
  const n = Rt(e) ? e : {}, o = Number(n.x), i = Number(n.y);
  return {
    x: Number.isFinite(o) ? Math.max(40, Math.round(o)) : t.x,
    y: Number.isFinite(i) ? Math.max(40, Math.round(i)) : t.y
  };
}
function Yv(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let o = n, i = 2;
  for (; t.has(o); )
    o = `${n}-${i}`, i += 1;
  return t.add(o), o;
}
function Zv(e) {
  return typeof e == "number" ? Bv[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Pe(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function Gv(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function Rt(e) {
  return typeof e == "object" && e !== null;
}
function js({ rows: e = 5 }) {
  return /* @__PURE__ */ r.jsx("div", { className: "wf-grid", "aria-busy": "true", "aria-label": "Loading", children: Array.from({ length: e }).map((t, n) => /* @__PURE__ */ r.jsx("div", { className: "wf-skeleton wf-skeleton-row", style: { width: `${90 - n % 3 * 12}%` } }, n)) });
}
function Ss({ icon: e, title: t, description: n, action: o }) {
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-empty-state", role: "status", children: [
    /* @__PURE__ */ r.jsx("div", { className: "wf-empty-state-icon", "aria-hidden": !0, children: e ?? /* @__PURE__ */ r.jsx(Mi, { size: 22 }) }),
    /* @__PURE__ */ r.jsx("h3", { children: t }),
    n ? /* @__PURE__ */ r.jsx("p", { children: n }) : null,
    o ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty-state-action", children: o }) : null
  ] });
}
function xo({ message: e, title: t = "Something went wrong" }) {
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-error-card", role: "alert", children: [
    /* @__PURE__ */ r.jsx(kt, { size: 18 }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-error-card-body", children: [
      /* @__PURE__ */ r.jsx("strong", { children: t }),
      /* @__PURE__ */ r.jsx("span", { children: e || "Please try again, or check that the Elsa server is reachable." })
    ] })
  ] });
}
const id = { workflowActivity: Lb }, rd = { workflow: Ob }, pc = "application/x-elsa-activity-version-id", Jv = 6, Qv = 1200, eb = 250, tb = [10, 25, 50], nb = 10, gc = "elsa-studio-workflow-palette-width", yc = "elsa-studio-workflow-inspector-width", mc = "elsa-studio-workflow-palette-collapsed", xc = "elsa-studio-workflow-inspector-collapsed", sd = "elsa-studio-workflow-side-panel-maximized", Ln = 180, Vn = 460, ob = 260, On = 260, Hn = 560, ib = 320, wc = 42, Ho = 16, ad = ct.createContext(null), cd = ct.createContext(null);
let Hr;
function t1(e) {
  Hr = e.dialogs, e.featureAreas.add({
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
        component: () => /* @__PURE__ */ r.jsx(rb, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list() })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ r.jsx(sb, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => /* @__PURE__ */ r.jsx(ab, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => /* @__PURE__ */ r.jsx(cb, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => /* @__PURE__ */ r.jsx(Hv, { context: e.backend })
      }
    ]
  });
}
function rb({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: o,
  workflowDesignerPanels: i
}) {
  const [s, a] = q(vc);
  te(() => {
    const u = () => a(vc());
    return window.addEventListener("popstate", u), () => window.removeEventListener("popstate", u);
  }, []);
  const c = (u) => {
    const l = u ? `/workflows/definitions?definition=${encodeURIComponent(u)}` : "/workflows/definitions";
    window.history.pushState({}, "", l), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return s ? /* @__PURE__ */ r.jsx(Rb, { context: e, definitionId: s, ai: t, propertyEditors: n, expressionEditors: o, workflowDesignerPanels: i, onBack: () => c(null) }) : /* @__PURE__ */ r.jsx(Li, { title: "Definitions", children: /* @__PURE__ */ r.jsx(ub, { context: e, ai: t, onOpen: c }) });
}
function sb({ context: e, ai: t }) {
  const [n, o] = q(bc);
  te(() => {
    const s = () => o(bc());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const i = fe((s) => {
    const a = s?.trim() ?? "", c = new URL(window.location.href);
    a ? c.searchParams.set("definition", a) : c.searchParams.delete("definition"), o(a || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ r.jsx(Li, { title: "Executables", children: /* @__PURE__ */ r.jsx(fb, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: i }) });
}
function ab({ context: e, ai: t }) {
  return /* @__PURE__ */ r.jsx(Li, { title: "Runs", children: /* @__PURE__ */ r.jsx(gb, { context: e, ai: t }) });
}
function cb({ context: e, ai: t }) {
  const n = lb();
  return /* @__PURE__ */ r.jsx(Li, { title: "Run", children: /* @__PURE__ */ r.jsx(yb, { context: e, ai: t, workflowExecutionId: n }) });
}
function Li({ title: e, children: t }) {
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ r.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ r.jsxs("div", { children: [
      /* @__PURE__ */ r.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ r.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function vc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function bc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function lb() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function ub({ context: e, ai: t, onOpen: n }) {
  const [o, i] = q(""), [s, a] = q("active"), [c, u] = q(1), [l, d] = q(nb), [f, h] = q("loading"), [p, g] = q(""), [v, w] = q(""), [m, k] = q([]), [y, b] = q(0), [S, E] = q(() => /* @__PURE__ */ new Set()), [j, _] = q(null), [M, F] = q(!1), [D, T] = q([]), [L, N] = q("idle"), A = re(null), I = ue(() => m.map((H) => H.id), [m]), $ = Wt(t, "weaver.workflows.suggest-create-metadata"), z = Wt(t, "weaver.workflows.explain-definition"), P = I.filter((H) => S.has(H)).length, B = I.length > 0 && P === I.length, K = fe(async () => {
    h("loading"), g("");
    try {
      const H = await T0(e, { search: o, state: s, page: c, pageSize: l }), Q = typeof H.totalCount == "number", ge = H.totalCount ?? H.definitions.length, ve = dd(ge, l);
      if (ge > 0 && c > ve) {
        u(ve);
        return;
      }
      k(Q ? H.definitions : Sb(H.definitions, c, l)), b(ge), h("ready");
    } catch (H) {
      g(H instanceof Error ? H.message : String(H)), h("failed");
    }
  }, [e, o, s, c, l]);
  te(() => {
    K();
  }, [K]), te(() => {
    A.current && (A.current.indeterminate = P > 0 && !B);
  }, [B, P]);
  const O = fe(async () => {
    if (!(L === "loading" || L === "ready")) {
      N("loading");
      try {
        const H = await fs(e);
        T(H.activities ?? []), N("ready");
      } catch (H) {
        N("failed"), g(H instanceof Error ? H.message : String(H));
      }
    }
  }, [L, e]), Z = () => {
    g(""), w(""), _({ name: "", description: "", rootKind: "flowchart" }), O();
  }, X = async () => {
    if (j?.name.trim()) {
      F(!0), g(""), w("");
      try {
        const H = await z0(e, {
          name: j.name.trim(),
          description: j.description.trim() || null,
          rootKind: j.rootKind,
          rootActivityVersionId: Cb(j, D)
        });
        _(null), n(H.definition.id);
      } catch (H) {
        g(H instanceof Error ? H.message : String(H));
      } finally {
        F(!1);
      }
    }
  }, oe = (H) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(H)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, se = async () => {
    if (m.length === 1 && c > 1) {
      u(c - 1);
      return;
    }
    await K();
  }, G = () => E(/* @__PURE__ */ new Set()), V = (H, Q) => {
    E((ge) => {
      const ve = new Set(ge);
      return Q ? ve.add(H) : ve.delete(H), ve;
    });
  }, U = (H) => {
    E((Q) => {
      const ge = new Set(Q);
      for (const ve of I)
        H ? ge.add(ve) : ge.delete(ve);
      return ge;
    });
  }, ae = (H) => {
    a(H), u(1), G();
  }, ce = (H) => {
    i(H), u(1), G();
  }, J = async (H) => {
    if (await Hr.confirm({ message: `Delete workflow definition "${H.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" })) {
      w(""), g("");
      try {
        await R0(e, H.id), V(H.id, !1), w(`Deleted ${H.name}`), await se();
      } catch (Q) {
        g(Q instanceof Error ? Q.message : String(Q));
      }
    }
  }, ee = async (H) => {
    w(""), g("");
    try {
      await L0(e, H.id), V(H.id, !1), w(`Restored ${H.name}`), await se();
    } catch (Q) {
      g(Q instanceof Error ? Q.message : String(Q));
    }
  }, de = async (H) => {
    if (await Hr.confirm({ message: `Permanently delete workflow definition "${H.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) {
      w(""), g("");
      try {
        await V0(e, H.id), V(H.id, !1), w(`Permanently deleted ${H.name}`), await se();
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
        /* @__PURE__ */ r.jsx(Ti, { size: 15 }),
        /* @__PURE__ */ r.jsx("input", { value: o, onChange: (H) => ce(H.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => {
        K();
      }, children: "Refresh" }),
      /* @__PURE__ */ r.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ r.jsxs("button", { type: "button", title: "Create workflow", onClick: Z, children: [
        /* @__PURE__ */ r.jsx(so, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ r.jsx(xo, { message: p, title: "Couldn't load workflow definitions" }) : null,
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
    f === "loading" ? /* @__PURE__ */ r.jsx(js, {}) : null,
    f === "ready" && m.length === 0 ? /* @__PURE__ */ r.jsx(
      Ss,
      {
        icon: /* @__PURE__ */ r.jsx(Cu, { size: 22 }),
        title: `No ${s} workflow definitions`,
        description: "Create a workflow to start designing automation, or adjust your filters to see more.",
        action: /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-link-button", onClick: Z, children: [
          /* @__PURE__ */ r.jsx(so, { size: 15 }),
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
              checked: B,
              onChange: (H) => U(H.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ r.jsx("span", { children: "Name" }),
          /* @__PURE__ */ r.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ r.jsx("span", { children: s === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ r.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ r.jsx("span", { children: "Actions" })
        ] }),
        m.map((H) => /* @__PURE__ */ r.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${H.name}`,
            "aria-selected": S.has(H.id),
            tabIndex: 0,
            onClick: () => n(H.id),
            onKeyDown: (Q) => {
              Q.currentTarget === Q.target && (Q.key !== "Enter" && Q.key !== " " || (Q.preventDefault(), n(H.id)));
            },
            children: [
              /* @__PURE__ */ r.jsx("label", { className: "wf-row-select", onClick: (Q) => Q.stopPropagation(), children: /* @__PURE__ */ r.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: S.has(H.id),
                  onChange: (Q) => V(H.id, Q.target.checked),
                  "aria-label": `Select workflow definition ${H.name}`
                }
              ) }),
              /* @__PURE__ */ r.jsxs("span", { children: [
                /* @__PURE__ */ r.jsx("strong", { children: H.name }),
                /* @__PURE__ */ r.jsx("small", { children: H.description || H.id })
              ] }),
              /* @__PURE__ */ r.jsx("span", { children: H.latestVersion ?? "No version" }),
              /* @__PURE__ */ r.jsx("span", { children: s === "deleted" ? it(H.deletedAt) : H.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ r.jsx("span", { children: it(H.lastModifiedAt) }),
              /* @__PURE__ */ r.jsx("span", { className: "wf-row-actions", onClick: (Q) => Q.stopPropagation(), children: s === "active" ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
                /* @__PURE__ */ r.jsx("button", { type: "button", onClick: (Q) => {
                  Q.stopPropagation(), n(H.id);
                }, children: "Open" }),
                /* @__PURE__ */ r.jsx("button", { type: "button", onClick: (Q) => {
                  Q.stopPropagation(), oe(H.id);
                }, children: "Artifacts" }),
                z ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => St(t, z, H), children: [
                  /* @__PURE__ */ r.jsx(jt, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ r.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  J(H);
                }, children: [
                  /* @__PURE__ */ r.jsx(di, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
                /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
                  ee(H);
                }, children: [
                  /* @__PURE__ */ r.jsx(us, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ r.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  de(H);
                }, children: [
                  /* @__PURE__ */ r.jsx(di, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          H.id
        ))
      ] }),
      /* @__PURE__ */ r.jsx(
        jb,
        {
          page: c,
          pageSize: l,
          totalCount: y,
          onPageChange: u,
          onPageSizeChange: (H) => {
            d(H), u(1);
          }
        }
      )
    ] }) : null,
    j ? /* @__PURE__ */ r.jsx(
      db,
      {
        draft: j,
        activities: D,
        catalogState: L,
        creating: M,
        suggestMetadataAction: $,
        onSuggestMetadata: $ ? () => St(t, $, { draft: j, activities: D }) : void 0,
        onChange: (H) => _(H),
        onClose: () => _(null),
        onSubmit: X
      }
    ) : null
  ] });
}
function db({ draft: e, activities: t, catalogState: n, creating: o, suggestMetadataAction: i, onSuggestMetadata: s, onChange: a, onClose: c, onSubmit: u }) {
  const l = ue(() => kb(t), [t]), d = Eb(e, t), f = (h) => {
    if (h.startsWith("kind:")) {
      a({ ...e, rootKind: h.slice(5), rootActivityVersionId: null });
      return;
    }
    const p = t.find((g) => g.activityVersionId === h);
    a({
      ...e,
      rootKind: fd(p) ?? e.rootKind,
      rootActivityVersionId: h
    });
  };
  return /* @__PURE__ */ r.jsx("div", { className: "wf-dialog-backdrop", role: "presentation", children: /* @__PURE__ */ r.jsx("section", { className: "wf-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "workflow-create-title", children: /* @__PURE__ */ r.jsxs(
    "form",
    {
      onSubmit: (h) => {
        h.preventDefault(), u();
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
              value: d,
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
function fb({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: o }) {
  const [i, s] = q("loading"), [a, c] = q(""), [u, l] = q(""), [d, f] = q(null), [h, p] = q([]), g = n?.trim().toLowerCase() ?? "", v = ue(
    () => g ? h.filter((j) => Ab(j, g)) : h,
    [g, h]
  ), w = ue(
    () => Array.from(new Set(h.flatMap((j) => [
      j.definitionId,
      j.definitionVersionId,
      j.sourceId
    ]).filter((j) => !!j))).sort((j, _) => j.localeCompare(_)),
    [h]
  ), m = Wt(t, "weaver.workflows.explain-executable"), k = fe(async () => {
    s("loading"), c("");
    try {
      p(await Tu(e)), s("ready");
    } catch (j) {
      c(j instanceof Error ? j.message : String(j)), s("failed");
    }
  }, [e]);
  te(() => {
    k();
  }, [k]);
  const y = async (j) => {
    l(""), f(null), c("");
    try {
      const _ = await Mu(e, j.artifactId), M = xd(_);
      f({ artifactId: j.artifactId, workflowExecutionId: M }), l(`Started ${j.artifactId}`);
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
        /* @__PURE__ */ r.jsx(Ti, { size: 14 }),
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
        /* @__PURE__ */ r.jsx(Au, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    i === "failed" ? /* @__PURE__ */ r.jsx(xo, { message: a }) : null,
    u ? /* @__PURE__ */ r.jsx(ld, { status: u, run: d }) : null,
    i === "loading" ? /* @__PURE__ */ r.jsx(js, {}) : null,
    i === "ready" && v.length === 0 ? /* @__PURE__ */ r.jsx(
      Ss,
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
        /* @__PURE__ */ r.jsx(hb, { executable: j, onCopied: S, onCopyFailed: E }),
        /* @__PURE__ */ r.jsx("span", { children: yd(j) }),
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
function hb({ executable: e, onCopied: t, onCopyFailed: n }) {
  const o = e.sourceId || e.definitionVersionId || e.definitionId, i = e.sourceVersion;
  return /* @__PURE__ */ r.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ r.jsx("span", { className: "wf-source-kind", children: md(e.sourceKind) }),
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
function ld({ status: e, run: t, compact: n = !1 }) {
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
      await Mb(e), o(n);
    } catch {
      i(n);
    }
  };
  return /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (a) => {
    s(a);
  }, children: /* @__PURE__ */ r.jsx(d0, { size: 12 }) });
}
function pb({ context: e, ai: t, definitionId: n, publishedArtifactId: o }) {
  const [i, s] = q("loading"), [a, c] = q(""), [u, l] = q(""), [d, f] = q(null), [h, p] = q([]), g = Wt(t, "weaver.workflows.explain-executable"), v = fe(async () => {
    s("loading"), c("");
    try {
      const S = await Tu(e);
      p(S.filter((E) => _b(E, n)).sort(Db)), s("ready");
    } catch (S) {
      c(S instanceof Error ? S.message : String(S)), p([]), s("failed");
    }
  }, [e, n]);
  te(() => {
    v();
  }, [v, o]);
  const w = async (S) => {
    l(""), f(null), c("");
    try {
      const E = await Mu(e, S.artifactId);
      f({ artifactId: S.artifactId, workflowExecutionId: xd(E) }), l(`Started ${S.artifactId}`);
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
        /* @__PURE__ */ r.jsx(us, { size: 13 }),
        " Refresh"
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: k, children: "Open list" })
    ] }),
    i === "failed" ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ r.jsx(kt, { size: 14 }),
      " ",
      a
    ] }) : null,
    u ? /* @__PURE__ */ r.jsx(ld, { status: u, run: d, compact: !0 }) : null,
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
            md(S.sourceKind),
            " ",
            S.sourceVersion ? `v${S.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("div", { children: [
          /* @__PURE__ */ r.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ r.jsx("dd", { children: yd(S) })
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
function gb({ context: e }) {
  const [t, n] = q("loading"), [o, i] = q(""), [s, a] = q(""), [c, u] = q(""), [l, d] = q([]), f = fe(async () => {
    n("loading"), i("");
    try {
      const p = await W0(e, {
        status: s || void 0,
        runKind: c || void 0,
        take: 100
      });
      d(p), n("ready");
    } catch (p) {
      i(p instanceof Error ? p.message : String(p)), d([]), n("failed");
    }
  }, [e, c, s]);
  te(() => {
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
        /* @__PURE__ */ r.jsxs("select", { "aria-label": "Run Kind", value: c, onChange: (p) => u(p.target.value), children: [
          /* @__PURE__ */ r.jsx("option", { value: "", children: "All kinds" }),
          /* @__PURE__ */ r.jsx("option", { value: "TestRun", children: "Test Run" }),
          /* @__PURE__ */ r.jsx("option", { value: "PublishedRun", children: "Published Run" }),
          /* @__PURE__ */ r.jsx("option", { value: "BackgroundWeaverRun", children: "Background Weaver Run" }),
          /* @__PURE__ */ r.jsx("option", { value: "Unknown", children: "Unknown / legacy" })
        ] })
      ] })
    ] }),
    t === "failed" ? /* @__PURE__ */ r.jsx(xo, { message: o }) : null,
    t === "loading" ? /* @__PURE__ */ r.jsx(js, {}) : null,
    t === "ready" && l.length === 0 ? /* @__PURE__ */ r.jsx(
      Ss,
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
            /* @__PURE__ */ r.jsx("span", { children: ud(p.runKind) }),
            /* @__PURE__ */ r.jsx("span", { children: /* @__PURE__ */ r.jsx(wo, { status: p.status, subStatus: p.subStatus }) }),
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
            /* @__PURE__ */ r.jsx("span", { children: Yu(p.startedAt ?? p.createdAt, p.completedAt ?? p.updatedAt) })
          ]
        },
        p.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function yb({ context: e, ai: t, workflowExecutionId: n }) {
  const [o, i] = q("loading"), [s, a] = q(""), [c, u] = q(null), [l, d] = q(null), f = Wt(t, "weaver.workflows.explain-instance"), h = fe(async () => {
    if (!n) {
      a("No workflow execution id was provided."), i("failed");
      return;
    }
    i("loading"), a("");
    try {
      const g = await K0(e, n), [v, w] = await Promise.all([
        P0(e, g.instance.definitionVersionId).then(
          (m) => ({ definitionVersion: m, error: "" }),
          (m) => ({ definitionVersion: null, error: m instanceof Error ? m.message : String(m) })
        ),
        fs(e)
      ]);
      u({
        details: g,
        definitionVersion: v.definitionVersion,
        definitionVersionError: v.error,
        activityCatalog: w.activities
      }), d(null), i("ready");
    } catch (g) {
      u(null), a(Gb(g, n)), i("failed");
    }
  }, [e, n]);
  te(() => {
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
        /* @__PURE__ */ r.jsx(us, { size: 14 }),
        " Refresh"
      ] }),
      c && f ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => St(t, f, c.details), children: [
        /* @__PURE__ */ r.jsx(jt, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    o === "loading" ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Loading workflow run..." }) : null,
    o === "failed" ? /* @__PURE__ */ r.jsx(xo, { message: s }) : null,
    o === "ready" && c ? /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-detail-workbench", children: [
      /* @__PURE__ */ r.jsx(
        mb,
        {
          definitionVersion: c.definitionVersion,
          definitionVersionError: c.definitionVersionError,
          activityCatalog: c.activityCatalog,
          details: c.details,
          selectedEvidenceId: l,
          onSelectEvidence: d
        }
      ),
      /* @__PURE__ */ r.jsx(
        xb,
        {
          ai: t,
          action: f,
          summary: c.details.instance,
          details: c.details,
          state: "ready",
          error: "",
          selectedEvidenceId: l,
          onSelectEvidence: d,
          activityCatalog: c.activityCatalog,
          graphNodeIds: c.definitionVersion ? Nb(c.definitionVersion, c.activityCatalog) : void 0
        }
      )
    ] }) : null
  ] });
}
function mb({ definitionVersion: e, definitionVersionError: t, activityCatalog: n, details: o, selectedEvidenceId: i, onSelectEvidence: s }) {
  const a = ue(() => {
    if (!e) return { nodes: [], edges: [] };
    const c = e.state.rootActivity;
    if (!c) return { nodes: [], edges: [] };
    const u = n.find((p) => p.activityVersionId === c.activityVersionId), l = hs(c, u), d = l === "unsupported" ? null : Zn(c, []), f = l === "unsupported" ? Pr(c, n, e.layout) : d ? Ru(d, n, e.layout) : Pr(c, n, e.layout), h = f.nodes.map((p) => ({
      ...p,
      draggable: !1,
      connectable: !1,
      deletable: !1
    }));
    return {
      nodes: uw(h, o.activities, o.incidents, i),
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
      /* @__PURE__ */ r.jsx(wo, { status: o.instance.status, subStatus: o.instance.subStatus })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ r.jsxs("div", { className: "wf-empty", children: [
        "The workflow run loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ r.jsx("small", { children: Zb(t) }) : null
      ] }),
      e && a.nodes.length === 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      a.nodes.length > 0 ? /* @__PURE__ */ r.jsxs(
        xu,
        {
          nodes: a.nodes,
          edges: a.edges,
          nodeTypes: id,
          edgeTypes: rd,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (c, u) => s(u.id),
          onPaneClick: () => s(null),
          children: [
            /* @__PURE__ */ r.jsx(vu, {}),
            /* @__PURE__ */ r.jsx(Su, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ r.jsx(Nu, {})
          ]
        }
      ) : null
    ] })
  ] });
}
function xb({ ai: e, action: t, summary: n, details: o, state: i, error: s, selectedEvidenceId: a = null, onSelectEvidence: c, graphNodeIds: u, activityCatalog: l = [] }) {
  const [d, f] = q("timeline");
  if (!n)
    return /* @__PURE__ */ r.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Select a workflow run to inspect its timeline." }) });
  const h = o?.incidents.length ?? 0, p = [
    { id: "timeline", title: "Timeline", order: 0, icon: /* @__PURE__ */ r.jsx(ls, { size: 14 }), render: () => null },
    { id: "issues", title: h > 0 ? `Issues (${h})` : "Issues", order: 1, icon: /* @__PURE__ */ r.jsx(kt, { size: 14 }), render: () => null },
    { id: "details", title: "Details", order: 2, icon: /* @__PURE__ */ r.jsx(ds, { size: 14 }), render: () => null }
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
    /* @__PURE__ */ r.jsx("div", { className: "wf-instance-tabs", children: /* @__PURE__ */ r.jsx(Go, { label: "Workflow run tabs", tabs: p, activeTabId: d, onSelect: (g) => f(g) }) }),
    i === "loading" ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Loading run details..." }) : null,
    i === "failed" ? /* @__PURE__ */ r.jsx(xo, { message: s }) : null,
    i === "ready" && o ? /* @__PURE__ */ r.jsx("div", { className: "wf-instance-tab-content", children: d === "timeline" ? /* @__PURE__ */ r.jsx(
      Rv,
      {
        activities: o.activities,
        activityCatalog: l,
        selectedEvidenceId: a,
        onSelectEvidence: c
      }
    ) : d === "issues" ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
      /* @__PURE__ */ r.jsx(wb, { incidents: o.incidents, selectedEvidenceId: a, onSelectEvidence: c }),
      /* @__PURE__ */ r.jsx(vb, { details: o, graphNodeIds: u })
    ] }) : /* @__PURE__ */ r.jsxs("dl", { className: "wf-instance-meta", children: [
      /* @__PURE__ */ r.jsx("dt", { children: "Status" }),
      /* @__PURE__ */ r.jsx("dd", { children: /* @__PURE__ */ r.jsx(wo, { status: n.status, subStatus: n.subStatus }) }),
      /* @__PURE__ */ r.jsx("dt", { children: "Run Kind" }),
      /* @__PURE__ */ r.jsx("dd", { children: ud(n.runKind) }),
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
function wb({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
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
function vb({ details: e, graphNodeIds: t }) {
  if (!t) return null;
  const n = new Map(e.activities.map((s) => [s.activityExecutionId, s])), o = e.activities.filter((s) => !t.has(Nc(s))), i = e.incidents.filter((s) => {
    const a = s.activityExecutionId ? n.get(s.activityExecutionId) : null, c = s.executableNodeId ?? (a ? Nc(a) : "");
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
function wo({ status: e, subStatus: t }) {
  return /* @__PURE__ */ r.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
function ud(e) {
  switch (bb(e)) {
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
function bb(e) {
  return (e ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}
function Nb(e, t) {
  const n = e.state.rootActivity;
  if (!n) return /* @__PURE__ */ new Set();
  const o = t.find((s) => s.activityVersionId === n.activityVersionId);
  if (hs(n, o) === "unsupported") return /* @__PURE__ */ new Set([n.nodeId]);
  const i = Zn(n, []);
  return new Set(i?.slot.activities.map((s) => s.nodeId) ?? [n.nodeId]);
}
function Nc(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function jb({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: i }) {
  const s = dd(n, t), a = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
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
      /* @__PURE__ */ r.jsx("select", { value: t, onChange: (u) => i(Number(u.target.value)), children: tb.map((u) => /* @__PURE__ */ r.jsx("option", { value: u, children: u }, u)) })
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
function Sb(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function dd(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Wt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function St(e, t, n) {
  const o = t.createPrompt(n);
  return o ? (e.dispatchPrompt(o), !0) : !1;
}
function kb(e) {
  const t = wi(e, "flowchart"), n = wi(e, "sequence"), o = [
    { value: t?.activityVersionId ?? "kind:flowchart", label: "Flowchart" },
    { value: n?.activityVersionId ?? "kind:sequence", label: "Sequence" }
  ], i = /* @__PURE__ */ new Map();
  for (const a of e.filter(gd)) {
    if (Ib(a)) continue;
    const c = a.category || "Uncategorized";
    i.set(c, [...i.get(c) ?? [], a]);
  }
  const s = Array.from(i.entries()).sort(([a], [c]) => a.localeCompare(c)).map(([a, c]) => ({
    name: a,
    activities: c.sort((u, l) => Se(u).localeCompare(Se(l)))
  }));
  return { compositeRoots: o, otherCategories: s };
}
function Eb(e, t) {
  return e.rootActivityVersionId ?? wi(t, e.rootKind)?.activityVersionId ?? `kind:${e.rootKind}`;
}
function Cb(e, t) {
  return e.rootActivityVersionId ?? wi(t, e.rootKind)?.activityVersionId ?? null;
}
function wi(e, t) {
  return e.find((n) => fd(n) === t);
}
function fd(e) {
  return e ? hd(e) ? "flowchart" : pd(e) ? "sequence" : null : null;
}
function Br(e) {
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
function Ib(e) {
  return hd(e) || pd(e);
}
function hd(e) {
  return Se(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function pd(e) {
  return Se(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function gd(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function yd(e) {
  return Tb(e.rootActivityType) || e.rootActivityType;
}
function Ab(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function _b(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function Db(e, t) {
  return jc(t) - jc(e);
}
function jc(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function md(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
function xd(e) {
  const t = e?.workflowExecutionId ?? e?.runId ?? e?.executionId;
  return typeof t == "string" && t.trim() ? t : null;
}
async function Mb(e) {
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
function Tb(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function $b(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    Bo(t, n.typeName, n), Bo(t, n.name, n), Bo(t, n.displayName, n);
    const o = n.typeName.split(".").filter(Boolean).at(-1);
    Bo(t, o, n);
  }
  return t;
}
function Pb(e, t, n) {
  const o = t.get(e.activityVersionId);
  return n.get(qn(o?.activityTypeKey)) ?? n.get(qn(mi(o?.activityTypeKey))) ?? n.get(qn(o?.displayName)) ?? n.get(qn(e.activityVersionId)) ?? null;
}
function Bo(e, t, n) {
  const o = qn(t);
  o && !e.has(o) && e.set(o, n);
}
function qn(e) {
  return e?.trim().toLowerCase() ?? "";
}
function Sc(e, t, n, o) {
  const i = Vi();
  if (!i) return t;
  const s = i.getItem(e);
  if (s == null) return t;
  const a = Number(s);
  return Number.isFinite(a) ? Zo(a, n, o) : t;
}
function kc(e, t) {
  const n = Vi();
  if (!n) return t;
  const o = n.getItem(e);
  return o === "true" ? !0 : o === "false" ? !1 : t;
}
function zb() {
  const e = Vi();
  if (!e) return null;
  const t = e.getItem(sd);
  return t === "palette" || t === "inspector" ? t : null;
}
function Vi() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function Bn(e, t) {
  const n = Vi();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function Zo(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function Rb({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: o,
  expressionEditors: i,
  workflowDesignerPanels: s,
  onBack: a
}) {
  const [c, u] = q(null), [l, d] = q(null), [f, h] = q([]), [p, g] = q([]), [v, w] = q(null), [m, k] = q(Uo), [y, b] = q("loading"), [S, E] = q([]), [j, _] = q([]), [M, F] = q([]), [D, T] = q(null), [L, N] = q(null), [A, I] = q(null), [$, z] = q(null), [P, B] = q(""), [K, O] = q(""), [Z, X] = q("idle"), [oe, se] = q(null), [G, V] = q(!1), [U, ae] = q(null), [ce, J] = q(() => /* @__PURE__ */ new Set()), [ee, de] = q(""), [H, Q] = q(() => Sc(gc, ob, Ln, Vn)), [ge, ve] = q(() => Sc(yc, ib, On, Hn)), [Ee, $e] = q(() => kc(mc, !1)), [Ie, qe] = q(() => kc(xc, !1)), [be, Re] = q(zb), [Le, Ye] = q("activities"), [Ze, Ge] = q("inspector"), [Et, wn] = q("designer"), Xe = re(null), Kt = re(null), vn = re(""), bn = re(0), vo = re(Promise.resolve()), Nn = re(/* @__PURE__ */ new Map()), Ve = re(sc()), ut = re(null), dt = re(""), qt = re(!1), [Oi, jn] = q(0), Xt = re(null), mt = re(null), Ct = re(!1), It = l?.state.rootActivity ?? null, Ue = ue(() => new Map(f.map((x) => [x.activityVersionId, x])), [f]), Sn = fe(
    (x) => lw([x.activityVersionId, x.activityTypeKey], v),
    [v]
  ), bo = ue(() => $b(p), [p]), Oe = ue(() => zu(It, S), [It, S]), No = hs(Oe, Oe ? Ue.get(Oe.activityVersionId) : void 0), we = !!Oe && No === "unsupported", He = ue(() => we ? null : Zn(It, S), [It, S, we]), Ut = ue(() => Br(f), [f]), jo = ue(() => {
    const x = ee.trim().toLowerCase();
    if (!x) return Ut;
    const C = f.filter((R) => Se(R).toLowerCase().includes(x) || R.activityTypeKey.toLowerCase().includes(x) || (R.category ?? "").toLowerCase().includes(x) || (R.description ?? "").toLowerCase().includes(x));
    return Br(C);
  }, [f, ee, Ut]), me = ue(() => we && Oe?.nodeId === L ? Oe : He?.slot.activities.find((x) => x.nodeId === L) ?? null, [we, He, Oe, L]), At = ue(
    () => me ? Pb(me, Ue, bo) : null,
    [Ue, bo, me]
  ), So = ue(
    () => me ? Sn({ activityVersionId: me.activityVersionId, activityTypeKey: Ue.get(me.activityVersionId)?.activityTypeKey }) : null,
    [Sn, Ue, me]
  ), kn = me ? ot(me) : [], _e = No === "flowchart" && He?.slot.mode === "flowchart", xt = !It || !we, ft = Z !== "idle", ko = !!l?.state.rootActivity && !ft, Eo = Wt(n, "weaver.workflows.find-draft-risks"), Co = Wt(n, "weaver.workflows.propose-update");
  te(() => {
    if (!(!c || !l))
      return window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
        workflowId: c.definition.id,
        workflowDefinitionId: c.definition.id,
        workflowVersionId: l.sourceVersionId ?? null,
        draftId: l.id,
        revision: Ub(l),
        selectedNodeId: L,
        selectedActivityType: At?.typeName ?? (me ? Ue.get(me.activityVersionId)?.activityTypeKey ?? me.activityVersionId : null),
        summary: c.definition.name,
        activities: vd(l.state.rootActivity, Ue),
        diagnostics: l.validationErrors.map((x) => ({ severity: x.code ?? "warning", message: x.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === c.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [Ue, c, l, At, me, L]), te(() => {
    const x = (R) => {
      const W = R.detail;
      if (!W?.batch || !W.respond) return;
      if (!l || !c) {
        W.respond({ ok: !1, message: "No active workflow draft is open." });
        return;
      }
      const Y = W.batch.workflowDefinitionId;
      if (Y && Y !== "active-draft" && Y !== c.definition.id) {
        W.respond({ ok: !1, message: `Batch targets workflow '${Y}', but '${c.definition.id}' is active.` });
        return;
      }
      try {
        const le = Fn(l), ie = Fv(l, W.batch, f), ye = `weaver-batch-${Date.now()}`;
        Nn.current.set(ye, le), d(ie.draft), E([]), N(ie.finalActivityIds.at(-1) ?? null), ae(null), se(null), O(ie.summary), B(""), W.respond({ ok: !0, result: { ...ie, undoToken: ye } });
      } catch (le) {
        const ie = le instanceof Error ? le.message : String(le);
        B(ie), W.respond({ ok: !1, message: ie });
      }
    }, C = (R) => {
      const W = R.detail;
      if (!W?.undoToken || !W.respond) return;
      const Y = Nn.current.get(W.undoToken);
      if (!Y) {
        W.respond({ ok: !1, message: "The Weaver batch undo point is no longer available." });
        return;
      }
      Nn.current.delete(W.undoToken), d(Y), E([]), N(null), ae(null), se(null), O("Restored workflow draft before Weaver batch."), B(""), W.respond({ ok: !0, summary: "Restored workflow draft before Weaver batch." });
    };
    return window.addEventListener(dc, x), window.addEventListener(fc, C), () => {
      window.removeEventListener(dc, x), window.removeEventListener(fc, C);
    };
  }, [f, c, l]), te(() => {
    Bn(gc, String(H));
  }, [H]), te(() => {
    Bn(yc, String(ge));
  }, [ge]), te(() => {
    Bn(mc, String(Ee));
  }, [Ee]), te(() => {
    Bn(xc, String(Ie));
  }, [Ie]), te(() => {
    Bn(sd, be);
  }, [be]), te(() => {
    if (!be) return;
    const x = (C) => {
      C.key === "Escape" && Re(null);
    };
    return window.addEventListener("keydown", x), () => window.removeEventListener("keydown", x);
  }, [be]);
  const En = fe(async () => {
    B(""), b("loading");
    const [x, C, R, W, Y] = await Promise.all([
      $0(e, t),
      fs(e),
      U0(e).then(
        (ie) => ({ ok: !0, descriptors: ie }),
        () => ({ ok: !1, descriptors: [] })
      ),
      Y0(e).then(
        (ie) => ({ ok: !0, descriptors: ie }),
        () => ({ ok: !1, descriptors: Uo })
      ),
      // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
      $u(e).then(
        (ie) => ie,
        () => null
      )
    ]), le = x.draft ?? null;
    u(x), vn.current = le ? Be(le) : "", Ve.current = sc(), ut.current = le ? Fn(le) : null, dt.current = le ? Be(le) : "", qt.current = !1, jn(0), d(le), h(C.activities ?? []), g(R.descriptors), w(Y), k(W.descriptors.length > 0 ? W.descriptors : Uo), b(R.ok ? "ready" : "failed"), E([]), N(null);
  }, [e, t]);
  te(() => {
    En().catch((x) => B(x instanceof Error ? x.message : String(x)));
  }, [En]), te(() => {
    J((x) => {
      let C = !1;
      const R = new Set(x);
      for (const W of Ut)
        R.has(W.category) || (R.add(W.category), C = !0);
      return C ? R : x;
    });
  }, [Ut]), te(() => {
    if (!Oe) {
      _([]), F([]);
      return;
    }
    const x = we ? Pr(Oe, f, l?.layout ?? []) : He ? Ru(He, f, l?.layout ?? []) : { nodes: [], edges: [] };
    _(x.nodes), F(x.edges);
  }, [f, l?.layout, we, He, Oe]);
  const Hi = (x) => {
    d((C) => C && { ...C, state: { ...C.state, rootActivity: x } });
  }, Cn = fe((x, C) => {
    if (l?.state.rootActivity && we)
      return;
    const R = Rr(x, Ic(x));
    if (!l?.state.rootActivity) {
      Hi(R), N(R.nodeId);
      return;
    }
    if (!He) {
      if (!ot(R)[0]) {
        O(""), B("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      d((Y) => {
        if (!Y?.state.rootActivity) return Y;
        const le = Y.state.rootActivity, ie = zr(R, [], [le]), ye = C ? [
          ...Y.layout.filter((xe) => xe.nodeId !== le.nodeId),
          {
            nodeId: le.nodeId,
            x: Math.round(C.x),
            y: Math.round(C.y)
          }
        ] : Y.layout;
        return {
          ...Y,
          layout: ye,
          state: {
            ...Y.state,
            rootActivity: ie
          }
        };
      }), N(l.state.rootActivity.nodeId), B(""), O(`Wrapped root in ${Se(x)}`);
      return;
    }
    d((W) => {
      if (!W?.state.rootActivity) return W;
      const Y = Zn(W.state.rootActivity, S);
      if (!Y) return W;
      const le = zr(W.state.rootActivity, S, [...Y.slot.activities, R]), ie = C ? [
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
  }, [l?.state.rootActivity, S, we, He]), Yt = fe((x, C) => {
    const R = Rr(x, Ic(x)), W = {
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
        sourcePorts: Hu(R, x)
      }
    };
    return { activityNode: R, node: W };
  }, []), De = fe((x, C, R = []) => {
    we || d((W) => {
      if (!W) return W;
      const Y = hw(W.layout, x), le = W.state.rootActivity;
      if (!le) return { ...W, layout: Y };
      const ie = Zn(le, S);
      if (!ie) return { ...W, layout: Y };
      const ye = dw(ie, x, C, R), xe = ie.slot.mode === "flowchart" ? fw(ye, C) : ye;
      return {
        ...W,
        layout: Y,
        state: {
          ...W.state,
          rootActivity: Lu(le, S, xe)
        }
      };
    });
  }, [S, we]), In = fe((x, C) => {
    if (!Xe.current) return null;
    const R = Xe.current.getBoundingClientRect();
    return D ? D.screenToFlowPosition({ x, y: C }) : {
      x: x - R.left,
      y: C - R.top
    };
  }, [D]), An = fe((x, C) => document.elementFromPoint(x, C)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), _n = fe((x, C, R) => {
    const W = j.find((Me) => Me.id === C.source), Y = j.find((Me) => Me.id === C.target), le = W && Y ? Kb(W, Y) : W ? Ac(W) : R, ie = Yt(x, le), xe = [...j.map((Me) => Me.selected ? { ...Me, selected: !1 } : Me), ie.node], Dt = jw(M, C, ie.node.id);
    _(xe), F(Dt), N(ie.node.id), De(xe, Dt, [ie.activityNode]);
  }, [De, Yt, M, j]), Zt = fe((x, C, R) => {
    if (!xt || !Xe.current) return !1;
    const W = Xe.current.getBoundingClientRect();
    if (!(C >= W.left && C <= W.right && R >= W.top && R <= W.bottom)) return !1;
    const le = In(C, R);
    if (!le) return !1;
    if (_e) {
      const ie = An(C, R), ye = ie ? M.find((xe) => xe.id === ie) : void 0;
      if (ye)
        return _n(x, ye, le), !0;
    }
    return Cn(x, le), !0;
  }, [Cn, xt, M, An, _e, _n, In]);
  te(() => {
    const x = (R) => {
      const W = Xt.current;
      if (!W) return;
      Math.hypot(R.clientX - W.startX, R.clientY - W.startY) >= Jv && (W.dragging = !0);
    }, C = (R) => {
      const W = Xt.current;
      if (Xt.current = null, !W?.dragging || !Xe.current || mt.current) return;
      const Y = Xe.current.getBoundingClientRect();
      R.clientX >= Y.left && R.clientX <= Y.right && R.clientY >= Y.top && R.clientY <= Y.bottom && (Ct.current = !0, window.setTimeout(() => {
        Ct.current = !1;
      }, 0), Zt(W.activity, R.clientX, R.clientY));
    };
    return window.addEventListener("pointermove", x), window.addEventListener("pointerup", C), window.addEventListener("pointercancel", C), () => {
      window.removeEventListener("pointermove", x), window.removeEventListener("pointerup", C), window.removeEventListener("pointercancel", C);
    };
  }, [D, Zt]);
  const Bi = (x, C) => {
    mt.current = { activityVersionId: C.activityVersionId, handledDrop: !1 }, x.dataTransfer.setData(pc, C.activityVersionId), x.dataTransfer.setData("text/plain", C.activityVersionId), x.dataTransfer.effectAllowed = "copy";
  }, Fi = (x, C) => {
    const R = mt.current;
    mt.current = null, !R?.handledDrop && (x.clientX === 0 && x.clientY === 0 || Zt(C, x.clientX, x.clientY) && (Ct.current = !0, window.setTimeout(() => {
      Ct.current = !1;
    }, 0)));
  }, Wi = (x, C) => {
    x.button === 0 && (Xt.current = {
      activity: C,
      startX: x.clientX,
      startY: x.clientY,
      dragging: !1
    });
  }, Ki = (x) => {
    Ct.current || xt && Cn(x);
  }, Dn = (x) => {
    if (!xt) {
      x.dataTransfer.dropEffect = "none";
      return;
    }
    if (x.preventDefault(), x.dataTransfer.dropEffect = "copy", !_e) return;
    const C = An(x.clientX, x.clientY);
    z(C);
  }, Io = (x) => {
    if (!Xe.current) return;
    const C = x.relatedTarget;
    C && Xe.current.contains(C) || z(null);
  }, Ao = (x) => {
    x.preventDefault(), z(null);
    const C = x.dataTransfer.getData(pc) || x.dataTransfer.getData("text/plain");
    if (!C || (x.stopPropagation(), mt.current?.activityVersionId === C && (mt.current.handledDrop = !0), !xt)) return;
    const R = Ue.get(C);
    R && Zt(R, x.clientX, x.clientY);
  }, qi = () => {
    if (!_e) return;
    const x = Xe.current?.getBoundingClientRect();
    x && I({
      kind: "fromEmpty",
      clientX: x.left + x.width / 2,
      clientY: x.top + x.height / 2
    });
  }, Mn = fe(async (x, C) => {
    const R = async () => {
      const Y = ++bn.current, le = Be(x);
      B("");
      try {
        const ie = await O0(e, x), ye = Be(ie);
        return vn.current = ye, d((xe) => !xe || xe.id !== ie.id ? xe : Be(xe) === le ? ie : { ...xe, validationErrors: ie.validationErrors }), Y === bn.current && O(C), ie;
      } catch (ie) {
        throw Y === bn.current && (O(""), B(ie instanceof Error ? ie.message : String(ie))), ie;
      }
    }, W = vo.current.then(R, R);
    return vo.current = W.catch(() => {
    }), W;
  }, [e]);
  te(() => {
    if (!G || !l || Be(l) === vn.current) return;
    O("Autosaving...");
    const C = window.setTimeout(() => {
      Mn(l, "Autosaved").catch(() => {
      });
    }, Qv);
    return () => window.clearTimeout(C);
  }, [G, l, Mn]), te(() => {
    if (!l) return;
    if (qt.current) {
      qt.current = !1;
      return;
    }
    const x = Be(l);
    if (x === dt.current) return;
    const C = window.setTimeout(() => {
      const R = ut.current;
      R && (Ve.current = ac(Ve.current, R), jn((W) => W + 1)), ut.current = Fn(l), dt.current = x;
    }, eb);
    return () => window.clearTimeout(C);
  }, [l]);
  const Xi = fe(() => {
    if (!l) return;
    const x = c?.definition.name;
    Gw(Uw(l, x), x), O("Exported workflow as JSON.");
  }, [l, c]), Ui = fe((x) => {
    d((C) => C && { ...C, state: x(C.state) });
  }, []), Yi = fe((x) => {
    if (!l) return "No draft is loaded.";
    const C = Zw(x, l);
    return C.ok ? (d(C.draft), N(null), E([]), O("Applied workflow JSON."), null) : C.error;
  }, [l]), Gt = fe(() => {
    if (!l) return;
    const x = Be(l);
    if (x === dt.current) return;
    const C = ut.current;
    C && (Ve.current = ac(Ve.current, C)), ut.current = Fn(l), dt.current = x;
  }, [l]), _t = fe((x) => {
    qt.current = !0, ut.current = Fn(x), dt.current = Be(x), d(x), N(null), E([]), jn((C) => C + 1);
  }, []), Tn = fe(() => {
    if (!l) return;
    Gt();
    const x = sv(Ve.current, l);
    x && (Ve.current = x.history, _t(x.snapshot));
  }, [l, Gt, _t]), Jt = fe(() => {
    if (!l) return;
    Gt();
    const x = av(Ve.current, l);
    x && (Ve.current = x.history, _t(x.snapshot));
  }, [l, Gt, _t]), { canUndoNow: Zi, canRedoNow: Gi } = ue(() => {
    const x = !!l && !!ut.current && Be(l) !== dt.current;
    return {
      canUndoNow: iv(Ve.current) || x,
      canRedoNow: rv(Ve.current) && !x
    };
  }, [l, Oi]);
  te(() => {
    const x = (C) => {
      if (Et !== "designer" || !(C.metaKey || C.ctrlKey)) return;
      const R = C.target;
      if (R && (R.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(R.tagName))) return;
      const W = C.key.toLowerCase();
      W === "z" && !C.shiftKey ? (C.preventDefault(), Tn()) : (W === "z" && C.shiftKey || W === "y") && (C.preventDefault(), Jt());
    };
    return window.addEventListener("keydown", x), () => window.removeEventListener("keydown", x);
  }, [Et, Tn, Jt]);
  const $n = async () => {
    if (!(!l || ft)) {
      X("saving"), O("Saving...");
      try {
        await Mn(l, "Saved");
      } catch {
      } finally {
        X("idle");
      }
    }
  }, Ji = async () => {
    if (!(!l || ft)) {
      X("promoting"), O("Promoting...");
      try {
        const x = await H0(e, l.id), C = await B0(e, x.versionId);
        ae(C.artifactId), O(`Published ${C.artifactVersion}`), await En();
      } catch (x) {
        O(""), B(x instanceof Error ? x.message : String(x));
      } finally {
        X("idle");
      }
    }
  }, Qi = async () => {
    if (!l?.state.rootActivity || ft) return;
    const x = l, C = Be(x);
    se(null), O("Preparing test run...");
    try {
      X("testRunPreparing"), O("Preparing test run...");
      const R = Yb(x);
      X("testRunStarting"), O("Starting test run...");
      const W = await F0(e, {
        definitionId: x.definitionId,
        snapshotId: R,
        state: x.state
      });
      se({ draftSignature: C, view: W }), Ge("runtime"), qe(!1), O(ks(W) ? "Test run rejected" : "Test run dispatched");
    } catch (R) {
      O(""), B(R instanceof Error ? R.message : String(R));
    } finally {
      X("idle");
    }
  }, _o = (x) => {
    const C = we ? x.filter((R) => R.type === "select") : x;
    C.length !== 0 && _((R) => Wl(C, R));
  }, Nd = (x) => {
    we || F((C) => Kl(x, C));
  }, er = (x) => !x.source || !x.target || x.source === x.target || !_e ? !1 : !x.targetHandle, jd = (x) => {
    if (!l?.state.rootActivity || !He || !_e || !er(x)) return;
    const C = yi(x.source, x.target, x.sourceHandle ?? "Done", x.targetHandle ?? void 0), R = Xl(C, M);
    F(R), De(j, R);
  }, Sd = () => {
    De(j, M);
  }, kd = !we && j.length > 0, Ed = fe(() => {
    if (we || j.length === 0) return;
    const x = He?.slot.mode === "sequence" ? "sequence" : "flowchart", C = ev(j, M, x), R = j.map((W) => {
      const Y = C.get(W.id);
      return Y ? { ...W, position: Y } : W;
    });
    _(R), De(R, M), window.requestAnimationFrame(() => D?.fitView({ padding: 0.2 })), O("Rearranged the canvas.");
  }, [M, j, He, we, De, D]), Cd = (x, C) => {
    if (!C.nodeId || C.handleType === "target") {
      Kt.current = null;
      return;
    }
    Kt.current = {
      nodeId: C.nodeId,
      handleId: C.handleId ?? null
    };
  }, Id = (x, C) => {
    const R = Xb(Kt.current, C);
    if (Kt.current = null, !R || !_e || C.toNode || C.toHandle || qb(x)) return;
    const W = wd(x);
    I({
      kind: "fromPort",
      sourceNodeId: R.nodeId,
      sourceHandleId: R.handleId,
      clientX: W.x,
      clientY: W.y
    });
  }, Ad = (x, C) => {
    if (!_e || !er(C)) return;
    const R = um(x, {
      ...C,
      sourceHandle: C.sourceHandle ?? "Done",
      targetHandle: C.targetHandle ?? void 0
    }, M, { shouldReplaceId: !1 });
    F(R), De(j, R);
  }, _d = (x) => {
    if (we || x.length === 0) return;
    const C = new Set(x.map((Y) => Y.id)), R = j.filter((Y) => !C.has(Y.id)), W = M.filter((Y) => !C.has(Y.source) && !C.has(Y.target));
    _(R), F(W), L && C.has(L) && N(null), De(R, W);
  }, Dd = (x) => {
    if (we || x.length === 0) return;
    const C = new Set(x.map((W) => W.id)), R = M.filter((W) => !C.has(W.id));
    F(R), De(j, R);
  }, Es = fe((x) => {
    if (we) return;
    const C = M.filter((R) => R.id !== x);
    F(C), De(j, C);
  }, [De, M, we, j]), Cs = fe((x, C, R) => {
    _e && I({ kind: "spliceEdge", edgeId: x, clientX: C, clientY: R });
  }, [_e]), Md = (x) => {
    const C = A;
    if (!C) return;
    I(null);
    const R = In(C.clientX, C.clientY) ?? { x: 0, y: 0 };
    if (C.kind === "fromEmpty") {
      const Y = Yt(x, R), ie = [...j.map((ye) => ye.selected ? { ...ye, selected: !1 } : ye), Y.node];
      _(ie), N(Y.node.id), De(ie, M, [Y.activityNode]);
      return;
    }
    if (C.kind === "fromPort") {
      const Y = j.find((Me) => Me.id === C.sourceNodeId), le = Y ? Ac(Y) : R, ie = Yt(x, le), xe = [...j.map((Me) => Me.selected ? { ...Me, selected: !1 } : Me), ie.node], Dt = [...M, yi(C.sourceNodeId, ie.node.id, C.sourceHandleId ?? "Done")];
      _(xe), F(Dt), N(ie.node.id), De(xe, Dt, [ie.activityNode]);
      return;
    }
    const W = M.find((Y) => Y.id === C.edgeId);
    W && _n(x, W, R);
  }, Td = ue(() => ({
    highlightedEdgeId: $,
    deleteEdge: Es,
    requestInsertActivity: Cs
  }), [Es, $, Cs]), $d = (x, C, R) => {
    E((W) => [...W, { ownerNodeId: x.nodeId, slotId: C, label: R }]), N(null);
  }, Pd = fe((x) => {
    d((C) => {
      const R = C?.state.rootActivity;
      return !C || !R ? C : {
        ...C,
        state: {
          ...C.state,
          rootActivity: Vu(R, x.nodeId, () => x)
        }
      };
    });
  }, []), zd = (x) => {
    J((C) => {
      const R = new Set(C);
      return R.has(x) ? R.delete(x) : R.add(x), R;
    });
  }, Is = (x) => {
    Re((C) => C === x ? null : C), x === "palette" ? $e((C) => !C) : qe((C) => !C);
  }, As = (x) => {
    x === "palette" ? $e(!1) : qe(!1), Re((C) => C === x ? null : x);
  }, _s = (x, C) => {
    Re(null), x === "palette" ? ($e(!1), Q((R) => Zo(R + C, Ln, Vn))) : (qe(!1), ve((R) => Zo(R + C, On, Hn)));
  }, Ds = (x, C) => {
    C.preventDefault(), Re(null), x === "palette" ? $e(!1) : qe(!1);
    const R = C.clientX, W = x === "palette" ? H : ge, Y = x === "palette" ? Ln : On, le = x === "palette" ? Vn : Hn;
    document.body.classList.add("wf-side-panel-resizing");
    const ie = (xe) => {
      const Dt = x === "palette" ? xe.clientX - R : R - xe.clientX, Me = Zo(W + Dt, Y, le);
      x === "palette" ? Q(Me) : ve(Me);
    }, ye = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", ie), window.removeEventListener("pointerup", ye), window.removeEventListener("pointercancel", ye);
    };
    window.addEventListener("pointermove", ie), window.addEventListener("pointerup", ye), window.addEventListener("pointercancel", ye);
  }, Ms = (x, C) => {
    C.key === "ArrowLeft" ? (C.preventDefault(), _s(x, x === "palette" ? -Ho : Ho)) : C.key === "ArrowRight" ? (C.preventDefault(), _s(x, x === "palette" ? Ho : -Ho)) : C.key === "Home" ? (C.preventDefault(), x === "palette" ? Q(Ln) : ve(On)) : C.key === "End" && (C.preventDefault(), x === "palette" ? Q(Vn) : ve(Hn));
  };
  if (!c || !l)
    return /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: P || "Loading workflow editor..." });
  const Rd = [
    "wf-editor-body",
    Ee ? "palette-collapsed" : "",
    Ie ? "inspector-collapsed" : "",
    be === "palette" ? "palette-maximized" : "",
    be === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), Ld = {
    "--wf-palette-width": `${Ee ? wc : H}px`,
    "--wf-inspector-width": `${Ie ? wc : ge}px`
  }, Ts = !Ee && be !== "inspector", $s = !Ie && be !== "palette", Do = oe?.draftSignature === Be(l) ? oe.view : null, Ps = Do && K.startsWith("Test run") ? "" : K, Vd = (x) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(x)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, Od = {
    definition: c.definition,
    draft: l,
    selectedActivity: me,
    selectedActivityDescriptor: At,
    selectedActivitySlots: kn,
    catalog: f,
    currentScopeOwner: Oe,
    frames: S
  }, zs = s.map((x) => {
    const C = x.component;
    return {
      id: x.id,
      title: x.title,
      side: x.side,
      order: x.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ r.jsx(C, { context: Od })
    };
  }), tr = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ r.jsx(Mi, { size: 15 }),
      render: Bd
    },
    ...zs.filter((x) => x.side === "left")
  ].sort(Ec), nr = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ r.jsx(ls, { size: 15 }),
      render: Fd
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: /* @__PURE__ */ r.jsx(gn, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(Wb, { testRun: Do, onOpenRun: Vd })
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ r.jsx(Cu, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(
        pb,
        {
          context: e,
          ai: n,
          definitionId: c.definition.id,
          publishedArtifactId: U
        }
      )
    },
    ...zs.filter((x) => x.side === "right")
  ].sort(Ec), Rs = tr.find((x) => x.id === Le) ?? tr[0], Ls = nr.find((x) => x.id === Ze) ?? nr[0], Hd = [
    { id: "designer", title: "Designer", order: 0, icon: /* @__PURE__ */ r.jsx(w0, { size: 14 }), render: () => null },
    { id: "code", title: "Code", order: 1, icon: /* @__PURE__ */ r.jsx(u0, { size: 14 }), render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: /* @__PURE__ */ r.jsx(ds, { size: 14 }), render: () => null }
  ];
  function Bd() {
    const x = ee.trim().length > 0;
    return /* @__PURE__ */ r.jsxs("div", { className: "wf-palette-body", children: [
      /* @__PURE__ */ r.jsxs("label", { className: "wf-palette-search", children: [
        /* @__PURE__ */ r.jsx(Ti, { size: 14, "aria-hidden": "true" }),
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
      /* @__PURE__ */ r.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: jo.length === 0 ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted wf-palette-empty", children: "No matching activities." }) : jo.map((C) => {
        const R = x || ce.has(C.category);
        return /* @__PURE__ */ r.jsxs("div", { className: "wf-palette-category", children: [
          /* @__PURE__ */ r.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-category-toggle",
              role: "treeitem",
              "aria-expanded": R,
              onClick: () => zd(C.category),
              children: [
                R ? /* @__PURE__ */ r.jsx(l0, { size: 14 }) : /* @__PURE__ */ r.jsx(en, { size: 14 }),
                /* @__PURE__ */ r.jsx("span", { children: C.category }),
                /* @__PURE__ */ r.jsx("small", { children: C.activities.length })
              ]
            }
          ),
          R ? /* @__PURE__ */ r.jsx("div", { className: "wf-palette-activities", role: "group", children: C.activities.map((W) => {
            const Y = W.description?.trim(), le = Y ? `wf-palette-description-${W.activityVersionId}` : void 0, ie = Se(W), ye = gi(W);
            return /* @__PURE__ */ r.jsxs(
              "button",
              {
                type: "button",
                className: "wf-palette-activity",
                role: "treeitem",
                draggable: !0,
                title: Y || Se(W),
                "aria-describedby": le,
                onClick: () => Ki(W),
                onDragStart: (xe) => Bi(xe, W),
                onDragEnd: (xe) => Fi(xe, W),
                onPointerDown: (xe) => Wi(xe, W),
                children: [
                  /* @__PURE__ */ r.jsx("span", { className: "wf-activity-icon", "data-icon": ye, "aria-hidden": "true", children: ys(ye) }),
                  /* @__PURE__ */ r.jsxs("span", { className: "wf-palette-activity-text", children: [
                    /* @__PURE__ */ r.jsx("strong", { children: ie }),
                    Y ? /* @__PURE__ */ r.jsx("small", { id: le, children: Y }) : null
                  ] }),
                  /* @__PURE__ */ r.jsx(h0, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
                ]
              },
              W.activityVersionId
            );
          }) }) : null
        ] }, C.category);
      }) })
    ] });
  }
  function Fd() {
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
      So ? /* @__PURE__ */ r.jsxs("div", { className: "wf-availability-notice", children: [
        /* @__PURE__ */ r.jsx(fi, { size: 14 }),
        /* @__PURE__ */ r.jsxs("span", { children: [
          "No longer available for new use · ",
          hi(So.state)
        ] })
      ] }) : null,
      /* @__PURE__ */ r.jsx(
        Vw,
        {
          activity: me,
          descriptor: At,
          editors: o,
          expressionEditors: i,
          expressionDescriptors: m,
          descriptorStatus: y,
          onChange: Pd
        }
      ),
      kn.length > 0 ? /* @__PURE__ */ r.jsxs("div", { className: "wf-slot-list", children: [
        /* @__PURE__ */ r.jsx("span", { children: "Embedded slots" }),
        kn.map((x) => /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => $d(me, x.id, `${j.find((C) => C.id === me.nodeId)?.data.label ?? me.nodeId} / ${x.label}`), children: [
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
      Ps ? /* @__PURE__ */ r.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ r.jsx(xn, { size: 13 }),
        " ",
        Ps
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
              disabled: !Zi,
              onClick: Tn,
              children: /* @__PURE__ */ r.jsx(x0, { size: 16 })
            }
          ),
          /* @__PURE__ */ r.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Redo",
              title: "Redo (Ctrl+Shift+Z)",
              disabled: !Gi,
              onClick: Jt,
              children: /* @__PURE__ */ r.jsx(y0, { size: 16 })
            }
          ),
          /* @__PURE__ */ r.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Auto-layout",
              title: "Auto-layout the canvas",
              disabled: !kd,
              onClick: Ed,
              children: /* @__PURE__ */ r.jsx(g0, { size: 16 })
            }
          )
        ] }),
        /* @__PURE__ */ r.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ r.jsx("input", { className: "wf-autosave-switch-input", type: "checkbox", checked: G, onChange: (x) => V(x.target.checked) }),
          /* @__PURE__ */ r.jsx("span", { children: "Autosave" })
        ] }),
        Eo ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => St(n, Eo, { definition: c.definition, draft: l }), children: [
          /* @__PURE__ */ r.jsx(jt, { size: 15 }),
          " Risks"
        ] }) : null,
        Co ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => St(n, Co, { definition: c.definition, draft: l }), children: [
          /* @__PURE__ */ r.jsx(jt, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ r.jsxs("button", { type: "button", title: "Export workflow as JSON", onClick: Xi, children: [
          /* @__PURE__ */ r.jsx(f0, { size: 15 }),
          " Export"
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: ft, onClick: () => {
          $n();
        }, children: [
          /* @__PURE__ */ r.jsx(Iu, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: ft, onClick: () => {
          Ji();
        }, children: [
          /* @__PURE__ */ r.jsx(Eu, { size: 15 }),
          " Promote"
        ] }),
        Do ? /* @__PURE__ */ r.jsx(
          Fb,
          {
            testRun: Do,
            onOpenDetails: () => {
              Ge("runtime"), qe(!1);
            }
          }
        ) : null,
        /* @__PURE__ */ r.jsxs(
          "button",
          {
            type: "button",
            disabled: !ko,
            title: l.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              Qi();
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
    /* @__PURE__ */ r.jsxs("div", { className: Rd, style: Ld, children: [
      /* @__PURE__ */ r.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ r.jsx(
            Go,
            {
              label: "Activities panel tabs",
              tabs: tr,
              activeTabId: Rs.id,
              onSelect: Ye
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
                onClick: () => Is("palette"),
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
                onClick: () => As("palette"),
                children: be === "palette" ? /* @__PURE__ */ r.jsx(Za, { size: 14 }) : /* @__PURE__ */ r.jsx(ui, { size: 14 })
              }
            )
          ] })
        ] }),
        Ts ? Rs.render() : null
      ] }),
      Ts && !be ? /* @__PURE__ */ r.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Ln,
          "aria-valuemax": Vn,
          "aria-valuenow": H,
          tabIndex: 0,
          onPointerDown: (x) => Ds("palette", x),
          onKeyDown: (x) => Ms("palette", x)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ r.jsx("div", { className: "wf-canvas-tabs", children: /* @__PURE__ */ r.jsx(
          Go,
          {
            label: "Editor view tabs",
            tabs: Hd,
            activeTabId: Et,
            onSelect: (x) => wn(x)
          }
        ) }),
        Et === "code" ? /* @__PURE__ */ r.jsx(fv, { draft: l, onApply: Yi }) : Et === "properties" ? /* @__PURE__ */ r.jsx(zv, { details: c, draft: l, context: e, onStateChange: Ui }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
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
          /* @__PURE__ */ r.jsxs("div", { className: "wf-canvas", ref: Xe, onDragOver: Dn, onDragLeave: Io, onDrop: Ao, children: [
            /* @__PURE__ */ r.jsx(ad.Provider, { value: Td, children: /* @__PURE__ */ r.jsx(cd.Provider, { value: Sn, children: /* @__PURE__ */ r.jsxs(
              xu,
              {
                nodes: j,
                edges: M,
                nodeTypes: id,
                edgeTypes: rd,
                onInit: T,
                onNodesChange: _o,
                onEdgesChange: Nd,
                onNodesDelete: _d,
                onEdgesDelete: Dd,
                onConnect: jd,
                onConnectStart: _e ? Cd : void 0,
                onConnectEnd: _e ? Id : void 0,
                onReconnect: _e ? Ad : void 0,
                isValidConnection: er,
                onDragOver: Dn,
                onDragLeave: Io,
                onDrop: Ao,
                onPaneClick: () => N(null),
                onNodeClick: (x, C) => N(C.id),
                onNodeDragStop: we ? void 0 : Sd,
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
                  /* @__PURE__ */ r.jsx(vu, { gap: 18, size: 1 }),
                  /* @__PURE__ */ r.jsx(Nu, {}),
                  /* @__PURE__ */ r.jsx(Su, { pannable: !0, zoomable: !0 })
                ]
              }
            ) }) }),
            _e && j.length === 0 ? /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => qi(), children: [
              /* @__PURE__ */ r.jsx(so, { size: 15 }),
              " Add activity"
            ] }) : null,
            A ? /* @__PURE__ */ r.jsx(
              Hb,
              {
                clientX: A.clientX,
                clientY: A.clientY,
                activities: f,
                onPick: Md,
                onClose: () => I(null)
              }
            ) : null
          ] }),
          /* @__PURE__ */ r.jsx(Bb, { draft: l })
        ] })
      ] }),
      $s && !be ? /* @__PURE__ */ r.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": On,
          "aria-valuemax": Hn,
          "aria-valuenow": ge,
          tabIndex: 0,
          onPointerDown: (x) => Ds("inspector", x),
          onKeyDown: (x) => Ms("inspector", x)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ r.jsx(
            Go,
            {
              label: "Inspector panel tabs",
              tabs: nr,
              activeTabId: Ls.id,
              onSelect: Ge
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
                onClick: () => Is("inspector"),
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
                onClick: () => As("inspector"),
                children: be === "inspector" ? /* @__PURE__ */ r.jsx(Za, { size: 14 }) : /* @__PURE__ */ r.jsx(ui, { size: 14 })
              }
            )
          ] })
        ] }),
        $s ? Ls.render() : null
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
function Ec(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
function Lb({ data: e, selected: t }) {
  const n = e, o = n.runtime, i = !n.suppressFlowPorts, s = i ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], a = Vb(n), u = ct.useContext(cd)?.({ activityVersionId: n.activityVersionId, activityTypeKey: n.activityTypeKey }) ?? null;
  return /* @__PURE__ */ r.jsxs(
    "div",
    {
      className: ["wf-node", t ? "selected" : "", o ? "wf-node-runtime" : "", o?.hasBlockingIncident ? "faulted" : "", u ? "wf-node-unavailable" : ""].filter(Boolean).join(" "),
      "data-icon": n.icon ?? "activity",
      children: [
        i && n.acceptsInbound ? /* @__PURE__ */ r.jsx(pn, { type: "target", position: ne.Left }) : null,
        u ? /* @__PURE__ */ r.jsx("span", { className: "wf-node-availability", title: `No longer available for new use · ${hi(u.state)}`, children: /* @__PURE__ */ r.jsx(fi, { size: 13 }) }) : null,
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
          o.status ? /* @__PURE__ */ r.jsx(wo, { status: o.status, subStatus: o.subStatus }) : null,
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
        s.map((l, d) => {
          const f = `${(d + 1) / (s.length + 1) * 100}%`;
          return /* @__PURE__ */ r.jsxs(ct.Fragment, { children: [
            /* @__PURE__ */ r.jsx("span", { className: "wf-node-port-label", style: { top: f }, children: l.displayName }),
            /* @__PURE__ */ r.jsx(pn, { type: "source", position: ne.Right, id: l.name, style: { top: f } })
          ] }, l.name);
        })
      ]
    }
  );
}
function Vb(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((i) => !!i).join(" · ");
}
function Ob(e) {
  const {
    id: t,
    sourceX: n,
    sourceY: o,
    targetX: i,
    targetY: s,
    sourcePosition: a,
    targetPosition: c,
    markerEnd: u,
    style: l,
    label: d,
    labelStyle: f
  } = e, h = ct.useContext(ad), [p, g] = q(!1), [v, w, m] = ai({ sourceX: n, sourceY: o, targetX: i, targetY: s, sourcePosition: a, targetPosition: c }), k = h?.highlightedEdgeId === t;
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsx(
      yo,
      {
        id: t,
        path: v,
        markerEnd: u,
        style: {
          ...l,
          strokeWidth: k ? 2.5 : l?.strokeWidth
        },
        label: d,
        labelX: w,
        labelY: m,
        labelStyle: f,
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1)
      }
    ),
    h ? /* @__PURE__ */ r.jsx(Tx, { children: /* @__PURE__ */ r.jsxs(
      "div",
      {
        className: ["wf-edge-actions", p ? "visible" : "", k ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${w}px, ${m}px)` },
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1),
        children: [
          /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (y) => h.requestInsertActivity(t, y.clientX, y.clientY), children: /* @__PURE__ */ r.jsx(so, { size: 12 }) }),
          /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => h.deleteEdge(t), children: /* @__PURE__ */ r.jsx(di, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function Hb({ clientX: e, clientY: t, activities: n, onPick: o, onClose: i }) {
  const [s, a] = q(""), [c, u] = q(0), l = re(null), d = re(null), f = ue(() => {
    const k = s.trim().toLowerCase(), y = n.filter(gd);
    return k ? y.filter((b) => Se(b).toLowerCase().includes(k) || b.activityTypeKey.toLowerCase().includes(k) || (b.category ?? "").toLowerCase().includes(k) || (b.description ?? "").toLowerCase().includes(k)) : y;
  }, [n, s]), h = ue(() => Br(f), [f]), p = ue(() => h.flatMap((k) => k.activities), [h]);
  te(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), te(() => {
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
      k.preventDefault(), u((y) => Math.min(y + 1, p.length - 1));
    else if (k.key === "ArrowUp")
      k.preventDefault(), u((y) => Math.max(y - 1, 0));
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
        ref: d,
        type: "search",
        value: s,
        placeholder: "Search activities...",
        "aria-label": "Search activities",
        onChange: (k) => {
          a(k.target.value), u(0);
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
            onMouseEnter: () => u(b),
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
function Bb({ draft: e }) {
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
function Fb({
  testRun: e,
  onOpenDetails: t
}) {
  const n = ks(e);
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
function Wb({ testRun: e, onOpenRun: t }) {
  if (!e)
    return /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Run the draft to see Runtime Evidence." }) });
  const n = ks(e), o = e.workflowExecutionId;
  return /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ r.jsxs("section", { className: "wf-runtime-card", "data-state": n ? "rejected" : "accepted", children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("span", { children: "Latest Test Run" }),
        /* @__PURE__ */ r.jsx("h3", { children: n ? "Rejected by the server" : "Transient run accepted" })
      ] }),
      /* @__PURE__ */ r.jsx(wo, { status: e.status, subStatus: e.commandDispatchStatus ?? void 0 })
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
        /* @__PURE__ */ r.jsx("dd", { children: Cc(e.activityCount, "activity") })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ r.jsx("dd", { children: Cc(e.incidentCount, "incident") })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ r.jsx("dd", { title: e.expiresAt ? it(e.expiresAt) : "None", children: e.expiresAt ? it(e.expiresAt) : "None" })
      ] })
    ] })
  ] }) });
}
function Cc(e, t) {
  return typeof e != "number" ? "Available on linked Run" : `${e} ${t}${e === 1 ? "" : "s"}`;
}
function Ic(e) {
  return `${Se(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function Ac(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function Kb(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function wd(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function qb(e) {
  const t = wd(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function Xb(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function Be(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function Ub(e) {
  return bd(Be(e));
}
function vd(e, t, n = []) {
  if (!e) return n;
  const o = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: o?.activityTypeKey ?? e.activityVersionId,
    displayName: o ? Se(o) : void 0
  });
  for (const i of ot(e))
    for (const s of i.activities) vd(s, t, n);
  return n;
}
function Fn(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function Yb(e) {
  return `${e.id}-${bd(JSON.stringify(e.state))}`;
}
function bd(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function ks(e) {
  return e.status.toLowerCase() === "rejected";
}
function Zb(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function Gb(e, t) {
  const n = e instanceof Error ? e.message : String(e);
  return Jb(e, n) ? `Run ${t} was not found.` : n;
}
function Jb(e, t) {
  if ((typeof e == "object" && e ? e.response?.status ?? e.status : void 0) === 404 || /\b404\b/.test(t)) return !0;
  try {
    const o = JSON.parse(t);
    return [o.error, o.title, o.detail].some((i) => typeof i == "string" && /not found/i.test(i));
  } catch {
    return /not found/i.test(t);
  }
}
export {
  qb as isConnectEndOverExistingWorkflowNode,
  t1 as register,
  Xb as resolveConnectEndSource
};
