import jt, { memo as Ee, forwardRef as ar, useRef as se, useEffect as oe, useCallback as de, useContext as to, useMemo as pe, useState as K, createContext as Ai, useLayoutEffect as ld, createElement as ai, useId as ac, lazy as ud, Suspense as dd } from "react";
import "@tanstack/react-query";
function fd(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var Fr = { exports: {} }, _n = {};
var bs;
function hd() {
  if (bs) return _n;
  bs = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function n(o, r, i) {
    var a = null;
    if (i !== void 0 && (a = "" + i), r.key !== void 0 && (a = "" + r.key), "key" in r) {
      i = {};
      for (var c in r)
        c !== "key" && (i[c] = r[c]);
    } else i = r;
    return r = i.ref, {
      $$typeof: e,
      type: o,
      key: a,
      ref: r !== void 0 ? r : null,
      props: i
    };
  }
  return _n.Fragment = t, _n.jsx = n, _n.jsxs = n, _n;
}
var Ns;
function pd() {
  return Ns || (Ns = 1, Fr.exports = hd()), Fr.exports;
}
var s = pd();
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
var gd = { value: () => {
} };
function cr() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new $o(n);
}
function $o(e) {
  this._ = e;
}
function yd(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
$o.prototype = cr.prototype = {
  constructor: $o,
  on: function(e, t) {
    var n = this._, o = yd(e + "", n), r, i = -1, a = o.length;
    if (arguments.length < 2) {
      for (; ++i < a; ) if ((r = (e = o[i]).type) && (r = md(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++i < a; )
      if (r = (e = o[i]).type) n[r] = Ss(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = Ss(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new $o(e);
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
function md(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function Ss(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = gd, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var ci = "http://www.w3.org/1999/xhtml";
const js = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: ci,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function lr(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), js.hasOwnProperty(t) ? { space: js[t], local: e } : e;
}
function xd(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === ci && t.documentElement.namespaceURI === ci ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function wd(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function cc(e) {
  var t = lr(e);
  return (t.local ? wd : xd)(t);
}
function vd() {
}
function Mi(e) {
  return e == null ? vd : function() {
    return this.querySelector(e);
  };
}
function bd(e) {
  typeof e != "function" && (e = Mi(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], a = i.length, c = o[r] = new Array(a), u, l, d = 0; d < a; ++d)
      (u = i[d]) && (l = e.call(u, u.__data__, d, i)) && ("__data__" in u && (l.__data__ = u.__data__), c[d] = l);
  return new Be(o, this._parents);
}
function Nd(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function Sd() {
  return [];
}
function lc(e) {
  return e == null ? Sd : function() {
    return this.querySelectorAll(e);
  };
}
function jd(e) {
  return function() {
    return Nd(e.apply(this, arguments));
  };
}
function Ed(e) {
  typeof e == "function" ? e = jd(e) : e = lc(e);
  for (var t = this._groups, n = t.length, o = [], r = [], i = 0; i < n; ++i)
    for (var a = t[i], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && (o.push(e.call(u, u.__data__, l, a)), r.push(u));
  return new Be(o, r);
}
function uc(e) {
  return function() {
    return this.matches(e);
  };
}
function dc(e) {
  return function(t) {
    return t.matches(e);
  };
}
var kd = Array.prototype.find;
function Cd(e) {
  return function() {
    return kd.call(this.children, e);
  };
}
function Id() {
  return this.firstElementChild;
}
function _d(e) {
  return this.select(e == null ? Id : Cd(typeof e == "function" ? e : dc(e)));
}
var Ad = Array.prototype.filter;
function Md() {
  return Array.from(this.children);
}
function Dd(e) {
  return function() {
    return Ad.call(this.children, e);
  };
}
function Pd(e) {
  return this.selectAll(e == null ? Md : Dd(typeof e == "function" ? e : dc(e)));
}
function Td(e) {
  typeof e != "function" && (e = uc(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], a = i.length, c = o[r] = [], u, l = 0; l < a; ++l)
      (u = i[l]) && e.call(u, u.__data__, l, i) && c.push(u);
  return new Be(o, this._parents);
}
function fc(e) {
  return new Array(e.length);
}
function $d() {
  return new Be(this._enter || this._groups.map(fc), this._parents);
}
function Bo(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
Bo.prototype = {
  constructor: Bo,
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
function Rd(e) {
  return function() {
    return e;
  };
}
function zd(e, t, n, o, r, i) {
  for (var a = 0, c, u = t.length, l = i.length; a < l; ++a)
    (c = t[a]) ? (c.__data__ = i[a], o[a] = c) : n[a] = new Bo(e, i[a]);
  for (; a < u; ++a)
    (c = t[a]) && (r[a] = c);
}
function Ld(e, t, n, o, r, i, a) {
  var c, u, l = /* @__PURE__ */ new Map(), d = t.length, f = i.length, h = new Array(d), p;
  for (c = 0; c < d; ++c)
    (u = t[c]) && (h[c] = p = a.call(u, u.__data__, c, t) + "", l.has(p) ? r[c] = u : l.set(p, u));
  for (c = 0; c < f; ++c)
    p = a.call(e, i[c], c, i) + "", (u = l.get(p)) ? (o[c] = u, u.__data__ = i[c], l.delete(p)) : n[c] = new Bo(e, i[c]);
  for (c = 0; c < d; ++c)
    (u = t[c]) && l.get(h[c]) === u && (r[c] = u);
}
function Vd(e) {
  return e.__data__;
}
function Od(e, t) {
  if (!arguments.length) return Array.from(this, Vd);
  var n = t ? Ld : zd, o = this._parents, r = this._groups;
  typeof e != "function" && (e = Rd(e));
  for (var i = r.length, a = new Array(i), c = new Array(i), u = new Array(i), l = 0; l < i; ++l) {
    var d = o[l], f = r[l], h = f.length, p = Hd(e.call(d, d && d.__data__, l, o)), g = p.length, b = c[l] = new Array(g), w = a[l] = new Array(g), m = u[l] = new Array(h);
    n(d, f, b, w, m, p, t);
    for (var E = 0, y = 0, v, N; E < g; ++E)
      if (v = b[E]) {
        for (E >= y && (y = E + 1); !(N = w[y]) && ++y < g; ) ;
        v._next = N || null;
      }
  }
  return a = new Be(a, o), a._enter = c, a._exit = u, a;
}
function Hd(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function Fd() {
  return new Be(this._exit || this._groups.map(fc), this._parents);
}
function Wd(e, t, n) {
  var o = this.enter(), r = this, i = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? i.remove() : n(i), o && r ? o.merge(r).order() : r;
}
function Bd(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, i = o.length, a = Math.min(r, i), c = new Array(r), u = 0; u < a; ++u)
    for (var l = n[u], d = o[u], f = l.length, h = c[u] = new Array(f), p, g = 0; g < f; ++g)
      (p = l[g] || d[g]) && (h[g] = p);
  for (; u < r; ++u)
    c[u] = n[u];
  return new Be(c, this._parents);
}
function Xd() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, i = o[r], a; --r >= 0; )
      (a = o[r]) && (i && a.compareDocumentPosition(i) ^ 4 && i.parentNode.insertBefore(a, i), i = a);
  return this;
}
function Kd(e) {
  e || (e = qd);
  function t(f, h) {
    return f && h ? e(f.__data__, h.__data__) : !f - !h;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), i = 0; i < o; ++i) {
    for (var a = n[i], c = a.length, u = r[i] = new Array(c), l, d = 0; d < c; ++d)
      (l = a[d]) && (u[d] = l);
    u.sort(t);
  }
  return new Be(r, this._parents).order();
}
function qd(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function Yd() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function Ud() {
  return Array.from(this);
}
function Zd() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length; r < i; ++r) {
      var a = o[r];
      if (a) return a;
    }
  return null;
}
function Gd() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function Jd() {
  return !this.node();
}
function Qd(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], i = 0, a = r.length, c; i < a; ++i)
      (c = r[i]) && e.call(c, c.__data__, i, r);
  return this;
}
function ef(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function tf(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function nf(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function of(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function rf(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function sf(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function af(e, t) {
  var n = lr(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? tf : ef : typeof t == "function" ? n.local ? sf : rf : n.local ? of : nf)(n, t));
}
function hc(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function cf(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function lf(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function uf(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function df(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? cf : typeof t == "function" ? uf : lf)(e, t, n ?? "")) : sn(this.node(), e);
}
function sn(e, t) {
  return e.style.getPropertyValue(t) || hc(e).getComputedStyle(e, null).getPropertyValue(t);
}
function ff(e) {
  return function() {
    delete this[e];
  };
}
function hf(e, t) {
  return function() {
    this[e] = t;
  };
}
function pf(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function gf(e, t) {
  return arguments.length > 1 ? this.each((t == null ? ff : typeof t == "function" ? pf : hf)(e, t)) : this.node()[e];
}
function pc(e) {
  return e.trim().split(/^|\s+/);
}
function Di(e) {
  return e.classList || new gc(e);
}
function gc(e) {
  this._node = e, this._names = pc(e.getAttribute("class") || "");
}
gc.prototype = {
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
function yc(e, t) {
  for (var n = Di(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function mc(e, t) {
  for (var n = Di(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function yf(e) {
  return function() {
    yc(this, e);
  };
}
function mf(e) {
  return function() {
    mc(this, e);
  };
}
function xf(e, t) {
  return function() {
    (t.apply(this, arguments) ? yc : mc)(this, e);
  };
}
function wf(e, t) {
  var n = pc(e + "");
  if (arguments.length < 2) {
    for (var o = Di(this.node()), r = -1, i = n.length; ++r < i; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? xf : t ? yf : mf)(n, t));
}
function vf() {
  this.textContent = "";
}
function bf(e) {
  return function() {
    this.textContent = e;
  };
}
function Nf(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function Sf(e) {
  return arguments.length ? this.each(e == null ? vf : (typeof e == "function" ? Nf : bf)(e)) : this.node().textContent;
}
function jf() {
  this.innerHTML = "";
}
function Ef(e) {
  return function() {
    this.innerHTML = e;
  };
}
function kf(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function Cf(e) {
  return arguments.length ? this.each(e == null ? jf : (typeof e == "function" ? kf : Ef)(e)) : this.node().innerHTML;
}
function If() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function _f() {
  return this.each(If);
}
function Af() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Mf() {
  return this.each(Af);
}
function Df(e) {
  var t = typeof e == "function" ? e : cc(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Pf() {
  return null;
}
function Tf(e, t) {
  var n = typeof e == "function" ? e : cc(e), o = t == null ? Pf : typeof t == "function" ? t : Mi(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function $f() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Rf() {
  return this.each($f);
}
function zf() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Lf() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Vf(e) {
  return this.select(e ? Lf : zf);
}
function Of(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function Hf(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function Ff(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function Wf(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, i; n < r; ++n)
        i = t[n], (!e.type || i.type === e.type) && i.name === e.name ? this.removeEventListener(i.type, i.listener, i.options) : t[++o] = i;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function Bf(e, t, n) {
  return function() {
    var o = this.__on, r, i = Hf(t);
    if (o) {
      for (var a = 0, c = o.length; a < c; ++a)
        if ((r = o[a]).type === e.type && r.name === e.name) {
          this.removeEventListener(r.type, r.listener, r.options), this.addEventListener(r.type, r.listener = i, r.options = n), r.value = t;
          return;
        }
    }
    this.addEventListener(e.type, i, n), r = { type: e.type, name: e.name, value: t, listener: i, options: n }, o ? o.push(r) : this.__on = [r];
  };
}
function Xf(e, t, n) {
  var o = Ff(e + ""), r, i = o.length, a;
  if (arguments.length < 2) {
    var c = this.node().__on;
    if (c) {
      for (var u = 0, l = c.length, d; u < l; ++u)
        for (r = 0, d = c[u]; r < i; ++r)
          if ((a = o[r]).type === d.type && a.name === d.name)
            return d.value;
    }
    return;
  }
  for (c = t ? Bf : Wf, r = 0; r < i; ++r) this.each(c(o[r], t, n));
  return this;
}
function xc(e, t, n) {
  var o = hc(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function Kf(e, t) {
  return function() {
    return xc(this, e, t);
  };
}
function qf(e, t) {
  return function() {
    return xc(this, e, t.apply(this, arguments));
  };
}
function Yf(e, t) {
  return this.each((typeof t == "function" ? qf : Kf)(e, t));
}
function* Uf() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length, a; r < i; ++r)
      (a = o[r]) && (yield a);
}
var wc = [null];
function Be(e, t) {
  this._groups = e, this._parents = t;
}
function no() {
  return new Be([[document.documentElement]], wc);
}
function Zf() {
  return this;
}
Be.prototype = no.prototype = {
  constructor: Be,
  select: bd,
  selectAll: Ed,
  selectChild: _d,
  selectChildren: Pd,
  filter: Td,
  data: Od,
  enter: $d,
  exit: Fd,
  join: Wd,
  merge: Bd,
  selection: Zf,
  order: Xd,
  sort: Kd,
  call: Yd,
  nodes: Ud,
  node: Zd,
  size: Gd,
  empty: Jd,
  each: Qd,
  attr: af,
  style: df,
  property: gf,
  classed: wf,
  text: Sf,
  html: Cf,
  raise: _f,
  lower: Mf,
  append: Df,
  insert: Tf,
  remove: Rf,
  clone: Vf,
  datum: Of,
  on: Xf,
  dispatch: Yf,
  [Symbol.iterator]: Uf
};
function We(e) {
  return typeof e == "string" ? new Be([[document.querySelector(e)]], [document.documentElement]) : new Be([[e]], wc);
}
function Gf(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Ue(e, t) {
  if (e = Gf(e), t === void 0 && (t = e.currentTarget), t) {
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
const Jf = { passive: !1 }, Bn = { capture: !0, passive: !1 };
function Wr(e) {
  e.stopImmediatePropagation();
}
function nn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function vc(e) {
  var t = e.document.documentElement, n = We(e).on("dragstart.drag", nn, Bn);
  "onselectstart" in t ? n.on("selectstart.drag", nn, Bn) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function bc(e, t) {
  var n = e.document.documentElement, o = We(e).on("dragstart.drag", null);
  t && (o.on("click.drag", nn, Bn), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const jo = (e) => () => e;
function li(e, {
  sourceEvent: t,
  subject: n,
  target: o,
  identifier: r,
  active: i,
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
    identifier: { value: r, enumerable: !0, configurable: !0 },
    active: { value: i, enumerable: !0, configurable: !0 },
    x: { value: a, enumerable: !0, configurable: !0 },
    y: { value: c, enumerable: !0, configurable: !0 },
    dx: { value: u, enumerable: !0, configurable: !0 },
    dy: { value: l, enumerable: !0, configurable: !0 },
    _: { value: d }
  });
}
li.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function Qf(e) {
  return !e.ctrlKey && !e.button;
}
function eh() {
  return this.parentNode;
}
function th(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function nh() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Nc() {
  var e = Qf, t = eh, n = th, o = nh, r = {}, i = cr("start", "drag", "end"), a = 0, c, u, l, d, f = 0;
  function h(v) {
    v.on("mousedown.drag", p).filter(o).on("touchstart.drag", w).on("touchmove.drag", m, Jf).on("touchend.drag touchcancel.drag", E).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function p(v, N) {
    if (!(d || !e.call(this, v, N))) {
      var k = y(this, t.call(this, v, N), v, N, "mouse");
      k && (We(v.view).on("mousemove.drag", g, Bn).on("mouseup.drag", b, Bn), vc(v.view), Wr(v), l = !1, c = v.clientX, u = v.clientY, k("start", v));
    }
  }
  function g(v) {
    if (nn(v), !l) {
      var N = v.clientX - c, k = v.clientY - u;
      l = N * N + k * k > f;
    }
    r.mouse("drag", v);
  }
  function b(v) {
    We(v.view).on("mousemove.drag mouseup.drag", null), bc(v.view, l), nn(v), r.mouse("end", v);
  }
  function w(v, N) {
    if (e.call(this, v, N)) {
      var k = v.changedTouches, S = t.call(this, v, N), _ = k.length, P, X;
      for (P = 0; P < _; ++P)
        (X = y(this, S, v, N, k[P].identifier, k[P])) && (Wr(v), X("start", v, k[P]));
    }
  }
  function m(v) {
    var N = v.changedTouches, k = N.length, S, _;
    for (S = 0; S < k; ++S)
      (_ = r[N[S].identifier]) && (nn(v), _("drag", v, N[S]));
  }
  function E(v) {
    var N = v.changedTouches, k = N.length, S, _;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), S = 0; S < k; ++S)
      (_ = r[N[S].identifier]) && (Wr(v), _("end", v, N[S]));
  }
  function y(v, N, k, S, _, P) {
    var X = i.copy(), M = Ue(P || k, N), $, H, j;
    if ((j = n.call(v, new li("beforestart", {
      sourceEvent: k,
      target: h,
      identifier: _,
      active: a,
      x: M[0],
      y: M[1],
      dx: 0,
      dy: 0,
      dispatch: X
    }), S)) != null)
      return $ = j.x - M[0] || 0, H = j.y - M[1] || 0, function A(I, D, T) {
        var R = M, V;
        switch (I) {
          case "start":
            r[_] = A, V = a++;
            break;
          case "end":
            delete r[_], --a;
          // falls through
          case "drag":
            M = Ue(T || D, N), V = a;
            break;
        }
        X.call(
          I,
          v,
          new li(I, {
            sourceEvent: D,
            subject: j,
            target: h,
            identifier: _,
            active: V,
            x: M[0] + $,
            y: M[1] + H,
            dx: M[0] - R[0],
            dy: M[1] - R[1],
            dispatch: X
          }),
          S
        );
      };
  }
  return h.filter = function(v) {
    return arguments.length ? (e = typeof v == "function" ? v : jo(!!v), h) : e;
  }, h.container = function(v) {
    return arguments.length ? (t = typeof v == "function" ? v : jo(v), h) : t;
  }, h.subject = function(v) {
    return arguments.length ? (n = typeof v == "function" ? v : jo(v), h) : n;
  }, h.touchable = function(v) {
    return arguments.length ? (o = typeof v == "function" ? v : jo(!!v), h) : o;
  }, h.on = function() {
    var v = i.on.apply(i, arguments);
    return v === i ? h : v;
  }, h.clickDistance = function(v) {
    return arguments.length ? (f = (v = +v) * v, h) : Math.sqrt(f);
  }, h;
}
function Pi(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function Sc(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function oo() {
}
var Xn = 0.7, Xo = 1 / Xn, on = "\\s*([+-]?\\d+)\\s*", Kn = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", rt = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", oh = /^#([0-9a-f]{3,8})$/, rh = new RegExp(`^rgb\\(${on},${on},${on}\\)$`), ih = new RegExp(`^rgb\\(${rt},${rt},${rt}\\)$`), sh = new RegExp(`^rgba\\(${on},${on},${on},${Kn}\\)$`), ah = new RegExp(`^rgba\\(${rt},${rt},${rt},${Kn}\\)$`), ch = new RegExp(`^hsl\\(${Kn},${rt},${rt}\\)$`), lh = new RegExp(`^hsla\\(${Kn},${rt},${rt},${Kn}\\)$`), Es = {
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
Pi(oo, zt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: ks,
  // Deprecated! Use color.formatHex.
  formatHex: ks,
  formatHex8: uh,
  formatHsl: dh,
  formatRgb: Cs,
  toString: Cs
});
function ks() {
  return this.rgb().formatHex();
}
function uh() {
  return this.rgb().formatHex8();
}
function dh() {
  return jc(this).formatHsl();
}
function Cs() {
  return this.rgb().formatRgb();
}
function zt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = oh.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? Is(t) : n === 3 ? new $e(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? Eo(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? Eo(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = rh.exec(e)) ? new $e(t[1], t[2], t[3], 1) : (t = ih.exec(e)) ? new $e(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = sh.exec(e)) ? Eo(t[1], t[2], t[3], t[4]) : (t = ah.exec(e)) ? Eo(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = ch.exec(e)) ? Ms(t[1], t[2] / 100, t[3] / 100, 1) : (t = lh.exec(e)) ? Ms(t[1], t[2] / 100, t[3] / 100, t[4]) : Es.hasOwnProperty(e) ? Is(Es[e]) : e === "transparent" ? new $e(NaN, NaN, NaN, 0) : null;
}
function Is(e) {
  return new $e(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function Eo(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new $e(e, t, n, o);
}
function fh(e) {
  return e instanceof oo || (e = zt(e)), e ? (e = e.rgb(), new $e(e.r, e.g, e.b, e.opacity)) : new $e();
}
function ui(e, t, n, o) {
  return arguments.length === 1 ? fh(e) : new $e(e, t, n, o ?? 1);
}
function $e(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
Pi($e, ui, Sc(oo, {
  brighter(e) {
    return e = e == null ? Xo : Math.pow(Xo, e), new $e(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Xn : Math.pow(Xn, e), new $e(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new $e(Tt(this.r), Tt(this.g), Tt(this.b), Ko(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: _s,
  // Deprecated! Use color.formatHex.
  formatHex: _s,
  formatHex8: hh,
  formatRgb: As,
  toString: As
}));
function _s() {
  return `#${Pt(this.r)}${Pt(this.g)}${Pt(this.b)}`;
}
function hh() {
  return `#${Pt(this.r)}${Pt(this.g)}${Pt(this.b)}${Pt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function As() {
  const e = Ko(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${Tt(this.r)}, ${Tt(this.g)}, ${Tt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Ko(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function Tt(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function Pt(e) {
  return e = Tt(e), (e < 16 ? "0" : "") + e.toString(16);
}
function Ms(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Ze(e, t, n, o);
}
function jc(e) {
  if (e instanceof Ze) return new Ze(e.h, e.s, e.l, e.opacity);
  if (e instanceof oo || (e = zt(e)), !e) return new Ze();
  if (e instanceof Ze) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), i = Math.max(t, n, o), a = NaN, c = i - r, u = (i + r) / 2;
  return c ? (t === i ? a = (n - o) / c + (n < o) * 6 : n === i ? a = (o - t) / c + 2 : a = (t - n) / c + 4, c /= u < 0.5 ? i + r : 2 - i - r, a *= 60) : c = u > 0 && u < 1 ? 0 : a, new Ze(a, c, u, e.opacity);
}
function ph(e, t, n, o) {
  return arguments.length === 1 ? jc(e) : new Ze(e, t, n, o ?? 1);
}
function Ze(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
Pi(Ze, ph, Sc(oo, {
  brighter(e) {
    return e = e == null ? Xo : Math.pow(Xo, e), new Ze(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Xn : Math.pow(Xn, e), new Ze(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new $e(
      Br(e >= 240 ? e - 240 : e + 120, r, o),
      Br(e, r, o),
      Br(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new Ze(Ds(this.h), ko(this.s), ko(this.l), Ko(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Ko(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${Ds(this.h)}, ${ko(this.s) * 100}%, ${ko(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function Ds(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function ko(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function Br(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const Ti = (e) => () => e;
function gh(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function yh(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function mh(e) {
  return (e = +e) == 1 ? Ec : function(t, n) {
    return n - t ? yh(t, n, e) : Ti(isNaN(t) ? n : t);
  };
}
function Ec(e, t) {
  var n = t - e;
  return n ? gh(e, n) : Ti(isNaN(e) ? t : e);
}
const qo = (function e(t) {
  var n = mh(t);
  function o(r, i) {
    var a = n((r = ui(r)).r, (i = ui(i)).r), c = n(r.g, i.g), u = n(r.b, i.b), l = Ec(r.opacity, i.opacity);
    return function(d) {
      return r.r = a(d), r.g = c(d), r.b = u(d), r.opacity = l(d), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function xh(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(i) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - i) + t[r] * i;
    return o;
  };
}
function wh(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function vh(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), i = new Array(n), a;
  for (a = 0; a < o; ++a) r[a] = Hn(e[a], t[a]);
  for (; a < n; ++a) i[a] = t[a];
  return function(c) {
    for (a = 0; a < o; ++a) i[a] = r[a](c);
    return i;
  };
}
function bh(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(o) {
    return n.setTime(e * (1 - o) + t * o), n;
  };
}
function ot(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function Nh(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = Hn(e[r], t[r]) : o[r] = t[r];
  return function(i) {
    for (r in n) o[r] = n[r](i);
    return o;
  };
}
var di = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, Xr = new RegExp(di.source, "g");
function Sh(e) {
  return function() {
    return e;
  };
}
function jh(e) {
  return function(t) {
    return e(t) + "";
  };
}
function kc(e, t) {
  var n = di.lastIndex = Xr.lastIndex = 0, o, r, i, a = -1, c = [], u = [];
  for (e = e + "", t = t + ""; (o = di.exec(e)) && (r = Xr.exec(t)); )
    (i = r.index) > n && (i = t.slice(n, i), c[a] ? c[a] += i : c[++a] = i), (o = o[0]) === (r = r[0]) ? c[a] ? c[a] += r : c[++a] = r : (c[++a] = null, u.push({ i: a, x: ot(o, r) })), n = Xr.lastIndex;
  return n < t.length && (i = t.slice(n), c[a] ? c[a] += i : c[++a] = i), c.length < 2 ? u[0] ? jh(u[0].x) : Sh(t) : (t = u.length, function(l) {
    for (var d = 0, f; d < t; ++d) c[(f = u[d]).i] = f.x(l);
    return c.join("");
  });
}
function Hn(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? Ti(t) : (n === "number" ? ot : n === "string" ? (o = zt(t)) ? (t = o, qo) : kc : t instanceof zt ? qo : t instanceof Date ? bh : wh(t) ? xh : Array.isArray(t) ? vh : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Nh : ot)(e, t);
}
var Ps = 180 / Math.PI, fi = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function Cc(e, t, n, o, r, i) {
  var a, c, u;
  return (a = Math.sqrt(e * e + t * t)) && (e /= a, t /= a), (u = e * n + t * o) && (n -= e * u, o -= t * u), (c = Math.sqrt(n * n + o * o)) && (n /= c, o /= c, u /= c), e * o < t * n && (e = -e, t = -t, u = -u, a = -a), {
    translateX: r,
    translateY: i,
    rotate: Math.atan2(t, e) * Ps,
    skewX: Math.atan(u) * Ps,
    scaleX: a,
    scaleY: c
  };
}
var Co;
function Eh(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? fi : Cc(t.a, t.b, t.c, t.d, t.e, t.f);
}
function kh(e) {
  return e == null || (Co || (Co = document.createElementNS("http://www.w3.org/2000/svg", "g")), Co.setAttribute("transform", e), !(e = Co.transform.baseVal.consolidate())) ? fi : (e = e.matrix, Cc(e.a, e.b, e.c, e.d, e.e, e.f));
}
function Ic(e, t, n, o) {
  function r(l) {
    return l.length ? l.pop() + " " : "";
  }
  function i(l, d, f, h, p, g) {
    if (l !== f || d !== h) {
      var b = p.push("translate(", null, t, null, n);
      g.push({ i: b - 4, x: ot(l, f) }, { i: b - 2, x: ot(d, h) });
    } else (f || h) && p.push("translate(" + f + t + h + n);
  }
  function a(l, d, f, h) {
    l !== d ? (l - d > 180 ? d += 360 : d - l > 180 && (l += 360), h.push({ i: f.push(r(f) + "rotate(", null, o) - 2, x: ot(l, d) })) : d && f.push(r(f) + "rotate(" + d + o);
  }
  function c(l, d, f, h) {
    l !== d ? h.push({ i: f.push(r(f) + "skewX(", null, o) - 2, x: ot(l, d) }) : d && f.push(r(f) + "skewX(" + d + o);
  }
  function u(l, d, f, h, p, g) {
    if (l !== f || d !== h) {
      var b = p.push(r(p) + "scale(", null, ",", null, ")");
      g.push({ i: b - 4, x: ot(l, f) }, { i: b - 2, x: ot(d, h) });
    } else (f !== 1 || h !== 1) && p.push(r(p) + "scale(" + f + "," + h + ")");
  }
  return function(l, d) {
    var f = [], h = [];
    return l = e(l), d = e(d), i(l.translateX, l.translateY, d.translateX, d.translateY, f, h), a(l.rotate, d.rotate, f, h), c(l.skewX, d.skewX, f, h), u(l.scaleX, l.scaleY, d.scaleX, d.scaleY, f, h), l = d = null, function(p) {
      for (var g = -1, b = h.length, w; ++g < b; ) f[(w = h[g]).i] = w.x(p);
      return f.join("");
    };
  };
}
var Ch = Ic(Eh, "px, ", "px)", "deg)"), Ih = Ic(kh, ", ", ")", ")"), _h = 1e-12;
function Ts(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Ah(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Mh(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const Ro = (function e(t, n, o) {
  function r(i, a) {
    var c = i[0], u = i[1], l = i[2], d = a[0], f = a[1], h = a[2], p = d - c, g = f - u, b = p * p + g * g, w, m;
    if (b < _h)
      m = Math.log(h / l) / t, w = function(S) {
        return [
          c + S * p,
          u + S * g,
          l * Math.exp(t * S * m)
        ];
      };
    else {
      var E = Math.sqrt(b), y = (h * h - l * l + o * b) / (2 * l * n * E), v = (h * h - l * l - o * b) / (2 * h * n * E), N = Math.log(Math.sqrt(y * y + 1) - y), k = Math.log(Math.sqrt(v * v + 1) - v);
      m = (k - N) / t, w = function(S) {
        var _ = S * m, P = Ts(N), X = l / (n * E) * (P * Mh(t * _ + N) - Ah(N));
        return [
          c + X * p,
          u + X * g,
          l * P / Ts(t * _ + N)
        ];
      };
    }
    return w.duration = m * 1e3 * t / Math.SQRT2, w;
  }
  return r.rho = function(i) {
    var a = Math.max(1e-3, +i), c = a * a, u = c * c;
    return e(a, c, u);
  }, r;
})(Math.SQRT2, 2, 4);
var an = 0, Ln = 0, An = 0, _c = 1e3, Yo, Vn, Uo = 0, Lt = 0, ur = 0, qn = typeof performance == "object" && performance.now ? performance : Date, Ac = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function $i() {
  return Lt || (Ac(Dh), Lt = qn.now() + ur);
}
function Dh() {
  Lt = 0;
}
function Zo() {
  this._call = this._time = this._next = null;
}
Zo.prototype = Mc.prototype = {
  constructor: Zo,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? $i() : +n) + (t == null ? 0 : +t), !this._next && Vn !== this && (Vn ? Vn._next = this : Yo = this, Vn = this), this._call = e, this._time = n, hi();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, hi());
  }
};
function Mc(e, t, n) {
  var o = new Zo();
  return o.restart(e, t, n), o;
}
function Ph() {
  $i(), ++an;
  for (var e = Yo, t; e; )
    (t = Lt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --an;
}
function $s() {
  Lt = (Uo = qn.now()) + ur, an = Ln = 0;
  try {
    Ph();
  } finally {
    an = 0, $h(), Lt = 0;
  }
}
function Th() {
  var e = qn.now(), t = e - Uo;
  t > _c && (ur -= t, Uo = e);
}
function $h() {
  for (var e, t = Yo, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Yo = n);
  Vn = e, hi(o);
}
function hi(e) {
  if (!an) {
    Ln && (Ln = clearTimeout(Ln));
    var t = e - Lt;
    t > 24 ? (e < 1 / 0 && (Ln = setTimeout($s, e - qn.now() - ur)), An && (An = clearInterval(An))) : (An || (Uo = qn.now(), An = setInterval(Th, _c)), an = 1, Ac($s));
  }
}
function Rs(e, t, n) {
  var o = new Zo();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var Rh = cr("start", "end", "cancel", "interrupt"), zh = [], Dc = 0, zs = 1, pi = 2, zo = 3, Ls = 4, gi = 5, Lo = 6;
function dr(e, t, n, o, r, i) {
  var a = e.__transition;
  if (!a) e.__transition = {};
  else if (n in a) return;
  Lh(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: Rh,
    tween: zh,
    time: i.time,
    delay: i.delay,
    duration: i.duration,
    ease: i.ease,
    timer: null,
    state: Dc
  });
}
function Ri(e, t) {
  var n = tt(e, t);
  if (n.state > Dc) throw new Error("too late; already scheduled");
  return n;
}
function it(e, t) {
  var n = tt(e, t);
  if (n.state > zo) throw new Error("too late; already running");
  return n;
}
function tt(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function Lh(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = Mc(i, 0, n.time);
  function i(l) {
    n.state = zs, n.timer.restart(a, n.delay, n.time), n.delay <= l && a(l - n.delay);
  }
  function a(l) {
    var d, f, h, p;
    if (n.state !== zs) return u();
    for (d in o)
      if (p = o[d], p.name === n.name) {
        if (p.state === zo) return Rs(a);
        p.state === Ls ? (p.state = Lo, p.timer.stop(), p.on.call("interrupt", e, e.__data__, p.index, p.group), delete o[d]) : +d < t && (p.state = Lo, p.timer.stop(), p.on.call("cancel", e, e.__data__, p.index, p.group), delete o[d]);
      }
    if (Rs(function() {
      n.state === zo && (n.state = Ls, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = pi, n.on.call("start", e, e.__data__, n.index, n.group), n.state === pi) {
      for (n.state = zo, r = new Array(h = n.tween.length), d = 0, f = -1; d < h; ++d)
        (p = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (r[++f] = p);
      r.length = f + 1;
    }
  }
  function c(l) {
    for (var d = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(u), n.state = gi, 1), f = -1, h = r.length; ++f < h; )
      r[f].call(e, d);
    n.state === gi && (n.on.call("end", e, e.__data__, n.index, n.group), u());
  }
  function u() {
    n.state = Lo, n.timer.stop(), delete o[t];
    for (var l in o) return;
    delete e.__transition;
  }
}
function Vo(e, t) {
  var n = e.__transition, o, r, i = !0, a;
  if (n) {
    t = t == null ? null : t + "";
    for (a in n) {
      if ((o = n[a]).name !== t) {
        i = !1;
        continue;
      }
      r = o.state > pi && o.state < gi, o.state = Lo, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[a];
    }
    i && delete e.__transition;
  }
}
function Vh(e) {
  return this.each(function() {
    Vo(this, e);
  });
}
function Oh(e, t) {
  var n, o;
  return function() {
    var r = it(this, e), i = r.tween;
    if (i !== n) {
      o = n = i;
      for (var a = 0, c = o.length; a < c; ++a)
        if (o[a].name === t) {
          o = o.slice(), o.splice(a, 1);
          break;
        }
    }
    r.tween = o;
  };
}
function Hh(e, t, n) {
  var o, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var i = it(this, e), a = i.tween;
    if (a !== o) {
      r = (o = a).slice();
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
function Fh(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = tt(this.node(), n).tween, r = 0, i = o.length, a; r < i; ++r)
      if ((a = o[r]).name === e)
        return a.value;
    return null;
  }
  return this.each((t == null ? Oh : Hh)(n, e, t));
}
function zi(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = it(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return tt(r, o).value[t];
  };
}
function Pc(e, t) {
  var n;
  return (typeof t == "number" ? ot : t instanceof zt ? qo : (n = zt(t)) ? (t = n, qo) : kc)(e, t);
}
function Wh(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Bh(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Xh(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var a = this.getAttribute(e);
    return a === r ? null : a === o ? i : i = t(o = a, n);
  };
}
function Kh(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var a = this.getAttributeNS(e.space, e.local);
    return a === r ? null : a === o ? i : i = t(o = a, n);
  };
}
function qh(e, t, n) {
  var o, r, i;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttribute(e) : (a = this.getAttribute(e), u = c + "", a === u ? null : a === o && u === r ? i : (r = u, i = t(o = a, c)));
  };
}
function Yh(e, t, n) {
  var o, r, i;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (a = this.getAttributeNS(e.space, e.local), u = c + "", a === u ? null : a === o && u === r ? i : (r = u, i = t(o = a, c)));
  };
}
function Uh(e, t) {
  var n = lr(e), o = n === "transform" ? Ih : Pc;
  return this.attrTween(e, typeof t == "function" ? (n.local ? Yh : qh)(n, o, zi(this, "attr." + e, t)) : t == null ? (n.local ? Bh : Wh)(n) : (n.local ? Kh : Xh)(n, o, t));
}
function Zh(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function Gh(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function Jh(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && Gh(e, i)), n;
  }
  return r._value = t, r;
}
function Qh(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && Zh(e, i)), n;
  }
  return r._value = t, r;
}
function ep(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = lr(e);
  return this.tween(n, (o.local ? Jh : Qh)(o, t));
}
function tp(e, t) {
  return function() {
    Ri(this, e).delay = +t.apply(this, arguments);
  };
}
function np(e, t) {
  return t = +t, function() {
    Ri(this, e).delay = t;
  };
}
function op(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? tp : np)(t, e)) : tt(this.node(), t).delay;
}
function rp(e, t) {
  return function() {
    it(this, e).duration = +t.apply(this, arguments);
  };
}
function ip(e, t) {
  return t = +t, function() {
    it(this, e).duration = t;
  };
}
function sp(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? rp : ip)(t, e)) : tt(this.node(), t).duration;
}
function ap(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    it(this, e).ease = t;
  };
}
function cp(e) {
  var t = this._id;
  return arguments.length ? this.each(ap(t, e)) : tt(this.node(), t).ease;
}
function lp(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    it(this, e).ease = n;
  };
}
function up(e) {
  if (typeof e != "function") throw new Error();
  return this.each(lp(this._id, e));
}
function dp(e) {
  typeof e != "function" && (e = uc(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], a = i.length, c = o[r] = [], u, l = 0; l < a; ++l)
      (u = i[l]) && e.call(u, u.__data__, l, i) && c.push(u);
  return new ht(o, this._parents, this._name, this._id);
}
function fp(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, i = Math.min(o, r), a = new Array(o), c = 0; c < i; ++c)
    for (var u = t[c], l = n[c], d = u.length, f = a[c] = new Array(d), h, p = 0; p < d; ++p)
      (h = u[p] || l[p]) && (f[p] = h);
  for (; c < o; ++c)
    a[c] = t[c];
  return new ht(a, this._parents, this._name, this._id);
}
function hp(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function pp(e, t, n) {
  var o, r, i = hp(t) ? Ri : it;
  return function() {
    var a = i(this, e), c = a.on;
    c !== o && (r = (o = c).copy()).on(t, n), a.on = r;
  };
}
function gp(e, t) {
  var n = this._id;
  return arguments.length < 2 ? tt(this.node(), n).on.on(e) : this.each(pp(n, e, t));
}
function yp(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function mp() {
  return this.on("end.remove", yp(this._id));
}
function xp(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Mi(e));
  for (var o = this._groups, r = o.length, i = new Array(r), a = 0; a < r; ++a)
    for (var c = o[a], u = c.length, l = i[a] = new Array(u), d, f, h = 0; h < u; ++h)
      (d = c[h]) && (f = e.call(d, d.__data__, h, c)) && ("__data__" in d && (f.__data__ = d.__data__), l[h] = f, dr(l[h], t, n, h, l, tt(d, n)));
  return new ht(i, this._parents, t, n);
}
function wp(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = lc(e));
  for (var o = this._groups, r = o.length, i = [], a = [], c = 0; c < r; ++c)
    for (var u = o[c], l = u.length, d, f = 0; f < l; ++f)
      if (d = u[f]) {
        for (var h = e.call(d, d.__data__, f, u), p, g = tt(d, n), b = 0, w = h.length; b < w; ++b)
          (p = h[b]) && dr(p, t, n, b, h, g);
        i.push(h), a.push(d);
      }
  return new ht(i, a, t, n);
}
var vp = no.prototype.constructor;
function bp() {
  return new vp(this._groups, this._parents);
}
function Np(e, t) {
  var n, o, r;
  return function() {
    var i = sn(this, e), a = (this.style.removeProperty(e), sn(this, e));
    return i === a ? null : i === n && a === o ? r : r = t(n = i, o = a);
  };
}
function Tc(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Sp(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var a = sn(this, e);
    return a === r ? null : a === o ? i : i = t(o = a, n);
  };
}
function jp(e, t, n) {
  var o, r, i;
  return function() {
    var a = sn(this, e), c = n(this), u = c + "";
    return c == null && (u = c = (this.style.removeProperty(e), sn(this, e))), a === u ? null : a === o && u === r ? i : (r = u, i = t(o = a, c));
  };
}
function Ep(e, t) {
  var n, o, r, i = "style." + t, a = "end." + i, c;
  return function() {
    var u = it(this, e), l = u.on, d = u.value[i] == null ? c || (c = Tc(t)) : void 0;
    (l !== n || r !== d) && (o = (n = l).copy()).on(a, r = d), u.on = o;
  };
}
function kp(e, t, n) {
  var o = (e += "") == "transform" ? Ch : Pc;
  return t == null ? this.styleTween(e, Np(e, o)).on("end.style." + e, Tc(e)) : typeof t == "function" ? this.styleTween(e, jp(e, o, zi(this, "style." + e, t))).each(Ep(this._id, e)) : this.styleTween(e, Sp(e, o, t), n).on("end.style." + e, null);
}
function Cp(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function Ip(e, t, n) {
  var o, r;
  function i() {
    var a = t.apply(this, arguments);
    return a !== r && (o = (r = a) && Cp(e, a, n)), o;
  }
  return i._value = t, i;
}
function _p(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, Ip(e, t, n ?? ""));
}
function Ap(e) {
  return function() {
    this.textContent = e;
  };
}
function Mp(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function Dp(e) {
  return this.tween("text", typeof e == "function" ? Mp(zi(this, "text", e)) : Ap(e == null ? "" : e + ""));
}
function Pp(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Tp(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Pp(r)), t;
  }
  return o._value = e, o;
}
function $p(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, Tp(e));
}
function Rp() {
  for (var e = this._name, t = this._id, n = $c(), o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var a = o[i], c = a.length, u, l = 0; l < c; ++l)
      if (u = a[l]) {
        var d = tt(u, t);
        dr(u, e, n, l, a, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new ht(o, this._parents, e, n);
}
function zp() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(i, a) {
    var c = { value: a }, u = { value: function() {
      --r === 0 && i();
    } };
    n.each(function() {
      var l = it(this, o), d = l.on;
      d !== e && (t = (e = d).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(u)), l.on = t;
    }), r === 0 && i();
  });
}
var Lp = 0;
function ht(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function $c() {
  return ++Lp;
}
var dt = no.prototype;
ht.prototype = {
  constructor: ht,
  select: xp,
  selectAll: wp,
  selectChild: dt.selectChild,
  selectChildren: dt.selectChildren,
  filter: dp,
  merge: fp,
  selection: bp,
  transition: Rp,
  call: dt.call,
  nodes: dt.nodes,
  node: dt.node,
  size: dt.size,
  empty: dt.empty,
  each: dt.each,
  on: gp,
  attr: Uh,
  attrTween: ep,
  style: kp,
  styleTween: _p,
  text: Dp,
  textTween: $p,
  remove: mp,
  tween: Fh,
  delay: op,
  duration: sp,
  ease: cp,
  easeVarying: up,
  end: zp,
  [Symbol.iterator]: dt[Symbol.iterator]
};
function Vp(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var Op = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: Vp
};
function Hp(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function Fp(e) {
  var t, n;
  e instanceof ht ? (t = e._id, e = e._name) : (t = $c(), (n = Op).time = $i(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var a = o[i], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && dr(u, e, t, l, a, n || Hp(u, t));
  return new ht(o, this._parents, e, t);
}
no.prototype.interrupt = Vh;
no.prototype.transition = Fp;
const Io = (e) => () => e;
function Wp(e, {
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
function ft(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
ft.prototype = {
  constructor: ft,
  scale: function(e) {
    return e === 1 ? this : new ft(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new ft(this.k, this.x + this.k * e, this.y + this.k * t);
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
var fr = new ft(1, 0, 0);
Rc.prototype = ft.prototype;
function Rc(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return fr;
  return e.__zoom;
}
function Kr(e) {
  e.stopImmediatePropagation();
}
function Mn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Bp(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function Xp() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function Vs() {
  return this.__zoom || fr;
}
function Kp(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function qp() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Yp(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], i = e.invertY(t[0][1]) - n[0][1], a = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    a > i ? (i + a) / 2 : Math.min(0, i) || Math.max(0, a)
  );
}
function zc() {
  var e = Bp, t = Xp, n = Yp, o = Kp, r = qp, i = [0, 1 / 0], a = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, u = Ro, l = cr("start", "zoom", "end"), d, f, h, p = 500, g = 150, b = 0, w = 10;
  function m(j) {
    j.property("__zoom", Vs).on("wheel.zoom", _, { passive: !1 }).on("mousedown.zoom", P).on("dblclick.zoom", X).filter(r).on("touchstart.zoom", M).on("touchmove.zoom", $).on("touchend.zoom touchcancel.zoom", H).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  m.transform = function(j, A, I, D) {
    var T = j.selection ? j.selection() : j;
    T.property("__zoom", Vs), j !== T ? N(j, A, I, D) : T.interrupt().each(function() {
      k(this, arguments).event(D).start().zoom(null, typeof A == "function" ? A.apply(this, arguments) : A).end();
    });
  }, m.scaleBy = function(j, A, I, D) {
    m.scaleTo(j, function() {
      var T = this.__zoom.k, R = typeof A == "function" ? A.apply(this, arguments) : A;
      return T * R;
    }, I, D);
  }, m.scaleTo = function(j, A, I, D) {
    m.transform(j, function() {
      var T = t.apply(this, arguments), R = this.__zoom, V = I == null ? v(T) : typeof I == "function" ? I.apply(this, arguments) : I, B = R.invert(V), F = typeof A == "function" ? A.apply(this, arguments) : A;
      return n(y(E(R, F), V, B), T, a);
    }, I, D);
  }, m.translateBy = function(j, A, I, D) {
    m.transform(j, function() {
      return n(this.__zoom.translate(
        typeof A == "function" ? A.apply(this, arguments) : A,
        typeof I == "function" ? I.apply(this, arguments) : I
      ), t.apply(this, arguments), a);
    }, null, D);
  }, m.translateTo = function(j, A, I, D, T) {
    m.transform(j, function() {
      var R = t.apply(this, arguments), V = this.__zoom, B = D == null ? v(R) : typeof D == "function" ? D.apply(this, arguments) : D;
      return n(fr.translate(B[0], B[1]).scale(V.k).translate(
        typeof A == "function" ? -A.apply(this, arguments) : -A,
        typeof I == "function" ? -I.apply(this, arguments) : -I
      ), R, a);
    }, D, T);
  };
  function E(j, A) {
    return A = Math.max(i[0], Math.min(i[1], A)), A === j.k ? j : new ft(A, j.x, j.y);
  }
  function y(j, A, I) {
    var D = A[0] - I[0] * j.k, T = A[1] - I[1] * j.k;
    return D === j.x && T === j.y ? j : new ft(j.k, D, T);
  }
  function v(j) {
    return [(+j[0][0] + +j[1][0]) / 2, (+j[0][1] + +j[1][1]) / 2];
  }
  function N(j, A, I, D) {
    j.on("start.zoom", function() {
      k(this, arguments).event(D).start();
    }).on("interrupt.zoom end.zoom", function() {
      k(this, arguments).event(D).end();
    }).tween("zoom", function() {
      var T = this, R = arguments, V = k(T, R).event(D), B = t.apply(T, R), F = I == null ? v(B) : typeof I == "function" ? I.apply(T, R) : I, Z = Math.max(B[1][0] - B[0][0], B[1][1] - B[0][1]), Y = T.__zoom, te = typeof A == "function" ? A.apply(T, R) : A, ce = u(Y.invert(F).concat(Z / Y.k), te.invert(F).concat(Z / te.k));
      return function(J) {
        if (J === 1) J = te;
        else {
          var L = ce(J), U = Z / L[2];
          J = new ft(U, F[0] - L[0] * U, F[1] - L[1] * U);
        }
        V.zoom(null, J);
      };
    });
  }
  function k(j, A, I) {
    return !I && j.__zooming || new S(j, A);
  }
  function S(j, A) {
    this.that = j, this.args = A, this.active = 0, this.sourceEvent = null, this.extent = t.apply(j, A), this.taps = 0;
  }
  S.prototype = {
    event: function(j) {
      return j && (this.sourceEvent = j), this;
    },
    start: function() {
      return ++this.active === 1 && (this.that.__zooming = this, this.emit("start")), this;
    },
    zoom: function(j, A) {
      return this.mouse && j !== "mouse" && (this.mouse[1] = A.invert(this.mouse[0])), this.touch0 && j !== "touch" && (this.touch0[1] = A.invert(this.touch0[0])), this.touch1 && j !== "touch" && (this.touch1[1] = A.invert(this.touch1[0])), this.that.__zoom = A, this.emit("zoom"), this;
    },
    end: function() {
      return --this.active === 0 && (delete this.that.__zooming, this.emit("end")), this;
    },
    emit: function(j) {
      var A = We(this.that).datum();
      l.call(
        j,
        this.that,
        new Wp(j, {
          sourceEvent: this.sourceEvent,
          target: m,
          transform: this.that.__zoom,
          dispatch: l
        }),
        A
      );
    }
  };
  function _(j, ...A) {
    if (!e.apply(this, arguments)) return;
    var I = k(this, A).event(j), D = this.__zoom, T = Math.max(i[0], Math.min(i[1], D.k * Math.pow(2, o.apply(this, arguments)))), R = Ue(j);
    if (I.wheel)
      (I.mouse[0][0] !== R[0] || I.mouse[0][1] !== R[1]) && (I.mouse[1] = D.invert(I.mouse[0] = R)), clearTimeout(I.wheel);
    else {
      if (D.k === T) return;
      I.mouse = [R, D.invert(R)], Vo(this), I.start();
    }
    Mn(j), I.wheel = setTimeout(V, g), I.zoom("mouse", n(y(E(D, T), I.mouse[0], I.mouse[1]), I.extent, a));
    function V() {
      I.wheel = null, I.end();
    }
  }
  function P(j, ...A) {
    if (h || !e.apply(this, arguments)) return;
    var I = j.currentTarget, D = k(this, A, !0).event(j), T = We(j.view).on("mousemove.zoom", F, !0).on("mouseup.zoom", Z, !0), R = Ue(j, I), V = j.clientX, B = j.clientY;
    vc(j.view), Kr(j), D.mouse = [R, this.__zoom.invert(R)], Vo(this), D.start();
    function F(Y) {
      if (Mn(Y), !D.moved) {
        var te = Y.clientX - V, ce = Y.clientY - B;
        D.moved = te * te + ce * ce > b;
      }
      D.event(Y).zoom("mouse", n(y(D.that.__zoom, D.mouse[0] = Ue(Y, I), D.mouse[1]), D.extent, a));
    }
    function Z(Y) {
      T.on("mousemove.zoom mouseup.zoom", null), bc(Y.view, D.moved), Mn(Y), D.event(Y).end();
    }
  }
  function X(j, ...A) {
    if (e.apply(this, arguments)) {
      var I = this.__zoom, D = Ue(j.changedTouches ? j.changedTouches[0] : j, this), T = I.invert(D), R = I.k * (j.shiftKey ? 0.5 : 2), V = n(y(E(I, R), D, T), t.apply(this, A), a);
      Mn(j), c > 0 ? We(this).transition().duration(c).call(N, V, D, j) : We(this).call(m.transform, V, D, j);
    }
  }
  function M(j, ...A) {
    if (e.apply(this, arguments)) {
      var I = j.touches, D = I.length, T = k(this, A, j.changedTouches.length === D).event(j), R, V, B, F;
      for (Kr(j), V = 0; V < D; ++V)
        B = I[V], F = Ue(B, this), F = [F, this.__zoom.invert(F), B.identifier], T.touch0 ? !T.touch1 && T.touch0[2] !== F[2] && (T.touch1 = F, T.taps = 0) : (T.touch0 = F, R = !0, T.taps = 1 + !!d);
      d && (d = clearTimeout(d)), R && (T.taps < 2 && (f = F[0], d = setTimeout(function() {
        d = null;
      }, p)), Vo(this), T.start());
    }
  }
  function $(j, ...A) {
    if (this.__zooming) {
      var I = k(this, A).event(j), D = j.changedTouches, T = D.length, R, V, B, F;
      for (Mn(j), R = 0; R < T; ++R)
        V = D[R], B = Ue(V, this), I.touch0 && I.touch0[2] === V.identifier ? I.touch0[0] = B : I.touch1 && I.touch1[2] === V.identifier && (I.touch1[0] = B);
      if (V = I.that.__zoom, I.touch1) {
        var Z = I.touch0[0], Y = I.touch0[1], te = I.touch1[0], ce = I.touch1[1], J = (J = te[0] - Z[0]) * J + (J = te[1] - Z[1]) * J, L = (L = ce[0] - Y[0]) * L + (L = ce[1] - Y[1]) * L;
        V = E(V, Math.sqrt(J / L)), B = [(Z[0] + te[0]) / 2, (Z[1] + te[1]) / 2], F = [(Y[0] + ce[0]) / 2, (Y[1] + ce[1]) / 2];
      } else if (I.touch0) B = I.touch0[0], F = I.touch0[1];
      else return;
      I.zoom("touch", n(y(V, B, F), I.extent, a));
    }
  }
  function H(j, ...A) {
    if (this.__zooming) {
      var I = k(this, A).event(j), D = j.changedTouches, T = D.length, R, V;
      for (Kr(j), h && clearTimeout(h), h = setTimeout(function() {
        h = null;
      }, p), R = 0; R < T; ++R)
        V = D[R], I.touch0 && I.touch0[2] === V.identifier ? delete I.touch0 : I.touch1 && I.touch1[2] === V.identifier && delete I.touch1;
      if (I.touch1 && !I.touch0 && (I.touch0 = I.touch1, delete I.touch1), I.touch0) I.touch0[1] = this.__zoom.invert(I.touch0[0]);
      else if (I.end(), I.taps === 2 && (V = Ue(V, this), Math.hypot(f[0] - V[0], f[1] - V[1]) < w)) {
        var B = We(this).on("dblclick.zoom");
        B && B.apply(this, arguments);
      }
    }
  }
  return m.wheelDelta = function(j) {
    return arguments.length ? (o = typeof j == "function" ? j : Io(+j), m) : o;
  }, m.filter = function(j) {
    return arguments.length ? (e = typeof j == "function" ? j : Io(!!j), m) : e;
  }, m.touchable = function(j) {
    return arguments.length ? (r = typeof j == "function" ? j : Io(!!j), m) : r;
  }, m.extent = function(j) {
    return arguments.length ? (t = typeof j == "function" ? j : Io([[+j[0][0], +j[0][1]], [+j[1][0], +j[1][1]]]), m) : t;
  }, m.scaleExtent = function(j) {
    return arguments.length ? (i[0] = +j[0], i[1] = +j[1], m) : [i[0], i[1]];
  }, m.translateExtent = function(j) {
    return arguments.length ? (a[0][0] = +j[0][0], a[1][0] = +j[1][0], a[0][1] = +j[0][1], a[1][1] = +j[1][1], m) : [[a[0][0], a[0][1]], [a[1][0], a[1][1]]];
  }, m.constrain = function(j) {
    return arguments.length ? (n = j, m) : n;
  }, m.duration = function(j) {
    return arguments.length ? (c = +j, m) : c;
  }, m.interpolate = function(j) {
    return arguments.length ? (u = j, m) : u;
  }, m.on = function() {
    var j = l.on.apply(l, arguments);
    return j === l ? m : j;
  }, m.clickDistance = function(j) {
    return arguments.length ? (b = (j = +j) * j, m) : Math.sqrt(b);
  }, m.tapDistance = function(j) {
    return arguments.length ? (w = +j, m) : w;
  }, m;
}
const Xe = {
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
}, Yn = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], Lc = ["Enter", " ", "Escape"], Vc = {
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
var cn;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(cn || (cn = {}));
var $t;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})($t || ($t = {}));
var Un;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Un || (Un = {}));
const Oc = {
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
var vt;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(vt || (vt = {}));
var Go;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(Go || (Go = {}));
var ne;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(ne || (ne = {}));
const Os = {
  [ne.Left]: ne.Right,
  [ne.Right]: ne.Left,
  [ne.Top]: ne.Bottom,
  [ne.Bottom]: ne.Top
};
function Hc(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Fc = (e) => "id" in e && "source" in e && "target" in e, Up = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), Li = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), ro = (e, t = [0, 0]) => {
  const { width: n, height: o } = pt(e), r = e.origin ?? t, i = n * r[0], a = o * r[1];
  return {
    x: e.position.x - i,
    y: e.position.y - a
  };
}, Zp = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const i = typeof r == "string";
    let a = !t.nodeLookup && !i ? r : void 0;
    t.nodeLookup && (a = i ? t.nodeLookup.get(r) : Li(r) ? r : t.nodeLookup.get(r.id));
    const c = a ? Jo(a, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return hr(o, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return pr(n);
}, io = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = hr(n, Jo(r)), o = !0);
  }), o ? pr(n) : { x: 0, y: 0, width: 0, height: 0 };
}, Vi = (e, t, [n, o, r] = [0, 0, 1], i = !1, a = !1) => {
  const c = {
    ...gn(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, u = [];
  for (const l of e.values()) {
    const { measured: d, selectable: f = !0, hidden: h = !1 } = l;
    if (a && !f || h)
      continue;
    const p = d.width ?? l.width ?? l.initialWidth ?? null, g = d.height ?? l.height ?? l.initialHeight ?? null, b = Zn(c, un(l)), w = (p ?? 0) * (g ?? 0), m = i && b > 0;
    (!l.internals.handleBounds || m || b >= w || l.dragging) && u.push(l);
  }
  return u;
}, Gp = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function Jp(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function Qp({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: i }, a) {
  if (e.size === 0)
    return !0;
  const c = Jp(e, a), u = io(c), l = Hi(u, t, n, a?.minZoom ?? r, a?.maxZoom ?? i, a?.padding ?? 0.1);
  return await o.setViewport(l, {
    duration: a?.duration,
    ease: a?.ease,
    interpolate: a?.interpolate
  }), !0;
}
function Wc({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: i }) {
  const a = n.get(e), c = a.parentId ? n.get(a.parentId) : void 0, { x: u, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, d = a.origin ?? o;
  let f = a.extent || r;
  if (a.extent === "parent" && !a.expandParent)
    if (!c)
      i?.("005", Xe.error005());
    else {
      const p = c.measured.width, g = c.measured.height;
      p && g && (f = [
        [u, l],
        [u + p, l + g]
      ]);
    }
  else c && Ot(a.extent) && (f = [
    [a.extent[0][0] + u, a.extent[0][1] + l],
    [a.extent[1][0] + u, a.extent[1][1] + l]
  ]);
  const h = Ot(f) ? Vt(t, f, a.measured) : t;
  return (a.measured.width === void 0 || a.measured.height === void 0) && i?.("015", Xe.error015()), {
    position: {
      x: h.x - u + (a.measured.width ?? 0) * d[0],
      y: h.y - l + (a.measured.height ?? 0) * d[1]
    },
    positionAbsolute: h
  };
}
async function eg({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const i = new Set(e.map((h) => h.id)), a = [];
  for (const h of n) {
    if (h.deletable === !1)
      continue;
    const p = i.has(h.id), g = !p && h.parentId && a.find((b) => b.id === h.parentId);
    (p || g) && a.push(h);
  }
  const c = new Set(t.map((h) => h.id)), u = o.filter((h) => h.deletable !== !1), d = Gp(a, u);
  for (const h of u)
    c.has(h.id) && !d.find((g) => g.id === h.id) && d.push(h);
  if (!r)
    return {
      edges: d,
      nodes: a
    };
  const f = await r({
    nodes: a,
    edges: d
  });
  return typeof f == "boolean" ? f ? { edges: d, nodes: a } : { edges: [], nodes: [] } : f;
}
const ln = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), Vt = (e = { x: 0, y: 0 }, t, n) => ({
  x: ln(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: ln(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Bc(e, t, n) {
  const { width: o, height: r } = pt(n), { x: i, y: a } = n.internals.positionAbsolute;
  return Vt(e, [
    [i, a],
    [i + o, a + r]
  ], t);
}
const Hs = (e, t, n) => e < t ? ln(Math.abs(e - t), 1, t) / t : e > n ? -ln(Math.abs(e - n), 1, t) / t : 0, Oi = (e, t, n = 15, o = 40) => {
  const r = Hs(e.x, o, t.width - o) * n, i = Hs(e.y, o, t.height - o) * n;
  return [r, i];
}, hr = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), yi = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), pr = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), un = (e, t = [0, 0]) => {
  const { x: n, y: o } = Li(e) ? e.internals.positionAbsolute : ro(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, Jo = (e, t = [0, 0]) => {
  const { x: n, y: o } = Li(e) ? e.internals.positionAbsolute : ro(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, Xc = (e, t) => pr(hr(yi(e), yi(t))), Zn = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, Fs = (e) => Ge(e.width) && Ge(e.height) && Ge(e.x) && Ge(e.y), Ge = (e) => !isNaN(e) && isFinite(e), Kc = (e, t) => (n, o) => {
}, so = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), gn = ({ x: e, y: t }, [n, o, r], i = !1, a = [1, 1]) => {
  const c = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return i ? so(c, a) : c;
}, dn = ({ x: e, y: t }, [n, o, r]) => ({
  x: e * r + n,
  y: t * r + o
});
function Jt(e, t) {
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
function tg(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = Jt(e, n), r = Jt(e, t);
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
    const o = Jt(e.top ?? e.y ?? 0, n), r = Jt(e.bottom ?? e.y ?? 0, n), i = Jt(e.left ?? e.x ?? 0, t), a = Jt(e.right ?? e.x ?? 0, t);
    return { top: o, right: a, bottom: r, left: i, x: i + a, y: o + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function ng(e, t, n, o, r, i) {
  const { x: a, y: c } = dn(e, [t, n, o]), { x: u, y: l } = dn({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), d = r - u, f = i - l;
  return {
    left: Math.floor(a),
    top: Math.floor(c),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const Hi = (e, t, n, o, r, i) => {
  const a = tg(i, t, n), c = (t - a.x) / e.width, u = (n - a.y) / e.height, l = Math.min(c, u), d = ln(l, o, r), f = e.x + e.width / 2, h = e.y + e.height / 2, p = t / 2 - f * d, g = n / 2 - h * d, b = ng(e, p, g, d, t, n), w = {
    left: Math.min(b.left - a.left, 0),
    top: Math.min(b.top - a.top, 0),
    right: Math.min(b.right - a.right, 0),
    bottom: Math.min(b.bottom - a.bottom, 0)
  };
  return {
    x: p - w.left + w.right,
    y: g - w.top + w.bottom,
    zoom: d
  };
}, Gn = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function Ot(e) {
  return e != null && e !== "parent";
}
function pt(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function qc(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function Yc(e, t = { width: 0, height: 0 }, n, o, r) {
  const i = { ...e }, a = o.get(n);
  if (a) {
    const c = a.origin || r;
    i.x += a.internals.positionAbsolute.x - (t.width ?? 0) * c[0], i.y += a.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return i;
}
function Ws(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function og() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function rg(e) {
  return { ...Vc, ...e || {} };
}
function Fn(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: i, y: a } = Je(e), c = gn({ x: i - (r?.left ?? 0), y: a - (r?.top ?? 0) }, o), { x: u, y: l } = n ? so(c, t) : c;
  return {
    xSnapped: u,
    ySnapped: l,
    ...c
  };
}
const Fi = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), Uc = (e) => e?.getRootNode?.() || window?.document, ig = ["INPUT", "SELECT", "TEXTAREA"];
function Zc(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : ig.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const Gc = (e) => "clientX" in e, Je = (e, t) => {
  const n = Gc(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, Bs = (e, t, n, o, r) => {
  const i = t.querySelectorAll(`.${e}`);
  return !i || !i.length ? null : Array.from(i).map((a) => {
    const c = a.getBoundingClientRect();
    return {
      id: a.getAttribute("data-handleid"),
      type: e,
      nodeId: r,
      position: a.getAttribute("data-handlepos"),
      x: (c.left - n.left) / o,
      y: (c.top - n.top) / o,
      ...Fi(a)
    };
  });
};
function Jc({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: i, targetControlX: a, targetControlY: c }) {
  const u = e * 0.125 + r * 0.375 + a * 0.375 + n * 0.125, l = t * 0.125 + i * 0.375 + c * 0.375 + o * 0.125, d = Math.abs(u - e), f = Math.abs(l - t);
  return [u, l, d, f];
}
function _o(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function Xs({ pos: e, x1: t, y1: n, x2: o, y2: r, c: i }) {
  switch (e) {
    case ne.Left:
      return [t - _o(t - o, i), n];
    case ne.Right:
      return [t + _o(o - t, i), n];
    case ne.Top:
      return [t, n - _o(n - r, i)];
    case ne.Bottom:
      return [t, n + _o(r - n, i)];
  }
}
function Qc({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: r, targetPosition: i = ne.Top, curvature: a = 0.25 }) {
  const [c, u] = Xs({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: a
  }), [l, d] = Xs({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: a
  }), [f, h, p, g] = Jc({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: r,
    sourceControlX: c,
    sourceControlY: u,
    targetControlX: l,
    targetControlY: d
  });
  return [
    `M${e},${t} C${c},${u} ${l},${d} ${o},${r}`,
    f,
    h,
    p,
    g
  ];
}
function el({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, i = n < e ? n + r : n - r, a = Math.abs(o - t) / 2, c = o < t ? o + a : o - a;
  return [i, c, r, a];
}
function sg({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: i = "basic" }) {
  if (i === "manual")
    return o;
  const a = r && n ? o + 1e3 : o, c = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return a + c;
}
function ag({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const i = hr(Jo(e), Jo(t));
  i.x === i.x2 && (i.x2 += 1), i.y === i.y2 && (i.y2 += 1);
  const a = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return Zn(a, pr(i)) > 0;
}
const tl = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, cg = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), lg = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Xe.error006()), t;
  const o = n.getEdgeId || tl;
  let r;
  return Fc(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, cg(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, ug = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: r, ...i } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", Xe.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return o.onError?.("007", Xe.error007(r)), n;
  const c = o.getEdgeId || tl, u = {
    ...i,
    id: o.shouldReplaceId ? c(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== r).concat(u);
};
function nl({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, i, a, c] = el({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, i, a, c];
}
const Ks = {
  [ne.Left]: { x: -1, y: 0 },
  [ne.Right]: { x: 1, y: 0 },
  [ne.Top]: { x: 0, y: -1 },
  [ne.Bottom]: { x: 0, y: 1 }
}, dg = ({ source: e, sourcePosition: t = ne.Bottom, target: n }) => t === ne.Left || t === ne.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, qs = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function fg({ source: e, sourcePosition: t = ne.Bottom, target: n, targetPosition: o = ne.Top, center: r, offset: i, stepPosition: a }) {
  const c = Ks[t], u = Ks[o], l = { x: e.x + c.x * i, y: e.y + c.y * i }, d = { x: n.x + u.x * i, y: n.y + u.y * i }, f = dg({
    source: l,
    sourcePosition: t,
    target: d
  }), h = f.x !== 0 ? "x" : "y", p = f[h];
  let g = [], b, w;
  const m = { x: 0, y: 0 }, E = { x: 0, y: 0 }, [, , y, v] = el({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[h] * u[h] === -1) {
    h === "x" ? (b = r.x ?? l.x + (d.x - l.x) * a, w = r.y ?? (l.y + d.y) / 2) : (b = r.x ?? (l.x + d.x) / 2, w = r.y ?? l.y + (d.y - l.y) * a);
    const _ = [
      { x: b, y: l.y },
      { x: b, y: d.y }
    ], P = [
      { x: l.x, y: w },
      { x: d.x, y: w }
    ];
    c[h] === p ? g = h === "x" ? _ : P : g = h === "x" ? P : _;
  } else {
    const _ = [{ x: l.x, y: d.y }], P = [{ x: d.x, y: l.y }];
    if (h === "x" ? g = c.x === p ? P : _ : g = c.y === p ? _ : P, t === o) {
      const j = Math.abs(e[h] - n[h]);
      if (j <= i) {
        const A = Math.min(i - 1, i - j);
        c[h] === p ? m[h] = (l[h] > e[h] ? -1 : 1) * A : E[h] = (d[h] > n[h] ? -1 : 1) * A;
      }
    }
    if (t !== o) {
      const j = h === "x" ? "y" : "x", A = c[h] === u[j], I = l[j] > d[j], D = l[j] < d[j];
      (c[h] === 1 && (!A && I || A && D) || c[h] !== 1 && (!A && D || A && I)) && (g = h === "x" ? _ : P);
    }
    const X = { x: l.x + m.x, y: l.y + m.y }, M = { x: d.x + E.x, y: d.y + E.y }, $ = Math.max(Math.abs(X.x - g[0].x), Math.abs(M.x - g[0].x)), H = Math.max(Math.abs(X.y - g[0].y), Math.abs(M.y - g[0].y));
    $ >= H ? (b = (X.x + M.x) / 2, w = g[0].y) : (b = g[0].x, w = (X.y + M.y) / 2);
  }
  const N = { x: l.x + m.x, y: l.y + m.y }, k = { x: d.x + E.x, y: d.y + E.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...N.x !== g[0].x || N.y !== g[0].y ? [N] : [],
    ...g,
    ...k.x !== g[g.length - 1].x || k.y !== g[g.length - 1].y ? [k] : [],
    n
  ], b, w, y, v];
}
function hg(e, t, n, o) {
  const r = Math.min(qs(e, t) / 2, qs(t, n) / 2, o), { x: i, y: a } = t;
  if (e.x === i && i === n.x || e.y === a && a === n.y)
    return `L${i} ${a}`;
  if (e.y === a) {
    const l = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${i + r * l},${a}Q ${i},${a} ${i},${a + r * d}`;
  }
  const c = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${i},${a + r * u}Q ${i},${a} ${i + r * c},${a}`;
}
function Qo({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: r, targetPosition: i = ne.Top, borderRadius: a = 5, centerX: c, centerY: u, offset: l = 20, stepPosition: d = 0.5 }) {
  const [f, h, p, g, b] = fg({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: i,
    center: { x: c, y: u },
    offset: l,
    stepPosition: d
  });
  let w = `M${f[0].x} ${f[0].y}`;
  for (let m = 1; m < f.length - 1; m++)
    w += hg(f[m - 1], f[m], f[m + 1], a);
  return w += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [w, h, p, g, b];
}
function Ys(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function pg(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!Ys(t) || !Ys(n))
    return null;
  const o = t.internals.handleBounds || Us(t.handles), r = n.internals.handleBounds || Us(n.handles), i = Zs(o?.source ?? [], e.sourceHandle), a = Zs(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === cn.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!i || !a)
    return e.onError?.("008", Xe.error008(i ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = i?.position || ne.Bottom, u = a?.position || ne.Top, l = Ht(t, i, c), d = Ht(n, a, u);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: d.x,
    targetY: d.y,
    sourcePosition: c,
    targetPosition: u
  };
}
function Us(e) {
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
function Ht(e, t, n = ne.Left, o = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, i = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: a, height: c } = t ?? pt(e);
  if (o)
    return { x: r + a / 2, y: i + c / 2 };
  switch (t?.position ?? n) {
    case ne.Top:
      return { x: r + a / 2, y: i };
    case ne.Right:
      return { x: r + a, y: i + c / 2 };
    case ne.Bottom:
      return { x: r + a / 2, y: i + c };
    case ne.Left:
      return { x: r, y: i + c / 2 };
  }
}
function Zs(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function mi(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function gg(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const i = /* @__PURE__ */ new Set();
  return e.reduce((a, c) => ([c.markerStart || o, c.markerEnd || r].forEach((u) => {
    if (u && typeof u == "object") {
      const l = mi(u, t);
      i.has(l) || (a.push({ id: l, color: u.color || n, ...u }), i.add(l));
    }
  }), a), []).sort((a, c) => a.id.localeCompare(c.id));
}
const ol = 1e3, yg = 10, Wi = {
  nodeOrigin: [0, 0],
  nodeExtent: Yn,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, mg = {
  ...Wi,
  checkEquality: !0
};
function Bi(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function xg(e, t, n) {
  const o = Bi(Wi, n);
  for (const r of e.values())
    if (r.parentId)
      Ki(r, e, t, o);
    else {
      const i = ro(r, o.nodeOrigin), a = Ot(r.extent) ? r.extent : o.nodeExtent, c = Vt(i, a, pt(r));
      r.internals.positionAbsolute = c;
    }
}
function wg(e, t) {
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
function Xi(e) {
  return e === "manual";
}
function xi(e, t, n, o = {}) {
  const r = Bi(mg, o), i = { i: 0 }, a = new Map(t), c = r?.elevateNodesOnSelect && !Xi(r.zIndexMode) ? ol : 0;
  let u = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = a.get(d.id);
    if (r.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const h = ro(d, r.nodeOrigin), p = Ot(d.extent) ? d.extent : r.nodeExtent, g = Vt(h, p, pt(d));
      f = {
        ...r.defaults,
        ...d,
        measured: {
          width: d.measured?.width,
          height: d.measured?.height
        },
        internals: {
          positionAbsolute: g,
          // if user re-initializes the node or removes `measured` for whatever reason, we reset the handleBounds so that the node gets re-measured
          handleBounds: wg(d, f),
          z: rl(d, c, r.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (u = !1), d.parentId && Ki(f, t, n, o, i), l ||= d.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: l };
}
function vg(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function Ki(e, t, n, o, r) {
  const { elevateNodesOnSelect: i, nodeOrigin: a, nodeExtent: c, zIndexMode: u } = Bi(Wi, o), l = e.parentId, d = t.get(l);
  if (!d) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  vg(e, n), r && !d.parentId && d.internals.rootParentIndex === void 0 && u === "auto" && (d.internals.rootParentIndex = ++r.i, d.internals.z = d.internals.z + r.i * yg), r && d.internals.rootParentIndex !== void 0 && (r.i = d.internals.rootParentIndex);
  const f = i && !Xi(u) ? ol : 0, { x: h, y: p, z: g } = bg(e, d, a, c, f, u), { positionAbsolute: b } = e.internals, w = h !== b.x || p !== b.y;
  (w || g !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: w ? { x: h, y: p } : b,
      z: g
    }
  });
}
function rl(e, t, n) {
  const o = Ge(e.zIndex) ? e.zIndex : 0;
  return Xi(n) ? o : o + (e.selected ? t : 0);
}
function bg(e, t, n, o, r, i) {
  const { x: a, y: c } = t.internals.positionAbsolute, u = pt(e), l = ro(e, n), d = Ot(e.extent) ? Vt(l, e.extent, u) : l;
  let f = Vt({ x: a + d.x, y: c + d.y }, o, u);
  e.extent === "parent" && (f = Bc(f, u, t));
  const h = rl(e, r, i), p = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: p >= h ? p + 1 : h
  };
}
function qi(e, t, n, o = [0, 0]) {
  const r = [], i = /* @__PURE__ */ new Map();
  for (const a of e) {
    const c = t.get(a.parentId);
    if (!c)
      continue;
    const u = i.get(a.parentId)?.expandedRect ?? un(c), l = Xc(u, a.rect);
    i.set(a.parentId, { expandedRect: l, parent: c });
  }
  return i.size > 0 && i.forEach(({ expandedRect: a, parent: c }, u) => {
    const l = c.internals.positionAbsolute, d = pt(c), f = c.origin ?? o, h = a.x < l.x ? Math.round(Math.abs(l.x - a.x)) : 0, p = a.y < l.y ? Math.round(Math.abs(l.y - a.y)) : 0, g = Math.max(d.width, Math.round(a.width)), b = Math.max(d.height, Math.round(a.height)), w = (g - d.width) * f[0], m = (b - d.height) * f[1];
    (h > 0 || p > 0 || w || m) && (r.push({
      id: u,
      type: "position",
      position: {
        x: c.position.x - h + w,
        y: c.position.y - p + m
      }
    }), n.get(u)?.forEach((E) => {
      e.some((y) => y.id === E.id) || r.push({
        id: E.id,
        type: "position",
        position: {
          x: E.position.x + h,
          y: E.position.y + p
        }
      });
    })), (d.width < a.width || d.height < a.height || h || p) && r.push({
      id: u,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: g + (h ? f[0] * h - w : 0),
        height: b + (p ? f[1] * p - m : 0)
      }
    });
  }), r;
}
function Ng(e, t, n, o, r, i, a) {
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
    const b = Fi(p.nodeElement), w = g.measured.width !== b.width || g.measured.height !== b.height;
    if (!!(b.width && b.height && (w || !g.internals.handleBounds || p.force))) {
      const E = p.nodeElement.getBoundingClientRect(), y = Ot(g.extent) ? g.extent : i;
      let { positionAbsolute: v } = g.internals;
      g.parentId && g.extent === "parent" ? v = Bc(v, b, t.get(g.parentId)) : y && (v = Vt(v, y, b));
      const N = {
        ...g,
        measured: b,
        internals: {
          ...g.internals,
          positionAbsolute: v,
          handleBounds: {
            source: Bs("source", p.nodeElement, E, f, g.id),
            target: Bs("target", p.nodeElement, E, f, g.id)
          }
        }
      };
      t.set(g.id, N), g.parentId && Ki(N, t, n, { nodeOrigin: r, zIndexMode: a }), u = !0, w && (l.push({
        id: g.id,
        type: "dimensions",
        dimensions: b
      }), g.expandParent && g.parentId && h.push({
        id: g.id,
        parentId: g.parentId,
        rect: un(N, r)
      }));
    }
  }
  if (h.length > 0) {
    const p = qi(h, t, n, r);
    l.push(...p);
  }
  return { changes: l, updatedInternals: u };
}
async function Sg({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: i }) {
  if (!t || !e.x && !e.y)
    return !1;
  const a = await t.setViewportConstrained({
    x: n[0] + e.x,
    y: n[1] + e.y,
    zoom: n[2]
  }, [
    [0, 0],
    [r, i]
  ], o);
  return !!a && (a.x !== n[0] || a.y !== n[1] || a.k !== n[2]);
}
function Gs(e, t, n, o, r, i) {
  let a = r;
  const c = o.get(a) || /* @__PURE__ */ new Map();
  o.set(a, c.set(n, t)), a = `${r}-${e}`;
  const u = o.get(a) || /* @__PURE__ */ new Map();
  if (o.set(a, u.set(n, t)), i) {
    a = `${r}-${e}-${i}`;
    const l = o.get(a) || /* @__PURE__ */ new Map();
    o.set(a, l.set(n, t));
  }
}
function il(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: i, sourceHandle: a = null, targetHandle: c = null } = o, u = { edgeId: o.id, source: r, target: i, sourceHandle: a, targetHandle: c }, l = `${r}-${a}--${i}-${c}`, d = `${i}-${c}--${r}-${a}`;
    Gs("source", u, d, e, r, a), Gs("target", u, l, e, i, c), t.set(o.id, o);
  }
}
function sl(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : sl(n, t) : !1;
}
function Js(e, t, n) {
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
function jg(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [i, a] of e)
    if ((a.selected || a.id === o) && (!a.parentId || !sl(a, e)) && (a.draggable || t && typeof a.draggable > "u")) {
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
function qr({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
  const r = [];
  for (const [a, c] of t) {
    const u = n.get(a)?.internals.userNode;
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
function Eg({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const i = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, a = so(i, t);
  return {
    x: a.x - i.x,
    y: a.y - i.y
  };
}
function kg({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let i = { x: null, y: null }, a = 0, c = /* @__PURE__ */ new Map(), u = !1, l = { x: 0, y: 0 }, d = null, f = !1, h = null, p = !1, g = !1, b = null;
  function w({ noDragClassName: E, handleSelector: y, domNode: v, isSelectable: N, nodeId: k, nodeClickDistance: S = 0 }) {
    h = We(v);
    function _({ x: $, y: H }) {
      const { nodeLookup: j, nodeExtent: A, snapGrid: I, snapToGrid: D, nodeOrigin: T, onNodeDrag: R, onSelectionDrag: V, onError: B, updateNodePositions: F } = t();
      i = { x: $, y: H };
      let Z = !1;
      const Y = c.size > 1, te = Y && A ? yi(io(c)) : null, ce = Y && D ? Eg({
        dragItems: c,
        snapGrid: I,
        x: $,
        y: H
      }) : null;
      for (const [J, L] of c) {
        if (!j.has(J))
          continue;
        let U = { x: $ - L.distance.x, y: H - L.distance.y };
        D && (U = ce ? {
          x: Math.round(U.x + ce.x),
          y: Math.round(U.y + ce.y)
        } : so(U, I));
        let ae = null;
        if (Y && A && !L.extent && te) {
          const { positionAbsolute: ee } = L.internals, le = ee.x - te.x + A[0][0], O = ee.x + L.measured.width - te.x2 + A[1][0], G = ee.y - te.y + A[0][1], he = ee.y + L.measured.height - te.y2 + A[1][1];
          ae = [
            [le, G],
            [O, he]
          ];
        }
        const { position: re, positionAbsolute: Q } = Wc({
          nodeId: J,
          nextPosition: U,
          nodeLookup: j,
          nodeExtent: ae || A,
          nodeOrigin: T,
          onError: B
        });
        Z = Z || L.position.x !== re.x || L.position.y !== re.y, L.position = re, L.internals.positionAbsolute = Q;
      }
      if (g = g || Z, !!Z && (F(c, !0), b && (o || R || !k && V))) {
        const [J, L] = qr({
          nodeId: k,
          dragItems: c,
          nodeLookup: j
        });
        o?.(b, c, J, L), R?.(b, J, L), k || V?.(b, L);
      }
    }
    async function P() {
      if (!d)
        return;
      const { transform: $, panBy: H, autoPanSpeed: j, autoPanOnNodeDrag: A } = t();
      if (!A) {
        u = !1, cancelAnimationFrame(a);
        return;
      }
      const [I, D] = Oi(l, d, j);
      (I !== 0 || D !== 0) && (i.x = (i.x ?? 0) - I / $[2], i.y = (i.y ?? 0) - D / $[2], await H({ x: I, y: D }) && _(i)), a = requestAnimationFrame(P);
    }
    function X($) {
      const { nodeLookup: H, multiSelectionActive: j, nodesDraggable: A, transform: I, snapGrid: D, snapToGrid: T, selectNodesOnDrag: R, onNodeDragStart: V, onSelectionDragStart: B, unselectNodesAndEdges: F } = t();
      f = !0, (!R || !N) && !j && k && (H.get(k)?.selected || F()), N && R && k && e?.(k);
      const Z = Fn($.sourceEvent, { transform: I, snapGrid: D, snapToGrid: T, containerBounds: d });
      if (i = Z, c = jg(H, A, Z, k), c.size > 0 && (n || V || !k && B)) {
        const [Y, te] = qr({
          nodeId: k,
          dragItems: c,
          nodeLookup: H
        });
        n?.($.sourceEvent, c, Y, te), V?.($.sourceEvent, Y, te), k || B?.($.sourceEvent, te);
      }
    }
    const M = Nc().clickDistance(S).on("start", ($) => {
      const { domNode: H, nodeDragThreshold: j, transform: A, snapGrid: I, snapToGrid: D } = t();
      d = H?.getBoundingClientRect() || null, p = !1, g = !1, b = $.sourceEvent, j === 0 && X($), i = Fn($.sourceEvent, { transform: A, snapGrid: I, snapToGrid: D, containerBounds: d }), l = Je($.sourceEvent, d);
    }).on("drag", ($) => {
      const { autoPanOnNodeDrag: H, transform: j, snapGrid: A, snapToGrid: I, nodeDragThreshold: D, nodeLookup: T } = t(), R = Fn($.sourceEvent, { transform: j, snapGrid: A, snapToGrid: I, containerBounds: d });
      if (b = $.sourceEvent, ($.sourceEvent.type === "touchmove" && $.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      k && !T.has(k)) && (p = !0), !p) {
        if (!u && H && f && (u = !0, P()), !f) {
          const V = Je($.sourceEvent, d), B = V.x - l.x, F = V.y - l.y;
          Math.sqrt(B * B + F * F) > D && X($);
        }
        (i.x !== R.xSnapped || i.y !== R.ySnapped) && c && f && (l = Je($.sourceEvent, d), _(R));
      }
    }).on("end", ($) => {
      if (!f || p) {
        p && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (u = !1, f = !1, cancelAnimationFrame(a), c.size > 0) {
        const { nodeLookup: H, updateNodePositions: j, onNodeDragStop: A, onSelectionDragStop: I } = t();
        if (g && (j(c, !1), g = !1), r || A || !k && I) {
          const [D, T] = qr({
            nodeId: k,
            dragItems: c,
            nodeLookup: H,
            dragging: !1
          });
          r?.($.sourceEvent, c, D, T), A?.($.sourceEvent, D, T), k || I?.($.sourceEvent, T);
        }
      }
    }).filter(($) => {
      const H = $.target;
      return !$.button && (!E || !Js(H, `.${E}`, v)) && (!y || Js(H, y, v));
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
function Cg(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const i of t.values())
    Zn(r, un(i)) > 0 && o.push(i);
  return o;
}
const Ig = 250;
function _g(e, t, n, o) {
  let r = [], i = 1 / 0;
  const a = Cg(e, n, t + Ig);
  for (const c of a) {
    const u = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of u) {
      if (o.nodeId === l.nodeId && o.type === l.type && o.id === l.id)
        continue;
      const { x: d, y: f } = Ht(c, l, l.position, !0), h = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(f - e.y, 2));
      h > t || (h < i ? (r = [{ ...l, x: d, y: f }], i = h) : h === i && r.push({ ...l, x: d, y: f }));
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
function al(e, t, n, o, r, i = !1) {
  const a = o.get(e);
  if (!a)
    return null;
  const c = r === "strict" ? a.internals.handleBounds?.[t] : [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []], u = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return u && i ? { ...u, ...Ht(a, u, u.position, !0) } : u;
}
function cl(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Ag(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const ll = () => !0;
function Mg(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: i, isTarget: a, domNode: c, nodeLookup: u, lib: l, autoPanOnConnect: d, flowId: f, panBy: h, cancelConnection: p, onConnectStart: g, onConnect: b, onConnectEnd: w, isValidConnection: m = ll, onReconnectEnd: E, updateConnection: y, getTransform: v, getFromHandle: N, autoPanSpeed: k, dragThreshold: S = 1, handleDomNode: _ }) {
  const P = Uc(e.target);
  let X = 0, M;
  const { x: $, y: H } = Je(e), j = cl(i, _), A = c?.getBoundingClientRect();
  let I = !1;
  if (!A || !j)
    return;
  const D = al(r, j, o, u, t);
  if (!D)
    return;
  let T = Je(e, A), R = !1, V = null, B = !1, F = null;
  function Z() {
    if (!d || !A)
      return;
    const [re, Q] = Oi(T, A, k);
    h({ x: re, y: Q }), X = requestAnimationFrame(Z);
  }
  const Y = {
    ...D,
    nodeId: r,
    type: j,
    position: D.position
  }, te = u.get(r);
  let J = {
    inProgress: !0,
    isValid: null,
    from: Ht(te, Y, ne.Left, !0),
    fromHandle: Y,
    fromPosition: Y.position,
    fromNode: te,
    to: T,
    toHandle: null,
    toPosition: Os[Y.position],
    toNode: null,
    pointer: T
  };
  function L() {
    I = !0, y(J), g?.(e, { nodeId: r, handleId: o, handleType: j });
  }
  S === 0 && L();
  function U(re) {
    if (!I) {
      const { x: he, y: ve } = Je(re), ke = he - $, Ie = ve - H;
      if (!(ke * ke + Ie * Ie > S * S))
        return;
      L();
    }
    if (!N() || !Y) {
      ae(re);
      return;
    }
    const Q = v();
    T = Je(re, A), M = _g(gn(T, Q, !1, [1, 1]), n, u, Y), R || (Z(), R = !0);
    const ee = ul(re, {
      handle: M,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: a ? "target" : "source",
      isValidConnection: m,
      doc: P,
      lib: l,
      flowId: f,
      nodeLookup: u
    });
    F = ee.handleDomNode, V = ee.connection, B = Ag(!!M, ee.isValid);
    const le = u.get(r), O = le ? Ht(le, Y, ne.Left, !0) : J.from, G = {
      ...J,
      from: O,
      isValid: B,
      to: ee.toHandle && B ? dn({ x: ee.toHandle.x, y: ee.toHandle.y }, Q) : T,
      toHandle: ee.toHandle,
      toPosition: B && ee.toHandle ? ee.toHandle.position : Os[Y.position],
      toNode: ee.toHandle ? u.get(ee.toHandle.nodeId) : null,
      pointer: T
    };
    y(G), J = G;
  }
  function ae(re) {
    if (!("touches" in re && re.touches.length > 0)) {
      if (I) {
        (M || F) && V && B && b?.(V);
        const { inProgress: Q, ...ee } = J, le = {
          ...ee,
          toPosition: J.toHandle ? J.toPosition : null
        };
        w?.(re, le), i && E?.(re, le);
      }
      p(), cancelAnimationFrame(X), R = !1, B = !1, V = null, F = null, P.removeEventListener("mousemove", U), P.removeEventListener("mouseup", ae), P.removeEventListener("touchmove", U), P.removeEventListener("touchend", ae);
    }
  }
  P.addEventListener("mousemove", U), P.addEventListener("mouseup", ae), P.addEventListener("touchmove", U), P.addEventListener("touchend", ae);
}
function ul(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: i, doc: a, lib: c, flowId: u, isValidConnection: l = ll, nodeLookup: d }) {
  const f = i === "target", h = t ? a.querySelector(`.${c}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: p, y: g } = Je(e), b = a.elementFromPoint(p, g), w = b?.classList.contains(`${c}-flow__handle`) ? b : h, m = {
    handleDomNode: w,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (w) {
    const E = cl(void 0, w), y = w.getAttribute("data-nodeid"), v = w.getAttribute("data-handleid"), N = w.classList.contains("connectable"), k = w.classList.contains("connectableend");
    if (!y || !E)
      return m;
    const S = {
      source: f ? y : o,
      sourceHandle: f ? v : r,
      target: f ? o : y,
      targetHandle: f ? r : v
    };
    m.connection = S;
    const P = N && k && (n === cn.Strict ? f && E === "source" || !f && E === "target" : y !== o || v !== r);
    m.isValid = P && l(S), m.toHandle = al(y, E, v, d, n, !0);
  }
  return m;
}
const wi = {
  onPointerDown: Mg,
  isValid: ul
};
function Dg({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = We(e);
  function i({ translateExtent: c, width: u, height: l, zoomStep: d = 1, pannable: f = !0, zoomable: h = !0, inversePan: p = !1 }) {
    const g = (y) => {
      if (y.sourceEvent.type !== "wheel" || !t)
        return;
      const v = n(), N = y.sourceEvent.ctrlKey && Gn() ? 10 : 1, k = -y.sourceEvent.deltaY * (y.sourceEvent.deltaMode === 1 ? 0.05 : y.sourceEvent.deltaMode ? 1 : 2e-3) * d, S = v[2] * Math.pow(2, k * N);
      t.scaleTo(S);
    };
    let b = [0, 0];
    const w = (y) => {
      (y.sourceEvent.type === "mousedown" || y.sourceEvent.type === "touchstart") && (b = [
        y.sourceEvent.clientX ?? y.sourceEvent.touches[0].clientX,
        y.sourceEvent.clientY ?? y.sourceEvent.touches[0].clientY
      ]);
    }, m = (y) => {
      const v = n();
      if (y.sourceEvent.type !== "mousemove" && y.sourceEvent.type !== "touchmove" || !t)
        return;
      const N = [
        y.sourceEvent.clientX ?? y.sourceEvent.touches[0].clientX,
        y.sourceEvent.clientY ?? y.sourceEvent.touches[0].clientY
      ], k = [N[0] - b[0], N[1] - b[1]];
      b = N;
      const S = o() * Math.max(v[2], Math.log(v[2])) * (p ? -1 : 1), _ = {
        x: v[0] - k[0] * S,
        y: v[1] - k[1] * S
      }, P = [
        [0, 0],
        [u, l]
      ];
      t.setViewportConstrained({
        x: _.x,
        y: _.y,
        zoom: v[2]
      }, P, c);
    }, E = zc().on("start", w).on("zoom", f ? m : null).on("zoom.wheel", h ? g : null);
    r.call(E, {});
  }
  function a() {
    r.on("zoom", null);
  }
  return {
    update: i,
    destroy: a,
    pointer: Ue
  };
}
const gr = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), Yr = ({ x: e, y: t, zoom: n }) => fr.translate(e, t).scale(n), en = (e, t) => e.target.closest(`.${t}`), dl = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Pg = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, Ur = (e, t = 0, n = Pg, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, fl = (e) => {
  const t = e.ctrlKey && Gn() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Tg({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: i, zoomOnPinch: a, onPanZoomStart: c, onPanZoom: u, onPanZoomEnd: l }) {
  return (d) => {
    if (en(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && a) {
      const w = Ue(d), m = fl(d), E = f * Math.pow(2, m);
      o.scaleTo(n, E, w, d);
      return;
    }
    const h = d.deltaMode === 1 ? 20 : 1;
    let p = r === $t.Vertical ? 0 : d.deltaX * h, g = r === $t.Horizontal ? 0 : d.deltaY * h;
    !Gn() && d.shiftKey && r !== $t.Vertical && (p = d.deltaY * h, g = 0), o.translateBy(
      n,
      -(p / f) * i,
      -(g / f) * i,
      // @ts-ignore
      { internal: !0 }
    );
    const b = gr(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(d, b), e.panScrollTimeout = setTimeout(() => {
      l?.(d, b), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(d, b));
  };
}
function $g({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const i = o.type === "wheel", a = !t && i && !o.ctrlKey, c = en(o, e);
    if (o.ctrlKey && i && c && o.preventDefault(), a || c)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function Rg({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = gr(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function zg({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (i) => {
    e.usedRightMouseButton = !!(n && dl(t, e.mouseButton ?? 0)), i.sourceEvent?.sync || o([i.transform.x, i.transform.y, i.transform.k]), r && !i.sourceEvent?.internal && r?.(i.sourceEvent, gr(i.transform));
  };
}
function Lg({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: i }) {
  return (a) => {
    if (!a.sourceEvent?.internal && (e.isZoomingOrPanning = !1, i && dl(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && a.sourceEvent && i(a.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
      const c = gr(a.transform);
      e.prevViewport = c, clearTimeout(e.timerId), e.timerId = setTimeout(
        () => {
          r?.(a.sourceEvent, c);
        },
        // we need a setTimeout for panOnScroll to suppress multiple end events fired during scroll
        n ? 150 : 0
      );
    }
  };
}
function Vg({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: i, userSelectionActive: a, noWheelClassName: c, noPanClassName: u, lib: l, connectionInProgress: d }) {
  return (f) => {
    const h = e || t, p = n && f.ctrlKey, g = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (en(f, `${l}-flow__node`) || en(f, `${l}-flow__edge`)))
      return !0;
    if (!o && !h && !r && !i && !n || a || d && !g || en(f, c) && g || en(f, u) && (!g || r && g && !e) || !n && f.ctrlKey && g)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!h && !r && !p && g || !o && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(o) && !o.includes(f.button) && f.type === "mousedown")
      return !1;
    const b = Array.isArray(o) && o.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || g) && b;
  };
}
function Og({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: i, onPanZoomStart: a, onPanZoomEnd: c, onDraggingChange: u }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = zc().scaleExtent([t, n]).translateExtent(o), h = We(e).call(f);
  E({
    x: r.x,
    y: r.y,
    zoom: ln(r.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], o);
  const p = h.on("wheel.zoom"), g = h.on("dblclick.zoom");
  f.wheelDelta(fl);
  async function b(M, $) {
    return h ? new Promise((H) => {
      f?.interpolate($?.interpolate === "linear" ? Hn : Ro).transform(Ur(h, $?.duration, $?.ease, () => H(!0)), M);
    }) : !1;
  }
  function w({ noWheelClassName: M, noPanClassName: $, onPaneContextMenu: H, userSelectionActive: j, panOnScroll: A, panOnDrag: I, panOnScrollMode: D, panOnScrollSpeed: T, preventScrolling: R, zoomOnPinch: V, zoomOnScroll: B, zoomOnDoubleClick: F, zoomActivationKeyPressed: Z, lib: Y, onTransformChange: te, connectionInProgress: ce, paneClickDistance: J, selectionOnDrag: L }) {
    j && !l.isZoomingOrPanning && m();
    const U = A && !Z && !j;
    f.clickDistance(L ? 1 / 0 : !Ge(J) || J < 0 ? 0 : J);
    const ae = U ? Tg({
      zoomPanValues: l,
      noWheelClassName: M,
      d3Selection: h,
      d3Zoom: f,
      panOnScrollMode: D,
      panOnScrollSpeed: T,
      zoomOnPinch: V,
      onPanZoomStart: a,
      onPanZoom: i,
      onPanZoomEnd: c
    }) : $g({
      noWheelClassName: M,
      preventScrolling: R,
      d3ZoomHandler: p
    });
    h.on("wheel.zoom", ae, { passive: !1 });
    const re = Rg({
      zoomPanValues: l,
      onDraggingChange: u,
      onPanZoomStart: a
    });
    f.on("start", re);
    const Q = zg({
      zoomPanValues: l,
      panOnDrag: I,
      onPaneContextMenu: !!H,
      onPanZoom: i,
      onTransformChange: te
    });
    f.on("zoom", Q);
    const ee = Lg({
      zoomPanValues: l,
      panOnDrag: I,
      panOnScroll: A,
      onPaneContextMenu: H,
      onPanZoomEnd: c,
      onDraggingChange: u
    });
    f.on("end", ee);
    const le = Vg({
      zoomActivationKeyPressed: Z,
      panOnDrag: I,
      zoomOnScroll: B,
      panOnScroll: A,
      zoomOnDoubleClick: F,
      zoomOnPinch: V,
      userSelectionActive: j,
      noPanClassName: $,
      noWheelClassName: M,
      lib: Y,
      connectionInProgress: ce
    });
    f.filter(le), F ? h.on("dblclick.zoom", g) : h.on("dblclick.zoom", null);
  }
  function m() {
    f.on("zoom", null);
  }
  async function E(M, $, H) {
    const j = Yr(M), A = f?.constrain()(j, $, H);
    return A && await b(A), A;
  }
  async function y(M, $) {
    const H = Yr(M);
    return await b(H, $), H;
  }
  function v(M) {
    if (h) {
      const $ = Yr(M), H = h.property("__zoom");
      (H.k !== M.zoom || H.x !== M.x || H.y !== M.y) && f?.transform(h, $, null, { sync: !0 });
    }
  }
  function N() {
    const M = h ? Rc(h.node()) : { x: 0, y: 0, k: 1 };
    return { x: M.x, y: M.y, zoom: M.k };
  }
  async function k(M, $) {
    return h ? new Promise((H) => {
      f?.interpolate($?.interpolate === "linear" ? Hn : Ro).scaleTo(Ur(h, $?.duration, $?.ease, () => H(!0)), M);
    }) : !1;
  }
  async function S(M, $) {
    return h ? new Promise((H) => {
      f?.interpolate($?.interpolate === "linear" ? Hn : Ro).scaleBy(Ur(h, $?.duration, $?.ease, () => H(!0)), M);
    }) : !1;
  }
  function _(M) {
    f?.scaleExtent(M);
  }
  function P(M) {
    f?.translateExtent(M);
  }
  function X(M) {
    const $ = !Ge(M) || M < 0 ? 0 : M;
    f?.clickDistance($);
  }
  return {
    update: w,
    destroy: m,
    setViewport: y,
    setViewportConstrained: E,
    getViewport: N,
    scaleTo: k,
    scaleBy: S,
    setScaleExtent: _,
    setTranslateExtent: P,
    syncViewport: v,
    setClickDistance: X
  };
}
var fn;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(fn || (fn = {}));
function Hg({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: i }) {
  const a = e - t, c = n - o, u = [a > 0 ? 1 : a < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return a && r && (u[0] = u[0] * -1), c && i && (u[1] = u[1] * -1), u;
}
function Qs(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: r
  };
}
function xt(e, t) {
  return Math.max(0, t - e);
}
function wt(e, t) {
  return Math.max(0, e - t);
}
function Ao(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function ea(e, t) {
  return e ? !t : t;
}
function Fg(e, t, n, o, r, i, a, c) {
  let { affectsX: u, affectsY: l } = t;
  const { isHorizontal: d, isVertical: f } = t, h = d && f, { xSnapped: p, ySnapped: g } = n, { minWidth: b, maxWidth: w, minHeight: m, maxHeight: E } = o, { x: y, y: v, width: N, height: k, aspectRatio: S } = e;
  let _ = Math.floor(d ? p - e.pointerX : 0), P = Math.floor(f ? g - e.pointerY : 0);
  const X = N + (u ? -_ : _), M = k + (l ? -P : P), $ = -i[0] * N, H = -i[1] * k;
  let j = Ao(X, b, w), A = Ao(M, m, E);
  if (a) {
    let T = 0, R = 0;
    u && _ < 0 ? T = xt(y + _ + $, a[0][0]) : !u && _ > 0 && (T = wt(y + X + $, a[1][0])), l && P < 0 ? R = xt(v + P + H, a[0][1]) : !l && P > 0 && (R = wt(v + M + H, a[1][1])), j = Math.max(j, T), A = Math.max(A, R);
  }
  if (c) {
    let T = 0, R = 0;
    u && _ > 0 ? T = wt(y + _, c[0][0]) : !u && _ < 0 && (T = xt(y + X, c[1][0])), l && P > 0 ? R = wt(v + P, c[0][1]) : !l && P < 0 && (R = xt(v + M, c[1][1])), j = Math.max(j, T), A = Math.max(A, R);
  }
  if (r) {
    if (d) {
      const T = Ao(X / S, m, E) * S;
      if (j = Math.max(j, T), a) {
        let R = 0;
        !u && !l || u && !l && h ? R = wt(v + H + X / S, a[1][1]) * S : R = xt(v + H + (u ? _ : -_) / S, a[0][1]) * S, j = Math.max(j, R);
      }
      if (c) {
        let R = 0;
        !u && !l || u && !l && h ? R = xt(v + X / S, c[1][1]) * S : R = wt(v + (u ? _ : -_) / S, c[0][1]) * S, j = Math.max(j, R);
      }
    }
    if (f) {
      const T = Ao(M * S, b, w) / S;
      if (A = Math.max(A, T), a) {
        let R = 0;
        !u && !l || l && !u && h ? R = wt(y + M * S + $, a[1][0]) / S : R = xt(y + (l ? P : -P) * S + $, a[0][0]) / S, A = Math.max(A, R);
      }
      if (c) {
        let R = 0;
        !u && !l || l && !u && h ? R = xt(y + M * S, c[1][0]) / S : R = wt(y + (l ? P : -P) * S, c[0][0]) / S, A = Math.max(A, R);
      }
    }
  }
  P = P + (P < 0 ? A : -A), _ = _ + (_ < 0 ? j : -j), r && (h ? X > M * S ? P = (ea(u, l) ? -_ : _) / S : _ = (ea(u, l) ? -P : P) * S : d ? (P = _ / S, l = u) : (_ = P * S, u = l));
  const I = u ? y + _ : y, D = l ? v + P : v;
  return {
    width: N + (u ? -_ : _),
    height: k + (l ? -P : P),
    x: i[0] * _ * (u ? -1 : 1) + I,
    y: i[1] * P * (l ? -1 : 1) + D
  };
}
const hl = { width: 0, height: 0, x: 0, y: 0 }, Wg = {
  ...hl,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function Bg(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, i = e.measured.width ?? 0, a = e.measured.height ?? 0, c = n[0] * i, u = n[1] * a;
  return [
    [o - c, r - u],
    [o + i - c, r + a - u]
  ];
}
function Xg({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const i = We(e);
  let a = {
    controlDirection: Qs("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: l, boundaries: d, keepAspectRatio: f, resizeDirection: h, onResizeStart: p, onResize: g, onResizeEnd: b, shouldResize: w }) {
    let m = { ...hl }, E = { ...Wg };
    a = {
      boundaries: d,
      resizeDirection: h,
      keepAspectRatio: f,
      controlDirection: Qs(l)
    };
    let y, v = null, N = [], k, S, _, P = !1;
    const X = Nc().on("start", (M) => {
      const { nodeLookup: $, transform: H, snapGrid: j, snapToGrid: A, nodeOrigin: I, paneDomNode: D } = n();
      if (y = $.get(t), !y)
        return;
      v = D?.getBoundingClientRect() ?? null;
      const { xSnapped: T, ySnapped: R } = Fn(M.sourceEvent, {
        transform: H,
        snapGrid: j,
        snapToGrid: A,
        containerBounds: v
      });
      m = {
        width: y.measured.width ?? 0,
        height: y.measured.height ?? 0,
        x: y.position.x ?? 0,
        y: y.position.y ?? 0
      }, E = {
        ...m,
        pointerX: T,
        pointerY: R,
        aspectRatio: m.width / m.height
      }, k = void 0, S = Ot(y.extent) ? y.extent : void 0, y.parentId && (y.extent === "parent" || y.expandParent) && (k = $.get(y.parentId)), k && y.extent === "parent" && (S = [
        [0, 0],
        [k.measured.width, k.measured.height]
      ]), N = [], _ = void 0;
      for (const [V, B] of $)
        if (B.parentId === t && (N.push({
          id: V,
          position: { ...B.position },
          extent: B.extent
        }), B.extent === "parent" || B.expandParent)) {
          const F = Bg(B, y, B.origin ?? I);
          _ ? _ = [
            [Math.min(F[0][0], _[0][0]), Math.min(F[0][1], _[0][1])],
            [Math.max(F[1][0], _[1][0]), Math.max(F[1][1], _[1][1])]
          ] : _ = F;
        }
      p?.(M, { ...m });
    }).on("drag", (M) => {
      const { transform: $, snapGrid: H, snapToGrid: j, nodeOrigin: A } = n(), I = Fn(M.sourceEvent, {
        transform: $,
        snapGrid: H,
        snapToGrid: j,
        containerBounds: v
      }), D = [];
      if (!y)
        return;
      const { x: T, y: R, width: V, height: B } = m, F = {}, Z = y.origin ?? A, { width: Y, height: te, x: ce, y: J } = Fg(E, a.controlDirection, I, a.boundaries, a.keepAspectRatio, Z, S, _), L = Y !== V, U = te !== B, ae = ce !== T && L, re = J !== R && U;
      if (!ae && !re && !L && !U)
        return;
      if ((ae || re || Z[0] === 1 || Z[1] === 1) && (F.x = ae ? ce : m.x, F.y = re ? J : m.y, m.x = F.x, m.y = F.y, N.length > 0)) {
        const O = ce - T, G = J - R;
        for (const he of N)
          he.position = {
            x: he.position.x - O + Z[0] * (Y - V),
            y: he.position.y - G + Z[1] * (te - B)
          }, D.push(he);
      }
      if ((L || U) && (F.width = L && (!a.resizeDirection || a.resizeDirection === "horizontal") ? Y : m.width, F.height = U && (!a.resizeDirection || a.resizeDirection === "vertical") ? te : m.height, m.width = F.width, m.height = F.height), k && y.expandParent) {
        const O = Z[0] * (F.width ?? 0);
        F.x && F.x < O && (m.x = O, E.x = E.x - (F.x - O));
        const G = Z[1] * (F.height ?? 0);
        F.y && F.y < G && (m.y = G, E.y = E.y - (F.y - G));
      }
      const Q = Hg({
        width: m.width,
        prevWidth: V,
        height: m.height,
        prevHeight: B,
        affectsX: a.controlDirection.affectsX,
        affectsY: a.controlDirection.affectsY
      }), ee = { ...m, direction: Q };
      w?.(M, ee) !== !1 && (P = !0, g?.(M, ee), o(F, D));
    }).on("end", (M) => {
      P && (b?.(M, { ...m }), r?.({ ...m }), P = !1);
    });
    i.call(X);
  }
  function u() {
    i.on(".drag", null);
  }
  return {
    update: c,
    destroy: u
  };
}
var Zr = { exports: {} }, Gr = {}, Jr = { exports: {} }, Qr = {};
var ta;
function Kg() {
  if (ta) return Qr;
  ta = 1;
  var e = jt;
  function t(f, h) {
    return f === h && (f !== 0 || 1 / f === 1 / h) || f !== f && h !== h;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, i = e.useLayoutEffect, a = e.useDebugValue;
  function c(f, h) {
    var p = h(), g = o({ inst: { value: p, getSnapshot: h } }), b = g[0].inst, w = g[1];
    return i(
      function() {
        b.value = p, b.getSnapshot = h, u(b) && w({ inst: b });
      },
      [f, p, h]
    ), r(
      function() {
        return u(b) && w({ inst: b }), f(function() {
          u(b) && w({ inst: b });
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
  return Qr.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : d, Qr;
}
var na;
function qg() {
  return na || (na = 1, Jr.exports = Kg()), Jr.exports;
}
var oa;
function Yg() {
  if (oa) return Gr;
  oa = 1;
  var e = jt, t = qg();
  function n(l, d) {
    return l === d && (l !== 0 || 1 / l === 1 / d) || l !== l && d !== d;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, i = e.useRef, a = e.useEffect, c = e.useMemo, u = e.useDebugValue;
  return Gr.useSyncExternalStoreWithSelector = function(l, d, f, h, p) {
    var g = i(null);
    if (g.current === null) {
      var b = { hasValue: !1, value: null };
      g.current = b;
    } else b = g.current;
    g = c(
      function() {
        function m(k) {
          if (!E) {
            if (E = !0, y = k, k = h(k), p !== void 0 && b.hasValue) {
              var S = b.value;
              if (p(S, k))
                return v = S;
            }
            return v = k;
          }
          if (S = v, o(y, k)) return S;
          var _ = h(k);
          return p !== void 0 && p(S, _) ? (y = k, S) : (y = k, v = _);
        }
        var E = !1, y, v, N = f === void 0 ? null : f;
        return [
          function() {
            return m(d());
          },
          N === null ? void 0 : function() {
            return m(N());
          }
        ];
      },
      [d, f, h, p]
    );
    var w = r(l, g[0], g[1]);
    return a(
      function() {
        b.hasValue = !0, b.value = w;
      },
      [w]
    ), u(w), w;
  }, Gr;
}
var ra;
function Ug() {
  return ra || (ra = 1, Zr.exports = Yg()), Zr.exports;
}
var Zg = Ug();
const Gg = /* @__PURE__ */ fd(Zg), Jg = {}, ia = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (d, f) => {
    const h = typeof d == "function" ? d(t) : d;
    if (!Object.is(h, t)) {
      const p = t;
      t = f ?? (typeof h != "object" || h === null) ? h : Object.assign({}, t, h), n.forEach((g) => g(t, p));
    }
  }, r = () => t, u = { setState: o, getState: r, getInitialState: () => l, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (Jg ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(o, r, u);
  return u;
}, Qg = (e) => e ? ia(e) : ia, { useDebugValue: ey } = jt, { useSyncExternalStoreWithSelector: ty } = Gg, ny = (e) => e;
function pl(e, t = ny, n) {
  const o = ty(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return ey(o), o;
}
const sa = (e, t) => {
  const n = Qg(e), o = (r, i = t) => pl(n, r, i);
  return Object.assign(o, n), o;
}, oy = (e, t) => e ? sa(e, t) : sa;
function be(e, t) {
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
var ei = { exports: {} }, Me = {};
var aa;
function ry() {
  if (aa) return Me;
  aa = 1;
  var e = jt;
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
  }, r = /* @__PURE__ */ Symbol.for("react.portal");
  function i(u, l, d) {
    var f = 3 < arguments.length && arguments[3] !== void 0 ? arguments[3] : null;
    return {
      $$typeof: r,
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
  return Me.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, Me.createPortal = function(u, l) {
    var d = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!l || l.nodeType !== 1 && l.nodeType !== 9 && l.nodeType !== 11)
      throw Error(t(299));
    return i(u, l, null, d);
  }, Me.flushSync = function(u) {
    var l = a.T, d = o.p;
    try {
      if (a.T = null, o.p = 2, u) return u();
    } finally {
      a.T = l, o.p = d, o.d.f();
    }
  }, Me.preconnect = function(u, l) {
    typeof u == "string" && (l ? (l = l.crossOrigin, l = typeof l == "string" ? l === "use-credentials" ? l : "" : void 0) : l = null, o.d.C(u, l));
  }, Me.prefetchDNS = function(u) {
    typeof u == "string" && o.d.D(u);
  }, Me.preinit = function(u, l) {
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
  }, Me.preinitModule = function(u, l) {
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
  }, Me.preload = function(u, l) {
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
  }, Me.preloadModule = function(u, l) {
    if (typeof u == "string")
      if (l) {
        var d = c(l.as, l.crossOrigin);
        o.d.m(u, {
          as: typeof l.as == "string" && l.as !== "script" ? l.as : void 0,
          crossOrigin: d,
          integrity: typeof l.integrity == "string" ? l.integrity : void 0
        });
      } else o.d.m(u);
  }, Me.requestFormReset = function(u) {
    o.d.r(u);
  }, Me.unstable_batchedUpdates = function(u, l) {
    return u(l);
  }, Me.useFormState = function(u, l, d) {
    return a.H.useFormState(u, l, d);
  }, Me.useFormStatus = function() {
    return a.H.useHostTransitionStatus();
  }, Me.version = "19.2.7", Me;
}
var ca;
function iy() {
  if (ca) return ei.exports;
  ca = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), ei.exports = ry(), ei.exports;
}
var sy = iy();
const yr = Ai(null), ay = yr.Provider, gl = Xe.error001("react");
function fe(e, t) {
  const n = to(yr);
  if (n === null)
    throw new Error(gl);
  return pl(n, e, t);
}
function Ne() {
  const e = to(yr);
  if (e === null)
    throw new Error(gl);
  return pe(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const la = { display: "none" }, cy = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, yl = "react-flow__node-desc", ml = "react-flow__edge-desc", ly = "react-flow__aria-live", uy = (e) => e.ariaLiveMessage, dy = (e) => e.ariaLabelConfig;
function fy({ rfId: e }) {
  const t = fe(uy);
  return s.jsx("div", { id: `${ly}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: cy, children: t });
}
function hy({ rfId: e, disableKeyboardA11y: t }) {
  const n = fe(dy);
  return s.jsxs(s.Fragment, { children: [s.jsx("div", { id: `${yl}-${e}`, style: la, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), s.jsx("div", { id: `${ml}-${e}`, style: la, children: n["edge.a11yDescription.default"] }), !t && s.jsx(fy, { rfId: e })] });
}
const mr = ar(({ position: e = "top-left", children: t, className: n, style: o, ...r }, i) => {
  const a = `${e}`.split("-");
  return s.jsx("div", { className: Ce(["react-flow__panel", n, ...a]), style: o, ref: i, ...r, children: t });
});
mr.displayName = "Panel";
function py({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : s.jsx(mr, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: s.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const gy = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, Mo = (e) => e.id;
function yy(e, t) {
  return be(e.selectedNodes.map(Mo), t.selectedNodes.map(Mo)) && be(e.selectedEdges.map(Mo), t.selectedEdges.map(Mo));
}
function my({ onSelectionChange: e }) {
  const t = Ne(), { selectedNodes: n, selectedEdges: o } = fe(gy, yy);
  return oe(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((i) => i(r));
  }, [n, o, e]), null;
}
const xy = (e) => !!e.onSelectionChangeHandlers;
function wy({ onSelectionChange: e }) {
  const t = fe(xy);
  return e || t ? s.jsx(my, { onSelectionChange: e }) : null;
}
const xl = [0, 0], vy = { x: 0, y: 0, zoom: 1 }, by = [
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
], ua = [...by, "rfId"], Ny = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), da = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: Yn,
  nodeOrigin: xl,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function Sy(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: i, setNodeExtent: a, reset: c, setDefaultNodesAndEdges: u } = fe(Ny, be), l = Ne();
  oe(() => (u(e.defaultNodes, e.defaultEdges), () => {
    d.current = da, c();
  }), []);
  const d = se(da);
  return oe(
    () => {
      for (const f of ua) {
        const h = e[f], p = d.current[f];
        h !== p && (typeof e[f] > "u" || (f === "nodes" ? t(h) : f === "edges" ? n(h) : f === "minZoom" ? o(h) : f === "maxZoom" ? r(h) : f === "translateExtent" ? i(h) : f === "nodeExtent" ? a(h) : f === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: rg(h) }) : f === "fitView" ? l.setState({ fitViewQueued: h }) : f === "fitViewOptions" ? l.setState({ fitViewOptions: h }) : l.setState({ [f]: h })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    ua.map((f) => e[f])
  ), null;
}
function fa() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function jy(e) {
  const [t, n] = K(e === "system" ? null : e);
  return oe(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = fa(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : fa()?.matches ? "dark" : "light";
}
const ha = typeof document < "u" ? document : null;
function Jn(e = null, t = { target: ha, actInsideInputWithModifier: !0 }) {
  const [n, o] = K(!1), r = se(!1), i = se(/* @__PURE__ */ new Set([])), [a, c] = pe(() => {
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
  return oe(() => {
    const u = t?.target ?? ha, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (p) => {
        if (r.current = p.ctrlKey || p.metaKey || p.shiftKey || p.altKey, (!r.current || r.current && !l) && Zc(p))
          return !1;
        const b = ga(p.code, c);
        if (i.current.add(p[b]), pa(a, i.current, !1)) {
          const w = p.composedPath?.()?.[0] || p.target, m = w?.nodeName === "BUTTON" || w?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !m) && p.preventDefault(), o(!0);
        }
      }, f = (p) => {
        const g = ga(p.code, c);
        pa(a, i.current, !0) ? (o(!1), i.current.clear()) : i.current.delete(p[g]), p.key === "Meta" && i.current.clear(), r.current = !1;
      }, h = () => {
        i.current.clear(), o(!1);
      };
      return u?.addEventListener("keydown", d), u?.addEventListener("keyup", f), window.addEventListener("blur", h), window.addEventListener("contextmenu", h), () => {
        u?.removeEventListener("keydown", d), u?.removeEventListener("keyup", f), window.removeEventListener("blur", h), window.removeEventListener("contextmenu", h);
      };
    }
  }, [e, o]), n;
}
function pa(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function ga(e, t) {
  return t.includes(e) ? "code" : "key";
}
const Ey = () => {
  const e = Ne();
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
      const { transform: [o, r, i], panZoom: a } = e.getState();
      return a ? (await a.setViewport({
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
      const { width: o, height: r, minZoom: i, maxZoom: a, panZoom: c } = e.getState(), u = Hi(t, o, r, i, a, n?.padding ?? 0.1);
      return c ? (await c.setViewport(u, {
        duration: n?.duration,
        ease: n?.ease,
        interpolate: n?.interpolate
      }), !0) : !1;
    },
    screenToFlowPosition: (t, n = {}) => {
      const { transform: o, snapGrid: r, snapToGrid: i, domNode: a } = e.getState();
      if (!a)
        return t;
      const { x: c, y: u } = a.getBoundingClientRect(), l = {
        x: t.x - c,
        y: t.y - u
      }, d = n.snapGrid ?? r, f = n.snapToGrid ?? i;
      return gn(l, o, f, d);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: r, y: i } = o.getBoundingClientRect(), a = dn(t, n);
      return {
        x: a.x + r,
        y: a.y + i
      };
    }
  }), []);
};
function wl(e, t) {
  const n = [], o = /* @__PURE__ */ new Map(), r = [];
  for (const i of e)
    if (i.type === "add") {
      r.push(i);
      continue;
    } else if (i.type === "remove" || i.type === "replace")
      o.set(i.id, [i]);
    else {
      const a = o.get(i.id);
      a ? a.push(i) : o.set(i.id, [i]);
    }
  for (const i of t) {
    const a = o.get(i.id);
    if (!a) {
      n.push(i);
      continue;
    }
    if (a[0].type === "remove")
      continue;
    if (a[0].type === "replace") {
      n.push({ ...a[0].item });
      continue;
    }
    const c = { ...i };
    for (const u of a)
      ky(u, c);
    n.push(c);
  }
  return r.length && r.forEach((i) => {
    i.index !== void 0 ? n.splice(i.index, 0, { ...i.item }) : n.push({ ...i.item });
  }), n;
}
function ky(e, t) {
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
function vl(e, t) {
  return wl(e, t);
}
function bl(e, t) {
  return wl(e, t);
}
function Dt(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function tn(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [r, i] of e) {
    const a = t.has(r);
    !(i.selected === void 0 && !a) && i.selected !== a && (n && (i.selected = a), o.push(Dt(i.id, a)));
  }
  return o;
}
function ya({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((r) => [r.id, r]));
  for (const [r, i] of e.entries()) {
    const a = t.get(i.id), c = a?.internals?.userNode ?? a;
    c !== void 0 && c !== i && n.push({ id: i.id, item: i, type: "replace" }), c === void 0 && n.push({ item: i, type: "add", index: r });
  }
  for (const [r] of t)
    o.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function ma(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const Nl = Kc();
function Sl(e, t, n = {}) {
  return lg(e, t, {
    ...n,
    onError: n.onError ?? Nl
  });
}
function Cy(e, t, n, o = { shouldReplaceId: !0 }) {
  return ug(e, t, n, {
    ...o,
    onError: o.onError ?? Nl
  });
}
const xa = (e) => Up(e), Iy = (e) => Fc(e);
function jl(e) {
  return ar(e);
}
const _y = typeof window < "u" ? ld : oe;
function wa(e) {
  const [t, n] = K(BigInt(0)), [o] = K(() => Ay(() => n((r) => r + BigInt(1))));
  return _y(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function Ay(e) {
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
const El = Ai(null);
function My({ children: e }) {
  const t = Ne(), n = de((c) => {
    const { nodes: u = [], setNodes: l, hasDefaultNodes: d, onNodesChange: f, nodeLookup: h, fitViewQueued: p, onNodesChangeMiddlewareMap: g } = t.getState();
    let b = u;
    for (const m of c)
      b = typeof m == "function" ? m(b) : m;
    let w = ya({
      items: b,
      lookup: h
    });
    for (const m of g.values())
      w = m(w);
    d && l(b), w.length > 0 ? f?.(w) : p && window.requestAnimationFrame(() => {
      const { fitViewQueued: m, nodes: E, setNodes: y } = t.getState();
      m && y(E);
    });
  }, []), o = wa(n), r = de((c) => {
    const { edges: u = [], setEdges: l, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: h } = t.getState();
    let p = u;
    for (const g of c)
      p = typeof g == "function" ? g(p) : g;
    d ? l(p) : f && f(ya({
      items: p,
      lookup: h
    }));
  }, []), i = wa(r), a = pe(() => ({ nodeQueue: o, edgeQueue: i }), []);
  return s.jsx(El.Provider, { value: a, children: e });
}
function Dy() {
  const e = to(El);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Py = (e) => !!e.panZoom;
function Yi() {
  const e = Ey(), t = Ne(), n = Dy(), o = fe(Py), r = pe(() => {
    const i = (f) => t.getState().nodeLookup.get(f), a = (f) => {
      n.nodeQueue.push(f);
    }, c = (f) => {
      n.edgeQueue.push(f);
    }, u = (f) => {
      const { nodeLookup: h, nodeOrigin: p } = t.getState(), g = xa(f) ? f : h.get(f.id), b = g.parentId ? Yc(g.position, g.measured, g.parentId, h, p) : g.position, w = {
        ...g,
        position: b,
        width: g.measured?.width ?? g.width,
        height: g.measured?.height ?? g.height
      };
      return un(w);
    }, l = (f, h, p = { replace: !1 }) => {
      a((g) => g.map((b) => {
        if (b.id === f) {
          const w = typeof h == "function" ? h(b) : h;
          return p.replace && xa(w) ? w : { ...b, ...w };
        }
        return b;
      }));
    }, d = (f, h, p = { replace: !1 }) => {
      c((g) => g.map((b) => {
        if (b.id === f) {
          const w = typeof h == "function" ? h(b) : h;
          return p.replace && Iy(w) ? w : { ...b, ...w };
        }
        return b;
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
        const { nodes: f = [], edges: h = [], transform: p } = t.getState(), [g, b, w] = p;
        return {
          nodes: f.map((m) => ({ ...m })),
          edges: h.map((m) => ({ ...m })),
          viewport: {
            x: g,
            y: b,
            zoom: w
          }
        };
      },
      deleteElements: async ({ nodes: f = [], edges: h = [] }) => {
        const { nodes: p, edges: g, onNodesDelete: b, onEdgesDelete: w, triggerNodeChanges: m, triggerEdgeChanges: E, onDelete: y, onBeforeDelete: v } = t.getState(), { nodes: N, edges: k } = await eg({
          nodesToRemove: f,
          edgesToRemove: h,
          nodes: p,
          edges: g,
          onBeforeDelete: v
        }), S = k.length > 0, _ = N.length > 0;
        if (S) {
          const P = k.map(ma);
          w?.(k), E(P);
        }
        if (_) {
          const P = N.map(ma);
          b?.(N), m(P);
        }
        return (_ || S) && y?.({ nodes: N, edges: k }), { deletedNodes: N, deletedEdges: k };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, h = !0, p) => {
        const g = Fs(f), b = g ? f : u(f), w = p !== void 0;
        return b ? (p || t.getState().nodes).filter((m) => {
          const E = t.getState().nodeLookup.get(m.id);
          if (E && !g && (m.id === f.id || !E.internals.positionAbsolute))
            return !1;
          const y = un(w ? m : E), v = Zn(y, b);
          return h && v > 0 || v >= y.width * y.height || v >= b.width * b.height;
        }) : [];
      },
      isNodeIntersecting: (f, h, p = !0) => {
        const b = Fs(f) ? f : u(f);
        if (!b)
          return !1;
        const w = Zn(b, h);
        return p && w > 0 || w >= h.width * h.height || w >= b.width * b.height;
      },
      updateNode: l,
      updateNodeData: (f, h, p = { replace: !1 }) => {
        l(f, (g) => {
          const b = typeof h == "function" ? h(g) : h;
          return p.replace ? { ...g, data: b } : { ...g, data: { ...g.data, ...b } };
        }, p);
      },
      updateEdge: d,
      updateEdgeData: (f, h, p = { replace: !1 }) => {
        d(f, (g) => {
          const b = typeof h == "function" ? h(g) : h;
          return p.replace ? { ...g, data: b } : { ...g, data: { ...g.data, ...b } };
        }, p);
      },
      getNodesBounds: (f) => {
        const { nodeLookup: h, nodeOrigin: p } = t.getState();
        return Zp(f, { nodeLookup: h, nodeOrigin: p });
      },
      getHandleConnections: ({ type: f, id: h, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}-${f}${h ? `-${h}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: h, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}${f ? h ? `-${f}-${h}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const h = t.getState().fitViewResolver ?? og();
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
const va = (e) => e.selected, Ty = typeof window < "u" ? window : void 0;
function $y({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = Ne(), { deleteElements: o } = Yi(), r = Jn(e, { actInsideInputWithModifier: !1 }), i = Jn(t, { target: Ty });
  oe(() => {
    if (r) {
      const { edges: a, nodes: c } = n.getState();
      o({ nodes: c.filter(va), edges: a.filter(va) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), oe(() => {
    n.setState({ multiSelectionActive: i });
  }, [i]);
}
function Ry(e) {
  const t = Ne();
  oe(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = Fi(e.current);
      (o.height === 0 || o.width === 0) && t.getState().onError?.("004", Xe.error004()), t.setState({ width: o.width || 500, height: o.height || 500 });
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
const xr = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, zy = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function Ly({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: i = $t.Free, zoomOnDoubleClick: a = !0, panOnDrag: c = !0, defaultViewport: u, translateExtent: l, minZoom: d, maxZoom: f, zoomActivationKeyCode: h, preventScrolling: p = !0, children: g, noWheelClassName: b, noPanClassName: w, onViewportChange: m, isControlledViewport: E, paneClickDistance: y, selectionOnDrag: v }) {
  const N = Ne(), k = se(null), { userSelectionActive: S, lib: _, connectionInProgress: P } = fe(zy, be), X = Jn(h), M = se();
  Ry(k);
  const $ = de((H) => {
    m?.({ x: H[0], y: H[1], zoom: H[2] }), E || N.setState({ transform: H });
  }, [m, E]);
  return oe(() => {
    if (k.current) {
      M.current = Og({
        domNode: k.current,
        minZoom: d,
        maxZoom: f,
        translateExtent: l,
        viewport: u,
        onDraggingChange: (I) => N.setState((D) => D.paneDragging === I ? D : { paneDragging: I }),
        onPanZoomStart: (I, D) => {
          const { onViewportChangeStart: T, onMoveStart: R } = N.getState();
          R?.(I, D), T?.(D);
        },
        onPanZoom: (I, D) => {
          const { onViewportChange: T, onMove: R } = N.getState();
          R?.(I, D), T?.(D);
        },
        onPanZoomEnd: (I, D) => {
          const { onViewportChangeEnd: T, onMoveEnd: R } = N.getState();
          R?.(I, D), T?.(D);
        }
      });
      const { x: H, y: j, zoom: A } = M.current.getViewport();
      return N.setState({
        panZoom: M.current,
        transform: [H, j, A],
        domNode: k.current.closest(".react-flow")
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
      zoomOnDoubleClick: a,
      panOnDrag: c,
      zoomActivationKeyPressed: X,
      preventScrolling: p,
      noPanClassName: w,
      userSelectionActive: S,
      noWheelClassName: b,
      lib: _,
      onTransformChange: $,
      connectionInProgress: P,
      selectionOnDrag: v,
      paneClickDistance: y
    });
  }, [
    e,
    t,
    n,
    o,
    r,
    i,
    a,
    c,
    X,
    p,
    w,
    S,
    b,
    _,
    $,
    P,
    v,
    y
  ]), s.jsx("div", { className: "react-flow__renderer", ref: k, style: xr, children: g });
}
const Vy = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function Oy() {
  const { userSelectionActive: e, userSelectionRect: t } = fe(Vy, be);
  return e && t ? s.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const ti = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, Hy = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function Fy({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Un.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: i, selectionOnDrag: a, onSelectionStart: c, onSelectionEnd: u, onPaneClick: l, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: h, onPaneMouseMove: p, onPaneMouseLeave: g, children: b }) {
  const w = se(0), m = Ne(), { userSelectionActive: E, elementsSelectable: y, dragging: v, connectionInProgress: N, panBy: k, autoPanSpeed: S } = fe(Hy, be), _ = y && (e || E), P = se(null), X = se(), M = se(/* @__PURE__ */ new Set()), $ = se(/* @__PURE__ */ new Set()), H = se(!1), j = se({ x: 0, y: 0 }), A = se(!1), I = (L) => {
    if (H.current || N) {
      H.current = !1;
      return;
    }
    l?.(L), m.getState().resetSelectedElements(), m.setState({ nodesSelectionActive: !1 });
  }, D = (L) => {
    if (Array.isArray(o) && o?.includes(2)) {
      L.preventDefault();
      return;
    }
    d?.(L);
  }, T = f ? (L) => f(L) : void 0, R = (L) => {
    H.current && (L.stopPropagation(), H.current = !1);
  }, V = (L) => {
    const { domNode: U, transform: ae } = m.getState();
    if (X.current = U?.getBoundingClientRect(), !X.current)
      return;
    const re = L.target === P.current;
    if (!re && !!L.target.closest(".nokey") || !e || !(a && re || t) || L.button !== 0 || !L.isPrimary)
      return;
    L.target?.setPointerCapture?.(L.pointerId), H.current = !1;
    const { x: le, y: O } = Je(L.nativeEvent, X.current), G = gn({ x: le, y: O }, ae);
    m.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: G.x,
        startY: G.y,
        x: le,
        y: O
      }
    }), re || (L.stopPropagation(), L.preventDefault());
  };
  function B(L, U) {
    const { userSelectionRect: ae } = m.getState();
    if (!ae)
      return;
    const { transform: re, nodeLookup: Q, edgeLookup: ee, connectionLookup: le, triggerNodeChanges: O, triggerEdgeChanges: G, defaultEdgeOptions: he } = m.getState(), ve = { x: ae.startX, y: ae.startY }, { x: ke, y: Ie } = dn(ve, re), me = {
      startX: ve.x,
      startY: ve.y,
      x: L < ke ? L : ke,
      y: U < Ie ? U : Ie,
      width: Math.abs(L - ke),
      height: Math.abs(U - Ie)
    }, Ye = M.current, st = $.current;
    M.current = new Set(Vi(Q, me, re, n === Un.Partial, !0).map((ze) => ze.id)), $.current = /* @__PURE__ */ new Set();
    const at = he?.selectable ?? !0;
    for (const ze of M.current) {
      const Le = le.get(ze);
      if (Le)
        for (const { edgeId: De } of Le.values()) {
          const ct = ee.get(De);
          ct && (ct.selectable ?? at) && $.current.add(De);
        }
    }
    if (!Ws(Ye, M.current)) {
      const ze = tn(Q, M.current, !0);
      O(ze);
    }
    if (!Ws(st, $.current)) {
      const ze = tn(ee, $.current);
      G(ze);
    }
    m.setState({
      userSelectionRect: me,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function F() {
    if (!r || !X.current)
      return;
    const [L, U] = Oi(j.current, X.current, S);
    k({ x: L, y: U }).then((ae) => {
      if (!H.current || !ae) {
        w.current = requestAnimationFrame(F);
        return;
      }
      const { x: re, y: Q } = j.current;
      B(re, Q), w.current = requestAnimationFrame(F);
    });
  }
  const Z = () => {
    cancelAnimationFrame(w.current), w.current = 0, A.current = !1;
  };
  oe(() => () => Z(), []);
  const Y = (L) => {
    const { userSelectionRect: U, transform: ae, resetSelectedElements: re } = m.getState();
    if (!X.current || !U)
      return;
    const { x: Q, y: ee } = Je(L.nativeEvent, X.current);
    j.current = { x: Q, y: ee };
    const le = dn({ x: U.startX, y: U.startY }, ae);
    if (!H.current) {
      const O = t ? 0 : i;
      if (Math.hypot(Q - le.x, ee - le.y) <= O)
        return;
      re(), c?.(L);
    }
    H.current = !0, A.current || (F(), A.current = !0), B(Q, ee);
  }, te = (L) => {
    L.button === 0 && (L.target?.releasePointerCapture?.(L.pointerId), !E && L.target === P.current && m.getState().userSelectionRect && I?.(L), m.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), H.current && (u?.(L), m.setState({
      nodesSelectionActive: M.current.size > 0
    })), Z());
  }, ce = (L) => {
    L.target?.releasePointerCapture?.(L.pointerId), Z();
  }, J = o === !0 || Array.isArray(o) && o.includes(0);
  return s.jsxs("div", { className: Ce(["react-flow__pane", { draggable: J, dragging: v, selection: e }]), onClick: _ ? void 0 : ti(I, P), onContextMenu: ti(D, P), onWheel: ti(T, P), onPointerEnter: _ ? void 0 : h, onPointerMove: _ ? Y : p, onPointerUp: _ ? te : void 0, onPointerCancel: _ ? ce : void 0, onPointerDownCapture: _ ? V : void 0, onClickCapture: _ ? R : void 0, onPointerLeave: g, ref: P, style: xr, children: [b, s.jsx(Oy, {})] });
}
function vi({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: i, multiSelectionActive: a, nodeLookup: c, onError: u } = t.getState(), l = c.get(e);
  if (!l) {
    u?.("012", Xe.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && a) && (i({ nodes: [l], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function kl({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: i, nodeClickDistance: a }) {
  const c = Ne(), [u, l] = K(!1), d = se();
  return oe(() => {
    d.current = kg({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (f) => {
        vi({
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
    if (!(t || !e.current || !d.current))
      return d.current.update({
        noDragClassName: n,
        handleSelector: o,
        domNode: e.current,
        isSelectable: i,
        nodeId: r,
        nodeClickDistance: a
      }), () => {
        d.current?.destroy();
      };
  }, [n, o, t, i, e, r, a]), u;
}
const Wy = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function Cl() {
  const e = Ne();
  return de((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: i, nodesDraggable: a, onError: c, updateNodePositions: u, nodeLookup: l, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), h = Wy(a), p = r ? i[0] : 5, g = r ? i[1] : 5, b = n.direction.x * p * n.factor, w = n.direction.y * g * n.factor;
    for (const [, m] of l) {
      if (!h(m))
        continue;
      let E = {
        x: m.internals.positionAbsolute.x + b,
        y: m.internals.positionAbsolute.y + w
      };
      r && (E = so(E, i));
      const { position: y, positionAbsolute: v } = Wc({
        nodeId: m.id,
        nextPosition: E,
        nodeLookup: l,
        nodeExtent: o,
        nodeOrigin: d,
        onError: c
      });
      m.position = y, m.internals.positionAbsolute = v, f.set(m.id, m);
    }
    u(f);
  }, []);
}
const Ui = Ai(null), By = Ui.Provider;
Ui.Consumer;
const Il = () => to(Ui), Xy = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), Ky = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: i, connection: a } = o, { fromHandle: c, toHandle: u, isValid: l } = a, d = u?.nodeId === e && u?.id === t && u?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: d,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: i === cn.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!r,
    valid: d && l
  };
};
function qy({ type: e = "source", position: t = ne.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: i = !0, id: a, onConnect: c, children: u, className: l, onMouseDown: d, onTouchStart: f, ...h }, p) {
  const g = a || null, b = e === "target", w = Ne(), m = Il(), { connectOnClick: E, noPanClassName: y, rfId: v } = fe(Xy, be), { connectingFrom: N, connectingTo: k, clickConnecting: S, isPossibleEndHandle: _, connectionInProcess: P, clickConnectionInProcess: X, valid: M } = fe(Ky(m, g, e), be);
  m || w.getState().onError?.("010", Xe.error010());
  const $ = (A) => {
    const { defaultEdgeOptions: I, onConnect: D, hasDefaultEdges: T } = w.getState(), R = {
      ...I,
      ...A
    };
    if (T) {
      const { edges: V, setEdges: B, onError: F } = w.getState();
      B(Sl(R, V, { onError: F }));
    }
    D?.(R), c?.(R);
  }, H = (A) => {
    if (!m)
      return;
    const I = Gc(A.nativeEvent);
    if (r && (I && A.button === 0 || !I)) {
      const D = w.getState();
      wi.onPointerDown(A.nativeEvent, {
        handleDomNode: A.currentTarget,
        autoPanOnConnect: D.autoPanOnConnect,
        connectionMode: D.connectionMode,
        connectionRadius: D.connectionRadius,
        domNode: D.domNode,
        nodeLookup: D.nodeLookup,
        lib: D.lib,
        isTarget: b,
        handleId: g,
        nodeId: m,
        flowId: D.rfId,
        panBy: D.panBy,
        cancelConnection: D.cancelConnection,
        onConnectStart: D.onConnectStart,
        onConnectEnd: (...T) => w.getState().onConnectEnd?.(...T),
        updateConnection: D.updateConnection,
        onConnect: $,
        isValidConnection: n || ((...T) => w.getState().isValidConnection?.(...T) ?? !0),
        getTransform: () => w.getState().transform,
        getFromHandle: () => w.getState().connection.fromHandle,
        autoPanSpeed: D.autoPanSpeed,
        dragThreshold: D.connectionDragThreshold
      });
    }
    I ? d?.(A) : f?.(A);
  }, j = (A) => {
    const { onClickConnectStart: I, onClickConnectEnd: D, connectionClickStartHandle: T, connectionMode: R, isValidConnection: V, lib: B, rfId: F, nodeLookup: Z, connection: Y } = w.getState();
    if (!m || !T && !r)
      return;
    if (!T) {
      I?.(A.nativeEvent, { nodeId: m, handleId: g, handleType: e }), w.setState({ connectionClickStartHandle: { nodeId: m, type: e, id: g } });
      return;
    }
    const te = Uc(A.target), ce = n || V, { connection: J, isValid: L } = wi.isValid(A.nativeEvent, {
      handle: {
        nodeId: m,
        id: g,
        type: e
      },
      connectionMode: R,
      fromNodeId: T.nodeId,
      fromHandleId: T.id || null,
      fromType: T.type,
      isValidConnection: ce,
      flowId: F,
      doc: te,
      lib: B,
      nodeLookup: Z
    });
    L && J && $(J);
    const U = structuredClone(Y);
    delete U.inProgress, U.toPosition = U.toHandle ? U.toHandle.position : null, D?.(A, U), w.setState({ connectionClickStartHandle: null });
  };
  return s.jsx("div", { "data-handleid": g, "data-nodeid": m, "data-handlepos": t, "data-id": `${v}-${m}-${g}-${e}`, className: Ce([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    y,
    l,
    {
      source: !b,
      target: b,
      connectable: o,
      connectablestart: r,
      connectableend: i,
      clickconnecting: S,
      connectingfrom: N,
      connectingto: k,
      valid: M,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!P || _) && (P || X ? i : r)
    }
  ]), onMouseDown: H, onTouchStart: H, onClick: E ? j : void 0, ref: p, ...h, children: u });
}
const hn = Ee(jl(qy));
function Yy({ data: e, isConnectable: t, sourcePosition: n = ne.Bottom }) {
  return s.jsxs(s.Fragment, { children: [e?.label, s.jsx(hn, { type: "source", position: n, isConnectable: t })] });
}
function Uy({ data: e, isConnectable: t, targetPosition: n = ne.Top, sourcePosition: o = ne.Bottom }) {
  return s.jsxs(s.Fragment, { children: [s.jsx(hn, { type: "target", position: n, isConnectable: t }), e?.label, s.jsx(hn, { type: "source", position: o, isConnectable: t })] });
}
function Zy() {
  return null;
}
function Gy({ data: e, isConnectable: t, targetPosition: n = ne.Top }) {
  return s.jsxs(s.Fragment, { children: [s.jsx(hn, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const er = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, ba = {
  input: Yy,
  default: Uy,
  output: Gy,
  group: Zy
};
function Jy(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const Qy = (e) => {
  const { width: t, height: n, x: o, y: r } = io(e.nodeLookup, {
    filter: (i) => !!i.selected
  });
  return {
    width: Ge(t) ? t : null,
    height: Ge(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function em({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = Ne(), { width: r, height: i, transformString: a, userSelectionActive: c } = fe(Qy, be), u = Cl(), l = se(null);
  oe(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !c && r !== null && i !== null;
  if (kl({
    nodeRef: l,
    disabled: !d
  }), !d)
    return null;
  const f = e ? (p) => {
    const g = o.getState().nodes.filter((b) => b.selected);
    e(p, g);
  } : void 0, h = (p) => {
    Object.prototype.hasOwnProperty.call(er, p.key) && (p.preventDefault(), u({
      direction: er[p.key],
      factor: p.shiftKey ? 4 : 1
    }));
  };
  return s.jsx("div", { className: Ce(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: a
  }, children: s.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : h, style: {
    width: r,
    height: i
  } }) });
}
const Na = typeof window < "u" ? window : void 0, tm = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function _l({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: a, paneClickDistance: c, deleteKeyCode: u, selectionKeyCode: l, selectionOnDrag: d, selectionMode: f, onSelectionStart: h, onSelectionEnd: p, multiSelectionKeyCode: g, panActivationKeyCode: b, zoomActivationKeyCode: w, elementsSelectable: m, zoomOnScroll: E, zoomOnPinch: y, panOnScroll: v, panOnScrollSpeed: N, panOnScrollMode: k, zoomOnDoubleClick: S, panOnDrag: _, autoPanOnSelection: P, defaultViewport: X, translateExtent: M, minZoom: $, maxZoom: H, preventScrolling: j, onSelectionContextMenu: A, noWheelClassName: I, noPanClassName: D, disableKeyboardA11y: T, onViewportChange: R, isControlledViewport: V }) {
  const { nodesSelectionActive: B, userSelectionActive: F } = fe(tm, be), Z = Jn(l, { target: Na }), Y = Jn(b, { target: Na }), te = Y || _, ce = Y || v, J = d && te !== !0, L = Z || F || J;
  return $y({ deleteKeyCode: u, multiSelectionKeyCode: g }), s.jsx(Ly, { onPaneContextMenu: i, elementsSelectable: m, zoomOnScroll: E, zoomOnPinch: y, panOnScroll: ce, panOnScrollSpeed: N, panOnScrollMode: k, zoomOnDoubleClick: S, panOnDrag: !Z && te, defaultViewport: X, translateExtent: M, minZoom: $, maxZoom: H, zoomActivationKeyCode: w, preventScrolling: j, noWheelClassName: I, noPanClassName: D, onViewportChange: R, isControlledViewport: V, paneClickDistance: c, selectionOnDrag: J, children: s.jsxs(Fy, { onSelectionStart: h, onSelectionEnd: p, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: a, panOnDrag: te, autoPanOnSelection: P, isSelecting: !!L, selectionMode: f, selectionKeyPressed: Z, paneClickDistance: c, selectionOnDrag: J, children: [e, B && s.jsx(em, { onSelectionContextMenu: A, noPanClassName: D, disableKeyboardA11y: T })] }) });
}
_l.displayName = "FlowRenderer";
const nm = Ee(_l), om = (e) => (t) => e ? Vi(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function rm(e) {
  return fe(de(om(e), [e]), be);
}
const im = (e) => e.updateNodeInternals;
function sm() {
  const e = fe(im), [t] = K(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
function am({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = Ne(), i = se(null), a = se(null), c = se(e.sourcePosition), u = se(e.targetPosition), l = se(t), d = n && !!e.internals.handleBounds;
  return oe(() => {
    i.current && !e.hidden && (!d || a.current !== i.current) && (a.current && o?.unobserve(a.current), o?.observe(i.current), a.current = i.current);
  }, [d, e.hidden]), oe(() => () => {
    a.current && (o?.unobserve(a.current), a.current = null);
  }, []), oe(() => {
    if (i.current) {
      const f = l.current !== t, h = c.current !== e.sourcePosition, p = u.current !== e.targetPosition;
      (f || h || p) && (l.current = t, c.current = e.sourcePosition, u.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: i.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), i;
}
function cm({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: i, onDoubleClick: a, nodesDraggable: c, elementsSelectable: u, nodesConnectable: l, nodesFocusable: d, resizeObserver: f, noDragClassName: h, noPanClassName: p, disableKeyboardA11y: g, rfId: b, nodeTypes: w, nodeClickDistance: m, onError: E }) {
  const { node: y, internals: v, isParent: N } = fe((L) => {
    const U = L.nodeLookup.get(e), ae = L.parentLookup.has(e);
    return {
      node: U,
      internals: U.internals,
      isParent: ae
    };
  }, be);
  let k = y.type || "default", S = w?.[k] || ba[k];
  S === void 0 && (E?.("003", Xe.error003(k)), k = "default", S = w?.default || ba.default);
  const _ = !!(y.draggable || c && typeof y.draggable > "u"), P = !!(y.selectable || u && typeof y.selectable > "u"), X = !!(y.connectable || l && typeof y.connectable > "u"), M = !!(y.focusable || d && typeof y.focusable > "u"), $ = Ne(), H = qc(y), j = am({ node: y, nodeType: k, hasDimensions: H, resizeObserver: f }), A = kl({
    nodeRef: j,
    disabled: y.hidden || !_,
    noDragClassName: h,
    handleSelector: y.dragHandle,
    nodeId: e,
    isSelectable: P,
    nodeClickDistance: m
  }), I = Cl();
  if (y.hidden)
    return null;
  const D = pt(y), T = Jy(y), R = P || _ || t || n || o || r, V = n ? (L) => n(L, { ...v.userNode }) : void 0, B = o ? (L) => o(L, { ...v.userNode }) : void 0, F = r ? (L) => r(L, { ...v.userNode }) : void 0, Z = i ? (L) => i(L, { ...v.userNode }) : void 0, Y = a ? (L) => a(L, { ...v.userNode }) : void 0, te = (L) => {
    const { selectNodesOnDrag: U, nodeDragThreshold: ae } = $.getState();
    P && (!U || !_ || ae > 0) && vi({
      id: e,
      store: $,
      nodeRef: j
    }), t && t(L, { ...v.userNode });
  }, ce = (L) => {
    if (!(Zc(L.nativeEvent) || g)) {
      if (Lc.includes(L.key) && P) {
        const U = L.key === "Escape";
        vi({
          id: e,
          store: $,
          unselect: U,
          nodeRef: j
        });
      } else if (_ && y.selected && Object.prototype.hasOwnProperty.call(er, L.key)) {
        L.preventDefault();
        const { ariaLabelConfig: U } = $.getState();
        $.setState({
          ariaLiveMessage: U["node.a11yDescription.ariaLiveMessage"]({
            direction: L.key.replace("Arrow", "").toLowerCase(),
            x: ~~v.positionAbsolute.x,
            y: ~~v.positionAbsolute.y
          })
        }), I({
          direction: er[L.key],
          factor: L.shiftKey ? 4 : 1
        });
      }
    }
  }, J = () => {
    if (g || !j.current?.matches(":focus-visible"))
      return;
    const { transform: L, width: U, height: ae, autoPanOnNodeFocus: re, setCenter: Q } = $.getState();
    if (!re)
      return;
    Vi(/* @__PURE__ */ new Map([[e, y]]), { x: 0, y: 0, width: U, height: ae }, L, !0).length > 0 || Q(y.position.x + D.width / 2, y.position.y + D.height / 2, {
      zoom: L[2]
    });
  };
  return s.jsx("div", { className: Ce([
    "react-flow__node",
    `react-flow__node-${k}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [p]: _
    },
    y.className,
    {
      selected: y.selected,
      selectable: P,
      parent: N,
      draggable: _,
      dragging: A
    }
  ]), ref: j, style: {
    zIndex: v.z,
    transform: `translate(${v.positionAbsolute.x}px,${v.positionAbsolute.y}px)`,
    pointerEvents: R ? "all" : "none",
    visibility: H ? "visible" : "hidden",
    ...y.style,
    ...T
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: V, onMouseMove: B, onMouseLeave: F, onContextMenu: Z, onClick: te, onDoubleClick: Y, onKeyDown: M ? ce : void 0, tabIndex: M ? 0 : void 0, onFocus: M ? J : void 0, role: y.ariaRole ?? (M ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": g ? void 0 : `${yl}-${b}`, "aria-label": y.ariaLabel, ...y.domAttributes, children: s.jsx(By, { value: e, children: s.jsx(S, { id: e, data: y.data, type: k, positionAbsoluteX: v.positionAbsolute.x, positionAbsoluteY: v.positionAbsolute.y, selected: y.selected ?? !1, selectable: P, draggable: _, deletable: y.deletable ?? !0, isConnectable: X, sourcePosition: y.sourcePosition, targetPosition: y.targetPosition, dragging: A, dragHandle: y.dragHandle, zIndex: v.z, parentId: y.parentId, ...D }) }) });
}
var lm = Ee(cm);
const um = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function Al(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: i } = fe(um, be), a = rm(e.onlyRenderVisibleElements), c = sm();
  return s.jsx("div", { className: "react-flow__nodes", style: xr, children: a.map((u) => (
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
    s.jsx(lm, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: i }, u)
  )) });
}
Al.displayName = "NodeRenderer";
const dm = Ee(Al);
function fm(e) {
  return fe(de((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const i = n.nodeLookup.get(r.source), a = n.nodeLookup.get(r.target);
        i && a && ag({
          sourceNode: i,
          targetNode: a,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && o.push(r.id);
      }
    return o;
  }, [e]), be);
}
const hm = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return s.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, pm = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return s.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Sa = {
  [Go.Arrow]: hm,
  [Go.ArrowClosed]: pm
};
function gm(e) {
  const t = Ne();
  return pe(() => Object.prototype.hasOwnProperty.call(Sa, e) ? Sa[e] : (t.getState().onError?.("009", Xe.error009(e)), null), [e]);
}
const ym = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: i = "strokeWidth", strokeWidth: a, orient: c = "auto-start-reverse" }) => {
  const u = gm(t);
  return u ? s.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: i, orient: c, refX: "0", refY: "0", children: s.jsx(u, { color: n, strokeWidth: a }) }) : null;
}, Ml = ({ defaultColor: e, rfId: t }) => {
  const n = fe((i) => i.edges), o = fe((i) => i.defaultEdgeOptions), r = pe(() => gg(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? s.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: s.jsx("defs", { children: r.map((i) => s.jsx(ym, { id: i.id, type: i.type, color: i.color, width: i.width, height: i.height, markerUnits: i.markerUnits, strokeWidth: i.strokeWidth, orient: i.orient }, i.id)) }) }) : null;
};
Ml.displayName = "MarkerDefinitions";
var mm = Ee(Ml);
function Dl({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: i, labelBgPadding: a = [2, 4], labelBgBorderRadius: c = 2, children: u, className: l, ...d }) {
  const [f, h] = K({ x: 1, y: 0, width: 0, height: 0 }), p = Ce(["react-flow__edge-textwrapper", l]), g = se(null);
  return oe(() => {
    if (g.current) {
      const b = g.current.getBBox();
      h({
        x: b.x,
        y: b.y,
        width: b.width,
        height: b.height
      });
    }
  }, [n]), n ? s.jsxs("g", { transform: `translate(${e - f.width / 2} ${t - f.height / 2})`, className: p, visibility: f.width ? "visible" : "hidden", ...d, children: [r && s.jsx("rect", { width: f.width + 2 * a[0], x: -a[0], y: -a[1], height: f.height + 2 * a[1], className: "react-flow__edge-textbg", style: i, rx: c, ry: c }), s.jsx("text", { className: "react-flow__edge-text", y: f.height / 2, dy: "0.3em", ref: g, style: o, children: n }), u] }) : null;
}
Dl.displayName = "EdgeText";
const xm = Ee(Dl);
function ao({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u, interactionWidth: l = 20, ...d }) {
  return s.jsxs(s.Fragment, { children: [s.jsx("path", { ...d, d: e, fill: "none", className: Ce(["react-flow__edge-path", d.className]) }), l ? s.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, o && Ge(t) && Ge(n) ? s.jsx(xm, { x: t, y: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u }) : null] });
}
function ja({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === ne.Left || e === ne.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function Pl({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: r, targetPosition: i = ne.Top }) {
  const [a, c] = ja({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [u, l] = ja({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [d, f, h, p] = Jc({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: r,
    sourceControlX: a,
    sourceControlY: c,
    targetControlX: u,
    targetControlY: l
  });
  return [
    `M${e},${t} C${a},${c} ${u},${l} ${o},${r}`,
    d,
    f,
    h,
    p
  ];
}
function Tl(e) {
  return Ee(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: a, targetPosition: c, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: g, markerEnd: b, markerStart: w, interactionWidth: m }) => {
    const [E, y, v] = Pl({
      sourceX: n,
      sourceY: o,
      sourcePosition: a,
      targetX: r,
      targetY: i,
      targetPosition: c
    }), N = e.isInternal ? void 0 : t;
    return s.jsx(ao, { id: N, path: E, labelX: y, labelY: v, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: g, markerEnd: b, markerStart: w, interactionWidth: m });
  });
}
const wm = Tl({ isInternal: !1 }), $l = Tl({ isInternal: !0 });
wm.displayName = "SimpleBezierEdge";
$l.displayName = "SimpleBezierEdgeInternal";
function Rl(e) {
  return Ee(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: h, sourcePosition: p = ne.Bottom, targetPosition: g = ne.Top, markerEnd: b, markerStart: w, pathOptions: m, interactionWidth: E }) => {
    const [y, v, N] = Qo({
      sourceX: n,
      sourceY: o,
      sourcePosition: p,
      targetX: r,
      targetY: i,
      targetPosition: g,
      borderRadius: m?.borderRadius,
      offset: m?.offset,
      stepPosition: m?.stepPosition
    }), k = e.isInternal ? void 0 : t;
    return s.jsx(ao, { id: k, path: y, labelX: v, labelY: N, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: b, markerStart: w, interactionWidth: E });
  });
}
const zl = Rl({ isInternal: !1 }), Ll = Rl({ isInternal: !0 });
zl.displayName = "SmoothStepEdge";
Ll.displayName = "SmoothStepEdgeInternal";
function Vl(e) {
  return Ee(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return s.jsx(zl, { ...n, id: o, pathOptions: pe(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const vm = Vl({ isInternal: !1 }), Ol = Vl({ isInternal: !0 });
vm.displayName = "StepEdge";
Ol.displayName = "StepEdgeInternal";
function Hl(e) {
  return Ee(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: p, markerStart: g, interactionWidth: b }) => {
    const [w, m, E] = nl({ sourceX: n, sourceY: o, targetX: r, targetY: i }), y = e.isInternal ? void 0 : t;
    return s.jsx(ao, { id: y, path: w, labelX: m, labelY: E, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: p, markerStart: g, interactionWidth: b });
  });
}
const bm = Hl({ isInternal: !1 }), Fl = Hl({ isInternal: !0 });
bm.displayName = "StraightEdge";
Fl.displayName = "StraightEdgeInternal";
function Wl(e) {
  return Ee(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: a = ne.Bottom, targetPosition: c = ne.Top, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: g, markerEnd: b, markerStart: w, pathOptions: m, interactionWidth: E }) => {
    const [y, v, N] = Qc({
      sourceX: n,
      sourceY: o,
      sourcePosition: a,
      targetX: r,
      targetY: i,
      targetPosition: c,
      curvature: m?.curvature
    }), k = e.isInternal ? void 0 : t;
    return s.jsx(ao, { id: k, path: y, labelX: v, labelY: N, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: g, markerEnd: b, markerStart: w, interactionWidth: E });
  });
}
const Nm = Wl({ isInternal: !1 }), Bl = Wl({ isInternal: !0 });
Nm.displayName = "BezierEdge";
Bl.displayName = "BezierEdgeInternal";
const Ea = {
  default: Bl,
  straight: Fl,
  step: Ol,
  smoothstep: Ll,
  simplebezier: $l
}, ka = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, Sm = (e, t, n) => n === ne.Left ? e - t : n === ne.Right ? e + t : e, jm = (e, t, n) => n === ne.Top ? e - t : n === ne.Bottom ? e + t : e, Ca = "react-flow__edgeupdater";
function Ia({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: i, onMouseOut: a, type: c }) {
  return s.jsx("circle", { onMouseDown: r, onMouseEnter: i, onMouseOut: a, className: Ce([Ca, `${Ca}-${c}`]), cx: Sm(t, o, e), cy: jm(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function Em({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: i, targetY: a, sourcePosition: c, targetPosition: u, onReconnect: l, onReconnectStart: d, onReconnectEnd: f, setReconnecting: h, setUpdateHover: p }) {
  const g = Ne(), b = (v, N) => {
    if (v.button !== 0)
      return;
    const { autoPanOnConnect: k, domNode: S, connectionMode: _, connectionRadius: P, lib: X, onConnectStart: M, cancelConnection: $, nodeLookup: H, rfId: j, panBy: A, updateConnection: I } = g.getState(), D = N.type === "target", T = (B, F) => {
      h(!1), f?.(B, n, N.type, F);
    }, R = (B) => l?.(n, B), V = (B, F) => {
      h(!0), d?.(v, n, N.type), M?.(B, F);
    };
    wi.onPointerDown(v.nativeEvent, {
      autoPanOnConnect: k,
      connectionMode: _,
      connectionRadius: P,
      domNode: S,
      handleId: N.id,
      nodeId: N.nodeId,
      nodeLookup: H,
      isTarget: D,
      edgeUpdaterType: N.type,
      lib: X,
      flowId: j,
      cancelConnection: $,
      panBy: A,
      isValidConnection: (...B) => g.getState().isValidConnection?.(...B) ?? !0,
      onConnect: R,
      onConnectStart: V,
      onConnectEnd: (...B) => g.getState().onConnectEnd?.(...B),
      onReconnectEnd: T,
      updateConnection: I,
      getTransform: () => g.getState().transform,
      getFromHandle: () => g.getState().connection.fromHandle,
      dragThreshold: g.getState().connectionDragThreshold,
      handleDomNode: v.currentTarget
    });
  }, w = (v) => b(v, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), m = (v) => b(v, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), E = () => p(!0), y = () => p(!1);
  return s.jsxs(s.Fragment, { children: [(e === !0 || e === "source") && s.jsx(Ia, { position: c, centerX: o, centerY: r, radius: t, onMouseDown: w, onMouseEnter: E, onMouseOut: y, type: "source" }), (e === !0 || e === "target") && s.jsx(Ia, { position: u, centerX: i, centerY: a, radius: t, onMouseDown: m, onMouseEnter: E, onMouseOut: y, type: "target" })] });
}
function km({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: i, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, reconnectRadius: d, onReconnect: f, onReconnectStart: h, onReconnectEnd: p, rfId: g, edgeTypes: b, noPanClassName: w, onError: m, disableKeyboardA11y: E }) {
  let y = fe((Q) => Q.edgeLookup.get(e));
  const v = fe((Q) => Q.defaultEdgeOptions);
  y = v ? { ...v, ...y } : y;
  let N = y.type || "default", k = b?.[N] || Ea[N];
  k === void 0 && (m?.("011", Xe.error011(N)), N = "default", k = b?.default || Ea.default);
  const S = !!(y.focusable || t && typeof y.focusable > "u"), _ = typeof f < "u" && (y.reconnectable || n && typeof y.reconnectable > "u"), P = !!(y.selectable || o && typeof y.selectable > "u"), X = se(null), [M, $] = K(!1), [H, j] = K(!1), A = Ne(), { zIndex: I, sourceX: D, sourceY: T, targetX: R, targetY: V, sourcePosition: B, targetPosition: F } = fe(de((Q) => {
    const ee = Q.nodeLookup.get(y.source), le = Q.nodeLookup.get(y.target);
    if (!ee || !le)
      return {
        zIndex: y.zIndex,
        ...ka
      };
    const O = pg({
      id: e,
      sourceNode: ee,
      targetNode: le,
      sourceHandle: y.sourceHandle || null,
      targetHandle: y.targetHandle || null,
      connectionMode: Q.connectionMode,
      onError: m
    });
    return {
      zIndex: sg({
        selected: y.selected,
        zIndex: y.zIndex,
        sourceNode: ee,
        targetNode: le,
        elevateOnSelect: Q.elevateEdgesOnSelect,
        zIndexMode: Q.zIndexMode
      }),
      ...O || ka
    };
  }, [y.source, y.target, y.sourceHandle, y.targetHandle, y.selected, y.zIndex]), be), Z = pe(() => y.markerStart ? `url('#${mi(y.markerStart, g)}')` : void 0, [y.markerStart, g]), Y = pe(() => y.markerEnd ? `url('#${mi(y.markerEnd, g)}')` : void 0, [y.markerEnd, g]);
  if (y.hidden || D === null || T === null || R === null || V === null)
    return null;
  const te = (Q) => {
    const { addSelectedEdges: ee, unselectNodesAndEdges: le, multiSelectionActive: O } = A.getState();
    P && (A.setState({ nodesSelectionActive: !1 }), y.selected && O ? (le({ nodes: [], edges: [y] }), X.current?.blur()) : ee([e])), r && r(Q, y);
  }, ce = i ? (Q) => {
    i(Q, { ...y });
  } : void 0, J = a ? (Q) => {
    a(Q, { ...y });
  } : void 0, L = c ? (Q) => {
    c(Q, { ...y });
  } : void 0, U = u ? (Q) => {
    u(Q, { ...y });
  } : void 0, ae = l ? (Q) => {
    l(Q, { ...y });
  } : void 0, re = (Q) => {
    if (!E && Lc.includes(Q.key) && P) {
      const { unselectNodesAndEdges: ee, addSelectedEdges: le } = A.getState();
      Q.key === "Escape" ? (X.current?.blur(), ee({ edges: [y] })) : le([e]);
    }
  };
  return s.jsx("svg", { style: { zIndex: I }, children: s.jsxs("g", { className: Ce([
    "react-flow__edge",
    `react-flow__edge-${N}`,
    y.className,
    w,
    {
      selected: y.selected,
      animated: y.animated,
      inactive: !P && !r,
      updating: M,
      selectable: P
    }
  ]), onClick: te, onDoubleClick: ce, onContextMenu: J, onMouseEnter: L, onMouseMove: U, onMouseLeave: ae, onKeyDown: S ? re : void 0, tabIndex: S ? 0 : void 0, role: y.ariaRole ?? (S ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": y.ariaLabel === null ? void 0 : y.ariaLabel || `Edge from ${y.source} to ${y.target}`, "aria-describedby": S ? `${ml}-${g}` : void 0, ref: X, ...y.domAttributes, children: [!H && s.jsx(k, { id: e, source: y.source, target: y.target, type: y.type, selected: y.selected, animated: y.animated, selectable: P, deletable: y.deletable ?? !0, label: y.label, labelStyle: y.labelStyle, labelShowBg: y.labelShowBg, labelBgStyle: y.labelBgStyle, labelBgPadding: y.labelBgPadding, labelBgBorderRadius: y.labelBgBorderRadius, sourceX: D, sourceY: T, targetX: R, targetY: V, sourcePosition: B, targetPosition: F, data: y.data, style: y.style, sourceHandleId: y.sourceHandle, targetHandleId: y.targetHandle, markerStart: Z, markerEnd: Y, pathOptions: "pathOptions" in y ? y.pathOptions : void 0, interactionWidth: y.interactionWidth }), _ && s.jsx(Em, { edge: y, isReconnectable: _, reconnectRadius: d, onReconnect: f, onReconnectStart: h, onReconnectEnd: p, sourceX: D, sourceY: T, targetX: R, targetY: V, sourcePosition: B, targetPosition: F, setUpdateHover: $, setReconnecting: j })] }) });
}
var Cm = Ee(km);
const Im = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function Xl({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: i, onEdgeContextMenu: a, onEdgeMouseEnter: c, onEdgeMouseMove: u, onEdgeMouseLeave: l, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: h, onReconnectStart: p, onReconnectEnd: g, disableKeyboardA11y: b }) {
  const { edgesFocusable: w, edgesReconnectable: m, elementsSelectable: E, onError: y } = fe(Im, be), v = fm(t);
  return s.jsxs("div", { className: "react-flow__edges", children: [s.jsx(mm, { defaultColor: e, rfId: n }), v.map((N) => s.jsx(Cm, { id: N, edgesFocusable: w, edgesReconnectable: m, elementsSelectable: E, noPanClassName: r, onReconnect: i, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, onClick: d, reconnectRadius: f, onDoubleClick: h, onReconnectStart: p, onReconnectEnd: g, rfId: n, onError: y, edgeTypes: o, disableKeyboardA11y: b }, N))] });
}
Xl.displayName = "EdgeRenderer";
const _m = Ee(Xl), Am = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function Mm({ children: e }) {
  const t = fe(Am);
  return s.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function Dm(e) {
  const t = Yi(), n = se(!1);
  oe(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Pm = (e) => e.panZoom?.syncViewport;
function Tm(e) {
  const t = fe(Pm), n = Ne();
  return oe(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function $m(e) {
  return e.connection.inProgress ? { ...e.connection, to: gn(e.connection.to, e.transform) } : { ...e.connection };
}
function Rm(e) {
  return $m;
}
function zm(e) {
  const t = Rm();
  return fe(t, be);
}
const Lm = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function Vm({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: i, height: a, isValid: c, inProgress: u } = fe(Lm, be);
  return !(i && r && u) ? null : s.jsx("svg", { style: e, width: i, height: a, className: "react-flow__connectionline react-flow__container", children: s.jsx("g", { className: Ce(["react-flow__connection", Hc(c)]), children: s.jsx(Kl, { style: t, type: n, CustomComponent: o, isValid: c }) }) });
}
const Kl = ({ style: e, type: t = vt.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: i, fromNode: a, fromHandle: c, fromPosition: u, to: l, toNode: d, toHandle: f, toPosition: h, pointer: p } = zm();
  if (!r)
    return;
  if (n)
    return s.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: a, fromHandle: c, fromX: i.x, fromY: i.y, toX: l.x, toY: l.y, fromPosition: u, toPosition: h, connectionStatus: Hc(o), toNode: d, toHandle: f, pointer: p });
  let g = "";
  const b = {
    sourceX: i.x,
    sourceY: i.y,
    sourcePosition: u,
    targetX: l.x,
    targetY: l.y,
    targetPosition: h
  };
  switch (t) {
    case vt.Bezier:
      [g] = Qc(b);
      break;
    case vt.SimpleBezier:
      [g] = Pl(b);
      break;
    case vt.Step:
      [g] = Qo({
        ...b,
        borderRadius: 0
      });
      break;
    case vt.SmoothStep:
      [g] = Qo(b);
      break;
    default:
      [g] = nl(b);
  }
  return s.jsx("path", { d: g, fill: "none", className: "react-flow__connection-path", style: e });
};
Kl.displayName = "ConnectionLine";
const Om = {};
function _a(e = Om) {
  se(e), Ne(), oe(() => {
  }, [e]);
}
function Hm() {
  Ne(), se(!1), oe(() => {
  }, []);
}
function ql({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: i, onEdgeDoubleClick: a, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: h, onSelectionEnd: p, connectionLineType: g, connectionLineStyle: b, connectionLineComponent: w, connectionLineContainerStyle: m, selectionKeyCode: E, selectionOnDrag: y, selectionMode: v, multiSelectionKeyCode: N, panActivationKeyCode: k, zoomActivationKeyCode: S, deleteKeyCode: _, onlyRenderVisibleElements: P, elementsSelectable: X, defaultViewport: M, translateExtent: $, minZoom: H, maxZoom: j, preventScrolling: A, defaultMarkerColor: I, zoomOnScroll: D, zoomOnPinch: T, panOnScroll: R, panOnScrollSpeed: V, panOnScrollMode: B, zoomOnDoubleClick: F, panOnDrag: Z, autoPanOnSelection: Y, onPaneClick: te, onPaneMouseEnter: ce, onPaneMouseMove: J, onPaneMouseLeave: L, onPaneScroll: U, onPaneContextMenu: ae, paneClickDistance: re, nodeClickDistance: Q, onEdgeContextMenu: ee, onEdgeMouseEnter: le, onEdgeMouseMove: O, onEdgeMouseLeave: G, reconnectRadius: he, onReconnect: ve, onReconnectStart: ke, onReconnectEnd: Ie, noDragClassName: me, noWheelClassName: Ye, noPanClassName: st, disableKeyboardA11y: at, nodeExtent: ze, rfId: Le, viewport: De, onViewportChange: ct }) {
  return _a(e), _a(t), Hm(), Dm(n), Tm(De), s.jsx(nm, { onPaneClick: te, onPaneMouseEnter: ce, onPaneMouseMove: J, onPaneMouseLeave: L, onPaneContextMenu: ae, onPaneScroll: U, paneClickDistance: re, deleteKeyCode: _, selectionKeyCode: E, selectionOnDrag: y, selectionMode: v, onSelectionStart: h, onSelectionEnd: p, multiSelectionKeyCode: N, panActivationKeyCode: k, zoomActivationKeyCode: S, elementsSelectable: X, zoomOnScroll: D, zoomOnPinch: T, zoomOnDoubleClick: F, panOnScroll: R, panOnScrollSpeed: V, panOnScrollMode: B, panOnDrag: Z, autoPanOnSelection: Y, defaultViewport: M, translateExtent: $, minZoom: H, maxZoom: j, onSelectionContextMenu: f, preventScrolling: A, noDragClassName: me, noWheelClassName: Ye, noPanClassName: st, disableKeyboardA11y: at, onViewportChange: ct, isControlledViewport: !!De, children: s.jsxs(Mm, { children: [s.jsx(_m, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: a, onReconnect: ve, onReconnectStart: ke, onReconnectEnd: Ie, onlyRenderVisibleElements: P, onEdgeContextMenu: ee, onEdgeMouseEnter: le, onEdgeMouseMove: O, onEdgeMouseLeave: G, reconnectRadius: he, defaultMarkerColor: I, noPanClassName: st, disableKeyboardA11y: at, rfId: Le }), s.jsx(Vm, { style: b, type: g, component: w, containerStyle: m }), s.jsx("div", { className: "react-flow__edgelabel-renderer" }), s.jsx(dm, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: i, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, nodeClickDistance: Q, onlyRenderVisibleElements: P, noPanClassName: st, noDragClassName: me, disableKeyboardA11y: at, nodeExtent: ze, rfId: Le }), s.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
ql.displayName = "GraphView";
const Fm = Ee(ql), Wm = Kc(), Aa = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: a, fitViewOptions: c, minZoom: u = 0.5, maxZoom: l = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: h = "basic" } = {}) => {
  const p = /* @__PURE__ */ new Map(), g = /* @__PURE__ */ new Map(), b = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), m = o ?? t ?? [], E = n ?? e ?? [], y = d ?? [0, 0], v = f ?? Yn;
  il(b, w, m);
  const { nodesInitialized: N } = xi(E, p, g, {
    nodeOrigin: y,
    nodeExtent: v,
    zIndexMode: h
  });
  let k = [0, 0, 1];
  if (a && r && i) {
    const S = io(p, {
      filter: (M) => !!((M.width || M.initialWidth) && (M.height || M.initialHeight))
    }), { x: _, y: P, zoom: X } = Hi(S, r, i, u, l, c?.padding ?? 0.1);
    k = [_, P, X];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: i ?? 0,
    transform: k,
    nodes: E,
    nodesInitialized: N,
    nodeLookup: p,
    parentLookup: g,
    edges: m,
    edgeLookup: w,
    connectionLookup: b,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: o !== void 0,
    panZoom: null,
    minZoom: u,
    maxZoom: l,
    translateExtent: Yn,
    nodeExtent: v,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: cn.Strict,
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
    connection: { ...Oc },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: Wm,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Vc,
    zIndexMode: h,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, Bm = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: a, fitViewOptions: c, minZoom: u, maxZoom: l, nodeOrigin: d, nodeExtent: f, zIndexMode: h }) => oy((p, g) => {
  async function b() {
    const { nodeLookup: w, panZoom: m, fitViewOptions: E, fitViewResolver: y, width: v, height: N, minZoom: k, maxZoom: S } = g();
    m && (await Qp({
      nodes: w,
      width: v,
      height: N,
      panZoom: m,
      minZoom: k,
      maxZoom: S
    }, E), y?.resolve(!0), p({ fitViewResolver: null }));
  }
  return {
    ...Aa({
      nodes: e,
      edges: t,
      width: r,
      height: i,
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
      const { nodeLookup: m, parentLookup: E, nodeOrigin: y, elevateNodesOnSelect: v, fitViewQueued: N, zIndexMode: k, nodesSelectionActive: S } = g(), { nodesInitialized: _, hasSelectedNodes: P } = xi(w, m, E, {
        nodeOrigin: y,
        nodeExtent: f,
        elevateNodesOnSelect: v,
        checkEquality: !0,
        zIndexMode: k
      }), X = S && P;
      N && _ ? (b(), p({
        nodes: w,
        nodesInitialized: _,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: X
      })) : p({ nodes: w, nodesInitialized: _, nodesSelectionActive: X });
    },
    setEdges: (w) => {
      const { connectionLookup: m, edgeLookup: E } = g();
      il(m, E, w), p({ edges: w });
    },
    setDefaultNodesAndEdges: (w, m) => {
      if (w) {
        const { setNodes: E } = g();
        E(w), p({ hasDefaultNodes: !0 });
      }
      if (m) {
        const { setEdges: E } = g();
        E(m), p({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (w) => {
      const { triggerNodeChanges: m, nodeLookup: E, parentLookup: y, domNode: v, nodeOrigin: N, nodeExtent: k, debug: S, fitViewQueued: _, zIndexMode: P } = g(), { changes: X, updatedInternals: M } = Ng(w, E, y, v, N, k, P);
      M && (xg(E, y, { nodeOrigin: N, nodeExtent: k, zIndexMode: P }), _ ? (b(), p({ fitViewQueued: !1, fitViewOptions: void 0 })) : p({}), X?.length > 0 && (S && console.log("React Flow: trigger node changes", X), m?.(X)));
    },
    updateNodePositions: (w, m = !1) => {
      const E = [];
      let y = [];
      const { nodeLookup: v, triggerNodeChanges: N, connection: k, updateConnection: S, onNodesChangeMiddlewareMap: _ } = g();
      for (const [P, X] of w) {
        const M = v.get(P), $ = !!(M?.expandParent && M?.parentId && X?.position), H = {
          id: P,
          type: "position",
          position: $ ? {
            x: Math.max(0, X.position.x),
            y: Math.max(0, X.position.y)
          } : X.position,
          dragging: m
        };
        if (M && k.inProgress && k.fromNode.id === M.id) {
          const j = Ht(M, k.fromHandle, ne.Left, !0);
          S({ ...k, from: j });
        }
        $ && M.parentId && E.push({
          id: P,
          parentId: M.parentId,
          rect: {
            ...X.internals.positionAbsolute,
            width: X.measured.width ?? 0,
            height: X.measured.height ?? 0
          }
        }), y.push(H);
      }
      if (E.length > 0) {
        const { parentLookup: P, nodeOrigin: X } = g(), M = qi(E, v, P, X);
        y.push(...M);
      }
      for (const P of _.values())
        y = P(y);
      N(y);
    },
    triggerNodeChanges: (w) => {
      const { onNodesChange: m, setNodes: E, nodes: y, hasDefaultNodes: v, debug: N } = g();
      if (w?.length) {
        if (v) {
          const k = vl(w, y);
          E(k);
        }
        N && console.log("React Flow: trigger node changes", w), m?.(w);
      }
    },
    triggerEdgeChanges: (w) => {
      const { onEdgesChange: m, setEdges: E, edges: y, hasDefaultEdges: v, debug: N } = g();
      if (w?.length) {
        if (v) {
          const k = bl(w, y);
          E(k);
        }
        N && console.log("React Flow: trigger edge changes", w), m?.(w);
      }
    },
    addSelectedNodes: (w) => {
      const { multiSelectionActive: m, edgeLookup: E, nodeLookup: y, triggerNodeChanges: v, triggerEdgeChanges: N } = g();
      if (m) {
        const k = w.map((S) => Dt(S, !0));
        v(k);
        return;
      }
      v(tn(y, /* @__PURE__ */ new Set([...w]), !0)), N(tn(E));
    },
    addSelectedEdges: (w) => {
      const { multiSelectionActive: m, edgeLookup: E, nodeLookup: y, triggerNodeChanges: v, triggerEdgeChanges: N } = g();
      if (m) {
        const k = w.map((S) => Dt(S, !0));
        N(k);
        return;
      }
      N(tn(E, /* @__PURE__ */ new Set([...w]))), v(tn(y, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: w, edges: m } = {}) => {
      const { edges: E, nodes: y, nodeLookup: v, triggerNodeChanges: N, triggerEdgeChanges: k } = g(), S = w || y, _ = m || E, P = [];
      for (const M of S) {
        if (!M.selected)
          continue;
        const $ = v.get(M.id);
        $ && ($.selected = !1), P.push(Dt(M.id, !1));
      }
      const X = [];
      for (const M of _)
        M.selected && X.push(Dt(M.id, !1));
      N(P), k(X);
    },
    setMinZoom: (w) => {
      const { panZoom: m, maxZoom: E } = g();
      m?.setScaleExtent([w, E]), p({ minZoom: w });
    },
    setMaxZoom: (w) => {
      const { panZoom: m, minZoom: E } = g();
      m?.setScaleExtent([E, w]), p({ maxZoom: w });
    },
    setTranslateExtent: (w) => {
      g().panZoom?.setTranslateExtent(w), p({ translateExtent: w });
    },
    resetSelectedElements: () => {
      const { edges: w, nodes: m, triggerNodeChanges: E, triggerEdgeChanges: y, elementsSelectable: v } = g();
      if (!v)
        return;
      const N = m.reduce((S, _) => _.selected ? [...S, Dt(_.id, !1)] : S, []), k = w.reduce((S, _) => _.selected ? [...S, Dt(_.id, !1)] : S, []);
      E(N), y(k);
    },
    setNodeExtent: (w) => {
      const { nodes: m, nodeLookup: E, parentLookup: y, nodeOrigin: v, elevateNodesOnSelect: N, nodeExtent: k, zIndexMode: S } = g();
      w[0][0] === k[0][0] && w[0][1] === k[0][1] && w[1][0] === k[1][0] && w[1][1] === k[1][1] || (xi(m, E, y, {
        nodeOrigin: v,
        nodeExtent: w,
        elevateNodesOnSelect: N,
        checkEquality: !1,
        zIndexMode: S
      }), p({ nodeExtent: w }));
    },
    panBy: (w) => {
      const { transform: m, width: E, height: y, panZoom: v, translateExtent: N } = g();
      return Sg({ delta: w, panZoom: v, transform: m, translateExtent: N, width: E, height: y });
    },
    setCenter: async (w, m, E) => {
      const { width: y, height: v, maxZoom: N, panZoom: k } = g();
      if (!k)
        return !1;
      const S = typeof E?.zoom < "u" ? E.zoom : N;
      return await k.setViewport({
        x: y / 2 - w * S,
        y: v / 2 - m * S,
        zoom: S
      }, { duration: E?.duration, ease: E?.ease, interpolate: E?.interpolate }), !0;
    },
    cancelConnection: () => {
      p({
        connection: { ...Oc }
      });
    },
    updateConnection: (w) => {
      p({ connection: w });
    },
    reset: () => p({ ...Aa() })
  };
}, Object.is);
function Xm({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: i, initialMinZoom: a, initialMaxZoom: c, initialFitViewOptions: u, fitView: l, nodeOrigin: d, nodeExtent: f, zIndexMode: h, children: p }) {
  const [g] = K(() => Bm({
    nodes: e,
    edges: t,
    defaultNodes: n,
    defaultEdges: o,
    width: r,
    height: i,
    fitView: l,
    minZoom: a,
    maxZoom: c,
    fitViewOptions: u,
    nodeOrigin: d,
    nodeExtent: f,
    zIndexMode: h
  }));
  return s.jsx(ay, { value: g, children: s.jsx(My, { children: p }) });
}
function Km({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: i, height: a, fitView: c, fitViewOptions: u, minZoom: l, maxZoom: d, nodeOrigin: f, nodeExtent: h, zIndexMode: p }) {
  return to(yr) ? s.jsx(s.Fragment, { children: e }) : s.jsx(Xm, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: i, initialHeight: a, fitView: c, initialFitViewOptions: u, initialMinZoom: l, initialMaxZoom: d, nodeOrigin: f, nodeExtent: h, zIndexMode: p, children: e });
}
const qm = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function Ym({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: i, edgeTypes: a, onNodeClick: c, onEdgeClick: u, onInit: l, onMove: d, onMoveStart: f, onMoveEnd: h, onConnect: p, onConnectStart: g, onConnectEnd: b, onClickConnectStart: w, onClickConnectEnd: m, onNodeMouseEnter: E, onNodeMouseMove: y, onNodeMouseLeave: v, onNodeContextMenu: N, onNodeDoubleClick: k, onNodeDragStart: S, onNodeDrag: _, onNodeDragStop: P, onNodesDelete: X, onEdgesDelete: M, onDelete: $, onSelectionChange: H, onSelectionDragStart: j, onSelectionDrag: A, onSelectionDragStop: I, onSelectionContextMenu: D, onSelectionStart: T, onSelectionEnd: R, onBeforeDelete: V, connectionMode: B, connectionLineType: F = vt.Bezier, connectionLineStyle: Z, connectionLineComponent: Y, connectionLineContainerStyle: te, deleteKeyCode: ce = "Backspace", selectionKeyCode: J = "Shift", selectionOnDrag: L = !1, selectionMode: U = Un.Full, panActivationKeyCode: ae = "Space", multiSelectionKeyCode: re = Gn() ? "Meta" : "Control", zoomActivationKeyCode: Q = Gn() ? "Meta" : "Control", snapToGrid: ee, snapGrid: le, onlyRenderVisibleElements: O = !1, selectNodesOnDrag: G, nodesDraggable: he, autoPanOnNodeFocus: ve, nodesConnectable: ke, nodesFocusable: Ie, nodeOrigin: me = xl, edgesFocusable: Ye, edgesReconnectable: st, elementsSelectable: at = !0, defaultViewport: ze = vy, minZoom: Le = 0.5, maxZoom: De = 2, translateExtent: ct = Yn, preventScrolling: Ke = !0, nodeExtent: gt, defaultMarkerColor: mn = "#b1b1b7", zoomOnScroll: xn = !0, zoomOnPinch: uo = !0, panOnScroll: wn = !1, panOnScrollSpeed: Ve = 0.5, panOnScrollMode: lt = $t.Free, zoomOnDoubleClick: ut = !0, panOnDrag: Bt = !0, onPaneClick: jr, onPaneMouseEnter: vn, onPaneMouseMove: Xt, onPaneMouseLeave: yt, onPaneScroll: Et, onPaneContextMenu: kt, paneClickDistance: nt = 1, nodeClickDistance: fo = 0, children: Oe, onReconnect: ho, onReconnectStart: we, onReconnectEnd: He, onEdgeContextMenu: Kt, onEdgeDoubleClick: po, onEdgeMouseEnter: Se, onEdgeMouseMove: Ct, onEdgeMouseLeave: bn, reconnectRadius: _e = 10, onNodesChange: It, onEdgesChange: mt, noDragClassName: Er = "nodrag", noWheelClassName: go = "nowheel", noPanClassName: Nn = "nopan", fitView: qt, fitViewOptions: yo, connectOnClick: Sn, attributionPosition: Yt, proOptions: Pe, defaultEdgeOptions: jn, elevateNodesOnSelect: En = !0, elevateEdgesOnSelect: kn = !1, disableKeyboardA11y: _t = !1, autoPanOnConnect: kr, autoPanOnNodeDrag: Cr, autoPanOnSelection: Ir = !0, autoPanSpeed: _r, connectionRadius: mo, isValidConnection: xo, onError: wo, style: Ar, id: Ut, nodeDragThreshold: Mr, connectionDragThreshold: Dr, viewport: Zt, onViewportChange: Gt, width: Cn, height: In, colorMode: Pr = "light", debug: Tr, onScroll: vo, ariaLabelConfig: $r, zIndexMode: bo = "basic", ...Rr }, zr) {
  const At = Ut || "1", Lr = jy(Pr), Vr = de((No) => {
    No.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), vo?.(No);
  }, [vo]);
  return s.jsx("div", { "data-testid": "rf__wrapper", ...Rr, onScroll: Vr, style: { ...Ar, ...qm }, ref: zr, className: Ce(["react-flow", r, Lr]), id: Ut, role: "application", children: s.jsxs(Km, { nodes: e, edges: t, width: Cn, height: In, fitView: qt, fitViewOptions: yo, minZoom: Le, maxZoom: De, nodeOrigin: me, nodeExtent: gt, zIndexMode: bo, children: [s.jsx(Sy, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: p, onConnectStart: g, onConnectEnd: b, onClickConnectStart: w, onClickConnectEnd: m, nodesDraggable: he, autoPanOnNodeFocus: ve, nodesConnectable: ke, nodesFocusable: Ie, edgesFocusable: Ye, edgesReconnectable: st, elementsSelectable: at, elevateNodesOnSelect: En, elevateEdgesOnSelect: kn, minZoom: Le, maxZoom: De, nodeExtent: gt, onNodesChange: It, onEdgesChange: mt, snapToGrid: ee, snapGrid: le, connectionMode: B, translateExtent: ct, connectOnClick: Sn, defaultEdgeOptions: jn, fitView: qt, fitViewOptions: yo, onNodesDelete: X, onEdgesDelete: M, onDelete: $, onNodeDragStart: S, onNodeDrag: _, onNodeDragStop: P, onSelectionDrag: A, onSelectionDragStart: j, onSelectionDragStop: I, onMove: d, onMoveStart: f, onMoveEnd: h, noPanClassName: Nn, nodeOrigin: me, rfId: At, autoPanOnConnect: kr, autoPanOnNodeDrag: Cr, autoPanSpeed: _r, onError: wo, connectionRadius: mo, isValidConnection: xo, selectNodesOnDrag: G, nodeDragThreshold: Mr, connectionDragThreshold: Dr, onBeforeDelete: V, debug: Tr, ariaLabelConfig: $r, zIndexMode: bo }), s.jsx(Fm, { onInit: l, onNodeClick: c, onEdgeClick: u, onNodeMouseEnter: E, onNodeMouseMove: y, onNodeMouseLeave: v, onNodeContextMenu: N, onNodeDoubleClick: k, nodeTypes: i, edgeTypes: a, connectionLineType: F, connectionLineStyle: Z, connectionLineComponent: Y, connectionLineContainerStyle: te, selectionKeyCode: J, selectionOnDrag: L, selectionMode: U, deleteKeyCode: ce, multiSelectionKeyCode: re, panActivationKeyCode: ae, zoomActivationKeyCode: Q, onlyRenderVisibleElements: O, defaultViewport: ze, translateExtent: ct, minZoom: Le, maxZoom: De, preventScrolling: Ke, zoomOnScroll: xn, zoomOnPinch: uo, zoomOnDoubleClick: ut, panOnScroll: wn, panOnScrollSpeed: Ve, panOnScrollMode: lt, panOnDrag: Bt, autoPanOnSelection: Ir, onPaneClick: jr, onPaneMouseEnter: vn, onPaneMouseMove: Xt, onPaneMouseLeave: yt, onPaneScroll: Et, onPaneContextMenu: kt, paneClickDistance: nt, nodeClickDistance: fo, onSelectionContextMenu: D, onSelectionStart: T, onSelectionEnd: R, onReconnect: ho, onReconnectStart: we, onReconnectEnd: He, onEdgeContextMenu: Kt, onEdgeDoubleClick: po, onEdgeMouseEnter: Se, onEdgeMouseMove: Ct, onEdgeMouseLeave: bn, reconnectRadius: _e, defaultMarkerColor: mn, noDragClassName: Er, noWheelClassName: go, noPanClassName: Nn, rfId: At, disableKeyboardA11y: _t, nodeExtent: gt, viewport: Zt, onViewportChange: Gt }), s.jsx(wy, { onSelectionChange: H }), Oe, s.jsx(py, { proOptions: Pe, position: Yt }), s.jsx(hy, { rfId: At, disableKeyboardA11y: _t })] }) });
}
var Yl = jl(Ym);
const Um = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function Zm({ children: e }) {
  const t = fe(Um);
  return t ? sy.createPortal(e, t) : null;
}
function Gm({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return s.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ce(["react-flow__background-pattern", n, o]) });
}
function Jm({ radius: e, className: t }) {
  return s.jsx("circle", { cx: e, cy: e, r: e, className: Ce(["react-flow__background-pattern", "dots", t]) });
}
var bt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(bt || (bt = {}));
const Qm = {
  [bt.Dots]: 1,
  [bt.Lines]: 1,
  [bt.Cross]: 6
}, ex = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function Ul({
  id: e,
  variant: t = bt.Dots,
  // only used for dots and cross
  gap: n = 20,
  // only used for lines and cross
  size: o,
  lineWidth: r = 1,
  offset: i = 0,
  color: a,
  bgColor: c,
  style: u,
  className: l,
  patternClassName: d
}) {
  const f = se(null), { transform: h, patternId: p } = fe(ex, be), g = o || Qm[t], b = t === bt.Dots, w = t === bt.Cross, m = Array.isArray(n) ? n : [n, n], E = [m[0] * h[2] || 1, m[1] * h[2] || 1], y = g * h[2], v = Array.isArray(i) ? i : [i, i], N = w ? [y, y] : E, k = [
    v[0] * h[2] || 1 + N[0] / 2,
    v[1] * h[2] || 1 + N[1] / 2
  ], S = `${p}${e || ""}`;
  return s.jsxs("svg", { className: Ce(["react-flow__background", l]), style: {
    ...u,
    ...xr,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": a
  }, ref: f, "data-testid": "rf__background", children: [s.jsx("pattern", { id: S, x: h[0] % E[0], y: h[1] % E[1], width: E[0], height: E[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${k[0]},-${k[1]})`, children: b ? s.jsx(Jm, { radius: y / 2, className: d }) : s.jsx(Gm, { dimensions: N, lineWidth: r, variant: t, className: d }) }), s.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${S})` })] });
}
Ul.displayName = "Background";
const Zl = Ee(Ul);
function tx() {
  return s.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: s.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function nx() {
  return s.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: s.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function ox() {
  return s.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: s.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function rx() {
  return s.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: s.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function ix() {
  return s.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: s.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function Do({ children: e, className: t, ...n }) {
  return s.jsx("button", { type: "button", className: Ce(["react-flow__controls-button", t]), ...n, children: e });
}
const sx = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function Gl({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: i, onZoomOut: a, onFitView: c, onInteractiveChange: u, className: l, children: d, position: f = "bottom-left", orientation: h = "vertical", "aria-label": p }) {
  const g = Ne(), { isInteractive: b, minZoomReached: w, maxZoomReached: m, ariaLabelConfig: E } = fe(sx, be), { zoomIn: y, zoomOut: v, fitView: N } = Yi(), k = () => {
    y(), i?.();
  }, S = () => {
    v(), a?.();
  }, _ = () => {
    N(r), c?.();
  }, P = () => {
    g.setState({
      nodesDraggable: !b,
      nodesConnectable: !b,
      elementsSelectable: !b
    }), u?.(!b);
  }, X = h === "horizontal" ? "horizontal" : "vertical";
  return s.jsxs(mr, { className: Ce(["react-flow__controls", X, l]), position: f, style: e, "data-testid": "rf__controls", "aria-label": p ?? E["controls.ariaLabel"], children: [t && s.jsxs(s.Fragment, { children: [s.jsx(Do, { onClick: k, className: "react-flow__controls-zoomin", title: E["controls.zoomIn.ariaLabel"], "aria-label": E["controls.zoomIn.ariaLabel"], disabled: m, children: s.jsx(tx, {}) }), s.jsx(Do, { onClick: S, className: "react-flow__controls-zoomout", title: E["controls.zoomOut.ariaLabel"], "aria-label": E["controls.zoomOut.ariaLabel"], disabled: w, children: s.jsx(nx, {}) })] }), n && s.jsx(Do, { className: "react-flow__controls-fitview", onClick: _, title: E["controls.fitView.ariaLabel"], "aria-label": E["controls.fitView.ariaLabel"], children: s.jsx(ox, {}) }), o && s.jsx(Do, { className: "react-flow__controls-interactive", onClick: P, title: E["controls.interactive.ariaLabel"], "aria-label": E["controls.interactive.ariaLabel"], children: b ? s.jsx(ix, {}) : s.jsx(rx, {}) }), d] });
}
Gl.displayName = "Controls";
const Jl = Ee(Gl);
function ax({ id: e, x: t, y: n, width: o, height: r, style: i, color: a, strokeColor: c, strokeWidth: u, className: l, borderRadius: d, shapeRendering: f, selected: h, onClick: p }) {
  const { background: g, backgroundColor: b } = i || {}, w = a || g || b;
  return s.jsx("rect", { className: Ce(["react-flow__minimap-node", { selected: h }, l]), x: t, y: n, rx: d, ry: d, width: o, height: r, style: {
    fill: w,
    stroke: c,
    strokeWidth: u
  }, shapeRendering: f, onClick: p ? (m) => p(m, e) : void 0 });
}
const cx = Ee(ax), lx = (e) => e.nodes.map((t) => t.id), ni = (e) => e instanceof Function ? e : () => e;
function ux({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: i = cx,
  onClick: a
}) {
  const c = fe(lx, be), u = ni(t), l = ni(e), d = ni(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return s.jsx(s.Fragment, { children: c.map((h) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    s.jsx(fx, { id: h, nodeColorFunc: u, nodeStrokeColorFunc: l, nodeClassNameFunc: d, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: i, onClick: a, shapeRendering: f }, h)
  )) });
}
function dx({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: i, shapeRendering: a, NodeComponent: c, onClick: u }) {
  const { node: l, x: d, y: f, width: h, height: p } = fe((g) => {
    const b = g.nodeLookup.get(e);
    if (!b)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const w = b.internals.userNode, { x: m, y: E } = b.internals.positionAbsolute, { width: y, height: v } = pt(w);
    return {
      node: w,
      x: m,
      y: E,
      width: y,
      height: v
    };
  }, be);
  return !l || l.hidden || !qc(l) ? null : s.jsx(c, { x: d, y: f, width: h, height: p, style: l.style, selected: !!l.selected, className: o(l), color: t(l), borderRadius: r, strokeColor: n(l), strokeWidth: i, shapeRendering: a, onClick: u, id: l.id });
}
const fx = Ee(dx);
var hx = Ee(ux);
const px = 200, gx = 150, yx = (e) => !e.hidden, mx = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? Xc(io(e.nodeLookup, { filter: yx }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, xx = "react-flow__minimap-desc";
function Ql({
  style: e,
  className: t,
  nodeStrokeColor: n,
  nodeColor: o,
  nodeClassName: r = "",
  nodeBorderRadius: i = 5,
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
  pannable: b = !1,
  zoomable: w = !1,
  ariaLabel: m,
  inversePan: E,
  zoomStep: y = 1,
  offsetScale: v = 5
}) {
  const N = Ne(), k = se(null), { boundingRect: S, viewBB: _, rfId: P, panZoom: X, translateExtent: M, flowWidth: $, flowHeight: H, ariaLabelConfig: j } = fe(mx, be), A = e?.width ?? px, I = e?.height ?? gx, D = S.width / A, T = S.height / I, R = Math.max(D, T), V = R * A, B = R * I, F = v * R, Z = S.x - (V - S.width) / 2 - F, Y = S.y - (B - S.height) / 2 - F, te = V + F * 2, ce = B + F * 2, J = `${xx}-${P}`, L = se(0), U = se();
  L.current = R, oe(() => {
    if (k.current && X)
      return U.current = Dg({
        domNode: k.current,
        panZoom: X,
        getTransform: () => N.getState().transform,
        getViewScale: () => L.current
      }), () => {
        U.current?.destroy();
      };
  }, [X]), oe(() => {
    U.current?.update({
      translateExtent: M,
      width: $,
      height: H,
      inversePan: E,
      pannable: b,
      zoomStep: y,
      zoomable: w
    });
  }, [b, w, E, y, M, $, H]);
  const ae = p ? (ee) => {
    const [le, O] = U.current?.pointer(ee) || [0, 0];
    p(ee, { x: le, y: O });
  } : void 0, re = g ? de((ee, le) => {
    const O = N.getState().nodeLookup.get(le).internals.userNode;
    g(ee, O);
  }, []) : void 0, Q = m ?? j["minimap.ariaLabel"];
  return s.jsx(mr, { position: h, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * R : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof a == "number" ? a : void 0
  }, className: Ce(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: s.jsxs("svg", { width: A, height: I, viewBox: `${Z} ${Y} ${te} ${ce}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": J, ref: k, onClick: ae, children: [Q && s.jsx("title", { id: J, children: Q }), s.jsx(hx, { onClick: re, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: i, nodeClassName: r, nodeStrokeWidth: a, nodeComponent: c }), s.jsx("path", { className: "react-flow__minimap-mask", d: `M${Z - F},${Y - F}h${te + F * 2}v${ce + F * 2}h${-te - F * 2}z
        M${_.x},${_.y}h${_.width}v${_.height}h${-_.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
Ql.displayName = "MiniMap";
const eu = Ee(Ql), wx = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, vx = {
  [fn.Line]: "right",
  [fn.Handle]: "bottom-right"
};
function bx({ nodeId: e, position: t, variant: n = fn.Handle, className: o, style: r = void 0, children: i, color: a, minWidth: c = 10, minHeight: u = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: h, autoScale: p = !0, shouldResize: g, onResizeStart: b, onResize: w, onResizeEnd: m }) {
  const E = Il(), y = typeof e == "string" ? e : E, v = Ne(), N = se(null), k = n === fn.Handle, S = fe(de(wx(k && p), [k, p]), be), _ = se(null), P = t ?? vx[n];
  oe(() => {
    if (!(!N.current || !y))
      return _.current || (_.current = Xg({
        domNode: N.current,
        nodeId: y,
        getStoreItems: () => {
          const { nodeLookup: M, transform: $, snapGrid: H, snapToGrid: j, nodeOrigin: A, domNode: I } = v.getState();
          return {
            nodeLookup: M,
            transform: $,
            snapGrid: H,
            snapToGrid: j,
            nodeOrigin: A,
            paneDomNode: I
          };
        },
        onChange: (M, $) => {
          const { triggerNodeChanges: H, nodeLookup: j, parentLookup: A, nodeOrigin: I } = v.getState(), D = [], T = { x: M.x, y: M.y }, R = j.get(y);
          if (R && R.expandParent && R.parentId) {
            const V = R.origin ?? I, B = M.width ?? R.measured.width ?? 0, F = M.height ?? R.measured.height ?? 0, Z = {
              id: R.id,
              parentId: R.parentId,
              rect: {
                width: B,
                height: F,
                ...Yc({
                  x: M.x ?? R.position.x,
                  y: M.y ?? R.position.y
                }, { width: B, height: F }, R.parentId, j, V)
              }
            }, Y = qi([Z], j, A, I);
            D.push(...Y), T.x = M.x ? Math.max(V[0] * B, M.x) : void 0, T.y = M.y ? Math.max(V[1] * F, M.y) : void 0;
          }
          if (T.x !== void 0 && T.y !== void 0) {
            const V = {
              id: y,
              type: "position",
              position: { ...T }
            };
            D.push(V);
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
          for (const V of $) {
            const B = {
              ...V,
              type: "position"
            };
            D.push(B);
          }
          H(D);
        },
        onEnd: ({ width: M, height: $ }) => {
          const H = {
            id: y,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: M,
              height: $
            }
          };
          v.getState().triggerNodeChanges([H]);
        }
      })), _.current.update({
        controlPosition: P,
        boundaries: {
          minWidth: c,
          minHeight: u,
          maxWidth: l,
          maxHeight: d
        },
        keepAspectRatio: f,
        resizeDirection: h,
        onResizeStart: b,
        onResize: w,
        onResizeEnd: m,
        shouldResize: g
      }), () => {
        _.current?.destroy();
      };
  }, [
    P,
    c,
    u,
    l,
    d,
    f,
    b,
    w,
    m,
    g
  ]);
  const X = P.split("-");
  return s.jsx("div", { className: Ce(["react-flow__resize-control", "nodrag", ...X, n, o]), ref: N, style: {
    ...r,
    scale: S,
    ...a && { [k ? "backgroundColor" : "borderColor"]: a }
  }, children: i });
}
Ee(bx);
const Nx = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), tu = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var Sx = {
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
const jx = ar(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: o,
    className: r = "",
    children: i,
    iconNode: a,
    ...c
  }, u) => ai(
    "svg",
    {
      ref: u,
      ...Sx,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: tu("lucide", r),
      ...c
    },
    [
      ...a.map(([l, d]) => ai(l, d)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const ge = (e, t) => {
  const n = ar(
    ({ className: o, ...r }, i) => ai(jx, {
      ref: i,
      iconNode: t,
      className: tu(`lucide-${Nx(e)}`, o),
      ...r
    })
  );
  return n.displayName = `${e}`, n;
};
const nu = ge("Boxes", [
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
const yn = ge("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const Ex = ge("ChevronDown", [
  ["path", { d: "m6 9 6 6 6-6", key: "qrunsl" }]
]);
const tr = ge("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const Qt = ge("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const qe = ge("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const kx = ge("CodeXml", [
  ["path", { d: "m18 16 4-4-4-4", key: "1inbqp" }],
  ["path", { d: "m6 8-4 4 4 4", key: "15zrgr" }],
  ["path", { d: "m14.5 4-5 16", key: "e7oirm" }]
]);
const Cx = ge("Copy", [
  ["rect", { width: "14", height: "14", x: "8", y: "8", rx: "2", ry: "2", key: "17jyea" }],
  ["path", { d: "M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2", key: "zix9uf" }]
]);
const Ix = ge("Download", [
  ["path", { d: "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4", key: "ih7n3h" }],
  ["polyline", { points: "7 10 12 15 17 10", key: "2ggqvy" }],
  ["line", { x1: "12", x2: "12", y1: "15", y2: "3", key: "1vk2je" }]
]);
const ou = ge("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const _x = ge("GripVertical", [
  ["circle", { cx: "9", cy: "12", r: "1", key: "1vctgf" }],
  ["circle", { cx: "9", cy: "5", r: "1", key: "hp0tcf" }],
  ["circle", { cx: "9", cy: "19", r: "1", key: "fkjjf6" }],
  ["circle", { cx: "15", cy: "12", r: "1", key: "1tmaij" }],
  ["circle", { cx: "15", cy: "5", r: "1", key: "19l28e" }],
  ["circle", { cx: "15", cy: "19", r: "1", key: "f4zoj3" }]
]);
const Zi = ge("ListTree", [
  ["path", { d: "M21 12h-8", key: "1bmf0i" }],
  ["path", { d: "M21 6H8", key: "1pqkrb" }],
  ["path", { d: "M21 18h-8", key: "1tm79t" }],
  ["path", { d: "M3 6v4c0 1.1.9 2 2 2h3", key: "1ywdgy" }],
  ["path", { d: "M3 10v6c0 1.1.9 2 2 2h3", key: "2wc746" }]
]);
const nr = ge("Maximize2", [
  ["polyline", { points: "15 3 21 3 21 9", key: "mznyad" }],
  ["polyline", { points: "9 21 3 21 3 15", key: "1avn1i" }],
  ["line", { x1: "21", x2: "14", y1: "3", y2: "10", key: "ota7mn" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const Ma = ge("Minimize2", [
  ["polyline", { points: "4 14 10 14 10 20", key: "11kfnr" }],
  ["polyline", { points: "20 10 14 10 14 4", key: "rlmsce" }],
  ["line", { x1: "14", x2: "21", y1: "10", y2: "3", key: "o5lafz" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const Ax = ge("Network", [
  ["rect", { x: "16", y: "16", width: "6", height: "6", rx: "1", key: "4q2zg0" }],
  ["rect", { x: "2", y: "16", width: "6", height: "6", rx: "1", key: "8cvhb9" }],
  ["rect", { x: "9", y: "2", width: "6", height: "6", rx: "1", key: "1egb70" }],
  ["path", { d: "M5 16v-3a1 1 0 0 1 1-1h12a1 1 0 0 1 1 1v3", key: "1jsf9p" }],
  ["path", { d: "M12 12V8", key: "2874zd" }]
]);
const Mx = ge("Package", [
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
const Qn = ge("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const Gi = ge("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const Dx = ge("Redo2", [
  ["path", { d: "m15 14 5-5-5-5", key: "12vg1m" }],
  ["path", { d: "M20 9H9.5A5.5 5.5 0 0 0 4 14.5A5.5 5.5 0 0 0 9.5 20H13", key: "6uklza" }]
]);
const Ji = ge("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const Px = ge("Save", [
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
const Qi = ge("Search", [
  ["circle", { cx: "11", cy: "11", r: "8", key: "4ej97u" }],
  ["path", { d: "m21 21-4.3-4.3", key: "1qie3q" }]
]);
const ru = ge("SlidersHorizontal", [
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
const Nt = ge("Sparkles", [
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
const Tx = ge("Terminal", [
  ["polyline", { points: "4 17 10 11 4 5", key: "akl6gq" }],
  ["line", { x1: "12", x2: "20", y1: "19", y2: "19", key: "q2wloq" }]
]);
const bi = ge("Trash2", [
  ["path", { d: "M3 6h18", key: "d0wm0j" }],
  ["path", { d: "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", key: "4alrt4" }],
  ["path", { d: "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", key: "v07s0e" }],
  ["line", { x1: "10", x2: "10", y1: "11", y2: "17", key: "1uufr5" }],
  ["line", { x1: "14", x2: "14", y1: "11", y2: "17", key: "xtxkd" }]
]);
const $x = ge("Undo2", [
  ["path", { d: "M9 14 4 9l5-5", key: "102s5s" }],
  ["path", { d: "M4 9h10.5a5.5 5.5 0 0 1 5.5 5.5a5.5 5.5 0 0 1-5.5 5.5H11", key: "f3b9sd" }]
]);
const Rx = ge("Workflow", [
  ["rect", { width: "8", height: "8", x: "3", y: "3", rx: "2", key: "by2w9f" }],
  ["path", { d: "M7 11v4a2 2 0 0 0 2 2h4", key: "xkn7yn" }],
  ["rect", { width: "8", height: "8", x: "13", y: "13", rx: "2", key: "1cgmvn" }]
]);
const iu = ge("X", [
  ["path", { d: "M18 6 6 18", key: "1bl5f8" }],
  ["path", { d: "m6 6 12 12", key: "d8bk6v" }]
]);
const zx = ge("Zap", [
  [
    "path",
    {
      d: "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z",
      key: "1xq2db"
    }
  ]
]), Lx = /* @__PURE__ */ new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);
function wr(e) {
  return su(e, Vx);
}
function vr(e) {
  return su(e, Ox);
}
function su(e, t) {
  return !e || !e.rootActivity ? e : { ...e, rootActivity: au(e.rootActivity, t) };
}
function au(e, t) {
  const n = t(e), o = n.structure;
  if (!o || !pn(o.payload)) return n;
  let r = !1;
  const i = { ...o.payload };
  for (const [a, c] of Object.entries(o.payload))
    Array.isArray(c) && c.length > 0 && c.every(Xx) && (i[a] = c.map((u) => au(u, t)), r = !0);
  return r ? { ...n, structure: { ...o, payload: i } } : n;
}
function Vx(e) {
  const t = [], n = {};
  for (const [r, i] of Object.entries(e))
    Lx.has(r) || (Bx(i) ? t.push({
      referenceKey: Hx(r),
      value: { value: Wx(i.expression.value), expressionType: i.expression.type || "Literal" }
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
function Ox(e) {
  const t = Array.isArray(e.inputs) ? e.inputs : [], n = {};
  for (const o of t) {
    if (!pn(o) || typeof o.referenceKey != "string") continue;
    const r = pn(o.value) ? o.value : {};
    n[Fx(o.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof r.expressionType == "string" ? r.expressionType : "Literal",
        value: r.value ?? ""
      }
    };
  }
  return { ...e, ...n, inputs: [] };
}
function Hx(e) {
  return e && e.charAt(0).toUpperCase() + e.slice(1);
}
function Fx(e) {
  return e && e.charAt(0).toLowerCase() + e.slice(1);
}
function Wx(e) {
  return e == null ? null : typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function Bx(e) {
  if (!pn(e) || Array.isArray(e)) return !1;
  const t = e.expression;
  return pn(t) && typeof t.type == "string";
}
function Xx(e) {
  return pn(e) && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function pn(e) {
  return typeof e == "object" && e !== null;
}
const Re = "/_elsa/workflow-management", Kx = "/publishing";
async function qx(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${Re}/definitions?${n.toString()}`);
}
async function Yx(e, t) {
  const n = await e.http.getJson(`${Re}/definitions/${encodeURIComponent(t)}`);
  return n.draft ? { ...n, draft: { ...n.draft, state: vr(n.draft.state) } } : n;
}
async function Ux(e, t) {
  const n = await e.http.getJson(`${Re}/versions/${encodeURIComponent(t)}`);
  return { ...n, state: vr(n.state) };
}
async function Zx(e, t) {
  return e.http.postJson(`${Re}/definitions`, t);
}
async function Gx(e, t) {
  await e.http.deleteJson(`${Re}/definitions/${encodeURIComponent(t)}`);
}
async function Jx(e, t) {
  await e.http.postJson(`${Re}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function Qx(e, t) {
  await e.http.deleteJson(`${Re}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function ew(e, t) {
  const n = await e.http.putJson(
    `${Re}/drafts/${encodeURIComponent(t.id)}`,
    { state: wr(t.state), layout: t.layout }
  );
  return { ...n, state: vr(n.state) };
}
async function tw(e, t) {
  return e.http.postJson(`${Re}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function nw(e, t) {
  return e.http.postJson(`${Re}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function ow(e, t) {
  const n = { ...t, state: wr(t.state) };
  try {
    return await e.http.postJson(`${Kx}/workflows/drafts/test-runs`, n);
  } catch (o) {
    const r = cw(o);
    if (r) return r;
    throw o;
  }
}
async function cu(e, t) {
  return e.http.postJson(`${Re}/executables/${encodeURIComponent(t)}/run`, {});
}
async function lu(e) {
  return e.http.getJson("/_demo/workflows/executables");
}
async function rw(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.runKind && n.set("runKind", t.runKind), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const o = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${o ? `?${o}` : ""}`);
}
async function iw(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function es(e) {
  return e.http.getJson(`${Re}/activities`);
}
async function sw(e) {
  const t = await uu(e, [
    `${Re}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? Da(t) : Da(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function aw(e) {
  const t = await uu(e, [
    `${Re}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : Oo;
}
async function uu(e, t) {
  let n;
  for (const o of t)
    try {
      return await e.http.getJson(o);
    } catch (r) {
      n = r;
    }
  throw n;
}
function Da(e) {
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
function cw(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = Pa(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return Pa(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function Pa(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const Oo = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
], br = "elsa.sequence.structure", co = "elsa.flowchart.structure";
function du(e, t) {
  if (!e) return null;
  let n = e;
  for (const o of t) {
    const r = Qe(n).find((a) => a.id === o.slotId);
    if (!r) return null;
    const i = r.activities.find((a) => a.nodeId === o.ownerNodeId);
    if (!i) return null;
    n = i;
  }
  return n;
}
function Wn(e, t) {
  const n = du(e, t);
  if (!n) return null;
  let o = Qe(n)[0];
  return o ? { owner: n, slot: o } : null;
}
function Qe(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = Iw(t), r = oi(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: _w(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, i]) => oi(i)).map(([i, a]) => ({
    id: `${t.kind}:${i}`,
    label: Mw(i),
    property: i,
    mode: "generic",
    activities: oi(a) ?? []
  }));
}
function fu(e, t, n) {
  const o = new Map(t.map((a) => [a.activityVersionId, a])), r = new Map(n.map((a) => [a.nodeId, a])), i = e.slot.activities.map((a, c) => {
    const u = o.get(a.activityVersionId), l = r.get(a.nodeId) ?? Aw(e.slot.mode, c);
    return gu(a, u, { x: l.x, y: l.y });
  });
  return {
    nodes: i,
    edges: e.slot.mode === "flowchart" ? vw(e.owner) : ww(e.slot, i)
  };
}
function Ni(e, t, n) {
  const o = t.find((i) => i.activityVersionId === e.activityVersionId), r = n.find((i) => i.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [gu(e, o, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function lw(e, t, n, o = null) {
  const r = new Map(t.map((c) => [c.activityExecutionId, c])), i = $a(t, (c) => c.authoredActivityId || c.executableNodeId), a = $a(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? r.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const u = i.get(c.id) ?? [], l = a.get(c.id) ?? [];
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
function ts(e, t) {
  return e?.structure?.kind === co || pw(t) ? "flowchart" : e?.structure?.kind === br || gw(t) ? "sequence" : "unsupported";
}
function Si(e, t, n) {
  if (t.length === 0) {
    const c = Qe(e)[0];
    return c ? eo(e, c, n) : e;
  }
  const [o, ...r] = t, i = Qe(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const a = i.activities.map((c) => c.nodeId === o.ownerNodeId ? Si(c, r, n) : c);
  return eo(e, i, a);
}
function hu(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, i = Qe(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const a = i.activities.map((c) => c.nodeId === o.ownerNodeId ? hu(c, r, n) : c);
  return eo(e, i, a);
}
function pu(e, t, n) {
  if (e.nodeId === t) return n(e);
  const o = Qe(e);
  if (o.length === 0) return e;
  let r = !1, i = e;
  for (const a of o) {
    const c = a.activities.map((u) => {
      const l = pu(u, t, n);
      return l !== u && (r = !0), l;
    });
    r && (i = eo(i, a, c));
  }
  return r ? i : e;
}
function eo(e, t, n) {
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
function uw(e, t, n, o = []) {
  const r = new Map(e.slot.activities.map((a) => [a.nodeId, a]));
  for (const a of o)
    r.set(a.nodeId, a);
  const i = t.map((a) => r.get(a.id)).filter((a) => !!a);
  return e.slot.mode === "sequence" && i.sort((a, c) => {
    const u = t.find((d) => d.id === a.nodeId), l = t.find((d) => d.id === c.nodeId);
    return (u?.position.x ?? 0) - (l?.position.x ?? 0);
  }), eo(e.owner, e.slot, i);
}
function dw(e, t) {
  return {
    ...e,
    structure: xw(e.structure, t)
  };
}
function fw(e, t) {
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
function ji(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: mw(e)
  };
}
function je(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? yw(t) : n;
}
function gu(e, t, n, o = {}) {
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
      icon: or(t),
      childSlots: Qe(e),
      acceptsInbound: bw(e, t),
      sourcePorts: o.suppressFlowPorts ? [] : yu(e, t),
      suppressFlowPorts: o.suppressFlowPorts
    }
  };
}
function or(e) {
  if (!e) return "activity";
  const t = hw(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), o = je(e).toLowerCase(), r = e.category?.toLowerCase() ?? "", i = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || o === "flowchart" ? "flowchart" : n.endsWith(".sequence") || o === "sequence" ? "sequence" : n.includes("writeline") || o.includes("write line") ? "terminal" : r.includes("runtime") ? "runtime" : i === "trigger" ? "trigger" : "activity";
}
function hw(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function pw(e) {
  return !!e && (je(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function gw(e) {
  return !!e && (je(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function yw(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function mw(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: br,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: co,
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
function xw(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], o = /* @__PURE__ */ new Map();
  for (const r of n) {
    if (!ns(r)) continue;
    const i = r.id;
    typeof i == "string" && o.set(i, r);
  }
  return {
    ...e,
    payload: {
      ...e.payload,
      connections: t.map((r) => {
        const i = o.get(r.id) ?? {}, a = r.data?.vertices, { vertices: c, ...u } = i;
        return {
          ...u,
          id: r.id,
          source: { nodeId: r.source, port: r.sourceHandle ?? "Done" },
          target: r.targetHandle ? { nodeId: r.target, port: r.targetHandle } : { nodeId: r.target },
          ...a?.length ? { vertices: a.map((l) => ({ x: Math.round(l.x), y: Math.round(l.y) })) } : {}
        };
      })
    }
  };
}
function ww(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function vw(e) {
  if (e.structure?.kind !== co) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, i = n.target;
    if (!r?.nodeId || !i?.nodeId) return null;
    const a = Array.isArray(n.vertices) ? n.vertices.filter(kw) : [];
    return {
      id: typeof n.id == "string" ? String(n.id) : `flow-${o}-${r.nodeId}-${i.nodeId}`,
      source: r.nodeId,
      target: i.nodeId,
      sourceHandle: r.port,
      targetHandle: i.port && i.port !== "Done" ? i.port : void 0,
      type: "workflow",
      label: r.port && r.port !== "Done" ? r.port : void 0,
      data: a.length ? { vertices: a } : void 0
    };
  }).filter((n) => !!n) : [];
}
function yu(e, t) {
  const n = Ta(e.cases);
  if (Sw(e, t) && n.length > 0)
    return [...n.map((i) => ({ name: i, displayName: i })), { name: "Default", displayName: "Default" }];
  const o = [
    ...Ho(t?.designFacets),
    ...Ho(t?.ports),
    ...Ho(t?.outputs)
  ];
  if (o.length > 0) return jw(o);
  const r = Ta(e.outcomes);
  return r.length > 0 ? r.map((i) => ({ name: i, displayName: i })) : [{ name: "Done", displayName: "Done" }];
}
function bw(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function rr(e, t, n, o) {
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
function Nw(e, t, n) {
  const o = rr(t.source, n, t.sourceHandle ?? "Done", void 0), r = rr(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((i) => i.id !== t.id).concat(o, r);
}
function oi(e) {
  return Array.isArray(e) ? e.filter(Cw) : null;
}
function Sw(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function Ho(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!ns(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...Ho(n.ports));
      continue;
    }
    const o = typeof n.type == "string" ? n.type : typeof n.portType == "string" ? n.portType : "", r = n.isBrowsable !== !1 && n.browsable !== !1, i = typeof n.name == "string" ? n.name : typeof n.id == "string" ? n.id : "";
    if (r && o.toLowerCase() === "flow" && i) {
      const a = typeof n.displayName == "string" ? n.displayName : i;
      t.push({ name: i, displayName: a });
    }
  }
  return t;
}
function jw(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function Ta(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function $a(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const o of e) {
    const r = t(o);
    r && n.set(r, [...n.get(r) ?? [], o]);
  }
  return n;
}
function Ew(e) {
  return [...e].sort((t, n) => Ra(n).localeCompare(Ra(t)))[0];
}
function Ra(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function kw(e) {
  return ns(e) && typeof e.x == "number" && typeof e.y == "number";
}
function ns(e) {
  return typeof e == "object" && e !== null;
}
function Cw(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Iw(e) {
  return e.kind === br ? "sequence" : e.kind === co ? "flowchart" : "generic";
}
function _w(e) {
  return e.kind === br || e.kind === co, "Activities";
}
function Aw(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function Mw(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Dw = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function mu(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function os(e) {
  return mu(e.name);
}
function Pw(e, t) {
  const n = os(t), o = e[n];
  return t.isWrapped === !1 ? o ?? t.defaultValue ?? "" : wu(o, t);
}
function xu(e, t) {
  return wu(e[os(t)], t);
}
function Tw(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function $w(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function za(e, t, n) {
  return {
    ...e,
    [os(t)]: n
  };
}
function Rw(e, t) {
  return t.isWrapped === !1 ? Pw(e, t) : xu(e, t).expression.value;
}
function wu(e, t) {
  return zw(e) ? {
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
function zw(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
const vu = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]);
function Lw({
  activity: e,
  descriptor: t,
  editors: n,
  expressionEditors: o,
  expressionDescriptors: r,
  descriptorStatus: i,
  onChange: a
}) {
  if (i === "loading")
    return /* @__PURE__ */ s.jsx("p", { className: "wf-muted", children: "Loading activity properties..." });
  if (!t)
    return /* @__PURE__ */ s.jsx("p", { className: "wf-muted", children: "No activity descriptor is available for this activity." });
  const c = t.inputs.filter((d) => d.isBrowsable !== !1).sort((d, f) => (d.order ?? 0) - (f.order ?? 0) || d.name.localeCompare(f.name));
  if (c.length === 0)
    return /* @__PURE__ */ s.jsx("p", { className: "wf-muted", children: "This activity does not expose editable properties." });
  const u = Bw(c), l = r.length > 0 ? r : Dw;
  return /* @__PURE__ */ s.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ s.jsx("span", { className: "wf-section-label", children: "Properties" }),
    u.map((d) => /* @__PURE__ */ s.jsxs("section", { className: "wf-property-group", children: [
      u.length > 1 ? /* @__PURE__ */ s.jsx("h4", { children: d.category }) : null,
      d.inputs.map((f) => /* @__PURE__ */ s.jsx(
        Vw,
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
function Vw({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: o,
  expressionDescriptors: r,
  onChange: i
}) {
  const a = t.isReadOnly === !0, c = { activity: e, expressionDescriptors: r, readOnly: a }, u = Fw(n, t, c), l = u?.component, d = t.isWrapped !== !1 ? xu(e, t) : null, f = d?.expression.type ?? "Literal", h = Rw(e, t), p = d ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: r,
    readOnly: a,
    surface: "inline",
    syntax: f
  } : null, g = p ? bu(o, p) : null, b = g?.surfaces.inline, w = g && p ? Nu(g, p, h) : [], m = !!(d && Xw(t, u?.id)), E = !!(d && Kw(t, u?.id)), [y, v] = K(!1), N = (_) => {
    const P = d ? Tw(d, _) : _;
    i(za(e, t, P));
  }, k = (_) => {
    d && i(za(e, t, $w(d, _)));
  }, S = b && p ? /* @__PURE__ */ s.jsx(
    b,
    {
      descriptor: t,
      syntax: f,
      value: h,
      disabled: a,
      context: p,
      onChange: N
    }
  ) : Hw(l, t, h, a, c, N);
  return /* @__PURE__ */ s.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ s.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ s.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ s.jsx("span", { children: Su(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ s.jsx("p", { children: t.description }) : null,
    d && !m ? /* @__PURE__ */ s.jsx(
      Ei,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: f,
        descriptors: r,
        disabled: a,
        onChange: k
      }
    ) : null,
    m ? /* @__PURE__ */ s.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ s.jsxs("div", { className: "wf-expression-editor", children: [
        S,
        ki(w)
      ] }),
      /* @__PURE__ */ s.jsx(
        Ei,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: f,
          descriptors: r,
          disabled: a,
          variant: "inline",
          onChange: k
        }
      ),
      E ? /* @__PURE__ */ s.jsx(
        "button",
        {
          type: "button",
          className: "wf-expression-expand-button",
          "aria-label": `Open expanded ${t.displayName || t.name} editor`,
          title: "Open expanded editor",
          onClick: () => v(!0),
          children: /* @__PURE__ */ s.jsx(nr, { size: 13 })
        }
      ) : null
    ] }) : /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
      S,
      ki(w)
    ] }),
    E && !m ? /* @__PURE__ */ s.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => v(!0),
        children: [
          /* @__PURE__ */ s.jsx(nr, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    y ? /* @__PURE__ */ s.jsx(
      Ow,
      {
        input: t,
        value: h,
        syntax: f,
        descriptors: r,
        activity: e,
        expressionEditors: o,
        disabled: a,
        onChange: N,
        onSyntaxChange: k,
        onClose: () => v(!1)
      }
    ) : null
  ] });
}
function Ow({
  input: e,
  value: t,
  syntax: n,
  descriptors: o,
  activity: r,
  expressionEditors: i,
  disabled: a,
  onChange: c,
  onSyntaxChange: u,
  onClose: l
}) {
  const d = ac(), f = e.displayName || e.name, h = {
    activity: r,
    descriptor: e,
    expressionDescriptors: o,
    readOnly: a,
    surface: "expanded",
    syntax: n
  }, p = bu(i, h), g = p?.surfaces.expanded, b = p ? Nu(p, h, t) : [], w = g ? null : Ww(i, h);
  return oe(() => {
    const m = (E) => {
      E.key === "Escape" && l();
    };
    return window.addEventListener("keydown", m), () => window.removeEventListener("keydown", m);
  }, [l]), /* @__PURE__ */ s.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ s.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": d, children: [
    /* @__PURE__ */ s.jsxs("header", { children: [
      /* @__PURE__ */ s.jsxs("div", { children: [
        /* @__PURE__ */ s.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ s.jsx("h3", { id: d, children: f })
      ] }),
      /* @__PURE__ */ s.jsx("button", { type: "button", "aria-label": `Close ${f} editor`, onClick: l, children: /* @__PURE__ */ s.jsx(iu, { size: 16 }) })
    ] }),
    /* @__PURE__ */ s.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ s.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ s.jsx(
          Ei,
          {
            label: `${f} expression syntax`,
            value: n,
            descriptors: o,
            disabled: a,
            onChange: u
          }
        ),
        /* @__PURE__ */ s.jsx("span", { children: Su(e.typeName) })
      ] }),
      e.description ? /* @__PURE__ */ s.jsx("p", { children: e.description }) : null,
      g ? /* @__PURE__ */ s.jsx(
        g,
        {
          descriptor: e,
          syntax: n,
          value: t,
          disabled: a,
          context: h,
          onChange: c
        }
      ) : /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
        w ? /* @__PURE__ */ s.jsx("p", { className: "wf-expression-editor-hint", children: w }) : null,
        /* @__PURE__ */ s.jsx(
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
      ki(b)
    ] }),
    /* @__PURE__ */ s.jsxs("footer", { children: [
      /* @__PURE__ */ s.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ s.jsx("button", { type: "button", onClick: l, children: "Close" })
    ] })
  ] }) });
}
function Hw(e, t, n, o, r, i) {
  return e ? /* @__PURE__ */ s.jsx(
    e,
    {
      descriptor: t,
      value: n,
      disabled: o,
      context: r,
      onChange: i
    }
  ) : /* @__PURE__ */ s.jsx("input", { type: "text", value: n == null ? "" : String(n), disabled: o, onChange: (a) => i(a.target.value) });
}
function Ei({
  label: e,
  value: t,
  descriptors: n,
  disabled: o,
  variant: r = "block",
  onChange: i
}) {
  const [a, c] = K(!1), u = ac(), l = n.find((f) => f.type === t), d = [
    "wf-syntax-picker-trigger",
    r === "inline" ? "inline" : "",
    a ? "open" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ s.jsxs("div", { className: r === "inline" ? "wf-syntax-picker inline" : "wf-syntax-picker", onBlur: (f) => {
    f.currentTarget.contains(f.relatedTarget) || c(!1);
  }, children: [
    /* @__PURE__ */ s.jsx(
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
        children: /* @__PURE__ */ s.jsx("span", { children: l?.displayName || l?.type || t })
      }
    ),
    a ? /* @__PURE__ */ s.jsx("div", { id: u, role: "listbox", className: "wf-syntax-picker-menu", "aria-label": e, children: n.map((f) => {
      const h = f.displayName || f.type, p = f.type === t;
      return /* @__PURE__ */ s.jsx(
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
function Fw(e, t, n) {
  return [...e].sort((o, r) => (o.order ?? 500) - (r.order ?? 500)).find((o) => o.supports(t, n));
}
function bu(e, t) {
  return [...e].sort((n, o) => (n.order ?? 500) - (o.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function Nu(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function Ww(e, t) {
  if (t.syntax.toLowerCase() === "literal") return null;
  const n = [...e].sort((a, c) => (a.order ?? 500) - (c.order ?? 500)).find((a) => a.supports(t) && a.metadata)?.metadata;
  if (!n) return `No enhanced editor is registered for ${t.syntax}. Using the generic text editor.`;
  const o = n.displayName?.trim() || "enhanced editor", r = n.installHint?.trim(), i = `No ${o} is registered for ${t.syntax}. Using the generic text editor.`;
  return r ? `${i} ${r}` : i;
}
function ki(e) {
  return e.length === 0 ? null : /* @__PURE__ */ s.jsx("div", { className: "wf-expression-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const o = t.severity ?? "info";
    return /* @__PURE__ */ s.jsxs("p", { className: `wf-expression-editor-diagnostic ${o}`, children: [
      t.code ? /* @__PURE__ */ s.jsx("span", { children: t.code }) : null,
      t.message
    ] }, `${t.code ?? "diagnostic"}-${n}`);
  }) });
}
function Bw(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "General";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return [...t.entries()].map(([n, o]) => ({ category: n, inputs: o }));
}
function Su(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function Xw(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !vu.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function Kw(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !vu.has(t) && n !== "multiline") return !1;
  const o = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(o) || n === "singleline" || n === "multiline";
}
function qw(e, t) {
  return {
    ...t ? { name: t } : {},
    definitionId: e.definitionId,
    state: wr(e.state),
    layout: e.layout
  };
}
function Yw(e) {
  return JSON.stringify(
    {
      state: wr(e.state),
      layout: e.layout
    },
    null,
    2
  );
}
function Uw(e, t) {
  let n;
  try {
    n = JSON.parse(e);
  } catch (r) {
    return { ok: !1, error: r instanceof Error ? r.message : String(r) };
  }
  if (!n || typeof n != "object")
    return { ok: !1, error: "Workflow JSON must be an object with a 'state' property." };
  const o = n;
  return !o.state || typeof o.state != "object" ? { ok: !1, error: "Workflow JSON is missing a valid 'state' object." } : o.layout !== void 0 && !Array.isArray(o.layout) ? { ok: !1, error: "'layout' must be an array when present." } : {
    ok: !0,
    draft: {
      ...t,
      state: vr(o.state),
      layout: o.layout ?? t.layout
    }
  };
}
function Zw(e, t) {
  const n = (t ?? "workflow").trim().replace(/[^\w.-]+/g, "-") || "workflow", o = new Blob([JSON.stringify(e, null, 2)], { type: "application/json" }), r = URL.createObjectURL(o), i = document.createElement("a");
  i.href = r, i.download = `${n}.json`, document.body.appendChild(i), i.click(), i.remove(), URL.revokeObjectURL(r);
}
const Gw = 320, Jw = 140;
function Qw(e, t, n) {
  return n === "sequence" ? e0(e) : t0(e, t);
}
function e0(e) {
  const t = /* @__PURE__ */ new Map();
  return e.forEach((n, o) => {
    t.set(n.id, { x: o * 280, y: 0 });
  }), t;
}
function t0(e, t) {
  const n = /* @__PURE__ */ new Map();
  if (e.length === 0) return n;
  const o = new Set(e.map((d) => d.id)), r = t.filter((d) => o.has(d.source) && o.has(d.target)), i = /* @__PURE__ */ new Set();
  for (const d of r)
    i.add(d.source), i.add(d.target);
  const a = /* @__PURE__ */ new Map();
  for (const d of e) a.set(d.id, 0);
  for (let d = 0; d < e.length; d += 1) {
    let f = !1;
    for (const h of r) {
      const p = (a.get(h.source) ?? 0) + 1;
      p > (a.get(h.target) ?? 0) && p <= e.length && (a.set(h.target, p), f = !0);
    }
    if (!f) break;
  }
  const c = Math.max(0, ...e.filter((d) => i.has(d.id)).map((d) => a.get(d.id) ?? 0)), u = i.size > 0 ? c + 1 : 0, l = /* @__PURE__ */ new Map();
  for (const d of e) {
    const f = i.has(d.id) ? a.get(d.id) ?? 0 : u, h = l.get(f);
    h ? h.push(d.id) : l.set(f, [d.id]);
  }
  for (const [d, f] of l)
    f.forEach((h, p) => {
      n.set(h, { x: d * Gw, y: p * Jw });
    });
  return n;
}
const n0 = 50;
function La() {
  return { past: [], future: [] };
}
function o0(e) {
  return e.past.length > 0;
}
function r0(e) {
  return e.future.length > 0;
}
function Va(e, t, n = n0) {
  const o = [...e.past, t];
  return o.length > n && o.splice(0, o.length - n), { past: o, future: [] };
}
function i0(e, t) {
  if (e.past.length === 0) return null;
  const n = e.past.slice(), o = n.pop();
  return { history: { past: n, future: [...e.future, t] }, snapshot: o };
}
function s0(e, t) {
  if (e.future.length === 0) return null;
  const n = e.future.slice(), o = n.pop();
  return { history: { past: [...e.past, t], future: n }, snapshot: o };
}
function et(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function ju(e, t) {
  if (!e || !t) return "";
  const n = Date.parse(e), o = Date.parse(t);
  if (Number.isNaN(n) || Number.isNaN(o) || o < n) return "";
  const r = Math.round((o - n) / 1e3);
  if (r < 60) return `${r}s`;
  const i = Math.floor(r / 60), a = r % 60;
  if (i < 60) return a ? `${i}m ${a}s` : `${i}m`;
  const c = Math.floor(i / 60), u = i % 60;
  return u ? `${c}h ${u}m` : `${c}h`;
}
function ir(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function rs(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ s.jsx(ou, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ s.jsx(Zi, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ s.jsx(Tx, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ s.jsx(Qn, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ s.jsx(zx, { size: 15 });
    default:
      return /* @__PURE__ */ s.jsx(nu, { size: 15 });
  }
}
function Oa({
  document: e,
  readOnly: t,
  minHeight: n,
  ariaLabel: o,
  onChange: r
}) {
  const i = (a) => {
    t || r({ ...e, value: a.target.value });
  };
  return /* @__PURE__ */ s.jsx(
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
      onChange: i
    }
  );
}
function a0({
  document: e,
  diagnostics: t = [],
  readOnly: n = !1,
  theme: o = "studio",
  minHeight: r = "220px",
  ariaLabel: i,
  languageAdapter: a,
  onChange: c
}) {
  const u = t.filter((h) => !h.uri || h.uri === e.uri), l = a?.displayName ?? e.language, d = a?.loadEditor, f = pe(
    () => d ? ud(d) : null,
    [d]
  );
  return /* @__PURE__ */ s.jsxs(
    "section",
    {
      className: "studio-code-editor",
      "data-language": e.language,
      "data-theme": o,
      "data-readonly": n,
      children: [
        /* @__PURE__ */ s.jsxs("div", { className: "studio-code-editor-header", children: [
          /* @__PURE__ */ s.jsx("span", { children: l }),
          /* @__PURE__ */ s.jsx("code", { children: e.uri })
        ] }),
        f ? /* @__PURE__ */ s.jsx(dd, { fallback: /* @__PURE__ */ s.jsx(
          Oa,
          {
            document: e,
            readOnly: n,
            minHeight: r,
            ariaLabel: i,
            onChange: c
          }
        ), children: /* @__PURE__ */ s.jsx(
          f,
          {
            document: e,
            readOnly: n,
            theme: o,
            minHeight: r,
            ariaLabel: i,
            onChange: c
          }
        ) }) : /* @__PURE__ */ s.jsx(
          Oa,
          {
            document: e,
            readOnly: n,
            minHeight: r,
            ariaLabel: i,
            onChange: c
          }
        ),
        /* @__PURE__ */ s.jsx(c0, { diagnostics: u })
      ]
    }
  );
}
function c0({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ s.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const o = t.severity ?? "info", r = l0(t);
    return /* @__PURE__ */ s.jsxs(
      "p",
      {
        className: `studio-code-editor-diagnostic ${o}`,
        children: [
          t.code ? /* @__PURE__ */ s.jsx("span", { children: t.code }) : null,
          r ? /* @__PURE__ */ s.jsx("small", { children: r }) : null,
          t.message
        ]
      },
      `${t.uri ?? "document"}-${t.code ?? "diagnostic"}-${n}`
    );
  }) });
}
function l0(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const u0 = { language: "json", displayName: "JSON" };
function d0({ draft: e, onApply: t }) {
  const n = pe(() => Yw(e), [e]), [o, r] = K(n), [i, a] = K(n), [c, u] = K(null);
  oe(() => {
    r(n), a(n), u(null);
  }, [n]);
  const l = o !== i, d = c ? [{ severity: "error", message: c }] : [], f = () => u(t(o));
  return /* @__PURE__ */ s.jsxs("div", { className: "wf-code-view", children: [
    /* @__PURE__ */ s.jsxs("div", { className: "wf-code-view-toolbar", children: [
      /* @__PURE__ */ s.jsx("span", { className: "wf-muted", children: "Edit the workflow definition as JSON. Changes apply to the draft when you click Apply." }),
      /* @__PURE__ */ s.jsxs("span", { className: "wf-code-view-actions", children: [
        /* @__PURE__ */ s.jsx("button", { type: "button", disabled: !l, onClick: () => {
          r(i), u(null);
        }, children: "Reset" }),
        /* @__PURE__ */ s.jsxs("button", { type: "button", disabled: !l, onClick: f, children: [
          /* @__PURE__ */ s.jsx(yn, { size: 14 }),
          " Apply"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ s.jsx("div", { className: "wf-code-view-editor", children: /* @__PURE__ */ s.jsx(
      a0,
      {
        ariaLabel: "Workflow JSON",
        document: { uri: "elsa://workflows/definition.json", language: "json", value: o },
        languageAdapter: u0,
        diagnostics: d,
        minHeight: "100%",
        theme: "studio",
        onChange: (h) => {
          r(h.value), c && u(null);
        }
      }
    ) })
  ] });
}
const f0 = ["name", "Name"], h0 = ["typeName", "TypeName", "type", "Type"], p0 = ["value", "Value", "defaultValue", "DefaultValue"];
function g0(e) {
  return !!e && typeof e == "object" && !Array.isArray(e);
}
function ri(e, t) {
  for (const n of t)
    if (n in e && e[n] != null) return String(e[n]);
  return "";
}
function ii({ title: e, emptyLabel: t, items: n }) {
  return /* @__PURE__ */ s.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ s.jsx("h3", { children: e }),
    n.length === 0 ? /* @__PURE__ */ s.jsx("p", { className: "wf-muted", children: t }) : /* @__PURE__ */ s.jsxs("table", { className: "wf-properties-table", children: [
      /* @__PURE__ */ s.jsx("thead", { children: /* @__PURE__ */ s.jsxs("tr", { children: [
        /* @__PURE__ */ s.jsx("th", { children: "Name" }),
        /* @__PURE__ */ s.jsx("th", { children: "Type" }),
        /* @__PURE__ */ s.jsx("th", { children: "Default" })
      ] }) }),
      /* @__PURE__ */ s.jsx("tbody", { children: n.map((o, r) => {
        const i = g0(o) ? o : null;
        if (!i)
          return /* @__PURE__ */ s.jsx("tr", { children: /* @__PURE__ */ s.jsx("td", { colSpan: 3, children: /* @__PURE__ */ s.jsx("code", { children: JSON.stringify(o) }) }) }, r);
        const a = ri(i, f0), c = ri(i, h0), u = ri(i, p0);
        return /* @__PURE__ */ s.jsxs("tr", { children: [
          /* @__PURE__ */ s.jsx("td", { children: a || /* @__PURE__ */ s.jsx("span", { className: "wf-muted", children: "—" }) }),
          /* @__PURE__ */ s.jsx("td", { children: c || /* @__PURE__ */ s.jsx("span", { className: "wf-muted", children: "—" }) }),
          /* @__PURE__ */ s.jsx("td", { children: u || /* @__PURE__ */ s.jsx("span", { className: "wf-muted", children: "—" }) })
        ] }, r);
      }) })
    ] })
  ] });
}
function y0({ details: e, draft: t }) {
  const n = t.state.variables ?? [], o = t.state.inputs ?? [], r = t.state.outputs ?? [], i = e?.versions ?? [], a = e?.definition.description?.trim();
  return /* @__PURE__ */ s.jsxs("div", { className: "wf-properties-view", children: [
    /* @__PURE__ */ s.jsxs("section", { className: "wf-properties-section", children: [
      /* @__PURE__ */ s.jsx("h3", { children: "Information" }),
      /* @__PURE__ */ s.jsxs("dl", { className: "wf-properties-info", children: [
        /* @__PURE__ */ s.jsx("dt", { children: "Name" }),
        /* @__PURE__ */ s.jsx("dd", { children: e?.definition.name ?? "—" }),
        /* @__PURE__ */ s.jsx("dt", { children: "Description" }),
        /* @__PURE__ */ s.jsx("dd", { children: a || /* @__PURE__ */ s.jsx("span", { className: "wf-muted", children: "No description" }) }),
        /* @__PURE__ */ s.jsx("dt", { children: "Definition ID" }),
        /* @__PURE__ */ s.jsx("dd", { children: /* @__PURE__ */ s.jsx("code", { children: t.definitionId }) })
      ] }),
      /* @__PURE__ */ s.jsx("p", { className: "wf-muted wf-properties-hint", children: "Workflow metadata is read-only here for now. Editing variables, inputs and outputs is coming soon." })
    ] }),
    /* @__PURE__ */ s.jsx(ii, { title: "Variables", emptyLabel: "No variables defined.", items: n }),
    /* @__PURE__ */ s.jsx(ii, { title: "Inputs", emptyLabel: "No inputs defined.", items: o }),
    /* @__PURE__ */ s.jsx(ii, { title: "Outputs", emptyLabel: "No outputs defined.", items: r }),
    /* @__PURE__ */ s.jsxs("section", { className: "wf-properties-section", children: [
      /* @__PURE__ */ s.jsx("h3", { children: "Versions" }),
      i.length === 0 ? /* @__PURE__ */ s.jsx("p", { className: "wf-muted", children: "No published versions yet." }) : /* @__PURE__ */ s.jsx("ul", { className: "wf-properties-versions", children: i.map((c) => /* @__PURE__ */ s.jsxs("li", { children: [
        /* @__PURE__ */ s.jsxs("span", { className: "wf-properties-version-tag", children: [
          "v",
          c.version
        ] }),
        /* @__PURE__ */ s.jsx("time", { children: et(c.createdAt) })
      ] }, c.id)) })
    ] })
  ] });
}
function m0({ activities: e, activityCatalog: t, selectedEvidenceId: n = null, onSelectEvidence: o }) {
  const r = pe(
    () => new Map(t.map((a) => [a.activityTypeKey, a])),
    [t]
  ), i = pe(() => w0(e), [e]);
  return i.length === 0 ? /* @__PURE__ */ s.jsx("div", { className: "wf-empty wf-timeline-empty", children: "No activity executions recorded yet." }) : /* @__PURE__ */ s.jsx("ol", { className: "wf-timeline", "aria-label": "Execution timeline", children: i.map((a) => {
    const c = r.get(a.activityType), u = or(c), l = c ? je(c) : ir(a.activityType) ?? a.activityType, d = ir(a.activityType) ?? a.activityType, f = v0(a.startedAt ?? a.scheduledAt), h = ju(a.startedAt, a.completedAt);
    return /* @__PURE__ */ s.jsx("li", { children: /* @__PURE__ */ s.jsxs(
      "button",
      {
        type: "button",
        className: "wf-timeline-entry",
        "data-selected": a.activityExecutionId === n,
        onClick: () => o?.(a.activityExecutionId),
        children: [
          /* @__PURE__ */ s.jsx("span", { className: "wf-timeline-icon wf-activity-icon", "data-icon": u, "aria-hidden": "true", children: rs(u) }),
          /* @__PURE__ */ s.jsxs("span", { className: "wf-timeline-body", children: [
            /* @__PURE__ */ s.jsx("strong", { title: l, children: l }),
            /* @__PURE__ */ s.jsx("small", { title: d, children: d })
          ] }),
          /* @__PURE__ */ s.jsxs("span", { className: "wf-timeline-meta", children: [
            f ? /* @__PURE__ */ s.jsx("time", { children: f }) : null,
            h ? /* @__PURE__ */ s.jsxs("small", { children: [
              "took ",
              h
            ] }) : null
          ] }),
          /* @__PURE__ */ s.jsx(x0, { status: a.status })
        ]
      }
    ) }, a.activityExecutionId);
  }) });
}
function x0({ status: e }) {
  const t = e.toLowerCase(), n = t === "completed" ? "OK" : e;
  return /* @__PURE__ */ s.jsx("span", { className: "wf-status-badge wf-timeline-pill", "data-status": t, children: n });
}
function w0(e) {
  return e.map((t, n) => ({ activity: t, index: n })).sort((t, n) => Ha(t.activity) - Ha(n.activity) || t.index - n.index).map((t) => t.activity);
}
function Ha(e) {
  const t = e.startedAt ?? e.scheduledAt, n = t ? Date.parse(t) : Number.NaN;
  return Number.isNaN(n) ? Number.MAX_SAFE_INTEGER : n;
}
function v0(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? "" : t.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: !1 });
}
const Fa = "elsa-studio:apply-workflow-graph-operation-batch", Wa = "elsa-studio:undo-workflow-graph-operation-batch", b0 = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function N0(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const o = A0(e), r = ku(o.state.rootActivity), i = /* @__PURE__ */ new Map(), a = /* @__PURE__ */ new Map(), c = [];
  for (const u of t.operations) {
    const l = _0(u.kind), d = u.parameters ?? {};
    if (l === "add-activity") {
      const f = Te(d.activityId) ?? u.temporaryReferences?.[0], h = I0(f ?? Te(d.displayName) ?? Te(d.activityType) ?? "weaver-activity", r), p = S0(u, h, n);
      a.set(h, p), c.push(h), f && i.set(f, h), o.state.rootActivity && j0(o.state.rootActivity, p);
      const g = Rt(d.position) ? Ci(d.position, { x: 280, y: 160 }) : null;
      g && (o.layout = Ba(o.layout, h, g));
      continue;
    }
    if (l === "set-root") {
      const f = si(o, d.activityId, i, a);
      if (!f) throw new Error("Weaver batch referenced an unknown root activity.");
      o.state.rootActivity = f;
      continue;
    }
    if (l === "set-designer-position") {
      const f = Ft(d.activityId, i);
      if (!f || !is(o.state.rootActivity, f)) throw new Error("Weaver batch referenced an unknown activity position.");
      o.layout = Ba(o.layout, f, Ci(d, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const f = si(o, d.activityId, i, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity property target.");
      C0(f, Te(d.propertyName) ?? "Value", d.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const f = si(o, d.activityId, i, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity update target.");
      const h = Rt(d.patch) ? d.patch : d;
      Object.assign(f, h);
      continue;
    }
    if (l === "remove-activity") {
      const f = Ft(d.activityId, i);
      if (!f) throw new Error("Weaver batch referenced an unknown activity remove target.");
      o.state.rootActivity = Eu(o.state.rootActivity, f), o.layout = o.layout.filter((h) => h.nodeId !== f);
      continue;
    }
    if (l === "connect-activities") {
      E0(o, d, i);
      continue;
    }
    if (l === "disconnect-activities") {
      k0(o, d, i);
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
function S0(e, t, n) {
  const o = e.parameters ?? {}, r = Te(o.activityVersionId) ?? Te(o.activityType) ?? "Elsa.Workflows.Activity", i = n.find((a) => a.activityVersionId === r || a.activityTypeKey === r || a.displayName === Te(o.displayName));
  return i ? ji(i, t) : {
    nodeId: t,
    activityVersionId: i?.activityVersionId ?? r,
    inputs: [],
    outputs: [],
    ...Te(o.displayName) ? { displayName: Te(o.displayName) } : {},
    designer: { position: Ci(o.position, { x: 280, y: 160 }) }
  };
}
function j0(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = ss(e);
  n && !n.some((o) => o.nodeId === t.nodeId) && n.push(t);
}
function E0(e, t, n) {
  const o = e.state.rootActivity;
  if (!o?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const r = Ft(t.sourceActivityId ?? t.sourceId ?? t.from, n), i = Ft(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!r || !i) throw new Error("Weaver batch connection is missing source or target activity.");
  const a = o.structure.payload, c = Array.isArray(a.connections) ? a.connections : [], u = Te(t.connectionId) ?? `flow-${r}-${i}`;
  a.connections = [
    ...c.filter((l) => !Rt(l) || l.id !== u),
    {
      id: u,
      source: { nodeId: r, port: Te(t.outcome) ?? Te(t.sourcePort) ?? "Done" },
      target: { nodeId: i }
    }
  ];
}
function k0(e, t, n) {
  const o = e.state.rootActivity, r = o?.structure?.payload.connections;
  if (!Array.isArray(r)) return;
  const i = Te(t.connectionId), a = Ft(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = Ft(t.targetActivityId ?? t.targetId ?? t.to, n);
  o.structure.payload.connections = r.filter((u) => {
    if (!Rt(u)) return !0;
    if (i && u.id === i) return !1;
    const l = Rt(u.source) ? u.source.nodeId : void 0, d = Rt(u.target) ? u.target.nodeId : void 0;
    return l !== a || d !== c;
  });
}
function C0(e, t, n) {
  e[mu(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: "Literal", value: n }
  };
}
function si(e, t, n, o) {
  const r = Ft(t, n);
  return r ? is(e.state.rootActivity, r) ?? o.get(r) ?? null : null;
}
function Ft(e, t) {
  const n = Te(e);
  return n ? t.get(n) ?? n : null;
}
function is(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of Cu(e)) {
    const o = is(n, t);
    if (o) return o;
  }
  return null;
}
function Eu(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = ss(e);
  if (n) {
    const o = n.map((r) => Eu(r, t)).filter((r) => !!r);
    n.splice(0, n.length, ...o);
  }
  return e;
}
function ku(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of Cu(e)) ku(n, t);
  return t;
}
function Cu(e) {
  return ss(e) ?? [];
}
function ss(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function Ba(e, t, n) {
  return [
    ...e.filter((o) => o.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function Ci(e, t) {
  const n = Rt(e) ? e : {}, o = Number(n.x), r = Number(n.y);
  return {
    x: Number.isFinite(o) ? Math.max(40, Math.round(o)) : t.x,
    y: Number.isFinite(r) ? Math.max(40, Math.round(r)) : t.y
  };
}
function I0(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let o = n, r = 2;
  for (; t.has(o); )
    o = `${n}-${r}`, r += 1;
  return t.add(o), o;
}
function _0(e) {
  return typeof e == "number" ? b0[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Te(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function A0(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function Rt(e) {
  return typeof e == "object" && e !== null;
}
const Iu = { workflowActivity: mv }, _u = { workflow: wv }, Xa = "application/x-elsa-activity-version-id", M0 = 6, D0 = 1200, P0 = 250, T0 = [10, 25, 50], $0 = 10, Ka = "elsa-studio-workflow-palette-width", qa = "elsa-studio-workflow-inspector-width", Ya = "elsa-studio-workflow-palette-collapsed", Ua = "elsa-studio-workflow-inspector-collapsed", Au = "elsa-studio-workflow-side-panel-maximized", Dn = 180, Pn = 460, R0 = 260, Tn = 260, $n = 560, z0 = 320, Za = 42, Po = 16, Mu = jt.createContext(null);
let Ii;
function Tv(e) {
  Ii = e.dialogs, e.featureAreas.add({
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
        component: () => /* @__PURE__ */ s.jsx(L0, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list() })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ s.jsx(V0, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => /* @__PURE__ */ s.jsx(O0, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => /* @__PURE__ */ s.jsx(H0, { context: e.backend, ai: e.ai })
      }
    ]
  });
}
function L0({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: o,
  workflowDesignerPanels: r
}) {
  const [i, a] = K(Ga);
  oe(() => {
    const u = () => a(Ga());
    return window.addEventListener("popstate", u), () => window.removeEventListener("popstate", u);
  }, []);
  const c = (u) => {
    const l = u ? `/workflows/definitions?definition=${encodeURIComponent(u)}` : "/workflows/definitions";
    window.history.pushState({}, "", l), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return i ? /* @__PURE__ */ s.jsx(yv, { context: e, definitionId: i, ai: t, propertyEditors: n, expressionEditors: o, workflowDesignerPanels: r, onBack: () => c(null) }) : /* @__PURE__ */ s.jsx(Nr, { title: "Definitions", children: /* @__PURE__ */ s.jsx(W0, { context: e, ai: t, onOpen: c }) });
}
function V0({ context: e, ai: t }) {
  const [n, o] = K(Ja);
  oe(() => {
    const i = () => o(Ja());
    return window.addEventListener("popstate", i), () => window.removeEventListener("popstate", i);
  }, []);
  const r = de((i) => {
    const a = i?.trim() ?? "", c = new URL(window.location.href);
    a ? c.searchParams.set("definition", a) : c.searchParams.delete("definition"), o(a || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ s.jsx(Nr, { title: "Executables", children: /* @__PURE__ */ s.jsx(X0, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: r }) });
}
function O0({ context: e, ai: t }) {
  return /* @__PURE__ */ s.jsx(Nr, { title: "Runs", children: /* @__PURE__ */ s.jsx(Y0, { context: e, ai: t }) });
}
function H0({ context: e, ai: t }) {
  const n = F0();
  return /* @__PURE__ */ s.jsx(Nr, { title: "Run", children: /* @__PURE__ */ s.jsx(U0, { context: e, ai: t, workflowExecutionId: n }) });
}
function Nr({ title: e, children: t }) {
  return /* @__PURE__ */ s.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ s.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ s.jsxs("div", { children: [
      /* @__PURE__ */ s.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ s.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function Ga() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Ja() {
  return new URLSearchParams(window.location.search).get("definition");
}
function F0() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function W0({ context: e, ai: t, onOpen: n }) {
  const [o, r] = K(""), [i, a] = K("active"), [c, u] = K(1), [l, d] = K($0), [f, h] = K("loading"), [p, g] = K(""), [b, w] = K(""), [m, E] = K([]), [y, v] = K(0), [N, k] = K(() => /* @__PURE__ */ new Set()), [S, _] = K(null), [P, X] = K(!1), [M, $] = K([]), [H, j] = K("idle"), A = se(null), I = pe(() => m.map((O) => O.id), [m]), D = Wt(t, "weaver.workflows.suggest-create-metadata"), T = Wt(t, "weaver.workflows.explain-definition"), R = I.filter((O) => N.has(O)).length, V = I.length > 0 && R === I.length, B = de(async () => {
    h("loading"), g("");
    try {
      const O = await qx(e, { search: o, state: i, page: c, pageSize: l }), G = typeof O.totalCount == "number", he = O.totalCount ?? O.definitions.length, ve = Tu(he, l);
      if (he > 0 && c > ve) {
        u(ve);
        return;
      }
      E(G ? O.definitions : ov(O.definitions, c, l)), v(he), h("ready");
    } catch (O) {
      g(O instanceof Error ? O.message : String(O)), h("failed");
    }
  }, [e, o, i, c, l]);
  oe(() => {
    B();
  }, [B]), oe(() => {
    A.current && (A.current.indeterminate = R > 0 && !V);
  }, [V, R]);
  const F = de(async () => {
    if (!(H === "loading" || H === "ready")) {
      j("loading");
      try {
        const O = await es(e);
        $(O.activities ?? []), j("ready");
      } catch (O) {
        j("failed"), g(O instanceof Error ? O.message : String(O));
      }
    }
  }, [H, e]), Z = () => {
    g(""), w(""), _({ name: "", description: "", rootKind: "flowchart" }), F();
  }, Y = async () => {
    if (S?.name.trim()) {
      X(!0), g(""), w("");
      try {
        const O = await Zx(e, {
          name: S.name.trim(),
          description: S.description.trim() || null,
          rootKind: S.rootKind,
          rootActivityVersionId: sv(S, M)
        });
        _(null), n(O.definition.id);
      } catch (O) {
        g(O instanceof Error ? O.message : String(O));
      } finally {
        X(!1);
      }
    }
  }, te = (O) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(O)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, ce = async () => {
    if (m.length === 1 && c > 1) {
      u(c - 1);
      return;
    }
    await B();
  }, J = () => k(/* @__PURE__ */ new Set()), L = (O, G) => {
    k((he) => {
      const ve = new Set(he);
      return G ? ve.add(O) : ve.delete(O), ve;
    });
  }, U = (O) => {
    k((G) => {
      const he = new Set(G);
      for (const ve of I)
        O ? he.add(ve) : he.delete(ve);
      return he;
    });
  }, ae = (O) => {
    a(O), u(1), J();
  }, re = (O) => {
    r(O), u(1), J();
  }, Q = async (O) => {
    if (await Ii.confirm({ message: `Delete workflow definition "${O.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" })) {
      w(""), g("");
      try {
        await Gx(e, O.id), L(O.id, !1), w(`Deleted ${O.name}`), await ce();
      } catch (G) {
        g(G instanceof Error ? G.message : String(G));
      }
    }
  }, ee = async (O) => {
    w(""), g("");
    try {
      await Jx(e, O.id), L(O.id, !1), w(`Restored ${O.name}`), await ce();
    } catch (G) {
      g(G instanceof Error ? G.message : String(G));
    }
  }, le = async (O) => {
    if (await Ii.confirm({ message: `Permanently delete workflow definition "${O.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) {
      w(""), g("");
      try {
        await Qx(e, O.id), L(O.id, !1), w(`Permanently deleted ${O.name}`), await ce();
      } catch (G) {
        g(G instanceof Error ? G.message : String(G));
      }
    }
  };
  return /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
    /* @__PURE__ */ s.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ s.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ s.jsx("button", { type: "button", className: i === "active" ? "active" : "", "aria-selected": i === "active", onClick: () => ae("active"), children: "Active" }),
        /* @__PURE__ */ s.jsx("button", { type: "button", className: i === "deleted" ? "active" : "", "aria-selected": i === "deleted", onClick: () => ae("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ s.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ s.jsx(Qi, { size: 15 }),
        /* @__PURE__ */ s.jsx("input", { value: o, onChange: (O) => re(O.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ s.jsx("button", { type: "button", onClick: () => {
        B();
      }, children: "Refresh" }),
      /* @__PURE__ */ s.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ s.jsxs("button", { type: "button", title: "Create workflow", onClick: Z, children: [
        /* @__PURE__ */ s.jsx(Gi, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ s.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ s.jsx(qe, { size: 16 }),
      " ",
      p
    ] }) : null,
    f !== "failed" && p ? /* @__PURE__ */ s.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ s.jsx(qe, { size: 16 }),
      " ",
      p
    ] }) : null,
    b ? /* @__PURE__ */ s.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ s.jsx(yn, { size: 14 }),
      " ",
      b
    ] }) : null,
    N.size > 0 ? /* @__PURE__ */ s.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ s.jsxs("span", { children: [
        N.size,
        " selected"
      ] }),
      /* @__PURE__ */ s.jsx("button", { type: "button", onClick: J, children: "Clear selection" })
    ] }) : null,
    f === "loading" ? /* @__PURE__ */ s.jsx("div", { className: "wf-empty", children: "Loading workflow definitions..." }) : null,
    f === "ready" && m.length === 0 ? /* @__PURE__ */ s.jsxs("div", { className: "wf-empty", children: [
      "No ",
      i,
      " workflow definitions found."
    ] }) : null,
    f === "ready" && m.length > 0 ? /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
      /* @__PURE__ */ s.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ s.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ s.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ s.jsx(
            "input",
            {
              ref: A,
              type: "checkbox",
              checked: V,
              onChange: (O) => U(O.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ s.jsx("span", { children: "Name" }),
          /* @__PURE__ */ s.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ s.jsx("span", { children: i === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ s.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ s.jsx("span", { children: "Actions" })
        ] }),
        m.map((O) => /* @__PURE__ */ s.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${O.name}`,
            "aria-selected": N.has(O.id),
            tabIndex: 0,
            onClick: () => n(O.id),
            onKeyDown: (G) => {
              G.currentTarget === G.target && (G.key !== "Enter" && G.key !== " " || (G.preventDefault(), n(O.id)));
            },
            children: [
              /* @__PURE__ */ s.jsx("label", { className: "wf-row-select", onClick: (G) => G.stopPropagation(), children: /* @__PURE__ */ s.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: N.has(O.id),
                  onChange: (G) => L(O.id, G.target.checked),
                  "aria-label": `Select workflow definition ${O.name}`
                }
              ) }),
              /* @__PURE__ */ s.jsxs("span", { children: [
                /* @__PURE__ */ s.jsx("strong", { children: O.name }),
                /* @__PURE__ */ s.jsx("small", { children: O.description || O.id })
              ] }),
              /* @__PURE__ */ s.jsx("span", { children: O.latestVersion ?? "No version" }),
              /* @__PURE__ */ s.jsx("span", { children: i === "deleted" ? et(O.deletedAt) : O.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ s.jsx("span", { children: et(O.lastModifiedAt) }),
              /* @__PURE__ */ s.jsx("span", { className: "wf-row-actions", onClick: (G) => G.stopPropagation(), children: i === "active" ? /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
                /* @__PURE__ */ s.jsx("button", { type: "button", onClick: (G) => {
                  G.stopPropagation(), n(O.id);
                }, children: "Open" }),
                /* @__PURE__ */ s.jsx("button", { type: "button", onClick: (G) => {
                  G.stopPropagation(), te(O.id);
                }, children: "Artifacts" }),
                T ? /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => St(t, T, O), children: [
                  /* @__PURE__ */ s.jsx(Nt, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ s.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  Q(O);
                }, children: [
                  /* @__PURE__ */ s.jsx(bi, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
                /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => {
                  ee(O);
                }, children: [
                  /* @__PURE__ */ s.jsx(Ji, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ s.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  le(O);
                }, children: [
                  /* @__PURE__ */ s.jsx(bi, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          O.id
        ))
      ] }),
      /* @__PURE__ */ s.jsx(
        nv,
        {
          page: c,
          pageSize: l,
          totalCount: y,
          onPageChange: u,
          onPageSizeChange: (O) => {
            d(O), u(1);
          }
        }
      )
    ] }) : null,
    S ? /* @__PURE__ */ s.jsx(
      B0,
      {
        draft: S,
        activities: M,
        catalogState: H,
        creating: P,
        suggestMetadataAction: D,
        onSuggestMetadata: D ? () => St(t, D, { draft: S, activities: M }) : void 0,
        onChange: (O) => _(O),
        onClose: () => _(null),
        onSubmit: Y
      }
    ) : null
  ] });
}
function B0({ draft: e, activities: t, catalogState: n, creating: o, suggestMetadataAction: r, onSuggestMetadata: i, onChange: a, onClose: c, onSubmit: u }) {
  const l = pe(() => rv(t), [t]), d = iv(e, t), f = (h) => {
    if (h.startsWith("kind:")) {
      a({ ...e, rootKind: h.slice(5), rootActivityVersionId: null });
      return;
    }
    const p = t.find((g) => g.activityVersionId === h);
    a({
      ...e,
      rootKind: $u(p) ?? e.rootKind,
      rootActivityVersionId: h
    });
  };
  return /* @__PURE__ */ s.jsx("div", { className: "wf-dialog-backdrop", role: "presentation", children: /* @__PURE__ */ s.jsx("section", { className: "wf-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "workflow-create-title", children: /* @__PURE__ */ s.jsxs(
    "form",
    {
      onSubmit: (h) => {
        h.preventDefault(), u();
      },
      children: [
        /* @__PURE__ */ s.jsxs("div", { className: "wf-dialog-heading", children: [
          /* @__PURE__ */ s.jsx("h3", { id: "workflow-create-title", children: "Create Workflow" }),
          r ? /* @__PURE__ */ s.jsxs("button", { type: "button", className: "wf-ai-action", onClick: i, title: r.description ?? r.label, children: [
            /* @__PURE__ */ s.jsx(Nt, { size: 13 }),
            " ",
            r.label
          ] }) : null
        ] }),
        /* @__PURE__ */ s.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ s.jsx("span", { children: "Display name" }),
          /* @__PURE__ */ s.jsx(
            "input",
            {
              autoFocus: !0,
              "aria-label": "Display name",
              value: e.name,
              onChange: (h) => a({ ...e, name: h.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ s.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ s.jsx("span", { children: "Description" }),
          /* @__PURE__ */ s.jsx(
            "textarea",
            {
              "aria-label": "Description",
              rows: 3,
              value: e.description,
              onChange: (h) => a({ ...e, description: h.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ s.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ s.jsx("span", { children: "Root activity" }),
          /* @__PURE__ */ s.jsxs(
            "select",
            {
              "aria-label": "Root activity",
              value: d,
              onChange: (h) => f(h.target.value),
              disabled: n === "loading",
              children: [
                /* @__PURE__ */ s.jsx("optgroup", { label: "Composite roots", children: l.compositeRoots.map((h) => /* @__PURE__ */ s.jsx("option", { value: h.value, children: h.label }, h.value)) }),
                l.otherCategories.map((h) => /* @__PURE__ */ s.jsx("optgroup", { label: h.name, children: h.activities.map((p) => /* @__PURE__ */ s.jsx("option", { value: p.activityVersionId, children: je(p) }, p.activityVersionId)) }, h.name))
              ]
            }
          )
        ] }),
        n === "loading" ? /* @__PURE__ */ s.jsx("div", { className: "wf-dialog-note", children: "Loading activity catalog..." }) : null,
        n === "failed" ? /* @__PURE__ */ s.jsx("div", { className: "wf-dialog-note", children: "Activity catalog could not be loaded. Composite roots remain available." }) : null,
        /* @__PURE__ */ s.jsxs("div", { className: "wf-dialog-actions", children: [
          /* @__PURE__ */ s.jsx("button", { type: "button", onClick: c, disabled: o, children: "Cancel" }),
          /* @__PURE__ */ s.jsx("button", { type: "submit", disabled: o || !e.name.trim(), children: o ? "Creating..." : "Create" })
        ] })
      ]
    }
  ) }) });
}
function X0({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: o }) {
  const [r, i] = K("loading"), [a, c] = K(""), [u, l] = K(""), [d, f] = K(null), [h, p] = K([]), g = n?.trim().toLowerCase() ?? "", b = pe(
    () => g ? h.filter((S) => cv(S, g)) : h,
    [g, h]
  ), w = pe(
    () => Array.from(new Set(h.flatMap((S) => [
      S.definitionId,
      S.definitionVersionId,
      S.sourceId
    ]).filter((S) => !!S))).sort((S, _) => S.localeCompare(_)),
    [h]
  ), m = Wt(t, "weaver.workflows.explain-executable"), E = de(async () => {
    i("loading"), c("");
    try {
      p(await lu(e)), i("ready");
    } catch (S) {
      c(S instanceof Error ? S.message : String(S)), i("failed");
    }
  }, [e]);
  oe(() => {
    E();
  }, [E]);
  const y = async (S) => {
    l(""), f(null), c("");
    try {
      const _ = await cu(e, S.artifactId), P = Hu(_);
      f({ artifactId: S.artifactId, workflowExecutionId: P }), l(`Started ${S.artifactId}`);
    } catch (_) {
      c(_ instanceof Error ? _.message : String(_));
    }
  }, v = (S) => {
    m && St(t, m, S) && (c(""), f(null), l(`Sent ${S.artifactId} to Weaver`));
  }, N = (S) => {
    c(""), f(null), l(`Copied ${S}`);
  }, k = (S) => {
    l(""), f(null), c(`Could not copy ${S}.`);
  };
  return /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
    /* @__PURE__ */ s.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ s.jsx("button", { type: "button", onClick: () => {
        E();
      }, children: "Refresh" }),
      /* @__PURE__ */ s.jsxs("label", { className: "wf-search wf-executable-definition-filter", children: [
        /* @__PURE__ */ s.jsx(Qi, { size: 14 }),
        /* @__PURE__ */ s.jsx(
          "input",
          {
            "aria-label": "Filter executables by workflow definition",
            list: "wf-executable-definition-options",
            placeholder: "Filter by definition ID",
            value: n ?? "",
            onChange: (S) => o(S.currentTarget.value || null)
          }
        )
      ] }),
      /* @__PURE__ */ s.jsx("datalist", { id: "wf-executable-definition-options", children: w.map((S) => /* @__PURE__ */ s.jsx("option", { value: S }, S)) }),
      n ? /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => o(null), children: [
        /* @__PURE__ */ s.jsx(iu, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    r === "failed" ? /* @__PURE__ */ s.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ s.jsx(qe, { size: 16 }),
      " ",
      a
    ] }) : null,
    u ? /* @__PURE__ */ s.jsx(Du, { status: u, run: d }) : null,
    r === "loading" ? /* @__PURE__ */ s.jsx("div", { className: "wf-empty", children: "Loading workflow executables..." }) : null,
    r === "ready" && b.length === 0 ? /* @__PURE__ */ s.jsx("div", { className: "wf-empty", children: n ? "No workflow executables match this definition filter." : "No workflow executables found. Publish a workflow definition to create one." }) : null,
    r === "ready" && b.length > 0 ? /* @__PURE__ */ s.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ s.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ s.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ s.jsx("span", { children: "Version" }),
        /* @__PURE__ */ s.jsx("span", { children: "Source" }),
        /* @__PURE__ */ s.jsx("span", { children: "Root" }),
        /* @__PURE__ */ s.jsx("span", { children: "Published" }),
        /* @__PURE__ */ s.jsx("span", { children: "Actions" })
      ] }),
      b.map((S) => /* @__PURE__ */ s.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ s.jsxs("span", { className: "wf-artifact-cell", children: [
          /* @__PURE__ */ s.jsxs("span", { className: "wf-cell-line", children: [
            /* @__PURE__ */ s.jsx("strong", { title: S.artifactId, children: S.artifactId }),
            /* @__PURE__ */ s.jsx(rn, { value: S.artifactId, ariaLabel: `Copy artifact ID ${S.artifactId}`, copiedLabel: "artifact ID", onCopied: N, onCopyFailed: k })
          ] }),
          /* @__PURE__ */ s.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
            /* @__PURE__ */ s.jsx("small", { title: S.artifactHash, children: S.artifactHash }),
            /* @__PURE__ */ s.jsx(rn, { value: S.artifactHash, ariaLabel: `Copy artifact hash ${S.artifactHash}`, copiedLabel: "artifact hash", onCopied: N, onCopyFailed: k })
          ] })
        ] }),
        /* @__PURE__ */ s.jsxs("span", { className: "wf-cell-line wf-version-cell", children: [
          /* @__PURE__ */ s.jsx("span", { children: S.artifactVersion }),
          /* @__PURE__ */ s.jsx(rn, { value: S.artifactVersion, ariaLabel: `Copy artifact version ${S.artifactVersion}`, copiedLabel: "artifact version", onCopied: N, onCopyFailed: k })
        ] }),
        /* @__PURE__ */ s.jsx(K0, { executable: S, onCopied: N, onCopyFailed: k }),
        /* @__PURE__ */ s.jsx("span", { children: Vu(S) }),
        /* @__PURE__ */ s.jsx("span", { children: et(S.publishedAt ?? S.createdAt) }),
        /* @__PURE__ */ s.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => {
            y(S);
          }, children: [
            /* @__PURE__ */ s.jsx(Qn, { size: 13 }),
            " Run"
          ] }),
          m ? /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => v(S), children: [
            /* @__PURE__ */ s.jsx(Nt, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, S.artifactId))
    ] }) : null
  ] });
}
function K0({ executable: e, onCopied: t, onCopyFailed: n }) {
  const o = e.sourceId || e.definitionVersionId || e.definitionId, r = e.sourceVersion;
  return /* @__PURE__ */ s.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ s.jsx("span", { className: "wf-source-kind", children: Ou(e.sourceKind) }),
    o ? /* @__PURE__ */ s.jsxs("span", { className: "wf-cell-line", children: [
      /* @__PURE__ */ s.jsx("code", { title: o, children: o }),
      /* @__PURE__ */ s.jsx(rn, { value: o, ariaLabel: `Copy source ID ${o}`, copiedLabel: "source ID", onCopied: t, onCopyFailed: n })
    ] }) : null,
    r ? /* @__PURE__ */ s.jsxs("small", { children: [
      "Version ",
      r
    ] }) : null
  ] });
}
function Du({ status: e, run: t, compact: n = !1 }) {
  const o = () => {
    t?.workflowExecutionId && (window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(t.workflowExecutionId)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  };
  return /* @__PURE__ */ s.jsxs("div", { className: `wf-status-line${n ? " compact" : ""}`, children: [
    /* @__PURE__ */ s.jsx(yn, { size: n ? 13 : 14 }),
    /* @__PURE__ */ s.jsx("span", { children: e }),
    t?.workflowExecutionId ? /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: o, children: [
      "Open Run ",
      t.workflowExecutionId
    ] }) : null
  ] });
}
function rn({ value: e, ariaLabel: t, copiedLabel: n, onCopied: o, onCopyFailed: r }) {
  if (!e) return null;
  const i = async (a) => {
    a.preventDefault(), a.stopPropagation();
    try {
      await dv(e), o(n);
    } catch {
      r(n);
    }
  };
  return /* @__PURE__ */ s.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (a) => {
    i(a);
  }, children: /* @__PURE__ */ s.jsx(Cx, { size: 12 }) });
}
function q0({ context: e, ai: t, definitionId: n, publishedArtifactId: o }) {
  const [r, i] = K("loading"), [a, c] = K(""), [u, l] = K(""), [d, f] = K(null), [h, p] = K([]), g = Wt(t, "weaver.workflows.explain-executable"), b = de(async () => {
    i("loading"), c("");
    try {
      const N = await lu(e);
      p(N.filter((k) => lv(k, n)).sort(uv)), i("ready");
    } catch (N) {
      c(N instanceof Error ? N.message : String(N)), p([]), i("failed");
    }
  }, [e, n]);
  oe(() => {
    b();
  }, [b, o]);
  const w = async (N) => {
    l(""), f(null), c("");
    try {
      const k = await cu(e, N.artifactId);
      f({ artifactId: N.artifactId, workflowExecutionId: Hu(k) }), l(`Started ${N.artifactId}`);
    } catch (k) {
      c(k instanceof Error ? k.message : String(k));
    }
  }, m = (N) => {
    g && St(t, g, N) && (c(""), f(null), l(`Sent ${N.artifactId} to Weaver`));
  }, E = () => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(n)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, y = (N) => {
    c(""), f(null), l(`Copied ${N}`);
  }, v = (N) => {
    l(""), f(null), c(`Could not copy ${N}.`);
  };
  return /* @__PURE__ */ s.jsxs("div", { className: "wf-artifacts-panel", children: [
    /* @__PURE__ */ s.jsxs("div", { className: "wf-artifacts-toolbar", children: [
      /* @__PURE__ */ s.jsxs("span", { children: [
        h.length,
        " artifact",
        h.length === 1 ? "" : "s"
      ] }),
      /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => {
        b();
      }, children: [
        /* @__PURE__ */ s.jsx(Ji, { size: 13 }),
        " Refresh"
      ] }),
      /* @__PURE__ */ s.jsx("button", { type: "button", onClick: E, children: "Open list" })
    ] }),
    r === "failed" ? /* @__PURE__ */ s.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ s.jsx(qe, { size: 14 }),
      " ",
      a
    ] }) : null,
    u ? /* @__PURE__ */ s.jsx(Du, { status: u, run: d, compact: !0 }) : null,
    r === "loading" ? /* @__PURE__ */ s.jsx("p", { className: "wf-muted", children: "Loading artifacts..." }) : null,
    r === "ready" && h.length === 0 ? /* @__PURE__ */ s.jsx("p", { className: "wf-muted", children: "No published artifacts for this workflow yet." }) : null,
    r === "ready" && h.length > 0 ? /* @__PURE__ */ s.jsx("div", { className: "wf-artifact-list", role: "list", "aria-label": "Workflow artifacts", children: h.map((N) => /* @__PURE__ */ s.jsxs("article", { className: "wf-artifact-card", role: "listitem", "data-active": N.artifactId === o ? "true" : void 0, children: [
      /* @__PURE__ */ s.jsxs("div", { className: "wf-artifact-card-heading", children: [
        /* @__PURE__ */ s.jsxs("div", { children: [
          /* @__PURE__ */ s.jsxs("span", { className: "wf-artifact-version", children: [
            "Version ",
            N.artifactVersion
          ] }),
          N.artifactId === o ? /* @__PURE__ */ s.jsx("span", { className: "wf-chip", children: "Latest publish" }) : null
        ] }),
        /* @__PURE__ */ s.jsx("span", { children: et(N.publishedAt ?? N.createdAt) })
      ] }),
      /* @__PURE__ */ s.jsxs("div", { className: "wf-artifact-card-values", children: [
        /* @__PURE__ */ s.jsxs("span", { className: "wf-cell-line", children: [
          /* @__PURE__ */ s.jsx("code", { title: N.artifactId, children: N.artifactId }),
          /* @__PURE__ */ s.jsx(rn, { value: N.artifactId, ariaLabel: `Copy artifact ID ${N.artifactId}`, copiedLabel: "artifact ID", onCopied: y, onCopyFailed: v })
        ] }),
        /* @__PURE__ */ s.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ s.jsx("code", { title: N.artifactHash, children: N.artifactHash }),
          /* @__PURE__ */ s.jsx(rn, { value: N.artifactHash, ariaLabel: `Copy artifact hash ${N.artifactHash}`, copiedLabel: "artifact hash", onCopied: y, onCopyFailed: v })
        ] })
      ] }),
      /* @__PURE__ */ s.jsxs("dl", { children: [
        /* @__PURE__ */ s.jsxs("div", { children: [
          /* @__PURE__ */ s.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ s.jsxs("dd", { children: [
            Ou(N.sourceKind),
            " ",
            N.sourceVersion ? `v${N.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ s.jsxs("div", { children: [
          /* @__PURE__ */ s.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ s.jsx("dd", { children: Vu(N) })
        ] })
      ] }),
      /* @__PURE__ */ s.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => {
          w(N);
        }, children: [
          /* @__PURE__ */ s.jsx(Qn, { size: 13 }),
          " Run"
        ] }),
        g ? /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => m(N), children: [
          /* @__PURE__ */ s.jsx(Nt, { size: 13 }),
          " Explain"
        ] }) : null
      ] })
    ] }, N.artifactId)) }) : null
  ] });
}
function Y0({ context: e }) {
  const [t, n] = K("loading"), [o, r] = K(""), [i, a] = K(""), [c, u] = K(""), [l, d] = K([]), f = de(async () => {
    n("loading"), r("");
    try {
      const p = await rw(e, {
        status: i || void 0,
        runKind: c || void 0,
        take: 100
      });
      d(p), n("ready");
    } catch (p) {
      r(p instanceof Error ? p.message : String(p)), d([]), n("failed");
    }
  }, [e, c, i]);
  oe(() => {
    f();
  }, [f]);
  const h = (p) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(p)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
    /* @__PURE__ */ s.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ s.jsx("button", { type: "button", onClick: () => {
        f();
      }, children: "Refresh" }),
      /* @__PURE__ */ s.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ s.jsx("span", { children: "Status" }),
        /* @__PURE__ */ s.jsxs("select", { "aria-label": "Workflow run status", value: i, onChange: (p) => a(p.target.value), children: [
          /* @__PURE__ */ s.jsx("option", { value: "", children: "All statuses" }),
          /* @__PURE__ */ s.jsx("option", { value: "Pending", children: "Pending" }),
          /* @__PURE__ */ s.jsx("option", { value: "Running", children: "Running" }),
          /* @__PURE__ */ s.jsx("option", { value: "Suspended", children: "Suspended" }),
          /* @__PURE__ */ s.jsx("option", { value: "Completed", children: "Completed" }),
          /* @__PURE__ */ s.jsx("option", { value: "Faulted", children: "Faulted" }),
          /* @__PURE__ */ s.jsx("option", { value: "Cancelled", children: "Cancelled" })
        ] })
      ] }),
      /* @__PURE__ */ s.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ s.jsx("span", { children: "Kind" }),
        /* @__PURE__ */ s.jsxs("select", { "aria-label": "Run Kind", value: c, onChange: (p) => u(p.target.value), children: [
          /* @__PURE__ */ s.jsx("option", { value: "", children: "All kinds" }),
          /* @__PURE__ */ s.jsx("option", { value: "TestRun", children: "Test Run" }),
          /* @__PURE__ */ s.jsx("option", { value: "PublishedRun", children: "Published Run" }),
          /* @__PURE__ */ s.jsx("option", { value: "BackgroundWeaverRun", children: "Background Weaver Run" }),
          /* @__PURE__ */ s.jsx("option", { value: "Unknown", children: "Unknown / legacy" })
        ] })
      ] })
    ] }),
    t === "failed" ? /* @__PURE__ */ s.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ s.jsx(qe, { size: 16 }),
      " ",
      o
    ] }) : null,
    t === "loading" ? /* @__PURE__ */ s.jsx("div", { className: "wf-empty", children: "Loading workflow runs..." }) : null,
    t === "ready" && l.length === 0 ? /* @__PURE__ */ s.jsx("div", { className: "wf-empty", children: "No workflow runs found. Run a published workflow executable to create history." }) : null,
    t === "ready" && l.length > 0 ? /* @__PURE__ */ s.jsxs("div", { className: "wf-grid wf-instance-grid", role: "table", "aria-label": "Workflow runs", children: [
      /* @__PURE__ */ s.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ s.jsx("span", { children: "Run" }),
        /* @__PURE__ */ s.jsx("span", { children: "Kind" }),
        /* @__PURE__ */ s.jsx("span", { children: "Status" }),
        /* @__PURE__ */ s.jsx("span", { children: "Definition" }),
        /* @__PURE__ */ s.jsx("span", { children: "Activity" }),
        /* @__PURE__ */ s.jsx("span", { children: "Started" }),
        /* @__PURE__ */ s.jsx("span", { children: "Duration" })
      ] }),
      l.map((p) => /* @__PURE__ */ s.jsxs(
        "button",
        {
          type: "button",
          className: "wf-grid-row",
          role: "row",
          "aria-label": `Inspect workflow run ${p.workflowExecutionId}`,
          onClick: () => h(p.workflowExecutionId),
          children: [
            /* @__PURE__ */ s.jsxs("span", { children: [
              /* @__PURE__ */ s.jsx("strong", { children: p.workflowExecutionId }),
              /* @__PURE__ */ s.jsx("small", { children: p.artifactId })
            ] }),
            /* @__PURE__ */ s.jsx("span", { children: Pu(p.runKind) }),
            /* @__PURE__ */ s.jsx("span", { children: /* @__PURE__ */ s.jsx(lo, { status: p.status, subStatus: p.subStatus }) }),
            /* @__PURE__ */ s.jsxs("span", { children: [
              /* @__PURE__ */ s.jsx("strong", { children: p.definitionId }),
              /* @__PURE__ */ s.jsx("small", { children: p.definitionVersionId })
            ] }),
            /* @__PURE__ */ s.jsxs("span", { children: [
              /* @__PURE__ */ s.jsxs("strong", { children: [
                p.activityCount,
                " activities"
              ] }),
              /* @__PURE__ */ s.jsxs("small", { children: [
                p.incidentCount,
                " incidents"
              ] })
            ] }),
            /* @__PURE__ */ s.jsx("span", { children: et(p.startedAt ?? p.createdAt) }),
            /* @__PURE__ */ s.jsx("span", { children: ju(p.startedAt ?? p.createdAt, p.completedAt ?? p.updatedAt) })
          ]
        },
        p.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function U0({ context: e, ai: t, workflowExecutionId: n }) {
  const [o, r] = K("loading"), [i, a] = K(""), [c, u] = K(null), [l, d] = K(null), f = Wt(t, "weaver.workflows.explain-instance"), h = de(async () => {
    if (!n) {
      a("No workflow execution id was provided."), r("failed");
      return;
    }
    r("loading"), a("");
    try {
      const g = await iw(e, n), [b, w] = await Promise.all([
        Ux(e, g.instance.definitionVersionId).then(
          (m) => ({ definitionVersion: m, error: "" }),
          (m) => ({ definitionVersion: null, error: m instanceof Error ? m.message : String(m) })
        ),
        es(e)
      ]);
      u({
        details: g,
        definitionVersion: b.definitionVersion,
        definitionVersionError: b.error,
        activityCatalog: w.activities
      }), d(null), r("ready");
    } catch (g) {
      u(null), a(Av(g, n)), r("failed");
    }
  }, [e, n]);
  oe(() => {
    h();
  }, [h]);
  const p = () => {
    window.history.pushState({}, "", "/workflows/instances"), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
    /* @__PURE__ */ s.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: p, children: [
        /* @__PURE__ */ s.jsx(tr, { size: 14 }),
        " Runs"
      ] }),
      /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => {
        h();
      }, children: [
        /* @__PURE__ */ s.jsx(Ji, { size: 14 }),
        " Refresh"
      ] }),
      c && f ? /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => St(t, f, c.details), children: [
        /* @__PURE__ */ s.jsx(Nt, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    o === "loading" ? /* @__PURE__ */ s.jsx("div", { className: "wf-empty", children: "Loading workflow run..." }) : null,
    o === "failed" ? /* @__PURE__ */ s.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ s.jsx(qe, { size: 16 }),
      " ",
      i
    ] }) : null,
    o === "ready" && c ? /* @__PURE__ */ s.jsxs("div", { className: "wf-instance-detail-workbench", children: [
      /* @__PURE__ */ s.jsx(
        Z0,
        {
          definitionVersion: c.definitionVersion,
          definitionVersionError: c.definitionVersionError,
          activityCatalog: c.activityCatalog,
          details: c.details,
          selectedEvidenceId: l,
          onSelectEvidence: d
        }
      ),
      /* @__PURE__ */ s.jsx(
        G0,
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
          graphNodeIds: c.definitionVersion ? tv(c.definitionVersion, c.activityCatalog) : void 0
        }
      )
    ] }) : null
  ] });
}
function Z0({ definitionVersion: e, definitionVersionError: t, activityCatalog: n, details: o, selectedEvidenceId: r, onSelectEvidence: i }) {
  const a = pe(() => {
    if (!e) return { nodes: [], edges: [] };
    const c = e.state.rootActivity;
    if (!c) return { nodes: [], edges: [] };
    const u = n.find((p) => p.activityVersionId === c.activityVersionId), l = ts(c, u), d = l === "unsupported" ? null : Wn(c, []), f = l === "unsupported" ? Ni(c, n, e.layout) : d ? fu(d, n, e.layout) : Ni(c, n, e.layout), h = f.nodes.map((p) => ({
      ...p,
      draggable: !1,
      connectable: !1,
      deletable: !1
    }));
    return {
      nodes: lw(h, o.activities, o.incidents, r),
      edges: f.edges.map((p) => ({ ...p, deletable: !1 }))
    };
  }, [n, e, o, r]);
  return /* @__PURE__ */ s.jsxs("section", { className: "wf-instance-canvas-shell", "aria-label": "Workflow run canvas", children: [
    /* @__PURE__ */ s.jsxs("header", { children: [
      /* @__PURE__ */ s.jsxs("div", { children: [
        /* @__PURE__ */ s.jsx("span", { children: "Definition version" }),
        /* @__PURE__ */ s.jsx("h3", { children: e ? /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
          e.definition.name,
          " ",
          /* @__PURE__ */ s.jsx("small", { children: e.version })
        ] }) : /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
          "Definition graph unavailable ",
          /* @__PURE__ */ s.jsx("small", { children: o.instance.definitionVersionId })
        ] }) })
      ] }),
      /* @__PURE__ */ s.jsx(lo, { status: o.instance.status, subStatus: o.instance.subStatus })
    ] }),
    /* @__PURE__ */ s.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ s.jsxs("div", { className: "wf-empty", children: [
        "The workflow run loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ s.jsx("small", { children: _v(t) }) : null
      ] }),
      e && a.nodes.length === 0 ? /* @__PURE__ */ s.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      a.nodes.length > 0 ? /* @__PURE__ */ s.jsxs(
        Yl,
        {
          nodes: a.nodes,
          edges: a.edges,
          nodeTypes: Iu,
          edgeTypes: _u,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (c, u) => i(u.id),
          onPaneClick: () => i(null),
          children: [
            /* @__PURE__ */ s.jsx(Zl, {}),
            /* @__PURE__ */ s.jsx(eu, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ s.jsx(Jl, {})
          ]
        }
      ) : null
    ] })
  ] });
}
function G0({ ai: e, action: t, summary: n, details: o, state: r, error: i, selectedEvidenceId: a = null, onSelectEvidence: c, graphNodeIds: u, activityCatalog: l = [] }) {
  const [d, f] = K("timeline");
  if (!n)
    return /* @__PURE__ */ s.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ s.jsx("div", { className: "wf-empty", children: "Select a workflow run to inspect its timeline." }) });
  const h = o?.incidents.length ?? 0, p = [
    { id: "timeline", title: "Timeline", order: 0, icon: /* @__PURE__ */ s.jsx(Zi, { size: 14 }), render: () => null },
    { id: "issues", title: h > 0 ? `Issues (${h})` : "Issues", order: 1, icon: /* @__PURE__ */ s.jsx(qe, { size: 14 }), render: () => null },
    { id: "details", title: "Details", order: 2, icon: /* @__PURE__ */ s.jsx(ru, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ s.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Workflow run details", children: [
    /* @__PURE__ */ s.jsxs("header", { children: [
      /* @__PURE__ */ s.jsxs("div", { children: [
        /* @__PURE__ */ s.jsx("span", { children: "Workflow Instance ID" }),
        /* @__PURE__ */ s.jsx("h3", { children: n.workflowExecutionId })
      ] }),
      t ? /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => St(e, t, o ?? n), children: [
        /* @__PURE__ */ s.jsx(Nt, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    /* @__PURE__ */ s.jsx("div", { className: "wf-instance-tabs", children: /* @__PURE__ */ s.jsx(Wo, { label: "Workflow run tabs", tabs: p, activeTabId: d, onSelect: (g) => f(g) }) }),
    r === "loading" ? /* @__PURE__ */ s.jsx("div", { className: "wf-empty", children: "Loading run details..." }) : null,
    r === "failed" ? /* @__PURE__ */ s.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ s.jsx(qe, { size: 16 }),
      " ",
      i
    ] }) : null,
    r === "ready" && o ? /* @__PURE__ */ s.jsx("div", { className: "wf-instance-tab-content", children: d === "timeline" ? /* @__PURE__ */ s.jsx(
      m0,
      {
        activities: o.activities,
        activityCatalog: l,
        selectedEvidenceId: a,
        onSelectEvidence: c
      }
    ) : d === "issues" ? /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
      /* @__PURE__ */ s.jsx(J0, { incidents: o.incidents, selectedEvidenceId: a, onSelectEvidence: c }),
      /* @__PURE__ */ s.jsx(Q0, { details: o, graphNodeIds: u })
    ] }) : /* @__PURE__ */ s.jsxs("dl", { className: "wf-instance-meta", children: [
      /* @__PURE__ */ s.jsx("dt", { children: "Status" }),
      /* @__PURE__ */ s.jsx("dd", { children: /* @__PURE__ */ s.jsx(lo, { status: n.status, subStatus: n.subStatus }) }),
      /* @__PURE__ */ s.jsx("dt", { children: "Run Kind" }),
      /* @__PURE__ */ s.jsx("dd", { children: Pu(n.runKind) }),
      /* @__PURE__ */ s.jsx("dt", { children: "Artifact" }),
      /* @__PURE__ */ s.jsxs("dd", { children: [
        n.artifactId,
        " ",
        /* @__PURE__ */ s.jsx("small", { children: n.artifactVersion })
      ] }),
      /* @__PURE__ */ s.jsx("dt", { children: "Definition" }),
      /* @__PURE__ */ s.jsxs("dd", { children: [
        n.definitionId,
        " ",
        /* @__PURE__ */ s.jsx("small", { children: n.definitionVersionId })
      ] }),
      /* @__PURE__ */ s.jsx("dt", { children: "Created" }),
      /* @__PURE__ */ s.jsx("dd", { children: et(n.createdAt) }),
      /* @__PURE__ */ s.jsx("dt", { children: "Started" }),
      /* @__PURE__ */ s.jsx("dd", { children: et(n.startedAt) }),
      /* @__PURE__ */ s.jsx("dt", { children: "Completed" }),
      /* @__PURE__ */ s.jsx("dd", { children: et(n.completedAt) }),
      /* @__PURE__ */ s.jsx("dt", { children: "Correlation" }),
      /* @__PURE__ */ s.jsx("dd", { children: n.correlationId || "None" })
    ] }) }) : null
  ] });
}
function J0({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
  return /* @__PURE__ */ s.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ s.jsx("h4", { children: "Incidents" }),
    e.length === 0 ? /* @__PURE__ */ s.jsx("p", { children: "No incidents recorded." }) : null,
    e.map((o) => /* @__PURE__ */ s.jsxs(
      "button",
      {
        type: "button",
        className: "wf-instance-incident",
        "data-severity": o.severity.toLowerCase(),
        "data-selected": o.incidentId === t,
        onClick: () => n?.(o.incidentId),
        children: [
          /* @__PURE__ */ s.jsx("strong", { children: o.failureType }),
          /* @__PURE__ */ s.jsxs("span", { children: [
            o.status,
            " · ",
            o.severity
          ] }),
          /* @__PURE__ */ s.jsx("p", { children: o.message })
        ]
      },
      o.incidentId
    ))
  ] });
}
function Q0({ details: e, graphNodeIds: t }) {
  if (!t) return null;
  const n = new Map(e.activities.map((i) => [i.activityExecutionId, i])), o = e.activities.filter((i) => !t.has(Qa(i))), r = e.incidents.filter((i) => {
    const a = i.activityExecutionId ? n.get(i.activityExecutionId) : null, c = i.executableNodeId ?? (a ? Qa(a) : "");
    return !c || !t.has(c);
  });
  return o.length === 0 && r.length === 0 ? null : /* @__PURE__ */ s.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ s.jsx("h4", { children: "Unmatched runtime evidence" }),
    /* @__PURE__ */ s.jsxs("div", { className: "wf-instance-unmatched-list", children: [
      o.map((i) => /* @__PURE__ */ s.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ s.jsx("strong", { children: ir(i.activityType) ?? i.activityType }),
        /* @__PURE__ */ s.jsx("small", { children: i.activityExecutionId })
      ] }, `activity-${i.activityExecutionId}`)),
      r.map((i) => /* @__PURE__ */ s.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ s.jsx("strong", { children: i.failureType }),
        /* @__PURE__ */ s.jsx("small", { children: i.incidentId })
      ] }, `incident-${i.incidentId}`))
    ] })
  ] });
}
function lo({ status: e, subStatus: t }) {
  return /* @__PURE__ */ s.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
function Pu(e) {
  switch (ev(e)) {
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
function ev(e) {
  return (e ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}
function tv(e, t) {
  const n = e.state.rootActivity;
  if (!n) return /* @__PURE__ */ new Set();
  const o = t.find((i) => i.activityVersionId === n.activityVersionId);
  if (ts(n, o) === "unsupported") return /* @__PURE__ */ new Set([n.nodeId]);
  const r = Wn(n, []);
  return new Set(r?.slot.activities.map((i) => i.nodeId) ?? [n.nodeId]);
}
function Qa(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function nv({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const i = Tu(n, t), a = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
  return /* @__PURE__ */ s.jsxs("div", { className: "wf-pagination", "aria-label": "Workflow definition pagination", children: [
    /* @__PURE__ */ s.jsxs("span", { className: "wf-pagination-summary", "aria-live": "polite", children: [
      "Showing ",
      a,
      "-",
      c,
      " of ",
      n
    ] }),
    /* @__PURE__ */ s.jsxs("label", { className: "wf-page-size", children: [
      "Rows",
      /* @__PURE__ */ s.jsx("select", { value: t, onChange: (u) => r(Number(u.target.value)), children: T0.map((u) => /* @__PURE__ */ s.jsx("option", { value: u, children: u }, u)) })
    ] }),
    /* @__PURE__ */ s.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ s.jsx(tr, { size: 14 }),
        " Previous"
      ] }),
      /* @__PURE__ */ s.jsxs("span", { children: [
        "Page ",
        e,
        " of ",
        i
      ] }),
      /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => o(e + 1), disabled: e >= i, "aria-label": "Next page", title: "Next page", children: [
        "Next ",
        /* @__PURE__ */ s.jsx(Qt, { size: 14 })
      ] })
    ] })
  ] });
}
function ov(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function Tu(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Wt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function St(e, t, n) {
  const o = t.createPrompt(n);
  return o ? (e.dispatchPrompt(o), !0) : !1;
}
function rv(e) {
  const t = sr(e, "flowchart"), n = sr(e, "sequence"), o = [
    { value: t?.activityVersionId ?? "kind:flowchart", label: "Flowchart" },
    { value: n?.activityVersionId ?? "kind:sequence", label: "Sequence" }
  ], r = /* @__PURE__ */ new Map();
  for (const a of e.filter(Lu)) {
    if (av(a)) continue;
    const c = a.category || "Uncategorized";
    r.set(c, [...r.get(c) ?? [], a]);
  }
  const i = Array.from(r.entries()).sort(([a], [c]) => a.localeCompare(c)).map(([a, c]) => ({
    name: a,
    activities: c.sort((u, l) => je(u).localeCompare(je(l)))
  }));
  return { compositeRoots: o, otherCategories: i };
}
function iv(e, t) {
  return e.rootActivityVersionId ?? sr(t, e.rootKind)?.activityVersionId ?? `kind:${e.rootKind}`;
}
function sv(e, t) {
  return e.rootActivityVersionId ?? sr(t, e.rootKind)?.activityVersionId ?? null;
}
function sr(e, t) {
  return e.find((n) => $u(n) === t);
}
function $u(e) {
  return e ? Ru(e) ? "flowchart" : zu(e) ? "sequence" : null : null;
}
function _i(e) {
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
function av(e) {
  return Ru(e) || zu(e);
}
function Ru(e) {
  return je(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function zu(e) {
  return je(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function Lu(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function Vu(e) {
  return fv(e.rootActivityType) || e.rootActivityType;
}
function cv(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function lv(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function uv(e, t) {
  return ec(t) - ec(e);
}
function ec(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function Ou(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
function Hu(e) {
  const t = e?.workflowExecutionId ?? e?.runId ?? e?.executionId;
  return typeof t == "string" && t.trim() ? t : null;
}
async function dv(e) {
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
function fv(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function hv(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    To(t, n.typeName, n), To(t, n.name, n), To(t, n.displayName, n);
    const o = n.typeName.split(".").filter(Boolean).at(-1);
    To(t, o, n);
  }
  return t;
}
function pv(e, t, n) {
  const o = t.get(e.activityVersionId);
  return n.get(On(o?.activityTypeKey)) ?? n.get(On(ir(o?.activityTypeKey))) ?? n.get(On(o?.displayName)) ?? n.get(On(e.activityVersionId)) ?? null;
}
function To(e, t, n) {
  const o = On(t);
  o && !e.has(o) && e.set(o, n);
}
function On(e) {
  return e?.trim().toLowerCase() ?? "";
}
function tc(e, t, n, o) {
  const r = Sr();
  if (!r) return t;
  const i = r.getItem(e);
  if (i == null) return t;
  const a = Number(i);
  return Number.isFinite(a) ? Fo(a, n, o) : t;
}
function nc(e, t) {
  const n = Sr();
  if (!n) return t;
  const o = n.getItem(e);
  return o === "true" ? !0 : o === "false" ? !1 : t;
}
function gv() {
  const e = Sr();
  if (!e) return null;
  const t = e.getItem(Au);
  return t === "palette" || t === "inspector" ? t : null;
}
function Sr() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function Rn(e, t) {
  const n = Sr();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function Fo(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function yv({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: o,
  expressionEditors: r,
  workflowDesignerPanels: i,
  onBack: a
}) {
  const [c, u] = K(null), [l, d] = K(null), [f, h] = K([]), [p, g] = K([]), [b, w] = K(Oo), [m, E] = K("loading"), [y, v] = K([]), [N, k] = K([]), [S, _] = K([]), [P, X] = K(null), [M, $] = K(null), [H, j] = K(null), [A, I] = K(null), [D, T] = K(""), [R, V] = K(""), [B, F] = K("idle"), [Z, Y] = K(null), [te, ce] = K(!1), [J, L] = K(null), [U, ae] = K(() => /* @__PURE__ */ new Set()), [re, Q] = K(""), [ee, le] = K(() => tc(Ka, R0, Dn, Pn)), [O, G] = K(() => tc(qa, z0, Tn, $n)), [he, ve] = K(() => nc(Ya, !1)), [ke, Ie] = K(() => nc(Ua, !1)), [me, Ye] = K(gv), [st, at] = K("activities"), [ze, Le] = K("inspector"), [De, ct] = K("designer"), Ke = se(null), gt = se(null), mn = se(""), xn = se(0), uo = se(Promise.resolve()), wn = se(/* @__PURE__ */ new Map()), Ve = se(La()), lt = se(null), ut = se(""), Bt = se(!1), [jr, vn] = K(0), Xt = se(null), yt = se(null), Et = se(!1), kt = l?.state.rootActivity ?? null, nt = pe(() => new Map(f.map((x) => [x.activityVersionId, x])), [f]), fo = pe(() => hv(p), [p]), Oe = pe(() => du(kt, y), [kt, y]), ho = ts(Oe, Oe ? nt.get(Oe.activityVersionId) : void 0), we = !!Oe && ho === "unsupported", He = pe(() => we ? null : Wn(kt, y), [kt, y, we]), Kt = pe(() => _i(f), [f]), po = pe(() => {
    const x = re.trim().toLowerCase();
    if (!x) return Kt;
    const C = f.filter((z) => je(z).toLowerCase().includes(x) || z.activityTypeKey.toLowerCase().includes(x) || (z.category ?? "").toLowerCase().includes(x) || (z.description ?? "").toLowerCase().includes(x));
    return _i(C);
  }, [f, re, Kt]), Se = pe(() => we && Oe?.nodeId === M ? Oe : He?.slot.activities.find((x) => x.nodeId === M) ?? null, [we, He, Oe, M]), Ct = pe(
    () => Se ? pv(Se, nt, fo) : null,
    [nt, fo, Se]
  ), bn = Se ? Qe(Se) : [], _e = ho === "flowchart" && He?.slot.mode === "flowchart", It = !kt || !we, mt = B !== "idle", Er = !!l?.state.rootActivity && !mt, go = Wt(n, "weaver.workflows.find-draft-risks"), Nn = Wt(n, "weaver.workflows.propose-update");
  oe(() => {
    if (!(!c || !l))
      return window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
        workflowId: c.definition.id,
        workflowDefinitionId: c.definition.id,
        workflowVersionId: l.sourceVersionId ?? null,
        draftId: l.id,
        revision: Cv(l),
        selectedNodeId: M,
        selectedActivityType: Ct?.typeName ?? (Se ? nt.get(Se.activityVersionId)?.activityTypeKey ?? Se.activityVersionId : null),
        summary: c.definition.name,
        activities: Wu(l.state.rootActivity, nt),
        diagnostics: l.validationErrors.map((x) => ({ severity: x.code ?? "warning", message: x.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === c.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [nt, c, l, Ct, Se, M]), oe(() => {
    const x = (z) => {
      const W = z.detail;
      if (!W?.batch || !W.respond) return;
      if (!l || !c) {
        W.respond({ ok: !1, message: "No active workflow draft is open." });
        return;
      }
      const q = W.batch.workflowDefinitionId;
      if (q && q !== "active-draft" && q !== c.definition.id) {
        W.respond({ ok: !1, message: `Batch targets workflow '${q}', but '${c.definition.id}' is active.` });
        return;
      }
      try {
        const ue = zn(l), ie = N0(l, W.batch, f), ye = `weaver-batch-${Date.now()}`;
        wn.current.set(ye, ue), d(ie.draft), v([]), $(ie.finalActivityIds.at(-1) ?? null), L(null), Y(null), V(ie.summary), T(""), W.respond({ ok: !0, result: { ...ie, undoToken: ye } });
      } catch (ue) {
        const ie = ue instanceof Error ? ue.message : String(ue);
        T(ie), W.respond({ ok: !1, message: ie });
      }
    }, C = (z) => {
      const W = z.detail;
      if (!W?.undoToken || !W.respond) return;
      const q = wn.current.get(W.undoToken);
      if (!q) {
        W.respond({ ok: !1, message: "The Weaver batch undo point is no longer available." });
        return;
      }
      wn.current.delete(W.undoToken), d(q), v([]), $(null), L(null), Y(null), V("Restored workflow draft before Weaver batch."), T(""), W.respond({ ok: !0, summary: "Restored workflow draft before Weaver batch." });
    };
    return window.addEventListener(Fa, x), window.addEventListener(Wa, C), () => {
      window.removeEventListener(Fa, x), window.removeEventListener(Wa, C);
    };
  }, [f, c, l]), oe(() => {
    Rn(Ka, String(ee));
  }, [ee]), oe(() => {
    Rn(qa, String(O));
  }, [O]), oe(() => {
    Rn(Ya, String(he));
  }, [he]), oe(() => {
    Rn(Ua, String(ke));
  }, [ke]), oe(() => {
    Rn(Au, me);
  }, [me]), oe(() => {
    if (!me) return;
    const x = (C) => {
      C.key === "Escape" && Ye(null);
    };
    return window.addEventListener("keydown", x), () => window.removeEventListener("keydown", x);
  }, [me]);
  const qt = de(async () => {
    T(""), E("loading");
    const [x, C, z, W] = await Promise.all([
      Yx(e, t),
      es(e),
      sw(e).then(
        (ue) => ({ ok: !0, descriptors: ue }),
        () => ({ ok: !1, descriptors: [] })
      ),
      aw(e).then(
        (ue) => ({ ok: !0, descriptors: ue }),
        () => ({ ok: !1, descriptors: Oo })
      )
    ]), q = x.draft ?? null;
    u(x), mn.current = q ? Fe(q) : "", Ve.current = La(), lt.current = q ? zn(q) : null, ut.current = q ? Fe(q) : "", Bt.current = !1, vn(0), d(q), h(C.activities ?? []), g(z.descriptors), w(W.descriptors.length > 0 ? W.descriptors : Oo), E(z.ok ? "ready" : "failed"), v([]), $(null);
  }, [e, t]);
  oe(() => {
    qt().catch((x) => T(x instanceof Error ? x.message : String(x)));
  }, [qt]), oe(() => {
    ae((x) => {
      let C = !1;
      const z = new Set(x);
      for (const W of Kt)
        z.has(W.category) || (z.add(W.category), C = !0);
      return C ? z : x;
    });
  }, [Kt]), oe(() => {
    if (!Oe) {
      k([]), _([]);
      return;
    }
    const x = we ? Ni(Oe, f, l?.layout ?? []) : He ? fu(He, f, l?.layout ?? []) : { nodes: [], edges: [] };
    k(x.nodes), _(x.edges);
  }, [f, l?.layout, we, He, Oe]);
  const yo = (x) => {
    d((C) => C && { ...C, state: { ...C.state, rootActivity: x } });
  }, Sn = de((x, C) => {
    if (l?.state.rootActivity && we)
      return;
    const z = ji(x, ic(x));
    if (!l?.state.rootActivity) {
      yo(z), $(z.nodeId);
      return;
    }
    if (!He) {
      if (!Qe(z)[0]) {
        V(""), T("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      d((q) => {
        if (!q?.state.rootActivity) return q;
        const ue = q.state.rootActivity, ie = Si(z, [], [ue]), ye = C ? [
          ...q.layout.filter((xe) => xe.nodeId !== ue.nodeId),
          {
            nodeId: ue.nodeId,
            x: Math.round(C.x),
            y: Math.round(C.y)
          }
        ] : q.layout;
        return {
          ...q,
          layout: ye,
          state: {
            ...q.state,
            rootActivity: ie
          }
        };
      }), $(l.state.rootActivity.nodeId), T(""), V(`Wrapped root in ${je(x)}`);
      return;
    }
    d((W) => {
      if (!W?.state.rootActivity) return W;
      const q = Wn(W.state.rootActivity, y);
      if (!q) return W;
      const ue = Si(W.state.rootActivity, y, [...q.slot.activities, z]), ie = C ? [
        ...W.layout.filter((ye) => ye.nodeId !== z.nodeId),
        {
          nodeId: z.nodeId,
          x: Math.round(C.x),
          y: Math.round(C.y)
        }
      ] : W.layout;
      return {
        ...W,
        layout: ie,
        state: {
          ...W.state,
          rootActivity: ue
        }
      };
    }), $(z.nodeId);
  }, [l?.state.rootActivity, y, we, He]), Yt = de((x, C) => {
    const z = ji(x, ic(x)), W = {
      id: z.nodeId,
      type: "workflowActivity",
      position: C,
      selected: !0,
      data: {
        label: je(x),
        activityVersionId: x.activityVersionId,
        activityTypeKey: x.activityTypeKey,
        category: x.category,
        executionType: x.executionType,
        icon: or(x),
        childSlots: Qe(z),
        acceptsInbound: String(x.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: yu(z, x)
      }
    };
    return { activityNode: z, node: W };
  }, []), Pe = de((x, C, z = []) => {
    we || d((W) => {
      if (!W) return W;
      const q = fw(W.layout, x), ue = W.state.rootActivity;
      if (!ue) return { ...W, layout: q };
      const ie = Wn(ue, y);
      if (!ie) return { ...W, layout: q };
      const ye = uw(ie, x, C, z), xe = ie.slot.mode === "flowchart" ? dw(ye, C) : ye;
      return {
        ...W,
        layout: q,
        state: {
          ...W.state,
          rootActivity: hu(ue, y, xe)
        }
      };
    });
  }, [y, we]), jn = de((x, C) => {
    if (!Ke.current) return null;
    const z = Ke.current.getBoundingClientRect();
    return P ? P.screenToFlowPosition({ x, y: C }) : {
      x: x - z.left,
      y: C - z.top
    };
  }, [P]), En = de((x, C) => document.elementFromPoint(x, C)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), kn = de((x, C, z) => {
    const W = N.find((Ae) => Ae.id === C.source), q = N.find((Ae) => Ae.id === C.target), ue = W && q ? jv(W, q) : W ? sc(W) : z, ie = Yt(x, ue), xe = [...N.map((Ae) => Ae.selected ? { ...Ae, selected: !1 } : Ae), ie.node], Mt = Nw(S, C, ie.node.id);
    k(xe), _(Mt), $(ie.node.id), Pe(xe, Mt, [ie.activityNode]);
  }, [Pe, Yt, S, N]), _t = de((x, C, z) => {
    if (!It || !Ke.current) return !1;
    const W = Ke.current.getBoundingClientRect();
    if (!(C >= W.left && C <= W.right && z >= W.top && z <= W.bottom)) return !1;
    const ue = jn(C, z);
    if (!ue) return !1;
    if (_e) {
      const ie = En(C, z), ye = ie ? S.find((xe) => xe.id === ie) : void 0;
      if (ye)
        return kn(x, ye, ue), !0;
    }
    return Sn(x, ue), !0;
  }, [Sn, It, S, En, _e, kn, jn]);
  oe(() => {
    const x = (z) => {
      const W = Xt.current;
      if (!W) return;
      Math.hypot(z.clientX - W.startX, z.clientY - W.startY) >= M0 && (W.dragging = !0);
    }, C = (z) => {
      const W = Xt.current;
      if (Xt.current = null, !W?.dragging || !Ke.current || yt.current) return;
      const q = Ke.current.getBoundingClientRect();
      z.clientX >= q.left && z.clientX <= q.right && z.clientY >= q.top && z.clientY <= q.bottom && (Et.current = !0, window.setTimeout(() => {
        Et.current = !1;
      }, 0), _t(W.activity, z.clientX, z.clientY));
    };
    return window.addEventListener("pointermove", x), window.addEventListener("pointerup", C), window.addEventListener("pointercancel", C), () => {
      window.removeEventListener("pointermove", x), window.removeEventListener("pointerup", C), window.removeEventListener("pointercancel", C);
    };
  }, [P, _t]);
  const kr = (x, C) => {
    yt.current = { activityVersionId: C.activityVersionId, handledDrop: !1 }, x.dataTransfer.setData(Xa, C.activityVersionId), x.dataTransfer.setData("text/plain", C.activityVersionId), x.dataTransfer.effectAllowed = "copy";
  }, Cr = (x, C) => {
    const z = yt.current;
    yt.current = null, !z?.handledDrop && (x.clientX === 0 && x.clientY === 0 || _t(C, x.clientX, x.clientY) && (Et.current = !0, window.setTimeout(() => {
      Et.current = !1;
    }, 0)));
  }, Ir = (x, C) => {
    x.button === 0 && (Xt.current = {
      activity: C,
      startX: x.clientX,
      startY: x.clientY,
      dragging: !1
    });
  }, _r = (x) => {
    Et.current || It && Sn(x);
  }, mo = (x) => {
    if (!It) {
      x.dataTransfer.dropEffect = "none";
      return;
    }
    if (x.preventDefault(), x.dataTransfer.dropEffect = "copy", !_e) return;
    const C = En(x.clientX, x.clientY);
    I(C);
  }, xo = (x) => {
    if (!Ke.current) return;
    const C = x.relatedTarget;
    C && Ke.current.contains(C) || I(null);
  }, wo = (x) => {
    x.preventDefault(), I(null);
    const C = x.dataTransfer.getData(Xa) || x.dataTransfer.getData("text/plain");
    if (!C || (x.stopPropagation(), yt.current?.activityVersionId === C && (yt.current.handledDrop = !0), !It)) return;
    const z = nt.get(C);
    z && _t(z, x.clientX, x.clientY);
  }, Ar = () => {
    if (!_e) return;
    const x = Ke.current?.getBoundingClientRect();
    x && j({
      kind: "fromEmpty",
      clientX: x.left + x.width / 2,
      clientY: x.top + x.height / 2
    });
  }, Ut = de(async (x, C) => {
    const z = async () => {
      const q = ++xn.current, ue = Fe(x);
      T("");
      try {
        const ie = await ew(e, x), ye = Fe(ie);
        return mn.current = ye, d((xe) => !xe || xe.id !== ie.id ? xe : Fe(xe) === ue ? ie : { ...xe, validationErrors: ie.validationErrors }), q === xn.current && V(C), ie;
      } catch (ie) {
        throw q === xn.current && (V(""), T(ie instanceof Error ? ie.message : String(ie))), ie;
      }
    }, W = uo.current.then(z, z);
    return uo.current = W.catch(() => {
    }), W;
  }, [e]);
  oe(() => {
    if (!te || !l || Fe(l) === mn.current) return;
    V("Autosaving...");
    const C = window.setTimeout(() => {
      Ut(l, "Autosaved").catch(() => {
      });
    }, D0);
    return () => window.clearTimeout(C);
  }, [te, l, Ut]), oe(() => {
    if (!l) return;
    if (Bt.current) {
      Bt.current = !1;
      return;
    }
    const x = Fe(l);
    if (x === ut.current) return;
    const C = window.setTimeout(() => {
      const z = lt.current;
      z && (Ve.current = Va(Ve.current, z), vn((W) => W + 1)), lt.current = zn(l), ut.current = x;
    }, P0);
    return () => window.clearTimeout(C);
  }, [l]);
  const Mr = de(() => {
    if (!l) return;
    const x = c?.definition.name;
    Zw(qw(l, x), x), V("Exported workflow as JSON.");
  }, [l, c]), Dr = de((x) => {
    if (!l) return "No draft is loaded.";
    const C = Uw(x, l);
    return C.ok ? (d(C.draft), $(null), v([]), V("Applied workflow JSON."), null) : C.error;
  }, [l]), Zt = de(() => {
    if (!l) return;
    const x = Fe(l);
    if (x === ut.current) return;
    const C = lt.current;
    C && (Ve.current = Va(Ve.current, C)), lt.current = zn(l), ut.current = x;
  }, [l]), Gt = de((x) => {
    Bt.current = !0, lt.current = zn(x), ut.current = Fe(x), d(x), $(null), v([]), vn((C) => C + 1);
  }, []), Cn = de(() => {
    if (!l) return;
    Zt();
    const x = i0(Ve.current, l);
    x && (Ve.current = x.history, Gt(x.snapshot));
  }, [l, Zt, Gt]), In = de(() => {
    if (!l) return;
    Zt();
    const x = s0(Ve.current, l);
    x && (Ve.current = x.history, Gt(x.snapshot));
  }, [l, Zt, Gt]), { canUndoNow: Pr, canRedoNow: Tr } = pe(() => {
    const x = !!l && !!lt.current && Fe(l) !== ut.current;
    return {
      canUndoNow: o0(Ve.current) || x,
      canRedoNow: r0(Ve.current) && !x
    };
  }, [l, jr]);
  oe(() => {
    const x = (C) => {
      if (De !== "designer" || !(C.metaKey || C.ctrlKey)) return;
      const z = C.target;
      if (z && (z.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(z.tagName))) return;
      const W = C.key.toLowerCase();
      W === "z" && !C.shiftKey ? (C.preventDefault(), Cn()) : (W === "z" && C.shiftKey || W === "y") && (C.preventDefault(), In());
    };
    return window.addEventListener("keydown", x), () => window.removeEventListener("keydown", x);
  }, [De, Cn, In]);
  const vo = async () => {
    if (!(!l || mt)) {
      F("saving"), V("Saving...");
      try {
        await Ut(l, "Saved");
      } catch {
      } finally {
        F("idle");
      }
    }
  }, $r = async () => {
    if (!(!l || mt)) {
      F("promoting"), V("Promoting...");
      try {
        const x = await tw(e, l.id), C = await nw(e, x.versionId);
        L(C.artifactId), V(`Published ${C.artifactVersion}`), await qt();
      } catch (x) {
        V(""), T(x instanceof Error ? x.message : String(x));
      } finally {
        F("idle");
      }
    }
  }, bo = async () => {
    if (!l?.state.rootActivity || mt) return;
    const x = l, C = Fe(x);
    Y(null), V("Preparing test run...");
    try {
      F("testRunPreparing"), V("Preparing test run...");
      const z = Iv(x);
      F("testRunStarting"), V("Starting test run...");
      const W = await ow(e, {
        definitionId: x.definitionId,
        snapshotId: z,
        state: x.state
      });
      Y({ draftSignature: C, view: W }), Le("runtime"), Ie(!1), V(as(W) ? "Test run rejected" : "Test run dispatched");
    } catch (z) {
      V(""), T(z instanceof Error ? z.message : String(z));
    } finally {
      F("idle");
    }
  }, Rr = (x) => {
    const C = we ? x.filter((z) => z.type === "select") : x;
    C.length !== 0 && k((z) => vl(C, z));
  }, zr = (x) => {
    we || _((C) => bl(x, C));
  }, At = (x) => !x.source || !x.target || x.source === x.target || !_e ? !1 : !x.targetHandle, Lr = (x) => {
    if (!l?.state.rootActivity || !He || !_e || !At(x)) return;
    const C = rr(x.source, x.target, x.sourceHandle ?? "Done", x.targetHandle ?? void 0), z = Sl(C, S);
    _(z), Pe(N, z);
  }, Vr = () => {
    Pe(N, S);
  }, No = !we && N.length > 0, Xu = de(() => {
    if (we || N.length === 0) return;
    const x = He?.slot.mode === "sequence" ? "sequence" : "flowchart", C = Qw(N, S, x), z = N.map((W) => {
      const q = C.get(W.id);
      return q ? { ...W, position: q } : W;
    });
    k(z), Pe(z, S), window.requestAnimationFrame(() => P?.fitView({ padding: 0.2 })), V("Rearranged the canvas.");
  }, [S, N, He, we, Pe, P]), Ku = (x, C) => {
    if (!C.nodeId || C.handleType === "target") {
      gt.current = null;
      return;
    }
    gt.current = {
      nodeId: C.nodeId,
      handleId: C.handleId ?? null
    };
  }, qu = (x, C) => {
    const z = kv(gt.current, C);
    if (gt.current = null, !z || !_e || C.toNode || C.toHandle || Ev(x)) return;
    const W = Fu(x);
    j({
      kind: "fromPort",
      sourceNodeId: z.nodeId,
      sourceHandleId: z.handleId,
      clientX: W.x,
      clientY: W.y
    });
  }, Yu = (x, C) => {
    if (!_e || !At(C)) return;
    const z = Cy(x, {
      ...C,
      sourceHandle: C.sourceHandle ?? "Done",
      targetHandle: C.targetHandle ?? void 0
    }, S, { shouldReplaceId: !1 });
    _(z), Pe(N, z);
  }, Uu = (x) => {
    if (we || x.length === 0) return;
    const C = new Set(x.map((q) => q.id)), z = N.filter((q) => !C.has(q.id)), W = S.filter((q) => !C.has(q.source) && !C.has(q.target));
    k(z), _(W), M && C.has(M) && $(null), Pe(z, W);
  }, Zu = (x) => {
    if (we || x.length === 0) return;
    const C = new Set(x.map((W) => W.id)), z = S.filter((W) => !C.has(W.id));
    _(z), Pe(N, z);
  }, cs = de((x) => {
    if (we) return;
    const C = S.filter((z) => z.id !== x);
    _(C), Pe(N, C);
  }, [Pe, S, we, N]), ls = de((x, C, z) => {
    _e && j({ kind: "spliceEdge", edgeId: x, clientX: C, clientY: z });
  }, [_e]), Gu = (x) => {
    const C = H;
    if (!C) return;
    j(null);
    const z = jn(C.clientX, C.clientY) ?? { x: 0, y: 0 };
    if (C.kind === "fromEmpty") {
      const q = Yt(x, z), ie = [...N.map((ye) => ye.selected ? { ...ye, selected: !1 } : ye), q.node];
      k(ie), $(q.node.id), Pe(ie, S, [q.activityNode]);
      return;
    }
    if (C.kind === "fromPort") {
      const q = N.find((Ae) => Ae.id === C.sourceNodeId), ue = q ? sc(q) : z, ie = Yt(x, ue), xe = [...N.map((Ae) => Ae.selected ? { ...Ae, selected: !1 } : Ae), ie.node], Mt = [...S, rr(C.sourceNodeId, ie.node.id, C.sourceHandleId ?? "Done")];
      k(xe), _(Mt), $(ie.node.id), Pe(xe, Mt, [ie.activityNode]);
      return;
    }
    const W = S.find((q) => q.id === C.edgeId);
    W && kn(x, W, z);
  }, Ju = pe(() => ({
    highlightedEdgeId: A,
    deleteEdge: cs,
    requestInsertActivity: ls
  }), [cs, A, ls]), Qu = (x, C, z) => {
    v((W) => [...W, { ownerNodeId: x.nodeId, slotId: C, label: z }]), $(null);
  }, ed = de((x) => {
    d((C) => {
      const z = C?.state.rootActivity;
      return !C || !z ? C : {
        ...C,
        state: {
          ...C.state,
          rootActivity: pu(z, x.nodeId, () => x)
        }
      };
    });
  }, []), td = (x) => {
    ae((C) => {
      const z = new Set(C);
      return z.has(x) ? z.delete(x) : z.add(x), z;
    });
  }, us = (x) => {
    Ye((C) => C === x ? null : C), x === "palette" ? ve((C) => !C) : Ie((C) => !C);
  }, ds = (x) => {
    x === "palette" ? ve(!1) : Ie(!1), Ye((C) => C === x ? null : x);
  }, fs = (x, C) => {
    Ye(null), x === "palette" ? (ve(!1), le((z) => Fo(z + C, Dn, Pn))) : (Ie(!1), G((z) => Fo(z + C, Tn, $n)));
  }, hs = (x, C) => {
    C.preventDefault(), Ye(null), x === "palette" ? ve(!1) : Ie(!1);
    const z = C.clientX, W = x === "palette" ? ee : O, q = x === "palette" ? Dn : Tn, ue = x === "palette" ? Pn : $n;
    document.body.classList.add("wf-side-panel-resizing");
    const ie = (xe) => {
      const Mt = x === "palette" ? xe.clientX - z : z - xe.clientX, Ae = Fo(W + Mt, q, ue);
      x === "palette" ? le(Ae) : G(Ae);
    }, ye = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", ie), window.removeEventListener("pointerup", ye), window.removeEventListener("pointercancel", ye);
    };
    window.addEventListener("pointermove", ie), window.addEventListener("pointerup", ye), window.addEventListener("pointercancel", ye);
  }, ps = (x, C) => {
    C.key === "ArrowLeft" ? (C.preventDefault(), fs(x, x === "palette" ? -Po : Po)) : C.key === "ArrowRight" ? (C.preventDefault(), fs(x, x === "palette" ? Po : -Po)) : C.key === "Home" ? (C.preventDefault(), x === "palette" ? le(Dn) : G(Tn)) : C.key === "End" && (C.preventDefault(), x === "palette" ? le(Pn) : G($n));
  };
  if (!c || !l)
    return /* @__PURE__ */ s.jsx("div", { className: "wf-empty", children: D || "Loading workflow editor..." });
  const nd = [
    "wf-editor-body",
    he ? "palette-collapsed" : "",
    ke ? "inspector-collapsed" : "",
    me === "palette" ? "palette-maximized" : "",
    me === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), od = {
    "--wf-palette-width": `${he ? Za : ee}px`,
    "--wf-inspector-width": `${ke ? Za : O}px`
  }, gs = !he && me !== "inspector", ys = !ke && me !== "palette", So = Z?.draftSignature === Fe(l) ? Z.view : null, ms = So && R.startsWith("Test run") ? "" : R, rd = (x) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(x)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, id = {
    definition: c.definition,
    draft: l,
    selectedActivity: Se,
    selectedActivityDescriptor: Ct,
    selectedActivitySlots: bn,
    catalog: f,
    currentScopeOwner: Oe,
    frames: y
  }, xs = i.map((x) => {
    const C = x.component;
    return {
      id: x.id,
      title: x.title,
      side: x.side,
      order: x.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ s.jsx(C, { context: id })
    };
  }), Or = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ s.jsx(nu, { size: 15 }),
      render: ad
    },
    ...xs.filter((x) => x.side === "left")
  ].sort(oc), Hr = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ s.jsx(Zi, { size: 15 }),
      render: cd
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: /* @__PURE__ */ s.jsx(Qn, { size: 15 }),
      render: () => /* @__PURE__ */ s.jsx(Sv, { testRun: So, onOpenRun: rd })
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ s.jsx(Mx, { size: 15 }),
      render: () => /* @__PURE__ */ s.jsx(
        q0,
        {
          context: e,
          ai: n,
          definitionId: c.definition.id,
          publishedArtifactId: J
        }
      )
    },
    ...xs.filter((x) => x.side === "right")
  ].sort(oc), ws = Or.find((x) => x.id === st) ?? Or[0], vs = Hr.find((x) => x.id === ze) ?? Hr[0], sd = [
    { id: "designer", title: "Designer", order: 0, icon: /* @__PURE__ */ s.jsx(Rx, { size: 14 }), render: () => null },
    { id: "code", title: "Code", order: 1, icon: /* @__PURE__ */ s.jsx(kx, { size: 14 }), render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: /* @__PURE__ */ s.jsx(ru, { size: 14 }), render: () => null }
  ];
  function ad() {
    const x = re.trim().length > 0;
    return /* @__PURE__ */ s.jsxs("div", { className: "wf-palette-body", children: [
      /* @__PURE__ */ s.jsxs("label", { className: "wf-palette-search", children: [
        /* @__PURE__ */ s.jsx(Qi, { size: 14, "aria-hidden": "true" }),
        /* @__PURE__ */ s.jsx(
          "input",
          {
            type: "search",
            value: re,
            placeholder: "Search activities",
            "aria-label": "Search activity palette",
            onChange: (C) => Q(C.target.value)
          }
        )
      ] }),
      /* @__PURE__ */ s.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: po.length === 0 ? /* @__PURE__ */ s.jsx("p", { className: "wf-muted wf-palette-empty", children: "No matching activities." }) : po.map((C) => {
        const z = x || U.has(C.category);
        return /* @__PURE__ */ s.jsxs("div", { className: "wf-palette-category", children: [
          /* @__PURE__ */ s.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-category-toggle",
              role: "treeitem",
              "aria-expanded": z,
              onClick: () => td(C.category),
              children: [
                z ? /* @__PURE__ */ s.jsx(Ex, { size: 14 }) : /* @__PURE__ */ s.jsx(Qt, { size: 14 }),
                /* @__PURE__ */ s.jsx("span", { children: C.category }),
                /* @__PURE__ */ s.jsx("small", { children: C.activities.length })
              ]
            }
          ),
          z ? /* @__PURE__ */ s.jsx("div", { className: "wf-palette-activities", role: "group", children: C.activities.map((W) => {
            const q = W.description?.trim(), ue = q ? `wf-palette-description-${W.activityVersionId}` : void 0, ie = je(W), ye = or(W);
            return /* @__PURE__ */ s.jsxs(
              "button",
              {
                type: "button",
                className: "wf-palette-activity",
                role: "treeitem",
                draggable: !0,
                title: q || je(W),
                "aria-describedby": ue,
                onClick: () => _r(W),
                onDragStart: (xe) => kr(xe, W),
                onDragEnd: (xe) => Cr(xe, W),
                onPointerDown: (xe) => Ir(xe, W),
                children: [
                  /* @__PURE__ */ s.jsx("span", { className: "wf-activity-icon", "data-icon": ye, "aria-hidden": "true", children: rs(ye) }),
                  /* @__PURE__ */ s.jsxs("span", { className: "wf-palette-activity-text", children: [
                    /* @__PURE__ */ s.jsx("strong", { children: ie }),
                    q ? /* @__PURE__ */ s.jsx("small", { id: ue, children: q }) : null
                  ] }),
                  /* @__PURE__ */ s.jsx(_x, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
                ]
              },
              W.activityVersionId
            );
          }) }) : null
        ] }, C.category);
      }) })
    ] });
  }
  function cd() {
    return Se ? /* @__PURE__ */ s.jsxs("div", { className: "wf-inspector-content", children: [
      /* @__PURE__ */ s.jsx("h3", { children: N.find((x) => x.id === Se.nodeId)?.data.label ?? Se.nodeId }),
      /* @__PURE__ */ s.jsxs("dl", { children: [
        /* @__PURE__ */ s.jsx("dt", { children: "Node ID" }),
        /* @__PURE__ */ s.jsx("dd", { children: Se.nodeId }),
        /* @__PURE__ */ s.jsx("dt", { children: "Activity type" }),
        /* @__PURE__ */ s.jsx("dd", { children: Ct?.typeName ?? nt.get(Se.activityVersionId)?.activityTypeKey ?? "Unknown" }),
        /* @__PURE__ */ s.jsx("dt", { children: "Activity version" }),
        /* @__PURE__ */ s.jsx("dd", { children: Se.activityVersionId })
      ] }),
      /* @__PURE__ */ s.jsx(
        Lw,
        {
          activity: Se,
          descriptor: Ct,
          editors: o,
          expressionEditors: r,
          expressionDescriptors: b,
          descriptorStatus: m,
          onChange: ed
        }
      ),
      bn.length > 0 ? /* @__PURE__ */ s.jsxs("div", { className: "wf-slot-list", children: [
        /* @__PURE__ */ s.jsx("span", { children: "Embedded slots" }),
        bn.map((x) => /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => Qu(Se, x.id, `${N.find((C) => C.id === Se.nodeId)?.data.label ?? Se.nodeId} / ${x.label}`), children: [
          x.label,
          /* @__PURE__ */ s.jsxs("small", { children: [
            x.activities.length,
            " activit",
            x.activities.length === 1 ? "y" : "ies"
          ] })
        ] }, x.id))
      ] }) : /* @__PURE__ */ s.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
    ] }) : /* @__PURE__ */ s.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
  }
  return /* @__PURE__ */ s.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ s.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ s.jsx("button", { type: "button", className: "wf-link-button", onClick: a, children: "Definitions" }),
      /* @__PURE__ */ s.jsx(Qt, { size: 14 }),
      /* @__PURE__ */ s.jsx("strong", { children: c.definition.name }),
      /* @__PURE__ */ s.jsx("span", { className: "wf-chip", children: "Draft" }),
      ms ? /* @__PURE__ */ s.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ s.jsx(yn, { size: 13 }),
        " ",
        ms
      ] }) : null,
      /* @__PURE__ */ s.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ s.jsxs("div", { className: "wf-canvas-tools", role: "group", "aria-label": "Canvas tools", children: [
          /* @__PURE__ */ s.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Undo",
              title: "Undo (Ctrl+Z)",
              disabled: !Pr,
              onClick: Cn,
              children: /* @__PURE__ */ s.jsx($x, { size: 16 })
            }
          ),
          /* @__PURE__ */ s.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Redo",
              title: "Redo (Ctrl+Shift+Z)",
              disabled: !Tr,
              onClick: In,
              children: /* @__PURE__ */ s.jsx(Dx, { size: 16 })
            }
          ),
          /* @__PURE__ */ s.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Auto-layout",
              title: "Auto-layout the canvas",
              disabled: !No,
              onClick: Xu,
              children: /* @__PURE__ */ s.jsx(Ax, { size: 16 })
            }
          )
        ] }),
        /* @__PURE__ */ s.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ s.jsx("input", { className: "wf-autosave-switch-input", type: "checkbox", checked: te, onChange: (x) => ce(x.target.checked) }),
          /* @__PURE__ */ s.jsx("span", { children: "Autosave" })
        ] }),
        go ? /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => St(n, go, { definition: c.definition, draft: l }), children: [
          /* @__PURE__ */ s.jsx(Nt, { size: 15 }),
          " Risks"
        ] }) : null,
        Nn ? /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => St(n, Nn, { definition: c.definition, draft: l }), children: [
          /* @__PURE__ */ s.jsx(Nt, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ s.jsxs("button", { type: "button", title: "Export workflow as JSON", onClick: Mr, children: [
          /* @__PURE__ */ s.jsx(Ix, { size: 15 }),
          " Export"
        ] }),
        /* @__PURE__ */ s.jsxs("button", { type: "button", disabled: mt, onClick: () => {
          vo();
        }, children: [
          /* @__PURE__ */ s.jsx(Px, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ s.jsxs("button", { type: "button", disabled: mt, onClick: () => {
          $r();
        }, children: [
          /* @__PURE__ */ s.jsx(ou, { size: 15 }),
          " Promote"
        ] }),
        So ? /* @__PURE__ */ s.jsx(
          Nv,
          {
            testRun: So,
            onOpenDetails: () => {
              Le("runtime"), Ie(!1);
            }
          }
        ) : null,
        /* @__PURE__ */ s.jsxs(
          "button",
          {
            type: "button",
            disabled: !Er,
            title: l.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              bo();
            },
            children: [
              /* @__PURE__ */ s.jsx(Qn, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    D ? /* @__PURE__ */ s.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ s.jsx(qe, { size: 16 }),
      " ",
      D
    ] }) : null,
    /* @__PURE__ */ s.jsxs("div", { className: nd, style: od, children: [
      /* @__PURE__ */ s.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ s.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ s.jsx(
            Wo,
            {
              label: "Activities panel tabs",
              tabs: Or,
              activeTabId: ws.id,
              onSelect: at
            }
          ),
          /* @__PURE__ */ s.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ s.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": he ? "Expand activities panel" : "Collapse activities panel",
                title: he ? "Expand" : "Collapse",
                onClick: () => us("palette"),
                children: he ? /* @__PURE__ */ s.jsx(Qt, { size: 14 }) : /* @__PURE__ */ s.jsx(tr, { size: 14 })
              }
            ),
            he ? null : /* @__PURE__ */ s.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": me === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: me === "palette" ? "Restore" : "Maximize",
                onClick: () => ds("palette"),
                children: me === "palette" ? /* @__PURE__ */ s.jsx(Ma, { size: 14 }) : /* @__PURE__ */ s.jsx(nr, { size: 14 })
              }
            )
          ] })
        ] }),
        gs ? ws.render() : null
      ] }),
      gs && !me ? /* @__PURE__ */ s.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Dn,
          "aria-valuemax": Pn,
          "aria-valuenow": ee,
          tabIndex: 0,
          onPointerDown: (x) => hs("palette", x),
          onKeyDown: (x) => ps("palette", x)
        }
      ) : /* @__PURE__ */ s.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ s.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ s.jsx("div", { className: "wf-canvas-tabs", children: /* @__PURE__ */ s.jsx(
          Wo,
          {
            label: "Editor view tabs",
            tabs: sd,
            activeTabId: De,
            onSelect: (x) => ct(x)
          }
        ) }),
        De === "code" ? /* @__PURE__ */ s.jsx(d0, { draft: l, onApply: Dr }) : De === "properties" ? /* @__PURE__ */ s.jsx(y0, { details: c, draft: l }) : /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
          /* @__PURE__ */ s.jsxs("div", { className: "wf-breadcrumb", children: [
            /* @__PURE__ */ s.jsx("button", { type: "button", onClick: () => {
              v([]), $(null);
            }, children: "Root" }),
            y.map((x, C) => /* @__PURE__ */ s.jsxs(jt.Fragment, { children: [
              /* @__PURE__ */ s.jsx(Qt, { size: 13 }),
              /* @__PURE__ */ s.jsx("button", { type: "button", onClick: () => {
                v(y.slice(0, C + 1)), $(null);
              }, children: x.label })
            ] }, `${x.ownerNodeId}-${x.slotId}-${C}`))
          ] }),
          /* @__PURE__ */ s.jsxs("div", { className: "wf-canvas", ref: Ke, onDragOver: mo, onDragLeave: xo, onDrop: wo, children: [
            /* @__PURE__ */ s.jsx(Mu.Provider, { value: Ju, children: /* @__PURE__ */ s.jsxs(
              Yl,
              {
                nodes: N,
                edges: S,
                nodeTypes: Iu,
                edgeTypes: _u,
                onInit: X,
                onNodesChange: Rr,
                onEdgesChange: zr,
                onNodesDelete: Uu,
                onEdgesDelete: Zu,
                onConnect: Lr,
                onConnectStart: _e ? Ku : void 0,
                onConnectEnd: _e ? qu : void 0,
                onReconnect: _e ? Yu : void 0,
                isValidConnection: At,
                onDragOver: mo,
                onDragLeave: xo,
                onDrop: wo,
                onPaneClick: () => $(null),
                onNodeClick: (x, C) => $(C.id),
                onNodeDragStop: we ? void 0 : Vr,
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
                  /* @__PURE__ */ s.jsx(Zl, { gap: 18, size: 1 }),
                  /* @__PURE__ */ s.jsx(Jl, {}),
                  /* @__PURE__ */ s.jsx(eu, { pannable: !0, zoomable: !0 })
                ]
              }
            ) }),
            _e && N.length === 0 ? /* @__PURE__ */ s.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => Ar(), children: [
              /* @__PURE__ */ s.jsx(Gi, { size: 15 }),
              " Add activity"
            ] }) : null,
            H ? /* @__PURE__ */ s.jsx(
              vv,
              {
                clientX: H.clientX,
                clientY: H.clientY,
                activities: f,
                onPick: Gu,
                onClose: () => j(null)
              }
            ) : null
          ] }),
          /* @__PURE__ */ s.jsx(bv, { draft: l })
        ] })
      ] }),
      ys && !me ? /* @__PURE__ */ s.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Tn,
          "aria-valuemax": $n,
          "aria-valuenow": O,
          tabIndex: 0,
          onPointerDown: (x) => hs("inspector", x),
          onKeyDown: (x) => ps("inspector", x)
        }
      ) : /* @__PURE__ */ s.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ s.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ s.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ s.jsx(
            Wo,
            {
              label: "Inspector panel tabs",
              tabs: Hr,
              activeTabId: vs.id,
              onSelect: Le
            }
          ),
          /* @__PURE__ */ s.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ s.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": ke ? "Expand inspector panel" : "Collapse inspector panel",
                title: ke ? "Expand" : "Collapse",
                onClick: () => us("inspector"),
                children: ke ? /* @__PURE__ */ s.jsx(tr, { size: 14 }) : /* @__PURE__ */ s.jsx(Qt, { size: 14 })
              }
            ),
            ke ? null : /* @__PURE__ */ s.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": me === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: me === "inspector" ? "Restore" : "Maximize",
                onClick: () => ds("inspector"),
                children: me === "inspector" ? /* @__PURE__ */ s.jsx(Ma, { size: 14 }) : /* @__PURE__ */ s.jsx(nr, { size: 14 })
              }
            )
          ] })
        ] }),
        ys ? vs.render() : null
      ] })
    ] })
  ] });
}
function Wo({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: o
}) {
  return /* @__PURE__ */ s.jsx("div", { className: "wf-panel-tab-list", role: "tablist", "aria-label": e, children: t.map((r) => /* @__PURE__ */ s.jsxs(
    "button",
    {
      type: "button",
      role: "tab",
      "aria-selected": r.id === n,
      className: r.id === n ? "active" : "",
      title: r.title,
      onClick: () => o(r.id),
      children: [
        r.icon ? /* @__PURE__ */ s.jsx("span", { className: "wf-panel-tab-icon", "aria-hidden": "true", children: r.icon }) : null,
        /* @__PURE__ */ s.jsx("span", { children: r.title })
      ]
    },
    r.id
  )) });
}
function oc(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
function mv({ data: e, selected: t }) {
  const n = e, o = n.runtime, r = !n.suppressFlowPorts, i = r ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], a = xv(n);
  return /* @__PURE__ */ s.jsxs(
    "div",
    {
      className: ["wf-node", t ? "selected" : "", o ? "wf-node-runtime" : "", o?.hasBlockingIncident ? "faulted" : ""].filter(Boolean).join(" "),
      "data-icon": n.icon ?? "activity",
      children: [
        r && n.acceptsInbound ? /* @__PURE__ */ s.jsx(hn, { type: "target", position: ne.Left }) : null,
        /* @__PURE__ */ s.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ s.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: rs(n.icon) }),
          /* @__PURE__ */ s.jsxs("span", { className: "wf-node-copy", children: [
            /* @__PURE__ */ s.jsx("strong", { children: n.label }),
            a ? /* @__PURE__ */ s.jsx("small", { children: a }) : null
          ] })
        ] }),
        n.childSlots.length > 0 ? /* @__PURE__ */ s.jsxs("span", { className: "wf-node-slot-badge", children: [
          n.childSlots.length,
          " slot",
          n.childSlots.length === 1 ? "" : "s"
        ] }) : null,
        o ? /* @__PURE__ */ s.jsxs("div", { className: "wf-node-runtime-strip", children: [
          o.status ? /* @__PURE__ */ s.jsx(lo, { status: o.status, subStatus: o.subStatus }) : null,
          o.incidentCount > 0 ? /* @__PURE__ */ s.jsxs("span", { className: "wf-node-runtime-count", children: [
            o.incidentCount,
            " incident",
            o.incidentCount === 1 ? "" : "s"
          ] }) : null,
          o.faultCount > 0 ? /* @__PURE__ */ s.jsxs("span", { className: "wf-node-runtime-count", children: [
            o.faultCount,
            " faults"
          ] }) : null
        ] }) : null,
        i.map((c, u) => {
          const l = `${(u + 1) / (i.length + 1) * 100}%`;
          return /* @__PURE__ */ s.jsxs(jt.Fragment, { children: [
            /* @__PURE__ */ s.jsx("span", { className: "wf-node-port-label", style: { top: l }, children: c.displayName }),
            /* @__PURE__ */ s.jsx(hn, { type: "source", position: ne.Right, id: c.name, style: { top: l } })
          ] }, c.name);
        })
      ]
    }
  );
}
function xv(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((r) => !!r).join(" · ");
}
function wv(e) {
  const {
    id: t,
    sourceX: n,
    sourceY: o,
    targetX: r,
    targetY: i,
    sourcePosition: a,
    targetPosition: c,
    markerEnd: u,
    style: l,
    label: d,
    labelStyle: f
  } = e, h = jt.useContext(Mu), [p, g] = K(!1), [b, w, m] = Qo({ sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: a, targetPosition: c }), E = h?.highlightedEdgeId === t;
  return /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
    /* @__PURE__ */ s.jsx(
      ao,
      {
        id: t,
        path: b,
        markerEnd: u,
        style: {
          ...l,
          strokeWidth: E ? 2.5 : l?.strokeWidth
        },
        label: d,
        labelX: w,
        labelY: m,
        labelStyle: f,
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1)
      }
    ),
    h ? /* @__PURE__ */ s.jsx(Zm, { children: /* @__PURE__ */ s.jsxs(
      "div",
      {
        className: ["wf-edge-actions", p ? "visible" : "", E ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${w}px, ${m}px)` },
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1),
        children: [
          /* @__PURE__ */ s.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (y) => h.requestInsertActivity(t, y.clientX, y.clientY), children: /* @__PURE__ */ s.jsx(Gi, { size: 12 }) }),
          /* @__PURE__ */ s.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => h.deleteEdge(t), children: /* @__PURE__ */ s.jsx(bi, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function vv({ clientX: e, clientY: t, activities: n, onPick: o, onClose: r }) {
  const [i, a] = K(""), [c, u] = K(0), l = se(null), d = se(null), f = pe(() => {
    const E = i.trim().toLowerCase(), y = n.filter(Lu);
    return E ? y.filter((v) => je(v).toLowerCase().includes(E) || v.activityTypeKey.toLowerCase().includes(E) || (v.category ?? "").toLowerCase().includes(E) || (v.description ?? "").toLowerCase().includes(E)) : y;
  }, [n, i]), h = pe(() => _i(f), [f]), p = pe(() => h.flatMap((E) => E.activities), [h]);
  oe(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), oe(() => {
    const E = (v) => {
      l.current?.contains(v.target) || r();
    }, y = (v) => {
      v.key === "Escape" && r();
    };
    return document.addEventListener("mousedown", E, !0), document.addEventListener("keydown", y), () => {
      document.removeEventListener("mousedown", E, !0), document.removeEventListener("keydown", y);
    };
  }, [r]);
  const g = (E) => {
    if (E.key === "ArrowDown")
      E.preventDefault(), u((y) => Math.min(y + 1, p.length - 1));
    else if (E.key === "ArrowUp")
      E.preventDefault(), u((y) => Math.max(y - 1, 0));
    else if (E.key === "Enter") {
      E.preventDefault();
      const y = p[c];
      y && o(y);
    }
  }, b = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), w = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let m = -1;
  return /* @__PURE__ */ s.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: b, top: w }, onMouseDown: (E) => E.stopPropagation(), onClick: (E) => E.stopPropagation(), children: [
    /* @__PURE__ */ s.jsx(
      "input",
      {
        ref: d,
        type: "search",
        value: i,
        placeholder: "Search activities...",
        "aria-label": "Search activities",
        onChange: (E) => {
          a(E.target.value), u(0);
        },
        onKeyDown: g
      }
    ),
    /* @__PURE__ */ s.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: h.length === 0 ? /* @__PURE__ */ s.jsx("p", { children: "No matching activities." }) : h.map((E) => /* @__PURE__ */ s.jsxs("section", { children: [
      /* @__PURE__ */ s.jsx("h4", { children: E.category }),
      E.activities.map((y) => {
        m += 1;
        const v = m, N = v === c;
        return /* @__PURE__ */ s.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": N,
            className: N ? "active" : "",
            onMouseEnter: () => u(v),
            onClick: () => o(y),
            children: [
              /* @__PURE__ */ s.jsx("strong", { children: je(y) }),
              /* @__PURE__ */ s.jsx("small", { children: y.category || y.activityTypeKey })
            ]
          },
          y.activityVersionId
        );
      })
    ] }, E.category)) })
  ] });
}
function bv({ draft: e }) {
  return e.validationErrors.length ? /* @__PURE__ */ s.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ s.jsx(qe, { size: 14 }),
    e.validationErrors.length,
    " validation issue",
    e.validationErrors.length === 1 ? "" : "s"
  ] }) : /* @__PURE__ */ s.jsxs("div", { className: "wf-validation ok", children: [
    /* @__PURE__ */ s.jsx(yn, { size: 14 }),
    " No validation errors"
  ] });
}
function Nv({
  testRun: e,
  onOpenDetails: t
}) {
  const n = as(e);
  return /* @__PURE__ */ s.jsx("div", { className: "wf-test-run-status", "data-state": n ? "rejected" : "accepted", children: /* @__PURE__ */ s.jsxs(
    "button",
    {
      type: "button",
      className: "wf-test-run-trigger",
      onClick: t,
      children: [
        n ? /* @__PURE__ */ s.jsx(qe, { size: 16 }) : /* @__PURE__ */ s.jsx(yn, { size: 16 }),
        n ? "Test run rejected" : "Test run dispatched"
      ]
    }
  ) });
}
function Sv({ testRun: e, onOpenRun: t }) {
  if (!e)
    return /* @__PURE__ */ s.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ s.jsx("div", { className: "wf-empty", children: "Run the draft to see Runtime Evidence." }) });
  const n = as(e), o = e.workflowExecutionId;
  return /* @__PURE__ */ s.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ s.jsxs("section", { className: "wf-runtime-card", "data-state": n ? "rejected" : "accepted", children: [
    /* @__PURE__ */ s.jsxs("header", { children: [
      /* @__PURE__ */ s.jsxs("div", { children: [
        /* @__PURE__ */ s.jsx("span", { children: "Latest Test Run" }),
        /* @__PURE__ */ s.jsx("h3", { children: n ? "Rejected by the server" : "Transient run accepted" })
      ] }),
      /* @__PURE__ */ s.jsx(lo, { status: e.status, subStatus: e.commandDispatchStatus ?? void 0 })
    ] }),
    /* @__PURE__ */ s.jsx("p", { children: "Ephemeral - not saved, promoted, or published." }),
    n && e.reason ? /* @__PURE__ */ s.jsxs("div", { className: "wf-runtime-reason", children: [
      /* @__PURE__ */ s.jsx(qe, { size: 14 }),
      " ",
      e.reason
    ] }) : null,
    /* @__PURE__ */ s.jsxs("dl", { className: "wf-runtime-meta", children: [
      /* @__PURE__ */ s.jsxs("div", { children: [
        /* @__PURE__ */ s.jsx("dt", { children: "Dispatch" }),
        /* @__PURE__ */ s.jsx("dd", { title: e.commandDispatchStatus ?? e.status, children: e.commandDispatchStatus ?? e.status })
      ] }),
      /* @__PURE__ */ s.jsxs("div", { children: [
        /* @__PURE__ */ s.jsx("dt", { children: "Test Run" }),
        /* @__PURE__ */ s.jsx("dd", { title: e.testRunId, children: e.testRunId })
      ] }),
      /* @__PURE__ */ s.jsxs("div", { children: [
        /* @__PURE__ */ s.jsx("dt", { children: "Artifact" }),
        /* @__PURE__ */ s.jsx("dd", { title: e.artifactId ?? "None", children: e.artifactId ?? "None" })
      ] }),
      /* @__PURE__ */ s.jsxs("div", { children: [
        /* @__PURE__ */ s.jsx("dt", { children: "Run / Instance" }),
        /* @__PURE__ */ s.jsx("dd", { title: o ?? "None", children: o ? /* @__PURE__ */ s.jsx("button", { type: "button", onClick: () => t(o), children: o }) : "None" })
      ] }),
      /* @__PURE__ */ s.jsxs("div", { children: [
        /* @__PURE__ */ s.jsx("dt", { children: "Activities" }),
        /* @__PURE__ */ s.jsx("dd", { children: rc(e.activityCount, "activity") })
      ] }),
      /* @__PURE__ */ s.jsxs("div", { children: [
        /* @__PURE__ */ s.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ s.jsx("dd", { children: rc(e.incidentCount, "incident") })
      ] }),
      /* @__PURE__ */ s.jsxs("div", { children: [
        /* @__PURE__ */ s.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ s.jsx("dd", { title: e.expiresAt ? et(e.expiresAt) : "None", children: e.expiresAt ? et(e.expiresAt) : "None" })
      ] })
    ] })
  ] }) });
}
function rc(e, t) {
  return typeof e != "number" ? "Available on linked Run" : `${e} ${t}${e === 1 ? "" : "s"}`;
}
function ic(e) {
  return `${je(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function sc(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function jv(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function Fu(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function Ev(e) {
  const t = Fu(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function kv(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function Fe(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function Cv(e) {
  return Bu(Fe(e));
}
function Wu(e, t, n = []) {
  if (!e) return n;
  const o = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: o?.activityTypeKey ?? e.activityVersionId,
    displayName: o ? je(o) : void 0
  });
  for (const r of Qe(e))
    for (const i of r.activities) Wu(i, t, n);
  return n;
}
function zn(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function Iv(e) {
  return `${e.id}-${Bu(JSON.stringify(e.state))}`;
}
function Bu(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function as(e) {
  return e.status.toLowerCase() === "rejected";
}
function _v(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function Av(e, t) {
  const n = e instanceof Error ? e.message : String(e);
  return Mv(e, n) ? `Run ${t} was not found.` : n;
}
function Mv(e, t) {
  if ((typeof e == "object" && e ? e.response?.status ?? e.status : void 0) === 404 || /\b404\b/.test(t)) return !0;
  try {
    const o = JSON.parse(t);
    return [o.error, o.title, o.detail].some((r) => typeof r == "string" && /not found/i.test(r));
  } catch {
    return /not found/i.test(t);
  }
}
export {
  Ev as isConnectEndOverExistingWorkflowNode,
  Tv as register,
  kv as resolveConnectEndSource
};
