import Tt, { memo as fe, forwardRef as gn, useRef as ne, useEffect as ie, useCallback as xe, useContext as zt, useMemo as ve, createContext as co, useState as ce, useLayoutEffect as Ka, createElement as qn } from "react";
function Qa(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var An = { exports: {} }, yt = {};
var Oo;
function Ja() {
  if (Oo) return yt;
  Oo = 1;
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
  return yt.Fragment = t, yt.jsx = n, yt.jsxs = n, yt;
}
var Vo;
function ec() {
  return Vo || (Vo = 1, An.exports = Ja()), An.exports;
}
var m = ec();
function he(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, o; n < e.length; n++)
      (o = he(e[n])) !== "" && (t += (t && " ") + o);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var tc = { value: () => {
} };
function pn() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new Kt(n);
}
function Kt(e) {
  this._ = e;
}
function nc(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
Kt.prototype = pn.prototype = {
  constructor: Kt,
  on: function(e, t) {
    var n = this._, o = nc(e + "", n), r, i = -1, s = o.length;
    if (arguments.length < 2) {
      for (; ++i < s; ) if ((r = (e = o[i]).type) && (r = oc(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++i < s; )
      if (r = (e = o[i]).type) n[r] = Bo(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = Bo(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new Kt(e);
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
function oc(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function Bo(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = tc, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var Wn = "http://www.w3.org/1999/xhtml";
const Fo = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: Wn,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function mn(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Fo.hasOwnProperty(t) ? { space: Fo[t], local: e } : e;
}
function rc(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === Wn && t.documentElement.namespaceURI === Wn ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function ic(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function ni(e) {
  var t = mn(e);
  return (t.local ? ic : rc)(t);
}
function sc() {
}
function lo(e) {
  return e == null ? sc : function() {
    return this.querySelector(e);
  };
}
function ac(e) {
  typeof e != "function" && (e = lo(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = new Array(s), l, a, u = 0; u < s; ++u)
      (l = i[u]) && (a = e.call(l, l.__data__, u, i)) && ("__data__" in l && (a.__data__ = l.__data__), c[u] = a);
  return new _e(o, this._parents);
}
function cc(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function lc() {
  return [];
}
function oi(e) {
  return e == null ? lc : function() {
    return this.querySelectorAll(e);
  };
}
function uc(e) {
  return function() {
    return cc(e.apply(this, arguments));
  };
}
function dc(e) {
  typeof e == "function" ? e = uc(e) : e = oi(e);
  for (var t = this._groups, n = t.length, o = [], r = [], i = 0; i < n; ++i)
    for (var s = t[i], c = s.length, l, a = 0; a < c; ++a)
      (l = s[a]) && (o.push(e.call(l, l.__data__, a, s)), r.push(l));
  return new _e(o, r);
}
function ri(e) {
  return function() {
    return this.matches(e);
  };
}
function ii(e) {
  return function(t) {
    return t.matches(e);
  };
}
var fc = Array.prototype.find;
function hc(e) {
  return function() {
    return fc.call(this.children, e);
  };
}
function gc() {
  return this.firstElementChild;
}
function pc(e) {
  return this.select(e == null ? gc : hc(typeof e == "function" ? e : ii(e)));
}
var mc = Array.prototype.filter;
function yc() {
  return Array.from(this.children);
}
function xc(e) {
  return function() {
    return mc.call(this.children, e);
  };
}
function wc(e) {
  return this.selectAll(e == null ? yc : xc(typeof e == "function" ? e : ii(e)));
}
function vc(e) {
  typeof e != "function" && (e = ri(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = [], l, a = 0; a < s; ++a)
      (l = i[a]) && e.call(l, l.__data__, a, i) && c.push(l);
  return new _e(o, this._parents);
}
function si(e) {
  return new Array(e.length);
}
function bc() {
  return new _e(this._enter || this._groups.map(si), this._parents);
}
function nn(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
nn.prototype = {
  constructor: nn,
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
function _c(e) {
  return function() {
    return e;
  };
}
function Sc(e, t, n, o, r, i) {
  for (var s = 0, c, l = t.length, a = i.length; s < a; ++s)
    (c = t[s]) ? (c.__data__ = i[s], o[s] = c) : n[s] = new nn(e, i[s]);
  for (; s < l; ++s)
    (c = t[s]) && (r[s] = c);
}
function Ec(e, t, n, o, r, i, s) {
  var c, l, a = /* @__PURE__ */ new Map(), u = t.length, d = i.length, f = new Array(u), p;
  for (c = 0; c < u; ++c)
    (l = t[c]) && (f[c] = p = s.call(l, l.__data__, c, t) + "", a.has(p) ? r[c] = l : a.set(p, l));
  for (c = 0; c < d; ++c)
    p = s.call(e, i[c], c, i) + "", (l = a.get(p)) ? (o[c] = l, l.__data__ = i[c], a.delete(p)) : n[c] = new nn(e, i[c]);
  for (c = 0; c < u; ++c)
    (l = t[c]) && a.get(f[c]) === l && (r[c] = l);
}
function Nc(e) {
  return e.__data__;
}
function Cc(e, t) {
  if (!arguments.length) return Array.from(this, Nc);
  var n = t ? Ec : Sc, o = this._parents, r = this._groups;
  typeof e != "function" && (e = _c(e));
  for (var i = r.length, s = new Array(i), c = new Array(i), l = new Array(i), a = 0; a < i; ++a) {
    var u = o[a], d = r[a], f = d.length, p = Mc(e.call(u, u && u.__data__, a, o)), g = p.length, v = c[a] = new Array(g), w = s[a] = new Array(g), y = l[a] = new Array(f);
    n(u, d, v, w, y, p, t);
    for (var _ = 0, h = 0, x, C; _ < g; ++_)
      if (x = v[_]) {
        for (_ >= h && (h = _ + 1); !(C = w[h]) && ++h < g; ) ;
        x._next = C || null;
      }
  }
  return s = new _e(s, o), s._enter = c, s._exit = l, s;
}
function Mc(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function kc() {
  return new _e(this._exit || this._groups.map(si), this._parents);
}
function Ic(e, t, n) {
  var o = this.enter(), r = this, i = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? i.remove() : n(i), o && r ? o.merge(r).order() : r;
}
function Ac(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, i = o.length, s = Math.min(r, i), c = new Array(r), l = 0; l < s; ++l)
    for (var a = n[l], u = o[l], d = a.length, f = c[l] = new Array(d), p, g = 0; g < d; ++g)
      (p = a[g] || u[g]) && (f[g] = p);
  for (; l < r; ++l)
    c[l] = n[l];
  return new _e(c, this._parents);
}
function Dc() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, i = o[r], s; --r >= 0; )
      (s = o[r]) && (i && s.compareDocumentPosition(i) ^ 4 && i.parentNode.insertBefore(s, i), i = s);
  return this;
}
function $c(e) {
  e || (e = Pc);
  function t(d, f) {
    return d && f ? e(d.__data__, f.__data__) : !d - !f;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), i = 0; i < o; ++i) {
    for (var s = n[i], c = s.length, l = r[i] = new Array(c), a, u = 0; u < c; ++u)
      (a = s[u]) && (l[u] = a);
    l.sort(t);
  }
  return new _e(r, this._parents).order();
}
function Pc(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function Tc() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function zc() {
  return Array.from(this);
}
function Rc() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length; r < i; ++r) {
      var s = o[r];
      if (s) return s;
    }
  return null;
}
function jc() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function Lc() {
  return !this.node();
}
function Hc(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], i = 0, s = r.length, c; i < s; ++i)
      (c = r[i]) && e.call(c, c.__data__, i, r);
  return this;
}
function Oc(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Vc(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Bc(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function Fc(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function Yc(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function Xc(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function qc(e, t) {
  var n = mn(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? Vc : Oc : typeof t == "function" ? n.local ? Xc : Yc : n.local ? Fc : Bc)(n, t));
}
function ai(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function Wc(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Zc(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function Uc(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function Gc(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? Wc : typeof t == "function" ? Uc : Zc)(e, t, n ?? "")) : ct(this.node(), e);
}
function ct(e, t) {
  return e.style.getPropertyValue(t) || ai(e).getComputedStyle(e, null).getPropertyValue(t);
}
function Kc(e) {
  return function() {
    delete this[e];
  };
}
function Qc(e, t) {
  return function() {
    this[e] = t;
  };
}
function Jc(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function el(e, t) {
  return arguments.length > 1 ? this.each((t == null ? Kc : typeof t == "function" ? Jc : Qc)(e, t)) : this.node()[e];
}
function ci(e) {
  return e.trim().split(/^|\s+/);
}
function uo(e) {
  return e.classList || new li(e);
}
function li(e) {
  this._node = e, this._names = ci(e.getAttribute("class") || "");
}
li.prototype = {
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
function ui(e, t) {
  for (var n = uo(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function di(e, t) {
  for (var n = uo(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function tl(e) {
  return function() {
    ui(this, e);
  };
}
function nl(e) {
  return function() {
    di(this, e);
  };
}
function ol(e, t) {
  return function() {
    (t.apply(this, arguments) ? ui : di)(this, e);
  };
}
function rl(e, t) {
  var n = ci(e + "");
  if (arguments.length < 2) {
    for (var o = uo(this.node()), r = -1, i = n.length; ++r < i; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? ol : t ? tl : nl)(n, t));
}
function il() {
  this.textContent = "";
}
function sl(e) {
  return function() {
    this.textContent = e;
  };
}
function al(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function cl(e) {
  return arguments.length ? this.each(e == null ? il : (typeof e == "function" ? al : sl)(e)) : this.node().textContent;
}
function ll() {
  this.innerHTML = "";
}
function ul(e) {
  return function() {
    this.innerHTML = e;
  };
}
function dl(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function fl(e) {
  return arguments.length ? this.each(e == null ? ll : (typeof e == "function" ? dl : ul)(e)) : this.node().innerHTML;
}
function hl() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function gl() {
  return this.each(hl);
}
function pl() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function ml() {
  return this.each(pl);
}
function yl(e) {
  var t = typeof e == "function" ? e : ni(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function xl() {
  return null;
}
function wl(e, t) {
  var n = typeof e == "function" ? e : ni(e), o = t == null ? xl : typeof t == "function" ? t : lo(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function vl() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function bl() {
  return this.each(vl);
}
function _l() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Sl() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function El(e) {
  return this.select(e ? Sl : _l);
}
function Nl(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function Cl(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function Ml(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function kl(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, i; n < r; ++n)
        i = t[n], (!e.type || i.type === e.type) && i.name === e.name ? this.removeEventListener(i.type, i.listener, i.options) : t[++o] = i;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function Il(e, t, n) {
  return function() {
    var o = this.__on, r, i = Cl(t);
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
function Al(e, t, n) {
  var o = Ml(e + ""), r, i = o.length, s;
  if (arguments.length < 2) {
    var c = this.node().__on;
    if (c) {
      for (var l = 0, a = c.length, u; l < a; ++l)
        for (r = 0, u = c[l]; r < i; ++r)
          if ((s = o[r]).type === u.type && s.name === u.name)
            return u.value;
    }
    return;
  }
  for (c = t ? Il : kl, r = 0; r < i; ++r) this.each(c(o[r], t, n));
  return this;
}
function fi(e, t, n) {
  var o = ai(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function Dl(e, t) {
  return function() {
    return fi(this, e, t);
  };
}
function $l(e, t) {
  return function() {
    return fi(this, e, t.apply(this, arguments));
  };
}
function Pl(e, t) {
  return this.each((typeof t == "function" ? $l : Dl)(e, t));
}
function* Tl() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length, s; r < i; ++r)
      (s = o[r]) && (yield s);
}
var hi = [null];
function _e(e, t) {
  this._groups = e, this._parents = t;
}
function Rt() {
  return new _e([[document.documentElement]], hi);
}
function zl() {
  return this;
}
_e.prototype = Rt.prototype = {
  constructor: _e,
  select: ac,
  selectAll: dc,
  selectChild: pc,
  selectChildren: wc,
  filter: vc,
  data: Cc,
  enter: bc,
  exit: kc,
  join: Ic,
  merge: Ac,
  selection: zl,
  order: Dc,
  sort: $c,
  call: Tc,
  nodes: zc,
  node: Rc,
  size: jc,
  empty: Lc,
  each: Hc,
  attr: qc,
  style: Gc,
  property: el,
  classed: rl,
  text: cl,
  html: fl,
  raise: gl,
  lower: ml,
  append: yl,
  insert: wl,
  remove: bl,
  clone: El,
  datum: Nl,
  on: Al,
  dispatch: Pl,
  [Symbol.iterator]: Tl
};
function be(e) {
  return typeof e == "string" ? new _e([[document.querySelector(e)]], [document.documentElement]) : new _e([[e]], hi);
}
function Rl(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Me(e, t) {
  if (e = Rl(e), t === void 0 && (t = e.currentTarget), t) {
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
const jl = { passive: !1 }, Nt = { capture: !0, passive: !1 };
function Dn(e) {
  e.stopImmediatePropagation();
}
function st(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function gi(e) {
  var t = e.document.documentElement, n = be(e).on("dragstart.drag", st, Nt);
  "onselectstart" in t ? n.on("selectstart.drag", st, Nt) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function pi(e, t) {
  var n = e.document.documentElement, o = be(e).on("dragstart.drag", null);
  t && (o.on("click.drag", st, Nt), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const Bt = (e) => () => e;
function Zn(e, {
  sourceEvent: t,
  subject: n,
  target: o,
  identifier: r,
  active: i,
  x: s,
  y: c,
  dx: l,
  dy: a,
  dispatch: u
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
    _: { value: u }
  });
}
Zn.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function Ll(e) {
  return !e.ctrlKey && !e.button;
}
function Hl() {
  return this.parentNode;
}
function Ol(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function Vl() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function mi() {
  var e = Ll, t = Hl, n = Ol, o = Vl, r = {}, i = pn("start", "drag", "end"), s = 0, c, l, a, u, d = 0;
  function f(x) {
    x.on("mousedown.drag", p).filter(o).on("touchstart.drag", w).on("touchmove.drag", y, jl).on("touchend.drag touchcancel.drag", _).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function p(x, C) {
    if (!(u || !e.call(this, x, C))) {
      var S = h(this, t.call(this, x, C), x, C, "mouse");
      S && (be(x.view).on("mousemove.drag", g, Nt).on("mouseup.drag", v, Nt), gi(x.view), Dn(x), a = !1, c = x.clientX, l = x.clientY, S("start", x));
    }
  }
  function g(x) {
    if (st(x), !a) {
      var C = x.clientX - c, S = x.clientY - l;
      a = C * C + S * S > d;
    }
    r.mouse("drag", x);
  }
  function v(x) {
    be(x.view).on("mousemove.drag mouseup.drag", null), pi(x.view, a), st(x), r.mouse("end", x);
  }
  function w(x, C) {
    if (e.call(this, x, C)) {
      var S = x.changedTouches, I = t.call(this, x, C), A = S.length, $, V;
      for ($ = 0; $ < A; ++$)
        (V = h(this, I, x, C, S[$].identifier, S[$])) && (Dn(x), V("start", x, S[$]));
    }
  }
  function y(x) {
    var C = x.changedTouches, S = C.length, I, A;
    for (I = 0; I < S; ++I)
      (A = r[C[I].identifier]) && (st(x), A("drag", x, C[I]));
  }
  function _(x) {
    var C = x.changedTouches, S = C.length, I, A;
    for (u && clearTimeout(u), u = setTimeout(function() {
      u = null;
    }, 500), I = 0; I < S; ++I)
      (A = r[C[I].identifier]) && (Dn(x), A("end", x, C[I]));
  }
  function h(x, C, S, I, A, $) {
    var V = i.copy(), k = Me($ || S, C), T, j, b;
    if ((b = n.call(x, new Zn("beforestart", {
      sourceEvent: S,
      target: f,
      identifier: A,
      active: s,
      x: k[0],
      y: k[1],
      dx: 0,
      dy: 0,
      dispatch: V
    }), I)) != null)
      return T = b.x - k[0] || 0, j = b.y - k[1] || 0, function N(E, M, P) {
        var D = k, O;
        switch (E) {
          case "start":
            r[A] = N, O = s++;
            break;
          case "end":
            delete r[A], --s;
          // falls through
          case "drag":
            k = Me(P || M, C), O = s;
            break;
        }
        V.call(
          E,
          x,
          new Zn(E, {
            sourceEvent: M,
            subject: b,
            target: f,
            identifier: A,
            active: O,
            x: k[0] + T,
            y: k[1] + j,
            dx: k[0] - D[0],
            dy: k[1] - D[1],
            dispatch: V
          }),
          I
        );
      };
  }
  return f.filter = function(x) {
    return arguments.length ? (e = typeof x == "function" ? x : Bt(!!x), f) : e;
  }, f.container = function(x) {
    return arguments.length ? (t = typeof x == "function" ? x : Bt(x), f) : t;
  }, f.subject = function(x) {
    return arguments.length ? (n = typeof x == "function" ? x : Bt(x), f) : n;
  }, f.touchable = function(x) {
    return arguments.length ? (o = typeof x == "function" ? x : Bt(!!x), f) : o;
  }, f.on = function() {
    var x = i.on.apply(i, arguments);
    return x === i ? f : x;
  }, f.clickDistance = function(x) {
    return arguments.length ? (d = (x = +x) * x, f) : Math.sqrt(d);
  }, f;
}
function fo(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function yi(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function jt() {
}
var Ct = 0.7, on = 1 / Ct, at = "\\s*([+-]?\\d+)\\s*", Mt = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", ze = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", Bl = /^#([0-9a-f]{3,8})$/, Fl = new RegExp(`^rgb\\(${at},${at},${at}\\)$`), Yl = new RegExp(`^rgb\\(${ze},${ze},${ze}\\)$`), Xl = new RegExp(`^rgba\\(${at},${at},${at},${Mt}\\)$`), ql = new RegExp(`^rgba\\(${ze},${ze},${ze},${Mt}\\)$`), Wl = new RegExp(`^hsl\\(${Mt},${ze},${ze}\\)$`), Zl = new RegExp(`^hsla\\(${Mt},${ze},${ze},${Mt}\\)$`), Yo = {
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
fo(jt, Ke, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Xo,
  // Deprecated! Use color.formatHex.
  formatHex: Xo,
  formatHex8: Ul,
  formatHsl: Gl,
  formatRgb: qo,
  toString: qo
});
function Xo() {
  return this.rgb().formatHex();
}
function Ul() {
  return this.rgb().formatHex8();
}
function Gl() {
  return xi(this).formatHsl();
}
function qo() {
  return this.rgb().formatRgb();
}
function Ke(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = Bl.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? Wo(t) : n === 3 ? new we(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? Ft(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? Ft(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = Fl.exec(e)) ? new we(t[1], t[2], t[3], 1) : (t = Yl.exec(e)) ? new we(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = Xl.exec(e)) ? Ft(t[1], t[2], t[3], t[4]) : (t = ql.exec(e)) ? Ft(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = Wl.exec(e)) ? Go(t[1], t[2] / 100, t[3] / 100, 1) : (t = Zl.exec(e)) ? Go(t[1], t[2] / 100, t[3] / 100, t[4]) : Yo.hasOwnProperty(e) ? Wo(Yo[e]) : e === "transparent" ? new we(NaN, NaN, NaN, 0) : null;
}
function Wo(e) {
  return new we(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function Ft(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new we(e, t, n, o);
}
function Kl(e) {
  return e instanceof jt || (e = Ke(e)), e ? (e = e.rgb(), new we(e.r, e.g, e.b, e.opacity)) : new we();
}
function Un(e, t, n, o) {
  return arguments.length === 1 ? Kl(e) : new we(e, t, n, o ?? 1);
}
function we(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
fo(we, Un, yi(jt, {
  brighter(e) {
    return e = e == null ? on : Math.pow(on, e), new we(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Ct : Math.pow(Ct, e), new we(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new we(Ue(this.r), Ue(this.g), Ue(this.b), rn(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: Zo,
  // Deprecated! Use color.formatHex.
  formatHex: Zo,
  formatHex8: Ql,
  formatRgb: Uo,
  toString: Uo
}));
function Zo() {
  return `#${Ze(this.r)}${Ze(this.g)}${Ze(this.b)}`;
}
function Ql() {
  return `#${Ze(this.r)}${Ze(this.g)}${Ze(this.b)}${Ze((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function Uo() {
  const e = rn(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${Ue(this.r)}, ${Ue(this.g)}, ${Ue(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function rn(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function Ue(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function Ze(e) {
  return e = Ue(e), (e < 16 ? "0" : "") + e.toString(16);
}
function Go(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new ke(e, t, n, o);
}
function xi(e) {
  if (e instanceof ke) return new ke(e.h, e.s, e.l, e.opacity);
  if (e instanceof jt || (e = Ke(e)), !e) return new ke();
  if (e instanceof ke) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), i = Math.max(t, n, o), s = NaN, c = i - r, l = (i + r) / 2;
  return c ? (t === i ? s = (n - o) / c + (n < o) * 6 : n === i ? s = (o - t) / c + 2 : s = (t - n) / c + 4, c /= l < 0.5 ? i + r : 2 - i - r, s *= 60) : c = l > 0 && l < 1 ? 0 : s, new ke(s, c, l, e.opacity);
}
function Jl(e, t, n, o) {
  return arguments.length === 1 ? xi(e) : new ke(e, t, n, o ?? 1);
}
function ke(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
fo(ke, Jl, yi(jt, {
  brighter(e) {
    return e = e == null ? on : Math.pow(on, e), new ke(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Ct : Math.pow(Ct, e), new ke(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new we(
      $n(e >= 240 ? e - 240 : e + 120, r, o),
      $n(e, r, o),
      $n(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new ke(Ko(this.h), Yt(this.s), Yt(this.l), rn(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = rn(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${Ko(this.h)}, ${Yt(this.s) * 100}%, ${Yt(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function Ko(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function Yt(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function $n(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const ho = (e) => () => e;
function eu(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function tu(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function nu(e) {
  return (e = +e) == 1 ? wi : function(t, n) {
    return n - t ? tu(t, n, e) : ho(isNaN(t) ? n : t);
  };
}
function wi(e, t) {
  var n = t - e;
  return n ? eu(e, n) : ho(isNaN(e) ? t : e);
}
const sn = (function e(t) {
  var n = nu(t);
  function o(r, i) {
    var s = n((r = Un(r)).r, (i = Un(i)).r), c = n(r.g, i.g), l = n(r.b, i.b), a = wi(r.opacity, i.opacity);
    return function(u) {
      return r.r = s(u), r.g = c(u), r.b = l(u), r.opacity = a(u), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function ou(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(i) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - i) + t[r] * i;
    return o;
  };
}
function ru(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function iu(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), i = new Array(n), s;
  for (s = 0; s < o; ++s) r[s] = St(e[s], t[s]);
  for (; s < n; ++s) i[s] = t[s];
  return function(c) {
    for (s = 0; s < o; ++s) i[s] = r[s](c);
    return i;
  };
}
function su(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(o) {
    return n.setTime(e * (1 - o) + t * o), n;
  };
}
function Te(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function au(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = St(e[r], t[r]) : o[r] = t[r];
  return function(i) {
    for (r in n) o[r] = n[r](i);
    return o;
  };
}
var Gn = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, Pn = new RegExp(Gn.source, "g");
function cu(e) {
  return function() {
    return e;
  };
}
function lu(e) {
  return function(t) {
    return e(t) + "";
  };
}
function vi(e, t) {
  var n = Gn.lastIndex = Pn.lastIndex = 0, o, r, i, s = -1, c = [], l = [];
  for (e = e + "", t = t + ""; (o = Gn.exec(e)) && (r = Pn.exec(t)); )
    (i = r.index) > n && (i = t.slice(n, i), c[s] ? c[s] += i : c[++s] = i), (o = o[0]) === (r = r[0]) ? c[s] ? c[s] += r : c[++s] = r : (c[++s] = null, l.push({ i: s, x: Te(o, r) })), n = Pn.lastIndex;
  return n < t.length && (i = t.slice(n), c[s] ? c[s] += i : c[++s] = i), c.length < 2 ? l[0] ? lu(l[0].x) : cu(t) : (t = l.length, function(a) {
    for (var u = 0, d; u < t; ++u) c[(d = l[u]).i] = d.x(a);
    return c.join("");
  });
}
function St(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? ho(t) : (n === "number" ? Te : n === "string" ? (o = Ke(t)) ? (t = o, sn) : vi : t instanceof Ke ? sn : t instanceof Date ? su : ru(t) ? ou : Array.isArray(t) ? iu : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? au : Te)(e, t);
}
var Qo = 180 / Math.PI, Kn = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function bi(e, t, n, o, r, i) {
  var s, c, l;
  return (s = Math.sqrt(e * e + t * t)) && (e /= s, t /= s), (l = e * n + t * o) && (n -= e * l, o -= t * l), (c = Math.sqrt(n * n + o * o)) && (n /= c, o /= c, l /= c), e * o < t * n && (e = -e, t = -t, l = -l, s = -s), {
    translateX: r,
    translateY: i,
    rotate: Math.atan2(t, e) * Qo,
    skewX: Math.atan(l) * Qo,
    scaleX: s,
    scaleY: c
  };
}
var Xt;
function uu(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Kn : bi(t.a, t.b, t.c, t.d, t.e, t.f);
}
function du(e) {
  return e == null || (Xt || (Xt = document.createElementNS("http://www.w3.org/2000/svg", "g")), Xt.setAttribute("transform", e), !(e = Xt.transform.baseVal.consolidate())) ? Kn : (e = e.matrix, bi(e.a, e.b, e.c, e.d, e.e, e.f));
}
function _i(e, t, n, o) {
  function r(a) {
    return a.length ? a.pop() + " " : "";
  }
  function i(a, u, d, f, p, g) {
    if (a !== d || u !== f) {
      var v = p.push("translate(", null, t, null, n);
      g.push({ i: v - 4, x: Te(a, d) }, { i: v - 2, x: Te(u, f) });
    } else (d || f) && p.push("translate(" + d + t + f + n);
  }
  function s(a, u, d, f) {
    a !== u ? (a - u > 180 ? u += 360 : u - a > 180 && (a += 360), f.push({ i: d.push(r(d) + "rotate(", null, o) - 2, x: Te(a, u) })) : u && d.push(r(d) + "rotate(" + u + o);
  }
  function c(a, u, d, f) {
    a !== u ? f.push({ i: d.push(r(d) + "skewX(", null, o) - 2, x: Te(a, u) }) : u && d.push(r(d) + "skewX(" + u + o);
  }
  function l(a, u, d, f, p, g) {
    if (a !== d || u !== f) {
      var v = p.push(r(p) + "scale(", null, ",", null, ")");
      g.push({ i: v - 4, x: Te(a, d) }, { i: v - 2, x: Te(u, f) });
    } else (d !== 1 || f !== 1) && p.push(r(p) + "scale(" + d + "," + f + ")");
  }
  return function(a, u) {
    var d = [], f = [];
    return a = e(a), u = e(u), i(a.translateX, a.translateY, u.translateX, u.translateY, d, f), s(a.rotate, u.rotate, d, f), c(a.skewX, u.skewX, d, f), l(a.scaleX, a.scaleY, u.scaleX, u.scaleY, d, f), a = u = null, function(p) {
      for (var g = -1, v = f.length, w; ++g < v; ) d[(w = f[g]).i] = w.x(p);
      return d.join("");
    };
  };
}
var fu = _i(uu, "px, ", "px)", "deg)"), hu = _i(du, ", ", ")", ")"), gu = 1e-12;
function Jo(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function pu(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function mu(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const Qt = (function e(t, n, o) {
  function r(i, s) {
    var c = i[0], l = i[1], a = i[2], u = s[0], d = s[1], f = s[2], p = u - c, g = d - l, v = p * p + g * g, w, y;
    if (v < gu)
      y = Math.log(f / a) / t, w = function(I) {
        return [
          c + I * p,
          l + I * g,
          a * Math.exp(t * I * y)
        ];
      };
    else {
      var _ = Math.sqrt(v), h = (f * f - a * a + o * v) / (2 * a * n * _), x = (f * f - a * a - o * v) / (2 * f * n * _), C = Math.log(Math.sqrt(h * h + 1) - h), S = Math.log(Math.sqrt(x * x + 1) - x);
      y = (S - C) / t, w = function(I) {
        var A = I * y, $ = Jo(C), V = a / (n * _) * ($ * mu(t * A + C) - pu(C));
        return [
          c + V * p,
          l + V * g,
          a * $ / Jo(t * A + C)
        ];
      };
    }
    return w.duration = y * 1e3 * t / Math.SQRT2, w;
  }
  return r.rho = function(i) {
    var s = Math.max(1e-3, +i), c = s * s, l = c * c;
    return e(s, c, l);
  }, r;
})(Math.SQRT2, 2, 4);
var lt = 0, bt = 0, xt = 0, Si = 1e3, an, _t, cn = 0, Qe = 0, yn = 0, kt = typeof performance == "object" && performance.now ? performance : Date, Ei = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function go() {
  return Qe || (Ei(yu), Qe = kt.now() + yn);
}
function yu() {
  Qe = 0;
}
function ln() {
  this._call = this._time = this._next = null;
}
ln.prototype = Ni.prototype = {
  constructor: ln,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? go() : +n) + (t == null ? 0 : +t), !this._next && _t !== this && (_t ? _t._next = this : an = this, _t = this), this._call = e, this._time = n, Qn();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Qn());
  }
};
function Ni(e, t, n) {
  var o = new ln();
  return o.restart(e, t, n), o;
}
function xu() {
  go(), ++lt;
  for (var e = an, t; e; )
    (t = Qe - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --lt;
}
function er() {
  Qe = (cn = kt.now()) + yn, lt = bt = 0;
  try {
    xu();
  } finally {
    lt = 0, vu(), Qe = 0;
  }
}
function wu() {
  var e = kt.now(), t = e - cn;
  t > Si && (yn -= t, cn = e);
}
function vu() {
  for (var e, t = an, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : an = n);
  _t = e, Qn(o);
}
function Qn(e) {
  if (!lt) {
    bt && (bt = clearTimeout(bt));
    var t = e - Qe;
    t > 24 ? (e < 1 / 0 && (bt = setTimeout(er, e - kt.now() - yn)), xt && (xt = clearInterval(xt))) : (xt || (cn = kt.now(), xt = setInterval(wu, Si)), lt = 1, Ei(er));
  }
}
function tr(e, t, n) {
  var o = new ln();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var bu = pn("start", "end", "cancel", "interrupt"), _u = [], Ci = 0, nr = 1, Jn = 2, Jt = 3, or = 4, eo = 5, en = 6;
function xn(e, t, n, o, r, i) {
  var s = e.__transition;
  if (!s) e.__transition = {};
  else if (n in s) return;
  Su(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: bu,
    tween: _u,
    time: i.time,
    delay: i.delay,
    duration: i.duration,
    ease: i.ease,
    timer: null,
    state: Ci
  });
}
function po(e, t) {
  var n = $e(e, t);
  if (n.state > Ci) throw new Error("too late; already scheduled");
  return n;
}
function Re(e, t) {
  var n = $e(e, t);
  if (n.state > Jt) throw new Error("too late; already running");
  return n;
}
function $e(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function Su(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = Ni(i, 0, n.time);
  function i(a) {
    n.state = nr, n.timer.restart(s, n.delay, n.time), n.delay <= a && s(a - n.delay);
  }
  function s(a) {
    var u, d, f, p;
    if (n.state !== nr) return l();
    for (u in o)
      if (p = o[u], p.name === n.name) {
        if (p.state === Jt) return tr(s);
        p.state === or ? (p.state = en, p.timer.stop(), p.on.call("interrupt", e, e.__data__, p.index, p.group), delete o[u]) : +u < t && (p.state = en, p.timer.stop(), p.on.call("cancel", e, e.__data__, p.index, p.group), delete o[u]);
      }
    if (tr(function() {
      n.state === Jt && (n.state = or, n.timer.restart(c, n.delay, n.time), c(a));
    }), n.state = Jn, n.on.call("start", e, e.__data__, n.index, n.group), n.state === Jn) {
      for (n.state = Jt, r = new Array(f = n.tween.length), u = 0, d = -1; u < f; ++u)
        (p = n.tween[u].value.call(e, e.__data__, n.index, n.group)) && (r[++d] = p);
      r.length = d + 1;
    }
  }
  function c(a) {
    for (var u = a < n.duration ? n.ease.call(null, a / n.duration) : (n.timer.restart(l), n.state = eo, 1), d = -1, f = r.length; ++d < f; )
      r[d].call(e, u);
    n.state === eo && (n.on.call("end", e, e.__data__, n.index, n.group), l());
  }
  function l() {
    n.state = en, n.timer.stop(), delete o[t];
    for (var a in o) return;
    delete e.__transition;
  }
}
function tn(e, t) {
  var n = e.__transition, o, r, i = !0, s;
  if (n) {
    t = t == null ? null : t + "";
    for (s in n) {
      if ((o = n[s]).name !== t) {
        i = !1;
        continue;
      }
      r = o.state > Jn && o.state < eo, o.state = en, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[s];
    }
    i && delete e.__transition;
  }
}
function Eu(e) {
  return this.each(function() {
    tn(this, e);
  });
}
function Nu(e, t) {
  var n, o;
  return function() {
    var r = Re(this, e), i = r.tween;
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
function Cu(e, t, n) {
  var o, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var i = Re(this, e), s = i.tween;
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
function Mu(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = $e(this.node(), n).tween, r = 0, i = o.length, s; r < i; ++r)
      if ((s = o[r]).name === e)
        return s.value;
    return null;
  }
  return this.each((t == null ? Nu : Cu)(n, e, t));
}
function mo(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = Re(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return $e(r, o).value[t];
  };
}
function Mi(e, t) {
  var n;
  return (typeof t == "number" ? Te : t instanceof Ke ? sn : (n = Ke(t)) ? (t = n, sn) : vi)(e, t);
}
function ku(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Iu(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Au(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttribute(e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Du(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttributeNS(e.space, e.local);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function $u(e, t, n) {
  var o, r, i;
  return function() {
    var s, c = n(this), l;
    return c == null ? void this.removeAttribute(e) : (s = this.getAttribute(e), l = c + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, c)));
  };
}
function Pu(e, t, n) {
  var o, r, i;
  return function() {
    var s, c = n(this), l;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (s = this.getAttributeNS(e.space, e.local), l = c + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, c)));
  };
}
function Tu(e, t) {
  var n = mn(e), o = n === "transform" ? hu : Mi;
  return this.attrTween(e, typeof t == "function" ? (n.local ? Pu : $u)(n, o, mo(this, "attr." + e, t)) : t == null ? (n.local ? Iu : ku)(n) : (n.local ? Du : Au)(n, o, t));
}
function zu(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function Ru(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function ju(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && Ru(e, i)), n;
  }
  return r._value = t, r;
}
function Lu(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && zu(e, i)), n;
  }
  return r._value = t, r;
}
function Hu(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = mn(e);
  return this.tween(n, (o.local ? ju : Lu)(o, t));
}
function Ou(e, t) {
  return function() {
    po(this, e).delay = +t.apply(this, arguments);
  };
}
function Vu(e, t) {
  return t = +t, function() {
    po(this, e).delay = t;
  };
}
function Bu(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Ou : Vu)(t, e)) : $e(this.node(), t).delay;
}
function Fu(e, t) {
  return function() {
    Re(this, e).duration = +t.apply(this, arguments);
  };
}
function Yu(e, t) {
  return t = +t, function() {
    Re(this, e).duration = t;
  };
}
function Xu(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Fu : Yu)(t, e)) : $e(this.node(), t).duration;
}
function qu(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    Re(this, e).ease = t;
  };
}
function Wu(e) {
  var t = this._id;
  return arguments.length ? this.each(qu(t, e)) : $e(this.node(), t).ease;
}
function Zu(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    Re(this, e).ease = n;
  };
}
function Uu(e) {
  if (typeof e != "function") throw new Error();
  return this.each(Zu(this._id, e));
}
function Gu(e) {
  typeof e != "function" && (e = ri(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, c = o[r] = [], l, a = 0; a < s; ++a)
      (l = i[a]) && e.call(l, l.__data__, a, i) && c.push(l);
  return new Oe(o, this._parents, this._name, this._id);
}
function Ku(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, i = Math.min(o, r), s = new Array(o), c = 0; c < i; ++c)
    for (var l = t[c], a = n[c], u = l.length, d = s[c] = new Array(u), f, p = 0; p < u; ++p)
      (f = l[p] || a[p]) && (d[p] = f);
  for (; c < o; ++c)
    s[c] = t[c];
  return new Oe(s, this._parents, this._name, this._id);
}
function Qu(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function Ju(e, t, n) {
  var o, r, i = Qu(t) ? po : Re;
  return function() {
    var s = i(this, e), c = s.on;
    c !== o && (r = (o = c).copy()).on(t, n), s.on = r;
  };
}
function ed(e, t) {
  var n = this._id;
  return arguments.length < 2 ? $e(this.node(), n).on.on(e) : this.each(Ju(n, e, t));
}
function td(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function nd() {
  return this.on("end.remove", td(this._id));
}
function od(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = lo(e));
  for (var o = this._groups, r = o.length, i = new Array(r), s = 0; s < r; ++s)
    for (var c = o[s], l = c.length, a = i[s] = new Array(l), u, d, f = 0; f < l; ++f)
      (u = c[f]) && (d = e.call(u, u.__data__, f, c)) && ("__data__" in u && (d.__data__ = u.__data__), a[f] = d, xn(a[f], t, n, f, a, $e(u, n)));
  return new Oe(i, this._parents, t, n);
}
function rd(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = oi(e));
  for (var o = this._groups, r = o.length, i = [], s = [], c = 0; c < r; ++c)
    for (var l = o[c], a = l.length, u, d = 0; d < a; ++d)
      if (u = l[d]) {
        for (var f = e.call(u, u.__data__, d, l), p, g = $e(u, n), v = 0, w = f.length; v < w; ++v)
          (p = f[v]) && xn(p, t, n, v, f, g);
        i.push(f), s.push(u);
      }
  return new Oe(i, s, t, n);
}
var id = Rt.prototype.constructor;
function sd() {
  return new id(this._groups, this._parents);
}
function ad(e, t) {
  var n, o, r;
  return function() {
    var i = ct(this, e), s = (this.style.removeProperty(e), ct(this, e));
    return i === s ? null : i === n && s === o ? r : r = t(n = i, o = s);
  };
}
function ki(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function cd(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = ct(this, e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function ld(e, t, n) {
  var o, r, i;
  return function() {
    var s = ct(this, e), c = n(this), l = c + "";
    return c == null && (l = c = (this.style.removeProperty(e), ct(this, e))), s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, c));
  };
}
function ud(e, t) {
  var n, o, r, i = "style." + t, s = "end." + i, c;
  return function() {
    var l = Re(this, e), a = l.on, u = l.value[i] == null ? c || (c = ki(t)) : void 0;
    (a !== n || r !== u) && (o = (n = a).copy()).on(s, r = u), l.on = o;
  };
}
function dd(e, t, n) {
  var o = (e += "") == "transform" ? fu : Mi;
  return t == null ? this.styleTween(e, ad(e, o)).on("end.style." + e, ki(e)) : typeof t == "function" ? this.styleTween(e, ld(e, o, mo(this, "style." + e, t))).each(ud(this._id, e)) : this.styleTween(e, cd(e, o, t), n).on("end.style." + e, null);
}
function fd(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function hd(e, t, n) {
  var o, r;
  function i() {
    var s = t.apply(this, arguments);
    return s !== r && (o = (r = s) && fd(e, s, n)), o;
  }
  return i._value = t, i;
}
function gd(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, hd(e, t, n ?? ""));
}
function pd(e) {
  return function() {
    this.textContent = e;
  };
}
function md(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function yd(e) {
  return this.tween("text", typeof e == "function" ? md(mo(this, "text", e)) : pd(e == null ? "" : e + ""));
}
function xd(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function wd(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && xd(r)), t;
  }
  return o._value = e, o;
}
function vd(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, wd(e));
}
function bd() {
  for (var e = this._name, t = this._id, n = Ii(), o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], c = s.length, l, a = 0; a < c; ++a)
      if (l = s[a]) {
        var u = $e(l, t);
        xn(l, e, n, a, s, {
          time: u.time + u.delay + u.duration,
          delay: 0,
          duration: u.duration,
          ease: u.ease
        });
      }
  return new Oe(o, this._parents, e, n);
}
function _d() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(i, s) {
    var c = { value: s }, l = { value: function() {
      --r === 0 && i();
    } };
    n.each(function() {
      var a = Re(this, o), u = a.on;
      u !== e && (t = (e = u).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(l)), a.on = t;
    }), r === 0 && i();
  });
}
var Sd = 0;
function Oe(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function Ii() {
  return ++Sd;
}
var Le = Rt.prototype;
Oe.prototype = {
  constructor: Oe,
  select: od,
  selectAll: rd,
  selectChild: Le.selectChild,
  selectChildren: Le.selectChildren,
  filter: Gu,
  merge: Ku,
  selection: sd,
  transition: bd,
  call: Le.call,
  nodes: Le.nodes,
  node: Le.node,
  size: Le.size,
  empty: Le.empty,
  each: Le.each,
  on: ed,
  attr: Tu,
  attrTween: Hu,
  style: dd,
  styleTween: gd,
  text: yd,
  textTween: vd,
  remove: nd,
  tween: Mu,
  delay: Bu,
  duration: Xu,
  ease: Wu,
  easeVarying: Uu,
  end: _d,
  [Symbol.iterator]: Le[Symbol.iterator]
};
function Ed(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var Nd = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: Ed
};
function Cd(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function Md(e) {
  var t, n;
  e instanceof Oe ? (t = e._id, e = e._name) : (t = Ii(), (n = Nd).time = go(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], c = s.length, l, a = 0; a < c; ++a)
      (l = s[a]) && xn(l, e, t, a, s, n || Cd(l, t));
  return new Oe(o, this._parents, e, t);
}
Rt.prototype.interrupt = Eu;
Rt.prototype.transition = Md;
const qt = (e) => () => e;
function kd(e, {
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
function He(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
He.prototype = {
  constructor: He,
  scale: function(e) {
    return e === 1 ? this : new He(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new He(this.k, this.x + this.k * e, this.y + this.k * t);
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
var wn = new He(1, 0, 0);
Ai.prototype = He.prototype;
function Ai(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return wn;
  return e.__zoom;
}
function Tn(e) {
  e.stopImmediatePropagation();
}
function wt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Id(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function Ad() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function rr() {
  return this.__zoom || wn;
}
function Dd(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function $d() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Pd(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], i = e.invertY(t[0][1]) - n[0][1], s = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    s > i ? (i + s) / 2 : Math.min(0, i) || Math.max(0, s)
  );
}
function Di() {
  var e = Id, t = Ad, n = Pd, o = Dd, r = $d, i = [0, 1 / 0], s = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, l = Qt, a = pn("start", "zoom", "end"), u, d, f, p = 500, g = 150, v = 0, w = 10;
  function y(b) {
    b.property("__zoom", rr).on("wheel.zoom", A, { passive: !1 }).on("mousedown.zoom", $).on("dblclick.zoom", V).filter(r).on("touchstart.zoom", k).on("touchmove.zoom", T).on("touchend.zoom touchcancel.zoom", j).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  y.transform = function(b, N, E, M) {
    var P = b.selection ? b.selection() : b;
    P.property("__zoom", rr), b !== P ? C(b, N, E, M) : P.interrupt().each(function() {
      S(this, arguments).event(M).start().zoom(null, typeof N == "function" ? N.apply(this, arguments) : N).end();
    });
  }, y.scaleBy = function(b, N, E, M) {
    y.scaleTo(b, function() {
      var P = this.__zoom.k, D = typeof N == "function" ? N.apply(this, arguments) : N;
      return P * D;
    }, E, M);
  }, y.scaleTo = function(b, N, E, M) {
    y.transform(b, function() {
      var P = t.apply(this, arguments), D = this.__zoom, O = E == null ? x(P) : typeof E == "function" ? E.apply(this, arguments) : E, L = D.invert(O), H = typeof N == "function" ? N.apply(this, arguments) : N;
      return n(h(_(D, H), O, L), P, s);
    }, E, M);
  }, y.translateBy = function(b, N, E, M) {
    y.transform(b, function() {
      return n(this.__zoom.translate(
        typeof N == "function" ? N.apply(this, arguments) : N,
        typeof E == "function" ? E.apply(this, arguments) : E
      ), t.apply(this, arguments), s);
    }, null, M);
  }, y.translateTo = function(b, N, E, M, P) {
    y.transform(b, function() {
      var D = t.apply(this, arguments), O = this.__zoom, L = M == null ? x(D) : typeof M == "function" ? M.apply(this, arguments) : M;
      return n(wn.translate(L[0], L[1]).scale(O.k).translate(
        typeof N == "function" ? -N.apply(this, arguments) : -N,
        typeof E == "function" ? -E.apply(this, arguments) : -E
      ), D, s);
    }, M, P);
  };
  function _(b, N) {
    return N = Math.max(i[0], Math.min(i[1], N)), N === b.k ? b : new He(N, b.x, b.y);
  }
  function h(b, N, E) {
    var M = N[0] - E[0] * b.k, P = N[1] - E[1] * b.k;
    return M === b.x && P === b.y ? b : new He(b.k, M, P);
  }
  function x(b) {
    return [(+b[0][0] + +b[1][0]) / 2, (+b[0][1] + +b[1][1]) / 2];
  }
  function C(b, N, E, M) {
    b.on("start.zoom", function() {
      S(this, arguments).event(M).start();
    }).on("interrupt.zoom end.zoom", function() {
      S(this, arguments).event(M).end();
    }).tween("zoom", function() {
      var P = this, D = arguments, O = S(P, D).event(M), L = t.apply(P, D), H = E == null ? x(L) : typeof E == "function" ? E.apply(P, D) : E, X = Math.max(L[1][0] - L[0][0], L[1][1] - L[0][1]), Y = P.__zoom, Z = typeof N == "function" ? N.apply(P, D) : N, ee = l(Y.invert(H).concat(X / Y.k), Z.invert(H).concat(X / Z.k));
      return function(U) {
        if (U === 1) U = Z;
        else {
          var R = ee(U), F = X / R[2];
          U = new He(F, H[0] - R[0] * F, H[1] - R[1] * F);
        }
        O.zoom(null, U);
      };
    });
  }
  function S(b, N, E) {
    return !E && b.__zooming || new I(b, N);
  }
  function I(b, N) {
    this.that = b, this.args = N, this.active = 0, this.sourceEvent = null, this.extent = t.apply(b, N), this.taps = 0;
  }
  I.prototype = {
    event: function(b) {
      return b && (this.sourceEvent = b), this;
    },
    start: function() {
      return ++this.active === 1 && (this.that.__zooming = this, this.emit("start")), this;
    },
    zoom: function(b, N) {
      return this.mouse && b !== "mouse" && (this.mouse[1] = N.invert(this.mouse[0])), this.touch0 && b !== "touch" && (this.touch0[1] = N.invert(this.touch0[0])), this.touch1 && b !== "touch" && (this.touch1[1] = N.invert(this.touch1[0])), this.that.__zoom = N, this.emit("zoom"), this;
    },
    end: function() {
      return --this.active === 0 && (delete this.that.__zooming, this.emit("end")), this;
    },
    emit: function(b) {
      var N = be(this.that).datum();
      a.call(
        b,
        this.that,
        new kd(b, {
          sourceEvent: this.sourceEvent,
          target: y,
          transform: this.that.__zoom,
          dispatch: a
        }),
        N
      );
    }
  };
  function A(b, ...N) {
    if (!e.apply(this, arguments)) return;
    var E = S(this, N).event(b), M = this.__zoom, P = Math.max(i[0], Math.min(i[1], M.k * Math.pow(2, o.apply(this, arguments)))), D = Me(b);
    if (E.wheel)
      (E.mouse[0][0] !== D[0] || E.mouse[0][1] !== D[1]) && (E.mouse[1] = M.invert(E.mouse[0] = D)), clearTimeout(E.wheel);
    else {
      if (M.k === P) return;
      E.mouse = [D, M.invert(D)], tn(this), E.start();
    }
    wt(b), E.wheel = setTimeout(O, g), E.zoom("mouse", n(h(_(M, P), E.mouse[0], E.mouse[1]), E.extent, s));
    function O() {
      E.wheel = null, E.end();
    }
  }
  function $(b, ...N) {
    if (f || !e.apply(this, arguments)) return;
    var E = b.currentTarget, M = S(this, N, !0).event(b), P = be(b.view).on("mousemove.zoom", H, !0).on("mouseup.zoom", X, !0), D = Me(b, E), O = b.clientX, L = b.clientY;
    gi(b.view), Tn(b), M.mouse = [D, this.__zoom.invert(D)], tn(this), M.start();
    function H(Y) {
      if (wt(Y), !M.moved) {
        var Z = Y.clientX - O, ee = Y.clientY - L;
        M.moved = Z * Z + ee * ee > v;
      }
      M.event(Y).zoom("mouse", n(h(M.that.__zoom, M.mouse[0] = Me(Y, E), M.mouse[1]), M.extent, s));
    }
    function X(Y) {
      P.on("mousemove.zoom mouseup.zoom", null), pi(Y.view, M.moved), wt(Y), M.event(Y).end();
    }
  }
  function V(b, ...N) {
    if (e.apply(this, arguments)) {
      var E = this.__zoom, M = Me(b.changedTouches ? b.changedTouches[0] : b, this), P = E.invert(M), D = E.k * (b.shiftKey ? 0.5 : 2), O = n(h(_(E, D), M, P), t.apply(this, N), s);
      wt(b), c > 0 ? be(this).transition().duration(c).call(C, O, M, b) : be(this).call(y.transform, O, M, b);
    }
  }
  function k(b, ...N) {
    if (e.apply(this, arguments)) {
      var E = b.touches, M = E.length, P = S(this, N, b.changedTouches.length === M).event(b), D, O, L, H;
      for (Tn(b), O = 0; O < M; ++O)
        L = E[O], H = Me(L, this), H = [H, this.__zoom.invert(H), L.identifier], P.touch0 ? !P.touch1 && P.touch0[2] !== H[2] && (P.touch1 = H, P.taps = 0) : (P.touch0 = H, D = !0, P.taps = 1 + !!u);
      u && (u = clearTimeout(u)), D && (P.taps < 2 && (d = H[0], u = setTimeout(function() {
        u = null;
      }, p)), tn(this), P.start());
    }
  }
  function T(b, ...N) {
    if (this.__zooming) {
      var E = S(this, N).event(b), M = b.changedTouches, P = M.length, D, O, L, H;
      for (wt(b), D = 0; D < P; ++D)
        O = M[D], L = Me(O, this), E.touch0 && E.touch0[2] === O.identifier ? E.touch0[0] = L : E.touch1 && E.touch1[2] === O.identifier && (E.touch1[0] = L);
      if (O = E.that.__zoom, E.touch1) {
        var X = E.touch0[0], Y = E.touch0[1], Z = E.touch1[0], ee = E.touch1[1], U = (U = Z[0] - X[0]) * U + (U = Z[1] - X[1]) * U, R = (R = ee[0] - Y[0]) * R + (R = ee[1] - Y[1]) * R;
        O = _(O, Math.sqrt(U / R)), L = [(X[0] + Z[0]) / 2, (X[1] + Z[1]) / 2], H = [(Y[0] + ee[0]) / 2, (Y[1] + ee[1]) / 2];
      } else if (E.touch0) L = E.touch0[0], H = E.touch0[1];
      else return;
      E.zoom("touch", n(h(O, L, H), E.extent, s));
    }
  }
  function j(b, ...N) {
    if (this.__zooming) {
      var E = S(this, N).event(b), M = b.changedTouches, P = M.length, D, O;
      for (Tn(b), f && clearTimeout(f), f = setTimeout(function() {
        f = null;
      }, p), D = 0; D < P; ++D)
        O = M[D], E.touch0 && E.touch0[2] === O.identifier ? delete E.touch0 : E.touch1 && E.touch1[2] === O.identifier && delete E.touch1;
      if (E.touch1 && !E.touch0 && (E.touch0 = E.touch1, delete E.touch1), E.touch0) E.touch0[1] = this.__zoom.invert(E.touch0[0]);
      else if (E.end(), E.taps === 2 && (O = Me(O, this), Math.hypot(d[0] - O[0], d[1] - O[1]) < w)) {
        var L = be(this).on("dblclick.zoom");
        L && L.apply(this, arguments);
      }
    }
  }
  return y.wheelDelta = function(b) {
    return arguments.length ? (o = typeof b == "function" ? b : qt(+b), y) : o;
  }, y.filter = function(b) {
    return arguments.length ? (e = typeof b == "function" ? b : qt(!!b), y) : e;
  }, y.touchable = function(b) {
    return arguments.length ? (r = typeof b == "function" ? b : qt(!!b), y) : r;
  }, y.extent = function(b) {
    return arguments.length ? (t = typeof b == "function" ? b : qt([[+b[0][0], +b[0][1]], [+b[1][0], +b[1][1]]]), y) : t;
  }, y.scaleExtent = function(b) {
    return arguments.length ? (i[0] = +b[0], i[1] = +b[1], y) : [i[0], i[1]];
  }, y.translateExtent = function(b) {
    return arguments.length ? (s[0][0] = +b[0][0], s[1][0] = +b[1][0], s[0][1] = +b[0][1], s[1][1] = +b[1][1], y) : [[s[0][0], s[0][1]], [s[1][0], s[1][1]]];
  }, y.constrain = function(b) {
    return arguments.length ? (n = b, y) : n;
  }, y.duration = function(b) {
    return arguments.length ? (c = +b, y) : c;
  }, y.interpolate = function(b) {
    return arguments.length ? (l = b, y) : l;
  }, y.on = function() {
    var b = a.on.apply(a, arguments);
    return b === a ? y : b;
  }, y.clickDistance = function(b) {
    return arguments.length ? (v = (b = +b) * b, y) : Math.sqrt(v);
  }, y.tapDistance = function(b) {
    return arguments.length ? (w = +b, y) : w;
  }, y;
}
const De = {
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
}, It = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], $i = ["Enter", " ", "Escape"], Pi = {
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
var ut;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(ut || (ut = {}));
var Ge;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(Ge || (Ge = {}));
var At;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(At || (At = {}));
const Ti = {
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
var Ye;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(Ye || (Ye = {}));
var un;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(un || (un = {}));
var W;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(W || (W = {}));
const ir = {
  [W.Left]: W.Right,
  [W.Right]: W.Left,
  [W.Top]: W.Bottom,
  [W.Bottom]: W.Top
};
function zi(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Ri = (e) => "id" in e && "source" in e && "target" in e, Td = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), yo = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), Lt = (e, t = [0, 0]) => {
  const { width: n, height: o } = Ve(e), r = e.origin ?? t, i = n * r[0], s = o * r[1];
  return {
    x: e.position.x - i,
    y: e.position.y - s
  };
}, zd = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const i = typeof r == "string";
    let s = !t.nodeLookup && !i ? r : void 0;
    t.nodeLookup && (s = i ? t.nodeLookup.get(r) : yo(r) ? r : t.nodeLookup.get(r.id));
    const c = s ? dn(s, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return vn(o, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return bn(n);
}, Ht = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = vn(n, dn(r)), o = !0);
  }), o ? bn(n) : { x: 0, y: 0, width: 0, height: 0 };
}, xo = (e, t, [n, o, r] = [0, 0, 1], i = !1, s = !1) => {
  const c = {
    ...mt(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, l = [];
  for (const a of e.values()) {
    const { measured: u, selectable: d = !0, hidden: f = !1 } = a;
    if (s && !d || f)
      continue;
    const p = u.width ?? a.width ?? a.initialWidth ?? null, g = u.height ?? a.height ?? a.initialHeight ?? null, v = Dt(c, ft(a)), w = (p ?? 0) * (g ?? 0), y = i && v > 0;
    (!a.internals.handleBounds || y || v >= w || a.dragging) && l.push(a);
  }
  return l;
}, Rd = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function jd(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function Ld({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: i }, s) {
  if (e.size === 0)
    return !0;
  const c = jd(e, s), l = Ht(c), a = vo(l, t, n, s?.minZoom ?? r, s?.maxZoom ?? i, s?.padding ?? 0.1);
  return await o.setViewport(a, {
    duration: s?.duration,
    ease: s?.ease,
    interpolate: s?.interpolate
  }), !0;
}
function ji({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: i }) {
  const s = n.get(e), c = s.parentId ? n.get(s.parentId) : void 0, { x: l, y: a } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, u = s.origin ?? o;
  let d = s.extent || r;
  if (s.extent === "parent" && !s.expandParent)
    if (!c)
      i?.("005", De.error005());
    else {
      const p = c.measured.width, g = c.measured.height;
      p && g && (d = [
        [l, a],
        [l + p, a + g]
      ]);
    }
  else c && et(s.extent) && (d = [
    [s.extent[0][0] + l, s.extent[0][1] + a],
    [s.extent[1][0] + l, s.extent[1][1] + a]
  ]);
  const f = et(d) ? Je(t, d, s.measured) : t;
  return (s.measured.width === void 0 || s.measured.height === void 0) && i?.("015", De.error015()), {
    position: {
      x: f.x - l + (s.measured.width ?? 0) * u[0],
      y: f.y - a + (s.measured.height ?? 0) * u[1]
    },
    positionAbsolute: f
  };
}
async function Hd({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const i = new Set(e.map((f) => f.id)), s = [];
  for (const f of n) {
    if (f.deletable === !1)
      continue;
    const p = i.has(f.id), g = !p && f.parentId && s.find((v) => v.id === f.parentId);
    (p || g) && s.push(f);
  }
  const c = new Set(t.map((f) => f.id)), l = o.filter((f) => f.deletable !== !1), u = Rd(s, l);
  for (const f of l)
    c.has(f.id) && !u.find((g) => g.id === f.id) && u.push(f);
  if (!r)
    return {
      edges: u,
      nodes: s
    };
  const d = await r({
    nodes: s,
    edges: u
  });
  return typeof d == "boolean" ? d ? { edges: u, nodes: s } : { edges: [], nodes: [] } : d;
}
const dt = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), Je = (e = { x: 0, y: 0 }, t, n) => ({
  x: dt(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: dt(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Li(e, t, n) {
  const { width: o, height: r } = Ve(n), { x: i, y: s } = n.internals.positionAbsolute;
  return Je(e, [
    [i, s],
    [i + o, s + r]
  ], t);
}
const sr = (e, t, n) => e < t ? dt(Math.abs(e - t), 1, t) / t : e > n ? -dt(Math.abs(e - n), 1, t) / t : 0, wo = (e, t, n = 15, o = 40) => {
  const r = sr(e.x, o, t.width - o) * n, i = sr(e.y, o, t.height - o) * n;
  return [r, i];
}, vn = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), to = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), bn = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), ft = (e, t = [0, 0]) => {
  const { x: n, y: o } = yo(e) ? e.internals.positionAbsolute : Lt(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, dn = (e, t = [0, 0]) => {
  const { x: n, y: o } = yo(e) ? e.internals.positionAbsolute : Lt(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, Hi = (e, t) => bn(vn(to(e), to(t))), Dt = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, ar = (e) => Ie(e.width) && Ie(e.height) && Ie(e.x) && Ie(e.y), Ie = (e) => !isNaN(e) && isFinite(e), Oi = (e, t) => (n, o) => {
}, Ot = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), mt = ({ x: e, y: t }, [n, o, r], i = !1, s = [1, 1]) => {
  const c = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return i ? Ot(c, s) : c;
}, ht = ({ x: e, y: t }, [n, o, r]) => ({
  x: e * r + n,
  y: t * r + o
});
function ot(e, t) {
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
function Od(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = ot(e, n), r = ot(e, t);
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
    const o = ot(e.top ?? e.y ?? 0, n), r = ot(e.bottom ?? e.y ?? 0, n), i = ot(e.left ?? e.x ?? 0, t), s = ot(e.right ?? e.x ?? 0, t);
    return { top: o, right: s, bottom: r, left: i, x: i + s, y: o + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function Vd(e, t, n, o, r, i) {
  const { x: s, y: c } = ht(e, [t, n, o]), { x: l, y: a } = ht({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), u = r - l, d = i - a;
  return {
    left: Math.floor(s),
    top: Math.floor(c),
    right: Math.floor(u),
    bottom: Math.floor(d)
  };
}
const vo = (e, t, n, o, r, i) => {
  const s = Od(i, t, n), c = (t - s.x) / e.width, l = (n - s.y) / e.height, a = Math.min(c, l), u = dt(a, o, r), d = e.x + e.width / 2, f = e.y + e.height / 2, p = t / 2 - d * u, g = n / 2 - f * u, v = Vd(e, p, g, u, t, n), w = {
    left: Math.min(v.left - s.left, 0),
    top: Math.min(v.top - s.top, 0),
    right: Math.min(v.right - s.right, 0),
    bottom: Math.min(v.bottom - s.bottom, 0)
  };
  return {
    x: p - w.left + w.right,
    y: g - w.top + w.bottom,
    zoom: u
  };
}, $t = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function et(e) {
  return e != null && e !== "parent";
}
function Ve(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function Vi(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function Bi(e, t = { width: 0, height: 0 }, n, o, r) {
  const i = { ...e }, s = o.get(n);
  if (s) {
    const c = s.origin || r;
    i.x += s.internals.positionAbsolute.x - (t.width ?? 0) * c[0], i.y += s.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return i;
}
function cr(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function Bd() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function Fd(e) {
  return { ...Pi, ...e || {} };
}
function Et(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: i, y: s } = Ae(e), c = mt({ x: i - (r?.left ?? 0), y: s - (r?.top ?? 0) }, o), { x: l, y: a } = n ? Ot(c, t) : c;
  return {
    xSnapped: l,
    ySnapped: a,
    ...c
  };
}
const bo = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), Fi = (e) => e?.getRootNode?.() || window?.document, Yd = ["INPUT", "SELECT", "TEXTAREA"];
function Yi(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : Yd.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const Xi = (e) => "clientX" in e, Ae = (e, t) => {
  const n = Xi(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, lr = (e, t, n, o, r) => {
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
      ...bo(s)
    };
  });
};
function qi({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: i, targetControlX: s, targetControlY: c }) {
  const l = e * 0.125 + r * 0.375 + s * 0.375 + n * 0.125, a = t * 0.125 + i * 0.375 + c * 0.375 + o * 0.125, u = Math.abs(l - e), d = Math.abs(a - t);
  return [l, a, u, d];
}
function Wt(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function ur({ pos: e, x1: t, y1: n, x2: o, y2: r, c: i }) {
  switch (e) {
    case W.Left:
      return [t - Wt(t - o, i), n];
    case W.Right:
      return [t + Wt(o - t, i), n];
    case W.Top:
      return [t, n - Wt(n - r, i)];
    case W.Bottom:
      return [t, n + Wt(r - n, i)];
  }
}
function Wi({ sourceX: e, sourceY: t, sourcePosition: n = W.Bottom, targetX: o, targetY: r, targetPosition: i = W.Top, curvature: s = 0.25 }) {
  const [c, l] = ur({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: s
  }), [a, u] = ur({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: s
  }), [d, f, p, g] = qi({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: r,
    sourceControlX: c,
    sourceControlY: l,
    targetControlX: a,
    targetControlY: u
  });
  return [
    `M${e},${t} C${c},${l} ${a},${u} ${o},${r}`,
    d,
    f,
    p,
    g
  ];
}
function Zi({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, i = n < e ? n + r : n - r, s = Math.abs(o - t) / 2, c = o < t ? o + s : o - s;
  return [i, c, r, s];
}
function Xd({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: i = "basic" }) {
  if (i === "manual")
    return o;
  const s = r && n ? o + 1e3 : o, c = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return s + c;
}
function qd({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const i = vn(dn(e), dn(t));
  i.x === i.x2 && (i.x2 += 1), i.y === i.y2 && (i.y2 += 1);
  const s = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return Dt(s, bn(i)) > 0;
}
const Wd = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, Zd = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), Ud = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", De.error006()), t;
  const o = n.getEdgeId || Wd;
  let r;
  return Ri(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, Zd(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
};
function Ui({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, i, s, c] = Zi({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, i, s, c];
}
const dr = {
  [W.Left]: { x: -1, y: 0 },
  [W.Right]: { x: 1, y: 0 },
  [W.Top]: { x: 0, y: -1 },
  [W.Bottom]: { x: 0, y: 1 }
}, Gd = ({ source: e, sourcePosition: t = W.Bottom, target: n }) => t === W.Left || t === W.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, fr = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Kd({ source: e, sourcePosition: t = W.Bottom, target: n, targetPosition: o = W.Top, center: r, offset: i, stepPosition: s }) {
  const c = dr[t], l = dr[o], a = { x: e.x + c.x * i, y: e.y + c.y * i }, u = { x: n.x + l.x * i, y: n.y + l.y * i }, d = Gd({
    source: a,
    sourcePosition: t,
    target: u
  }), f = d.x !== 0 ? "x" : "y", p = d[f];
  let g = [], v, w;
  const y = { x: 0, y: 0 }, _ = { x: 0, y: 0 }, [, , h, x] = Zi({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[f] * l[f] === -1) {
    f === "x" ? (v = r.x ?? a.x + (u.x - a.x) * s, w = r.y ?? (a.y + u.y) / 2) : (v = r.x ?? (a.x + u.x) / 2, w = r.y ?? a.y + (u.y - a.y) * s);
    const A = [
      { x: v, y: a.y },
      { x: v, y: u.y }
    ], $ = [
      { x: a.x, y: w },
      { x: u.x, y: w }
    ];
    c[f] === p ? g = f === "x" ? A : $ : g = f === "x" ? $ : A;
  } else {
    const A = [{ x: a.x, y: u.y }], $ = [{ x: u.x, y: a.y }];
    if (f === "x" ? g = c.x === p ? $ : A : g = c.y === p ? A : $, t === o) {
      const b = Math.abs(e[f] - n[f]);
      if (b <= i) {
        const N = Math.min(i - 1, i - b);
        c[f] === p ? y[f] = (a[f] > e[f] ? -1 : 1) * N : _[f] = (u[f] > n[f] ? -1 : 1) * N;
      }
    }
    if (t !== o) {
      const b = f === "x" ? "y" : "x", N = c[f] === l[b], E = a[b] > u[b], M = a[b] < u[b];
      (c[f] === 1 && (!N && E || N && M) || c[f] !== 1 && (!N && M || N && E)) && (g = f === "x" ? A : $);
    }
    const V = { x: a.x + y.x, y: a.y + y.y }, k = { x: u.x + _.x, y: u.y + _.y }, T = Math.max(Math.abs(V.x - g[0].x), Math.abs(k.x - g[0].x)), j = Math.max(Math.abs(V.y - g[0].y), Math.abs(k.y - g[0].y));
    T >= j ? (v = (V.x + k.x) / 2, w = g[0].y) : (v = g[0].x, w = (V.y + k.y) / 2);
  }
  const C = { x: a.x + y.x, y: a.y + y.y }, S = { x: u.x + _.x, y: u.y + _.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...C.x !== g[0].x || C.y !== g[0].y ? [C] : [],
    ...g,
    ...S.x !== g[g.length - 1].x || S.y !== g[g.length - 1].y ? [S] : [],
    n
  ], v, w, h, x];
}
function Qd(e, t, n, o) {
  const r = Math.min(fr(e, t) / 2, fr(t, n) / 2, o), { x: i, y: s } = t;
  if (e.x === i && i === n.x || e.y === s && s === n.y)
    return `L${i} ${s}`;
  if (e.y === s) {
    const a = e.x < n.x ? -1 : 1, u = e.y < n.y ? 1 : -1;
    return `L ${i + r * a},${s}Q ${i},${s} ${i},${s + r * u}`;
  }
  const c = e.x < n.x ? 1 : -1, l = e.y < n.y ? -1 : 1;
  return `L ${i},${s + r * l}Q ${i},${s} ${i + r * c},${s}`;
}
function no({ sourceX: e, sourceY: t, sourcePosition: n = W.Bottom, targetX: o, targetY: r, targetPosition: i = W.Top, borderRadius: s = 5, centerX: c, centerY: l, offset: a = 20, stepPosition: u = 0.5 }) {
  const [d, f, p, g, v] = Kd({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: i,
    center: { x: c, y: l },
    offset: a,
    stepPosition: u
  });
  let w = `M${d[0].x} ${d[0].y}`;
  for (let y = 1; y < d.length - 1; y++)
    w += Qd(d[y - 1], d[y], d[y + 1], s);
  return w += `L${d[d.length - 1].x} ${d[d.length - 1].y}`, [w, f, p, g, v];
}
function hr(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function Jd(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!hr(t) || !hr(n))
    return null;
  const o = t.internals.handleBounds || gr(t.handles), r = n.internals.handleBounds || gr(n.handles), i = pr(o?.source ?? [], e.sourceHandle), s = pr(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === ut.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!i || !s)
    return e.onError?.("008", De.error008(i ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = i?.position || W.Bottom, l = s?.position || W.Top, a = tt(t, i, c), u = tt(n, s, l);
  return {
    sourceX: a.x,
    sourceY: a.y,
    targetX: u.x,
    targetY: u.y,
    sourcePosition: c,
    targetPosition: l
  };
}
function gr(e) {
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
function tt(e, t, n = W.Left, o = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, i = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: s, height: c } = t ?? Ve(e);
  if (o)
    return { x: r + s / 2, y: i + c / 2 };
  switch (t?.position ?? n) {
    case W.Top:
      return { x: r + s / 2, y: i };
    case W.Right:
      return { x: r + s, y: i + c / 2 };
    case W.Bottom:
      return { x: r + s / 2, y: i + c };
    case W.Left:
      return { x: r, y: i + c / 2 };
  }
}
function pr(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function oo(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function ef(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const i = /* @__PURE__ */ new Set();
  return e.reduce((s, c) => ([c.markerStart || o, c.markerEnd || r].forEach((l) => {
    if (l && typeof l == "object") {
      const a = oo(l, t);
      i.has(a) || (s.push({ id: a, color: l.color || n, ...l }), i.add(a));
    }
  }), s), []).sort((s, c) => s.id.localeCompare(c.id));
}
const Gi = 1e3, tf = 10, _o = {
  nodeOrigin: [0, 0],
  nodeExtent: It,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, nf = {
  ..._o,
  checkEquality: !0
};
function So(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function of(e, t, n) {
  const o = So(_o, n);
  for (const r of e.values())
    if (r.parentId)
      No(r, e, t, o);
    else {
      const i = Lt(r, o.nodeOrigin), s = et(r.extent) ? r.extent : o.nodeExtent, c = Je(i, s, Ve(r));
      r.internals.positionAbsolute = c;
    }
}
function rf(e, t) {
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
function Eo(e) {
  return e === "manual";
}
function ro(e, t, n, o = {}) {
  const r = So(nf, o), i = { i: 0 }, s = new Map(t), c = r?.elevateNodesOnSelect && !Eo(r.zIndexMode) ? Gi : 0;
  let l = e.length > 0, a = !1;
  t.clear(), n.clear();
  for (const u of e) {
    let d = s.get(u.id);
    if (r.checkEquality && u === d?.internals.userNode)
      t.set(u.id, d);
    else {
      const f = Lt(u, r.nodeOrigin), p = et(u.extent) ? u.extent : r.nodeExtent, g = Je(f, p, Ve(u));
      d = {
        ...r.defaults,
        ...u,
        measured: {
          width: u.measured?.width,
          height: u.measured?.height
        },
        internals: {
          positionAbsolute: g,
          // if user re-initializes the node or removes `measured` for whatever reason, we reset the handleBounds so that the node gets re-measured
          handleBounds: rf(u, d),
          z: Ki(u, c, r.zIndexMode),
          userNode: u
        }
      }, t.set(u.id, d);
    }
    (d.measured === void 0 || d.measured.width === void 0 || d.measured.height === void 0) && !d.hidden && (l = !1), u.parentId && No(d, t, n, o, i), a ||= u.selected ?? !1;
  }
  return { nodesInitialized: l, hasSelectedNodes: a };
}
function sf(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function No(e, t, n, o, r) {
  const { elevateNodesOnSelect: i, nodeOrigin: s, nodeExtent: c, zIndexMode: l } = So(_o, o), a = e.parentId, u = t.get(a);
  if (!u) {
    console.warn(`Parent node ${a} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  sf(e, n), r && !u.parentId && u.internals.rootParentIndex === void 0 && l === "auto" && (u.internals.rootParentIndex = ++r.i, u.internals.z = u.internals.z + r.i * tf), r && u.internals.rootParentIndex !== void 0 && (r.i = u.internals.rootParentIndex);
  const d = i && !Eo(l) ? Gi : 0, { x: f, y: p, z: g } = af(e, u, s, c, d, l), { positionAbsolute: v } = e.internals, w = f !== v.x || p !== v.y;
  (w || g !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: w ? { x: f, y: p } : v,
      z: g
    }
  });
}
function Ki(e, t, n) {
  const o = Ie(e.zIndex) ? e.zIndex : 0;
  return Eo(n) ? o : o + (e.selected ? t : 0);
}
function af(e, t, n, o, r, i) {
  const { x: s, y: c } = t.internals.positionAbsolute, l = Ve(e), a = Lt(e, n), u = et(e.extent) ? Je(a, e.extent, l) : a;
  let d = Je({ x: s + u.x, y: c + u.y }, o, l);
  e.extent === "parent" && (d = Li(d, l, t));
  const f = Ki(e, r, i), p = t.internals.z ?? 0;
  return {
    x: d.x,
    y: d.y,
    z: p >= f ? p + 1 : f
  };
}
function Co(e, t, n, o = [0, 0]) {
  const r = [], i = /* @__PURE__ */ new Map();
  for (const s of e) {
    const c = t.get(s.parentId);
    if (!c)
      continue;
    const l = i.get(s.parentId)?.expandedRect ?? ft(c), a = Hi(l, s.rect);
    i.set(s.parentId, { expandedRect: a, parent: c });
  }
  return i.size > 0 && i.forEach(({ expandedRect: s, parent: c }, l) => {
    const a = c.internals.positionAbsolute, u = Ve(c), d = c.origin ?? o, f = s.x < a.x ? Math.round(Math.abs(a.x - s.x)) : 0, p = s.y < a.y ? Math.round(Math.abs(a.y - s.y)) : 0, g = Math.max(u.width, Math.round(s.width)), v = Math.max(u.height, Math.round(s.height)), w = (g - u.width) * d[0], y = (v - u.height) * d[1];
    (f > 0 || p > 0 || w || y) && (r.push({
      id: l,
      type: "position",
      position: {
        x: c.position.x - f + w,
        y: c.position.y - p + y
      }
    }), n.get(l)?.forEach((_) => {
      e.some((h) => h.id === _.id) || r.push({
        id: _.id,
        type: "position",
        position: {
          x: _.position.x + f,
          y: _.position.y + p
        }
      });
    })), (u.width < s.width || u.height < s.height || f || p) && r.push({
      id: l,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: g + (f ? d[0] * f - w : 0),
        height: v + (p ? d[1] * p - y : 0)
      }
    });
  }), r;
}
function cf(e, t, n, o, r, i, s) {
  const c = o?.querySelector(".xyflow__viewport");
  let l = !1;
  if (!c)
    return { changes: [], updatedInternals: l };
  const a = [], u = window.getComputedStyle(c), { m22: d } = new window.DOMMatrixReadOnly(u.transform), f = [];
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
      }), l = !0;
      continue;
    }
    const v = bo(p.nodeElement), w = g.measured.width !== v.width || g.measured.height !== v.height;
    if (!!(v.width && v.height && (w || !g.internals.handleBounds || p.force))) {
      const _ = p.nodeElement.getBoundingClientRect(), h = et(g.extent) ? g.extent : i;
      let { positionAbsolute: x } = g.internals;
      g.parentId && g.extent === "parent" ? x = Li(x, v, t.get(g.parentId)) : h && (x = Je(x, h, v));
      const C = {
        ...g,
        measured: v,
        internals: {
          ...g.internals,
          positionAbsolute: x,
          handleBounds: {
            source: lr("source", p.nodeElement, _, d, g.id),
            target: lr("target", p.nodeElement, _, d, g.id)
          }
        }
      };
      t.set(g.id, C), g.parentId && No(C, t, n, { nodeOrigin: r, zIndexMode: s }), l = !0, w && (a.push({
        id: g.id,
        type: "dimensions",
        dimensions: v
      }), g.expandParent && g.parentId && f.push({
        id: g.id,
        parentId: g.parentId,
        rect: ft(C, r)
      }));
    }
  }
  if (f.length > 0) {
    const p = Co(f, t, n, r);
    a.push(...p);
  }
  return { changes: a, updatedInternals: l };
}
async function lf({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: i }) {
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
function mr(e, t, n, o, r, i) {
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
function Qi(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: i, sourceHandle: s = null, targetHandle: c = null } = o, l = { edgeId: o.id, source: r, target: i, sourceHandle: s, targetHandle: c }, a = `${r}-${s}--${i}-${c}`, u = `${i}-${c}--${r}-${s}`;
    mr("source", l, u, e, r, s), mr("target", l, a, e, i, c), t.set(o.id, o);
  }
}
function Ji(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : Ji(n, t) : !1;
}
function yr(e, t, n) {
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
function uf(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [i, s] of e)
    if ((s.selected || s.id === o) && (!s.parentId || !Ji(s, e)) && (s.draggable || t && typeof s.draggable > "u")) {
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
function zn({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
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
function df({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const i = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, s = Ot(i, t);
  return {
    x: s.x - i.x,
    y: s.y - i.y
  };
}
function ff({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let i = { x: null, y: null }, s = 0, c = /* @__PURE__ */ new Map(), l = !1, a = { x: 0, y: 0 }, u = null, d = !1, f = null, p = !1, g = !1, v = null;
  function w({ noDragClassName: _, handleSelector: h, domNode: x, isSelectable: C, nodeId: S, nodeClickDistance: I = 0 }) {
    f = be(x);
    function A({ x: T, y: j }) {
      const { nodeLookup: b, nodeExtent: N, snapGrid: E, snapToGrid: M, nodeOrigin: P, onNodeDrag: D, onSelectionDrag: O, onError: L, updateNodePositions: H } = t();
      i = { x: T, y: j };
      let X = !1;
      const Y = c.size > 1, Z = Y && N ? to(Ht(c)) : null, ee = Y && M ? df({
        dragItems: c,
        snapGrid: E,
        x: T,
        y: j
      }) : null;
      for (const [U, R] of c) {
        if (!b.has(U))
          continue;
        let F = { x: T - R.distance.x, y: j - R.distance.y };
        M && (F = ee ? {
          x: Math.round(F.x + ee.x),
          y: Math.round(F.y + ee.y)
        } : Ot(F, E));
        let J = null;
        if (Y && N && !R.extent && Z) {
          const { positionAbsolute: G } = R.internals, te = G.x - Z.x + N[0][0], se = G.x + R.measured.width - Z.x2 + N[1][0], de = G.y - Z.y + N[0][1], me = G.y + R.measured.height - Z.y2 + N[1][1];
          J = [
            [te, de],
            [se, me]
          ];
        }
        const { position: K, positionAbsolute: q } = ji({
          nodeId: U,
          nextPosition: F,
          nodeLookup: b,
          nodeExtent: J || N,
          nodeOrigin: P,
          onError: L
        });
        X = X || R.position.x !== K.x || R.position.y !== K.y, R.position = K, R.internals.positionAbsolute = q;
      }
      if (g = g || X, !!X && (H(c, !0), v && (o || D || !S && O))) {
        const [U, R] = zn({
          nodeId: S,
          dragItems: c,
          nodeLookup: b
        });
        o?.(v, c, U, R), D?.(v, U, R), S || O?.(v, R);
      }
    }
    async function $() {
      if (!u)
        return;
      const { transform: T, panBy: j, autoPanSpeed: b, autoPanOnNodeDrag: N } = t();
      if (!N) {
        l = !1, cancelAnimationFrame(s);
        return;
      }
      const [E, M] = wo(a, u, b);
      (E !== 0 || M !== 0) && (i.x = (i.x ?? 0) - E / T[2], i.y = (i.y ?? 0) - M / T[2], await j({ x: E, y: M }) && A(i)), s = requestAnimationFrame($);
    }
    function V(T) {
      const { nodeLookup: j, multiSelectionActive: b, nodesDraggable: N, transform: E, snapGrid: M, snapToGrid: P, selectNodesOnDrag: D, onNodeDragStart: O, onSelectionDragStart: L, unselectNodesAndEdges: H } = t();
      d = !0, (!D || !C) && !b && S && (j.get(S)?.selected || H()), C && D && S && e?.(S);
      const X = Et(T.sourceEvent, { transform: E, snapGrid: M, snapToGrid: P, containerBounds: u });
      if (i = X, c = uf(j, N, X, S), c.size > 0 && (n || O || !S && L)) {
        const [Y, Z] = zn({
          nodeId: S,
          dragItems: c,
          nodeLookup: j
        });
        n?.(T.sourceEvent, c, Y, Z), O?.(T.sourceEvent, Y, Z), S || L?.(T.sourceEvent, Z);
      }
    }
    const k = mi().clickDistance(I).on("start", (T) => {
      const { domNode: j, nodeDragThreshold: b, transform: N, snapGrid: E, snapToGrid: M } = t();
      u = j?.getBoundingClientRect() || null, p = !1, g = !1, v = T.sourceEvent, b === 0 && V(T), i = Et(T.sourceEvent, { transform: N, snapGrid: E, snapToGrid: M, containerBounds: u }), a = Ae(T.sourceEvent, u);
    }).on("drag", (T) => {
      const { autoPanOnNodeDrag: j, transform: b, snapGrid: N, snapToGrid: E, nodeDragThreshold: M, nodeLookup: P } = t(), D = Et(T.sourceEvent, { transform: b, snapGrid: N, snapToGrid: E, containerBounds: u });
      if (v = T.sourceEvent, (T.sourceEvent.type === "touchmove" && T.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      S && !P.has(S)) && (p = !0), !p) {
        if (!l && j && d && (l = !0, $()), !d) {
          const O = Ae(T.sourceEvent, u), L = O.x - a.x, H = O.y - a.y;
          Math.sqrt(L * L + H * H) > M && V(T);
        }
        (i.x !== D.xSnapped || i.y !== D.ySnapped) && c && d && (a = Ae(T.sourceEvent, u), A(D));
      }
    }).on("end", (T) => {
      if (!d || p) {
        p && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (l = !1, d = !1, cancelAnimationFrame(s), c.size > 0) {
        const { nodeLookup: j, updateNodePositions: b, onNodeDragStop: N, onSelectionDragStop: E } = t();
        if (g && (b(c, !1), g = !1), r || N || !S && E) {
          const [M, P] = zn({
            nodeId: S,
            dragItems: c,
            nodeLookup: j,
            dragging: !1
          });
          r?.(T.sourceEvent, c, M, P), N?.(T.sourceEvent, M, P), S || E?.(T.sourceEvent, P);
        }
      }
    }).filter((T) => {
      const j = T.target;
      return !T.button && (!_ || !yr(j, `.${_}`, x)) && (!h || yr(j, h, x));
    });
    f.call(k);
  }
  function y() {
    f?.on(".drag", null);
  }
  return {
    update: w,
    destroy: y
  };
}
function hf(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const i of t.values())
    Dt(r, ft(i)) > 0 && o.push(i);
  return o;
}
const gf = 250;
function pf(e, t, n, o) {
  let r = [], i = 1 / 0;
  const s = hf(e, n, t + gf);
  for (const c of s) {
    const l = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const a of l) {
      if (o.nodeId === a.nodeId && o.type === a.type && o.id === a.id)
        continue;
      const { x: u, y: d } = tt(c, a, a.position, !0), f = Math.sqrt(Math.pow(u - e.x, 2) + Math.pow(d - e.y, 2));
      f > t || (f < i ? (r = [{ ...a, x: u, y: d }], i = f) : f === i && r.push({ ...a, x: u, y: d }));
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
function es(e, t, n, o, r, i = !1) {
  const s = o.get(e);
  if (!s)
    return null;
  const c = r === "strict" ? s.internals.handleBounds?.[t] : [...s.internals.handleBounds?.source ?? [], ...s.internals.handleBounds?.target ?? []], l = (n ? c?.find((a) => a.id === n) : c?.[0]) ?? null;
  return l && i ? { ...l, ...tt(s, l, l.position, !0) } : l;
}
function ts(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function mf(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const ns = () => !0;
function yf(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: i, isTarget: s, domNode: c, nodeLookup: l, lib: a, autoPanOnConnect: u, flowId: d, panBy: f, cancelConnection: p, onConnectStart: g, onConnect: v, onConnectEnd: w, isValidConnection: y = ns, onReconnectEnd: _, updateConnection: h, getTransform: x, getFromHandle: C, autoPanSpeed: S, dragThreshold: I = 1, handleDomNode: A }) {
  const $ = Fi(e.target);
  let V = 0, k;
  const { x: T, y: j } = Ae(e), b = ts(i, A), N = c?.getBoundingClientRect();
  let E = !1;
  if (!N || !b)
    return;
  const M = es(r, b, o, l, t);
  if (!M)
    return;
  let P = Ae(e, N), D = !1, O = null, L = !1, H = null;
  function X() {
    if (!u || !N)
      return;
    const [K, q] = wo(P, N, S);
    f({ x: K, y: q }), V = requestAnimationFrame(X);
  }
  const Y = {
    ...M,
    nodeId: r,
    type: b,
    position: M.position
  }, Z = l.get(r);
  let U = {
    inProgress: !0,
    isValid: null,
    from: tt(Z, Y, W.Left, !0),
    fromHandle: Y,
    fromPosition: Y.position,
    fromNode: Z,
    to: P,
    toHandle: null,
    toPosition: ir[Y.position],
    toNode: null,
    pointer: P
  };
  function R() {
    E = !0, h(U), g?.(e, { nodeId: r, handleId: o, handleType: b });
  }
  I === 0 && R();
  function F(K) {
    if (!E) {
      const { x: me, y: Ce } = Ae(K), Se = me - T, Ee = Ce - j;
      if (!(Se * Se + Ee * Ee > I * I))
        return;
      R();
    }
    if (!C() || !Y) {
      J(K);
      return;
    }
    const q = x();
    P = Ae(K, N), k = pf(mt(P, q, !1, [1, 1]), n, l, Y), D || (X(), D = !0);
    const G = os(K, {
      handle: k,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: s ? "target" : "source",
      isValidConnection: y,
      doc: $,
      lib: a,
      flowId: d,
      nodeLookup: l
    });
    H = G.handleDomNode, O = G.connection, L = mf(!!k, G.isValid);
    const te = l.get(r), se = te ? tt(te, Y, W.Left, !0) : U.from, de = {
      ...U,
      from: se,
      isValid: L,
      to: G.toHandle && L ? ht({ x: G.toHandle.x, y: G.toHandle.y }, q) : P,
      toHandle: G.toHandle,
      toPosition: L && G.toHandle ? G.toHandle.position : ir[Y.position],
      toNode: G.toHandle ? l.get(G.toHandle.nodeId) : null,
      pointer: P
    };
    h(de), U = de;
  }
  function J(K) {
    if (!("touches" in K && K.touches.length > 0)) {
      if (E) {
        (k || H) && O && L && v?.(O);
        const { inProgress: q, ...G } = U, te = {
          ...G,
          toPosition: U.toHandle ? U.toPosition : null
        };
        w?.(K, te), i && _?.(K, te);
      }
      p(), cancelAnimationFrame(V), D = !1, L = !1, O = null, H = null, $.removeEventListener("mousemove", F), $.removeEventListener("mouseup", J), $.removeEventListener("touchmove", F), $.removeEventListener("touchend", J);
    }
  }
  $.addEventListener("mousemove", F), $.addEventListener("mouseup", J), $.addEventListener("touchmove", F), $.addEventListener("touchend", J);
}
function os(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: i, doc: s, lib: c, flowId: l, isValidConnection: a = ns, nodeLookup: u }) {
  const d = i === "target", f = t ? s.querySelector(`.${c}-flow__handle[data-id="${l}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: p, y: g } = Ae(e), v = s.elementFromPoint(p, g), w = v?.classList.contains(`${c}-flow__handle`) ? v : f, y = {
    handleDomNode: w,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (w) {
    const _ = ts(void 0, w), h = w.getAttribute("data-nodeid"), x = w.getAttribute("data-handleid"), C = w.classList.contains("connectable"), S = w.classList.contains("connectableend");
    if (!h || !_)
      return y;
    const I = {
      source: d ? h : o,
      sourceHandle: d ? x : r,
      target: d ? o : h,
      targetHandle: d ? r : x
    };
    y.connection = I;
    const $ = C && S && (n === ut.Strict ? d && _ === "source" || !d && _ === "target" : h !== o || x !== r);
    y.isValid = $ && a(I), y.toHandle = es(h, _, x, u, n, !0);
  }
  return y;
}
const io = {
  onPointerDown: yf,
  isValid: os
};
function xf({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = be(e);
  function i({ translateExtent: c, width: l, height: a, zoomStep: u = 1, pannable: d = !0, zoomable: f = !0, inversePan: p = !1 }) {
    const g = (h) => {
      if (h.sourceEvent.type !== "wheel" || !t)
        return;
      const x = n(), C = h.sourceEvent.ctrlKey && $t() ? 10 : 1, S = -h.sourceEvent.deltaY * (h.sourceEvent.deltaMode === 1 ? 0.05 : h.sourceEvent.deltaMode ? 1 : 2e-3) * u, I = x[2] * Math.pow(2, S * C);
      t.scaleTo(I);
    };
    let v = [0, 0];
    const w = (h) => {
      (h.sourceEvent.type === "mousedown" || h.sourceEvent.type === "touchstart") && (v = [
        h.sourceEvent.clientX ?? h.sourceEvent.touches[0].clientX,
        h.sourceEvent.clientY ?? h.sourceEvent.touches[0].clientY
      ]);
    }, y = (h) => {
      const x = n();
      if (h.sourceEvent.type !== "mousemove" && h.sourceEvent.type !== "touchmove" || !t)
        return;
      const C = [
        h.sourceEvent.clientX ?? h.sourceEvent.touches[0].clientX,
        h.sourceEvent.clientY ?? h.sourceEvent.touches[0].clientY
      ], S = [C[0] - v[0], C[1] - v[1]];
      v = C;
      const I = o() * Math.max(x[2], Math.log(x[2])) * (p ? -1 : 1), A = {
        x: x[0] - S[0] * I,
        y: x[1] - S[1] * I
      }, $ = [
        [0, 0],
        [l, a]
      ];
      t.setViewportConstrained({
        x: A.x,
        y: A.y,
        zoom: x[2]
      }, $, c);
    }, _ = Di().on("start", w).on("zoom", d ? y : null).on("zoom.wheel", f ? g : null);
    r.call(_, {});
  }
  function s() {
    r.on("zoom", null);
  }
  return {
    update: i,
    destroy: s,
    pointer: Me
  };
}
const _n = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), Rn = ({ x: e, y: t, zoom: n }) => wn.translate(e, t).scale(n), rt = (e, t) => e.target.closest(`.${t}`), rs = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), wf = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, jn = (e, t = 0, n = wf, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, is = (e) => {
  const t = e.ctrlKey && $t() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function vf({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: i, zoomOnPinch: s, onPanZoomStart: c, onPanZoom: l, onPanZoomEnd: a }) {
  return (u) => {
    if (rt(u, t))
      return u.ctrlKey && u.preventDefault(), !1;
    u.preventDefault(), u.stopImmediatePropagation();
    const d = n.property("__zoom").k || 1;
    if (u.ctrlKey && s) {
      const w = Me(u), y = is(u), _ = d * Math.pow(2, y);
      o.scaleTo(n, _, w, u);
      return;
    }
    const f = u.deltaMode === 1 ? 20 : 1;
    let p = r === Ge.Vertical ? 0 : u.deltaX * f, g = r === Ge.Horizontal ? 0 : u.deltaY * f;
    !$t() && u.shiftKey && r !== Ge.Vertical && (p = u.deltaY * f, g = 0), o.translateBy(
      n,
      -(p / d) * i,
      -(g / d) * i,
      // @ts-ignore
      { internal: !0 }
    );
    const v = _n(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (l?.(u, v), e.panScrollTimeout = setTimeout(() => {
      a?.(u, v), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(u, v));
  };
}
function bf({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const i = o.type === "wheel", s = !t && i && !o.ctrlKey, c = rt(o, e);
    if (o.ctrlKey && i && c && o.preventDefault(), s || c)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function _f({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = _n(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function Sf({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (i) => {
    e.usedRightMouseButton = !!(n && rs(t, e.mouseButton ?? 0)), i.sourceEvent?.sync || o([i.transform.x, i.transform.y, i.transform.k]), r && !i.sourceEvent?.internal && r?.(i.sourceEvent, _n(i.transform));
  };
}
function Ef({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: i }) {
  return (s) => {
    if (!s.sourceEvent?.internal && (e.isZoomingOrPanning = !1, i && rs(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && s.sourceEvent && i(s.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
      const c = _n(s.transform);
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
function Nf({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: i, userSelectionActive: s, noWheelClassName: c, noPanClassName: l, lib: a, connectionInProgress: u }) {
  return (d) => {
    const f = e || t, p = n && d.ctrlKey, g = d.type === "wheel";
    if (d.button === 1 && d.type === "mousedown" && (rt(d, `${a}-flow__node`) || rt(d, `${a}-flow__edge`)))
      return !0;
    if (!o && !f && !r && !i && !n || s || u && !g || rt(d, c) && g || rt(d, l) && (!g || r && g && !e) || !n && d.ctrlKey && g)
      return !1;
    if (!n && d.type === "touchstart" && d.touches?.length > 1)
      return d.preventDefault(), !1;
    if (!f && !r && !p && g || !o && (d.type === "mousedown" || d.type === "touchstart") || Array.isArray(o) && !o.includes(d.button) && d.type === "mousedown")
      return !1;
    const v = Array.isArray(o) && o.includes(d.button) || !d.button || d.button <= 1;
    return (!d.ctrlKey || g) && v;
  };
}
function Cf({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: i, onPanZoomStart: s, onPanZoomEnd: c, onDraggingChange: l }) {
  const a = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, u = e.getBoundingClientRect(), d = Di().scaleExtent([t, n]).translateExtent(o), f = be(e).call(d);
  _({
    x: r.x,
    y: r.y,
    zoom: dt(r.zoom, t, n)
  }, [
    [0, 0],
    [u.width, u.height]
  ], o);
  const p = f.on("wheel.zoom"), g = f.on("dblclick.zoom");
  d.wheelDelta(is);
  async function v(k, T) {
    return f ? new Promise((j) => {
      d?.interpolate(T?.interpolate === "linear" ? St : Qt).transform(jn(f, T?.duration, T?.ease, () => j(!0)), k);
    }) : !1;
  }
  function w({ noWheelClassName: k, noPanClassName: T, onPaneContextMenu: j, userSelectionActive: b, panOnScroll: N, panOnDrag: E, panOnScrollMode: M, panOnScrollSpeed: P, preventScrolling: D, zoomOnPinch: O, zoomOnScroll: L, zoomOnDoubleClick: H, zoomActivationKeyPressed: X, lib: Y, onTransformChange: Z, connectionInProgress: ee, paneClickDistance: U, selectionOnDrag: R }) {
    b && !a.isZoomingOrPanning && y();
    const F = N && !X && !b;
    d.clickDistance(R ? 1 / 0 : !Ie(U) || U < 0 ? 0 : U);
    const J = F ? vf({
      zoomPanValues: a,
      noWheelClassName: k,
      d3Selection: f,
      d3Zoom: d,
      panOnScrollMode: M,
      panOnScrollSpeed: P,
      zoomOnPinch: O,
      onPanZoomStart: s,
      onPanZoom: i,
      onPanZoomEnd: c
    }) : bf({
      noWheelClassName: k,
      preventScrolling: D,
      d3ZoomHandler: p
    });
    f.on("wheel.zoom", J, { passive: !1 });
    const K = _f({
      zoomPanValues: a,
      onDraggingChange: l,
      onPanZoomStart: s
    });
    d.on("start", K);
    const q = Sf({
      zoomPanValues: a,
      panOnDrag: E,
      onPaneContextMenu: !!j,
      onPanZoom: i,
      onTransformChange: Z
    });
    d.on("zoom", q);
    const G = Ef({
      zoomPanValues: a,
      panOnDrag: E,
      panOnScroll: N,
      onPaneContextMenu: j,
      onPanZoomEnd: c,
      onDraggingChange: l
    });
    d.on("end", G);
    const te = Nf({
      zoomActivationKeyPressed: X,
      panOnDrag: E,
      zoomOnScroll: L,
      panOnScroll: N,
      zoomOnDoubleClick: H,
      zoomOnPinch: O,
      userSelectionActive: b,
      noPanClassName: T,
      noWheelClassName: k,
      lib: Y,
      connectionInProgress: ee
    });
    d.filter(te), H ? f.on("dblclick.zoom", g) : f.on("dblclick.zoom", null);
  }
  function y() {
    d.on("zoom", null);
  }
  async function _(k, T, j) {
    const b = Rn(k), N = d?.constrain()(b, T, j);
    return N && await v(N), N;
  }
  async function h(k, T) {
    const j = Rn(k);
    return await v(j, T), j;
  }
  function x(k) {
    if (f) {
      const T = Rn(k), j = f.property("__zoom");
      (j.k !== k.zoom || j.x !== k.x || j.y !== k.y) && d?.transform(f, T, null, { sync: !0 });
    }
  }
  function C() {
    const k = f ? Ai(f.node()) : { x: 0, y: 0, k: 1 };
    return { x: k.x, y: k.y, zoom: k.k };
  }
  async function S(k, T) {
    return f ? new Promise((j) => {
      d?.interpolate(T?.interpolate === "linear" ? St : Qt).scaleTo(jn(f, T?.duration, T?.ease, () => j(!0)), k);
    }) : !1;
  }
  async function I(k, T) {
    return f ? new Promise((j) => {
      d?.interpolate(T?.interpolate === "linear" ? St : Qt).scaleBy(jn(f, T?.duration, T?.ease, () => j(!0)), k);
    }) : !1;
  }
  function A(k) {
    d?.scaleExtent(k);
  }
  function $(k) {
    d?.translateExtent(k);
  }
  function V(k) {
    const T = !Ie(k) || k < 0 ? 0 : k;
    d?.clickDistance(T);
  }
  return {
    update: w,
    destroy: y,
    setViewport: h,
    setViewportConstrained: _,
    getViewport: C,
    scaleTo: S,
    scaleBy: I,
    setScaleExtent: A,
    setTranslateExtent: $,
    syncViewport: x,
    setClickDistance: V
  };
}
var gt;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(gt || (gt = {}));
function Mf({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: i }) {
  const s = e - t, c = n - o, l = [s > 0 ? 1 : s < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return s && r && (l[0] = l[0] * -1), c && i && (l[1] = l[1] * -1), l;
}
function xr(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: r
  };
}
function Be(e, t) {
  return Math.max(0, t - e);
}
function Fe(e, t) {
  return Math.max(0, e - t);
}
function Zt(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function wr(e, t) {
  return e ? !t : t;
}
function kf(e, t, n, o, r, i, s, c) {
  let { affectsX: l, affectsY: a } = t;
  const { isHorizontal: u, isVertical: d } = t, f = u && d, { xSnapped: p, ySnapped: g } = n, { minWidth: v, maxWidth: w, minHeight: y, maxHeight: _ } = o, { x: h, y: x, width: C, height: S, aspectRatio: I } = e;
  let A = Math.floor(u ? p - e.pointerX : 0), $ = Math.floor(d ? g - e.pointerY : 0);
  const V = C + (l ? -A : A), k = S + (a ? -$ : $), T = -i[0] * C, j = -i[1] * S;
  let b = Zt(V, v, w), N = Zt(k, y, _);
  if (s) {
    let P = 0, D = 0;
    l && A < 0 ? P = Be(h + A + T, s[0][0]) : !l && A > 0 && (P = Fe(h + V + T, s[1][0])), a && $ < 0 ? D = Be(x + $ + j, s[0][1]) : !a && $ > 0 && (D = Fe(x + k + j, s[1][1])), b = Math.max(b, P), N = Math.max(N, D);
  }
  if (c) {
    let P = 0, D = 0;
    l && A > 0 ? P = Fe(h + A, c[0][0]) : !l && A < 0 && (P = Be(h + V, c[1][0])), a && $ > 0 ? D = Fe(x + $, c[0][1]) : !a && $ < 0 && (D = Be(x + k, c[1][1])), b = Math.max(b, P), N = Math.max(N, D);
  }
  if (r) {
    if (u) {
      const P = Zt(V / I, y, _) * I;
      if (b = Math.max(b, P), s) {
        let D = 0;
        !l && !a || l && !a && f ? D = Fe(x + j + V / I, s[1][1]) * I : D = Be(x + j + (l ? A : -A) / I, s[0][1]) * I, b = Math.max(b, D);
      }
      if (c) {
        let D = 0;
        !l && !a || l && !a && f ? D = Be(x + V / I, c[1][1]) * I : D = Fe(x + (l ? A : -A) / I, c[0][1]) * I, b = Math.max(b, D);
      }
    }
    if (d) {
      const P = Zt(k * I, v, w) / I;
      if (N = Math.max(N, P), s) {
        let D = 0;
        !l && !a || a && !l && f ? D = Fe(h + k * I + T, s[1][0]) / I : D = Be(h + (a ? $ : -$) * I + T, s[0][0]) / I, N = Math.max(N, D);
      }
      if (c) {
        let D = 0;
        !l && !a || a && !l && f ? D = Be(h + k * I, c[1][0]) / I : D = Fe(h + (a ? $ : -$) * I, c[0][0]) / I, N = Math.max(N, D);
      }
    }
  }
  $ = $ + ($ < 0 ? N : -N), A = A + (A < 0 ? b : -b), r && (f ? V > k * I ? $ = (wr(l, a) ? -A : A) / I : A = (wr(l, a) ? -$ : $) * I : u ? ($ = A / I, a = l) : (A = $ * I, l = a));
  const E = l ? h + A : h, M = a ? x + $ : x;
  return {
    width: C + (l ? -A : A),
    height: S + (a ? -$ : $),
    x: i[0] * A * (l ? -1 : 1) + E,
    y: i[1] * $ * (a ? -1 : 1) + M
  };
}
const ss = { width: 0, height: 0, x: 0, y: 0 }, If = {
  ...ss,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function Af(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, i = e.measured.width ?? 0, s = e.measured.height ?? 0, c = n[0] * i, l = n[1] * s;
  return [
    [o - c, r - l],
    [o + i - c, r + s - l]
  ];
}
function Df({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const i = be(e);
  let s = {
    controlDirection: xr("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: a, boundaries: u, keepAspectRatio: d, resizeDirection: f, onResizeStart: p, onResize: g, onResizeEnd: v, shouldResize: w }) {
    let y = { ...ss }, _ = { ...If };
    s = {
      boundaries: u,
      resizeDirection: f,
      keepAspectRatio: d,
      controlDirection: xr(a)
    };
    let h, x = null, C = [], S, I, A, $ = !1;
    const V = mi().on("start", (k) => {
      const { nodeLookup: T, transform: j, snapGrid: b, snapToGrid: N, nodeOrigin: E, paneDomNode: M } = n();
      if (h = T.get(t), !h)
        return;
      x = M?.getBoundingClientRect() ?? null;
      const { xSnapped: P, ySnapped: D } = Et(k.sourceEvent, {
        transform: j,
        snapGrid: b,
        snapToGrid: N,
        containerBounds: x
      });
      y = {
        width: h.measured.width ?? 0,
        height: h.measured.height ?? 0,
        x: h.position.x ?? 0,
        y: h.position.y ?? 0
      }, _ = {
        ...y,
        pointerX: P,
        pointerY: D,
        aspectRatio: y.width / y.height
      }, S = void 0, I = et(h.extent) ? h.extent : void 0, h.parentId && (h.extent === "parent" || h.expandParent) && (S = T.get(h.parentId)), S && h.extent === "parent" && (I = [
        [0, 0],
        [S.measured.width, S.measured.height]
      ]), C = [], A = void 0;
      for (const [O, L] of T)
        if (L.parentId === t && (C.push({
          id: O,
          position: { ...L.position },
          extent: L.extent
        }), L.extent === "parent" || L.expandParent)) {
          const H = Af(L, h, L.origin ?? E);
          A ? A = [
            [Math.min(H[0][0], A[0][0]), Math.min(H[0][1], A[0][1])],
            [Math.max(H[1][0], A[1][0]), Math.max(H[1][1], A[1][1])]
          ] : A = H;
        }
      p?.(k, { ...y });
    }).on("drag", (k) => {
      const { transform: T, snapGrid: j, snapToGrid: b, nodeOrigin: N } = n(), E = Et(k.sourceEvent, {
        transform: T,
        snapGrid: j,
        snapToGrid: b,
        containerBounds: x
      }), M = [];
      if (!h)
        return;
      const { x: P, y: D, width: O, height: L } = y, H = {}, X = h.origin ?? N, { width: Y, height: Z, x: ee, y: U } = kf(_, s.controlDirection, E, s.boundaries, s.keepAspectRatio, X, I, A), R = Y !== O, F = Z !== L, J = ee !== P && R, K = U !== D && F;
      if (!J && !K && !R && !F)
        return;
      if ((J || K || X[0] === 1 || X[1] === 1) && (H.x = J ? ee : y.x, H.y = K ? U : y.y, y.x = H.x, y.y = H.y, C.length > 0)) {
        const se = ee - P, de = U - D;
        for (const me of C)
          me.position = {
            x: me.position.x - se + X[0] * (Y - O),
            y: me.position.y - de + X[1] * (Z - L)
          }, M.push(me);
      }
      if ((R || F) && (H.width = R && (!s.resizeDirection || s.resizeDirection === "horizontal") ? Y : y.width, H.height = F && (!s.resizeDirection || s.resizeDirection === "vertical") ? Z : y.height, y.width = H.width, y.height = H.height), S && h.expandParent) {
        const se = X[0] * (H.width ?? 0);
        H.x && H.x < se && (y.x = se, _.x = _.x - (H.x - se));
        const de = X[1] * (H.height ?? 0);
        H.y && H.y < de && (y.y = de, _.y = _.y - (H.y - de));
      }
      const q = Mf({
        width: y.width,
        prevWidth: O,
        height: y.height,
        prevHeight: L,
        affectsX: s.controlDirection.affectsX,
        affectsY: s.controlDirection.affectsY
      }), G = { ...y, direction: q };
      w?.(k, G) !== !1 && ($ = !0, g?.(k, G), o(H, M));
    }).on("end", (k) => {
      $ && (v?.(k, { ...y }), r?.({ ...y }), $ = !1);
    });
    i.call(V);
  }
  function l() {
    i.on(".drag", null);
  }
  return {
    update: c,
    destroy: l
  };
}
var Ln = { exports: {} }, Hn = {}, On = { exports: {} }, Vn = {};
var vr;
function $f() {
  if (vr) return Vn;
  vr = 1;
  var e = Tt;
  function t(d, f) {
    return d === f && (d !== 0 || 1 / d === 1 / f) || d !== d && f !== f;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, i = e.useLayoutEffect, s = e.useDebugValue;
  function c(d, f) {
    var p = f(), g = o({ inst: { value: p, getSnapshot: f } }), v = g[0].inst, w = g[1];
    return i(
      function() {
        v.value = p, v.getSnapshot = f, l(v) && w({ inst: v });
      },
      [d, p, f]
    ), r(
      function() {
        return l(v) && w({ inst: v }), d(function() {
          l(v) && w({ inst: v });
        });
      },
      [d]
    ), s(p), p;
  }
  function l(d) {
    var f = d.getSnapshot;
    d = d.value;
    try {
      var p = f();
      return !n(d, p);
    } catch {
      return !0;
    }
  }
  function a(d, f) {
    return f();
  }
  var u = typeof window > "u" || typeof window.document > "u" || typeof window.document.createElement > "u" ? a : c;
  return Vn.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : u, Vn;
}
var br;
function Pf() {
  return br || (br = 1, On.exports = $f()), On.exports;
}
var _r;
function Tf() {
  if (_r) return Hn;
  _r = 1;
  var e = Tt, t = Pf();
  function n(a, u) {
    return a === u && (a !== 0 || 1 / a === 1 / u) || a !== a && u !== u;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, i = e.useRef, s = e.useEffect, c = e.useMemo, l = e.useDebugValue;
  return Hn.useSyncExternalStoreWithSelector = function(a, u, d, f, p) {
    var g = i(null);
    if (g.current === null) {
      var v = { hasValue: !1, value: null };
      g.current = v;
    } else v = g.current;
    g = c(
      function() {
        function y(S) {
          if (!_) {
            if (_ = !0, h = S, S = f(S), p !== void 0 && v.hasValue) {
              var I = v.value;
              if (p(I, S))
                return x = I;
            }
            return x = S;
          }
          if (I = x, o(h, S)) return I;
          var A = f(S);
          return p !== void 0 && p(I, A) ? (h = S, I) : (h = S, x = A);
        }
        var _ = !1, h, x, C = d === void 0 ? null : d;
        return [
          function() {
            return y(u());
          },
          C === null ? void 0 : function() {
            return y(C());
          }
        ];
      },
      [u, d, f, p]
    );
    var w = r(a, g[0], g[1]);
    return s(
      function() {
        v.hasValue = !0, v.value = w;
      },
      [w]
    ), l(w), w;
  }, Hn;
}
var Sr;
function zf() {
  return Sr || (Sr = 1, Ln.exports = Tf()), Ln.exports;
}
var Rf = zf();
const jf = /* @__PURE__ */ Qa(Rf), Lf = {}, Er = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (u, d) => {
    const f = typeof u == "function" ? u(t) : u;
    if (!Object.is(f, t)) {
      const p = t;
      t = d ?? (typeof f != "object" || f === null) ? f : Object.assign({}, t, f), n.forEach((g) => g(t, p));
    }
  }, r = () => t, l = { setState: o, getState: r, getInitialState: () => a, subscribe: (u) => (n.add(u), () => n.delete(u)), destroy: () => {
    (Lf ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, a = t = e(o, r, l);
  return l;
}, Hf = (e) => e ? Er(e) : Er, { useDebugValue: Of } = Tt, { useSyncExternalStoreWithSelector: Vf } = jf, Bf = (e) => e;
function as(e, t = Bf, n) {
  const o = Vf(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return Of(o), o;
}
const Nr = (e, t) => {
  const n = Hf(e), o = (r, i = t) => as(n, r, i);
  return Object.assign(o, n), o;
}, Ff = (e, t) => e ? Nr(e, t) : Nr;
function le(e, t) {
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
var Bn = { exports: {} }, ye = {};
var Cr;
function Yf() {
  if (Cr) return ye;
  Cr = 1;
  var e = Tt;
  function t(l) {
    var a = "https://react.dev/errors/" + l;
    if (1 < arguments.length) {
      a += "?args[]=" + encodeURIComponent(arguments[1]);
      for (var u = 2; u < arguments.length; u++)
        a += "&args[]=" + encodeURIComponent(arguments[u]);
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
  function i(l, a, u) {
    var d = 3 < arguments.length && arguments[3] !== void 0 ? arguments[3] : null;
    return {
      $$typeof: r,
      key: d == null ? null : "" + d,
      children: l,
      containerInfo: a,
      implementation: u
    };
  }
  var s = e.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE;
  function c(l, a) {
    if (l === "font") return "";
    if (typeof a == "string")
      return a === "use-credentials" ? a : "";
  }
  return ye.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, ye.createPortal = function(l, a) {
    var u = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!a || a.nodeType !== 1 && a.nodeType !== 9 && a.nodeType !== 11)
      throw Error(t(299));
    return i(l, a, null, u);
  }, ye.flushSync = function(l) {
    var a = s.T, u = o.p;
    try {
      if (s.T = null, o.p = 2, l) return l();
    } finally {
      s.T = a, o.p = u, o.d.f();
    }
  }, ye.preconnect = function(l, a) {
    typeof l == "string" && (a ? (a = a.crossOrigin, a = typeof a == "string" ? a === "use-credentials" ? a : "" : void 0) : a = null, o.d.C(l, a));
  }, ye.prefetchDNS = function(l) {
    typeof l == "string" && o.d.D(l);
  }, ye.preinit = function(l, a) {
    if (typeof l == "string" && a && typeof a.as == "string") {
      var u = a.as, d = c(u, a.crossOrigin), f = typeof a.integrity == "string" ? a.integrity : void 0, p = typeof a.fetchPriority == "string" ? a.fetchPriority : void 0;
      u === "style" ? o.d.S(
        l,
        typeof a.precedence == "string" ? a.precedence : void 0,
        {
          crossOrigin: d,
          integrity: f,
          fetchPriority: p
        }
      ) : u === "script" && o.d.X(l, {
        crossOrigin: d,
        integrity: f,
        fetchPriority: p,
        nonce: typeof a.nonce == "string" ? a.nonce : void 0
      });
    }
  }, ye.preinitModule = function(l, a) {
    if (typeof l == "string")
      if (typeof a == "object" && a !== null) {
        if (a.as == null || a.as === "script") {
          var u = c(
            a.as,
            a.crossOrigin
          );
          o.d.M(l, {
            crossOrigin: u,
            integrity: typeof a.integrity == "string" ? a.integrity : void 0,
            nonce: typeof a.nonce == "string" ? a.nonce : void 0
          });
        }
      } else a == null && o.d.M(l);
  }, ye.preload = function(l, a) {
    if (typeof l == "string" && typeof a == "object" && a !== null && typeof a.as == "string") {
      var u = a.as, d = c(u, a.crossOrigin);
      o.d.L(l, u, {
        crossOrigin: d,
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
  }, ye.preloadModule = function(l, a) {
    if (typeof l == "string")
      if (a) {
        var u = c(a.as, a.crossOrigin);
        o.d.m(l, {
          as: typeof a.as == "string" && a.as !== "script" ? a.as : void 0,
          crossOrigin: u,
          integrity: typeof a.integrity == "string" ? a.integrity : void 0
        });
      } else o.d.m(l);
  }, ye.requestFormReset = function(l) {
    o.d.r(l);
  }, ye.unstable_batchedUpdates = function(l, a) {
    return l(a);
  }, ye.useFormState = function(l, a, u) {
    return s.H.useFormState(l, a, u);
  }, ye.useFormStatus = function() {
    return s.H.useHostTransitionStatus();
  }, ye.version = "19.2.7", ye;
}
var Mr;
function Xf() {
  if (Mr) return Bn.exports;
  Mr = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), Bn.exports = Yf(), Bn.exports;
}
Xf();
const Sn = co(null), qf = Sn.Provider, cs = De.error001("react");
function re(e, t) {
  const n = zt(Sn);
  if (n === null)
    throw new Error(cs);
  return as(n, e, t);
}
function ue() {
  const e = zt(Sn);
  if (e === null)
    throw new Error(cs);
  return ve(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const kr = { display: "none" }, Wf = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, ls = "react-flow__node-desc", us = "react-flow__edge-desc", Zf = "react-flow__aria-live", Uf = (e) => e.ariaLiveMessage, Gf = (e) => e.ariaLabelConfig;
function Kf({ rfId: e }) {
  const t = re(Uf);
  return m.jsx("div", { id: `${Zf}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: Wf, children: t });
}
function Qf({ rfId: e, disableKeyboardA11y: t }) {
  const n = re(Gf);
  return m.jsxs(m.Fragment, { children: [m.jsx("div", { id: `${ls}-${e}`, style: kr, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), m.jsx("div", { id: `${us}-${e}`, style: kr, children: n["edge.a11yDescription.default"] }), !t && m.jsx(Kf, { rfId: e })] });
}
const En = gn(({ position: e = "top-left", children: t, className: n, style: o, ...r }, i) => {
  const s = `${e}`.split("-");
  return m.jsx("div", { className: he(["react-flow__panel", n, ...s]), style: o, ref: i, ...r, children: t });
});
En.displayName = "Panel";
function Jf({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : m.jsx(En, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: m.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const eh = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, Ut = (e) => e.id;
function th(e, t) {
  return le(e.selectedNodes.map(Ut), t.selectedNodes.map(Ut)) && le(e.selectedEdges.map(Ut), t.selectedEdges.map(Ut));
}
function nh({ onSelectionChange: e }) {
  const t = ue(), { selectedNodes: n, selectedEdges: o } = re(eh, th);
  return ie(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((i) => i(r));
  }, [n, o, e]), null;
}
const oh = (e) => !!e.onSelectionChangeHandlers;
function rh({ onSelectionChange: e }) {
  const t = re(oh);
  return e || t ? m.jsx(nh, { onSelectionChange: e }) : null;
}
const ds = [0, 0], ih = { x: 0, y: 0, zoom: 1 }, sh = [
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
], Ir = [...sh, "rfId"], ah = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), Ar = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: It,
  nodeOrigin: ds,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function ch(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: i, setNodeExtent: s, reset: c, setDefaultNodesAndEdges: l } = re(ah, le), a = ue();
  ie(() => (l(e.defaultNodes, e.defaultEdges), () => {
    u.current = Ar, c();
  }), []);
  const u = ne(Ar);
  return ie(
    () => {
      for (const d of Ir) {
        const f = e[d], p = u.current[d];
        f !== p && (typeof e[d] > "u" || (d === "nodes" ? t(f) : d === "edges" ? n(f) : d === "minZoom" ? o(f) : d === "maxZoom" ? r(f) : d === "translateExtent" ? i(f) : d === "nodeExtent" ? s(f) : d === "ariaLabelConfig" ? a.setState({ ariaLabelConfig: Fd(f) }) : d === "fitView" ? a.setState({ fitViewQueued: f }) : d === "fitViewOptions" ? a.setState({ fitViewOptions: f }) : a.setState({ [d]: f })));
      }
      u.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    Ir.map((d) => e[d])
  ), null;
}
function Dr() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function lh(e) {
  const [t, n] = ce(e === "system" ? null : e);
  return ie(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = Dr(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : Dr()?.matches ? "dark" : "light";
}
const $r = typeof document < "u" ? document : null;
function Pt(e = null, t = { target: $r, actInsideInputWithModifier: !0 }) {
  const [n, o] = ce(!1), r = ne(!1), i = ne(/* @__PURE__ */ new Set([])), [s, c] = ve(() => {
    if (e !== null) {
      const a = (Array.isArray(e) ? e : [e]).filter((d) => typeof d == "string").map((d) => d.replace("+", `
`).replace(`

`, `
+`).split(`
`)), u = a.reduce((d, f) => d.concat(...f), []);
      return [a, u];
    }
    return [[], []];
  }, [e]);
  return ie(() => {
    const l = t?.target ?? $r, a = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const u = (p) => {
        if (r.current = p.ctrlKey || p.metaKey || p.shiftKey || p.altKey, (!r.current || r.current && !a) && Yi(p))
          return !1;
        const v = Tr(p.code, c);
        if (i.current.add(p[v]), Pr(s, i.current, !1)) {
          const w = p.composedPath?.()?.[0] || p.target, y = w?.nodeName === "BUTTON" || w?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !y) && p.preventDefault(), o(!0);
        }
      }, d = (p) => {
        const g = Tr(p.code, c);
        Pr(s, i.current, !0) ? (o(!1), i.current.clear()) : i.current.delete(p[g]), p.key === "Meta" && i.current.clear(), r.current = !1;
      }, f = () => {
        i.current.clear(), o(!1);
      };
      return l?.addEventListener("keydown", u), l?.addEventListener("keyup", d), window.addEventListener("blur", f), window.addEventListener("contextmenu", f), () => {
        l?.removeEventListener("keydown", u), l?.removeEventListener("keyup", d), window.removeEventListener("blur", f), window.removeEventListener("contextmenu", f);
      };
    }
  }, [e, o]), n;
}
function Pr(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function Tr(e, t) {
  return t.includes(e) ? "code" : "key";
}
const uh = () => {
  const e = ue();
  return ve(() => ({
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
      const { width: o, height: r, minZoom: i, maxZoom: s, panZoom: c } = e.getState(), l = vo(t, o, r, i, s, n?.padding ?? 0.1);
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
      }, u = n.snapGrid ?? r, d = n.snapToGrid ?? i;
      return mt(a, o, d, u);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: r, y: i } = o.getBoundingClientRect(), s = ht(t, n);
      return {
        x: s.x + r,
        y: s.y + i
      };
    }
  }), []);
};
function fs(e, t) {
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
      dh(l, c);
    n.push(c);
  }
  return r.length && r.forEach((i) => {
    i.index !== void 0 ? n.splice(i.index, 0, { ...i.item }) : n.push({ ...i.item });
  }), n;
}
function dh(e, t) {
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
function hs(e, t) {
  return fs(e, t);
}
function gs(e, t) {
  return fs(e, t);
}
function We(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function it(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [r, i] of e) {
    const s = t.has(r);
    !(i.selected === void 0 && !s) && i.selected !== s && (n && (i.selected = s), o.push(We(i.id, s)));
  }
  return o;
}
function zr({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((r) => [r.id, r]));
  for (const [r, i] of e.entries()) {
    const s = t.get(i.id), c = s?.internals?.userNode ?? s;
    c !== void 0 && c !== i && n.push({ id: i.id, item: i, type: "replace" }), c === void 0 && n.push({ item: i, type: "add", index: r });
  }
  for (const [r] of t)
    o.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function Rr(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const fh = Oi();
function ps(e, t, n = {}) {
  return Ud(e, t, {
    ...n,
    onError: n.onError ?? fh
  });
}
const jr = (e) => Td(e), hh = (e) => Ri(e);
function ms(e) {
  return gn(e);
}
const gh = typeof window < "u" ? Ka : ie;
function Lr(e) {
  const [t, n] = ce(BigInt(0)), [o] = ce(() => ph(() => n((r) => r + BigInt(1))));
  return gh(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function ph(e) {
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
const ys = co(null);
function mh({ children: e }) {
  const t = ue(), n = xe((c) => {
    const { nodes: l = [], setNodes: a, hasDefaultNodes: u, onNodesChange: d, nodeLookup: f, fitViewQueued: p, onNodesChangeMiddlewareMap: g } = t.getState();
    let v = l;
    for (const y of c)
      v = typeof y == "function" ? y(v) : y;
    let w = zr({
      items: v,
      lookup: f
    });
    for (const y of g.values())
      w = y(w);
    u && a(v), w.length > 0 ? d?.(w) : p && window.requestAnimationFrame(() => {
      const { fitViewQueued: y, nodes: _, setNodes: h } = t.getState();
      y && h(_);
    });
  }, []), o = Lr(n), r = xe((c) => {
    const { edges: l = [], setEdges: a, hasDefaultEdges: u, onEdgesChange: d, edgeLookup: f } = t.getState();
    let p = l;
    for (const g of c)
      p = typeof g == "function" ? g(p) : g;
    u ? a(p) : d && d(zr({
      items: p,
      lookup: f
    }));
  }, []), i = Lr(r), s = ve(() => ({ nodeQueue: o, edgeQueue: i }), []);
  return m.jsx(ys.Provider, { value: s, children: e });
}
function yh() {
  const e = zt(ys);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const xh = (e) => !!e.panZoom;
function Mo() {
  const e = uh(), t = ue(), n = yh(), o = re(xh), r = ve(() => {
    const i = (d) => t.getState().nodeLookup.get(d), s = (d) => {
      n.nodeQueue.push(d);
    }, c = (d) => {
      n.edgeQueue.push(d);
    }, l = (d) => {
      const { nodeLookup: f, nodeOrigin: p } = t.getState(), g = jr(d) ? d : f.get(d.id), v = g.parentId ? Bi(g.position, g.measured, g.parentId, f, p) : g.position, w = {
        ...g,
        position: v,
        width: g.measured?.width ?? g.width,
        height: g.measured?.height ?? g.height
      };
      return ft(w);
    }, a = (d, f, p = { replace: !1 }) => {
      s((g) => g.map((v) => {
        if (v.id === d) {
          const w = typeof f == "function" ? f(v) : f;
          return p.replace && jr(w) ? w : { ...v, ...w };
        }
        return v;
      }));
    }, u = (d, f, p = { replace: !1 }) => {
      c((g) => g.map((v) => {
        if (v.id === d) {
          const w = typeof f == "function" ? f(v) : f;
          return p.replace && hh(w) ? w : { ...v, ...w };
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
        return d.map((f) => ({ ...f }));
      },
      getEdge: (d) => t.getState().edgeLookup.get(d),
      setNodes: s,
      setEdges: c,
      addNodes: (d) => {
        const f = Array.isArray(d) ? d : [d];
        n.nodeQueue.push((p) => [...p, ...f]);
      },
      addEdges: (d) => {
        const f = Array.isArray(d) ? d : [d];
        n.edgeQueue.push((p) => [...p, ...f]);
      },
      toObject: () => {
        const { nodes: d = [], edges: f = [], transform: p } = t.getState(), [g, v, w] = p;
        return {
          nodes: d.map((y) => ({ ...y })),
          edges: f.map((y) => ({ ...y })),
          viewport: {
            x: g,
            y: v,
            zoom: w
          }
        };
      },
      deleteElements: async ({ nodes: d = [], edges: f = [] }) => {
        const { nodes: p, edges: g, onNodesDelete: v, onEdgesDelete: w, triggerNodeChanges: y, triggerEdgeChanges: _, onDelete: h, onBeforeDelete: x } = t.getState(), { nodes: C, edges: S } = await Hd({
          nodesToRemove: d,
          edgesToRemove: f,
          nodes: p,
          edges: g,
          onBeforeDelete: x
        }), I = S.length > 0, A = C.length > 0;
        if (I) {
          const $ = S.map(Rr);
          w?.(S), _($);
        }
        if (A) {
          const $ = C.map(Rr);
          v?.(C), y($);
        }
        return (A || I) && h?.({ nodes: C, edges: S }), { deletedNodes: C, deletedEdges: S };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (d, f = !0, p) => {
        const g = ar(d), v = g ? d : l(d), w = p !== void 0;
        return v ? (p || t.getState().nodes).filter((y) => {
          const _ = t.getState().nodeLookup.get(y.id);
          if (_ && !g && (y.id === d.id || !_.internals.positionAbsolute))
            return !1;
          const h = ft(w ? y : _), x = Dt(h, v);
          return f && x > 0 || x >= h.width * h.height || x >= v.width * v.height;
        }) : [];
      },
      isNodeIntersecting: (d, f, p = !0) => {
        const v = ar(d) ? d : l(d);
        if (!v)
          return !1;
        const w = Dt(v, f);
        return p && w > 0 || w >= f.width * f.height || w >= v.width * v.height;
      },
      updateNode: a,
      updateNodeData: (d, f, p = { replace: !1 }) => {
        a(d, (g) => {
          const v = typeof f == "function" ? f(g) : f;
          return p.replace ? { ...g, data: v } : { ...g, data: { ...g.data, ...v } };
        }, p);
      },
      updateEdge: u,
      updateEdgeData: (d, f, p = { replace: !1 }) => {
        u(d, (g) => {
          const v = typeof f == "function" ? f(g) : f;
          return p.replace ? { ...g, data: v } : { ...g, data: { ...g.data, ...v } };
        }, p);
      },
      getNodesBounds: (d) => {
        const { nodeLookup: f, nodeOrigin: p } = t.getState();
        return zd(d, { nodeLookup: f, nodeOrigin: p });
      },
      getHandleConnections: ({ type: d, id: f, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}-${d}${f ? `-${f}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: d, handleId: f, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}${d ? f ? `-${d}-${f}` : `-${d}` : ""}`)?.values() ?? []),
      fitView: async (d) => {
        const f = t.getState().fitViewResolver ?? Bd();
        return t.setState({ fitViewQueued: !0, fitViewOptions: d, fitViewResolver: f }), n.nodeQueue.push((p) => [...p]), f.promise;
      }
    };
  }, []);
  return ve(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const Hr = (e) => e.selected, wh = typeof window < "u" ? window : void 0;
function vh({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = ue(), { deleteElements: o } = Mo(), r = Pt(e, { actInsideInputWithModifier: !1 }), i = Pt(t, { target: wh });
  ie(() => {
    if (r) {
      const { edges: s, nodes: c } = n.getState();
      o({ nodes: c.filter(Hr), edges: s.filter(Hr) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), ie(() => {
    n.setState({ multiSelectionActive: i });
  }, [i]);
}
function bh(e) {
  const t = ue();
  ie(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = bo(e.current);
      (o.height === 0 || o.width === 0) && t.getState().onError?.("004", De.error004()), t.setState({ width: o.width || 500, height: o.height || 500 });
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
const Nn = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, _h = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function Sh({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: i = Ge.Free, zoomOnDoubleClick: s = !0, panOnDrag: c = !0, defaultViewport: l, translateExtent: a, minZoom: u, maxZoom: d, zoomActivationKeyCode: f, preventScrolling: p = !0, children: g, noWheelClassName: v, noPanClassName: w, onViewportChange: y, isControlledViewport: _, paneClickDistance: h, selectionOnDrag: x }) {
  const C = ue(), S = ne(null), { userSelectionActive: I, lib: A, connectionInProgress: $ } = re(_h, le), V = Pt(f), k = ne();
  bh(S);
  const T = xe((j) => {
    y?.({ x: j[0], y: j[1], zoom: j[2] }), _ || C.setState({ transform: j });
  }, [y, _]);
  return ie(() => {
    if (S.current) {
      k.current = Cf({
        domNode: S.current,
        minZoom: u,
        maxZoom: d,
        translateExtent: a,
        viewport: l,
        onDraggingChange: (E) => C.setState((M) => M.paneDragging === E ? M : { paneDragging: E }),
        onPanZoomStart: (E, M) => {
          const { onViewportChangeStart: P, onMoveStart: D } = C.getState();
          D?.(E, M), P?.(M);
        },
        onPanZoom: (E, M) => {
          const { onViewportChange: P, onMove: D } = C.getState();
          D?.(E, M), P?.(M);
        },
        onPanZoomEnd: (E, M) => {
          const { onViewportChangeEnd: P, onMoveEnd: D } = C.getState();
          D?.(E, M), P?.(M);
        }
      });
      const { x: j, y: b, zoom: N } = k.current.getViewport();
      return C.setState({
        panZoom: k.current,
        transform: [j, b, N],
        domNode: S.current.closest(".react-flow")
      }), () => {
        k.current?.destroy();
      };
    }
  }, []), ie(() => {
    k.current?.update({
      onPaneContextMenu: e,
      zoomOnScroll: t,
      zoomOnPinch: n,
      panOnScroll: o,
      panOnScrollSpeed: r,
      panOnScrollMode: i,
      zoomOnDoubleClick: s,
      panOnDrag: c,
      zoomActivationKeyPressed: V,
      preventScrolling: p,
      noPanClassName: w,
      userSelectionActive: I,
      noWheelClassName: v,
      lib: A,
      onTransformChange: T,
      connectionInProgress: $,
      selectionOnDrag: x,
      paneClickDistance: h
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
    V,
    p,
    w,
    I,
    v,
    A,
    T,
    $,
    x,
    h
  ]), m.jsx("div", { className: "react-flow__renderer", ref: S, style: Nn, children: g });
}
const Eh = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function Nh() {
  const { userSelectionActive: e, userSelectionRect: t } = re(Eh, le);
  return e && t ? m.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const Fn = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, Ch = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function Mh({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = At.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: i, selectionOnDrag: s, onSelectionStart: c, onSelectionEnd: l, onPaneClick: a, onPaneContextMenu: u, onPaneScroll: d, onPaneMouseEnter: f, onPaneMouseMove: p, onPaneMouseLeave: g, children: v }) {
  const w = ne(0), y = ue(), { userSelectionActive: _, elementsSelectable: h, dragging: x, connectionInProgress: C, panBy: S, autoPanSpeed: I } = re(Ch, le), A = h && (e || _), $ = ne(null), V = ne(), k = ne(/* @__PURE__ */ new Set()), T = ne(/* @__PURE__ */ new Set()), j = ne(!1), b = ne({ x: 0, y: 0 }), N = ne(!1), E = (R) => {
    if (j.current || C) {
      j.current = !1;
      return;
    }
    a?.(R), y.getState().resetSelectedElements(), y.setState({ nodesSelectionActive: !1 });
  }, M = (R) => {
    if (Array.isArray(o) && o?.includes(2)) {
      R.preventDefault();
      return;
    }
    u?.(R);
  }, P = d ? (R) => d(R) : void 0, D = (R) => {
    j.current && (R.stopPropagation(), j.current = !1);
  }, O = (R) => {
    const { domNode: F, transform: J } = y.getState();
    if (V.current = F?.getBoundingClientRect(), !V.current)
      return;
    const K = R.target === $.current;
    if (!K && !!R.target.closest(".nokey") || !e || !(s && K || t) || R.button !== 0 || !R.isPrimary)
      return;
    R.target?.setPointerCapture?.(R.pointerId), j.current = !1;
    const { x: te, y: se } = Ae(R.nativeEvent, V.current), de = mt({ x: te, y: se }, J);
    y.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: de.x,
        startY: de.y,
        x: te,
        y: se
      }
    }), K || (R.stopPropagation(), R.preventDefault());
  };
  function L(R, F) {
    const { userSelectionRect: J } = y.getState();
    if (!J)
      return;
    const { transform: K, nodeLookup: q, edgeLookup: G, connectionLookup: te, triggerNodeChanges: se, triggerEdgeChanges: de, defaultEdgeOptions: me } = y.getState(), Ce = { x: J.startX, y: J.startY }, { x: Se, y: Ee } = ht(Ce, K), z = {
      startX: Ce.x,
      startY: Ce.y,
      x: R < Se ? R : Se,
      y: F < Ee ? F : Ee,
      width: Math.abs(R - Se),
      height: Math.abs(F - Ee)
    }, B = k.current, Q = T.current;
    k.current = new Set(xo(q, z, K, n === At.Partial, !0).map((ae) => ae.id)), T.current = /* @__PURE__ */ new Set();
    const oe = me?.selectable ?? !0;
    for (const ae of k.current) {
      const ge = te.get(ae);
      if (ge)
        for (const { edgeId: pe } of ge.values()) {
          const je = G.get(pe);
          je && (je.selectable ?? oe) && T.current.add(pe);
        }
    }
    if (!cr(B, k.current)) {
      const ae = it(q, k.current, !0);
      se(ae);
    }
    if (!cr(Q, T.current)) {
      const ae = it(G, T.current);
      de(ae);
    }
    y.setState({
      userSelectionRect: z,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function H() {
    if (!r || !V.current)
      return;
    const [R, F] = wo(b.current, V.current, I);
    S({ x: R, y: F }).then((J) => {
      if (!j.current || !J) {
        w.current = requestAnimationFrame(H);
        return;
      }
      const { x: K, y: q } = b.current;
      L(K, q), w.current = requestAnimationFrame(H);
    });
  }
  const X = () => {
    cancelAnimationFrame(w.current), w.current = 0, N.current = !1;
  };
  ie(() => () => X(), []);
  const Y = (R) => {
    const { userSelectionRect: F, transform: J, resetSelectedElements: K } = y.getState();
    if (!V.current || !F)
      return;
    const { x: q, y: G } = Ae(R.nativeEvent, V.current);
    b.current = { x: q, y: G };
    const te = ht({ x: F.startX, y: F.startY }, J);
    if (!j.current) {
      const se = t ? 0 : i;
      if (Math.hypot(q - te.x, G - te.y) <= se)
        return;
      K(), c?.(R);
    }
    j.current = !0, N.current || (H(), N.current = !0), L(q, G);
  }, Z = (R) => {
    R.button === 0 && (R.target?.releasePointerCapture?.(R.pointerId), !_ && R.target === $.current && y.getState().userSelectionRect && E?.(R), y.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), j.current && (l?.(R), y.setState({
      nodesSelectionActive: k.current.size > 0
    })), X());
  }, ee = (R) => {
    R.target?.releasePointerCapture?.(R.pointerId), X();
  }, U = o === !0 || Array.isArray(o) && o.includes(0);
  return m.jsxs("div", { className: he(["react-flow__pane", { draggable: U, dragging: x, selection: e }]), onClick: A ? void 0 : Fn(E, $), onContextMenu: Fn(M, $), onWheel: Fn(P, $), onPointerEnter: A ? void 0 : f, onPointerMove: A ? Y : p, onPointerUp: A ? Z : void 0, onPointerCancel: A ? ee : void 0, onPointerDownCapture: A ? O : void 0, onClickCapture: A ? D : void 0, onPointerLeave: g, ref: $, style: Nn, children: [v, m.jsx(Nh, {})] });
}
function so({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: i, multiSelectionActive: s, nodeLookup: c, onError: l } = t.getState(), a = c.get(e);
  if (!a) {
    l?.("012", De.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), a.selected ? (n || a.selected && s) && (i({ nodes: [a], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function xs({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: i, nodeClickDistance: s }) {
  const c = ue(), [l, a] = ce(!1), u = ne();
  return ie(() => {
    u.current = ff({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (d) => {
        so({
          id: d,
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
  }, []), ie(() => {
    if (!(t || !e.current || !u.current))
      return u.current.update({
        noDragClassName: n,
        handleSelector: o,
        domNode: e.current,
        isSelectable: i,
        nodeId: r,
        nodeClickDistance: s
      }), () => {
        u.current?.destroy();
      };
  }, [n, o, t, i, e, r, s]), l;
}
const kh = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function ws() {
  const e = ue();
  return xe((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: i, nodesDraggable: s, onError: c, updateNodePositions: l, nodeLookup: a, nodeOrigin: u } = e.getState(), d = /* @__PURE__ */ new Map(), f = kh(s), p = r ? i[0] : 5, g = r ? i[1] : 5, v = n.direction.x * p * n.factor, w = n.direction.y * g * n.factor;
    for (const [, y] of a) {
      if (!f(y))
        continue;
      let _ = {
        x: y.internals.positionAbsolute.x + v,
        y: y.internals.positionAbsolute.y + w
      };
      r && (_ = Ot(_, i));
      const { position: h, positionAbsolute: x } = ji({
        nodeId: y.id,
        nextPosition: _,
        nodeLookup: a,
        nodeExtent: o,
        nodeOrigin: u,
        onError: c
      });
      y.position = h, y.internals.positionAbsolute = x, d.set(y.id, y);
    }
    l(d);
  }, []);
}
const ko = co(null), Ih = ko.Provider;
ko.Consumer;
const vs = () => zt(ko), Ah = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), Dh = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: i, connection: s } = o, { fromHandle: c, toHandle: l, isValid: a } = s, u = l?.nodeId === e && l?.id === t && l?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: u,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: i === ut.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!r,
    valid: u && a
  };
};
function $h({ type: e = "source", position: t = W.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: i = !0, id: s, onConnect: c, children: l, className: a, onMouseDown: u, onTouchStart: d, ...f }, p) {
  const g = s || null, v = e === "target", w = ue(), y = vs(), { connectOnClick: _, noPanClassName: h, rfId: x } = re(Ah, le), { connectingFrom: C, connectingTo: S, clickConnecting: I, isPossibleEndHandle: A, connectionInProcess: $, clickConnectionInProcess: V, valid: k } = re(Dh(y, g, e), le);
  y || w.getState().onError?.("010", De.error010());
  const T = (N) => {
    const { defaultEdgeOptions: E, onConnect: M, hasDefaultEdges: P } = w.getState(), D = {
      ...E,
      ...N
    };
    if (P) {
      const { edges: O, setEdges: L, onError: H } = w.getState();
      L(ps(D, O, { onError: H }));
    }
    M?.(D), c?.(D);
  }, j = (N) => {
    if (!y)
      return;
    const E = Xi(N.nativeEvent);
    if (r && (E && N.button === 0 || !E)) {
      const M = w.getState();
      io.onPointerDown(N.nativeEvent, {
        handleDomNode: N.currentTarget,
        autoPanOnConnect: M.autoPanOnConnect,
        connectionMode: M.connectionMode,
        connectionRadius: M.connectionRadius,
        domNode: M.domNode,
        nodeLookup: M.nodeLookup,
        lib: M.lib,
        isTarget: v,
        handleId: g,
        nodeId: y,
        flowId: M.rfId,
        panBy: M.panBy,
        cancelConnection: M.cancelConnection,
        onConnectStart: M.onConnectStart,
        onConnectEnd: (...P) => w.getState().onConnectEnd?.(...P),
        updateConnection: M.updateConnection,
        onConnect: T,
        isValidConnection: n || ((...P) => w.getState().isValidConnection?.(...P) ?? !0),
        getTransform: () => w.getState().transform,
        getFromHandle: () => w.getState().connection.fromHandle,
        autoPanSpeed: M.autoPanSpeed,
        dragThreshold: M.connectionDragThreshold
      });
    }
    E ? u?.(N) : d?.(N);
  }, b = (N) => {
    const { onClickConnectStart: E, onClickConnectEnd: M, connectionClickStartHandle: P, connectionMode: D, isValidConnection: O, lib: L, rfId: H, nodeLookup: X, connection: Y } = w.getState();
    if (!y || !P && !r)
      return;
    if (!P) {
      E?.(N.nativeEvent, { nodeId: y, handleId: g, handleType: e }), w.setState({ connectionClickStartHandle: { nodeId: y, type: e, id: g } });
      return;
    }
    const Z = Fi(N.target), ee = n || O, { connection: U, isValid: R } = io.isValid(N.nativeEvent, {
      handle: {
        nodeId: y,
        id: g,
        type: e
      },
      connectionMode: D,
      fromNodeId: P.nodeId,
      fromHandleId: P.id || null,
      fromType: P.type,
      isValidConnection: ee,
      flowId: H,
      doc: Z,
      lib: L,
      nodeLookup: X
    });
    R && U && T(U);
    const F = structuredClone(Y);
    delete F.inProgress, F.toPosition = F.toHandle ? F.toHandle.position : null, M?.(N, F), w.setState({ connectionClickStartHandle: null });
  };
  return m.jsx("div", { "data-handleid": g, "data-nodeid": y, "data-handlepos": t, "data-id": `${x}-${y}-${g}-${e}`, className: he([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    h,
    a,
    {
      source: !v,
      target: v,
      connectable: o,
      connectablestart: r,
      connectableend: i,
      clickconnecting: I,
      connectingfrom: C,
      connectingto: S,
      valid: k,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!$ || A) && ($ || V ? i : r)
    }
  ]), onMouseDown: j, onTouchStart: j, onClick: _ ? b : void 0, ref: p, ...f, children: l });
}
const pt = fe(ms($h));
function Ph({ data: e, isConnectable: t, sourcePosition: n = W.Bottom }) {
  return m.jsxs(m.Fragment, { children: [e?.label, m.jsx(pt, { type: "source", position: n, isConnectable: t })] });
}
function Th({ data: e, isConnectable: t, targetPosition: n = W.Top, sourcePosition: o = W.Bottom }) {
  return m.jsxs(m.Fragment, { children: [m.jsx(pt, { type: "target", position: n, isConnectable: t }), e?.label, m.jsx(pt, { type: "source", position: o, isConnectable: t })] });
}
function zh() {
  return null;
}
function Rh({ data: e, isConnectable: t, targetPosition: n = W.Top }) {
  return m.jsxs(m.Fragment, { children: [m.jsx(pt, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const fn = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, Or = {
  input: Ph,
  default: Th,
  output: Rh,
  group: zh
};
function jh(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const Lh = (e) => {
  const { width: t, height: n, x: o, y: r } = Ht(e.nodeLookup, {
    filter: (i) => !!i.selected
  });
  return {
    width: Ie(t) ? t : null,
    height: Ie(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function Hh({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = ue(), { width: r, height: i, transformString: s, userSelectionActive: c } = re(Lh, le), l = ws(), a = ne(null);
  ie(() => {
    n || a.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const u = !c && r !== null && i !== null;
  if (xs({
    nodeRef: a,
    disabled: !u
  }), !u)
    return null;
  const d = e ? (p) => {
    const g = o.getState().nodes.filter((v) => v.selected);
    e(p, g);
  } : void 0, f = (p) => {
    Object.prototype.hasOwnProperty.call(fn, p.key) && (p.preventDefault(), l({
      direction: fn[p.key],
      factor: p.shiftKey ? 4 : 1
    }));
  };
  return m.jsx("div", { className: he(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: s
  }, children: m.jsx("div", { ref: a, className: "react-flow__nodesselection-rect", onContextMenu: d, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : f, style: {
    width: r,
    height: i
  } }) });
}
const Vr = typeof window < "u" ? window : void 0, Oh = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function bs({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, paneClickDistance: c, deleteKeyCode: l, selectionKeyCode: a, selectionOnDrag: u, selectionMode: d, onSelectionStart: f, onSelectionEnd: p, multiSelectionKeyCode: g, panActivationKeyCode: v, zoomActivationKeyCode: w, elementsSelectable: y, zoomOnScroll: _, zoomOnPinch: h, panOnScroll: x, panOnScrollSpeed: C, panOnScrollMode: S, zoomOnDoubleClick: I, panOnDrag: A, autoPanOnSelection: $, defaultViewport: V, translateExtent: k, minZoom: T, maxZoom: j, preventScrolling: b, onSelectionContextMenu: N, noWheelClassName: E, noPanClassName: M, disableKeyboardA11y: P, onViewportChange: D, isControlledViewport: O }) {
  const { nodesSelectionActive: L, userSelectionActive: H } = re(Oh, le), X = Pt(a, { target: Vr }), Y = Pt(v, { target: Vr }), Z = Y || A, ee = Y || x, U = u && Z !== !0, R = X || H || U;
  return vh({ deleteKeyCode: l, multiSelectionKeyCode: g }), m.jsx(Sh, { onPaneContextMenu: i, elementsSelectable: y, zoomOnScroll: _, zoomOnPinch: h, panOnScroll: ee, panOnScrollSpeed: C, panOnScrollMode: S, zoomOnDoubleClick: I, panOnDrag: !X && Z, defaultViewport: V, translateExtent: k, minZoom: T, maxZoom: j, zoomActivationKeyCode: w, preventScrolling: b, noWheelClassName: E, noPanClassName: M, onViewportChange: D, isControlledViewport: O, paneClickDistance: c, selectionOnDrag: U, children: m.jsxs(Mh, { onSelectionStart: f, onSelectionEnd: p, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, panOnDrag: Z, autoPanOnSelection: $, isSelecting: !!R, selectionMode: d, selectionKeyPressed: X, paneClickDistance: c, selectionOnDrag: U, children: [e, L && m.jsx(Hh, { onSelectionContextMenu: N, noPanClassName: M, disableKeyboardA11y: P })] }) });
}
bs.displayName = "FlowRenderer";
const Vh = fe(bs), Bh = (e) => (t) => e ? xo(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function Fh(e) {
  return re(xe(Bh(e), [e]), le);
}
const Yh = (e) => e.updateNodeInternals;
function Xh() {
  const e = re(Yh), [t] = ce(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
function qh({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = ue(), i = ne(null), s = ne(null), c = ne(e.sourcePosition), l = ne(e.targetPosition), a = ne(t), u = n && !!e.internals.handleBounds;
  return ie(() => {
    i.current && !e.hidden && (!u || s.current !== i.current) && (s.current && o?.unobserve(s.current), o?.observe(i.current), s.current = i.current);
  }, [u, e.hidden]), ie(() => () => {
    s.current && (o?.unobserve(s.current), s.current = null);
  }, []), ie(() => {
    if (i.current) {
      const d = a.current !== t, f = c.current !== e.sourcePosition, p = l.current !== e.targetPosition;
      (d || f || p) && (a.current = t, c.current = e.sourcePosition, l.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: i.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), i;
}
function Wh({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: i, onDoubleClick: s, nodesDraggable: c, elementsSelectable: l, nodesConnectable: a, nodesFocusable: u, resizeObserver: d, noDragClassName: f, noPanClassName: p, disableKeyboardA11y: g, rfId: v, nodeTypes: w, nodeClickDistance: y, onError: _ }) {
  const { node: h, internals: x, isParent: C } = re((R) => {
    const F = R.nodeLookup.get(e), J = R.parentLookup.has(e);
    return {
      node: F,
      internals: F.internals,
      isParent: J
    };
  }, le);
  let S = h.type || "default", I = w?.[S] || Or[S];
  I === void 0 && (_?.("003", De.error003(S)), S = "default", I = w?.default || Or.default);
  const A = !!(h.draggable || c && typeof h.draggable > "u"), $ = !!(h.selectable || l && typeof h.selectable > "u"), V = !!(h.connectable || a && typeof h.connectable > "u"), k = !!(h.focusable || u && typeof h.focusable > "u"), T = ue(), j = Vi(h), b = qh({ node: h, nodeType: S, hasDimensions: j, resizeObserver: d }), N = xs({
    nodeRef: b,
    disabled: h.hidden || !A,
    noDragClassName: f,
    handleSelector: h.dragHandle,
    nodeId: e,
    isSelectable: $,
    nodeClickDistance: y
  }), E = ws();
  if (h.hidden)
    return null;
  const M = Ve(h), P = jh(h), D = $ || A || t || n || o || r, O = n ? (R) => n(R, { ...x.userNode }) : void 0, L = o ? (R) => o(R, { ...x.userNode }) : void 0, H = r ? (R) => r(R, { ...x.userNode }) : void 0, X = i ? (R) => i(R, { ...x.userNode }) : void 0, Y = s ? (R) => s(R, { ...x.userNode }) : void 0, Z = (R) => {
    const { selectNodesOnDrag: F, nodeDragThreshold: J } = T.getState();
    $ && (!F || !A || J > 0) && so({
      id: e,
      store: T,
      nodeRef: b
    }), t && t(R, { ...x.userNode });
  }, ee = (R) => {
    if (!(Yi(R.nativeEvent) || g)) {
      if ($i.includes(R.key) && $) {
        const F = R.key === "Escape";
        so({
          id: e,
          store: T,
          unselect: F,
          nodeRef: b
        });
      } else if (A && h.selected && Object.prototype.hasOwnProperty.call(fn, R.key)) {
        R.preventDefault();
        const { ariaLabelConfig: F } = T.getState();
        T.setState({
          ariaLiveMessage: F["node.a11yDescription.ariaLiveMessage"]({
            direction: R.key.replace("Arrow", "").toLowerCase(),
            x: ~~x.positionAbsolute.x,
            y: ~~x.positionAbsolute.y
          })
        }), E({
          direction: fn[R.key],
          factor: R.shiftKey ? 4 : 1
        });
      }
    }
  }, U = () => {
    if (g || !b.current?.matches(":focus-visible"))
      return;
    const { transform: R, width: F, height: J, autoPanOnNodeFocus: K, setCenter: q } = T.getState();
    if (!K)
      return;
    xo(/* @__PURE__ */ new Map([[e, h]]), { x: 0, y: 0, width: F, height: J }, R, !0).length > 0 || q(h.position.x + M.width / 2, h.position.y + M.height / 2, {
      zoom: R[2]
    });
  };
  return m.jsx("div", { className: he([
    "react-flow__node",
    `react-flow__node-${S}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [p]: A
    },
    h.className,
    {
      selected: h.selected,
      selectable: $,
      parent: C,
      draggable: A,
      dragging: N
    }
  ]), ref: b, style: {
    zIndex: x.z,
    transform: `translate(${x.positionAbsolute.x}px,${x.positionAbsolute.y}px)`,
    pointerEvents: D ? "all" : "none",
    visibility: j ? "visible" : "hidden",
    ...h.style,
    ...P
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: O, onMouseMove: L, onMouseLeave: H, onContextMenu: X, onClick: Z, onDoubleClick: Y, onKeyDown: k ? ee : void 0, tabIndex: k ? 0 : void 0, onFocus: k ? U : void 0, role: h.ariaRole ?? (k ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": g ? void 0 : `${ls}-${v}`, "aria-label": h.ariaLabel, ...h.domAttributes, children: m.jsx(Ih, { value: e, children: m.jsx(I, { id: e, data: h.data, type: S, positionAbsoluteX: x.positionAbsolute.x, positionAbsoluteY: x.positionAbsolute.y, selected: h.selected ?? !1, selectable: $, draggable: A, deletable: h.deletable ?? !0, isConnectable: V, sourcePosition: h.sourcePosition, targetPosition: h.targetPosition, dragging: N, dragHandle: h.dragHandle, zIndex: x.z, parentId: h.parentId, ...M }) }) });
}
var Zh = fe(Wh);
const Uh = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function _s(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: i } = re(Uh, le), s = Fh(e.onlyRenderVisibleElements), c = Xh();
  return m.jsx("div", { className: "react-flow__nodes", style: Nn, children: s.map((l) => (
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
    m.jsx(Zh, { id: l, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: i }, l)
  )) });
}
_s.displayName = "NodeRenderer";
const Gh = fe(_s);
function Kh(e) {
  return re(xe((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const i = n.nodeLookup.get(r.source), s = n.nodeLookup.get(r.target);
        i && s && qd({
          sourceNode: i,
          targetNode: s,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && o.push(r.id);
      }
    return o;
  }, [e]), le);
}
const Qh = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return m.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, Jh = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return m.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Br = {
  [un.Arrow]: Qh,
  [un.ArrowClosed]: Jh
};
function eg(e) {
  const t = ue();
  return ve(() => Object.prototype.hasOwnProperty.call(Br, e) ? Br[e] : (t.getState().onError?.("009", De.error009(e)), null), [e]);
}
const tg = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: i = "strokeWidth", strokeWidth: s, orient: c = "auto-start-reverse" }) => {
  const l = eg(t);
  return l ? m.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: i, orient: c, refX: "0", refY: "0", children: m.jsx(l, { color: n, strokeWidth: s }) }) : null;
}, Ss = ({ defaultColor: e, rfId: t }) => {
  const n = re((i) => i.edges), o = re((i) => i.defaultEdgeOptions), r = ve(() => ef(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? m.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: m.jsx("defs", { children: r.map((i) => m.jsx(tg, { id: i.id, type: i.type, color: i.color, width: i.width, height: i.height, markerUnits: i.markerUnits, strokeWidth: i.strokeWidth, orient: i.orient }, i.id)) }) }) : null;
};
Ss.displayName = "MarkerDefinitions";
var ng = fe(Ss);
function Es({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: i, labelBgPadding: s = [2, 4], labelBgBorderRadius: c = 2, children: l, className: a, ...u }) {
  const [d, f] = ce({ x: 1, y: 0, width: 0, height: 0 }), p = he(["react-flow__edge-textwrapper", a]), g = ne(null);
  return ie(() => {
    if (g.current) {
      const v = g.current.getBBox();
      f({
        x: v.x,
        y: v.y,
        width: v.width,
        height: v.height
      });
    }
  }, [n]), n ? m.jsxs("g", { transform: `translate(${e - d.width / 2} ${t - d.height / 2})`, className: p, visibility: d.width ? "visible" : "hidden", ...u, children: [r && m.jsx("rect", { width: d.width + 2 * s[0], x: -s[0], y: -s[1], height: d.height + 2 * s[1], className: "react-flow__edge-textbg", style: i, rx: c, ry: c }), m.jsx("text", { className: "react-flow__edge-text", y: d.height / 2, dy: "0.3em", ref: g, style: o, children: n }), l] }) : null;
}
Es.displayName = "EdgeText";
const og = fe(Es);
function Cn({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: c, labelBgBorderRadius: l, interactionWidth: a = 20, ...u }) {
  return m.jsxs(m.Fragment, { children: [m.jsx("path", { ...u, d: e, fill: "none", className: he(["react-flow__edge-path", u.className]) }), a ? m.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: a, className: "react-flow__edge-interaction" }) : null, o && Ie(t) && Ie(n) ? m.jsx(og, { x: t, y: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: c, labelBgBorderRadius: l }) : null] });
}
function Fr({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === W.Left || e === W.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function Ns({ sourceX: e, sourceY: t, sourcePosition: n = W.Bottom, targetX: o, targetY: r, targetPosition: i = W.Top }) {
  const [s, c] = Fr({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [l, a] = Fr({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [u, d, f, p] = qi({
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
    u,
    d,
    f,
    p
  ];
}
function Cs(e) {
  return fe(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: c, label: l, labelStyle: a, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: p, style: g, markerEnd: v, markerStart: w, interactionWidth: y }) => {
    const [_, h, x] = Ns({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: c
    }), C = e.isInternal ? void 0 : t;
    return m.jsx(Cn, { id: C, path: _, labelX: h, labelY: x, label: l, labelStyle: a, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: p, style: g, markerEnd: v, markerStart: w, interactionWidth: y });
  });
}
const rg = Cs({ isInternal: !1 }), Ms = Cs({ isInternal: !0 });
rg.displayName = "SimpleBezierEdge";
Ms.displayName = "SimpleBezierEdgeInternal";
function ks(e) {
  return fe(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: c, labelShowBg: l, labelBgStyle: a, labelBgPadding: u, labelBgBorderRadius: d, style: f, sourcePosition: p = W.Bottom, targetPosition: g = W.Top, markerEnd: v, markerStart: w, pathOptions: y, interactionWidth: _ }) => {
    const [h, x, C] = no({
      sourceX: n,
      sourceY: o,
      sourcePosition: p,
      targetX: r,
      targetY: i,
      targetPosition: g,
      borderRadius: y?.borderRadius,
      offset: y?.offset,
      stepPosition: y?.stepPosition
    }), S = e.isInternal ? void 0 : t;
    return m.jsx(Cn, { id: S, path: h, labelX: x, labelY: C, label: s, labelStyle: c, labelShowBg: l, labelBgStyle: a, labelBgPadding: u, labelBgBorderRadius: d, style: f, markerEnd: v, markerStart: w, interactionWidth: _ });
  });
}
const Is = ks({ isInternal: !1 }), As = ks({ isInternal: !0 });
Is.displayName = "SmoothStepEdge";
As.displayName = "SmoothStepEdgeInternal";
function Ds(e) {
  return fe(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return m.jsx(Is, { ...n, id: o, pathOptions: ve(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const ig = Ds({ isInternal: !1 }), $s = Ds({ isInternal: !0 });
ig.displayName = "StepEdge";
$s.displayName = "StepEdgeInternal";
function Ps(e) {
  return fe(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: c, labelShowBg: l, labelBgStyle: a, labelBgPadding: u, labelBgBorderRadius: d, style: f, markerEnd: p, markerStart: g, interactionWidth: v }) => {
    const [w, y, _] = Ui({ sourceX: n, sourceY: o, targetX: r, targetY: i }), h = e.isInternal ? void 0 : t;
    return m.jsx(Cn, { id: h, path: w, labelX: y, labelY: _, label: s, labelStyle: c, labelShowBg: l, labelBgStyle: a, labelBgPadding: u, labelBgBorderRadius: d, style: f, markerEnd: p, markerStart: g, interactionWidth: v });
  });
}
const sg = Ps({ isInternal: !1 }), Ts = Ps({ isInternal: !0 });
sg.displayName = "StraightEdge";
Ts.displayName = "StraightEdgeInternal";
function zs(e) {
  return fe(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s = W.Bottom, targetPosition: c = W.Top, label: l, labelStyle: a, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: p, style: g, markerEnd: v, markerStart: w, pathOptions: y, interactionWidth: _ }) => {
    const [h, x, C] = Wi({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: c,
      curvature: y?.curvature
    }), S = e.isInternal ? void 0 : t;
    return m.jsx(Cn, { id: S, path: h, labelX: x, labelY: C, label: l, labelStyle: a, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: p, style: g, markerEnd: v, markerStart: w, interactionWidth: _ });
  });
}
const ag = zs({ isInternal: !1 }), Rs = zs({ isInternal: !0 });
ag.displayName = "BezierEdge";
Rs.displayName = "BezierEdgeInternal";
const Yr = {
  default: Rs,
  straight: Ts,
  step: $s,
  smoothstep: As,
  simplebezier: Ms
}, Xr = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, cg = (e, t, n) => n === W.Left ? e - t : n === W.Right ? e + t : e, lg = (e, t, n) => n === W.Top ? e - t : n === W.Bottom ? e + t : e, qr = "react-flow__edgeupdater";
function Wr({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: i, onMouseOut: s, type: c }) {
  return m.jsx("circle", { onMouseDown: r, onMouseEnter: i, onMouseOut: s, className: he([qr, `${qr}-${c}`]), cx: cg(t, o, e), cy: lg(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function ug({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: i, targetY: s, sourcePosition: c, targetPosition: l, onReconnect: a, onReconnectStart: u, onReconnectEnd: d, setReconnecting: f, setUpdateHover: p }) {
  const g = ue(), v = (x, C) => {
    if (x.button !== 0)
      return;
    const { autoPanOnConnect: S, domNode: I, connectionMode: A, connectionRadius: $, lib: V, onConnectStart: k, cancelConnection: T, nodeLookup: j, rfId: b, panBy: N, updateConnection: E } = g.getState(), M = C.type === "target", P = (L, H) => {
      f(!1), d?.(L, n, C.type, H);
    }, D = (L) => a?.(n, L), O = (L, H) => {
      f(!0), u?.(x, n, C.type), k?.(L, H);
    };
    io.onPointerDown(x.nativeEvent, {
      autoPanOnConnect: S,
      connectionMode: A,
      connectionRadius: $,
      domNode: I,
      handleId: C.id,
      nodeId: C.nodeId,
      nodeLookup: j,
      isTarget: M,
      edgeUpdaterType: C.type,
      lib: V,
      flowId: b,
      cancelConnection: T,
      panBy: N,
      isValidConnection: (...L) => g.getState().isValidConnection?.(...L) ?? !0,
      onConnect: D,
      onConnectStart: O,
      onConnectEnd: (...L) => g.getState().onConnectEnd?.(...L),
      onReconnectEnd: P,
      updateConnection: E,
      getTransform: () => g.getState().transform,
      getFromHandle: () => g.getState().connection.fromHandle,
      dragThreshold: g.getState().connectionDragThreshold,
      handleDomNode: x.currentTarget
    });
  }, w = (x) => v(x, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), y = (x) => v(x, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), _ = () => p(!0), h = () => p(!1);
  return m.jsxs(m.Fragment, { children: [(e === !0 || e === "source") && m.jsx(Wr, { position: c, centerX: o, centerY: r, radius: t, onMouseDown: w, onMouseEnter: _, onMouseOut: h, type: "source" }), (e === !0 || e === "target") && m.jsx(Wr, { position: l, centerX: i, centerY: s, radius: t, onMouseDown: y, onMouseEnter: _, onMouseOut: h, type: "target" })] });
}
function dg({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: i, onContextMenu: s, onMouseEnter: c, onMouseMove: l, onMouseLeave: a, reconnectRadius: u, onReconnect: d, onReconnectStart: f, onReconnectEnd: p, rfId: g, edgeTypes: v, noPanClassName: w, onError: y, disableKeyboardA11y: _ }) {
  let h = re((q) => q.edgeLookup.get(e));
  const x = re((q) => q.defaultEdgeOptions);
  h = x ? { ...x, ...h } : h;
  let C = h.type || "default", S = v?.[C] || Yr[C];
  S === void 0 && (y?.("011", De.error011(C)), C = "default", S = v?.default || Yr.default);
  const I = !!(h.focusable || t && typeof h.focusable > "u"), A = typeof d < "u" && (h.reconnectable || n && typeof h.reconnectable > "u"), $ = !!(h.selectable || o && typeof h.selectable > "u"), V = ne(null), [k, T] = ce(!1), [j, b] = ce(!1), N = ue(), { zIndex: E, sourceX: M, sourceY: P, targetX: D, targetY: O, sourcePosition: L, targetPosition: H } = re(xe((q) => {
    const G = q.nodeLookup.get(h.source), te = q.nodeLookup.get(h.target);
    if (!G || !te)
      return {
        zIndex: h.zIndex,
        ...Xr
      };
    const se = Jd({
      id: e,
      sourceNode: G,
      targetNode: te,
      sourceHandle: h.sourceHandle || null,
      targetHandle: h.targetHandle || null,
      connectionMode: q.connectionMode,
      onError: y
    });
    return {
      zIndex: Xd({
        selected: h.selected,
        zIndex: h.zIndex,
        sourceNode: G,
        targetNode: te,
        elevateOnSelect: q.elevateEdgesOnSelect,
        zIndexMode: q.zIndexMode
      }),
      ...se || Xr
    };
  }, [h.source, h.target, h.sourceHandle, h.targetHandle, h.selected, h.zIndex]), le), X = ve(() => h.markerStart ? `url('#${oo(h.markerStart, g)}')` : void 0, [h.markerStart, g]), Y = ve(() => h.markerEnd ? `url('#${oo(h.markerEnd, g)}')` : void 0, [h.markerEnd, g]);
  if (h.hidden || M === null || P === null || D === null || O === null)
    return null;
  const Z = (q) => {
    const { addSelectedEdges: G, unselectNodesAndEdges: te, multiSelectionActive: se } = N.getState();
    $ && (N.setState({ nodesSelectionActive: !1 }), h.selected && se ? (te({ nodes: [], edges: [h] }), V.current?.blur()) : G([e])), r && r(q, h);
  }, ee = i ? (q) => {
    i(q, { ...h });
  } : void 0, U = s ? (q) => {
    s(q, { ...h });
  } : void 0, R = c ? (q) => {
    c(q, { ...h });
  } : void 0, F = l ? (q) => {
    l(q, { ...h });
  } : void 0, J = a ? (q) => {
    a(q, { ...h });
  } : void 0, K = (q) => {
    if (!_ && $i.includes(q.key) && $) {
      const { unselectNodesAndEdges: G, addSelectedEdges: te } = N.getState();
      q.key === "Escape" ? (V.current?.blur(), G({ edges: [h] })) : te([e]);
    }
  };
  return m.jsx("svg", { style: { zIndex: E }, children: m.jsxs("g", { className: he([
    "react-flow__edge",
    `react-flow__edge-${C}`,
    h.className,
    w,
    {
      selected: h.selected,
      animated: h.animated,
      inactive: !$ && !r,
      updating: k,
      selectable: $
    }
  ]), onClick: Z, onDoubleClick: ee, onContextMenu: U, onMouseEnter: R, onMouseMove: F, onMouseLeave: J, onKeyDown: I ? K : void 0, tabIndex: I ? 0 : void 0, role: h.ariaRole ?? (I ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": h.ariaLabel === null ? void 0 : h.ariaLabel || `Edge from ${h.source} to ${h.target}`, "aria-describedby": I ? `${us}-${g}` : void 0, ref: V, ...h.domAttributes, children: [!j && m.jsx(S, { id: e, source: h.source, target: h.target, type: h.type, selected: h.selected, animated: h.animated, selectable: $, deletable: h.deletable ?? !0, label: h.label, labelStyle: h.labelStyle, labelShowBg: h.labelShowBg, labelBgStyle: h.labelBgStyle, labelBgPadding: h.labelBgPadding, labelBgBorderRadius: h.labelBgBorderRadius, sourceX: M, sourceY: P, targetX: D, targetY: O, sourcePosition: L, targetPosition: H, data: h.data, style: h.style, sourceHandleId: h.sourceHandle, targetHandleId: h.targetHandle, markerStart: X, markerEnd: Y, pathOptions: "pathOptions" in h ? h.pathOptions : void 0, interactionWidth: h.interactionWidth }), A && m.jsx(ug, { edge: h, isReconnectable: A, reconnectRadius: u, onReconnect: d, onReconnectStart: f, onReconnectEnd: p, sourceX: M, sourceY: P, targetX: D, targetY: O, sourcePosition: L, targetPosition: H, setUpdateHover: T, setReconnecting: b })] }) });
}
var fg = fe(dg);
const hg = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function js({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: i, onEdgeContextMenu: s, onEdgeMouseEnter: c, onEdgeMouseMove: l, onEdgeMouseLeave: a, onEdgeClick: u, reconnectRadius: d, onEdgeDoubleClick: f, onReconnectStart: p, onReconnectEnd: g, disableKeyboardA11y: v }) {
  const { edgesFocusable: w, edgesReconnectable: y, elementsSelectable: _, onError: h } = re(hg, le), x = Kh(t);
  return m.jsxs("div", { className: "react-flow__edges", children: [m.jsx(ng, { defaultColor: e, rfId: n }), x.map((C) => m.jsx(fg, { id: C, edgesFocusable: w, edgesReconnectable: y, elementsSelectable: _, noPanClassName: r, onReconnect: i, onContextMenu: s, onMouseEnter: c, onMouseMove: l, onMouseLeave: a, onClick: u, reconnectRadius: d, onDoubleClick: f, onReconnectStart: p, onReconnectEnd: g, rfId: n, onError: h, edgeTypes: o, disableKeyboardA11y: v }, C))] });
}
js.displayName = "EdgeRenderer";
const gg = fe(js), pg = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function mg({ children: e }) {
  const t = re(pg);
  return m.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function yg(e) {
  const t = Mo(), n = ne(!1);
  ie(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const xg = (e) => e.panZoom?.syncViewport;
function wg(e) {
  const t = re(xg), n = ue();
  return ie(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function vg(e) {
  return e.connection.inProgress ? { ...e.connection, to: mt(e.connection.to, e.transform) } : { ...e.connection };
}
function bg(e) {
  return vg;
}
function _g(e) {
  const t = bg();
  return re(t, le);
}
const Sg = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function Eg({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: i, height: s, isValid: c, inProgress: l } = re(Sg, le);
  return !(i && r && l) ? null : m.jsx("svg", { style: e, width: i, height: s, className: "react-flow__connectionline react-flow__container", children: m.jsx("g", { className: he(["react-flow__connection", zi(c)]), children: m.jsx(Ls, { style: t, type: n, CustomComponent: o, isValid: c }) }) });
}
const Ls = ({ style: e, type: t = Ye.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: i, fromNode: s, fromHandle: c, fromPosition: l, to: a, toNode: u, toHandle: d, toPosition: f, pointer: p } = _g();
  if (!r)
    return;
  if (n)
    return m.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: s, fromHandle: c, fromX: i.x, fromY: i.y, toX: a.x, toY: a.y, fromPosition: l, toPosition: f, connectionStatus: zi(o), toNode: u, toHandle: d, pointer: p });
  let g = "";
  const v = {
    sourceX: i.x,
    sourceY: i.y,
    sourcePosition: l,
    targetX: a.x,
    targetY: a.y,
    targetPosition: f
  };
  switch (t) {
    case Ye.Bezier:
      [g] = Wi(v);
      break;
    case Ye.SimpleBezier:
      [g] = Ns(v);
      break;
    case Ye.Step:
      [g] = no({
        ...v,
        borderRadius: 0
      });
      break;
    case Ye.SmoothStep:
      [g] = no(v);
      break;
    default:
      [g] = Ui(v);
  }
  return m.jsx("path", { d: g, fill: "none", className: "react-flow__connection-path", style: e });
};
Ls.displayName = "ConnectionLine";
const Ng = {};
function Zr(e = Ng) {
  ne(e), ue(), ie(() => {
  }, [e]);
}
function Cg() {
  ue(), ne(!1), ie(() => {
  }, []);
}
function Hs({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: i, onEdgeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: l, onNodeMouseLeave: a, onNodeContextMenu: u, onSelectionContextMenu: d, onSelectionStart: f, onSelectionEnd: p, connectionLineType: g, connectionLineStyle: v, connectionLineComponent: w, connectionLineContainerStyle: y, selectionKeyCode: _, selectionOnDrag: h, selectionMode: x, multiSelectionKeyCode: C, panActivationKeyCode: S, zoomActivationKeyCode: I, deleteKeyCode: A, onlyRenderVisibleElements: $, elementsSelectable: V, defaultViewport: k, translateExtent: T, minZoom: j, maxZoom: b, preventScrolling: N, defaultMarkerColor: E, zoomOnScroll: M, zoomOnPinch: P, panOnScroll: D, panOnScrollSpeed: O, panOnScrollMode: L, zoomOnDoubleClick: H, panOnDrag: X, autoPanOnSelection: Y, onPaneClick: Z, onPaneMouseEnter: ee, onPaneMouseMove: U, onPaneMouseLeave: R, onPaneScroll: F, onPaneContextMenu: J, paneClickDistance: K, nodeClickDistance: q, onEdgeContextMenu: G, onEdgeMouseEnter: te, onEdgeMouseMove: se, onEdgeMouseLeave: de, reconnectRadius: me, onReconnect: Ce, onReconnectStart: Se, onReconnectEnd: Ee, noDragClassName: z, noWheelClassName: B, noPanClassName: Q, disableKeyboardA11y: oe, nodeExtent: ae, rfId: ge, viewport: pe, onViewportChange: je }) {
  return Zr(e), Zr(t), Cg(), yg(n), wg(pe), m.jsx(Vh, { onPaneClick: Z, onPaneMouseEnter: ee, onPaneMouseMove: U, onPaneMouseLeave: R, onPaneContextMenu: J, onPaneScroll: F, paneClickDistance: K, deleteKeyCode: A, selectionKeyCode: _, selectionOnDrag: h, selectionMode: x, onSelectionStart: f, onSelectionEnd: p, multiSelectionKeyCode: C, panActivationKeyCode: S, zoomActivationKeyCode: I, elementsSelectable: V, zoomOnScroll: M, zoomOnPinch: P, zoomOnDoubleClick: H, panOnScroll: D, panOnScrollSpeed: O, panOnScrollMode: L, panOnDrag: X, autoPanOnSelection: Y, defaultViewport: k, translateExtent: T, minZoom: j, maxZoom: b, onSelectionContextMenu: d, preventScrolling: N, noDragClassName: z, noWheelClassName: B, noPanClassName: Q, disableKeyboardA11y: oe, onViewportChange: je, isControlledViewport: !!pe, children: m.jsxs(mg, { children: [m.jsx(gg, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: s, onReconnect: Ce, onReconnectStart: Se, onReconnectEnd: Ee, onlyRenderVisibleElements: $, onEdgeContextMenu: G, onEdgeMouseEnter: te, onEdgeMouseMove: se, onEdgeMouseLeave: de, reconnectRadius: me, defaultMarkerColor: E, noPanClassName: Q, disableKeyboardA11y: oe, rfId: ge }), m.jsx(Eg, { style: v, type: g, component: w, containerStyle: y }), m.jsx("div", { className: "react-flow__edgelabel-renderer" }), m.jsx(Gh, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: i, onNodeMouseEnter: c, onNodeMouseMove: l, onNodeMouseLeave: a, onNodeContextMenu: u, nodeClickDistance: q, onlyRenderVisibleElements: $, noPanClassName: Q, noDragClassName: z, disableKeyboardA11y: oe, nodeExtent: ae, rfId: ge }), m.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Hs.displayName = "GraphView";
const Mg = fe(Hs), kg = Oi(), Ur = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: c, minZoom: l = 0.5, maxZoom: a = 2, nodeOrigin: u, nodeExtent: d, zIndexMode: f = "basic" } = {}) => {
  const p = /* @__PURE__ */ new Map(), g = /* @__PURE__ */ new Map(), v = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), y = o ?? t ?? [], _ = n ?? e ?? [], h = u ?? [0, 0], x = d ?? It;
  Qi(v, w, y);
  const { nodesInitialized: C } = ro(_, p, g, {
    nodeOrigin: h,
    nodeExtent: x,
    zIndexMode: f
  });
  let S = [0, 0, 1];
  if (s && r && i) {
    const I = Ht(p, {
      filter: (k) => !!((k.width || k.initialWidth) && (k.height || k.initialHeight))
    }), { x: A, y: $, zoom: V } = vo(I, r, i, l, a, c?.padding ?? 0.1);
    S = [A, $, V];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: i ?? 0,
    transform: S,
    nodes: _,
    nodesInitialized: C,
    nodeLookup: p,
    parentLookup: g,
    edges: y,
    edgeLookup: w,
    connectionLookup: v,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: o !== void 0,
    panZoom: null,
    minZoom: l,
    maxZoom: a,
    translateExtent: It,
    nodeExtent: x,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: ut.Strict,
    domNode: null,
    paneDragging: !1,
    noPanClassName: "nopan",
    nodeOrigin: h,
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
    connection: { ...Ti },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: kg,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Pi,
    zIndexMode: f,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, Ig = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: c, minZoom: l, maxZoom: a, nodeOrigin: u, nodeExtent: d, zIndexMode: f }) => Ff((p, g) => {
  async function v() {
    const { nodeLookup: w, panZoom: y, fitViewOptions: _, fitViewResolver: h, width: x, height: C, minZoom: S, maxZoom: I } = g();
    y && (await Ld({
      nodes: w,
      width: x,
      height: C,
      panZoom: y,
      minZoom: S,
      maxZoom: I
    }, _), h?.resolve(!0), p({ fitViewResolver: null }));
  }
  return {
    ...Ur({
      nodes: e,
      edges: t,
      width: r,
      height: i,
      fitView: s,
      fitViewOptions: c,
      minZoom: l,
      maxZoom: a,
      nodeOrigin: u,
      nodeExtent: d,
      defaultNodes: n,
      defaultEdges: o,
      zIndexMode: f
    }),
    setNodes: (w) => {
      const { nodeLookup: y, parentLookup: _, nodeOrigin: h, elevateNodesOnSelect: x, fitViewQueued: C, zIndexMode: S, nodesSelectionActive: I } = g(), { nodesInitialized: A, hasSelectedNodes: $ } = ro(w, y, _, {
        nodeOrigin: h,
        nodeExtent: d,
        elevateNodesOnSelect: x,
        checkEquality: !0,
        zIndexMode: S
      }), V = I && $;
      C && A ? (v(), p({
        nodes: w,
        nodesInitialized: A,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: V
      })) : p({ nodes: w, nodesInitialized: A, nodesSelectionActive: V });
    },
    setEdges: (w) => {
      const { connectionLookup: y, edgeLookup: _ } = g();
      Qi(y, _, w), p({ edges: w });
    },
    setDefaultNodesAndEdges: (w, y) => {
      if (w) {
        const { setNodes: _ } = g();
        _(w), p({ hasDefaultNodes: !0 });
      }
      if (y) {
        const { setEdges: _ } = g();
        _(y), p({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (w) => {
      const { triggerNodeChanges: y, nodeLookup: _, parentLookup: h, domNode: x, nodeOrigin: C, nodeExtent: S, debug: I, fitViewQueued: A, zIndexMode: $ } = g(), { changes: V, updatedInternals: k } = cf(w, _, h, x, C, S, $);
      k && (of(_, h, { nodeOrigin: C, nodeExtent: S, zIndexMode: $ }), A ? (v(), p({ fitViewQueued: !1, fitViewOptions: void 0 })) : p({}), V?.length > 0 && (I && console.log("React Flow: trigger node changes", V), y?.(V)));
    },
    updateNodePositions: (w, y = !1) => {
      const _ = [];
      let h = [];
      const { nodeLookup: x, triggerNodeChanges: C, connection: S, updateConnection: I, onNodesChangeMiddlewareMap: A } = g();
      for (const [$, V] of w) {
        const k = x.get($), T = !!(k?.expandParent && k?.parentId && V?.position), j = {
          id: $,
          type: "position",
          position: T ? {
            x: Math.max(0, V.position.x),
            y: Math.max(0, V.position.y)
          } : V.position,
          dragging: y
        };
        if (k && S.inProgress && S.fromNode.id === k.id) {
          const b = tt(k, S.fromHandle, W.Left, !0);
          I({ ...S, from: b });
        }
        T && k.parentId && _.push({
          id: $,
          parentId: k.parentId,
          rect: {
            ...V.internals.positionAbsolute,
            width: V.measured.width ?? 0,
            height: V.measured.height ?? 0
          }
        }), h.push(j);
      }
      if (_.length > 0) {
        const { parentLookup: $, nodeOrigin: V } = g(), k = Co(_, x, $, V);
        h.push(...k);
      }
      for (const $ of A.values())
        h = $(h);
      C(h);
    },
    triggerNodeChanges: (w) => {
      const { onNodesChange: y, setNodes: _, nodes: h, hasDefaultNodes: x, debug: C } = g();
      if (w?.length) {
        if (x) {
          const S = hs(w, h);
          _(S);
        }
        C && console.log("React Flow: trigger node changes", w), y?.(w);
      }
    },
    triggerEdgeChanges: (w) => {
      const { onEdgesChange: y, setEdges: _, edges: h, hasDefaultEdges: x, debug: C } = g();
      if (w?.length) {
        if (x) {
          const S = gs(w, h);
          _(S);
        }
        C && console.log("React Flow: trigger edge changes", w), y?.(w);
      }
    },
    addSelectedNodes: (w) => {
      const { multiSelectionActive: y, edgeLookup: _, nodeLookup: h, triggerNodeChanges: x, triggerEdgeChanges: C } = g();
      if (y) {
        const S = w.map((I) => We(I, !0));
        x(S);
        return;
      }
      x(it(h, /* @__PURE__ */ new Set([...w]), !0)), C(it(_));
    },
    addSelectedEdges: (w) => {
      const { multiSelectionActive: y, edgeLookup: _, nodeLookup: h, triggerNodeChanges: x, triggerEdgeChanges: C } = g();
      if (y) {
        const S = w.map((I) => We(I, !0));
        C(S);
        return;
      }
      C(it(_, /* @__PURE__ */ new Set([...w]))), x(it(h, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: w, edges: y } = {}) => {
      const { edges: _, nodes: h, nodeLookup: x, triggerNodeChanges: C, triggerEdgeChanges: S } = g(), I = w || h, A = y || _, $ = [];
      for (const k of I) {
        if (!k.selected)
          continue;
        const T = x.get(k.id);
        T && (T.selected = !1), $.push(We(k.id, !1));
      }
      const V = [];
      for (const k of A)
        k.selected && V.push(We(k.id, !1));
      C($), S(V);
    },
    setMinZoom: (w) => {
      const { panZoom: y, maxZoom: _ } = g();
      y?.setScaleExtent([w, _]), p({ minZoom: w });
    },
    setMaxZoom: (w) => {
      const { panZoom: y, minZoom: _ } = g();
      y?.setScaleExtent([_, w]), p({ maxZoom: w });
    },
    setTranslateExtent: (w) => {
      g().panZoom?.setTranslateExtent(w), p({ translateExtent: w });
    },
    resetSelectedElements: () => {
      const { edges: w, nodes: y, triggerNodeChanges: _, triggerEdgeChanges: h, elementsSelectable: x } = g();
      if (!x)
        return;
      const C = y.reduce((I, A) => A.selected ? [...I, We(A.id, !1)] : I, []), S = w.reduce((I, A) => A.selected ? [...I, We(A.id, !1)] : I, []);
      _(C), h(S);
    },
    setNodeExtent: (w) => {
      const { nodes: y, nodeLookup: _, parentLookup: h, nodeOrigin: x, elevateNodesOnSelect: C, nodeExtent: S, zIndexMode: I } = g();
      w[0][0] === S[0][0] && w[0][1] === S[0][1] && w[1][0] === S[1][0] && w[1][1] === S[1][1] || (ro(y, _, h, {
        nodeOrigin: x,
        nodeExtent: w,
        elevateNodesOnSelect: C,
        checkEquality: !1,
        zIndexMode: I
      }), p({ nodeExtent: w }));
    },
    panBy: (w) => {
      const { transform: y, width: _, height: h, panZoom: x, translateExtent: C } = g();
      return lf({ delta: w, panZoom: x, transform: y, translateExtent: C, width: _, height: h });
    },
    setCenter: async (w, y, _) => {
      const { width: h, height: x, maxZoom: C, panZoom: S } = g();
      if (!S)
        return !1;
      const I = typeof _?.zoom < "u" ? _.zoom : C;
      return await S.setViewport({
        x: h / 2 - w * I,
        y: x / 2 - y * I,
        zoom: I
      }, { duration: _?.duration, ease: _?.ease, interpolate: _?.interpolate }), !0;
    },
    cancelConnection: () => {
      p({
        connection: { ...Ti }
      });
    },
    updateConnection: (w) => {
      p({ connection: w });
    },
    reset: () => p({ ...Ur() })
  };
}, Object.is);
function Ag({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: i, initialMinZoom: s, initialMaxZoom: c, initialFitViewOptions: l, fitView: a, nodeOrigin: u, nodeExtent: d, zIndexMode: f, children: p }) {
  const [g] = ce(() => Ig({
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
    nodeOrigin: u,
    nodeExtent: d,
    zIndexMode: f
  }));
  return m.jsx(qf, { value: g, children: m.jsx(mh, { children: p }) });
}
function Dg({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: i, height: s, fitView: c, fitViewOptions: l, minZoom: a, maxZoom: u, nodeOrigin: d, nodeExtent: f, zIndexMode: p }) {
  return zt(Sn) ? m.jsx(m.Fragment, { children: e }) : m.jsx(Ag, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: i, initialHeight: s, fitView: c, initialFitViewOptions: l, initialMinZoom: a, initialMaxZoom: u, nodeOrigin: d, nodeExtent: f, zIndexMode: p, children: e });
}
const $g = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function Pg({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: i, edgeTypes: s, onNodeClick: c, onEdgeClick: l, onInit: a, onMove: u, onMoveStart: d, onMoveEnd: f, onConnect: p, onConnectStart: g, onConnectEnd: v, onClickConnectStart: w, onClickConnectEnd: y, onNodeMouseEnter: _, onNodeMouseMove: h, onNodeMouseLeave: x, onNodeContextMenu: C, onNodeDoubleClick: S, onNodeDragStart: I, onNodeDrag: A, onNodeDragStop: $, onNodesDelete: V, onEdgesDelete: k, onDelete: T, onSelectionChange: j, onSelectionDragStart: b, onSelectionDrag: N, onSelectionDragStop: E, onSelectionContextMenu: M, onSelectionStart: P, onSelectionEnd: D, onBeforeDelete: O, connectionMode: L, connectionLineType: H = Ye.Bezier, connectionLineStyle: X, connectionLineComponent: Y, connectionLineContainerStyle: Z, deleteKeyCode: ee = "Backspace", selectionKeyCode: U = "Shift", selectionOnDrag: R = !1, selectionMode: F = At.Full, panActivationKeyCode: J = "Space", multiSelectionKeyCode: K = $t() ? "Meta" : "Control", zoomActivationKeyCode: q = $t() ? "Meta" : "Control", snapToGrid: G, snapGrid: te, onlyRenderVisibleElements: se = !1, selectNodesOnDrag: de, nodesDraggable: me, autoPanOnNodeFocus: Ce, nodesConnectable: Se, nodesFocusable: Ee, nodeOrigin: z = ds, edgesFocusable: B, edgesReconnectable: Q, elementsSelectable: oe = !0, defaultViewport: ae = ih, minZoom: ge = 0.5, maxZoom: pe = 2, translateExtent: je = It, preventScrolling: Ws = !0, nodeExtent: kn, defaultMarkerColor: Zs = "#b1b1b7", zoomOnScroll: Us = !0, zoomOnPinch: Gs = !0, panOnScroll: Ks = !1, panOnScrollSpeed: Qs = 0.5, panOnScrollMode: Js = Ge.Free, zoomOnDoubleClick: ea = !0, panOnDrag: ta = !0, onPaneClick: na, onPaneMouseEnter: oa, onPaneMouseMove: ra, onPaneMouseLeave: ia, onPaneScroll: sa, onPaneContextMenu: aa, paneClickDistance: ca = 1, nodeClickDistance: la = 0, children: ua, onReconnect: da, onReconnectStart: fa, onReconnectEnd: ha, onEdgeContextMenu: ga, onEdgeDoubleClick: pa, onEdgeMouseEnter: ma, onEdgeMouseMove: ya, onEdgeMouseLeave: xa, reconnectRadius: wa = 10, onNodesChange: va, onEdgesChange: ba, noDragClassName: _a = "nodrag", noWheelClassName: Sa = "nowheel", noPanClassName: $o = "nopan", fitView: Po, fitViewOptions: To, connectOnClick: Ea, attributionPosition: Na, proOptions: Ca, defaultEdgeOptions: Ma, elevateNodesOnSelect: ka = !0, elevateEdgesOnSelect: Ia = !1, disableKeyboardA11y: zo = !1, autoPanOnConnect: Aa, autoPanOnNodeDrag: Da, autoPanOnSelection: $a = !0, autoPanSpeed: Pa, connectionRadius: Ta, isValidConnection: za, onError: Ra, style: ja, id: Ro, nodeDragThreshold: La, connectionDragThreshold: Ha, viewport: Oa, onViewportChange: Va, width: Ba, height: Fa, colorMode: Ya = "light", debug: Xa, onScroll: jo, ariaLabelConfig: qa, zIndexMode: Lo = "basic", ...Wa }, Za) {
  const In = Ro || "1", Ua = lh(Ya), Ga = xe((Ho) => {
    Ho.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), jo?.(Ho);
  }, [jo]);
  return m.jsx("div", { "data-testid": "rf__wrapper", ...Wa, onScroll: Ga, style: { ...ja, ...$g }, ref: Za, className: he(["react-flow", r, Ua]), id: Ro, role: "application", children: m.jsxs(Dg, { nodes: e, edges: t, width: Ba, height: Fa, fitView: Po, fitViewOptions: To, minZoom: ge, maxZoom: pe, nodeOrigin: z, nodeExtent: kn, zIndexMode: Lo, children: [m.jsx(ch, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: p, onConnectStart: g, onConnectEnd: v, onClickConnectStart: w, onClickConnectEnd: y, nodesDraggable: me, autoPanOnNodeFocus: Ce, nodesConnectable: Se, nodesFocusable: Ee, edgesFocusable: B, edgesReconnectable: Q, elementsSelectable: oe, elevateNodesOnSelect: ka, elevateEdgesOnSelect: Ia, minZoom: ge, maxZoom: pe, nodeExtent: kn, onNodesChange: va, onEdgesChange: ba, snapToGrid: G, snapGrid: te, connectionMode: L, translateExtent: je, connectOnClick: Ea, defaultEdgeOptions: Ma, fitView: Po, fitViewOptions: To, onNodesDelete: V, onEdgesDelete: k, onDelete: T, onNodeDragStart: I, onNodeDrag: A, onNodeDragStop: $, onSelectionDrag: N, onSelectionDragStart: b, onSelectionDragStop: E, onMove: u, onMoveStart: d, onMoveEnd: f, noPanClassName: $o, nodeOrigin: z, rfId: In, autoPanOnConnect: Aa, autoPanOnNodeDrag: Da, autoPanSpeed: Pa, onError: Ra, connectionRadius: Ta, isValidConnection: za, selectNodesOnDrag: de, nodeDragThreshold: La, connectionDragThreshold: Ha, onBeforeDelete: O, debug: Xa, ariaLabelConfig: qa, zIndexMode: Lo }), m.jsx(Mg, { onInit: a, onNodeClick: c, onEdgeClick: l, onNodeMouseEnter: _, onNodeMouseMove: h, onNodeMouseLeave: x, onNodeContextMenu: C, onNodeDoubleClick: S, nodeTypes: i, edgeTypes: s, connectionLineType: H, connectionLineStyle: X, connectionLineComponent: Y, connectionLineContainerStyle: Z, selectionKeyCode: U, selectionOnDrag: R, selectionMode: F, deleteKeyCode: ee, multiSelectionKeyCode: K, panActivationKeyCode: J, zoomActivationKeyCode: q, onlyRenderVisibleElements: se, defaultViewport: ae, translateExtent: je, minZoom: ge, maxZoom: pe, preventScrolling: Ws, zoomOnScroll: Us, zoomOnPinch: Gs, zoomOnDoubleClick: ea, panOnScroll: Ks, panOnScrollSpeed: Qs, panOnScrollMode: Js, panOnDrag: ta, autoPanOnSelection: $a, onPaneClick: na, onPaneMouseEnter: oa, onPaneMouseMove: ra, onPaneMouseLeave: ia, onPaneScroll: sa, onPaneContextMenu: aa, paneClickDistance: ca, nodeClickDistance: la, onSelectionContextMenu: M, onSelectionStart: P, onSelectionEnd: D, onReconnect: da, onReconnectStart: fa, onReconnectEnd: ha, onEdgeContextMenu: ga, onEdgeDoubleClick: pa, onEdgeMouseEnter: ma, onEdgeMouseMove: ya, onEdgeMouseLeave: xa, reconnectRadius: wa, defaultMarkerColor: Zs, noDragClassName: _a, noWheelClassName: Sa, noPanClassName: $o, rfId: In, disableKeyboardA11y: zo, nodeExtent: kn, viewport: Oa, onViewportChange: Va }), m.jsx(rh, { onSelectionChange: j }), ua, m.jsx(Jf, { proOptions: Ca, position: Na }), m.jsx(Qf, { rfId: In, disableKeyboardA11y: zo })] }) });
}
var Tg = ms(Pg);
function zg({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return m.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: he(["react-flow__background-pattern", n, o]) });
}
function Rg({ radius: e, className: t }) {
  return m.jsx("circle", { cx: e, cy: e, r: e, className: he(["react-flow__background-pattern", "dots", t]) });
}
var Xe;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(Xe || (Xe = {}));
const jg = {
  [Xe.Dots]: 1,
  [Xe.Lines]: 1,
  [Xe.Cross]: 6
}, Lg = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function Os({
  id: e,
  variant: t = Xe.Dots,
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
  patternClassName: u
}) {
  const d = ne(null), { transform: f, patternId: p } = re(Lg, le), g = o || jg[t], v = t === Xe.Dots, w = t === Xe.Cross, y = Array.isArray(n) ? n : [n, n], _ = [y[0] * f[2] || 1, y[1] * f[2] || 1], h = g * f[2], x = Array.isArray(i) ? i : [i, i], C = w ? [h, h] : _, S = [
    x[0] * f[2] || 1 + C[0] / 2,
    x[1] * f[2] || 1 + C[1] / 2
  ], I = `${p}${e || ""}`;
  return m.jsxs("svg", { className: he(["react-flow__background", a]), style: {
    ...l,
    ...Nn,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": s
  }, ref: d, "data-testid": "rf__background", children: [m.jsx("pattern", { id: I, x: f[0] % _[0], y: f[1] % _[1], width: _[0], height: _[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${S[0]},-${S[1]})`, children: v ? m.jsx(Rg, { radius: h / 2, className: u }) : m.jsx(zg, { dimensions: C, lineWidth: r, variant: t, className: u }) }), m.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${I})` })] });
}
Os.displayName = "Background";
const Hg = fe(Os);
function Og() {
  return m.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: m.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function Vg() {
  return m.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: m.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function Bg() {
  return m.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: m.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function Fg() {
  return m.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: m.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function Yg() {
  return m.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: m.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function Gt({ children: e, className: t, ...n }) {
  return m.jsx("button", { type: "button", className: he(["react-flow__controls-button", t]), ...n, children: e });
}
const Xg = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function Vs({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: i, onZoomOut: s, onFitView: c, onInteractiveChange: l, className: a, children: u, position: d = "bottom-left", orientation: f = "vertical", "aria-label": p }) {
  const g = ue(), { isInteractive: v, minZoomReached: w, maxZoomReached: y, ariaLabelConfig: _ } = re(Xg, le), { zoomIn: h, zoomOut: x, fitView: C } = Mo(), S = () => {
    h(), i?.();
  }, I = () => {
    x(), s?.();
  }, A = () => {
    C(r), c?.();
  }, $ = () => {
    g.setState({
      nodesDraggable: !v,
      nodesConnectable: !v,
      elementsSelectable: !v
    }), l?.(!v);
  }, V = f === "horizontal" ? "horizontal" : "vertical";
  return m.jsxs(En, { className: he(["react-flow__controls", V, a]), position: d, style: e, "data-testid": "rf__controls", "aria-label": p ?? _["controls.ariaLabel"], children: [t && m.jsxs(m.Fragment, { children: [m.jsx(Gt, { onClick: S, className: "react-flow__controls-zoomin", title: _["controls.zoomIn.ariaLabel"], "aria-label": _["controls.zoomIn.ariaLabel"], disabled: y, children: m.jsx(Og, {}) }), m.jsx(Gt, { onClick: I, className: "react-flow__controls-zoomout", title: _["controls.zoomOut.ariaLabel"], "aria-label": _["controls.zoomOut.ariaLabel"], disabled: w, children: m.jsx(Vg, {}) })] }), n && m.jsx(Gt, { className: "react-flow__controls-fitview", onClick: A, title: _["controls.fitView.ariaLabel"], "aria-label": _["controls.fitView.ariaLabel"], children: m.jsx(Bg, {}) }), o && m.jsx(Gt, { className: "react-flow__controls-interactive", onClick: $, title: _["controls.interactive.ariaLabel"], "aria-label": _["controls.interactive.ariaLabel"], children: v ? m.jsx(Yg, {}) : m.jsx(Fg, {}) }), u] });
}
Vs.displayName = "Controls";
const qg = fe(Vs);
function Wg({ id: e, x: t, y: n, width: o, height: r, style: i, color: s, strokeColor: c, strokeWidth: l, className: a, borderRadius: u, shapeRendering: d, selected: f, onClick: p }) {
  const { background: g, backgroundColor: v } = i || {}, w = s || g || v;
  return m.jsx("rect", { className: he(["react-flow__minimap-node", { selected: f }, a]), x: t, y: n, rx: u, ry: u, width: o, height: r, style: {
    fill: w,
    stroke: c,
    strokeWidth: l
  }, shapeRendering: d, onClick: p ? (y) => p(y, e) : void 0 });
}
const Zg = fe(Wg), Ug = (e) => e.nodes.map((t) => t.id), Yn = (e) => e instanceof Function ? e : () => e;
function Gg({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: i = Zg,
  onClick: s
}) {
  const c = re(Ug, le), l = Yn(t), a = Yn(e), u = Yn(n), d = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return m.jsx(m.Fragment, { children: c.map((f) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    m.jsx(Qg, { id: f, nodeColorFunc: l, nodeStrokeColorFunc: a, nodeClassNameFunc: u, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: i, onClick: s, shapeRendering: d }, f)
  )) });
}
function Kg({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: i, shapeRendering: s, NodeComponent: c, onClick: l }) {
  const { node: a, x: u, y: d, width: f, height: p } = re((g) => {
    const v = g.nodeLookup.get(e);
    if (!v)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const w = v.internals.userNode, { x: y, y: _ } = v.internals.positionAbsolute, { width: h, height: x } = Ve(w);
    return {
      node: w,
      x: y,
      y: _,
      width: h,
      height: x
    };
  }, le);
  return !a || a.hidden || !Vi(a) ? null : m.jsx(c, { x: u, y: d, width: f, height: p, style: a.style, selected: !!a.selected, className: o(a), color: t(a), borderRadius: r, strokeColor: n(a), strokeWidth: i, shapeRendering: s, onClick: l, id: a.id });
}
const Qg = fe(Kg);
var Jg = fe(Gg);
const ep = 200, tp = 150, np = (e) => !e.hidden, op = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? Hi(Ht(e.nodeLookup, { filter: np }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, rp = "react-flow__minimap-desc";
function Bs({
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
  maskStrokeColor: u,
  maskStrokeWidth: d,
  position: f = "bottom-right",
  onClick: p,
  onNodeClick: g,
  pannable: v = !1,
  zoomable: w = !1,
  ariaLabel: y,
  inversePan: _,
  zoomStep: h = 1,
  offsetScale: x = 5
}) {
  const C = ue(), S = ne(null), { boundingRect: I, viewBB: A, rfId: $, panZoom: V, translateExtent: k, flowWidth: T, flowHeight: j, ariaLabelConfig: b } = re(op, le), N = e?.width ?? ep, E = e?.height ?? tp, M = I.width / N, P = I.height / E, D = Math.max(M, P), O = D * N, L = D * E, H = x * D, X = I.x - (O - I.width) / 2 - H, Y = I.y - (L - I.height) / 2 - H, Z = O + H * 2, ee = L + H * 2, U = `${rp}-${$}`, R = ne(0), F = ne();
  R.current = D, ie(() => {
    if (S.current && V)
      return F.current = xf({
        domNode: S.current,
        panZoom: V,
        getTransform: () => C.getState().transform,
        getViewScale: () => R.current
      }), () => {
        F.current?.destroy();
      };
  }, [V]), ie(() => {
    F.current?.update({
      translateExtent: k,
      width: T,
      height: j,
      inversePan: _,
      pannable: v,
      zoomStep: h,
      zoomable: w
    });
  }, [v, w, _, h, k, T, j]);
  const J = p ? (G) => {
    const [te, se] = F.current?.pointer(G) || [0, 0];
    p(G, { x: te, y: se });
  } : void 0, K = g ? xe((G, te) => {
    const se = C.getState().nodeLookup.get(te).internals.userNode;
    g(G, se);
  }, []) : void 0, q = y ?? b["minimap.ariaLabel"];
  return m.jsx(En, { position: f, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-background-color-props": typeof a == "string" ? a : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof d == "number" ? d * D : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof s == "number" ? s : void 0
  }, className: he(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: m.jsxs("svg", { width: N, height: E, viewBox: `${X} ${Y} ${Z} ${ee}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": U, ref: S, onClick: J, children: [q && m.jsx("title", { id: U, children: q }), m.jsx(Jg, { onClick: K, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: i, nodeClassName: r, nodeStrokeWidth: s, nodeComponent: c }), m.jsx("path", { className: "react-flow__minimap-mask", d: `M${X - H},${Y - H}h${Z + H * 2}v${ee + H * 2}h${-Z - H * 2}z
        M${A.x},${A.y}h${A.width}v${A.height}h${-A.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
Bs.displayName = "MiniMap";
const ip = fe(Bs), sp = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, ap = {
  [gt.Line]: "right",
  [gt.Handle]: "bottom-right"
};
function cp({ nodeId: e, position: t, variant: n = gt.Handle, className: o, style: r = void 0, children: i, color: s, minWidth: c = 10, minHeight: l = 10, maxWidth: a = Number.MAX_VALUE, maxHeight: u = Number.MAX_VALUE, keepAspectRatio: d = !1, resizeDirection: f, autoScale: p = !0, shouldResize: g, onResizeStart: v, onResize: w, onResizeEnd: y }) {
  const _ = vs(), h = typeof e == "string" ? e : _, x = ue(), C = ne(null), S = n === gt.Handle, I = re(xe(sp(S && p), [S, p]), le), A = ne(null), $ = t ?? ap[n];
  ie(() => {
    if (!(!C.current || !h))
      return A.current || (A.current = Df({
        domNode: C.current,
        nodeId: h,
        getStoreItems: () => {
          const { nodeLookup: k, transform: T, snapGrid: j, snapToGrid: b, nodeOrigin: N, domNode: E } = x.getState();
          return {
            nodeLookup: k,
            transform: T,
            snapGrid: j,
            snapToGrid: b,
            nodeOrigin: N,
            paneDomNode: E
          };
        },
        onChange: (k, T) => {
          const { triggerNodeChanges: j, nodeLookup: b, parentLookup: N, nodeOrigin: E } = x.getState(), M = [], P = { x: k.x, y: k.y }, D = b.get(h);
          if (D && D.expandParent && D.parentId) {
            const O = D.origin ?? E, L = k.width ?? D.measured.width ?? 0, H = k.height ?? D.measured.height ?? 0, X = {
              id: D.id,
              parentId: D.parentId,
              rect: {
                width: L,
                height: H,
                ...Bi({
                  x: k.x ?? D.position.x,
                  y: k.y ?? D.position.y
                }, { width: L, height: H }, D.parentId, b, O)
              }
            }, Y = Co([X], b, N, E);
            M.push(...Y), P.x = k.x ? Math.max(O[0] * L, k.x) : void 0, P.y = k.y ? Math.max(O[1] * H, k.y) : void 0;
          }
          if (P.x !== void 0 && P.y !== void 0) {
            const O = {
              id: h,
              type: "position",
              position: { ...P }
            };
            M.push(O);
          }
          if (k.width !== void 0 && k.height !== void 0) {
            const L = {
              id: h,
              type: "dimensions",
              resizing: !0,
              setAttributes: f ? f === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: k.width,
                height: k.height
              }
            };
            M.push(L);
          }
          for (const O of T) {
            const L = {
              ...O,
              type: "position"
            };
            M.push(L);
          }
          j(M);
        },
        onEnd: ({ width: k, height: T }) => {
          const j = {
            id: h,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: k,
              height: T
            }
          };
          x.getState().triggerNodeChanges([j]);
        }
      })), A.current.update({
        controlPosition: $,
        boundaries: {
          minWidth: c,
          minHeight: l,
          maxWidth: a,
          maxHeight: u
        },
        keepAspectRatio: d,
        resizeDirection: f,
        onResizeStart: v,
        onResize: w,
        onResizeEnd: y,
        shouldResize: g
      }), () => {
        A.current?.destroy();
      };
  }, [
    $,
    c,
    l,
    a,
    u,
    d,
    v,
    w,
    y,
    g
  ]);
  const V = $.split("-");
  return m.jsx("div", { className: he(["react-flow__resize-control", "nodrag", ...V, n, o]), ref: C, style: {
    ...r,
    scale: I,
    ...s && { [S ? "backgroundColor" : "borderColor"]: s }
  }, children: i });
}
fe(cp);
const lp = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), Fs = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var up = {
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
const dp = gn(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: o,
    className: r = "",
    children: i,
    iconNode: s,
    ...c
  }, l) => qn(
    "svg",
    {
      ref: l,
      ...up,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: Fs("lucide", r),
      ...c
    },
    [
      ...s.map(([a, u]) => qn(a, u)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const Ne = (e, t) => {
  const n = gn(
    ({ className: o, ...r }, i) => qn(dp, {
      ref: i,
      iconNode: t,
      className: Fs(`lucide-${lp(e)}`, o),
      ...r
    })
  );
  return n.displayName = `${e}`, n;
};
const fp = Ne("Boxes", [
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
const Io = Ne("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const Gr = Ne("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const Ao = Ne("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const Ys = Ne("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const hp = Ne("ListTree", [
  ["path", { d: "M21 12h-8", key: "1bmf0i" }],
  ["path", { d: "M21 6H8", key: "1pqkrb" }],
  ["path", { d: "M21 18h-8", key: "1tm79t" }],
  ["path", { d: "M3 6v4c0 1.1.9 2 2 2h3", key: "1ywdgy" }],
  ["path", { d: "M3 10v6c0 1.1.9 2 2 2h3", key: "2wc746" }]
]);
const gp = Ne("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const pp = Ne("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const mp = Ne("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const yp = Ne("Save", [
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
const xp = Ne("Search", [
  ["circle", { cx: "11", cy: "11", r: "8", key: "4ej97u" }],
  ["path", { d: "m21 21-4.3-4.3", key: "1qie3q" }]
]);
const Kr = Ne("Trash2", [
  ["path", { d: "M3 6h18", key: "d0wm0j" }],
  ["path", { d: "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", key: "4alrt4" }],
  ["path", { d: "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", key: "v07s0e" }],
  ["line", { x1: "10", x2: "10", y1: "11", y2: "17", key: "1uufr5" }],
  ["line", { x1: "14", x2: "14", y1: "11", y2: "17", key: "xtxkd" }]
]), Pe = "/_elsa/workflow-management";
async function wp(e, t, n = "active") {
  const o = new URLSearchParams({ state: n });
  return t.trim() && o.set("search", t.trim()), nt(e, `${Pe}/definitions?${o.toString()}`);
}
async function vp(e, t) {
  return nt(e, `${Pe}/definitions/${encodeURIComponent(t)}`);
}
async function bp(e, t) {
  return Vt(e, `${Pe}/definitions`, t);
}
async function _p(e, t) {
  await nt(e, `${Pe}/definitions/${encodeURIComponent(t)}`, {
    method: "DELETE"
  });
}
async function Sp(e, t) {
  await Vt(e, `${Pe}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function Ep(e, t) {
  await nt(e, `${Pe}/definitions/${encodeURIComponent(t)}/permanent`, {
    method: "DELETE"
  });
}
async function Np(e, t) {
  return nt(e, `${Pe}/drafts/${encodeURIComponent(t.id)}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify({ state: t.state, layout: t.layout })
  });
}
async function Cp(e, t) {
  return Vt(e, `${Pe}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function Mp(e, t) {
  return Vt(e, `${Pe}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function kp(e, t) {
  return Vt(e, `${Pe}/executables/${encodeURIComponent(t)}/run`, {});
}
async function Ip(e) {
  return nt(e, `${Pe}/activities`);
}
async function Vt(e, t, n) {
  return nt(e, t, {
    method: "POST",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify(n)
  });
}
async function nt(e, t, n) {
  const o = await fetch(new URL(t, e.baseUrl).toString(), n), r = await o.text();
  if (!o.ok)
    throw new Error(Ap(r) || `Request failed with ${o.status}.`);
  return r ? JSON.parse(r) : {};
}
function Ap(e) {
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
const Do = "elsa.sequence.structure", Mn = "elsa.flowchart.structure";
function Qr(e, t) {
  if (!e) return null;
  let n = e, o = qe(n)[0];
  if (!o) return null;
  for (const r of t) {
    const i = qe(n).find((c) => c.id === r.slotId);
    if (!i) return null;
    const s = i.activities.find((c) => c.nodeId === r.ownerNodeId);
    if (!s || (n = s, o = qe(n)[0], !o)) return null;
  }
  return { owner: n, slot: o };
}
function qe(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = Vp(t), r = Xn(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: Bp(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, i]) => Xn(i)).map(([i, s]) => ({
    id: `${t.kind}:${i}`,
    label: Yp(i),
    property: i,
    mode: "generic",
    activities: Xn(s) ?? []
  }));
}
function Dp(e, t, n) {
  const o = new Map(t.map((s) => [s.activityVersionId, s])), r = new Map(n.map((s) => [s.nodeId, s])), i = e.slot.activities.map((s, c) => {
    const l = o.get(s.activityVersionId), a = r.get(s.nodeId) ?? Fp(e.slot.mode, c);
    return {
      id: s.nodeId,
      type: "workflowActivity",
      position: { x: a.x, y: a.y },
      data: {
        label: l?.displayName ?? s.activityVersionId,
        activityVersionId: s.activityVersionId,
        activityTypeKey: l?.activityTypeKey,
        childSlots: qe(s)
      }
    };
  });
  return {
    nodes: i,
    edges: e.slot.mode === "flowchart" ? Hp(e.owner) : Lp(e.slot, i)
  };
}
function Xs(e, t, n) {
  if (t.length === 0) {
    const c = qe(e)[0];
    return c ? hn(e, c, n) : e;
  }
  const [o, ...r] = t, i = qe(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((c) => c.nodeId === o.ownerNodeId ? Xs(c, r, n) : c);
  return hn(e, i, s);
}
function ao(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, i = qe(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((c) => c.nodeId === o.ownerNodeId ? ao(c, r, n) : c);
  return hn(e, i, s);
}
function hn(e, t, n) {
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
function $p(e, t, n) {
  const o = new Map(e.slot.activities.map((i) => [i.nodeId, i])), r = t.map((i) => o.get(i.id)).filter((i) => !!i);
  return e.slot.mode === "sequence" && r.sort((i, s) => {
    const c = t.find((a) => a.id === i.nodeId), l = t.find((a) => a.id === s.nodeId);
    return (c?.position.x ?? 0) - (l?.position.x ?? 0);
  }), hn(e.owner, e.slot, r);
}
function Pp(e, t) {
  return {
    ...e,
    structure: jp(e.structure, t)
  };
}
function Tp(e, t) {
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
function zp(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: Rp(e)
  };
}
function qs(e) {
  return e.displayName || e.activityTypeKey.split(".").at(-1) || e.activityTypeKey;
}
function Rp(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: Do,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: Mn,
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
function jp(e, t) {
  return e ? {
    ...e,
    payload: {
      ...e.payload,
      connections: t.map((n) => ({
        id: n.id,
        source: { nodeId: n.source, port: n.sourceHandle ?? "Done" },
        target: { nodeId: n.target, port: n.targetHandle ?? "Done" }
      }))
    }
  } : e ?? null;
}
function Lp(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function Hp(e) {
  if (e.structure?.kind !== Mn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, i = n.target;
    return !r?.nodeId || !i?.nodeId ? null : {
      id: typeof n.id == "string" ? String(n.id) : `flow-${o}-${r.nodeId}-${i.nodeId}`,
      source: r.nodeId,
      target: i.nodeId,
      sourceHandle: r.port,
      targetHandle: i.port,
      type: "smoothstep",
      label: r.port && r.port !== "Done" ? r.port : void 0
    };
  }).filter((n) => !!n) : [];
}
function Xn(e) {
  return Array.isArray(e) ? e.filter(Op) : null;
}
function Op(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Vp(e) {
  return e.kind === Do ? "sequence" : e.kind === Mn ? "flowchart" : "generic";
}
function Bp(e) {
  return e.kind === Do || e.kind === Mn, "Activities";
}
function Fp(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function Yp(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Xp = { workflowActivity: Kp }, Jr = "application/x-elsa-activity-version-id", qp = 6, Wp = 1200;
function t0(e) {
  e.navigation.add({
    id: "workflows",
    label: "Workflows",
    path: "/workflows/definitions",
    order: 20,
    iconColor: "#0ea5e9"
  }), e.routes.add({
    id: "workflows-definitions",
    path: "/workflows/definitions",
    label: "Workflow definitions",
    component: () => /* @__PURE__ */ m.jsx(Zp, { context: e.backend })
  });
}
function Zp({ context: e }) {
  const [t, n] = ce(ei);
  ie(() => {
    const r = () => n(ei());
    return window.addEventListener("popstate", r), () => window.removeEventListener("popstate", r);
  }, []);
  const o = (r) => {
    const i = r ? `/workflows/definitions?definition=${encodeURIComponent(r)}` : "/workflows/definitions";
    window.history.pushState({}, "", i), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return t ? /* @__PURE__ */ m.jsx(Gp, { context: e, definitionId: t, onBack: () => o(null) }) : /* @__PURE__ */ m.jsx(Up, { context: e, onOpen: o });
}
function ei() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Up({ context: e, onOpen: t }) {
  const [n, o] = ce(""), [r, i] = ce("active"), [s, c] = ce("loading"), [l, a] = ce(""), [u, d] = ce(""), [f, p] = ce([]), g = xe(async () => {
    c("loading"), a("");
    try {
      const h = await wp(e, n, r);
      p(h.definitions), c("ready");
    } catch (h) {
      a(h instanceof Error ? h.message : String(h)), c("failed");
    }
  }, [e, n, r]);
  ie(() => {
    g();
  }, [g]);
  const v = async (h) => {
    const x = window.prompt("Workflow name", h === "sequence" ? "Sequence workflow" : "Flowchart workflow");
    if (!x?.trim()) return;
    const C = await bp(e, { name: x, description: null, rootKind: h });
    t(C.definition.id);
  }, w = async (h) => {
    if (window.confirm(`Delete workflow definition "${h.name}"? You can restore it from the Deleted view.`)) {
      d(""), a("");
      try {
        await _p(e, h.id), d(`Deleted ${h.name}`), await g();
      } catch (x) {
        a(x instanceof Error ? x.message : String(x));
      }
    }
  }, y = async (h) => {
    d(""), a("");
    try {
      await Sp(e, h.id), d(`Restored ${h.name}`), await g();
    } catch (x) {
      a(x instanceof Error ? x.message : String(x));
    }
  }, _ = async (h) => {
    if (window.confirm(`Permanently delete workflow definition "${h.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`)) {
      d(""), a("");
      try {
        await Ep(e, h.id), d(`Permanently deleted ${h.name}`), await g();
      } catch (x) {
        a(x instanceof Error ? x.message : String(x));
      }
    }
  };
  return /* @__PURE__ */ m.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ m.jsxs("div", { className: "wf-page-header", children: [
      /* @__PURE__ */ m.jsxs("div", { children: [
        /* @__PURE__ */ m.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
        /* @__PURE__ */ m.jsx("h2", { children: "Definitions" })
      ] }),
      /* @__PURE__ */ m.jsxs("div", { className: "wf-actions", children: [
        /* @__PURE__ */ m.jsxs("button", { type: "button", onClick: () => {
          v("sequence");
        }, children: [
          /* @__PURE__ */ m.jsx(pp, { size: 15 }),
          " Sequence"
        ] }),
        /* @__PURE__ */ m.jsxs("button", { type: "button", onClick: () => {
          v("flowchart");
        }, children: [
          /* @__PURE__ */ m.jsx(Ys, { size: 15 }),
          " Flowchart"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ m.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ m.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ m.jsx("button", { type: "button", className: r === "active" ? "active" : "", "aria-selected": r === "active", onClick: () => i("active"), children: "Active" }),
        /* @__PURE__ */ m.jsx("button", { type: "button", className: r === "deleted" ? "active" : "", "aria-selected": r === "deleted", onClick: () => i("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ m.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ m.jsx(xp, { size: 15 }),
        /* @__PURE__ */ m.jsx("input", { value: n, onChange: (h) => o(h.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ m.jsx("button", { type: "button", onClick: () => {
        g();
      }, children: "Refresh" })
    ] }),
    s === "failed" ? /* @__PURE__ */ m.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ m.jsx(Ao, { size: 16 }),
      " ",
      l
    ] }) : null,
    u ? /* @__PURE__ */ m.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ m.jsx(Io, { size: 14 }),
      " ",
      u
    ] }) : null,
    s === "loading" ? /* @__PURE__ */ m.jsx("div", { className: "wf-empty", children: "Loading workflow definitions..." }) : null,
    s === "ready" && f.length === 0 ? /* @__PURE__ */ m.jsxs("div", { className: "wf-empty", children: [
      "No ",
      r,
      " workflow definitions found."
    ] }) : null,
    s === "ready" && f.length > 0 ? /* @__PURE__ */ m.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
      /* @__PURE__ */ m.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ m.jsx("span", { children: "Name" }),
        /* @__PURE__ */ m.jsx("span", { children: "Latest version" }),
        /* @__PURE__ */ m.jsx("span", { children: r === "deleted" ? "Deleted" : "Draft" }),
        /* @__PURE__ */ m.jsx("span", { children: "Modified" }),
        /* @__PURE__ */ m.jsx("span", { children: "Actions" })
      ] }),
      f.map((h) => /* @__PURE__ */ m.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ m.jsxs("span", { children: [
          /* @__PURE__ */ m.jsx("strong", { children: h.name }),
          /* @__PURE__ */ m.jsx("small", { children: h.description || h.id })
        ] }),
        /* @__PURE__ */ m.jsx("span", { children: h.latestVersion ?? "No version" }),
        /* @__PURE__ */ m.jsx("span", { children: r === "deleted" ? ti(h.deletedAt) : h.draftId ? "Draft" : "None" }),
        /* @__PURE__ */ m.jsx("span", { children: ti(h.lastModifiedAt) }),
        /* @__PURE__ */ m.jsx("span", { className: "wf-row-actions", children: r === "active" ? /* @__PURE__ */ m.jsxs(m.Fragment, { children: [
          /* @__PURE__ */ m.jsx("button", { type: "button", onClick: () => t(h.id), children: "Open" }),
          /* @__PURE__ */ m.jsxs("button", { type: "button", className: "danger", onClick: () => {
            w(h);
          }, children: [
            /* @__PURE__ */ m.jsx(Kr, { size: 13 }),
            " Delete"
          ] })
        ] }) : /* @__PURE__ */ m.jsxs(m.Fragment, { children: [
          /* @__PURE__ */ m.jsxs("button", { type: "button", onClick: () => {
            y(h);
          }, children: [
            /* @__PURE__ */ m.jsx(mp, { size: 13 }),
            " Restore"
          ] }),
          /* @__PURE__ */ m.jsxs("button", { type: "button", className: "danger", onClick: () => {
            _(h);
          }, children: [
            /* @__PURE__ */ m.jsx(Kr, { size: 13 }),
            " Delete permanently"
          ] })
        ] }) })
      ] }, h.id))
    ] }) : null
  ] });
}
function Gp({ context: e, definitionId: t, onBack: n }) {
  const [o, r] = ce(null), [i, s] = ce(null), [c, l] = ce([]), [a, u] = ce([]), [d, f] = ce([]), [p, g] = ce([]), [v, w] = ce(null), [y, _] = ce(null), [h, x] = ce(""), [C, S] = ce(""), [I, A] = ce(!1), [$, V] = ce(null), k = ne(null), T = ne(""), j = ne(0), b = ne(null), N = ne(!1), E = i?.state.rootActivity ?? null, M = ve(() => Qr(E, a), [E, a]), P = ve(() => new Map(c.map((z) => [z.activityVersionId, z])), [c]), D = ve(() => M?.slot.activities.find((z) => z.nodeId === y) ?? null, [M, y]), O = D ? qe(D) : [], L = xe(async () => {
    x("");
    const [z, B] = await Promise.all([
      vp(e, t),
      Ip(e)
    ]), Q = z.draft ?? null;
    r(z), T.current = Q ? vt(Q) : "", s(Q), l(B.activities), u([]), _(null);
  }, [e, t]);
  ie(() => {
    L().catch((z) => x(z instanceof Error ? z.message : String(z)));
  }, [L]), ie(() => {
    if (!M) {
      f([]), g([]);
      return;
    }
    const z = Dp(M, c, i?.layout ?? []);
    f(z.nodes), g(z.edges);
  }, [M, c, i?.layout]);
  const H = (z) => {
    s((B) => B && { ...B, state: { ...B.state, rootActivity: z } });
  }, X = xe((z, B) => {
    if (!i?.state.rootActivity || !M) return;
    const Q = zp(z, Jp(z));
    s((oe) => {
      if (!oe?.state.rootActivity) return oe;
      const ae = Qr(oe.state.rootActivity, a);
      if (!ae) return oe;
      const ge = Xs(oe.state.rootActivity, a, [...ae.slot.activities, Q]), pe = B ? [
        ...oe.layout.filter((je) => je.nodeId !== Q.nodeId),
        {
          nodeId: Q.nodeId,
          x: Math.round(B.x),
          y: Math.round(B.y)
        }
      ] : oe.layout;
      return {
        ...oe,
        layout: pe,
        state: {
          ...oe.state,
          rootActivity: ge
        }
      };
    }), _(Q.nodeId);
  }, [i?.state.rootActivity, a, M]), Y = xe((z, B) => {
    if (!v || !k.current) return null;
    const Q = k.current.getBoundingClientRect();
    return v.screenToFlowPosition({
      x: z - Q.left,
      y: B - Q.top
    });
  }, [v]), Z = xe((z, B, Q) => {
    if (!k.current) return !1;
    const oe = k.current.getBoundingClientRect();
    if (!(B >= oe.left && B <= oe.right && Q >= oe.top && Q <= oe.bottom)) return !1;
    const ge = Y(B, Q);
    return ge ? (X(z, ge), !0) : !1;
  }, [X, Y]);
  ie(() => {
    const z = (Q) => {
      const oe = b.current;
      if (!oe) return;
      Math.hypot(Q.clientX - oe.startX, Q.clientY - oe.startY) >= qp && (oe.dragging = !0);
    }, B = (Q) => {
      const oe = b.current;
      if (b.current = null, !oe?.dragging || !v || !k.current) return;
      const ae = k.current.getBoundingClientRect();
      Q.clientX >= ae.left && Q.clientX <= ae.right && Q.clientY >= ae.top && Q.clientY <= ae.bottom && (N.current = !0, window.setTimeout(() => {
        N.current = !1;
      }, 0), Z(oe.activity, Q.clientX, Q.clientY));
    };
    return window.addEventListener("pointermove", z), window.addEventListener("pointerup", B), window.addEventListener("pointercancel", B), () => {
      window.removeEventListener("pointermove", z), window.removeEventListener("pointerup", B), window.removeEventListener("pointercancel", B);
    };
  }, [v, Z]);
  const ee = (z, B) => {
    z.dataTransfer.setData(Jr, B.activityVersionId), z.dataTransfer.setData("text/plain", B.activityVersionId), z.dataTransfer.effectAllowed = "copy";
  }, U = (z, B) => {
    z.clientX === 0 && z.clientY === 0 || Z(B, z.clientX, z.clientY) && (N.current = !0, window.setTimeout(() => {
      N.current = !1;
    }, 0));
  }, R = (z, B) => {
    z.button === 0 && (b.current = {
      activity: B,
      startX: z.clientX,
      startY: z.clientY,
      dragging: !1
    });
  }, F = (z) => {
    N.current || X(z);
  }, J = (z) => {
    z.preventDefault(), z.dataTransfer.dropEffect = "copy";
  }, K = (z) => {
    z.preventDefault();
    const B = z.dataTransfer.getData(Jr) || z.dataTransfer.getData("text/plain"), Q = P.get(B);
    Q && Z(Q, z.clientX, z.clientY);
  }, q = xe(async (z, B) => {
    const Q = ++j.current, oe = vt(z);
    x("");
    try {
      const ae = await Np(e, z), ge = vt(ae);
      T.current = ge, s((pe) => !pe || pe.id !== ae.id ? pe : vt(pe) === oe ? ae : { ...pe, validationErrors: ae.validationErrors }), Q === j.current && S(B);
    } catch (ae) {
      Q === j.current && (S(""), x(ae instanceof Error ? ae.message : String(ae)));
    }
  }, [e]);
  ie(() => {
    if (!I || !i || vt(i) === T.current) return;
    S("Autosaving...");
    const B = window.setTimeout(() => {
      q(i, "Autosaved");
    }, Wp);
    return () => window.clearTimeout(B);
  }, [I, i, q]);
  const G = async () => {
    i && (S("Saving..."), await q(i, "Saved"));
  }, te = async () => {
    if (i) {
      S("Promoting...");
      try {
        const z = await Cp(e, i.id), B = await Mp(e, z.versionId);
        V(B.artifactId), S(`Published ${B.artifactVersion}`), await L();
      } catch (z) {
        S(""), x(z instanceof Error ? z.message : String(z));
      }
    }
  }, se = async () => {
    if ($) {
      S("Running...");
      try {
        await kp(e, $), S("Run dispatched");
      } catch (z) {
        S(""), x(z instanceof Error ? z.message : String(z));
      }
    }
  }, de = (z) => f((B) => hs(z, B)), me = (z) => g((B) => gs(z, B)), Ce = (z) => {
    if (!i?.state.rootActivity || !M || M.slot.mode !== "flowchart") return;
    const B = ps(z, p), Q = Pp(M.owner, B);
    g(B), H(ao(i.state.rootActivity, a, Q));
  }, Se = () => {
    s((z) => {
      if (!z) return z;
      const B = Tp(z.layout, d);
      if (!z.state.rootActivity || !M) return { ...z, layout: B };
      const Q = $p(M, d);
      return {
        ...z,
        layout: B,
        state: {
          ...z.state,
          rootActivity: ao(z.state.rootActivity, a, Q)
        }
      };
    });
  }, Ee = (z, B, Q) => {
    u((oe) => [...oe, { ownerNodeId: z.nodeId, slotId: B, label: Q }]), _(null);
  };
  return !o || !i ? /* @__PURE__ */ m.jsx("div", { className: "wf-empty", children: h || "Loading workflow editor..." }) : /* @__PURE__ */ m.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ m.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ m.jsx("button", { type: "button", className: "wf-link-button", onClick: n, children: "Definitions" }),
      /* @__PURE__ */ m.jsx(Gr, { size: 14 }),
      /* @__PURE__ */ m.jsx("strong", { children: o.definition.name }),
      /* @__PURE__ */ m.jsx("span", { className: "wf-chip", children: "Draft" }),
      C ? /* @__PURE__ */ m.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ m.jsx(Io, { size: 13 }),
        " ",
        C
      ] }) : null,
      /* @__PURE__ */ m.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ m.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ m.jsx("input", { type: "checkbox", checked: I, onChange: (z) => A(z.target.checked) }),
          /* @__PURE__ */ m.jsx("span", { children: "Autosave" })
        ] }),
        /* @__PURE__ */ m.jsxs("button", { type: "button", onClick: () => {
          G();
        }, children: [
          /* @__PURE__ */ m.jsx(yp, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ m.jsxs("button", { type: "button", onClick: () => {
          te();
        }, children: [
          /* @__PURE__ */ m.jsx(Ys, { size: 15 }),
          " Promote"
        ] }),
        /* @__PURE__ */ m.jsxs("button", { type: "button", disabled: !$, onClick: () => {
          se();
        }, children: [
          /* @__PURE__ */ m.jsx(gp, { size: 15 }),
          " Run"
        ] })
      ] })
    ] }),
    h ? /* @__PURE__ */ m.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ m.jsx(Ao, { size: 16 }),
      " ",
      h
    ] }) : null,
    /* @__PURE__ */ m.jsxs("div", { className: "wf-editor-body", children: [
      /* @__PURE__ */ m.jsxs("aside", { className: "wf-palette", children: [
        /* @__PURE__ */ m.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ m.jsx(fp, { size: 15 }),
          " Activities"
        ] }),
        /* @__PURE__ */ m.jsx("div", { className: "wf-palette-list", children: c.map((z) => /* @__PURE__ */ m.jsxs(
          "button",
          {
            type: "button",
            draggable: !0,
            onClick: () => F(z),
            onDragStart: (B) => ee(B, z),
            onDragEnd: (B) => U(B, z),
            onPointerDown: (B) => R(B, z),
            children: [
              /* @__PURE__ */ m.jsx("strong", { children: qs(z) }),
              /* @__PURE__ */ m.jsx("small", { children: z.category })
            ]
          },
          z.activityVersionId
        )) })
      ] }),
      /* @__PURE__ */ m.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ m.jsxs("div", { className: "wf-breadcrumb", children: [
          /* @__PURE__ */ m.jsx("button", { type: "button", onClick: () => {
            u([]), _(null);
          }, children: "Root" }),
          a.map((z, B) => /* @__PURE__ */ m.jsxs(Tt.Fragment, { children: [
            /* @__PURE__ */ m.jsx(Gr, { size: 13 }),
            /* @__PURE__ */ m.jsx("button", { type: "button", onClick: () => {
              u(a.slice(0, B + 1)), _(null);
            }, children: z.label })
          ] }, `${z.ownerNodeId}-${z.slotId}-${B}`))
        ] }),
        /* @__PURE__ */ m.jsx("div", { className: "wf-canvas", ref: k, onDragOver: J, onDrop: K, children: /* @__PURE__ */ m.jsxs(
          Tg,
          {
            nodes: d,
            edges: p,
            nodeTypes: Xp,
            onInit: w,
            onNodesChange: de,
            onEdgesChange: me,
            onConnect: Ce,
            onDragOver: J,
            onDrop: K,
            onNodeClick: (z, B) => _(B.id),
            onNodeDragStop: Se,
            fitView: !0,
            children: [
              /* @__PURE__ */ m.jsx(Hg, { gap: 18, size: 1 }),
              /* @__PURE__ */ m.jsx(qg, {}),
              /* @__PURE__ */ m.jsx(ip, { pannable: !0, zoomable: !0 })
            ]
          }
        ) }),
        /* @__PURE__ */ m.jsx(Qp, { draft: i })
      ] }),
      /* @__PURE__ */ m.jsxs("aside", { className: "wf-inspector", children: [
        /* @__PURE__ */ m.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ m.jsx(hp, { size: 15 }),
          " Inspector"
        ] }),
        D ? /* @__PURE__ */ m.jsxs("div", { className: "wf-inspector-content", children: [
          /* @__PURE__ */ m.jsx("h3", { children: d.find((z) => z.id === D.nodeId)?.data.label ?? D.nodeId }),
          /* @__PURE__ */ m.jsxs("dl", { children: [
            /* @__PURE__ */ m.jsx("dt", { children: "Node ID" }),
            /* @__PURE__ */ m.jsx("dd", { children: D.nodeId }),
            /* @__PURE__ */ m.jsx("dt", { children: "Activity version" }),
            /* @__PURE__ */ m.jsx("dd", { children: D.activityVersionId })
          ] }),
          O.length > 0 ? /* @__PURE__ */ m.jsxs("div", { className: "wf-slot-list", children: [
            /* @__PURE__ */ m.jsx("span", { children: "Embedded slots" }),
            O.map((z) => /* @__PURE__ */ m.jsxs("button", { type: "button", onClick: () => Ee(D, z.id, `${d.find((B) => B.id === D.nodeId)?.data.label ?? D.nodeId} / ${z.label}`), children: [
              z.label,
              /* @__PURE__ */ m.jsxs("small", { children: [
                z.activities.length,
                " activit",
                z.activities.length === 1 ? "y" : "ies"
              ] })
            ] }, z.id))
          ] }) : /* @__PURE__ */ m.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
        ] }) : /* @__PURE__ */ m.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." })
      ] })
    ] })
  ] });
}
function Kp({ data: e, selected: t }) {
  const n = e;
  return /* @__PURE__ */ m.jsxs("div", { className: t ? "wf-node selected" : "wf-node", children: [
    /* @__PURE__ */ m.jsx(pt, { type: "target", position: W.Left }),
    /* @__PURE__ */ m.jsx("strong", { children: n.label }),
    /* @__PURE__ */ m.jsx("small", { children: n.activityTypeKey ?? n.activityVersionId }),
    n.childSlots.length > 0 ? /* @__PURE__ */ m.jsxs("span", { children: [
      n.childSlots.length,
      " embedded slot",
      n.childSlots.length === 1 ? "" : "s"
    ] }) : null,
    /* @__PURE__ */ m.jsx(pt, { type: "source", position: W.Right })
  ] });
}
function Qp({ draft: e }) {
  return e.validationErrors.length ? /* @__PURE__ */ m.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ m.jsx(Ao, { size: 14 }),
    e.validationErrors.length,
    " validation issue",
    e.validationErrors.length === 1 ? "" : "s"
  ] }) : /* @__PURE__ */ m.jsxs("div", { className: "wf-validation ok", children: [
    /* @__PURE__ */ m.jsx(Io, { size: 14 }),
    " No validation errors"
  ] });
}
function Jp(e) {
  return `${qs(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function vt(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function ti(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
export {
  t0 as register
};
