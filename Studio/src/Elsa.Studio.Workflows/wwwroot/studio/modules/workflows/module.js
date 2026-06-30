import ct, { memo as Se, forwardRef as kr, useRef as ie, useEffect as ee, useCallback as ue, useContext as ho, useMemo as de, useState as X, createContext as Zi, useLayoutEffect as ff, createElement as Si, useId as Oc, lazy as hf, Suspense as pf } from "react";
import { useQuery as Hc, useQueryClient as yf, useMutation as gf } from "@tanstack/react-query";
function mf(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var ii = { exports: {} }, Rn = {};
var qs;
function xf() {
  if (qs) return Rn;
  qs = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function n(o, r, s) {
    var a = null;
    if (s !== void 0 && (a = "" + s), r.key !== void 0 && (a = "" + r.key), "key" in r) {
      s = {};
      for (var c in r)
        c !== "key" && (s[c] = r[c]);
    } else s = r;
    return r = s.ref, {
      $$typeof: e,
      type: o,
      key: a,
      ref: r !== void 0 ? r : null,
      props: s
    };
  }
  return Rn.Fragment = t, Rn.jsx = n, Rn.jsxs = n, Rn;
}
var Us;
function wf() {
  return Us || (Us = 1, ii.exports = xf()), ii.exports;
}
var i = wf();
function Ee(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, o; n < e.length; n++)
      (o = Ee(e[n])) !== "" && (t += (t && " ") + o);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var vf = { value: () => {
} };
function Er() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new Uo(n);
}
function Uo(e) {
  this._ = e;
}
function bf(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
Uo.prototype = Er.prototype = {
  constructor: Uo,
  on: function(e, t) {
    var n = this._, o = bf(e + "", n), r, s = -1, a = o.length;
    if (arguments.length < 2) {
      for (; ++s < a; ) if ((r = (e = o[s]).type) && (r = Nf(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++s < a; )
      if (r = (e = o[s]).type) n[r] = Ys(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = Ys(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new Uo(e);
  },
  call: function(e, t) {
    if ((r = arguments.length - 2) > 0) for (var n = new Array(r), o = 0, r, s; o < r; ++o) n[o] = arguments[o + 2];
    if (!this._.hasOwnProperty(e)) throw new Error("unknown type: " + e);
    for (s = this._[e], o = 0, r = s.length; o < r; ++o) s[o].value.apply(t, n);
  },
  apply: function(e, t, n) {
    if (!this._.hasOwnProperty(e)) throw new Error("unknown type: " + e);
    for (var o = this._[e], r = 0, s = o.length; r < s; ++r) o[r].value.apply(t, n);
  }
};
function Nf(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function Ys(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = vf, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var ki = "http://www.w3.org/1999/xhtml";
const Zs = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: ki,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function Cr(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Zs.hasOwnProperty(t) ? { space: Zs[t], local: e } : e;
}
function jf(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === ki && t.documentElement.namespaceURI === ki ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Sf(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function Wc(e) {
  var t = Cr(e);
  return (t.local ? Sf : jf)(t);
}
function kf() {
}
function Gi(e) {
  return e == null ? kf : function() {
    return this.querySelector(e);
  };
}
function Ef(e) {
  typeof e != "function" && (e = Gi(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = o[r] = new Array(a), u, l, d = 0; d < a; ++d)
      (u = s[d]) && (l = e.call(u, u.__data__, d, s)) && ("__data__" in u && (l.__data__ = u.__data__), c[d] = l);
  return new Ue(o, this._parents);
}
function Cf(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function If() {
  return [];
}
function Bc(e) {
  return e == null ? If : function() {
    return this.querySelectorAll(e);
  };
}
function Af(e) {
  return function() {
    return Cf(e.apply(this, arguments));
  };
}
function _f(e) {
  typeof e == "function" ? e = Af(e) : e = Bc(e);
  for (var t = this._groups, n = t.length, o = [], r = [], s = 0; s < n; ++s)
    for (var a = t[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && (o.push(e.call(u, u.__data__, l, a)), r.push(u));
  return new Ue(o, r);
}
function Fc(e) {
  return function() {
    return this.matches(e);
  };
}
function Kc(e) {
  return function(t) {
    return t.matches(e);
  };
}
var Df = Array.prototype.find;
function Mf(e) {
  return function() {
    return Df.call(this.children, e);
  };
}
function Tf() {
  return this.firstElementChild;
}
function $f(e) {
  return this.select(e == null ? Tf : Mf(typeof e == "function" ? e : Kc(e)));
}
var Pf = Array.prototype.filter;
function zf() {
  return Array.from(this.children);
}
function Lf(e) {
  return function() {
    return Pf.call(this.children, e);
  };
}
function Rf(e) {
  return this.selectAll(e == null ? zf : Lf(typeof e == "function" ? e : Kc(e)));
}
function Vf(e) {
  typeof e != "function" && (e = Fc(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = o[r] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new Ue(o, this._parents);
}
function Xc(e) {
  return new Array(e.length);
}
function Of() {
  return new Ue(this._enter || this._groups.map(Xc), this._parents);
}
function rr(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
rr.prototype = {
  constructor: rr,
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
function Hf(e) {
  return function() {
    return e;
  };
}
function Wf(e, t, n, o, r, s) {
  for (var a = 0, c, u = t.length, l = s.length; a < l; ++a)
    (c = t[a]) ? (c.__data__ = s[a], o[a] = c) : n[a] = new rr(e, s[a]);
  for (; a < u; ++a)
    (c = t[a]) && (r[a] = c);
}
function Bf(e, t, n, o, r, s, a) {
  var c, u, l = /* @__PURE__ */ new Map(), d = t.length, f = s.length, h = new Array(d), p;
  for (c = 0; c < d; ++c)
    (u = t[c]) && (h[c] = p = a.call(u, u.__data__, c, t) + "", l.has(p) ? r[c] = u : l.set(p, u));
  for (c = 0; c < f; ++c)
    p = a.call(e, s[c], c, s) + "", (u = l.get(p)) ? (o[c] = u, u.__data__ = s[c], l.delete(p)) : n[c] = new rr(e, s[c]);
  for (c = 0; c < d; ++c)
    (u = t[c]) && l.get(h[c]) === u && (r[c] = u);
}
function Ff(e) {
  return e.__data__;
}
function Kf(e, t) {
  if (!arguments.length) return Array.from(this, Ff);
  var n = t ? Bf : Wf, o = this._parents, r = this._groups;
  typeof e != "function" && (e = Hf(e));
  for (var s = r.length, a = new Array(s), c = new Array(s), u = new Array(s), l = 0; l < s; ++l) {
    var d = o[l], f = r[l], h = f.length, p = Xf(e.call(d, d && d.__data__, l, o)), y = p.length, v = c[l] = new Array(y), x = a[l] = new Array(y), m = u[l] = new Array(h);
    n(d, f, v, x, m, p, t);
    for (var E = 0, g = 0, b, j; E < y; ++E)
      if (b = v[E]) {
        for (E >= g && (g = E + 1); !(j = x[g]) && ++g < y; ) ;
        b._next = j || null;
      }
  }
  return a = new Ue(a, o), a._enter = c, a._exit = u, a;
}
function Xf(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function qf() {
  return new Ue(this._exit || this._groups.map(Xc), this._parents);
}
function Uf(e, t, n) {
  var o = this.enter(), r = this, s = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? s.remove() : n(s), o && r ? o.merge(r).order() : r;
}
function Yf(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, s = o.length, a = Math.min(r, s), c = new Array(r), u = 0; u < a; ++u)
    for (var l = n[u], d = o[u], f = l.length, h = c[u] = new Array(f), p, y = 0; y < f; ++y)
      (p = l[y] || d[y]) && (h[y] = p);
  for (; u < r; ++u)
    c[u] = n[u];
  return new Ue(c, this._parents);
}
function Zf() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, s = o[r], a; --r >= 0; )
      (a = o[r]) && (s && a.compareDocumentPosition(s) ^ 4 && s.parentNode.insertBefore(a, s), s = a);
  return this;
}
function Gf(e) {
  e || (e = Jf);
  function t(f, h) {
    return f && h ? e(f.__data__, h.__data__) : !f - !h;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), s = 0; s < o; ++s) {
    for (var a = n[s], c = a.length, u = r[s] = new Array(c), l, d = 0; d < c; ++d)
      (l = a[d]) && (u[d] = l);
    u.sort(t);
  }
  return new Ue(r, this._parents).order();
}
function Jf(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function Qf() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function eh() {
  return Array.from(this);
}
function th() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, s = o.length; r < s; ++r) {
      var a = o[r];
      if (a) return a;
    }
  return null;
}
function nh() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function oh() {
  return !this.node();
}
function rh(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], s = 0, a = r.length, c; s < a; ++s)
      (c = r[s]) && e.call(c, c.__data__, s, r);
  return this;
}
function ih(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function sh(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function ah(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function ch(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function lh(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function uh(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function dh(e, t) {
  var n = Cr(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? sh : ih : typeof t == "function" ? n.local ? uh : lh : n.local ? ch : ah)(n, t));
}
function qc(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function fh(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function hh(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function ph(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function yh(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? fh : typeof t == "function" ? ph : hh)(e, t, n ?? "")) : un(this.node(), e);
}
function un(e, t) {
  return e.style.getPropertyValue(t) || qc(e).getComputedStyle(e, null).getPropertyValue(t);
}
function gh(e) {
  return function() {
    delete this[e];
  };
}
function mh(e, t) {
  return function() {
    this[e] = t;
  };
}
function xh(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function wh(e, t) {
  return arguments.length > 1 ? this.each((t == null ? gh : typeof t == "function" ? xh : mh)(e, t)) : this.node()[e];
}
function Uc(e) {
  return e.trim().split(/^|\s+/);
}
function Ji(e) {
  return e.classList || new Yc(e);
}
function Yc(e) {
  this._node = e, this._names = Uc(e.getAttribute("class") || "");
}
Yc.prototype = {
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
function Zc(e, t) {
  for (var n = Ji(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function Gc(e, t) {
  for (var n = Ji(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function vh(e) {
  return function() {
    Zc(this, e);
  };
}
function bh(e) {
  return function() {
    Gc(this, e);
  };
}
function Nh(e, t) {
  return function() {
    (t.apply(this, arguments) ? Zc : Gc)(this, e);
  };
}
function jh(e, t) {
  var n = Uc(e + "");
  if (arguments.length < 2) {
    for (var o = Ji(this.node()), r = -1, s = n.length; ++r < s; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? Nh : t ? vh : bh)(n, t));
}
function Sh() {
  this.textContent = "";
}
function kh(e) {
  return function() {
    this.textContent = e;
  };
}
function Eh(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function Ch(e) {
  return arguments.length ? this.each(e == null ? Sh : (typeof e == "function" ? Eh : kh)(e)) : this.node().textContent;
}
function Ih() {
  this.innerHTML = "";
}
function Ah(e) {
  return function() {
    this.innerHTML = e;
  };
}
function _h(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function Dh(e) {
  return arguments.length ? this.each(e == null ? Ih : (typeof e == "function" ? _h : Ah)(e)) : this.node().innerHTML;
}
function Mh() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function Th() {
  return this.each(Mh);
}
function $h() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Ph() {
  return this.each($h);
}
function zh(e) {
  var t = typeof e == "function" ? e : Wc(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Lh() {
  return null;
}
function Rh(e, t) {
  var n = typeof e == "function" ? e : Wc(e), o = t == null ? Lh : typeof t == "function" ? t : Gi(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function Vh() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Oh() {
  return this.each(Vh);
}
function Hh() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Wh() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Bh(e) {
  return this.select(e ? Wh : Hh);
}
function Fh(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function Kh(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function Xh(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function qh(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, s; n < r; ++n)
        s = t[n], (!e.type || s.type === e.type) && s.name === e.name ? this.removeEventListener(s.type, s.listener, s.options) : t[++o] = s;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function Uh(e, t, n) {
  return function() {
    var o = this.__on, r, s = Kh(t);
    if (o) {
      for (var a = 0, c = o.length; a < c; ++a)
        if ((r = o[a]).type === e.type && r.name === e.name) {
          this.removeEventListener(r.type, r.listener, r.options), this.addEventListener(r.type, r.listener = s, r.options = n), r.value = t;
          return;
        }
    }
    this.addEventListener(e.type, s, n), r = { type: e.type, name: e.name, value: t, listener: s, options: n }, o ? o.push(r) : this.__on = [r];
  };
}
function Yh(e, t, n) {
  var o = Xh(e + ""), r, s = o.length, a;
  if (arguments.length < 2) {
    var c = this.node().__on;
    if (c) {
      for (var u = 0, l = c.length, d; u < l; ++u)
        for (r = 0, d = c[u]; r < s; ++r)
          if ((a = o[r]).type === d.type && a.name === d.name)
            return d.value;
    }
    return;
  }
  for (c = t ? Uh : qh, r = 0; r < s; ++r) this.each(c(o[r], t, n));
  return this;
}
function Jc(e, t, n) {
  var o = qc(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function Zh(e, t) {
  return function() {
    return Jc(this, e, t);
  };
}
function Gh(e, t) {
  return function() {
    return Jc(this, e, t.apply(this, arguments));
  };
}
function Jh(e, t) {
  return this.each((typeof t == "function" ? Gh : Zh)(e, t));
}
function* Qh() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, s = o.length, a; r < s; ++r)
      (a = o[r]) && (yield a);
}
var Qc = [null];
function Ue(e, t) {
  this._groups = e, this._parents = t;
}
function po() {
  return new Ue([[document.documentElement]], Qc);
}
function ep() {
  return this;
}
Ue.prototype = po.prototype = {
  constructor: Ue,
  select: Ef,
  selectAll: _f,
  selectChild: $f,
  selectChildren: Rf,
  filter: Vf,
  data: Kf,
  enter: Of,
  exit: qf,
  join: Uf,
  merge: Yf,
  selection: ep,
  order: Zf,
  sort: Gf,
  call: Qf,
  nodes: eh,
  node: th,
  size: nh,
  empty: oh,
  each: rh,
  attr: dh,
  style: yh,
  property: wh,
  classed: jh,
  text: Ch,
  html: Dh,
  raise: Th,
  lower: Ph,
  append: zh,
  insert: Rh,
  remove: Oh,
  clone: Bh,
  datum: Fh,
  on: Yh,
  dispatch: Jh,
  [Symbol.iterator]: Qh
};
function qe(e) {
  return typeof e == "string" ? new Ue([[document.querySelector(e)]], [document.documentElement]) : new Ue([[e]], Qc);
}
function tp(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function et(e, t) {
  if (e = tp(e), t === void 0 && (t = e.currentTarget), t) {
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
const np = { passive: !1 }, eo = { capture: !0, passive: !1 };
function si(e) {
  e.stopImmediatePropagation();
}
function an(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function el(e) {
  var t = e.document.documentElement, n = qe(e).on("dragstart.drag", an, eo);
  "onselectstart" in t ? n.on("selectstart.drag", an, eo) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function tl(e, t) {
  var n = e.document.documentElement, o = qe(e).on("dragstart.drag", null);
  t && (o.on("click.drag", an, eo), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const Lo = (e) => () => e;
function Ei(e, {
  sourceEvent: t,
  subject: n,
  target: o,
  identifier: r,
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
    identifier: { value: r, enumerable: !0, configurable: !0 },
    active: { value: s, enumerable: !0, configurable: !0 },
    x: { value: a, enumerable: !0, configurable: !0 },
    y: { value: c, enumerable: !0, configurable: !0 },
    dx: { value: u, enumerable: !0, configurable: !0 },
    dy: { value: l, enumerable: !0, configurable: !0 },
    _: { value: d }
  });
}
Ei.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function op(e) {
  return !e.ctrlKey && !e.button;
}
function rp() {
  return this.parentNode;
}
function ip(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function sp() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function nl() {
  var e = op, t = rp, n = ip, o = sp, r = {}, s = Er("start", "drag", "end"), a = 0, c, u, l, d, f = 0;
  function h(b) {
    b.on("mousedown.drag", p).filter(o).on("touchstart.drag", x).on("touchmove.drag", m, np).on("touchend.drag touchcancel.drag", E).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function p(b, j) {
    if (!(d || !e.call(this, b, j))) {
      var k = g(this, t.call(this, b, j), b, j, "mouse");
      k && (qe(b.view).on("mousemove.drag", y, eo).on("mouseup.drag", v, eo), el(b.view), si(b), l = !1, c = b.clientX, u = b.clientY, k("start", b));
    }
  }
  function y(b) {
    if (an(b), !l) {
      var j = b.clientX - c, k = b.clientY - u;
      l = j * j + k * k > f;
    }
    r.mouse("drag", b);
  }
  function v(b) {
    qe(b.view).on("mousemove.drag mouseup.drag", null), tl(b.view, l), an(b), r.mouse("end", b);
  }
  function x(b, j) {
    if (e.call(this, b, j)) {
      var k = b.changedTouches, S = t.call(this, b, j), _ = k.length, M, W;
      for (M = 0; M < _; ++M)
        (W = g(this, S, b, j, k[M].identifier, k[M])) && (si(b), W("start", b, k[M]));
    }
  }
  function m(b) {
    var j = b.changedTouches, k = j.length, S, _;
    for (S = 0; S < k; ++S)
      (_ = r[j[S].identifier]) && (an(b), _("drag", b, j[S]));
  }
  function E(b) {
    var j = b.changedTouches, k = j.length, S, _;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), S = 0; S < k; ++S)
      (_ = r[j[S].identifier]) && (si(b), _("end", b, j[S]));
  }
  function g(b, j, k, S, _, M) {
    var W = s.copy(), D = et(M || k, j), T, R, N;
    if ((N = n.call(b, new Ei("beforestart", {
      sourceEvent: k,
      target: h,
      identifier: _,
      active: a,
      x: D[0],
      y: D[1],
      dx: 0,
      dy: 0,
      dispatch: W
    }), S)) != null)
      return T = N.x - D[0] || 0, R = N.y - D[1] || 0, function A(I, $, z) {
        var P = D, B;
        switch (I) {
          case "start":
            r[_] = A, B = a++;
            break;
          case "end":
            delete r[_], --a;
          // falls through
          case "drag":
            D = et(z || $, j), B = a;
            break;
        }
        W.call(
          I,
          b,
          new Ei(I, {
            sourceEvent: $,
            subject: N,
            target: h,
            identifier: _,
            active: B,
            x: D[0] + T,
            y: D[1] + R,
            dx: D[0] - P[0],
            dy: D[1] - P[1],
            dispatch: W
          }),
          S
        );
      };
  }
  return h.filter = function(b) {
    return arguments.length ? (e = typeof b == "function" ? b : Lo(!!b), h) : e;
  }, h.container = function(b) {
    return arguments.length ? (t = typeof b == "function" ? b : Lo(b), h) : t;
  }, h.subject = function(b) {
    return arguments.length ? (n = typeof b == "function" ? b : Lo(b), h) : n;
  }, h.touchable = function(b) {
    return arguments.length ? (o = typeof b == "function" ? b : Lo(!!b), h) : o;
  }, h.on = function() {
    var b = s.on.apply(s, arguments);
    return b === s ? h : b;
  }, h.clickDistance = function(b) {
    return arguments.length ? (f = (b = +b) * b, h) : Math.sqrt(f);
  }, h;
}
function Qi(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function ol(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function yo() {
}
var to = 0.7, ir = 1 / to, cn = "\\s*([+-]?\\d+)\\s*", no = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", at = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", ap = /^#([0-9a-f]{3,8})$/, cp = new RegExp(`^rgb\\(${cn},${cn},${cn}\\)$`), lp = new RegExp(`^rgb\\(${at},${at},${at}\\)$`), up = new RegExp(`^rgba\\(${cn},${cn},${cn},${no}\\)$`), dp = new RegExp(`^rgba\\(${at},${at},${at},${no}\\)$`), fp = new RegExp(`^hsl\\(${no},${at},${at}\\)$`), hp = new RegExp(`^hsla\\(${no},${at},${at},${no}\\)$`), Gs = {
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
Qi(yo, Ot, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Js,
  // Deprecated! Use color.formatHex.
  formatHex: Js,
  formatHex8: pp,
  formatHsl: yp,
  formatRgb: Qs,
  toString: Qs
});
function Js() {
  return this.rgb().formatHex();
}
function pp() {
  return this.rgb().formatHex8();
}
function yp() {
  return rl(this).formatHsl();
}
function Qs() {
  return this.rgb().formatRgb();
}
function Ot(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = ap.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? ea(t) : n === 3 ? new Re(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? Ro(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? Ro(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = cp.exec(e)) ? new Re(t[1], t[2], t[3], 1) : (t = lp.exec(e)) ? new Re(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = up.exec(e)) ? Ro(t[1], t[2], t[3], t[4]) : (t = dp.exec(e)) ? Ro(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = fp.exec(e)) ? oa(t[1], t[2] / 100, t[3] / 100, 1) : (t = hp.exec(e)) ? oa(t[1], t[2] / 100, t[3] / 100, t[4]) : Gs.hasOwnProperty(e) ? ea(Gs[e]) : e === "transparent" ? new Re(NaN, NaN, NaN, 0) : null;
}
function ea(e) {
  return new Re(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function Ro(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new Re(e, t, n, o);
}
function gp(e) {
  return e instanceof yo || (e = Ot(e)), e ? (e = e.rgb(), new Re(e.r, e.g, e.b, e.opacity)) : new Re();
}
function Ci(e, t, n, o) {
  return arguments.length === 1 ? gp(e) : new Re(e, t, n, o ?? 1);
}
function Re(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
Qi(Re, Ci, ol(yo, {
  brighter(e) {
    return e = e == null ? ir : Math.pow(ir, e), new Re(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? to : Math.pow(to, e), new Re(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new Re(zt(this.r), zt(this.g), zt(this.b), sr(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: ta,
  // Deprecated! Use color.formatHex.
  formatHex: ta,
  formatHex8: mp,
  formatRgb: na,
  toString: na
}));
function ta() {
  return `#${Pt(this.r)}${Pt(this.g)}${Pt(this.b)}`;
}
function mp() {
  return `#${Pt(this.r)}${Pt(this.g)}${Pt(this.b)}${Pt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function na() {
  const e = sr(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${zt(this.r)}, ${zt(this.g)}, ${zt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function sr(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function zt(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function Pt(e) {
  return e = zt(e), (e < 16 ? "0" : "") + e.toString(16);
}
function oa(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new tt(e, t, n, o);
}
function rl(e) {
  if (e instanceof tt) return new tt(e.h, e.s, e.l, e.opacity);
  if (e instanceof yo || (e = Ot(e)), !e) return new tt();
  if (e instanceof tt) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), s = Math.max(t, n, o), a = NaN, c = s - r, u = (s + r) / 2;
  return c ? (t === s ? a = (n - o) / c + (n < o) * 6 : n === s ? a = (o - t) / c + 2 : a = (t - n) / c + 4, c /= u < 0.5 ? s + r : 2 - s - r, a *= 60) : c = u > 0 && u < 1 ? 0 : a, new tt(a, c, u, e.opacity);
}
function xp(e, t, n, o) {
  return arguments.length === 1 ? rl(e) : new tt(e, t, n, o ?? 1);
}
function tt(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
Qi(tt, xp, ol(yo, {
  brighter(e) {
    return e = e == null ? ir : Math.pow(ir, e), new tt(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? to : Math.pow(to, e), new tt(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new Re(
      ai(e >= 240 ? e - 240 : e + 120, r, o),
      ai(e, r, o),
      ai(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new tt(ra(this.h), Vo(this.s), Vo(this.l), sr(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = sr(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${ra(this.h)}, ${Vo(this.s) * 100}%, ${Vo(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function ra(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function Vo(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function ai(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const es = (e) => () => e;
function wp(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function vp(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function bp(e) {
  return (e = +e) == 1 ? il : function(t, n) {
    return n - t ? vp(t, n, e) : es(isNaN(t) ? n : t);
  };
}
function il(e, t) {
  var n = t - e;
  return n ? wp(e, n) : es(isNaN(e) ? t : e);
}
const ar = (function e(t) {
  var n = bp(t);
  function o(r, s) {
    var a = n((r = Ci(r)).r, (s = Ci(s)).r), c = n(r.g, s.g), u = n(r.b, s.b), l = il(r.opacity, s.opacity);
    return function(d) {
      return r.r = a(d), r.g = c(d), r.b = u(d), r.opacity = l(d), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function Np(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(s) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - s) + t[r] * s;
    return o;
  };
}
function jp(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Sp(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), s = new Array(n), a;
  for (a = 0; a < o; ++a) r[a] = Zn(e[a], t[a]);
  for (; a < n; ++a) s[a] = t[a];
  return function(c) {
    for (a = 0; a < o; ++a) s[a] = r[a](c);
    return s;
  };
}
function kp(e, t) {
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
function Ep(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = Zn(e[r], t[r]) : o[r] = t[r];
  return function(s) {
    for (r in n) o[r] = n[r](s);
    return o;
  };
}
var Ii = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, ci = new RegExp(Ii.source, "g");
function Cp(e) {
  return function() {
    return e;
  };
}
function Ip(e) {
  return function(t) {
    return e(t) + "";
  };
}
function sl(e, t) {
  var n = Ii.lastIndex = ci.lastIndex = 0, o, r, s, a = -1, c = [], u = [];
  for (e = e + "", t = t + ""; (o = Ii.exec(e)) && (r = ci.exec(t)); )
    (s = r.index) > n && (s = t.slice(n, s), c[a] ? c[a] += s : c[++a] = s), (o = o[0]) === (r = r[0]) ? c[a] ? c[a] += r : c[++a] = r : (c[++a] = null, u.push({ i: a, x: st(o, r) })), n = ci.lastIndex;
  return n < t.length && (s = t.slice(n), c[a] ? c[a] += s : c[++a] = s), c.length < 2 ? u[0] ? Ip(u[0].x) : Cp(t) : (t = u.length, function(l) {
    for (var d = 0, f; d < t; ++d) c[(f = u[d]).i] = f.x(l);
    return c.join("");
  });
}
function Zn(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? es(t) : (n === "number" ? st : n === "string" ? (o = Ot(t)) ? (t = o, ar) : sl : t instanceof Ot ? ar : t instanceof Date ? kp : jp(t) ? Np : Array.isArray(t) ? Sp : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Ep : st)(e, t);
}
var ia = 180 / Math.PI, Ai = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function al(e, t, n, o, r, s) {
  var a, c, u;
  return (a = Math.sqrt(e * e + t * t)) && (e /= a, t /= a), (u = e * n + t * o) && (n -= e * u, o -= t * u), (c = Math.sqrt(n * n + o * o)) && (n /= c, o /= c, u /= c), e * o < t * n && (e = -e, t = -t, u = -u, a = -a), {
    translateX: r,
    translateY: s,
    rotate: Math.atan2(t, e) * ia,
    skewX: Math.atan(u) * ia,
    scaleX: a,
    scaleY: c
  };
}
var Oo;
function Ap(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Ai : al(t.a, t.b, t.c, t.d, t.e, t.f);
}
function _p(e) {
  return e == null || (Oo || (Oo = document.createElementNS("http://www.w3.org/2000/svg", "g")), Oo.setAttribute("transform", e), !(e = Oo.transform.baseVal.consolidate())) ? Ai : (e = e.matrix, al(e.a, e.b, e.c, e.d, e.e, e.f));
}
function cl(e, t, n, o) {
  function r(l) {
    return l.length ? l.pop() + " " : "";
  }
  function s(l, d, f, h, p, y) {
    if (l !== f || d !== h) {
      var v = p.push("translate(", null, t, null, n);
      y.push({ i: v - 4, x: st(l, f) }, { i: v - 2, x: st(d, h) });
    } else (f || h) && p.push("translate(" + f + t + h + n);
  }
  function a(l, d, f, h) {
    l !== d ? (l - d > 180 ? d += 360 : d - l > 180 && (l += 360), h.push({ i: f.push(r(f) + "rotate(", null, o) - 2, x: st(l, d) })) : d && f.push(r(f) + "rotate(" + d + o);
  }
  function c(l, d, f, h) {
    l !== d ? h.push({ i: f.push(r(f) + "skewX(", null, o) - 2, x: st(l, d) }) : d && f.push(r(f) + "skewX(" + d + o);
  }
  function u(l, d, f, h, p, y) {
    if (l !== f || d !== h) {
      var v = p.push(r(p) + "scale(", null, ",", null, ")");
      y.push({ i: v - 4, x: st(l, f) }, { i: v - 2, x: st(d, h) });
    } else (f !== 1 || h !== 1) && p.push(r(p) + "scale(" + f + "," + h + ")");
  }
  return function(l, d) {
    var f = [], h = [];
    return l = e(l), d = e(d), s(l.translateX, l.translateY, d.translateX, d.translateY, f, h), a(l.rotate, d.rotate, f, h), c(l.skewX, d.skewX, f, h), u(l.scaleX, l.scaleY, d.scaleX, d.scaleY, f, h), l = d = null, function(p) {
      for (var y = -1, v = h.length, x; ++y < v; ) f[(x = h[y]).i] = x.x(p);
      return f.join("");
    };
  };
}
var Dp = cl(Ap, "px, ", "px)", "deg)"), Mp = cl(_p, ", ", ")", ")"), Tp = 1e-12;
function sa(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function $p(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Pp(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const Yo = (function e(t, n, o) {
  function r(s, a) {
    var c = s[0], u = s[1], l = s[2], d = a[0], f = a[1], h = a[2], p = d - c, y = f - u, v = p * p + y * y, x, m;
    if (v < Tp)
      m = Math.log(h / l) / t, x = function(S) {
        return [
          c + S * p,
          u + S * y,
          l * Math.exp(t * S * m)
        ];
      };
    else {
      var E = Math.sqrt(v), g = (h * h - l * l + o * v) / (2 * l * n * E), b = (h * h - l * l - o * v) / (2 * h * n * E), j = Math.log(Math.sqrt(g * g + 1) - g), k = Math.log(Math.sqrt(b * b + 1) - b);
      m = (k - j) / t, x = function(S) {
        var _ = S * m, M = sa(j), W = l / (n * E) * (M * Pp(t * _ + j) - $p(j));
        return [
          c + W * p,
          u + W * y,
          l * M / sa(t * _ + j)
        ];
      };
    }
    return x.duration = m * 1e3 * t / Math.SQRT2, x;
  }
  return r.rho = function(s) {
    var a = Math.max(1e-3, +s), c = a * a, u = c * c;
    return e(a, c, u);
  }, r;
})(Math.SQRT2, 2, 4);
var dn = 0, qn = 0, Vn = 0, ll = 1e3, cr, Un, lr = 0, Ht = 0, Ir = 0, oo = typeof performance == "object" && performance.now ? performance : Date, ul = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function ts() {
  return Ht || (ul(zp), Ht = oo.now() + Ir);
}
function zp() {
  Ht = 0;
}
function ur() {
  this._call = this._time = this._next = null;
}
ur.prototype = dl.prototype = {
  constructor: ur,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? ts() : +n) + (t == null ? 0 : +t), !this._next && Un !== this && (Un ? Un._next = this : cr = this, Un = this), this._call = e, this._time = n, _i();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, _i());
  }
};
function dl(e, t, n) {
  var o = new ur();
  return o.restart(e, t, n), o;
}
function Lp() {
  ts(), ++dn;
  for (var e = cr, t; e; )
    (t = Ht - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --dn;
}
function aa() {
  Ht = (lr = oo.now()) + Ir, dn = qn = 0;
  try {
    Lp();
  } finally {
    dn = 0, Vp(), Ht = 0;
  }
}
function Rp() {
  var e = oo.now(), t = e - lr;
  t > ll && (Ir -= t, lr = e);
}
function Vp() {
  for (var e, t = cr, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : cr = n);
  Un = e, _i(o);
}
function _i(e) {
  if (!dn) {
    qn && (qn = clearTimeout(qn));
    var t = e - Ht;
    t > 24 ? (e < 1 / 0 && (qn = setTimeout(aa, e - oo.now() - Ir)), Vn && (Vn = clearInterval(Vn))) : (Vn || (lr = oo.now(), Vn = setInterval(Rp, ll)), dn = 1, ul(aa));
  }
}
function ca(e, t, n) {
  var o = new ur();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var Op = Er("start", "end", "cancel", "interrupt"), Hp = [], fl = 0, la = 1, Di = 2, Zo = 3, ua = 4, Mi = 5, Go = 6;
function Ar(e, t, n, o, r, s) {
  var a = e.__transition;
  if (!a) e.__transition = {};
  else if (n in a) return;
  Wp(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: Op,
    tween: Hp,
    time: s.time,
    delay: s.delay,
    duration: s.duration,
    ease: s.ease,
    timer: null,
    state: fl
  });
}
function ns(e, t) {
  var n = it(e, t);
  if (n.state > fl) throw new Error("too late; already scheduled");
  return n;
}
function lt(e, t) {
  var n = it(e, t);
  if (n.state > Zo) throw new Error("too late; already running");
  return n;
}
function it(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function Wp(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = dl(s, 0, n.time);
  function s(l) {
    n.state = la, n.timer.restart(a, n.delay, n.time), n.delay <= l && a(l - n.delay);
  }
  function a(l) {
    var d, f, h, p;
    if (n.state !== la) return u();
    for (d in o)
      if (p = o[d], p.name === n.name) {
        if (p.state === Zo) return ca(a);
        p.state === ua ? (p.state = Go, p.timer.stop(), p.on.call("interrupt", e, e.__data__, p.index, p.group), delete o[d]) : +d < t && (p.state = Go, p.timer.stop(), p.on.call("cancel", e, e.__data__, p.index, p.group), delete o[d]);
      }
    if (ca(function() {
      n.state === Zo && (n.state = ua, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = Di, n.on.call("start", e, e.__data__, n.index, n.group), n.state === Di) {
      for (n.state = Zo, r = new Array(h = n.tween.length), d = 0, f = -1; d < h; ++d)
        (p = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (r[++f] = p);
      r.length = f + 1;
    }
  }
  function c(l) {
    for (var d = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(u), n.state = Mi, 1), f = -1, h = r.length; ++f < h; )
      r[f].call(e, d);
    n.state === Mi && (n.on.call("end", e, e.__data__, n.index, n.group), u());
  }
  function u() {
    n.state = Go, n.timer.stop(), delete o[t];
    for (var l in o) return;
    delete e.__transition;
  }
}
function Jo(e, t) {
  var n = e.__transition, o, r, s = !0, a;
  if (n) {
    t = t == null ? null : t + "";
    for (a in n) {
      if ((o = n[a]).name !== t) {
        s = !1;
        continue;
      }
      r = o.state > Di && o.state < Mi, o.state = Go, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[a];
    }
    s && delete e.__transition;
  }
}
function Bp(e) {
  return this.each(function() {
    Jo(this, e);
  });
}
function Fp(e, t) {
  var n, o;
  return function() {
    var r = lt(this, e), s = r.tween;
    if (s !== n) {
      o = n = s;
      for (var a = 0, c = o.length; a < c; ++a)
        if (o[a].name === t) {
          o = o.slice(), o.splice(a, 1);
          break;
        }
    }
    r.tween = o;
  };
}
function Kp(e, t, n) {
  var o, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var s = lt(this, e), a = s.tween;
    if (a !== o) {
      r = (o = a).slice();
      for (var c = { name: t, value: n }, u = 0, l = r.length; u < l; ++u)
        if (r[u].name === t) {
          r[u] = c;
          break;
        }
      u === l && r.push(c);
    }
    s.tween = r;
  };
}
function Xp(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = it(this.node(), n).tween, r = 0, s = o.length, a; r < s; ++r)
      if ((a = o[r]).name === e)
        return a.value;
    return null;
  }
  return this.each((t == null ? Fp : Kp)(n, e, t));
}
function os(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = lt(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return it(r, o).value[t];
  };
}
function hl(e, t) {
  var n;
  return (typeof t == "number" ? st : t instanceof Ot ? ar : (n = Ot(t)) ? (t = n, ar) : sl)(e, t);
}
function qp(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Up(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Yp(e, t, n) {
  var o, r = n + "", s;
  return function() {
    var a = this.getAttribute(e);
    return a === r ? null : a === o ? s : s = t(o = a, n);
  };
}
function Zp(e, t, n) {
  var o, r = n + "", s;
  return function() {
    var a = this.getAttributeNS(e.space, e.local);
    return a === r ? null : a === o ? s : s = t(o = a, n);
  };
}
function Gp(e, t, n) {
  var o, r, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttribute(e) : (a = this.getAttribute(e), u = c + "", a === u ? null : a === o && u === r ? s : (r = u, s = t(o = a, c)));
  };
}
function Jp(e, t, n) {
  var o, r, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (a = this.getAttributeNS(e.space, e.local), u = c + "", a === u ? null : a === o && u === r ? s : (r = u, s = t(o = a, c)));
  };
}
function Qp(e, t) {
  var n = Cr(e), o = n === "transform" ? Mp : hl;
  return this.attrTween(e, typeof t == "function" ? (n.local ? Jp : Gp)(n, o, os(this, "attr." + e, t)) : t == null ? (n.local ? Up : qp)(n) : (n.local ? Zp : Yp)(n, o, t));
}
function ey(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function ty(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function ny(e, t) {
  var n, o;
  function r() {
    var s = t.apply(this, arguments);
    return s !== o && (n = (o = s) && ty(e, s)), n;
  }
  return r._value = t, r;
}
function oy(e, t) {
  var n, o;
  function r() {
    var s = t.apply(this, arguments);
    return s !== o && (n = (o = s) && ey(e, s)), n;
  }
  return r._value = t, r;
}
function ry(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = Cr(e);
  return this.tween(n, (o.local ? ny : oy)(o, t));
}
function iy(e, t) {
  return function() {
    ns(this, e).delay = +t.apply(this, arguments);
  };
}
function sy(e, t) {
  return t = +t, function() {
    ns(this, e).delay = t;
  };
}
function ay(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? iy : sy)(t, e)) : it(this.node(), t).delay;
}
function cy(e, t) {
  return function() {
    lt(this, e).duration = +t.apply(this, arguments);
  };
}
function ly(e, t) {
  return t = +t, function() {
    lt(this, e).duration = t;
  };
}
function uy(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? cy : ly)(t, e)) : it(this.node(), t).duration;
}
function dy(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    lt(this, e).ease = t;
  };
}
function fy(e) {
  var t = this._id;
  return arguments.length ? this.each(dy(t, e)) : it(this.node(), t).ease;
}
function hy(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    lt(this, e).ease = n;
  };
}
function py(e) {
  if (typeof e != "function") throw new Error();
  return this.each(hy(this._id, e));
}
function yy(e) {
  typeof e != "function" && (e = Fc(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = o[r] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new yt(o, this._parents, this._name, this._id);
}
function gy(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, s = Math.min(o, r), a = new Array(o), c = 0; c < s; ++c)
    for (var u = t[c], l = n[c], d = u.length, f = a[c] = new Array(d), h, p = 0; p < d; ++p)
      (h = u[p] || l[p]) && (f[p] = h);
  for (; c < o; ++c)
    a[c] = t[c];
  return new yt(a, this._parents, this._name, this._id);
}
function my(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function xy(e, t, n) {
  var o, r, s = my(t) ? ns : lt;
  return function() {
    var a = s(this, e), c = a.on;
    c !== o && (r = (o = c).copy()).on(t, n), a.on = r;
  };
}
function wy(e, t) {
  var n = this._id;
  return arguments.length < 2 ? it(this.node(), n).on.on(e) : this.each(xy(n, e, t));
}
function vy(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function by() {
  return this.on("end.remove", vy(this._id));
}
function Ny(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Gi(e));
  for (var o = this._groups, r = o.length, s = new Array(r), a = 0; a < r; ++a)
    for (var c = o[a], u = c.length, l = s[a] = new Array(u), d, f, h = 0; h < u; ++h)
      (d = c[h]) && (f = e.call(d, d.__data__, h, c)) && ("__data__" in d && (f.__data__ = d.__data__), l[h] = f, Ar(l[h], t, n, h, l, it(d, n)));
  return new yt(s, this._parents, t, n);
}
function jy(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Bc(e));
  for (var o = this._groups, r = o.length, s = [], a = [], c = 0; c < r; ++c)
    for (var u = o[c], l = u.length, d, f = 0; f < l; ++f)
      if (d = u[f]) {
        for (var h = e.call(d, d.__data__, f, u), p, y = it(d, n), v = 0, x = h.length; v < x; ++v)
          (p = h[v]) && Ar(p, t, n, v, h, y);
        s.push(h), a.push(d);
      }
  return new yt(s, a, t, n);
}
var Sy = po.prototype.constructor;
function ky() {
  return new Sy(this._groups, this._parents);
}
function Ey(e, t) {
  var n, o, r;
  return function() {
    var s = un(this, e), a = (this.style.removeProperty(e), un(this, e));
    return s === a ? null : s === n && a === o ? r : r = t(n = s, o = a);
  };
}
function pl(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Cy(e, t, n) {
  var o, r = n + "", s;
  return function() {
    var a = un(this, e);
    return a === r ? null : a === o ? s : s = t(o = a, n);
  };
}
function Iy(e, t, n) {
  var o, r, s;
  return function() {
    var a = un(this, e), c = n(this), u = c + "";
    return c == null && (u = c = (this.style.removeProperty(e), un(this, e))), a === u ? null : a === o && u === r ? s : (r = u, s = t(o = a, c));
  };
}
function Ay(e, t) {
  var n, o, r, s = "style." + t, a = "end." + s, c;
  return function() {
    var u = lt(this, e), l = u.on, d = u.value[s] == null ? c || (c = pl(t)) : void 0;
    (l !== n || r !== d) && (o = (n = l).copy()).on(a, r = d), u.on = o;
  };
}
function _y(e, t, n) {
  var o = (e += "") == "transform" ? Dp : hl;
  return t == null ? this.styleTween(e, Ey(e, o)).on("end.style." + e, pl(e)) : typeof t == "function" ? this.styleTween(e, Iy(e, o, os(this, "style." + e, t))).each(Ay(this._id, e)) : this.styleTween(e, Cy(e, o, t), n).on("end.style." + e, null);
}
function Dy(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function My(e, t, n) {
  var o, r;
  function s() {
    var a = t.apply(this, arguments);
    return a !== r && (o = (r = a) && Dy(e, a, n)), o;
  }
  return s._value = t, s;
}
function Ty(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, My(e, t, n ?? ""));
}
function $y(e) {
  return function() {
    this.textContent = e;
  };
}
function Py(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function zy(e) {
  return this.tween("text", typeof e == "function" ? Py(os(this, "text", e)) : $y(e == null ? "" : e + ""));
}
function Ly(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Ry(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Ly(r)), t;
  }
  return o._value = e, o;
}
function Vy(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, Ry(e));
}
function Oy() {
  for (var e = this._name, t = this._id, n = yl(), o = this._groups, r = o.length, s = 0; s < r; ++s)
    for (var a = o[s], c = a.length, u, l = 0; l < c; ++l)
      if (u = a[l]) {
        var d = it(u, t);
        Ar(u, e, n, l, a, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new yt(o, this._parents, e, n);
}
function Hy() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(s, a) {
    var c = { value: a }, u = { value: function() {
      --r === 0 && s();
    } };
    n.each(function() {
      var l = lt(this, o), d = l.on;
      d !== e && (t = (e = d).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(u)), l.on = t;
    }), r === 0 && s();
  });
}
var Wy = 0;
function yt(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function yl() {
  return ++Wy;
}
var ht = po.prototype;
yt.prototype = {
  constructor: yt,
  select: Ny,
  selectAll: jy,
  selectChild: ht.selectChild,
  selectChildren: ht.selectChildren,
  filter: yy,
  merge: gy,
  selection: ky,
  transition: Oy,
  call: ht.call,
  nodes: ht.nodes,
  node: ht.node,
  size: ht.size,
  empty: ht.empty,
  each: ht.each,
  on: wy,
  attr: Qp,
  attrTween: ry,
  style: _y,
  styleTween: Ty,
  text: zy,
  textTween: Vy,
  remove: by,
  tween: Xp,
  delay: ay,
  duration: uy,
  ease: fy,
  easeVarying: py,
  end: Hy,
  [Symbol.iterator]: ht[Symbol.iterator]
};
function By(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var Fy = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: By
};
function Ky(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function Xy(e) {
  var t, n;
  e instanceof yt ? (t = e._id, e = e._name) : (t = yl(), (n = Fy).time = ts(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, s = 0; s < r; ++s)
    for (var a = o[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && Ar(u, e, t, l, a, n || Ky(u, t));
  return new yt(o, this._parents, e, t);
}
po.prototype.interrupt = Bp;
po.prototype.transition = Xy;
const Ho = (e) => () => e;
function qy(e, {
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
var _r = new pt(1, 0, 0);
gl.prototype = pt.prototype;
function gl(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return _r;
  return e.__zoom;
}
function li(e) {
  e.stopImmediatePropagation();
}
function On(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Uy(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function Yy() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function da() {
  return this.__zoom || _r;
}
function Zy(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function Gy() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Jy(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], s = e.invertY(t[0][1]) - n[0][1], a = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    a > s ? (s + a) / 2 : Math.min(0, s) || Math.max(0, a)
  );
}
function ml() {
  var e = Uy, t = Yy, n = Jy, o = Zy, r = Gy, s = [0, 1 / 0], a = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, u = Yo, l = Er("start", "zoom", "end"), d, f, h, p = 500, y = 150, v = 0, x = 10;
  function m(N) {
    N.property("__zoom", da).on("wheel.zoom", _, { passive: !1 }).on("mousedown.zoom", M).on("dblclick.zoom", W).filter(r).on("touchstart.zoom", D).on("touchmove.zoom", T).on("touchend.zoom touchcancel.zoom", R).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  m.transform = function(N, A, I, $) {
    var z = N.selection ? N.selection() : N;
    z.property("__zoom", da), N !== z ? j(N, A, I, $) : z.interrupt().each(function() {
      k(this, arguments).event($).start().zoom(null, typeof A == "function" ? A.apply(this, arguments) : A).end();
    });
  }, m.scaleBy = function(N, A, I, $) {
    m.scaleTo(N, function() {
      var z = this.__zoom.k, P = typeof A == "function" ? A.apply(this, arguments) : A;
      return z * P;
    }, I, $);
  }, m.scaleTo = function(N, A, I, $) {
    m.transform(N, function() {
      var z = t.apply(this, arguments), P = this.__zoom, B = I == null ? b(z) : typeof I == "function" ? I.apply(this, arguments) : I, K = P.invert(B), O = typeof A == "function" ? A.apply(this, arguments) : A;
      return n(g(E(P, O), B, K), z, a);
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
      return n(_r.translate(K[0], K[1]).scale(B.k).translate(
        typeof A == "function" ? -A.apply(this, arguments) : -A,
        typeof I == "function" ? -I.apply(this, arguments) : -I
      ), P, a);
    }, $, z);
  };
  function E(N, A) {
    return A = Math.max(s[0], Math.min(s[1], A)), A === N.k ? N : new pt(A, N.x, N.y);
  }
  function g(N, A, I) {
    var $ = A[0] - I[0] * N.k, z = A[1] - I[1] * N.k;
    return $ === N.x && z === N.y ? N : new pt(N.k, $, z);
  }
  function b(N) {
    return [(+N[0][0] + +N[1][0]) / 2, (+N[0][1] + +N[1][1]) / 2];
  }
  function j(N, A, I, $) {
    N.on("start.zoom", function() {
      k(this, arguments).event($).start();
    }).on("interrupt.zoom end.zoom", function() {
      k(this, arguments).event($).end();
    }).tween("zoom", function() {
      var z = this, P = arguments, B = k(z, P).event($), K = t.apply(z, P), O = I == null ? b(K) : typeof I == "function" ? I.apply(z, P) : I, Z = Math.max(K[1][0] - K[0][0], K[1][1] - K[0][1]), q = z.__zoom, oe = typeof A == "function" ? A.apply(z, P) : A, se = u(q.invert(O).concat(Z / q.k), oe.invert(O).concat(Z / oe.k));
      return function(G) {
        if (G === 1) G = oe;
        else {
          var V = se(G), Y = Z / V[2];
          G = new pt(Y, O[0] - V[0] * Y, O[1] - V[1] * Y);
        }
        B.zoom(null, G);
      };
    });
  }
  function k(N, A, I) {
    return !I && N.__zooming || new S(N, A);
  }
  function S(N, A) {
    this.that = N, this.args = A, this.active = 0, this.sourceEvent = null, this.extent = t.apply(N, A), this.taps = 0;
  }
  S.prototype = {
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
      var A = qe(this.that).datum();
      l.call(
        N,
        this.that,
        new qy(N, {
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
    var I = k(this, A).event(N), $ = this.__zoom, z = Math.max(s[0], Math.min(s[1], $.k * Math.pow(2, o.apply(this, arguments)))), P = et(N);
    if (I.wheel)
      (I.mouse[0][0] !== P[0] || I.mouse[0][1] !== P[1]) && (I.mouse[1] = $.invert(I.mouse[0] = P)), clearTimeout(I.wheel);
    else {
      if ($.k === z) return;
      I.mouse = [P, $.invert(P)], Jo(this), I.start();
    }
    On(N), I.wheel = setTimeout(B, y), I.zoom("mouse", n(g(E($, z), I.mouse[0], I.mouse[1]), I.extent, a));
    function B() {
      I.wheel = null, I.end();
    }
  }
  function M(N, ...A) {
    if (h || !e.apply(this, arguments)) return;
    var I = N.currentTarget, $ = k(this, A, !0).event(N), z = qe(N.view).on("mousemove.zoom", O, !0).on("mouseup.zoom", Z, !0), P = et(N, I), B = N.clientX, K = N.clientY;
    el(N.view), li(N), $.mouse = [P, this.__zoom.invert(P)], Jo(this), $.start();
    function O(q) {
      if (On(q), !$.moved) {
        var oe = q.clientX - B, se = q.clientY - K;
        $.moved = oe * oe + se * se > v;
      }
      $.event(q).zoom("mouse", n(g($.that.__zoom, $.mouse[0] = et(q, I), $.mouse[1]), $.extent, a));
    }
    function Z(q) {
      z.on("mousemove.zoom mouseup.zoom", null), tl(q.view, $.moved), On(q), $.event(q).end();
    }
  }
  function W(N, ...A) {
    if (e.apply(this, arguments)) {
      var I = this.__zoom, $ = et(N.changedTouches ? N.changedTouches[0] : N, this), z = I.invert($), P = I.k * (N.shiftKey ? 0.5 : 2), B = n(g(E(I, P), $, z), t.apply(this, A), a);
      On(N), c > 0 ? qe(this).transition().duration(c).call(j, B, $, N) : qe(this).call(m.transform, B, $, N);
    }
  }
  function D(N, ...A) {
    if (e.apply(this, arguments)) {
      var I = N.touches, $ = I.length, z = k(this, A, N.changedTouches.length === $).event(N), P, B, K, O;
      for (li(N), B = 0; B < $; ++B)
        K = I[B], O = et(K, this), O = [O, this.__zoom.invert(O), K.identifier], z.touch0 ? !z.touch1 && z.touch0[2] !== O[2] && (z.touch1 = O, z.taps = 0) : (z.touch0 = O, P = !0, z.taps = 1 + !!d);
      d && (d = clearTimeout(d)), P && (z.taps < 2 && (f = O[0], d = setTimeout(function() {
        d = null;
      }, p)), Jo(this), z.start());
    }
  }
  function T(N, ...A) {
    if (this.__zooming) {
      var I = k(this, A).event(N), $ = N.changedTouches, z = $.length, P, B, K, O;
      for (On(N), P = 0; P < z; ++P)
        B = $[P], K = et(B, this), I.touch0 && I.touch0[2] === B.identifier ? I.touch0[0] = K : I.touch1 && I.touch1[2] === B.identifier && (I.touch1[0] = K);
      if (B = I.that.__zoom, I.touch1) {
        var Z = I.touch0[0], q = I.touch0[1], oe = I.touch1[0], se = I.touch1[1], G = (G = oe[0] - Z[0]) * G + (G = oe[1] - Z[1]) * G, V = (V = se[0] - q[0]) * V + (V = se[1] - q[1]) * V;
        B = E(B, Math.sqrt(G / V)), K = [(Z[0] + oe[0]) / 2, (Z[1] + oe[1]) / 2], O = [(q[0] + se[0]) / 2, (q[1] + se[1]) / 2];
      } else if (I.touch0) K = I.touch0[0], O = I.touch0[1];
      else return;
      I.zoom("touch", n(g(B, K, O), I.extent, a));
    }
  }
  function R(N, ...A) {
    if (this.__zooming) {
      var I = k(this, A).event(N), $ = N.changedTouches, z = $.length, P, B;
      for (li(N), h && clearTimeout(h), h = setTimeout(function() {
        h = null;
      }, p), P = 0; P < z; ++P)
        B = $[P], I.touch0 && I.touch0[2] === B.identifier ? delete I.touch0 : I.touch1 && I.touch1[2] === B.identifier && delete I.touch1;
      if (I.touch1 && !I.touch0 && (I.touch0 = I.touch1, delete I.touch1), I.touch0) I.touch0[1] = this.__zoom.invert(I.touch0[0]);
      else if (I.end(), I.taps === 2 && (B = et(B, this), Math.hypot(f[0] - B[0], f[1] - B[1]) < x)) {
        var K = qe(this).on("dblclick.zoom");
        K && K.apply(this, arguments);
      }
    }
  }
  return m.wheelDelta = function(N) {
    return arguments.length ? (o = typeof N == "function" ? N : Ho(+N), m) : o;
  }, m.filter = function(N) {
    return arguments.length ? (e = typeof N == "function" ? N : Ho(!!N), m) : e;
  }, m.touchable = function(N) {
    return arguments.length ? (r = typeof N == "function" ? N : Ho(!!N), m) : r;
  }, m.extent = function(N) {
    return arguments.length ? (t = typeof N == "function" ? N : Ho([[+N[0][0], +N[0][1]], [+N[1][0], +N[1][1]]]), m) : t;
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
    return arguments.length ? (x = +N, m) : x;
  }, m;
}
const Ye = {
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
}, ro = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], xl = ["Enter", " ", "Escape"], wl = {
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
var fn;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(fn || (fn = {}));
var Lt;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(Lt || (Lt = {}));
var io;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(io || (io = {}));
const vl = {
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
var dr;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(dr || (dr = {}));
var ne;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(ne || (ne = {}));
const fa = {
  [ne.Left]: ne.Right,
  [ne.Right]: ne.Left,
  [ne.Top]: ne.Bottom,
  [ne.Bottom]: ne.Top
};
function bl(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Nl = (e) => "id" in e && "source" in e && "target" in e, Qy = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), rs = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), go = (e, t = [0, 0]) => {
  const { width: n, height: o } = gt(e), r = e.origin ?? t, s = n * r[0], a = o * r[1];
  return {
    x: e.position.x - s,
    y: e.position.y - a
  };
}, eg = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const s = typeof r == "string";
    let a = !t.nodeLookup && !s ? r : void 0;
    t.nodeLookup && (a = s ? t.nodeLookup.get(r) : rs(r) ? r : t.nodeLookup.get(r.id));
    const c = a ? fr(a, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return Dr(o, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return Mr(n);
}, mo = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = Dr(n, fr(r)), o = !0);
  }), o ? Mr(n) : { x: 0, y: 0, width: 0, height: 0 };
}, is = (e, t, [n, o, r] = [0, 0, 1], s = !1, a = !1) => {
  const c = {
    ...vn(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, u = [];
  for (const l of e.values()) {
    const { measured: d, selectable: f = !0, hidden: h = !1 } = l;
    if (a && !f || h)
      continue;
    const p = d.width ?? l.width ?? l.initialWidth ?? null, y = d.height ?? l.height ?? l.initialHeight ?? null, v = so(c, pn(l)), x = (p ?? 0) * (y ?? 0), m = s && v > 0;
    (!l.internals.handleBounds || m || v >= x || l.dragging) && u.push(l);
  }
  return u;
}, tg = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function ng(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function og({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: s }, a) {
  if (e.size === 0)
    return !0;
  const c = ng(e, a), u = mo(c), l = as(u, t, n, a?.minZoom ?? r, a?.maxZoom ?? s, a?.padding ?? 0.1);
  return await o.setViewport(l, {
    duration: a?.duration,
    ease: a?.ease,
    interpolate: a?.interpolate
  }), !0;
}
function jl({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: s }) {
  const a = n.get(e), c = a.parentId ? n.get(a.parentId) : void 0, { x: u, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, d = a.origin ?? o;
  let f = a.extent || r;
  if (a.extent === "parent" && !a.expandParent)
    if (!c)
      s?.("005", Ye.error005());
    else {
      const p = c.measured.width, y = c.measured.height;
      p && y && (f = [
        [u, l],
        [u + p, l + y]
      ]);
    }
  else c && Bt(a.extent) && (f = [
    [a.extent[0][0] + u, a.extent[0][1] + l],
    [a.extent[1][0] + u, a.extent[1][1] + l]
  ]);
  const h = Bt(f) ? Wt(t, f, a.measured) : t;
  return (a.measured.width === void 0 || a.measured.height === void 0) && s?.("015", Ye.error015()), {
    position: {
      x: h.x - u + (a.measured.width ?? 0) * d[0],
      y: h.y - l + (a.measured.height ?? 0) * d[1]
    },
    positionAbsolute: h
  };
}
async function rg({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const s = new Set(e.map((h) => h.id)), a = [];
  for (const h of n) {
    if (h.deletable === !1)
      continue;
    const p = s.has(h.id), y = !p && h.parentId && a.find((v) => v.id === h.parentId);
    (p || y) && a.push(h);
  }
  const c = new Set(t.map((h) => h.id)), u = o.filter((h) => h.deletable !== !1), d = tg(a, u);
  for (const h of u)
    c.has(h.id) && !d.find((y) => y.id === h.id) && d.push(h);
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
const hn = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), Wt = (e = { x: 0, y: 0 }, t, n) => ({
  x: hn(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: hn(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Sl(e, t, n) {
  const { width: o, height: r } = gt(n), { x: s, y: a } = n.internals.positionAbsolute;
  return Wt(e, [
    [s, a],
    [s + o, a + r]
  ], t);
}
const ha = (e, t, n) => e < t ? hn(Math.abs(e - t), 1, t) / t : e > n ? -hn(Math.abs(e - n), 1, t) / t : 0, ss = (e, t, n = 15, o = 40) => {
  const r = ha(e.x, o, t.width - o) * n, s = ha(e.y, o, t.height - o) * n;
  return [r, s];
}, Dr = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), Ti = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), Mr = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), pn = (e, t = [0, 0]) => {
  const { x: n, y: o } = rs(e) ? e.internals.positionAbsolute : go(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, fr = (e, t = [0, 0]) => {
  const { x: n, y: o } = rs(e) ? e.internals.positionAbsolute : go(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, kl = (e, t) => Mr(Dr(Ti(e), Ti(t))), so = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, pa = (e) => nt(e.width) && nt(e.height) && nt(e.x) && nt(e.y), nt = (e) => !isNaN(e) && isFinite(e), El = (e, t) => (n, o) => {
}, xo = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), vn = ({ x: e, y: t }, [n, o, r], s = !1, a = [1, 1]) => {
  const c = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return s ? xo(c, a) : c;
}, yn = ({ x: e, y: t }, [n, o, r]) => ({
  x: e * r + n,
  y: t * r + o
});
function nn(e, t) {
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
function ig(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = nn(e, n), r = nn(e, t);
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
    const o = nn(e.top ?? e.y ?? 0, n), r = nn(e.bottom ?? e.y ?? 0, n), s = nn(e.left ?? e.x ?? 0, t), a = nn(e.right ?? e.x ?? 0, t);
    return { top: o, right: a, bottom: r, left: s, x: s + a, y: o + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function sg(e, t, n, o, r, s) {
  const { x: a, y: c } = yn(e, [t, n, o]), { x: u, y: l } = yn({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), d = r - u, f = s - l;
  return {
    left: Math.floor(a),
    top: Math.floor(c),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const as = (e, t, n, o, r, s) => {
  const a = ig(s, t, n), c = (t - a.x) / e.width, u = (n - a.y) / e.height, l = Math.min(c, u), d = hn(l, o, r), f = e.x + e.width / 2, h = e.y + e.height / 2, p = t / 2 - f * d, y = n / 2 - h * d, v = sg(e, p, y, d, t, n), x = {
    left: Math.min(v.left - a.left, 0),
    top: Math.min(v.top - a.top, 0),
    right: Math.min(v.right - a.right, 0),
    bottom: Math.min(v.bottom - a.bottom, 0)
  };
  return {
    x: p - x.left + x.right,
    y: y - x.top + x.bottom,
    zoom: d
  };
}, ao = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function Bt(e) {
  return e != null && e !== "parent";
}
function gt(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function Cl(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function Il(e, t = { width: 0, height: 0 }, n, o, r) {
  const s = { ...e }, a = o.get(n);
  if (a) {
    const c = a.origin || r;
    s.x += a.internals.positionAbsolute.x - (t.width ?? 0) * c[0], s.y += a.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return s;
}
function ya(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function ag() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function cg(e) {
  return { ...wl, ...e || {} };
}
function Gn(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: s, y: a } = ot(e), c = vn({ x: s - (r?.left ?? 0), y: a - (r?.top ?? 0) }, o), { x: u, y: l } = n ? xo(c, t) : c;
  return {
    xSnapped: u,
    ySnapped: l,
    ...c
  };
}
const cs = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), Al = (e) => e?.getRootNode?.() || window?.document, lg = ["INPUT", "SELECT", "TEXTAREA"];
function _l(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : lg.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const Dl = (e) => "clientX" in e, ot = (e, t) => {
  const n = Dl(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, ga = (e, t, n, o, r) => {
  const s = t.querySelectorAll(`.${e}`);
  return !s || !s.length ? null : Array.from(s).map((a) => {
    const c = a.getBoundingClientRect();
    return {
      id: a.getAttribute("data-handleid"),
      type: e,
      nodeId: r,
      position: a.getAttribute("data-handlepos"),
      x: (c.left - n.left) / o,
      y: (c.top - n.top) / o,
      ...cs(a)
    };
  });
};
function Ml({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: s, targetControlX: a, targetControlY: c }) {
  const u = e * 0.125 + r * 0.375 + a * 0.375 + n * 0.125, l = t * 0.125 + s * 0.375 + c * 0.375 + o * 0.125, d = Math.abs(u - e), f = Math.abs(l - t);
  return [u, l, d, f];
}
function Wo(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function ma({ pos: e, x1: t, y1: n, x2: o, y2: r, c: s }) {
  switch (e) {
    case ne.Left:
      return [t - Wo(t - o, s), n];
    case ne.Right:
      return [t + Wo(o - t, s), n];
    case ne.Top:
      return [t, n - Wo(n - r, s)];
    case ne.Bottom:
      return [t, n + Wo(r - n, s)];
  }
}
function Tl({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: r, targetPosition: s = ne.Top, curvature: a = 0.25 }) {
  const [c, u] = ma({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: a
  }), [l, d] = ma({
    pos: s,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: a
  }), [f, h, p, y] = Ml({
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
    y
  ];
}
function $l({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, s = n < e ? n + r : n - r, a = Math.abs(o - t) / 2, c = o < t ? o + a : o - a;
  return [s, c, r, a];
}
function ug({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: s = "basic" }) {
  if (s === "manual")
    return o;
  const a = r && n ? o + 1e3 : o, c = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return a + c;
}
function dg({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const s = Dr(fr(e), fr(t));
  s.x === s.x2 && (s.x2 += 1), s.y === s.y2 && (s.y2 += 1);
  const a = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return so(a, Mr(s)) > 0;
}
const Pl = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, fg = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), hg = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Ye.error006()), t;
  const o = n.getEdgeId || Pl;
  let r;
  return Nl(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, fg(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, pg = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: r, ...s } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", Ye.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return o.onError?.("007", Ye.error007(r)), n;
  const c = o.getEdgeId || Pl, u = {
    ...s,
    id: o.shouldReplaceId ? c(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== r).concat(u);
};
function zl({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, s, a, c] = $l({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, s, a, c];
}
const xa = {
  [ne.Left]: { x: -1, y: 0 },
  [ne.Right]: { x: 1, y: 0 },
  [ne.Top]: { x: 0, y: -1 },
  [ne.Bottom]: { x: 0, y: 1 }
}, yg = ({ source: e, sourcePosition: t = ne.Bottom, target: n }) => t === ne.Left || t === ne.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, wa = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function gg({ source: e, sourcePosition: t = ne.Bottom, target: n, targetPosition: o = ne.Top, center: r, offset: s, stepPosition: a }) {
  const c = xa[t], u = xa[o], l = { x: e.x + c.x * s, y: e.y + c.y * s }, d = { x: n.x + u.x * s, y: n.y + u.y * s }, f = yg({
    source: l,
    sourcePosition: t,
    target: d
  }), h = f.x !== 0 ? "x" : "y", p = f[h];
  let y = [], v, x;
  const m = { x: 0, y: 0 }, E = { x: 0, y: 0 }, [, , g, b] = $l({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[h] * u[h] === -1) {
    h === "x" ? (v = r.x ?? l.x + (d.x - l.x) * a, x = r.y ?? (l.y + d.y) / 2) : (v = r.x ?? (l.x + d.x) / 2, x = r.y ?? l.y + (d.y - l.y) * a);
    const _ = [
      { x: v, y: l.y },
      { x: v, y: d.y }
    ], M = [
      { x: l.x, y: x },
      { x: d.x, y: x }
    ];
    c[h] === p ? y = h === "x" ? _ : M : y = h === "x" ? M : _;
  } else {
    const _ = [{ x: l.x, y: d.y }], M = [{ x: d.x, y: l.y }];
    if (h === "x" ? y = c.x === p ? M : _ : y = c.y === p ? _ : M, t === o) {
      const N = Math.abs(e[h] - n[h]);
      if (N <= s) {
        const A = Math.min(s - 1, s - N);
        c[h] === p ? m[h] = (l[h] > e[h] ? -1 : 1) * A : E[h] = (d[h] > n[h] ? -1 : 1) * A;
      }
    }
    if (t !== o) {
      const N = h === "x" ? "y" : "x", A = c[h] === u[N], I = l[N] > d[N], $ = l[N] < d[N];
      (c[h] === 1 && (!A && I || A && $) || c[h] !== 1 && (!A && $ || A && I)) && (y = h === "x" ? _ : M);
    }
    const W = { x: l.x + m.x, y: l.y + m.y }, D = { x: d.x + E.x, y: d.y + E.y }, T = Math.max(Math.abs(W.x - y[0].x), Math.abs(D.x - y[0].x)), R = Math.max(Math.abs(W.y - y[0].y), Math.abs(D.y - y[0].y));
    T >= R ? (v = (W.x + D.x) / 2, x = y[0].y) : (v = y[0].x, x = (W.y + D.y) / 2);
  }
  const j = { x: l.x + m.x, y: l.y + m.y }, k = { x: d.x + E.x, y: d.y + E.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...j.x !== y[0].x || j.y !== y[0].y ? [j] : [],
    ...y,
    ...k.x !== y[y.length - 1].x || k.y !== y[y.length - 1].y ? [k] : [],
    n
  ], v, x, g, b];
}
function mg(e, t, n, o) {
  const r = Math.min(wa(e, t) / 2, wa(t, n) / 2, o), { x: s, y: a } = t;
  if (e.x === s && s === n.x || e.y === a && a === n.y)
    return `L${s} ${a}`;
  if (e.y === a) {
    const l = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${s + r * l},${a}Q ${s},${a} ${s},${a + r * d}`;
  }
  const c = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${s},${a + r * u}Q ${s},${a} ${s + r * c},${a}`;
}
function hr({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: r, targetPosition: s = ne.Top, borderRadius: a = 5, centerX: c, centerY: u, offset: l = 20, stepPosition: d = 0.5 }) {
  const [f, h, p, y, v] = gg({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: s,
    center: { x: c, y: u },
    offset: l,
    stepPosition: d
  });
  let x = `M${f[0].x} ${f[0].y}`;
  for (let m = 1; m < f.length - 1; m++)
    x += mg(f[m - 1], f[m], f[m + 1], a);
  return x += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [x, h, p, y, v];
}
function va(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function xg(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!va(t) || !va(n))
    return null;
  const o = t.internals.handleBounds || ba(t.handles), r = n.internals.handleBounds || ba(n.handles), s = Na(o?.source ?? [], e.sourceHandle), a = Na(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === fn.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!s || !a)
    return e.onError?.("008", Ye.error008(s ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = s?.position || ne.Bottom, u = a?.position || ne.Top, l = Ft(t, s, c), d = Ft(n, a, u);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: d.x,
    targetY: d.y,
    sourcePosition: c,
    targetPosition: u
  };
}
function ba(e) {
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
function Ft(e, t, n = ne.Left, o = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, s = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: a, height: c } = t ?? gt(e);
  if (o)
    return { x: r + a / 2, y: s + c / 2 };
  switch (t?.position ?? n) {
    case ne.Top:
      return { x: r + a / 2, y: s };
    case ne.Right:
      return { x: r + a, y: s + c / 2 };
    case ne.Bottom:
      return { x: r + a / 2, y: s + c };
    case ne.Left:
      return { x: r, y: s + c / 2 };
  }
}
function Na(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function $i(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function wg(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const s = /* @__PURE__ */ new Set();
  return e.reduce((a, c) => ([c.markerStart || o, c.markerEnd || r].forEach((u) => {
    if (u && typeof u == "object") {
      const l = $i(u, t);
      s.has(l) || (a.push({ id: l, color: u.color || n, ...u }), s.add(l));
    }
  }), a), []).sort((a, c) => a.id.localeCompare(c.id));
}
const Ll = 1e3, vg = 10, ls = {
  nodeOrigin: [0, 0],
  nodeExtent: ro,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, bg = {
  ...ls,
  checkEquality: !0
};
function us(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function Ng(e, t, n) {
  const o = us(ls, n);
  for (const r of e.values())
    if (r.parentId)
      fs(r, e, t, o);
    else {
      const s = go(r, o.nodeOrigin), a = Bt(r.extent) ? r.extent : o.nodeExtent, c = Wt(s, a, gt(r));
      r.internals.positionAbsolute = c;
    }
}
function jg(e, t) {
  if (!e.handles)
    return e.measured ? t?.internals.handleBounds : void 0;
  const n = [], o = [];
  for (const r of e.handles) {
    const s = {
      id: r.id,
      width: r.width ?? 1,
      height: r.height ?? 1,
      nodeId: e.id,
      x: r.x,
      y: r.y,
      position: r.position,
      type: r.type
    };
    r.type === "source" ? n.push(s) : r.type === "target" && o.push(s);
  }
  return {
    source: n,
    target: o
  };
}
function ds(e) {
  return e === "manual";
}
function Pi(e, t, n, o = {}) {
  const r = us(bg, o), s = { i: 0 }, a = new Map(t), c = r?.elevateNodesOnSelect && !ds(r.zIndexMode) ? Ll : 0;
  let u = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = a.get(d.id);
    if (r.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const h = go(d, r.nodeOrigin), p = Bt(d.extent) ? d.extent : r.nodeExtent, y = Wt(h, p, gt(d));
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
          handleBounds: jg(d, f),
          z: Rl(d, c, r.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (u = !1), d.parentId && fs(f, t, n, o, s), l ||= d.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: l };
}
function Sg(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function fs(e, t, n, o, r) {
  const { elevateNodesOnSelect: s, nodeOrigin: a, nodeExtent: c, zIndexMode: u } = us(ls, o), l = e.parentId, d = t.get(l);
  if (!d) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Sg(e, n), r && !d.parentId && d.internals.rootParentIndex === void 0 && u === "auto" && (d.internals.rootParentIndex = ++r.i, d.internals.z = d.internals.z + r.i * vg), r && d.internals.rootParentIndex !== void 0 && (r.i = d.internals.rootParentIndex);
  const f = s && !ds(u) ? Ll : 0, { x: h, y: p, z: y } = kg(e, d, a, c, f, u), { positionAbsolute: v } = e.internals, x = h !== v.x || p !== v.y;
  (x || y !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: x ? { x: h, y: p } : v,
      z: y
    }
  });
}
function Rl(e, t, n) {
  const o = nt(e.zIndex) ? e.zIndex : 0;
  return ds(n) ? o : o + (e.selected ? t : 0);
}
function kg(e, t, n, o, r, s) {
  const { x: a, y: c } = t.internals.positionAbsolute, u = gt(e), l = go(e, n), d = Bt(e.extent) ? Wt(l, e.extent, u) : l;
  let f = Wt({ x: a + d.x, y: c + d.y }, o, u);
  e.extent === "parent" && (f = Sl(f, u, t));
  const h = Rl(e, r, s), p = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: p >= h ? p + 1 : h
  };
}
function hs(e, t, n, o = [0, 0]) {
  const r = [], s = /* @__PURE__ */ new Map();
  for (const a of e) {
    const c = t.get(a.parentId);
    if (!c)
      continue;
    const u = s.get(a.parentId)?.expandedRect ?? pn(c), l = kl(u, a.rect);
    s.set(a.parentId, { expandedRect: l, parent: c });
  }
  return s.size > 0 && s.forEach(({ expandedRect: a, parent: c }, u) => {
    const l = c.internals.positionAbsolute, d = gt(c), f = c.origin ?? o, h = a.x < l.x ? Math.round(Math.abs(l.x - a.x)) : 0, p = a.y < l.y ? Math.round(Math.abs(l.y - a.y)) : 0, y = Math.max(d.width, Math.round(a.width)), v = Math.max(d.height, Math.round(a.height)), x = (y - d.width) * f[0], m = (v - d.height) * f[1];
    (h > 0 || p > 0 || x || m) && (r.push({
      id: u,
      type: "position",
      position: {
        x: c.position.x - h + x,
        y: c.position.y - p + m
      }
    }), n.get(u)?.forEach((E) => {
      e.some((g) => g.id === E.id) || r.push({
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
        width: y + (h ? f[0] * h - x : 0),
        height: v + (p ? f[1] * p - m : 0)
      }
    });
  }), r;
}
function Eg(e, t, n, o, r, s, a) {
  const c = o?.querySelector(".xyflow__viewport");
  let u = !1;
  if (!c)
    return { changes: [], updatedInternals: u };
  const l = [], d = window.getComputedStyle(c), { m22: f } = new window.DOMMatrixReadOnly(d.transform), h = [];
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
      }), u = !0;
      continue;
    }
    const v = cs(p.nodeElement), x = y.measured.width !== v.width || y.measured.height !== v.height;
    if (!!(v.width && v.height && (x || !y.internals.handleBounds || p.force))) {
      const E = p.nodeElement.getBoundingClientRect(), g = Bt(y.extent) ? y.extent : s;
      let { positionAbsolute: b } = y.internals;
      y.parentId && y.extent === "parent" ? b = Sl(b, v, t.get(y.parentId)) : g && (b = Wt(b, g, v));
      const j = {
        ...y,
        measured: v,
        internals: {
          ...y.internals,
          positionAbsolute: b,
          handleBounds: {
            source: ga("source", p.nodeElement, E, f, y.id),
            target: ga("target", p.nodeElement, E, f, y.id)
          }
        }
      };
      t.set(y.id, j), y.parentId && fs(j, t, n, { nodeOrigin: r, zIndexMode: a }), u = !0, x && (l.push({
        id: y.id,
        type: "dimensions",
        dimensions: v
      }), y.expandParent && y.parentId && h.push({
        id: y.id,
        parentId: y.parentId,
        rect: pn(j, r)
      }));
    }
  }
  if (h.length > 0) {
    const p = hs(h, t, n, r);
    l.push(...p);
  }
  return { changes: l, updatedInternals: u };
}
async function Cg({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: s }) {
  if (!t || !e.x && !e.y)
    return !1;
  const a = await t.setViewportConstrained({
    x: n[0] + e.x,
    y: n[1] + e.y,
    zoom: n[2]
  }, [
    [0, 0],
    [r, s]
  ], o);
  return !!a && (a.x !== n[0] || a.y !== n[1] || a.k !== n[2]);
}
function ja(e, t, n, o, r, s) {
  let a = r;
  const c = o.get(a) || /* @__PURE__ */ new Map();
  o.set(a, c.set(n, t)), a = `${r}-${e}`;
  const u = o.get(a) || /* @__PURE__ */ new Map();
  if (o.set(a, u.set(n, t)), s) {
    a = `${r}-${e}-${s}`;
    const l = o.get(a) || /* @__PURE__ */ new Map();
    o.set(a, l.set(n, t));
  }
}
function Vl(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: s, sourceHandle: a = null, targetHandle: c = null } = o, u = { edgeId: o.id, source: r, target: s, sourceHandle: a, targetHandle: c }, l = `${r}-${a}--${s}-${c}`, d = `${s}-${c}--${r}-${a}`;
    ja("source", u, d, e, r, a), ja("target", u, l, e, s, c), t.set(o.id, o);
  }
}
function Ol(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : Ol(n, t) : !1;
}
function Sa(e, t, n) {
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
function Ig(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [s, a] of e)
    if ((a.selected || a.id === o) && (!a.parentId || !Ol(a, e)) && (a.draggable || t && typeof a.draggable > "u")) {
      const c = e.get(s);
      c && r.set(s, {
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
  return r;
}
function ui({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
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
  const s = n.get(e)?.internals.userNode;
  return [
    s ? {
      ...s,
      position: t.get(e)?.position || s.position,
      dragging: o
    } : r[0],
    r
  ];
}
function Ag({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const s = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, a = xo(s, t);
  return {
    x: a.x - s.x,
    y: a.y - s.y
  };
}
function _g({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let s = { x: null, y: null }, a = 0, c = /* @__PURE__ */ new Map(), u = !1, l = { x: 0, y: 0 }, d = null, f = !1, h = null, p = !1, y = !1, v = null;
  function x({ noDragClassName: E, handleSelector: g, domNode: b, isSelectable: j, nodeId: k, nodeClickDistance: S = 0 }) {
    h = qe(b);
    function _({ x: T, y: R }) {
      const { nodeLookup: N, nodeExtent: A, snapGrid: I, snapToGrid: $, nodeOrigin: z, onNodeDrag: P, onSelectionDrag: B, onError: K, updateNodePositions: O } = t();
      s = { x: T, y: R };
      let Z = !1;
      const q = c.size > 1, oe = q && A ? Ti(mo(c)) : null, se = q && $ ? Ag({
        dragItems: c,
        snapGrid: I,
        x: T,
        y: R
      }) : null;
      for (const [G, V] of c) {
        if (!N.has(G))
          continue;
        let Y = { x: T - V.distance.x, y: R - V.distance.y };
        $ && (Y = se ? {
          x: Math.round(Y.x + se.x),
          y: Math.round(Y.y + se.y)
        } : xo(Y, I));
        let ae = null;
        if (q && A && !V.extent && oe) {
          const { positionAbsolute: te } = V.internals, fe = te.x - oe.x + A[0][0], H = te.x + V.measured.width - oe.x2 + A[1][0], Q = te.y - oe.y + A[0][1], ye = te.y + V.measured.height - oe.y2 + A[1][1];
          ae = [
            [fe, Q],
            [H, ye]
          ];
        }
        const { position: ce, positionAbsolute: J } = jl({
          nodeId: G,
          nextPosition: Y,
          nodeLookup: N,
          nodeExtent: ae || A,
          nodeOrigin: z,
          onError: K
        });
        Z = Z || V.position.x !== ce.x || V.position.y !== ce.y, V.position = ce, V.internals.positionAbsolute = J;
      }
      if (y = y || Z, !!Z && (O(c, !0), v && (o || P || !k && B))) {
        const [G, V] = ui({
          nodeId: k,
          dragItems: c,
          nodeLookup: N
        });
        o?.(v, c, G, V), P?.(v, G, V), k || B?.(v, V);
      }
    }
    async function M() {
      if (!d)
        return;
      const { transform: T, panBy: R, autoPanSpeed: N, autoPanOnNodeDrag: A } = t();
      if (!A) {
        u = !1, cancelAnimationFrame(a);
        return;
      }
      const [I, $] = ss(l, d, N);
      (I !== 0 || $ !== 0) && (s.x = (s.x ?? 0) - I / T[2], s.y = (s.y ?? 0) - $ / T[2], await R({ x: I, y: $ }) && _(s)), a = requestAnimationFrame(M);
    }
    function W(T) {
      const { nodeLookup: R, multiSelectionActive: N, nodesDraggable: A, transform: I, snapGrid: $, snapToGrid: z, selectNodesOnDrag: P, onNodeDragStart: B, onSelectionDragStart: K, unselectNodesAndEdges: O } = t();
      f = !0, (!P || !j) && !N && k && (R.get(k)?.selected || O()), j && P && k && e?.(k);
      const Z = Gn(T.sourceEvent, { transform: I, snapGrid: $, snapToGrid: z, containerBounds: d });
      if (s = Z, c = Ig(R, A, Z, k), c.size > 0 && (n || B || !k && K)) {
        const [q, oe] = ui({
          nodeId: k,
          dragItems: c,
          nodeLookup: R
        });
        n?.(T.sourceEvent, c, q, oe), B?.(T.sourceEvent, q, oe), k || K?.(T.sourceEvent, oe);
      }
    }
    const D = nl().clickDistance(S).on("start", (T) => {
      const { domNode: R, nodeDragThreshold: N, transform: A, snapGrid: I, snapToGrid: $ } = t();
      d = R?.getBoundingClientRect() || null, p = !1, y = !1, v = T.sourceEvent, N === 0 && W(T), s = Gn(T.sourceEvent, { transform: A, snapGrid: I, snapToGrid: $, containerBounds: d }), l = ot(T.sourceEvent, d);
    }).on("drag", (T) => {
      const { autoPanOnNodeDrag: R, transform: N, snapGrid: A, snapToGrid: I, nodeDragThreshold: $, nodeLookup: z } = t(), P = Gn(T.sourceEvent, { transform: N, snapGrid: A, snapToGrid: I, containerBounds: d });
      if (v = T.sourceEvent, (T.sourceEvent.type === "touchmove" && T.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      k && !z.has(k)) && (p = !0), !p) {
        if (!u && R && f && (u = !0, M()), !f) {
          const B = ot(T.sourceEvent, d), K = B.x - l.x, O = B.y - l.y;
          Math.sqrt(K * K + O * O) > $ && W(T);
        }
        (s.x !== P.xSnapped || s.y !== P.ySnapped) && c && f && (l = ot(T.sourceEvent, d), _(P));
      }
    }).on("end", (T) => {
      if (!f || p) {
        p && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (u = !1, f = !1, cancelAnimationFrame(a), c.size > 0) {
        const { nodeLookup: R, updateNodePositions: N, onNodeDragStop: A, onSelectionDragStop: I } = t();
        if (y && (N(c, !1), y = !1), r || A || !k && I) {
          const [$, z] = ui({
            nodeId: k,
            dragItems: c,
            nodeLookup: R,
            dragging: !1
          });
          r?.(T.sourceEvent, c, $, z), A?.(T.sourceEvent, $, z), k || I?.(T.sourceEvent, z);
        }
      }
    }).filter((T) => {
      const R = T.target;
      return !T.button && (!E || !Sa(R, `.${E}`, b)) && (!g || Sa(R, g, b));
    });
    h.call(D);
  }
  function m() {
    h?.on(".drag", null);
  }
  return {
    update: x,
    destroy: m
  };
}
function Dg(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const s of t.values())
    so(r, pn(s)) > 0 && o.push(s);
  return o;
}
const Mg = 250;
function Tg(e, t, n, o) {
  let r = [], s = 1 / 0;
  const a = Dg(e, n, t + Mg);
  for (const c of a) {
    const u = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of u) {
      if (o.nodeId === l.nodeId && o.type === l.type && o.id === l.id)
        continue;
      const { x: d, y: f } = Ft(c, l, l.position, !0), h = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(f - e.y, 2));
      h > t || (h < s ? (r = [{ ...l, x: d, y: f }], s = h) : h === s && r.push({ ...l, x: d, y: f }));
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
function Hl(e, t, n, o, r, s = !1) {
  const a = o.get(e);
  if (!a)
    return null;
  const c = r === "strict" ? a.internals.handleBounds?.[t] : [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []], u = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return u && s ? { ...u, ...Ft(a, u, u.position, !0) } : u;
}
function Wl(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function $g(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const Bl = () => !0;
function Pg(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: s, isTarget: a, domNode: c, nodeLookup: u, lib: l, autoPanOnConnect: d, flowId: f, panBy: h, cancelConnection: p, onConnectStart: y, onConnect: v, onConnectEnd: x, isValidConnection: m = Bl, onReconnectEnd: E, updateConnection: g, getTransform: b, getFromHandle: j, autoPanSpeed: k, dragThreshold: S = 1, handleDomNode: _ }) {
  const M = Al(e.target);
  let W = 0, D;
  const { x: T, y: R } = ot(e), N = Wl(s, _), A = c?.getBoundingClientRect();
  let I = !1;
  if (!A || !N)
    return;
  const $ = Hl(r, N, o, u, t);
  if (!$)
    return;
  let z = ot(e, A), P = !1, B = null, K = !1, O = null;
  function Z() {
    if (!d || !A)
      return;
    const [ce, J] = ss(z, A, k);
    h({ x: ce, y: J }), W = requestAnimationFrame(Z);
  }
  const q = {
    ...$,
    nodeId: r,
    type: N,
    position: $.position
  }, oe = u.get(r);
  let G = {
    inProgress: !0,
    isValid: null,
    from: Ft(oe, q, ne.Left, !0),
    fromHandle: q,
    fromPosition: q.position,
    fromNode: oe,
    to: z,
    toHandle: null,
    toPosition: fa[q.position],
    toNode: null,
    pointer: z
  };
  function V() {
    I = !0, g(G), y?.(e, { nodeId: r, handleId: o, handleType: N });
  }
  S === 0 && V();
  function Y(ce) {
    if (!I) {
      const { x: ye, y: ve } = ot(ce), ke = ye - T, Te = ve - R;
      if (!(ke * ke + Te * Te > S * S))
        return;
      V();
    }
    if (!j() || !q) {
      ae(ce);
      return;
    }
    const J = b();
    z = ot(ce, A), D = Tg(vn(z, J, !1, [1, 1]), n, u, q), P || (Z(), P = !0);
    const te = Fl(ce, {
      handle: D,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: a ? "target" : "source",
      isValidConnection: m,
      doc: M,
      lib: l,
      flowId: f,
      nodeLookup: u
    });
    O = te.handleDomNode, B = te.connection, K = $g(!!D, te.isValid);
    const fe = u.get(r), H = fe ? Ft(fe, q, ne.Left, !0) : G.from, Q = {
      ...G,
      from: H,
      isValid: K,
      to: te.toHandle && K ? yn({ x: te.toHandle.x, y: te.toHandle.y }, J) : z,
      toHandle: te.toHandle,
      toPosition: K && te.toHandle ? te.toHandle.position : fa[q.position],
      toNode: te.toHandle ? u.get(te.toHandle.nodeId) : null,
      pointer: z
    };
    g(Q), G = Q;
  }
  function ae(ce) {
    if (!("touches" in ce && ce.touches.length > 0)) {
      if (I) {
        (D || O) && B && K && v?.(B);
        const { inProgress: J, ...te } = G, fe = {
          ...te,
          toPosition: G.toHandle ? G.toPosition : null
        };
        x?.(ce, fe), s && E?.(ce, fe);
      }
      p(), cancelAnimationFrame(W), P = !1, K = !1, B = null, O = null, M.removeEventListener("mousemove", Y), M.removeEventListener("mouseup", ae), M.removeEventListener("touchmove", Y), M.removeEventListener("touchend", ae);
    }
  }
  M.addEventListener("mousemove", Y), M.addEventListener("mouseup", ae), M.addEventListener("touchmove", Y), M.addEventListener("touchend", ae);
}
function Fl(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: s, doc: a, lib: c, flowId: u, isValidConnection: l = Bl, nodeLookup: d }) {
  const f = s === "target", h = t ? a.querySelector(`.${c}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: p, y } = ot(e), v = a.elementFromPoint(p, y), x = v?.classList.contains(`${c}-flow__handle`) ? v : h, m = {
    handleDomNode: x,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (x) {
    const E = Wl(void 0, x), g = x.getAttribute("data-nodeid"), b = x.getAttribute("data-handleid"), j = x.classList.contains("connectable"), k = x.classList.contains("connectableend");
    if (!g || !E)
      return m;
    const S = {
      source: f ? g : o,
      sourceHandle: f ? b : r,
      target: f ? o : g,
      targetHandle: f ? r : b
    };
    m.connection = S;
    const M = j && k && (n === fn.Strict ? f && E === "source" || !f && E === "target" : g !== o || b !== r);
    m.isValid = M && l(S), m.toHandle = Hl(g, E, b, d, n, !0);
  }
  return m;
}
const zi = {
  onPointerDown: Pg,
  isValid: Fl
};
function zg({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = qe(e);
  function s({ translateExtent: c, width: u, height: l, zoomStep: d = 1, pannable: f = !0, zoomable: h = !0, inversePan: p = !1 }) {
    const y = (g) => {
      if (g.sourceEvent.type !== "wheel" || !t)
        return;
      const b = n(), j = g.sourceEvent.ctrlKey && ao() ? 10 : 1, k = -g.sourceEvent.deltaY * (g.sourceEvent.deltaMode === 1 ? 0.05 : g.sourceEvent.deltaMode ? 1 : 2e-3) * d, S = b[2] * Math.pow(2, k * j);
      t.scaleTo(S);
    };
    let v = [0, 0];
    const x = (g) => {
      (g.sourceEvent.type === "mousedown" || g.sourceEvent.type === "touchstart") && (v = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ]);
    }, m = (g) => {
      const b = n();
      if (g.sourceEvent.type !== "mousemove" && g.sourceEvent.type !== "touchmove" || !t)
        return;
      const j = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ], k = [j[0] - v[0], j[1] - v[1]];
      v = j;
      const S = o() * Math.max(b[2], Math.log(b[2])) * (p ? -1 : 1), _ = {
        x: b[0] - k[0] * S,
        y: b[1] - k[1] * S
      }, M = [
        [0, 0],
        [u, l]
      ];
      t.setViewportConstrained({
        x: _.x,
        y: _.y,
        zoom: b[2]
      }, M, c);
    }, E = ml().on("start", x).on("zoom", f ? m : null).on("zoom.wheel", h ? y : null);
    r.call(E, {});
  }
  function a() {
    r.on("zoom", null);
  }
  return {
    update: s,
    destroy: a,
    pointer: et
  };
}
const Tr = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), di = ({ x: e, y: t, zoom: n }) => _r.translate(e, t).scale(n), rn = (e, t) => e.target.closest(`.${t}`), Kl = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Lg = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, fi = (e, t = 0, n = Lg, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, Xl = (e) => {
  const t = e.ctrlKey && ao() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Rg({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: s, zoomOnPinch: a, onPanZoomStart: c, onPanZoom: u, onPanZoomEnd: l }) {
  return (d) => {
    if (rn(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && a) {
      const x = et(d), m = Xl(d), E = f * Math.pow(2, m);
      o.scaleTo(n, E, x, d);
      return;
    }
    const h = d.deltaMode === 1 ? 20 : 1;
    let p = r === Lt.Vertical ? 0 : d.deltaX * h, y = r === Lt.Horizontal ? 0 : d.deltaY * h;
    !ao() && d.shiftKey && r !== Lt.Vertical && (p = d.deltaY * h, y = 0), o.translateBy(
      n,
      -(p / f) * s,
      -(y / f) * s,
      // @ts-ignore
      { internal: !0 }
    );
    const v = Tr(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(d, v), e.panScrollTimeout = setTimeout(() => {
      l?.(d, v), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(d, v));
  };
}
function Vg({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const s = o.type === "wheel", a = !t && s && !o.ctrlKey, c = rn(o, e);
    if (o.ctrlKey && s && c && o.preventDefault(), a || c)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function Og({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = Tr(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function Hg({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (s) => {
    e.usedRightMouseButton = !!(n && Kl(t, e.mouseButton ?? 0)), s.sourceEvent?.sync || o([s.transform.x, s.transform.y, s.transform.k]), r && !s.sourceEvent?.internal && r?.(s.sourceEvent, Tr(s.transform));
  };
}
function Wg({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: s }) {
  return (a) => {
    if (!a.sourceEvent?.internal && (e.isZoomingOrPanning = !1, s && Kl(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && a.sourceEvent && s(a.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
      const c = Tr(a.transform);
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
function Bg({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: s, userSelectionActive: a, noWheelClassName: c, noPanClassName: u, lib: l, connectionInProgress: d }) {
  return (f) => {
    const h = e || t, p = n && f.ctrlKey, y = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (rn(f, `${l}-flow__node`) || rn(f, `${l}-flow__edge`)))
      return !0;
    if (!o && !h && !r && !s && !n || a || d && !y || rn(f, c) && y || rn(f, u) && (!y || r && y && !e) || !n && f.ctrlKey && y)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!h && !r && !p && y || !o && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(o) && !o.includes(f.button) && f.type === "mousedown")
      return !1;
    const v = Array.isArray(o) && o.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || y) && v;
  };
}
function Fg({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: s, onPanZoomStart: a, onPanZoomEnd: c, onDraggingChange: u }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = ml().scaleExtent([t, n]).translateExtent(o), h = qe(e).call(f);
  E({
    x: r.x,
    y: r.y,
    zoom: hn(r.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], o);
  const p = h.on("wheel.zoom"), y = h.on("dblclick.zoom");
  f.wheelDelta(Xl);
  async function v(D, T) {
    return h ? new Promise((R) => {
      f?.interpolate(T?.interpolate === "linear" ? Zn : Yo).transform(fi(h, T?.duration, T?.ease, () => R(!0)), D);
    }) : !1;
  }
  function x({ noWheelClassName: D, noPanClassName: T, onPaneContextMenu: R, userSelectionActive: N, panOnScroll: A, panOnDrag: I, panOnScrollMode: $, panOnScrollSpeed: z, preventScrolling: P, zoomOnPinch: B, zoomOnScroll: K, zoomOnDoubleClick: O, zoomActivationKeyPressed: Z, lib: q, onTransformChange: oe, connectionInProgress: se, paneClickDistance: G, selectionOnDrag: V }) {
    N && !l.isZoomingOrPanning && m();
    const Y = A && !Z && !N;
    f.clickDistance(V ? 1 / 0 : !nt(G) || G < 0 ? 0 : G);
    const ae = Y ? Rg({
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
    }) : Vg({
      noWheelClassName: D,
      preventScrolling: P,
      d3ZoomHandler: p
    });
    h.on("wheel.zoom", ae, { passive: !1 });
    const ce = Og({
      zoomPanValues: l,
      onDraggingChange: u,
      onPanZoomStart: a
    });
    f.on("start", ce);
    const J = Hg({
      zoomPanValues: l,
      panOnDrag: I,
      onPaneContextMenu: !!R,
      onPanZoom: s,
      onTransformChange: oe
    });
    f.on("zoom", J);
    const te = Wg({
      zoomPanValues: l,
      panOnDrag: I,
      panOnScroll: A,
      onPaneContextMenu: R,
      onPanZoomEnd: c,
      onDraggingChange: u
    });
    f.on("end", te);
    const fe = Bg({
      zoomActivationKeyPressed: Z,
      panOnDrag: I,
      zoomOnScroll: K,
      panOnScroll: A,
      zoomOnDoubleClick: O,
      zoomOnPinch: B,
      userSelectionActive: N,
      noPanClassName: T,
      noWheelClassName: D,
      lib: q,
      connectionInProgress: se
    });
    f.filter(fe), O ? h.on("dblclick.zoom", y) : h.on("dblclick.zoom", null);
  }
  function m() {
    f.on("zoom", null);
  }
  async function E(D, T, R) {
    const N = di(D), A = f?.constrain()(N, T, R);
    return A && await v(A), A;
  }
  async function g(D, T) {
    const R = di(D);
    return await v(R, T), R;
  }
  function b(D) {
    if (h) {
      const T = di(D), R = h.property("__zoom");
      (R.k !== D.zoom || R.x !== D.x || R.y !== D.y) && f?.transform(h, T, null, { sync: !0 });
    }
  }
  function j() {
    const D = h ? gl(h.node()) : { x: 0, y: 0, k: 1 };
    return { x: D.x, y: D.y, zoom: D.k };
  }
  async function k(D, T) {
    return h ? new Promise((R) => {
      f?.interpolate(T?.interpolate === "linear" ? Zn : Yo).scaleTo(fi(h, T?.duration, T?.ease, () => R(!0)), D);
    }) : !1;
  }
  async function S(D, T) {
    return h ? new Promise((R) => {
      f?.interpolate(T?.interpolate === "linear" ? Zn : Yo).scaleBy(fi(h, T?.duration, T?.ease, () => R(!0)), D);
    }) : !1;
  }
  function _(D) {
    f?.scaleExtent(D);
  }
  function M(D) {
    f?.translateExtent(D);
  }
  function W(D) {
    const T = !nt(D) || D < 0 ? 0 : D;
    f?.clickDistance(T);
  }
  return {
    update: x,
    destroy: m,
    setViewport: g,
    setViewportConstrained: E,
    getViewport: j,
    scaleTo: k,
    scaleBy: S,
    setScaleExtent: _,
    setTranslateExtent: M,
    syncViewport: b,
    setClickDistance: W
  };
}
var gn;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(gn || (gn = {}));
function Kg({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: s }) {
  const a = e - t, c = n - o, u = [a > 0 ? 1 : a < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return a && r && (u[0] = u[0] * -1), c && s && (u[1] = u[1] * -1), u;
}
function ka(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: r
  };
}
function wt(e, t) {
  return Math.max(0, t - e);
}
function vt(e, t) {
  return Math.max(0, e - t);
}
function Bo(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function Ea(e, t) {
  return e ? !t : t;
}
function Xg(e, t, n, o, r, s, a, c) {
  let { affectsX: u, affectsY: l } = t;
  const { isHorizontal: d, isVertical: f } = t, h = d && f, { xSnapped: p, ySnapped: y } = n, { minWidth: v, maxWidth: x, minHeight: m, maxHeight: E } = o, { x: g, y: b, width: j, height: k, aspectRatio: S } = e;
  let _ = Math.floor(d ? p - e.pointerX : 0), M = Math.floor(f ? y - e.pointerY : 0);
  const W = j + (u ? -_ : _), D = k + (l ? -M : M), T = -s[0] * j, R = -s[1] * k;
  let N = Bo(W, v, x), A = Bo(D, m, E);
  if (a) {
    let z = 0, P = 0;
    u && _ < 0 ? z = wt(g + _ + T, a[0][0]) : !u && _ > 0 && (z = vt(g + W + T, a[1][0])), l && M < 0 ? P = wt(b + M + R, a[0][1]) : !l && M > 0 && (P = vt(b + D + R, a[1][1])), N = Math.max(N, z), A = Math.max(A, P);
  }
  if (c) {
    let z = 0, P = 0;
    u && _ > 0 ? z = vt(g + _, c[0][0]) : !u && _ < 0 && (z = wt(g + W, c[1][0])), l && M > 0 ? P = vt(b + M, c[0][1]) : !l && M < 0 && (P = wt(b + D, c[1][1])), N = Math.max(N, z), A = Math.max(A, P);
  }
  if (r) {
    if (d) {
      const z = Bo(W / S, m, E) * S;
      if (N = Math.max(N, z), a) {
        let P = 0;
        !u && !l || u && !l && h ? P = vt(b + R + W / S, a[1][1]) * S : P = wt(b + R + (u ? _ : -_) / S, a[0][1]) * S, N = Math.max(N, P);
      }
      if (c) {
        let P = 0;
        !u && !l || u && !l && h ? P = wt(b + W / S, c[1][1]) * S : P = vt(b + (u ? _ : -_) / S, c[0][1]) * S, N = Math.max(N, P);
      }
    }
    if (f) {
      const z = Bo(D * S, v, x) / S;
      if (A = Math.max(A, z), a) {
        let P = 0;
        !u && !l || l && !u && h ? P = vt(g + D * S + T, a[1][0]) / S : P = wt(g + (l ? M : -M) * S + T, a[0][0]) / S, A = Math.max(A, P);
      }
      if (c) {
        let P = 0;
        !u && !l || l && !u && h ? P = wt(g + D * S, c[1][0]) / S : P = vt(g + (l ? M : -M) * S, c[0][0]) / S, A = Math.max(A, P);
      }
    }
  }
  M = M + (M < 0 ? A : -A), _ = _ + (_ < 0 ? N : -N), r && (h ? W > D * S ? M = (Ea(u, l) ? -_ : _) / S : _ = (Ea(u, l) ? -M : M) * S : d ? (M = _ / S, l = u) : (_ = M * S, u = l));
  const I = u ? g + _ : g, $ = l ? b + M : b;
  return {
    width: j + (u ? -_ : _),
    height: k + (l ? -M : M),
    x: s[0] * _ * (u ? -1 : 1) + I,
    y: s[1] * M * (l ? -1 : 1) + $
  };
}
const ql = { width: 0, height: 0, x: 0, y: 0 }, qg = {
  ...ql,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function Ug(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, s = e.measured.width ?? 0, a = e.measured.height ?? 0, c = n[0] * s, u = n[1] * a;
  return [
    [o - c, r - u],
    [o + s - c, r + a - u]
  ];
}
function Yg({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const s = qe(e);
  let a = {
    controlDirection: ka("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: l, boundaries: d, keepAspectRatio: f, resizeDirection: h, onResizeStart: p, onResize: y, onResizeEnd: v, shouldResize: x }) {
    let m = { ...ql }, E = { ...qg };
    a = {
      boundaries: d,
      resizeDirection: h,
      keepAspectRatio: f,
      controlDirection: ka(l)
    };
    let g, b = null, j = [], k, S, _, M = !1;
    const W = nl().on("start", (D) => {
      const { nodeLookup: T, transform: R, snapGrid: N, snapToGrid: A, nodeOrigin: I, paneDomNode: $ } = n();
      if (g = T.get(t), !g)
        return;
      b = $?.getBoundingClientRect() ?? null;
      const { xSnapped: z, ySnapped: P } = Gn(D.sourceEvent, {
        transform: R,
        snapGrid: N,
        snapToGrid: A,
        containerBounds: b
      });
      m = {
        width: g.measured.width ?? 0,
        height: g.measured.height ?? 0,
        x: g.position.x ?? 0,
        y: g.position.y ?? 0
      }, E = {
        ...m,
        pointerX: z,
        pointerY: P,
        aspectRatio: m.width / m.height
      }, k = void 0, S = Bt(g.extent) ? g.extent : void 0, g.parentId && (g.extent === "parent" || g.expandParent) && (k = T.get(g.parentId)), k && g.extent === "parent" && (S = [
        [0, 0],
        [k.measured.width, k.measured.height]
      ]), j = [], _ = void 0;
      for (const [B, K] of T)
        if (K.parentId === t && (j.push({
          id: B,
          position: { ...K.position },
          extent: K.extent
        }), K.extent === "parent" || K.expandParent)) {
          const O = Ug(K, g, K.origin ?? I);
          _ ? _ = [
            [Math.min(O[0][0], _[0][0]), Math.min(O[0][1], _[0][1])],
            [Math.max(O[1][0], _[1][0]), Math.max(O[1][1], _[1][1])]
          ] : _ = O;
        }
      p?.(D, { ...m });
    }).on("drag", (D) => {
      const { transform: T, snapGrid: R, snapToGrid: N, nodeOrigin: A } = n(), I = Gn(D.sourceEvent, {
        transform: T,
        snapGrid: R,
        snapToGrid: N,
        containerBounds: b
      }), $ = [];
      if (!g)
        return;
      const { x: z, y: P, width: B, height: K } = m, O = {}, Z = g.origin ?? A, { width: q, height: oe, x: se, y: G } = Xg(E, a.controlDirection, I, a.boundaries, a.keepAspectRatio, Z, S, _), V = q !== B, Y = oe !== K, ae = se !== z && V, ce = G !== P && Y;
      if (!ae && !ce && !V && !Y)
        return;
      if ((ae || ce || Z[0] === 1 || Z[1] === 1) && (O.x = ae ? se : m.x, O.y = ce ? G : m.y, m.x = O.x, m.y = O.y, j.length > 0)) {
        const H = se - z, Q = G - P;
        for (const ye of j)
          ye.position = {
            x: ye.position.x - H + Z[0] * (q - B),
            y: ye.position.y - Q + Z[1] * (oe - K)
          }, $.push(ye);
      }
      if ((V || Y) && (O.width = V && (!a.resizeDirection || a.resizeDirection === "horizontal") ? q : m.width, O.height = Y && (!a.resizeDirection || a.resizeDirection === "vertical") ? oe : m.height, m.width = O.width, m.height = O.height), k && g.expandParent) {
        const H = Z[0] * (O.width ?? 0);
        O.x && O.x < H && (m.x = H, E.x = E.x - (O.x - H));
        const Q = Z[1] * (O.height ?? 0);
        O.y && O.y < Q && (m.y = Q, E.y = E.y - (O.y - Q));
      }
      const J = Kg({
        width: m.width,
        prevWidth: B,
        height: m.height,
        prevHeight: K,
        affectsX: a.controlDirection.affectsX,
        affectsY: a.controlDirection.affectsY
      }), te = { ...m, direction: J };
      x?.(D, te) !== !1 && (M = !0, y?.(D, te), o(O, $));
    }).on("end", (D) => {
      M && (v?.(D, { ...m }), r?.({ ...m }), M = !1);
    });
    s.call(W);
  }
  function u() {
    s.on(".drag", null);
  }
  return {
    update: c,
    destroy: u
  };
}
var hi = { exports: {} }, pi = {}, yi = { exports: {} }, gi = {};
var Ca;
function Zg() {
  if (Ca) return gi;
  Ca = 1;
  var e = ct;
  function t(f, h) {
    return f === h && (f !== 0 || 1 / f === 1 / h) || f !== f && h !== h;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, s = e.useLayoutEffect, a = e.useDebugValue;
  function c(f, h) {
    var p = h(), y = o({ inst: { value: p, getSnapshot: h } }), v = y[0].inst, x = y[1];
    return s(
      function() {
        v.value = p, v.getSnapshot = h, u(v) && x({ inst: v });
      },
      [f, p, h]
    ), r(
      function() {
        return u(v) && x({ inst: v }), f(function() {
          u(v) && x({ inst: v });
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
  return gi.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : d, gi;
}
var Ia;
function Gg() {
  return Ia || (Ia = 1, yi.exports = Zg()), yi.exports;
}
var Aa;
function Jg() {
  if (Aa) return pi;
  Aa = 1;
  var e = ct, t = Gg();
  function n(l, d) {
    return l === d && (l !== 0 || 1 / l === 1 / d) || l !== l && d !== d;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, s = e.useRef, a = e.useEffect, c = e.useMemo, u = e.useDebugValue;
  return pi.useSyncExternalStoreWithSelector = function(l, d, f, h, p) {
    var y = s(null);
    if (y.current === null) {
      var v = { hasValue: !1, value: null };
      y.current = v;
    } else v = y.current;
    y = c(
      function() {
        function m(k) {
          if (!E) {
            if (E = !0, g = k, k = h(k), p !== void 0 && v.hasValue) {
              var S = v.value;
              if (p(S, k))
                return b = S;
            }
            return b = k;
          }
          if (S = b, o(g, k)) return S;
          var _ = h(k);
          return p !== void 0 && p(S, _) ? (g = k, S) : (g = k, b = _);
        }
        var E = !1, g, b, j = f === void 0 ? null : f;
        return [
          function() {
            return m(d());
          },
          j === null ? void 0 : function() {
            return m(j());
          }
        ];
      },
      [d, f, h, p]
    );
    var x = r(l, y[0], y[1]);
    return a(
      function() {
        v.hasValue = !0, v.value = x;
      },
      [x]
    ), u(x), x;
  }, pi;
}
var _a;
function Qg() {
  return _a || (_a = 1, hi.exports = Jg()), hi.exports;
}
var em = Qg();
const tm = /* @__PURE__ */ mf(em), nm = {}, Da = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (d, f) => {
    const h = typeof d == "function" ? d(t) : d;
    if (!Object.is(h, t)) {
      const p = t;
      t = f ?? (typeof h != "object" || h === null) ? h : Object.assign({}, t, h), n.forEach((y) => y(t, p));
    }
  }, r = () => t, u = { setState: o, getState: r, getInitialState: () => l, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (nm ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(o, r, u);
  return u;
}, om = (e) => e ? Da(e) : Da, { useDebugValue: rm } = ct, { useSyncExternalStoreWithSelector: im } = tm, sm = (e) => e;
function Ul(e, t = sm, n) {
  const o = im(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return rm(o), o;
}
const Ma = (e, t) => {
  const n = om(e), o = (r, s = t) => Ul(n, r, s);
  return Object.assign(o, n), o;
}, am = (e, t) => e ? Ma(e, t) : Ma;
function Ne(e, t) {
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
var mi = { exports: {} }, Me = {};
var Ta;
function cm() {
  if (Ta) return Me;
  Ta = 1;
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
  }, r = /* @__PURE__ */ Symbol.for("react.portal");
  function s(u, l, d) {
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
    return s(u, l, null, d);
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
var $a;
function lm() {
  if ($a) return mi.exports;
  $a = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), mi.exports = cm(), mi.exports;
}
var um = lm();
const $r = Zi(null), dm = $r.Provider, Yl = Ye.error001("react");
function pe(e, t) {
  const n = ho($r);
  if (n === null)
    throw new Error(Yl);
  return Ul(n, e, t);
}
function je() {
  const e = ho($r);
  if (e === null)
    throw new Error(Yl);
  return de(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const Pa = { display: "none" }, fm = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, Zl = "react-flow__node-desc", Gl = "react-flow__edge-desc", hm = "react-flow__aria-live", pm = (e) => e.ariaLiveMessage, ym = (e) => e.ariaLabelConfig;
function gm({ rfId: e }) {
  const t = pe(pm);
  return i.jsx("div", { id: `${hm}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: fm, children: t });
}
function mm({ rfId: e, disableKeyboardA11y: t }) {
  const n = pe(ym);
  return i.jsxs(i.Fragment, { children: [i.jsx("div", { id: `${Zl}-${e}`, style: Pa, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), i.jsx("div", { id: `${Gl}-${e}`, style: Pa, children: n["edge.a11yDescription.default"] }), !t && i.jsx(gm, { rfId: e })] });
}
const Pr = kr(({ position: e = "top-left", children: t, className: n, style: o, ...r }, s) => {
  const a = `${e}`.split("-");
  return i.jsx("div", { className: Ee(["react-flow__panel", n, ...a]), style: o, ref: s, ...r, children: t });
});
Pr.displayName = "Panel";
function xm({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : i.jsx(Pr, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: i.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const wm = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, Fo = (e) => e.id;
function vm(e, t) {
  return Ne(e.selectedNodes.map(Fo), t.selectedNodes.map(Fo)) && Ne(e.selectedEdges.map(Fo), t.selectedEdges.map(Fo));
}
function bm({ onSelectionChange: e }) {
  const t = je(), { selectedNodes: n, selectedEdges: o } = pe(wm, vm);
  return ee(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((s) => s(r));
  }, [n, o, e]), null;
}
const Nm = (e) => !!e.onSelectionChangeHandlers;
function jm({ onSelectionChange: e }) {
  const t = pe(Nm);
  return e || t ? i.jsx(bm, { onSelectionChange: e }) : null;
}
const Jl = [0, 0], Sm = { x: 0, y: 0, zoom: 1 }, km = [
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
], za = [...km, "rfId"], Em = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), La = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: ro,
  nodeOrigin: Jl,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function Cm(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: s, setNodeExtent: a, reset: c, setDefaultNodesAndEdges: u } = pe(Em, Ne), l = je();
  ee(() => (u(e.defaultNodes, e.defaultEdges), () => {
    d.current = La, c();
  }), []);
  const d = ie(La);
  return ee(
    () => {
      for (const f of za) {
        const h = e[f], p = d.current[f];
        h !== p && (typeof e[f] > "u" || (f === "nodes" ? t(h) : f === "edges" ? n(h) : f === "minZoom" ? o(h) : f === "maxZoom" ? r(h) : f === "translateExtent" ? s(h) : f === "nodeExtent" ? a(h) : f === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: cg(h) }) : f === "fitView" ? l.setState({ fitViewQueued: h }) : f === "fitViewOptions" ? l.setState({ fitViewOptions: h }) : l.setState({ [f]: h })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    za.map((f) => e[f])
  ), null;
}
function Ra() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function Im(e) {
  const [t, n] = X(e === "system" ? null : e);
  return ee(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = Ra(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : Ra()?.matches ? "dark" : "light";
}
const Va = typeof document < "u" ? document : null;
function co(e = null, t = { target: Va, actInsideInputWithModifier: !0 }) {
  const [n, o] = X(!1), r = ie(!1), s = ie(/* @__PURE__ */ new Set([])), [a, c] = de(() => {
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
  return ee(() => {
    const u = t?.target ?? Va, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (p) => {
        if (r.current = p.ctrlKey || p.metaKey || p.shiftKey || p.altKey, (!r.current || r.current && !l) && _l(p))
          return !1;
        const v = Ha(p.code, c);
        if (s.current.add(p[v]), Oa(a, s.current, !1)) {
          const x = p.composedPath?.()?.[0] || p.target, m = x?.nodeName === "BUTTON" || x?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !m) && p.preventDefault(), o(!0);
        }
      }, f = (p) => {
        const y = Ha(p.code, c);
        Oa(a, s.current, !0) ? (o(!1), s.current.clear()) : s.current.delete(p[y]), p.key === "Meta" && s.current.clear(), r.current = !1;
      }, h = () => {
        s.current.clear(), o(!1);
      };
      return u?.addEventListener("keydown", d), u?.addEventListener("keyup", f), window.addEventListener("blur", h), window.addEventListener("contextmenu", h), () => {
        u?.removeEventListener("keydown", d), u?.removeEventListener("keyup", f), window.removeEventListener("blur", h), window.removeEventListener("contextmenu", h);
      };
    }
  }, [e, o]), n;
}
function Oa(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function Ha(e, t) {
  return t.includes(e) ? "code" : "key";
}
const Am = () => {
  const e = je();
  return de(() => ({
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
      const { transform: [o, r, s], panZoom: a } = e.getState();
      return a ? (await a.setViewport({
        x: t.x ?? o,
        y: t.y ?? r,
        zoom: t.zoom ?? s
      }, n), !0) : !1;
    },
    getViewport: () => {
      const [t, n, o] = e.getState().transform;
      return { x: t, y: n, zoom: o };
    },
    setCenter: async (t, n, o) => e.getState().setCenter(t, n, o),
    fitBounds: async (t, n) => {
      const { width: o, height: r, minZoom: s, maxZoom: a, panZoom: c } = e.getState(), u = as(t, o, r, s, a, n?.padding ?? 0.1);
      return c ? (await c.setViewport(u, {
        duration: n?.duration,
        ease: n?.ease,
        interpolate: n?.interpolate
      }), !0) : !1;
    },
    screenToFlowPosition: (t, n = {}) => {
      const { transform: o, snapGrid: r, snapToGrid: s, domNode: a } = e.getState();
      if (!a)
        return t;
      const { x: c, y: u } = a.getBoundingClientRect(), l = {
        x: t.x - c,
        y: t.y - u
      }, d = n.snapGrid ?? r, f = n.snapToGrid ?? s;
      return vn(l, o, f, d);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: r, y: s } = o.getBoundingClientRect(), a = yn(t, n);
      return {
        x: a.x + r,
        y: a.y + s
      };
    }
  }), []);
};
function Ql(e, t) {
  const n = [], o = /* @__PURE__ */ new Map(), r = [];
  for (const s of e)
    if (s.type === "add") {
      r.push(s);
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
      _m(u, c);
    n.push(c);
  }
  return r.length && r.forEach((s) => {
    s.index !== void 0 ? n.splice(s.index, 0, { ...s.item }) : n.push({ ...s.item });
  }), n;
}
function _m(e, t) {
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
function eu(e, t) {
  return Ql(e, t);
}
function tu(e, t) {
  return Ql(e, t);
}
function $t(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function sn(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [r, s] of e) {
    const a = t.has(r);
    !(s.selected === void 0 && !a) && s.selected !== a && (n && (s.selected = a), o.push($t(s.id, a)));
  }
  return o;
}
function Wa({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((r) => [r.id, r]));
  for (const [r, s] of e.entries()) {
    const a = t.get(s.id), c = a?.internals?.userNode ?? a;
    c !== void 0 && c !== s && n.push({ id: s.id, item: s, type: "replace" }), c === void 0 && n.push({ item: s, type: "add", index: r });
  }
  for (const [r] of t)
    o.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function Ba(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const nu = El();
function ou(e, t, n = {}) {
  return hg(e, t, {
    ...n,
    onError: n.onError ?? nu
  });
}
function Dm(e, t, n, o = { shouldReplaceId: !0 }) {
  return pg(e, t, n, {
    ...o,
    onError: o.onError ?? nu
  });
}
const Fa = (e) => Qy(e), Mm = (e) => Nl(e);
function ru(e) {
  return kr(e);
}
const Tm = typeof window < "u" ? ff : ee;
function Ka(e) {
  const [t, n] = X(BigInt(0)), [o] = X(() => $m(() => n((r) => r + BigInt(1))));
  return Tm(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function $m(e) {
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
const iu = Zi(null);
function Pm({ children: e }) {
  const t = je(), n = ue((c) => {
    const { nodes: u = [], setNodes: l, hasDefaultNodes: d, onNodesChange: f, nodeLookup: h, fitViewQueued: p, onNodesChangeMiddlewareMap: y } = t.getState();
    let v = u;
    for (const m of c)
      v = typeof m == "function" ? m(v) : m;
    let x = Wa({
      items: v,
      lookup: h
    });
    for (const m of y.values())
      x = m(x);
    d && l(v), x.length > 0 ? f?.(x) : p && window.requestAnimationFrame(() => {
      const { fitViewQueued: m, nodes: E, setNodes: g } = t.getState();
      m && g(E);
    });
  }, []), o = Ka(n), r = ue((c) => {
    const { edges: u = [], setEdges: l, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: h } = t.getState();
    let p = u;
    for (const y of c)
      p = typeof y == "function" ? y(p) : y;
    d ? l(p) : f && f(Wa({
      items: p,
      lookup: h
    }));
  }, []), s = Ka(r), a = de(() => ({ nodeQueue: o, edgeQueue: s }), []);
  return i.jsx(iu.Provider, { value: a, children: e });
}
function zm() {
  const e = ho(iu);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Lm = (e) => !!e.panZoom;
function ps() {
  const e = Am(), t = je(), n = zm(), o = pe(Lm), r = de(() => {
    const s = (f) => t.getState().nodeLookup.get(f), a = (f) => {
      n.nodeQueue.push(f);
    }, c = (f) => {
      n.edgeQueue.push(f);
    }, u = (f) => {
      const { nodeLookup: h, nodeOrigin: p } = t.getState(), y = Fa(f) ? f : h.get(f.id), v = y.parentId ? Il(y.position, y.measured, y.parentId, h, p) : y.position, x = {
        ...y,
        position: v,
        width: y.measured?.width ?? y.width,
        height: y.measured?.height ?? y.height
      };
      return pn(x);
    }, l = (f, h, p = { replace: !1 }) => {
      a((y) => y.map((v) => {
        if (v.id === f) {
          const x = typeof h == "function" ? h(v) : h;
          return p.replace && Fa(x) ? x : { ...v, ...x };
        }
        return v;
      }));
    }, d = (f, h, p = { replace: !1 }) => {
      c((y) => y.map((v) => {
        if (v.id === f) {
          const x = typeof h == "function" ? h(v) : h;
          return p.replace && Mm(x) ? x : { ...v, ...x };
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
        const { nodes: f = [], edges: h = [], transform: p } = t.getState(), [y, v, x] = p;
        return {
          nodes: f.map((m) => ({ ...m })),
          edges: h.map((m) => ({ ...m })),
          viewport: {
            x: y,
            y: v,
            zoom: x
          }
        };
      },
      deleteElements: async ({ nodes: f = [], edges: h = [] }) => {
        const { nodes: p, edges: y, onNodesDelete: v, onEdgesDelete: x, triggerNodeChanges: m, triggerEdgeChanges: E, onDelete: g, onBeforeDelete: b } = t.getState(), { nodes: j, edges: k } = await rg({
          nodesToRemove: f,
          edgesToRemove: h,
          nodes: p,
          edges: y,
          onBeforeDelete: b
        }), S = k.length > 0, _ = j.length > 0;
        if (S) {
          const M = k.map(Ba);
          x?.(k), E(M);
        }
        if (_) {
          const M = j.map(Ba);
          v?.(j), m(M);
        }
        return (_ || S) && g?.({ nodes: j, edges: k }), { deletedNodes: j, deletedEdges: k };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, h = !0, p) => {
        const y = pa(f), v = y ? f : u(f), x = p !== void 0;
        return v ? (p || t.getState().nodes).filter((m) => {
          const E = t.getState().nodeLookup.get(m.id);
          if (E && !y && (m.id === f.id || !E.internals.positionAbsolute))
            return !1;
          const g = pn(x ? m : E), b = so(g, v);
          return h && b > 0 || b >= g.width * g.height || b >= v.width * v.height;
        }) : [];
      },
      isNodeIntersecting: (f, h, p = !0) => {
        const v = pa(f) ? f : u(f);
        if (!v)
          return !1;
        const x = so(v, h);
        return p && x > 0 || x >= h.width * h.height || x >= v.width * v.height;
      },
      updateNode: l,
      updateNodeData: (f, h, p = { replace: !1 }) => {
        l(f, (y) => {
          const v = typeof h == "function" ? h(y) : h;
          return p.replace ? { ...y, data: v } : { ...y, data: { ...y.data, ...v } };
        }, p);
      },
      updateEdge: d,
      updateEdgeData: (f, h, p = { replace: !1 }) => {
        d(f, (y) => {
          const v = typeof h == "function" ? h(y) : h;
          return p.replace ? { ...y, data: v } : { ...y, data: { ...y.data, ...v } };
        }, p);
      },
      getNodesBounds: (f) => {
        const { nodeLookup: h, nodeOrigin: p } = t.getState();
        return eg(f, { nodeLookup: h, nodeOrigin: p });
      },
      getHandleConnections: ({ type: f, id: h, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}-${f}${h ? `-${h}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: h, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}${f ? h ? `-${f}-${h}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const h = t.getState().fitViewResolver ?? ag();
        return t.setState({ fitViewQueued: !0, fitViewOptions: f, fitViewResolver: h }), n.nodeQueue.push((p) => [...p]), h.promise;
      }
    };
  }, []);
  return de(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const Xa = (e) => e.selected, Rm = typeof window < "u" ? window : void 0;
function Vm({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = je(), { deleteElements: o } = ps(), r = co(e, { actInsideInputWithModifier: !1 }), s = co(t, { target: Rm });
  ee(() => {
    if (r) {
      const { edges: a, nodes: c } = n.getState();
      o({ nodes: c.filter(Xa), edges: a.filter(Xa) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), ee(() => {
    n.setState({ multiSelectionActive: s });
  }, [s]);
}
function Om(e) {
  const t = je();
  ee(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = cs(e.current);
      (o.height === 0 || o.width === 0) && t.getState().onError?.("004", Ye.error004()), t.setState({ width: o.width || 500, height: o.height || 500 });
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
const zr = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, Hm = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function Wm({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: s = Lt.Free, zoomOnDoubleClick: a = !0, panOnDrag: c = !0, defaultViewport: u, translateExtent: l, minZoom: d, maxZoom: f, zoomActivationKeyCode: h, preventScrolling: p = !0, children: y, noWheelClassName: v, noPanClassName: x, onViewportChange: m, isControlledViewport: E, paneClickDistance: g, selectionOnDrag: b }) {
  const j = je(), k = ie(null), { userSelectionActive: S, lib: _, connectionInProgress: M } = pe(Hm, Ne), W = co(h), D = ie();
  Om(k);
  const T = ue((R) => {
    m?.({ x: R[0], y: R[1], zoom: R[2] }), E || j.setState({ transform: R });
  }, [m, E]);
  return ee(() => {
    if (k.current) {
      D.current = Fg({
        domNode: k.current,
        minZoom: d,
        maxZoom: f,
        translateExtent: l,
        viewport: u,
        onDraggingChange: (I) => j.setState(($) => $.paneDragging === I ? $ : { paneDragging: I }),
        onPanZoomStart: (I, $) => {
          const { onViewportChangeStart: z, onMoveStart: P } = j.getState();
          P?.(I, $), z?.($);
        },
        onPanZoom: (I, $) => {
          const { onViewportChange: z, onMove: P } = j.getState();
          P?.(I, $), z?.($);
        },
        onPanZoomEnd: (I, $) => {
          const { onViewportChangeEnd: z, onMoveEnd: P } = j.getState();
          P?.(I, $), z?.($);
        }
      });
      const { x: R, y: N, zoom: A } = D.current.getViewport();
      return j.setState({
        panZoom: D.current,
        transform: [R, N, A],
        domNode: k.current.closest(".react-flow")
      }), () => {
        D.current?.destroy();
      };
    }
  }, []), ee(() => {
    D.current?.update({
      onPaneContextMenu: e,
      zoomOnScroll: t,
      zoomOnPinch: n,
      panOnScroll: o,
      panOnScrollSpeed: r,
      panOnScrollMode: s,
      zoomOnDoubleClick: a,
      panOnDrag: c,
      zoomActivationKeyPressed: W,
      preventScrolling: p,
      noPanClassName: x,
      userSelectionActive: S,
      noWheelClassName: v,
      lib: _,
      onTransformChange: T,
      connectionInProgress: M,
      selectionOnDrag: b,
      paneClickDistance: g
    });
  }, [
    e,
    t,
    n,
    o,
    r,
    s,
    a,
    c,
    W,
    p,
    x,
    S,
    v,
    _,
    T,
    M,
    b,
    g
  ]), i.jsx("div", { className: "react-flow__renderer", ref: k, style: zr, children: y });
}
const Bm = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function Fm() {
  const { userSelectionActive: e, userSelectionRect: t } = pe(Bm, Ne);
  return e && t ? i.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const xi = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, Km = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function Xm({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = io.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: s, selectionOnDrag: a, onSelectionStart: c, onSelectionEnd: u, onPaneClick: l, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: h, onPaneMouseMove: p, onPaneMouseLeave: y, children: v }) {
  const x = ie(0), m = je(), { userSelectionActive: E, elementsSelectable: g, dragging: b, connectionInProgress: j, panBy: k, autoPanSpeed: S } = pe(Km, Ne), _ = g && (e || E), M = ie(null), W = ie(), D = ie(/* @__PURE__ */ new Set()), T = ie(/* @__PURE__ */ new Set()), R = ie(!1), N = ie({ x: 0, y: 0 }), A = ie(!1), I = (V) => {
    if (R.current || j) {
      R.current = !1;
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
    R.current && (V.stopPropagation(), R.current = !1);
  }, B = (V) => {
    const { domNode: Y, transform: ae } = m.getState();
    if (W.current = Y?.getBoundingClientRect(), !W.current)
      return;
    const ce = V.target === M.current;
    if (!ce && !!V.target.closest(".nokey") || !e || !(a && ce || t) || V.button !== 0 || !V.isPrimary)
      return;
    V.target?.setPointerCapture?.(V.pointerId), R.current = !1;
    const { x: fe, y: H } = ot(V.nativeEvent, W.current), Q = vn({ x: fe, y: H }, ae);
    m.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: Q.x,
        startY: Q.y,
        x: fe,
        y: H
      }
    }), ce || (V.stopPropagation(), V.preventDefault());
  };
  function K(V, Y) {
    const { userSelectionRect: ae } = m.getState();
    if (!ae)
      return;
    const { transform: ce, nodeLookup: J, edgeLookup: te, connectionLookup: fe, triggerNodeChanges: H, triggerEdgeChanges: Q, defaultEdgeOptions: ye } = m.getState(), ve = { x: ae.startX, y: ae.startY }, { x: ke, y: Te } = yn(ve, ce), Ie = {
      startX: ve.x,
      startY: ve.y,
      x: V < ke ? V : ke,
      y: Y < Te ? Y : Te,
      width: Math.abs(V - ke),
      height: Math.abs(Y - Te)
    }, Oe = D.current, be = T.current;
    D.current = new Set(is(J, Ie, ce, n === io.Partial, !0).map((We) => We.id)), T.current = /* @__PURE__ */ new Set();
    const He = ye?.selectable ?? !0;
    for (const We of D.current) {
      const Ge = fe.get(We);
      if (Ge)
        for (const { edgeId: Je } of Ge.values()) {
          const Qe = te.get(Je);
          Qe && (Qe.selectable ?? He) && T.current.add(Je);
        }
    }
    if (!ya(Oe, D.current)) {
      const We = sn(J, D.current, !0);
      H(We);
    }
    if (!ya(be, T.current)) {
      const We = sn(te, T.current);
      Q(We);
    }
    m.setState({
      userSelectionRect: Ie,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function O() {
    if (!r || !W.current)
      return;
    const [V, Y] = ss(N.current, W.current, S);
    k({ x: V, y: Y }).then((ae) => {
      if (!R.current || !ae) {
        x.current = requestAnimationFrame(O);
        return;
      }
      const { x: ce, y: J } = N.current;
      K(ce, J), x.current = requestAnimationFrame(O);
    });
  }
  const Z = () => {
    cancelAnimationFrame(x.current), x.current = 0, A.current = !1;
  };
  ee(() => () => Z(), []);
  const q = (V) => {
    const { userSelectionRect: Y, transform: ae, resetSelectedElements: ce } = m.getState();
    if (!W.current || !Y)
      return;
    const { x: J, y: te } = ot(V.nativeEvent, W.current);
    N.current = { x: J, y: te };
    const fe = yn({ x: Y.startX, y: Y.startY }, ae);
    if (!R.current) {
      const H = t ? 0 : s;
      if (Math.hypot(J - fe.x, te - fe.y) <= H)
        return;
      ce(), c?.(V);
    }
    R.current = !0, A.current || (O(), A.current = !0), K(J, te);
  }, oe = (V) => {
    V.button === 0 && (V.target?.releasePointerCapture?.(V.pointerId), !E && V.target === M.current && m.getState().userSelectionRect && I?.(V), m.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), R.current && (u?.(V), m.setState({
      nodesSelectionActive: D.current.size > 0
    })), Z());
  }, se = (V) => {
    V.target?.releasePointerCapture?.(V.pointerId), Z();
  }, G = o === !0 || Array.isArray(o) && o.includes(0);
  return i.jsxs("div", { className: Ee(["react-flow__pane", { draggable: G, dragging: b, selection: e }]), onClick: _ ? void 0 : xi(I, M), onContextMenu: xi($, M), onWheel: xi(z, M), onPointerEnter: _ ? void 0 : h, onPointerMove: _ ? q : p, onPointerUp: _ ? oe : void 0, onPointerCancel: _ ? se : void 0, onPointerDownCapture: _ ? B : void 0, onClickCapture: _ ? P : void 0, onPointerLeave: y, ref: M, style: zr, children: [v, i.jsx(Fm, {})] });
}
function Li({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: s, multiSelectionActive: a, nodeLookup: c, onError: u } = t.getState(), l = c.get(e);
  if (!l) {
    u?.("012", Ye.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && a) && (s({ nodes: [l], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function su({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: s, nodeClickDistance: a }) {
  const c = je(), [u, l] = X(!1), d = ie();
  return ee(() => {
    d.current = _g({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (f) => {
        Li({
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
  }, []), ee(() => {
    if (!(t || !e.current || !d.current))
      return d.current.update({
        noDragClassName: n,
        handleSelector: o,
        domNode: e.current,
        isSelectable: s,
        nodeId: r,
        nodeClickDistance: a
      }), () => {
        d.current?.destroy();
      };
  }, [n, o, t, s, e, r, a]), u;
}
const qm = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function au() {
  const e = je();
  return ue((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: s, nodesDraggable: a, onError: c, updateNodePositions: u, nodeLookup: l, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), h = qm(a), p = r ? s[0] : 5, y = r ? s[1] : 5, v = n.direction.x * p * n.factor, x = n.direction.y * y * n.factor;
    for (const [, m] of l) {
      if (!h(m))
        continue;
      let E = {
        x: m.internals.positionAbsolute.x + v,
        y: m.internals.positionAbsolute.y + x
      };
      r && (E = xo(E, s));
      const { position: g, positionAbsolute: b } = jl({
        nodeId: m.id,
        nextPosition: E,
        nodeLookup: l,
        nodeExtent: o,
        nodeOrigin: d,
        onError: c
      });
      m.position = g, m.internals.positionAbsolute = b, f.set(m.id, m);
    }
    u(f);
  }, []);
}
const ys = Zi(null), Um = ys.Provider;
ys.Consumer;
const cu = () => ho(ys), Ym = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), Zm = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: s, connection: a } = o, { fromHandle: c, toHandle: u, isValid: l } = a, d = u?.nodeId === e && u?.id === t && u?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: d,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: s === fn.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!r,
    valid: d && l
  };
};
function Gm({ type: e = "source", position: t = ne.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: s = !0, id: a, onConnect: c, children: u, className: l, onMouseDown: d, onTouchStart: f, ...h }, p) {
  const y = a || null, v = e === "target", x = je(), m = cu(), { connectOnClick: E, noPanClassName: g, rfId: b } = pe(Ym, Ne), { connectingFrom: j, connectingTo: k, clickConnecting: S, isPossibleEndHandle: _, connectionInProcess: M, clickConnectionInProcess: W, valid: D } = pe(Zm(m, y, e), Ne);
  m || x.getState().onError?.("010", Ye.error010());
  const T = (A) => {
    const { defaultEdgeOptions: I, onConnect: $, hasDefaultEdges: z } = x.getState(), P = {
      ...I,
      ...A
    };
    if (z) {
      const { edges: B, setEdges: K, onError: O } = x.getState();
      K(ou(P, B, { onError: O }));
    }
    $?.(P), c?.(P);
  }, R = (A) => {
    if (!m)
      return;
    const I = Dl(A.nativeEvent);
    if (r && (I && A.button === 0 || !I)) {
      const $ = x.getState();
      zi.onPointerDown(A.nativeEvent, {
        handleDomNode: A.currentTarget,
        autoPanOnConnect: $.autoPanOnConnect,
        connectionMode: $.connectionMode,
        connectionRadius: $.connectionRadius,
        domNode: $.domNode,
        nodeLookup: $.nodeLookup,
        lib: $.lib,
        isTarget: v,
        handleId: y,
        nodeId: m,
        flowId: $.rfId,
        panBy: $.panBy,
        cancelConnection: $.cancelConnection,
        onConnectStart: $.onConnectStart,
        onConnectEnd: (...z) => x.getState().onConnectEnd?.(...z),
        updateConnection: $.updateConnection,
        onConnect: T,
        isValidConnection: n || ((...z) => x.getState().isValidConnection?.(...z) ?? !0),
        getTransform: () => x.getState().transform,
        getFromHandle: () => x.getState().connection.fromHandle,
        autoPanSpeed: $.autoPanSpeed,
        dragThreshold: $.connectionDragThreshold
      });
    }
    I ? d?.(A) : f?.(A);
  }, N = (A) => {
    const { onClickConnectStart: I, onClickConnectEnd: $, connectionClickStartHandle: z, connectionMode: P, isValidConnection: B, lib: K, rfId: O, nodeLookup: Z, connection: q } = x.getState();
    if (!m || !z && !r)
      return;
    if (!z) {
      I?.(A.nativeEvent, { nodeId: m, handleId: y, handleType: e }), x.setState({ connectionClickStartHandle: { nodeId: m, type: e, id: y } });
      return;
    }
    const oe = Al(A.target), se = n || B, { connection: G, isValid: V } = zi.isValid(A.nativeEvent, {
      handle: {
        nodeId: m,
        id: y,
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
    const Y = structuredClone(q);
    delete Y.inProgress, Y.toPosition = Y.toHandle ? Y.toHandle.position : null, $?.(A, Y), x.setState({ connectionClickStartHandle: null });
  };
  return i.jsx("div", { "data-handleid": y, "data-nodeid": m, "data-handlepos": t, "data-id": `${b}-${m}-${y}-${e}`, className: Ee([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    g,
    l,
    {
      source: !v,
      target: v,
      connectable: o,
      connectablestart: r,
      connectableend: s,
      clickconnecting: S,
      connectingfrom: j,
      connectingto: k,
      valid: D,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!M || _) && (M || W ? s : r)
    }
  ]), onMouseDown: R, onTouchStart: R, onClick: E ? N : void 0, ref: p, ...h, children: u });
}
const mn = Se(ru(Gm));
function Jm({ data: e, isConnectable: t, sourcePosition: n = ne.Bottom }) {
  return i.jsxs(i.Fragment, { children: [e?.label, i.jsx(mn, { type: "source", position: n, isConnectable: t })] });
}
function Qm({ data: e, isConnectable: t, targetPosition: n = ne.Top, sourcePosition: o = ne.Bottom }) {
  return i.jsxs(i.Fragment, { children: [i.jsx(mn, { type: "target", position: n, isConnectable: t }), e?.label, i.jsx(mn, { type: "source", position: o, isConnectable: t })] });
}
function ex() {
  return null;
}
function tx({ data: e, isConnectable: t, targetPosition: n = ne.Top }) {
  return i.jsxs(i.Fragment, { children: [i.jsx(mn, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const pr = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, qa = {
  input: Jm,
  default: Qm,
  output: tx,
  group: ex
};
function nx(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const ox = (e) => {
  const { width: t, height: n, x: o, y: r } = mo(e.nodeLookup, {
    filter: (s) => !!s.selected
  });
  return {
    width: nt(t) ? t : null,
    height: nt(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function rx({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = je(), { width: r, height: s, transformString: a, userSelectionActive: c } = pe(ox, Ne), u = au(), l = ie(null);
  ee(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !c && r !== null && s !== null;
  if (su({
    nodeRef: l,
    disabled: !d
  }), !d)
    return null;
  const f = e ? (p) => {
    const y = o.getState().nodes.filter((v) => v.selected);
    e(p, y);
  } : void 0, h = (p) => {
    Object.prototype.hasOwnProperty.call(pr, p.key) && (p.preventDefault(), u({
      direction: pr[p.key],
      factor: p.shiftKey ? 4 : 1
    }));
  };
  return i.jsx("div", { className: Ee(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: a
  }, children: i.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : h, style: {
    width: r,
    height: s
  } }) });
}
const Ua = typeof window < "u" ? window : void 0, ix = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function lu({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: s, onPaneScroll: a, paneClickDistance: c, deleteKeyCode: u, selectionKeyCode: l, selectionOnDrag: d, selectionMode: f, onSelectionStart: h, onSelectionEnd: p, multiSelectionKeyCode: y, panActivationKeyCode: v, zoomActivationKeyCode: x, elementsSelectable: m, zoomOnScroll: E, zoomOnPinch: g, panOnScroll: b, panOnScrollSpeed: j, panOnScrollMode: k, zoomOnDoubleClick: S, panOnDrag: _, autoPanOnSelection: M, defaultViewport: W, translateExtent: D, minZoom: T, maxZoom: R, preventScrolling: N, onSelectionContextMenu: A, noWheelClassName: I, noPanClassName: $, disableKeyboardA11y: z, onViewportChange: P, isControlledViewport: B }) {
  const { nodesSelectionActive: K, userSelectionActive: O } = pe(ix, Ne), Z = co(l, { target: Ua }), q = co(v, { target: Ua }), oe = q || _, se = q || b, G = d && oe !== !0, V = Z || O || G;
  return Vm({ deleteKeyCode: u, multiSelectionKeyCode: y }), i.jsx(Wm, { onPaneContextMenu: s, elementsSelectable: m, zoomOnScroll: E, zoomOnPinch: g, panOnScroll: se, panOnScrollSpeed: j, panOnScrollMode: k, zoomOnDoubleClick: S, panOnDrag: !Z && oe, defaultViewport: W, translateExtent: D, minZoom: T, maxZoom: R, zoomActivationKeyCode: x, preventScrolling: N, noWheelClassName: I, noPanClassName: $, onViewportChange: P, isControlledViewport: B, paneClickDistance: c, selectionOnDrag: G, children: i.jsxs(Xm, { onSelectionStart: h, onSelectionEnd: p, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: s, onPaneScroll: a, panOnDrag: oe, autoPanOnSelection: M, isSelecting: !!V, selectionMode: f, selectionKeyPressed: Z, paneClickDistance: c, selectionOnDrag: G, children: [e, K && i.jsx(rx, { onSelectionContextMenu: A, noPanClassName: $, disableKeyboardA11y: z })] }) });
}
lu.displayName = "FlowRenderer";
const sx = Se(lu), ax = (e) => (t) => e ? is(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function cx(e) {
  return pe(ue(ax(e), [e]), Ne);
}
const lx = (e) => e.updateNodeInternals;
function ux() {
  const e = pe(lx), [t] = X(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
    const o = /* @__PURE__ */ new Map();
    n.forEach((r) => {
      const s = r.target.getAttribute("data-id");
      o.set(s, {
        id: s,
        nodeElement: r.target,
        force: !0
      });
    }), e(o);
  }));
  return ee(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function dx({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = je(), s = ie(null), a = ie(null), c = ie(e.sourcePosition), u = ie(e.targetPosition), l = ie(t), d = n && !!e.internals.handleBounds;
  return ee(() => {
    s.current && !e.hidden && (!d || a.current !== s.current) && (a.current && o?.unobserve(a.current), o?.observe(s.current), a.current = s.current);
  }, [d, e.hidden]), ee(() => () => {
    a.current && (o?.unobserve(a.current), a.current = null);
  }, []), ee(() => {
    if (s.current) {
      const f = l.current !== t, h = c.current !== e.sourcePosition, p = u.current !== e.targetPosition;
      (f || h || p) && (l.current = t, c.current = e.sourcePosition, u.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: s.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), s;
}
function fx({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: s, onDoubleClick: a, nodesDraggable: c, elementsSelectable: u, nodesConnectable: l, nodesFocusable: d, resizeObserver: f, noDragClassName: h, noPanClassName: p, disableKeyboardA11y: y, rfId: v, nodeTypes: x, nodeClickDistance: m, onError: E }) {
  const { node: g, internals: b, isParent: j } = pe((V) => {
    const Y = V.nodeLookup.get(e), ae = V.parentLookup.has(e);
    return {
      node: Y,
      internals: Y.internals,
      isParent: ae
    };
  }, Ne);
  let k = g.type || "default", S = x?.[k] || qa[k];
  S === void 0 && (E?.("003", Ye.error003(k)), k = "default", S = x?.default || qa.default);
  const _ = !!(g.draggable || c && typeof g.draggable > "u"), M = !!(g.selectable || u && typeof g.selectable > "u"), W = !!(g.connectable || l && typeof g.connectable > "u"), D = !!(g.focusable || d && typeof g.focusable > "u"), T = je(), R = Cl(g), N = dx({ node: g, nodeType: k, hasDimensions: R, resizeObserver: f }), A = su({
    nodeRef: N,
    disabled: g.hidden || !_,
    noDragClassName: h,
    handleSelector: g.dragHandle,
    nodeId: e,
    isSelectable: M,
    nodeClickDistance: m
  }), I = au();
  if (g.hidden)
    return null;
  const $ = gt(g), z = nx(g), P = M || _ || t || n || o || r, B = n ? (V) => n(V, { ...b.userNode }) : void 0, K = o ? (V) => o(V, { ...b.userNode }) : void 0, O = r ? (V) => r(V, { ...b.userNode }) : void 0, Z = s ? (V) => s(V, { ...b.userNode }) : void 0, q = a ? (V) => a(V, { ...b.userNode }) : void 0, oe = (V) => {
    const { selectNodesOnDrag: Y, nodeDragThreshold: ae } = T.getState();
    M && (!Y || !_ || ae > 0) && Li({
      id: e,
      store: T,
      nodeRef: N
    }), t && t(V, { ...b.userNode });
  }, se = (V) => {
    if (!(_l(V.nativeEvent) || y)) {
      if (xl.includes(V.key) && M) {
        const Y = V.key === "Escape";
        Li({
          id: e,
          store: T,
          unselect: Y,
          nodeRef: N
        });
      } else if (_ && g.selected && Object.prototype.hasOwnProperty.call(pr, V.key)) {
        V.preventDefault();
        const { ariaLabelConfig: Y } = T.getState();
        T.setState({
          ariaLiveMessage: Y["node.a11yDescription.ariaLiveMessage"]({
            direction: V.key.replace("Arrow", "").toLowerCase(),
            x: ~~b.positionAbsolute.x,
            y: ~~b.positionAbsolute.y
          })
        }), I({
          direction: pr[V.key],
          factor: V.shiftKey ? 4 : 1
        });
      }
    }
  }, G = () => {
    if (y || !N.current?.matches(":focus-visible"))
      return;
    const { transform: V, width: Y, height: ae, autoPanOnNodeFocus: ce, setCenter: J } = T.getState();
    if (!ce)
      return;
    is(/* @__PURE__ */ new Map([[e, g]]), { x: 0, y: 0, width: Y, height: ae }, V, !0).length > 0 || J(g.position.x + $.width / 2, g.position.y + $.height / 2, {
      zoom: V[2]
    });
  };
  return i.jsx("div", { className: Ee([
    "react-flow__node",
    `react-flow__node-${k}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [p]: _
    },
    g.className,
    {
      selected: g.selected,
      selectable: M,
      parent: j,
      draggable: _,
      dragging: A
    }
  ]), ref: N, style: {
    zIndex: b.z,
    transform: `translate(${b.positionAbsolute.x}px,${b.positionAbsolute.y}px)`,
    pointerEvents: P ? "all" : "none",
    visibility: R ? "visible" : "hidden",
    ...g.style,
    ...z
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: B, onMouseMove: K, onMouseLeave: O, onContextMenu: Z, onClick: oe, onDoubleClick: q, onKeyDown: D ? se : void 0, tabIndex: D ? 0 : void 0, onFocus: D ? G : void 0, role: g.ariaRole ?? (D ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": y ? void 0 : `${Zl}-${v}`, "aria-label": g.ariaLabel, ...g.domAttributes, children: i.jsx(Um, { value: e, children: i.jsx(S, { id: e, data: g.data, type: k, positionAbsoluteX: b.positionAbsolute.x, positionAbsoluteY: b.positionAbsolute.y, selected: g.selected ?? !1, selectable: M, draggable: _, deletable: g.deletable ?? !0, isConnectable: W, sourcePosition: g.sourcePosition, targetPosition: g.targetPosition, dragging: A, dragHandle: g.dragHandle, zIndex: b.z, parentId: g.parentId, ...$ }) }) });
}
var hx = Se(fx);
const px = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function uu(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: s } = pe(px, Ne), a = cx(e.onlyRenderVisibleElements), c = ux();
  return i.jsx("div", { className: "react-flow__nodes", style: zr, children: a.map((u) => (
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
    i.jsx(hx, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: s }, u)
  )) });
}
uu.displayName = "NodeRenderer";
const yx = Se(uu);
function gx(e) {
  return pe(ue((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const s = n.nodeLookup.get(r.source), a = n.nodeLookup.get(r.target);
        s && a && dg({
          sourceNode: s,
          targetNode: a,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && o.push(r.id);
      }
    return o;
  }, [e]), Ne);
}
const mx = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return i.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, xx = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return i.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Ya = {
  [dr.Arrow]: mx,
  [dr.ArrowClosed]: xx
};
function wx(e) {
  const t = je();
  return de(() => Object.prototype.hasOwnProperty.call(Ya, e) ? Ya[e] : (t.getState().onError?.("009", Ye.error009(e)), null), [e]);
}
const vx = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: s = "strokeWidth", strokeWidth: a, orient: c = "auto-start-reverse" }) => {
  const u = wx(t);
  return u ? i.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: s, orient: c, refX: "0", refY: "0", children: i.jsx(u, { color: n, strokeWidth: a }) }) : null;
}, du = ({ defaultColor: e, rfId: t }) => {
  const n = pe((s) => s.edges), o = pe((s) => s.defaultEdgeOptions), r = de(() => wg(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? i.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: i.jsx("defs", { children: r.map((s) => i.jsx(vx, { id: s.id, type: s.type, color: s.color, width: s.width, height: s.height, markerUnits: s.markerUnits, strokeWidth: s.strokeWidth, orient: s.orient }, s.id)) }) }) : null;
};
du.displayName = "MarkerDefinitions";
var bx = Se(du);
function fu({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: s, labelBgPadding: a = [2, 4], labelBgBorderRadius: c = 2, children: u, className: l, ...d }) {
  const [f, h] = X({ x: 1, y: 0, width: 0, height: 0 }), p = Ee(["react-flow__edge-textwrapper", l]), y = ie(null);
  return ee(() => {
    if (y.current) {
      const v = y.current.getBBox();
      h({
        x: v.x,
        y: v.y,
        width: v.width,
        height: v.height
      });
    }
  }, [n]), n ? i.jsxs("g", { transform: `translate(${e - f.width / 2} ${t - f.height / 2})`, className: p, visibility: f.width ? "visible" : "hidden", ...d, children: [r && i.jsx("rect", { width: f.width + 2 * a[0], x: -a[0], y: -a[1], height: f.height + 2 * a[1], className: "react-flow__edge-textbg", style: s, rx: c, ry: c }), i.jsx("text", { className: "react-flow__edge-text", y: f.height / 2, dy: "0.3em", ref: y, style: o, children: n }), u] }) : null;
}
fu.displayName = "EdgeText";
const Nx = Se(fu);
function wo({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u, interactionWidth: l = 20, ...d }) {
  return i.jsxs(i.Fragment, { children: [i.jsx("path", { ...d, d: e, fill: "none", className: Ee(["react-flow__edge-path", d.className]) }), l ? i.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, o && nt(t) && nt(n) ? i.jsx(Nx, { x: t, y: n, label: o, labelStyle: r, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u }) : null] });
}
function Za({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === ne.Left || e === ne.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function hu({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: r, targetPosition: s = ne.Top }) {
  const [a, c] = Za({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [u, l] = Za({
    pos: s,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [d, f, h, p] = Ml({
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
function pu(e) {
  return Se(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: s, sourcePosition: a, targetPosition: c, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: y, markerEnd: v, markerStart: x, interactionWidth: m }) => {
    const [E, g, b] = hu({
      sourceX: n,
      sourceY: o,
      sourcePosition: a,
      targetX: r,
      targetY: s,
      targetPosition: c
    }), j = e.isInternal ? void 0 : t;
    return i.jsx(wo, { id: j, path: E, labelX: g, labelY: b, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: y, markerEnd: v, markerStart: x, interactionWidth: m });
  });
}
const jx = pu({ isInternal: !1 }), yu = pu({ isInternal: !0 });
jx.displayName = "SimpleBezierEdge";
yu.displayName = "SimpleBezierEdgeInternal";
function gu(e) {
  return Se(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: h, sourcePosition: p = ne.Bottom, targetPosition: y = ne.Top, markerEnd: v, markerStart: x, pathOptions: m, interactionWidth: E }) => {
    const [g, b, j] = hr({
      sourceX: n,
      sourceY: o,
      sourcePosition: p,
      targetX: r,
      targetY: s,
      targetPosition: y,
      borderRadius: m?.borderRadius,
      offset: m?.offset,
      stepPosition: m?.stepPosition
    }), k = e.isInternal ? void 0 : t;
    return i.jsx(wo, { id: k, path: g, labelX: b, labelY: j, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: v, markerStart: x, interactionWidth: E });
  });
}
const mu = gu({ isInternal: !1 }), xu = gu({ isInternal: !0 });
mu.displayName = "SmoothStepEdge";
xu.displayName = "SmoothStepEdgeInternal";
function wu(e) {
  return Se(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return i.jsx(mu, { ...n, id: o, pathOptions: de(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Sx = wu({ isInternal: !1 }), vu = wu({ isInternal: !0 });
Sx.displayName = "StepEdge";
vu.displayName = "StepEdgeInternal";
function bu(e) {
  return Se(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: p, markerStart: y, interactionWidth: v }) => {
    const [x, m, E] = zl({ sourceX: n, sourceY: o, targetX: r, targetY: s }), g = e.isInternal ? void 0 : t;
    return i.jsx(wo, { id: g, path: x, labelX: m, labelY: E, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: p, markerStart: y, interactionWidth: v });
  });
}
const kx = bu({ isInternal: !1 }), Nu = bu({ isInternal: !0 });
kx.displayName = "StraightEdge";
Nu.displayName = "StraightEdgeInternal";
function ju(e) {
  return Se(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: s, sourcePosition: a = ne.Bottom, targetPosition: c = ne.Top, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: y, markerEnd: v, markerStart: x, pathOptions: m, interactionWidth: E }) => {
    const [g, b, j] = Tl({
      sourceX: n,
      sourceY: o,
      sourcePosition: a,
      targetX: r,
      targetY: s,
      targetPosition: c,
      curvature: m?.curvature
    }), k = e.isInternal ? void 0 : t;
    return i.jsx(wo, { id: k, path: g, labelX: b, labelY: j, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: p, style: y, markerEnd: v, markerStart: x, interactionWidth: E });
  });
}
const Ex = ju({ isInternal: !1 }), Su = ju({ isInternal: !0 });
Ex.displayName = "BezierEdge";
Su.displayName = "BezierEdgeInternal";
const Ga = {
  default: Su,
  straight: Nu,
  step: vu,
  smoothstep: xu,
  simplebezier: yu
}, Ja = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, Cx = (e, t, n) => n === ne.Left ? e - t : n === ne.Right ? e + t : e, Ix = (e, t, n) => n === ne.Top ? e - t : n === ne.Bottom ? e + t : e, Qa = "react-flow__edgeupdater";
function ec({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: s, onMouseOut: a, type: c }) {
  return i.jsx("circle", { onMouseDown: r, onMouseEnter: s, onMouseOut: a, className: Ee([Qa, `${Qa}-${c}`]), cx: Cx(t, o, e), cy: Ix(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function Ax({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: s, targetY: a, sourcePosition: c, targetPosition: u, onReconnect: l, onReconnectStart: d, onReconnectEnd: f, setReconnecting: h, setUpdateHover: p }) {
  const y = je(), v = (b, j) => {
    if (b.button !== 0)
      return;
    const { autoPanOnConnect: k, domNode: S, connectionMode: _, connectionRadius: M, lib: W, onConnectStart: D, cancelConnection: T, nodeLookup: R, rfId: N, panBy: A, updateConnection: I } = y.getState(), $ = j.type === "target", z = (K, O) => {
      h(!1), f?.(K, n, j.type, O);
    }, P = (K) => l?.(n, K), B = (K, O) => {
      h(!0), d?.(b, n, j.type), D?.(K, O);
    };
    zi.onPointerDown(b.nativeEvent, {
      autoPanOnConnect: k,
      connectionMode: _,
      connectionRadius: M,
      domNode: S,
      handleId: j.id,
      nodeId: j.nodeId,
      nodeLookup: R,
      isTarget: $,
      edgeUpdaterType: j.type,
      lib: W,
      flowId: N,
      cancelConnection: T,
      panBy: A,
      isValidConnection: (...K) => y.getState().isValidConnection?.(...K) ?? !0,
      onConnect: P,
      onConnectStart: B,
      onConnectEnd: (...K) => y.getState().onConnectEnd?.(...K),
      onReconnectEnd: z,
      updateConnection: I,
      getTransform: () => y.getState().transform,
      getFromHandle: () => y.getState().connection.fromHandle,
      dragThreshold: y.getState().connectionDragThreshold,
      handleDomNode: b.currentTarget
    });
  }, x = (b) => v(b, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), m = (b) => v(b, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), E = () => p(!0), g = () => p(!1);
  return i.jsxs(i.Fragment, { children: [(e === !0 || e === "source") && i.jsx(ec, { position: c, centerX: o, centerY: r, radius: t, onMouseDown: x, onMouseEnter: E, onMouseOut: g, type: "source" }), (e === !0 || e === "target") && i.jsx(ec, { position: u, centerX: s, centerY: a, radius: t, onMouseDown: m, onMouseEnter: E, onMouseOut: g, type: "target" })] });
}
function _x({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, reconnectRadius: d, onReconnect: f, onReconnectStart: h, onReconnectEnd: p, rfId: y, edgeTypes: v, noPanClassName: x, onError: m, disableKeyboardA11y: E }) {
  let g = pe((J) => J.edgeLookup.get(e));
  const b = pe((J) => J.defaultEdgeOptions);
  g = b ? { ...b, ...g } : g;
  let j = g.type || "default", k = v?.[j] || Ga[j];
  k === void 0 && (m?.("011", Ye.error011(j)), j = "default", k = v?.default || Ga.default);
  const S = !!(g.focusable || t && typeof g.focusable > "u"), _ = typeof f < "u" && (g.reconnectable || n && typeof g.reconnectable > "u"), M = !!(g.selectable || o && typeof g.selectable > "u"), W = ie(null), [D, T] = X(!1), [R, N] = X(!1), A = je(), { zIndex: I, sourceX: $, sourceY: z, targetX: P, targetY: B, sourcePosition: K, targetPosition: O } = pe(ue((J) => {
    const te = J.nodeLookup.get(g.source), fe = J.nodeLookup.get(g.target);
    if (!te || !fe)
      return {
        zIndex: g.zIndex,
        ...Ja
      };
    const H = xg({
      id: e,
      sourceNode: te,
      targetNode: fe,
      sourceHandle: g.sourceHandle || null,
      targetHandle: g.targetHandle || null,
      connectionMode: J.connectionMode,
      onError: m
    });
    return {
      zIndex: ug({
        selected: g.selected,
        zIndex: g.zIndex,
        sourceNode: te,
        targetNode: fe,
        elevateOnSelect: J.elevateEdgesOnSelect,
        zIndexMode: J.zIndexMode
      }),
      ...H || Ja
    };
  }, [g.source, g.target, g.sourceHandle, g.targetHandle, g.selected, g.zIndex]), Ne), Z = de(() => g.markerStart ? `url('#${$i(g.markerStart, y)}')` : void 0, [g.markerStart, y]), q = de(() => g.markerEnd ? `url('#${$i(g.markerEnd, y)}')` : void 0, [g.markerEnd, y]);
  if (g.hidden || $ === null || z === null || P === null || B === null)
    return null;
  const oe = (J) => {
    const { addSelectedEdges: te, unselectNodesAndEdges: fe, multiSelectionActive: H } = A.getState();
    M && (A.setState({ nodesSelectionActive: !1 }), g.selected && H ? (fe({ nodes: [], edges: [g] }), W.current?.blur()) : te([e])), r && r(J, g);
  }, se = s ? (J) => {
    s(J, { ...g });
  } : void 0, G = a ? (J) => {
    a(J, { ...g });
  } : void 0, V = c ? (J) => {
    c(J, { ...g });
  } : void 0, Y = u ? (J) => {
    u(J, { ...g });
  } : void 0, ae = l ? (J) => {
    l(J, { ...g });
  } : void 0, ce = (J) => {
    if (!E && xl.includes(J.key) && M) {
      const { unselectNodesAndEdges: te, addSelectedEdges: fe } = A.getState();
      J.key === "Escape" ? (W.current?.blur(), te({ edges: [g] })) : fe([e]);
    }
  };
  return i.jsx("svg", { style: { zIndex: I }, children: i.jsxs("g", { className: Ee([
    "react-flow__edge",
    `react-flow__edge-${j}`,
    g.className,
    x,
    {
      selected: g.selected,
      animated: g.animated,
      inactive: !M && !r,
      updating: D,
      selectable: M
    }
  ]), onClick: oe, onDoubleClick: se, onContextMenu: G, onMouseEnter: V, onMouseMove: Y, onMouseLeave: ae, onKeyDown: S ? ce : void 0, tabIndex: S ? 0 : void 0, role: g.ariaRole ?? (S ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": g.ariaLabel === null ? void 0 : g.ariaLabel || `Edge from ${g.source} to ${g.target}`, "aria-describedby": S ? `${Gl}-${y}` : void 0, ref: W, ...g.domAttributes, children: [!R && i.jsx(k, { id: e, source: g.source, target: g.target, type: g.type, selected: g.selected, animated: g.animated, selectable: M, deletable: g.deletable ?? !0, label: g.label, labelStyle: g.labelStyle, labelShowBg: g.labelShowBg, labelBgStyle: g.labelBgStyle, labelBgPadding: g.labelBgPadding, labelBgBorderRadius: g.labelBgBorderRadius, sourceX: $, sourceY: z, targetX: P, targetY: B, sourcePosition: K, targetPosition: O, data: g.data, style: g.style, sourceHandleId: g.sourceHandle, targetHandleId: g.targetHandle, markerStart: Z, markerEnd: q, pathOptions: "pathOptions" in g ? g.pathOptions : void 0, interactionWidth: g.interactionWidth }), _ && i.jsx(Ax, { edge: g, isReconnectable: _, reconnectRadius: d, onReconnect: f, onReconnectStart: h, onReconnectEnd: p, sourceX: $, sourceY: z, targetX: P, targetY: B, sourcePosition: K, targetPosition: O, setUpdateHover: T, setReconnecting: N })] }) });
}
var Dx = Se(_x);
const Mx = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function ku({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: s, onEdgeContextMenu: a, onEdgeMouseEnter: c, onEdgeMouseMove: u, onEdgeMouseLeave: l, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: h, onReconnectStart: p, onReconnectEnd: y, disableKeyboardA11y: v }) {
  const { edgesFocusable: x, edgesReconnectable: m, elementsSelectable: E, onError: g } = pe(Mx, Ne), b = gx(t);
  return i.jsxs("div", { className: "react-flow__edges", children: [i.jsx(bx, { defaultColor: e, rfId: n }), b.map((j) => i.jsx(Dx, { id: j, edgesFocusable: x, edgesReconnectable: m, elementsSelectable: E, noPanClassName: r, onReconnect: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, onClick: d, reconnectRadius: f, onDoubleClick: h, onReconnectStart: p, onReconnectEnd: y, rfId: n, onError: g, edgeTypes: o, disableKeyboardA11y: v }, j))] });
}
ku.displayName = "EdgeRenderer";
const Tx = Se(ku), $x = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function Px({ children: e }) {
  const t = pe($x);
  return i.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function zx(e) {
  const t = ps(), n = ie(!1);
  ee(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Lx = (e) => e.panZoom?.syncViewport;
function Rx(e) {
  const t = pe(Lx), n = je();
  return ee(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function Vx(e) {
  return e.connection.inProgress ? { ...e.connection, to: vn(e.connection.to, e.transform) } : { ...e.connection };
}
function Ox(e) {
  return Vx;
}
function Hx(e) {
  const t = Ox();
  return pe(t, Ne);
}
const Wx = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function Bx({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: s, height: a, isValid: c, inProgress: u } = pe(Wx, Ne);
  return !(s && r && u) ? null : i.jsx("svg", { style: e, width: s, height: a, className: "react-flow__connectionline react-flow__container", children: i.jsx("g", { className: Ee(["react-flow__connection", bl(c)]), children: i.jsx(Eu, { style: t, type: n, CustomComponent: o, isValid: c }) }) });
}
const Eu = ({ style: e, type: t = bt.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: s, fromNode: a, fromHandle: c, fromPosition: u, to: l, toNode: d, toHandle: f, toPosition: h, pointer: p } = Hx();
  if (!r)
    return;
  if (n)
    return i.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: a, fromHandle: c, fromX: s.x, fromY: s.y, toX: l.x, toY: l.y, fromPosition: u, toPosition: h, connectionStatus: bl(o), toNode: d, toHandle: f, pointer: p });
  let y = "";
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
      [y] = Tl(v);
      break;
    case bt.SimpleBezier:
      [y] = hu(v);
      break;
    case bt.Step:
      [y] = hr({
        ...v,
        borderRadius: 0
      });
      break;
    case bt.SmoothStep:
      [y] = hr(v);
      break;
    default:
      [y] = zl(v);
  }
  return i.jsx("path", { d: y, fill: "none", className: "react-flow__connection-path", style: e });
};
Eu.displayName = "ConnectionLine";
const Fx = {};
function tc(e = Fx) {
  ie(e), je(), ee(() => {
  }, [e]);
}
function Kx() {
  je(), ie(!1), ee(() => {
  }, []);
}
function Cu({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: s, onEdgeDoubleClick: a, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: h, onSelectionEnd: p, connectionLineType: y, connectionLineStyle: v, connectionLineComponent: x, connectionLineContainerStyle: m, selectionKeyCode: E, selectionOnDrag: g, selectionMode: b, multiSelectionKeyCode: j, panActivationKeyCode: k, zoomActivationKeyCode: S, deleteKeyCode: _, onlyRenderVisibleElements: M, elementsSelectable: W, defaultViewport: D, translateExtent: T, minZoom: R, maxZoom: N, preventScrolling: A, defaultMarkerColor: I, zoomOnScroll: $, zoomOnPinch: z, panOnScroll: P, panOnScrollSpeed: B, panOnScrollMode: K, zoomOnDoubleClick: O, panOnDrag: Z, autoPanOnSelection: q, onPaneClick: oe, onPaneMouseEnter: se, onPaneMouseMove: G, onPaneMouseLeave: V, onPaneScroll: Y, onPaneContextMenu: ae, paneClickDistance: ce, nodeClickDistance: J, onEdgeContextMenu: te, onEdgeMouseEnter: fe, onEdgeMouseMove: H, onEdgeMouseLeave: Q, reconnectRadius: ye, onReconnect: ve, onReconnectStart: ke, onReconnectEnd: Te, noDragClassName: Ie, noWheelClassName: Oe, noPanClassName: be, disableKeyboardA11y: He, nodeExtent: We, rfId: Ge, viewport: Je, onViewportChange: Qe }) {
  return tc(e), tc(t), Kx(), zx(n), Rx(Je), i.jsx(sx, { onPaneClick: oe, onPaneMouseEnter: se, onPaneMouseMove: G, onPaneMouseLeave: V, onPaneContextMenu: ae, onPaneScroll: Y, paneClickDistance: ce, deleteKeyCode: _, selectionKeyCode: E, selectionOnDrag: g, selectionMode: b, onSelectionStart: h, onSelectionEnd: p, multiSelectionKeyCode: j, panActivationKeyCode: k, zoomActivationKeyCode: S, elementsSelectable: W, zoomOnScroll: $, zoomOnPinch: z, zoomOnDoubleClick: O, panOnScroll: P, panOnScrollSpeed: B, panOnScrollMode: K, panOnDrag: Z, autoPanOnSelection: q, defaultViewport: D, translateExtent: T, minZoom: R, maxZoom: N, onSelectionContextMenu: f, preventScrolling: A, noDragClassName: Ie, noWheelClassName: Oe, noPanClassName: be, disableKeyboardA11y: He, onViewportChange: Qe, isControlledViewport: !!Je, children: i.jsxs(Px, { children: [i.jsx(Tx, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: a, onReconnect: ve, onReconnectStart: ke, onReconnectEnd: Te, onlyRenderVisibleElements: M, onEdgeContextMenu: te, onEdgeMouseEnter: fe, onEdgeMouseMove: H, onEdgeMouseLeave: Q, reconnectRadius: ye, defaultMarkerColor: I, noPanClassName: be, disableKeyboardA11y: He, rfId: Ge }), i.jsx(Bx, { style: v, type: y, component: x, containerStyle: m }), i.jsx("div", { className: "react-flow__edgelabel-renderer" }), i.jsx(yx, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, nodeClickDistance: J, onlyRenderVisibleElements: M, noPanClassName: be, noDragClassName: Ie, disableKeyboardA11y: He, nodeExtent: We, rfId: Ge }), i.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Cu.displayName = "GraphView";
const Xx = Se(Cu), qx = El(), nc = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: s, fitView: a, fitViewOptions: c, minZoom: u = 0.5, maxZoom: l = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: h = "basic" } = {}) => {
  const p = /* @__PURE__ */ new Map(), y = /* @__PURE__ */ new Map(), v = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), m = o ?? t ?? [], E = n ?? e ?? [], g = d ?? [0, 0], b = f ?? ro;
  Vl(v, x, m);
  const { nodesInitialized: j } = Pi(E, p, y, {
    nodeOrigin: g,
    nodeExtent: b,
    zIndexMode: h
  });
  let k = [0, 0, 1];
  if (a && r && s) {
    const S = mo(p, {
      filter: (D) => !!((D.width || D.initialWidth) && (D.height || D.initialHeight))
    }), { x: _, y: M, zoom: W } = as(S, r, s, u, l, c?.padding ?? 0.1);
    k = [_, M, W];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: s ?? 0,
    transform: k,
    nodes: E,
    nodesInitialized: j,
    nodeLookup: p,
    parentLookup: y,
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
    translateExtent: ro,
    nodeExtent: b,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: fn.Strict,
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
    fitViewQueued: a ?? !1,
    fitViewOptions: c,
    fitViewResolver: null,
    connection: { ...vl },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: qx,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: wl,
    zIndexMode: h,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, Ux = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: s, fitView: a, fitViewOptions: c, minZoom: u, maxZoom: l, nodeOrigin: d, nodeExtent: f, zIndexMode: h }) => am((p, y) => {
  async function v() {
    const { nodeLookup: x, panZoom: m, fitViewOptions: E, fitViewResolver: g, width: b, height: j, minZoom: k, maxZoom: S } = y();
    m && (await og({
      nodes: x,
      width: b,
      height: j,
      panZoom: m,
      minZoom: k,
      maxZoom: S
    }, E), g?.resolve(!0), p({ fitViewResolver: null }));
  }
  return {
    ...nc({
      nodes: e,
      edges: t,
      width: r,
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
    setNodes: (x) => {
      const { nodeLookup: m, parentLookup: E, nodeOrigin: g, elevateNodesOnSelect: b, fitViewQueued: j, zIndexMode: k, nodesSelectionActive: S } = y(), { nodesInitialized: _, hasSelectedNodes: M } = Pi(x, m, E, {
        nodeOrigin: g,
        nodeExtent: f,
        elevateNodesOnSelect: b,
        checkEquality: !0,
        zIndexMode: k
      }), W = S && M;
      j && _ ? (v(), p({
        nodes: x,
        nodesInitialized: _,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: W
      })) : p({ nodes: x, nodesInitialized: _, nodesSelectionActive: W });
    },
    setEdges: (x) => {
      const { connectionLookup: m, edgeLookup: E } = y();
      Vl(m, E, x), p({ edges: x });
    },
    setDefaultNodesAndEdges: (x, m) => {
      if (x) {
        const { setNodes: E } = y();
        E(x), p({ hasDefaultNodes: !0 });
      }
      if (m) {
        const { setEdges: E } = y();
        E(m), p({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (x) => {
      const { triggerNodeChanges: m, nodeLookup: E, parentLookup: g, domNode: b, nodeOrigin: j, nodeExtent: k, debug: S, fitViewQueued: _, zIndexMode: M } = y(), { changes: W, updatedInternals: D } = Eg(x, E, g, b, j, k, M);
      D && (Ng(E, g, { nodeOrigin: j, nodeExtent: k, zIndexMode: M }), _ ? (v(), p({ fitViewQueued: !1, fitViewOptions: void 0 })) : p({}), W?.length > 0 && (S && console.log("React Flow: trigger node changes", W), m?.(W)));
    },
    updateNodePositions: (x, m = !1) => {
      const E = [];
      let g = [];
      const { nodeLookup: b, triggerNodeChanges: j, connection: k, updateConnection: S, onNodesChangeMiddlewareMap: _ } = y();
      for (const [M, W] of x) {
        const D = b.get(M), T = !!(D?.expandParent && D?.parentId && W?.position), R = {
          id: M,
          type: "position",
          position: T ? {
            x: Math.max(0, W.position.x),
            y: Math.max(0, W.position.y)
          } : W.position,
          dragging: m
        };
        if (D && k.inProgress && k.fromNode.id === D.id) {
          const N = Ft(D, k.fromHandle, ne.Left, !0);
          S({ ...k, from: N });
        }
        T && D.parentId && E.push({
          id: M,
          parentId: D.parentId,
          rect: {
            ...W.internals.positionAbsolute,
            width: W.measured.width ?? 0,
            height: W.measured.height ?? 0
          }
        }), g.push(R);
      }
      if (E.length > 0) {
        const { parentLookup: M, nodeOrigin: W } = y(), D = hs(E, b, M, W);
        g.push(...D);
      }
      for (const M of _.values())
        g = M(g);
      j(g);
    },
    triggerNodeChanges: (x) => {
      const { onNodesChange: m, setNodes: E, nodes: g, hasDefaultNodes: b, debug: j } = y();
      if (x?.length) {
        if (b) {
          const k = eu(x, g);
          E(k);
        }
        j && console.log("React Flow: trigger node changes", x), m?.(x);
      }
    },
    triggerEdgeChanges: (x) => {
      const { onEdgesChange: m, setEdges: E, edges: g, hasDefaultEdges: b, debug: j } = y();
      if (x?.length) {
        if (b) {
          const k = tu(x, g);
          E(k);
        }
        j && console.log("React Flow: trigger edge changes", x), m?.(x);
      }
    },
    addSelectedNodes: (x) => {
      const { multiSelectionActive: m, edgeLookup: E, nodeLookup: g, triggerNodeChanges: b, triggerEdgeChanges: j } = y();
      if (m) {
        const k = x.map((S) => $t(S, !0));
        b(k);
        return;
      }
      b(sn(g, /* @__PURE__ */ new Set([...x]), !0)), j(sn(E));
    },
    addSelectedEdges: (x) => {
      const { multiSelectionActive: m, edgeLookup: E, nodeLookup: g, triggerNodeChanges: b, triggerEdgeChanges: j } = y();
      if (m) {
        const k = x.map((S) => $t(S, !0));
        j(k);
        return;
      }
      j(sn(E, /* @__PURE__ */ new Set([...x]))), b(sn(g, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: x, edges: m } = {}) => {
      const { edges: E, nodes: g, nodeLookup: b, triggerNodeChanges: j, triggerEdgeChanges: k } = y(), S = x || g, _ = m || E, M = [];
      for (const D of S) {
        if (!D.selected)
          continue;
        const T = b.get(D.id);
        T && (T.selected = !1), M.push($t(D.id, !1));
      }
      const W = [];
      for (const D of _)
        D.selected && W.push($t(D.id, !1));
      j(M), k(W);
    },
    setMinZoom: (x) => {
      const { panZoom: m, maxZoom: E } = y();
      m?.setScaleExtent([x, E]), p({ minZoom: x });
    },
    setMaxZoom: (x) => {
      const { panZoom: m, minZoom: E } = y();
      m?.setScaleExtent([E, x]), p({ maxZoom: x });
    },
    setTranslateExtent: (x) => {
      y().panZoom?.setTranslateExtent(x), p({ translateExtent: x });
    },
    resetSelectedElements: () => {
      const { edges: x, nodes: m, triggerNodeChanges: E, triggerEdgeChanges: g, elementsSelectable: b } = y();
      if (!b)
        return;
      const j = m.reduce((S, _) => _.selected ? [...S, $t(_.id, !1)] : S, []), k = x.reduce((S, _) => _.selected ? [...S, $t(_.id, !1)] : S, []);
      E(j), g(k);
    },
    setNodeExtent: (x) => {
      const { nodes: m, nodeLookup: E, parentLookup: g, nodeOrigin: b, elevateNodesOnSelect: j, nodeExtent: k, zIndexMode: S } = y();
      x[0][0] === k[0][0] && x[0][1] === k[0][1] && x[1][0] === k[1][0] && x[1][1] === k[1][1] || (Pi(m, E, g, {
        nodeOrigin: b,
        nodeExtent: x,
        elevateNodesOnSelect: j,
        checkEquality: !1,
        zIndexMode: S
      }), p({ nodeExtent: x }));
    },
    panBy: (x) => {
      const { transform: m, width: E, height: g, panZoom: b, translateExtent: j } = y();
      return Cg({ delta: x, panZoom: b, transform: m, translateExtent: j, width: E, height: g });
    },
    setCenter: async (x, m, E) => {
      const { width: g, height: b, maxZoom: j, panZoom: k } = y();
      if (!k)
        return !1;
      const S = typeof E?.zoom < "u" ? E.zoom : j;
      return await k.setViewport({
        x: g / 2 - x * S,
        y: b / 2 - m * S,
        zoom: S
      }, { duration: E?.duration, ease: E?.ease, interpolate: E?.interpolate }), !0;
    },
    cancelConnection: () => {
      p({
        connection: { ...vl }
      });
    },
    updateConnection: (x) => {
      p({ connection: x });
    },
    reset: () => p({ ...nc() })
  };
}, Object.is);
function Yx({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: s, initialMinZoom: a, initialMaxZoom: c, initialFitViewOptions: u, fitView: l, nodeOrigin: d, nodeExtent: f, zIndexMode: h, children: p }) {
  const [y] = X(() => Ux({
    nodes: e,
    edges: t,
    defaultNodes: n,
    defaultEdges: o,
    width: r,
    height: s,
    fitView: l,
    minZoom: a,
    maxZoom: c,
    fitViewOptions: u,
    nodeOrigin: d,
    nodeExtent: f,
    zIndexMode: h
  }));
  return i.jsx(dm, { value: y, children: i.jsx(Pm, { children: p }) });
}
function Zx({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: s, height: a, fitView: c, fitViewOptions: u, minZoom: l, maxZoom: d, nodeOrigin: f, nodeExtent: h, zIndexMode: p }) {
  return ho($r) ? i.jsx(i.Fragment, { children: e }) : i.jsx(Yx, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: s, initialHeight: a, fitView: c, initialFitViewOptions: u, initialMinZoom: l, initialMaxZoom: d, nodeOrigin: f, nodeExtent: h, zIndexMode: p, children: e });
}
const Gx = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function Jx({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: s, edgeTypes: a, onNodeClick: c, onEdgeClick: u, onInit: l, onMove: d, onMoveStart: f, onMoveEnd: h, onConnect: p, onConnectStart: y, onConnectEnd: v, onClickConnectStart: x, onClickConnectEnd: m, onNodeMouseEnter: E, onNodeMouseMove: g, onNodeMouseLeave: b, onNodeContextMenu: j, onNodeDoubleClick: k, onNodeDragStart: S, onNodeDrag: _, onNodeDragStop: M, onNodesDelete: W, onEdgesDelete: D, onDelete: T, onSelectionChange: R, onSelectionDragStart: N, onSelectionDrag: A, onSelectionDragStop: I, onSelectionContextMenu: $, onSelectionStart: z, onSelectionEnd: P, onBeforeDelete: B, connectionMode: K, connectionLineType: O = bt.Bezier, connectionLineStyle: Z, connectionLineComponent: q, connectionLineContainerStyle: oe, deleteKeyCode: se = "Backspace", selectionKeyCode: G = "Shift", selectionOnDrag: V = !1, selectionMode: Y = io.Full, panActivationKeyCode: ae = "Space", multiSelectionKeyCode: ce = ao() ? "Meta" : "Control", zoomActivationKeyCode: J = ao() ? "Meta" : "Control", snapToGrid: te, snapGrid: fe, onlyRenderVisibleElements: H = !1, selectNodesOnDrag: Q, nodesDraggable: ye, autoPanOnNodeFocus: ve, nodesConnectable: ke, nodesFocusable: Te, nodeOrigin: Ie = Jl, edgesFocusable: Oe, edgesReconnectable: be, elementsSelectable: He = !0, defaultViewport: We = Sm, minZoom: Ge = 0.5, maxZoom: Je = 2, translateExtent: Qe = ro, preventScrolling: Ct = !0, nodeExtent: Ut, defaultMarkerColor: Ze = "#b1b1b7", zoomOnScroll: Yt = !0, zoomOnPinch: jn = !0, panOnScroll: Sn = !1, panOnScrollSpeed: So = 0.5, panOnScrollMode: kn = Lt.Free, zoomOnDoubleClick: Be = !0, panOnDrag: ut = !0, onPaneClick: dt, onPaneMouseEnter: Zt, onPaneMouseMove: Br, onPaneMouseLeave: En, onPaneScroll: Gt, onPaneContextMenu: mt, paneClickDistance: It = 1, nodeClickDistance: At = 0, children: ze, onReconnect: Cn, onReconnectStart: ko, onReconnectEnd: Fe, onEdgeContextMenu: Eo, onEdgeDoubleClick: we, onEdgeMouseEnter: Ke, onEdgeMouseMove: Jt, onEdgeMouseLeave: Co, reconnectRadius: ge = 10, onNodesChange: _t, onEdgesChange: Io, noDragClassName: In = "nodrag", noWheelClassName: An = "nowheel", noPanClassName: _e = "nopan", fitView: xt, fitViewOptions: ft, connectOnClick: Fr, attributionPosition: Ao, proOptions: _o, defaultEdgeOptions: _n, elevateNodesOnSelect: Kr = !0, elevateEdgesOnSelect: Dn = !1, disableKeyboardA11y: Dt = !1, autoPanOnConnect: $e, autoPanOnNodeDrag: Mn, autoPanOnSelection: Tn = !0, autoPanSpeed: $n, connectionRadius: Qt, isValidConnection: Xr, onError: qr, style: Ur, id: Do, nodeDragThreshold: Mo, connectionDragThreshold: To, viewport: $o, onViewportChange: Yr, width: Pn, height: Zr, colorMode: Gr = "light", debug: Jr, onScroll: Mt, ariaLabelConfig: en, zIndexMode: tn = "basic", ...zn }, Qr) {
  const Ln = Do || "1", ei = Im(Gr), ti = ue((Po) => {
    Po.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), Mt?.(Po);
  }, [Mt]);
  return i.jsx("div", { "data-testid": "rf__wrapper", ...zn, onScroll: ti, style: { ...Ur, ...Gx }, ref: Qr, className: Ee(["react-flow", r, ei]), id: Do, role: "application", children: i.jsxs(Zx, { nodes: e, edges: t, width: Pn, height: Zr, fitView: xt, fitViewOptions: ft, minZoom: Ge, maxZoom: Je, nodeOrigin: Ie, nodeExtent: Ut, zIndexMode: tn, children: [i.jsx(Cm, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: p, onConnectStart: y, onConnectEnd: v, onClickConnectStart: x, onClickConnectEnd: m, nodesDraggable: ye, autoPanOnNodeFocus: ve, nodesConnectable: ke, nodesFocusable: Te, edgesFocusable: Oe, edgesReconnectable: be, elementsSelectable: He, elevateNodesOnSelect: Kr, elevateEdgesOnSelect: Dn, minZoom: Ge, maxZoom: Je, nodeExtent: Ut, onNodesChange: _t, onEdgesChange: Io, snapToGrid: te, snapGrid: fe, connectionMode: K, translateExtent: Qe, connectOnClick: Fr, defaultEdgeOptions: _n, fitView: xt, fitViewOptions: ft, onNodesDelete: W, onEdgesDelete: D, onDelete: T, onNodeDragStart: S, onNodeDrag: _, onNodeDragStop: M, onSelectionDrag: A, onSelectionDragStart: N, onSelectionDragStop: I, onMove: d, onMoveStart: f, onMoveEnd: h, noPanClassName: _e, nodeOrigin: Ie, rfId: Ln, autoPanOnConnect: $e, autoPanOnNodeDrag: Mn, autoPanSpeed: $n, onError: qr, connectionRadius: Qt, isValidConnection: Xr, selectNodesOnDrag: Q, nodeDragThreshold: Mo, connectionDragThreshold: To, onBeforeDelete: B, debug: Jr, ariaLabelConfig: en, zIndexMode: tn }), i.jsx(Xx, { onInit: l, onNodeClick: c, onEdgeClick: u, onNodeMouseEnter: E, onNodeMouseMove: g, onNodeMouseLeave: b, onNodeContextMenu: j, onNodeDoubleClick: k, nodeTypes: s, edgeTypes: a, connectionLineType: O, connectionLineStyle: Z, connectionLineComponent: q, connectionLineContainerStyle: oe, selectionKeyCode: G, selectionOnDrag: V, selectionMode: Y, deleteKeyCode: se, multiSelectionKeyCode: ce, panActivationKeyCode: ae, zoomActivationKeyCode: J, onlyRenderVisibleElements: H, defaultViewport: We, translateExtent: Qe, minZoom: Ge, maxZoom: Je, preventScrolling: Ct, zoomOnScroll: Yt, zoomOnPinch: jn, zoomOnDoubleClick: Be, panOnScroll: Sn, panOnScrollSpeed: So, panOnScrollMode: kn, panOnDrag: ut, autoPanOnSelection: Tn, onPaneClick: dt, onPaneMouseEnter: Zt, onPaneMouseMove: Br, onPaneMouseLeave: En, onPaneScroll: Gt, onPaneContextMenu: mt, paneClickDistance: It, nodeClickDistance: At, onSelectionContextMenu: $, onSelectionStart: z, onSelectionEnd: P, onReconnect: Cn, onReconnectStart: ko, onReconnectEnd: Fe, onEdgeContextMenu: Eo, onEdgeDoubleClick: we, onEdgeMouseEnter: Ke, onEdgeMouseMove: Jt, onEdgeMouseLeave: Co, reconnectRadius: ge, defaultMarkerColor: Ze, noDragClassName: In, noWheelClassName: An, noPanClassName: _e, rfId: Ln, disableKeyboardA11y: Dt, nodeExtent: Ut, viewport: $o, onViewportChange: Yr }), i.jsx(jm, { onSelectionChange: R }), ze, i.jsx(xm, { proOptions: _o, position: Ao }), i.jsx(mm, { rfId: Ln, disableKeyboardA11y: Dt })] }) });
}
var Iu = ru(Jx);
const Qx = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function ew({ children: e }) {
  const t = pe(Qx);
  return t ? um.createPortal(e, t) : null;
}
function tw({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return i.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ee(["react-flow__background-pattern", n, o]) });
}
function nw({ radius: e, className: t }) {
  return i.jsx("circle", { cx: e, cy: e, r: e, className: Ee(["react-flow__background-pattern", "dots", t]) });
}
var jt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(jt || (jt = {}));
const ow = {
  [jt.Dots]: 1,
  [jt.Lines]: 1,
  [jt.Cross]: 6
}, rw = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function Au({
  id: e,
  variant: t = jt.Dots,
  // only used for dots and cross
  gap: n = 20,
  // only used for lines and cross
  size: o,
  lineWidth: r = 1,
  offset: s = 0,
  color: a,
  bgColor: c,
  style: u,
  className: l,
  patternClassName: d
}) {
  const f = ie(null), { transform: h, patternId: p } = pe(rw, Ne), y = o || ow[t], v = t === jt.Dots, x = t === jt.Cross, m = Array.isArray(n) ? n : [n, n], E = [m[0] * h[2] || 1, m[1] * h[2] || 1], g = y * h[2], b = Array.isArray(s) ? s : [s, s], j = x ? [g, g] : E, k = [
    b[0] * h[2] || 1 + j[0] / 2,
    b[1] * h[2] || 1 + j[1] / 2
  ], S = `${p}${e || ""}`;
  return i.jsxs("svg", { className: Ee(["react-flow__background", l]), style: {
    ...u,
    ...zr,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": a
  }, ref: f, "data-testid": "rf__background", children: [i.jsx("pattern", { id: S, x: h[0] % E[0], y: h[1] % E[1], width: E[0], height: E[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${k[0]},-${k[1]})`, children: v ? i.jsx(nw, { radius: g / 2, className: d }) : i.jsx(tw, { dimensions: j, lineWidth: r, variant: t, className: d }) }), i.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${S})` })] });
}
Au.displayName = "Background";
const _u = Se(Au);
function iw() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: i.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function sw() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: i.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function aw() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: i.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function cw() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: i.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function lw() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: i.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function Ko({ children: e, className: t, ...n }) {
  return i.jsx("button", { type: "button", className: Ee(["react-flow__controls-button", t]), ...n, children: e });
}
const uw = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function Du({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: s, onZoomOut: a, onFitView: c, onInteractiveChange: u, className: l, children: d, position: f = "bottom-left", orientation: h = "vertical", "aria-label": p }) {
  const y = je(), { isInteractive: v, minZoomReached: x, maxZoomReached: m, ariaLabelConfig: E } = pe(uw, Ne), { zoomIn: g, zoomOut: b, fitView: j } = ps(), k = () => {
    g(), s?.();
  }, S = () => {
    b(), a?.();
  }, _ = () => {
    j(r), c?.();
  }, M = () => {
    y.setState({
      nodesDraggable: !v,
      nodesConnectable: !v,
      elementsSelectable: !v
    }), u?.(!v);
  }, W = h === "horizontal" ? "horizontal" : "vertical";
  return i.jsxs(Pr, { className: Ee(["react-flow__controls", W, l]), position: f, style: e, "data-testid": "rf__controls", "aria-label": p ?? E["controls.ariaLabel"], children: [t && i.jsxs(i.Fragment, { children: [i.jsx(Ko, { onClick: k, className: "react-flow__controls-zoomin", title: E["controls.zoomIn.ariaLabel"], "aria-label": E["controls.zoomIn.ariaLabel"], disabled: m, children: i.jsx(iw, {}) }), i.jsx(Ko, { onClick: S, className: "react-flow__controls-zoomout", title: E["controls.zoomOut.ariaLabel"], "aria-label": E["controls.zoomOut.ariaLabel"], disabled: x, children: i.jsx(sw, {}) })] }), n && i.jsx(Ko, { className: "react-flow__controls-fitview", onClick: _, title: E["controls.fitView.ariaLabel"], "aria-label": E["controls.fitView.ariaLabel"], children: i.jsx(aw, {}) }), o && i.jsx(Ko, { className: "react-flow__controls-interactive", onClick: M, title: E["controls.interactive.ariaLabel"], "aria-label": E["controls.interactive.ariaLabel"], children: v ? i.jsx(lw, {}) : i.jsx(cw, {}) }), d] });
}
Du.displayName = "Controls";
const Mu = Se(Du);
function dw({ id: e, x: t, y: n, width: o, height: r, style: s, color: a, strokeColor: c, strokeWidth: u, className: l, borderRadius: d, shapeRendering: f, selected: h, onClick: p }) {
  const { background: y, backgroundColor: v } = s || {}, x = a || y || v;
  return i.jsx("rect", { className: Ee(["react-flow__minimap-node", { selected: h }, l]), x: t, y: n, rx: d, ry: d, width: o, height: r, style: {
    fill: x,
    stroke: c,
    strokeWidth: u
  }, shapeRendering: f, onClick: p ? (m) => p(m, e) : void 0 });
}
const fw = Se(dw), hw = (e) => e.nodes.map((t) => t.id), wi = (e) => e instanceof Function ? e : () => e;
function pw({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: s = fw,
  onClick: a
}) {
  const c = pe(hw, Ne), u = wi(t), l = wi(e), d = wi(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return i.jsx(i.Fragment, { children: c.map((h) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    i.jsx(gw, { id: h, nodeColorFunc: u, nodeStrokeColorFunc: l, nodeClassNameFunc: d, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: s, onClick: a, shapeRendering: f }, h)
  )) });
}
function yw({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: s, shapeRendering: a, NodeComponent: c, onClick: u }) {
  const { node: l, x: d, y: f, width: h, height: p } = pe((y) => {
    const v = y.nodeLookup.get(e);
    if (!v)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const x = v.internals.userNode, { x: m, y: E } = v.internals.positionAbsolute, { width: g, height: b } = gt(x);
    return {
      node: x,
      x: m,
      y: E,
      width: g,
      height: b
    };
  }, Ne);
  return !l || l.hidden || !Cl(l) ? null : i.jsx(c, { x: d, y: f, width: h, height: p, style: l.style, selected: !!l.selected, className: o(l), color: t(l), borderRadius: r, strokeColor: n(l), strokeWidth: s, shapeRendering: a, onClick: u, id: l.id });
}
const gw = Se(yw);
var mw = Se(pw);
const xw = 200, ww = 150, vw = (e) => !e.hidden, bw = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? kl(mo(e.nodeLookup, { filter: vw }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, Nw = "react-flow__minimap-desc";
function Tu({
  style: e,
  className: t,
  nodeStrokeColor: n,
  nodeColor: o,
  nodeClassName: r = "",
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
  onNodeClick: y,
  pannable: v = !1,
  zoomable: x = !1,
  ariaLabel: m,
  inversePan: E,
  zoomStep: g = 1,
  offsetScale: b = 5
}) {
  const j = je(), k = ie(null), { boundingRect: S, viewBB: _, rfId: M, panZoom: W, translateExtent: D, flowWidth: T, flowHeight: R, ariaLabelConfig: N } = pe(bw, Ne), A = e?.width ?? xw, I = e?.height ?? ww, $ = S.width / A, z = S.height / I, P = Math.max($, z), B = P * A, K = P * I, O = b * P, Z = S.x - (B - S.width) / 2 - O, q = S.y - (K - S.height) / 2 - O, oe = B + O * 2, se = K + O * 2, G = `${Nw}-${M}`, V = ie(0), Y = ie();
  V.current = P, ee(() => {
    if (k.current && W)
      return Y.current = zg({
        domNode: k.current,
        panZoom: W,
        getTransform: () => j.getState().transform,
        getViewScale: () => V.current
      }), () => {
        Y.current?.destroy();
      };
  }, [W]), ee(() => {
    Y.current?.update({
      translateExtent: D,
      width: T,
      height: R,
      inversePan: E,
      pannable: v,
      zoomStep: g,
      zoomable: x
    });
  }, [v, x, E, g, D, T, R]);
  const ae = p ? (te) => {
    const [fe, H] = Y.current?.pointer(te) || [0, 0];
    p(te, { x: fe, y: H });
  } : void 0, ce = y ? ue((te, fe) => {
    const H = j.getState().nodeLookup.get(fe).internals.userNode;
    y(te, H);
  }, []) : void 0, J = m ?? N["minimap.ariaLabel"];
  return i.jsx(Pr, { position: h, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * P : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof a == "number" ? a : void 0
  }, className: Ee(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: i.jsxs("svg", { width: A, height: I, viewBox: `${Z} ${q} ${oe} ${se}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": G, ref: k, onClick: ae, children: [J && i.jsx("title", { id: G, children: J }), i.jsx(mw, { onClick: ce, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: s, nodeClassName: r, nodeStrokeWidth: a, nodeComponent: c }), i.jsx("path", { className: "react-flow__minimap-mask", d: `M${Z - O},${q - O}h${oe + O * 2}v${se + O * 2}h${-oe - O * 2}z
        M${_.x},${_.y}h${_.width}v${_.height}h${-_.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
Tu.displayName = "MiniMap";
const $u = Se(Tu), jw = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, Sw = {
  [gn.Line]: "right",
  [gn.Handle]: "bottom-right"
};
function kw({ nodeId: e, position: t, variant: n = gn.Handle, className: o, style: r = void 0, children: s, color: a, minWidth: c = 10, minHeight: u = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: h, autoScale: p = !0, shouldResize: y, onResizeStart: v, onResize: x, onResizeEnd: m }) {
  const E = cu(), g = typeof e == "string" ? e : E, b = je(), j = ie(null), k = n === gn.Handle, S = pe(ue(jw(k && p), [k, p]), Ne), _ = ie(null), M = t ?? Sw[n];
  ee(() => {
    if (!(!j.current || !g))
      return _.current || (_.current = Yg({
        domNode: j.current,
        nodeId: g,
        getStoreItems: () => {
          const { nodeLookup: D, transform: T, snapGrid: R, snapToGrid: N, nodeOrigin: A, domNode: I } = b.getState();
          return {
            nodeLookup: D,
            transform: T,
            snapGrid: R,
            snapToGrid: N,
            nodeOrigin: A,
            paneDomNode: I
          };
        },
        onChange: (D, T) => {
          const { triggerNodeChanges: R, nodeLookup: N, parentLookup: A, nodeOrigin: I } = b.getState(), $ = [], z = { x: D.x, y: D.y }, P = N.get(g);
          if (P && P.expandParent && P.parentId) {
            const B = P.origin ?? I, K = D.width ?? P.measured.width ?? 0, O = D.height ?? P.measured.height ?? 0, Z = {
              id: P.id,
              parentId: P.parentId,
              rect: {
                width: K,
                height: O,
                ...Il({
                  x: D.x ?? P.position.x,
                  y: D.y ?? P.position.y
                }, { width: K, height: O }, P.parentId, N, B)
              }
            }, q = hs([Z], N, A, I);
            $.push(...q), z.x = D.x ? Math.max(B[0] * K, D.x) : void 0, z.y = D.y ? Math.max(B[1] * O, D.y) : void 0;
          }
          if (z.x !== void 0 && z.y !== void 0) {
            const B = {
              id: g,
              type: "position",
              position: { ...z }
            };
            $.push(B);
          }
          if (D.width !== void 0 && D.height !== void 0) {
            const K = {
              id: g,
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
          R($);
        },
        onEnd: ({ width: D, height: T }) => {
          const R = {
            id: g,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: D,
              height: T
            }
          };
          b.getState().triggerNodeChanges([R]);
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
        onResize: x,
        onResizeEnd: m,
        shouldResize: y
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
    x,
    m,
    y
  ]);
  const W = M.split("-");
  return i.jsx("div", { className: Ee(["react-flow__resize-control", "nodrag", ...W, n, o]), ref: j, style: {
    ...r,
    scale: S,
    ...a && { [k ? "backgroundColor" : "borderColor"]: a }
  }, children: s });
}
Se(kw);
const Ew = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), Pu = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var Cw = {
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
const Iw = kr(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: o,
    className: r = "",
    children: s,
    iconNode: a,
    ...c
  }, u) => Si(
    "svg",
    {
      ref: u,
      ...Cw,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: Pu("lucide", r),
      ...c
    },
    [
      ...a.map(([l, d]) => Si(l, d)),
      ...Array.isArray(s) ? s : [s]
    ]
  )
);
const he = (e, t) => {
  const n = kr(
    ({ className: o, ...r }, s) => Si(Iw, {
      ref: s,
      iconNode: t,
      className: Pu(`lucide-${Ew(e)}`, o),
      ...r
    })
  );
  return n.displayName = `${e}`, n;
};
const Aw = he("Activity", [
  [
    "path",
    {
      d: "M22 12h-2.48a2 2 0 0 0-1.93 1.46l-2.35 8.36a.25.25 0 0 1-.48 0L9.24 2.18a.25.25 0 0 0-.48 0l-2.35 8.36A2 2 0 0 1 4.49 12H2",
      key: "169zse"
    }
  ]
]);
const Lr = he("Boxes", [
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
const bn = he("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const zu = he("ChevronDown", [
  ["path", { d: "m6 9 6 6 6-6", key: "qrunsl" }]
]);
const yr = he("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const on = he("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const _w = he("ChevronUp", [["path", { d: "m18 15-6-6-6 6", key: "153udz" }]]);
const Et = he("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const Dw = he("CodeXml", [
  ["path", { d: "m18 16 4-4-4-4", key: "1inbqp" }],
  ["path", { d: "m6 8-4 4 4 4", key: "15zrgr" }],
  ["path", { d: "m14.5 4-5 16", key: "e7oirm" }]
]);
const Mw = he("Copy", [
  ["rect", { width: "14", height: "14", x: "8", y: "8", rx: "2", ry: "2", key: "17jyea" }],
  ["path", { d: "M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2", key: "zix9uf" }]
]);
const Tw = he("Download", [
  ["path", { d: "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4", key: "ih7n3h" }],
  ["polyline", { points: "7 10 12 15 17 10", key: "2ggqvy" }],
  ["line", { x1: "12", x2: "12", y1: "15", y2: "3", key: "1vk2je" }]
]);
const oc = he("EyeOff", [
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
const Lu = he("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const Ru = he("GripVertical", [
  ["circle", { cx: "9", cy: "12", r: "1", key: "1vctgf" }],
  ["circle", { cx: "9", cy: "5", r: "1", key: "hp0tcf" }],
  ["circle", { cx: "9", cy: "19", r: "1", key: "fkjjf6" }],
  ["circle", { cx: "15", cy: "12", r: "1", key: "1tmaij" }],
  ["circle", { cx: "15", cy: "5", r: "1", key: "19l28e" }],
  ["circle", { cx: "15", cy: "19", r: "1", key: "f4zoj3" }]
]);
const $w = he("ListChecks", [
  ["path", { d: "m3 17 2 2 4-4", key: "1jhpwq" }],
  ["path", { d: "m3 7 2 2 4-4", key: "1obspn" }],
  ["path", { d: "M13 6h8", key: "15sg57" }],
  ["path", { d: "M13 12h8", key: "h98zly" }],
  ["path", { d: "M13 18h8", key: "oe0vm4" }]
]);
const gs = he("ListTree", [
  ["path", { d: "M21 12h-8", key: "1bmf0i" }],
  ["path", { d: "M21 6H8", key: "1pqkrb" }],
  ["path", { d: "M21 18h-8", key: "1tm79t" }],
  ["path", { d: "M3 6v4c0 1.1.9 2 2 2h3", key: "1ywdgy" }],
  ["path", { d: "M3 10v6c0 1.1.9 2 2 2h3", key: "2wc746" }]
]);
const gr = he("Maximize2", [
  ["polyline", { points: "15 3 21 3 21 9", key: "mznyad" }],
  ["polyline", { points: "9 21 3 21 3 15", key: "1avn1i" }],
  ["line", { x1: "21", x2: "14", y1: "3", y2: "10", key: "ota7mn" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const rc = he("Minimize2", [
  ["polyline", { points: "4 14 10 14 10 20", key: "11kfnr" }],
  ["polyline", { points: "20 10 14 10 14 4", key: "rlmsce" }],
  ["line", { x1: "14", x2: "21", y1: "10", y2: "3", key: "o5lafz" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
const Pw = he("Network", [
  ["rect", { x: "16", y: "16", width: "6", height: "6", rx: "1", key: "4q2zg0" }],
  ["rect", { x: "2", y: "16", width: "6", height: "6", rx: "1", key: "8cvhb9" }],
  ["rect", { x: "9", y: "2", width: "6", height: "6", rx: "1", key: "1egb70" }],
  ["path", { d: "M5 16v-3a1 1 0 0 1 1-1h12a1 1 0 0 1 1 1v3", key: "1jsf9p" }],
  ["path", { d: "M12 12V8", key: "2874zd" }]
]);
const Vu = he("Package", [
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
const xn = he("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const wn = he("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const zw = he("Redo2", [
  ["path", { d: "m15 14 5-5-5-5", key: "12vg1m" }],
  ["path", { d: "M20 9H9.5A5.5 5.5 0 0 0 4 14.5A5.5 5.5 0 0 0 9.5 20H13", key: "6uklza" }]
]);
const ms = he("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const Ou = he("Save", [
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
const Rr = he("Search", [
  ["circle", { cx: "11", cy: "11", r: "8", key: "4ej97u" }],
  ["path", { d: "m21 21-4.3-4.3", key: "1qie3q" }]
]);
const ic = he("Shield", [
  [
    "path",
    {
      d: "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z",
      key: "oel41y"
    }
  ]
]);
const xs = he("SlidersHorizontal", [
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
const St = he("Sparkles", [
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
const Lw = he("Terminal", [
  ["polyline", { points: "4 17 10 11 4 5", key: "akl6gq" }],
  ["line", { x1: "12", x2: "20", y1: "19", y2: "19", key: "q2wloq" }]
]);
const lo = he("Trash2", [
  ["path", { d: "M3 6h18", key: "d0wm0j" }],
  ["path", { d: "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", key: "4alrt4" }],
  ["path", { d: "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", key: "v07s0e" }],
  ["line", { x1: "10", x2: "10", y1: "11", y2: "17", key: "1uufr5" }],
  ["line", { x1: "14", x2: "14", y1: "11", y2: "17", key: "xtxkd" }]
]);
const mr = he("TriangleAlert", [
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
const Rw = he("Undo2", [
  ["path", { d: "M9 14 4 9l5-5", key: "102s5s" }],
  ["path", { d: "M4 9h10.5a5.5 5.5 0 0 1 5.5 5.5a5.5 5.5 0 0 1-5.5 5.5H11", key: "f3b9sd" }]
]);
const Vw = he("Workflow", [
  ["rect", { width: "8", height: "8", x: "3", y: "3", rx: "2", key: "by2w9f" }],
  ["path", { d: "M7 11v4a2 2 0 0 0 2 2h4", key: "xkn7yn" }],
  ["rect", { width: "8", height: "8", x: "13", y: "13", rx: "2", key: "1cgmvn" }]
]);
const Ow = he("Wrench", [
  [
    "path",
    {
      d: "M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z",
      key: "cbrjhi"
    }
  ]
]);
const Hu = he("X", [
  ["path", { d: "M18 6 6 18", key: "1bl5f8" }],
  ["path", { d: "m6 6 12 12", key: "d8bk6v" }]
]);
const Hw = he("Zap", [
  [
    "path",
    {
      d: "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z",
      key: "1xq2db"
    }
  ]
]), Ww = /* @__PURE__ */ new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);
function vo(e) {
  return Wu(e, Bw);
}
function Vr(e) {
  return Wu(e, Fw);
}
function Wu(e, t) {
  return !e || !e.rootActivity ? e : { ...e, rootActivity: Bu(e.rootActivity, t) };
}
function Bu(e, t) {
  const n = t(e), o = n.structure;
  if (!o || !Kt(o.payload)) return n;
  let r = !1;
  const s = { ...o.payload };
  for (const [a, c] of Object.entries(o.payload))
    Array.isArray(c) && c.length > 0 && c.every(Zw) && (s[a] = c.map((u) => Bu(u, t)), r = !0);
  return r ? { ...n, structure: { ...o, payload: s } } : n;
}
function Bw(e) {
  const t = [], n = {};
  for (const [r, s] of Object.entries(e))
    Ww.has(r) || (Yw(s) ? t.push({
      referenceKey: Kw(r),
      value: { value: qw(s.expression), expressionType: s.expression.type || "Literal" }
    }) : n[r] = s);
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
function Fw(e) {
  const t = Array.isArray(e.inputs) ? e.inputs : [], n = {};
  for (const o of t) {
    if (!Kt(o) || typeof o.referenceKey != "string") continue;
    const r = Kt(o.value) ? o.value : {};
    n[Xw(o.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof r.expressionType == "string" ? r.expressionType : "Literal",
        value: r.value ?? ""
      }
    };
  }
  return { ...e, ...n, inputs: [] };
}
function Kw(e) {
  return e && e.charAt(0).toUpperCase() + e.slice(1);
}
function Xw(e) {
  return e && e.charAt(0).toLowerCase() + e.slice(1);
}
function qw(e) {
  return (e.type || "Literal") === "Variable" && Kt(e.value) ? e.value : Uw(e.value);
}
function Uw(e) {
  return e == null ? null : typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function Yw(e) {
  if (!Kt(e) || Array.isArray(e)) return !1;
  const t = e.expression;
  return Kt(t) && typeof t.type == "string";
}
function Zw(e) {
  return Kt(e) && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Kt(e) {
  return typeof e == "object" && e !== null;
}
const bo = "elsa.sequence.structure", Nn = "elsa.flowchart.structure";
function Fu(e, t) {
  if (!e) return null;
  let n = e;
  for (const o of t) {
    const r = Ve(n).find((a) => a.id === o.slotId);
    if (!r) return null;
    const s = r.activities.find((a) => a.nodeId === o.ownerNodeId);
    if (!s) return null;
    n = s;
  }
  return n;
}
function Gw(e, t, n = (o) => o.nodeId) {
  if (!e) return null;
  if (e.nodeId === t) return [];
  const o = (r, s) => {
    const a = Ve(r);
    if (a[0]?.activities.some((c) => c.nodeId === t)) return s;
    for (const c of a)
      for (const u of c.activities) {
        const l = o(u, [...s, { ownerNodeId: u.nodeId, slotId: c.id, label: n(u) }]);
        if (l) return l;
      }
    return null;
  };
  return o(e, []);
}
function Jn(e, t) {
  const n = Fu(e, t);
  if (!n) return null;
  let o = Ve(n)[0];
  return o ? { owner: n, slot: o } : null;
}
function Ve(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = g0(t), r = vi(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: m0(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, s]) => vi(s)).map(([s, a]) => ({
    id: `${t.kind}:${s}`,
    label: w0(s),
    property: s,
    mode: "generic",
    activities: vi(a) ?? []
  }));
}
function Ku(e, t, n) {
  const o = new Map(t.map((a) => [a.activityVersionId, a])), r = new Map(n.map((a) => [a.nodeId, a])), s = e.slot.activities.map((a, c) => {
    const u = o.get(a.activityVersionId), l = r.get(a.nodeId) ?? x0(e.slot.mode, c);
    return Uu(a, u, { x: l.x, y: l.y });
  });
  return {
    nodes: s,
    edges: e.slot.mode === "flowchart" ? Yu(e.owner) : c0(e.slot, s)
  };
}
function Ri(e, t, n) {
  const o = t.find((s) => s.activityVersionId === e.activityVersionId), r = n.find((s) => s.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [Uu(e, o, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function Jw(e, t, n, o = null) {
  const r = new Map(t.map((c) => [c.activityExecutionId, c])), s = ac(t, (c) => c.authoredActivityId || c.executableNodeId), a = ac(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? r.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const u = s.get(c.id) ?? [], l = a.get(c.id) ?? [];
    if (u.length === 0 && l.length === 0) return c;
    const d = h0(u), f = o === c.id || u.some((p) => p.activityExecutionId === o) || l.some((p) => p.incidentId === o), h = {
      status: d?.status,
      subStatus: d?.subStatus,
      activityExecutionId: d?.activityExecutionId,
      faultCount: u.reduce((p, y) => p + y.faultCount + y.aggregateFaultCount, 0),
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
function ws(e, t) {
  return e?.structure?.kind === Nn || o0(t) ? "flowchart" : e?.structure?.kind === bo || r0(t) ? "sequence" : "unsupported";
}
function Vi(e, t, n) {
  if (t.length === 0) {
    const c = Ve(e)[0];
    return c ? uo(e, c, n) : e;
  }
  const [o, ...r] = t, s = Ve(e).find((c) => c.id === o.slotId);
  if (!s) return e;
  const a = s.activities.map((c) => c.nodeId === o.ownerNodeId ? Vi(c, r, n) : c);
  return uo(e, s, a);
}
function Xu(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, s = Ve(e).find((c) => c.id === o.slotId);
  if (!s) return e;
  const a = s.activities.map((c) => c.nodeId === o.ownerNodeId ? Xu(c, r, n) : c);
  return uo(e, s, a);
}
function qu(e, t, n) {
  if (e.nodeId === t) return n(e);
  const o = Ve(e);
  if (o.length === 0) return e;
  let r = !1, s = e;
  for (const a of o) {
    const c = a.activities.map((u) => {
      const l = qu(u, t, n);
      return l !== u && (r = !0), l;
    });
    r && (s = uo(s, a, c));
  }
  return r ? s : e;
}
function uo(e, t, n) {
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
function Qw(e, t, n, o = []) {
  const r = new Map(e.slot.activities.map((a) => [a.nodeId, a]));
  for (const a of o)
    r.set(a.nodeId, a);
  const s = t.map((a) => r.get(a.id)).filter((a) => !!a);
  return e.slot.mode === "sequence" && s.sort((a, c) => {
    const u = t.find((d) => d.id === a.nodeId), l = t.find((d) => d.id === c.nodeId);
    return (u?.position.x ?? 0) - (l?.position.x ?? 0);
  }), uo(e.owner, e.slot, s);
}
function e0(e, t) {
  return {
    ...e,
    structure: a0(e.structure, t)
  };
}
function t0(e, t) {
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
function Oi(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: s0(e)
  };
}
function Ae(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? i0(t) : n;
}
function Uu(e, t, n, o = {}) {
  return {
    id: e.nodeId,
    type: "workflowActivity",
    position: n,
    connectable: o.connectable,
    deletable: o.deletable,
    draggable: o.draggable,
    data: {
      label: t ? Ae(t) : e.activityVersionId,
      activityVersionId: e.activityVersionId,
      activityTypeKey: t?.activityTypeKey,
      category: t?.category,
      executionType: t?.executionType,
      icon: xr(t),
      childSlots: Ve(e),
      acceptsInbound: l0(e, t),
      sourcePorts: o.suppressFlowPorts ? [] : Zu(e, t),
      suppressFlowPorts: o.suppressFlowPorts
    }
  };
}
function xr(e) {
  if (!e) return "activity";
  const t = n0(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), o = Ae(e).toLowerCase(), r = e.category?.toLowerCase() ?? "", s = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || o === "flowchart" ? "flowchart" : n.endsWith(".sequence") || o === "sequence" ? "sequence" : n.includes("writeline") || o.includes("write line") ? "terminal" : r.includes("runtime") ? "runtime" : s === "trigger" ? "trigger" : "activity";
}
function n0(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function o0(e) {
  return !!e && (Ae(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function r0(e) {
  return !!e && (Ae(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function i0(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function s0(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: bo,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: Nn,
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
function a0(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], o = /* @__PURE__ */ new Map();
  for (const r of n) {
    if (!vs(r)) continue;
    const s = r.id;
    typeof s == "string" && o.set(s, r);
  }
  return {
    ...e,
    payload: {
      ...e.payload,
      connections: t.map((r) => {
        const s = o.get(r.id) ?? {}, a = r.data?.vertices, { vertices: c, ...u } = s;
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
function c0(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function Yu(e) {
  if (e.structure?.kind !== Nn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, s = n.target;
    if (!r?.nodeId || !s?.nodeId) return null;
    const a = Array.isArray(n.vertices) ? n.vertices.filter(p0) : [];
    return {
      id: typeof n.id == "string" ? String(n.id) : `flow-${o}-${r.nodeId}-${s.nodeId}`,
      source: r.nodeId,
      target: s.nodeId,
      sourceHandle: r.port,
      targetHandle: s.port && s.port !== "Done" ? s.port : void 0,
      type: "workflow",
      label: r.port && r.port !== "Done" ? r.port : void 0,
      data: a.length ? { vertices: a } : void 0
    };
  }).filter((n) => !!n) : [];
}
function Zu(e, t) {
  const n = sc(e.cases);
  if (d0(e, t) && n.length > 0)
    return [...n.map((s) => ({ name: s, displayName: s })), { name: "Default", displayName: "Default" }];
  const o = [
    ...Qo(t?.designFacets),
    ...Qo(t?.ports),
    ...Qo(t?.outputs)
  ];
  if (o.length > 0) return f0(o);
  const r = sc(e.outcomes);
  return r.length > 0 ? r.map((s) => ({ name: s, displayName: s })) : [{ name: "Done", displayName: "Done" }];
}
function l0(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function wr(e, t, n, o) {
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
function u0(e, t, n) {
  const o = wr(t.source, n, t.sourceHandle ?? "Done", void 0), r = wr(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((s) => s.id !== t.id).concat(o, r);
}
function vi(e) {
  return Array.isArray(e) ? e.filter(y0) : null;
}
function d0(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function Qo(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!vs(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...Qo(n.ports));
      continue;
    }
    const o = typeof n.type == "string" ? n.type : typeof n.portType == "string" ? n.portType : "", r = n.isBrowsable !== !1 && n.browsable !== !1, s = typeof n.name == "string" ? n.name : typeof n.id == "string" ? n.id : "";
    if (r && o.toLowerCase() === "flow" && s) {
      const a = typeof n.displayName == "string" ? n.displayName : s;
      t.push({ name: s, displayName: a });
    }
  }
  return t;
}
function f0(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function sc(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function ac(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const o of e) {
    const r = t(o);
    r && n.set(r, [...n.get(r) ?? [], o]);
  }
  return n;
}
function h0(e) {
  return [...e].sort((t, n) => cc(n).localeCompare(cc(t)))[0];
}
function cc(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function p0(e) {
  return vs(e) && typeof e.x == "number" && typeof e.y == "number";
}
function vs(e) {
  return typeof e == "object" && e !== null;
}
function y0(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function g0(e) {
  return e.kind === bo ? "sequence" : e.kind === Nn ? "flowchart" : "generic";
}
function m0(e) {
  return e.kind === bo || e.kind === Nn, "Activities";
}
function x0(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function w0(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Gu = "String", v0 = "singleline";
function Pe(e, t, n = "") {
  return { typeName: e, namespace: t, assemblyName: n, assemblyVersion: "" };
}
const Ju = [
  Pe("String", "System", "System.Private.CoreLib"),
  Pe("Boolean", "System", "System.Private.CoreLib"),
  Pe("Int32", "System", "System.Private.CoreLib"),
  Pe("Int64", "System", "System.Private.CoreLib"),
  Pe("Double", "System", "System.Private.CoreLib"),
  Pe("Decimal", "System", "System.Private.CoreLib"),
  Pe("DateTimeOffset", "System", "System.Private.CoreLib"),
  Pe("DateTime", "System", "System.Private.CoreLib"),
  Pe("TimeSpan", "System", "System.Private.CoreLib"),
  Pe("Guid", "System", "System.Private.CoreLib"),
  Pe("Object", "System", "System.Private.CoreLib")
], Qu = Ju[0];
function Hi(e) {
  return !e || !e.typeName ? "" : e.namespace ? `${e.namespace}.${e.typeName}` : e.typeName;
}
function ed(e) {
  const t = e.trim();
  if (!t) return { ...Qu };
  const n = Ju.find((r) => Hi(r) === t || r.typeName === t);
  if (n) return { ...n };
  const o = t.lastIndexOf(".");
  return o > 0 ? Pe(t.slice(o + 1), t.slice(0, o)) : Pe(t, "");
}
function td(e) {
  const t = (e ?? "").trim();
  if (!t) return null;
  const n = t.lastIndexOf(".");
  return n > 0 ? Pe(t.slice(n + 1), t.slice(0, n)) : Pe(t, "");
}
function b0() {
  return typeof crypto < "u" && typeof crypto.randomUUID == "function" ? crypto.randomUUID() : `id-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
}
function N0(e, t) {
  const n = new Set(t);
  let o = 1, r = `${e}${o}`;
  for (; n.has(r); )
    o += 1, r = `${e}${o}`;
  return r;
}
function j0(e) {
  return {
    referenceKey: b0(),
    name: e.name,
    typeInformation: e.typeKey ? ed(e.typeKey) : { ...Qu },
    storageDriverType: td(e.storageDriverKey),
    default: null
  };
}
function S0(e, t) {
  return { ...e, ...t };
}
function k0(e) {
  return e === "" ? null : { value: e, expressionType: "Literal" };
}
function E0(e) {
  return e == null || e.value == null ? "" : typeof e.value == "object" ? JSON.stringify(e.value) : String(e.value);
}
function C0(e) {
  return {
    name: e.name,
    type: e.type?.trim() || Gu,
    displayName: e.name,
    description: "",
    category: "",
    isArray: !1,
    uiHint: v0,
    storageDriverType: e.storageDriverType ?? null,
    defaultValue: null,
    defaultSyntax: null,
    isReadOnly: null
  };
}
function I0(e, t) {
  return { ...e, ...t };
}
function A0(e) {
  return {
    name: e.name,
    type: e.type?.trim() || Gu,
    displayName: e.name,
    description: "",
    category: "",
    isArray: !1
  };
}
function _0(e, t) {
  return { ...e, ...t };
}
function D0(e) {
  const t = e.split(",")[0].trim();
  return (t.split(".").pop() ?? t).split("`")[0];
}
function nd(e, t) {
  return e && !e.includes(",") && !e.includes(".") ? e : D0(e || t);
}
function M0(e, t) {
  return nd(e, t).replace(/StorageDriver$/, "");
}
function od(e) {
  return !!e && typeof e == "object" && !Array.isArray(e);
}
function Nt(e, t) {
  for (const n of t) {
    const o = e[n];
    if (o != null && typeof o != "object") return String(o);
  }
  return "";
}
function rd(e, t) {
  for (const n of t)
    if (n in e && e[n] != null) return e[n] === !0 || e[n] === "true";
  return !1;
}
const lc = ["name", "Name"], vr = ["name", "Name"], id = ["type", "Type", "typeName", "TypeName"], sd = ["isArray", "IsArray"], T0 = ["storageDriverType", "StorageDriverType"], $0 = ["defaultValue", "DefaultValue"], br = "workflow", P0 = /* @__PURE__ */ new Set([bo, Nn]);
function z0(e) {
  const t = e?.structure?.kind;
  return !!t && P0.has(t);
}
function ad(e) {
  const t = e?.structure?.payload?.variables;
  return Array.isArray(t) ? t.filter(od) : [];
}
function L0(e, t) {
  return e.structure ? {
    ...e,
    structure: {
      ...e.structure,
      payload: { ...e.structure.payload, variables: t }
    }
  } : e;
}
function R0(e, t) {
  return {
    referenceKey: e,
    declaringScopeId: t && t !== br ? t : br
  };
}
function cd(e) {
  if (e && typeof e == "object") {
    const t = e;
    return typeof t.referenceKey == "string" && t.referenceKey ? { referenceKey: t.referenceKey, declaringScopeId: typeof t.declaringScopeId == "string" ? t.declaringScopeId : null } : null;
  }
  if (typeof e == "string") {
    const t = e.trim();
    if (t.startsWith("{"))
      try {
        return cd(JSON.parse(t));
      } catch {
      }
    return t ? { referenceKey: t, declaringScopeId: null } : null;
  }
  return null;
}
function V0(e) {
  if (!e) return "";
  const t = [`workflow:${uc(e.variables)}`], n = (o) => {
    const r = Ve(o), s = r.flatMap((a) => a.activities.map((c) => c.nodeId));
    t.push(`${o.nodeId}:${uc(ad(o))}>${s.join(",")}`), r.forEach((a) => a.activities.forEach(n));
  };
  return e.rootActivity && n(e.rootActivity), t.join(";");
}
function uc(e) {
  return (e ?? []).map((t) => `${t.referenceKey}=${t.name}`).join(",");
}
function O0(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const o of e ?? [])
    o.scopeId === t && n.set(o.referenceKey, `Shadows "${o.name}" declared in an outer scope.`);
  return n;
}
const Ce = "/_elsa/workflow-management", H0 = "/publishing", Qn = {
  activities: ["workflows", "activities"],
  activityAvailabilitySettings: ["workflows", "activity-availability", "settings"],
  activityAvailabilityDiagnostics: ["workflows", "activity-availability", "diagnostics"]
};
function W0(e) {
  return Hc({
    queryKey: Qn.activityAvailabilitySettings,
    queryFn: () => sv(e)
  });
}
function B0(e) {
  return Hc({
    queryKey: Qn.activityAvailabilityDiagnostics,
    queryFn: () => dd(e)
  });
}
function F0(e) {
  const t = yf();
  return gf({
    mutationFn: (n) => av(e, n),
    onSuccess: () => {
      t.invalidateQueries({ queryKey: Qn.activityAvailabilitySettings }), t.invalidateQueries({ queryKey: Qn.activityAvailabilityDiagnostics }), t.invalidateQueries({ queryKey: Qn.activities });
    }
  });
}
async function K0(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${Ce}/definitions?${n.toString()}`);
}
async function X0(e, t) {
  const n = await e.http.getJson(`${Ce}/definitions/${encodeURIComponent(t)}`);
  return n.draft ? { ...n, draft: { ...n.draft, state: Vr(n.draft.state) } } : n;
}
async function q0(e, t, n) {
  const o = await e.http.postJson(
    `${Ce}/design/scoped-variables/analyze`,
    { state: vo(t), nodeId: n }
  );
  return {
    visibleVariables: Array.isArray(o?.visibleVariables) ? o.visibleVariables : [],
    shadowingWarnings: Array.isArray(o?.shadowingWarnings) ? o.shadowingWarnings : []
  };
}
const bi = (e) => ({ visibleVariables: [], shadowingWarnings: [], status: e });
function U0(e, t, n) {
  const o = de(() => V0(t), [t]), [r, s] = X(() => bi("loading"));
  return ee(() => {
    if (!t) {
      s(bi("unavailable"));
      return;
    }
    let a = !1;
    return s((c) => ({ ...c, status: "loading" })), q0(e, t, n).then(
      (c) => {
        a || s({ ...c, status: "ready" });
      },
      () => {
        a || s(bi("unavailable"));
      }
    ), () => {
      a = !0;
    };
  }, [e, n, o]), r;
}
async function Y0(e, t) {
  const n = await e.http.getJson(`${Ce}/versions/${encodeURIComponent(t)}`);
  return { ...n, state: Vr(n.state) };
}
async function Z0(e, t) {
  return e.http.postJson(`${Ce}/definitions`, t);
}
async function G0(e, t) {
  await e.http.deleteJson(`${Ce}/definitions/${encodeURIComponent(t)}`);
}
async function J0(e, t) {
  await e.http.postJson(`${Ce}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function Q0(e, t) {
  await e.http.deleteJson(`${Ce}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function ev(e, t) {
  const n = await e.http.putJson(
    `${Ce}/drafts/${encodeURIComponent(t.id)}`,
    { state: vo(t.state), layout: t.layout }
  );
  return { ...n, state: Vr(n.state) };
}
async function tv(e, t) {
  return e.http.postJson(`${Ce}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function nv(e, t) {
  return e.http.postJson(`${Ce}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function ov(e, t) {
  const n = { ...t, state: vo(t.state) };
  try {
    return await e.http.postJson(`${H0}/workflows/drafts/test-runs`, n);
  } catch (o) {
    const r = fv(o);
    if (r) return r;
    throw o;
  }
}
async function ld(e, t) {
  return e.http.postJson(`${Ce}/executables/${encodeURIComponent(t)}/run`, {});
}
async function ud(e) {
  return e.http.getJson("/_demo/workflows/executables");
}
async function rv(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.runKind && n.set("runKind", t.runKind), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const o = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${o ? `?${o}` : ""}`);
}
async function iv(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function bs(e) {
  return e.http.getJson(`${Ce}/activities`);
}
async function sv(e) {
  return e.http.getJson(`${Ce}/activities/availability/settings`);
}
async function av(e, t) {
  return e.http.putJson(`${Ce}/activities/availability/settings`, t);
}
async function dd(e) {
  return e.http.getJson(`${Ce}/activities/availability/diagnostics`);
}
async function cv(e) {
  const t = await Or(e, [
    `${Ce}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? dc(t) : dc(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function lv(e) {
  const t = await Or(e, [
    `${Ce}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : er;
}
async function uv(e) {
  const t = await Or(e, [
    `${Ce}/descriptors/variables`,
    "/descriptors/variables"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((o) => fd(o));
}
async function dv(e) {
  const t = await Or(e, [
    `${Ce}/descriptors/storage-drivers`,
    "/descriptors/storage-drivers"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((o) => fd(o));
}
function fd(e) {
  return !!e && typeof e == "object" && typeof e.typeName == "string" && e.typeName.length > 0;
}
async function Or(e, t) {
  let n;
  for (const o of t)
    try {
      return await e.http.getJson(o);
    } catch (r) {
      n = r;
    }
  throw n;
}
function dc(e) {
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
function fv(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = fc(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return fc(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function fc(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const er = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
], hv = [
  "Available",
  "BlockedByHostBaseline",
  "HiddenByManagementSettings",
  "RemovedFromCatalog",
  "UnresolvedReference"
], pv = {
  Available: "Available",
  BlockedByHostBaseline: "Host blocked",
  HiddenByManagementSettings: "Management hidden",
  RemovedFromCatalog: "Removed",
  UnresolvedReference: "Unresolved"
};
function Rt(e) {
  return typeof e == "string" ? e : typeof e == "number" ? hv[e] ?? "Available" : "Available";
}
function Nr(e) {
  const t = Rt(e);
  return pv[t] ?? t;
}
function yv(e) {
  return Rt(e).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function gv(e) {
  return Rt(e) !== "Available";
}
function mv(e) {
  return e === "Only" || e === 1 ? "Only" : "AllExcept";
}
function xv(e) {
  return e === "Only" ? 1 : 0;
}
function hc(e) {
  const t = e?.rules;
  return {
    mode: mv(e?.mode),
    activityTypes: t?.activityTypes ?? [],
    sets: t?.sets ?? []
  };
}
function wv(e) {
  return e.referenceKind === 0 || e.referenceKind === "ActivityType";
}
function vv(e) {
  return [...e?.items ?? []].filter(wv).filter((t) => t.activityTypeKey && t.activityDefinitionId).sort((t, n) => jr(t).localeCompare(jr(n)));
}
function bv(e) {
  return [...e?.items ?? []].filter((t) => {
    const n = Rt(t.state);
    return n === "RemovedFromCatalog" || n === "UnresolvedReference";
  }).sort((t, n) => (t.referenceName ?? "").localeCompare(n.referenceName ?? ""));
}
function pc(e, t) {
  return e.includes(t) ? e.filter((n) => n !== t) : [...e, t].sort((n, o) => n.localeCompare(o));
}
function jr(e) {
  const t = e?.displayName?.trim();
  if (t) return t;
  const n = e?.activityTypeKey?.split(".").filter(Boolean).at(-1) ?? "";
  return Nv(n) || e?.activityTypeKey || "Activity";
}
function Nv(e) {
  return e.replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").replace(/([a-z0-9])([A-Z])/g, "$1 $2").trim();
}
function jv(e, t) {
  const n = new Set(e.filter((o) => !!o));
  return (t?.items ?? []).find((o) => gv(o.state) ? [o.activityDefinitionId, o.activityTypeKey, o.referenceName].some((r) => r && n.has(r)) : !1) ?? null;
}
const Sv = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function hd(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function Ns(e) {
  return hd(e.name);
}
function kv(e, t) {
  const n = Ns(t), o = e[n];
  return t.isWrapped === !1 ? o ?? t.defaultValue ?? "" : yd(o, t);
}
function pd(e, t) {
  return yd(e[Ns(t)], t);
}
function Ev(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function Cv(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function yc(e, t, n) {
  return {
    ...e,
    [Ns(t)]: n
  };
}
function Iv(e, t) {
  return t.isWrapped === !1 ? kv(e, t) : pd(e, t).expression.value;
}
function yd(e, t) {
  return zv(e) ? {
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
const Av = /* @__PURE__ */ new Set([
  "icollection",
  "ireadonlycollection",
  "ilist",
  "ireadonlylist",
  "list",
  "collection",
  "ienumerable",
  "iset",
  "hashset",
  "sortedset",
  "observablecollection",
  "array"
]);
function _v(e) {
  const t = e?.trim();
  if (!t) return null;
  const n = /^([\w.+]+)\[\]/.exec(t);
  if (n) return { elementTypeName: n[1] };
  const o = t.indexOf("`");
  if (o < 0) return null;
  const r = t.slice(0, o), s = (r.split(".").pop() ?? r).toLowerCase();
  return Av.has(s) ? { elementTypeName: Dv(t.slice(o)) } : null;
}
function Dv(e) {
  const t = /\[\[([\w.+]+)/.exec(e);
  if (t) return t[1];
  const n = /\[([\w.+]+)/.exec(e);
  return n ? n[1] : null;
}
function Mv(e) {
  const t = e.uiHint?.toLowerCase();
  return t === "json" || t === "code" ? !0 : e.uiSpecifications?.repeater === !1;
}
function Tv(e) {
  if (Array.isArray(e)) return e;
  if (e == null) return [];
  if (typeof e == "string") {
    const t = e.trim();
    if (!t) return [];
    if (t.startsWith("["))
      try {
        const n = JSON.parse(t);
        if (Array.isArray(n)) return n;
      } catch {
      }
    return [e];
  }
  return [e];
}
function $v(e) {
  const t = (e ?? "").toLowerCase();
  return t === "system.boolean" || t === "boolean" || t === "bool" ? !1 : "";
}
function Pv(e, t) {
  return {
    ...e,
    typeName: t ?? "System.String",
    isWrapped: !1,
    // The description documents the collection as a whole; repeating it on every row is noise.
    description: null
  };
}
function Ni(e, t, n) {
  if (t === n || t < 0 || n < 0 || t >= e.length || n >= e.length) return e;
  const o = [...e], [r] = o.splice(t, 1);
  return o.splice(n, 0, r), o;
}
function zv(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
const gd = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]), Lv = "Variable";
function Rv({
  activity: e,
  descriptor: t,
  editors: n,
  expressionEditors: o,
  expressionDescriptors: r,
  descriptorStatus: s,
  visibleVariables: a,
  scopeStatus: c,
  onChange: u
}) {
  if (s === "loading")
    return /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "Loading activity properties..." });
  if (!t)
    return /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "No activity descriptor is available for this activity." });
  const l = t.inputs.filter((h) => h.isBrowsable !== !1).sort((h, p) => (h.order ?? 0) - (p.order ?? 0) || h.name.localeCompare(p.name));
  if (l.length === 0)
    return /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "This activity does not expose editable properties." });
  const d = Xv(l), f = r.length > 0 ? r : Sv;
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ i.jsx("span", { className: "wf-section-label", children: "Properties" }),
    d.map((h) => /* @__PURE__ */ i.jsxs("section", { className: "wf-property-group", children: [
      d.length > 1 ? /* @__PURE__ */ i.jsx("h4", { children: h.category }) : null,
      h.inputs.map((p) => /* @__PURE__ */ i.jsx(
        Vv,
        {
          activity: e,
          input: p,
          editors: n,
          expressionEditors: o,
          expressionDescriptors: f,
          visibleVariables: a,
          scopeStatus: c,
          onChange: u
        },
        p.name
      ))
    ] }, h.category))
  ] });
}
function Vv({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: o,
  expressionDescriptors: r,
  visibleVariables: s,
  scopeStatus: a,
  onChange: c
}) {
  const u = t.isReadOnly === !0, l = { activity: e, expressionDescriptors: r, readOnly: u }, d = Ki(n, t, l), f = d?.component, h = t.isWrapped !== !1 ? pd(e, t) : null, p = h?.expression.type ?? "Literal", y = Iv(e, t), v = p.toLowerCase() === "literal", x = h && v && !Mv(t) ? _v(t.typeName) : null, m = x ? Ki(n, t, { ...l, scope: "collection" }) : void 0, E = h ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: r,
    readOnly: u,
    surface: "inline",
    syntax: p
  } : null, g = E ? xd(o, E) : null, b = g?.surfaces.inline, j = g && E ? wd(g, E, y) : [], k = !!(h && qv(t, d?.id)), S = !!(h && Uv(t, d?.id)), [_, M] = X(!1), W = (N) => {
    const A = h ? Ev(h, N) : N;
    c(yc(e, t, A));
  }, D = (N) => {
    h && c(yc(e, t, Cv(h, N)));
  }, T = x ? m ? Wi(m.component, t, y, u, { ...l, scope: "collection" }, W) : /* @__PURE__ */ i.jsx(
    Hv,
    {
      input: t,
      elementTypeName: x.elementTypeName,
      value: y,
      editors: n,
      context: l,
      disabled: u,
      onChange: W
    }
  ) : null, R = p === Lv && h ? /* @__PURE__ */ i.jsx(
    Fv,
    {
      value: y,
      visibleVariables: s,
      scopeStatus: a,
      disabled: u,
      onChange: W
    }
  ) : T ?? (b && E ? /* @__PURE__ */ i.jsx(
    b,
    {
      descriptor: t,
      syntax: p,
      value: y,
      disabled: u,
      context: E,
      onChange: W
    }
  ) : Wi(f, t, y, u, l, W));
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ i.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ i.jsx("span", { children: vd(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ i.jsx("p", { children: t.description }) : null,
    h && !k ? /* @__PURE__ */ i.jsx(
      Bi,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: p,
        descriptors: r,
        disabled: u,
        onChange: D
      }
    ) : null,
    k ? /* @__PURE__ */ i.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-expression-editor", children: [
        R,
        Xi(j)
      ] }),
      /* @__PURE__ */ i.jsx(
        Bi,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: p,
          descriptors: r,
          disabled: u,
          variant: "inline",
          onChange: D
        }
      ),
      S ? /* @__PURE__ */ i.jsx(
        "button",
        {
          type: "button",
          className: "wf-expression-expand-button",
          "aria-label": `Open expanded ${t.displayName || t.name} editor`,
          title: "Open expanded editor",
          onClick: () => M(!0),
          children: /* @__PURE__ */ i.jsx(gr, { size: 13 })
        }
      ) : null
    ] }) : /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
      R,
      Xi(j)
    ] }),
    S && !k ? /* @__PURE__ */ i.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => M(!0),
        children: [
          /* @__PURE__ */ i.jsx(gr, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    _ ? /* @__PURE__ */ i.jsx(
      Wv,
      {
        input: t,
        value: y,
        syntax: p,
        descriptors: r,
        activity: e,
        expressionEditors: o,
        disabled: u,
        onChange: W,
        onSyntaxChange: D,
        onClose: () => M(!1)
      }
    ) : null
  ] });
}
function Ov(e, t, n) {
  return [
    "wf-collection-item",
    t === e ? "dragging" : "",
    t !== null && t !== e && n === e ? "drop-target" : ""
  ].filter(Boolean).join(" ");
}
function Hv({
  input: e,
  elementTypeName: t,
  value: n,
  editors: o,
  context: r,
  disabled: s,
  onChange: a
}) {
  const c = Tv(n), u = Pv(e, t), l = { ...r, scope: "element" }, d = Ki(o, u, l)?.component, f = e.displayName || e.name, h = (j, k) => a(c.map((S, _) => _ === j ? k : S)), [p, y] = X(null), [v, x] = X(null), m = () => {
    y(null), x(null);
  }, E = (j) => (k) => {
    y(j), k.dataTransfer.effectAllowed = "move", k.dataTransfer.setData("text/plain", String(j));
  }, g = (j) => (k) => {
    p !== null && (k.preventDefault(), k.dataTransfer.dropEffect = "move", v !== j && x(j));
  }, b = (j) => (k) => {
    k.preventDefault(), p !== null && p !== j && a(Ni(c, p, j)), m();
  };
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-collection-editor", children: [
    c.length === 0 ? /* @__PURE__ */ i.jsx("p", { className: "wf-collection-empty", children: "No items yet." }) : /* @__PURE__ */ i.jsx("ul", { className: "wf-collection-items", children: c.map((j, k) => /* @__PURE__ */ i.jsxs(
      "li",
      {
        className: Ov(k, p, v),
        onDragOver: g(k),
        onDrop: b(k),
        children: [
          /* @__PURE__ */ i.jsx(
            "span",
            {
              className: "wf-collection-item-handle",
              draggable: !s,
              "aria-label": `Drag ${f} item ${k + 1} to reorder`,
              title: "Drag to reorder",
              onDragStart: E(k),
              onDragEnd: m,
              children: /* @__PURE__ */ i.jsx(Ru, { size: 13, "aria-hidden": "true" })
            }
          ),
          /* @__PURE__ */ i.jsx("div", { className: "wf-collection-item-editor", children: Wi(d, u, j, s, l, (S) => h(k, S)) }),
          /* @__PURE__ */ i.jsxs("div", { className: "wf-collection-item-actions", children: [
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${k + 1} up`,
                disabled: s || k === 0,
                onClick: () => a(Ni(c, k, k - 1)),
                children: /* @__PURE__ */ i.jsx(_w, { size: 13 })
              }
            ),
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${k + 1} down`,
                disabled: s || k === c.length - 1,
                onClick: () => a(Ni(c, k, k + 1)),
                children: /* @__PURE__ */ i.jsx(zu, { size: 13 })
              }
            ),
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button danger",
                "aria-label": `Remove ${f} item ${k + 1}`,
                disabled: s,
                onClick: () => a(c.filter((S, _) => _ !== k)),
                children: /* @__PURE__ */ i.jsx(lo, { size: 13 })
              }
            )
          ] })
        ]
      },
      k
    )) }),
    /* @__PURE__ */ i.jsxs(
      "button",
      {
        type: "button",
        className: "wf-collection-add",
        disabled: s,
        onClick: () => a([...c, $v(t)]),
        children: [
          /* @__PURE__ */ i.jsx(wn, { size: 13 }),
          " Add item"
        ]
      }
    )
  ] });
}
function Wv({
  input: e,
  value: t,
  syntax: n,
  descriptors: o,
  activity: r,
  expressionEditors: s,
  disabled: a,
  onChange: c,
  onSyntaxChange: u,
  onClose: l
}) {
  const d = Oc(), f = e.displayName || e.name, h = {
    activity: r,
    descriptor: e,
    expressionDescriptors: o,
    readOnly: a,
    surface: "expanded",
    syntax: n
  }, p = xd(s, h), y = p?.surfaces.expanded, v = p ? wd(p, h, t) : [], x = y ? null : Kv(s, h);
  return ee(() => {
    const m = (E) => {
      E.key === "Escape" && l();
    };
    return window.addEventListener("keydown", m), () => window.removeEventListener("keydown", m);
  }, [l]), /* @__PURE__ */ i.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ i.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": d, children: [
    /* @__PURE__ */ i.jsxs("header", { children: [
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ i.jsx("h3", { id: d, children: f })
      ] }),
      /* @__PURE__ */ i.jsx("button", { type: "button", "aria-label": `Close ${f} editor`, onClick: l, children: /* @__PURE__ */ i.jsx(Hu, { size: 16 }) })
    ] }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ i.jsx(
          Bi,
          {
            label: `${f} expression syntax`,
            value: n,
            descriptors: o,
            disabled: a,
            onChange: u
          }
        ),
        /* @__PURE__ */ i.jsx("span", { children: vd(e.typeName) })
      ] }),
      e.description ? /* @__PURE__ */ i.jsx("p", { children: e.description }) : null,
      y ? /* @__PURE__ */ i.jsx(
        y,
        {
          descriptor: e,
          syntax: n,
          value: t,
          disabled: a,
          context: h,
          onChange: c
        }
      ) : /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
        x ? /* @__PURE__ */ i.jsx("p", { className: "wf-expression-editor-hint", children: x }) : null,
        /* @__PURE__ */ i.jsx(
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
      Xi(v)
    ] }),
    /* @__PURE__ */ i.jsxs("footer", { children: [
      /* @__PURE__ */ i.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: l, children: "Close" })
    ] })
  ] }) });
}
function Wi(e, t, n, o, r, s) {
  return e ? /* @__PURE__ */ i.jsx(
    e,
    {
      descriptor: t,
      value: n,
      disabled: o,
      context: r,
      onChange: s
    }
  ) : /* @__PURE__ */ i.jsx("input", { type: "text", value: n == null ? "" : String(n), disabled: o, onChange: (a) => s(a.target.value) });
}
function Bi({
  label: e,
  value: t,
  descriptors: n,
  disabled: o,
  variant: r = "block",
  onChange: s
}) {
  const [a, c] = X(!1), u = Oc(), l = n.find((f) => f.type === t), d = [
    "wf-syntax-picker-trigger",
    r === "inline" ? "inline" : "",
    a ? "open" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ i.jsxs("div", { className: r === "inline" ? "wf-syntax-picker inline" : "wf-syntax-picker", onBlur: (f) => {
    f.currentTarget.contains(f.relatedTarget) || c(!1);
  }, children: [
    /* @__PURE__ */ i.jsx(
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
        children: /* @__PURE__ */ i.jsx("span", { children: l?.displayName || l?.type || t })
      }
    ),
    a ? /* @__PURE__ */ i.jsx("div", { id: u, role: "listbox", className: "wf-syntax-picker-menu", "aria-label": e, children: n.map((f) => {
      const h = f.displayName || f.type, p = f.type === t;
      return /* @__PURE__ */ i.jsx(
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
const Fi = "::";
function md(e) {
  return !e || e === br ? br : e;
}
function gc(e, t) {
  return `${md(t)}${Fi}${e}`;
}
function Bv(e) {
  const t = e.indexOf(Fi);
  if (t < 0) return null;
  const n = e.slice(t + Fi.length);
  return n ? { scopeId: e.slice(0, t), referenceKey: n } : null;
}
function Fv({ value: e, visibleVariables: t, scopeStatus: n, disabled: o, onChange: r }) {
  const s = cd(e), a = !!e && typeof e == "object" || typeof e == "string" && e.trim().startsWith("{"), c = s && (a || t.some((d) => d.referenceKey === s.referenceKey)) ? s : null, u = c ? gc(c.referenceKey, c.declaringScopeId) : "", l = !!c && t.some(
    (d) => d.referenceKey === c.referenceKey && d.scopeId === md(c.declaringScopeId)
  );
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-variable-picker", children: [
    /* @__PURE__ */ i.jsxs(
      "select",
      {
        "aria-label": "Variable reference",
        value: u,
        disabled: o,
        onChange: (d) => {
          const f = Bv(d.target.value);
          f && r(R0(f.referenceKey, f.scopeId));
        },
        children: [
          /* @__PURE__ */ i.jsx("option", { value: "", children: "Select a variable…" }),
          c && !l ? /* @__PURE__ */ i.jsxs("option", { value: u, children: [
            c.referenceKey,
            " (not visible from this scope)"
          ] }) : null,
          t.map((d) => {
            const f = gc(d.referenceKey, d.scopeId);
            return /* @__PURE__ */ i.jsxs("option", { value: f, children: [
              d.name,
              d.isWorkflowScope ? " · workflow" : " · container"
            ] }, f);
          })
        ]
      }
    ),
    n === "unavailable" ? /* @__PURE__ */ i.jsx("p", { className: "wf-variable-picker-note", children: "Variable scope information is unavailable (pending backend support). Existing references are preserved." }) : n === "ready" && t.length === 0 ? /* @__PURE__ */ i.jsx("p", { className: "wf-variable-picker-note", children: "No variables are visible here. Declare one on the workflow or a container scope." }) : null
  ] });
}
function Ki(e, t, n) {
  return [...e].sort((o, r) => (o.order ?? 500) - (r.order ?? 500)).find((o) => o.supports(t, n));
}
function xd(e, t) {
  return [...e].sort((n, o) => (n.order ?? 500) - (o.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function wd(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function Kv(e, t) {
  if (t.syntax.toLowerCase() === "literal") return null;
  const n = [...e].sort((a, c) => (a.order ?? 500) - (c.order ?? 500)).find((a) => a.supports(t) && a.metadata)?.metadata;
  if (!n) return `No enhanced editor is registered for ${t.syntax}. Using the generic text editor.`;
  const o = n.displayName?.trim() || "enhanced editor", r = n.installHint?.trim(), s = `No ${o} is registered for ${t.syntax}. Using the generic text editor.`;
  return r ? `${s} ${r}` : s;
}
function Xi(e) {
  return e.length === 0 ? null : /* @__PURE__ */ i.jsx("div", { className: "wf-expression-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const o = t.severity ?? "info";
    return /* @__PURE__ */ i.jsxs("p", { className: `wf-expression-editor-diagnostic ${o}`, children: [
      t.code ? /* @__PURE__ */ i.jsx("span", { children: t.code }) : null,
      t.message
    ] }, `${t.code ?? "diagnostic"}-${n}`);
  }) });
}
function Xv(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "General";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return [...t.entries()].map(([n, o]) => ({ category: n, inputs: o }));
}
function vd(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function qv(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !gd.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function Uv(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !gd.has(t) && n !== "multiline") return !1;
  const o = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(o) || n === "singleline" || n === "multiline";
}
function Yv(e, t) {
  return {
    ...t ? { name: t } : {},
    definitionId: e.definitionId,
    state: vo(e.state),
    layout: e.layout
  };
}
function Zv(e) {
  return JSON.stringify(
    {
      state: vo(e.state),
      layout: e.layout
    },
    null,
    2
  );
}
function Gv(e, t) {
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
      state: Vr(o.state),
      layout: o.layout ?? t.layout
    }
  };
}
function Jv(e, t) {
  const n = (t ?? "workflow").trim().replace(/[^\w.-]+/g, "-") || "workflow", o = new Blob([JSON.stringify(e, null, 2)], { type: "application/json" }), r = URL.createObjectURL(o), s = document.createElement("a");
  s.href = r, s.download = `${n}.json`, document.body.appendChild(s), s.click(), s.remove(), URL.revokeObjectURL(r);
}
const Qv = 320, eb = 140;
function tb(e, t, n) {
  return n === "sequence" ? nb(e) : ob(e, t);
}
function nb(e) {
  const t = /* @__PURE__ */ new Map();
  return e.forEach((n, o) => {
    t.set(n.id, { x: o * 280, y: 0 });
  }), t;
}
function ob(e, t) {
  const n = /* @__PURE__ */ new Map();
  if (e.length === 0) return n;
  const o = new Set(e.map((d) => d.id)), r = t.filter((d) => o.has(d.source) && o.has(d.target)), s = /* @__PURE__ */ new Set();
  for (const d of r)
    s.add(d.source), s.add(d.target);
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
  const c = Math.max(0, ...e.filter((d) => s.has(d.id)).map((d) => a.get(d.id) ?? 0)), u = s.size > 0 ? c + 1 : 0, l = /* @__PURE__ */ new Map();
  for (const d of e) {
    const f = s.has(d.id) ? a.get(d.id) ?? 0 : u, h = l.get(f);
    h ? h.push(d.id) : l.set(f, [d.id]);
  }
  for (const [d, f] of l)
    f.forEach((h, p) => {
      n.set(h, { x: d * Qv, y: p * eb });
    });
  return n;
}
const rb = 50;
function mc() {
  return { past: [], future: [] };
}
function ib(e) {
  return e.past.length > 0;
}
function sb(e) {
  return e.future.length > 0;
}
function xc(e, t, n = rb) {
  const o = [...e.past, t];
  return o.length > n && o.splice(0, o.length - n), { past: o, future: [] };
}
function ab(e, t) {
  if (e.past.length === 0) return null;
  const n = e.past.slice(), o = n.pop();
  return { history: { past: n, future: [...e.future, t] }, snapshot: o };
}
function cb(e, t) {
  if (e.future.length === 0) return null;
  const n = e.future.slice(), o = n.pop();
  return { history: { past: [...e.past, t], future: n }, snapshot: o };
}
function rt(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function bd(e, t) {
  if (!e || !t) return "";
  const n = Date.parse(e), o = Date.parse(t);
  if (Number.isNaN(n) || Number.isNaN(o) || o < n) return "";
  const r = Math.round((o - n) / 1e3);
  if (r < 60) return `${r}s`;
  const s = Math.floor(r / 60), a = r % 60;
  if (s < 60) return a ? `${s}m ${a}s` : `${s}m`;
  const c = Math.floor(s / 60), u = s % 60;
  return u ? `${c}h ${u}m` : `${c}h`;
}
function Sr(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function js(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ i.jsx(Lu, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ i.jsx(gs, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ i.jsx(Lw, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ i.jsx(xn, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ i.jsx(Hw, { size: 15 });
    default:
      return /* @__PURE__ */ i.jsx(Lr, { size: 15 });
  }
}
function wc({
  document: e,
  readOnly: t,
  minHeight: n,
  ariaLabel: o,
  onChange: r
}) {
  const s = (a) => {
    t || r({ ...e, value: a.target.value });
  };
  return /* @__PURE__ */ i.jsx(
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
function lb({
  document: e,
  diagnostics: t = [],
  readOnly: n = !1,
  theme: o = "studio",
  minHeight: r = "220px",
  ariaLabel: s,
  languageAdapter: a,
  onChange: c
}) {
  const u = t.filter((h) => !h.uri || h.uri === e.uri), l = a?.displayName ?? e.language, d = a?.loadEditor, f = de(
    () => d ? hf(d) : null,
    [d]
  );
  return /* @__PURE__ */ i.jsxs(
    "section",
    {
      className: "studio-code-editor",
      "data-language": e.language,
      "data-theme": o,
      "data-readonly": n,
      children: [
        /* @__PURE__ */ i.jsxs("div", { className: "studio-code-editor-header", children: [
          /* @__PURE__ */ i.jsx("span", { children: l }),
          /* @__PURE__ */ i.jsx("code", { children: e.uri })
        ] }),
        f ? /* @__PURE__ */ i.jsx(pf, { fallback: /* @__PURE__ */ i.jsx(
          wc,
          {
            document: e,
            readOnly: n,
            minHeight: r,
            ariaLabel: s,
            onChange: c
          }
        ), children: /* @__PURE__ */ i.jsx(
          f,
          {
            document: e,
            readOnly: n,
            theme: o,
            minHeight: r,
            ariaLabel: s,
            onChange: c
          }
        ) }) : /* @__PURE__ */ i.jsx(
          wc,
          {
            document: e,
            readOnly: n,
            minHeight: r,
            ariaLabel: s,
            onChange: c
          }
        ),
        /* @__PURE__ */ i.jsx(ub, { diagnostics: u })
      ]
    }
  );
}
function ub({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ i.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const o = t.severity ?? "info", r = db(t);
    return /* @__PURE__ */ i.jsxs(
      "p",
      {
        className: `studio-code-editor-diagnostic ${o}`,
        children: [
          t.code ? /* @__PURE__ */ i.jsx("span", { children: t.code }) : null,
          r ? /* @__PURE__ */ i.jsx("small", { children: r }) : null,
          t.message
        ]
      },
      `${t.uri ?? "document"}-${t.code ?? "diagnostic"}-${n}`
    );
  }) });
}
function db(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const fb = { language: "json", displayName: "JSON" };
function hb({ draft: e, onApply: t }) {
  const n = de(() => Zv(e), [e]), [o, r] = X(n), [s, a] = X(n), [c, u] = X(null);
  ee(() => {
    r(n), a(n), u(null);
  }, [n]);
  const l = o !== s, d = c ? [{ severity: "error", message: c }] : [], f = () => u(t(o));
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-code-view", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-code-view-toolbar", children: [
      /* @__PURE__ */ i.jsx("span", { className: "wf-muted", children: "Edit the workflow definition as JSON. Changes apply to the draft when you click Apply." }),
      /* @__PURE__ */ i.jsxs("span", { className: "wf-code-view-actions", children: [
        /* @__PURE__ */ i.jsx("button", { type: "button", disabled: !l, onClick: () => {
          r(s), u(null);
        }, children: "Reset" }),
        /* @__PURE__ */ i.jsxs("button", { type: "button", disabled: !l, onClick: f, children: [
          /* @__PURE__ */ i.jsx(bn, { size: 14 }),
          " Apply"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ i.jsx("div", { className: "wf-code-view-editor", children: /* @__PURE__ */ i.jsx(
      lb,
      {
        ariaLabel: "Workflow JSON",
        document: { uri: "elsa://workflows/definition.json", language: "json", value: o },
        languageAdapter: fb,
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
function Nd(e) {
  const [t, n] = X(null), [o, r] = X(null);
  ee(() => {
    let c = !1;
    return uv(e).then(
      (u) => {
        c || n(u);
      },
      () => {
        c || n([]);
      }
    ), dv(e).then(
      (u) => {
        c || r(u);
      },
      () => {
        c || r([]);
      }
    ), () => {
      c = !0;
    };
  }, [e]);
  const s = de(
    () => t && t.length > 0 ? t.map((c) => ({
      value: c.typeName,
      label: nd(c.displayName, c.typeName),
      group: c.category?.trim() || "Other"
    })) : null,
    [t]
  ), a = de(
    () => o && o.length > 0 ? o.filter((c) => !c.deprecated).map((c) => ({
      value: c.typeName,
      label: M0(c.displayName, c.typeName)
    })) : null,
    [o]
  );
  return { typeOptions: s, storageOptions: a };
}
function Ss(e) {
  return !e || e.length === 0 ? void 0 : (e.find((n) => /(^|\.)String$/i.test(n.value) || n.label.toLowerCase() === "string") ?? e[0]).value;
}
function ks(e, t, n) {
  return {
    add: () => {
      const o = N0(n.namePrefix, e.map((r) => Nt(r, n.nameKeys)));
      t([...e, n.create(o)]);
    },
    update: (o, r) => t(e.map((s, a) => a === o ? n.patch(s, r) : s)),
    remove: (o) => t(e.filter((r, s) => s !== o))
  };
}
function fo({ value: e, options: t, placeholder: n, allowEmpty: o, ariaLabel: r, onChange: s }) {
  if (!t)
    return /* @__PURE__ */ i.jsx(
      "input",
      {
        type: "text",
        "aria-label": r,
        value: e,
        placeholder: n,
        onChange: (l) => s(l.target.value)
      }
    );
  const a = e === "" || t.some((l) => l.value === e), c = Array.from(new Set(t.map((l) => l.group).filter((l) => !!l))), u = c.length > 0;
  return /* @__PURE__ */ i.jsxs("select", { "aria-label": r, value: e, onChange: (l) => s(l.target.value), children: [
    o ? /* @__PURE__ */ i.jsx("option", { value: "", children: n ?? "—" }) : null,
    a ? null : /* @__PURE__ */ i.jsx("option", { value: e, children: e }),
    u ? c.map((l) => /* @__PURE__ */ i.jsx("optgroup", { label: l, children: t.filter((d) => d.group === l).map((d) => /* @__PURE__ */ i.jsx("option", { value: d.value, children: d.label }, d.value)) }, l)) : t.map((l) => /* @__PURE__ */ i.jsx("option", { value: l.value, children: l.label }, l.value))
  ] });
}
function Es({ title: e, addLabel: t, emptyLabel: n, headers: o, isEmpty: r, onAdd: s, children: a }) {
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-properties-section-head", children: [
      /* @__PURE__ */ i.jsx("h3", { children: e }),
      /* @__PURE__ */ i.jsxs("button", { type: "button", className: "wf-properties-add", onClick: s, children: [
        /* @__PURE__ */ i.jsx(wn, { size: 14 }),
        " ",
        t
      ] })
    ] }),
    r ? /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: n }) : /* @__PURE__ */ i.jsxs("table", { className: "wf-properties-table", children: [
      /* @__PURE__ */ i.jsx("thead", { children: /* @__PURE__ */ i.jsxs("tr", { children: [
        o.map((c) => /* @__PURE__ */ i.jsx("th", { children: c }, c)),
        /* @__PURE__ */ i.jsx("th", { "aria-label": "Actions" })
      ] }) }),
      /* @__PURE__ */ i.jsx("tbody", { children: a })
    ] })
  ] });
}
function Cs({ label: e, onRemove: t }) {
  return /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx("button", { type: "button", className: "wf-properties-remove", "aria-label": e, title: e, onClick: t, children: /* @__PURE__ */ i.jsx(lo, { size: 14 }) }) });
}
function jd({ items: e, typeOptions: t, storageOptions: n, title: o = "Variables", addLabel: r = "Add variable", emptyLabel: s = "No variables defined.", warnings: a, onChange: c }) {
  const { add: u, update: l, remove: d } = ks(e, c, {
    namePrefix: "Variable",
    nameKeys: lc,
    create: (f) => j0({ name: f, typeKey: Ss(t) }),
    patch: S0
  });
  return /* @__PURE__ */ i.jsx(
    Es,
    {
      title: o,
      addLabel: r,
      emptyLabel: s,
      headers: ["Name", "Type", "Default", "Storage"],
      isEmpty: e.length === 0,
      onAdd: u,
      children: e.map((f, h) => {
        const p = f, y = Nt(f, lc), v = a?.get(p.referenceKey);
        return /* @__PURE__ */ i.jsxs("tr", { children: [
          /* @__PURE__ */ i.jsxs("td", { children: [
            /* @__PURE__ */ i.jsx("input", { type: "text", "aria-label": "Variable name", value: y, onChange: (x) => l(h, { name: x.target.value }) }),
            v ? /* @__PURE__ */ i.jsx("span", { className: "wf-properties-warning", role: "note", title: v, children: v }) : null
          ] }),
          /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            fo,
            {
              ariaLabel: "Variable type",
              value: Hi(p.typeInformation),
              options: t,
              placeholder: "Type",
              onChange: (x) => l(h, { typeInformation: ed(x) })
            }
          ) }),
          /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            "input",
            {
              type: "text",
              "aria-label": "Variable default value",
              value: E0(p.default),
              placeholder: "(empty)",
              onChange: (x) => l(h, { default: k0(x.target.value) })
            }
          ) }),
          /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            fo,
            {
              ariaLabel: "Variable storage driver",
              value: Hi(p.storageDriverType),
              options: n,
              placeholder: "—",
              allowEmpty: !0,
              onChange: (x) => l(h, { storageDriverType: td(x) })
            }
          ) }),
          /* @__PURE__ */ i.jsx(Cs, { label: `Remove variable ${y || h + 1}`, onRemove: () => d(h) })
        ] }, h);
      })
    }
  );
}
function pb({ items: e, typeOptions: t, storageOptions: n, onChange: o }) {
  const { add: r, update: s, remove: a } = ks(e, o, {
    namePrefix: "Input",
    nameKeys: vr,
    create: (c) => C0({ name: c, type: Ss(t) }),
    patch: I0
  });
  return /* @__PURE__ */ i.jsx(
    Es,
    {
      title: "Inputs",
      addLabel: "Add input",
      emptyLabel: "No inputs defined.",
      headers: ["Name", "Type", "Array", "Default", "Storage"],
      isEmpty: e.length === 0,
      onAdd: r,
      children: e.map((c, u) => {
        const l = Nt(c, vr);
        return /* @__PURE__ */ i.jsxs("tr", { children: [
          /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx("input", { type: "text", "aria-label": "Input name", value: l, onChange: (d) => s(u, { name: d.target.value }) }) }),
          /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            fo,
            {
              ariaLabel: "Input type",
              value: Nt(c, id),
              options: t,
              placeholder: "Type",
              onChange: (d) => s(u, { type: d })
            }
          ) }),
          /* @__PURE__ */ i.jsx("td", { className: "wf-properties-cell-center", children: /* @__PURE__ */ i.jsx(
            "input",
            {
              type: "checkbox",
              "aria-label": "Input is array",
              checked: rd(c, sd),
              onChange: (d) => s(u, { isArray: d.target.checked })
            }
          ) }),
          /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            "input",
            {
              type: "text",
              "aria-label": "Input default value",
              value: Nt(c, $0),
              placeholder: "(empty)",
              onChange: (d) => s(u, { defaultValue: d.target.value === "" ? null : d.target.value })
            }
          ) }),
          /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            fo,
            {
              ariaLabel: "Input storage driver",
              value: Nt(c, T0),
              options: n,
              placeholder: "—",
              allowEmpty: !0,
              onChange: (d) => s(u, { storageDriverType: d || null })
            }
          ) }),
          /* @__PURE__ */ i.jsx(Cs, { label: `Remove input ${l || u + 1}`, onRemove: () => a(u) })
        ] }, u);
      })
    }
  );
}
function yb({ items: e, typeOptions: t, onChange: n }) {
  const { add: o, update: r, remove: s } = ks(e, n, {
    namePrefix: "Output",
    nameKeys: vr,
    create: (a) => A0({ name: a, type: Ss(t) }),
    patch: _0
  });
  return /* @__PURE__ */ i.jsx(
    Es,
    {
      title: "Outputs",
      addLabel: "Add output",
      emptyLabel: "No outputs defined.",
      headers: ["Name", "Type", "Array"],
      isEmpty: e.length === 0,
      onAdd: o,
      children: e.map((a, c) => {
        const u = Nt(a, vr);
        return /* @__PURE__ */ i.jsxs("tr", { children: [
          /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx("input", { type: "text", "aria-label": "Output name", value: u, onChange: (l) => r(c, { name: l.target.value }) }) }),
          /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            fo,
            {
              ariaLabel: "Output type",
              value: Nt(a, id),
              options: t,
              placeholder: "Type",
              onChange: (l) => r(c, { type: l })
            }
          ) }),
          /* @__PURE__ */ i.jsx("td", { className: "wf-properties-cell-center", children: /* @__PURE__ */ i.jsx(
            "input",
            {
              type: "checkbox",
              "aria-label": "Output is array",
              checked: rd(a, sd),
              onChange: (l) => r(c, { isArray: l.target.checked })
            }
          ) }),
          /* @__PURE__ */ i.jsx(Cs, { label: `Remove output ${u || c + 1}`, onRemove: () => s(c) })
        ] }, c);
      })
    }
  );
}
function tr(e) {
  return (e ?? []).filter(od);
}
function gb({ context: e, variables: t, title: n, addLabel: o, emptyLabel: r, warnings: s, onChange: a }) {
  const { typeOptions: c, storageOptions: u } = Nd(e);
  return /* @__PURE__ */ i.jsx(
    jd,
    {
      items: tr(t),
      typeOptions: c,
      storageOptions: u,
      title: n,
      addLabel: o,
      emptyLabel: r,
      warnings: s,
      onChange: a
    }
  );
}
function mb({ details: e, draft: t, context: n, onStateChange: o }) {
  const { typeOptions: r, storageOptions: s } = Nd(n), a = tr(t.state.variables), c = tr(t.state.inputs), u = tr(t.state.outputs), l = e?.versions ?? [], d = e?.definition.description?.trim();
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-properties-view", children: [
    /* @__PURE__ */ i.jsxs("section", { className: "wf-properties-section", children: [
      /* @__PURE__ */ i.jsx("h3", { children: "Information" }),
      /* @__PURE__ */ i.jsxs("dl", { className: "wf-properties-info", children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Name" }),
        /* @__PURE__ */ i.jsx("dd", { children: e?.definition.name ?? "—" }),
        /* @__PURE__ */ i.jsx("dt", { children: "Description" }),
        /* @__PURE__ */ i.jsx("dd", { children: d || /* @__PURE__ */ i.jsx("span", { className: "wf-muted", children: "No description" }) }),
        /* @__PURE__ */ i.jsx("dt", { children: "Definition ID" }),
        /* @__PURE__ */ i.jsx("dd", { children: /* @__PURE__ */ i.jsx("code", { children: t.definitionId }) })
      ] })
    ] }),
    /* @__PURE__ */ i.jsx(
      jd,
      {
        items: a,
        typeOptions: r,
        storageOptions: s,
        onChange: (f) => o((h) => ({ ...h, variables: f }))
      }
    ),
    /* @__PURE__ */ i.jsx(
      pb,
      {
        items: c,
        typeOptions: r,
        storageOptions: s,
        onChange: (f) => o((h) => ({ ...h, inputs: f }))
      }
    ),
    /* @__PURE__ */ i.jsx(
      yb,
      {
        items: u,
        typeOptions: r,
        onChange: (f) => o((h) => ({ ...h, outputs: f }))
      }
    ),
    /* @__PURE__ */ i.jsxs("section", { className: "wf-properties-section", children: [
      /* @__PURE__ */ i.jsx("h3", { children: "Versions" }),
      l.length === 0 ? /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "No published versions yet." }) : /* @__PURE__ */ i.jsx("ul", { className: "wf-properties-versions", children: l.map((f) => /* @__PURE__ */ i.jsxs("li", { children: [
        /* @__PURE__ */ i.jsxs("span", { className: "wf-properties-version-tag", children: [
          "v",
          f.version
        ] }),
        /* @__PURE__ */ i.jsx("time", { children: rt(f.createdAt) })
      ] }, f.id)) })
    ] })
  ] });
}
const xb = "Expressions/UnresolvedVariable";
function wb(e) {
  return String(e.type ?? e.code ?? "");
}
function vb(e) {
  return wb(e) === xb;
}
function bb(e) {
  const t = String(e ?? "").trim().split("/").filter(Boolean), [n, o, ...r] = t, s = n === "$workflow";
  return {
    nodeId: !n || s ? null : n,
    isWorkflowScope: s,
    bag: o === "inputs" || o === "outputs" || o === "variables" ? o : null,
    referenceKey: r.length > 0 ? r.join("/") : null
  };
}
function Nb(e) {
  return (e ?? []).filter(vb).map((t) => ({
    error: t,
    path: bb(t.path),
    message: t.message ?? "Variable reference is not visible from this activity's scope."
  }));
}
function jb({ activities: e, activityCatalog: t, selectedEvidenceId: n = null, onSelectEvidence: o }) {
  const r = de(
    () => new Map(t.map((a) => [a.activityTypeKey, a])),
    [t]
  ), s = de(() => kb(e), [e]);
  return s.length === 0 ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty wf-timeline-empty", children: "No activity executions recorded yet." }) : /* @__PURE__ */ i.jsx("ol", { className: "wf-timeline", "aria-label": "Execution timeline", children: s.map((a) => {
    const c = r.get(a.activityType), u = xr(c), l = c ? Ae(c) : Sr(a.activityType) ?? a.activityType, d = Sr(a.activityType) ?? a.activityType, f = Eb(a.startedAt ?? a.scheduledAt), h = bd(a.startedAt, a.completedAt);
    return /* @__PURE__ */ i.jsx("li", { children: /* @__PURE__ */ i.jsxs(
      "button",
      {
        type: "button",
        className: "wf-timeline-entry",
        "data-selected": a.activityExecutionId === n,
        onClick: () => o?.(a.activityExecutionId),
        children: [
          /* @__PURE__ */ i.jsx("span", { className: "wf-timeline-icon wf-activity-icon", "data-icon": u, "aria-hidden": "true", children: js(u) }),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-timeline-body", children: [
            /* @__PURE__ */ i.jsx("strong", { title: l, children: l }),
            /* @__PURE__ */ i.jsx("small", { title: d, children: d })
          ] }),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-timeline-meta", children: [
            f ? /* @__PURE__ */ i.jsx("time", { children: f }) : null,
            h ? /* @__PURE__ */ i.jsxs("small", { children: [
              "took ",
              h
            ] }) : null
          ] }),
          /* @__PURE__ */ i.jsx(Sb, { status: a.status })
        ]
      }
    ) }, a.activityExecutionId);
  }) });
}
function Sb({ status: e }) {
  const t = e.toLowerCase(), n = t === "completed" ? "OK" : e;
  return /* @__PURE__ */ i.jsx("span", { className: "wf-status-badge wf-timeline-pill", "data-status": t, children: n });
}
function kb(e) {
  return e.map((t, n) => ({ activity: t, index: n })).sort((t, n) => vc(t.activity) - vc(n.activity) || t.index - n.index).map((t) => t.activity);
}
function vc(e) {
  const t = e.startedAt ?? e.scheduledAt, n = t ? Date.parse(t) : Number.NaN;
  return Number.isNaN(n) ? Number.MAX_SAFE_INTEGER : n;
}
function Eb(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? "" : t.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: !1 });
}
function Cb({ context: e }) {
  const t = W0(e), n = B0(e), o = F0(e), r = t.data ?? null, s = n.data ?? null, a = t.isLoading || n.isLoading, c = o.isPending, [u, l] = X(() => hc(r)), [d, f] = X(""), [h, p] = X(null);
  ee(() => {
    l(hc(r));
  }, [r]);
  const y = de(() => vv(s), [s]), v = de(() => bv(s), [s]), x = s?.sets ?? [], m = de(() => {
    const T = d.trim().toLowerCase();
    return T ? y.filter(
      (R) => jr(R).toLowerCase().includes(T) || (R.activityTypeKey ?? "").toLowerCase().includes(T)
    ) : y;
  }, [y, d]), E = new Set(u.activityTypes), g = new Set(u.sets), b = y.filter((T) => Rt(T.state) === "BlockedByHostBaseline").length, j = y.filter((T) => Rt(T.state) === "HiddenByManagementSettings").length, k = o.error ?? t.error ?? n.error, S = k instanceof Error ? k.message : k ? "Activity availability could not be loaded." : null, _ = (T) => l((R) => ({ ...R, mode: T })), M = (T) => l((R) => ({ ...R, activityTypes: pc(R.activityTypes, T) })), W = (T) => l((R) => ({ ...R, sets: pc(R.sets, T) })), D = () => {
    p(null), o.mutate(
      {
        scope: r?.scope ?? "host-default",
        mode: xv(u.mode),
        rules: { activityTypes: u.activityTypes, sets: u.sets }
      },
      { onSuccess: () => p("Activity availability saved.") }
    );
  };
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-page availability-page", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-page-header", children: [
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("div", { className: "wf-kicker", children: "Workflows" }),
        /* @__PURE__ */ i.jsxs("h2", { children: [
          /* @__PURE__ */ i.jsx($w, { size: 18 }),
          " Activity availability"
        ] }),
        /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "Control which activities can be added to new workflows. Host baseline rules always take precedence." })
      ] }),
      /* @__PURE__ */ i.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ i.jsxs("button", { type: "button", className: "availability-save", onClick: D, disabled: a || c, children: [
        /* @__PURE__ */ i.jsx(Ou, { size: 15 }),
        c ? "Saving…" : "Save"
      ] }) })
    ] }),
    /* @__PURE__ */ i.jsxs("div", { className: "availability-body", children: [
      S && /* @__PURE__ */ i.jsx("div", { className: "availability-banner availability-banner-error", children: S }),
      h && !S && /* @__PURE__ */ i.jsx("div", { className: "availability-banner availability-banner-success", children: h }),
      /* @__PURE__ */ i.jsxs("div", { className: "availability-mode", role: "group", "aria-label": "Activity availability mode", children: [
        /* @__PURE__ */ i.jsxs("button", { type: "button", className: u.mode === "AllExcept" ? "active" : "", onClick: () => _("AllExcept"), disabled: a || c, children: [
          /* @__PURE__ */ i.jsx(oc, { size: 15 }),
          /* @__PURE__ */ i.jsxs("span", { children: [
            /* @__PURE__ */ i.jsx("strong", { children: "All except" }),
            /* @__PURE__ */ i.jsx("em", { children: "Show everything except the selected activities" })
          ] })
        ] }),
        /* @__PURE__ */ i.jsxs("button", { type: "button", className: u.mode === "Only" ? "active" : "", onClick: () => _("Only"), disabled: a || c, children: [
          /* @__PURE__ */ i.jsx(ic, { size: 15 }),
          /* @__PURE__ */ i.jsxs("span", { children: [
            /* @__PURE__ */ i.jsx("strong", { children: "Only" }),
            /* @__PURE__ */ i.jsx("em", { children: "Show only the selected activities" })
          ] })
        ] })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { className: "availability-counts", children: [
        /* @__PURE__ */ i.jsxs("span", { children: [
          /* @__PURE__ */ i.jsx(ic, { size: 14 }),
          " ",
          b,
          " host blocked"
        ] }),
        /* @__PURE__ */ i.jsxs("span", { children: [
          /* @__PURE__ */ i.jsx(oc, { size: 14 }),
          " ",
          j,
          " management hidden"
        ] }),
        /* @__PURE__ */ i.jsxs("span", { children: [
          /* @__PURE__ */ i.jsx(mr, { size: 14 }),
          " ",
          v.length,
          " unresolved"
        ] })
      ] }),
      x.length > 0 && /* @__PURE__ */ i.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ i.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ i.jsx(xs, { size: 14 }),
          " Sets"
        ] }),
        /* @__PURE__ */ i.jsx("div", { className: "availability-set-list", children: x.map((T) => /* @__PURE__ */ i.jsxs("label", { className: "availability-set-option", children: [
          /* @__PURE__ */ i.jsx("input", { type: "checkbox", checked: g.has(T.name), disabled: a || c, onChange: () => W(T.name) }),
          /* @__PURE__ */ i.jsx("span", { children: T.name }),
          /* @__PURE__ */ i.jsx("code", { children: (T.activityTypeKeys ?? []).length })
        ] }, T.name)) })
      ] }),
      /* @__PURE__ */ i.jsxs("section", { className: "availability-section availability-section-grow", children: [
        /* @__PURE__ */ i.jsxs("div", { className: "availability-section-head", children: [
          /* @__PURE__ */ i.jsxs("h3", { className: "wf-section-label", children: [
            /* @__PURE__ */ i.jsx(Aw, { size: 14 }),
            " Activities"
          ] }),
          /* @__PURE__ */ i.jsxs("div", { className: "wf-search availability-search", children: [
            /* @__PURE__ */ i.jsx(Rr, { size: 14 }),
            /* @__PURE__ */ i.jsx("input", { type: "search", value: d, placeholder: "Filter activities…", onChange: (T) => f(T.target.value) })
          ] })
        ] }),
        /* @__PURE__ */ i.jsxs("div", { className: "availability-activity-list", children: [
          a && y.length === 0 && /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "Loading availability…" }),
          !a && y.length === 0 && /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "No availability diagnostics reported." }),
          !a && y.length > 0 && m.length === 0 && /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "No activities match the filter." }),
          m.map((T) => {
            const N = Rt(T.state) === "BlockedByHostBaseline", A = T.activityTypeKey ?? T.activityDefinitionId ?? "";
            return /* @__PURE__ */ i.jsxs("label", { className: `availability-activity-option ${N ? "disabled" : ""}`, children: [
              /* @__PURE__ */ i.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: E.has(A),
                  disabled: a || c || N,
                  onChange: () => M(A)
                }
              ),
              /* @__PURE__ */ i.jsxs("span", { className: "availability-activity-main", children: [
                /* @__PURE__ */ i.jsx("strong", { children: jr(T) }),
                /* @__PURE__ */ i.jsx("code", { children: T.activityTypeKey })
              ] }),
              /* @__PURE__ */ i.jsx("em", { className: `availability-state ${yv(T.state)}`, children: Nr(T.state) })
            ] }, A);
          })
        ] })
      ] }),
      v.length > 0 && /* @__PURE__ */ i.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ i.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ i.jsx(mr, { size: 14 }),
          " Unresolved references"
        ] }),
        /* @__PURE__ */ i.jsx("div", { className: "availability-unresolved-list", children: v.map((T) => /* @__PURE__ */ i.jsxs("span", { children: [
          /* @__PURE__ */ i.jsx("strong", { children: T.referenceName }),
          /* @__PURE__ */ i.jsx("em", { children: Nr(T.state) })
        ] }, `${T.layer}-${T.referenceKind}-${T.referenceName}`)) })
      ] })
    ] })
  ] });
}
const bc = "elsa-studio:apply-workflow-graph-operation-batch", Nc = "elsa-studio:undo-workflow-graph-operation-batch", Ib = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function Ab(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const o = Lb(e), r = kd(o.state.rootActivity), s = /* @__PURE__ */ new Map(), a = /* @__PURE__ */ new Map(), c = [];
  for (const u of t.operations) {
    const l = zb(u.kind), d = u.parameters ?? {};
    if (l === "add-activity") {
      const f = Le(d.activityId) ?? u.temporaryReferences?.[0], h = Pb(f ?? Le(d.displayName) ?? Le(d.activityType) ?? "weaver-activity", r), p = _b(u, h, n);
      a.set(h, p), c.push(h), f && s.set(f, h), o.state.rootActivity && Db(o.state.rootActivity, p);
      const y = Vt(d.position) ? qi(d.position, { x: 280, y: 160 }) : null;
      y && (o.layout = jc(o.layout, h, y));
      continue;
    }
    if (l === "set-root") {
      const f = ji(o, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown root activity.");
      o.state.rootActivity = f;
      continue;
    }
    if (l === "set-designer-position") {
      const f = Xt(d.activityId, s);
      if (!f || !Is(o.state.rootActivity, f)) throw new Error("Weaver batch referenced an unknown activity position.");
      o.layout = jc(o.layout, f, qi(d, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const f = ji(o, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity property target.");
      $b(f, Le(d.propertyName) ?? "Value", d.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const f = ji(o, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity update target.");
      const h = Vt(d.patch) ? d.patch : d;
      Object.assign(f, h);
      continue;
    }
    if (l === "remove-activity") {
      const f = Xt(d.activityId, s);
      if (!f) throw new Error("Weaver batch referenced an unknown activity remove target.");
      o.state.rootActivity = Sd(o.state.rootActivity, f), o.layout = o.layout.filter((h) => h.nodeId !== f);
      continue;
    }
    if (l === "connect-activities") {
      Mb(o, d, s);
      continue;
    }
    if (l === "disconnect-activities") {
      Tb(o, d, s);
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
function _b(e, t, n) {
  const o = e.parameters ?? {}, r = Le(o.activityVersionId) ?? Le(o.activityType) ?? "Elsa.Workflows.Activity", s = n.find((a) => a.activityVersionId === r || a.activityTypeKey === r || a.displayName === Le(o.displayName));
  return s ? Oi(s, t) : {
    nodeId: t,
    activityVersionId: s?.activityVersionId ?? r,
    inputs: [],
    outputs: [],
    ...Le(o.displayName) ? { displayName: Le(o.displayName) } : {},
    designer: { position: qi(o.position, { x: 280, y: 160 }) }
  };
}
function Db(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = As(e);
  n && !n.some((o) => o.nodeId === t.nodeId) && n.push(t);
}
function Mb(e, t, n) {
  const o = e.state.rootActivity;
  if (!o?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const r = Xt(t.sourceActivityId ?? t.sourceId ?? t.from, n), s = Xt(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!r || !s) throw new Error("Weaver batch connection is missing source or target activity.");
  const a = o.structure.payload, c = Array.isArray(a.connections) ? a.connections : [], u = Le(t.connectionId) ?? `flow-${r}-${s}`;
  a.connections = [
    ...c.filter((l) => !Vt(l) || l.id !== u),
    {
      id: u,
      source: { nodeId: r, port: Le(t.outcome) ?? Le(t.sourcePort) ?? "Done" },
      target: { nodeId: s }
    }
  ];
}
function Tb(e, t, n) {
  const o = e.state.rootActivity, r = o?.structure?.payload.connections;
  if (!Array.isArray(r)) return;
  const s = Le(t.connectionId), a = Xt(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = Xt(t.targetActivityId ?? t.targetId ?? t.to, n);
  o.structure.payload.connections = r.filter((u) => {
    if (!Vt(u)) return !0;
    if (s && u.id === s) return !1;
    const l = Vt(u.source) ? u.source.nodeId : void 0, d = Vt(u.target) ? u.target.nodeId : void 0;
    return l !== a || d !== c;
  });
}
function $b(e, t, n) {
  e[hd(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: "Literal", value: n }
  };
}
function ji(e, t, n, o) {
  const r = Xt(t, n);
  return r ? Is(e.state.rootActivity, r) ?? o.get(r) ?? null : null;
}
function Xt(e, t) {
  const n = Le(e);
  return n ? t.get(n) ?? n : null;
}
function Is(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of Ed(e)) {
    const o = Is(n, t);
    if (o) return o;
  }
  return null;
}
function Sd(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = As(e);
  if (n) {
    const o = n.map((r) => Sd(r, t)).filter((r) => !!r);
    n.splice(0, n.length, ...o);
  }
  return e;
}
function kd(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of Ed(e)) kd(n, t);
  return t;
}
function Ed(e) {
  return As(e) ?? [];
}
function As(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function jc(e, t, n) {
  return [
    ...e.filter((o) => o.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function qi(e, t) {
  const n = Vt(e) ? e : {}, o = Number(n.x), r = Number(n.y);
  return {
    x: Number.isFinite(o) ? Math.max(40, Math.round(o)) : t.x,
    y: Number.isFinite(r) ? Math.max(40, Math.round(r)) : t.y
  };
}
function Pb(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let o = n, r = 2;
  for (; t.has(o); )
    o = `${n}-${r}`, r += 1;
  return t.add(o), o;
}
function zb(e) {
  return typeof e == "number" ? Ib[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Le(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function Lb(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function Vt(e) {
  return typeof e == "object" && e !== null;
}
function _s({ rows: e = 5 }) {
  return /* @__PURE__ */ i.jsx("div", { className: "wf-grid", "aria-busy": "true", "aria-label": "Loading", children: Array.from({ length: e }).map((t, n) => /* @__PURE__ */ i.jsx("div", { className: "wf-skeleton wf-skeleton-row", style: { width: `${90 - n % 3 * 12}%` } }, n)) });
}
function Ds({ icon: e, title: t, description: n, action: o }) {
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-empty-state", role: "status", children: [
    /* @__PURE__ */ i.jsx("div", { className: "wf-empty-state-icon", "aria-hidden": !0, children: e ?? /* @__PURE__ */ i.jsx(Lr, { size: 22 }) }),
    /* @__PURE__ */ i.jsx("h3", { children: t }),
    n ? /* @__PURE__ */ i.jsx("p", { children: n }) : null,
    o ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty-state-action", children: o }) : null
  ] });
}
function No({ message: e, title: t = "Something went wrong" }) {
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-error-card", role: "alert", children: [
    /* @__PURE__ */ i.jsx(Et, { size: 18 }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-error-card-body", children: [
      /* @__PURE__ */ i.jsx("strong", { children: t }),
      /* @__PURE__ */ i.jsx("span", { children: e || "Please try again, or check that the Elsa server is reachable." })
    ] })
  ] });
}
const Cd = { workflowActivity: C1 }, Id = { workflow: A1 }, Sc = "application/x-elsa-activity-version-id", Rb = 6, Vb = 1200, Ob = 250, Hb = [10, 25, 50], Wb = 10, kc = "elsa-studio-workflow-palette-width", Ec = "elsa-studio-workflow-inspector-width", Cc = "elsa-studio-workflow-palette-collapsed", Ic = "elsa-studio-workflow-inspector-collapsed", Ad = "elsa-studio-workflow-side-panel-maximized", Hn = 180, Wn = 460, Bb = 260, Bn = 260, Fn = 560, Fb = 320, Ac = 42, Xo = 16, Kb = [
  {
    value: "flowchart",
    label: "Flowchart",
    hint: "Free-form activities with explicit connections between ports."
  },
  {
    value: "sequence",
    label: "Sequence",
    hint: "Activities run top-to-bottom in the order you place them."
  }
], _d = ct.createContext(null), Dd = ct.createContext(null);
let Ui;
function F1(e) {
  Ui = e.dialogs, e.featureAreas.add({
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
        component: () => /* @__PURE__ */ i.jsx(Xb, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list() })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ i.jsx(qb, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => /* @__PURE__ */ i.jsx(Ub, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => /* @__PURE__ */ i.jsx(Yb, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => /* @__PURE__ */ i.jsx(Cb, { context: e.backend })
      }
    ]
  });
}
function Xb({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: o,
  workflowDesignerPanels: r
}) {
  const [s, a] = X(_c);
  ee(() => {
    const u = () => a(_c());
    return window.addEventListener("popstate", u), () => window.removeEventListener("popstate", u);
  }, []);
  const c = (u) => {
    const l = u ? `/workflows/definitions?definition=${encodeURIComponent(u)}` : "/workflows/definitions";
    window.history.pushState({}, "", l), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return s ? /* @__PURE__ */ i.jsx(E1, { context: e, definitionId: s, ai: t, propertyEditors: n, expressionEditors: o, workflowDesignerPanels: r, onBack: () => c(null) }) : /* @__PURE__ */ i.jsx(Hr, { title: "Definitions", children: /* @__PURE__ */ i.jsx(Gb, { context: e, ai: t, onOpen: c }) });
}
function qb({ context: e, ai: t }) {
  const [n, o] = X(Dc);
  ee(() => {
    const s = () => o(Dc());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const r = ue((s) => {
    const a = s?.trim() ?? "", c = new URL(window.location.href);
    a ? c.searchParams.set("definition", a) : c.searchParams.delete("definition"), o(a || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ i.jsx(Hr, { title: "Executables", children: /* @__PURE__ */ i.jsx(Qb, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: r }) });
}
function Ub({ context: e, ai: t }) {
  return /* @__PURE__ */ i.jsx(Hr, { title: "Runs", children: /* @__PURE__ */ i.jsx(n1, { context: e, ai: t }) });
}
function Yb({ context: e, ai: t }) {
  const n = Zb();
  return /* @__PURE__ */ i.jsx(Hr, { title: "Run", children: /* @__PURE__ */ i.jsx(o1, { context: e, ai: t, workflowExecutionId: n }) });
}
function Hr({ title: e, children: t }) {
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ i.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ i.jsxs("div", { children: [
      /* @__PURE__ */ i.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ i.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function _c() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Dc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Zb() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function Gb({ context: e, ai: t, onOpen: n }) {
  const [o, r] = X(""), [s, a] = X("active"), [c, u] = X(1), [l, d] = X(Wb), [f, h] = X("loading"), [p, y] = X(""), [v, x] = X(""), [m, E] = X([]), [g, b] = X(0), [j, k] = X(() => /* @__PURE__ */ new Set()), [S, _] = X(null), [M, W] = X(!1), [D, T] = X([]), [R, N] = X("idle"), A = ie(null), I = de(() => m.map((H) => H.id), [m]), $ = qt(t, "weaver.workflows.suggest-create-metadata"), z = qt(t, "weaver.workflows.explain-definition"), P = I.filter((H) => j.has(H)).length, B = I.length > 0 && P === I.length, K = ue(async () => {
    h("loading"), y("");
    try {
      const H = await K0(e, { search: o, state: s, page: c, pageSize: l }), Q = typeof H.totalCount == "number", ye = H.totalCount ?? H.definitions.length, ve = $d(ye, l);
      if (ye > 0 && c > ve) {
        u(ve);
        return;
      }
      E(Q ? H.definitions : d1(H.definitions, c, l)), b(ye), h("ready");
    } catch (H) {
      y(H instanceof Error ? H.message : String(H)), h("failed");
    }
  }, [e, o, s, c, l]);
  ee(() => {
    K();
  }, [K]), ee(() => {
    A.current && (A.current.indeterminate = P > 0 && !B);
  }, [B, P]);
  const O = ue(async () => {
    if (!(R === "loading" || R === "ready")) {
      N("loading");
      try {
        const H = await bs(e);
        T(H.activities ?? []), N("ready");
      } catch (H) {
        N("failed"), y(H instanceof Error ? H.message : String(H));
      }
    }
  }, [R, e]), Z = () => {
    y(""), x(""), _({ name: "", description: "", rootKind: "flowchart" }), O();
  }, q = async () => {
    if (S?.name.trim()) {
      W(!0), y(""), x("");
      try {
        const H = await Z0(e, {
          name: S.name.trim(),
          description: S.description.trim() || null,
          rootKind: S.rootKind,
          rootActivityVersionId: f1(S, D)
        });
        _(null), n(H.definition.id);
      } catch (H) {
        y(H instanceof Error ? H.message : String(H));
      } finally {
        W(!1);
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
  }, G = () => k(/* @__PURE__ */ new Set()), V = (H, Q) => {
    k((ye) => {
      const ve = new Set(ye);
      return Q ? ve.add(H) : ve.delete(H), ve;
    });
  }, Y = (H) => {
    k((Q) => {
      const ye = new Set(Q);
      for (const ve of I)
        H ? ye.add(ve) : ye.delete(ve);
      return ye;
    });
  }, ae = (H) => {
    a(H), u(1), G();
  }, ce = (H) => {
    r(H), u(1), G();
  }, J = async (H) => {
    if (await Ui.confirm({ message: `Delete workflow definition "${H.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" })) {
      x(""), y("");
      try {
        await G0(e, H.id), V(H.id, !1), x(`Deleted ${H.name}`), await se();
      } catch (Q) {
        y(Q instanceof Error ? Q.message : String(Q));
      }
    }
  }, te = async (H) => {
    x(""), y("");
    try {
      await J0(e, H.id), V(H.id, !1), x(`Restored ${H.name}`), await se();
    } catch (Q) {
      y(Q instanceof Error ? Q.message : String(Q));
    }
  }, fe = async (H) => {
    if (await Ui.confirm({ message: `Permanently delete workflow definition "${H.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) {
      x(""), y("");
      try {
        await Q0(e, H.id), V(H.id, !1), x(`Permanently deleted ${H.name}`), await se();
      } catch (Q) {
        y(Q instanceof Error ? Q.message : String(Q));
      }
    }
  };
  return /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ i.jsx("button", { type: "button", className: s === "active" ? "active" : "", "aria-selected": s === "active", onClick: () => ae("active"), children: "Active" }),
        /* @__PURE__ */ i.jsx("button", { type: "button", className: s === "deleted" ? "active" : "", "aria-selected": s === "deleted", onClick: () => ae("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ i.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ i.jsx(Rr, { size: 15 }),
        /* @__PURE__ */ i.jsx("input", { value: o, onChange: (H) => ce(H.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => {
        K();
      }, children: "Refresh" }),
      /* @__PURE__ */ i.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ i.jsxs("button", { type: "button", title: "Create workflow", onClick: Z, children: [
        /* @__PURE__ */ i.jsx(wn, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ i.jsx(No, { message: p, title: "Couldn't load workflow definitions" }) : null,
    f !== "failed" && p ? /* @__PURE__ */ i.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ i.jsx(Et, { size: 16 }),
      " ",
      p
    ] }) : null,
    v ? /* @__PURE__ */ i.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ i.jsx(bn, { size: 14 }),
      " ",
      v
    ] }) : null,
    j.size > 0 ? /* @__PURE__ */ i.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ i.jsxs("span", { children: [
        j.size,
        " selected"
      ] }),
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: G, children: "Clear selection" })
    ] }) : null,
    f === "loading" ? /* @__PURE__ */ i.jsx(_s, {}) : null,
    f === "ready" && m.length === 0 ? /* @__PURE__ */ i.jsx(
      Ds,
      {
        icon: /* @__PURE__ */ i.jsx(Vu, { size: 22 }),
        title: `No ${s} workflow definitions`,
        description: "Create a workflow to start designing automation, or adjust your filters to see more.",
        action: /* @__PURE__ */ i.jsxs("button", { type: "button", className: "wf-link-button", onClick: Z, children: [
          /* @__PURE__ */ i.jsx(wn, { size: 15 }),
          " Create workflow"
        ] })
      }
    ) : null,
    f === "ready" && m.length > 0 ? /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ i.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ i.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ i.jsx(
            "input",
            {
              ref: A,
              type: "checkbox",
              checked: B,
              onChange: (H) => Y(H.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ i.jsx("span", { children: "Name" }),
          /* @__PURE__ */ i.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ i.jsx("span", { children: s === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ i.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ i.jsx("span", { children: "Actions" })
        ] }),
        m.map((H) => /* @__PURE__ */ i.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${H.name}`,
            "aria-selected": j.has(H.id),
            tabIndex: 0,
            onClick: () => n(H.id),
            onKeyDown: (Q) => {
              Q.currentTarget === Q.target && (Q.key !== "Enter" && Q.key !== " " || (Q.preventDefault(), n(H.id)));
            },
            children: [
              /* @__PURE__ */ i.jsx("label", { className: "wf-row-select", onClick: (Q) => Q.stopPropagation(), children: /* @__PURE__ */ i.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: j.has(H.id),
                  onChange: (Q) => V(H.id, Q.target.checked),
                  "aria-label": `Select workflow definition ${H.name}`
                }
              ) }),
              /* @__PURE__ */ i.jsxs("span", { children: [
                /* @__PURE__ */ i.jsx("strong", { children: H.name }),
                /* @__PURE__ */ i.jsx("small", { children: H.description || H.id })
              ] }),
              /* @__PURE__ */ i.jsx("span", { children: H.latestVersion ?? "No version" }),
              /* @__PURE__ */ i.jsx("span", { children: s === "deleted" ? rt(H.deletedAt) : H.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ i.jsx("span", { children: rt(H.lastModifiedAt) }),
              /* @__PURE__ */ i.jsx("span", { className: "wf-row-actions", onClick: (Q) => Q.stopPropagation(), children: s === "active" ? /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
                /* @__PURE__ */ i.jsx("button", { type: "button", onClick: (Q) => {
                  Q.stopPropagation(), n(H.id);
                }, children: "Open" }),
                /* @__PURE__ */ i.jsx("button", { type: "button", onClick: (Q) => {
                  Q.stopPropagation(), oe(H.id);
                }, children: "Artifacts" }),
                z ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => kt(t, z, H), children: [
                  /* @__PURE__ */ i.jsx(St, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ i.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  J(H);
                }, children: [
                  /* @__PURE__ */ i.jsx(lo, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
                /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => {
                  te(H);
                }, children: [
                  /* @__PURE__ */ i.jsx(ms, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ i.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  fe(H);
                }, children: [
                  /* @__PURE__ */ i.jsx(lo, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          H.id
        ))
      ] }),
      /* @__PURE__ */ i.jsx(
        u1,
        {
          page: c,
          pageSize: l,
          totalCount: g,
          onPageChange: u,
          onPageSizeChange: (H) => {
            d(H), u(1);
          }
        }
      )
    ] }) : null,
    S ? /* @__PURE__ */ i.jsx(
      Jb,
      {
        draft: S,
        creating: M,
        suggestMetadataAction: $,
        onSuggestMetadata: $ ? () => kt(t, $, { draft: S, activities: D }) : void 0,
        onChange: (H) => _(H),
        onClose: () => _(null),
        onSubmit: q
      }
    ) : null
  ] });
}
function Jb({ draft: e, creating: t, suggestMetadataAction: n, onSuggestMetadata: o, onChange: r, onClose: s, onSubmit: a }) {
  return /* @__PURE__ */ i.jsx("div", { className: "wf-dialog-backdrop", role: "presentation", children: /* @__PURE__ */ i.jsx("section", { className: "wf-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "workflow-create-title", children: /* @__PURE__ */ i.jsxs(
    "form",
    {
      onSubmit: (c) => {
        c.preventDefault(), a();
      },
      children: [
        /* @__PURE__ */ i.jsxs("div", { className: "wf-dialog-heading", children: [
          /* @__PURE__ */ i.jsx("h3", { id: "workflow-create-title", children: "Create Workflow" }),
          n ? /* @__PURE__ */ i.jsxs("button", { type: "button", className: "wf-ai-action", onClick: o, title: n.description ?? n.label, children: [
            /* @__PURE__ */ i.jsx(St, { size: 13 }),
            " ",
            n.label
          ] }) : null
        ] }),
        /* @__PURE__ */ i.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ i.jsx("span", { children: "Display name" }),
          /* @__PURE__ */ i.jsx(
            "input",
            {
              autoFocus: !0,
              "aria-label": "Display name",
              value: e.name,
              onChange: (c) => r({ ...e, name: c.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ i.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ i.jsx("span", { children: "Description" }),
          /* @__PURE__ */ i.jsx(
            "textarea",
            {
              "aria-label": "Description",
              rows: 3,
              value: e.description,
              onChange: (c) => r({ ...e, description: c.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ i.jsxs("fieldset", { className: "wf-form-field wf-root-field", children: [
          /* @__PURE__ */ i.jsx("legend", { children: "Root activity" }),
          /* @__PURE__ */ i.jsx("div", { className: "wf-root-cards", role: "radiogroup", "aria-label": "Root activity", children: Kb.map((c) => {
            const u = e.rootKind === c.value;
            return /* @__PURE__ */ i.jsxs("label", { className: "wf-root-card", "data-checked": u || void 0, children: [
              /* @__PURE__ */ i.jsx(
                "input",
                {
                  type: "radio",
                  name: "wf-root-kind",
                  "aria-label": c.label,
                  value: c.value,
                  checked: u,
                  onChange: () => r({ ...e, rootKind: c.value, rootActivityVersionId: null })
                }
              ),
              /* @__PURE__ */ i.jsx("span", { className: "wf-root-card-title", children: c.label }),
              /* @__PURE__ */ i.jsx("span", { className: "wf-root-card-hint", children: c.hint })
            ] }, c.value);
          }) })
        ] }),
        /* @__PURE__ */ i.jsxs("div", { className: "wf-dialog-actions", children: [
          /* @__PURE__ */ i.jsx("button", { type: "button", onClick: s, disabled: t, children: "Cancel" }),
          /* @__PURE__ */ i.jsx("button", { type: "submit", disabled: t || !e.name.trim(), children: t ? "Creating..." : "Create" })
        ] })
      ]
    }
  ) }) });
}
function Qb({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: o }) {
  const [r, s] = X("loading"), [a, c] = X(""), [u, l] = X(""), [d, f] = X(null), [h, p] = X([]), y = n?.trim().toLowerCase() ?? "", v = de(
    () => y ? h.filter((S) => x1(S, y)) : h,
    [y, h]
  ), x = de(
    () => Array.from(new Set(h.flatMap((S) => [
      S.definitionId,
      S.definitionVersionId,
      S.sourceId
    ]).filter((S) => !!S))).sort((S, _) => S.localeCompare(_)),
    [h]
  ), m = qt(t, "weaver.workflows.explain-executable"), E = ue(async () => {
    s("loading"), c("");
    try {
      p(await ud(e)), s("ready");
    } catch (S) {
      c(S instanceof Error ? S.message : String(S)), s("failed");
    }
  }, [e]);
  ee(() => {
    E();
  }, [E]);
  const g = async (S) => {
    l(""), f(null), c("");
    try {
      const _ = await ld(e, S.artifactId), M = Ld(_);
      f({ artifactId: S.artifactId, workflowExecutionId: M }), l(`Started ${S.artifactId}`);
    } catch (_) {
      c(_ instanceof Error ? _.message : String(_));
    }
  }, b = (S) => {
    m && kt(t, m, S) && (c(""), f(null), l(`Sent ${S.artifactId} to Weaver`));
  }, j = (S) => {
    c(""), f(null), l(`Copied ${S}`);
  }, k = (S) => {
    l(""), f(null), c(`Could not copy ${S}.`);
  };
  return /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => {
        E();
      }, children: "Refresh" }),
      /* @__PURE__ */ i.jsxs("label", { className: "wf-search wf-executable-definition-filter", children: [
        /* @__PURE__ */ i.jsx(Rr, { size: 14 }),
        /* @__PURE__ */ i.jsx(
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
      /* @__PURE__ */ i.jsx("datalist", { id: "wf-executable-definition-options", children: x.map((S) => /* @__PURE__ */ i.jsx("option", { value: S }, S)) }),
      n ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => o(null), children: [
        /* @__PURE__ */ i.jsx(Hu, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    r === "failed" ? /* @__PURE__ */ i.jsx(No, { message: a }) : null,
    u ? /* @__PURE__ */ i.jsx(Md, { status: u, run: d }) : null,
    r === "loading" ? /* @__PURE__ */ i.jsx(_s, {}) : null,
    r === "ready" && v.length === 0 ? /* @__PURE__ */ i.jsx(
      Ds,
      {
        icon: /* @__PURE__ */ i.jsx(xn, { size: 22 }),
        title: "No workflow executables",
        description: n ? "No executables match this definition filter." : "Publish a workflow definition to make it executable."
      }
    ) : null,
    r === "ready" && v.length > 0 ? /* @__PURE__ */ i.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ i.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ i.jsx("span", { children: "Version" }),
        /* @__PURE__ */ i.jsx("span", { children: "Source" }),
        /* @__PURE__ */ i.jsx("span", { children: "Root" }),
        /* @__PURE__ */ i.jsx("span", { children: "Published" }),
        /* @__PURE__ */ i.jsx("span", { children: "Actions" })
      ] }),
      v.map((S) => /* @__PURE__ */ i.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ i.jsxs("span", { className: "wf-artifact-cell", children: [
          /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line", children: [
            /* @__PURE__ */ i.jsx("strong", { title: S.artifactId, children: S.artifactId }),
            /* @__PURE__ */ i.jsx(ln, { value: S.artifactId, ariaLabel: `Copy artifact ID ${S.artifactId}`, copiedLabel: "artifact ID", onCopied: j, onCopyFailed: k })
          ] }),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
            /* @__PURE__ */ i.jsx("small", { title: S.artifactHash, children: S.artifactHash }),
            /* @__PURE__ */ i.jsx(ln, { value: S.artifactHash, ariaLabel: `Copy artifact hash ${S.artifactHash}`, copiedLabel: "artifact hash", onCopied: j, onCopyFailed: k })
          ] })
        ] }),
        /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line wf-version-cell", children: [
          /* @__PURE__ */ i.jsx("span", { children: S.artifactVersion }),
          /* @__PURE__ */ i.jsx(ln, { value: S.artifactVersion, ariaLabel: `Copy artifact version ${S.artifactVersion}`, copiedLabel: "artifact version", onCopied: j, onCopyFailed: k })
        ] }),
        /* @__PURE__ */ i.jsx(e1, { executable: S, onCopied: j, onCopyFailed: k }),
        /* @__PURE__ */ i.jsx("span", { children: Pd(S) }),
        /* @__PURE__ */ i.jsx("span", { children: rt(S.publishedAt ?? S.createdAt) }),
        /* @__PURE__ */ i.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => {
            g(S);
          }, children: [
            /* @__PURE__ */ i.jsx(xn, { size: 13 }),
            " Run"
          ] }),
          m ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => b(S), children: [
            /* @__PURE__ */ i.jsx(St, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, S.artifactId))
    ] }) : null
  ] });
}
function e1({ executable: e, onCopied: t, onCopyFailed: n }) {
  const o = e.sourceId || e.definitionVersionId || e.definitionId, r = e.sourceVersion;
  return /* @__PURE__ */ i.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ i.jsx("span", { className: "wf-source-kind", children: zd(e.sourceKind) }),
    o ? /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line", children: [
      /* @__PURE__ */ i.jsx("code", { title: o, children: o }),
      /* @__PURE__ */ i.jsx(ln, { value: o, ariaLabel: `Copy source ID ${o}`, copiedLabel: "source ID", onCopied: t, onCopyFailed: n })
    ] }) : null,
    r ? /* @__PURE__ */ i.jsxs("small", { children: [
      "Version ",
      r
    ] }) : null
  ] });
}
function Md({ status: e, run: t, compact: n = !1 }) {
  const o = () => {
    t?.workflowExecutionId && (window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(t.workflowExecutionId)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  };
  return /* @__PURE__ */ i.jsxs("div", { className: `wf-status-line${n ? " compact" : ""}`, children: [
    /* @__PURE__ */ i.jsx(bn, { size: n ? 13 : 14 }),
    /* @__PURE__ */ i.jsx("span", { children: e }),
    t?.workflowExecutionId ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: o, children: [
      "Open Run ",
      t.workflowExecutionId
    ] }) : null
  ] });
}
function ln({ value: e, ariaLabel: t, copiedLabel: n, onCopied: o, onCopyFailed: r }) {
  if (!e) return null;
  const s = async (a) => {
    a.preventDefault(), a.stopPropagation();
    try {
      await b1(e), o(n);
    } catch {
      r(n);
    }
  };
  return /* @__PURE__ */ i.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (a) => {
    s(a);
  }, children: /* @__PURE__ */ i.jsx(Mw, { size: 12 }) });
}
function t1({ context: e, ai: t, definitionId: n, publishedArtifactId: o }) {
  const [r, s] = X("loading"), [a, c] = X(""), [u, l] = X(""), [d, f] = X(null), [h, p] = X([]), y = qt(t, "weaver.workflows.explain-executable"), v = ue(async () => {
    s("loading"), c("");
    try {
      const j = await ud(e);
      p(j.filter((k) => w1(k, n)).sort(v1)), s("ready");
    } catch (j) {
      c(j instanceof Error ? j.message : String(j)), p([]), s("failed");
    }
  }, [e, n]);
  ee(() => {
    v();
  }, [v, o]);
  const x = async (j) => {
    l(""), f(null), c("");
    try {
      const k = await ld(e, j.artifactId);
      f({ artifactId: j.artifactId, workflowExecutionId: Ld(k) }), l(`Started ${j.artifactId}`);
    } catch (k) {
      c(k instanceof Error ? k.message : String(k));
    }
  }, m = (j) => {
    y && kt(t, y, j) && (c(""), f(null), l(`Sent ${j.artifactId} to Weaver`));
  }, E = () => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(n)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, g = (j) => {
    c(""), f(null), l(`Copied ${j}`);
  }, b = (j) => {
    l(""), f(null), c(`Could not copy ${j}.`);
  };
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-artifacts-panel", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-artifacts-toolbar", children: [
      /* @__PURE__ */ i.jsxs("span", { children: [
        h.length,
        " artifact",
        h.length === 1 ? "" : "s"
      ] }),
      /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => {
        v();
      }, children: [
        /* @__PURE__ */ i.jsx(ms, { size: 13 }),
        " Refresh"
      ] }),
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: E, children: "Open list" })
    ] }),
    r === "failed" ? /* @__PURE__ */ i.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ i.jsx(Et, { size: 14 }),
      " ",
      a
    ] }) : null,
    u ? /* @__PURE__ */ i.jsx(Md, { status: u, run: d, compact: !0 }) : null,
    r === "loading" ? /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "Loading artifacts..." }) : null,
    r === "ready" && h.length === 0 ? /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "No published artifacts for this workflow yet." }) : null,
    r === "ready" && h.length > 0 ? /* @__PURE__ */ i.jsx("div", { className: "wf-artifact-list", role: "list", "aria-label": "Workflow artifacts", children: h.map((j) => /* @__PURE__ */ i.jsxs("article", { className: "wf-artifact-card", role: "listitem", "data-active": j.artifactId === o ? "true" : void 0, children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-artifact-card-heading", children: [
        /* @__PURE__ */ i.jsxs("div", { children: [
          /* @__PURE__ */ i.jsxs("span", { className: "wf-artifact-version", children: [
            "Version ",
            j.artifactVersion
          ] }),
          j.artifactId === o ? /* @__PURE__ */ i.jsx("span", { className: "wf-chip", children: "Latest publish" }) : null
        ] }),
        /* @__PURE__ */ i.jsx("span", { children: rt(j.publishedAt ?? j.createdAt) })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { className: "wf-artifact-card-values", children: [
        /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line", children: [
          /* @__PURE__ */ i.jsx("code", { title: j.artifactId, children: j.artifactId }),
          /* @__PURE__ */ i.jsx(ln, { value: j.artifactId, ariaLabel: `Copy artifact ID ${j.artifactId}`, copiedLabel: "artifact ID", onCopied: g, onCopyFailed: b })
        ] }),
        /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ i.jsx("code", { title: j.artifactHash, children: j.artifactHash }),
          /* @__PURE__ */ i.jsx(ln, { value: j.artifactHash, ariaLabel: `Copy artifact hash ${j.artifactHash}`, copiedLabel: "artifact hash", onCopied: g, onCopyFailed: b })
        ] })
      ] }),
      /* @__PURE__ */ i.jsxs("dl", { children: [
        /* @__PURE__ */ i.jsxs("div", { children: [
          /* @__PURE__ */ i.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ i.jsxs("dd", { children: [
            zd(j.sourceKind),
            " ",
            j.sourceVersion ? `v${j.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ i.jsxs("div", { children: [
          /* @__PURE__ */ i.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ i.jsx("dd", { children: Pd(j) })
        ] })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => {
          x(j);
        }, children: [
          /* @__PURE__ */ i.jsx(xn, { size: 13 }),
          " Run"
        ] }),
        y ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => m(j), children: [
          /* @__PURE__ */ i.jsx(St, { size: 13 }),
          " Explain"
        ] }) : null
      ] })
    ] }, j.artifactId)) }) : null
  ] });
}
function n1({ context: e }) {
  const [t, n] = X("loading"), [o, r] = X(""), [s, a] = X(""), [c, u] = X(""), [l, d] = X([]), f = ue(async () => {
    n("loading"), r("");
    try {
      const p = await rv(e, {
        status: s || void 0,
        runKind: c || void 0,
        take: 100
      });
      d(p), n("ready");
    } catch (p) {
      r(p instanceof Error ? p.message : String(p)), d([]), n("failed");
    }
  }, [e, c, s]);
  ee(() => {
    f();
  }, [f]);
  const h = (p) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(p)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => {
        f();
      }, children: "Refresh" }),
      /* @__PURE__ */ i.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ i.jsx("span", { children: "Status" }),
        /* @__PURE__ */ i.jsxs("select", { "aria-label": "Workflow run status", value: s, onChange: (p) => a(p.target.value), children: [
          /* @__PURE__ */ i.jsx("option", { value: "", children: "All statuses" }),
          /* @__PURE__ */ i.jsx("option", { value: "Pending", children: "Pending" }),
          /* @__PURE__ */ i.jsx("option", { value: "Running", children: "Running" }),
          /* @__PURE__ */ i.jsx("option", { value: "Suspended", children: "Suspended" }),
          /* @__PURE__ */ i.jsx("option", { value: "Completed", children: "Completed" }),
          /* @__PURE__ */ i.jsx("option", { value: "Faulted", children: "Faulted" }),
          /* @__PURE__ */ i.jsx("option", { value: "Cancelled", children: "Cancelled" })
        ] })
      ] }),
      /* @__PURE__ */ i.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ i.jsx("span", { children: "Kind" }),
        /* @__PURE__ */ i.jsxs("select", { "aria-label": "Run Kind", value: c, onChange: (p) => u(p.target.value), children: [
          /* @__PURE__ */ i.jsx("option", { value: "", children: "All kinds" }),
          /* @__PURE__ */ i.jsx("option", { value: "TestRun", children: "Test Run" }),
          /* @__PURE__ */ i.jsx("option", { value: "PublishedRun", children: "Published Run" }),
          /* @__PURE__ */ i.jsx("option", { value: "BackgroundWeaverRun", children: "Background Weaver Run" }),
          /* @__PURE__ */ i.jsx("option", { value: "Unknown", children: "Unknown / legacy" })
        ] })
      ] })
    ] }),
    t === "failed" ? /* @__PURE__ */ i.jsx(No, { message: o }) : null,
    t === "loading" ? /* @__PURE__ */ i.jsx(_s, {}) : null,
    t === "ready" && l.length === 0 ? /* @__PURE__ */ i.jsx(
      Ds,
      {
        icon: /* @__PURE__ */ i.jsx(Lr, { size: 22 }),
        title: "No workflow runs yet",
        description: "Run a published workflow executable to create execution history here."
      }
    ) : null,
    t === "ready" && l.length > 0 ? /* @__PURE__ */ i.jsxs("div", { className: "wf-grid wf-instance-grid", role: "table", "aria-label": "Workflow runs", children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ i.jsx("span", { children: "Run" }),
        /* @__PURE__ */ i.jsx("span", { children: "Kind" }),
        /* @__PURE__ */ i.jsx("span", { children: "Status" }),
        /* @__PURE__ */ i.jsx("span", { children: "Definition" }),
        /* @__PURE__ */ i.jsx("span", { children: "Activity" }),
        /* @__PURE__ */ i.jsx("span", { children: "Started" }),
        /* @__PURE__ */ i.jsx("span", { children: "Duration" })
      ] }),
      l.map((p) => /* @__PURE__ */ i.jsxs(
        "button",
        {
          type: "button",
          className: "wf-grid-row",
          role: "row",
          "aria-label": `Inspect workflow run ${p.workflowExecutionId}`,
          onClick: () => h(p.workflowExecutionId),
          children: [
            /* @__PURE__ */ i.jsxs("span", { children: [
              /* @__PURE__ */ i.jsx("strong", { children: p.workflowExecutionId }),
              /* @__PURE__ */ i.jsx("small", { children: p.artifactId })
            ] }),
            /* @__PURE__ */ i.jsx("span", { children: Td(p.runKind) }),
            /* @__PURE__ */ i.jsx("span", { children: /* @__PURE__ */ i.jsx(jo, { status: p.status, subStatus: p.subStatus }) }),
            /* @__PURE__ */ i.jsxs("span", { children: [
              /* @__PURE__ */ i.jsx("strong", { children: p.definitionId }),
              /* @__PURE__ */ i.jsx("small", { children: p.definitionVersionId })
            ] }),
            /* @__PURE__ */ i.jsxs("span", { children: [
              /* @__PURE__ */ i.jsxs("strong", { children: [
                p.activityCount,
                " activities"
              ] }),
              /* @__PURE__ */ i.jsxs("small", { children: [
                p.incidentCount,
                " incidents"
              ] })
            ] }),
            /* @__PURE__ */ i.jsx("span", { children: rt(p.startedAt ?? p.createdAt) }),
            /* @__PURE__ */ i.jsx("span", { children: bd(p.startedAt ?? p.createdAt, p.completedAt ?? p.updatedAt) })
          ]
        },
        p.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function o1({ context: e, ai: t, workflowExecutionId: n }) {
  const [o, r] = X("loading"), [s, a] = X(""), [c, u] = X(null), [l, d] = X(null), f = qt(t, "weaver.workflows.explain-instance"), h = ue(async () => {
    if (!n) {
      a("No workflow execution id was provided."), r("failed");
      return;
    }
    r("loading"), a("");
    try {
      const y = await iv(e, n), [v, x] = await Promise.all([
        Y0(e, y.instance.definitionVersionId).then(
          (m) => ({ definitionVersion: m, error: "" }),
          (m) => ({ definitionVersion: null, error: m instanceof Error ? m.message : String(m) })
        ),
        bs(e)
      ]);
      u({
        details: y,
        definitionVersion: v.definitionVersion,
        definitionVersionError: v.error,
        activityCatalog: x.activities
      }), d(null), r("ready");
    } catch (y) {
      u(null), a(O1(y, n)), r("failed");
    }
  }, [e, n]);
  ee(() => {
    h();
  }, [h]);
  const p = () => {
    window.history.pushState({}, "", "/workflows/instances"), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: p, children: [
        /* @__PURE__ */ i.jsx(yr, { size: 14 }),
        " Runs"
      ] }),
      /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => {
        h();
      }, children: [
        /* @__PURE__ */ i.jsx(ms, { size: 14 }),
        " Refresh"
      ] }),
      c && f ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => kt(t, f, c.details), children: [
        /* @__PURE__ */ i.jsx(St, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    o === "loading" ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "Loading workflow run..." }) : null,
    o === "failed" ? /* @__PURE__ */ i.jsx(No, { message: s }) : null,
    o === "ready" && c ? /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-detail-workbench", children: [
      /* @__PURE__ */ i.jsx(
        r1,
        {
          definitionVersion: c.definitionVersion,
          definitionVersionError: c.definitionVersionError,
          activityCatalog: c.activityCatalog,
          details: c.details,
          selectedEvidenceId: l,
          onSelectEvidence: d
        }
      ),
      /* @__PURE__ */ i.jsx(
        i1,
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
          graphNodeIds: c.definitionVersion ? l1(c.definitionVersion, c.activityCatalog) : void 0
        }
      )
    ] }) : null
  ] });
}
function r1({ definitionVersion: e, definitionVersionError: t, activityCatalog: n, details: o, selectedEvidenceId: r, onSelectEvidence: s }) {
  const a = de(() => {
    if (!e) return { nodes: [], edges: [] };
    const c = e.state.rootActivity;
    if (!c) return { nodes: [], edges: [] };
    const u = n.find((p) => p.activityVersionId === c.activityVersionId), l = ws(c, u), d = l === "unsupported" ? null : Jn(c, []), f = l === "unsupported" ? Ri(c, n, e.layout) : d ? Ku(d, n, e.layout) : Ri(c, n, e.layout), h = f.nodes.map((p) => ({
      ...p,
      draggable: !1,
      connectable: !1,
      deletable: !1
    }));
    return {
      nodes: Jw(h, o.activities, o.incidents, r),
      edges: f.edges.map((p) => ({ ...p, deletable: !1 }))
    };
  }, [n, e, o, r]);
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-instance-canvas-shell", "aria-label": "Workflow run canvas", children: [
    /* @__PURE__ */ i.jsxs("header", { children: [
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("span", { children: "Definition version" }),
        /* @__PURE__ */ i.jsx("h3", { children: e ? /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
          e.definition.name,
          " ",
          /* @__PURE__ */ i.jsx("small", { children: e.version })
        ] }) : /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
          "Definition graph unavailable ",
          /* @__PURE__ */ i.jsx("small", { children: o.instance.definitionVersionId })
        ] }) })
      ] }),
      /* @__PURE__ */ i.jsx(jo, { status: o.instance.status, subStatus: o.instance.subStatus })
    ] }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ i.jsxs("div", { className: "wf-empty", children: [
        "The workflow run loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ i.jsx("small", { children: V1(t) }) : null
      ] }),
      e && a.nodes.length === 0 ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      a.nodes.length > 0 ? /* @__PURE__ */ i.jsxs(
        Iu,
        {
          nodes: a.nodes,
          edges: a.edges,
          nodeTypes: Cd,
          edgeTypes: Id,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (c, u) => s(u.id),
          onPaneClick: () => s(null),
          children: [
            /* @__PURE__ */ i.jsx(_u, {}),
            /* @__PURE__ */ i.jsx($u, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ i.jsx(Mu, {})
          ]
        }
      ) : null
    ] })
  ] });
}
function i1({ ai: e, action: t, summary: n, details: o, state: r, error: s, selectedEvidenceId: a = null, onSelectEvidence: c, graphNodeIds: u, activityCatalog: l = [] }) {
  const [d, f] = X("timeline");
  if (!n)
    return /* @__PURE__ */ i.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "Select a workflow run to inspect its timeline." }) });
  const h = o?.incidents.length ?? 0, p = [
    { id: "timeline", title: "Timeline", order: 0, icon: /* @__PURE__ */ i.jsx(gs, { size: 14 }), render: () => null },
    { id: "issues", title: h > 0 ? `Issues (${h})` : "Issues", order: 1, icon: /* @__PURE__ */ i.jsx(Et, { size: 14 }), render: () => null },
    { id: "details", title: "Details", order: 2, icon: /* @__PURE__ */ i.jsx(xs, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ i.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Workflow run details", children: [
    /* @__PURE__ */ i.jsxs("header", { children: [
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("span", { children: "Workflow Instance ID" }),
        /* @__PURE__ */ i.jsx("h3", { children: n.workflowExecutionId })
      ] }),
      t ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => kt(e, t, o ?? n), children: [
        /* @__PURE__ */ i.jsx(St, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    /* @__PURE__ */ i.jsx("div", { className: "wf-instance-tabs", children: /* @__PURE__ */ i.jsx(or, { label: "Workflow run tabs", tabs: p, activeTabId: d, onSelect: (y) => f(y) }) }),
    r === "loading" ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "Loading run details..." }) : null,
    r === "failed" ? /* @__PURE__ */ i.jsx(No, { message: s }) : null,
    r === "ready" && o ? /* @__PURE__ */ i.jsx("div", { className: "wf-instance-tab-content", children: d === "timeline" ? /* @__PURE__ */ i.jsx(
      jb,
      {
        activities: o.activities,
        activityCatalog: l,
        selectedEvidenceId: a,
        onSelectEvidence: c
      }
    ) : d === "issues" ? /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
      /* @__PURE__ */ i.jsx(s1, { incidents: o.incidents, selectedEvidenceId: a, onSelectEvidence: c }),
      /* @__PURE__ */ i.jsx(a1, { details: o, graphNodeIds: u })
    ] }) : /* @__PURE__ */ i.jsxs("dl", { className: "wf-instance-meta", children: [
      /* @__PURE__ */ i.jsx("dt", { children: "Status" }),
      /* @__PURE__ */ i.jsx("dd", { children: /* @__PURE__ */ i.jsx(jo, { status: n.status, subStatus: n.subStatus }) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Run Kind" }),
      /* @__PURE__ */ i.jsx("dd", { children: Td(n.runKind) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Artifact" }),
      /* @__PURE__ */ i.jsxs("dd", { children: [
        n.artifactId,
        " ",
        /* @__PURE__ */ i.jsx("small", { children: n.artifactVersion })
      ] }),
      /* @__PURE__ */ i.jsx("dt", { children: "Definition" }),
      /* @__PURE__ */ i.jsxs("dd", { children: [
        n.definitionId,
        " ",
        /* @__PURE__ */ i.jsx("small", { children: n.definitionVersionId })
      ] }),
      /* @__PURE__ */ i.jsx("dt", { children: "Created" }),
      /* @__PURE__ */ i.jsx("dd", { children: rt(n.createdAt) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Started" }),
      /* @__PURE__ */ i.jsx("dd", { children: rt(n.startedAt) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Completed" }),
      /* @__PURE__ */ i.jsx("dd", { children: rt(n.completedAt) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Correlation" }),
      /* @__PURE__ */ i.jsx("dd", { children: n.correlationId || "None" })
    ] }) }) : null
  ] });
}
function s1({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ i.jsx("h4", { children: "Incidents" }),
    e.length === 0 ? /* @__PURE__ */ i.jsx("p", { children: "No incidents recorded." }) : null,
    e.map((o) => /* @__PURE__ */ i.jsxs(
      "button",
      {
        type: "button",
        className: "wf-instance-incident",
        "data-severity": o.severity.toLowerCase(),
        "data-selected": o.incidentId === t,
        onClick: () => n?.(o.incidentId),
        children: [
          /* @__PURE__ */ i.jsx("strong", { children: o.failureType }),
          /* @__PURE__ */ i.jsxs("span", { children: [
            o.status,
            " · ",
            o.severity
          ] }),
          /* @__PURE__ */ i.jsx("p", { children: o.message })
        ]
      },
      o.incidentId
    ))
  ] });
}
function a1({ details: e, graphNodeIds: t }) {
  if (!t) return null;
  const n = new Map(e.activities.map((s) => [s.activityExecutionId, s])), o = e.activities.filter((s) => !t.has(Mc(s))), r = e.incidents.filter((s) => {
    const a = s.activityExecutionId ? n.get(s.activityExecutionId) : null, c = s.executableNodeId ?? (a ? Mc(a) : "");
    return !c || !t.has(c);
  });
  return o.length === 0 && r.length === 0 ? null : /* @__PURE__ */ i.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ i.jsx("h4", { children: "Unmatched runtime evidence" }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-unmatched-list", children: [
      o.map((s) => /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ i.jsx("strong", { children: Sr(s.activityType) ?? s.activityType }),
        /* @__PURE__ */ i.jsx("small", { children: s.activityExecutionId })
      ] }, `activity-${s.activityExecutionId}`)),
      r.map((s) => /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ i.jsx("strong", { children: s.failureType }),
        /* @__PURE__ */ i.jsx("small", { children: s.incidentId })
      ] }, `incident-${s.incidentId}`))
    ] })
  ] });
}
function jo({ status: e, subStatus: t }) {
  return /* @__PURE__ */ i.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
function Td(e) {
  switch (c1(e)) {
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
function c1(e) {
  return (e ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}
function l1(e, t) {
  const n = e.state.rootActivity;
  if (!n) return /* @__PURE__ */ new Set();
  const o = t.find((s) => s.activityVersionId === n.activityVersionId);
  if (ws(n, o) === "unsupported") return /* @__PURE__ */ new Set([n.nodeId]);
  const r = Jn(n, []);
  return new Set(r?.slot.activities.map((s) => s.nodeId) ?? [n.nodeId]);
}
function Mc(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function u1({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const s = $d(n, t), a = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-pagination", "aria-label": "Workflow definition pagination", children: [
    /* @__PURE__ */ i.jsxs("span", { className: "wf-pagination-summary", "aria-live": "polite", children: [
      "Showing ",
      a,
      "-",
      c,
      " of ",
      n
    ] }),
    /* @__PURE__ */ i.jsxs("label", { className: "wf-page-size", children: [
      "Rows",
      /* @__PURE__ */ i.jsx("select", { value: t, onChange: (u) => r(Number(u.target.value)), children: Hb.map((u) => /* @__PURE__ */ i.jsx("option", { value: u, children: u }, u)) })
    ] }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ i.jsx(yr, { size: 14 }),
        " Previous"
      ] }),
      /* @__PURE__ */ i.jsxs("span", { children: [
        "Page ",
        e,
        " of ",
        s
      ] }),
      /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => o(e + 1), disabled: e >= s, "aria-label": "Next page", title: "Next page", children: [
        "Next ",
        /* @__PURE__ */ i.jsx(on, { size: 14 })
      ] })
    ] })
  ] });
}
function d1(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function $d(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function qt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function kt(e, t, n) {
  const o = t.createPrompt(n);
  return o ? (e.dispatchPrompt(o), !0) : !1;
}
function f1(e, t) {
  return e.rootActivityVersionId ?? h1(t, e.rootKind)?.activityVersionId ?? null;
}
function h1(e, t) {
  return e.find((n) => p1(n) === t);
}
function p1(e) {
  return e ? y1(e) ? "flowchart" : g1(e) ? "sequence" : null : null;
}
function Yi(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "Uncategorized";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [o]) => n.localeCompare(o)).map(([n, o]) => ({
    category: n,
    activities: o.sort((r, s) => Ae(r).localeCompare(Ae(s)))
  }));
}
function y1(e) {
  return Ae(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function g1(e) {
  return Ae(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function m1(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function Pd(e) {
  return N1(e.rootActivityType) || e.rootActivityType;
}
function x1(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function w1(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function v1(e, t) {
  return Tc(t) - Tc(e);
}
function Tc(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function zd(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
function Ld(e) {
  const t = e?.workflowExecutionId ?? e?.runId ?? e?.executionId;
  return typeof t == "string" && t.trim() ? t : null;
}
async function b1(e) {
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
function N1(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function j1(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    qo(t, n.typeName, n), qo(t, n.name, n), qo(t, n.displayName, n);
    const o = n.typeName.split(".").filter(Boolean).at(-1);
    qo(t, o, n);
  }
  return t;
}
function S1(e, t, n) {
  const o = t.get(e.activityVersionId);
  return n.get(Yn(o?.activityTypeKey)) ?? n.get(Yn(Sr(o?.activityTypeKey))) ?? n.get(Yn(o?.displayName)) ?? n.get(Yn(e.activityVersionId)) ?? null;
}
function qo(e, t, n) {
  const o = Yn(t);
  o && !e.has(o) && e.set(o, n);
}
function Yn(e) {
  return e?.trim().toLowerCase() ?? "";
}
function $c(e, t, n, o) {
  const r = Wr();
  if (!r) return t;
  const s = r.getItem(e);
  if (s == null) return t;
  const a = Number(s);
  return Number.isFinite(a) ? nr(a, n, o) : t;
}
function Pc(e, t) {
  const n = Wr();
  if (!n) return t;
  const o = n.getItem(e);
  return o === "true" ? !0 : o === "false" ? !1 : t;
}
function k1() {
  const e = Wr();
  if (!e) return null;
  const t = e.getItem(Ad);
  return t === "palette" || t === "inspector" ? t : null;
}
function Wr() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function Kn(e, t) {
  const n = Wr();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function nr(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function E1({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: o,
  expressionEditors: r,
  workflowDesignerPanels: s,
  onBack: a
}) {
  const [c, u] = X(null), [l, d] = X(null), [f, h] = X([]), [p, y] = X([]), [v, x] = X(null), [m, E] = X(er), [g, b] = X("loading"), [j, k] = X([]), [S, _] = X([]), [M, W] = X([]), [D, T] = X(null), [R, N] = X(null), [A, I] = X(null), [$, z] = X(null), [P, B] = X(""), [K, O] = X(""), [Z, q] = X("idle"), [oe, se] = X(null), [G, V] = X(!1), [Y, ae] = X(null), [ce, J] = X(() => /* @__PURE__ */ new Set()), [te, fe] = X(""), [H, Q] = X(() => $c(kc, Bb, Hn, Wn)), [ye, ve] = X(() => $c(Ec, Fb, Bn, Fn)), [ke, Te] = X(() => Pc(Cc, !1)), [Ie, Oe] = X(() => Pc(Ic, !1)), [be, He] = X(k1), [We, Ge] = X("activities"), [Je, Qe] = X("inspector"), [Ct, Ut] = X("designer"), Ze = ie(null), Yt = ie(null), jn = ie(""), Sn = ie(0), So = ie(Promise.resolve()), kn = ie(/* @__PURE__ */ new Map()), Be = ie(mc()), ut = ie(null), dt = ie(""), Zt = ie(!1), [Br, En] = X(0), Gt = ie(null), mt = ie(null), It = ie(!1), At = l?.state.rootActivity ?? null, ze = de(() => new Map(f.map((w) => [w.activityVersionId, w])), [f]), Cn = ue(
    (w) => jv([w.activityVersionId, w.activityTypeKey], v),
    [v]
  ), ko = de(() => j1(p), [p]), Fe = de(() => Fu(At, j), [At, j]), Eo = ws(Fe, Fe ? ze.get(Fe.activityVersionId) : void 0), we = !!Fe && Eo === "unsupported", Ke = de(() => we ? null : Jn(At, j), [At, j, we]), Jt = de(() => Yi(f), [f]), Co = de(() => {
    const w = te.trim().toLowerCase();
    if (!w) return Jt;
    const C = f.filter((L) => Ae(L).toLowerCase().includes(w) || L.activityTypeKey.toLowerCase().includes(w) || (L.category ?? "").toLowerCase().includes(w) || (L.description ?? "").toLowerCase().includes(w));
    return Yi(C);
  }, [f, te, Jt]), ge = de(() => we && Fe?.nodeId === R ? Fe : Ke?.slot.activities.find((w) => w.nodeId === R) ?? null, [we, Ke, Fe, R]), _t = de(
    () => ge ? S1(ge, ze, ko) : null,
    [ze, ko, ge]
  ), Io = de(
    () => ge ? Cn({ activityVersionId: ge.activityVersionId, activityTypeKey: ze.get(ge.activityVersionId)?.activityTypeKey }) : null,
    [Cn, ze, ge]
  ), In = ge ? Ve(ge) : [], An = U0(e, l?.state, R), _e = Eo === "flowchart" && Ke?.slot.mode === "flowchart", xt = !At || !we, ft = Z !== "idle", Fr = !!l?.state.rootActivity && !ft, Ao = qt(n, "weaver.workflows.find-draft-risks"), _o = qt(n, "weaver.workflows.propose-update");
  ee(() => {
    if (!(!c || !l))
      return window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
        workflowId: c.definition.id,
        workflowDefinitionId: c.definition.id,
        workflowVersionId: l.sourceVersionId ?? null,
        draftId: l.id,
        revision: L1(l),
        selectedNodeId: R,
        selectedActivityType: _t?.typeName ?? (ge ? ze.get(ge.activityVersionId)?.activityTypeKey ?? ge.activityVersionId : null),
        summary: c.definition.name,
        activities: Vd(l.state.rootActivity, ze),
        connections: Od(l.state.rootActivity),
        diagnostics: l.validationErrors.map((w) => ({ severity: w.code ?? "warning", message: w.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === c.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [ze, c, l, _t, ge, R]), ee(() => {
    const w = (L) => {
      const F = L.detail;
      if (!F?.batch || !F.respond) return;
      if (!l || !c) {
        F.respond({ ok: !1, message: "No active workflow draft is open." });
        return;
      }
      const U = F.batch.workflowDefinitionId;
      if (U && U !== "active-draft" && U !== c.definition.id) {
        F.respond({ ok: !1, message: `Batch targets workflow '${U}', but '${c.definition.id}' is active.` });
        return;
      }
      try {
        const le = Xn(l), re = Ab(l, F.batch, f), me = `weaver-batch-${Date.now()}`;
        kn.current.set(me, le), d(re.draft), k([]), N(re.finalActivityIds.at(-1) ?? null), ae(null), se(null), O(re.summary), B(""), F.respond({ ok: !0, result: { ...re, undoToken: me } });
      } catch (le) {
        const re = le instanceof Error ? le.message : String(le);
        B(re), F.respond({ ok: !1, message: re });
      }
    }, C = (L) => {
      const F = L.detail;
      if (!F?.undoToken || !F.respond) return;
      const U = kn.current.get(F.undoToken);
      if (!U) {
        F.respond({ ok: !1, message: "The Weaver batch undo point is no longer available." });
        return;
      }
      kn.current.delete(F.undoToken), d(U), k([]), N(null), ae(null), se(null), O("Restored workflow draft before Weaver batch."), B(""), F.respond({ ok: !0, summary: "Restored workflow draft before Weaver batch." });
    };
    return window.addEventListener(bc, w), window.addEventListener(Nc, C), () => {
      window.removeEventListener(bc, w), window.removeEventListener(Nc, C);
    };
  }, [f, c, l]), ee(() => {
    Kn(kc, String(H));
  }, [H]), ee(() => {
    Kn(Ec, String(ye));
  }, [ye]), ee(() => {
    Kn(Cc, String(ke));
  }, [ke]), ee(() => {
    Kn(Ic, String(Ie));
  }, [Ie]), ee(() => {
    Kn(Ad, be);
  }, [be]), ee(() => {
    if (!be) return;
    const w = (C) => {
      C.key === "Escape" && He(null);
    };
    return window.addEventListener("keydown", w), () => window.removeEventListener("keydown", w);
  }, [be]);
  const _n = ue(async () => {
    B(""), b("loading");
    const [w, C, L, F, U] = await Promise.all([
      X0(e, t),
      bs(e),
      cv(e).then(
        (re) => ({ ok: !0, descriptors: re }),
        () => ({ ok: !1, descriptors: [] })
      ),
      lv(e).then(
        (re) => ({ ok: !0, descriptors: re }),
        () => ({ ok: !1, descriptors: er })
      ),
      // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
      dd(e).then(
        (re) => re,
        () => null
      )
    ]), le = w.draft ?? null;
    u(w), jn.current = le ? Xe(le) : "", Be.current = mc(), ut.current = le ? Xn(le) : null, dt.current = le ? Xe(le) : "", Zt.current = !1, En(0), d(le), h(C.activities ?? []), y(L.descriptors), x(U), E(F.descriptors.length > 0 ? F.descriptors : er), b(L.ok ? "ready" : "failed"), k([]), N(null);
  }, [e, t]);
  ee(() => {
    _n().catch((w) => B(w instanceof Error ? w.message : String(w)));
  }, [_n]), ee(() => {
    J((w) => {
      let C = !1;
      const L = new Set(w);
      for (const F of Jt)
        L.has(F.category) || (L.add(F.category), C = !0);
      return C ? L : w;
    });
  }, [Jt]), ee(() => {
    if (!Fe) {
      _([]), W([]);
      return;
    }
    const w = we ? Ri(Fe, f, l?.layout ?? []) : Ke ? Ku(Ke, f, l?.layout ?? []) : { nodes: [], edges: [] };
    _(w.nodes), W(w.edges);
  }, [f, l?.layout, we, Ke, Fe]);
  const Kr = (w) => {
    d((C) => C && { ...C, state: { ...C.state, rootActivity: w } });
  }, Dn = ue((w, C) => {
    if (l?.state.rootActivity && we)
      return;
    const L = Oi(w, Rc(w));
    if (!l?.state.rootActivity) {
      Kr(L), N(L.nodeId);
      return;
    }
    if (!Ke) {
      if (!Ve(L)[0]) {
        O(""), B("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      d((U) => {
        if (!U?.state.rootActivity) return U;
        const le = U.state.rootActivity, re = Vi(L, [], [le]), me = C ? [
          ...U.layout.filter((xe) => xe.nodeId !== le.nodeId),
          {
            nodeId: le.nodeId,
            x: Math.round(C.x),
            y: Math.round(C.y)
          }
        ] : U.layout;
        return {
          ...U,
          layout: me,
          state: {
            ...U.state,
            rootActivity: re
          }
        };
      }), N(l.state.rootActivity.nodeId), B(""), O(`Wrapped root in ${Ae(w)}`);
      return;
    }
    d((F) => {
      if (!F?.state.rootActivity) return F;
      const U = Jn(F.state.rootActivity, j);
      if (!U) return F;
      const le = Vi(F.state.rootActivity, j, [...U.slot.activities, L]), re = C ? [
        ...F.layout.filter((me) => me.nodeId !== L.nodeId),
        {
          nodeId: L.nodeId,
          x: Math.round(C.x),
          y: Math.round(C.y)
        }
      ] : F.layout;
      return {
        ...F,
        layout: re,
        state: {
          ...F.state,
          rootActivity: le
        }
      };
    }), N(L.nodeId);
  }, [l?.state.rootActivity, j, we, Ke]), Dt = ue((w, C) => {
    const L = Oi(w, Rc(w)), F = {
      id: L.nodeId,
      type: "workflowActivity",
      position: C,
      selected: !0,
      data: {
        label: Ae(w),
        activityVersionId: w.activityVersionId,
        activityTypeKey: w.activityTypeKey,
        category: w.category,
        executionType: w.executionType,
        icon: xr(w),
        childSlots: Ve(L),
        acceptsInbound: String(w.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: Zu(L, w)
      }
    };
    return { activityNode: L, node: F };
  }, []), $e = ue((w, C, L = []) => {
    we || d((F) => {
      if (!F) return F;
      const U = t0(F.layout, w), le = F.state.rootActivity;
      if (!le) return { ...F, layout: U };
      const re = Jn(le, j);
      if (!re) return { ...F, layout: U };
      const me = Qw(re, w, C, L), xe = re.slot.mode === "flowchart" ? e0(me, C) : me;
      return {
        ...F,
        layout: U,
        state: {
          ...F.state,
          rootActivity: Xu(le, j, xe)
        }
      };
    });
  }, [j, we]), Mn = ue((w, C) => {
    if (!Ze.current) return null;
    const L = Ze.current.getBoundingClientRect();
    return D ? D.screenToFlowPosition({ x: w, y: C }) : {
      x: w - L.left,
      y: C - L.top
    };
  }, [D]), Tn = ue((w, C) => document.elementFromPoint(w, C)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), $n = ue((w, C, L) => {
    const F = S.find((De) => De.id === C.source), U = S.find((De) => De.id === C.target), le = F && U ? $1(F, U) : F ? Vc(F) : L, re = Dt(w, le), xe = [...S.map((De) => De.selected ? { ...De, selected: !1 } : De), re.node], Tt = u0(M, C, re.node.id);
    _(xe), W(Tt), N(re.node.id), $e(xe, Tt, [re.activityNode]);
  }, [$e, Dt, M, S]), Qt = ue((w, C, L) => {
    if (!xt || !Ze.current) return !1;
    const F = Ze.current.getBoundingClientRect();
    if (!(C >= F.left && C <= F.right && L >= F.top && L <= F.bottom)) return !1;
    const le = Mn(C, L);
    if (!le) return !1;
    if (_e) {
      const re = Tn(C, L), me = re ? M.find((xe) => xe.id === re) : void 0;
      if (me)
        return $n(w, me, le), !0;
    }
    return Dn(w, le), !0;
  }, [Dn, xt, M, Tn, _e, $n, Mn]);
  ee(() => {
    const w = (L) => {
      const F = Gt.current;
      if (!F) return;
      Math.hypot(L.clientX - F.startX, L.clientY - F.startY) >= Rb && (F.dragging = !0);
    }, C = (L) => {
      const F = Gt.current;
      if (Gt.current = null, !F?.dragging || !Ze.current || mt.current) return;
      const U = Ze.current.getBoundingClientRect();
      L.clientX >= U.left && L.clientX <= U.right && L.clientY >= U.top && L.clientY <= U.bottom && (It.current = !0, window.setTimeout(() => {
        It.current = !1;
      }, 0), Qt(F.activity, L.clientX, L.clientY));
    };
    return window.addEventListener("pointermove", w), window.addEventListener("pointerup", C), window.addEventListener("pointercancel", C), () => {
      window.removeEventListener("pointermove", w), window.removeEventListener("pointerup", C), window.removeEventListener("pointercancel", C);
    };
  }, [D, Qt]);
  const Xr = (w, C) => {
    mt.current = { activityVersionId: C.activityVersionId, handledDrop: !1 }, w.dataTransfer.setData(Sc, C.activityVersionId), w.dataTransfer.setData("text/plain", C.activityVersionId), w.dataTransfer.effectAllowed = "copy";
  }, qr = (w, C) => {
    const L = mt.current;
    mt.current = null, !L?.handledDrop && (w.clientX === 0 && w.clientY === 0 || Qt(C, w.clientX, w.clientY) && (It.current = !0, window.setTimeout(() => {
      It.current = !1;
    }, 0)));
  }, Ur = (w, C) => {
    w.button === 0 && (Gt.current = {
      activity: C,
      startX: w.clientX,
      startY: w.clientY,
      dragging: !1
    });
  }, Do = (w) => {
    It.current || xt && Dn(w);
  }, Mo = (w) => {
    if (!xt) {
      w.dataTransfer.dropEffect = "none";
      return;
    }
    if (w.preventDefault(), w.dataTransfer.dropEffect = "copy", !_e) return;
    const C = Tn(w.clientX, w.clientY);
    z(C);
  }, To = (w) => {
    if (!Ze.current) return;
    const C = w.relatedTarget;
    C && Ze.current.contains(C) || z(null);
  }, $o = (w) => {
    w.preventDefault(), z(null);
    const C = w.dataTransfer.getData(Sc) || w.dataTransfer.getData("text/plain");
    if (!C || (w.stopPropagation(), mt.current?.activityVersionId === C && (mt.current.handledDrop = !0), !xt)) return;
    const L = ze.get(C);
    L && Qt(L, w.clientX, w.clientY);
  }, Yr = () => {
    if (!_e) return;
    const w = Ze.current?.getBoundingClientRect();
    w && I({
      kind: "fromEmpty",
      clientX: w.left + w.width / 2,
      clientY: w.top + w.height / 2
    });
  }, Pn = ue(async (w, C) => {
    const L = async () => {
      const U = ++Sn.current, le = Xe(w);
      B("");
      try {
        const re = await ev(e, w), me = Xe(re);
        return jn.current = me, d((xe) => !xe || xe.id !== re.id ? xe : Xe(xe) === le ? re : { ...xe, validationErrors: re.validationErrors }), U === Sn.current && O(C), re;
      } catch (re) {
        throw U === Sn.current && (O(""), B(re instanceof Error ? re.message : String(re))), re;
      }
    }, F = So.current.then(L, L);
    return So.current = F.catch(() => {
    }), F;
  }, [e]);
  ee(() => {
    if (!G || !l || Xe(l) === jn.current) return;
    O("Autosaving...");
    const C = window.setTimeout(() => {
      Pn(l, "Autosaved").catch(() => {
      });
    }, Vb);
    return () => window.clearTimeout(C);
  }, [G, l, Pn]), ee(() => {
    if (!l) return;
    if (Zt.current) {
      Zt.current = !1;
      return;
    }
    const w = Xe(l);
    if (w === dt.current) return;
    const C = window.setTimeout(() => {
      const L = ut.current;
      L && (Be.current = xc(Be.current, L), En((F) => F + 1)), ut.current = Xn(l), dt.current = w;
    }, Ob);
    return () => window.clearTimeout(C);
  }, [l]);
  const Zr = ue(() => {
    if (!l) return;
    const w = c?.definition.name;
    Jv(Yv(l, w), w), O("Exported workflow as JSON.");
  }, [l, c]), Gr = ue((w) => {
    d((C) => C && { ...C, state: w(C.state) });
  }, []), Jr = ue((w) => {
    if (!l) return "No draft is loaded.";
    const C = Gv(w, l);
    return C.ok ? (d(C.draft), N(null), k([]), O("Applied workflow JSON."), null) : C.error;
  }, [l]), Mt = ue(() => {
    if (!l) return;
    const w = Xe(l);
    if (w === dt.current) return;
    const C = ut.current;
    C && (Be.current = xc(Be.current, C)), ut.current = Xn(l), dt.current = w;
  }, [l]), en = ue((w) => {
    Zt.current = !0, ut.current = Xn(w), dt.current = Xe(w), d(w), N(null), k([]), En((C) => C + 1);
  }, []), tn = ue(() => {
    if (!l) return;
    Mt();
    const w = ab(Be.current, l);
    w && (Be.current = w.history, en(w.snapshot));
  }, [l, Mt, en]), zn = ue(() => {
    if (!l) return;
    Mt();
    const w = cb(Be.current, l);
    w && (Be.current = w.history, en(w.snapshot));
  }, [l, Mt, en]), { canUndoNow: Qr, canRedoNow: Ln } = de(() => {
    const w = !!l && !!ut.current && Xe(l) !== dt.current;
    return {
      canUndoNow: ib(Be.current) || w,
      canRedoNow: sb(Be.current) && !w
    };
  }, [l, Br]);
  ee(() => {
    const w = (C) => {
      if (Ct !== "designer" || !(C.metaKey || C.ctrlKey)) return;
      const L = C.target;
      if (L && (L.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(L.tagName))) return;
      const F = C.key.toLowerCase();
      F === "z" && !C.shiftKey ? (C.preventDefault(), tn()) : (F === "z" && C.shiftKey || F === "y") && (C.preventDefault(), zn());
    };
    return window.addEventListener("keydown", w), () => window.removeEventListener("keydown", w);
  }, [Ct, tn, zn]);
  const ei = async () => {
    if (!(!l || ft)) {
      q("saving"), O("Saving...");
      try {
        await Pn(l, "Saved");
      } catch {
      } finally {
        q("idle");
      }
    }
  }, ti = async () => {
    if (!(!l || ft)) {
      q("promoting"), O("Promoting...");
      try {
        const w = await tv(e, l.id), C = await nv(e, w.versionId);
        ae(C.artifactId), O(`Published ${C.artifactVersion}`), await _n();
      } catch (w) {
        O(""), B(w instanceof Error ? w.message : String(w));
      } finally {
        q("idle");
      }
    }
  }, Po = async () => {
    if (!l?.state.rootActivity || ft) return;
    const w = l, C = Xe(w);
    se(null), O("Preparing test run...");
    try {
      q("testRunPreparing"), O("Preparing test run...");
      const L = R1(w);
      q("testRunStarting"), O("Starting test run...");
      const F = await ov(e, {
        definitionId: w.definitionId,
        snapshotId: L,
        state: w.state
      });
      se({ draftSignature: C, view: F }), Qe("runtime"), Oe(!1), O(Ms(F) ? "Test run rejected" : "Test run dispatched");
    } catch (L) {
      O(""), B(L instanceof Error ? L.message : String(L));
    } finally {
      q("idle");
    }
  }, Wd = (w) => {
    const C = we ? w.filter((L) => L.type === "select") : w;
    C.length !== 0 && _((L) => eu(C, L));
  }, Bd = (w) => {
    we || W((C) => tu(w, C));
  }, ni = (w) => !w.source || !w.target || w.source === w.target || !_e ? !1 : !w.targetHandle, Fd = (w) => {
    if (!l?.state.rootActivity || !Ke || !_e || !ni(w)) return;
    const C = wr(w.source, w.target, w.sourceHandle ?? "Done", w.targetHandle ?? void 0), L = ou(C, M);
    W(L), $e(S, L);
  }, Kd = () => {
    $e(S, M);
  }, Xd = !we && S.length > 0, qd = ue(() => {
    if (we || S.length === 0) return;
    const w = Ke?.slot.mode === "sequence" ? "sequence" : "flowchart", C = tb(S, M, w), L = S.map((F) => {
      const U = C.get(F.id);
      return U ? { ...F, position: U } : F;
    });
    _(L), $e(L, M), window.requestAnimationFrame(() => D?.fitView({ padding: 0.2 })), O("Rearranged the canvas.");
  }, [M, S, Ke, we, $e, D]), Ud = (w, C) => {
    if (!C.nodeId || C.handleType === "target") {
      Yt.current = null;
      return;
    }
    Yt.current = {
      nodeId: C.nodeId,
      handleId: C.handleId ?? null
    };
  }, Yd = (w, C) => {
    const L = z1(Yt.current, C);
    if (Yt.current = null, !L || !_e || C.toNode || C.toHandle || P1(w)) return;
    const F = Rd(w);
    I({
      kind: "fromPort",
      sourceNodeId: L.nodeId,
      sourceHandleId: L.handleId,
      clientX: F.x,
      clientY: F.y
    });
  }, Zd = (w, C) => {
    if (!_e || !ni(C)) return;
    const L = Dm(w, {
      ...C,
      sourceHandle: C.sourceHandle ?? "Done",
      targetHandle: C.targetHandle ?? void 0
    }, M, { shouldReplaceId: !1 });
    W(L), $e(S, L);
  }, Gd = (w) => {
    if (we || w.length === 0) return;
    const C = new Set(w.map((U) => U.id)), L = S.filter((U) => !C.has(U.id)), F = M.filter((U) => !C.has(U.source) && !C.has(U.target));
    _(L), W(F), R && C.has(R) && N(null), $e(L, F);
  }, Jd = (w) => {
    if (we || w.length === 0) return;
    const C = new Set(w.map((F) => F.id)), L = M.filter((F) => !C.has(F.id));
    W(L), $e(S, L);
  }, Ts = ue((w) => {
    if (we) return;
    const C = M.filter((L) => L.id !== w);
    W(C), $e(S, C);
  }, [$e, M, we, S]), $s = ue((w, C, L) => {
    _e && I({ kind: "spliceEdge", edgeId: w, clientX: C, clientY: L });
  }, [_e]), Qd = (w) => {
    const C = A;
    if (!C) return;
    I(null);
    const L = Mn(C.clientX, C.clientY) ?? { x: 0, y: 0 };
    if (C.kind === "fromEmpty") {
      const U = Dt(w, L), re = [...S.map((me) => me.selected ? { ...me, selected: !1 } : me), U.node];
      _(re), N(U.node.id), $e(re, M, [U.activityNode]);
      return;
    }
    if (C.kind === "fromPort") {
      const U = S.find((De) => De.id === C.sourceNodeId), le = U ? Vc(U) : L, re = Dt(w, le), xe = [...S.map((De) => De.selected ? { ...De, selected: !1 } : De), re.node], Tt = [...M, wr(C.sourceNodeId, re.node.id, C.sourceHandleId ?? "Done")];
      _(xe), W(Tt), N(re.node.id), $e(xe, Tt, [re.activityNode]);
      return;
    }
    const F = M.find((U) => U.id === C.edgeId);
    F && $n(w, F, L);
  }, ef = de(() => ({
    highlightedEdgeId: $,
    deleteEdge: Ts,
    requestInsertActivity: $s
  }), [Ts, $, $s]), tf = (w, C, L) => {
    k((F) => [...F, { ownerNodeId: w.nodeId, slotId: C, label: L }]), N(null);
  }, Ps = ue((w) => {
    d((C) => {
      const L = C?.state.rootActivity;
      return !C || !L ? C : {
        ...C,
        state: {
          ...C.state,
          rootActivity: qu(L, w.nodeId, () => w)
        }
      };
    });
  }, []), nf = ue((w) => {
    if (!w) return;
    const C = l?.state.rootActivity;
    if (!C) return;
    const L = Gw(C, w, (F) => {
      const U = ze.get(F.activityVersionId);
      return U ? Ae(U) : F.nodeId;
    });
    L && (Ut("designer"), k(L), N(w), Oe(!1));
  }, [l?.state.rootActivity, ze]), of = (w) => {
    J((C) => {
      const L = new Set(C);
      return L.has(w) ? L.delete(w) : L.add(w), L;
    });
  }, zs = (w) => {
    He((C) => C === w ? null : C), w === "palette" ? Te((C) => !C) : Oe((C) => !C);
  }, Ls = (w) => {
    w === "palette" ? Te(!1) : Oe(!1), He((C) => C === w ? null : w);
  }, Rs = (w, C) => {
    He(null), w === "palette" ? (Te(!1), Q((L) => nr(L + C, Hn, Wn))) : (Oe(!1), ve((L) => nr(L + C, Bn, Fn)));
  }, Vs = (w, C) => {
    C.preventDefault(), He(null), w === "palette" ? Te(!1) : Oe(!1);
    const L = C.clientX, F = w === "palette" ? H : ye, U = w === "palette" ? Hn : Bn, le = w === "palette" ? Wn : Fn;
    document.body.classList.add("wf-side-panel-resizing");
    const re = (xe) => {
      const Tt = w === "palette" ? xe.clientX - L : L - xe.clientX, De = nr(F + Tt, U, le);
      w === "palette" ? Q(De) : ve(De);
    }, me = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", re), window.removeEventListener("pointerup", me), window.removeEventListener("pointercancel", me);
    };
    window.addEventListener("pointermove", re), window.addEventListener("pointerup", me), window.addEventListener("pointercancel", me);
  }, Os = (w, C) => {
    C.key === "ArrowLeft" ? (C.preventDefault(), Rs(w, w === "palette" ? -Xo : Xo)) : C.key === "ArrowRight" ? (C.preventDefault(), Rs(w, w === "palette" ? Xo : -Xo)) : C.key === "Home" ? (C.preventDefault(), w === "palette" ? Q(Hn) : ve(Bn)) : C.key === "End" && (C.preventDefault(), w === "palette" ? Q(Wn) : ve(Fn));
  };
  if (!c || !l)
    return /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: P || "Loading workflow editor..." });
  const rf = [
    "wf-editor-body",
    ke ? "palette-collapsed" : "",
    Ie ? "inspector-collapsed" : "",
    be === "palette" ? "palette-maximized" : "",
    be === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), sf = {
    "--wf-palette-width": `${ke ? Ac : H}px`,
    "--wf-inspector-width": `${Ie ? Ac : ye}px`
  }, Hs = !ke && be !== "inspector", Ws = !Ie && be !== "palette", zo = oe?.draftSignature === Xe(l) ? oe.view : null, Bs = zo && K.startsWith("Test run") ? "" : K, af = (w) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(w)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, cf = {
    definition: c.definition,
    draft: l,
    selectedActivity: ge,
    selectedActivityDescriptor: _t,
    selectedActivitySlots: In,
    catalog: f,
    currentScopeOwner: Fe,
    frames: j
  }, Fs = s.map((w) => {
    const C = w.component;
    return {
      id: w.id,
      title: w.title,
      side: w.side,
      order: w.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ i.jsx(C, { context: cf })
    };
  }), oi = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ i.jsx(Lr, { size: 15 }),
      render: uf
    },
    ...Fs.filter((w) => w.side === "left")
  ].sort(zc), ri = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ i.jsx(gs, { size: 15 }),
      render: df
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: /* @__PURE__ */ i.jsx(xn, { size: 15 }),
      render: () => /* @__PURE__ */ i.jsx(T1, { testRun: zo, onOpenRun: af })
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ i.jsx(Vu, { size: 15 }),
      render: () => /* @__PURE__ */ i.jsx(
        t1,
        {
          context: e,
          ai: n,
          definitionId: c.definition.id,
          publishedArtifactId: Y
        }
      )
    },
    ...Fs.filter((w) => w.side === "right")
  ].sort(zc), Ks = oi.find((w) => w.id === We) ?? oi[0], Xs = ri.find((w) => w.id === Je) ?? ri[0], lf = [
    { id: "designer", title: "Designer", order: 0, icon: /* @__PURE__ */ i.jsx(Vw, { size: 14 }), render: () => null },
    { id: "code", title: "Code", order: 1, icon: /* @__PURE__ */ i.jsx(Dw, { size: 14 }), render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: /* @__PURE__ */ i.jsx(xs, { size: 14 }), render: () => null }
  ];
  function uf() {
    const w = te.trim().length > 0;
    return /* @__PURE__ */ i.jsxs("div", { className: "wf-palette-body", children: [
      /* @__PURE__ */ i.jsxs("label", { className: "wf-palette-search", children: [
        /* @__PURE__ */ i.jsx(Rr, { size: 14, "aria-hidden": "true" }),
        /* @__PURE__ */ i.jsx(
          "input",
          {
            type: "search",
            value: te,
            placeholder: "Search activities",
            "aria-label": "Search activity palette",
            onChange: (C) => fe(C.target.value)
          }
        )
      ] }),
      /* @__PURE__ */ i.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: Co.length === 0 ? /* @__PURE__ */ i.jsx("p", { className: "wf-muted wf-palette-empty", children: "No matching activities." }) : Co.map((C) => {
        const L = w || ce.has(C.category);
        return /* @__PURE__ */ i.jsxs("div", { className: "wf-palette-category", children: [
          /* @__PURE__ */ i.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-category-toggle",
              role: "treeitem",
              "aria-expanded": L,
              onClick: () => of(C.category),
              children: [
                L ? /* @__PURE__ */ i.jsx(zu, { size: 14 }) : /* @__PURE__ */ i.jsx(on, { size: 14 }),
                /* @__PURE__ */ i.jsx("span", { children: C.category }),
                /* @__PURE__ */ i.jsx("small", { children: C.activities.length })
              ]
            }
          ),
          L ? /* @__PURE__ */ i.jsx("div", { className: "wf-palette-activities", role: "group", children: C.activities.map((F) => {
            const U = F.description?.trim(), le = U ? `wf-palette-description-${F.activityVersionId}` : void 0, re = Ae(F), me = xr(F);
            return /* @__PURE__ */ i.jsxs(
              "button",
              {
                type: "button",
                className: "wf-palette-activity",
                role: "treeitem",
                draggable: !0,
                title: U || Ae(F),
                "aria-describedby": le,
                onClick: () => Do(F),
                onDragStart: (xe) => Xr(xe, F),
                onDragEnd: (xe) => qr(xe, F),
                onPointerDown: (xe) => Ur(xe, F),
                children: [
                  /* @__PURE__ */ i.jsx("span", { className: "wf-activity-icon", "data-icon": me, "aria-hidden": "true", children: js(me) }),
                  /* @__PURE__ */ i.jsxs("span", { className: "wf-palette-activity-text", children: [
                    /* @__PURE__ */ i.jsx("strong", { children: re }),
                    U ? /* @__PURE__ */ i.jsx("small", { id: le, children: U }) : null
                  ] }),
                  /* @__PURE__ */ i.jsx(Ru, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
                ]
              },
              F.activityVersionId
            );
          }) }) : null
        ] }, C.category);
      }) })
    ] });
  }
  function df() {
    return ge ? /* @__PURE__ */ i.jsxs("div", { className: "wf-inspector-content", children: [
      /* @__PURE__ */ i.jsx("h3", { children: S.find((w) => w.id === ge.nodeId)?.data.label ?? ge.nodeId }),
      /* @__PURE__ */ i.jsxs("dl", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Node ID" }),
        /* @__PURE__ */ i.jsx("dd", { children: ge.nodeId }),
        /* @__PURE__ */ i.jsx("dt", { children: "Activity type" }),
        /* @__PURE__ */ i.jsx("dd", { children: _t?.typeName ?? ze.get(ge.activityVersionId)?.activityTypeKey ?? "Unknown" }),
        /* @__PURE__ */ i.jsx("dt", { children: "Activity version" }),
        /* @__PURE__ */ i.jsx("dd", { children: ge.activityVersionId })
      ] }),
      Io ? /* @__PURE__ */ i.jsxs("div", { className: "wf-availability-notice", children: [
        /* @__PURE__ */ i.jsx(mr, { size: 14 }),
        /* @__PURE__ */ i.jsxs("span", { children: [
          "No longer available for new use · ",
          Nr(Io.state)
        ] })
      ] }) : null,
      /* @__PURE__ */ i.jsx(
        Rv,
        {
          activity: ge,
          descriptor: _t,
          editors: o,
          expressionEditors: r,
          expressionDescriptors: m,
          descriptorStatus: g,
          visibleVariables: An.visibleVariables,
          scopeStatus: An.status,
          onChange: Ps
        }
      ),
      z0(ge) ? /* @__PURE__ */ i.jsx("div", { className: "wf-container-variables", children: /* @__PURE__ */ i.jsx(
        gb,
        {
          context: e,
          variables: ad(ge),
          title: "Container variables",
          addLabel: "Add container variable",
          emptyLabel: "No container variables declared on this activity.",
          warnings: O0(An.shadowingWarnings, ge.nodeId),
          onChange: (w) => Ps(L0(ge, w))
        }
      ) }) : null,
      In.length > 0 ? /* @__PURE__ */ i.jsxs("div", { className: "wf-slot-list", children: [
        /* @__PURE__ */ i.jsx("span", { children: "Embedded slots" }),
        In.map((w) => /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => tf(ge, w.id, `${S.find((C) => C.id === ge.nodeId)?.data.label ?? ge.nodeId} / ${w.label}`), children: [
          w.label,
          /* @__PURE__ */ i.jsxs("small", { children: [
            w.activities.length,
            " activit",
            w.activities.length === 1 ? "y" : "ies"
          ] })
        ] }, w.id))
      ] }) : /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
    ] }) : /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
  }
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ i.jsx("button", { type: "button", className: "wf-link-button", onClick: a, children: "Definitions" }),
      /* @__PURE__ */ i.jsx(on, { size: 14 }),
      /* @__PURE__ */ i.jsx("strong", { children: c.definition.name }),
      /* @__PURE__ */ i.jsx("span", { className: "wf-chip", children: "Draft" }),
      Bs ? /* @__PURE__ */ i.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ i.jsx(bn, { size: 13 }),
        " ",
        Bs
      ] }) : null,
      /* @__PURE__ */ i.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ i.jsxs("div", { className: "wf-canvas-tools", role: "group", "aria-label": "Canvas tools", children: [
          /* @__PURE__ */ i.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Undo",
              title: "Undo (Ctrl+Z)",
              disabled: !Qr,
              onClick: tn,
              children: /* @__PURE__ */ i.jsx(Rw, { size: 16 })
            }
          ),
          /* @__PURE__ */ i.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Redo",
              title: "Redo (Ctrl+Shift+Z)",
              disabled: !Ln,
              onClick: zn,
              children: /* @__PURE__ */ i.jsx(zw, { size: 16 })
            }
          ),
          /* @__PURE__ */ i.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Auto-layout",
              title: "Auto-layout the canvas",
              disabled: !Xd,
              onClick: qd,
              children: /* @__PURE__ */ i.jsx(Pw, { size: 16 })
            }
          )
        ] }),
        /* @__PURE__ */ i.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ i.jsx("input", { className: "wf-autosave-switch-input", type: "checkbox", checked: G, onChange: (w) => V(w.target.checked) }),
          /* @__PURE__ */ i.jsx("span", { children: "Autosave" })
        ] }),
        Ao ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => kt(n, Ao, { definition: c.definition, draft: l }), children: [
          /* @__PURE__ */ i.jsx(St, { size: 15 }),
          " Risks"
        ] }) : null,
        _o ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => kt(n, _o, { definition: c.definition, draft: l }), children: [
          /* @__PURE__ */ i.jsx(St, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ i.jsxs("button", { type: "button", title: "Export workflow as JSON", onClick: Zr, children: [
          /* @__PURE__ */ i.jsx(Tw, { size: 15 }),
          " Export"
        ] }),
        /* @__PURE__ */ i.jsxs("button", { type: "button", disabled: ft, onClick: () => {
          ei();
        }, children: [
          /* @__PURE__ */ i.jsx(Ou, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ i.jsxs("button", { type: "button", disabled: ft, onClick: () => {
          ti();
        }, children: [
          /* @__PURE__ */ i.jsx(Lu, { size: 15 }),
          " Promote"
        ] }),
        zo ? /* @__PURE__ */ i.jsx(
          M1,
          {
            testRun: zo,
            onOpenDetails: () => {
              Qe("runtime"), Oe(!1);
            }
          }
        ) : null,
        /* @__PURE__ */ i.jsxs(
          "button",
          {
            type: "button",
            disabled: !Fr,
            title: l.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              Po();
            },
            children: [
              /* @__PURE__ */ i.jsx(xn, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    P ? /* @__PURE__ */ i.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ i.jsx(Et, { size: 16 }),
      " ",
      P
    ] }) : null,
    /* @__PURE__ */ i.jsxs("div", { className: rf, style: sf, children: [
      /* @__PURE__ */ i.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ i.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ i.jsx(
            or,
            {
              label: "Activities panel tabs",
              tabs: oi,
              activeTabId: Ks.id,
              onSelect: Ge
            }
          ),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": ke ? "Expand activities panel" : "Collapse activities panel",
                title: ke ? "Expand" : "Collapse",
                onClick: () => zs("palette"),
                children: ke ? /* @__PURE__ */ i.jsx(on, { size: 14 }) : /* @__PURE__ */ i.jsx(yr, { size: 14 })
              }
            ),
            ke ? null : /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": be === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: be === "palette" ? "Restore" : "Maximize",
                onClick: () => Ls("palette"),
                children: be === "palette" ? /* @__PURE__ */ i.jsx(rc, { size: 14 }) : /* @__PURE__ */ i.jsx(gr, { size: 14 })
              }
            )
          ] })
        ] }),
        Hs ? Ks.render() : null
      ] }),
      Hs && !be ? /* @__PURE__ */ i.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Hn,
          "aria-valuemax": Wn,
          "aria-valuenow": H,
          tabIndex: 0,
          onPointerDown: (w) => Vs("palette", w),
          onKeyDown: (w) => Os("palette", w)
        }
      ) : /* @__PURE__ */ i.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ i.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ i.jsx("div", { className: "wf-canvas-tabs", children: /* @__PURE__ */ i.jsx(
          or,
          {
            label: "Editor view tabs",
            tabs: lf,
            activeTabId: Ct,
            onSelect: (w) => Ut(w)
          }
        ) }),
        Ct === "code" ? /* @__PURE__ */ i.jsx(hb, { draft: l, onApply: Jr }) : Ct === "properties" ? /* @__PURE__ */ i.jsx(mb, { details: c, draft: l, context: e, onStateChange: Gr }) : /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
          /* @__PURE__ */ i.jsxs("div", { className: "wf-breadcrumb", children: [
            /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => {
              k([]), N(null);
            }, children: "Root" }),
            j.map((w, C) => /* @__PURE__ */ i.jsxs(ct.Fragment, { children: [
              /* @__PURE__ */ i.jsx(on, { size: 13 }),
              /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => {
                k(j.slice(0, C + 1)), N(null);
              }, children: w.label })
            ] }, `${w.ownerNodeId}-${w.slotId}-${C}`))
          ] }),
          /* @__PURE__ */ i.jsxs("div", { className: "wf-canvas", ref: Ze, onDragOver: Mo, onDragLeave: To, onDrop: $o, children: [
            /* @__PURE__ */ i.jsx(_d.Provider, { value: ef, children: /* @__PURE__ */ i.jsx(Dd.Provider, { value: Cn, children: /* @__PURE__ */ i.jsxs(
              Iu,
              {
                nodes: S,
                edges: M,
                nodeTypes: Cd,
                edgeTypes: Id,
                onInit: T,
                onNodesChange: Wd,
                onEdgesChange: Bd,
                onNodesDelete: Gd,
                onEdgesDelete: Jd,
                onConnect: Fd,
                onConnectStart: _e ? Ud : void 0,
                onConnectEnd: _e ? Yd : void 0,
                onReconnect: _e ? Zd : void 0,
                isValidConnection: ni,
                onDragOver: Mo,
                onDragLeave: To,
                onDrop: $o,
                onPaneClick: () => N(null),
                onNodeClick: (w, C) => N(C.id),
                onNodeDragStop: we ? void 0 : Kd,
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
                  /* @__PURE__ */ i.jsx(_u, { gap: 18, size: 1 }),
                  /* @__PURE__ */ i.jsx(Mu, {}),
                  /* @__PURE__ */ i.jsx($u, { pannable: !0, zoomable: !0 })
                ]
              }
            ) }) }),
            _e && S.length === 0 ? /* @__PURE__ */ i.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => Yr(), children: [
              /* @__PURE__ */ i.jsx(wn, { size: 15 }),
              " Add activity"
            ] }) : null,
            A ? /* @__PURE__ */ i.jsx(
              _1,
              {
                clientX: A.clientX,
                clientY: A.clientY,
                activities: f,
                onPick: Qd,
                onClose: () => I(null)
              }
            ) : null
          ] }),
          /* @__PURE__ */ i.jsx(D1, { draft: l, onRepair: nf })
        ] })
      ] }),
      Ws && !be ? /* @__PURE__ */ i.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Bn,
          "aria-valuemax": Fn,
          "aria-valuenow": ye,
          tabIndex: 0,
          onPointerDown: (w) => Vs("inspector", w),
          onKeyDown: (w) => Os("inspector", w)
        }
      ) : /* @__PURE__ */ i.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ i.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ i.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ i.jsx(
            or,
            {
              label: "Inspector panel tabs",
              tabs: ri,
              activeTabId: Xs.id,
              onSelect: Qe
            }
          ),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": Ie ? "Expand inspector panel" : "Collapse inspector panel",
                title: Ie ? "Expand" : "Collapse",
                onClick: () => zs("inspector"),
                children: Ie ? /* @__PURE__ */ i.jsx(yr, { size: 14 }) : /* @__PURE__ */ i.jsx(on, { size: 14 })
              }
            ),
            Ie ? null : /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": be === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: be === "inspector" ? "Restore" : "Maximize",
                onClick: () => Ls("inspector"),
                children: be === "inspector" ? /* @__PURE__ */ i.jsx(rc, { size: 14 }) : /* @__PURE__ */ i.jsx(gr, { size: 14 })
              }
            )
          ] })
        ] }),
        Ws ? Xs.render() : null
      ] })
    ] })
  ] });
}
function or({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: o
}) {
  return /* @__PURE__ */ i.jsx("div", { className: "wf-panel-tab-list", role: "tablist", "aria-label": e, children: t.map((r) => /* @__PURE__ */ i.jsxs(
    "button",
    {
      type: "button",
      role: "tab",
      "aria-selected": r.id === n,
      className: r.id === n ? "active" : "",
      title: r.title,
      onClick: () => o(r.id),
      children: [
        r.icon ? /* @__PURE__ */ i.jsx("span", { className: "wf-panel-tab-icon", "aria-hidden": "true", children: r.icon }) : null,
        /* @__PURE__ */ i.jsx("span", { children: r.title })
      ]
    },
    r.id
  )) });
}
function zc(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
function C1({ data: e, selected: t }) {
  const n = e, o = n.runtime, r = !n.suppressFlowPorts, s = r ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], a = I1(n), u = ct.useContext(Dd)?.({ activityVersionId: n.activityVersionId, activityTypeKey: n.activityTypeKey }) ?? null;
  return /* @__PURE__ */ i.jsxs(
    "div",
    {
      className: ["wf-node", t ? "selected" : "", o ? "wf-node-runtime" : "", o?.hasBlockingIncident ? "faulted" : "", u ? "wf-node-unavailable" : ""].filter(Boolean).join(" "),
      "data-icon": n.icon ?? "activity",
      children: [
        r && n.acceptsInbound ? /* @__PURE__ */ i.jsx(mn, { type: "target", position: ne.Left }) : null,
        u ? /* @__PURE__ */ i.jsx("span", { className: "wf-node-availability", title: `No longer available for new use · ${Nr(u.state)}`, children: /* @__PURE__ */ i.jsx(mr, { size: 13 }) }) : null,
        /* @__PURE__ */ i.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ i.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: js(n.icon) }),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-node-copy", children: [
            /* @__PURE__ */ i.jsx("strong", { children: n.label }),
            a ? /* @__PURE__ */ i.jsx("small", { children: a }) : null
          ] })
        ] }),
        n.childSlots.length > 0 ? /* @__PURE__ */ i.jsxs("span", { className: "wf-node-slot-badge", children: [
          n.childSlots.length,
          " slot",
          n.childSlots.length === 1 ? "" : "s"
        ] }) : null,
        o ? /* @__PURE__ */ i.jsxs("div", { className: "wf-node-runtime-strip", children: [
          o.status ? /* @__PURE__ */ i.jsx(jo, { status: o.status, subStatus: o.subStatus }) : null,
          o.incidentCount > 0 ? /* @__PURE__ */ i.jsxs("span", { className: "wf-node-runtime-count", children: [
            o.incidentCount,
            " incident",
            o.incidentCount === 1 ? "" : "s"
          ] }) : null,
          o.faultCount > 0 ? /* @__PURE__ */ i.jsxs("span", { className: "wf-node-runtime-count", children: [
            o.faultCount,
            " faults"
          ] }) : null
        ] }) : null,
        s.map((l, d) => {
          const f = `${(d + 1) / (s.length + 1) * 100}%`;
          return /* @__PURE__ */ i.jsxs(ct.Fragment, { children: [
            /* @__PURE__ */ i.jsx("span", { className: "wf-node-port-label", style: { top: f }, children: l.displayName }),
            /* @__PURE__ */ i.jsx(mn, { type: "source", position: ne.Right, id: l.name, style: { top: f } })
          ] }, l.name);
        })
      ]
    }
  );
}
function I1(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((r) => !!r).join(" · ");
}
function A1(e) {
  const {
    id: t,
    sourceX: n,
    sourceY: o,
    targetX: r,
    targetY: s,
    sourcePosition: a,
    targetPosition: c,
    markerEnd: u,
    style: l,
    label: d,
    labelStyle: f
  } = e, h = ct.useContext(_d), [p, y] = X(!1), [v, x, m] = hr({ sourceX: n, sourceY: o, targetX: r, targetY: s, sourcePosition: a, targetPosition: c }), E = h?.highlightedEdgeId === t;
  return /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
    /* @__PURE__ */ i.jsx(
      wo,
      {
        id: t,
        path: v,
        markerEnd: u,
        style: {
          ...l,
          strokeWidth: E ? 2.5 : l?.strokeWidth
        },
        label: d,
        labelX: x,
        labelY: m,
        labelStyle: f,
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1)
      }
    ),
    h ? /* @__PURE__ */ i.jsx(ew, { children: /* @__PURE__ */ i.jsxs(
      "div",
      {
        className: ["wf-edge-actions", p ? "visible" : "", E ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${x}px, ${m}px)` },
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1),
        children: [
          /* @__PURE__ */ i.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (g) => h.requestInsertActivity(t, g.clientX, g.clientY), children: /* @__PURE__ */ i.jsx(wn, { size: 12 }) }),
          /* @__PURE__ */ i.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => h.deleteEdge(t), children: /* @__PURE__ */ i.jsx(lo, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function _1({ clientX: e, clientY: t, activities: n, onPick: o, onClose: r }) {
  const [s, a] = X(""), [c, u] = X(0), l = ie(null), d = ie(null), f = de(() => {
    const E = s.trim().toLowerCase(), g = n.filter(m1);
    return E ? g.filter((b) => Ae(b).toLowerCase().includes(E) || b.activityTypeKey.toLowerCase().includes(E) || (b.category ?? "").toLowerCase().includes(E) || (b.description ?? "").toLowerCase().includes(E)) : g;
  }, [n, s]), h = de(() => Yi(f), [f]), p = de(() => h.flatMap((E) => E.activities), [h]);
  ee(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), ee(() => {
    const E = (b) => {
      l.current?.contains(b.target) || r();
    }, g = (b) => {
      b.key === "Escape" && r();
    };
    return document.addEventListener("mousedown", E, !0), document.addEventListener("keydown", g), () => {
      document.removeEventListener("mousedown", E, !0), document.removeEventListener("keydown", g);
    };
  }, [r]);
  const y = (E) => {
    if (E.key === "ArrowDown")
      E.preventDefault(), u((g) => Math.min(g + 1, p.length - 1));
    else if (E.key === "ArrowUp")
      E.preventDefault(), u((g) => Math.max(g - 1, 0));
    else if (E.key === "Enter") {
      E.preventDefault();
      const g = p[c];
      g && o(g);
    }
  }, v = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), x = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let m = -1;
  return /* @__PURE__ */ i.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: v, top: x }, onMouseDown: (E) => E.stopPropagation(), onClick: (E) => E.stopPropagation(), children: [
    /* @__PURE__ */ i.jsx(
      "input",
      {
        ref: d,
        type: "search",
        value: s,
        placeholder: "Search activities...",
        "aria-label": "Search activities",
        onChange: (E) => {
          a(E.target.value), u(0);
        },
        onKeyDown: y
      }
    ),
    /* @__PURE__ */ i.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: h.length === 0 ? /* @__PURE__ */ i.jsx("p", { children: "No matching activities." }) : h.map((E) => /* @__PURE__ */ i.jsxs("section", { children: [
      /* @__PURE__ */ i.jsx("h4", { children: E.category }),
      E.activities.map((g) => {
        m += 1;
        const b = m, j = b === c;
        return /* @__PURE__ */ i.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": j,
            className: j ? "active" : "",
            onMouseEnter: () => u(b),
            onClick: () => o(g),
            children: [
              /* @__PURE__ */ i.jsx("strong", { children: Ae(g) }),
              /* @__PURE__ */ i.jsx("small", { children: g.category || g.activityTypeKey })
            ]
          },
          g.activityVersionId
        );
      })
    ] }, E.category)) })
  ] });
}
function D1({ draft: e, onRepair: t }) {
  const n = e.validationErrors;
  if (!n.length)
    return /* @__PURE__ */ i.jsxs("div", { className: "wf-validation ok", children: [
      /* @__PURE__ */ i.jsx(bn, { size: 14 }),
      " No validation errors"
    ] });
  const o = Nb(n), r = new Map(o.map((s) => [s.error, s]));
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-validation-summary", children: [
      /* @__PURE__ */ i.jsx(Et, { size: 14 }),
      n.length,
      " validation issue",
      n.length === 1 ? "" : "s",
      o.length > 0 ? /* @__PURE__ */ i.jsxs("span", { className: "wf-validation-variable-count", children: [
        " · ",
        o.length,
        " invalid variable reference",
        o.length === 1 ? "" : "s"
      ] }) : null
    ] }),
    /* @__PURE__ */ i.jsx("ul", { className: "wf-validation-list", children: n.map((s, a) => {
      const c = r.get(s);
      return /* @__PURE__ */ i.jsxs("li", { className: c ? "wf-validation-item repairable" : "wf-validation-item", children: [
        /* @__PURE__ */ i.jsx("span", { className: "wf-validation-message", children: s.message ?? "Validation issue." }),
        c?.path.nodeId ? /* @__PURE__ */ i.jsxs("button", { type: "button", className: "wf-validation-repair", onClick: () => t(c.path.nodeId), children: [
          /* @__PURE__ */ i.jsx(Ow, { size: 12 }),
          " Repair"
        ] }) : null
      ] }, a);
    }) })
  ] });
}
function M1({
  testRun: e,
  onOpenDetails: t
}) {
  const n = Ms(e);
  return /* @__PURE__ */ i.jsx("div", { className: "wf-test-run-status", "data-state": n ? "rejected" : "accepted", children: /* @__PURE__ */ i.jsxs(
    "button",
    {
      type: "button",
      className: "wf-test-run-trigger",
      onClick: t,
      children: [
        n ? /* @__PURE__ */ i.jsx(Et, { size: 16 }) : /* @__PURE__ */ i.jsx(bn, { size: 16 }),
        n ? "Test run rejected" : "Test run dispatched"
      ]
    }
  ) });
}
function T1({ testRun: e, onOpenRun: t }) {
  if (!e)
    return /* @__PURE__ */ i.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "Run the draft to see Runtime Evidence." }) });
  const n = Ms(e), o = e.workflowExecutionId;
  return /* @__PURE__ */ i.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ i.jsxs("section", { className: "wf-runtime-card", "data-state": n ? "rejected" : "accepted", children: [
    /* @__PURE__ */ i.jsxs("header", { children: [
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("span", { children: "Latest Test Run" }),
        /* @__PURE__ */ i.jsx("h3", { children: n ? "Rejected by the server" : "Transient run accepted" })
      ] }),
      /* @__PURE__ */ i.jsx(jo, { status: e.status, subStatus: e.commandDispatchStatus ?? void 0 })
    ] }),
    /* @__PURE__ */ i.jsx("p", { children: "Ephemeral - not saved, promoted, or published." }),
    n && e.reason ? /* @__PURE__ */ i.jsxs("div", { className: "wf-runtime-reason", children: [
      /* @__PURE__ */ i.jsx(Et, { size: 14 }),
      " ",
      e.reason
    ] }) : null,
    /* @__PURE__ */ i.jsxs("dl", { className: "wf-runtime-meta", children: [
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Dispatch" }),
        /* @__PURE__ */ i.jsx("dd", { title: e.commandDispatchStatus ?? e.status, children: e.commandDispatchStatus ?? e.status })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Test Run" }),
        /* @__PURE__ */ i.jsx("dd", { title: e.testRunId, children: e.testRunId })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Artifact" }),
        /* @__PURE__ */ i.jsx("dd", { title: e.artifactId ?? "None", children: e.artifactId ?? "None" })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Run / Instance" }),
        /* @__PURE__ */ i.jsx("dd", { title: o ?? "None", children: o ? /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => t(o), children: o }) : "None" })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Activities" }),
        /* @__PURE__ */ i.jsx("dd", { children: Lc(e.activityCount, "activity") })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ i.jsx("dd", { children: Lc(e.incidentCount, "incident") })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ i.jsx("dd", { title: e.expiresAt ? rt(e.expiresAt) : "None", children: e.expiresAt ? rt(e.expiresAt) : "None" })
      ] })
    ] })
  ] }) });
}
function Lc(e, t) {
  return typeof e != "number" ? "Available on linked Run" : `${e} ${t}${e === 1 ? "" : "s"}`;
}
function Rc(e) {
  return `${Ae(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function Vc(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function $1(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function Rd(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function P1(e) {
  const t = Rd(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function z1(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function Xe(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function L1(e) {
  return Hd(Xe(e));
}
function Vd(e, t, n = []) {
  if (!e) return n;
  const o = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: o?.activityTypeKey ?? e.activityVersionId,
    displayName: o ? Ae(o) : void 0
  });
  for (const r of Ve(e))
    for (const s of r.activities) Vd(s, t, n);
  return n;
}
function Od(e, t = []) {
  if (!e) return t;
  for (const n of Yu(e))
    t.push({ source: n.source, target: n.target, sourcePort: n.sourceHandle ?? void 0, targetPort: n.targetHandle ?? void 0 });
  for (const n of Ve(e))
    for (const o of n.activities) Od(o, t);
  return t;
}
function Xn(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function R1(e) {
  return `${e.id}-${Hd(JSON.stringify(e.state))}`;
}
function Hd(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function Ms(e) {
  return e.status.toLowerCase() === "rejected";
}
function V1(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function O1(e, t) {
  const n = e instanceof Error ? e.message : String(e);
  return H1(e, n) ? `Run ${t} was not found.` : n;
}
function H1(e, t) {
  if ((typeof e == "object" && e ? e.response?.status ?? e.status : void 0) === 404 || /\b404\b/.test(t)) return !0;
  try {
    const o = JSON.parse(t);
    return [o.error, o.title, o.detail].some((r) => typeof r == "string" && /not found/i.test(r));
  } catch {
    return /not found/i.test(t);
  }
}
export {
  P1 as isConnectEndOverExistingWorkflowNode,
  F1 as register,
  z1 as resolveConnectEndSource
};
