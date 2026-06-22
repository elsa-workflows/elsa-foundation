import rt, { memo as me, forwardRef as Rn, useRef as se, useEffect as ae, useCallback as fe, useContext as Kt, useMemo as pe, useState as ne, createContext as Xo, useLayoutEffect as yc, createElement as Io } from "react";
function xc(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var uo = { exports: {} }, Pt = {};
var Er;
function wc() {
  if (Er) return Pt;
  Er = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function n(o, r, i) {
    var s = null;
    if (i !== void 0 && (s = "" + i), r.key !== void 0 && (s = "" + r.key), "key" in r) {
      i = {};
      for (var a in r)
        a !== "key" && (i[a] = r[a]);
    } else i = r;
    return r = i.ref, {
      $$typeof: e,
      type: o,
      key: s,
      ref: r !== void 0 ? r : null,
      props: i
    };
  }
  return Pt.Fragment = t, Pt.jsx = n, Pt.jsxs = n, Pt;
}
var _r;
function vc() {
  return _r || (_r = 1, uo.exports = wc()), uo.exports;
}
var h = vc();
function ye(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, o; n < e.length; n++)
      (o = ye(e[n])) !== "" && (t += (t && " ") + o);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var bc = { value: () => {
} };
function zn() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new mn(n);
}
function mn(e) {
  this._ = e;
}
function Sc(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
mn.prototype = zn.prototype = {
  constructor: mn,
  on: function(e, t) {
    var n = this._, o = Sc(e + "", n), r, i = -1, s = o.length;
    if (arguments.length < 2) {
      for (; ++i < s; ) if ((r = (e = o[i]).type) && (r = Ec(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++i < s; )
      if (r = (e = o[i]).type) n[r] = Nr(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = Nr(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new mn(e);
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
function Ec(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function Nr(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = bc, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var Mo = "http://www.w3.org/1999/xhtml";
const Cr = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: Mo,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function Ln(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Cr.hasOwnProperty(t) ? { space: Cr[t], local: e } : e;
}
function _c(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === Mo && t.documentElement.namespaceURI === Mo ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Nc(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function Oi(e) {
  var t = Ln(e);
  return (t.local ? Nc : _c)(t);
}
function Cc() {
}
function Wo(e) {
  return e == null ? Cc : function() {
    return this.querySelector(e);
  };
}
function kc(e) {
  typeof e != "function" && (e = Wo(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = new Array(s), l, c, d = 0; d < s; ++d)
      (l = i[d]) && (c = e.call(l, l.__data__, d, i)) && ("__data__" in l && (c.__data__ = l.__data__), a[d] = c);
  return new Me(o, this._parents);
}
function Ic(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function Mc() {
  return [];
}
function Bi(e) {
  return e == null ? Mc : function() {
    return this.querySelectorAll(e);
  };
}
function Ac(e) {
  return function() {
    return Ic(e.apply(this, arguments));
  };
}
function Dc(e) {
  typeof e == "function" ? e = Ac(e) : e = Bi(e);
  for (var t = this._groups, n = t.length, o = [], r = [], i = 0; i < n; ++i)
    for (var s = t[i], a = s.length, l, c = 0; c < a; ++c)
      (l = s[c]) && (o.push(e.call(l, l.__data__, c, s)), r.push(l));
  return new Me(o, r);
}
function Fi(e) {
  return function() {
    return this.matches(e);
  };
}
function Yi(e) {
  return function(t) {
    return t.matches(e);
  };
}
var Pc = Array.prototype.find;
function jc(e) {
  return function() {
    return Pc.call(this.children, e);
  };
}
function $c() {
  return this.firstElementChild;
}
function Tc(e) {
  return this.select(e == null ? $c : jc(typeof e == "function" ? e : Yi(e)));
}
var Rc = Array.prototype.filter;
function zc() {
  return Array.from(this.children);
}
function Lc(e) {
  return function() {
    return Rc.call(this.children, e);
  };
}
function Hc(e) {
  return this.selectAll(e == null ? zc : Lc(typeof e == "function" ? e : Yi(e)));
}
function Vc(e) {
  typeof e != "function" && (e = Fi(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = [], l, c = 0; c < s; ++c)
      (l = i[c]) && e.call(l, l.__data__, c, i) && a.push(l);
  return new Me(o, this._parents);
}
function Xi(e) {
  return new Array(e.length);
}
function Oc() {
  return new Me(this._enter || this._groups.map(Xi), this._parents);
}
function En(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
En.prototype = {
  constructor: En,
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
function Bc(e) {
  return function() {
    return e;
  };
}
function Fc(e, t, n, o, r, i) {
  for (var s = 0, a, l = t.length, c = i.length; s < c; ++s)
    (a = t[s]) ? (a.__data__ = i[s], o[s] = a) : n[s] = new En(e, i[s]);
  for (; s < l; ++s)
    (a = t[s]) && (r[s] = a);
}
function Yc(e, t, n, o, r, i, s) {
  var a, l, c = /* @__PURE__ */ new Map(), d = t.length, u = i.length, f = new Array(d), g;
  for (a = 0; a < d; ++a)
    (l = t[a]) && (f[a] = g = s.call(l, l.__data__, a, t) + "", c.has(g) ? r[a] = l : c.set(g, l));
  for (a = 0; a < u; ++a)
    g = s.call(e, i[a], a, i) + "", (l = c.get(g)) ? (o[a] = l, l.__data__ = i[a], c.delete(g)) : n[a] = new En(e, i[a]);
  for (a = 0; a < d; ++a)
    (l = t[a]) && c.get(f[a]) === l && (r[a] = l);
}
function Xc(e) {
  return e.__data__;
}
function Wc(e, t) {
  if (!arguments.length) return Array.from(this, Xc);
  var n = t ? Yc : Fc, o = this._parents, r = this._groups;
  typeof e != "function" && (e = Bc(e));
  for (var i = r.length, s = new Array(i), a = new Array(i), l = new Array(i), c = 0; c < i; ++c) {
    var d = o[c], u = r[c], f = u.length, g = qc(e.call(d, d && d.__data__, c, o)), m = g.length, v = a[c] = new Array(m), w = s[c] = new Array(m), y = l[c] = new Array(f);
    n(d, u, v, w, y, g, t);
    for (var S = 0, p = 0, x, N; S < m; ++S)
      if (x = v[S]) {
        for (S >= p && (p = S + 1); !(N = w[p]) && ++p < m; ) ;
        x._next = N || null;
      }
  }
  return s = new Me(s, o), s._enter = a, s._exit = l, s;
}
function qc(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function Zc() {
  return new Me(this._exit || this._groups.map(Xi), this._parents);
}
function Uc(e, t, n) {
  var o = this.enter(), r = this, i = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? i.remove() : n(i), o && r ? o.merge(r).order() : r;
}
function Kc(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, i = o.length, s = Math.min(r, i), a = new Array(r), l = 0; l < s; ++l)
    for (var c = n[l], d = o[l], u = c.length, f = a[l] = new Array(u), g, m = 0; m < u; ++m)
      (g = c[m] || d[m]) && (f[m] = g);
  for (; l < r; ++l)
    a[l] = n[l];
  return new Me(a, this._parents);
}
function Gc() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, i = o[r], s; --r >= 0; )
      (s = o[r]) && (i && s.compareDocumentPosition(i) ^ 4 && i.parentNode.insertBefore(s, i), i = s);
  return this;
}
function Qc(e) {
  e || (e = Jc);
  function t(u, f) {
    return u && f ? e(u.__data__, f.__data__) : !u - !f;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), i = 0; i < o; ++i) {
    for (var s = n[i], a = s.length, l = r[i] = new Array(a), c, d = 0; d < a; ++d)
      (c = s[d]) && (l[d] = c);
    l.sort(t);
  }
  return new Me(r, this._parents).order();
}
function Jc(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function el() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function tl() {
  return Array.from(this);
}
function nl() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length; r < i; ++r) {
      var s = o[r];
      if (s) return s;
    }
  return null;
}
function ol() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function rl() {
  return !this.node();
}
function il(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], i = 0, s = r.length, a; i < s; ++i)
      (a = r[i]) && e.call(a, a.__data__, i, r);
  return this;
}
function sl(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function al(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function cl(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function ll(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function ul(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function dl(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function fl(e, t) {
  var n = Ln(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? al : sl : typeof t == "function" ? n.local ? dl : ul : n.local ? ll : cl)(n, t));
}
function Wi(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function hl(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function gl(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function pl(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function ml(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? hl : typeof t == "function" ? pl : gl)(e, t, n ?? "")) : bt(this.node(), e);
}
function bt(e, t) {
  return e.style.getPropertyValue(t) || Wi(e).getComputedStyle(e, null).getPropertyValue(t);
}
function yl(e) {
  return function() {
    delete this[e];
  };
}
function xl(e, t) {
  return function() {
    this[e] = t;
  };
}
function wl(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function vl(e, t) {
  return arguments.length > 1 ? this.each((t == null ? yl : typeof t == "function" ? wl : xl)(e, t)) : this.node()[e];
}
function qi(e) {
  return e.trim().split(/^|\s+/);
}
function qo(e) {
  return e.classList || new Zi(e);
}
function Zi(e) {
  this._node = e, this._names = qi(e.getAttribute("class") || "");
}
Zi.prototype = {
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
function Ui(e, t) {
  for (var n = qo(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function Ki(e, t) {
  for (var n = qo(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function bl(e) {
  return function() {
    Ui(this, e);
  };
}
function Sl(e) {
  return function() {
    Ki(this, e);
  };
}
function El(e, t) {
  return function() {
    (t.apply(this, arguments) ? Ui : Ki)(this, e);
  };
}
function _l(e, t) {
  var n = qi(e + "");
  if (arguments.length < 2) {
    for (var o = qo(this.node()), r = -1, i = n.length; ++r < i; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? El : t ? bl : Sl)(n, t));
}
function Nl() {
  this.textContent = "";
}
function Cl(e) {
  return function() {
    this.textContent = e;
  };
}
function kl(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function Il(e) {
  return arguments.length ? this.each(e == null ? Nl : (typeof e == "function" ? kl : Cl)(e)) : this.node().textContent;
}
function Ml() {
  this.innerHTML = "";
}
function Al(e) {
  return function() {
    this.innerHTML = e;
  };
}
function Dl(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function Pl(e) {
  return arguments.length ? this.each(e == null ? Ml : (typeof e == "function" ? Dl : Al)(e)) : this.node().innerHTML;
}
function jl() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function $l() {
  return this.each(jl);
}
function Tl() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Rl() {
  return this.each(Tl);
}
function zl(e) {
  var t = typeof e == "function" ? e : Oi(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Ll() {
  return null;
}
function Hl(e, t) {
  var n = typeof e == "function" ? e : Oi(e), o = t == null ? Ll : typeof t == "function" ? t : Wo(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function Vl() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Ol() {
  return this.each(Vl);
}
function Bl() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Fl() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Yl(e) {
  return this.select(e ? Fl : Bl);
}
function Xl(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function Wl(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function ql(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function Zl(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, i; n < r; ++n)
        i = t[n], (!e.type || i.type === e.type) && i.name === e.name ? this.removeEventListener(i.type, i.listener, i.options) : t[++o] = i;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function Ul(e, t, n) {
  return function() {
    var o = this.__on, r, i = Wl(t);
    if (o) {
      for (var s = 0, a = o.length; s < a; ++s)
        if ((r = o[s]).type === e.type && r.name === e.name) {
          this.removeEventListener(r.type, r.listener, r.options), this.addEventListener(r.type, r.listener = i, r.options = n), r.value = t;
          return;
        }
    }
    this.addEventListener(e.type, i, n), r = { type: e.type, name: e.name, value: t, listener: i, options: n }, o ? o.push(r) : this.__on = [r];
  };
}
function Kl(e, t, n) {
  var o = ql(e + ""), r, i = o.length, s;
  if (arguments.length < 2) {
    var a = this.node().__on;
    if (a) {
      for (var l = 0, c = a.length, d; l < c; ++l)
        for (r = 0, d = a[l]; r < i; ++r)
          if ((s = o[r]).type === d.type && s.name === d.name)
            return d.value;
    }
    return;
  }
  for (a = t ? Ul : Zl, r = 0; r < i; ++r) this.each(a(o[r], t, n));
  return this;
}
function Gi(e, t, n) {
  var o = Wi(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function Gl(e, t) {
  return function() {
    return Gi(this, e, t);
  };
}
function Ql(e, t) {
  return function() {
    return Gi(this, e, t.apply(this, arguments));
  };
}
function Jl(e, t) {
  return this.each((typeof t == "function" ? Ql : Gl)(e, t));
}
function* eu() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length, s; r < i; ++r)
      (s = o[r]) && (yield s);
}
var Qi = [null];
function Me(e, t) {
  this._groups = e, this._parents = t;
}
function Gt() {
  return new Me([[document.documentElement]], Qi);
}
function tu() {
  return this;
}
Me.prototype = Gt.prototype = {
  constructor: Me,
  select: kc,
  selectAll: Dc,
  selectChild: Tc,
  selectChildren: Hc,
  filter: Vc,
  data: Wc,
  enter: Oc,
  exit: Zc,
  join: Uc,
  merge: Kc,
  selection: tu,
  order: Gc,
  sort: Qc,
  call: el,
  nodes: tl,
  node: nl,
  size: ol,
  empty: rl,
  each: il,
  attr: fl,
  style: ml,
  property: vl,
  classed: _l,
  text: Il,
  html: Pl,
  raise: $l,
  lower: Rl,
  append: zl,
  insert: Hl,
  remove: Ol,
  clone: Yl,
  datum: Xl,
  on: Kl,
  dispatch: Jl,
  [Symbol.iterator]: eu
};
function ke(e) {
  return typeof e == "string" ? new Me([[document.querySelector(e)]], [document.documentElement]) : new Me([[e]], Qi);
}
function nu(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Re(e, t) {
  if (e = nu(e), t === void 0 && (t = e.currentTarget), t) {
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
const ou = { passive: !1 }, Vt = { capture: !0, passive: !1 };
function fo(e) {
  e.stopImmediatePropagation();
}
function wt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Ji(e) {
  var t = e.document.documentElement, n = ke(e).on("dragstart.drag", wt, Vt);
  "onselectstart" in t ? n.on("selectstart.drag", wt, Vt) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function es(e, t) {
  var n = e.document.documentElement, o = ke(e).on("dragstart.drag", null);
  t && (o.on("click.drag", wt, Vt), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const an = (e) => () => e;
function Ao(e, {
  sourceEvent: t,
  subject: n,
  target: o,
  identifier: r,
  active: i,
  x: s,
  y: a,
  dx: l,
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
    y: { value: a, enumerable: !0, configurable: !0 },
    dx: { value: l, enumerable: !0, configurable: !0 },
    dy: { value: c, enumerable: !0, configurable: !0 },
    _: { value: d }
  });
}
Ao.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function ru(e) {
  return !e.ctrlKey && !e.button;
}
function iu() {
  return this.parentNode;
}
function su(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function au() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function ts() {
  var e = ru, t = iu, n = su, o = au, r = {}, i = zn("start", "drag", "end"), s = 0, a, l, c, d, u = 0;
  function f(x) {
    x.on("mousedown.drag", g).filter(o).on("touchstart.drag", w).on("touchmove.drag", y, ou).on("touchend.drag touchcancel.drag", S).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function g(x, N) {
    if (!(d || !e.call(this, x, N))) {
      var E = p(this, t.call(this, x, N), x, N, "mouse");
      E && (ke(x.view).on("mousemove.drag", m, Vt).on("mouseup.drag", v, Vt), Ji(x.view), fo(x), c = !1, a = x.clientX, l = x.clientY, E("start", x));
    }
  }
  function m(x) {
    if (wt(x), !c) {
      var N = x.clientX - a, E = x.clientY - l;
      c = N * N + E * E > u;
    }
    r.mouse("drag", x);
  }
  function v(x) {
    ke(x.view).on("mousemove.drag mouseup.drag", null), es(x.view, c), wt(x), r.mouse("end", x);
  }
  function w(x, N) {
    if (e.call(this, x, N)) {
      var E = x.changedTouches, M = t.call(this, x, N), D = E.length, j, H;
      for (j = 0; j < D; ++j)
        (H = p(this, M, x, N, E[j].identifier, E[j])) && (fo(x), H("start", x, E[j]));
    }
  }
  function y(x) {
    var N = x.changedTouches, E = N.length, M, D;
    for (M = 0; M < E; ++M)
      (D = r[N[M].identifier]) && (wt(x), D("drag", x, N[M]));
  }
  function S(x) {
    var N = x.changedTouches, E = N.length, M, D;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), M = 0; M < E; ++M)
      (D = r[N[M].identifier]) && (fo(x), D("end", x, N[M]));
  }
  function p(x, N, E, M, D, j) {
    var H = i.copy(), A = Re(j || E, N), R, V, b;
    if ((b = n.call(x, new Ao("beforestart", {
      sourceEvent: E,
      target: f,
      identifier: D,
      active: s,
      x: A[0],
      y: A[1],
      dx: 0,
      dy: 0,
      dispatch: H
    }), M)) != null)
      return R = b.x - A[0] || 0, V = b.y - A[1] || 0, function C(_, I, T) {
        var $ = A, F;
        switch (_) {
          case "start":
            r[D] = C, F = s++;
            break;
          case "end":
            delete r[D], --s;
          // falls through
          case "drag":
            A = Re(T || I, N), F = s;
            break;
        }
        H.call(
          _,
          x,
          new Ao(_, {
            sourceEvent: I,
            subject: b,
            target: f,
            identifier: D,
            active: F,
            x: A[0] + R,
            y: A[1] + V,
            dx: A[0] - $[0],
            dy: A[1] - $[1],
            dispatch: H
          }),
          M
        );
      };
  }
  return f.filter = function(x) {
    return arguments.length ? (e = typeof x == "function" ? x : an(!!x), f) : e;
  }, f.container = function(x) {
    return arguments.length ? (t = typeof x == "function" ? x : an(x), f) : t;
  }, f.subject = function(x) {
    return arguments.length ? (n = typeof x == "function" ? x : an(x), f) : n;
  }, f.touchable = function(x) {
    return arguments.length ? (o = typeof x == "function" ? x : an(!!x), f) : o;
  }, f.on = function() {
    var x = i.on.apply(i, arguments);
    return x === i ? f : x;
  }, f.clickDistance = function(x) {
    return arguments.length ? (u = (x = +x) * x, f) : Math.sqrt(u);
  }, f;
}
function Zo(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function ns(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function Qt() {
}
var Ot = 0.7, _n = 1 / Ot, vt = "\\s*([+-]?\\d+)\\s*", Bt = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", Xe = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", cu = /^#([0-9a-f]{3,8})$/, lu = new RegExp(`^rgb\\(${vt},${vt},${vt}\\)$`), uu = new RegExp(`^rgb\\(${Xe},${Xe},${Xe}\\)$`), du = new RegExp(`^rgba\\(${vt},${vt},${vt},${Bt}\\)$`), fu = new RegExp(`^rgba\\(${Xe},${Xe},${Xe},${Bt}\\)$`), hu = new RegExp(`^hsl\\(${Bt},${Xe},${Xe}\\)$`), gu = new RegExp(`^hsla\\(${Bt},${Xe},${Xe},${Bt}\\)$`), kr = {
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
Zo(Qt, dt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Ir,
  // Deprecated! Use color.formatHex.
  formatHex: Ir,
  formatHex8: pu,
  formatHsl: mu,
  formatRgb: Mr,
  toString: Mr
});
function Ir() {
  return this.rgb().formatHex();
}
function pu() {
  return this.rgb().formatHex8();
}
function mu() {
  return os(this).formatHsl();
}
function Mr() {
  return this.rgb().formatRgb();
}
function dt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = cu.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? Ar(t) : n === 3 ? new Se(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? cn(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? cn(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = lu.exec(e)) ? new Se(t[1], t[2], t[3], 1) : (t = uu.exec(e)) ? new Se(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = du.exec(e)) ? cn(t[1], t[2], t[3], t[4]) : (t = fu.exec(e)) ? cn(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = hu.exec(e)) ? jr(t[1], t[2] / 100, t[3] / 100, 1) : (t = gu.exec(e)) ? jr(t[1], t[2] / 100, t[3] / 100, t[4]) : kr.hasOwnProperty(e) ? Ar(kr[e]) : e === "transparent" ? new Se(NaN, NaN, NaN, 0) : null;
}
function Ar(e) {
  return new Se(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function cn(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new Se(e, t, n, o);
}
function yu(e) {
  return e instanceof Qt || (e = dt(e)), e ? (e = e.rgb(), new Se(e.r, e.g, e.b, e.opacity)) : new Se();
}
function Do(e, t, n, o) {
  return arguments.length === 1 ? yu(e) : new Se(e, t, n, o ?? 1);
}
function Se(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
Zo(Se, Do, ns(Qt, {
  brighter(e) {
    return e = e == null ? _n : Math.pow(_n, e), new Se(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Ot : Math.pow(Ot, e), new Se(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new Se(lt(this.r), lt(this.g), lt(this.b), Nn(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: Dr,
  // Deprecated! Use color.formatHex.
  formatHex: Dr,
  formatHex8: xu,
  formatRgb: Pr,
  toString: Pr
}));
function Dr() {
  return `#${ct(this.r)}${ct(this.g)}${ct(this.b)}`;
}
function xu() {
  return `#${ct(this.r)}${ct(this.g)}${ct(this.b)}${ct((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function Pr() {
  const e = Nn(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${lt(this.r)}, ${lt(this.g)}, ${lt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Nn(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function lt(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function ct(e) {
  return e = lt(e), (e < 16 ? "0" : "") + e.toString(16);
}
function jr(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new ze(e, t, n, o);
}
function os(e) {
  if (e instanceof ze) return new ze(e.h, e.s, e.l, e.opacity);
  if (e instanceof Qt || (e = dt(e)), !e) return new ze();
  if (e instanceof ze) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), i = Math.max(t, n, o), s = NaN, a = i - r, l = (i + r) / 2;
  return a ? (t === i ? s = (n - o) / a + (n < o) * 6 : n === i ? s = (o - t) / a + 2 : s = (t - n) / a + 4, a /= l < 0.5 ? i + r : 2 - i - r, s *= 60) : a = l > 0 && l < 1 ? 0 : s, new ze(s, a, l, e.opacity);
}
function wu(e, t, n, o) {
  return arguments.length === 1 ? os(e) : new ze(e, t, n, o ?? 1);
}
function ze(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
Zo(ze, wu, ns(Qt, {
  brighter(e) {
    return e = e == null ? _n : Math.pow(_n, e), new ze(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Ot : Math.pow(Ot, e), new ze(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new Se(
      ho(e >= 240 ? e - 240 : e + 120, r, o),
      ho(e, r, o),
      ho(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new ze($r(this.h), ln(this.s), ln(this.l), Nn(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Nn(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${$r(this.h)}, ${ln(this.s) * 100}%, ${ln(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function $r(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function ln(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function ho(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const Uo = (e) => () => e;
function vu(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function bu(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function Su(e) {
  return (e = +e) == 1 ? rs : function(t, n) {
    return n - t ? bu(t, n, e) : Uo(isNaN(t) ? n : t);
  };
}
function rs(e, t) {
  var n = t - e;
  return n ? vu(e, n) : Uo(isNaN(e) ? t : e);
}
const Cn = (function e(t) {
  var n = Su(t);
  function o(r, i) {
    var s = n((r = Do(r)).r, (i = Do(i)).r), a = n(r.g, i.g), l = n(r.b, i.b), c = rs(r.opacity, i.opacity);
    return function(d) {
      return r.r = s(d), r.g = a(d), r.b = l(d), r.opacity = c(d), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function Eu(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(i) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - i) + t[r] * i;
    return o;
  };
}
function _u(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Nu(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), i = new Array(n), s;
  for (s = 0; s < o; ++s) r[s] = Lt(e[s], t[s]);
  for (; s < n; ++s) i[s] = t[s];
  return function(a) {
    for (s = 0; s < o; ++s) i[s] = r[s](a);
    return i;
  };
}
function Cu(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(o) {
    return n.setTime(e * (1 - o) + t * o), n;
  };
}
function Ye(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function ku(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = Lt(e[r], t[r]) : o[r] = t[r];
  return function(i) {
    for (r in n) o[r] = n[r](i);
    return o;
  };
}
var Po = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, go = new RegExp(Po.source, "g");
function Iu(e) {
  return function() {
    return e;
  };
}
function Mu(e) {
  return function(t) {
    return e(t) + "";
  };
}
function is(e, t) {
  var n = Po.lastIndex = go.lastIndex = 0, o, r, i, s = -1, a = [], l = [];
  for (e = e + "", t = t + ""; (o = Po.exec(e)) && (r = go.exec(t)); )
    (i = r.index) > n && (i = t.slice(n, i), a[s] ? a[s] += i : a[++s] = i), (o = o[0]) === (r = r[0]) ? a[s] ? a[s] += r : a[++s] = r : (a[++s] = null, l.push({ i: s, x: Ye(o, r) })), n = go.lastIndex;
  return n < t.length && (i = t.slice(n), a[s] ? a[s] += i : a[++s] = i), a.length < 2 ? l[0] ? Mu(l[0].x) : Iu(t) : (t = l.length, function(c) {
    for (var d = 0, u; d < t; ++d) a[(u = l[d]).i] = u.x(c);
    return a.join("");
  });
}
function Lt(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? Uo(t) : (n === "number" ? Ye : n === "string" ? (o = dt(t)) ? (t = o, Cn) : is : t instanceof dt ? Cn : t instanceof Date ? Cu : _u(t) ? Eu : Array.isArray(t) ? Nu : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? ku : Ye)(e, t);
}
var Tr = 180 / Math.PI, jo = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function ss(e, t, n, o, r, i) {
  var s, a, l;
  return (s = Math.sqrt(e * e + t * t)) && (e /= s, t /= s), (l = e * n + t * o) && (n -= e * l, o -= t * l), (a = Math.sqrt(n * n + o * o)) && (n /= a, o /= a, l /= a), e * o < t * n && (e = -e, t = -t, l = -l, s = -s), {
    translateX: r,
    translateY: i,
    rotate: Math.atan2(t, e) * Tr,
    skewX: Math.atan(l) * Tr,
    scaleX: s,
    scaleY: a
  };
}
var un;
function Au(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? jo : ss(t.a, t.b, t.c, t.d, t.e, t.f);
}
function Du(e) {
  return e == null || (un || (un = document.createElementNS("http://www.w3.org/2000/svg", "g")), un.setAttribute("transform", e), !(e = un.transform.baseVal.consolidate())) ? jo : (e = e.matrix, ss(e.a, e.b, e.c, e.d, e.e, e.f));
}
function as(e, t, n, o) {
  function r(c) {
    return c.length ? c.pop() + " " : "";
  }
  function i(c, d, u, f, g, m) {
    if (c !== u || d !== f) {
      var v = g.push("translate(", null, t, null, n);
      m.push({ i: v - 4, x: Ye(c, u) }, { i: v - 2, x: Ye(d, f) });
    } else (u || f) && g.push("translate(" + u + t + f + n);
  }
  function s(c, d, u, f) {
    c !== d ? (c - d > 180 ? d += 360 : d - c > 180 && (c += 360), f.push({ i: u.push(r(u) + "rotate(", null, o) - 2, x: Ye(c, d) })) : d && u.push(r(u) + "rotate(" + d + o);
  }
  function a(c, d, u, f) {
    c !== d ? f.push({ i: u.push(r(u) + "skewX(", null, o) - 2, x: Ye(c, d) }) : d && u.push(r(u) + "skewX(" + d + o);
  }
  function l(c, d, u, f, g, m) {
    if (c !== u || d !== f) {
      var v = g.push(r(g) + "scale(", null, ",", null, ")");
      m.push({ i: v - 4, x: Ye(c, u) }, { i: v - 2, x: Ye(d, f) });
    } else (u !== 1 || f !== 1) && g.push(r(g) + "scale(" + u + "," + f + ")");
  }
  return function(c, d) {
    var u = [], f = [];
    return c = e(c), d = e(d), i(c.translateX, c.translateY, d.translateX, d.translateY, u, f), s(c.rotate, d.rotate, u, f), a(c.skewX, d.skewX, u, f), l(c.scaleX, c.scaleY, d.scaleX, d.scaleY, u, f), c = d = null, function(g) {
      for (var m = -1, v = f.length, w; ++m < v; ) u[(w = f[m]).i] = w.x(g);
      return u.join("");
    };
  };
}
var Pu = as(Au, "px, ", "px)", "deg)"), ju = as(Du, ", ", ")", ")"), $u = 1e-12;
function Rr(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Tu(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Ru(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const yn = (function e(t, n, o) {
  function r(i, s) {
    var a = i[0], l = i[1], c = i[2], d = s[0], u = s[1], f = s[2], g = d - a, m = u - l, v = g * g + m * m, w, y;
    if (v < $u)
      y = Math.log(f / c) / t, w = function(M) {
        return [
          a + M * g,
          l + M * m,
          c * Math.exp(t * M * y)
        ];
      };
    else {
      var S = Math.sqrt(v), p = (f * f - c * c + o * v) / (2 * c * n * S), x = (f * f - c * c - o * v) / (2 * f * n * S), N = Math.log(Math.sqrt(p * p + 1) - p), E = Math.log(Math.sqrt(x * x + 1) - x);
      y = (E - N) / t, w = function(M) {
        var D = M * y, j = Rr(N), H = c / (n * S) * (j * Ru(t * D + N) - Tu(N));
        return [
          a + H * g,
          l + H * m,
          c * j / Rr(t * D + N)
        ];
      };
    }
    return w.duration = y * 1e3 * t / Math.SQRT2, w;
  }
  return r.rho = function(i) {
    var s = Math.max(1e-3, +i), a = s * s, l = a * a;
    return e(s, a, l);
  }, r;
})(Math.SQRT2, 2, 4);
var St = 0, Rt = 0, jt = 0, cs = 1e3, kn, zt, In = 0, ft = 0, Hn = 0, Ft = typeof performance == "object" && performance.now ? performance : Date, ls = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function Ko() {
  return ft || (ls(zu), ft = Ft.now() + Hn);
}
function zu() {
  ft = 0;
}
function Mn() {
  this._call = this._time = this._next = null;
}
Mn.prototype = us.prototype = {
  constructor: Mn,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? Ko() : +n) + (t == null ? 0 : +t), !this._next && zt !== this && (zt ? zt._next = this : kn = this, zt = this), this._call = e, this._time = n, $o();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, $o());
  }
};
function us(e, t, n) {
  var o = new Mn();
  return o.restart(e, t, n), o;
}
function Lu() {
  Ko(), ++St;
  for (var e = kn, t; e; )
    (t = ft - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --St;
}
function zr() {
  ft = (In = Ft.now()) + Hn, St = Rt = 0;
  try {
    Lu();
  } finally {
    St = 0, Vu(), ft = 0;
  }
}
function Hu() {
  var e = Ft.now(), t = e - In;
  t > cs && (Hn -= t, In = e);
}
function Vu() {
  for (var e, t = kn, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : kn = n);
  zt = e, $o(o);
}
function $o(e) {
  if (!St) {
    Rt && (Rt = clearTimeout(Rt));
    var t = e - ft;
    t > 24 ? (e < 1 / 0 && (Rt = setTimeout(zr, e - Ft.now() - Hn)), jt && (jt = clearInterval(jt))) : (jt || (In = Ft.now(), jt = setInterval(Hu, cs)), St = 1, ls(zr));
  }
}
function Lr(e, t, n) {
  var o = new Mn();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var Ou = zn("start", "end", "cancel", "interrupt"), Bu = [], ds = 0, Hr = 1, To = 2, xn = 3, Vr = 4, Ro = 5, wn = 6;
function Vn(e, t, n, o, r, i) {
  var s = e.__transition;
  if (!s) e.__transition = {};
  else if (n in s) return;
  Fu(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: Ou,
    tween: Bu,
    time: i.time,
    delay: i.delay,
    duration: i.duration,
    ease: i.ease,
    timer: null,
    state: ds
  });
}
function Go(e, t) {
  var n = Ve(e, t);
  if (n.state > ds) throw new Error("too late; already scheduled");
  return n;
}
function qe(e, t) {
  var n = Ve(e, t);
  if (n.state > xn) throw new Error("too late; already running");
  return n;
}
function Ve(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function Fu(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = us(i, 0, n.time);
  function i(c) {
    n.state = Hr, n.timer.restart(s, n.delay, n.time), n.delay <= c && s(c - n.delay);
  }
  function s(c) {
    var d, u, f, g;
    if (n.state !== Hr) return l();
    for (d in o)
      if (g = o[d], g.name === n.name) {
        if (g.state === xn) return Lr(s);
        g.state === Vr ? (g.state = wn, g.timer.stop(), g.on.call("interrupt", e, e.__data__, g.index, g.group), delete o[d]) : +d < t && (g.state = wn, g.timer.stop(), g.on.call("cancel", e, e.__data__, g.index, g.group), delete o[d]);
      }
    if (Lr(function() {
      n.state === xn && (n.state = Vr, n.timer.restart(a, n.delay, n.time), a(c));
    }), n.state = To, n.on.call("start", e, e.__data__, n.index, n.group), n.state === To) {
      for (n.state = xn, r = new Array(f = n.tween.length), d = 0, u = -1; d < f; ++d)
        (g = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (r[++u] = g);
      r.length = u + 1;
    }
  }
  function a(c) {
    for (var d = c < n.duration ? n.ease.call(null, c / n.duration) : (n.timer.restart(l), n.state = Ro, 1), u = -1, f = r.length; ++u < f; )
      r[u].call(e, d);
    n.state === Ro && (n.on.call("end", e, e.__data__, n.index, n.group), l());
  }
  function l() {
    n.state = wn, n.timer.stop(), delete o[t];
    for (var c in o) return;
    delete e.__transition;
  }
}
function vn(e, t) {
  var n = e.__transition, o, r, i = !0, s;
  if (n) {
    t = t == null ? null : t + "";
    for (s in n) {
      if ((o = n[s]).name !== t) {
        i = !1;
        continue;
      }
      r = o.state > To && o.state < Ro, o.state = wn, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[s];
    }
    i && delete e.__transition;
  }
}
function Yu(e) {
  return this.each(function() {
    vn(this, e);
  });
}
function Xu(e, t) {
  var n, o;
  return function() {
    var r = qe(this, e), i = r.tween;
    if (i !== n) {
      o = n = i;
      for (var s = 0, a = o.length; s < a; ++s)
        if (o[s].name === t) {
          o = o.slice(), o.splice(s, 1);
          break;
        }
    }
    r.tween = o;
  };
}
function Wu(e, t, n) {
  var o, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var i = qe(this, e), s = i.tween;
    if (s !== o) {
      r = (o = s).slice();
      for (var a = { name: t, value: n }, l = 0, c = r.length; l < c; ++l)
        if (r[l].name === t) {
          r[l] = a;
          break;
        }
      l === c && r.push(a);
    }
    i.tween = r;
  };
}
function qu(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = Ve(this.node(), n).tween, r = 0, i = o.length, s; r < i; ++r)
      if ((s = o[r]).name === e)
        return s.value;
    return null;
  }
  return this.each((t == null ? Xu : Wu)(n, e, t));
}
function Qo(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = qe(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return Ve(r, o).value[t];
  };
}
function fs(e, t) {
  var n;
  return (typeof t == "number" ? Ye : t instanceof dt ? Cn : (n = dt(t)) ? (t = n, Cn) : is)(e, t);
}
function Zu(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Uu(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Ku(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttribute(e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Gu(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttributeNS(e.space, e.local);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Qu(e, t, n) {
  var o, r, i;
  return function() {
    var s, a = n(this), l;
    return a == null ? void this.removeAttribute(e) : (s = this.getAttribute(e), l = a + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a)));
  };
}
function Ju(e, t, n) {
  var o, r, i;
  return function() {
    var s, a = n(this), l;
    return a == null ? void this.removeAttributeNS(e.space, e.local) : (s = this.getAttributeNS(e.space, e.local), l = a + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a)));
  };
}
function ed(e, t) {
  var n = Ln(e), o = n === "transform" ? ju : fs;
  return this.attrTween(e, typeof t == "function" ? (n.local ? Ju : Qu)(n, o, Qo(this, "attr." + e, t)) : t == null ? (n.local ? Uu : Zu)(n) : (n.local ? Gu : Ku)(n, o, t));
}
function td(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function nd(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function od(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && nd(e, i)), n;
  }
  return r._value = t, r;
}
function rd(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && td(e, i)), n;
  }
  return r._value = t, r;
}
function id(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = Ln(e);
  return this.tween(n, (o.local ? od : rd)(o, t));
}
function sd(e, t) {
  return function() {
    Go(this, e).delay = +t.apply(this, arguments);
  };
}
function ad(e, t) {
  return t = +t, function() {
    Go(this, e).delay = t;
  };
}
function cd(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? sd : ad)(t, e)) : Ve(this.node(), t).delay;
}
function ld(e, t) {
  return function() {
    qe(this, e).duration = +t.apply(this, arguments);
  };
}
function ud(e, t) {
  return t = +t, function() {
    qe(this, e).duration = t;
  };
}
function dd(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? ld : ud)(t, e)) : Ve(this.node(), t).duration;
}
function fd(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    qe(this, e).ease = t;
  };
}
function hd(e) {
  var t = this._id;
  return arguments.length ? this.each(fd(t, e)) : Ve(this.node(), t).ease;
}
function gd(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    qe(this, e).ease = n;
  };
}
function pd(e) {
  if (typeof e != "function") throw new Error();
  return this.each(gd(this._id, e));
}
function md(e) {
  typeof e != "function" && (e = Fi(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = [], l, c = 0; c < s; ++c)
      (l = i[c]) && e.call(l, l.__data__, c, i) && a.push(l);
  return new Ge(o, this._parents, this._name, this._id);
}
function yd(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, i = Math.min(o, r), s = new Array(o), a = 0; a < i; ++a)
    for (var l = t[a], c = n[a], d = l.length, u = s[a] = new Array(d), f, g = 0; g < d; ++g)
      (f = l[g] || c[g]) && (u[g] = f);
  for (; a < o; ++a)
    s[a] = t[a];
  return new Ge(s, this._parents, this._name, this._id);
}
function xd(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function wd(e, t, n) {
  var o, r, i = xd(t) ? Go : qe;
  return function() {
    var s = i(this, e), a = s.on;
    a !== o && (r = (o = a).copy()).on(t, n), s.on = r;
  };
}
function vd(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Ve(this.node(), n).on.on(e) : this.each(wd(n, e, t));
}
function bd(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function Sd() {
  return this.on("end.remove", bd(this._id));
}
function Ed(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Wo(e));
  for (var o = this._groups, r = o.length, i = new Array(r), s = 0; s < r; ++s)
    for (var a = o[s], l = a.length, c = i[s] = new Array(l), d, u, f = 0; f < l; ++f)
      (d = a[f]) && (u = e.call(d, d.__data__, f, a)) && ("__data__" in d && (u.__data__ = d.__data__), c[f] = u, Vn(c[f], t, n, f, c, Ve(d, n)));
  return new Ge(i, this._parents, t, n);
}
function _d(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Bi(e));
  for (var o = this._groups, r = o.length, i = [], s = [], a = 0; a < r; ++a)
    for (var l = o[a], c = l.length, d, u = 0; u < c; ++u)
      if (d = l[u]) {
        for (var f = e.call(d, d.__data__, u, l), g, m = Ve(d, n), v = 0, w = f.length; v < w; ++v)
          (g = f[v]) && Vn(g, t, n, v, f, m);
        i.push(f), s.push(d);
      }
  return new Ge(i, s, t, n);
}
var Nd = Gt.prototype.constructor;
function Cd() {
  return new Nd(this._groups, this._parents);
}
function kd(e, t) {
  var n, o, r;
  return function() {
    var i = bt(this, e), s = (this.style.removeProperty(e), bt(this, e));
    return i === s ? null : i === n && s === o ? r : r = t(n = i, o = s);
  };
}
function hs(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Id(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = bt(this, e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Md(e, t, n) {
  var o, r, i;
  return function() {
    var s = bt(this, e), a = n(this), l = a + "";
    return a == null && (l = a = (this.style.removeProperty(e), bt(this, e))), s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a));
  };
}
function Ad(e, t) {
  var n, o, r, i = "style." + t, s = "end." + i, a;
  return function() {
    var l = qe(this, e), c = l.on, d = l.value[i] == null ? a || (a = hs(t)) : void 0;
    (c !== n || r !== d) && (o = (n = c).copy()).on(s, r = d), l.on = o;
  };
}
function Dd(e, t, n) {
  var o = (e += "") == "transform" ? Pu : fs;
  return t == null ? this.styleTween(e, kd(e, o)).on("end.style." + e, hs(e)) : typeof t == "function" ? this.styleTween(e, Md(e, o, Qo(this, "style." + e, t))).each(Ad(this._id, e)) : this.styleTween(e, Id(e, o, t), n).on("end.style." + e, null);
}
function Pd(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function jd(e, t, n) {
  var o, r;
  function i() {
    var s = t.apply(this, arguments);
    return s !== r && (o = (r = s) && Pd(e, s, n)), o;
  }
  return i._value = t, i;
}
function $d(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, jd(e, t, n ?? ""));
}
function Td(e) {
  return function() {
    this.textContent = e;
  };
}
function Rd(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function zd(e) {
  return this.tween("text", typeof e == "function" ? Rd(Qo(this, "text", e)) : Td(e == null ? "" : e + ""));
}
function Ld(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Hd(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Ld(r)), t;
  }
  return o._value = e, o;
}
function Vd(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, Hd(e));
}
function Od() {
  for (var e = this._name, t = this._id, n = gs(), o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], a = s.length, l, c = 0; c < a; ++c)
      if (l = s[c]) {
        var d = Ve(l, t);
        Vn(l, e, n, c, s, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new Ge(o, this._parents, e, n);
}
function Bd() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(i, s) {
    var a = { value: s }, l = { value: function() {
      --r === 0 && i();
    } };
    n.each(function() {
      var c = qe(this, o), d = c.on;
      d !== e && (t = (e = d).copy(), t._.cancel.push(a), t._.interrupt.push(a), t._.end.push(l)), c.on = t;
    }), r === 0 && i();
  });
}
var Fd = 0;
function Ge(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function gs() {
  return ++Fd;
}
var Ue = Gt.prototype;
Ge.prototype = {
  constructor: Ge,
  select: Ed,
  selectAll: _d,
  selectChild: Ue.selectChild,
  selectChildren: Ue.selectChildren,
  filter: md,
  merge: yd,
  selection: Cd,
  transition: Od,
  call: Ue.call,
  nodes: Ue.nodes,
  node: Ue.node,
  size: Ue.size,
  empty: Ue.empty,
  each: Ue.each,
  on: vd,
  attr: ed,
  attrTween: id,
  style: Dd,
  styleTween: $d,
  text: zd,
  textTween: Vd,
  remove: Sd,
  tween: qu,
  delay: cd,
  duration: dd,
  ease: hd,
  easeVarying: pd,
  end: Bd,
  [Symbol.iterator]: Ue[Symbol.iterator]
};
function Yd(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var Xd = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: Yd
};
function Wd(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function qd(e) {
  var t, n;
  e instanceof Ge ? (t = e._id, e = e._name) : (t = gs(), (n = Xd).time = Ko(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], a = s.length, l, c = 0; c < a; ++c)
      (l = s[c]) && Vn(l, e, t, c, s, n || Wd(l, t));
  return new Ge(o, this._parents, e, t);
}
Gt.prototype.interrupt = Yu;
Gt.prototype.transition = qd;
const dn = (e) => () => e;
function Zd(e, {
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
function Ke(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
Ke.prototype = {
  constructor: Ke,
  scale: function(e) {
    return e === 1 ? this : new Ke(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new Ke(this.k, this.x + this.k * e, this.y + this.k * t);
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
var On = new Ke(1, 0, 0);
ps.prototype = Ke.prototype;
function ps(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return On;
  return e.__zoom;
}
function po(e) {
  e.stopImmediatePropagation();
}
function $t(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Ud(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function Kd() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function Or() {
  return this.__zoom || On;
}
function Gd(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function Qd() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Jd(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], i = e.invertY(t[0][1]) - n[0][1], s = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    s > i ? (i + s) / 2 : Math.min(0, i) || Math.max(0, s)
  );
}
function ms() {
  var e = Ud, t = Kd, n = Jd, o = Gd, r = Qd, i = [0, 1 / 0], s = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], a = 250, l = yn, c = zn("start", "zoom", "end"), d, u, f, g = 500, m = 150, v = 0, w = 10;
  function y(b) {
    b.property("__zoom", Or).on("wheel.zoom", D, { passive: !1 }).on("mousedown.zoom", j).on("dblclick.zoom", H).filter(r).on("touchstart.zoom", A).on("touchmove.zoom", R).on("touchend.zoom touchcancel.zoom", V).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  y.transform = function(b, C, _, I) {
    var T = b.selection ? b.selection() : b;
    T.property("__zoom", Or), b !== T ? N(b, C, _, I) : T.interrupt().each(function() {
      E(this, arguments).event(I).start().zoom(null, typeof C == "function" ? C.apply(this, arguments) : C).end();
    });
  }, y.scaleBy = function(b, C, _, I) {
    y.scaleTo(b, function() {
      var T = this.__zoom.k, $ = typeof C == "function" ? C.apply(this, arguments) : C;
      return T * $;
    }, _, I);
  }, y.scaleTo = function(b, C, _, I) {
    y.transform(b, function() {
      var T = t.apply(this, arguments), $ = this.__zoom, F = _ == null ? x(T) : typeof _ == "function" ? _.apply(this, arguments) : _, B = $.invert(F), O = typeof C == "function" ? C.apply(this, arguments) : C;
      return n(p(S($, O), F, B), T, s);
    }, _, I);
  }, y.translateBy = function(b, C, _, I) {
    y.transform(b, function() {
      return n(this.__zoom.translate(
        typeof C == "function" ? C.apply(this, arguments) : C,
        typeof _ == "function" ? _.apply(this, arguments) : _
      ), t.apply(this, arguments), s);
    }, null, I);
  }, y.translateTo = function(b, C, _, I, T) {
    y.transform(b, function() {
      var $ = t.apply(this, arguments), F = this.__zoom, B = I == null ? x($) : typeof I == "function" ? I.apply(this, arguments) : I;
      return n(On.translate(B[0], B[1]).scale(F.k).translate(
        typeof C == "function" ? -C.apply(this, arguments) : -C,
        typeof _ == "function" ? -_.apply(this, arguments) : -_
      ), $, s);
    }, I, T);
  };
  function S(b, C) {
    return C = Math.max(i[0], Math.min(i[1], C)), C === b.k ? b : new Ke(C, b.x, b.y);
  }
  function p(b, C, _) {
    var I = C[0] - _[0] * b.k, T = C[1] - _[1] * b.k;
    return I === b.x && T === b.y ? b : new Ke(b.k, I, T);
  }
  function x(b) {
    return [(+b[0][0] + +b[1][0]) / 2, (+b[0][1] + +b[1][1]) / 2];
  }
  function N(b, C, _, I) {
    b.on("start.zoom", function() {
      E(this, arguments).event(I).start();
    }).on("interrupt.zoom end.zoom", function() {
      E(this, arguments).event(I).end();
    }).tween("zoom", function() {
      var T = this, $ = arguments, F = E(T, $).event(I), B = t.apply(T, $), O = _ == null ? x(B) : typeof _ == "function" ? _.apply(T, $) : _, K = Math.max(B[1][0] - B[0][0], B[1][1] - B[0][1]), W = T.__zoom, G = typeof C == "function" ? C.apply(T, $) : C, oe = l(W.invert(O).concat(K / W.k), G.invert(O).concat(K / G.k));
      return function(q) {
        if (q === 1) q = G;
        else {
          var L = oe(q), Z = K / L[2];
          q = new Ke(Z, O[0] - L[0] * Z, O[1] - L[1] * Z);
        }
        F.zoom(null, q);
      };
    });
  }
  function E(b, C, _) {
    return !_ && b.__zooming || new M(b, C);
  }
  function M(b, C) {
    this.that = b, this.args = C, this.active = 0, this.sourceEvent = null, this.extent = t.apply(b, C), this.taps = 0;
  }
  M.prototype = {
    event: function(b) {
      return b && (this.sourceEvent = b), this;
    },
    start: function() {
      return ++this.active === 1 && (this.that.__zooming = this, this.emit("start")), this;
    },
    zoom: function(b, C) {
      return this.mouse && b !== "mouse" && (this.mouse[1] = C.invert(this.mouse[0])), this.touch0 && b !== "touch" && (this.touch0[1] = C.invert(this.touch0[0])), this.touch1 && b !== "touch" && (this.touch1[1] = C.invert(this.touch1[0])), this.that.__zoom = C, this.emit("zoom"), this;
    },
    end: function() {
      return --this.active === 0 && (delete this.that.__zooming, this.emit("end")), this;
    },
    emit: function(b) {
      var C = ke(this.that).datum();
      c.call(
        b,
        this.that,
        new Zd(b, {
          sourceEvent: this.sourceEvent,
          target: y,
          transform: this.that.__zoom,
          dispatch: c
        }),
        C
      );
    }
  };
  function D(b, ...C) {
    if (!e.apply(this, arguments)) return;
    var _ = E(this, C).event(b), I = this.__zoom, T = Math.max(i[0], Math.min(i[1], I.k * Math.pow(2, o.apply(this, arguments)))), $ = Re(b);
    if (_.wheel)
      (_.mouse[0][0] !== $[0] || _.mouse[0][1] !== $[1]) && (_.mouse[1] = I.invert(_.mouse[0] = $)), clearTimeout(_.wheel);
    else {
      if (I.k === T) return;
      _.mouse = [$, I.invert($)], vn(this), _.start();
    }
    $t(b), _.wheel = setTimeout(F, m), _.zoom("mouse", n(p(S(I, T), _.mouse[0], _.mouse[1]), _.extent, s));
    function F() {
      _.wheel = null, _.end();
    }
  }
  function j(b, ...C) {
    if (f || !e.apply(this, arguments)) return;
    var _ = b.currentTarget, I = E(this, C, !0).event(b), T = ke(b.view).on("mousemove.zoom", O, !0).on("mouseup.zoom", K, !0), $ = Re(b, _), F = b.clientX, B = b.clientY;
    Ji(b.view), po(b), I.mouse = [$, this.__zoom.invert($)], vn(this), I.start();
    function O(W) {
      if ($t(W), !I.moved) {
        var G = W.clientX - F, oe = W.clientY - B;
        I.moved = G * G + oe * oe > v;
      }
      I.event(W).zoom("mouse", n(p(I.that.__zoom, I.mouse[0] = Re(W, _), I.mouse[1]), I.extent, s));
    }
    function K(W) {
      T.on("mousemove.zoom mouseup.zoom", null), es(W.view, I.moved), $t(W), I.event(W).end();
    }
  }
  function H(b, ...C) {
    if (e.apply(this, arguments)) {
      var _ = this.__zoom, I = Re(b.changedTouches ? b.changedTouches[0] : b, this), T = _.invert(I), $ = _.k * (b.shiftKey ? 0.5 : 2), F = n(p(S(_, $), I, T), t.apply(this, C), s);
      $t(b), a > 0 ? ke(this).transition().duration(a).call(N, F, I, b) : ke(this).call(y.transform, F, I, b);
    }
  }
  function A(b, ...C) {
    if (e.apply(this, arguments)) {
      var _ = b.touches, I = _.length, T = E(this, C, b.changedTouches.length === I).event(b), $, F, B, O;
      for (po(b), F = 0; F < I; ++F)
        B = _[F], O = Re(B, this), O = [O, this.__zoom.invert(O), B.identifier], T.touch0 ? !T.touch1 && T.touch0[2] !== O[2] && (T.touch1 = O, T.taps = 0) : (T.touch0 = O, $ = !0, T.taps = 1 + !!d);
      d && (d = clearTimeout(d)), $ && (T.taps < 2 && (u = O[0], d = setTimeout(function() {
        d = null;
      }, g)), vn(this), T.start());
    }
  }
  function R(b, ...C) {
    if (this.__zooming) {
      var _ = E(this, C).event(b), I = b.changedTouches, T = I.length, $, F, B, O;
      for ($t(b), $ = 0; $ < T; ++$)
        F = I[$], B = Re(F, this), _.touch0 && _.touch0[2] === F.identifier ? _.touch0[0] = B : _.touch1 && _.touch1[2] === F.identifier && (_.touch1[0] = B);
      if (F = _.that.__zoom, _.touch1) {
        var K = _.touch0[0], W = _.touch0[1], G = _.touch1[0], oe = _.touch1[1], q = (q = G[0] - K[0]) * q + (q = G[1] - K[1]) * q, L = (L = oe[0] - W[0]) * L + (L = oe[1] - W[1]) * L;
        F = S(F, Math.sqrt(q / L)), B = [(K[0] + G[0]) / 2, (K[1] + G[1]) / 2], O = [(W[0] + oe[0]) / 2, (W[1] + oe[1]) / 2];
      } else if (_.touch0) B = _.touch0[0], O = _.touch0[1];
      else return;
      _.zoom("touch", n(p(F, B, O), _.extent, s));
    }
  }
  function V(b, ...C) {
    if (this.__zooming) {
      var _ = E(this, C).event(b), I = b.changedTouches, T = I.length, $, F;
      for (po(b), f && clearTimeout(f), f = setTimeout(function() {
        f = null;
      }, g), $ = 0; $ < T; ++$)
        F = I[$], _.touch0 && _.touch0[2] === F.identifier ? delete _.touch0 : _.touch1 && _.touch1[2] === F.identifier && delete _.touch1;
      if (_.touch1 && !_.touch0 && (_.touch0 = _.touch1, delete _.touch1), _.touch0) _.touch0[1] = this.__zoom.invert(_.touch0[0]);
      else if (_.end(), _.taps === 2 && (F = Re(F, this), Math.hypot(u[0] - F[0], u[1] - F[1]) < w)) {
        var B = ke(this).on("dblclick.zoom");
        B && B.apply(this, arguments);
      }
    }
  }
  return y.wheelDelta = function(b) {
    return arguments.length ? (o = typeof b == "function" ? b : dn(+b), y) : o;
  }, y.filter = function(b) {
    return arguments.length ? (e = typeof b == "function" ? b : dn(!!b), y) : e;
  }, y.touchable = function(b) {
    return arguments.length ? (r = typeof b == "function" ? b : dn(!!b), y) : r;
  }, y.extent = function(b) {
    return arguments.length ? (t = typeof b == "function" ? b : dn([[+b[0][0], +b[0][1]], [+b[1][0], +b[1][1]]]), y) : t;
  }, y.scaleExtent = function(b) {
    return arguments.length ? (i[0] = +b[0], i[1] = +b[1], y) : [i[0], i[1]];
  }, y.translateExtent = function(b) {
    return arguments.length ? (s[0][0] = +b[0][0], s[1][0] = +b[1][0], s[0][1] = +b[0][1], s[1][1] = +b[1][1], y) : [[s[0][0], s[0][1]], [s[1][0], s[1][1]]];
  }, y.constrain = function(b) {
    return arguments.length ? (n = b, y) : n;
  }, y.duration = function(b) {
    return arguments.length ? (a = +b, y) : a;
  }, y.interpolate = function(b) {
    return arguments.length ? (l = b, y) : l;
  }, y.on = function() {
    var b = c.on.apply(c, arguments);
    return b === c ? y : b;
  }, y.clickDistance = function(b) {
    return arguments.length ? (v = (b = +b) * b, y) : Math.sqrt(v);
  }, y.tapDistance = function(b) {
    return arguments.length ? (w = +b, y) : w;
  }, y;
}
const Ae = {
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
}, Yt = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], ys = ["Enter", " ", "Escape"], xs = {
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
var Et;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(Et || (Et = {}));
var ut;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(ut || (ut = {}));
var Xt;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Xt || (Xt = {}));
const ws = {
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
var nt;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(nt || (nt = {}));
var An;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(An || (An = {}));
var Q;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(Q || (Q = {}));
const Br = {
  [Q.Left]: Q.Right,
  [Q.Right]: Q.Left,
  [Q.Top]: Q.Bottom,
  [Q.Bottom]: Q.Top
};
function vs(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const bs = (e) => "id" in e && "source" in e && "target" in e, ef = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), Jo = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), Jt = (e, t = [0, 0]) => {
  const { width: n, height: o } = Qe(e), r = e.origin ?? t, i = n * r[0], s = o * r[1];
  return {
    x: e.position.x - i,
    y: e.position.y - s
  };
}, tf = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const i = typeof r == "string";
    let s = !t.nodeLookup && !i ? r : void 0;
    t.nodeLookup && (s = i ? t.nodeLookup.get(r) : Jo(r) ? r : t.nodeLookup.get(r.id));
    const a = s ? Dn(s, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return Bn(o, a);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return Fn(n);
}, en = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = Bn(n, Dn(r)), o = !0);
  }), o ? Fn(n) : { x: 0, y: 0, width: 0, height: 0 };
}, er = (e, t, [n, o, r] = [0, 0, 1], i = !1, s = !1) => {
  const a = {
    ...Mt(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, l = [];
  for (const c of e.values()) {
    const { measured: d, selectable: u = !0, hidden: f = !1 } = c;
    if (s && !u || f)
      continue;
    const g = d.width ?? c.width ?? c.initialWidth ?? null, m = d.height ?? c.height ?? c.initialHeight ?? null, v = Wt(a, Nt(c)), w = (g ?? 0) * (m ?? 0), y = i && v > 0;
    (!c.internals.handleBounds || y || v >= w || c.dragging) && l.push(c);
  }
  return l;
}, nf = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function of(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function rf({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: i }, s) {
  if (e.size === 0)
    return !0;
  const a = of(e, s), l = en(a), c = nr(l, t, n, s?.minZoom ?? r, s?.maxZoom ?? i, s?.padding ?? 0.1);
  return await o.setViewport(c, {
    duration: s?.duration,
    ease: s?.ease,
    interpolate: s?.interpolate
  }), !0;
}
function Ss({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: i }) {
  const s = n.get(e), a = s.parentId ? n.get(s.parentId) : void 0, { x: l, y: c } = a ? a.internals.positionAbsolute : { x: 0, y: 0 }, d = s.origin ?? o;
  let u = s.extent || r;
  if (s.extent === "parent" && !s.expandParent)
    if (!a)
      i?.("005", Ae.error005());
    else {
      const g = a.measured.width, m = a.measured.height;
      g && m && (u = [
        [l, c],
        [l + g, c + m]
      ]);
    }
  else a && gt(s.extent) && (u = [
    [s.extent[0][0] + l, s.extent[0][1] + c],
    [s.extent[1][0] + l, s.extent[1][1] + c]
  ]);
  const f = gt(u) ? ht(t, u, s.measured) : t;
  return (s.measured.width === void 0 || s.measured.height === void 0) && i?.("015", Ae.error015()), {
    position: {
      x: f.x - l + (s.measured.width ?? 0) * d[0],
      y: f.y - c + (s.measured.height ?? 0) * d[1]
    },
    positionAbsolute: f
  };
}
async function sf({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const i = new Set(e.map((f) => f.id)), s = [];
  for (const f of n) {
    if (f.deletable === !1)
      continue;
    const g = i.has(f.id), m = !g && f.parentId && s.find((v) => v.id === f.parentId);
    (g || m) && s.push(f);
  }
  const a = new Set(t.map((f) => f.id)), l = o.filter((f) => f.deletable !== !1), d = nf(s, l);
  for (const f of l)
    a.has(f.id) && !d.find((m) => m.id === f.id) && d.push(f);
  if (!r)
    return {
      edges: d,
      nodes: s
    };
  const u = await r({
    nodes: s,
    edges: d
  });
  return typeof u == "boolean" ? u ? { edges: d, nodes: s } : { edges: [], nodes: [] } : u;
}
const _t = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), ht = (e = { x: 0, y: 0 }, t, n) => ({
  x: _t(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: _t(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Es(e, t, n) {
  const { width: o, height: r } = Qe(n), { x: i, y: s } = n.internals.positionAbsolute;
  return ht(e, [
    [i, s],
    [i + o, s + r]
  ], t);
}
const Fr = (e, t, n) => e < t ? _t(Math.abs(e - t), 1, t) / t : e > n ? -_t(Math.abs(e - n), 1, t) / t : 0, tr = (e, t, n = 15, o = 40) => {
  const r = Fr(e.x, o, t.width - o) * n, i = Fr(e.y, o, t.height - o) * n;
  return [r, i];
}, Bn = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), zo = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), Fn = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), Nt = (e, t = [0, 0]) => {
  const { x: n, y: o } = Jo(e) ? e.internals.positionAbsolute : Jt(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, Dn = (e, t = [0, 0]) => {
  const { x: n, y: o } = Jo(e) ? e.internals.positionAbsolute : Jt(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, _s = (e, t) => Fn(Bn(zo(e), zo(t))), Wt = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, Yr = (e) => Le(e.width) && Le(e.height) && Le(e.x) && Le(e.y), Le = (e) => !isNaN(e) && isFinite(e), Ns = (e, t) => (n, o) => {
}, tn = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), Mt = ({ x: e, y: t }, [n, o, r], i = !1, s = [1, 1]) => {
  const a = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return i ? tn(a, s) : a;
}, Ct = ({ x: e, y: t }, [n, o, r]) => ({
  x: e * r + n,
  y: t * r + o
});
function mt(e, t) {
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
function af(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = mt(e, n), r = mt(e, t);
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
    const o = mt(e.top ?? e.y ?? 0, n), r = mt(e.bottom ?? e.y ?? 0, n), i = mt(e.left ?? e.x ?? 0, t), s = mt(e.right ?? e.x ?? 0, t);
    return { top: o, right: s, bottom: r, left: i, x: i + s, y: o + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function cf(e, t, n, o, r, i) {
  const { x: s, y: a } = Ct(e, [t, n, o]), { x: l, y: c } = Ct({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), d = r - l, u = i - c;
  return {
    left: Math.floor(s),
    top: Math.floor(a),
    right: Math.floor(d),
    bottom: Math.floor(u)
  };
}
const nr = (e, t, n, o, r, i) => {
  const s = af(i, t, n), a = (t - s.x) / e.width, l = (n - s.y) / e.height, c = Math.min(a, l), d = _t(c, o, r), u = e.x + e.width / 2, f = e.y + e.height / 2, g = t / 2 - u * d, m = n / 2 - f * d, v = cf(e, g, m, d, t, n), w = {
    left: Math.min(v.left - s.left, 0),
    top: Math.min(v.top - s.top, 0),
    right: Math.min(v.right - s.right, 0),
    bottom: Math.min(v.bottom - s.bottom, 0)
  };
  return {
    x: g - w.left + w.right,
    y: m - w.top + w.bottom,
    zoom: d
  };
}, qt = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function gt(e) {
  return e != null && e !== "parent";
}
function Qe(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function Cs(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function ks(e, t = { width: 0, height: 0 }, n, o, r) {
  const i = { ...e }, s = o.get(n);
  if (s) {
    const a = s.origin || r;
    i.x += s.internals.positionAbsolute.x - (t.width ?? 0) * a[0], i.y += s.internals.positionAbsolute.y - (t.height ?? 0) * a[1];
  }
  return i;
}
function Xr(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function lf() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function uf(e) {
  return { ...xs, ...e || {} };
}
function Ht(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: i, y: s } = He(e), a = Mt({ x: i - (r?.left ?? 0), y: s - (r?.top ?? 0) }, o), { x: l, y: c } = n ? tn(a, t) : a;
  return {
    xSnapped: l,
    ySnapped: c,
    ...a
  };
}
const or = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), Is = (e) => e?.getRootNode?.() || window?.document, df = ["INPUT", "SELECT", "TEXTAREA"];
function Ms(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : df.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const As = (e) => "clientX" in e, He = (e, t) => {
  const n = As(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, Wr = (e, t, n, o, r) => {
  const i = t.querySelectorAll(`.${e}`);
  return !i || !i.length ? null : Array.from(i).map((s) => {
    const a = s.getBoundingClientRect();
    return {
      id: s.getAttribute("data-handleid"),
      type: e,
      nodeId: r,
      position: s.getAttribute("data-handlepos"),
      x: (a.left - n.left) / o,
      y: (a.top - n.top) / o,
      ...or(s)
    };
  });
};
function Ds({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: i, targetControlX: s, targetControlY: a }) {
  const l = e * 0.125 + r * 0.375 + s * 0.375 + n * 0.125, c = t * 0.125 + i * 0.375 + a * 0.375 + o * 0.125, d = Math.abs(l - e), u = Math.abs(c - t);
  return [l, c, d, u];
}
function fn(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function qr({ pos: e, x1: t, y1: n, x2: o, y2: r, c: i }) {
  switch (e) {
    case Q.Left:
      return [t - fn(t - o, i), n];
    case Q.Right:
      return [t + fn(o - t, i), n];
    case Q.Top:
      return [t, n - fn(n - r, i)];
    case Q.Bottom:
      return [t, n + fn(r - n, i)];
  }
}
function Ps({ sourceX: e, sourceY: t, sourcePosition: n = Q.Bottom, targetX: o, targetY: r, targetPosition: i = Q.Top, curvature: s = 0.25 }) {
  const [a, l] = qr({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: s
  }), [c, d] = qr({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: s
  }), [u, f, g, m] = Ds({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: r,
    sourceControlX: a,
    sourceControlY: l,
    targetControlX: c,
    targetControlY: d
  });
  return [
    `M${e},${t} C${a},${l} ${c},${d} ${o},${r}`,
    u,
    f,
    g,
    m
  ];
}
function js({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, i = n < e ? n + r : n - r, s = Math.abs(o - t) / 2, a = o < t ? o + s : o - s;
  return [i, a, r, s];
}
function ff({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: i = "basic" }) {
  if (i === "manual")
    return o;
  const s = r && n ? o + 1e3 : o, a = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return s + a;
}
function hf({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const i = Bn(Dn(e), Dn(t));
  i.x === i.x2 && (i.x2 += 1), i.y === i.y2 && (i.y2 += 1);
  const s = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return Wt(s, Fn(i)) > 0;
}
const $s = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, gf = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), pf = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Ae.error006()), t;
  const o = n.getEdgeId || $s;
  let r;
  return bs(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, gf(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, mf = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: r, ...i } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", Ae.error006()), n;
  if (!n.find((c) => c.id === e.id))
    return o.onError?.("007", Ae.error007(r)), n;
  const a = o.getEdgeId || $s, l = {
    ...i,
    id: o.shouldReplaceId ? a(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((c) => c.id !== r).concat(l);
};
function Ts({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, i, s, a] = js({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, i, s, a];
}
const Zr = {
  [Q.Left]: { x: -1, y: 0 },
  [Q.Right]: { x: 1, y: 0 },
  [Q.Top]: { x: 0, y: -1 },
  [Q.Bottom]: { x: 0, y: 1 }
}, yf = ({ source: e, sourcePosition: t = Q.Bottom, target: n }) => t === Q.Left || t === Q.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, Ur = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function xf({ source: e, sourcePosition: t = Q.Bottom, target: n, targetPosition: o = Q.Top, center: r, offset: i, stepPosition: s }) {
  const a = Zr[t], l = Zr[o], c = { x: e.x + a.x * i, y: e.y + a.y * i }, d = { x: n.x + l.x * i, y: n.y + l.y * i }, u = yf({
    source: c,
    sourcePosition: t,
    target: d
  }), f = u.x !== 0 ? "x" : "y", g = u[f];
  let m = [], v, w;
  const y = { x: 0, y: 0 }, S = { x: 0, y: 0 }, [, , p, x] = js({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (a[f] * l[f] === -1) {
    f === "x" ? (v = r.x ?? c.x + (d.x - c.x) * s, w = r.y ?? (c.y + d.y) / 2) : (v = r.x ?? (c.x + d.x) / 2, w = r.y ?? c.y + (d.y - c.y) * s);
    const D = [
      { x: v, y: c.y },
      { x: v, y: d.y }
    ], j = [
      { x: c.x, y: w },
      { x: d.x, y: w }
    ];
    a[f] === g ? m = f === "x" ? D : j : m = f === "x" ? j : D;
  } else {
    const D = [{ x: c.x, y: d.y }], j = [{ x: d.x, y: c.y }];
    if (f === "x" ? m = a.x === g ? j : D : m = a.y === g ? D : j, t === o) {
      const b = Math.abs(e[f] - n[f]);
      if (b <= i) {
        const C = Math.min(i - 1, i - b);
        a[f] === g ? y[f] = (c[f] > e[f] ? -1 : 1) * C : S[f] = (d[f] > n[f] ? -1 : 1) * C;
      }
    }
    if (t !== o) {
      const b = f === "x" ? "y" : "x", C = a[f] === l[b], _ = c[b] > d[b], I = c[b] < d[b];
      (a[f] === 1 && (!C && _ || C && I) || a[f] !== 1 && (!C && I || C && _)) && (m = f === "x" ? D : j);
    }
    const H = { x: c.x + y.x, y: c.y + y.y }, A = { x: d.x + S.x, y: d.y + S.y }, R = Math.max(Math.abs(H.x - m[0].x), Math.abs(A.x - m[0].x)), V = Math.max(Math.abs(H.y - m[0].y), Math.abs(A.y - m[0].y));
    R >= V ? (v = (H.x + A.x) / 2, w = m[0].y) : (v = m[0].x, w = (H.y + A.y) / 2);
  }
  const N = { x: c.x + y.x, y: c.y + y.y }, E = { x: d.x + S.x, y: d.y + S.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...N.x !== m[0].x || N.y !== m[0].y ? [N] : [],
    ...m,
    ...E.x !== m[m.length - 1].x || E.y !== m[m.length - 1].y ? [E] : [],
    n
  ], v, w, p, x];
}
function wf(e, t, n, o) {
  const r = Math.min(Ur(e, t) / 2, Ur(t, n) / 2, o), { x: i, y: s } = t;
  if (e.x === i && i === n.x || e.y === s && s === n.y)
    return `L${i} ${s}`;
  if (e.y === s) {
    const c = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${i + r * c},${s}Q ${i},${s} ${i},${s + r * d}`;
  }
  const a = e.x < n.x ? 1 : -1, l = e.y < n.y ? -1 : 1;
  return `L ${i},${s + r * l}Q ${i},${s} ${i + r * a},${s}`;
}
function Pn({ sourceX: e, sourceY: t, sourcePosition: n = Q.Bottom, targetX: o, targetY: r, targetPosition: i = Q.Top, borderRadius: s = 5, centerX: a, centerY: l, offset: c = 20, stepPosition: d = 0.5 }) {
  const [u, f, g, m, v] = xf({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: i,
    center: { x: a, y: l },
    offset: c,
    stepPosition: d
  });
  let w = `M${u[0].x} ${u[0].y}`;
  for (let y = 1; y < u.length - 1; y++)
    w += wf(u[y - 1], u[y], u[y + 1], s);
  return w += `L${u[u.length - 1].x} ${u[u.length - 1].y}`, [w, f, g, m, v];
}
function Kr(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function vf(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!Kr(t) || !Kr(n))
    return null;
  const o = t.internals.handleBounds || Gr(t.handles), r = n.internals.handleBounds || Gr(n.handles), i = Qr(o?.source ?? [], e.sourceHandle), s = Qr(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === Et.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!i || !s)
    return e.onError?.("008", Ae.error008(i ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const a = i?.position || Q.Bottom, l = s?.position || Q.Top, c = pt(t, i, a), d = pt(n, s, l);
  return {
    sourceX: c.x,
    sourceY: c.y,
    targetX: d.x,
    targetY: d.y,
    sourcePosition: a,
    targetPosition: l
  };
}
function Gr(e) {
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
function pt(e, t, n = Q.Left, o = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, i = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: s, height: a } = t ?? Qe(e);
  if (o)
    return { x: r + s / 2, y: i + a / 2 };
  switch (t?.position ?? n) {
    case Q.Top:
      return { x: r + s / 2, y: i };
    case Q.Right:
      return { x: r + s, y: i + a / 2 };
    case Q.Bottom:
      return { x: r + s / 2, y: i + a };
    case Q.Left:
      return { x: r, y: i + a / 2 };
  }
}
function Qr(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Lo(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function bf(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const i = /* @__PURE__ */ new Set();
  return e.reduce((s, a) => ([a.markerStart || o, a.markerEnd || r].forEach((l) => {
    if (l && typeof l == "object") {
      const c = Lo(l, t);
      i.has(c) || (s.push({ id: c, color: l.color || n, ...l }), i.add(c));
    }
  }), s), []).sort((s, a) => s.id.localeCompare(a.id));
}
const Rs = 1e3, Sf = 10, rr = {
  nodeOrigin: [0, 0],
  nodeExtent: Yt,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Ef = {
  ...rr,
  checkEquality: !0
};
function ir(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function _f(e, t, n) {
  const o = ir(rr, n);
  for (const r of e.values())
    if (r.parentId)
      ar(r, e, t, o);
    else {
      const i = Jt(r, o.nodeOrigin), s = gt(r.extent) ? r.extent : o.nodeExtent, a = ht(i, s, Qe(r));
      r.internals.positionAbsolute = a;
    }
}
function Nf(e, t) {
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
function sr(e) {
  return e === "manual";
}
function Ho(e, t, n, o = {}) {
  const r = ir(Ef, o), i = { i: 0 }, s = new Map(t), a = r?.elevateNodesOnSelect && !sr(r.zIndexMode) ? Rs : 0;
  let l = e.length > 0, c = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let u = s.get(d.id);
    if (r.checkEquality && d === u?.internals.userNode)
      t.set(d.id, u);
    else {
      const f = Jt(d, r.nodeOrigin), g = gt(d.extent) ? d.extent : r.nodeExtent, m = ht(f, g, Qe(d));
      u = {
        ...r.defaults,
        ...d,
        measured: {
          width: d.measured?.width,
          height: d.measured?.height
        },
        internals: {
          positionAbsolute: m,
          // if user re-initializes the node or removes `measured` for whatever reason, we reset the handleBounds so that the node gets re-measured
          handleBounds: Nf(d, u),
          z: zs(d, a, r.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, u);
    }
    (u.measured === void 0 || u.measured.width === void 0 || u.measured.height === void 0) && !u.hidden && (l = !1), d.parentId && ar(u, t, n, o, i), c ||= d.selected ?? !1;
  }
  return { nodesInitialized: l, hasSelectedNodes: c };
}
function Cf(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function ar(e, t, n, o, r) {
  const { elevateNodesOnSelect: i, nodeOrigin: s, nodeExtent: a, zIndexMode: l } = ir(rr, o), c = e.parentId, d = t.get(c);
  if (!d) {
    console.warn(`Parent node ${c} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Cf(e, n), r && !d.parentId && d.internals.rootParentIndex === void 0 && l === "auto" && (d.internals.rootParentIndex = ++r.i, d.internals.z = d.internals.z + r.i * Sf), r && d.internals.rootParentIndex !== void 0 && (r.i = d.internals.rootParentIndex);
  const u = i && !sr(l) ? Rs : 0, { x: f, y: g, z: m } = kf(e, d, s, a, u, l), { positionAbsolute: v } = e.internals, w = f !== v.x || g !== v.y;
  (w || m !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: w ? { x: f, y: g } : v,
      z: m
    }
  });
}
function zs(e, t, n) {
  const o = Le(e.zIndex) ? e.zIndex : 0;
  return sr(n) ? o : o + (e.selected ? t : 0);
}
function kf(e, t, n, o, r, i) {
  const { x: s, y: a } = t.internals.positionAbsolute, l = Qe(e), c = Jt(e, n), d = gt(e.extent) ? ht(c, e.extent, l) : c;
  let u = ht({ x: s + d.x, y: a + d.y }, o, l);
  e.extent === "parent" && (u = Es(u, l, t));
  const f = zs(e, r, i), g = t.internals.z ?? 0;
  return {
    x: u.x,
    y: u.y,
    z: g >= f ? g + 1 : f
  };
}
function cr(e, t, n, o = [0, 0]) {
  const r = [], i = /* @__PURE__ */ new Map();
  for (const s of e) {
    const a = t.get(s.parentId);
    if (!a)
      continue;
    const l = i.get(s.parentId)?.expandedRect ?? Nt(a), c = _s(l, s.rect);
    i.set(s.parentId, { expandedRect: c, parent: a });
  }
  return i.size > 0 && i.forEach(({ expandedRect: s, parent: a }, l) => {
    const c = a.internals.positionAbsolute, d = Qe(a), u = a.origin ?? o, f = s.x < c.x ? Math.round(Math.abs(c.x - s.x)) : 0, g = s.y < c.y ? Math.round(Math.abs(c.y - s.y)) : 0, m = Math.max(d.width, Math.round(s.width)), v = Math.max(d.height, Math.round(s.height)), w = (m - d.width) * u[0], y = (v - d.height) * u[1];
    (f > 0 || g > 0 || w || y) && (r.push({
      id: l,
      type: "position",
      position: {
        x: a.position.x - f + w,
        y: a.position.y - g + y
      }
    }), n.get(l)?.forEach((S) => {
      e.some((p) => p.id === S.id) || r.push({
        id: S.id,
        type: "position",
        position: {
          x: S.position.x + f,
          y: S.position.y + g
        }
      });
    })), (d.width < s.width || d.height < s.height || f || g) && r.push({
      id: l,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: m + (f ? u[0] * f - w : 0),
        height: v + (g ? u[1] * g - y : 0)
      }
    });
  }), r;
}
function If(e, t, n, o, r, i, s) {
  const a = o?.querySelector(".xyflow__viewport");
  let l = !1;
  if (!a)
    return { changes: [], updatedInternals: l };
  const c = [], d = window.getComputedStyle(a), { m22: u } = new window.DOMMatrixReadOnly(d.transform), f = [];
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
      }), l = !0;
      continue;
    }
    const v = or(g.nodeElement), w = m.measured.width !== v.width || m.measured.height !== v.height;
    if (!!(v.width && v.height && (w || !m.internals.handleBounds || g.force))) {
      const S = g.nodeElement.getBoundingClientRect(), p = gt(m.extent) ? m.extent : i;
      let { positionAbsolute: x } = m.internals;
      m.parentId && m.extent === "parent" ? x = Es(x, v, t.get(m.parentId)) : p && (x = ht(x, p, v));
      const N = {
        ...m,
        measured: v,
        internals: {
          ...m.internals,
          positionAbsolute: x,
          handleBounds: {
            source: Wr("source", g.nodeElement, S, u, m.id),
            target: Wr("target", g.nodeElement, S, u, m.id)
          }
        }
      };
      t.set(m.id, N), m.parentId && ar(N, t, n, { nodeOrigin: r, zIndexMode: s }), l = !0, w && (c.push({
        id: m.id,
        type: "dimensions",
        dimensions: v
      }), m.expandParent && m.parentId && f.push({
        id: m.id,
        parentId: m.parentId,
        rect: Nt(N, r)
      }));
    }
  }
  if (f.length > 0) {
    const g = cr(f, t, n, r);
    c.push(...g);
  }
  return { changes: c, updatedInternals: l };
}
async function Mf({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: i }) {
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
function Jr(e, t, n, o, r, i) {
  let s = r;
  const a = o.get(s) || /* @__PURE__ */ new Map();
  o.set(s, a.set(n, t)), s = `${r}-${e}`;
  const l = o.get(s) || /* @__PURE__ */ new Map();
  if (o.set(s, l.set(n, t)), i) {
    s = `${r}-${e}-${i}`;
    const c = o.get(s) || /* @__PURE__ */ new Map();
    o.set(s, c.set(n, t));
  }
}
function Ls(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: i, sourceHandle: s = null, targetHandle: a = null } = o, l = { edgeId: o.id, source: r, target: i, sourceHandle: s, targetHandle: a }, c = `${r}-${s}--${i}-${a}`, d = `${i}-${a}--${r}-${s}`;
    Jr("source", l, d, e, r, s), Jr("target", l, c, e, i, a), t.set(o.id, o);
  }
}
function Hs(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : Hs(n, t) : !1;
}
function ei(e, t, n) {
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
function Af(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [i, s] of e)
    if ((s.selected || s.id === o) && (!s.parentId || !Hs(s, e)) && (s.draggable || t && typeof s.draggable > "u")) {
      const a = e.get(i);
      a && r.set(i, {
        id: i,
        position: a.position || { x: 0, y: 0 },
        distance: {
          x: n.x - a.internals.positionAbsolute.x,
          y: n.y - a.internals.positionAbsolute.y
        },
        extent: a.extent,
        parentId: a.parentId,
        origin: a.origin,
        expandParent: a.expandParent,
        internals: {
          positionAbsolute: a.internals.positionAbsolute || { x: 0, y: 0 }
        },
        measured: {
          width: a.measured.width ?? 0,
          height: a.measured.height ?? 0
        }
      });
    }
  return r;
}
function mo({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
  const r = [];
  for (const [s, a] of t) {
    const l = n.get(s)?.internals.userNode;
    l && r.push({
      ...l,
      position: a.position,
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
function Df({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const i = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, s = tn(i, t);
  return {
    x: s.x - i.x,
    y: s.y - i.y
  };
}
function Pf({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let i = { x: null, y: null }, s = 0, a = /* @__PURE__ */ new Map(), l = !1, c = { x: 0, y: 0 }, d = null, u = !1, f = null, g = !1, m = !1, v = null;
  function w({ noDragClassName: S, handleSelector: p, domNode: x, isSelectable: N, nodeId: E, nodeClickDistance: M = 0 }) {
    f = ke(x);
    function D({ x: R, y: V }) {
      const { nodeLookup: b, nodeExtent: C, snapGrid: _, snapToGrid: I, nodeOrigin: T, onNodeDrag: $, onSelectionDrag: F, onError: B, updateNodePositions: O } = t();
      i = { x: R, y: V };
      let K = !1;
      const W = a.size > 1, G = W && C ? zo(en(a)) : null, oe = W && I ? Df({
        dragItems: a,
        snapGrid: _,
        x: R,
        y: V
      }) : null;
      for (const [q, L] of a) {
        if (!b.has(q))
          continue;
        let Z = { x: R - L.distance.x, y: V - L.distance.y };
        I && (Z = oe ? {
          x: Math.round(Z.x + oe.x),
          y: Math.round(Z.y + oe.y)
        } : tn(Z, _));
        let re = null;
        if (W && C && !L.extent && G) {
          const { positionAbsolute: Y } = L.internals, te = Y.x - G.x + C[0][0], ie = Y.x + L.measured.width - G.x2 + C[1][0], de = Y.y - G.y + C[0][1], xe = Y.y + L.measured.height - G.y2 + C[1][1];
          re = [
            [te, de],
            [ie, xe]
          ];
        }
        const { position: ee, positionAbsolute: P } = Ss({
          nodeId: q,
          nextPosition: Z,
          nodeLookup: b,
          nodeExtent: re || C,
          nodeOrigin: T,
          onError: B
        });
        K = K || L.position.x !== ee.x || L.position.y !== ee.y, L.position = ee, L.internals.positionAbsolute = P;
      }
      if (m = m || K, !!K && (O(a, !0), v && (o || $ || !E && F))) {
        const [q, L] = mo({
          nodeId: E,
          dragItems: a,
          nodeLookup: b
        });
        o?.(v, a, q, L), $?.(v, q, L), E || F?.(v, L);
      }
    }
    async function j() {
      if (!d)
        return;
      const { transform: R, panBy: V, autoPanSpeed: b, autoPanOnNodeDrag: C } = t();
      if (!C) {
        l = !1, cancelAnimationFrame(s);
        return;
      }
      const [_, I] = tr(c, d, b);
      (_ !== 0 || I !== 0) && (i.x = (i.x ?? 0) - _ / R[2], i.y = (i.y ?? 0) - I / R[2], await V({ x: _, y: I }) && D(i)), s = requestAnimationFrame(j);
    }
    function H(R) {
      const { nodeLookup: V, multiSelectionActive: b, nodesDraggable: C, transform: _, snapGrid: I, snapToGrid: T, selectNodesOnDrag: $, onNodeDragStart: F, onSelectionDragStart: B, unselectNodesAndEdges: O } = t();
      u = !0, (!$ || !N) && !b && E && (V.get(E)?.selected || O()), N && $ && E && e?.(E);
      const K = Ht(R.sourceEvent, { transform: _, snapGrid: I, snapToGrid: T, containerBounds: d });
      if (i = K, a = Af(V, C, K, E), a.size > 0 && (n || F || !E && B)) {
        const [W, G] = mo({
          nodeId: E,
          dragItems: a,
          nodeLookup: V
        });
        n?.(R.sourceEvent, a, W, G), F?.(R.sourceEvent, W, G), E || B?.(R.sourceEvent, G);
      }
    }
    const A = ts().clickDistance(M).on("start", (R) => {
      const { domNode: V, nodeDragThreshold: b, transform: C, snapGrid: _, snapToGrid: I } = t();
      d = V?.getBoundingClientRect() || null, g = !1, m = !1, v = R.sourceEvent, b === 0 && H(R), i = Ht(R.sourceEvent, { transform: C, snapGrid: _, snapToGrid: I, containerBounds: d }), c = He(R.sourceEvent, d);
    }).on("drag", (R) => {
      const { autoPanOnNodeDrag: V, transform: b, snapGrid: C, snapToGrid: _, nodeDragThreshold: I, nodeLookup: T } = t(), $ = Ht(R.sourceEvent, { transform: b, snapGrid: C, snapToGrid: _, containerBounds: d });
      if (v = R.sourceEvent, (R.sourceEvent.type === "touchmove" && R.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      E && !T.has(E)) && (g = !0), !g) {
        if (!l && V && u && (l = !0, j()), !u) {
          const F = He(R.sourceEvent, d), B = F.x - c.x, O = F.y - c.y;
          Math.sqrt(B * B + O * O) > I && H(R);
        }
        (i.x !== $.xSnapped || i.y !== $.ySnapped) && a && u && (c = He(R.sourceEvent, d), D($));
      }
    }).on("end", (R) => {
      if (!u || g) {
        g && a.size > 0 && t().updateNodePositions(a, !1);
        return;
      }
      if (l = !1, u = !1, cancelAnimationFrame(s), a.size > 0) {
        const { nodeLookup: V, updateNodePositions: b, onNodeDragStop: C, onSelectionDragStop: _ } = t();
        if (m && (b(a, !1), m = !1), r || C || !E && _) {
          const [I, T] = mo({
            nodeId: E,
            dragItems: a,
            nodeLookup: V,
            dragging: !1
          });
          r?.(R.sourceEvent, a, I, T), C?.(R.sourceEvent, I, T), E || _?.(R.sourceEvent, T);
        }
      }
    }).filter((R) => {
      const V = R.target;
      return !R.button && (!S || !ei(V, `.${S}`, x)) && (!p || ei(V, p, x));
    });
    f.call(A);
  }
  function y() {
    f?.on(".drag", null);
  }
  return {
    update: w,
    destroy: y
  };
}
function jf(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const i of t.values())
    Wt(r, Nt(i)) > 0 && o.push(i);
  return o;
}
const $f = 250;
function Tf(e, t, n, o) {
  let r = [], i = 1 / 0;
  const s = jf(e, n, t + $f);
  for (const a of s) {
    const l = [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []];
    for (const c of l) {
      if (o.nodeId === c.nodeId && o.type === c.type && o.id === c.id)
        continue;
      const { x: d, y: u } = pt(a, c, c.position, !0), f = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(u - e.y, 2));
      f > t || (f < i ? (r = [{ ...c, x: d, y: u }], i = f) : f === i && r.push({ ...c, x: d, y: u }));
    }
  }
  if (!r.length)
    return null;
  if (r.length > 1) {
    const a = o.type === "source" ? "target" : "source";
    return r.find((l) => l.type === a) ?? r[0];
  }
  return r[0];
}
function Vs(e, t, n, o, r, i = !1) {
  const s = o.get(e);
  if (!s)
    return null;
  const a = r === "strict" ? s.internals.handleBounds?.[t] : [...s.internals.handleBounds?.source ?? [], ...s.internals.handleBounds?.target ?? []], l = (n ? a?.find((c) => c.id === n) : a?.[0]) ?? null;
  return l && i ? { ...l, ...pt(s, l, l.position, !0) } : l;
}
function Os(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Rf(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const Bs = () => !0;
function zf(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: i, isTarget: s, domNode: a, nodeLookup: l, lib: c, autoPanOnConnect: d, flowId: u, panBy: f, cancelConnection: g, onConnectStart: m, onConnect: v, onConnectEnd: w, isValidConnection: y = Bs, onReconnectEnd: S, updateConnection: p, getTransform: x, getFromHandle: N, autoPanSpeed: E, dragThreshold: M = 1, handleDomNode: D }) {
  const j = Is(e.target);
  let H = 0, A;
  const { x: R, y: V } = He(e), b = Os(i, D), C = a?.getBoundingClientRect();
  let _ = !1;
  if (!C || !b)
    return;
  const I = Vs(r, b, o, l, t);
  if (!I)
    return;
  let T = He(e, C), $ = !1, F = null, B = !1, O = null;
  function K() {
    if (!d || !C)
      return;
    const [ee, P] = tr(T, C, E);
    f({ x: ee, y: P }), H = requestAnimationFrame(K);
  }
  const W = {
    ...I,
    nodeId: r,
    type: b,
    position: I.position
  }, G = l.get(r);
  let q = {
    inProgress: !0,
    isValid: null,
    from: pt(G, W, Q.Left, !0),
    fromHandle: W,
    fromPosition: W.position,
    fromNode: G,
    to: T,
    toHandle: null,
    toPosition: Br[W.position],
    toNode: null,
    pointer: T
  };
  function L() {
    _ = !0, p(q), m?.(e, { nodeId: r, handleId: o, handleType: b });
  }
  M === 0 && L();
  function Z(ee) {
    if (!_) {
      const { x: xe, y: je } = He(ee), De = xe - R, Pe = je - V;
      if (!(De * De + Pe * Pe > M * M))
        return;
      L();
    }
    if (!N() || !W) {
      re(ee);
      return;
    }
    const P = x();
    T = He(ee, C), A = Tf(Mt(T, P, !1, [1, 1]), n, l, W), $ || (K(), $ = !0);
    const Y = Fs(ee, {
      handle: A,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: s ? "target" : "source",
      isValidConnection: y,
      doc: j,
      lib: c,
      flowId: u,
      nodeLookup: l
    });
    O = Y.handleDomNode, F = Y.connection, B = Rf(!!A, Y.isValid);
    const te = l.get(r), ie = te ? pt(te, W, Q.Left, !0) : q.from, de = {
      ...q,
      from: ie,
      isValid: B,
      to: Y.toHandle && B ? Ct({ x: Y.toHandle.x, y: Y.toHandle.y }, P) : T,
      toHandle: Y.toHandle,
      toPosition: B && Y.toHandle ? Y.toHandle.position : Br[W.position],
      toNode: Y.toHandle ? l.get(Y.toHandle.nodeId) : null,
      pointer: T
    };
    p(de), q = de;
  }
  function re(ee) {
    if (!("touches" in ee && ee.touches.length > 0)) {
      if (_) {
        (A || O) && F && B && v?.(F);
        const { inProgress: P, ...Y } = q, te = {
          ...Y,
          toPosition: q.toHandle ? q.toPosition : null
        };
        w?.(ee, te), i && S?.(ee, te);
      }
      g(), cancelAnimationFrame(H), $ = !1, B = !1, F = null, O = null, j.removeEventListener("mousemove", Z), j.removeEventListener("mouseup", re), j.removeEventListener("touchmove", Z), j.removeEventListener("touchend", re);
    }
  }
  j.addEventListener("mousemove", Z), j.addEventListener("mouseup", re), j.addEventListener("touchmove", Z), j.addEventListener("touchend", re);
}
function Fs(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: i, doc: s, lib: a, flowId: l, isValidConnection: c = Bs, nodeLookup: d }) {
  const u = i === "target", f = t ? s.querySelector(`.${a}-flow__handle[data-id="${l}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: g, y: m } = He(e), v = s.elementFromPoint(g, m), w = v?.classList.contains(`${a}-flow__handle`) ? v : f, y = {
    handleDomNode: w,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (w) {
    const S = Os(void 0, w), p = w.getAttribute("data-nodeid"), x = w.getAttribute("data-handleid"), N = w.classList.contains("connectable"), E = w.classList.contains("connectableend");
    if (!p || !S)
      return y;
    const M = {
      source: u ? p : o,
      sourceHandle: u ? x : r,
      target: u ? o : p,
      targetHandle: u ? r : x
    };
    y.connection = M;
    const j = N && E && (n === Et.Strict ? u && S === "source" || !u && S === "target" : p !== o || x !== r);
    y.isValid = j && c(M), y.toHandle = Vs(p, S, x, d, n, !0);
  }
  return y;
}
const Vo = {
  onPointerDown: zf,
  isValid: Fs
};
function Lf({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = ke(e);
  function i({ translateExtent: a, width: l, height: c, zoomStep: d = 1, pannable: u = !0, zoomable: f = !0, inversePan: g = !1 }) {
    const m = (p) => {
      if (p.sourceEvent.type !== "wheel" || !t)
        return;
      const x = n(), N = p.sourceEvent.ctrlKey && qt() ? 10 : 1, E = -p.sourceEvent.deltaY * (p.sourceEvent.deltaMode === 1 ? 0.05 : p.sourceEvent.deltaMode ? 1 : 2e-3) * d, M = x[2] * Math.pow(2, E * N);
      t.scaleTo(M);
    };
    let v = [0, 0];
    const w = (p) => {
      (p.sourceEvent.type === "mousedown" || p.sourceEvent.type === "touchstart") && (v = [
        p.sourceEvent.clientX ?? p.sourceEvent.touches[0].clientX,
        p.sourceEvent.clientY ?? p.sourceEvent.touches[0].clientY
      ]);
    }, y = (p) => {
      const x = n();
      if (p.sourceEvent.type !== "mousemove" && p.sourceEvent.type !== "touchmove" || !t)
        return;
      const N = [
        p.sourceEvent.clientX ?? p.sourceEvent.touches[0].clientX,
        p.sourceEvent.clientY ?? p.sourceEvent.touches[0].clientY
      ], E = [N[0] - v[0], N[1] - v[1]];
      v = N;
      const M = o() * Math.max(x[2], Math.log(x[2])) * (g ? -1 : 1), D = {
        x: x[0] - E[0] * M,
        y: x[1] - E[1] * M
      }, j = [
        [0, 0],
        [l, c]
      ];
      t.setViewportConstrained({
        x: D.x,
        y: D.y,
        zoom: x[2]
      }, j, a);
    }, S = ms().on("start", w).on("zoom", u ? y : null).on("zoom.wheel", f ? m : null);
    r.call(S, {});
  }
  function s() {
    r.on("zoom", null);
  }
  return {
    update: i,
    destroy: s,
    pointer: Re
  };
}
const Yn = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), yo = ({ x: e, y: t, zoom: n }) => On.translate(e, t).scale(n), yt = (e, t) => e.target.closest(`.${t}`), Ys = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Hf = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, xo = (e, t = 0, n = Hf, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, Xs = (e) => {
  const t = e.ctrlKey && qt() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Vf({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: i, zoomOnPinch: s, onPanZoomStart: a, onPanZoom: l, onPanZoomEnd: c }) {
  return (d) => {
    if (yt(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const u = n.property("__zoom").k || 1;
    if (d.ctrlKey && s) {
      const w = Re(d), y = Xs(d), S = u * Math.pow(2, y);
      o.scaleTo(n, S, w, d);
      return;
    }
    const f = d.deltaMode === 1 ? 20 : 1;
    let g = r === ut.Vertical ? 0 : d.deltaX * f, m = r === ut.Horizontal ? 0 : d.deltaY * f;
    !qt() && d.shiftKey && r !== ut.Vertical && (g = d.deltaY * f, m = 0), o.translateBy(
      n,
      -(g / u) * i,
      -(m / u) * i,
      // @ts-ignore
      { internal: !0 }
    );
    const v = Yn(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (l?.(d, v), e.panScrollTimeout = setTimeout(() => {
      c?.(d, v), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, a?.(d, v));
  };
}
function Of({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const i = o.type === "wheel", s = !t && i && !o.ctrlKey, a = yt(o, e);
    if (o.ctrlKey && i && a && o.preventDefault(), s || a)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function Bf({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = Yn(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function Ff({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (i) => {
    e.usedRightMouseButton = !!(n && Ys(t, e.mouseButton ?? 0)), i.sourceEvent?.sync || o([i.transform.x, i.transform.y, i.transform.k]), r && !i.sourceEvent?.internal && r?.(i.sourceEvent, Yn(i.transform));
  };
}
function Yf({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: i }) {
  return (s) => {
    if (!s.sourceEvent?.internal && (e.isZoomingOrPanning = !1, i && Ys(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && s.sourceEvent && i(s.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
      const a = Yn(s.transform);
      e.prevViewport = a, clearTimeout(e.timerId), e.timerId = setTimeout(
        () => {
          r?.(s.sourceEvent, a);
        },
        // we need a setTimeout for panOnScroll to suppress multiple end events fired during scroll
        n ? 150 : 0
      );
    }
  };
}
function Xf({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: i, userSelectionActive: s, noWheelClassName: a, noPanClassName: l, lib: c, connectionInProgress: d }) {
  return (u) => {
    const f = e || t, g = n && u.ctrlKey, m = u.type === "wheel";
    if (u.button === 1 && u.type === "mousedown" && (yt(u, `${c}-flow__node`) || yt(u, `${c}-flow__edge`)))
      return !0;
    if (!o && !f && !r && !i && !n || s || d && !m || yt(u, a) && m || yt(u, l) && (!m || r && m && !e) || !n && u.ctrlKey && m)
      return !1;
    if (!n && u.type === "touchstart" && u.touches?.length > 1)
      return u.preventDefault(), !1;
    if (!f && !r && !g && m || !o && (u.type === "mousedown" || u.type === "touchstart") || Array.isArray(o) && !o.includes(u.button) && u.type === "mousedown")
      return !1;
    const v = Array.isArray(o) && o.includes(u.button) || !u.button || u.button <= 1;
    return (!u.ctrlKey || m) && v;
  };
}
function Wf({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: i, onPanZoomStart: s, onPanZoomEnd: a, onDraggingChange: l }) {
  const c = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), u = ms().scaleExtent([t, n]).translateExtent(o), f = ke(e).call(u);
  S({
    x: r.x,
    y: r.y,
    zoom: _t(r.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], o);
  const g = f.on("wheel.zoom"), m = f.on("dblclick.zoom");
  u.wheelDelta(Xs);
  async function v(A, R) {
    return f ? new Promise((V) => {
      u?.interpolate(R?.interpolate === "linear" ? Lt : yn).transform(xo(f, R?.duration, R?.ease, () => V(!0)), A);
    }) : !1;
  }
  function w({ noWheelClassName: A, noPanClassName: R, onPaneContextMenu: V, userSelectionActive: b, panOnScroll: C, panOnDrag: _, panOnScrollMode: I, panOnScrollSpeed: T, preventScrolling: $, zoomOnPinch: F, zoomOnScroll: B, zoomOnDoubleClick: O, zoomActivationKeyPressed: K, lib: W, onTransformChange: G, connectionInProgress: oe, paneClickDistance: q, selectionOnDrag: L }) {
    b && !c.isZoomingOrPanning && y();
    const Z = C && !K && !b;
    u.clickDistance(L ? 1 / 0 : !Le(q) || q < 0 ? 0 : q);
    const re = Z ? Vf({
      zoomPanValues: c,
      noWheelClassName: A,
      d3Selection: f,
      d3Zoom: u,
      panOnScrollMode: I,
      panOnScrollSpeed: T,
      zoomOnPinch: F,
      onPanZoomStart: s,
      onPanZoom: i,
      onPanZoomEnd: a
    }) : Of({
      noWheelClassName: A,
      preventScrolling: $,
      d3ZoomHandler: g
    });
    f.on("wheel.zoom", re, { passive: !1 });
    const ee = Bf({
      zoomPanValues: c,
      onDraggingChange: l,
      onPanZoomStart: s
    });
    u.on("start", ee);
    const P = Ff({
      zoomPanValues: c,
      panOnDrag: _,
      onPaneContextMenu: !!V,
      onPanZoom: i,
      onTransformChange: G
    });
    u.on("zoom", P);
    const Y = Yf({
      zoomPanValues: c,
      panOnDrag: _,
      panOnScroll: C,
      onPaneContextMenu: V,
      onPanZoomEnd: a,
      onDraggingChange: l
    });
    u.on("end", Y);
    const te = Xf({
      zoomActivationKeyPressed: K,
      panOnDrag: _,
      zoomOnScroll: B,
      panOnScroll: C,
      zoomOnDoubleClick: O,
      zoomOnPinch: F,
      userSelectionActive: b,
      noPanClassName: R,
      noWheelClassName: A,
      lib: W,
      connectionInProgress: oe
    });
    u.filter(te), O ? f.on("dblclick.zoom", m) : f.on("dblclick.zoom", null);
  }
  function y() {
    u.on("zoom", null);
  }
  async function S(A, R, V) {
    const b = yo(A), C = u?.constrain()(b, R, V);
    return C && await v(C), C;
  }
  async function p(A, R) {
    const V = yo(A);
    return await v(V, R), V;
  }
  function x(A) {
    if (f) {
      const R = yo(A), V = f.property("__zoom");
      (V.k !== A.zoom || V.x !== A.x || V.y !== A.y) && u?.transform(f, R, null, { sync: !0 });
    }
  }
  function N() {
    const A = f ? ps(f.node()) : { x: 0, y: 0, k: 1 };
    return { x: A.x, y: A.y, zoom: A.k };
  }
  async function E(A, R) {
    return f ? new Promise((V) => {
      u?.interpolate(R?.interpolate === "linear" ? Lt : yn).scaleTo(xo(f, R?.duration, R?.ease, () => V(!0)), A);
    }) : !1;
  }
  async function M(A, R) {
    return f ? new Promise((V) => {
      u?.interpolate(R?.interpolate === "linear" ? Lt : yn).scaleBy(xo(f, R?.duration, R?.ease, () => V(!0)), A);
    }) : !1;
  }
  function D(A) {
    u?.scaleExtent(A);
  }
  function j(A) {
    u?.translateExtent(A);
  }
  function H(A) {
    const R = !Le(A) || A < 0 ? 0 : A;
    u?.clickDistance(R);
  }
  return {
    update: w,
    destroy: y,
    setViewport: p,
    setViewportConstrained: S,
    getViewport: N,
    scaleTo: E,
    scaleBy: M,
    setScaleExtent: D,
    setTranslateExtent: j,
    syncViewport: x,
    setClickDistance: H
  };
}
var kt;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(kt || (kt = {}));
function qf({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: i }) {
  const s = e - t, a = n - o, l = [s > 0 ? 1 : s < 0 ? -1 : 0, a > 0 ? 1 : a < 0 ? -1 : 0];
  return s && r && (l[0] = l[0] * -1), a && i && (l[1] = l[1] * -1), l;
}
function ti(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: r
  };
}
function et(e, t) {
  return Math.max(0, t - e);
}
function tt(e, t) {
  return Math.max(0, e - t);
}
function hn(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function ni(e, t) {
  return e ? !t : t;
}
function Zf(e, t, n, o, r, i, s, a) {
  let { affectsX: l, affectsY: c } = t;
  const { isHorizontal: d, isVertical: u } = t, f = d && u, { xSnapped: g, ySnapped: m } = n, { minWidth: v, maxWidth: w, minHeight: y, maxHeight: S } = o, { x: p, y: x, width: N, height: E, aspectRatio: M } = e;
  let D = Math.floor(d ? g - e.pointerX : 0), j = Math.floor(u ? m - e.pointerY : 0);
  const H = N + (l ? -D : D), A = E + (c ? -j : j), R = -i[0] * N, V = -i[1] * E;
  let b = hn(H, v, w), C = hn(A, y, S);
  if (s) {
    let T = 0, $ = 0;
    l && D < 0 ? T = et(p + D + R, s[0][0]) : !l && D > 0 && (T = tt(p + H + R, s[1][0])), c && j < 0 ? $ = et(x + j + V, s[0][1]) : !c && j > 0 && ($ = tt(x + A + V, s[1][1])), b = Math.max(b, T), C = Math.max(C, $);
  }
  if (a) {
    let T = 0, $ = 0;
    l && D > 0 ? T = tt(p + D, a[0][0]) : !l && D < 0 && (T = et(p + H, a[1][0])), c && j > 0 ? $ = tt(x + j, a[0][1]) : !c && j < 0 && ($ = et(x + A, a[1][1])), b = Math.max(b, T), C = Math.max(C, $);
  }
  if (r) {
    if (d) {
      const T = hn(H / M, y, S) * M;
      if (b = Math.max(b, T), s) {
        let $ = 0;
        !l && !c || l && !c && f ? $ = tt(x + V + H / M, s[1][1]) * M : $ = et(x + V + (l ? D : -D) / M, s[0][1]) * M, b = Math.max(b, $);
      }
      if (a) {
        let $ = 0;
        !l && !c || l && !c && f ? $ = et(x + H / M, a[1][1]) * M : $ = tt(x + (l ? D : -D) / M, a[0][1]) * M, b = Math.max(b, $);
      }
    }
    if (u) {
      const T = hn(A * M, v, w) / M;
      if (C = Math.max(C, T), s) {
        let $ = 0;
        !l && !c || c && !l && f ? $ = tt(p + A * M + R, s[1][0]) / M : $ = et(p + (c ? j : -j) * M + R, s[0][0]) / M, C = Math.max(C, $);
      }
      if (a) {
        let $ = 0;
        !l && !c || c && !l && f ? $ = et(p + A * M, a[1][0]) / M : $ = tt(p + (c ? j : -j) * M, a[0][0]) / M, C = Math.max(C, $);
      }
    }
  }
  j = j + (j < 0 ? C : -C), D = D + (D < 0 ? b : -b), r && (f ? H > A * M ? j = (ni(l, c) ? -D : D) / M : D = (ni(l, c) ? -j : j) * M : d ? (j = D / M, c = l) : (D = j * M, l = c));
  const _ = l ? p + D : p, I = c ? x + j : x;
  return {
    width: N + (l ? -D : D),
    height: E + (c ? -j : j),
    x: i[0] * D * (l ? -1 : 1) + _,
    y: i[1] * j * (c ? -1 : 1) + I
  };
}
const Ws = { width: 0, height: 0, x: 0, y: 0 }, Uf = {
  ...Ws,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function Kf(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, i = e.measured.width ?? 0, s = e.measured.height ?? 0, a = n[0] * i, l = n[1] * s;
  return [
    [o - a, r - l],
    [o + i - a, r + s - l]
  ];
}
function Gf({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const i = ke(e);
  let s = {
    controlDirection: ti("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function a({ controlPosition: c, boundaries: d, keepAspectRatio: u, resizeDirection: f, onResizeStart: g, onResize: m, onResizeEnd: v, shouldResize: w }) {
    let y = { ...Ws }, S = { ...Uf };
    s = {
      boundaries: d,
      resizeDirection: f,
      keepAspectRatio: u,
      controlDirection: ti(c)
    };
    let p, x = null, N = [], E, M, D, j = !1;
    const H = ts().on("start", (A) => {
      const { nodeLookup: R, transform: V, snapGrid: b, snapToGrid: C, nodeOrigin: _, paneDomNode: I } = n();
      if (p = R.get(t), !p)
        return;
      x = I?.getBoundingClientRect() ?? null;
      const { xSnapped: T, ySnapped: $ } = Ht(A.sourceEvent, {
        transform: V,
        snapGrid: b,
        snapToGrid: C,
        containerBounds: x
      });
      y = {
        width: p.measured.width ?? 0,
        height: p.measured.height ?? 0,
        x: p.position.x ?? 0,
        y: p.position.y ?? 0
      }, S = {
        ...y,
        pointerX: T,
        pointerY: $,
        aspectRatio: y.width / y.height
      }, E = void 0, M = gt(p.extent) ? p.extent : void 0, p.parentId && (p.extent === "parent" || p.expandParent) && (E = R.get(p.parentId)), E && p.extent === "parent" && (M = [
        [0, 0],
        [E.measured.width, E.measured.height]
      ]), N = [], D = void 0;
      for (const [F, B] of R)
        if (B.parentId === t && (N.push({
          id: F,
          position: { ...B.position },
          extent: B.extent
        }), B.extent === "parent" || B.expandParent)) {
          const O = Kf(B, p, B.origin ?? _);
          D ? D = [
            [Math.min(O[0][0], D[0][0]), Math.min(O[0][1], D[0][1])],
            [Math.max(O[1][0], D[1][0]), Math.max(O[1][1], D[1][1])]
          ] : D = O;
        }
      g?.(A, { ...y });
    }).on("drag", (A) => {
      const { transform: R, snapGrid: V, snapToGrid: b, nodeOrigin: C } = n(), _ = Ht(A.sourceEvent, {
        transform: R,
        snapGrid: V,
        snapToGrid: b,
        containerBounds: x
      }), I = [];
      if (!p)
        return;
      const { x: T, y: $, width: F, height: B } = y, O = {}, K = p.origin ?? C, { width: W, height: G, x: oe, y: q } = Zf(S, s.controlDirection, _, s.boundaries, s.keepAspectRatio, K, M, D), L = W !== F, Z = G !== B, re = oe !== T && L, ee = q !== $ && Z;
      if (!re && !ee && !L && !Z)
        return;
      if ((re || ee || K[0] === 1 || K[1] === 1) && (O.x = re ? oe : y.x, O.y = ee ? q : y.y, y.x = O.x, y.y = O.y, N.length > 0)) {
        const ie = oe - T, de = q - $;
        for (const xe of N)
          xe.position = {
            x: xe.position.x - ie + K[0] * (W - F),
            y: xe.position.y - de + K[1] * (G - B)
          }, I.push(xe);
      }
      if ((L || Z) && (O.width = L && (!s.resizeDirection || s.resizeDirection === "horizontal") ? W : y.width, O.height = Z && (!s.resizeDirection || s.resizeDirection === "vertical") ? G : y.height, y.width = O.width, y.height = O.height), E && p.expandParent) {
        const ie = K[0] * (O.width ?? 0);
        O.x && O.x < ie && (y.x = ie, S.x = S.x - (O.x - ie));
        const de = K[1] * (O.height ?? 0);
        O.y && O.y < de && (y.y = de, S.y = S.y - (O.y - de));
      }
      const P = qf({
        width: y.width,
        prevWidth: F,
        height: y.height,
        prevHeight: B,
        affectsX: s.controlDirection.affectsX,
        affectsY: s.controlDirection.affectsY
      }), Y = { ...y, direction: P };
      w?.(A, Y) !== !1 && (j = !0, m?.(A, Y), o(O, I));
    }).on("end", (A) => {
      j && (v?.(A, { ...y }), r?.({ ...y }), j = !1);
    });
    i.call(H);
  }
  function l() {
    i.on(".drag", null);
  }
  return {
    update: a,
    destroy: l
  };
}
var wo = { exports: {} }, vo = {}, bo = { exports: {} }, So = {};
var oi;
function Qf() {
  if (oi) return So;
  oi = 1;
  var e = rt;
  function t(u, f) {
    return u === f && (u !== 0 || 1 / u === 1 / f) || u !== u && f !== f;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, i = e.useLayoutEffect, s = e.useDebugValue;
  function a(u, f) {
    var g = f(), m = o({ inst: { value: g, getSnapshot: f } }), v = m[0].inst, w = m[1];
    return i(
      function() {
        v.value = g, v.getSnapshot = f, l(v) && w({ inst: v });
      },
      [u, g, f]
    ), r(
      function() {
        return l(v) && w({ inst: v }), u(function() {
          l(v) && w({ inst: v });
        });
      },
      [u]
    ), s(g), g;
  }
  function l(u) {
    var f = u.getSnapshot;
    u = u.value;
    try {
      var g = f();
      return !n(u, g);
    } catch {
      return !0;
    }
  }
  function c(u, f) {
    return f();
  }
  var d = typeof window > "u" || typeof window.document > "u" || typeof window.document.createElement > "u" ? c : a;
  return So.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : d, So;
}
var ri;
function Jf() {
  return ri || (ri = 1, bo.exports = Qf()), bo.exports;
}
var ii;
function eh() {
  if (ii) return vo;
  ii = 1;
  var e = rt, t = Jf();
  function n(c, d) {
    return c === d && (c !== 0 || 1 / c === 1 / d) || c !== c && d !== d;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, i = e.useRef, s = e.useEffect, a = e.useMemo, l = e.useDebugValue;
  return vo.useSyncExternalStoreWithSelector = function(c, d, u, f, g) {
    var m = i(null);
    if (m.current === null) {
      var v = { hasValue: !1, value: null };
      m.current = v;
    } else v = m.current;
    m = a(
      function() {
        function y(E) {
          if (!S) {
            if (S = !0, p = E, E = f(E), g !== void 0 && v.hasValue) {
              var M = v.value;
              if (g(M, E))
                return x = M;
            }
            return x = E;
          }
          if (M = x, o(p, E)) return M;
          var D = f(E);
          return g !== void 0 && g(M, D) ? (p = E, M) : (p = E, x = D);
        }
        var S = !1, p, x, N = u === void 0 ? null : u;
        return [
          function() {
            return y(d());
          },
          N === null ? void 0 : function() {
            return y(N());
          }
        ];
      },
      [d, u, f, g]
    );
    var w = r(c, m[0], m[1]);
    return s(
      function() {
        v.hasValue = !0, v.value = w;
      },
      [w]
    ), l(w), w;
  }, vo;
}
var si;
function th() {
  return si || (si = 1, wo.exports = eh()), wo.exports;
}
var nh = th();
const oh = /* @__PURE__ */ xc(nh), rh = {}, ai = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (d, u) => {
    const f = typeof d == "function" ? d(t) : d;
    if (!Object.is(f, t)) {
      const g = t;
      t = u ?? (typeof f != "object" || f === null) ? f : Object.assign({}, t, f), n.forEach((m) => m(t, g));
    }
  }, r = () => t, l = { setState: o, getState: r, getInitialState: () => c, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (rh ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, c = t = e(o, r, l);
  return l;
}, ih = (e) => e ? ai(e) : ai, { useDebugValue: sh } = rt, { useSyncExternalStoreWithSelector: ah } = oh, ch = (e) => e;
function qs(e, t = ch, n) {
  const o = ah(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return sh(o), o;
}
const ci = (e, t) => {
  const n = ih(e), o = (r, i = t) => qs(n, r, i);
  return Object.assign(o, n), o;
}, lh = (e, t) => e ? ci(e, t) : ci;
function he(e, t) {
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
var Eo = { exports: {} }, ve = {};
var li;
function uh() {
  if (li) return ve;
  li = 1;
  var e = rt;
  function t(l) {
    var c = "https://react.dev/errors/" + l;
    if (1 < arguments.length) {
      c += "?args[]=" + encodeURIComponent(arguments[1]);
      for (var d = 2; d < arguments.length; d++)
        c += "&args[]=" + encodeURIComponent(arguments[d]);
    }
    return "Minified React error #" + l + "; visit " + c + " for the full message or use the non-minified dev environment for full errors and additional helpful warnings.";
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
  function i(l, c, d) {
    var u = 3 < arguments.length && arguments[3] !== void 0 ? arguments[3] : null;
    return {
      $$typeof: r,
      key: u == null ? null : "" + u,
      children: l,
      containerInfo: c,
      implementation: d
    };
  }
  var s = e.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE;
  function a(l, c) {
    if (l === "font") return "";
    if (typeof c == "string")
      return c === "use-credentials" ? c : "";
  }
  return ve.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, ve.createPortal = function(l, c) {
    var d = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!c || c.nodeType !== 1 && c.nodeType !== 9 && c.nodeType !== 11)
      throw Error(t(299));
    return i(l, c, null, d);
  }, ve.flushSync = function(l) {
    var c = s.T, d = o.p;
    try {
      if (s.T = null, o.p = 2, l) return l();
    } finally {
      s.T = c, o.p = d, o.d.f();
    }
  }, ve.preconnect = function(l, c) {
    typeof l == "string" && (c ? (c = c.crossOrigin, c = typeof c == "string" ? c === "use-credentials" ? c : "" : void 0) : c = null, o.d.C(l, c));
  }, ve.prefetchDNS = function(l) {
    typeof l == "string" && o.d.D(l);
  }, ve.preinit = function(l, c) {
    if (typeof l == "string" && c && typeof c.as == "string") {
      var d = c.as, u = a(d, c.crossOrigin), f = typeof c.integrity == "string" ? c.integrity : void 0, g = typeof c.fetchPriority == "string" ? c.fetchPriority : void 0;
      d === "style" ? o.d.S(
        l,
        typeof c.precedence == "string" ? c.precedence : void 0,
        {
          crossOrigin: u,
          integrity: f,
          fetchPriority: g
        }
      ) : d === "script" && o.d.X(l, {
        crossOrigin: u,
        integrity: f,
        fetchPriority: g,
        nonce: typeof c.nonce == "string" ? c.nonce : void 0
      });
    }
  }, ve.preinitModule = function(l, c) {
    if (typeof l == "string")
      if (typeof c == "object" && c !== null) {
        if (c.as == null || c.as === "script") {
          var d = a(
            c.as,
            c.crossOrigin
          );
          o.d.M(l, {
            crossOrigin: d,
            integrity: typeof c.integrity == "string" ? c.integrity : void 0,
            nonce: typeof c.nonce == "string" ? c.nonce : void 0
          });
        }
      } else c == null && o.d.M(l);
  }, ve.preload = function(l, c) {
    if (typeof l == "string" && typeof c == "object" && c !== null && typeof c.as == "string") {
      var d = c.as, u = a(d, c.crossOrigin);
      o.d.L(l, d, {
        crossOrigin: u,
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
  }, ve.preloadModule = function(l, c) {
    if (typeof l == "string")
      if (c) {
        var d = a(c.as, c.crossOrigin);
        o.d.m(l, {
          as: typeof c.as == "string" && c.as !== "script" ? c.as : void 0,
          crossOrigin: d,
          integrity: typeof c.integrity == "string" ? c.integrity : void 0
        });
      } else o.d.m(l);
  }, ve.requestFormReset = function(l) {
    o.d.r(l);
  }, ve.unstable_batchedUpdates = function(l, c) {
    return l(c);
  }, ve.useFormState = function(l, c, d) {
    return s.H.useFormState(l, c, d);
  }, ve.useFormStatus = function() {
    return s.H.useHostTransitionStatus();
  }, ve.version = "19.2.7", ve;
}
var ui;
function dh() {
  if (ui) return Eo.exports;
  ui = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), Eo.exports = uh(), Eo.exports;
}
var fh = dh();
const Xn = Xo(null), hh = Xn.Provider, Zs = Ae.error001("react");
function ce(e, t) {
  const n = Kt(Xn);
  if (n === null)
    throw new Error(Zs);
  return qs(n, e, t);
}
function ge() {
  const e = Kt(Xn);
  if (e === null)
    throw new Error(Zs);
  return pe(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const di = { display: "none" }, gh = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, Us = "react-flow__node-desc", Ks = "react-flow__edge-desc", ph = "react-flow__aria-live", mh = (e) => e.ariaLiveMessage, yh = (e) => e.ariaLabelConfig;
function xh({ rfId: e }) {
  const t = ce(mh);
  return h.jsx("div", { id: `${ph}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: gh, children: t });
}
function wh({ rfId: e, disableKeyboardA11y: t }) {
  const n = ce(yh);
  return h.jsxs(h.Fragment, { children: [h.jsx("div", { id: `${Us}-${e}`, style: di, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), h.jsx("div", { id: `${Ks}-${e}`, style: di, children: n["edge.a11yDescription.default"] }), !t && h.jsx(xh, { rfId: e })] });
}
const Wn = Rn(({ position: e = "top-left", children: t, className: n, style: o, ...r }, i) => {
  const s = `${e}`.split("-");
  return h.jsx("div", { className: ye(["react-flow__panel", n, ...s]), style: o, ref: i, ...r, children: t });
});
Wn.displayName = "Panel";
function vh({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : h.jsx(Wn, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: h.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const bh = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, gn = (e) => e.id;
function Sh(e, t) {
  return he(e.selectedNodes.map(gn), t.selectedNodes.map(gn)) && he(e.selectedEdges.map(gn), t.selectedEdges.map(gn));
}
function Eh({ onSelectionChange: e }) {
  const t = ge(), { selectedNodes: n, selectedEdges: o } = ce(bh, Sh);
  return ae(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((i) => i(r));
  }, [n, o, e]), null;
}
const _h = (e) => !!e.onSelectionChangeHandlers;
function Nh({ onSelectionChange: e }) {
  const t = ce(_h);
  return e || t ? h.jsx(Eh, { onSelectionChange: e }) : null;
}
const Gs = [0, 0], Ch = { x: 0, y: 0, zoom: 1 }, kh = [
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
], fi = [...kh, "rfId"], Ih = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), hi = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: Yt,
  nodeOrigin: Gs,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function Mh(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: i, setNodeExtent: s, reset: a, setDefaultNodesAndEdges: l } = ce(Ih, he), c = ge();
  ae(() => (l(e.defaultNodes, e.defaultEdges), () => {
    d.current = hi, a();
  }), []);
  const d = se(hi);
  return ae(
    () => {
      for (const u of fi) {
        const f = e[u], g = d.current[u];
        f !== g && (typeof e[u] > "u" || (u === "nodes" ? t(f) : u === "edges" ? n(f) : u === "minZoom" ? o(f) : u === "maxZoom" ? r(f) : u === "translateExtent" ? i(f) : u === "nodeExtent" ? s(f) : u === "ariaLabelConfig" ? c.setState({ ariaLabelConfig: uf(f) }) : u === "fitView" ? c.setState({ fitViewQueued: f }) : u === "fitViewOptions" ? c.setState({ fitViewOptions: f }) : c.setState({ [u]: f })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    fi.map((u) => e[u])
  ), null;
}
function gi() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function Ah(e) {
  const [t, n] = ne(e === "system" ? null : e);
  return ae(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = gi(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : gi()?.matches ? "dark" : "light";
}
const pi = typeof document < "u" ? document : null;
function Zt(e = null, t = { target: pi, actInsideInputWithModifier: !0 }) {
  const [n, o] = ne(!1), r = se(!1), i = se(/* @__PURE__ */ new Set([])), [s, a] = pe(() => {
    if (e !== null) {
      const c = (Array.isArray(e) ? e : [e]).filter((u) => typeof u == "string").map((u) => u.replace("+", `
`).replace(`

`, `
+`).split(`
`)), d = c.reduce((u, f) => u.concat(...f), []);
      return [c, d];
    }
    return [[], []];
  }, [e]);
  return ae(() => {
    const l = t?.target ?? pi, c = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (g) => {
        if (r.current = g.ctrlKey || g.metaKey || g.shiftKey || g.altKey, (!r.current || r.current && !c) && Ms(g))
          return !1;
        const v = yi(g.code, a);
        if (i.current.add(g[v]), mi(s, i.current, !1)) {
          const w = g.composedPath?.()?.[0] || g.target, y = w?.nodeName === "BUTTON" || w?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !y) && g.preventDefault(), o(!0);
        }
      }, u = (g) => {
        const m = yi(g.code, a);
        mi(s, i.current, !0) ? (o(!1), i.current.clear()) : i.current.delete(g[m]), g.key === "Meta" && i.current.clear(), r.current = !1;
      }, f = () => {
        i.current.clear(), o(!1);
      };
      return l?.addEventListener("keydown", d), l?.addEventListener("keyup", u), window.addEventListener("blur", f), window.addEventListener("contextmenu", f), () => {
        l?.removeEventListener("keydown", d), l?.removeEventListener("keyup", u), window.removeEventListener("blur", f), window.removeEventListener("contextmenu", f);
      };
    }
  }, [e, o]), n;
}
function mi(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function yi(e, t) {
  return t.includes(e) ? "code" : "key";
}
const Dh = () => {
  const e = ge();
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
      const { width: o, height: r, minZoom: i, maxZoom: s, panZoom: a } = e.getState(), l = nr(t, o, r, i, s, n?.padding ?? 0.1);
      return a ? (await a.setViewport(l, {
        duration: n?.duration,
        ease: n?.ease,
        interpolate: n?.interpolate
      }), !0) : !1;
    },
    screenToFlowPosition: (t, n = {}) => {
      const { transform: o, snapGrid: r, snapToGrid: i, domNode: s } = e.getState();
      if (!s)
        return t;
      const { x: a, y: l } = s.getBoundingClientRect(), c = {
        x: t.x - a,
        y: t.y - l
      }, d = n.snapGrid ?? r, u = n.snapToGrid ?? i;
      return Mt(c, o, u, d);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: r, y: i } = o.getBoundingClientRect(), s = Ct(t, n);
      return {
        x: s.x + r,
        y: s.y + i
      };
    }
  }), []);
};
function Qs(e, t) {
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
    const a = { ...i };
    for (const l of s)
      Ph(l, a);
    n.push(a);
  }
  return r.length && r.forEach((i) => {
    i.index !== void 0 ? n.splice(i.index, 0, { ...i.item }) : n.push({ ...i.item });
  }), n;
}
function Ph(e, t) {
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
function Js(e, t) {
  return Qs(e, t);
}
function ea(e, t) {
  return Qs(e, t);
}
function at(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function xt(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [r, i] of e) {
    const s = t.has(r);
    !(i.selected === void 0 && !s) && i.selected !== s && (n && (i.selected = s), o.push(at(i.id, s)));
  }
  return o;
}
function xi({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((r) => [r.id, r]));
  for (const [r, i] of e.entries()) {
    const s = t.get(i.id), a = s?.internals?.userNode ?? s;
    a !== void 0 && a !== i && n.push({ id: i.id, item: i, type: "replace" }), a === void 0 && n.push({ item: i, type: "add", index: r });
  }
  for (const [r] of t)
    o.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function wi(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const ta = Ns();
function na(e, t, n = {}) {
  return pf(e, t, {
    ...n,
    onError: n.onError ?? ta
  });
}
function jh(e, t, n, o = { shouldReplaceId: !0 }) {
  return mf(e, t, n, {
    ...o,
    onError: o.onError ?? ta
  });
}
const vi = (e) => ef(e), $h = (e) => bs(e);
function oa(e) {
  return Rn(e);
}
const Th = typeof window < "u" ? yc : ae;
function bi(e) {
  const [t, n] = ne(BigInt(0)), [o] = ne(() => Rh(() => n((r) => r + BigInt(1))));
  return Th(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function Rh(e) {
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
const ra = Xo(null);
function zh({ children: e }) {
  const t = ge(), n = fe((a) => {
    const { nodes: l = [], setNodes: c, hasDefaultNodes: d, onNodesChange: u, nodeLookup: f, fitViewQueued: g, onNodesChangeMiddlewareMap: m } = t.getState();
    let v = l;
    for (const y of a)
      v = typeof y == "function" ? y(v) : y;
    let w = xi({
      items: v,
      lookup: f
    });
    for (const y of m.values())
      w = y(w);
    d && c(v), w.length > 0 ? u?.(w) : g && window.requestAnimationFrame(() => {
      const { fitViewQueued: y, nodes: S, setNodes: p } = t.getState();
      y && p(S);
    });
  }, []), o = bi(n), r = fe((a) => {
    const { edges: l = [], setEdges: c, hasDefaultEdges: d, onEdgesChange: u, edgeLookup: f } = t.getState();
    let g = l;
    for (const m of a)
      g = typeof m == "function" ? m(g) : m;
    d ? c(g) : u && u(xi({
      items: g,
      lookup: f
    }));
  }, []), i = bi(r), s = pe(() => ({ nodeQueue: o, edgeQueue: i }), []);
  return h.jsx(ra.Provider, { value: s, children: e });
}
function Lh() {
  const e = Kt(ra);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Hh = (e) => !!e.panZoom;
function lr() {
  const e = Dh(), t = ge(), n = Lh(), o = ce(Hh), r = pe(() => {
    const i = (u) => t.getState().nodeLookup.get(u), s = (u) => {
      n.nodeQueue.push(u);
    }, a = (u) => {
      n.edgeQueue.push(u);
    }, l = (u) => {
      const { nodeLookup: f, nodeOrigin: g } = t.getState(), m = vi(u) ? u : f.get(u.id), v = m.parentId ? ks(m.position, m.measured, m.parentId, f, g) : m.position, w = {
        ...m,
        position: v,
        width: m.measured?.width ?? m.width,
        height: m.measured?.height ?? m.height
      };
      return Nt(w);
    }, c = (u, f, g = { replace: !1 }) => {
      s((m) => m.map((v) => {
        if (v.id === u) {
          const w = typeof f == "function" ? f(v) : f;
          return g.replace && vi(w) ? w : { ...v, ...w };
        }
        return v;
      }));
    }, d = (u, f, g = { replace: !1 }) => {
      a((m) => m.map((v) => {
        if (v.id === u) {
          const w = typeof f == "function" ? f(v) : f;
          return g.replace && $h(w) ? w : { ...v, ...w };
        }
        return v;
      }));
    };
    return {
      getNodes: () => t.getState().nodes.map((u) => ({ ...u })),
      getNode: (u) => i(u)?.internals.userNode,
      getInternalNode: i,
      getEdges: () => {
        const { edges: u = [] } = t.getState();
        return u.map((f) => ({ ...f }));
      },
      getEdge: (u) => t.getState().edgeLookup.get(u),
      setNodes: s,
      setEdges: a,
      addNodes: (u) => {
        const f = Array.isArray(u) ? u : [u];
        n.nodeQueue.push((g) => [...g, ...f]);
      },
      addEdges: (u) => {
        const f = Array.isArray(u) ? u : [u];
        n.edgeQueue.push((g) => [...g, ...f]);
      },
      toObject: () => {
        const { nodes: u = [], edges: f = [], transform: g } = t.getState(), [m, v, w] = g;
        return {
          nodes: u.map((y) => ({ ...y })),
          edges: f.map((y) => ({ ...y })),
          viewport: {
            x: m,
            y: v,
            zoom: w
          }
        };
      },
      deleteElements: async ({ nodes: u = [], edges: f = [] }) => {
        const { nodes: g, edges: m, onNodesDelete: v, onEdgesDelete: w, triggerNodeChanges: y, triggerEdgeChanges: S, onDelete: p, onBeforeDelete: x } = t.getState(), { nodes: N, edges: E } = await sf({
          nodesToRemove: u,
          edgesToRemove: f,
          nodes: g,
          edges: m,
          onBeforeDelete: x
        }), M = E.length > 0, D = N.length > 0;
        if (M) {
          const j = E.map(wi);
          w?.(E), S(j);
        }
        if (D) {
          const j = N.map(wi);
          v?.(N), y(j);
        }
        return (D || M) && p?.({ nodes: N, edges: E }), { deletedNodes: N, deletedEdges: E };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (u, f = !0, g) => {
        const m = Yr(u), v = m ? u : l(u), w = g !== void 0;
        return v ? (g || t.getState().nodes).filter((y) => {
          const S = t.getState().nodeLookup.get(y.id);
          if (S && !m && (y.id === u.id || !S.internals.positionAbsolute))
            return !1;
          const p = Nt(w ? y : S), x = Wt(p, v);
          return f && x > 0 || x >= p.width * p.height || x >= v.width * v.height;
        }) : [];
      },
      isNodeIntersecting: (u, f, g = !0) => {
        const v = Yr(u) ? u : l(u);
        if (!v)
          return !1;
        const w = Wt(v, f);
        return g && w > 0 || w >= f.width * f.height || w >= v.width * v.height;
      },
      updateNode: c,
      updateNodeData: (u, f, g = { replace: !1 }) => {
        c(u, (m) => {
          const v = typeof f == "function" ? f(m) : f;
          return g.replace ? { ...m, data: v } : { ...m, data: { ...m.data, ...v } };
        }, g);
      },
      updateEdge: d,
      updateEdgeData: (u, f, g = { replace: !1 }) => {
        d(u, (m) => {
          const v = typeof f == "function" ? f(m) : f;
          return g.replace ? { ...m, data: v } : { ...m, data: { ...m.data, ...v } };
        }, g);
      },
      getNodesBounds: (u) => {
        const { nodeLookup: f, nodeOrigin: g } = t.getState();
        return tf(u, { nodeLookup: f, nodeOrigin: g });
      },
      getHandleConnections: ({ type: u, id: f, nodeId: g }) => Array.from(t.getState().connectionLookup.get(`${g}-${u}${f ? `-${f}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: u, handleId: f, nodeId: g }) => Array.from(t.getState().connectionLookup.get(`${g}${u ? f ? `-${u}-${f}` : `-${u}` : ""}`)?.values() ?? []),
      fitView: async (u) => {
        const f = t.getState().fitViewResolver ?? lf();
        return t.setState({ fitViewQueued: !0, fitViewOptions: u, fitViewResolver: f }), n.nodeQueue.push((g) => [...g]), f.promise;
      }
    };
  }, []);
  return pe(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const Si = (e) => e.selected, Vh = typeof window < "u" ? window : void 0;
function Oh({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = ge(), { deleteElements: o } = lr(), r = Zt(e, { actInsideInputWithModifier: !1 }), i = Zt(t, { target: Vh });
  ae(() => {
    if (r) {
      const { edges: s, nodes: a } = n.getState();
      o({ nodes: a.filter(Si), edges: s.filter(Si) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), ae(() => {
    n.setState({ multiSelectionActive: i });
  }, [i]);
}
function Bh(e) {
  const t = ge();
  ae(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = or(e.current);
      (o.height === 0 || o.width === 0) && t.getState().onError?.("004", Ae.error004()), t.setState({ width: o.width || 500, height: o.height || 500 });
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
const qn = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, Fh = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function Yh({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: i = ut.Free, zoomOnDoubleClick: s = !0, panOnDrag: a = !0, defaultViewport: l, translateExtent: c, minZoom: d, maxZoom: u, zoomActivationKeyCode: f, preventScrolling: g = !0, children: m, noWheelClassName: v, noPanClassName: w, onViewportChange: y, isControlledViewport: S, paneClickDistance: p, selectionOnDrag: x }) {
  const N = ge(), E = se(null), { userSelectionActive: M, lib: D, connectionInProgress: j } = ce(Fh, he), H = Zt(f), A = se();
  Bh(E);
  const R = fe((V) => {
    y?.({ x: V[0], y: V[1], zoom: V[2] }), S || N.setState({ transform: V });
  }, [y, S]);
  return ae(() => {
    if (E.current) {
      A.current = Wf({
        domNode: E.current,
        minZoom: d,
        maxZoom: u,
        translateExtent: c,
        viewport: l,
        onDraggingChange: (_) => N.setState((I) => I.paneDragging === _ ? I : { paneDragging: _ }),
        onPanZoomStart: (_, I) => {
          const { onViewportChangeStart: T, onMoveStart: $ } = N.getState();
          $?.(_, I), T?.(I);
        },
        onPanZoom: (_, I) => {
          const { onViewportChange: T, onMove: $ } = N.getState();
          $?.(_, I), T?.(I);
        },
        onPanZoomEnd: (_, I) => {
          const { onViewportChangeEnd: T, onMoveEnd: $ } = N.getState();
          $?.(_, I), T?.(I);
        }
      });
      const { x: V, y: b, zoom: C } = A.current.getViewport();
      return N.setState({
        panZoom: A.current,
        transform: [V, b, C],
        domNode: E.current.closest(".react-flow")
      }), () => {
        A.current?.destroy();
      };
    }
  }, []), ae(() => {
    A.current?.update({
      onPaneContextMenu: e,
      zoomOnScroll: t,
      zoomOnPinch: n,
      panOnScroll: o,
      panOnScrollSpeed: r,
      panOnScrollMode: i,
      zoomOnDoubleClick: s,
      panOnDrag: a,
      zoomActivationKeyPressed: H,
      preventScrolling: g,
      noPanClassName: w,
      userSelectionActive: M,
      noWheelClassName: v,
      lib: D,
      onTransformChange: R,
      connectionInProgress: j,
      selectionOnDrag: x,
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
    a,
    H,
    g,
    w,
    M,
    v,
    D,
    R,
    j,
    x,
    p
  ]), h.jsx("div", { className: "react-flow__renderer", ref: E, style: qn, children: m });
}
const Xh = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function Wh() {
  const { userSelectionActive: e, userSelectionRect: t } = ce(Xh, he);
  return e && t ? h.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const _o = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, qh = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function Zh({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Xt.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: i, selectionOnDrag: s, onSelectionStart: a, onSelectionEnd: l, onPaneClick: c, onPaneContextMenu: d, onPaneScroll: u, onPaneMouseEnter: f, onPaneMouseMove: g, onPaneMouseLeave: m, children: v }) {
  const w = se(0), y = ge(), { userSelectionActive: S, elementsSelectable: p, dragging: x, connectionInProgress: N, panBy: E, autoPanSpeed: M } = ce(qh, he), D = p && (e || S), j = se(null), H = se(), A = se(/* @__PURE__ */ new Set()), R = se(/* @__PURE__ */ new Set()), V = se(!1), b = se({ x: 0, y: 0 }), C = se(!1), _ = (L) => {
    if (V.current || N) {
      V.current = !1;
      return;
    }
    c?.(L), y.getState().resetSelectedElements(), y.setState({ nodesSelectionActive: !1 });
  }, I = (L) => {
    if (Array.isArray(o) && o?.includes(2)) {
      L.preventDefault();
      return;
    }
    d?.(L);
  }, T = u ? (L) => u(L) : void 0, $ = (L) => {
    V.current && (L.stopPropagation(), V.current = !1);
  }, F = (L) => {
    const { domNode: Z, transform: re } = y.getState();
    if (H.current = Z?.getBoundingClientRect(), !H.current)
      return;
    const ee = L.target === j.current;
    if (!ee && !!L.target.closest(".nokey") || !e || !(s && ee || t) || L.button !== 0 || !L.isPrimary)
      return;
    L.target?.setPointerCapture?.(L.pointerId), V.current = !1;
    const { x: te, y: ie } = He(L.nativeEvent, H.current), de = Mt({ x: te, y: ie }, re);
    y.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: de.x,
        startY: de.y,
        x: te,
        y: ie
      }
    }), ee || (L.stopPropagation(), L.preventDefault());
  };
  function B(L, Z) {
    const { userSelectionRect: re } = y.getState();
    if (!re)
      return;
    const { transform: ee, nodeLookup: P, edgeLookup: Y, connectionLookup: te, triggerNodeChanges: ie, triggerEdgeChanges: de, defaultEdgeOptions: xe } = y.getState(), je = { x: re.startX, y: re.startY }, { x: De, y: Pe } = Ct(je, ee), $e = {
      startX: je.x,
      startY: je.y,
      x: L < De ? L : De,
      y: Z < Pe ? Z : Pe,
      width: Math.abs(L - De),
      height: Math.abs(Z - Pe)
    }, Je = A.current, Be = R.current;
    A.current = new Set(er(P, $e, ee, n === Xt.Partial, !0).map((_e) => _e.id)), R.current = /* @__PURE__ */ new Set();
    const Fe = xe?.selectable ?? !0;
    for (const _e of A.current) {
      const Ne = te.get(_e);
      if (Ne)
        for (const { edgeId: Te } of Ne.values()) {
          const Ze = Y.get(Te);
          Ze && (Ze.selectable ?? Fe) && R.current.add(Te);
        }
    }
    if (!Xr(Je, A.current)) {
      const _e = xt(P, A.current, !0);
      ie(_e);
    }
    if (!Xr(Be, R.current)) {
      const _e = xt(Y, R.current);
      de(_e);
    }
    y.setState({
      userSelectionRect: $e,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function O() {
    if (!r || !H.current)
      return;
    const [L, Z] = tr(b.current, H.current, M);
    E({ x: L, y: Z }).then((re) => {
      if (!V.current || !re) {
        w.current = requestAnimationFrame(O);
        return;
      }
      const { x: ee, y: P } = b.current;
      B(ee, P), w.current = requestAnimationFrame(O);
    });
  }
  const K = () => {
    cancelAnimationFrame(w.current), w.current = 0, C.current = !1;
  };
  ae(() => () => K(), []);
  const W = (L) => {
    const { userSelectionRect: Z, transform: re, resetSelectedElements: ee } = y.getState();
    if (!H.current || !Z)
      return;
    const { x: P, y: Y } = He(L.nativeEvent, H.current);
    b.current = { x: P, y: Y };
    const te = Ct({ x: Z.startX, y: Z.startY }, re);
    if (!V.current) {
      const ie = t ? 0 : i;
      if (Math.hypot(P - te.x, Y - te.y) <= ie)
        return;
      ee(), a?.(L);
    }
    V.current = !0, C.current || (O(), C.current = !0), B(P, Y);
  }, G = (L) => {
    L.button === 0 && (L.target?.releasePointerCapture?.(L.pointerId), !S && L.target === j.current && y.getState().userSelectionRect && _?.(L), y.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), V.current && (l?.(L), y.setState({
      nodesSelectionActive: A.current.size > 0
    })), K());
  }, oe = (L) => {
    L.target?.releasePointerCapture?.(L.pointerId), K();
  }, q = o === !0 || Array.isArray(o) && o.includes(0);
  return h.jsxs("div", { className: ye(["react-flow__pane", { draggable: q, dragging: x, selection: e }]), onClick: D ? void 0 : _o(_, j), onContextMenu: _o(I, j), onWheel: _o(T, j), onPointerEnter: D ? void 0 : f, onPointerMove: D ? W : g, onPointerUp: D ? G : void 0, onPointerCancel: D ? oe : void 0, onPointerDownCapture: D ? F : void 0, onClickCapture: D ? $ : void 0, onPointerLeave: m, ref: j, style: qn, children: [v, h.jsx(Wh, {})] });
}
function Oo({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: i, multiSelectionActive: s, nodeLookup: a, onError: l } = t.getState(), c = a.get(e);
  if (!c) {
    l?.("012", Ae.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), c.selected ? (n || c.selected && s) && (i({ nodes: [c], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function ia({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: i, nodeClickDistance: s }) {
  const a = ge(), [l, c] = ne(!1), d = se();
  return ae(() => {
    d.current = Pf({
      getStoreItems: () => a.getState(),
      onNodeMouseDown: (u) => {
        Oo({
          id: u,
          store: a,
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
const Uh = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function sa() {
  const e = ge();
  return fe((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: i, nodesDraggable: s, onError: a, updateNodePositions: l, nodeLookup: c, nodeOrigin: d } = e.getState(), u = /* @__PURE__ */ new Map(), f = Uh(s), g = r ? i[0] : 5, m = r ? i[1] : 5, v = n.direction.x * g * n.factor, w = n.direction.y * m * n.factor;
    for (const [, y] of c) {
      if (!f(y))
        continue;
      let S = {
        x: y.internals.positionAbsolute.x + v,
        y: y.internals.positionAbsolute.y + w
      };
      r && (S = tn(S, i));
      const { position: p, positionAbsolute: x } = Ss({
        nodeId: y.id,
        nextPosition: S,
        nodeLookup: c,
        nodeExtent: o,
        nodeOrigin: d,
        onError: a
      });
      y.position = p, y.internals.positionAbsolute = x, u.set(y.id, y);
    }
    l(u);
  }, []);
}
const ur = Xo(null), Kh = ur.Provider;
ur.Consumer;
const aa = () => Kt(ur), Gh = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), Qh = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: i, connection: s } = o, { fromHandle: a, toHandle: l, isValid: c } = s, d = l?.nodeId === e && l?.id === t && l?.type === n;
  return {
    connectingFrom: a?.nodeId === e && a?.id === t && a?.type === n,
    connectingTo: d,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: i === Et.Strict ? a?.type !== n : e !== a?.nodeId || t !== a?.id,
    connectionInProcess: !!a,
    clickConnectionInProcess: !!r,
    valid: d && c
  };
};
function Jh({ type: e = "source", position: t = Q.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: i = !0, id: s, onConnect: a, children: l, className: c, onMouseDown: d, onTouchStart: u, ...f }, g) {
  const m = s || null, v = e === "target", w = ge(), y = aa(), { connectOnClick: S, noPanClassName: p, rfId: x } = ce(Gh, he), { connectingFrom: N, connectingTo: E, clickConnecting: M, isPossibleEndHandle: D, connectionInProcess: j, clickConnectionInProcess: H, valid: A } = ce(Qh(y, m, e), he);
  y || w.getState().onError?.("010", Ae.error010());
  const R = (C) => {
    const { defaultEdgeOptions: _, onConnect: I, hasDefaultEdges: T } = w.getState(), $ = {
      ..._,
      ...C
    };
    if (T) {
      const { edges: F, setEdges: B, onError: O } = w.getState();
      B(na($, F, { onError: O }));
    }
    I?.($), a?.($);
  }, V = (C) => {
    if (!y)
      return;
    const _ = As(C.nativeEvent);
    if (r && (_ && C.button === 0 || !_)) {
      const I = w.getState();
      Vo.onPointerDown(C.nativeEvent, {
        handleDomNode: C.currentTarget,
        autoPanOnConnect: I.autoPanOnConnect,
        connectionMode: I.connectionMode,
        connectionRadius: I.connectionRadius,
        domNode: I.domNode,
        nodeLookup: I.nodeLookup,
        lib: I.lib,
        isTarget: v,
        handleId: m,
        nodeId: y,
        flowId: I.rfId,
        panBy: I.panBy,
        cancelConnection: I.cancelConnection,
        onConnectStart: I.onConnectStart,
        onConnectEnd: (...T) => w.getState().onConnectEnd?.(...T),
        updateConnection: I.updateConnection,
        onConnect: R,
        isValidConnection: n || ((...T) => w.getState().isValidConnection?.(...T) ?? !0),
        getTransform: () => w.getState().transform,
        getFromHandle: () => w.getState().connection.fromHandle,
        autoPanSpeed: I.autoPanSpeed,
        dragThreshold: I.connectionDragThreshold
      });
    }
    _ ? d?.(C) : u?.(C);
  }, b = (C) => {
    const { onClickConnectStart: _, onClickConnectEnd: I, connectionClickStartHandle: T, connectionMode: $, isValidConnection: F, lib: B, rfId: O, nodeLookup: K, connection: W } = w.getState();
    if (!y || !T && !r)
      return;
    if (!T) {
      _?.(C.nativeEvent, { nodeId: y, handleId: m, handleType: e }), w.setState({ connectionClickStartHandle: { nodeId: y, type: e, id: m } });
      return;
    }
    const G = Is(C.target), oe = n || F, { connection: q, isValid: L } = Vo.isValid(C.nativeEvent, {
      handle: {
        nodeId: y,
        id: m,
        type: e
      },
      connectionMode: $,
      fromNodeId: T.nodeId,
      fromHandleId: T.id || null,
      fromType: T.type,
      isValidConnection: oe,
      flowId: O,
      doc: G,
      lib: B,
      nodeLookup: K
    });
    L && q && R(q);
    const Z = structuredClone(W);
    delete Z.inProgress, Z.toPosition = Z.toHandle ? Z.toHandle.position : null, I?.(C, Z), w.setState({ connectionClickStartHandle: null });
  };
  return h.jsx("div", { "data-handleid": m, "data-nodeid": y, "data-handlepos": t, "data-id": `${x}-${y}-${m}-${e}`, className: ye([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    p,
    c,
    {
      source: !v,
      target: v,
      connectable: o,
      connectablestart: r,
      connectableend: i,
      clickconnecting: M,
      connectingfrom: N,
      connectingto: E,
      valid: A,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!j || D) && (j || H ? i : r)
    }
  ]), onMouseDown: V, onTouchStart: V, onClick: S ? b : void 0, ref: g, ...f, children: l });
}
const It = me(oa(Jh));
function eg({ data: e, isConnectable: t, sourcePosition: n = Q.Bottom }) {
  return h.jsxs(h.Fragment, { children: [e?.label, h.jsx(It, { type: "source", position: n, isConnectable: t })] });
}
function tg({ data: e, isConnectable: t, targetPosition: n = Q.Top, sourcePosition: o = Q.Bottom }) {
  return h.jsxs(h.Fragment, { children: [h.jsx(It, { type: "target", position: n, isConnectable: t }), e?.label, h.jsx(It, { type: "source", position: o, isConnectable: t })] });
}
function ng() {
  return null;
}
function og({ data: e, isConnectable: t, targetPosition: n = Q.Top }) {
  return h.jsxs(h.Fragment, { children: [h.jsx(It, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const jn = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, Ei = {
  input: eg,
  default: tg,
  output: og,
  group: ng
};
function rg(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const ig = (e) => {
  const { width: t, height: n, x: o, y: r } = en(e.nodeLookup, {
    filter: (i) => !!i.selected
  });
  return {
    width: Le(t) ? t : null,
    height: Le(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function sg({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = ge(), { width: r, height: i, transformString: s, userSelectionActive: a } = ce(ig, he), l = sa(), c = se(null);
  ae(() => {
    n || c.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !a && r !== null && i !== null;
  if (ia({
    nodeRef: c,
    disabled: !d
  }), !d)
    return null;
  const u = e ? (g) => {
    const m = o.getState().nodes.filter((v) => v.selected);
    e(g, m);
  } : void 0, f = (g) => {
    Object.prototype.hasOwnProperty.call(jn, g.key) && (g.preventDefault(), l({
      direction: jn[g.key],
      factor: g.shiftKey ? 4 : 1
    }));
  };
  return h.jsx("div", { className: ye(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: s
  }, children: h.jsx("div", { ref: c, className: "react-flow__nodesselection-rect", onContextMenu: u, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : f, style: {
    width: r,
    height: i
  } }) });
}
const _i = typeof window < "u" ? window : void 0, ag = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function ca({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, paneClickDistance: a, deleteKeyCode: l, selectionKeyCode: c, selectionOnDrag: d, selectionMode: u, onSelectionStart: f, onSelectionEnd: g, multiSelectionKeyCode: m, panActivationKeyCode: v, zoomActivationKeyCode: w, elementsSelectable: y, zoomOnScroll: S, zoomOnPinch: p, panOnScroll: x, panOnScrollSpeed: N, panOnScrollMode: E, zoomOnDoubleClick: M, panOnDrag: D, autoPanOnSelection: j, defaultViewport: H, translateExtent: A, minZoom: R, maxZoom: V, preventScrolling: b, onSelectionContextMenu: C, noWheelClassName: _, noPanClassName: I, disableKeyboardA11y: T, onViewportChange: $, isControlledViewport: F }) {
  const { nodesSelectionActive: B, userSelectionActive: O } = ce(ag, he), K = Zt(c, { target: _i }), W = Zt(v, { target: _i }), G = W || D, oe = W || x, q = d && G !== !0, L = K || O || q;
  return Oh({ deleteKeyCode: l, multiSelectionKeyCode: m }), h.jsx(Yh, { onPaneContextMenu: i, elementsSelectable: y, zoomOnScroll: S, zoomOnPinch: p, panOnScroll: oe, panOnScrollSpeed: N, panOnScrollMode: E, zoomOnDoubleClick: M, panOnDrag: !K && G, defaultViewport: H, translateExtent: A, minZoom: R, maxZoom: V, zoomActivationKeyCode: w, preventScrolling: b, noWheelClassName: _, noPanClassName: I, onViewportChange: $, isControlledViewport: F, paneClickDistance: a, selectionOnDrag: q, children: h.jsxs(Zh, { onSelectionStart: f, onSelectionEnd: g, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, panOnDrag: G, autoPanOnSelection: j, isSelecting: !!L, selectionMode: u, selectionKeyPressed: K, paneClickDistance: a, selectionOnDrag: q, children: [e, B && h.jsx(sg, { onSelectionContextMenu: C, noPanClassName: I, disableKeyboardA11y: T })] }) });
}
ca.displayName = "FlowRenderer";
const cg = me(ca), lg = (e) => (t) => e ? er(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function ug(e) {
  return ce(fe(lg(e), [e]), he);
}
const dg = (e) => e.updateNodeInternals;
function fg() {
  const e = ce(dg), [t] = ne(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
function hg({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = ge(), i = se(null), s = se(null), a = se(e.sourcePosition), l = se(e.targetPosition), c = se(t), d = n && !!e.internals.handleBounds;
  return ae(() => {
    i.current && !e.hidden && (!d || s.current !== i.current) && (s.current && o?.unobserve(s.current), o?.observe(i.current), s.current = i.current);
  }, [d, e.hidden]), ae(() => () => {
    s.current && (o?.unobserve(s.current), s.current = null);
  }, []), ae(() => {
    if (i.current) {
      const u = c.current !== t, f = a.current !== e.sourcePosition, g = l.current !== e.targetPosition;
      (u || f || g) && (c.current = t, a.current = e.sourcePosition, l.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: i.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), i;
}
function gg({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: i, onDoubleClick: s, nodesDraggable: a, elementsSelectable: l, nodesConnectable: c, nodesFocusable: d, resizeObserver: u, noDragClassName: f, noPanClassName: g, disableKeyboardA11y: m, rfId: v, nodeTypes: w, nodeClickDistance: y, onError: S }) {
  const { node: p, internals: x, isParent: N } = ce((L) => {
    const Z = L.nodeLookup.get(e), re = L.parentLookup.has(e);
    return {
      node: Z,
      internals: Z.internals,
      isParent: re
    };
  }, he);
  let E = p.type || "default", M = w?.[E] || Ei[E];
  M === void 0 && (S?.("003", Ae.error003(E)), E = "default", M = w?.default || Ei.default);
  const D = !!(p.draggable || a && typeof p.draggable > "u"), j = !!(p.selectable || l && typeof p.selectable > "u"), H = !!(p.connectable || c && typeof p.connectable > "u"), A = !!(p.focusable || d && typeof p.focusable > "u"), R = ge(), V = Cs(p), b = hg({ node: p, nodeType: E, hasDimensions: V, resizeObserver: u }), C = ia({
    nodeRef: b,
    disabled: p.hidden || !D,
    noDragClassName: f,
    handleSelector: p.dragHandle,
    nodeId: e,
    isSelectable: j,
    nodeClickDistance: y
  }), _ = sa();
  if (p.hidden)
    return null;
  const I = Qe(p), T = rg(p), $ = j || D || t || n || o || r, F = n ? (L) => n(L, { ...x.userNode }) : void 0, B = o ? (L) => o(L, { ...x.userNode }) : void 0, O = r ? (L) => r(L, { ...x.userNode }) : void 0, K = i ? (L) => i(L, { ...x.userNode }) : void 0, W = s ? (L) => s(L, { ...x.userNode }) : void 0, G = (L) => {
    const { selectNodesOnDrag: Z, nodeDragThreshold: re } = R.getState();
    j && (!Z || !D || re > 0) && Oo({
      id: e,
      store: R,
      nodeRef: b
    }), t && t(L, { ...x.userNode });
  }, oe = (L) => {
    if (!(Ms(L.nativeEvent) || m)) {
      if (ys.includes(L.key) && j) {
        const Z = L.key === "Escape";
        Oo({
          id: e,
          store: R,
          unselect: Z,
          nodeRef: b
        });
      } else if (D && p.selected && Object.prototype.hasOwnProperty.call(jn, L.key)) {
        L.preventDefault();
        const { ariaLabelConfig: Z } = R.getState();
        R.setState({
          ariaLiveMessage: Z["node.a11yDescription.ariaLiveMessage"]({
            direction: L.key.replace("Arrow", "").toLowerCase(),
            x: ~~x.positionAbsolute.x,
            y: ~~x.positionAbsolute.y
          })
        }), _({
          direction: jn[L.key],
          factor: L.shiftKey ? 4 : 1
        });
      }
    }
  }, q = () => {
    if (m || !b.current?.matches(":focus-visible"))
      return;
    const { transform: L, width: Z, height: re, autoPanOnNodeFocus: ee, setCenter: P } = R.getState();
    if (!ee)
      return;
    er(/* @__PURE__ */ new Map([[e, p]]), { x: 0, y: 0, width: Z, height: re }, L, !0).length > 0 || P(p.position.x + I.width / 2, p.position.y + I.height / 2, {
      zoom: L[2]
    });
  };
  return h.jsx("div", { className: ye([
    "react-flow__node",
    `react-flow__node-${E}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [g]: D
    },
    p.className,
    {
      selected: p.selected,
      selectable: j,
      parent: N,
      draggable: D,
      dragging: C
    }
  ]), ref: b, style: {
    zIndex: x.z,
    transform: `translate(${x.positionAbsolute.x}px,${x.positionAbsolute.y}px)`,
    pointerEvents: $ ? "all" : "none",
    visibility: V ? "visible" : "hidden",
    ...p.style,
    ...T
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: F, onMouseMove: B, onMouseLeave: O, onContextMenu: K, onClick: G, onDoubleClick: W, onKeyDown: A ? oe : void 0, tabIndex: A ? 0 : void 0, onFocus: A ? q : void 0, role: p.ariaRole ?? (A ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": m ? void 0 : `${Us}-${v}`, "aria-label": p.ariaLabel, ...p.domAttributes, children: h.jsx(Kh, { value: e, children: h.jsx(M, { id: e, data: p.data, type: E, positionAbsoluteX: x.positionAbsolute.x, positionAbsoluteY: x.positionAbsolute.y, selected: p.selected ?? !1, selectable: j, draggable: D, deletable: p.deletable ?? !0, isConnectable: H, sourcePosition: p.sourcePosition, targetPosition: p.targetPosition, dragging: C, dragHandle: p.dragHandle, zIndex: x.z, parentId: p.parentId, ...I }) }) });
}
var pg = me(gg);
const mg = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function la(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: i } = ce(mg, he), s = ug(e.onlyRenderVisibleElements), a = fg();
  return h.jsx("div", { className: "react-flow__nodes", style: qn, children: s.map((l) => (
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
    h.jsx(pg, { id: l, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: a, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: i }, l)
  )) });
}
la.displayName = "NodeRenderer";
const yg = me(la);
function xg(e) {
  return ce(fe((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const i = n.nodeLookup.get(r.source), s = n.nodeLookup.get(r.target);
        i && s && hf({
          sourceNode: i,
          targetNode: s,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && o.push(r.id);
      }
    return o;
  }, [e]), he);
}
const wg = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return h.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, vg = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return h.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Ni = {
  [An.Arrow]: wg,
  [An.ArrowClosed]: vg
};
function bg(e) {
  const t = ge();
  return pe(() => Object.prototype.hasOwnProperty.call(Ni, e) ? Ni[e] : (t.getState().onError?.("009", Ae.error009(e)), null), [e]);
}
const Sg = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: i = "strokeWidth", strokeWidth: s, orient: a = "auto-start-reverse" }) => {
  const l = bg(t);
  return l ? h.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: i, orient: a, refX: "0", refY: "0", children: h.jsx(l, { color: n, strokeWidth: s }) }) : null;
}, ua = ({ defaultColor: e, rfId: t }) => {
  const n = ce((i) => i.edges), o = ce((i) => i.defaultEdgeOptions), r = pe(() => bf(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? h.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: h.jsx("defs", { children: r.map((i) => h.jsx(Sg, { id: i.id, type: i.type, color: i.color, width: i.width, height: i.height, markerUnits: i.markerUnits, strokeWidth: i.strokeWidth, orient: i.orient }, i.id)) }) }) : null;
};
ua.displayName = "MarkerDefinitions";
var Eg = me(ua);
function da({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: i, labelBgPadding: s = [2, 4], labelBgBorderRadius: a = 2, children: l, className: c, ...d }) {
  const [u, f] = ne({ x: 1, y: 0, width: 0, height: 0 }), g = ye(["react-flow__edge-textwrapper", c]), m = se(null);
  return ae(() => {
    if (m.current) {
      const v = m.current.getBBox();
      f({
        x: v.x,
        y: v.y,
        width: v.width,
        height: v.height
      });
    }
  }, [n]), n ? h.jsxs("g", { transform: `translate(${e - u.width / 2} ${t - u.height / 2})`, className: g, visibility: u.width ? "visible" : "hidden", ...d, children: [r && h.jsx("rect", { width: u.width + 2 * s[0], x: -s[0], y: -s[1], height: u.height + 2 * s[1], className: "react-flow__edge-textbg", style: i, rx: a, ry: a }), h.jsx("text", { className: "react-flow__edge-text", y: u.height / 2, dy: "0.3em", ref: m, style: o, children: n }), l] }) : null;
}
da.displayName = "EdgeText";
const _g = me(da);
function nn({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: a, labelBgBorderRadius: l, interactionWidth: c = 20, ...d }) {
  return h.jsxs(h.Fragment, { children: [h.jsx("path", { ...d, d: e, fill: "none", className: ye(["react-flow__edge-path", d.className]) }), c ? h.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: c, className: "react-flow__edge-interaction" }) : null, o && Le(t) && Le(n) ? h.jsx(_g, { x: t, y: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: a, labelBgBorderRadius: l }) : null] });
}
function Ci({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === Q.Left || e === Q.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function fa({ sourceX: e, sourceY: t, sourcePosition: n = Q.Bottom, targetX: o, targetY: r, targetPosition: i = Q.Top }) {
  const [s, a] = Ci({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [l, c] = Ci({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [d, u, f, g] = Ds({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: r,
    sourceControlX: s,
    sourceControlY: a,
    targetControlX: l,
    targetControlY: c
  });
  return [
    `M${e},${t} C${s},${a} ${l},${c} ${o},${r}`,
    d,
    u,
    f,
    g
  ];
}
function ha(e) {
  return me(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: a, label: l, labelStyle: c, labelShowBg: d, labelBgStyle: u, labelBgPadding: f, labelBgBorderRadius: g, style: m, markerEnd: v, markerStart: w, interactionWidth: y }) => {
    const [S, p, x] = fa({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: a
    }), N = e.isInternal ? void 0 : t;
    return h.jsx(nn, { id: N, path: S, labelX: p, labelY: x, label: l, labelStyle: c, labelShowBg: d, labelBgStyle: u, labelBgPadding: f, labelBgBorderRadius: g, style: m, markerEnd: v, markerStart: w, interactionWidth: y });
  });
}
const Ng = ha({ isInternal: !1 }), ga = ha({ isInternal: !0 });
Ng.displayName = "SimpleBezierEdge";
ga.displayName = "SimpleBezierEdgeInternal";
function pa(e) {
  return me(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: d, labelBgBorderRadius: u, style: f, sourcePosition: g = Q.Bottom, targetPosition: m = Q.Top, markerEnd: v, markerStart: w, pathOptions: y, interactionWidth: S }) => {
    const [p, x, N] = Pn({
      sourceX: n,
      sourceY: o,
      sourcePosition: g,
      targetX: r,
      targetY: i,
      targetPosition: m,
      borderRadius: y?.borderRadius,
      offset: y?.offset,
      stepPosition: y?.stepPosition
    }), E = e.isInternal ? void 0 : t;
    return h.jsx(nn, { id: E, path: p, labelX: x, labelY: N, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: d, labelBgBorderRadius: u, style: f, markerEnd: v, markerStart: w, interactionWidth: S });
  });
}
const ma = pa({ isInternal: !1 }), ya = pa({ isInternal: !0 });
ma.displayName = "SmoothStepEdge";
ya.displayName = "SmoothStepEdgeInternal";
function xa(e) {
  return me(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return h.jsx(ma, { ...n, id: o, pathOptions: pe(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Cg = xa({ isInternal: !1 }), wa = xa({ isInternal: !0 });
Cg.displayName = "StepEdge";
wa.displayName = "StepEdgeInternal";
function va(e) {
  return me(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: d, labelBgBorderRadius: u, style: f, markerEnd: g, markerStart: m, interactionWidth: v }) => {
    const [w, y, S] = Ts({ sourceX: n, sourceY: o, targetX: r, targetY: i }), p = e.isInternal ? void 0 : t;
    return h.jsx(nn, { id: p, path: w, labelX: y, labelY: S, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: d, labelBgBorderRadius: u, style: f, markerEnd: g, markerStart: m, interactionWidth: v });
  });
}
const kg = va({ isInternal: !1 }), ba = va({ isInternal: !0 });
kg.displayName = "StraightEdge";
ba.displayName = "StraightEdgeInternal";
function Sa(e) {
  return me(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s = Q.Bottom, targetPosition: a = Q.Top, label: l, labelStyle: c, labelShowBg: d, labelBgStyle: u, labelBgPadding: f, labelBgBorderRadius: g, style: m, markerEnd: v, markerStart: w, pathOptions: y, interactionWidth: S }) => {
    const [p, x, N] = Ps({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: a,
      curvature: y?.curvature
    }), E = e.isInternal ? void 0 : t;
    return h.jsx(nn, { id: E, path: p, labelX: x, labelY: N, label: l, labelStyle: c, labelShowBg: d, labelBgStyle: u, labelBgPadding: f, labelBgBorderRadius: g, style: m, markerEnd: v, markerStart: w, interactionWidth: S });
  });
}
const Ig = Sa({ isInternal: !1 }), Ea = Sa({ isInternal: !0 });
Ig.displayName = "BezierEdge";
Ea.displayName = "BezierEdgeInternal";
const ki = {
  default: Ea,
  straight: ba,
  step: wa,
  smoothstep: ya,
  simplebezier: ga
}, Ii = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, Mg = (e, t, n) => n === Q.Left ? e - t : n === Q.Right ? e + t : e, Ag = (e, t, n) => n === Q.Top ? e - t : n === Q.Bottom ? e + t : e, Mi = "react-flow__edgeupdater";
function Ai({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: i, onMouseOut: s, type: a }) {
  return h.jsx("circle", { onMouseDown: r, onMouseEnter: i, onMouseOut: s, className: ye([Mi, `${Mi}-${a}`]), cx: Mg(t, o, e), cy: Ag(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function Dg({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: i, targetY: s, sourcePosition: a, targetPosition: l, onReconnect: c, onReconnectStart: d, onReconnectEnd: u, setReconnecting: f, setUpdateHover: g }) {
  const m = ge(), v = (x, N) => {
    if (x.button !== 0)
      return;
    const { autoPanOnConnect: E, domNode: M, connectionMode: D, connectionRadius: j, lib: H, onConnectStart: A, cancelConnection: R, nodeLookup: V, rfId: b, panBy: C, updateConnection: _ } = m.getState(), I = N.type === "target", T = (B, O) => {
      f(!1), u?.(B, n, N.type, O);
    }, $ = (B) => c?.(n, B), F = (B, O) => {
      f(!0), d?.(x, n, N.type), A?.(B, O);
    };
    Vo.onPointerDown(x.nativeEvent, {
      autoPanOnConnect: E,
      connectionMode: D,
      connectionRadius: j,
      domNode: M,
      handleId: N.id,
      nodeId: N.nodeId,
      nodeLookup: V,
      isTarget: I,
      edgeUpdaterType: N.type,
      lib: H,
      flowId: b,
      cancelConnection: R,
      panBy: C,
      isValidConnection: (...B) => m.getState().isValidConnection?.(...B) ?? !0,
      onConnect: $,
      onConnectStart: F,
      onConnectEnd: (...B) => m.getState().onConnectEnd?.(...B),
      onReconnectEnd: T,
      updateConnection: _,
      getTransform: () => m.getState().transform,
      getFromHandle: () => m.getState().connection.fromHandle,
      dragThreshold: m.getState().connectionDragThreshold,
      handleDomNode: x.currentTarget
    });
  }, w = (x) => v(x, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), y = (x) => v(x, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), S = () => g(!0), p = () => g(!1);
  return h.jsxs(h.Fragment, { children: [(e === !0 || e === "source") && h.jsx(Ai, { position: a, centerX: o, centerY: r, radius: t, onMouseDown: w, onMouseEnter: S, onMouseOut: p, type: "source" }), (e === !0 || e === "target") && h.jsx(Ai, { position: l, centerX: i, centerY: s, radius: t, onMouseDown: y, onMouseEnter: S, onMouseOut: p, type: "target" })] });
}
function Pg({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: i, onContextMenu: s, onMouseEnter: a, onMouseMove: l, onMouseLeave: c, reconnectRadius: d, onReconnect: u, onReconnectStart: f, onReconnectEnd: g, rfId: m, edgeTypes: v, noPanClassName: w, onError: y, disableKeyboardA11y: S }) {
  let p = ce((P) => P.edgeLookup.get(e));
  const x = ce((P) => P.defaultEdgeOptions);
  p = x ? { ...x, ...p } : p;
  let N = p.type || "default", E = v?.[N] || ki[N];
  E === void 0 && (y?.("011", Ae.error011(N)), N = "default", E = v?.default || ki.default);
  const M = !!(p.focusable || t && typeof p.focusable > "u"), D = typeof u < "u" && (p.reconnectable || n && typeof p.reconnectable > "u"), j = !!(p.selectable || o && typeof p.selectable > "u"), H = se(null), [A, R] = ne(!1), [V, b] = ne(!1), C = ge(), { zIndex: _, sourceX: I, sourceY: T, targetX: $, targetY: F, sourcePosition: B, targetPosition: O } = ce(fe((P) => {
    const Y = P.nodeLookup.get(p.source), te = P.nodeLookup.get(p.target);
    if (!Y || !te)
      return {
        zIndex: p.zIndex,
        ...Ii
      };
    const ie = vf({
      id: e,
      sourceNode: Y,
      targetNode: te,
      sourceHandle: p.sourceHandle || null,
      targetHandle: p.targetHandle || null,
      connectionMode: P.connectionMode,
      onError: y
    });
    return {
      zIndex: ff({
        selected: p.selected,
        zIndex: p.zIndex,
        sourceNode: Y,
        targetNode: te,
        elevateOnSelect: P.elevateEdgesOnSelect,
        zIndexMode: P.zIndexMode
      }),
      ...ie || Ii
    };
  }, [p.source, p.target, p.sourceHandle, p.targetHandle, p.selected, p.zIndex]), he), K = pe(() => p.markerStart ? `url('#${Lo(p.markerStart, m)}')` : void 0, [p.markerStart, m]), W = pe(() => p.markerEnd ? `url('#${Lo(p.markerEnd, m)}')` : void 0, [p.markerEnd, m]);
  if (p.hidden || I === null || T === null || $ === null || F === null)
    return null;
  const G = (P) => {
    const { addSelectedEdges: Y, unselectNodesAndEdges: te, multiSelectionActive: ie } = C.getState();
    j && (C.setState({ nodesSelectionActive: !1 }), p.selected && ie ? (te({ nodes: [], edges: [p] }), H.current?.blur()) : Y([e])), r && r(P, p);
  }, oe = i ? (P) => {
    i(P, { ...p });
  } : void 0, q = s ? (P) => {
    s(P, { ...p });
  } : void 0, L = a ? (P) => {
    a(P, { ...p });
  } : void 0, Z = l ? (P) => {
    l(P, { ...p });
  } : void 0, re = c ? (P) => {
    c(P, { ...p });
  } : void 0, ee = (P) => {
    if (!S && ys.includes(P.key) && j) {
      const { unselectNodesAndEdges: Y, addSelectedEdges: te } = C.getState();
      P.key === "Escape" ? (H.current?.blur(), Y({ edges: [p] })) : te([e]);
    }
  };
  return h.jsx("svg", { style: { zIndex: _ }, children: h.jsxs("g", { className: ye([
    "react-flow__edge",
    `react-flow__edge-${N}`,
    p.className,
    w,
    {
      selected: p.selected,
      animated: p.animated,
      inactive: !j && !r,
      updating: A,
      selectable: j
    }
  ]), onClick: G, onDoubleClick: oe, onContextMenu: q, onMouseEnter: L, onMouseMove: Z, onMouseLeave: re, onKeyDown: M ? ee : void 0, tabIndex: M ? 0 : void 0, role: p.ariaRole ?? (M ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": p.ariaLabel === null ? void 0 : p.ariaLabel || `Edge from ${p.source} to ${p.target}`, "aria-describedby": M ? `${Ks}-${m}` : void 0, ref: H, ...p.domAttributes, children: [!V && h.jsx(E, { id: e, source: p.source, target: p.target, type: p.type, selected: p.selected, animated: p.animated, selectable: j, deletable: p.deletable ?? !0, label: p.label, labelStyle: p.labelStyle, labelShowBg: p.labelShowBg, labelBgStyle: p.labelBgStyle, labelBgPadding: p.labelBgPadding, labelBgBorderRadius: p.labelBgBorderRadius, sourceX: I, sourceY: T, targetX: $, targetY: F, sourcePosition: B, targetPosition: O, data: p.data, style: p.style, sourceHandleId: p.sourceHandle, targetHandleId: p.targetHandle, markerStart: K, markerEnd: W, pathOptions: "pathOptions" in p ? p.pathOptions : void 0, interactionWidth: p.interactionWidth }), D && h.jsx(Dg, { edge: p, isReconnectable: D, reconnectRadius: d, onReconnect: u, onReconnectStart: f, onReconnectEnd: g, sourceX: I, sourceY: T, targetX: $, targetY: F, sourcePosition: B, targetPosition: O, setUpdateHover: R, setReconnecting: b })] }) });
}
var jg = me(Pg);
const $g = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function _a({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: i, onEdgeContextMenu: s, onEdgeMouseEnter: a, onEdgeMouseMove: l, onEdgeMouseLeave: c, onEdgeClick: d, reconnectRadius: u, onEdgeDoubleClick: f, onReconnectStart: g, onReconnectEnd: m, disableKeyboardA11y: v }) {
  const { edgesFocusable: w, edgesReconnectable: y, elementsSelectable: S, onError: p } = ce($g, he), x = xg(t);
  return h.jsxs("div", { className: "react-flow__edges", children: [h.jsx(Eg, { defaultColor: e, rfId: n }), x.map((N) => h.jsx(jg, { id: N, edgesFocusable: w, edgesReconnectable: y, elementsSelectable: S, noPanClassName: r, onReconnect: i, onContextMenu: s, onMouseEnter: a, onMouseMove: l, onMouseLeave: c, onClick: d, reconnectRadius: u, onDoubleClick: f, onReconnectStart: g, onReconnectEnd: m, rfId: n, onError: p, edgeTypes: o, disableKeyboardA11y: v }, N))] });
}
_a.displayName = "EdgeRenderer";
const Tg = me(_a), Rg = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function zg({ children: e }) {
  const t = ce(Rg);
  return h.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function Lg(e) {
  const t = lr(), n = se(!1);
  ae(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Hg = (e) => e.panZoom?.syncViewport;
function Vg(e) {
  const t = ce(Hg), n = ge();
  return ae(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function Og(e) {
  return e.connection.inProgress ? { ...e.connection, to: Mt(e.connection.to, e.transform) } : { ...e.connection };
}
function Bg(e) {
  return Og;
}
function Fg(e) {
  const t = Bg();
  return ce(t, he);
}
const Yg = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function Xg({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: i, height: s, isValid: a, inProgress: l } = ce(Yg, he);
  return !(i && r && l) ? null : h.jsx("svg", { style: e, width: i, height: s, className: "react-flow__connectionline react-flow__container", children: h.jsx("g", { className: ye(["react-flow__connection", vs(a)]), children: h.jsx(Na, { style: t, type: n, CustomComponent: o, isValid: a }) }) });
}
const Na = ({ style: e, type: t = nt.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: i, fromNode: s, fromHandle: a, fromPosition: l, to: c, toNode: d, toHandle: u, toPosition: f, pointer: g } = Fg();
  if (!r)
    return;
  if (n)
    return h.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: s, fromHandle: a, fromX: i.x, fromY: i.y, toX: c.x, toY: c.y, fromPosition: l, toPosition: f, connectionStatus: vs(o), toNode: d, toHandle: u, pointer: g });
  let m = "";
  const v = {
    sourceX: i.x,
    sourceY: i.y,
    sourcePosition: l,
    targetX: c.x,
    targetY: c.y,
    targetPosition: f
  };
  switch (t) {
    case nt.Bezier:
      [m] = Ps(v);
      break;
    case nt.SimpleBezier:
      [m] = fa(v);
      break;
    case nt.Step:
      [m] = Pn({
        ...v,
        borderRadius: 0
      });
      break;
    case nt.SmoothStep:
      [m] = Pn(v);
      break;
    default:
      [m] = Ts(v);
  }
  return h.jsx("path", { d: m, fill: "none", className: "react-flow__connection-path", style: e });
};
Na.displayName = "ConnectionLine";
const Wg = {};
function Di(e = Wg) {
  se(e), ge(), ae(() => {
  }, [e]);
}
function qg() {
  ge(), se(!1), ae(() => {
  }, []);
}
function Ca({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: i, onEdgeDoubleClick: s, onNodeMouseEnter: a, onNodeMouseMove: l, onNodeMouseLeave: c, onNodeContextMenu: d, onSelectionContextMenu: u, onSelectionStart: f, onSelectionEnd: g, connectionLineType: m, connectionLineStyle: v, connectionLineComponent: w, connectionLineContainerStyle: y, selectionKeyCode: S, selectionOnDrag: p, selectionMode: x, multiSelectionKeyCode: N, panActivationKeyCode: E, zoomActivationKeyCode: M, deleteKeyCode: D, onlyRenderVisibleElements: j, elementsSelectable: H, defaultViewport: A, translateExtent: R, minZoom: V, maxZoom: b, preventScrolling: C, defaultMarkerColor: _, zoomOnScroll: I, zoomOnPinch: T, panOnScroll: $, panOnScrollSpeed: F, panOnScrollMode: B, zoomOnDoubleClick: O, panOnDrag: K, autoPanOnSelection: W, onPaneClick: G, onPaneMouseEnter: oe, onPaneMouseMove: q, onPaneMouseLeave: L, onPaneScroll: Z, onPaneContextMenu: re, paneClickDistance: ee, nodeClickDistance: P, onEdgeContextMenu: Y, onEdgeMouseEnter: te, onEdgeMouseMove: ie, onEdgeMouseLeave: de, reconnectRadius: xe, onReconnect: je, onReconnectStart: De, onReconnectEnd: Pe, noDragClassName: $e, noWheelClassName: Je, noPanClassName: Be, disableKeyboardA11y: Fe, nodeExtent: _e, rfId: Ne, viewport: Te, onViewportChange: Ze }) {
  return Di(e), Di(t), qg(), Lg(n), Vg(Te), h.jsx(cg, { onPaneClick: G, onPaneMouseEnter: oe, onPaneMouseMove: q, onPaneMouseLeave: L, onPaneContextMenu: re, onPaneScroll: Z, paneClickDistance: ee, deleteKeyCode: D, selectionKeyCode: S, selectionOnDrag: p, selectionMode: x, onSelectionStart: f, onSelectionEnd: g, multiSelectionKeyCode: N, panActivationKeyCode: E, zoomActivationKeyCode: M, elementsSelectable: H, zoomOnScroll: I, zoomOnPinch: T, zoomOnDoubleClick: O, panOnScroll: $, panOnScrollSpeed: F, panOnScrollMode: B, panOnDrag: K, autoPanOnSelection: W, defaultViewport: A, translateExtent: R, minZoom: V, maxZoom: b, onSelectionContextMenu: u, preventScrolling: C, noDragClassName: $e, noWheelClassName: Je, noPanClassName: Be, disableKeyboardA11y: Fe, onViewportChange: Ze, isControlledViewport: !!Te, children: h.jsxs(zg, { children: [h.jsx(Tg, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: s, onReconnect: je, onReconnectStart: De, onReconnectEnd: Pe, onlyRenderVisibleElements: j, onEdgeContextMenu: Y, onEdgeMouseEnter: te, onEdgeMouseMove: ie, onEdgeMouseLeave: de, reconnectRadius: xe, defaultMarkerColor: _, noPanClassName: Be, disableKeyboardA11y: Fe, rfId: Ne }), h.jsx(Xg, { style: v, type: m, component: w, containerStyle: y }), h.jsx("div", { className: "react-flow__edgelabel-renderer" }), h.jsx(yg, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: i, onNodeMouseEnter: a, onNodeMouseMove: l, onNodeMouseLeave: c, onNodeContextMenu: d, nodeClickDistance: P, onlyRenderVisibleElements: j, noPanClassName: Be, noDragClassName: $e, disableKeyboardA11y: Fe, nodeExtent: _e, rfId: Ne }), h.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Ca.displayName = "GraphView";
const Zg = me(Ca), Ug = Ns(), Pi = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: a, minZoom: l = 0.5, maxZoom: c = 2, nodeOrigin: d, nodeExtent: u, zIndexMode: f = "basic" } = {}) => {
  const g = /* @__PURE__ */ new Map(), m = /* @__PURE__ */ new Map(), v = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), y = o ?? t ?? [], S = n ?? e ?? [], p = d ?? [0, 0], x = u ?? Yt;
  Ls(v, w, y);
  const { nodesInitialized: N } = Ho(S, g, m, {
    nodeOrigin: p,
    nodeExtent: x,
    zIndexMode: f
  });
  let E = [0, 0, 1];
  if (s && r && i) {
    const M = en(g, {
      filter: (A) => !!((A.width || A.initialWidth) && (A.height || A.initialHeight))
    }), { x: D, y: j, zoom: H } = nr(M, r, i, l, c, a?.padding ?? 0.1);
    E = [D, j, H];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: i ?? 0,
    transform: E,
    nodes: S,
    nodesInitialized: N,
    nodeLookup: g,
    parentLookup: m,
    edges: y,
    edgeLookup: w,
    connectionLookup: v,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: o !== void 0,
    panZoom: null,
    minZoom: l,
    maxZoom: c,
    translateExtent: Yt,
    nodeExtent: x,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: Et.Strict,
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
    fitViewOptions: a,
    fitViewResolver: null,
    connection: { ...ws },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: Ug,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: xs,
    zIndexMode: f,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, Kg = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: a, minZoom: l, maxZoom: c, nodeOrigin: d, nodeExtent: u, zIndexMode: f }) => lh((g, m) => {
  async function v() {
    const { nodeLookup: w, panZoom: y, fitViewOptions: S, fitViewResolver: p, width: x, height: N, minZoom: E, maxZoom: M } = m();
    y && (await rf({
      nodes: w,
      width: x,
      height: N,
      panZoom: y,
      minZoom: E,
      maxZoom: M
    }, S), p?.resolve(!0), g({ fitViewResolver: null }));
  }
  return {
    ...Pi({
      nodes: e,
      edges: t,
      width: r,
      height: i,
      fitView: s,
      fitViewOptions: a,
      minZoom: l,
      maxZoom: c,
      nodeOrigin: d,
      nodeExtent: u,
      defaultNodes: n,
      defaultEdges: o,
      zIndexMode: f
    }),
    setNodes: (w) => {
      const { nodeLookup: y, parentLookup: S, nodeOrigin: p, elevateNodesOnSelect: x, fitViewQueued: N, zIndexMode: E, nodesSelectionActive: M } = m(), { nodesInitialized: D, hasSelectedNodes: j } = Ho(w, y, S, {
        nodeOrigin: p,
        nodeExtent: u,
        elevateNodesOnSelect: x,
        checkEquality: !0,
        zIndexMode: E
      }), H = M && j;
      N && D ? (v(), g({
        nodes: w,
        nodesInitialized: D,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: H
      })) : g({ nodes: w, nodesInitialized: D, nodesSelectionActive: H });
    },
    setEdges: (w) => {
      const { connectionLookup: y, edgeLookup: S } = m();
      Ls(y, S, w), g({ edges: w });
    },
    setDefaultNodesAndEdges: (w, y) => {
      if (w) {
        const { setNodes: S } = m();
        S(w), g({ hasDefaultNodes: !0 });
      }
      if (y) {
        const { setEdges: S } = m();
        S(y), g({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (w) => {
      const { triggerNodeChanges: y, nodeLookup: S, parentLookup: p, domNode: x, nodeOrigin: N, nodeExtent: E, debug: M, fitViewQueued: D, zIndexMode: j } = m(), { changes: H, updatedInternals: A } = If(w, S, p, x, N, E, j);
      A && (_f(S, p, { nodeOrigin: N, nodeExtent: E, zIndexMode: j }), D ? (v(), g({ fitViewQueued: !1, fitViewOptions: void 0 })) : g({}), H?.length > 0 && (M && console.log("React Flow: trigger node changes", H), y?.(H)));
    },
    updateNodePositions: (w, y = !1) => {
      const S = [];
      let p = [];
      const { nodeLookup: x, triggerNodeChanges: N, connection: E, updateConnection: M, onNodesChangeMiddlewareMap: D } = m();
      for (const [j, H] of w) {
        const A = x.get(j), R = !!(A?.expandParent && A?.parentId && H?.position), V = {
          id: j,
          type: "position",
          position: R ? {
            x: Math.max(0, H.position.x),
            y: Math.max(0, H.position.y)
          } : H.position,
          dragging: y
        };
        if (A && E.inProgress && E.fromNode.id === A.id) {
          const b = pt(A, E.fromHandle, Q.Left, !0);
          M({ ...E, from: b });
        }
        R && A.parentId && S.push({
          id: j,
          parentId: A.parentId,
          rect: {
            ...H.internals.positionAbsolute,
            width: H.measured.width ?? 0,
            height: H.measured.height ?? 0
          }
        }), p.push(V);
      }
      if (S.length > 0) {
        const { parentLookup: j, nodeOrigin: H } = m(), A = cr(S, x, j, H);
        p.push(...A);
      }
      for (const j of D.values())
        p = j(p);
      N(p);
    },
    triggerNodeChanges: (w) => {
      const { onNodesChange: y, setNodes: S, nodes: p, hasDefaultNodes: x, debug: N } = m();
      if (w?.length) {
        if (x) {
          const E = Js(w, p);
          S(E);
        }
        N && console.log("React Flow: trigger node changes", w), y?.(w);
      }
    },
    triggerEdgeChanges: (w) => {
      const { onEdgesChange: y, setEdges: S, edges: p, hasDefaultEdges: x, debug: N } = m();
      if (w?.length) {
        if (x) {
          const E = ea(w, p);
          S(E);
        }
        N && console.log("React Flow: trigger edge changes", w), y?.(w);
      }
    },
    addSelectedNodes: (w) => {
      const { multiSelectionActive: y, edgeLookup: S, nodeLookup: p, triggerNodeChanges: x, triggerEdgeChanges: N } = m();
      if (y) {
        const E = w.map((M) => at(M, !0));
        x(E);
        return;
      }
      x(xt(p, /* @__PURE__ */ new Set([...w]), !0)), N(xt(S));
    },
    addSelectedEdges: (w) => {
      const { multiSelectionActive: y, edgeLookup: S, nodeLookup: p, triggerNodeChanges: x, triggerEdgeChanges: N } = m();
      if (y) {
        const E = w.map((M) => at(M, !0));
        N(E);
        return;
      }
      N(xt(S, /* @__PURE__ */ new Set([...w]))), x(xt(p, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: w, edges: y } = {}) => {
      const { edges: S, nodes: p, nodeLookup: x, triggerNodeChanges: N, triggerEdgeChanges: E } = m(), M = w || p, D = y || S, j = [];
      for (const A of M) {
        if (!A.selected)
          continue;
        const R = x.get(A.id);
        R && (R.selected = !1), j.push(at(A.id, !1));
      }
      const H = [];
      for (const A of D)
        A.selected && H.push(at(A.id, !1));
      N(j), E(H);
    },
    setMinZoom: (w) => {
      const { panZoom: y, maxZoom: S } = m();
      y?.setScaleExtent([w, S]), g({ minZoom: w });
    },
    setMaxZoom: (w) => {
      const { panZoom: y, minZoom: S } = m();
      y?.setScaleExtent([S, w]), g({ maxZoom: w });
    },
    setTranslateExtent: (w) => {
      m().panZoom?.setTranslateExtent(w), g({ translateExtent: w });
    },
    resetSelectedElements: () => {
      const { edges: w, nodes: y, triggerNodeChanges: S, triggerEdgeChanges: p, elementsSelectable: x } = m();
      if (!x)
        return;
      const N = y.reduce((M, D) => D.selected ? [...M, at(D.id, !1)] : M, []), E = w.reduce((M, D) => D.selected ? [...M, at(D.id, !1)] : M, []);
      S(N), p(E);
    },
    setNodeExtent: (w) => {
      const { nodes: y, nodeLookup: S, parentLookup: p, nodeOrigin: x, elevateNodesOnSelect: N, nodeExtent: E, zIndexMode: M } = m();
      w[0][0] === E[0][0] && w[0][1] === E[0][1] && w[1][0] === E[1][0] && w[1][1] === E[1][1] || (Ho(y, S, p, {
        nodeOrigin: x,
        nodeExtent: w,
        elevateNodesOnSelect: N,
        checkEquality: !1,
        zIndexMode: M
      }), g({ nodeExtent: w }));
    },
    panBy: (w) => {
      const { transform: y, width: S, height: p, panZoom: x, translateExtent: N } = m();
      return Mf({ delta: w, panZoom: x, transform: y, translateExtent: N, width: S, height: p });
    },
    setCenter: async (w, y, S) => {
      const { width: p, height: x, maxZoom: N, panZoom: E } = m();
      if (!E)
        return !1;
      const M = typeof S?.zoom < "u" ? S.zoom : N;
      return await E.setViewport({
        x: p / 2 - w * M,
        y: x / 2 - y * M,
        zoom: M
      }, { duration: S?.duration, ease: S?.ease, interpolate: S?.interpolate }), !0;
    },
    cancelConnection: () => {
      g({
        connection: { ...ws }
      });
    },
    updateConnection: (w) => {
      g({ connection: w });
    },
    reset: () => g({ ...Pi() })
  };
}, Object.is);
function Gg({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: i, initialMinZoom: s, initialMaxZoom: a, initialFitViewOptions: l, fitView: c, nodeOrigin: d, nodeExtent: u, zIndexMode: f, children: g }) {
  const [m] = ne(() => Kg({
    nodes: e,
    edges: t,
    defaultNodes: n,
    defaultEdges: o,
    width: r,
    height: i,
    fitView: c,
    minZoom: s,
    maxZoom: a,
    fitViewOptions: l,
    nodeOrigin: d,
    nodeExtent: u,
    zIndexMode: f
  }));
  return h.jsx(hh, { value: m, children: h.jsx(zh, { children: g }) });
}
function Qg({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: i, height: s, fitView: a, fitViewOptions: l, minZoom: c, maxZoom: d, nodeOrigin: u, nodeExtent: f, zIndexMode: g }) {
  return Kt(Xn) ? h.jsx(h.Fragment, { children: e }) : h.jsx(Gg, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: i, initialHeight: s, fitView: a, initialFitViewOptions: l, initialMinZoom: c, initialMaxZoom: d, nodeOrigin: u, nodeExtent: f, zIndexMode: g, children: e });
}
const Jg = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function ep({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: i, edgeTypes: s, onNodeClick: a, onEdgeClick: l, onInit: c, onMove: d, onMoveStart: u, onMoveEnd: f, onConnect: g, onConnectStart: m, onConnectEnd: v, onClickConnectStart: w, onClickConnectEnd: y, onNodeMouseEnter: S, onNodeMouseMove: p, onNodeMouseLeave: x, onNodeContextMenu: N, onNodeDoubleClick: E, onNodeDragStart: M, onNodeDrag: D, onNodeDragStop: j, onNodesDelete: H, onEdgesDelete: A, onDelete: R, onSelectionChange: V, onSelectionDragStart: b, onSelectionDrag: C, onSelectionDragStop: _, onSelectionContextMenu: I, onSelectionStart: T, onSelectionEnd: $, onBeforeDelete: F, connectionMode: B, connectionLineType: O = nt.Bezier, connectionLineStyle: K, connectionLineComponent: W, connectionLineContainerStyle: G, deleteKeyCode: oe = "Backspace", selectionKeyCode: q = "Shift", selectionOnDrag: L = !1, selectionMode: Z = Xt.Full, panActivationKeyCode: re = "Space", multiSelectionKeyCode: ee = qt() ? "Meta" : "Control", zoomActivationKeyCode: P = qt() ? "Meta" : "Control", snapToGrid: Y, snapGrid: te, onlyRenderVisibleElements: ie = !1, selectNodesOnDrag: de, nodesDraggable: xe, autoPanOnNodeFocus: je, nodesConnectable: De, nodesFocusable: Pe, nodeOrigin: $e = Gs, edgesFocusable: Je, edgesReconnectable: Be, elementsSelectable: Fe = !0, defaultViewport: _e = Ch, minZoom: Ne = 0.5, maxZoom: Te = 2, translateExtent: Ze = Yt, preventScrolling: Kn = !0, nodeExtent: At, defaultMarkerColor: Gn = "#b1b1b7", zoomOnScroll: Dt = !0, zoomOnPinch: Qn = !0, panOnScroll: Jn = !1, panOnScrollSpeed: eo = 0.5, panOnScrollMode: to = ut.Free, zoomOnDoubleClick: no = !0, panOnDrag: oo = !0, onPaneClick: ro, onPaneMouseEnter: rn, onPaneMouseMove: sn, onPaneMouseLeave: io, onPaneScroll: so, onPaneContextMenu: ao, paneClickDistance: co = 1, nodeClickDistance: k = 0, children: z, onReconnect: X, onReconnectStart: U, onReconnectEnd: J, onEdgeContextMenu: ue, onEdgeDoubleClick: le, onEdgeMouseEnter: we, onEdgeMouseMove: Ce, onEdgeMouseLeave: st, reconnectRadius: be = 10, onNodesChange: Va, onEdgesChange: Oa, noDragClassName: Ba = "nodrag", noWheelClassName: Fa = "nowheel", noPanClassName: pr = "nopan", fitView: mr, fitViewOptions: yr, connectOnClick: Ya, attributionPosition: Xa, proOptions: Wa, defaultEdgeOptions: qa, elevateNodesOnSelect: Za = !0, elevateEdgesOnSelect: Ua = !1, disableKeyboardA11y: xr = !1, autoPanOnConnect: Ka, autoPanOnNodeDrag: Ga, autoPanOnSelection: Qa = !0, autoPanSpeed: Ja, connectionRadius: ec, isValidConnection: tc, onError: nc, style: oc, id: wr, nodeDragThreshold: rc, connectionDragThreshold: ic, viewport: sc, onViewportChange: ac, width: cc, height: lc, colorMode: uc = "light", debug: dc, onScroll: vr, ariaLabelConfig: fc, zIndexMode: br = "basic", ...hc }, gc) {
  const lo = wr || "1", pc = Ah(uc), mc = fe((Sr) => {
    Sr.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), vr?.(Sr);
  }, [vr]);
  return h.jsx("div", { "data-testid": "rf__wrapper", ...hc, onScroll: mc, style: { ...oc, ...Jg }, ref: gc, className: ye(["react-flow", r, pc]), id: wr, role: "application", children: h.jsxs(Qg, { nodes: e, edges: t, width: cc, height: lc, fitView: mr, fitViewOptions: yr, minZoom: Ne, maxZoom: Te, nodeOrigin: $e, nodeExtent: At, zIndexMode: br, children: [h.jsx(Mh, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: g, onConnectStart: m, onConnectEnd: v, onClickConnectStart: w, onClickConnectEnd: y, nodesDraggable: xe, autoPanOnNodeFocus: je, nodesConnectable: De, nodesFocusable: Pe, edgesFocusable: Je, edgesReconnectable: Be, elementsSelectable: Fe, elevateNodesOnSelect: Za, elevateEdgesOnSelect: Ua, minZoom: Ne, maxZoom: Te, nodeExtent: At, onNodesChange: Va, onEdgesChange: Oa, snapToGrid: Y, snapGrid: te, connectionMode: B, translateExtent: Ze, connectOnClick: Ya, defaultEdgeOptions: qa, fitView: mr, fitViewOptions: yr, onNodesDelete: H, onEdgesDelete: A, onDelete: R, onNodeDragStart: M, onNodeDrag: D, onNodeDragStop: j, onSelectionDrag: C, onSelectionDragStart: b, onSelectionDragStop: _, onMove: d, onMoveStart: u, onMoveEnd: f, noPanClassName: pr, nodeOrigin: $e, rfId: lo, autoPanOnConnect: Ka, autoPanOnNodeDrag: Ga, autoPanSpeed: Ja, onError: nc, connectionRadius: ec, isValidConnection: tc, selectNodesOnDrag: de, nodeDragThreshold: rc, connectionDragThreshold: ic, onBeforeDelete: F, debug: dc, ariaLabelConfig: fc, zIndexMode: br }), h.jsx(Zg, { onInit: c, onNodeClick: a, onEdgeClick: l, onNodeMouseEnter: S, onNodeMouseMove: p, onNodeMouseLeave: x, onNodeContextMenu: N, onNodeDoubleClick: E, nodeTypes: i, edgeTypes: s, connectionLineType: O, connectionLineStyle: K, connectionLineComponent: W, connectionLineContainerStyle: G, selectionKeyCode: q, selectionOnDrag: L, selectionMode: Z, deleteKeyCode: oe, multiSelectionKeyCode: ee, panActivationKeyCode: re, zoomActivationKeyCode: P, onlyRenderVisibleElements: ie, defaultViewport: _e, translateExtent: Ze, minZoom: Ne, maxZoom: Te, preventScrolling: Kn, zoomOnScroll: Dt, zoomOnPinch: Qn, zoomOnDoubleClick: no, panOnScroll: Jn, panOnScrollSpeed: eo, panOnScrollMode: to, panOnDrag: oo, autoPanOnSelection: Qa, onPaneClick: ro, onPaneMouseEnter: rn, onPaneMouseMove: sn, onPaneMouseLeave: io, onPaneScroll: so, onPaneContextMenu: ao, paneClickDistance: co, nodeClickDistance: k, onSelectionContextMenu: I, onSelectionStart: T, onSelectionEnd: $, onReconnect: X, onReconnectStart: U, onReconnectEnd: J, onEdgeContextMenu: ue, onEdgeDoubleClick: le, onEdgeMouseEnter: we, onEdgeMouseMove: Ce, onEdgeMouseLeave: st, reconnectRadius: be, defaultMarkerColor: Gn, noDragClassName: Ba, noWheelClassName: Fa, noPanClassName: pr, rfId: lo, disableKeyboardA11y: xr, nodeExtent: At, viewport: sc, onViewportChange: ac }), h.jsx(Nh, { onSelectionChange: V }), z, h.jsx(vh, { proOptions: Wa, position: Xa }), h.jsx(wh, { rfId: lo, disableKeyboardA11y: xr })] }) });
}
var tp = oa(ep);
const np = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function op({ children: e }) {
  const t = ce(np);
  return t ? fh.createPortal(e, t) : null;
}
function rp({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return h.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: ye(["react-flow__background-pattern", n, o]) });
}
function ip({ radius: e, className: t }) {
  return h.jsx("circle", { cx: e, cy: e, r: e, className: ye(["react-flow__background-pattern", "dots", t]) });
}
var ot;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(ot || (ot = {}));
const sp = {
  [ot.Dots]: 1,
  [ot.Lines]: 1,
  [ot.Cross]: 6
}, ap = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function ka({
  id: e,
  variant: t = ot.Dots,
  // only used for dots and cross
  gap: n = 20,
  // only used for lines and cross
  size: o,
  lineWidth: r = 1,
  offset: i = 0,
  color: s,
  bgColor: a,
  style: l,
  className: c,
  patternClassName: d
}) {
  const u = se(null), { transform: f, patternId: g } = ce(ap, he), m = o || sp[t], v = t === ot.Dots, w = t === ot.Cross, y = Array.isArray(n) ? n : [n, n], S = [y[0] * f[2] || 1, y[1] * f[2] || 1], p = m * f[2], x = Array.isArray(i) ? i : [i, i], N = w ? [p, p] : S, E = [
    x[0] * f[2] || 1 + N[0] / 2,
    x[1] * f[2] || 1 + N[1] / 2
  ], M = `${g}${e || ""}`;
  return h.jsxs("svg", { className: ye(["react-flow__background", c]), style: {
    ...l,
    ...qn,
    "--xy-background-color-props": a,
    "--xy-background-pattern-color-props": s
  }, ref: u, "data-testid": "rf__background", children: [h.jsx("pattern", { id: M, x: f[0] % S[0], y: f[1] % S[1], width: S[0], height: S[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${E[0]},-${E[1]})`, children: v ? h.jsx(ip, { radius: p / 2, className: d }) : h.jsx(rp, { dimensions: N, lineWidth: r, variant: t, className: d }) }), h.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${M})` })] });
}
ka.displayName = "Background";
const cp = me(ka);
function lp() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: h.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function up() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: h.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function dp() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: h.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function fp() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: h.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function hp() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: h.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function pn({ children: e, className: t, ...n }) {
  return h.jsx("button", { type: "button", className: ye(["react-flow__controls-button", t]), ...n, children: e });
}
const gp = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function Ia({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: i, onZoomOut: s, onFitView: a, onInteractiveChange: l, className: c, children: d, position: u = "bottom-left", orientation: f = "vertical", "aria-label": g }) {
  const m = ge(), { isInteractive: v, minZoomReached: w, maxZoomReached: y, ariaLabelConfig: S } = ce(gp, he), { zoomIn: p, zoomOut: x, fitView: N } = lr(), E = () => {
    p(), i?.();
  }, M = () => {
    x(), s?.();
  }, D = () => {
    N(r), a?.();
  }, j = () => {
    m.setState({
      nodesDraggable: !v,
      nodesConnectable: !v,
      elementsSelectable: !v
    }), l?.(!v);
  }, H = f === "horizontal" ? "horizontal" : "vertical";
  return h.jsxs(Wn, { className: ye(["react-flow__controls", H, c]), position: u, style: e, "data-testid": "rf__controls", "aria-label": g ?? S["controls.ariaLabel"], children: [t && h.jsxs(h.Fragment, { children: [h.jsx(pn, { onClick: E, className: "react-flow__controls-zoomin", title: S["controls.zoomIn.ariaLabel"], "aria-label": S["controls.zoomIn.ariaLabel"], disabled: y, children: h.jsx(lp, {}) }), h.jsx(pn, { onClick: M, className: "react-flow__controls-zoomout", title: S["controls.zoomOut.ariaLabel"], "aria-label": S["controls.zoomOut.ariaLabel"], disabled: w, children: h.jsx(up, {}) })] }), n && h.jsx(pn, { className: "react-flow__controls-fitview", onClick: D, title: S["controls.fitView.ariaLabel"], "aria-label": S["controls.fitView.ariaLabel"], children: h.jsx(dp, {}) }), o && h.jsx(pn, { className: "react-flow__controls-interactive", onClick: j, title: S["controls.interactive.ariaLabel"], "aria-label": S["controls.interactive.ariaLabel"], children: v ? h.jsx(hp, {}) : h.jsx(fp, {}) }), d] });
}
Ia.displayName = "Controls";
const pp = me(Ia);
function mp({ id: e, x: t, y: n, width: o, height: r, style: i, color: s, strokeColor: a, strokeWidth: l, className: c, borderRadius: d, shapeRendering: u, selected: f, onClick: g }) {
  const { background: m, backgroundColor: v } = i || {}, w = s || m || v;
  return h.jsx("rect", { className: ye(["react-flow__minimap-node", { selected: f }, c]), x: t, y: n, rx: d, ry: d, width: o, height: r, style: {
    fill: w,
    stroke: a,
    strokeWidth: l
  }, shapeRendering: u, onClick: g ? (y) => g(y, e) : void 0 });
}
const yp = me(mp), xp = (e) => e.nodes.map((t) => t.id), No = (e) => e instanceof Function ? e : () => e;
function wp({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: i = yp,
  onClick: s
}) {
  const a = ce(xp, he), l = No(t), c = No(e), d = No(n), u = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return h.jsx(h.Fragment, { children: a.map((f) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    h.jsx(bp, { id: f, nodeColorFunc: l, nodeStrokeColorFunc: c, nodeClassNameFunc: d, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: i, onClick: s, shapeRendering: u }, f)
  )) });
}
function vp({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: i, shapeRendering: s, NodeComponent: a, onClick: l }) {
  const { node: c, x: d, y: u, width: f, height: g } = ce((m) => {
    const v = m.nodeLookup.get(e);
    if (!v)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const w = v.internals.userNode, { x: y, y: S } = v.internals.positionAbsolute, { width: p, height: x } = Qe(w);
    return {
      node: w,
      x: y,
      y: S,
      width: p,
      height: x
    };
  }, he);
  return !c || c.hidden || !Cs(c) ? null : h.jsx(a, { x: d, y: u, width: f, height: g, style: c.style, selected: !!c.selected, className: o(c), color: t(c), borderRadius: r, strokeColor: n(c), strokeWidth: i, shapeRendering: s, onClick: l, id: c.id });
}
const bp = me(vp);
var Sp = me(wp);
const Ep = 200, _p = 150, Np = (e) => !e.hidden, Cp = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? _s(en(e.nodeLookup, { filter: Np }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, kp = "react-flow__minimap-desc";
function Ma({
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
  nodeComponent: a,
  bgColor: l,
  maskColor: c,
  maskStrokeColor: d,
  maskStrokeWidth: u,
  position: f = "bottom-right",
  onClick: g,
  onNodeClick: m,
  pannable: v = !1,
  zoomable: w = !1,
  ariaLabel: y,
  inversePan: S,
  zoomStep: p = 1,
  offsetScale: x = 5
}) {
  const N = ge(), E = se(null), { boundingRect: M, viewBB: D, rfId: j, panZoom: H, translateExtent: A, flowWidth: R, flowHeight: V, ariaLabelConfig: b } = ce(Cp, he), C = e?.width ?? Ep, _ = e?.height ?? _p, I = M.width / C, T = M.height / _, $ = Math.max(I, T), F = $ * C, B = $ * _, O = x * $, K = M.x - (F - M.width) / 2 - O, W = M.y - (B - M.height) / 2 - O, G = F + O * 2, oe = B + O * 2, q = `${kp}-${j}`, L = se(0), Z = se();
  L.current = $, ae(() => {
    if (E.current && H)
      return Z.current = Lf({
        domNode: E.current,
        panZoom: H,
        getTransform: () => N.getState().transform,
        getViewScale: () => L.current
      }), () => {
        Z.current?.destroy();
      };
  }, [H]), ae(() => {
    Z.current?.update({
      translateExtent: A,
      width: R,
      height: V,
      inversePan: S,
      pannable: v,
      zoomStep: p,
      zoomable: w
    });
  }, [v, w, S, p, A, R, V]);
  const re = g ? (Y) => {
    const [te, ie] = Z.current?.pointer(Y) || [0, 0];
    g(Y, { x: te, y: ie });
  } : void 0, ee = m ? fe((Y, te) => {
    const ie = N.getState().nodeLookup.get(te).internals.userNode;
    m(Y, ie);
  }, []) : void 0, P = y ?? b["minimap.ariaLabel"];
  return h.jsx(Wn, { position: f, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-background-color-props": typeof c == "string" ? c : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof u == "number" ? u * $ : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof s == "number" ? s : void 0
  }, className: ye(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: h.jsxs("svg", { width: C, height: _, viewBox: `${K} ${W} ${G} ${oe}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": q, ref: E, onClick: re, children: [P && h.jsx("title", { id: q, children: P }), h.jsx(Sp, { onClick: ee, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: i, nodeClassName: r, nodeStrokeWidth: s, nodeComponent: a }), h.jsx("path", { className: "react-flow__minimap-mask", d: `M${K - O},${W - O}h${G + O * 2}v${oe + O * 2}h${-G - O * 2}z
        M${D.x},${D.y}h${D.width}v${D.height}h${-D.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
Ma.displayName = "MiniMap";
const Ip = me(Ma), Mp = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, Ap = {
  [kt.Line]: "right",
  [kt.Handle]: "bottom-right"
};
function Dp({ nodeId: e, position: t, variant: n = kt.Handle, className: o, style: r = void 0, children: i, color: s, minWidth: a = 10, minHeight: l = 10, maxWidth: c = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: u = !1, resizeDirection: f, autoScale: g = !0, shouldResize: m, onResizeStart: v, onResize: w, onResizeEnd: y }) {
  const S = aa(), p = typeof e == "string" ? e : S, x = ge(), N = se(null), E = n === kt.Handle, M = ce(fe(Mp(E && g), [E, g]), he), D = se(null), j = t ?? Ap[n];
  ae(() => {
    if (!(!N.current || !p))
      return D.current || (D.current = Gf({
        domNode: N.current,
        nodeId: p,
        getStoreItems: () => {
          const { nodeLookup: A, transform: R, snapGrid: V, snapToGrid: b, nodeOrigin: C, domNode: _ } = x.getState();
          return {
            nodeLookup: A,
            transform: R,
            snapGrid: V,
            snapToGrid: b,
            nodeOrigin: C,
            paneDomNode: _
          };
        },
        onChange: (A, R) => {
          const { triggerNodeChanges: V, nodeLookup: b, parentLookup: C, nodeOrigin: _ } = x.getState(), I = [], T = { x: A.x, y: A.y }, $ = b.get(p);
          if ($ && $.expandParent && $.parentId) {
            const F = $.origin ?? _, B = A.width ?? $.measured.width ?? 0, O = A.height ?? $.measured.height ?? 0, K = {
              id: $.id,
              parentId: $.parentId,
              rect: {
                width: B,
                height: O,
                ...ks({
                  x: A.x ?? $.position.x,
                  y: A.y ?? $.position.y
                }, { width: B, height: O }, $.parentId, b, F)
              }
            }, W = cr([K], b, C, _);
            I.push(...W), T.x = A.x ? Math.max(F[0] * B, A.x) : void 0, T.y = A.y ? Math.max(F[1] * O, A.y) : void 0;
          }
          if (T.x !== void 0 && T.y !== void 0) {
            const F = {
              id: p,
              type: "position",
              position: { ...T }
            };
            I.push(F);
          }
          if (A.width !== void 0 && A.height !== void 0) {
            const B = {
              id: p,
              type: "dimensions",
              resizing: !0,
              setAttributes: f ? f === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: A.width,
                height: A.height
              }
            };
            I.push(B);
          }
          for (const F of R) {
            const B = {
              ...F,
              type: "position"
            };
            I.push(B);
          }
          V(I);
        },
        onEnd: ({ width: A, height: R }) => {
          const V = {
            id: p,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: A,
              height: R
            }
          };
          x.getState().triggerNodeChanges([V]);
        }
      })), D.current.update({
        controlPosition: j,
        boundaries: {
          minWidth: a,
          minHeight: l,
          maxWidth: c,
          maxHeight: d
        },
        keepAspectRatio: u,
        resizeDirection: f,
        onResizeStart: v,
        onResize: w,
        onResizeEnd: y,
        shouldResize: m
      }), () => {
        D.current?.destroy();
      };
  }, [
    j,
    a,
    l,
    c,
    d,
    u,
    v,
    w,
    y,
    m
  ]);
  const H = j.split("-");
  return h.jsx("div", { className: ye(["react-flow__resize-control", "nodrag", ...H, n, o]), ref: N, style: {
    ...r,
    scale: M,
    ...s && { [E ? "backgroundColor" : "borderColor"]: s }
  }, children: i });
}
me(Dp);
const Pp = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), Aa = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var jp = {
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
const $p = Rn(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: o,
    className: r = "",
    children: i,
    iconNode: s,
    ...a
  }, l) => Io(
    "svg",
    {
      ref: l,
      ...jp,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: Aa("lucide", r),
      ...a
    },
    [
      ...s.map(([c, d]) => Io(c, d)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const Ee = (e, t) => {
  const n = Rn(
    ({ className: o, ...r }, i) => Io($p, {
      ref: i,
      iconNode: t,
      className: Aa(`lucide-${Pp(e)}`, o),
      ...r
    })
  );
  return n.displayName = `${e}`, n;
};
const Tp = Ee("Boxes", [
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
const Zn = Ee("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const Rp = Ee("ChevronDown", [
  ["path", { d: "m6 9 6 6 6-6", key: "qrunsl" }]
]);
const zp = Ee("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const bn = Ee("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const Ut = Ee("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const Lp = Ee("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const Hp = Ee("ListTree", [
  ["path", { d: "M21 12h-8", key: "1bmf0i" }],
  ["path", { d: "M21 6H8", key: "1pqkrb" }],
  ["path", { d: "M21 18h-8", key: "1tm79t" }],
  ["path", { d: "M3 6v4c0 1.1.9 2 2 2h3", key: "1ywdgy" }],
  ["path", { d: "M3 10v6c0 1.1.9 2 2 2h3", key: "2wc746" }]
]);
const Da = Ee("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const dr = Ee("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const Vp = Ee("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const Op = Ee("Save", [
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
const Bp = Ee("Search", [
  ["circle", { cx: "11", cy: "11", r: "8", key: "4ej97u" }],
  ["path", { d: "m21 21-4.3-4.3", key: "1qie3q" }]
]);
const Bo = Ee("Trash2", [
  ["path", { d: "M3 6h18", key: "d0wm0j" }],
  ["path", { d: "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", key: "4alrt4" }],
  ["path", { d: "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", key: "v07s0e" }],
  ["line", { x1: "10", x2: "10", y1: "11", y2: "17", key: "1uufr5" }],
  ["line", { x1: "14", x2: "14", y1: "11", y2: "17", key: "xtxkd" }]
]), Oe = "/_elsa/workflow-management", ji = 1e4;
async function Fp(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), it(e, `${Oe}/definitions?${n.toString()}`);
}
async function Yp(e, t) {
  return it(e, `${Oe}/definitions/${encodeURIComponent(t)}`);
}
async function Xp(e, t) {
  return on(e, `${Oe}/definitions`, t);
}
async function Wp(e, t) {
  await it(e, `${Oe}/definitions/${encodeURIComponent(t)}`, {
    method: "DELETE"
  });
}
async function qp(e, t) {
  await on(e, `${Oe}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function Zp(e, t) {
  await it(e, `${Oe}/definitions/${encodeURIComponent(t)}/permanent`, {
    method: "DELETE"
  });
}
async function Up(e, t) {
  return it(e, `${Oe}/drafts/${encodeURIComponent(t.id)}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify({ state: t.state, layout: t.layout })
  });
}
async function Kp(e, t) {
  return on(e, `${Oe}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function Gp(e, t) {
  return on(e, `${Oe}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function Pa(e, t) {
  return on(e, `${Oe}/executables/${encodeURIComponent(t)}/run`, {});
}
async function Qp(e) {
  return it(e, "/_demo/workflows/executables");
}
async function ja(e) {
  return it(e, `${Oe}/activities`);
}
async function on(e, t, n) {
  return it(e, t, {
    method: "POST",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify(n)
  });
}
async function it(e, t, n) {
  const o = new URL(t, e.baseUrl).toString(), r = new AbortController(), i = window.setTimeout(() => r.abort(), ji);
  let s;
  try {
    s = await fetch(o, {
      ...n,
      signal: r.signal
    });
  } catch (l) {
    throw r.signal.aborted ? new Error(`Request to ${o} timed out after ${ji / 1e3} seconds. Check Studio:BackendBaseUrl and make sure the backend workflow-management API is responding.`) : l;
  } finally {
    window.clearTimeout(i);
  }
  const a = await s.text();
  if (!s.ok)
    throw new Error(Jp(a) || `Request failed with ${s.status}.`);
  return a ? JSON.parse(a) : {};
}
function Jp(e) {
  if (!e.trim()) return "";
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
    if (typeof t.detail == "string") return t.detail;
    if (typeof t.title == "string") return t.title;
  } catch {
    return e;
  }
  return e;
}
const fr = "elsa.sequence.structure", Un = "elsa.flowchart.structure";
function Co(e, t) {
  if (!e) return null;
  let n = e, o = We(n)[0];
  if (!o) return null;
  for (const r of t) {
    const i = We(n).find((a) => a.id === r.slotId);
    if (!i) return null;
    const s = i.activities.find((a) => a.nodeId === r.ownerNodeId);
    if (!s || (n = s, o = We(n)[0], !o)) return null;
  }
  return { owner: n, slot: o };
}
function We(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = pm(t), r = ko(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: mm(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, i]) => ko(i)).map(([i, s]) => ({
    id: `${t.kind}:${i}`,
    label: xm(i),
    property: i,
    mode: "generic",
    activities: ko(s) ?? []
  }));
}
function em(e, t, n) {
  const o = new Map(t.map((s) => [s.activityVersionId, s])), r = new Map(n.map((s) => [s.nodeId, s])), i = e.slot.activities.map((s, a) => {
    const l = o.get(s.activityVersionId), c = r.get(s.nodeId) ?? ym(e.slot.mode, a);
    return {
      id: s.nodeId,
      type: "workflowActivity",
      position: { x: c.x, y: c.y },
      data: {
        label: l?.displayName ?? s.activityVersionId,
        activityVersionId: s.activityVersionId,
        activityTypeKey: l?.activityTypeKey,
        childSlots: We(s),
        acceptsInbound: lm(s, l),
        sourcePorts: Ta(s, l)
      }
    };
  });
  return {
    nodes: i,
    edges: e.slot.mode === "flowchart" ? cm(e.owner) : am(e.slot, i)
  };
}
function Fo(e, t, n) {
  if (t.length === 0) {
    const a = We(e)[0];
    return a ? $n(e, a, n) : e;
  }
  const [o, ...r] = t, i = We(e).find((a) => a.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((a) => a.nodeId === o.ownerNodeId ? Fo(a, r, n) : a);
  return $n(e, i, s);
}
function $a(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, i = We(e).find((a) => a.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((a) => a.nodeId === o.ownerNodeId ? $a(a, r, n) : a);
  return $n(e, i, s);
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
function tm(e, t, n, o = []) {
  const r = new Map(e.slot.activities.map((s) => [s.nodeId, s]));
  for (const s of o)
    r.set(s.nodeId, s);
  const i = t.map((s) => r.get(s.id)).filter((s) => !!s);
  return e.slot.mode === "sequence" && i.sort((s, a) => {
    const l = t.find((d) => d.id === s.nodeId), c = t.find((d) => d.id === a.nodeId);
    return (l?.position.x ?? 0) - (c?.position.x ?? 0);
  }), $n(e.owner, e.slot, i);
}
function nm(e, t) {
  return {
    ...e,
    structure: sm(e.structure, t)
  };
}
function om(e, t) {
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
function $i(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: im(e)
  };
}
function Ie(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? rm(t) : n;
}
function rm(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function im(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: fr,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: Un,
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
function sm(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], o = /* @__PURE__ */ new Map();
  for (const r of n) {
    if (!hr(r)) continue;
    const i = r.id;
    typeof i == "string" && o.set(i, r);
  }
  return {
    ...e,
    payload: {
      ...e.payload,
      connections: t.map((r) => {
        const i = o.get(r.id) ?? {}, s = r.data?.vertices, { vertices: a, ...l } = i;
        return {
          ...l,
          id: r.id,
          source: { nodeId: r.source, port: r.sourceHandle ?? "Done" },
          target: r.targetHandle ? { nodeId: r.target, port: r.targetHandle } : { nodeId: r.target },
          ...s?.length ? { vertices: s.map((c) => ({ x: Math.round(c.x), y: Math.round(c.y) })) } : {}
        };
      })
    }
  };
}
function am(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function cm(e) {
  if (e.structure?.kind !== Un) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, i = n.target;
    if (!r?.nodeId || !i?.nodeId) return null;
    const s = Array.isArray(n.vertices) ? n.vertices.filter(hm) : [];
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
function Ta(e, t) {
  const n = Ti(e.cases);
  if (dm(e, t) && n.length > 0)
    return [...n.map((i) => ({ name: i, displayName: i })), { name: "Default", displayName: "Default" }];
  const o = [
    ...Sn(t?.designFacets),
    ...Sn(t?.ports),
    ...Sn(t?.outputs)
  ];
  if (o.length > 0) return fm(o);
  const r = Ti(e.outcomes);
  return r.length > 0 ? r.map((i) => ({ name: i, displayName: i })) : [{ name: "Done", displayName: "Done" }];
}
function lm(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function Tn(e, t, n, o) {
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
function um(e, t, n) {
  const o = Tn(t.source, n, t.sourceHandle ?? "Done", void 0), r = Tn(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((i) => i.id !== t.id).concat(o, r);
}
function ko(e) {
  return Array.isArray(e) ? e.filter(gm) : null;
}
function dm(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function Sn(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!hr(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...Sn(n.ports));
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
function fm(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function Ti(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function hm(e) {
  return hr(e) && typeof e.x == "number" && typeof e.y == "number";
}
function hr(e) {
  return typeof e == "object" && e !== null;
}
function gm(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function pm(e) {
  return e.kind === fr ? "sequence" : e.kind === Un ? "flowchart" : "generic";
}
function mm(e) {
  return e.kind === fr || e.kind === Un, "Activities";
}
function ym(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function xm(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const wm = { workflowActivity: Hm }, vm = { workflow: Vm }, Ri = "application/x-elsa-activity-version-id", bm = 6, Sm = 1200, Em = [10, 25, 50], _m = 10, Ra = rt.createContext(null);
function Wm(e) {
  e.navigation.add({
    id: "workflows",
    label: "Workflows",
    path: "/workflows/definitions",
    activePathPrefix: "/workflows",
    order: 20,
    iconColor: "#0ea5e9"
  }), e.routes.add({
    id: "workflows-definitions",
    path: "/workflows/definitions",
    label: "Workflow definitions",
    component: () => /* @__PURE__ */ h.jsx(Nm, { context: e.backend })
  }), e.routes.add({
    id: "workflows-executables",
    path: "/workflows/executables",
    label: "Workflow executables",
    component: () => /* @__PURE__ */ h.jsx(Cm, { context: e.backend })
  }), e.routes.add({
    id: "workflows-instances",
    path: "/workflows/instances",
    label: "Workflow instances",
    component: () => /* @__PURE__ */ h.jsx(km, {})
  });
}
function Nm({ context: e }) {
  const [t, n] = ne(zi);
  ae(() => {
    const r = () => n(zi());
    return window.addEventListener("popstate", r), () => window.removeEventListener("popstate", r);
  }, []);
  const o = (r) => {
    const i = r ? `/workflows/definitions?definition=${encodeURIComponent(r)}` : "/workflows/definitions";
    window.history.pushState({}, "", i), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return t ? /* @__PURE__ */ h.jsx(Lm, { context: e, definitionId: t, onBack: () => o(null) }) : /* @__PURE__ */ h.jsx(gr, { activePath: "/workflows/definitions", title: "Definitions", children: /* @__PURE__ */ h.jsx(Im, { context: e, onOpen: o }) });
}
function Cm({ context: e }) {
  const [t, n] = ne(Li);
  return ae(() => {
    const o = () => n(Li());
    return window.addEventListener("popstate", o), () => window.removeEventListener("popstate", o);
  }, []), /* @__PURE__ */ h.jsx(gr, { activePath: "/workflows/executables", title: "Executables", children: /* @__PURE__ */ h.jsx(Am, { context: e, definitionFilter: t }) });
}
function km() {
  return /* @__PURE__ */ h.jsx(gr, { activePath: "/workflows/instances", title: "Instances", children: /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: "Workflow instance history will appear here when the runtime exposes an instance query endpoint." }) });
}
function gr({ activePath: e, title: t, children: n }) {
  const o = (r) => {
    window.history.pushState({}, "", r), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ h.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ h.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ h.jsxs("div", { children: [
      /* @__PURE__ */ h.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ h.jsx("h2", { children: t })
    ] }) }),
    /* @__PURE__ */ h.jsxs("nav", { className: "wf-section-tabs", "aria-label": "Workflow views", children: [
      /* @__PURE__ */ h.jsx("a", { className: e === "/workflows/definitions" ? "active" : "", href: "/workflows/definitions", onClick: (r) => {
        r.preventDefault(), o("/workflows/definitions");
      }, children: "Definitions" }),
      /* @__PURE__ */ h.jsx("a", { className: e === "/workflows/executables" ? "active" : "", href: "/workflows/executables", onClick: (r) => {
        r.preventDefault(), o("/workflows/executables");
      }, children: "Executables" }),
      /* @__PURE__ */ h.jsx("a", { className: e === "/workflows/instances" ? "active" : "", href: "/workflows/instances", onClick: (r) => {
        r.preventDefault(), o("/workflows/instances");
      }, children: "Instances" })
    ] }),
    n
  ] });
}
function zi() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Li() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Im({ context: e, onOpen: t }) {
  const [n, o] = ne(""), [r, i] = ne("active"), [s, a] = ne(1), [l, c] = ne(_m), [d, u] = ne("loading"), [f, g] = ne(""), [m, v] = ne(""), [w, y] = ne([]), [S, p] = ne(0), [x, N] = ne(() => /* @__PURE__ */ new Set()), [E, M] = ne(null), [D, j] = ne(!1), [H, A] = ne([]), [R, V] = ne("idle"), b = se(null), C = pe(() => w.map((P) => P.id), [w]), _ = C.filter((P) => x.has(P)).length, I = C.length > 0 && _ === C.length, T = fe(async () => {
    u("loading"), g("");
    try {
      const P = await Fp(e, { search: n, state: r, page: s, pageSize: l }), Y = typeof P.totalCount == "number", te = P.totalCount ?? P.definitions.length, ie = za(te, l);
      if (te > 0 && s > ie) {
        a(ie);
        return;
      }
      y(Y ? P.definitions : Pm(P.definitions, s, l)), p(te), u("ready");
    } catch (P) {
      g(P instanceof Error ? P.message : String(P)), u("failed");
    }
  }, [e, n, r, s, l]);
  ae(() => {
    T();
  }, [T]), ae(() => {
    b.current && (b.current.indeterminate = _ > 0 && !I);
  }, [I, _]);
  const $ = fe(async () => {
    if (!(R === "loading" || R === "ready")) {
      V("loading");
      try {
        const P = await ja(e);
        A(P.activities ?? []), V("ready");
      } catch (P) {
        V("failed"), g(P instanceof Error ? P.message : String(P));
      }
    }
  }, [R, e]), F = () => {
    g(""), v(""), M({ name: "", description: "", rootKind: "flowchart" }), $();
  }, B = async () => {
    if (E?.name.trim()) {
      j(!0), g(""), v("");
      try {
        const P = await Xp(e, {
          name: E.name.trim(),
          description: E.description.trim() || null,
          rootKind: E.rootKind
        });
        M(null), t(P.definition.id);
      } catch (P) {
        g(P instanceof Error ? P.message : String(P));
      } finally {
        j(!1);
      }
    }
  }, O = (P) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(P)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, K = async () => {
    if (w.length === 1 && s > 1) {
      a(s - 1);
      return;
    }
    await T();
  }, W = () => N(/* @__PURE__ */ new Set()), G = (P, Y) => {
    N((te) => {
      const ie = new Set(te);
      return Y ? ie.add(P) : ie.delete(P), ie;
    });
  }, oe = (P) => {
    N((Y) => {
      const te = new Set(Y);
      for (const ie of C)
        P ? te.add(ie) : te.delete(ie);
      return te;
    });
  }, q = (P) => {
    i(P), a(1), W();
  }, L = (P) => {
    o(P), a(1), W();
  }, Z = async (P) => {
    if (window.confirm(`Delete workflow definition "${P.name}"? You can restore it from the Deleted view.`)) {
      v(""), g("");
      try {
        await Wp(e, P.id), G(P.id, !1), v(`Deleted ${P.name}`), await K();
      } catch (Y) {
        g(Y instanceof Error ? Y.message : String(Y));
      }
    }
  }, re = async (P) => {
    v(""), g("");
    try {
      await qp(e, P.id), G(P.id, !1), v(`Restored ${P.name}`), await K();
    } catch (Y) {
      g(Y instanceof Error ? Y.message : String(Y));
    }
  }, ee = async (P) => {
    if (window.confirm(`Permanently delete workflow definition "${P.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`)) {
      v(""), g("");
      try {
        await Zp(e, P.id), G(P.id, !1), v(`Permanently deleted ${P.name}`), await K();
      } catch (Y) {
        g(Y instanceof Error ? Y.message : String(Y));
      }
    }
  };
  return /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
    /* @__PURE__ */ h.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ h.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ h.jsx("button", { type: "button", className: r === "active" ? "active" : "", "aria-selected": r === "active", onClick: () => q("active"), children: "Active" }),
        /* @__PURE__ */ h.jsx("button", { type: "button", className: r === "deleted" ? "active" : "", "aria-selected": r === "deleted", onClick: () => q("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ h.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ h.jsx(Bp, { size: 15 }),
        /* @__PURE__ */ h.jsx("input", { value: n, onChange: (P) => L(P.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
        T();
      }, children: "Refresh" }),
      /* @__PURE__ */ h.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ h.jsxs("button", { type: "button", title: "Create workflow", onClick: F, children: [
        /* @__PURE__ */ h.jsx(dr, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    d === "failed" ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Ut, { size: 16 }),
      " ",
      f
    ] }) : null,
    d !== "failed" && f ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Ut, { size: 16 }),
      " ",
      f
    ] }) : null,
    m ? /* @__PURE__ */ h.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ h.jsx(Zn, { size: 14 }),
      " ",
      m
    ] }) : null,
    x.size > 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ h.jsxs("span", { children: [
        x.size,
        " selected"
      ] }),
      /* @__PURE__ */ h.jsx("button", { type: "button", onClick: W, children: "Clear selection" })
    ] }) : null,
    d === "loading" ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: "Loading workflow definitions..." }) : null,
    d === "ready" && w.length === 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-empty", children: [
      "No ",
      r,
      " workflow definitions found."
    ] }) : null,
    d === "ready" && w.length > 0 ? /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
      /* @__PURE__ */ h.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ h.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ h.jsx(
            "input",
            {
              ref: b,
              type: "checkbox",
              checked: I,
              onChange: (P) => oe(P.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ h.jsx("span", { children: "Name" }),
          /* @__PURE__ */ h.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ h.jsx("span", { children: r === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ h.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ h.jsx("span", { children: "Actions" })
        ] }),
        w.map((P) => /* @__PURE__ */ h.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${P.name}`,
            "aria-selected": x.has(P.id),
            tabIndex: 0,
            onClick: () => t(P.id),
            onKeyDown: (Y) => {
              Y.currentTarget === Y.target && (Y.key !== "Enter" && Y.key !== " " || (Y.preventDefault(), t(P.id)));
            },
            children: [
              /* @__PURE__ */ h.jsx("label", { className: "wf-row-select", onClick: (Y) => Y.stopPropagation(), children: /* @__PURE__ */ h.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: x.has(P.id),
                  onChange: (Y) => G(P.id, Y.target.checked),
                  "aria-label": `Select workflow definition ${P.name}`
                }
              ) }),
              /* @__PURE__ */ h.jsxs("span", { children: [
                /* @__PURE__ */ h.jsx("strong", { children: P.name }),
                /* @__PURE__ */ h.jsx("small", { children: P.description || P.id })
              ] }),
              /* @__PURE__ */ h.jsx("span", { children: P.latestVersion ?? "No version" }),
              /* @__PURE__ */ h.jsx("span", { children: r === "deleted" ? Yo(P.deletedAt) : P.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ h.jsx("span", { children: Yo(P.lastModifiedAt) }),
              /* @__PURE__ */ h.jsx("span", { className: "wf-row-actions", onClick: (Y) => Y.stopPropagation(), children: r === "active" ? /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
                /* @__PURE__ */ h.jsx("button", { type: "button", onClick: (Y) => {
                  Y.stopPropagation(), t(P.id);
                }, children: "Open" }),
                /* @__PURE__ */ h.jsx("button", { type: "button", onClick: (Y) => {
                  Y.stopPropagation(), O(P.id);
                }, children: "Artifacts" }),
                /* @__PURE__ */ h.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  Z(P);
                }, children: [
                  /* @__PURE__ */ h.jsx(Bo, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
                /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
                  re(P);
                }, children: [
                  /* @__PURE__ */ h.jsx(Vp, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ h.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  ee(P);
                }, children: [
                  /* @__PURE__ */ h.jsx(Bo, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          P.id
        ))
      ] }),
      /* @__PURE__ */ h.jsx(
        Dm,
        {
          page: s,
          pageSize: l,
          totalCount: S,
          onPageChange: a,
          onPageSizeChange: (P) => {
            c(P), a(1);
          }
        }
      )
    ] }) : null,
    E ? /* @__PURE__ */ h.jsx(
      Mm,
      {
        draft: E,
        activities: H,
        catalogState: R,
        creating: D,
        onChange: (P) => M(P),
        onClose: () => M(null),
        onSubmit: B
      }
    ) : null
  ] });
}
function Mm({ draft: e, activities: t, catalogState: n, creating: o, onChange: r, onClose: i, onSubmit: s }) {
  const a = pe(() => jm(t), [t]);
  return /* @__PURE__ */ h.jsx("div", { className: "wf-dialog-backdrop", role: "presentation", children: /* @__PURE__ */ h.jsx("section", { className: "wf-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "workflow-create-title", children: /* @__PURE__ */ h.jsxs(
    "form",
    {
      onSubmit: (l) => {
        l.preventDefault(), s();
      },
      children: [
        /* @__PURE__ */ h.jsx("div", { className: "wf-dialog-heading", children: /* @__PURE__ */ h.jsx("h3", { id: "workflow-create-title", children: "Create Workflow" }) }),
        /* @__PURE__ */ h.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ h.jsx("span", { children: "Display name" }),
          /* @__PURE__ */ h.jsx(
            "input",
            {
              autoFocus: !0,
              "aria-label": "Display name",
              value: e.name,
              onChange: (l) => r({ ...e, name: l.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ h.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ h.jsx("span", { children: "Description" }),
          /* @__PURE__ */ h.jsx(
            "textarea",
            {
              "aria-label": "Description",
              rows: 3,
              value: e.description,
              onChange: (l) => r({ ...e, description: l.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ h.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ h.jsx("span", { children: "Root activity" }),
          /* @__PURE__ */ h.jsxs(
            "select",
            {
              "aria-label": "Root activity",
              value: e.rootKind,
              onChange: (l) => r({ ...e, rootKind: l.target.value }),
              disabled: n === "loading",
              children: [
                /* @__PURE__ */ h.jsx("optgroup", { label: "Composite roots", children: a.compositeRoots.map((l) => /* @__PURE__ */ h.jsx("option", { value: l.value, children: l.label }, l.value)) }),
                a.otherCategories.map((l) => /* @__PURE__ */ h.jsx("optgroup", { label: l.name, children: l.activities.map((c) => /* @__PURE__ */ h.jsx("option", { value: `unsupported:${c.activityVersionId}`, disabled: !0, children: Ie(c) }, c.activityVersionId)) }, l.name))
              ]
            }
          )
        ] }),
        n === "loading" ? /* @__PURE__ */ h.jsx("div", { className: "wf-dialog-note", children: "Loading activity catalog..." }) : null,
        n === "failed" ? /* @__PURE__ */ h.jsx("div", { className: "wf-dialog-note", children: "Activity catalog could not be loaded. Composite roots remain available." }) : null,
        /* @__PURE__ */ h.jsxs("div", { className: "wf-dialog-actions", children: [
          /* @__PURE__ */ h.jsx("button", { type: "button", onClick: i, disabled: o, children: "Cancel" }),
          /* @__PURE__ */ h.jsx("button", { type: "submit", disabled: o || !e.name.trim(), children: o ? "Creating..." : "Create" })
        ] })
      ]
    }
  ) }) });
}
function Am({ context: e, definitionFilter: t }) {
  const [n, o] = ne("loading"), [r, i] = ne(""), [s, a] = ne(""), [l, c] = ne([]), d = pe(
    () => t ? l.filter((g) => g.definitionId === t || g.sourceId === t) : l,
    [t, l]
  ), u = fe(async () => {
    o("loading"), i("");
    try {
      c(await Qp(e)), o("ready");
    } catch (g) {
      i(g instanceof Error ? g.message : String(g)), o("failed");
    }
  }, [e]);
  ae(() => {
    u();
  }, [u]);
  const f = async (g) => {
    a(""), i("");
    try {
      await Pa(e, g.artifactId), a(`Started ${g.artifactId}`);
    } catch (m) {
      i(m instanceof Error ? m.message : String(m));
    }
  };
  return /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
    /* @__PURE__ */ h.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
        u();
      }, children: "Refresh" }),
      t ? /* @__PURE__ */ h.jsxs("span", { className: "wf-filter-chip", children: [
        "Definition ",
        t
      ] }) : null
    ] }),
    n === "failed" ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Ut, { size: 16 }),
      " ",
      r
    ] }) : null,
    s ? /* @__PURE__ */ h.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ h.jsx(Zn, { size: 14 }),
      " ",
      s
    ] }) : null,
    n === "loading" ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: "Loading workflow executables..." }) : null,
    n === "ready" && d.length === 0 ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: t ? "No workflow executables found for this definition." : "No workflow executables found. Publish a workflow definition to create one." }) : null,
    n === "ready" && d.length > 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ h.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ h.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ h.jsx("span", { children: "Version" }),
        /* @__PURE__ */ h.jsx("span", { children: "Source" }),
        /* @__PURE__ */ h.jsx("span", { children: "Root" }),
        /* @__PURE__ */ h.jsx("span", { children: "Published" }),
        /* @__PURE__ */ h.jsx("span", { children: "Actions" })
      ] }),
      d.map((g) => /* @__PURE__ */ h.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ h.jsxs("span", { children: [
          /* @__PURE__ */ h.jsx("strong", { children: g.artifactId }),
          /* @__PURE__ */ h.jsx("small", { children: g.artifactHash })
        ] }),
        /* @__PURE__ */ h.jsx("span", { children: g.artifactVersion }),
        /* @__PURE__ */ h.jsx("span", { children: Tm(g) }),
        /* @__PURE__ */ h.jsx("span", { children: Rm(g) }),
        /* @__PURE__ */ h.jsx("span", { children: Yo(g.publishedAt ?? g.createdAt) }),
        /* @__PURE__ */ h.jsx("span", { className: "wf-row-actions", children: /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
          f(g);
        }, children: [
          /* @__PURE__ */ h.jsx(Da, { size: 13 }),
          " Run"
        ] }) })
      ] }, g.artifactId))
    ] }) : null
  ] });
}
function Dm({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const i = za(n, t), s = n === 0 ? 0 : (e - 1) * t + 1, a = Math.min(e * t, n);
  return /* @__PURE__ */ h.jsxs("div", { className: "wf-pagination", "aria-label": "Workflow definition pagination", children: [
    /* @__PURE__ */ h.jsxs("span", { className: "wf-pagination-summary", "aria-live": "polite", children: [
      "Showing ",
      s,
      "-",
      a,
      " of ",
      n
    ] }),
    /* @__PURE__ */ h.jsxs("label", { className: "wf-page-size", children: [
      "Rows",
      /* @__PURE__ */ h.jsx("select", { value: t, onChange: (l) => r(Number(l.target.value)), children: Em.map((l) => /* @__PURE__ */ h.jsx("option", { value: l, children: l }, l)) })
    ] }),
    /* @__PURE__ */ h.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ h.jsx(zp, { size: 14 }),
        " Previous"
      ] }),
      /* @__PURE__ */ h.jsxs("span", { children: [
        "Page ",
        e,
        " of ",
        i
      ] }),
      /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => o(e + 1), disabled: e >= i, "aria-label": "Next page", title: "Next page", children: [
        "Next ",
        /* @__PURE__ */ h.jsx(bn, { size: 14 })
      ] })
    ] })
  ] });
}
function Pm(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function za(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function jm(e) {
  const t = [
    { value: "flowchart", label: "Flowchart" },
    { value: "sequence", label: "Sequence" }
  ], n = /* @__PURE__ */ new Map();
  for (const r of e.filter(Ha)) {
    if ($m(r)) continue;
    const i = r.category || "Uncategorized";
    n.set(i, [...n.get(i) ?? [], r]);
  }
  const o = Array.from(n.entries()).sort(([r], [i]) => r.localeCompare(i)).map(([r, i]) => ({
    name: r,
    activities: i.sort((s, a) => Ie(s).localeCompare(Ie(a)))
  }));
  return { compositeRoots: t, otherCategories: o };
}
function La(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "Uncategorized";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [o]) => n.localeCompare(o)).map(([n, o]) => ({
    category: n,
    activities: o.sort((r, i) => Ie(r).localeCompare(Ie(i)))
  }));
}
function $m(e) {
  const t = Ie(e);
  return t === "Flowchart" || t === "Sequence" || e.activityTypeKey.endsWith(".Flowchart") || e.activityTypeKey.endsWith(".Sequence");
}
function Ha(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function Tm(e) {
  return e.sourceKind || e.sourceId || e.sourceVersion ? [e.sourceKind, e.sourceId, e.sourceVersion].filter(Boolean).join(" / ") : e.definitionId;
}
function Rm(e) {
  return zm(e.rootActivityType) || e.rootActivityType;
}
function zm(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function Lm({ context: e, definitionId: t, onBack: n }) {
  const [o, r] = ne(null), [i, s] = ne(null), [a, l] = ne([]), [c, d] = ne([]), [u, f] = ne([]), [g, m] = ne([]), [v, w] = ne(null), [y, S] = ne(null), [p, x] = ne(null), [N, E] = ne(null), [M, D] = ne(""), [j, H] = ne(""), [A, R] = ne(!1), [V, b] = ne(null), [C, _] = ne(() => /* @__PURE__ */ new Set()), I = se(null), T = se(null), $ = se(""), F = se(0), B = se(null), O = se(!1), K = i?.state.rootActivity ?? null, W = pe(() => Co(K, c), [K, c]), G = pe(() => new Map(a.map((k) => [k.activityVersionId, k])), [a]), oe = pe(() => La(a), [a]), q = pe(() => W?.slot.activities.find((k) => k.nodeId === y) ?? null, [W, y]), L = q ? We(q) : [], Z = fe(async () => {
    D("");
    const [k, z] = await Promise.all([
      Yp(e, t),
      ja(e)
    ]), X = k.draft ?? null;
    r(k), $.current = X ? Tt(X) : "", s(X), l(z.activities ?? []), d([]), S(null);
  }, [e, t]);
  ae(() => {
    Z().catch((k) => D(k instanceof Error ? k.message : String(k)));
  }, [Z]), ae(() => {
    _((k) => {
      let z = !1;
      const X = new Set(k);
      for (const U of oe)
        X.has(U.category) || (X.add(U.category), z = !0);
      return z ? X : k;
    });
  }, [oe]), ae(() => {
    if (!W) {
      f([]), m([]);
      return;
    }
    const k = em(W, a, i?.layout ?? []);
    f(k.nodes), m(k.edges);
  }, [W, a, i?.layout]);
  const re = (k) => {
    s((z) => z && { ...z, state: { ...z.state, rootActivity: k } });
  }, ee = fe((k, z) => {
    const X = $i(k, Hi(k));
    if (!i?.state.rootActivity) {
      re(X), S(X.nodeId);
      return;
    }
    if (!W) {
      if (!We(X)[0]) {
        H(""), D("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      s((J) => {
        if (!J?.state.rootActivity) return J;
        const ue = J.state.rootActivity, le = Fo(X, [], [ue]), we = z ? [
          ...J.layout.filter((Ce) => Ce.nodeId !== ue.nodeId),
          {
            nodeId: ue.nodeId,
            x: Math.round(z.x),
            y: Math.round(z.y)
          }
        ] : J.layout;
        return {
          ...J,
          layout: we,
          state: {
            ...J.state,
            rootActivity: le
          }
        };
      }), S(i.state.rootActivity.nodeId), D(""), H(`Wrapped root in ${Ie(k)}`);
      return;
    }
    s((U) => {
      if (!U?.state.rootActivity) return U;
      const J = Co(U.state.rootActivity, c);
      if (!J) return U;
      const ue = Fo(U.state.rootActivity, c, [...J.slot.activities, X]), le = z ? [
        ...U.layout.filter((we) => we.nodeId !== X.nodeId),
        {
          nodeId: X.nodeId,
          x: Math.round(z.x),
          y: Math.round(z.y)
        }
      ] : U.layout;
      return {
        ...U,
        layout: le,
        state: {
          ...U.state,
          rootActivity: ue
        }
      };
    }), S(X.nodeId);
  }, [i?.state.rootActivity, c, W]), P = fe((k, z) => {
    const X = $i(k, Hi(k)), U = {
      id: X.nodeId,
      type: "workflowActivity",
      position: z,
      selected: !0,
      data: {
        label: Ie(k),
        activityVersionId: k.activityVersionId,
        activityTypeKey: k.activityTypeKey,
        childSlots: We(X),
        acceptsInbound: String(k.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: Ta(X, k)
      }
    };
    return { activityNode: X, node: U };
  }, []), Y = fe((k, z, X = []) => {
    s((U) => {
      if (!U) return U;
      const J = om(U.layout, k), ue = U.state.rootActivity;
      if (!ue) return { ...U, layout: J };
      const le = Co(ue, c);
      if (!le) return { ...U, layout: J };
      const we = tm(le, k, z, X), Ce = le.slot.mode === "flowchart" ? nm(we, z) : we;
      return {
        ...U,
        layout: J,
        state: {
          ...U.state,
          rootActivity: $a(ue, c, Ce)
        }
      };
    });
  }, [c]), te = fe((k, z) => {
    if (!I.current) return null;
    const X = I.current.getBoundingClientRect();
    return v ? v.screenToFlowPosition({ x: k, y: z }) : {
      x: k - X.left,
      y: z - X.top
    };
  }, [v]), ie = fe((k, z) => document.elementFromPoint(k, z)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), de = fe((k, z, X) => {
    const U = u.find((be) => be.id === z.source), J = u.find((be) => be.id === z.target), ue = U && J ? Fm(U, J) : U ? Vi(U) : X, le = P(k, ue), Ce = [...u.map((be) => be.selected ? { ...be, selected: !1 } : be), le.node], st = um(g, z, le.node.id);
    f(Ce), m(st), S(le.node.id), Y(Ce, st, [le.activityNode]);
  }, [Y, P, g, u]), xe = fe((k, z, X) => {
    if (!I.current) return !1;
    const U = I.current.getBoundingClientRect();
    if (!(z >= U.left && z <= U.right && X >= U.top && X <= U.bottom)) return !1;
    const ue = te(z, X);
    if (!ue) return !1;
    if (W?.slot.mode === "flowchart") {
      const le = ie(z, X), we = le ? g.find((Ce) => Ce.id === le) : void 0;
      if (we)
        return de(k, we, ue), !0;
    }
    return ee(k, ue), !0;
  }, [ee, g, ie, W?.slot.mode, de, te]);
  ae(() => {
    const k = (X) => {
      const U = B.current;
      if (!U) return;
      Math.hypot(X.clientX - U.startX, X.clientY - U.startY) >= bm && (U.dragging = !0);
    }, z = (X) => {
      const U = B.current;
      if (B.current = null, !U?.dragging || !I.current) return;
      const J = I.current.getBoundingClientRect();
      X.clientX >= J.left && X.clientX <= J.right && X.clientY >= J.top && X.clientY <= J.bottom && (O.current = !0, window.setTimeout(() => {
        O.current = !1;
      }, 0), xe(U.activity, X.clientX, X.clientY));
    };
    return window.addEventListener("pointermove", k), window.addEventListener("pointerup", z), window.addEventListener("pointercancel", z), () => {
      window.removeEventListener("pointermove", k), window.removeEventListener("pointerup", z), window.removeEventListener("pointercancel", z);
    };
  }, [v, xe]);
  const je = (k, z) => {
    k.dataTransfer.setData(Ri, z.activityVersionId), k.dataTransfer.setData("text/plain", z.activityVersionId), k.dataTransfer.effectAllowed = "copy";
  }, De = (k, z) => {
    k.clientX === 0 && k.clientY === 0 || xe(z, k.clientX, k.clientY) && (O.current = !0, window.setTimeout(() => {
      O.current = !1;
    }, 0));
  }, Pe = (k, z) => {
    k.button === 0 && (B.current = {
      activity: z,
      startX: k.clientX,
      startY: k.clientY,
      dragging: !1
    });
  }, $e = (k) => {
    O.current || ee(k);
  }, Je = (k) => {
    if (k.preventDefault(), k.dataTransfer.dropEffect = "copy", W?.slot.mode !== "flowchart") return;
    const z = ie(k.clientX, k.clientY);
    E(z);
  }, Be = (k) => {
    if (!I.current) return;
    const z = k.relatedTarget;
    z && I.current.contains(z) || E(null);
  }, Fe = (k) => {
    k.preventDefault(), E(null);
    const z = k.dataTransfer.getData(Ri) || k.dataTransfer.getData("text/plain"), X = G.get(z);
    X && xe(X, k.clientX, k.clientY);
  }, _e = () => {
    const k = I.current?.getBoundingClientRect();
    k && x({
      kind: "fromEmpty",
      clientX: k.left + k.width / 2,
      clientY: k.top + k.height / 2
    });
  }, Ne = fe(async (k, z) => {
    const X = ++F.current, U = Tt(k);
    D("");
    try {
      const J = await Up(e, k), ue = Tt(J);
      $.current = ue, s((le) => !le || le.id !== J.id ? le : Tt(le) === U ? J : { ...le, validationErrors: J.validationErrors }), X === F.current && H(z);
    } catch (J) {
      X === F.current && (H(""), D(J instanceof Error ? J.message : String(J)));
    }
  }, [e]);
  ae(() => {
    if (!A || !i || Tt(i) === $.current) return;
    H("Autosaving...");
    const z = window.setTimeout(() => {
      Ne(i, "Autosaved");
    }, Sm);
    return () => window.clearTimeout(z);
  }, [A, i, Ne]);
  const Te = async () => {
    i && (H("Saving..."), await Ne(i, "Saved"));
  }, Ze = async () => {
    if (i) {
      H("Promoting...");
      try {
        const k = await Kp(e, i.id), z = await Gp(e, k.versionId);
        b(z.artifactId), H(`Published ${z.artifactVersion}`), await Z();
      } catch (k) {
        H(""), D(k instanceof Error ? k.message : String(k));
      }
    }
  }, Kn = async () => {
    if (V) {
      H("Running...");
      try {
        await Pa(e, V), H("Run dispatched");
      } catch (k) {
        H(""), D(k instanceof Error ? k.message : String(k));
      }
    }
  }, At = (k) => f((z) => Js(k, z)), Gn = (k) => m((z) => ea(k, z)), Dt = (k) => !k.source || !k.target || k.source === k.target || W?.slot.mode !== "flowchart" ? !1 : !k.targetHandle, Qn = (k) => {
    if (!i?.state.rootActivity || !W || W.slot.mode !== "flowchart" || !Dt(k)) return;
    const z = Tn(k.source, k.target, k.sourceHandle ?? "Done", k.targetHandle ?? void 0), X = na(z, g);
    m(X), Y(u, X);
  }, Jn = () => {
    Y(u, g);
  }, eo = (k, z) => {
    if (!z.nodeId || z.handleType === "target") {
      T.current = null;
      return;
    }
    T.current = {
      nodeId: z.nodeId,
      handleId: z.handleId ?? null
    };
  }, to = (k) => {
    const z = T.current;
    if (T.current = null, !z || W?.slot.mode !== "flowchart" || k.target?.closest(".react-flow__handle, .react-flow__node")) return;
    const U = Ym(k);
    x({
      kind: "fromPort",
      sourceNodeId: z.nodeId,
      sourceHandleId: z.handleId,
      clientX: U.x,
      clientY: U.y
    });
  }, no = (k, z) => {
    if (!Dt(z)) return;
    const X = jh(k, {
      ...z,
      sourceHandle: z.sourceHandle ?? "Done",
      targetHandle: z.targetHandle ?? void 0
    }, g, { shouldReplaceId: !1 });
    m(X), Y(u, X);
  }, oo = (k) => {
    if (k.length === 0) return;
    const z = new Set(k.map((J) => J.id)), X = u.filter((J) => !z.has(J.id)), U = g.filter((J) => !z.has(J.source) && !z.has(J.target));
    f(X), m(U), y && z.has(y) && S(null), Y(X, U);
  }, ro = (k) => {
    if (k.length === 0) return;
    const z = new Set(k.map((U) => U.id)), X = g.filter((U) => !z.has(U.id));
    m(X), Y(u, X);
  }, rn = fe((k) => {
    const z = g.filter((X) => X.id !== k);
    m(z), Y(u, z);
  }, [Y, g, u]), sn = fe((k, z, X) => {
    x({ kind: "spliceEdge", edgeId: k, clientX: z, clientY: X });
  }, []), io = (k) => {
    const z = p;
    if (!z) return;
    x(null);
    const X = te(z.clientX, z.clientY) ?? { x: 0, y: 0 };
    if (z.kind === "fromEmpty") {
      const J = P(k, X), le = [...u.map((we) => we.selected ? { ...we, selected: !1 } : we), J.node];
      f(le), S(J.node.id), Y(le, g, [J.activityNode]);
      return;
    }
    if (z.kind === "fromPort") {
      const J = u.find((be) => be.id === z.sourceNodeId), ue = J ? Vi(J) : X, le = P(k, ue), Ce = [...u.map((be) => be.selected ? { ...be, selected: !1 } : be), le.node], st = [...g, Tn(z.sourceNodeId, le.node.id, z.sourceHandleId ?? "Done")];
      f(Ce), m(st), S(le.node.id), Y(Ce, st, [le.activityNode]);
      return;
    }
    const U = g.find((J) => J.id === z.edgeId);
    U && de(k, U, X);
  }, so = pe(() => ({
    highlightedEdgeId: N,
    deleteEdge: rn,
    requestInsertActivity: sn
  }), [rn, N, sn]), ao = (k, z, X) => {
    d((U) => [...U, { ownerNodeId: k.nodeId, slotId: z, label: X }]), S(null);
  }, co = (k) => {
    _((z) => {
      const X = new Set(z);
      return X.has(k) ? X.delete(k) : X.add(k), X;
    });
  };
  return !o || !i ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: M || "Loading workflow editor..." }) : /* @__PURE__ */ h.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ h.jsx("button", { type: "button", className: "wf-link-button", onClick: n, children: "Definitions" }),
      /* @__PURE__ */ h.jsx(bn, { size: 14 }),
      /* @__PURE__ */ h.jsx("strong", { children: o.definition.name }),
      /* @__PURE__ */ h.jsx("span", { className: "wf-chip", children: "Draft" }),
      j ? /* @__PURE__ */ h.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ h.jsx(Zn, { size: 13 }),
        " ",
        j
      ] }) : null,
      /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ h.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ h.jsx("input", { type: "checkbox", checked: A, onChange: (k) => R(k.target.checked) }),
          /* @__PURE__ */ h.jsx("span", { children: "Autosave" })
        ] }),
        /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
          Te();
        }, children: [
          /* @__PURE__ */ h.jsx(Op, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
          Ze();
        }, children: [
          /* @__PURE__ */ h.jsx(Lp, { size: 15 }),
          " Promote"
        ] }),
        /* @__PURE__ */ h.jsxs("button", { type: "button", disabled: !V, onClick: () => {
          Kn();
        }, children: [
          /* @__PURE__ */ h.jsx(Da, { size: 15 }),
          " Run"
        ] })
      ] })
    ] }),
    M ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Ut, { size: 16 }),
      " ",
      M
    ] }) : null,
    /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-body", children: [
      /* @__PURE__ */ h.jsxs("aside", { className: "wf-palette", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ h.jsx(Tp, { size: 15 }),
          " Activities"
        ] }),
        /* @__PURE__ */ h.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: oe.map((k) => {
          const z = C.has(k.category);
          return /* @__PURE__ */ h.jsxs("div", { className: "wf-palette-category", children: [
            /* @__PURE__ */ h.jsxs(
              "button",
              {
                type: "button",
                className: "wf-palette-category-toggle",
                role: "treeitem",
                "aria-expanded": z,
                onClick: () => co(k.category),
                children: [
                  z ? /* @__PURE__ */ h.jsx(Rp, { size: 14 }) : /* @__PURE__ */ h.jsx(bn, { size: 14 }),
                  /* @__PURE__ */ h.jsx("span", { children: k.category }),
                  /* @__PURE__ */ h.jsx("small", { children: k.activities.length })
                ]
              }
            ),
            z ? /* @__PURE__ */ h.jsx("div", { className: "wf-palette-activities", role: "group", children: k.activities.map((X) => {
              const U = X.description?.trim(), J = U ? `wf-palette-description-${X.activityVersionId}` : void 0;
              return /* @__PURE__ */ h.jsxs(
                "button",
                {
                  type: "button",
                  className: "wf-palette-activity",
                  role: "treeitem",
                  draggable: !0,
                  title: U || Ie(X),
                  "aria-describedby": J,
                  onClick: () => $e(X),
                  onDragStart: (ue) => je(ue, X),
                  onDragEnd: (ue) => De(ue, X),
                  onPointerDown: (ue) => Pe(ue, X),
                  children: [
                    /* @__PURE__ */ h.jsx("strong", { children: Ie(X) }),
                    U ? /* @__PURE__ */ h.jsx("small", { id: J, children: U }) : null
                  ]
                },
                X.activityVersionId
              );
            }) }) : null
          ] }, k.category);
        }) })
      ] }),
      /* @__PURE__ */ h.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-breadcrumb", children: [
          /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
            d([]), S(null);
          }, children: "Root" }),
          c.map((k, z) => /* @__PURE__ */ h.jsxs(rt.Fragment, { children: [
            /* @__PURE__ */ h.jsx(bn, { size: 13 }),
            /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
              d(c.slice(0, z + 1)), S(null);
            }, children: k.label })
          ] }, `${k.ownerNodeId}-${k.slotId}-${z}`))
        ] }),
        /* @__PURE__ */ h.jsxs("div", { className: "wf-canvas", ref: I, onDragOver: Je, onDragLeave: Be, onDrop: Fe, children: [
          /* @__PURE__ */ h.jsx(Ra.Provider, { value: so, children: /* @__PURE__ */ h.jsxs(
            tp,
            {
              nodes: u,
              edges: g,
              nodeTypes: wm,
              edgeTypes: vm,
              onInit: w,
              onNodesChange: At,
              onEdgesChange: Gn,
              onNodesDelete: oo,
              onEdgesDelete: ro,
              onConnect: Qn,
              onConnectStart: W?.slot.mode === "flowchart" ? eo : void 0,
              onConnectEnd: W?.slot.mode === "flowchart" ? to : void 0,
              onReconnect: W?.slot.mode === "flowchart" ? no : void 0,
              isValidConnection: Dt,
              onDragOver: Je,
              onDragLeave: Be,
              onDrop: Fe,
              onPaneClick: () => S(null),
              onNodeClick: (k, z) => S(z.id),
              onNodeDragStop: Jn,
              fitView: !0,
              minZoom: 0.2,
              maxZoom: 1.8,
              nodesConnectable: W?.slot.mode === "flowchart",
              selectionOnDrag: !0,
              multiSelectionKeyCode: ["Shift", "Meta", "Control"],
              deleteKeyCode: ["Backspace", "Delete"],
              panActivationKeyCode: null,
              defaultEdgeOptions: { type: "workflow" },
              children: [
                /* @__PURE__ */ h.jsx(cp, { gap: 18, size: 1 }),
                /* @__PURE__ */ h.jsx(pp, {}),
                /* @__PURE__ */ h.jsx(Ip, { pannable: !0, zoomable: !0 })
              ]
            }
          ) }),
          W?.slot.mode === "flowchart" && u.length === 0 ? /* @__PURE__ */ h.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => _e(), children: [
            /* @__PURE__ */ h.jsx(dr, { size: 15 }),
            " Add activity"
          ] }) : null,
          p ? /* @__PURE__ */ h.jsx(
            Om,
            {
              clientX: p.clientX,
              clientY: p.clientY,
              activities: a,
              onPick: io,
              onClose: () => x(null)
            }
          ) : null
        ] }),
        /* @__PURE__ */ h.jsx(Bm, { draft: i })
      ] }),
      /* @__PURE__ */ h.jsxs("aside", { className: "wf-inspector", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ h.jsx(Hp, { size: 15 }),
          " Inspector"
        ] }),
        q ? /* @__PURE__ */ h.jsxs("div", { className: "wf-inspector-content", children: [
          /* @__PURE__ */ h.jsx("h3", { children: u.find((k) => k.id === q.nodeId)?.data.label ?? q.nodeId }),
          /* @__PURE__ */ h.jsxs("dl", { children: [
            /* @__PURE__ */ h.jsx("dt", { children: "Node ID" }),
            /* @__PURE__ */ h.jsx("dd", { children: q.nodeId }),
            /* @__PURE__ */ h.jsx("dt", { children: "Activity version" }),
            /* @__PURE__ */ h.jsx("dd", { children: q.activityVersionId })
          ] }),
          L.length > 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-slot-list", children: [
            /* @__PURE__ */ h.jsx("span", { children: "Embedded slots" }),
            L.map((k) => /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => ao(q, k.id, `${u.find((z) => z.id === q.nodeId)?.data.label ?? q.nodeId} / ${k.label}`), children: [
              k.label,
              /* @__PURE__ */ h.jsxs("small", { children: [
                k.activities.length,
                " activit",
                k.activities.length === 1 ? "y" : "ies"
              ] })
            ] }, k.id))
          ] }) : /* @__PURE__ */ h.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
        ] }) : /* @__PURE__ */ h.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." })
      ] })
    ] })
  ] });
}
function Hm({ data: e, selected: t }) {
  const n = e, o = n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }];
  return /* @__PURE__ */ h.jsxs("div", { className: t ? "wf-node selected" : "wf-node", children: [
    n.acceptsInbound ? /* @__PURE__ */ h.jsx(It, { type: "target", position: Q.Left }) : null,
    /* @__PURE__ */ h.jsx("strong", { children: n.label }),
    /* @__PURE__ */ h.jsx("small", { children: n.activityTypeKey ?? n.activityVersionId }),
    n.childSlots.length > 0 ? /* @__PURE__ */ h.jsxs("span", { children: [
      n.childSlots.length,
      " embedded slot",
      n.childSlots.length === 1 ? "" : "s"
    ] }) : null,
    o.map((r, i) => {
      const s = `${(i + 1) / (o.length + 1) * 100}%`;
      return /* @__PURE__ */ h.jsxs(rt.Fragment, { children: [
        /* @__PURE__ */ h.jsx("span", { className: "wf-node-port-label", style: { top: s }, children: r.displayName }),
        /* @__PURE__ */ h.jsx(It, { type: "source", position: Q.Right, id: r.name, style: { top: s } })
      ] }, r.name);
    })
  ] });
}
function Vm(e) {
  const {
    id: t,
    sourceX: n,
    sourceY: o,
    targetX: r,
    targetY: i,
    sourcePosition: s,
    targetPosition: a,
    markerEnd: l,
    style: c,
    label: d,
    labelStyle: u
  } = e, f = rt.useContext(Ra), [g, m] = ne(!1), [v, w, y] = Pn({ sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: a }), S = f?.highlightedEdgeId === t;
  return /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
    /* @__PURE__ */ h.jsx(
      nn,
      {
        id: t,
        path: v,
        markerEnd: l,
        style: {
          ...c,
          strokeWidth: S ? 2.5 : c?.strokeWidth
        },
        label: d,
        labelX: w,
        labelY: y,
        labelStyle: u,
        onMouseEnter: () => m(!0),
        onMouseLeave: () => m(!1)
      }
    ),
    f ? /* @__PURE__ */ h.jsx(op, { children: /* @__PURE__ */ h.jsxs(
      "div",
      {
        className: ["wf-edge-actions", g ? "visible" : "", S ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${w}px, ${y}px)` },
        onMouseEnter: () => m(!0),
        onMouseLeave: () => m(!1),
        children: [
          /* @__PURE__ */ h.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (p) => f.requestInsertActivity(t, p.clientX, p.clientY), children: /* @__PURE__ */ h.jsx(dr, { size: 12 }) }),
          /* @__PURE__ */ h.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => f.deleteEdge(t), children: /* @__PURE__ */ h.jsx(Bo, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function Om({ clientX: e, clientY: t, activities: n, onPick: o, onClose: r }) {
  const [i, s] = ne(""), [a, l] = ne(0), c = se(null), d = se(null), u = pe(() => {
    const S = i.trim().toLowerCase(), p = n.filter(Ha);
    return S ? p.filter((x) => Ie(x).toLowerCase().includes(S) || x.activityTypeKey.toLowerCase().includes(S) || (x.category ?? "").toLowerCase().includes(S) || (x.description ?? "").toLowerCase().includes(S)) : p;
  }, [n, i]), f = pe(() => La(u), [u]), g = pe(() => f.flatMap((S) => S.activities), [f]);
  ae(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), ae(() => {
    const S = (x) => {
      c.current?.contains(x.target) || r();
    }, p = (x) => {
      x.key === "Escape" && r();
    };
    return document.addEventListener("mousedown", S, !0), document.addEventListener("keydown", p), () => {
      document.removeEventListener("mousedown", S, !0), document.removeEventListener("keydown", p);
    };
  }, [r]);
  const m = (S) => {
    if (S.key === "ArrowDown")
      S.preventDefault(), l((p) => Math.min(p + 1, g.length - 1));
    else if (S.key === "ArrowUp")
      S.preventDefault(), l((p) => Math.max(p - 1, 0));
    else if (S.key === "Enter") {
      S.preventDefault();
      const p = g[a];
      p && o(p);
    }
  }, v = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), w = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let y = -1;
  return /* @__PURE__ */ h.jsxs("div", { ref: c, className: "wf-connect-menu", style: { left: v, top: w }, onMouseDown: (S) => S.stopPropagation(), onClick: (S) => S.stopPropagation(), children: [
    /* @__PURE__ */ h.jsx(
      "input",
      {
        ref: d,
        type: "search",
        value: i,
        placeholder: "Search activities...",
        "aria-label": "Search activities",
        onChange: (S) => {
          s(S.target.value), l(0);
        },
        onKeyDown: m
      }
    ),
    /* @__PURE__ */ h.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: f.length === 0 ? /* @__PURE__ */ h.jsx("p", { children: "No matching activities." }) : f.map((S) => /* @__PURE__ */ h.jsxs("section", { children: [
      /* @__PURE__ */ h.jsx("h4", { children: S.category }),
      S.activities.map((p) => {
        y += 1;
        const x = y, N = x === a;
        return /* @__PURE__ */ h.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": N,
            className: N ? "active" : "",
            onMouseEnter: () => l(x),
            onClick: () => o(p),
            children: [
              /* @__PURE__ */ h.jsx("strong", { children: Ie(p) }),
              /* @__PURE__ */ h.jsx("small", { children: p.category || p.activityTypeKey })
            ]
          },
          p.activityVersionId
        );
      })
    ] }, S.category)) })
  ] });
}
function Bm({ draft: e }) {
  return e.validationErrors.length ? /* @__PURE__ */ h.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ h.jsx(Ut, { size: 14 }),
    e.validationErrors.length,
    " validation issue",
    e.validationErrors.length === 1 ? "" : "s"
  ] }) : /* @__PURE__ */ h.jsxs("div", { className: "wf-validation ok", children: [
    /* @__PURE__ */ h.jsx(Zn, { size: 14 }),
    " No validation errors"
  ] });
}
function Hi(e) {
  return `${Ie(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function Vi(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function Fm(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function Ym(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function Tt(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function Yo(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
export {
  Wm as register
};
